using System.Collections.Generic;
using UnityEngine;

namespace IronFauna.Brawl
{
    /// <summary>The six repairable parts of the player's tournament rig.</summary>
    public enum RigPart { Hull, Head, ArmL, ArmR, LegL, LegR }

    /// <summary>One purchasable upgrade. Illegal mods are stronger but can be caught at the pre-bout inspection.</summary>
    public sealed class TournamentMod
    {
        public string id;
        public string name;
        public string desc;
        public int cost;
        public bool illegal;
        /// <summary>Chance (0..1) per bout that the officials find this mod.</summary>
        public float detectChance;

        public float hullMult = 1f;
        public float poiseMult = 1f;
        public float limbMult = 1f;
        public float damageMult = 1f;
        public float strikeSpeedMult = 1f;
        public float moveSpeedMult = 1f;
        public float staminaRegenMult = 1f;
        public float knockbackMult = 1f;
        public float limbTearMult = 1f;
    }

    /// <summary>
    /// Tournament-only economy and wear: credits earned by landing hits, per-part condition that carries from bout to
    /// bout (torn-off limbs stay missing until replaced), repairs, legal mods and black-market mods.
    /// Lives on <see cref="TournamentState.garage"/> and is reset with every new bracket.
    /// </summary>
    public sealed class TournamentGarage
    {
        // ---------------------------------------------------------------- economy tuning
        public const float CreditsPerDamage = 3f;      // a 20-damage hook pays 60 CR
        public const int LimbTearBonus = 150;
        /// <summary>Lowest hull a rig can start a bout with, so an unrepaired wreck is fragile but still fights.</summary>
        public const float MinStartHull = 0.2f;

        public static readonly RigPart[] Parts = { RigPart.Hull, RigPart.Head, RigPart.ArmL, RigPart.ArmR, RigPart.LegL, RigPart.LegR };

        public static readonly TournamentMod[] Catalog =
        {
            // ---- legal workshop parts
            new TournamentMod { id = "frame",   name = "REINFORCED FRAME",  desc = "+15% HULL",                           cost = 350, hullMult = 1.15f },
            new TournamentMod { id = "servo",   name = "SERVO TUNE",        desc = "+10% WALK & SPRINT SPEED",            cost = 300, moveSpeedMult = 1.10f },
            new TournamentMod { id = "knuckle", name = "HARDENED KNUCKLES", desc = "+10% STRIKE DAMAGE",                  cost = 400, damageMult = 1.10f },
            new TournamentMod { id = "joints",  name = "JOINT LOCKS",       desc = "+30% LIMB TOUGHNESS",                 cost = 300, limbMult = 1.30f },
            new TournamentMod { id = "coolant", name = "COOLANT LOOP",      desc = "+20% STAMINA REGEN",                  cost = 250, staminaRegenMult = 1.20f },
            // ---- black market (caught = confiscated + fined)
            new TournamentMod { id = "overclock", name = "OVERCLOCKED CORE",    desc = "+25% DAMAGE, +10% STRIKE SPEED",  cost = 600, illegal = true, detectChance = 0.20f, damageMult = 1.25f, strikeSpeedMult = 1.10f },
            new TournamentMod { id = "lead",      name = "LEAD-WEIGHTED FISTS", desc = "+40% KNOCKBACK, +10% DAMAGE",     cost = 500, illegal = true, detectChance = 0.15f, knockbackMult = 1.40f, damageMult = 1.10f },
            new TournamentMod { id = "nitro",     name = "NITRO INJECTORS",     desc = "+25% SPEED, +40% STAMINA REGEN",  cost = 550, illegal = true, detectChance = 0.15f, moveSpeedMult = 1.25f, staminaRegenMult = 1.40f },
            new TournamentMod { id = "spikes",    name = "HIDDEN SHOCK SPIKES", desc = "+60% LIMB TEARING",               cost = 700, illegal = true, detectChance = 0.25f, limbTearMult = 1.60f },
            new TournamentMod { id = "plating",   name = "UNSANCTIONED PLATING", desc = "+35% HULL, +20% POISE",          cost = 650, illegal = true, detectChance = 0.20f, hullMult = 1.35f, poiseMult = 1.20f },
        };

        public int credits;
        private readonly Dictionary<RigPart, float> _condition = new Dictionary<RigPart, float>();
        private readonly HashSet<RigPart> _missing = new HashSet<RigPart>();
        private readonly HashSet<string> _owned = new HashSet<string>();

        public TournamentGarage() { Reset(); }

        public void Reset()
        {
            credits = 0;
            _owned.Clear();
            _missing.Clear();
            foreach (var p in Parts) _condition[p] = 1f;
        }

        // ---------------------------------------------------------------- parts
        public static string PartName(RigPart p)
        {
            switch (p)
            {
                case RigPart.Hull: return "HULL / TORSO";
                case RigPart.Head: return "HEAD";
                case RigPart.ArmL: return "LEFT ARM";
                case RigPart.ArmR: return "RIGHT ARM";
                case RigPart.LegL: return "LEFT LEG";
                default: return "RIGHT LEG";
            }
        }

        public static LimbId ToLimb(RigPart p)
        {
            switch (p)
            {
                case RigPart.Head: return LimbId.Head;
                case RigPart.ArmL: return LimbId.ArmL;
                case RigPart.ArmR: return LimbId.ArmR;
                case RigPart.LegL: return LimbId.LegL;
                case RigPart.LegR: return LimbId.LegR;
                default: return LimbId.None;
            }
        }

        public float Condition(RigPart p) => _missing.Contains(p) ? 0f : (_condition.TryGetValue(p, out float c) ? c : 1f);
        public bool IsMissing(RigPart p) => _missing.Contains(p);

        /// <summary>Full-repair price at 0%; the actual price scales with the damage. A missing limb costs a replacement.</summary>
        private static int FullRepairPrice(RigPart p) => p == RigPart.Hull ? 400 : p == RigPart.Head ? 150 : 120;
        private const int LimbReplacementPrice = 300;

        public int RepairCost(RigPart p)
        {
            if (_missing.Contains(p)) return LimbReplacementPrice;
            float dmg = 1f - Condition(p);
            if (dmg <= 0.005f) return 0;
            return Mathf.Max(5, Mathf.CeilToInt(FullRepairPrice(p) * dmg));
        }

        public int RepairAllCost()
        {
            int sum = 0;
            foreach (var p in Parts) sum += RepairCost(p);
            return sum;
        }

        public bool Repair(RigPart p)
        {
            int cost = RepairCost(p);
            if (cost <= 0 || credits < cost) return false;
            credits -= cost;
            _missing.Remove(p);
            _condition[p] = 1f;
            return true;
        }

        /// <summary>Repairs the cheapest damaged parts first until the money runs out. Returns how many were fixed.</summary>
        public int RepairAllAffordable()
        {
            var order = new List<RigPart>(Parts);
            order.Sort((a, b) => RepairCost(a).CompareTo(RepairCost(b)));
            int fixedCount = 0;
            foreach (var p in order)
            {
                if (RepairCost(p) > 0 && Repair(p)) fixedCount++;
            }
            return fixedCount;
        }

        // ---------------------------------------------------------------- mods
        public bool Owns(TournamentMod m) => _owned.Contains(m.id);

        public bool Buy(TournamentMod m)
        {
            if (m == null || Owns(m) || credits < m.cost) return false;
            credits -= m.cost;
            _owned.Add(m.id);
            return true;
        }

        public IEnumerable<TournamentMod> OwnedMods()
        {
            foreach (var m in Catalog) if (_owned.Contains(m.id)) yield return m;
        }

        public int IllegalCount()
        {
            int n = 0;
            foreach (var m in OwnedMods()) if (m.illegal) n++;
            return n;
        }

        /// <summary>
        /// Pre-bout inspection: each black-market mod is rolled against its detection chance. Caught mods are confiscated
        /// and fined half their price. Returns a feed line, or null when nothing was found.
        /// </summary>
        public string RunInspection()
        {
            var caught = new List<TournamentMod>();
            foreach (var m in OwnedMods())
            {
                if (m.illegal && Random.value < m.detectChance) caught.Add(m);
            }
            if (caught.Count == 0) return null;

            int fine = 0;
            var names = new List<string>();
            foreach (var m in caught)
            {
                _owned.Remove(m.id);
                fine += m.cost / 2;
                names.Add(m.name);
            }
            int paid = Mathf.Min(fine, credits);
            credits -= paid;
            return $"INSPECTION BUST! CONFISCATED: {string.Join(", ", names)}  //  FINED {paid} CR";
        }

        // ---------------------------------------------------------------- applying to / reading from a fighter
        /// <summary>Applies owned mods, then the carried wear, to a freshly spawned player rig.
        /// Call right after <see cref="MechRagdoll.ApplyLoadoutStats"/>.</summary>
        public void ApplyToFighter(MechRagdoll r)
        {
            if (r == null) return;
            float hull = 1f, poise = 1f, limb = 1f, dmg = 1f, strike = 1f, move = 1f, regen = 1f, kb = 1f, tear = 1f;
            foreach (var m in OwnedMods())
            {
                hull *= m.hullMult; poise *= m.poiseMult; limb *= m.limbMult; dmg *= m.damageMult;
                strike *= m.strikeSpeedMult; move *= m.moveSpeedMult; regen *= m.staminaRegenMult;
                kb *= m.knockbackMult; tear *= m.limbTearMult;
            }

            r.maxHealth = Mathf.Round(r.maxHealth * hull);
            r.health = r.maxHealth;
            r.poiseMax = Mathf.Round(r.poiseMax * poise);
            r.knockbackScale *= kb;
            r.limbTearScale *= tear;
            r.ScaleLimbHealth(limb);
            if (r.ghost != null)
            {
                r.ghost.damageScale *= dmg;
                r.ghost.strikeSpeedScale *= strike;
                r.ghost.maxSpeed *= move;
                r.ghost.sprintSpeed *= move;
                r.ghost.staminaRegen *= regen;
            }

            // carried wear
            r.health = r.maxHealth * Mathf.Max(MinStartHull, Condition(RigPart.Hull));
            foreach (var p in Parts)
            {
                if (p == RigPart.Hull) continue;
                var id = ToLimb(p);
                if (_missing.Contains(p) && p != RigPart.Head) r.RemoveLimbQuiet(id);
                else r.SetLimbHealth01(id, Mathf.Max(0.1f, Condition(p)));
            }
        }

        /// <summary>Records the rig's wear at the end of a bout so it carries into the next one.</summary>
        public void CaptureFromFighter(MechRagdoll r)
        {
            if (r == null) return;
            _condition[RigPart.Hull] = r.maxHealth > 0f ? Mathf.Clamp01(r.health / r.maxHealth) : 0f;
            foreach (var p in Parts)
            {
                if (p == RigPart.Hull) continue;
                var id = ToLimb(p);
                if (!r.HasLimb(id))
                {
                    if (p == RigPart.Head) _condition[p] = 0f;   // a lost head is a KO: wrecked, not missing
                    else _missing.Add(p);
                }
                else _condition[p] = r.GetLimbHealth01(id);
            }
        }
    }
}
