using UnityEngine;

namespace IronFauna.Boxing
{
    /// <summary>
    /// Procedurally synthesised fight sounds (no audio assets needed): a deep metal-on-metal impact, a guard clank
    /// and the air whoosh of a big arm. Played through one 2D source so the camera hears every hit the same.
    /// </summary>
    public static class BoxingSfx
    {
        private const int Rate = 44100;
        private static AudioClip _impact, _clank, _whoosh, _bell, _timber;
        private static readonly AudioClip[] _voiceClips = new AudioClip[4];
        private static AudioSource _src;
        private static System.Random _rng = new System.Random(7);

        public static void Attach(GameObject host)
        {
            _src = host.GetComponent<AudioSource>();
            if (_src == null) _src = host.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.ignoreListenerPause = true;
            if (Object.FindAnyObjectByType<AudioListener>() == null) host.AddComponent<AudioListener>();
        }

        public static void Impact(float power)
        {
            if (_impact == null) _impact = Make("IF_Impact", 0.55f, ImpactSample);
            Play(_impact, Mathf.Lerp(0.55f, 1f, power), Mathf.Lerp(1.15f, 0.78f, power));
        }

        public static void Clank(float power)
        {
            if (_clank == null) _clank = Make("IF_Clank", 0.4f, ClankSample);
            Play(_clank, Mathf.Lerp(0.4f, 0.75f, power), Mathf.Lerp(1.1f, 0.9f, power));
        }

        public static void Whoosh(float power)
        {
            if (_whoosh == null) _whoosh = Make("IF_Whoosh", 0.3f, null);
            Play(_whoosh, Mathf.Lerp(0.15f, 0.35f, power), Mathf.Lerp(1.25f, 0.85f, power));
        }

        public static void AnnouncerBell()
        {
            if (_bell == null) _bell = Make("IF_Bell", 0.85f, BellSample);
            Play(_bell, 0.65f, 1.0f);
        }

        public static void TimberCreak()
        {
            if (_timber == null) _timber = Make("IF_Timber", 0.9f, TimberSample);
            Play(_timber, 0.8f, 0.95f);
        }

        public static void AnnouncerVoice(int style, float hype = 1f)
        {
            int idx = ((style % _voiceClips.Length) + _voiceClips.Length) % _voiceClips.Length;
            if (_voiceClips[idx] == null)
            {
                int captured = idx;
                _voiceClips[idx] = Make("IF_Announcer_" + idx, 0.95f, (t, n) => VoiceSample(t, n, captured));
            }
            Play(_voiceClips[idx], Mathf.Lerp(0.5f, 0.85f, hype), Mathf.Lerp(0.92f, 1.08f, hype));
        }

        private static void Play(AudioClip clip, float vol, float pitch)
        {
            if (_src == null) return;
            _src.pitch = pitch * (0.95f + (float)_rng.NextDouble() * 0.1f);
            _src.PlayOneShot(clip, vol * IronFauna.Core.GameSettings.SfxVolume);
        }

        private delegate float Sample(float t, float noise);

        private static AudioClip Make(string name, float seconds, Sample f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            if (f == null)
            {
                // whoosh: noise through a sweeping one-pole low-pass, swelling then fading
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float k = t / seconds;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f));
                    float cutoff = Mathf.Lerp(0.02f, 0.12f, Mathf.Sin(Mathf.PI * k));
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (noise - lp) * cutoff;
                    data[i] = lp * env * 2.2f;
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    data[i] = f(t, (float)(rng.NextDouble() * 2.0 - 1.0));
                }
            }
            float peak = 1e-4f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) data[i] = data[i] / peak * 0.9f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float _lpImpact;

        private static float ImpactSample(float t, float noise)
        {
            // body: a pitch-dropping sine thump; crack: a short noise burst; ring: two metallic partials
            float thumpF = Mathf.Lerp(150f, 42f, Mathf.Clamp01(t / 0.12f));
            float thump = Mathf.Sin(2f * Mathf.PI * thumpF * t) * Mathf.Exp(-t * 9f);
            _lpImpact += (noise - _lpImpact) * 0.25f;
            float crack = _lpImpact * Mathf.Exp(-t * 60f) * 1.4f;
            float ring = (Mathf.Sin(2f * Mathf.PI * 523f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 811f * t) * 0.4f) * Mathf.Exp(-t * 14f) * 0.22f;
            return thump * 1.1f + crack + ring;
        }

        private static float ClankSample(float t, float noise)
        {
            float ring = Mathf.Sin(2f * Mathf.PI * 410f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1030f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 1610f * t) * 0.2f;
            float thud = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 18f);
            return ring * Mathf.Exp(-t * 11f) + noise * Mathf.Exp(-t * 90f) * 0.6f + thud * 0.5f;
        }

        private static float BellSample(float t, float noise)
        {
            // Double boxing ring bell + low stadium horn
            float t2 = t > 0.22f ? t - 0.22f : t;
            float bell = (Mathf.Sin(2f * Mathf.PI * 880f * t2) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 1320f * t2) * 0.35f
                        + Mathf.Sin(2f * Mathf.PI * 2110f * t2) * 0.2f) * Mathf.Exp(-t2 * 7f);
            float horn = (Mathf.Sin(2f * Mathf.PI * 146.8f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 293.6f * t)) * Mathf.Exp(-t * 3.2f) * 0.35f;
            return bell + horn;
        }

        private static float TimberSample(float t, float noise)
        {
            // Cracking wood pulses followed by a deep falling whoosh
            float crackPulse = Mathf.Abs(Mathf.Sin(t * 38f)) * Mathf.Exp(-t * 3.5f);
            float wood = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(240f, 75f, t) * t) * crackPulse;
            float swoosh = noise * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)) * 0.45f;
            return wood * 0.9f + swoosh;
        }

        private static float VoiceSample(float t, float noise, int style)
        {
            // Arcade mecha announcer vocal-formant cadence + crowd roar swell
            float syllableRate = style == 0 ? 6.5f : style == 1 ? 8.0f : style == 2 ? 5.2f : 7.2f;
            float syl = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * syllableRate * t));
            syl = Mathf.Pow(syl, 0.4f) * Mathf.Exp(-t * 1.4f);
            float f0 = style == 0 ? Mathf.Lerp(125f, 165f, t)
                     : style == 1 ? Mathf.Lerp(155f, 115f, t)
                     : style == 2 ? 135f + Mathf.Sin(t * 18f) * 22f
                     : Mathf.Lerp(110f, 175f, Mathf.Repeat(t * 2.5f, 1f));
            float f1 = 520f + Mathf.Sin(t * 24f) * 180f;
            float f2 = 1480f + Mathf.Cos(t * 19f) * 320f;
            float glottal = Mathf.Sin(2f * Mathf.PI * f0 * t)
                          + 0.65f * Mathf.Sin(2f * Mathf.PI * f1 * t)
                          + 0.35f * Mathf.Sin(2f * Mathf.PI * f2 * t);
            float crowd = noise * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.95f)) * 0.22f;
            return glottal * syl * 0.75f + crowd;
        }
    }
}
