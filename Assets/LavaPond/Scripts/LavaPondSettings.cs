using UnityEngine;

namespace LavaPond
{
    /// <summary>How the generated UVs are laid out.</summary>
    public enum PondUVMode
    {
        /// <summary>Projected from world position, tiling every uvScale metres.</summary>
        WorldPlanar = 0,

        /// <summary>One 0-1 tile stretched across the whole pond.</summary>
        Normalized = 1
    }

    /// <summary>
    /// Every knob that shapes the lava pond mesh. Kept as a plain serializable class so the same
    /// settings can be authored in the inspector, stored in a ScriptableObject, or passed to
    /// <see cref="LavaPondMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class LavaPondSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for a completely different pond with the same silhouette budget.")]
        public int seed = 20260802;

        [Header("Shore")]
        [Tooltip("Average radius of the pond, in metres.")]
        [Range(2f, 60f)] public float radius = 12f;

        [Tooltip("How far the shoreline wanders from a perfect circle. 0 = round pool.")]
        [Range(0f, 0.45f)] public float shoreIrregularity = 0.2f;

        [Tooltip("Vertices around the shoreline. Drives the whole poly budget.")]
        [Range(12, 96)] public int angularSegments = 48;

        [Tooltip("Concentric rings across the pond. More rings = finer facets and finer cracks.")]
        [Range(2, 20)] public int radialRings = 10;

        [Header("Crust")]
        [Tooltip("Number of cooled plates the surface is broken into. Each plate gets its own " +
                 "height and shade, and the gaps between them are where the lava shows through.")]
        [Range(1, 160)] public int plateCount = 26;

        [Tooltip("How much of the pond's surface has crusted over. The gaps between the plates are " +
                 "where the lava shows through.\n\n" +
                 "1 is skinned over completely, 0 is bare lava with no crust on it anywhere. Same " +
                 "control the rivers have, and it reads the same way round.")]
        [Range(0f, 1f)] public float crustCoverage = 0.78f;

        [Tooltip("How much sooner the shore crusts than the middle, and how far in the bias reaches " +
                 "as a fraction of the radius. The edge of a pond is its coolest part, so a little " +
                 "of this stops the open lava reading as a disc dropped in the centre.\n\n" +
                 "It only ever biases. Crust Coverage still decides how much crust there is: this " +
                 "cannot put a ring on a pond set to have none, or leave a hole in one set solid.")]
        [Range(0f, 0.6f)] public float shoreCrustBand = 0.18f;

        [Tooltip("Fraction of plates still glowing rather than fully cooled. Biased toward the " +
                 "plates sitting next to open lava.")]
        [Range(0f, 1f)] public float warmCrustRatio = 0.35f;

        [Tooltip("How far the plates pull back from each other, in metres. This is the width of " +
                 "the glowing cracks and the single most visible knob here.")]
        [Range(0f, 1.5f)] public float crackWidth = 0.22f;

        [Tooltip("How far the molten surface sits below the crust. Reads as the thickness of the " +
                 "plates when you look into a crack.")]
        [Range(0.02f, 2f)] public float crustThickness = 0.3f;

        [Tooltip("Vertical offset between neighbouring plates, so the crust is not a flat lid.")]
        [Range(0f, 0.5f)] public float plateHeightVariation = 0.1f;

        [Tooltip("Sideways wobble applied to interior vertices so facets are not a clean radial fan.")]
        [Range(0f, 1f)] public float crustJitter = 0.5f;

        [Header("Molten")]
        [Tooltip("How much the lava surface under the crust rolls and bulges.")]
        [Range(0f, 1f)] public float moltenTurbulence = 0.55f;

        [Header("Rim")]
        [Tooltip("Width of the scorched rock bank ringing the pond.")]
        [Range(0f, 20f)] public float rimWidth = 2.8f;

        [Tooltip("Peak height of the rim above the crust.")]
        [Range(0f, 8f)] public float rimHeight = 0.9f;

        [Tooltip("Concentric rings across the rim.")]
        [Range(1, 8)] public int rimRings = 3;

        [Tooltip("Random height noise on the rim.")]
        [Range(0f, 1f)] public float rimRoughness = 0.6f;

        [Header("Body")]
        [Tooltip("How far the solid block extends below the crust, so the asset is not a paper sheet.")]
        [Range(0f, 10f)] public float depth = 1.6f;

        [Header("Vent")]
        [Tooltip("Raise a spatter cone with a molten mouth somewhere on the pond. Turn this off " +
                 "for a plain pool, on for the version that is obviously feeding the flow.")]
        public bool vent = false;

        [Tooltip("Radius of the cone's mouth, in metres.")]
        [Range(0.3f, 12f)] public float ventRadius = 2.2f;

        [Tooltip("How far the cone stands above the crust.")]
        [Range(0.2f, 12f)] public float ventHeight = 1.6f;

        [Tooltip("Where the vent sits, as a fraction of the pond radius from the centre. Kept well " +
                 "inside the shore so the cone's base never runs into the rim.")]
        [Range(-0.5f, 0.5f)] public float ventOffsetX = 0f;

        [Range(-0.5f, 0.5f)] public float ventOffsetZ = 0f;

        [Tooltip("How ragged the mouth is. 0 gives a neat circle, which never looks built by spatter.")]
        [Range(0f, 0.7f)] public float ventIrregularity = 0.3f;

        [Header("Detail: crust slabs")]
        [Tooltip("Broken slabs of crust tipped up out of the surface.")]
        [Range(0, 40)] public int slabCount = 12;

        [Range(0.3f, 4f)] public float slabSize = 1.4f;
        [Range(0f, 3f)] public float slabHeight = 0.85f;

        [Header("Detail: lava bubbles")]
        [Tooltip("Domes swelling up out of the open lava. Only ever placed in molten pools.")]
        [Range(0, 60)] public int bubbleCount = 14;

        [Range(0.1f, 4f)] public float bubbleSize = 0.9f;

        [Header("Detail: rocks")]
        [Tooltip("Boulders on the rim and stranded out on the crust.")]
        [Range(0, 60)] public int rockCount = 16;

        [Range(0.1f, 4f)] public float rockSize = 0.95f;

        [Tooltip("Fraction of rocks stranded on the crust rather than sitting on the rim.")]
        [Range(0f, 1f)] public float rockOnCrustRatio = 0.25f;

        [Header("Output")]
        [Tooltip("How the UVs are laid out.\n\n" +
                 "World Planar tiles a texture every uvScale metres and is what ordinary rock and " +
                 "lava textures want.\n\n" +
                 "Normalised stretches a single 0-1 tile across the whole pond. Shaders that mask " +
                 "or remap on UV rather than tiling need this: an unclamped Remap node fed a UV of " +
                 "3 instead of 1 will drive the colour straight past white.")]
        public PondUVMode uvMode = PondUVMode.WorldPlanar;

        [Tooltip("World units per UV tile. World Planar mode only.")]
        [Range(0.1f, 20f)] public float uvScale = 4f;

        [Tooltip("Which way the lava travels, in degrees clockwise from world +Z: 0 is +Z, 90 is +X.\n\n" +
                 "A scrolling lava material runs its pattern along the V axis, and on a pond that axis " +
                 "is a world direction rather than anything the pond knows about. Point this along the " +
                 "river that feeds the pool and the two read as one flow carrying on into it.\n\n" +
                 "It only steers the molten surface. The crust and rock keep their own projection.")]
        [Range(0f, 360f)] public float flowAngle = 0f;

        public LavaPondSettings Clone()
        {
            return (LavaPondSettings)MemberwiseClone();
        }
    }
}
