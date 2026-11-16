using UnityEngine;
using IronFauna.Core;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Assembles an IronFauna mech or quadruped biomech using the Universal Clamshell Envelope:
    /// - Bare gunmetal skeleton with 100% uncovered rotary joint hubs (glowing orange knee/elbow/neck cores)
    /// - Two-half clamshells (Crimson Front + Maroon Back) clamped onto single bones with toggle latches
    /// - 45-degree beveled joint clearances so legs fold into all 5 poses with zero overlap
    /// - Hip hub remains vertically aligned above the foot center across all 5 poses
    /// </summary>
    public class MechClamshellBuilder : MonoBehaviour
    {
        [Header("Chassis & Stance Configuration")]
        public bool isQuadrupedChassis = false;
        public LegPosePreset legPose = LegPosePreset.Pose1_HumanLeg;

        [Header("Mix-and-Match Clamshell Set Selection (Any Front + Any Back)")]
        public ClamshellSetArchetype headFrontSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype headBackSet  = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype bodyFrontSet = ClamshellSetArchetype.Set01_Ironclad;
        public ClamshellSetArchetype bodyBackSet  = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype armFrontSet  = ClamshellSetArchetype.Set04_Ripjaw;
        public ClamshellSetArchetype armBackSet   = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype thighFrontSet = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype thighBackSet  = ClamshellSetArchetype.Set02_Bastion;
        public ClamshellSetArchetype calfFrontSet  = ClamshellSetArchetype.Set03_Overclock;
        public ClamshellSetArchetype calfBackSet   = ClamshellSetArchetype.Set05_Afterburn;
        public ClamshellSetArchetype footFrontSet  = ClamshellSetArchetype.Set04_Ripjaw;
        public ClamshellSetArchetype footBackSet   = ClamshellSetArchetype.Set04_Ripjaw;

        [Header("Bone Dimensions (Universal Envelope)")]
        public float thighLength = 1.15f;
        public float calfLength  = 1.15f;
        public float jointRadius = 0.20f;
        public float collarWidth = 0.36f;

        private Transform _visualRoot;

        private void Start()
        {
            RebuildMech();
        }

        [ContextMenu("Rebuild Clamshell Mech")]
        public void RebuildMech()
        {
            if (_visualRoot != null)
            {
                if (Application.isPlaying) Destroy(_visualRoot.gameObject);
                else DestroyImmediate(_visualRoot.gameObject);
            }

            _visualRoot = new GameObject("ClamshellMechRoot").transform;
            _visualRoot.SetParent(transform, false);

            // Compute balanced leg joint angles so Hip is always directly above Foot (X=0, Z=0)
            GetPoseAngles(legPose, out float thighPitchDeg, out float calfPitchDeg, out float hipHeight);

            if (!isQuadrupedChassis)
            {
                BuildHumanoidChassis(thighPitchDeg, calfPitchDeg, hipHeight);
            }
            else
            {
                BuildQuadrupedChassis(thighPitchDeg, calfPitchDeg, hipHeight);
            }
        }

        /// <summary>
        /// Solves 2-bone kinematics so that in all 5 poses (Human Leg, Chicken Forward,
        /// Reverse Extended, Chicken Backward, Reverse Critical Fold), the Hip Hub stays
        /// directly on the vertical plumb line above the Ankle/Foot (zOffset == 0).
        /// </summary>
        public void GetPoseAngles(LegPosePreset pose, out float thighAngleDeg, out float calfAngleDeg, out float hipHeight)
        {
            // Since thighLength == calfLength == L, setting calfAngleDeg = -thighAngleDeg
            // guarantees that Hip Z == Ankle Z (vertical plumb line balance!).
            // Positive thighAngle tilts the knee FORWARD (+Z); Negative tilts the knee BACKWARD (-Z, reverse joint).
            float angle = 22f;
            switch (pose)
            {
                case LegPosePreset.Pose1_HumanLeg:
                    angle = 20f;  // Standard forward knee
                    break;
                case LegPosePreset.Pose2_ChickenBendForward:
                    angle = 42f;  // Deep forward knee crouch
                    break;
                case LegPosePreset.Pose3_ReverseExtended:
                    angle = -18f; // Tall reverse-joint stance (knee swung backward)
                    break;
                case LegPosePreset.Pose4_ChickenBendBackward:
                    angle = -38f; // Pronounced digitigrade Z-bend (knee swung backward)
                    break;
                case LegPosePreset.Pose5_ReverseCriticalFold:
                    angle = -54f; // Deepest reverse fold before 45-degree bevel stop
                    break;
            }

            thighAngleDeg = angle;
            calfAngleDeg  = -angle;
            float rad = Mathf.Abs(angle) * Mathf.Deg2Rad;
            hipHeight = 0.25f + (thighLength + calfLength) * Mathf.Cos(rad);
        }

        private void BuildHumanoidChassis(float thighAngle, float calfAngle, float hipHeight)
        {
            // Left and Right Legs
            BuildLegAssembly("Leg_L", new Vector3(-0.48f, 0f, 0f), thighAngle, calfAngle, hipHeight);
            BuildLegAssembly("Leg_R", new Vector3( 0.48f, 0f, 0f), thighAngle, calfAngle, hipHeight);

            // Pelvis / Waist Hub
            Vector3 waistPos = new Vector3(0f, hipHeight + 0.1f, 0f);
            CreateJointHub("WaistHub", _visualRoot, waistPos, jointRadius * 1.1f, true);

            // Upright Spine + Clamshell Torso Core (No limbs attached to the armor plates!)
            Transform torsoRoot = new GameObject("TorsoCore").transform;
            torsoRoot.SetParent(_visualRoot, false);
            torsoRoot.localPosition = waistPos + new Vector3(0f, 0.72f, 0f);

            // Bare gunmetal spine
            CreateBox("SpineBone", torsoRoot, Vector3.zero, new Vector3(0.32f, 1.1f, 0.28f), IronFaunaPalette.GunmetalFrame);

            // Clamshell Front Chest Half (Crimson) + Rear Backplate Half (Maroon)
            BuildClamshellPair(torsoRoot, "Torso", new Vector3(0.82f, 0.92f, 0.30f), bodyFrontSet, bodyBackSet);

            // Uncovered Neck Hub + Head Clamshell
            Vector3 neckPos = waistPos + new Vector3(0f, 1.42f, 0f);
            CreateJointHub("NeckHub", _visualRoot, neckPos, jointRadius * 0.85f, true);

            Transform headRoot = new GameObject("HeadCore").transform;
            headRoot.SetParent(_visualRoot, false);
            headRoot.localPosition = neckPos + new Vector3(0f, 0.34f, 0f);
            CreateBox("SkullSensorCore", headRoot, Vector3.zero, new Vector3(0.26f, 0.34f, 0.28f), IronFaunaPalette.GunmetalFrame);
            BuildClamshellPair(headRoot, "Head", new Vector3(0.44f, 0.40f, 0.24f), headFrontSet, headBackSet);
            // Cyan Visor
            CreateBox("VisorOptic", headRoot, new Vector3(0f, 0.04f, 0.25f), new Vector3(0.32f, 0.10f, 0.04f), IronFaunaPalette.CyanEnergy, true);

            // Left & Right Arms (Uncovered Shoulder, Elbow, Wrist Hubs + Clamshells)
            BuildArmAssembly("Arm_L", waistPos + new Vector3(-0.72f, 1.05f, 0f));
            BuildArmAssembly("Arm_R", waistPos + new Vector3( 0.72f, 1.05f, 0f));
        }

        private void BuildQuadrupedChassis(float thighAngle, float calfAngle, float hipHeight)
        {
            // 4 Legs (Front Left/Right, Rear Left/Right)
            BuildLegAssembly("FrontLeg_L", new Vector3(-0.52f, 0f,  0.85f), thighAngle, calfAngle, hipHeight);
            BuildLegAssembly("FrontLeg_R", new Vector3( 0.52f, 0f,  0.85f), thighAngle, calfAngle, hipHeight);
            BuildLegAssembly("RearLeg_L",  new Vector3(-0.52f, 0f, -0.85f), -thighAngle, -calfAngle, hipHeight);
            BuildLegAssembly("RearLeg_R",  new Vector3( 0.52f, 0f, -0.85f), -thighAngle, -calfAngle, hipHeight);

            // Horizontal Beast Spine + Front Thorax (Crimson) & Rear Haunch (Maroon) Clamshells
            Transform beastSpine = new GameObject("QuadrupedSpineCore").transform;
            beastSpine.SetParent(_visualRoot, false);
            beastSpine.localPosition = new Vector3(0f, hipHeight + 0.15f, 0f);

            CreateBox("HorizontalSpine", beastSpine, Vector3.zero, new Vector3(0.36f, 0.34f, 2.1f), IronFaunaPalette.GunmetalFrame);
            CreateBox("ThoraxFrontHalf", beastSpine, new Vector3(0f, 0.05f, 0.52f), new Vector3(0.76f, 0.62f, 0.82f), IronFaunaPalette.CrimsonFront);
            CreateBox("HaunchRearHalf",  beastSpine, new Vector3(0f, 0.05f, -0.52f), new Vector3(0.72f, 0.58f, 0.82f), IronFaunaPalette.MaroonRear);

            // Uncovered Dorsal Turret Hub + Neck Hub
            CreateJointHub("DorsalWeaponHub", beastSpine, new Vector3(0f, 0.44f, 0f), jointRadius, true);
            CreateJointHub("QuadNeckHub", beastSpine, new Vector3(0f, 0.18f, 1.08f), jointRadius * 0.9f, true);

            Transform beastHead = new GameObject("BeastHeadCore").transform;
            beastHead.SetParent(beastSpine, false);
            beastHead.localPosition = new Vector3(0f, 0.28f, 1.48f);
            BuildClamshellPair(beastHead, "AnimalHead", new Vector3(0.46f, 0.38f, 0.32f), headFrontSet, headBackSet);
            CreateBox("BeastOptics", beastHead, new Vector3(0f, 0.06f, 0.33f), new Vector3(0.36f, 0.08f, 0.05f), IronFaunaPalette.CyanEnergy, true);
        }

        private void BuildLegAssembly(string legName, Vector3 footGroundPos, float thighAngleDeg, float calfAngleDeg, float hipHeight)
        {
            Transform legRoot = new GameObject(legName).transform;
            legRoot.SetParent(_visualRoot, false);
            legRoot.localPosition = footGroundPos;

            Vector3 anklePos = new Vector3(0f, 0.25f, 0f);
            float thighRad = thighAngleDeg * Mathf.Deg2Rad;
            Vector3 kneePos = anklePos + new Vector3(0f, calfLength * Mathf.Cos(thighRad), calfLength * Mathf.Sin(thighRad));
            Vector3 hipPos  = new Vector3(0f, hipHeight, 0f);

            // 1. Uncovered Rotary Joint Hubs (Hip, Glowing-Orange Knee, Ankle)
            CreateJointHub("HipHub",   legRoot, hipPos,   jointRadius, false);
            CreateJointHub("KneeHub",  legRoot, kneePos,  jointRadius, true); // Glowing orange knee core!
            CreateJointHub("AnkleHub", legRoot, anklePos, jointRadius * 0.8f, false);

            // 2. Thigh Bone + Clamshell Halves (Stops short of Hip and Knee hubs with 45° bevel clearance!)
            Transform thighBone = CreateBoneSegment("ThighBone", legRoot, hipPos, kneePos, jointRadius * 1.35f);
            float safeThighShellLen = Mathf.Max(0.3f, thighLength - (jointRadius * 2.6f));
            BuildClamshellPair(thighBone, "Thigh", new Vector3(collarWidth, safeThighShellLen, 0.18f), thighFrontSet, thighBackSet);

            // 3. Calf Bone + Clamshell Halves (Stops short of Knee and Ankle hubs with 45° bevel clearance!)
            Transform calfBone = CreateBoneSegment("CalfBone", legRoot, kneePos, anklePos, jointRadius * 1.25f);
            float safeCalfShellLen = Mathf.Max(0.3f, calfLength - (jointRadius * 2.5f));
            BuildClamshellPair(calfBone, "Calf", new Vector3(collarWidth, safeCalfShellLen, 0.17f), calfFrontSet, calfBackSet);

            // 4. Foot Chassis + Toe (Front Crimson) & Heel (Rear Maroon) Clamshell Halves
            Transform footBone = new GameObject("FootBone").transform;
            footBone.SetParent(legRoot, false);
            footBone.localPosition = new Vector3(0f, 0.10f, 0f);
            CreateBox("BareFootStrut", footBone, Vector3.zero, new Vector3(collarWidth * 0.7f, 0.14f, 0.56f), IronFaunaPalette.GunmetalFrame);
            CreateBox("FootToeFrontHalf", footBone, new Vector3(0f, 0.02f, 0.22f), new Vector3(collarWidth, 0.18f, 0.34f), IronFaunaPalette.CrimsonFront);
            CreateBox("FootHeelBackHalf", footBone, new Vector3(0f, 0.02f, -0.20f), new Vector3(collarWidth * 0.95f, 0.18f, 0.26f), IronFaunaPalette.MaroonRear);
        }

        private void BuildArmAssembly(string armName, Vector3 shoulderPos)
        {
            Transform armRoot = new GameObject(armName).transform;
            armRoot.SetParent(_visualRoot, false);
            armRoot.localPosition = shoulderPos;

            Vector3 elbowPos = new Vector3(0f, -0.68f, 0.08f);
            Vector3 wristPos = new Vector3(0f, -1.34f, 0.24f);

            // Uncovered Shoulder, Glowing-Orange Elbow, and Wrist Hubs
            CreateJointHub("ShoulderHub", armRoot, Vector3.zero, jointRadius * 0.85f, true);
            CreateJointHub("ElbowHub",    armRoot, elbowPos,     jointRadius * 0.75f, true);
            CreateJointHub("WristHub",    armRoot, wristPos,     jointRadius * 0.60f, true);

            Transform upperArmBone = CreateBoneSegment("UpperArmBone", armRoot, Vector3.zero, elbowPos, 0.22f);
            BuildClamshellPair(upperArmBone, "UpperArm", new Vector3(0.28f, 0.38f, 0.14f), armFrontSet, armBackSet);

            Transform forearmBone = CreateBoneSegment("LowerArmBone", armRoot, elbowPos, wristPos, 0.20f);
            BuildClamshellPair(forearmBone, "LowerArm", new Vector3(0.28f, 0.38f, 0.15f), armFrontSet, armBackSet);

            Transform handRoot = new GameObject("HandManipulator").transform;
            handRoot.SetParent(armRoot, false);
            handRoot.localPosition = wristPos + new Vector3(0f, -0.18f, 0.05f);
            BuildClamshellPair(handRoot, "Hand", new Vector3(0.22f, 0.20f, 0.12f), armFrontSet, armBackSet);
        }

        private void BuildClamshellPair(Transform boneParent, string label, Vector3 halfSize, ClamshellSetArchetype frontSet, ClamshellSetArchetype backSet)
        {
            float zOffset = halfSize.z * 0.55f + 0.04f;

            // Front Half (Crimson Red + Off-White Panel Strip)
            GameObject frontHalf = CreateBox($"{label}_FrontHalf_{frontSet}", boneParent, new Vector3(0f, 0f, zOffset), halfSize, IronFaunaPalette.CrimsonFront);
            CreateBox($"{label}_WhiteStrip", frontHalf.transform, new Vector3(0.22f, 0f, 0.52f), new Vector3(0.18f, 0.88f, 0.08f), IronFaunaPalette.OffWhitePanel);

            // Back Half (Darker Maroon Red + Hazard Stripe Block)
            GameObject backHalf = CreateBox($"{label}_BackHalf_{backSet}", boneParent, new Vector3(0f, 0f, -zOffset), halfSize, IronFaunaPalette.MaroonRear);
            CreateBox($"{label}_HazardBlock", backHalf.transform, new Vector3(0f, -0.25f, -0.52f), new Vector3(0.86f, 0.18f, 0.08f), IronFaunaPalette.HazardYellow);

            // Left & Right Mechanical Toggle Latches across the parting seam
            float latchX = halfSize.x * 0.52f;
            CreateBox($"{label}_ToggleLatch_L", boneParent, new Vector3(-latchX, 0f, 0f), new Vector3(0.05f, halfSize.y * 0.22f, 0.14f), IronFaunaPalette.DarkIron);
            CreateBox($"{label}_ToggleLatch_R", boneParent, new Vector3( latchX, 0f, 0f), new Vector3(0.05f, halfSize.y * 0.22f, 0.14f), IronFaunaPalette.DarkIron);

            // Archetype-specific outer detail (e.g., Overclock Cyan Fin, Ripjaw Cleat, Aegis Hard-Light)
            if (frontSet == ClamshellSetArchetype.Set04_Ripjaw)
            {
                CreateBox($"{label}_RipjawCleat", frontHalf.transform, new Vector3(0f, 0f, 0.62f), new Vector3(0.16f, 0.72f, 0.28f), IronFaunaPalette.GunmetalFrame);
            }
            else if (frontSet == ClamshellSetArchetype.Set10_Aegis)
            {
                CreateBox($"{label}_HardLightEmitter", frontHalf.transform, new Vector3(0f, 0f, 0.58f), new Vector3(0.25f, 0.78f, 0.08f), IronFaunaPalette.CyanEnergy, true);
            }

            if (backSet == ClamshellSetArchetype.Set03_Overclock)
            {
                CreateBox($"{label}_RadiatorLouvers", backHalf.transform, new Vector3(0f, 0.1f, -0.58f), new Vector3(0.65f, 0.55f, 0.18f), IronFaunaPalette.CyanEnergy, true);
            }
            else if (backSet == ClamshellSetArchetype.Set05_Afterburn)
            {
                CreateBox($"{label}_ThrusterNozzle", backHalf.transform, new Vector3(0f, -0.15f, -0.65f), new Vector3(0.52f, 0.32f, 0.28f), IronFaunaPalette.DarkIron);
            }
        }

        private Transform CreateBoneSegment(string name, Transform parent, Vector3 topPos, Vector3 bottomPos, float thickness)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            Vector3 mid = (topPos + bottomPos) * 0.5f;
            // Point +Y from bottomPos toward topPos so local +Y is UP and local +Z stays FORWARD!
            Vector3 dir = topPos - bottomPos;
            float len = dir.magnitude;

            bone.localPosition = mid;
            if (len > 0.001f)
            {
                bone.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            }

            CreateBox($"{name}_Strut", bone, Vector3.zero, new Vector3(thickness * 0.55f, len, thickness * 0.55f), IronFaunaPalette.GunmetalFrame);
            return bone;
        }

        private void CreateJointHub(string name, Transform parent, Vector3 localPos, float radius, bool glowingOrangeCore)
        {
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = name;
            hub.transform.SetParent(parent, false);
            hub.transform.localPosition = localPos;
            hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // Rotary drum oriented along X axis
            hub.transform.localScale = new Vector3(radius * 2f, 0.16f, radius * 2f);
            DestroyCollider(hub);
            ApplyPaletteMaterial(hub, IronFaunaPalette.GunmetalFrame, false);

            if (glowingOrangeCore)
            {
                GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                core.name = $"{name}_OrangeCore";
                core.transform.SetParent(hub.transform, false);
                core.transform.localPosition = Vector3.zero;
                core.transform.localScale = new Vector3(0.62f, 1.08f, 0.62f);
                DestroyCollider(core);
                ApplyPaletteMaterial(core, IronFaunaPalette.OrangeJointCore, true);
            }
        }

        private GameObject CreateBox(string name, Transform parent, Vector3 localPos, Vector3 localScale, Color color, bool emissive = false)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = localScale;
            DestroyCollider(box);
            ApplyPaletteMaterial(box, color, emissive);
            return box;
        }

        private void DestroyCollider(GameObject go)
        {
            if (go.TryGetComponent<Collider>(out var col))
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        public static void ApplyPaletteMaterial(GameObject go, Color baseColor, bool emissive)
        {
            if (!go.TryGetComponent<Renderer>(out var rend)) return;

            string hex = ColorUtility.ToHtmlStringRGB(baseColor);
            string matName = emissive ? $"IF_Mat_{hex}_Emissive" : $"IF_Mat_{hex}";

#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets", "Materials");
            }
            string assetPath = $"Assets/Materials/{matName}.mat";
            Material existingMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existingMat != null)
            {
                ConfigureMaterial(existingMat, baseColor, emissive);
                UnityEditor.EditorUtility.SetDirty(existingMat);
                rend.sharedMaterial = existingMat;
                return;
            }
#endif

            Material templateMat = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;

            Material mat = templateMat != null
                ? new Material(templateMat)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = matName;
            ConfigureMaterial(mat, baseColor, emissive);

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.CreateAsset(mat, $"Assets/Materials/{matName}.mat");
#endif
            rend.sharedMaterial = mat;
        }

        private static void ConfigureMaterial(Material mat, Color baseColor, bool emissive)
        {
            mat.color = baseColor;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", 0.38f);
            mat.SetFloat("_Metallic", baseColor == IronFaunaPalette.GunmetalFrame ? 0.55f : 0.15f);

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", baseColor * 2.5f);
            }
        }
    }
}
