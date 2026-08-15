using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Barriers.EditorTools
{
    /// <summary>
    /// Inspector and scene tool for <see cref="BarrierLine"/>: the buttons that build a run, and the
    /// two things that make it workable — clicking the line onto the hillside exactly where it
    /// should go, and dragging the points afterwards.
    /// </summary>
    [CustomEditor(typeof(BarrierLine))]
    public class BarrierLineEditor : UnityEditor.Editor
    {
        /// <summary>
        /// While on, clicking the ground in the scene view extends the line.
        ///
        /// Static because drawing is a mode rather than a per-inspector setting, and because
        /// creating a line from the menu turns it on: a new line has no shape yet, so the only
        /// useful thing to be doing with it is clicking one onto the ground.
        /// </summary>
        static bool _drawMode;

        /// <summary>Which point has a move handle on it. One at a time, or a long run is a thicket.</summary>
        int _selected = -1;

        /// <summary>Cached preview, so a 1 km line is not resampled on every scene repaint.</summary>
        List<BarrierRoute> _routes;
        List<List<BarrierLine.Placement>> _placements;
        bool _previewDirty = true;
        Matrix4x4 _lastFrame = Matrix4x4.zero;

        /// <summary>Beyond this many, the preview draws the rows but stops drawing every marker.</summary>
        const int MaxPreviewMarkers = 600;

        public static void BeginDrawing()
        {
            _drawMode = true;
            SceneView.RepaintAll();
        }

        void OnEnable()
        {
            _previewDirty = true;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            _previewDirty = true;
            SceneView.RepaintAll();
        }

        // ==================================================================== inspector

        public override void OnInspectorGUI()
        {
            var line = (BarrierLine)target;

            DrawStats(line);
            DrawActions(line);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                _previewDirty = true;
                // The component does not rebuild from OnValidate, because that also fires on scene
                // load and after every recompile. An edit made here is a real edit, so it does.
                RebuildIfAuto(line);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Rebuilding replaces every instance under the container, so hand-edits to individual " +
                "barriers are lost and Undo will not bring them back. Press Detach Instances when a " +
                "run is final — that hands the objects to the scene and leaves this line free to " +
                "build a new one.",
                MessageType.None);
        }

        void DrawStats(BarrierLine line)
        {
            EnsurePreview(line);

            int placements = 0;
            float length = 0f;
            int rows = _routes != null ? _routes.Count : 0;
            if (_routes != null)
            {
                for (int i = 0; i < _routes.Count; i++) length += _routes[i].Length;
                for (int i = 0; i < _placements.Count; i++) placements += _placements[i].Count;
                if (rows > 0) length /= rows; // both rows are roughly the same run; report one
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    string.Format("{0} points  ·  {1:0.#} m per row  ·  {2} rows",
                                  line.points.Count, length, rows),
                    EditorStyles.boldLabel);

                EditorGUILayout.LabelField(string.Format("{0} to place", placements));

                if (line.LastPlaced > 0 || line.LastSkipped > 0)
                {
                    EditorGUILayout.LabelField(
                        string.Format("last build: {0} placed, {1} skipped",
                                      line.LastPlaced, line.LastSkipped));
                }
            }

            bool hasPrefab = false;
            for (int i = 0; i < line.prefabs.Count; i++)
                if (line.prefabs[i] != null && line.prefabs[i].prefab != null) { hasPrefab = true; break; }

            if (!hasPrefab)
            {
                EditorGUILayout.HelpBox(
                    "No prefabs assigned, so nothing will be placed. Drop the fence, rock or post " +
                    "prefabs you want into What To Place.",
                    MessageType.Warning);
            }

            if (line.pathSource == BarrierPathSource.DrawnPoints && line.points.Count < 2)
            {
                EditorGUILayout.HelpBox(
                    "A line needs at least two points. Press Draw Line In Scene View and click " +
                    "along the ground where the barriers should run.",
                    MessageType.Warning);
            }
        }

        void DrawActions(BarrierLine line)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Now")) Defer(line, l => l.Build());
                if (GUILayout.Button("Clear")) Defer(line, l => l.ClearInstances());
            }

            bool wantDraw = GUILayout.Toggle(
                _drawMode,
                _drawMode ? "Drawing — click the ground (press again to stop)"
                          : "Draw Line In Scene View",
                "Button");

            if (wantDraw != _drawMode)
            {
                _drawMode = wantDraw;
                if (_drawMode) line.pathSource = BarrierPathSource.DrawnPoints;
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fit Spacing To Prefab")) FitSpacingToPrefab(line);
                if (GUILayout.Button("Snap Points To Ground")) SnapPointsToGround(line);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reverse Direction")) ReverseDirection(line);
                if (GUILayout.Button("Clear Points")) ClearPoints(line);
                if (GUILayout.Button("Detach Instances")) DetachInstances(line);
            }
        }

        /// <summary>
        /// Reads the first prefab's length along its own forward and sets the spacing to it, so
        /// fence sections meet end to end instead of being eyeballed.
        /// </summary>
        void FitSpacingToPrefab(BarrierLine line)
        {
            GameObject prefab = null;
            for (int i = 0; i < line.prefabs.Count; i++)
                if (line.prefabs[i] != null && line.prefabs[i].prefab != null)
                { prefab = line.prefabs[i].prefab; break; }

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Barrier Line", "Assign a prefab first.", "OK");
                return;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                EditorUtility.DisplayDialog("Barrier Line",
                    prefab.name + " has no renderers, so there is nothing to measure.", "OK");
                return;
            }

            // Bounds are in world space on a prefab asset, which is the prefab's own space here.
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            // A section placed with facing AlongPath runs along its local Z, so that is the span
            // that has to match the gap. Fall back to X for a model authored sideways.
            float along = b.size.z;
            if (along < 0.01f || b.size.x > b.size.z * 1.5f) along = b.size.x;

            float scaled = along * Mathf.Max(0.01f, (line.scaleMin + line.scaleMax) * 0.5f);
            if (scaled < 0.05f)
            {
                EditorUtility.DisplayDialog("Barrier Line",
                    "Measured " + scaled.ToString("0.###") + " m, which is too small to space by.", "OK");
                return;
            }

            Undo.RecordObject(line, "Fit Barrier Spacing");
            line.spacingMode = BarrierSpacingMode.Distance;
            line.spacing = scaled;
            line.spacingJitter = 0f;
            EditorUtility.SetDirty(line);
            _previewDirty = true;

            Debug.Log(string.Format("Barrier Line: spacing set to {0:0.###} m from {1}.",
                                    scaled, prefab.name), line);
            RebuildIfAuto(line);
        }

        void SnapPointsToGround(BarrierLine line)
        {
            if (line.points.Count == 0) return;

            Undo.RecordObject(line, "Snap Barrier Points");
            for (int i = 0; i < line.points.Count; i++)
            {
                Vector3 world = line.transform.TransformPoint(line.points[i]);
                Vector3 p, n;
                if (line.SampleGroundWorld(world, out p, out n))
                    line.points[i] = line.transform.InverseTransformPoint(p);
            }
            EditorUtility.SetDirty(line);
            _previewDirty = true;
            RebuildIfAuto(line);
        }

        void ReverseDirection(BarrierLine line)
        {
            if (line.points.Count < 2) return;

            // Which side is left and which is right is read off the direction of travel, so this is
            // also how you swap the two rows over without editing the offset.
            Undo.RecordObject(line, "Reverse Barrier Line");
            line.points.Reverse();
            EditorUtility.SetDirty(line);
            _previewDirty = true;
            RebuildIfAuto(line);
        }

        void ClearPoints(BarrierLine line)
        {
            if (line.points.Count > 0 &&
                !EditorUtility.DisplayDialog("Barrier Line",
                    "Throw away all " + line.points.Count + " points?", "Clear", "Cancel")) return;

            Undo.RecordObject(line, "Clear Barrier Points");
            line.points.Clear();
            _selected = -1;
            EditorUtility.SetDirty(line);
            _previewDirty = true;
            RebuildIfAuto(line);
        }

        void DetachInstances(BarrierLine line)
        {
            GameObject detached = line.DetachInstances();
            if (detached == null)
            {
                EditorUtility.DisplayDialog("Barrier Line", "There is nothing built to detach yet.", "OK");
                return;
            }

            Undo.RegisterCreatedObjectUndo(detached, "Detach Barriers");
            EditorGUIUtility.PingObject(detached);
            MarkSceneDirty(line);
            Debug.Log("Barrier Line: detached " + detached.transform.childCount +
                      " barriers to " + detached.name + ".", detached);
        }

        void RebuildIfAuto(BarrierLine line)
        {
            if (!line.autoRebuild) return;
            Defer(line, l => l.Build());
        }

        /// <summary>
        /// Runs an action on the next editor tick.
        ///
        /// Building destroys and creates scene objects, and doing that in the middle of the GUI pass
        /// that asked for it can tear the layout out from under itself. One tick later there is no
        /// GUI in flight.
        /// </summary>
        static void Defer(BarrierLine line, System.Action<BarrierLine> action)
        {
            EditorApplication.delayCall += () =>
            {
                if (line == null) return;
                action(line);
                MarkSceneDirty(line);
                SceneView.RepaintAll();
            };
        }

        static void MarkSceneDirty(BarrierLine line)
        {
            EditorUtility.SetDirty(line);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(line.gameObject.scene);
        }

        // ==================================================================== scene view

        void OnSceneGUI()
        {
            var line = (BarrierLine)target;

            EnsurePreview(line);
            DrawPreview();

            if (line.pathSource != BarrierPathSource.DrawnPoints)
            {
                DrawLegend(line);
                return;
            }

            Transform tr = line.transform;
            var world = new List<Vector3>(line.points.Count);
            for (int i = 0; i < line.points.Count; i++) world.Add(tr.TransformPoint(line.points[i]));

            DrawDrawnLine(world, line.closedLoop);

            bool changed = false;
            changed |= DrawPointHandles(line, tr, world);
            changed |= DrawInsertHandles(line, tr, world);
            if (_drawMode) changed |= HandleAppendClicks(line, tr);

            DrawLegend(line);

            if (changed)
            {
                EditorUtility.SetDirty(line);
                _previewDirty = true;
                RebuildIfAuto(line);
            }
        }

        void EnsurePreview(BarrierLine line)
        {
            // The points are local to the object, so dragging the object drags the whole line with
            // it. Nothing else notices that, and the preview would sit where the line used to be.
            Matrix4x4 frame = line.transform.localToWorldMatrix;
            if (frame != _lastFrame) { _lastFrame = frame; _previewDirty = true; }

            if (!_previewDirty && _routes != null) return;

            _routes = line.BuildRoutes();
            _placements = new List<List<BarrierLine.Placement>>();
            for (int i = 0; i < _routes.Count; i++)
            {
                int skipped;
                _placements.Add(line.SolvePlacements(_routes[i], i, out skipped));
            }
            _previewDirty = false;
        }

        /// <summary>
        /// Draws where the rows land and which way each object will face, before anything is built.
        /// This is the answer to "is that offset the right side of the track" without a rebuild.
        /// </summary>
        void DrawPreview()
        {
            if (_routes == null) return;

            for (int r = 0; r < _routes.Count; r++)
            {
                BarrierRoute route = _routes[r];
                if (!route.IsValid) continue;

                var pts = new Vector3[route.Stations.Count];
                for (int i = 0; i < route.Stations.Count; i++) pts[i] = route.Stations[i].Position;

                Handles.color = r == 0 ? new Color(0.35f, 0.85f, 1f, 0.85f)
                                       : new Color(1f, 0.55f, 0.35f, 0.85f);
                Handles.DrawAAPolyLine(3f, pts);

                List<BarrierLine.Placement> placements = _placements[r];
                if (placements.Count > MaxPreviewMarkers) continue;

                for (int i = 0; i < placements.Count; i++)
                {
                    Vector3 p = placements[i].Position;
                    float size = HandleUtility.GetHandleSize(p);
                    Handles.SphereHandleCap(0, p, Quaternion.identity, size * 0.06f, EventType.Repaint);
                    // A short tick showing which way the object is turned.
                    Handles.DrawLine(p, p + placements[i].Rotation * Vector3.forward * size * 0.35f);
                }
            }
        }

        static void DrawDrawnLine(List<Vector3> world, bool closed)
        {
            if (world.Count < 2) return;

            var pts = new List<Vector3>(world);
            if (closed) pts.Add(world[0]);

            Handles.color = new Color(0.95f, 0.8f, 0.35f, 0.9f);
            Handles.DrawAAPolyLine(4f, pts.ToArray());
        }

        /// <summary>
        /// A dot on every point; a full move handle on the one that is selected.
        ///
        /// Giving every point its own move gizmo is unusable past about a dozen — the arrows overlap
        /// each other and the thing being aimed at — so clicking a dot picks it and the handle
        /// follows the selection.
        /// </summary>
        bool DrawPointHandles(BarrierLine line, Transform tr, List<Vector3> world)
        {
            bool changed = false;
            bool deleting = Event.current.shift;

            if (_selected >= line.points.Count) _selected = -1;

            for (int i = 0; i < world.Count; i++)
            {
                Vector3 point = world[i];
                float size = HandleUtility.GetHandleSize(point);

                if (deleting)
                {
                    Handles.color = new Color(1f, 0.25f, 0.2f, 0.95f);
                    if (Handles.Button(point, Quaternion.identity, size * 0.12f, size * 0.18f,
                                       Handles.SphereHandleCap))
                    {
                        Undo.RecordObject(line, "Delete Barrier Point");
                        line.points.RemoveAt(i);
                        _selected = -1;
                        return true; // the list changed under us; redraw before touching it again
                    }
                    continue;
                }

                if (i == _selected)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(line, "Move Barrier Point");
                        line.points[i] = tr.InverseTransformPoint(Drape(line, moved));
                        changed = true;
                    }

                    Handles.color = new Color(0.3f, 1f, 0.5f, 0.95f);
                    Handles.SphereHandleCap(0, point, Quaternion.identity, size * 0.1f, EventType.Repaint);
                }
                else
                {
                    Handles.color = new Color(0.95f, 0.75f, 0.2f, 0.9f);
                    if (Handles.Button(point, Quaternion.identity, size * 0.08f, size * 0.14f,
                                       Handles.SphereHandleCap))
                    {
                        _selected = i;
                        Repaint();
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// A small dot halfway along each leg. Clicking it puts a new point there, which is how a
        /// straight run gets turned into a bend without redrawing the whole line.
        /// </summary>
        bool DrawInsertHandles(BarrierLine line, Transform tr, List<Vector3> world)
        {
            if (Event.current.shift || world.Count < 2) return false; // deleting takes over the dots

            int legs = line.closedLoop ? world.Count : world.Count - 1;
            for (int leg = 0; leg < legs; leg++)
            {
                Vector3 a = world[leg];
                Vector3 b = world[(leg + 1) % world.Count];
                Vector3 mid = (a + b) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid);

                Handles.color = new Color(1f, 0.9f, 0.5f, 0.75f);
                if (!Handles.Button(mid, Quaternion.identity, size * 0.05f, size * 0.1f,
                                    Handles.DotHandleCap)) continue;

                Undo.RecordObject(line, "Insert Barrier Point");
                line.points.Insert(leg + 1, tr.InverseTransformPoint(Drape(line, mid)));
                _selected = leg + 1;
                return true;
            }

            return false;
        }

        /// <summary>Click anywhere on the ground to extend the line while drawing is on.</summary>
        bool HandleAppendClicks(BarrierLine line, Transform tr)
        {
            // Take the default control so a click lands here rather than selecting whatever is
            // under the cursor.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);

            Event e = Event.current;

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return))
            {
                _drawMode = false;
                e.Use();
                Repaint();
                return false;
            }

            if (e.type != EventType.MouseDown || e.button != 0 || e.alt || e.shift) return false;

            Vector3 hit;
            if (!TryPickGround(line, e.mousePosition, out hit)) return false;

            // The first point also puts the object itself there, so the transform sits with its
            // line rather than back at the origin where it was created.
            if (line.points.Count == 0)
            {
                Undo.RecordObject(tr, "Place Barrier Line");
                tr.position = hit;
            }

            Undo.RecordObject(line, "Append Barrier Point");
            line.points.Add(tr.InverseTransformPoint(hit));
            _selected = line.points.Count - 1;
            e.Use();
            return true;
        }

        /// <summary>
        /// Where the cursor is pointing on the ground. Tries colliders first, then falls back to the
        /// line's own ground sampler — a terrain with no collider still answers that one.
        /// </summary>
        static bool TryPickGround(BarrierLine line, Vector2 mousePosition, out Vector3 hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

            RaycastHit info;
            if (Physics.Raycast(ray, out info, 100000f, ~0, QueryTriggerInteraction.Ignore))
            {
                hit = info.point;
                return true;
            }

            var plane = new Plane(Vector3.up, line.transform.position);
            float distance;
            if (!plane.Raycast(ray, out distance))
            {
                hit = Vector3.zero;
                return false;
            }

            hit = Drape(line, ray.GetPoint(distance));
            return true;
        }

        /// <summary>Drops a point onto the ground so the handles sit where the barriers will.</summary>
        static Vector3 Drape(BarrierLine line, Vector3 worldPoint)
        {
            Vector3 ground, normal;
            if (!line.SampleGroundWorld(worldPoint, out ground, out normal)) return worldPoint;
            return ground;
        }

        void DrawLegend(BarrierLine line)
        {
            Handles.BeginGUI();
            var rect = new Rect(10f, 10f, 300f, _drawMode ? 96f : 80f);
            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.Label(line.points.Count + " points  ·  " +
                            (line.side == BarrierSide.Both ? "two rows" : "one row"),
                            EditorStyles.boldLabel);
            GUILayout.Label("Click a point to pick it, then drag the handle");
            GUILayout.Label("Click a small dot to insert a point");
            GUILayout.Label("Hold Shift and click a point to delete it");
            if (_drawMode) GUILayout.Label("Click the ground to extend  ·  Esc to stop");

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ==================================================================== creation

        [MenuItem("GameObject/3D Object/Barrier Line", false, 40)]
        public static void CreateBarrierLine(MenuCommand command)
        {
            var go = new GameObject("Barrier Line");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            go.AddComponent<BarrierLine>();

            Undo.RegisterCreatedObjectUndo(go, "Create Barrier Line");
            Selection.activeObject = go;
            BeginDrawing();
        }

        [MenuItem("Tools/Barriers/New Barrier Line")]
        public static void CreateFromToolsMenu()
        {
            CreateBarrierLine(new MenuCommand(null));
        }
    }
}
