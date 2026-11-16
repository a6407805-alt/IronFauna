using UnityEngine;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Automated grid defense turret that targets the nearest living EnemyWalker in range.
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

            Vector3 dir = nearest.transform.position - transform.position;
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
                if (enemy == null || enemy.IsDead) continue;
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
}
