using UnityEditor;

namespace IronFauna.EditorTools
{
    /// <summary>
    /// Import settings for hand-modeled parts in Assets/Resources/ClamshellMeshes: readable meshes (the builder
    /// re-fits them into each slot's envelope), no rig/animation, normals from the file so hard edges stay hard.
    /// </summary>
    public class ClamshellModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/ClamshellMeshes/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.globalScale = 1f;
        }
    }
}
