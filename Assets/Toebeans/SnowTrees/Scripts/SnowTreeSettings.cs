using System;
using UnityEngine;

namespace Toebeans.SnowTrees
{
    /// <summary>Silhouette family a snow tree is grown into.</summary>
    public enum SnowTreeShape
    {
        /// <summary>Full, wide spruce - short and broad at the skirt.</summary>
        Wide = 0,
        /// <summary>Narrow steeple - tapers evenly to a slender crown.</summary>
        Steeple = 1,
        /// <summary>Slim spire - almost columnar, very tall for its width.</summary>
        Slim = 2,
    }

    /// <summary>The three trees authored for Toebeans 3.</summary>
    public enum SnowTreeVariant
    {
        SnowSpruceA = 0,
        SnowSpruceB = 1,
        SnowSpruceC = 2,
    }

    public static class SnowTreeVariantExtensions
    {
        /// <summary>File-friendly name, matching the prefabs in Prefabs/.</summary>
        public static string AssetName(this SnowTreeVariant variant)
        {
            switch (variant)
            {
                case SnowTreeVariant.SnowSpruceB: return "SnowSpruce_B";
                case SnowTreeVariant.SnowSpruceC: return "SnowSpruce_C";
                default: return "SnowSpruce_A";
            }
        }
    }

    /// <summary>
    /// Every knob the generator reads. Values are in metres; a tree is built
    /// with its base at the local origin and grows up +Y.
    /// </summary>
    [Serializable]
    public struct SnowTreeSettings
    {
        [Tooltip("Any change to the seed reshuffles bough placement and snow lumps.")]
        public int seed;

        [Tooltip("Trunk height from the ground to the base of the crown spire.")]
        public float height;

        [Tooltip("Radius of the widest tier - drives the whole silhouette.")]
        public float radius;

        [Tooltip("How many rings of boughs are stacked up the trunk.")]
        public int tiers;

        public SnowTreeShape shape;

        [Tooltip("Boughs on the lowest tier; upper tiers thin out from here.")]
        public int boughsPerTier;

        [Tooltip("Exposed roots splaying out of the ground.")]
        public int rootCount;

        [Range(0f, 0.5f)]
        [Tooltip("Height fraction where the lowest tier sits - the bare trunk below it.")]
        public float lowestTier;

        [Tooltip("Multiplier on every snow lump. 0.6 = a light dusting, 1.4 = buried.")]
        public float snowScale;

        [Range(0f, 1f)]
        [Tooltip("Chance that any given bough carries a snow drift.")]
        public float snowCoverage;

        [Range(0.03f, 0.14f)]
        [Tooltip("Snow detail: voxel size as a fraction of radius. Smaller is " +
                 "rounder and far heavier; 0.055 is the authored look.")]
        public float snowCellScale;

        public static SnowTreeSettings ForVariant(SnowTreeVariant variant)
        {
            switch (variant)
            {
                case SnowTreeVariant.SnowSpruceB:
                    return new SnowTreeSettings
                    {
                        seed = 90210,
                        height = 7.6f,
                        radius = 1.45f,
                        tiers = 13,
                        shape = SnowTreeShape.Steeple,
                        boughsPerTier = 7,
                        rootCount = 5,
                        lowestTier = 0.14f,
                        snowScale = 1.05f,
                        snowCoverage = 1f,
                        snowCellScale = 0.055f,
                    };
                case SnowTreeVariant.SnowSpruceC:
                    return new SnowTreeSettings
                    {
                        seed = 4242,
                        height = 8.6f,
                        radius = 1.15f,
                        tiers = 16,
                        shape = SnowTreeShape.Slim,
                        boughsPerTier = 6,
                        rootCount = 7,
                        lowestTier = 0.13f,
                        snowScale = 1.2f,
                        snowCoverage = 1f,
                        snowCellScale = 0.055f,
                    };
                default:
                    return new SnowTreeSettings
                    {
                        seed = 1337,
                        height = 6f,
                        radius = 1.7f,
                        tiers = 10,
                        shape = SnowTreeShape.Wide,
                        boughsPerTier = 8,
                        rootCount = 6,
                        lowestTier = 0.16f,
                        snowScale = 1f,
                        snowCoverage = 1f,
                        snowCellScale = 0.055f,
                    };
            }
        }

        /// <summary>Clamps the values a user can drag into nonsense.</summary>
        public SnowTreeSettings Sanitised()
        {
            var s = this;
            s.height = Mathf.Max(0.5f, s.height);
            s.radius = Mathf.Max(0.1f, s.radius);
            s.tiers = Mathf.Clamp(s.tiers, 1, 40);
            s.boughsPerTier = Mathf.Clamp(s.boughsPerTier, 3, 16);
            s.rootCount = Mathf.Clamp(s.rootCount, 0, 12);
            s.lowestTier = Mathf.Clamp(s.lowestTier, 0f, 0.5f);
            s.snowScale = Mathf.Clamp(s.snowScale, 0f, 3f);
            s.snowCoverage = Mathf.Clamp01(s.snowCoverage);
            // A zero here would mean an infinite grid; clamp before it bites.
            s.snowCellScale = Mathf.Clamp(s.snowCellScale <= 0f ? 0.055f : s.snowCellScale, 0.03f, 0.2f);
            return s;
        }
    }
}
