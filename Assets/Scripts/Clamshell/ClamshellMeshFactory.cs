using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Procedural hard-surface mesh kit for IronFauna's clamshell armor.
    /// Every mesh is a convex "loft": a convex cross-section (in local X/Z) swept along local Y through
    /// a list of stations, each station scaling the section. Faces get their own vertices so the result
    /// is flat-shaded with crisp chamfers (the cel-shaded hard-surface look from the art bible).
    ///
    /// Shell convention used by every armor half:
    ///   local +Y = along the bone, local +Z = outward (front halves), local X = plate width.
    /// Meshes are cached by their parameters, so 1,000,000 loadout combinations share a small mesh set.
    /// </summary>
    public static class ClamshellMeshFactory
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public struct Station
        {
            public float y;
            public float sx;
            public float sz;

            public Station(float y, float sx, float sz)
            {
                this.y = y;
                this.sx = sx;
                this.sz = sz;
            }
        }

        /// <summary>Clears cached meshes (call after a domain reload or when regenerating everything).</summary>
        public static void ClearCache()
        {
            foreach (var mesh in Cache.Values)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Object.Destroy(mesh);
                else Object.DestroyImmediate(mesh);
            }
            Cache.Clear();
        }

        // ------------------------------------------------------------------
        // Public shapes
        // ------------------------------------------------------------------

        /// <summary>
        /// One clamshell half: a half-octagon plate that starts at the seam plane (z = sign * inner)
        /// and extends outward by <paramref name="depth"/>. Both bone-ends taper with a 45° bevel
        /// (depth shrinks by <paramref name="bevel"/>) so neighbouring halves clear each other when a joint folds.
        /// sign = +1 for front/outer halves, -1 for back/inner halves.
        /// </summary>
        public static Mesh ShellHalf(float length, float width, float depth, float inner, float bevel, float sign)
        {
            string key = Key("shell", length, width, depth, inner, bevel, sign);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            bevel = Mathf.Clamp(bevel, 0.01f, Mathf.Min(depth * 0.85f, length * 0.32f));
            float c = Mathf.Min(width * 0.5f, depth) * 0.35f;
            float hw = width * 0.5f;

            // Section in X/Z with the seam face at z = 0 (shifted to z = inner below).
            var section = new[]
            {
                new Vector2(-hw, 0f),
                new Vector2(-hw, depth - c),
                new Vector2(-hw + c, depth),
                new Vector2(hw - c, depth),
                new Vector2(hw, depth - c),
                new Vector2(hw, 0f),
            };

            float h = length * 0.5f;
            float endDepth = (depth - bevel) / depth;
            float endWidth = Mathf.Clamp01((width - bevel * 1.2f) / width);
            var stations = new[]
            {
                new Station(-h, endWidth, endDepth),
                new Station(-h + bevel, 1f, 1f),
                new Station(h - bevel, 1f, 1f),
                new Station(h, endWidth, endDepth),
            };

            var mesh = Loft(section, stations, inner, sign, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// A lengthwise clamshell half (toe / heel):
        /// flat seam face at y = -length/2, tapered 45° bevel at the outer end y = +length/2.
        /// The section is a chamfered rectangle centred on the local origin (X width, Z height).
        /// </summary>
        public static Mesh LengthwiseHalf(float length, float width, float height, float bevel, float endScale,
            float seamBevel = 0f, float seamScale = 1f)
        {
            string key = Key("lhalf", length, width, height, bevel, endScale, seamBevel, seamScale);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            bevel = Mathf.Clamp(bevel, 0.01f, length * 0.6f);
            seamBevel = Mathf.Clamp(seamBevel, 0f, length - bevel - 0.01f);
            var section = Octagon(width, height, Mathf.Min(width, height) * 0.18f);
            float h = length * 0.5f;
            Station[] stations = seamBevel > 0.001f
                ? new[]
                {
                    // Seam end lowered only in height (Z) so the toe/heel can tuck under a folding calf.
                    new Station(-h, 1f, seamScale),
                    new Station(-h + seamBevel, 1f, 1f),
                    new Station(h - bevel, 1f, 1f),
                    new Station(h, endScale, endScale),
                }
                : new[]
                {
                    new Station(-h, 1f, 1f),
                    new Station(h - bevel, 1f, 1f),
                    new Station(h, endScale, endScale),
                };
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Box with every edge chamfered (the workhorse greeble).</summary>
        public static Mesh BeveledBox(Vector3 size, float bevel)
        {
            string key = Key("bbox", size.x, size.y, size.z, bevel);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float b = Mathf.Max(0.0005f, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f));
            var section = Octagon(size.x, size.z, b);
            float h = size.y * 0.5f;
            Station[] stations;
            if (size.y > b * 2.2f)
            {
                float sx = Mathf.Max(0.05f, (size.x - 2f * b) / size.x);
                float sz = Mathf.Max(0.05f, (size.z - 2f * b) / size.z);
                stations = new[]
                {
                    new Station(-h, sx, sz),
                    new Station(-h + b, 1f, 1f),
                    new Station(h - b, 1f, 1f),
                    new Station(h, sx, sz),
                };
            }
            else
            {
                stations = new[] { new Station(-h, 1f, 1f), new Station(h, 1f, 1f) };
            }

            // Octagon is centred on z = 0, so pass inner = -0 with no shift.
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>N-sided prism along local Y (canisters, coils, nozzles, hubs).</summary>
        public static Mesh Prism(float radius, float height, int sides)
        {
            return TaperedPrism(radius, radius, height, sides);
        }

        /// <summary>N-sided prism along local Y whose radius goes from radiusBottom (y-) to radiusTop (y+).</summary>
        public static Mesh TaperedPrism(float radiusBottom, float radiusTop, float height, int sides)
        {
            sides = Mathf.Clamp(sides, 3, 32);
            string key = Key("prism", radiusBottom, radiusTop, height, sides);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var section = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f;
                section[i] = new Vector2(Mathf.Cos(a) * radiusBottom, Mathf.Sin(a) * radiusBottom);
            }
            float s = radiusBottom > 0.0001f ? radiusTop / radiusBottom : 1f;
            float h = height * 0.5f;
            var stations = new[] { new Station(-h, 1f, 1f), new Station(h, s, s) };
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Ridge with a triangular section (base on z = 0, apex at z = height) whose apex height scales
        /// from 1 at y- to <paramref name="endScale"/> at y+. endScale ~0 makes a saw tooth.
        /// </summary>
        public static Mesh Ridge(float width, float height, float length, float startScale, float endScale)
        {
            string key = Key("ridge", width, height, length, startScale, endScale);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var section = new[]
            {
                new Vector2(-width * 0.5f, 0f),
                new Vector2(0f, height),
                new Vector2(width * 0.5f, 0f),
            };
            float h = length * 0.5f;
            var stations = new[]
            {
                new Station(-h, 1f, Mathf.Max(0.02f, startScale)),
                new Station(h, 1f, Mathf.Max(0.02f, endScale)),
            };
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Faceted stealth prism: triangular ridge that tapers to points at both ends.</summary>
        public static Mesh FacetPrism(float width, float height, float length)
        {
            string key = Key("facet", width, height, length);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var section = new[]
            {
                new Vector2(-width * 0.5f, 0f),
                new Vector2(-width * 0.18f, height),
                new Vector2(width * 0.18f, height),
                new Vector2(width * 0.5f, 0f),
            };
            float h = length * 0.5f;
            var stations = new[]
            {
                new Station(-h, 0.08f, 0.08f),
                new Station(-h * 0.35f, 1f, 1f),
                new Station(h * 0.35f, 1f, 1f),
                new Station(h, 0.08f, 0.08f),
            };
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Box-section wedge sitting on z = 0 whose height ramps from startScale (y-) to 1 (y+).</summary>
        public static Mesh Wedge(float width, float height, float length, float startScale)
        {
            string key = Key("wedge", width, height, length, startScale);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float hw = width * 0.5f;
            float c = Mathf.Min(hw, height) * 0.25f;
            var section = new[]
            {
                new Vector2(-hw, 0f),
                new Vector2(-hw, height - c),
                new Vector2(-hw + c, height),
                new Vector2(hw - c, height),
                new Vector2(hw, height - c),
                new Vector2(hw, 0f),
            };
            float h = length * 0.5f;
            var stations = new[]
            {
                new Station(-h, 1f, Mathf.Max(0.05f, startScale)),
                new Station(h, 1f, 1f),
            };
            var mesh = Loft(section, stations, 0f, 1f, key);
            Cache[key] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------
        // Core loft
        // ------------------------------------------------------------------

        private static Vector2[] Octagon(float width, float depth, float chamfer)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float b = Mathf.Min(chamfer, Mathf.Min(hx, hz) * 0.9f);
            return new[]
            {
                new Vector2(-hx + b, -hz),
                new Vector2(hx - b, -hz),
                new Vector2(hx, -hz + b),
                new Vector2(hx, hz - b),
                new Vector2(hx - b, hz),
                new Vector2(-hx + b, hz),
                new Vector2(-hx, hz - b),
                new Vector2(-hx, -hz + b),
            };
        }

        /// <summary>
        /// Sweeps a convex X/Z section through the stations. Section point (x, z) at a station becomes
        /// (x * sx, y, sign * (inner + z * sz)). The resulting solid is convex, so each face is oriented
        /// outward by comparing its normal with the solid's centroid.
        /// </summary>
        private static Mesh Loft(Vector2[] section, Station[] stations, float inner, float sign, string name)
        {
            int n = section.Length;
            int m = stations.Length;
            var rings = new Vector3[m][];
            Vector3 centroid = Vector3.zero;
            int count = 0;
            for (int s = 0; s < m; s++)
            {
                rings[s] = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = section[i];
                    var v = new Vector3(p.x * stations[s].sx, stations[s].y, sign * (inner + p.y * stations[s].sz));
                    rings[s][i] = v;
                    centroid += v;
                    count++;
                }
            }
            centroid /= Mathf.Max(1, count);

            var verts = new List<Vector3>(n * m * 4 + n * 2);
            var normals = new List<Vector3>(verts.Capacity);
            var tris = new List<int>(n * m * 6 + n * 6);

            // Side faces
            for (int s = 0; s < m - 1; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    // Stations can scale X and Z by different amounts, which twists a side quad slightly;
                    // two flat triangles keep the faceted shading exact.
                    EmitPolygon(new[] { rings[s][i], rings[s][j], rings[s + 1][j] }, centroid, verts, normals, tris);
                    EmitPolygon(new[] { rings[s][i], rings[s + 1][j], rings[s + 1][i] }, centroid, verts, normals, tris);
                }
            }

            // End caps
            EmitPolygon(rings[0], centroid, verts, normals, tris);
            EmitPolygon(rings[m - 1], centroid, verts, normals, tris);

            var mesh = new Mesh { name = "IF_" + name };
            mesh.hideFlags = HideFlags.DontSave;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(SmoothedNormalsAsTangents(verts, normals));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The outline pass extrudes along these averaged normals (stored in tangent.xyz with w = 0) so the
        /// ink outline stays closed around the hard flat-shaded edges.
        /// </summary>
        public static List<Vector4> SmoothedNormalsAsTangents(List<Vector3> verts, List<Vector3> normals)
        {
            var sums = new Dictionary<Vector3Int, Vector3>();
            for (int i = 0; i < verts.Count; i++)
            {
                var key = Quantize(verts[i]);
                sums.TryGetValue(key, out var sum);
                sums[key] = sum + normals[i];
            }
            var tangents = new List<Vector4>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 n = sums[Quantize(verts[i])];
                n = n.sqrMagnitude > 1e-10f ? n.normalized : normals[i];
                tangents.Add(new Vector4(n.x, n.y, n.z, 0f));
            }
            return tangents;
        }

        private static Vector3Int Quantize(Vector3 v)
        {
            return new Vector3Int(Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
        }

        private static void EmitPolygon(Vector3[] poly, Vector3 solidCentroid, List<Vector3> verts, List<Vector3> normals, List<int> tris)
        {
            // Drop consecutive duplicate points (collapsed stations make triangles out of quads).
            var pts = new List<Vector3>(poly.Length);
            for (int i = 0; i < poly.Length; i++)
            {
                if (pts.Count == 0 || (poly[i] - pts[pts.Count - 1]).sqrMagnitude > 1e-12f) pts.Add(poly[i]);
            }
            if (pts.Count > 2 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude <= 1e-12f) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return;

            Vector3 normal = Vector3.zero;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                center += pts[i];
                if (i >= 1 && i < pts.Count - 1)
                {
                    normal += Vector3.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
                }
            }
            center /= pts.Count;
            if (normal.sqrMagnitude < 1e-14f) return; // zero-area face

            normal.Normalize();
            if (Vector3.Dot(normal, center - solidCentroid) < 0f)
            {
                pts.Reverse();
                normal = -normal;
            }

            int start = verts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                verts.Add(pts[i]);
                normals.Add(normal);
            }
            for (int i = 1; i < pts.Count - 1; i++)
            {
                tris.Add(start);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
        }

        private static string Key(string kind, params float[] values)
        {
            var sb = new System.Text.StringBuilder(kind);
            foreach (float v in values)
            {
                sb.Append('_').Append(v.ToString("F4", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
