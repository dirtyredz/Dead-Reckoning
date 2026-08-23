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
        public const string PluginVersion = ModBuildInfo.Version;

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;
        private static bool farSightChecked;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> PickNpcKey;
        internal static ConfigEntry<int> SoulblobIndex;

        internal static ConfigEntry<float> StandoffDistance;
        internal static ConfigEntry<float> HoverHeight;
        internal static ConfigEntry<float> GroundClearance;
        internal static ConfigEntry<float> FollowStrength;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<float> HudPosX;
        internal static ConfigEntry<float> HudPosY;
        internal static ConfigEntry<float> HudFontSize;
        internal static ConfigEntry<bool> Collide;
        internal static ConfigEntry<bool> FollowPath;
        internal static ConfigEntry<float> MaxLeash;
        internal static ConfigEntry<float> IdleSpring;
        internal static ConfigEntry<float> IdleDamping;

        internal static ConfigEntry<bool> RecolorFlame;
        internal static ConfigEntry<string> FlameColor;

        internal static ConfigEntry<bool> VerboseLogging;

        // Mod Menu reads these out of ConfigDescription.Tags to title its sections.
        private const string ProofSection = "ModMenu.Section=General";
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

            PickNpcKey = Config.Bind(
                "General", "PickNpcKey", new KeyboardShortcut(UnityEngine.KeyCode.F6),
                new ConfigDescription(
                    "Open the game's NPC picker to choose who the skull points toward.",
                    null,
                    ProofSection, "ModMenu.Label=Pick NPC key"));

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

            IdleSpring = Config.Bind(
                "Tuning", "IdleFollowSpring", 6f,
                new ConfigDescription(
                    "When NOT seeking, how strongly the skull is pulled back toward you. Lower = lazier: it lags further behind before catching up.",
                    new AcceptableValueRange<float>(1f, 30f),
                    TuningSection, "ModMenu.Label=Idle follow spring"));

            IdleDamping = Config.Bind(
                "Tuning", "IdleFollowDamping", 2.5f,
                new ConfigDescription(
                    "When NOT seeking, how much its drift is damped. Lower = bouncier rebound/overshoot as it catches up; higher = settles straight in.",
                    new AcceptableValueRange<float>(0.5f, 10f),
                    TuningSection, "ModMenu.Label=Idle follow damping"));

            ShowHud = Config.Bind(
                "Tuning", "ShowHud", true,
                new ConfigDescription(
                    "Show on-screen text of what the skull is currently seeking.",
                    null,
                    TuningSection, "ModMenu.Label=Show seeking text"));

            HudPosX = Config.Bind(
                "Tuning", "HudPosX", 0f,
                new ConfigDescription(
                    "Seeking text horizontal position: 0 = left edge, 1 = right edge.",
                    new AcceptableValueRange<float>(0f, 1f),
                    TuningSection, "ModMenu.Label=Seeking text X"));

            HudPosY = Config.Bind(
                "Tuning", "HudPosY", 0.25f,
                new ConfigDescription(
                    "Seeking text vertical position, measured DOWN from the top: 0 = top, 1 = bottom.",
                    new AcceptableValueRange<float>(0f, 1f),
                    TuningSection, "ModMenu.Label=Seeking text Y (from top)"));

            HudFontSize = Config.Bind(
                "Tuning", "HudFontSize", 30f,
                new ConfigDescription(
                    "Seeking text font size.",
                    new AcceptableValueRange<float>(12f, 72f),
                    TuningSection, "ModMenu.Label=Seeking text size"));

            Collide = Config.Bind(
                "Tuning", "Collide", true,
                new ConfigDescription(
                    "Stop the skull passing through house walls. Turn off if it ever gets stuck.",
                    null,
                    TuningSection, "ModMenu.Label=Collide with walls"));

            FollowPath = Config.Bind(
                "Tuning", "FollowPath", true,
                new ConfigDescription(
                    "Lead you along the actual walkable route (around furniture/walls) instead of a straight line. Turn off for the old direct-line behaviour.",
                    null,
                    TuningSection, "ModMenu.Label=Follow the route"));

            RecolorFlame = Config.Bind(
                "Tuning", "RecolorFlame", true,
                new ConfigDescription(
                    "Recolour the soul blob's flame to a fixed colour (also stops it varying between spawns).",
                    null,
                    TuningSection, "ModMenu.Label=Recolour flame"));

            FlameColor = Config.Bind(
                "Tuning", "FlameColor", "#000000",
                new ConfigDescription(
                    "Soul blob flame colour, as a hex code (e.g. #000000 for black). Applied when 'Recolour flame' is on.",
                    null,
                    TuningSection, "ModMenu.Label=Flame colour (hex)"));

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

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Seek an NPC ({PickNpcKey.Value}) or a place to summon the skull.");
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
