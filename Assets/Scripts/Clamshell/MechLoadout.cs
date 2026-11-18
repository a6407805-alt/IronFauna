using System;
using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Everything the garage can change about a mech. Serialised to JSON in PlayerPrefs so the
    /// loadout survives between sessions and is handed to the arena on Deploy.
    /// </summary>
    [Serializable]
    public class MechLoadout
    {
        public const int SlotCount = 18; // matches ClamshellSlot

        public LegPosePreset legPose = LegPosePreset.Pose1_HumanLeg;
        public int paintScheme;
        public MechWeightClass torsoWeight = MechWeightClass.Medium;
        public MechWeightClass legWeight = MechWeightClass.Medium;
        public int[] slotSets = new int[SlotCount];

        public ClamshellSetArchetype Get(ClamshellSlot slot)
        {
            EnsureSize();
            return (ClamshellSetArchetype)Mathf.Clamp(slotSets[(int)slot], 0, ClamshellCatalog.SetCount - 1);
        }

        public void Set(ClamshellSlot slot, ClamshellSetArchetype set)
        {
            EnsureSize();
            slotSets[(int)slot] = (int)set;
        }

        public MechLoadout Clone()
        {
            EnsureSize();
            var copy = (MechLoadout)MemberwiseClone();
            copy.slotSets = (int[])slotSets.Clone();
            return copy;
        }

        /// <summary>How many physical copies of a slot a mech carries (two arms, two legs, one body and head).</summary>
        public static int SlotMultiplicity(ClamshellSlot slot)
        {
            bool leg = slot <= ClamshellSlot.FootBack;
            bool arm = slot >= ClamshellSlot.ShoulderFront && slot <= ClamshellSlot.HandBack;
            return leg || arm ? 2 : 1;
        }

        public ClamshellCatalog.PartStats ComputeStats()
        {
            var total = new ClamshellCatalog.PartStats(100, 100, 100, 100, 0);
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = (ClamshellSlot)i;
                int copies = SlotMultiplicity(slot);
                var s = ClamshellCatalog.Stats(Get(slot));
                for (int c = 0; c < copies; c++) total += s;
            }

            // Weight classes: the torso trades protection against cooling and cost, the legs trade speed against load.
            float armor = 1f, speed = 1f, firepower = 1f, cooling = 1f;
            if (torsoWeight == MechWeightClass.Light) { armor *= 0.88f; cooling *= 1.10f; total.cost -= 60; }
            else if (torsoWeight == MechWeightClass.Heavy) { armor *= 1.15f; firepower *= 1.10f; total.cost += 120; }
            if (legWeight == MechWeightClass.Light) { speed *= 1.15f; armor *= 0.95f; total.cost -= 40; }
            else if (legWeight == MechWeightClass.Heavy) { speed *= 0.85f; armor *= 1.10f; total.cost += 80; }
            total.armor = Mathf.RoundToInt(total.armor * armor);
            total.speed = Mathf.RoundToInt(total.speed * speed);
            total.firepower = Mathf.RoundToInt(total.firepower * firepower);
            total.cooling = Mathf.RoundToInt(total.cooling * cooling);
            return total;
        }

        public static MechLoadout Default()
        {
            var l = new MechLoadout();
            for (int i = 0; i < SlotCount; i++) l.slotSets[i] = (int)ClamshellSetArchetype.Set01_Ironclad;
            return l;
        }

        public struct CombatModifiers
        {
            public float healthMult;
            public float limbHealthMult;
            public float poiseMult;
            public float massMult;
            public float moveSpeedMult;
            public float sprintSpeedMult;
            public float strikeSpeedMult;
            public float damageMult;
            public float knockbackDealtMult;
            public float knockbackTakenMult;
            public float limbTearMult;
            public bool hyperArmor;
            public string weightLabel;
        }

        /// <summary>
        /// Translates torsoWeight, legWeight and the 18 clamshell armor slots into real combat advantages:
        /// Light = fast movement, rapid combos, agile floor-scoot and high jumps;
        /// Medium = balanced MMA all-rounder and submission grappler;
        /// Heavy = huge hull/limb HP, hyper-armor during wind-ups, heavy slams and wall-smashing mass.
        /// </summary>
        public CombatModifiers ComputeCombatModifiers()
        {
            var stats = ComputeStats();
            float armorNorm = Mathf.Clamp((stats.armor - 190f) / 140f, -0.35f, 0.55f);
            float speedNorm = Mathf.Clamp((stats.speed - 110f) / 110f, -0.30f, 0.45f);
            float powerNorm = Mathf.Clamp((stats.firepower - 120f) / 110f, -0.25f, 0.45f);
            float staminaNorm = Mathf.Clamp((stats.cooling - 110f) / 110f, -0.25f, 0.45f);

            var m = new CombatModifiers
            {
                healthMult = 1f + armorNorm * 0.45f,
                limbHealthMult = 1f + armorNorm * 0.4f,
                poiseMult = 1f + armorNorm * 0.35f + staminaNorm * 0.25f,
                massMult = 1f,
                moveSpeedMult = 1f + speedNorm * 0.3f,
                sprintSpeedMult = 1f + speedNorm * 0.32f,
                strikeSpeedMult = 1f + staminaNorm * 0.22f,
                damageMult = 1f + powerNorm * 0.35f,
                knockbackDealtMult = 1f + powerNorm * 0.3f,
                knockbackTakenMult = 1f,
                limbTearMult = 1f + powerNorm * 0.35f,
                hyperArmor = torsoWeight == MechWeightClass.Heavy,
                weightLabel = torsoWeight == legWeight ? torsoWeight.ToString().ToUpperInvariant() : $"{torsoWeight.ToString().ToUpperInvariant()}/{legWeight.ToString().ToUpperInvariant()}",
            };

            if (torsoWeight == MechWeightClass.Light)
            {
                m.healthMult *= 0.82f;
                m.limbHealthMult *= 0.85f;
                m.poiseMult *= 0.80f;
                m.massMult *= 0.82f;
                m.strikeSpeedMult *= 1.22f;
                m.moveSpeedMult *= 1.12f;
                m.knockbackTakenMult *= 1.22f;
            }
            else if (torsoWeight == MechWeightClass.Heavy)
            {
                m.healthMult *= 1.38f;
                m.limbHealthMult *= 1.35f;
                m.poiseMult *= 1.42f;
                m.massMult *= 1.28f;
                m.strikeSpeedMult *= 0.86f;
                m.damageMult *= 1.24f;
                m.knockbackDealtMult *= 1.32f;
                m.knockbackTakenMult *= 0.72f;
                m.limbTearMult *= 1.25f;
            }

            if (legWeight == MechWeightClass.Light)
            {
                m.moveSpeedMult *= 1.22f;
                m.sprintSpeedMult *= 1.25f;
                m.massMult *= 0.90f;
                m.knockbackTakenMult *= 1.12f;
            }
            else if (legWeight == MechWeightClass.Heavy)
            {
                m.moveSpeedMult *= 0.82f;
                m.sprintSpeedMult *= 0.84f;
                m.healthMult *= 1.12f;
                m.poiseMult *= 1.20f;
                m.massMult *= 1.18f;
                m.knockbackTakenMult *= 0.78f;
            }
            return m;
        }

        public const int PresetCount = 10;

        public static MechLoadout CreatePreset(int index, out string title, out string roleNote)
        {
            int idx = ((index % PresetCount) + PresetCount) % PresetCount;
            var l = new MechLoadout { legPose = LegPosePreset.Pose1_HumanLeg, paintScheme = idx % IronFauna.Core.IronFaunaPalette.PaintSchemes.Length };
            ClamshellSetArchetype set = (ClamshellSetArchetype)idx;
            for (int i = 0; i < SlotCount; i++) l.slotSets[i] = (int)set;

            switch (idx)
            {
                case 0:
                    l.torsoWeight = MechWeightClass.Medium; l.legWeight = MechWeightClass.Medium;
                    title = "IRONCLAD BRAWLER"; roleNote = "MEDIUM ALL-ROUNDER · Balanced hull, speed & grappling.";
                    break;
                case 1:
                    l.torsoWeight = MechWeightClass.Light; l.legWeight = MechWeightClass.Light;
                    title = "MANTIS NAK MUAY"; roleNote = "LIGHT STRIKER · +34% speed, lightning kicks & flying knees.";
                    break;
                case 2:
                    l.torsoWeight = MechWeightClass.Heavy; l.legWeight = MechWeightClass.Heavy;
                    title = "GOLIATH JUGGERNAUT"; roleNote = "HEAVY TITAN · Hyper-armor wind-ups, massive slams & hull.";
                    break;
                case 3:
                    l.torsoWeight = MechWeightClass.Medium; l.legWeight = MechWeightClass.Light;
                    title = "RIPJAW JIU-JITSU"; roleNote = "AGILE GRAPPLER · Fast floor butt-scoot, takedowns & limb tearing.";
                    break;
                case 4:
                    l.torsoWeight = MechWeightClass.Light; l.legWeight = MechWeightClass.Light;
                    title = "AFTERBURN BLITZ"; roleNote = "FEATHERWEIGHT · Fastest footwork, tornado kicks & slips.";
                    break;
                case 5:
                    l.torsoWeight = MechWeightClass.Heavy; l.legWeight = MechWeightClass.Heavy;
                    title = "BASTION COLOSSUS"; roleNote = "SUPER-HEAVY TANK · Maximum armor & knockback resistance.";
                    break;
                case 6:
                    l.torsoWeight = MechWeightClass.Light; l.legWeight = MechWeightClass.Medium;
                    title = "OVERCLOCK BOXER"; roleNote = "SPEED BOXER · +28% faster punch wind-up & combo recovery.";
                    break;
                case 7:
                    l.torsoWeight = MechWeightClass.Medium; l.legWeight = MechWeightClass.Medium;
                    title = "VOLTAIC CLINCHER"; roleNote = "CLINCH FIGHTER · Heavy elbows, knees & suplex throws.";
                    break;
                case 8:
                    l.torsoWeight = MechWeightClass.Heavy; l.legWeight = MechWeightClass.Medium;
                    title = "SALVO CRUSHER"; roleNote = "POWER PUNCHER · Heavy torso hyper-armor with mobile legs.";
                    break;
                default:
                    l.torsoWeight = MechWeightClass.Medium; l.legWeight = MechWeightClass.Heavy;
                    title = "AEGIS GUARDIAN"; roleNote = "ROOTED COUNTER · Heavy planted legs, high poise & sweeps.";
                    break;
            }
            return l;
        }

        private void EnsureSize()
        {
            if (slotSets == null || slotSets.Length != SlotCount)
            {
                var resized = new int[SlotCount];
                if (slotSets != null) Array.Copy(slotSets, resized, Mathf.Min(slotSets.Length, SlotCount));
                slotSets = resized;
            }
        }
    }

    /// <summary>PlayerPrefs persistence for the player's mech loadout.</summary>
    public static class MechLoadoutStore
    {
        private const string Key = "IronFauna.Loadout.v1";

        public static bool HasSaved => PlayerPrefs.HasKey(Key);

        public static MechLoadout Load()
        {
            if (!PlayerPrefs.HasKey(Key)) return MechLoadout.Default();
            try
            {
                string json = PlayerPrefs.GetString(Key);
                var loadout = JsonUtility.FromJson<MechLoadout>(json);
                if (loadout == null) return MechLoadout.Default();
                if (!json.Contains("\"torsoWeight\""))
                {
                    // Saved before weight classes existed: that frame is today's Medium.
                    loadout.torsoWeight = MechWeightClass.Medium;
                    loadout.legWeight = MechWeightClass.Medium;
                }
                return loadout;
            }
            catch (ArgumentException)
            {
                return MechLoadout.Default();
            }
        }

        public static void Save(MechLoadout loadout)
        {
            if (loadout == null) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(loadout));
            PlayerPrefs.Save();
        }
    }
}
