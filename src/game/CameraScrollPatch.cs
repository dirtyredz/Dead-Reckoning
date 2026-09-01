using HarmonyLib;

namespace DeadReckoning
{
    /// <summary>
    /// Stops the world camera from zooming/rotating on the scroll wheel while our NPC picker or the
    /// game's Relationships panel is open. The camera reads scroll via the game's static
    /// <c>Input.MouseScrollDelta</c> wrapper; UI lists scroll via Unity's EventSystem (a different
    /// path), so the list keeps scrolling. We only zero it while those specific UIs are up, so other
    /// scroll-over-UI features (e.g. the calendar) are untouched.
    /// </summary>
    internal static class WorldScrollBlock
    {
        internal static bool PickerOpen;
        internal static bool RelationshipOpen;
        internal static bool Blocked => PickerOpen || RelationshipOpen;
    }

    // Scroll-rotate path (reads the MouseScrollDelta wrapper).
    [HarmonyPatch(typeof(Input), "MouseScrollDelta", MethodType.Getter)]
    internal static class InputMouseScrollDeltaPatch
    {
        private static void Postfix(ref UnityEngine.Vector2 __result)
        {
            if (WorldScrollBlock.Blocked && (__result.x != 0f || __result.y != 0f))
                __result = UnityEngine.Vector2.zero;
        }
    }

    // The actual zoom: GameCamera.ProcessPlayerCameraToggle reads the Rewired zoom axis and toggles
    // the close/far camera. Skip it entirely while our picker / the relationship panel is open.
    [HarmonyPatch(typeof(GameCamera), "ProcessPlayerCameraToggle")]
    internal static class GameCameraZoomBlockPatch
    {
        // Blocks the GAME's own scroll zoom-toggle while our menus are open.
        private static bool Prefix() => !WorldScrollBlock.Blocked;
    }

    /// <summary>
    /// Coexistence with the Far Sight camera-zoom mod: it zooms on scroll unless its own
    /// <c>IsGameplay()</c> says we're in a menu, and it doesn't recognise our hotkey picker as one.
    /// This postfix (patched by reflection only if Far Sight is installed) makes Far Sight treat our
    /// open picker/relationship panel as "not gameplay", so it stands down and stops zooming.
    /// </summary>
    internal static class FarSightCoexistPatch
    {
        internal static void Postfix(ref bool __result)
        {
            if (WorldScrollBlock.Blocked) __result = false;
        }
    }
}
