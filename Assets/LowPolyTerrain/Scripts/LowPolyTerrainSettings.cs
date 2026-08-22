using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Every knob that shapes the terrain height field. Plain serializable class so the same
    /// settings can be authored in the inspector or handed to <see cref="LowPolyTerrainBuilder"/>
    /// from a headless test.
    /// </summary>
    [System.Serializable]
    public class LowPolyTerrainSettings
    {
        [Header("Seed")]
        [Tooltip("Change for a completely different world with the same budget and silhouette.")]
        public int seed = 20260811;

        [Header("Facets")]
        [Tooltip("Edge length of one low-poly facet, in metres. This is the single knob that sets " +
                 "the whole aesthetic: the height field is made exactly planar over each facet " +
                 "triangle, so the terrain shades as flat faces instead of a smooth blob. " +
                 "Smaller = finer, busier facets. 6-12 m reads well on a 250 m map.")]
        [Range(2f, 40f)] public float facetSize = 8f;

        [Tooltip("Randomise which way each facet quad is split. Off gives a uniform herringbone " +
                 "that reads as a woven pattern from the air; on breaks it up.")]
        public bool jitterDiagonals = true;

        [Tooltip("Pushes each lattice corner sideways before the height is sampled, so facets are " +
                 "not a perfect grid. Costs nothing and does most of the work of hiding the lattice.")]
        [Range(0f, 0.45f)] public float latticeJitter = 0.28f;

        [Header("Floor pan")]
        [Tooltip("Height of the flat datum the open ground sits at, in metres above terrain zero. " +
                 "Leave headroom below it if anything needs to be carved down into the ground.")]
        [Range(0f, 40f)] public float panHeight = 6f;

        [Tooltip("Peak-to-trough height of the rolling ground, in metres. This is the knob to watch " +
                 "for driveability - the inspector reports the steepest slope it produces.")]
        [Range(0f, 40f)] public float panRelief = 7f;

        [Tooltip("Size of the main rolls in the ground, in metres. Large values give long lazy " +
                 "swells; small values give a bumpy field a kart will bottom out on.")]
        [Range(15f, 300f)] public float panWavelength = 110f;

        [Tooltip("Layers of detail added to the ground. Each is half the size and half the height " +
                 "of the one before.")]
        [Range(1, 6)] public int panOctaves = 4;

        [Tooltip("Flattens the ground toward the datum, pulling the low ground up into broad plains " +
                 "and leaving the rolls as isolated rises. 0 leaves the noise as it comes.")]
        [Range(0f, 1f)] public float panFlatten = 0.35f;

        [Header("Hills and valley")]
        [Tooltip("Height of the hill country above the floor pan, in metres. 0 leaves the pan flat " +
                 "and the world is a bowl with a wall round it.\n\n" +
                 "This is a separate landform from Pan Relief, not a bigger version of it. Pan " +
                 "relief rolls the whole floor; hills stand up out of it as their own shapes and " +
                 "are held back from the valley, so there is somewhere flat left to build on.")]
        [Range(0f, 120f)] public float hillHeight = 0f;

        [Tooltip("Distance between one hill and the next, in metres.")]
        [Range(40f, 400f)] public float hillWavelength = 170f;

        [Tooltip("Layers of detail in the hills. Each is half the size and half the height of the " +
                 "one before.")]
        [Range(1, 5)] public int hillOctaves = 3;

        [Tooltip("How much of the map the hills claim, before the valley is cut out of them. Low " +
                 "values leave isolated knolls in open ground; high values give continuous hill " +
                 "country with the valley as the only way through.")]
        [Range(0f, 1f)] public float hillCoverage = 0.55f;

        [Tooltip("Width of the flat valley floor, in metres. This is the buildable ground - the " +
                 "hills are held off it entirely. 0 disables the valley and lets hills run over the " +
                 "whole map.")]
        [Range(0f, 400f)] public float valleyWidth = 0f;

        [Tooltip("Distance over which the hills climb away from the valley floor, in metres.\n\n" +
                 "This is the driveability knob, not Hill Height. The steepest the flanks can get " +
                 "is roughly atan(hillHeight / valleyBlend), so a 45 m hill over a 60 m blend is a " +
                 "37 degree wall no kart will climb, and the same hill over 130 m is a 19 degree " +
                 "slope it can.")]
        [Range(10f, 300f)] public float valleyBlend = 110f;

        [Tooltip("Direction the valley runs, in degrees, measured from +X turning toward +Z.")]
        [Range(0f, 180f)] public float valleyBearingDegrees = 35f;

        [Tooltip("Shifts the valley off the centre of the map, in metres, at right angles to its " +
                 "bearing. Positive moves it toward +Z.")]
        [Range(-200f, 200f)] public float valleyOffset = 0f;

        [Tooltip("How far the valley wanders off a straight line, in metres. Without it the flanks " +
                 "are two parallel walls and the valley reads as a trench rather than as ground.")]
        [Range(0f, 120f)] public float valleyWander = 0f;

        [Header("Pond basin")]
        [Tooltip("Radius of a basin scooped out of the floor, in metres. 0 disables it.\n\n" +
                 "The basin is carved last, after the hills and the wall, so it always wins. Keep " +
                 "it inside the playable area - centred under the wall it will cut a notch out of " +
                 "the mountain instead of a pond out of the field.")]
        [Range(0f, 200f)] public float pondRadius = 0f;

        [Tooltip("How far the basin floor sits below the surrounding ground, in metres. Watch this " +
                 "against Pan Height: there is nothing below terrain zero to carve into, so a basin " +
                 "deeper than the floor it starts from comes out clipped flat.")]
        [Range(0f, 60f)] public float pondDepth = 10f;

        [Tooltip("Where the basin sits, as a fraction of the map on each axis. (0,0) is the corner " +
                 "at terrain-local origin, (1,1) the opposite one.")]
        public Vector2 pondCenter = new Vector2(0.5f, 0.5f);

        [Tooltip("Fraction of the radius that is flat bottom rather than sloping bank. A pond needs " +
                 "a flat floor for the water plane to sit on without the surface cutting into the " +
                 "bank at one side and hovering at the other.")]
        [Range(0f, 0.9f)] public float pondFloorFlat = 0.35f;

        [Header("Mountain wall")]
        [Tooltip("Build the perimeter wall. Off leaves the pan alone, which is useful for judging " +
                 "the ground on its own.")]
        public bool buildWall = true;

        [Tooltip("How far the wall reaches in from the map edge, in metres. This is playable area " +
                 "you are giving up - the inspector reports what is left.")]
        [Range(5f, 120f)] public float wallWidth = 45f;

        [Tooltip("Height of the wall crest above the floor pan, in metres. The inspector warns if " +
                 "the crest would clip the terrain's own height ceiling.")]
        [Range(5f, 200f)] public float wallHeight = 75f;

        [Tooltip("How much the crest rises and falls along the perimeter, as a fraction of the " +
                 "wall height. 0 is a level rampart; high values give peaks and saddles.")]
        [Range(0f, 0.8f)] public float crestVariation = 0.42f;

        [Tooltip("Distance along the perimeter between one peak and the next, in metres.")]
        [Range(20f, 400f)] public float crestWavelength = 140f;

        [Tooltip("How far the foot of the wall wanders in and out, in metres. This is what stops " +
                 "the playable area being an obvious rounded rectangle.")]
        [Range(0f, 60f)] public float footWander = 16f;

        [Tooltip("Shape of the climb from foot to crest. 0.5 is an even S-curve. Lower pushes the " +
                 "steep part outward, leaving a wide gentle apron you can build on; higher makes " +
                 "the wall rise almost immediately.")]
        [Range(0.15f, 0.85f)] public float wallProfileBias = 0.42f;

        [Tooltip("Ridged detail carved into the wall face, in metres. This is what gives the wall " +
                 "gullies and spurs rather than a smooth ramp.")]
        [Range(0f, 30f)] public float wallRelief = 9f;

        [Tooltip("Size of the gullies and spurs on the wall face, in metres.")]
        [Range(10f, 200f)] public float wallReliefWavelength = 48f;

        [Header("Texturing")]
        [Tooltip("Paint the terrain layers by height and slope as well as shaping it. Because the " +
                 "height field is planar over each facet, slope is constant across a facet too - so " +
                 "a slope-driven rule paints whole facets and the texturing comes out as faceted as " +
                 "the geometry.")]
        public bool paintLayers = true;

        [Tooltip("Slope at which the ash flats start giving way to scorched ground, in degrees.")]
        [Range(0f, 90f)] public float scorchedSlopeStart = 10f;

        [Tooltip("Slope at which the ground is fully scorched, in degrees.")]
        [Range(0f, 90f)] public float scorchedSlopeFull = 22f;

        [Tooltip("Slope at which bare basalt starts showing through, in degrees.")]
        [Range(0f, 90f)] public float basaltSlopeStart = 26f;

        [Tooltip("Slope at which the ground is bare basalt, in degrees. This is most of the crater wall.")]
        [Range(0f, 90f)] public float basaltSlopeFull = 40f;

        [Tooltip("Height BELOW which molten ground pools, in metres. Lava collects in the low " +
                 "basins of the pan, so this rule runs the opposite way to a snowline.\n\n" +
                 "Watch the LOWER edge of the band (this minus Molten Band): lava is only full " +
                 "below that, so a band hanging under the floor of the pan paints almost nothing " +
                 "however generous this number looks.")]
        [Range(0f, 200f)] public float moltenHeight = 12f;

        [Tooltip("Height over which the molten ground fades out as the floor rises, in metres.")]
        [Range(0.5f, 100f)] public float moltenBand = 3f;

        [Tooltip("Lava will not cling to anything steeper than this, in degrees, so it pools on the " +
                 "flats instead of running up the crater wall.")]
        [Range(0f, 90f)] public float moltenMaxSlope = 16f;

        [Tooltip("Wobble applied to every texture threshold, as a fraction. Without it the bands are " +
                 "perfect contour lines and the map reads as a topographic map.")]
        [Range(0f, 1f)] public float textureNoise = 0.35f;

        [Tooltip("Size of that wobble, in metres.")]
        [Range(2f, 200f)] public float textureNoiseWavelength = 34f;

        [Header("Protected areas")]
        [Tooltip("Hold the ground near existing scene objects at its current height, so raising the " +
                 "world does not bury the props you have already placed. The shaper collects these " +
                 "from the scene; the inspector lists what it found.")]
        public bool protectExistingObjects = true;

        [Tooltip("Extra flat ground kept around each protected object, beyond its own footprint, " +
                 "in metres.")]
        [Range(0f, 60f)] public float protectionMargin = 12f;

        [Tooltip("Distance over which protected ground blends back into the generated world, in " +
                 "metres. Short blends leave a visible crater lip.")]
        [Range(1f, 80f)] public float protectionBlend = 22f;

        public LowPolyTerrainSettings Clone()
        {
            return (LowPolyTerrainSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// A patch of ground the shaper must leave at a fixed height. Terrain-local metres, so the
    /// builder never has to know where the terrain sits in the world.
    /// </summary>
    [System.Serializable]
    public struct ProtectedArea
    {
        public float centerX;
        public float centerZ;
        public float radius;
        public float height;

        public ProtectedArea(float centerX, float centerZ, float radius, float height)
        {
            this.centerX = centerX;
            this.centerZ = centerZ;
            this.radius = radius;
            this.height = height;
        }
    }
}
