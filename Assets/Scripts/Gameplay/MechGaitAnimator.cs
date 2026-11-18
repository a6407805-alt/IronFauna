using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;
using IronFauna.Core;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Procedural walk cycle with planted feet for a fighting mech (drives the hips and legs of each brawl
    /// fighter's animation ghost; MechBoxer poses the upper body on top).
    ///
    /// Weight comes from what a heavy walking machine does, not from slowing a light one down:
    ///  - feet are planted in the world and never slide; each leg bends with two-bone IK to reach its foot;
    ///  - a gait clock alternates the legs in a fixed rhythm with a
    ///    long stance and a short, lifted swing; the foot rolls heel-up at lift-off and toe-up before it lands;
    ///  - each footfall drives the body down into a damped spring, so the frame sinks and rebounds on every stomp;
    ///  - the hips sway over the standing leg, the torso counter-twists, the arms counter-swing;
    ///  - standing still, the feet settle with a few shuffle steps; turning in place steps the feet around.
    /// Speed comes from how the transform moves. Runs in LateUpdate before MechBoxer and the brawl camera.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(MechClamshellBuilder))]
    public class MechGaitAnimator : MonoBehaviour
    {
        [System.Serializable]
        public struct GaitProfile
        {
            public float cycleTime;      // seconds for a full cycle (every leg steps once)
            public float duty;           // share of the cycle each foot is on the ground
            public float liftHeight;     // m, top of the swing arc
            public float stompImpulse;   // m/s pushed into the body spring on each footfall
            public float bodyFrequency;  // Hz of the body spring (lower = heavier, slower rebound)
            public float stompShake;     // footfall strength reported to listeners
        }

        [Header("Gait by leg weight class")]
        public GaitProfile light = new GaitProfile { cycleTime = 0.9f, duty = 0.58f, liftHeight = 0.34f, stompImpulse = 0.28f, bodyFrequency = 3.4f, stompShake = 0.10f };
        public GaitProfile medium = new GaitProfile { cycleTime = 1.1f, duty = 0.62f, liftHeight = 0.38f, stompImpulse = 0.42f, bodyFrequency = 2.8f, stompShake = 0.17f };
        public GaitProfile heavy = new GaitProfile { cycleTime = 1.35f, duty = 0.66f, liftHeight = 0.42f, stompImpulse = 0.62f, bodyFrequency = 2.2f, stompShake = 0.26f };

        [Header("Body")]
        [Tooltip("How far the hips shift over the standing foot (share of the hip width).")]
        public float hipSway = 0.3f;
        public float swayRollDegrees = 3f;
        public float torsoTwistDegrees = 4f;
        public float armSwingDegrees = 9f;
        [Tooltip("Extra crouch at walking speed (m), so the legs have room for a long stride.")]
        public float walkCrouch = 0.16f;
        [Tooltip("Deepest the hips may drop to keep a planted foot in reach (m).")]
        public float maxReachDrop = 0.5f;

        [Header("Settling")]
        public float settleDistance = 0.18f;
        public float settleYaw = 22f;

        [Header("Dust")]
        public bool footfallDust = true;
        public float dustScale = 0.7f;

        private class LegState
        {
            public MechClamshellBuilder.LegRig rig;
            public float offset;
            public Vector3 restRootLocal;   // foot sole at rest, in the mech's root space
            public Vector3 plant;           // world sole position while planted
            public float plantYaw;
            public bool swinging;
            public Vector3 liftPos;
            public float liftYaw;
            public Vector3 footPos;         // world sole position this frame
            public Quaternion footRot;
            public float restBendSign;
            public Vector3 restThighDir, restCalfDir, restHinge;
        }

        private MechClamshellBuilder _builder;
        private readonly List<LegState> _legs = new List<LegState>();
        private int _version = -1;
        private float _phase;
        private float _bodyY, _bodyVy, _swayX;
        private float _reachDrop;
        private float _airBlend;
        private float _glideBlend;
        private Vector3 _lastPos;
        private bool _wasGrounded = true;
        private bool _wasHanging;
        private Vector3 _velocity;
        private int _footfallsPerCycle = 2;
        private bool _biped = true;
        private ParticleSystem _dust;

        /// <summary>Called on every footfall with the world position and impact strength (0..1).</summary>
        public event System.Action<Vector3, float> Footfall;

        private void Awake()
        {
            _builder = GetComponent<MechClamshellBuilder>();
            _lastPos = transform.position;
        }

        private GaitProfile Profile => _builder.legWeight == MechWeightClass.Light ? light : _builder.legWeight == MechWeightClass.Heavy ? heavy : medium;

        private void Init()
        {
            _version = _builder.BuildVersion;
            _legs.Clear();
            if (_builder.visualRoot == null) return;
            _builder.visualRoot.localPosition = Vector3.zero;
            _bodyY = _bodyVy = _swayX = _reachDrop = 0f;

            _biped = true;
            _footfallsPerCycle = 2;
            foreach (var rig in _builder.legRigs)
            {
                if (rig.root == null || rig.foot == null) continue;
                var leg = new LegState { rig = rig };
                bool left = rig.root.name.EndsWith("_L");
                leg.offset = left ? 0f : 0.5f;

                leg.restRootLocal = transform.InverseTransformPoint(rig.foot.position);
                leg.plant = leg.footPos = rig.foot.position;
                leg.plantYaw = leg.liftYaw = transform.eulerAngles.y;
                leg.footRot = Quaternion.Euler(0f, leg.plantYaw, 0f);

                // Rest frames for the IK (leg root space).
                Vector3 dirHA = (rig.ankle - rig.hip).normalized;
                Vector3 bend = Vector3.ProjectOnPlane(rig.knee - rig.hip, dirHA).normalized;
                leg.restHinge = Vector3.Cross(bend, dirHA).normalized;
                leg.restThighDir = (rig.knee - rig.hip).normalized;
                leg.restCalfDir = (rig.ankle - rig.knee).normalized;
                _legs.Add(leg);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _builder == null) return;
            if (_version != _builder.BuildVersion || _legs.Count == 0)
            {
                Init();
                if (_legs.Count == 0) return;
            }
            GaitProfile g = Profile;

            // ---------------- motion state
            bool grounded = true, boosting = false;
            float walkSpeed = 3f;
            Vector3 raw = (transform.position - _lastPos) / dt;
            _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-10f * dt));
            Vector3 vel = _velocity;
            _lastPos = transform.position;
            vel.y = 0f;
            float speed = vel.magnitude;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.5f, walkSpeed));

            bool gliding = boosting && grounded && speed > walkSpeed * 1.25f;
            _glideBlend = Mathf.MoveTowards(_glideBlend, gliding ? 1f : 0f, dt * 4f);
            bool landedNow = grounded && !_wasGrounded && _airBlend > 0.25f;
            _wasGrounded = grounded;
            if (landedNow) _airBlend = 0f;
            else _airBlend = Mathf.MoveTowards(_airBlend, grounded ? 0f : 1f, dt * 3f);
            bool hanging = _airBlend > 0f || _glideBlend > 0f;

            if (_wasHanging && !hanging)
            {
                // Touch down: every foot plants where it is now; the gait steps them back under the body.
                foreach (var leg in _legs)
                {
                    leg.plant = GroundPoint(leg.footPos);
                    leg.plantYaw = leg.footRot.eulerAngles.y;
                    leg.swinging = false;
                }
                if (landedNow) Stomp(transform.position, 1f, g);
            }
            _wasHanging = hanging;

            // ---------------- gait clock: walking, or settling feet that are out of place
            bool walking = speed > 0.25f && _airBlend < 0.5f && _glideBlend < 0.5f;
            bool settling = false;
            if (!walking && _airBlend < 0.5f && _glideBlend < 0.5f)
            {
                foreach (var leg in _legs)
                {
                    if (leg.swinging) { settling = true; break; }
                    Vector3 n = NeutralPoint(leg);
                    Vector3 d = leg.plant - n;
                    d.y = 0f;
                    if (d.magnitude > settleDistance || Mathf.Abs(Mathf.DeltaAngle(leg.plantYaw, transform.eulerAngles.y)) > settleYaw)
                    {
                        settling = true;
                        break;
                    }
                }
            }
            float cycle = g.cycleTime * (walking ? Mathf.Lerp(1.15f, 0.9f, speed01) : 0.85f);
            if (walking || settling) _phase = Mathf.Repeat(_phase + dt / cycle, 1f);

            // ---------------- feet
            float worstReach = 0f;
            foreach (var leg in _legs)
            {
                if (_airBlend > 0f || _glideBlend > 0f)
                {
                    HangFoot(leg, g, dt);
                }
                else
                {
                    float p = Mathf.Repeat(_phase + leg.offset, 1f);
                    bool inSwing = (walking || settling) && p >= g.duty;
                    if (inSwing && !leg.swinging)
                    {
                        leg.swinging = true;
                        leg.liftPos = leg.plant;
                        leg.liftYaw = leg.plantYaw;
                    }
                    if (leg.swinging)
                    {
                        float t = inSwing ? Mathf.Clamp01((p - g.duty) / (1f - g.duty)) : 1f;
                        float remaining = (1f - t) * (1f - g.duty) * cycle;
                        Vector3 target = LandingPoint(leg, vel * (remaining + g.duty * cycle * 0.5f));
                        float targetYaw = transform.eulerAngles.y;
                        float e = t * t * (3f - 2f * t);
                        Vector3 flat = Vector3.Lerp(leg.liftPos, target, e);
                        float stride = Vector3.Distance(new Vector3(leg.liftPos.x, 0f, leg.liftPos.z), new Vector3(target.x, 0f, target.z));
                        float lift = g.liftHeight * Mathf.Clamp01(stride / 0.7f + 0.3f);
                        leg.footPos = flat + Vector3.up * (Mathf.Sin(Mathf.PI * t) * lift);
                        float yaw = Mathf.LerpAngle(leg.liftYaw, targetYaw, e);
                        // Heel lifts first, toe comes up before the strike.
                        float pitch = 22f * Mathf.Sin(Mathf.PI * t) * (1f - 2f * t);
                        leg.footRot = Quaternion.Euler(pitch, yaw, 0f);

                        if (!inSwing || t >= 1f)
                        {
                            leg.swinging = false;
                            leg.plant = target;
                            leg.plantYaw = targetYaw;
                            leg.footPos = leg.plant;
                            leg.footRot = Quaternion.Euler(0f, leg.plantYaw, 0f);
                            Stomp(leg.plant, Mathf.Clamp01(0.35f + 0.65f * speed01), g);
                        }
                    }
                    else
                    {
                        leg.footPos = leg.plant;
                        leg.footRot = Quaternion.Euler(0f, leg.plantYaw, 0f);
                    }
                }
                worstReach = Mathf.Max(worstReach, ReachDeficit(leg));
            }

            // ---------------- body: spring that sinks on each stomp, crouch for reach, sway over the stance foot
            _reachDrop = Mathf.MoveTowards(_reachDrop, Mathf.Min(maxReachDrop, _reachDrop + worstReach), dt * 3f);
            if (worstReach <= 0.001f) _reachDrop = Mathf.MoveTowards(_reachDrop, 0f, dt * 0.6f);
            float k = Mathf.Pow(2f * Mathf.PI * g.bodyFrequency, 2f);
            float c = 2f * 0.45f * Mathf.Sqrt(k);
            float rhythm = walking ? -0.025f * Mathf.Cos(2f * Mathf.PI * _footfallsPerCycle * _phase) * speed01 : 0f;
            float bodyTarget = -walkCrouch * speed01 - 0.18f * _glideBlend - _reachDrop + rhythm;
            _bodyVy += (k * (bodyTarget - _bodyY) - c * _bodyVy) * dt;
            _bodyY += _bodyVy * dt;

            float swayTarget = 0f;
            if (_biped && (walking || settling))
            {
                float sum = 0f;
                int count = 0;
                foreach (var leg in _legs)
                {
                    if (leg.swinging) continue;
                    sum += transform.InverseTransformPoint(leg.plant).x;
                    count++;
                }
                if (count > 0) swayTarget = sum / count * hipSway;
            }
            _swayX = Mathf.Lerp(_swayX, swayTarget, 1f - Mathf.Exp(-5f * dt));

            Transform visual = _builder.visualRoot;
            visual.localPosition = new Vector3(_swayX, _bodyY, 0f);

            // ---------------- legs reach their feet
            foreach (var leg in _legs) SolveLeg(leg);

            // ---------------- upper body: roll over the stance leg, counter-twist, arm swing
            float swing = Mathf.Sin(2f * Mathf.PI * _phase) * speed01 * (walking ? 1f : 0f);
            if (_builder.bodyRoot != null)
            {
                float hipHalf = 0.5f;
                if (_legs.Count > 1) hipHalf = Mathf.Max(0.2f, Mathf.Abs(_legs[0].restRootLocal.x));
                float roll = -(_swayX / hipHalf) * swayRollDegrees;
                float twist = swing * torsoTwistDegrees;
                float pitch = 6f * _glideBlend;
                _builder.bodyRoot.localRotation = Quaternion.Euler(pitch, twist, roll) * _builder.bodyRoot.localRotation;
            }
            float arm = swing * armSwingDegrees;
            if (_builder.leftArmRoot != null) _builder.leftArmRoot.localRotation = Quaternion.Euler(arm, 0f, 0f) * _builder.leftArmRoot.localRotation;
            if (_builder.rightArmRoot != null) _builder.rightArmRoot.localRotation = Quaternion.Euler(-arm, 0f, 0f) * _builder.rightArmRoot.localRotation;
        }

        // ------------------------------------------------------------------------------- feet helpers
        private Vector3 NeutralPoint(LegState leg)
        {
            return GroundPoint(transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z)));
        }

        private Vector3 LandingPoint(LegState leg, Vector3 lead)
        {
            Vector3 hip = leg.rig.root.TransformPoint(leg.rig.hip);
            float legLen = leg.rig.thighLength + leg.rig.calfLength;
            // Never aim a step further than the leg can reach with the hips at their lowest.
            float drop = Mathf.Max(0.05f, hip.y - transform.position.y - MechClamshellBuilder.AnkleHeight + maxReachDrop);
            float reach = Mathf.Sqrt(Mathf.Max(0f, legLen * legLen * 0.94f - drop * drop));
            lead = Vector3.ClampMagnitude(lead, Mathf.Max(0.1f, reach * 0.8f));
            Vector3 n = transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z));
            return GroundPoint(n + lead);
        }

        /// <summary>Feet hang under the hips in the air and trail just off the deck while boost-gliding.</summary>
        private void HangFoot(LegState leg, GaitProfile g, float dt)
        {
            Vector3 n = transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z));
            Vector3 glide = n + transform.forward * -0.25f + Vector3.up * 0.14f;
            float legLen = leg.rig.thighLength + leg.rig.calfLength;
            Vector3 hipWorld = leg.rig.root.TransformPoint(leg.rig.hip);
            Vector3 hang = hipWorld + Vector3.down * (legLen * 0.86f + MechClamshellBuilder.AnkleHeight) + transform.forward * 0.15f;
            Vector3 want = Vector3.Lerp(glide, hang, _airBlend);
            leg.footPos = Vector3.Lerp(leg.footPos, want, 1f - Mathf.Exp(-12f * dt));
            leg.footRot = Quaternion.Slerp(leg.footRot, Quaternion.Euler(_airBlend > 0.5f ? 14f : -8f, transform.eulerAngles.y, 0f), 1f - Mathf.Exp(-10f * dt));
            leg.swinging = false;
        }

        private Vector3 GroundPoint(Vector3 p)
        {
            float best = float.NegativeInfinity;
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 2.5f, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                // never stand on a mech (the brawler's physical bodies, including this mech's own)
                var body = hit.collider.attachedRigidbody;
                if (body != null && body.GetComponent<IronFauna.Brawl.MechRagdollPart>() != null) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            if (float.IsNegativeInfinity(best)) best = transform.position.y;
            return new Vector3(p.x, best, p.z);
        }

        private float ReachDeficit(LegState leg)
        {
            Vector3 ankle = leg.footPos + leg.footRot * Vector3.up * MechClamshellBuilder.AnkleHeight;
            Vector3 hip = leg.rig.root.TransformPoint(leg.rig.hip);
            float max = (leg.rig.thighLength + leg.rig.calfLength) * 0.97f;
            return Mathf.Max(0f, Vector3.Distance(hip, ankle) - max);
        }

        // ------------------------------------------------------------------------------- IK
        private void SolveLeg(LegState leg)
        {
            var rig = leg.rig;
            if (rig.foot != null) rig.foot.SetPositionAndRotation(leg.footPos, leg.footRot);

            Vector3 ankleWorld = leg.footPos + leg.footRot * Vector3.up * MechClamshellBuilder.AnkleHeight;
            Vector3 a = rig.root.InverseTransformPoint(ankleWorld);
            Vector3 h = rig.hip;
            float l1 = rig.thighLength, l2 = rig.calfLength;
            Vector3 d = a - h;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.005f);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;

            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float height = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 hint = rig.kneeForward ? Vector3.forward : Vector3.back;
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.forward;
            Vector3 knee = h + dir * along + bend * height;
            Vector3 ankle = h + dir * dist;

            Vector3 hinge = Vector3.Cross(bend, dir).normalized;
            Vector3 thighDir = (knee - h).normalized;
            Vector3 calfDir = (ankle - knee).normalized;

            Quaternion r1 = Quaternion.LookRotation(thighDir, hinge) * Quaternion.Inverse(Quaternion.LookRotation(leg.restThighDir, leg.restHinge));
            rig.thigh.localRotation = r1;
            Quaternion inv = Quaternion.Inverse(r1);
            Quaternion r2 = Quaternion.LookRotation(inv * calfDir, inv * hinge) * Quaternion.Inverse(Quaternion.LookRotation(leg.restCalfDir, leg.restHinge));
            rig.calf.localRotation = r2;
        }

        // ------------------------------------------------------------------------------- footfall
        private void Stomp(Vector3 at, float strength, GaitProfile g)
        {
            _bodyVy -= g.stompImpulse * strength;
            if (footfallDust) Dust(at, strength);
            Footfall?.Invoke(at, strength);
        }

        private void Dust(Vector3 at, float strength)
        {
            if (_dust == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) { footfallDust = false; return; }
                var go = new GameObject("FootfallDust");
                go.transform.SetParent(transform, false);
                _dust = go.AddComponent<ParticleSystem>();
                _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _dust.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
                main.startColor = IronFaunaPalette.WithAlpha(IronFaunaPalette.Lerp(IronFaunaPalette.StudioFloorLine, IronFaunaPalette.StudioFill, 0.6f), 0.22f);
                main.gravityModifier = -0.05f;
                main.maxParticles = 300;
                main.playOnAwake = false;
                var emission = _dust.emission;
                emission.rateOverTime = 0f;
                var shape = _dust.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.35f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var col = _dust.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(IronFaunaPalette.UiNeutral, 0f), new GradientColorKey(IronFaunaPalette.UiNeutral, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
                var size = _dust.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                Texture2D puff = SoftPuff();
                mat.SetTexture("_BaseMap", puff);
                mat.mainTexture = puff;
                renderer.sharedMaterial = mat;
            }
            var emit = new ParticleSystem.EmitParams { position = at + Vector3.up * 0.05f, applyShapeToPosition = true };
            float scale = dustScale;
            _dust.Emit(emit, Mathf.RoundToInt(Mathf.Lerp(3f, 10f, strength) * scale));
        }

        private static Texture2D _puff;

        /// <summary>Soft round dust sprite (white, alpha falls off to the edge).</summary>
        private static Texture2D SoftPuff()
        {
            if (_puff != null) return _puff;
            const int n = 64;
            _puff = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "IF_DustPuff", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            _puff.SetPixels32(px);
            _puff.Apply();
            return _puff;
        }
    }
}
