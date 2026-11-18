using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;
using IronFauna.Core;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Procedural brawl island: a rolling, rocky mesa with a big hill and a couple of mounds, a flatter fighting
    /// ground in the middle, a ragged cliff all round (fall off and you're out), half-buried boulders and
    /// outcrops. All static colliders, so MechBoxer.followGround and the walk cycle's foot probes ride it.
    /// </summary>
    public static class BrawlTerrain
    {
        public static Transform Build(Transform parent, float top, float radius, int seed)
        {
            var root = new GameObject("BrawlTerrain").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;

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
                float y = Height(x, z, top, radius, ox, oz);
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
            mr.sharedMaterial = IronFaunaMaterials.Get(IronFaunaPalette.TerrainDirt, false);
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;

            // ---------------- boulders and outcrops (static, half buried)
            var placed = new List<Vector3>();
            for (int k = 0; k < 18; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(0.32f, 0.86f, (float)rng.NextDouble()) * radius;
                Vector3 p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                bool tooClose = false;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 36f) { tooClose = true; break; }
                if (tooClose) continue;
                placed.Add(p);
                float size = Mathf.Lerp(1.6f, 4.8f, (float)rng.NextDouble());
                bool spire = k % 5 == 0;
                Vector3 scale = spire ? new Vector3(size * 0.7f, size * 2.2f, size * 0.7f) : new Vector3(size, size * 0.7f, size * 1.15f);
                p.y = Height(p.x, p.z, top, radius, ox, oz) - scale.y * 0.28f;
                Rock(root, "Boulder", p, scale, (float)rng.NextDouble() * 360f, rng.Next(), k % 3 == 0 ? IronFaunaPalette.TerrainRockDark : IronFaunaPalette.TerrainRock);
            }
            // cliff rubble just inside the rim
            for (int k = 0; k < 26; k++)
            {
                float ang = k / 26f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.2f;
                float r = radius * Mathf.Lerp(0.9f, 0.98f, (float)rng.NextDouble());
                Vector3 p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                float size = Mathf.Lerp(1.2f, 3.2f, (float)rng.NextDouble());
                p.y = Height(p.x, p.z, top, radius, ox, oz) - size * 0.3f;
                Rock(root, "RimRock", p, new Vector3(size, size * 0.8f, size), (float)rng.NextDouble() * 360f, rng.Next(), IronFaunaPalette.TerrainRockDark);
            }
            return root;
        }

        /// <summary>Ground height of the island at (x, z).</summary>
        public static float Height(float x, float z, float top, float radius, float ox = 0f, float oz = 0f)
        {
            float r = Mathf.Sqrt(x * x + z * z) / radius;
            float fbm = 0f, amp = 1f, freq = 0.045f;
            for (int o = 0; o < 4; o++)
            {
                fbm += (Mathf.PerlinNoise(ox + x * freq, oz + z * freq) - 0.5f) * amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            float centreFlat = Mathf.Clamp01((r - 0.12f) / 0.3f);
            centreFlat = centreFlat * centreFlat * (3f - 2f * centreFlat);
            float h = fbm * 4.2f * Mathf.Lerp(0.25f, 1f, centreFlat);
            h += 7.5f * Mathf.Exp(-((x - 12f) * (x - 12f) + (z + 8f) * (z + 8f)) / (2f * 7.5f * 7.5f));   // the big hill
            h += 3.2f * Mathf.Exp(-((x + 13f) * (x + 13f) + (z - 11f) * (z - 11f)) / (2f * 5f * 5f));   // a mound
            h += 2.4f * Mathf.Exp(-((x + 4f) * (x + 4f) + (z + 17f) * (z + 17f)) / (2f * 4f * 4f));     // a rise
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
