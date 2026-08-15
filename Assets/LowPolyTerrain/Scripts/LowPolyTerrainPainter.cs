using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Turns a shaped heightmap into terrain splat weights for the lava world: molten ground pooled
    /// in the low basins, ash across the flats, scorched ground on the rises, bare basalt up the
    /// crater wall.
    ///
    /// Note the molten rule runs <b>opposite to a snowline</b> - lava collects in the dips and on
    /// gentle ground, so it keys on being below a height rather than above one, and is cut off by
    /// slope so it cannot run up the wall.
    ///
    /// The rules are driven mostly by slope, which is deliberate. <see cref="LowPolyTerrainBuilder"/>
    /// leaves the height field planar over each facet, so slope is <b>constant across a facet</b> -
    /// a slope-driven rule therefore paints whole facets in one colour, and the texturing ends up as
    /// faceted as the geometry rather than fighting it. Height rules vary within a facet, but only
    /// linearly, so they read as a clean gradient.
    ///
    /// Pure maths in, weights out - no scene objects, so it runs headlessly like the builder.
    /// </summary>
    public static class LowPolyTerrainPainter
    {
        /// <summary>Layer order the shaper assigns, and the order of the returned weights.</summary>
        public const int LayerAsh = 0;
        public const int LayerScorched = 1;
        public const int LayerBasalt = 2;
        public const int LayerMolten = 3;
        public const int LayerCount = 4;

        /// <summary>
        /// Splat weights indexed [z, x, layer], normalised so each texel sums to 1.
        /// </summary>
        public static float[,,] Build(
            LowPolyTerrainSettings s,
            float[,] heights,
            int alphaRes,
            float sizeX, float sizeZ, float sizeY)
        {
            int hres = heights.GetLength(0);
            var map = new float[alphaRes, alphaRes, LayerCount];

            float hStepX = sizeX / (hres - 1);
            float hStepZ = sizeZ / (hres - 1);
            float noiseWl = Mathf.Max(1f, s.textureNoiseWavelength);

            for (int az = 0; az < alphaRes; az++)
            {
                float v = alphaRes > 1 ? (float)az / (alphaRes - 1) : 0f;
                float worldZ = v * sizeZ;
                int hz = Mathf.Clamp(Mathf.RoundToInt(v * (hres - 1)), 1, hres - 2);

                for (int ax = 0; ax < alphaRes; ax++)
                {
                    float u = alphaRes > 1 ? (float)ax / (alphaRes - 1) : 0f;
                    float worldX = u * sizeX;
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hres - 1)), 1, hres - 2);

                    float height = heights[hz, hx] * sizeY;

                    // Central differences on the heightmap grid, so the slope inherits the field's
                    // per-facet constancy instead of being smoothed across facet boundaries.
                    float dhx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * sizeY / (2f * hStepX);
                    float dhz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * sizeY / (2f * hStepZ);
                    float slope = Mathf.Atan(Mathf.Sqrt(dhx * dhx + dhz * dhz)) * Mathf.Rad2Deg;

                    // One noise field wobbles every threshold, so the bands stop being contour lines.
                    float n = 0f;
                    if (s.textureNoise > 0f)
                    {
                        n = (TerrainNoise.Fbm(worldX / noiseWl, worldZ / noiseWl, s.seed + 6151, 3) * 2f - 1f)
                            * s.textureNoise;
                    }

                    float slopeWobble = n * 8f;      // degrees
                    float heightWobble = n * 9f;     // metres

                    float scorched = SmoothStep(
                        s.scorchedSlopeStart + slopeWobble, s.scorchedSlopeFull + slopeWobble, slope);

                    float basalt = SmoothStep(
                        s.basaltSlopeStart + slopeWobble, s.basaltSlopeFull + slopeWobble, slope);

                    // Inverted against a snowline: full at the bottom of the basin, gone as the
                    // floor climbs past moltenHeight.
                    float molten = 1f - SmoothStep(
                        s.moltenHeight - s.moltenBand + heightWobble,
                        s.moltenHeight + heightWobble,
                        height);

                    // Lava lies flat. Anything with a slope on it drains, so it never climbs the wall.
                    molten *= 1f - SmoothStep(s.moltenMaxSlope - 6f, s.moltenMaxSlope + 6f, slope);

                    // Each layer only takes what the ones above it left, so the stack stays ordered
                    // molten -> basalt -> scorched -> ash without any weight going negative.
                    float remaining = 1f;

                    float wMolten = Mathf.Clamp01(molten) * remaining;
                    remaining -= wMolten;

                    float wBasalt = Mathf.Clamp01(basalt) * remaining;
                    remaining -= wBasalt;

                    float wScorched = Mathf.Clamp01(scorched) * remaining;
                    remaining -= wScorched;

                    float wAsh = remaining;

                    float total = wAsh + wScorched + wBasalt + wMolten;
                    if (total <= 1e-6f) { wAsh = 1f; total = 1f; }

                    map[az, ax, LayerAsh] = wAsh / total;
                    map[az, ax, LayerScorched] = wScorched / total;
                    map[az, ax, LayerBasalt] = wBasalt / total;
                    map[az, ax, LayerMolten] = wMolten / total;
                }
            }

            return map;
        }

        static float SmoothStep(float edge0, float edge1, float v)
        {
            if (edge1 - edge0 < 1e-6f) return v < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
