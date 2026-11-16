using UnityEngine;
using UnityEngine.UI;
using IronFauna.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IronFauna.Menu
{
    /// <summary>Arena overlay: shows a hint and returns to the garage on Escape / gamepad Start.</summary>
    public class ArenaReturnToGarage : MonoBehaviour
    {
        private void Start()
        {
            var canvas = MenuUI.CreateCanvas("ArenaHud", 10);
            canvas.transform.SetParent(transform, false);
            var hint = MenuUI.Label(canvas.transform, "ESC  —  BACK TO GARAGE", 22, IronFaunaPalette.OffWhitePanel, TextAnchor.LowerRight, FontStyle.Bold);
            MenuUI.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-560f, 24f), new Vector2(-32f, 64f));
        }

        private void Update()
        {
            bool back = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) back = true;
            if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) back = true;
#else
            back = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (back) IronFaunaGameFlow.ReturnToGarage();
        }
    }
}
