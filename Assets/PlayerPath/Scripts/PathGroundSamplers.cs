using UnityEngine;

namespace PlayerPath
{
    /// <summary>Which surface the path is laid over.</summary>
    public enum PathGroundMode
    {
        /// <summary>Sample a Unity Terrain's heightmap. Fast, and exact on terrain.</summary>
        Terrain = 0,

        /// <summary>Raycast down onto colliders. Slower, but works over meshes, rocks and props.</summary>
        Raycast = 1,

        /// <summary>A flat plane through the generator. Mostly useful for previewing the shape.</summary>
        Flat = 2
    }

    /// <summary>
    /// Reads a Unity Terrain's heightmap directly. No colliders needed, and no raycasts.
    ///
    /// This file and <c>PlayerPathGenerator</c> are the only things in the package that touch the
    /// scene at all; the solver and the mesh builder go through <see cref="IPathGround"/> instead.
    /// </summary>
    public sealed class TerrainPathGround : IPathGround
    {
        readonly Terrain _terrain;
        readonly TerrainData _data;
        readonly Vector3 _origin;
        readonly Vector3 _size;

        public TerrainPathGround(Terrain terrain)
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

            // Off the edge of the terrain there is nothing to build on. Say so rather than
            // clamping, which would smear the last height along the boundary.
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            point = new Vector3(worldPos.x, _terrain.SampleHeight(worldPos) + _origin.y, worldPos.z);
            normal = _data.GetInterpolatedNormal(u, v);
            return true;
        }
    }

    /// <summary>
    /// Casts straight down onto colliders. Use this when the path has to run over meshes, rocks or
    /// props rather than only over terrain.
    /// </summary>
    public sealed class RaycastPathGround : IPathGround
    {
        readonly LayerMask _mask;
        readonly float _probeUp;
        readonly float _probeDown;

        public RaycastPathGround(LayerMask mask, float probeUp, float probeDown)
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
}
