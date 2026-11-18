using UnityEngine;
using UnityEngine.InputSystem;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Keyboard + mouse (and gamepad) controls for a MechBoxer.
    ///   WASD / left stick  move (W = in, S = out, A/D = circle)
    ///   LMB / X  jab        RMB / Y  cross
    ///   Q / B    hook       E / A    uppercut
    ///   Shift / LB (hold)   high guard
    ///   Space / RB          slip (toward the A/D or stick side; alternates when neutral)
    /// </summary>
    [RequireComponent(typeof(MechBoxer))]
    public class MechBoxerPlayerInput : MonoBehaviour
    {
        private MechBoxer _boxer;
        private int _lastSlip = 1;

        private void Awake() => _boxer = GetComponent<MechBoxer>();

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
            }
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.04f) move = pad.leftStick.ReadValue();

            PunchType punch = PunchType.None;
            bool guard = false;
            bool slip = false;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) punch = PunchType.Jab;
                if (mouse.rightButton.wasPressedThisFrame) punch = PunchType.Cross;
            }
            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) punch = PunchType.Hook;
                if (kb.eKey.wasPressedThisFrame) punch = PunchType.Uppercut;
                guard |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                slip |= kb.spaceKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                if (pad.buttonWest.wasPressedThisFrame) punch = PunchType.Jab;
                if (pad.buttonNorth.wasPressedThisFrame) punch = PunchType.Cross;
                if (pad.buttonEast.wasPressedThisFrame) punch = PunchType.Hook;
                if (pad.buttonSouth.wasPressedThisFrame) punch = PunchType.Uppercut;
                guard |= pad.leftShoulder.isPressed || pad.leftTrigger.ReadValue() > 0.4f;
                slip |= pad.rightShoulder.wasPressedThisFrame;
            }

            int slipDir = 0;
            if (slip)
            {
                slipDir = move.x < -0.3f ? -1 : move.x > 0.3f ? 1 : -_lastSlip;
                _lastSlip = slipDir;
            }

            var i = _boxer.intent;
            i.move = move;
            if (punch != PunchType.None) i.punch = punch;
            i.guard = guard;
            if (slipDir != 0) i.slip = slipDir;
            _boxer.intent = i;
        }
    }
}
