using UnityEngine;

namespace PlayerPath
{
    /// <summary>
    /// Deterministic xorshift32. Its own copy of the generator the lava packages use, kept separate
    /// so this package stands alone: the same seed lays the same paving on every platform, and
    /// generating never disturbs Unity's global random state.
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

        /// <summary>Uniform integer in [min, max).</summary>
        public int Range(int min, int max)
        {
            if (max <= min) return min;
            return min + (int)(NextUInt() % (uint)(max - min));
        }

        public bool Chance(float probability)
        {
            return Value() < probability;
        }

        /// <summary>Uniform in [-amount, amount].</summary>
        public float Signed(float amount)
        {
            return (Value() * 2f - 1f) * amount;
        }
    }

    /// <summary>
    /// Hash-based value noise. Everything the path varies by is driven from here rather than from
    /// Perlin, so a given seed rebuilds the identical path in any Unity version.
    /// </summary>
    public static class PathNoise
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

        /// <summary>Stable [0, 1) hash of a single integer id. Used for per-stone variation.</summary>
        public static float Hash1(int id, int seed)
        {
            return Hash(id, id * 31 + 7, seed);
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

        /// <summary>Two octaves of value noise, remapped to [-1, 1].</summary>
        public static float Signed(float x, float y, int seed)
        {
            float v = Value(x, y, seed) * 0.65f + Value(x * 2.17f + 11.3f, y * 2.17f - 7.9f, seed + 977) * 0.35f;
            return v * 2f - 1f;
        }
    }
}
