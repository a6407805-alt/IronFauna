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

        private static Texture2D _wear;

        /// <summary>
        /// Tiling wear map for mech surfaces (parts carry box-projected UVs): RGB is a faint albedo mottle,
        /// alpha scales smoothness between about 0.62 and 1 in soft patches with finer grain on top.
        /// </summary>
        private static Texture2D WearMap()
        {
            if (_wear != null) return _wear;
            const int size = 256;
            _wear = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "IF_WearMap", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = 0.55f * TileNoise(u, v, 4) + 0.3f * TileNoise(u, v, 11) + 0.15f * TileNoise(u, v, 37);
                    byte albedo = (byte)Mathf.RoundToInt(Mathf.Lerp(218f, 255f, n));
                    byte smooth = (byte)Mathf.RoundToInt(Mathf.Lerp(158f, 255f, Mathf.SmoothStep(0f, 1f, n)));
                    px[y * size + x] = new Color32(albedo, albedo, albedo, smooth);
                }
            }
            _wear.SetPixels32(px);
            _wear.Apply(true);
            return _wear;
        }

        /// <summary>Value noise that repeats every <paramref name="period"/> cells, so the texture tiles.</summary>
        private static float TileNoise(float u, float v, int period)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0 % period, y0 % period, period), b = Hash((x0 + 1) % period, y0 % period, period);
            float c = Hash(x0 % period, (y0 + 1) % period, period), d = Hash((x0 + 1) % period, (y0 + 1) % period, period);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
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
            else if (baseColor == IronFaunaPalette.GunmetalFrame) { albedo = IronFaunaPalette.SteelCast; metallic = 0.7f; smoothness = 0.34f; }    // skeleton: dark matte cast metal
            else if (baseColor == IronFaunaPalette.DarkIron) { albedo = IronFaunaPalette.SteelBlack; metallic = 0.5f; smoothness = 0.24f; }       // recesses, conduits
            else if (baseColor == IronFaunaPalette.OffWhitePanel) { metallic = 0.9f; smoothness = 0.66f; }   // polished rods, pins, trim
            else if (baseColor == IronFaunaPalette.CopperCoil) { metallic = 1f; smoothness = 0.58f; }
            else if (baseColor == IronFaunaPalette.HazardYellow) { metallic = 0.1f; smoothness = 0.45f; }   // painted warning marks
            else { albedo = IronFaunaPalette.Lerp(baseColor, IronFaunaPalette.SteelDark, 0.38f); metallic = 0.45f; smoothness = 0.6f; }                                                  // armor: coated metal

            mat.color = albedo;
            mat.SetColor("_BaseColor", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (!emissive)
            {
                // Machined-metal breakup: the wear map tints the albedo slightly and its alpha varies the smoothness.
                Texture2D wear = WearMap();
                mat.SetTexture("_BaseMap", wear);
                mat.mainTexture = wear;
                mat.SetFloat("_SmoothnessTextureChannel", 1f);
                mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
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
