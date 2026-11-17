using System.Collections.Generic;
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
        public const float AnkleHeight = 0.26f;

        [Header("Chassis & Stance Configuration")]
        public bool isQuadrupedChassis = false;
        public LegPosePreset legPose = LegPosePreset.Pose1_HumanLeg;
        [Tooltip("Index into IronFaunaPalette.PaintSchemes")]
        public int paintScheme = 0;
        [Tooltip("Rebuild automatically when the scene starts. The garage turns this off and rebuilds on demand.")]
        public bool buildOnStart = true;

        [Header("Weight Classes (skeleton + armor envelope)")]
        [Tooltip("Torso, arms and hardpoint sizes.")]
        public MechWeightClass torsoWeight = MechWeightClass.Medium;
        [Tooltip("Thighs, calves, feet and leg joints.")]
        public MechWeightClass legWeight = MechWeightClass.Medium;
        [Tooltip("Off = bare skeleton (garage skeleton view, or all armor knocked off).")]
        public bool showArmor = true;
        [Tooltip("Presentation stance for the title screen and garage: feet planted wide, arms raised, head turned. Leave off for gameplay, where the motion controller poses the mech.")]
        public bool showcasePose;

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
        public float jointRadius = 0.15f;
        [Tooltip("Half-width where the clavicle struts leave the spine (humanoid).")]
        public float collarWidth = 0.36f;

        // ---- Animation pivots, refreshed on every rebuild (driven by MechRobotOperator) ----
        /// <summary>Root of the generated mech (child of this transform).</summary>
        public Transform visualRoot => _visualRoot;
        /// <summary>Upper body pivot: torso, arms, head (humanoid) or body shell and head (quadruped).</summary>
        [System.NonSerialized] public Transform bodyRoot;
        /// <summary>Head yaw pivot at the neck hub.</summary>
        [System.NonSerialized] public Transform headRoot;
        /// <summary>Arm roots at the shoulder hubs (humanoid only).</summary>
        [System.NonSerialized] public Transform leftArmRoot;
        [System.NonSerialized] public Transform rightArmRoot;
        /// <summary>Leg swing pivots at hip height, left (-X) and right (+X) side.</summary>
        [System.NonSerialized] public List<Transform> leftSideLegs = new List<Transform>();
        [System.NonSerialized] public List<Transform> rightSideLegs = new List<Transform>();
        /// <summary>Projectile spawn point (right hand, or the dorsal hub on a quadruped).</summary>
        [System.NonSerialized] public Transform weaponMuzzle;
        /// <summary>Weapon sockets on the skeleton, refreshed on every rebuild.</summary>
        [System.NonSerialized] public List<MechHardpoint> hardpoints = new List<MechHardpoint>();
        /// <summary>Hip height of the current stance (same as <see cref="HipHeight"/>).</summary>
        public float baseHipHeight => HipHeight;

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
        private MechFrameSpec _torso = MechFrameSpec.Get(MechWeightClass.Medium);
        private MechFrameSpec _leg = MechFrameSpec.Get(MechWeightClass.Medium);

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

            /// <summary>This envelope in another weight class.</summary>
            public ShellDims For(MechFrameSpec spec)
            {
                return new ShellDims(width * spec.armorWidth, depth * spec.armorDepth, inner * spec.seamOffset, bevel * spec.armorDepth);
            }
        }

        // Medium-class envelopes. Every armor set shares them (only the outer details change) and
        // ShellDims.For() resizes them for Light and Heavy frames. Head and hands are universal.
        private static readonly ShellDims ThighDims = new ShellDims(0.46f, 0.22f, 0.09f, 0.187f);
        private static readonly ShellDims CalfDims = new ShellDims(0.42f, 0.22f, 0.09f, 0.187f);
        private static readonly ShellDims TorsoDims = new ShellDims(1.00f, 0.30f, 0.17f, 0.24f);
        private static readonly ShellDims HeadDims = new ShellDims(0.56f, 0.15f, 0.15f, 0.12f);
        private static readonly ShellDims ShoulderDims = new ShellDims(0.44f, 0.18f, 0.08f, 0.12f);
        private static readonly ShellDims UpperArmDims = new ShellDims(0.34f, 0.15f, 0.07f, 0.12f);
        private static readonly ShellDims LowerArmDims = new ShellDims(0.36f, 0.16f, 0.07f, 0.12f);
        private static readonly ShellDims HandDims = new ShellDims(0.30f, 0.12f, 0.06f, 0.08f);

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
            torsoWeight = loadout.torsoWeight;
            legWeight = loadout.legWeight;
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
                torsoWeight = torsoWeight,
                legWeight = legWeight,
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
            _torso = MechFrameSpec.Get(torsoWeight);
            _leg = MechFrameSpec.Get(legWeight);
            hardpoints.Clear();

            GetPoseShape(legPose, out bool kneeForward, out float extension);
            float hipHeight = AnkleHeight + extension * (thighLength + calfLength);
            HipHeight = hipHeight;

            if (!isQuadrupedChassis) BuildHumanoidChassis(kneeForward, hipHeight);
            else BuildQuadrupedChassis(kneeForward, hipHeight);
            BuildAnimationPivots(hipHeight);
        }

        /// <summary>Switches the stance and rebuilds the mech.</summary>
        public void SetLegPose(LegPosePreset pose)
        {
            legPose = pose;
            RebuildMech();
        }

        /// <summary>
        /// Groups the built parts under pivots the motion code can move: one swing pivot per leg at hip height,
        /// an upper-body pivot (the height MechRobotOperator expects), a head pivot at the neck, and the arm roots.
        /// </summary>
        private void BuildAnimationPivots(float hipHeight)
        {
            leftSideLegs.Clear();
            rightSideLegs.Clear();
            var children = new List<Transform>();
            foreach (Transform child in _visualRoot) children.Add(child);

            float bodyY = isQuadrupedChassis ? hipHeight + 0.15f : hipHeight + 0.82f;
            bodyRoot = ClamshellParts.Empty(_visualRoot, "BodyPivot", new Vector3(0f, bodyY, 0f), Quaternion.identity);
            headRoot = null;
            Vector3 neckPos = Vector3.zero;
            foreach (var child in children)
            {
                string n = child.name;
                if (n.StartsWith("Leg_") || n.StartsWith("FrontLeg_") || n.StartsWith("RearLeg_"))
                {
                    Vector3 p = child.localPosition;
                    var pivot = ClamshellParts.Empty(_visualRoot, n + "_SwingPivot", new Vector3(p.x, hipHeight, p.z), Quaternion.identity);
                    child.SetParent(pivot, true);
                    (p.x < 0f ? leftSideLegs : rightSideLegs).Add(pivot);
                    continue;
                }
                if (n == "NeckHub" || n == "QuadNeckHub") neckPos = child.localPosition;
                child.SetParent(bodyRoot, true);
            }

            headRoot = ClamshellParts.Empty(bodyRoot, "HeadPivot", bodyRoot.InverseTransformPoint(_visualRoot.TransformPoint(neckPos)), Quaternion.identity);
            foreach (var child in children)
            {
                string n = child.name;
                if (n.StartsWith("Head") || n.StartsWith("NeckHub") || n.StartsWith("QuadNeckHub") || n.StartsWith("AnimalHead") || n.StartsWith("BeastOptic"))
                {
                    child.SetParent(headRoot, true);
                }
            }

            leftArmRoot = bodyRoot.Find("Arm_L");
            rightArmRoot = bodyRoot.Find("Arm_R");
            weaponMuzzle = rightArmRoot != null ? rightArmRoot.Find("WeaponMuzzle") : bodyRoot.Find("WeaponMuzzle");
            if (showcasePose) ApplyShowcasePose();
        }

        /// <summary>
        /// Presentation stance: legs splayed with the toes out and one foot leading (soles stay flat on the deck),
        /// torso turned a little, head looking back toward the viewer. The arms are posed in BuildArmAssembly.
        /// </summary>
        private void ApplyShowcasePose()
        {
            float lift = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                var legs = pass == 0 ? leftSideLegs : rightSideLegs;
                float side = pass == 0 ? -1f : 1f;
                foreach (var pivot in legs)
                {
                    if (pivot == null || pivot.childCount == 0) continue;
                    float roll = side * 5f;                                   // feet wider than the hips
                    float yawOut = isQuadrupedChassis ? 0f : side * 9f;       // toes out
                    float pitch = isQuadrupedChassis ? 0f : side * 3f;        // left foot leads, right foot trails
                    pivot.localRotation = Quaternion.Euler(pitch, yawOut, roll);

                    Transform legRoot = pivot.GetChild(0);
                    Transform foot = legRoot.Find("FootBone");
                    if (foot == null) continue;
                    Vector3 ankle = _visualRoot.InverseTransformPoint(legRoot.TransformPoint(new Vector3(0f, AnkleHeight, 0f)));
                    foot.rotation = _visualRoot.rotation * Quaternion.Euler(0f, yawOut, 0f);
                    foot.position = _visualRoot.TransformPoint(ankle - new Vector3(0f, AnkleHeight, 0f));
                    lift = Mathf.Max(lift, ankle.y - AnkleHeight);
                }
            }
            _visualRoot.localPosition = new Vector3(0f, -lift, 0f);
            if (bodyRoot != null) bodyRoot.localRotation = Quaternion.Euler(2f, isQuadrupedChassis ? 0f : 5f, 0f);
            if (headRoot != null) headRoot.localRotation = Quaternion.Euler(isQuadrupedChassis ? 6f : 4f, isQuadrupedChassis ? 10f : -14f, 0f);
        }

        /// <summary>
        /// Each pose is a knee direction plus how far the leg is extended (hip-to-ankle distance as a
        /// fraction of thigh + calf). Verified with a side-view collision check: the critical fold stops at
        /// 70% (checked against the hand-modeled Ironclad parts too) and the forward chicken bend at 80%; deeper than that, plates meet at the knee or the toe.
        /// </summary>
        public static void GetPoseShape(LegPosePreset pose, out bool kneeForward, out float extension)
        {
            switch (pose)
            {
                case LegPosePreset.Pose2_ChickenBendForward: kneeForward = true; extension = 0.80f; break;
                case LegPosePreset.Pose3_ReverseExtended: kneeForward = false; extension = 0.93f; break;
                case LegPosePreset.Pose4_ChickenBendBackward: kneeForward = false; extension = 0.74f; break;
                case LegPosePreset.Pose5_ReverseCriticalFold: kneeForward = false; extension = 0.70f; break;
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
            float hipX = _torso.hipX;
            BuildLegAssembly("Leg_L", new Vector3(-hipX, 0f, 0f), kneeForward, hipHeight);
            BuildLegAssembly("Leg_R", new Vector3(hipX, 0f, 0f), kneeForward, hipHeight);

            float waistY = hipHeight + 0.30f;
            float neckY = waistY + 1.5f;
            Vector3 waist = new Vector3(0f, waistY, 0f);
            Vector3 neck = new Vector3(0f, neckY, 0f);

            // Torso skeleton: hip track, multi-segment spine, rib cage + reactor, shoulder yoke, neck collar
            // and the back weapon sockets. Falls back to plain struts if the model is missing.
            bool torsoSkeleton = Skel(_visualRoot, "TorsoSkeleton", $"Skel_TorsoHumanoid_{torsoWeight}", waist, Quaternion.identity, Vector3.one) != null;
            if (torsoSkeleton)
            {
                MechFrameSpec.HardpointSizes(torsoWeight, out var sideSize, out var mainSize, out var auxSize);
                AddHardpoint("BackUpperL", waist + new Vector3(-0.31f, 1.51f, -0.38f), sideSize, Vector3.up);
                AddHardpoint("BackUpperR", waist + new Vector3(0.31f, 1.51f, -0.38f), sideSize, Vector3.up);
                AddHardpoint("BackCentre", waist + new Vector3(0f, 1.52f, -0.44f), mainSize, Vector3.up);
                AddHardpoint("LowerSpine", waist + new Vector3(0f, 0.12f, -0.39f), auxSize, Vector3.back);
            }
            else
            {
                Strut(_visualRoot, "PelvisBar", new Vector3(-hipX, hipHeight, 0f), new Vector3(hipX, hipHeight, 0f), 0.16f);
                Strut(_visualRoot, "PelvisPost", new Vector3(0f, hipHeight, 0f), new Vector3(0f, waistY, 0f), 0.18f);
                Strut(_visualRoot, "SpineBone", waist, neck, 0.32f);
            }
            Hub(_visualRoot, "WaistHub", waist, jointRadius * 1.1f * _torso.jointRadius, true);

            // Torso clamshell (front chest / back reactor plate)
            ClamshellOnBone(_visualRoot, "Torso", waist, neck, 0.24f, 0.20f, Vector3.forward, TorsoDims.For(_torso),
                bodyFrontSet, bodyBackSet, -0.35f, 0.35f, out _, out _);

            // Neck + head (skull core + face/cranium halves + cyan visor)
            Hub(_visualRoot, "NeckHub", neck, jointRadius * 0.85f, false);
            Vector3 crown = neck + new Vector3(0f, 0.72f, 0f);
            Transform headMount = ClamshellOnBone(_visualRoot, "Head", neck, crown, 0.18f, 0f, Vector3.forward, HeadDims,
                headFrontSet, headBackSet, -0.42f, -0.05f, out float headLen, out _);
            if (Skel(_visualRoot, "HeadSkeleton", "Skel_Head", neck, Quaternion.identity, Vector3.one) == null)
            {
                ClamshellParts.Spawn(headMount, "SkullSensorCore", ClamshellMeshFactory.BeveledBox(new Vector3(0.30f, headLen * 0.95f, 0.30f), 0.03f),
                    Vector3.zero, Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            }
            if (showArmor)
            {
                float visorZ = HeadDims.inner + HeadDims.depth + 0.02f;
                ClamshellParts.Spawn(headMount, "VisorOptic", ClamshellMeshFactory.BeveledBox(new Vector3(HeadDims.width * 0.6f, headLen * 0.03f, 0.04f), 0.004f),
                    new Vector3(0f, headLen * 0.18f, visorZ), Quaternion.identity, IronFaunaPalette.CyanEnergy, true);
            }

            // Shoulders (pauldrons on the clavicle struts) + arms
            float shoulderY = neckY - 0.30f;
            NeckHeight = neckY;
            ChestHeight = shoulderY;
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "_L" : "_R";
                Vector3 clavicleStart = new Vector3(side * collarWidth * (_torso.collar / 0.36f), shoulderY, 0f);
                Vector3 shoulderHub = new Vector3(side * _torso.shoulderX, shoulderY, 0f);
                if (!torsoSkeleton) Strut(_visualRoot, "Clavicle" + suffix, clavicleStart, shoulderHub, 0.16f);
                ClamshellOnBone(_visualRoot, "Shoulder" + suffix, clavicleStart, shoulderHub, 0.20f, 0.21f, Vector3.up, ShoulderDims.For(_torso),
                    shoulderFrontSet, shoulderBackSet, -0.35f, 0.35f, out _, out _);
                BuildArmAssembly("Arm" + suffix, shoulderHub, side);
            }
        }

        private void BuildQuadrupedChassis(bool kneeForward, float hipHeight)
        {
            float legX = _torso.legX;
            const float legZ = 0.85f;
            // Quadruped knees always point away from the body: knees pointing inward cross under the
            // spine in the deeper stances and the front and rear leg armor collide. The stance still sets the height.
            BuildLegAssembly("FrontLeg_L", new Vector3(-legX, 0f, legZ), true, hipHeight);
            BuildLegAssembly("FrontLeg_R", new Vector3(legX, 0f, legZ), true, hipHeight);
            BuildLegAssembly("RearLeg_L", new Vector3(-legX, 0f, -legZ), false, hipHeight);
            BuildLegAssembly("RearLeg_R", new Vector3(legX, 0f, -legZ), false, hipHeight);

            float spineY = hipHeight + 0.15f;
            ChestHeight = spineY;
            const float bodyHalfLen = 1.0f;
            float bodyW = 0.90f * _torso.armorWidth;
            const float bodyH = 0.70f;

            // Animal torso skeleton: arched spine with vertebrae and cable tracks, ribs, shoulder and hip girdles,
            // and a bulkhead ring at the armor seam that carries the dorsal and flank weapon sockets.
            Vector3 hipLine = new Vector3(0f, hipHeight, 0f);
            bool torsoSkeleton = Skel(_visualRoot, "TorsoSkeleton", $"Skel_TorsoAnimal_{torsoWeight}", hipLine, Quaternion.identity, Vector3.one) != null;
            if (torsoSkeleton)
            {
                MechFrameSpec.HardpointSizes(torsoWeight, out var sideSize, out var mainSize, out _);
                float top = 0.15f + bodyH * 0.5f + 0.02f;
                AddHardpoint("Dorsal", hipLine + new Vector3(0f, top + 0.135f, 0f), mainSize, Vector3.up);
                AddHardpoint("FlankL", hipLine + new Vector3(-(bodyW * 0.5f + 0.115f), 0.15f, 0f), sideSize, Vector3.left);
                AddHardpoint("FlankR", hipLine + new Vector3(bodyW * 0.5f + 0.115f, 0.15f, 0f), sideSize, Vector3.right);
                AddHardpoint("Tail", hipLine + new Vector3(0f, 0.21f, -1.165f), HardpointSize.Small, Vector3.back);
            }
            else
            {
                Strut(_visualRoot, "FrontHipBar", new Vector3(-legX, hipHeight, legZ), new Vector3(legX, hipHeight, legZ), 0.16f);
                Strut(_visualRoot, "RearHipBar", new Vector3(-legX, hipHeight, -legZ), new Vector3(legX, hipHeight, -legZ), 0.16f);
                Strut(_visualRoot, "HorizontalSpine", new Vector3(0f, spineY, -1.05f), new Vector3(0f, spineY, 1.05f), 0.36f);
                Hub(_visualRoot, "DorsalWeaponHub", new Vector3(0f, spineY + bodyH * 0.5f + 0.04f, 0f), jointRadius, true);
            }
            ClamshellParts.Empty(_visualRoot, "WeaponMuzzle", new Vector3(0f, spineY + bodyH * 0.5f + 0.2f, 0.4f), Quaternion.identity);

            // Thorax (front half) + haunch (back half): split along the spine, seam over the dorsal hub.
            var bodyMesh = ClamshellMeshFactory.LengthwiseHalf(bodyHalfLen, bodyW, bodyH, 0.22f, 0.55f);
            Quaternion facingFwd = Quaternion.LookRotation(Vector3.up, Vector3.forward); // local Y forward, local Z up
            Quaternion facingBack = Quaternion.LookRotation(Vector3.up, Vector3.back);
            var bodySize = new Vector3(bodyW, bodyHalfLen, bodyH);
            Vector3 thoraxPos = new Vector3(0f, spineY, bodyHalfLen * 0.5f);
            Vector3 haunchPos = new Vector3(0f, spineY, -bodyHalfLen * 0.5f);
            var bodyFrame = new ClamshellFeatureKit.Frame
            {
                width = bodyW, length = bodyHalfLen, depth = 0.17f, outer = bodyH * 0.5f, sign = 1f,
                bandLo = -0.30f, bandHi = 0.32f, shellColor = _front,
            };
            if (showArmor && TrySpawnLengthwise(_visualRoot, $"ThoraxFrontHalf_{bodyFrontSet}", $"QuadBodyFront_{bodyFrontSet}", bodySize, thoraxPos, facingFwd, _front, _torso.armorWidth) == null)
            {
                var thorax = ClamshellParts.Spawn(_visualRoot, $"ThoraxFrontHalf_{bodyFrontSet}", bodyMesh, thoraxPos, facingFwd, _front);
                ClamshellFeatureKit.Build(thorax.transform, bodyFrontSet, true, bodyFrame);
            }
            if (showArmor && TrySpawnLengthwise(_visualRoot, $"HaunchBackHalf_{bodyBackSet}", $"QuadBodyBack_{bodyBackSet}", bodySize, haunchPos, facingBack, _rear, _torso.armorWidth) == null)
            {
                var haunch = ClamshellParts.Spawn(_visualRoot, $"HaunchBackHalf_{bodyBackSet}", bodyMesh, haunchPos, facingBack, _rear);
                bodyFrame.shellColor = _rear;
                ClamshellFeatureKit.Build(haunch.transform, bodyBackSet, false, bodyFrame);
            }

            // Neck hub + animal head: skull (back half) and snout (front half), split along the head.
            Vector3 neck = new Vector3(0f, spineY + 0.18f, 1.12f);
            Hub(_visualRoot, "QuadNeckHub", neck, jointRadius * 0.9f, false);
            NeckHeight = neck.y;
            Skel(_visualRoot, "AnimalHeadSkeleton", "Skel_HeadAnimal", neck, Quaternion.identity, Vector3.one);
            if (showArmor) BuildAnimalHeadShells(neck, facingFwd, facingBack);
        }

        private void BuildAnimalHeadShells(Vector3 neck, Quaternion facingFwd, Quaternion facingBack)
        {
            var skullMesh = ClamshellMeshFactory.LengthwiseHalf(0.24f, 0.42f, 0.34f, 0.08f, 0.6f);
            var snoutMesh = ClamshellMeshFactory.LengthwiseHalf(0.24f, 0.36f, 0.28f, 0.10f, 0.45f);
            Vector3 skullPos = neck + new Vector3(0f, 0.04f, 0.32f);
            Vector3 snoutPos = neck + new Vector3(0f, 0.02f, 0.56f);
            if (TrySpawnLengthwise(_visualRoot, $"AnimalHead_BackHalf_{headBackSet}", $"AnimalHeadBack_{headBackSet}",
                    new Vector3(0.42f, 0.24f, 0.34f), skullPos, facingBack, _rear) == null)
            {
                var skull = ClamshellParts.Spawn(_visualRoot, $"AnimalHead_BackHalf_{headBackSet}", skullMesh, skullPos, facingBack, _rear);
                ClamshellFeatureKit.Build(skull.transform, headBackSet, false, new ClamshellFeatureKit.Frame
                {
                    width = 0.42f, length = 0.24f, depth = 0.12f, outer = 0.17f, sign = 1f, bandLo = -0.35f, bandHi = 0.25f, shellColor = _rear,
                });
            }
            // Hand-modeled snouts carry their own eyes.
            if (TrySpawnLengthwise(_visualRoot, $"AnimalHead_FrontHalf_{headFrontSet}", $"AnimalHeadFront_{headFrontSet}",
                    new Vector3(0.36f, 0.24f, 0.28f), snoutPos, facingFwd, _front) != null)
            {
                return;
            }
            var snout = ClamshellParts.Spawn(_visualRoot, $"AnimalHead_FrontHalf_{headFrontSet}", snoutMesh, snoutPos, facingFwd, _front);
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

            float r = jointRadius * _leg.jointRadius;
            Hub(legRoot, "HipHub", hip, r, false);
            Hub(legRoot, "KneeHub", knee, r, true);
            Hub(legRoot, "AnkleHub", ankle, r * 0.8f, false);

            Bone(legRoot, "ThighBone", $"Skel_Thigh_{legWeight}", knee, hip, 1.15f, 0.18f);
            Bone(legRoot, "CalfBone", $"Skel_Calf_{legWeight}", ankle, knee, 1.15f, 0.18f);

            JointGuard(legRoot, "KneeGuard", knee, hip, ankle, kneeForward ? Vector3.forward : Vector3.back, r / 0.15f);

            float clear = r + JointGap;
            // Details sit on the half of each plate away from the knee so a deep fold cannot close them together.
            ClamshellOnBone(legRoot, "Thigh", knee, hip, clear, clear, Vector3.forward, ThighDims.For(_leg),
                thighFrontSet, thighBackSet, 0.02f, 0.38f, out _, out _);
            ClamshellOnBone(legRoot, "Calf", ankle, knee, clear, clear, Vector3.forward, CalfDims.For(_leg),
                calfFrontSet, calfBackSet, -0.22f, 0.10f, out _, out _);

            BuildFoot(legRoot);
        }

        private void BuildFoot(Transform legRoot)
        {
            Transform foot = ClamshellParts.Empty(legRoot, "FootBone", Vector3.zero, Quaternion.identity);

            if (Skel(foot, "FootSkeleton", $"Skel_Foot_{legWeight}", Vector3.zero, Quaternion.identity, new Vector3(_leg.armorWidth, 1f, 1f)) == null)
            {
                ClamshellParts.Spawn(foot, "SolePad", ClamshellMeshFactory.BeveledBox(new Vector3(0.54f, 0.03f, 1.16f), 0.01f),
                    new Vector3(0f, 0.015f, 0.12f), Quaternion.identity, IronFaunaPalette.DarkIron);
                ClamshellParts.Spawn(foot, "BareFootStrut", ClamshellMeshFactory.BeveledBox(new Vector3(0.26f, 0.04f, 1.08f), 0.012f),
                    new Vector3(0f, 0.04f, 0.12f), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            }
            if (!showArmor) return;

            // Toe (front) and heel (back) halves meet under the ankle hub. Both are beveled on the ankle side
            // too, so the calf can fold over them (verified for all 5 stances).
            Quaternion facingFwd = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Quaternion facingBack = Quaternion.LookRotation(Vector3.up, Vector3.back);
            const float baseY = 0.06f;

            const float toeLen = 0.48f, toeH = 0.22f;
            float footW = 0.50f * _leg.armorWidth;
            Vector3 toePos = new Vector3(0f, baseY + toeH * 0.5f, 0.20f + toeLen * 0.5f);
            if (TrySpawnLengthwise(foot, $"Foot_FrontHalf_{footFrontSet}", $"FootFront_{footFrontSet}",
                    new Vector3(footW, toeLen, toeH), toePos, facingFwd, _front, _leg.armorWidth) == null)
            {
                var toe = ClamshellParts.Spawn(foot, $"Foot_FrontHalf_{footFrontSet}",
                    ClamshellMeshFactory.LengthwiseHalf(toeLen, footW, toeH, 0.16f, 0.45f, 0.12f, 0.55f), toePos, facingFwd, _front);
                ClamshellFeatureKit.Build(toe.transform, footFrontSet, true, new ClamshellFeatureKit.Frame
                {
                    width = footW, length = toeLen, depth = 0.05f, outer = toeH * 0.5f, sign = 1f, bandLo = -0.15f, bandHi = 0.18f, shellColor = _front,
                });
            }

            const float heelLen = 0.28f, heelH = 0.16f;
            Vector3 heelPos = new Vector3(0f, baseY + heelH * 0.5f, -0.16f - heelLen * 0.5f);
            if (TrySpawnLengthwise(foot, $"Foot_BackHalf_{footBackSet}", $"FootBack_{footBackSet}",
                    new Vector3(footW * 0.92f, heelLen, heelH), heelPos, facingBack, _rear, _leg.armorWidth) == null)
            {
                var heel = ClamshellParts.Spawn(foot, $"Foot_BackHalf_{footBackSet}",
                    ClamshellMeshFactory.LengthwiseHalf(heelLen, footW * 0.92f, heelH, 0.10f, 0.6f, 0.06f, 0.8f), heelPos, facingBack, _rear);
                ClamshellFeatureKit.Build(heel.transform, footBackSet, false, new ClamshellFeatureKit.Frame
                {
                    width = footW * 0.92f, length = heelLen, depth = 0.04f, outer = heelH * 0.5f, sign = 1f, bandLo = -0.3f, bandHi = 0.2f, shellColor = _rear,
                });
            }

            var latch = ClamshellMeshFactory.BeveledBox(new Vector3(0.05f, 0.07f, 0.14f), 0.012f);
            ClamshellParts.Spawn(foot, "ToggleLatch_L", latch, new Vector3(-footW * 0.52f, 0.09f, 0.02f), Quaternion.identity, IronFaunaPalette.DarkIron);
            ClamshellParts.Spawn(foot, "ToggleLatch_R", latch, new Vector3(footW * 0.52f, 0.09f, 0.02f), Quaternion.identity, IronFaunaPalette.DarkIron);
        }

        private void BuildArmAssembly(string armName, Vector3 shoulderPos, int side)
        {
            Transform armRoot = ClamshellParts.Empty(_visualRoot, armName, shoulderPos, Quaternion.identity);

            Vector3 shoulder = Vector3.zero;
            Vector3 elbow = new Vector3(side * 0.06f, -0.70f, 0.06f);
            Vector3 wrist = elbow + new Vector3(side * 0.02f, -0.64f, 0.22f);
            Vector3 tip = wrist + new Vector3(0f, -0.34f, 0.08f);
            if (showcasePose)
            {
                // Weapons-ready: elbows out and back, forearms forward; the right arm leads. Bone lengths unchanged.
                bool lead = side > 0;
                float upper = elbow.magnitude, lower = (wrist - elbow).magnitude, hand = (tip - wrist).magnitude;
                elbow = new Vector3(side * 0.16f, -0.66f, lead ? -0.10f : -0.04f).normalized * upper;
                wrist = elbow + new Vector3(-side * 0.05f, lead ? -0.30f : -0.52f, lead ? 0.60f : 0.44f).normalized * lower;
                tip = wrist + new Vector3(0f, lead ? -0.10f : -0.28f, lead ? 0.33f : 0.21f).normalized * hand;
            }

            float jr = _torso.jointRadius;
            Hub(armRoot, "ShoulderHub", shoulder, 0.15f * jr, false);
            Hub(armRoot, "ElbowHub", elbow, 0.13f * jr, true);
            Hub(armRoot, "WristHub", wrist, 0.11f * jr, false);

            Bone(armRoot, "UpperArmBone", $"Skel_UpperArm_{torsoWeight}", elbow, shoulder, (shoulder - elbow).magnitude, 0.14f);
            Bone(armRoot, "LowerArmBone", $"Skel_LowerArm_{torsoWeight}", wrist, elbow, (elbow - wrist).magnitude, 0.13f);
            if (Skel(armRoot, "HandBone", "Skel_Hand", wrist, BoneRotation(wrist, tip, Vector3.forward), Vector3.one) == null)
            {
                Strut(armRoot, "HandBone", wrist, tip, 0.12f);
            }

            JointGuard(armRoot, "ElbowGuard", elbow, shoulder, wrist, Vector3.back, 0.13f * jr / 0.15f * 0.9f);

            ClamshellOnBone(armRoot, "UpperArm", elbow, shoulder, 0.18f, 0.20f, Vector3.forward, UpperArmDims.For(_torso),
                upperArmFrontSet, upperArmBackSet, -0.35f, 0.35f, out _, out _);
            ClamshellOnBone(armRoot, "LowerArm", wrist, elbow, 0.16f, 0.18f, Vector3.forward, LowerArmDims.For(_torso),
                lowerArmFrontSet, lowerArmBackSet, -0.35f, 0.35f, out _, out _);
            ClamshellOnBone(armRoot, "Hand", wrist, tip, 0.15f, 0f, Vector3.forward, HandDims,
                handFrontSet, handBackSet, -0.3f, 0.3f, out _, out _);
            if (side > 0) ClamshellParts.Empty(armRoot, "WeaponMuzzle", tip + new Vector3(0f, 0f, 0.25f), Quaternion.identity);
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

            Quaternion rot = BoneRotation(from, to, outwardHint);

            Transform mount = ClamshellParts.Empty(parent, label + "_Clamshell", center, rot);
            if (!showArmor) return mount;
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
            string halfName = front ? $"{label}_FrontHalf_{set}" : $"{label}_BackHalf_{set}";

            // Hand-modeled part? It already carries its own details, fitted into this slot's envelope.
            if (TrySlotFor(label, front, out ClamshellSlot slot))
            {
                float outer = (dims.inner + dims.depth * 1.6f) * sign;
                var target = new Bounds(
                    new Vector3(0f, 0f, (dims.inner * sign + outer) * 0.5f),
                    new Vector3(dims.width, shellLength, dims.depth * 1.6f));
                if (ClamshellMeshLibrary.TryGet(slot, set, target, out Mesh custom, out var roles))
                {
                    var go = ClamshellParts.Spawn(mount, halfName + "_Custom", custom, Vector3.zero, Quaternion.identity, color);
                    if (roles != null && roles.Length > 1)
                    {
                        var mats = new Material[roles.Length];
                        for (int i = 0; i < roles.Length; i++) mats[i] = RoleMaterial(roles[i], color);
                        go.GetComponent<MeshRenderer>().sharedMaterials = mats;
                    }
                    return;
                }
            }

            var mesh = ClamshellMeshFactory.ShellHalf(shellLength, dims.width, dims.depth, dims.inner, dims.bevel, sign);
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

        /// <summary>
        /// Spawns the hand-modeled lengthwise part <paramref name="resource"/> (feet, quadruped body, animal head) in a
        /// box of <paramref name="size"/> (X width, Y length toward the tip, Z height) centred at the spawn point.
        /// Returns null when there is no model, so the caller builds the procedural half instead.
        /// <paramref name="widthScale"/> widens or narrows the model for Light and Heavy frames.
        /// </summary>
        private GameObject TrySpawnLengthwise(Transform parent, string objName, string resource, Vector3 size, Vector3 pos, Quaternion rot, Color color, float widthScale = 1f)
        {
            if (!ClamshellMeshLibrary.TryGetLengthwise(resource, new Bounds(Vector3.zero, size), out Mesh custom, out var roles, widthScale)) return null;
            var go = ClamshellParts.Spawn(parent, objName + "_Custom", custom, pos, rot, color);
            if (roles != null && roles.Length > 1)
            {
                var mats = new Material[roles.Length];
                for (int i = 0; i < roles.Length; i++) mats[i] = RoleMaterial(roles[i], color);
                go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            }
            return go;
        }

        private static Material RoleMaterial(ClamshellMeshLibrary.PartRole role, Color shell)
        {
            switch (role)
            {
                case ClamshellMeshLibrary.PartRole.Panel: return IronFaunaMaterials.Get(IronFaunaPalette.OffWhitePanel, false);
                case ClamshellMeshLibrary.PartRole.Hazard: return IronFaunaMaterials.Get(IronFaunaPalette.HazardYellow, false);
                case ClamshellMeshLibrary.PartRole.Dark: return IronFaunaMaterials.Get(IronFaunaPalette.DarkIron, false);
                case ClamshellMeshLibrary.PartRole.Frame: return IronFaunaMaterials.Get(IronFaunaPalette.GunmetalFrame, false);
                case ClamshellMeshLibrary.PartRole.Glow: return IronFaunaMaterials.Get(IronFaunaPalette.CyanEnergy, true);
                case ClamshellMeshLibrary.PartRole.Core: return IronFaunaMaterials.Get(IronFaunaPalette.OrangeJointCore, true);
                case ClamshellMeshLibrary.PartRole.Copper: return IronFaunaMaterials.Get(IronFaunaPalette.CopperCoil, false);
                default: return IronFaunaMaterials.Get(shell, false);
            }
        }

        private static bool TrySlotFor(string label, bool front, out ClamshellSlot slot)
        {
            slot = ClamshellSlot.ThighFront;
            if (label.StartsWith("Thigh")) slot = front ? ClamshellSlot.ThighFront : ClamshellSlot.ThighBack;
            else if (label.StartsWith("Calf")) slot = front ? ClamshellSlot.CalfFront : ClamshellSlot.CalfBack;
            else if (label.StartsWith("Torso")) slot = front ? ClamshellSlot.BodyFront : ClamshellSlot.BodyBack;
            else if (label.StartsWith("Head")) slot = front ? ClamshellSlot.HeadFront : ClamshellSlot.HeadBack;
            else if (label.StartsWith("Shoulder")) slot = front ? ClamshellSlot.ShoulderFront : ClamshellSlot.ShoulderBack;
            else if (label.StartsWith("UpperArm")) slot = front ? ClamshellSlot.UpperArmFront : ClamshellSlot.UpperArmBack;
            else if (label.StartsWith("LowerArm")) slot = front ? ClamshellSlot.LowerArmFront : ClamshellSlot.LowerArmBack;
            else if (label.StartsWith("Hand")) slot = front ? ClamshellSlot.HandFront : ClamshellSlot.HandBack;
            else return false;
            return true;
        }

        /// <summary>Rotation with local +Y along the bone and local +Z toward <paramref name="outwardHint"/>.</summary>
        private static Quaternion BoneRotation(Vector3 from, Vector3 to, Vector3 outwardHint)
        {
            Vector3 delta = to - from;
            Vector3 dir = delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector3.up;
            Vector3 outward = Vector3.ProjectOnPlane(outwardHint, dir);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Vector3.up, dir);
            return Quaternion.LookRotation(outward.normalized, dir);
        }

        /// <summary>
        /// Spawns a modeled skeleton piece ("Skel_*" in Resources/ClamshellMeshes) at its pivot. Returns null when
        /// the model is missing so the caller can fall back to a plain strut or drum.
        /// </summary>
        private static GameObject Skel(Transform parent, string objName, string resource, Vector3 pos, Quaternion rot, Vector3 scale, Color? paint = null)
        {
            if (!ClamshellMeshLibrary.TryGetRaw(resource, scale, out Mesh mesh, out var roles)) return null;
            Color shell = paint ?? IronFaunaPalette.GunmetalFrame;
            var go = ClamshellParts.Spawn(parent, objName, mesh, pos, rot, shell);
            if (roles != null && roles.Length > 1)
            {
                var mats = new Material[roles.Length];
                for (int i = 0; i < roles.Length; i++) mats[i] = RoleMaterial(roles[i], shell);
                go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>
        /// Layered guard floating over the open (outer) side of a knee or elbow. It is mounted on the joint, not on
        /// either limb plate, so it overlaps the joint like a shingle without ever meeting the armor as the limb folds.
        /// </summary>
        private void JointGuard(Transform parent, string name, Vector3 joint, Vector3 upper, Vector3 lower, Vector3 straightOut, float scale)
        {
            if (!showArmor) return;
            Vector3 toUpper = (upper - joint).normalized;
            Vector3 outDir = -(toUpper + (lower - joint).normalized);
            if (outDir.sqrMagnitude < 0.01f) outDir = straightOut; // dead-straight limb
            outDir.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(toUpper, outDir);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            Skel(parent, name, "Skel_JointGuard", joint, Quaternion.LookRotation(outDir, up.normalized), Vector3.one * scale, _front);
        }

        /// <summary>Limb bone between two joints: the modeled bone for this weight class, or a plain strut.</summary>
        private static void Bone(Transform parent, string name, string resource, Vector3 from, Vector3 to, float modeledLength, float fallbackThickness)
        {
            float len = (to - from).magnitude;
            var scale = new Vector3(1f, len / Mathf.Max(modeledLength, 1e-4f), 1f);
            if (Skel(parent, name, resource, (from + to) * 0.5f, BoneRotation(from, to, Vector3.forward), scale) == null)
            {
                Strut(parent, name, from, to, fallbackThickness);
            }
        }

        private void AddHardpoint(string id, Vector3 localPos, HardpointSize size, Vector3 mountAxis)
        {
            Transform t = ClamshellParts.Empty(_visualRoot, $"Hardpoint_{id}_{size}", localPos, Quaternion.identity);
            var hp = t.gameObject.AddComponent<MechHardpoint>();
            hp.id = id;
            hp.size = size;
            hp.mountAxis = mountAxis;
            hardpoints.Add(hp);
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
            if (Skel(parent, name, glowingCore ? "Skel_JointCore" : "Skel_Joint", localPos, Quaternion.identity, Vector3.one * radius) != null) return;
            Quaternion alongX = Quaternion.Euler(0f, 0f, 90f); // rotary drum axis along X (the leg/arm hinge axis)
            ClamshellParts.Spawn(parent, name, ClamshellMeshFactory.Prism(radius, radius * 1.1f, 16), localPos, alongX, IronFaunaPalette.GunmetalFrame);
            if (glowingCore)
            {
                ClamshellParts.Spawn(parent, name + "_OrangeCore", ClamshellMeshFactory.Prism(radius * 0.6f, radius * 1.22f, 16), localPos, alongX, IronFaunaPalette.OrangeJointCore, true);
            }
        }

        /// <summary>Kept for existing callers (arena setup, grid defenses).</summary>
        public static void ApplyPaletteMaterial(GameObject go, Color baseColor, bool emissive)
        {
            IronFaunaMaterials.Apply(go, baseColor, emissive);
        }
    }
}
