using UnityEngine;

namespace FrozenLake
{
    /// <summary>
    /// Every knob that shapes the frozen lake mesh. Kept as a plain serializable class so the
    /// same settings can be authored in the inspector, stored in a ScriptableObject, or passed
    /// to <see cref="FrozenLakeMeshBuilder"/> from a test.
    /// </summary>
    [System.Serializable]
    public class FrozenLakeSettings
    {
        [Header("Seed")]
        [Tooltip("Change this for a completely different lake with the same silhouette budget.")]
        public int seed = 20260731;

        [Header("Shore")]
        [Tooltip("Average radius of the ice sheet, in metres.")]
        [Range(2f, 60f)] public float radius = 12f;

        [Tooltip("How far the shoreline wanders from a perfect circle. 0 = round pond.")]
        [Range(0f, 0.45f)] public float shoreIrregularity = 0.18f;

        [Tooltip("Vertices around the shoreline. Drives the whole poly budget.")]
        [Range(12, 96)] public int angularSegments = 44;

        [Tooltip("Concentric rings across the ice. More rings = finer facets.")]
        [Range(2, 20)] public int radialRings = 9;

        [Header("Ice")]
        [Tooltip("Number of ice plates the surface is broken into. Each plate gets its own height and shade.")]
        [Range(1, 120)] public int plateCount = 18;

        [Tooltip("Vertical offset between neighbouring plates. This is what reads as cracks.")]
        [Range(0f, 0.4f)] public float plateHeightVariation = 0.09f;

        [Tooltip("Fraction of plates that use the darker deep-ice material.")]
        [Range(0f, 1f)] public float deepIceRatio = 0.3f;

        [Tooltip("Fraction of plates near the shore that get dusted with snow.")]
        [Range(0f, 1f)] public float shoreSnowRatio = 0.45f;

        [Tooltip("Sideways wobble applied to interior ice vertices so facets are not a clean radial fan.")]
        [Range(0f, 1f)] public float iceJitter = 0.45f;

        [Header("Snow bank")]
        [Tooltip("Width of the snow berm ringing the lake.")]
        [Range(0f, 20f)] public float bankWidth = 2.6f;

        [Tooltip("Peak height of the snow berm above the ice.")]
        [Range(0f, 8f)] public float bankHeight = 0.85f;

        [Tooltip("Concentric rings across the snow berm.")]
        [Range(1, 8)] public int bankRings = 3;

        [Tooltip("Random height noise on the berm.")]
        [Range(0f, 1f)] public float bankRoughness = 0.6f;

        [Header("Body")]
        [Tooltip("How far the solid block extends below the ice, so the asset is not a paper sheet.")]
        [Range(0f, 10f)] public float depth = 1.4f;

        [Header("Detail: ice shards")]
        [Tooltip("Broken slabs of ice heaved up out of the sheet.")]
        [Range(0, 40)] public int shardCount = 12;

        [Range(0.3f, 4f)] public float shardSize = 1.45f;
        [Range(0f, 3f)] public float shardHeight = 0.9f;

        [Header("Detail: snow patches")]
        [Tooltip("Flat drifts of snow lying on top of the ice.")]
        [Range(0, 40)] public int snowPatchCount = 10;

        [Range(0.3f, 6f)] public float snowPatchSize = 1.6f;

        [Header("Detail: rocks")]
        [Tooltip("Boulders on the berm and frozen into the ice.")]
        [Range(0, 60)] public int rockCount = 14;

        [Range(0.1f, 4f)] public float rockSize = 0.95f;

        [Tooltip("Fraction of rocks frozen into the ice rather than sitting on the bank.")]
        [Range(0f, 1f)] public float rockOnIceRatio = 0.25f;

        [Header("Output")]
        [Tooltip("World units per UV tile for the generated planar UVs.")]
        [Range(0.1f, 20f)] public float uvScale = 4f;

        public FrozenLakeSettings Clone()
        {
            return (FrozenLakeSettings)MemberwiseClone();
        }
    }
}
