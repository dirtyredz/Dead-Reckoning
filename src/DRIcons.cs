using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>Tiny UI helpers shared across the mod.</summary>
    internal static class DRIcons
    {
        /// <summary>Draw an "X" from two crossed bars (no font glyph needed). Returns the container,
        /// centred in its parent; bars don't catch raycasts.</summary>
        internal static GameObject BuildX(Transform parent, float size, Color color)
        {
            var go = new GameObject("X", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);

            for (int i = 0; i < 2; i++)
            {
                var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
                var brt = (RectTransform)bar.transform;
                brt.SetParent(go.transform, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = Vector2.zero;
                brt.sizeDelta = new Vector2(size * 0.95f, Mathf.Max(2.5f, size * 0.16f));
                brt.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                var img = bar.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            return go;
        }
    }
}
