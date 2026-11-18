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
        public float toughness = 42f;
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
                ClamshellMeshFactory.BeveledBox(new Vector3(radius * 2.1f, 0.8f, radius * 2.1f), 0.08f),
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
                ClamshellMeshFactory.BeveledBox(trunkSize, radius * 0.22f),
                new Vector3(0f, height * 0.5f, 0f), Quaternion.identity, IronFaunaPalette.ForestBark);
            trunkVis.GetComponent<Renderer>().sharedMaterial = barkMat;

            // 3 layered evergreen canopy tiers
            for (int tier = 0; tier < 3; tier++)
            {
                float w = radius * (5.4f - tier * 1.25f);
                float h = height * (0.26f - tier * 0.03f);
                float y = height * (0.58f + tier * 0.18f);
                var crown = ClamshellParts.Spawn(treeGo.transform, "Canopy_" + tier,
                    ClamshellMeshFactory.BeveledBox(new Vector3(w, h, w), 0.18f),
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
            if (_fallen) return;
            _damage += damage;
            if (_damage >= toughness)
            {
                Vector3 pushDir = new Vector3(velocity.x, 0f, velocity.z);
                if (pushDir.sqrMagnitude < 1e-3f) pushDir = transform.position - point;
                pushDir.y = 0f;
                Topple(pushDir.sqrMagnitude > 1e-3f ? pushDir.normalized : Vector3.forward);
            }
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
            Destroy(gameObject, 15f);
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
        }
    }
}
