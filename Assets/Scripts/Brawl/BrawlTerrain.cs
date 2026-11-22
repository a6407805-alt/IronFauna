using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;
using IronFauna.Core;

namespace IronFauna.Brawl
{
    public enum BrawlStageId
    {
        BasaltMesa = 0,
        FoundryCrucible = 1,
        OrbitalDish = 2,
        ScrapyardColosseum = 3,
        GlacierTrench = 4,
        NeoMetroCity = 5,
        RedwoodForest = 6,
        WrestlingRing = 7,
    }

    /// <summary>
    /// Procedural multi-stage brawl builder: generates terrain, lighting colors, and destructible environment cover
    /// for 7 distinct arenas (Basalt Mesa, Foundry Crucible, Orbital Dish, Scrapyard Colosseum, Glacier Trench,
    /// Neo-Metro City, Redwood Timberland).
    /// All static terrain colliders work seamlessly with MechBoxer.followGround and the walk cycle's foot probes.
    /// </summary>
    public static class BrawlTerrain
    {
        public const int StageCount = 8;

        public static string StageName(BrawlStageId stage)
        {
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible: return "FOUNDRY CRUCIBLE";
                case BrawlStageId.OrbitalDish: return "ORBITAL DISH";
                case BrawlStageId.ScrapyardColosseum: return "SCRAPYARD COLOSSEUM";
                case BrawlStageId.GlacierTrench: return "GLACIER TRENCH";
                case BrawlStageId.NeoMetroCity: return "NEO-METRO CITY";
                case BrawlStageId.RedwoodForest: return "REDWOOD TIMBERLAND";
                case BrawlStageId.WrestlingRing: return "THE SQUARED CIRCLE";
                default: return "BASALT MESA";
            }
        }

        public static void StageColors(BrawlStageId stage, out Color groundColor, out Color rockColor, out Color rimColor, out Color skyColor, out Color sunColor)
        {
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                    groundColor = IronFaunaPalette.FoundryDeck;
                    rockColor = IronFaunaPalette.FoundryRust;
                    rimColor = IronFaunaPalette.DarkIron;
                    skyColor = IronFaunaPalette.FoundrySky;
                    sunColor = IronFaunaPalette.OrangeJointCore;
                    break;
                case BrawlStageId.OrbitalDish:
                    groundColor = IronFaunaPalette.OrbitalDeck;
                    rockColor = IronFaunaPalette.GunmetalFrame;
                    rimColor = IronFaunaPalette.DarkIron;
                    skyColor = IronFaunaPalette.OrbitalSky;
                    sunColor = IronFaunaPalette.KeyLightWarm;
                    break;
                case BrawlStageId.ScrapyardColosseum:
                    groundColor = IronFaunaPalette.ScrapyardSand;
                    rockColor = IronFaunaPalette.FoundryRust;
                    rimColor = IronFaunaPalette.TerrainRockDark;
                    skyColor = IronFaunaPalette.ScrapyardSky;
                    sunColor = IronFaunaPalette.SunWarm;
                    break;
                case BrawlStageId.GlacierTrench:
                    groundColor = IronFaunaPalette.GlacierSnow;
                    rockColor = IronFaunaPalette.GlacierIce;
                    rimColor = IronFaunaPalette.AmbientSkyCool;
                    skyColor = IronFaunaPalette.GlacierSky;
                    sunColor = IronFaunaPalette.OffWhitePanel;
                    break;
                case BrawlStageId.NeoMetroCity:
                    groundColor = IronFaunaPalette.CityAsphalt;
                    rockColor = IronFaunaPalette.CityConcrete;
                    rimColor = IronFaunaPalette.DarkIron;
                    skyColor = IronFaunaPalette.CitySky;
                    sunColor = IronFaunaPalette.KeyLightWarm;
                    break;
                case BrawlStageId.RedwoodForest:
                    groundColor = IronFaunaPalette.ForestMoss;
                    rockColor = IronFaunaPalette.TerrainRock;
                    rimColor = IronFaunaPalette.ForestBark;
                    skyColor = IronFaunaPalette.ForestSky;
                    sunColor = IronFaunaPalette.SunWarm;
                    break;
                case BrawlStageId.WrestlingRing:
                    groundColor = IronFaunaPalette.DarkIron;
                    rockColor = IronFaunaPalette.GunmetalFrame;
                    rimColor = IronFaunaPalette.ShadowSlate;
                    skyColor = IronFaunaPalette.ScrapyardSky;
                    sunColor = IronFaunaPalette.SunWarm;
                    break;
                default:
                    groundColor = IronFaunaPalette.TerrainDirt;
                    rockColor = IronFaunaPalette.TerrainRock;
                    rimColor = IronFaunaPalette.TerrainRockDark;
                    skyColor = IronFaunaPalette.SkyHaze;
                    sunColor = IronFaunaPalette.SunWarm;
                    break;
            }
        }

        public static Transform Build(Transform parent, float top, float radius, int seed, BrawlStageId stage = BrawlStageId.BasaltMesa)
        {
            var root = new GameObject("BrawlTerrain_" + stage).transform;
            root.SetParent(parent, false);
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
            StageColors(stage, out Color groundCol, out Color rockCol, out Color rimCol, out _, out _);

            // ---------------- ground mesh
            const int n = 120;
            float half = radius * 1.12f;
            var verts = new Vector3[(n + 1) * (n + 1)];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                float x = -half + 2f * half * i / n;
                float z = -half + 2f * half * j / n;
                float y = Height(x, z, top, radius, ox, oz, stage);
                verts[j * (n + 1) + i] = new Vector3(x, y, z);
                uvs[j * (n + 1) + i] = new Vector2((float)i / n, (float)j / n);   // 0..1: the baked stage ground map
            }
            var tris = new int[n * n * 6];
            int t = 0;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "BrawlGround", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = ground.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BrawlStageLook.GroundMaterial(stage, mesh, n, half, top, radius, seed);
            var groundCollider = ground.AddComponent<MeshCollider>();
            groundCollider.sharedMesh = mesh;
            ActiveGround = groundCollider;

            // ---------------- boulders and outcrops (static, half buried; omitted on paved Neo-Metro City streets)
            var placed = new List<Vector3>();
            if (stage == BrawlStageId.WrestlingRing) BuildRing(root, top);
            int boulderCount = stage == BrawlStageId.NeoMetroCity || stage == BrawlStageId.WrestlingRing ? 0 : stage == BrawlStageId.OrbitalDish ? 8 : 16;
            for (int k = 0; k < boulderCount; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(0.36f, 0.74f, (float)rng.NextDouble()) * radius;   // all inside the arena wall
                Vector3 p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                bool tooClose = false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 36f) { tooClose = true; break; }
                if (tooClose) continue;
                placed.Add(p);
                float size = Mathf.Lerp(1.6f, 4.4f, (float)rng.NextDouble());
                bool spire = k % 4 == 0;
                Vector3 scale = spire ? new Vector3(size * 0.7f, size * 2.2f, size * 0.7f) : new Vector3(size, size * 0.7f, size * 1.15f);
                p.y = Height(p.x, p.z, top, radius, ox, oz, stage) - scale.y * 0.28f;
                Rock(root, "Boulder", p, scale, (float)rng.NextDouble() * 360f, rng.Next(), k % 3 == 0 ? rimCol : rockCol);
            }
            // cliff rubble just inside the rim
            for (int k = 0; k < 24; k++)
            {
                float ang = k / 24f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.2f;
                float r = radius * Mathf.Lerp(0.9f, 0.98f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                float size = Mathf.Lerp(1.2f, 3.0f, (float)rng.NextDouble());
                p.y = Height(p.x, p.z, top, radius, ox, oz, stage) - size * 0.3f;
                Rock(root, "RimRock", p, new Vector3(size, size * 0.8f, size), (float)rng.NextDouble() * 360f, rng.Next(), rimCol);
            }
            return root;
        }

        /// <summary>Spawns stage-specific destructible structures, walls, pillars, buildings, falling trees and crates.</summary>
        public static void PopulateDestructibles(Transform arena, BrawlStageId stage, int seed)
        {
            var rng = new System.Random(seed);
            StageColors(stage, out _, out Color rockCol, out Color rimCol, out _, out _);

            if (stage == BrawlStageId.NeoMetroCity)
            {
                PopulateCityDestructibles(arena, rng);
                return;
            }
            if (stage == BrawlStageId.WrestlingRing) return;   // a clean ring: nothing to smash but each other
            if (stage == BrawlStageId.RedwoodForest)
            {
                PopulateForestDestructibles(arena, rng);
                return;
            }

            // 1. Perimeter / mid-ring destructible pillars & towers
            int pillarCount = stage == BrawlStageId.ScrapyardColosseum ? 10 : 8;
            for (int i = 0; i < pillarCount; i++)
            {
                float ang = i / (float)pillarCount * Mathf.PI * 2f + 0.35f;
                float dist = Mathf.Lerp(12f, 22f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                float h = Mathf.Lerp(5.5f, 9f, (float)rng.NextDouble());
                Color col = stage == BrawlStageId.GlacierTrench ? IronFaunaPalette.GlacierIce
                          : stage == BrawlStageId.FoundryCrucible ? IronFaunaPalette.FoundryRust
                          : stage == BrawlStageId.OrbitalDish ? IronFaunaPalette.OffWhitePanel
                          : rockCol;
                var pillar = BreakableProp.Create(arena, "DestructiblePillar", new Vector3(p.x, gy + h * 0.5f - 0.3f, p.z),
                    new Vector3(1.8f, h, 1.8f), col, 220f, true, new Vector3Int(2, 5, 2));
                if (stage == BrawlStageId.BasaltMesa || stage == BrawlStageId.ScrapyardColosseum || stage == BrawlStageId.GlacierTrench)
                    pillar.DressAsRock(rng.Next());   // natural stages: a rock spire, not a box
            }

            // 2. Destructible walls, shipping containers & barriers
            int wallCount = 6;
            for (int i = 0; i < wallCount; i++)
            {
                float ang = i / (float)wallCount * Mathf.PI * 2f + 0.8f;
                float dist = Mathf.Lerp(9f, 18f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                Vector3 size = stage == BrawlStageId.ScrapyardColosseum
                    ? new Vector3(5.2f, 3.2f, 2.2f) // shipping container
                    : new Vector3(4.6f, 3.6f, 1.1f); // blast wall / ice wall / radar screen
                Color col = stage == BrawlStageId.ScrapyardColosseum ? (i % 2 == 0 ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.CobaltFront)
                          : stage == BrawlStageId.OrbitalDish ? IronFaunaPalette.OrbitalSeam      // radar screens: grey panels, not flat cyan slabs
                          : stage == BrawlStageId.GlacierTrench ? IronFaunaPalette.GlacierDeepIce
                          : rimCol;
                var prop = BreakableProp.Create(arena, "DestructibleWall", new Vector3(p.x, gy + size.y * 0.5f - 0.2f, p.z),
                    size, col, 180f, true, new Vector3Int(4, 3, 2));
                prop.transform.rotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 90f, 0f);
            }

            // 3. Orbital Dish central breakable antenna tower
            if (stage == BrawlStageId.OrbitalDish && TryTerrain(Vector3.up * 25f, out float cy))
            {
                BreakableProp.Create(arena, "DishSpire", new Vector3(0f, cy + 5.5f, 0f),
                    new Vector3(2.4f, 11f, 2.4f), IronFaunaPalette.OffWhitePanel, 240f, true, new Vector3Int(2, 6, 2));
            }

            // 4. Loose crates (explosive cells removed at the owner's request, 2026-11-18)
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3(Random.Range(-21f, 21f), 0f, Random.Range(-21f, 21f));
                if (p.magnitude < 5.5f) p = p.normalized * 7.5f;
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                const bool explosive = false;
                float sz = explosive ? 1.35f : Random.Range(1.1f, 1.9f);
                Color col = explosive ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.HazardYellow;
                BreakableProp.Create(arena, explosive ? "ExplosiveCell" : "Crate",
                    new Vector3(p.x, gy + sz * 0.5f + 0.05f, p.z),
                    explosive ? new Vector3(sz * 0.85f, sz * 1.3f, sz * 0.85f) : Vector3.one * sz,
                    col, 110f, false, new Vector3Int(2, 2, 2), explosive);
            }
        }

        private static void PopulateCityDestructibles(Transform arena, System.Random rng)
        {
            // 1. Multi-story destructible city buildings right on the intersection corners & avenues (3-4 stacked floors per building!)
            Vector2[] buildingLots =
            {
                new Vector2(-11.5f, -11.5f), new Vector2(11.5f, -11.5f),
                new Vector2(-11.5f, 11.5f),  new Vector2(11.5f, 11.5f),
                new Vector2(0f, -18.5f),     new Vector2(0f, 18.5f),
                new Vector2(-18.5f, 0f),     new Vector2(18.5f, 0f),
            };
            for (int b = 0; b < buildingLots.Length; b++)
            {
                Vector3 basePos = new Vector3(buildingLots[b].x, 0f, buildingLots[b].y);
                if (!TryTerrain(basePos + Vector3.up * 25f, out float gy)) continue;
                float w = Mathf.Lerp(4.2f, 5.4f, (float)rng.NextDouble());
                float d = Mathf.Lerp(4.2f, 5.4f, (float)rng.NextDouble());
                float floorH = 3.2f;
                int stories = 3 + (b % 2);
                for (int f = 0; f < stories; f++)
                {
                    Color col = (f % 2 == 0) ? IronFaunaPalette.CityConcrete : IronFaunaPalette.CityGlass;
                    Vector3 center = new Vector3(basePos.x, gy + (f + 0.5f) * floorH, basePos.z);
                    // All stories start anchored; when a lower floor shatters, upper floors un-anchor and crash down into the street!
                    var floorProp = BreakableProp.Create(arena, $"CityBuilding_F{f + 1}", center,
                        new Vector3(w, floorH * 0.96f, d), col, 32f, true, new Vector3Int(3, 2, 3));
                    // Lit window ribbon around each story
                    Color neon = (b % 3 == 0) ? IronFaunaPalette.CyanEnergy : (b % 3 == 1) ? IronFaunaPalette.HazardYellow : IronFaunaPalette.OrangeJointCore;
                    ClamshellParts.Spawn(floorProp.transform, "WindowBand",
                        ClamshellMeshFactory.BeveledBox(new Vector3(w * 1.02f, 0.34f, d * 1.02f), 0.02f),
                        Vector3.zero, Quaternion.identity, neon, true);
                }
            }

            // 2. Streetlights & neon billboards along the cross-streets
            for (int i = 0; i < 8; i++)
            {
                float ang = i / 8f * Mathf.PI * 2f + 0.2f;
                Vector3 p = new Vector3(Mathf.Cos(ang) * 9.5f, 0f, Mathf.Sin(ang) * 9.5f);
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                var post = BreakableProp.Create(arena, "Streetlight", new Vector3(p.x, gy + 3.2f, p.z),
                    new Vector3(0.55f, 6.4f, 0.55f), IronFaunaPalette.GunmetalFrame, 120f, true, new Vector3Int(1, 4, 1));
                ClamshellParts.Spawn(post.transform, "StreetLampHead",
                    ClamshellMeshFactory.BeveledBox(new Vector3(1.1f, 0.32f, 1.1f), 0.04f),
                    new Vector3(0f, 3.25f, 0f), Quaternion.identity, IronFaunaPalette.HazardYellow, true);
            }

            // 3. Parked street cars / vehicles (cabin roof + glowing headlights/taillights)
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3(Mathf.Lerp(-16f, 16f, (float)rng.NextDouble()), 0f, Mathf.Lerp(-16f, 16f, (float)rng.NextDouble()));
                if (p.magnitude < 5f) p = p.normalized * 6.5f;
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                Color carCol = (i % 3 == 0) ? IronFaunaPalette.CrimsonFront : (i % 3 == 1) ? IronFaunaPalette.HazardYellow : IronFaunaPalette.CobaltFront;
                var car = BreakableProp.Create(arena, "StreetCar", new Vector3(p.x, gy + 0.72f, p.z),
                    new Vector3(2.1f, 0.95f, 4.1f), carCol, 85f, false, new Vector3Int(2, 2, 3), false);
                var cab = ClamshellParts.Spawn(car.transform, "CarCabin",
                    ClamshellMeshFactory.BeveledBox(new Vector3(1.75f, 0.68f, 2.1f), 0.08f),
                    new Vector3(0f, 0.76f, -0.15f), Quaternion.identity, IronFaunaPalette.CityGlass);
                if (cab.TryGetComponent<Renderer>(out var cr)) cr.sharedMaterial = IronFaunaMaterials.GetTerrain(IronFaunaPalette.CityGlass);
                ClamshellParts.Spawn(car.transform, "Headlights",
                    ClamshellMeshFactory.BeveledBox(new Vector3(1.6f, 0.22f, 0.14f), 0.02f),
                    new Vector3(0f, 0.12f, 2.04f), Quaternion.identity, IronFaunaPalette.HazardYellow, true);
                ClamshellParts.Spawn(car.transform, "Taillights",
                    ClamshellMeshFactory.BeveledBox(new Vector3(1.6f, 0.22f, 0.14f), 0.02f),
                    new Vector3(0f, 0.12f, -2.04f), Quaternion.identity, IronFaunaPalette.CrimsonFront, true);
                car.transform.rotation = Quaternion.Euler(0f, (i % 2 == 0) ? 0f : 90f, 0f);
            }

            // (lane paint, crosswalks and manholes are baked into the ground by BrawlStageLook)
        }

        private static void PopulateForestDestructibles(Transform arena, System.Random rng)
        {
            // 1. 20 tall Redwood / Pine trees (including inner-grove trees right inside the combat zone!) that crack at the stump and fall over ("TIMBER!") when hit!
            var treePositions = new List<Vector3>();
            for (int i = 0; i < 20; i++)
            {
                float ang = i / 20f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.25f;
                float dist = (i < 6)
                    ? Mathf.Lerp(4.2f, 9.5f, (float)rng.NextDouble())
                    : Mathf.Lerp(10.0f, 22f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                bool tooClose = false;
                foreach (var q in treePositions) if ((q - p).sqrMagnitude < 18f) { tooClose = true; break; }
                if (tooClose) continue;
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                treePositions.Add(p);
                float h = Mathf.Lerp(10.5f, 15.5f, (float)rng.NextDouble());
                float rad = Mathf.Lerp(0.55f, 0.80f, (float)rng.NextDouble());
                FallingTree.Create(arena, new Vector3(p.x, gy, p.z), h, rad, rng.Next());
            }

            // 2. Breakable forest boulders & fallen log barricades
            for (int i = 0; i < 8; i++)
            {
                float ang = i / 8f * Mathf.PI * 2f + 0.4f;
                float dist = Mathf.Lerp(9f, 18f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!TryTerrain(p + Vector3.up * 25f, out float gy)) continue;
                var log = BreakableProp.Create(arena, "FallenTimberLog", new Vector3(p.x, gy + 0.7f, p.z),
                    new Vector3(1.3f, 1.3f, 4.8f), IronFaunaPalette.ForestBark, 110f, false, new Vector3Int(2, 2, 4));
                log.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
            }
        }

        /// <summary>Ground height of the island at (x, z) for the given stage.</summary>
        /// <summary>Wrestling ring: half-size of the square canvas and how high it stands above the arena floor (m).</summary>
        public const float RingHalf = 11f, RingHeight = 1.4f;

        /// <summary>
        /// The ring itself: canvas, apron skirt, four corner posts with padded turnbuckles and three rows of ropes.
        /// The ropes are look-only (no colliders): the Rope Rebound special just runs out and bounces back.
        /// </summary>
        private static void BuildRing(Transform root, float top)
        {
            var ring = new GameObject("WrestlingRing").transform;
            ring.SetParent(root, false);
            Material canvas = IronFaunaMaterials.Get(IronFaunaPalette.OffWhitePanel, false);
            Material apron = IronFaunaMaterials.Get(IronFaunaPalette.MaroonRear, false);
            Material post = IronFaunaMaterials.Get(IronFaunaPalette.DarkIron, false);
            Material pad = IronFaunaMaterials.Get(IronFaunaPalette.CrimsonFront, false);
            Material[] ropes =
            {
                IronFaunaMaterials.Get(IronFaunaPalette.CrimsonFront, false),
                IronFaunaMaterials.Get(IronFaunaPalette.OffWhitePanel, false),
                IronFaunaMaterials.Get(IronFaunaPalette.CyanEnergy, true),
            };

            GameObject Box(string name, Vector3 pos, Vector3 size, Material m, bool collider)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(ring, false);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());   // immediate: spawns raycast the ground right after building
                return go;
            }

            float h = RingHalf;
            // canvas (slightly proud of the terrain so it reads cleanly) and the logo-coloured centre square
            Box("Canvas", new Vector3(0f, top + 0.04f, 0f), new Vector3(h * 2f + 0.4f, 0.1f, h * 2f + 0.4f), canvas, true);
            Box("CentreMark", new Vector3(0f, top + 0.095f, 0f), new Vector3(5f, 0.01f, 5f), pad, false);
            // apron skirt down to the floor on all four sides
            float skirtY = top - RingHeight * 0.5f;
            Box("ApronN", new Vector3(0f, skirtY, h + 0.2f), new Vector3(h * 2f + 0.4f, RingHeight, 0.12f), apron, false);
            Box("ApronS", new Vector3(0f, skirtY, -h - 0.2f), new Vector3(h * 2f + 0.4f, RingHeight, 0.12f), apron, false);
            Box("ApronE", new Vector3(h + 0.2f, skirtY, 0f), new Vector3(0.12f, RingHeight, h * 2f + 0.4f), apron, false);
            Box("ApronW", new Vector3(-h - 0.2f, skirtY, 0f), new Vector3(0.12f, RingHeight, h * 2f + 0.4f), apron, false);

            // corner posts + turnbuckle pads
            const float postH = 2.6f;
            var corners = new[] { new Vector3(h, 0f, h), new Vector3(-h, 0f, h), new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h) };
            foreach (var c in corners)
            {
                Box("Post", new Vector3(c.x, top + postH * 0.5f, c.z), new Vector3(0.35f, postH, 0.35f), post, true);
                for (int k = 0; k < 3; k++)
                    Box("Turnbuckle", new Vector3(c.x * 0.985f, top + 0.8f + k * 0.62f, c.z * 0.985f), new Vector3(0.5f, 0.32f, 0.5f), pad, false);
            }
            // three rows of ropes between the posts (visual only)
            for (int k = 0; k < 3; k++)
            {
                float y = top + 0.8f + k * 0.62f;
                var m = ropes[k];
                Box("RopeN" + k, new Vector3(0f, y, h), new Vector3(h * 2f, 0.07f, 0.07f), m, false);
                Box("RopeS" + k, new Vector3(0f, y, -h), new Vector3(h * 2f, 0.07f, 0.07f), m, false);
                Box("RopeE" + k, new Vector3(h, y, 0f), new Vector3(0.07f, 0.07f, h * 2f), m, false);
                Box("RopeW" + k, new Vector3(-h, y, 0f), new Vector3(0.07f, 0.07f, h * 2f), m, false);
            }
            // ring steps at two corners
            Box("StepsA", new Vector3(h + 1.0f, top - RingHeight + 0.45f, h - 1.2f), new Vector3(1.4f, 0.9f, 1.4f), post, true);
            Box("StepsB", new Vector3(-h - 1.0f, top - RingHeight + 0.45f, -h + 1.2f), new Vector3(1.4f, 0.9f, 1.4f), post, true);

            // overhead ring lights
            var lightGo = new GameObject("RingLight");
            lightGo.transform.SetParent(ring, false);
            lightGo.transform.localPosition = new Vector3(0f, top + 16f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 85f;
            spot.range = 40f;
            spot.intensity = 6f;
            spot.color = IronFaunaPalette.KeyLightWarm;
            spot.shadows = LightShadows.Soft;
        }

        /// <summary>The bare ground of the arena being built (not rocks, scenery or last round's arena).</summary>
        public static Collider ActiveGround;

        /// <summary>
        /// Ground height under (x, z) on the bare terrain only. Scenery and props use this so nothing ends up perched on
        /// a rock's collision hull, on top of other scenery, or on the previous round's arena (owner 2026-11-22: "floating objects everywhere").
        /// </summary>
        public static bool TryTerrain(Vector3 p, out float y)
        {
            y = 0f;
            if (ActiveGround == null || !ActiveGround.enabled) return IronFauna.Boxing.MechBoxer.TryGround(p, out y);
            var ray = new Ray(new Vector3(p.x, p.y + 60f, p.z), Vector3.down);
            if (!ActiveGround.Raycast(ray, out RaycastHit hit, 200f)) return false;
            y = hit.point.y;
            return true;
        }

        /// <summary>Radius of the arena wall (inside the cliff rim everywhere).</summary>
        public const float WallRadiusFrac = 0.82f;
        public static float WallRadius(float islandRadius) => islandRadius * WallRadiusFrac;
        public const float WallHeight = 3.4f, FenceHeight = 5.5f;

        /// <summary>
        /// A real wall around the arena (owner 2026-11-22: "you can still throw your opponent out... prevent it, an actual
        /// physical wall, not an invisible one"): an armoured gunmetal base with a hazard stripe that follows the ground,
        /// steel posts with cyan beacons, and a three-bar energy fence on top. The base, posts and fence all collide, so
        /// throws, slams and knockbacks bounce off it. Built after the scenery so nothing is placed on top of it.
        /// </summary>
        public static void BuildArenaWall(Transform parent, float top, float radius, int seed, BrawlStageId stage)
        {
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;   // same terrain offsets as Build
            var root = new GameObject("ArenaWall").transform;
            root.SetParent(parent, false);

            Material plate = IronFaunaMaterials.Get(IronFaunaPalette.GunmetalFrame, false);
            Material dark = IronFaunaMaterials.Get(IronFaunaPalette.DarkIron, false);
            Material stripe = IronFaunaMaterials.Get(IronFaunaPalette.HazardYellow, false);
            Material glow = IronFaunaMaterials.Get(IronFaunaPalette.CyanEnergy, true);

            float R = WallRadius(radius);
            const int segs = 48;
            const float thick = 0.9f;
            float segLen = 2f * Mathf.PI * R / segs + 0.25f;   // slight overlap: no gaps between panels

            GameObject Box(string name, Vector3 pos, Quaternion rot, Vector3 size, Material m, bool collider)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = size;
                var mr = go.GetComponent<MeshRenderer>();
                if (m != null) mr.sharedMaterial = m; else mr.enabled = false;
                if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
                return go;
            }

            float GroundAt(float ang, float dist) => Height(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist, top, radius, ox, oz, stage);

            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs, am = (a0 + a1) * 0.5f;
                float gMin = float.MaxValue, gMax = float.MinValue;
                for (int k = 0; k <= 4; k++)
                {
                    float sa = Mathf.Lerp(a0, a1, k / 4f);
                    foreach (float sr in new[] { R - thick, R, R + thick })
                    {
                        float g = GroundAt(sa, sr);
                        gMin = Mathf.Min(gMin, g);
                        gMax = Mathf.Max(gMax, g);
                    }
                }
                Vector3 outward = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
                float bottom = gMin - 2.5f, wallTop = gMax + WallHeight;
                Vector3 c = outward * R;

                // armoured base panel
                Box("WallPanel", new Vector3(c.x, (bottom + wallTop) * 0.5f, c.z), rot, new Vector3(segLen, wallTop - bottom, thick), plate, true);
                // inner face detail: dark recessed band, hazard stripe and a rail along the top
                Box("WallBand", new Vector3(c.x, gMax + 0.9f, c.z) - outward * (thick * 0.5f + 0.02f), rot, new Vector3(segLen * 0.94f, 0.6f, 0.06f), dark, false);
                Box("WallStripe", new Vector3(c.x, wallTop - 0.45f, c.z) - outward * (thick * 0.5f + 0.03f), rot, new Vector3(segLen * 0.96f, 0.28f, 0.06f), stripe, false);
                Box("WallCap", new Vector3(c.x, wallTop + 0.09f, c.z), rot, new Vector3(segLen, 0.18f, thick + 0.25f), dark, false);

                // energy fence above the panel: three glowing bars (visible) on one solid fence body
                Box("Fence", new Vector3(c.x, wallTop + FenceHeight * 0.5f, c.z), rot, new Vector3(segLen, FenceHeight, 0.3f), null, true);
                for (int b = 0; b < 3; b++)
                {
                    float barY = wallTop + 1.4f + b * 1.75f;
                    Box("FenceBar", new Vector3(c.x, barY, c.z), rot, new Vector3(segLen, 0.12f, 0.12f), glow, false);
                }

                // post at the panel joint, with a beacon on top
                Vector3 pd = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 pc = pd * R;
                Quaternion prot = Quaternion.LookRotation(pd, Vector3.up);
                float postTop = wallTop + FenceHeight + 0.3f;
                Box("WallPost", new Vector3(pc.x, (bottom + postTop) * 0.5f, pc.z), prot, new Vector3(0.6f, postTop - bottom, thick + 0.4f), dark, true);
                Box("PostBeacon", new Vector3(pc.x, postTop + 0.15f, pc.z), prot, new Vector3(0.4f, 0.3f, 0.4f), glow, false);
            }
        }

        public static float Height(float x, float z, float top, float radius, float ox = 0f, float oz = 0f, BrawlStageId stage = BrawlStageId.BasaltMesa)
        {
            float r = Mathf.Sqrt(x * x + z * z) / radius;
            float fbm = 0f, amp = 1f, freq = 0.045f;
            for (int o = 0; o < 4; o++)
            {
                fbm += (Mathf.PerlinNoise(ox + x * freq, oz + z * freq) - 0.5f) * amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            float h = 0f;
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible:
                {
                    // Stepped industrial octagonal tiers: flat fighting decks with raised outer walkways
                    float tier = r < 0.38f ? 0f : r < 0.68f ? 1.6f : 3.2f;
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Repeat(r * 3.2f, 1f));
                    h = Mathf.Lerp(tier, tier, blend) + fbm * 0.45f;
                    break;
                }
                case BrawlStageId.OrbitalDish:
                {
                    // Parabolic radar dish: slopes gently down to the center, rises into a steep bowl at the rim
                    h = (r * r) * 7.5f - 1.5f + fbm * 0.35f;
                    break;
                }
                case BrawlStageId.ScrapyardColosseum:
                {
                    // Sunken gladiatorial pit with terraced walls and scrap mounds
                    float bowl = Mathf.Clamp01((r - 0.42f) / 0.42f);
                    h = bowl * bowl * 6.5f + fbm * 2.2f;
                    break;
                }
                case BrawlStageId.GlacierTrench:
                {
                    // Narrow icy ridge along X with twin snow peaks and steep side drop-offs along Z
                    float ridgeDrop = Mathf.Clamp01(Mathf.Abs(z) / (radius * 0.68f));
                    h = fbm * 3.0f - ridgeDrop * ridgeDrop * 5.5f;
                    h += 5.5f * Mathf.Exp(-((x - 15f) * (x - 15f) + z * z) / (2f * 6.5f * 6.5f));
                    h += 4.8f * Mathf.Exp(-((x + 15f) * (x + 15f) + z * z) / (2f * 6.5f * 6.5f));
                    break;
                }
                case BrawlStageId.NeoMetroCity:
                {
                    // Flat urban street grid with slightly raised city block sidewalks
                    bool avenue = Mathf.Abs(x) < 6.5f || Mathf.Abs(z) < 6.5f;
                    h = (avenue ? 0f : 0.35f) + fbm * 0.15f;
                    break;
                }
                case BrawlStageId.WrestlingRing:
                {
                    // Raised square ring in the middle of a flat arena floor (owner 2026-11-22: "a boxing ring map")
                    bool inRing = Mathf.Abs(x) <= RingHalf && Mathf.Abs(z) <= RingHalf;
                    h = inRing ? 0f : -RingHeight;
                    break;
                }
                case BrawlStageId.RedwoodForest:
                {
                    // Gentle rolling woodland clearing with soft knolls around the perimeter
                    float edgeRise = Mathf.Clamp01((r - 0.25f) / 0.55f);
                    h = fbm * 2.6f + edgeRise * 2.2f;
                    break;
                }
                default:
                {
                    float centreFlat = Mathf.Clamp01((r - 0.12f) / 0.3f);
                    centreFlat = centreFlat * centreFlat * (3f - 2f * centreFlat);
                    h = fbm * 4.2f * Mathf.Lerp(0.25f, 1f, centreFlat);
                    h += 7.5f * Mathf.Exp(-((x - 12f) * (x - 12f) + (z + 8f) * (z + 8f)) / (2f * 7.5f * 7.5f));   // the big hill
                    h += 3.2f * Mathf.Exp(-((x + 13f) * (x + 13f) + (z - 11f) * (z - 11f)) / (2f * 5f * 5f));   // a mound
                    h += 2.4f * Mathf.Exp(-((x + 4f) * (x + 4f) + (z + 17f) * (z + 17f)) / (2f * 4f * 4f));     // a rise
                    break;
                }
            }
            float y = top + h;
            // ragged cliff at the rim, then a long drop
            float rim = 0.88f + (Mathf.PerlinNoise(ox + x * 0.12f, oz + z * 0.12f) - 0.5f) * 0.08f;
            if (r > rim)
            {
                float k = Mathf.Clamp01((r - rim) / 0.1f);
                y = Mathf.Lerp(y, top - 30f, k * k);
            }
            return y;
        }

        private static void Rock(Transform parent, string name, Vector3 pos, Vector3 scale, float yaw, int seed, Color color)
        {
            Mesh mesh = RockMesh(seed);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = IronFaunaMaterials.GetTerrain(color);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
        }

        /// <summary>A lumpy, faceted unit rock: an icosphere pushed around by noise, flattened underneath.</summary>
        public static Mesh RockMesh(int seed)
        {
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 50f, oy = (float)rng.NextDouble() * 50f;
            List<Vector3> v;
            List<int> f;
            Icosphere(2, out v, out f);
            for (int i = 0; i < v.Count; i++)
            {
                Vector3 p = v[i];
                float nse = Mathf.PerlinNoise(ox + p.x * 1.7f + p.z, oy + p.y * 1.7f - p.z) - 0.5f;
                float s = 0.5f * (1f + nse * 0.55f);
                p *= s;
                if (p.y < -0.2f) p.y = -0.2f - (p.y + 0.2f) * 0.3f;
                v[i] = p;
            }
            // flat-shaded facets read as rock
            var fv = new Vector3[f.Count];
            var tri = new int[f.Count];
            var uv = new Vector2[f.Count];
            // the icosphere table winds counter-clockwise; Unity's front faces are clockwise, so swap two corners
            for (int i = 0; i < f.Count; i += 3) { int tmp = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = tmp; }
            for (int i = 0; i < f.Count; i++)
            {
                fv[i] = v[f[i]];
                tri[i] = i;
                uv[i] = new Vector2(fv[i].x + fv[i].y, fv[i].z - fv[i].y) * 0.8f;
            }
            var mesh = new Mesh { name = "Rock", hideFlags = HideFlags.DontSave };
            mesh.vertices = fv;
            mesh.triangles = tri;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void Icosphere(int subdivisions, out List<Vector3> verts, out List<int> faces)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var cache = new Dictionary<long, int>();
            var vlist = verts;
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }

            int Mid(int p, int q)
            {
                long key = p < q ? ((long)p << 32) | (uint)q : ((long)q << 32) | (uint)p;
                if (cache.TryGetValue(key, out int idx)) return idx;
                vlist.Add(((vlist[p] + vlist[q]) * 0.5f).normalized);
                idx = vlist.Count - 1;
                cache[key] = idx;
                return idx;
            }
        }
    }
}
