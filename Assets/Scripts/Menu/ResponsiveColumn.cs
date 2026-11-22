using UnityEngine;

namespace IronFauna.Menu
{
    /// <summary>
    /// Left-docked side column whose width follows the window: a share of the screen on wide/landscape windows
    /// (clamped between minWidth and maxWidth), and the full width on narrow or portrait windows.
    /// Expects anchors (0,0)-(0,1); drives offsetMin.x / offsetMax.x only.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResponsiveColumn : MonoBehaviour
    {
        public float widthFraction = 0.34f;
        public float minWidth = 600f;
        public float maxWidth = 860f;
        [Tooltip("Below this width/height aspect the column spans the whole window.")]
        public float fullWidthBelowAspect = 1.15f;

        private RectTransform _rt;
        private RectTransform _parent;

        private void LateUpdate()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            if (_parent == null) _parent = _rt.parent as RectTransform;
            if (_parent == null) return;

            Vector2 p = _parent.rect.size;
            if (p.x <= 1f || p.y <= 1f) return;

            float width = p.x / p.y < fullWidthBelowAspect
                ? p.x
                : Mathf.Min(p.x, Mathf.Clamp(p.x * widthFraction, minWidth, maxWidth));

            if (!Mathf.Approximately(_rt.offsetMax.x, width) || _rt.offsetMin.x != 0f)
            {
                _rt.offsetMin = new Vector2(0f, _rt.offsetMin.y);
                _rt.offsetMax = new Vector2(width, _rt.offsetMax.y);
            }
        }
    }
}
