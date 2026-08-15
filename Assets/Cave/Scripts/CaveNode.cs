using UnityEngine;

namespace CaveTunnel
{
    /// <summary>
    /// One control point on the cave path. The mesh is a cross-section swept along a Catmull-Rom
    /// curve through these, so the node list is the whole shape: drag a node to bend the tunnel,
    /// widen one to swell it into a cavern, drop one to dive underground.
    ///
    /// The position is the middle of the <em>floor</em>, not the middle of the bore, so a node sits
    /// where the kart drives. That is what makes "snap to ground" mean something.
    ///
    /// All values are in the generator's local space.
    /// </summary>
    [System.Serializable]
    public class CaveNode
    {
        [Tooltip("Middle of the floor at this point, in the generator's local space.")]
        public Vector3 position;

        [Tooltip("Half-width of the passage here. Wall to wall is twice this.")]
        [Min(0.1f)] public float width = 5f;

        [Tooltip("Height from the floor to the top of the arch here.")]
        [Min(0.1f)] public float height = 4.5f;

        [Tooltip("Banks the cross-section around the direction of travel, in degrees. Tilts the " +
                 "whole passage, floor included, so a corner can be cambered.")]
        [Range(-90f, 90f)] public float roll;

        [Tooltip("1 is a dead flat drivable floor. Lower values bow the floor down into a rounded " +
                 "trough, which reads as a natural cave but is worse to drive.")]
        [Range(0f, 1f)] public float floorFlatten = 1f;

        [Tooltip("Scales wall roughness locally. Drop to 0 where the tunnel has to meet built " +
                 "geometry cleanly, push past 1 for a chewed-up section.")]
        [Range(0f, 2f)] public float roughness = 1f;

        public CaveNode() { }

        public CaveNode(Vector3 position, float width, float height)
        {
            this.position = position;
            this.width = width;
            this.height = height;
        }

        public CaveNode Clone()
        {
            return (CaveNode)MemberwiseClone();
        }
    }
}
