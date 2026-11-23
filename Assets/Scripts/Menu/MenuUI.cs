using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronFauna.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace IronFauna.Menu
{
    /// <summary>
    /// Small uGUI construction kit so the title screen and garage can be built entirely from code
    /// (no prefabs or TextMeshPro import needed). Colours come from IronFaunaPalette only.
    /// </summary>
    public static class MenuUI
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2400f, 1350f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        public static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors a rect: anchors in 0..1 screen space, offsets in reference pixels.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static RectTransform Fill(RectTransform rt)
        {
            return Place(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = NewRect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var rt = NewRect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button CreateButton(Transform parent, string label, int fontSize, Color normal, Color hover, Color textColor, UnityAction onClick)
        {
            var rt = NewRect(parent, "Button_" + label);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = IronFaunaPalette.UiNeutral;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.pressedColor = IronFaunaPalette.Lerp(hover, IronFaunaPalette.UiPanel, 0.25f);
            colors.selectedColor = normal;
            colors.disabledColor = IronFaunaPalette.WithAlpha(normal, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);
            string upper = label.ToUpperInvariant();
            rt.gameObject.AddComponent<UiSoundHook>().isBack = upper == "BACK" || upper.StartsWith("BACK ") || upper.StartsWith("< BACK");

            var text = Label(rt, label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Fill(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(10f, 0f);
            text.rectTransform.offsetMax = new Vector2(-10f, 0f);
            Fit(text, 10);
            return btn;
        }

        /// <summary>Makes a single-line label shrink (never grow past its authored size) to fit its rect.</summary>
        public static AutoFitText Fit(Text text, int minSize = 10, float fill = 0.92f)
        {
            var fit = text.GetComponent<AutoFitText>();
            // Read the authored size BEFORE adding the component: AddComponent runs OnEnable -> Fit() at once,
            // against the label's not-yet-laid-out placeholder rect, which would shrink the font first.
            int authored = fit != null ? fit.maxSize : text.fontSize;
            if (fit == null) fit = text.gameObject.AddComponent<AutoFitText>();
            fit.maxSize = authored;
            fit.minSize = Mathf.Min(minSize, authored);
            fit.fill = fill;
            fit.Fit();
            return fit;
        }

        /// <summary>A layout child that soaks up spare height (flexible) or adds a fixed gap.</summary>
        public static RectTransform Spacer(Transform parent, float height, bool flexible = false)
        {
            var rt = NewRect(parent, flexible ? "FlexSpacer" : "Gap");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = flexible ? 0f : height;
            le.preferredHeight = height;
            le.flexibleHeight = flexible ? 1f : 0f;
            return rt;
        }

        public static LayoutElement Size(Component c, float preferredHeight, float preferredWidth = -1f, float flexibleWidth = -1f)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.minHeight = preferredHeight;
            // Rows are HorizontalLayoutGroups with childForceExpandHeight, which report flexibleHeight = 1;
            // pin it to 0 so vertical stacks don't stretch rows into the spare space.
            le.flexibleHeight = 0f;
            if (preferredWidth >= 0f)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }
            if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static VerticalLayoutGroup VStack(GameObject go, float spacing, int padding)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperLeft;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing, int padding)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        /// <summary>"Label  [&lt;]  value  [&gt;]" row used for every part slot, paint and stance.</summary>
        public sealed class Cycler
        {
            public Text Value;
            public Button Prev;
            public Button Next;
            public GameObject Root;

            public void SetInteractable(bool on)
            {
                Prev.interactable = on;
                Next.interactable = on;
                Value.color = on ? IronFaunaPalette.UiText : IronFaunaPalette.WithAlpha(IronFaunaPalette.UiText, 0.35f);
            }
        }

        public static Cycler CreateCycler(Transform parent, string label, UnityAction onPrev, UnityAction onNext)
        {
            var row = NewRect(parent, "Cycler_" + label);
            HStack(row.gameObject, 8f, 0);
            Size(row, 46f);

            var bg = row.gameObject.AddComponent<Image>();
            bg.color = IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.55f);

            var name = Label(row, label.ToUpperInvariant(), 20, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            var nameLe = Size(name, 46f, 190f, 0.6f);
            nameLe.minWidth = 70f;   // shrinks with the panel; AutoFitText scales the words down
            Fit(name, 10, 0.96f);

            Color btnColor = IronFaunaPalette.UiButton;
            Color btnHover = IronFaunaPalette.UiSelect;
            var prev = CreateButton(row, "<", 24, btnColor, btnHover, IronFaunaPalette.UiText, onPrev);
            Size(prev, 46f, 52f);
            var value = Label(row, "-", 22, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            Size(value, 46f, 220f, 1f).minWidth = 90f;
            Fit(value, 10);
            var next = CreateButton(row, ">", 24, btnColor, btnHover, IronFaunaPalette.UiText, onNext);
            Size(next, 46f, 52f);

            return new Cycler { Value = value, Prev = prev, Next = next, Root = row.gameObject };
        }

        /// <summary>Horizontal stat bar; returns the fill rect so callers can set anchorMax.x.</summary>
        public static RectTransform StatBar(Transform parent, string label, out Text valueText)
        {
            var row = NewRect(parent, "Stat_" + label);
            Size(row, 34f);

            var name = Label(row, label.ToUpperInvariant(), 18, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(0.32f, 1f), Vector2.zero, Vector2.zero);
            Fit(name, 10, 0.96f);

            var track = Panel(row, "Track", IronFaunaPalette.DarkIron);
            Place(track.rectTransform, new Vector2(0.32f, 0.25f), new Vector2(0.84f, 0.75f), Vector2.zero, Vector2.zero);
            var fill = Panel(track.transform, "Fill", IronFaunaPalette.UiAccent);
            Place(fill.rectTransform, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);

            valueText = Label(row, "0", 18, IronFaunaPalette.UiText, TextAnchor.MiddleRight, FontStyle.Bold);
            Place(valueText.rectTransform, new Vector2(0.84f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            Fit(valueText, 10, 0.96f);
            return fill.rectTransform;
        }
    }
}
