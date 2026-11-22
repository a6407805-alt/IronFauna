using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Arena announcer: a broadcast banner for everything, plus Halo-style voiced callouts (via <see cref="AnnouncerVoice"/>)
    /// for key moments only: fight start, first blood, combo streaks, multi-knockouts and KO sprees, parries, ripostes,
    /// guard breaks, torn limbs, knockouts, ring outs, victory / defeat, champion and inspection busts.
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

        private AnnouncerVoice _voice;

        // streak tracking (reset every round / bout)
        private int _combo;
        private float _comboLastT = -99f;
        private readonly List<float> _myKoTimes = new List<float>();
        private int _myKoTotal;
        private readonly Dictionary<string, float> _keyCooldown = new Dictionary<string, float>();

        public const float ComboWindow = 2.2f;
        public const float MultiKoWindow = 5f;

        /// <summary>Every voiced callout: key (also the file name for a recorded clip in Resources/Announcer/) and spoken text.</summary>
        public static readonly (string key, string text)[] Callouts =
        {
            ("fight", "Fight!"),
            ("quarterfinal", "Quarterfinal... Fight!"),
            ("semifinal", "Semifinal... Fight!"),
            ("grand_final", "The grand final... Fight!"),
            ("first_blood", "First blood!"),
            ("combo_3", "Triple strike!"),
            ("combo_4", "Quad strike!"),
            ("combo_5", "Overdrive!"),
            ("combo_6", "Scrap storm!"),
            ("combo_8", "Unstoppable!"),
            ("knockout", "Knockout!"),
            ("double_ko", "Double knockout!"),
            ("triple_ko", "Triple knockout!"),
            ("overkill", "Overkill!"),
            ("rampage", "Rampage!"),
            ("massacre", "Scrapyard massacre!"),
            ("ring_out", "Ring out!"),
            ("final_two", "Final two!"),
            ("headhunter", "Headhunter!"),
            ("arm_off", "Arm torn off!"),
            ("leg_off", "Leg ripped off!"),
            ("dismembered", "Dismembered!"),
            ("denied", "Denied!"),
            ("punished", "Punished!"),
            ("guard_break", "Guard break!"),
            ("counter", "Counter!"),
            ("victory", "Victory!"),
            ("defeat", "Defeat!"),
            ("match_over", "That's the match!"),
            ("champion", "We have a new champion!"),
            ("busted", "Busted!"),
        };

        private void Awake()
        {
            _voice = gameObject.AddComponent<AnnouncerVoice>();
            foreach (var c in Callouts) _voice.Prewarm(c.key, c.text);
        }

        private static string TextFor(string key)
        {
            foreach (var c in Callouts) if (c.key == key) return c.text;
            return null;
        }

        private void Voice(string key, int priority, float keyCooldown = 0f)
        {
            if (_voice == null || key == null) return;
            if (keyCooldown > 0f)
            {
                if (_keyCooldown.TryGetValue(key, out float until) && Time.unscaledTime < until) return;
                _keyCooldown[key] = Time.unscaledTime + keyCooldown;
            }
            _voice.Say(key, TextFor(key), priority);
        }

        private void ResetStreaks()
        {
            _combo = 0;
            _comboLastT = -99f;
            _myKoTimes.Clear();
            _myKoTotal = 0;
            _keyCooldown.Clear();
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
            var liveTxt = MenuUI.Label(liveBadge.transform, "LIVE", 14, IronFaunaPalette.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
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
            ResetStreaks();
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
                        line = "FOUNDRY CRUCIBLE — MIND THE MOLTEN SEAMS!";
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
            Announce(line, 3.6f, 2, "fight");
        }

        public void OnHeavyHit(string attacker, string victim, float damage)
        {
            if (!_firstBloodDone && damage >= 8f)
            {
                _firstBloodDone = true;
                Announce($"FIRST BLOOD! {attacker} ROCKS {victim}!", 2.5f, 2, "first_blood");
                return;
            }
            if (damage >= 18f && _cooldown <= 0f)
            {
                string line = HeavyHitLines[UnityEngine.Random.Range(0, HeavyHitLines.Length)];
                Announce($"{attacker}: {line}", 2.2f, 1);
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
            string say = limb == LimbId.Head ? "headhunter"
                : (limb == LimbId.LegL || limb == LimbId.LegR) ? (legsLeft == 0 ? "dismembered" : "leg_off")
                : armsLeft == 0 ? "dismembered" : "arm_off";
            Announce(call, 3.2f, 3, say);
        }

        public void OnGrappleMove(string who, string moveCode)
        {
            if (moveCode == "SUPLEX")
                Announce($"WHAT A THROW! {who} LANDS A BACK-ARCH SUPLEX!", 2.7f, 2);
            else if (moveCode == "SLAM")
                Announce($"OVERHEAD SLAM BY {who} SHAKES THE ARENA!", 2.5f, 2);
            else if (moveCode == "JOINT CRANK")
                Announce($"JIU-JITSU SUBMISSION CRANK ON THE MAT BY {who}!", 2.5f, 2);
            else if (moveCode.Contains("NO LEGS"))
                Announce($"{who} TRIED TO KICK WITH NO LEGS — OFF BALANCE!", 2.6f, 2);
            else if (moveCode.Contains("1-LEG KICK"))
                Announce($"{who} KICKED WITH THEIR ONLY LEG AND TOPPLED OVER!", 2.6f, 2);
        }

        public void OnTreeFelled()
        {
            BoxingSfx.TimberCreak();
            Announce("TIMBER! A GIANT REDWOOD TREE IS CRASHING DOWN!", 3.0f, 3);
        }

        public void OnPropShattered(float size)
        {
            if (size >= 0.45f && _cooldown <= 0f)
                Announce("STRUCTURAL COLLAPSE — COVER SMASHED TO RUBBLE!", 2.2f, 1);
        }

        public void OnElimination(string who, bool ringOut, int aliveRemaining)
        {
            if (ringOut)
                Announce($"RING OUT! {who} IS HURLED INTO THE ABYSS!", 2.8f, 3, "ring_out");
            else if (aliveRemaining == 2)
                Announce($"{who} IS OUT — DOWN TO THE FINAL TWO!", 2.8f, 3, "final_two");
            else
                Announce($"KNOCKOUT! {who} IS REDUCED TO SCRAP!", 2.5f, 2, "knockout");
        }

        /// <param name="playerWon">true = "Victory!", false = "Defeat!", null = "That's the match!"</param>
        public void OnMatchEnd(string headline, bool? playerWon = null)
        {
            BoxingSfx.AnnouncerBell();
            string key = playerWon == true ? "victory" : playerWon == false ? "defeat" : "match_over";
            Announce($"MATCH OVER — {headline}!", 3.8f, 4, key);
        }

        public void OnTournamentBoutStart(string roundName, string opponentName, string opponentStyle, BrawlStageId stage, bool isBoss)
        {
            _firstBloodDone = false;
            _lastPriority = 0;
            ResetStreaks();
            BoxingSfx.AnnouncerBell();
            string stageName = BrawlTerrain.StageName(stage);
            string line = isBoss
                ? $"{roundName} AT {stageName} — CHALLENGER VERSUS DEFENDING CHAMPION {opponentName} — FIGHT!"
                : $"{roundName} AT {stageName} — YOU VERSUS {opponentName}, {opponentStyle} — LET'S SCRAP!";
            string key = isBoss || roundName.Contains("GRAND") ? "grand_final" : roundName.Contains("SEMI") ? "semifinal" : roundName.Contains("QUARTER") ? "quarterfinal" : "fight";
            Announce(line, 4.2f, 3, key);
        }

        public void OnTournamentChampion()
        {
            BoxingSfx.AnnouncerBell();
            Announce("UNBELIEVABLE! WE HAVE A NEW IRONFAUNA GRAND TOURNAMENT CHAMPION!", 4.6f, 5, "champion");
        }

        // ------------------------------------------------------------------ Halo-style streaks & medals
        /// <summary>Call for every landed hit. Tracks your combo streak (hits chained within <see cref="ComboWindow"/> s
        /// without getting hit back) and calls TRIPLE STRIKE, QUAD STRIKE, OVERDRIVE, SCRAP STORM, UNSTOPPABLE.</summary>
        public void NoteHit(bool byPlayer, bool onPlayer)
        {
            float now = Time.unscaledTime;
            if (onPlayer) { _combo = 0; return; }
            if (!byPlayer) return;
            _combo = now - _comboLastT <= ComboWindow ? _combo + 1 : 1;
            _comboLastT = now;
            string key = null, banner = null;
            switch (_combo)
            {
                case 3: key = "combo_3"; banner = "TRIPLE STRIKE!"; break;
                case 4: key = "combo_4"; banner = "QUAD STRIKE!"; break;
                case 5: key = "combo_5"; banner = "OVERDRIVE!"; break;
                case 6: key = "combo_6"; banner = "SCRAP STORM!"; break;
                case 8: key = "combo_8"; banner = "UNSTOPPABLE!"; break;
            }
            if (key != null) Announce($"{banner}  {_combo}-HIT COMBO", 1.8f, 3, key);
        }

        /// <summary>Call when the player's side scores a knockout: DOUBLE / TRIPLE KNOCKOUT, OVERKILL within
        /// <see cref="MultiKoWindow"/> s, and RAMPAGE / SCRAPYARD MASSACRE for 3 / 5 knockouts in one match.</summary>
        public void NotePlayerKnockout()
        {
            float now = Time.unscaledTime;
            _myKoTimes.RemoveAll(t => now - t > MultiKoWindow);
            _myKoTimes.Add(now);
            _myKoTotal++;
            int multi = _myKoTimes.Count;
            if (multi == 2) Announce("DOUBLE KNOCKOUT!", 2.2f, 4, "double_ko");
            else if (multi == 3) Announce("TRIPLE KNOCKOUT!", 2.2f, 4, "triple_ko");
            else if (multi >= 4) Announce("OVERKILL!", 2.4f, 4, "overkill");
            else if (_myKoTotal == 3) Announce("RAMPAGE! THREE KNOCKOUTS", 2.2f, 3, "rampage");
            else if (_myKoTotal == 5) Announce("SCRAPYARD MASSACRE! FIVE KNOCKOUTS", 2.4f, 4, "massacre");
        }

        public void OnPlayerParry() => Announce("DENIED! PARRY - RIPOSTE NOW!", 1.5f, 2, "denied", 3f);
        public void OnPlayerRiposte() => Announce("PUNISHED!", 1.5f, 3, "punished", 3f);
        public void OnPlayerGuardBreak() => Announce("GUARD BREAK!", 1.5f, 2, "guard_break", 4f);
        public void OnPlayerCounter() => Announce("COUNTER!", 1.2f, 1, "counter", 6f);
        public void OnInspectionBust() => Announce("BUSTED! ILLEGAL MODS CONFISCATED", 3f, 4, "busted");

        /// <summary>
        /// Shows a line in the broadcast banner and, when <paramref name="voiceKey"/> is set, queues that voiced callout.
        /// Hits, grapples and smashed props are banner-only so the voice never narrates everything.
        /// </summary>
        private void Announce(string text, float duration, int priority, string voiceKey = null, float voiceCooldown = 0f)
        {
            if (voiceKey != null) Voice(voiceKey, priority, voiceCooldown);
            if (_holdT > 0.2f && priority < _lastPriority) return;
            _lastPriority = priority;
            _holdT = duration;
            _popT = 0f;
            _cooldown = 1.6f;
            if (_lineText != null) _lineText.text = text;
            if (_panel != null) _panel.gameObject.SetActive(true);
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
