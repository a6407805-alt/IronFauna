using UnityEngine;
using UnityEngine.SceneManagement;
using IronFauna.Clamshell;
using IronFauna.Core;
using IronFauna.Gameplay;

namespace IronFauna.Menu
{
    /// <summary>
    /// Scene flow: MainMenu (title + garage) -> Deploy -> arena (SampleScene) with the saved mech,
    /// and Escape in the arena returns to the garage.
    /// </summary>
    public static class IronFaunaGameFlow
    {
        public const string MenuScene = "MainMenu";
        public const string ArenaScene = "SampleScene";
        public const string ShowcaseRootName = "IronFauna_ClamshellShowcases";

        /// <summary>Set when the player deploys; consumed when the arena finishes loading.</summary>
        public static bool DeployPending { get; private set; }

        /// <summary>When true the menu opens straight into the garage (returning from the arena).</summary>
        public static bool OpenGarageOnMenuLoad { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DeployPending = false;
            OpenGarageOnMenuLoad = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void Deploy(MechLoadout loadout)
        {
            MechLoadoutStore.Save(loadout);
            if (!Application.CanStreamedLevelBeLoaded(ArenaScene))
            {
                Debug.LogWarning($"[IronFauna] '{ArenaScene}' is not in Build Settings. Run IronFauna > Setup Menu Scene & Build Settings.");
                return;
            }
            DeployPending = true;
            SceneManager.LoadScene(ArenaScene);
        }

        public static void ReturnToGarage()
        {
            if (!Application.CanStreamedLevelBeLoaded(MenuScene))
            {
                Debug.LogWarning($"[IronFauna] '{MenuScene}' is not in Build Settings. Run IronFauna > Setup Menu Scene & Build Settings.");
                return;
            }
            OpenGarageOnMenuLoad = true;
            SceneManager.LoadScene(MenuScene);
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!DeployPending || scene.name != ArenaScene) return;
            DeployPending = false;

            // The arena's showcase mechs make way for the player's own build.
            var showcase = GameObject.Find(ShowcaseRootName);
            if (showcase != null) showcase.SetActive(false);

            // The arena was lit for the old cel-shaded look: its pale floor blows out to white under the realistic
            // materials and gives no sense of speed. Swap in the dark panelled hangar deck (4 m panels).
            if (IronFaunaMaterials.Realistic)
            {
                var floor = GameObject.Find("ArenaDeckFloor");
                if (floor != null && floor.TryGetComponent<Renderer>(out var floorRenderer))
                {
                    float size = Mathf.Max(floor.transform.lossyScale.x, floor.transform.lossyScale.z);
                    if (floorRenderer.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) size *= Mathf.Max(mf.sharedMesh.bounds.size.x, mf.sharedMesh.bounds.size.z);
                    floorRenderer.sharedMaterial = IronFaunaMaterials.StudioFloor(Mathf.Max(1f, size / 4f));
                }
            }

            var go = new GameObject("PlayerMech");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0f, -2.5f), Quaternion.identity);
            var builder = go.AddComponent<MechClamshellBuilder>();
            builder.buildOnStart = false;
            builder.ApplyLoadout(MechLoadoutStore.Load());
            builder.RebuildMech();

            go.AddComponent<ArenaReturnToGarage>();

            // Pilot it: procedural animation + combat (MechRobotOperator), mass-based WASD movement, and the
            // first / third-person camera on the scene's main camera.
            var op = go.AddComponent<MechRobotOperator>();
            op.aiWingmanActive = false;
            op.externalLocomotion = true;
            var pilot = go.AddComponent<MechPilotController>();
            Camera cam = Camera.main;
            if (cam != null)
            {
                var pilotCam = cam.GetComponent<MechPilotCamera>();
                if (pilotCam == null) pilotCam = cam.gameObject.AddComponent<MechPilotCamera>();
                pilotCam.target = pilot;
                op.aimPointOverride = pilotCam.AimPoint;
            }
        }
    }
}
