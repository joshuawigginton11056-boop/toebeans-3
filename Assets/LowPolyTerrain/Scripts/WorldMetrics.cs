using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// The one place the size of a world is written down.
    ///
    /// Every race map has to be the same size, and a Unity terrain has five separate numbers that
    /// decide that - size, heightmap resolution, alphamap resolution, base map resolution and
    /// detail resolution - spread across an asset you can only edit through the inspector. Copying
    /// a map by hand means copying five numbers correctly, and getting the heightmap resolution
    /// wrong by one step silently halves the facet density rather than failing.
    ///
    /// These values are LavaWorld's, read off <c>Assets/Terrain/LavaWorld_Terrain.asset</c>. Change
    /// them here and <see cref="LowPolyTerrain.EditorTools.WorldTerrainFactory"/> stamps every new
    /// world to match; existing terrains are not touched, because resizing a terrain that already
    /// has objects placed on it moves the ground out from under all of them.
    /// </summary>
    public static class WorldMetrics
    {
        /// <summary>Map extent in metres, on both X and Z. Maps are square.</summary>
        public const float Span = 500f;

        /// <summary>
        /// Terrain height ceiling in metres. Heights are stored as a 0-1 fraction of this, so it is
        /// also the vertical precision: 150 m over a 16-bit heightmap is ~2 mm steps, far below
        /// anything a kart can feel. Raising it costs precision, which is why the mountain wall is
        /// 75 m rather than the ceiling.
        /// </summary>
        public const float Height = 150f;

        /// <summary>
        /// Heightmap resolution. Must be 2^n + 1 - Unity silently rounds anything else to the
        /// nearest valid value, so it is a constant rather than something callers compute.
        /// At 1025 over 500 m the grid is ~0.49 m, which comfortably resolves an 8 m facet.
        /// </summary>
        public const int HeightmapResolution = 1025;

        /// <summary>Splat map resolution. Powers of two here, unlike the heightmap.</summary>
        public const int AlphamapResolution = 1024;

        /// <summary>Resolution of the baked low-distance terrain texture.</summary>
        public const int BaseMapResolution = 1024;

        /// <summary>Detail (grass/mesh scatter) map resolution, and the patch size it is split into.</summary>
        public const int DetailResolution = 1024;
        public const int DetailResolutionPerPatch = 32;

        /// <summary>Terrain LOD aggressiveness, in pixels of screen error.</summary>
        public const float HeightmapPixelError = 5f;

        /// <summary>Distance at which the terrain falls back to the baked base map, in metres.</summary>
        public const float BasemapDistance = 1000f;

        /// <summary>Terrain size as Unity wants it.</summary>
        public static Vector3 Size
        {
            get { return new Vector3(Span, Height, Span); }
        }

        /// <summary>
        /// Where a new world's terrain corner goes, so the playable area is centred on the world
        /// origin. Large coordinates cost floating-point precision in physics, and a kart is a
        /// rigidbody stack that shows it as jitter, so a map should straddle the origin rather than
        /// sit off in the positive quadrant.
        ///
        /// LavaWorld predates this and sits at (402, 0, 636.7). It is deliberately left where it is:
        /// moving it now would move the ground out from under every barrier, prop and spline on it.
        /// </summary>
        public static Vector3 Origin
        {
            get { return new Vector3(-Span * 0.5f, 0f, -Span * 0.5f); }
        }

        /// <summary>
        /// Drivable extent left once the mountain wall has eaten its margin, in metres. The wall
        /// reaches in from all four sides, hence twice the width.
        /// </summary>
        public static float PlayableSpan(float wallWidth)
        {
            return Span - 2f * wallWidth;
        }
    }
}
