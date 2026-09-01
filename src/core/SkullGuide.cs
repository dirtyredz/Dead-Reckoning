using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Chicken.UI;
using Chicken.Utilities;
using Director.Core;
using Director.Nodes;
using UnityEngine;

namespace DeadReckoning
{
    /// <summary>
    /// Spawns the skull soul blob (index 0) and steers it. Two behaviours:
    /// <list type="bullet">
    /// <item><b>Tracking</b> — when an NPC is picked and we have a live fix on them, the skull leads:
    /// it sits on the line between you and them, pointing the way.</item>
    /// <item><b>Idle</b> — no target, or the target can't be located (off-room / not an NPC we can
    /// resolve): the skull just stays by you and lazily floats and wanders. This idle-vs-lead
    /// behaviour is itself the "am I tracking?" feedback — no label needed.</item>
    /// </list>
    /// Soul blobs float/bob natively and write nothing to the save. Their own behaviour flees the
    /// player, so we disable it, drive <c>Mover.Move</c>, and turn off <c>ForceOnGround</c> (else the
    /// mover snaps to terrain and the skull drops into rivers).
    /// </summary>
    internal sealed class SkullGuide : MonoBehaviour
    {
        internal static SkullGuide Instance; // so external UI (relationship Track button) can reach us

        private CritterView active;
        private List<ItemAsset> soulblobs;
        private GridSelector selector;

        private NpcConfigAsset tracked;          // NPC target
        private List<RoomAsset> trackedRooms;    // place/house target (a location can span rooms)
        private RoomAsset pinRoom;               // free-pin: the room + exact in-room spot
        private Vector3 pinRoomPos;
        private bool hasPin;
        private Transform trackedNode;           // quest gather/mine node: the live in-scene harvest/ore node
        private string trackedName;
        private Action<PickNpcListWidget> npcClickedHandler;

        private StartQuestNode trackedQuestNode;  // quest target: steer to its current objective's last NPC
        private QuestPersistence trackedQuestData;
        private float nextQuestRefresh;

        private JobPersistence trackedJobData;    // job target: steer to its hand-in NPC
        private float nextJobRefresh;

        private readonly TrackHud hud = new TrackHud();
        private readonly MapPin mapPin = new MapPin();
        private readonly PathGuide pathGuide = new PathGuide();

        private PickNpcScreen pickerScreen;
        private bool pickerBlocking; // blocks gameplay input (camera zoom) while the picker is open
        private static bool pickerProbed; // one-shot: dump a picker card's hierarchy to find the native selection frame
        private static bool mapProbed;    // one-shot: dump a tracked map house marker's hierarchy
        private static bool npcMarkerProbed; // one-shot: dump a tracked map NPC marker's hierarchy
        private const string PickerBlockId = "DeadReckoningNpcPicker";

        private const float MaxSpeed = 14f;        // floor; the real cap scales with player speed
        private Vector3 lastPlayerPos;
        private bool hasLastPlayer;
        private float playerSpeed;
        private Vector3 idleVel;        // persistent velocity for the idle spring-damper (lag + rebound)
        private const float TrackEyeHeight = 0.5f; // body-center height the tracking sightline runs at
        private float skullVisualLift = -1f;       // how far the visible skull floats above its steer-point

        private const float SkullRadius = 0.3f;    // for wall collision
        private const float Skin = 0.05f;
        private static int envMask = -1;           // obstacle layers (walls), NOT ground/water/bridge
        private static int groundMask = -1;        // ground/terrain/bridge deck the skull hovers above
        private float nextProbe;
        private float nextDebugLog;

        private Vector3? cachedRoute; // door-to-head-toward for an off-room target
        private float nextRouteAt;

        private bool wantActive;      // user intends the skull to be out; survives scene changes
        private float nextSpawnAttempt;

        private void Awake()
        {
            Instance = this;
            hud.OnClose = ClearTarget; // the ✕ next to the seeking overlay stops seeking
        }

        // --- External tracking API (used by the Relationships Track button). Mirrors the picker path
        // without touching it, so the working F8 flow is unchanged. ---
        internal bool IsTracked(NpcConfigAsset cfg) => cfg != null && tracked == cfg;

        internal void SetTracked(NpcConfigAsset cfg)
        {
            tracked = cfg;
            trackedRooms = null; hasPin = false; // single target
            trackedQuestNode = null; trackedQuestData = null; trackedJobData = null; trackedNode = null;
            trackedName = cfg != null ? AddressableLibrary<NpcLibrary>.Instance.GetNpcName(cfg, checkIsNameRevealed: false) : null;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking: {trackedName ?? "<none>"}");
            if (cfg != null) EnsureSkull();
        }

        internal void SetTrackedRooms(List<RoomAsset> rooms, string name)
        {
            trackedRooms = rooms != null ? rooms.Where(r => r != null).ToList() : null;
            tracked = null; hasPin = false; // single target
            trackedQuestNode = null; trackedQuestData = null; trackedJobData = null; trackedNode = null;
            trackedName = name;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking place: {name ?? "<none>"}");
            if (trackedRooms != null && trackedRooms.Count > 0) EnsureSkull();
        }

        private void SetPin(RoomAsset room, Vector3 roomPos, string name)
        {
            trackedRooms = new List<RoomAsset> { room }; // reuse room routing when out of the room
            pinRoom = room; pinRoomPos = roomPos; hasPin = true;
            tracked = null;
            trackedQuestNode = null; trackedQuestData = null; trackedJobData = null; trackedNode = null;
            trackedName = name;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking pin: {name}");
            EnsureSkull();
        }

        /// <summary>Tracking something makes the skull appear if it isn't already out — there's no manual
        /// summon key any more. The Update loop keeps it (re)spawned while <c>wantActive</c> holds.</summary>
        private void EnsureSkull()
        {
            wantActive = true;
            if (active == null && Time.time >= nextSpawnAttempt)
            {
                nextSpawnAttempt = Time.time + 0.75f;
                Spawn();
            }
        }

        /// <summary>World position of a free pin when we're in its room (relative to the player).</summary>
        private Vector3? PinWorld()
        {
            try
            {
                IEntityAsset playerEntity = AssetLibrary.Instance.PlayerEntity;
                Vector3 playerRoomPos = GamePersistence.Instance.EntityCharacters.FindOrCreate(playerEntity).RoomPosition;
                return PlayerPos() + (pinRoomPos - playerRoomPos);
            }
            catch { return null; }
        }

        private bool HasTarget() => tracked != null || (trackedRooms != null && trackedRooms.Count > 0) || trackedNode != null;

        internal void ToggleTrack(NpcConfigAsset cfg)
        {
            if (IsTracked(cfg)) ClearTarget();
            else SetTracked(cfg);
        }

        // --- Quest tracking (used by the Quest Log Track button) ----------------------------------

        internal bool IsQuestTracked(QuestPersistence data) =>
            trackedQuestData != null && data != null && trackedQuestData.Guid == data.Guid;

        internal void ToggleQuest(StartQuestNode node, QuestPersistence data)
        {
            if (IsQuestTracked(data)) ClearTarget();
            else TrackQuest(node, data);
        }

        internal void TrackQuest(StartQuestNode node, QuestPersistence data)
        {
            trackedQuestNode = node;
            trackedQuestData = data;
            tracked = null; trackedRooms = null; hasPin = false; trackedNode = null; trackedJobData = null;
            trackedName = node != null ? node.GetQuestTitle() : null;
            cachedRoute = null; nextRouteAt = 0f; nextQuestRefresh = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking quest: {trackedName ?? "<none>"}");
            if (DeadReckoningPlugin.VerboseLogging.Value) DumpQuest();
            RefreshQuestTarget();
            EnsureSkull();
        }

        // --- Job tracking (used by the Quest Log's job entries) -----------------------------------

        internal bool IsJobTracked(JobPersistence data) =>
            trackedJobData != null && data != null && trackedJobData.Guid == data.Guid;

        internal void ToggleJob(JobPersistence data)
        {
            if (IsJobTracked(data)) ClearTarget();
            else TrackJob(data);
        }

        internal void TrackJob(JobPersistence data)
        {
            trackedJobData = data;
            trackedQuestNode = null; trackedQuestData = null;
            tracked = null; trackedRooms = null; hasPin = false; trackedNode = null;
            trackedName = JobTitle(data);
            cachedRoute = null; nextRouteAt = 0f; nextJobRefresh = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking job: {trackedName ?? "<none>"}");
            RefreshJobTarget();
            EnsureSkull();
        }

        /// <summary>"Job for <npc>", matching the Quest Log's own job title (minus status prefix).</summary>
        private static string JobTitle(JobPersistence data)
        {
            try
            {
                return LocalizationLibrary.Translate("journal-quest-screen-job-title-prefix") + " " +
                       AddressableLibrary<NpcLibrary>.Instance.GetNpcName(data.SubjectNpcAssetRef.Asset, checkIsNameRevealed: false);
            }
            catch { return "Job"; }
        }

        /// <summary>A job steers to its hand-in NPC. Re-resolved on a throttle because some job types
        /// switch the hand-in NPC as the job progresses, and completing (or missing) the job must
        /// dismiss the skull rather than leave it seeking forever.</summary>
        private void RefreshJobTarget()
        {
            if (trackedJobData == null) return;
            if (Time.time < nextJobRefresh) return;
            nextJobRefresh = Time.time + 0.5f;

            try
            {
                if (trackedJobData.IsCompleted || trackedJobData.IsPastDeadline)
                {
                    DeadReckoningPlugin.Log.LogInfo($"Tracked job {(trackedJobData.IsCompleted ? "completed" : "expired")} — stopping: {trackedName ?? "<none>"}");
                    ClearTarget();
                    return;
                }

                NpcConfigAsset cfg = trackedJobData.CompletionNpcConfigAsset;
                if (tracked != cfg)
                {
                    tracked = cfg; trackedRooms = null; hasPin = false; trackedNode = null;
                    cachedRoute = null; nextRouteAt = 0f;
                }
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Job target refresh failed: {e.Message}"); }
        }

        /// <summary>Objectives of the tracked quest, in order (node + its saved state).</summary>
        private static IEnumerable<KeyValuePair<QuestObjectiveNode, QuestObjectivePersistence>> QuestObjectives(StartQuestNode start)
        {
            if (start == null) yield break;
            foreach (BaseDirectorNode child in start.ChildNodes)
            {
                if (child is QuestObjectiveNode qon)
                {
                    QuestObjectivePersistence p = GamePersistence.Instance.QuestObjectives.FindOrCreate(qon.SerializedGuid);
                    yield return new KeyValuePair<QuestObjectiveNode, QuestObjectivePersistence>(qon, p);
                }
            }
        }

        /// <summary>Point the skull at the tracked quest's current objective — its last still-required NPC
        /// (the "last task"). Drives the existing NPC steering by setting <c>tracked</c>. Idles if the
        /// current objective's task isn't a locatable NPC.</summary>
        private void RefreshQuestTarget()
        {
            if (trackedQuestNode == null) return;
            if (Time.time < nextQuestRefresh) return;
            nextQuestRefresh = Time.time + 0.5f;

            // Quest finished while we were seeking it → stop and dismiss the skull. Otherwise no
            // objective stays InProgress, RefreshQuestTarget resolves no target, and the skull is
            // left idle-wandering with "Seeking: <quest>" still on the HUD forever.
            if (trackedQuestData != null && trackedQuestData.IsCompleted)
            {
                DeadReckoningPlugin.Log.LogInfo($"Tracked quest completed — stopping: {trackedName ?? "<none>"}");
                ClearTarget();
                return;
            }

            try
            {
                NpcConfigAsset targetCfg = null;
                List<RoomAsset> targetRooms = null;
                QuestObjectiveNode activeNode = null;
                foreach (KeyValuePair<QuestObjectiveNode, QuestObjectivePersistence> kv in QuestObjectives(trackedQuestNode))
                {
                    QuestObjectiveNode node = kv.Key;
                    QuestObjectivePersistence p = kv.Value;
                    if (p == null || p.Status != QuestObjectiveStatus.InProgress) continue;
                    activeNode = node; // the objective we're steering for (used for gather/mine node lookup)

                    // a) Last still-required NPC of this objective (RequiredNpcList minus DoneNpcList).
                    for (int i = p.RequiredNpcList.Count - 1; i >= 0; i--)
                    {
                        EntityAsset entity = p.RequiredNpcList[i].Asset;
                        if (entity == null) continue;
                        if (p.DoneNpcList.Any(d => d.Asset == entity)) continue;
                        if (AddressableLibrary<NpcLibrary>.Instance.TryFind(entity, out NpcConfigAsset cfg) && cfg != null)
                        {
                            targetCfg = cfg;
                            break;
                        }
                    }

                    // b) Most objectives carry no NPC requirement — the target (vendor / recipient /
                    // location) is the gold-coloured (#FCEBAE) name in the visible title, e.g. "Deliver
                    // Red Wine to <gold>Orlock</gold>" or "Go to the <gold>Town Hall</gold>". The gold
                    // token is the game's own curated target, so an NPC it names is authoritative — try
                    // it first.
                    string goldName = null;
                    if (targetCfg == null)
                    {
                        goldName = LastGoldToken(node.GetObjectiveTitle());
                        if (!string.IsNullOrEmpty(goldName)) targetCfg = FindNpcByName(goldName);
                    }

                    // c) The recipient can be named only in the objective's INTERNAL dev-name, not the
                    // NPC list and not the visible title — e.g. dev-name "Bring a copper bar to Yabbis'
                    // pond in Moonlit Pines", shown as just "the little pond". Fall to this only when the
                    // gold token wasn't itself an NPC, so a real gold-token character always wins over a
                    // free-form dev-name mention — but a named recipient still beats the vague region (d).
                    if (targetCfg == null)
                        targetCfg = FindNpcMentioned(node.ObjectiveName);

                    // d) Still no character → treat the gold token as a place and route to its rooms.
                    if (targetCfg == null && !string.IsNullOrEmpty(goldName))
                        targetRooms = ResolveQuestLocation(goldName);

                    if (targetCfg != null || targetRooms != null) break;
                }

                // c) Gather/mine objectives ("Mine Copper Ore in the Cave of Echoes") name an item, not
                // a character. Once you're inside the target region, point at the actual node in the
                // scene — the vein/bush — instead of idling at the region's threshold. The node only
                // exists in the loaded room, so we only scan once we're in the region the gold token
                // resolved to (or unconstrained when the objective named no region). Reuse the current
                // node while it's still valid to avoid flip-flopping between two equidistant veins.
                Transform targetNode = null;
                if (targetCfg == null && activeNode != null && InTargetRegion(targetRooms))
                {
                    ItemAsset item = QuestNodeLocator.ObjectiveItem(activeNode);
                    if (item != null)
                        targetNode = (trackedNode != null && QuestNodeLocator.StillYields(trackedNode, item))
                            ? trackedNode
                            : QuestNodeLocator.NearestNode(item, PlayerPos());
                }

                if (targetCfg != null)
                {
                    if (tracked != targetCfg)
                    {
                        tracked = targetCfg; trackedRooms = null; hasPin = false; trackedNode = null;
                        cachedRoute = null; nextRouteAt = 0f;
                    }
                }
                else if (targetNode != null)
                {
                    if (trackedNode != targetNode || tracked != null || trackedRooms != null)
                    {
                        trackedNode = targetNode; tracked = null; trackedRooms = null; hasPin = false;
                        cachedRoute = null; nextRouteAt = 0f;
                    }
                }
                else if (targetRooms != null)
                {
                    if (tracked != null || trackedNode != null || !SameRoomSet(trackedRooms, targetRooms))
                    {
                        trackedRooms = targetRooms; tracked = null; hasPin = false; trackedNode = null;
                        cachedRoute = null; nextRouteAt = 0f;
                    }
                }
                else if (tracked != null || trackedRooms != null || trackedNode != null)
                {
                    tracked = null; trackedRooms = null; hasPin = false; trackedNode = null; cachedRoute = null; // idle
                }
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Quest target refresh failed: {e.Message}"); }
        }

        /// <summary>True when the player is in the region the objective's gold token resolved to, so a
        /// gather/mine node scan is worth running (the node only exists in the loaded room). An
        /// objective that named no region (null) is scanned wherever the player is.</summary>
        private bool InTargetRegion(List<RoomAsset> region)
        {
            if (region == null) return true;
            if (!AddressableLibrary<NavigationLibrary>.Exists) return false;
            RoomAsset cur = GamePersistence.CurrentRoomAsset;
            return cur != null && region.Contains(cur);
        }

        /// <summary>The inner text of the LAST gold-coloured (#FCEBAE) token in an objective title — the
        /// game's colour for a character/location reference. That's the "to X"/"from X"/"Visit X" target.</summary>
        private static string LastGoldToken(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            int idx = title.LastIndexOf("<color=#FCEBAE", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int gt = title.IndexOf('>', idx);
            if (gt < 0) return null;
            int close = title.IndexOf("</color>", gt, StringComparison.OrdinalIgnoreCase);
            if (close < 0) return null;
            return StripTags(title.Substring(gt + 1, close - gt - 1)).Trim();
        }

        private static string StripTags(string s)
        {
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>An NPC whose display name matches (a game character referenced by name in a quest).</summary>
        private static NpcConfigAsset FindNpcByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                NpcLibrary lib = AddressableLibrary<NpcLibrary>.Instance;
                foreach (NpcConfigAsset cfg in NpcLibrary.NpcConfigs)
                {
                    string n = lib.GetNpcName(cfg, checkIsNameRevealed: false);
                    if (!string.IsNullOrEmpty(n) && string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return cfg;
                }
            }
            catch { }
            return null;
        }

        // Word-splitters for the objective dev-name scan: whitespace + punctuation, incl. both
        // apostrophes so a possessive ("Yabbis'") yields the bare name token.
        private static readonly char[] NameWordSeps =
            { ' ', '\t', '\n', '\r', '\'', '’', '.', ',', '!', '?', '"', '(', ')', '-', ':', ';' };

        /// <summary>An NPC whose (possibly multi-word) display name appears as a whole run of words in
        /// <paramref name="text"/> (the objective's internal dev-name), or null. This is how a "deliver
        /// to X" objective whose visible title hides the recipient — e.g. dev-name "Bring a copper bar
        /// to Yabbis' pond", shown only as "the little pond" — still resolves to Yabbis. Matches the NPC
        /// whose name occurs LATEST, so the "to &lt;recipient&gt;" at the end wins over an earlier
        /// mention. Whole-word runs (not substrings) so "Ed" can't match "Echoes" and multi-word names
        /// like "Old Man Jenkins" are reachable.</summary>
        private static NpcConfigAsset FindNpcMentioned(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                // Ordered, normalized word list of the dev-name.
                var words = new List<string>();
                foreach (string tok in text.Split(NameWordSeps, StringSplitOptions.RemoveEmptyEntries))
                {
                    string w = NormalizeName(tok);
                    if (w.Length > 0) words.Add(w);
                }
                if (words.Count == 0) return null;

                NpcLibrary lib = AddressableLibrary<NpcLibrary>.Instance;
                NpcConfigAsset best = null;
                int bestPos = -1;
                foreach (NpcConfigAsset cfg in NpcLibrary.NpcConfigs)
                {
                    string n = lib.GetNpcName(cfg, checkIsNameRevealed: false);
                    if (string.IsNullOrEmpty(n)) continue;
                    string[] nameWords = NormalizeName(n).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // Skip a single ultra-short name (e.g. a 1-letter alias) — too generic to trust.
                    if (nameWords.Length == 0 || (nameWords.Length == 1 && nameWords[0].Length < 2)) continue;
                    int pos = LastRunIndex(words, nameWords);
                    if (pos > bestPos) { bestPos = pos; best = cfg; }
                }
                return best;
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Objective NPC-name scan failed: {e.Message}"); }
            return null;
        }

        /// <summary>The start index of the LAST contiguous occurrence of <paramref name="needle"/> in
        /// <paramref name="hay"/> (whole-word run match), or -1.</summary>
        private static int LastRunIndex(List<string> hay, string[] needle)
        {
            for (int start = hay.Count - needle.Length; start >= 0; start--)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                    if (hay[start + j] != needle[j]) { ok = false; break; }
                if (ok) return start;
            }
            return -1;
        }

        // Cache the last quest-location lookup so the 0.5s refresh doesn't re-scan every tick — the
        // rooms behind a place name never change, only the name we're resolving does.
        private string lastQuestLocName;
        private List<RoomAsset> lastQuestLocRooms;

        /// <summary>Resolve a location name from a quest objective (e.g. "Town Hall") to the rooms that
        /// make up that place, so the skull routes there like a tracked map house. Prefers the named
        /// map location marker (matches the map exactly); falls back to any room whose own name matches.
        /// Null when the name isn't a place we can locate. Cached by name.</summary>
        private List<RoomAsset> ResolveQuestLocation(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name == lastQuestLocName && lastQuestLocRooms != null) return lastQuestLocRooms;
            List<RoomAsset> rooms = FindLocationRooms(name);
            if (rooms != null) { lastQuestLocName = name; lastQuestLocRooms = rooms; } // only cache hits (retry until the map/nav library is ready)
            return rooms;
        }

        /// <summary>The rooms of the place whose name matches <paramref name="name"/>. Two passes over
        /// the map's location markers (exact match wins over a looser contains match), then a room-name
        /// fallback for places that aren't a labelled map marker.</summary>
        private static List<RoomAsset> FindLocationRooms(string name)
        {
            string want = NormalizeName(name);
            if (want.Length == 0) return null;
            try
            {
                MapLocationMarker exact = null, loose = null;
                foreach (MapLocationMarker m in UnityEngine.Object.FindObjectsByType<MapLocationMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (m == null || m.RoomAssets == null || m.RoomAssets.Count == 0) continue;
                    foreach (string cand in MarkerNames(m))
                    {
                        string got = NormalizeName(cand);
                        if (got.Length == 0) continue;
                        if (got == want) { exact = m; break; }
                        if (loose == null && (got.Contains(want) || want.Contains(got))) loose = m;
                    }
                    if (exact != null) break;
                }
                MapLocationMarker hit = exact ?? loose;
                if (hit != null) return hit.RoomAssets.Where(r => r != null).ToList();

                // Fallback: a room whose own name matches (a place with no labelled map marker).
                if (AddressableLibrary<NavigationLibrary>.Exists)
                {
                    var rooms = new List<RoomAsset>();
                    foreach (NavigationLibrary.RoomData rd in AddressableLibrary<NavigationLibrary>.Instance.Rooms)
                    {
                        RoomAsset ra = rd != null ? rd.RoomAsset : null;
                        if (ra == null) continue;
                        string rn;
                        try { rn = ra.GetRoomName(false); } catch { rn = null; }
                        if (NormalizeName(rn) == want) rooms.Add(ra);
                    }
                    if (rooms.Count > 0) return rooms;
                }
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Quest location lookup failed: {e.Message}"); }
            return null;
        }

        /// <summary>Candidate display names for a map location marker: its shown name, its raw localized
        /// name (survives the "???" that GetLocationName returns for unvisited rooms), and each room's
        /// own name.</summary>
        private static IEnumerable<string> MarkerNames(MapLocationMarker m)
        {
            string shown = null;
            try { shown = m.GetLocationName(); } catch { }
            if (!string.IsNullOrEmpty(shown)) yield return shown;
            string raw = null;
            try { raw = m.LocationName != null ? m.LocationName.GetTranslation() : null; } catch { }
            if (!string.IsNullOrEmpty(raw)) yield return raw;
            foreach (RoomAsset ra in m.RoomAssets)
            {
                if (ra == null) continue;
                string rn = null;
                try { rn = ra.GetRoomName(false); } catch { }
                if (!string.IsNullOrEmpty(rn)) yield return rn;
            }
        }

        /// <summary>Lower-cased, tag-stripped, article-trimmed name for lenient place matching.</summary>
        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = StripTags(s).Trim().ToLowerInvariant();
            if (s.StartsWith("the ")) s = s.Substring(4);
            return s.Trim();
        }

        private static bool SameRoomSet(List<RoomAsset> a, List<RoomAsset> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!b.Contains(a[i])) return false;
            return true;
        }

        /// <summary>The on-screen objectives list for the tracked quest — completed/failed dimmed and
        /// struck through, mirroring the Quest Log panel.</summary>
        // Quest text is denser than a single "Tracking: X" line, so cap its size a bit below the config.
        private static float QuestHudFontSize() => Mathf.Min(DeadReckoningPlugin.HudFontSize.Value, 22f);

        /// <summary>The tracked quest's objectives list, echoing the Quest Log panel: gold title, a purple
        /// diamond for active objectives and a magenta check for completed (dimmed + struck through).
        /// TMP has no closing &lt;/alpha&gt;, so alpha is reset with &lt;alpha=#FF&gt; instead.</summary>
        private string BuildQuestHud()
        {
            var sb = new StringBuilder();
            sb.Append("<b><color=#F5D98A>").Append(trackedName ?? "Quest").Append("</color></b>");
            try
            {
                foreach (KeyValuePair<QuestObjectiveNode, QuestObjectivePersistence> kv in QuestObjectives(trackedQuestNode))
                {
                    QuestObjectiveNode node = kv.Key;
                    QuestObjectivePersistence p = kv.Value;
                    if (p == null || p.Status == QuestObjectiveStatus.NotStarted) continue;

                    bool done = p.Status == QuestObjectiveStatus.Completed || p.Status == QuestObjectiveStatus.Failed;
                    string title = node.GetObjectiveTitle();
                    string counter = node.ShowCounterValue ? $"  {node.CurrentCounterValue}/{node.RequiredCounterValue}" : "";
                    sb.Append('\n');
                    if (done)
                        sb.Append("<color=#E36FD0>✓</color> <alpha=#77><s>").Append(title).Append(counter).Append("</s><alpha=#FF>");
                    else
                        sb.Append("<color=#9E77E6>◆</color> ").Append(title).Append(counter);
                }
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>One-shot (VerboseLogging): dump the tracked quest's objectives + their NPC requirements
        /// so we can see how a target like a vendor is (or isn't) referenced.</summary>
        private void DumpQuest()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"DR-QUESTPROBE '{trackedName}':");
                foreach (KeyValuePair<QuestObjectiveNode, QuestObjectivePersistence> kv in QuestObjectives(trackedQuestNode))
                {
                    QuestObjectiveNode node = kv.Key;
                    QuestObjectivePersistence p = kv.Value;
                    sb.AppendLine($"  obj '{node.ObjectiveName}' status={p?.Status} counter={node.ShowCounterValue}({node.CurrentCounterValue}/{node.RequiredCounterValue}) title='{node.GetObjectiveTitle()}'");
                    if (p != null)
                    {
                        foreach (var req in p.RequiredNpcList)
                        {
                            EntityAsset e = req.Asset;
                            bool npc = e != null && AddressableLibrary<NpcLibrary>.Instance.TryFind(e, out _);
                            sb.AppendLine($"      requiredNpc: {(e != null ? e.name : "null")} resolvesToNpc={npc} done={p.DoneNpcList.Any(d => d.Asset == e)}");
                        }
                    }
                    foreach (BaseDirectorNode child in node.ChildNodes)
                        sb.AppendLine($"      child: {child.GetType().Name}");
                }
                DeadReckoningPlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Quest probe failed: {e.Message}"); }
        }

        private float nextRelCheck;

        private void Update()
        {
            DeadReckoningPlugin.TryPatchFarSight(); // one-time; Far Sight loads after us so patch it here

            if (!DeadReckoningPlugin.Enabled.Value)
            {
                if (active != null) Despawn();
                WorldScrollBlock.PickerOpen = false;
                WorldScrollBlock.RelationshipOpen = false;
                hud.Hide();
                return;
            }

            // Keep the camera from zooming on scroll while our picker / the relationship panel is up.
            WorldScrollBlock.PickerOpen = PickerOpen();
            if (Time.time >= nextRelCheck)
            {
                nextRelCheck = Time.time + 0.25f;
                WorldScrollBlock.RelationshipOpen = RelationshipPanelOpen();
            }

            try
            {
                if (DeadReckoningPlugin.PickNpcKey.Value.IsDown())
                {
                    if (PickerOpen()) ClosePicker();
                    else OpenNpcPicker();
                }

                // Track from the map by DOUBLE-CLICKING a place/NPC/spot.
                if (MapScreenOpen() && DoubleClickedOnMap())
                    TryMapTrack();

                // Let the cancel/back input (Esc / controller B) close the picker too.
                if (PickerOpen() && InputUtility.GetCancelInputDown())
                    ClosePicker();
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Input handling failed: {e}");
            }

            // While the NPC picker is up, gameplay input is blocked so the scroll wheel scrolls the
            // list instead of also zooming the camera. Drop the block once it closes (pick or cancel).
            if (pickerBlocking && (pickerScreen == null || !pickerScreen.gameObject.activeInHierarchy))
                SetPickerBlock(false);

            // Auto-(re)spawn: the soul blob is a room object destroyed on scene changes (active goes
            // Unity-null), so re-create it in the new scene while the user still wants it out.
            if (wantActive && active == null && Time.time >= nextSpawnAttempt)
            {
                nextSpawnAttempt = Time.time + 0.75f;
                Spawn();
            }

            if (trackedQuestNode != null) RefreshQuestTarget();
            if (trackedJobData != null) RefreshJobTarget();
            if (active != null) { Steer(); ApplyFlameColor(); }

            UpdateHud();
            UpdateMapPin();
            UpdateTrackedHighlights();
        }

        /// <summary>Border the tracked target wherever it's shown: map house/NPC badges, and the picker.</summary>
        private void UpdateTrackedHighlights()
        {
            try
            {
                if (MapScreenOpen())
                {
                    // If we're seeking an NPC, find the room they're in right now. When that room belongs
                    // to a house/place marker, we light up the HOUSE (they're indoors); when they're out
                    // in the open their own NPC marker lights up instead. Recomputed each frame, so the
                    // map follows them live as they move between the two.
                    RoomAsset npcRoom = TrackedNpcRoom();

                    foreach (MapLocationMarkerListWidget w in UnityEngine.Object.FindObjectsByType<MapLocationMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        MapLocationMarker m = ((ListWidget<MapLocationMarker>)w).Data;
                        bool on = m != null && m.RoomAssets != null &&
                            ((!hasPin && trackedRooms != null && m.RoomAssets.Any(r => trackedRooms.Contains(r))) ||
                             (npcRoom != null && m.RoomAssets.Contains(npcRoom)));
                        if (on && !mapProbed && DeadReckoningPlugin.VerboseLogging.Value)
                        {
                            mapProbed = true;
                            try { DumpWidget(w, "DR-HOUSEPROBE map house marker hierarchy:"); }
                            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"House probe failed: {e.Message}"); }
                        }
                        MapMarkerTint.Set(w, on);
                    }
                    foreach (MapNpcMarkerListWidget w in UnityEngine.Object.FindObjectsByType<MapNpcMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        bool onNpc = SameNpc(((ListWidget<NpcConfigAsset>)w).Data, tracked);
                        if (onNpc && !npcMarkerProbed && DeadReckoningPlugin.VerboseLogging.Value)
                        {
                            npcMarkerProbed = true;
                            try { DumpWidget(w, "DR-NPCMARKERPROBE map npc marker hierarchy:"); }
                            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"NPC marker probe failed: {e.Message}"); }
                        }
                        MapMarkerTint.Set(w, onNpc, circle: true);
                    }
                }

                if (PickerOpen())
                {
                    foreach (PickNpcListWidget w in UnityEngine.Object.FindObjectsByType<PickNpcListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        if (!pickerProbed && DeadReckoningPlugin.VerboseLogging.Value)
                        {
                            pickerProbed = true;
                            try { DumpPickerCard(w); } catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Picker probe failed: {e.Message}"); }
                        }
                        PickerCardHighlight.Set(w, SameNpc(w.Data, tracked));
                    }
                }
            }
            catch { }
        }

        /// <summary>Match NPCs by identity, not reference — a quest-resolved NpcConfigAsset can be a
        /// different instance than the picker/map widget's, so reference == would wrongly miss it.</summary>
        private static bool SameNpc(NpcConfigAsset a, NpcConfigAsset b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            try { return a.Entity != null && b.Entity != null && a.Entity.SerializedGuid == b.Entity.SerializedGuid; }
            catch { return false; }
        }

        /// <summary>The room the currently-sought NPC is in right now (null if we're not seeking an NPC).
        /// Used to light up their house on the map when they're indoors.</summary>
        private RoomAsset TrackedNpcRoom()
        {
            if (tracked == null || tracked.Entity == null || hasPin || trackedRooms != null) return null;
            try { return GamePersistence.Instance.EntityCharacters.FindOrCreate(tracked.Entity).Room; }
            catch { return null; }
        }

        /// <summary>Are we currently seeking (any of) these rooms as a place — i.e. is this the house/place
        /// marker that's lit up right now?</summary>
        private bool IsTrackingRooms(System.Collections.Generic.IReadOnlyList<RoomAsset> rooms)
        {
            if (hasPin || trackedRooms == null || rooms == null) return false;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && trackedRooms.Contains(rooms[i])) return true;
            return false;
        }

        private void UpdateMapPin()
        {
            if (!hasPin || !MapScreenOpen())
            {
                mapPin.Hide();
                return;
            }
            try
            {
                MapWidget mw = UIScreen<MapScreen>.Instance.ActiveMapWidget;
                if (mw == null || mw.RoomMarkers == null || mw.RoomMarkers.Length == 0)
                {
                    mapPin.Hide();
                    return;
                }
                // Parent the pin to the constant-size marker OVERLAY (where the house/location markers
                // live), NOT the zoom-scaled map content that RoomMarkers sit in — otherwise the pin
                // shrinks with zoom-out. GetUIPositionFromRoomPosition returns a world position, so it
                // still lands in the right spot regardless of parent.
                Transform parent = MapMarkerOverlay()
                    ?? (mw.RoomMarkers[0].RectTransform != null ? mw.RoomMarkers[0].RectTransform.parent : mw.transform);
                mapPin.Refresh(mw, pinRoom, pinRoomPos, parent);
            }
            catch { mapPin.Hide(); }
        }

        /// <summary>The constant-size overlay the game's location/NPC markers live in (siblings of our
        /// pin), so the pin doesn't scale with the map zoom. Null if no such marker is present.</summary>
        private static Transform MapMarkerOverlay()
        {
            var loc = UnityEngine.Object.FindObjectsByType<MapLocationMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (loc.Length > 0 && loc[0].transform.parent != null) return loc[0].transform.parent;
            var npc = UnityEngine.Object.FindObjectsByType<MapNpcMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (npc.Length > 0 && npc[0].transform.parent != null) return npc[0].transform.parent;
            return null;
        }

        private void UpdateHud()
        {
            if (!DeadReckoningPlugin.ShowHud.Value || active == null || MenuOpen())
            {
                hud.Hide();
                return;
            }
            if (trackedQuestNode != null) { hud.Set(BuildQuestHud(), QuestHudFontSize()); return; }
            string line = HasTarget() ? $"Seeking: <color=#F5D98A>{trackedName}</color>" : "Seeking: no one yet";
            hud.Set(line, QuestHudFontSize());
        }

        /// <summary>True when a menu (map, relationships, pause, chest, cutscene…) is up, so the tracking
        /// text hides instead of overlaying it. Same signal ChestLabels uses:
        /// <c>PlayerCursorInteractionScreen</c> is showing exactly when the player can act in the world,
        /// and is gone for every menu/chest/pause/cutscene. Fails to "no menu" if the state can't be read.</summary>
        private static bool MenuOpen()
        {
            try
            {
                var cursor = UIScreen<PlayerCursorInteractionScreen>.Instance;
                return cursor == null || !cursor.IsShowing;
            }
            catch { return false; }
        }

        // ---- Spawn --------------------------------------------------------------------------

        private bool ResolveSources()
        {
            if (soulblobs != null && soulblobs.Count > 0 && selector != null) return true;

            SoulblobSpawner spawner = UnityEngine.Object.FindAnyObjectByType<SoulblobSpawner>();
            if (spawner != null)
            {
                soulblobs = spawner.SoulBlobs != null ? spawner.SoulBlobs.Where(x => x != null).ToList() : null;
                selector = spawner.Selector;
            }
            if (selector == null)
                selector = UnityEngine.Object.FindAnyObjectByType<GridSelector>();

            if (soulblobs == null || soulblobs.Count == 0 || selector == null)
            {
                DeadReckoningPlugin.Log.LogWarning(
                    "No SoulblobSpawner/soul blobs found in this room. Go to an area where soul blobs appear, then try again.");
                return false;
            }
            return true;
        }

        private void Spawn()
        {
            if (!AssetLibrary.Exists || !MonoBehaviourSingleton<PlayerView>.Exists || !MonoBehaviourSingleton<RoomContainers>.Exists)
            {
                DeadReckoningPlugin.Log.LogWarning("Not in an active save yet — load in, then try again.");
                return;
            }
            if (!ResolveSources()) return;

            int index = Mathf.Clamp(DeadReckoningPlugin.SoulblobIndex.Value, 0, soulblobs.Count - 1);
            ItemAsset blob = soulblobs[index];
            Vector3 fwd = PlayerForwardIsh();
            Vector3 start = PlayerPos() + fwd * DeadReckoningPlugin.StandoffDistance.Value + Vector3.up * DeadReckoningPlugin.HoverHeight.Value;

            CritterView view;
            try
            {
                view = CritterView.SpawnCritter(start, blob, selector, startRandomState: false);
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"SpawnCritter threw for '{blob.name}': {e}");
                return;
            }

            if (view.CritterBehaviour != null)
                view.CritterBehaviour.enabled = false; // its own behaviour flees the player; take the wheel

            // The mover snaps to terrain height by default (ForceOnGround); the game's own fly states
            // turn it off. Since we disabled the behaviour, do it ourselves — otherwise the skull
            // hugs the ground and drops into rivers/pits instead of flying at hover height.
            if (view.Mover != null)
                view.Mover.ForceOnGround = false;

            active = view;
            skullVisualLift = -1f; // re-measure the mesh's float offset for this instance
            hasLastPlayer = false; playerSpeed = 0f; idleVel = Vector3.zero;

            if (DeadReckoningPlugin.VerboseLogging.Value) { try { DumpBlob(view); } catch { } }
            try { CacheFlame(view); ApplyFlameColor(); } catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Flame recolour failed: {e.Message}"); }

            // The first steer snaps it to its spot (see justSpawned) so it appears there instead of
            // flying in from off-screen. No fade/scale — plain appear.
            justSpawned = true;

            DeadReckoningPlugin.Log.LogInfo("Skull soul blob spawned (auto-summoned by tracking). F8 to pick an NPC, F7 to clear.");
        }

        private bool justSpawned; // first steer snaps to the target spot so it doesn't fly in

        private void Despawn()
        {
            if (active == null) return;
            CritterView dying = active;
            active = null;
            try
            {
                if (dying.CritterBehaviour != null && dying.CritterBehaviour.enabled)
                    dying.HideAndDestroy();
                else if (dying.gameObject != null)
                    UnityEngine.Object.Destroy(dying.gameObject);
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogWarning($"Despawn fell back to Destroy: {e.Message}");
                if (dying != null && dying.gameObject != null) UnityEngine.Object.Destroy(dying.gameObject);
            }
        }

        private readonly List<Material> flameMats = new List<Material>();
        private readonly List<ParticleSystem> flareParticles = new List<ParticleSystem>();

        /// <summary>Cache the soul blob's flame pieces (the Fire mesh + Flare particle) — NOT the skull —
        /// using instanced materials so the ambient world soul blobs keep their own colour.</summary>
        private void CacheFlame(CritterView view)
        {
            flameMats.Clear();
            flareParticles.Clear();

            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
            {
                bool isFlame = r.name == "Fire" || r.name == "Flare" ||
                               (r.sharedMaterial != null &&
                                (r.sharedMaterial.name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 r.sharedMaterial.name.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0));
                if (!isFlame) continue;
                foreach (Material m in r.materials) // instances
                    if (m != null) flameMats.Add(m);
            }
            foreach (ParticleSystem ps in view.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) flareParticles.Add(ps);
        }

        /// <summary>Apply the configured flame colour to the cached pieces. Runs each frame so it takes
        /// effect immediately when the colour config changes and survives late material init.</summary>
        private void ApplyFlameColor()
        {
            if (!DeadReckoningPlugin.RecolorFlame.Value || (flameMats.Count == 0 && flareParticles.Count == 0)) return;
            Color color = ParseHtml(DeadReckoningPlugin.FlameColor.Value, new Color(0.54f, 0.31f, 1f));

            // The BlobFire flame is a 3-stop gradient (inner→mid→outer). Alpha stays 0 like the originals
            // (the shader reads RGB). Inner is lightened, outer darkened, for a natural flame ramp.
            Color inner = Color.Lerp(color, Color.white, 0.45f); inner.a = 0f;
            Color mid = new Color(color.r, color.g, color.b, 0f);
            Color outer = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, 0f);

            for (int i = 0; i < flameMats.Count; i++)
            {
                Material m = flameMats[i];
                if (m == null) continue;
                if (m.HasProperty("_InnerColor")) m.SetColor("_InnerColor", inner);
                if (m.HasProperty("_MidColor")) m.SetColor("_MidColor", mid);
                if (m.HasProperty("_OuterColor")) m.SetColor("_OuterColor", outer);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color * 1.5f); // flare additive tint (HDR)
            }
            for (int i = 0; i < flareParticles.Count; i++)
            {
                if (flareParticles[i] == null) continue;
                ParticleSystem.MainModule main = flareParticles[i].main;
                main.startColor = color;
            }
        }

        private static Color ParseHtml(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }

        /// <summary>One-shot (VerboseLogging): dump the soul blob's renderers/materials/particles so we can
        /// find the flame colour to recolour.</summary>
        private static void DumpBlob(CritterView view)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DR-BLOBPROBE '{view.name}':");
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
            {
                sb.AppendLine($"  Renderer '{r.name}' ({r.GetType().Name}) enabled={r.enabled}");
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    sb.AppendLine($"    mat '{m.name}' shader='{m.shader?.name}'");
                    Shader sh = m.shader;
                    if (sh != null)
                    {
                        int n = sh.GetPropertyCount();
                        for (int i = 0; i < n; i++)
                        {
                            var type = sh.GetPropertyType(i);
                            string name = sh.GetPropertyName(i);
                            string val = type == UnityEngine.Rendering.ShaderPropertyType.Color ? $"={m.GetColor(name)}"
                                       : type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range ? $"={m.GetFloat(name)}"
                                       : "";
                            sb.AppendLine($"      prop {name} ({type}){val}");
                        }
                    }
                }
            }
            foreach (ParticleSystem ps in view.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                sb.AppendLine($"  Particle '{ps.name}' startColor={main.startColor.color} mode={main.startColor.mode}");
            }
            DeadReckoningPlugin.Log.LogInfo(sb.ToString());
        }

        private void OnDisable() => Despawn();
        private void OnDestroy() { Despawn(); DetachPicker(); SetPickerBlock(false); hud.Destroy(); mapPin.Destroy(); if (Instance == this) Instance = null; }

        // ---- NPC picker (native PickNpcScreen) ----------------------------------------------

        private void OpenNpcPicker()
        {
            if (!AssetLibrary.Exists || !AddressableLibrary<NpcLibrary>.Exists)
            {
                DeadReckoningPlugin.Log.LogWarning("Not in an active save yet — can't open the NPC picker.");
                return;
            }

            List<NpcConfigAsset> roster = BuildNpcRoster();
            if (roster.Count == 0)
            {
                DeadReckoningPlugin.Log.LogWarning("No NPCs found to track.");
                return;
            }

            try
            {
                var screen = UIScreen<PickNpcScreen>.Instance;
                if (npcClickedHandler == null) npcClickedHandler = OnNpcPicked;
                screen.OnNpcClicked.RemoveListener(npcClickedHandler); // avoid stacking on repeat opens
                screen.OnNpcClicked.AddListener(npcClickedHandler);
                screen.Setup(roster);
                screen.Show("Seek who?");
                pickerScreen = screen;
                SetPickerBlock(true);
                AttachStopButton(screen);
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Opening PickNpcScreen failed: {e}");
            }
        }

        private const string StopButtonName = "DR_StopSeek";
        private static FieldInfo pickerTitleField;

        /// <summary>Add a "Stop Seeking" button at the top of the NPC picker (shown only while something is
        /// being sought). Reuses the button across opens.</summary>
        private void AttachStopButton(PickNpcScreen screen)
        {
            try
            {
                bool seeking = tracked != null || trackedRooms != null || hasPin || trackedQuestNode != null || trackedJobData != null;
                Transform existing = DRUi.FindDeep(screen.transform, StopButtonName);
                if (existing != null) { existing.gameObject.SetActive(seeking); if (!seeking) return; }
                if (!seeking) return;
                if (existing != null) return;

                if (pickerTitleField == null)
                    pickerTitleField = typeof(PickNpcScreen).GetField("titleContainer", BindingFlags.Instance | BindingFlags.NonPublic);
                var titleGo = pickerTitleField?.GetValue(screen) as GameObject;
                Transform parent = titleGo != null ? titleGo.transform.parent : screen.transform;
                int belowTitle = titleGo != null ? titleGo.transform.GetSiblingIndex() + 1 : 0;

                var color = new Color(1f, 0.5f, 0.5f);
                var go = new GameObject(StopButtonName, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.SetSiblingIndex(belowTitle); // just below the "Seek who?" title
                var le = go.GetComponent<UnityEngine.UI.LayoutElement>();
                le.preferredHeight = le.minHeight = 38f;
                var bg = go.GetComponent<UnityEngine.UI.Image>();
                bg.color = new Color(0f, 0f, 0f, 0f);
                var btn = go.GetComponent<UnityEngine.UI.Button>();
                btn.targetGraphic = bg;

                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                var trt = (RectTransform)txtGo.transform;
                trt.SetParent(go.transform, false);
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                var label = txtGo.GetComponent<TMPro.TextMeshProUGUI>();
                label.text = "Stop Seeking";
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.fontSize = 22f;
                label.color = color;
                label.raycastTarget = false;
                TMPro.TextMeshProUGUI srcFont = screen.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (srcFont != null) { if (srcFont.font != null) label.font = srcFont.font; if (srcFont.fontSharedMaterial != null) label.fontSharedMaterial = srcFont.fontSharedMaterial; }

                // X icon just left of the centred label.
                GameObject xIcon = DRIcons.BuildX(go.transform, 20f, color);
                var xrt = (RectTransform)xIcon.transform;
                xrt.anchorMin = xrt.anchorMax = new Vector2(0.5f, 0.5f);
                xrt.anchoredPosition = new Vector2(-90f, 0f);

                go.AddComponent<HoverScale>().Target = go.transform; // scale text + X together on hover
                btn.onClick.AddListener(() => { ClearTarget(); ClosePicker(); });
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Stop-seek button failed: {e.Message}"); }
        }

        private void OnNpcPicked(PickNpcListWidget widget)
        {
            NpcConfigAsset picked = widget != null ? widget.Data : null;

            // Clicking the NPC you're already seeking stops seeking (and closes the picker).
            if (picked != null && SameNpc(picked, tracked))
            {
                ClearTarget();
                DetachPicker();
                try { UIScreen<PickNpcScreen>.Instance.Hide(); } catch { }
                SetPickerBlock(false);
                return;
            }

            tracked = picked;
            trackedRooms = null; hasPin = false; // clear any place/free-pin target so the NPC actually wins
            trackedQuestNode = null; trackedQuestData = null; trackedJobData = null; trackedNode = null; // picking someone overrides quest tracking
            trackedName = tracked != null
                ? AddressableLibrary<NpcLibrary>.Instance.GetNpcName(tracked, checkIsNameRevealed: false)
                : null;
            cachedRoute = null; nextRouteAt = 0f;
            DetachPicker();
            try { UIScreen<PickNpcScreen>.Instance.Hide(); } catch { }
            SetPickerBlock(false);
            DeadReckoningPlugin.Log.LogInfo($"Now tracking: {trackedName ?? "<none>"}");
            if (tracked != null) EnsureSkull();
        }

        private void SetPickerBlock(bool on)
        {
            if (on == pickerBlocking) return;
            try
            {
                if (MonoBehaviourSingleton<PlayerView>.Exists)
                {
                    PlayerInput input = MonoBehaviourSingleton<PlayerView>.Instance.Input;
                    if (on) input.InputBlocker.Add(PickerBlockId);
                    else input.InputBlocker.Remove(PickerBlockId);
                }
            }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Picker input block toggle failed: {e.Message}"); }
            pickerBlocking = on;
        }

        private void ClearTarget()
        {
            tracked = null;
            trackedRooms = null;
            hasPin = false;
            trackedQuestNode = null; trackedQuestData = null; trackedJobData = null; trackedNode = null;
            trackedName = null;
            cachedRoute = null; nextRouteAt = 0f;
            // No target => no skull. It only exists while you're tracking something now.
            wantActive = false;
            Despawn();
            DeadReckoningPlugin.Log.LogInfo("Tracking cleared — the skull is dismissed.");
        }

        private void DetachPicker()
        {
            if (npcClickedHandler == null) return;
            try { UIScreen<PickNpcScreen>.Instance.OnNpcClicked.RemoveListener(npcClickedHandler); } catch { }
        }

        private bool PickerOpen() => pickerScreen != null && pickerScreen.gameObject.activeInHierarchy;

        /// <summary>One-shot: dump a picker card's full hierarchy — active state, Image sprite + color,
        /// TMP text — so we can find the native selection frame (the orange glow + wings) and clone it
        /// purple for the tracked card. Gated on VerboseLogging.</summary>
        private static void DumpPickerCard(PickNpcListWidget w) => DumpWidget(w, "DR-PICKERPROBE npc picker card hierarchy:");

        private static void DumpWidget(Component w, string header)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(header);
            WalkPicker(w.transform, 0, sb);
            DeadReckoningPlugin.Log.LogInfo(sb.ToString());
        }

        private static void WalkPicker(Transform t, int depth, System.Text.StringBuilder sb)
        {
            string indent = new string(' ', depth * 2);
            var parts = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (c is UnityEngine.UI.Image img)
                    tn += $"(sprite={(img.sprite != null ? img.sprite.name : "null")},color={ColorHex(img.color)},a={img.color.a:0.##})";
                else if (c is UnityEngine.UI.RawImage raw)
                    tn += $"(raw,color={ColorHex(raw.color)})";
                else if (c is TMPro.TextMeshProUGUI tmp)
                    tn += $"(text='{tmp.text}')";
                else if (c is RectTransform rtc)
                    tn += $"(size={rtc.sizeDelta})";
                else if (c is CanvasGroup cg)
                    tn += $"(alpha={cg.alpha:0.##})";
                parts.Add(tn);
            }
            sb.AppendLine($"{indent}{t.name} active={t.gameObject.activeSelf} [{string.Join(", ", parts)}]");
            for (int i = 0; i < t.childCount; i++)
                WalkPicker(t.GetChild(i), depth + 1, sb);
        }

        private static string ColorHex(Color c) =>
            $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";

        // ---- Track from the map (double-click a place/NPC/spot, or press the map-track key) --

        private float lastMapClickTime = -1f;
        private Vector2 lastMapClickPos;
        private const float DoubleClickTime = 0.35f;   // max gap between the two clicks
        private const float DoubleClickSlop = 22f;     // max pixel drift between the two clicks

        /// <summary>True on the second of two quick left-clicks near the same spot. Only polled while the
        /// map is open, so ordinary gameplay clicks never seed it.</summary>
        private bool DoubleClickedOnMap()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return false;

            Vector2 pos = UnityEngine.Input.mousePosition;
            float now = Time.unscaledTime;
            bool isDouble = now - lastMapClickTime <= DoubleClickTime &&
                            (pos - lastMapClickPos).sqrMagnitude <= DoubleClickSlop * DoubleClickSlop;

            if (isDouble)
            {
                lastMapClickTime = -1f; // consume so a triple-click doesn't re-fire
                return true;
            }

            lastMapClickTime = now;
            lastMapClickPos = pos;
            return false;
        }

        private void TryMapTrack()
        {
            try
            {
                if (!MapScreenOpen())
                {
                    DeadReckoningPlugin.Log.LogInfo("Open the map first, hover a place or NPC, then press the map-track key.");
                    return;
                }
                MapCursor cursor = UIScreen<MapScreen>.Instance.MapCursor;
                IMapInteractable hovered = cursor != null ? cursor.HoveredInteractable : null;
                if (DeadReckoningPlugin.VerboseLogging.Value)
                    DeadReckoningPlugin.Log.LogInfo($"DR-MAPTRACK cursor={(cursor != null)} hovered={(hovered != null ? hovered.GetType().Name : "null")}");

                if (hovered is MapLocationMarkerListWidget locWidget)
                {
                    MapLocationMarker marker = ((ListWidget<MapLocationMarker>)locWidget).Data;
                    if (marker != null && marker.RoomAssets != null && marker.RoomAssets.Count > 0)
                    {
                        // Double-clicking a place you're already seeking cancels it.
                        if (IsTrackingRooms(marker.RoomAssets)) ClearTarget();
                        else SetTrackedRooms(marker.RoomAssets, marker.GetLocationName());
                    }
                    else TryFreePin(); // no usable room on this marker → treat as a free pin
                }
                else if (hovered is MapNpcMarkerListWidget npcWidget)
                {
                    NpcConfigAsset cfg = ((ListWidget<NpcConfigAsset>)npcWidget).Data;
                    if (cfg != null)
                    {
                        // Double-clicking an NPC you're already seeking cancels it.
                        if (SameNpc(cfg, tracked)) ClearTarget();
                        else SetTracked(cfg);
                    }
                    else TryFreePin();
                }
                else
                {
                    // Nothing trackable hovered → free pin: the room whose map rectangle is under the cursor.
                    TryFreePin();
                }
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Map track failed: {e}");
            }
        }

        /// <summary>Free pin: track the room whose map rectangle is under the cursor (same routing as
        /// a house — you're just picking a spot instead of a labelled marker).</summary>
        private void TryFreePin()
        {
            MapWidget mw = UIScreen<MapScreen>.Instance.ActiveMapWidget;
            if (mw == null || mw.RoomMarkers == null || !AddressableLibrary<NavigationLibrary>.Exists)
            {
                DeadReckoningPlugin.Log.LogInfo("Can't pin here — the map isn't ready.");
                return;
            }

            // Screen-point hit test must use the map canvas's camera (null only for overlay canvases).
            Canvas canvas = mw.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            Vector2 mouse = Input.MousePosition;

            // Find the smallest room rectangle under the cursor, and the local point inside it.
            MapRoomMarker best = null;
            Vector2 bestLocal = Vector2.zero;
            float bestArea = float.MaxValue;
            foreach (MapRoomMarker rm in mw.RoomMarkers)
            {
                if (rm == null || rm.RoomAsset == null || rm.RectTransform == null) continue;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rm.RectTransform, mouse, cam, out Vector2 local)) continue;
                Rect r = rm.RectTransform.rect;
                if (!r.Contains(local)) continue;
                float area = Mathf.Abs(r.width * r.height);
                if (area < bestArea) { bestArea = area; best = rm; bestLocal = local; }
            }

            if (best == null)
            {
                DeadReckoningPlugin.Log.LogInfo("No spot under the cursor — point directly at a place on the map, then press the key.");
                return;
            }

            // Room-rect local point → room's nav rect (0-1 lerp) → room-local position.
            NavigationLibrary.RoomData rd = AddressableLibrary<NavigationLibrary>.Instance.GetRoomData(best.RoomAsset);
            if (rd == null) { DeadReckoningPlugin.Log.LogWarning("Pinned room has no nav data."); return; }
            Rect rect = best.RectTransform.rect;
            float u = Mathf.InverseLerp(rect.xMin, rect.xMax, bestLocal.x);
            float v = Mathf.InverseLerp(rect.yMin, rect.yMax, bestLocal.y);
            Vector3 nav = new Vector3(
                Mathf.Lerp(rd.NavigationGraphRect.xMin, rd.NavigationGraphRect.xMax, u),
                0f,
                Mathf.Lerp(rd.NavigationGraphRect.yMin, rd.NavigationGraphRect.yMax, v));
            Vector3 roomPos = rd.NavToRoomPosition(nav);

            // Double-clicking (near) the pin you already dropped cancels it.
            if (hasPin && pinRoom == best.RoomAsset && (pinRoomPos - roomPos).sqrMagnitude <= 4f)
            {
                ClearTarget();
                return;
            }

            string name;
            try { name = best.RoomAsset.GetRoomName(true); } catch { name = null; }
            if (string.IsNullOrEmpty(name) || name == "???") name = "Pinned spot";
            SetPin(best.RoomAsset, roomPos, $"📍 {name}");
        }

        private static bool MapScreenOpen()
        {
            try { return UIScreen<MapScreen>.Instance != null && UIScreen<MapScreen>.Instance.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        private static bool RelationshipPanelOpen()
        {
            try
            {
                RelationshipListWidget[] arr = UnityEngine.Object.FindObjectsByType<RelationshipListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                return arr != null && arr.Length > 0;
            }
            catch { return false; }
        }

        private void ClosePicker()
        {
            DetachPicker();
            try { UIScreen<PickNpcScreen>.Instance.Hide(); } catch { }
            SetPickerBlock(false);
            pickerScreen = null;
        }

        private static List<NpcConfigAsset> BuildNpcRoster()
        {
            var list = new List<NpcConfigAsset>();
            var seen = new HashSet<NpcConfigAsset>();
            try
            {
                foreach (EntityCharacterPersistence p in GamePersistence.Instance.EntityCharacters)
                {
                    NpcConfigAsset cfg = AddressableLibrary<NpcLibrary>.Instance.Find(p.Guid);
                    if (cfg == null || !seen.Add(cfg)) continue;
                    // Only NPCs the player has actually met — the native widget shows "???" otherwise.
                    if (!GamePersistence.Instance.NpcRelationships.FindOrCreate(cfg.Entity).IsNameRevealed) continue;
                    list.Add(cfg);
                }
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogWarning($"Building NPC roster failed: {e.Message}");
            }
            if (list.Count == 0)
                DeadReckoningPlugin.Log.LogWarning("No discovered NPCs yet — meet some characters first.");
            return list;
        }

        // ---- Steering -----------------------------------------------------------------------

        private void Steer()
        {
            Vector3 skull = active.Mover.transform.position;
            Vector3 player = PlayerPos();

            // Track player speed so the skull can outrun the player (e.g. sprinting in cat form).
            if (hasLastPlayer && Time.deltaTime > 0f)
            {
                float inst = Vector3.Distance(player, lastPlayerPos) / Time.deltaTime;
                playerSpeed = Mathf.Lerp(playerSpeed, inst, 0.25f); // smooth out spikes
            }
            lastPlayerPos = player;
            hasLastPlayer = true;

            // Leash: if the skull strays too far (snagged on geometry, or you ran off), snap it back
            // beside you instead of leaving it stranded.
            if (Vector3.Distance(skull, player) > DeadReckoningPlugin.MaxLeash.Value)
            {
                Vector3 reset = player + PlayerForwardIsh() * DeadReckoningPlugin.StandoffDistance.Value
                                + Vector3.up * DeadReckoningPlugin.HoverHeight.Value;
                active.Mover.Teleport(reset, forceToWalkablePosition: false);
                cachedRoute = null; nextRouteAt = 0f;
                return;
            }

            Vector3? live = HasTarget() ? TrackedWorldPos() : null;

            float standoff = DeadReckoningPlugin.StandoffDistance.Value;
            float height = DeadReckoningPlugin.HoverHeight.Value;
            Vector3 hover;

            if (live.HasValue)
            {
                // Both ends at the SAME height so a level target gives a flat line (no upward bias) —
                // the skull lands on the sightline, not above your head. Its own bob still makes it float.
                Vector3 a = player + Vector3.up * TrackEyeHeight;

                // Follow the walkable route: lead a standoff along the A* path so we curve around
                // furniture/walls. Falls back to the straight me→target line when no path is available.
                Vector3 leadPoint;
                // Lead further along the route than the idle standoff, so it clearly scouts ahead.
                float pathLeadDist = standoff * 1.8f;
                if (DeadReckoningPlugin.FollowPath.Value && pathGuide.TryLead(player, live.Value, pathLeadDist, out Vector3 pathLead))
                {
                    leadPoint = new Vector3(pathLead.x, a.y, pathLead.z);
                    // Don't let it wander far from you when you're not heading toward the target: cap how
                    // far the lead point can sit from the player (straight-line).
                    Vector3 fromPlayer = leadPoint - a;
                    float maxLead = standoff * 2.2f;
                    if (fromPlayer.magnitude > maxLead) leadPoint = a + fromPlayer.normalized * maxLead;
                }
                else
                {
                    Vector3 b = live.Value + Vector3.up * TrackEyeHeight;
                    Vector3 to = b - a;
                    float dist = to.magnitude;
                    Vector3 dir = dist > 0.001f ? to / dist : FlatDirFromPlayer(skull, player);
                    float reach = Mathf.Min(standoff, dist * 0.85f); // don't sit on top of a close target
                    leadPoint = a + dir * reach;
                }

                // The skull mesh floats above the point we steer; drop the steer-point by that much so
                // the mesh itself lands on the lead point instead of above it.
                hover = leadPoint - Vector3.up * SkullVisualLift();
            }
            else
            {
                pathGuide.Clear();
                // Idle = "not seeking": settle on the nearest point of a sphere around you, in whatever
                // HORIZONTAL direction the skull currently sits — so if you run north and it lags to your
                // south, it settles to your south instead of flying over your head to one fixed spot. The
                // spring-damper below still gives it the lag + rebound. Direction is flattened so the rest
                // point stays at hover height regardless of the skull's current altitude.
                float t = Time.time;
                Vector3 center = player + Vector3.up * (height + Mathf.Sin(t * 1.3f) * 0.15f); // gentle bob
                Vector3 flatFrom = skull - center; flatFrom.y = 0f;
                Vector3 dir;
                if (flatFrom.sqrMagnitude > 0.01f) dir = flatFrom.normalized;
                else { dir = PlayerForwardIsh(); dir.y = 0f; dir = -dir.normalized; } // fallback: behind you
                float radius = standoff + (Mathf.PerlinNoise(t * 0.13f, 4.7f) - 0.5f) * (standoff * 0.4f); // breathe
                hover = center + dir * radius;
            }

            // Ride above the surface beneath the skull (terrain, bridge deck) so it follows a bridge
            // arch instead of clipping through it. Raise-only, so it never gets forced down into a
            // river/void — over water there's no ground hit, so it keeps its player-based height.
            hover = ClampAboveGround(hover, player.y);

            if (DeadReckoningPlugin.VerboseLogging.Value && Time.time >= nextProbe)
            {
                nextProbe = Time.time + 1.5f;
                ProbeNearbyLayers(skull);
            }

            // Cap scales with the player so it always keeps up, plus headroom to catch back up.
            if (justSpawned)
            {
                // Land it on its spot instantly (while it's still fading in) so it never flies across.
                active.Mover.Teleport(hover, forceToWalkablePosition: false);
                justSpawned = false;
            }
            else
            {
                float cap = Mathf.Max(MaxSpeed, playerSpeed * 1.8f + 3f);
                Vector3 vel;
                if (live.HasValue)
                {
                    // Seeking: crisp proportional chase toward the lead point (settles to the standoff).
                    vel = (hover - skull) * DeadReckoningPlugin.FollowStrength.Value;
                    idleVel = vel; // seed the idle spring so the hand-off when tracking drops is smooth
                }
                else
                {
                    // Idle: a spring-damper with its OWN momentum, so it genuinely lags when you run
                    // then springs to catch up and gently overshoots — never a locked "stay X away" gap.
                    float dt = Time.deltaTime;
                    idleVel += (hover - skull) * DeadReckoningPlugin.IdleSpring.Value * dt;      // pull toward you
                    idleVel *= Mathf.Max(0f, 1f - DeadReckoningPlugin.IdleDamping.Value * dt);   // damping
                    vel = idleVel;
                }
                vel = Vector3.ClampMagnitude(vel, cap);
                if (DeadReckoningPlugin.Collide.Value)
                    vel = AvoidWalls(skull, vel);
                if (!live.HasValue) idleVel = vel; // persist the actually-applied velocity (post clamp/avoid)
                if (vel.sqrMagnitude > 0.0001f)
                    active.Mover.Move(vel);
            }

            if (DeadReckoningPlugin.VerboseLogging.Value && HasTarget() && Time.time >= nextDebugLog)
            {
                nextDebugLog = Time.time + 1f;
                DeadReckoningPlugin.Log.LogInfo(
                    $"track='{trackedName}' live={(live.HasValue ? live.Value.ToString() : "no (idle wander)")} skull={skull}");
            }
        }

        /// <summary>
        /// Where to steer for the tracked NPC: their live position if they're in this room, else the
        /// world position of the door leading toward their room (so the skull guides you into the
        /// house). Null only when there's genuinely no route — then the skull idles/wanders.
        /// </summary>
        private Vector3? TrackedWorldPos()
        {
            // Quest gather/mine node: steer straight to the live in-scene node (the copper vein, the
            // berry bush). It's in the loaded room, so the A* lead in Steer curves the skull to it.
            if (trackedNode != null) return trackedNode.position;

            // Place/house target: lead toward the nearest tracked room's door; idle once we're inside.
            if (trackedRooms != null && trackedRooms.Count > 0)
            {
                if (!AddressableLibrary<NavigationLibrary>.Exists) return null;
                RoomAsset cur = GamePersistence.CurrentRoomAsset;
                if (cur == null) return null;
                if (trackedRooms.Contains(cur))
                {
                    // In the target room: a free pin points at its exact spot; a house just idles (arrived).
                    if (hasPin && cur == pinRoom) return PinWorld();
                    return null;
                }
                if (Time.time >= nextRouteAt)
                {
                    nextRouteAt = Time.time + 0.4f;
                    cachedRoute = RoomRouter.DoorTowardFirstReachable(cur, trackedRooms);
                }
                return cachedRoute;
            }

            if (tracked == null || tracked.Entity == null) return null;
            SerializedGuid want = tracked.Entity.SerializedGuid;

            // Same room → live transform.
            List<EntityCharacter> instances = EntityCharacter.Instances;
            for (int i = 0; i < instances.Count; i++)
            {
                EntityCharacter ec = instances[i];
                if (ec == null || ec.Entity == null || ec.Entity.SerializedGuid != want) continue;
                ICharacterView cv = ec.CharacterView;
                return (cv != null && cv.Mover != null) ? cv.Mover.transform.position : ec.transform.position;
            }

            // Different room → route to the door that heads toward their room (BFS, throttled).
            RoomAsset current = AddressableLibrary<NavigationLibrary>.Exists ? GamePersistence.CurrentRoomAsset : null;
            RoomAsset npcRoom = null;
            try { npcRoom = GamePersistence.Instance.EntityCharacters.FindOrCreate(tracked.Entity).Room; } catch { }

            bool routable = current != null && npcRoom != null && npcRoom != current;
            if (routable && Time.time >= nextRouteAt)
            {
                nextRouteAt = Time.time + 0.4f;
                cachedRoute = RoomRouter.DoorToward(current, npcRoom);
            }
            Vector3? result = routable ? cachedRoute : (Vector3?)null;

            if (DeadReckoningPlugin.VerboseLogging.Value && Time.time >= nextNpcPosLog)
            {
                nextNpcPosLog = Time.time + 1f;
                DeadReckoningPlugin.Log.LogInfo(
                    $"DR-NPCPOS npc='{trackedName}' inRoomInstances=false current='{SafeName(current)}' npcRoom='{SafeName(npcRoom)}' routable={routable} door={(result.HasValue ? result.Value.ToString() : "null")}");
            }
            return result;
        }

        private float nextNpcPosLog;
        private static string SafeName(RoomAsset r) { try { return r == null ? "null" : r.name; } catch { return "?"; } }

        // ---- Helpers ------------------------------------------------------------------------

        private static Vector3 PlayerPos() => MonoBehaviourSingleton<PlayerView>.Instance.Mover.transform.position;

        private static Vector3 PlayerForwardIsh()
        {
            Transform t = MonoBehaviourSingleton<PlayerView>.Instance.Mover.transform;
            Vector3 f = t.forward; f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }

        private static Vector3 FlatDirFromPlayer(Vector3 from, Vector3 player)
        {
            Vector3 d = from - player; d.y = 0f;
            return d.sqrMagnitude > 0.001f ? d.normalized : PlayerForwardIsh();
        }

        /// <summary>Raise the hover point so the skull clears the ground/bridge below it (accounting
        /// for the mesh floating above the steer-point). Never lowers, so over water it's a no-op.</summary>
        private Vector3 ClampAboveGround(Vector3 hover, float refY)
        {
            if (groundMask == -1)
            {
                if (!AssetLibrary.Exists) return hover;
                groundMask = AssetLibrary.Instance.GroundLayerMask.value;
            }
            if (groundMask == 0) return hover;

            Vector3 org = new Vector3(hover.x, Mathf.Max(hover.y, refY) + 8f, hover.z);
            if (Physics.Raycast(org, Vector3.down, out RaycastHit hit, 40f, groundMask, QueryTriggerInteraction.Ignore))
            {
                float floor = hit.point.y + DeadReckoningPlugin.GroundClearance.Value - SkullVisualLift();
                if (hover.y < floor) hover.y = floor;
            }
            return hover;
        }

        /// <summary>Verbose diagnostic: log the layers of colliders near the skull, so we can see what
        /// layer house walls / the bridge are actually on and target collision correctly.</summary>
        private void ProbeNearbyLayers(Vector3 at)
        {
            try
            {
                // Cast horizontally in 8 directions and report what's hit (incl. triggers), so we can
                // see exactly which collider/layer the house wall is and whether it's a trigger.
                Vector3[] dirs =
                {
                    Vector3.forward, new Vector3(1,0,1).normalized, Vector3.right, new Vector3(1,0,-1).normalized,
                    Vector3.back, new Vector3(-1,0,-1).normalized, Vector3.left, new Vector3(-1,0,1).normalized,
                };
                string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                var seen = new HashSet<string>();
                for (int i = 0; i < dirs.Length; i++)
                {
                    if (Physics.SphereCast(at, 0.3f, dirs[i], out RaycastHit h, 2f, ~0, QueryTriggerInteraction.Collide))
                    {
                        int layer = h.collider.gameObject.layer;
                        string ln = LayerMask.LayerToName(layer);
                        seen.Add($"{names[i]}:{h.distance:0.0}m [{layer}]{(string.IsNullOrEmpty(ln) ? "?" : ln)}={h.collider.name} trig={(h.collider.isTrigger ? 1 : 0)} nY={h.normal.y:0.0}");
                    }
                }
                if (seen.Count > 0)
                    DeadReckoningPlugin.Log.LogInfo("DR-WALLPROBE " + string.Join(" || ", seen));
            }
            catch { }
        }

        /// <summary>
        /// Stop the skull passing through house walls: spherecast the intended step against the
        /// "Default" layer (static geometry) and clamp to just short of a hit, sliding along the
        /// surface so it can still round corners toward a door. Ground/player/NPCs are on other
        /// layers, so it stays free to fly low and hug the player.
        /// </summary>
        private Vector3 AvoidWalls(Vector3 from, Vector3 vel)
        {
            if (Time.deltaTime <= 0f) return vel;
            if (envMask == -1)
            {
                if (!AssetLibrary.Exists) return vel; // not ready yet; try again next frame
                // ObstacleLayer only — the state that felt good (obstacle collision, clean bridge, no
                // snagging). Ground/Default/normal-filter/SphereCastAll experiments made it worse and
                // were reverted; house walls (Ground-layer) remain a known clip, accepted for now.
                envMask = AssetLibrary.Instance.ObstacleLayerMask.value;
            }
            if (envMask == 0) return vel;

            Vector3 step = vel * Time.deltaTime;
            float mag = step.magnitude;
            if (mag < 1e-5f) return vel;
            Vector3 dir = step / mag;

            if (Physics.SphereCast(from, SkullRadius, dir, out RaycastHit hit, mag + Skin, envMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 allowed = dir * Mathf.Max(0f, hit.distance - Skin);
                Vector3 remaining = step - dir * Mathf.Min(hit.distance, mag);
                Vector3 slide = Vector3.ProjectOnPlane(remaining, hit.normal);
                return (allowed + slide) / Time.deltaTime;
            }
            return vel;
        }

        /// <summary>How far the visible skull mesh floats above the mover point we steer. Measured
        /// once from the renderer bounds and cached (the bob then oscillates around the sightline).</summary>
        private float SkullVisualLift()
        {
            if (skullVisualLift >= 0f) return skullVisualLift;
            try
            {
                Renderer r = active.GetComponentInChildren<Renderer>();
                if (r != null)
                    skullVisualLift = Mathf.Max(0f, r.bounds.center.y - active.Mover.transform.position.y);
            }
            catch { }
            return skullVisualLift < 0f ? 0f : skullVisualLift;
        }
    }
}
