using UnityEngine;

namespace Volcano
{
    /// <summary>What, if anything, is cut through the base of the cone for the track to use.</summary>
    public enum PassageMode
    {
        /// <summary>Solid mountain. No way through.</summary>
        None = 0,

        /// <summary>
        /// Cut the two mouths but build no tunnel. Use this when the passage itself is coming from
        /// the Cave Tunnel generator, so the cave's own walls fill the hole.
        /// </summary>
        PortalsOnly = 1,

        /// <summary>
        /// Cut the mouths and build the tunnel between them: rock walls, a flat drivable floor and
        /// an apron at each end that buries itself under the surrounding ground.
        /// </summary>
        Bore = 2
    }

    /// <summary>How the generated UVs are laid out.</summary>
    public enum VolcanoUVMode
    {
        /// <summary>Projected from world position, tiling every uvScale metres.</summary>
        WorldPlanar = 0,

        /// <summary>One 0-1 tile stretched over the whole cone, for shaders that mask on UV.</summary>
        Normalized = 1
    }

    /// <summary>
    /// Every knob that shapes the volcano. A plain serializable class, so the same settings can be
    /// authored in the inspector or handed straight to <see cref="VolcanoMeshBuilder"/> from a test.
    ///
    /// The defaults are tuned for a centrepiece on a 500 m map with a 378 m playable span: a 176 m
    /// footprint, which is big enough to dominate and small enough to drive a circuit around.
    /// </summary>
    [System.Serializable]
    public class VolcanoSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for a completely different mountain with the same silhouette budget.")]
        public int seed = 20260811;

        // ---------------------------------------------------------------- silhouette

        [Header("Silhouette")]
        [Tooltip("Radius of the foot of the cone, in metres. This is half the footprint, so keep an " +
                 "eye on how much map is left around it for the track.")]
        [Range(20f, 220f)] public float baseRadius = 88f;

        [Tooltip("Height of the crater rim above the foot, in metres.")]
        [Range(10f, 160f)] public float height = 55f;

        [Tooltip("Shape of the flank between the foot and the rim.\n\n" +
                 "Above 1 gives the concave stratovolcano profile: a shallow apron at the bottom " +
                 "steepening towards the summit. 1 is a straight-sided cone. Below 1 bulges out " +
                 "into a shield volcano.")]
        [Range(0.5f, 2.5f)] public float coneCurve = 1.35f;

        [Tooltip("Radius of the outside of the summit rim, in metres.")]
        [Range(6f, 90f)] public float rimRadius = 26f;

        [Tooltip("Width of the flat rim crest, in metres. The crater opens inside it, so this is " +
                 "the ledge that runs round the top and the ledge the spillways are notched into.")]
        [Range(0.5f, 30f)] public float rimWidth = 6f;

        [Tooltip("How far the crater floor sits below the rim, in metres.")]
        [Range(1f, 80f)] public float craterDepth = 13f;

        [Tooltip("How much of the crater is flat floor rather than sloping wall.")]
        [Range(0.05f, 0.95f)] public float craterFloorFraction = 0.5f;

        [Tooltip("Large-scale wobble in the height of the cone around its axis, as a fraction of " +
                 "the height. A perfectly circular volcano reads as a traffic cone.")]
        [Range(0f, 0.3f)] public float coneVariation = 0.07f;

        [Tooltip("How many lumps that wobble is broken into, going round.")]
        [Range(2, 12)] public int coneVariationLobes = 4;

        // ---------------------------------------------------------------- flank detail

        [Header("Flank detail")]
        [Tooltip("Depth of the gullies raked down the flanks, in metres. These are what break the " +
                 "cone up into faces and they double as beds for anything that runs down it.")]
        [Range(0f, 14f)] public float gullyDepth = 3.5f;

        [Tooltip("How many gullies run round the cone.")]
        [Range(3, 40)] public int gullyCount = 11;

        [Tooltip("How pinched the gullies are. Low spreads them into broad waves, high cuts them " +
                 "into narrow channels with untouched ground in between.")]
        [Range(0.5f, 6f)] public float gullySharpness = 2.4f;

        [Tooltip("Height of the general roughness on the flanks, in metres.")]
        [Range(0f, 8f)] public float roughness = 1.6f;

        [Tooltip("Size of that roughness, in metres. Keep it well above the facet size or it only " +
                 "shows up as noise on the normals.")]
        [Range(4f, 120f)] public float roughnessScale = 26f;

        [Tooltip("How ragged the rim crest is, in metres.")]
        [Range(0f, 12f)] public float rimRoughness = 2.5f;

        // ---------------------------------------------------------------- foot

        [Header("Foot")]
        [Tooltip("Width of the buried skirt past the foot of the cone, in metres.")]
        [Range(0f, 40f)] public float skirtWidth = 12f;

        [Tooltip("How far the outer edge of that skirt is sunk below the base plane, in metres. " +
                 "Keep it above the height of the bumps in the ground you are standing this on, or " +
                 "the mountain will show daylight under its foot.")]
        [Range(0f, 20f)] public float skirtSink = 4f;

        // ---------------------------------------------------------------- lava

        [Header("Lava")]
        [Tooltip("How far the surface of the lava standing in the crater sits below the rim, in " +
                 "metres. Nothing here draws the lava itself; this is the level a Lava Pond wants " +
                 "to be placed at, and it decides which spillways actually overflow.")]
        [Range(0f, 60f)] public float lavaDepthBelowRim = 5f;

        [Tooltip("Glowing fissures cut into the upper cone. Cheap detail, and the thing that stops " +
                 "the mountain reading as a grey lump at night.")]
        [Range(0, 40)] public int fissureCount = 9;

        [Tooltip("How far the fissures run down from the rim, in metres.")]
        [Range(2f, 90f)] public float fissureLength = 22f;

        [Tooltip("Width of a fissure, in metres.")]
        [Range(0.2f, 6f)] public float fissureWidth = 1.1f;

        [Tooltip("How far a fissure is laid above the rock, in metres.\n\n" +
                 "It has to clear the facets rather than the height field: a fissure follows the " +
                 "true curve while the mesh cuts the corner off it with a flat face, and on a cone " +
                 "this shape the face sits above the curve. Too little and fissures disappear into " +
                 "the hillside in patches.")]
        [Range(0.02f, 2f)] public float fissureLift = 0.3f;

        // ---------------------------------------------------------------- spillways

        [Header("Spillways")]
        [Tooltip("Notches cut through the rim for lava to pour out of, each one carrying a channel " +
                 "down the flank. Set a Lava Flow generator running from each notch and it has a " +
                 "real bed to follow rather than having to be talked into staying put.")]
        [Range(0, 6)] public int spillwayCount = 2;

        [Tooltip("Where the first notch sits, in degrees around the cone.")]
        [Range(0f, 360f)] public float spillwayAngle = 35f;

        [Tooltip("How unevenly the notches are spread. 0 spaces them exactly.")]
        [Range(0f, 1f)] public float spillwayScatter = 0.45f;

        [Tooltip("How far the notch cuts below the rim crest, in metres. This has to be more than " +
                 "the lava depth below the rim or nothing will ever come out of it.")]
        [Range(0.5f, 40f)] public float notchDrop = 7f;

        [Tooltip("Width of the notch where it cuts the rim, in metres.")]
        [Range(2f, 60f)] public float spillwayWidth = 14f;

        [Tooltip("How much wider the channel gets by the time it reaches the foot, as a multiple " +
                 "of its width at the rim. Lava fans out as the ground flattens.")]
        [Range(0f, 4f)] public float spillwayWiden = 1.2f;

        [Tooltip("Depth of the channel once it is clear of the rim, in metres.")]
        [Range(0f, 12f)] public float spillwayChannelDepth = 3.5f;

        // ---------------------------------------------------------------- passage

        [Header("Passage")]
        [Tooltip("What is cut through the base of the cone.\n\n" +
                 "Portals Only cuts the two mouths and leaves the tunnel to the Cave Tunnel " +
                 "generator.\n\n" +
                 "Bore cuts the mouths and builds the tunnel too, with a flat floor at the base " +
                 "plane that a kart can drive straight into.")]
        public PassageMode passage = PassageMode.Bore;

        [Tooltip("Heading of the passage, in degrees around the cone. The two mouths come out at " +
                 "this bearing and its opposite.")]
        [Range(0f, 360f)] public float boreYaw = 118f;

        [Tooltip("How far the passage is pushed off the axis of the cone, in metres. 0 runs it " +
                 "straight under the crater; offsetting it makes for a shorter tunnel closer to " +
                 "one flank.")]
        [Range(-120f, 120f)] public float boreOffset = 0f;

        [Tooltip("Width of the passage floor, in metres. A 16 m track wants noticeably more than " +
                 "16 m here so there is somewhere to go wrong.")]
        [Range(4f, 60f)] public float boreWidth = 22f;

        [Tooltip("Height of the vertical part of the walls before the arch starts, in metres.")]
        [Range(0.5f, 30f)] public float boreWallHeight = 6f;

        [Tooltip("Height of the crown of the arch above the floor, in metres.")]
        [Range(2f, 45f)] public float boreHeight = 13f;

        [Tooltip("Facets in the arch. Low is the point; this is a cave hacked out of rock, not a " +
                 "railway tunnel.")]
        [Range(2, 20)] public int boreArchSegments = 5;

        [Tooltip("Height of the passage floor above the base plane, in metres. 0 puts it level " +
                 "with the ground the volcano is standing on, which is what a track wants.")]
        [Range(-20f, 40f)] public float boreFloorHeight = 0f;

        [Tooltip("Distance between cross-sections down the tunnel, in metres.")]
        [Range(1f, 12f)] public float boreStationSpacing = 3.5f;

        [Tooltip("How far the floor runs on past each mouth, in metres, and how far it has sunk by " +
                 "the end of that run. The run out is what stops a kart hitting a step where the " +
                 "tunnel floor meets the ground: the floor dives under the ground rather than " +
                 "ending on a lip.")]
        [Range(0f, 40f)] public float boreApronLength = 10f;

        [Range(0f, 8f)] public float boreApronDrop = 1.5f;

        [Tooltip("How ragged the tunnel walls are, in metres. The floor is never roughened: the " +
                 "generated mesh is the collider and this is a road.")]
        [Range(0f, 4f)] public float boreWallRoughness = 0.55f;

        [Tooltip("A band of molten rock glowing along the bottom of the tunnel walls, so the " +
                 "passage lights itself rather than being a black hole in the middle of the map. " +
                 "It is part of the wall, so nothing drives over it.")]
        public bool boreLavaSeam = true;

        [Range(0.1f, 4f)] public float boreSeamHeight = 0.8f;

        [Tooltip("How far the hole cut in the mountain is inset inside the tunnel it is cut for, " +
                 "in metres. The two surfaces are solved separately and meet at the mouth, so a " +
                 "small overlap here is what guarantees no hairline crack of daylight around the " +
                 "arch. Raise it if you can see one; a big value shows as a visible rock lip.")]
        [Range(0f, 2f)] public float mouthOverlap = 0.3f;

        // ---------------------------------------------------------------- mesh density

        [Header("Mesh density")]
        [Tooltip("Faces around the cone. This is the main poly control and the main look control: " +
                 "the fewer there are, the wider each facet reads.")]
        [Range(12, 160)] public int angularSegments = 56;

        [Tooltip("Rings down the flank, from the rim to the foot.")]
        [Range(4, 80)] public int radialRings = 26;

        [Tooltip("Rings inside the crater.")]
        [Range(2, 30)] public int craterRings = 7;

        [Tooltip("How much the rings bunch up towards the summit, where the profile bends most. " +
                 "1 spaces them evenly.")]
        [Range(0.4f, 3f)] public float ringBias = 1.35f;

        // ---------------------------------------------------------------- props

        [Header("Rock detail")]
        [Tooltip("Crags jutting out of the flanks.")]
        [Range(0, 120)] public int cragCount = 30;

        [Range(1f, 20f)] public float cragSize = 5.5f;

        [Tooltip("Boulders sitting on the cone and scattered round its foot.")]
        [Range(0, 200)] public int boulderCount = 46;

        [Range(0.4f, 12f)] public float boulderSize = 3.2f;

        [Tooltip("Spires standing up off the rim crest, so the summit has a silhouette.")]
        [Range(0, 60)] public int rimSpireCount = 12;

        [Range(0.5f, 20f)] public float rimSpireHeight = 5f;

        // ---------------------------------------------------------------- shading

        [Header("Shading bands")]
        [Tooltip("Fraction of the height above which the flank is ash rather than basalt.")]
        [Range(0f, 1f)] public float ashHeightFraction = 0.5f;

        [Tooltip("Fraction of the height above which the rock is scorched and glowing at the edges.")]
        [Range(0f, 1f)] public float emberHeightFraction = 0.86f;

        // ---------------------------------------------------------------- output

        [Header("Output")]
        [Tooltip("How the UVs are laid out.\n\n" +
                 "World Planar tiles a texture every uvScale metres and is what ordinary rock " +
                 "materials want.\n\n" +
                 "Normalised stretches a single 0-1 tile over the whole mountain, for shaders that " +
                 "mask or remap on UV rather than tiling.")]
        public VolcanoUVMode uvMode = VolcanoUVMode.WorldPlanar;

        [Tooltip("World units per UV tile. World Planar mode only.")]
        [Range(0.2f, 40f)] public float uvScale = 8f;

        public VolcanoSettings Clone()
        {
            return (VolcanoSettings)MemberwiseClone();
        }

        /// <summary>Radius the crater opens at, i.e. the inside edge of the rim crest.</summary>
        public float CraterLipRadius
        {
            get { return Mathf.Max(1f, rimRadius - rimWidth); }
        }

        /// <summary>Height of the lava standing in the crater, above the base plane.</summary>
        public float LavaLevel
        {
            get { return height - lavaDepthBelowRim; }
        }

        /// <summary>Height of the floor of a spillway notch where it cuts the rim.</summary>
        public float NotchLevel
        {
            get { return height - notchDrop; }
        }
    }
}
