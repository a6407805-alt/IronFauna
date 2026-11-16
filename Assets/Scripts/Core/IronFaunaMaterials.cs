using System.Collections.Generic;
using UnityEngine;

namespace IronFauna.Core
{
    /// <summary>
    /// Shared material cache. One material per palette colour (plus an emissive variant), so a mech built
    /// from hundreds of parts uses a handful of materials and batches well.
    /// In the editor (outside Play mode) materials are saved as assets under Assets/Materials so scenes
    /// built by editor tools keep their references; at runtime they are cached in memory only.
    /// </summary>
    public static class IronFaunaMaterials
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _gridFloor;

        public static Material Get(Color baseColor, bool emissive)
        {
            string hex = ColorUtility.ToHtmlStringRGB(baseColor);
            string key = emissive ? $"IF_Mat_{hex}_Emissive" : $"IF_Mat_{hex}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            Material mat = null;
#if UNITY_EDITOR
            string assetPath = $"Assets/Materials/{key}.mat";
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null && !Application.isPlaying)
            {
                if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Materials"))
                {
                    UnityEditor.AssetDatabase.CreateFolder("Assets", "Materials");
                }
                mat = CreateMaterial(key, baseColor, emissive);
                UnityEditor.AssetDatabase.CreateAsset(mat, assetPath);
            }
#endif
            if (mat == null)
            {
                mat = CreateMaterial(key, baseColor, emissive);
                mat.hideFlags = HideFlags.DontSave;
            }

            Cache[key] = mat;
            return mat;
        }

        public static void Apply(GameObject go, Color baseColor, bool emissive)
        {
            if (go != null && go.TryGetComponent<Renderer>(out var rend))
            {
                rend.sharedMaterial = Get(baseColor, emissive);
            }
        }

        /// <summary>Tan blueprint-grid floor material with a generated grid texture (runtime only).</summary>
        public static Material GridFloor(float tiles)
        {
            if (_gridFloor == null)
            {
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    name = "IF_BlueprintGrid",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Color c = IronFaunaPalette.FloorTan;
                        bool major = x < 3 || y < 3;
                        bool minor = (x % 64) < 1 || (y % 64) < 1;
                        if (major) c = IronFaunaPalette.GridLineDark;
                        else if (minor) c = IronFaunaPalette.GridLineLight;
                        pixels[y * size + x] = c;
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply(true);

                _gridFloor = CreateMaterial("IF_Mat_BlueprintGridFloor", IronFaunaPalette.UiNeutral, false);
                _gridFloor.hideFlags = HideFlags.DontSave;
                if (_gridFloor.HasProperty("_BaseMap")) _gridFloor.SetTexture("_BaseMap", tex);
                _gridFloor.mainTexture = tex;
                _gridFloor.SetFloat("_Smoothness", 0.15f);
            }

            _gridFloor.mainTextureScale = new Vector2(tiles, tiles);
            if (_gridFloor.HasProperty("_BaseMap")) _gridFloor.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            return _gridFloor;
        }

        private static Material CreateMaterial(string name, Color baseColor, bool emissive)
        {
            Material template = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            Material mat = template != null
                ? new Material(template)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.color = baseColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.38f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", baseColor == IronFaunaPalette.GunmetalFrame ? 0.55f : 0.15f);

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", baseColor * 2.5f);
            }
            return mat;
        }
    }
}
