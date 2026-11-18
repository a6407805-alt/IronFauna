using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Clamshell;
using IronFauna.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IronFauna.Menu
{
    /// <summary>
    /// Title screen + garage, built entirely at runtime on top of a 3D mech showcase stage.
    /// Drop this on one GameObject in an otherwise empty scene (IronFauna > Setup Menu Scene does it for you).
    /// </summary>
    public class IronFaunaMenuRoot : MonoBehaviour
    {
        [Tooltip("Open straight into the garage instead of the title screen.")]
        public bool startInGarage;

        private enum Tab { Frame, Head, Body, Arms, Legs }

        private struct SlotRow
        {
            public readonly string label;
            public readonly ClamshellSlot slot;

            public SlotRow(string label, ClamshellSlot slot)
            {
                this.label = label;
                this.slot = slot;
            }
        }

        private static readonly Dictionary<Tab, SlotRow[]> TabSlots = new Dictionary<Tab, SlotRow[]>
        {
            { Tab.Head, new[] { new SlotRow("Head front", ClamshellSlot.HeadFront), new SlotRow("Head back", ClamshellSlot.HeadBack) } },
            { Tab.Body, new[] { new SlotRow("Body front", ClamshellSlot.BodyFront), new SlotRow("Body back", ClamshellSlot.BodyBack) } },
            {
                Tab.Arms, new[]
                {
                    new SlotRow("Shoulder top", ClamshellSlot.ShoulderFront), new SlotRow("Shoulder under", ClamshellSlot.ShoulderBack),
                    new SlotRow("Upper arm front", ClamshellSlot.UpperArmFront), new SlotRow("Upper arm back", ClamshellSlot.UpperArmBack),
                    new SlotRow("Forearm front", ClamshellSlot.LowerArmFront), new SlotRow("Forearm back", ClamshellSlot.LowerArmBack),
                    new SlotRow("Hand front", ClamshellSlot.HandFront), new SlotRow("Hand back", ClamshellSlot.HandBack),
                }
            },
            {
                Tab.Legs, new[]
                {
                    new SlotRow("Thigh front", ClamshellSlot.ThighFront), new SlotRow("Thigh back", ClamshellSlot.ThighBack),
                    new SlotRow("Calf front", ClamshellSlot.CalfFront), new SlotRow("Calf back", ClamshellSlot.CalfBack),
                    new SlotRow("Toe", ClamshellSlot.FootFront), new SlotRow("Heel", ClamshellSlot.FootBack),
                }
            },
        };

        private static readonly string[] PoseNames =
        {
            "Human leg", "Chicken bend forward", "Reverse extended", "Chicken bend back", "Reverse critical fold",
        };

        private MechShowcaseStage _stage;
        private MechLoadout _loadout;

        private GameObject _titleCanvas;
        private GameObject _garageCanvas;

        private readonly Dictionary<Tab, GameObject> _tabPages = new Dictionary<Tab, GameObject>();
        private readonly Dictionary<Tab, Image> _tabButtons = new Dictionary<Tab, Image>();
        private readonly List<KeyValuePair<ClamshellSlot, MenuUI.Cycler>> _slotCyclers = new List<KeyValuePair<ClamshellSlot, MenuUI.Cycler>>();
        private MenuUI.Cycler _chassisCycler;
        private MenuUI.Cycler _poseCycler;
        private MenuUI.Cycler _paintCycler;
        private MenuUI.Cycler _torsoWeightCycler;
        private MenuUI.Cycler _legWeightCycler;
        private MenuUI.Cycler _viewCycler;
        private bool _skeletonView;
        private MenuUI.Cycler _fullSetCycler;
        private Text _armsNote;
        private Text _headerInfo;
        private Text _selectedTitle;
        private Text _selectedPerk;
        private Text _saveStatus;
        private Text _titleFooter;
        private readonly Dictionary<string, KeyValuePair<RectTransform, Text>> _statBars = new Dictionary<string, KeyValuePair<RectTransform, Text>>();
        private Tab _tab = Tab.Frame;
        private ClamshellSetArchetype _fullSet = ClamshellSetArchetype.Set01_Ironclad;
        private bool _inGarage;
        private bool _dirty;

        private void Start()
        {
            _loadout = MechLoadoutStore.Load();

            _stage = new GameObject("MechShowcaseStage").AddComponent<MechShowcaseStage>();
            _stage.Build();
            RebuildMech();

            MenuUI.EnsureEventSystem();
            BuildTitle();
            BuildGarage();

            bool garage = startInGarage || IronFaunaGameFlow.OpenGarageOnMenuLoad;
            IronFaunaGameFlow.OpenGarageOnMenuLoad = false;
            if (garage) ShowGarage(); else ShowTitle();
        }

        private void Update()
        {
            bool back = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) back = true;
            if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) back = true;
#else
            back = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (back && _inGarage) ShowTitle();
        }

        // ------------------------------------------------------------------
        // Screens
        // ------------------------------------------------------------------

        private void ShowTitle()
        {
            _inGarage = false;
            _titleCanvas.SetActive(true);
            _garageCanvas.SetActive(false);
            _stage.autoSpin = true;
            _stage.allowUserOrbit = false;
            _stage.Frame(_stage.Mech.HipHeight + 0.6f, 10.5f, 2.2f);
            _titleFooter.text = MechLoadoutStore.HasSaved ? "v0.1 prototype   //   saved loadout ready" : "v0.1 prototype   //   no saved loadout yet";
        }

        /// <summary>Mech brawl (active ragdolls): hides the menu and turntable and builds the arena in this scene
        /// (Esc reloads the menu). The earlier boxing ring is still available as IronFauna.Boxing.MechBoxingMatch.</summary>
        private void StartBrawl()
        {
            _titleCanvas.SetActive(false);
            _garageCanvas.SetActive(false);
            if (_stage != null)
            {
                _stage.enabled = false;
                foreach (string n in new[] { "Turntable", "TurntableHazardRing" })
                {
                    var t = _stage.transform.Find(n);
                    if (t != null) t.gameObject.SetActive(false);
                }
            }
            IronFauna.Brawl.MechBrawlMatch.Begin(_loadout, _stage != null ? _stage.StageCamera : Camera.main);
            enabled = false;
        }

        private void ShowGarage()
        {
            _inGarage = true;
            _titleCanvas.SetActive(false);
            _garageCanvas.SetActive(true);
            _stage.autoSpin = false;
            _stage.allowUserOrbit = true;
            _stage.ResetTurntable();
            SelectTab(_tab);
            RefreshAll();
        }

        // ------------------------------------------------------------------
        // Title
        // ------------------------------------------------------------------

        private void BuildTitle()
        {
            var canvas = MenuUI.CreateCanvas("TitleCanvas", 0);
            _titleCanvas = canvas.gameObject;
            var root = canvas.transform;

            var column = MenuUI.Panel(root, "TitleColumn", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.72f));
            MenuUI.Place(column.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(760f, 0f));
            var stripe = MenuUI.Panel(column.transform, "HazardEdge", IronFaunaPalette.UiAccent);
            MenuUI.Place(stripe.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-8f, 0f), Vector2.zero);

            var title = MenuUI.Label(column.transform, "IRONFAUNA", 124, IronFaunaPalette.UiText, TextAnchor.LowerLeft, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            MenuUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(80f, -360f), new Vector2(-40f, -170f));

            var bar = MenuUI.Panel(column.transform, "TitleBar", IronFaunaPalette.UiSelect);
            MenuUI.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -380f), new Vector2(560f, -368f));

            var tagline = MenuUI.Label(column.transform, "YOU ARE THE TOWER AND THE DEFENDER", 26, IronFaunaPalette.UiAccent, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Place(tagline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(84f, -440f), new Vector2(-40f, -396f));

            var buttons = MenuUI.NewRect(column.transform, "TitleButtons");
            MenuUI.Place(buttons, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -800f), new Vector2(520f, -500f));
            MenuUI.VStack(buttons.gameObject, 18f, 0);

            AddTitleButton(buttons, "BRAWL", StartBrawl);
            AddTitleButton(buttons, "GARAGE", ShowGarage);
            AddTitleButton(buttons, "DEPLOY", () => IronFaunaGameFlow.Deploy(_loadout));
            AddTitleButton(buttons, "QUIT", IronFaunaGameFlow.Quit);

            _titleFooter = MenuUI.Label(column.transform, "", 18, IronFaunaPalette.UiTextDim, TextAnchor.LowerLeft);
            MenuUI.Place(_titleFooter.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(84f, 40f), new Vector2(-40f, 80f));
        }

        private static void AddTitleButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var b = MenuUI.CreateButton(parent, label, 34, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, action);
            MenuUI.Size(b, 84f);
        }

        // ------------------------------------------------------------------
        // Garage
        // ------------------------------------------------------------------

        private void BuildGarage()
        {
            var canvas = MenuUI.CreateCanvas("GarageCanvas", 0);
            _garageCanvas = canvas.gameObject;
            var root = canvas.transform;

            // Header
            var header = MenuUI.Panel(root, "Header", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.85f));
            MenuUI.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -84f), Vector2.zero);
            var headerTitle = MenuUI.Label(header.transform, "GARAGE", 46, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(headerTitle.rectTransform, Vector2.zero, new Vector2(0.4f, 1f), new Vector2(40f, 0f), Vector2.zero);
            _headerInfo = MenuUI.Label(header.transform, "", 22, IronFaunaPalette.UiAccent, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(_headerInfo.rectTransform, new Vector2(0.4f, 0f), Vector2.one, Vector2.zero, new Vector2(-40f, 0f));
            var headerEdge = MenuUI.Panel(header.transform, "HazardEdge", IronFaunaPalette.UiAccent);
            MenuUI.Place(headerEdge.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 6f));

            // Left column: tabs + slot pages
            var left = MenuUI.Panel(root, "PartsColumn", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.72f));
            MenuUI.Place(left.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(32f, 120f), new Vector2(712f, -108f));

            var tabs = MenuUI.NewRect(left.transform, "Tabs");
            MenuUI.Place(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -76f), new Vector2(-16f, -16f));
            var tabLayout = MenuUI.HStack(tabs.gameObject, 6f, 0);
            tabLayout.childForceExpandWidth = true;
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                Tab captured = t;
                var b = MenuUI.CreateButton(tabs, t.ToString().ToUpperInvariant(), 20, IronFaunaPalette.UiNeutral, IronFaunaPalette.UiNeutral, IronFaunaPalette.UiText, () => SelectTab(captured));
                MenuUI.Size(b, 60f, 120f, 1f);
                _tabButtons[t] = b.targetGraphic as Image;
            }

            var pages = MenuUI.NewRect(left.transform, "Pages");
            MenuUI.Place(pages, Vector2.zero, Vector2.one, new Vector2(16f, 150f), new Vector2(-16f, -92f));
            BuildFramePage(pages);
            foreach (var kv in TabSlots) BuildSlotPage(pages, kv.Key, kv.Value);

            // "Apply one set to this whole tab" row, pinned under the pages.
            var fullSet = MenuUI.NewRect(left.transform, "FullSetRow");
            MenuUI.Place(fullSet, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 72f), new Vector2(-16f, 134f));
            MenuUI.VStack(fullSet.gameObject, 0f, 0);
            _fullSetCycler = MenuUI.CreateCycler(fullSet, "Full set", () => CycleFullSet(-1), () => CycleFullSet(1));
            var applyRow = MenuUI.NewRect(left.transform, "ApplyRow");
            MenuUI.Place(applyRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-16f, 64f));
            var applyBtn = MenuUI.CreateButton(applyRow, "APPLY SET TO THIS TAB", 20, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, ApplyFullSetToTab);
            MenuUI.Fill((RectTransform)applyBtn.transform);

            // Right column: stats + selected part
            var right = MenuUI.Panel(root, "StatsColumn", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.72f));
            MenuUI.Place(right.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-532f, 420f), new Vector2(-32f, -108f));
            var statsStack = MenuUI.NewRect(right.transform, "Stats");
            MenuUI.Fill(statsStack);
            MenuUI.VStack(statsStack.gameObject, 10f, 24);
            var statsTitle = MenuUI.Label(statsStack, "LOADOUT STATS", 26, IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(statsTitle, 40f);
            AddStat(statsStack, "Armor");
            AddStat(statsStack, "Speed");
            AddStat(statsStack, "Firepower");
            AddStat(statsStack, "Cooling");
            var gap = MenuUI.NewRect(statsStack, "Gap");
            MenuUI.Size(gap, 12f);
            _selectedTitle = MenuUI.Label(statsStack, "SELECTED PART", 22, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(_selectedTitle, 34f);
            _selectedPerk = MenuUI.Label(statsStack, "Pick a part on the left.", 20, IronFaunaPalette.UiText, TextAnchor.UpperLeft);
            MenuUI.Size(_selectedPerk, 90f);

            // Bottom bar
            var bottom = MenuUI.NewRect(root, "BottomBar");
            MenuUI.Place(bottom, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1180f, 32f), new Vector2(-32f, 104f));
            var bottomLayout = MenuUI.HStack(bottom.gameObject, 12f, 0);
            bottomLayout.childForceExpandWidth = true;
            AddBarButton(bottom, "BACK", ShowTitle, IronFaunaPalette.UiButton);
            AddBarButton(bottom, "RANDOMIZE", Randomize, IronFaunaPalette.UiButton);
            AddBarButton(bottom, "RESET", ResetLoadout, IronFaunaPalette.UiButton);
            AddBarButton(bottom, "SAVE", Save, IronFaunaPalette.UiButton);
            AddBarButton(bottom, "DEPLOY", () => IronFaunaGameFlow.Deploy(_loadout), IronFaunaPalette.CrimsonFront);

            _saveStatus = MenuUI.Label(root, "", 18, IronFaunaPalette.UiTextDim, TextAnchor.LowerRight);
            MenuUI.Place(_saveStatus.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1180f, 108f), new Vector2(-32f, 140f));

            var hint = MenuUI.Label(root, "DRAG TO ROTATE   //   SCROLL TO ZOOM   //   ESC FOR TITLE", 18, IronFaunaPalette.UiTextDim, TextAnchor.LowerRight);
            MenuUI.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1180f, 140f), new Vector2(-32f, 172f));
        }

        private void AddBarButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color normal)
        {
            var b = MenuUI.CreateButton(parent, label, 24, normal, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, action);
            MenuUI.Size(b, 72f, 200f, 1f);
        }

        private void AddStat(Transform parent, string name)
        {
            var fill = MenuUI.StatBar(parent, name, out Text value);
            _statBars[name] = new KeyValuePair<RectTransform, Text>(fill, value);
        }

        private GameObject NewPage(Transform pages, Tab tab)
        {
            var page = MenuUI.NewRect(pages, "Page_" + tab);
            MenuUI.Fill(page);
            MenuUI.VStack(page.gameObject, 8f, 0);
            _tabPages[tab] = page.gameObject;
            return page.gameObject;
        }

        private void BuildFramePage(Transform pages)
        {
            var page = NewPage(pages, Tab.Frame);
            _chassisCycler = MenuUI.CreateCycler(page.transform, "Chassis", ToggleChassis, ToggleChassis);
            _poseCycler = MenuUI.CreateCycler(page.transform, "Stance", () => CyclePose(-1), () => CyclePose(1));
            _paintCycler = MenuUI.CreateCycler(page.transform, "Paint", () => CyclePaint(-1), () => CyclePaint(1));
            _torsoWeightCycler = MenuUI.CreateCycler(page.transform, "Torso weight", () => CycleTorsoWeight(-1), () => CycleTorsoWeight(1));
            _legWeightCycler = MenuUI.CreateCycler(page.transform, "Leg weight", () => CycleLegWeight(-1), () => CycleLegWeight(1));
            _viewCycler = MenuUI.CreateCycler(page.transform, "View", ToggleSkeletonView, ToggleSkeletonView);
            var note = MenuUI.Label(page.transform,
                "Pick a torso and leg weight class, then clamp armor onto the skeleton. Any front half fits any back half; the seam always runs down the sides.",
                19, IronFaunaPalette.UiTextDim, TextAnchor.UpperLeft);
            MenuUI.Size(note, 90f);
        }

        private void BuildSlotPage(Transform pages, Tab tab, SlotRow[] rows)
        {
            var page = NewPage(pages, tab);
            if (tab == Tab.Arms)
            {
                _armsNote = MenuUI.Label(page.transform, "QUADRUPED CHASSIS HAS NO ARMS", 20, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(_armsNote, 36f);
            }
            foreach (var row in rows)
            {
                ClamshellSlot slot = row.slot;
                var cycler = MenuUI.CreateCycler(page.transform, row.label, () => CycleSlot(slot, -1), () => CycleSlot(slot, 1));
                _slotCyclers.Add(new KeyValuePair<ClamshellSlot, MenuUI.Cycler>(slot, cycler));
            }
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            foreach (var kv in _tabPages) kv.Value.SetActive(kv.Key == tab);
            foreach (var kv in _tabButtons)
            {
                if (kv.Value != null) kv.Value.color = kv.Key == tab ? IronFaunaPalette.UiSelect : IronFaunaPalette.UiButton;
            }
            FrameCameraForTab();
            RefreshAll();
        }

        private void FrameCameraForTab()
        {
            var mech = _stage.Mech;
            switch (_tab)
            {
                // Offsets keep the mech centred in the gap between the parts column and the stats column.
                case Tab.Head: _stage.Frame(mech.NeckHeight + 0.3f, 4.2f, 0.2f); break;
                case Tab.Body: _stage.Frame(mech.ChestHeight - 0.4f, 6.0f, 0.3f); break;
                case Tab.Arms: _stage.Frame(mech.ChestHeight - 0.6f, 6.0f, 0.3f); break;
                case Tab.Legs: _stage.Frame(mech.HipHeight * 0.5f, 6.5f, 0.3f); break;
                default: _stage.Frame(mech.HipHeight + 0.4f, 10f, 0.5f); break;
            }
        }

        private void CycleSlot(ClamshellSlot slot, int delta)
        {
            var set = ClamshellCatalog.Cycle(_loadout.Get(slot), delta);
            _loadout.Set(slot, set);
            ShowSelected(slot, set);
            Changed();
        }

        private void CycleFullSet(int delta)
        {
            _fullSet = ClamshellCatalog.Cycle(_fullSet, delta);
            _fullSetCycler.Value.text = ClamshellCatalog.SetName(_fullSet, false).ToUpperInvariant();
            _selectedTitle.text = ClamshellCatalog.SetName(_fullSet, _loadout.quadruped).ToUpperInvariant() + " SET";
            _selectedPerk.text = ClamshellCatalog.Perk(_fullSet);
        }

        private void ApplyFullSetToTab()
        {
            if (_tab == Tab.Frame)
            {
                for (int i = 0; i < MechLoadout.SlotCount; i++) _loadout.Set((ClamshellSlot)i, _fullSet);
            }
            else if (TabSlots.TryGetValue(_tab, out var rows))
            {
                foreach (var row in rows) _loadout.Set(row.slot, _fullSet);
            }
            Changed();
        }

        private void ToggleChassis()
        {
            _loadout.quadruped = !_loadout.quadruped;
            Changed();
            FrameCameraForTab();
        }

        private void CyclePose(int delta)
        {
            int count = PoseNames.Length;
            _loadout.legPose = (LegPosePreset)((((int)_loadout.legPose + delta) % count + count) % count);
            Changed();
            FrameCameraForTab();
        }

        private void CycleTorsoWeight(int delta)
        {
            _loadout.torsoWeight = MechFrameSpec.Cycle(_loadout.torsoWeight, delta);
            Changed();
        }

        private void CycleLegWeight(int delta)
        {
            _loadout.legWeight = MechFrameSpec.Cycle(_loadout.legWeight, delta);
            Changed();
        }

        /// <summary>Garage-only: hide the armor to inspect the bare skeleton and its weapon sockets.</summary>
        private void ToggleSkeletonView()
        {
            _skeletonView = !_skeletonView;
            RebuildMech();
            RefreshAll();
        }

        private void CyclePaint(int delta)
        {
            _loadout.paintScheme += delta;
            int count = IronFaunaPalette.PaintSchemes.Length;
            _loadout.paintScheme = ((_loadout.paintScheme % count) + count) % count;
            Changed();
        }

        private void Randomize()
        {
            for (int i = 0; i < MechLoadout.SlotCount; i++) _loadout.Set((ClamshellSlot)i, ClamshellCatalog.Random());
            _loadout.paintScheme = Random.Range(0, IronFaunaPalette.PaintSchemes.Length);
            Changed();
        }

        private void ResetLoadout()
        {
            bool quad = _loadout.quadruped;
            _loadout = MechLoadout.Default();
            _loadout.quadruped = quad;
            _skeletonView = false;
            Changed();
        }

        private void Save()
        {
            MechLoadoutStore.Save(_loadout);
            _dirty = false;
            RefreshAll();
        }

        private void Changed()
        {
            _dirty = true;
            RebuildMech();
            RefreshAll();
        }

        private void RebuildMech()
        {
            _stage.Mech.ApplyLoadout(_loadout);
            _stage.Mech.showArmor = !_skeletonView;
            _stage.Mech.RebuildMech();
        }

        private void ShowSelected(ClamshellSlot slot, ClamshellSetArchetype set)
        {
            _selectedTitle.text = ClamshellCatalog.PartName(slot, set, _loadout.quadruped).ToUpperInvariant();
            _selectedPerk.text = ClamshellCatalog.Perk(set);
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------

        private void RefreshAll()
        {
            if (_garageCanvas == null) return;
            bool quad = _loadout.quadruped;

            _chassisCycler.Value.text = quad ? "QUADRUPED BEAST" : "HUMANOID";
            _poseCycler.Value.text = PoseNames[Mathf.Clamp((int)_loadout.legPose, 0, PoseNames.Length - 1)].ToUpperInvariant();
            _paintCycler.Value.text = IronFaunaPalette.GetPaint(_loadout.paintScheme).name.ToUpperInvariant();
            _torsoWeightCycler.Value.text = _loadout.torsoWeight.ToString().ToUpperInvariant();
            _legWeightCycler.Value.text = _loadout.legWeight.ToString().ToUpperInvariant();
            _viewCycler.Value.text = _skeletonView ? "SKELETON ONLY" : "ARMORED";
            _fullSetCycler.Value.text = ClamshellCatalog.SetName(_fullSet, false).ToUpperInvariant();

            foreach (var kv in _slotCyclers)
            {
                bool used = MechLoadout.SlotUsed(kv.Key, quad);
                kv.Value.SetInteractable(used);
                kv.Value.Value.text = used ? ClamshellCatalog.PartName(kv.Key, _loadout.Get(kv.Key), quad).ToUpperInvariant() : "—";
            }
            if (_armsNote != null) _armsNote.gameObject.SetActive(quad);

            var stats = _loadout.ComputeStats();
            SetStat("Armor", stats.armor, 330f);
            SetStat("Speed", stats.speed, 240f);
            SetStat("Firepower", stats.firepower, 240f);
            SetStat("Cooling", stats.cooling, 240f);

            _headerInfo.text = $"{(quad ? "QUADRUPED" : "HUMANOID")}   //   {IronFaunaPalette.GetPaint(_loadout.paintScheme).name.ToUpperInvariant()}   //   {stats.cost} CR";
            _saveStatus.text = _dirty ? "UNSAVED CHANGES" : "LOADOUT SAVED";
        }

        private void SetStat(string name, int value, float max)
        {
            if (!_statBars.TryGetValue(name, out var bar)) return;
            float t = Mathf.Clamp01(value / max);
            bar.Key.anchorMax = new Vector2(t, 1f);
            bar.Value.text = value.ToString();
        }
    }
}
