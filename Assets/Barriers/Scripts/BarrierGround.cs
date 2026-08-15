using UnityEngine;

namespace Barriers
{
    /// <summary>Which surface the barriers are snapped down onto.</summary>
    public enum BarrierGroundMode
    {
        /// <summary>Sample a Unity Terrain's heightmap. Fast, exact, and needs no collider.</summary>
        Terrain = 0,

        /// <summary>Raycast down onto colliders. Slower, but sees paths, rocks, bridges and props.</summary>
        Raycast = 1,

        /// <summary>A flat plane through the line's own object. Useful for previewing the shape.</summary>
        Flat = 2
    }

    /// <summary>
    /// Anything that can answer "where is the ground under here, and which way does it face".
    ///
    /// Kept as an interface so the route builder never touches the scene: the component hands it a
    /// terrain or a raycast sampler, a test hands it an analytic hillside.
    /// </summary>
    public interface IBarrierGround
    {
        /// <summary>Ground point and unit normal under <paramref name="worldPos"/>, in world space.
        /// Returns false when there is nothing under there to stand on.</summary>
        bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal);
    }

    /// <summary>A flat plane at a fixed height. What a line falls back to with no terrain.</summary>
    public sealed class FlatBarrierGround : IBarrierGround
    {
        readonly float _y;
        public FlatBarrierGround(float y) { _y = y; }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            point = new Vector3(worldPos.x, _y, worldPos.z);
            normal = Vector3.up;
            return true;
        }
    }

    /// <summary>Reads a Unity Terrain's heightmap directly. No colliders needed, and no raycasts.</summary>
    public sealed class TerrainBarrierGround : IBarrierGround
    {
        readonly Terrain _terrain;
        readonly TerrainData _data;
        readonly Vector3 _origin;
        readonly Vector3 _size;

        public TerrainBarrierGround(Terrain terrain)
        {
            _terrain = terrain;
            _data = terrain != null ? terrain.terrainData : null;
            _origin = terrain != null ? terrain.transform.position : Vector3.zero;
            _size = _data != null ? _data.size : Vector3.one;
        }

        public bool IsValid { get { return _terrain != null && _data != null; } }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            point = worldPos;
            normal = Vector3.up;
            if (!IsValid) return false;

            float u = (worldPos.x - _origin.x) / _size.x;
            float v = (worldPos.z - _origin.z) / _size.z;

            // Off the edge of the terrain there is nothing to stand a post on. Say so rather than
            // clamping, which would pile the whole overhanging run up on the boundary.
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            point = new Vector3(worldPos.x, _terrain.SampleHeight(worldPos) + _origin.y, worldPos.z);
            normal = _data.GetInterpolatedNormal(u, v);
            return true;
        }
    }

    /// <summary>
    /// Casts straight down onto colliders. Use this when the line has to run along a generated path,
    /// a bridge or a rock rather than only over terrain.
    /// </summary>
    public sealed class RaycastBarrierGround : IBarrierGround
    {
        readonly LayerMask _mask;
        readonly float _probeUp;
        readonly float _probeDown;

        public RaycastBarrierGround(LayerMask mask, float probeUp, float probeDown)
        {
            _mask = mask;
            _probeUp = Mathf.Max(1f, probeUp);
            _probeDown = Mathf.Max(2f, probeDown);
        }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            var ray = new Ray(worldPos + Vector3.up * _probeUp, Vector3.down);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, _probeUp + _probeDown, _mask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            point = worldPos;
            normal = Vector3.up;
            return false;
        }
    }

    /// <summary>
    /// The same xorshift generator the other generators in this project use. Deterministic from a
    /// seed, so a line rebuilds identically after a scene reload or a domain reload — without that,
    /// every rebuild would reshuffle which prefab landed where.
    /// </summary>
    public sealed class BarrierRng
    {
        uint _state;

        public BarrierRng(int seed)
        {
            _state = (uint)seed;
            if (_state == 0u) _state = 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>Uniform in [0,1).</summary>
        public float Value { get { return (NextUInt() & 0xFFFFFF) / 16777216f; } }

        public float Range(float min, float max) { return min + (max - min) * Value; }

        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }
}
