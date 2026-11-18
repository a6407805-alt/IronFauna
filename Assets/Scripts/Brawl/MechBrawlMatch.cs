using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Clamshell;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    public enum BrawlGameMode
    {
        FreeForAll = 0,
        CoopCampaign = 1,
        TeamBrawl = 2,
    }

    [System.Serializable]
    public class BrawlMatchConfig
    {
        public BrawlGameMode mode = BrawlGameMode.FreeForAll;
        public BrawlStageId stage = BrawlStageId.BasaltMesa;
        public int localPlayers = 1;       // 1..4 split-screen players on this PC
        public int totalFighters = 8;      // up to 8 fighters in the arena
        public BrawlNetRole netRole = BrawlNetRole.Offline;
        public string hostIp = "127.0.0.1";
        public int campaignAct = 0;        // 0..4 across the 5 stages
    }

    /// <summary>
    /// 8-player mech brawler (Gang Beasts / MMA / Kickboxing / Jiu-Jitsu active-ragdoll combat):
    ///  - Modes: 8-Player Free-For-All, 8-Player Co-Op Campaign (5 Acts across 5 Stages + Bosses), and 4v4 Team Brawl.
    ///  - Local Co-Op: 1–4 players on one screen with automatic split-screen cameras.
    ///  - Online 8P: Host or Join over UDP (:7777) via MechBrawlNet.
    ///  - 5 Destructible Stages: Basalt Mesa, Foundry Crucible, Orbital Dish, Scrapyard Colosseum, Glacier Trench.
    ///  - Distinct Light / Medium / Heavy robot presets with real combat advantages.
    /// </summary>
    public class MechBrawlMatch : MonoBehaviour
    {
        public const float ArenaHalf = 17f;
        public const float IslandRadius = 34f;
        public const float ArenaTop = 6f;

        public BrawlMatchConfig config = new BrawlMatchConfig();

        private static readonly string[] CampaignActTitles =
        {
            "ACT I · SCRAPYARD QUALIFIERS",
            "ACT II · BASALT MESA AMBUSH",
            "ACT III · GLACIER TRENCH SIEGE",
            "ACT IV · FOUNDRY HEAVYWEIGHTS",
            "ACT V · ORBITAL DISH GRAND CHAMPION",
        };

        private static readonly BrawlStageId[] CampaignActStages =
        {
            BrawlStageId.ScrapyardColosseum,
            BrawlStageId.BasaltMesa,
            BrawlStageId.GlacierTrench,
            BrawlStageId.FoundryCrucible,
            BrawlStageId.OrbitalDish,
        };

        private class Fighter
        {
            public string name;
            public string weightTag;
            public MechRagdoll ragdoll;
            public MechBoxer ghost;
            public GameObject physGo, ghostGo;
            public bool player;
            public int localPlayerIndex = -1;
            public int team; // 0 = FFA, 1 = Allies, 2 = Enemies
            public bool isBoss;
            public bool out_;
        }

        private readonly List<Fighter> _fighters = new List<Fighter>();
        private readonly List<MechRagdoll> _ragdollList = new List<MechRagdoll>();
        private readonly List<Camera> _extraCameras = new List<Camera>();
        private readonly List<MechBrawlCamera> _playerCams = new List<MechBrawlCamera>();

        private MechLoadout _loadout;
        private Camera _camera;
        private Transform _arena;
        private RectTransform _hpBar, _poiseBar;
        private Text _banner, _alive, _moveLabel, _stageHeader, _postureLabel;
        private MechBoxerPlayerInput _input;
        private MechBrawlNet _net;
        private float _bannerT, _resetT = -1f, _targetT;
        private bool _roundWon;
        private float _savedFixedDt;

        public static MechBrawlMatch Begin(MechLoadout loadout, Camera camera, BrawlMatchConfig matchConfig = null)
        {
            var m = new GameObject("MechBrawlMatch").AddComponent<MechBrawlMatch>();
            m._loadout = loadout != null ? loadout.Clone() : MechLoadout.Default();
            m._camera = camera != null ? camera : Camera.main;
            if (matchConfig != null) m.config = matchConfig;
            m.Setup();
            return m;
        }

        private void Setup()
        {
            _savedFixedDt = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 1f / 90f;   // ragdolls need a finer physics step
            BreakableProp.Shattered += OnShattered;
            if (_camera != null)
            {
                BoxingSfx.Attach(_camera.gameObject);
            }
            _net = gameObject.AddComponent<MechBrawlNet>();
            if (config.netRole != BrawlNetRole.Offline)
            {
                _net.StartNetwork(config.netRole, config.hostIp, MechBrawlNet.DefaultPort);
            }
            BuildHud();
            StartRound();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ------------------------------------------------------------------ round & campaign
        private void StartRound()
        {
            foreach (var f in _fighters)
            {
                if (f.physGo != null) Destroy(f.physGo);
                if (f.ghostGo != null) Destroy(f.ghostGo);
            }
            _fighters.Clear();
            _ragdollList.Clear();
            _input = null;
            if (_arena != null) Destroy(_arena.gameObject);

            if (config.mode == BrawlGameMode.CoopCampaign)
            {
                config.campaignAct = Mathf.Clamp(config.campaignAct, 0, CampaignActStages.Length - 1);
                config.stage = CampaignActStages[config.campaignAct];
            }

            BuildArena(config.stage);

            int localCount = Mathf.Clamp(config.localPlayers, 1, 4);
            int total = Mathf.Clamp(config.totalFighters, Mathf.Max(2, localCount + 1), 8);
            SetupSplitScreenCameras(localCount);

            for (int i = 0; i < total; i++)
            {
                bool isLocalPlayer = i < localCount;
                int team = 0;
                bool isBoss = false;
                if (config.mode == BrawlGameMode.CoopCampaign)
                {
                    // Co-Op Campaign: up to 3 Allied slots (all local players + co-op AI allies) vs 5 Enemy AI wave/boss slots
                    int allyCount = Mathf.Max(localCount, 3);
                    team = i < allyCount ? 1 : 2;
                    isBoss = (config.campaignAct == 4 && i == total - 1) || (config.campaignAct >= 2 && i == total - 1);
                }
                else if (config.mode == BrawlGameMode.TeamBrawl)
                {
                    team = (i < total / 2 || isLocalPlayer) ? 1 : 2;
                }

                float ang = i * Mathf.PI * 2f / total;
                float spawnRadius = 10.5f;
                if (team == 1)
                {
                    ang = Mathf.Lerp(-0.7f, 0.7f, (i + 0.5f) / Mathf.Max(1f, total * 0.5f)) + Mathf.PI;
                }
                else if (team == 2)
                {
                    ang = Mathf.Lerp(-0.9f, 0.9f, (i - total * 0.4f + 0.5f) / Mathf.Max(1f, total * 0.6f));
                }

                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * spawnRadius;
                pos.y = MechBoxer.TryGround(pos + Vector3.up * 25f, out float gy) ? gy : ArenaTop;
                float yaw = ang * Mathf.Rad2Deg + 180f;

                MechLoadout l;
                string label;
                float hpMult = 1f;
                if (i == 0)
                {
                    l = _loadout.Clone();
                    label = localCount > 1 ? "P1" : "YOU";
                }
                else if (isLocalPlayer)
                {
                    l = MechLoadout.CreatePreset((i * 2) % MechLoadout.PresetCount, out string presetTitle, out _);
                    l.paintScheme = (_loadout.paintScheme + i) % IronFaunaPalette.PaintSchemes.Length;
                    label = $"P{i + 1}";
                }
                else if (isBoss)
                {
                    l = MechLoadout.CreatePreset(2, out _, out _); // Goliath Juggernaut Boss
                    l.paintScheme = 0;
                    label = config.campaignAct == 4 ? "APEX GOLIATH [BOSS]" : "IRON ENFORCER [BOSS]";
                    hpMult = config.campaignAct == 4 ? 1.85f : 1.45f;
                }
                else
                {
                    int presetIdx = SelectPresetForSlot(i, team);
                    l = MechLoadout.CreatePreset(presetIdx, out string presetName, out _);
                    string shortTitle = presetName.Split(' ')[0];
                    label = team == 1 ? $"ALLY {shortTitle}" : $"{shortTitle} {i}";
                    if (config.mode == BrawlGameMode.CoopCampaign)
                    {
                        hpMult = 0.85f + config.campaignAct * 0.12f;
                    }
                }

                Camera playerCam = isLocalPlayer && i < _playerCams.Count ? _playerCams[i].GetComponent<Camera>() : null;
                var spawned = Spawn(label, l, pos, yaw, isLocalPlayer, isLocalPlayer ? i : -1, team, isBoss, hpMult, playerCam);
                _fighters.Add(spawned);
                _ragdollList.Add(spawned.ragdoll);
                if (isLocalPlayer && i < _playerCams.Count)
                {
                    _playerCams[i].player = spawned.ragdoll;
                    _playerCams[i].Snap();
                }
            }

            Retarget();
            UpdateStageHeader();
            if (config.mode == BrawlGameMode.CoopCampaign)
            {
                Banner(CampaignActTitles[config.campaignAct], 2.2f);
            }
            else
            {
                Banner(BrawlTerrain.StageName(config.stage), 1.6f);
            }
        }

        private int SelectPresetForSlot(int slot, int team)
        {
            if (config.mode == BrawlGameMode.CoopCampaign && team == 2)
            {
                switch (config.campaignAct)
                {
                    case 0: return (slot % 2 == 0) ? 1 : 4; // Act 1: Light Strikers (Mantis / Afterburn)
                    case 1: return (slot % 2 == 0) ? 3 : 7; // Act 2: Jiu-Jitsu & Clinch (Ripjaw / Voltaic)
                    case 2: return (slot % 3 == 0) ? 6 : 9; // Act 3: Glacier Counter & Boxers
                    case 3: return (slot % 2 == 0) ? 2 : 5; // Act 4: Foundry Heavyweights (Goliath / Bastion)
                    default: return (slot + 1) % MechLoadout.PresetCount;
                }
            }
            return (slot * 3 + 1) % MechLoadout.PresetCount;
        }

        private void SetupSplitScreenCameras(int localCount)
        {
            foreach (var c in _extraCameras)
            {
                if (c != null) Destroy(c.gameObject);
            }
            _extraCameras.Clear();
            _playerCams.Clear();

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            for (int i = 0; i < localCount; i++)
            {
                Camera cam;
                if (i == 0)
                {
                    cam = _camera;
                }
                else
                {
                    var camGo = new GameObject($"SplitScreenCam_P{i + 1}");
                    camGo.transform.SetParent(transform, false);
                    cam = camGo.AddComponent<Camera>();
                    var urp = cam.GetUniversalAdditionalCameraData();
                    urp.renderPostProcessing = true;
                    _extraCameras.Add(cam);
                }

                cam.fieldOfView = 48f;
                cam.farClipPlane = 400f;
                cam.rect = ViewportRect(i, localCount);
                var brawlCam = cam.GetComponent<MechBrawlCamera>();
                if (brawlCam == null) brawlCam = cam.gameObject.AddComponent<MechBrawlCamera>();
                brawlCam.playerIndex = i;
                _playerCams.Add(brawlCam);
            }
        }

        private static Rect ViewportRect(int index, int count)
        {
            if (count <= 1) return new Rect(0f, 0f, 1f, 1f);
            if (count == 2) return index == 0 ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f);
            switch (index)
            {
                case 0: return new Rect(0f, 0.5f, 0.5f, 0.5f);
                case 1: return new Rect(0.5f, 0.5f, 0.5f, 0.5f);
                case 2: return new Rect(0f, 0f, 0.5f, 0.5f);
                default: return new Rect(0.5f, 0f, 0.5f, 0.5f);
            }
        }

        private Fighter Spawn(string name, MechLoadout l, Vector3 pos, float yaw, bool player, int localPlayerIdx, int team, bool isBoss, float hpMult, Camera playerCam)
        {
            var f = new Fighter { name = name, player = player, localPlayerIndex = localPlayerIdx, team = team, isBoss = isBoss };
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            float scale = isBoss ? 1.24f : 1f;

            f.ghostGo = new GameObject(name + "_Ghost");
            f.ghostGo.transform.SetPositionAndRotation(pos, rot);
            f.ghostGo.transform.localScale = Vector3.one * scale;
            var gb = f.ghostGo.AddComponent<MechClamshellBuilder>();
            gb.buildOnStart = false;
            gb.ApplyLoadout(l);
            gb.legPose = LegPosePreset.Pose1_HumanLeg;
            gb.RebuildMech();
            var gait = f.ghostGo.AddComponent<IronFauna.Gameplay.MechGaitAnimator>();
            if (player)
            {
                gait.dustScale = 1f;
                gait.Footfall += (at, strength) => BoxingFeel.Shake(0.05f * strength);
            }
            f.ghost = f.ghostGo.AddComponent<MechBoxer>();
            f.ghost.followGround = true;
            f.ghost.ringHalfSize = ArenaHalf + 30f;
            f.ghost.minDistance = 2.3f;
            if (player)
            {
                var inp = f.ghostGo.AddComponent<MechBoxerPlayerInput>();
                inp.playerIndex = Mathf.Max(0, localPlayerIdx);
                if (playerCam != null) f.ghost.moveReference = playerCam.transform;
                f.ghost.lockOn = false;
            }
            else
            {
                var ai = f.ghostGo.AddComponent<MechBoxerAI>();
                ai.skill = isBoss ? 0.82f : (config.mode == BrawlGameMode.CoopCampaign ? Mathf.Clamp(0.42f + config.campaignAct * 0.1f, 0.4f, 0.85f) : Random.Range(0.42f, 0.72f));
                ai.edgeHalfSize = IslandRadius * 0.6f;
            }

            f.physGo = new GameObject(name);
            f.physGo.transform.SetPositionAndRotation(pos, rot);
            f.physGo.transform.localScale = Vector3.one * scale;
            var pb = f.physGo.AddComponent<MechClamshellBuilder>();
            pb.buildOnStart = false;
            pb.ApplyLoadout(l);
            pb.legPose = LegPosePreset.Pose1_HumanLeg;
            pb.RebuildMech();
            f.ragdoll = f.physGo.AddComponent<MechRagdoll>();
            f.ragdoll.team = team;
            f.ragdoll.Build(pb, f.ghost);
            f.ragdoll.ApplyLoadoutStats(l, hpMult);
            f.weightTag = f.ragdoll.weightLabel;
            f.ragdoll.Hit += OnHit;
            f.ragdoll.ArmorShed += (who, at) => BoxingSfx.Clank(0.3f);
            f.ragdoll.KnockedOut += who => { if (who == _fighters[0].ragdoll) Banner("K.O.", 2.5f); };
            f.ragdoll.GrappleEvent += OnGrapple;
            f.ghost.PunchThrown += (who, power) => { if (player) BoxingSfx.Whoosh(power); };
            return f;
        }

        private void BuildArena(BrawlStageId stage)
        {
            _arena = new GameObject("BrawlArena").transform;
            _arena.SetParent(transform, false);
            int seed = Random.Range(0, 100000);
            BrawlTerrain.Build(_arena, ArenaTop, IslandRadius, seed, stage);
            Physics.SyncTransforms();
            BrawlTerrain.PopulateDestructibles(_arena, stage, seed + 7);

            BrawlTerrain.StageColors(stage, out Color groundCol, out _, out _, out Color skyCol, out Color sunCol);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (!l.transform.IsChildOf(transform)) l.enabled = false;
            foreach (var cam in _playerCams)
            {
                var c = cam.GetComponent<Camera>();
                if (c != null)
                {
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = skyCol;
                }
            }
            if (_camera != null)
            {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = skyCol;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = skyCol;
            RenderSettings.fogStartDistance = 42f;
            RenderSettings.fogEndDistance = 185f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = IronFaunaPalette.Lerp(skyCol, IronFaunaPalette.ShadowSlate, 0.35f);
            RenderSettings.ambientEquatorColor = IronFaunaPalette.Lerp(groundCol, skyCol, 0.4f);
            RenderSettings.ambientGroundColor = IronFaunaPalette.Lerp(groundCol, IronFaunaPalette.ShadowSlate, 0.6f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_arena, false);
            sunGo.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = sunCol;
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            var fillGo = new GameObject("SkyFill");
            fillGo.transform.SetParent(_arena, false);
            fillGo.transform.rotation = Quaternion.Euler(60f, 140f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = skyCol;
            fill.intensity = 0.38f;
            fill.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ hud
        private void BuildHud()
        {
            MenuUI.EnsureEventSystem();
            var canvas = MenuUI.CreateCanvas("BrawlHud", 5);
            canvas.transform.SetParent(transform, false);
            Transform root = canvas.transform;
            _hpBar = Bar(root, "HULL", new Vector2(0.03f, 1f), new Vector2(0.33f, 1f), -110f, IronFaunaPalette.CrimsonFront);
            _poiseBar = Bar(root, "BALANCE", new Vector2(0.03f, 1f), new Vector2(0.24f, 1f), -170f, IronFaunaPalette.CyanEnergy);

            _stageHeader = MenuUI.Label(root, "", 24, IronFaunaPalette.UiAccent, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Place(_stageHeader.rectTransform, new Vector2(0.03f, 1f), new Vector2(0.58f, 1f), new Vector2(0f, -72f), new Vector2(0f, -24f));

            _postureLabel = MenuUI.Label(root, "", 22, IronFaunaPalette.HazardYellow, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUI.Place(_postureLabel.rectTransform, new Vector2(0.03f, 1f), new Vector2(0.45f, 1f), new Vector2(0f, -245f), new Vector2(0f, -205f));

            _alive = MenuUI.Label(root, "", 24, IronFaunaPalette.UiText, TextAnchor.UpperRight, FontStyle.Bold);
            MenuUI.Place(_alive.rectTransform, new Vector2(0.60f, 1f), new Vector2(0.97f, 1f), new Vector2(0f, -340f), new Vector2(0f, -32f));
            _banner = MenuUI.Label(root, "", 110, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(_banner.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -40f), new Vector2(0f, 240f));
            var help = MenuUI.Label(root,
                "WASD MOVE · CTRL SPRINT · X/ALT CROUCH · Z JIU-JITSU SIT & FLOOR SCOOT · LMB JAB · RMB CROSS · Q HOOK · E UPPERCUT · F/R/V KICKS · SCROLL+SPACE SPECIAL (20)\nHOLD G GRAB (Z/X DRAG ON FLOOR · LMB THROW · RMB JOINT-CRANK/TEAR LIMB · SPACE SUPLEX/SLAM) · TAB NEXT STAGE · F2 SPLIT-SCREEN CO-OP · ESC MENU",
                19, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(help.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), new Vector2(0f, 96f));

            var movePanel = MenuUI.Panel(root, "SpecialPanel", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.7f));
            MenuUI.Place(movePanel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-560f, 106f), new Vector2(-40f, 326f));
            _moveLabel = MenuUI.Label(movePanel.transform, "", 28, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            _moveLabel.supportRichText = true;
            MenuUI.Fill(_moveLabel.rectTransform);
        }

        private void UpdateStageHeader()
        {
            if (_stageHeader == null) return;
            string modeStr = config.mode == BrawlGameMode.CoopCampaign
                ? CampaignActTitles[Mathf.Clamp(config.campaignAct, 0, CampaignActTitles.Length - 1)]
                : config.mode == BrawlGameMode.TeamBrawl ? "4v4 SQUAD BRAWL" : "8-PLAYER FREE-FOR-ALL";
            string netStr = config.netRole == BrawlNetRole.Host
                ? $"  //  ONLINE HOST :{MechBrawlNet.DefaultPort} ({(_net != null ? _net.ConnectedClientCount + 1 : 1)}/8)"
                : config.netRole == BrawlNetRole.Client
                ? $"  //  ONLINE CLIENT ({config.hostIp}:{MechBrawlNet.DefaultPort})"
                : config.localPlayers > 1 ? $"  //  {config.localPlayers}P SPLIT-SCREEN CO-OP" : "";
            _stageHeader.text = $"{modeStr}   //   STAGE: {BrawlTerrain.StageName(config.stage)}{netStr}";
        }

        private void UpdateMoveLabel()
        {
            if (_moveLabel == null) return;
            if (_input == null && _fighters.Count > 0 && _fighters[0].ghost != null) _input = _fighters[0].ghost.GetComponent<MechBoxerPlayerInput>();
            if (_input == null) return;
            var list = MechBoxer.Specials;
            int i = _input.SpecialIndex, n = list.Length;
            string prev = MechBoxer.MoveName(list[(i - 1 + n) % n]), cur = MechBoxer.MoveName(list[i]), next = MechBoxer.MoveName(list[(i + 1) % n]);
            _moveLabel.text = $"<size=20><color=#8592A3>{prev}</color></size>\n<size=38>{cur}</size>\n<size=20><color=#8592A3>{next}</color></size>\n<size=17><color=#A9D4FF>SCROLL PICK ({i + 1}/{n}) · SPACE THROW</color></size>";
        }

        private RectTransform Bar(Transform root, string label, Vector2 a0, Vector2 a1, float y, Color color)
        {
            var back = MenuUI.Panel(root, label, IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.8f));
            MenuUI.Place(back.rectTransform, a0, a1, new Vector2(0f, y - 30f), new Vector2(0f, y));
            var fill = MenuUI.Panel(back.transform, "Fill", color);
            var rt = MenuUI.Fill(fill.rectTransform);
            var t = MenuUI.Label(root, label, 22, IronFaunaPalette.UiText, TextAnchor.LowerLeft, FontStyle.Bold);
            MenuUI.Place(t.rectTransform, a0, a1, new Vector2(0f, y), new Vector2(0f, y + 30f));
            return rt;
        }

        private static void SetBar(RectTransform bar, float v)
        {
            if (bar == null) return;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
        }

        private void Banner(string text, float seconds)
        {
            if (_banner == null) return;
            _banner.text = text;
            _bannerT = seconds;
        }

        // ------------------------------------------------------------------ feel
        private void OnHit(MechRagdoll victim, MechRagdoll attacker, Vector3 point, float damage, float power, bool ripped)
        {
            var me = _fighters.Count > 0 ? _fighters[0].ragdoll : null;
            bool mine = victim == me || attacker == me;
            float k = Mathf.Clamp01(damage / 25f);
            BoxingSfx.Impact(Mathf.Max(k, ripped ? 1f : 0f));
            if (mine)
            {
                BoxingFeel.HitStop(Mathf.Lerp(0.05f, 0.14f, k) + (ripped ? 0.15f : 0f));
                BoxingFeel.Shake(Mathf.Lerp(0.25f, 0.75f, k) + (ripped ? 0.4f : 0f));
                BoxingFeel.FovKick = Mathf.Lerp(1.5f, 5f, k);
            }
            if (ripped && attacker == me) Banner("LIMB TORN", 1.2f);
        }

        private void OnGrapple(MechRagdoll who, string what)
        {
            var me = _fighters.Count > 0 ? _fighters[0].ragdoll : null;
            if (what == "SLAM" || what == "SUPLEX" || what == "TORN" || what == "JOINT CRANK") { BoxingFeel.Shake(0.6f); BoxingFeel.HitStop(0.1f); }
            if (what == "THROW") BoxingFeel.Shake(0.3f);
            if (who == me && (what == "TORN" || what == "SLAM" || what == "SUPLEX" || what == "THROW" || what == "JOINT CRANK"))
                Banner(what == "TORN" ? "LIMB TORN" : what, 1.1f);
        }

        private void OnShattered(Vector3 at, float size)
        {
            BoxingSfx.Impact(0.6f + size * 0.4f);
            if (_fighters.Count > 0 && Vector3.Distance(at, _fighters[0].ragdoll.Position) < 15f) BoxingFeel.Shake(0.3f + size * 0.4f);
        }

        // ------------------------------------------------------------------ loop
        private void Retarget()
        {
            foreach (var f in _fighters)
            {
                if (f.out_ || f.ragdoll == null) continue;
                Fighter best = null;
                float bestD = float.MaxValue;
                foreach (var o in _fighters)
                {
                    if (o == f || o.out_ || o.ragdoll == null) continue;
                    if (f.team != 0 && o.team == f.team) continue; // Allies target Enemies only!
                    float d = (o.ragdoll.Position - f.ragdoll.Position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = o; }
                }
                f.ghost.opponent = best != null ? best.ghost : null;
                if (f.player && f.localPlayerIndex >= 0 && f.localPlayerIndex < _playerCams.Count)
                {
                    _playerCams[f.localPlayerIndex].target = best != null ? best.ragdoll : null;
                }
            }
        }

        private void Update()
        {
            BoxingFeel.Tick(Time.unscaledDeltaTime);
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (kb != null && kb.tabKey.wasPressedThisFrame)
            {
                // Cycle to the next stage anytime with Tab
                config.stage = (BrawlStageId)(((int)config.stage + 1) % BrawlTerrain.StageCount);
                if (config.mode == BrawlGameMode.CoopCampaign) config.campaignAct = (config.campaignAct + 1) % CampaignActStages.Length;
                StartRound();
                return;
            }
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                // Cycle 1..4 Local Split-Screen Players live with F2
                config.localPlayers = (config.localPlayers % 4) + 1;
                StartRound();
                return;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (_net != null && config.netRole != BrawlNetRole.Offline && _fighters.Count > 0)
            {
                _net.PollNetwork(_ragdollList, _fighters[0].ghost != null ? _fighters[0].ghost.intent : default);
                UpdateStageHeader();
            }

            UpdateMoveLabel();
            _targetT -= Time.unscaledDeltaTime;
            if (_targetT <= 0f) { _targetT = 0.4f; Retarget(); }

            // knockouts and ring-outs
            int aliveTotal = 0, aliveAllies = 0, aliveEnemies = 0;
            Fighter last = null;
            foreach (var f in _fighters)
            {
                if (!f.out_ && (f.ragdoll.IsKO || f.ragdoll.Position.y < ArenaTop - 13f))
                {
                    f.out_ = true;
                    if (f.ragdoll.Position.y < ArenaTop - 13f && f.player) Banner("RING OUT", 2f);
                }
                if (!f.out_)
                {
                    aliveTotal++;
                    last = f;
                    if (f.team == 1) aliveAllies++;
                    else if (f.team == 2) aliveEnemies++;
                }
            }

            if (_alive != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var f in _fighters)
                {
                    string tag = $"[{f.weightTag}]";
                    if (f.out_) sb.AppendLine($"<color=#8592A3>{f.name} {tag}  OUT</color>");
                    else if (f.team == 1) sb.AppendLine($"<color=#24E5FF>{f.name} {tag}  {Mathf.CeilToInt(f.ragdoll.health)}HP</color>");
                    else if (f.isBoss) sb.AppendLine($"<color=#FF7A1A>{f.name} {tag}  {Mathf.CeilToInt(f.ragdoll.health)}HP</color>");
                    else sb.AppendLine($"{f.name} {tag}  {Mathf.CeilToInt(f.ragdoll.health)}HP");
                }
                _alive.text = sb.ToString();
                _alive.supportRichText = true;
            }

            if (_fighters.Count > 0 && _fighters[0].ragdoll != null)
            {
                var me = _fighters[0].ragdoll;
                SetBar(_hpBar, me.health / me.maxHealth);
                SetBar(_poiseBar, me.Poise01);
                if (_postureLabel != null && me.ghost != null)
                {
                    string posture = me.ghost.IsSeated ? "POSTURE: JIU-JITSU FLOOR SIT / SCOOT (Z)"
                                   : me.ghost.IsCrouching ? "POSTURE: MMA CROUCH / TAKEDOWN (X)"
                                   : $"FRAME: {me.weightLabel}" + (me.ghost.hyperArmor ? " (HYPER-ARMOR)" : "");
                    _postureLabel.text = posture;
                }
            }

            if (_resetT < 0f)
            {
                if (config.mode == BrawlGameMode.CoopCampaign || config.mode == BrawlGameMode.TeamBrawl)
                {
                    if (aliveEnemies == 0)
                    {
                        _roundWon = true;
                        bool campaignClear = config.mode == BrawlGameMode.CoopCampaign && config.campaignAct >= CampaignActStages.Length - 1;
                        Banner(campaignClear ? "CAMPAIGN CHAMPION!" : (config.mode == BrawlGameMode.CoopCampaign ? "STAGE CLEAR!" : "SQUAD VICTORY!"), 3.2f);
                        _resetT = 4.2f;
                    }
                    else if (aliveAllies == 0)
                    {
                        _roundWon = false;
                        Banner("SQUAD WIPED · RETRYING", 3.2f);
                        _resetT = 4.2f;
                    }
                }
                else if (aliveTotal <= 1)
                {
                    _roundWon = last != null && last.player;
                    Banner(last != null && last.player ? $"{last.name} WINS" : last != null ? last.name + " WINS" : "DRAW", 3.5f);
                    _resetT = 4.5f;
                }
            }

            if (_resetT > 0f)
            {
                _resetT -= Time.unscaledDeltaTime;
                if (_resetT <= 0f)
                {
                    _resetT = -1f;
                    if (config.mode == BrawlGameMode.CoopCampaign && _roundWon)
                    {
                        config.campaignAct = (config.campaignAct + 1) % CampaignActStages.Length;
                    }
                    StartRound();
                }
            }

            if (_bannerT > 0f)
            {
                _bannerT -= Time.unscaledDeltaTime;
                var c = _banner.color;
                c.a = Mathf.Clamp01(_bannerT * 2.5f);
                _banner.color = c;
            }
        }

        private void Exit()
        {
            BoxingFeel.Clear();
            Time.fixedDeltaTime = _savedFixedDt;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            BreakableProp.Shattered -= OnShattered;
            BoxingFeel.Clear();
            if (_savedFixedDt > 0f) Time.fixedDeltaTime = _savedFixedDt;
        }
    }
}
