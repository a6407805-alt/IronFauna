using UnityEngine;
using IronFauna.Core;

namespace IronFauna.Menu
{
    /// <summary>
    /// Title / garage / setup music (owner 2026-11-22: "a little bit of menu music"): loops Resources/Music/menu_theme
    /// (an original industrial track), fades in on the menus and out when a fight starts. Volume = Settings > Music.
    /// </summary>
    public class MenuMusic : MonoBehaviour
    {
        private const float Level = 0.55f;   // the track is mastered loud; this sits under the UI sounds
        private AudioSource _src;
        private bool _wantPlaying = true;

        private void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = Resources.Load<AudioClip>("Music/menu_theme");
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.priority = 0;
            _src.volume = 0f;
            if (_src.clip != null) _src.Play();
        }

        /// <summary>Fade out and stop (a fight is starting).</summary>
        public void FadeOut() => _wantPlaying = false;

        private void Update()
        {
            if (_src == null || _src.clip == null) return;
            float target = _wantPlaying ? Level * GameSettings.MusicVolume : 0f;
            float rate = _wantPlaying ? 0.35f : 0.9f;   // ~2.5 s fade in, ~0.6 s fade out
            _src.volume = Mathf.MoveTowards(_src.volume, target, rate * Time.unscaledDeltaTime);
            if (!_wantPlaying && _src.volume <= 0.001f && _src.isPlaying) _src.Stop();
        }
    }
}
