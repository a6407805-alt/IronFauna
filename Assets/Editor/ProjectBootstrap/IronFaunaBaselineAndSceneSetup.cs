using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFauna.Core;
using IronFauna.Clamshell;

namespace ProjectBootstrap
{
    public static class IronFaunaBaselineAndSceneSetup
    {
        // Invoke via: unity run ... -- -executeMethod ProjectBootstrap.IronFaunaBaselineAndSceneSetup.ApplyBaselineAndBuildScene
        public static void ApplyBaselineAndBuildScene()
        {
            try
            {
                // 0. Open SampleScene.unity explicitly so batchmode edits and saves the real scene file
                const string scenePath = "Assets/Scenes/SampleScene.unity";
                if (System.IO.File.Exists(scenePath))
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }

                // 1. Quality Tier -> highest desktop tier
                string[] qualityNames = QualitySettings.names;
                if (qualityNames != null && qualityNames.Length > 0)
                {
                    QualitySettings.SetQualityLevel(qualityNames.Length - 1, true);
                }

                // 2. URP Asset -> Ensure HDR is enabled for ACES Tonemapping & Bloom
                var urpAsset = UniversalRenderPipeline.asset;
                if (urpAsset == null)
                {
                    urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                }
                if (urpAsset != null)
                {
                    urpAsset.supportsHDR = true;
                    EditorUtility.SetDirty(urpAsset);
                }

                // 3. Configure Active Scene Lighting (craft-game-floors: scene-lighting.md + art-direction.md)
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = IronFaunaPalette.AmbientSkyCool;
                RenderSettings.ambientEquatorColor = IronFaunaPalette.AmbientEquatorMid;
                RenderSettings.ambientGroundColor = IronFaunaPalette.AmbientGroundDark;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = IronFaunaPalette.AmbientSkyCool;
                RenderSettings.fogDensity = 0.008f;

                // 4. Directional Key Light
                Light dirLight = UnityEngine.Object.FindFirstObjectByType<Light>();
                if (dirLight == null)
                {
                    var lightGo = new GameObject("Directional Light");
                    dirLight = lightGo.AddComponent<Light>();
                    dirLight.type = LightType.Directional;
                }
                dirLight.color = IronFaunaPalette.KeyLightWarm;
                dirLight.intensity = 1.22f;
                dirLight.shadows = LightShadows.Soft;
                dirLight.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

                // 5. Main Camera + Post-Processing enabled
                Camera cam = Camera.main;
                if (cam == null)
                {
                    var camGo = new GameObject("Main Camera");
                    camGo.tag = "MainCamera";
                    cam = camGo.AddComponent<Camera>();
                }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = IronFaunaPalette.ShadowSlate;
                cam.transform.position = new Vector3(0f, 3.8f, -8.5f);
                cam.transform.rotation = Quaternion.Euler(14f, 0f, 0f);

                if (!cam.TryGetComponent<UniversalAdditionalCameraData>(out var camData))
                {
                    camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }
                camData.renderPostProcessing = true;

                // 6. Global Volume + VolumeProfile (ACES Tonemapping, Bloom, Vignette, ColorAdjustments)
                SetupGlobalVolume();

                // 7. Build IronFauna Blueprint Arena & Clamshell Mechs
                BuildArenaAndMechs();

                // 8. Save Scene & Assets explicitly to Assets/Scenes/SampleScene.unity
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/SampleScene.unity");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 9. Render Baseline Verification Screenshot (baseline.png)
                CaptureCameraToPng(cam, "C:/Users/ahren/.gemini/antigravity/scratch/IronFauna/baseline.png", 1280, 720);

                Debug.Log("[IronFaunaBaseline] Visual baseline and Clamshell Arena scene saved successfully.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IronFaunaBaseline] Error: {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        private static void CaptureCameraToPng(Camera cam, string outputPath, int width, int height)
        {
            if (cam == null) return;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            RenderTexture prevCamRt = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] pngBytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(outputPath, pngBytes);

            cam.targetTexture = prevCamRt;
            RenderTexture.active = prevActive;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[IronFaunaBaseline] Captured screenshot to {outputPath}");
        }

        private static void SetupGlobalVolume()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            const string profilePath = "Assets/Settings/IronFaunaGlobalVolumeProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
            {
                AssetDatabase.DeleteAsset(profilePath);
            }
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            var tonemapping = profile.Add<Tonemapping>(false);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            var bloom = profile.Add<Bloom>(false);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.95f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.45f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;

            var vignette = profile.Add<Vignette>(false);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.20f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.35f;

            var colorAdj = profile.Add<ColorAdjustments>(false);
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.35f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 12f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 8f;

            EditorUtility.SetDirty(profile);

            Volume globalVol = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (globalVol == null)
            {
                var volGo = new GameObject("Global Volume");
                globalVol = volGo.AddComponent<Volume>();
            }
            globalVol.isGlobal = true;
            globalVol.sharedProfile = profile;
        }

        private static void BuildArenaAndMechs()
        {
            GameObject existingArena = GameObject.Find("IronFauna_ArenaEnvironment");
            if (existingArena != null) UnityEngine.Object.DestroyImmediate(existingArena);

            Transform arenaRoot = new GameObject("IronFauna_ArenaEnvironment").transform;

            // Tan Blueprint Arena Deck Floor
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ArenaDeckFloor";
            floor.transform.SetParent(arenaRoot, false);
            floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            floor.transform.localScale = new Vector3(36f, 0.5f, 36f);
            MechClamshellBuilder.ApplyPaletteMaterial(floor, IronFaunaPalette.FloorTan, false);

            // Gunmetal Perimeter Walls + Baseboard Trim (craft-game-floors environment floor)
            CreateWallWithTrim(arenaRoot, "NorthWall", new Vector3(0f, 1.5f, 14f), new Vector3(36f, 3.5f, 0.8f));
            CreateWallWithTrim(arenaRoot, "WestWall",  new Vector3(-14f, 1.5f, 0f), new Vector3(0.8f, 3.5f, 28f));
            CreateWallWithTrim(arenaRoot, "EastWall",  new Vector3( 14f, 1.5f, 0f), new Vector3(0.8f, 3.5f, 28f));

            // Central Objective Reactor Core (Behind the mechs)
            Transform objective = new GameObject("CentralObjective_ReactorCore").transform;
            objective.SetParent(arenaRoot, false);
            objective.localPosition = new Vector3(0f, 0f, 6.5f);

            GameObject objBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            objBase.name = "Objective_BasePedestal";
            objBase.transform.SetParent(objective, false);
            objBase.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            objBase.transform.localScale = new Vector3(3.2f, 0.3f, 3.2f);
            MechClamshellBuilder.ApplyPaletteMaterial(objBase, IronFaunaPalette.GunmetalFrame, false);

            GameObject objPillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            objPillar.name = "Objective_EnergyCore";
            objPillar.transform.SetParent(objective, false);
            objPillar.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            objPillar.transform.localScale = new Vector3(1.1f, 1.8f, 1.1f);
            MechClamshellBuilder.ApplyPaletteMaterial(objPillar, IronFaunaPalette.CyanEnergy, true);

            // Remove old showcase mechs if re-running
            GameObject existingMechs = GameObject.Find("IronFauna_ClamshellShowcases");
            if (existingMechs != null) UnityEngine.Object.DestroyImmediate(existingMechs);
            Transform mechsRoot = new GameObject("IronFauna_ClamshellShowcases").transform;

            // 1. Center Hero: Humanoid Clamshell Mech (Kitbashed sets)
            GameObject heroMech = new GameObject("HeroMech_HumanoidClamshell");
            heroMech.transform.SetParent(mechsRoot, false);
            heroMech.transform.localPosition = new Vector3(-1.8f, 0f, -2.2f);
            heroMech.transform.localRotation = Quaternion.Euler(0f, 155f, 0f);
            var heroBuilder = heroMech.AddComponent<MechClamshellBuilder>();
            heroBuilder.isQuadrupedChassis = false;
            heroBuilder.legPose = LegPosePreset.Pose1_HumanLeg;
            heroBuilder.RebuildMech();

            // 2. Left Hero: Quadruped Animal Clamshell Biomech (Reverse-Joint Stance)
            GameObject beastMech = new GameObject("BeastMech_QuadrupedClamshell");
            beastMech.transform.SetParent(mechsRoot, false);
            beastMech.transform.localPosition = new Vector3(2.0f, 0f, -2.0f);
            beastMech.transform.localRotation = Quaternion.Euler(0f, -150f, 0f);
            var beastBuilder = beastMech.AddComponent<MechClamshellBuilder>();
            beastBuilder.isQuadrupedChassis = true;
            beastBuilder.legPose = LegPosePreset.Pose3_ReverseExtended;
            beastBuilder.RebuildMech();
        }

        private static void CreateWallWithTrim(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = pos;
            wall.transform.localScale = scale;
            MechClamshellBuilder.ApplyPaletteMaterial(wall, IronFaunaPalette.GunmetalFrame, false);

            GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trim.name = $"{name}_HazardTrim";
            trim.transform.SetParent(wall.transform, false);
            trim.transform.localPosition = new Vector3(0f, -0.42f, 0f);
            trim.transform.localScale = new Vector3(1.02f, 0.12f, 1.02f);
            MechClamshellBuilder.ApplyPaletteMaterial(trim, IronFaunaPalette.HazardYellow, false);
        }
    }
}
