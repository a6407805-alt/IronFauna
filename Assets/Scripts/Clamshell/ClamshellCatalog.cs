using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Display names, perks and stat deltas for the 10 clamshell set archetypes, mirroring the
    /// catalogs in Docs/. Every half (front or back, any slot) of a given set contributes the same
    /// stat delta, so mixing sets is a pure build trade-off (no weight classes).
    /// </summary>
    public static class ClamshellCatalog
    {
        public const int SetCount = 10;

        public struct PartStats
        {
            public int armor;
            public int speed;
            public int firepower;
            public int cooling;
            public int cost;

            public PartStats(int armor, int speed, int firepower, int cooling, int cost)
            {
                this.armor = armor;
                this.speed = speed;
                this.firepower = firepower;
                this.cooling = cooling;
                this.cost = cost;
            }

            public static PartStats operator +(PartStats a, PartStats b)
            {
                return new PartStats(a.armor + b.armor, a.speed + b.speed, a.firepower + b.firepower, a.cooling + b.cooling, a.cost + b.cost);
            }
        }

        private static readonly string[] HumanoidSetNames =
        {
            "Ironclad", "Bastion", "Overclock", "Ripjaw", "Afterburn",
            "Voltaic", "Salvo", "Goliath", "Mantis", "Aegis",
        };

        private static readonly string[] AnimalSetNames =
        {
            "Leo-Frame", "Ceratops", "Gepard", "Lupus", "Aquila",
            "Volt-Viper", "Ursus", "Tusk", "Stalker", "Testudo",
        };

        // Humanoid head names from Docs/ironfauna_head_clamshell_catalog.md.
        private static readonly string[] HumanoidHeadNames =
        {
            "Noble-V", "Grenadier-UA", "Vanguard-Fin", "Shade-Eye", "Recon-Scope",
            "Gungnir-Arc", "Commando-Vulcan", "Juggernaut-EOD", "Kasuar-Locus", "Emile-Aegis",
        };

        private static readonly string[] Perks =
        {
            "Balanced durability at the lowest credit cost.",
            "Ballistic damage reduction and extra reserve ammo.",
            "Faster energy weapon and booster cooldowns.",
            "Harder melee kicks, charges and rams.",
            "Faster dodges and air-dash acceleration.",
            "Short-range EMP pulse on melee and dash.",
            "Micro-missile swarms and countermeasures.",
            "Knockback immunity and bonus structure-bash damage.",
            "Shrinks enemy turret and drone detection range.",
            "Projects a hard-light barrier.",
        };

        // armor, speed, firepower, cooling, credit cost (per half)
        private static readonly PartStats[] HalfStats =
        {
            new PartStats(3, 0, 0, 0, 30),   // Ironclad
            new PartStats(6, -2, 1, -1, 45), // Bastion
            new PartStats(1, 1, 1, 4, 55),   // Overclock
            new PartStats(2, 1, 3, -1, 50),  // Ripjaw
            new PartStats(1, 4, 0, 1, 52),   // Afterburn
            new PartStats(2, 0, 2, 1, 60),   // Voltaic
            new PartStats(2, -1, 4, -1, 65), // Salvo
            new PartStats(7, -3, 2, -2, 70), // Goliath
            new PartStats(1, 3, 1, 1, 62),   // Mantis
            new PartStats(4, -1, 0, 2, 75),  // Aegis
        };

        public static string SetName(ClamshellSetArchetype set, bool animal)
        {
            int i = Index(set);
            return animal ? AnimalSetNames[i] : HumanoidSetNames[i];
        }

        /// <summary>Name shown for a specific slot (heads and bodies have their own names per chassis).</summary>
        public static string PartName(ClamshellSlot slot, ClamshellSetArchetype set, bool quadruped)
        {
            int i = Index(set);
            bool isHead = slot == ClamshellSlot.HeadFront || slot == ClamshellSlot.HeadBack;
            bool isBody = slot == ClamshellSlot.BodyFront || slot == ClamshellSlot.BodyBack;
            if (quadruped && (isHead || isBody)) return AnimalSetNames[i];
            if (isHead) return HumanoidHeadNames[i];
            return HumanoidSetNames[i];
        }

        public static string Perk(ClamshellSetArchetype set)
        {
            return Perks[Index(set)];
        }

        public static PartStats Stats(ClamshellSetArchetype set)
        {
            return HalfStats[Index(set)];
        }

        public static ClamshellSetArchetype Cycle(ClamshellSetArchetype set, int delta)
        {
            int i = ((Index(set) + delta) % SetCount + SetCount) % SetCount;
            return (ClamshellSetArchetype)i;
        }

        public static ClamshellSetArchetype Random()
        {
            return (ClamshellSetArchetype)UnityEngine.Random.Range(0, SetCount);
        }

        private static int Index(ClamshellSetArchetype set)
        {
            return Mathf.Clamp((int)set, 0, SetCount - 1);
        }
    }
}
