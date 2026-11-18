using UnityEngine;
using UnityEngine.InputSystem;
using IronFauna.Boxing;

namespace IronFauna.Brawl
{
    /// <summary>
    /// The boxing camera, for the brawler: low behind the player's mech, framing the opponent it is locked on to,
    /// pulled back as they separate; rides hits with trauma shake and FOV punch on unscaled time.
    /// Follows the physical bodies (pelvis), not the hidden animation ghosts. Takes a viewport rect for split screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MechBrawlCamera : MonoBehaviour
    {
        public MechRagdoll player;
        public MechRagdoll target;
        public float baseFov = 48f;
        public float height = 4.8f;
        public float side = 2.6f;

        private Camera _cam;
        private Vector3 _pos, _vel, _look, _lookVel;
        private bool _snapped;
        private float _yaw;
        public float mouseSensitivity = 0.12f;

        private void Awake() => _cam = GetComponent<Camera>();

        public void Snap() => _snapped = false;

        private void LateUpdate()
        {
            if (player == null) return;
            if (!_snapped) _yaw = player.ghost.transform.eulerAngles.y;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            Vector3 a = player.Position;
            a.y = 0f;
            // frame the locked-on opponent while squared up; otherwise sit behind where the mech is heading
            bool framing = target != null && player.ghost.FightBlend > 0.4f;
            Vector3 axis;
            float sep;
            Vector3 b;
            if (framing)
            {
                b = target.Position;
                b.y = 0f;
                axis = b - a;
                sep = Mathf.Max(0.5f, axis.magnitude);
                axis /= sep;
                _yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-6f * dt));
            }
            else
            {
                // roaming: the mouse turns the camera; the mech walks wherever it is steered relative to it
                var mouse = Mouse.current;
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked) _yaw += mouse.delta.ReadValue().x * mouseSensitivity;
                var pad = Gamepad.current;
                if (pad != null) _yaw += pad.rightStick.ReadValue().x * 140f * dt;
                axis = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                sep = 5f;
                b = a + axis * 5f;
            }
            Vector3 right = Vector3.Cross(Vector3.up, axis);
            float floor = player.ghost.transform.position.y;

            float back = framing ? 6.2f + Mathf.Min(sep, 12f) * 0.85f : 9f;
            Vector3 wantPos = a - axis * back + right * side + Vector3.up * (floor + height);
            Vector3 wantLook = Vector3.Lerp(a, b, framing ? Mathf.Min(0.62f, 6f / sep) : 0.55f) + Vector3.up * (floor + 3.2f);

            if (!_snapped) { _pos = wantPos; _look = wantLook; _vel = _lookVel = Vector3.zero; _snapped = true; }
            _pos = Vector3.SmoothDamp(_pos, wantPos, ref _vel, 0.35f, Mathf.Infinity, dt);
            _look = Vector3.SmoothDamp(_look, wantLook, ref _lookVel, 0.18f, Mathf.Infinity, dt);

            float tr = BoxingFeel.Trauma * BoxingFeel.Trauma;
            float t = Time.unscaledTime * 26f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(1.7f, t) - 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f) * (0.9f * tr);
            float roll = (Mathf.PerlinNoise(t * 0.8f, 9.3f) - 0.5f) * 7f * tr;
            transform.position = _pos + shake;
            transform.rotation = Quaternion.LookRotation(_look - _pos) * Quaternion.Euler(0f, 0f, roll);
            _cam.fieldOfView = baseFov - BoxingFeel.FovKick;
        }
    }
}
