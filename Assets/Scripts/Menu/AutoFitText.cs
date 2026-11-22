using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IronFauna.Menu
{
    /// <summary>
    /// Single-line text that picks the largest font size (between minSize and maxSize) whose rendered width and
    /// height both fit inside its RectTransform. Re-fits whenever the rect resizes or the text changes, so labels
    /// never spill past their button or panel at any window size.
    /// </summary>
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public class AutoFitText : UIBehaviour
    {
        public int maxSize = 24;
        public int minSize = 10;
        [Tooltip("Fraction of the rect usable for text (leaves breathing room at the edges).")]
        [Range(0.5f, 1f)] public float fill = 0.92f;

        private Text _text;
        private string _lastText;
        private Vector2 _lastSize;

        private Text TextComp => _text != null ? _text : (_text = GetComponent<Text>());

        protected override void OnEnable()
        {
            base.OnEnable();
            Fit();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) Fit();
        }

        private void LateUpdate()
        {
            var t = TextComp;
            if (t == null) return;
            if (!ReferenceEquals(t.text, _lastText) || ((RectTransform)transform).rect.size != _lastSize) Fit();
        }

        public void Fit()
        {
            var t = TextComp;
            if (t == null) return;
            var rt = (RectTransform)transform;
            Vector2 size = rt.rect.size;
            _lastText = t.text;
            _lastSize = size;
            if (size.x <= 1f || size.y <= 1f || string.IsNullOrEmpty(t.text) || t.font == null) return;

            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            float availW = size.x * fill;
            float availH = size.y * fill;
            float ppu = Mathf.Max(0.0001f, t.pixelsPerUnit);
            var gen = t.cachedTextGeneratorForLayout;

            int lo = Mathf.Max(1, minSize), hi = Mathf.Max(lo, maxSize), best = lo;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var settings = t.GetGenerationSettings(Vector2.zero);
                settings.fontSize = mid;   // the generator applies scaleFactor (= pixelsPerUnit) itself
                float w = gen.GetPreferredWidth(t.text, settings) / ppu;
                float h = gen.GetPreferredHeight(t.text, settings) / ppu;
                if (w <= availW && h <= availH) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            if (t.fontSize != best) t.fontSize = best;
        }
    }
}
