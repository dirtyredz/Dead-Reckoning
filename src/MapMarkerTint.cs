using System.Collections.Generic;
using Chicken.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// Marks a tracked map marker (house/place, or NPC) by recolouring the game's OWN badge elements
    /// purple rather than drawing a frame around the whole widget — which wrapped the hidden expand
    /// popup and came out huge and offset. We tint the always-visible icon plate, plus the expand
    /// popup's ornamental frame and name banner. Colours are asserted each frame while tracked and
    /// restored to their true base colour (from <c>ColorLibrary</c>, not a hover-time snapshot) on
    /// untrack. The popup's native bat-wings are the game's own and left untouched.
    /// </summary>
    internal static class MapMarkerTint
    {
        private static readonly Color Purple = new Color(0.62f, 0.42f, 1f, 1f);

        internal static void Set(Component widget, bool on)
        {
            if (widget == null) return;
            Transform host = widget.transform;
            var tint = host.GetComponent<DRMarkerTint>();

            if (!on)
            {
                if (tint != null) tint.SetOn(false);
                return;
            }

            if (tint == null) tint = Build(host);
            tint.SetOn(true);
        }

        private static DRMarkerTint Build(Transform host)
        {
            var t = host.gameObject.AddComponent<DRMarkerTint>();
            t.Purple = Purple;

            // Compact state: the always-visible house/NPC icon plate.
            AddChild(t, FindDeep(host, "LayoutContainer"), "Background");

            // Expanded popup: its ornamental frame and its name banner (not the dark body, not the wings).
            Transform popup = FindDeep(host, "PopupWidget");
            if (popup != null)
            {
                Add(t, FindDeep(popup, "Frame"));
                AddChild(t, FindDeep(popup, "TitleContainer"), "Background");
            }
            return t;
        }

        private static void AddChild(DRMarkerTint t, Transform parent, string child)
        {
            if (parent != null) Add(t, parent.Find(child));
        }

        private static void Add(DRMarkerTint t, Transform tr)
        {
            if (tr == null) return;
            var img = tr.GetComponent<Image>();
            if (img == null) return;

            var col = tr.GetComponent<UIColorable>();
            Color baseColor = img.color;
            if (col != null)
            {
                Color lib = ColorLibrary.GetColor(col.Color); // true default, not a hover snapshot
                if (lib.a > 0.001f) baseColor = lib;
            }
            t.Add(img, baseColor, col);
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

    /// <summary>Holds the native marker graphics we recolour, their base colours, and their colourables
    /// so a tracked marker can be tinted purple every frame and cleanly reverted on untrack.</summary>
    internal sealed class DRMarkerTint : MonoBehaviour
    {
        internal Color Purple;
        private readonly List<Image> imgs = new List<Image>();
        private readonly List<Color> bases = new List<Color>();
        private readonly List<UIColorable> cols = new List<UIColorable>();
        private bool on;

        internal void Add(Image img, Color baseColor, UIColorable col)
        {
            imgs.Add(img);
            bases.Add(baseColor);
            cols.Add(col);
        }

        internal void SetOn(bool value)
        {
            if (value)
            {
                for (int i = 0; i < imgs.Count; i++)
                    if (imgs[i] != null) imgs[i].color = Purple; // re-assert over the game's own driver
                on = true;
            }
            else if (on)
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (imgs[i] != null) imgs[i].color = bases[i];
                    if (cols[i] != null) cols[i].ClearOverride(0f);
                }
                on = false;
            }
        }
    }
}
