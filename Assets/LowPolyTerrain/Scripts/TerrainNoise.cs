using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Deterministic xorshift32. Own copy rather than a shared one, matching the other generators
    /// here, so the shaper stays free of assembly dependencies and can be run headlessly.
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
    /// Hash-based value noise. Everything the shaper needs is here so the height field is stable
    /// across platforms and Unity versions, and so no Unity native call is reached from the builder.
    /// </summary>
    public static class TerrainNoise
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

        /// <summary>Fractal value noise in [0, 1]. Octaves double in frequency and halve in weight.</summary>
        public static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, fx = x, fy = y;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(fx, fy, seed + i * 1013) * amp;
                norm += amp;
                amp *= 0.5f;
                // Offset each octave so the lattices do not line up and streak.
                fx = fx * 2f + 17.3f;
                fy = fy * 2f - 9.7f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// Ridged fractal noise in [0, 1]. Folding the noise about its midpoint turns the smooth
        /// humps of <see cref="Fbm"/> into creases, which is what makes a crest read as a mountain
        /// range rather than a row of dunes.
        /// </summary>
        public static float Ridge(float x, float y, int seed, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, fx = x, fy = y;
            for (int i = 0; i < octaves; i++)
            {
                float v = 1f - Mathf.Abs(Value(fx, fy, seed + i * 7717) * 2f - 1f);
                sum += v * v * amp;
                norm += amp;
                amp *= 0.5f;
                fx = fx * 2f + 5.1f;
                fy = fy * 2f + 23.9f;
            }
            return norm > 0f ? sum / norm : 0f;
        }
    }
}
