using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LavaFlow.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="LavaFlowGenerator"/>: live stats, the regenerate pair, a bake
    /// button, and the two things that make the route workable — converting a downhill run into
    /// waypoints you can then hand-edit, and dragging those waypoints in the scene view.
    /// </summary>
    [CustomEditor(typeof(LavaFlowGenerator))]
    [CanEditMultipleObjects]
    public class LavaFlowGeneratorEditor : UnityEditor.Editor
    {
        /// <summary>While on, clicking the ground in the scene view extends the route.</summary>
        bool _appendMode;

        public override void OnInspectorGUI()
        {
            var generator = (LavaFlowGenerator)target;

            // Actions and warnings go above the settings rather than below them. The Settings
            // foldout is long enough on its own, and with a Mesh Renderer expanded above it the
            // buttons end up several screens down, where nobody finds them.
            DrawMaterialWarnings(generator);
            DrawStats(generator);
            DrawActions(generator);

            EditorGUILayout.Space();

            // Several settings only steer the automatic walk, and staring at Max Length wondering
            // why a hand-drawn route is being cut short is a bad half hour.
            if (generator.Settings.pathMode != FlowPathMode.Downhill)
            {
                EditorGUILayout.HelpBox(
                    "This route is drawn, not found, so it is exactly as long as you made it. " +
                    "Max Length, Momentum, Wander, Flat Slope Angle, River Run Length and Meander " +
                    "only apply to Downhill routing and do nothing here.",
                    MessageType.Info);
            }

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 dark crust, 1 warm crust, 2 molten lava, 3 rock. Assign " +
                "four materials on the Mesh Renderer in that order. Slot 2 is the one that wants " +
                "the scrolling LavaFlow/Molten Lava shader; the rest are ordinary rock.\n\n" +
                "That shader needs UV Mode left on Flow Aligned. It is the only mode that records " +
                "which way downstream is, and without it the lava will scroll sideways.",
                MessageType.None);
        }

        void DrawActions(LavaFlowGenerator generator)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate"))
                {
                    foreach (Object t in targets) ((LavaFlowGenerator)t).Generate();
                }

                if (GUILayout.Button("Randomise Seed"))
                {
                    foreach (Object t in targets)
                    {
                        var g = (LavaFlowGenerator)t;
                        Undo.RecordObject(g, "Randomise Lava Flow");
                        g.Randomize();
                        EditorUtility.SetDirty(g);
                    }
                }
            }

            // Merging is the answer to "my river is in pieces", so it sits above everything else
            // and stays available while several flows are selected.
            int selectedFlows = CountSelectedFlows();
            string mergeLabel = selectedFlows >= 2
                ? "Merge " + selectedFlows + " Selected Flows Into One River"
                : "Merge Chained Flows Into One River";

            if (GUILayout.Button(mergeLabel))
                MergeFlows(generator);

            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                bool waypoints = generator.Settings.pathMode == FlowPathMode.Waypoints;

                if (GUILayout.Button("Start A New Route Here"))
                {
                    Undo.RecordObject(generator, "Start Lava Flow Route");
                    generator.Settings.waypoints = new List<Vector3>();
                    generator.Settings.pathMode = FlowPathMode.Waypoints;
                    EditorUtility.SetDirty(generator);
                    generator.Generate();

                    _appendMode = true;
                    waypoints = true;
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
                    // Drawing only means anything on an authored route, so switch to one, keeping
                    // whatever line the terrain had already picked.
                    if (_appendMode && !waypoints)
                    {
                        ConvertToWaypoints(generator);
                        waypoints = true;
                    }
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Convert Route To Waypoints"))
                    ConvertToWaypoints(generator);

                if (waypoints)
                {
                    if (generator.Settings.waypoints.Count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "Waypoints mode with no points builds nothing. Either press Convert " +
                            "Route To Waypoints to start from the route the terrain picked, or " +
                            "turn on Draw Route and click along the ground.", MessageType.Warning);
                    }

                    if (GUILayout.Button("Add Waypoint At End"))
                        AddWaypoint(generator);
                }

                using (new EditorGUI.DisabledScope(generator.Mesh == null))
                {
                    if (GUILayout.Button("Add Continuation Flow"))
                        AddContinuation(generator);

                    if (GUILayout.Button("Save Mesh Asset..."))
                        SaveMeshAsset(generator);

                    if (GUILayout.Button("Rebuild Glow Lights"))
                        LavaFlowLightBaker.Rebuild(generator);
                }
            }

            // The buttons above go grey rather than vanish when they cannot run, which is easy to
            // read as them not being there at all.
            if (generator.Mesh == null)
            {
                EditorGUILayout.HelpBox(
                    "Some actions stay greyed out until the flow has built a mesh. Press " +
                    "Regenerate, and check the ground mode if nothing appears.", MessageType.Info);
            }
        }

        /// <summary>
        /// Catches the one material mistake that costs an hour every time: dropping a shader that
        /// treats UV as a mask onto a mesh whose UVs are measured in metres.
        ///
        /// The symptom is not a subtle one and it is not obviously a UV problem — the surface
        /// renders flat white and bloom smears it over the whole screen, which looks far more like
        /// a post-processing fault than a material one. Worth naming the offending slot outright.
        /// </summary>
        static void DrawMaterialWarnings(LavaFlowGenerator generator)
        {
            if (generator.Settings.uvMode == FlowUVMode.Normalized) return;

            var renderer = generator.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Material[] materials = renderer.sharedMaterials;
            string offenders = null;

            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null || !MasksOnUV(m)) continue;

                string entry = "Element " + i + " (" + m.name + ")";
                offenders = offenders == null ? entry : offenders + ", " + entry;
            }

            if (offenders == null) return;


            EditorGUILayout.HelpBox(
                offenders + " uses a shader that reads UV as a mask rather than as a tiling " +
                "coordinate, and was authored for a small plane with UVs inside 0-1.\n\n" +
                "This flow's UVs are in metres and run well past 1, so that shader drives its " +
                "colour far past white and bloom spreads it over the screen.\n\n" +
                "Put LF_Molten in the molten slot instead. Setting UV Mode to Normalised also " +
                "stops the white, but it stretches a single tile over the entire flow and throws " +
                "away which way downstream is, so nothing will scroll correctly.",
                MessageType.Error);
        }

        /// <summary>
        /// True for the project's Asset Store lava shadergraph and anything built from it. Detected
        /// by its properties rather than by name, so a renamed copy is still caught.
        /// </summary>
        static bool MasksOnUV(Material m)
        {
            return m.HasProperty("_LavaColor") || m.HasProperty("_Lavaremap");
        }

        static void DrawStats(LavaFlowGenerator generator)
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

            EditorGUILayout.LabelField("Triangles", tris.ToString("N0"));
            EditorGUILayout.LabelField("Vertices", mesh.vertexCount.ToString("N0"));

            FlowPath path = generator.Path;
            if (path != null && path.IsValid)
            {
                float drop = path.Stations[0].Center.y - path.Stations[path.Count - 1].Center.y;
                EditorGUILayout.LabelField("Route", string.Format("{0:F0} m long, {1:F0} m of drop",
                                                                  path.Length, drop));
            }
            else
            {
                EditorGUILayout.LabelField("Route", "not solved");
            }
        }

        /// <summary>
        /// Freezes the route the downhill walk found into editable waypoints. The usual way to work
        /// is to let the terrain pick the line, then move the handful of points that went somewhere
        /// you did not want.
        /// </summary>
        static void ConvertToWaypoints(LavaFlowGenerator generator)
        {
            FlowPath path = generator.SolvePath();
            if (path == null || !path.IsValid)
            {
                EditorUtility.DisplayDialog("Lava Flow",
                    "There is no route to convert yet. Check the ground mode and that the source " +
                    "sits over the terrain.", "OK");
                return;
            }

            // One point every few metres: enough to hold the shape, few enough to drag by hand.
            float spacing = Mathf.Max(6f, generator.Settings.stationSpacing * 5f);
            var points = new List<Vector3>();
            float next = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                if (path.Stations[i].Distance < next && i != path.Count - 1) continue;
                next = path.Stations[i].Distance + spacing;
                if (i == 0) continue; // the source is always the first control point
                points.Add(path.Stations[i].Center);
            }

            Undo.RecordObject(generator, "Convert Lava Flow Route");
            generator.Settings.waypoints = points;
            generator.Settings.pathMode = FlowPathMode.Waypoints;
            EditorUtility.SetDirty(generator);
            generator.Generate();
        }

        static int CountSelectedFlows()
        {
            return Selection.GetFiltered<LavaFlowGenerator>(SelectionMode.Editable).Length;
        }

        /// <summary>
        /// Collapses several flows into one continuous route on <paramref name="primary"/>.
        ///
        /// Chaining separate meshes can only ever butt them up against each other: each is solved
        /// on its own, so moving one reopens the join, and no amount of snapping survives being
        /// dragged around. A river that has to bend and be re-drawn wants to be a single ribbon
        /// with an editable centreline, and this converts one into the other — every piece's route
        /// becomes waypoints on the first flow, and the leftovers are deleted.
        /// </summary>
        static void MergeFlows(LavaFlowGenerator primary)
        {
            List<LavaFlowGenerator> flows = CollectFlows(primary);
            if (flows.Count < 2)
            {
                EditorUtility.DisplayDialog("Lava Flow",
                    "Nothing to merge. Select the flows you want joined — or chain them with the " +
                    "Upstream field — then press this again.", "OK");
                return;
            }

            List<LavaFlowGenerator> ordered = OrderHeadToToe(flows, primary);

            // The merged route is written onto whichever flow the river actually starts at, which
            // is not necessarily the one that was clicked.
            primary = ordered[0];

            // Walk every route in order and string them into one line through the world.
            var line = new List<Vector3>();
            float biggestGap = 0f;

            var positions = new List<Vector3>();
            var slopes = new List<float>();
            var widths = new List<float>();
            float spacing = Mathf.Max(6f, primary.Settings.stationSpacing * 4f);

            for (int i = 0; i < ordered.Count; i++)
            {
                LavaFlowGenerator flow = ordered[i];
                if (flow.Path == null || !flow.Path.IsValid) flow.Generate();

                flow.SampleCentreline(spacing, positions, slopes, widths);
                if (positions.Count == 0) continue;

                if (line.Count > 0)
                {
                    float gap = Vector3.Distance(line[line.Count - 1], positions[0]);
                    if (gap > biggestGap) biggestGap = gap;

                    // Drop a head that lands on top of the previous toe, or the route doubles back
                    // on itself for a station and the ribbon kinks.
                    if (gap < spacing * 0.5f) positions.RemoveAt(0);
                }

                line.AddRange(positions);
            }

            if (line.Count < 2)
            {
                EditorUtility.DisplayDialog("Lava Flow",
                    "None of those flows have a route solved yet. Press Regenerate first.", "OK");
                return;
            }

            float length = 0f;
            for (int i = 1; i < line.Count; i++) length += Vector3.Distance(line[i - 1], line[i]);

            Undo.RecordObject(primary, "Merge Lava Flows");

            // The first point is the source, which is the object's own position.
            primary.transform.position = line[0];

            var waypoints = new List<Vector3>(line.Count - 1);
            for (int i = 1; i < line.Count; i++)
                waypoints.Add(primary.transform.InverseTransformPoint(line[i]));

            primary.Settings.waypoints = waypoints;
            primary.Settings.pathMode = FlowPathMode.Waypoints;
            primary.Settings.maxLength = Mathf.Clamp(length * 1.15f, 10f, 3000f);
            EditorUtility.SetDirty(primary);

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] == primary) continue;
                Undo.DestroyObjectImmediate(ordered[i].gameObject);
            }

            primary.Generate();
            Selection.activeObject = primary.gameObject;

            Debug.LogFormat(primary,
                "Lava Flow: merged {0} flows into one {1:F0} m river with {2} waypoints." +
                (biggestGap > spacing * 2f
                    ? " Largest gap bridged was {3:F1} m — check that stretch, it was interpolated."
                    : ""),
                ordered.Count, length, waypoints.Count, biggestGap);
        }

        /// <summary>The selected flows, or failing that everything chained onto this one.</summary>
        static List<LavaFlowGenerator> CollectFlows(LavaFlowGenerator primary)
        {
            var found = new List<LavaFlowGenerator>();

            LavaFlowGenerator[] selected = Selection.GetFiltered<LavaFlowGenerator>(SelectionMode.Editable);
            if (selected.Length >= 2)
            {
                found.AddRange(selected);
                if (!found.Contains(primary)) found.Add(primary);
                return found;
            }

            // Nothing useful selected: follow the upstream links down from this flow instead.
            LavaFlowGenerator[] all = Object.FindObjectsByType<LavaFlowGenerator>(FindObjectsInactive.Include);
            found.Add(primary);

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < all.Length; i++)
                {
                    if (found.Contains(all[i])) continue;
                    if (all[i].Upstream == null || !found.Contains(all[i].Upstream)) continue;
                    found.Add(all[i]);
                    grew = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Puts the flows in the order the lava travels: from <paramref name="primary"/>, each time
        /// taking whichever of the rest starts nearest where the last one ended.
        /// </summary>
        static List<LavaFlowGenerator> OrderHeadToToe(List<LavaFlowGenerator> flows, LavaFlowGenerator primary)
        {
            LavaFlowGenerator start = FindSource(flows, primary);

            var remaining = new List<LavaFlowGenerator>(flows);
            remaining.Remove(start);

            var ordered = new List<LavaFlowGenerator> { start };
            Vector3 toe = ToeOf(start);

            while (remaining.Count > 0)
            {
                int best = 0;
                float bestDistance = float.MaxValue;

                for (int i = 0; i < remaining.Count; i++)
                {
                    float d = Vector3.Distance(toe, HeadOf(remaining[i]));
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    best = i;
                }

                ordered.Add(remaining[best]);
                toe = ToeOf(remaining[best]);
                remaining.RemoveAt(best);
            }

            return ordered;
        }

        /// <summary>
        /// Which of these flows the river starts at.
        ///
        /// Taking whichever one happened to be clicked and chaining outwards from it produces a
        /// river that runs from the middle to one end and then teleports back — the merged route
        /// crosses itself and looks like nothing at all. The real source is the flow whose head no
        /// other flow ends near, so that is what gets picked. An explicit upstream link beats the
        /// guess whenever there is one.
        /// </summary>
        static LavaFlowGenerator FindSource(List<LavaFlowGenerator> flows, LavaFlowGenerator fallback)
        {
            for (int i = 0; i < flows.Count; i++)
            {
                if (flows[i].Upstream == null || !flows.Contains(flows[i].Upstream))
                {
                    // Chained, and this one has nothing above it inside the set.
                    bool anyLinked = false;
                    for (int k = 0; k < flows.Count; k++)
                        if (flows[k].Upstream != null && flows.Contains(flows[k].Upstream)) anyLinked = true;

                    if (anyLinked) return flows[i];
                    break;
                }
            }

            LavaFlowGenerator best = fallback;
            float bestDistance = -1f;

            for (int i = 0; i < flows.Count; i++)
            {
                Vector3 head = HeadOf(flows[i]);

                float nearestToe = float.MaxValue;
                for (int k = 0; k < flows.Count; k++)
                {
                    if (k == i) continue;
                    float d = Vector3.Distance(head, ToeOf(flows[k]));
                    if (d < nearestToe) nearestToe = d;
                }

                if (nearestToe <= bestDistance) continue;
                bestDistance = nearestToe;
                best = flows[i];
            }

            return best;
        }

        static Vector3 HeadOf(LavaFlowGenerator flow)
        {
            return flow.transform.position;
        }

        static Vector3 ToeOf(LavaFlowGenerator flow)
        {
            Vector3 point, heading;
            float halfWidth;
            return flow.TryGetToe(out point, out heading, out halfWidth) ? point : flow.transform.position;
        }

        /// <summary>
        /// Creates the next stretch of river, already chained to this one: same tuning, same
        /// materials, parked on this flow's toe and starting at the width the lava arrives at.
        ///
        /// It comes back in Downhill mode with no waypoints, because a continuation is a new piece
        /// of ground to find a way across — copying the parent's traced route would only send it
        /// down the same line again.
        /// </summary>
        static void AddContinuation(LavaFlowGenerator generator)
        {
            var go = new GameObject(NextName(generator.name));
            Undo.RegisterCreatedObjectUndo(go, "Add Continuation Flow");
            go.transform.SetParent(generator.transform.parent, true);

            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var source = generator.GetComponent<MeshRenderer>();
            if (source != null) renderer.sharedMaterials = source.sharedMaterials;

            var next = go.AddComponent<LavaFlowGenerator>();
            EditorUtility.CopySerialized(generator, next);

            var so = new SerializedObject(next);
            so.FindProperty("upstream").objectReferenceValue = generator;
            so.FindProperty("snapToUpstream").boolValue = true;
            so.ApplyModifiedProperties();

            // Comes back as a short straight stub on an authored route, not as another downhill
            // walk. A continuation that runs off wherever the terrain takes it cannot be shaped,
            // which defeats the point of adding one by hand.
            Vector3 toe, heading;
            float halfWidth;
            generator.TryGetToe(out toe, out heading, out halfWidth);
            if (heading.sqrMagnitude < 1e-6f) heading = generator.transform.forward;

            next.Settings.pathMode = FlowPathMode.Waypoints;
            next.Settings.waypoints = new List<Vector3>();
            EditorUtility.SetDirty(next);

            // Generate once so the object parks itself on the toe, then place the stub relative to
            // where it actually ended up rather than guessing at the local space beforehand.
            next.Generate();

            next.Settings.waypoints.Add(
                next.transform.InverseTransformPoint(toe + heading.normalized * 25f));
            EditorUtility.SetDirty(next);
            next.Generate();

            Selection.activeObject = go;
            EditorGUIUtility.PingObject(go);
        }

        /// <summary>"Lava Flow" becomes "Lava Flow 2", "Lava Flow 2" becomes "Lava Flow 3".</summary>
        static string NextName(string name)
        {
            int space = name.LastIndexOf(' ');
            int number;
            if (space > 0 && int.TryParse(name.Substring(space + 1), out number))
                return name.Substring(0, space) + " " + (number + 1);

            return name + " 2";
        }

        /// <summary>
        /// Appends a point carrying on in the direction the route was already heading, so a new
        /// waypoint lands somewhere useful rather than on top of the source.
        /// </summary>
        static void AddWaypoint(LavaFlowGenerator generator)
        {
            List<Vector3> pts = generator.Settings.waypoints;

            Vector3 last = pts.Count > 0 ? pts[pts.Count - 1] : Vector3.zero;
            Vector3 previous = pts.Count > 1 ? pts[pts.Count - 2] : Vector3.zero;
            Vector3 heading = pts.Count > 1 ? (last - previous) : generator.transform.InverseTransformDirection(generator.transform.forward);
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;

            Undo.RecordObject(generator, "Add Lava Flow Waypoint");
            pts.Add(last + heading.normalized * 12f);
            EditorUtility.SetDirty(generator);
            generator.Generate();
        }

        static void SaveMeshAsset(LavaFlowGenerator generator)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Lava Flow Mesh", generator.Mesh.name, "asset",
                "Bake the current flow into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(generator.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved lava flow mesh to " + path, copy);
        }

        // ------------------------------------------------------------------ scene view

        void OnSceneGUI()
        {
            var generator = (LavaFlowGenerator)target;
            LavaFlowSettings s = generator.Settings;
            if (s.pathMode != FlowPathMode.Waypoints) return;

            Transform tr = generator.transform;

            // The source is the first point of the route, but it is the object's own transform and
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

            DrawLegend(generator, s);

            if (changed)
            {
                EditorUtility.SetDirty(generator);
                generator.Generate();
            }
        }

        static void DrawRouteLine(List<Vector3> world)
        {
            if (world.Count < 2) return;

            Handles.color = new Color(1f, 0.55f, 0.15f, 0.85f);
            Handles.DrawAAPolyLine(4f, world.ToArray());

            Handles.color = new Color(0.2f, 0.9f, 0.4f, 0.95f);
            Handles.SphereHandleCap(0, world[0], Quaternion.identity,
                                    HandleUtility.GetHandleSize(world[0]) * 0.14f, EventType.Repaint);
            Handles.Label(world[0], "  source");
        }

        /// <summary>Move handles on each waypoint, plus a delete button while Shift is held.</summary>
        bool DrawPointHandles(LavaFlowGenerator generator, Transform tr, LavaFlowSettings s,
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
                        Undo.RecordObject(generator, "Delete Lava Flow Waypoint");
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
                    Undo.RecordObject(generator, "Move Lava Flow Waypoint");
                    s.waypoints[i] = tr.InverseTransformPoint(Drape(generator, moved));
                    changed = true;
                }

                Handles.color = new Color(1f, 0.5f, 0.1f, 0.9f);
                Handles.SphereHandleCap(0, point, Quaternion.identity, size * 0.1f, EventType.Repaint);
                Handles.Label(point, "  " + (i + 1));
            }

            return changed;
        }

        /// <summary>
        /// A small dot halfway along each leg. Clicking it puts a new point there, which is how a
        /// straight run gets turned into a bend without rebuilding the whole route.
        /// </summary>
        bool DrawInsertHandles(LavaFlowGenerator generator, Transform tr, LavaFlowSettings s,
                               List<Vector3> world)
        {
            if (Event.current.shift) return false; // deleting takes over the dots

            for (int leg = 0; leg < world.Count - 1; leg++)
            {
                Vector3 mid = (world[leg] + world[leg + 1]) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid);

                Handles.color = new Color(1f, 0.85f, 0.4f, 0.8f);
                if (!Handles.Button(mid, Quaternion.identity, size * 0.055f, size * 0.11f,
                                    Handles.DotHandleCap)) continue;

                Undo.RecordObject(generator, "Insert Lava Flow Waypoint");
                // Leg 0 runs from the source, so it inserts at the head of the list.
                s.waypoints.Insert(leg, tr.InverseTransformPoint(Drape(generator, mid)));
                return true;
            }

            return false;
        }

        /// <summary>Click anywhere on the ground to extend the route while append mode is on.</summary>
        bool HandleAppendClicks(LavaFlowGenerator generator, Transform tr, LavaFlowSettings s)
        {
            // Take the default control so a click lands here rather than selecting whatever is
            // under the cursor.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return false;

            Vector3 hit;
            if (!TryPickGround(generator, e.mousePosition, out hit)) return false;

            // The first click on an empty route puts the source there rather than adding a point.
            // The source is the object's own position, so without this the river starts wherever
            // the object happened to be sitting and runs back to the first click — which reads as
            // the lava flowing the wrong way down the route that was just drawn.
            if (s.waypoints.Count == 0)
            {
                Undo.RecordObject(tr, "Place Lava Flow Source");
                tr.position = hit;
                tr.hasChanged = false;
                e.Use();
                return true;
            }

            Undo.RecordObject(generator, "Append Lava Flow Waypoint");
            s.waypoints.Add(tr.InverseTransformPoint(hit));
            e.Use();
            return true;
        }

        /// <summary>
        /// Where the cursor is pointing on the ground. Tries colliders first, then falls back to
        /// the flow's own ground sampler — a terrain with no collider still answers that one, and
        /// this project's terrain is what the route normally follows.
        /// </summary>
        static bool TryPickGround(LavaFlowGenerator generator, Vector2 mousePosition, out Vector3 hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            RaycastHit info;
            if (Physics.Raycast(ray, out info, 100000f, ~0, QueryTriggerInteraction.Ignore))
            {
                hit = info.point;
                return true;
            }

            // No collider: drop the ray onto the height the route is already at, then drape.
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

        /// <summary>Drops a point onto the ground so the handles sit where the lava will.</summary>
        static Vector3 Drape(LavaFlowGenerator generator, Vector3 worldPoint)
        {
            Vector3 ground, normal;
            if (!generator.SampleGroundWorld(worldPoint, out ground, out normal)) return worldPoint;
            return ground;
        }

        void DrawLegend(LavaFlowGenerator generator, LavaFlowSettings s)
        {
            Handles.BeginGUI();
            var rect = new Rect(10f, 10f, 260f, _appendMode ? 78f : 62f);
            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.Label(s.waypoints.Count + " waypoints", EditorStyles.boldLabel);
            GUILayout.Label("Click a small dot to insert a point");
            GUILayout.Label("Hold Shift and click a point to delete it");
            if (_appendMode)
            {
                GUILayout.Label(s.waypoints.Count == 0
                    ? "Click the ground to place the source"
                    : "Click the ground to add to the end");
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }

    /// <summary>
    /// Hangs a chain of point lights down the channel. Emissive materials do not light anything in
    /// URP unless the scene is baked, so without these the flow glows but the ground beside it
    /// stays as dark as it was — which is the single most common reason lava reads as a decal.
    /// </summary>
    public static class LavaFlowLightBaker
    {
        const string HolderName = "Flow Lights";

        public static void Rebuild(LavaFlowGenerator generator)
        {
            Transform existing = generator.transform.Find(HolderName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var positions = new List<Vector3>();
            var slopes = new List<float>();
            var widths = new List<float>();
            generator.SampleCentreline(9f, positions, slopes, widths);
            if (positions.Count == 0)
            {
                Debug.LogWarning("Lava Flow: no route to place lights along yet.", generator);
                return;
            }

            var holder = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(holder, "Rebuild Lava Flow Lights");
            holder.transform.SetParent(generator.transform, false);
            holder.transform.localPosition = Vector3.zero;

            for (int i = 0; i < positions.Count; i++)
            {
                var go = new GameObject("Glow " + (i + 1));
                go.transform.SetParent(holder.transform, true);
                go.transform.position = positions[i] + Vector3.up * Mathf.Max(1f, widths[i] * 0.35f);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.42f, 0.12f);
                // Bright and short-ranged on the cascades, broader and softer on the river.
                light.range = Mathf.Lerp(widths[i] * 2.6f, widths[i] * 1.8f, slopes[i]) + 6f;
                light.intensity = Mathf.Lerp(3.2f, 5.5f, slopes[i]);
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForceVertex;
            }

            Debug.Log("Lava Flow: placed " + positions.Count + " glow lights.", holder);
        }
    }

    /// <summary>
    /// Adds the flow to the GameObject creation menu with its materials already wired up. The
    /// materials are written on first use rather than shipped, so the set always matches whichever
    /// render pipeline the project is actually on.
    /// </summary>
    public static class LavaFlowMenu
    {
        const string RootFolder = "Assets/LavaFlow";
        const string MaterialFolder = RootFolder + "/Materials";

        [MenuItem("GameObject/3D Object/Lava Flow (Low Poly)", false, 14)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Lava Flow");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<LavaFlowGenerator>();

            renderer.sharedMaterials = EnsureMaterials();

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>Loads the four submesh materials, creating any that are not there yet.</summary>
        static Material[] EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "LavaFlow");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");

            var materials = new Material[4];
            // Emission runs the red channel over 1 so the lava blooms, while green and blue stay
            // well under it. Push more than one channel past 1 and they both clip to full, which
            // turns the glow yellow and then white however orange the base colour is.
            materials[0] = LoadLit("LF_Crust_Dark", new Color(0.08f, 0.075f, 0.085f), Color.black, 0.2f);
            materials[1] = LoadLit("LF_Crust_Warm", new Color(0.22f, 0.09f, 0.055f),
                                   new Color(0.7f, 0.16f, 0.02f), 0.24f);
            materials[2] = LoadMolten("LF_Molten");
            materials[3] = LoadLit("LF_Rock", new Color(0.25f, 0.235f, 0.225f), Color.black, 0.12f);
            return materials;
        }

        static Material LoadMolten(string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("LavaFlow/Molten Lava");
            // If the scrolling shader failed to compile, fall back to a plain emissive material
            // rather than leaving the slot empty and rendering the channel magenta.
            if (shader == null)
                return LoadLit(name, new Color(0.8f, 0.25f, 0.03f), new Color(2.2f, 0.5f, 0.06f), 0.35f);

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
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
                // Deliberately kept out of global illumination: a flow this size is a large emitter,
                // and letting it bounce turns everything standing near it orange. The glow lights
                // on the inspector are the controllable way to light the scene from it.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
