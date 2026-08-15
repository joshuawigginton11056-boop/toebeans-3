using UnityEngine;

namespace CaveTunnel
{
    /// <summary>How the generator lays UVs around the bore.</summary>
    public enum CaveUvMode
    {
        /// <summary>
        /// U follows real distance around each ring, so a tile is the same physical size everywhere.
        /// The cost is that every ring's U range depends on its own circumference, so wherever the
        /// passage changes width the mapping shears between neighbouring rings — which is what makes
        /// a cavern look smeared. The seam does not meet either: a circumference is never a whole
        /// number of tiles.
        /// </summary>
        ArcLength = 0,

        /// <summary>
        /// U is a fixed whole number of tiles around every ring regardless of its size. Neighbouring
        /// rings always agree, so shaping never shears, and the seam meets exactly. The trade is
        /// that the texture stretches in proportion as the passage widens.
        /// </summary>
        Proportional = 1
    }

    /// <summary>
    /// Everything that shapes the cave but is not per-node. Kept as a plain serializable class so
    /// the same settings can be authored in the inspector or handed to
    /// <see cref="CaveMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class CaveSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for different rock, same layout.")]
        public int seed = 20260808;

        [Header("Resolution")]
        [Tooltip("Sides around the bore. Low numbers are what give the faceted low-poly look; " +
                 "12-16 matches the POLY_Mountain style.")]
        [Range(6, 48)] public int radialSegments = 14;

        [Tooltip("Distance between cross-sections along the path, in metres, on the straights.")]
        [Range(0.25f, 10f)] public float ringSpacing = 2f;

        [Tooltip("Extra cross-sections through bends: the most the path may turn between two of " +
                 "them. Lower is smoother through corners and costs triangles only where the cave " +
                 "actually curves.")]
        [Range(1f, 45f)] public float degreesPerRing = 6f;

        [Tooltip("How the curve is spaced through the nodes. 0.5 is centripetal and can never loop " +
                 "back through itself, whatever the node spacing — leave it there unless you know " +
                 "you want otherwise. 0 is the uniform curve: rounder through evenly spaced nodes, " +
                 "but it overshoots and kinks when spacing is uneven. 1 is chordal.")]
        [Range(0f, 1f)] public float curveAlpha = 0.5f;

        [Header("Editing")]
        [Tooltip("Smallest node spacing an insert may leave behind, as a multiple of the local " +
                 "half-width. A corner needs about 1.4 half-widths of spacing to survive a 90 " +
                 "degree turn and 2 to survive a hairpin, so 0.5 leaves room to work while still " +
                 "refusing the pile-ups that force a turn too tight to build — which is what " +
                 "clicking + repeatedly to smooth a corner does. 0 turns the guard off.")]
        [Range(0f, 2f)] public float minNodeSpacing = 0.5f;

        [Header("Rock")]
        [Tooltip("Wall displacement as a fraction of the local half-width. 0 is a clean bore.")]
        [Range(0f, 1f)] public float roughness = 0.3f;

        [Tooltip("Size of the rock lumps, in metres. Small values give gravel, large give boulders.")]
        [Range(0.5f, 40f)] public float roughnessScale = 7f;

        [Tooltip("How much of the roughness reaches the floor. Kept low so the driving surface " +
                 "stays smooth while the walls and ceiling stay rugged.")]
        [Range(0f, 1f)] public float floorRoughness = 0.06f;

        [Tooltip("Per-face brightness scatter baked into vertex colours. This is what stops a " +
                 "single-material flat-shaded cave reading as a flat grey pipe.")]
        [Range(0f, 0.6f)] public float shadeVariation = 0.16f;

        [Header("Mouths")]
        [Tooltip("A lip of rock ringing each opening, in metres, so the mouth has visible " +
                 "thickness instead of a paper edge. 0 turns it off.")]
        [Range(0f, 20f)] public float mouthRim = 1.5f;

        [Tooltip("Distance over which roughness fades out towards each end, in metres. Gives you a " +
                 "clean circular opening to bury in a hillside or match to a portal.")]
        [Range(0f, 30f)] public float mouthSmoothing = 4f;

        [Header("Texture mapping")]
        [Tooltip("How UVs are laid out. Arc Length keeps a constant texture size everywhere but " +
                 "shears where the passage changes width. Proportional never shears but stretches " +
                 "the texture as the passage widens. If neither looks right, use a triplanar " +
                 "material instead and ignore this whole section.")]
        public CaveUvMode uvMode = CaveUvMode.Proportional;

        [Tooltip("Metres per texture tile along the length of the cave.")]
        [Range(0.5f, 20f)] public float uvScaleAlong = 4f;

        [Tooltip("Metres per texture tile around the bore. Arc Length mode only.")]
        [Range(0.5f, 20f)] public float uvScaleAround = 4f;

        [Tooltip("Whole tiles around the bore. Proportional mode only. Being a whole number is the " +
                 "point — it is what makes the texture meet itself at the seam.")]
        [Range(1, 24)] public int uvTilesAround = 4;

        [Tooltip("The walls face inwards, because you are meant to be inside them. Flip this if " +
                 "your scale is negative on one axis and the cave has turned inside out.")]
        public bool flipWinding;

        public CaveSettings Clone()
        {
            return (CaveSettings)MemberwiseClone();
        }
    }
}
