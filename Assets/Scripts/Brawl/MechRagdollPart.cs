using UnityEngine;

namespace IronFauna.Brawl
{
    /// <summary>One physics segment of a MechRagdoll: forwards its collisions to the owner.</summary>
    public class MechRagdollPart : MonoBehaviour
    {
        public MechRagdoll owner;
        public MechRagdoll.Segment segment;

        private void OnCollisionEnter(Collision c)
        {
            if (owner != null && segment != null) owner.OnPartCollision(segment, c);
        }
    }
}
