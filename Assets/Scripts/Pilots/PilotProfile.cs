using System;
using System.Collections.Generic;
using UnityEngine;
using IronFauna.Brawl;
using IronFauna.Clamshell;

namespace IronFauna.Pilots
{
    /// <summary>
    /// A friend's personal fighter (owner 2026-11-22: "my friends should have their own personal fighter, save it,
    /// invest in it, incentivise play - illegal upgrades and stat increases"). Each pilot owns a robot (garage
    /// loadout), SCRAP earned in every match, XP / level, permanent upgrade tracks and black-market chips.
    /// Saved on this PC in PlayerPrefs; each split-screen player picks their pilot.
    /// </summary>
    [Serializable]
    public class PilotProfile
    {
        public string name = "PILOT";
        public MechLoadout loadout = MechLoadout.Default();
        public int scrap;
        public int xp;
        public int matches, wins, knockouts;
        public int[] upgrades = new int[PilotUpgrades.TrackCount];   // level 0..5 per track
        public List<string> chips = new List<string>();                // installed black-market chips (ids)

        public int Level => PilotUpgrades.LevelForXp(xp);

        public int UpgradeLevel(int track)
        {
            EnsureSize();
            return Mathf.Clamp(upgrades[track], 0, PilotUpgrades.MaxTier);
        }

        public void EnsureSize()
        {
            if (upgrades == null || upgrades.Length != PilotUpgrades.TrackCount)
            {
                var n = new int[PilotUpgrades.TrackCount];
                if (upgrades != null) Array.Copy(upgrades, n, Mathf.Min(upgrades.Length, n.Length));
                upgrades = n;
            }
            if (chips == null) chips = new List<string>();
            if (loadout == null) loadout = MechLoadout.Default();
            loadout.legPose = LegPosePreset.Pose1_HumanLeg;
        }
    }

    [Serializable]
    internal class PilotRoster
    {
        public List<PilotProfile> pilots = new List<PilotProfile>();
        public int[] slotPilot = { 0, 1, 2, 3 };   // which pilot each local player (P1..P4) uses
        public int garagePilot;                     // the pilot the garage edits
    }

    /// <summary>All pilots on this PC.</summary>
    public static class PilotStore
    {
        private const string Key = "IronFauna.Pilots.v1";
        public const int MaxPilots = 8;
        private static PilotRoster _roster;

        private static PilotRoster Roster
        {
            get
            {
                if (_roster == null) Load();
                return _roster;
            }
        }

        public static IReadOnlyList<PilotProfile> Pilots => Roster.pilots;
        public static int Count => Roster.pilots.Count;

        private static void Load()
        {
            _roster = null;
            if (PlayerPrefs.HasKey(Key))
            {
                try { _roster = JsonUtility.FromJson<PilotRoster>(PlayerPrefs.GetString(Key)); }
                catch (ArgumentException) { _roster = null; }
            }
            if (_roster == null || _roster.pilots == null || _roster.pilots.Count == 0)
            {
                // first run: the existing saved garage robot becomes PILOT 1
                _roster = new PilotRoster();
                var first = new PilotProfile { name = "PILOT 1", loadout = MechLoadoutStore.Load() };
                _roster.pilots.Add(first);
            }
            if (_roster.slotPilot == null || _roster.slotPilot.Length != 4) _roster.slotPilot = new[] { 0, 1, 2, 3 };
            foreach (var p in _roster.pilots) p.EnsureSize();
            Save();
        }

        public static void Save()
        {
            if (_roster == null) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(_roster));
            PlayerPrefs.Save();
        }

        public static PilotProfile Get(int index) => Count == 0 ? null : Roster.pilots[Mathf.Clamp(index, 0, Count - 1)];

        public static int GaragePilotIndex
        {
            get => Mathf.Clamp(Roster.garagePilot, 0, Count - 1);
            set { Roster.garagePilot = Mathf.Clamp(value, 0, Count - 1); Save(); }
        }

        public static PilotProfile GaragePilot => Get(GaragePilotIndex);

        /// <summary>The pilot local player <paramref name="slot"/> (0 = P1) fights as. Players without a pilot
        /// of their own (more players than pilots) get null and fight with a preset robot.</summary>
        public static int SlotPilotIndex(int slot)
        {
            slot = Mathf.Clamp(slot, 0, 3);
            int i = Roster.slotPilot[slot];
            return i >= 0 && i < Count ? i : (slot < Count ? slot : -1);
        }

        public static PilotProfile ForSlot(int slot)
        {
            int i = SlotPilotIndex(slot);
            return i >= 0 ? Roster.pilots[i] : null;
        }

        public static void SetSlotPilot(int slot, int pilotIndex)
        {
            Roster.slotPilot[Mathf.Clamp(slot, 0, 3)] = Mathf.Clamp(pilotIndex, 0, Count - 1);
            Save();
        }

        public static PilotProfile Create()
        {
            if (Count >= MaxPilots) return null;
            int n = Count + 1;
            var p = new PilotProfile { name = $"PILOT {n}", loadout = MechLoadout.CreatePreset((n * 3) % MechLoadout.PresetCount, out _, out _) };
            p.EnsureSize();
            Roster.pilots.Add(p);
            Save();
            return p;
        }

        public static void Delete(int index)
        {
            if (Count <= 1 || index < 0 || index >= Count) return;
            Roster.pilots.RemoveAt(index);
            for (int s = 0; s < 4; s++)
            {
                if (Roster.slotPilot[s] == index) Roster.slotPilot[s] = Mathf.Min(s, Count - 1);
                else if (Roster.slotPilot[s] > index) Roster.slotPilot[s]--;
            }
            if (Roster.garagePilot >= Count) Roster.garagePilot = Count - 1;
            Save();
        }
    }

    /// <summary>One black-market chip: a big permanent boost that can burn out after a match (and is caught by
    /// tournament inspections like the Repair Bay's illegal mods).</summary>
    public sealed class PilotChip
    {
        public string id, name, desc;
        public int cost;
        public int minLevel;
        public float burnoutChance;   // chance per match that the chip fries and is lost
        public float hull = 1f, damage = 1f, speed = 1f, regen = 1f, limbTear = 1f, poise = 1f, strikeSpeed = 1f;
    }

    /// <summary>Upgrade tracks, chips, XP curve, rewards and applying it all to a fighter.</summary>
    public static class PilotUpgrades
    {
        public const int TrackCount = 5;
        public const int MaxTier = 5;
        public static readonly string[] TrackNames = { "HULL PLATING", "STRIKE POWER", "SERVO SPEED", "REACTOR (STAMINA)", "JOINT LOCKS" };
        public static readonly string[] TrackPerTier = { "+6% HULL", "+5% DAMAGE", "+4% SPEED", "+8% STAMINA REGEN, +5% MAX", "+8% LIMB TOUGHNESS" };
        private static readonly int[] TierCost = { 200, 450, 800, 1300, 2000 };
        private static readonly int[] TierLevel = { 1, 3, 5, 7, 10 };   // pilot level needed for each tier

        public static readonly PilotChip[] Chips =
        {
            new PilotChip { id = "overclock", name = "OVERCLOCK CHIP", desc = "+15% DAMAGE, +6% STRIKE SPEED", cost = 1500, minLevel = 3, burnoutChance = 0.08f, damage = 1.15f, strikeSpeed = 1.06f },
            new PilotChip { id = "nitro", name = "NITRO CELLS", desc = "+12% SPEED, +25% STAMINA REGEN", cost = 1400, minLevel = 3, burnoutChance = 0.08f, speed = 1.12f, regen = 1.25f },
            new PilotChip { id = "spikes", name = "SPIKED KNUCKLES", desc = "+35% LIMB TEARING", cost = 1800, minLevel = 5, burnoutChance = 0.10f, limbTear = 1.35f },
            new PilotChip { id = "plating", name = "BOOTLEG PLATING", desc = "+18% HULL, +15% POISE", cost = 2200, minLevel = 6, burnoutChance = 0.07f, hull = 1.18f, poise = 1.15f },
            new PilotChip { id = "berserk", name = "BERSERKER CORE", desc = "+25% DAMAGE, -10% HULL", cost = 2600, minLevel = 8, burnoutChance = 0.12f, damage = 1.25f, hull = 0.9f },
        };

        public static PilotChip Chip(string id)
        {
            foreach (var c in Chips) if (c.id == id) return c;
            return null;
        }

        // ---------------------------------------------------------------- XP & levels
        /// <summary>XP needed to reach a level (level 1 = 0 XP): 250, 600, 1050, 1600 ...</summary>
        public static int XpForLevel(int level) => level <= 1 ? 0 : 50 * (level - 1) * (level + 3);
        public static int LevelForXp(int xp)
        {
            int l = 1;
            while (l < 50 && xp >= XpForLevel(l + 1)) l++;
            return l;
        }

        // ---------------------------------------------------------------- shop
        public static int NextTierCost(PilotProfile p, int track)
        {
            int lv = p.UpgradeLevel(track);
            return lv >= MaxTier ? 0 : TierCost[lv];
        }

        public static int NextTierLevelNeeded(PilotProfile p, int track)
        {
            int lv = p.UpgradeLevel(track);
            return lv >= MaxTier ? 0 : TierLevel[lv];
        }

        public static bool CanUpgrade(PilotProfile p, int track) =>
            p.UpgradeLevel(track) < MaxTier && p.scrap >= NextTierCost(p, track) && p.Level >= NextTierLevelNeeded(p, track);

        public static bool Upgrade(PilotProfile p, int track)
        {
            if (!CanUpgrade(p, track)) return false;
            p.scrap -= NextTierCost(p, track);
            p.upgrades[track]++;
            PilotStore.Save();
            return true;
        }

        public static bool BuyChip(PilotProfile p, PilotChip c)
        {
            if (c == null || p.chips.Contains(c.id) || p.scrap < c.cost || p.Level < c.minLevel) return false;
            p.scrap -= c.cost;
            p.chips.Add(c.id);
            PilotStore.Save();
            return true;
        }

        // ---------------------------------------------------------------- in the ring
        /// <summary>Applies a pilot's upgrade tracks and chips to a freshly spawned fighter
        /// (call right after <see cref="MechRagdoll.ApplyLoadoutStats"/>).</summary>
        public static void ApplyToFighter(PilotProfile p, MechRagdoll r)
        {
            if (p == null || r == null) return;
            p.EnsureSize();
            float hull = 1f + 0.06f * p.UpgradeLevel(0);
            float dmg = 1f + 0.05f * p.UpgradeLevel(1);
            float speed = 1f + 0.04f * p.UpgradeLevel(2);
            float regen = 1f + 0.08f * p.UpgradeLevel(3);
            float stamMax = 1f + 0.05f * p.UpgradeLevel(3);
            float limb = 1f + 0.08f * p.UpgradeLevel(4);
            float tear = 1f, poise = 1f, strike = 1f;
            foreach (var id in p.chips)
            {
                var c = Chip(id);
                if (c == null) continue;
                hull *= c.hull; dmg *= c.damage; speed *= c.speed; regen *= c.regen; tear *= c.limbTear; poise *= c.poise; strike *= c.strikeSpeed;
            }
            r.maxHealth = Mathf.Round(r.maxHealth * hull);
            r.health = r.maxHealth;
            r.poiseMax *= poise;
            r.limbTearScale *= tear;
            r.ScaleLimbHealth(limb);
            if (r.ghost != null)
            {
                r.ghost.damageScale *= dmg;
                r.ghost.strikeSpeedScale *= strike;
                r.ghost.maxSpeed *= speed;
                r.ghost.sprintSpeed *= speed;
                r.ghost.staminaRegen *= regen;
                r.ghost.staminaMax *= stamMax;
                r.ghost.stamina = r.ghost.staminaMax;
            }
        }

        /// <summary>
        /// End-of-match pay-out for one pilot: SCRAP and XP for damage dealt, knockouts and a win; then each
        /// black-market chip may burn out. Returns a short summary line ("+340 SCRAP  LEVEL 4!  NITRO CELLS BURNED OUT").
        /// </summary>
        public static string AwardMatch(PilotProfile p, float damageDealt, int kos, bool won)
        {
            if (p == null) return null;
            p.EnsureSize();
            int before = p.Level;
            int pay = Mathf.RoundToInt(damageDealt * 1.2f) + kos * 60 + (won ? 200 : 40);
            p.scrap += pay;
            p.xp += pay;
            p.matches++;
            if (won) p.wins++;
            p.knockouts += kos;
            string line = $"{p.name}: +{pay} SCRAP";
            if (p.Level > before) line += $"  //  LEVEL {p.Level}!";
            for (int i = p.chips.Count - 1; i >= 0; i--)
            {
                var c = Chip(p.chips[i]);
                if (c != null && UnityEngine.Random.value < c.burnoutChance)
                {
                    p.chips.RemoveAt(i);
                    line += $"  //  {c.name} BURNED OUT";
                }
            }
            PilotStore.Save();
            return line;
        }
    }
}
