using UnityEngine;

namespace RockBridge
{
    /// <summary>
    /// One control point on the crossing. The bridge is a cross-section swept along a Catmull-Rom
    /// curve through these, so the node list is the whole layout: drag a node sideways to bend the
    /// crossing, add one to reach further.
    ///
    /// The position is the middle of the <em>driving surface</em> — a node sits where a kart drives,
    /// not on the ground and not at the bottom of the slab.
    ///
    /// Whether the height in that position is used at all depends on
    /// <see cref="BridgeSettings.heightMode"/>. On the two automatic modes the deck works its own
    /// height out from what is underneath and this Y is ignored; <see cref="heightOffset"/> is the
    /// per-node adjustment that still applies there. On <see cref="BridgeHeightMode.Free"/> the Y is
    /// the deck and the offset is added to it.
    ///
    /// All values are in the generator's local space.
    /// </summary>
    [System.Serializable]
    public class BridgeNode
    {
        [Tooltip("Middle of the driving surface at this point, in the generator's local space. On " +
                 "the automatic height modes only X and Z are used — the deck finds its own height " +
                 "from the ground and the lava below it.")]
        public Vector3 position;

        [Tooltip("Full width of the driving surface here, edge to edge, in metres. Ignored while " +
                 "Uniform Width is on in the settings, which is the setting that guarantees the " +
                 "deck can never narrow. A kart here is 1.65 m across, so 14 m is eight and a half " +
                 "abreast and 16 m is comfortable for a twelve-kart field.")]
        [Min(3f)] public float width = 16f;

        [Tooltip("Raises or lowers the deck here, in metres, on top of whatever the height mode " +
                 "worked out. This is the per-node height control on the automatic modes — use it " +
                 "to lift one span clear of something without moving the rest of the bridge.")]
        public float heightOffset;

        [Tooltip("Extra bank here, in degrees, added on top of whatever the automatic banking works " +
                 "out from the corner. Positive raises the right-hand edge. Leave at 0 and let the " +
                 "corner bank itself.")]
        [Range(-60f, 60f)] public float bank;

        [Tooltip("Scales the parapet height here. 1 is the height set in the settings; 0 drops the " +
                 "parapet flush so the edge is open — which is how you make the mouth of the bridge " +
                 "meet an open shore. The driving surface itself is unaffected.")]
        [Range(0f, 2f)] public float wallScale = 1f;

        public BridgeNode() { }

        public BridgeNode(Vector3 position, float width)
        {
            this.position = position;
            this.width = width;
        }

        public BridgeNode Clone()
        {
            return (BridgeNode)MemberwiseClone();
        }
    }
}
