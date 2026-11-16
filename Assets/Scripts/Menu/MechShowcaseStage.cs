using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFauna.Clamshell;
using IronFauna.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IronFauna.Menu
{
    /// <summary>
    /// The 3D half of the menus: blueprint-grid floor, hazard-ringed turntable, key light,
    /// the player's mech, and an orbit camera that can auto-spin (title) or be dragged (garage).
    /// </summary>
    public class MechShowcaseStage : MonoBehaviour
    {
        public MechClamshellBuilder Mech { get; private set; }
        public Camera StageCamera { get; private set; }

        [Header("Orbit")]
        public float yaw = 205f;
        public float pitch = 10f;
        public float distance = 9f;
        public float focusHeight = 2.4f;
        [Tooltip("Shifts the framing so the mech sits right of centre, leaving room for UI on the left.")]
        public float screenOffset = 1.6f;
        public bool autoSpin = true;
        public bool allowUserOrbit;
        public float autoSpinSpeed = 14f;

        private Transform _turntable;
        private float _targetYaw;
        private float _targetPitch;
        private float _targetDistance;
        private float _targetFocus;
        private float _targetOffset;
        private bool _dragging;

        public void Build()
        {
            BuildFloor();
            BuildLighting();
            BuildCamera();

            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(transform, false);
            ClamshellParts.Spawn(_turntable, "TurntableDeck", ClamshellMeshFactory.Prism(1.9f, 0.12f, 24),
                new Vector3(0f, 0.06f, 0f), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            ClamshellParts.Spawn(transform, "TurntableHazardRing", ClamshellMeshFactory.Prism(2.05f, 0.08f, 24),
                new Vector3(0f, 0.04f, 0f), Quaternion.identity, IronFaunaPalette.HazardYellow);

            var mechGo = new GameObject("PlayerMech");
            mechGo.transform.SetParent(_turntable, false);
            mechGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            Mech = mechGo.AddComponent<MechClamshellBuilder>();
            Mech.buildOnStart = false;

            SnapCamera();
        }

        /// <summary>Smoothly moves the camera to frame a height on the mech.</summary>
        public void Frame(float height, float dist, float offset)
        {
            _targetFocus = height;
            _targetDistance = dist;
            _targetOffset = offset;
        }

        public void SnapCamera()
        {
            _targetYaw = yaw;
            _targetPitch = pitch;
            _targetDistance = distance;
            _targetFocus = focusHeight;
            _targetOffset = screenOffset;
            ApplyCamera();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (autoSpin && _turntable != null)
            {
                _turntable.Rotate(0f, autoSpinSpeed * dt, 0f, Space.World);
            }

            if (allowUserOrbit) HandleOrbitInput();

            float k = 1f - Mathf.Exp(-10f * dt);
            yaw = Mathf.LerpAngle(yaw, _targetYaw, k);
            pitch = Mathf.Lerp(pitch, _targetPitch, k);
            distance = Mathf.Lerp(distance, _targetDistance, k);
            focusHeight = Mathf.Lerp(focusHeight, _targetFocus, k);
            screenOffset = Mathf.Lerp(screenOffset, _targetOffset, k);
            ApplyCamera();
        }

        private void HandleOrbitInput()
        {
            bool pressed = false;
            bool pressedThisFrame = false;
            Vector2 delta = Vector2.zero;
            float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                pressed = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
                pressedThisFrame = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
                delta = mouse.delta.ReadValue() * 0.2f;
                float wheel = mouse.scroll.ReadValue().y;
                scroll = Mathf.Abs(wheel) > 0.01f ? Mathf.Sign(wheel) : 0f; // platforms report different units per notch
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.rightStick.ReadValue();
                _targetYaw += stick.x * 120f * Time.unscaledDeltaTime;
                _targetPitch = Mathf.Clamp(_targetPitch - stick.y * 60f * Time.unscaledDeltaTime, -5f, 40f);
            }
#else
            pressed = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            pressedThisFrame = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 4f;
            scroll = Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f ? Mathf.Sign(Input.mouseScrollDelta.y) : 0f;
#endif
            if (pressedThisFrame)
            {
                // Only start a drag when the press did not land on UI.
                _dragging = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            }
            if (!pressed) _dragging = false;

            if (_dragging)
            {
                _targetYaw += delta.x;
                _targetPitch = Mathf.Clamp(_targetPitch - delta.y * 0.5f, -5f, 40f);
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Mathf.Abs(scroll) > 0.01f && !overUi)
            {
                _targetDistance = Mathf.Clamp(_targetDistance - scroll * 0.6f, 2.5f, 14f);
            }
        }

        private void ApplyCamera()
        {
            if (StageCamera == null) return;
            Vector3 focus = new Vector3(0f, focusHeight, 0f);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus + orbit * new Vector3(0f, 0f, -distance);
            Transform cam = StageCamera.transform;
            cam.position = pos;
            cam.rotation = Quaternion.LookRotation(focus - pos, Vector3.up);
            // Slide sideways so the mech sits to the right of the UI column.
            cam.position -= cam.right * screenOffset;
        }

        private void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "BlueprintFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(6f, 1f, 6f); // 60 m
            floor.GetComponent<Renderer>().sharedMaterial = IronFaunaMaterials.GridFloor(30f); // 2 m cells
            var col = floor.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private void BuildLighting()
        {
            Light key = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { key = l; break; }
            }
            if (key == null)
            {
                key = new GameObject("KeyLight").AddComponent<Light>();
                key.type = LightType.Directional;
            }
            key.color = IronFaunaPalette.KeyLightWarm;
            key.intensity = 1.25f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = IronFaunaPalette.AmbientSkyCool;
            RenderSettings.ambientEquatorColor = IronFaunaPalette.AmbientEquatorMid;
            RenderSettings.ambientGroundColor = IronFaunaPalette.AmbientGroundDark;

            // Fade the floor edge into the slate backdrop.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = IronFaunaPalette.ShadowSlate;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 42f;
        }

        private void BuildCamera()
        {
            StageCamera = Camera.main;
            if (StageCamera == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                StageCamera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            StageCamera.clearFlags = CameraClearFlags.SolidColor;
            StageCamera.backgroundColor = IronFaunaPalette.ShadowSlate;
            StageCamera.fieldOfView = 34f;
            StageCamera.nearClipPlane = 0.1f;
            StageCamera.farClipPlane = 200f;
            var data = StageCamera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }
}
