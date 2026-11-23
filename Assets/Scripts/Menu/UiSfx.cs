using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronFauna.Core;

namespace IronFauna.Menu
{
    /// <summary>
    /// Menu sounds (owner 2026-11-22: "I'd like menu select noises"): a light metallic tick on hover, a servo + latch clunk
    /// on select, a lower double clunk for back. Clips in Resources/UI (synthesised for the game). Effects volume applies;
    /// they keep playing while a fight is paused.
    /// </summary>
    public static class UiSfx
    {
        private static AudioSource _src;
        private static AudioClip _hover, _select, _back;
        private static float _lastHover;

        private static bool Ensure()
        {
            if (_src != null) return true;
            var go = new GameObject("IronFauna_UiSfx");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.ignoreListenerPause = true;
            _src.priority = 8;
            _hover = Resources.Load<AudioClip>("UI/ui_hover");
            _select = Resources.Load<AudioClip>("UI/ui_select");
            _back = Resources.Load<AudioClip>("UI/ui_back");
            return true;
        }

        private static void Play(AudioClip clip, float vol)
        {
            if (!Ensure() || clip == null) return;
            _src.PlayOneShot(clip, vol * GameSettings.SfxVolume);
        }

        public static void Hover()
        {
            if (Time.unscaledTime - _lastHover < 0.045f) return;   // sweeping across a list stays a soft patter
            _lastHover = Time.unscaledTime;
            Play(_hover, 0.45f);
        }

        public static void Select() => Play(_select, 0.8f);
        public static void Back() => Play(_back, 0.8f);
    }

    /// <summary>Added to every MenuUI button: hover / click / gamepad submit sounds.</summary>
    public class UiSoundHook : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, ISelectHandler, ISubmitHandler
    {
        public bool isBack;
        private Selectable _sel;

        private void Awake() => _sel = GetComponent<Selectable>();
        private bool Live => _sel == null || (_sel.interactable && _sel.isActiveAndEnabled);

        private bool _pointerInside;
        public void OnPointerEnter(PointerEventData e) { _pointerInside = true; if (Live) UiSfx.Hover(); }
        public void OnPointerExit(PointerEventData e) => _pointerInside = false;
        // gamepad / keyboard navigation (a mouse click also selects the button: no tick on top of the clunk)
        public void OnSelect(BaseEventData e) { if (Live && !_pointerInside) UiSfx.Hover(); }
        public void OnPointerClick(PointerEventData e) { if (Live && e.button == PointerEventData.InputButton.Left) Click(); }
        public void OnSubmit(BaseEventData e) { if (Live) Click(); }

        private void Click()
        {
            if (isBack) UiSfx.Back(); else UiSfx.Select();
        }
    }
}
