using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace PlayerPath
{
    /// <summary>
    /// Lays a walkable path along the ground under this GameObject.
    ///
    /// Click the route out across the hillside and it drapes itself onto the terrain: paved deck,
    /// a low brick edge down both sides to stop the player walking off, and stairs wherever the
    /// ground is too steep to walk up. Drag any point afterwards and the whole path re-solves — it
    /// is one ribbon with an editable centreline, not a row of pieces butted together.
    ///
    /// The mesh is built procedurally rather than shipped as a model, so it rebuilds from the seed
    /// whenever the scene loads and every tweak in the inspector is a live preview. Use "Save Mesh
    /// Asset" on the inspector to bake one down if you would rather ship static geometry.
    ///
    /// The renderer expects four materials, in submesh order: deck, edge brick, trim, glow.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Player Path/Player Path Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlayerPathGenerator : MonoBehaviour
    {
        [SerializeField] PathSettings settings = new PathSettings();

        [Header("Ground")]
        [Tooltip("What the path is laid over.")]
        [SerializeField] PathGroundMode groundMode = PathGroundMode.Terrain;

        [Tooltip("Terrain mode. Leave empty to use the active terrain in the scene.")]
        [SerializeField] Terrain terrain;

        [Tooltip("Raycast mode. Which layers count as ground.")]
        [SerializeField] LayerMask groundLayers = ~0;

        [Header("Path source")]
        [Tooltip("Spline mode. The spline the path follows.")]
        [SerializeField] SplineContainer spline;

        [Header("Output")]
        [Tooltip("Push the generated mesh onto a MeshCollider on this object. The player falls " +
                 "straight through the path without one.")]
        [SerializeField] bool updateCollider = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        PathRoute _route;

        public PathSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>The solved route, in local space. Null until the first generate.</summary>
        public PathRoute Route { get { return _route; } }

        public bool WantsCollider { get { return updateCollider; } }

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

        void Update()
        {
            // The route starts where this object stands, so dragging it has to re-solve. OnValidate
            // does not fire for transform edits, which would otherwise leave the path behind while
            // the handle moved.
            if (!liveUpdate || Application.isPlaying) return;
            if (!transform.hasChanged) return;

            transform.hasChanged = false;
            Generate();
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

        /// <summary>Rebuilds the path and assigns it to this object's filter and collider.</summary>
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
            target.name = "PlayerPath_" + settings.seed;

            _route = SolveRoute();
            Fill(target, settings, _route);

            _mesh = target;
            filter.sharedMesh = target;

            if (updateCollider)
            {
                var collider = GetComponent<MeshCollider>();
                if (collider != null)
                {
                    // Assigning the same mesh back does not rebuild the collision data, and a path
                    // whose collider is a rebuild behind is one the player walks through.
                    collider.sharedMesh = null;
                    // An unrouted path builds an empty mesh, and handing that to a MeshCollider is
                    // an error every rebuild. Leave the collider empty until there is a route.
                    if (target.vertexCount > 0) collider.sharedMesh = target;
                }
            }
        }

        /// <summary>Rolls a new seed and rebuilds.</summary>
        public void Randomize()
        {
            settings.seed = Random.Range(int.MinValue, int.MaxValue);
            Generate();
        }

        /// <summary>Solves the route without building geometry. Used by the editor tools.</summary>
        public PathRoute SolveRoute()
        {
            IPathGround ground = BuildGroundSampler();
            List<Vector3> control = BuildControlPoints();

            return PathRouteSolver.Solve(settings, ground, transform.position,
                                         transform.worldToLocalMatrix, control);
        }

        /// <summary>
        /// Ground point and normal under a world position, using whatever ground mode this path is
        /// set to. The scene-view tools drape their handles with it.
        /// </summary>
        public bool SampleGroundWorld(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            return BuildGroundSampler().Sample(worldPos, out point, out normal);
        }

        public IPathGround BuildGroundSampler()
        {
            switch (groundMode)
            {
                case PathGroundMode.Terrain:
                    Terrain t = ActiveTerrain;
                    var sampler = new TerrainPathGround(t);
                    // No terrain in the scene at all: better a flat preview than an empty mesh.
                    return sampler.IsValid ? (IPathGround)sampler : new FlatPathGround(transform.position.y);

                case PathGroundMode.Raycast:
                    return new RaycastPathGround(groundLayers, 50f, 4000f);

                default:
                    return new FlatPathGround(transform.position.y);
            }
        }

        /// <summary>The terrain this path is laid on, whether it was assigned or found.</summary>
        public Terrain ActiveTerrain
        {
            get { return terrain != null ? terrain : Terrain.activeTerrain; }
        }

        List<Vector3> BuildControlPoints()
        {
            var pts = new List<Vector3>();

            if (settings.routeMode == PathRouteMode.Waypoints)
            {
                // The first point is always this object, so the path starts where it stands.
                pts.Add(transform.position);
                for (int i = 0; i < settings.waypoints.Count; i++)
                    pts.Add(transform.TransformPoint(settings.waypoints[i]));
            }
            else if (settings.routeMode == PathRouteMode.Spline && spline != null)
            {
                pts.AddRange(PathSplineSource.Sample(spline, 1024));
            }

            return pts;
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(PathSettings settings, PathRoute route)
        {
            var mesh = new Mesh();
            mesh.name = "PlayerPath_" + (settings != null ? settings.seed : 0);
            Fill(mesh, settings, route);
            return mesh;
        }

        static void Fill(Mesh mesh, PathSettings settings, PathRoute route)
        {
            PathMeshBuffer buf = PathMeshBuilder.Build(settings, route);

            mesh.Clear();
            // A long path passes the 16-bit vertex limit easily, so widen the index buffer.
            mesh.indexFormat = buf.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(buf.Vertices);
            mesh.SetNormals(buf.Normals);
            mesh.SetUVs(0, buf.UVs);
            mesh.SetUVs(1, buf.UV1);
            mesh.SetColors(buf.Colors);

            mesh.subMeshCount = buf.Submeshes.Length;
            for (int i = 0; i < buf.Submeshes.Length; i++)
                mesh.SetTriangles(buf.Submeshes[i], i, false);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        // ------------------------------------------------------------------ gameplay hooks

        /// <summary>
        /// World-space points down the middle of the deck, one every
        /// <paramref name="spacingMetres"/>, each with how steep the path is there and how wide.
        /// This is what torches, spawn points, patrol routes and trigger volumes want placing from.
        /// </summary>
        public void SampleCentreline(float spacingMetres, List<Vector3> positions,
                                     List<float> grades, List<float> widths)
        {
            if (positions != null) positions.Clear();
            if (grades != null) grades.Clear();
            if (widths != null) widths.Clear();
            if (_route == null || !_route.IsValid) return;

            float step = Mathf.Max(0.5f, spacingMetres);
            float next = 0f;

            for (int i = 0; i < _route.Count; i++)
            {
                PathStation st = _route.Stations[i];
                if (st.Distance < next && i != _route.Count - 1) continue;
                next = st.Distance + step;

                if (positions != null) positions.Add(transform.TransformPoint(st.Center));
                if (grades != null) grades.Add(st.Grade);
                if (widths != null) widths.Add(st.HalfWidth * 2f);
            }
        }

        /// <summary>How many steps the path breaks into, for the inspector's read-out.</summary>
        public int CountSteps()
        {
            if (_route == null || !_route.IsValid) return 0;

            int steps = 0;
            for (int i = 0; i < _route.Count; i++)
                if (Mathf.Abs(_route.Stations[i].Riser) > 1e-4f) steps++;
            return steps;
        }

        /// <summary>
        /// The tallest step on the path, in metres.
        ///
        /// A riser can only land on a cross-section, so the shortest tread the path can build is one
        /// station. Where the hill falls faster than one rise per station, the steps have to get
        /// taller instead — and past about twice the authored rise the player can no longer walk up
        /// them. The inspector watches this number for exactly that reason.
        /// </summary>
        public float TallestRiser()
        {
            if (_route == null || !_route.IsValid) return 0f;

            float tallest = 0f;
            for (int i = 0; i < _route.Count; i++)
                tallest = Mathf.Max(tallest, Mathf.Abs(_route.Stations[i].Riser));
            return tallest;
        }

        void OnDrawGizmosSelected()
        {
            if (_route == null || !_route.IsValid) return;

            // The route, coloured by how steep the ground under it is.
            for (int i = 0; i < _route.Count - 1; i++)
            {
                Vector3 a = transform.TransformPoint(_route.Stations[i].Center);
                Vector3 b = transform.TransformPoint(_route.Stations[i + 1].Center);
                float steep = Mathf.Clamp01(_route.Stations[i].Grade / 35f);
                Gizmos.color = Color.Lerp(new Color(0.4f, 0.85f, 1f, 0.9f),
                                          new Color(1f, 0.45f, 0.2f, 0.9f), steep);
                Gizmos.DrawLine(a, b);
            }
        }
    }

    /// <summary>
    /// Reads control points off a Spline Container. Kept in its own file's worth of code because it
    /// is the only thing in the package that depends on com.unity.splines.
    /// </summary>
    public static class PathSplineSource
    {
        /// <summary>World-space points along the spline, evenly spaced in spline parameter.</summary>
        public static List<Vector3> Sample(SplineContainer container, int samples)
        {
            var pts = new List<Vector3>();
            if (container == null || container.Spline == null || container.Spline.Count < 2) return pts;

            samples = Mathf.Clamp(samples, 2, 4096);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                Unity.Mathematics.float3 p = container.EvaluatePosition(t);
                pts.Add(new Vector3(p.x, p.y, p.z));
            }
            return pts;
        }
    }
}
