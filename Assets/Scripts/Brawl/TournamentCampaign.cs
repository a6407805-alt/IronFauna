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
    /// 10-round Tournament Campaign gauntlet (owner 2026-11-22): rounds 1-4 rising rivals, round 5 MINI-BOSS,
    /// rounds 6-9 a tougher tier, round 10 the GRAND MASTER (a very hard final boss). Damage carries between rounds
    /// (Repair Bay). Rendered as a 10-round ladder overlay with MenuUI + IronFaunaPalette.
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
        /// <summary>0 = regular rival, 1 = round-5 mini-boss, 2 = round-10 Grand Master.</summary>
        public int bossTier;
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
        public int roundIndex; // 0..9
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
        public const int TotalRounds = 10;
        public const int MiniBossRound = 4;      // round 5 (0-based index)
        public const int GrandMasterRound = 9;   // round 10

        /// <summary>Arena for each round (City and Forest stay off the tournament, like the title menu).</summary>
        public static readonly BrawlStageId[] RoundStages =
        {
            BrawlStageId.BasaltMesa,
            BrawlStageId.ScrapyardColosseum,
            BrawlStageId.GlacierTrench,
            BrawlStageId.FoundryCrucible,
            BrawlStageId.ScrapyardColosseum,   // mini-boss
            BrawlStageId.BasaltMesa,
            BrawlStageId.GlacierTrench,
            BrawlStageId.FoundryCrucible,
            BrawlStageId.ScrapyardColosseum,
            BrawlStageId.WrestlingRing,        // Grand Master: the title fight in the ring
        };

        public readonly TournamentMatch[] rounds = new TournamentMatch[TotalRounds];

        public TournamentEntrant playerEntrant;
        /// <summary>Credits, carried part wear and mods for the player's rig (Repair Bay).</summary>
        public readonly TournamentGarage garage = new TournamentGarage();
        public int currentRound; // 0..9
        public bool tournamentWon;
        public bool playerEliminated;
        public string lastRoundFeed = IntroFeed;

        private const string IntroFeed = "10 ROUNDS. A MINI-BOSS AT ROUND 5. THE GRAND MASTER WAITS AT ROUND 10. DAMAGE CARRIES OVER - REPAIR BETWEEN BOUTS.";

        public static string RoundName(int round)
        {
            if (round == MiniBossRound) return "ROUND 5 - MINI-BOSS";
            if (round == GrandMasterRound) return "ROUND 10 - GRAND MASTER";
            return $"ROUND {round + 1}";
        }

        public BrawlStageId CurrentStage => RoundStages[Mathf.Clamp(currentRound, 0, RoundStages.Length - 1)];
        public string CurrentRoundName => RoundName(Mathf.Clamp(currentRound, 0, TotalRounds - 1));
        public bool IsFinalRound => currentRound >= GrandMasterRound;

        public TournamentMatch ActivePlayerMatch => rounds[Mathf.Clamp(currentRound, 0, TotalRounds - 1)];
        public TournamentEntrant CurrentOpponent => ActivePlayerMatch != null ? ActivePlayerMatch.OpponentOfPlayer : null;

        public void GenerateNewBracket(MechLoadout playerLoadout)
        {
            currentRound = 0;
            tournamentWon = false;
            playerEliminated = false;
            garage.Reset();
            lastRoundFeed = IntroFeed;

            string playerWeight = playerLoadout != null
                ? playerLoadout.ComputeCombatModifiers().weightLabel
                : "MEDIUM";

            playerEntrant = new TournamentEntrant
            {
                seed = 0,
                callsign = "YOU",
                styleTitle = "CHALLENGER",
                roleNote = "CUSTOM GARAGE RIG.",
                weightTag = playerWeight,
                isPlayer = true,
                presetIndex = 0,
                paintScheme = playerLoadout != null ? playerLoadout.paintScheme : 0,
                skill = 0.7f,
                hpMult = 1.0f,
            };

            // Act I rivals (rounds 1-4) and Act II rivals (rounds 6-9), shuffled within each act,
            // then given a rising difficulty curve by slot.
            var actOne = new List<TournamentEntrant>
            {
                MakeRival("MANTIS-07", "NAK MUAY STRIKER", 1, 4),
                MakeRival("AFTERBURN", "BLITZ FEATHERWEIGHT", 4, 1),
                MakeRival("RIPJAW", "MAT SUBMISSIONIST", 3, 5),
                MakeRival("OVERCLOCK", "SPEED BOXER", 6, 2),
            };
            var actTwo = new List<TournamentEntrant>
            {
                MakeRival("VOLTAIC", "CLINCH SUPLEXER", 7, 6),
                MakeRival("AEGIS-IX", "ROOTED COUNTER", 9, 3),
                MakeRival("SALVO", "IRON CRUSHER", 8, 6),
                MakeRival("BASTION", "COLOSSUS TANK", 5, 3),
            };
            Shuffle(actOne);
            Shuffle(actTwo);

            float[] actOneSkill = { 0.45f, 0.50f, 0.55f, 0.60f };
            float[] actOneHull  = { 0.90f, 0.95f, 1.00f, 1.05f };
            float[] actTwoSkill = { 0.64f, 0.68f, 0.72f, 0.76f };
            float[] actTwoHull  = { 1.08f, 1.14f, 1.20f, 1.26f };

            var ladder = new TournamentEntrant[TotalRounds];
            for (int i = 0; i < 4; i++)
            {
                actOne[i].skill = actOneSkill[i]; actOne[i].hpMult = actOneHull[i];
                actTwo[i].skill = actTwoSkill[i]; actTwo[i].hpMult = actTwoHull[i];
                ladder[i] = actOne[i];
                ladder[5 + i] = actTwo[i];
            }

            var miniBoss = MakeRival("WARDEN KRAG", "MINI-BOSS // HEAVYWEIGHT ENFORCER", 5, 0);
            miniBoss.skill = 0.78f; miniBoss.hpMult = 1.5f; miniBoss.isBoss = true; miniBoss.bossTier = 1;
            miniBoss.roleNote = "OVERSIZED SUPER-HEAVY. +50% HULL, HARDER HITS, TOUGHER LIMBS. WEAR HIM DOWN - DON'T TRADE BLOWS.";
            ladder[MiniBossRound] = miniBoss;

            var grandMaster = MakeRival("GRANDMASTER APEX", "THE GRAND MASTER // UNDEFEATED", 2, 0);
            grandMaster.skill = 1f; grandMaster.hpMult = 2.0f; grandMaster.isBoss = true; grandMaster.bossTier = 2;
            grandMaster.roleNote = "MAXIMUM AI: READS AND PARRIES EVERYTHING. DOUBLE HULL, BRUTAL FAST STRIKES, ENDLESS STAMINA, IRON LIMBS. COME FULLY REPAIRED.";
            ladder[GrandMasterRound] = grandMaster;

            for (int i = 0; i < TotalRounds; i++)
            {
                ladder[i].seed = i + 1;
                rounds[i] = new TournamentMatch { roundIndex = i, matchIndex = 0, fighterA = playerEntrant, fighterB = ladder[i] };
            }
        }

        public void RecordPlayerResult(bool playerWon)
        {
            var match = ActivePlayerMatch;
            if (match == null) return;
            var opp = match.OpponentOfPlayer;

            if (!playerWon)
            {
                playerEliminated = true;
                lastRoundFeed = opp != null
                    ? $"KNOCKED OUT BY {opp.callsign} IN {CurrentRoundName}. REPAIR, THEN REMATCH - OR START A NEW TOURNAMENT."
                    : $"KNOCKED OUT IN {CurrentRoundName}.";
                return;
            }

            match.completed = true;
            match.winner = playerEntrant;
            playerEntrant.wins++;
            if (opp != null) opp.eliminated = true;

            if (currentRound >= GrandMasterRound)
            {
                tournamentWon = true;
                lastRoundFeed = "THE GRAND MASTER HAS FALLEN! YOU ARE THE IRONFAUNA CHAMPION - 10 ROUNDS, 10 WINS.";
                return;
            }

            string cleared = currentRound == MiniBossRound
                ? "MINI-BOSS WARDEN KRAG DESTROYED! ACT II BEGINS."
                : $"{CurrentRoundName} CLEARED.";
            currentRound++;
            var next = CurrentOpponent;
            lastRoundFeed = currentRound == GrandMasterRound
                ? $"{cleared}  ONE FIGHT LEFT: THE GRAND MASTER. REPAIR EVERYTHING FIRST."
                : $"{cleared}  NEXT: {CurrentRoundName} VS {(next != null ? next.callsign : "TBD")} AT {BrawlTerrain.StageName(CurrentStage)}.";
        }

        public void PrepareRematch()
        {
            playerEliminated = false;
            lastRoundFeed = $"REMATCH - {CurrentRoundName} AT {BrawlTerrain.StageName(CurrentStage)}.";
        }

        private static TournamentEntrant MakeRival(string callsign, string styleTitle, int presetIndex, int paintScheme)
        {
            var l = MechLoadout.CreatePreset(presetIndex, out _, out string roleNote);
            return new TournamentEntrant
            {
                callsign = callsign,
                styleTitle = styleTitle,
                roleNote = roleNote,
                weightTag = l.ComputeCombatModifiers().weightLabel,
                presetIndex = presetIndex,
                paintScheme = paintScheme % IronFaunaPalette.PaintSchemes.Length,
                skill = 0.5f,
                hpMult = 1f,
            };
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
                ? "IRONFAUNA TOURNAMENT  //  GRAND MASTER DEFEATED"
                : state.playerEliminated
                ? $"IRONFAUNA TOURNAMENT  //  KNOCKED OUT ({state.CurrentRoundName})"
                : $"IRONFAUNA TOURNAMENT  //  {state.CurrentRoundName}";
            var title = MenuUI.Label(header.transform, titleText, 44, state.tournamentWon ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.75f, 1f), new Vector2(44f, 6f), Vector2.zero);

            string recStr = (localPlayers > 1
                ? $"{localPlayers}P TAG  //  WINS: {state.playerEntrant.wins}/{TournamentState.TotalRounds}"
                : $"SOLO  //  WINS: {state.playerEntrant.wins}/{TournamentState.TotalRounds}")
                + $"  //  {state.garage.credits:N0} CR";
            var rec = MenuUI.Label(header.transform, recStr, 24, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(rec.rectTransform, new Vector2(0.68f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-44f, 0f));

            // Left / Center: the 10-round ladder (Act I rounds 1-5 left, Act II rounds 6-10 right)
            var bracketPanel = MenuUI.Panel(_contentRoot, "LadderArea", IronFaunaPalette.WithAlpha(IronFaunaPalette.UiPanel, 0.88f));
            MenuUI.Place(bracketPanel.rectTransform, new Vector2(0.02f, 0.11f), new Vector2(0.67f, 0.89f), Vector2.zero, Vector2.zero);
            var bracketTop = MenuUI.Panel(bracketPanel.transform, "LadderTopRule", IronFaunaPalette.UiSelect);
            MenuUI.Place(bracketTop.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), Vector2.zero);

            BuildColumnTitle(bracketPanel.transform, "ACT I  //  ROUNDS 1-5", 0.02f, 0.49f, state.currentRound < TournamentState.TotalRounds / 2 && !state.tournamentWon);
            BuildColumnTitle(bracketPanel.transform, "ACT II  //  ROUNDS 6-10", 0.51f, 0.98f, state.currentRound >= TournamentState.TotalRounds / 2 || state.tournamentWon);
            for (int i = 0; i < TournamentState.TotalRounds; i++)
            {
                int col = i / 5, row = i % 5;
                float x0 = col == 0 ? 0.02f : 0.51f;
                float y1 = 0.89f - row * 0.176f;
                BuildRoundCard(bracketPanel.transform, state, i, x0, x0 + 0.47f, y1 - 0.155f, y1);
            }

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
            MenuUI.Fit(feed, 11);

            var hint = MenuUI.Label(footer.transform, "ENTER / SPACE: START BOUT   //   ESC: MAIN MENU   //   LAND HITS = CREDITS", 18, IronFaunaPalette.UiTextDim, TextAnchor.MiddleRight, FontStyle.Bold);
            MenuUI.Place(hint.rectTransform, new Vector2(0.75f, 0f), new Vector2(0.985f, 1f), Vector2.zero, Vector2.zero);
        }

        private static void BuildColumnTitle(Transform parent, string text, float x0, float x1, bool active)
        {
            var lbl = MenuUI.Label(parent, text, 22, active ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(lbl.rectTransform, new Vector2(x0, 0.91f), new Vector2(x1, 0.98f), Vector2.zero, Vector2.zero);
        }

        /// <summary>One ladder row: round number, opponent, arena and status (CLEARED / NEXT / LOST / upcoming).</summary>
        private static void BuildRoundCard(Transform parent, TournamentState state, int round, float x0, float x1, float y0, float y1)
        {
            var m = state.rounds[round];
            var opp = m != null ? m.OpponentOfPlayer : null;
            bool cleared = m != null && m.completed;
            bool current = !state.tournamentWon && round == state.currentRound;
            bool lost = current && state.playerEliminated;
            int tier = opp != null ? opp.bossTier : 0;

            Color accent = tier == 2 ? IronFaunaPalette.HazardYellow : tier == 1 ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.UiButton;
            Color bgCol = current
                ? IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, lost ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.OrangeJointCore, 0.18f)
                : tier > 0 ? IronFaunaPalette.Lerp(IronFaunaPalette.DarkIron, IronFaunaPalette.MaroonRear, 0.35f)
                : IronFaunaPalette.WithAlpha(IronFaunaPalette.DarkIron, 0.90f);

            var card = MenuUI.Panel(parent, "Round_" + (round + 1), bgCol);
            MenuUI.Place(card.rectTransform, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
            var stripe = MenuUI.Panel(card.transform, "Edge", cleared ? IronFaunaPalette.CyanEnergy : current ? IronFaunaPalette.HazardYellow : accent);
            MenuUI.Place(stripe.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(7f, 0f));

            var num = MenuUI.Label(card.transform, (round + 1).ToString(), 40, tier > 0 ? accent : IronFaunaPalette.UiTextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUI.Place(num.rectTransform, new Vector2(0.01f, 0f), new Vector2(0.15f, 1f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(num, 14);

            string name = opp != null ? opp.callsign : "TBD";
            Color nameCol = cleared ? IronFaunaPalette.UiTextDim : tier > 0 ? accent : IronFaunaPalette.UiText;
            var nameLbl = MenuUI.Label(card.transform, name, 24, nameCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(nameLbl.rectTransform, new Vector2(0.16f, 0.48f), new Vector2(0.74f, 0.96f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(nameLbl, 12);

            string sub = tier == 2 ? "FINAL BOSS" : tier == 1 ? "MINI-BOSS" : opp != null ? $"{opp.styleTitle}  ·  {ShortWeight(opp.weightTag)}" : "";
            sub += "  ·  " + BrawlTerrain.StageName(TournamentState.RoundStages[round]);
            var subLbl = MenuUI.Label(card.transform, sub, 15, IronFaunaPalette.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUI.Place(subLbl.rectTransform, new Vector2(0.16f, 0.06f), new Vector2(0.98f, 0.48f), Vector2.zero, Vector2.zero);
            MenuUI.Fit(subLbl, 9);

            string status = cleared ? "CLEARED" : lost ? "LOST" : current ? "NEXT" : "";
            if (status.Length > 0)
            {
                Color sCol = cleared ? IronFaunaPalette.CyanEnergy : lost ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.HazardYellow;
                var badge = MenuUI.Panel(card.transform, "Status", sCol);
                MenuUI.Place(badge.rectTransform, new Vector2(0.76f, 0.56f), new Vector2(0.98f, 0.92f), Vector2.zero, Vector2.zero);
                var bTxt = MenuUI.Label(badge.transform, status, 15, IronFaunaPalette.DarkIron, TextAnchor.MiddleCenter, FontStyle.Bold);
                MenuUI.Fill(bTxt.rectTransform);
                MenuUI.Fit(bTxt, 9);
            }
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
                var crown = MenuUI.Label(stack, "CHAMPION!", 38, IronFaunaPalette.HazardYellow, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(crown, 52f);

                var desc = MenuUI.Label(stack,
                    "10 ROUNDS, 10 WINS. YOU BROKE THE MINI-BOSS AND TORE DOWN THE GRAND MASTER.\n\nSTART A NEW TOURNAMENT TO RUN THE GAUNTLET AGAIN AGAINST A FRESH ORDER OF RIVALS.",
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
                    ? $"YOUR SQUAD  VS  {opp.callsign}"
                    : $"YOU  VS  {opp.callsign}";
                var vsLbl = MenuUI.Label(stack, vsHeader, 30, opp.isBoss ? IronFaunaPalette.HazardYellow : IronFaunaPalette.UiText, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(vsLbl, 46f);

                var styleLbl = MenuUI.Label(stack, $"{opp.styleTitle}  //  {opp.weightTag} FRAME", 20, IronFaunaPalette.CyanEnergy, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUI.Size(styleLbl, 32f);

                var notesLbl = MenuUI.Label(stack, opp.roleNote, 19, IronFaunaPalette.UiTextDim, TextAnchor.UpperLeft);
                MenuUI.Size(notesLbl, 88f);

                int threatPct = Mathf.RoundToInt(Mathf.Clamp01(opp.skill) * 100f);
                int hullPct = Mathf.RoundToInt(opp.hpMult * 100f);
                string formatNote = localPlayers > 1 ? $"{localPlayers}v{localPlayers} TAG" : "1v1";
                var tape = MenuUI.Label(stack,
                    $"FORMAT:  {formatNote}\nRIVAL AI RATING:  {threatPct}%\nRIVAL HULL SCALE:  {hullPct}%" + (opp.bossTier == 2 ? "\nWARNING:  GRAND MASTER - FINAL BOSS" : opp.bossTier == 1 ? "\nWARNING:  MINI-BOSS" : ""),
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
                string fightLabel = state.IsFinalRound ? "FACE THE GRAND MASTER" : state.currentRound == TournamentState.MiniBossRound ? "FIGHT THE MINI-BOSS" : "ENTER RING (FIGHT)";
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
