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
