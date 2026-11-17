# CLAUDE.md — IronFauna (Unity 6 URP)

When building gameplay, feel, audio, shell, or live features, load the `craft-game-floors` skill; if it isn't installed, ask the user whether to run `unity skill install <client>` (ask: never run it without a yes).

---

## 1. Project Overview
- **Title:** IronFauna (v0.1 Prototype)
- **Engine:** Unity `6000.6.8f1` (`x86_64`) · Universal Render Pipeline (URP `17.6.0`)
- **Concept:** Third-person biomechanical/humanoid mech action + grid-based tower defense hybrid.
- **Core Hook:** *You are the tower AND the defender.* Every enemy wave kill earns credits—spend them upgrading/swapping your mech's modular clamshell armor & weapons, or building walls, barriers, and auto-turrets on the grid around the central Objective Core.

## 2. Art Bible & Visual Rules (`Assets/Scripts/Core/IronFaunaPalette.cs`)
- **Style Statement (changed 2026-11-16 at the owner's request: "looks real, feels real"):** realistic hard-surface mecha — physically based metal and coated paint (URP Lit), dark hangar studio with key/rim/fill lights, a generated reflection environment, ACES grading, restrained bloom. `IronFaunaMaterials.Realistic` (default true) selects this look; false restores the older cel-shaded toon shader (`IronFauna/Toon`, ink outlines, tan blueprint floor).
- **Presentation:** the title screen and garage show the mech in a posed stance (`MechClamshellBuilder.showcasePose`: legs splayed, toes out, arms raised, head turned) with a slow idle motion (`MechShowcaseStage.AnimateIdle`). Gameplay leaves `showcasePose` off; the motion controller poses the mech.
- **Lessons:** large flat glossy surfaces (floor, turntable) mirror the rim light and softboxes into a white glare — keep them matte (`MakeMatte`). `Mathf.SmoothStep(a, b, t)` interpolates; it is not GLSL `smoothstep` (use `Ramp`).
- **Garage UI skin:** dark translucent panels and cool text colours from the `Ui*` palette entries, drawn on a 2400×1350 reference canvas for finer type.
- **Palette (Single Source of Truth — never use literal `Color.*` outside `IronFaunaPalette.cs`):**
  - `FloorTan`: `#C8B89A` · `GunmetalFrame`: `#3A3F46` · `DarkIron`: `#23272D`
  - `CrimsonFront`: `#C8282E` (all Front/Outer clamshell halves)
  - `MaroonRear`: `#6E1E26` (all Rear/Inner clamshell halves)
  - `OffWhitePanel`: `#F0ECE1` · `HazardYellow`: `#E6B800` · `ShadowSlate`: `#1E2229`
  - `OrangeJointCore`: `#FF7A1A` (emissive rotary joint cores) · `CyanEnergy`: `#24E5FF` (visors, radiators, hard-light, reactor core)

## 3. Two-Layer Build: Skeleton + Universal Clamshell Armor (220 Interchangeable Parts, 3 Weight Classes)
- **Design brief (adopted 2026-11-16, replaces older rules where they conflict):** every mech is (1) a structural skeleton that looks finished on its own and (2) two-piece clamshell armor that mounts on it. Visual blend: Armored Core 4/For Answer + Zoids Battle Legends + Gundam inner frames. Assembly order: torso (Humanoid/Animal × Light/Medium/Heavy) → legs (4 types × 3 weights) → helmet → arms (grip hands, wrist weapons, forearm weapons, whole-arm guns). Seams run down the sides only, never the centre of the face or chest.
- **Built so far:** skeleton + weight classes + hardpoint sockets (section 7). **Not built yet:** overlapping/sliding knee armor (armor still stops short of every joint), the 4 separate leg types (still one two-bone leg with 5 stances), arm weapon types, weapons for the sockets, destruction.
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
- **Second-generation shapes (2026-11-16, owner's direction: aerodynamic, angular, fast, militaristic - not boxy):** `Tools/Blender/aero_ironclad.py` rebuilds Set01 (16 plates + toe and heel) and is the template for redoing the other sets. Rules: no rectangles - every plate is a dart with a creased centre keel and steep chamfered flanks, pointed toward the joint it folds past; a full-width hood with a chevron leading edge overlaps the bulk half on a dark underlay (barbed arrowhead silhouette); raked edge blades; ramped scoops ending in a dark exhaust mouth on backs; sparse detail (polished spine strip, bolts, one hazard mark). `lx()` lofts with scale plus shear. 
- **`Tools/Blender/aero_sets.py` is now the one script that builds all 220 armor parts** (run it instead of `ironclad.py` / `clamshell_sets.py`, which remain as libraries): Set01 from `aero_ironclad.py`; sets 02-10 as the same dart + hood base with a flat deck that carries each set's signature (`front_sig`, `back_sig`, `eyes`, `head_back` from `clamshell_sets.py`); one pointed toe and heel shared by every set; quadruped bodies and animal heads on a faceted `fuselage` shell (patched over `SetStyle.lw_shell`). All ten sets clear the fold check in all three weight classes. Known rough spots: hands and feet are identical across sets, quadruped bodies are the least refined shapes, and Unity discards a few self-intersecting faces on Ripjaw's small saw-tooth details (import warnings).
- **Polish pass (2026-11-16):** `Part.to_object` (ironclad.py) finishes every part with smooth shading, a 2-segment 9 mm Bevel (angle limit 30°, harden normals) and a Weighted Normal modifier; FBX is exported with `mesh_smooth_type="OFF"` so those normals survive. `ClamshellMeshLibrary` now keeps the modeled normals through fitting (never `RecalculateNormals` on modeled parts) and adds box-projected UVs for the wear map. Hoods on sets 02-10 have a recessed deck panel (`deck_hood`, `REC`) that the set's signature sits in. `Skel_JointGuard` is a layered cowl mounted on the knee and elbow joints (`MechClamshellBuilder.JointGuard`), on the open side of the bend so it never meets the limb plates. Rear torso brackets tie the back sockets to the shoulder yoke.
- **Materials (realistic look):** skeleton = dark matte cast metal (`SteelCast`, metallic 0.7 / smoothness 0.34), recesses = `SteelBlack`, armor = satin coated composite (metallic 0.45 / smoothness 0.6); all share a generated tiling wear map (albedo mottle + smoothness in alpha). Emissives stay razor-thin (visor, sensor slits and nodes, recessed thruster glow).
- `IronFaunaModelDrop` only auto-runs on a script reload: after dropping a zip without code changes use `IronFauna > Import Model Drop`. Zips over ~10 MB must be delivered with a file UUID (SendUserFile), not a staged path.
- `Assets/Editor/IronFaunaModelDrop.cs`: bulk delivery - a zip at `<project>/ModelDrop/models.zip` with project-relative `Assets/...` entries is unpacked on the next editor refresh (or `IronFauna > Import Model Drop`) and then deleted.
- Material role `Copper` maps to `IronFaunaPalette.CopperCoil`.
- `Tools/Blender/fold_check_all.py <blend>` checks thigh/calf/foot of every set in all 5 stances (toe details stay under ~0.30 m height: the calf passes over the toe in Pose2).

## 7. Skeleton, Weight Classes & Hardpoints
- `Assets/Scripts/Clamshell/MechFrameSpec.cs` — `MechWeightClass` (Light/Medium/Heavy) and the per-class numbers: frame thickness, armor width/depth, seam offset, joint radius, hip/shoulder/collar/quadruped leg spacing. **Mirrors `CLASSES` in `Tools/Blender/skeleton.py` — change both together.** Medium = the original universal envelope.
- `MechClamshellBuilder.torsoWeight` (torso, arms, hardpoint sizes) and `legWeight` (thigh, calf, foot, leg joints) are independent; both are saved in `MechLoadout` and affect stats. `showArmor = false` builds the bare skeleton (garage "View" toggle).
- `Tools/Blender/skeleton.py` builds 26 `Skel_*.fbx` pieces into `Assets/Resources/ClamshellMeshes/`: torso frames (`Skel_TorsoHumanoid_<Class>`, `Skel_TorsoAnimal_<Class>`), limb bones with pistons and armor hangers (`Skel_<Thigh|Calf|UpperArm|LowerArm>_<Class>`), `Skel_Foot_<Class>`, `Skel_Hand`, `Skel_Head`, `Skel_HeadAnimal`, `Skel_Joint`/`Skel_JointCore` (unit radius).
- Skeleton pieces are true size and never fitted: Blender X = side, -Y = front, Z = up, origin at the pivot; `ClamshellMeshLibrary.TryGetRaw` loads them as they are. If a model is missing the builder falls back to plain struts and drums.
- Clearance rule: where armor clamps on, the skeleton stays inside the slot between the halves (|front/back| below the seam offset). Head and hands are universal (not weight-classed).
- Hardpoints (`MechHardpoint` components in `MechClamshellBuilder.hardpoints`): humanoid `BackUpperL`, `BackUpperR`, `BackCentre`, `LowerSpine`; animal `Dorsal`, `FlankL`, `FlankR`, `Tail`. Socket size by torso class — Light: Small sides / Medium main; Medium: Medium / Large; Heavy: Large / Large.
- `fold_check_all.py <blend> --aw --ad --ai --jr` re-runs the leg clearance check for a weight class (all 10 sets clear in all three).
- `Assets/Editor/ProjectBootstrap/IronFaunaBaselineAndSceneSetup.cs` — Editor setup for URP Global Volume (`Assets/Settings/IronFaunaGlobalVolumeProfile.asset`), lighting, and the `SampleScene.unity` Clamshell Arena.
