using System;
using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;

namespace IronFauna.Boxing
{
    public enum PunchType
    {
        None, Jab, Cross, Hook, Uppercut,
        // martial-arts specials (scroll to pick, Space to fire)
        SpinningBackfist, SupermanPunch, Elbow, FrontKick, Roundhouse, FlyingKnee,
        Haymaker, SideKick, AxeKick, SpinningHookKick, Headbutt, ShoulderCharge,
        // MMA, Kickboxing, Muay Thai & Jiu-Jitsu additions
        LowCalfKick, QuestionMarkKick, TornadoKick, CapoeiraSweep,
        DoubleLegTakedown, FlyingScissor, Upkick, ClinchKnee,
    }

    /// <summary>
    /// The fighter's brain and upper body: movement intent, strikes, specials and grapple poses, animated
    /// procedurally on the hidden "ghost" mech that the physical ragdoll (IronFauna.Brawl.MechRagdoll) follows.
    ///  - Movement: camera-relative (moveReference) or opponent-relative intent, sprint, ground following; faces where
    ///    it walks and turns into an opponent to strike. The hips and legs belong to MechGaitAnimator.
    ///  - Strikes: long readable wind-ups, torso drive, lunges, full arm extension, heavy recoveries; kicks take
    ///    over one leg (KickLeg); specials spin, jump, headbutt or shoulder-charge.
    ///  - Grapples: reach / hold / slam / swing poses (holding, holdLift, holdSwing, HoldPoint).
    ///  - Never still: knee bounce, weave and guard pumping; hit reactions snap the head and torso on springs.
    /// Player input or AI writes <see cref="intent"/> every frame.
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
            public bool sprint;        // held: run (open stance, no squaring up)
            public bool grab;          // held: reach for / hold on to another mech (release = throw)
            public bool crouch;        // held: deep MMA crouch (ducks high strikes, shoots takedowns)
            public bool sit;           // held/toggled: Jiu-Jitsu floor sit / butt-scoot & floor body-drag
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
        [Tooltip("When set, intent.move is read in this transform's yaw frame (the camera) instead of relative to the opponent")]
        public Transform moveReference;
        [Tooltip("Squares up to the opponent inside this range; further out it turns to face where it walks")]
        public float lockOnRange = 9f;
        [Tooltip("Off: never locks on - faces where it walks; turns into an opponent only while punching or guarding")]
        public bool lockOn = true;
        public float punchTurnRange = 4.5f;
        public float sprintSpeed = 5.6f;
        [Tooltip("Ride the terrain: the root follows the static ground under it (hills, rocks)")]
        public bool followGround;
        public float minDistance = 2.05f;

        public float health = 100f;
        public float maxHealth = 100f;
        public bool IsKO { get; private set; }
        public bool Punching => _punch != null;
        public PunchType CurrentPunch => _punch != null ? _punch.type : PunchType.None;
        /// <summary>0..1 during a wind-up (the AI reads it to react).</summary>
        public bool Telegraphing => _punch != null && _phase == Phase.Windup;
        public Vector3 Velocity => _vel;
        /// <summary>0 = roaming (walks where it is steered), 1 = squared up to the opponent in a fighting stance.</summary>
        public float FightBlend => _fight;

        [Header("Weight Class & Stance")]
        public float strikeSpeedScale = 1f;
        public float damageScale = 1f;
        public bool hyperArmor;
        public bool hasArmL = true, hasArmR = true, hasLegL = true, hasLegR = true;
        public int ArmsLeft => (hasArmL ? 1 : 0) + (hasArmR ? 1 : 0);
        public int LegsLeft => (hasLegL ? 1 : 0) + (hasLegR ? 1 : 0);
        public float CrouchBlend => _crouchBlend;
        public float SeatedBlend => _seatedBlend;
        public bool IsSeated => _seatedBlend > 0.45f || LegsLeft == 0;
        public bool IsCrouching => _crouchBlend > 0.45f || IsSeated;

        /// <summary>Fired when a move is blocked by missing limbs or throws the mech off balance (message, knockdownSeconds).</summary>
        public event Action<string, float> OffBalanceTriggered;

        public void SetLimbState(bool armL, bool armR, bool legL, bool legR)
        {
            hasArmL = armL;
            hasArmR = armR;
            hasLegL = legL;
            hasLegR = legR;
            if (_gait == null) _gait = GetComponent<IronFauna.Gameplay.MechGaitAnimator>();
            if (_gait != null)
            {
                _gait.hasLeftLeg = legL;
                _gait.hasRightLeg = legR;
            }
            if (LegsLeft == 0) intent.sit = true;
        }

        /// <summary>Physics brawler: the ragdoll's collisions deal the damage, so the distance check is skipped.</summary>
        [NonSerialized] public bool physicsDriven;
        /// <summary>Set by the ragdoll while its body is down or broken: no new punches, no footwork.</summary>
        [NonSerialized] public bool suppressed;
        /// <summary>Increments on every punch so a physical strike lands once.</summary>
        public int PunchSerial { get; private set; }
        /// <summary>The fist is travelling at (or resting on) the target.</summary>
        public bool StrikeActive => _punch != null && (_phase == Phase.Strike || _phase == Phase.Hold);
        public bool PunchLeadHand => _punch != null && _activeStrikeLead;
        public float PunchPower => _punch != null ? Power(_punch) : 0f;
        public float PunchDamage => _punch != null ? _punch.damage * damageScale : 0f;
        public bool StrikeSweeps => _punch != null && _punch.sweep;
        /// <summary>Ragdoll got hit: interrupt a wind-up and stagger the footwork.</summary>
        /// <summary>Shoves the fighter (adds to its travel velocity; footwork brakes it back down).</summary>
        public void AddKnockback(Vector3 v)
        {
            _vel += new Vector3(v.x, 0f, v.z);
        }

        public void Interrupt(float stagger)
        {
            if (hyperArmor && _punch != null && stagger < 0.28f) return; // Heavy hyper-armor powers through light hits
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
            public string name;
            public bool lead;                 // lead side (left in orthodox): hand, or leg for kicks
            public float windup, strike, hold, recover;
            public float twist;               // torso yaw at contact, degrees (+ = lead shoulder forward)
            public float load;                // torso yaw during the wind-up (opposite way)
            public float lean, roll, dip, lunge;
            public float damage, knock, stop, shake;
            public Vector3 pull;              // wind-up pull of the fist (body space, mirrored by hand)
            public Vector3 arc;               // control-point offset of the strike path (0 = straight)
            public Vector3 elbow;             // elbow direction hint at contact (x mirrored by hand)
            public float aimDrop;             // aim this far below the head point (uppercut finds the chin)
            // specials
            public bool kick, knee;           // the move is thrown with a leg
            public float spin;                // full upper-body spin during the strike (deg)
            public float rise;                // upper body lifts (jumping strikes)
            public float lungeImpulse;        // the whole mech surges forward as the strike starts (m/s)
            public float reach = 1f;          // fraction of full arm reach (elbows are short)
            public float kickSide;            // roundhouse: the foot swings in from the side
            public float kickUp;              // axe kick: the foot rises high and chops down
            public bool head;                 // headbutt: the head is the weapon
            public bool body;                 // shoulder charge: the torso is the weapon
            public bool sweep;                // low sweep / takedown: trips & knocks down opponents on contact
        }

        // Exaggerated on purpose: long, readable wind-ups that load the whole body, big torso drive, a lunge,
        // and a slow heavy recovery - every strike is a commitment you can see coming and punish.
        private static readonly Dictionary<PunchType, PunchSpec> Specs = new Dictionary<PunchType, PunchSpec>
        {
            [PunchType.Jab] = new PunchSpec
            {
                type = PunchType.Jab, name = "JAB", lead = true, windup = 0.16f, strike = 0.11f, hold = 0.08f, recover = 0.28f,
                twist = 48f, load = 22f, lean = 12f, roll = -4f, dip = 0.1f, lunge = 0.55f,
                damage = 5f, knock = 1.1f, stop = 0.06f, shake = 0.25f,
                pull = new Vector3(0.1f, -0.1f, -0.42f), arc = Vector3.zero, elbow = new Vector3(0.6f, -1f, -0.1f),
            },
            [PunchType.Cross] = new PunchSpec
            {
                type = PunchType.Cross, name = "CROSS", lead = false, windup = 0.26f, strike = 0.13f, hold = 0.1f, recover = 0.4f,
                twist = -52f, load = 50f, lean = 20f, roll = 6f, dip = 0.18f, lunge = 0.9f,
                damage = 10f, knock = 2.4f, stop = 0.1f, shake = 0.5f,
                pull = new Vector3(0.25f, -0.12f, -0.75f), arc = Vector3.zero, elbow = new Vector3(0.7f, -1f, -0.1f),
            },
            [PunchType.Hook] = new PunchSpec
            {
                type = PunchType.Hook, name = "HOOK", lead = true, windup = 0.3f, strike = 0.15f, hold = 0.1f, recover = 0.45f,
                twist = -38f, load = 62f, lean = 10f, roll = 14f, dip = 0.3f, lunge = 0.5f,
                damage = 13f, knock = 3f, stop = 0.13f, shake = 0.65f,
                pull = new Vector3(0.75f, 0.05f, -0.45f), arc = new Vector3(2.0f, 0.2f, 0.3f), elbow = new Vector3(1f, 0.45f, -0.35f),
            },
            [PunchType.Uppercut] = new PunchSpec
            {
                type = PunchType.Uppercut, name = "UPPERCUT", lead = false, windup = 0.34f, strike = 0.14f, hold = 0.12f, recover = 0.48f,
                twist = -30f, load = 36f, lean = -8f, roll = -10f, dip = 0.75f, lunge = 0.6f,
                damage = 15f, knock = 2.6f, stop = 0.14f, shake = 0.75f, rise = 0.25f,
                pull = new Vector3(0.15f, -1.0f, -0.25f), arc = new Vector3(0.15f, -1.8f, 0.3f), elbow = new Vector3(0.35f, -1f, -0.5f),
                aimDrop = 0.35f,
            },
            [PunchType.SpinningBackfist] = new PunchSpec
            {
                type = PunchType.SpinningBackfist, name = "SPINNING BACKFIST", lead = true, windup = 0.22f, strike = 0.34f, hold = 0.08f, recover = 0.5f,
                twist = 20f, load = -30f, lean = 4f, roll = 0f, dip = 0.15f, lunge = 0.4f, spin = 360f, lungeImpulse = 2.5f,
                damage = 16f, knock = 3.4f, stop = 0.15f, shake = 0.8f,
                pull = new Vector3(-0.3f, 0f, -0.2f), arc = Vector3.zero, elbow = new Vector3(0.3f, -1f, -0.2f),
            },
            [PunchType.SupermanPunch] = new PunchSpec
            {
                type = PunchType.SupermanPunch, name = "SUPERMAN PUNCH", lead = false, windup = 0.32f, strike = 0.16f, hold = 0.12f, recover = 0.5f,
                twist = -58f, load = 40f, lean = 26f, roll = 4f, dip = 0.45f, lunge = 1.2f, rise = 0.7f, lungeImpulse = 7f,
                damage = 17f, knock = 3.2f, stop = 0.15f, shake = 0.85f,
                pull = new Vector3(0.3f, 0.2f, -0.9f), arc = new Vector3(0f, 0.6f, 0f), elbow = new Vector3(0.6f, -1f, -0.1f),
            },
            [PunchType.Elbow] = new PunchSpec
            {
                type = PunchType.Elbow, name = "SPINNING ELBOW", lead = false, windup = 0.2f, strike = 0.16f, hold = 0.1f, recover = 0.38f,
                twist = -70f, load = 55f, lean = 10f, roll = 10f, dip = 0.2f, lunge = 0.6f, reach = 0.42f,
                damage = 14f, knock = 2.6f, stop = 0.13f, shake = 0.7f,
                pull = new Vector3(0.5f, 0.25f, -0.2f), arc = new Vector3(1.2f, 0.3f, 0f), elbow = new Vector3(1f, 0.6f, 0.4f),
            },
            [PunchType.FrontKick] = new PunchSpec
            {
                type = PunchType.FrontKick, name = "FRONT KICK", lead = false, kick = true, windup = 0.3f, strike = 0.14f, hold = 0.14f, recover = 0.45f,
                twist = -10f, load = 10f, lean = -16f, roll = 0f, dip = 0.1f, lunge = 0.3f,
                damage = 13f, knock = 4.5f, stop = 0.12f, shake = 0.7f, aimDrop = 1.6f,
            },
            [PunchType.Roundhouse] = new PunchSpec
            {
                type = PunchType.Roundhouse, name = "ROUNDHOUSE KICK", lead = false, kick = true, windup = 0.34f, strike = 0.2f, hold = 0.12f, recover = 0.55f,
                twist = -70f, load = 30f, lean = -12f, roll = -22f, dip = 0.1f, lunge = 0.2f, kickSide = 1.8f,
                damage = 19f, knock = 3.8f, stop = 0.17f, shake = 0.9f, aimDrop = 0.6f,
            },
            [PunchType.FlyingKnee] = new PunchSpec
            {
                type = PunchType.FlyingKnee, name = "FLYING KNEE", lead = false, kick = true, knee = true, windup = 0.28f, strike = 0.18f, hold = 0.12f, recover = 0.5f,
                twist = -15f, load = 15f, lean = -6f, roll = 0f, dip = 0.35f, lunge = 0.8f, rise = 0.9f, lungeImpulse = 7.5f,
                damage = 18f, knock = 4f, stop = 0.16f, shake = 0.85f, aimDrop = 1.2f,
            },
            [PunchType.Haymaker] = new PunchSpec
            {
                type = PunchType.Haymaker, name = "HAYMAKER", lead = false, windup = 0.45f, strike = 0.18f, hold = 0.12f, recover = 0.6f,
                twist = -65f, load = 70f, lean = 24f, roll = 12f, dip = 0.35f, lunge = 1.1f, lungeImpulse = 3f,
                damage = 22f, knock = 5f, stop = 0.18f, shake = 1f,
                pull = new Vector3(0.9f, 0.5f, -1.0f), arc = new Vector3(1.2f, 1.8f, -0.2f), elbow = new Vector3(1f, 0.5f, -0.4f),
            },
            [PunchType.SideKick] = new PunchSpec
            {
                type = PunchType.SideKick, name = "SIDE KICK", lead = false, kick = true, windup = 0.3f, strike = 0.13f, hold = 0.16f, recover = 0.45f,
                twist = -85f, load = 20f, lean = -22f, roll = -14f, dip = 0.1f, lunge = 0.4f,
                damage = 15f, knock = 7f, stop = 0.13f, shake = 0.8f, aimDrop = 1.3f,
            },
            [PunchType.AxeKick] = new PunchSpec
            {
                type = PunchType.AxeKick, name = "AXE KICK", lead = false, kick = true, windup = 0.36f, strike = 0.2f, hold = 0.14f, recover = 0.5f,
                twist = -20f, load = 15f, lean = -18f, roll = 0f, dip = 0.05f, lunge = 0.5f, kickUp = 2.8f,
                damage = 20f, knock = 3f, stop = 0.17f, shake = 0.95f, aimDrop = 0.2f,
            },
            [PunchType.SpinningHookKick] = new PunchSpec
            {
                type = PunchType.SpinningHookKick, name = "SPINNING HOOK KICK", lead = false, kick = true, windup = 0.24f, strike = 0.3f, hold = 0.1f, recover = 0.55f,
                twist = 30f, load = -30f, lean = -14f, roll = -18f, dip = 0.1f, lunge = 0.3f, spin = 360f, kickSide = -1.8f, lungeImpulse = 2f,
                damage = 23f, knock = 5f, stop = 0.2f, shake = 1f, aimDrop = 0.4f,
            },
            [PunchType.Headbutt] = new PunchSpec
            {
                type = PunchType.Headbutt, name = "HEADBUTT", lead = true, head = true, windup = 0.3f, strike = 0.12f, hold = 0.1f, recover = 0.4f,
                twist = 0f, load = 0f, lean = 48f, roll = 0f, dip = 0.3f, lunge = 1.3f, lungeImpulse = 5f,
                damage = 16f, knock = 3.5f, stop = 0.16f, shake = 0.9f,
            },
            [PunchType.ShoulderCharge] = new PunchSpec
            {
                type = PunchType.ShoulderCharge, name = "SHOULDER CHARGE", lead = true, body = true, windup = 0.32f, strike = 0.35f, hold = 0.12f, recover = 0.5f,
                twist = 75f, load = -10f, lean = 20f, roll = -8f, dip = 0.35f, lunge = 1.0f, lungeImpulse = 13f,
                damage = 14f, knock = 8f, stop = 0.15f, shake = 0.95f,
            },
            [PunchType.LowCalfKick] = new PunchSpec
            {
                type = PunchType.LowCalfKick, name = "LOW CALF KICK", lead = false, kick = true, sweep = true, windup = 0.22f, strike = 0.13f, hold = 0.10f, recover = 0.36f,
                twist = -55f, load = 25f, lean = 8f, roll = -16f, dip = 0.38f, lunge = 0.45f, kickSide = 1.4f,
                damage = 15f, knock = 3.2f, stop = 0.13f, shake = 0.75f, aimDrop = 2.6f,
            },
            [PunchType.QuestionMarkKick] = new PunchSpec
            {
                type = PunchType.QuestionMarkKick, name = "QUESTION MARK KICK", lead = true, kick = true, windup = 0.28f, strike = 0.16f, hold = 0.12f, recover = 0.44f,
                twist = 50f, load = -25f, lean = -14f, roll = 18f, dip = 0.15f, lunge = 0.45f, kickSide = -1.2f, kickUp = 1.6f,
                damage = 20f, knock = 3.8f, stop = 0.16f, shake = 0.9f, aimDrop = 0.2f,
            },
            [PunchType.TornadoKick] = new PunchSpec
            {
                type = PunchType.TornadoKick, name = "360 TORNADO KICK", lead = false, kick = true, windup = 0.26f, strike = 0.32f, hold = 0.12f, recover = 0.52f,
                twist = -65f, load = 40f, lean = -12f, roll = -20f, dip = 0.35f, lunge = 0.8f, spin = 360f, rise = 1.05f, kickSide = 1.9f, lungeImpulse = 6.5f,
                damage = 24f, knock = 5.5f, stop = 0.19f, shake = 1.0f, aimDrop = 0.35f,
            },
            [PunchType.CapoeiraSweep] = new PunchSpec
            {
                type = PunchType.CapoeiraSweep, name = "CAPOEIRA FLOOR SWEEP", lead = false, kick = true, sweep = true, windup = 0.26f, strike = 0.32f, hold = 0.12f, recover = 0.48f,
                twist = -70f, load = 35f, lean = 34f, roll = -28f, dip = 1.35f, lunge = 0.65f, spin = 360f, kickSide = 2.1f, lungeImpulse = 4f,
                damage = 17f, knock = 5.2f, stop = 0.16f, shake = 0.9f, aimDrop = 2.9f,
            },
            [PunchType.DoubleLegTakedown] = new PunchSpec
            {
                type = PunchType.DoubleLegTakedown, name = "DOUBLE-LEG TAKEDOWN", lead = true, body = true, sweep = true, windup = 0.24f, strike = 0.32f, hold = 0.14f, recover = 0.48f,
                twist = 15f, load = -10f, lean = 42f, roll = 0f, dip = 1.15f, lunge = 1.4f, lungeImpulse = 15f,
                damage = 16f, knock = 7.5f, stop = 0.16f, shake = 0.95f, aimDrop = 2.0f,
            },
            [PunchType.FlyingScissor] = new PunchSpec
            {
                type = PunchType.FlyingScissor, name = "FLYING SCISSOR TAKEDOWN", lead = false, kick = true, sweep = true, windup = 0.28f, strike = 0.26f, hold = 0.14f, recover = 0.55f,
                twist = -80f, load = 30f, lean = -32f, roll = -36f, dip = 0.9f, lunge = 1.2f, rise = 0.85f, kickSide = 1.7f, lungeImpulse = 11f,
                damage = 21f, knock = 6.5f, stop = 0.18f, shake = 0.95f, aimDrop = 2.2f,
            },
            [PunchType.Upkick] = new PunchSpec
            {
                type = PunchType.Upkick, name = "JIU-JITSU UPKICK", lead = false, kick = true, windup = 0.18f, strike = 0.14f, hold = 0.12f, recover = 0.36f,
                twist = -10f, load = 8f, lean = -28f, roll = 0f, dip = 0.2f, lunge = 0.25f, kickUp = 1.9f,
                damage = 18f, knock = 5.5f, stop = 0.15f, shake = 0.85f, aimDrop = 0.5f,
            },
            [PunchType.ClinchKnee] = new PunchSpec
            {
                type = PunchType.ClinchKnee, name = "MUAY THAI PLUM KNEE", lead = true, kick = true, knee = true, windup = 0.22f, strike = 0.14f, hold = 0.12f, recover = 0.38f,
                twist = 25f, load = -15f, lean = -8f, roll = 6f, dip = 0.2f, lunge = 0.65f, lungeImpulse = 4.5f,
                damage = 19f, knock = 4.2f, stop = 0.16f, shake = 0.85f, aimDrop = 1.1f,
            },
        };

        /// <summary>The scroll-wheel specials, in order.</summary>
        public static readonly PunchType[] Specials =
        {
            PunchType.SpinningBackfist, PunchType.SupermanPunch, PunchType.Haymaker, PunchType.Elbow,
            PunchType.FrontKick, PunchType.LowCalfKick, PunchType.SideKick, PunchType.Roundhouse,
            PunchType.QuestionMarkKick, PunchType.AxeKick, PunchType.SpinningHookKick, PunchType.TornadoKick,
            PunchType.CapoeiraSweep, PunchType.FlyingKnee, PunchType.ClinchKnee, PunchType.DoubleLegTakedown,
            PunchType.FlyingScissor, PunchType.Upkick, PunchType.Headbutt, PunchType.ShoulderCharge,
        };

        public static string MoveName(PunchType t) => Specs.TryGetValue(t, out var s) ? s.name : "";

        /// <summary>The move in progress is a kick (or knee).</summary>
        public bool StrikeIsKick => _punch != null && _punch.kick;
        /// <summary>The move in progress is thrown with the lead side (hand or leg), accounting for missing-limb rerouting.</summary>
        public bool StrikeLead => _punch != null && _activeStrikeLead;
        /// <summary>A strike is winding up (telegraph).</summary>
        public bool WindingUp => _punch != null && _phase == Phase.Windup;
        public bool StrikeIsHead => _punch != null && _punch.head;
        public bool StrikeIsBody => _punch != null && _punch.body;

        // ------------------------------------------------------------------ grappling (driven by MechRagdoll)
        /// <summary>Set by the ragdoll while this mech holds something (a mech or a torn-off limb).</summary>
        [NonSerialized] public bool holding;
        /// <summary>0..1 lift of the held thing overhead (slam wind-up), set by the ragdoll.</summary>
        [NonSerialized] public float holdLift;
        /// <summary>0..1 progress of a swing of a held limb, set by the ragdoll (-1 = none).</summary>
        [NonSerialized] public float holdSwing = -1f;
        /// <summary>A strike button pressed while holding (LMB throw, RMB rip, Space slam ...): the ragdoll consumes it.</summary>
        [NonSerialized] public PunchType holdAction;
        public bool Reaching => intent.grab && ArmsLeft > 0 && !holding && _punch == null && !IsKO && !suppressed;

        /// <summary>Where a held thing is carried: out in front of the chest, dragged low on the mat when crouching/seated, raised overhead for a slam, swung in an arc.</summary>
        public Vector3 HoldPoint
        {
            get
            {
                if (_b == null || _b.bodyRoot == null) return transform.position + transform.forward * 2f + Vector3.up * 3f;
                float floorDrag = Mathf.Clamp01(_crouchBlend * 0.65f + _seatedBlend);
                float yOffset = Mathf.Lerp(_chestB + 0.1f, _chestB - 1.15f, floorDrag) + holdLift * 2.6f;
                float zOffset = Mathf.Lerp(1.9f, 2.25f, floorDrag) - holdLift * 0.9f;
                Vector3 local = new Vector3(0f, yOffset, zOffset);
                if (holdSwing >= 0f)
                {
                    float a = Mathf.Lerp(-120f, 100f, EaseInOut(holdSwing)) * Mathf.Deg2Rad;
                    local = new Vector3(Mathf.Sin(a) * 2.6f, _chestB + 0.4f, Mathf.Cos(a) * 2.6f);
                }
                Vector3 world = _b.bodyRoot.TransformPoint(local);
                if (floorDrag > 0.35f && holdLift < 0.15f)
                {
                    world.y = Mathf.Max(transform.position.y + 0.55f, world.y);
                }
                return world;
            }
        }

        private enum Phase { Windup, Strike, Hold, Recover }

        // ------------------------------------------------------------------ state
        private MechClamshellBuilder _b;
        private int _builtVersion = -1;

        private class Foot
        {
            public MechClamshellBuilder.LegRig rig;
            public bool lead;
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
        private bool _activeStrikeLead;
        private Phase _phase;
        private float _phaseT;
        private Vector3 _aim;               // fist target (world)
        private Vector3 _strikeFrom;        // wrist position when the strike began
        private bool _connected, _blockedHit;
        private PunchType _buffer;
        private float _bufferT;

        private float _slipT, _slipDir;
        private float _fight = 1f;
        private float _guardBlend;
        private float _crouchBlend;
        private float _seatedBlend;
        private IronFauna.Gameplay.MechGaitAnimator _gait;

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
                var f = new Foot { rig = rig, lead = lead };
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
            Vector3 toOpp = opponent != null ? opponent.transform.position - transform.position : Vector3.zero;
            toOpp.y = 0f;
            float dist = opponent != null ? toOpp.magnitude : float.MaxValue;
            Vector3 oppFwd = dist > 0.01f && dist < float.MaxValue ? toOpp / dist : transform.forward;

            Vector2 m = IsKO || suppressed ? Vector2.zero : Vector2.ClampMagnitude(intent.move, 1f);
            bool sprinting = intent.sprint && m.sqrMagnitude > 0.05f && _punch == null && _guardBlend < 0.5f;

            // squared up (fighting stance, always facing the opponent) only when someone is close and we are not running
            bool engage = lockOn && opponent != null && !opponent.IsKO && dist < lockOnRange && !sprinting;
            bool busy = _punch != null || _guardBlend > 0.5f || intent.punch != PunchType.None;
            if (busy) engage |= opponent != null && !opponent.IsKO && dist < (lockOn ? lockOnRange * 1.5f : punchTurnRange);
            _fight = Mathf.MoveTowards(_fight, engage ? 1f : 0f, dt * 2.5f);

            // input frame: the camera when given (free movement), otherwise the line to the opponent
            Vector3 fwd, right;
            if (moveReference != null)
            {
                fwd = Vector3.ProjectOnPlane(moveReference.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(moveReference.up, Vector3.up);
                fwd.Normalize();
            }
            else fwd = oppFwd;
            right = Vector3.Cross(Vector3.up, fwd);

            float slow = (holding ? 0.6f : 1f) * (_punch != null ? 0.35f : 1f) * (_stagger > 0f ? 0.3f : 1f) * (_guardBlend > 0.5f ? 0.7f : 1f);
            if (LegsLeft == 0) slow *= ArmsLeft > 0 ? 0.42f : 0.18f; // legless arm-crawl (or worm squirm if 0 arms)
            else if (LegsLeft == 1) slow *= 0.58f;                    // one-legged hop
            else if (_seatedBlend > 0.25f) slow *= 0.62f;             // Jiu-Jitsu butt-scoot / floor body-drag speed
            else if (_crouchBlend > 0.25f) slow *= 0.78f;
            float top = (sprinting && LegsLeft == 2 && _seatedBlend < 0.2f && _crouchBlend < 0.3f) ? sprintSpeed : maxSpeed;
            Vector3 wish = right * m.x + fwd * m.y;
            // backing off while squared up is slower than stepping in
            if (_fight > 0.5f && Vector3.Dot(wish, oppFwd) < 0f) wish *= 0.85f;
            Vector3 want = wish * top * slow;
            float rate = want.sqrMagnitude > _vel.sqrMagnitude ? acceleration : braking;
            if (IsKO) rate = 3f;
            _vel = Vector3.MoveTowards(_vel, want, rate * dt);

            Vector3 p = transform.position + _vel * dt;
            if (opponent != null && !opponent.IsKO)
            {
                Vector3 d = p - opponent.transform.position;
                d.y = 0f;
                if (d.magnitude < minDistance)
                {
                    Vector3 push = d.sqrMagnitude > 1e-4f ? d.normalized : -oppFwd;
                    p = opponent.transform.position + push * minDistance;
                    _vel -= Vector3.Project(_vel, -push) * (Vector3.Dot(_vel, -push) > 0f ? 1f : 0f);
                }
            }
            p.x = Mathf.Clamp(p.x, -ringHalfSize, ringHalfSize);
            p.z = Mathf.Clamp(p.z, -ringHalfSize, ringHalfSize);
            if (followGround && TryGround(p, out float gy)) p.y = Mathf.Lerp(transform.position.y, gy, 1f - Mathf.Exp(-14f * dt));
            transform.position = p;

            if (!IsKO)
            {
                Vector3 face = transform.forward;
                if (_fight > 0.5f && dist > 0.01f) face = oppFwd;
                else if (_vel.sqrMagnitude > 0.2f) face = _vel.normalized;
                float target = Quaternion.LookRotation(face).eulerAngles.y;
                float rate2 = turnRate * (_fight > 0.5f ? 1f : 0.7f) * (sprinting ? 0.6f : 1f);
                float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, target, rate2 * dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            _stagger = Mathf.Max(0f, _stagger - dt);
        }

        /// <summary>Height of the static ground (no rigidbodies: not mechs, crates or debris) under a point.</summary>
        public static bool TryGround(Vector3 p, out float y)
        {
            y = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, 65f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody != null) continue;
                if (hit.point.y > y) y = hit.point.y;
            }
            return !float.IsNegativeInfinity(y);
        }

        // ------------------------------------------------------------------ punches
        private void TickPunchInput()
        {
            if (holding)
            {
                if (intent.punch != PunchType.None) holdAction = intent.punch;
                _buffer = PunchType.None;
                return;
            }
            if (intent.punch != PunchType.None) { _buffer = intent.punch; _bufferT = 0.28f; }
            else _bufferT -= Time.deltaTime;
            if (_bufferT <= 0f) _buffer = PunchType.None;
            if (intent.slip != 0 && _punch == null && _slipT <= 0f && !IsKO) { _slipT = 0.42f; _slipDir = intent.slip; }

            bool canStart = !IsKO && !suppressed && _stagger <= 0f && _slipT < 0.15f &&
                            (_punch == null || (_phase == Phase.Recover && _phaseT > _punch.recover * 0.35f));
            if (_buffer != PunchType.None && canStart)
            {
                PunchType chosen = _buffer;
                // From Jiu-Jitsu floor sit, kicking throws an Upkick or Floor Sweep, and punching from crouch can shoot a takedown
                if (_seatedBlend > 0.5f && (chosen == PunchType.FrontKick || chosen == PunchType.Roundhouse)) chosen = PunchType.Upkick;
                StartPunch(Specs[chosen]);
                _buffer = PunchType.None;
            }
        }

        private bool HasArmForLead(bool lead)
        {
            bool isLeft = lead != southpaw;
            return isLeft ? hasArmL : hasArmR;
        }

        private bool HasLegForLead(bool lead)
        {
            bool isLeft = lead != southpaw;
            return isLeft ? hasLegL : hasLegR;
        }

        private Arm ArmFor(bool lead)
        {
            foreach (var a in _arms) if (a.lead == lead) return a;
            return _arms.Count > 0 ? _arms[0] : null;
        }

        private void StartPunch(PunchSpec spec)
        {
            bool useLead = spec.lead;

            // 1. Kick / Knee checks against missing legs
            if (spec.kick)
            {
                if (LegsLeft == 0)
                {
                    // Kicking with NO legs: impossible! Lurches forward on stumps and falls off balance!
                    _joltVel += new Vector3(0f, -0.6f, 2.2f);
                    _vel += transform.forward * 2.2f;
                    OffBalanceTriggered?.Invoke("NO LEGS — OFF BALANCE!", 0.95f);
                    return;
                }
                if (!HasLegForLead(useLead))
                {
                    // Switch kick to the remaining intact leg
                    useLead = !useLead;
                }
            }
            // 2. Arm Punch / Elbow checks against missing arms
            else if (!spec.head && !spec.body)
            {
                if (ArmsLeft == 0)
                {
                    // No arms left: cannot throw punches! Desperate headbutt instead
                    spec = Specs[PunchType.Headbutt];
                    useLead = spec.lead;
                    OffBalanceTriggered?.Invoke("NO ARMS — HEADBUTT!", 0f);
                }
                else if (!HasArmForLead(useLead))
                {
                    // Reroute punch to the remaining intact arm
                    useLead = !useLead;
                }
            }

            var arm = ArmFor(useLead);
            if (arm == null) return;
            _punch = spec;
            _activeStrikeLead = useLead;
            _phase = Phase.Windup;
            _phaseT = 0f;
            _connected = _blockedHit = false;
            _aim = AimPoint(spec);
            PunchSerial++;
            PunchThrown?.Invoke(this, Power(spec));
        }

        private static float Power(PunchSpec s) => Mathf.InverseLerp(4f, 23f, s.damage);

        private Vector3 AimPoint(PunchSpec spec)
        {
            if (opponent == null) return transform.position + transform.forward * 3f + Vector3.up * (_b.NeckHeight + 0.2f);
            return opponent.HeadPoint() + Vector3.down * spec.aimDrop;
        }

        /// <summary>Where a punch to the head lands (moves when the boxer slips, ducks, sits, crawls, or is knocked down on the mat).</summary>
        public Vector3 HeadPoint()
        {
            if (suppressed || IsKO) return transform.position + Vector3.up * 0.85f;
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
            _phaseT += dt * Mathf.Max(0.5f, strikeSpeedScale);
            var s = _punch;
            switch (_phase)
            {
                case Phase.Windup:
                    // keep tracking the target while loading up; commit when the strike starts
                    _aim = Vector3.Lerp(_aim, AimPoint(s), 1f - Mathf.Exp(-10f * dt));
                    if (_phaseT >= s.windup)
                    {
                        _phase = Phase.Strike; _phaseT = 0f;
                        if (s.lungeImpulse > 0f) _vel += transform.forward * (s.lungeImpulse * (LegsLeft == 0 ? 0.35f : LegsLeft == 1 ? 0.65f : 1f));
                        var arm = ArmFor(_activeStrikeLead);
                        _strikeFrom = arm != null ? arm.wrist : _aim;
                    }
                    break;
                case Phase.Strike:
                    if (_phaseT >= s.strike)
                    {
                        Resolve(s);
                        _phase = Phase.Hold; _phaseT = 0f;
                        if (s.type == PunchType.FlyingScissor) intent.sit = true; // Flying scissor lands in Jiu-Jitsu floor guard
                        // Standing on 1 leg and kicking with that ONLY leg leaves 0 legs on the ground -> topples off balance!
                        if (s.kick && LegsLeft == 1 && !IsSeated)
                        {
                            OffBalanceTriggered?.Invoke("1-LEG KICK — OFF BALANCE!", 1.15f);
                        }
                    }
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

            // ---------------- bounce: a springy knee-bob on the balls of the feet in a fighting stance (the feet stay
            // planted by the walk cycle); fades while walking free, punching or knocked out
            float wantBounce = IsKO ? 0f : (_punch != null ? 0.15f : (_guardBlend > 0.5f ? 0.45f : 0.7f)) * _fight * Mathf.Lerp(1f, 0.45f, speed01);
            _bounceWeight = Mathf.MoveTowards(_bounceWeight, wantBounce, dt * 4f);
            _hop += dt * bounceHz * Mathf.Lerp(0.85f, 1.15f, speed01);
            if (_hop >= 1f) _hop -= 1f;
            const float air = 0.56f;
            float hopShape = _hop < air ? Mathf.Sin(Mathf.PI * _hop / air) : -0.45f * Mathf.Sin(Mathf.PI * (_hop - air) / (1f - air));

            _slipT = Mathf.Max(0f, _slipT - dt);
            float slip = _slipT > 0f ? Mathf.Sin(Mathf.PI * (1f - _slipT / 0.42f)) : 0f;   // in and back out
            _guardBlend = Mathf.MoveTowards(_guardBlend, intent.guard && _punch == null && !IsKO ? 1f : 0f, dt * 7f);
            bool wantSit = (intent.sit || LegsLeft == 0) && !IsKO;
            bool oppGrounded = opponent != null && (opponent.LegsLeft == 0 || opponent.IsSeated || opponent.suppressed);
            bool groundPound = oppGrounded && _punch != null && !_punch.kick && LegsLeft > 0;
            bool wantCrouch = (intent.crouch || groundPound || (_punch != null && (_punch.type == PunchType.CapoeiraSweep || _punch.type == PunchType.DoubleLegTakedown))) && !wantSit && !IsKO;
            _seatedBlend = Mathf.MoveTowards(_seatedBlend, wantSit ? 1f : 0f, dt * 4.5f);
            _crouchBlend = Mathf.MoveTowards(_crouchBlend, wantCrouch ? 1f : 0f, dt * (groundPound ? 9f : 5.5f));
            if (_gait == null) _gait = GetComponent<IronFauna.Gameplay.MechGaitAnimator>();
            if (_gait != null)
            {
                _gait.crouchBlend = _crouchBlend;
                _gait.seatedBlend = _seatedBlend;
                _gait.hasLeftLeg = hasLegL;
                _gait.hasRightLeg = hasLegR;
                // Full-body kinetic chain: pelvis coils and opens into punches and kicks
                float kickHipRoll = (_punch != null && _punch.kick) ? (_activeStrikeLead != southpaw ? 1f : -1f) * _kickW * (12f + Mathf.Abs(_punch.kickSide) * 7f) : 0f;
                float kickHipPitch = (_punch != null && _punch.kick) ? -_kickW * 10f : pLean * 0.25f;
                _gait.combatPelvisAngles = new Vector3(kickHipPitch, pYaw * 0.36f, kickHipRoll);
            }

            float koDrop = 0f, koPitch = 0f;
            if (IsKO)
            {
                _koT += dt;
                float k = EaseOut3(_koT / 0.9f);
                koDrop = 1.25f * k;
                koPitch = -24f * k;
            }

            float bodyTarget = -crouch + hopShape * bounceHeight * _bounceWeight * (1f - _seatedBlend) - pDip - slip * 0.28f - _guardBlend * 0.06f - _crouchBlend * 0.35f - koDrop;
            Spring(ref _bodyY, ref _bodyVy, bodyTarget, bodyFrequency, bodyDamping, dt);

            // ---------------- weight shift: hips travel over the front foot on punches, lag behind travel
            float weave = Mathf.Sin(_t * 2f * Mathf.PI * 0.43f) * 0.1f * (_punch == null ? 1f : 0.3f) * (IsKO ? 0f : 1f);
            Vector3 shiftTarget = new Vector3(weave + slip * _slipDir * 0.42f - localVel.x * 0.05f, 0f,
                                              pLunge - localVel.z * 0.06f);
            Spring(ref _shift, ref _shiftVel, shiftTarget, 3.2f, 0.6f, dt);
            Spring(ref _jolt, ref _joltVel, Vector3.zero, 2.4f, 0.42f, dt);

            Vector3 root = new Vector3(_shift.x + _jolt.x, _bodyY + _jolt.y, _shift.z + _jolt.z);

            // ---------------- torso: stance turn + punch drive + travel lean + weave/slip roll + hit jolt
            float stanceYaw = 16f * sgn * _fight;
            float travelLean = Mathf.Clamp(localVel.z * 2.2f, -5f, 6f);
            float sitLean = -14f * _seatedBlend + (_seatedBlend > 0.3f ? Mathf.Sin(_t * 7f) * 8f * speed01 : 0f);
            float crouchLean = (groundPound ? 26f : 18f) * _crouchBlend;
            float yawTarget = stanceYaw + pYaw + Mathf.Sin(_t * 2f * Mathf.PI * 0.43f + 1f) * 3f;
            float pitchTarget = Mathf.Lerp(3f, 7f, _fight) + pLean + travelLean + _guardBlend * 6f + slip * 10f + crouchLean + sitLean + koPitch;
            float rollTarget = pRoll - localVel.x * 1.2f - slip * _slipDir * 16f - weave * 25f;
            float torsoHz = _punch != null && (_phase == Phase.Strike || _phase == Phase.Hold) ? 9f : 4.5f;
            Spring(ref _yaw, ref _yawVel, yawTarget, torsoHz, 0.62f, dt);
            Spring(ref _pitch, ref _pitchVel, pitchTarget, 4f, 0.55f, dt);
            Spring(ref _roll, ref _rollVel, rollTarget, 3.5f, 0.55f, dt);
            // the hips are the walk cycle's (MechGaitAnimator); punches, dips and hits move the upper body only
            SpecialDrive(out float spinDeg, out float rise);
            _b.bodyRoot.localPosition = _bodyRestPos + new Vector3(root.x, (_bodyY + crouch) + _jolt.y, root.z) + Vector3.up * rise;
            _b.bodyRoot.localRotation = Quaternion.Euler(_pitch + _jolt.z * 12f, _yaw + spinDeg * sgn, _roll - _jolt.x * 14f) * _bodyRestRot;

            // ---------------- head: keeps eyes on the opponent, snaps on hits
            Spring(ref _headSnap, ref _headSnapVel, Vector3.zero, 2.6f, 0.38f, dt);
            if (_b.headRoot != null)
            {
                float lookYaw = -_yaw * 0.7f;
                float lookPitch = -(_pitch - 7f) * 0.6f - 6f;
                _b.headRoot.localRotation = Quaternion.Euler(lookPitch + _headSnap.x, lookYaw + _headSnap.y, -_roll * 0.4f + _headSnap.z) * _headRestRot;
            }

            // ---------------- kicks take one leg over from the walk cycle
            KickLeg(dt, rise);

            // ---------------- arms
            Spring(ref _armPush, ref _armPushVel, Vector3.zero, 3.4f, 0.5f, dt);
            // another animator may have swung the arm roots this frame (the walk cycle's arm swing): fists are aimed
            // in world space, so start each arm from its rest mount
            foreach (var a in _arms) a.rig.root.localRotation = Quaternion.identity;
            foreach (var a in _arms) ArmPose(a, dt, hopShape, sgn);
        }

        /// <summary>Upper-body spin (spinning strikes) and lift (jumping strikes) for the move in progress.</summary>
        private void SpecialDrive(out float spin, out float rise)
        {
            spin = rise = 0f;
            if (_punch == null) return;
            var s = _punch;
            switch (_phase)
            {
                case Phase.Windup:
                    spin = s.spin > 0f ? -25f * EaseInOut(_phaseT / s.windup) : 0f;         // wind back before the spin
                    rise = -s.rise * 0.3f * EaseInOut(_phaseT / s.windup);                // sink to jump
                    break;
                case Phase.Strike:
                {
                    float k = _phaseT / s.strike;
                    spin = s.spin > 0f ? Mathf.Lerp(-25f, s.spin, EaseInOut(k)) : 0f;
                    rise = s.rise * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 0.75f + 0.1f));
                    break;
                }
                case Phase.Hold:
                    spin = 0f;                                                              // 360 = home
                    rise = s.rise * 0.6f;
                    break;
                case Phase.Recover:
                    rise = s.rise * 0.6f * (1f - EaseInOut(_phaseT / (s.recover * (_connected ? 1f : 1.35f))));
                    break;
            }
        }

        private float _kickW;

        /// <summary>
        /// Kicks and knees: the kicking leg chambers (knee up), snaps out along the move's path to the target and
        /// retracts, blended over whatever the walk cycle is doing with that leg; the other leg keeps walking.
        /// </summary>
        private void KickLeg(float dt, float rise)
        {
            bool kicking = _punch != null && _punch.kick && LegsLeft > 0;
            float want = 0f;
            Foot f = null;
            if (kicking)
            {
                foreach (var ft in _feet) if (ft.lead == _activeStrikeLead) f = ft;
                switch (_phase)
                {
                    case Phase.Windup: want = EaseInOut(_phaseT / _punch.windup); break;
                    case Phase.Strike:
                    case Phase.Hold: want = 1f; break;
                    case Phase.Recover: want = 1f - EaseInOut(_phaseT / (_punch.recover * (_connected ? 1f : 1.35f))); break;
                }
            }
            _kickW = kicking ? want : Mathf.MoveTowards(_kickW, 0f, dt * 6f);
            if (f == null || _kickW <= 0.001f) return;

            var s = _punch;
            var rig = f.rig;
            Vector3 hipW = rig.root.TransformPoint(rig.hip);
            float legLen = rig.thighLength + rig.calfLength;
            Vector3 fwd = transform.forward;
            Vector3 side = transform.right * (f.rig.root.name.EndsWith("_L") ? -1f : 1f);
            Vector3 walkFoot = rig.foot.position;
            Quaternion walkRot = rig.foot.rotation;

            // chamber: knee driven up, foot tucked under it
            Vector3 chamber = hipW + fwd * 0.55f + Vector3.down * (legLen * (s.knee ? 0.42f : 0.5f)) + side * (s.kickSide > 0f ? 0.6f : 0.1f);
            // target: the ankle reaches for the aim point (a knee keeps the foot tucked and drives the knee there)
            Vector3 toAim = _aim - hipW;
            Vector3 strikeAnkle = hipW + Vector3.ClampMagnitude(toAim, legLen * 0.97f);
            if (s.knee) strikeAnkle = hipW + fwd * 0.75f + Vector3.down * (legLen * 0.32f);
            Vector3 strikeFoot = strikeAnkle - Vector3.up * MechClamshellBuilder.AnkleHeight;
            Vector3 ctrl = Vector3.Lerp(chamber, strikeFoot, 0.5f) + side * s.kickSide + Vector3.up * (Mathf.Abs(s.kickSide) * 0.3f + s.kickUp);

            Vector3 footPos;
            switch (_phase)
            {
                case Phase.Windup: footPos = Vector3.Lerp(walkFoot, chamber, _kickW); break;
                case Phase.Strike:
                {
                    float k = EaseIn2(_phaseT / s.strike);
                    footPos = (1 - k) * (1 - k) * chamber + 2 * (1 - k) * k * ctrl + k * k * strikeFoot;
                    break;
                }
                case Phase.Hold: footPos = strikeFoot; break;
                default: footPos = Vector3.Lerp(walkFoot, Vector3.Lerp(chamber, strikeFoot, 0.4f), _kickW); break;
            }
            Vector3 dir = footPos - hipW;
            Quaternion kickRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd + side * s.kickSide * 0.3f, Vector3.up).normalized, Vector3.up)
                                 * Quaternion.Euler(s.knee ? 60f : -30f, 0f, 0f);
            Quaternion rot = Quaternion.Slerp(walkRot, kickRot, _kickW);
            SolveLegAt(f, footPos, rot, fwd);
        }

        /// <summary>Two-bone IK for one leg to an explicit foot pose (knee bends toward <paramref name="kneeDir"/>).</summary>
        private void SolveLegAt(Foot f, Vector3 footPos, Quaternion rot, Vector3 kneeDir)
        {
            var rig = f.rig;
            Vector3 ankleWorld = footPos + rot * Vector3.up * MechClamshellBuilder.AnkleHeight;
            Vector3 a = rig.root.InverseTransformPoint(ankleWorld);
            Vector3 h = rig.hip;
            float l1 = rig.thighLength, l2 = rig.calfLength;
            Vector3 d = a - h;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.05f, l1 + l2 - 0.01f);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float height = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 hint = rig.root.InverseTransformDirection(kneeDir + Vector3.up * 0.6f);
            if (!rig.kneeForward) hint = -hint;
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.forward;
            Vector3 knee = h + dir * along + bend * height;
            Vector3 ankle = h + dir * dist;
            if (rig.foot != null)
            {
                Vector3 clampedFootWorld = rig.root.TransformPoint(ankle) - rot * Vector3.up * MechClamshellBuilder.AnkleHeight;
                rig.foot.SetPositionAndRotation(clampedFootWorld, rot);
            }
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

            bool punching = _punch != null && !_punch.kick && !_punch.head && !_punch.body && _activeStrikeLead == a.lead;
            if (punching)
            {
                var s = _punch;
                float x = a.rig.side;
                // Shoulder / clavicle protraction: the shoulder reaches forward into the punch and retracts on windup
                float shoulProtract = _phase == Phase.Windup ? -14f * EaseInOut(_phaseT / s.windup)
                                    : (_phase == Phase.Strike || _phase == Phase.Hold) ? 20f
                                    : 20f * (1f - EaseInOut(_phaseT / (s.recover * (_connected ? 1f : 1.35f))));
                a.rig.root.localRotation = Quaternion.Euler(-Mathf.Abs(shoulProtract) * 0.35f, -x * shoulProtract, 0f);

                Vector3 pull = body.TransformPoint(GuardPoint(a, hopShape) + new Vector3(s.pull.x * x, s.pull.y, s.pull.z));
                Vector3 elbowHint = new Vector3(s.elbow.x * x, s.elbow.y, s.elbow.z);
                Vector3 shoulderW = a.rig.root.TransformPoint(a.rig.shoulder);
                float reach = (a.rig.upperLength + a.rig.foreLength - 0.02f) * s.reach;
                Vector3 toAim = _aim - shoulderW;
                Vector3 contact = shoulderW + (toAim.sqrMagnitude > 1e-4f ? toAim.normalized : transform.forward) * reach;
                if (s.spin > 0f)
                {
                    contact = body.TransformPoint(new Vector3(x * (Mathf.Abs(a.rig.shoulder.x) + reach * 0.95f + 0.9f), _chestB + 0.1f, 0.35f));
                }
                switch (_phase)
                {
                    case Phase.Windup:
                        want = Vector3.Lerp(guard, pull, EaseInOut(_phaseT / s.windup));
                        hintTarget = Vector3.Lerp(hintTarget, elbowHint, EaseInOut(_phaseT / s.windup));
                        follow = 32f;
                        break;
                    case Phase.Strike:
                    {
                        float k = EaseIn2(_phaseT / s.strike);
                        Vector3 ctrl = Vector3.Lerp(_strikeFrom, contact, 0.5f) + body.TransformDirection(new Vector3(s.arc.x * x, s.arc.y, s.arc.z));
                        want = (1 - k) * (1 - k) * _strikeFrom + 2 * (1 - k) * k * ctrl + k * k * contact;
                        hintTarget = elbowHint;
                        follow = 90f;
                        break;
                    }
                    case Phase.Hold:
                        want = contact;
                        hintTarget = elbowHint;
                        follow = _connected ? 6f : 40f;
                        break;
                    case Phase.Recover:
                    {
                        float k = EaseInOut(_phaseT / (s.recover * (_connected ? 1f : 1.35f)));
                        want = Vector3.Lerp(contact, guard, k);
                        hintTarget = Vector3.Lerp(elbowHint, hintTarget, k);
                        follow = 24f;
                        break;
                    }
                }
            }
            else if (_punch != null)
            {
                if (_punch.kick && a.lead == _activeStrikeLead)
                {
                    // Muay Thai / Kickboxing counterbalance: the kick-side arm whips down and back across the hip!
                    want = body.TransformPoint(new Vector3(a.rig.side * 0.85f, _chestB - 0.85f, -0.35f * _kickW));
                    hintTarget = new Vector3(a.rig.side * 0.9f, -0.5f, -0.8f);
                    follow = 24f;
                }
                else
                {
                    // Opposite hand stays tucked tight to the temple/chin to protect the head
                    want = body.TransformPoint(GuardPoint(a, hopShape) + new Vector3(-a.rig.side * 0.08f, 0.10f, -0.14f));
                }
            }
            else if (holding || Reaching)
            {
                // grapple: intact hands reach out to the target or clamp on what is held
                Vector3 shoulderW = a.rig.root.TransformPoint(a.rig.shoulder);
                float reach = a.rig.upperLength + a.rig.foreLength - 0.03f;
                Vector3 target;
                if (holding) target = HoldPoint + body.right * a.rig.side * 0.45f;
                else
                {
                    Vector3 grabAt = opponent != null && (opponent.transform.position - transform.position).sqrMagnitude < 25f
                        ? opponent.ChestPoint() : body.TransformPoint(new Vector3(0f, _chestB, 2.5f));
                    target = grabAt + body.right * a.rig.side * 0.5f;
                }
                Vector3 d = target - shoulderW;
                want = shoulderW + Vector3.ClampMagnitude(d, reach);
                hintTarget = new Vector3(a.rig.side * 0.8f, -0.6f, -0.3f);
                follow = 16f;
            }
            else if (_seatedBlend > 0.35f && _vel.sqrMagnitude > 0.05f)
            {
                // Jiu-Jitsu butt-scoot OR legless arm-crawl on the mat:
                // When legless (LegsLeft == 0), the arms reach far out in front and haul the torso forward!
                bool legless = LegsLeft == 0;
                float scootCycle = _t * (legless ? 8.5f : 7f) + (a.lead ? 0f : Mathf.PI);
                float zReach = legless ? (0.85f + Mathf.Cos(scootCycle) * 0.75f) : (-0.15f + Mathf.Cos(scootCycle) * 0.45f);
                float xSpread = legless ? a.rig.side * 0.65f : a.rig.side * 0.92f;
                Vector3 matHand = transform.TransformPoint(new Vector3(xSpread, 0.22f + Mathf.Max(0f, Mathf.Sin(scootCycle)) * 0.26f, zReach));
                want = Vector3.Lerp(want, matHand, _seatedBlend);
                hintTarget = new Vector3(a.rig.side * 0.9f, legless ? 0.2f : -0.4f, -0.8f);
                follow = 22f;
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
