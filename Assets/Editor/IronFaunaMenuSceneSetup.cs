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
    /// Creates Assets/Scenes/MainMenu.unity (title + garage) and puts it first in Build Settings,
    /// ahead of the arena (SampleScene). Runs once automatically when the project opens without a
    /// MainMenu scene, and can be re-run from the IronFauna menu.
    /// Also rebuilds procedural mechs in open scenes, since their meshes are not saved into scene files.
    /// </summary>
    [InitializeOnLoad]
    public static class IronFaunaMenuSceneSetup
    {
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string ArenaScenePath = "Assets/Scenes/SampleScene.unity";
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
            Debug.Log("[IronFauna] MainMenu scene created and Build Settings updated (MainMenu first, then the arena).");
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

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == MenuScenePath);
            var ordered = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScenePath, true) };
            if (!scenes.Any(s => s.path == ArenaScenePath) && System.IO.File.Exists(ArenaScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ArenaScenePath, true));
            }
            ordered.AddRange(scenes);
            EditorBuildSettings.scenes = ordered.ToArray();
        }
    }
}
