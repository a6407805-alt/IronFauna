using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Extra brawl HUD, built with MenuUI and coloured from IronFaunaPalette:
    ///  - a live keyboard + mouse diagram (bottom-right) whose keys light up while held,
    ///  - robot damage outlines for you and your current target (green healthy, yellow injured, red critical, grey gone),
    ///  - overhead health bars over every fighter's head (yours also shows stamina).
    /// </summary>
    public sealed class BrawlHudExtras
    {
        // ---------------------------------------------------------------- keyboard diagram
        private enum KeyKind { Move, Attack, Defend, Grapple, Utility }

        private sealed class KeyCap
        {
            public Image bg;
            public Text key, action;
            public KeyKind kind;
            public System.Func<bool> held;
            public float flash;   // keeps a quick tap visible for a moment
            public string normalAction;
            public string grappleAction;   // label while you are holding a robot (null = unchanged)
        }

        private Text _kbTitle;
        private bool _wasGrappling;

        private readonly List<KeyCap> _keys = new List<KeyCap>();

        // ---------------------------------------------------------------- robot outlines
        private sealed class Doll
        {
            public Text title, hull;
            public readonly Dictionary<RigPart, Image> parts = new Dictionary<RigPart, Image>();
            public GameObject root;
        }

        private Doll _myDoll, _targetDoll;

        // ---------------------------------------------------------------- overhead bars
        private sealed class OverheadBar
        {
            public RectTransform root;
            public RectTransform fill;
            public Image fillImg;
            public Text name;
            public GameObject staminaRoot;
            public RectTransform staminaFill;
            public Image staminaImg;
        }

        private readonly Dictionary<MechRagdoll, OverheadBar> _bars = new Dictionary<MechRagdoll, OverheadBar>();
        private Transform _root;

        public static Color StatusColor(float health01, bool gone)
        {
            if (gone) return IronFaunaPalette.UiStatusGone;
            if (health01 > 0.66f) return IronFaunaPalette.UiStatusGood;
            if (health01 > 0.33f) return IronFaunaPalette.UiStatusHurt;
            return IronFaunaPalette.UiStatusBad;
        }

        public void Build(Transform root)
        {
            _root = root;
            BuildKeyboard(root);
            BuildDolls(root);
        }

        // ================================================================= keyboard
        private void BuildKeyboard(Transform root)
        {
            const float u = 52f, gap = 4f, pad = 12f, header = 26f;
            const float cols = 8.6f, rows = 4f;
            float w = cols * u + pad * 2f, h = rows * u + pad * 2f + header;

            var panel = MenuUI.Panel(root, "KeyboardDiagram", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.86f));
            MenuUI.Place(panel.rectTransform, Vector2.zero, Vector2.zero, new Vector2(30f, 14f), new Vector2(30f + w, 14f + h));   // bottom-left
            var top = MenuUI.Panel(panel.transform, "TopRule", IronFaunaPalette.CyanEnergy);
            MenuUI.Place(top.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -4f), Vector2.zero);
            var title = MenuUI.Label(panel.transform, NormalTitle, 13, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(pad, -header), new Vector2(-pad, -6f));
            title.supportRichText = true;
            MenuUI.Fit(title, 8, 0.98f);
            _kbTitle = title;

            var area = MenuUI.NewRect(panel.transform, "Keys");
            MenuUI.Place(area, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -header - 4f));

            // (label, action, x, row, width, height, kind, held)
            var kb = (System.Func<Keyboard>)(() => Keyboard.current);
            var ms = (System.Func<Mouse>)(() => Mouse.current);
            void K(string label, string action, float x, float row, float wu, float hu, KeyKind kind, System.Func<bool> held, string grappleAction = null)
            {
                var rt = MenuUI.NewRect(area, "Key_" + label);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x * u, -row * u);
                rt.sizeDelta = new Vector2(wu * u - gap, hu * u - gap);
                var bg = rt.gameObject.AddComponent<Image>();
                bg.color = IronFaunaPalette.UiButton;
                var edge = MenuUI.Panel(rt, "Edge", KindColor(kind));
                MenuUI.Place(edge.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));

                var key = MenuUI.Label(rt, label, 19, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Place(key.rectTransform, new Vector2(0f, 0.38f), Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, -2f));
                MenuUI.Fit(key, 10);
                var act = MenuUI.Label(rt, action, 10, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Place(act.rectTransform, Vector2.zero, new Vector2(1f, 0.40f), new Vector2(3f, 5f), new Vector2(-3f, 0f));
                MenuUI.Fit(act, 7);
                _keys.Add(new KeyCap { bg = bg, key = key, action = act, kind = kind, held = held, normalAction = action, grappleAction = grappleAction });
            }

            // row 0
            K("Q", "HOOK", 1f, 0f, 1f, 1f, KeyKind.Attack, () => kb() != null && kb().qKey.isPressed, "CHOKESLAM");
            K("W", "FORWARD", 2f, 0f, 1f, 1f, KeyKind.Move, () => kb() != null && kb().wKey.isPressed);
            K("E", "UPPERCUT", 3f, 0f, 1f, 1f, KeyKind.Attack, () => kb() != null && kb().eKey.isPressed, "POWERBOMB");
            K("R", "ROUNDHOUSE", 4f, 0f, 1f, 1f, KeyKind.Attack, () => kb() != null && kb().rKey.isPressed, "GIANT SWING");
            // row 1
            K("A", "LEFT", 1.25f, 1f, 1f, 1f, KeyKind.Move, () => kb() != null && kb().aKey.isPressed);
            K("S", "BACK", 2.25f, 1f, 1f, 1f, KeyKind.Move, () => kb() != null && kb().sKey.isPressed);
            K("D", "RIGHT", 3.25f, 1f, 1f, 1f, KeyKind.Move, () => kb() != null && kb().dKey.isPressed);
            K("F", "FRONT KICK", 4.25f, 1f, 1f, 1f, KeyKind.Attack, () => kb() != null && kb().fKey.isPressed, "PILEDRIVER");
            K("G", "GRAB (HOLD)", 5.25f, 1f, 1.35f, 1f, KeyKind.Grapple, () => kb() != null && kb().gKey.isPressed, "LET GO = THROW");
            // row 2
            K("SHIFT", "GUARD / PARRY", 0f, 2f, 1.75f, 1f, KeyKind.Defend, () => kb() != null && kb().leftShiftKey.isPressed);
            K("Z", "FLOOR SIT", 1.75f, 2f, 1f, 1f, KeyKind.Utility, () => kb() != null && kb().zKey.isPressed, "DRAG ON MAT");
            K("X", "CROUCH", 2.75f, 2f, 1f, 1f, KeyKind.Defend, () => kb() != null && kb().xKey.isPressed, "SUPLEX STANCE");
            K("C", "DODGE", 3.75f, 2f, 1f, 1f, KeyKind.Defend, () => kb() != null && kb().cKey.isPressed);
            K("V", "LOW KICK", 4.75f, 2f, 1f, 1f, KeyKind.Attack, () => kb() != null && kb().vKey.isPressed);
            // row 3
            K("CTRL", "SPRINT", 0f, 3f, 1.5f, 1f, KeyKind.Move, () => kb() != null && kb().leftCtrlKey.isPressed);
            K("SPACE", "SPECIAL MOVE", 2f, 3f, 3.75f, 1f, KeyKind.Attack, () => kb() != null && kb().spaceKey.isPressed, "SLAM  (CROUCHED = SUPLEX)");
            // mouse
            K("LMB", "JAB", 6.6f, 0f, 1f, 1.5f, KeyKind.Attack, () => ms() != null && ms().leftButton.isPressed, "THROW");
            K("RMB", "CROSS", 7.6f, 0f, 1f, 1.5f, KeyKind.Attack, () => ms() != null && ms().rightButton.isPressed, "TEAR LIMB");
            K("WHEEL", "PICK SPECIAL", 6.6f, 1.5f, 2f, 0.9f, KeyKind.Utility,
                () => ms() != null && Mathf.Abs(ms().scroll.ReadValue().y) > 0.01f);

            // legend
            var legend = MenuUI.Label(area, "MOVE    ATTACK    DEFEND / COUNTER    GRAPPLE", 10, IronFaunaPalette.UiTextDim, TextAnchor.LowerLeft, FontStyle.Bold);
            var lrt = legend.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            lrt.anchoredPosition = new Vector2(6.6f * u, -2.5f * u);
            lrt.sizeDelta = new Vector2(2f * u - gap, 1.4f * u);
            legend.horizontalOverflow = HorizontalWrapMode.Wrap;
            legend.supportRichText = true;
            legend.text = $"<color=#{Hex(KindColor(KeyKind.Move))}>■ MOVE</color>\n<color=#{Hex(KindColor(KeyKind.Attack))}>■ ATTACK</color>\n" +
                          $"<color=#{Hex(KindColor(KeyKind.Defend))}>■ DEFEND</color>\n<color=#{Hex(KindColor(KeyKind.Grapple))}>■ GRAPPLE</color>";
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private static string NormalTitle =>
            $"CONTROLS  //  <color=#{Hex(IronFaunaPalette.CrimsonFront)}>HOLD G NEAR A ROBOT TO GRAB IT</color>";
        private static string GrappleTitle =>
            $"<color=#{Hex(IronFaunaPalette.CrimsonFront)}>GRAPPLING!</color>  E POWERBOMB · Q CHOKESLAM · F PILEDRIVER · R GIANT SWING · LMB THROW · RMB TEAR";

        private static Color KindColor(KeyKind k)
        {
            switch (k)
            {
                case KeyKind.Move: return IronFaunaPalette.CyanEnergy;
                case KeyKind.Attack: return IronFaunaPalette.OrangeJointCore;
                case KeyKind.Defend: return IronFaunaPalette.HazardYellow;
                case KeyKind.Grapple: return IronFaunaPalette.CrimsonFront;
                default: return IronFaunaPalette.UiAccent;
            }
        }

        // ================================================================= robot outlines
        private void BuildDolls(Transform root)
        {
            var panel = MenuUI.Panel(root, "DamagePanel", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.86f));
            MenuUI.Place(panel.rectTransform, new Vector2(0.02f, 1f), new Vector2(0.18f, 1f), new Vector2(0f, -250f), new Vector2(0f, -20f));
            var top = MenuUI.Panel(panel.transform, "TopRule", IronFaunaPalette.UiStatusGood);
            MenuUI.Place(top.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -4f), Vector2.zero);

            _myDoll = MakeDoll(panel.transform, "YOU", 0f, 0.5f);
            _targetDoll = MakeDoll(panel.transform, "TARGET", 0.5f, 1f);

            var legend = MenuUI.Label(panel.transform, "", 11, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            legend.supportRichText = true;
            legend.text = $"<color=#{Hex(IronFaunaPalette.UiStatusGood)}>■ OK</color>  <color=#{Hex(IronFaunaPalette.UiStatusHurt)}>■ HURT</color>  " +
                          $"<color=#{Hex(IronFaunaPalette.UiStatusBad)}>■ BAD</color>  <color=#{Hex(IronFaunaPalette.UiStatusGone)}>■ GONE</color>";
            MenuUI.Place(legend.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(6f, 4f), new Vector2(-6f, 22f));
        }

        private Doll MakeDoll(Transform parent, string title, float x0, float x1)
        {
            var d = new Doll();
            var area = MenuUI.NewRect(parent, "Doll_" + title);
            MenuUI.Place(area, new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(6f, 26f), new Vector2(-6f, -8f));
            d.root = area.gameObject;

            d.title = MenuUI.Label(area, title, 13, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(d.title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -20f), Vector2.zero);
            MenuUI.Fit(d.title, 10);
            d.hull = MenuUI.Label(area, "", 11, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(d.hull.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -36f), new Vector2(0f, -20f));
            MenuUI.Fit(d.hull, 9);

            // body silhouette inside a fixed-aspect box (on-screen left = the robot's left side)
            // the fitter sizes "Body" inside "BodyArea", so the silhouette never overlaps the title / hull text
            var bodyArea = MenuUI.NewRect(area, "BodyArea");
            MenuUI.Place(bodyArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -40f));
            var body = MenuUI.NewRect(bodyArea, "Body");
            var fit = body.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 0.72f;

            Part(d, body, RigPart.Head, 0.38f, 0.82f, 0.62f, 1.00f);
            Part(d, body, RigPart.Hull, 0.28f, 0.42f, 0.72f, 0.80f);
            Part(d, body, RigPart.ArmL, 0.06f, 0.36f, 0.25f, 0.80f);
            Part(d, body, RigPart.ArmR, 0.75f, 0.36f, 0.94f, 0.80f);
            Part(d, body, RigPart.LegL, 0.29f, 0.00f, 0.48f, 0.40f);
            Part(d, body, RigPart.LegR, 0.52f, 0.00f, 0.71f, 0.40f);
            return d;
        }

        private static void Part(Doll d, RectTransform body, RigPart p, float x0, float y0, float x1, float y1)
        {
            var frame = MenuUI.Panel(body, "Frame_" + p, IronFaunaPalette.ShadowSlate);
            MenuUI.Place(frame.rectTransform, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
            var fill = MenuUI.Panel(frame.transform, "Fill", IronFaunaPalette.UiStatusGood);
            MenuUI.Place(fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            d.parts[p] = fill;
        }

        private static void UpdateDoll(Doll d, MechRagdoll r, string title)
        {
            if (d == null) return;
            d.root.SetActive(r != null);
            if (r == null) return;
            d.title.text = title;
            float hull = Mathf.Clamp01(r.health / Mathf.Max(1f, r.maxHealth));
            d.hull.text = r.IsKO ? "KNOCKED OUT" : $"HULL {Mathf.RoundToInt(hull * 100f)}%";
            d.hull.color = StatusColor(hull, r.IsKO);
            foreach (var kv in d.parts)
            {
                if (kv.Key == RigPart.Hull) { kv.Value.color = StatusColor(hull, r.IsKO); continue; }
                LimbId id = TournamentGarage.ToLimb(kv.Key);
                bool gone = !r.HasLimb(id);
                kv.Value.color = StatusColor(r.GetLimbHealth01(id), gone);
            }
        }

        // ================================================================= overhead bars
        private OverheadBar GetBar(MechRagdoll r)
        {
            if (_bars.TryGetValue(r, out var b) && b.root != null) return b;
            b = new OverheadBar();
            b.root = MenuUI.NewRect(_root, "Overhead");
            b.root.sizeDelta = new Vector2(130f, 24f);
            b.root.SetAsFirstSibling();   // behind the HUD cards
            var back = MenuUI.Panel(b.root, "Back", IronFaunaPalette.WithAlpha(IronFaunaPalette.ShadowSlate, 0.9f));
            MenuUI.Place(back.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 8f));
            b.fillImg = MenuUI.Panel(back.transform, "Fill", IronFaunaPalette.UiStatusGood);
            b.fill = b.fillImg.rectTransform;
            MenuUI.Place(b.fill, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            var st = MenuUI.Panel(b.root, "StaminaBack", IronFaunaPalette.WithAlpha(IronFaunaPalette.ShadowSlate, 0.9f));
            MenuUI.Place(st.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -6f), new Vector2(0f, -1f));
            b.staminaRoot = st.gameObject;
            b.staminaImg = MenuUI.Panel(st.transform, "Fill", IronFaunaPalette.HazardYellow);
            b.staminaFill = b.staminaImg.rectTransform;
            MenuUI.Place(b.staminaFill, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            b.name = MenuUI.Label(b.root, "", 11, IronFaunaPalette.UiText, TextAnchor.LowerCenter, FontStyle.Bold);
            MenuUI.Place(b.name.rectTransform, new Vector2(0f, 0f), Vector2.one, new Vector2(-30f, 9f), new Vector2(30f, 0f));
            _bars[r] = b;
            return b;
        }

        // ================================================================= per frame
        /// <param name="me">The fighter the local player controls (or spectates).</param>
        /// <param name="target">The local player's current target (may be null).</param>
        /// <param name="others">Every fighter that should get an overhead bar, with its display name and team.</param>
        public void Tick(MechRagdoll me, MechRagdoll target, IEnumerable<(MechRagdoll r, string name, int team, bool out_)> others, Camera cam)
        {
            float dt = Time.unscaledDeltaTime;
            bool grappling = me != null && me.IsHolding;
            if (grappling != _wasGrappling)
            {
                _wasGrappling = grappling;
                if (_kbTitle != null) _kbTitle.text = grappling ? GrappleTitle : NormalTitle;
                foreach (var k in _keys)
                {
                    if (k.grappleAction != null) k.action.text = grappling ? k.grappleAction : k.normalAction;
                }
            }
            foreach (var k in _keys)
            {
                bool down = k.held != null && k.held();
                if (down) k.flash = 0.12f;
                else if (k.flash > 0f) k.flash -= dt;
                bool lit = down || k.flash > 0f;
                Color c = KindColor(k.kind);
                k.bg.color = lit ? c : IronFaunaPalette.UiButton;
                k.key.color = lit ? IronFaunaPalette.DarkIron : IronFaunaPalette.UiText;
                k.action.color = lit ? IronFaunaPalette.DarkIron : IronFaunaPalette.UiTextDim;
                k.bg.rectTransform.localScale = lit ? new Vector3(0.94f, 0.94f, 1f) : Vector3.one;
            }

            UpdateDoll(_myDoll, me, "YOU");
            UpdateDoll(_targetDoll, target != null && target != me ? target : null, "TARGET");

            var seen = new HashSet<MechRagdoll>();
            if (others != null && cam != null)
            {
                foreach (var o in others)
                {
                    if (o.r == null) continue;
                    bool isMe = o.r == me;
                    seen.Add(o.r);
                    var bar = GetBar(o.r);
                    Vector3 vp = cam.WorldToViewportPoint(o.r.HeadPosition + Vector3.up * 0.9f);
                    bool show = !o.out_ && !o.r.IsKO && vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                    bar.root.gameObject.SetActive(show);
                    if (!show) continue;
                    bar.root.anchorMin = bar.root.anchorMax = new Vector2(vp.x, vp.y);
                    bar.root.anchoredPosition = Vector2.zero;
                    float t = Mathf.Clamp01(o.r.health / Mathf.Max(1f, o.r.maxHealth));
                    bar.fill.anchorMax = new Vector2(t, 1f);
                    bar.fillImg.color = StatusColor(t, false);
                    bar.name.text = isMe ? "YOU" : o.name;
                    bar.staminaRoot.SetActive(isMe && o.r.ghost != null);
                    if (isMe && o.r.ghost != null)
                    {
                        bar.staminaFill.anchorMax = new Vector2(Mathf.Clamp01(o.r.ghost.Stamina01), 1f);
                        bar.staminaImg.color = o.r.ghost.Exhausted ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.HazardYellow;
                    }
                    bar.name.color = isMe ? IronFaunaPalette.CyanEnergy : o.team == 2 ? IronFaunaPalette.OrangeJointCore : o.team == 1 ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.UiText;
                    float s = o.r == target ? 1.15f : 1f;
                    bar.root.localScale = new Vector3(s, s, 1f);
                }
            }
            // hide bars for fighters that no longer exist (new round)
            var stale = new List<MechRagdoll>();
            foreach (var kv in _bars) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var r in stale)
            {
                if (_bars[r].root != null) Object.Destroy(_bars[r].root.gameObject);
                _bars.Remove(r);
            }
        }
    }
}
