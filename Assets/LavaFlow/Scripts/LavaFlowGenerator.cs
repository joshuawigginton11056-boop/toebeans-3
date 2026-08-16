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

        [Tooltip("Raycast mode. Objects the flow must not mistake for ground — a bridge spanning " +
                 "the channel, a prop standing in it. The probe sees through these and their " +
                 "children and reads the ground underneath, so the lava passes below them instead " +
                 "of climbing over them.\n\n" +
                 "This flow's own colliders are always ignored; they do not need listing.")]
        [SerializeField] Transform[] groundIgnore = new Transform[0];

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

        [Header("Pool")]
        [Tooltip("The pond this river runs into. Set it and the route ends at the pond's actual " +
                 "shoreline every time it rebuilds, whatever the terrain does under it.\n\n" +
                 "The pond is told about the river too: its rim is notched for the mouth, the " +
                 "crust in front of it stays molten, and its lava scrolls the way the river runs.")]
        [SerializeField] LavaPond.LavaPondGenerator pond;

        [Tooltip("Run the route into the pond. Turn it off to keep the pond's side of the join — " +
                 "the notched rim and the molten mouth — while placing the route by hand.")]
        [SerializeField] bool snapToPond = true;

        [Tooltip("How far the route may reach forward to find the pond, in metres. Left short of " +
                 "this it is carried on to the shore; drawn past it, the overshoot is cut off.")]
        [Range(1f, 120f)] [SerializeField] float pondReach = 30f;

        [Tooltip("How far under the pond's lava the river carries on before it stops, in metres. " +
                 "This is what hides the end of the mesh: the toe finishes inside the pool rather " +
                 "than at the edge of it, so there is no seam to line up and nothing to overlap.")]
        [Range(0.5f, 30f)] [SerializeField] float pondTuck = 5f;

        [Tooltip("Metres before the shoreline over which the river settles into the pool: the " +
                 "banks sink, the channel loses its depth and the surface comes down onto the " +
                 "pond's lava. Too short and the river arrives as a step.")]
        [Range(2f, 80f)] [SerializeField] float pondSettle = 16f;

        [Tooltip("How much wider the channel spreads at the mouth. Lava that has lost its slope " +
                 "has nothing pushing it on and fans out. It can only widen — the river is never " +
                 "allowed to narrow on its way into the pool.")]
        [Range(1f, 3f)] [SerializeField] float pondMouthFlare = 1.35f;

        [Tooltip("Point the pond's lava the way this river runs. A pool fed by a river drifts away " +
                 "from the mouth; without this it scrolls along whatever direction it was left set to.")]
        [SerializeField] bool alignPondFlow = true;

        [Tooltip("Identifies this river's inlet on the pond, so several rivers can feed one pool " +
                 "and each keeps its own. Assigned automatically.")]
        [HideInInspector] [SerializeField] int outfallId;

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
        FlowOutfall _outfall;
        LavaPond.LavaPondGenerator _servedPond;
        Pose3 _pondPose;
        Transform[] _ignoreCache;

        /// <summary>
        /// Which generator holds which outfall id this session. Duplicating a flow copies the id
        /// with everything else, and two rivers writing one inlet would leave the pond opened in
        /// one place for two mouths; a copy takes a fresh id the first time it builds.
        /// </summary>
        static readonly Dictionary<int, LavaFlowGenerator> Claimed = new Dictionary<int, LavaFlowGenerator>();

        public LavaFlowSettings Settings { get { return settings; } }

        /// <summary>The mesh currently on the filter, or null if nothing has been generated yet.</summary>
        public Mesh Mesh { get { return _mesh; } }

        /// <summary>The solved route, in local space. Null until the first generate.</summary>
        public FlowPath Path { get { return _path; } }

        /// <summary>The flow this one carries on from, if any.</summary>
        public LavaFlowGenerator Upstream { get { return upstream; } }

        /// <summary>
        /// Everything the ground probe must see through: the objects named on the inspector, plus
        /// this flow itself. Self is always in the list because a flow that has already generated
        /// owns colliders — its baked mesh, and the barrier walls standing along its banks — and a
        /// probe that lands on last build's wall top would drape the next build over it.
        /// </summary>
        public Transform[] GroundIgnore
        {
            get
            {
                int n = groundIgnore != null ? groundIgnore.Length : 0;
                if (_ignoreCache == null || _ignoreCache.Length != n + 1)
                    _ignoreCache = new Transform[n + 1];

                for (int i = 0; i < n; i++) _ignoreCache[i] = groundIgnore[i];
                _ignoreCache[n] = transform;
                return _ignoreCache;
            }
        }

        /// <summary>The pool this flow runs into, if any.</summary>
        public LavaPond.LavaPondGenerator Pond { get { return pond; } }

        /// <summary>
        /// Where the last solve decided the flow meets the pond, or null when it does not: no pond
        /// set, snapping off, or the route ending too far away for <c>pondReach</c> to bridge.
        /// </summary>
        public FlowOutfall Outfall { get { return _outfall; } }

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

            // Dragging the pond has to drag the river's mouth with it, the same way dragging the
            // flow above does. Without this the join only looks right until the pool is nudged.
            if (PondMoved())
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

        /// <summary>True when the pond has been moved, turned or resized since the last rebuild.</summary>
        bool PondMoved()
        {
            if (pond == null) return false;

            Transform t = pond.transform;
            if (t.position == _pondPose.Position && t.rotation == _pondPose.Rotation &&
                t.lossyScale == _pondPose.Scale) return false;

            _pondPose = new Pose3(t.position, t.rotation, t.lossyScale);
            return true;
        }

        struct Pose3
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public Pose3(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        void OnDestroy()
        {
            // A deleted river must not leave the pond notched for a mouth that is no longer there.
            // Only when it is genuinely being deleted, though: this also runs when the scene is
            // unloaded and when play mode starts, and clearing the inlet then would edit the pond
            // every time anyone pressed play. An unloading scene reports itself as such.
            if (_servedPond != null && !Application.isPlaying && gameObject.scene.isLoaded)
            {
                if (_servedPond.ClearInlet(outfallId)) RebuildPond(_servedPond);
                _servedPond = null;
            }

            if (outfallId != 0)
            {
                LavaFlowGenerator holder;
                if (Claimed.TryGetValue(outfallId, out holder) && holder == this)
                    Claimed.Remove(outfallId);
            }

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

            // Last, because it needs the solved route: the pond is told where the mouth ended up.
            UpdatePondInlet();
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

                // The pond changes the *route*, and it does so here, on the control points, before
                // anything is resampled. See RouteIntoPond for why that matters.
                LavaFlowSettings solveSettings = settings;
                _outfall = RouteIntoPond(ground, ref control, ref solveSettings);

                return LavaFlowPathSolver.Solve(solveSettings, ground, transform.position, transform.forward,
                                                transform.worldToLocalMatrix, control, entryHalfWidth,
                                                _outfall);
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

        // ------------------------------------------------------------------ pond join

        /// <summary>
        /// Ends the route in the pond, by editing the control points the route is drawn from.
        ///
        /// This is the whole design. The obvious way — solve the route, then cut it at the
        /// shoreline and graft a straight run on — puts a kink where the curve meets the graft, and
        /// a kink is read downstream as a hairpin bend: <c>LimitWidthToCurvature</c> exists to stop
        /// a wide ribbon folding through itself on a tight corner, it cannot tell a real corner
        /// from a seam, and it pinches the channel shut in exactly the few metres everyone is
        /// looking at. Cutting the *control points* instead and letting the ordinary resampler draw
        /// through them keeps the last leg a straight continuation of the line the river was
        /// already on, so there is no corner there at all and nothing for the clamp to bite on.
        ///
        /// Returns the outfall, which carries only what the solver still needs — the surface to
        /// settle onto, and how far out to start settling — or null when there is no join.
        /// </summary>
        FlowOutfall RouteIntoPond(IGroundSampler ground, ref List<Vector3> control,
                                  ref LavaFlowSettings solveSettings)
        {
            if (pond == null || !snapToPond) return null;

            // A downhill flow has no control points to edit, so it is walked first and the route it
            // found becomes the control polyline. Taking the walk's own stations keeps its meander
            // and everything else the terrain gave it.
            List<Vector3> route = control;
            bool walked = false;

            if (settings.pathMode == FlowPathMode.Downhill || route == null || route.Count < 2)
            {
                FlowPath probe = LavaFlowPathSolver.Solve(settings, ground, transform.position,
                                                          transform.forward, Matrix4x4.identity, null);
                if (probe == null || !probe.IsValid) return null;

                route = new List<Vector3>(probe.Count);
                for (int i = 0; i < probe.Count; i++) route.Add(probe.Stations[i].Center);
                walked = true;
            }

            Vector3 shorePoint, inward;
            float surfaceY;
            int entryIndex;
            if (!TryFindEntry(route, out shorePoint, out inward, out surfaceY, out entryIndex)) return null;

            // Far enough in to be under the pond's surface, never so far the toe comes out of the
            // far side of a small pool.
            float pondRadius = pond.Settings.radius * pond.WorldScale;
            float tuck = Mathf.Min(pondTuck, pondRadius * 0.8f);

            control = LavaFlowPathSolver.BuildPondEntry(route, entryIndex, shorePoint, inward,
                                                        tuck, settings.stationSpacing, out inward);

            // A walked route is now an authored one: it is being drawn through the points the walk
            // found, plus the entry. Cloned so the component's own settings are never touched.
            if (walked)
            {
                solveSettings = settings.Clone();
                solveSettings.pathMode = FlowPathMode.Waypoints;
            }

            return new FlowOutfall
            {
                ShorePoint = shorePoint,
                Inward = inward,
                SurfaceY = surfaceY,
                Settle = pondSettle,
                MouthFlare = pondMouthFlare
            };
        }

        /// <summary>
        /// Where the drawn route meets the edge of the lava, and the direction it is travelling in
        /// when it gets there. Measured against the route's own last leg so the entry carries on
        /// in the direction the river was already going.
        /// </summary>
        bool TryFindEntry(List<Vector3> route, out Vector3 shorePoint, out Vector3 inward,
                          out float surfaceY, out int entryIndex)
        {
            shorePoint = Vector3.zero;
            inward = Vector3.zero;
            surfaceY = 0f;

            // The first point actually over the lava, or the last point when the route stops short.
            // Containment, not a half-plane: a river that wanders on its way down can be on the
            // pond's side of the shoreline long before it arrives.
            int leg = route.Count - 1;
            for (int i = 1; i < route.Count; i++)
            {
                if (!pond.ContainsWorld(route[i])) continue;
                leg = i;
                break;
            }

            entryIndex = leg;
            Vector3 tail = route[leg];
            Vector3 heading = route[leg] - route[leg - 1];
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f) heading = Flatten(transform.forward);
            if (heading.sqrMagnitude < 1e-6f) return false;
            heading.Normalize();

            Vector3 point;
            if (!pond.TryGetShoreCrossing(tail, heading, out point, out surfaceY)) return false;

            float pondRadius = pond.Settings.radius * pond.WorldScale;

            // Positive when the route stops short of the shore, negative when it runs past. Behind
            // counts as generously as the width of the pool, so a route drawn straight across it
            // still snaps, while a pond left far behind cannot reel the river back into itself.
            Vector3 gapVector = point - tail;
            gapVector.y = 0f;
            float gap = Vector3.Dot(gapVector, heading);
            if (gap > pondReach || -gap > pondReach + pondRadius * 2f) return false;

            shorePoint = point;
            inward = heading;
            return true;
        }

        /// <summary>
        /// Tells the pond where this river arrives, how wide its mouth is and which way it runs, so
        /// the pond can notch its rim, keep the crust off the lava in front of it and scroll the
        /// same way. Rebuilds the pond only when one of those has actually moved.
        /// </summary>
        void UpdatePondInlet()
        {
            // Retargeted or unset since the last rebuild: hand the old pond its shore back.
            if (_servedPond != null && _servedPond != pond)
            {
                if (_servedPond.ClearInlet(OutfallId())) RebuildPond(_servedPond);
                _servedPond = null;
            }

            if (pond == null || _path == null || !_path.IsValid) return;

            Vector3 mouth, heading;
            float halfWidth;
            if (!TryGetMouth(out mouth, out heading, out halfWidth)) return;

            // The fan of open lava in front of the mouth is measured off the river's own width, so
            // a wider river keeps a wider stretch of the pool molten.
            float reach = halfWidth * 2f * Mathf.Max(0f, pond.Settings.inletMeltReach);

            bool changed = pond.SetInlet(OutfallId(), mouth, halfWidth, reach);
            if (alignPondFlow) changed |= pond.SetFlowDirection(heading);

            _servedPond = pond;
            if (changed) RebuildPond(pond);
        }

        /// <summary>Where the river meets the pond, in world space, and how wide it is there.</summary>
        bool TryGetMouth(out Vector3 mouth, out Vector3 heading, out float halfWidth)
        {
            FlowStation end = _path.Stations[_path.Count - 1];
            mouth = transform.TransformPoint(end.Center);
            heading = Flatten(transform.TransformDirection(end.Forward)).normalized;

            Vector3 scale = transform.lossyScale;
            float lateral = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            halfWidth = end.HalfWidth * lateral;

            if (_outfall == null) return true;

            mouth = _outfall.ShorePoint;
            heading = _outfall.Inward;

            // Width at the shoreline rather than at the toe: the toe is metres further in, under
            // the pond's own lava, and by then the mouth has already fanned out.
            for (int i = 0; i < _path.Count; i++)
            {
                Vector3 world = transform.TransformPoint(_path.Stations[i].Center);
                halfWidth = _path.Stations[i].HalfWidth * lateral;
                if (_outfall.DistancePastShore(world) >= 0f) break;
            }
            return true;
        }

        static void RebuildPond(LavaPond.LavaPondGenerator target)
        {
            target.Generate();
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(target);
#endif
        }

        /// <summary>
        /// This river's key on the pond, assigned the first time it is needed. Duplicating a flow
        /// copies the id with everything else, so a copy that finds its id already in use this
        /// session takes a new one rather than fighting the original over one inlet.
        /// </summary>
        int OutfallId()
        {
            LavaFlowGenerator holder;
            bool taken = outfallId != 0 && Claimed.TryGetValue(outfallId, out holder)
                         && holder != null && holder != this;

            if (outfallId == 0 || taken)
            {
                do { outfallId = Random.Range(1, int.MaxValue); }
                while (Claimed.ContainsKey(outfallId));
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            Claimed[outfallId] = this;
            return outfallId;
        }

        /// <summary>What the inspector needs to say about the join, in one shot.</summary>
        public struct PondJoinStatus
        {
            public bool HasPond;
            public bool Snapped;

            /// <summary>Metres from the end of the route to the pond's shore. Positive when the
            /// route stops short, negative when it runs past.</summary>
            public float Gap;

            /// <summary>Width of the river where it arrives, in metres.</summary>
            public float MouthWidth;
        }

        /// <summary>
        /// How the join stands after the last rebuild. A river that is not snapping has to say so
        /// and say why: the failure is otherwise silent, and looks exactly like the feature not
        /// working rather than like the pond being out of reach.
        /// </summary>
        public PondJoinStatus GetPondJoinStatus()
        {
            var status = new PondJoinStatus { HasPond = pond != null };
            if (!status.HasPond || _path == null || !_path.IsValid) return status;

            Vector3 mouth, heading;
            float halfWidth;
            if (TryGetMouth(out mouth, out heading, out halfWidth)) status.MouthWidth = halfWidth * 2f;

            if (_outfall != null)
            {
                status.Snapped = true;
                return status;
            }

            FlowStation end = _path.Stations[_path.Count - 1];
            Vector3 tail = transform.TransformPoint(end.Center);
            Vector3 dir = Flatten(transform.TransformDirection(end.Forward));
            if (dir.sqrMagnitude < 1e-6f) return status;

            Vector3 shorePoint;
            float surfaceY;
            if (pond.TryGetShoreCrossing(tail, dir.normalized, out shorePoint, out surfaceY))
                status.Gap = Vector3.Dot(Flatten(shorePoint - tail), dir.normalized);
            else
                status.Gap = Vector3.Distance(Flatten(tail), Flatten(pond.transform.position));

            return status;
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
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
                    return new RaycastGround(groundLayers, 50f, 4000f, GroundIgnore);

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
