using UnityEngine;
using UnityEngine.UI;

namespace DeadReckoning
{
    /// <summary>
    /// A marker on the map at the current free-pin spot: a solid red diamond with white "ping" waves
    /// (expanding, fading diamonds) radiating out of it so it's easy to spot. Positioned each frame
    /// from <see cref="MapWidget.GetUIPositionFromRoomPosition"/>, so it stays pinned as the map moves.
    /// </summary>
    internal sealed class MapPin
    {
        private const int RippleCount = 3;
        private const float PingSpeed = 0.7f;   // cycles/sec
        private const float BaseSize = 16f;
        private const float MaxRipple = 3.4f;   // ripple grows to this × base

        private GameObject go;
        private RectTransform rt;
        private readonly RectTransform[] ripples = new RectTransform[RippleCount];
        private readonly Image[] rippleImgs = new Image[RippleCount];

        internal void Refresh(MapWidget mw, RoomAsset room, Vector3 roomPos, Transform parent)
        {
            Ensure(parent);
            Vector3 pos = mw.GetUIPositionFromRoomPosition(roomPos, room);
            if (pos == Vector3.zero) { go.SetActive(false); return; } // room not on this map page
            rt.position = pos;

            for (int i = 0; i < RippleCount; i++)
            {
                float phase = Mathf.Repeat(Time.time * PingSpeed + (float)i / RippleCount, 1f);
                float scale = Mathf.Lerp(0.9f, MaxRipple, phase);
                float alpha = Mathf.Lerp(0.6f, 0f, phase);
                ripples[i].localScale = new Vector3(scale, scale, 1f);
                Color c = rippleImgs[i].color; c.a = alpha; rippleImgs[i].color = c;
            }

            go.SetActive(true);
        }

        internal void Hide() { if (go != null) go.SetActive(false); }
        internal void Destroy() { if (go != null) Object.Destroy(go); go = null; }

        private void Ensure(Transform parent)
        {
            if (go != null)
            {
                if (parent != null && rt.parent != parent) rt.SetParent(parent, worldPositionStays: false);
                return;
            }

            go = new GameObject("DeadReckoning_MapPin", typeof(RectTransform));
            rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, worldPositionStays: false);
            rt.sizeDelta = new Vector2(BaseSize, BaseSize);
            rt.localScale = Vector3.one;

            // White ripples first (rendered under the main diamond).
            for (int i = 0; i < RippleCount; i++)
            {
                ripples[i] = Diamond(rt, new Color(1f, 1f, 1f, 0f), BaseSize);
                rippleImgs[i] = ripples[i].GetComponent<Image>();
            }

            // Solid red diamond on top (with a dark rim for contrast).
            RectTransform rim = Diamond(rt, new Color(0.05f, 0.03f, 0.08f, 0.95f), BaseSize + 4f);
            RectTransform core = Diamond(rt, new Color(0.95f, 0.28f, 0.38f, 1f), BaseSize);
            rim.SetAsLastSibling();
            core.SetAsLastSibling();
        }

        private static RectTransform Diamond(Transform parent, Color color, float size)
        {
            var g = new GameObject("Diamond", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)g.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var img = g.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }
    }
}
