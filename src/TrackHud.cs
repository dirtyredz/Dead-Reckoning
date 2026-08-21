using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// A small fixed status window (top-left) showing what the skull is currently tracking. Also
    /// serves as the confirmation when you track a house from the map — it updates immediately.
    /// Screen-space overlay; uses the game's Gelica font so it reads native.
    /// </summary>
    internal sealed class TrackHud
    {
        private Canvas canvas;
        private CanvasGroup group;
        private TextMeshProUGUI label;

        private static TMP_FontAsset gelica;
        private static bool fontSearched;

        internal void Set(string text)
        {
            Ensure();
            label.text = text;
            group.alpha = 1f;
        }

        internal void Hide()
        {
            if (group != null) group.alpha = 0f;
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
            go.AddComponent<CanvasScaler>();
            group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(go.transform, false);
            var prt = panelGo.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(16f, -16f);
            prt.sizeDelta = new Vector2(300f, 34f);
            var img = panelGo.AddComponent<Image>();
            img.color = new Color(0.08f, 0.05f, 0.12f, 0.72f);
            img.raycastTarget = false;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(panelGo.transform, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(12f, 2f);
            lrt.offsetMax = new Vector2(-12f, -2f);
            label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Left;
            label.fontSize = 18f;
            label.color = new Color(0.85f, 0.9f, 1f);
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            TMP_FontAsset f = Font();
            if (f != null) label.font = f;

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
