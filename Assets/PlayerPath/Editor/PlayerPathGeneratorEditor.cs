using System.Collections.Generic;
using CaveTunnel;
using UnityEditor;
using UnityEngine;

namespace PlayerPath.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="PlayerPathGenerator"/>: live stats, the regenerate pair, a bake
    /// button, and the two things that make the route workable — clicking it onto the hillside in
    /// the scene view, and dragging the points afterwards.
    /// </summary>
    [CustomEditor(typeof(PlayerPathGenerator))]
    [CanEditMultipleObjects]
    public class PlayerPathGeneratorEditor : UnityEditor.Editor
    {
        /// <summary>
        /// While on, clicking the ground in the scene view extends the route.
        ///
        /// Static because drawing is a mode rather than a per-inspector setting, and because
        /// creating a path from the menu turns it on: a new path has no route yet, so the only
        /// useful thing to be doing with it is clicking one onto the ground.
        /// </summary>
        static bool _appendMode;

        /// <summary>
        /// Whether the start of the route has been put somewhere deliberately yet.
        ///
        /// The first click places the start and the rest append to it, but the start is the object's
        /// own transform rather than a waypoint — so an empty route cannot tell "nobody has clicked
        /// yet" from "the start has just been placed" by counting points. Without this the first
        /// branch below matches forever: every click moves the start again, no point is ever added,
        /// and the tool looks like it is ignoring the mouse.
        /// </summary>
        static bool _startPlaced;

        /// <summary>
        /// Which cave to trace a route from with Set Waypoints From Cave. Static so the choice
        /// persists across selection changes, same reasoning as <see cref="_appendMode"/> above —
        /// there is no per-route field for it because the copy is one-off, not a live link.
        /// </summary>
        static CaveTunnelGenerator _caveSource;

        /// <summary>Puts the scene view into route-drawing mode. Called when a path is created.</summary>
        public static void BeginDrawing()
        {
            _appendMode = true;
            _startPlaced = false;
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            var generator = (PlayerPathGenerator)target;

            // Actions and warnings go above the settings rather than below them. The Settings
            // foldout is long enough on its own, and with a Mesh Renderer expanded above it the
            // buttons end up several screens down, where nobody finds them.
            DrawColliderWarning(generator);
            DrawStats(generator);
            DrawActions(generator);

            EditorGUILayout.Space();
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 deck, 1 edge brick, 2 trim, 3 glow. Assign four materials " +
                "on the Mesh Renderer in that order, and swap the deck and edge ones freely — that " +
                "is what they are separated for.\n\n" +
                "Slot 3 is the heat under the path: the joints between the flagstones, the seam at " +
                "the foot of each wall and the bricks that have not finished cooling. Give it an " +
                "emissive material, or turn Glowing Joints off if you want the path cold.",
                MessageType.None);
        }

        void DrawActions(PlayerPathGenerator generator)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate"))
                {
                    foreach (Object t in targets) ((PlayerPathGenerator)t).Generate();
                }

                if (GUILayout.Button("Randomise Seed"))
                {
                    foreach (Object t in targets)
                    {
                        var g = (PlayerPathGenerator)t;
                        Undo.RecordObject(g, "Randomise Player Path");
                        g.Randomize();
                        EditorUtility.SetDirty(g);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                if (GUILayout.Button("Start A New Route Here"))
                {
                    Undo.RecordObject(generator, "Start Player Path Route");
                    generator.Settings.waypoints = new List<Vector3>();
                    generator.Settings.routeMode = PathRouteMode.Waypoints;
                    EditorUtility.SetDirty(generator);
                    generator.Generate();

                    _appendMode = true;
                    _startPlaced = false;
                    SceneView.RepaintAll();
                }

                bool wantAppend = GUILayout.Toggle(
                    _appendMode,
                    _appendMode ? "Drawing Route — click the ground (press again to stop)"
                                : "Draw Route In Scene View",
                    "Button");

                if (wantAppend != _appendMode)
                {
                    _appendMode = wantAppend;
                    if (_appendMode)
                    {
                        generator.Settings.routeMode = PathRouteMode.Waypoints;
                        // Resuming a route that already has points carries on from the end; an empty
                        // one is still waiting for its start to be put down.
                        _startPlaced = generator.Settings.waypoints.Count > 0;
                    }
                    SceneView.RepaintAll();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _caveSource = (CaveTunnelGenerator)EditorGUILayout.ObjectField(
                        "Cave Source", _caveSource, typeof(CaveTunnelGenerator), true);

                    using (new EditorGUI.DisabledScope(_caveSource == null))
                    {
                        if (GUILayout.Button("Set Waypoints From Cave", GUILayout.Width(180)))
                            SetWaypointsFromCave(generator, _caveSource);
                    }
                }

                if (generator.Settings.routeMode == PathRouteMode.Waypoints)
                {
                    if (generator.Settings.waypoints.Count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "A route with no points builds nothing. Press Draw Route In Scene View " +
                            "and click along the ground where the path should run.",
                            MessageType.Warning);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Add Point At End")) AddWaypoint(generator);
                        if (GUILayout.Button("Reverse Direction")) ReverseRoute(generator);
                    }
                }

                using (new EditorGUI.DisabledScope(generator.Mesh == null))
                {
                    if (GUILayout.Button("Flatten Terrain Under Path"))
                        PathTerrainCarver.Carve(generator);

                    if (GUILayout.Button("Rebuild Edge Torches"))
                        PathTorchBaker.Rebuild(generator);

                    if (GUILayout.Button("Save Mesh Asset..."))
                        SaveMeshAsset(generator);
                }
            }

            if (generator.Mesh == null)
            {
                EditorGUILayout.HelpBox(
                    "Some actions stay greyed out until the path has built a mesh. Press " +
                    "Regenerate, and check the ground mode if nothing appears.", MessageType.Info);
            }
        }

        /// <summary>
        /// The one mistake that costs an evening: a path with no collider. It looks finished from
        /// every angle, and the player walks straight through it into the mountain.
        /// </summary>
        static void DrawColliderWarning(PlayerPathGenerator generator)
        {
            if (!generator.WantsCollider) return;
            if (generator.GetComponent<MeshCollider>() != null) return;

            EditorGUILayout.HelpBox(
                "There is no Mesh Collider on this object, so nothing can stand on the path.",
                MessageType.Warning);

            if (!GUILayout.Button("Add Mesh Collider")) return;

            var collider = Undo.AddComponent<MeshCollider>(generator.gameObject);
            collider.sharedMesh = generator.Mesh;
        }

        static void DrawStats(PlayerPathGenerator generator)
        {
            Mesh mesh = generator.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                tris += (int)(mesh.GetIndexCount(i) / 3);

            PathRoute route = generator.Route;
            if (route == null || !route.IsValid)
            {
                EditorGUILayout.LabelField("Triangles", tris.ToString("N0"));
                EditorGUILayout.LabelField("Route", "not solved");
                return;
            }

            // Per metre rather than in total: the total only says how long the path is, and the
            // brickwork is dense enough that a long path adds up quickly.
            EditorGUILayout.LabelField("Triangles", string.Format("{0:N0}  ({1:F0} per metre)",
                                                                  tris, tris / Mathf.Max(1f, route.Length)));
            EditorGUILayout.LabelField("Vertices", mesh.vertexCount.ToString("N0"));

            float drop = route.Stations[0].Center.y - route.Stations[route.Count - 1].Center.y;
            EditorGUILayout.LabelField("Route", string.Format("{0:F0} m long, {1:F0} m of drop",
                                                              route.Length, drop));

            int steps = generator.CountSteps();
            float tallest = generator.TallestRiser();
            EditorGUILayout.LabelField("Steps", steps == 0
                ? "none — the deck ramps the whole way"
                : string.Format("{0:N0}, tallest {1:F2} m", steps, tallest));

            PathSettings s = generator.Settings;
            if (steps > 0 && tallest > s.stepRise * 2.2f)
            {
                EditorGUILayout.HelpBox(string.Format(
                    "Some steps are {0:F2} m tall, which is more than the player can walk up.\n\n" +
                    "A step can only start at a cross-section, so the shortest tread the path can " +
                    "build is one Station Spacing ({1:F2} m) — and where the hill falls faster than " +
                    "that, the risers grow instead. Drop Station Spacing to about {2:F2} to fix it, " +
                    "or route the path across the slope instead of straight down it.",
                    tallest, s.stationSpacing,
                    Mathf.Max(0.3f, s.stationSpacing * s.stepRise * 2f / tallest)),
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Appends a point carrying on in the direction the route was already heading, so a new
        /// point lands somewhere useful rather than on top of the last one.
        /// </summary>
        static void AddWaypoint(PlayerPathGenerator generator)
        {
            List<Vector3> pts = generator.Settings.waypoints;

            Vector3 last = pts.Count > 0 ? pts[pts.Count - 1] : Vector3.zero;
            Vector3 previous = pts.Count > 1 ? pts[pts.Count - 2] : Vector3.zero;
            Vector3 heading = pts.Count > 1
                ? (last - previous)
                : generator.transform.InverseTransformDirection(generator.transform.forward);
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;

            Undo.RecordObject(generator, "Add Player Path Point");
            pts.Add(last + heading.normalized * 8f);
            EditorUtility.SetDirty(generator);
            generator.Generate();
        }

        /// <summary>
        /// Turns the path round. The route runs from the object's own position, so reversing means
        /// moving the object to the far end and rewriting every point — which is exactly why it is
        /// a button rather than something to do by hand.
        /// </summary>
        static void ReverseRoute(PlayerPathGenerator generator)
        {
            List<Vector3> pts = generator.Settings.waypoints;
            if (pts.Count == 0) return;

            Transform tr = generator.transform;

            var world = new List<Vector3>(pts.Count + 1);
            world.Add(tr.position);
            for (int i = 0; i < pts.Count; i++) world.Add(tr.TransformPoint(pts[i]));
            world.Reverse();

            Undo.RecordObject(generator, "Reverse Player Path");
            Undo.RecordObject(tr, "Reverse Player Path");

            tr.position = world[0];
            tr.hasChanged = false;

            var flipped = new List<Vector3>(world.Count - 1);
            for (int i = 1; i < world.Count; i++) flipped.Add(tr.InverseTransformPoint(world[i]));

            generator.Settings.waypoints = flipped;
            EditorUtility.SetDirty(generator);
            generator.Generate();
        }

        /// <summary>
        /// Traces the route through a cave onto this path: the object moves onto the cave's first
        /// node and the rest of the nodes become waypoints, in order. A cave node's position is
        /// already the middle of its floor (see <see cref="CaveNode"/>), so this is a straight
        /// copy rather than anything that needs draping — the route through a cave should follow
        /// the cave's own floor, not whatever a Terrain or Raycast sampler would say about it.
        ///
        /// It is a one-off copy, not a live link: reshaping the cave afterwards needs another
        /// press to pick the change up. That is deliberate — it is what keeps the cave's own nodes
        /// as the one place the shape is authored, instead of two lists that can drift apart.
        /// </summary>
        static void SetWaypointsFromCave(PlayerPathGenerator generator, CaveTunnelGenerator cave)
        {
            List<CaveNode> nodes = cave.Nodes;
            if (nodes == null || nodes.Count < 2)
            {
                EditorUtility.DisplayDialog("Player Path",
                    "The selected cave has fewer than two nodes, so there is no route to trace.",
                    "OK");
                return;
            }

            var world = new List<Vector3>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                world.Add(cave.transform.TransformPoint(nodes[i].position));

            Transform tr = generator.transform;
            Undo.RecordObject(tr, "Set Player Path Start From Cave");
            Undo.RecordObject(generator, "Set Player Path Waypoints From Cave");

            tr.position = world[0];
            tr.hasChanged = false;

            var local = new List<Vector3>(world.Count - 1);
            for (int i = 1; i < world.Count; i++)
                local.Add(tr.InverseTransformPoint(world[i]));

            generator.Settings.waypoints = local;
            generator.Settings.routeMode = PathRouteMode.Waypoints;
            EditorUtility.SetDirty(generator);
            generator.Generate();

            Debug.Log("Player Path: traced " + nodes.Count + " points from " + cave.name + ".",
                      generator);
        }

        static void SaveMeshAsset(PlayerPathGenerator generator)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Player Path Mesh", generator.Mesh.name, "asset",
                "Bake the current path into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(generator.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved player path mesh to " + path, copy);
        }

        // ------------------------------------------------------------------ scene view

        void OnSceneGUI()
        {
            var generator = (PlayerPathGenerator)target;
            PathSettings s = generator.Settings;
            if (s.routeMode != PathRouteMode.Waypoints) return;

            Transform tr = generator.transform;

            // The start is the first point of the route, but it is the object's own transform and
            // is moved with the normal move tool, so it gets a marker rather than a handle.
            var world = new List<Vector3>(s.waypoints.Count + 1);
            world.Add(tr.position);
            for (int i = 0; i < s.waypoints.Count; i++)
                world.Add(tr.TransformPoint(s.waypoints[i]));

            DrawRouteLine(world);

            bool changed = false;
            changed |= DrawPointHandles(generator, tr, s, world);
            changed |= DrawInsertHandles(generator, tr, s, world);
            if (_appendMode) changed |= HandleAppendClicks(generator, tr, s);

            DrawLegend(s);

            if (changed)
            {
                EditorUtility.SetDirty(generator);
                generator.Generate();
            }
        }

        static void DrawRouteLine(List<Vector3> world)
        {
            if (world.Count < 2) return;

            Handles.color = new Color(0.95f, 0.8f, 0.35f, 0.9f);
            Handles.DrawAAPolyLine(4f, world.ToArray());

            Handles.color = new Color(0.2f, 0.9f, 0.4f, 0.95f);
            Handles.SphereHandleCap(0, world[0], Quaternion.identity,
                                    HandleUtility.GetHandleSize(world[0]) * 0.14f, EventType.Repaint);
            Handles.Label(world[0], "  start");
        }

        /// <summary>Move handles on each point, plus a delete button while Shift is held.</summary>
        bool DrawPointHandles(PlayerPathGenerator generator, Transform tr, PathSettings s,
                              List<Vector3> world)
        {
            bool changed = false;
            bool deleting = Event.current.shift;

            for (int i = 0; i < s.waypoints.Count; i++)
            {
                Vector3 point = world[i + 1];
                float size = HandleUtility.GetHandleSize(point);

                if (deleting)
                {
                    Handles.color = new Color(1f, 0.25f, 0.2f, 0.95f);
                    if (Handles.Button(point, Quaternion.identity, size * 0.13f, size * 0.2f,
                                       Handles.SphereHandleCap))
                    {
                        Undo.RecordObject(generator, "Delete Player Path Point");
                        s.waypoints.RemoveAt(i);
                        return true; // the list changed under us; redraw before touching it again
                    }

                    Handles.Label(point, "  remove " + (i + 1));
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(generator, "Move Player Path Point");
                    s.waypoints[i] = tr.InverseTransformPoint(Drape(generator, moved));
                    changed = true;
                }

                Handles.color = new Color(0.95f, 0.75f, 0.2f, 0.9f);
                Handles.SphereHandleCap(0, point, Quaternion.identity, size * 0.1f, EventType.Repaint);
                Handles.Label(point, "  " + (i + 1));
            }

            return changed;
        }

        /// <summary>
        /// A small dot halfway along each leg. Clicking it puts a new point there, which is how a
        /// straight run gets turned into a bend without redrawing the whole route.
        /// </summary>
        bool DrawInsertHandles(PlayerPathGenerator generator, Transform tr, PathSettings s,
                               List<Vector3> world)
        {
            if (Event.current.shift) return false; // deleting takes over the dots

            for (int leg = 0; leg < world.Count - 1; leg++)
            {
                Vector3 mid = (world[leg] + world[leg + 1]) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid);

                Handles.color = new Color(1f, 0.9f, 0.5f, 0.8f);
                if (!Handles.Button(mid, Quaternion.identity, size * 0.055f, size * 0.11f,
                                    Handles.DotHandleCap)) continue;

                Undo.RecordObject(generator, "Insert Player Path Point");
                // Leg 0 runs from the start, so it inserts at the head of the list.
                s.waypoints.Insert(leg, tr.InverseTransformPoint(Drape(generator, mid)));
                return true;
            }

            return false;
        }

        /// <summary>Click anywhere on the ground to extend the route while drawing is on.</summary>
        bool HandleAppendClicks(PlayerPathGenerator generator, Transform tr, PathSettings s)
        {
            // Take the default control so a click lands here rather than selecting whatever is
            // under the cursor.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return false;

            Vector3 hit;
            if (!TryPickGround(generator, e.mousePosition, out hit)) return false;

            // The first click on an empty route puts the start there rather than adding a point.
            // The start is the object's own position, so without this the path begins wherever the
            // object happened to be sitting and runs back to the first click. It is a one-off:
            // testing the point count instead would match on every later click too, because placing
            // the start does not add one.
            if (!_startPlaced && s.waypoints.Count == 0)
            {
                Undo.RecordObject(tr, "Place Player Path Start");
                tr.position = hit;
                tr.hasChanged = false;
                _startPlaced = true;
                e.Use();
                return true;
            }

            Undo.RecordObject(generator, "Append Player Path Point");
            s.waypoints.Add(tr.InverseTransformPoint(hit));
            e.Use();
            return true;
        }

        /// <summary>
        /// Where the cursor is pointing on the ground. Tries colliders first, then falls back to the
        /// path's own ground sampler — a terrain with no collider still answers that one.
        /// </summary>
        static bool TryPickGround(PlayerPathGenerator generator, Vector2 mousePosition, out Vector3 hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            RaycastHit info;
            if (Physics.Raycast(ray, out info, 100000f, ~0, QueryTriggerInteraction.Ignore))
            {
                hit = info.point;
                return true;
            }

            var plane = new Plane(Vector3.up, generator.transform.position);
            float distance;
            if (!plane.Raycast(ray, out distance))
            {
                hit = Vector3.zero;
                return false;
            }

            hit = Drape(generator, ray.GetPoint(distance));
            return true;
        }

        /// <summary>Drops a point onto the ground so the handles sit where the path will.</summary>
        static Vector3 Drape(PlayerPathGenerator generator, Vector3 worldPoint)
        {
            Vector3 ground, normal;
            if (!generator.SampleGroundWorld(worldPoint, out ground, out normal)) return worldPoint;
            return ground;
        }

        void DrawLegend(PathSettings s)
        {
            Handles.BeginGUI();
            var rect = new Rect(10f, 10f, 280f, _appendMode ? 78f : 62f);
            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.Label(s.waypoints.Count + " points", EditorStyles.boldLabel);
            GUILayout.Label("Click a small dot to insert a point");
            GUILayout.Label("Hold Shift and click a point to delete it");
            if (_appendMode)
            {
                GUILayout.Label(!_startPlaced && s.waypoints.Count == 0
                    ? "Click the ground to place the start"
                    : "Click the ground to add to the end");
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }

    /// <summary>
    /// Cuts a shelf into the terrain under the path.
    ///
    /// A path laid across a slope is level across its width, because that is what a built path is.
    /// The hillside is not, so the uphill side of the deck is inside the mountain and the terrain
    /// pokes up through the paving. Lowering the ground under the footprint is the fix, and doing it
    /// by hand with the terrain brush along a switchback is an afternoon.
    /// </summary>
    public static class PathTerrainCarver
    {
        /// <summary>How far past the wall the shelf blends back into the untouched hillside.</summary>
        const float Falloff = 3f;

        public static void Carve(PlayerPathGenerator generator)
        {
            PathRoute route = generator.Route;
            if (route == null || !route.IsValid)
            {
                EditorUtility.DisplayDialog("Player Path", "There is no path to carve under yet.", "OK");
                return;
            }

            Terrain terrain = generator.ActiveTerrain;
            if (terrain == null || terrain.terrainData == null)
            {
                EditorUtility.DisplayDialog("Player Path",
                    "No terrain found. Assign one in the Ground section first.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Flatten Terrain Under Path",
                    "This edits the terrain heightmap under the path, lowering the hillside to just " +
                    "below the deck and blending back out over " + Falloff + " m.\n\n" +
                    "It changes the terrain asset, not just the scene. Undo puts it back.",
                    "Flatten", "Cancel")) return;

            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Flatten Terrain Under Path");

            int res = data.heightmapResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            // Heightmap cells are square in world units, one every size/(res-1) metres.
            float cellX = size.x / (res - 1);
            float cellZ = size.z / (res - 1);

            Transform tr = generator.transform;
            int n = route.Count;

            // Everything the path could possibly touch, in cell indices.
            float reach = 0f;
            for (int i = 0; i < n; i++)
                reach = Mathf.Max(reach, route.Stations[i].HalfWidth);
            reach += generator.Settings.wallThickness + generator.Settings.seamWidth + Falloff + 1f;

            int minX = res, maxX = -1, minZ = res, maxZ = -1;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = tr.TransformPoint(route.Stations[i].Center);
                minX = Mathf.Min(minX, Mathf.FloorToInt((p.x - reach - origin.x) / cellX));
                maxX = Mathf.Max(maxX, Mathf.CeilToInt((p.x + reach - origin.x) / cellX));
                minZ = Mathf.Min(minZ, Mathf.FloorToInt((p.z - reach - origin.z) / cellZ));
                maxZ = Mathf.Max(maxZ, Mathf.CeilToInt((p.z + reach - origin.z) / cellZ));
            }

            minX = Mathf.Clamp(minX, 0, res - 1);
            maxX = Mathf.Clamp(maxX, 0, res - 1);
            minZ = Mathf.Clamp(minZ, 0, res - 1);
            maxZ = Mathf.Clamp(maxZ, 0, res - 1);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width < 2 || height < 2)
            {
                EditorUtility.DisplayDialog("Player Path",
                    "The path does not overlap this terrain. Check that the right terrain is " +
                    "assigned in the Ground section.", "OK");
                return;
            }

            float[,] heights = data.GetHeights(minX, minZ, width, height);
            var weight = new float[height, width];
            var target = new float[height, width];

            // How far below the deck the ground should sit: under the paving and its joints, with a
            // little more so the terrain never grazes back through the stones.
            float sink = generator.Settings.jointDepth + generator.Settings.surfaceLift + 0.15f;

            for (int k = 0; k < n - 1; k++)
            {
                Vector3 a = tr.TransformPoint(route.Stations[k].Center);
                Vector3 b = tr.TransformPoint(route.Stations[k + 1].Center);

                float band = route.Stations[k].HalfWidth + generator.Settings.seamWidth +
                             generator.Settings.wallThickness;
                float outer = band + Falloff;

                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - outer - origin.x) / cellX), minX, maxX);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + outer - origin.x) / cellX), minX, maxX);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, b.z) - outer - origin.z) / cellZ), minZ, maxZ);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z, b.z) + outer - origin.z) / cellZ), minZ, maxZ);

                for (int z = z0; z <= z1; z++)
                {
                    float worldZ = origin.z + z * cellZ;
                    for (int x = x0; x <= x1; x++)
                    {
                        float worldX = origin.x + x * cellX;

                        float t;
                        float distance = DistanceToSegment(worldX, worldZ, a, b, out t);
                        if (distance > outer) continue;

                        float w = distance <= band
                            ? 1f
                            : Mathf.SmoothStep(1f, 0f, (distance - band) / Falloff);

                        int lz = z - minZ;
                        int lx = x - minX;
                        if (w <= weight[lz, lx]) continue;

                        weight[lz, lx] = w;
                        target[lz, lx] = Mathf.Lerp(a.y, b.y, t) - sink;
                    }
                }
            }

            int touched = 0;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float w = weight[z, x];
                    if (w <= 0f) continue;

                    float normalized = Mathf.Clamp01((target[z, x] - origin.y) / Mathf.Max(0.01f, size.y));
                    heights[z, x] = Mathf.Lerp(heights[z, x], normalized, w);
                    touched++;
                }
            }

            data.SetHeights(minX, minZ, heights);
            EditorUtility.SetDirty(data);

            Debug.Log(string.Format(
                "Player Path: flattened {0:N0} heightmap cells under the path. The terrain here is " +
                "{1:F1} m per cell, so anything finer than that cannot be carved.",
                touched, Mathf.Max(cellX, cellZ)), generator);
        }

        /// <summary>Horizontal distance from a point to a segment, and how far along it landed.</summary>
        static float DistanceToSegment(float x, float z, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            float lengthSq = dx * dx + dz * dz;

            t = lengthSq > 1e-6f ? Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / lengthSq) : 0f;

            float px = a.x + dx * t - x;
            float pz = a.z + dz * t - z;
            return Mathf.Sqrt(px * px + pz * pz);
        }
    }

    /// <summary>
    /// Hangs a line of low warm lights along the edges of the path. Emissive materials do not light
    /// anything in URP unless the scene is baked, so without these the glow under the paving reads
    /// as a decal painted on the mountain rather than as something the player is walking over.
    /// </summary>
    public static class PathTorchBaker
    {
        const string HolderName = "Path Lights";

        public static void Rebuild(PlayerPathGenerator generator)
        {
            Transform existing = generator.transform.Find(HolderName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var positions = new List<Vector3>();
            var grades = new List<float>();
            var widths = new List<float>();
            generator.SampleCentreline(7f, positions, grades, widths);
            if (positions.Count == 0)
            {
                Debug.LogWarning("Player Path: no route to place lights along yet.", generator);
                return;
            }

            var holder = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(holder, "Rebuild Player Path Lights");
            holder.transform.SetParent(generator.transform, false);
            holder.transform.localPosition = Vector3.zero;

            for (int i = 0; i < positions.Count; i++)
            {
                var go = new GameObject("Path Glow " + (i + 1));
                go.transform.SetParent(holder.transform, true);
                // Just above the deck: the light is coming out of the joints, not off a lamp post.
                go.transform.position = positions[i] + Vector3.up * 0.35f;

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.5f, 0.18f);
                light.range = Mathf.Max(4f, widths[i] * 2.4f);
                light.intensity = 1.6f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForceVertex;
            }

            Debug.Log("Player Path: placed " + positions.Count + " glow lights.", holder);
        }
    }

    /// <summary>
    /// Adds the path to the GameObject creation menu with its materials already wired up. The
    /// materials are written on first use rather than shipped, so the set always matches whichever
    /// render pipeline the project is actually on.
    /// </summary>
    public static class PlayerPathMenu
    {
        const string RootFolder = "Assets/PlayerPath";
        const string MaterialFolder = RootFolder + "/Materials";

        [MenuItem("GameObject/3D Object/Player Path (Low Poly)", false, 15)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Player Path");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var generator = go.AddComponent<PlayerPathGenerator>();
            go.AddComponent<MeshCollider>();

            renderer.sharedMaterials = EnsureMaterials();

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            PlaceInView(go, generator);

            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;

            // A new path has no route, so it builds nothing and there is nothing to see. Rather
            // than leave an empty object selected and let it read as the menu item having failed,
            // go straight into drawing: the next click on the ground starts the path.
            PlayerPathGeneratorEditor.BeginDrawing();

            Debug.Log("Player Path: click along the ground in the Scene view to draw the route. " +
                      "The first click places the start. Press the Draw Route button in the " +
                      "Inspector when you are finished.", go);
        }

        /// <summary>
        /// Drops the new path onto the ground in the middle of what the scene view is looking at.
        ///
        /// A GameObject made in code lands at the world origin, which on a terrain of this size is
        /// underneath the mountain and out of shot — so the path is created, is selected, and is
        /// nowhere the user can see it.
        /// </summary>
        static void PlaceInView(GameObject go, PlayerPathGenerator generator)
        {
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 point = view != null ? view.pivot : Vector3.zero;

            Vector3 ground, normal;
            if (generator.SampleGroundWorld(point, out ground, out normal)) point = ground;

            go.transform.position = point;
            go.transform.hasChanged = false;
        }

        /// <summary>Loads the four submesh materials, creating any that are not there yet.</summary>
        static Material[] EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "PlayerPath");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");

            var materials = new Material[4];
            materials[0] = LoadLit("PP_Deck", new Color(0.20f, 0.19f, 0.20f), Color.black, 0.16f);
            materials[1] = LoadLit("PP_Edge", new Color(0.19f, 0.11f, 0.10f), Color.black, 0.2f);
            materials[2] = LoadLit("PP_Trim", new Color(0.13f, 0.125f, 0.13f), Color.black, 0.1f);
            // Emission runs the red channel over 1 so the glow blooms, while green and blue stay
            // well under it. Push more than one channel past 1 and they both clip to full, which
            // turns the glow yellow and then white however orange the base colour is.
            materials[3] = LoadLit("PP_Glow", new Color(0.55f, 0.16f, 0.03f),
                                   new Color(2.4f, 0.55f, 0.06f), 0.3f);
            return materials;
        }

        static Material LoadLit(string name, Color baseColor, Color emission, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", emission);
                // Deliberately kept out of global illumination: the glow lights on the inspector are
                // the controllable way to light the scene from this.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
