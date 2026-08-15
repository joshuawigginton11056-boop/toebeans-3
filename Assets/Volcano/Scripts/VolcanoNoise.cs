using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Deterministic xorshift32, the same one the other generators in this project use. Kept out of
    /// UnityEngine.Random so the same seed builds the same volcano on every platform and generation
    /// never disturbs the global random state.
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
    /// Hash-based value noise. Two flavours: the ordinary 2D field used to rough up the flanks, and
    /// a <see cref="Ring"/> variant that wraps exactly once around the cone.
    ///
    /// The ring version matters more than it looks. Sampling a 2D noise field along a circle very
    /// nearly works, but the sample at 359 degrees and the one at 1 degree land in different cells,
    /// so the gullies never close up and the volcano carries a visible seam down one side.
    /// </summary>
    public static class VolcanoNoise
    {
        static float Hash(int x, int y, int seed)
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

        /// <summary>Positive modulo, so a cell index either side of the seam lands on the same hash.</summary>
        static int Wrap(int i, int period)
        {
            int m = i % period;
            return m < 0 ? m + period : m;
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
            float v = Value(x, y, seed) * 0.65f +
                      Value(x * 2.17f + 11.3f, y * 2.17f - 7.9f, seed + 977) * 0.35f;
            return v * 2f - 1f;
        }

        /// <summary>
        /// Value noise around a circle, in [0, 1]. <paramref name="turns"/> is the angle as a
        /// fraction of a full turn and <paramref name="period"/> is how many cells fit in that turn,
        /// so the field is exactly periodic and the seam closes.
        /// </summary>
        public static float Ring(float turns, int period, int seed)
        {
            if (period < 1) period = 1;

            float x = turns * period;
            int xi = Mathf.FloorToInt(x);
            float xf = Fade(x - xi);

            int a = Wrap(xi, period);
            int b = Wrap(xi + 1, period);
            return Mathf.Lerp(Hash(a, 0, seed), Hash(b, 0, seed), xf);
        }

        /// <summary>Ring noise remapped to [-1, 1], with a second octave for a less regular outline.</summary>
        public static float RingSigned(float turns, int period, int seed)
        {
            float v = Ring(turns, period, seed) * 0.68f +
                      Ring(turns, period * 2, seed + 613) * 0.32f;
            return v * 2f - 1f;
        }

        /// <summary>
        /// Ridged ring noise in [0, 1], peaking in narrow bands. Feeding this into a subtraction is
        /// what carves gullies rather than gentle waviness: <paramref name="sharpness"/> above 1
        /// pinches the peaks into channels and leaves the ground between them alone.
        /// </summary>
        public static float RingRidged(float turns, int period, int seed, float sharpness)
        {
            float n = Ring(turns, period, seed) * 0.7f + Ring(turns, period * 2, seed + 4231) * 0.3f;
            float ridge = 1f - Mathf.Abs(n * 2f - 1f);
            return Mathf.Pow(Mathf.Clamp01(ridge), Mathf.Max(0.05f, sharpness));
        }

        /// <summary>Three octaves of 2D value noise in [-1, 1], for surface roughness.</summary>
        public static float Fbm(float x, float y, int seed)
        {
            float v = Value(x, y, seed) * 0.55f +
                      Value(x * 2.03f + 5.1f, y * 2.03f - 3.7f, seed + 1301) * 0.30f +
                      Value(x * 4.11f - 9.4f, y * 4.11f + 2.2f, seed + 2609) * 0.15f;
            return v * 2f - 1f;
        }

        /// <summary>Hermite ease between 0 and 1. Clamped, unlike a bare lerp.</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Hermite ease from <paramref name="a"/> to <paramref name="b"/>. Handles b &lt; a.</summary>
        public static float SmoothStep(float a, float b, float t)
        {
            if (Mathf.Abs(b - a) < 1e-6f) return t >= b ? 1f : 0f;
            return SmoothStep01((t - a) / (b - a));
        }
    }
}
