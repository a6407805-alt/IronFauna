using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>Weight class of a torso or a set of legs.</summary>
    public enum MechWeightClass
    {
        Light,   // sleek, high-mobility frame
        Medium,  // balanced military standard
        Heavy,   // broad, reinforced bulkheads
    }

    /// <summary>Weapon size a hardpoint socket accepts.</summary>
    public enum HardpointSize
    {
        Small,   // SMGs, PDWs, micro-missile pods
        Medium,  // light cannons, autocannons, heavy energy rifles
        Large,   // siege cannons, railguns, heavy rocket pods
    }

    /// <summary>
    /// Proportions of one weight class: how thick the skeleton is, how wide and deep the armor envelope
    /// around it is, and where the hips and shoulders sit. The same numbers drive the Blender skeleton
    /// (Tools/Blender/skeleton.py, CLASSES) - change both together.
    /// </summary>
    public struct MechFrameSpec
    {
        public MechWeightClass weight;
        public float frame;        // skeleton thickness
        public float armorWidth;   // armor envelope width
        public float armorDepth;   // armor envelope depth
        public float seamOffset;   // distance from the bone to the armor's inner face
        public float jointRadius;  // rotary joint size
        public float hipX;         // humanoid: half the hip-track width
        public float shoulderX;    // humanoid: shoulder joint offset from the spine
        public float collar;       // humanoid: where the pauldron bone leaves the torso
        public float legX;         // quadruped: half the leg spacing across the body

        public static MechFrameSpec Get(MechWeightClass weight)
        {
            switch (weight)
            {
                case MechWeightClass.Light:
                    return new MechFrameSpec { weight = weight, frame = 0.82f, armorWidth = 0.88f, armorDepth = 0.85f, seamOffset = 0.90f, jointRadius = 0.90f, hipX = 0.43f, shoulderX = 1.02f, collar = 0.32f, legX = 0.66f };
                case MechWeightClass.Heavy:
                    return new MechFrameSpec { weight = weight, frame = 1.20f, armorWidth = 1.14f, armorDepth = 1.15f, seamOffset = 1.12f, jointRadius = 1.12f, hipX = 0.55f, shoulderX = 1.24f, collar = 0.42f, legX = 0.80f };
                default:
                    return new MechFrameSpec { weight = MechWeightClass.Medium, frame = 1f, armorWidth = 1f, armorDepth = 1f, seamOffset = 1f, jointRadius = 1f, hipX = 0.48f, shoulderX = 1.12f, collar = 0.36f, legX = 0.72f };
            }
        }

        public static MechWeightClass Cycle(MechWeightClass weight, int delta)
        {
            return (MechWeightClass)((((int)weight + delta) % 3 + 3) % 3);
        }

        /// <summary>Socket size of the side mounts and of the main (centre / dorsal) mount for a torso class.</summary>
        public static void HardpointSizes(MechWeightClass torso, out HardpointSize side, out HardpointSize main, out HardpointSize aux)
        {
            switch (torso)
            {
                case MechWeightClass.Light: side = HardpointSize.Small; main = HardpointSize.Medium; aux = HardpointSize.Small; break;
                case MechWeightClass.Heavy: side = HardpointSize.Large; main = HardpointSize.Large; aux = HardpointSize.Medium; break;
                default: side = HardpointSize.Medium; main = HardpointSize.Large; aux = HardpointSize.Medium; break;
            }
        }
    }
}
