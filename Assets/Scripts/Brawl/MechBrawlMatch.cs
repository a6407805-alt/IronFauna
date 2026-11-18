using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Clamshell;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Free-for-all mech brawl (Gang Beasts style, local test build): a raised arena over the void with
    /// destructible crates, pillars and walls; the player plus bots, each an active-ragdoll mech driven by a hidden
    /// animated ghost. Knock everyone out or off the edge. Last mech standing wins; the arena resets.
    /// Built so a network layer can later own spawning and feed each ghost's intent.
    /// </summary>
    public class MechBrawlMatch : MonoBehaviour
    {
        public const float ArenaHalf = 17f;
        public const float IslandRadius = 34f;
        public const float ArenaTop = 6f;
        public int bots = 3;

        private class Fighter
        {
            public string name;
            public MechRagdoll ragdoll;
            public MechBoxer ghost;
            public GameObject physGo, ghostGo;
            public bool player;
            public bool out_;
        }

        private readonly List<Fighter> _fighters = new List<Fighter>();
        private MechLoadout _loadout;
        private Camera _camera;
        private MechBrawlCamera _cam;
        private Transform _arena;
        private RectTransform _hpBar, _poiseBar;
        private Text _banner, _alive, _moveLabel;
        private MechBoxerPlayerInput _input;
        private float _bannerT, _resetT = -1f, _targetT;
        private float _savedFixedDt;

        public static MechBrawlMatch Begin(MechLoadout loadout, Camera camera)
        {
            var m = new GameObject("MechBrawlMatch").AddComponent<MechBrawlMatch>();
            m._loadout = loadout.Clone();
            m._camera = camera != null ? camera : Camera.main;
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
                _camera.fieldOfView = 48f;
                _camera.farClipPlane = 400f;
                _cam = _camera.GetComponent<MechBrawlCamera>();
                if (_cam == null) _cam = _camera.gameObject.AddComponent<MechBrawlCamera>();
                BoxingSfx.Attach(_camera.gameObject);
            }
            BuildHud();
            StartRound();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ------------------------------------------------------------------ round
        private void StartRound()
        {
            foreach (var f in _fighters)
            {
                if (f.physGo != null) Destroy(f.physGo);
                if (f.ghostGo != null) Destroy(f.ghostGo);
            }
            _fighters.Clear();
            _input = null;
            if (_arena != null) Destroy(_arena.gameObject);
            BuildArena();

            int count = bots + 1;
            for (int i = 0; i < count; i++)
            {
                float ang = i * Mathf.PI * 2f / count;
                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 9f;
                pos.y = MechBoxer.TryGround(pos + Vector3.up * 20f, out float gy) ? gy : ArenaTop;
                float yaw = ang * Mathf.Rad2Deg + 180f;
                var l = _loadout.Clone();
                l.quadruped = false;
                l.legPose = LegPosePreset.Pose1_HumanLeg;
                if (i > 0)
                {
                    for (int s = 0; s < MechLoadout.SlotCount; s++) l.slotSets[s] = (int)ClamshellSetArchetype.Set01_Ironclad;
                    l.paintScheme = (_loadout.paintScheme + i * 2) % IronFaunaPalette.PaintSchemes.Length;
                    l.torsoWeight = l.legWeight = (MechWeightClass)(i % 3);
                }
                _fighters.Add(Spawn(i == 0 ? "YOU" : "BOT " + i, l, pos, yaw, i == 0));
            }
            if (_cam != null) { _cam.player = _fighters[0].ragdoll; _cam.Snap(); }
            Retarget();
            Banner("BRAWL", 1.4f);
        }

        private Fighter Spawn(string name, MechLoadout l, Vector3 pos, float yaw, bool player)
        {
            var f = new Fighter { name = name, player = player };
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            f.ghostGo = new GameObject(name + "_Ghost");
            f.ghostGo.transform.SetPositionAndRotation(pos, rot);
            var gb = f.ghostGo.AddComponent<MechClamshellBuilder>();
            gb.buildOnStart = false;
            gb.ApplyLoadout(l);
            gb.legPose = LegPosePreset.Pose1_HumanLeg;
            gb.RebuildMech();
            // legs: the Deploy walk cycle (planted feet, individual steps, stomps); the boxer poses the upper body
            f.ghostGo.AddComponent<IronFauna.Gameplay.MechGaitAnimator>();
            f.ghost = f.ghostGo.AddComponent<MechBoxer>();
            f.ghost.externalLegs = true;
            f.ghost.followGround = true;
            f.ghost.ringHalfSize = ArenaHalf + 30f;      // the arena edge is real: no invisible walls
            f.ghost.minDistance = 2.3f;
            if (player)
            {
                f.ghostGo.AddComponent<MechBoxerPlayerInput>();
                f.ghost.lockOn = false;     // free movement: faces where you steer, turns into a target only to hit it
            }
            else
            {
                var ai = f.ghostGo.AddComponent<MechBoxerAI>();
                ai.skill = Random.Range(0.35f, 0.65f);
                ai.edgeHalfSize = IslandRadius * 0.6f;
            }

            f.physGo = new GameObject(name);
            f.physGo.transform.SetPositionAndRotation(pos, rot);
            var pb = f.physGo.AddComponent<MechClamshellBuilder>();
            pb.buildOnStart = false;
            pb.ApplyLoadout(l);
            pb.legPose = LegPosePreset.Pose1_HumanLeg;
            pb.RebuildMech();
            f.ragdoll = f.physGo.AddComponent<MechRagdoll>();
            f.ragdoll.Build(pb, f.ghost);
            f.ragdoll.Hit += OnHit;
            f.ragdoll.ArmorShed += (who, at) => BoxingSfx.Clank(0.3f);
            f.ragdoll.KnockedOut += who => { if (who == _fighters[0].ragdoll) Banner("K.O.", 2.5f); };
            f.ghost.PunchThrown += (who, power) => { if (player) BoxingSfx.Whoosh(power); };
            if (player) f.ghost.Footfall += (at, strength) => BoxingFeel.Shake(0.05f * strength);
            return f;
        }

        private void BuildArena()
        {
            _arena = new GameObject("BrawlArena").transform;
            _arena.SetParent(transform, false);
            BrawlTerrain.Build(_arena, ArenaTop, IslandRadius, Random.Range(0, 100000));
            Physics.SyncTransforms();

            // destructible cover on the ground: rock pillars stand until smashed, crates are loose
            var rng = new System.Random(Random.Range(0, 100000));
            for (int i = 0; i < 6; i++)
            {
                float ang = i / 6f * Mathf.PI * 2f + 0.4f;
                Vector3 p = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Mathf.Lerp(13f, 22f, (float)rng.NextDouble());
                if (!MechBoxer.TryGround(p + Vector3.up * 20f, out float gy)) continue;
                float h = Mathf.Lerp(5f, 8f, (float)rng.NextDouble());
                BreakableProp.Create(_arena, "RockPillar", new Vector3(p.x, gy + h * 0.5f - 0.4f, p.z), new Vector3(1.6f, h, 1.6f),
                    IronFaunaPalette.TerrainRock, 280f, true, new Vector3Int(2, 5, 2));
            }
            for (int i = 0; i < 10; i++)
            {
                Vector3 p = new Vector3(Random.Range(-20f, 20f), 0f, Random.Range(-20f, 20f));
                if (p.magnitude < 6f) p = p.normalized * 8f;
                if (!MechBoxer.TryGround(p + Vector3.up * 20f, out float gy)) continue;
                float sz = Random.Range(1.0f, 1.8f);
                BreakableProp.Create(_arena, "Crate", new Vector3(p.x, gy + sz * 0.5f + 0.05f, p.z), Vector3.one * sz, IronFaunaPalette.HazardYellow, 120f, false, new Vector3Int(2, 2, 2));
            }

            // outdoors: overcast sky, distance haze, a low warm sun; the hangar studio lights are switched off
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (!l.transform.IsChildOf(transform)) l.enabled = false;
            if (_camera != null)
            {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = IronFaunaPalette.SkyHaze;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = IronFaunaPalette.SkyHaze;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 190f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = IronFaunaPalette.Lerp(IronFaunaPalette.SkyHaze, IronFaunaPalette.ShadowSlate, 0.35f);
            RenderSettings.ambientEquatorColor = IronFaunaPalette.Lerp(IronFaunaPalette.TerrainDirt, IronFaunaPalette.SkyHaze, 0.4f);
            RenderSettings.ambientGroundColor = IronFaunaPalette.Lerp(IronFaunaPalette.TerrainDirt, IronFaunaPalette.ShadowSlate, 0.6f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_arena, false);
            sunGo.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = IronFaunaPalette.SunWarm;
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            var fillGo = new GameObject("SkyFill");
            fillGo.transform.SetParent(_arena, false);
            fillGo.transform.rotation = Quaternion.Euler(60f, 140f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = IronFaunaPalette.SkyHaze;
            fill.intensity = 0.35f;
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
            _alive = MenuUI.Label(root, "", 30, IronFaunaPalette.UiText, TextAnchor.UpperRight, FontStyle.Bold);
            MenuUI.Place(_alive.rectTransform, new Vector2(0.6f, 1f), new Vector2(0.97f, 1f), new Vector2(0f, -200f), new Vector2(0f, -40f));
            _banner = MenuUI.Label(root, "", 140, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(_banner.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -40f), new Vector2(0f, 260f));
            var help = MenuUI.Label(root,
                "WASD MOVE   ·   CTRL SPRINT   ·   LMB JAB   ·   RMB CROSS   ·   Q HOOK   ·   E UPPERCUT   ·   SHIFT GUARD   ·   C SLIP   ·   SCROLL + SPACE SPECIAL   ·   ESC EXIT",
                22, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(help.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(0f, 80f));

            var movePanel = MenuUI.Panel(root, "SpecialPanel", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.7f));
            MenuUI.Place(movePanel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-560f, 110f), new Vector2(-50f, 330f));
            _moveLabel = MenuUI.Label(movePanel.transform, "", 30, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            _moveLabel.supportRichText = true;
            MenuUI.Fill(_moveLabel.rectTransform);
        }

        private void UpdateMoveLabel()
        {
            if (_moveLabel == null) return;
            if (_input == null && _fighters.Count > 0 && _fighters[0].ghost != null) _input = _fighters[0].ghost.GetComponent<MechBoxerPlayerInput>();
            if (_input == null) return;
            var list = MechBoxer.Specials;
            int i = _input.SpecialIndex, n = list.Length;
            string prev = MechBoxer.MoveName(list[(i - 1 + n) % n]), cur = MechBoxer.MoveName(list[i]), next = MechBoxer.MoveName(list[(i + 1) % n]);
            _moveLabel.text = $"<size=22><color=#8592A3>{prev}</color></size>\n<size=44>{cur}</size>\n<size=22><color=#8592A3>{next}</color></size>\n<size=18><color=#A9D4FF>SCROLL TO PICK  ·  SPACE TO THROW</color></size>";
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

        private void OnShattered(Vector3 at, float size)
        {
            BoxingSfx.Impact(0.6f + size * 0.4f);
            if (_fighters.Count > 0 && Vector3.Distance(at, _fighters[0].ragdoll.Position) < 12f) BoxingFeel.Shake(0.3f + size * 0.4f);
        }

        // ------------------------------------------------------------------ loop
        private void Retarget()
        {
            foreach (var f in _fighters)
            {
                if (f.out_) continue;
                Fighter best = null;
                float bestD = float.MaxValue;
                foreach (var o in _fighters)
                {
                    if (o == f || o.out_) continue;
                    float d = (o.ragdoll.Position - f.ragdoll.Position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = o; }
                }
                f.ghost.opponent = best != null ? best.ghost : null;
                if (f.player && _cam != null) _cam.target = best != null ? best.ragdoll : null;
            }
        }

        private void Update()
        {
            BoxingFeel.Tick(Time.unscaledDeltaTime);
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            UpdateMoveLabel();
            _targetT -= Time.unscaledDeltaTime;
            if (_targetT <= 0f) { _targetT = 0.4f; Retarget(); }

            // knockouts and ring-outs
            int alive = 0;
            Fighter last = null;
            foreach (var f in _fighters)
            {
                if (!f.out_ && (f.ragdoll.IsKO || f.ragdoll.Position.y < ArenaTop - 12f))
                {
                    f.out_ = true;
                    if (f.ragdoll.Position.y < ArenaTop - 12f && f.player) Banner("RING OUT", 2f);
                }
                if (!f.out_) { alive++; last = f; }
            }
            if (_alive != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var f in _fighters) sb.AppendLine(f.out_ ? $"<color=#8592A3>{f.name}  OUT</color>" : f.name);
                _alive.text = sb.ToString();
                _alive.supportRichText = true;
            }
            if (_fighters.Count > 0)
            {
                var me = _fighters[0].ragdoll;
                SetBar(_hpBar, me.health / me.maxHealth);
                SetBar(_poiseBar, me.Poise01);
            }

            if (_resetT < 0f && alive <= 1)
            {
                Banner(last != null && last.player ? "YOU WIN" : last != null ? last.name + " WINS" : "DRAW", 3.5f);
                _resetT = 4.5f;
            }
            if (_resetT > 0f)
            {
                _resetT -= Time.unscaledDeltaTime;
                if (_resetT <= 0f) { _resetT = -1f; StartRound(); }
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
