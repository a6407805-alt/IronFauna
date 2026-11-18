using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFauna.Clamshell;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Mech boxing: builds a ring in the current (menu) scene, the player's mech against a sparring partner,
    /// the fight camera and HUD, and drives impact feel (hit-stop, shake, sparks, synthesized sound).
    /// Knockouts reset the bout; Escape returns to the title screen.
    /// </summary>
    public class MechBoxingMatch : MonoBehaviour
    {
        public const float RingHalf = 7.4f;      // inside the ropes
        public const float DeckHeight = 0.55f;

        private MechBoxer _player, _cpu;
        private MechBoxingCamera _cam;
        private RectTransform _playerBar, _cpuBar;
        private RectTransform _playerLag, _cpuLag;
        private Text _banner;
        private float _bannerT;
        private float _resetT = -1f;
        private float _playerLagV = 1f, _cpuLagV = 1f;
        private ParticleSystem _sparks;
        private int _playerWins, _cpuWins;
        private Text _score;

        public static MechBoxingMatch Begin(MechLoadout playerLoadout, Camera camera)
        {
            var go = new GameObject("MechBoxingMatch");
            var m = go.AddComponent<MechBoxingMatch>();
            m.Build(playerLoadout, camera);
            return m;
        }

        // ------------------------------------------------------------------ setup
        private void Build(MechLoadout playerLoadout, Camera camera)
        {
            BuildRing();

            var pl = playerLoadout.Clone();
            pl.quadruped = false;
            pl.legPose = LegPosePreset.Pose1_HumanLeg;
            var cl = pl.Clone();
            for (int i = 0; i < MechLoadout.SlotCount; i++) cl.slotSets[i] = (int)ClamshellSetArchetype.Set01_Ironclad;
            cl.paintScheme = pl.paintScheme == 5 ? 0 : 5;     // Cobalt Lancer, or Crimson against a cobalt player
            cl.torsoWeight = MechWeightClass.Medium;
            cl.legWeight = MechWeightClass.Medium;

            _player = SpawnBoxer("PlayerBoxer", pl, new Vector3(0f, DeckHeight, -2.8f), 0f);
            _cpu = SpawnBoxer("SparringBoxer", cl, new Vector3(0f, DeckHeight, 2.8f), 180f);
            _player.opponent = _cpu;
            _cpu.opponent = _player;
            _player.gameObject.AddComponent<MechBoxerPlayerInput>();
            _cpu.gameObject.AddComponent<MechBoxerAI>();
            // the sparring partner is a touch slower so the player can feel out the controls
            _cpu.bounceHz = 1.55f;
            _cpu.maxSpeed = 2.7f;

            foreach (var b in new[] { _player, _cpu })
            {
                b.PunchThrown += (who, power) => BoxingSfx.Whoosh(power);
                b.PunchResolved += OnPunchResolved;
                b.KnockedOut += OnKnockedOut;
            }

            if (camera == null) camera = Camera.main;
            if (camera != null)
            {
                camera.fieldOfView = 46f;
                _cam = camera.GetComponent<MechBoxingCamera>();
                if (_cam == null) _cam = camera.gameObject.AddComponent<MechBoxingCamera>();
                _cam.player = _player;
                _cam.opponent = _cpu;
                _cam.Snap();
                BoxingSfx.Attach(camera.gameObject);
            }
            BuildHud();
            Banner("FIGHT", 1.4f);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private MechBoxer SpawnBoxer(string name, MechLoadout loadout, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var builder = go.AddComponent<MechClamshellBuilder>();
            builder.buildOnStart = false;
            builder.ApplyLoadout(loadout);
            builder.legPose = LegPosePreset.Pose1_HumanLeg;
            builder.RebuildMech();
            var boxer = go.AddComponent<MechBoxer>();
            boxer.ringHalfSize = RingHalf - 1.3f;
            // heavier frames bounce lower and slower and hit harder - feel the class
            float heavy = loadout.legWeight == MechWeightClass.Heavy ? 1f : loadout.legWeight == MechWeightClass.Light ? -1f : 0f;
            boxer.bounceHz = 1.7f - heavy * 0.18f;
            boxer.bounceHeight = 0.17f - heavy * 0.04f;
            boxer.maxSpeed = 3.0f - heavy * 0.45f;
            boxer.bodyFrequency = 4.2f - heavy * 0.5f;
            return boxer;
        }

        private void BuildRing()
        {
            var ring = new GameObject("BoxingRing").transform;
            ring.SetParent(transform, false);
            float outer = RingHalf + 0.9f;
            // platform: canvas deck on a dark skirt with an apron
            var deck = ClamshellParts.Spawn(ring, "RingDeck", ClamshellMeshFactory.BeveledBox(new Vector3(outer * 2f, DeckHeight, outer * 2f), 0.06f),
                new Vector3(0f, DeckHeight * 0.5f, 0f), Quaternion.identity, IronFaunaPalette.SteelDark);
            if (IronFaunaMaterials.Realistic) deck.GetComponent<MeshRenderer>().sharedMaterial = IronFaunaMaterials.StudioDeck();
            ClamshellParts.Spawn(ring, "RingCanvas", ClamshellMeshFactory.BeveledBox(new Vector3(RingHalf * 2f, 0.02f, RingHalf * 2f), 0.005f),
                new Vector3(0f, DeckHeight + 0.005f, 0f), Quaternion.identity, IronFaunaPalette.SteelBlack);
            // centre logo ring
            ClamshellParts.Spawn(ring, "RingLogo", ClamshellMeshFactory.Prism(2.2f, 0.012f, 48),
                new Vector3(0f, DeckHeight + 0.014f, 0f), Quaternion.identity, IronFaunaPalette.SteelDark);
            ClamshellParts.Spawn(ring, "RingLogoInner", ClamshellMeshFactory.Prism(1.9f, 0.016f, 48),
                new Vector3(0f, DeckHeight + 0.016f, 0f), Quaternion.identity, IronFaunaPalette.SteelBlack);

            Color[] pads = { IronFaunaPalette.CrimsonFront, IronFaunaPalette.CobaltFront, IronFaunaPalette.OffWhitePanel, IronFaunaPalette.OffWhitePanel };
            Vector3[] corners = { new Vector3(-1, 0, -1), new Vector3(1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1) };
            float postH = 3.6f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = corners[i] * (RingHalf + 0.35f);
                ClamshellParts.Spawn(ring, "Post" + i, ClamshellMeshFactory.Prism(0.16f, postH, 12),
                    new Vector3(c.x, DeckHeight + postH * 0.5f, c.z), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
                ClamshellParts.Spawn(ring, "Pad" + i, ClamshellMeshFactory.Prism(0.3f, 2.4f, 12),
                    new Vector3(c.x - corners[i].x * 0.12f, DeckHeight + 2.1f, c.z - corners[i].z * 0.12f), Quaternion.identity, pads[i]);
                ClamshellParts.Spawn(ring, "PostCap" + i, ClamshellMeshFactory.Prism(0.2f, 0.06f, 12),
                    new Vector3(c.x, DeckHeight + postH + 0.03f, c.z), Quaternion.identity, IronFaunaPalette.CyanEnergy, true);
            }
            // three ropes per side, at mech waist height
            float[] heights = { 1.3f, 2.1f, 2.9f };
            float span = (RingHalf + 0.35f) * 2f;
            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 off = rot * new Vector3(0f, 0f, RingHalf + 0.35f);
                foreach (float h in heights)
                {
                    ClamshellParts.Spawn(ring, "Rope", ClamshellMeshFactory.BeveledBox(new Vector3(span, 0.09f, 0.09f), 0.03f),
                        off + Vector3.up * (DeckHeight + h), rot, h > 2.5f ? IronFaunaPalette.OffWhitePanel : IronFaunaPalette.CrimsonFront);
                }
            }

            // overhead ring light so the fighters pop out of the dark hangar
            var lightGo = new GameObject("RingSpot");
            lightGo.transform.SetParent(ring, false);
            lightGo.transform.SetPositionAndRotation(new Vector3(0f, 16f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 70f;
            spot.innerSpotAngle = 40f;
            spot.range = 30f;
            spot.intensity = 200f;
            spot.color = IronFaunaPalette.StudioKey;
            spot.shadows = LightShadows.Soft;
        }

        private void BuildHud()
        {
            MenuUI.EnsureEventSystem();
            var canvas = MenuUI.CreateCanvas("BoxingHud", 5);
            canvas.transform.SetParent(transform, false);
            Transform root = canvas.transform;

            _playerBar = HealthBar(root, "YOU", true, out _playerLag);
            _cpuBar = HealthBar(root, "SPARRING PARTNER", false, out _cpuLag);

            _score = MenuUI.Label(root, "0 - 0", 40, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(_score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -120f), new Vector2(120f, -50f));

            _banner = MenuUI.Label(root, "", 150, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(_banner.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -40f), new Vector2(0f, 260f));

            var help = MenuUI.Label(root,
                "WASD MOVE   ·   LMB JAB   ·   RMB CROSS   ·   Q HOOK   ·   E UPPERCUT   ·   SHIFT GUARD   ·   SPACE SLIP   ·   ESC EXIT",
                22, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(help.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(0f, 80f));
        }

        private RectTransform HealthBar(Transform root, string label, bool left, out RectTransform lag)
        {
            float a0 = left ? 0.04f : 0.58f, a1 = left ? 0.42f : 0.96f;
            var back = MenuUI.Panel(root, label + "_Back", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.8f));
            MenuUI.Place(back.rectTransform, new Vector2(a0, 1f), new Vector2(a1, 1f), new Vector2(0f, -110f), new Vector2(0f, -70f));
            var lagImg = MenuUI.Panel(back.transform, "Lag", IronFaunaPalette.OffWhitePanel);
            lag = MenuUI.Fill(lagImg.rectTransform);
            lag.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            var fill = MenuUI.Panel(back.transform, "Fill", left ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.CobaltFront);
            var fillRt = MenuUI.Fill(fill.rectTransform);
            var name = MenuUI.Label(root, label, 26, IronFaunaPalette.UiText, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(name.rectTransform, new Vector2(a0, 1f), new Vector2(a1, 1f), new Vector2(0f, -64f), new Vector2(0f, -30f));
            return fillRt;
        }

        private void SetBar(RectTransform bar, float v, bool left)
        {
            if (bar == null) return;
            v = Mathf.Clamp01(v);
            bar.anchorMin = new Vector2(left ? 0f : 1f - v, 0f);
            bar.anchorMax = new Vector2(left ? v : 1f, 1f);
        }

        // ------------------------------------------------------------------ impacts
        private void OnPunchResolved(MechBoxer attacker, Vector3 point, float power, bool blocked, bool landed)
        {
            if (!landed) return;
            bool playerHit = attacker == _cpu;
            if (blocked)
            {
                BoxingSfx.Clank(power);
                BoxingFeel.HitStop(Mathf.Lerp(0.03f, 0.06f, power));
                BoxingFeel.Shake(Mathf.Lerp(0.12f, 0.25f, power));
                Sparks(point, 8, IronFaunaPalette.CyanEnergy);
                return;
            }
            BoxingSfx.Impact(power);
            BoxingFeel.HitStop(Mathf.Lerp(0.055f, 0.14f, power));
            BoxingFeel.Shake(Mathf.Lerp(0.25f, 0.7f, power) * (playerHit ? 1.15f : 1f));
            BoxingFeel.FovKick = Mathf.Lerp(1.5f, 5f, power);
            Sparks(point, Mathf.RoundToInt(Mathf.Lerp(14f, 40f, power)), IronFaunaPalette.OrangeJointCore);
        }

        private void OnKnockedOut(MechBoxer who)
        {
            if (who == _cpu) _playerWins++; else _cpuWins++;
            Banner("K.O.", 3f);
            BoxingFeel.HitStop(0.35f);
            BoxingFeel.Shake(1f);
            _resetT = 3.6f;
        }

        private void Sparks(Vector3 at, int count, Color color)
        {
            if (_sparks == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) return;
                var go = new GameObject("ImpactSparks");
                go.transform.SetParent(transform, false);
                _sparks = go.AddComponent<ParticleSystem>();
                _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _sparks.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.useUnscaledTime = true;     // sparks fly through the hit-stop: the freeze reads as power
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 16f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                main.gravityModifier = 1.6f;
                main.maxParticles = 400;
                main.playOnAwake = false;
                var emission = _sparks.emission;
                emission.rateOverTime = 0f;
                var shape = _sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.1f;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.05f;
                renderer.lengthScale = 2f;
                var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
                mat.SetColor("_BaseColor", IronFaunaPalette.UiNeutral);
                renderer.sharedMaterial = mat;
            }
            var emit = new ParticleSystem.EmitParams { position = at, startColor = color, applyShapeToPosition = true };
            _sparks.Emit(emit, count);
        }

        private void Banner(string text, float seconds)
        {
            if (_banner == null) return;
            _banner.text = text;
            _bannerT = seconds;
        }

        // ------------------------------------------------------------------ loop
        private void Update()
        {
            BoxingFeel.Tick(Time.unscaledDeltaTime);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Exit();
                return;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            float dt = Time.unscaledDeltaTime;
            float p = _player.health / _player.maxHealth, c = _cpu.health / _cpu.maxHealth;
            SetBar(_playerBar, p, true);
            SetBar(_cpuBar, c, false);
            // the white "damage taken" bar drains after a beat, so each hit reads as a chunk
            _playerLagV = Mathf.Max(p, Mathf.MoveTowards(_playerLagV, p, dt * 0.35f));
            _cpuLagV = Mathf.Max(c, Mathf.MoveTowards(_cpuLagV, c, dt * 0.35f));
            SetBar(_playerLag, _playerLagV, true);
            SetBar(_cpuLag, _cpuLagV, false);
            if (_score != null) _score.text = $"{_playerWins} - {_cpuWins}";

            if (_bannerT > 0f)
            {
                _bannerT -= dt;
                var col = _banner.color;
                col.a = Mathf.Clamp01(_bannerT * 2.5f);
                _banner.color = col;
            }

            if (_resetT > 0f)
            {
                _resetT -= dt;
                if (_resetT <= 0f)
                {
                    _player.ResetFighter(new Vector3(0f, DeckHeight, -2.8f), 0f);
                    _cpu.ResetFighter(new Vector3(0f, DeckHeight, 2.8f), 180f);
                    _playerLagV = _cpuLagV = 1f;
                    if (_cam != null) _cam.Snap();
                    Banner("FIGHT", 1.4f);
                }
            }
        }

        private void Exit()
        {
            BoxingFeel.Clear();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            BoxingFeel.Clear();
        }
    }
}
