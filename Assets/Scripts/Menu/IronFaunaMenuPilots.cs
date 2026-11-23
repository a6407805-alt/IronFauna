using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Core;
using IronFauna.Pilots;

namespace IronFauna.Menu
{
    /// <summary>
    /// PILOTS & UPGRADES screen (owner 2026-11-22: personal fighters for friends - save a robot, invest in it, illegal
    /// upgrades): pick / create / rename / delete pilots, see level, XP, SCRAP and record, buy permanent upgrade tiers
    /// (gated by pilot level) and black-market chips, and jump to the garage to build that pilot's robot.
    /// </summary>
    public partial class IronFaunaMenuRoot
    {
        private int _pilotSel;
        private RectTransform _pilotList, _pilotDetail;
        private string _pilotStatus = "";

        private void ShowPilots()
        {
            MenuMusic.Play("pilots");
            _inGarage = false;
            _titleCanvas.SetActive(false);
            _garageCanvas.SetActive(false);
            HideSetupScreens();
            _pilotsCanvas.SetActive(true);
            _pilotSel = PilotStore.GaragePilotIndex;
            _pilotStatus = "EVERY MATCH EARNS SCRAP AND XP: DAMAGE DEALT, KNOCKOUTS AND WINS. HIGHER LEVELS UNLOCK BIGGER UPGRADES.";
            RebuildPilots();
        }

        private void BuildPilots()
        {
            var canvas = MenuUI.CreateCanvas("PilotsCanvas", 1);
            _pilotsCanvas = canvas.gameObject;
            var root = canvas.transform;
            var bg = MenuUI.Panel(root, "Backdrop", IronFaunaPalette.WithAlpha(IronFaunaPalette.StudioBackdrop, 0.9f));
            MenuUI.Fill(bg.rectTransform);

            var header = MenuUI.Label(root, "PILOTS & UPGRADES", 56, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(header.rectTransform, new Vector2(0.02f, 0.9f), new Vector2(0.7f, 0.98f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(header, 20);

            var listPanel = MenuUI.Panel(root, "PilotList", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.92f));
            MenuUI.Place(listPanel.rectTransform, new Vector2(0.02f, 0.03f), new Vector2(0.24f, 0.89f), Vector2.zero, Vector2.zero);
            _pilotList = MenuUI.NewRect(listPanel.transform, "Stack");
            MenuUI.Place(_pilotList, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
            MenuUI.VStack(_pilotList.gameObject, 8f, 0);

            var detailPanel = MenuUI.Panel(root, "PilotDetail", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.92f));
            MenuUI.Place(detailPanel.rectTransform, new Vector2(0.255f, 0.03f), new Vector2(0.98f, 0.89f), Vector2.zero, Vector2.zero);
            var rule = MenuUI.Panel(detailPanel.transform, "Rule", IronFaunaPalette.HazardYellow);
            MenuUI.Place(rule.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -5f), Vector2.zero);
            _pilotDetail = MenuUI.NewRect(detailPanel.transform, "Detail");
            MenuUI.Place(_pilotDetail, Vector2.zero, Vector2.one, new Vector2(24f, 18f), new Vector2(-24f, -18f));
            _pilotsCanvas.SetActive(false);
        }

        private void RebuildPilots()
        {
            for (int i = _pilotList.childCount - 1; i >= 0; i--) Destroy(_pilotList.GetChild(i).gameObject);
            for (int i = _pilotDetail.childCount - 1; i >= 0; i--) Destroy(_pilotDetail.GetChild(i).gameObject);
            _pilotSel = Mathf.Clamp(_pilotSel, 0, PilotStore.Count - 1);

            // ---- left: pilot list
            var t = MenuUI.Label(_pilotList, "YOUR PILOTS", 22, IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(t, 34f);
            for (int i = 0; i < PilotStore.Count; i++)
            {
                int idx = i;
                var p = PilotStore.Get(i);
                bool sel = i == _pilotSel;
                var b = MenuUI.CreateButton(_pilotList, $"{p.name}   LV {p.Level}", 22,
                    sel ? IronFaunaPalette.UiSelect : IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText,
                    () => { _pilotSel = idx; RebuildPilots(); });
                MenuUI.Size(b, 58f);
            }
            if (PilotStore.Count < PilotStore.MaxPilots)
            {
                var add = MenuUI.CreateButton(_pilotList, "+ NEW PILOT", 22, IronFaunaPalette.CrimsonFront, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText,
                    () => { PilotStore.Create(); _pilotSel = PilotStore.Count - 1; _pilotStatus = "NEW PILOT CREATED - RENAME THEM AND BUILD THEIR ROBOT IN THE GARAGE."; RebuildPilots(); });
                MenuUI.Size(add, 58f);
            }
            MenuUI.Spacer(_pilotList, 0f, flexible: true);
            var back = MenuUI.CreateButton(_pilotList, "BACK", 22, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, ShowTitle);
            MenuUI.Size(back, 54f);

            // ---- right: selected pilot
            var pilot = PilotStore.Get(_pilotSel);
            var top = MenuUI.NewRect(_pilotDetail, "Top");
            MenuUI.Place(top, new Vector2(0f, 0.80f), Vector2.one, Vector2.zero, Vector2.zero);

            var nameField = BuildInputField(top, pilot.name);
            MenuUI.Place((RectTransform)nameField.transform, new Vector2(0f, 0.5f), new Vector2(0.38f, 1f), Vector2.zero, new Vector2(0f, -4f));
            nameField.characterLimit = 16;
            nameField.onEndEdit.AddListener(v =>
            {
                string n = string.IsNullOrWhiteSpace(v) ? pilot.name : v.Trim().ToUpperInvariant();
                pilot.name = n;
                PilotStore.Save();
                RebuildPilots();
            });

            int lv = pilot.Level;
            int xpNow = pilot.xp - PilotUpgrades.XpForLevel(lv);
            int xpNeed = PilotUpgrades.XpForLevel(lv + 1) - PilotUpgrades.XpForLevel(lv);
            var lvl = MenuUI.Label(top, $"LEVEL {lv}", 36, IronFaunaPalette.HazardYellow, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(lvl.rectTransform, new Vector2(0.40f, 0.5f), new Vector2(0.58f, 1f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(lvl, 14);
            var track = MenuUI.Panel(top, "XpTrack", IronFaunaPalette.ShadowSlate);
            MenuUI.Place(track.rectTransform, new Vector2(0.40f, 0.30f), new Vector2(0.72f, 0.44f), Vector2.zero, Vector2.zero);
            var fill = MenuUI.Panel(track.transform, "Fill", IronFaunaPalette.HazardYellow);
            MenuUI.Place(fill.rectTransform, Vector2.zero, new Vector2(Mathf.Clamp01(xpNow / (float)Mathf.Max(1, xpNeed)), 1f), Vector2.zero, Vector2.zero);
            var xpl = MenuUI.Label(top, $"XP {xpNow} / {xpNeed}", 16, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(xpl.rectTransform, new Vector2(0.40f, 0.04f), new Vector2(0.72f, 0.28f), Vector2.zero, Vector2.zero);

            var scrap = MenuUI.Label(top, $"{pilot.scrap:N0} SCRAP", 40, IronFaunaPalette.HazardYellow, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(scrap.rectTransform, new Vector2(0.72f, 0.5f), Vector2.one, Vector2.zero, Vector2.zero);
            MenuUI.Fit(scrap, 14);
            var rec = MenuUI.Label(top, $"{pilot.matches} MATCHES  //  {pilot.wins} WINS  //  {pilot.knockouts} KOs", 17, IronFaunaPalette.UiTextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(rec.rectTransform, new Vector2(0.72f, 0.04f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(rec, 9);

            var editBtn = MenuUI.CreateButton(top, "BUILD THIS ROBOT IN THE GARAGE", 18, IronFaunaPalette.UiSelect, IronFaunaPalette.CyanEnergy, IronFaunaPalette.UiText,
                () =>
                {
                    PilotStore.GaragePilotIndex = _pilotSel;
                    _loadout = PilotStore.GaragePilot.loadout.Clone();
                    RebuildMech();
                    ShowGarage();
                });
            MenuUI.Place((RectTransform)editBtn.transform, new Vector2(0f, 0.02f), new Vector2(0.38f, 0.44f), Vector2.zero, Vector2.zero);

            // upgrades (left) and black market (right)
            var ups = MenuUI.NewRect(_pilotDetail, "Upgrades");
            MenuUI.Place(ups, new Vector2(0f, 0.08f), new Vector2(0.55f, 0.78f), Vector2.zero, new Vector2(-12f, 0f));
            MenuUI.VStack(ups.gameObject, 8f, 0);
            var uh = MenuUI.Label(ups, "PERMANENT UPGRADES", 22, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(uh, 32f);
            for (int tr = 0; tr < PilotUpgrades.TrackCount; tr++) UpgradeRow(ups, pilot, tr);

            var bm = MenuUI.NewRect(_pilotDetail, "BlackMarket");
            MenuUI.Place(bm, new Vector2(0.55f, 0.08f), new Vector2(1f, 0.78f), new Vector2(12f, 0f), Vector2.zero);
            MenuUI.VStack(bm.gameObject, 8f, 0);
            var bh = MenuUI.Label(bm, "BLACK MARKET CHIPS  (ILLEGAL)", 22, IronFaunaPalette.CrimsonFront, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(bh, 32f);
            var warn = MenuUI.Label(bm, "BIG BOOSTS - BUT A CHIP CAN BURN OUT AFTER ANY MATCH AND BE LOST.", 15, IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(warn, 22f);
            MenuUI.Fit(warn, 9);
            foreach (var c in PilotUpgrades.Chips) ChipRow(bm, pilot, c);

            // footer: status + delete
            var status = MenuUI.Label(_pilotDetail, _pilotStatus, 18, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(status.rectTransform, Vector2.zero, new Vector2(0.78f, 0.07f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(status, 10);
            if (PilotStore.Count > 1)
            {
                var del = MenuUI.CreateButton(_pilotDetail, "DELETE PILOT", 18, IronFaunaPalette.MaroonRear, IronFaunaPalette.CrimsonFront, IronFaunaPalette.UiText,
                    () => { PilotStore.Delete(_pilotSel); _pilotSel = 0; _pilotStatus = "PILOT DELETED."; RebuildPilots(); });
                MenuUI.Place((RectTransform)del.transform, new Vector2(0.8f, 0f), new Vector2(1f, 0.065f), Vector2.zero, Vector2.zero);
            }
        }

        private void UpgradeRow(Transform parent, PilotProfile p, int tr)
        {
            int lv = p.UpgradeLevel(tr);
            var row = MenuUI.Panel(parent, "Track_" + tr, IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.9f));
            MenuUI.Size(row, 76f);
            var name = MenuUI.Label(row.transform, PilotUpgrades.TrackNames[tr], 20, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.6f, 1f), new Vector2(12f, 0f), Vector2.zero);
            MenuUI.Fit(name, 10);
            var per = MenuUI.Label(row.transform, $"{PilotUpgrades.TrackPerTier[tr]} PER TIER", 14, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(per.rectTransform, new Vector2(0f, 0.08f), new Vector2(0.4f, 0.5f), new Vector2(12f, 0f), Vector2.zero);
            MenuUI.Fit(per, 8);
            for (int k = 0; k < PilotUpgrades.MaxTier; k++)
            {
                var pip = MenuUI.Panel(row.transform, "Pip" + k, k < lv ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.ShadowSlate);
                float x0 = 0.42f + k * 0.034f;
                MenuUI.Place(pip.rectTransform, new Vector2(x0, 0.18f), new Vector2(x0 + 0.028f, 0.42f), Vector2.zero, Vector2.zero);
            }
            string label;
            bool can = PilotUpgrades.CanUpgrade(p, tr);
            if (lv >= PilotUpgrades.MaxTier) label = "MAXED";
            else if (p.Level < PilotUpgrades.NextTierLevelNeeded(p, tr)) label = $"NEEDS LV {PilotUpgrades.NextTierLevelNeeded(p, tr)}";
            else label = $"TIER {lv + 1}  {PilotUpgrades.NextTierCost(p, tr)}";
            var btn = MenuUI.CreateButton(row.transform, label, 17, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () =>
            {
                _pilotStatus = PilotUpgrades.Upgrade(p, tr) ? $"{PilotUpgrades.TrackNames[tr]} UPGRADED TO TIER {p.UpgradeLevel(tr)}." : "NOT ENOUGH SCRAP.";
                RebuildPilots();
            });
            MenuUI.Place((RectTransform)btn.transform, new Vector2(0.62f, 0.14f), new Vector2(1f, 0.86f), Vector2.zero, new Vector2(-8f, 0f));
            btn.interactable = can;
        }

        private void ChipRow(Transform parent, PilotProfile p, PilotChip c)
        {
            bool owned = p.chips.Contains(c.id);
            var row = MenuUI.Panel(parent, "Chip_" + c.id, IronFaunaPalette.WithAlpha(IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, IronFaunaPalette.MaroonRear, 0.45f), 0.92f));
            MenuUI.Size(row, 68f);
            var name = MenuUI.Label(row.transform, c.name, 19, owned ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.6f, 1f), new Vector2(12f, 0f), Vector2.zero);
            MenuUI.Fit(name, 10);
            var desc = MenuUI.Label(row.transform, $"{c.desc}  //  {Mathf.RoundToInt(c.burnoutChance * 100f)}% BURNOUT", 13, IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(desc.rectTransform, new Vector2(0f, 0.06f), new Vector2(0.62f, 0.5f), new Vector2(12f, 0f), Vector2.zero);
            MenuUI.Fit(desc, 8);
            string label = owned ? "INSTALLED" : p.Level < c.minLevel ? $"NEEDS LV {c.minLevel}" : $"BUY  {c.cost}";
            var btn = MenuUI.CreateButton(row.transform, label, 17, IronFaunaPalette.CrimsonFront, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, () =>
            {
                _pilotStatus = PilotUpgrades.BuyChip(p, c) ? $"{c.name} INSTALLED. HOPE IT HOLDS." : "NOT ENOUGH SCRAP.";
                RebuildPilots();
            });
            MenuUI.Place((RectTransform)btn.transform, new Vector2(0.64f, 0.14f), new Vector2(1f, 0.86f), Vector2.zero, new Vector2(-8f, 0f));
            btn.interactable = !owned && p.Level >= c.minLevel && p.scrap >= c.cost;
        }
    }
}
