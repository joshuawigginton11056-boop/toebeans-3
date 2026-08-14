using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace LavaFlow
{
    /// <summary>
    /// Pours a generated lava flow down the ground under this GameObject.
    ///
    /// Put this where the lava comes out, point it downhill, and it walks the terrain to find its
    /// own route: narrow, fast and molten while the ground is steep, then broadening into a
    /// crusted, meandering river once it reaches the flat. Switch the path mode to Waypoints if you
    /// want to trace an exact route instead; the points are draggable in the scene view.
    ///
    /// The mesh is built procedurally rather than shipped as a model, so it rebuilds from the seed
    /// whenever the scene loads and every tweak in the inspector is a live preview. Use "Save Mesh
    /// Asset" on the inspector to bake one down if you would rather ship static geometry.
    ///
    /// The renderer expects four materials, in submesh order: dark crust, warm crust, molten lava,
    /// rock. Slot 2 is the one that wants the scrolling lava shader.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Lava Flow/Lava Flow Generator")]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class LavaFlowGenerator : MonoBehaviour
    {
        [SerializeField] LavaFlowSettings settings = new LavaFlowSettings();

        [Header("Ground")]
        [Tooltip("What the flow is poured over.")]
        [SerializeField] GroundMode groundMode = GroundMode.Terrain;

        [Tooltip("Terrain mode. Leave empty to use the active terrain in the scene.")]
        [SerializeField] Terrain terrain;

        [Tooltip("Raycast mode. Which layers count as ground.")]
        [SerializeField] LayerMask groundLayers = ~0;

        [Header("Path source")]
        [Tooltip("Spline mode. The spline the flow follows.")]
        [SerializeField] SplineContainer spline;

        [Header("Chaining")]
        [Tooltip("The flow this one carries on from. Set it and this flow moves itself onto that " +
                 "one's toe, leaves at the same heading, and starts at exactly the width the lava " +
                 "arrived at, so the two read as one river rather than two meshes pushed together.")]
        [SerializeField] LavaFlowGenerator upstream;

        [Tooltip("Move this object onto the upstream flow's toe on every rebuild. Turn it off to " +
                 "keep the width and crust continuity but place the join by hand.")]
        [SerializeField] bool snapToUpstream = true;

        [Header("Output")]
        [Tooltip("Push the generated mesh onto a MeshCollider on this object, if there is one.")]
        [SerializeField] bool updateCollider = false;

        [Tooltip("Rebuild as soon as a value changes in the inspector.")]
        [SerializeField] bool liveUpdate = true;

        Mesh _mesh;
        FlowPath _path;
        bool _solving;
        Vector3 _lastJoin;
        float _lastJoinWidth = -1f;

        public LavaFlowSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>The solved route, in local space. Null until the first generate.</summary>
        public FlowPath Path { get { return _path; } }

        /// <summary>The flow this one carries on from, if any.</summary>
        public LavaFlowGenerator Upstream { get { return upstream; } }

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
            // The route is solved from where this object stands, so dragging the source has to
            // re-solve it. OnValidate does not fire for transform edits, which would otherwise
            // leave the flow behind while the handle moved.
            if (!liveUpdate || Application.isPlaying) return;

            // Retuning a flow moves its toe, and everything chained below it has to follow.
            if (UpstreamJoinMoved())
            {
                Generate();
                return;
            }

            if (!transform.hasChanged) return;

            transform.hasChanged = false;
            Generate();
        }

        /// <summary>True when the flow above has been rebuilt somewhere other than where it was.</summary>
        bool UpstreamJoinMoved()
        {
            if (upstream == null || upstream == this) return false;

            Vector3 point, heading;
            float halfWidth;
            if (!upstream.TryGetToe(out point, out heading, out halfWidth)) return false;

            if ((point - _lastJoin).sqrMagnitude < 1e-6f &&
                Mathf.Abs(halfWidth - _lastJoinWidth) < 1e-4f) return false;

            _lastJoin = point;
            _lastJoinWidth = halfWidth;
            return true;
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

        /// <summary>Rebuilds the flow and assigns it to this object's filter and collider.</summary>
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
            target.name = "LavaFlow_" + settings.seed;

            _path = SolvePath();
            Fill(target, settings, _path);

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

        /// <summary>Solves the route without building geometry. Used by the editor tools.</summary>
        public FlowPath SolvePath()
        {
            // Two flows set as each other's upstream would otherwise chase each other forever.
            if (_solving) return _path;
            _solving = true;

            try
            {
                float entryHalfWidth = SnapToUpstream();

                IGroundSampler ground = BuildGroundSampler();
                List<Vector3> control = BuildControlPoints();

                return LavaFlowPathSolver.Solve(settings, ground, transform.position, transform.forward,
                                                transform.worldToLocalMatrix, control, entryHalfWidth);
            }
            finally
            {
                _solving = false;
            }
        }

        /// <summary>World-space point and arrival half width where this flow hands over to the next.</summary>
        public bool TryGetToe(out Vector3 point, out Vector3 heading, out float halfWidth)
        {
            point = transform.position;
            heading = transform.forward;
            halfWidth = 0f;
            if (_path == null || !_path.IsValid) return false;

            FlowStation end = _path.Stations[_path.Count - 1];
            point = transform.TransformPoint(end.Center);
            heading = transform.TransformDirection(end.Forward);

            Vector3 scale = transform.lossyScale;
            halfWidth = end.HalfWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return true;
        }

        /// <summary>
        /// Moves this flow onto the end of the one it continues, and reports the half width the
        /// lava arrives at so the join has no step in it. Returns 0 when there is no upstream flow.
        /// </summary>
        float SnapToUpstream()
        {
            if (upstream == null || upstream == this) return 0f;

            // Read the upstream route rather than asking it to rebuild: it may not have generated
            // yet, and going through Generate would recurse back down a chain of flows.
            FlowPath up = upstream.Path;
            if (up == null || !up.IsValid) up = upstream.SolvePath();
            if (up == null || !up.IsValid) return 0f;

            FlowStation end = up.Stations[up.Count - 1];
            Transform ut = upstream.transform;

            Vector3 joinPoint = ut.TransformPoint(end.Center);
            Vector3 joinHeading = ut.TransformDirection(end.Forward);

            // Width travels through the join in world units, so a scaled upstream flow still hands
            // over the width it actually looks.
            Vector3 scale = ut.lossyScale;
            float halfWidth = end.HalfWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

            if (snapToUpstream)
            {
                // Only write the transform when it has actually moved. Assigning it unconditionally
                // sets hasChanged every rebuild, and Update would then rebuild forever.
                if ((transform.position - joinPoint).sqrMagnitude > 1e-6f)
                    transform.position = joinPoint;

                // Rotation is only the starting heading, and only Downhill mode uses it. In the
                // other modes it would swing the authored route, which are local coordinates.
                if (settings.pathMode == FlowPathMode.Downhill && joinHeading.sqrMagnitude > 1e-6f)
                {
                    var wanted = Quaternion.LookRotation(joinHeading, Vector3.up);
                    if (Quaternion.Angle(transform.rotation, wanted) > 0.05f)
                        transform.rotation = wanted;
                }
            }

            // Back into this object's own scale, which is what the solver works in.
            Vector3 mine = transform.lossyScale;
            float myScale = Mathf.Max(Mathf.Abs(mine.x), Mathf.Abs(mine.z));
            return myScale > 1e-4f ? halfWidth / myScale : halfWidth;
        }

        /// <summary>
        /// Ground point and normal under a world position, using whatever ground mode this flow is
        /// set to. The scene-view path tools drape their handles with it.
        /// </summary>
        public bool SampleGroundWorld(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            return BuildGroundSampler().Sample(worldPos, out point, out normal);
        }

        IGroundSampler BuildGroundSampler()
        {
            switch (groundMode)
            {
                case GroundMode.Terrain:
                    Terrain t = terrain != null ? terrain : Terrain.activeTerrain;
                    var sampler = new TerrainGround(t);
                    // No terrain in the scene at all: better a flat preview than an empty mesh.
                    return sampler.IsValid ? (IGroundSampler)sampler : new FlatGround(transform.position.y);

                case GroundMode.Raycast:
                    return new RaycastGround(groundLayers, 50f, 4000f);

                default:
                    return new FlatGround(transform.position.y);
            }
        }

        List<Vector3> BuildControlPoints()
        {
            var pts = new List<Vector3>();

            if (settings.pathMode == FlowPathMode.Waypoints)
            {
                // The first point is always this object, so the flow starts where the source is.
                pts.Add(transform.position);
                for (int i = 0; i < settings.waypoints.Count; i++)
                    pts.Add(transform.TransformPoint(settings.waypoints[i]));
            }
            else if (settings.pathMode == FlowPathMode.Spline && spline != null)
            {
                int samples = Mathf.Clamp(
                    Mathf.CeilToInt(settings.maxLength / Mathf.Max(0.2f, settings.stationSpacing)) * 3,
                    16, 4096);
                pts.AddRange(LavaFlowSplineSource.Sample(spline, samples));
            }

            return pts;
        }

        /// <summary>Builds a standalone mesh, for baking to an asset or for pooling at runtime.</summary>
        public static Mesh Create(LavaFlowSettings settings, FlowPath path)
        {
            var mesh = new Mesh();
            mesh.name = "LavaFlow_" + (settings != null ? settings.seed : 0);
            Fill(mesh, settings, path);
            return mesh;
        }

        static void Fill(Mesh mesh, LavaFlowSettings settings, FlowPath path)
        {
            MeshBuffer buf = LavaFlowMeshBuilder.Build(settings, path);

            mesh.Clear();
            // A long flow passes the 16-bit vertex limit easily, so widen the index buffer.
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
        /// World-space points down the middle of the channel, one every
        /// <paramref name="spacingMetres"/>, each with how steep and how wide the flow is there.
        /// This is what particle systems, point lights and damage volumes want to be placed from.
        /// </summary>
        public void SampleCentreline(float spacingMetres, List<Vector3> positions,
                                     List<float> slopes, List<float> widths)
        {
            if (positions != null) positions.Clear();
            if (slopes != null) slopes.Clear();
            if (widths != null) widths.Clear();
            if (_path == null || !_path.IsValid) return;

            float step = Mathf.Max(0.5f, spacingMetres);
            float next = 0f;

            for (int i = 0; i < _path.Count; i++)
            {
                FlowStation st = _path.Stations[i];
                if (st.Distance < next && i != _path.Count - 1) continue;
                next = st.Distance + step;

                if (positions != null) positions.Add(transform.TransformPoint(st.Center));
                if (slopes != null) slopes.Add(st.SlopeNorm);
                if (widths != null) widths.Add(st.HalfWidth * 2f);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (_path == null || !_path.IsValid) return;

            // The route, coloured by how fast the lava is moving along it.
            for (int i = 0; i < _path.Count - 1; i++)
            {
                Vector3 a = transform.TransformPoint(_path.Stations[i].Center);
                Vector3 b = transform.TransformPoint(_path.Stations[i + 1].Center);
                float slope = _path.Stations[i].SlopeNorm;
                Gizmos.color = Color.Lerp(new Color(1f, 0.35f, 0.05f, 0.9f),
                                          new Color(1f, 0.95f, 0.6f, 0.9f), slope);
                Gizmos.DrawLine(a, b);
            }

            // The toe: where a flow chained below this one will attach itself.
            Vector3 toe, heading;
            float halfWidth;
            if (TryGetToe(out toe, out heading, out halfWidth))
            {
                Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.8f);
                Gizmos.DrawWireSphere(toe, Mathf.Max(0.4f, halfWidth));
            }
        }
    }
}
