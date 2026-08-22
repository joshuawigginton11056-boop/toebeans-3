using UnityEngine;

namespace Farm
{
    /// <summary>
    /// The clock every piece of farm ambience reads.
    ///
    /// It exists because of the constraint the rest of this project is built to: the game is
    /// multiplayer-shaped now, not later. Nothing on the farm accumulates state frame by frame — a
    /// cow's position, a windmill's angle and a duck's bob are all pure functions of (seed, time).
    /// Give two machines the same seed and the same time and they draw the same farm, with no
    /// animation state on the wire and no drift over a twenty-minute session.
    ///
    /// That only holds if "the same time" means something. <see cref="Time.timeAsDouble"/> counts
    /// from each client's own load, so two clients that joined a minute apart are a minute out.
    /// Nothing here is gameplay — a cow standing somewhere slightly different is cosmetic — but the
    /// animals do carry colliders, and a kart clipping a cow that is not there on the host is the
    /// kind of bug that is impossible to reproduce.
    ///
    /// So: point <see cref="Source"/> at the session's network time as soon as there is one, and
    /// the whole farm falls into step. One line, from wherever the netcode lives:
    ///
    ///     FarmClock.Source = () => NetworkManager.ServerTime.Time;
    ///
    /// Until then it runs off local time, which is right for single player and for the editor.
    /// </summary>
    public static class FarmClock
    {
        public delegate double TimeSource();

        /// <summary>Set this to the session clock. Null falls back to local time.</summary>
        public static TimeSource Source;

        public static double Now
        {
            get { return Source != null ? Source() : Time.timeAsDouble; }
        }

        /// <summary>
        /// A stable per-instance seed from a world position.
        ///
        /// Scene-authored positions are identical on every client, so this gives each animal the
        /// same seed everywhere without anybody assigning one. Quantised to the centimetre first,
        /// because a float that survived a scene save and a float that survived a network transform
        /// are not bit-identical, and a hash amplifies that into a completely different animal.
        /// </summary>
        public static int SeedFrom(Vector3 position)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(position.x * 100f);
                int y = Mathf.RoundToInt(position.y * 100f);
                int z = Mathf.RoundToInt(position.z * 100f);
                int h = 17;
                h = h * 31 + x;
                h = h * 31 + y;
                h = h * 31 + z;
                return h == 0 ? 1 : h;
            }
        }
    }

    /// <summary>
    /// A tiny deterministic random source.
    ///
    /// Not <see cref="UnityEngine.Random"/>: that is one global stream, so what it hands a cow
    /// depends on how many other things drew from it first this frame. Two clients loading the same
    /// scene in a different order would get different farms, which is the one thing this whole
    /// arrangement is trying to avoid.
    /// </summary>
    public struct FarmRandom
    {
        uint _state;

        public FarmRandom(int seed)
        {
            _state = (uint)(seed == 0 ? 1 : seed);
        }

        public uint NextUInt()
        {
            // xorshift32. Small, fast, and identical on every platform, which a hash based on
            // floating point would not be.
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public float Value { get { return (NextUInt() & 0xFFFFFF) / (float)0x1000000; } }

        public float Range(float min, float max) { return min + (max - min) * Value; }

        public int Range(int minInclusive, int maxExclusive)
        {
            int span = maxExclusive - minInclusive;
            return span <= 0 ? minInclusive : minInclusive + (int)(NextUInt() % (uint)span);
        }
    }
}
