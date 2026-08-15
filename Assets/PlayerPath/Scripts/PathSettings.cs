using System.Collections.Generic;
using UnityEngine;

namespace PlayerPath
{
    /// <summary>Where the centreline of the path comes from.</summary>
    public enum PathRouteMode
    {
        /// <summary>Follows points clicked and dragged in the scene view. This is the one to use.</summary>
        Waypoints = 0,

        /// <summary>Follows a Spline Container, for anyone who would rather author with splines.</summary>
        Spline = 1
    }

    /// <summary>How the generated UVs are laid out.</summary>
    public enum PathUVMode
    {
        /// <summary>Measured in metres: across the path and along it on the deck, along and up on
        /// the walls. What an ordinary tiling stone or brick texture wants.</summary>
        PathAligned = 0,

        /// <summary>One 0-1 tile stretched over the whole path, for shaders that mask on UV.</summary>
        Normalized = 1,

        /// <summary>Projected from world position, tiling every uvScale metres.</summary>
        WorldPlanar = 2
    }

    /// <summary>What runs along the edges of the path to keep the player on it.</summary>
    public enum PathEdgeStyle
    {
        /// <summary>A low brick parapet, laid in courses with a running bond.</summary>
        BrickWall = 0,

        /// <summary>A single course of long kerbstones. The lowest, least fussy edge.</summary>
        Kerb = 1,

        /// <summary>Brick, with the top courses cut away at intervals into crenellations. The gaps
        /// drop to a lower course rather than to the deck, so they are still a wall.</summary>
        Battlement = 2,

        /// <summary>Squat posts at intervals with a lower wall running between them.</summary>
        Pillars = 3
    }

    /// <summary>Whether the deck ramps with the ground or breaks into flights of steps.</summary>
    public enum PathStepMode
    {
        /// <summary>Never step. The deck follows the ground however steep it gets.</summary>
        None = 0,

        /// <summary>Step wherever the ground is steeper than <c>stepAngle</c>, ramp everywhere else.
        /// One route then gives both the gentle traverses and the stairs down the steep faces.</summary>
        Auto = 1,

        /// <summary>Step the whole way, however gentle the ground.</summary>
        Always = 2
    }

    /// <summary>
    /// Every knob that shapes the path. A plain serializable class, so the same settings can be
    /// authored in the inspector or handed to <see cref="PathMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class PathSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for different paving and brickwork along the same route.")]
        public int seed = 20260807;

        // ---------------------------------------------------------------- route

        [Header("Route")]
        [Tooltip("Where the centreline comes from.\n\n" +
                 "Waypoints follows the points you click in the scene view.\n\n" +
                 "Spline follows a Spline Container.")]
        public PathRouteMode routeMode = PathRouteMode.Waypoints;

        [Tooltip("Positions are local to this object; drag them in the scene view.")]
        public List<Vector3> waypoints = new List<Vector3>();

        [Tooltip("Distance between cross-sections. Smaller hugs the ground more closely and costs " +
                 "proportionally more triangles.")]
        [Range(0.3f, 4f)] public float stationSpacing = 0.9f;

        [Tooltip("How much the centreline is eased before the deck is laid on it. A path is built, " +
                 "not poured: it bridges the small bumps rather than reproducing every one.")]
        [Range(0, 8)] public int routeSmoothing = 2;

        [Tooltip("Clearance between the ground and the underside of the paving, in metres. A little " +
                 "lift stops the path z-fighting with the terrain. The stones themselves sit a " +
                 "joint's depth above this again, so the deck you walk on is higher than this " +
                 "number by Joint Depth.")]
        [Range(0f, 0.6f)] public float surfaceLift = 0.06f;

        // ---------------------------------------------------------------- deck

        [Header("Deck")]
        [Tooltip("Walkable width between the two edges, in metres. This is the width the player " +
                 "actually has; the walls are built outside it.")]
        [Range(1f, 16f)] public float pathWidth = 3.2f;

        [Tooltip("Cross-sections across the deck. Drives the poly budget together with spacing, and " +
                 "sets how fine the paving grid is.")]
        [Range(2, 24)] public int lateralSegments = 8;

        [Tooltip("How much the width breathes along the route, so the path does not read as an " +
                 "extruded ruler. The walkable width never drops below what a bend allows.")]
        [Range(0f, 0.4f)] public float widthVariation = 0.05f;

        // ---------------------------------------------------------------- paving

        [Header("Paving")]
        [Tooltip("Size of a flagstone, in metres.")]
        [Range(0.3f, 6f)] public float flagstoneSize = 1.1f;

        [Tooltip("How irregular the flagstone boundaries are. 0 gives a grid of identical squares.")]
        [Range(0f, 1f)] public float flagstoneJitter = 0.65f;

        [Tooltip("How far the stones pull back from each other, in metres. This is the width of the " +
                 "joints between them, and the single most visible knob here.")]
        [Range(0f, 0.4f)] public float jointWidth = 0.07f;

        [Tooltip("How deep the joints are cut, in metres. Reads as the thickness of the stones.")]
        [Range(0.02f, 0.6f)] public float jointDepth = 0.09f;

        [Tooltip("How much the stones sit at different heights from each other. A little makes the " +
                 "paving read as laid by hand; a lot makes it read as broken.")]
        [Range(0f, 0.25f)] public float flagstoneRelief = 0.035f;

        [Tooltip("Fraction of the stones that are missing altogether, showing whatever is beneath " +
                 "the paving.")]
        [Range(0f, 0.4f)] public float brokenStones = 0.06f;

        [Tooltip("Whether what shows between and beneath the stones glows. This is the layer of " +
                 "heat under the path, and it is what ties the path to the lava rather than the " +
                 "brickwork does.")]
        public bool glowingJoints = true;

        // ---------------------------------------------------------------- steps

        [Header("Steps")]
        [Tooltip("Whether the deck ramps with the ground or breaks into flights of steps.\n\n" +
                 "Auto steps only where the ground is too steep to walk up, which is what turns one " +
                 "drawn route into traverses joined by stairs.")]
        public PathStepMode stepMode = PathStepMode.Auto;

        [Tooltip("Auto mode. Ground steeper than this becomes steps. Below it the deck just ramps.")]
        [Range(5f, 40f)] public float stepAngle = 15f;

        [Tooltip("Height of one step, in metres. Somewhere near 0.2 is comfortable to walk up; " +
                 "much more and the player has to jump it.")]
        [Range(0.08f, 0.6f)] public float stepRise = 0.24f;

        [Tooltip("Depth of one step, in metres. Steps are never shallower than the station spacing, " +
                 "so drop the spacing if you want very fine stairs.")]
        [Range(0.3f, 3f)] public float stepTread = 1f;

        // ---------------------------------------------------------------- edges

        [Header("Edges")]
        [Tooltip("What runs along the edges to keep the player on the path.")]
        public PathEdgeStyle edgeStyle = PathEdgeStyle.BrickWall;

        [Tooltip("How high the edge stands above the deck, in metres. Kept low on purpose: it is a " +
                 "kerb to stop a fall, not a wall to hide behind.")]
        [Range(0f, 2.5f)] public float wallHeight = 0.55f;

        [Tooltip("How thick the edge is, in metres.")]
        [Range(0.08f, 1.2f)] public float wallThickness = 0.3f;

        [Tooltip("Gap between the paving and the foot of the wall, in metres. This is where the " +
                 "heat under the path shows through, so it reads as a glowing line down both sides.")]
        [Range(0f, 0.5f)] public float seamWidth = 0.07f;

        [Tooltip("Length of one brick, in metres.")]
        [Range(0.15f, 2f)] public float brickLength = 0.55f;

        [Tooltip("Height of one course of bricks, in metres.")]
        [Range(0.05f, 0.6f)] public float brickCourse = 0.16f;

        [Tooltip("Width of the mortar joint between bricks, in metres. The wall has a solid core " +
                 "behind the bricks, so this is a recess rather than a hole.")]
        [Range(0f, 0.12f)] public float mortarGap = 0.025f;

        [Tooltip("How unevenly the bricks are laid: how far each one sits proud of, below or along " +
                 "from where a machine would have put it.")]
        [Range(0f, 1f)] public float brickJitter = 0.5f;

        [Tooltip("Fraction of the bricks that are still glowing. Scattered through the wall these " +
                 "read as heat trapped in the stone.")]
        [Range(0f, 0.6f)] public float hotBrickChance = 0.1f;

        [Tooltip("Thickness of the coping stone capping the wall, in metres. 0 leaves the top " +
                 "course bare.")]
        [Range(0f, 0.35f)] public float capHeight = 0.07f;

        [Tooltip("How far the coping overhangs the wall on each side, in metres.")]
        [Range(0f, 0.25f)] public float capOverhang = 0.04f;

        [Tooltip("Battlement and Pillars. Distance from one merlon or post to the next, in metres.")]
        [Range(0.8f, 12f)] public float featureSpacing = 3f;

        [Tooltip("Battlement and Pillars. How much of that distance the merlon or post takes up.")]
        [Range(0.1f, 0.9f)] public float featureSize = 0.45f;

        [Tooltip("Pillars. How far a post stands out past the wall, in metres. It only grows " +
                 "outwards, so the walkable width stays the same.")]
        [Range(0f, 0.8f)] public float featureBulge = 0.18f;

        // ---------------------------------------------------------------- foundation

        [Header("Foundation")]
        [Tooltip("How far the outer face is buried into the ground, in metres. Keep this above the " +
                 "size of the bumps in your terrain or the path will show daylight under its edge.")]
        [Range(0f, 4f)] public float embedDepth = 0.8f;

        [Tooltip("How far the foundation is allowed to reach down before it gives up, in metres. " +
                 "Cut across a steep face the downhill side has a long way to fall, and without a " +
                 "limit one bad waypoint builds a wall to the bottom of the mountain.")]
        [Range(0.5f, 40f)] public float maxFoundation = 6f;

        // ---------------------------------------------------------------- output

        [Header("Output")]
        [Tooltip("How the UVs are laid out.\n\n" +
                 "Path Aligned measures them in metres, so a stone or brick texture tiles at its " +
                 "real size and stays square on both the deck and the walls.\n\n" +
                 "Normalised stretches one 0-1 tile over everything, for shaders that mask or remap " +
                 "on UV rather than tiling.\n\n" +
                 "World Planar tiles every uvScale metres, projected from world position.")]
        public PathUVMode uvMode = PathUVMode.PathAligned;

        [Tooltip("Metres per UV tile.")]
        [Range(0.1f, 20f)] public float uvScale = 2f;

        public PathSettings Clone()
        {
            var copy = (PathSettings)MemberwiseClone();
            copy.waypoints = new List<Vector3>(waypoints);
            return copy;
        }
    }
}
