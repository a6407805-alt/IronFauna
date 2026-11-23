using System.Collections.Generic;
using UnityEngine;
using IronFauna.Core;

namespace IronFauna.Menu
{
    /// <summary>
    /// Menu music (owner 2026-11-22: "the song is a banger... a different song for fight/brawl, pilots, garage, settings,
    /// and the tournament menu is silent"). One persistent player that crossfades between tracks in Resources/Music:
    /// menu_theme (title), tournament (tournament setup + bracket between bouts), brawl (FIGHT setup), pilots, garage
    /// (garage + tournament repair bay), settings (overlay: returns to the previous track when closed).
    /// Each track resumes where it left off. Silent during fights. Volume = Settings > Music.
    /// </summary>
    public class MenuMusic : MonoBehaviour
    {
        private const float Level = 0.55f;   // tracks are mastered loud; this sits under the UI sounds
        private const float FadeIn = 0.45f, FadeOut = 0.8f;   // volume units per second

        private static MenuMusic _inst;
        private readonly AudioSource[] _src = new AudioSource[2];
        private readonly string[] _srcTrack = new string[2];
        private readonly Dictionary<string, float> _resumeAt = new Dictionary<string, float>();
        private int _cur;
        private string _track;
        private string _overlayReturn;
        private bool _overlayOpen;

        private static MenuMusic Instance
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("IronFauna_Music");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<MenuMusic>();
                }
                return _inst;
            }
        }

        /// <summary>Crossfade to a track (file name in Resources/Music). Null = silence.</summary>
        public static void Play(string track) => Instance.Switch(track);
        public static void Stop() => Instance.Switch(null);

        /// <summary>Settings and other overlays: play their own track, then return to whatever was playing.</summary>
        public static void OpenOverlay(string track)
        {
            var m = Instance;
            if (!m._overlayOpen) m._overlayReturn = m._track;
            m._overlayOpen = true;
            m.Switch(track);
        }

        public static void CloseOverlay()
        {
            var m = Instance;
            if (!m._overlayOpen) return;
            m._overlayOpen = false;
            m.Switch(m._overlayReturn);
        }

        private void Awake()
        {
            for (int i = 0; i < 2; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.volume = 0f;
                s.ignoreListenerPause = true;   // keeps playing under the pause / settings overlay
                _src[i] = s;
            }
        }

        private void Switch(string track)
        {
            if (track == _track) return;
            // remember where the outgoing track was, so coming back resumes it
            if (_track != null && _src[_cur].isPlaying && _src[_cur].clip != null) _resumeAt[_track] = _src[_cur].time;
            _track = track;
            if (track == null) return;

            var clip = Resources.Load<AudioClip>("Music/" + track);
            if (clip == null) clip = Resources.Load<AudioClip>("Music/menu_theme");
            if (clip == null) return;

            // the other source fades in the new track while the current one fades out
            int next = 1 - _cur;
            var s = _src[next];
            if (s.clip != clip || !s.isPlaying)
            {
                s.Stop();
                s.clip = clip;
                s.volume = 0f;
                float at = _resumeAt.TryGetValue(track, out float t) ? t : 0f;
                s.time = Mathf.Clamp(at, 0f, Mathf.Max(0f, clip.length - 0.1f));
                s.Play();
            }
            _srcTrack[next] = track;
            _cur = next;
        }

        private void Update()
        {
            float music = Level * GameSettings.MusicVolume;
            for (int i = 0; i < 2; i++)
            {
                var s = _src[i];
                bool active = i == _cur && _track != null && _srcTrack[i] == _track;
                float target = active ? music : 0f;
                s.volume = Mathf.MoveTowards(s.volume, target, (active ? FadeIn : FadeOut) * Time.unscaledDeltaTime);
                if (!active && s.isPlaying && s.volume <= 0.001f) s.Stop();
            }
        }
    }
}
