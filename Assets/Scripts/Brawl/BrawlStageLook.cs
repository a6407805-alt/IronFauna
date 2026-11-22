using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFauna.Clamshell;
using IronFauna.Core;
using P = IronFauna.Core.IronFaunaPalette;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Stage fidelity pass (owner, 2026-11-18: "really work on the fidelity of the stages, make some nice levels").
    /// Everything is generated at match start, no art assets:
    ///  - Ground: a 1024² albedo (+ smoothness in alpha, + emission for molten seams / light rings) baked from the
    ///    terrain's own height and slope, with stage markings (deck plates, dish panels, lane paint, tyre tracks,
    ///    crevasses, trails), plus a tiling detail albedo/normal for close-up grain.
    ///  - Sky: an equirectangular panorama (gradient, sun, clouds, stars, planet, aurora, smoke) on Skybox/Panoramic,
    ///    trilight ambient, matching fog, a per-stage post-processing volume and a one-shot reflection probe.
    ///  - Backdrop: mountain / butte / ice-peak rings, a valley floor (lava sea, cloud sea, forest canopy ...),
    ///    a city skyline with lit windows, smokestacks, the dish's feed tripod, the colosseum wall, a tree wall.
    ///  - Set dressing and ambient particles: basalt columns, ice spires, ferns, tyre stacks, gantries, traffic
    ///    lights; dust, embers, snow, rain, leaves, sparkles; a few point lights where something glows.
    /// </summary>
    public static class BrawlStageLook
    {
        private enum Fx { Dust, Embers, Sparkle, Snow, Rain, Leaves }
        private enum SkyKind { Day, Smoke, Space, Sunset, Overcast, Night, Mist }

        private class Look
        {
            public Color zenith, horizon, below, sun, ambSky, ambEquator, ambGround, fog, filter;
            public float sunPitch, sunYaw, sunIntensity, fillIntensity = 0.5f, fogStart, fogEnd, clouds;
            public float exposure, contrast, saturation, bloom, bloomThreshold, vignette, temperature;
            public float groundMetal, groundSmooth = 1f;
            public SkyKind sky;
            public Fx fx;
        }

        private static Look Get(BrawlStageId stage)
        {
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                    return new Look
                    {
                        zenith = P.FoundrySkyZenith, horizon = P.FoundrySkyHorizon, below = P.FoundrySeam, sun = P.OrangeJointCore,
                        sunPitch = 24f, sunYaw = 140f, sunIntensity = 1.3f, fillIntensity = 0.35f,
                        ambSky = P.FoundrySkyHorizon * 0.7f, ambEquator = P.FoundryRust * 0.6f, ambGround = P.FoundryMolten * 0.3f,
                        fog = P.FoundrySkyHorizon * 0.75f, fogStart = 22f, fogEnd = 180f, clouds = 0.85f, filter = P.MesaSun,
                        exposure = 0.25f, contrast = 18f, saturation = 10f, bloom = 0.95f, bloomThreshold = 0.9f, vignette = 0.38f, temperature = 10f,
                        groundMetal = 0.55f, sky = SkyKind.Smoke, fx = Fx.Embers,
                    };
                case BrawlStageId.OrbitalDish:
                    return new Look
                    {
                        zenith = P.OrbitalSkyZenith, horizon = P.OrbitalSkyHorizon, below = P.OrbitalCloud, sun = P.KeyLightWarm,
                        sunPitch = 56f, sunYaw = -20f, sunIntensity = 1.8f, fillIntensity = 0.25f,
                        ambSky = P.Lerp(P.OrbitalSkyHorizon, P.OrbitalSkyZenith, 0.55f), ambEquator = P.OrbitalSkyHorizon * 0.7f, ambGround = P.OrbitalSeam * 0.6f,
                        fog = P.Lerp(P.OrbitalSkyHorizon, P.OrbitalCloud, 0.45f), fogStart = 70f, fogEnd = 420f, clouds = 0f, filter = P.StageWhite,
                        exposure = -0.45f, contrast = 20f, saturation = 4f, bloom = 0.35f, bloomThreshold = 1.2f, vignette = 0.3f, temperature = -6f,
                        groundMetal = 0.25f, sky = SkyKind.Space, fx = Fx.Sparkle,
                    };
                case BrawlStageId.ScrapyardColosseum:
                    return new Look
                    {
                        zenith = P.ScrapSkyZenith, horizon = P.ScrapSkyHorizon, below = P.ScrapOil, sun = P.ScrapSun,
                        sunPitch = 15f, sunYaw = 60f, sunIntensity = 2.2f, fillIntensity = 0.45f,
                        ambSky = P.Lerp(P.ScrapSkyZenith, P.ScrapSkyHorizon, 0.4f), ambEquator = P.ScrapSkyHorizon * 0.7f, ambGround = P.ScrapyardSand * 0.6f,
                        fog = P.Lerp(P.ScrapSkyHorizon, P.ScrapyardSand, 0.3f), fogStart = 35f, fogEnd = 240f, clouds = 0.45f, filter = P.ScrapSun,
                        exposure = 0.15f, contrast = 16f, saturation = 8f, bloom = 0.55f, bloomThreshold = 1f, vignette = 0.34f, temperature = 14f,
                        sky = SkyKind.Sunset, fx = Fx.Dust,
                    };
                case BrawlStageId.GlacierTrench:
                    return new Look
                    {
                        zenith = P.GlacierSkyZenith, horizon = P.GlacierSkyHorizon, below = P.GlacierIce, sun = P.OffWhitePanel,
                        sunPitch = 34f, sunYaw = -60f, sunIntensity = 1.25f, fillIntensity = 0.4f,
                        ambSky = P.GlacierSkyHorizon * 0.9f, ambEquator = P.GlacierSnow * 0.85f, ambGround = P.GlacierIce * 0.75f,
                        fog = P.GlacierSkyHorizon, fogStart = 25f, fogEnd = 200f, clouds = 0.7f, filter = P.StageWhite,
                        exposure = -0.4f, contrast = 12f, saturation = -4f, bloom = 0.3f, bloomThreshold = 1.3f, vignette = 0.3f, temperature = -14f,
                        sky = SkyKind.Overcast, fx = Fx.Snow,
                    };
                case BrawlStageId.NeoMetroCity:
                    return new Look
                    {
                        zenith = P.CitySkyZenith, horizon = P.CitySkyHorizon, below = P.CityAsphalt * 0.5f, sun = P.Lerp(P.AmbientSkyCool, P.CyanEnergy, 0.2f),
                        sunPitch = 42f, sunYaw = 120f, sunIntensity = 0.6f, fillIntensity = 0.3f,
                        ambSky = P.CitySkyHorizon, ambEquator = P.Lerp(P.CitySkyHorizon, P.CityNeonPink, 0.15f), ambGround = P.CityAsphalt * 0.5f,
                        fog = P.CitySkyHorizon * 0.8f, fogStart = 30f, fogEnd = 230f, clouds = 0.3f, filter = P.StageWhite,
                        exposure = 0.45f, contrast = 18f, saturation = 12f, bloom = 1.1f, bloomThreshold = 0.8f, vignette = 0.4f, temperature = -8f,
                        groundMetal = 0.1f, sky = SkyKind.Night, fx = Fx.Rain,
                    };
                case BrawlStageId.RedwoodForest:
                    return new Look
                    {
                        zenith = P.ForestSkyZenith, horizon = P.ForestSkyHorizon, below = P.ForestMoss * 0.5f, sun = P.ForestSun,
                        sunPitch = 32f, sunYaw = 30f, sunIntensity = 2f, fillIntensity = 0.5f,
                        ambSky = P.ForestSkyZenith, ambEquator = P.Lerp(P.ForestCanopy, P.ForestSkyHorizon, 0.5f), ambGround = P.ForestDirt,
                        fog = P.Lerp(P.ForestSkyHorizon, P.ForestCanopy, 0.15f), fogStart = 18f, fogEnd = 150f, clouds = 0.4f, filter = P.ForestSun,
                        exposure = 0.1f, contrast = 10f, saturation = 8f, bloom = 0.45f, bloomThreshold = 1.1f, vignette = 0.3f, temperature = 6f,
                        sky = SkyKind.Mist, fx = Fx.Leaves,
                    };
                default:
                    return new Look
                    {
                        zenith = P.MesaSkyZenith, horizon = P.MesaSkyHorizon, below = P.TerrainRockDark, sun = P.MesaSun,
                        sunPitch = 28f, sunYaw = -38f, sunIntensity = 2.4f, fillIntensity = 0.5f,
                        ambSky = P.Lerp(P.MesaSkyZenith, P.MesaSkyHorizon, 0.5f), ambEquator = P.MesaSkyHorizon * 0.8f, ambGround = P.TerrainDirt,
                        fog = P.Lerp(P.MesaSkyHorizon, P.MesaSkyZenith, 0.15f), fogStart = 45f, fogEnd = 300f, clouds = 0.35f, filter = P.MesaSun,
                        exposure = -0.15f, contrast = 12f, saturation = 6f, bloom = 0.35f, bloomThreshold = 1.2f, vignette = 0.28f, temperature = 8f,
                        sky = SkyKind.Day, fx = Fx.Dust,
                    };
            }
        }

        // ------------------------------------------------------------------ menu map previews
        private static readonly Dictionary<BrawlStageId, Texture2D> Previews = new Dictionary<BrawlStageId, Texture2D>();

        /// <summary>
        /// A small illustrated postcard of a stage for the map selector: its real sky gradient and sun, a landmark
        /// silhouette (buttes, smokestacks, planet + dish, colosseum stands, ice spires, skyline, pines) and its ground.
        /// Cached for the session; not released with the arena.
        /// </summary>
        public static Texture2D PreviewTexture(BrawlStageId stage)
        {
            if (Previews.TryGetValue(stage, out var cached) && cached != null) return cached;
            const int W = 384, H = 216;
            var look = Get(stage);
            BrawlTerrain.StageColors(stage, out Color ground, out Color rock, out Color rim, out _, out _);
            var px = new Color[W * H];
            var rng = new System.Random(1000 + (int)stage);
            float horizon = 0.38f;                          // fraction of height where the ground starts
            float sunX = 0.5f + Mathf.Clamp(look.sunYaw / 90f, -0.4f, 0.4f);
            float sunY = horizon + Mathf.Clamp01(look.sunPitch / 70f) * 0.5f;
            Color far = L(look.below, look.horizon, 0.35f) * 0.55f;
            far.a = 1f;
            Color near = rock * 0.6f;
            near.a = 1f;

            // landmark silhouette heights per column (fraction of height above the horizon)
            var skyline = new float[W];
            var lit = new bool[W];
            switch (stage)
            {
                case BrawlStageId.BasaltMesa:      // flat-topped buttes
                    for (int k = 0; k < 4; k++)
                    {
                        int c = rng.Next(20, W - 20), half = rng.Next(18, 46);
                        float top = 0.12f + (float)rng.NextDouble() * 0.16f;
                        for (int x = Mathf.Max(0, c - half - 8); x < Mathf.Min(W, c + half + 8); x++)
                        {
                            float edge = Mathf.Clamp01(1f - (Mathf.Abs(x - c) - half) / 8f);
                            skyline[x] = Mathf.Max(skyline[x], top * edge);
                        }
                    }
                    break;
                case BrawlStageId.FoundryCrucible:  // smokestacks + factory block
                    for (int x = 0; x < W; x++) skyline[x] = 0.07f;
                    for (int k = 0; k < 5; k++)
                    {
                        int c = rng.Next(10, W - 10), half = rng.Next(4, 9);
                        float top = 0.25f + (float)rng.NextDouble() * 0.22f;
                        for (int x = Mathf.Max(0, c - half); x < Mathf.Min(W, c + half); x++) skyline[x] = Mathf.Max(skyline[x], top);
                    }
                    break;
                case BrawlStageId.ScrapyardColosseum: // stepped stands all round
                    for (int x = 0; x < W; x++)
                    {
                        float t = Mathf.Abs(x / (float)W - 0.5f) * 2f;
                        skyline[x] = 0.06f + t * t * 0.16f + ((x / 12) % 2 == 0 ? 0.012f : 0f);
                        lit[x] = (x % 23) == 0;
                    }
                    break;
                case BrawlStageId.GlacierTrench:    // ice spires
                    for (int k = 0; k < 9; k++)
                    {
                        int c = rng.Next(0, W), half = rng.Next(10, 26);
                        float top = 0.12f + (float)rng.NextDouble() * 0.24f;
                        for (int x = Mathf.Max(0, c - half); x < Mathf.Min(W, c + half); x++)
                            skyline[x] = Mathf.Max(skyline[x], top * (1f - Mathf.Abs(x - c) / (float)half));
                    }
                    break;
                case BrawlStageId.NeoMetroCity:     // skyline with lit windows
                    for (int x = 0; x < W;)
                    {
                        int bw = rng.Next(14, 34);
                        float top = 0.10f + (float)rng.NextDouble() * 0.34f;
                        for (int i = 0; i < bw && x < W; i++, x++) { skyline[x] = top; lit[x] = (i % 4) == 1; }
                        x += rng.Next(2, 5);
                    }
                    break;
                case BrawlStageId.RedwoodForest:    // tall pines
                    for (int k = 0; k < 16; k++)
                    {
                        int c = rng.Next(0, W), half = rng.Next(8, 16);
                        float top = 0.22f + (float)rng.NextDouble() * 0.3f;
                        for (int x = Mathf.Max(0, c - half); x < Mathf.Min(W, c + half); x++)
                            skyline[x] = Mathf.Max(skyline[x], top * (1f - Mathf.Abs(x - c) / (float)half * 0.85f));
                    }
                    break;
                default:                             // Orbital Dish: low dish rim, planet in the sky
                    for (int x = 0; x < W; x++)
                    {
                        float t = (x / (float)W - 0.5f) * 2f;
                        skyline[x] = 0.04f + Mathf.Max(0f, 0.1f - Mathf.Abs(t) * 0.1f);
                    }
                    break;
            }

            for (int y = 0; y < H; y++)
            {
                float v = y / (float)(H - 1);
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1);
                    Color c;
                    if (v >= horizon)
                    {
                        float t = (v - horizon) / (1f - horizon);
                        c = L(look.horizon, look.zenith, Mathf.Pow(t, 0.7f));
                        // sun / planet
                        float dx = (u - sunX) * (W / (float)H), dy = v - sunY;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (stage == BrawlStageId.OrbitalDish)
                        {
                            float pd = Mathf.Sqrt(((u - 0.28f) * (W / (float)H)) * ((u - 0.28f) * (W / (float)H)) + (v - 0.86f) * (v - 0.86f));
                            if (pd < 0.2f) c = L(P.OrbitalPlanet, look.horizon, Mathf.Clamp01((pd - 0.12f) / 0.08f) * 0.6f);
                            if (rng.NextDouble() < 0.002) c = P.OffWhitePanel;   // stars
                        }
                        c = L(c, look.sun, Mathf.Clamp01(1f - d / 0.07f) * 0.9f + Mathf.Clamp01(1f - d / 0.3f) * 0.18f);
                        if (stage == BrawlStageId.GlacierTrench) c = L(c, P.GlacierAurora, Mathf.Clamp01(1f - Mathf.Abs(v - 0.78f - Mathf.Sin(u * 9f) * 0.04f) / 0.05f) * 0.35f);
                        // landmark silhouette
                        float sk = skyline[x];
                        if (v - horizon < sk)
                        {
                            c = L(far, look.horizon, 0.15f);
                            if (lit[x] && ((y / 5) % 2 == 0) && rng.NextDouble() < 0.6)
                                c = stage == BrawlStageId.NeoMetroCity ? P.CityWindow : P.HazardYellow;
                            if (stage == BrawlStageId.FoundryCrucible) c = L(c, P.FoundryMolten, Mathf.Clamp01(1f - (v - horizon) / 0.08f) * 0.45f);
                        }
                    }
                    else
                    {
                        // ground: arena island in perspective, falling away to the rim
                        float t = v / horizon;                    // 0 bottom .. 1 horizon
                        float width = Mathf.Lerp(0.95f, 0.55f, t);
                        bool island = Mathf.Abs(u - 0.5f) < width * 0.5f;
                        c = island ? L(ground, far, t * 0.55f) : L(look.below, far, 0.4f);
                        float n = (float)rng.NextDouble();
                        c = L(c, rock, n < 0.05f ? 0.4f : 0f) * (0.92f + n * 0.12f);
                        if (island && Mathf.Abs(Mathf.Abs(u - 0.5f) - width * 0.5f) < 0.006f) c = rim;
                        if (stage == BrawlStageId.FoundryCrucible && island && Mathf.Abs(Mathf.Sin(u * 40f + t * 9f)) < 0.04f) c = P.FoundryMolten;
                        if (stage == BrawlStageId.OrbitalDish && island && Mathf.Abs(Mathf.Sin(((u - 0.5f) / Mathf.Max(0.05f, width)) * 18f)) < 0.05f) c = P.CyanEnergy * 0.8f;
                    }
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { name = "MapPreview_" + stage, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(false, true);
            Previews[stage] = tex;
            return tex;
        }

        // Generated textures, materials and meshes of the current stage: released before the next one is built
        // (Tab cycles stages and every round rebuilds the arena).
        private static readonly List<Object> Owned = new List<Object>();

        private static T Own<T>(T o) where T : Object
        {
            if (o != null) Owned.Add(o);
            return o;
        }

        public static void Release()
        {
            foreach (var o in Owned) if (o != null) Object.Destroy(o);
            Owned.Clear();
        }

        // ================================================================== ground
        private const int GroundRes = 1024;
        private const int DetailRes = 256;

        /// <summary>Bakes the stage ground material from the terrain mesh (an (n+1)² grid spanning ±half, uv 0..1).</summary>
        public static Material GroundMaterial(BrawlStageId stage, Mesh ground, int n, float half, float top, float radius, int seed)
        {
            var look = Get(stage);
            Vector3[] v = ground.vertices;
            Vector3[] nm = ground.normals;
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 200f, oz = (float)rng.NextDouble() * 200f;
            bool glow = stage == BrawlStageId.FoundryCrucible || stage == BrawlStageId.OrbitalDish;

            var albedo = new Color32[GroundRes * GroundRes];
            Color32[] emission = glow ? new Color32[GroundRes * GroundRes] : null;
            for (int j = 0; j < GroundRes; j++)
            for (int i = 0; i < GroundRes; i++)
            {
                float u = (i + 0.5f) / GroundRes, w = (j + 0.5f) / GroundRes;
                float x = -half + 2f * half * u, z = -half + 2f * half * w;
                // bilinear sample of the terrain grid
                float gx = u * n, gz = w * n;
                int ix = Mathf.Min((int)gx, n - 1), iz = Mathf.Min((int)gz, n - 1);
                float fx = gx - ix, fz = gz - iz;
                int a = iz * (n + 1) + ix, b = a + 1, c = a + n + 1, d = c + 1;
                float h = Mathf.Lerp(Mathf.Lerp(v[a].y, v[b].y, fx), Mathf.Lerp(v[c].y, v[d].y, fx), fz) - top;
                float ny = Mathf.Lerp(Mathf.Lerp(nm[a].y, nm[b].y, fx), Mathf.Lerp(nm[c].y, nm[d].y, fx), fz);
                float slope = 1f - Mathf.Clamp01(ny);
                float r = Mathf.Sqrt(x * x + z * z) / radius;
                Sample(stage, x + ox, z + oz, x, z, h, slope, r, out Color col, out float smooth, out Color emit);
                col.a = smooth;
                albedo[j * GroundRes + i] = col;
                if (emission != null) emission[j * GroundRes + i] = emit;
            }

            var tex = new Texture2D(GroundRes, GroundRes, TextureFormat.RGBA32, true) { name = "IF_Ground_" + stage, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(albedo);
            tex.Apply(true, true);
            Own(tex);

            var mat = new Material(IronFaunaMaterials.SurfaceShader()) { name = "IF_GroundMat_" + stage, hideFlags = HideFlags.DontSave };
            mat.color = P.StageWhite;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", P.StageWhite);
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", look.groundMetal);
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", look.groundSmooth);
                mat.SetFloat("_SmoothnessTextureChannel", 1f);
                mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            if (mat.HasProperty("_DetailAlbedoMap"))
            {
                Detail(stage, out Texture2D dAlb, out Texture2D dNrm, out float tiles);
                Own(dAlb);
                Own(dNrm);
                mat.SetTexture("_DetailAlbedoMap", dAlb);
                mat.SetTexture("_DetailNormalMap", dNrm);
                mat.SetTextureScale("_DetailAlbedoMap", Vector2.one * tiles);
                mat.SetFloat("_DetailAlbedoMapScale", 1f);
                mat.SetFloat("_DetailNormalMapScale", 0.9f);
                mat.EnableKeyword("_DETAIL_MULX2");
            }
            if (emission != null && mat.HasProperty("_EmissionMap"))
            {
                var etex = new Texture2D(GroundRes, GroundRes, TextureFormat.RGBA32, true) { name = "IF_GroundGlow_" + stage, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                etex.SetPixels32(emission);
                etex.Apply(true, true);
                Own(etex);
                mat.SetTexture("_EmissionMap", etex);
                mat.SetColor("_EmissionColor", P.StageWhite * 2.4f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return Own(mat);
        }

        private static float N(float x, float z) => Mathf.PerlinNoise(x, z);

        private static float Fbm(float x, float z, int oct = 3)
        {
            float s = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < oct; o++)
            {
                s += Mathf.PerlinNoise(x, z) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f;
                z = z * 2.03f + 9.7f;
                amp *= 0.5f;
            }
            return s / norm;
        }

        private static float Ridge(float x, float z) => 1f - Mathf.Abs(Mathf.PerlinNoise(x, z) * 2f - 1f);

        private static float Hash(int x, int y, int s = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static float Ramp(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Color L(Color a, Color b, float t) => P.Lerp(a, b, Mathf.Clamp01(t));

        /// <summary>One texel of a stage's ground: (nx, nz) noise-offset world xz, (x, z) world xz, h height above the arena top.</summary>
        private static void Sample(BrawlStageId stage, float nx, float nz, float x, float z, float h, float slope, float r,
                                   out Color col, out float smooth, out Color emit)
        {
            emit = P.StageBlack;
            float grain = Hash(Mathf.FloorToInt(x * 13.5f), Mathf.FloorToInt(z * 13.5f));
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                {
                    const float plate = 2.4f;
                    float px = x / plate, pz = z / plate;
                    int cx = Mathf.FloorToInt(px), cz = Mathf.FloorToInt(pz);
                    float fx = px - cx, fz = pz - cz;
                    float edge = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fz, 1f - fz)) * plate;
                    col = P.FoundryPlate * (0.82f + 0.3f * Hash(cx, cz, 3));
                    float rust = Ramp(0.52f, 0.75f, Fbm(nx * 0.11f, nz * 0.11f));
                    col = L(col, P.FoundryRust, rust * 0.75f);
                    float scorch = Ramp(0.5f, 0.15f, r) * Ramp(0.35f, 0.65f, Fbm(nx * 0.3f + 40f, nz * 0.3f));
                    col = L(col, P.FoundrySeam, scorch * 0.7f);
                    smooth = 0.42f - rust * 0.25f + grain * 0.06f;
                    if (edge < 0.05f) { col = P.FoundrySeam; smooth = 0.1f; }
                    else if (edge < 0.16f && (Hash(cx, cz, 9) > 0.4f) && Mathf.Repeat((fx + fz) * plate * 2.2f, 1f) < 0.12f) col *= 0.7f;   // rivet rows
                    // the tier walls: hazard chevrons
                    float wall = Ramp(0.22f, 0.4f, slope);
                    if (wall > 0f)
                    {
                        Color stripe = Mathf.Repeat((x + z) * 0.6f, 1f) < 0.5f ? P.HazardYellow : P.FoundrySeam;
                        col = L(col, stripe * 0.85f, wall);
                        smooth = Mathf.Lerp(smooth, 0.35f, wall);
                    }
                    // molten seams: thin glowing cracks across the decks and six channels running out from the crucible
                    float crack = Ramp(0.955f, 0.985f, Ridge(nx * 0.07f, nz * 0.07f)) * Ramp(0.15f, 0.3f, r) * (1f - wall);
                    float ang = Mathf.Atan2(z, x) / (Mathf.PI * 2f) * 6f;
                    float channel = Ramp(0.45f, 0.2f, Mathf.Abs(Mathf.Repeat(ang + 0.5f, 1f) - 0.5f) * Mathf.Max(1f, r * 26f)) * Ramp(0.06f, 0.1f, r) * Ramp(0.37f, 0.33f, r);
                    float molten = Mathf.Max(crack, channel);
                    if (molten > 0f)
                    {
                        col = L(col, P.FoundrySeam, molten);
                        emit = P.FoundryMolten * molten * (0.75f + 0.25f * Fbm(nx * 0.8f, nz * 0.8f));
                        smooth = Mathf.Lerp(smooth, 0.8f, molten);
                    }
                    break;
                }
                case BrawlStageId.OrbitalDish:
                {
                    float rr = Mathf.Sqrt(x * x + z * z);
                    const float ring = 3f;
                    int ri = Mathf.FloorToInt(rr / ring);
                    float fr = rr / ring - ri;
                    int segs = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * (ri + 0.5f) * ring / 3.2f));
                    float a01 = Mathf.Repeat(Mathf.Atan2(z, x) / (Mathf.PI * 2f), 1f) * segs;
                    int si = Mathf.FloorToInt(a01);
                    float fs = a01 - si;
                    float arc = 2f * Mathf.PI * Mathf.Max(rr, 0.5f) / segs;
                    float edge = Mathf.Min(Mathf.Min(fr, 1f - fr) * ring, Mathf.Min(fs, 1f - fs) * arc);
                    col = P.OrbitalPanel * (0.68f + 0.1f * Hash(ri, si, 5));   // a mid-grey ceramic: pure white blew out under the high sun
                    float grime = Ramp(0.55f, 0.8f, Fbm(nx * 0.09f, nz * 0.09f));
                    col = L(col, P.OrbitalSeam, grime * 0.35f);
                    float scorch = Ramp(0.22f, 0.04f, r) * Ramp(0.4f, 0.7f, Fbm(nx * 0.4f, nz * 0.4f));
                    col = L(col, P.GunmetalFrame, scorch * 0.6f);
                    smooth = 0.6f - grime * 0.2f;
                    if (edge < 0.05f) { col = P.OrbitalSeam * 0.8f; smooth = 0.2f; }
                    // every third ring is a cyan light strip; small status lamps on some panels
                    if (ri % 3 == 2 && fr > 0.94f) { emit = P.CyanEnergy * 1.2f; col = P.DarkIron; smooth = 0.8f; }
                    else if (Hash(ri, si, 11) > 0.93f && Mathf.Abs(fs - 0.5f) * arc < 0.12f && Mathf.Abs(fr - 0.5f) * ring < 0.12f) emit = P.CyanEnergy * 0.8f;
                    break;
                }
                case BrawlStageId.ScrapyardColosseum:
                {
                    col = L(P.ScrapyardSand, P.MesaDust, Fbm(nx * 0.07f, nz * 0.07f) * 0.8f);
                    col = L(col, P.FoundryRust, Ramp(0.6f, 0.8f, Fbm(nx * 0.25f + 30f, nz * 0.25f)) * 0.5f);
                    col = L(col, P.TerrainRockDark, Ramp(0.25f, 0.5f, slope) * 0.7f);
                    smooth = 0.1f;
                    // two pairs of tyre tracks curving through the pit
                    for (int t = 0; t < 2; t++)
                    {
                        float path = (t == 0 ? z : x) - 7f * Mathf.Sin((t == 0 ? x : z) * 0.09f + t * 2f) - (t == 0 ? -4f : 6f);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float dTrack = Mathf.Abs(path - s * 0.9f);
                            if (dTrack < 0.32f)
                            {
                                float tread = Mathf.Repeat((t == 0 ? x : z) * 3f, 1f) < 0.45f ? 0.72f : 0.84f;
                                col *= Mathf.Lerp(tread, 1f, dTrack / 0.32f);
                            }
                        }
                    }
                    float oil = Ramp(0.7f, 0.76f, N(nx * 0.16f + 80f, nz * 0.16f));
                    col = L(col, P.ScrapOil, oil);
                    smooth = Mathf.Lerp(smooth, 0.65f, oil);
                    if (grain > 0.985f) col = L(col, P.CopperCoil, 0.6f);   // flakes of rust and copper
                    break;
                }
                case BrawlStageId.GlacierTrench:
                {
                    float ripple = Mathf.Sin(x * 2.1f + Fbm(nx * 0.2f, nz * 0.2f) * 9f) * 0.035f;
                    col = P.GlacierSnow * (0.8f + ripple + grain * 0.03f);   // snow below white so it keeps its shape
                    float ice = Ramp(0.14f, 0.32f, slope) + Ramp(-1.5f, -4.5f, h) * 0.6f;
                    col = L(col, P.GlacierIce, ice);
                    col = L(col, P.GlacierDeepIce, Ramp(0.4f, 0.65f, slope) * 0.8f);
                    float crack = Ramp(0.97f, 0.99f, Ridge(nx * 0.05f, nz * 0.12f));
                    col = L(col, P.GlacierCrack, crack * 0.85f);
                    smooth = Mathf.Lerp(0.3f + (grain > 0.97f ? 0.5f : 0f), 0.9f, Mathf.Clamp01(ice));
                    break;
                }
                case BrawlStageId.NeoMetroCity:
                {
                    bool avX = Mathf.Abs(z) < 6.5f, avZ = Mathf.Abs(x) < 6.5f;
                    if (avX || avZ)
                    {
                        col = P.CityAsphalt * (0.8f + 0.25f * Fbm(nx * 0.3f, nz * 0.3f)) * (0.94f + grain * 0.1f);
                        smooth = 0.3f;
                        bool inter = avX && avZ;
                        // centre dashes and edge lines
                        if (!inter && avX && Mathf.Abs(z) < 0.12f && Mathf.Repeat(x / 3f, 1f) < 0.55f) col = P.HazardYellow * 0.9f;
                        if (!inter && avZ && Mathf.Abs(x) < 0.12f && Mathf.Repeat(z / 3f, 1f) < 0.55f) col = P.HazardYellow * 0.9f;
                        if (!inter && avX && Mathf.Abs(Mathf.Abs(z) - 5.9f) < 0.08f) col = P.OffWhitePanel * 0.85f;
                        if (!inter && avZ && Mathf.Abs(Mathf.Abs(x) - 5.9f) < 0.08f) col = P.OffWhitePanel * 0.85f;
                        // zebra crossings around the intersection
                        if (avX && Mathf.Abs(x) > 6.8f && Mathf.Abs(x) < 8.8f && Mathf.Abs(z) < 5.6f && Mathf.Repeat(z, 1f) < 0.5f) col = P.OffWhitePanel * 0.85f;
                        if (avZ && Mathf.Abs(z) > 6.8f && Mathf.Abs(z) < 8.8f && Mathf.Abs(x) < 5.6f && Mathf.Repeat(x, 1f) < 0.5f) col = P.OffWhitePanel * 0.85f;
                        // manholes
                        float mx = Mathf.Repeat(x + 2f, 11f) - 5.5f, mz = Mathf.Repeat(z + 3f, 11f) - 5.5f;
                        float md = Mathf.Sqrt(mx * mx + mz * mz);
                        if (md < 0.55f) col = md > 0.48f ? P.DarkIron : P.GunmetalFrame * (Mathf.Repeat((mx + mz) * 5f, 1f) < 0.5f ? 0.8f : 1f);
                        // cracks and rain puddles
                        col = L(col, P.ShadowSlate, Ramp(0.975f, 0.99f, Ridge(nx * 0.18f, nz * 0.18f)) * 0.8f);
                        float puddle = Ramp(0.64f, 0.7f, N(nx * 0.12f + 60f, nz * 0.12f));
                        col *= Mathf.Lerp(1f, 0.6f, puddle);
                        smooth = Mathf.Lerp(smooth, 0.95f, puddle);
                    }
                    else
                    {
                        float kerb = Mathf.Min(Mathf.Abs(Mathf.Abs(x) - 6.5f), Mathf.Abs(Mathf.Abs(z) - 6.5f));
                        float tx = Mathf.Repeat(x / 1.5f, 1f), tz = Mathf.Repeat(z / 1.5f, 1f);
                        bool joint = Mathf.Min(Mathf.Min(tx, 1f - tx), Mathf.Min(tz, 1f - tz)) * 1.5f < 0.03f;
                        col = P.CitySidewalk * (0.9f + 0.15f * Hash(Mathf.FloorToInt(x / 1.5f), Mathf.FloorToInt(z / 1.5f), 2));
                        if (joint) col *= 0.7f;
                        if (kerb < 0.3f) col = P.OffWhitePanel * 0.6f;
                        col = L(col, P.ShadowSlate, Ramp(0.6f, 0.85f, Fbm(nx * 0.2f, nz * 0.2f)) * 0.25f);
                        smooth = 0.35f;
                    }
                    break;
                }
                case BrawlStageId.RedwoodForest:
                {
                    col = L(P.ForestMoss, P.ForestLitter, Ramp(0.42f, 0.68f, Fbm(nx * 0.09f, nz * 0.09f)));
                    col = L(col, P.ForestFern, Ramp(0.6f, 0.75f, Fbm(nx * 0.2f + 20f, nz * 0.2f)) * 0.7f);
                    float trail = Ramp(0.05f, 0.015f, Mathf.Abs(Fbm(nx * 0.035f, nz * 0.035f, 2) - 0.5f));
                    col = L(col, P.ForestDirt, trail * 0.9f);
                    col = L(col, P.TerrainRock, Ramp(0.3f, 0.5f, slope) * 0.6f);
                    if (grain > 0.93f) col = L(col, P.ForestBark, 0.45f);   // needles and cones
                    col *= 0.92f + grain * 0.12f;
                    smooth = 0.14f + trail * 0.05f;
                    break;
                }
                default:
                {
                    col = L(P.TerrainDirt, P.MesaDust, Ramp(0.45f, 0.72f, Fbm(nx * 0.08f, nz * 0.08f)) * 0.75f);
                    float grass = Ramp(0.52f, 0.68f, Fbm(nx * 0.15f + 50f, nz * 0.15f)) * (1f - Ramp(0.1f, 0.25f, slope));
                    col = L(col, P.MesaGrass, grass * 0.85f);
                    col = L(col, P.TerrainRock, Ramp(0.16f, 0.36f, slope));
                    col = L(col, P.TerrainRockDark, Ramp(0.42f, 0.7f, slope) * 0.85f);
                    float trail = Ramp(0.06f, 0.02f, Mathf.Abs(r - 0.32f - (Fbm(nx * 0.05f, nz * 0.05f, 2) - 0.5f) * 0.25f));
                    col = L(col, P.MesaDust, trail * 0.6f);
                    col *= 0.88f + grain * 0.22f;
                    smooth = 0.08f + Ramp(0.16f, 0.4f, slope) * 0.12f;
                    break;
                }
            }
            col.a = 1f;
            emit.a = 1f;
        }

        // ------------------------------------------------------------------ tiling detail (grain under the camera)
        private static float VNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wx(int k) => ((k % period) + period) % period;
            float a = Hash(Wx(x0), Wx(y0), seed), b = Hash(Wx(x0 + 1), Wx(y0), seed);
            float c = Hash(Wx(x0), Wx(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wx(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float DetailHeight(BrawlStageId stage, float u, float v)
        {
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                {
                    // diamond tread plate with scuffs
                    float a = Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 10f + v * 10f))), b = Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 10f - v * 10f)));
                    float tread = Ramp(0.9f, 0.98f, a) * Ramp(0.3f, 0.6f, b);
                    return 0.5f + tread * 0.25f + (VNoise(u, v, 48, 1) - 0.5f) * 0.25f;
                }
                case BrawlStageId.OrbitalDish:
                    return 0.5f + (VNoise(u, v * 0.05f, 96, 2) - 0.5f) * 0.15f + (VNoise(u, v, 64, 3) - 0.5f) * 0.1f;
                case BrawlStageId.GlacierTrench:
                    return 0.4f * VNoise(u, v, 8, 4) + 0.35f * VNoise(u, v, 24, 5) + 0.25f * VNoise(u, v, 96, 6);
                case BrawlStageId.NeoMetroCity:
                    return 0.25f * VNoise(u, v, 16, 7) + 0.4f * VNoise(u, v, 64, 8) + 0.35f * VNoise(u, v, 128, 9);
                case BrawlStageId.RedwoodForest:
                    return 0.45f * VNoise(u, v, 12, 10) + 0.35f * VNoise(u, v, 40, 11) + 0.2f * VNoise(u, v, 128, 12);
                case BrawlStageId.ScrapyardColosseum:
                    return 0.35f * (0.5f + 0.5f * Mathf.Sin((v + VNoise(u, v, 6, 13) * 0.3f) * Mathf.PI * 16f)) + 0.35f * VNoise(u, v, 48, 14) + 0.3f * VNoise(u, v, 128, 15);
                default:
                {
                    // grit and pebbles
                    float peb = Ramp(0.62f, 0.75f, VNoise(u, v, 20, 16));
                    return 0.3f * VNoise(u, v, 12, 17) + 0.3f * VNoise(u, v, 64, 18) + peb * 0.4f;
                }
            }
        }

        private static void Detail(BrawlStageId stage, out Texture2D albedo, out Texture2D normal, out float tiles)
        {
            tiles = stage == BrawlStageId.FoundryCrucible ? 32f : stage == BrawlStageId.NeoMetroCity ? 44f : 38f;
            var hgt = new float[DetailRes * DetailRes];
            for (int j = 0; j < DetailRes; j++)
            for (int i = 0; i < DetailRes; i++)
                hgt[j * DetailRes + i] = DetailHeight(stage, (float)i / DetailRes, (float)j / DetailRes);
            var alb = new Color32[hgt.Length];
            var nrm = new Color32[hgt.Length];
            float amp = stage == BrawlStageId.OrbitalDish ? 0.2f : 0.45f;
            float strength = stage == BrawlStageId.OrbitalDish ? 1.5f : stage == BrawlStageId.GlacierTrench ? 2.5f : 4f;
            for (int j = 0; j < DetailRes; j++)
            for (int i = 0; i < DetailRes; i++)
            {
                float hv = hgt[j * DetailRes + i];
                float g = Mathf.Clamp01(0.5f + (hv - 0.5f) * amp);
                alb[j * DetailRes + i] = P.Lerp(P.StageBlack, P.StageWhite, g);
                float hx = hgt[j * DetailRes + (i + 1) % DetailRes] - hgt[j * DetailRes + (i + DetailRes - 1) % DetailRes];
                float hy = hgt[((j + 1) % DetailRes) * DetailRes + i] - hgt[((j + DetailRes - 1) % DetailRes) * DetailRes + i];
                Vector3 n3 = new Vector3(-hx * strength, -hy * strength, 1f).normalized;
                var c = new Color(n3.x * 0.5f + 0.5f, n3.y * 0.5f + 0.5f, n3.z * 0.5f + 0.5f, 1f);
                nrm[j * DetailRes + i] = c;
            }
            albedo = new Texture2D(DetailRes, DetailRes, TextureFormat.RGBA32, true) { name = "IF_Detail_" + stage, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, hideFlags = HideFlags.DontSave };
            albedo.SetPixels32(alb);
            albedo.Apply(true, true);
            normal = new Texture2D(DetailRes, DetailRes, TextureFormat.RGBA32, true, true) { name = "IF_DetailN_" + stage, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, hideFlags = HideFlags.DontSave };
            normal.SetPixels32(nrm);
            normal.Apply(true, true);
        }

        // ================================================================== sky, light, fog, post
        /// <summary>Sun, fill, ambient, fog, skybox, post-processing and a reflection probe for the stage.</summary>
        public static void ApplyLighting(Transform arena, BrawlStageId stage, IEnumerable<Camera> cameras, float top)
        {
            var look = Get(stage);
            Quaternion sunRot = Quaternion.Euler(look.sunPitch, look.sunYaw, 0f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(arena, false);
            sunGo.transform.rotation = sunRot;
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = look.sun;
            sun.intensity = look.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            RenderSettings.sun = sun;

            var fillGo = new GameObject("SkyFill");
            fillGo.transform.SetParent(arena, false);
            fillGo.transform.rotation = Quaternion.Euler(50f, look.sunYaw + 180f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = L(look.zenith, look.horizon, 0.5f);
            fill.intensity = look.fillIntensity;
            fill.shadows = LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambSky;
            RenderSettings.ambientEquatorColor = look.ambEquator;
            RenderSettings.ambientGroundColor = look.ambGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.fog;
            RenderSettings.fogStartDistance = look.fogStart;
            RenderSettings.fogEndDistance = look.fogEnd;

            Material sky = SkyMaterial(stage, look, -(sunRot * Vector3.forward));
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.backgroundColor = look.horizon;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1400f);
            }

            // post: a stage grade on top of the studio volume
            var volGo = new GameObject("StageVolume");
            volGo.transform.SetParent(arena, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 50f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            Own(profile);
            vol.profile = profile;
            Ensure<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = Ensure<Bloom>(profile);
            bloom.intensity.Override(look.bloom);
            bloom.threshold.Override(look.bloomThreshold);
            bloom.scatter.Override(0.7f);
            var grade = Ensure<ColorAdjustments>(profile);
            grade.postExposure.Override(look.exposure);
            grade.contrast.Override(look.contrast);
            grade.saturation.Override(look.saturation);
            grade.colorFilter.Override(L(P.StageWhite, look.filter, 0.18f));
            var wb = Ensure<WhiteBalance>(profile);
            wb.temperature.Override(look.temperature);
            var vignette = Ensure<Vignette>(profile);
            vignette.intensity.Override(look.vignette);
            vignette.smoothness.Override(0.45f);
            Ensure<DepthOfField>(profile).mode.Override(DepthOfFieldMode.Off);
        }

        private static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
            component.active = true;
            return component;
        }

        /// <summary>Captures the finished stage once so metal armor reflects this sky and these surroundings.</summary>
        public static void CaptureReflections(Transform arena, float top)
        {
            var go = new GameObject("StageReflections");
            go.transform.SetParent(arena, false);
            go.transform.position = new Vector3(0f, top + 4f, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(160f, 80f, 160f);
            probe.resolution = 256;
            probe.farClipPlane = 900f;
            probe.importance = 10;
            probe.RenderProbe();
        }

        private static Material SkyMaterial(BrawlStageId stage, Look look, Vector3 toSun)
        {
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh == null) return null;
            const int W = 1024, H = 512;
            var px = new Color32[W * H];
            float ox = stage.GetHashCode() * 13.7f;
            Vector3 planetDir = Quaternion.Euler(-22f, 150f, 0f) * Vector3.forward;
            for (int j = 0; j < H; j++)
            {
                float lat = (1f - (j + 0.5f) / H) * Mathf.PI;
                float y = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                for (int i = 0; i < W; i++)
                {
                    float lon = (0.5f - (i + 0.5f) / W) * Mathf.PI * 2f;
                    var dir = new Vector3(sl * Mathf.Cos(lon), y, sl * Mathf.Sin(lon));
                    Color c;
                    if (y >= 0f)
                    {
                        float t = Mathf.Pow(y, look.sky == SkyKind.Space ? 0.35f : 0.55f);
                        c = L(look.horizon, look.zenith, t);
                    }
                    else c = L(look.horizon, look.below, Ramp(0f, 0.25f, -y));

                    float sd = Mathf.Max(0f, Vector3.Dot(dir, toSun));
                    float glow = Mathf.Pow(sd, 6f) * 0.35f + Mathf.Pow(sd, 48f) * 0.5f;
                    if (look.sky == SkyKind.Overcast || look.sky == SkyKind.Mist) glow *= 0.6f;
                    if (look.sky == SkyKind.Night) glow *= 0.35f;
                    c += look.sun * glow;
                    if (sd > 0.9994f && look.sky != SkyKind.Overcast) c = L(c, P.StageWhite, 0.95f);

                    if (y > 0.02f)
                    {
                        // clouds: noise projected onto a flat sky ceiling
                        float k = 1f / (y + 0.12f);
                        float cx = dir.x * k * 1.6f + ox, cz = dir.z * k * 1.6f;
                        if (look.clouds > 0f)
                        {
                            float cl = Fbm(cx, cz, 4);
                            float cover = Ramp(1f - look.clouds * 0.75f, 1.05f - look.clouds * 0.55f, cl) * Ramp(0.02f, 0.2f, y);
                            Color cloudCol;
                            switch (look.sky)
                            {
                                case SkyKind.Smoke: cloudCol = L(P.FoundrySkyZenith, P.FoundryMolten, Ramp(0.3f, 0f, y) * 0.35f); break;
                                case SkyKind.Sunset: cloudCol = L(P.ScrapSkyHorizon, P.CityNeonPink, 0.15f) * L(P.StageWhite, P.ScrapSkyZenith, y); break;
                                case SkyKind.Night: cloudCol = L(look.horizon, P.CityNeonPink, 0.12f) * 0.7f; break;
                                default: cloudCol = L(P.OffWhitePanel, look.horizon, 0.3f) * (0.85f + Mathf.Pow(sd, 3f) * 0.3f); break;
                            }
                            c = L(c, cloudCol, cover * 0.85f);
                        }
                        if (look.sky == SkyKind.Space || look.sky == SkyKind.Night)
                        {
                            float star = Hash(i, j, 77);
                            float thresh = look.sky == SkyKind.Space ? 0.994f : 0.998f;
                            if (star > thresh) c += P.StageWhite * ((star - thresh) / (1f - thresh)) * Ramp(0.05f, 0.4f, y);
                        }
                        if (look.sky == SkyKind.Overcast)
                        {
                            // faint aurora curtains
                            float band = Mathf.Sin(lon * 3f + Fbm(lon * 2f, y * 3f) * 4f) * 0.5f + 0.5f;
                            c = L(c, P.GlacierAurora, Ramp(0.7f, 1f, band) * Ramp(0.15f, 0.35f, y) * Ramp(0.75f, 0.45f, y) * 0.22f);
                        }
                    }
                    if (look.sky == SkyKind.Space)
                    {
                        float pd = Vector3.Dot(dir, planetDir);
                        if (pd > 0.985f)
                        {
                            float edge = Ramp(0.985f, 0.9875f, pd);
                            Vector3 local = dir - planetDir * pd;
                            float lit = Mathf.Clamp01(0.5f + Vector3.Dot(local.normalized, toSun) * 2.5f);
                            float bands = 0.85f + 0.15f * Mathf.Sin(Vector3.Dot(dir, Vector3.up) * 260f);
                            c = L(c, P.OrbitalPlanet * bands * Mathf.Lerp(0.15f, 1f, lit), edge);
                        }
                    }
                    if (look.sky == SkyKind.Smoke && y < 0.25f && y > -0.05f)
                        c += P.FoundryMolten * Ramp(0.25f, 0f, y) * 0.18f * (0.6f + 0.4f * N(lon * 6f, 3f));
                    c.a = 1f;
                    px[j * W + i] = c;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "IF_Sky_" + stage, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Own(tex);
            var mat = Own(new Material(sh) { name = "IF_SkyMat_" + stage, hideFlags = HideFlags.DontSave });
            mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Mapping")) mat.SetFloat("_Mapping", 1f);
            if (mat.HasProperty("_ImageType")) mat.SetFloat("_ImageType", 0f);
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1f);
            if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", P.StageGrey);
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            return mat;
        }

        // ================================================================== backdrop & set dressing
        /// <summary>Distant scenery, set dressing, ambient particles and stage lights (after the terrain and props exist).</summary>
        public static void Dress(Transform arena, BrawlStageId stage, float top, float radius, int seed)
        {
            var rng = new System.Random(seed * 31 + 7);
            var look = Get(stage);
            var root = new GameObject("StageDressing").transform;
            root.SetParent(arena, false);
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                    Floor(root, top - 26f, Emissive(P.FoundryMolten, 1.6f, P.FoundrySeam));
                    Stacks(root, top, rng);
                    Gantries(root, top, radius);
                    Railings(root, top, radius);
                    PointLights(root, top, radius, P.FoundryMolten, 6, 0.3f, 3.5f, 14f);
                    break;
                case BrawlStageId.OrbitalDish:
                    Floor(root, top - 28f, Lit(P.OrbitalCloud, 0.2f, 0f));
                    CloudPuffs(root, top - 28.5f, rng);
                    FeedTripod(root, top, radius);
                    Masts(root, top, radius, rng);
                    break;
                case BrawlStageId.ScrapyardColosseum:
                    Floor(root, top - 28f, IronFaunaMaterials.GetTerrain(P.ScrapOil));
                    MountainRing(root, top, 120f, 240f, 40f, 10f, 34f, false, P.Lerp(P.ScrapyardSand, P.ScrapSkyHorizon, 0.3f), rng);
                    Colosseum(root, top, radius);
                    TyreStacks(root, top, radius, rng);
                    Floodlights(root, top, radius);
                    break;
                case BrawlStageId.GlacierTrench:
                    Floor(root, top - 28f, Lit(P.GlacierIce, 0.85f, 0f));
                    MountainRing(root, top, 110f, 260f, 45f, 25f, 75f, true, P.GlacierSnow, rng);
                    IceSpires(root, top, radius, rng);
                    break;
                case BrawlStageId.NeoMetroCity:
                    Floor(root, top - 28f, IronFaunaMaterials.GetTerrain(P.CityAsphalt * 0.6f));
                    Skyline(root, top, rng);
                    TrafficLights(root, top);
                    break;
                case BrawlStageId.RedwoodForest:
                    Floor(root, top - 28f, IronFaunaMaterials.GetTerrain(P.ForestCanopy * 0.6f));
                    MountainRing(root, top, 140f, 280f, 40f, 15f, 45f, false, P.Lerp(P.ForestCanopy, P.ForestSkyHorizon, 0.4f), rng);
                    TreeWall(root, top, rng);
                    Undergrowth(root, top, radius, rng, true);
                    break;
                default:
                    Floor(root, top - 28f, IronFaunaMaterials.GetTerrain(P.Lerp(P.TerrainDirt, P.MesaDust, 0.5f)));
                    MesaRing(root, top, rng);
                    BasaltColumns(root, top, radius, rng);
                    Undergrowth(root, top, radius, rng, false);
                    break;
            }
            Particles(root, look.fx, top);
        }

        // ------------------------------------------------------------------ materials
        private static Material Lit(Color c, float smooth, float metal)
        {
            var m = Own(new Material(IronFaunaMaterials.SurfaceShader()) { hideFlags = HideFlags.DontSave });
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        private static Material Emissive(Color glow, float intensity, Color baseCol)
        {
            var m = Lit(baseCol, 0.6f, 0f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", glow * intensity);
            return m;
        }

        // ------------------------------------------------------------------ mesh helpers
        private class MeshBuilder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            /// <summary>A box with world-scaled UVs (1 uv unit = <paramref name="uvUnit"/> metres) for window grids.</summary>
            public void Box(Vector3 c, Vector3 s, float yaw, float uvUnit = 1f)
            {
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 h = s * 0.5f;
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down };
                foreach (var n in axes)
                {
                    Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n);
                    Vector3 w = Mathf.Abs(n.y) > 0.5f ? Vector3.Cross(n, Vector3.right) : Vector3.up;
                    float su = Mathf.Abs(Vector3.Dot(u, s)), sw = Mathf.Abs(Vector3.Dot(w, s));
                    Vector3 centre = Vector3.Scale(n, h);
                    int b = v.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (k == 1 || k == 2) ? 0.5f : -0.5f, e = k >= 2 ? 0.5f : -0.5f;
                        v.Add(c + q * (centre + u * (a * su) + w * (e * sw)));
                        uv.Add(new Vector2((a + 0.5f) * su / uvUnit, (e + 0.5f) * sw / uvUnit));
                    }
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                    t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }

            public void Add(Mesh m, Matrix4x4 tr)
            {
                int b = v.Count;
                var mv = m.vertices;
                var muv = m.uv;
                for (int k = 0; k < mv.Length; k++)
                {
                    v.Add(tr.MultiplyPoint3x4(mv[k]));
                    uv.Add(muv != null && muv.Length == mv.Length ? muv[k] : Vector2.zero);
                }
                foreach (int idx in m.triangles) t.Add(b + idx);
            }

            public GameObject Spawn(Transform parent, string name, Material mat, bool collider = false)
            {
                var mesh = Own(new Mesh { name = name, hideFlags = HideFlags.DontSave });
                if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                return go;
            }
        }

        private static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => Matrix4x4.TRS(p, q, s);

        private static float Ground(float x, float z, float fallback)
        {
            return IronFauna.Boxing.MechBoxer.TryGround(new Vector3(x, fallback + 30f, z), out float gy) ? gy : fallback;
        }

        private static float Rand(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        // ------------------------------------------------------------------ shared backdrop pieces
        private static void Floor(Transform root, float y, Material mat)
        {
            var mb = new MeshBuilder();
            const int seg = 64;
            const float R = 1100f;
            mb.v.Add(new Vector3(0f, y, 0f));
            mb.uv.Add(Vector2.zero);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                mb.v.Add(new Vector3(Mathf.Cos(a) * R, y, Mathf.Sin(a) * R));
                mb.uv.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 60f);
            }
            for (int i = 1; i <= seg; i++) { mb.t.Add(0); mb.t.Add(i + 1); mb.t.Add(i); }
            mb.Spawn(root, "ValleyFloor", mat);
        }

        /// <summary>A ring of distant ridges between rIn and rOut (peaks = sharp ice summits, else rolling hills).</summary>
        private static void MountainRing(Transform root, float top, float rIn, float rOut, float drop, float hMin, float hMax, bool peaks, Color col, System.Random rng)
        {
            var mb = new MeshBuilder();
            const int seg = 220, rings = 8;
            float ox = Rand(rng, 0f, 100f);
            for (int k = 0; k <= rings; k++)
            {
                float tr = k / (float)rings;
                float rr = Mathf.Lerp(rIn, rOut, tr);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                    float n = peaks ? Mathf.Pow(Mathf.Clamp01(Ridge(cx * 2.4f + ox, sz * 2.4f + tr * 1.5f)), 2.2f) : Fbm(cx * 1.8f + ox, sz * 1.8f + tr * 0.8f, 4);
                    float prof = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(tr * 1.15f))), 0.8f);   // sin(pi) is a hair below 0: Pow of it is NaN
                    float hgt = Mathf.Lerp(hMin, hMax, n) * prof;
                    float y = top - drop + (drop + hgt) * Mathf.Clamp01(prof * 1.6f);
                    mb.v.Add(new Vector3(cx * rr, y, sz * rr));
                    mb.uv.Add(new Vector2(a * 20f, tr * 6f));
                }
            }
            for (int k = 0; k < rings; k++)
            for (int i = 0; i < seg; i++)
            {
                int a = k * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                mb.t.Add(a); mb.t.Add(b); mb.t.Add(c);
                mb.t.Add(b); mb.t.Add(d); mb.t.Add(c);
            }
            mb.Spawn(root, peaks ? "IcePeaks" : "DistantRidges", peaks ? Lit(col, 0.5f, 0f) : IronFaunaMaterials.GetTerrain(col));
        }

        private static void Particles(Transform root, Fx fx, float top)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return;
            var go = new GameObject("Ambient_" + fx);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, top + (fx == Fx.Embers ? 1f : fx == Fx.Rain || fx == Fx.Snow || fx == Fx.Leaves ? 22f : 6f), 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.prewarm = true;
            main.loop = true;
            var em = ps.emission;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(80f, fx == Fx.Dust || fx == Fx.Sparkle ? 14f : 1f, 80f);
            var noise = ps.noise;
            var rot = ps.rotationOverLifetime;
            bool additive = fx == Fx.Embers || fx == Fx.Sparkle;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            switch (fx)
            {
                case Fx.Embers:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
                    main.startColor = new ParticleSystem.MinMaxGradient(P.FoundryMolten, P.HazardYellow);
                    main.gravityModifier = -0.12f;
                    main.maxParticles = 900;
                    em.rateOverTime = 160f;
                    shape.rotation = new Vector3(-90f, 0f, 0f);
                    noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.4f;
                    break;
                case Fx.Sparkle:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.startColor = new ParticleSystem.MinMaxGradient(P.CyanEnergy, P.OffWhitePanel);
                    main.maxParticles = 500;
                    em.rateOverTime = 60f;
                    noise.enabled = true; noise.strength = 0.3f; noise.frequency = 0.2f;
                    break;
                case Fx.Snow:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
                    main.startColor = P.WithAlpha(P.OffWhitePanel, 0.85f);
                    main.maxParticles = 6000;
                    em.rateOverTime = 450f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.25f;
                    var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(1.5f, 3f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    break;
                case Fx.Rain:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 1.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 16f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
                    main.startColor = P.WithAlpha(P.Lerp(P.AmbientSkyCool, P.OffWhitePanel, 0.5f), 0.35f);
                    main.maxParticles = 5000;
                    em.rateOverTime = 2600f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    rend.renderMode = ParticleSystemRenderMode.Stretch;
                    rend.velocityScale = 0.06f;
                    rend.lengthScale = 2f;
                    break;
                case Fx.Leaves:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
                    main.startColor = new ParticleSystem.MinMaxGradient(P.ForestLitter, P.Lerp(P.ForestCanopy, P.HazardYellow, 0.3f));
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.maxParticles = 800;
                    em.rateOverTime = 45f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    noise.enabled = true; noise.strength = 1.4f; noise.frequency = 0.3f;
                    rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                    break;
                default:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
                    main.startColor = P.WithAlpha(P.MesaDust, 0.4f);
                    main.maxParticles = 900;
                    em.rateOverTime = 80f;
                    noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.15f;
                    var dv = ps.velocityOverLifetime; dv.enabled = true; dv.x = new ParticleSystem.MinMaxCurve(0.4f, 1.1f); dv.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f); dv.z = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                    break;
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(P.StageWhite, 0f), new GradientColorKey(P.StageWhite, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var mat = Own(new Material(sh) { hideFlags = HideFlags.DontSave });
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) mat.EnableKeyword("_BLENDMODE_ADD");
            Texture2D dot = SoftDot();
            mat.SetTexture("_BaseMap", dot);
            mat.mainTexture = dot;
            rend.sharedMaterial = mat;
            ps.Play();
        }

        private static Texture2D _dot;

        private static Texture2D SoftDot()
        {
            if (_dot != null) return _dot;
            const int S = 32;
            _dot = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "IF_SoftDot", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[S * S];
            for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                float dx = (i + 0.5f) / S * 2f - 1f, dy = (j + 0.5f) / S * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[j * S + i] = P.WithAlpha(P.StageWhite, a * a);
            }
            _dot.SetPixels32(px);
            _dot.Apply(true, true);
            return _dot;
        }

        private static void PointLights(Transform root, float top, float radius, Color col, int count, float rFrac, float intensity, float range)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.5f) / count * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius * rFrac, z = Mathf.Sin(a) * radius * rFrac;
                var go = new GameObject("GlowLight");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(x, Ground(x, z, top) + 1.2f, z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = col;
                l.intensity = intensity;
                l.range = range;
                l.shadows = LightShadows.None;
            }
        }

        // ------------------------------------------------------------------ Basalt Mesa
        private static void MesaRing(Transform root, float top, System.Random rng)
        {
            // flat-topped buttes and a soft ridge line behind them
            MountainRing(root, top, 170f, 320f, 50f, 8f, 40f, false, P.Lerp(P.TerrainRock, P.MesaSkyHorizon, 0.35f), rng);
            var mb = new MeshBuilder();
            for (int i = 0; i < 14; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = Rand(rng, 85f, 150f);
                float w = Rand(rng, 14f, 40f), h = Rand(rng, 18f, 42f);
                Vector3 p = new Vector3(Mathf.Cos(a) * d, top - 34f + h * 0.5f, Mathf.Sin(a) * d);
                mb.Add(ClamshellMeshFactory.TaperedPrism(w * 0.5f, w * 0.38f, h + 34f, 9), TRS(p + Vector3.down * 17f, Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), Vector3.one));
                mb.Add(ClamshellMeshFactory.TaperedPrism(w * 0.39f, w * 0.36f, 2.5f, 9), TRS(p + Vector3.up * (h * 0.5f + 1.2f), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), Vector3.one));
            }
            mb.Spawn(root, "Buttes", IronFaunaMaterials.GetTerrain(P.Lerp(P.FoundryRust, P.TerrainRock, 0.55f)));
        }

        private static void BasaltColumns(Transform root, float top, float radius, System.Random rng)
        {
            var mb = new MeshBuilder();
            for (int c = 0; c < 6; c++)
            {
                float a = c / 6f * Mathf.PI * 2f + Rand(rng, -0.3f, 0.3f);
                float d = radius * Rand(rng, 0.72f, 0.86f);
                Vector2 centre = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                int cols = rng.Next(7, 14);
                for (int k = 0; k < cols; k++)
                {
                    Vector2 off = new Vector2(Rand(rng, -2.4f, 2.4f), Rand(rng, -2.4f, 2.4f));
                    float x = centre.x + off.x, z = centre.y + off.y;
                    float gy = Ground(x, z, top);
                    float h = Rand(rng, 1.5f, 6.5f) * (1f - off.magnitude / 4f) + 1f;
                    float rad = Rand(rng, 0.45f, 0.7f);
                    mb.Add(ClamshellMeshFactory.Prism(rad, h + 1f, 6), TRS(new Vector3(x, gy + h * 0.5f - 0.5f, z), Quaternion.Euler(Rand(rng, -4f, 4f), Rand(rng, 0f, 60f), Rand(rng, -4f, 4f)), Vector3.one));
                }
            }
            mb.Spawn(root, "BasaltColumns", IronFaunaMaterials.GetTerrain(P.TerrainRockDark), true);
        }

        /// <summary>Dry grass tufts (mesa) or ferns (forest): thin blades combined into one mesh, no collision.</summary>
        private static void Undergrowth(Transform root, float top, float radius, System.Random rng, bool ferns)
        {
            var mb = new MeshBuilder();
            Mesh blade = ClamshellMeshFactory.TaperedPrism(ferns ? 0.07f : 0.035f, 0.002f, ferns ? 0.9f : 0.55f, 3);
            int clumps = ferns ? 260 : 340;
            for (int c = 0; c < clumps; c++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = Mathf.Sqrt((float)rng.NextDouble()) * radius * 0.86f;
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                if (!ferns && (Fbm(x * 0.15f + 50f, z * 0.15f) < 0.5f)) continue;   // grass only grows in the patches the ground bake paints
                float gy = Ground(x, z, top);
                int blades = ferns ? 9 : 7;
                float scale = Rand(rng, 0.7f, 1.4f);
                for (int b = 0; b < blades; b++)
                {
                    float yaw = b / (float)blades * 360f + Rand(rng, -20f, 20f);
                    float tilt = ferns ? Rand(rng, 45f, 70f) : Rand(rng, 8f, 30f);
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                    float len = (ferns ? 0.9f : 0.55f) * scale;
                    mb.Add(blade, TRS(new Vector3(x, gy, z) + q * Vector3.up * len * 0.5f, q, Vector3.one * scale));
                }
            }
            if (mb.v.Count > 0) mb.Spawn(root, ferns ? "Ferns" : "DryGrass", Lit(ferns ? P.ForestFern : P.MesaGrass, 0.15f, 0f));
        }

        // ------------------------------------------------------------------ Foundry Crucible
        private static void Stacks(Transform root, float top, System.Random rng)
        {
            var body = new MeshBuilder();
            var glow = new MeshBuilder();
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f + Rand(rng, -0.15f, 0.15f), d = Rand(rng, 60f, 110f);
                float h = Rand(rng, 30f, 70f), r = Rand(rng, 2.5f, 5f);
                Vector3 b = new Vector3(Mathf.Cos(a) * d, top - 26f, Mathf.Sin(a) * d);
                body.Add(ClamshellMeshFactory.TaperedPrism(r * 1.15f, r * 0.85f, h, 14), TRS(b + Vector3.up * h * 0.5f, Quaternion.identity, Vector3.one));
                glow.Add(ClamshellMeshFactory.Prism(r * 0.9f, 0.8f, 14), TRS(b + Vector3.up * (h + 0.3f), Quaternion.identity, Vector3.one));
                if (i % 3 == 0)
                {
                    // furnace block with lit vents at the stack's foot
                    float w = Rand(rng, 10f, 18f);
                    body.Box(b + Vector3.up * 9f + new Vector3(0f, 0f, 0f), new Vector3(w, 18f + 26f, w * 0.7f), a * Mathf.Rad2Deg);
                    glow.Box(b + Vector3.up * 26f, new Vector3(w * 0.8f, 1.2f, w * 0.72f), a * Mathf.Rad2Deg);
                }
            }
            body.Spawn(root, "Smokestacks", IronFaunaMaterials.GetTerrain(P.Lerp(P.FoundrySeam, P.FoundryRust, 0.35f)));
            glow.Spawn(root, "FurnaceGlow", Emissive(P.FoundryMolten, 2.5f, P.FoundrySeam));
        }

        private static void Gantries(Transform root, float top, float radius)
        {
            var mb = new MeshBuilder();
            float y = top + 15f, span = radius * 1.05f;
            for (int g = 0; g < 2; g++)
            {
                float yaw = g * 90f + 20f;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                // twin girders, cross ties and legs that stand on the outer walkway
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Box(q * new Vector3(s * 1.6f, 0f, 0f) + Vector3.up * y, new Vector3(0.6f, 1.4f, span * 2f), yaw);
                    for (int e = -1; e <= 1; e += 2)
                    {
                        Vector3 foot = q * new Vector3(s * 1.6f, 0f, e * span);
                        float gy = Ground(foot.x, foot.z, top);
                        mb.Box(new Vector3(foot.x, (gy + y) * 0.5f, foot.z), new Vector3(0.7f, y - gy, 0.7f), yaw);
                    }
                }
                for (float k = -span; k <= span; k += 3f) mb.Box(q * new Vector3(0f, 0f, k) + Vector3.up * (y + 0.5f), new Vector3(3.8f, 0.25f, 0.25f), yaw);
                // the crane trolley and its hook block
                mb.Box(q * new Vector3(0f, 0f, g == 0 ? 6f : -9f) + Vector3.up * (y + 1.1f), new Vector3(4.4f, 1.6f, 3f), yaw);
                mb.Box(q * new Vector3(0f, 0f, g == 0 ? 6f : -9f) + Vector3.up * (y - 4f), new Vector3(0.15f, 8f, 0.15f), yaw);
                mb.Box(q * new Vector3(0f, 0f, g == 0 ? 6f : -9f) + Vector3.up * (y - 8.4f), new Vector3(1.2f, 1f, 1.2f), yaw);
            }
            mb.Spawn(root, "Gantries", Lit(P.Lerp(P.HazardYellow, P.FoundryRust, 0.45f), 0.35f, 0.5f));
        }

        private static void Railings(Transform root, float top, float radius)
        {
            // handrails along the walkway lips (decor only: no colliders so falls still happen)
            var mb = new MeshBuilder();
            foreach (float rf in new[] { 0.38f, 0.68f })
            {
                float rr = radius * rf + 0.6f;
                int posts = Mathf.RoundToInt(2f * Mathf.PI * rr / 2.2f);
                for (int i = 0; i < posts; i++)
                {
                    float a = i / (float)posts * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                    float gy = Ground(x, z, top);
                    mb.Box(new Vector3(x, gy + 0.55f, z), new Vector3(0.08f, 1.1f, 0.08f), -a * Mathf.Rad2Deg);
                    mb.Box(new Vector3(x, gy + 1.08f, z), new Vector3(0.07f, 0.07f, 2.25f), -a * Mathf.Rad2Deg);
                }
            }
            mb.Spawn(root, "Railings", Lit(P.HazardYellow, 0.45f, 0.1f));
        }

        // ------------------------------------------------------------------ Orbital Dish
        private static void CloudPuffs(Transform root, float y, System.Random rng)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < 70; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = Rand(rng, 60f, 420f);
                float s = Rand(rng, 12f, 40f);
                mb.Add(BrawlTerrain.RockMesh(rng.Next()), TRS(new Vector3(Mathf.Cos(a) * d, y + s * 0.1f, Mathf.Sin(a) * d), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), new Vector3(s * 2f, s * 0.6f, s * 1.6f)));
            }
            mb.Spawn(root, "CloudSea", Lit(P.OrbitalCloud, 0.05f, 0f));
        }

        private static void FeedTripod(Transform root, float top, float radius)
        {
            // three struts from the dish rim up to the receiver hanging over the centre: the stage's landmark
            var mb = new MeshBuilder();
            Vector3 apex = new Vector3(0f, top + 30f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = (i / 3f + 0.08f) * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius * 0.93f, z = Mathf.Sin(a) * radius * 0.93f;
                Vector3 foot = new Vector3(x, Ground(x, z, top), z);
                Vector3 dir = apex - foot;
                Quaternion q = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                mb.Add(ClamshellMeshFactory.TaperedPrism(0.9f, 0.45f, dir.magnitude, 8), TRS(foot + dir * 0.5f, q, Vector3.one));
                mb.Box(foot + Vector3.up * 0.6f, new Vector3(3f, 1.2f, 3f), a * Mathf.Rad2Deg);
            }
            mb.Add(ClamshellMeshFactory.TaperedPrism(2.2f, 3.4f, 5f, 16), TRS(apex + Vector3.down * 2f, Quaternion.identity, Vector3.one));
            mb.Add(ClamshellMeshFactory.TaperedPrism(1.1f, 2.2f, 2.5f, 16), TRS(apex + Vector3.down * 5.6f, Quaternion.identity, Vector3.one));
            mb.Spawn(root, "FeedTripod", Lit(P.OrbitalPanel, 0.55f, 0.35f));
            var lens = new MeshBuilder();
            lens.Add(ClamshellMeshFactory.Prism(1f, 0.3f, 16), TRS(apex + Vector3.down * 7f, Quaternion.identity, Vector3.one));
            lens.Spawn(root, "ReceiverLens", Emissive(P.CyanEnergy, 3f, P.DarkIron));
            var lgo = new GameObject("ReceiverLight");
            lgo.transform.SetParent(root, false);
            lgo.transform.position = apex + Vector3.down * 8f;
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 70f;
            l.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            l.color = P.CyanEnergy;
            l.intensity = 6f;
            l.range = 40f;
            l.shadows = LightShadows.None;
        }

        private static void Masts(Transform root, float top, float radius, System.Random rng)
        {
            var mb = new MeshBuilder();
            var lamps = new MeshBuilder();
            for (int i = 0; i < 10; i++)
            {
                float a = (i + 0.5f) / 10f * Mathf.PI * 2f;
                float d = radius * Rand(rng, 1.25f, 1.6f);
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                float h = Rand(rng, 18f, 34f);
                Vector3 b = new Vector3(x, top - 40f, z);
                mb.Add(ClamshellMeshFactory.TaperedPrism(1.4f, 0.5f, h + 40f, 6), TRS(b + Vector3.up * (h + 40f) * 0.5f, Quaternion.identity, Vector3.one));
                mb.Add(ClamshellMeshFactory.TaperedPrism(4f, 0.6f, 1.2f, 12), TRS(b + Vector3.up * (h + 34f), Quaternion.Euler(Rand(rng, -30f, 30f), 0f, Rand(rng, -30f, 30f)), Vector3.one));
                lamps.Add(ClamshellMeshFactory.Prism(0.35f, 0.5f, 8), TRS(b + Vector3.up * (h + 40.3f), Quaternion.identity, Vector3.one));
            }
            mb.Spawn(root, "RelayMasts", Lit(P.OrbitalSeam, 0.5f, 0.6f));
            lamps.Spawn(root, "MastBeacons", Emissive(P.CrimsonFront, 4f, P.DarkIron));
        }

        // ------------------------------------------------------------------ Scrapyard Colosseum
        private static void Colosseum(Transform root, float top, float radius)
        {
            // the stands: a ring wall of arched bays just past the cliff, with banners
            var wall = new MeshBuilder();
            var banners = new MeshBuilder();
            float rr = radius * 1.25f;
            const int bays = 40;
            for (int i = 0; i < bays; i++)
            {
                float a = i / (float)bays * Mathf.PI * 2f;
                float yaw = -a * Mathf.Rad2Deg + 90f;
                Vector3 c = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                float bw = 2f * Mathf.PI * rr / bays;
                float baseY = top - 30f;
                wall.Box(c + Vector3.up * (baseY + 22f), new Vector3(bw * 0.3f, 44f, 3f), yaw);              // pier
                wall.Box(c + Vector3.up * (top + 13f), new Vector3(bw, 1.6f, 3.2f), yaw);                    // arcade lintel
                wall.Box(c + Vector3.up * (top + 2f), new Vector3(bw, 1.2f, 3.2f), yaw);
                wall.Box(c + Vector3.up * (top - 9f), new Vector3(bw, 1.2f, 3.2f), yaw);
                // stepped seating behind
                for (int s = 0; s < 6; s++)
                {
                    Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (2.5f + s * 2.2f);
                    wall.Box(c + o + Vector3.up * (top + 14f + s * 1.6f), new Vector3(bw * 1.02f, 1.6f, 2.2f), yaw);
                }
                if (i % 4 == 0) banners.Box(c - new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.7f + Vector3.up * (top + 8f), new Vector3(bw * 0.5f, 8f, 0.1f), yaw);
            }
            wall.Spawn(root, "ColosseumStands", IronFaunaMaterials.GetTerrain(P.Lerp(P.ScrapyardSand, P.TerrainRock, 0.5f)));
            banners.Spawn(root, "Banners", Lit(P.CrimsonFront, 0.2f, 0f));
        }

        private static void TyreStacks(Transform root, float top, float radius, System.Random rng)
        {
            var tyres = new MeshBuilder();
            var hulks = new MeshBuilder();
            Mesh tyre = ClamshellMeshFactory.Prism(0.62f, 0.38f, 14);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f + Rand(rng, -0.2f, 0.2f), d = radius * Rand(rng, 0.78f, 0.88f);
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                float gy = Ground(x, z, top);
                if (i % 3 == 2)
                {
                    // a stripped car hulk on its side
                    hulks.Box(new Vector3(x, gy + 0.9f, z), new Vector3(4f, 1.8f, 2f), Rand(rng, 0f, 360f));
                    hulks.Box(new Vector3(x, gy + 2.1f, z), new Vector3(2.2f, 0.7f, 1.8f), Rand(rng, 0f, 360f));
                    continue;
                }
                int n = rng.Next(2, 6);
                for (int k = 0; k < n; k++)
                    tyres.Add(tyre, TRS(new Vector3(x + Rand(rng, -0.1f, 0.1f), gy + 0.19f + k * 0.38f, z + Rand(rng, -0.1f, 0.1f)), Quaternion.Euler(Rand(rng, -5f, 5f), Rand(rng, 0f, 360f), 0f), Vector3.one));
            }
            tyres.Spawn(root, "TyreStacks", Lit(P.ShadowSlate, 0.25f, 0f), true);
            hulks.Spawn(root, "CarHulks", Lit(P.Lerp(P.FoundryRust, P.CobaltFront, 0.25f), 0.3f, 0.4f), true);
        }

        private static void Floodlights(Transform root, float top, float radius)
        {
            var mb = new MeshBuilder();
            var lamps = new MeshBuilder();
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                float rr = radius * 1.15f;
                Vector3 b = new Vector3(Mathf.Cos(a) * rr, top - 30f, Mathf.Sin(a) * rr);
                mb.Box(b + Vector3.up * 27f, new Vector3(1.2f, 54f, 1.2f), 0f);
                Vector3 head = b + Vector3.up * 54f;
                lamps.Box(head, new Vector3(5f, 2.4f, 0.6f), -a * Mathf.Rad2Deg + 90f);
                var lgo = new GameObject("Floodlight");
                lgo.transform.SetParent(root, false);
                lgo.transform.position = head;
                lgo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, top, 0f) - head);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 55f;
                l.color = P.KeyLightWarm;
                l.intensity = 9f;
                l.range = 90f;
                l.shadows = LightShadows.None;
            }
            mb.Spawn(root, "FloodlightTowers", Lit(P.GunmetalFrame, 0.4f, 0.6f));
            lamps.Spawn(root, "FloodlightLamps", Emissive(P.KeyLightWarm, 4f, P.DarkIron));
        }

        // ------------------------------------------------------------------ Glacier Trench
        private static void IceSpires(Transform root, float top, float radius, System.Random rng)
        {
            var mb = new MeshBuilder();
            for (int c = 0; c < 8; c++)
            {
                float a = c / 8f * Mathf.PI * 2f + Rand(rng, -0.25f, 0.25f), d = radius * Rand(rng, 0.7f, 0.86f);
                float cx = Mathf.Cos(a) * d, cz = Mathf.Sin(a) * d;
                int n = rng.Next(3, 7);
                for (int k = 0; k < n; k++)
                {
                    float x = cx + Rand(rng, -2f, 2f), z = cz + Rand(rng, -2f, 2f);
                    float h = Rand(rng, 2f, 7.5f);
                    float gy = Ground(x, z, top);
                    Quaternion q = Quaternion.Euler(Rand(rng, -18f, 18f), Rand(rng, 0f, 360f), Rand(rng, -18f, 18f));
                    mb.Add(ClamshellMeshFactory.TaperedPrism(Rand(rng, 0.5f, 1.1f), 0.05f, h, 5), TRS(new Vector3(x, gy + h * 0.45f - 0.3f, z), q, Vector3.one));
                }
            }
            mb.Spawn(root, "IceSpires", Lit(P.Lerp(P.GlacierIce, P.GlacierDeepIce, 0.3f), 0.92f, 0f), true);
        }

        // ------------------------------------------------------------------ Neo-Metro City
        private static Material _windows;

        private static Material WindowMaterial()
        {
            if (_windows != null) return _windows;
            const int S = 128;   // 4 x 4 windows per tile, 1 tile = 12 m
            var alb = new Color32[S * S];
            var emi = new Color32[S * S];
            for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                int wx = i / 32, wy = j / 32;
                float fx = (i % 32) / 32f, fy = (j % 32) / 32f;
                bool glass = fx > 0.12f && fx < 0.88f && fy > 0.18f && fy < 0.82f;
                float lit = Hash(wx, wy, 41);
                alb[j * S + i] = glass ? P.Lerp(P.CityGlass, P.ShadowSlate, 0.6f) : P.Lerp(P.CityConcrete, P.ShadowSlate, 0.55f);
                Color e = P.StageBlack;
                if (glass && lit > 0.45f) e = (lit > 0.9f ? P.Lerp(P.CyanEnergy, P.CityWindow, 0.4f) : P.CityWindow) * Mathf.Lerp(0.35f, 1f, Hash(wx, wy, 42));
                e.a = 1f;
                emi[j * S + i] = e;
            }
            var at = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "IF_Windows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            at.SetPixels32(alb); at.Apply(true, true);
            var et = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "IF_WindowsGlow", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            et.SetPixels32(emi); et.Apply(true, true);
            _windows = Lit(P.StageWhite, 0.55f, 0.2f);
            Owned.Remove(_windows);   // cached for every city round
            _windows.SetTexture("_BaseMap", at);
            _windows.mainTexture = at;
            _windows.EnableKeyword("_EMISSION");
            _windows.SetTexture("_EmissionMap", et);
            _windows.SetColor("_EmissionColor", P.StageWhite * 1.6f);
            _windows.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return _windows;
        }

        private static void Skyline(Transform root, float top, System.Random rng)
        {
            var mb = new MeshBuilder();
            var signs = new MeshBuilder();
            var signsB = new MeshBuilder();
            for (int i = 0; i < 150; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = Rand(rng, 46f, 190f);
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                // keep the four avenues open as long vistas
                if (Mathf.Abs(x) < 8f || Mathf.Abs(z) < 8f) continue;
                float w = Rand(rng, 8f, 22f), dd = Rand(rng, 8f, 22f);
                float h = Rand(rng, 18f, 40f) + Mathf.Pow((float)rng.NextDouble(), 3f) * 90f * Mathf.InverseLerp(40f, 190f, d);
                float yaw = Mathf.Round(Rand(rng, 0f, 4f)) * 90f + Rand(rng, -3f, 3f);
                mb.Box(new Vector3(x, top - 30f + (h + 30f) * 0.5f, z), new Vector3(w, h + 30f, dd), yaw, 12f);
                if (h > 50f) mb.Box(new Vector3(x, top + h + 3f, z), new Vector3(w * 0.6f, 6f, dd * 0.6f), yaw, 12f);
                if (rng.NextDouble() < 0.35)
                {
                    Vector3 toC = new Vector3(-x, 0f, -z).normalized;
                    Vector3 face = new Vector3(x, top + Rand(rng, 6f, Mathf.Min(h, 30f)), z) + toC * (Mathf.Min(w, dd) * 0.5f + 0.2f);
                    (rng.NextDouble() < 0.5 ? signs : signsB).Box(face, new Vector3(Rand(rng, 4f, 9f), Rand(rng, 1.5f, 4f), 0.3f), Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg);
                }
            }
            mb.Spawn(root, "Skyline", WindowMaterial());
            signs.Spawn(root, "NeonSignsPink", Emissive(P.CityNeonPink, 3f, P.DarkIron));
            signsB.Spawn(root, "NeonSignsCyan", Emissive(P.CyanEnergy, 3f, P.DarkIron));
            // neon washes the wet streets
            float[] sx = { 14f, -14f, 14f, -14f }, sz = { 14f, 14f, -14f, -14f };
            for (int i = 0; i < 4; i++)
            {
                var lgo = new GameObject("NeonWash");
                lgo.transform.SetParent(root, false);
                lgo.transform.position = new Vector3(sx[i], top + 6f, sz[i]);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = i % 2 == 0 ? P.CityNeonPink : P.CyanEnergy;
                l.intensity = 5f;
                l.range = 22f;
                l.shadows = LightShadows.None;
            }
        }

        private static void TrafficLights(Transform root, float top)
        {
            var poles = new MeshBuilder();
            var red = new MeshBuilder();
            var green = new MeshBuilder();
            float[] px = { 7.2f, -7.2f, 7.2f, -7.2f }, pz = { 7.2f, 7.2f, -7.2f, -7.2f };
            for (int i = 0; i < 4; i++)
            {
                float gy = Ground(px[i], pz[i], top);
                Vector3 b = new Vector3(px[i], gy, pz[i]);
                float yaw = 45f + i * 90f;
                poles.Box(b + Vector3.up * 2.8f, new Vector3(0.22f, 5.6f, 0.22f), yaw);
                Vector3 arm = new Vector3(-Mathf.Sign(px[i]) * 2f, 5.4f, 0f);
                poles.Box(b + arm, new Vector3(4f, 0.18f, 0.18f), 0f);
                poles.Box(b + arm * 1.9f + Vector3.down * 0.6f, new Vector3(0.4f, 1.2f, 0.4f), 0f);
                (i % 2 == 0 ? red : green).Box(b + arm * 1.9f + Vector3.down * (i % 2 == 0 ? 0.25f : 0.95f) + Vector3.forward * 0.21f, new Vector3(0.24f, 0.24f, 0.04f), 0f);
            }
            poles.Spawn(root, "TrafficPoles", Lit(P.GunmetalFrame, 0.45f, 0.6f));
            red.Spawn(root, "SignalRed", Emissive(P.CrimsonFront, 5f, P.DarkIron));
            green.Spawn(root, "SignalGreen", Emissive(P.CyanEnergy, 5f, P.DarkIron));
        }

        // ------------------------------------------------------------------ Redwood Timberland
        private static void TreeWall(Transform root, float top, System.Random rng)
        {
            var trunks = new MeshBuilder();
            var crowns = new MeshBuilder();
            for (int i = 0; i < 260; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = Rand(rng, 40f, 130f);
                float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                float baseY = top - 30f + Mathf.InverseLerp(130f, 40f, d) * 6f;
                float h = Rand(rng, 40f, 70f);
                float r = Rand(rng, 0.9f, 1.8f);
                trunks.Add(ClamshellMeshFactory.TaperedPrism(r, r * 0.45f, h, 7), TRS(new Vector3(x, baseY + h * 0.5f, z), Quaternion.identity, Vector3.one));
                int tiers = 4;
                for (int t = 0; t < tiers; t++)
                {
                    float ty = baseY + h * Mathf.Lerp(0.5f, 0.95f, t / (float)(tiers - 1));
                    float cr = Mathf.Lerp(6f, 2.4f, t / (float)(tiers - 1)) * Rand(rng, 0.8f, 1.15f);
                    crowns.Add(ClamshellMeshFactory.TaperedPrism(cr, 0.3f, cr * 1.6f, 8), TRS(new Vector3(x, ty, z), Quaternion.Euler(0f, Rand(rng, 0f, 60f), 0f), Vector3.one));
                }
            }
            trunks.Spawn(root, "DistantTrunks", IronFaunaMaterials.GetTerrain(P.Lerp(P.ForestBark, P.ForestDirt, 0.4f)));
            crowns.Spawn(root, "DistantCanopy", IronFaunaMaterials.GetTerrain(P.Lerp(P.ForestCanopy, P.ForestFern, 0.5f)));
        }
    }
}
