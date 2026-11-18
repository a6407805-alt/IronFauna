using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// Pilot camera for a MechPilotController. V toggles between:
    ///  - third person: over-the-shoulder spring arm that never passes through walls, scroll to zoom;
    ///  - first person: cockpit view from the mech's visor, with the stride and landings felt in the camera.
    /// Mouse aims (cursor locked). Boost widens the field of view; dashes and landings shake the view.
    /// Also draws a small pilot HUD: crosshair, view mode, boost energy.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MechPilotCamera : MonoBehaviour
    {
        public enum ViewMode { ThirdPerson, FirstPerson }

        public MechPilotController target;
        public ViewMode mode = ViewMode.ThirdPerson;

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel.")]
        public float mouseSensitivity = 0.09f;
        public bool invertY;

        [Header("Third person")]
        public float distance = 9f;
        public float minDistance = 4.5f;
        public float maxDistance = 15f;
        public Vector3 shoulderOffset = new Vector3(1.1f, 0.35f, 0f);
        public float followDamping = 0.07f;
        public float collisionRadius = 0.35f;

        [Header("Field of view")]
        public float thirdPersonFov = 58f;
        public float firstPersonFov = 74f;
        public float boostFovKick = 9f;

        [Header("Shake")]
        public float shakeMaxAngle = 2.6f;
        public float shakeMaxOffset = 0.18f;

        private Camera _cam;
        private Vector3 _pivotVel;
        private Vector3 _pivot;
        private float _currentDistance;
        private float _trauma;
        private float _fovExtra;
        private Transform _hiddenHead;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private Text _modeLabel;
        private RectTransform _energyFill;
        private Image _energyFillImage;
        private Text _speedLabel;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _cam.nearClipPlane = 0.05f;
            _currentDistance = distance;
            if (target != null) _pivot = PivotPoint();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            BuildHud();
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ShowHead();
        }

        private void LateUpdate()
        {
            if (target == null || target.Builder == null) return;
            float dt = Time.deltaTime;

            // ---------------- input
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                mode = mode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;
            }
            if (mouse != null)
            {
                // Click back into the game after alt-tabbing.
                if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
                    target.Yaw += d.x;
                    target.Pitch = Mathf.Clamp(target.Pitch + (invertY ? d.y : -d.y),
                        mode == ViewMode.FirstPerson ? -70f : -30f, mode == ViewMode.FirstPerson ? 70f : 55f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * 1.2f, minDistance, maxDistance);
            }

            // ---------------- shake from dashes and landings
            if (target.PendingImpact > 0f)
            {
                _trauma = Mathf.Clamp01(Mathf.Max(_trauma, target.PendingImpact));
                target.PendingImpact = 0f;
            }
            _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 1.6f);
            float shake = _trauma * _trauma;
            float t = Time.time * 22f;
            Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * (2f * shakeMaxAngle * shake);
            Vector3 shakePos = new Vector3(Mathf.PerlinNoise(t, 3.1f) - 0.5f, Mathf.PerlinNoise(5.7f, t) - 0.5f, 0f) * (2f * shakeMaxOffset * shake);

            Quaternion look = Quaternion.Euler(target.Pitch, target.Yaw, 0f);
            if (mode == ViewMode.FirstPerson) PlaceFirstPerson(look, shakeRot, shakePos);
            else PlaceThirdPerson(look, shakeRot, shakePos, dt);

            // ---------------- field of view
            float baseFov = mode == ViewMode.FirstPerson ? firstPersonFov : thirdPersonFov;
            _fovExtra = Mathf.Lerp(_fovExtra, target.Boosting ? boostFovKick : 0f, 1f - Mathf.Exp(-6f * dt));
            _cam.fieldOfView = baseFov + _fovExtra;

            UpdateHud();
        }

        /// <summary>World point under the crosshair (scenery or enemies, never the pilot's own mech).</summary>
        public Vector3 AimPoint()
        {
            Ray ray = new Ray(transform.position, transform.forward);
            float best = 300f;
            foreach (var hit in Physics.RaycastAll(ray, best, ~0, QueryTriggerInteraction.Ignore))
            {
                if (target != null && hit.collider.transform.IsChildOf(target.transform)) continue;
                best = Mathf.Min(best, hit.distance);
            }
            return ray.GetPoint(best);
        }

        private Vector3 PivotPoint()
        {
            var b = target.Builder;
            float h = b.isQuadrupedChassis ? b.HipHeight + 1.1f : b.NeckHeight;
            return target.transform.position + Vector3.up * h;
        }

        private void PlaceThirdPerson(Quaternion look, Vector3 shakeRot, Vector3 shakePos, float dt)
        {
            ShowHead();
            // Follow lags a touch behind the mech so its acceleration reads on screen.
            _pivot = Vector3.SmoothDamp(_pivot, PivotPoint(), ref _pivotVel, followDamping);
            Vector3 origin = _pivot + look * new Vector3(shoulderOffset.x, shoulderOffset.y, 0f);

            // Spring arm: pull in when scenery is between the pivot and the camera (the mech itself is ignored).
            float want = distance;
            Vector3 back = look * Vector3.back;
            var hits = Physics.SphereCastAll(origin, collisionRadius, back, want, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(target.transform)) continue;
                if (hit.distance > 0f) want = Mathf.Min(want, hit.distance);
            }
            want = Mathf.Max(want, 1.2f);
            // Snap in immediately, ease back out.
            _currentDistance = want < _currentDistance ? want : Mathf.Lerp(_currentDistance, want, 1f - Mathf.Exp(-4f * dt));

            Vector3 pos = origin + back * _currentDistance;
            pos.y = Mathf.Max(pos.y, 0.4f);
            transform.SetPositionAndRotation(pos + look * shakePos, look * Quaternion.Euler(shakeRot));
        }

        private void PlaceFirstPerson(Quaternion look, Vector3 shakeRot, Vector3 shakePos)
        {
            var b = target.Builder;
            Transform head = b.headRoot != null ? b.headRoot : target.transform;
            Transform body = b.bodyRoot != null ? b.bodyRoot : target.transform;
            HideHead(head);
            // Eye at the visor. The head rides on the torso, so stride bob, lean and landing squash come through.
            Vector3 eyeLocal = b.isQuadrupedChassis ? new Vector3(0f, 0.22f, 0.45f) : new Vector3(0f, 0.42f, 0.2f);
            Vector3 eye = head.position + body.rotation * eyeLocal;
            // A little of the torso's roll, so banking into turns and the weight shift of each step are felt.
            float roll = Mathf.DeltaAngle(0f, body.eulerAngles.z) * 0.6f;
            transform.SetPositionAndRotation(eye + look * (shakePos * 0.5f), look * Quaternion.Euler(shakeRot.x, shakeRot.y, shakeRot.z + roll));
        }

        /// <summary>The cockpit sits inside the head: hide its shell from this camera but keep its shadow.</summary>
        private void HideHead(Transform head)
        {
            if (_hiddenHead == head && _hidden.Count > 0 && _hidden[0] != null) return;
            ShowHead();
            _hiddenHead = head;
            foreach (var r in head.GetComponentsInChildren<Renderer>()) _hidden.Add(r);
            foreach (var r in _hidden) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        private void ShowHead()
        {
            foreach (var r in _hidden)
            {
                if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
            }
            _hidden.Clear();
            _hiddenHead = null;
        }

        // ---------------------------------------------------------------- HUD
        private void BuildHud()
        {
            var canvas = MenuUI.CreateCanvas("PilotHud", 5);
            canvas.transform.SetParent(transform, false);
            Transform root = canvas.transform;

            // Crosshair: a centre dot and four short ticks.
            Color line = IronFaunaPalette.WithAlpha(IronFaunaPalette.UiAccent, 0.85f);
            var dot = MenuUI.Panel(root, "CrosshairDot", line);
            MenuUI.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-2f, -2f), new Vector2(2f, 2f));
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            foreach (var d in dirs)
            {
                var tick = MenuUI.Panel(root, "CrosshairTick", line);
                Vector2 c = d * 20f;
                Vector2 half = d.x != 0f ? new Vector2(8f, 1.5f) : new Vector2(1.5f, 8f);
                MenuUI.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), c - half, c + half);
            }

            // Boost energy bar, bottom centre.
            var back = MenuUI.Panel(root, "EnergyBack", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.7f));
            MenuUI.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 190f), new Vector2(260f, 200f));
            _energyFillImage = MenuUI.Panel(back.transform, "EnergyFill", IronFaunaPalette.UiAccent);
            _energyFill = _energyFillImage.rectTransform;
            MenuUI.Place(_energyFill, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var energyLabel = MenuUI.Label(root, "BOOST", 18, IronFaunaPalette.UiTextDim, TextAnchor.LowerLeft, FontStyle.Bold);
            MenuUI.Place(energyLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 204f), new Vector2(0f, 230f));
            _speedLabel = MenuUI.Label(root, "", 18, IronFaunaPalette.UiText, TextAnchor.LowerRight, FontStyle.Bold);
            MenuUI.Place(_speedLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 204f), new Vector2(260f, 230f));

            // View mode and controls, top centre (the arena HUD owns the corners and the bottom bar).
            _modeLabel = MenuUI.Label(root, "", 16, IronFaunaPalette.UiText, TextAnchor.UpperCenter, FontStyle.Bold);
            MenuUI.Place(_modeLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-470f, -70f), new Vector2(470f, -24f));
        }

        private void UpdateHud()
        {
            if (_energyFill == null) return;
            _energyFill.anchorMax = new Vector2(Mathf.Clamp01(target.Energy01), 1f);
            _energyFillImage.color = target.Energy01 < 0.2f ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiAccent;
            Vector3 v = target.Velocity;
            v.y = 0f;
            _speedLabel.text = $"{v.magnitude * 3.6f:0} KM/H";
            _modeLabel.text = (mode == ViewMode.FirstPerson ? "FIRST PERSON" : "THIRD PERSON")
                + "\nWASD MOVE  ·  MOUSE AIM  ·  SHIFT BOOST  ·  SPACE JUMP / HOLD HOVER  ·  V CAMERA  ·  SCROLL ZOOM";
        }
    }
}
