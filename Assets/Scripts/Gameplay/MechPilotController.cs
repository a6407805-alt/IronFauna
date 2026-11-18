using UnityEngine;
using UnityEngine.InputSystem;
using IronFauna.Clamshell;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Player locomotion for a piloted mech: camera-relative WASD with mass (acceleration, braking, limited turn rate,
    /// little air control), boost (Shift), thruster jump and hover (Space), real collisions through a CharacterController,
    /// and landing impacts. Handling comes from the leg and torso weight classes. Works with MechPilotCamera (V toggles
    /// first / third person) and drives MechRobotOperator's procedural animation through FeedLocomotion.
    /// </summary>
    [RequireComponent(typeof(MechClamshellBuilder))]
    public class MechPilotController : MonoBehaviour
    {
        /// <summary>Handling of one weight class.</summary>
        [System.Serializable]
        public struct Handling
        {
            public float walkSpeed;     // m/s
            public float boostSpeed;    // m/s while boosting
            public float acceleration;  // m/s^2 toward the target speed
            public float braking;       // m/s^2 when no key is held
            public float turnRate;      // deg/s the torso turns toward the aim
            public float jumpSpeed;     // m/s upward at take-off
            public float hoverThrust;   // m/s^2 upward while Space is held in the air
        }

        [Header("Handling by leg weight class")]
        public Handling light = new Handling { walkSpeed = 9f, boostSpeed = 22f, acceleration = 26f, braking = 30f, turnRate = 240f, jumpSpeed = 9f, hoverThrust = 27f };
        public Handling medium = new Handling { walkSpeed = 7f, boostSpeed = 18f, acceleration = 18f, braking = 22f, turnRate = 170f, jumpSpeed = 8f, hoverThrust = 24f };
        public Handling heavy = new Handling { walkSpeed = 5f, boostSpeed = 13f, acceleration = 11f, braking = 15f, turnRate = 110f, jumpSpeed = 6.5f, hoverThrust = 21f };

        [Header("Physics")]
        public float gravity = -24f;
        [Range(0f, 1f)] public float airControl = 0.25f;
        [Tooltip("Instant extra speed when boost starts (a dash).")]
        public float boostKick = 6f;
        [Tooltip("Fall speed (m/s) that counts as a hard landing.")]
        public float hardLandingSpeed = 14f;

        [Header("Boost energy")]
        public float energyMax = 100f;
        public float boostDrain = 28f;
        public float hoverDrain = 34f;
        public float energyRegen = 24f;
        public float regenDelay = 0.6f;

        // ---- Read by the camera and HUD ----
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public Vector3 Velocity => _velocity + Vector3.up * _verticalSpeed;
        public bool Grounded { get; private set; } = true;
        public bool Boosting { get; private set; }
        public float Energy { get; private set; }
        public float Energy01 => energyMax > 0f ? Energy / energyMax : 0f;
        /// <summary>0..1 kick for camera shake; read and cleared by the camera.</summary>
        public float PendingImpact { get; set; }
        public float WalkPhase { get; private set; }
        public MechClamshellBuilder Builder { get; private set; }

        private CharacterController _cc;
        private MechRobotOperator _operator;
        private Vector3 _velocity;        // planar
        private float _verticalSpeed;
        private float _regenTimer;
        private bool _wasBoosting;
        private float _landSlow;          // brief speed penalty after a hard landing

        private void Awake()
        {
            Builder = GetComponent<MechClamshellBuilder>();
            Energy = energyMax;
            Yaw = transform.eulerAngles.y;
        }

        private void Start()
        {
            _operator = GetComponent<MechRobotOperator>();
            if (_operator != null)
            {
                _operator.externalLocomotion = true;
                _operator.aiWingmanActive = false;
                _operator.isPlayerPiloted = true;
                // A walking machine this size takes slow, long strides; the default gait reads like a toy.
                _operator.stepFreq = Builder.legWeight == MechWeightClass.Heavy ? 1.15f : Builder.legWeight == MechWeightClass.Light ? 1.7f : 1.4f;
                _operator.legSwingMaxDeg = 18f;
                _operator.armSwingMaxDeg = 10f;
                _operator.bobAmp = Builder.legWeight == MechWeightClass.Heavy ? 0.1f : 0.07f;
                _operator.accelLeanMaxDeg = 6f;
            }
            SetupCollider();
        }

        /// <summary>Capsule sized to the built mech, for walls, the objective and other solid scenery.</summary>
        private void SetupCollider()
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            bool quad = Builder.isQuadrupedChassis;
            float top = quad ? Builder.HipHeight + 0.9f : Mathf.Max(Builder.NeckHeight + 0.4f, 2f);
            float radius = quad ? 1.15f : 0.85f;
            _cc.radius = radius;
            _cc.height = Mathf.Max(top, radius * 2f + 0.1f);
            _cc.center = new Vector3(0f, _cc.height * 0.5f + 0.02f, 0f);
            _cc.stepOffset = 0.45f;
            _cc.slopeLimit = 40f;
            _cc.skinWidth = 0.06f;
            _cc.minMoveDistance = 0f;
        }

        public Handling Current
        {
            get
            {
                Handling h = Builder.legWeight == MechWeightClass.Light ? light : Builder.legWeight == MechWeightClass.Heavy ? heavy : medium;
                // A heavier torso slows the turn and the boost a little.
                float torso = Builder.torsoWeight == MechWeightClass.Light ? 1.08f : Builder.torsoWeight == MechWeightClass.Heavy ? 0.88f : 1f;
                h.turnRate *= torso;
                h.boostSpeed *= Mathf.Lerp(1f, torso, 0.5f);
                if (Builder.isQuadrupedChassis)
                {
                    h.walkSpeed *= 1.15f;
                    h.boostSpeed *= 1.1f;
                }
                return h;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _cc == null) return;
            Handling h = Current;

            // ---------------- input (WASD, Shift, Space)
            Vector2 move = Vector2.zero;
            bool boostHeld = false, jumpPressed = false, jumpHeld = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
                boostHeld = kb.leftShiftKey.isPressed;
                jumpPressed = kb.spaceKey.wasPressedThisFrame;
                jumpHeld = kb.spaceKey.isPressed;
            }
            if (move.sqrMagnitude > 1f) move.Normalize();

            // ---------------- torso turns toward the aim at the class's turn rate (heavy mechs lag the camera)
            float bodyYaw = transform.eulerAngles.y;
            float newYaw = Mathf.MoveTowardsAngle(bodyYaw, Yaw, h.turnRate * dt);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

            // ---------------- planar velocity with mass
            Quaternion aimYaw = Quaternion.Euler(0f, Yaw, 0f);
            Vector3 wish = aimYaw * new Vector3(move.x, 0f, move.y);
            bool canBoost = boostHeld && move.sqrMagnitude > 0.01f && Energy > 1f;
            Boosting = canBoost;
            if (Boosting && !_wasBoosting && Energy > 15f)
            {
                _velocity += wish.normalized * boostKick;   // dash on boost start
                PendingImpact = Mathf.Max(PendingImpact, 0.25f);
            }
            _wasBoosting = Boosting;

            _landSlow = Mathf.MoveTowards(_landSlow, 0f, dt * 2.5f);
            float topSpeed = (Boosting ? h.boostSpeed : h.walkSpeed) * (1f - 0.6f * _landSlow);
            Vector3 target = wish * topSpeed;
            float rate = move.sqrMagnitude > 0.01f ? (Boosting ? h.acceleration * 1.8f : h.acceleration) : h.braking;
            if (!Grounded) rate *= airControl;
            _velocity = Vector3.MoveTowards(_velocity, target, rate * dt);

            // ---------------- vertical: jump, hover thrust, gravity
            if (Grounded && jumpPressed && Energy > 8f)
            {
                _verticalSpeed = h.jumpSpeed;
                Grounded = false;
                Energy -= 8f;
                _regenTimer = regenDelay;
                if (_operator != null) _operator.PlayJumpStretch();
            }
            bool hovering = !Grounded && jumpHeld && _verticalSpeed < 2f && Energy > 1f;
            _verticalSpeed += (gravity + (hovering ? h.hoverThrust : 0f)) * dt;

            // ---------------- energy
            if (Boosting || hovering)
            {
                Energy -= (Boosting ? boostDrain : 0f) * dt + (hovering ? hoverDrain : 0f) * dt;
                _regenTimer = regenDelay;
            }
            else if (_regenTimer > 0f) _regenTimer -= dt;
            else Energy += energyRegen * dt;
            Energy = Mathf.Clamp(Energy, 0f, energyMax);

            // ---------------- move with collisions
            float fallSpeed = -_verticalSpeed;
            CollisionFlags flags = _cc.Move((_velocity + Vector3.up * _verticalSpeed) * dt);
            bool hitGround = (flags & CollisionFlags.Below) != 0 || transform.position.y <= 0.001f;
            if (transform.position.y < 0f) transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
            if ((flags & CollisionFlags.Above) != 0 && _verticalSpeed > 0f) _verticalSpeed = 0f;
            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Lose the speed that went into the wall.
                Vector3 actual = _cc.velocity;
                actual.y = 0f;
                if (actual.magnitude < _velocity.magnitude) _velocity = Vector3.MoveTowards(_velocity, actual, 40f * dt);
            }

            if (hitGround)
            {
                if (!Grounded && fallSpeed > 3f)
                {
                    float impact = Mathf.Clamp01(fallSpeed / hardLandingSpeed);
                    PendingImpact = Mathf.Max(PendingImpact, impact);
                    if (impact > 0.6f) _landSlow = impact;
                    if (_operator != null) _operator.PlayLandSquash(impact);
                }
                Grounded = true;
                _verticalSpeed = -2f;   // keep pressed onto the ground and slopes
            }
            else if (Grounded && _verticalSpeed <= 0f)
            {
                Grounded = false;       // walked off an edge
                _verticalSpeed = 0f;
            }

            WalkPhase += dt * (Grounded ? _velocity.magnitude : 0f);
            if (_operator != null) _operator.FeedLocomotion(_velocity, Grounded ? _velocity.magnitude / Mathf.Max(0.1f, h.walkSpeed) : 0.2f);
        }
    }
}
