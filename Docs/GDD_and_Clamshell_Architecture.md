# IronFauna — Master GDD & 220-Part Universal Clamshell Reference

This folder contains the design and engineering documentation for **IronFauna** (Unity 6 URP, 3rd-person biomechanical/humanoid mech combat + grid tower defense hybrid).

## 1. Art Bible (Three-Line Spec)
1. **Style Statement:** Borderlands × Zoids hard-surface cel-shaded industrial mecha — flat-faced machined steel clamshell plates, sharp 45° chamfers, bold dark-ink outlines, two-band toon lighting on a tan blueprint-grid arena.
2. **Palette (`IronFaunaPalette.cs`):**
   - `FloorTan`: `#C8B89A` · `GunmetalFrame`: `#3A3F46` · `CrimsonFront`: `#C8282E` · `MaroonRear`: `#6E1E26` · `OffWhitePanel`: `#F0ECE1` · `HazardYellow`: `#E6B800` · `ShadowSlate`: `#1E2229` · `OrangeJointCore`: `#FF7A1A` · `CyanEnergy`: `#24E5FF`
3. **Mood:** Warm industrial key light (`#FFF0D6`, 42° elevation) across the tan blueprint arena with cool slate shadow bounce (`#4A586E`) and HDR bloom on exposed `#FF7A1A` orange rotary joint hubs and `#24E5FF` cyan optics/radiators.

## 2. Universal Clamshell Construction Rules (No Weight Classes)
- **Single-Bone Clamping:** Every armor piece splits into **two clamshell halves** (`Front Half` in Crimson Red `#C8282E`, `Rear Half` in Darker Maroon `#6E1E26`) that clamp onto **one bone only** with mechanical toggle latches across the seam.
- **100% Exposed Rotary Joint Hubs:** Armor stops short of every circular joint hub (`Hip`, glowing-orange `Knee`, `Ankle`, `Neck`, `Shoulder`, `Elbow`, `Wrist`, `Waist`) with `45°` beveled clearance chamfers so limbs can fold into both **Human Forward-Bend** and **Chicken Reverse-Joint** stances without plates colliding.
- **Universal Seam & Collar Envelope:** All 10 sets share identical base collar widths, `45°` bevel angles, and toggle-latch coordinates across **220 interchangeable halves**:
  - **Legs (60 parts):** 10 Sets × 6 Slots (`Thigh Front`, `Thigh Back`, `Calf Front`, `Calf Back`, `Foot Front/Toe`, `Foot Back/Heel`)
  - **Bodies (40 parts):** 10 Humanoid Upright Sets (`Chest Front`, `Backplate Rear`) + 10 Quadruped Animal Sets (`Thorax Front`, `Haunch Rear`) — standalone limbless cores
  - **Arms (80 parts):** 10 Sets × 8 Slots (`Shoulder Front/Back`, `Upper Arm Front/Back`, `Lower Arm Front/Back`, `Hand Front/Back`)
  - **Heads (40 parts):** 10 Humanoid Sets (*Halo: Reach × Gundam × Armored Core* inspired `Visor Front` + `Cranium Rear`) + 10 Animal/Biomech Sets (`Snout Front` + `Mane/Crest Rear`)

## 3. Core Game Loop & Milestone 1 Plan
- **M1-A (Gray-Box + Clamshell Prototype):** Flat arena scene with central Objective + HP bar, third-person Mech Controller (move, aim, shoot + Universal Clamshell procedural skeleton & adjustable knee pivot), Enemy NavMesh walker pathfinding to Objective, Kill → Credit counter.
- **M1-B (Grid Defense System):** Build grid around Objective, Wall placement (`30` credits, `NavMeshObstacle` dynamic carving), Build phase timer between waves, Auto-Turret (`150` credits, targets nearest enemy).
- **M1-C (Hangar & Between-Wave Shop):** Weapon/Clamshell customization + between-wave shop (mech upgrade vs. grid defense tension).
- **M1-D (First Playtest Build):** 5–10 wave Survival run.
