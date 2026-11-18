using UnityEngine;

namespace IronFauna.Clamshell
{
    public enum ClamshellSlot
    {
        // Legs (6 slots per leg)
        ThighFront,
        ThighBack,
        CalfFront,
        CalfBack,
        FootFront,
        FootBack,

        // Body (2 slots)
        BodyFront,
        BodyBack,

        // Arms (8 slots per arm)
        ShoulderFront,
        ShoulderBack,
        UpperArmFront,
        UpperArmBack,
        LowerArmFront,
        LowerArmBack,
        HandFront,
        HandBack,

        // Head (2 slots)
        HeadFront,
        HeadBack
    }

    public enum ClamshellSetArchetype
    {
        Set01_Ironclad,   // Standard flat chamfered plates + off-white stripe / hazard block
        Set02_Bastion,    // ERA reactive armor bricks / reserve ammo & recoil anchor
        Set03_Overclock,  // Cut-out cyan conduit frames / angled heat-sink radiator louvers
        Set04_Ripjaw,     // Serrated steel kicking cleats & saw-blades / twin micro-thrusters
        Set05_Afterburn,  // Ram-air intake scoops / vectored rectangular rocket nozzles
        Set06_Voltaic,    // Copper Tesla induction coils & arc-nodes / capacitor cell banks
        Set07_Salvo,      // Flush micro-missile launcher doors / chaff & flare dispensers
        Set08_Goliath,    // Heavy pneumatic pile-driver rams / counterweight ballast tanks
        Set09_Mantis,     // Faceted radar-deflecting stealth prisms / baffled ECM shrouds
        Set10_Aegis       // Slotted cyan hard-light projector rails / field-inverter packs
    }

    public enum LegPosePreset
    {
        Pose1_HumanLeg,
        Pose2_ChickenBendForward,
        Pose3_ReverseExtended,
        Pose4_ChickenBendBackward,
        Pose5_ReverseCriticalFold
    }

    [CreateAssetMenu(fileName = "ClamshellPart", menuName = "IronFauna/Clamshell Part Data")]
    public class ClamshellPartData : ScriptableObject
    {
        public string partId = "IF_PART_01";
        public string displayName = "Ironclad Front Shell";
        public ClamshellSlot slot = ClamshellSlot.ThighFront;
        public ClamshellSetArchetype setArchetype = ClamshellSetArchetype.Set01_Ironclad;

        [Header("Optional Custom Blender Prefab (Uses Procedural Clamshell if Null)")]
        public GameObject customPartPrefab;

        [Header("Gameplay Modifiers (No Weight Classes — Pure Build Tradeoffs)")]
        public float armorBonus = 25f;
        public float moveSpeedMultiplier = 1.0f;
        public float weaponDamageMultiplier = 1.0f;
        public float cooldownMultiplier = 1.0f;
        public int creditCost = 100;
    }
}
