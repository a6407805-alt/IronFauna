using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Brawl;
using IronFauna.Core;

namespace IronFauna.Menu
{
    /// <summary>
    /// Dedicated setup screens (owner 2026-11-22: "not the selector at the bottom left - when I click Tournament or
    /// Fight I want a menu for it"):
    ///  - TOURNAMENT CAMPAIGN: solo or 2-4 player local co-op (split screen, tag team), the 10-round gauntlet overview.
    ///  - FIGHT (BRAWL): mode (8P free-for-all / 4v4 squad), a map selector with illustrated previews, local players,
    ///    and online (offline / host / join with the host's IP).
    /// Built with MenuUI, coloured from IronFaunaPalette.
    /// </summary>
    public partial class IronFaunaMenuRoot
    {
        /// <summary>A row of mutually exclusive option buttons with a highlighted selection.</summary>
        private sealed class OptionGroup
        {
            public readonly List<Button> buttons = new List<Button>();
            public readonly List<Image> edges = new List<Image>();
            public readonly List<Text> labels = new List<Text>();
            public int selected;

            public void Refresh()
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    bool on = i == selected;
                    var c = buttons[i].colors;
                    c.normalColor = on ? IronFaunaPalette.UiSelect : IronFaunaPalette.UiButton;
                    c.selectedColor = c.normalColor;
                    buttons[i].colors = c;
                    edges[i].color = on ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.WithAlpha(IronFaunaPalette.UiTextDim, 0.25f);
                    labels[i].color = on ? IronFaunaPalette.UiText : IronFaunaPalette.UiTextDim;
                }
            }
        }

        private static readonly BrawlStageId[] MapOrder =
        {
            BrawlStageId.WrestlingRing, BrawlStageId.BasaltMesa, BrawlStageId.ScrapyardColosseum, BrawlStageId.FoundryCrucible, BrawlStageId.GlacierTrench,
            BrawlStageId.OrbitalDish, BrawlStageId.NeoMetroCity, BrawlStageId.RedwoodForest,
        };

        private static string MapBlurb(BrawlStageId s)
        {
            switch (s)
            {
                case BrawlStageId.FoundryCrucible: return "STEEL DECKS OVER MOLTEN SEAMS. SMOKESTACKS, GANTRIES AND EMBERS.";
                case BrawlStageId.OrbitalDish: return "A RELAY DISH HIGH ABOVE THE PLANET. FALL OFF THE EDGE AND YOU'RE GONE.";
                case BrawlStageId.ScrapyardColosseum: return "A JUNK-BUILT ARENA RINGED BY STANDS. TYRE STACKS AND CAR HULKS FOR COVER.";
                case BrawlStageId.GlacierTrench: return "SLICK ICE BETWEEN FROZEN SPIRES UNDER THE AURORA. MIND THE RIDGE.";
                case BrawlStageId.NeoMetroCity: return "NEON STREETS AT NIGHT. SMASH THROUGH BUILDINGS AND PARKED CARS.";
                case BrawlStageId.WrestlingRing: return "A REAL WRESTLING RING IN THE COLOSSEUM. HIT THE ROPES (ROPE REBOUND) AND TAKE IT OUTSIDE.";
                case BrawlStageId.RedwoodForest: return "GIANT REDWOODS - KNOCK ONE DOWN AND WATCH IT FALL ON SOMEONE.";
                default: return "A WIND-SWEPT BASALT PLATEAU WITH ROCK SPIRES. THE CLASSIC ARENA.";
            }
        }

        // tournament screen
        private OptionGroup _tPlayers;
        // brawl screen
        private OptionGroup _bMode, _bMap, _bPlayers, _bOnline;
        private RawImage _bPreview;
        private Text _bPreviewName, _bPreviewBlurb, _bSummary;
        private GameObject _bIpRow;
        private InputField _bIp;

        // ================================================================ screen switching
        private void HideSetupScreens()
        {
            if (_pilotsCanvas != null) _pilotsCanvas.SetActive(false);
            if (_tournamentSetupCanvas != null) _tournamentSetupCanvas.SetActive(false);
            if (_brawlSetupCanvas != null) _brawlSetupCanvas.SetActive(false);
        }

        private void ShowTournamentSetup()
        {
            MenuMusic.Play("tournament");
            _inGarage = false;
            _titleCanvas.SetActive(false);
            _garageCanvas.SetActive(false);
            HideSetupScreens();
            _tournamentSetupCanvas.SetActive(true);
            _tPlayers.selected = Mathf.Clamp(_matchConfig.localPlayers - 1, 0, 3);
            _tPlayers.Refresh();
            RefreshPilotPickers(_tPilotPickers);
        }

        private void ShowBrawlSetup()
        {
            MenuMusic.Play("brawl");
            _inGarage = false;
            _titleCanvas.SetActive(false);
            _garageCanvas.SetActive(false);
            HideSetupScreens();
            _brawlSetupCanvas.SetActive(true);
            if (_matchConfig.mode != BrawlGameMode.FreeForAll && _matchConfig.mode != BrawlGameMode.TeamBrawl)
                _matchConfig.mode = BrawlGameMode.FreeForAll;
            _bMode.selected = _matchConfig.mode == BrawlGameMode.TeamBrawl ? 1 : 0;
            _bMap.selected = Mathf.Max(0, Array.IndexOf(MapOrder, _matchConfig.stage));
            _bPlayers.selected = Mathf.Clamp(_matchConfig.localPlayers - 1, 0, 3);
            _bOnline.selected = (int)_matchConfig.netRole;
            RefreshBrawlSetup();
        }

        // ================================================================ shared building blocks
        /// <summary>Left setup panel (wide, responsive) with a scaling vertical stack; returns the stack.</summary>
        private RectTransform BuildSetupShell(string canvasName, string title, string subtitle, Color accent, out GameObject canvasGo, float widthFraction)
        {
            var canvas = MenuUI.CreateCanvas(canvasName, 1);
            canvasGo = canvas.gameObject;
            var root = canvas.transform;

            var column = MenuUI.Panel(root, "SetupColumn", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.86f));
            MenuUI.Place(column.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(1100f, 0f));
            var rc = column.gameObject.AddComponent<ResponsiveColumn>();
            rc.widthFraction = widthFraction;
            rc.minWidth = 900f;
            rc.maxWidth = 1400f;
            var stripe = MenuUI.Panel(column.transform, "Edge", accent);
            MenuUI.Place(stripe.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-8f, 0f), Vector2.zero);

            var viewport = MenuUI.NewRect(column.transform, "SetupViewport");
            MenuUI.Place(viewport, Vector2.zero, Vector2.one, new Vector2(64f, 36f), new Vector2(-48f, -44f));
            var content = MenuUI.NewRect(viewport, "SetupContent");
            MenuUI.VStack(content.gameObject, 10f, 0);
            var fitter = viewport.gameObject.AddComponent<FitContentToViewport>();
            fitter.content = content;
            fitter.Configure();

            var t = MenuUI.Label(content, title, 64, IronFaunaPalette.UiText, TextAnchor.LowerLeft, FontStyle.Bold);
            MenuUI.Size(t, 76f);
            MenuUI.Fit(t, 24, 0.98f);
            var barRow = MenuUI.NewRect(content, "Bar");
            MenuUI.Size(barRow, 8f);
            var bar = MenuUI.Panel(barRow, "AccentBar", accent);
            MenuUI.Place(bar.rectTransform, Vector2.zero, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var sub = MenuUI.Label(content, subtitle, 22, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(sub, 34f);
            MenuUI.Fit(sub, 11, 0.98f);
            return content;
        }

        private static void SectionLabel(Transform parent, string text)
        {
            MenuUI.Spacer(parent, 10f);
            var l = MenuUI.Label(parent, text, 20, IronFaunaPalette.OrangeJointCore, TextAnchor.LowerLeft, FontStyle.Bold);
            MenuUI.Size(l, 28f);
            MenuUI.Fit(l, 11, 0.98f);
        }

        /// <summary>Horizontal row of option cards (title + small description).</summary>
        private static OptionGroup OptionRow(Transform parent, string[] titles, string[] subs, float height, Action<int> onPick)
        {
            var group = new OptionGroup();
            var row = MenuUI.NewRect(parent, "OptionRow");
            MenuUI.Size(row, height);
            var h = MenuUI.HStack(row.gameObject, 10f, 0);
            h.childForceExpandWidth = true;
            for (int i = 0; i < titles.Length; i++)
            {
                int idx = i;
                var btn = MenuUI.CreateButton(row, "", 22, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () =>
                {
                    group.selected = idx;
                    group.Refresh();
                    onPick?.Invoke(idx);
                });
                MenuUI.Size(btn, height, 0f, 1f);
                var main = btn.GetComponentInChildren<Text>();
                main.text = titles[i];
                bool hasSub = subs != null && i < subs.Length && !string.IsNullOrEmpty(subs[i]);
                main.rectTransform.anchorMin = new Vector2(0f, hasSub ? 0.42f : 0f);
                main.rectTransform.anchorMax = Vector2.one;
                if (hasSub)
                {
                    var s = MenuUI.Label(btn.transform, subs[i], 14, IronFaunaPalette.UiTextDim, TextAnchor.UpperCenter, FontStyle.Bold);
                    MenuUI.Place(s.rectTransform, Vector2.zero, new Vector2(1f, 0.42f), new Vector2(8f, 4f), new Vector2(-8f, 0f));
                    MenuUI.Fit(s, 9);
                }
                var edge = MenuUI.Panel(btn.transform, "SelectEdge", IronFaunaPalette.UiTextDim);
                MenuUI.Place(edge.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 5f));
                group.buttons.Add(btn);
                group.edges.Add(edge);
                group.labels.Add(main);
            }
            group.Refresh();
            return group;
        }

        private static void ActionButton(Transform parent, string label, Color normal, UnityEngine.Events.UnityAction action, float height = 70f)
        {
            var b = MenuUI.CreateButton(parent, label, 30, normal, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, action);
            MenuUI.Size(b, height);
        }

        // ================================================================ TOURNAMENT setup
        private void BuildTournamentSetup()
        {
            var content = BuildSetupShell("TournamentSetupCanvas", "TOURNAMENT CAMPAIGN",
                "10 ROUNDS  //  MINI-BOSS AT ROUND 5  //  THE GRAND MASTER AT ROUND 10",
                IronFaunaPalette.CrimsonFront, out _tournamentSetupCanvas, 0.46f);

            SectionLabel(content, "WHO'S FIGHTING?");
            _tPlayers = OptionRow(content,
                new[] { "SOLO", "2P CO-OP", "3P CO-OP", "4P CO-OP" },
                new[] { "1 V 1 EVERY ROUND", "SPLIT SCREEN TAG TEAM", "SPLIT SCREEN TAG TEAM", "SPLIT SCREEN TAG TEAM" },
                92f, i => { _matchConfig.localPlayers = i + 1; RefreshPilotPickers(_tPilotPickers); });

            SectionLabel(content, "PILOTS  (EACH PLAYER FIGHTS AS THEIR OWN SAVED ROBOT)");
            _tPilotPickers = BuildPilotPickers(content);

            SectionLabel(content, "THE GAUNTLET");
            var gauntlet = MenuUI.Panel(content, "Gauntlet", IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.9f));
            MenuUI.Size(gauntlet, 250f);
            var gl = MenuUI.HStack(gauntlet.gameObject, 6f, 12);
            gl.childForceExpandWidth = true;
            for (int r = 0; r < TournamentState.TotalRounds; r++)
            {
                bool mini = r == TournamentState.MiniBossRound, gm = r == TournamentState.GrandMasterRound;
                Color c = gm ? IronFaunaPalette.HazardYellow : mini ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.UiButton;
                var cell = MenuUI.Panel(gauntlet.transform, "R" + (r + 1), IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, c, gm || mini ? 0.45f : 0.6f));
                MenuUI.Size(cell, 220f, 0f, gm ? 1.6f : mini ? 1.3f : 1f);
                var n = MenuUI.Label(cell.transform, (r + 1).ToString(), 44, gm || mini ? c : IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Place(n.rectTransform, new Vector2(0f, 0.45f), Vector2.one, Vector2.zero, Vector2.zero);
                MenuUI.Fit(n, 14);
                string tag = gm ? "GRAND\nMASTER" : mini ? "MINI\nBOSS" : BrawlTerrain.StageName(TournamentState.RoundStages[r]).Split(' ')[0];
                var tl = MenuUI.Label(cell.transform, tag, 15, gm || mini ? c : IronFaunaPalette.UiTextDim, TextAnchor.UpperCenter, FontStyle.Bold);
                MenuUI.Place(tl.rectTransform, Vector2.zero, new Vector2(1f, 0.45f), new Vector2(3f, 6f), new Vector2(-3f, 0f));
            }

            var info = MenuUI.Label(content,
                "• DAMAGE CARRIES OVER FROM ROUND TO ROUND - A TORN-OFF ARM STAYS GONE UNTIL YOU PAY TO REPLACE IT.\n" +
                "• EVERY HIT YOU LAND EARNS CREDITS. SPEND THEM IN THE REPAIR BAY ON REPAIRS, MODS - OR ILLEGAL MODS.\n" +
                "• LOSE A ROUND AND YOU CAN REPAIR AND REMATCH IT.",
                19, IronFaunaPalette.UiText, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Size(info, 110f);

            MenuUI.Spacer(content, 0f, flexible: true).GetComponent<LayoutElement>().minHeight = 12f;
            ActionButton(content, "START TOURNAMENT", IronFaunaPalette.CrimsonFront, StartTournamentCampaign);
            ActionButton(content, "BACK", IronFaunaPalette.UiButton, ShowTitle, 56f);
            _tournamentSetupCanvas.SetActive(false);
        }

        // ================================================================ BRAWL setup
        private void BuildBrawlSetup()
        {
            var content = BuildSetupShell("BrawlSetupCanvas", "FIGHT  //  BRAWL",
                "PICK A MODE, AN ARENA AND WHO'S PLAYING",
                IronFaunaPalette.CyanEnergy, out _brawlSetupCanvas, 0.62f);

            SectionLabel(content, "MODE");
            _bMode = OptionRow(content,
                new[] { "8-PLAYER FREE-FOR-ALL", "4v4 SQUAD BRAWL" },
                new[] { "EVERY ROBOT FOR ITSELF - LAST ONE STANDING WINS", "TWO TEAMS OF FOUR - WIPE THE OTHER SQUAD" },
                86f, i => { _matchConfig.mode = i == 1 ? BrawlGameMode.TeamBrawl : BrawlGameMode.FreeForAll; RefreshBrawlSetup(); });

            SectionLabel(content, "ARENA");
            // map tiles (left) + big preview (right)
            var mapRow = MenuUI.NewRect(content, "MapRow");
            MenuUI.Size(mapRow, 330f);
            var tiles = MenuUI.NewRect(mapRow, "Tiles");
            MenuUI.Place(tiles, Vector2.zero, new Vector2(0.56f, 1f), Vector2.zero, new Vector2(-8f, 0f));
            var grid = tiles.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150f, 104f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            _bMap = new OptionGroup();
            for (int i = 0; i < MapOrder.Length; i++)
            {
                int idx = i;
                var stage = MapOrder[i];
                var btn = MenuUI.CreateButton(tiles, "", 13, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () =>
                {
                    _bMap.selected = idx;
                    _matchConfig.stage = MapOrder[idx];
                    _bMap.Refresh();
                    RefreshBrawlSetup();
                });
                var thumb = new GameObject("Thumb", typeof(RectTransform)).AddComponent<RawImage>();
                thumb.transform.SetParent(btn.transform, false);
                thumb.texture = BrawlStageLook.PreviewTexture(stage);
                thumb.raycastTarget = false;
                MenuUI.Place(thumb.rectTransform, new Vector2(0f, 0.26f), Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, -4f));
                var label = btn.GetComponentInChildren<Text>();
                label.text = BrawlTerrain.StageName(stage);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = new Vector2(1f, 0.26f);
                label.transform.SetAsLastSibling();
                var edge = MenuUI.Panel(btn.transform, "SelectEdge", IronFaunaPalette.UiTextDim);
                MenuUI.Place(edge.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4f));
                _bMap.buttons.Add(btn);
                _bMap.edges.Add(edge);
                _bMap.labels.Add(label);
            }

            var previewPanel = MenuUI.Panel(mapRow, "Preview", IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.92f));
            MenuUI.Place(previewPanel.rectTransform, new Vector2(0.56f, 0f), Vector2.one, new Vector2(8f, 0f), Vector2.zero);
            _bPreview = new GameObject("PreviewImage", typeof(RectTransform)).AddComponent<RawImage>();
            _bPreview.transform.SetParent(previewPanel.transform, false);
            _bPreview.raycastTarget = false;
            MenuUI.Place(_bPreview.rectTransform, new Vector2(0f, 0.32f), Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, -8f));
            _bPreviewName = MenuUI.Label(previewPanel.transform, "", 26, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(_bPreviewName.rectTransform, new Vector2(0f, 0.20f), new Vector2(1f, 0.32f), new Vector2(12f, 0f), new Vector2(-8f, 0f));
            MenuUI.Fit(_bPreviewName, 12);
            _bPreviewBlurb = MenuUI.Label(previewPanel.transform, "", 15, IronFaunaPalette.UiTextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Place(_bPreviewBlurb.rectTransform, Vector2.zero, new Vector2(1f, 0.20f), new Vector2(12f, 6f), new Vector2(-8f, 0f));

            SectionLabel(content, "PLAYERS ON THIS PC");
            _bPlayers = OptionRow(content,
                new[] { "SOLO", "2P SPLIT", "3P SPLIT", "4P SPLIT" },
                new[] { "YOU + 7 BOTS", "SPLIT SCREEN", "SPLIT SCREEN", "SPLIT SCREEN" },
                76f, i => { _matchConfig.localPlayers = i + 1; RefreshBrawlSetup(); });
            _bPilotPickers = BuildPilotPickers(content);

            SectionLabel(content, "ONLINE");
            _bOnline = OptionRow(content,
                new[] { "OFFLINE", "HOST GAME", "JOIN GAME" },
                new[] { "BOTS FILL THE ARENA", "FRIENDS JOIN WITH A CODE", "TYPE A FRIEND'S CODE" },
                76f, i => { _matchConfig.netRole = (BrawlNetRole)i; RefreshBrawlSetup(); });

            // join code (only for JOIN): the host sees it at the top of their screen once the fight starts
            var ipRow = MenuUI.NewRect(content, "JoinCodeRow");
            MenuUI.Size(ipRow, 52f);
            _bIpRow = ipRow.gameObject;
            var ipLbl = MenuUI.Label(ipRow, "JOIN CODE", 20, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(ipLbl.rectTransform, Vector2.zero, new Vector2(0.2f, 1f), Vector2.zero, Vector2.zero);
            _bIp = BuildInputField(ipRow, _matchConfig.joinCode);
            _bIp.characterLimit = 12;
            _bIp.characterValidation = InputField.CharacterValidation.Alphanumeric;
            MenuUI.Place((RectTransform)_bIp.transform, new Vector2(0.2f, 0f), new Vector2(0.6f, 1f), Vector2.zero, Vector2.zero);
            _bIp.onValueChanged.AddListener(v =>
            {
                _matchConfig.joinCode = (v ?? "").Trim().ToUpperInvariant();
                RefreshBrawlSetup();
            });

            _bSummary = MenuUI.Label(content, "", 18, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(_bSummary, 30f);
            MenuUI.Fit(_bSummary, 10, 0.98f);

            MenuUI.Spacer(content, 0f, flexible: true).GetComponent<LayoutElement>().minHeight = 8f;
            ActionButton(content, "START FIGHT", IronFaunaPalette.CrimsonFront, StartBrawl);
            ActionButton(content, "BACK", IronFaunaPalette.UiButton, ShowTitle, 56f);
            _brawlSetupCanvas.SetActive(false);
        }

        private static InputField BuildInputField(Transform parent, string value)
        {
            var rt = MenuUI.NewRect(parent, "InputField");
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = IronFaunaPalette.DarkIron;
            var field = rt.gameObject.AddComponent<InputField>();
            var text = MenuUI.Label(rt, "", 22, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-12f, -2f));
            text.supportRichText = false;
            var ph = MenuUI.Label(rt, "127.0.0.1", 22, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            MenuUI.Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-12f, -2f));
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            field.characterLimit = 64;
            field.text = value;
            return field;
        }

        // ---------------------------------------------------------------- per-player pilot pickers
        private sealed class PilotPicker { public GameObject root; public Text value; }
        private List<PilotPicker> _tPilotPickers, _bPilotPickers;

        private List<PilotPicker> BuildPilotPickers(Transform parent)
        {
            var list = new List<PilotPicker>();
            var row = MenuUI.NewRect(parent, "PilotPickers");
            MenuUI.Size(row, 52f);
            var h = MenuUI.HStack(row.gameObject, 10f, 0);
            h.childForceExpandWidth = true;
            for (int slot = 0; slot < 4; slot++)
            {
                int s = slot;
                var cell = MenuUI.Panel(row, "Pilot_P" + (slot + 1), IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.9f));
                MenuUI.Size(cell, 52f, 0f, 1f);
                var prev = MenuUI.CreateButton(cell.transform, "<", 20, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () => CycleSlotPilot(s, -1));
                MenuUI.Place((RectTransform)prev.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(36f, 0f));
                var next = MenuUI.CreateButton(cell.transform, ">", 20, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, () => CycleSlotPilot(s, 1));
                MenuUI.Place((RectTransform)next.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(-36f, 0f), Vector2.zero);
                var val = MenuUI.Label(cell.transform, "", 17, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Place(val.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-40f, 0f));
                MenuUI.Fit(val, 9);
                list.Add(new PilotPicker { root = cell.gameObject, value = val });
            }
            return list;
        }

        private void CycleSlotPilot(int slot, int d)
        {
            int n = IronFauna.Pilots.PilotStore.Count;
            int cur = IronFauna.Pilots.PilotStore.SlotPilotIndex(slot);
            if (cur < 0) cur = 0;
            IronFauna.Pilots.PilotStore.SetSlotPilot(slot, ((cur + d) % n + n) % n);
            RefreshPilotPickers(_tPilotPickers);
            RefreshPilotPickers(_bPilotPickers);
        }

        private void RefreshPilotPickers(List<PilotPicker> pickers)
        {
            if (pickers == null) return;
            for (int slot = 0; slot < pickers.Count; slot++)
            {
                bool on = slot < _matchConfig.localPlayers;
                pickers[slot].root.SetActive(on);
                if (!on) continue;
                var p = IronFauna.Pilots.PilotStore.ForSlot(slot);
                pickers[slot].value.text = p != null ? $"P{slot + 1}: {p.name}  (LV {p.Level})" : $"P{slot + 1}: PRESET ROBOT";
            }
        }

        private void RefreshBrawlSetup()
        {
            if (_bMode == null) return;
            _bMode.Refresh();
            _bMap.Refresh();
            _bPlayers.Refresh();
            _bOnline.Refresh();
            var stage = MapOrder[Mathf.Clamp(_bMap.selected, 0, MapOrder.Length - 1)];
            _matchConfig.stage = stage;
            if (_bPreview != null) _bPreview.texture = BrawlStageLook.PreviewTexture(stage);
            if (_bPreviewName != null) _bPreviewName.text = BrawlTerrain.StageName(stage);
            if (_bPreviewBlurb != null) _bPreviewBlurb.text = MapBlurb(stage);
            if (_bIpRow != null) _bIpRow.SetActive(_matchConfig.netRole == BrawlNetRole.Client);
            RefreshPilotPickers(_bPilotPickers);

            string mode = _matchConfig.mode == BrawlGameMode.TeamBrawl ? "4v4 SQUAD BRAWL" : "8P FREE-FOR-ALL";
            string who = _matchConfig.localPlayers == 1 ? "SOLO" : $"{_matchConfig.localPlayers} PLAYERS SPLIT SCREEN";
            string net = _matchConfig.netRole == BrawlNetRole.Host ? "HOSTING (YOUR JOIN CODE SHOWS IN THE FIGHT)"
                : _matchConfig.netRole == BrawlNetRole.Client
                    ? (string.IsNullOrEmpty(_matchConfig.joinCode) ? "TYPE THE HOST'S JOIN CODE" : $"JOINING {_matchConfig.joinCode} (THE HOST PICKS MODE + MAP)")
                    : "OFFLINE";
            if (_bSummary != null) _bSummary.text = $"{mode}  //  {BrawlTerrain.StageName(stage)}  //  {who}  //  {net}";
        }
    }
}
