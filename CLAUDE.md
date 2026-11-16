# CLAUDE.md — IronFauna (Unity 6 URP)

When building gameplay, feel, audio, shell, or live features, load the `craft-game-floors` skill; if it isn't installed, ask the user whether to run `unity skill install <client>` (ask: never run it without a yes).

---

## 1. Project Overview
- **Title:** IronFauna (v0.1 Prototype)
- **Engine:** Unity `6000.6.8f1` (`x86_64`) · Universal Render Pipeline (URP `17.6.0`)
- **Concept:** Third-person biomechanical/humanoid mech action + grid-based tower defense hybrid.
- **Core Hook:** *You are the tower AND the defender.* Every enemy wave kill earns credits—spend them upgrading/swapping your mech's modular clamshell armor & weapons, or building walls, barriers, and auto-turrets on the grid around the central Objective Core.

## 2. Art Bible & Visual Rules (`Assets/Scripts/Core/IronFaunaPalette.cs`)
- **Style Statement:** Borderlands × Zoids hard-surface cel-shaded industrial mecha — flat-faced machined steel clamshell plates, sharp `45°` chamfers, bold dark-ink outlines, two-band toon lighting on a tan blueprint-grid arena.
- **Palette (Single Source of Truth — never use literal `Color.*` outside `IronFaunaPalette.cs`):**
  - `FloorTan`: `#C8B89A` · `GunmetalFrame`: `#3A3F46` · `DarkIron`: `#23272D`
  - `CrimsonFront`: `#C8282E` (all Front/Outer clamshell halves)
  - `MaroonRear`: `#6E1E26` (all Rear/Inner clamshell halves)
  - `OffWhitePanel`: `#F0ECE1` · `HazardYellow`: `#E6B800` · `ShadowSlate`: `#1E2229`
  - `OrangeJointCore`: `#FF7A1A` (emissive rotary joint cores) · `CyanEnergy`: `#24E5FF` (visors, radiators, hard-light, reactor core)

## 3. Universal Clamshell Modular System (No Weight Classes — 220 Interchangeable Parts)
- **Single-Bone Clamping:** Every armor piece splits into **two clamshell halves** (`Front Half` in Crimson Red + `Back Half` in Darker Maroon) that clamp onto **one bone only** and lock via mechanical toggle latches across the parting seam.
- **100% Exposed Rotary Joint Hubs:** Armor stops short of every circular joint drum (`Hip`, glowing-orange `Knee`, `Ankle`, `Neck`, `Shoulder`, `Elbow`, `Wrist`, `Waist`) with `45°` beveled clearance cutouts. Never cover a joint or allow armor overlap.
- **5 Balanced Leg Poses:** `Pose1_HumanLeg`, `Pose2_ChickenBendForward`, `Pose3_ReverseExtended`, `Pose4_ChickenBendBackward`, `Pose5_ReverseCriticalFold` — in every pose, the Hip Hub stays on the vertical plumb line directly above the Foot.
- **Full Part Catalogs & Concept Blueprints (`Docs/`):**
  - `Docs/GDD_and_Clamshell_Architecture.md` — Master GDD & architecture summary
  - `Docs/ironfauna_10_leg_clamshell_catalog.md` — 60 Leg halves (`10 Sets × 6 Slots`: Thigh Front/Back, Calf Front/Back, Foot Front/Back) + Upper-body expansion
  - `Docs/ironfauna_body_clamshell_catalog.md` — 40 Standalone Body halves (`10 Humanoid Sets` + `10 Quadruped Animal Sets`, no limbs attached)
  - `Docs/ironfauna_arm_clamshell_catalog.md` — 80 Arm halves (`10 Sets × 4 Segments × 2 Halves`: Shoulders, Upper Arm, Lower Arm, Hands)
  - `Docs/ironfauna_head_clamshell_catalog.md` — 40 Head halves (`10 Halo: Reach × Gundam × Armored Core Humanoid Sets` + `10 Animal/Biomech Sets`)
  - `Docs/Blueprints/*.jpg` — All orthographic Top/Side/Front build sheets and 10-set lineups

## 4. Key Codebase Files
- `Assets/Scripts/Core/IronFaunaPalette.cs` — Master hex color palette.
- `Assets/Scripts/Clamshell/ClamshellPartData.cs` — `ScriptableObject` & enums for all 18 Clamshell Slots, 10 Set Archetypes (`Ironclad` through `Aegis`), and 5 Leg Poses.
- `Assets/Scripts/Clamshell/MechClamshellBuilder.cs` — Procedural 3D assembler for both Humanoid and Quadruped Clamshell Mechs with a real 2-bone IK leg solver (hip always above the ankle). Exposes `GetSlot/SetSlot`, `ApplyLoadout/CaptureLoadout`.
- `Assets/Scripts/Clamshell/ClamshellMeshFactory.cs` — Procedural hard-surface meshes (beveled clamshell halves, chamfered boxes, prisms, ridges, facets), flat-shaded and cached by parameters.
- `Assets/Scripts/Clamshell/ClamshellFeatureKit.cs` — Set-specific details for all 10 archetypes × front/back. Details stay inside a band on the plate and below 70% of plate depth so the joint-clearance rules hold.
- `Assets/Scripts/Clamshell/ClamshellCatalog.cs` — Set names (humanoid + animal), perks and per-half stat deltas.
- `Assets/Scripts/Clamshell/MechLoadout.cs` — Serializable loadout (chassis, stance, paint, 18 slots) + `MechLoadoutStore` (PlayerPrefs JSON).
- `Assets/Scripts/Core/IronFaunaMaterials.cs` — Shared material cache (one material per palette colour) + blueprint grid floor material.
- `Assets/Scripts/Gameplay/` — `ObjectiveCore`, `EnemyWalker`, `AutoTurret`, `GridDefenseManager` (one MonoBehaviour per file so they can be added in the Inspector).
- `Assets/Scripts/Menu/` — `IronFaunaMenuRoot` (title screen + garage), `MechShowcaseStage` (turntable, lights, orbit camera), `MenuUI` (code-built uGUI kit), `IronFaunaGameFlow` (MainMenu → Deploy → arena → Esc back to garage).
- `Assets/Editor/IronFaunaMenuSceneSetup.cs` — Creates `Assets/Scenes/MainMenu.unity`, puts it first in Build Settings, and rebuilds procedural mechs when scenes open (`IronFauna` menu).

## 5. Leg Clearance Rules (verified numerically)
- Shell ends stop `jointRadius + 0.06` from each hub and bevel 45° (depth shrinks by `0.15`).
- Thigh details live on the hip-side band (`0.02..0.38` of shell length), calf details on the ankle-side band (`-0.38..-0.02`), so a deep reverse fold cannot close details together.
- `Pose5_ReverseCriticalFold` is 70% extension and `Pose2_ChickenBendForward` 80%; deeper makes thigh/calf front details or calf/toe meet.
- Calf details sit in the `-0.22..0.10` band; thigh details in `0.02..0.38`.

## 6. Hand-Modeled Parts
- `Assets/Resources/ClamshellMeshes/<Slot>_<Set>.fbx` (e.g. `ThighFront_Set01_Ironclad.fbx`) replaces the procedural half for that slot/set; `ClamshellMeshLibrary` re-orients and fits it into the slot envelope. Material names pick the colour role: Shell, Panel, Hazard, Dark, Frame, Glow.
- Authoring: bone along Blender Z, plate on the -Y side with its inner face at Y = 0, total depth about 1.6x the envelope depth. Source script: `Tools/Blender/ironclad.py` (run with Blender or the `bpy` module); `Tools/Blender/fold_check.py` verifies the 5 stances against the real models.
- All 10 sets are modeled (220 parts): `Tools/Blender/clamshell_sets.py` builds sets 02-10 plus feet, quadruped body and animal heads for every set (Set01 limbs/body/head stay in `ironclad.py`). Each set is a `SetStyle` subclass with its signature features (`front_sig`, `back_sig`, `eyes`, `toe_tip`, `animal_body`, `animal_head_detail`).
- Lengthwise parts (`FootFront/FootBack`, `QuadBodyFront/QuadBodyBack`, `AnimalHeadFront/AnimalHeadBack` + `_<Set>.fbx`) are modeled at true size: X width, Z length (+Z = free tip), height toward -Y from Y = 0. `ClamshellMeshLibrary.TryGetLengthwise` scales them uniformly by length and seats them on the bottom; keep details inside the shell's length.
- Material role `Copper` maps to `IronFaunaPalette.CopperCoil`.
- `Tools/Blender/fold_check_all.py <blend>` checks thigh/calf/foot of every set in all 5 stances (toe details stay under ~0.30 m height: the calf passes over the toe in Pose2).
- `Assets/Editor/ProjectBootstrap/IronFaunaBaselineAndSceneSetup.cs` — Editor setup for URP Global Volume (`Assets/Settings/IronFaunaGlobalVolumeProfile.asset`), lighting, and the `SampleScene.unity` Clamshell Arena.
