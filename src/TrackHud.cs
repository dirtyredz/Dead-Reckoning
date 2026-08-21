using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// On-screen text of what the skull is currently tracking. Also serves as the confirmation when you
    /// track a house from the map — it updates immediately. Screen-space overlay, no background panel;
    /// uses the game's Gelica font with a dark outline so it stays readable over any scene. Position
    /// (normalised, measured down from the top) and font size come from config and are applied live.
    /// </summary>
    internal sealed class TrackHud
    {
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform panel;
        private TextMeshProUGUI label;
        private GameObject closeButton;

        /// <summary>Invoked when the player clicks the ✕ next to the overlay (stop seeking).</summary>
        internal Action OnClose;

        private static TMP_FontAsset gelica;
        private static bool fontSearched;

        internal void Set(string text) => Set(text, DeadReckoningPlugin.HudFontSize.Value);

        internal void Set(string text, float fontSize)
        {
            Ensure();
            label.text = text;
            label.fontSize = fontSize;

            // Live position: anchor at the configured normalised screen point (Y measured from the top).
            float x = Mathf.Clamp01(DeadReckoningPlugin.HudPosX.Value);
            float yFromTop = Mathf.Clamp01(DeadReckoningPlugin.HudPosY.Value);
            panel.anchorMin = panel.anchorMax = new Vector2(x, 1f - yFromTop);
            // Pivot at the top edge so the list top sits at the anchor and flows DOWN; pivot x follows the
            // horizontal edge so a right-side position grows leftward instead of off-screen.
            panel.pivot = new Vector2(x, 1f);
            // Nudge in from the very edge so text isn't clipped against the screen border.
            panel.anchoredPosition = new Vector2((0.5f - x) * 24f, 0f);

            group.alpha = 1f;
            group.blocksRaycasts = true; // so the ✕ is clickable
        }

        internal void Hide()
        {
            if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; }
        }

        internal void Destroy()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
        }

        private void Ensure()
        {
            if (canvas != null) return;

            var go = new GameObject("DeadReckoning_TrackHud");
            UnityEngine.Object.DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>(); // required for the ✕ to receive clicks/hover
            group = go.AddComponent<CanvasGroup>();
            group.interactable = true; // else the ✕ Button won't fire onClick
            group.blocksRaycasts = false;

            var panelGo = new GameObject("Label");
            panelGo.transform.SetParent(go.transform, false);
            panel = panelGo.AddComponent<RectTransform>();
            panel.sizeDelta = new Vector2(560f, 600f); // tall: the quest objectives list grows downward

            label = panelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.TopLeft; // multi-line list flows down from the anchor
            label.fontSize = DeadReckoningPlugin.HudFontSize.Value;
            label.color = Color.white; // base white; gold is applied per-span via rich-text tags
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = new Vector4(34f, 0f, 0f, 0f); // leave room for the ✕ at the top-left

            TMP_FontAsset f = Font();
            if (f != null)
            {
                label.font = f;
                // Own material instance so the outline below doesn't bleed onto the game's shared
                // Gelica material (which would outline every Gelica text in the game).
                if (f.material != null) label.fontMaterial = new Material(f.material);
            }

            // Dark outline so the text reads over any background now that there's no panel behind it.
            label.outlineColor = new Color(0f, 0f, 0f, 1f);
            label.outlineWidth = 0.22f;

            // A clickable X at the overlay's top-left to stop seeking (drawn, not a font glyph).
            closeButton = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            var crt = (RectTransform)closeButton.transform;
            crt.SetParent(panelGo.transform, false);
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(0f, 0f);
            crt.sizeDelta = new Vector2(28f, 28f);
            var hit = closeButton.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f); // transparent, but catches the click
            hit.raycastTarget = true;
            GameObject xIcon = DRIcons.BuildX(closeButton.transform, 20f, new Color(1f, 0.5f, 0.5f));
            var xBtn = closeButton.GetComponent<Button>();
            xBtn.targetGraphic = hit;
            xBtn.onClick.AddListener(() => OnClose?.Invoke());
            // Scale the centred X icon (not the top-left-pivoted container) so hover grows from its centre.
            closeButton.AddComponent<HoverScale>().Target = xIcon.transform;

            group.alpha = 0f;
        }

        private static TMP_FontAsset Font()
        {
            if (fontSearched) return gelica;
            fontSearched = true;
            try
            {
                foreach (TMP_FontAsset a in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (a != null && a.name.IndexOf("Gelica", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        gelica = a;
                        break;
                    }
                }
            }
            catch { }
            return gelica;
        }
    }
}
