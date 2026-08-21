using System;
using System.Collections.Generic;
using System.Linq;
using Chicken.UI;
using Chicken.Utilities;
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
        private string trackedName;
        private Action<PickNpcListWidget> npcClickedHandler;

        private readonly TrackHud hud = new TrackHud();
        private readonly MapPin mapPin = new MapPin();

        private PickNpcScreen pickerScreen;
        private bool pickerBlocking; // blocks gameplay input (camera zoom) while the picker is open
        private static bool pickerProbed; // one-shot: dump a picker card's hierarchy to find the native selection frame
        private static bool mapProbed;    // one-shot: dump a tracked map house marker's hierarchy
        private const string PickerBlockId = "DeadReckoningNpcPicker";

        private const float MaxSpeed = 14f;        // floor; the real cap scales with player speed
        private const float WanderDegPerSec = 35f; // lazy drift speed around the player when idle
        private Vector3 lastPlayerPos;
        private bool hasLastPlayer;
        private float playerSpeed;
        private const float TrackEyeHeight = 0.5f; // body-center height the tracking sightline runs at
        private float skullVisualLift = -1f;       // how far the visible skull floats above its steer-point

        private const float SkullRadius = 0.3f;    // for wall collision
        private const float Skin = 0.05f;
        private static int envMask = -1;           // obstacle layers (walls), NOT ground/water/bridge
        private static int groundMask = -1;        // ground/terrain/bridge deck the skull hovers above
        private float nextProbe;
        private float wanderAngle;
        private float nextDebugLog;

        private Vector3? cachedRoute; // door-to-head-toward for an off-room target
        private float nextRouteAt;

        private bool wantActive;      // user intends the skull to be out; survives scene changes
        private float nextSpawnAttempt;

        private void Awake() => Instance = this;

        // --- External tracking API (used by the Relationships Track button). Mirrors the picker path
        // without touching it, so the working F8 flow is unchanged. ---
        internal bool IsTracked(NpcConfigAsset cfg) => cfg != null && tracked == cfg;

        internal void SetTracked(NpcConfigAsset cfg)
        {
            tracked = cfg;
            trackedRooms = null; hasPin = false; // single target
            trackedName = cfg != null ? AddressableLibrary<NpcLibrary>.Instance.GetNpcName(cfg, checkIsNameRevealed: false) : null;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking: {trackedName ?? "<none>"}");
        }

        internal void SetTrackedRooms(List<RoomAsset> rooms, string name)
        {
            trackedRooms = rooms != null ? rooms.Where(r => r != null).ToList() : null;
            tracked = null; hasPin = false; // single target
            trackedName = name;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking place: {name ?? "<none>"}");
        }

        private void SetPin(RoomAsset room, Vector3 roomPos, string name)
        {
            trackedRooms = new List<RoomAsset> { room }; // reuse room routing when out of the room
            pinRoom = room; pinRoomPos = roomPos; hasPin = true;
            tracked = null;
            trackedName = name;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo($"Now tracking pin: {name}");
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

        private bool HasTarget() => tracked != null || (trackedRooms != null && trackedRooms.Count > 0);

        internal void ToggleTrack(NpcConfigAsset cfg)
        {
            if (IsTracked(cfg)) ClearTarget();
            else SetTracked(cfg);
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
                if (DeadReckoningPlugin.SpawnKey.Value.IsDown())
                {
                    wantActive = !wantActive;
                    if (!wantActive) Despawn();
                }

                if (DeadReckoningPlugin.PickNpcKey.Value.IsDown())
                {
                    if (PickerOpen()) ClosePicker();
                    else OpenNpcPicker();
                }

                if (DeadReckoningPlugin.MapTrackKey.Value.IsDown())
                    TryMapTrack();

                // Let the cancel/back input (Esc / controller B) close the picker too.
                if (PickerOpen() && InputUtility.GetCancelInputDown())
                    ClosePicker();

                if (DeadReckoningPlugin.ClearTargetKey.Value.IsDown())
                    ClearTarget();
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

            if (active != null) Steer();

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
                    foreach (MapLocationMarkerListWidget w in UnityEngine.Object.FindObjectsByType<MapLocationMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        MapLocationMarker m = ((ListWidget<MapLocationMarker>)w).Data;
                        bool on = !hasPin && trackedRooms != null && m != null && m.RoomAssets != null && m.RoomAssets.Any(r => trackedRooms.Contains(r));
                        if (on && !mapProbed && DeadReckoningPlugin.VerboseLogging.Value)
                        {
                            mapProbed = true;
                            try { DumpWidget(w, "DR-HOUSEPROBE map house marker hierarchy:"); }
                            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"House probe failed: {e.Message}"); }
                        }
                        MapMarkerTint.Set(w, on);
                    }
                    foreach (MapNpcMarkerListWidget w in UnityEngine.Object.FindObjectsByType<MapNpcMarkerListWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        MapMarkerTint.Set(w, tracked != null && ((ListWidget<NpcConfigAsset>)w).Data == tracked);
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
                        MapMarkerHighlight.Set(w, tracked != null && w.Data == tracked);
                    }
                }
            }
            catch { }
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
                Transform parent = mw.RoomMarkers[0].RectTransform != null ? mw.RoomMarkers[0].RectTransform.parent : mw.transform;
                mapPin.Refresh(mw, pinRoom, pinRoomPos, parent);
            }
            catch { mapPin.Hide(); }
        }

        private void UpdateHud()
        {
            if (!DeadReckoningPlugin.ShowHud.Value || active == null)
            {
                hud.Hide();
                return;
            }
            hud.Set(HasTarget() ? $"Tracking: {trackedName}" : "Tracking: nobody yet");
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
            hasLastPlayer = false; playerSpeed = 0f;
            wanderAngle = Mathf.Atan2(fwd.z, fwd.x) * Mathf.Rad2Deg; // start idle drift from where it spawned
            DeadReckoningPlugin.Log.LogInfo("Skull soul blob spawned. F8 to pick an NPC to track, F7 to clear, F9 to despawn.");
        }

        private void Despawn()
        {
            if (active == null) return;
            try
            {
                if (active.CritterBehaviour != null && active.CritterBehaviour.enabled)
                    active.HideAndDestroy();
                else
                    UnityEngine.Object.Destroy(active.gameObject);
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogWarning($"Despawn fell back to Destroy: {e.Message}");
                if (active != null) UnityEngine.Object.Destroy(active.gameObject);
            }
            finally { active = null; }
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
                screen.Show("Track who?");
                pickerScreen = screen;
                SetPickerBlock(true);
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogError($"Opening PickNpcScreen failed: {e}");
            }
        }

        private void OnNpcPicked(PickNpcListWidget widget)
        {
            tracked = widget != null ? widget.Data : null;
            trackedRooms = null; hasPin = false; // clear any place/free-pin target so the NPC actually wins
            trackedName = tracked != null
                ? AddressableLibrary<NpcLibrary>.Instance.GetNpcName(tracked, checkIsNameRevealed: false)
                : null;
            cachedRoute = null; nextRouteAt = 0f;
            DetachPicker();
            try { UIScreen<PickNpcScreen>.Instance.Hide(); } catch { }
            SetPickerBlock(false);
            DeadReckoningPlugin.Log.LogInfo($"Now tracking: {trackedName ?? "<none>"}");
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
            trackedName = null;
            cachedRoute = null; nextRouteAt = 0f;
            DeadReckoningPlugin.Log.LogInfo("Tracking cleared — the skull just floats near you.");
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

        // ---- Track from the map (hover a place/NPC marker, press the map-track key) ---------

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
                        SetTrackedRooms(marker.RoomAssets, marker.GetLocationName());
                    else
                        DeadReckoningPlugin.Log.LogWarning("That place has no room to route to.");
                }
                else if (hovered is MapNpcMarkerListWidget npcWidget)
                {
                    NpcConfigAsset cfg = ((ListWidget<NpcConfigAsset>)npcWidget).Data;
                    if (cfg != null) SetTracked(cfg);
                }
                else
                {
                    // Nothing hovered → free pin: track the room whose map rectangle is under the cursor.
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

        private static Vector3? RouteToNearestRoom(RoomAsset current, List<RoomAsset> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                Vector3? door = RoomRouter.DoorToward(current, rooms[i]);
                if (door.HasValue) return door;
            }
            return null;
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
                // Leading: sit on the straight torso-to-torso line from the player to the target, a
                // standoff in. Both ends use the SAME height so a level target gives a flat line (no
                // artificial upward bias) — the skull lands on the sightline, not above your head.
                // Its own bob still makes it float. It only rises/dips when the target actually does.
                Vector3 a = player + Vector3.up * TrackEyeHeight;
                Vector3 b = live.Value + Vector3.up * TrackEyeHeight;
                Vector3 to = b - a;
                float dist = to.magnitude;
                Vector3 dir = dist > 0.001f ? to / dist : FlatDirFromPlayer(skull, player);
                float reach = Mathf.Min(standoff, dist * 0.85f); // don't sit on top of a close target
                Vector3 onLine = a + dir * reach;                // where the VISIBLE skull should land
                // The skull mesh floats above the point we steer; drop the steer-point by that much so
                // the mesh itself lands on the me→target screen line instead of above it.
                hover = onLine - Vector3.up * SkullVisualLift();

                // Keep the idle wander angle in sync so the hand-off when tracking drops is smooth.
                Vector3 flat = dir; flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f) wanderAngle = Mathf.Atan2(flat.z, flat.x) * Mathf.Rad2Deg;
            }
            else
            {
                // Idle: lazily drift/wander around the player. This is the "not tracking" tell.
                float noise = Mathf.PerlinNoise(Time.time * 0.25f, 12.3f) - 0.5f; // -0.5..0.5, smooth
                wanderAngle += noise * WanderDegPerSec * Time.deltaTime;
                float rad = wanderAngle * Mathf.Deg2Rad;
                Vector3 wanderDir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                float s = standoff + Mathf.Sin(Time.time * 0.7f) * 0.4f;  // gentle breathe in/out
                float h = height + Mathf.Sin(Time.time * 1.3f) * 0.15f;   // gentle vertical drift
                hover = player + wanderDir * s + Vector3.up * h;
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
            float cap = Mathf.Max(MaxSpeed, playerSpeed * 1.8f + 3f);
            Vector3 vel = (hover - skull) * DeadReckoningPlugin.FollowStrength.Value;
            vel = Vector3.ClampMagnitude(vel, cap);
            if (DeadReckoningPlugin.Collide.Value)
                vel = AvoidWalls(skull, vel);
            if (vel.sqrMagnitude > 0.0001f)
                active.Mover.Move(vel);

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
                    cachedRoute = RouteToNearestRoom(cur, trackedRooms);
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
