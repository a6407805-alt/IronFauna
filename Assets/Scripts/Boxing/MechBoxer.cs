using System;
using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;

namespace IronFauna.Boxing
{
    public enum PunchType { None, Jab, Cross, Hook, Uppercut }

    /// <summary>
    /// A mech that boxes: procedural, physically-flavoured animation on a MechClamshellBuilder rig.
    ///  - Never still: a floaty bounce on the balls of the feet (heels up), weave, guard pumping in rhythm.
    ///  - Footwork: the body glides, feet re-plant during the airborne half of each bounce, the foot on the side of
    ///    travel leads, and the mech always squares up to its opponent.
    ///  - Punches (jab, cross, hook, uppercut): wind-up that loads the hips, a fast strike driven by torso rotation,
    ///    a lunge and weight transfer onto the front foot, rear-heel pivot, then a slower recovery. Two-bone arm IK.
    ///  - Impact: the attacker's fist recoils, the defender's head and torso snap on springs and slide back.
    ///    Blocking, slipping, whiffs, knockdown.
    /// Something else (player input or AI) writes <see cref="intent"/> every frame.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(MechClamshellBuilder))]
    public class MechBoxer : MonoBehaviour
    {
        public struct Intent
        {
            public Vector2 move;       // x = strafe right, y = toward the opponent
            public PunchType punch;    // pressed this frame (buffered)
            public bool guard;         // held: high guard
            public int slip;           // pressed this frame: -1 slip left, +1 slip right
        }

        [NonSerialized] public Intent intent;
        public MechBoxer opponent;
        public bool southpaw;

        [Header("Feel")]
        public float bounceHz = 1.7f;
        public float bounceHeight = 0.17f;
        public float crouch = 0.34f;
        public float bodyFrequency = 4.2f;     // body spring: lower = floatier
        public float bodyDamping = 0.5f;
        public float maxSpeed = 3.0f;
        public float acceleration = 13f;
        public float braking = 9f;
        public float turnRate = 260f;
        public float ringHalfSize = 6.2f;
        public float minDistance = 2.05f;

        public float health = 100f;
        public float maxHealth = 100f;
        public bool IsKO { get; private set; }
        public bool Punching => _punch != null;
        public PunchType CurrentPunch => _punch != null ? _punch.type : PunchType.None;
        /// <summary>0..1 during a wind-up (the AI reads it to react).</summary>
        public bool Telegraphing => _punch != null && _phase == Phase.Windup;
        public Vector3 Velocity => _vel;

        /// <summary>Physics brawler: the ragdoll's collisions deal the damage, so the distance check is skipped.</summary>
        [NonSerialized] public bool physicsDriven;
        /// <summary>Set by the ragdoll while its body is down or broken: no new punches, no footwork.</summary>
        [NonSerialized] public bool suppressed;
        /// <summary>Increments on every punch so a physical strike lands once.</summary>
        public int PunchSerial { get; private set; }
        /// <summary>The fist is travelling at (or resting on) the target.</summary>
        public bool StrikeActive => _punch != null && (_phase == Phase.Strike || _phase == Phase.Hold);
        public bool PunchLeadHand => _punch != null && _punch.lead;
        public float PunchPower => _punch != null ? Power(_punch) : 0f;
        public float PunchDamage => _punch != null ? _punch.damage : 0f;
        /// <summary>Ragdoll got hit: interrupt a wind-up and stagger the footwork.</summary>
        public void Interrupt(float stagger)
        {
            _stagger = Mathf.Max(_stagger, stagger);
            if (_punch != null && _phase == Phase.Windup) _punch = null;
        }

        /// <summary>attacker, contact point, power 0..1, blocked, landed (false = whiff)</summary>
        public event Action<MechBoxer, Vector3, float, bool, bool> PunchResolved;
        public event Action<MechBoxer> KnockedOut;
        public event Action<MechBoxer, float> PunchThrown;

        // ------------------------------------------------------------------ punch catalogue
        private class PunchSpec
        {
            public PunchType type;
            public bool lead;                 // lead hand (left in orthodox)
            public float windup, strike, hold, recover;
            public float twist;               // torso yaw at contact, degrees (+ = lead shoulder forward)
            public float load;                // torso yaw during the wind-up (opposite way)
            public float lean, roll, dip, lunge;
            public float damage, knock, stop, shake;
            public Vector3 pull;              // wind-up pull of the fist (body space, mirrored by hand)
            public Vector3 arc;               // control-point offset of the strike path (0 = straight)
            public Vector3 elbow;             // elbow direction hint at contact (x mirrored by hand)
            public float aimDrop;             // aim this far below the head point (uppercut finds the chin)
        }

        private static readonly Dictionary<PunchType, PunchSpec> Specs = new Dictionary<PunchType, PunchSpec>
        {
            [PunchType.Jab] = new PunchSpec
            {
                type = PunchType.Jab, lead = true, windup = 0.07f, strike = 0.10f, hold = 0.05f, recover = 0.20f,
                twist = 34f, load = 14f, lean = 6f, roll = -2f, dip = 0.04f, lunge = 0.28f,
                damage = 4f, knock = 0.9f, stop = 0.055f, shake = 0.22f,
                pull = new Vector3(0.04f, -0.06f, -0.12f), arc = Vector3.zero, elbow = new Vector3(0.6f, -1f, -0.1f),
            },
            [PunchType.Cross] = new PunchSpec
            {
                type = PunchType.Cross, lead = false, windup = 0.12f, strike = 0.12f, hold = 0.07f, recover = 0.30f,
                twist = -30f, load = 32f, lean = 11f, roll = 3f, dip = 0.09f, lunge = 0.5f,
                damage = 9f, knock = 2.0f, stop = 0.09f, shake = 0.45f,
                pull = new Vector3(0.08f, -0.1f, -0.22f), arc = Vector3.zero, elbow = new Vector3(0.7f, -1f, -0.1f),
            },
            [PunchType.Hook] = new PunchSpec
            {
                type = PunchType.Hook, lead = true, windup = 0.14f, strike = 0.14f, hold = 0.07f, recover = 0.34f,
                twist = -20f, load = 40f, lean = 6f, roll = 8f, dip = 0.16f, lunge = 0.25f,
                damage = 12f, knock = 2.6f, stop = 0.12f, shake = 0.6f,
                pull = new Vector3(0.28f, -0.04f, -0.1f), arc = new Vector3(1.25f, 0.12f, 0.15f), elbow = new Vector3(1f, 0.35f, -0.35f),
            },
            [PunchType.Uppercut] = new PunchSpec
            {
                type = PunchType.Uppercut, lead = false, windup = 0.16f, strike = 0.13f, hold = 0.08f, recover = 0.36f,
                twist = -18f, load = 24f, lean = -4f, roll = -6f, dip = 0.42f, lunge = 0.3f,
                damage = 14f, knock = 2.2f, stop = 0.13f, shake = 0.7f,
                pull = new Vector3(0.06f, -0.55f, -0.05f), arc = new Vector3(0.1f, -1.1f, 0.2f), elbow = new Vector3(0.35f, -1f, -0.5f),
                aimDrop = 0.35f,
            },
        };

        private enum Phase { Windup, Strike, Hold, Recover }

        // ------------------------------------------------------------------ state
        private MechClamshellBuilder _b;
        private int _builtVersion = -1;

        private class Foot
        {
            public MechClamshellBuilder.LegRig rig;
            public bool lead;
            public Vector3 stance;          // local offset from the root
            public float yaw;               // toe-out (deg)
            public Vector3 pos;             // planted world position (sole centre)
            public float lift;              // current lift height
            public float heel;              // heel raise (deg)
            public float pivot;             // extra yaw (rear-heel pivot on a cross)
            public Vector3 restThighDir, restCalfDir, restHinge;
        }

        private class Arm
        {
            public MechClamshellBuilder.ArmRig rig;
            public bool lead;
            public Vector3 restUpperDir, restForeDir, restHinge;
            public Vector3 wrist;           // current IK target (world)
            public Vector3 hint;            // elbow hint (arm-root space)
        }

        private readonly List<Foot> _feet = new List<Foot>();
        private readonly List<Arm> _arms = new List<Arm>();
        private Vector3 _bodyRestPos;
        private Quaternion _bodyRestRot = Quaternion.identity;
        private Quaternion _headRestRot = Quaternion.identity;
        private float _neckB, _chestB;      // neck / shoulder height in body-pivot space

        private Vector3 _vel;
        private float _hop;                 // bounce phase 0..1
        private float _bounceWeight = 1f;
        private float _bodyY, _bodyVy;
        private Vector3 _shift, _shiftVel;  // weight-transfer offset of the hips (local x/z)
        private float _yaw, _yawVel, _pitch, _pitchVel, _roll, _rollVel;
        private float _t;

        private PunchSpec _punch;
        private Phase _phase;
        private float _phaseT;
        private Vector3 _aim;               // fist target (world)
        private Vector3 _strikeFrom;        // wrist position when the strike began
        private bool _connected, _blockedHit;
        private PunchType _buffer;
        private float _bufferT;

        private float _slipT, _slipDir;
        private float _guardBlend;

        // Hit reaction springs (defender).
        private Vector3 _headSnap, _headSnapVel;      // euler degrees
        private Vector3 _jolt, _joltVel;              // body offset
        private Vector3 _armPush, _armPushVel;        // blocked: guard driven back
        private float _stagger;
        private float _koT;

        public void ResetFighter(Vector3 position, float yaw)
        {
            health = maxHealth;
            IsKO = false;
            _koT = 0f;
            _punch = null;
            _vel = Vector3.zero;
            _stagger = 0f;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _builtVersion = -1;
        }

        // ------------------------------------------------------------------ rig
        private void Init()
        {
            _b = GetComponent<MechClamshellBuilder>();
            if (_b == null || _b.visualRoot == null || _b.bodyRoot == null) return;
            _builtVersion = _b.BuildVersion;
            _feet.Clear();
            _arms.Clear();

            _bodyRestPos = _b.bodyRoot.localPosition;
            _bodyRestRot = _b.bodyRoot.localRotation;
            _headRestRot = _b.headRoot != null ? _b.headRoot.localRotation : Quaternion.identity;
            float bodyY = _b.HipHeight + 0.82f;
            _neckB = _b.NeckHeight - bodyY;
            _chestB = _b.ChestHeight - bodyY;

            float leadSide = southpaw ? 1f : -1f;
            foreach (var rig in _b.legRigs)
            {
                if (rig.root == null || rig.foot == null) continue;
                bool left = rig.root.name.EndsWith("_L");
                bool lead = (left ? -1f : 1f) == leadSide;
                float x = transform.InverseTransformPoint(rig.foot.position).x;
                var f = new Foot
                {
                    rig = rig, lead = lead,
                    stance = new Vector3(x * 0.92f + (lead ? 0.06f : -0.06f) * -leadSide, 0f, lead ? 0.62f : -0.58f),
                    yaw = (lead ? 12f : 38f) * -leadSide,
                };
                f.pos = transform.TransformPoint(f.stance);
                Vector3 dirHA = (rig.ankle - rig.hip).normalized;
                Vector3 bend = Vector3.ProjectOnPlane(rig.knee - rig.hip, dirHA).normalized;
                f.restHinge = Vector3.Cross(bend, dirHA).normalized;
                f.restThighDir = (rig.knee - rig.hip).normalized;
                f.restCalfDir = (rig.ankle - rig.knee).normalized;
                _feet.Add(f);
            }
            foreach (var rig in _b.armRigs)
            {
                if (rig.root == null) continue;
                var a = new Arm { rig = rig, lead = rig.side == (southpaw ? 1 : -1) };
                Vector3 d = (rig.wrist - rig.shoulder).normalized;
                Vector3 bend = Vector3.ProjectOnPlane(rig.elbow - rig.shoulder, d).normalized;
                a.restHinge = Vector3.Cross(bend, d).normalized;
                a.restUpperDir = (rig.elbow - rig.shoulder).normalized;
                a.restForeDir = (rig.wrist - rig.elbow).normalized;
                a.wrist = rig.fore.TransformPoint(rig.wrist - rig.elbow);
                a.hint = new Vector3(rig.side * 0.5f, -1f, -0.4f);
                _arms.Add(a);
            }
            _bodyY = -crouch;
            _bodyVy = 0f;
        }

        // ------------------------------------------------------------------ public actions
        /// <summary>Applies a landed (or blocked) punch from <paramref name="from"/>.</summary>
        public void TakeHit(MechBoxer from, Vector3 point, Vector3 dir, float damage, float knock, bool blocked, bool head)
        {
            if (IsKO) return;
            Vector3 local = transform.InverseTransformDirection(dir);
            if (blocked)
            {
                health -= damage * 0.15f;
                _armPushVel += new Vector3(-local.x, 0.2f, -1f) * (1.6f + knock * 0.6f);
                _joltVel += new Vector3(local.x * 0.4f, 0f, -0.6f) * knock;
                _vel += dir * knock * 0.55f;
            }
            else
            {
                health -= damage;
                float s = 30f + knock * 34f;
                // head whips away from the fist: yaw from sideways blows, pitch from straight and rising ones
                float back = Mathf.Max(0.2f, -local.z);   // dir points out of the defender's back for a straight blow
                _headSnapVel += new Vector3(-back * s * (head ? 1f : 0.3f) - Mathf.Max(0f, local.y) * s * 1.6f,
                                            local.x * s * 1.4f, -local.x * s * 0.8f) * 4f;
                _joltVel += new Vector3(local.x * 1.2f, -0.4f, -1.4f) * knock;
                _vel += dir * knock * 1.1f;
                _stagger = Mathf.Max(_stagger, 0.12f + knock * 0.09f);
                if (_punch != null && _phase == Phase.Windup) _punch = null;   // a clean hit interrupts the wind-up
            }
            if (health <= 0f && !IsKO)
            {
                health = 0f;
                IsKO = true;
                _punch = null;
                _vel += dir * 2.5f;
                KnockedOut?.Invoke(this);
            }
        }

        public bool Blocking => _guardBlend > 0.5f && _punch == null && !IsKO;

        // ------------------------------------------------------------------ frame
        private void LateUpdate()
        {
            if (_b == null) _b = GetComponent<MechClamshellBuilder>();
            if (_b == null || _b.visualRoot == null) return;
            if (_builtVersion != _b.BuildVersion || _feet.Count == 0) Init();
            if (_feet.Count == 0) return;

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) dt = 1e-5f;     // hit-stop: the pose holds but stays solved
            _t += dt;

            TickPunchInput();
            Move(dt);
            TickPunch(dt);
            Animate(dt);
            intent.punch = PunchType.None;
            intent.slip = 0;
        }

        // ------------------------------------------------------------------ locomotion
        private void Move(float dt)
        {
            Vector3 toOpp = opponent != null ? opponent.transform.position - transform.position : transform.forward;
            toOpp.y = 0f;
            float dist = toOpp.magnitude;
            Vector3 fwd = dist > 0.01f ? toOpp / dist : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            Vector2 m = IsKO || suppressed ? Vector2.zero : Vector2.ClampMagnitude(intent.move, 1f);
            float slow = (_punch != null ? 0.35f : 1f) * (_stagger > 0f ? 0.3f : 1f) * (_guardBlend > 0.5f ? 0.7f : 1f);
            Vector3 want = (right * m.x * 0.92f + fwd * m.y * (m.y < 0f ? 0.85f : 1f)) * maxSpeed * slow;
            float rate = want.sqrMagnitude > _vel.sqrMagnitude ? acceleration : braking;
            if (IsKO) rate = 3f;
            _vel = Vector3.MoveTowards(_vel, want, rate * dt);

            Vector3 p = transform.position + _vel * dt;
            if (opponent != null)
            {
                Vector3 d = p - opponent.transform.position;
                d.y = 0f;
                if (d.magnitude < minDistance)
                {
                    Vector3 push = d.sqrMagnitude > 1e-4f ? d.normalized : -fwd;
                    p = opponent.transform.position + push * minDistance;
                    _vel -= Vector3.Project(_vel, -push) * (Vector3.Dot(_vel, -push) > 0f ? 1f : 0f);
                }
            }
            p.x = Mathf.Clamp(p.x, -ringHalfSize, ringHalfSize);
            p.z = Mathf.Clamp(p.z, -ringHalfSize, ringHalfSize);
            transform.position = p;

            if (!IsKO && dist > 0.01f)
            {
                float target = Quaternion.LookRotation(fwd).eulerAngles.y;
                float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, target, turnRate * dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            _stagger = Mathf.Max(0f, _stagger - dt);
        }

        // ------------------------------------------------------------------ punches
        private void TickPunchInput()
        {
            if (intent.punch != PunchType.None) { _buffer = intent.punch; _bufferT = 0.28f; }
            else _bufferT -= Time.deltaTime;
            if (_bufferT <= 0f) _buffer = PunchType.None;
            if (intent.slip != 0 && _punch == null && _slipT <= 0f && !IsKO) { _slipT = 0.42f; _slipDir = intent.slip; }

            bool canStart = !IsKO && !suppressed && _stagger <= 0f && _slipT < 0.15f &&
                            (_punch == null || (_phase == Phase.Recover && _phaseT > _punch.recover * 0.35f));
            if (_buffer != PunchType.None && canStart)
            {
                StartPunch(Specs[_buffer]);
                _buffer = PunchType.None;
            }
        }

        private Arm ArmFor(bool lead)
        {
            foreach (var a in _arms) if (a.lead == lead) return a;
            return _arms.Count > 0 ? _arms[0] : null;
        }

        private void StartPunch(PunchSpec spec)
        {
            var arm = ArmFor(spec.lead);
            if (arm == null) return;
            _punch = spec;
            _phase = Phase.Windup;
            _phaseT = 0f;
            _connected = _blockedHit = false;
            _aim = AimPoint(spec);
            PunchSerial++;
            PunchThrown?.Invoke(this, Power(spec));
        }

        private static float Power(PunchSpec s) => Mathf.InverseLerp(3f, 14f, s.damage);

        private Vector3 AimPoint(PunchSpec spec)
        {
            if (opponent == null) return transform.position + transform.forward * 3f + Vector3.up * (_b.NeckHeight + 0.2f);
            return opponent.HeadPoint() + Vector3.down * spec.aimDrop;
        }

        /// <summary>Where a punch to the head lands (moves when the boxer slips or ducks).</summary>
        public Vector3 HeadPoint()
        {
            if (_b != null && _b.headRoot != null) return _b.headRoot.position + Vector3.up * 0.28f;
            return transform.position + Vector3.up * 4f;
        }

        public Vector3 ChestPoint()
        {
            if (_b != null && _b.bodyRoot != null) return _b.bodyRoot.TransformPoint(new Vector3(0f, _chestB - 0.2f, 0.25f));
            return transform.position + Vector3.up * 3f;
        }

        private void TickPunch(float dt)
        {
            if (_punch == null) return;
            _phaseT += dt;
            var s = _punch;
            switch (_phase)
            {
                case Phase.Windup:
                    // keep tracking the target while loading up; commit when the strike starts
                    _aim = Vector3.Lerp(_aim, AimPoint(s), 1f - Mathf.Exp(-10f * dt));
                    if (_phaseT >= s.windup)
                    {
                        _phase = Phase.Strike; _phaseT = 0f;
                        var arm = ArmFor(s.lead);
                        _strikeFrom = arm != null ? arm.wrist : _aim;
                    }
                    break;
                case Phase.Strike:
                    if (_phaseT >= s.strike) { Resolve(s); _phase = Phase.Hold; _phaseT = 0f; }
                    break;
                case Phase.Hold:
                    if (_phaseT >= s.hold * (_connected ? 0.7f : 1.6f)) { _phase = Phase.Recover; _phaseT = 0f; }
                    break;
                case Phase.Recover:
                    if (_phaseT >= s.recover * (_connected ? 1f : 1.35f)) _punch = null;   // whiffs cost more
                    break;
            }
        }

        private void Resolve(PunchSpec s)
        {
            if (physicsDriven) { _connected = true; return; }
            var arm = ArmFor(s.lead);
            Vector3 fist = arm != null ? arm.rig.fore.TransformPoint(arm.rig.tip - arm.rig.elbow) : _aim;
            if (opponent == null || opponent.IsKO)
            {
                PunchResolved?.Invoke(this, fist, Power(s), false, false);
                return;
            }
            float dHead = Vector3.Distance(fist, opponent.HeadPoint() + Vector3.down * s.aimDrop * 0.5f);
            float dBody = Vector3.Distance(fist, opponent.ChestPoint());
            bool head = dHead < 0.75f;
            bool body = !head && dBody < 0.95f;
            if (!head && !body)
            {
                PunchResolved?.Invoke(this, fist, Power(s), false, false);
                return;
            }
            _connected = true;
            // a guard stops straight punches and hooks from the front; uppercuts split a high guard
            bool blocked = opponent.Blocking && (s.type != PunchType.Uppercut || UnityEngine.Random.value < 0.4f);
            _blockedHit = blocked;
            Vector3 dir = (opponent.transform.position - transform.position);
            dir.y = 0f;
            dir = dir.normalized;
            if (s.type == PunchType.Hook) dir = (dir + transform.right * (s.lead == !southpaw ? 1f : -1f) * 0.9f).normalized;
            if (s.type == PunchType.Uppercut) dir = (dir + Vector3.up * 0.8f).normalized;
            float dmg = s.damage * (head ? 1f : 0.7f);
            opponent.TakeHit(this, fist, dir, dmg, s.knock, blocked, head);
            // the fist bounces back off the target
            if (arm != null) arm.wrist -= (fist - transform.position).normalized * (blocked ? 0.22f : 0.12f);
            _vel -= dir * (blocked ? 0.6f : 0.25f);
            PunchResolved?.Invoke(this, fist, Power(s), blocked, true);
        }

        // ------------------------------------------------------------------ animation
        private static float EaseOut3(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
        private static float EaseIn2(float x) { x = Mathf.Clamp01(x); return x * x; }
        private static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        private static void Spring(ref float x, ref float v, float target, float freq, float damp, float dt)
        {
            float w = 2f * Mathf.PI * freq;
            v += (-w * w * (x - target) - 2f * damp * w * v) * dt;
            x += v * dt;
        }

        private static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float freq, float damp, float dt)
        {
            float w = 2f * Mathf.PI * freq;
            v += (-w * w * (x - target) - 2f * damp * w * v) * dt;
            x += v * dt;
        }

        /// <summary>Punch progress → body drive (yaw, lean, roll, dip, lunge) and the fist path.</summary>
        private void PunchDrive(out float yaw, out float lean, out float roll, out float dip, out float lunge, out float plant)
        {
            yaw = lean = roll = dip = lunge = 0f;
            plant = 0f;
            if (_punch == null) return;
            var s = _punch;
            float sgn = southpaw ? -1f : 1f;
            switch (_phase)
            {
                case Phase.Windup:
                {
                    float k = EaseInOut(_phaseT / s.windup);
                    yaw = s.load * k;
                    dip = s.dip * k;
                    roll = -s.roll * 0.5f * k;
                    lean = -s.lean * 0.2f * k;
                    plant = k;
                    break;
                }
                case Phase.Strike:
                {
                    float k = EaseIn2(_phaseT / s.strike);        // accelerates into the target
                    yaw = Mathf.Lerp(s.load, s.twist, k);
                    dip = Mathf.Lerp(s.dip, s.type == PunchType.Uppercut ? -0.1f : s.dip * 0.4f, k);
                    roll = Mathf.Lerp(-s.roll * 0.5f, s.roll, k);
                    lean = Mathf.Lerp(-s.lean * 0.2f, s.lean, k);
                    lunge = s.lunge * k;
                    plant = 1f;
                    break;
                }
                case Phase.Hold:
                    yaw = s.twist; dip = s.type == PunchType.Uppercut ? -0.1f : s.dip * 0.4f; roll = s.roll; lean = s.lean; lunge = s.lunge; plant = 1f;
                    break;
                case Phase.Recover:
                {
                    float k = EaseInOut(_phaseT / (s.recover * (_connected ? 1f : 1.35f)));
                    yaw = s.twist * (1f - k);
                    dip = (s.type == PunchType.Uppercut ? -0.1f : s.dip * 0.4f) * (1f - k);
                    roll = s.roll * (1f - k);
                    lean = s.lean * (1f - k) + (_connected ? 0f : 4f * Mathf.Sin(k * Mathf.PI));   // whiff: overbalance
                    lunge = s.lunge * (1f - k);
                    plant = 1f - k;
                    break;
                }
            }
            // lead-hand punches turn the lead shoulder in, rear-hand ones the rear: twist sign is already per spec;
            // southpaw mirrors everything sideways.
            yaw *= sgn;
            roll *= sgn;
        }

        private void Animate(float dt)
        {
            float sgn = southpaw ? -1f : 1f;
            Vector3 localVel = transform.InverseTransformDirection(_vel);
            float speed01 = Mathf.Clamp01(_vel.magnitude / Mathf.Max(0.1f, maxSpeed));

            PunchDrive(out float pYaw, out float pLean, out float pRoll, out float pDip, out float pLunge, out float plant);

            // ---------------- bounce: never stops (slower and heavier while punching, gone when knocked out)
            float wantBounce = IsKO ? 0f : (_punch != null ? 0.15f : (_guardBlend > 0.5f ? 0.6f : 1f));
            _bounceWeight = Mathf.MoveTowards(_bounceWeight, wantBounce, dt * 4f);
            _hop += dt * bounceHz * Mathf.Lerp(0.85f, 1.15f, speed01);
            if (_hop >= 1f) _hop -= 1f;
            const float air = 0.56f;
            float hopShape = _hop < air ? Mathf.Sin(Mathf.PI * _hop / air) : -0.45f * Mathf.Sin(Mathf.PI * (_hop - air) / (1f - air));

            _slipT = Mathf.Max(0f, _slipT - dt);
            float slip = _slipT > 0f ? Mathf.Sin(Mathf.PI * (1f - _slipT / 0.42f)) : 0f;   // in and back out
            _guardBlend = Mathf.MoveTowards(_guardBlend, intent.guard && _punch == null && !IsKO ? 1f : 0f, dt * 7f);

            float koDrop = 0f, koPitch = 0f;
            if (IsKO)
            {
                _koT += dt;
                float k = EaseOut3(_koT / 0.9f);
                koDrop = 1.25f * k;
                koPitch = -24f * k;
            }

            float bodyTarget = -crouch + hopShape * bounceHeight * _bounceWeight - pDip - slip * 0.28f - _guardBlend * 0.06f - koDrop;
            Spring(ref _bodyY, ref _bodyVy, bodyTarget, bodyFrequency, bodyDamping, dt);

            // ---------------- weight shift: hips travel over the front foot on punches, lag behind travel
            float weave = Mathf.Sin(_t * 2f * Mathf.PI * 0.43f) * 0.1f * (_punch == null ? 1f : 0.3f) * (IsKO ? 0f : 1f);
            Vector3 shiftTarget = new Vector3(weave + slip * _slipDir * 0.42f - localVel.x * 0.05f, 0f,
                                              pLunge - localVel.z * 0.06f);
            Spring(ref _shift, ref _shiftVel, shiftTarget, 3.2f, 0.6f, dt);
            Spring(ref _jolt, ref _joltVel, Vector3.zero, 2.4f, 0.42f, dt);

            Vector3 root = new Vector3(_shift.x + _jolt.x, _bodyY + _jolt.y, _shift.z + _jolt.z);
            _b.visualRoot.localPosition = root;
            _b.visualRoot.localRotation = Quaternion.identity;

            // ---------------- torso: stance turn + punch drive + travel lean + weave/slip roll + hit jolt
            float stanceYaw = 16f * sgn;
            float travelLean = Mathf.Clamp(localVel.z * 2.2f, -5f, 6f);
            float yawTarget = stanceYaw + pYaw + Mathf.Sin(_t * 2f * Mathf.PI * 0.43f + 1f) * 3f;
            float pitchTarget = 7f + pLean + travelLean + _guardBlend * 6f + slip * 10f + koPitch;
            float rollTarget = pRoll - localVel.x * 1.2f - slip * _slipDir * 16f - weave * 25f;
            float torsoHz = _punch != null && (_phase == Phase.Strike || _phase == Phase.Hold) ? 9f : 4.5f;
            Spring(ref _yaw, ref _yawVel, yawTarget, torsoHz, 0.62f, dt);
            Spring(ref _pitch, ref _pitchVel, pitchTarget, 4f, 0.55f, dt);
            Spring(ref _roll, ref _rollVel, rollTarget, 3.5f, 0.55f, dt);
            _b.bodyRoot.localPosition = _bodyRestPos;
            _b.bodyRoot.localRotation = Quaternion.Euler(_pitch + _jolt.z * 12f, _yaw, _roll - _jolt.x * 14f) * _bodyRestRot;

            // ---------------- head: keeps eyes on the opponent, snaps on hits
            Spring(ref _headSnap, ref _headSnapVel, Vector3.zero, 2.6f, 0.38f, dt);
            if (_b.headRoot != null)
            {
                float lookYaw = -_yaw * 0.7f;
                float lookPitch = -(_pitch - 7f) * 0.6f - 6f;
                _b.headRoot.localRotation = Quaternion.Euler(lookPitch + _headSnap.x, lookYaw + _headSnap.y, -_roll * 0.4f + _headSnap.z) * _headRestRot;
            }

            // ---------------- feet
            FeetStep(dt, plant, localVel, sgn);
            foreach (var f in _feet) SolveLeg(f);

            // ---------------- arms
            Spring(ref _armPush, ref _armPushVel, Vector3.zero, 3.4f, 0.5f, dt);
            foreach (var a in _arms) ArmPose(a, dt, hopShape, sgn);
        }

        private void FeetStep(float dt, float plant, Vector3 localVel, float sgn)
        {
            foreach (var f in _feet)
            {
                // Feet leave the deck in the airborne half of the bounce; the foot on the side of travel goes first.
                float travelSide = Vector3.Dot(new Vector3(localVel.x, 0f, localVel.z), new Vector3(f.stance.x, 0f, f.stance.z).normalized);
                float early = Mathf.Clamp(travelSide * 0.05f, -0.08f, 0.08f);
                float h = _hop + early;
                if (h < 0f) h += 1f;
                bool airborne = h > 0.03f && h < 0.53f && _bounceWeight > 0.3f && !IsKO;

                Vector3 target = transform.TransformPoint(f.stance + new Vector3(localVel.x, 0f, localVel.z) * 0.16f);
                // on a punch the front foot steps in with the lunge and the rear foot drives (pivots on the ball)
                if (_punch != null && f.lead == _punch.lead) target += transform.forward * _shift.z * 0.4f;
                target.y = transform.position.y;

                float far = Vector3.Distance(f.pos, target);
                if (airborne || far > 0.85f)
                {
                    f.pos = Vector3.Lerp(f.pos, target, 1f - Mathf.Exp(-(airborne ? 16f : 9f) * dt));
                }
                float liftWant = airborne ? Mathf.Sin(Mathf.PI * (h - 0.03f) / 0.5f) * 0.11f * _bounceWeight : 0f;
                if (far > 0.85f && !airborne) liftWant = 0.12f;
                f.lift = Mathf.Lerp(f.lift, liftWant, 1f - Mathf.Exp(-20f * dt));

                // on the balls of the feet: heels up, more on push-off and on the driving rear foot of a punch
                bool drive = _punch != null && f.lead != _punch.lead;
                float heelWant = IsKO ? 0f : 14f + (airborne ? 8f : 0f) + (drive ? 20f * plant : 0f);
                f.heel = Mathf.Lerp(f.heel, heelWant, 1f - Mathf.Exp(-14f * dt));
                // rear-hand punches turn the rear heel out (toes toward the target); a lead hook pivots the lead foot
                float pivotWant = 0f;
                if (_punch != null && !_punch.lead && !f.lead) pivotWant = -30f * plant * sgn;
                else if (_punch != null && _punch.type == PunchType.Hook && f.lead) pivotWant = 25f * plant * sgn;
                f.pivot = Mathf.Lerp(f.pivot, pivotWant, 1f - Mathf.Exp(-14f * dt));
            }
        }

        private void SolveLeg(Foot f)
        {
            var rig = f.rig;
            const float toe = 0.34f;          // ball of the foot, ahead of the ankle
            Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y + f.yaw + f.pivot, 0f);
            Quaternion rot = yaw * Quaternion.Euler(-f.heel, 0f, 0f);
            // the foot pivots on its ball: keep the ball where the stance puts it, lift the heel
            Vector3 ball = f.pos + yaw * new Vector3(0f, 0f, toe) + Vector3.up * f.lift;
            Vector3 footPos = ball - rot * new Vector3(0f, 0f, toe);
            if (rig.foot != null) rig.foot.SetPositionAndRotation(footPos, rot);

            Vector3 ankleWorld = footPos + rot * Vector3.up * MechClamshellBuilder.AnkleHeight;
            Vector3 a = rig.root.InverseTransformPoint(ankleWorld);
            Vector3 h = rig.hip;
            float l1 = rig.thighLength, l2 = rig.calfLength;
            Vector3 d = a - h;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.05f, l1 + l2 - 0.01f);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float height = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            // knees track over the toes (a little outward)
            Vector3 toeDir = rig.root.InverseTransformDirection(yaw * Vector3.forward);
            Vector3 hint = rig.kneeForward ? toeDir : -toeDir;
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.forward;
            Vector3 knee = h + dir * along + bend * height;
            Vector3 ankle = h + dir * dist;
            Vector3 hinge = Vector3.Cross(bend, dir).normalized;
            Quaternion r1 = Quaternion.LookRotation((knee - h).normalized, hinge) * Quaternion.Inverse(Quaternion.LookRotation(f.restThighDir, f.restHinge));
            rig.thigh.localRotation = r1;
            Quaternion inv = Quaternion.Inverse(r1);
            Quaternion r2 = Quaternion.LookRotation(inv * (ankle - knee).normalized, inv * hinge) * Quaternion.Inverse(Quaternion.LookRotation(f.restCalfDir, f.restHinge));
            rig.calf.localRotation = r2;
        }

        /// <summary>Guard position of a wrist in body-pivot space.</summary>
        private Vector3 GuardPoint(Arm a, float hopShape)
        {
            float x = a.rig.side;
            Vector3 g = a.lead
                ? new Vector3(x * 0.42f, _neckB - 0.12f, 0.95f)
                : new Vector3(x * 0.46f, _neckB - 0.24f, 0.72f);
            // high guard: fists to the temples, forearms in front of the face
            Vector3 high = new Vector3(x * 0.34f, _neckB + 0.1f, 0.62f);
            g = Vector3.Lerp(g, high, _guardBlend);
            // hands pump against the bounce and circle a little; they never sit dead still
            float circle = _t * 2f * Mathf.PI * 0.9f + (a.lead ? 0f : 2.1f);
            g += new Vector3(Mathf.Cos(circle) * 0.04f, -hopShape * 0.06f + Mathf.Sin(circle) * 0.035f, 0f) * (1f - _guardBlend * 0.6f);
            if (IsKO) g = new Vector3(x * 0.75f, _chestB - 1.1f, 0.2f);
            return g;
        }

        private void ArmPose(Arm a, float dt, float hopShape, float sgn)
        {
            Transform body = _b.bodyRoot;
            Vector3 guard = body.TransformPoint(GuardPoint(a, hopShape) + new Vector3(_armPush.x, _armPush.y, -Mathf.Abs(_armPush.z)));
            Vector3 hintTarget = new Vector3(a.rig.side * 0.55f, -1f, -0.45f);
            Vector3 want = guard;
            float follow = 18f;

            bool punching = _punch != null && _punch.lead == a.lead;
            if (punching)
            {
                var s = _punch;
                float x = a.rig.side;
                Vector3 pull = body.TransformPoint(GuardPoint(a, hopShape) + new Vector3(s.pull.x * x, s.pull.y, s.pull.z));
                Vector3 elbowHint = new Vector3(s.elbow.x * x, s.elbow.y, s.elbow.z);
                // the wrist stops a hand's length short of the target
                Vector3 shoulderW = a.rig.root.TransformPoint(a.rig.shoulder);
                float reach = a.rig.upperLength + a.rig.foreLength - 0.03f;
                float hand = Vector3.Distance(a.rig.wrist, a.rig.tip);
                Vector3 toAim = _aim - shoulderW;
                Vector3 contact = shoulderW + Vector3.ClampMagnitude(toAim - toAim.normalized * hand * 0.9f, reach);
                switch (_phase)
                {
                    case Phase.Windup:
                        want = Vector3.Lerp(guard, pull, EaseInOut(_phaseT / s.windup));
                        hintTarget = Vector3.Lerp(hintTarget, elbowHint, EaseInOut(_phaseT / s.windup));
                        follow = 30f;
                        break;
                    case Phase.Strike:
                    {
                        float k = EaseIn2(_phaseT / s.strike);
                        Vector3 ctrl = Vector3.Lerp(_strikeFrom, contact, 0.5f) + body.TransformDirection(new Vector3(s.arc.x * x, s.arc.y, s.arc.z));
                        // quadratic bezier: straight for jab/cross, around the side for hooks, up from below for uppercuts
                        want = (1 - k) * (1 - k) * _strikeFrom + 2 * (1 - k) * k * ctrl + k * k * contact;
                        hintTarget = elbowHint;
                        follow = 80f;
                        break;
                    }
                    case Phase.Hold:
                        want = contact;
                        hintTarget = elbowHint;
                        follow = _connected ? 6f : 40f;   // after a hit the fist rides the recoil a moment
                        break;
                    case Phase.Recover:
                    {
                        float k = EaseInOut(_phaseT / (s.recover * (_connected ? 1f : 1.35f)));
                        want = Vector3.Lerp(contact, guard, k);
                        hintTarget = Vector3.Lerp(elbowHint, hintTarget, k);
                        follow = 22f;
                        break;
                    }
                }
            }
            else if (_punch != null)
            {
                // the other hand stays home, tucked tight to the chin
                want = body.TransformPoint(GuardPoint(a, hopShape) + new Vector3(-a.rig.side * 0.06f, 0.06f, -0.12f));
            }

            a.wrist = Vector3.Lerp(a.wrist, want, 1f - Mathf.Exp(-follow * dt));
            a.hint = Vector3.Lerp(a.hint, hintTarget, 1f - Mathf.Exp(-14f * dt));
            SolveArm(a);
        }

        private void SolveArm(Arm a)
        {
            var rig = a.rig;
            Vector3 w = rig.root.InverseTransformPoint(a.wrist);
            Vector3 s = rig.shoulder;
            float l1 = rig.upperLength, l2 = rig.foreLength;
            Vector3 d = w - s;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.08f, l1 + l2 - 0.005f);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float height = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(a.hint, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.down;
            Vector3 elbow = s + dir * along + bend * height;
            Vector3 wrist = s + dir * dist;
            Vector3 hinge = Vector3.Cross(bend, dir).normalized;
            Quaternion r1 = Quaternion.LookRotation((elbow - s).normalized, hinge) * Quaternion.Inverse(Quaternion.LookRotation(a.restUpperDir, a.restHinge));
            rig.upper.localRotation = r1;
            Quaternion inv = Quaternion.Inverse(r1);
            Quaternion r2 = Quaternion.LookRotation(inv * (wrist - elbow).normalized, inv * hinge) * Quaternion.Inverse(Quaternion.LookRotation(a.restForeDir, a.restHinge));
            rig.fore.localRotation = r2;
        }
    }
}
