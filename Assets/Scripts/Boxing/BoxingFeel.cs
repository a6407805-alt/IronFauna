using UnityEngine;

namespace IronFauna.Boxing
{
    /// <summary>Shared impact feel: hit-stop (a brief near-freeze of game time) and camera trauma.</summary>
    public static class BoxingFeel
    {
        public static float Trauma;
        public static float FovKick;
        private static float _stopLeft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Trauma = 0f; FovKick = 0f; _stopLeft = 0f; }

        public static void HitStop(float seconds) => _stopLeft = Mathf.Max(_stopLeft, seconds);

        public static void Shake(float amount) => Trauma = Mathf.Min(1f, Trauma + amount);

        /// <summary>Call once per frame (unscaled) by whoever owns the match.</summary>
        public static void Tick(float unscaledDt)
        {
            if (_stopLeft > 0f)
            {
                _stopLeft -= unscaledDt;
                Time.timeScale = _stopLeft > 0f ? 0.03f : 1f;
            }
            Trauma = Mathf.Max(0f, Trauma - unscaledDt * 1.6f);
            FovKick = Mathf.MoveTowards(FovKick, 0f, unscaledDt * 18f);
        }

        public static void Clear()
        {
            _stopLeft = 0f;
            Trauma = 0f;
            FovKick = 0f;
            Time.timeScale = 1f;
        }
    }
}
