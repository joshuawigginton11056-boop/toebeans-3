using UnityEngine;

namespace RockBridge
{
    /// <summary>Where the bridge reads the world from when it works out its own height.</summary>
    public enum BridgeGroundMode
    {
        /// <summary>
        /// Terrain heightmap for the floor, colliders for the surface. This is the one to leave it
        /// on: the deck clears whatever is lying on the ground — a lava pool, a rock, another
        /// bridge — while the piers still reach the solid floor underneath it.
        /// </summary>
        Auto = 0,

        /// <summary>Terrain heightmap only. Fast and exact, but blind to anything sitting on it.</summary>
        Terrain = 1,

        /// <summary>Colliders only. Use where there is no Unity Terrain under the crossing.</summary>
        Colliders = 2,

        /// <summary>A flat plane at a fixed height. Mostly for previewing the shape.</summary>
        Flat = 3
    }

    /// <summary>
    /// What is underneath one point of the bridge.
    ///
    /// The two heights are kept apart because they answer different questions, and using one for
    /// both is what puts a deck through a lava pool or a pier hanging in mid-air:
    ///
    /// <see cref="Surface"/> is the top of whatever is there — the lava, the boulder, the hillside.
    /// It is what the deck has to clear.
    ///
    /// <see cref="Floor"/> is the solid ground beneath all of it. It is where a pier has to foot. A
    /// leg that stopped at <see cref="Surface"/> over a lava pool would stand on the lava rather
    /// than rise out of it, which reads as a mistake even though nothing is geometrically wrong.
    /// </summary>
    public struct GroundSample
    {
        /// <summary>False when there is nothing under this point at all — off the edge of the world.</summary>
        public bool Found;

        /// <summary>Top of the highest thing here, in world Y.</summary>
        public float Surface;

        /// <summary>Top of the solid ground here, in world Y. Never above <see cref="Surface"/>.</summary>
        public float Floor;
    }

    /// <summary>
    /// Reads the world under the bridge. The solver and the mesh builder go through this rather than
    /// touching the scene, which is what keeps them runnable in the headless harness — a test hands
    /// in a synthetic hillside and asserts on the triangles that come out.
    /// </summary>
    public interface IBridgeGround
    {
        bool Sample(Vector3 worldPos, out GroundSample sample);
    }

    /// <summary>A level plane. Nothing to probe, so it always answers.</summary>
    public sealed class FlatBridgeGround : IBridgeGround
    {
        readonly float _height;

        public FlatBridgeGround(float height) { _height = height; }

        public bool Sample(Vector3 worldPos, out GroundSample sample)
        {
            sample = new GroundSample { Found = true, Surface = _height, Floor = _height };
            return true;
        }
    }

    /// <summary>
    /// Reads a Unity Terrain's heightmap directly. No colliders needed, and no raycasts — so this is
    /// the only sampler that still works while the scene's colliders are mid-rebuild.
    /// </summary>
    public sealed class TerrainBridgeGround : IBridgeGround
    {
        readonly Terrain _terrain;
        readonly Vector3 _origin;
        readonly Vector3 _size;

        public TerrainBridgeGround(Terrain terrain)
        {
            _terrain = terrain;
            TerrainData data = terrain != null ? terrain.terrainData : null;
            _origin = terrain != null ? terrain.transform.position : Vector3.zero;
            _size = data != null ? data.size : Vector3.one;
        }

        public bool IsValid { get { return _terrain != null && _terrain.terrainData != null; } }

        public bool Sample(Vector3 worldPos, out GroundSample sample)
        {
            sample = new GroundSample();
            if (!IsValid) return false;

            float u = (worldPos.x - _origin.x) / _size.x;
            float v = (worldPos.z - _origin.z) / _size.z;

            // Off the edge of the terrain there is nothing to build on. Say so rather than clamping,
            // which would smear the boundary height out under a bridge running past the edge.
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            float y = _terrain.SampleHeight(worldPos) + _origin.y;
            sample.Found = true;
            sample.Surface = y;
            sample.Floor = y;
            return true;
        }
    }

    /// <summary>
    /// Casts straight down through everything under a point and keeps both ends of what it finds.
    ///
    /// <c>RaycastAll</c> rather than <c>Raycast</c>, and that is the whole reason this class exists:
    /// a single cast onto a lava pool stops at the lava, so a pier built from it would stand on the
    /// surface. Taking every hit gives the pool's top as the surface and the lake bed under it as
    /// the floor, which is what lets a leg rise out of the lava the way a real one would.
    /// </summary>
    public sealed class ColliderBridgeGround : IBridgeGround
    {
        readonly LayerMask _mask;
        readonly float _probeUp;
        readonly float _probeDown;
        readonly Collider[] _ignore;
        readonly RaycastHit[] _hits = new RaycastHit[32];

        public ColliderBridgeGround(LayerMask mask, float probeUp, float probeDown, Collider[] ignore)
        {
            _mask = mask;
            _probeUp = Mathf.Max(1f, probeUp);
            _probeDown = Mathf.Max(2f, probeDown);
            _ignore = ignore;
        }

        public bool Sample(Vector3 worldPos, out GroundSample sample)
        {
            sample = new GroundSample();

            var origin = new Vector3(worldPos.x, worldPos.y + _probeUp, worldPos.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _probeUp + _probeDown,
                                                _mask, QueryTriggerInteraction.Ignore);

            float high = float.NegativeInfinity;
            float low = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                // The bridge must never read itself, or raising the deck raises the ground it is
                // measuring against and the height runs away on every rebuild.
                if (IsIgnored(_hits[i].collider)) continue;

                float y = _hits[i].point.y;
                if (y > high) high = y;
                if (y < low) low = y;
            }

            if (float.IsNegativeInfinity(high)) return false;

            sample.Found = true;
            sample.Surface = high;
            sample.Floor = low;
            return true;
        }

        bool IsIgnored(Collider c)
        {
            if (c == null) return true;
            if (_ignore == null) return false;

            for (int i = 0; i < _ignore.Length; i++)
                if (_ignore[i] == c) return true;
            return false;
        }
    }

    /// <summary>
    /// Terrain for the floor, colliders for the surface, and each falls back to the other where it
    /// has nothing to say. This is <see cref="BridgeGroundMode.Auto"/>, and it is the default
    /// because it is the only one that gets both of a bridge's questions right at once over a
    /// lava pool sitting in a terrain basin.
    /// </summary>
    public sealed class CompositeBridgeGround : IBridgeGround
    {
        readonly TerrainBridgeGround _terrain;
        readonly ColliderBridgeGround _colliders;

        public CompositeBridgeGround(TerrainBridgeGround terrain, ColliderBridgeGround colliders)
        {
            _terrain = terrain;
            _colliders = colliders;
        }

        public bool Sample(Vector3 worldPos, out GroundSample sample)
        {
            var t = new GroundSample();
            var c = new GroundSample();

            bool hasTerrain = _terrain != null && _terrain.Sample(worldPos, out t) && t.Found;
            bool hasColliders = _colliders != null && _colliders.Sample(worldPos, out c) && c.Found;

            sample = new GroundSample();
            if (!hasTerrain && !hasColliders) return false;

            if (!hasTerrain) { sample = c; return true; }
            if (!hasColliders) { sample = t; return true; }

            sample.Found = true;
            sample.Surface = Mathf.Max(t.Surface, c.Surface);

            // The terrain heightmap is the floor, full stop. The colliders only ever say what is
            // lying on top of it.
            //
            // The first version took whichever of the two was lower, reasoning that a pier should
            // never be left short of solid ground. That is wrong, because the lowest thing a ray
            // meets is not the ground — it is the far side of whatever it passed through. Over a
            // lava pool the ray exits through the pool's own underside, 5 m below the lake bed, and
            // that became "the floor": the legs overshot, and the landings — which sit on the floor
            // — sank with it. Worse, it made the terrain blend unstable, since blending lowered the
            // ground, which lowered the floor, which lowered the landing, which asked the blend to
            // dig again.
            sample.Floor = t.Floor;
            return true;
        }
    }
}
