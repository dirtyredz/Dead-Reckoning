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

        internal static void Set(Component widget, bool on, bool circle = false)
        {
            if (widget == null) return;
            Transform host = widget.transform;
            var tint = host.GetComponent<DRMarkerTint>();

            if (!on)
            {
                if (tint != null) tint.SetOn(false);
                return;
            }

            if (tint == null) tint = Build(host, circle);
            tint.SetOn(true);
        }

        private static DRMarkerTint Build(Transform host, bool circle)
        {
            var t = host.gameObject.AddComponent<DRMarkerTint>();
            t.Circle = circle; // NPC icons are round → round ping; houses/pins → diamond

            // House compact plate + its little down-arrow.
            Transform houseBg = DRUi.FindDeep(host, "LayoutContainer")?.Find("Background");
            Add(t, houseBg);
            if (houseBg != null) Add(t, houseBg.Find("Arrow"));

            // NPC compact plate: the round icon background.
            AddChild(t, DRUi.FindDeep(host, "IconContainer"), "Background");

            // Name banner (both house and NPC have a NameplateWidget): its background + down-arrow.
            Transform nameplate = DRUi.FindDeep(host, "NameplateWidget");
            if (nameplate != null)
            {
                Transform nameBg = DRUi.FindDeep(nameplate, "Background");
                Add(t, nameBg);
                if (nameBg != null) Add(t, nameBg.Find("Arrow"));
            }

            // House expanded popup: ornamental frame, name banner, and its down-arrow.
            Transform popup = DRUi.FindDeep(host, "PopupWidget");
            if (popup != null)
            {
                Add(t, DRUi.FindDeep(popup, "Frame"));
                AddChild(t, DRUi.FindDeep(popup, "TitleContainer"), "Background");
                Transform popupBg = popup.Find("Background");
                if (popupBg != null) Add(t, popupBg.Find("Arrow"));
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
    }

    /// <summary>Holds the native marker graphics we recolour (kept solid purple as the base) plus a set
    /// of spreading "ping" waves centred on the marker — the same expanding/fading pulse the free pin
    /// uses — so a tracked marker draws the eye instead of sitting at one subtle shade. Colours and
    /// waves are driven each frame while tracked and cleanly reverted/hidden on untrack.</summary>
    internal sealed class DRMarkerTint : MonoBehaviour
    {
        private const int RippleCount = 3;
        private const float PingSpeed = 0.7f;  // cycles/sec, matching the free pin
        private const float BaseSize = 42f;     // ~ the marker icon; waves start around it
        private const float MaxRipple = 2.6f;   // grows to this × base
        private const float PingRaise = 36f;    // lift the ping onto the icon/tooltip, off the base anchor
        private static readonly Color Highlight = new Color(1f, 0.55f, 0.18f, 1f); // orange recolour of the badge
        private static readonly Color Wave = new Color(1f, 0.72f, 0.30f, 1f);      // brighter orange ping waves

        internal bool Circle; // round ping (NPC) vs diamond (house/pin)
        private static Sprite circleSprite;
        private static float nextCircleScan;

        private readonly List<Image> imgs = new List<Image>();
        private readonly List<Color> bases = new List<Color>();
        private readonly List<UIColorable> cols = new List<UIColorable>();

        private GameObject pingRoot;
        private readonly RectTransform[] ripples = new RectTransform[RippleCount];
        private readonly Image[] rippleImgs = new Image[RippleCount];
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
                    if (imgs[i] != null) imgs[i].color = Highlight; // solid orange base (re-asserted)

                EnsurePing();
                pingRoot.SetActive(true);
                for (int i = 0; i < RippleCount; i++)
                {
                    float phase = Mathf.Repeat(Time.time * PingSpeed + (float)i / RippleCount, 1f);
                    float scale = Mathf.Lerp(0.9f, MaxRipple, phase);
                    float alpha = Mathf.Lerp(0.55f, 0f, phase);
                    ripples[i].localScale = new Vector3(scale, scale, 1f);
                    Color c = rippleImgs[i].color; c.a = alpha; rippleImgs[i].color = c;
                }
                on = true;
            }
            else if (on)
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (imgs[i] != null) imgs[i].color = bases[i];
                    if (cols[i] != null) cols[i].ClearOverride(0f);
                }
                if (pingRoot != null) pingRoot.SetActive(false);
                on = false;
            }
        }

        /// <summary>Build the ping waves once, centred on the marker. Parented to VisibilityContainer
        /// (always alpha 1, and fades away with the marker when it leaves the map page) rather than the
        /// icon plate, whose CanvasGroup goes to alpha 0 when the popup expands.</summary>
        private void EnsurePing()
        {
            if (pingRoot != null) return;

            Transform parent = DRUi.FindDeep(transform, "VisibilityContainer") ?? transform;
            pingRoot = new GameObject("DR_MarkerPing", typeof(RectTransform));
            var prt = (RectTransform)pingRoot.transform;
            prt.SetParent(parent, worldPositionStays: false);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // NPC ping sits centred on the round icon; house/pin ping lifts onto the tooltip.
            prt.anchoredPosition = new Vector2(0f, Circle ? 0f : PingRaise);
            prt.localScale = Vector3.one;
            prt.SetAsFirstSibling(); // waves behind the marker art

            Sprite sprite = Circle ? CircleSprite() : null;
            for (int i = 0; i < RippleCount; i++)
            {
                ripples[i] = Wave2(prt, new Color(Wave.r, Wave.g, Wave.b, 0f), sprite);
                rippleImgs[i] = ripples[i].GetComponent<Image>();
            }
        }

        /// <summary>One wave: a hollow diamond (rotated box) for houses, or a ring using the game's Circle
        /// sprite for NPCs.</summary>
        private static RectTransform Wave2(Transform parent, Color color, Sprite circleSprite)
        {
            var g = new GameObject("Wave", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)g.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(BaseSize, BaseSize);
            rt.localRotation = Quaternion.Euler(0f, 0f, circleSprite != null ? 0f : 45f);
            var img = g.GetComponent<Image>();
            if (circleSprite != null) img.sprite = circleSprite; // round wave for NPCs
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            if (Time.unscaledTime < nextCircleScan) return null;
            nextCircleScan = Time.unscaledTime + 2f;
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && s.name == "Circle") { circleSprite = s; break; }
            return circleSprite;
        }
    }
}
