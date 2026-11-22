using System;
using System.Collections.Generic;
using UnityEngine;
using IronFauna.Boxing;
using IronFauna.Clamshell;
using IronFauna.Core;

namespace IronFauna.Brawl
{
    public enum LimbId { None, ArmL, ArmR, LegL, LegR, Head }

    /// <summary>
    /// Active ragdoll for a procedurally built mech (Gang Beasts style).
    ///
    /// The visible mech is split into physics segments - pelvis, torso, head, upper arms, forearms, thighs, calves,
    /// feet - each a Rigidbody with a box collider, joined by ConfigurableJoints. A hidden "ghost" copy of the same
    /// mech runs the procedural fighter animation (MechBoxer). Every physics step the joint motors chase the ghost's
    /// local rotations and a balance controller pushes the pelvis toward the ghost's hips. Muscle strength and
    /// balance can drop (stagger, knockdown, KO) and come back (getting up).
    ///
    /// Damage is physical: a striking limb that hits another mech deals damage from its speed and mass.
    /// Each limb has its own health; armor plates crack off as it drops and at zero the joint is torn out and the
    /// limb flies free. Input only reaches the ghost's MechBoxer.intent, so a network layer can feed it later.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class MechRagdoll : MonoBehaviour
    {
        public MechClamshellBuilder body;    // the visible mech (becomes physical)
        public MechBoxer ghost;               // the hidden animated mech
        public float health = 160f;
        public float maxHealth = 160f;
        public float poiseMax = 50f;
        [Tooltip("How far hits shove mechs around (technical pass: 1 = a solid rock, not a launch)")]
        public float knockbackScale = 1f;
        public float knockbackTakenScale = 1f;
        public float limbTearScale = 1f;
        [Tooltip("0 = Free-For-All, 1 = Allied Team (Players + Co-Op Bots), 2 = Enemy AI Team")]
        public int team;
        public string weightLabel = "MEDIUM";

        [Header("Strength")]
        public float muscle = 1f;             // joint motor multiplier (0 = limp)
        public float balance = 1f;            // pelvis controller multiplier (0 = no help standing)

        public bool IsKO { get; private set; }
        public bool IsDown => _downT > 0f || IsKO;
        public Rigidbody Pelvis => _pelvis != null ? _pelvis.rb : null;
        public Vector3 Position => _pelvis != null ? _pelvis.rb.position : transform.position;
        /// <summary>World position of the head (falls back to the pelvis once the head is gone).</summary>
        public Vector3 HeadPosition => _head != null && !_head.detached && _head.rb != null ? _head.rb.position : Position + Vector3.up * 2f;
        public float Poise01 => Mathf.Clamp01(_poise / poiseMax);
        public float GetLimbHealth01(LimbId id) => _lost.Contains(id) ? 0f : (_limbHealth.TryGetValue(id, out float v) ? Mathf.Clamp01(v / LimbMax(id)) : 1f);

        /// <summary>Full health of a limb after weight-class / tournament scaling.</summary>
        private float LimbMax(LimbId id) => _limbMax.TryGetValue(id, out float m) && m > 0f ? m : (id == LimbId.Head ? HeadHealth : LimbHealth);

        /// <summary>victim, attacker, point, damage, power 0..1, limb ripped</summary>
        public event Action<MechRagdoll, MechRagdoll, Vector3, float, float, bool> Hit;
        public event Action<MechRagdoll> KnockedOut;
        public event Action<MechRagdoll, Vector3> ArmorShed;
        public event Action<MechRagdoll, LimbId> LimbRipped;

        public class Segment
        {
            public string name;
            public LimbId limb;
            public bool striker;                  // forearm or foot/calf: deals damage
            public bool leadHand;
            public Transform phys;
            public Rigidbody rb;
            public Transform target;              // ghost counterpart
            public Segment parent;
            public ConfigurableJoint joint;
            public Vector3 connectedAnchorRest;
            public Quaternion startLocal = Quaternion.identity;
            public float spring, damper;
            public readonly List<Transform> armor = new List<Transform>();
            public bool detached;
            public Transform tip;                 // fist / toe point (grabbing, trails)
            public MechRagdoll heldBy;            // a torn-off limb someone is wielding
        }

        private readonly List<Segment> _segs = new List<Segment>();
        private Segment _pelvis, _torso, _head;
        private readonly Dictionary<LimbId, float> _limbHealth = new Dictionary<LimbId, float>();
        private readonly HashSet<LimbId> _lost = new HashSet<LimbId>();
        private readonly Dictionary<LimbId, float> _limbMax = new Dictionary<LimbId, float>();
        private float _totalMass;
        private float _poise;
        private float _downT;
        private float _recoverT;
        private float _lastHitT;
        private const float LimbHealth = 80f, HeadHealth = 90f;
        /// <summary>Strikes tear a limb off only below this fraction of hull (grapple tears are separate).</summary>
        private const float LimbRipHull = 0.5f;
        /// <summary>Global strike damage (owner: still too chaotic): every fist/foot/knee/head/shoulder hit.</summary>
        private const float StrikeDamageScale = 0.75f;
        private float _koElapsed;
        private bool _koCleared;
        private readonly Dictionary<MechRagdoll, int> _struckBy = new Dictionary<MechRagdoll, int>();

        // ------------------------------------------------------------------ build
        /// <summary>Turns <paramref name="mech"/> into physics segments driven by <paramref name="animated"/>.</summary>
        public void Build(MechClamshellBuilder mech, MechBoxer animated)
        {
            body = mech;
            ghost = animated;
            ghost.physicsDriven = true;
            var g = animated.GetComponent<MechClamshellBuilder>();

            // pose the visible mech like the ghost before it is split up
            Transform container = new GameObject("RagdollParts").transform;
            container.SetParent(transform, false);

            _pelvis = Add("Pelvis", mech.visualRoot, g.visualRoot, null, 30f, LimbId.None, false, false, 0f);
            _torso = Add("Torso", mech.bodyRoot, g.bodyRoot, _pelvis, 42f, LimbId.None, false, false, 9000f);
            _head = Add("Head", mech.headRoot, g.headRoot, _torso, 6f, LimbId.Head, false, false, 1600f);
            for (int i = 0; i < mech.armRigs.Count && i < g.armRigs.Count; i++)
            {
                var a = mech.armRigs[i];
                var ga = FindArm(g, a.side);
                if (ga == null) continue;
                LimbId limb = a.side < 0 ? LimbId.ArmL : LimbId.ArmR;
                bool lead = a.side == (animated.southpaw ? 1 : -1);
                var up = Add("UpperArm" + Side(a.side), a.upper, ga.upper, _torso, 9f, limb, false, lead, 6000f);
                var fo = Add("Forearm" + Side(a.side), a.fore, ga.fore, up, 8f, limb, true, lead, 5200f);
                AddTrail(fo, a.tip - a.elbow);
            }
            for (int i = 0; i < mech.legRigs.Count; i++)
            {
                var l = mech.legRigs[i];
                bool left = l.root.name.EndsWith("_L");
                var gl = FindLeg(g, left);
                if (gl == null) continue;
                LimbId limb = left ? LimbId.LegL : LimbId.LegR;
                bool leadLeg = left != animated.southpaw;
                var th = Add("Thigh" + (left ? "_L" : "_R"), l.thigh, gl.thigh, _pelvis, 14f, limb, true, leadLeg, 7000f);
                var ca = Add("Calf" + (left ? "_L" : "_R"), l.calf, gl.calf, th, 11f, limb, true, leadLeg, 6000f);
                if (l.foot != null && gl.foot != null)
                {
                    var ft = Add("Foot" + (left ? "_L" : "_R"), l.foot, gl.foot, ca, 7f, limb, true, leadLeg, 3000f);
                    AddTrail(ft, new Vector3(0f, 0.12f, 0.55f));
                }
            }

            // Flatten: every segment except the pelvis becomes a top-level body under the container (deepest first)
            for (int i = _segs.Count - 1; i >= 1; i--) _segs[i].phys.SetParent(container, true);

            foreach (var s in _segs) CollectArmor(s);
            foreach (var s in _segs) MakeBody(s);
            foreach (var s in _segs) if (s.parent != null) MakeJoint(s, mech);

            // a mech's own parts never collide with each other
            var cols = new List<Collider>();
            foreach (var s in _segs) cols.AddRange(s.phys.GetComponents<Collider>());
            for (int i = 0; i < cols.Count; i++)
                for (int j = i + 1; j < cols.Count; j++)
                    Physics.IgnoreCollision(cols[i], cols[j], true);

            foreach (LimbId id in Enum.GetValues(typeof(LimbId))) if (id != LimbId.None) _limbHealth[id] = id == LimbId.Head ? HeadHealth : LimbHealth;
            _poise = poiseMax;
            health = maxHealth;

            // hide the ghost (it still animates)
            foreach (var r in animated.GetComponentsInChildren<Renderer>()) r.enabled = false;
            ghost.SetLimbState(true, true, true, true);
            ghost.OffBalanceTriggered += (msg, kd) =>
            {
                if (kd > 0f && !IsKO) Knockdown(kd);
                if (!string.IsNullOrEmpty(msg)) GrappleEvent?.Invoke(this, msg);
            };
        }

        public void ApplyLoadoutStats(MechLoadout loadout, float bonusHpMult = 1f)
        {
            if (loadout == null) return;
            var m = loadout.ComputeCombatModifiers();
            weightLabel = m.weightLabel;
            maxHealth = Mathf.Round(160f * m.healthMult * bonusHpMult);
            health = maxHealth;
            poiseMax = Mathf.Round(50f * m.poiseMult * Mathf.Lerp(1f, 1.4f, bonusHpMult - 1f));
            _poise = poiseMax;
            knockbackScale = m.knockbackDealtMult;
            knockbackTakenScale = m.knockbackTakenMult;
            limbTearScale = m.limbTearMult;

            float limbScale = m.limbHealthMult * Mathf.Lerp(1f, 1.3f, bonusHpMult - 1f);
            foreach (LimbId id in Enum.GetValues(typeof(LimbId)))
            {
                if (id == LimbId.None) continue;
                _limbMax[id] = (id == LimbId.Head ? HeadHealth : LimbHealth) * limbScale;
                _limbHealth[id] = _limbMax[id];
            }

            _totalMass = 0f;
            foreach (var s in _segs)
            {
                if (s.rb != null)
                {
                    s.rb.mass *= m.massMult;
                    _totalMass += s.rb.mass;
                }
            }

            if (ghost != null)
            {
                ghost.maxSpeed = 3.0f * m.moveSpeedMult;
                ghost.sprintSpeed = 5.6f * m.sprintSpeedMult;
                ghost.strikeSpeedScale = m.strikeSpeedMult;
                ghost.damageScale = m.damageMult;
                ghost.hyperArmor = m.hyperArmor;
                // stamina by weight class: light frames recover fast, heavy frames carry a deep tank but refill slowly
                var tw = loadout.torsoWeight;
                ghost.staminaMax = tw == MechWeightClass.Heavy ? 120f : tw == MechWeightClass.Light ? 90f : 100f;
                ghost.staminaRegen = tw == MechWeightClass.Heavy ? 27f : tw == MechWeightClass.Light ? 42f : 34f;
                ghost.dodgeCost = loadout.legWeight == MechWeightClass.Heavy ? 27f : loadout.legWeight == MechWeightClass.Light ? 15f : 20f;
                ghost.dodgeSpeed = loadout.legWeight == MechWeightClass.Heavy ? 6.6f : loadout.legWeight == MechWeightClass.Light ? 9.2f : 8f;
                ghost.stamina = ghost.staminaMax;
            }
        }

        private static string Side(int side) => side < 0 ? "_L" : "_R";

        private static MechClamshellBuilder.ArmRig FindArm(MechClamshellBuilder g, int side)
        {
            foreach (var a in g.armRigs) if (a.side == side) return a;
            return null;
        }

        private static MechClamshellBuilder.LegRig FindLeg(MechClamshellBuilder g, bool left)
        {
            foreach (var l in g.legRigs) if (l.root.name.EndsWith("_L") == left) return l;
            return null;
        }

        private Segment Add(string name, Transform phys, Transform target, Segment parent, float mass, LimbId limb, bool striker, bool lead, float spring)
        {
            var s = new Segment { name = name, phys = phys, target = target, parent = parent, limb = limb, striker = striker, leadHand = lead, spring = spring, damper = spring * 0.045f };
            var rb = phys.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            // anti-explosion: overlapping bodies are pushed apart gently instead of at "infinite" speed,
            // and joints get more solver passes so a hard yank cannot stretch them apart
            rb.maxDepenetrationVelocity = 4f;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            s.rb = rb;
            _segs.Add(s);
            return s;
        }

        private void CollectArmor(Segment s)
        {
            foreach (var t in s.phys.GetComponentsInChildren<Transform>())
            {
                if (!t.name.Contains("Half") || t.GetComponent<Renderer>() == null && t.GetComponentInChildren<Renderer>() == null) continue;
                // only plates whose nearest segment is this one
                Transform p = t.parent;
                bool mine = true;
                while (p != null && p != s.phys)
                {
                    if (p.GetComponent<Rigidbody>() != null) { mine = false; break; }
                    p = p.parent;
                }
                if (mine && !s.armor.Contains(t) && !IsInsideOther(t, s)) s.armor.Add(t);
            }
        }

        private static bool IsInsideOther(Transform t, Segment s)
        {
            for (Transform p = t.parent; p != null && p != s.phys; p = p.parent) if (p.name.Contains("Half")) return true;
            return false;
        }

        private void MakeBody(Segment s)
        {
            var rb = s.rb;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.solverIterations = 16;
            rb.solverVelocityIterations = 6;
            rb.maxAngularVelocity = 45f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            _totalMass += rb.mass;

            // one box around the segment's own meshes (not other segments'), shrunk a little so neighbours overlap less
            Bounds b = default;
            bool any = false;
            foreach (var mf in s.phys.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || OwnerOf(mf.transform) != s.phys) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 c = mb.center, e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = s.phys.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(local, Vector3.zero); any = true; }
                    else b.Encapsulate(local);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one * 0.3f);
            var box = s.phys.gameObject.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = Vector3.Max(b.size * 0.88f, Vector3.one * 0.12f);
            var mat = PhysicsMat();
            box.sharedMaterial = mat;

            var hit = s.phys.gameObject.AddComponent<MechRagdollPart>();
            hit.owner = this;
            hit.segment = s;
        }

        private static Transform OwnerOf(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.GetComponent<Rigidbody>() != null) return p;
            return null;
        }

        private static PhysicsMaterial _mat;
        private static PhysicsMaterial PhysicsMat()
        {
            if (_mat == null)
            {
                _mat = new PhysicsMaterial("MechSteel")
                {
                    dynamicFriction = 0.7f, staticFriction = 0.9f, bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }
            return _mat;
        }

        private void MakeJoint(Segment s, MechClamshellBuilder mech)
        {
            var j = s.phys.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = s.parent.rb;
            j.autoConfigureConnectedAnchor = false;
            Vector3 anchor = Vector3.zero;
            if (s.name.StartsWith("Foot")) anchor = new Vector3(0f, MechClamshellBuilder.AnkleHeight, 0f);
            j.anchor = anchor;
            s.connectedAnchorRest = s.parent.phys.InverseTransformPoint(s.phys.TransformPoint(anchor));
            j.connectedAnchor = s.connectedAnchorRest;
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 8f;
            j.enablePreprocessing = false;
            s.joint = j;
            s.startLocal = Quaternion.Inverse(s.parent.phys.rotation) * s.phys.rotation;
            ApplyDrive(s, 1f);
        }

        private static void ApplyDrive(Segment s, float m)
        {
            if (s.joint == null) return;
            var d = new JointDrive { positionSpring = s.spring * m, positionDamper = s.damper * Mathf.Max(0.3f, m), maximumForce = float.MaxValue };
            s.joint.slerpDrive = d;
        }

        // ------------------------------------------------------------------ simulation
        private float _appliedMuscle = -1f;

        private float _follow = 1f;          // 0..1: how exactly the hips and legs copy the walk cycle
        private bool _legsKinematic;

        private bool IsLowerBody(Segment s) => s == _pelvis || s.name.StartsWith("Thigh") || s.name.StartsWith("Calf") || s.name.StartsWith("Foot");

        private void SetLowerBodyKinematic(bool on)
        {
            if (_legsKinematic == on) return;
            _legsKinematic = on;
            foreach (var s in _segs)
            {
                if (!IsLowerBody(s)) continue;
                bool k = on && !s.detached;
                if (s.rb.isKinematic == k) continue;
                if (!k) { s.rb.isKinematic = false; continue; }
                s.rb.linearVelocity = Vector3.zero;
                s.rb.angularVelocity = Vector3.zero;
                s.rb.isKinematic = true;
            }
        }

        /// <summary>
        /// Standing: the hips and legs are the Deploy walk cycle (MechGaitAnimator on the ghost), copied exactly
        /// (kinematic), so feet plant and step for real; torso, arms and head are physical on motorised joints, so
        /// blows rock the upper body and knock the mech back. Knocked down (or off an edge): the whole body goes
        /// limp; getting up blends the legs back onto the walk cycle.
        /// </summary>
        private void FixedUpdate()
        {
            if (_pelvis == null || ghost == null) return;
            if (netPuppet) return;   // online client: the host simulates this body
            float dt = Time.fixedDeltaTime;

            // walked or was knocked off the edge: nothing under the hips any more - fall
            if (!IsDown && _recoverT <= 0f && !SupportUnder(ghost.transform.position)) Knockdown(2.5f);

            if (!IsKO)
            {
                if (_downT > 0f)
                {
                    _downT -= dt;
                    muscle = Mathf.MoveTowards(muscle, 0.12f, dt * 6f);
                    balance = 0f;
                    if (_grabbedBy != null) _downT = Mathf.Max(_downT, 0.2f);
                    // only get up where there is floor to stand on
                    if (_downT <= 0f)
                    {
                        if (SupportUnder(_pelvis.rb.position)) _recoverT = 0.9f;
                        else _downT = 0.5f;
                    }
                }
                else if (_recoverT > 0f)
                {
                    _recoverT -= dt;
                    float k = 1f - _recoverT / 0.9f;
                    muscle = Mathf.Lerp(0.3f, 1f, k);
                    balance = k;
                }
                else
                {
                    muscle = Mathf.MoveTowards(muscle, 1f, dt * 2f);
                    balance = 1f;
                }
                _poise = Mathf.MoveTowards(_poise, poiseMax, dt * (Time.time - _lastHitT > 1.2f ? 14f : 3f));
            }
            else
            {
                _koElapsed += dt;
                if (!_koCleared && _koElapsed > 2.0f)
                {
                    _koCleared = true;
                    // Once a KO'd mech has collapsed onto the floor, ignore collisions with active fighters so living mechs never get snagged or propped up on dead chassis piles
                    var myCols = GetComponentsInChildren<Collider>();
                    foreach (var otherPart in FindObjectsByType<MechRagdollPart>())
                    {
                        if (otherPart == null || otherPart.owner == null || otherPart.owner == this) continue;
                        foreach (var otherCol in otherPart.GetComponents<Collider>())
                        {
                            foreach (var myCol in myCols)
                                if (myCol != null && otherCol != null) Physics.IgnoreCollision(myCol, otherCol, true);
                        }
                    }
                }
            }
            ghost.suppressed = IsDown || _recoverT > 0f;

            bool standing = !IsDown;
            if (standing && !_legsKinematic)
            {
                // getting up: put the ghost where the body lies (snapped to the slope height!), then snap lower body and torso cleanly to prevent joint offset drift
                Vector3 pp = _torso != null && _torso.rb != null ? _torso.rb.position : _pelvis.rb.position;
                float groundY = MechBoxer.TryGround(pp + Vector3.up * 4f, out float gy) ? gy : pp.y;
                ghost.transform.position = new Vector3(pp.x, groundY, pp.z);
                foreach (var s in _segs)
                {
                    if (!IsLowerBody(s) || s.detached || s.target == null) continue;
                    s.rb.position = s.target.position;
                    s.rb.rotation = s.target.rotation;
                }
                if (_torso != null && _torso.rb != null && _torso.target != null)
                {
                    _torso.rb.linearVelocity = Vector3.zero;
                    _torso.rb.angularVelocity = Vector3.zero;
                    _torso.rb.position = _torso.target.position;
                    _torso.rb.rotation = _torso.target.rotation;
                }
                _follow = 0f;
            }
            SetLowerBodyKinematic(standing);
            _follow = standing ? Mathf.MoveTowards(_follow, 1f, dt / 0.6f) : 0f;
            if (_torso != null && _torso.joint != null && _pelvis != null && _torso.target != null && _pelvis.target != null)
            {
                _torso.joint.connectedAnchor = standing
                    ? _pelvis.target.InverseTransformPoint(_torso.target.TransformPoint(_torso.joint.anchor))
                    : _torso.connectedAnchorRest;
            }

            // motors on the physical joints (upper body; the whole body while limp; snappier during active strikes)
            float strikeBoost = standing && ghost.StrikeActive ? 1.38f : 1f;
            float targetDrive = muscle * strikeBoost;
            if (Mathf.Abs(_appliedMuscle - targetDrive) > 0.01f)
            {
                foreach (var s in _segs) ApplyDrive(s, _lost.Contains(s.limb) ? 0.02f : targetDrive);
                _appliedMuscle = targetDrive;
            }
            foreach (var s in _segs)
            {
                if (s.joint == null || s.target == null || s.parent == null) continue;
                Quaternion targetLocal = Quaternion.Inverse(s.parent.target.rotation) * s.target.rotation;
                SetTargetRotationLocal(s.joint, targetLocal, s.startLocal);
            }

            if (standing)
            {
                // hips and legs = the walk cycle
                float k = _follow >= 1f ? 1f : 1f - Mathf.Exp(-10f * dt) + _follow * 0.5f;
                foreach (var s in _segs)
                {
                    if (!IsLowerBody(s) || s.detached || !s.rb.isKinematic || s.target == null) continue;
                    s.rb.MovePosition(Vector3.Lerp(s.rb.position, s.target.position, Mathf.Clamp01(k)));
                    s.rb.MoveRotation(Quaternion.Slerp(s.rb.rotation, s.target.rotation, Mathf.Clamp01(k)));
                }
                // keep the torso upright over the hips on top of its joint motor
                PdRotate(_torso.rb, _torso.target.rotation, 5.5f * balance, 0.9f);
                if (ghost.StrikeActive) TickActiveStrikeSweep();
            }

            // the ghost never wanders away from the body (falls, sliding down hills, being thrown)
            if (IsDown)
            {
                Vector3 pp = _pelvis.rb.position;
                Vector3 err = pp - _pelvis.target.position;
                err.y = 0f;
                ghost.transform.position += err;
                if (MechBoxer.TryGround(ghost.transform.position + Vector3.up * 4f, out float gy))
                {
                    Vector3 gp = ghost.transform.position;
                    gp.y = gy;
                    ghost.transform.position = gp;
                }
            }

            Grapple(dt);
            if (_grabbedBy != null) TickStruggle(dt);
        }

        /// <summary>Floor (not a mech or a steep cliff wall) under this point, accounting for hills and slopes.</summary>
        private bool SupportUnder(Vector3 p)
        {
            if (p.x * p.x + p.z * p.z > 33.5f * 33.5f) return false;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < 0.62f) continue; // steep cliff walls are not standable floor
                var rb = hit.collider.attachedRigidbody;
                if (rb != null && rb.GetComponent<MechRagdollPart>() != null) continue;
                return true;
            }
            return false;
        }

        private static void PdRotate(Rigidbody rb, Quaternion target, float freq, float zeta)
        {
            Quaternion delta = target * Quaternion.Inverse(rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 0.01f) axis = Vector3.zero;
            float w = 2f * Mathf.PI * freq;
            Vector3 acc = w * w * axis * (angle * Mathf.Deg2Rad) - 2f * zeta * w * rb.angularVelocity;
            rb.AddTorque(Vector3.ClampMagnitude(acc, 220f), ForceMode.Acceleration);
        }

        private bool Grounded()
        {
            foreach (var s in _segs)
            {
                if (!s.name.StartsWith("Foot") || s.detached) continue;
                foreach (var hit in Physics.RaycastAll(s.rb.position + Vector3.up * 0.4f, Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.rigidbody == null || hit.rigidbody.GetComponent<MechRagdollPart>() == null) return true;
                }
            }
            return false;
        }

        private int LegsLeft() => (_lost.Contains(LimbId.LegL) ? 0 : 1) + (_lost.Contains(LimbId.LegR) ? 0 : 1);

        /// <summary>From the Unity forums (mstevenson): drive a ConfigurableJoint to a local rotation.</summary>
        private static void SetTargetRotationLocal(ConfigurableJoint joint, Quaternion targetLocalRotation, Quaternion startLocalRotation)
        {
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);
            Quaternion r = Quaternion.Inverse(worldToJoint);
            r *= Quaternion.Inverse(targetLocalRotation) * startLocalRotation;
            r *= worldToJoint;
            joint.targetRotation = r;
        }

        // ------------------------------------------------------------------ damage
        /// <summary>Called by a striking part of <paramref name="attacker"/> that hit <paramref name="seg"/>.</summary>
        /// <summary>A fighter's strike (fist, foot, knee, head, shoulder) reached this mech: dodge, parry and block are
        /// resolved here before any damage. Explosions, falls and thrown bodies call ReceiveStrike directly.</summary>
        public void ReceiveFighterStrike(MechRagdoll attacker, Segment seg, Vector3 point, Vector3 dir, float damage, float power)
        {
            if (seg.detached || IsKO || attacker == null || attacker.ghost == null) return;
            var atk = attacker.ghost;
            atk.OnStrikeLanded();
            if (!IsDown && ghost != null)
            {
                if (ghost.Evading) { CombatCue?.Invoke(this, attacker, point, "EVADE"); return; }
                Vector3 toAtk = attacker.Position - Position;
                toAtk.y = 0f;
                Vector3 fwd = ghost.transform.forward;
                fwd.y = 0f;
                bool facing = toAtk.sqrMagnitude > 1e-4f && Vector3.Dot(fwd.normalized, toAtk.normalized) > 0.2f;
                if (facing && !IsHeld && !atk.StrikeUnblockable)
                {
                    if (ghost.Parrying)
                    {
                        atk.OnParried();
                        attacker.muscle = Mathf.Min(attacker.muscle, 0.55f);
                        attacker._poise -= 14f;
                        if (attacker.ghost != null) attacker.ghost.AddKnockback(toAtk.normalized * 1.6f);
                        ghost.OnParrySuccess();
                        EmitHitSparks(point, 0.9f);
                        CombatCue?.Invoke(this, attacker, point, "PARRY");
                        return;
                    }
                    if (ghost.Blocking)
                    {
                        ghost.OnBlocked(damage, atk.StrikeGuardBreak, dir);
                        atk.OnStrikeBlocked();
                        health -= damage * 0.08f;   // chip damage only: no limb damage, no armor shed
                        _lastHitT = Time.time;
                        seg.rb.AddForceAtPosition(dir.normalized * damage * 1.5f, point, ForceMode.Impulse);
                        EmitHitSparks(point, 0.25f);
                        CombatCue?.Invoke(this, attacker, point, ghost.GuardBroken ? "GUARD BREAK" : "BLOCK");
                        if (health <= 0f && !IsKO) KnockOut();
                        return;
                    }
                }
            }
            float poiseMult = 1f;
            string cue = null;
            if (atk.StrikeIsRiposte) { poiseMult = 2.2f; cue = "RIPOSTE"; }
            else if (ghost != null && (ghost.Vulnerable || ghost.GuardBroken))
            {
                damage *= 1.3f;
                poiseMult = 1.5f;
                cue = "COUNTER";
            }
            ReceiveStrike(attacker, seg, point, dir, damage, power, poiseMult);
            if (cue != null) CombatCue?.Invoke(this, attacker, point, cue);
        }

        /// <summary>victim, attacker, point, cue ("PARRY", "BLOCK", "GUARD BREAK", "COUNTER", "RIPOSTE", "EVADE")</summary>
        public event Action<MechRagdoll, MechRagdoll, Vector3, string> CombatCue;

        public void ReceiveStrike(MechRagdoll attacker, Segment seg, Vector3 point, Vector3 dir, float damage, float power, float poiseMult = 1f)
        {
            if (seg.detached) return;
            damage = Mathf.Clamp(damage, 0f, 30f);
            if (damage < 1.5f) return;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -transform.forward;

            bool headShot = seg.limb == LimbId.Head;
            float mult = headShot ? 1.35f : seg == _torso || seg == _pelvis ? 1f : 0.8f;
            health -= damage * mult;
            _lastHitT = Time.time;
            _poise -= damage * (headShot ? 1.3f : 1f) * poiseMult;

            // the blow rocks the body (a weighty stagger, not a launch): a shove at the contact point on top of the collision
            float shove = (attacker != null ? attacker.knockbackScale : knockbackScale) * knockbackTakenScale;
            seg.rb.AddForceAtPosition(dir * damage * 5f * shove, point, ForceMode.Impulse);
            ghost.Interrupt(0.18f + damage * 0.018f * knockbackTakenScale);
            ghost.AddKnockback(dir * Mathf.Lerp(0.6f, 3.2f, damage / 25f) * shove);
            muscle = Mathf.Min(muscle, Mathf.Lerp(1f, 0.7f, damage / 25f));   // a hard hit goes slack for a beat

            bool ripped = false;
            float tearMult = attacker != null ? attacker.limbTearScale : 1f;
            LimbId targetLimb = seg.limb;   // torso hits wear down the hull, not a random limb
            if (targetLimb != LimbId.None && !_lost.Contains(targetLimb))
            {
                float limbDmg = damage * (seg.limb != LimbId.None ? (headShot ? 0.8f : 0.9f) : 0.5f) * tearMult;
                _limbHealth[targetLimb] -= limbDmg;
                float lh = _limbHealth[targetLimb];
                Segment armorSeg = seg.limb == targetLimb ? seg : null;
                if (armorSeg == null)
                {
                    foreach (var s in _segs) if (s.limb == targetLimb && s.armor.Count > 0) { armorSeg = s; break; }
                }
                // armor cracks off the damaged limb as it weakens; torso clamshell stays intact until critical hull (<28% HP)
                if (armorSeg != null)
                {
                    if (lh < 38f) ShedArmor(armorSeg, dir, 1);
                    if (lh < 18f) ShedArmor(armorSeg, dir, 2);
                }
                if (health < maxHealth * 0.28f && (seg == _torso || seg == _pelvis)) ShedArmor(seg, dir, 1);
                if (lh <= 0f)
                {
                    // Decapitation only finishes a fighter who is already below 35% hull integrity; otherwise it sheds head armor & knocks down!
                    if (targetLimb == LimbId.Head && health > maxHealth * 0.35f)
                    {
                        _limbHealth[targetLimb] = 16f;
                        if (armorSeg != null) ShedArmor(armorSeg, dir, 2);
                        Knockdown(2.0f);
                    }
                    else if (targetLimb != LimbId.Head && health > maxHealth * LimbRipHull)
                    {
                        // a limb only comes off a fighter that is already badly damaged; before that it is wrecked but holds
                        _limbHealth[targetLimb] = 12f;
                        if (armorSeg != null) ShedArmor(armorSeg, dir, 2);
                    }
                    else
                    {
                        RipLimb(targetLimb, dir * damage);
                        ripped = true;
                    }
                }
            }
            else if (health < maxHealth * 0.28f) ShedArmor(seg, dir, 1);

            if (_poise <= 0f && !IsKO) Knockdown(1.4f + damage * 0.03f);
            if (health <= 0f && !IsKO) KnockOut();
            EmitHitSparks(point, Mathf.Clamp01(damage / 20f) + (ripped ? 0.8f : 0f));
            Hit?.Invoke(this, attacker, point, damage, power, ripped);
        }

        private static ParticleSystem _sparks;

        private static void EmitHitSparks(Vector3 at, float intensity)
        {
            if (_sparks == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) return;
                var go = new GameObject("HitSparksPool") { hideFlags = HideFlags.DontSave };
                _sparks = go.AddComponent<ParticleSystem>();
                _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _sparks.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 16f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
                main.startColor = new ParticleSystem.MinMaxGradient(IronFaunaPalette.OrangeJointCore, IronFaunaPalette.HazardYellow);
                main.gravityModifier = 1.6f;
                main.maxParticles = 400;
                main.playOnAwake = false;
                var emission = _sparks.emission;
                emission.rateOverTime = 0f;
                var shape = _sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.18f;
                var size = _sparks.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                var pr = go.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.velocityScale = 0.06f;
                pr.lengthScale = 2.2f;
                var mat = new Material(sh) { hideFlags = HideFlags.DontSave };
                mat.SetColor("_BaseColor", IronFaunaPalette.OrangeJointCore);
                pr.sharedMaterial = mat;
            }
            var ep = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true };
            _sparks.Emit(ep, Mathf.RoundToInt(Mathf.Lerp(8f, 32f, Mathf.Clamp01(intensity))));
        }

        public void Knockdown(float seconds)
        {
            if (_heldSeg != null) DropHold();
            _downT = Mathf.Max(_downT, seconds);
            _poise = poiseMax * 0.6f;
        }

        private void KnockOut()
        {
            IsKO = true;
            health = 0f;
            muscle = 0.04f;
            balance = 0f;
            _downT = 999f;
            ghost.suppressed = true;
            KnockedOut?.Invoke(this);
        }

        public void ReviveSecondWind(float hp = 24f)
        {
            IsKO = false;
            _koElapsed = 0f;
            health = Mathf.Max(health, hp);
            _poise = poiseMax * 0.8f;
            _downT = 0.25f;
            _recoverT = 0.7f;
            muscle = 0.6f;
            balance = 0.6f;
            if (ghost != null) ghost.suppressed = false;
        }

        private void ShedArmor(Segment seg, Vector3 dir, int count)
        {
            for (int i = 0; i < count && seg.armor.Count > 0; i++)
            {
                Transform plate = seg.armor[seg.armor.Count - 1];
                seg.armor.RemoveAt(seg.armor.Count - 1);
                if (plate == null) continue;
                plate.SetParent(null, true);
                var rb = plate.gameObject.AddComponent<Rigidbody>();
                rb.mass = 3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var mf = plate.GetComponentInChildren<MeshFilter>();
                var box = plate.gameObject.AddComponent<BoxCollider>();
                if (mf != null && mf.sharedMesh != null && mf.transform == plate)
                {
                    box.center = mf.sharedMesh.bounds.center;
                    box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.05f);
                }
                box.sharedMaterial = PhysicsMat();
                rb.linearVelocity = seg.rb.linearVelocity;
                rb.AddForce((dir + UnityEngine.Random.insideUnitSphere * 0.6f + Vector3.up * 0.5f).normalized * 18f, ForceMode.Impulse);
                rb.AddTorque(UnityEngine.Random.insideUnitSphere * 30f, ForceMode.Impulse);
                foreach (var c in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, c, true);
                Destroy(plate.gameObject, 12f);
                ArmorShed?.Invoke(this, plate.position);
            }
        }

        /// <summary>Tears a whole limb off at its root joint; it keeps flopping as one piece.</summary>
        public void RipLimb(LimbId limb, Vector3 impulse)
        {
            if (_lost.Contains(limb)) return;
            _lost.Add(limb);
            foreach (var s in _segs)
            {
                if (s.limb != limb) continue;
                bool root = s.parent != null && s.parent.limb != limb;
                ApplyDrive(s, root ? 0.02f : 0.22f); // keep child segments (forearm, calf/foot) joined to their torn limb root
                if (root && s.joint != null)
                {
                    Destroy(s.joint);
                    s.joint = null;
                    s.detached = true;
                    s.rb.isKinematic = false;
                    s.rb.AddForce(impulse * 1.2f + Vector3.up * 25f, ForceMode.Impulse);
                }
            }
            foreach (var s in _segs) if (s.limb == limb) { s.detached = true; s.rb.isKinematic = false; }
            // a torn-off limb is a separate object now: it may hit its old body
            foreach (var s in _segs)
            {
                if (s.limb != limb) continue;
                foreach (var o in _segs)
                {
                    if (o.limb == limb) continue;
                    foreach (var c1 in s.phys.GetComponents<Collider>())
                        foreach (var c2 in o.phys.GetComponents<Collider>()) Physics.IgnoreCollision(c1, c2, false);
                }
            }
            _appliedMuscle = -1f;
            if (ghost != null)
            {
                ghost.SetLimbState(HasLimb(LimbId.ArmL), HasLimb(LimbId.ArmR), HasLimb(LimbId.LegL), HasLimb(LimbId.LegR));
                if (ghost.ArmsLeft == 0 && _heldSeg != null) DropHold();
            }
            if ((limb == LimbId.LegL || limb == LimbId.LegR) && !IsKO)
            {
                // Losing a leg compromises balance immediately and permanently lowers max poise!
                poiseMax *= 0.65f;
                _poise = Mathf.Min(_poise, poiseMax);
                Knockdown(1.5f);
            }
            if (limb == LimbId.Head && !IsKO) KnockOut();
            LimbRipped?.Invoke(this, limb);
        }

        public bool HasLimb(LimbId limb) => !_lost.Contains(limb);

        // ------------------------------------------------------------------ tournament wear & mods
        /// <summary>Multiplies every limb's full health (tournament Joint Locks mod). Limbs are refilled.</summary>
        public void ScaleLimbHealth(float k)
        {
            foreach (LimbId id in Enum.GetValues(typeof(LimbId)))
            {
                if (id == LimbId.None) continue;
                _limbMax[id] = LimbMax(id) * k;
                _limbHealth[id] = _limbMax[id];
            }
        }

        /// <summary>Sets a limb's current health as a fraction of its full health (damage carried between bouts).</summary>
        public void SetLimbHealth01(LimbId id, float t)
        {
            if (id == LimbId.None || _lost.Contains(id)) return;
            _limbHealth[id] = Mathf.Max(1f, LimbMax(id) * Mathf.Clamp01(t));
        }

        /// <summary>
        /// Starts a bout without a limb that was torn off earlier and not replaced: no flying limb, no knockdown,
        /// no announcer call - the segments are simply gone and the animation adapts.
        /// </summary>
        public void RemoveLimbQuiet(LimbId limb)
        {
            if (limb == LimbId.None || limb == LimbId.Head || _lost.Contains(limb)) return;
            _lost.Add(limb);
            foreach (var s in _segs)
            {
                if (s.limb != limb) continue;
                s.detached = true;
                if (s.joint != null) { Destroy(s.joint); s.joint = null; }
                s.rb.isKinematic = true;
                s.armor.Clear();
                s.phys.gameObject.SetActive(false);
            }
            _appliedMuscle = -1f;
            if (ghost != null) ghost.SetLimbState(HasLimb(LimbId.ArmL), HasLimb(LimbId.ArmR), HasLimb(LimbId.LegL), HasLimb(LimbId.LegR));
            if (limb == LimbId.LegL || limb == LimbId.LegR)
            {
                poiseMax *= 0.65f;
                _poise = Mathf.Min(_poise, poiseMax);
            }
        }

        // ------------------------------------------------------------------ strike trails (readability)
        private readonly List<KeyValuePair<Segment, TrailRenderer>> _trails = new List<KeyValuePair<Segment, TrailRenderer>>();
        private static Material _trailMat;

        private void AddTrail(Segment s, Vector3 local)
        {
            var go = new GameObject("StrikeTrail");
            go.transform.SetParent(s.phys, false);
            go.transform.localPosition = local;
            s.tip = go.transform;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.22f;
            tr.minVertexDistance = 0.05f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(IronFaunaPalette.OffWhitePanel, 0f), new GradientColorKey(IronFaunaPalette.OrangeJointCore, 0.35f), new GradientColorKey(IronFaunaPalette.CrimsonFront, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.emitting = false;
            if (_trailMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null)
                {
                    _trailMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                    _trailMat.SetFloat("_Surface", 1f);
                    _trailMat.SetFloat("_Blend", 2f);   // additive
                    _trailMat.SetOverrideTag("RenderType", "Transparent");
                    _trailMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    _trailMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _trailMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    _trailMat.SetInt("_ZWrite", 0);
                    _trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    _trailMat.EnableKeyword("_BLENDMODE_ADD");
                }
            }
            if (_trailMat != null) tr.sharedMaterial = _trailMat;
            _trails.Add(new KeyValuePair<Segment, TrailRenderer>(s, tr));
        }

        private void Update()
        {
            if (ghost == null) return;
            if (netPuppet) TickPuppet(Time.deltaTime);
            foreach (var kv in _trails)
            {
                var s = kv.Key;
                bool leg = s.limb == LimbId.LegL || s.limb == LimbId.LegR;
                bool mine = (ghost.StrikeActive || ghost.WindingUp) && ghost.StrikeLead == s.leadHand && ghost.StrikeIsKick == leg && !s.detached;
                if (kv.Value != null) kv.Value.emitting = mine && !IsDown;
            }
        }

        /// <summary>A part hit something: strikes, wielded limbs, falls and thrown bodies all go through here.</summary>
        internal void OnPartCollision(Segment mine, Collision c)
        {
            if (mine == null || c == null) return;
            if (netPuppet) return;   // online client: hits are decided by the host
            if (mine.heldBy != null) { mine.heldBy.WeaponStrike(mine, c); return; }
            TryStrike(mine, c);
            ImpactDamage(mine, c);
        }

        private int _propStrikeSerial = -1;

        /// <summary>Ensures kinematic kicks/sweeps and fast strikes reliably connect with enemy legs, trees, and buildings even when both colliders are kinematic.</summary>
        private void TickActiveStrikeSweep()
        {
            if (IsDown || ghost == null || !ghost.StrikeActive) return;
            int serial = ghost.PunchSerial;
            if (_propStrikeSerial != serial)
            {
                Vector3 envProbe = Position + ghost.transform.forward * 1.4f + Vector3.up * 1.4f;
                foreach (var col in Physics.OverlapSphere(envProbe, 1.45f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var tree = col.GetComponentInParent<FallingTree>();
                    if (tree != null)
                    {
                        _propStrikeSerial = serial;
                        tree.ApplyStrike(envProbe, ghost.transform.forward * (6f * knockbackScale), ghost.PunchDamage * 8f * knockbackScale);
                        break;
                    }
                    var prop = col.GetComponentInParent<BreakableProp>();
                    if (prop != null)
                    {
                        _propStrikeSerial = serial;
                        prop.ApplyStrike(envProbe, ghost.transform.forward * (6f * knockbackScale), ghost.PunchDamage * 7f * knockbackScale);
                        break;
                    }
                }
            }
            foreach (var mine in _segs)
            {
                if (mine.detached) continue;
                bool arm = mine.limb == LimbId.ArmL || mine.limb == LimbId.ArmR;
                bool leg = mine.limb == LimbId.LegL || mine.limb == LimbId.LegR;
                bool side = ghost.StrikeLead == mine.leadHand;
                bool armStrike = arm && mine.striker && side && !ghost.StrikeIsKick && !ghost.StrikeIsHead && !ghost.StrikeIsBody;
                bool legStrike = leg && mine.striker && side && ghost.StrikeIsKick;
                bool headStrike = mine == _head && ghost.StrikeIsHead;
                bool bodyStrike = mine == _torso && ghost.StrikeIsBody;
                if (!armStrike && !legStrike && !headStrike && !bodyStrike) continue;

                Vector3 tipPos = mine.tip != null ? mine.tip.position : mine.rb.position;
                float radius = bodyStrike ? 0.85f : legStrike ? 0.62f : 0.48f;
                foreach (var col in Physics.OverlapSphere(tipPos, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = col.attachedRigidbody;
                    var otherPart = rb != null ? rb.GetComponent<MechRagdollPart>() : col.GetComponentInParent<MechRagdollPart>();
                    if (otherPart == null || otherPart.owner == null || otherPart.owner == this || otherPart.segment == null || otherPart.segment.detached) continue;
                    if (team != 0 && otherPart.owner.team == team) continue;
                    if (_struckBy.TryGetValue(otherPart.owner, out int last) && last == serial) continue;
                    _struckBy[otherPart.owner] = serial;
                    Vector3 dir = otherPart.owner.Position - Position;
                    dir.y = 0f;
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : ghost.transform.forward;
                    var victim = otherPart.owner;
                    float before = victim.health;
                    victim.ReceiveFighterStrike(this, otherPart.segment, tipPos, dir + Vector3.up * 0.1f, ghost.PunchDamage * StrikeDamageScale, ghost.PunchPower);
                    if (ghost.StrikeSweeps && !victim.IsDown && victim.health < before - 3f)
                    {
                        victim.Knockdown(1.25f);
                    }
                }
            }
        }

        /// <summary>One damaging contact per strike per victim: fists, feet, knees, the head (headbutt), the torso (charge).</summary>
        internal void TryStrike(Segment mine, Collision c)
        {
            if (IsDown || ghost == null || mine == null || mine.detached || !ghost.StrikeActive || c == null) return;
            var prop = c.collider != null ? c.collider.GetComponentInParent<BreakableProp>() : null;
            if (prop != null)
            {
                Vector3 hitPt = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
                prop.ApplyStrike(hitPt, transform.forward * (5f * knockbackScale), ghost.PunchDamage * 6f * knockbackScale);
            }
            var tree = c.collider != null ? c.collider.GetComponentInParent<FallingTree>() : null;
            if (tree != null)
            {
                Vector3 hitPt = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
                tree.ApplyStrike(hitPt, transform.forward * (5f * knockbackScale), ghost.PunchDamage * 6f * knockbackScale);
            }
            var otherPart = c.rigidbody != null ? c.rigidbody.GetComponent<MechRagdollPart>() : null;
            if (otherPart == null || otherPart.owner == null || otherPart.owner == this || otherPart.segment == null || otherPart.segment.detached) return;
            if (team != 0 && otherPart.owner.team == team) return; // friendly fire off in Co-Op Campaign & Team Brawl
            bool arm = mine.limb == LimbId.ArmL || mine.limb == LimbId.ArmR;
            bool leg = mine.limb == LimbId.LegL || mine.limb == LimbId.LegR;
            bool side = ghost.StrikeLead == mine.leadHand;
            bool armStrike = arm && mine.striker && side && !ghost.StrikeIsKick && !ghost.StrikeIsHead && !ghost.StrikeIsBody;
            bool legStrike = leg && side && ghost.StrikeIsKick;
            bool headStrike = mine == _head && ghost.StrikeIsHead;
            bool bodyStrike = (mine == _torso || mine.name.StartsWith("UpperArm")) && ghost.StrikeIsBody;
            if (!armStrike && !legStrike && !headStrike && !bodyStrike) return;
            int serial = ghost.PunchSerial;
            if (_struckBy.TryGetValue(otherPart.owner, out int last) && last == serial) return;
            _struckBy[otherPart.owner] = serial;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
            Vector3 dir = otherPart.owner.Position - Position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            float speed = c.relativeVelocity.magnitude;
            float damage = ghost.PunchDamage * StrikeDamageScale * Mathf.Clamp(speed / 8f, 0.8f, 1.1f);
            float hpBefore = otherPart.owner.health;
            otherPart.owner.ReceiveFighterStrike(this, otherPart.segment, point, dir + Vector3.up * 0.1f, damage, ghost.PunchPower);
            if (ghost.StrikeSweeps && !otherPart.owner.IsDown && otherPart.owner.health < hpBefore - 3f)
            {
                otherPart.owner.Knockdown(1.25f);
            }
        }

        // ------------------------------------------------------------------ falls, throws, bowling
        private float _impactT;
        private MechRagdoll _thrownBy;
        private float _thrownT = -10f, _slammedT = -10f;
        private float _slamMult = 1.8f;
        private bool _impactUsed;

        // ------------------------------------------------------------------ grab balance (owner 2026-11-22: "grab-throw
        // over and over, no struggle, no way out, way too much damage - won the tournament throwing them off the cliff")
        private const float HoldStaminaPerSecond = 9f;
        private const float GrabImmunity = 2.5f, GrabCooldown = 1.2f;
        private float _grabImmuneUntil, _grabCooldownUntil;
        private float _struggle;
        private int _mashSeen;
        private int _grabStreak;
        private float _lastGrabbedT = -99f;
        private bool? _isHuman;
        private float _aiSkill = 0.5f;

        /// <summary>0..1 progress of breaking out of a grab (HUD meter).</summary>
        public float Struggle01 => Mathf.Clamp01(_struggle);

        /// <summary>Grabbed again and again within 10 s: each throw / slam / piledriver does 30% less.</summary>
        internal float RepeatGrabFactor => Mathf.Pow(0.7f, Mathf.Max(0, _grabStreak - 1));

        /// <summary>The held robot fights back: players mash any attack / dodge key, AI struggles by skill; more stamina
        /// = faster; an exhausted grabber loses the grip twice as fast. Full meter = break free.</summary>
        private void TickStruggle(float dt)
        {
            if (_grabbedBy == null || IsKO || ghost == null) return;
            if (_isHuman == null)
            {
                _isHuman = ghost.GetComponent<MechBoxerPlayerInput>() != null;
                var ai = ghost.GetComponent<MechBoxerAI>();
                if (ai != null) _aiSkill = ai.skill;
            }
            float rate = 0.16f;
            if (_isHuman == true)
            {
                int presses = ghost.mashCount - _mashSeen;
                _mashSeen = ghost.mashCount;
                _struggle += presses * 0.11f;
            }
            else rate += 0.25f + _aiSkill * 0.45f;
            rate *= Mathf.Lerp(0.6f, 1.3f, ghost.Stamina01);
            if (_grabbedBy.ghost != null && _grabbedBy.ghost.Exhausted) rate *= 2f;
            _struggle += rate * dt;
            if (_struggle >= 1f) _grabbedBy.BreakFree(this);
        }

        /// <summary>The held robot escapes: both are shoved apart, the grabber loses stamina and can't re-grab them for a bit.</summary>
        internal void BreakFree(MechRagdoll victim)
        {
            if (_held != victim) return;
            DropHold();
            if (ghost != null) ghost.SpendStamina(20f);
            Vector3 away = victim.Position - Position;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            foreach (var s in victim._segs) if (!s.detached && !s.rb.isKinematic) s.rb.linearVelocity = away * 4f + Vector3.up * 1.5f;
            victim._downT = Mathf.Min(victim._downT, 0.35f);
            GrappleEvent?.Invoke(victim, "BROKE FREE");
        }

        // ------------------------------------------------------------------ ring out (no more instant wins off the cliff)
        /// <summary>
        /// Fell off the arena: lose <paramref name="hullFraction"/> of max hull and get put back on the arena at
        /// <paramref name="spot"/>, knocked down. Returns true if the fall knocked the robot out instead.
        /// </summary>
        public bool RingOutPenalty(float hullFraction, Vector3 spot)
        {
            if (IsKO) return true;
            if (_heldSeg != null) DropHold();
            health -= maxHealth * hullFraction;
            _lastHitT = Time.time;
            if (health <= 0f) { KnockOut(); return true; }
            Vector3 delta = spot + Vector3.up * 1.2f - _pelvis.rb.position;
            foreach (var s in _segs)
            {
                if (s.detached || s.rb == null) continue;
                s.rb.position += delta;
                s.phys.transform.position += delta;
                s.rb.linearVelocity = Vector3.zero;
                s.rb.angularVelocity = Vector3.zero;
            }
            if (ghost != null) ghost.Teleport(spot);
            _downT = Mathf.Max(_downT, 1.2f);
            _grabImmuneUntil = Time.time + GrabImmunity;
            return false;
        }

        // ------------------------------------------------------------------ WWE-style grab finishers (owner 2026-11-22)
        // While holding a robot: E powerbomb, Q chokeslam, F piledriver, R giant swing (LMB throw, RMB tear, Space slam).
        private enum Finisher { None, Powerbomb, Chokeslam, Piledriver, GiantSwing }
        private Finisher _fin;
        private float _finT;

        private void StartFinisher(Finisher f)
        {
            if (_held == null || ghost == null) return;
            if (ghost.Exhausted) return;
            ghost.SpendStamina(f == Finisher.GiantSwing ? 26f : 22f);
            _fin = f;
            _finT = 0f;
            _slamT = -1f;
            BoxingSfx.Whoosh(0.8f);
        }

        /// <summary>Runs the finisher timeline; returns true once the victim has been released.</summary>
        private bool TickFinisher(float dt)
        {
            var victim = _held;
            if (victim == null) { _fin = Finisher.None; return false; }
            _finT += dt;
            Vector3 fwd = ghost.transform.forward, right = ghost.transform.right;
            switch (_fin)
            {
                case Finisher.Powerbomb:
                    // hoist overhead, then crash them down back-first with a shockwave
                    ghost.holdLift = Mathf.Clamp01(_finT / 0.6f);
                    ghost.holdExtraHeight = 0.9f * ghost.holdLift;
                    if (_finT >= 0.68f)
                    {
                        foreach (var s in victim._segs)
                            if (!s.rb.isKinematic) { s.rb.linearVelocity = fwd * 4f + Vector3.down * 34f; s.rb.angularVelocity = right * -7f; }
                        ReleaseFinisher(victim, 2.4f, 2.8f, "POWERBOMB", 3.6f);
                        return true;
                    }
                    break;
                case Finisher.Chokeslam:
                    // one-handed lift by the neck plate, high and fast, then straight down
                    ghost.holdLift = Mathf.Clamp01(_finT / 0.42f);
                    ghost.holdExtraHeight = 1.4f * ghost.holdLift;
                    ghost.holdExtraReach = 0.3f * ghost.holdLift;
                    if (_finT >= 0.52f)
                    {
                        foreach (var s in victim._segs)
                            if (!s.rb.isKinematic) { s.rb.linearVelocity = fwd * 2f + Vector3.down * 38f; s.rb.angularVelocity = right * -4f; }
                        ReleaseFinisher(victim, 2.1f, 2.5f, "CHOKESLAM", 2.8f);
                        return true;
                    }
                    break;
                case Finisher.Piledriver:
                    // flip them upside down, then drive them head-first into the floor
                    ghost.holdLift = Mathf.Clamp01(_finT / 0.5f) * 0.7f;
                    if (_finT < 0.45f)
                        foreach (var s in victim._segs) if (!s.rb.isKinematic) s.rb.angularVelocity = right * 7.5f;
                    if (_finT >= 0.62f)
                    {
                        foreach (var s in victim._segs)
                            if (!s.rb.isKinematic) { s.rb.linearVelocity = Vector3.down * 30f; s.rb.angularVelocity = Vector3.zero; }
                        if (victim._head != null && !victim._head.detached)
                            victim.ReceiveStrike(this, victim._head, victim._head.rb.position, Vector3.down, 24f * victim.RepeatGrabFactor, 0.9f, 2f);
                        ReleaseFinisher(victim, 1.6f, 3.0f, "PILEDRIVER", 2.2f);
                        return true;
                    }
                    break;
                case Finisher.GiantSwing:
                    // grab the legs and spin them round, faster and faster, then let fly
                    float spin = Mathf.Lerp(120f, 420f, Mathf.Clamp01(_finT / 1.1f));
                    ghost.forcedSpin = spin;
                    ghost.holdExtraReach = 0.9f;
                    ghost.holdExtraHeight = -0.35f;
                    if (_finT >= 1.7f)
                    {
                        Vector3 tangent = Vector3.Cross(Vector3.up, (victim.Position - Position).normalized);
                        float speed = spin * Mathf.Deg2Rad * 3f;
                        foreach (var s in victim._segs)
                            if (!s.rb.isKinematic) { s.rb.linearVelocity = tangent * speed + Vector3.up * 6f; s.rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 4f; }
                        victim._thrownBy = this;
                        victim._thrownT = Time.time;
                        victim._impactUsed = false;
                        ghost.forcedSpin = 0f;
                        DropHold();
                        victim.Knockdown(2f);
                        GrappleEvent?.Invoke(this, "GIANT SWING");
                        BoxingSfx.Whoosh(1f);
                        return true;
                    }
                    break;
            }
            return false;
        }

        /// <summary>Lets go of a finisher victim: they land with a damage multiplier, stay down, and anyone close to
        /// the impact gets knocked down by the shockwave.</summary>
        private void ReleaseFinisher(MechRagdoll victim, float slamMult, float downTime, string name, float shockRadius)
        {
            victim._thrownBy = this;
            victim._slammedT = Time.time;
            victim._slamMult = slamMult;
            victim._impactUsed = false;
            Vector3 impact = victim.Position;
            DropHold();
            victim.Knockdown(downTime);
            GrappleEvent?.Invoke(this, name);
            BoxingSfx.Impact(1f);
            Shockwave(impact, shockRadius, victim);
        }

        private void Shockwave(Vector3 point, float radius, MechRagdoll except)
        {
            var hitOwners = new HashSet<MechRagdoll>();
            foreach (var col in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var rb = col.attachedRigidbody;
                var part = rb != null ? rb.GetComponent<MechRagdollPart>() : null;
                if (part == null || part.owner == null || part.owner == this || part.owner == except || part.segment == null || part.segment.detached) continue;
                if (team != 0 && part.owner.team == team) continue;
                if (!hitOwners.Add(part.owner)) continue;
                Vector3 dir = part.owner.Position - point;
                dir.y = 0f;
                part.owner.ReceiveStrike(this, part.segment, part.segment.rb.position, dir.normalized + Vector3.up * 0.4f, 7f, 0.6f);
                if (!part.owner.IsDown) part.owner.Knockdown(1.1f);
            }
        }

        /// <summary>A body that is down (thrown, slammed, knocked flying) takes damage from hard landings, and
        /// bowls over anyone it crashes into.</summary>
        private void ImpactDamage(Segment mine, Collision c)
        {
            if (!IsDown || mine.detached) return;
            if (_grabbedBy != null) return;   // being carried is not a crash: no impact damage until thrown / slammed / released
            float speed = c.relativeVelocity.magnitude;
            if (speed < 8.5f || Time.time - _impactT < 0.3f) return;
            _impactT = Time.time;
            bool slammed = Time.time - _slammedT < 2f;
            bool thrown = Time.time - _thrownT < 3f;
            if ((slammed || thrown) && _impactUsed) return;   // bounces after the first landing don't hurt
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
            float damage = (speed - 8.5f) * 2.4f * (slammed ? _slamMult : 1f);
            if (slammed || thrown)
            {
                _impactUsed = true;
                damage *= (thrown && !slammed ? 0.7f : 1f) * RepeatGrabFactor;
            }
            var attacker = thrown || slammed ? _thrownBy : null;

            var otherPart = c.rigidbody != null ? c.rigidbody.GetComponent<MechRagdollPart>() : null;
            if (otherPart != null && otherPart.owner != null && otherPart.owner != this && otherPart.segment != null && !otherPart.segment.detached)
            {
                // human bowling ball: whoever this crashes into goes down too
                var other = otherPart.owner;
                other.ReceiveStrike(attacker ?? this, otherPart.segment, point, mine.rb.linearVelocity, damage * 0.7f, 0.7f);
                if (speed > 11f && !other.IsDown) other.Knockdown(1.2f);
            }
            ApplyFallDamage(mine, point, damage, attacker);
        }

        private void ApplyFallDamage(Segment seg, Vector3 point, float damage, MechRagdoll attacker)
        {
            damage = Mathf.Clamp(damage, 0f, 25f);
            if (damage < 2f || IsKO) return;
            health -= damage;
            _lastHitT = Time.time;
            bool ripped = false;
            if (seg.limb != LimbId.None && !_lost.Contains(seg.limb))
            {
                _limbHealth[seg.limb] -= damage * 0.4f;
                if (_limbHealth[seg.limb] < 30f) ShedArmor(seg, Vector3.up, 1);
                if (_limbHealth[seg.limb] <= 0f)
                {
                    if (health > maxHealth * LimbRipHull) _limbHealth[seg.limb] = 12f;
                    else { RipLimb(seg.limb, Vector3.up * damage); ripped = true; }
                }
            }
            else ShedArmor(seg, Vector3.up, 1);
            if (health <= 0f) KnockOut();
            Hit?.Invoke(this, attacker, point, damage, Mathf.Clamp01(damage / 25f), ripped);
        }

        // ------------------------------------------------------------------ grappling
        private MechRagdoll _held;                // the mech being held (null when wielding a torn limb)
        private Segment _heldSeg;                 // the part the hands are clamped on
        private float _heldMass, _holdT, _slamT = -1f, _swingT = -1f;
        private int _swingSerial;
        private MechRagdoll _grabbedBy;
        private const float HeldMaxSpeed = 14f, HeldMaxSpin = 18f;
        private Collider[] _bodyColliders;
        private readonly Dictionary<MechRagdoll, Coroutine> _restoreCollision = new Dictionary<MechRagdoll, Coroutine>();

        private Collider[] BodyColliders => _bodyColliders ??= GetComponentsInChildren<Collider>(true);

        /// <summary>Grabber and grabbed body stop colliding with each other while held (the hold point sits inside the
        /// grabber, so the two bodies used to fight each other and explode).</summary>
        private void IgnoreBodyCollision(MechRagdoll other, bool ignore)
        {
            if (other == null) return;
            foreach (var a in BodyColliders)
            {
                if (a == null) continue;
                foreach (var b in other.BodyColliders)
                {
                    if (b != null) Physics.IgnoreCollision(a, b, ignore);
                }
            }
        }

        private System.Collections.IEnumerator RestoreBodyCollisionLater(MechRagdoll other)
        {
            yield return new WaitForSeconds(0.6f);   // let a thrown / slammed body clear the grabber first
            _restoreCollision.Remove(other);
            if (other != null && _held != other) IgnoreBodyCollision(other, false);
        }
        public bool IsHeld => _grabbedBy != null;
        public bool IsHolding => _heldSeg != null;
        public event System.Action<MechRagdoll, string> GrappleEvent;   // who, "GRAB" / "THROW" / "SLAM" / "TORN" / "PICKUP"

        private void Grapple(float dt)
        {
            if (ghost == null) return;
            if (_heldSeg != null && (_heldSeg.rb == null || (_held != null && _held._grabbedBy != this))) DropHold();
            bool want = ghost.intent.grab && !IsDown;
            if (_heldSeg == null && want && ghost.Reaching) TryGrab();

            ghost.holding = _heldSeg != null;
            if (_heldSeg == null)
            {
                ghost.holdLift = 0f;
                ghost.holdSwing = -1f;
                ghost.holdAction = PunchType.None;
                return;
            }
            if (!want) { Throw(); return; }

            // buttons while holding: LMB throw, RMB / Q tear, E / Space slam; a wielded limb swings on any strike
            var act = ghost.holdAction;
            ghost.holdAction = PunchType.None;
            bool weapon = _held == null;
            if (act != PunchType.None && _slamT < 0f)
            {
                if (weapon)
                {
                    if (_swingT < 0f) { _swingT = 0f; _swingSerial++; BoxingSfx.Whoosh(1f); }
                }
                else if (_fin != Finisher.None) { }                                   // finisher in progress
                else if (act == PunchType.Jab) { Throw(); return; }
                else if (act == PunchType.Cross) { Tug(); if (_heldSeg == null) return; }
                else if (act == PunchType.Uppercut) StartFinisher(Finisher.Powerbomb);
                else if (act == PunchType.Hook) StartFinisher(Finisher.Chokeslam);
                else if (act == PunchType.FrontKick) StartFinisher(Finisher.Piledriver);
                else if (act == PunchType.Roundhouse) StartFinisher(Finisher.GiantSwing);
                else { _slamT = 0f; }
            }
            if (_fin != Finisher.None)
            {
                if (TickFinisher(dt)) return;   // released
            }

            if (_slamT >= 0f)
            {
                _slamT += dt;
                ghost.holdLift = Mathf.Clamp01(_slamT / 0.4f);
                if (_slamT >= 0.48f) { Slam(); return; }
            }
            else if (_fin == Finisher.None) ghost.holdLift = Mathf.MoveTowards(ghost.holdLift, 0f, dt * 3f);

            if (_swingT >= 0f)
            {
                _swingT += dt / 0.32f;
                ghost.holdSwing = Mathf.Clamp01(_swingT);
                if (_swingT >= 1f) { _swingT = -1f; ghost.holdSwing = -1f; }
            }

            // carry: the held part is hauled to the hold point (whole-body weight, so it really comes along)
            Rigidbody rb = _heldSeg.rb;
            Vector3 hp = ghost.HoldPoint;
            float w = 2f * Mathf.PI * (weapon ? 5f : 3f), zeta = 0.85f;
            Vector3 acc = w * w * (hp - rb.position) + 2f * zeta * w * (ghost.Velocity - rb.linearVelocity);
            acc = Vector3.ClampMagnitude(acc, weapon ? 260f : _fin == Finisher.GiantSwing ? 240f : 110f);
            if (_held != null)
            {
                // Anti-explosion: move the whole held body as one (same acceleration on every segment) instead of
                // hauling a full-body mass by a single forearm, which yanked joints apart; and cap its speed.
                Vector3 a = acc * 0.75f - Physics.gravity * 0.9f;
                foreach (var hs in _held._segs)
                {
                    if (hs.detached || hs.rb == null || hs.rb.isKinematic) continue;
                    hs.rb.AddForce(a, ForceMode.Acceleration);
                    float cap = _fin == Finisher.GiantSwing ? 26f : HeldMaxSpeed;
                    if (hs.rb.linearVelocity.sqrMagnitude > cap * cap) hs.rb.linearVelocity = hs.rb.linearVelocity.normalized * cap;
                    if (hs.rb.angularVelocity.sqrMagnitude > HeldMaxSpin * HeldMaxSpin) hs.rb.angularVelocity = hs.rb.angularVelocity.normalized * HeldMaxSpin;
                }
            }
            else rb.AddForce(acc * _heldMass * 0.75f - Physics.gravity * _heldMass * 0.9f, ForceMode.Force);

            if (_held != null)
            {
                // holding a whole robot is tiring: no free stamina while you hold, and an empty tank loses the grip
                ghost.SpendStamina(HoldStaminaPerSecond * dt);
                if (ghost.stamina <= 0.5f && !_held.IsKO) { BreakFree(_held); return; }
                if (_fin == Finisher.None)
                {
                    _holdT += dt;
                    if (_holdT > 3.2f && !_held.IsKO) { BreakFree(_held); return; }
                }
            }
        }

        private void TryGrab()
        {
            if (ghost.Exhausted || ghost.stamina <= 0f) return;   // too spent to clinch
            if (Time.time < _grabCooldownUntil) return;            // can't chain grabs
            Segment best = null;
            MechRagdoll bestOwner = null;
            float bestD = 1.4f;
            foreach (var s in _segs)
            {
                if (s.tip == null || s.detached || !s.name.StartsWith("Forearm")) continue;
                Vector3 tip = s.tip.position;
                foreach (var col in Physics.OverlapSphere(tip, 1.4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = col.attachedRigidbody;
                    if (rb == null) continue;
                    var part = rb.GetComponent<MechRagdollPart>();
                    if (part == null || part.owner == null || part.owner == this || part.segment == null || part.segment.heldBy != null) continue;
                    if (team != 0 && !part.segment.detached && part.owner.team == team) continue;
                    if (!part.segment.detached && part.owner._grabbedBy != null) continue;
                    if (!part.segment.detached && Time.time < part.owner._grabImmuneUntil) continue;   // just escaped / was thrown
                    if (!part.segment.detached && part.owner.ghost != null && part.owner.ghost.Evading) continue;   // dodged the clinch
                    float d = Vector3.Distance(col.ClosestPoint(tip), tip);
                    if (d < bestD) { bestD = d; best = part.segment; bestOwner = part.owner; }
                }
            }
            if (best == null) return;
            _heldSeg = best;
            _holdT = 0f;
            _slamT = _swingT = -1f;
            if (best.detached)
            {
                _held = null;
                best.heldBy = this;
                _heldMass = bestOwner.LimbMass(best.limb);
                GrappleEvent?.Invoke(this, "PICKUP");
            }
            else
            {
                _held = bestOwner;
                _heldMass = bestOwner._totalMass;
                if (_restoreCollision.TryGetValue(bestOwner, out var pending) && pending != null) StopCoroutine(pending);
                _restoreCollision.Remove(bestOwner);
                IgnoreBodyCollision(bestOwner, true);
                bestOwner.GrabbedBy(this);
                ghost.SpendStamina(18f);
                GrappleEvent?.Invoke(this, bestOwner.ghost != null && bestOwner.ghost.Blocking ? "GUARD GRAB" : "GRAB");
            }
            BoxingSfx.Clank(0.7f);
        }

        private void DropHold()
        {
            if (_heldSeg != null) _heldSeg.heldBy = null;
            if (_held != null && _held._grabbedBy == this) _held.ReleasedBy(this);
            if (_held != null)
            {
                _held._grabImmuneUntil = Time.time + GrabImmunity;
                _grabCooldownUntil = Time.time + GrabCooldown;
            }
            if (_held != null && isActiveAndEnabled && !_restoreCollision.ContainsKey(_held))
                _restoreCollision[_held] = StartCoroutine(RestoreBodyCollisionLater(_held));
            _held = null;
            _heldSeg = null;
            _slamT = _swingT = -1f;
            _fin = Finisher.None;
            if (ghost != null)
            {
                ghost.holding = false; ghost.holdLift = 0f; ghost.holdSwing = -1f;
                ghost.holdExtraHeight = ghost.holdExtraReach = ghost.forcedSpin = 0f;
            }
        }

        private void Throw()
        {
            Vector3 v = ghost.transform.forward * 11f + Vector3.up * 5.5f + ghost.Velocity * 0.5f;
            if (_held != null)
            {
                var victim = _held;
                ghost.SpendStamina(10f);
                victim._impactUsed = false;
                foreach (var s in victim._segs) if (!s.rb.isKinematic) { s.rb.linearVelocity = v; s.rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 3f; }
                victim._thrownBy = this;
                victim._thrownT = Time.time;
                DropHold();
                victim.Knockdown(1.6f);
                GrappleEvent?.Invoke(this, "THROW");
            }
            else if (_heldSeg != null)
            {
                var limbOwner = _heldSeg.phys.GetComponent<MechRagdollPart>().owner;
                foreach (var s in limbOwner._segs) if (s.limb == _heldSeg.limb && s.detached) { s.rb.linearVelocity = v * 1.4f; s.rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f; }
                DropHold();
                GrappleEvent?.Invoke(this, "THROW");
            }
            BoxingSfx.Whoosh(1f);
        }

        private void Slam()
        {
            if (_held == null) { DropHold(); return; }
            var victim = _held;
            bool suplex = ghost != null && ghost.IsCrouching;
            Vector3 v = suplex
                ? (-ghost.transform.forward * 14f + Vector3.down * 28f) // Jiu-Jitsu / Wrestling back-arch suplex
                : (ghost.transform.forward * 5f + Vector3.down * 26f);
            foreach (var s in victim._segs) if (!s.rb.isKinematic) s.rb.linearVelocity = v;
            victim._thrownBy = this;
            victim._slammedT = Time.time;
            victim._slamMult = 1.8f;
            victim._impactUsed = false;
            DropHold();
            victim.Knockdown(2.2f);
            GrappleEvent?.Invoke(this, suplex ? "SUPLEX" : "SLAM");
        }

        /// <summary>Yank on the held part: limbs take heavy damage and tear off into your hands (bonus damage in Jiu-Jitsu floor lock).</summary>
        private void Tug()
        {
            var victim = _held;
            if (victim == null) return;
            Vector3 toMe = (Position - victim.Position);
            toMe.y = 0f;
            float groundLockBonus = (ghost != null && ghost.IsSeated ? 1.65f : ghost != null && ghost.IsCrouching ? 1.3f : 1f) * limbTearScale;
            Segment torn = victim.TugLimb(_heldSeg, toMe.normalized, this, groundLockBonus);
            BoxingSfx.Clank(1f);
            if (torn != null)
            {
                // the limb comes away in your hands - now it's a club
                DropHold();
                _heldSeg = torn;
                torn.heldBy = this;
                _heldMass = victim.LimbMass(torn.limb);
                GrappleEvent?.Invoke(this, "TORN");
            }
            else if (ghost != null && ghost.IsSeated)
            {
                GrappleEvent?.Invoke(this, "JOINT CRANK");
            }
        }

        internal Segment TugLimb(Segment seg, Vector3 dir, MechRagdoll attacker, float mult = 1f)
        {
            LimbId limb = seg.limb;
            if (limb == LimbId.None)
            {
                // torso / hips: shake them like a rag doll
                health -= 6f * mult;
                foreach (var s in _segs) if (!s.rb.isKinematic) s.rb.AddForce(dir * 30f + UnityEngine.Random.insideUnitSphere * 20f, ForceMode.Impulse);
                ShedArmor(seg, dir, 1);
                if (health <= 0f) KnockOut();
                Hit?.Invoke(this, attacker, seg.rb.position, 6f * mult, 0.4f, false);
                return null;
            }
            if (_lost.Contains(limb)) return null;
            _limbHealth[limb] -= 22f * mult;
            health -= 7f * mult;
            ShedArmor(seg, dir, 1);
            bool ripped = _limbHealth[limb] <= 0f;
            Segment root = null;
            if (ripped)
            {
                if (limb == LimbId.Head && health > maxHealth * 0.35f)
                {
                    _limbHealth[limb] = 16f;
                    ripped = false;
                    Knockdown(2.0f);
                }
                else
                {
                    RipLimb(limb, dir * 20f);
                    foreach (var s in _segs) if (s.limb == limb && (s.parent == null || s.parent.limb != limb)) root = s;
                }
            }
            if (health <= 0f && !IsKO) KnockOut();
            Hit?.Invoke(this, attacker, seg.rb.position, ripped ? 18f : 7f, ripped ? 1f : 0.5f, ripped);
            return root;
        }

        internal float LimbMass(LimbId limb)
        {
            float m = 0f;
            foreach (var s in _segs) if (s.limb == limb) m += s.rb.mass;
            return Mathf.Max(5f, m);
        }

        internal void GrabbedBy(MechRagdoll attacker)
        {
            _grabStreak = Time.time - _lastGrabbedT < 10f ? _grabStreak + 1 : 1;
            _lastGrabbedT = Time.time;
            _struggle = 0f;
            _mashSeen = ghost != null ? ghost.mashCount : 0;
            _grabbedBy = attacker;
            _downT = Mathf.Max(_downT, 999f);
            _recoverT = 0f;
            muscle = 0.2f;
            balance = 0f;
        }

        internal void ReleasedBy(MechRagdoll attacker)
        {
            if (_grabbedBy != attacker) return;
            _grabbedBy = null;
            if (!IsKO) _downT = 1.0f;
        }

        /// <summary>Swinging a torn-off limb into another mech.</summary>
        internal void WeaponStrike(Segment weapon, Collision c)
        {
            var otherPart = c.rigidbody != null ? c.rigidbody.GetComponent<MechRagdollPart>() : null;
            if (otherPart == null || otherPart.owner == null || otherPart.owner == this || otherPart.segment == null || otherPart.segment.detached || otherPart.segment.heldBy != null) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 4f) return;
            int serial = 1000000 + _swingSerial;
            if (_struckBy.TryGetValue(otherPart.owner, out int last) && last == serial) return;
            _struckBy[otherPart.owner] = serial;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : weapon.rb.position;
            Vector3 dir = otherPart.owner.Position - Position;
            dir.y = 0f;
            otherPart.owner.ReceiveStrike(this, otherPart.segment, point, dir.normalized + Vector3.up * 0.3f, Mathf.Clamp(speed * 2.2f, 8f, 28f), Mathf.Clamp01(speed / 14f));
        }

        // ------------------------------------------------------------------ online (host streams poses, clients puppet)
        /// <summary>Online client: every segment is kinematic and follows the poses the host sends.</summary>
        public bool netPuppet { get; private set; }
        private Vector3[] _netFromP, _netToP;
        private Quaternion[] _netFromR, _netToR;
        private float _netT, _netSpan = 0.05f, _netLastRecv;
        private const float NetPosScale = 200f;   // 5 mm steps, +-163 m

        public void EnterNetPuppet()
        {
            netPuppet = true;
            if (_heldSeg != null) DropHold();
            foreach (var s in _segs)
            {
                if (s.rb == null) continue;
                s.rb.isKinematic = true;
                s.rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        /// <summary>Host: every segment's position and rotation (14 bytes each).</summary>
        public void WriteNetPose(System.IO.BinaryWriter w)
        {
            w.Write((byte)_segs.Count);
            foreach (var s in _segs)
            {
                Vector3 p = s.phys != null ? s.phys.position : Vector3.zero;
                Quaternion q = s.phys != null ? s.phys.rotation : Quaternion.identity;
                w.Write(PackPos(p.x)); w.Write(PackPos(p.y)); w.Write(PackPos(p.z));
                if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
                w.Write(PackUnit(q.x)); w.Write(PackUnit(q.y)); w.Write(PackUnit(q.z)); w.Write(PackUnit(q.w));
            }
        }

        private static short PackPos(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * NetPosScale), short.MinValue, short.MaxValue);
        private static short PackUnit(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32767, 32767);

        /// <summary>Client: reads a pose written by <see cref="WriteNetPose"/> and starts blending to it.</summary>
        public void ReadNetPose(System.IO.BinaryReader r)
        {
            int n = r.ReadByte();
            bool match = n == _segs.Count;
            if (match && (_netToP == null || _netToP.Length != n))
            {
                _netFromP = new Vector3[n]; _netToP = new Vector3[n];
                _netFromR = new Quaternion[n]; _netToR = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    var t = _segs[i].phys;
                    _netToP[i] = t != null ? t.position : Vector3.zero;
                    _netToR[i] = t != null ? t.rotation : Quaternion.identity;
                }
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(r.ReadInt16(), r.ReadInt16(), r.ReadInt16()) / NetPosScale;
                var q = new Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f);
                if (!match) continue;
                var t = _segs[i].phys;
                _netFromP[i] = t != null ? t.position : p;
                _netFromR[i] = t != null ? t.rotation : q;
                _netToP[i] = p;
                float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                _netToR[i] = m > 0.0001f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
            }
            if (!match) return;
            float now = Time.unscaledTime;
            _netSpan = Mathf.Clamp(now - _netLastRecv, 0.02f, 0.15f);
            _netLastRecv = now;
            _netT = 0f;
        }

        private void TickPuppet(float dt)
        {
            if (_netToP == null || _netToP.Length != _segs.Count) return;
            _netT += dt;
            float k = Mathf.Clamp01(_netT / _netSpan);
            for (int i = 0; i < _segs.Count; i++)
            {
                var s = _segs[i];
                if (s.phys == null) continue;
                Vector3 p = Vector3.LerpUnclamped(_netFromP[i], _netToP[i], k);
                Quaternion q = Quaternion.Slerp(_netFromR[i], _netToR[i], k);
                if (s.rb != null) { s.rb.position = p; s.rb.rotation = q; }
                s.phys.SetPositionAndRotation(p, q);
            }
        }

        private static readonly LimbId[] NetLimbs = { LimbId.ArmL, LimbId.ArmR, LimbId.LegL, LimbId.LegR, LimbId.Head };

        /// <summary>Host: hull, poise, KO and limb health.</summary>
        public void WriteNetStatus(System.IO.BinaryWriter w)
        {
            w.Write(health);
            w.Write(maxHealth);
            w.Write((byte)Mathf.RoundToInt(Poise01 * 255f));
            w.Write(IsKO);
            byte lost = 0;
            for (int i = 0; i < NetLimbs.Length; i++)
            {
                if (_lost.Contains(NetLimbs[i])) lost |= (byte)(1 << i);
                w.Write((byte)Mathf.RoundToInt(GetLimbHealth01(NetLimbs[i]) * 255f));
            }
            w.Write(lost);
        }

        /// <summary>Client: applies what <see cref="WriteNetStatus"/> wrote.</summary>
        public void ReadNetStatus(System.IO.BinaryReader r)
        {
            health = r.ReadSingle();
            maxHealth = r.ReadSingle();
            _poise = r.ReadByte() / 255f * poiseMax;
            bool ko = r.ReadBoolean();
            var lh = new float[NetLimbs.Length];
            for (int i = 0; i < NetLimbs.Length; i++) lh[i] = r.ReadByte() / 255f;
            byte lost = r.ReadByte();
            bool limbsChanged = false;
            for (int i = 0; i < NetLimbs.Length; i++)
            {
                var id = NetLimbs[i];
                bool gone = (lost & (1 << i)) != 0;
                if (gone && _lost.Add(id)) limbsChanged = true;
                _limbHealth[id] = lh[i] * LimbMax(id);
            }
            if (ko && !IsKO)
            {
                IsKO = true;
                if (ghost != null) ghost.suppressed = true;
            }
            else if (!ko && IsKO)
            {
                IsKO = false;
                if (ghost != null) ghost.suppressed = false;
            }
            if (limbsChanged && ghost != null)
                ghost.SetLimbState(!_lost.Contains(LimbId.ArmL), !_lost.Contains(LimbId.ArmR), !_lost.Contains(LimbId.LegL), !_lost.Contains(LimbId.LegR));
        }

        /// <summary>Client: shows a hit the host reported (sparks, and the Hit event drives sounds / camera shake / popups).</summary>
        public void NetShowHit(MechRagdoll attacker, Vector3 point, float damage, float power, bool ripped)
        {
            EmitHitSparks(point, Mathf.Clamp01(damage / 20f) + (ripped ? 0.8f : 0f));
            Hit?.Invoke(this, attacker, point, damage, power, ripped);
        }

    }
}
