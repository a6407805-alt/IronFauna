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
        public float pitch = 5f;
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
        private Transform _idleBody;
        private Transform _idleHead;
        private Vector3 _idleBodyPos;
        private Quaternion _idleBodyRot;
        private Quaternion _idleHeadRot;

        public void Build()
        {
            BuildFloor();
            BuildLighting();
            BuildCamera();

            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(transform, false);
            var deck = ClamshellParts.Spawn(_turntable, "TurntableDeck", ClamshellMeshFactory.Prism(1.9f, 0.12f, 24),
                new Vector3(0f, 0.06f, 0f), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            if (IronFaunaMaterials.Realistic) deck.GetComponent<MeshRenderer>().sharedMaterial = IronFaunaMaterials.StudioDeck();
            ClamshellParts.Spawn(transform, "TurntableHazardRing", ClamshellMeshFactory.Prism(2.05f, 0.08f, 24),
                new Vector3(0f, 0.04f, 0f), Quaternion.identity, IronFaunaPalette.HazardYellow);

            var mechGo = new GameObject("PlayerMech");
            mechGo.transform.SetParent(_turntable, false);
            mechGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            Mech = mechGo.AddComponent<MechClamshellBuilder>();
            Mech.buildOnStart = false;
            Mech.showcasePose = true;

            SnapCamera();
        }

        /// <summary>Smoothly moves the camera to frame a height on the mech.</summary>
        public void Frame(float height, float dist, float offset)
        {
            _targetFocus = height;
            _targetDistance = dist;
            _targetOffset = offset;
        }

        /// <summary>Turns the turntable back so the mech faces the camera.</summary>
        public void ResetTurntable()
        {
            if (_turntable != null) _turntable.localRotation = Quaternion.Euler(0f, yaw + 180f, 0f);
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
            AnimateIdle();

            float k = 1f - Mathf.Exp(-10f * dt);
            yaw = Mathf.LerpAngle(yaw, _targetYaw, k);
            pitch = Mathf.Lerp(pitch, _targetPitch, k);
            distance = Mathf.Lerp(distance, _targetDistance, k);
            focusHeight = Mathf.Lerp(focusHeight, _targetFocus, k);
            screenOffset = Mathf.Lerp(screenOffset, _targetOffset, k);
            ApplyCamera();
        }

        /// <summary>Keeps the mech alive on the turntable: slow reactor breathing and a wandering head.</summary>
        private void AnimateIdle()
        {
            if (Mech == null || Mech.bodyRoot == null) return;
            if (Mech.bodyRoot != _idleBody)
            {
                // The mech was rebuilt: remember the new rest pose.
                _idleBody = Mech.bodyRoot;
                _idleBodyPos = _idleBody.localPosition;
                _idleBodyRot = _idleBody.localRotation;
                _idleHead = Mech.headRoot;
                _idleHeadRot = _idleHead != null ? _idleHead.localRotation : Quaternion.identity;
            }
            float t = Time.unscaledTime;
            _idleBody.localPosition = _idleBodyPos + new Vector3(0f, Mathf.Sin(t * 1.3f) * 0.012f, 0f);
            _idleBody.localRotation = _idleBodyRot * Quaternion.Euler(Mathf.Sin(t * 0.9f) * 0.5f, Mathf.Sin(t * 0.5f) * 0.9f, 0f);
            if (_idleHead != null) _idleHead.localRotation = _idleHeadRot * Quaternion.Euler(0f, Mathf.Sin(t * 0.35f) * 6f, 0f);
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
            floor.GetComponent<Renderer>().sharedMaterial = IronFaunaMaterials.Realistic
                ? IronFaunaMaterials.StudioFloor(15f)   // 4 m deck panels
                : IronFaunaMaterials.GridFloor(30f);    // 2 m cells
            var col = floor.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private void BuildLighting()
        {
            if (IronFaunaMaterials.Realistic)
            {
                BuildStudioLighting();
                return;
            }

            Light key = null;
            foreach (var l in FindObjectsByType<Light>())
            {
                if (l.type == LightType.Directional) { key = l; break; }
            }
            if (key == null)
            {
                key = new GameObject("KeyLight").AddComponent<Light>();
                key.type = LightType.Directional;
            }
            key.color = IronFaunaPalette.KeyLightWarm;
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = IronFaunaPalette.AmbientSkyCool;
            RenderSettings.ambientEquatorColor = IronFaunaPalette.AmbientEquatorMid;
            RenderSettings.ambientGroundColor = IronFaunaPalette.AmbientGroundDark;

            // The arena's volume profile pushes exposure up for the old lit materials; the toon shader
            // already sits at full brightness, so take the boost off (runtime copy, the asset is untouched).
            var volume = FindAnyObjectByType<Volume>();
            if (volume != null && volume.profile != null && volume.profile.TryGet(out ColorAdjustments adjust))
            {
                adjust.postExposure.overrideState = true;
                adjust.postExposure.value = 0f;
            }
            if (volume != null && volume.profile != null && volume.profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 1.0f;
            }

            // Fade the floor edge into the slate backdrop.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = IronFaunaPalette.ShadowSlate;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 42f;
        }

        /// <summary>
        /// Dark hangar studio for the realistic look: overhead key from the camera's left, cool rim from behind on the
        /// right, soft fill, a generated reflection environment (softboxes for the metal to catch) and filmic grading.
        /// Light directions are relative to the default camera yaw; the turntable spins, the lights do not.
        /// </summary>
        private void BuildStudioLighting()
        {
            Light key = null;
            foreach (var l in FindObjectsByType<Light>())
            {
                if (l.type == LightType.Directional) { key = l; break; }
            }
            if (key == null) key = NewDirectional("KeyLight");
            Quaternion keyRot = Quaternion.Euler(50f, yaw + 35f, 0f);
            Quaternion rimRot = Quaternion.Euler(34f, yaw + 220f, 0f);
            key.color = IronFaunaPalette.StudioKey;
            key.intensity = 1.3f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = keyRot;

            Light rim = NewDirectional("RimLight");
            rim.color = IronFaunaPalette.StudioRim;
            rim.intensity = 1.3f;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = rimRot;

            Light fill = NewDirectional("FillLight");
            fill.color = IronFaunaPalette.StudioFill;
            fill.intensity = 0.5f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(14f, yaw - 50f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = IronFaunaPalette.Lerp(IronFaunaPalette.StudioAmbientLow, IronFaunaPalette.StudioFill, 0.55f);
            RenderSettings.ambientEquatorColor = IronFaunaPalette.Lerp(IronFaunaPalette.StudioAmbientLow, IronFaunaPalette.StudioFill, 0.25f);
            RenderSettings.ambientGroundColor = IronFaunaPalette.StudioAmbientLow;

            BuildStudioReflection(keyRot * Vector3.back, rimRot * Vector3.back);

            var volume = FindAnyObjectByType<Volume>();
            if (volume == null)
            {
                volume = new GameObject("StudioVolume").AddComponent<Volume>();
                volume.transform.SetParent(transform, false);
                volume.isGlobal = true;
                volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            }
            VolumeProfile profile = volume.profile; // runtime copy, the asset is untouched
            Ensure<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = Ensure<Bloom>(profile);
            bloom.threshold.Override(1.2f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.62f);
            var grade = Ensure<ColorAdjustments>(profile);
            grade.postExposure.Override(0f);
            grade.contrast.Override(12f);
            grade.saturation.Override(-6f);
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.5f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = IronFaunaPalette.StudioBackdrop;
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 40f;
        }

        private Light NewDirectional(string lightName)
        {
            var light = new GameObject(lightName).AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.type = LightType.Directional;
            return light;
        }

        /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth in between.</summary>
        private static float Ramp(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
            component.active = true;
            return component;
        }

        /// <summary>
        /// Metal only looks like metal when it has something to reflect. Builds a small HDR cubemap of a dark hangar
        /// with two softboxes (where the key and rim lights are) and a thin horizon strip, and makes it the scene's
        /// reflection environment.
        /// </summary>
        private static void BuildStudioReflection(Vector3 toKey, Vector3 toRim)
        {
            const int n = 64;
            var cube = new Cubemap(n, TextureFormat.RGBAHalf, true) { name = "IF_StudioReflection", hideFlags = HideFlags.DontSave };
            var px = new Color[n * n];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = 2f * (x + 0.5f) / n - 1f;
                        float v = 2f * (y + 0.5f) / n - 1f;
                        Vector3 d;
                        switch ((CubemapFace)face)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1f, -v, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1f, -v, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1f, v); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1f, -v); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1f); break;
                            default: d = new Vector3(-u, -v, -1f); break;
                        }
                        d.Normalize();
                        Color c = IronFaunaPalette.StudioBackdrop * 0.5f
                            + IronFaunaPalette.StudioFill * (Ramp(-0.3f, 0.7f, d.y) * 0.35f)
                            + IronFaunaPalette.StudioFill * (Mathf.Exp(-(d.y * d.y) / 0.006f) * 0.9f)
                            + IronFaunaPalette.StudioKey * (Ramp(0.84f, 0.97f, Vector3.Dot(d, toKey)) * 4f)
                            + IronFaunaPalette.StudioKey * (Ramp(0.90f, 0.99f, d.y) * 2.5f)
                            + IronFaunaPalette.StudioRim * (Ramp(0.88f, 0.98f, Vector3.Dot(d, toRim)) * 3f);
                        c.a = 1f;
                        px[y * n + x] = c;
                    }
                }
                cube.SetPixels(px, (CubemapFace)face);
            }
            cube.Apply(true);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 1f;
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
            StageCamera.backgroundColor = IronFaunaMaterials.Realistic ? IronFaunaPalette.StudioBackdrop : IronFaunaPalette.ShadowSlate;
            StageCamera.fieldOfView = IronFaunaMaterials.Realistic ? 30f : 34f;
            StageCamera.nearClipPlane = 0.1f;
            StageCamera.farClipPlane = 200f;
            var data = StageCamera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }
}
