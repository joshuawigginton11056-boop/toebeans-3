using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Barriers
{
    /// <summary>Where the line's shape comes from.</summary>
    public enum BarrierPathSource
    {
        /// <summary>Points clicked onto the ground in the scene view.</summary>
        DrawnPoints = 0,

        /// <summary>A Spline Container, so an existing spline can be lined without redrawing it.</summary>
        Spline = 1
    }

    /// <summary>Which rows get built.</summary>
    public enum BarrierSide
    {
        /// <summary>One row, straight down the drawn line.</summary>
        Centre = 0,

        /// <summary>One row, offset to the left of the drawn direction.</summary>
        Left = 1,

        /// <summary>One row, offset to the right of the drawn direction.</summary>
        Right = 2,

        /// <summary>Two rows, one down each side. Draw the middle of the track and get both edges.</summary>
        Both = 3
    }

    public enum BarrierSpacingMode
    {
        /// <summary>A placement every N metres, however long the line is.</summary>
        Distance = 0,

        /// <summary>A fixed number of placements, spread evenly over the line.</summary>
        Count = 1
    }

    /// <summary>Which way each placed object is turned.</summary>
    public enum BarrierFacing
    {
        /// <summary>Forward runs along the line. What fences, rails and walls want.</summary>
        AlongPath = 0,

        /// <summary>Forward points away from the drawn line — outwards, off the edge.</summary>
        FaceOutward = 1,

        /// <summary>Forward points back towards the drawn line.</summary>
        FaceInward = 2,

        /// <summary>Any yaw at all. What rocks, stumps and rubble want.</summary>
        RandomYaw = 3
    }

    public enum BarrierPickMode
    {
        /// <summary>Pick from the list at random, honouring the weights.</summary>
        RandomWeighted = 0,

        /// <summary>Cycle through the list in order. Use this for repeating fence sections.</summary>
        Sequential = 1
    }

    /// <summary>One entry in the prefab list, with how often it comes up.</summary>
    [System.Serializable]
    public class BarrierPrefabEntry
    {
        public GameObject prefab;

        [Tooltip("Relative chance of this one being picked, in Random Weighted mode.")]
        [Min(0f)] public float weight = 1f;
    }

    /// <summary>
    /// Lines the edge of the playable area with objects.
    ///
    /// Click the run out across the hillside, set a spacing, and the prefabs are dropped onto the
    /// ground along it — turned to follow the line, tilted with the slope as much or as little as
    /// you want, and randomised in scale and yaw so a long run does not read as a repeat. Draw the
    /// middle of the track and set Side to Both and you get a row down each edge from one line.
    ///
    /// Everything is rebuilt from <see cref="seed"/>, so the same settings always give the same run
    /// back. Instances are ordinary children — press Detach Instances in the inspector when you want
    /// to start hand-editing them, or the next rebuild will replace them.
    /// </summary>
    [AddComponentMenu("Barriers/Barrier Line")]
    [DisallowMultipleComponent]
    public class BarrierLine : MonoBehaviour
    {
        // ------------------------------------------------------------------ path

        [Header("Path")]
        [Tooltip("Where the shape comes from. Drawn Points is the one you click in the scene view.")]
        public BarrierPathSource pathSource = BarrierPathSource.DrawnPoints;

        [Tooltip("The drawn run. Positions are local to this object; drag them in the scene view.")]
        public List<Vector3> points = new List<Vector3>();

        [Tooltip("Spline mode. The spline the line follows.")]
        public SplineContainer spline;

        [Tooltip("Join the last point back to the first, for a run that rings the whole area.")]
        public bool closedLoop;

        [Tooltip("How much the drawn corners are eased before anything is placed. A hand-clicked " +
                 "run is never as smooth as it looks; this is what stops a barrier line reading as " +
                 "a series of straights.")]
        [Range(0, 12)] public int smoothing = 3;

        [Tooltip("How finely the line is resampled before placing, in metres. Smaller follows the " +
                 "ground and the corners more closely and costs a little more to rebuild.")]
        [Range(0.2f, 4f)] public float sampleSpacing = 0.5f;

        // ------------------------------------------------------------------ ground

        [Header("Ground")]
        [Tooltip("What the objects are snapped down onto.")]
        public BarrierGroundMode groundMode = BarrierGroundMode.Terrain;

        [Tooltip("Terrain mode. Leave empty to use the active terrain in the scene.")]
        public Terrain terrain;

        [Tooltip("Raycast mode. Which layers count as ground. Use this to sit barriers on a " +
                 "generated path or a bridge rather than on the terrain underneath it.")]
        public LayerMask groundLayers = ~0;

        // ------------------------------------------------------------------ what to place

        [Header("What To Place")]
        public List<BarrierPrefabEntry> prefabs = new List<BarrierPrefabEntry>();

        [Tooltip("Random Weighted for scattered rocks and posts; Sequential for repeating sections.")]
        public BarrierPickMode pickMode = BarrierPickMode.RandomWeighted;

        [Tooltip("Change this for a different arrangement from the same settings.")]
        public int seed = 12345;

        // ------------------------------------------------------------------ spacing

        [Header("Spacing")]
        public BarrierSpacingMode spacingMode = BarrierSpacingMode.Distance;

        [Tooltip("Metres between placements. For fence sections that must join up, use Fit Spacing " +
                 "To Prefab in the inspector to read the length straight off the model.")]
        [Min(0.05f)] public float spacing = 4f;

        [Tooltip("Count mode. How many to place along the whole line.")]
        [Min(1)] public int count = 20;

        [Tooltip("How much the gap is allowed to vary, as a fraction. 0 is a dead-even run.")]
        [Range(0f, 0.9f)] public float spacingJitter = 0f;

        [Tooltip("Metres of clear line before the first placement.")]
        [Min(0f)] public float startOffset = 0f;

        [Tooltip("Metres of clear line left at the end.")]
        [Min(0f)] public float endMargin = 0f;

        // ------------------------------------------------------------------ sides

        [Header("Sides")]
        [Tooltip("Both places a row down each side of the drawn line, which is what you want when " +
                 "you have drawn the middle of the track.")]
        public BarrierSide side = BarrierSide.Centre;

        [Tooltip("Metres from the drawn line out to each row. Half the width of the playable area.")]
        public float lateralOffset = 7f;

        [Tooltip("How far each object may wander sideways off its row, in metres.")]
        [Min(0f)] public float lateralJitter = 0f;

        [Tooltip("Stagger the second row by half a gap, so the two sides do not line up.")]
        public bool staggerSides = false;

        // ------------------------------------------------------------------ placement

        [Header("Placement")]
        public BarrierFacing facing = BarrierFacing.AlongPath;

        [Tooltip("How much each object leans with the ground. 0 stands everything upright, which is " +
                 "right for fence posts; 1 lays it flat to the slope, which is right for rocks.")]
        [Range(0f, 1f)] public float alignToGroundNormal = 0f;

        [Tooltip("Degrees of random turn on top of the facing.")]
        [Range(0f, 180f)] public float yawJitter = 0f;

        [Tooltip("Degrees of random lean, for rubble that should not look planted.")]
        [Range(0f, 45f)] public float tiltJitter = 0f;

        [Tooltip("Metres to raise every object after snapping.")]
        public float heightOffset = 0f;

        [Tooltip("Metres to bury every object, so nothing floats on uneven ground.")]
        [Min(0f)] public float sinkDepth = 0.15f;

        [Tooltip("Smallest random scale, as a multiple of the prefab's own.")]
        [Min(0.01f)] public float scaleMin = 1f;

        [Tooltip("Largest random scale, as a multiple of the prefab's own.")]
        [Min(0.01f)] public float scaleMax = 1f;

        [Tooltip("Off lets width, height and depth vary independently. Distorts a model, so it " +
                 "suits rocks and not fences.")]
        public bool uniformScale = true;

        // ------------------------------------------------------------------ filters

        [Header("Skip Rules")]
        [Tooltip("Leave a gap where the ground is steeper than this. Nothing stands on a cliff.")]
        [Range(0f, 90f)] public float maxGroundSlope = 90f;

        [Tooltip("Leave a gap where the ground sampler found nothing — off the terrain, or over a " +
                 "hole. Turn this off to place there anyway.")]
        public bool skipUngrounded = true;

        [Tooltip("Fraction of placements dropped at random, for a broken or ruined run.")]
        [Range(0f, 0.9f)] public float randomSkip = 0f;

        // ------------------------------------------------------------------ blocking wall

        [Header("Blocking Wall")]
        [Tooltip("Sweep an invisible collider along the line as well.\n\n" +
                 "Spaced objects do not close the edge: a kart fits between two rocks. This puts a " +
                 "solid wall behind them that nothing can drive through, with no renderer on it.")]
        public bool buildBlockingWall = false;

        [Tooltip("How far the wall stands above the ground.")]
        [Min(0.1f)] public float wallHeight = 2.5f;

        [Tooltip("How thick the wall is across the line.")]
        [Min(0.05f)] public float wallThickness = 0.4f;

        [Tooltip("How far the wall is buried, so a bumpy surface leaves no gap under it.")]
        [Min(0f)] public float wallEmbed = 1f;

        [Tooltip("Sweep interval for the wall, in metres. Coarser is cheaper and cuts corners more.")]
        [Range(0.5f, 20f)] public float wallSegmentLength = 2f;

        // ------------------------------------------------------------------ output

        [Header("Output")]
        [Tooltip("Child object the instances are parented under.")]
        public string containerName = "Barrier Instances";

        [Tooltip("Mark the placed objects static, so they batch and light like scenery.")]
        public bool markInstancesStatic = true;

        [Tooltip("Rebuild as soon as a value changes in the inspector or a point is dragged. Turn " +
                 "this off on a long run if dragging a slider gets heavy, and use Build Now.")]
        public bool autoRebuild = true;

        // ------------------------------------------------------------------ stats

        [System.NonSerialized] public int LastPlaced;
        [System.NonSerialized] public int LastSkipped;
        [System.NonSerialized] public float LastLength;

#if UNITY_EDITOR
        /// <summary>What a placed barrier is marked as. Everything but navigation, which is
        /// deprecated on the flags enum and would warn on every instance.</summary>
        const UnityEditor.StaticEditorFlags StaticFlags =
            UnityEditor.StaticEditorFlags.ContributeGI |
            UnityEditor.StaticEditorFlags.OccluderStatic |
            UnityEditor.StaticEditorFlags.OccludeeStatic |
            UnityEditor.StaticEditorFlags.BatchingStatic |
            UnityEditor.StaticEditorFlags.ReflectionProbeStatic;
#endif

        // ==================================================================== routes

        /// <summary>The drawn run in world space, whichever source it comes from.</summary>
        public List<Vector3> ControlPointsWorld()
        {
            var pts = new List<Vector3>();

            if (pathSource == BarrierPathSource.Spline)
            {
                if (spline != null && spline.Spline != null && spline.Spline.Count >= 2)
                {
                    const int samples = 512;
                    for (int i = 0; i < samples; i++)
                    {
                        Unity.Mathematics.float3 p = spline.EvaluatePosition(i / (float)(samples - 1));
                        pts.Add(new Vector3(p.x, p.y, p.z));
                    }
                }
                return pts;
            }

            for (int i = 0; i < points.Count; i++) pts.Add(transform.TransformPoint(points[i]));
            return pts;
        }

        public IBarrierGround BuildGroundSampler()
        {
            switch (groundMode)
            {
                case BarrierGroundMode.Terrain:
                    var sampler = new TerrainBarrierGround(ActiveTerrain);
                    // No terrain in the scene at all: a flat preview beats placing nothing.
                    return sampler.IsValid ? (IBarrierGround)sampler
                                           : new FlatBarrierGround(transform.position.y);

                case BarrierGroundMode.Raycast:
                    return new RaycastBarrierGround(groundLayers, 100f, 5000f);

                default:
                    return new FlatBarrierGround(transform.position.y);
            }
        }

        public Terrain ActiveTerrain { get { return terrain != null ? terrain : Terrain.activeTerrain; } }

        /// <summary>Where the ground is under a world point, using this line's ground mode.</summary>
        public bool SampleGroundWorld(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            return BuildGroundSampler().Sample(worldPos, out point, out normal);
        }

        /// <summary>
        /// The rows this line builds: one for Centre, Left or Right, two for Both. Has no side
        /// effects, so the scene view can call it to draw a preview.
        /// </summary>
        public List<BarrierRoute> BuildRoutes()
        {
            var routes = new List<BarrierRoute>();
            List<Vector3> control = ControlPointsWorld();
            if (control.Count < 2) return routes;

            IBarrierGround ground = BuildGroundSampler();

            switch (side)
            {
                case BarrierSide.Centre:
                    routes.Add(BarrierRoute.Build(control, ground, sampleSpacing, smoothing, 0f, closedLoop));
                    break;
                case BarrierSide.Left:
                    routes.Add(BarrierRoute.Build(control, ground, sampleSpacing, smoothing, -Mathf.Abs(lateralOffset), closedLoop));
                    break;
                case BarrierSide.Right:
                    routes.Add(BarrierRoute.Build(control, ground, sampleSpacing, smoothing, Mathf.Abs(lateralOffset), closedLoop));
                    break;
                case BarrierSide.Both:
                    routes.Add(BarrierRoute.Build(control, ground, sampleSpacing, smoothing, -Mathf.Abs(lateralOffset), closedLoop));
                    routes.Add(BarrierRoute.Build(control, ground, sampleSpacing, smoothing, Mathf.Abs(lateralOffset), closedLoop));
                    break;
            }

            return routes;
        }

        /// <summary>
        /// The placements a route works out to, without touching the scene. The scene view draws its
        /// preview from this, and <see cref="Build"/> instantiates from it, so what you see before
        /// pressing the button is what you get after.
        /// </summary>
        public struct Placement
        {
            public Vector3 Position;
            public Vector3 Scale;

            /// <summary>Which entry in <see cref="prefabs"/> goes here, or -1 if the list is empty.</summary>
            public int PrefabIndex;

            /// <summary>Facing, already flattened into the plane the object stands in.</summary>
            public Vector3 Forward;

            /// <summary>Which way is up here, after blending towards the ground normal.</summary>
            public Vector3 Up;

            /// <summary>Extra turn about <see cref="Up"/>, in degrees.</summary>
            public float Yaw;

            /// <summary>Random lean, in degrees about the object's own right and forward.</summary>
            public float TiltX, TiltZ;

            /// <summary>
            /// The rotation these add up to.
            ///
            /// Built on demand rather than stored, because every Quaternion method is a native call:
            /// keeping them out of the solver is what lets the spacing, the skip rules and the
            /// determinism be run and asserted outside the Editor.
            /// </summary>
            public Quaternion Rotation
            {
                get
                {
                    Quaternion rot = Quaternion.LookRotation(Forward, Up);
                    rot = Quaternion.AngleAxis(Yaw, Up) * rot;
                    if (TiltX != 0f || TiltZ != 0f)
                        rot = Quaternion.AngleAxis(TiltX, rot * Vector3.right) *
                              Quaternion.AngleAxis(TiltZ, rot * Vector3.forward) * rot;
                    return rot;
                }
            }
        }

        public List<Placement> SolvePlacements(BarrierRoute route, int routeIndex, out int skipped)
        {
            var list = new List<Placement>();
            skipped = 0;
            if (route == null || !route.IsValid) return list;

            // Each row gets its own stream off the same seed, so turning Both on does not reshuffle
            // the side that was already there.
            var rng = new BarrierRng(seed + routeIndex * 7919);
            _sequentialCursor = 0;

            float usable = route.Length - startOffset - endMargin;
            if (usable <= 0f) return list;

            float step = spacing;
            if (spacingMode == BarrierSpacingMode.Count)
            {
                int n = Mathf.Max(1, count);
                step = closedLoop ? usable / n : (n > 1 ? usable / (n - 1) : usable);
            }
            step = Mathf.Max(0.05f, step);

            float d = startOffset;
            if (staggerSides && routeIndex % 2 == 1) d += step * 0.5f;

            float end = route.Length - endMargin;
            // On a closed loop the last placement would land on top of the first.
            if (closedLoop) end -= step * 0.5f;

            int placedThisRoute = 0;
            int guard = 0;

            while (d <= end + 1e-4f && guard++ < 100000)
            {
                float advance = step;
                if (spacingJitter > 0f)
                {
                    advance = step * rng.Range(1f - spacingJitter, 1f + spacingJitter);
                    advance = Mathf.Max(0.05f, advance);
                }

                if (spacingMode == BarrierSpacingMode.Count && placedThisRoute >= Mathf.Max(1, count)) break;

                Placement p;
                if (TrySolveOne(route, rng, d, out p)) { list.Add(p); placedThisRoute++; }
                else skipped++;

                d += advance;
            }

            return list;
        }

        bool TrySolveOne(BarrierRoute route, BarrierRng rng, float distance, out Placement placement)
        {
            placement = default(Placement);

            BarrierStation st;
            if (!route.SampleAt(distance, out st)) return false;

            Vector3 pos = st.Position;
            Vector3 normal = st.Normal;
            bool grounded = st.Grounded;

            // Every random draw happens whether or not the placement survives, so a skip does not
            // shift the whole rest of the run onto different prefabs.
            float lateral = lateralJitter > 0f ? rng.Range(-lateralJitter, lateralJitter) : 0f;
            float yaw = yawJitter > 0f ? rng.Range(-yawJitter, yawJitter) : 0f;
            float tiltA = tiltJitter > 0f ? rng.Range(-tiltJitter, tiltJitter) : 0f;
            float tiltB = tiltJitter > 0f ? rng.Range(-tiltJitter, tiltJitter) : 0f;
            float randomFacing = rng.Range(0f, 360f);
            float sx = rng.Range(scaleMin, scaleMax);
            float sy = uniformScale ? sx : rng.Range(scaleMin, scaleMax);
            float sz = uniformScale ? sx : rng.Range(scaleMin, scaleMax);
            float skipRoll = rng.Value;
            int prefabIndex = PickPrefab(rng);

            if (randomSkip > 0f && skipRoll < randomSkip) return false;

            if (Mathf.Abs(lateral) > 1e-4f)
            {
                Vector3 moved = pos + st.Right * lateral;
                Vector3 gp, gn;
                if (SampleGroundWorld(moved, out gp, out gn)) { pos = gp; normal = gn; }
                else { pos = moved; grounded = false; }
            }

            if (skipUngrounded && !grounded) return false;
            if (Vector3.Angle(normal, Vector3.up) > maxGroundSlope) return false;

            Vector3 up = BarrierRoute.BlendDirection(Vector3.up, normal, alignToGroundNormal);

            Vector3 forward;
            switch (facing)
            {
                case BarrierFacing.FaceOutward:
                    forward = st.Right * OutwardSign(route);
                    break;
                case BarrierFacing.FaceInward:
                    forward = -st.Right * OutwardSign(route);
                    break;
                case BarrierFacing.RandomYaw:
                    // Turned about world up rather than the object's, so the draw is a plain angle
                    // and no rotation type is needed to work out where it points.
                    float radians = randomFacing * Mathf.Deg2Rad;
                    forward = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                    break;
                default:
                    forward = st.Tangent;
                    break;
            }

            // Flatten the forward into the plane the object stands in, or LookRotation quietly
            // rolls it when the ground is steep.
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;

            placement.Position = pos + Vector3.up * (heightOffset - sinkDepth);
            placement.Forward = forward.normalized;
            placement.Up = up;
            placement.Yaw = yaw;
            placement.TiltX = tiltJitter > 0f ? tiltA : 0f;
            placement.TiltZ = tiltJitter > 0f ? tiltB : 0f;
            placement.Scale = new Vector3(sx, sy, sz);
            // A line with no prefabs yet still solves, so the scene view can show the spacing
            // before anything is dropped into the list.
            placement.PrefabIndex = prefabIndex;
            return true;
        }

        /// <summary>Which way is off the edge. A centre row has no far side, so it faces right.</summary>
        static float OutwardSign(BarrierRoute route)
        {
            return Mathf.Approximately(route.SideSign, 0f) ? 1f : route.SideSign;
        }

        int PickPrefab(BarrierRng rng)
        {
            int usable = 0;
            float total = 0f;
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null || prefabs[i].prefab == null) continue;
                usable++;
                total += Mathf.Max(0f, prefabs[i].weight);
            }
            if (usable == 0) return -1;

            if (pickMode == BarrierPickMode.Sequential)
            {
                int nth = _sequentialCursor++ % usable;
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (prefabs[i] == null || prefabs[i].prefab == null) continue;
                    if (nth-- == 0) return i;
                }
                return -1;
            }

            // Every weight at zero would otherwise never pick anything.
            if (total <= 0f)
            {
                int nth = rng.RangeInt(0, usable);
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (prefabs[i] == null || prefabs[i].prefab == null) continue;
                    if (nth-- == 0) return i;
                }
                return -1;
            }

            float roll = rng.Range(0f, total);
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null || prefabs[i].prefab == null) continue;
                roll -= Mathf.Max(0f, prefabs[i].weight);
                if (roll <= 0f) return i;
            }

            for (int i = prefabs.Count - 1; i >= 0; i--)
                if (prefabs[i] != null && prefabs[i].prefab != null) return i;
            return -1;
        }

        int _sequentialCursor;

        // ==================================================================== building

        /// <summary>Clears the old instances and places the run again.</summary>
        public void Build()
        {
            Transform container = GetOrCreateContainer();
            ClearChildren(container);

            LastPlaced = 0;
            LastSkipped = 0;
            LastLength = 0f;

            List<BarrierRoute> routes = BuildRoutes();
            for (int r = 0; r < routes.Count; r++)
            {
                BarrierRoute route = routes[r];
                if (!route.IsValid) continue;
                LastLength += route.Length;

                int skipped;
                List<Placement> placements = SolvePlacements(route, r, out skipped);
                LastSkipped += skipped;

                for (int i = 0; i < placements.Count; i++)
                {
                    if (Spawn(placements[i], container, r, i)) LastPlaced++;
                }
            }

            BuildWall(routes);
        }

        /// <summary>Removes everything this line has placed.</summary>
        public void ClearInstances()
        {
            Transform container = transform.Find(SafeContainerName);
            if (container != null) DestroySafely(container.gameObject);

            Transform wall = transform.Find(WallObjectName);
            if (wall != null)
            {
                DiscardWallMesh(wall.GetComponent<MeshCollider>());
                DestroySafely(wall.gameObject);
            }

            LastPlaced = 0;
            LastSkipped = 0;
        }

        /// <summary>
        /// Hands the instances over to the scene: they are unparented from this line and it forgets
        /// about them, so they can be hand-edited without the next rebuild throwing the work away.
        /// </summary>
        public GameObject DetachInstances()
        {
            Transform container = transform.Find(SafeContainerName);
            if (container == null) return null;

            container.name = gameObject.name + " Barriers (detached)";
            container.SetParent(transform.parent, true);
            return container.gameObject;
        }

        bool Spawn(Placement p, Transform container, int routeIndex, int ordinal)
        {
            if (p.PrefabIndex < 0 || p.PrefabIndex >= prefabs.Count) return false;
            GameObject prefab = prefabs[p.PrefabIndex].prefab;
            if (prefab == null) return false;

            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Instantiate as a prefab instance rather than a copy, so edits to the source
                // prefab still reach every barrier already placed.
                go = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, container) as GameObject;
            }
#endif
            if (go == null) go = Instantiate(prefab, container);

            go.transform.SetPositionAndRotation(p.Position, p.Rotation);
            go.transform.localScale = Vector3.Scale(prefab.transform.localScale, p.Scale);
            go.name = string.Format("{0}_{1}_{2:D3}", prefab.name, routeIndex == 0 ? "A" : "B", ordinal);

#if UNITY_EDITOR
            if (markInstancesStatic && !Application.isPlaying)
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
#endif
            return true;
        }

        void BuildWall(List<BarrierRoute> routes)
        {
            Transform existing = transform.Find(WallObjectName);

            if (!buildBlockingWall)
            {
                if (existing != null)
                {
                    DiscardWallMesh(existing.GetComponent<MeshCollider>());
                    DestroySafely(existing.gameObject);
                }
                return;
            }

            GameObject wallObject;
            if (existing != null) wallObject = existing.gameObject;
            else
            {
                wallObject = new GameObject(WallObjectName);
                wallObject.transform.SetParent(transform, false);
            }

            var collider = wallObject.GetComponent<MeshCollider>();
            if (collider == null) collider = wallObject.AddComponent<MeshCollider>();
            wallObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            wallObject.transform.localScale = Vector3.one;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Both rows go into one collider. Two MeshColliders would work as well, but one object
            // is one thing to find in the hierarchy and one thing to toggle off while testing.
            for (int r = 0; r < routes.Count; r++)
            {
                BarrierWallBuffer part = BarrierWallBuilder.Build(
                    routes[r], wallHeight, wallThickness, wallEmbed, wallSegmentLength,
                    transform.worldToLocalMatrix);

                if (part.IsEmpty) continue;

                int offset = verts.Count;
                verts.AddRange(part.Vertices);
                for (int i = 0; i < part.Triangles.Count; i++) tris.Add(part.Triangles[i] + offset);
            }

            var combined = new Mesh { name = "BarrierWall" };
            combined.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            combined.SetVertices(verts);
            combined.SetTriangles(tris, 0, true);
            combined.RecalculateNormals();

            // Assigning the same mesh back does not rebuild the collision data, and a wall whose
            // collider is a rebuild behind is one the player drives through.
            DiscardWallMesh(collider);

            if (verts.Count > 0) collider.sharedMesh = combined;
            else DestroySafely(combined);
        }

        /// <summary>
        /// Drops the collider's current mesh and destroys it.
        ///
        /// The mesh is built rather than loaded, so nothing else owns it — and it cannot be tracked
        /// in a field, because a domain reload wipes the field while leaving the mesh on the
        /// collider. Reading it back off the collider is what makes a rebuild after a script
        /// recompile stop leaking one wall per press.
        /// </summary>
        static void DiscardWallMesh(MeshCollider collider)
        {
            if (collider == null) return;

            Mesh old = collider.sharedMesh;
            collider.sharedMesh = null;
            if (old == null) return;

#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(old)) return; // somebody baked it; leave it alone
#endif
            DestroySafely(old);
        }

        // ==================================================================== plumbing

        string SafeContainerName
        {
            get { return string.IsNullOrEmpty(containerName) ? "Barrier Instances" : containerName; }
        }

        const string WallObjectName = "Blocking Wall";

        Transform GetOrCreateContainer()
        {
            Transform container = transform.Find(SafeContainerName);
            if (container != null) return container;

            var go = new GameObject(SafeContainerName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        static void ClearChildren(Transform container)
        {
            while (container.childCount > 0)
                DestroySafely(container.GetChild(0).gameObject);
        }

        static void DestroySafely(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>
        /// Deliberately does not rebuild.
        ///
        /// OnValidate also fires when the scene loads and after every script recompile, and unlike a
        /// procedural mesh these instances are real serialised GameObjects — rebuilding here would
        /// throw away and re-place every barrier in the scene just for opening it, and mark the
        /// scene dirty doing it. The inspector drives rebuilds instead, so only an actual edit
        /// causes one.
        /// </summary>
        void OnValidate()
        {
            if (scaleMax < scaleMin) scaleMax = scaleMin;
        }

        void OnDrawGizmosSelected()
        {
            List<Vector3> control = ControlPointsWorld();
            if (control.Count < 2) return;

            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            for (int i = 0; i < control.Count - 1; i++) Gizmos.DrawLine(control[i], control[i + 1]);
            if (closedLoop) Gizmos.DrawLine(control[control.Count - 1], control[0]);
        }
    }
}
