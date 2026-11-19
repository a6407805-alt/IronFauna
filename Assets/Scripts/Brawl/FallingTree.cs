using System.Collections.Generic;
using UnityEngine;
using IronFauna.Clamshell;
using IronFauna.Core;

namespace IronFauna.Brawl
{
    /// <summary>
    /// A tall forest tree that stands upright until chopped/smashed by punches, kicks, charges, or thrown mechs,
    /// then cracks at the stump and topples over as a heavy physical log ("TIMBER!"), crushing any fighter in its path.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallingTree : MonoBehaviour
    {
        public float toughness = 260f;   // a few committed heavy blows fell a redwood
        public float height = 13f;
        public float trunkRadius = 0.65f;
        public static event System.Action<Vector3> TreeFelled;

        private Rigidbody _rb;
        private float _damage;
        private bool _fallen;
        private float _fallTime;
        private float _spawnTime;
        private readonly HashSet<MechRagdoll> _crushed = new HashSet<MechRagdoll>();

        private void Awake() => _spawnTime = Time.time;

        public static FallingTree Create(Transform parent, Vector3 groundPos, float height, float radius, int seed)
        {
            // Stump stays in the ground where the tree stood
            var stump = ClamshellParts.Spawn(parent, "TreeStump",
                ClamshellMeshFactory.TaperedPrism(radius * 1.25f, radius * 0.95f, 0.8f, 12),
                groundPos + Vector3.up * 0.35f, Quaternion.Euler(0f, (seed % 360), 0f), IronFaunaPalette.ForestBark);
            stump.GetComponent<Renderer>().sharedMaterial = IronFaunaMaterials.GetTerrain(IronFaunaPalette.ForestBark);

            // Trunk + canopy pivot at the base of the cut
            var treeGo = new GameObject("FallingTree");
            treeGo.transform.SetParent(parent, false);
            treeGo.transform.position = groundPos + Vector3.up * 0.75f;
            treeGo.transform.rotation = Quaternion.Euler(0f, (seed * 37) % 360, 0f);

            var barkMat = IronFaunaMaterials.GetTerrain(IronFaunaPalette.ForestBark);
            var canopyMat = IronFaunaMaterials.GetTerrain(IronFaunaPalette.ForestCanopy);

            Vector3 trunkSize = new Vector3(radius * 1.75f, height, radius * 1.75f);
            var trunkVis = ClamshellParts.Spawn(treeGo.transform, "Trunk",
                ClamshellMeshFactory.TaperedPrism(radius * 0.95f, radius * 0.55f, height, 12),   // round, tapering trunk (was a box)
                new Vector3(0f, height * 0.5f, 0f), Quaternion.identity, IronFaunaPalette.ForestBark);
            trunkVis.GetComponent<Renderer>().sharedMaterial = barkMat;

            // 3 layered evergreen canopy tiers
            for (int tier = 0; tier < 4; tier++)
            {
                float w = radius * (6.2f - tier * 1.3f);
                float h = height * (0.3f - tier * 0.04f);
                float y = height * (0.5f + tier * 0.15f);
                var crown = ClamshellParts.Spawn(treeGo.transform, "Canopy_" + tier,
                    ClamshellMeshFactory.TaperedPrism(w * 0.5f, w * 0.04f, h, 9),   // drooping conifer tiers (were boxes)
                    new Vector3(0f, y, 0f), Quaternion.Euler(0f, tier * 30f, 0f), IronFaunaPalette.ForestCanopy);
                crown.GetComponent<Renderer>().sharedMaterial = canopyMat;
            }

            var col = treeGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, height * 0.52f, 0f);
            col.size = new Vector3(radius * 1.85f, height * 1.02f, radius * 1.85f);

            var rb = treeGo.AddComponent<Rigidbody>();
            rb.mass = 420f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.centerOfMass = new Vector3(0f, height * 0.58f, 0f);

            var ft = treeGo.AddComponent<FallingTree>();
            ft._rb = rb;
            ft.height = height;
            ft.trunkRadius = radius;
            return ft;
        }

        public void ApplyStrike(Vector3 point, Vector3 velocity, float damage)
        {
            if (_fallen)
            {
                if (Time.time - _fallTime > 2.6f && damage > 100f) SplinterFallenTrunk(point, velocity);   // only heavy blows splinter a trunk
                return;
            }
            _damage += damage;
            if (_damage >= toughness)
            {
                Vector3 pushDir = new Vector3(velocity.x, 0f, velocity.z);
                if (pushDir.sqrMagnitude < 1e-3f) pushDir = transform.position - point;
                pushDir.y = 0f;
                Topple(pushDir.sqrMagnitude > 1e-3f ? pushDir.normalized : Vector3.forward);
            }
        }

        private bool _splintered;

        private void SplinterFallenTrunk(Vector3 point, Vector3 velocity)
        {
            if (_splintered) return;
            _splintered = true;
            var barkMat = IronFaunaMaterials.GetTerrain(IronFaunaPalette.ForestBark);
            Mesh logMesh = ClamshellMeshFactory.Prism(trunkRadius * 0.7f, height * 0.18f, 10);
            for (int i = 0; i < 4; i++)
            {
                Vector3 world = transform.TransformPoint(new Vector3(0f, height * (0.18f + i * 0.2f), 0f));
                var chunk = ClamshellParts.Spawn(transform.parent, "SplinteredLog", logMesh,
                    transform.parent != null ? transform.parent.InverseTransformPoint(world) : world,
                    transform.rotation, IronFaunaPalette.ForestBark);
                if (chunk.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = barkMat;
                var bc = chunk.AddComponent<BoxCollider>();
                bc.size = new Vector3(trunkRadius * 1.4f, height * 0.18f, trunkRadius * 1.4f);
                var rb = chunk.AddComponent<Rigidbody>();
                rb.mass = 18f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.linearVelocity = (velocity * 0.2f) + Vector3.up * 2.5f + Random.insideUnitSphere * 2.5f;
                Destroy(chunk, 18f);
            }
            Destroy(gameObject);
        }

        public void Topple(Vector3 fallDirection)
        {
            if (_fallen) return;
            _fallen = true;
            _fallTime = Time.time;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = false;
            fallDirection.y = 0f;
            if (fallDirection.sqrMagnitude < 1e-4f) fallDirection = Vector3.forward;
            fallDirection.Normalize();

            Vector3 torqueAxis = Vector3.Cross(Vector3.up, fallDirection);
            _rb.AddForceAtPosition(fallDirection * 1800f, transform.position + Vector3.up * (height * 0.75f), ForceMode.Impulse);
            _rb.AddTorque(torqueAxis * 2400f, ForceMode.Impulse);
            TreeFelled?.Invoke(transform.position);
            Destroy(gameObject, 32f);
        }

        private void OnCollisionEnter(Collision c)
        {
            if (c == null || Time.time - _spawnTime < 0.8f) return;
            if (!_fallen)
            {
                bool hitByMech = c.rigidbody != null && c.rigidbody.GetComponent<MechRagdollPart>() != null;
                if (!hitByMech && c.relativeVelocity.magnitude < 5f) return;
                float hit = c.impulse.magnitude;
                if (hitByMech)
                {
                    if (c.relativeVelocity.magnitude < 4f) return;   // brushing past a trunk does nothing
                    hit = Mathf.Max(hit, 24f + c.relativeVelocity.magnitude * 28f);
                }
                if (hit < 20f) return;
                _damage += hit;
                if (_damage >= toughness)
                {
                    Vector3 push = transform.position - (c.contactCount > 0 ? c.GetContact(0).point : c.transform.position);
                    Topple(push);
                }
                return;
            }

            // While falling, the heavy trunk crushes any mech or breakable prop in its path!
            if (Time.time - _fallTime < 4.5f && _rb != null && _rb.linearVelocity.magnitude > 2.2f)
            {
                var part = c.rigidbody != null ? c.rigidbody.GetComponent<MechRagdollPart>() : null;
                if (part != null && part.owner != null && part.segment != null && _crushed.Add(part.owner))
                {
                    Vector3 pt = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                    Vector3 dir = _rb.linearVelocity.normalized + Vector3.up * 0.2f;
                    part.owner.ReceiveStrike(null, part.segment, pt, dir, 28f, 1f);
                    part.owner.Knockdown(2.2f);
                }
                var prop = c.collider != null ? c.collider.GetComponentInParent<BreakableProp>() : null;
                if (prop != null)
                {
                    prop.Shatter(c.contactCount > 0 ? c.GetContact(0).point : transform.position, _rb.linearVelocity);
                }
            }
            else if (Time.time - _fallTime >= 2.8f && c.rigidbody != null && c.rigidbody.GetComponent<MechRagdollPart>() != null && c.relativeVelocity.magnitude > 3.5f)
            {
                SplinterFallenTrunk(c.contactCount > 0 ? c.GetContact(0).point : transform.position, c.relativeVelocity);
            }
        }
    }
}
