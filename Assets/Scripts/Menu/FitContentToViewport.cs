using UnityEngine;
using UnityEngine.UI;

namespace IronFauna.Menu
{
    /// <summary>
    /// Sits on a viewport rect and drives one child "content" rect that holds a VerticalLayoutGroup.
    /// The content always fills the viewport; when the stack's preferred height is taller than the viewport
    /// (short windows), the whole stack is scaled down uniformly so nothing is cut off or overlaps.
    /// Flexible spacers inside the stack absorb any spare height on tall windows.
    /// </summary>
    [DisallowMultipleComponent]
    public class FitContentToViewport : MonoBehaviour
    {
        public RectTransform content;
        [Range(0.2f, 1f)] public float minScale = 0.35f;

        private void Awake() => Configure();

        public void Configure()
        {
            if (content == null) return;
            content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (content == null) return;
            Vector2 avail = ((RectTransform)transform).rect.size;
            if (avail.x <= 1f || avail.y <= 1f) return;

            float pref = LayoutUtility.GetPreferredHeight(content);
            float s = pref > avail.y ? Mathf.Max(minScale, avail.y / pref) : 1f;

            var scale = new Vector3(s, s, 1f);
            var size = new Vector2(avail.x / s, avail.y / s);
            if (content.localScale != scale) content.localScale = scale;
            if (content.sizeDelta != size) content.sizeDelta = size;
            if (content.anchoredPosition != Vector2.zero) content.anchoredPosition = Vector2.zero;
        }
    }
}
