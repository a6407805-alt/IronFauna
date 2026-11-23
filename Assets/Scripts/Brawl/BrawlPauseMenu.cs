using System;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Esc in a fight (owner 2026-11-22: "Esc should bring up a settings menu and an option to quit, not cancel the game"):
    /// RESUME / SETTINGS / RESTART ROUND / QUIT TO MAIN MENU / QUIT GAME. Offline the fight freezes; online it keeps running
    /// (your robot just stops taking input) because the other players are still playing.
    /// </summary>
    public class BrawlPauseMenu : MonoBehaviour
    {
        public bool freezeTime = true;
        public bool allowRestart = true;
        public Action onRestart;
        public Action onQuitToMenu;

        private GameObject _root;
        private Button _restartBtn;
        private Text _hint;
        private GameSettingsPanel _settings;
        private float _savedTimeScale = 1f;

        public bool IsOpen => _root != null && _root.activeSelf;
        public bool Frozen => IsOpen && freezeTime;

        private void Awake()
        {
            MenuUI.EnsureEventSystem();
            var canvas = MenuUI.CreateCanvas("IronFauna_Pause", 450);
            canvas.transform.SetParent(transform, false);

            var dim = MenuUI.Panel(canvas.transform, "PauseOverlay", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.72f));
            MenuUI.Fill(dim.rectTransform);
            _root = dim.gameObject;

            var box = MenuUI.Panel(dim.transform, "PauseBox", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.97f));
            MenuUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-320f, -330f), new Vector2(320f, 330f));
            var stack = MenuUI.VStack(box.gameObject, 12f, 34);
            stack.childAlignment = TextAnchor.UpperCenter;

            var title = MenuUI.Label(box.transform, "PAUSED", 56, IronFaunaPalette.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Size(title, 80f);

            Button Btn(string label, Color normal, Color hover, Action click)
            {
                var b = MenuUI.CreateButton(box.transform, label, 28, normal, hover, IronFaunaPalette.UiText, () => click());
                MenuUI.Size(b, 66f);
                return b;
            }

            Btn("RESUME", IronFaunaPalette.UiSelect, IronFaunaPalette.UiAccent, Close);
            Btn("SETTINGS", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, OpenSettings);
            _restartBtn = Btn("RESTART ROUND", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, () => { Close(); onRestart?.Invoke(); });
            Btn("QUIT TO MAIN MENU", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, () => { Close(); onQuitToMenu?.Invoke(); });
            Btn("QUIT GAME", IronFaunaPalette.MaroonRear, IronFaunaPalette.CrimsonFront, QuitGame);

            MenuUI.Spacer(box.transform, 4f, true);
            _hint = MenuUI.Label(box.transform, "ESC  RESUME", 18, IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Size(_hint, 28f);

            _settings = GameSettingsPanel.Build(canvas.transform, null);
            _root.SetActive(false);
        }

        /// <summary>Esc / Start: closes settings, else toggles the pause menu.</summary>
        public void HandleBack()
        {
            if (IsOpen || (_settings != null && _settings.IsOpen)) UiSfx.Back(); else UiSfx.Select();
            if (_settings != null && _settings.IsOpen) { _settings.Close(); return; }
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            _restartBtn.gameObject.SetActive(allowRestart);
            _hint.text = freezeTime ? "ESC  RESUME" : "ONLINE: THE FIGHT KEEPS GOING  //  ESC  RESUME";
            _root.SetActive(true);
            MechBoxerPlayerInput.InputBlocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (freezeTime)
            {
                _savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (_settings != null && _settings.IsOpen) _settings.Close();
            _root.SetActive(false);
            MechBoxerPlayerInput.InputBlocked = false;
            if (freezeTime)
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OpenSettings() => _settings?.Open();

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            MechBoxerPlayerInput.InputBlocked = false;
            AudioListener.pause = false;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }
    }
}
