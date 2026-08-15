using UnityEngine;

namespace CaveTunnel
{
    /// <summary>
    /// Small hash-based 3D value noise. Three dimensions rather than two because the wall wobble is
    /// sampled on a ring: feeding it the point on the unit circle plus the distance along the path
    /// is the only way to get a lump that joins up with itself where the ring closes.
    ///
    /// Hash-based rather than <c>Mathf.PerlinNoise</c> so the same seed gives the same rock on every
    /// platform and Unity version, and so generation never disturbs the global random state.
    /// </summary>
    public static class CaveNoise
    {
        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1440662683 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        static float Fade(float t)
        {
            return t * t * (3f - 2f * t);
        }

        /// <summary>Smoothed value noise in [0, 1].</summary>
        public static float Value(float x, float y, float z, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            int zi = Mathf.FloorToInt(z);
            float xf = Fade(x - xi);
            float yf = Fade(y - yi);
            float zf = Fade(z - zi);

            float v000 = Hash(xi, yi, zi, seed);
            float v100 = Hash(xi + 1, yi, zi, seed);
            float v010 = Hash(xi, yi + 1, zi, seed);
            float v110 = Hash(xi + 1, yi + 1, zi, seed);
            float v001 = Hash(xi, yi, zi + 1, seed);
            float v101 = Hash(xi + 1, yi, zi + 1, seed);
            float v011 = Hash(xi, yi + 1, zi + 1, seed);
            float v111 = Hash(xi + 1, yi + 1, zi + 1, seed);

            float x00 = Mathf.Lerp(v000, v100, xf);
            float x10 = Mathf.Lerp(v010, v110, xf);
            float x01 = Mathf.Lerp(v001, v101, xf);
            float x11 = Mathf.Lerp(v011, v111, xf);

            float y0 = Mathf.Lerp(x00, x10, yf);
            float y1 = Mathf.Lerp(x01, x11, yf);
            return Mathf.Lerp(y0, y1, zf);
        }

        /// <summary>Two octaves of value noise, remapped to [-1, 1].</summary>
        public static float Signed(float x, float y, float z, int seed)
        {
            float v = Value(x, y, z, seed) * 0.65f
                    + Value(x * 2.13f + 19.7f, y * 2.13f - 5.1f, z * 2.13f + 31.4f, seed + 977) * 0.35f;
            return v * 2f - 1f;
        }
    }
}
