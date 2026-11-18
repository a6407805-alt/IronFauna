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
        public float poiseMax = 28f;
        [Tooltip("Chaos: how far hits shove mechs around")]
        public float knockbackScale = 1.8f;
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
        public float Poise01 => Mathf.Clamp01(_poise / poiseMax);
        public float GetLimbHealth01(LimbId id) => _lost.Contains(id) ? 0f : (_limbHealth.TryGetValue(id, out float v) ? Mathf.Clamp01(v / (id == LimbId.Head ? 70f : 55f)) : 1f);

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
        private float _totalMass;
        private float _poise;
        private float _downT;
        private float _recoverT;
        private float _lastHitT;
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

            foreach (LimbId id in Enum.GetValues(typeof(LimbId))) if (id != LimbId.None) _limbHealth[id] = id == LimbId.Head ? 70f : 55f;
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
            poiseMax = Mathf.Round(30f * m.poiseMult * Mathf.Lerp(1f, 1.4f, bonusHpMult - 1f));
            _poise = poiseMax;
            knockbackScale = 1.8f * m.knockbackDealtMult;
            knockbackTakenScale = m.knockbackTakenMult;
            limbTearScale = m.limbTearMult;

            float limbScale = m.limbHealthMult * Mathf.Lerp(1f, 1.3f, bonusHpMult - 1f);
            foreach (LimbId id in Enum.GetValues(typeof(LimbId)))
            {
                if (id != LimbId.None) _limbHealth[id] = (id == LimbId.Head ? 70f : 55f) * limbScale;
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
        public void ReceiveStrike(MechRagdoll attacker, Segment seg, Vector3 point, Vector3 dir, float damage, float power)
        {
            if (seg.detached) return;
            damage = Mathf.Clamp(damage, 0f, 40f);
            if (damage < 1.5f) return;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -transform.forward;

            bool headShot = seg.limb == LimbId.Head;
            float mult = headShot ? 1.35f : seg == _torso || seg == _pelvis ? 1f : 0.8f;
            health -= damage * mult;
            _lastHitT = Time.time;
            _poise -= damage * (headShot ? 1.6f : 1f);

            // the blow really moves the body: a shove at the contact point on top of the collision itself
            float shove = (attacker != null ? attacker.knockbackScale : knockbackScale) * knockbackTakenScale;
            seg.rb.AddForceAtPosition(dir * damage * 16f * shove, point, ForceMode.Impulse);
            ghost.Interrupt(0.12f + damage * 0.012f * knockbackTakenScale);
            ghost.AddKnockback(dir * Mathf.Lerp(1.5f, 8f, damage / 25f) * shove);
            muscle = Mathf.Min(muscle, Mathf.Lerp(1f, 0.35f, damage / 25f));   // a hard hit goes slack for a beat

            bool ripped = false;
            float tearMult = attacker != null ? attacker.limbTearScale : 1f;
            LimbId targetLimb = seg.limb;
            if (targetLimb == LimbId.None && damage >= 9f)
            {
                LimbId[] limbs = { LimbId.ArmL, LimbId.ArmR, LimbId.LegL, LimbId.LegR };
                LimbId pick = limbs[UnityEngine.Random.Range(0, limbs.Length)];
                if (!_lost.Contains(pick)) targetLimb = pick;
            }
            if (targetLimb != LimbId.None && !_lost.Contains(targetLimb))
            {
                float limbDmg = damage * (seg.limb != LimbId.None ? (headShot ? 0.9f : 1.35f) : 0.65f) * tearMult;
                _limbHealth[targetLimb] -= limbDmg;
                float lh = _limbHealth[targetLimb];
                // armor cracks off as the limb weakens
                if (lh < 38f) ShedArmor(seg, dir, 1);
                if (lh < 18f) ShedArmor(seg, dir, 2);
                if (lh <= 0f)
                {
                    // Decapitation only finishes a fighter who is already below 35% hull integrity; otherwise it sheds head armor & knocks down!
                    if (targetLimb == LimbId.Head && health > maxHealth * 0.35f)
                    {
                        _limbHealth[targetLimb] = 16f;
                        ShedArmor(seg, dir, 2);
                        Knockdown(2.0f);
                    }
                    else
                    {
                        RipLimb(targetLimb, dir * damage);
                        ripped = true;
                    }
                }
            }
            else if (health < maxHealth * 0.6f) ShedArmor(seg, dir, 1);

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
                    if (_propStrikeSerial != serial)
                    {
                        var tree = col.GetComponentInParent<FallingTree>();
                        if (tree != null)
                        {
                            _propStrikeSerial = serial;
                            tree.ApplyStrike(tipPos, ghost.transform.forward * (14f * knockbackScale), ghost.PunchDamage * 18f * knockbackScale);
                        }
                        var prop = col.GetComponentInParent<BreakableProp>();
                        if (prop != null)
                        {
                            _propStrikeSerial = serial;
                            prop.ApplyStrike(tipPos, ghost.transform.forward * (14f * knockbackScale), ghost.PunchDamage * 18f * knockbackScale);
                        }
                    }

                    var rb = col.attachedRigidbody;
                    var otherPart = rb != null ? rb.GetComponent<MechRagdollPart>() : col.GetComponentInParent<MechRagdollPart>();
                    if (otherPart == null || otherPart.owner == null || otherPart.owner == this || otherPart.segment == null || otherPart.segment.detached) continue;
                    if (team != 0 && otherPart.owner.team == team) continue;
                    if (_struckBy.TryGetValue(otherPart.owner, out int last) && last == serial) continue;
                    _struckBy[otherPart.owner] = serial;
                    Vector3 dir = otherPart.owner.Position - Position;
                    dir.y = 0f;
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : ghost.transform.forward;
                    otherPart.owner.ReceiveStrike(this, otherPart.segment, tipPos, dir + Vector3.up * 0.25f, ghost.PunchDamage, ghost.PunchPower);
                    if (ghost.StrikeSweeps && !otherPart.owner.IsDown)
                    {
                        otherPart.owner.Knockdown(1.25f);
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
                prop.ApplyStrike(hitPt, transform.forward * (12f * knockbackScale), ghost.PunchDamage * 18f * knockbackScale);
            }
            var tree = c.collider != null ? c.collider.GetComponentInParent<FallingTree>() : null;
            if (tree != null)
            {
                Vector3 hitPt = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
                tree.ApplyStrike(hitPt, transform.forward * (12f * knockbackScale), ghost.PunchDamage * 16f * knockbackScale);
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
            float damage = ghost.PunchDamage * Mathf.Clamp(speed / 8f, 0.7f, 1.4f);
            otherPart.owner.ReceiveStrike(this, otherPart.segment, point, dir + Vector3.up * 0.25f, damage, ghost.PunchPower);
            if (ghost.StrikeSweeps && !otherPart.owner.IsDown)
            {
                otherPart.owner.Knockdown(1.25f);
            }
        }

        // ------------------------------------------------------------------ falls, throws, bowling
        private float _impactT;
        private MechRagdoll _thrownBy;
        private float _thrownT = -10f, _slammedT = -10f;

        /// <summary>A body that is down (thrown, slammed, knocked flying) takes damage from hard landings, and
        /// bowls over anyone it crashes into.</summary>
        private void ImpactDamage(Segment mine, Collision c)
        {
            if (!IsDown || mine.detached) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 8.5f || Time.time - _impactT < 0.3f) return;
            _impactT = Time.time;
            bool slammed = Time.time - _slammedT < 2f;
            bool thrown = Time.time - _thrownT < 3f;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
            float damage = (speed - 8.5f) * 2.4f * (slammed ? 1.8f : 1f);
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
            damage = Mathf.Clamp(damage, 0f, 40f);
            if (damage < 2f || IsKO) return;
            health -= damage;
            _lastHitT = Time.time;
            bool ripped = false;
            if (seg.limb != LimbId.None && !_lost.Contains(seg.limb))
            {
                _limbHealth[seg.limb] -= damage * 0.8f;
                if (_limbHealth[seg.limb] < 30f) ShedArmor(seg, Vector3.up, 1);
                if (_limbHealth[seg.limb] <= 0f) { RipLimb(seg.limb, Vector3.up * damage); ripped = true; }
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
                else if (act == PunchType.Jab) { Throw(); return; }
                else if (act == PunchType.Cross || act == PunchType.Hook) { Tug(); if (_heldSeg == null) return; }
                else { _slamT = 0f; }
            }

            if (_slamT >= 0f)
            {
                _slamT += dt;
                ghost.holdLift = Mathf.Clamp01(_slamT / 0.4f);
                if (_slamT >= 0.48f) { Slam(); return; }
            }
            else ghost.holdLift = Mathf.MoveTowards(ghost.holdLift, 0f, dt * 3f);

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
            acc = Vector3.ClampMagnitude(acc, weapon ? 260f : 110f);
            rb.AddForce(acc * _heldMass * 0.75f - Physics.gravity * _heldMass * 0.9f, ForceMode.Force);

            if (_held != null)
            {
                _holdT += dt;
                if (_holdT > 4f && !_held.IsKO) { var h = _held; DropHold(); h.Knockdown(0.6f); GrappleEvent?.Invoke(this, "BROKE FREE"); }
            }
        }

        private void TryGrab()
        {
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
                bestOwner.GrabbedBy(this);
                GrappleEvent?.Invoke(this, "GRAB");
            }
            BoxingSfx.Clank(0.7f);
        }

        private void DropHold()
        {
            if (_heldSeg != null) _heldSeg.heldBy = null;
            if (_held != null && _held._grabbedBy == this) _held.ReleasedBy(this);
            _held = null;
            _heldSeg = null;
            _slamT = _swingT = -1f;
            if (ghost != null) { ghost.holding = false; ghost.holdLift = 0f; ghost.holdSwing = -1f; }
        }

        private void Throw()
        {
            Vector3 v = ghost.transform.forward * 16f + Vector3.up * 7.5f + ghost.Velocity;
            if (_held != null)
            {
                var victim = _held;
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
    }
}
