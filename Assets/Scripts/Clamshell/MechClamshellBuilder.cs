using UnityEngine;
using UnityEngine.Serialization;
using IronFauna.Core;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Assembles an IronFauna mech (humanoid or quadruped) from procedural clamshell meshes:
    /// - Bare gunmetal skeleton with fully exposed rotary joint hubs (glowing orange knee/elbow/neck cores)
    /// - Two-half clamshells (front + back) clamped onto one bone each, joined by toggle latches
    /// - 45° beveled shell ends and joint-side detail bands, so legs fold into all 5 poses with no overlap
    ///   (verified for every pose with a 2D side-view collision check during development)
    /// - Real two-bone IK: the hip hub always sits on the plumb line directly above the ankle (mid-foot)
    /// </summary>
    public class MechClamshellBuilder : MonoBehaviour
    {
        public const string RootName = "ClamshellMechRoot";
        public const float AnkleHeight = 0.30f;

        [Header("Chassis & Stance Configuration")]
        public bool isQuadrupedChassis = false;
        public LegPosePreset legPose = LegPosePreset.Pose1_HumanLeg;
        [Tooltip("Index into IronFaunaPalette.PaintSchemes")]
        public int paintScheme = 0;
        [Tooltip("Rebuild automatically when the scene starts. The garage turns this off and rebuilds on demand.")]
        public bool buildOnStart = true;

        [Header("Mix-and-Match Clamshell Set Selection (Any Front + Any Back)")]
        public ClamshellSetArchetype headFrontSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype headBackSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype bodyFrontSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype bodyBackSet = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype shoulderFrontSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype shoulderBackSet = ClamshellSetArchetype.Set01_Ironclad;
        [FormerlySerializedAs("armFrontSet")]
        public ClamshellSetArchetype upperArmFrontSet = ClamshellSetArchetype.Set04_Ripjaw;
        [FormerlySerializedAs("armBackSet")]
        public ClamshellSetArchetype upperArmBackSet = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype lowerArmFrontSet = ClamshellSetArchetype.Set04_Ripjaw;
        public ClamshellSetArchetype lowerArmBackSet = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype handFrontSet = ClamshellSetArchetype.Set04_Ripjaw;
        public ClamshellSetArchetype handBackSet = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype thighFrontSet = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype thighBackSet = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype calfFrontSet = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype calfBackSet = ClamshellSetArchetype.Set05_Afterburn;
        public ClamshellSetArchetype footFrontSet = ClamshellSetArchetype.Set04_Ripjaw;
        public ClamshellSetArchetype footBackSet = ClamshellSetArchetype.Set04_Ripjaw;

        [Header("Bone Dimensions (Universal Envelope)")]
        public float thighLength = 1.15f;
        public float calfLength = 1.15f;
        public float jointRadius = 0.20f;

        /// <summary>Height of the hip hubs after the last rebuild (for camera framing).</summary>
        public float HipHeight { get; private set; }
        /// <summary>Height of the neck hub after the last rebuild.</summary>
        public float NeckHeight { get; private set; }
        /// <summary>Height of the shoulder line (humanoid) or spine (quadruped) after the last rebuild.</summary>
        public float ChestHeight { get; private set; }

        private const float JointGap = 0.06f;

        private Transform _visualRoot;
        private Color _front;
        private Color _rear;

        private struct ShellDims
        {
            public readonly float width;
            public readonly float depth;
            public readonly float inner;
            public readonly float bevel;

            public ShellDims(float width, float depth, float inner, float bevel)
            {
                this.width = width;
                this.depth = depth;
                this.inner = inner;
                this.bevel = bevel;
            }
        }

        // Universal envelopes (no weight classes): every set shares these, only the outer details change.
        private static readonly ShellDims ThighDims = new ShellDims(0.36f, 0.16f, 0.075f, 0.15f);
        private static readonly ShellDims CalfDims = new ShellDims(0.34f, 0.16f, 0.075f, 0.15f);
        private static readonly ShellDims TorsoDims = new ShellDims(0.82f, 0.24f, 0.15f, 0.20f);
        private static readonly ShellDims HeadDims = new ShellDims(0.46f, 0.12f, 0.14f, 0.10f);
        private static readonly ShellDims ShoulderDims = new ShellDims(0.34f, 0.14f, 0.07f, 0.10f);
        private static readonly ShellDims UpperArmDims = new ShellDims(0.26f, 0.11f, 0.06f, 0.10f);
        private static readonly ShellDims LowerArmDims = new ShellDims(0.28f, 0.12f, 0.06f, 0.10f);
        private static readonly ShellDims HandDims = new ShellDims(0.24f, 0.09f, 0.05f, 0.07f);

        private void Start()
        {
            if (buildOnStart) RebuildMech();
        }

        // ------------------------------------------------------------------
        // Loadout access
        // ------------------------------------------------------------------

        public ClamshellSetArchetype GetSlot(ClamshellSlot slot)
        {
            switch (slot)
            {
                case ClamshellSlot.ThighFront: return thighFrontSet;
                case ClamshellSlot.ThighBack: return thighBackSet;
                case ClamshellSlot.CalfFront: return calfFrontSet;
                case ClamshellSlot.CalfBack: return calfBackSet;
                case ClamshellSlot.FootFront: return footFrontSet;
                case ClamshellSlot.FootBack: return footBackSet;
                case ClamshellSlot.BodyFront: return bodyFrontSet;
                case ClamshellSlot.BodyBack: return bodyBackSet;
                case ClamshellSlot.ShoulderFront: return shoulderFrontSet;
                case ClamshellSlot.ShoulderBack: return shoulderBackSet;
                case ClamshellSlot.UpperArmFront: return upperArmFrontSet;
                case ClamshellSlot.UpperArmBack: return upperArmBackSet;
                case ClamshellSlot.LowerArmFront: return lowerArmFrontSet;
                case ClamshellSlot.LowerArmBack: return lowerArmBackSet;
                case ClamshellSlot.HandFront: return handFrontSet;
                case ClamshellSlot.HandBack: return handBackSet;
                case ClamshellSlot.HeadFront: return headFrontSet;
                case ClamshellSlot.HeadBack: return headBackSet;
                default: return ClamshellSetArchetype.Set01_Ironclad;
            }
        }

        public void SetSlot(ClamshellSlot slot, ClamshellSetArchetype set)
        {
            switch (slot)
            {
                case ClamshellSlot.ThighFront: thighFrontSet = set; break;
                case ClamshellSlot.ThighBack: thighBackSet = set; break;
                case ClamshellSlot.CalfFront: calfFrontSet = set; break;
                case ClamshellSlot.CalfBack: calfBackSet = set; break;
                case ClamshellSlot.FootFront: footFrontSet = set; break;
                case ClamshellSlot.FootBack: footBackSet = set; break;
                case ClamshellSlot.BodyFront: bodyFrontSet = set; break;
                case ClamshellSlot.BodyBack: bodyBackSet = set; break;
                case ClamshellSlot.ShoulderFront: shoulderFrontSet = set; break;
                case ClamshellSlot.ShoulderBack: shoulderBackSet = set; break;
                case ClamshellSlot.UpperArmFront: upperArmFrontSet = set; break;
                case ClamshellSlot.UpperArmBack: upperArmBackSet = set; break;
                case ClamshellSlot.LowerArmFront: lowerArmFrontSet = set; break;
                case ClamshellSlot.LowerArmBack: lowerArmBackSet = set; break;
                case ClamshellSlot.HandFront: handFrontSet = set; break;
                case ClamshellSlot.HandBack: handBackSet = set; break;
                case ClamshellSlot.HeadFront: headFrontSet = set; break;
                case ClamshellSlot.HeadBack: headBackSet = set; break;
            }
        }

        public void ApplyLoadout(MechLoadout loadout)
        {
            if (loadout == null) return;
            isQuadrupedChassis = loadout.quadruped;
            legPose = loadout.legPose;
            paintScheme = loadout.paintScheme;
            for (int i = 0; i < MechLoadout.SlotCount; i++)
            {
                var slot = (ClamshellSlot)i;
                SetSlot(slot, loadout.Get(slot));
            }
        }

        public MechLoadout CaptureLoadout()
        {
            var loadout = new MechLoadout
            {
                quadruped = isQuadrupedChassis,
                legPose = legPose,
                paintScheme = paintScheme,
            };
            for (int i = 0; i < MechLoadout.SlotCount; i++)
            {
                var slot = (ClamshellSlot)i;
                loadout.Set(slot, GetSlot(slot));
            }
            return loadout;
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        [ContextMenu("Rebuild Clamshell Mech")]
        public void RebuildMech()
        {
            ClearExistingRoots();
            _visualRoot = ClamshellParts.Empty(transform, RootName, Vector3.zero, Quaternion.identity);

            var paint = IronFaunaPalette.GetPaint(paintScheme);
            _front = paint.front;
            _rear = paint.rear;

            GetPoseShape(legPose, out bool kneeForward, out float extension);
            float hipHeight = AnkleHeight + extension * (thighLength + calfLength);
            HipHeight = hipHeight;

            if (!isQuadrupedChassis) BuildHumanoidChassis(kneeForward, hipHeight);
            else BuildQuadrupedChassis(kneeForward, hipHeight);
        }

        /// <summary>
        /// Each pose is a knee direction plus how far the leg is extended (hip-to-ankle distance as a
        /// fraction of thigh + calf). The critical fold stops at 56%: deeper than that, the thigh and
        /// calf details meet at the knee.
        /// </summary>
        public static void GetPoseShape(LegPosePreset pose, out bool kneeForward, out float extension)
        {
            switch (pose)
            {
                case LegPosePreset.Pose2_ChickenBendForward: kneeForward = true; extension = 0.74f; break;
                case LegPosePreset.Pose3_ReverseExtended: kneeForward = false; extension = 0.93f; break;
                case LegPosePreset.Pose4_ChickenBendBackward: kneeForward = false; extension = 0.74f; break;
                case LegPosePreset.Pose5_ReverseCriticalFold: kneeForward = false; extension = 0.56f; break;
                default: kneeForward = true; extension = 0.94f; break;
            }
        }

        /// <summary>Two-bone IK in the leg's Y/Z plane with the hip directly above the ankle.</summary>
        public static Vector3 SolveKnee(Vector3 hip, Vector3 ankle, float thigh, float calf, bool kneeForward)
        {
            float dist = Mathf.Clamp(Vector3.Distance(hip, ankle), Mathf.Abs(thigh - calf) + 0.001f, thigh + calf - 0.001f);
            float cosA = (thigh * thigh + dist * dist - calf * calf) / (2f * thigh * dist);
            float angle = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 down = (ankle - hip).normalized;
            Vector3 swing = Vector3.ProjectOnPlane(kneeForward ? Vector3.forward : Vector3.back, down).normalized;
            return hip + (down * Mathf.Cos(angle) + swing * Mathf.Sin(angle)) * thigh;
        }

        private void ClearExistingRoots()
        {
            // Includes roots saved into scenes by older versions, which a private field would not remember.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != RootName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            _visualRoot = null;
        }

        private void BuildHumanoidChassis(bool kneeForward, float hipHeight)
        {
            const float hipX = 0.48f;
            BuildLegAssembly("Leg_L", new Vector3(-hipX, 0f, 0f), kneeForward, hipHeight);
            BuildLegAssembly("Leg_R", new Vector3(hipX, 0f, 0f), kneeForward, hipHeight);

            // Pelvis bar + waist
            Strut(_visualRoot, "PelvisBar", new Vector3(-hipX, hipHeight, 0f), new Vector3(hipX, hipHeight, 0f), 0.16f);
            float waistY = hipHeight + 0.30f;
            Strut(_visualRoot, "PelvisPost", new Vector3(0f, hipHeight, 0f), new Vector3(0f, waistY, 0f), 0.18f);
            Hub(_visualRoot, "WaistHub", new Vector3(0f, waistY, 0f), jointRadius * 1.1f, true);

            // Spine + torso clamshell (front chest / back reactor plate)
            float neckY = waistY + 1.5f;
            Vector3 waist = new Vector3(0f, waistY, 0f);
            Vector3 neck = new Vector3(0f, neckY, 0f);
            Strut(_visualRoot, "SpineBone", waist, neck, 0.28f);
            ClamshellOnBone(_visualRoot, "Torso", waist, neck, 0.30f, 0.24f, Vector3.forward, TorsoDims,
                bodyFrontSet, bodyBackSet, -0.35f, 0.35f, out _, out _);

            // Neck + head (skull core + face/cranium halves + cyan visor)
            Hub(_visualRoot, "NeckHub", neck, jointRadius * 0.85f, true);
            Vector3 crown = neck + new Vector3(0f, 0.66f, 0f);
            Transform headMount = ClamshellOnBone(_visualRoot, "Head", neck, crown, 0.20f, 0f, Vector3.forward, HeadDims,
                headFrontSet, headBackSet, -0.42f, -0.05f, out float headLen, out _);
            ClamshellParts.Spawn(headMount, "SkullSensorCore", ClamshellMeshFactory.BeveledBox(new Vector3(0.26f, headLen * 0.95f, 0.28f), 0.03f),
                Vector3.zero, Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            float visorZ = HeadDims.inner + HeadDims.depth + 0.02f;
            ClamshellParts.Spawn(headMount, "VisorOptic", ClamshellMeshFactory.BeveledBox(new Vector3(HeadDims.width * 0.62f, headLen * 0.12f, 0.05f), 0.01f),
                new Vector3(0f, headLen * 0.18f, visorZ), Quaternion.identity, IronFaunaPalette.CyanEnergy, true);

            // Shoulders (pauldrons on the clavicle struts) + arms
            float shoulderY = neckY - 0.26f;
            NeckHeight = neckY;
            ChestHeight = shoulderY;
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "_L" : "_R";
                Vector3 clavicleStart = new Vector3(side * 0.30f, shoulderY, 0f);
                Vector3 shoulderHub = new Vector3(side * 1.0f, shoulderY, 0f);
                Strut(_visualRoot, "Clavicle" + suffix, clavicleStart, shoulderHub, 0.14f);
                ClamshellOnBone(_visualRoot, "Shoulder" + suffix, clavicleStart, shoulderHub, 0.15f, 0.22f, Vector3.up, ShoulderDims,
                    shoulderFrontSet, shoulderBackSet, -0.35f, 0.35f, out _, out _);
                BuildArmAssembly("Arm" + suffix, shoulderHub, side);
            }
        }

        private void BuildQuadrupedChassis(bool kneeForward, float hipHeight)
        {
            const float legX = 0.62f;
            const float legZ = 0.85f;
            // Quadruped knees always point away from the body: knees pointing inward cross under the
            // spine in the deeper stances and the front and rear leg armor collide. The stance still sets the height.
            BuildLegAssembly("FrontLeg_L", new Vector3(-legX, 0f, legZ), true, hipHeight);
            BuildLegAssembly("FrontLeg_R", new Vector3(legX, 0f, legZ), true, hipHeight);
            BuildLegAssembly("RearLeg_L", new Vector3(-legX, 0f, -legZ), false, hipHeight);
            BuildLegAssembly("RearLeg_R", new Vector3(legX, 0f, -legZ), false, hipHeight);

            // Hip cross-bars + horizontal spine
            Strut(_visualRoot, "FrontHipBar", new Vector3(-legX, hipHeight, legZ), new Vector3(legX, hipHeight, legZ), 0.16f);
            Strut(_visualRoot, "RearHipBar", new Vector3(-legX, hipHeight, -legZ), new Vector3(legX, hipHeight, -legZ), 0.16f);
            float spineY = hipHeight + 0.15f;
            Strut(_visualRoot, "HorizontalSpine", new Vector3(0f, spineY, -1.05f), new Vector3(0f, spineY, 1.05f), 0.34f);
            ChestHeight = spineY;

            // Thorax (front half) + haunch (back half): split along the spine, seam over the dorsal hub.
            const float bodyHalfLen = 1.0f;
            const float bodyW = 0.78f;
            const float bodyH = 0.62f;
            var bodyMesh = ClamshellMeshFactory.LengthwiseHalf(bodyHalfLen, bodyW, bodyH, 0.22f, 0.55f);
            Quaternion facingFwd = Quaternion.LookRotation(Vector3.up, Vector3.forward); // local Y forward, local Z up
            Quaternion facingBack = Quaternion.LookRotation(Vector3.up, Vector3.back);
            var thorax = ClamshellParts.Spawn(_visualRoot, $"ThoraxFrontHalf_{bodyFrontSet}", bodyMesh, new Vector3(0f, spineY, bodyHalfLen * 0.5f), facingFwd, _front);
            var haunch = ClamshellParts.Spawn(_visualRoot, $"HaunchBackHalf_{bodyBackSet}", bodyMesh, new Vector3(0f, spineY, -bodyHalfLen * 0.5f), facingBack, _rear);
            var bodyFrame = new ClamshellFeatureKit.Frame
            {
                width = bodyW, length = bodyHalfLen, depth = 0.17f, outer = bodyH * 0.5f, sign = 1f,
                bandLo = -0.30f, bandHi = 0.32f, shellColor = _front,
            };
            ClamshellFeatureKit.Build(thorax.transform, bodyFrontSet, true, bodyFrame);
            bodyFrame.shellColor = _rear;
            ClamshellFeatureKit.Build(haunch.transform, bodyBackSet, false, bodyFrame);

            Hub(_visualRoot, "DorsalWeaponHub", new Vector3(0f, spineY + bodyH * 0.5f + 0.04f, 0f), jointRadius, true);

            // Neck hub + animal head: skull (back half) and snout (front half), split along the head.
            Vector3 neck = new Vector3(0f, spineY + 0.18f, 1.12f);
            Hub(_visualRoot, "QuadNeckHub", neck, jointRadius * 0.9f, true);
            NeckHeight = neck.y;

            var skullMesh = ClamshellMeshFactory.LengthwiseHalf(0.24f, 0.42f, 0.34f, 0.08f, 0.6f);
            var snoutMesh = ClamshellMeshFactory.LengthwiseHalf(0.24f, 0.36f, 0.28f, 0.10f, 0.45f);
            var skull = ClamshellParts.Spawn(_visualRoot, $"AnimalHead_BackHalf_{headBackSet}", skullMesh, neck + new Vector3(0f, 0.04f, 0.32f), facingBack, _rear);
            var snout = ClamshellParts.Spawn(_visualRoot, $"AnimalHead_FrontHalf_{headFrontSet}", snoutMesh, neck + new Vector3(0f, 0.02f, 0.56f), facingFwd, _front);
            ClamshellFeatureKit.Build(skull.transform, headBackSet, false, new ClamshellFeatureKit.Frame
            {
                width = 0.42f, length = 0.24f, depth = 0.12f, outer = 0.17f, sign = 1f, bandLo = -0.35f, bandHi = 0.25f, shellColor = _rear,
            });
            ClamshellFeatureKit.Build(snout.transform, headFrontSet, true, new ClamshellFeatureKit.Frame
            {
                width = 0.36f, length = 0.24f, depth = 0.10f, outer = 0.14f, sign = 1f, bandLo = -0.15f, bandHi = 0.35f, shellColor = _front,
            });
            var eye = ClamshellMeshFactory.BeveledBox(new Vector3(0.07f, 0.05f, 0.05f), 0.01f);
            for (int side = -1; side <= 1; side += 2)
            {
                ClamshellParts.Spawn(_visualRoot, "BeastOptic", eye, neck + new Vector3(side * 0.15f, 0.12f, 0.47f), Quaternion.identity, IronFaunaPalette.CyanEnergy, true);
            }
        }

        private void BuildLegAssembly(string legName, Vector3 footGroundPos, bool kneeForward, float hipHeight)
        {
            Transform legRoot = ClamshellParts.Empty(_visualRoot, legName, footGroundPos, Quaternion.identity);

            Vector3 ankle = new Vector3(0f, AnkleHeight, 0f);
            Vector3 hip = new Vector3(0f, hipHeight, 0f);
            Vector3 knee = SolveKnee(hip, ankle, thighLength, calfLength, kneeForward);

            Hub(legRoot, "HipHub", hip, jointRadius, false);
            Hub(legRoot, "KneeHub", knee, jointRadius, true);
            Hub(legRoot, "AnkleHub", ankle, jointRadius * 0.8f, false);

            Strut(legRoot, "ThighBone", knee, hip, 0.14f);
            Strut(legRoot, "CalfBone", ankle, knee, 0.14f);

            float clear = jointRadius + JointGap;
            // Details sit on the half of each plate away from the knee so a deep fold cannot close them together.
            ClamshellOnBone(legRoot, "Thigh", knee, hip, clear, clear, Vector3.forward, ThighDims,
                thighFrontSet, thighBackSet, 0.02f, 0.38f, out _, out _);
            ClamshellOnBone(legRoot, "Calf", ankle, knee, clear, clear, Vector3.forward, CalfDims,
                calfFrontSet, calfBackSet, -0.38f, -0.02f, out _, out _);

            BuildFoot(legRoot);
        }

        private void BuildFoot(Transform legRoot)
        {
            Transform foot = ClamshellParts.Empty(legRoot, "FootBone", Vector3.zero, Quaternion.identity);

            ClamshellParts.Spawn(foot, "SolePad", ClamshellMeshFactory.BeveledBox(new Vector3(0.42f, 0.03f, 1.08f), 0.01f),
                new Vector3(0f, 0.015f, 0.1f), Quaternion.identity, IronFaunaPalette.DarkIron);
            ClamshellParts.Spawn(foot, "BareFootStrut", ClamshellMeshFactory.BeveledBox(new Vector3(0.24f, 0.06f, 1.0f), 0.015f),
                new Vector3(0f, 0.06f, 0.1f), Quaternion.identity, IronFaunaPalette.GunmetalFrame);

            // Toe (front) and heel (back) halves meet under the ankle; both stay clear of the ankle hub.
            Quaternion facingFwd = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Quaternion facingBack = Quaternion.LookRotation(Vector3.up, Vector3.back);

            const float toeLen = 0.42f, toeH = 0.24f, footW = 0.38f;
            var toe = ClamshellParts.Spawn(foot, $"Foot_FrontHalf_{footFrontSet}", ClamshellMeshFactory.LengthwiseHalf(toeLen, footW, toeH, 0.14f, 0.45f),
                new Vector3(0f, 0.09f + toeH * 0.5f, 0.20f + toeLen * 0.5f), facingFwd, _front);
            ClamshellFeatureKit.Build(toe.transform, footFrontSet, true, new ClamshellFeatureKit.Frame
            {
                width = footW, length = toeLen, depth = 0.05f, outer = toeH * 0.5f, sign = 1f, bandLo = -0.38f, bandHi = 0.1f, shellColor = _front,
            });

            const float heelLen = 0.22f, heelH = 0.15f;
            var heel = ClamshellParts.Spawn(foot, $"Foot_BackHalf_{footBackSet}", ClamshellMeshFactory.LengthwiseHalf(heelLen, footW * 0.92f, heelH, 0.08f, 0.6f),
                new Vector3(0f, 0.09f + heelH * 0.5f, -0.20f - heelLen * 0.5f), facingBack, _rear);
            ClamshellFeatureKit.Build(heel.transform, footBackSet, false, new ClamshellFeatureKit.Frame
            {
                width = footW * 0.92f, length = heelLen, depth = 0.04f, outer = heelH * 0.5f, sign = 1f, bandLo = -0.4f, bandHi = 0.2f, shellColor = _rear,
            });

            var latch = ClamshellMeshFactory.BeveledBox(new Vector3(0.04f, 0.06f, 0.12f), 0.01f);
            ClamshellParts.Spawn(foot, "ToggleLatch_L", latch, new Vector3(-0.2f, 0.09f, 0f), Quaternion.identity, IronFaunaPalette.DarkIron);
            ClamshellParts.Spawn(foot, "ToggleLatch_R", latch, new Vector3(0.2f, 0.09f, 0f), Quaternion.identity, IronFaunaPalette.DarkIron);
        }

        private void BuildArmAssembly(string armName, Vector3 shoulderPos, int side)
        {
            Transform armRoot = ClamshellParts.Empty(_visualRoot, armName, shoulderPos, Quaternion.identity);

            Vector3 shoulder = Vector3.zero;
            Vector3 elbow = new Vector3(side * 0.06f, -0.70f, 0.06f);
            Vector3 wrist = elbow + new Vector3(side * 0.02f, -0.64f, 0.22f);
            Vector3 tip = wrist + new Vector3(0f, -0.34f, 0.08f);

            Hub(armRoot, "ShoulderHub", shoulder, 0.17f, true);
            Hub(armRoot, "ElbowHub", elbow, 0.15f, true);
            Hub(armRoot, "WristHub", wrist, 0.12f, true);

            Strut(armRoot, "UpperArmBone", elbow, shoulder, 0.12f);
            Strut(armRoot, "LowerArmBone", wrist, elbow, 0.11f);
            Strut(armRoot, "HandBone", wrist, tip, 0.10f);

            ClamshellOnBone(armRoot, "UpperArm", elbow, shoulder, 0.19f, 0.21f, Vector3.forward, UpperArmDims,
                upperArmFrontSet, upperArmBackSet, -0.35f, 0.35f, out _, out _);
            ClamshellOnBone(armRoot, "LowerArm", wrist, elbow, 0.16f, 0.19f, Vector3.forward, LowerArmDims,
                lowerArmFrontSet, lowerArmBackSet, -0.35f, 0.35f, out _, out _);
            ClamshellOnBone(armRoot, "Hand", wrist, tip, 0.16f, 0f, Vector3.forward, HandDims,
                handFrontSet, handBackSet, -0.3f, 0.3f, out _, out _);
        }

        /// <summary>
        /// Clamps a front + back clamshell pair onto the bone from <paramref name="from"/> to <paramref name="to"/>,
        /// leaving the given clearances at each end for the joint hubs. Returns the mount transform
        /// (local +Y along the bone, local +Z toward <paramref name="outwardHint"/>).
        /// </summary>
        private Transform ClamshellOnBone(Transform parent, string label, Vector3 from, Vector3 to, float clearFrom, float clearTo,
            Vector3 outwardHint, ShellDims dims, ClamshellSetArchetype frontSet, ClamshellSetArchetype backSet,
            float bandLo, float bandHi, out float shellLength, out Vector3 center)
        {
            Vector3 delta = to - from;
            float len = delta.magnitude;
            Vector3 dir = len > 0.0001f ? delta / len : Vector3.up;
            shellLength = Mathf.Max(0.12f, len - clearFrom - clearTo);
            center = from + dir * (clearFrom + shellLength * 0.5f);

            Vector3 outward = Vector3.ProjectOnPlane(outwardHint, dir);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Vector3.up, dir);
            Quaternion rot = Quaternion.LookRotation(outward.normalized, dir);

            Transform mount = ClamshellParts.Empty(parent, label + "_Clamshell", center, rot);
            BuildHalf(mount, label, true, frontSet, shellLength, dims, bandLo, bandHi);
            BuildHalf(mount, label, false, backSet, shellLength, dims, bandLo, bandHi);

            // Toggle latches straddling the parting seam on both sides.
            var latch = ClamshellMeshFactory.BeveledBox(new Vector3(0.04f, shellLength * 0.14f, dims.inner * 2f + 0.06f), 0.008f);
            float lx = dims.width * 0.5f + 0.02f;
            for (int i = -1; i <= 1; i += 2)
            {
                float y = i * shellLength * 0.22f;
                ClamshellParts.Spawn(mount, $"{label}_ToggleLatch_L", latch, new Vector3(-lx, y, 0f), Quaternion.identity, IronFaunaPalette.DarkIron);
                ClamshellParts.Spawn(mount, $"{label}_ToggleLatch_R", latch, new Vector3(lx, y, 0f), Quaternion.identity, IronFaunaPalette.DarkIron);
            }
            return mount;
        }

        private void BuildHalf(Transform mount, string label, bool front, ClamshellSetArchetype set, float shellLength,
            ShellDims dims, float bandLo, float bandHi)
        {
            float sign = front ? 1f : -1f;
            Color color = front ? _front : _rear;
            var mesh = ClamshellMeshFactory.ShellHalf(shellLength, dims.width, dims.depth, dims.inner, dims.bevel, sign);
            string halfName = front ? $"{label}_FrontHalf_{set}" : $"{label}_BackHalf_{set}";
            var half = ClamshellParts.Spawn(mount, halfName, mesh, Vector3.zero, Quaternion.identity, color);
            ClamshellFeatureKit.Build(half.transform, set, front, new ClamshellFeatureKit.Frame
            {
                width = dims.width,
                length = shellLength,
                depth = dims.depth,
                outer = dims.inner + dims.depth,
                sign = sign,
                bandLo = bandLo,
                bandHi = bandHi,
                shellColor = color,
            });
        }

        private static void Strut(Transform parent, string name, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 0.0001f) return;
            var mesh = ClamshellMeshFactory.BeveledBox(new Vector3(thickness, len, thickness), thickness * 0.2f);
            ClamshellParts.Spawn(parent, name + "_Strut", mesh, (from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, delta / len), IronFaunaPalette.GunmetalFrame);
        }

        private static void Hub(Transform parent, string name, Vector3 localPos, float radius, bool glowingCore)
        {
            Quaternion alongX = Quaternion.Euler(0f, 0f, 90f); // rotary drum axis along X (the leg/arm hinge axis)
            ClamshellParts.Spawn(parent, name, ClamshellMeshFactory.Prism(radius, 0.16f, 16), localPos, alongX, IronFaunaPalette.GunmetalFrame);
            if (glowingCore)
            {
                ClamshellParts.Spawn(parent, name + "_OrangeCore", ClamshellMeshFactory.Prism(radius * 0.62f, 0.18f, 16), localPos, alongX, IronFaunaPalette.OrangeJointCore, true);
            }
        }

        /// <summary>Kept for existing callers (arena setup, grid defenses).</summary>
        public static void ApplyPaletteMaterial(GameObject go, Color baseColor, bool emissive)
        {
            IronFaunaMaterials.Apply(go, baseColor, emissive);
        }
    }
}
