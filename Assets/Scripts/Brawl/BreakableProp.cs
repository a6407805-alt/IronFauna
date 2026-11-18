using UnityEngine;
using IronFauna.Clamshell;
using IronFauna.Core;

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
        public bool explosive;
        public static event System.Action<Vector3, float> Shattered;

        private float _damage;
        private bool _broken;
        private float _spawnTime;

        private void Awake() => _spawnTime = Time.time;

        public static BreakableProp Create(Transform parent, string name, Vector3 pos, Vector3 size, Color color, float massPerM3, bool anchored, Vector3Int pieces, bool explosive = false)
        {
            var go = ClamshellParts.Spawn(parent, name, ClamshellMeshFactory.BeveledBox(size, Mathf.Min(0.04f, size.x * 0.05f)), pos, Quaternion.identity, color);
            if (go.TryGetComponent<Renderer>(out var rend)) rend.sharedMaterial = IronFaunaMaterials.GetTerrain(color);
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(5f, size.x * size.y * size.z * massPerM3);
            rb.isKinematic = anchored;      // walls and pillars stand until broken
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var p = go.AddComponent<BreakableProp>();
            p.color = color;
            p.pieces = pieces;
            p.explosive = explosive;
            p.toughness = explosive ? 110f : Mathf.Clamp(rb.mass * 0.42f + 120f, 160f, 380f);
            return p;
        }

        public void ApplyStrike(Vector3 point, Vector3 velocity, float damage)
        {
            if (_broken) return;
            _damage += damage;
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.AddForceAtPosition(velocity * 14f, point, ForceMode.Impulse);
            if (_damage >= toughness) Shatter(point, velocity);
        }

        private void OnCollisionEnter(Collision c)
        {
            if (_broken || Time.time - _spawnTime < 0.8f) return;
            float hit = c.impulse.magnitude;
            bool hitByMech = c.rigidbody != null && c.rigidbody.GetComponent<MechRagdollPart>() != null;
            if (hitByMech)
            {
                hit = Mathf.Max(hit, c.relativeVelocity.magnitude * 26f);
            }
            else if (c.relativeVelocity.magnitude < 4.5f)
            {
                return; // resting stacked building floors ignore gentle settling contacts
            }
            if (hit < 35f) return;
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
                if (go.TryGetComponent<Renderer>(out var rend)) rend.sharedMaterial = IronFaunaMaterials.GetTerrain(color);
                var bc = go.AddComponent<BoxCollider>();
                bc.size = cell * 0.96f;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = chunkMass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 away = (world - point);
                float near = Mathf.Clamp01(1.5f - away.magnitude / Mathf.Max(0.5f, size.magnitude * 0.5f));
                rb.linearVelocity = (parentRb.isKinematic ? Vector3.zero : parentRb.linearVelocity) + away.normalized * near * 6f + velocity * 0.15f * near;
                rb.angularVelocity = Random.insideUnitSphere * 4f;
                Destroy(go, Random.Range(22f, 30f));
            }
            // If this was a supporting building story, un-anchor any story resting directly above it so the skyscraper collapses downward!
            foreach (var col in Physics.OverlapBox(transform.position + Vector3.up * (size.y * 0.85f), new Vector3(size.x * 0.45f, size.y * 0.6f, size.z * 0.45f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var aboveProp = col.GetComponentInParent<BreakableProp>();
                if (aboveProp != null && aboveProp != this && !aboveProp._broken)
                {
                    var aboveRb = aboveProp.GetComponent<Rigidbody>();
                    if (aboveRb != null && aboveRb.isKinematic)
                    {
                        aboveRb.isKinematic = false;
                        aboveRb.AddForce(Vector3.down * 45f, ForceMode.Impulse);
                        aboveProp.toughness = Mathf.Min(aboveProp.toughness, 95f);
                    }
                }
            }
            if (explosive)
            {
                var hitOwners = new System.Collections.Generic.HashSet<MechRagdoll>();
                foreach (var col in Physics.OverlapSphere(point, 6.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = col.attachedRigidbody;
                    if (rb == null) continue;
                    var part = rb.GetComponent<MechRagdollPart>();
                    if (part != null && part.owner != null && hitOwners.Add(part.owner))
                    {
                        Vector3 dir = (part.owner.Position - point);
                        float dist = Mathf.Max(1f, dir.magnitude);
                        float dmg = Mathf.Lerp(16f, 4f, dist / 6.2f);
                        part.owner.ReceiveStrike(null, part.segment, point, dir.normalized + Vector3.up * 0.45f, dmg, 0.9f);
                        part.owner.Knockdown(1.3f);
                    }
                    else if (!rb.isKinematic)
                    {
                        rb.AddExplosionForce(550f, point, 6.2f, 1.0f, ForceMode.Impulse);
                    }
                }
            }
            Shattered?.Invoke(point, explosive ? 1.2f : Mathf.Clamp01(parentRb.mass / 2000f));
            Destroy(gameObject);
        }
    }
}
