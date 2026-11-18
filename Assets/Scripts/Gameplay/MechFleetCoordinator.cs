using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using IronFauna.Core;
using IronFauna.Clamshell;
using IronFauna.Input;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Coordinates the IronFauna Robot Fleet (Humanoid Mech + Quadruped Biomech),
    /// Cinemachine 3.x camera tracking, wave EnemyWalker spawning, and energy pickups.
    /// </summary>
    public class MechFleetCoordinator : MonoBehaviour
    {
        public static MechFleetCoordinator Instance { get; private set; }

        public List<MechRobotOperator> robots = new List<MechRobotOperator>();
        public int activeRobotIndex = 0;
        public CinemachineCamera cinemachineCam;

        [Header("Wave Spawner")]
        public float waveSpawnInterval = 4.5f;
        public int maxConcurrentEnemies = 4;
        private float _spawnTimer = 1.0f;
        public int waveNumber = 1;

        public MechRobotOperator ActiveRobot =>
            (robots.Count > 0 && activeRobotIndex >= 0 && activeRobotIndex < robots.Count)
                ? robots[activeRobotIndex]
                : null;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (robots.Count == 0)
            {
                robots.AddRange(FindObjectsByType<MechRobotOperator>(FindObjectsSortMode.None));
            }
            ApplyActiveRobotSelection();
            SpawnEnergyPickup(new Vector3(-2.2f, 0.6f, -0.5f));
        }

        private void Update()
        {
            if (IronFaunaInputController.Instance != null && IronFaunaInputController.Instance.swapRobotPressed)
            {
                SwapActiveRobot();
            }

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f && EnemyWalker.ActiveEnemies.Count < maxConcurrentEnemies)
            {
                _spawnTimer = waveSpawnInterval;
                SpawnWaveEnemy();
            }
        }

        public void SwapActiveRobot()
        {
            if (robots.Count == 0) return;
            activeRobotIndex = (activeRobotIndex + 1) % robots.Count;
            ApplyActiveRobotSelection();
        }

        private void ApplyActiveRobotSelection()
        {
            for (int i = 0; i < robots.Count; i++)
            {
                if (robots[i] != null)
                {
                    robots[i].isPlayerPiloted = (i == activeRobotIndex);
                }
            }

            if (cinemachineCam != null && ActiveRobot != null)
            {
                Transform target = ActiveRobot.CameraFollowProxy != null
                    ? ActiveRobot.CameraFollowProxy
                    : ActiveRobot.transform;
                cinemachineCam.Follow = target;
                cinemachineCam.LookAt = target;
            }

            if (ActiveRobot != null)
            {
                Debug.Log($"[IronFauna] Now piloting: {ActiveRobot.name} (Pose: {ActiveRobot.Builder.legPose})");
            }
        }

        private void SpawnWaveEnemy()
        {
            float angle = UnityEngine.Random.Range(-70f, 70f) * Mathf.Deg2Rad;
            Vector3 spawnPos = new Vector3(Mathf.Sin(angle) * 11f, 0f, -Mathf.Cos(angle) * 10.5f);

            GameObject enemyGo = new GameObject($"EnemyBiomech_W{waveNumber}");
            enemyGo.transform.position = spawnPos;

            // Assemble an insectoid/predatory enemy biomech using the Clamshell Builder
            var builder = enemyGo.AddComponent<MechClamshellBuilder>();
            builder.isQuadrupedChassis = true;
            builder.legPose = LegPosePreset.Pose4_ChickenBendBackward;
            builder.thighFrontSet = ClamshellSetArchetype.Set09_Mantis;
            builder.calfFrontSet = ClamshellSetArchetype.Set04_Ripjaw;
            builder.thighLength = 0.65f;
            builder.calfLength = 0.65f;
            builder.collarWidth = 0.24f;
            builder.jointRadius = 0.13f;
            builder.RebuildMech();

            var walker = enemyGo.AddComponent<EnemyWalker>();
            walker.maxHp = 50f;
            walker.moveSpeed = 2.8f;

            var anim = enemyGo.AddComponent<MechRobotOperator>();
            anim.isPlayerPiloted = false;
            anim.aiWingmanActive = false; // Driven by EnemyWalker movement
            enemyGo.AddComponent<MechGaitAnimator>(); // planted feet and footfalls, same as the player's mech
        }

        private void SpawnEnergyPickup(Vector3 pos)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "EnergyPickup_CreditOrb";
            orb.transform.position = pos;
            orb.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            if (orb.TryGetComponent<SphereCollider>(out var col))
            {
                col.isTrigger = true;
                col.radius = 1.2f;
            }
            MechClamshellBuilder.ApplyPaletteMaterial(orb, IronFaunaPalette.OrangeJointCore, true);
        }
    }
}
