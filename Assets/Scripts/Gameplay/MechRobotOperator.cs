using System;
using UnityEngine;
using IronFauna.Core;
using IronFauna.Clamshell;
using IronFauna.Input;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Drives an IronFauna Clamshell Robot (Humanoid or Quadruped) with:
    /// - Multi-device input (Keyboard & Mouse, PS5 DualSense, Xbox Controller) OR AI Wingman patrol
    /// - Procedural motion floor (character-motion.md): Gait bob, weight-shift roll, counter-swinging
    ///   clamshell legs & arms, acceleration pitch lean, turn banking, jump/land squash & stretch,
    ///   head look-tracking, and smooth settle into idle reactor-breathing (never freezes).
    /// - Combat & Grid operation: Railgun projectiles, Ripjaw melee saw-slash, live 5-pose knee pivot
    ///   switching, and Grid Wall / Auto-Turret deployment.
    /// </summary>
    [RequireComponent(typeof(MechClamshellBuilder))]
    public class MechRobotOperator : MonoBehaviour
    {
        [Header("Pilot Mode")]
        public bool isPlayerPiloted = true;
        public bool aiWingmanActive = true;
        [Tooltip("Movement comes from another component (MechPilotController): this operator only animates and fights.")]
        public bool externalLocomotion;
        /// <summary>When set (first/third-person pilot), shots go where the crosshair points.</summary>
        public System.Func<Vector3> aimPointOverride;

        [Header("Locomotion Tuning")]
        public float baseMoveSpeed = 5.5f;
        public float sprintMultiplier = 1.55f;
        public float jumpVelocity = 6.8f;
        public float gravity = -18f;
        public float turnSmoothSpeed = 10f;

        [Header("Procedural Motion Floor (character-motion.md)")]
        public float stepFreq = 2.5f;
        public float bobAmp = 0.09f;
        public float legSwingMaxDeg = 28f;
        public float armSwingMaxDeg = 22f;
        public float accelLeanMaxDeg = 8f;
        public float turnBankMaxDeg = 6f;
        public float idleBreatheFreq = 0.65f;
        public float idleBreatheAmp = 0.035f;

        [Header("Combat & Operation")]
        public float fireRate = 0.22f;
        public float projectileSpeed = 32f;
        public float projectileDamage = 25f;
        public float meleeSlashDamage = 55f;
        public float meleeSlashRadius = 3.2f;

        [Header("Runtime Telemetry (Read-Only)")]
        public float currentNormalizedSpeed;
        public float gaitBlendAmplitude;
        public int pickupsCollected;

        public MechClamshellBuilder Builder { get; private set; }
        public Transform CameraFollowProxy { get; private set; }

        private Vector3 _velocity;
        private Vector3 _prevPlanarVelocity;
        private float _verticalVelocity;
        private bool _isGrounded = true;
        private float _gaitTime;
        private float _idleTime;
        private float _fireCooldown;
        private float _recoilOffset;
        private float _meleeSwingTimer;
        private float _squashStretchTimer;
        private Vector3 _targetScale = Vector3.one;
        private float _smoothedPitchLean;
        private float _smoothedRollBank;
        private float _smoothedHeadYaw;
        private float _aiPatrolAngle;

        private void Awake()
        {
            Builder = GetComponent<MechClamshellBuilder>();

            // Camera follow proxy with damped yaw (camera-rigs.md: prevents camera whip)
            GameObject proxyGo = new GameObject($"{name}_CamTarget");
            proxyGo.transform.SetParent(transform, false);
            proxyGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            CameraFollowProxy = proxyGo.transform;

            EnsurePhysicsAndTriggerSetup();
        }

        private void EnsurePhysicsAndTriggerSetup()
        {
            // character-motion.md physics rule: keep a kinematic Rigidbody + trigger SphereCollider on root
            if (!TryGetComponent<Rigidbody>(out var rb))
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            if (!TryGetComponent<SphereCollider>(out var trig))
            {
                trig = gameObject.AddComponent<SphereCollider>();
            }
            trig.isTrigger = true;
            trig.radius = 1.25f;
            trig.center = new Vector3(0f, 1.25f, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 moveInput = Vector2.zero;
            bool sprint = false;
            bool jump = false;
            bool firePrimary = false;
            bool slashSecondary = false;

            var input = IronFaunaInputController.Instance;
            if (isPlayerPiloted && input != null)
            {
                moveInput = input.moveInput;
                sprint = input.isSprinting;
                jump = input.jumpPressed;
                firePrimary = input.primaryFireHeld;
                slashSecondary = input.secondarySlashPressed;

                // Camera / Aim yaw from mouse or right stick
                if (!externalLocomotion && input.lookDelta.sqrMagnitude > 0.0001f)
                {
                    CameraFollowProxy.Rotate(Vector3.up, input.lookDelta.x, Space.World);
                }

                // Live 5-Pose Knee Pivot Switching (Q/E or D-Pad Left/Right)
                if (input.nextPosePressed) CycleLegPose(1);
                if (input.prevPosePressed) CycleLegPose(-1);

                // Grid Defense Placement (1/2 or D-Pad Up/Down)
                if (input.placeWallPressed)   PlaceGridStructure(true);
                if (input.placeTurretPressed) PlaceGridStructure(false);
            }

            // If no manual input is pressed (or on the AI Wingman robot), run AI Wingman / Patrol so both robots operate!
            if (externalLocomotion)
            {
                // Pilot controller moved the mech this frame (FeedLocomotion); never let the AI take the controls.
            }
            else if ((!isPlayerPiloted && aiWingmanActive) ||
                (isPlayerPiloted && aiWingmanActive && moveInput.sqrMagnitude < 0.01f && !firePrimary && !slashSecondary))
            {
                ComputeAIWingmanInputs(out moveInput, out firePrimary);
            }

            if (!externalLocomotion) UpdateLocomotion(moveInput, sprint, jump, dt);
            UpdateProceduralMotion(dt);
            UpdateCombatOperation(firePrimary, slashSecondary, dt);
        }

        /// <summary>Movement state from an external pilot controller, so gait, lean and banking follow the real motion.</summary>
        public void FeedLocomotion(Vector3 planarVelocity, float normalizedSpeed)
        {
            _velocity = planarVelocity;
            currentNormalizedSpeed = Mathf.Clamp01(normalizedSpeed);
        }

        /// <summary>Stretch on take-off.</summary>
        public void PlayJumpStretch()
        {
            _targetScale = new Vector3(0.95f, 1.06f, 0.95f);
            _squashStretchTimer = 0.15f;
        }

        /// <summary>Squash on landing, scaled by impact strength (0..1).</summary>
        public void PlayLandSquash(float strength)
        {
            strength = Mathf.Clamp01(strength);
            _targetScale = new Vector3(1f + 0.08f * strength, 1f - 0.12f * strength, 1f + 0.08f * strength);
            _squashStretchTimer = 0.1f + 0.08f * strength;
        }

        private void ComputeAIWingmanInputs(out Vector2 move, out bool fire)
        {
            _aiPatrolAngle += Time.deltaTime * (Builder.isQuadrupedChassis ? 0.85f : 0.65f);
            float radius = Builder.isQuadrupedChassis ? 4.2f : 3.4f;
            Vector3 center = new Vector3(Builder.isQuadrupedChassis ? 2.2f : -2.2f, 0f, -1.5f);
            Vector3 desiredPos = center + new Vector3(Mathf.Cos(_aiPatrolAngle) * radius, 0f, Mathf.Sin(_aiPatrolAngle) * radius * 0.65f);

            Vector3 toTarget = desiredPos - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude > 0.35f)
            {
                Vector3 dir = toTarget.normalized;
                move = new Vector2(dir.x, dir.z);
            }
            else
            {
                move = Vector2.zero;
            }

            fire = EnemyWalker.ActiveEnemies.Count > 0;
        }

        private void UpdateLocomotion(Vector2 moveInput, bool sprint, bool jump, float dt)
        {
            float maxSpeed = baseMoveSpeed * (sprint ? sprintMultiplier : 1f) * (Builder.isQuadrupedChassis ? 1.2f : 1f);
            Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            Vector3 targetPlanarVel = moveDir * maxSpeed;
            _velocity = Vector3.Lerp(_velocity, targetPlanarVel, 10f * dt);

            // Turn robot toward movement or nearest enemy
            Vector3 facingDir = _velocity.sqrMagnitude > 0.15f ? _velocity.normalized : Vector3.zero;
            if (facingDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(facingDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSmoothSpeed * dt);
            }

            // Vertical thruster jump & gravity
            if (_isGrounded && jump)
            {
                _verticalVelocity = jumpVelocity;
                _isGrounded = false;
                _targetScale = new Vector3(0.92f, 1.12f, 0.92f); // Jump stretch
                _squashStretchTimer = 0.15f;
            }

            _verticalVelocity += gravity * dt;
            Vector3 nextPos = transform.position + (_velocity + Vector3.up * _verticalVelocity) * dt;

            // Clamp inside arena bounds and ground plane
            if (nextPos.y <= 0f)
            {
                if (!_isGrounded && _verticalVelocity < -2f)
                {
                    // Landing squash
                    _targetScale = new Vector3(1.10f, 0.85f, 1.10f);
                    _squashStretchTimer = 0.14f;
                }
                nextPos.y = 0f;
                _verticalVelocity = 0f;
                _isGrounded = true;
            }

            nextPos.x = Mathf.Clamp(nextPos.x, -12.5f, 12.5f);
            nextPos.z = Mathf.Clamp(nextPos.z, -12.5f, 12.5f);
            transform.position = nextPos;

            currentNormalizedSpeed = Mathf.Clamp01(_velocity.magnitude / Mathf.Max(0.1f, maxSpeed));
        }

        private void UpdateProceduralMotion(float dt)
        {
            if (Builder == null || Builder.visualRoot == null) return;

            // 1. Settle blend (0.3s ramp to/from idle so the robot never freezes)
            gaitBlendAmplitude = Mathf.MoveTowards(gaitBlendAmplitude, currentNormalizedSpeed, dt / 0.3f);

            _gaitTime += dt * stepFreq * Mathf.PI * 2f * Mathf.Max(0.35f, gaitBlendAmplitude);
            _idleTime += dt * idleBreatheFreq * Mathf.PI * 2f;

            // 2. Gait bob + Idle reactor breathing
            float gaitBob = Mathf.Sin(_gaitTime * 2f) * bobAmp * gaitBlendAmplitude;
            float idleBob = Mathf.Sin(_idleTime) * idleBreatheAmp * (1f - gaitBlendAmplitude * 0.7f);
            float halfRoll = Mathf.Sin(_gaitTime) * 2.4f * gaitBlendAmplitude;

            // 3. Acceleration pitch lean & turn bank
            Vector3 worldAccel = (_velocity - _prevPlanarVelocity) / Mathf.Max(0.001f, dt);
            _prevPlanarVelocity = _velocity;
            Vector3 localAccel = transform.InverseTransformDirection(worldAccel);

            float targetPitch = Mathf.Clamp(localAccel.z * 0.45f, -accelLeanMaxDeg, accelLeanMaxDeg) + (currentNormalizedSpeed * 4f);
            float targetBank  = Mathf.Clamp(-localAccel.x * 0.35f, -turnBankMaxDeg, turnBankMaxDeg);
            _smoothedPitchLean = Mathf.Lerp(_smoothedPitchLean, targetPitch, 8f * dt);
            _smoothedRollBank  = Mathf.Lerp(_smoothedRollBank, targetBank + halfRoll, 8f * dt);

            // 4. Squash & stretch recovery
            if (_squashStretchTimer > 0f)
            {
                _squashStretchTimer -= dt;
                Builder.visualRoot.localScale = Vector3.Lerp(Builder.visualRoot.localScale, _targetScale, 18f * dt);
            }
            else
            {
                Builder.visualRoot.localScale = Vector3.Lerp(Builder.visualRoot.localScale, Vector3.one, 12f * dt);
            }

            // Apply bob and lean to bodyRoot
            if (Builder.bodyRoot != null)
            {
                float baseBodyY = Builder.isQuadrupedChassis ? (Builder.baseHipHeight + 0.15f) : (Builder.baseHipHeight + 0.82f);
                Builder.bodyRoot.localPosition = new Vector3(0f, baseBodyY + gaitBob + idleBob, 0f);
                Builder.bodyRoot.localRotation = Quaternion.Euler(_smoothedPitchLean, 0f, _smoothedRollBank);
            }

            // 5. Clamshell Leg Swing (opposite phase on Left vs Right / Diagonal Pairs)
            float legSwingAngle = Mathf.Sin(_gaitTime) * legSwingMaxDeg * gaitBlendAmplitude;
            foreach (var leg in Builder.leftSideLegs)
            {
                if (leg != null) leg.localRotation = Quaternion.Euler(legSwingAngle, 0f, 0f);
            }
            foreach (var leg in Builder.rightSideLegs)
            {
                if (leg != null) leg.localRotation = Quaternion.Euler(-legSwingAngle, 0f, 0f);
            }

            // 6. Clamshell Arm Counter-Swing + Weapon Recoil / Melee Slash
            if (Builder.leftArmRoot != null)
            {
                float slashAngle = _meleeSwingTimer > 0f ? Mathf.Sin((1f - _meleeSwingTimer / 0.25f) * Mathf.PI) * -75f : 0f;
                float armSwingL = -Mathf.Sin(_gaitTime) * armSwingMaxDeg * gaitBlendAmplitude + slashAngle;
                Builder.leftArmRoot.localRotation = Quaternion.Euler(armSwingL, 0f, 0f);
            }
            if (Builder.rightArmRoot != null)
            {
                float armSwingR = Mathf.Sin(_gaitTime) * (armSwingMaxDeg * 0.6f) * gaitBlendAmplitude - (_recoilOffset * 35f);
                Builder.rightArmRoot.localRotation = Quaternion.Euler(armSwingR, 0f, 0f);
            }

            // 7. Head Look Tracking (Yaws toward nearest enemy or subtle idle scan)
            if (Builder.headRoot != null)
            {
                float desiredHeadYaw = Mathf.Sin(_idleTime * 0.7f) * 14f * (1f - gaitBlendAmplitude * 0.5f);
                EnemyWalker targetEnemy = FindNearestEnemy();
                if (targetEnemy != null)
                {
                    Vector3 localToEnemy = transform.InverseTransformPoint(targetEnemy.transform.position);
                    desiredHeadYaw = Mathf.Clamp(Mathf.Atan2(localToEnemy.x, localToEnemy.z) * Mathf.Rad2Deg, -55f, 55f);
                }
                _smoothedHeadYaw = Mathf.Lerp(_smoothedHeadYaw, desiredHeadYaw, 8f * dt);
                Builder.headRoot.localRotation = Quaternion.Euler(0f, _smoothedHeadYaw, 0f);
            }
        }

        private void UpdateCombatOperation(bool firePrimary, bool slashSecondary, float dt)
        {
            _fireCooldown -= dt;
            _recoilOffset = Mathf.MoveTowards(_recoilOffset, 0f, dt * 3.5f);
            if (_meleeSwingTimer > 0f) _meleeSwingTimer -= dt;

            if (firePrimary && _fireCooldown <= 0f)
            {
                _fireCooldown = fireRate;
                _recoilOffset = 1f;
                FireRailgunProjectile();
            }

            if (slashSecondary && _meleeSwingTimer <= 0f)
            {
                _meleeSwingTimer = 0.25f;
                ExecuteRipjawSlash();
            }
        }

        private void FireRailgunProjectile()
        {
            Vector3 spawnPos = Builder.weaponMuzzle != null
                ? Builder.weaponMuzzle.position
                : transform.position + Vector3.up * 1.8f + transform.forward * 0.8f;

            Vector3 aimDir = transform.forward;
            EnemyWalker nearest = aimPointOverride == null ? FindNearestEnemy() : null;
            if (aimPointOverride != null)
            {
                aimDir = (aimPointOverride() - spawnPos).normalized;
            }
            else if (nearest != null)
            {
                aimDir = ((nearest.transform.position + Vector3.up * 0.8f) - spawnPos).normalized;
            }

            GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bolt.name = "RailgunBolt";
            bolt.transform.position = spawnPos;
            bolt.transform.rotation = Quaternion.LookRotation(aimDir);
            bolt.transform.localScale = new Vector3(0.12f, 0.12f, 0.65f);
            if (bolt.TryGetComponent<Collider>(out var col)) Destroy(col);
            MechClamshellBuilder.ApplyPaletteMaterial(bolt, IronFaunaPalette.CyanEnergy, true);

            var proj = bolt.AddComponent<RailgunProjectile>();
            proj.Init(aimDir * projectileSpeed, projectileDamage);
        }

        private void ExecuteRipjawSlash()
        {
            foreach (var enemy in EnemyWalker.ActiveEnemies.ToArray())
            {
                if (enemy == null) continue;
                if (Vector3.Distance(transform.position, enemy.transform.position) <= meleeSlashRadius)
                {
                    enemy.TakeDamage(meleeSlashDamage);
                }
            }
            Debug.Log($"[IronFauna] {name} executed Ripjaw Melee Saw-Slash!");
        }

        public void CycleLegPose(int delta)
        {
            int count = Enum.GetValues(typeof(LegPosePreset)).Length;
            int next = ((int)Builder.legPose + delta + count) % count;
            Builder.SetLegPose((LegPosePreset)next);
            Debug.Log($"[IronFauna] {name} switched Knee Pivot Pose to: {Builder.legPose}");
        }

        private void PlaceGridStructure(bool isWall)
        {
            if (GridDefenseManager.Instance == null) return;
            Vector3 ahead = transform.position + transform.forward * 2.5f;
            Vector2Int cell = new Vector2Int(
                Mathf.RoundToInt(ahead.x / GridDefenseManager.Instance.cellSize),
                Mathf.RoundToInt(ahead.z / GridDefenseManager.Instance.cellSize));

            bool placed = isWall
                ? GridDefenseManager.Instance.TryPlaceWall(cell)
                : GridDefenseManager.Instance.TryPlaceTurret(cell);

            if (placed)
            {
                Debug.Log($"[IronFauna] Placed {(isWall ? "Grid Wall" : "Auto-Turret")} at cell {cell}");
            }
        }

        private EnemyWalker FindNearestEnemy()
        {
            EnemyWalker best = null;
            float bestDist = 30f;
            foreach (var e in EnemyWalker.ActiveEnemies)
            {
                if (e == null) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null && other.name.Contains("EnergyPickup"))
            {
                pickupsCollected++;
                if (GridDefenseManager.Instance != null)
                {
                    GridDefenseManager.Instance.AddCredits(25);
                }
                Debug.Log($"[IronFauna] {name} triggered EnergyPickup! Total pickups: {pickupsCollected}");
                Destroy(other.gameObject);
            }
        }
    }

    public class RailgunProjectile : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _damage;
        private float _life = 2.0f;

        public void Init(Vector3 vel, float dmg)
        {
            _velocity = vel;
            _damage = dmg;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            transform.position += _velocity * dt;
            _life -= dt;

            foreach (var enemy in EnemyWalker.ActiveEnemies.ToArray())
            {
                if (enemy == null) continue;
                if (Vector3.Distance(transform.position, enemy.transform.position + Vector3.up * 0.8f) < 1.1f)
                {
                    enemy.TakeDamage(_damage);
                    Destroy(gameObject);
                    return;
                }
            }

            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
