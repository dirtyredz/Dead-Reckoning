using System;
using System.Reflection;
using Chicken.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// Adds a Track control to the NPC daily-activity icon row, wired to Dead Reckoning's tracker.
    /// Hooks <see cref="RelationshipDailyActivitiesWidget"/>.Setup — the shared component that builds
    /// the chat/gift/affection/nokturna icons — so the control appears everywhere that row does: the
    /// Relationships list AND the character screen. The control is a bundled pin icon
    /// (<c>track-icon.png</c>, next to the DLL) dropped right after the Gift icon; it renders in a
    /// larger child so it matches the other icons' weight, turns green while tracking, and scales on
    /// hover. Purely additive — nothing in the existing tracking path is touched.
    /// </summary>
    [HarmonyPatch(typeof(RelationshipDailyActivitiesWidget), "Setup")]
    internal static class RelationshipDailyActivitiesWidgetSetupPatch
    {
        private static void Postfix(RelationshipDailyActivitiesWidget __instance, NpcConfigAsset config)
        {
            try { RelationshipTrackButton.Attach(__instance, config); }
            catch (Exception e) { DeadReckoningPlugin.Log.LogWarning($"Track button attach failed: {e.Message}"); }
        }
    }

    internal static class RelationshipTrackButton
    {
        private const string ButtonName = "DR_TrackButton";
        private const float SlotSize = 34f;    // layout slot: matches the card's daily-activity icons
        private const float PinSize = 50f;      // list-row pin (renders bigger than its slot: glow padding)
        private const float CharPinSize = 64f;  // character-screen pin, sized to match the "Daily X" icons
        // Full brightness in both states — the SELECTION is shown by a colour change, not dimming.
        private static readonly Color IdleTint = Color.white;                        // natural (orange) pin
        private static readonly Color TrackedTint = new Color(0.42f, 1f, 0.55f, 1f); // bright green = tracking

        private static readonly FieldInfo GiftField =
            AccessTools.Field(typeof(RelationshipDailyActivitiesWidget), "dailyGiftWidget");

        /// <summary>Re-apply state to every Track button (after the tracked NPC changes).</summary>
        internal static void RefreshAll()
        {
            foreach (DRTrackRef r in UnityEngine.Object.FindObjectsByType<DRTrackRef>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                try
                {
                    bool tracked = SkullGuide.Instance != null && SkullGuide.Instance.IsTracked(r.Cfg);
                    ApplyState(r.Icon, r.Label, tracked);
                }
                catch { }
            }
        }

        internal static void Attach(RelationshipDailyActivitiesWidget widget, NpcConfigAsset cfg)
        {
            if (widget == null || cfg == null) return;

            Component gift = GiftField?.GetValue(widget) as Component;
            Transform parent = gift != null ? gift.transform.parent : widget.transform; // the daily-icon row
            if (parent == null) return;

            // The list card carries a RelationshipListWidget; the character screen's row does not. Only
            // the roomier character screen gets a "Track/Tracking" word beside the icon.
            bool withText = widget.GetComponentInParent<RelationshipListWidget>() == null;

            Transform existing = DRUi.FindDeep(parent, ButtonName);
            DRTrackRef refc = existing != null
                ? existing.GetComponent<DRTrackRef>()
                : Build(parent, gift != null ? gift.transform : null, withText);
            if (refc == null || refc.Button == null) return;

            refc.Cfg = cfg;
            bool tracked = SkullGuide.Instance != null && SkullGuide.Instance.IsTracked(cfg);
            ApplyState(refc.Icon, refc.Label, tracked);

            NpcConfigAsset captured = cfg;
            refc.Button.onClick.RemoveAllListeners();
            refc.Button.onClick.AddListener(() =>
            {
                SkullGuide.Instance?.ToggleTrack(captured);
                RefreshAll();
            });
        }

        /// <summary>Colour is the on/off cue on the pin; both states stay at full brightness. The label
        /// only changes its text — a cloned row keeps the game's native label colour; the fallback
        /// plate (no pin) tints its own text since it has no icon to signal state.</summary>
        private static void ApplyState(Image icon, TextMeshProUGUI label, bool tracked)
        {
            bool hasPin = icon != null && icon.sprite != null;
            if (hasPin) icon.color = tracked ? TrackedTint : IdleTint;
            if (label != null)
            {
                label.text = tracked ? "Seeking" : "Seek";
                if (!hasPin) label.color = tracked ? TrackedTint : Color.white; // fallback plate only
            }
        }

        private static DRTrackRef Build(Transform parent, Transform gift, bool charScreen)
        {
            // The character screen is a labelled vertical list ("Daily Gift" etc.). Clone that native
            // row so our entry inherits its layout, font, colour and icon size; the cramped Relationships
            // list is just a small icon in the horizontal row.
            if (charScreen && gift != null)
            {
                DRTrackRef cloned = TryCloneRow(gift, parent);
                if (cloned != null) return cloned;
            }
            return BuildIcon(parent, gift);
        }

        /// <summary>The Relationships-list control: a small pin icon in the horizontal daily-icon row.</summary>
        private static DRTrackRef BuildIcon(Transform parent, Transform gift)
        {
            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = SlotSize;
            le.preferredHeight = le.minHeight = SlotSize;

            var root = go.GetComponent<Image>();
            root.color = new Color(0f, 0f, 0f, 0f);
            root.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = root;

            var refc = go.AddComponent<DRTrackRef>();
            refc.Button = btn;

            Transform hoverTarget;
            Sprite sprite = TrackIcon.Get();
            if (sprite != null)
            {
                var pinGo = new GameObject("Pin", typeof(RectTransform), typeof(Image));
                var prt = (RectTransform)pinGo.transform;
                prt.SetParent(go.transform, false);
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.zero;
                prt.sizeDelta = new Vector2(PinSize, PinSize);
                var img = pinGo.GetComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = IdleTint;
                refc.Icon = img;
                hoverTarget = pinGo.transform;
            }
            else
            {
                root.color = new Color(0.12f, 0.08f, 0.18f, 0.55f);
                refc.Icon = root;
                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                var trt = (RectTransform)txtGo.transform;
                trt.SetParent(go.transform, false);
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                var label = txtGo.GetComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 9f;
                label.raycastTarget = false;
                CopyFont(FindFont(parent), label);
                refc.Label = label;
                hoverTarget = txtGo.transform;
            }

            go.AddComponent<HoverScale>().Target = hoverTarget;
            if (gift != null && gift.parent == parent) rt.SetSiblingIndex(gift.GetSiblingIndex() + 1);
            return refc;
        }

        /// <summary>The character-screen control: clone the native "Daily Gift" row, swap its icon for
        /// our pin (at the same rect) and its label text for "Track", and make the whole row clickable.
        /// Returns null (caller falls back to the plain icon) if anything about the clone goes wrong.</summary>
        private static DRTrackRef TryCloneRow(Transform gift, Transform parent)
        {
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(gift.gameObject, parent, false);
                clone.name = ButtonName;
                clone.SetActive(true);
                clone.transform.SetSiblingIndex(gift.GetSiblingIndex() + 1);

                // Stop the native widget driving the icon/label from completion state.
                var driver = clone.GetComponent<RelationshipDailyActivityWidget>();
                if (driver != null) UnityEngine.Object.Destroy(driver);

                // Replace the native addressable icon(s) with our pin at the same rect.
                Image iconModel = null;
                Transform iconParent = clone.transform;
                foreach (Image im in clone.GetComponentsInChildren<Image>(true))
                {
                    if (im.GetType().Name != "ImageForAddressable") continue;
                    if (iconModel == null) { iconModel = im; iconParent = im.transform.parent; }
                    im.gameObject.SetActive(false);
                }

                Image pin = null;
                Sprite sprite = TrackIcon.Get();
                if (sprite != null)
                {
                    // Centre a larger pin in the native icon slot — the PNG's glow padding makes a
                    // tight-fit pin read smaller than the game's icons, so we oversize it.
                    var pinGo = new GameObject("Pin", typeof(RectTransform), typeof(Image));
                    var prt = (RectTransform)pinGo.transform;
                    prt.SetParent(iconParent, false);
                    prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                    prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.anchoredPosition = Vector2.zero;
                    prt.sizeDelta = new Vector2(CharPinSize, CharPinSize);
                    pin = pinGo.GetComponent<Image>();
                    // Raycast-on so hovering/clicking the icon itself (it overflows the row) bubbles up
                    // to the row's Button/HoverScale.
                    pin.sprite = sprite; pin.preserveAspect = true; pin.raycastTarget = true; pin.color = IdleTint;
                }

                // Keep the native label styling; just take over its text (drop any localiser that would
                // overwrite it).
                TextMeshProUGUI label = null;
                foreach (TextMeshProUGUI t in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (label == null || t.gameObject.activeInHierarchy) label = t;
                    if (t.gameObject.activeInHierarchy) break;
                }
                if (label != null)
                {
                    foreach (Component c in label.GetComponents<Component>())
                        if (c != null && c.GetType().Name == "LocalizedTextField") UnityEngine.Object.Destroy(c);
                }

                // Make the whole ROW the button/hover source (an ancestor of icon + label), with a
                // transparent raycast graphic covering it. Pointer events on the icon (raycast-on) or
                // the label bubble up here, so hover enlarges the pin and clicks toggle — anywhere.
                Image rowImg = clone.GetComponent<Image>();
                if (rowImg == null) rowImg = clone.AddComponent<Image>();
                rowImg.color = new Color(0f, 0f, 0f, 0f);
                rowImg.raycastTarget = true;
                var btn = clone.AddComponent<Button>();
                btn.targetGraphic = rowImg;

                var refc = clone.AddComponent<DRTrackRef>();
                refc.Button = btn;
                refc.Icon = pin;
                refc.Label = label;
                if (pin != null) clone.AddComponent<HoverScale>().Target = pin.transform;
                return refc;
            }
            catch (Exception e)
            {
                DeadReckoningPlugin.Log.LogWarning($"Track row clone failed, using plain icon: {e.Message}");
                return null;
            }
        }

        private static TextMeshProUGUI FindFont(Transform ctx)
        {
            return ctx != null ? ctx.root.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        }

        private static void CopyFont(TextMeshProUGUI src, TextMeshProUGUI dst)
        {
            if (src == null || dst == null) return;
            if (src.font != null) dst.font = src.font;
            if (src.fontSharedMaterial != null) dst.fontSharedMaterial = src.fontSharedMaterial;
        }
    }

    /// <summary>Per-button state so the tracked NPC can be re-resolved on refresh.</summary>
    internal sealed class DRTrackRef : MonoBehaviour
    {
        internal NpcConfigAsset Cfg;
        internal Button Button;
        internal Image Icon;
        internal TextMeshProUGUI Label;
    }
}
