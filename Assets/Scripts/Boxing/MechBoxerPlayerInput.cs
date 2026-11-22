using UnityEngine;
using UnityEngine.InputSystem;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Keyboard + mouse (and multi-gamepad split-screen) controls for a MechBoxer.
    ///   WASD / left stick   move, relative to the camera
    ///   Ctrl / L3 (hold)    sprint (stands up from floor sit)
    ///   X / Alt (hold)      MMA crouch (ducks high swings, sets up takedowns & low drags)
    ///   Z / D-pad Down      Jiu-Jitsu floor sit toggle (butt-scoot with WASD, drag grabbed bodies on the floor, upkick with F/R)
    ///   LMB / X  jab        RMB / Y  cross
    ///   Q / B    hook       E / A    uppercut
    ///   F front kick        R roundhouse      V low calf kick
    ///   Shift / LB (hold)   high guard
    ///   G / LT (hold)       grab - drag on the floor with Z/X, LMB throw, RMB joint-crank/tear limb, Space suplex/slam,
    ///                       E powerbomb, Q chokeslam, F piledriver, R giant swing
    ///   Scroll / D-pad L/R  pick a martial-arts special (20 moves)      Space / RT   throw it
    ///   C / RB              slip
    /// </summary>
    [RequireComponent(typeof(MechBoxer))]
    public class MechBoxerPlayerInput : MonoBehaviour
    {
        [Tooltip("0 = Player 1 (WASD/Mouse or Pad 0), 1..3 = Local Co-Op Players 2..4 (Gamepads or IJKL/Arrows for P2)")]
        public int playerIndex;
        public bool allowMouseAndWasd = true;

        private MechBoxer _boxer;
        private MechBoxerAI _idleAi;
        private int _lastSlip = 1;
        private float _scrollAcc;
        private bool _sitToggle;
        private float _idleTime;
        public bool autoPilot;

        /// <summary>Index into MechBoxer.Specials of the special the scroll wheel has picked.</summary>
        public int SpecialIndex { get; private set; }
        public PunchType SelectedSpecial => MechBoxer.Specials[SpecialIndex];
        public event System.Action<PunchType> SpecialChanged;

        private void Cycle(int d)
        {
            int n = MechBoxer.Specials.Length;
            SpecialIndex = ((SpecialIndex + d) % n + n) % n;
            SpecialChanged?.Invoke(SelectedSpecial);
        }

        public void SelectMove(PunchType t)
        {
            var list = MechBoxer.Specials;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == t) { SpecialIndex = i; return; }
            }
        }

        private void Awake()
        {
            _boxer = GetComponent<MechBoxer>();
            _idleAi = GetComponent<MechBoxerAI>();
            if (_idleAi == null)
            {
                _idleAi = gameObject.AddComponent<MechBoxerAI>();
                _idleAi.skill = 0.72f;
                _idleAi.enabled = false;
            }
        }

        private void Start()
        {
            if (_boxer.moveReference == null && Camera.main != null) _boxer.moveReference = Camera.main.transform;
        }

        private Gamepad GetAssignedPad()
        {
            var pads = Gamepad.all;
            if (pads.Count == 0) return null;
            if (playerIndex == 0) return pads.Count == 1 && allowMouseAndWasd ? pads[0] : (pads.Count > 0 && !allowMouseAndWasd ? pads[0] : (pads.Count == 1 ? pads[0] : null));
            int idx = allowMouseAndWasd ? playerIndex - 1 : playerIndex;
            return idx >= 0 && idx < pads.Count ? pads[idx] : null;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = GetAssignedPad();
            Vector2 move = Vector2.zero;
            PunchType punch = PunchType.None;
            bool guard = false;
            bool slip = false;
            bool sprint = false;
            bool grab = false;
            bool crouch = false;

            if (playerIndex == 0 && allowMouseAndWasd)
            {
                if (kb != null)
                {
                    if (kb.wKey.isPressed) move.y += 1f;
                    if (kb.sKey.isPressed) move.y -= 1f;
                    if (kb.dKey.isPressed) move.x += 1f;
                    if (kb.aKey.isPressed) move.x -= 1f;
                    if (kb.qKey.wasPressedThisFrame) punch = PunchType.Hook;
                    if (kb.eKey.wasPressedThisFrame) punch = PunchType.Uppercut;
                    if (kb.fKey.wasPressedThisFrame) punch = PunchType.FrontKick;
                    if (kb.rKey.wasPressedThisFrame) punch = PunchType.Roundhouse;
                    if (kb.vKey.wasPressedThisFrame) punch = PunchType.LowCalfKick;
                    if (kb.f1Key.wasPressedThisFrame) autoPilot = !autoPilot;
                    guard |= kb.leftShiftKey.isPressed;
                    slip |= kb.cKey.wasPressedThisFrame;
                    crouch |= kb.xKey.isPressed; // X only so Alt+Tab never locks crouch on!
                    if (kb.zKey.wasPressedThisFrame) _sitToggle = !_sitToggle;
                    if (kb.spaceKey.wasPressedThisFrame) punch = SelectedSpecial;
                    sprint |= kb.leftCtrlKey.isPressed;
                    grab |= kb.gKey.isPressed;
                }
                if (mouse != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame) punch = PunchType.Jab;
                    if (mouse.rightButton.wasPressedThisFrame) punch = PunchType.Cross;
                    float sc = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(sc) > 0.01f)
                    {
                        _scrollAcc += Mathf.Sign(sc);
                        Cycle(sc > 0f ? -1 : 1);
                    }
                }
            }
            else if (playerIndex == 1 && kb != null)
            {
                // Player 2 keyboard fallback (IJKL / Arrows + U/O/Y/P/H/N/M/RightShift/Enter) for 2-player couch co-op on one PC
                if (kb.iKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.kKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.lKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.jKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.uKey.wasPressedThisFrame) punch = PunchType.Jab;
                if (kb.oKey.wasPressedThisFrame) punch = PunchType.Cross;
                if (kb.yKey.wasPressedThisFrame) punch = PunchType.Hook;
                if (kb.pKey.wasPressedThisFrame) punch = PunchType.Uppercut;
                if (kb.semicolonKey.wasPressedThisFrame) punch = PunchType.FrontKick;
                if (kb.quoteKey.wasPressedThisFrame) punch = PunchType.Roundhouse;
                guard |= kb.rightShiftKey.isPressed;
                sprint |= kb.rightCtrlKey.isPressed;
                grab |= kb.hKey.isPressed;
                crouch |= kb.mKey.isPressed;
                if (kb.nKey.wasPressedThisFrame) _sitToggle = !_sitToggle;
                if (kb.leftBracketKey.wasPressedThisFrame) Cycle(-1);
                if (kb.rightBracketKey.wasPressedThisFrame) Cycle(1);
                if (kb.enterKey.wasPressedThisFrame) punch = SelectedSpecial;
            }

            if (pad != null)
            {
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.04f) move = pad.leftStick.ReadValue();
                if (pad.buttonWest.wasPressedThisFrame) punch = PunchType.Jab;
                if (pad.buttonNorth.wasPressedThisFrame) punch = PunchType.Cross;
                if (pad.buttonEast.wasPressedThisFrame) punch = PunchType.Hook;
                if (pad.buttonSouth.wasPressedThisFrame) punch = PunchType.Uppercut;
                guard |= pad.leftShoulder.isPressed;
                grab |= pad.leftTrigger.ReadValue() > 0.4f;
                slip |= pad.rightShoulder.wasPressedThisFrame;
                crouch |= pad.rightStickButton.isPressed;
                if (pad.dpad.down.wasPressedThisFrame) _sitToggle = !_sitToggle;
                if (pad.dpad.up.wasPressedThisFrame) punch = PunchType.FrontKick;
                if (pad.dpad.right.wasPressedThisFrame) Cycle(1);
                if (pad.dpad.left.wasPressedThisFrame) Cycle(-1);
                if (pad.rightTrigger.wasPressedThisFrame) punch = SelectedSpecial;
                sprint |= pad.leftStickButton.isPressed;
            }

            if (sprint) _sitToggle = false; // sprinting stands the mech back up from Jiu-Jitsu floor sit

            int slipDir = 0;
            if (slip)
            {
                slipDir = move.x < -0.3f ? -1 : move.x > 0.3f ? 1 : -_lastSlip;
                _lastSlip = slipDir;
            }

            bool anyHumanInput = move.sqrMagnitude > 0.01f || punch != PunchType.None || guard || slip || sprint || grab || crouch || _sitToggle;
            if (anyHumanInput)
            {
                _idleTime = 0f;
                if (move.sqrMagnitude > 0.01f || punch != PunchType.None) autoPilot = false;
            }
            else
            {
                _idleTime += Time.unscaledDeltaTime;
            }

            // If Auto-Pilot (F1) is on or the player has been hands-off for 6s, let the AI spar until any key is pressed!
            if (_idleAi != null)
            {
                bool aiActive = autoPilot || _idleTime > 6f;
                _idleAi.enabled = aiActive;
                if (aiActive) return;
            }

            var i = _boxer.intent;
            i.move = move;
            if (punch != PunchType.None) { i.punch = punch; _boxer.mashCount++; }   // presses also struggle out of grabs
            i.guard = guard;
            i.sprint = sprint;
            i.grab = grab;
            i.crouch = crouch;
            i.sit = _sitToggle;
            if (slipDir != 0) { i.slip = slipDir; _boxer.mashCount++; }
            _boxer.intent = i;
        }
    }
}
