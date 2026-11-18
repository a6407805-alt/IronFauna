using UnityEngine;
using UnityEngine.UIElements;
using IronFauna.Input;
using IronFauna.Gameplay;

namespace IronFauna.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class IronFaunaHUDController : MonoBehaviour
    {
        private Label _objectiveHpLabel;
        private Label _creditsLabel;
        private Label _activeRobotLabel;
        private Label _activePoseLabel;
        private Label _detectedDeviceLabel;
        private Label _bindingPromptsLabel;

        private Button _btnAutoScheme;
        private Button _btnKbmScheme;
        private Button _btnPs5Scheme;
        private Button _btnXboxScheme;
        private Button _btnSwapRobot;
        private Button _btnCyclePose;
        private Button _btnInvertY;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null || doc.rootVisualElement == null) return;

            var root = doc.rootVisualElement;
            _objectiveHpLabel    = root.Q<Label>("objectiveHpLabel");
            _creditsLabel        = root.Q<Label>("creditsLabel");
            _activeRobotLabel    = root.Q<Label>("activeRobotLabel");
            _activePoseLabel     = root.Q<Label>("activePoseLabel");
            _detectedDeviceLabel = root.Q<Label>("detectedDeviceLabel");
            _bindingPromptsLabel = root.Q<Label>("bindingPromptsLabel");

            _btnAutoScheme = root.Q<Button>("btnAutoScheme");
            _btnKbmScheme  = root.Q<Button>("btnKbmScheme");
            _btnPs5Scheme  = root.Q<Button>("btnPs5Scheme");
            _btnXboxScheme = root.Q<Button>("btnXboxScheme");
            _btnSwapRobot  = root.Q<Button>("btnSwapRobot");
            _btnCyclePose  = root.Q<Button>("btnCyclePose");
            _btnInvertY    = root.Q<Button>("btnInvertY");

            if (_btnAutoScheme != null) _btnAutoScheme.clicked += () => SelectScheme(ControlSchemeOption.AutoDetect);
            if (_btnKbmScheme  != null) _btnKbmScheme.clicked  += () => SelectScheme(ControlSchemeOption.KeyboardAndMouse);
            if (_btnPs5Scheme  != null) _btnPs5Scheme.clicked  += () => SelectScheme(ControlSchemeOption.PS5DualSense);
            if (_btnXboxScheme != null) _btnXboxScheme.clicked += () => SelectScheme(ControlSchemeOption.XboxController);

            if (_btnSwapRobot != null)
            {
                _btnSwapRobot.clicked += () =>
                {
                    if (MechFleetCoordinator.Instance != null)
                        MechFleetCoordinator.Instance.SwapActiveRobot();
                };
            }

            if (_btnCyclePose != null)
            {
                _btnCyclePose.clicked += () =>
                {
                    if (MechFleetCoordinator.Instance != null && MechFleetCoordinator.Instance.ActiveRobot != null)
                        MechFleetCoordinator.Instance.ActiveRobot.CycleLegPose(1);
                };
            }

            if (_btnInvertY != null)
            {
                _btnInvertY.clicked += () =>
                {
                    var input = IronFaunaInputController.Instance;
                    if (input != null)
                    {
                        input.invertY = !input.invertY;
                        _btnInvertY.text = $"INVERT Y: {(input.invertY ? "ON" : "OFF")}";
                    }
                };
            }
        }

        private void SelectScheme(ControlSchemeOption option)
        {
            if (IronFaunaInputController.Instance != null)
            {
                IronFaunaInputController.Instance.SetControlScheme(option);
            }
            UpdateSchemeButtonClasses(option);
        }

        private void UpdateSchemeButtonClasses(ControlSchemeOption selected)
        {
            SetActiveClass(_btnAutoScheme, selected == ControlSchemeOption.AutoDetect);
            SetActiveClass(_btnKbmScheme,  selected == ControlSchemeOption.KeyboardAndMouse);
            SetActiveClass(_btnPs5Scheme,  selected == ControlSchemeOption.PS5DualSense);
            SetActiveClass(_btnXboxScheme, selected == ControlSchemeOption.XboxController);
        }

        private void SetActiveClass(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("scheme-button-active");
            else btn.RemoveFromClassList("scheme-button-active");
        }

        private void Update()
        {
            if (_objectiveHpLabel != null && ObjectiveCore.Instance != null)
            {
                _objectiveHpLabel.text = $"OBJECTIVE CORE HP: {ObjectiveCore.Instance.currentHp:0} / {ObjectiveCore.Instance.maxHp:0}";
            }

            if (_creditsLabel != null && GridDefenseManager.Instance != null)
            {
                _creditsLabel.text = $"CREDITS: {GridDefenseManager.Instance.credits} CR  |  ENEMIES: {EnemyWalker.ActiveEnemies.Count}";
            }

            if (MechFleetCoordinator.Instance != null && MechFleetCoordinator.Instance.ActiveRobot != null)
            {
                var robot = MechFleetCoordinator.Instance.ActiveRobot;
                if (_activeRobotLabel != null)
                    _activeRobotLabel.text = $"PILOTING: {robot.name} (Speed: {robot.currentNormalizedSpeed * 100f:0}%)";
                if (_activePoseLabel != null && robot.Builder != null)
                    _activePoseLabel.text = $"KNEE PIVOT POSE: {robot.Builder.legPose}";
            }

            if (IronFaunaInputController.Instance != null)
            {
                var input = IronFaunaInputController.Instance;
                if (_detectedDeviceLabel != null)
                    _detectedDeviceLabel.text = $"MODE: {input.selectedScheme}  ->  ACTIVE: {input.activeResolvedScheme}";
                if (_bindingPromptsLabel != null)
                    _bindingPromptsLabel.text = input.GetActiveBindingPrompts();
            }
        }
    }
}
