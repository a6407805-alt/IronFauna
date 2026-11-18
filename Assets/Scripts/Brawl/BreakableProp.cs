using UnityEngine;
using IronFauna.Clamshell;

namespace IronFauna.Brawl
{
    /// <summary>
    /// A destructible block (crate, pillar, wall section). Takes damage from impacts - mechs crashing into it,
    /// thrown limbs, other debris - and shatters into physical chunks once it has taken enough.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BreakableProp : MonoBehaviour
    {
        public float toughness = 600f;     // accumulated impulse before it shatters
        public Vector3Int pieces = new Vector3Int(3, 3, 3);
        public Color color;
        public static event System.Action<Vector3, float> Shattered;

        private float _damage;
        private bool _broken;

        public static BreakableProp Create(Transform parent, string name, Vector3 pos, Vector3 size, Color color, float massPerM3, bool anchored, Vector3Int pieces)
        {
            var go = ClamshellParts.Spawn(parent, name, ClamshellMeshFactory.BeveledBox(size, Mathf.Min(0.04f, size.x * 0.05f)), pos, Quaternion.identity, color);
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(5f, size.x * size.y * size.z * massPerM3);
            rb.isKinematic = anchored;      // walls and pillars stand until broken
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var p = go.AddComponent<BreakableProp>();
            p.color = color;
            p.pieces = pieces;
            p.toughness = rb.mass * 3.5f + 120f;   // chaos: thrown mechs and big hits smash cover
            return p;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (_broken) return;
            float hit = c.impulse.magnitude;
            if (hit < 40f) return;
            _damage += hit;
            if (_damage >= toughness) Shatter(c.contactCount > 0 ? c.GetContact(0).point : transform.position, c.relativeVelocity);
        }

        public void Shatter(Vector3 point, Vector3 velocity)
        {
            if (_broken) return;
            _broken = true;
            var box = GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
            Vector3 cell = new Vector3(size.x / pieces.x, size.y / pieces.y, size.z / pieces.z);
            var parentRb = GetComponent<Rigidbody>();
            float chunkMass = Mathf.Max(1f, parentRb.mass / (pieces.x * pieces.y * pieces.z));
            Mesh mesh = ClamshellMeshFactory.BeveledBox(cell * 0.96f, Mathf.Min(0.03f, cell.x * 0.08f));
            for (int x = 0; x < pieces.x; x++)
            for (int y = 0; y < pieces.y; y++)
            for (int z = 0; z < pieces.z; z++)
            {
                Vector3 local = new Vector3((x + 0.5f) * cell.x - size.x * 0.5f, (y + 0.5f) * cell.y - size.y * 0.5f, (z + 0.5f) * cell.z - size.z * 0.5f);
                Vector3 world = transform.TransformPoint(local);
                var go = ClamshellParts.Spawn(transform.parent, name + "_Chunk", mesh, transform.parent != null ? transform.parent.InverseTransformPoint(world) : world,
                    transform.parent != null ? Quaternion.Inverse(transform.parent.rotation) * transform.rotation : transform.rotation, color);
                var bc = go.AddComponent<BoxCollider>();
                bc.size = cell * 0.96f;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = chunkMass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 away = (world - point);
                float near = Mathf.Clamp01(1.5f - away.magnitude / Mathf.Max(0.5f, size.magnitude * 0.5f));
                rb.linearVelocity = (parentRb.isKinematic ? Vector3.zero : parentRb.linearVelocity) + away.normalized * near * 6f + velocity * 0.15f * near;
                rb.angularVelocity = Random.insideUnitSphere * 4f;
                Destroy(go, Random.Range(10f, 14f));
            }
            Shattered?.Invoke(point, Mathf.Clamp01(parentRb.mass / 2000f));
            Destroy(gameObject);
        }
    }
}
