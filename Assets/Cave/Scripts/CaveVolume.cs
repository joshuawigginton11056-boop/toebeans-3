using UnityEngine;

namespace CaveTunnel
{
    /// <summary>
    /// A solid-body query for the cave interior, produced alongside the mesh.
    ///
    /// This exists so the terrain hole puncher can ask "is this bit of hillside standing inside my
    /// tunnel?" without re-deriving the swept path. Testing against the mesh triangles would be both
    /// slower and wrong: the mesh is an open-ended surface, so there is no inside to test against.
    ///
    /// Everything is in the generator's local space.
    /// </summary>
    public sealed class CaveVolume
    {
        readonly Vector3[] _positions;
        readonly Vector3[] _axes;
        readonly Vector3[] _ups;
        readonly Vector3[] _rights;
        readonly float[] _widths;
        readonly float[] _heights;
        readonly float[] _floorFlattens;
        readonly float[] _halfExtents;

        /// <summary>Local-space bounds of the interior, so callers can skip work far away.</summary>
        public Bounds LocalBounds { get; private set; }

        public int SampleCount { get { return _positions.Length; } }

        internal CaveVolume(Vector3[] positions, Vector3[] axes, Vector3[] ups, Vector3[] rights,
                            float[] widths, float[] heights, float[] floorFlattens)
        {
            _positions = positions;
            _axes = axes;
            _ups = ups;
            _rights = rights;
            _widths = widths;
            _heights = heights;
            _floorFlattens = floorFlattens;

            int n = positions.Length;
            _halfExtents = new float[n];
            for (int i = 0; i < n; i++)
            {
                // Each sample owns a slab reaching halfway to its neighbours, with a little overlap
                // so a point between two rings is never missed by both.
                float prev = i > 0 ? Vector3.Distance(positions[i], positions[i - 1]) : 0f;
                float next = i < n - 1 ? Vector3.Distance(positions[i], positions[i + 1]) : 0f;
                _halfExtents[i] = Mathf.Max(prev, next) * 0.6f + 0.01f;
            }

            var bounds = new Bounds(positions[0], Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                float r = Mathf.Max(widths[i], heights[i]);
                bounds.Encapsulate(positions[i] + Vector3.one * r);
                bounds.Encapsulate(positions[i] - Vector3.one * r);
            }
            LocalBounds = bounds;
        }

        /// <summary>
        /// True when the point is inside the hollow part of the cave. <paramref name="padding"/>
        /// swells the test outwards, which is how the hole puncher makes sure it clears the mouth
        /// rather than grazing it.
        /// </summary>
        public bool Contains(Vector3 localPoint, float padding = 0f)
        {
            for (int i = 0; i < _positions.Length; i++)
            {
                Vector3 d = localPoint - _positions[i];

                // The end samples are capped flush with their mouth plane. Letting them keep a full
                // slab would push the volume out past the opening, and a hole puncher trusting that
                // would strip terrain from in front of the mouth where there is no cave floor to
                // catch anything — a gap you fall through rather than a way in.
                float along = Vector3.Dot(d, _axes[i]);
                float lo = i == 0 ? 0f : -_halfExtents[i];
                float hi = i == _positions.Length - 1 ? 0f : _halfExtents[i];
                if (along < lo || along > hi) continue;

                float u = Vector3.Dot(d, _rights[i]);
                float v = Vector3.Dot(d, _ups[i]);

                float w = _widths[i] + padding;
                float h = _heights[i] + padding;

                float vNorm;
                if (v >= 0f)
                {
                    vNorm = v / h;
                }
                else
                {
                    // Below the springing line the section is either a shallow trough or, at
                    // floorFlatten 1, nothing at all — in which case only the padding reaches down.
                    float lower = _heights[i] * 0.5f * (1f - _floorFlattens[i]) + padding;
                    if (lower < 1e-4f) continue;
                    vNorm = v / lower;
                }

                float uNorm = u / w;
                if (uNorm * uNorm + vNorm * vNorm <= 1f) return true;
            }

            return false;
        }
    }
}
