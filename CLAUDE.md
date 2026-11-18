# CLAUDE.md — IronFauna (Unity 6 URP)

When building gameplay, feel, audio, shell, or live features, load the `craft-game-floors` skill; if it isn't installed, ask the user whether to run `unity skill install <client>` (ask: never run it without a yes).

---

## 1. Project Overview
- **Title:** IronFauna · Unity `6000.6.8f1` · URP `17.6.0` · Input System only.
- **The game (owner, 2026-11-17/18): a Gang Beasts-style physics mech brawler.** Humanoid robots fight with MMA moves, grapple, throw and slam, and tear each other apart limb from limb; every strike is readable and committed, with real weight (For Honor / Dark Souls). Targets: **8-player online + local co-op** (split screen, the camera sits behind your own mech), **destructible arenas and fighters**, a garage to build fighters from clamshell armor.
- **Removed 2026-11-18 at the owner's request** ("remove the deploy, remove anything not related to the mech fighting game, remove the four-legged one"): the tower-defence game (objective core, enemy walkers, turrets, grid), Deploy and the piloting/shooting code (`MechPilotController`, `MechPilotCamera`, `MechRobotOperator`, `MechFleetCoordinator`), Antigravity's HUD and input controller, `SampleScene`, the boxing-ring prototype, the Unity template readme, and the whole quadruped chassis (builder code, garage option, 44 models, Blender generators). All of it is in git history before commit "Clean up: fighting game only".
- Scenes: `Assets/Scenes/MainMenu.unity` is the only scene; the brawl runs inside it (FIGHT builds the arena, Esc reloads the scene).

## 2. Art Bible & Visual Rules (`Assets/Scripts/Core/IronFaunaPalette.cs`)
- **Style:** realistic hard-surface mecha — physically based metal and coated paint (URP Lit), a dark hangar studio for the title/garage (key/rim/fill lights, generated reflection cubemap, ACES, restrained bloom), an outdoor rocky island for brawls (sun, sky fill, haze). `IronFaunaMaterials.Realistic` (default true); false restores the old toon shader.
- **Presentation:** title and garage show the mech in `MechClamshellBuilder.showcasePose` with `MechShowcaseStage.AnimateIdle`.
- **Lessons:** big flat glossy surfaces mirror the lights into white glare — keep them matte (`MakeMatte`). `Mathf.SmoothStep(a, b, t)` interpolates; it is not GLSL `smoothstep` (use `Ramp`).
- **Garage UI skin:** dark translucent panels, `Ui*` palette text colours, 2400×1350 reference canvas.
- **Palette (single source of truth — never use literal `Color.*` outside `IronFaunaPalette.cs`):** `CrimsonFront` `#C8282E` (front halves) · `MaroonRear` `#6E1E26` (back halves) · `GunmetalFrame` `#3A3F46` · `DarkIron` `#23272D` · `OffWhitePanel` `#F0ECE1` · `HazardYellow` `#E6B800` · `ShadowSlate` `#1E2229` · `OrangeJointCore` `#FF7A1A` · `CyanEnergy` `#24E5FF`, plus `Steel*`, `Studio*`, `Terrain*`, `SkyHaze`, `SunWarm` and the paint schemes.

## 3. Two-Layer Build: Skeleton + Clamshell Armor (180 parts, 10 sets, 3 weight classes)
- Every mech is (1) a structural skeleton that looks finished on its own and (2) two-piece clamshell armor that clamps onto it (front half crimson, back half maroon, toggle latches, seams down the sides only). Visual blend: Armored Core 4/For Answer + Zoids + Gundam inner frames.
- **Exposed joint hubs:** armor stops short of every joint drum with 45° bevelled clearance; never cover a joint.
- Leg stance enum (`LegPosePreset`, 5 stances) still exists in the builder; fighters always use `Pose1_HumanLeg`.
- Not built yet: overlapping knee armor, separate leg types, armor that changes fighting stats beyond the garage numbers.
- Part catalogues: `Docs/ironfauna_*_clamshell_catalog.md`, `Docs/Blueprints/*.jpg`; design summary `Docs/GDD_and_Clamshell_Architecture.md`.

## 4. Code Map
- `Assets/Scripts/Clamshell/` — `MechClamshellBuilder` (procedural humanoid assembler: skeleton, armor, hubs, `legRigs`/`armRigs` with IK pivots, hardpoints, `GetSlot/SetSlot`, `ApplyLoadout/CaptureLoadout`), `ClamshellMeshFactory`, `ClamshellMeshLibrary` (loads/fits FBX parts), `ClamshellFeatureKit` (`ClamshellParts.Spawn/Empty`), `ClamshellCatalog` (set names, perks, stats), `ClamshellPartData` (slot/set/pose enums), `MechLoadout` (+ `MechLoadoutStore`, PlayerPrefs JSON), `MechFrameSpec` (weight classes), `MechHardpoint`.
- `Assets/Scripts/Core/` — `IronFaunaPalette`, `IronFaunaMaterials`.
- `Assets/Scripts/Menu/` — `IronFaunaMenuRoot` (title: FIGHT / GARAGE / QUIT; garage: paint, weight classes, armored/skeleton view, per-slot sets, stats Armor/Speed/Power/Stamina; FIGHT saves and starts a brawl), `MechShowcaseStage` (turntable, studio lights, orbit camera), `MenuUI` (code-built uGUI kit).
- `Assets/Scripts/Gameplay/MechGaitAnimator.cs` — the walk cycle (hips + legs) of every fighter.
- `Assets/Scripts/Boxing/` — `MechBoxer` (fighter brain + upper body), `MechBoxerAI`, `MechBoxerPlayerInput`, `BoxingFeel` (hit-stop, trauma), `BoxingSfx` (synthesised impact/clank/whoosh).
- `Assets/Scripts/Brawl/` — `MechRagdoll`, `MechRagdollPart`, `MechBrawlMatch`, `MechBrawlCamera`, `BrawlTerrain`, `BreakableProp`.
- `Assets/Editor/` — `IronFaunaMenuSceneSetup` (creates MainMenu, only build scene, rebuilds mechs in open scenes, toon upgrade menu), `IronFaunaModelDrop` (bulk FBX zip import), `ClamshellModelImporter`.

## 5. Leg Clearance Rules (verified numerically)
- Shell ends stop `jointRadius + 0.06` from each hub and bevel 45° (depth shrinks by `0.15`).
- Thigh details live on the hip-side band (`0.02..0.38` of shell length), calf details on `-0.22..0.10`, so a deep fold cannot close details together.
- `Tools/Blender/fold_check_all.py <blend> [--aw --ad --ai --jr]` checks thigh/calf/foot of every set in all 5 stances per weight class.

## 6. Modeled Parts (Blender → FBX)
- `Assets/Resources/ClamshellMeshes/<Slot>_<Set>.fbx` replaces the procedural half for that slot/set; `ClamshellMeshLibrary` re-orients and fits it into the slot envelope. Material names pick the colour role: Shell, Panel, Hazard, Dark, Frame, Glow, Copper.
- Plate authoring: bone along Blender Z, plate on the -Y side with its inner face at Y = 0, depth about 1.6× the envelope depth. Feet (`FootFront/FootBack`) are lengthwise parts at true size: X width, Z length (+Z = toe/heel tip), height toward -Y; `TryGetLengthwise` scales them uniformly.
- **`Tools/Blender/aero_sets.py` builds all 180 armor parts** (second-generation angular shapes; Set01 from `aero_ironclad.py`; sets 02-10 = dart + hood base with each set's signature from `clamshell_sets.py`, which is now a library). `ironclad.py` holds `Part` (polish: smooth shading, 9 mm 2-segment bevel, weighted normals; FBX with `mesh_smooth_type="OFF"`).
- **Third generation (2026-11-17, owner: "sleek and polished"; Set01 only, as a test):** `Tools/Blender/sleek_ironclad.py` — dense smooth car-body surfaces with crisp shoulder lines, gapped raised panels, inlaid lines, swept fins, fan-triangulated caps. If approved, port sets 02-10 to it. The owner found the result clean but "more boxing game"; the art direction is still open.
- `Tools/Blender/skeleton.py` builds 23 `Skel_*.fbx` pieces (humanoid torsos per class, limb bones, feet, hand, head, joints, joint guard). Skeleton pieces are true size, never fitted (`TryGetRaw`). Mirrors `MechFrameSpec` — change both together.
- Materials: skeleton = matte cast metal (`SteelCast`), recesses `SteelBlack`, armor = satin coated composite; shared generated wear map; emissives razor-thin.
- Delivery: zip with `Assets/...` entries at `<project>/ModelDrop/models.zip`, then `IronFauna > Import Model Drop` (auto-runs only on a script reload). Zips over ~10 MB go by file UUID (SendUserFile).

## 7. Skeleton, Weight Classes & Hardpoints
- `MechFrameSpec`: Light/Medium/Heavy frame thickness, armor width/depth, seam offset, joint radius, hip/shoulder/collar spacing. **Mirrors `CLASSES` in `Tools/Blender/skeleton.py`.**
- `torsoWeight` (torso, arms, hardpoint sizes) and `legWeight` (legs) are independent, saved in `MechLoadout`, and change stats and the walk cycle profile. `showArmor = false` shows the bare skeleton.
- Clearance: where armor clamps on, the skeleton stays inside the slot between the halves. Head and hands are universal.
- Hardpoints (`MechHardpoint`): `BackUpperL`, `BackUpperR`, `BackCentre`, `LowerSpine`; size by torso class. Unused by the brawler so far.

## 8. The Brawler
- **Milestones:** (1) active-ragdoll fighters [done]; (2) strikes, guard, poise, knockdowns, kicks/knees/elbows [done, first pass]; (3) grapples, throws, slams, limb tearing [done, first pass]; (4) networking (server-authoritative physics, clients send `MechBoxer.Intent`) [next]; (5) split-screen local co-op.
- **Each fighter = a hidden animated ghost + a physical body.** The ghost (`MechClamshellBuilder` + `MechGaitAnimator` + `MechBoxer` + input or AI) moves and poses; `MechRagdoll` turns a second builder into Rigidbody segments (pelvis = visualRoot, torso, head, upper/forearms, thighs/calves/feet; box colliders; ConfigurableJoints with slerp drives) that follow it. While standing, pelvis and legs are **kinematic copies** of the walk cycle (owner: Deploy's legs were "almost perfect"); torso, arms and head stay physical so hits rock them. Knockdown, being grabbed, or no floor under the hips (`SupportUnder`) = whole body limp; get-up re-centres the ghost and blends back over 0.6 s. Physics step 1/90 s in a match.
- **Movement:** camera-relative WASD (`moveReference`), Ctrl sprint, no lock-on (owner: removed) — faces where it walks and turns into an opponent within `punchTurnRange` only to strike; `followGround` rides the terrain (`MechBoxer.TryGround` ignores rigidbodies). Keep the cursor inside the game window.
- **Strikes (`MechBoxer` specs):** exaggerated, readable wind-ups, torso drive, lunges, full arm extension, slow recoveries; `StrikeTrail`s on fists/feet. LMB jab, RMB cross, Q hook, E uppercut, F front kick, R roundhouse, Shift guard, C slip; scroll / D-pad picks one of 12 specials (spinning backfist, superman punch, haymaker, spinning elbow, front/side/axe/roundhouse/spinning hook kick, flying knee, headbutt, shoulder charge), Space / RT throws it. Kicks override one leg after the walk cycle (`KickLeg`, `SolveLegAt`).
- **Damage (`MechRagdoll`):** strikes land through collisions (`OnPartCollision` → `TryStrike`: arms, legs for kicks, head for headbutts, torso for charges; once per `PunchSerial`), damage from the spec × contact speed. Per-limb health, armor halves crack off (`ShedArmor`), limbs tear off (`RipLimb`), poise break = knockdown, head lost = KO. Big knockback (`knockbackScale`). Downed bodies take landing damage and bowl over mechs they crash into (`ImpactDamage`).
- **Grapples:** hold G / LT to reach (`MechBoxer.Reaching`); `TryGrab` clamps onto the nearest mech part or a loose limb; the held part is hauled to `HoldPoint` with the victim's whole mass (victim limp, breaks free after 4 s). While holding: LMB or release = throw, RMB/Q = tear (`TugLimb`, the torn limb becomes a club), E/Space = slam; a held limb swings on any strike (`WeaponStrike`). AI grabs, throws, slams and tears too.
- **Match (`MechBrawlMatch`, title FIGHT):** procedural island (`BrawlTerrain`: hills, boulders, cliff rim), rock pillars and crates (`BreakableProp`), player + 3 bots free-for-all, KO or ring-out eliminates, last standing wins, round resets; HUD hull/balance bars, special selector, controls; `MechBrawlCamera` (mouse orbit, terrain collision, shake). Impact feel via `BoxingFeel` + `BoxingSfx`.
- Known gaps: everything is tuned blind until tested; bots use the same moves but simple decisions; no online or split screen yet.
