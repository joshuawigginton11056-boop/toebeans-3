using UnityEngine;

namespace RaceTrack
{
    /// <summary>How the road texture is laid across the width of the track.</summary>
    public enum RoadUvMode
    {
        /// <summary>
        /// U runs in real metres, so a tile is the same physical size across the track as it is
        /// along it. This is what you want for tarmac, gravel, grass, ice — anything that should
        /// look the same size everywhere.
        /// </summary>
        Metres = 0,

        /// <summary>
        /// U runs 0 to <see cref="TrackSettings.uvTilesAcross"/> from edge to edge whatever the
        /// track is doing. Use this for a texture that paints the road itself — lane lines, a
        /// centre stripe, a start grid — since it stays registered to the edges.
        /// </summary>
        Normalised = 1
    }

    /// <summary>
    /// Everything that shapes the track but is not per-node. A plain serializable class, so the same
    /// settings can be authored in the inspector or handed to <see cref="TrackMeshBuilder"/> from a
    /// test.
    /// </summary>
    [System.Serializable]
    public class TrackSettings
    {
        [Header("Layout")]
        [Tooltip("Joins the last node back to the first, seamlessly — no end caps, no kink, and the " +
                 "banking is closed round the loop so the surface meets itself exactly. This is the " +
                 "normal way to build a circuit. Turn it off for a point-to-point stage.")]
        public bool closedLoop = true;

        [Tooltip("Forces every cross-section to Track Width and ignores the per-node widths. This " +
                 "is the setting that guarantees the racing surface can never narrow or swell " +
                 "anywhere on the lap. Turn it off only if you deliberately want a pinch point.")]
        public bool uniformWidth = true;

        [Tooltip("Full width of the racing surface in metres while Uniform Width is on. A kart here " +
                 "is 1.65 m across, so 14 m is eight and a half abreast — eight-up racing with room " +
                 "to overtake, and about what a Mario Kart circuit runs. Below 12 m it starts to " +
                 "feel like a corridor; past 20 m it reads as a runway.")]
        [Min(2f)] public float trackWidth = 14f;

        [Header("Resolution")]
        [Tooltip("Distance between cross-sections along the straights, in metres. Corners get more " +
                 "on their own — see Degrees Per Section.")]
        [Range(0.5f, 20f)] public float sectionSpacing = 3f;

        [Tooltip("Extra cross-sections through bends: the most the track may turn between two of " +
                 "them. This is what makes a corner read as a curve rather than as a run of flats, " +
                 "and it costs triangles only where the track actually bends. 4 degrees is smooth " +
                 "at racing speed.")]
        [Range(0.5f, 30f)] public float degreesPerSection = 4f;

        [Tooltip("Spans across the racing surface. The surface is dead flat either way, so this is " +
                 "only about how finely the road texture and lightmaps are sampled across the width.")]
        [Range(1, 32)] public int crossSegments = 8;

        [Tooltip("How the curve is spaced through the nodes. 0.5 is centripetal and can never loop " +
                 "back through itself whatever the node spacing — leave it there. 0 is the uniform " +
                 "curve, rounder through evenly spaced nodes but it overshoots and kinks when the " +
                 "spacing is uneven. 1 is chordal.")]
        [Range(0f, 1f)] public float curveAlpha = 0.5f;

        [Header("Banking")]
        [Tooltip("How much of the ideal bank for each corner to actually apply. 1 leans the track " +
                 "into every turn the way a Mario Kart circuit does; 0 leaves it dead flat and " +
                 "leans only where you have set a node's bank by hand.")]
        [Range(0f, 1f)] public float autoBank = 1f;

        [Tooltip("The speed the automatic banking is tuned for, in metres per second. The bank is " +
                 "the angle at which a kart at this speed would sit flat in its seat through the " +
                 "corner. 18 m/s is a fast kart.")]
        [Range(1f, 80f)] public float bankSpeed = 18f;

        [Tooltip("Ceiling on the automatic bank, in degrees. Tight corners would otherwise ask for " +
                 "near-vertical walls. 20-25 is the Mario Kart look; push it past 45 for a bowl.")]
        [Range(0f, 89f)] public float maxAutoBank = 22f;

        [Tooltip("Distance over which the automatic bank is averaged, in metres. Raw corner " +
                 "curvature is twitchy and would ripple the surface; this is what turns the bank " +
                 "into a long ease in and out of the corner. Roughly a second of travel is right.")]
        [Range(0f, 200f)] public float bankSmoothing = 25f;

        [Tooltip("How hard the surface fights to stay level. 1 keeps the track flat side to side " +
                 "however it climbs and turns, which is what you want for driving. Lower it to let " +
                 "the ribbon carry its own twist around a corkscrew or a full vertical loop. It " +
                 "fades out on its own where the track goes near-vertical, since 'level' means " +
                 "nothing there.")]
        [Range(0f, 1f)] public float keepLevel = 1f;

        [Header("Edges")]
        [Tooltip("Width of the rumble strip outside the racing surface, each side, in metres. It " +
                 "is flush with the road — never a lip — so it is drivable margin and takes its own " +
                 "material. 0 removes it.")]
        [Range(0f, 10f)] public float kerbWidth = 1.5f;

        [Tooltip("Height of the barrier wall outside the kerb, in metres. 0 leaves the edge open " +
                 "and the track becomes a bare slab you can fall off.")]
        [Range(0f, 20f)] public float wallHeight = 1.2f;

        [Tooltip("Thickness of the barrier wall, in metres.")]
        [Range(0.05f, 5f)] public float wallThickness = 0.5f;

        [Tooltip("How far the top of the barrier leans outwards from its foot, in metres. A little " +
                 "lean stops a kart climbing the wall and reads as a race barrier rather than a box.")]
        [Range(0f, 3f)] public float wallLean = 0.15f;

        [Tooltip("Thickness of the slab under the road, in metres. This is what gives a track " +
                 "hanging in the air a solid underside instead of a paper edge.")]
        [Range(0.05f, 20f)] public float deckThickness = 1f;

        [Header("Texture mapping")]
        [Tooltip("How the road texture runs across the width. Metres keeps a tile the same real " +
                 "size in both directions; Normalised registers it to the edges so painted lane " +
                 "markings stay put.")]
        public RoadUvMode roadUvMode = RoadUvMode.Metres;

        [Tooltip("Metres per texture tile, along the track and (in Metres mode) across it.")]
        [Range(0.25f, 40f)] public float uvMetresPerTile = 4f;

        [Tooltip("Tiles from edge to edge in Normalised mode. 1 stretches one copy of the texture " +
                 "over the whole width.")]
        [Range(1, 32)] public int uvTilesAcross = 1;

        [Tooltip("Metres per stripe on the kerbs. This is separate from the road because a rumble " +
                 "strip wants a much tighter repeat than tarmac does.")]
        [Range(0.1f, 20f)] public float kerbMetresPerStripe = 2f;

        [Tooltip("On a closed loop, stretches the along-track tiling very slightly so a whole " +
                 "number of tiles fits the lap. Without it the texture meets a fraction of a tile " +
                 "out of step at the start line and draws a visible seam across the road.")]
        public bool matchSeamTiling = true;

        [Header("Guards")]
        [Tooltip("The tightest corner radius you intend to drive, in metres. Corners tighter than " +
                 "this are flagged in the inspector and in the scene view. A kart at 15 m/s wants " +
                 "20-25 m; this is a driving limit, not a geometric one, so nothing enforces it " +
                 "for you.")]
        [Range(1f, 300f)] public float minCornerRadius = 25f;

        [Tooltip("Smallest node spacing an insert may leave behind, as a multiple of the local " +
                 "half-width. Packing nodes together is what forces a corner too tight to build, " +
                 "so this refuses the pile-up rather than letting the mesh fold. 0 turns it off.")]
        [Range(0f, 2f)] public float minNodeSpacing = 0.5f;

        [Tooltip("Flip if your transform has a negative scale on one axis and the track has turned " +
                 "inside out.")]
        public bool flipWinding;

        /// <summary>
        /// Half the width of the widest part of the cross-section — the outside of the barrier.
        ///
        /// This, not the road half-width, is the number a corner radius has to beat: the mesh folds
        /// when the sweep turns tighter than its own outermost point, and that point is the top
        /// outer corner of the wall, not the edge of the tarmac.
        /// </summary>
        public float OuterHalfWidth(float roadHalfWidth)
        {
            return roadHalfWidth + kerbWidth + wallThickness + wallLean;
        }

        public TrackSettings Clone()
        {
            return (TrackSettings)MemberwiseClone();
        }
    }
}
