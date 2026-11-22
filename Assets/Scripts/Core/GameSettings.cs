using UnityEngine;

namespace IronFauna.Core
{
    /// <summary>
    /// Player settings (owner 2026-11-22: "Esc should bring up a settings menu... screen ratios, graphics settings, sound").
    /// Saved in PlayerPrefs and applied at start-up. Read by the audio, camera and input code.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "IronFauna.Settings.";

        public static readonly string[] DisplayModeNames = { "FULLSCREEN", "BORDERLESS WINDOW", "WINDOWED" };
        private static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        public static readonly int[] FrameCaps = { 30, 60, 90, 120, 144, 165, 240, 0 };

        public static float MasterVolume = 1f;
        public static float SfxVolume = 1f;
        public static float AnnouncerVolume = 1f;
        public static float CameraShake = 1f;
        public static float MouseSensitivity = 1f;
        public static int DisplayMode = 1;          // borderless by default
        public static int ResolutionIndex = -1;     // -1 = desktop resolution
        public static int Quality = -1;             // -1 = project default
        public static bool VSync = true;
        public static int FrameCapIndex = 1;        // 60 (only used with VSync off)

        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Load();
            Apply(false);
        }

        public static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(Prefix + "Master", 1f);
            SfxVolume = PlayerPrefs.GetFloat(Prefix + "Sfx", 1f);
            AnnouncerVolume = PlayerPrefs.GetFloat(Prefix + "Announcer", 1f);
            CameraShake = PlayerPrefs.GetFloat(Prefix + "Shake", 1f);
            MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "Mouse", 1f);
            DisplayMode = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "DisplayMode", 1), 0, DisplayModes.Length - 1);
            ResolutionIndex = PlayerPrefs.GetInt(Prefix + "Resolution", -1);
            Quality = PlayerPrefs.GetInt(Prefix + "Quality", -1);
            VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) == 1;
            FrameCapIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "FrameCap", 1), 0, FrameCaps.Length - 1);
            _loaded = true;
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Master", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "Sfx", SfxVolume);
            PlayerPrefs.SetFloat(Prefix + "Announcer", AnnouncerVolume);
            PlayerPrefs.SetFloat(Prefix + "Shake", CameraShake);
            PlayerPrefs.SetFloat(Prefix + "Mouse", MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "DisplayMode", DisplayMode);
            PlayerPrefs.SetInt(Prefix + "Resolution", ResolutionIndex);
            PlayerPrefs.SetInt(Prefix + "Quality", Quality);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "FrameCap", FrameCapIndex);
            PlayerPrefs.Save();
        }

        /// <summary>Distinct resolutions the monitor offers (largest last).</summary>
        public static Resolution[] Resolutions()
        {
            var all = Screen.resolutions;
            var list = new System.Collections.Generic.List<Resolution>();
            foreach (var r in all)
            {
                bool dup = false;
                foreach (var e in list) if (e.width == r.width && e.height == r.height) { dup = true; break; }
                if (!dup) list.Add(r);
            }
            if (list.Count == 0) list.Add(new Resolution { width = Screen.width, height = Screen.height });
            return list.ToArray();
        }

        public static string ResolutionLabel()
        {
            var res = Resolutions();
            if (ResolutionIndex < 0 || ResolutionIndex >= res.Length) return "DESKTOP";
            var r = res[ResolutionIndex];
            return $"{r.width} x {r.height}  ({AspectName(r.width, r.height)})";
        }

        private static string AspectName(int w, int h)
        {
            float a = (float)w / Mathf.Max(1, h);
            if (Mathf.Abs(a - 16f / 9f) < 0.02f) return "16:9";
            if (Mathf.Abs(a - 16f / 10f) < 0.02f) return "16:10";
            if (Mathf.Abs(a - 21f / 9f) < 0.06f) return "21:9";
            if (Mathf.Abs(a - 32f / 9f) < 0.06f) return "32:9";
            if (Mathf.Abs(a - 4f / 3f) < 0.02f) return "4:3";
            return a.ToString("0.00") + ":1";
        }

        public static string QualityLabel()
        {
            var names = QualitySettings.names;
            int q = Quality < 0 ? QualitySettings.GetQualityLevel() : Mathf.Clamp(Quality, 0, names.Length - 1);
            return names.Length > 0 ? names[q].ToUpperInvariant() : "DEFAULT";
        }

        public static string FrameCapLabel() => FrameCaps[FrameCapIndex] == 0 ? "UNLIMITED" : FrameCaps[FrameCapIndex] + " FPS";

        /// <summary>Applies everything. <paramref name="display"/>: also change resolution / window mode (skipped in the editor).</summary>
        public static void Apply(bool display = true)
        {
            if (!_loaded) Load();
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
            if (Quality >= 0 && Quality < QualitySettings.names.Length && Quality != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(Quality, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : (FrameCaps[FrameCapIndex] == 0 ? -1 : FrameCaps[FrameCapIndex]);
#if !UNITY_EDITOR
            if (display)
            {
                var mode = DisplayModes[Mathf.Clamp(DisplayMode, 0, DisplayModes.Length - 1)];
                var res = Resolutions();
                if (ResolutionIndex >= 0 && ResolutionIndex < res.Length)
                    Screen.SetResolution(res[ResolutionIndex].width, res[ResolutionIndex].height, mode);
                else
                    Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, mode);
            }
#endif
        }
    }
}
