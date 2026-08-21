using Chicken.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// Marks a tracked NPC picker card with the game's OWN selection frame, recoloured purple — the
    /// same technique the Tastic-Palette mods use for colour swatches: clone the real widget piece so
    /// it carries the authentic art, sizing and thickness, then strip the <c>UIColorable</c>/
    /// <c>AnimatedWidget</c> that would re-tint or hide it and set the colour directly. No hand-drawn
    /// box, no sizing math.
    ///
    /// The game's real selection draws the frame AND recolours the name plate, so we mirror both. The
    /// purple clone is parked just BEFORE the native frame so that, when the player hovers a tracked
    /// card, the game's own orange selection (+ wing cursor) still draws on top as normal hover
    /// feedback; the name plate sits above both. The name plate is a game object driven by its own
    /// <c>UIColorable</c>, so we write its colour each frame while tracked (yielding to the native
    /// orange while it is actually hovered) and restore its true base colour — from
    /// <c>ColorLibrary.GetColor</c>, not a hover snapshot — on untrack. Map markers are handled
    /// separately by <see cref="MapMarkerTint"/> (they have no cloneable selection frame).
    /// </summary>
    internal static class MapMarkerHighlight
    {
        private const string FrameName = "DR_TrackFrame";
        private const float NativeOutset = 6f; // push the frame a touch outside the card
        private static readonly Color Purple = new Color(0.62f, 0.42f, 1f, 1f);

        internal static void Set(Component widget, bool on)
        {
            if (widget == null) return;
            var host = widget.transform as RectTransform;
            if (host == null) return;

            Transform existing = FindDeep(host, FrameName);
            var marker = existing != null ? existing.GetComponent<DRFrameMarker>() : null;

            if (!on)
            {
                if (marker != null)
                {
                    ApplyNameTint(marker, false); // restore the plate we recoloured (once)
                    marker.gameObject.SetActive(false);
                }
                return;
            }

            if (marker == null)
            {
                marker = BuildClone(host);
                if (marker == null) return;
            }

            marker.gameObject.SetActive(true);
            ApplyNameTint(marker, true);
        }

        /// <summary>Clone the card's native selection frame, tint it purple, and strip the components
        /// that would re-tint or fade it. Returns null if the card has no selection frame to clone.</summary>
        private static DRFrameMarker BuildClone(RectTransform host)
        {
            Transform native = FindDeep(host, "SelectionContainer"); // named clone excluded (it's FrameName)
            if (native == null) return null;

            GameObject clone = Object.Instantiate(native.gameObject, native.parent, false); // sibling; keeps layout
            clone.name = FrameName;
            // Sit just BEFORE the native frame so the game's orange selection draws on top of us on
            // hover, and the name plate (which comes after both) still draws on top of everything.
            clone.transform.SetSiblingIndex(native.GetSiblingIndex());

            // Kill the game's own colour driver and show/hide animator so our purple sticks and the
            // frame stays put instead of fading out when nothing is "selecting" it.
            foreach (UIColorable c in clone.GetComponentsInChildren<UIColorable>(true)) Object.Destroy(c);
            foreach (AnimatedWidget a in clone.GetComponentsInChildren<AnimatedWidget>(true)) Object.Destroy(a);

            foreach (Image img in clone.GetComponentsInChildren<Image>(true))
            {
                img.color = Purple;
                img.raycastTarget = false;
            }

            // Push the frame a few px outside the card so it reads like the native selection.
            ((RectTransform)clone.transform).sizeDelta += new Vector2(NativeOutset * 2f, NativeOutset * 2f);

            var marker = clone.AddComponent<DRFrameMarker>();

            // Capture the name plate (and the card's button, to yield to native hover) so we can
            // recolour it like the real selection and revert cleanly.
            Transform nameContainer = FindDeep(host, "NameContainer");
            Transform bg = nameContainer != null ? nameContainer.Find("Background") : null;
            if (bg != null)
            {
                marker.PlateImage = bg.GetComponent<Image>();
                marker.PlateColorable = bg.GetComponent<UIColorable>();
                marker.PlateBase = marker.PlateColorable != null
                    ? ColorLibrary.GetColor(marker.PlateColorable.Color) // true default, not a hover snapshot
                    : (marker.PlateImage != null ? marker.PlateImage.color : Color.white);
            }
            marker.Button = host.GetComponent<AnimatedButton>();
            return marker;
        }

        /// <summary>Tint (or restore) the name plate. While tracked we assert purple every frame, but
        /// yield to the game's own orange while the card is actually hovered/selected. Restore is
        /// one-shot so untracked cards hover normally.</summary>
        private static void ApplyNameTint(DRFrameMarker m, bool on)
        {
            if (m.PlateImage == null) return;

            if (on)
            {
                bool hovered = m.Button != null && SafeIsSelected(m.Button);
                if (!hovered) m.PlateImage.color = Purple;
                m.PlateTinted = true;
            }
            else if (m.PlateTinted)
            {
                m.PlateImage.color = m.PlateBase;
                if (m.PlateColorable != null) m.PlateColorable.ClearOverride(0f);
                m.PlateTinted = false;
            }
        }

        private static bool SafeIsSelected(AnimatedButton b)
        {
            try { return b.IsSelected; } catch { return false; }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>Tags our cloned selection frame and the name plate it recolours.</summary>
    internal sealed class DRFrameMarker : MonoBehaviour
    {
        internal Image PlateImage;
        internal UIColorable PlateColorable;
        internal Color PlateBase;
        internal bool PlateTinted;
        internal AnimatedButton Button;
    }
}
