using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Live ringside announcer with real Windows SAPI Text-To-Speech (TTS) + synthesised stadium stings and
    /// a televised MMA broadcast banner with animated audio equalizer bars.
    /// </summary>
    public class BrawlAnnouncer : MonoBehaviour
    {
        private RectTransform _panel;
        private Text _speakerTag;
        private Text _lineText;
        private readonly RectTransform[] _eqBars = new RectTransform[6];
        private float _holdT;
        private float _popT;
        private float _cooldown;
        private int _lastPriority;
        private bool _firstBloodDone;

        private Process _ttsProc;

        private static readonly string[] HeavyHitLines =
        {
            "CRUSHING HAYMAKER CONNECTS!",
            "HEAVY METAL IMPACT — ARMOR CRACKING!",
            "BRUTAL STRIKE ROCKS THE CHASSIS!",
            "SPARKS FLY ON A CLEAN COUNTER!",
        };

        private void Awake()
        {
            StartTtsWorker();
        }

        private void OnDestroy()
        {
            StopTtsWorker();
        }

        private void StartTtsWorker()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"$v = New-Object -ComObject SAPI.SpVoice; $v.Rate = 2; $v.Volume = 100; while (($line = [Console]::In.ReadLine()) -ne $null) { if ($line.Length -gt 0) { [void]$v.Speak($line, 3) } }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                };
                _ttsProc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[IronFauna TTS] Could not start SAPI voice worker ({ex.Message}). Using synth fallback.");
                _ttsProc = null;
            }
#endif
        }

        private void StopTtsWorker()
        {
            if (_ttsProc == null) return;
            try
            {
                if (!_ttsProc.HasExited)
                {
                    _ttsProc.StandardInput.Close();
                    _ttsProc.Kill();
                }
                _ttsProc.Dispose();
            }
            catch { }
            _ttsProc = null;
        }

        private void SpeakTts(string rawText)
        {
            if (_ttsProc == null || _ttsProc.HasExited) return;
            try
            {
                string spoken = rawText
                    .Replace("ACT VII", "Act 7")
                    .Replace("ACT VI", "Act 6")
                    .Replace("ACT V", "Act 5")
                    .Replace("ACT IV", "Act 4")
                    .Replace("ACT III", "Act 3")
                    .Replace("ACT II", "Act 2")
                    .Replace("ACT I", "Act 1")
                    .Replace("1-LEG", "one leg")
                    .Replace("8P", "8 player")
                    .Replace("4v4", "4 versus 4")
                    .Replace("—", ", ")
                    .Replace("·", ", ")
                    .Replace("//", ", ");
                spoken = Regex.Replace(spoken, @"\[.*?\]", "");
                _ttsProc.StandardInput.WriteLine(spoken);
                _ttsProc.StandardInput.Flush();
            }
            catch { }
        }

        public void BuildUi(Transform canvasRoot)
        {
            var box = MenuUI.Panel(canvasRoot, "AnnouncerBar", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.90f));
            _panel = box.rectTransform;
            MenuUI.Place(_panel, new Vector2(0.32f, 1f), new Vector2(0.68f, 1f), new Vector2(0f, -118f), new Vector2(0f, -20f));

            var accentTop = MenuUI.Panel(box.transform, "AccentTop", IronFaunaPalette.OrangeJointCore);
            MenuUI.Place(accentTop.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), Vector2.zero);

            var accentBottom = MenuUI.Panel(box.transform, "AccentBottom", IronFaunaPalette.UiSelect);
            MenuUI.Place(accentBottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));

            var liveBadge = MenuUI.Panel(box.transform, "LiveBadge", IronFaunaPalette.CrimsonFront);
            MenuUI.Place(liveBadge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(126f, -10f));
            var liveTxt = MenuUI.Label(liveBadge.transform, "LIVE TTS", 14, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Fill(liveTxt.rectTransform);

            _speakerTag = MenuUI.Label(box.transform, "RINGSIDE ANNOUNCER // PLAY-BY-PLAY", 15, IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(_speakerTag.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(138f, -36f), new Vector2(-90f, -8f));

            // Animated audio equalizer bars on the top-right of the broadcast banner
            for (int i = 0; i < _eqBars.Length; i++)
            {
                var bar = MenuUI.Panel(box.transform, "Eq_" + i, IronFaunaPalette.CyanEnergy);
                float x0 = -76f + i * 10f;
                MenuUI.Place(bar.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(x0, -34f), new Vector2(x0 + 6f, -12f));
                bar.rectTransform.pivot = new Vector2(0.5f, 0f);
                _eqBars[i] = bar.rectTransform;
            }

            _lineText = MenuUI.Label(box.transform, "", 23, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(_lineText.rectTransform, new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.62f), Vector2.zero, Vector2.zero);
        }

        public void OnRoundStart(BrawlStageId stage, BrawlGameMode mode, string actTitle)
        {
            _firstBloodDone = false;
            _lastPriority = 0;
            BoxingSfx.AnnouncerBell();
            string line;
            if (mode == BrawlGameMode.CoopCampaign)
            {
                line = $"{actTitle} — PROTECT YOUR SQUAD AND CRUSH THEM!";
            }
            else
            {
                switch (stage)
                {
                    case BrawlStageId.NeoMetroCity:
                        line = "WELCOME TO NEO-METRO CITY — LEVEL EVERY BUILDING!";
                        break;
                    case BrawlStageId.RedwoodForest:
                        line = "REDWOOD TIMBERLAND — CHOP THE TREES AND WATCH YOUR HEAD!";
                        break;
                    case BrawlStageId.FoundryCrucible:
                        line = "FOUNDRY CRUCIBLE — WATCH THOSE EXPLOSIVE FUEL CELLS!";
                        break;
                    case BrawlStageId.OrbitalDish:
                        line = "HIGH ABOVE EARTH ON THE ORBITAL DISH — LET'S SCRAP!";
                        break;
                    case BrawlStageId.GlacierTrench:
                        line = "SUBZERO GLACIER TRENCH — DON'T SLIP OFF THE RIDGE!";
                        break;
                    case BrawlStageId.ScrapyardColosseum:
                        line = "WELCOME TO THE SCRAPYARD COLOSSEUM — NO MERCY!";
                        break;
                    default:
                        line = "8 MECHS ENTER BASALT MESA — TEAR THEM APART!";
                        break;
                }
            }
            Announce(line, 3.6f, 2, 0, 0.9f);
        }

        public void OnHeavyHit(string attacker, string victim, float damage)
        {
            if (!_firstBloodDone && damage >= 8f)
            {
                _firstBloodDone = true;
                Announce($"FIRST BLOOD! {attacker} ROCKS {victim}!", 2.5f, 2, 1, 0.8f);
                return;
            }
            if (damage >= 18f && _cooldown <= 0f)
            {
                string line = HeavyHitLines[UnityEngine.Random.Range(0, HeavyHitLines.Length)];
                Announce($"{attacker}: {line}", 2.2f, 1, 1, 0.75f);
            }
        }

        public void OnLimbRipped(string victim, LimbId limb, int legsLeft, int armsLeft)
        {
            string call;
            if (limb == LimbId.Head)
            {
                call = $"DECAPITATION! {victim}'S HEAD IS TORN CLEAN OFF!";
            }
            else if (limb == LimbId.LegL || limb == LimbId.LegR)
            {
                call = legsLeft == 0
                    ? $"BOTH LEGS GONE! {victim} IS CRAWLING ON THE FLOOR!"
                    : $"LEG RIPPED OFF! {victim} IS HOPPING ON ONE LEG!";
            }
            else
            {
                call = armsLeft == 0
                    ? $"NO ARMS LEFT! {victim} CAN ONLY HEADBUTT AND KICK!"
                    : $"ARM TORN OUT AT THE SOCKET ON {victim}!";
            }
            Announce(call, 3.2f, 3, 2, 1.0f);
        }

        public void OnGrappleMove(string who, string moveCode)
        {
            if (moveCode == "SUPLEX")
                Announce($"WHAT A THROW! {who} LANDS A BACK-ARCH SUPLEX!", 2.7f, 2, 3, 0.95f);
            else if (moveCode == "SLAM")
                Announce($"OVERHEAD SLAM BY {who} SHAKES THE ARENA!", 2.5f, 2, 1, 0.9f);
            else if (moveCode == "JOINT CRANK")
                Announce($"JIU-JITSU SUBMISSION CRANK ON THE MAT BY {who}!", 2.5f, 2, 2, 0.85f);
            else if (moveCode.Contains("NO LEGS"))
                Announce($"{who} TRIED TO KICK WITH NO LEGS — OFF BALANCE!", 2.6f, 2, 3, 0.9f);
            else if (moveCode.Contains("1-LEG KICK"))
                Announce($"{who} KICKED WITH THEIR ONLY LEG AND TOPPLED OVER!", 2.6f, 2, 3, 0.9f);
        }

        public void OnTreeFelled()
        {
            BoxingSfx.TimberCreak();
            Announce("TIMBER! A GIANT REDWOOD TREE IS CRASHING DOWN!", 3.0f, 3, 0, 1.0f);
        }

        public void OnPropShattered(float size)
        {
            if (size >= 1.0f)
                Announce("EXPLOSIVE DETONATION ROCKS THE ARENA!", 2.4f, 2, 1, 0.95f);
            else if (size >= 0.45f && _cooldown <= 0f)
                Announce("STRUCTURAL COLLAPSE — COVER SMASHED TO RUBBLE!", 2.2f, 1, 2, 0.8f);
        }

        public void OnElimination(string who, bool ringOut, int aliveRemaining)
        {
            if (ringOut)
                Announce($"RING OUT! {who} IS HURLED INTO THE ABYSS!", 2.8f, 3, 3, 1.0f);
            else if (aliveRemaining == 2)
                Announce($"{who} IS OUT — DOWN TO THE FINAL TWO!", 2.8f, 3, 0, 0.95f);
            else
                Announce($"KNOCKOUT! {who} IS REDUCED TO SCRAP!", 2.5f, 2, 1, 0.9f);
        }

        public void OnMatchEnd(string headline)
        {
            BoxingSfx.AnnouncerBell();
            Announce($"MATCH OVER — {headline}!", 3.8f, 4, 0, 1.0f);
        }

        private void Announce(string text, float duration, int priority, int voiceStyle, float hype)
        {
            if (_holdT > 0.2f && priority < _lastPriority) return;
            _lastPriority = priority;
            _holdT = duration;
            _popT = 0f;
            _cooldown = 1.6f;
            if (_lineText != null) _lineText.text = text;
            if (_panel != null) _panel.gameObject.SetActive(true);
            BoxingSfx.AnnouncerVoice(voiceStyle, hype * 0.45f);
            SpeakTts(text);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_cooldown > 0f) _cooldown -= dt;
            if (_panel == null) return;

            if (_holdT > 0f)
            {
                _holdT -= dt;
                _popT = Mathf.MoveTowards(_popT, 1f, dt / 0.22f);
                float x = _popT - 1f;
                float scale = 1f + 2.7f * x * x * x + 1.7f * x * x;
                _panel.localScale = new Vector3(scale, scale, 1f);

                // Animate the equalizer bars while the announcer is speaking
                float t = Time.unscaledTime * 16f;
                for (int i = 0; i < _eqBars.Length; i++)
                {
                    if (_eqBars[i] == null) continue;
                    float h = _holdT > 0.4f ? (0.25f + 0.75f * Mathf.Abs(Mathf.Sin(t + i * 1.15f))) : 0.15f;
                    _eqBars[i].localScale = new Vector3(1f, h, 1f);
                }
            }
            else if (_panel.gameObject.activeSelf)
            {
                _panel.gameObject.SetActive(false);
                _lastPriority = 0;
            }
        }
    }
}
