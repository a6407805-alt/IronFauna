using System.Collections.Generic;
using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// Hand-modeled clamshell halves. Drop a model at
    ///   Assets/Resources/ClamshellMeshes/&lt;Slot&gt;_&lt;Set&gt;.fbx   (e.g. ThighFront_Set01_Ironclad.fbx)
    /// and the builder uses it instead of the procedural half. Models are re-oriented automatically
    /// (mesh Y = along the bone, the offset axis = outward) and fitted to the universal envelope
    /// of that slot, so every hand-made part still clears the joints the same way.
    /// Authoring guide (Blender): origin on the bone centre line, bone along Z, plate pushed out toward -Y
    /// (the front view), plate depth about 1.6 x the envelope depth including details.
    /// </summary>
    public static class ClamshellMeshLibrary
    {
        public const string ResourceFolder = "ClamshellMeshes";

        private static readonly Dictionary<string, Mesh> Fitted = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Mesh> Sources = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, PartRole[]> Roles = new Dictionary<string, PartRole[]>();

        /// <summary>Colour role per sub-mesh, taken from the Blender material name (Shell, Panel, Hazard, Dark, Frame, Glow, Core, Copper).</summary>
        public enum PartRole { Shell, Panel, Hazard, Dark, Frame, Glow, Core, Copper }
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static string ResourceName(ClamshellSlot slot, ClamshellSetArchetype set)
        {
            return $"{slot}_{set}";
        }

        /// <summary>True if a hand-made model exists for this slot and set.</summary>
        public static bool Has(ClamshellSlot slot, ClamshellSetArchetype set)
        {
            return LoadSource(ResourceName(slot, set)) != null;
        }

        /// <summary>
        /// Returns the hand-made model for this slot/set fitted into <paramref name="target"/> (local bounds of
        /// the half: X width, Y along the bone, Z outward), or false if there is no model.
        /// </summary>
        public static bool TryGet(ClamshellSlot slot, ClamshellSetArchetype set, Bounds target, out Mesh mesh, out PartRole[] roles)
        {
            mesh = null;
            roles = null;
            string name = ResourceName(slot, set);
            Mesh source = LoadSource(name);
            if (source == null) return false;
            Roles.TryGetValue(name, out roles);

            string key = $"{name}|{target.center.x:F3},{target.center.y:F3},{target.center.z:F3}|{target.size.x:F3},{target.size.y:F3},{target.size.z:F3}";
            if (Fitted.TryGetValue(key, out mesh) && mesh != null) return true;

            mesh = FitToEnvelope(source, target, target.center.z >= 0f ? 1f : -1f);
            mesh.name = "IF_Custom_" + name;
            Fitted[key] = mesh;
            return true;
        }

        /// <summary>
        /// Lengthwise parts (feet, quadruped body, animal head) are modeled at true size: X width, Z length with
        /// +Z the free tip, height toward -Y from Y = 0. Loaded by name (e.g. "FootFront_Set02_Bastion") and scaled
        /// uniformly so the model's length matches <paramref name="target"/> (local Y), centred on X and seated on
        /// the target's bottom (min Z). Details may stick out sideways or upward.
        /// </summary>
        public static bool TryGetLengthwise(string name, Bounds target, out Mesh mesh, out PartRole[] roles)
        {
            mesh = null;
            roles = null;
            Mesh source = LoadSource(name);
            if (source == null) return false;
            Roles.TryGetValue(name, out roles);

            string key = $"LW|{name}|{target.center.x:F3},{target.center.y:F3},{target.center.z:F3}|{target.size.x:F3},{target.size.y:F3},{target.size.z:F3}";
            if (Fitted.TryGetValue(key, out mesh) && mesh != null) return true;

            // Re-orient like a plate (bone = Y, offset axis = Z) with outward = +Z (up), then scale uniformly.
            Mesh oriented = FitToEnvelope(source, new Bounds(Vector3.zero, Vector3.one), 1f, true);
            Vector3[] v = oriented.vertices;
            Bounds ob = oriented.bounds;
            float s = target.size.y / Mathf.Max(ob.size.y, 1e-5f);
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = new Vector3(target.center.x + (v[i].x - ob.center.x) * s,
                    target.min.y + (v[i].y - ob.min.y) * s,
                    target.min.z + (v[i].z - ob.min.z) * s);
            }
            oriented.vertices = v;
            oriented.RecalculateNormals();
            oriented.RecalculateBounds();
            oriented.SetTangents(ClamshellMeshFactory.SmoothedNormalsAsTangents(new List<Vector3>(v), new List<Vector3>(oriented.normals)));
            oriented.name = "IF_Custom_" + name;
            mesh = oriented;
            Fitted[key] = mesh;
            return true;
        }

        public static void ClearCache()
        {
            Fitted.Clear();
            Sources.Clear();
            Roles.Clear();
            Missing.Clear();
        }

        private static Mesh LoadSource(string name)
        {
            if (Sources.TryGetValue(name, out var cached) && cached != null) return cached;
            if (Missing.Contains(name)) return null;

            Mesh mesh = null;
            PartRole[] roles = null;
            var prefab = Resources.Load<GameObject>($"{ResourceFolder}/{name}");
            if (prefab != null)
            {
                var filter = prefab.GetComponentInChildren<MeshFilter>();
                var renderer = prefab.GetComponentInChildren<MeshRenderer>();
                if (filter != null) mesh = filter.sharedMesh;
                if (mesh != null && renderer != null)
                {
                    var mats = renderer.sharedMaterials;
                    roles = new PartRole[mesh.subMeshCount];
                    for (int i = 0; i < roles.Length; i++)
                    {
                        roles[i] = i < mats.Length && mats[i] != null ? RoleFromName(mats[i].name) : PartRole.Shell;
                    }
                }
            }
            if (mesh == null) mesh = Resources.Load<Mesh>($"{ResourceFolder}/{name}");

            if (mesh == null || !mesh.isReadable)
            {
                if (mesh != null) Debug.LogWarning($"[IronFauna] Enable Read/Write on the import settings of {ResourceFolder}/{name} so it can be fitted.");
                Missing.Add(name);
                return null;
            }
            Sources[name] = mesh;
            if (roles != null) Roles[name] = roles;
            return mesh;
        }

        private static PartRole RoleFromName(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            if (n.Contains("panel") || n.Contains("white")) return PartRole.Panel;
            if (n.Contains("hazard") || n.Contains("yellow")) return PartRole.Hazard;
            if (n.Contains("dark")) return PartRole.Dark;
            if (n.Contains("frame") || n.Contains("gunmetal")) return PartRole.Frame;
            if (n.Contains("glow") || n.Contains("cyan")) return PartRole.Glow;
            if (n.Contains("core") || n.Contains("orange")) return PartRole.Core;
            if (n.Contains("copper")) return PartRole.Copper;
            return PartRole.Shell;
        }

        private static Mesh FitToEnvelope(Mesh source, Bounds target, float outwardSign, bool keepProportions = false)
        {
            Vector3[] verts = source.vertices;
            Bounds b = source.bounds;
            Vector3 size = b.size;
            Vector3 center = b.center;

            // Authoring convention: the bone runs along Blender Z, which the FBX export (Apply Transform)
            // turns into the mesh's Y axis. Of the other two axes, the outward one is the axis whose centre
            // sits furthest off the bone; the remaining one is the plate width.
            const int boneAxis = 1;
            float offX = Mathf.Abs(center.x) / Mathf.Max(size.x, 1e-5f);
            float offZ = Mathf.Abs(center.z) / Mathf.Max(size.z, 1e-5f);
            int outAxis = offZ >= offX ? 2 : 0;
            int widthAxis = outAxis == 2 ? 0 : 2;
            float outSourceSign = center[outAxis] >= 0f ? 1f : -1f;

            // The remap is a signed axis permutation; pick the width sign so its determinant is +1,
            // which keeps triangle winding (and so front faces) correct. Back halves mirror Z.
            float zSign = outwardSign;
            float permSign = Vector3.Dot(Vector3.Cross(Axis(widthAxis), Axis(boneAxis)), Axis(outAxis));
            float widthSign = permSign * outSourceSign * zSign;

            var remapped = new Vector3[verts.Length];
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                var p = new Vector3(v[widthAxis] * widthSign, v[boneAxis], v[outAxis] * outSourceSign * zSign);
                remapped[i] = p;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            // Fit width and length to the envelope exactly; keep the modeled depth proportion (capped at the
            // envelope's maximum detail depth) and seat the inner face on the seam.
            Vector3 srcSize = max - min;
            Vector3 tMin = target.min;
            Vector3 tMax = target.max;
            Vector3 tSize = target.size;
            float sx = tSize.x / Mathf.Max(srcSize.x, 1e-5f);
            float sy = tSize.y / Mathf.Max(srcSize.y, 1e-5f);
            float sz = Mathf.Min((sx + sy) * 0.5f, tSize.z / Mathf.Max(srcSize.z, 1e-5f));
            if (keepProportions) sx = sy = sz = 1f; // caller rescales uniformly
            for (int i = 0; i < remapped.Length; i++)
            {
                Vector3 p = remapped[i];
                float z = zSign > 0f ? tMin.z + (p.z - min.z) * sz : tMax.z - (max.z - p.z) * sz;
                remapped[i] = new Vector3(tMin.x + (p.x - min.x) * sx, tMin.y + (p.y - min.y) * sy, z);
            }

            var mesh = new Mesh { hideFlags = HideFlags.DontSave };
            if (remapped.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = remapped;
            mesh.subMeshCount = source.subMeshCount;
            for (int sm = 0; sm < source.subMeshCount; sm++) mesh.SetTriangles(source.GetTriangles(sm), sm);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var normals = new List<Vector3>(mesh.normals);
            mesh.SetTangents(ClamshellMeshFactory.SmoothedNormalsAsTangents(new List<Vector3>(remapped), normals));
            return mesh;
        }

        private static Vector3 Axis(int i)
        {
            return i == 0 ? Vector3.right : (i == 1 ? Vector3.up : Vector3.forward);
        }
    }
}
