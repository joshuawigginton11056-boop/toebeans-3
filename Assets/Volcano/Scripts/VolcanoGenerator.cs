using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Volcano
{
    /// <summary>
    /// Drops a generated low-poly volcano onto this GameObject's MeshFilter.
    ///
    /// The mesh is built procedurally rather than shipped as a model, so it rebuilds from the seed
    /// whenever the scene loads and every tweak in the inspector is a live preview. Use "Save Mesh
    /// Asset" on the inspector to bake one down if you would rather ship static geometry.
    ///
    /// The renderer expects four materials, in submesh order: rock, ash, ember, molten. Only the
    /// molten slot needs to be emissive.
    ///
    /// The object's own position is the middle of the foot of the cone, standing on the ground, so
    /// "Snap To Ground" means something and so the passage floor lines up with the surrounding
    /// terrain without anyone having to work out an offset.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Volcano/Volcano Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class VolcanoGenerator : MonoBehaviour
    {
        [SerializeField] VolcanoSettings settings = new VolcanoSettings();

        [Tooltip("The rivers that pour out of the spillway notches: how far they run out across the " +
                 "map, how smooth and how fast they look, and whether they block karts.\n\n" +
                 "Nothing here changes the mountain. Press \"Add Spillway Rivers\" after editing it.")]
        [SerializeField] VolcanoRiverSettings rivers = new VolcanoRiverSettings();

        [Header("Output")]
        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one. The " +
                 "mountain is what the track drives through, so this normally wants to be on.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        VolcanoShape _shape;

        public VolcanoSettings Settings { get { return settings; } }

        /// <summary>Settings for the rivers hanging off the spillways. Never shapes the cone.</summary>
        public VolcanoRiverSettings Rivers
        {
            get
            {
                if (rivers == null) rivers = new VolcanoRiverSettings();
                return rivers;
            }
        }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>
        /// The maths behind the current mesh. Rebuilt with it, so anything reading this after a
        /// settings change is looking at the shape that is actually on screen.
        /// </summary>
        public VolcanoShape Shape
        {
            get
            {
                if (_shape == null) _shape = new VolcanoShape(settings);
                return _shape;
            }
        }

        void OnEnable()
        {
            // Procedural meshes are not serialised with the scene, so rebuild after every load,
            // domain reload and play-mode transition.
            if (_mesh == null) Generate();
        }

        void OnValidate()
        {
            if (!liveUpdate) return;
#if UNITY_EDITOR
            // OnValidate runs during serialisation; defer so we are not touching objects mid-import.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Generate();
            };
#else
            Generate();
#endif
        }

        void OnDestroy()
        {
            // Only ever destroy the instance we made ourselves; a baked asset must survive.
            if (_mesh == null) return;
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(_mesh))
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
#else
            Destroy(_mesh);
#endif
            _mesh = null;
        }

        /// <summary>Rebuilds the volcano and assigns it to this object's filter and collider.</summary>
        public void Generate()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null) return;

#if UNITY_EDITOR
            bool ownsCurrent = _mesh != null && !UnityEditor.AssetDatabase.Contains(_mesh);
#else
            bool ownsCurrent = _mesh != null;
#endif
            // Refill the mesh we already own rather than leaking a new one on every keystroke.
            Mesh target = ownsCurrent ? _mesh : new Mesh();
            target.name = "Volcano_" + settings.seed;

            _shape = new VolcanoShape(settings);
            Fill(target, settings, _shape);

            _mesh = target;
            filter.sharedMesh = target;

            if (updateCollider)
            {
                var collider = GetComponent<MeshCollider>();
                if (collider != null) collider.sharedMesh = target;
            }
        }

        /// <summary>Rolls a new seed and rebuilds.</summary>
        public void Randomize()
        {
            settings.seed = Random.Range(int.MinValue, int.MaxValue);
            Generate();
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(VolcanoSettings settings)
        {
            var mesh = new Mesh();
            mesh.name = "Volcano_" + (settings != null ? settings.seed : 0);
            Fill(mesh, settings, new VolcanoShape(settings));
            return mesh;
        }

        static void Fill(Mesh mesh, VolcanoSettings settings, VolcanoShape shape)
        {
            VolcanoMeshBuffer buf = VolcanoMeshBuilder.Build(settings, shape);

            mesh.Clear();
            // A mountain this size passes the 16-bit vertex limit easily, so widen the index buffer.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);
            mesh.SetColors(buf.Colors);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
                mesh.SetTriangles(buf.Submeshes[i], i, false);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        // ------------------------------------------------------------------ gameplay hooks

        /// <summary>
        /// World-space centre and radius of the lava standing in the crater. This is where a Lava
        /// Pond wants to be placed, and where the smoke plume comes out of.
        /// </summary>
        public bool TryGetCraterLava(out Vector3 center, out float radius)
        {
            center = transform.TransformPoint(new Vector3(0f, settings.LavaLevel, 0f));
            radius = PoolRadius() * UniformScale();
            return radius > 0.01f;
        }

        /// <summary>
        /// How wide the lava pool is at the level it stands at, in local units. Solved off the
        /// crater profile rather than guessed, so it stays right when the crater is retuned.
        /// </summary>
        public float PoolRadius()
        {
            float lip = settings.CraterLipRadius;
            float level = settings.LavaLevel;

            float lo = 0f;
            float hi = lip;
            if (Shape.HeightPolar(0f, 0f) >= level) return 0f;

            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Shape.HeightPolar(mid, 0f) < level) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// World-space route down a spillway channel, from inside the pool out to
        /// <paramref name="outerRadius"/>. Hand this to a Lava Flow generator as its waypoints and
        /// the river is guaranteed to sit in the channel that was cut for it.
        /// </summary>
        public List<Vector3> SpillwayRouteWorld(int index, float outerRadius, float spacing)
        {
            var local = Shape.SpillwayRoute(index, outerRadius, spacing);
            for (int i = 0; i < local.Count; i++) local[i] = transform.TransformPoint(local[i]);
            return local;
        }

        /// <summary>
        /// Where a track drives into the passage: the middle of the floor at each mouth, with the
        /// direction pointing out of the mountain. Index 0 and 1 are the two ends.
        /// </summary>
        public bool TryGetPortalWorld(int index, out Vector3 floorCentre, out Vector3 outward)
        {
            Vector3 localPoint, localDir;
            floorCentre = transform.position;
            outward = transform.forward;

            if (!Shape.TryGetPortal(index, out localPoint, out localDir)) return false;

            floorCentre = transform.TransformPoint(localPoint);
            outward = transform.TransformDirection(localDir).normalized;
            return true;
        }

        float UniformScale()
        {
            Vector3 s = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        void OnDrawGizmosSelected()
        {
            VolcanoShape shape = Shape;

            // The lava level, so it is obvious whether the notches are actually below it.
            Vector3 center;
            float radius;
            if (TryGetCraterLava(out center, out radius) && radius > 0.01f)
            {
                Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
                DrawCircle(center, radius, transform.up);
            }

            // Every spillway channel, from the pool to past the foot.
            Gizmos.color = new Color(1f, 0.62f, 0.18f, 0.85f);
            for (int i = 0; i < shape.SpillwayCount; i++)
            {
                List<Vector3> route = SpillwayRouteWorld(i, shape.OuterRadius, 6f);
                for (int k = 0; k < route.Count - 1; k++) Gizmos.DrawLine(route[k], route[k + 1]);
            }

            // The passage, drawn along the middle of its floor so it is clear where a track would go.
            if (shape.HasPassage)
            {
                Vector3 a, b, da, db;
                if (TryGetPortalWorld(0, out a, out da) && TryGetPortalWorld(1, out b, out db))
                {
                    Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
                    Gizmos.DrawLine(a, b);
                    Gizmos.DrawWireSphere(a, settings.boreWidth * 0.5f);
                    Gizmos.DrawWireSphere(b, settings.boreWidth * 0.5f);
                    Gizmos.DrawLine(a, a + da * 12f);
                    Gizmos.DrawLine(b, b + db * 12f);
                }
            }
        }

        void DrawCircle(Vector3 center, float radius, Vector3 up)
        {
            Vector3 a = Vector3.Cross(up, Vector3.right);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(up, Vector3.forward);
            a = a.normalized * radius;
            Vector3 b = Vector3.Cross(up.normalized, a);

            const int steps = 32;
            Vector3 prev = center + a;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps * Mathf.PI * 2f;
                Vector3 p = center + a * Mathf.Cos(t) + b * Mathf.Sin(t);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
