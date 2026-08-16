using UnityEngine;

namespace LavaFlow
{
    /// <summary>Which surface the flow is poured over.</summary>
    public enum GroundMode
    {
        /// <summary>Sample a Unity Terrain's heightmap. Fast, and exact on terrain.</summary>
        Terrain = 0,

        /// <summary>Raycast down onto colliders. Slower, but works over meshes, rocks and props.</summary>
        Raycast = 1,

        /// <summary>A flat plane through the generator. Mostly useful for previewing the shape.</summary>
        Flat = 2
    }

    /// <summary>Reads a Unity Terrain's heightmap directly. No colliders needed, and no raycasts.</summary>
    public sealed class TerrainGround : IGroundSampler
    {
        readonly Terrain _terrain;
        readonly TerrainData _data;
        readonly Vector3 _origin;
        readonly Vector3 _size;

        public TerrainGround(Terrain terrain)
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

            // Off the edge of the terrain there is nothing to flow over. Say so rather than
            // clamping, which would run the flow along the boundary forever.
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            point = new Vector3(worldPos.x, _terrain.SampleHeight(worldPos) + _origin.y, worldPos.z);
            normal = _data.GetInterpolatedNormal(u, v);
            return true;
        }
    }

    /// <summary>
    /// A downward probe that can see through nominated objects.
    ///
    /// Raycast ground means "whatever collider is under this point", and that is wrong for anything
    /// standing *over* the river rather than under it: a bridge across the channel becomes the
    /// riverbed, and the lava climbs its deck. Naming the bridge here makes the probe skip it and
    /// read the ground beneath, without needing a spare layer for every such object.
    /// </summary>
    public static class GroundProbe
    {
        // Editor-time generation, single-threaded, so one shared buffer is enough. Sized for the
        // deepest stack of colliders a probe is likely to pass through before it reaches ground.
        static readonly RaycastHit[] Buffer = new RaycastHit[32];

        /// <summary>True when <paramref name="t"/> is one of the ignored roots, or under one.</summary>
        public static bool IsIgnored(Transform t, Transform[] ignore)
        {
            if (ignore == null || t == null) return false;

            for (int i = 0; i < ignore.Length; i++)
            {
                Transform root = ignore[i];
                if (root == null) continue;

                for (Transform p = t; p != null; p = p.parent)
                    if (p == root) return true;
            }

            return false;
        }

        /// <summary>
        /// The topmost collider under <paramref name="worldPos"/> that is not ignored. Casts from
        /// <paramref name="probeUp"/> metres above, down <paramref name="probeDown"/> metres.
        /// </summary>
        public static bool Cast(Vector3 worldPos, float probeUp, float probeDown, LayerMask mask,
                               Transform[] ignore, out RaycastHit hit)
        {
            var ray = new Ray(worldPos + Vector3.up * probeUp, Vector3.down);
            float distance = probeUp + probeDown;

            bool ignoring = false;
            if (ignore != null)
                for (int i = 0; i < ignore.Length && !ignoring; i++)
                    if (ignore[i] != null) ignoring = true;

            // Nothing to skip: the single-hit cast is both cheaper and exactly what we want.
            if (!ignoring)
                return Physics.Raycast(ray, out hit, distance, mask, QueryTriggerInteraction.Ignore);

            int count = Physics.RaycastNonAlloc(ray, Buffer, distance, mask, QueryTriggerInteraction.Ignore);

            // RaycastNonAlloc does not sort, and the buffer can overflow on a busy line, so pick the
            // nearest surviving hit by distance rather than trusting the order they arrive in.
            bool found = false;
            float best = float.MaxValue;
            hit = default(RaycastHit);

            for (int i = 0; i < count && i < Buffer.Length; i++)
            {
                if (IsIgnored(Buffer[i].collider.transform, ignore)) continue;
                if (Buffer[i].distance >= best) continue;

                best = Buffer[i].distance;
                hit = Buffer[i];
                found = true;
            }

            return found;
        }
    }

    /// <summary>
    /// Casts straight down onto colliders. Use this when the flow has to run over meshes or props
    /// rather than only over terrain.
    /// </summary>
    public sealed class RaycastGround : IGroundSampler
    {
        readonly LayerMask _mask;
        readonly float _probeUp;
        readonly float _probeDown;
        readonly Transform[] _ignore;

        public RaycastGround(LayerMask mask, float probeUp, float probeDown)
            : this(mask, probeUp, probeDown, null) { }

        public RaycastGround(LayerMask mask, float probeUp, float probeDown, Transform[] ignore)
        {
            _mask = mask;
            _probeUp = Mathf.Max(1f, probeUp);
            _probeDown = Mathf.Max(2f, probeDown);
            _ignore = ignore;
        }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            RaycastHit hit;
            if (GroundProbe.Cast(worldPos, _probeUp, _probeDown, _mask, _ignore, out hit))
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
}
