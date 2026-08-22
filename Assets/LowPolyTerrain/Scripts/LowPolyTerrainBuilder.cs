using System.Collections.Generic;
using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Turns <see cref="LowPolyTerrainSettings"/> into a Unity heightmap.
    ///
    /// The whole design rests on one invariant: <b>all shaping happens on a coarse lattice, and the
    /// fine heightmap is only ever a planar evaluation of that lattice.</b> Planarity survives
    /// refinement - if every fine sample inside a lattice triangle lies on that triangle's plane,
    /// then every fine triangle Unity builds from those samples is coplanar too, whichever way it
    /// happens to split its quads. Unity derives terrain normals by finite difference, so a coplanar
    /// patch yields one constant normal and shades as a single flat face.
    ///
    /// That is why this reads as low poly at 513x513 while a low-resolution heightmap does not: it
    /// is the flatness that makes facets, not the vertex count. Nothing here reduces the triangle
    /// count Unity draws - terrain LOD still owns that.
    ///
    /// No scene objects, no asset loading, no Unity native calls, so the whole thing runs headlessly.
    /// </summary>
    public static class LowPolyTerrainBuilder
    {
        /// <summary>What a build produced, alongside the numbers the inspector reports.</summary>
        public struct Result
        {
            /// <summary>Normalised heights indexed [z, x], ready for TerrainData.SetHeights.</summary>
            public float[,] Heights;

            public int FacetCellsX;
            public int FacetCellsZ;
            public float ActualFacetSizeX;
            public float ActualFacetSizeZ;

            public float MinHeight;
            public float MaxHeight;

            /// <summary>Steepest slope anywhere on the pan, in degrees. The driveability number.</summary>
            public float MaxPanSlopeDegrees;

            /// <summary>Steepest slope anywhere at all, in degrees. Expected to be near-vertical on the wall.</summary>
            public float MaxSlopeDegrees;

            /// <summary>Side of the square of open ground left inside the wall foot, in metres.</summary>
            public float PlayableSpan;

            /// <summary>True if the shape wanted to go above the terrain's own height ceiling.</summary>
            public bool ClampedAtCeiling;

            public int TriangleCount { get { return FacetCellsX * FacetCellsZ * 2; } }
        }

        /// <summary>One lattice corner: where it sits (warped) and how high it is.</summary>
        struct Corner
        {
            public float X;
            public float Z;
            public float Y;
        }

        public static Result Build(
            LowPolyTerrainSettings s,
            int resolution,
            float sizeX,
            float sizeZ,
            float sizeY,
            IList<ProtectedArea> protectedAreas)
        {
            var result = new Result();

            int cellsX = Mathf.Max(1, Mathf.RoundToInt(sizeX / Mathf.Max(0.01f, s.facetSize)));
            int cellsZ = Mathf.Max(1, Mathf.RoundToInt(sizeZ / Mathf.Max(0.01f, s.facetSize)));
            float cellX = sizeX / cellsX;
            float cellZ = sizeZ / cellsZ;

            result.FacetCellsX = cellsX;
            result.FacetCellsZ = cellsZ;
            result.ActualFacetSizeX = cellX;
            result.ActualFacetSizeZ = cellZ;

            Corner[,] lattice = BuildLattice(s, cellsX, cellsZ, cellX, cellZ, sizeX, sizeZ, protectedAreas);

            // Which diagonal each quad splits along. Fixed per quad so the two triangles of a quad
            // always agree with each other and with their neighbours across a shared edge.
            bool[,] flipped = new bool[cellsX, cellsZ];
            for (int i = 0; i < cellsX; i++)
                for (int j = 0; j < cellsZ; j++)
                    flipped[i, j] = s.jitterDiagonals && TerrainNoise.Hash(i, j, s.seed + 5501) < 0.5f;

            float[,] heights = new float[resolution, resolution];
            float min = float.MaxValue, max = float.MinValue;
            bool clamped = false;

            float stepX = sizeX / (resolution - 1);
            float stepZ = sizeZ / (resolution - 1);

            for (int jz = 0; jz < resolution; jz++)
            {
                float z = jz * stepZ;
                for (int ix = 0; ix < resolution; ix++)
                {
                    float x = ix * stepX;
                    float y = SampleLattice(lattice, flipped, cellsX, cellsZ, cellX, cellZ, x, z);

                    if (y < min) min = y;
                    if (y > max) max = y;

                    float n = y / sizeY;
                    if (n > 1f) { n = 1f; clamped = true; }
                    else if (n < 0f) { n = 0f; clamped = true; }

                    heights[jz, ix] = n;
                }
            }

            result.Heights = heights;
            result.MinHeight = min;
            result.MaxHeight = max;
            result.ClampedAtCeiling = clamped;

            MeasureSlopes(heights, resolution, sizeX, sizeZ, sizeY, s, ref result);

            float foot = s.buildWall ? s.wallWidth + s.footWander : 0f;
            result.PlayableSpan = Mathf.Max(0f, Mathf.Min(sizeX, sizeZ) - 2f * foot);

            return result;
        }

        // ---------------------------------------------------------------- lattice

        static Corner[,] BuildLattice(
            LowPolyTerrainSettings s,
            int cellsX, int cellsZ, float cellX, float cellZ,
            float sizeX, float sizeZ,
            IList<ProtectedArea> protectedAreas)
        {
            var lattice = new Corner[cellsX + 1, cellsZ + 1];

            for (int i = 0; i <= cellsX; i++)
            {
                for (int j = 0; j <= cellsZ; j++)
                {
                    float x = i * cellX;
                    float z = j * cellZ;

                    // The outer ring stays pinned so the lattice covers the terrain exactly and no
                    // fine sample can fall outside every triangle.
                    bool onEdge = i == 0 || j == 0 || i == cellsX || j == cellsZ;
                    if (!onEdge && s.latticeJitter > 0f)
                    {
                        float jx = TerrainNoise.Hash(i, j, s.seed + 991) * 2f - 1f;
                        float jz = TerrainNoise.Hash(i, j, s.seed + 4409) * 2f - 1f;
                        x += jx * s.latticeJitter * cellX;
                        z += jz * s.latticeJitter * cellZ;
                    }

                    var c = new Corner();
                    c.X = x;
                    c.Z = z;
                    c.Y = HeightAt(s, x, z, sizeX, sizeZ, protectedAreas);
                    lattice[i, j] = c;
                }
            }

            return lattice;
        }

        /// <summary>The world shape, evaluated only ever at lattice corners.</summary>
        static float HeightAt(
            LowPolyTerrainSettings s,
            float x, float z,
            float sizeX, float sizeZ,
            IList<ProtectedArea> protectedAreas)
        {
            float h = PanHeight(s, x, z);

            h += HillHeight(s, x, z, sizeX, sizeZ);

            if (s.buildWall)
                h += WallHeight(s, x, z, sizeX, sizeZ);

            // Carved last, so it wins against whatever it lands on. Everything above only ever adds.
            h -= PondDepth(s, x, z, sizeX, sizeZ);

            if (protectedAreas != null)
            {
                for (int k = 0; k < protectedAreas.Count; k++)
                {
                    ProtectedArea a = protectedAreas[k];
                    float dx = x - a.centerX;
                    float dz = z - a.centerZ;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= a.radius + s.protectionBlend) continue;

                    float w = 1f - SmoothStep(a.radius, a.radius + s.protectionBlend, d);
                    h = Mathf.Lerp(h, a.height, w);
                }
            }

            return h;
        }

        static float PanHeight(LowPolyTerrainSettings s, float x, float z)
        {
            float wl = Mathf.Max(1f, s.panWavelength);
            float n = TerrainNoise.Fbm(x / wl, z / wl, s.seed, s.panOctaves) * 2f - 1f;

            // Raising the exponent pulls mid values toward the datum, which turns a uniformly wavy
            // field into broad plains with occasional rises - much easier to route a track across.
            if (s.panFlatten > 0f)
            {
                float k = 1f + s.panFlatten * 2f;
                n = Mathf.Sign(n) * Mathf.Pow(Mathf.Abs(n), k);
            }

            return s.panHeight + n * s.panRelief * 0.5f;
        }

        /// <summary>
        /// Hill country standing up out of the pan, held off the valley floor.
        ///
        /// The noise is thresholded rather than used raw. Raw fbm gives a surface that is wavy
        /// everywhere, which reads as one lumpy field however tall it is; cutting everything below
        /// a floor to zero and remapping what is left leaves genuine flat ground between genuine
        /// hills, which is what makes them read as landforms.
        /// </summary>
        static float HillHeight(LowPolyTerrainSettings s, float x, float z, float sizeX, float sizeZ)
        {
            if (s.hillHeight <= 0f) return 0f;

            float wl = Mathf.Max(1f, s.hillWavelength);
            float n = TerrainNoise.Fbm(x / wl + 53.1f, z / wl - 71.4f, s.seed + 5171, s.hillOctaves);

            // Coverage sets where the ground stops being flat. High coverage drops the floor, so
            // more of the noise survives and the hills join up.
            float floor = 1f - Mathf.Clamp01(s.hillCoverage);
            if (n <= floor) return 0f;

            float t = (n - floor) / Mathf.Max(1e-4f, 1f - floor);
            t = t * t * (3f - 2f * t);

            return s.hillHeight * t * ValleyMask(s, x, z, sizeX, sizeZ);
        }

        /// <summary>
        /// 0 on the valley floor, 1 clear of it. Multiplying the hills by this is what keeps the
        /// valley flat - the hills are never generated there rather than being flattened afterwards,
        /// so there is no seam where a hill was cut off.
        /// </summary>
        static float ValleyMask(LowPolyTerrainSettings s, float x, float z, float sizeX, float sizeZ)
        {
            if (s.valleyWidth <= 0f) return 1f;

            float ang = s.valleyBearingDegrees * Mathf.Deg2Rad;
            float dx = x - sizeX * 0.5f;
            float dz = z - sizeZ * 0.5f;

            // Distance to the valley axis, measured along its perpendicular.
            float d = dx * -Mathf.Sin(ang) + dz * Mathf.Cos(ang) - s.valleyOffset;

            if (s.valleyWander > 0f)
            {
                float ww = Mathf.Max(1f, s.valleyWidth * 1.6f);
                d += (TerrainNoise.Fbm(x / ww - 17.9f, z / ww + 63.2f, s.seed + 8419, 2) * 2f - 1f)
                     * s.valleyWander;
            }

            float half = s.valleyWidth * 0.5f;
            return SmoothStep(half, half + Mathf.Max(1f, s.valleyBlend), Mathf.Abs(d));
        }

        /// <summary>A flat-bottomed basin scooped out of whatever the ground was.</summary>
        static float PondDepth(LowPolyTerrainSettings s, float x, float z, float sizeX, float sizeZ)
        {
            if (s.pondRadius <= 0f || s.pondDepth <= 0f) return 0f;

            float cx = s.pondCenter.x * sizeX;
            float cz = s.pondCenter.y * sizeZ;
            float dx = x - cx, dz = z - cz;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= s.pondRadius) return 0f;

            float flat = s.pondRadius * Mathf.Clamp01(s.pondFloorFlat);
            return s.pondDepth * (1f - SmoothStep(flat, s.pondRadius, d));
        }

        static float WallHeight(LowPolyTerrainSettings s, float x, float z, float sizeX, float sizeZ)
        {
            // Distance to the nearest map border. Using the minimum of the four means the corners
            // are the deepest inside the wall band, so they build up into massifs on their own.
            float d = Mathf.Min(Mathf.Min(x, sizeX - x), Mathf.Min(z, sizeZ - z));

            if (s.footWander > 0f)
            {
                float fw = Mathf.Max(1f, s.crestWavelength * 0.6f);
                d += (TerrainNoise.Fbm(x / fw + 31.7f, z / fw - 12.4f, s.seed + 3301, 3) * 2f - 1f) * s.footWander;
            }

            float t = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, s.wallWidth));
            if (t <= 0f) return 0f;

            // Bias below 0.5 delays the climb, leaving a gentle apron inside the foot; above 0.5
            // the wall leaps up as soon as you cross the foot.
            float k = (1f - s.wallProfileBias) / Mathf.Max(0.01f, s.wallProfileBias);
            float p = SmoothStep(0f, 1f, Mathf.Pow(t, k));

            float cw = Mathf.Max(1f, s.crestWavelength);
            float crestNoise = TerrainNoise.Fbm(x / cw, z / cw, s.seed + 7717, 3) * 2f - 1f;
            float crest = s.wallHeight * (1f + s.crestVariation * crestNoise);

            float h = p * crest;

            if (s.wallRelief > 0f)
            {
                float rw = Mathf.Max(1f, s.wallReliefWavelength);
                float ridge = TerrainNoise.Ridge(x / rw, z / rw, s.seed + 2213, 3);
                // Fade the relief in above the foot so gullies never bite into the flat ground.
                h += ridge * s.wallRelief * SmoothStep(0f, 0.35f, t);
            }

            return h;
        }

        // ---------------------------------------------------------------- sampling

        /// <summary>
        /// Height of the warped lattice at an arbitrary point, by locating the triangle containing
        /// it and evaluating that triangle's plane. Because corners move by less than half a cell,
        /// the containing quad is always within one of the unwarped one, so a 3x3 search is exact.
        /// </summary>
        static float SampleLattice(
            Corner[,] lattice, bool[,] flipped,
            int cellsX, int cellsZ, float cellX, float cellZ,
            float x, float z)
        {
            int ci = Mathf.Clamp(Mathf.FloorToInt(x / cellX), 0, cellsX - 1);
            int cj = Mathf.Clamp(Mathf.FloorToInt(z / cellZ), 0, cellsZ - 1);

            float bestY = 0f;
            float bestScore = float.NegativeInfinity;

            for (int di = -1; di <= 1; di++)
            {
                int i = ci + di;
                if (i < 0 || i >= cellsX) continue;

                for (int dj = -1; dj <= 1; dj++)
                {
                    int j = cj + dj;
                    if (j < 0 || j >= cellsZ) continue;

                    Corner c00 = lattice[i, j];
                    Corner c10 = lattice[i + 1, j];
                    Corner c01 = lattice[i, j + 1];
                    Corner c11 = lattice[i + 1, j + 1];

                    // Two triangles per quad, split along whichever diagonal this quad chose.
                    if (!flipped[i, j])
                    {
                        if (TryTriangle(c00, c10, c11, x, z, ref bestY, ref bestScore)) return bestY;
                        if (TryTriangle(c00, c11, c01, x, z, ref bestY, ref bestScore)) return bestY;
                    }
                    else
                    {
                        if (TryTriangle(c00, c10, c01, x, z, ref bestY, ref bestScore)) return bestY;
                        if (TryTriangle(c10, c11, c01, x, z, ref bestY, ref bestScore)) return bestY;
                    }
                }
            }

            // Nothing claimed the point - only reachable through floating point slop on a shared
            // edge, so the nearest triangle's plane is the right answer.
            return bestY;
        }

        /// <summary>
        /// Barycentric evaluation of one triangle. Returns true when the point is strictly inside,
        /// and otherwise records how close it came so the caller can fall back to the best fit.
        /// </summary>
        static bool TryTriangle(Corner a, Corner b, Corner c, float x, float z,
                                ref float bestY, ref float bestScore)
        {
            float v0x = b.X - a.X, v0z = b.Z - a.Z;
            float v1x = c.X - a.X, v1z = c.Z - a.Z;
            float den = v0x * v1z - v1x * v0z;
            if (den > -1e-9f && den < 1e-9f) return false;

            float px = x - a.X, pz = z - a.Z;
            float w1 = (px * v1z - v1x * pz) / den;
            float w2 = (v0x * pz - px * v0z) / den;
            float w0 = 1f - w1 - w2;

            float y = a.Y * w0 + b.Y * w1 + c.Y * w2;

            float score = Mathf.Min(w0, Mathf.Min(w1, w2));
            if (score > bestScore)
            {
                bestScore = score;
                bestY = y;
            }

            return score >= 0f;
        }

        // ---------------------------------------------------------------- stats

        static void MeasureSlopes(
            float[,] heights, int resolution,
            float sizeX, float sizeZ, float sizeY,
            LowPolyTerrainSettings s, ref Result result)
        {
            float stepX = sizeX / (resolution - 1);
            float stepZ = sizeZ / (resolution - 1);

            // Anything within the wall band is expected to be near vertical, so the driveability
            // number is measured on the open ground only.
            float foot = s.buildWall ? s.wallWidth + s.footWander : 0f;

            float maxAll = 0f, maxPan = 0f;

            for (int jz = 1; jz < resolution - 1; jz++)
            {
                float z = jz * stepZ;
                for (int ix = 1; ix < resolution - 1; ix++)
                {
                    float x = ix * stepX;

                    float dhx = (heights[jz, ix + 1] - heights[jz, ix - 1]) * sizeY / (2f * stepX);
                    float dhz = (heights[jz + 1, ix] - heights[jz - 1, ix]) * sizeY / (2f * stepZ);
                    float slope = Mathf.Atan(Mathf.Sqrt(dhx * dhx + dhz * dhz)) * Mathf.Rad2Deg;

                    if (slope > maxAll) maxAll = slope;

                    bool inPan = x > foot && x < sizeX - foot && z > foot && z < sizeZ - foot;
                    if (inPan && slope > maxPan) maxPan = slope;
                }
            }

            result.MaxSlopeDegrees = maxAll;
            result.MaxPanSlopeDegrees = maxPan;
        }

        static float SmoothStep(float edge0, float edge1, float v)
        {
            if (edge1 - edge0 < 1e-6f) return v < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
