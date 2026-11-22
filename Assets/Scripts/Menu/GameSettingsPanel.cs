using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Core;

namespace IronFauna.Menu
{
    /// <summary>
    /// SETTINGS overlay used by the title screen and the in-fight pause menu: video (display mode, resolution, quality,
    /// V-sync, frame cap), audio (master, effects, announcer) and controls (mouse sensitivity, camera shake).
    /// Every change applies and saves at once.
    /// </summary>
    public sealed class GameSettingsPanel
    {
        public GameObject Root { get; private set; }
        public bool IsOpen => Root != null && Root.activeSelf;

        private Action _onClose;
        private readonly List<(MenuUI.Cycler row, Func<string> value)> _rows = new List<(MenuUI.Cycler, Func<string>)>();

        public static GameSettingsPanel Build(Transform canvasRoot, Action onClose)
        {
            var p = new GameSettingsPanel { _onClose = onClose };
            var dim = MenuUI.Panel(canvasRoot, "SettingsOverlay", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.78f));
            MenuUI.Fill(dim.rectTransform);
            p.Root = dim.gameObject;

            var box = MenuUI.Panel(dim.transform, "SettingsBox", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.97f));
            MenuUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-560f, -500f), new Vector2(560f, 500f));
            var stack = MenuUI.VStack(box.gameObject, 8f, 30);
            stack.childAlignment = TextAnchor.UpperCenter;

            var title = MenuUI.Label(box.transform, "SETTINGS", 50, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(title, 68f);

            p.Header(box.transform, "VIDEO");
            p.Row(box.transform, "DISPLAY MODE", () => GameSettings.DisplayModeNames[GameSettings.DisplayMode], d =>
            {
                GameSettings.DisplayMode = Wrap(GameSettings.DisplayMode + d, GameSettings.DisplayModeNames.Length);
            }, true);
            p.Row(box.transform, "RESOLUTION", GameSettings.ResolutionLabel, d =>
            {
                int n = GameSettings.Resolutions().Length;
                // -1 (desktop) .. n-1
                GameSettings.ResolutionIndex = Wrap(GameSettings.ResolutionIndex + 1 + d, n + 1) - 1;
            }, true);
            p.Row(box.transform, "GRAPHICS QUALITY", GameSettings.QualityLabel, d =>
            {
                int n = Mathf.Max(1, QualitySettings.names.Length);
                int cur = GameSettings.Quality < 0 ? QualitySettings.GetQualityLevel() : GameSettings.Quality;
                GameSettings.Quality = Wrap(cur + d, n);
            });
            p.Row(box.transform, "V-SYNC", () => GameSettings.VSync ? "ON" : "OFF", d => GameSettings.VSync = !GameSettings.VSync);
            p.Row(box.transform, "FRAME RATE CAP", () => GameSettings.VSync ? "MATCHES MONITOR (V-SYNC)" : GameSettings.FrameCapLabel(), d =>
            {
                GameSettings.FrameCapIndex = Wrap(GameSettings.FrameCapIndex + d, GameSettings.FrameCaps.Length);
            });

            p.Header(box.transform, "AUDIO");
            p.Row(box.transform, "MASTER VOLUME", () => Pct(GameSettings.MasterVolume), d => GameSettings.MasterVolume = Step(GameSettings.MasterVolume, d, 0f, 1f));
            p.Row(box.transform, "EFFECTS VOLUME", () => Pct(GameSettings.SfxVolume), d => GameSettings.SfxVolume = Step(GameSettings.SfxVolume, d, 0f, 1f));
            p.Row(box.transform, "ANNOUNCER VOLUME", () => Pct(GameSettings.AnnouncerVolume), d => GameSettings.AnnouncerVolume = Step(GameSettings.AnnouncerVolume, d, 0f, 1f));

            p.Header(box.transform, "CONTROLS");
            p.Row(box.transform, "MOUSE SENSITIVITY", () => Pct(GameSettings.MouseSensitivity), d => GameSettings.MouseSensitivity = Step(GameSettings.MouseSensitivity, d, 0.2f, 3f));
            p.Row(box.transform, "CAMERA SHAKE", () => Pct(GameSettings.CameraShake), d => GameSettings.CameraShake = Step(GameSettings.CameraShake, d, 0f, 1.5f));

            MenuUI.Spacer(box.transform, 10f, true);
            var buttons = MenuUI.NewRect(box.transform, "SettingsButtons");
            MenuUI.HStack(buttons.gameObject, 14f, 0).childForceExpandWidth = true;
            MenuUI.Size(buttons, 62f);
            var reset = MenuUI.CreateButton(buttons, "RESET TO DEFAULTS", 24, IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiTextDim, () =>
            {
                GameSettings.MasterVolume = 1f; GameSettings.SfxVolume = 1f; GameSettings.AnnouncerVolume = 1f;
                GameSettings.MouseSensitivity = 1f; GameSettings.CameraShake = 1f;
                GameSettings.DisplayMode = 1; GameSettings.ResolutionIndex = -1; GameSettings.Quality = -1;
                GameSettings.VSync = true; GameSettings.FrameCapIndex = 1;
                GameSettings.Apply(true); GameSettings.Save(); p.Refresh();
            });
            MenuUI.Size(reset, 62f, -1f, 1f);
            var back = MenuUI.CreateButton(buttons, "BACK", 26, IronFaunaPalette.UiSelect, IronFaunaPalette.UiAccent, IronFaunaPalette.UiText, p.Close);
            MenuUI.Size(back, 62f, -1f, 1f);

            p.Root.SetActive(false);
            return p;
        }

        public void Open()
        {
            Refresh();
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
        }

        public void Close()
        {
            if (Root == null || !Root.activeSelf) return;
            Root.SetActive(false);
            _onClose?.Invoke();
        }

        private void Header(Transform parent, string text)
        {
            var h = MenuUI.Label(parent, text, 22, IronFaunaPalette.UiTextDim, TextAnchor.LowerLeft, FontStyle.Bold);
            MenuUI.Size(h, 38f);
        }

        private void Row(Transform parent, string label, Func<string> value, Action<int> change, bool display = false)
        {
            MenuUI.Cycler row = null;
            row = MenuUI.CreateCycler(parent, label, () => Change(change, -1, display), () => Change(change, +1, display));
            _rows.Add((row, value));
        }

        private void Change(Action<int> change, int dir, bool display)
        {
            change(dir);
            GameSettings.Apply(display);
            GameSettings.Save();
            Refresh();
        }

        private void Refresh()
        {
            foreach (var (row, value) in _rows) row.Value.text = value();
        }

        private static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;
        private static float Step(float v, int dir, float min, float max) => Mathf.Clamp(Mathf.Round((v + dir * 0.1f) * 10f) / 10f, min, max);
        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
    }
}
