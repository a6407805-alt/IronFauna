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
        private static Material _studioFloor;

        /// <summary>
        /// Realistic look (default): physically based metal and coated paint on URP Lit, lit by the hangar studio.
        /// Set to false before any material is requested to get the older cel-shaded toon look back.
        /// </summary>
        public static bool Realistic = true;

        public static Material Get(Color baseColor, bool emissive)
        {
            string hex = ColorUtility.ToHtmlStringRGB(baseColor);
            string key = emissive ? $"IF_Mat_{hex}_Emissive" : $"IF_Mat_{hex}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            Material mat = null;
#if UNITY_EDITOR
            string assetPath = $"Assets/Materials/{key}.mat";
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            Shader surface = SurfaceShader();
            if (mat != null && surface != null)
            {
                // Bring saved materials onto the current look (shader and surface values).
                if (mat.shader != surface) mat.shader = surface;
                Configure(mat, baseColor, emissive);
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mat);
            }
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

                // The floor needs a texture, so it stays on URP Lit rather than the toon shader.
                Material litTemplate = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial
                    : null;
                _gridFloor = litTemplate != null ? new Material(litTemplate) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _gridFloor.name = "IF_Mat_BlueprintGridFloor";
                // Slightly dimmed so the sunlit tan floor stays below the bloom threshold.
                Color floorTint = IronFaunaPalette.Lerp(IronFaunaPalette.UiNeutral, IronFaunaPalette.AmbientEquatorMid, 0.35f);
                _gridFloor.color = floorTint;
                if (_gridFloor.HasProperty("_BaseColor")) _gridFloor.SetColor("_BaseColor", floorTint);
                _gridFloor.hideFlags = HideFlags.DontSave;
                if (_gridFloor.HasProperty("_BaseMap")) _gridFloor.SetTexture("_BaseMap", tex);
                _gridFloor.mainTexture = tex;
                _gridFloor.SetFloat("_Smoothness", 0.15f);
            }

            _gridFloor.mainTextureScale = new Vector2(tiles, tiles);
            if (_gridFloor.HasProperty("_BaseMap")) _gridFloor.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            return _gridFloor;
        }

        /// <summary>Dark polished hangar deck with faint panel seams (realistic look).</summary>
        public static Material StudioFloor(float tiles)
        {
            if (_studioFloor == null)
            {
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
                {
                    name = "IF_HangarDeck",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 8,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool seam = x < 2 || y < 2;
                        pixels[y * size + x] = seam ? IronFaunaPalette.StudioFloorLine : IronFaunaPalette.StudioFloor;
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply(true);

                _studioFloor = new Material(LitShader()) { name = "IF_Mat_HangarDeck", hideFlags = HideFlags.DontSave };
                _studioFloor.color = IronFaunaPalette.UiNeutral;
                if (_studioFloor.HasProperty("_BaseColor")) _studioFloor.SetColor("_BaseColor", IronFaunaPalette.UiNeutral);
                if (_studioFloor.HasProperty("_BaseMap")) _studioFloor.SetTexture("_BaseMap", tex);
                _studioFloor.mainTexture = tex;
                _studioFloor.SetFloat("_Metallic", 0f);
                _studioFloor.SetFloat("_Smoothness", 0.3f);
                MakeMatte(_studioFloor);
            }
            _studioFloor.mainTextureScale = new Vector2(tiles, tiles);
            if (_studioFloor.HasProperty("_BaseMap")) _studioFloor.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            return _studioFloor;
        }

        /// <summary>Dark matte steel for stage dressing (turntable deck) that should not out-shine the mech.</summary>
        public static Material StudioDeck()
        {
            if (_studioDeck == null)
            {
                _studioDeck = new Material(LitShader()) { name = "IF_Mat_StudioDeck", hideFlags = HideFlags.DontSave };
                _studioDeck.color = IronFaunaPalette.SteelDark;
                if (_studioDeck.HasProperty("_BaseColor")) _studioDeck.SetColor("_BaseColor", IronFaunaPalette.SteelDark);
                _studioDeck.SetFloat("_Metallic", 0.2f);
                _studioDeck.SetFloat("_Smoothness", 0.3f);
                MakeMatte(_studioDeck);
            }
            return _studioDeck;
        }

        /// <summary>
        /// Large flat surfaces mirror the low rim light and the studio softboxes straight into the camera as a white
        /// glare, so the deck and floor take diffuse light and shadows only.
        /// </summary>
        private static void MakeMatte(Material mat)
        {
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        private static Material _studioDeck;
        private static Shader _lit;

        private static Shader LitShader()
        {
            if (_lit != null) return _lit;
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                Material template = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial;
                if (template != null) _lit = template.shader;
            }
            return _lit;
        }

        /// <summary>Shader for mech parts in the current look.</summary>
        public static Shader SurfaceShader()
        {
            if (Realistic)
            {
                Shader lit = LitShader();
                if (lit != null) return lit;
            }
            return ToonShader();
        }

        private const string ToonShaderName = "IronFauna/Toon";
        private const string ToonTemplateResource = "IF_ToonTemplate";
        private static Shader _toon;

        /// <summary>The toon shader (found by name in the editor, or via the Resources template material in builds).</summary>
        public static Shader ToonShader()
        {
            if (_toon != null) return _toon;
            _toon = Shader.Find(ToonShaderName);
            if (_toon == null)
            {
                var template = Resources.Load<Material>(ToonTemplateResource);
                if (template != null) _toon = template.shader;
            }
            return _toon;
        }

        private static Material CreateMaterial(string name, Color baseColor, bool emissive)
        {
            Material mat;
            Shader toon = SurfaceShader();
            if (toon != null)
            {
                mat = new Material(toon);
            }
            else
            {
                Material template = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial
                    : null;
                mat = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
            mat.name = name;
            Configure(mat, baseColor, emissive);
            return mat;
        }

        /// <summary>Physically based surface: what kind of metal or coating a palette colour stands for.</summary>
        private static void ConfigureRealistic(Material mat, Color baseColor, bool emissive)
        {
            Color albedo = baseColor;
            float metallic, smoothness;
            if (emissive) { metallic = 0f; smoothness = 0.6f; }
            else if (baseColor == IronFaunaPalette.GunmetalFrame) { albedo = IronFaunaPalette.SteelBare; metallic = 0.9f; smoothness = 0.48f; }
            else if (baseColor == IronFaunaPalette.DarkIron) { albedo = IronFaunaPalette.SteelDark; metallic = 0.85f; smoothness = 0.38f; }
            else if (baseColor == IronFaunaPalette.OffWhitePanel) { metallic = 0.9f; smoothness = 0.66f; }   // polished rods, pins, trim
            else if (baseColor == IronFaunaPalette.CopperCoil) { metallic = 1f; smoothness = 0.58f; }
            else if (baseColor == IronFaunaPalette.HazardYellow) { metallic = 0.1f; smoothness = 0.45f; }   // painted warning marks
            else { albedo = IronFaunaPalette.Lerp(baseColor, IronFaunaPalette.SteelDark, 0.38f); metallic = 0.5f; smoothness = 0.42f; }                                                  // armor: coated metal

            mat.color = albedo;
            mat.SetColor("_BaseColor", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", baseColor * 3.2f);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", baseColor * 0f);
            }
        }

        private static void Configure(Material mat, Color baseColor, bool emissive)
        {
            if (Realistic && mat.shader == LitShader())
            {
                ConfigureRealistic(mat, baseColor, emissive);
                return;
            }
            mat.color = baseColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", IronFaunaPalette.ShadowSlate);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.38f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", baseColor == IronFaunaPalette.GunmetalFrame ? 0.55f : 0.15f);

            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", baseColor * 2.2f);
                // Glowing parts read as light sources: no shade band, thin outline.
                if (mat.HasProperty("_ShadowStrength")) mat.SetFloat("_ShadowStrength", 0f);
            }
            else if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", baseColor * 0f);
            }
        }
    }
}
