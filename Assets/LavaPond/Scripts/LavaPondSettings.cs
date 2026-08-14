using System.Collections.Generic;
using UnityEngine;

namespace LavaPond
{
    /// <summary>
    /// A place where a river pours in. Written by the Lava Flow that feeds the pool rather than
    /// authored by hand, and it changes three things about the shore it lands on: the rock rim is
    /// notched down instead of damming the river, the shore lip stops walling the mouth off, and
    /// the crust is swept away in a fan in front of it, because lava arriving there has had no
    /// time to skin over.
    ///
    /// It never moves the pond's outline or its footprint, and it never touches a single random
    /// number: a pond with no inlets builds exactly the mesh it always did, and adding one leaves
    /// every plate, boulder and bubble outside the mouth where it was.
    ///
    /// Angles are in the pond's own local space, measured the way the shoreline measures them:
    /// <c>Atan2(z, x)</c>, so 0 is local +X. The generator converts before writing any of this.
    /// </summary>
    [System.Serializable]
    public struct PondInlet
    {
        /// <summary>Identifies the flow that owns this inlet, so it updates its own entry and no
        /// other. Zero for one placed by hand.</summary>
        public int owner;

        /// <summary>Where on the shore the river arrives, in degrees.</summary>
        public float angleDeg;

        /// <summary>Half the width of the river's mouth, in the pond's local units.</summary>
        public float halfWidth;

        /// <summary>How far the arriving lava keeps the crust open, in the pond's local units.</summary>
        public float reach;

        public bool Matches(PondInlet other)
        {
            return owner == other.owner
                   && Mathf.Abs(Mathf.DeltaAngle(angleDeg, other.angleDeg)) < 0.05f
                   && Mathf.Abs(halfWidth - other.halfWidth) < 0.01f
                   && Mathf.Abs(reach - other.reach) < 0.01f;
        }
    }

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

        [Header("Inlets")]
        [Tooltip("Where rivers pour in. A Lava Flow with this pond set as the pool it runs into " +
                 "keeps its own entry here and updates it whenever the route moves, so there is " +
                 "normally nothing to edit by hand.")]
        public List<PondInlet> inlets = new List<PondInlet>();

        [Tooltip("How far past the mouth the crust stays broken up, as a multiple of the river's " +
                 "width. Lava arriving from a river is the hottest thing in the pool and takes a " +
                 "while to skin over, so a fan of open lava reaches out from the mouth.")]
        [Range(0f, 6f)] public float inletMeltReach = 2.2f;

        [Tooltip("How deep the rim is notched where a river runs in, as a fraction of its height. " +
                 "1 cuts it to the level of the lava. The outer edge of the rim never moves, so " +
                 "the pond still sits flush on the ground around it.")]
        [Range(0f, 1f)] public float inletRimCut = 1f;

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
            var copy = (LavaPondSettings)MemberwiseClone();
            copy.inlets = inlets != null ? new List<PondInlet>(inlets) : new List<PondInlet>();
            return copy;
        }
    }
}
