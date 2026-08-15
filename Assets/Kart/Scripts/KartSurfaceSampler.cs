using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Karting
{
    /// <summary>
    /// Works out what a wheel is standing on. On terrain it reads the splat weights under the contact
    /// point and blends the two strongest layers; on ordinary colliders it falls back to the physics
    /// material, then the renderer's material, then the object's name.
    ///
    /// Sampling is throttled per wheel: splat lookups are cheap but not free, and the ground under a
    /// tyre does not change meaningfully between physics ticks.
    /// </summary>
    public class KartSurfaceSampler
    {
        /// <summary>Seconds between splat lookups for a given wheel.</summary>
        public float sampleInterval = 0.07f;

        readonly Dictionary<Terrain, KartSurface[]> _terrainLayers = new();
        readonly Dictionary<Collider, KartSurface> _colliderSurfaces = new();
        readonly float[] _nextSampleTime;
        readonly KartSurface[] _cached;

        public KartSurfaceSampler(int wheelCount)
        {
            _nextSampleTime = new float[wheelCount];
            _cached = new KartSurface[wheelCount];
            for (int i = 0; i < wheelCount; i++)
                _cached[i] = KartSurface.Default;
        }

        public KartSurface Cached(int wheelIndex) => _cached[wheelIndex];

        /// <summary>Surface under a grounded wheel, re-read at most every <see cref="sampleInterval"/>.</summary>
        public KartSurface Sample(int wheelIndex, Collider collider, Vector3 point, float time)
        {
            if (collider == null)
                return _cached[wheelIndex];

            if (time < _nextSampleTime[wheelIndex])
                return _cached[wheelIndex];

            _nextSampleTime[wheelIndex] = time + sampleInterval;
            _cached[wheelIndex] = Resolve(collider, point);
            return _cached[wheelIndex];
        }

        /// <summary>Uncached lookup, for editor tooling and tests.</summary>
        public KartSurface Resolve(Collider collider, Vector3 point)
        {
            var terrain = collider.GetComponent<Terrain>();
            if (terrain != null && terrain.terrainData != null)
                return SampleTerrain(terrain, point);

            if (_colliderSurfaces.TryGetValue(collider, out KartSurface known))
                return known;

            KartSurface resolved = KartSurfaceLibrary.Classify(DescribeCollider(collider));
            _colliderSurfaces[collider] = resolved;
            return resolved;
        }

        // ------------------------------------------------------------------ terrain

        KartSurface SampleTerrain(Terrain terrain, Vector3 point)
        {
            TerrainData data = terrain.terrainData;
            KartSurface[] layers = LayerSurfaces(terrain);
            if (layers.Length == 0)
                return KartSurface.Default;

            Vector3 local = point - terrain.transform.position;
            float u = Mathf.Clamp01(local.x / data.size.x);
            float v = Mathf.Clamp01(local.z / data.size.z);

            int x = Mathf.Clamp(Mathf.RoundToInt(u * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);

            float[,,] weights = data.GetAlphamaps(x, y, 1, 1);

            int first = -1, second = -1;
            float firstWeight = 0f, secondWeight = 0f;
            int count = Mathf.Min(layers.Length, weights.GetLength(2));

            for (int i = 0; i < count; i++)
            {
                float w = weights[0, 0, i];
                if (w > firstWeight)
                {
                    second = first; secondWeight = firstWeight;
                    first = i; firstWeight = w;
                }
                else if (w > secondWeight)
                {
                    second = i; secondWeight = w;
                }
            }

            if (first < 0)
                return KartSurface.Default;
            if (second < 0 || secondWeight <= 0.001f)
                return layers[first];

            // Straddling a boundary: blend, so grip changes as the paint does rather than snapping
            // at the midpoint of a blend the artist drew as a gradient.
            float t = secondWeight / (firstWeight + secondWeight);
            return KartSurfaceLibrary.Blend(layers[first], layers[second], t);
        }

        KartSurface[] LayerSurfaces(Terrain terrain)
        {
            if (_terrainLayers.TryGetValue(terrain, out KartSurface[] cached))
                return cached;

            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            var surfaces = new KartSurface[terrainLayers.Length];

            for (int i = 0; i < terrainLayers.Length; i++)
            {
                TerrainLayer layer = terrainLayers[i];
                surfaces[i] = KartSurfaceLibrary.Classify(DescribeLayer(layer));
            }

            _terrainLayers[terrain] = surfaces;
            return surfaces;
        }

        /// <summary>
        /// The layer's own name first, then the texture it paints with. In this project the layers are
        /// all called "NewLayer N", so the texture is the only thing carrying the information.
        /// </summary>
        public static string DescribeLayer(TerrainLayer layer)
        {
            if (layer == null)
                return null;

            KartSurface byName = KartSurfaceLibrary.Classify(layer.name);
            if (!string.Equals(byName.name, KartSurface.Default.name))
                return layer.name;

            return layer.diffuseTexture != null ? layer.diffuseTexture.name : layer.name;
        }

        static string DescribeCollider(Collider collider)
        {
            // A physics material is the deliberate signal, so it wins over anything incidental.
            var physicsMaterial = collider.sharedMaterial;
            if (physicsMaterial != null)
                return physicsMaterial.name;

            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                return renderer.sharedMaterial.name;

            return collider.gameObject.name;
        }
    }
}
