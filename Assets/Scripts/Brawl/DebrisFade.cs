using UnityEngine;

namespace IronFauna.Brawl
{
    /// <summary>Shrinks a debris chunk away over its last second, then removes it (keeps the arena floor readable).</summary>
    public class DebrisFade : MonoBehaviour
    {
        public float life = 7f;
        private float _t;
        private Vector3 _scale;

        private void Start() => _scale = transform.localScale;

        private void Update()
        {
            _t += Time.deltaTime;
            float left = life - _t;
            if (left <= 0f) { Destroy(gameObject); return; }
            if (left < 1f) transform.localScale = _scale * Mathf.Max(0.01f, left);
        }
    }
}
