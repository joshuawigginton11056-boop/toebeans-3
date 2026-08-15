using UnityEngine;

namespace RockBridge
{
    /// <summary>
    /// Deterministic xorshift32. Same generator as the ones in <c>LavaPond</c> and <c>LavaFlow</c>,
    /// kept as its own copy so the packages stay independent: the same seed produces the same rock on
    /// every platform, and generation never disturbs the global random state.
    /// </summary>
    public struct Rng
    {
        uint _state;

        public Rng(int seed)
        {
            // 0 is an absorbing state for xorshift, so fold the seed into something non-zero.
            _state = (uint)seed * 747796405u + 2891336453u;
            if (_state == 0u) _state = 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Value()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>Uniform in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Value();
        }

        /// <summary>Uniform in [-amount, amount].</summary>
        public float Signed(float amount)
        {
            return (Value() * 2f - 1f) * amount;
        }
    }

    /// <summary>
    /// Hash-based value noise. Everything the rock is roughened by is driven from here rather than
    /// from Perlin, so a given seed rebuilds the identical bridge in any Unity version.
    /// </summary>
    public static class BridgeNoise
    {
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
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
        public static float Value(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = Fade(x - xi);
            float yf = Fade(y - yi);

            float v00 = Hash(xi, yi, seed);
            float v10 = Hash(xi + 1, yi, seed);
            float v01 = Hash(xi, yi + 1, seed);
            float v11 = Hash(xi + 1, yi + 1, seed);

            float a = Mathf.Lerp(v00, v10, xf);
            float b = Mathf.Lerp(v01, v11, xf);
            return Mathf.Lerp(a, b, yf);
        }

        /// <summary>
        /// Three octaves, remapped to [-1, 1]. This is what the rock faces are displaced by.
        /// </summary>
        public static float Fbm(float x, float y, int seed)
        {
            float v = Value(x, y, seed) * 0.55f
                    + Value(x * 2.03f + 5.7f, y * 2.03f + 1.9f, seed + 331) * 0.30f
                    + Value(x * 4.11f - 2.3f, y * 4.11f + 8.1f, seed + 733) * 0.15f;
            return v * 2f - 1f;
        }

        /// <summary>
        /// Noise that wraps exactly over <paramref name="period"/> steps of <paramref name="x"/>.
        ///
        /// A pier is a ring of faces, so its roughness has to meet itself: sampling plain noise round
        /// the ring leaves one seam face where the last sample does not match the first, and on a
        /// flat-shaded column that single mismatched facet is the one thing the eye finds. Blending
        /// the sample with its wrapped partner costs one extra lookup and removes the seam entirely.
        /// </summary>
        public static float Ring(float x, float y, int period, int seed)
        {
            if (period < 2) return Fbm(x, y, seed);

            float t = Mathf.Repeat(x, period) / period;
            float a = Fbm(Mathf.Repeat(x, period), y, seed);
            float b = Fbm(Mathf.Repeat(x, period) - period, y, seed);
            return Mathf.Lerp(a, b, t);
        }
    }
}
