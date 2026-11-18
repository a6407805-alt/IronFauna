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
    }

    /// <summary>
    /// Procedural multi-stage brawl builder: generates terrain, lighting colors, and destructible environment cover
    /// for 5 distinct arenas (Basalt Mesa, Foundry Crucible, Orbital Dish, Scrapyard Colosseum, Glacier Trench).
    /// All static terrain colliders work seamlessly with MechBoxer.followGround and the walk cycle's foot probes.
    /// </summary>
    public static class BrawlTerrain
    {
        public const int StageCount = 5;

        public static string StageName(BrawlStageId stage)
        {
            switch (stage)
            {
                case BrawlStageId.FoundryCrucible: return "FOUNDRY CRUCIBLE";
                case BrawlStageId.OrbitalDish: return "ORBITAL DISH";
                case BrawlStageId.ScrapyardColosseum: return "SCRAPYARD COLOSSEUM";
                case BrawlStageId.GlacierTrench: return "GLACIER TRENCH";
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
                uvs[j * (n + 1) + i] = new Vector2(x, z) * 0.12f;
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
            mr.sharedMaterial = IronFaunaMaterials.Get(groundCol, false);
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;

            // ---------------- boulders and outcrops (static, half buried)
            var placed = new List<Vector3>();
            int boulderCount = stage == BrawlStageId.OrbitalDish ? 8 : 16;
            for (int k = 0; k < boulderCount; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(0.36f, 0.84f, (float)rng.NextDouble()) * radius;
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

        /// <summary>Spawns stage-specific destructible structures, walls, pillars, containers and explosive cells.</summary>
        public static void PopulateDestructibles(Transform arena, BrawlStageId stage, int seed)
        {
            var rng = new System.Random(seed);
            StageColors(stage, out _, out Color rockCol, out Color rimCol, out _, out _);

            // 1. Perimeter / mid-ring destructible pillars & towers
            int pillarCount = stage == BrawlStageId.ScrapyardColosseum ? 10 : 8;
            for (int i = 0; i < pillarCount; i++)
            {
                float ang = i / (float)pillarCount * Mathf.PI * 2f + 0.35f;
                float dist = Mathf.Lerp(12f, 22f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!IronFauna.Boxing.MechBoxer.TryGround(p + Vector3.up * 25f, out float gy)) continue;
                float h = Mathf.Lerp(5.5f, 9f, (float)rng.NextDouble());
                Color col = stage == BrawlStageId.GlacierTrench ? IronFaunaPalette.GlacierIce
                          : stage == BrawlStageId.FoundryCrucible ? IronFaunaPalette.FoundryRust
                          : stage == BrawlStageId.OrbitalDish ? IronFaunaPalette.OffWhitePanel
                          : rockCol;
                BreakableProp.Create(arena, "DestructiblePillar", new Vector3(p.x, gy + h * 0.5f - 0.3f, p.z),
                    new Vector3(1.8f, h, 1.8f), col, 220f, true, new Vector3Int(2, 5, 2));
            }

            // 2. Destructible walls, shipping containers & barriers
            int wallCount = 6;
            for (int i = 0; i < wallCount; i++)
            {
                float ang = i / (float)wallCount * Mathf.PI * 2f + 0.8f;
                float dist = Mathf.Lerp(9f, 18f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!IronFauna.Boxing.MechBoxer.TryGround(p + Vector3.up * 25f, out float gy)) continue;
                Vector3 size = stage == BrawlStageId.ScrapyardColosseum
                    ? new Vector3(5.2f, 3.2f, 2.2f) // shipping container
                    : new Vector3(4.6f, 3.6f, 1.1f); // blast wall / ice wall / radar screen
                Color col = stage == BrawlStageId.ScrapyardColosseum ? (i % 2 == 0 ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.CobaltFront)
                          : stage == BrawlStageId.OrbitalDish ? IronFaunaPalette.CyanEnergy
                          : stage == BrawlStageId.GlacierTrench ? IronFaunaPalette.GlacierIce
                          : rimCol;
                var prop = BreakableProp.Create(arena, "DestructibleWall", new Vector3(p.x, gy + size.y * 0.5f - 0.2f, p.z),
                    size, col, 180f, true, new Vector3Int(4, 3, 2));
                prop.transform.rotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 90f, 0f);
            }

            // 3. Orbital Dish central breakable antenna tower
            if (stage == BrawlStageId.OrbitalDish && IronFauna.Boxing.MechBoxer.TryGround(Vector3.up * 25f, out float cy))
            {
                BreakableProp.Create(arena, "DishSpire", new Vector3(0f, cy + 5.5f, 0f),
                    new Vector3(2.4f, 11f, 2.4f), IronFaunaPalette.OffWhitePanel, 240f, true, new Vector3Int(2, 6, 2));
            }

            // 4. Loose crates & explosive fuel/plasma cells
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = new Vector3(Random.Range(-21f, 21f), 0f, Random.Range(-21f, 21f));
                if (p.magnitude < 5.5f) p = p.normalized * 7.5f;
                if (!IronFauna.Boxing.MechBoxer.TryGround(p + Vector3.up * 25f, out float gy)) continue;
                bool explosive = (i % 3 == 0) || (stage == BrawlStageId.FoundryCrucible && i % 2 == 0);
                float sz = explosive ? 1.35f : Random.Range(1.1f, 1.9f);
                Color col = explosive ? IronFaunaPalette.OrangeJointCore : IronFaunaPalette.HazardYellow;
                BreakableProp.Create(arena, explosive ? "ExplosiveCell" : "Crate",
                    new Vector3(p.x, gy + sz * 0.5f + 0.05f, p.z),
                    explosive ? new Vector3(sz * 0.85f, sz * 1.3f, sz * 0.85f) : Vector3.one * sz,
                    col, 110f, false, new Vector3Int(2, 2, 2), explosive);
            }
        }

        /// <summary>Ground height of the island at (x, z) for the given stage.</summary>
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
            go.AddComponent<MeshRenderer>().sharedMaterial = IronFaunaMaterials.Get(color, false);
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
