using UnityEngine;
using UnityEngine.InputSystem;

namespace IronFauna.Input
{
    public enum ControlSchemeOption
    {
        AutoDetect = 0,
        KeyboardAndMouse = 1,
        PS5DualSense = 2,
        XboxController = 3
    }

    /// <summary>
    /// Unified input manager supporting Keyboard & Mouse, PS5 DualSense, and Xbox Wireless Controllers
    /// via Unity's Input System, with runtime scheme switching, deadzones, sensitivity, and prompt glyphs.
    /// </summary>
    public class IronFaunaInputController : MonoBehaviour
    {
        public static IronFaunaInputController Instance { get; private set; }

        [Header("Control Scheme & Options")]
        public ControlSchemeOption selectedScheme = ControlSchemeOption.AutoDetect;
        public ControlSchemeOption activeResolvedScheme = ControlSchemeOption.KeyboardAndMouse;

        [Range(0.1f, 5.0f)] public float mouseSensitivity = 1.2f;
        [Range(30f, 300f)]  public float controllerLookSensitivity = 135f;
        [Range(0.05f, 0.4f)] public float stickDeadzone = 0.15f;
        public bool invertY = false;

        [Header("Live Input State (Read-Only)")]
        public Vector2 moveInput;
        public Vector2 lookDelta;
        public bool isSprinting;
        public bool jumpPressed;
        public bool primaryFireHeld;
        public bool secondarySlashPressed;
        public bool nextPosePressed;
        public bool prevPosePressed;
        public bool swapRobotPressed;
        public bool placeWallPressed;
        public bool placeTurretPressed;
        public bool toggleOptionsPressed;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            ResolveActiveScheme();
            ReadInputs();
        }

        public void SetControlScheme(ControlSchemeOption scheme)
        {
            selectedScheme = scheme;
            ResolveActiveScheme();
            Debug.Log($"[IronFaunaInput] Control scheme set to: {selectedScheme} (Active: {activeResolvedScheme})");
        }

        private void ResolveActiveScheme()
        {
            if (selectedScheme != ControlSchemeOption.AutoDetect)
            {
                activeResolvedScheme = selectedScheme;
                return;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null && WasGamepadActuatedThisFrame(pad))
            {
                string desc = $"{pad.displayName} {pad.layout} {pad.description.manufacturer}".ToLowerInvariant();
                if (desc.Contains("dualsense") || desc.Contains("dualshock") || desc.Contains("playstation") || desc.Contains("sony"))
                {
                    activeResolvedScheme = ControlSchemeOption.PS5DualSense;
                }
                else
                {
                    activeResolvedScheme = ControlSchemeOption.XboxController;
                }
                return;
            }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if ((kb != null && kb.anyKey.wasPressedThisFrame) ||
                (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)))
            {
                activeResolvedScheme = ControlSchemeOption.KeyboardAndMouse;
            }
        }

        private bool WasGamepadActuatedThisFrame(Gamepad pad)
        {
            return pad.leftStick.ReadValue().sqrMagnitude > stickDeadzone * stickDeadzone ||
                   pad.rightStick.ReadValue().sqrMagnitude > stickDeadzone * stickDeadzone ||
                   pad.buttonSouth.wasPressedThisFrame ||
                   pad.buttonNorth.wasPressedThisFrame ||
                   pad.buttonWest.wasPressedThisFrame ||
                   pad.buttonEast.wasPressedThisFrame ||
                   pad.rightTrigger.ReadValue() > 0.2f ||
                   pad.leftTrigger.ReadValue() > 0.2f ||
                   pad.dpad.ReadValue().sqrMagnitude > 0.1f;
        }

        private void ReadInputs()
        {
            moveInput = Vector2.zero;
            lookDelta = Vector2.zero;
            isSprinting = false;
            jumpPressed = false;
            primaryFireHeld = false;
            secondarySlashPressed = false;
            nextPosePressed = false;
            prevPosePressed = false;
            swapRobotPressed = false;
            placeWallPressed = false;
            placeTurretPressed = false;
            toggleOptionsPressed = false;

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;

            bool readKbm = selectedScheme == ControlSchemeOption.AutoDetect || selectedScheme == ControlSchemeOption.KeyboardAndMouse;
            bool readPad = selectedScheme == ControlSchemeOption.AutoDetect ||
                           selectedScheme == ControlSchemeOption.PS5DualSense ||
                           selectedScheme == ControlSchemeOption.XboxController;

            if (readKbm && kb != null)
            {
                float mx = 0f, my = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  mx -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mx += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  my -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    my += 1f;

                Vector2 kbmMove = new Vector2(mx, my);
                if (kbmMove.sqrMagnitude > 1f) kbmMove.Normalize();
                moveInput += kbmMove;

                isSprinting |= kb.leftShiftKey.isPressed;
                jumpPressed |= kb.spaceKey.wasPressedThisFrame;
                secondarySlashPressed |= kb.fKey.wasPressedThisFrame;
                prevPosePressed |= kb.qKey.wasPressedThisFrame;
                nextPosePressed |= kb.eKey.wasPressedThisFrame;
                swapRobotPressed |= kb.tabKey.wasPressedThisFrame;
                placeWallPressed |= kb.digit1Key.wasPressedThisFrame;
                placeTurretPressed |= kb.digit2Key.wasPressedThisFrame;
                toggleOptionsPressed |= kb.escapeKey.wasPressedThisFrame || kb.oKey.wasPressedThisFrame;
            }

            if (readKbm && mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 rawDelta = mouse.delta.ReadValue() * (mouseSensitivity * 0.15f);
                    if (invertY) rawDelta.y = -rawDelta.y;
                    lookDelta += rawDelta;
                }
                primaryFireHeld |= mouse.leftButton.isPressed;
                secondarySlashPressed |= mouse.middleButton.wasPressedThisFrame;
            }

            if (readPad && pad != null)
            {
                // Left Stick Move (with radial deadzone)
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude > stickDeadzone)
                {
                    moveInput += Vector2.ClampMagnitude(ls, 1f);
                }

                // Right Stick Look (PS5 R3 / Xbox RS)
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.magnitude > stickDeadzone)
                {
                    Vector2 padLook = rs * (controllerLookSensitivity * Time.deltaTime);
                    if (invertY) padLook.y = -padLook.y;
                    lookDelta += padLook;
                }

                // Triggers & Shoulders:
                // PS5: R2 Fire, L2/Square Slash, L1/L3 Sprint, Cross Jump, Triangle Swap Robot
                // Xbox: RT Fire, LT/X Slash, LB/LS Sprint, A Jump, Y Swap Robot
                primaryFireHeld |= pad.rightTrigger.ReadValue() > 0.25f || pad.rightShoulder.isPressed;
                secondarySlashPressed |= pad.leftTrigger.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
                isSprinting |= pad.leftShoulder.isPressed || pad.leftStickButton.isPressed;
                jumpPressed |= pad.buttonSouth.wasPressedThisFrame;
                swapRobotPressed |= pad.buttonNorth.wasPressedThisFrame;

                // D-Pad: Left/Right = Cycle Leg Pivot Pose; Up = Place Wall; Down = Place Auto-Turret
                prevPosePressed |= pad.dpad.left.wasPressedThisFrame;
                nextPosePressed |= pad.dpad.right.wasPressedThisFrame;
                placeWallPressed |= pad.dpad.up.wasPressedThisFrame;
                placeTurretPressed |= pad.dpad.down.wasPressedThisFrame;

                toggleOptionsPressed |= pad.startButton.wasPressedThisFrame;
            }

            moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }

        public string GetActiveBindingPrompts()
        {
            switch (activeResolvedScheme)
            {
                case ControlSchemeOption.PS5DualSense:
                    return "PS5 DUALSENSE: [L3] Move · [R3] Aim · [L1/L3] Boost · [Cross] Thruster Jump · [R2] Railgun · [L2/Square] Saw-Slash · [D-Pad L/R] Knee Pivot Pose · [D-Pad Up/Down] Wall/Turret · [Triangle] Swap Robot";
                case ControlSchemeOption.XboxController:
                    return "XBOX CONTROLLER: [LS] Move · [RS] Aim · [LB/LS] Boost · [A] Thruster Jump · [RT] Railgun · [LT/X] Saw-Slash · [D-Pad L/R] Knee Pivot Pose · [D-Pad Up/Down] Wall/Turret · [Y] Swap Robot";
                default:
                    return "KEYBOARD & MOUSE: [WASD] Move · [Hold RMB] Orbit Camera · [Shift] Boost · [Space] Jump · [LMB] Railgun · [F] Saw-Slash · [Q/E] Knee Pivot Pose · [1/2] Wall/Turret · [Tab] Swap Robot";
            }
        }
    }
}
