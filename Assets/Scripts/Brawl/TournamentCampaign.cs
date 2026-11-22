using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using IronFauna.Clamshell;
using IronFauna.Core;
using IronFauna.Menu;

namespace IronFauna.Brawl
{
    /// <summary>
    /// 8-Mech Single-Elimination Tournament Campaign ("IRONFAUNA GRAND CIRCUIT"):
    ///   Quarterfinals (Scrapyard Colosseum) -> Semifinals (Foundry Crucible) -> Grand Final (Orbital Dish).
    /// Simulates AI-vs-AI bouts on the rest of the bracket after each round and renders a live
    /// interactive uGUI bracket & scouting overlay styled exclusively with IronFaunaPalette.
    /// </summary>
    public sealed class TournamentEntrant
    {
        public int seed;
        public string callsign;
        public string styleTitle;
        public string roleNote;
        public string weightTag;
        public bool isPlayer;
        public bool isBoss;
        public int presetIndex;
        public int paintScheme;
        public float skill;
        public float hpMult;
        public int wins;
        public bool eliminated;

        public MechLoadout BuildLoadout(MechLoadout playerLoadout)
        {
            if (isPlayer && playerLoadout != null) return playerLoadout.Clone();
            var l = MechLoadout.CreatePreset(presetIndex, out _, out _);
            l.paintScheme = paintScheme;
            return l;
        }
    }

    public sealed class TournamentMatch
    {
        public int roundIndex; // 0 = QF, 1 = SF, 2 = Final
        public int matchIndex;
        public TournamentEntrant fighterA;
        public TournamentEntrant fighterB;
        public TournamentEntrant winner;
        public bool completed;

        public bool IsPlayerMatch =>
            (fighterA != null && fighterA.isPlayer) || (fighterB != null && fighterB.isPlayer);

        public TournamentEntrant OpponentOfPlayer
        {
            get
            {
                if (fighterA != null && fighterA.isPlayer) return fighterB;
                if (fighterB != null && fighterB.isPlayer) return fighterA;
                return null;
            }
        }
    }

    public sealed class TournamentState
    {
        public static readonly string[] RoundNames = { "QUARTERFINAL", "SEMIFINAL", "GRAND FINAL" };
        public static readonly BrawlStageId[] RoundStages =
        {
            BrawlStageId.ScrapyardColosseum,
            BrawlStageId.FoundryCrucible,
            BrawlStageId.OrbitalDish,
        };

        public readonly TournamentMatch[] quarterfinals = new TournamentMatch[4];
        public readonly TournamentMatch[] semifinals = new TournamentMatch[2];
        public TournamentMatch grandFinal;

        public TournamentEntrant playerEntrant;
        /// <summary>Credits, carried part wear and mods for the player's rig (Repair Bay).</summary>
        public readonly TournamentGarage garage = new TournamentGarage();
        public int currentRound; // 0, 1, 2
        public bool tournamentWon;
        public bool playerEliminated;
        public string lastRoundFeed = "8 MECHS ENTER THE CIRCUIT. WIN 3 BOUTS TO CLAIM THE IRONFAUNA BELT.";

        public BrawlStageId CurrentStage => RoundStages[Mathf.Clamp(currentRound, 0, RoundStages.Length - 1)];
        public string CurrentRoundName => RoundNames[Mathf.Clamp(currentRound, 0, RoundNames.Length - 1)];

        public TournamentMatch ActivePlayerMatch
        {
            get
            {
                if (currentRound == 0)
                {
                    foreach (var m in quarterfinals) if (m.IsPlayerMatch) return m;
                }
                else if (currentRound == 1)
                {
                    foreach (var m in semifinals) if (m.IsPlayerMatch) return m;
                }
                else
                {
                    return grandFinal;
                }
                return null;
            }
        }

        public TournamentEntrant CurrentOpponent => ActivePlayerMatch != null ? ActivePlayerMatch.OpponentOfPlayer : null;

        public void GenerateNewBracket(MechLoadout playerLoadout)
        {
            currentRound = 0;
            tournamentWon = false;
            playerEliminated = false;
            garage.Reset();
            lastRoundFeed = "8 MECHS ENTER THE CIRCUIT. WIN 3 BOUTS TO CLAIM THE IRONFAUNA BELT.";

            string playerWeight = playerLoadout != null
                ? playerLoadout.ComputeCombatModifiers().weightLabel
                : "MEDIUM";

            playerEntrant = new TournamentEntrant
            {
                seed = 8,
                callsign = "YOU",
                styleTitle = "CHALLENGER",
                roleNote = "CUSTOM GARAGE RIG · FIGHTING UP FROM THE #8 SEED.",
                weightTag = playerWeight,
                isPlayer = true,
                isBoss = false,
                presetIndex = 0,
                paintScheme = playerLoadout != null ? playerLoadout.paintScheme : 0,
                skill = 0.7f,
                hpMult = 1.0f,
            };

            // Curated rival pool split into 3 tiers for a clean tournament difficulty curve:
            // Tier 1 (QF striker), Tier 2 (SF technical grappler/boxer), Tier 3 (Heavyweight & Defending Champion).
            var tier1 = new List<TournamentEntrant>
            {
                MakeRival(7, "MANTIS-07", "NAK MUAY STRIKER", 1, 4, 0.50f, 0.94f, false),
                MakeRival(6, "AFTERBURN", "BLITZ FEATHERWEIGHT", 4, 1, 0.52f, 0.92f, false),
            };
            var tier2 = new List<TournamentEntrant>
            {
                MakeRival(5, "RIPJAW", "MAT SUBMISSIONIST", 3, 5, 0.60f, 1.02f, false),
                MakeRival(4, "OVERCLOCK", "SPEED BOXER", 6, 2, 0.62f, 1.04f, false),
                MakeRival(3, "VOLTAIC", "CLINCH SUPLEXER", 7, 6, 0.65f, 1.08f, false),
                MakeRival(3, "AEGIS-IX", "ROOTED COUNTER", 9, 3, 0.64f, 1.10f, false),
            };
            var tier3 = new List<TournamentEntrant>
            {
                MakeRival(2, "SALVO", "IRON CRUSHER", 8, 6, 0.72f, 1.18f, false),
                MakeRival(2, "BASTION", "COLOSSUS TANK", 5, 3, 0.70f, 1.24f, false),
            };
            Shuffle(tier1);
            Shuffle(tier2);
            Shuffle(tier3);

            var champ = MakeRival(1, "APEX GOLIATH", "DEFENDING CHAMPION", 2, 0, 0.84f, 1.42f, true);

            // Seeded 8-fighter bracket:
            // Left Half (Player's path to the Final):
            //   QF0: YOU (#8) vs Tier1[0] (#7)
            //   QF1: Tier2[0] (#4) vs Tier2[1] (#5)
            // Right Half (Defending Champion's path):
            //   QF2: Tier2[2] (#3) vs Tier1[1] (#6)
            //   QF3: APEX GOLIATH (#1) vs Tier3[0] (#2)
            tier1[0].seed = 7;
            tier2[0].seed = 4;
            tier2[1].seed = 5;
            tier2[2].seed = 3;
            tier1[1].seed = 6;
            tier3[0].seed = 2;
            champ.seed = 1;

            quarterfinals[0] = new TournamentMatch { roundIndex = 0, matchIndex = 0, fighterA = playerEntrant, fighterB = tier1[0] };
            quarterfinals[1] = new TournamentMatch { roundIndex = 0, matchIndex = 1, fighterA = tier2[0], fighterB = tier2[1] };
            quarterfinals[2] = new TournamentMatch { roundIndex = 0, matchIndex = 2, fighterA = tier2[2], fighterB = tier1[1] };
            quarterfinals[3] = new TournamentMatch { roundIndex = 0, matchIndex = 3, fighterA = champ, fighterB = tier3[0] };

            semifinals[0] = new TournamentMatch { roundIndex = 1, matchIndex = 0 };
            semifinals[1] = new TournamentMatch { roundIndex = 1, matchIndex = 1 };
            grandFinal = new TournamentMatch { roundIndex = 2, matchIndex = 0 };
        }

        public void RecordPlayerResult(bool playerWon)
        {
            var match = ActivePlayerMatch;
            if (match == null) return;

            if (!playerWon)
            {
                playerEliminated = true;
                var opp = match.OpponentOfPlayer;
                lastRoundFeed = opp != null
                    ? $"KNOCKED OUT BY {opp.callsign} IN THE {CurrentRoundName}. REMATCH OR START A NEW CIRCUIT."
                    : $"ELIMINATED IN THE {CurrentRoundName}.";
                return;
            }

            // Mark player bout won
            match.completed = true;
            match.winner = playerEntrant;
            playerEntrant.wins++;
            if (match.OpponentOfPlayer != null) match.OpponentOfPlayer.eliminated = true;

            // Simulate all other bouts in this round so the bracket advances organically
            var simLines = new List<string>();
            if (currentRound == 0)
            {
                for (int i = 0; i < quarterfinals.Length; i++)
                {
                    var qf = quarterfinals[i];
                    if (!qf.completed)
                    {
                        SimAiMatch(qf);
                        simLines.Add($"{qf.winner.callsign} def. {LoserOf(qf).callsign}");
                    }
                }
                semifinals[0].fighterA = quarterfinals[0].winner;
                semifinals[0].fighterB = quarterfinals[1].winner;
                semifinals[1].fighterA = quarterfinals[2].winner;
                semifinals[1].fighterB = quarterfinals[3].winner;
                currentRound = 1;
            }
            else if (currentRound == 1)
            {
                for (int i = 0; i < semifinals.Length; i++)
                {
                    var sf = semifinals[i];
                    if (!sf.completed)
                    {
                        SimAiMatch(sf);
                        simLines.Add($"{sf.winner.callsign} def. {LoserOf(sf).callsign}");
                    }
                }
                grandFinal.fighterA = semifinals[0].winner;
                grandFinal.fighterB = semifinals[1].winner;
                currentRound = 2;
            }
            else
            {
                tournamentWon = true;
            }

            if (tournamentWon)
            {
                lastRoundFeed = "GRAND FINAL VICTORY! YOU ARE THE NEW IRONFAUNA CIRCUIT CHAMPION!";
            }
            else
            {
                lastRoundFeed = simLines.Count > 0
                    ? "BRACKET UPDATE: " + string.Join("  ·  ", simLines)
                    : $"ADVANCING TO THE {CurrentRoundName}!";
            }
        }

        public void PrepareRematch()
        {
            playerEliminated = false;
            lastRoundFeed = $"REMATCH SCHEDULED — {CurrentRoundName} AT {BrawlTerrain.StageName(CurrentStage)}.";
        }

        private static TournamentEntrant MakeRival(int seed, string callsign, string styleTitle, int presetIndex, int paintScheme, float skill, float hpMult, bool isBoss)
        {
            var l = MechLoadout.CreatePreset(presetIndex, out _, out string roleNote);
            string wTag = l.ComputeCombatModifiers().weightLabel;
            return new TournamentEntrant
            {
                seed = seed,
                callsign = callsign,
                styleTitle = styleTitle,
                roleNote = roleNote,
                weightTag = wTag,
                isPlayer = false,
                isBoss = isBoss,
                presetIndex = presetIndex,
                paintScheme = paintScheme % IronFaunaPalette.PaintSchemes.Length,
                skill = skill,
                hpMult = hpMult,
            };
        }

        private static void SimAiMatch(TournamentMatch m)
        {
            if (m.fighterA == null || m.fighterB == null) return;
            float scoreA = m.fighterA.skill + m.fighterA.hpMult * 0.25f + (m.fighterA.isBoss ? 0.14f : 0f) + UnityEngine.Random.Range(-0.18f, 0.18f);
            float scoreB = m.fighterB.skill + m.fighterB.hpMult * 0.25f + (m.fighterB.isBoss ? 0.14f : 0f) + UnityEngine.Random.Range(-0.18f, 0.18f);
            bool aWins = scoreA >= scoreB;
            m.winner = aWins ? m.fighterA : m.fighterB;
            var loser = aWins ? m.fighterB : m.fighterA;
            m.winner.wins++;
            loser.eliminated = true;
            m.completed = true;
        }

        private static TournamentEntrant LoserOf(TournamentMatch m)
        {
            if (m.winner == m.fighterA) return m.fighterB;
            return m.fighterA;
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>
    /// Full-screen interactive Tournament Bracket & Scouting Overlay built in code with MenuUI + IronFaunaPalette.
    /// Shown before the first bout, between rounds as the bracket advances, and upon championship victory or KO.
    /// </summary>
    public sealed class TournamentBracketOverlay
    {
        private Canvas _canvas;
        private Transform _contentRoot;

        public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            MenuUI.EnsureEventSystem();
            _canvas = MenuUI.CreateCanvas("TournamentBracketCanvas", 20);
            _canvas.transform.SetParent(parent, false);
            _contentRoot = _canvas.transform;
            _canvas.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        public void Show(
            TournamentState state,
            int localPlayers,
            UnityAction onFight,
            UnityAction onRematch,
            UnityAction onNewTournament,
            UnityAction onMainMenu,
            UnityAction onRepairBay = null)
        {
            if (_canvas == null || state == null) return;

            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_contentRoot.GetChild(i).gameObject);
            }

            _canvas.gameObject.SetActive(true);

            // Full-screen dark industrial backdrop
            var bg = MenuUI.Panel(_contentRoot, "Backdrop", IronFaunaPalette.WithAlpha(IronFaunaPalette.StudioBackdrop, 0.94f));
            MenuUI.Fill(bg.rectTransform);

            // Top championship header bar
            var header = MenuUI.Panel(_contentRoot, "Header", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.96f));
            MenuUI.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -116f), Vector2.zero);
            var headerRule = MenuUI.Panel(header.transform, "HeaderRule", state.tournamentWon ? IronFaunaPalette.HazardYellow : IronFaunaPalette.OrangeJointCore);
            MenuUI.Place(headerRule.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 5f));

            string titleText = state.tournamentWon
                ? "IRONFAUNA GRAND TOURNAMENT  //  CHAMPION CROWNED"
                : state.playerEliminated
                ? $"IRONFAUNA GRAND TOURNAMENT  //  ELIMINATED ({state.CurrentRoundName})"
                : $"IRONFAUNA GRAND TOURNAMENT  //  {state.CurrentRoundName}";
            var title = MenuUI.Label(header.transform, titleText, 44, state.tournamentWon ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.75f, 1f), new Vector2(44f, 6f), Vector2.zero);

            string recStr = (localPlayers > 1
                ? $"{localPlayers}P TAG CIRCUIT  //  WINS: {state.playerEntrant.wins}/3"
                : $"SOLO CIRCUIT  //  WINS: {state.playerEntrant.wins}/3")
                + $"  //  {state.garage.credits:N0} CR";
            var rec = MenuUI.Label(header.transform, recStr, 24, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(rec.rectTransform, new Vector2(0.68f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-44f, 0f));

            // Left / Center Bracket Container (x: 0.02..0.67)
            var bracketPanel = MenuUI.Panel(_contentRoot, "BracketArea", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.88f));
            MenuUI.Place(bracketPanel.rectTransform, new Vector2(0.02f, 0.11f), new Vector2(0.67f, 0.89f), Vector2.zero, Vector2.zero);
            var bracketTop = MenuUI.Panel(bracketPanel.transform, "BracketTopRule", IronFaunaPalette.UiSelect);
            MenuUI.Place(bracketTop.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);

            // Column Titles
            BuildColumnTitle(bracketPanel.transform, "QUARTERFINALS", 0.02f, 0.31f, state.currentRound == 0 && !state.tournamentWon);
            BuildColumnTitle(bracketPanel.transform, "SEMIFINALS", 0.35f, 0.64f, state.currentRound == 1 && !state.tournamentWon);
            BuildColumnTitle(bracketPanel.transform, "GRAND FINAL", 0.68f, 0.98f, state.currentRound == 2 || state.tournamentWon);

            // Column 1: 4 Quarterfinal cards
            float[] qfY = { 0.72f, 0.50f, 0.27f, 0.05f };
            for (int i = 0; i < 4; i++)
            {
                BuildMatchCard(bracketPanel.transform, state.quarterfinals[i], 0.02f, 0.31f, qfY[i], qfY[i] + 0.17f, state.currentRound == 0 && !state.tournamentWon && !state.playerEliminated);
            }

            // Column 2: 2 Semifinal cards
            float[] sfY = { 0.61f, 0.16f };
            for (int i = 0; i < 2; i++)
            {
                BuildMatchCard(bracketPanel.transform, state.semifinals[i], 0.35f, 0.64f, sfY[i], sfY[i] + 0.17f, state.currentRound == 1 && !state.tournamentWon && !state.playerEliminated);
            }

            // Column 3: Grand Final card + Champion Trophy box
            BuildMatchCard(bracketPanel.transform, state.grandFinal, 0.68f, 0.98f, 0.44f, 0.64f, state.currentRound == 2 && !state.tournamentWon && !state.playerEliminated);
            BuildChampionBox(bracketPanel.transform, state, 0.68f, 0.98f, 0.14f, 0.36f);

            // Subtle connector bars between columns
            DrawConnector(bracketPanel.transform, 0.31f, 0.35f, 0.80f, 0.58f, 0.695f);
            DrawConnector(bracketPanel.transform, 0.31f, 0.35f, 0.35f, 0.13f, 0.245f);
            DrawConnector(bracketPanel.transform, 0.64f, 0.68f, 0.695f, 0.245f, 0.54f);

            // Right Panel: Next Bout / Scouting Report & Action Buttons (x: 0.69..0.98)
            var scoutPanel = MenuUI.Panel(_contentRoot, "ScoutingPanel", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.92f));
            MenuUI.Place(scoutPanel.rectTransform, new Vector2(0.69f, 0.11f), new Vector2(0.98f, 0.89f), Vector2.zero, Vector2.zero);
            var scoutRule = MenuUI.Panel(scoutPanel.transform, "ScoutTopRule", state.tournamentWon ? IronFaunaPalette.HazardYellow : IronFaunaPalette.CrimsonFront);
            MenuUI.Place(scoutRule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -5f), Vector2.zero);

            BuildScoutingCard(scoutPanel.transform, state, localPlayers, onFight, onRematch, onNewTournament, onMainMenu, onRepairBay);

            // Bottom Ticker Bar (last simulated results + controls hint)
            var footer = MenuUI.Panel(_contentRoot, "FooterBar", IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.92f));
            MenuUI.Place(footer.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.09f), Vector2.zero, Vector2.zero);
            var footerRule = MenuUI.Panel(footer.transform, "FooterRule", IronFaunaPalette.UiButton);
            MenuUI.Place(footerRule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);

            var feed = MenuUI.Label(footer.transform, state.lastRoundFeed, 20, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(feed.rectTransform, new Vector2(0.015f, 0f), new Vector2(0.75f, 1f), Vector2.zero, Vector2.zero);

            var hint = MenuUI.Label(footer.transform, "ENTER / SPACE: START BOUT   //   ESC: MAIN MENU   //   LAND HITS = CREDITS", 18, IronFaunaPalette.UiTextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(hint.rectTransform, new Vector2(0.75f, 0f), new Vector2(0.985f, 1f), Vector2.zero, Vector2.zero);
        }

        private static void BuildColumnTitle(Transform parent, string text, float x0, float x1, bool active)
        {
            var lbl = MenuUI.Label(parent, text, 22, active ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(lbl.rectTransform, new Vector2(x0, 0.91f), new Vector2(x1, 0.98f), Vector2.zero, Vector2.zero);
        }

        private static void BuildMatchCard(Transform parent, TournamentMatch m, float x0, float x1, float y0, float y1, bool isActiveRound)
        {
            bool isPlayerNext = isActiveRound && m != null && m.IsPlayerMatch && !m.completed;
            Color bgCol = isPlayerNext
                ? IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, IronFaunaPalette.OrangeJointCore, 0.16f)
                : IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.90f);
            Color edgeCol = isPlayerNext
                ? IronFaunaPalette.HazardYellow
                : (m != null && m.completed ? IronFaunaPalette.CyanEnergy : IronFaunaPalette.UiButton);

            var card = MenuUI.Panel(parent, "MatchCard", bgCol);
            MenuUI.Place(card.rectTransform, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);

            var stripe = MenuUI.Panel(card.transform, "Edge", edgeCol);
            MenuUI.Place(stripe.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 0f));

            var divider = MenuUI.Panel(card.transform, "Divider", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiTextDim, 0.35f));
            MenuUI.Place(divider.rectTransform, new Vector2(0.05f, 0.49f), new Vector2(0.95f, 0.51f), Vector2.zero, Vector2.zero);

            BuildFighterSlotRow(card.transform, m?.fighterA, m, true);
            BuildFighterSlotRow(card.transform, m?.fighterB, m, false);

            if (isPlayerNext)
            {
                var badge = MenuUI.Panel(card.transform, "NextBadge", IronFaunaPalette.HazardYellow);
                MenuUI.Place(badge.rectTransform, new Vector2(0.68f, 0.88f), new Vector2(0.98f, 1.06f), Vector2.zero, Vector2.zero);
                var bTxt = MenuUI.Label(badge.transform, "NEXT BOUT", 14, IronFaunaPalette.DarkIron, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Fill(bTxt.rectTransform);
            }
        }

        private static void BuildFighterSlotRow(Transform card, TournamentEntrant e, TournamentMatch m, bool top)
        {
            float yMin = top ? 0.52f : 0.04f;
            float yMax = top ? 0.96f : 0.48f;

            if (e == null)
            {
                var tbd = MenuUI.Label(card, "TBD", 18, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
                MenuUI.Place(tbd.rectTransform, new Vector2(0.06f, yMin), new Vector2(0.95f, yMax), Vector2.zero, Vector2.zero);
                return;
            }

            bool isWinner = m != null && m.completed && m.winner == e;
            bool isLoser = m != null && m.completed && m.winner != e;

            Color nameCol = isLoser
                ? IronFaunaPalette.UiTextDim
                : e.isPlayer ? IronFaunaPalette.CyanEnergy
                : e.isBoss ? IronFaunaPalette.OrangeJointCore
                : isWinner ? IronFaunaPalette.HazardYellow
                : IronFaunaPalette.UiText;

            string nameStr = $"#{e.seed}  {e.callsign}";
            var nameLbl = MenuUI.Label(card, nameStr, 20, nameCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(nameLbl.rectTransform, new Vector2(0.06f, yMin), new Vector2(0.76f, yMax), Vector2.zero, Vector2.zero);

            string statusStr = isWinner ? "WIN" : isLoser ? "KO" : ShortWeight(e.weightTag);
            Color statusCol = isWinner ? IronFaunaPalette.CyanEnergy : isLoser ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.UiTextDim;
            var statusLbl = MenuUI.Label(card, statusStr, 16, statusCol, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(statusLbl.rectTransform, new Vector2(0.74f, yMin), new Vector2(0.95f, yMax), Vector2.zero, Vector2.zero);
        }

        private static void BuildChampionBox(Transform parent, TournamentState state, float x0, float x1, float y0, float y1)
        {
            bool crowned = state.tournamentWon && state.grandFinal != null && state.grandFinal.winner != null;
            Color boxCol = crowned
                ? IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, IronFaunaPalette.HazardYellow, 0.22f)
                : IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.72f);

            var box = MenuUI.Panel(parent, "ChampionBox", boxCol);
            MenuUI.Place(box.rectTransform, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
            var topRule = MenuUI.Panel(box.transform, "ChampRule", crowned ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiButton);
            MenuUI.Place(topRule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);

            var hdr = MenuUI.Label(box.transform, "CIRCUIT CHAMPION", 18, IronFaunaPalette.HazardYellow, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(hdr.rectTransform, new Vector2(0.05f, 0.65f), new Vector2(0.95f, 0.94f), Vector2.zero, Vector2.zero);

            string champText = crowned ? state.grandFinal.winner.callsign + "  (3-0)" : "UNDECIDED";
            var val = MenuUI.Label(box.transform, champText, crowned ? 32 : 24, crowned ? IronFaunaPalette.UiText : IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(val.rectTransform, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.65f), Vector2.zero, Vector2.zero);
        }

        private static void BuildScoutingCard(
            Transform parent,
            TournamentState state,
            int localPlayers,
            UnityAction onFight,
            UnityAction onRematch,
            UnityAction onNewTournament,
            UnityAction onMainMenu,
            UnityAction onRepairBay)
        {
            var stack = MenuUI.NewRect(parent, "ScoutStack");
            MenuUI.Place(stack, Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -24f));
            MenuUI.VStack(stack.gameObject, 12f, 0);

            if (state.tournamentWon)
            {
                var crown = MenuUI.Label(stack, "GRAND CHAMPION!", 38, IronFaunaPalette.HazardYellow, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(crown, 52f);

                var desc = MenuUI.Label(stack,
                    "YOU CONQUERED ALL THREE ROUNDS OF THE IRONFAUNA GRAND CIRCUIT AND CLAIMED THE CHAMPIONSHIP BELT.\n\nSTART A FRESH BRACKET TO DEFEND YOUR CROWN AGAINST A NEW DRAW OF RIVALS.",
                    21, IronFaunaPalette.UiText, TextAnchor.UpperLeft);
                MenuUI.Size(desc, 220f);

                var spacer = MenuUI.NewRect(stack, "Spacer");
                MenuUI.Size(spacer, 140f);

                AddActionButton(stack, "NEW TOURNAMENT", IronFaunaPalette.HazardYellow, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.DarkIron, onNewTournament);
                AddActionButton(stack, "MAIN MENU", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, onMainMenu);
                return;
            }

            var opp = state.CurrentOpponent;
            string headerText = state.playerEliminated ? "BOUT LOST // REMATCH?" : $"NEXT BOUT // {state.CurrentRoundName}";
            var hdr = MenuUI.Label(stack, headerText, 24, state.playerEliminated ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.OrangeJointCore, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(hdr, 36f);

            string stageStr = "ARENA: " + BrawlTerrain.StageName(state.CurrentStage);
            var stageLbl = MenuUI.Label(stack, stageStr, 20, IronFaunaPalette.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(stageLbl, 30f);

            var rule = MenuUI.Panel(stack, "Rule", IronFaunaPalette.UiSelect);
            MenuUI.Size(rule, 3f);

            if (opp != null)
            {
                string vsHeader = localPlayers > 1
                    ? $"YOUR SQUAD  VS  #{opp.seed} {opp.callsign}"
                    : $"YOU (#8)  VS  #{opp.seed} {opp.callsign}";
                var vsLbl = MenuUI.Label(stack, vsHeader, 30, opp.isBoss ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(vsLbl, 46f);

                var styleLbl = MenuUI.Label(stack, $"{opp.styleTitle}  //  {opp.weightTag} FRAME", 20, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(styleLbl, 32f);

                var notesLbl = MenuUI.Label(stack, opp.roleNote, 19, IronFaunaPalette.UiTextDim, TextAnchor.UpperLeft);
                MenuUI.Size(notesLbl, 88f);

                int threatPct = Mathf.RoundToInt(Mathf.Clamp01(opp.skill) * 100f);
                int hullPct = Mathf.RoundToInt(opp.hpMult * 100f);
                string formatNote = localPlayers > 1 ? $"{localPlayers}v{localPlayers} TAG ELIMINATION" : "1v1 SINGLE ELIMINATION";
                var tape = MenuUI.Label(stack,
                    $"FORMAT:  {formatNote}\nRIVAL AI RATING:  {threatPct}%\nRIVAL HULL SCALE:  {hullPct}%" + (opp.isBoss ? "\nWARNING:  DEFENDING CHAMPION BOSS" : ""),
                    19, IronFaunaPalette.UiText, TextAnchor.UpperLeft, FontStyle.Bold);
                MenuUI.Size(tape, 130f);
            }

            // Rig condition summary (damage carries between bouts)
            var g = state.garage;
            string rig = $"YOUR RIG:  HULL {Mathf.RoundToInt(g.Condition(RigPart.Hull) * 100f)}%";
            int missing = 0, damaged = 0;
            foreach (var part in TournamentGarage.Parts)
            {
                if (part == RigPart.Hull) continue;
                if (g.IsMissing(part)) missing++;
                else if (g.Condition(part) < 0.995f) damaged++;
            }
            if (missing > 0) rig += $"  //  {missing} LIMB(S) MISSING";
            if (damaged > 0) rig += $"  //  {damaged} DAMAGED";
            if (g.IllegalCount() > 0) rig += $"  //  {g.IllegalCount()} ILLEGAL MOD(S)";
            var rigLbl = MenuUI.Label(stack, rig, 19, missing > 0 || g.Condition(RigPart.Hull) < 0.5f ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.CyanEnergy, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Size(rigLbl, 30f);
            MenuUI.Fit(rigLbl, 11);

            var gap = MenuUI.NewRect(stack, "Gap");
            MenuUI.Size(gap, 20f);

            if (state.playerEliminated)
            {
                AddActionButton(stack, "REMATCH BOUT", IronFaunaPalette.CrimsonFront, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, onRematch);
                if (onRepairBay != null) AddActionButton(stack, $"REPAIR BAY  ({g.credits:N0} CR)", IronFaunaPalette.UiSelect, IronFaunaPalette.CyanEnergy, IronFaunaPalette.UiText, onRepairBay);
                AddActionButton(stack, "NEW TOURNAMENT", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, onNewTournament);
                AddActionButton(stack, "MAIN MENU", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, onMainMenu);
            }
            else
            {
                string fightLabel = state.currentRound == 2 ? "ENTER GRAND FINAL" : "ENTER RING (FIGHT)";
                AddActionButton(stack, fightLabel, IronFaunaPalette.CrimsonFront, IronFaunaPalette.OrangeJointCore, IronFaunaPalette.UiText, onFight);
                if (onRepairBay != null) AddActionButton(stack, $"REPAIR BAY  ({g.credits:N0} CR)", IronFaunaPalette.UiSelect, IronFaunaPalette.CyanEnergy, IronFaunaPalette.UiText, onRepairBay);
                AddActionButton(stack, "MAIN MENU", IronFaunaPalette.UiButton, IronFaunaPalette.UiSelect, IronFaunaPalette.UiText, onMainMenu);
            }
        }

        private static void AddActionButton(Transform parent, string label, Color normal, Color hover, Color textCol, UnityAction onClick)
        {
            var b = MenuUI.CreateButton(parent, label, 24, normal, hover, textCol, onClick);
            MenuUI.Size(b, 62f);
        }

        private static void DrawConnector(Transform parent, float x0, float x1, float yTop, float yBot, float yMid)
        {
            Color c = IronFaunaPalette.WithAlpha(IronFaunaPalette.UiSelect, 0.7f);
            float xm = (x0 + x1) * 0.5f;

            var hTop = MenuUI.Panel(parent, "ConnTop", c);
            MenuUI.Place(hTop.rectTransform, new Vector2(x0, yTop - 0.002f), new Vector2(xm, yTop + 0.002f), Vector2.zero, Vector2.zero);

            var hBot = MenuUI.Panel(parent, "ConnBot", c);
            MenuUI.Place(hBot.rectTransform, new Vector2(x0, yBot - 0.002f), new Vector2(xm, yBot + 0.002f), Vector2.zero, Vector2.zero);

            var vert = MenuUI.Panel(parent, "ConnVert", c);
            MenuUI.Place(vert.rectTransform, new Vector2(xm - 0.0015f, yBot), new Vector2(xm + 0.0015f, yTop), Vector2.zero, Vector2.zero);

            var hOut = MenuUI.Panel(parent, "ConnOut", c);
            MenuUI.Place(hOut.rectTransform, new Vector2(xm, yMid - 0.002f), new Vector2(x1, yMid + 0.002f), Vector2.zero, Vector2.zero);
        }

        private static string ShortWeight(string weightTag)
        {
            if (string.IsNullOrEmpty(weightTag)) return "MED";
            if (weightTag == "HEAVY") return "HVY";
            if (weightTag == "MEDIUM") return "MED";
            if (weightTag == "LIGHT") return "LGT";
            return weightTag.Replace("HEAVY", "H").Replace("MEDIUM", "M").Replace("LIGHT", "L");
        }
    }
}
