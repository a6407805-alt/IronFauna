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

        public bool quadruped;
        public LegPosePreset legPose = LegPosePreset.Pose1_HumanLeg;
        public int paintScheme;
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

        /// <summary>True if this slot exists on the chassis (quadrupeds have no arms).</summary>
        public static bool SlotUsed(ClamshellSlot slot, bool quadruped)
        {
            if (!quadruped) return true;
            return slot < ClamshellSlot.ShoulderFront || slot > ClamshellSlot.HandBack;
        }

        /// <summary>How many physical copies of a slot a chassis carries (two arms, two or four legs).</summary>
        public static int SlotMultiplicity(ClamshellSlot slot, bool quadruped)
        {
            if (!SlotUsed(slot, quadruped)) return 0;
            bool leg = slot <= ClamshellSlot.FootBack;
            bool arm = slot >= ClamshellSlot.ShoulderFront && slot <= ClamshellSlot.HandBack;
            if (leg) return quadruped ? 4 : 2;
            if (arm) return 2;
            return 1;
        }

        public ClamshellCatalog.PartStats ComputeStats()
        {
            var total = new ClamshellCatalog.PartStats(100, quadruped ? 115 : 100, 100, 100, 0);
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = (ClamshellSlot)i;
                int copies = SlotMultiplicity(slot, quadruped);
                var s = ClamshellCatalog.Stats(Get(slot));
                for (int c = 0; c < copies; c++) total += s;
            }
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
                var loadout = JsonUtility.FromJson<MechLoadout>(PlayerPrefs.GetString(Key));
                return loadout ?? MechLoadout.Default();
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
