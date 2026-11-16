using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace IronFauna.Gameplay
{
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

        /// <summary>True once the killing blow has landed (Destroy only happens at the end of the frame).</summary>
        public bool IsDead { get; private set; }

        private const float RepathInterval = 0.5f;

        private float _attackCooldown;
        private float _repathTimer;
        private NavMeshAgent _agent;

        private void OnEnable()
        {
            ActiveEnemies.Add(this);
            currentHp = maxHp;
            IsDead = false;
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
            if (IsDead) return;
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
                return;
            }

            if (_agent != null && _agent.isOnNavMesh)
            {
                // Re-path on a timer instead of every frame.
                _repathTimer -= Time.deltaTime;
                if (_repathTimer <= 0f || !_agent.hasPath)
                {
                    _repathTimer = RepathInterval;
                    _agent.SetDestination(targetPos);
                }
            }
            else
            {
                Vector3 dir = targetPos - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    dir.Normalize();
                    transform.position += dir * (moveSpeed * Time.deltaTime);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
                }
            }
        }

        public void TakeDamage(float dmg)
        {
            if (IsDead) return; // several hits can land in the same frame; only the first kill pays out
            currentHp -= dmg;
            if (currentHp <= 0f)
            {
                IsDead = true;
                ActiveEnemies.Remove(this);
                if (GridDefenseManager.Instance != null)
                {
                    GridDefenseManager.Instance.AddCredits(creditReward);
                }
                Destroy(gameObject);
            }
        }
    }
}
