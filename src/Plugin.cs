using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace DeadReckoning
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Moonlight Peaks.exe")]
    public sealed class DeadReckoningPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dirtyredz.moonlightpeaks.deadreckoning";
        public const string PluginName = "Dead Reckoning";
        // Keep in step with <Version> in the csproj - pack.ps1 names the archive from that one
        // and BepInEx reports this one. See 12-versioning-and-release.md.
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;
        private static bool farSightChecked;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> SpawnKey;
        internal static ConfigEntry<KeyboardShortcut> PickNpcKey;
        internal static ConfigEntry<KeyboardShortcut> MapTrackKey;
        internal static ConfigEntry<KeyboardShortcut> ClearTargetKey;
        internal static ConfigEntry<int> SoulblobIndex;

        internal static ConfigEntry<float> StandoffDistance;
        internal static ConfigEntry<float> HoverHeight;
        internal static ConfigEntry<float> GroundClearance;
        internal static ConfigEntry<float> FollowStrength;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> Collide;
        internal static ConfigEntry<float> MaxLeash;

        internal static ConfigEntry<bool> VerboseLogging;

        // Mod Menu reads these out of ConfigDescription.Tags to title its sections.
        private const string ProofSection = "ModMenu.Section=Floating proof (dev)";
        private const string TuningSection = "ModMenu.Section=Follow tuning";
        private const string DiagnosticsSection = "ModMenu.Section=Diagnostics";

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "General", "Enabled", true,
                new ConfigDescription(
                    "Master switch for Dead Reckoning.",
                    null,
                    ProofSection, "ModMenu.Label=Enabled"));

            SpawnKey = Config.Bind(
                "General", "SpawnKey", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                new ConfigDescription(
                    "Spawn/despawn a soul blob that hovers and follows you.",
                    null,
                    ProofSection, "ModMenu.Label=Spawn / despawn key"));

            PickNpcKey = Config.Bind(
                "General", "PickNpcKey", new KeyboardShortcut(UnityEngine.KeyCode.F8),
                new ConfigDescription(
                    "Open the game's NPC picker to choose who the skull points toward.",
                    null,
                    ProofSection, "ModMenu.Label=Pick NPC key"));

            MapTrackKey = Config.Bind(
                "General", "MapTrackKey", new KeyboardShortcut(UnityEngine.KeyCode.F6),
                new ConfigDescription(
                    "While the map is open, track the place/house or NPC you're hovering.",
                    null,
                    ProofSection, "ModMenu.Label=Track hovered on map key"));

            ClearTargetKey = Config.Bind(
                "General", "ClearTargetKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
                new ConfigDescription(
                    "Stop tracking — the skull goes back to just hovering near you.",
                    null,
                    ProofSection, "ModMenu.Label=Clear target key"));

            SoulblobIndex = Config.Bind(
                "General", "SoulblobIndex", 0,
                new ConfigDescription(
                    "Which soul blob variant to spawn (0 = the skull).",
                    new AcceptableValueRange<int>(0, 32),
                    ProofSection, "ModMenu.Label=Soul blob variant"));

            StandoffDistance = Config.Bind(
                "Tuning", "StandoffDistance", 2.5f,
                new ConfigDescription(
                    "How far the skull hovers from you, in world units.",
                    new AcceptableValueRange<float>(0.5f, 8f),
                    TuningSection, "ModMenu.Label=Hover distance"));

            HoverHeight = Config.Bind(
                "Tuning", "HoverHeight", 1.6f,
                new ConfigDescription(
                    "How high above your feet the skull floats, in world units.",
                    new AcceptableValueRange<float>(0f, 4f),
                    TuningSection, "ModMenu.Label=Hover height"));

            GroundClearance = Config.Bind(
                "Tuning", "GroundClearance", 0.7f,
                new ConfigDescription(
                    "Minimum height the skull flies above the ground/stairs/bridge below it. Raise if it snags on low things.",
                    new AcceptableValueRange<float>(0f, 2.5f),
                    TuningSection, "ModMenu.Label=Height off the ground"));

            FollowStrength = Config.Bind(
                "Tuning", "FollowStrength", 12f,
                new ConfigDescription(
                    "How eagerly the skull chases its hover spot. Higher = snappier.",
                    new AcceptableValueRange<float>(1f, 40f),
                    TuningSection, "ModMenu.Label=Follow strength"));

            MaxLeash = Config.Bind(
                "Tuning", "MaxLeash", 7f,
                new ConfigDescription(
                    "If the skull gets this far from you, it snaps back to your side.",
                    new AcceptableValueRange<float>(3f, 40f),
                    TuningSection, "ModMenu.Label=Reset distance"));

            ShowHud = Config.Bind(
                "Tuning", "ShowHud", true,
                new ConfigDescription(
                    "Show a small window (top-left) with what the skull is currently tracking.",
                    null,
                    TuningSection, "ModMenu.Label=Show tracking window"));

            Collide = Config.Bind(
                "Tuning", "Collide", true,
                new ConfigDescription(
                    "Stop the skull passing through house walls. Turn off if it ever gets stuck.",
                    null,
                    TuningSection, "ModMenu.Label=Collide with walls"));

            VerboseLogging = Config.Bind(
                "Diagnostics", "VerboseLogging", false,
                new ConfigDescription(
                    "Log how the floating asset and targets are resolved.",
                    null,
                    DiagnosticsSection, "ModMenu.Label=Verbose logging"));

            gameObject.AddComponent<SkullGuide>();

            try
            {
                HarmonyInstance = new Harmony(PluginGuid);
                HarmonyInstance.PatchAll();
                // Far Sight loads AFTER us, so its type isn't available yet — the coexistence patch is
                // attached lazily from SkullGuide's first in-game frame (see TryPatchFarSight).
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Harmony patching failed (relationship Track button disabled): {e.Message}");
            }

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Press {SpawnKey.Value} in-game to spawn the floating proof.");
        }

        // If the Far Sight zoom mod is installed, make it stand down while our picker/relationship
        // panel is open (it otherwise scroll-zooms over them). Called once from SkullGuide's first
        // Update, by which point all plugins (incl. Far Sight, which loads after us) are present.
        internal static void TryPatchFarSight()
        {
            if (farSightChecked || HarmonyInstance == null) return;
            farSightChecked = true;
            try
            {
                System.Type farType = AccessTools.TypeByName("FarSightPlugin");
                if (farType == null) { Log.LogInfo("Far Sight not detected — no zoom coexistence needed."); return; }
                System.Reflection.MethodInfo isGameplay = AccessTools.Method(farType, "IsGameplay");
                if (isGameplay == null) { Log.LogWarning("Far Sight found but IsGameplay missing — can't coordinate zoom."); return; }
                HarmonyInstance.Patch(isGameplay, postfix: new HarmonyMethod(AccessTools.Method(typeof(FarSightCoexistPatch), nameof(FarSightCoexistPatch.Postfix))));
                Log.LogInfo("Far Sight detected — it will stand down while the NPC picker / relationship panel is open.");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Far Sight zoom coexistence patch failed: {e.Message}");
            }
        }
    }
}
