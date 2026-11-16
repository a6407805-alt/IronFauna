using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using IronFauna.Core;
using IronFauna.Clamshell;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Central Objective (Reactor Core) that wave enemies push toward.
    /// </summary>
    public class ObjectiveCore : MonoBehaviour
    {
        public static ObjectiveCore Instance { get; private set; }

        public float maxHp = 500f;
        public float currentHp = 500f;
        public bool isDestroyed => currentHp <= 0f;

        private void Awake()
        {
            Instance = this;
            currentHp = maxHp;
        }

        public void TakeDamage(float amount)
        {
            if (isDestroyed) return;
            currentHp = Mathf.Max(0f, currentHp - amount);
            Debug.Log($"[IronFauna] Objective HP: {currentHp}/{maxHp}");
            if (currentHp <= 0f)
            {
                Debug.Log("[IronFauna] OBJECTIVE DESTROYED — GAME OVER!");
            }
        }
    }

    /// <summary>
    /// Wave enemy biomech walker that pathfinds toward the ObjectiveCore and drops credits on death.
    /// </summary>
    public class EnemyWalker : MonoBehaviour
    {
        public static readonly List<EnemyWalker> ActiveEnemies = new List<EnemyWalker>();

        public float maxHp = 60f;
        public float currentHp = 60f;
        public float moveSpeed = 3.2f;
        public float attackDamage = 12f;
        public float attackRange = 2.4f;
        public int creditReward = 10;
        public bool isElite = false;

        private float _attackCooldown;
        private NavMeshAgent _agent;

        private void OnEnable()
        {
            ActiveEnemies.Add(this);
            currentHp = maxHp;
            if (TryGetComponent(out _agent))
            {
                _agent.speed = moveSpeed;
            }
        }

        private void OnDisable()
        {
            ActiveEnemies.Remove(this);
        }

        private void Update()
        {
            if (ObjectiveCore.Instance == null || ObjectiveCore.Instance.isDestroyed) return;

            Vector3 targetPos = ObjectiveCore.Instance.transform.position;
            float dist = Vector3.Distance(transform.position, targetPos);

            if (dist <= attackRange)
            {
                _attackCooldown -= Time.deltaTime;
                if (_attackCooldown <= 0f)
                {
                    _attackCooldown = 1.2f;
                    ObjectiveCore.Instance.TakeDamage(attackDamage);
                }
            }
            else
            {
                if (_agent != null && _agent.isOnNavMesh)
                {
                    _agent.SetDestination(targetPos);
                }
                else
                {
                    Vector3 dir = (targetPos - transform.position).normalized;
                    dir.y = 0f;
                    transform.position += dir * (moveSpeed * Time.deltaTime);
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
                    }
                }
            }
        }

        public void TakeDamage(float dmg)
        {
            currentHp -= dmg;
            if (currentHp <= 0f)
            {
                if (GridDefenseManager.Instance != null)
                {
                    GridDefenseManager.Instance.AddCredits(creditReward);
                }
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Automated grid defense turret that targets the nearest EnemyWalker in range.
    /// </summary>
    public class AutoTurret : MonoBehaviour
    {
        public float range = 12f;
        public float fireInterval = 0.75f;
        public float damagePerShot = 18f;
        public Transform turretHead;

        private float _cooldown;

        private void Update()
        {
            _cooldown -= Time.deltaTime;
            EnemyWalker nearest = FindNearestEnemy();
            if (nearest == null) return;

            Vector3 dir = (nearest.transform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f && turretHead != null)
            {
                turretHead.rotation = Quaternion.Slerp(turretHead.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
            }

            if (_cooldown <= 0f)
            {
                _cooldown = fireInterval;
                nearest.TakeDamage(damagePerShot);
            }
        }

        private EnemyWalker FindNearestEnemy()
        {
            EnemyWalker best = null;
            float bestDist = range;
            foreach (var enemy in EnemyWalker.ActiveEnemies)
            {
                if (enemy == null) continue;
                float d = Vector3.Distance(transform.position, enemy.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = enemy;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// M1-B Grid Defense & Economy Manager (Walls = 30 credits, Auto-Turrets = 150 credits, Starting Budget = 300).
    /// </summary>
    public class GridDefenseManager : MonoBehaviour
    {
        public static GridDefenseManager Instance { get; private set; }

        public int credits = 300;
        public int wallCost = 30;
        public int turretCost = 150;
        public float cellSize = 2.0f;

        private readonly HashSet<Vector2Int> _occupiedCells = new HashSet<Vector2Int>();

        private void Awake()
        {
            Instance = this;
        }

        public void AddCredits(int amount)
        {
            credits += amount;
            Debug.Log($"[IronFauna] +{amount} Credits! Total Credits: {credits}");
        }

        public bool TryPlaceWall(Vector2Int cell)
        {
            if (_occupiedCells.Contains(cell) || credits < wallCost) return false;
            credits -= wallCost;
            _occupiedCells.Add(cell);

            Vector3 worldPos = new Vector3(cell.x * cellSize, 0.75f, cell.y * cellSize);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"GridWall_{cell.x}_{cell.y}";
            wall.transform.position = worldPos;
            wall.transform.localScale = new Vector3(cellSize * 0.92f, 1.5f, cellSize * 0.92f);
            MechClamshellBuilder.ApplyPaletteMaterial(wall, IronFaunaPalette.GunmetalFrame, false);

            var obstacle = wall.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            return true;
        }

        public bool TryPlaceTurret(Vector2Int cell)
        {
            if (_occupiedCells.Contains(cell) || credits < turretCost) return false;
            credits -= turretCost;
            _occupiedCells.Add(cell);

            Vector3 worldPos = new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
            GameObject turretRoot = new GameObject($"GridAutoTurret_{cell.x}_{cell.y}");
            turretRoot.transform.position = worldPos;

            GameObject baseBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBlock.transform.SetParent(turretRoot.transform, false);
            baseBlock.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            baseBlock.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
            MechClamshellBuilder.ApplyPaletteMaterial(baseBlock, IronFaunaPalette.MaroonRear, false);

            GameObject headBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headBlock.transform.SetParent(turretRoot.transform, false);
            headBlock.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            headBlock.transform.localScale = new Vector3(0.8f, 0.5f, 1.4f);
            MechClamshellBuilder.ApplyPaletteMaterial(headBlock.gameObject, IronFaunaPalette.CrimsonFront, false);

            var turret = turretRoot.AddComponent<AutoTurret>();
            turret.turretHead = headBlock.transform;

            var obstacle = turretRoot.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            return true;
        }
    }
}
