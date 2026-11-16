using UnityEngine;

namespace IronFauna.Core
{
    /// <summary>
    /// Single source of truth for IronFauna's Borderlands x Zoids Clamshell Art Bible palette.
    /// Defined with pure C# Color32 structs so static initialization is 100% deterministic.
    /// </summary>
    public static class IronFaunaPalette
    {
        // Arena & Environment
        public static readonly Color FloorTan          = new Color32(0xC8, 0xB8, 0x9A, 0xFF); // #C8B89A Warm tan blueprint grid deck
        public static readonly Color GunmetalFrame     = new Color32(0x3A, 0x3F, 0x46, 0xFF); // #3A3F46 Bare gunmetal skeleton & arena walls
        public static readonly Color DarkIron          = new Color32(0x23, 0x27, 0x2D, 0xFF); // #23272D Sole pads, latches, inner sockets

        // Clamshell Armor Palette
        public static readonly Color CrimsonFront      = new Color32(0xC8, 0x28, 0x2E, 0xFF); // #C8282E Front / Outer clamshell halves
        public static readonly Color MaroonRear        = new Color32(0x6E, 0x1E, 0x26, 0xFF); // #6E1E26 Rear / Inner clamshell halves
        public static readonly Color OffWhitePanel     = new Color32(0xF0, 0xEC, 0xE1, 0xFF); // #F0ECE1 Off-white panel strips & stencils
        public static readonly Color HazardYellow      = new Color32(0xE6, 0xB8, 0x00, 0xFF); // #E6B800 Hazard warning stripes

        // Shadows & Outlines
        public static readonly Color ShadowSlate       = new Color32(0x1E, 0x22, 0x29, 0xFF); // #1E2229 Palette-tinted toon shadow & ink outline

        // Emissive Accents (HDR-ready)
        public static readonly Color OrangeJointCore   = new Color32(0xFF, 0x7A, 0x1A, 0xFF); // #FF7A1A Exposed rotary joint core glow
        public static readonly Color CyanEnergy        = new Color32(0x24, 0xE5, 0xFF, 0xFF); // #24E5FF Sensor optics, radiators & hard-light
        public static readonly Color CopperCoil        = new Color32(0xC8, 0x6D, 0x3B, 0xFF); // #C86D3B Voltaic induction coils

        // Lighting Story
        public static readonly Color KeyLightWarm      = new Color32(0xFF, 0xF0, 0xD6, 0xFF); // #FFF0D6 Warm industrial directional key
        public static readonly Color AmbientSkyCool    = new Color32(0x5A, 0x68, 0x7E, 0xFF); // #5A687E Cool slate sky bounce
        public static readonly Color AmbientEquatorMid = new Color32(0x7E, 0x76, 0x6B, 0xFF); // #7E766B Neutral warm-gray equator
        public static readonly Color AmbientGroundDark = new Color32(0x3B, 0x34, 0x2C, 0xFF); // #3B342C Warm ground bounce
    }
}
