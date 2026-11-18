using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using IronFauna.Clamshell;
using IronFauna.Menu;

namespace IronFauna.EditorTools
{
    /// <summary>
    /// Creates Assets/Scenes/MainMenu.unity (title, garage, and the brawl that runs inside it) and makes it the
    /// only scene in Build Settings. Runs once automatically when the project opens without a
    /// MainMenu scene, and can be re-run from the IronFauna menu.
    /// Also rebuilds procedural mechs in open scenes, since their meshes are not saved into scene files.
    /// </summary>
    [InitializeOnLoad]
    public static class IronFaunaMenuSceneSetup
    {
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string VolumeProfilePath = "Assets/Settings/IronFaunaGlobalVolumeProfile.asset";
        private const string AutoRunKey = "IronFauna.MenuSceneSetup.AutoRan";

        static IronFaunaMenuSceneSetup()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RebuildMechsInOpenScenes;
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureToonMaterials();
            RebuildMechsInOpenScenes();

            if (SessionState.GetBool(AutoRunKey, false)) return;
            SessionState.SetBool(AutoRunKey, true);
            if (!System.IO.File.Exists(MenuScenePath))
            {
                CreateMenuScene();
            }
            EnsureBuildSettings();
        }

        [MenuItem("IronFauna/Setup Menu Scene && Build Settings")]
        public static void SetupFromMenu()
        {
            CreateMenuScene();
            EnsureBuildSettings();
            Debug.Log("[IronFauna] MainMenu scene created and Build Settings updated.");
        }

        [MenuItem("IronFauna/Rebuild Mechs In Open Scenes")]
        public static void RebuildMechsInOpenScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var builder in Object.FindObjectsByType<MechClamshellBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (builder != null && builder.gameObject.scene.IsValid()) builder.RebuildMech();
            }
        }

        private const string ToonShaderName = "IronFauna/Toon";
        private const string ToonTemplatePath = "Assets/Resources/IF_ToonTemplate.mat";

        /// <summary>
        /// Keeps a material that references the toon shader in Resources (so builds include the shader)
        /// and moves every palette material (Assets/Materials/IF_Mat_*) onto the toon shader.
        /// </summary>
        [MenuItem("IronFauna/Upgrade Materials To Toon Shader")]
        public static void EnsureToonMaterials()
        {
            var toon = Shader.Find(ToonShaderName);
            if (toon == null) return;

            if (AssetDatabase.LoadAssetAtPath<Material>(ToonTemplatePath) == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateAsset(new Material(toon) { name = "IF_ToonTemplate" }, ToonTemplatePath);
            }

            // The realistic look keeps palette materials on URP Lit (IronFaunaMaterials.Get converts them).
            if (IronFauna.Core.IronFaunaMaterials.Realistic) return;
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) return;
            foreach (string guid in AssetDatabase.FindAssets("IF_Mat_ t:Material", new[] { "Assets/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader == toon) continue;
                Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                bool emissive = mat.name.EndsWith("_Emissive");
                mat.shader = toon;
                mat.SetColor("_BaseColor", baseColor);
                mat.SetColor("_OutlineColor", IronFauna.Core.IronFaunaPalette.ShadowSlate);
                mat.SetColor("_EmissionColor", emissive ? baseColor * 2.2f : baseColor * 0f);
                if (emissive) mat.SetFloat("_ShadowStrength", 0f);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        private static void CreateMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && string.IsNullOrEmpty(active.path) && active.isDirty)
            {
                Debug.LogWarning("[IronFauna] Save the current scene first, then run IronFauna > Setup Menu Scene & Build Settings.");
                return;
            }

            Scene scene;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[IronFauna] Could not create the MainMenu scene automatically ({ex.Message}). Run IronFauna > Setup Menu Scene & Build Settings.");
                return;
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            cam.transform.position = new Vector3(0f, 3f, -10f);
            SceneManager.MoveGameObjectToScene(camGo, scene);

            var lightGo = new GameObject("KeyLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null)
            {
                var volGo = new GameObject("Global Volume");
                var vol = volGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
                SceneManager.MoveGameObjectToScene(volGo, scene);
            }

            var rootGo = new GameObject("IronFauna_MenuRoot");
            rootGo.AddComponent<IronFaunaMenuRoot>();
            SceneManager.MoveGameObjectToScene(rootGo, scene);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, MenuScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }

        private static void EnsureBuildSettings()
        {
            if (!System.IO.File.Exists(MenuScenePath)) return;

            // the whole game runs in the menu scene: drop everything else (the old tower-defence arena)
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScenePath, true) };
        }
    }
}
