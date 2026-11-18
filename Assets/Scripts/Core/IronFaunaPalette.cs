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

        // Blueprint grid lines (title / garage floor)
        public static readonly Color GridLineLight     = new Color32(0xDA, 0xCC, 0xB0, 0xFF); // #DACCB0 Minor grid line
        public static readonly Color GridLineDark      = new Color32(0xA8, 0x96, 0x74, 0xFF); // #A89674 Major grid line

        // UI neutrals (multiplier white for uGUI tints)
        public static readonly Color UiNeutral         = new Color32(0xFF, 0xFF, 0xFF, 0xFF); // #FFFFFF Untinted UI graphic
        public static readonly Color UiPanel           = new Color32(0x0A, 0x0F, 0x17, 0xFF); // #0A0F17 Garage panel backing (used translucent)
        public static readonly Color UiButton          = new Color32(0x1A, 0x23, 0x30, 0xFF); // #1A2330 Idle button / row control
        public static readonly Color UiSelect          = new Color32(0x2F, 0x5F, 0x8F, 0xFF); // #2F5F8F Hovered / selected control
        public static readonly Color UiText            = new Color32(0xDC, 0xE4, 0xEE, 0xFF); // #DCE4EE Primary readout text
        public static readonly Color UiTextDim         = new Color32(0x85, 0x92, 0xA3, 0xFF); // #8592A3 Labels and hints
        public static readonly Color UiAccent          = new Color32(0xA9, 0xD4, 0xFF, 0xFF); // #A9D4FF Header readouts, rules, stat bars

        // Hangar studio (realistic look)
        public static readonly Color SteelBare         = new Color32(0x5C, 0x63, 0x6D, 0xFF); // #5C636D Machined skeleton steel
        public static readonly Color SteelCast         = new Color32(0x3A, 0x40, 0x48, 0xFF); // #3A4048 Matte cast skeleton metal
        public static readonly Color SteelBlack        = new Color32(0x1C, 0x20, 0x26, 0xFF); // #1C2026 Blackened recesses, conduits, treads
        public static readonly Color SteelDark         = new Color32(0x2C, 0x31, 0x38, 0xFF); // #2C3138 Blackened steel: joints caps, cables, treads
        public static readonly Color StudioBackdrop    = new Color32(0x0B, 0x12, 0x1C, 0xFF); // #0B121C Hangar void behind the mech
        public static readonly Color StudioFloor       = new Color32(0x16, 0x1B, 0x23, 0xFF); // #161B23 Polished hangar deck
        public static readonly Color StudioFloorLine   = new Color32(0x24, 0x2C, 0x38, 0xFF); // #242C38 Deck panel seams
        public static readonly Color StudioKey         = new Color32(0xFF, 0xF4, 0xE6, 0xFF); // #FFF4E6 Overhead key light
        public static readonly Color StudioRim         = new Color32(0x8F, 0xC4, 0xFF, 0xFF); // #8FC4FF Cool rim light from behind
        public static readonly Color StudioFill        = new Color32(0x4A, 0x5A, 0x72, 0xFF); // #4A5A72 Soft fill / ambient sky
        public static readonly Color StudioAmbientLow  = new Color32(0x0E, 0x12, 0x18, 0xFF); // #0E1218 Ambient ground

        // Brawl terrain (rocky hill island & multi-stage palettes)
        public static readonly Color TerrainDirt       = new Color32(0x5B, 0x4E, 0x40, 0xFF); // #5B4E40 Packed dirt / scrub
        public static readonly Color TerrainRock       = new Color32(0x6E, 0x6A, 0x63, 0xFF); // #6E6A63 Weathered basalt
        public static readonly Color TerrainRockDark   = new Color32(0x3E, 0x3B, 0x37, 0xFF); // #3E3B37 Cliff faces, shadowed rock
        public static readonly Color SkyHaze           = new Color32(0x8C, 0x9B, 0xAA, 0xFF); // #8C9BAA Overcast sky / distance fog
        public static readonly Color SunWarm           = new Color32(0xFF, 0xE6, 0xC4, 0xFF); // #FFE6C4 Low afternoon sun

        // Stage palettes (Foundry, Orbital, Scrapyard, Glacier)
        public static readonly Color FoundryDeck       = new Color32(0x2B, 0x28, 0x26, 0xFF); // #2B2826 Sooty smelter deck
        public static readonly Color FoundryRust       = new Color32(0x6E, 0x38, 0x26, 0xFF); // #6E3826 Oxidised foundry girder
        public static readonly Color FoundrySky        = new Color32(0x38, 0x22, 0x1A, 0xFF); // #38221A Smelter smoke haze
        public static readonly Color OrbitalDeck       = new Color32(0xC8, 0xD2, 0xDC, 0xFF); // #C8D2DC High-altitude dish ceramic
        public static readonly Color OrbitalSky        = new Color32(0x16, 0x28, 0x44, 0xFF); // #162844 Stratosphere deep blue
        public static readonly Color ScrapyardSand     = new Color32(0x78, 0x64, 0x4C, 0xFF); // #78644C Rusty pit floor
        public static readonly Color ScrapyardSky      = new Color32(0x9E, 0x84, 0x68, 0xFF); // #9E8468 Dusty colosseum haze
        public static readonly Color GlacierIce        = new Color32(0x9A, 0xBE, 0xD8, 0xFF); // #9ABED8 Glacial blue ice
        public static readonly Color GlacierSnow       = new Color32(0xD8, 0xE4, 0xEE, 0xFF); // #D8E4EE Packed ridge snow
        public static readonly Color GlacierSky        = new Color32(0x6C, 0x88, 0xA6, 0xFF); // #6C88A6 Subzero storm sky

        // City & Forest Stage palettes
        public static readonly Color CityAsphalt       = new Color32(0x48, 0x4E, 0x58, 0xFF); // #484E58 Urban street asphalt
        public static readonly Color CityConcrete      = new Color32(0x98, 0xA2, 0xAE, 0xFF); // #98A2AE Skyscraper concrete facade
        public static readonly Color CityGlass         = new Color32(0x4C, 0x86, 0xB8, 0xFF); // #4C86B8 Office tower glass
        public static readonly Color CitySky           = new Color32(0x3A, 0x4E, 0x6E, 0xFF); // #3A4E6E Dusk metropolitan skyline
        public static readonly Color ForestMoss        = new Color32(0x3E, 0x56, 0x34, 0xFF); // #3E5634 Mossy forest floor
        public static readonly Color ForestBark        = new Color32(0x5A, 0x3C, 0x28, 0xFF); // #5A3C28 Redwood tree trunk bark
        public static readonly Color ForestCanopy      = new Color32(0x2E, 0x62, 0x36, 0xFF); // #2E6236 Pine / redwood foliage
        public static readonly Color ForestSky         = new Color32(0x7A, 0x98, 0x8C, 0xFF); // #7A988C Misty timberland sky

        // Garage paint schemes (front / rear halves). Index 0 is the art-bible default.
        public static readonly Color DesertFront       = new Color32(0xC9, 0xA1, 0x5B, 0xFF); // #C9A15B Desert Raider front
        public static readonly Color DesertRear        = new Color32(0x7A, 0x5A, 0x2E, 0xFF); // #7A5A2E Desert Raider rear
        public static readonly Color ArcticFront       = new Color32(0xDD, 0xE3, 0xE8, 0xFF); // #DDE3E8 Arctic Hunter front
        public static readonly Color ArcticRear        = new Color32(0x6F, 0x7F, 0x8C, 0xFF); // #6F7F8C Arctic Hunter rear
        public static readonly Color NightFront        = new Color32(0x4A, 0x52, 0x60, 0xFF); // #4A5260 Night Ops front
        public static readonly Color NightRear         = new Color32(0x2A, 0x2F, 0x38, 0xFF); // #2A2F38 Night Ops rear
        public static readonly Color JungleFront       = new Color32(0x5E, 0x7A, 0x3E, 0xFF); // #5E7A3E Jungle Stalker front
        public static readonly Color JungleRear        = new Color32(0x2F, 0x44, 0x26, 0xFF); // #2F4426 Jungle Stalker rear
        public static readonly Color CobaltFront       = new Color32(0x2F, 0x6F, 0xC8, 0xFF); // #2F6FC8 Cobalt Lancer front
        public static readonly Color CobaltRear        = new Color32(0x1B, 0x35, 0x66, 0xFF); // #1B3566 Cobalt Lancer rear

        public struct PaintScheme
        {
            public readonly string name;
            public readonly Color front;
            public readonly Color rear;

            public PaintScheme(string name, Color front, Color rear)
            {
                this.name = name;
                this.front = front;
                this.rear = rear;
            }
        }

        public static readonly PaintScheme[] PaintSchemes =
        {
            new PaintScheme("Crimson Standard", CrimsonFront, MaroonRear),
            new PaintScheme("Desert Raider", DesertFront, DesertRear),
            new PaintScheme("Arctic Hunter", ArcticFront, ArcticRear),
            new PaintScheme("Night Ops", NightFront, NightRear),
            new PaintScheme("Jungle Stalker", JungleFront, JungleRear),
            new PaintScheme("Cobalt Lancer", CobaltFront, CobaltRear),
            new PaintScheme("Hazard Works", HazardYellow, GunmetalFrame),
        };

        public static PaintScheme GetPaint(int index)
        {
            int count = PaintSchemes.Length;
            return PaintSchemes[((index % count) + count) % count];
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, t);
        }
    }
}
