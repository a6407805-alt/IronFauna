using UnityEngine;

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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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
}
