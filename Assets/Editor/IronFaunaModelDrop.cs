using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace IronFauna.EditorTools
{
    /// <summary>
    /// Bulk model delivery: put a zip at &lt;project&gt;/ModelDrop/models.zip whose entries are project-relative paths
    /// under Assets/ (e.g. Assets/Resources/ClamshellMeshes/ThighFront_Set02_Bastion.fbx). On the next editor
    /// refresh (or IronFauna &gt; Import Model Drop) the files are unpacked over the existing ones and the zip is removed.
    /// </summary>
    [InitializeOnLoad]
    public static class IronFaunaModelDrop
    {
        private static string ZipPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ModelDrop", "models.zip"));

        static IronFaunaModelDrop()
        {
            EditorApplication.delayCall += () => Import(false);
        }

        [MenuItem("IronFauna/Import Model Drop")]
        public static void ImportFromMenu()
        {
            Import(true);
        }

        private static void Import(bool verbose)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string zipPath = ZipPath;
            if (!File.Exists(zipPath))
            {
                if (verbose) Debug.Log("[IronFauna] No model drop found at " + zipPath);
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            int count = 0;
            using (var stream = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // folder entry
                    string target = Path.GetFullPath(Path.Combine(projectRoot, entry.FullName));
                    if (!target.StartsWith(assetsRoot, System.StringComparison.OrdinalIgnoreCase)) continue; // only inside Assets/
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream source = entry.Open())
                    using (FileStream output = File.Create(target))
                    {
                        source.CopyTo(output);
                    }
                    count++;
                }
            }
            File.Delete(zipPath);
            AssetDatabase.Refresh();
            Debug.Log($"[IronFauna] Model drop imported: {count} files.");
        }
    }
}
