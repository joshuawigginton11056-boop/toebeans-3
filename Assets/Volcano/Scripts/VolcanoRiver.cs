using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using LavaFlow;

namespace Volcano
{
    /// <summary>
    /// Sits on each river hanging off a volcano and does the two things the Lava Flow generator has
    /// no business knowing about.
    ///
    /// **It remembers the route it was given.** "Add Spillway Rivers" used to overwrite the
    /// waypoints every time it ran, so any route you dragged out across the map was gone the next
    /// time anyone pressed a button on the volcano — which reads exactly like the rivers not being
    /// editable at all. This stores a signature of the route as generated; once the waypoints stop
    /// matching it, the river counts as hand-authored and the volcano leaves the route alone.
    ///
    /// **It builds the wall that keeps karts out of the lava.** The flow mesh is useless as a
    /// collider: it is a ribbon lying about a metre off the ground, so a kart drives up onto it and
    /// straight across. The barrier is a separate invisible wall down each bank, tall enough to stop
    /// one, closed at both ends so nobody drives in from the toe.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Volcano/Volcano River")]
    [RequireComponent(typeof(LavaFlowGenerator))]
    public class VolcanoRiver : MonoBehaviour
    {
        const string BarrierName = "Lava Barrier";

        [Tooltip("Which spillway notch on the volcano this river pours out of.")]
        [SerializeField] int spillwayIndex;

        [Tooltip("Signature of the route as the volcano last generated it. When the waypoints stop " +
                 "matching this, the route counts as yours and the volcano stops rewriting it.")]
        [SerializeField] int routeSignature;

        [Tooltip("Set once the route has been generated at least once. Guards against a fresh " +
                 "component with a zero signature being mistaken for an edited route.")]
        [SerializeField] bool routeCaptured;

        // Rebuild triggers. The barrier has to follow the flow when waypoints are dragged, and the
        // flow does not announce that it re-solved, so watch the shape of the path it produced.
        int _lastStations = -1;
        float _lastLength = -1f;
        Mesh _barrierMesh;

        public int SpillwayIndex { get { return spillwayIndex; } set { spillwayIndex = value; } }

        /// <summary>
        /// True once the waypoints differ from the ones the volcano generated. This is what stops
        /// "Build Everything" throwing away a route that was drawn by hand.
        /// </summary>
        public bool RouteWasEdited
        {
            get
            {
                if (!routeCaptured) return false;
                var flow = GetComponent<LavaFlowGenerator>();
                if (flow == null) return false;
                return RouteSignatureOf(flow.Settings.waypoints) != routeSignature;
            }
        }

        /// <summary>Records the current route as the generated one, so edits from here are noticed.</summary>
        public void CaptureRoute()
        {
            var flow = GetComponent<LavaFlowGenerator>();
            if (flow == null) return;

            routeSignature = RouteSignatureOf(flow.Settings.waypoints);
            routeCaptured = true;
        }

        /// <summary>
        /// A hash of the route, rounded to the centimetre. Rounded because a waypoint that has been
        /// round-tripped through serialisation is not bit-identical to the one that was written, and
        /// a river that decided it had been edited every time the scene reloaded would be worse than
        /// one that never noticed at all.
        /// </summary>
        static int RouteSignatureOf(List<Vector3> waypoints)
        {
            if (waypoints == null) return 0;

            unchecked
            {
                int hash = 17 + waypoints.Count * 31;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    Vector3 p = waypoints[i];
                    hash = hash * 31 + Mathf.RoundToInt(p.x * 100f);
                    hash = hash * 31 + Mathf.RoundToInt(p.y * 100f);
                    hash = hash * 31 + Mathf.RoundToInt(p.z * 100f);
                }
                return hash;
            }
        }

        // ------------------------------------------------------------------ barrier

        void OnEnable()
        {
            // Generated meshes are not serialised, so the collider comes back empty after every
            // scene load and domain reload.
            _lastStations = -1;
#if UNITY_EDITOR
            // Deferred for the same reason the generators defer: this runs while the scene is still
            // being deserialised, and it may have to create the barrier child.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                RebuildBarrierFromVolcano();
            };
#else
            RebuildBarrierFromVolcano();
#endif
        }

        void OnDestroy()
        {
            if (_barrierMesh == null) return;
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(_barrierMesh))
            {
                if (Application.isPlaying) Destroy(_barrierMesh);
                else DestroyImmediate(_barrierMesh);
            }
#else
            Destroy(_barrierMesh);
#endif
            _barrierMesh = null;
        }

        void Update()
        {
            if (Application.isPlaying) return;

            // Dragging a waypoint re-solves the flow, and the wall has to move with it or it ends up
            // fencing off where the river used to be.
            var flow = GetComponent<LavaFlowGenerator>();
            if (flow == null) return;

            FlowPath path = flow.Path;
            if (path == null || !path.IsValid) return;

            if (path.Count == _lastStations && Mathf.Abs(path.Length - _lastLength) < 0.01f) return;
            RebuildBarrierFromVolcano();
        }

        /// <summary>Rebuilds the barrier using the settings on the volcano this river hangs off.</summary>
        public void RebuildBarrierFromVolcano()
        {
            RebuildBarrier(FindSettings());
        }

        VolcanoRiverSettings FindSettings()
        {
            var volcano = GetComponentInParent<VolcanoGenerator>();
            return volcano != null ? volcano.Rivers : new VolcanoRiverSettings();
        }

        /// <summary>
        /// Builds the wall down both banks and hangs it off a child of this river, or clears it when
        /// blocking is off. The mesh is in this object's local space, and the child is left at
        /// identity, so the two agree however the river itself has been placed.
        /// </summary>
        public void RebuildBarrier(VolcanoRiverSettings settings)
        {
            var flow = GetComponent<LavaFlowGenerator>();
            if (flow == null) return;

            Transform child = transform.Find(BarrierName);

            if (settings == null || !settings.blockKarts)
            {
                if (child != null)
                {
                    var off = child.GetComponent<MeshCollider>();
                    if (off != null) off.sharedMesh = null;
                    child.gameObject.SetActive(false);
                }
                _lastStations = -1;
                _lastLength = -1f;
                return;
            }

            FlowPath path = flow.Path;
            if (path == null || !path.IsValid)
            {
                flow.Generate();
                path = flow.Path;
            }
            if (path == null || !path.IsValid) return;

            if (child == null)
            {
                var go = new GameObject(BarrierName);
                go.transform.SetParent(transform, false);
                child = go.transform;
            }

            child.gameObject.SetActive(true);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            var collider = child.GetComponent<MeshCollider>();
            if (collider == null) collider = child.gameObject.AddComponent<MeshCollider>();

            // The build probes the ground outside the wall, and last build's wall is standing in
            // exactly the place it wants to look at.
            collider.enabled = false;

            // A wall is a wall from both sides and it is never something a kart should be able to
            // squeeze inside, so it stays a plain concave mesh rather than a convex hull, which
            // would fill the channel in and let karts drive over the top of the lava.
            collider.convex = false;
            collider.isTrigger = false;

#if UNITY_EDITOR
            bool ownsCurrent = _barrierMesh != null && !UnityEditor.AssetDatabase.Contains(_barrierMesh);
#else
            bool ownsCurrent = _barrierMesh != null;
#endif
            Mesh mesh = ownsCurrent ? _barrierMesh : new Mesh();
            mesh.name = "LavaBarrier_" + name;

            BuildBarrierMesh(mesh, path, settings, transform);

            _barrierMesh = mesh;
            // Reassigning the same mesh instance does not re-cook the collider, so clear it first.
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.enabled = true;

            _lastStations = path.Count;
            _lastLength = path.Length;
        }

        /// <summary>
        /// Two vertical strips, one down each outer edge of the ribbon, capped at the head and the
        /// toe. Every quad is emitted twice with opposite winding: a kart that clips a wall from the
        /// wrong side would otherwise pass straight through it.
        ///
        /// The crest is measured from the ground a kart would be standing on when it hits the wall,
        /// not from the lava's edge. Those are the same thing only where the flow lies in a hollow;
        /// wherever the ground outside is higher — which on a cone is most of the way down — a wall
        /// raised off the lava's edge comes out shorter than it reads, and a sweep found stretches
        /// where its crest was 0.29 m BELOW the ground outside it. That is not a low wall, it is a
        /// ramp into the lava.
        /// </summary>
        static void BuildBarrierMesh(Mesh mesh, FlowPath path, VolcanoRiverSettings s, Transform tr)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            int n = path.Count;
            int outerLeft = 0;
            int outerRight = path.Lateral - 1;

            // World up expressed locally, so the wall stands upright rather than leaning with the
            // hillside. A wall raked back along the slope is one a kart can climb.
            Vector3 up = tr.InverseTransformDirection(Vector3.up);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();

            // Heights are in local units, and the probe measures in world metres.
            float scale = Mathf.Abs(tr.lossyScale.y) > 1e-4f ? Mathf.Abs(tr.lossyScale.y) : 1f;
            float bottom = -s.barrierSink;

            var leftBase = new Vector3[n];
            var rightBase = new Vector3[n];
            var leftTop = new float[n];
            var rightTop = new float[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 right = path.Stations[i].Right;

                // Inset runs towards the middle of the channel: +Right from the left edge, -Right
                // from the right one.
                leftBase[i] = path.Ground[i, outerLeft] + right * s.barrierInset;
                rightBase[i] = path.Ground[i, outerRight] - right * s.barrierInset;

                leftTop[i] = CrestHeight(tr, leftBase[i], -right, s, scale);
                rightTop[i] = CrestHeight(tr, rightBase[i], right, s, scale);
            }

            for (int i = 0; i < n - 1; i++)
            {
                AddWall(verts, tris, leftBase[i], leftBase[i + 1], up, bottom, leftTop[i], leftTop[i + 1]);
                AddWall(verts, tris, rightBase[i], rightBase[i + 1], up, bottom, rightTop[i], rightTop[i + 1]);
            }

            // Caps, so nobody drives in off the end of the river.
            AddWall(verts, tris, leftBase[0], rightBase[0], up, bottom, leftTop[0], rightTop[0]);
            AddWall(verts, tris, leftBase[n - 1], rightBase[n - 1], up, bottom,
                    leftTop[n - 1], rightTop[n - 1]);

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateNormals();
        }

        /// <summary>
        /// How far above a post the wall has to reach, in local units, so it clears the ground
        /// outside by the full barrier height.
        ///
        /// Probed at two distances rather than one: a kart is stopped by the wall from wherever it
        /// happens to be standing, and on a lumpy hillside the metre right against the wall is not
        /// the one that decides whether the crest is reachable.
        /// </summary>
        static float CrestHeight(Transform tr, Vector3 postLocal, Vector3 outwardLocal,
                                 VolcanoRiverSettings s, float scale)
        {
            Vector3 post = tr.TransformPoint(postLocal);
            Vector3 outward = tr.TransformDirection(outwardLocal);
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) return s.barrierHeight;
            outward.Normalize();

            float highest = post.y;
            for (float d = 1.5f; d <= 4.5f; d += 1.5f)
            {
                Vector3 probe = post + outward * d;
                RaycastHit hit;
                if (Physics.Raycast(probe + Vector3.up * 120f, Vector3.down, out hit, 400f))
                    highest = Mathf.Max(highest, hit.point.y);
            }

            return ((highest - post.y) + s.barrierHeight) / scale;
        }

        static void AddWall(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b,
                            Vector3 up, float bottom, float topA, float topB)
        {
            int v = verts.Count;

            verts.Add(a + up * bottom);
            verts.Add(b + up * bottom);
            verts.Add(b + up * topB);
            verts.Add(a + up * topA);

            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);

            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }
    }
}
