using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Between-bout Repair Bay for the Tournament Campaign: repair individual parts, buy legal workshop mods and
    /// black-market mods, paid for with credits earned by landing hits. Built at runtime with MenuUI + IronFaunaPalette.
    /// </summary>
    public sealed class TournamentRepairBay
    {
        private Canvas _canvas;
        private Transform _root;
        private TournamentGarage _garage;
        private UnityAction _onBack;
        private string _status = "";

        public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            MenuUI.EnsureEventSystem();
            _canvas = MenuUI.CreateCanvas("TournamentRepairBayCanvas", 21);
            _canvas.transform.SetParent(parent, false);
            _root = _canvas.transform;
            _canvas.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        public void Show(TournamentGarage garage, UnityAction onBack)
        {
            _garage = garage;
            _onBack = onBack;
            _status = garage.IllegalCount() > 0
                ? "BLACK-MARKET PARTS FITTED. OFFICIALS INSPECT EVERY RIG BEFORE EACH BOUT."
                : "LAND HITS IN THE RING TO EARN CREDITS. DAMAGE CARRIES OVER BETWEEN BOUTS.";
            Rebuild();
        }

        private void Rebuild()
        {
            if (_canvas == null || _garage == null) return;
            for (int i = _root.childCount - 1; i >= 0; i--) Object.Destroy(_root.GetChild(i).gameObject);
            _canvas.gameObject.SetActive(true);

            var bg = MenuUI.Panel(_root, "Backdrop", IronFaunaPalette.WithAlpha(IronFaunaPalette.StudioBackdrop, 0.96f));
            MenuUI.Fill(bg.rectTransform);

            // ---- header
            var header = MenuUI.Panel(_root, "Header", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.96f));
            MenuUI.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -116f), Vector2.zero);
            var rule = MenuUI.Panel(header.transform, "HeaderRule", IronFaunaPalette.OrangeJointCore);
            MenuUI.Place(rule.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 5f));
            var title = MenuUI.Label(header.transform, "REPAIR BAY  //  PIT CREW", 44, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(title.rectTransform, Vector2.zero, new Vector2(0.62f, 1f), new Vector2(44f, 6f), Vector2.zero);
            MenuUI.Fit(title, 18);
            var cr = MenuUI.Label(header.transform, $"CREDITS:  {_garage.credits:N0} CR", 40, IronFaunaPalette.HazardYellow, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(cr.rectTransform, new Vector2(0.62f, 0f), Vector2.one, Vector2.zero, new Vector2(-44f, 0f));
            MenuUI.Fit(cr, 18);

            BuildPartsColumn(Column("PARTS & REPAIRS", 0.02f, 0.36f, IronFaunaPalette.CyanEnergy, IronFaunaPalette.UiPanel));
            BuildModColumn(Column("WORKSHOP MODS  (LEGAL)", 0.38f, 0.67f, IronFaunaPalette.UiAccent, IronFaunaPalette.UiPanel), false);
            BuildModColumn(Column("BLACK MARKET  (ILLEGAL)", 0.69f, 0.98f, IronFaunaPalette.CrimsonFront,
                IronFaunaPalette.Lerp(IronFaunaPalette.UiPanel, IronFaunaPalette.MaroonRear, 0.35f)), true);

            // ---- footer: status + back
            var footer = MenuUI.Panel(_root, "Footer", IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.92f));
            MenuUI.Place(footer.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.10f), Vector2.zero, Vector2.zero);
            var status = MenuUI.Label(footer.transform, _status, 22, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(status.rectTransform, new Vector2(0.015f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(status, 12);
            var back = MenuUI.CreateButton(footer.transform, "BACK TO BRACKET", 26, IronFaunaPalette.CrimsonFront, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, _onBack);
            MenuUI.Place((RectTransform)back.transform, new Vector2(0.74f, 0.14f), new Vector2(0.99f, 0.86f), Vector2.zero, Vector2.zero);
        }

        /// <summary>A titled column panel; returns the vertical stack to fill.</summary>
        private RectTransform Column(string heading, float x0, float x1, Color accent, Color fill)
        {
            var panel = MenuUI.Panel(_root, "Column_" + heading, IronFaunaPalette.WithAlpha(fill, 0.92f));
            MenuUI.Place(panel.rectTransform, new Vector2(x0, 0.12f), new Vector2(x1, 0.89f), Vector2.zero, Vector2.zero);
            var top = MenuUI.Panel(panel.transform, "TopRule", accent);
            MenuUI.Place(top.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -5f), Vector2.zero);

            var stack = MenuUI.NewRect(panel.transform, "Stack");
            MenuUI.Place(stack, Vector2.zero, Vector2.one, new Vector2(22f, 18f), new Vector2(-22f, -22f));
            MenuUI.VStack(stack.gameObject, 10f, 0);

            var h = MenuUI.Label(stack, heading, 26, accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(h, 40f);
            MenuUI.Fit(h, 12);
            return stack;
        }

        // ---------------------------------------------------------------- parts
        private void BuildPartsColumn(RectTransform stack)
        {
            foreach (var p in TournamentGarage.Parts) PartRow(stack, p);

            MenuUI.Spacer(stack, 8f);
            int all = _garage.RepairAllCost();
            var allBtn = MenuUI.CreateButton(stack, all > 0 ? $"REPAIR ALL  —  {all} CR" : "RIG FULLY REPAIRED", 24,
                IronFaunaPalette.UiSelect, IronFaunaPalette.CyanEnergy, IronFaunaPalette.UiText, () =>
                {
                    int n = _garage.RepairAllAffordable();
                    _status = n == 0 ? "NOT ENOUGH CREDITS FOR ANY REPAIR."
                        : _garage.RepairAllCost() == 0 ? "PIT CREW RESTORED THE WHOLE RIG."
                        : $"REPAIRED {n} PART(S). NOT ENOUGH CREDITS FOR THE REST.";
                    Rebuild();
                });
            MenuUI.Size(allBtn, 58f);
            allBtn.interactable = all > 0;
            MenuUI.Spacer(stack, 0f, flexible: true);
        }

        private void PartRow(RectTransform stack, RigPart p)
        {
            bool missing = _garage.IsMissing(p);
            float cond = _garage.Condition(p);
            int cost = _garage.RepairCost(p);

            var row = MenuUI.Panel(stack, "Part_" + p, IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.9f));
            MenuUI.Size(row, 78f);

            var name = MenuUI.Label(row.transform, TournamentGarage.PartName(p), 22, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.58f, 1f), new Vector2(14f, 0f), Vector2.zero);
            MenuUI.Fit(name, 12);

            Color barCol = missing ? IronFaunaPalette.CrimsonFront
                : cond > 0.66f ? IronFaunaPalette.CyanEnergy
                : cond > 0.33f ? IronFaunaPalette.HazardYellow
                : IronFaunaPalette.OrangeJointCore;
            var track = MenuUI.Panel(row.transform, "Track", IronFaunaPalette.ShadowSlate);
            MenuUI.Place(track.rectTransform, new Vector2(0f, 0.18f), new Vector2(0.42f, 0.42f), new Vector2(14f, 0f), Vector2.zero);
            var fill = MenuUI.Panel(track.transform, "Fill", barCol);
            MenuUI.Place(fill.rectTransform, Vector2.zero, new Vector2(Mathf.Clamp01(cond), 1f), Vector2.zero, Vector2.zero);

            var pct = MenuUI.Label(row.transform, missing ? "MISSING" : $"{Mathf.RoundToInt(cond * 100f)}%", 20, barCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(pct.rectTransform, new Vector2(0.43f, 0f), new Vector2(0.60f, 0.6f), new Vector2(8f, 0f), Vector2.zero);
            MenuUI.Fit(pct, 10);

            string label = cost == 0 ? "OK" : missing ? $"REPLACE  {cost} CR" : $"REPAIR  {cost} CR";
            var btn = MenuUI.CreateButton(row.transform, label, 20, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () =>
            {
                _status = _garage.Repair(p)
                    ? $"{TournamentGarage.PartName(p)} {(missing ? "REPLACED" : "REPAIRED")}."
                    : $"NOT ENOUGH CREDITS TO FIX THE {TournamentGarage.PartName(p)}.";
                Rebuild();
            });
            MenuUI.Place((RectTransform)btn.transform, new Vector2(0.61f, 0.14f), new Vector2(1f, 0.86f), Vector2.zero, new Vector2(-10f, 0f));
            btn.interactable = cost > 0 && _garage.credits >= cost;
        }

        // ---------------------------------------------------------------- mods
        private void BuildModColumn(RectTransform stack, bool illegal)
        {
            if (illegal)
            {
                var warn = MenuUI.Label(stack, "CAUGHT AT INSPECTION = CONFISCATED + FINED HALF THE PRICE", 17, IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(warn, 26f);
                MenuUI.Fit(warn, 10);
            }
            foreach (var m in TournamentGarage.Catalog)
            {
                if (m.illegal == illegal) ModRow(stack, m);
            }
            MenuUI.Spacer(stack, 0f, flexible: true);
        }

        private void ModRow(RectTransform stack, TournamentMod m)
        {
            bool owned = _garage.Owns(m);
            Color rowCol = m.illegal
                ? IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, IronFaunaPalette.MaroonRear, 0.45f)
                : IronFaunaPalette.DarkIron;

            var row = MenuUI.Panel(stack, "Mod_" + m.id, IronFaunaPalette.WithAlpha(rowCol, 0.92f));
            MenuUI.Size(row, m.illegal ? 96f : 86f);

            var name = MenuUI.Label(row.transform, m.name, 22, owned ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(name.rectTransform, new Vector2(0f, 0.52f), new Vector2(0.62f, 1f), new Vector2(14f, 0f), Vector2.zero);
            MenuUI.Fit(name, 12);

            string info = m.illegal ? $"{m.desc}\n{Mathf.RoundToInt(m.detectChance * 100f)}% CHANCE TO BE CAUGHT PER BOUT" : m.desc;
            var desc = MenuUI.Label(row.transform, info, 17, m.illegal ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.UiTextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Place(desc.rectTransform, Vector2.zero, new Vector2(0.62f, 0.52f), new Vector2(14f, 4f), Vector2.zero);

            string label = owned ? "INSTALLED" : $"BUY  {m.cost} CR";
            Color normal = owned ? IronFaunaPalette.UiSelect : m.illegal ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.UiButton;
            var btn = MenuUI.CreateButton(row.transform, label, 20, normal, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, () =>
            {
                _status = _garage.Buy(m)
                    ? (m.illegal ? $"{m.name} SMUGGLED IN. KEEP IT QUIET." : $"{m.name} INSTALLED.")
                    : $"NOT ENOUGH CREDITS FOR {m.name}.";
                Rebuild();
            });
            MenuUI.Place((RectTransform)btn.transform, new Vector2(0.64f, 0.16f), new Vector2(1f, 0.84f), Vector2.zero, new Vector2(-10f, 0f));
            btn.interactable = !owned && _garage.credits >= m.cost;
        }
    }
}
