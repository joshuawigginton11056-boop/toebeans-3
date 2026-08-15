using UnityEngine;

namespace RaceTrack
{
    /// <summary>
    /// One control point on the racing line. The track is a cross-section swept along a Catmull-Rom
    /// curve through these, so the node list is the whole layout: drag a node to bend the circuit,
    /// lift one to fly the track over the one below it, close the list into a loop to make a lap.
    ///
    /// The position is the middle of the <em>racing surface</em>, so a node sits where a kart drives
    /// — not on the ground, and not at the bottom of the slab. Height is entirely free; nothing here
    /// is anchored to terrain.
    ///
    /// All values are in the generator's local space.
    /// </summary>
    [System.Serializable]
    public class TrackNode
    {
        [Tooltip("Middle of the racing surface at this point, in the generator's local space. " +
                 "Drag it anywhere, at any height — the track is a free-floating ribbon and does " +
                 "not care what is underneath it.")]
        public Vector3 position;

        [Tooltip("Full width of the racing surface here, tarmac edge to tarmac edge, in metres. " +
                 "Ignored while Uniform Width is switched on in the settings, which is the setting " +
                 "that guarantees the track can never narrow. A kart here is 1.65 m across, so 14 m " +
                 "is eight and a half abreast and 12 m is the sensible floor for eight-up racing.")]
        [Min(2f)] public float width = 14f;

        [Tooltip("Extra bank here, in degrees, added on top of whatever the automatic banking works " +
                 "out from the corner. Positive raises the right-hand edge. Leave at 0 and let the " +
                 "corner bank itself unless you want a wall-ride or a flat-out cambered straight.")]
        [Range(-89f, 89f)] public float bank;

        [Tooltip("Scales the barrier height here. 1 is the height set in the settings; 0 drops the " +
                 "barrier flush so the edge is open, which is how you make a ramp mouth, a jump " +
                 "landing or a shortcut. The racing surface itself is unaffected.")]
        [Range(0f, 2f)] public float wallScale = 1f;

        public TrackNode() { }

        public TrackNode(Vector3 position, float width)
        {
            this.position = position;
            this.width = width;
        }

        public TrackNode Clone()
        {
            return (TrackNode)MemberwiseClone();
        }
    }
}
