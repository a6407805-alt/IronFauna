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
        public int playerIndex;
        public float baseFov = 48f;
        public float height = 4.8f;
        public float side = 2.6f;

        private Camera _cam;
        private Vector3 _pos, _vel, _look, _lookVel;
        private bool _snapped;
        private float _yaw;
        public float mouseSensitivity = 0.12f;
        [Tooltip("Off: the camera never frames the opponent; the mouse / right stick always orbits it")]
        public bool lockOn = false;

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
            bool framing = lockOn && target != null && player.ghost.FightBlend > 0.4f;
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
                // roaming: the mouse turns P1's camera; each split-screen player's gamepad right stick turns their camera
                if (playerIndex == 0)
                {
                    var mouse = Mouse.current;
                    if (mouse != null && Cursor.lockState == CursorLockMode.Locked) _yaw += mouse.delta.ReadValue().x * mouseSensitivity;
                }
                var pads = Gamepad.all;
                Gamepad pad = playerIndex < pads.Count ? pads[playerIndex] : null;
                if (pad != null && pad.rightStick.ReadValue().sqrMagnitude > 0.02f)
                {
                    _yaw += pad.rightStick.ReadValue().x * 140f * dt;
                }
                else if (playerIndex > 0 && player.ghost != null && player.ghost.Velocity.sqrMagnitude > 0.4f)
                {
                    float moveYaw = Mathf.Atan2(player.ghost.Velocity.x, player.ghost.Velocity.z) * Mathf.Rad2Deg;
                    _yaw = Mathf.LerpAngle(_yaw, moveYaw, 1f - Mathf.Exp(-2.5f * dt));
                }
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
            // don't bury the camera in a hill or a rock: pull it in along the line from the mech
            Vector3 camPos = _pos;
            Vector3 from = new Vector3(a.x, floor + 3f, a.z);
            Vector3 toCam = camPos - from;
            float len = toCam.magnitude;
            if (len > 0.1f)
            {
                float free = len;
                foreach (var hit in Physics.SphereCastAll(from, 0.45f, toCam / len, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance <= 0f) continue;
                    var rb = hit.collider.attachedRigidbody;
                    if (rb != null && rb.GetComponent<MechRagdollPart>() != null) continue; // ignore mechs, pull in on terrain, buildings & trees
                    free = Mathf.Min(free, hit.distance);
                }
                camPos = from + toCam / len * Mathf.Max(1.5f, free);
            }
            transform.position = camPos + shake;
            transform.rotation = Quaternion.LookRotation(_look - camPos) * Quaternion.Euler(0f, 0f, roll);
            _cam.fieldOfView = baseFov - BoxingFeel.FovKick;
        }
    }
}
