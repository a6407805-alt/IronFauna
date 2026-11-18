using UnityEngine;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Fight camera: low and behind the player's rear shoulder, framing both fighters, pulled back as they
    /// separate. Rides the hits (trauma shake, FOV punch) on unscaled time, so it keeps moving through hit-stop.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MechBoxingCamera : MonoBehaviour
    {
        public MechBoxer player;
        public MechBoxer opponent;
        public float baseFov = 46f;
        public float height = 4.6f;
        public float side = 2.6f;

        private Camera _cam;
        private Vector3 _pos, _vel;
        private Vector3 _look, _lookVel;
        private bool _snapped;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        public void Snap() => _snapped = false;

        private void LateUpdate()
        {
            if (player == null || opponent == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            Vector3 a = player.transform.position, b = opponent.transform.position;
            Vector3 axis = b - a;
            axis.y = 0f;
            float sep = Mathf.Max(0.5f, axis.magnitude);
            axis /= sep;
            Vector3 right = Vector3.Cross(Vector3.up, axis);

            float back = 6.2f + sep * 0.85f;
            Vector3 wantPos = a - axis * back + right * side + Vector3.up * height;
            Vector3 wantLook = Vector3.Lerp(a, b, 0.62f) + Vector3.up * 3.2f;

            if (!_snapped) { _pos = wantPos; _look = wantLook; _vel = _lookVel = Vector3.zero; _snapped = true; }
            _pos = Vector3.SmoothDamp(_pos, wantPos, ref _vel, 0.35f, Mathf.Infinity, dt);
            _look = Vector3.SmoothDamp(_look, wantLook, ref _lookVel, 0.18f, Mathf.Infinity, dt);

            float tr = BoxingFeel.Trauma * BoxingFeel.Trauma;
            float t = Time.unscaledTime * 26f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(1.7f, t) - 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f) * (0.9f * tr);
            float rollShake = (Mathf.PerlinNoise(t * 0.8f, 9.3f) - 0.5f) * 7f * tr;

            transform.position = _pos + shake;
            transform.rotation = Quaternion.LookRotation(_look - _pos) * Quaternion.Euler(0f, 0f, rollShake);
            _cam.fieldOfView = baseFov - BoxingFeel.FovKick;
        }
    }
}
