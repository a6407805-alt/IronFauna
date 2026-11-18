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
        public float poiseMax = 34f;

        [Header("Strength")]
        public float muscle = 1f;             // joint motor multiplier (0 = limp)
        public float balance = 1f;            // pelvis controller multiplier (0 = no help standing)

        public bool IsKO { get; private set; }
        public bool IsDown => _downT > 0f || IsKO;
        public Rigidbody Pelvis => _pelvis != null ? _pelvis.rb : null;
        public Vector3 Position => _pelvis != null ? _pelvis.rb.position : transform.position;
        public float Poise01 => Mathf.Clamp01(_poise / poiseMax);

        /// <summary>victim, attacker, point, damage, power 0..1, limb ripped</summary>
        public event Action<MechRagdoll, MechRagdoll, Vector3, float, float, bool> Hit;
        public event Action<MechRagdoll> KnockedOut;
        public event Action<MechRagdoll, Vector3> ArmorShed;

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
            public Quaternion startLocal = Quaternion.identity;
            public float spring, damper;
            public readonly List<Transform> armor = new List<Transform>();
            public bool detached;
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
            j.autoConfigureConnectedAnchor = true;
            Vector3 anchor = Vector3.zero;
            if (s == _torso) anchor = s.phys.InverseTransformPoint(mech.visualRoot.TransformPoint(new Vector3(0f, mech.HipHeight + 0.30f, 0f)));
            else if (s.name.StartsWith("Foot")) anchor = new Vector3(0f, MechClamshellBuilder.AnkleHeight, 0f);
            j.anchor = anchor;
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
            ghost.suppressed = IsDown || _recoverT > 0f;

            bool standing = !IsDown;
            if (standing && !_legsKinematic)
            {
                // getting up: put the ghost where the body lies, then blend the legs onto the walk cycle
                Vector3 g = ghost.transform.position;
                Vector3 pp = _pelvis.rb.position;
                ghost.transform.position = new Vector3(pp.x, g.y, pp.z);
                _follow = 0f;
            }
            SetLowerBodyKinematic(standing);
            _follow = standing ? Mathf.MoveTowards(_follow, 1f, dt / 0.6f) : 0f;

            // motors on the physical joints (upper body; the whole body while limp)
            if (Mathf.Abs(_appliedMuscle - muscle) > 0.01f)
            {
                foreach (var s in _segs) ApplyDrive(s, _lost.Contains(s.limb) ? 0.02f : muscle);
                _appliedMuscle = muscle;
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
                PdRotate(_torso.rb, _torso.target.rotation, 5f * balance, 0.9f);
            }

            // the ghost never wanders away from the body (falls, being thrown)
            if (IsDown)
            {
                Vector3 err = _pelvis.rb.position - _pelvis.target.position;
                err.y = 0f;
                ghost.transform.position += err;
            }
        }

        /// <summary>Floor (not a mech) under this point, within a few metres.</summary>
        private bool SupportUnder(Vector3 p)
        {
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 4.5f, ~0, QueryTriggerInteraction.Ignore))
            {
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
        public void ReceiveStrike(MechRagdoll attacker, Segment seg, Vector3 point, Vector3 velocity, float strikerMass, float power)
        {
            if (IsKO && seg.detached) return;
            float speed = velocity.magnitude;
            float damage = Mathf.Clamp(speed * strikerMass * 0.075f * Mathf.Lerp(0.7f, 1.5f, power), 0f, 30f);
            if (damage < 1.5f) return;
            Vector3 dir = speed > 0.01f ? velocity / speed : -transform.forward;

            bool headShot = seg.limb == LimbId.Head;
            float mult = headShot ? 1.35f : seg == _torso || seg == _pelvis ? 1f : 0.8f;
            health -= damage * mult;
            _lastHitT = Time.time;
            _poise -= damage * (headShot ? 1.6f : 1f);

            // the blow really moves the body: a shove at the contact point on top of the collision itself
            seg.rb.AddForceAtPosition(dir * damage * 9f, point, ForceMode.Impulse);
            ghost.Interrupt(0.12f + damage * 0.012f);
            ghost.AddKnockback(dir * Mathf.Lerp(0.6f, 4f, damage / 25f));
            muscle = Mathf.Min(muscle, Mathf.Lerp(1f, 0.35f, damage / 25f));   // a hard hit goes slack for a beat

            bool ripped = false;
            if (seg.limb != LimbId.None && !_lost.Contains(seg.limb))
            {
                _limbHealth[seg.limb] -= damage * (headShot ? 0.9f : 1.2f);
                float lh = _limbHealth[seg.limb];
                // armor cracks off as the limb weakens
                if (lh < 38f) ShedArmor(seg, dir, 1);
                if (lh < 18f) ShedArmor(seg, dir, 2);
                if (lh <= 0f) { RipLimb(seg.limb, dir * damage); ripped = true; }
            }
            else if (health < maxHealth * 0.6f) ShedArmor(seg, dir, 1);

            if (_poise <= 0f && !IsKO) Knockdown(1.4f + damage * 0.03f);
            if (health <= 0f && !IsKO) KnockOut();
            Hit?.Invoke(this, attacker, point, damage, power, ripped);
        }

        public void Knockdown(float seconds)
        {
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
                ApplyDrive(s, 0.02f);
                bool root = s.parent != null && s.parent.limb != limb;
                if (root && s.joint != null)
                {
                    Destroy(s.joint);
                    s.joint = null;
                    s.detached = true;
                    s.rb.AddForce(impulse * 1.2f + Vector3.up * 25f, ForceMode.Impulse);
                }
            }
            foreach (var s in _segs) if (s.limb == limb) s.detached = true;
            _appliedMuscle = -1f;
            if (limb == LimbId.Head && !IsKO) KnockOut();
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

        /// <summary>Called by a part's collision: one damaging contact per punch per victim.</summary>
        internal void TryStrike(Segment mine, Collision c)
        {
            if (IsDown || ghost == null || mine.detached) return;
            var otherPart = c.rigidbody != null ? c.rigidbody.GetComponent<MechRagdollPart>() : null;
            if (otherPart == null || otherPart.owner == this) return;
            bool arm = mine.limb == LimbId.ArmL || mine.limb == LimbId.ArmR;
            bool leg = mine.limb == LimbId.LegL || mine.limb == LimbId.LegR;
            bool active = mine.striker && ghost.StrikeActive && ghost.StrikeLead == mine.leadHand;
            bool armStrike = arm && active && !ghost.StrikeIsKick;
            bool legStrike = leg && active && ghost.StrikeIsKick;
            if (!armStrike && !legStrike) return;
            int serial = ghost.PunchSerial;
            if (_struckBy.TryGetValue(otherPart.owner, out int last) && last == serial) return;
            _struckBy[otherPart.owner] = serial;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : mine.rb.position;
            Vector3 dir = otherPart.owner.Position - Position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            float speed = Mathf.Max(c.relativeVelocity.magnitude, 3f);
            otherPart.owner.ReceiveStrike(this, otherPart.segment, point, (dir + Vector3.up * 0.15f).normalized * speed, mine.rb.mass + 8f, ghost.PunchPower);
        }
    }
}
