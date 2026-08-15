using UnityEngine;

namespace RockBridge
{
    /// <summary>How the deck decides what height to run at.</summary>
    public enum BridgeHeightMode
    {
        /// <summary>
        /// One level deck across the crossing, easing down onto the ground at both ends. The level
        /// is <see cref="BridgeSettings.deckHeight"/> above the highest thing the bridge passes
        /// over, so raising that one number lifts the whole span and the legs grow to reach it.
        ///
        /// This is the default, and it is what a bridge over a pool wants: flat to drive, and tied
        /// into the shore at both ends whatever height the middle is at.
        /// </summary>
        LevelSpan = 0,

        /// <summary>
        /// The deck holds <see cref="BridgeSettings.deckHeight"/> above whatever is below it the
        /// whole way, smoothed out over <see cref="BridgeSettings.heightSmoothing"/> metres. A
        /// causeway that follows the lie of the land rather than spanning it. Still lands on the
        /// ground at both ends.
        /// </summary>
        FollowGround = 1,

        /// <summary>
        /// Node heights are the deck, exactly as dragged, and nothing is measured off the ground.
        /// Take this when you want to hand-shape a profile the automatic modes will not give you.
        /// The legs still find their own length.
        /// </summary>
        Free = 2
    }

    /// <summary>
    /// Everything that shapes the bridge but is not per-node. A plain serializable class, so the
    /// same settings can be authored in the inspector or handed to
    /// <see cref="BridgeMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class BridgeSettings
    {
        // ------------------------------------------------------------------------ height

        [Header("Height")]
        [Tooltip("How the deck decides its height. Level Span is the bridge: flat across the middle, " +
                 "easing down onto the ground at both ends. Follow Ground is a causeway. Free hands " +
                 "the height back to the nodes.")]
        public BridgeHeightMode heightMode = BridgeHeightMode.LevelSpan;

        [Tooltip("How high the driving surface sits above what it crosses, in metres. This is the " +
                 "one number to drag: raise it and the whole span lifts, and the rock legs grow " +
                 "downwards on their own to meet the ground that is now further away.")]
        [Range(0f, 120f)] public float deckHeight = 12f;

        [Tooltip("Distance at each end over which the deck eases down from the span onto the " +
                 "ground, in metres. The whole climb happens here, so a long approach is a gentle " +
                 "one.\n\nThis is the setting that decides whether karts fly off the top of the " +
                 "ramp, and the gradient is not what to judge it by — what a suspension feels is " +
                 "how tightly the ramp flattens out at the crest. The inspector reports that as a " +
                 "vertical radius and as g at racing speed. 90 m carries a 12 m deck at about a " +
                 "third of a g; halve it and the same deck throws the kart.")]
        [Range(5f, 400f)] public float approachLength = 90f;

        [Tooltip("Lifts the middle of the span above the level by this many metres — the gentle hump " +
                 "of a real bridge. It flattens to nothing at both ends on its own, so it never " +
                 "tilts a landing. Keep it small; it is a look, not a ramp.")]
        [Range(0f, 20f)] public float arch = 2f;

        [Tooltip("How far the last stretch of deck sits into the ground at each end, in metres.\n\n" +
                 "Leave this at 0 and press Blend Terrain Into Landings instead — that reshapes the " +
                 "ground to the deck and is the only way the join is genuinely seamless. Raise it " +
                 "only if you are not going to blend: a deck landing flush on unblended ground can " +
                 "have its leading edge standing a few centimetres proud, and an edge is what a " +
                 "kart catches.\n\n" +
                 "Note that a non-zero sink and the terrain blend pull against each other — the " +
                 "sink puts the deck below the ground and the blend brings the ground down to the " +
                 "deck — so with both on, press the blend once and no more.")]
        [Range(0f, 3f)] public float landingSink;

        [Tooltip("Distance over which Follow Ground averages the ground height, in metres. Raw " +
                 "ground under a wide deck is far too twitchy to drive on. No effect on the other " +
                 "height modes.")]
        [Range(0f, 300f)] public float heightSmoothing = 60f;

        [Tooltip("Where the bridge reads the world from. Auto uses the terrain heightmap for the " +
                 "solid floor the legs stand on and colliders for the surface the deck must clear, " +
                 "which is what gets a pool sitting in a terrain basin right.")]
        public BridgeGroundMode groundMode = BridgeGroundMode.Auto;

        [Tooltip("Which colliders count as ground. Leave it on Everything unless props are " +
                 "confusing the probe.")]
        public LayerMask groundMask = ~0;

        [Tooltip("Height the Flat ground mode sits at, in world metres. Also the fallback height " +
                 "wherever a probe finds nothing at all under the bridge.")]
        public float flatGroundHeight;

        [Tooltip("Overrides the measured surface height with a fixed world Y when working out the " +
                 "span's level. Set this when the thing being crossed has no collider to probe — a " +
                 "lava river, for instance — and type its surface height here instead.")]
        public bool useFixedDatum;

        [Tooltip("The world Y the deck is held Deck Height above, while Use Fixed Datum is on.")]
        public float fixedDatum;

        // ------------------------------------------------------------------------ layout

        [Header("Deck")]
        [Tooltip("Forces every cross-section to Deck Width and ignores the per-node widths. This is " +
                 "the setting that guarantees the driving surface can never narrow or swell " +
                 "anywhere on the crossing. Turn it off only if you deliberately want a pinch point.")]
        public bool uniformWidth = true;

        [Tooltip("Full width of the driving surface in metres while Uniform Width is on. A kart " +
                 "here is 1.65 m across, so 16 m is nearly ten abreast — room for a twelve-kart " +
                 "field to cross without the pack having to file into a line. Below 12 m it starts " +
                 "to feel like a corridor.")]
        [Min(3f)] public float deckWidth = 16f;

        [Tooltip("Thickness of the slab under the driving surface, in metres. This is the depth of " +
                 "rock you see from the side and from below, so it is most of what makes the bridge " +
                 "read as heavy rather than as a plank.")]
        [Range(0.2f, 20f)] public float deckThickness = 2.5f;

        [Tooltip("How much deeper the middle of the underside hangs than its edges, in metres. A " +
                 "flat soffit reads as a poured slab; a curved one reads as a beam carved out of " +
                 "rock. Purely cosmetic — nothing drives down there.")]
        [Range(0f, 6f)] public float soffitCamber = 0.9f;

        [Tooltip("Width of the flush margin down either edge of the driving surface, in metres. It " +
                 "is level with the deck — never a lip — so it is drivable room that takes its own " +
                 "material. 0 removes it.")]
        [Range(0f, 8f)] public float vergeWidth = 1.2f;

        [Header("Parapet")]
        [Tooltip("Height of the rock wall down either side, in metres. This is what keeps a " +
                 "twelve-kart field on the bridge. 0 leaves the edge open and the deck becomes a " +
                 "bare slab you can fall off.")]
        [Range(0f, 12f)] public float parapetHeight = 1.6f;

        [Tooltip("Thickness of the parapet wall, in metres. A rock parapet wants to look quarried " +
                 "rather than cast, so it is thick — under about half a metre it reads as a fence.")]
        [Range(0.1f, 6f)] public float parapetThickness = 1.1f;

        [Tooltip("How far the top of the parapet leans outwards from its foot, in metres. A little " +
                 "lean stops a kart climbing the wall and reads as masonry rather than as a box.")]
        [Range(0f, 3f)] public float parapetLean = 0.18f;

        [Tooltip("How ragged the top of the parapet is, in metres of random rise and fall. This is " +
                 "the only roughness anywhere near the driving surface, and it is on the wall top " +
                 "only — the deck itself is left dead smooth on purpose.")]
        [Range(0f, 2f)] public float parapetRelief = 0.35f;

        [Tooltip("Length of one block of parapet, in metres. The relief changes from block to " +
                 "block, so this is really 'how big are the stones'.")]
        [Range(1f, 30f)] public float parapetBlockLength = 6f;

        // -------------------------------------------------------------------------- legs

        [Header("Rock legs")]
        [Tooltip("Builds the rock legs that carry the deck. They find their own length: the top is " +
                 "the underside of the deck and the foot is the solid ground below, so raising Deck " +
                 "Height makes every leg longer without anything else being touched.")]
        public bool buildPiers = true;

        [Tooltip("Distance between legs along the bridge, in metres. Wider spacing reads as a " +
                 "bolder span and costs fewer triangles; closer reads as a viaduct.")]
        [Range(8f, 200f)] public float pierSpacing = 42f;

        [Tooltip("Shortest leg worth building, in metres. Below this the deck is close enough to " +
                 "the ground that a leg would be a stub, and the abutment skirt covers it instead.")]
        [Range(0.5f, 20f)] public float minPierHeight = 3f;

        [Tooltip("Width of a leg across the bridge, as a fraction of the deck's width. A wide deck " +
                 "on a thin post looks unsupported; around half is the proportion that reads as " +
                 "carrying the weight.")]
        [Range(0.15f, 1f)] public float pierWidthRatio = 0.5f;

        [Tooltip("Thickness of a leg along the bridge, in metres, measured at the top.")]
        [Range(1f, 40f)] public float pierThickness = 6f;

        [Tooltip("How much wider a leg gets per metre of its own height. This is what makes a tall " +
                 "leg look like it is holding something up: at 0.04 a 20 m leg is nearly twice as " +
                 "thick at the foot as at the top, and a short one is barely tapered at all. 0 " +
                 "gives straight columns.")]
        [Range(0f, 0.15f)] public float pierBatter = 0.045f;

        [Tooltip("Ceiling on that spread, as a multiple of the leg's own top size. Without it a very " +
                 "tall leg over a deep basin grows into a mountain of its own.")]
        [Range(1f, 8f)] public float pierMaxSpread = 3.2f;

        [Tooltip("Faces around a leg. Odd numbers read as broken rock rather than as a machined " +
                 "post; 7 or 9 is the low-poly look this map is built in.")]
        [Range(3, 24)] public int pierSides = 7;

        [Tooltip("Height of one band of rock up a leg, in metres. Smaller bands give more strata " +
                 "and more triangles.")]
        [Range(1f, 20f)] public float pierBandHeight = 4.5f;

        [Tooltip("How far the rock of a leg wanders in and out, as a fraction of its radius. This " +
                 "is what stops it being a cylinder. Past about 0.35 the bands start to read as " +
                 "separate lumps.")]
        [Range(0f, 0.6f)] public float pierRoughness = 0.22f;

        [Tooltip("How far a leg flares out where it meets the deck, as a fraction of its top size — " +
                 "the haunch. This is the join that makes the deck look grown out of the leg " +
                 "instead of balanced on it.")]
        [Range(0f, 2f)] public float haunchSpread = 0.55f;

        [Tooltip("Height of that flare, in metres.")]
        [Range(0f, 20f)] public float haunchHeight = 5f;

        [Tooltip("How far the top of a leg pushes up inside the deck slab, in metres. It has to be " +
                 "more than nothing: a leg stopping exactly on the underside puts two surfaces in " +
                 "the same plane, and the renderer then flickers between them as the camera moves.")]
        [Range(0.05f, 3f)] public float pierTopEmbed = 0.6f;

        [Tooltip("How far a leg's foot sinks below the ground, in metres. Same reason — a foot " +
                 "landing exactly on the ground leaves a hairline of daylight under it wherever the " +
                 "terrain dips between two of its corners.")]
        [Range(0.1f, 10f)] public float footingDepth = 2f;

        // --------------------------------------------------------------------- abutments

        [Header("Landings")]
        [Tooltip("Fills the wedge between the underside of the deck and the ground where the bridge " +
                 "meets the shore, so the crossing rises out of the bank instead of ending in a " +
                 "slab floating above it. Where the gap grows past Landing Fill Depth the legs take " +
                 "over.")]
        public bool buildAbutments = true;

        [Tooltip("How deep a gap under the deck the landing fill will close, in metres. Beyond this " +
                 "the drop belongs to a leg, so this is really 'where does the bank stop and the " +
                 "viaduct start'.\n\nKeep it near Shortest Leg Worth Building — the two are meant " +
                 "to hand over to each other. Set it much higher and the fill stops being a bank " +
                 "at all: it runs the length of the crossing as a pair of walls under the deck, " +
                 "which is the single most likely way to end up with a bridge that looks like a " +
                 "trough. The inspector reports how far it actually ran.")]
        [Range(1f, 60f)] public float abutmentDepth = 5f;

        [Tooltip("How far the landing fill splays outwards per metre of its own depth. A buttress " +
                 "that widens as it drops reads as bearing the load into the bank.")]
        [Range(0f, 1.5f)] public float abutmentFlare = 0.35f;

        // ---------------------------------------------------------------------- sampling

        [Header("Resolution")]
        [Tooltip("Distance between cross-sections along the straights, in metres. Corners get more " +
                 "on their own — see Degrees Per Section.")]
        [Range(0.5f, 20f)] public float sectionSpacing = 3f;

        [Tooltip("Extra cross-sections through bends: the most the bridge may turn between two of " +
                 "them. This is what makes a curve read as a curve rather than as a run of flats, " +
                 "and it costs triangles only where the deck actually bends. 4 degrees is smooth at " +
                 "racing speed.")]
        [Range(0.5f, 30f)] public float degreesPerSection = 4f;

        [Tooltip("Spans across the driving surface. The surface is dead flat either way, so this is " +
                 "only about how finely the rock texture and lightmaps are sampled across it.")]
        [Range(1, 32)] public int crossSegments = 8;

        [Tooltip("How the curve is spaced through the nodes. 0.5 is centripetal and can never loop " +
                 "back through itself whatever the node spacing — leave it there. 0 overshoots and " +
                 "kinks when the spacing is uneven.")]
        [Range(0f, 1f)] public float curveAlpha = 0.5f;

        // ----------------------------------------------------------------------- banking

        [Header("Banking")]
        [Tooltip("How much of the ideal bank for each corner to actually apply. 1 leans the deck " +
                 "into every turn; 0 leaves it dead flat and leans only where you have set a node's " +
                 "bank by hand.")]
        [Range(0f, 1f)] public float autoBank = 1f;

        [Tooltip("The speed the automatic banking is tuned for, in metres per second. The bank is " +
                 "the angle at which a kart at this speed would sit flat in its seat through the " +
                 "corner. 18 m/s is a fast kart.")]
        [Range(1f, 80f)] public float bankSpeed = 18f;

        [Tooltip("Ceiling on the automatic bank, in degrees. A rock bridge wants less lean than a " +
                 "race circuit — much past 15 and the parapet starts to overhang the drop.")]
        [Range(0f, 60f)] public float maxAutoBank = 12f;

        [Tooltip("Distance over which the automatic bank is averaged, in metres. Raw corner " +
                 "curvature is twitchy and would ripple the surface; this is what turns the bank " +
                 "into a long ease in and out of the corner.")]
        [Range(0f, 200f)] public float bankSmoothing = 30f;

        // ------------------------------------------------------------------------- look

        [Header("Rock")]
        [Tooltip("Changes every random decision the rock makes — which way each band of a leg " +
                 "wanders, how the parapet's top breaks up. Same seed, same bridge, every rebuild.")]
        public int seed = 20260814;

        [Tooltip("Metres per texture tile on the deck and the rock.")]
        [Range(0.25f, 40f)] public float uvMetresPerTile = 6f;

        // ----------------------------------------------------------------------- guards

        [Header("Guards")]
        [Tooltip("The tightest corner radius you intend to drive, in metres. Corners tighter than " +
                 "this are flagged in the inspector and in the scene view. A kart at 15 m/s wants " +
                 "20-25 m; this is a driving limit, not a geometric one, so nothing enforces it.")]
        [Range(1f, 300f)] public float minCornerRadius = 30f;

        [Tooltip("Steepest approach you are willing to drive, in degrees. The inspector warns when " +
                 "the ramps come out steeper than this, and tells you how much longer to make them." +
                 "\n\nThis is the softer of the two ramp guards and it is set for a kart, not a " +
                 "road: 14 degrees is a quarter grade, which a kart climbs without noticing. The " +
                 "guard that actually decides whether a ramp is drivable is the crest reading " +
                 "above it, in g.")]
        [Range(1f, 45f)] public float maxGradient = 14f;

        [Tooltip("The speed you expect karts to arrive at, in metres per second. Used only to " +
                 "report how hard the crest of the approach ramp hits — a ramp that is gentle in " +
                 "degrees can still launch a kart if it flattens out over too short a distance, and " +
                 "that depends on speed. This project's kart tops out near 26 m/s.")]
        [Range(1f, 60f)] public float crossingSpeed = 22f;

        [Tooltip("Flip if your transform has a negative scale on one axis and the bridge has turned " +
                 "inside out.")]
        public bool flipWinding;

        /// <summary>
        /// Half the width of the widest part of the cross-section — the outside of the parapet.
        ///
        /// This, not the deck half-width, is the number a corner radius has to beat: the mesh folds
        /// when the sweep turns tighter than its own outermost point, and that point is the top
        /// outer corner of the parapet, not the edge of the driving surface.
        /// </summary>
        public float OuterHalfWidth(float deckHalfWidth)
        {
            return deckHalfWidth + Mathf.Max(0f, vergeWidth)
                 + (parapetHeight > 0.01f ? Mathf.Max(0f, parapetThickness) + Mathf.Max(0f, parapetLean) : 0f);
        }

        public BridgeSettings Clone()
        {
            return (BridgeSettings)MemberwiseClone();
        }
    }
}
