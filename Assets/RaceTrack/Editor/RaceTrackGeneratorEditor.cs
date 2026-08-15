using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaceTrack.EditorTools
{
    /// <summary>
    /// Inspector and scene-view handles for <see cref="RaceTrackGenerator"/>.
    ///
    /// The scene view is the point of this tool: click a node to select it, drag it anywhere at any
    /// height, pull one handle to widen the track and another to bank it, and click the dots between
    /// nodes to add more. The two blue lines are the real edges of the racing surface as built, taken
    /// off the solved path rather than sketched — where they turn red the corner is tighter than the
    /// ribbon is wide and the mesh has folded.
    /// </summary>
    [CustomEditor(typeof(RaceTrackGenerator))]
    public class RaceTrackGeneratorEditor : UnityEditor.Editor
    {
        static readonly Color CentreColor = new Color(1f, 0.78f, 0.25f, 0.8f);
        static readonly Color EdgeColor = new Color(0.45f, 0.85f, 1f, 0.9f);
        static readonly Color FoldingColor = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color TightColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color NodeColor = new Color(1f, 0.78f, 0.25f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color InsertColor = new Color(0.5f, 1f, 0.6f, 0.9f);
        static readonly Color BlockedInsertColor = new Color(0.55f, 0.3f, 0.3f, 0.7f);

        int _selected;
        float _heightField = 10f;

        // ------------------------------------------------------------------ inspector

        public override void OnInspectorGUI()
        {
            var gen = (RaceTrackGenerator)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawStats(gen);
            DrawWarnings(gen);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Nodes", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Node After Selected")) AddNodeAfter(gen, _selected);

                using (new EditorGUI.DisabledScope(gen.Nodes.Count <= (gen.IsClosed ? 3 : 2)))
                {
                    if (GUILayout.Button("Delete Selected")) DeleteNode(gen, _selected);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spread Nodes Evenly"))
                {
                    Undo.RecordObject(gen, "Spread Track Nodes Evenly");
                    gen.RedistributeNodes();
                    _selected = Mathf.Clamp(_selected, 0, gen.Nodes.Count - 1);
                    EditorUtility.SetDirty(gen);
                }

                // Enabled off the curve as well as off the nodes: the curve can be too tight while
                // every node looks fine, and that is exactly the case worth offering the fix for.
                bool needsEasing = gen.FindTightCorners().Count > 0
                                || gen.Path.TightestRadius() < gen.Settings.minCornerRadius;

                using (new EditorGUI.DisabledScope(!needsEasing))
                {
                    if (GUILayout.Button("Ease Tight Corners")) EaseCorners(gen);
                }
            }

            EditorGUILayout.Space();
            DrawHeightSection(gen);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mesh", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check Track")) CheckTrack(gen);
                if (GUILayout.Button("Regenerate")) gen.Generate();

                using (new EditorGUI.DisabledScope(gen.Mesh == null))
                {
                    if (GUILayout.Button("Save Mesh Asset...")) SaveMeshAsset(gen);
                }
            }

            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 road, 1 kerb, 2 wall, 3 underside. Assign four materials " +
                "on the Mesh Renderer in that order — swapping the first one is how you change what " +
                "the track is made of.",
                MessageType.None);
        }

        /// <summary>
        /// Height is the thing this track is meant to be free about, so it gets its own controls
        /// rather than leaving you to drag eight nodes up one at a time.
        /// </summary>
        void DrawHeightSection(RaceTrackGenerator gen)
        {
            EditorGUILayout.LabelField("Height", EditorStyles.boldLabel);

            _heightField = EditorGUILayout.FloatField(
                new GUIContent("Metres", "Used by both buttons below."), _heightField);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Raise All By"))
                {
                    Undo.RecordObject(gen, "Raise Track");
                    gen.MoveAllBy(Vector3.up * _heightField);
                    EditorUtility.SetDirty(gen);
                }

                if (GUILayout.Button("Lower All By"))
                {
                    Undo.RecordObject(gen, "Lower Track");
                    gen.MoveAllBy(Vector3.down * _heightField);
                    EditorUtility.SetDirty(gen);
                }

                if (GUILayout.Button("Flatten To"))
                {
                    Undo.RecordObject(gen, "Flatten Track");
                    gen.SetAllHeights(_heightField);
                    EditorUtility.SetDirty(gen);
                }
            }

            if (GUILayout.Button("Follow The Ground (+ Metres Clearance)"))
            {
                Undo.RecordObject(gen, "Snap Track To Ground");
                int hits = gen.SnapNodesToGround(_heightField);
                EditorUtility.SetDirty(gen);
                Debug.Log(hits == 0
                    ? "Nothing solid under any node — the track kept the height you gave it."
                    : "Seated " + hits + " of " + gen.Nodes.Count + " nodes on the ground, " +
                      _heightField.ToString("0.##") + " m clear. Nodes with nothing beneath them " +
                      "were left where they were.", gen);
            }

            EditorGUILayout.HelpBox(
                "The track is not attached to anything. Nodes carry their own height, so it can run " +
                "along the ground, over a mountain or through open sky — and one part of the " +
                "circuit can pass clean over another.",
                MessageType.None);
        }

        static void DrawStats(RaceTrackGenerator gen)
        {
            Mesh mesh = gen.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            TrackPath path = gen.Path;
            TrackMeshBuffer built = gen.LastBuild;

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)(mesh.GetIndexCount(i) / 3);

            EditorGUILayout.LabelField("Lap length", path.Length.ToString("N0") + " m" +
                                                     (path.Closed ? " (closed loop)" : " (point to point)"));
            EditorGUILayout.LabelField("Triangles", tris.ToString("N0") +
                (path.Length > 1f ? "   (" + (tris / path.Length).ToString("F0") + " per metre)" : ""));

            if (built != null && built.MaxRoadWidth > 0f)
            {
                bool constant = built.MaxRoadWidth - built.MinRoadWidth < 0.01f;
                EditorGUILayout.LabelField("Racing surface",
                    constant
                        ? built.MaxRoadWidth.ToString("F2") + " m everywhere"
                        : built.MinRoadWidth.ToString("F2") + " - " + built.MaxRoadWidth.ToString("F2") + " m");
            }

            float radius = path.TightestRadius();
            EditorGUILayout.LabelField("Tightest corner",
                float.IsInfinity(radius) ? "straight" : radius.ToString("F1") + " m radius");
            EditorGUILayout.LabelField("Steepest gradient", path.SteepestGradient().ToString("F1") + " degrees");
            EditorGUILayout.LabelField("Most bank", path.MaxBank().ToString("F1") + " degrees");

            if (gen.GetComponent<MeshCollider>() == null)
            {
                EditorGUILayout.HelpBox(
                    "No MeshCollider on this object, so nothing can drive on the track.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Separates the two things that can be wrong with a corner, because they need different
        /// answers. A folded corner is not buildable at all and no setting rescues it. A merely tight
        /// one builds perfectly and is simply too sharp to race through at speed.
        ///
        /// Everything here is measured on the solved curve rather than on the circle through three
        /// nodes. The two genuinely disagree — a curve through unevenly spaced nodes bends harder
        /// between them than the node polygon implies, so a layout whose every node looks legal can
        /// still have a corner that folds. Warning off the node polygon would have quietly passed
        /// exactly those cases.
        /// </summary>
        void DrawWarnings(RaceTrackGenerator gen)
        {
            TrackPath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            float asked = gen.Settings.minCornerRadius;

            int foldSection;
            float advance = path.WorstEdgeAdvance(gen.Settings, out foldSection);

            float radius;
            int tightNode = gen.TightestSectionNode(out radius);
            if (tightNode < 0) return;

            if (advance <= 0f)
            {
                int node = gen.NearestNodeTo(path.Samples[foldSection].Position);
                EditorGUILayout.HelpBox(
                    "The track folds through itself near node " + node + ": the curve turns at " +
                    radius.ToString("F0") + " m radius there, and the track needs " +
                    (gen.OuterHalfWidthAt(node) * 2f).ToString("F0") + " m of room to get round. The " +
                    "inside edge sweeps backwards and the surface tears.\n\n" +
                    "No setting can build this — the road would have to overlap itself. Spread those " +
                    "nodes further apart, or narrow the track through the corner.",
                    MessageType.Error);
            }
            else if (radius < asked)
            {
                EditorGUILayout.HelpBox(
                    "Builds cleanly, but the tightest corner is " + radius.ToString("F0") +
                    " m radius near node " + tightNode + ", against the " + asked.ToString("F0") +
                    " m you asked for. A kart arriving at speed will not hold it.",
                    MessageType.Warning);
            }
            else
            {
                return;
            }

            // When the curve is much tighter than the node polygon, the cause is uneven node spacing
            // rather than any one corner, and moving nodes about will not fix it. Say so, because the
            // fix is a different button.
            float nodeRadius = gen.TightestNodeRadius();
            if (!float.IsInfinity(nodeRadius) && radius < nodeRadius * 0.75f)
            {
                EditorGUILayout.HelpBox(
                    "The nodes themselves are laid out for a " + nodeRadius.ToString("F0") +
                    " m corner, but the curve through them bends to " + radius.ToString("F0") +
                    " m. That gap is uneven node spacing — the curve tightens where nodes bunch up. " +
                    "Spread Nodes Evenly is the fix for this, not moving corners.",
                    MessageType.Info);
            }

            if (GUILayout.Button("Select Tightest Corner"))
            {
                _selected = tightNode;
                SceneView.RepaintAll();
                Repaint();
            }
        }

        void EaseCorners(RaceTrackGenerator gen)
        {
            Undo.RecordObject(gen, "Ease Track Corners");

            float beforeCurve = gen.Path.TightestRadius();
            int moved = gen.RelaxTightCorners();
            EditorUtility.SetDirty(gen);

            // Report what is actually left, not what was attempted. Easing one corner sharpens its
            // neighbours until they are eased in turn, so the count of flagged corners can rise even
            // as every radius improves — the tightest radius is the number that matters. And it is
            // read off the curve, not the nodes, because the curve is what gets driven.
            float afterNodes = gen.TightestNodeRadius();
            float afterCurve = gen.Path.TightestRadius();
            float asked = gen.Settings.minCornerRadius;

            var sb = new System.Text.StringBuilder();
            sb.Append(moved == 0
                ? "No corner could be opened out further without making another one worse. "
                : "Eased " + moved + " node(s). ");
            sb.Append("Tightest corner on the curve is now " + afterCurve.ToString("F0") + " m radius");
            sb.Append(moved == 0 || Mathf.Abs(afterCurve - beforeCurve) < 0.5f
                ? "."
                : " (was " + beforeCurve.ToString("F0") + " m).");

            if (afterCurve >= asked)
            {
                sb.Append(" Clear of the " + asked.ToString("F0") + " m you asked for.");
                Debug.Log(sb.ToString(), gen);
                return;
            }

            if (!float.IsInfinity(afterNodes) && afterCurve < afterNodes * 0.75f)
            {
                sb.Append(" The nodes are spaced for " + afterNodes.ToString("F0") + " m, so what is " +
                          "left is the curve bending harder where the nodes bunch up — press Spread " +
                          "Nodes Evenly next.");
            }
            else
            {
                sb.Append(" Still under the " + asked.ToString("F0") + " m you asked for: this layout " +
                          "does not have the room. Move nodes apart, or accept a slower corner.");
            }

            Debug.LogWarning(sb.ToString(), gen);
        }

        /// <summary>
        /// Measures the track that was actually built rather than the one that was asked for. The
        /// width question in particular deserves a real answer: a swept ribbon holds its width by
        /// construction and can only lose it by folding, so a number taken off the emitted vertices
        /// settles it either way.
        /// </summary>
        static void CheckTrack(RaceTrackGenerator gen)
        {
            gen.Generate();

            TrackPath path = gen.Path;
            TrackMeshBuffer built = gen.LastBuild;
            if (path == null || built == null || path.Samples.Count < 2)
            {
                Debug.LogWarning("Nothing to check — the track has no path yet.", gen);
                return;
            }

            float advance = path.WorstEdgeAdvance(gen.Settings);
            float radius = path.TightestRadius();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(gen.name + " — " + path.Length.ToString("N0") + " m " +
                          (path.Closed ? "closed circuit" : "point-to-point stage"));
            sb.AppendLine("  Racing surface: " + built.MinRoadWidth.ToString("F3") + " to " +
                          built.MaxRoadWidth.ToString("F3") + " m wide (measured on the mesh, " +
                          path.Samples.Count.ToString("N0") + " cross-sections)");
            sb.AppendLine("  Tightest corner: " +
                          (float.IsInfinity(radius) ? "straight" : radius.ToString("F1") + " m radius"));
            sb.AppendLine("  Room left on the inside edge at its worst: " + (advance * 100f).ToString("F0") +
                          "%   " + (advance > 0.5f ? "(comfortable)"
                                  : advance > 0f ? "(buildable, but crowded on the inside)"
                                                 : "(FOLDED — the surface tears here)"));
            sb.AppendLine("  Steepest gradient: " + path.SteepestGradient().ToString("F1") + " degrees");
            sb.AppendLine("  Most bank: " + path.MaxBank().ToString("F1") + " degrees");
            sb.AppendLine("  Triangles: " + built.TriangleCount.ToString("N0") +
                          "  (road " + built.TriangleCountIn(TrackSlot.Road).ToString("N0") +
                          ", kerb " + built.TriangleCountIn(TrackSlot.Kerb).ToString("N0") +
                          ", wall " + built.TriangleCountIn(TrackSlot.Wall).ToString("N0") +
                          ", underside " + built.TriangleCountIn(TrackSlot.Underside).ToString("N0") + ")");
            if (built.DegenerateTriangles > 0)
            {
                sb.AppendLine("  " + built.DegenerateTriangles + " collapsed triangle(s) dropped — " +
                              "normal where a barrier is scaled to nothing, worth a look otherwise.");
            }

            float karts = built.MinRoadWidth / 1.6f;
            sb.AppendLine("  That is about " + Mathf.FloorToInt(karts) + " karts abreast at the " +
                          "narrowest point.");

            if (advance <= 0f) Debug.LogError(sb.ToString(), gen);
            else Debug.Log(sb.ToString(), gen);
        }

        // ----------------------------------------------------------------- scene view

        void OnSceneGUI()
        {
            var gen = (RaceTrackGenerator)target;
            List<TrackNode> nodes = gen.Nodes;
            if (nodes == null || nodes.Count == 0) return;

            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);

            DrawRibbon(gen);
            DrawCornerFlags(gen);
            DrawInsertButtons(gen);
            DrawNodeButtons(gen);

            if (_selected >= 0 && _selected < nodes.Count)
            {
                DrawMoveHandle(gen, _selected);
                DrawWidthHandle(gen, _selected);
                DrawBankHandle(gen, _selected);
            }

            DrawOverlay(gen);

            if (GUI.changed) SceneView.RepaintAll();
        }

        /// <summary>
        /// Traces the two edges of the racing surface and the line between them, taken from the
        /// solved path — so what you see is where the track really goes, banking and all, not a
        /// sketch of the node polygon.
        /// </summary>
        static void DrawRibbon(RaceTrackGenerator gen)
        {
            TrackPath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            int n = path.Samples.Count;
            int count = path.Closed ? n + 1 : n;

            var left = new Vector3[count];
            var right = new Vector3[count];
            var centre = new Vector3[count];
            Transform tf = gen.transform;

            for (int i = 0; i < count; i++)
            {
                TrackSample s = path.Samples[i % n];
                left[i] = tf.TransformPoint(s.Position - s.Right * s.HalfWidth);
                right[i] = tf.TransformPoint(s.Position + s.Right * s.HalfWidth);
                centre[i] = tf.TransformPoint(s.Position);
            }

            Handles.color = CentreColor;
            Handles.DrawAAPolyLine(2f, centre);

            Handles.color = gen.Settings != null && path.WorstEdgeAdvance(gen.Settings) <= 0f
                ? FoldingColor
                : EdgeColor;
            Handles.DrawAAPolyLine(3f, left);
            Handles.DrawAAPolyLine(3f, right);
        }

        /// <summary>
        /// Marks the one place on the circuit that is actually the problem, on the curve where it
        /// happens rather than on the nearest node. Ringing every flagged node instead turns a long
        /// circuit into a field of overlapping circles and says nothing about which one to fix.
        /// </summary>
        static void DrawCornerFlags(RaceTrackGenerator gen)
        {
            TrackPath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            int section;
            float radius = path.TightestRadius(out section);
            if (section < 0 || radius >= gen.Settings.minCornerRadius) return;

            TrackSample s = path.Samples[section];
            Vector3 world = gen.transform.TransformPoint(s.Position);
            bool folds = path.WorstEdgeAdvance(gen.Settings) <= 0f;

            Handles.color = folds ? FoldingColor : TightColor;
            Handles.DrawWireDisc(world, gen.transform.TransformDirection(s.Up), radius);
            Handles.Label(world, folds
                ? "  tightest corner: " + radius.ToString("F0") + " m — too tight to build"
                : "  tightest corner: " + radius.ToString("F0") + " m radius");
        }

        void DrawNodeButtons(RaceTrackGenerator gen)
        {
            List<TrackNode> nodes = gen.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == _selected) continue;

                Vector3 world = gen.transform.TransformPoint(nodes[i].position);
                float size = HandleUtility.GetHandleSize(world) * 0.09f;

                Handles.color = NodeColor;
                if (Handles.Button(world, Quaternion.identity, size, size * 1.4f, Handles.SphereHandleCap))
                {
                    _selected = i;
                    Repaint();
                }
            }
        }

        void DrawInsertButtons(RaceTrackGenerator gen)
        {
            List<TrackNode> nodes = gen.Nodes;
            int spans = gen.IsClosed ? nodes.Count : nodes.Count - 1;

            for (int i = 0; i < spans; i++)
            {
                int next = (i + 1) % nodes.Count;
                Vector3 midLocal = (nodes[i].position + nodes[next].position) * 0.5f;
                Vector3 world = gen.transform.TransformPoint(midLocal);
                float size = HandleUtility.GetHandleSize(world) * 0.055f;

                // A blocked spot still draws and still responds — it explains itself on click. A
                // button that silently vanishes reads as the tool being broken.
                bool allowed = gen.CanInsertBefore(i);
                Handles.color = allowed ? InsertColor : BlockedInsertColor;

                if (!Handles.Button(world, Quaternion.identity, size, size * 1.6f, Handles.DotHandleCap))
                    continue;

                if (!allowed)
                {
                    float gap = Vector3.Distance(nodes[i].position, nodes[next].position);
                    Debug.LogWarning(string.Format(
                        "Not inserting between nodes {0} and {1}: they are {2:F1} m apart and the " +
                        "guard needs {3:F1} m here.\n" +
                        "Packing nodes together is what forces a corner tighter than the track is " +
                        "wide. To smooth this bend, move the existing nodes further apart — or use " +
                        "Spread Nodes Evenly — rather than adding more between them. Lower Min Node " +
                        "Spacing if you really do want detail this fine.",
                        i, next, gap, gen.MinimumGapBefore(i)), gen);
                    return;
                }

                Undo.RecordObject(gen, "Insert Track Node");
                TrackNode a = nodes[i];
                TrackNode b = nodes[next];
                nodes.Insert(i + 1, new TrackNode(midLocal, (a.width + b.width) * 0.5f)
                {
                    bank = (a.bank + b.bank) * 0.5f,
                    wallScale = (a.wallScale + b.wallScale) * 0.5f
                });
                _selected = i + 1;
                gen.Generate();
                EditorUtility.SetDirty(gen);
                return; // the list just changed underneath us
            }
        }

        static void DrawMoveHandle(RaceTrackGenerator gen, int index)
        {
            TrackNode node = gen.Nodes[index];
            Vector3 world = gen.transform.TransformPoint(node.position);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Tools.pivotRotation == PivotRotation.Local
                ? gen.transform.rotation
                : Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(gen, "Move Track Node");
            node.position = gen.transform.InverseTransformPoint(moved);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        /// <summary>
        /// Drags the right-hand edge of the track out or in. With Uniform Width on this edits the one
        /// width every cross-section uses, which is the setting that keeps the track from ever
        /// narrowing — so widening from any node widens the whole circuit, on purpose.
        /// </summary>
        static void DrawWidthHandle(RaceTrackGenerator gen, int index)
        {
            Vector3 tangent, up, right;
            if (!FrameAt(gen, index, out tangent, out up, out right)) return;

            TrackNode node = gen.Nodes[index];
            Transform tf = gen.transform;
            float half = (gen.Settings.uniformWidth ? gen.Settings.trackWidth : node.width) * 0.5f;

            Vector3 at = tf.TransformPoint(node.position + right * half);

            Handles.color = Color.white;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(at, tf.TransformDirection(right),
                HandleUtility.GetHandleSize(at) * 0.11f, Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            float reach = Vector3.Dot(tf.InverseTransformPoint(moved) - node.position, right);
            float width = Mathf.Max(2f, reach * 2f);

            Undo.RecordObject(gen, "Set Track Width");
            if (gen.Settings.uniformWidth) gen.Settings.trackWidth = width;
            else node.width = width;
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        /// <summary>
        /// Lifts or drops the right-hand edge to set the node's extra bank. Reading the angle back
        /// off a dragged edge point is far more direct than a rotation gizmo, because banking is
        /// precisely the question "how much higher is that edge than this one".
        /// </summary>
        static void DrawBankHandle(RaceTrackGenerator gen, int index)
        {
            Vector3 tangent, up, right;
            if (!FrameAt(gen, index, out tangent, out up, out right)) return;

            TrackNode node = gen.Nodes[index];
            Transform tf = gen.transform;
            float half = (gen.Settings.uniformWidth ? gen.Settings.trackWidth : node.width) * 0.5f;

            Vector3 local = node.position + right * (half * 0.6f);
            Vector3 at = tf.TransformPoint(local);

            Handles.color = new Color(0.6f, 1f, 0.7f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(at, tf.TransformDirection(up),
                HandleUtility.GetHandleSize(at) * 0.1f, Handles.ConeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            float lift = Vector3.Dot(tf.InverseTransformPoint(moved) - local, up);

            Undo.RecordObject(gen, "Bank Track Node");
            node.bank = Mathf.Clamp(node.bank + Mathf.Atan2(lift, half * 0.6f) * Mathf.Rad2Deg, -89f, 89f);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DrawOverlay(RaceTrackGenerator gen)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(10f, 10f, 260f, 96f), GUI.skin.box);

            TrackNode node = gen.Nodes[_selected];
            GUILayout.Label(string.Format("Node {0} of {1}", _selected + 1, gen.Nodes.Count),
                            EditorStyles.boldLabel);
            GUILayout.Label(string.Format("{0:F1} m wide, {1:+0.0;-0.0;0} deg bank, y = {2:F1}",
                gen.Settings.uniformWidth ? gen.Settings.trackWidth : node.width,
                node.bank, node.position.y));

            float radius = gen.TurnRadiusAt(_selected);
            GUILayout.Label(float.IsInfinity(radius)
                ? "straight through here"
                : string.Format("{0:F0} m corner radius", radius));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("< Prev"))
                {
                    _selected = TrackPath.Prev(_selected, gen.Nodes.Count, true);
                    Repaint();
                }
                if (GUILayout.Button("Next >"))
                {
                    _selected = TrackPath.Next(_selected, gen.Nodes.Count, true);
                    Repaint();
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // --------------------------------------------------------------------- shared

        /// <summary>
        /// The frame of the solved path at the sample nearest this node, so the width and bank
        /// handles sit on the surface the generator actually built — including whatever the automatic
        /// banking did to it — rather than on a guess made from the node polygon.
        /// </summary>
        static bool FrameAt(RaceTrackGenerator gen, int index, out Vector3 tangent, out Vector3 up,
                            out Vector3 right)
        {
            tangent = Vector3.forward;
            up = Vector3.up;
            right = Vector3.right;

            TrackPath path = gen.Path;
            if (path == null || path.Samples.Count == 0) return false;

            Vector3 target = gen.Nodes[index].position;
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < path.Samples.Count; i++)
            {
                float d = (path.Samples[i].Position - target).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = i;
            }

            TrackSample s = path.Samples[nearest];
            tangent = s.Tangent;
            up = s.Up;
            right = s.Right;
            return true;
        }

        void AddNodeAfter(RaceTrackGenerator gen, int index)
        {
            List<TrackNode> nodes = gen.Nodes;
            Undo.RecordObject(gen, "Add Track Node");

            index = Mathf.Clamp(index, 0, nodes.Count - 1);
            TrackNode from = nodes[index];

            Vector3 direction;
            if (gen.IsClosed || index < nodes.Count - 1)
            {
                direction = nodes[(index + 1) % nodes.Count].position - from.position;
            }
            else
            {
                direction = from.position - nodes[Mathf.Max(0, index - 1)].position;
            }
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;

            // Far enough out that the new node cannot itself be the start of a corner too tight to
            // build, using the same measure the insert guard applies.
            float step = Mathf.Max(gen.Settings.minCornerRadius, gen.OuterHalfWidthAt(index) * 2f);
            nodes.Insert(index + 1, new TrackNode(from.position + direction.normalized * step, from.width)
            {
                bank = from.bank,
                wallScale = from.wallScale
            });

            _selected = index + 1;
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DeleteNode(RaceTrackGenerator gen, int index)
        {
            List<TrackNode> nodes = gen.Nodes;
            int floor = gen.IsClosed ? 3 : 2;
            if (nodes.Count <= floor || index < 0 || index >= nodes.Count) return;

            Undo.RecordObject(gen, "Delete Track Node");
            nodes.RemoveAt(index);
            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        static void SaveMeshAsset(RaceTrackGenerator gen)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Track Mesh", gen.Mesh.name, "asset",
                "Bake the current circuit into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(gen.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved track mesh to " + path, copy);
        }
    }

    /// <summary>Adds the track to the GameObject creation menu with its materials already wired up.</summary>
    public static class RaceTrackMenu
    {
        const string MaterialFolder = "Assets/RaceTrack/Materials/";

        static readonly Color RoadColor = new Color(0.24f, 0.24f, 0.26f, 1f);
        static readonly Color KerbColor = new Color(0.85f, 0.22f, 0.20f, 1f);
        static readonly Color WallColor = new Color(0.88f, 0.89f, 0.92f, 1f);
        static readonly Color UndersideColor = new Color(0.36f, 0.33f, 0.30f, 1f);

        [MenuItem("GameObject/3D Object/Race Track", false, 14)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Race Track");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<RaceTrackGenerator>();

            renderer.sharedMaterials = new[]
            {
                LoadOrCreateMaterial("Track_Road", RoadColor),
                LoadOrCreateMaterial("Track_Kerb", KerbColor),
                LoadOrCreateMaterial("Track_Wall", WallColor),
                LoadOrCreateMaterial("Track_Underside", UndersideColor)
            };

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>
        /// Fetches one of the track materials, making it if it is not there. Created rather than
        /// shipped so the tool survives being handed on as scripts alone: a missing .mat would leave
        /// every new track with empty material slots, which renders magenta and reads as the tool
        /// being broken on arrival.
        /// </summary>
        static Material LoadOrCreateMaterial(string name, Color color)
        {
            string path = MaterialFolder + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = SurfaceShader();
            if (shader == null) return null;

            EnsureMaterialFolder();

            var mat = new Material(shader);
            Tint(mat, color);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// Sets base colour and a matt finish by whichever property names the shader actually has.
        /// Standard calls them _Color/_Glossiness; the SRP shaders call them _BaseColor/_Smoothness.
        /// Setting the wrong name is silent — the material comes out untinted and glossy rather than
        /// erroring — so every write is guarded.
        /// </summary>
        static void Tint(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// The shader to light a new track with.
        ///
        /// The active render pipeline is asked, and it is asked because <c>Shader.isSupported</c>
        /// cannot answer this: under URP the built-in Standard shader is still found and still
        /// reports supported, and still renders magenta. This project is URP.
        /// </summary>
        static Shader SurfaceShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultShader != null) return pipeline.defaultShader;

            string[] fallbacks = { "Universal Render Pipeline/Lit", "HDRP/Lit", "Standard" };
            foreach (string name in fallbacks)
            {
                Shader s = Shader.Find(name);
                if (s != null) return s;
            }

            Debug.LogWarning("Race Track: found no shader to build a material from. Assign materials " +
                             "to the Mesh Renderer yourself — the mesh itself is fine.");
            return null;
        }

        internal static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RaceTrack"))
                AssetDatabase.CreateFolder("Assets", "RaceTrack");
            if (!AssetDatabase.IsValidFolder("Assets/RaceTrack/Materials"))
                AssetDatabase.CreateFolder("Assets/RaceTrack", "Materials");
        }
    }
}
