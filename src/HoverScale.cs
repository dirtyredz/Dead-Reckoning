using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadReckoning
{
    /// <summary>Smoothly scales a target up while the pointer is over this element — the hover animation.
    /// Shared by every Track/stop control (Relationships + character-screen buttons, the Quest Log
    /// button, the picker's Stop control, and the HUD ✕).</summary>
    internal sealed class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Transform Target;
        private const float HoverScaleAmount = 1.3f;
        private const float Speed = 14f;
        private float goal = 1f;
        private float current = 1f;

        public void OnPointerEnter(PointerEventData e) => goal = HoverScaleAmount;
        public void OnPointerExit(PointerEventData e) => goal = 1f;

        private void OnDisable()
        {
            goal = current = 1f;
            if (Target != null) Target.localScale = Vector3.one;
        }

        private void Update()
        {
            if (Target == null) return;
            current = Mathf.Lerp(current, goal, Time.unscaledDeltaTime * Speed);
            Target.localScale = Vector3.one * current;
        }
    }
}
