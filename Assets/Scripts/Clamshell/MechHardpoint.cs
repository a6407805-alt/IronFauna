using UnityEngine;

namespace IronFauna.Clamshell
{
    /// <summary>
    /// A weapon mounting socket on the skeleton (back pylons, lower spine).
    /// Created by <see cref="MechClamshellBuilder"/> on every rebuild; weapons parent themselves here.
    /// </summary>
    public class MechHardpoint : MonoBehaviour
    {
        public string id;
        public HardpointSize size;
        [Tooltip("Direction the socket opens toward, in the mech's local space.")]
        public Vector3 mountAxis = Vector3.up;
    }
}
