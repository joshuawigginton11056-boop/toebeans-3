using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockBridge.EditorTools
{
    /// <summary>
    /// Inspector and scene-view handles for <see cref="RockBridgeGenerator"/>.
    ///
    /// The scene view is the point of this tool: click a node to select it, drag it across the
    /// water to move the crossing, drag it up to nudge that part of the deck, and click the dots
    /// between nodes to add more. The two blue lines are the real edges of the driving surface as
    /// built, taken off the solved path rather than sketched — where they turn red the corner is
    /// tighter than the deck is wide and the mesh has folded.
    ///
    /// Every warning is measured on the solved curve and on the mesh that was actually emitted,
    /// never on the node polygon. The two genuinely disagree, and the gap between them is exactly
    /// where a fold hides.
    /// </summary>
    [CustomEditor(typeof(RockBridgeGenerator))]
    public class RockBridgeGeneratorEditor : UnityEditor.Editor
    {
        static readonly Color CentreColor = new Color(1f, 0.72f, 0.35f, 0.8f);
        static readonly Color EdgeColor = new Color(0.45f, 0.85f, 1f, 0.9f);
        static readonly Color FoldingColor = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color TightColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color NodeColor = new Color(1f, 0.72f, 0.35f, 1f);
        static readonly Color InsertColor = new Color(0.5f, 1f, 0.6f, 0.9f);
        static readonly Color LegColor = new Color(0.65f, 0.6f, 0.75f, 0.85f);

        int _selected;
        float _heightField = 10f;

        // ------------------------------------------------------------------ inspector

        public override void OnInspectorGUI()
        {
            var gen = (RockBridgeGenerator)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawStats(gen);
            DrawWarnings(gen);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Crossing", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Node At End")) AddNodeAfter(gen, gen.Nodes.Count - 1);

                using (new EditorGUI.DisabledScope(gen.Nodes.Count <= 2))
                {
                    if (GUILayout.Button("Delete Selected")) DeleteNode(gen, _selected);
                }

                if (GUILayout.Button("Spread Nodes Evenly"))
                {
                    Undo.RecordObject(gen, "Spread Bridge Nodes Evenly");
                    gen.RedistributeNodes();
                    _selected = Mathf.Clamp(_selected, 0, gen.Nodes.Count - 1);
                    EditorUtility.SetDirty(gen);
                }
            }

            EditorGUILayout.Space();
            DrawLandingSection(gen);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mesh", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check Bridge")) CheckBridge(gen);
                if (GUILayout.Button("Regenerate")) gen.Generate();

                using (new EditorGUI.DisabledScope(gen.Mesh == null))
                {
                    if (GUILayout.Button("Save Mesh Asset...")) SaveMeshAsset(gen);
                }
            }

            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 deck, 1 verge, 2 parapet, 3 rock. Assign four materials " +
                "on the Mesh Renderer in that order. The rock slot is the legs, the underside and " +
                "the landing fill together — it is all the same stone on purpose.",
                MessageType.None);
        }

        /// <summary>
        /// The landing is the one part of a bridge a kart actually feels, so it gets its own
        /// section — though with Landing Sink at 0 there is usually nothing to do here, because the
        /// deck already lands exactly on the ground.
        /// </summary>
        void DrawLandingSection(RockBridgeGenerator gen)
        {
            EditorGUILayout.LabelField("Landings", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "A flat deck cannot be made flush with a bumpy hillside by moving the deck. Landing " +
                "Sink is one number and the ground differs at the two ends, so every value leaves " +
                "the deck standing proud somewhere or lets terrain poke through somewhere else.\n\n" +
                "Blend Terrain Into Landings reshapes the ground instead, which is the only way the " +
                "join is genuinely seamless. It sets Landing Sink to 0 as part of the operation.",
                MessageType.None);

            if (GUILayout.Button("Blend Terrain Into Landings"))
                BridgeLandingBlender.Blend(gen);

            if (gen.Settings.heightMode != BridgeHeightMode.Free) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Free Height", EditorStyles.boldLabel);
            _heightField = EditorGUILayout.FloatField(
                new GUIContent("Metres", "Used by the buttons below."), _heightField);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Raise All By"))
                {
                    Undo.RecordObject(gen, "Raise Bridge");
                    gen.MoveAllBy(Vector3.up * _heightField);
                    EditorUtility.SetDirty(gen);
                }
                if (GUILayout.Button("Lower All By"))
                {
                    Undo.RecordObject(gen, "Lower Bridge");
                    gen.MoveAllBy(Vector3.down * _heightField);
                    EditorUtility.SetDirty(gen);
                }
                if (GUILayout.Button("Sit On The Ground (+ Metres)"))
                {
                    Undo.RecordObject(gen, "Snap Bridge To Ground");
                    int hits = gen.SnapNodesToGround(_heightField);
                    EditorUtility.SetDirty(gen);
                    Debug.Log(hits == 0
                        ? "Nothing under any node — the bridge kept the heights you gave it."
                        : "Seated " + hits + " of " + gen.Nodes.Count + " nodes, " +
                          _heightField.ToString("0.##") + " m clear of the ground.", gen);
                }
            }
        }

        static void DrawStats(RockBridgeGenerator gen)
        {
            Mesh mesh = gen.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            BridgePath path = gen.Path;
            BridgeMeshBuffer built = gen.LastBuild;

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)(mesh.GetIndexCount(i) / 3);

            EditorGUILayout.LabelField("Crossing", path.Length.ToString("N0") + " m");
            EditorGUILayout.LabelField("Triangles", tris.ToString("N0") +
                (path.Length > 1f ? "   (" + (tris / path.Length).ToString("F0") + " per metre)" : ""));

            if (built != null && built.MaxDeckWidth > 0f)
            {
                bool constant = built.MaxDeckWidth - built.MinDeckWidth < 0.01f;
                EditorGUILayout.LabelField("Driving surface",
                    constant
                        ? built.MaxDeckWidth.ToString("F2") + " m everywhere   (" +
                          Mathf.FloorToInt(built.MaxDeckWidth / 1.65f) + " karts abreast)"
                        : built.MinDeckWidth.ToString("F2") + " - " + built.MaxDeckWidth.ToString("F2") + " m");
            }

            if (gen.Settings.heightMode != BridgeHeightMode.Free)
            {
                EditorGUILayout.LabelField("Span sits at",
                    path.SpanLevel.ToString("F1") + " m   (" + gen.Settings.deckHeight.ToString("F1") +
                    " m over the " + path.Datum.ToString("F1") + " m it crosses)");
            }

            int clearSection;
            float clearance = path.TightestClearance(gen.Settings, out clearSection);
            if (clearSection >= 0)
            {
                EditorGUILayout.LabelField("Least clearance", clearance.ToString("F1") +
                                           " m under the slab");
            }

            if (built != null)
            {
                EditorGUILayout.LabelField("Rock legs", built.PierCount == 0
                    ? "none — the deck is close to the ground the whole way"
                    : built.PierCount + ", longest " + built.TallestPier.ToString("F1") + " m");

                EditorGUILayout.LabelField("Landing fill", !gen.Settings.buildAbutments
                    ? "off"
                    : built.FillLength < 0.5f
                        ? "none"
                        : built.FillLength.ToString("F0") + " m of " + path.Length.ToString("F0") + " m");
            }

            float radius = gen.Path.TightestRadius();
            EditorGUILayout.LabelField("Tightest corner",
                float.IsInfinity(radius) ? "straight" : radius.ToString("F1") + " m radius");
            EditorGUILayout.LabelField("Steepest approach", path.SteepestGradient().ToString("F1") + " degrees");

            float verticalR = path.MinVerticalRadius();
            EditorGUILayout.LabelField("Ramp crest", float.IsInfinity(verticalR)
                ? "flat"
                : verticalR.ToString("F0") + " m radius   (" +
                  path.VerticalLoadAt(gen.Settings.crossingSpeed).ToString("F2") + " g at " +
                  gen.Settings.crossingSpeed.ToString("F0") + " m/s)");

            EditorGUILayout.LabelField("Most bank", path.MaxBank().ToString("F1") + " degrees");

            if (gen.GetComponent<MeshCollider>() == null)
            {
                EditorGUILayout.HelpBox(
                    "No MeshCollider on this object, so nothing can drive on the bridge.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Everything that can be wrong with a bridge, each with the answer that actually fixes it.
        ///
        /// The four are genuinely different problems and want different buttons: a folded corner is
        /// not buildable at all; a merely tight one builds perfectly and is too sharp to race
        /// through; a steep approach is a ramp problem and is fixed by lengthening it, not by
        /// lowering the deck; and a deck that has sunk into what it is crossing is a clearance
        /// problem that no corner setting touches.
        /// </summary>
        void DrawWarnings(RockBridgeGenerator gen)
        {
            BridgePath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            BridgeSettings s = gen.Settings;

            if (gen.IsTilted)
            {
                EditorGUILayout.HelpBox(
                    "This object is tilted off world up by " +
                    Vector3.Angle(gen.transform.up, Vector3.up).ToString("F0") + " degrees. The " +
                    "height modes carry ground heights into local space as a plain Y, which is only " +
                    "exact while the object's own up is world up — so the deck's height over the " +
                    "ground will be off. Rotating on Y is fine; reset the X and Z rotation.",
                    MessageType.Warning);
            }

            int foldSection;
            float advance = path.WorstEdgeAdvance(s, out foldSection);
            float radius;
            int tightNode = gen.TightestSectionNode(out radius);

            if (advance <= 0f && tightNode >= 0)
            {
                int node = gen.NearestNodeTo(path.Samples[foldSection].Position);
                EditorGUILayout.HelpBox(
                    "The deck folds through itself near node " + node + ": the curve turns at " +
                    radius.ToString("F0") + " m radius there, and the bridge needs " +
                    (gen.OuterHalfWidthAt(node) * 2f).ToString("F0") + " m of room to get round. The " +
                    "inside edge sweeps backwards and the surface tears.\n\n" +
                    "No setting can build this — the deck would have to overlap itself. Spread those " +
                    "nodes further apart, or narrow the bridge through the corner.",
                    MessageType.Error);
            }
            else if (tightNode >= 0 && radius < s.minCornerRadius)
            {
                EditorGUILayout.HelpBox(
                    "Builds cleanly, but the tightest corner is " + radius.ToString("F0") +
                    " m radius near node " + tightNode + ", against the " + s.minCornerRadius.ToString("F0") +
                    " m you asked for. A kart arriving at speed will not hold it — and there is a " +
                    "parapet and a drop on both sides here.",
                    MessageType.Warning);
            }

            int steepSection;
            float gradient = path.SteepestGradient(out steepSection);
            if (gradient > s.maxGradient && s.heightMode != BridgeHeightMode.Free)
            {
                // The ramp has to lose the whole height over the approach, so the length needed
                // scales straight off the tangent — which makes this a number rather than advice.
                float needed = s.approachLength * Mathf.Tan(gradient * Mathf.Deg2Rad)
                                                / Mathf.Max(0.02f, Mathf.Tan(s.maxGradient * Mathf.Deg2Rad));
                EditorGUILayout.HelpBox(
                    "The approach ramps reach " + gradient.ToString("F1") + " degrees, against the " +
                    s.maxGradient.ToString("F0") + " you asked for. At " + s.deckHeight.ToString("F0") +
                    " m of deck height that climb has to happen somewhere.\n\n" +
                    "Raise Approach Length to about " + needed.ToString("F0") + " m — that is the fix. " +
                    "Lowering the deck works too, but costs the clearance underneath.",
                    MessageType.Warning);
            }

            // Separate from the gradient warning on purpose, because it is a separate failure and
            // the gradient does not predict it: what throws a kart is how tightly the ramp flattens
            // out at the crest, not how steep it was on the way up. A ramp can pass the gradient
            // check comfortably and still put the whole field in the air.
            float load = path.VerticalLoadAt(s.crossingSpeed);
            if (load > 0.7f && s.heightMode != BridgeHeightMode.Free)
            {
                float crestRadius = path.MinVerticalRadius();
                float wanted = s.crossingSpeed * s.crossingSpeed / (0.35f * 9.81f);
                float longer = s.approachLength * Mathf.Sqrt(wanted / Mathf.Max(1f, crestRadius));

                EditorGUILayout.HelpBox(
                    "The crest of the approach ramp is a " + crestRadius.ToString("F0") + " m vertical " +
                    "radius, which is " + load.ToString("F2") + " g at " + s.crossingSpeed.ToString("F0") +
                    " m/s. Past about 1 g the wheels leave the deck and the kart lands wherever it " +
                    "likes — with a parapet and a drop either side.\n\n" +
                    "This is not the gradient, and lowering the deck barely helps. Raise Approach " +
                    "Length to about " + longer.ToString("F0") + " m, which brings it to a third of a g.",
                    load > 1f ? MessageType.Error : MessageType.Warning);
            }

            int clearSection;
            float clearance = path.TightestClearance(s, out clearSection);
            if (clearSection >= 0 && clearance < 0f)
            {
                EditorGUILayout.HelpBox(
                    "The underside of the deck is " + (-clearance).ToString("F1") + " m *inside* " +
                    "whatever it is crossing, near node " +
                    gen.NearestNodeTo(path.Samples[clearSection].Position) + ".\n\n" +
                    "Raise Deck Height. If what you are crossing has no collider — a lava river, " +
                    "say — the probe cannot see it at all: switch on Use Fixed Datum and type its " +
                    "surface height in instead.",
                    MessageType.Error);
            }

            // The fill is bounded by a depth, not a distance, so on a bridge that never flies higher
            // than that depth the boundary never triggers and the two "landings" meet in the middle
            // as a pair of walls running the whole deck. It reads as a trough rather than a bridge,
            // and nothing in the settings hints at it — the depth looks perfectly reasonable.
            BridgeMeshBuffer buf = gen.LastBuild;
            if (s.buildAbutments && buf != null && path.Length > 1f && buf.FillLength > path.Length * 0.5f)
            {
                EditorGUILayout.HelpBox(
                    "The landing fill is running " + buf.FillLength.ToString("F0") + " m of this " +
                    path.Length.ToString("F0") + " m crossing — it has stopped being a pair of " +
                    "landings and become two walls down the length of the deck, hiding the legs " +
                    "and the drop behind them.\n\n" +
                    "Lower Landing Fill Depth to about " + s.minPierHeight.ToString("F0") +
                    " m so it hands over to the legs, or turn Build Landing Fill off entirely and " +
                    "let the legs carry the whole crossing.",
                    MessageType.Warning);
            }

            if (path.HasGaps)
            {
                EditorGUILayout.HelpBox(
                    "Part of this bridge has nothing underneath it that the probe can find — off the " +
                    "edge of the terrain, or over a hole. No legs are built there and the height " +
                    "falls back to Flat Ground Height. Fine if it is deliberate.",
                    MessageType.Info);
            }

            if (tightNode >= 0 && radius < s.minCornerRadius && GUILayout.Button("Select Tightest Corner"))
            {
                _selected = tightNode;
                SceneView.RepaintAll();
                Repaint();
            }
        }

        /// <summary>
        /// Measures the bridge that was actually built rather than the one that was asked for. The
        /// width question in particular deserves a real answer: a swept ribbon holds its width by
        /// construction and can only lose it by folding, so a number taken off the emitted vertices
        /// settles it either way.
        /// </summary>
        static void CheckBridge(RockBridgeGenerator gen)
        {
            gen.Generate();

            BridgePath path = gen.Path;
            BridgeMeshBuffer built = gen.LastBuild;
            if (path == null || built == null || path.Samples.Count < 2)
            {
                Debug.LogWarning("Nothing to check — the bridge has no path yet.", gen);
                return;
            }

            BridgeSettings s = gen.Settings;
            float advance = path.WorstEdgeAdvance(s);
            float radius = path.TightestRadius();
            int clearSection;
            float clearance = path.TightestClearance(s, out clearSection);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(gen.name + " — " + path.Length.ToString("N0") + " m crossing");
            sb.AppendLine("  Driving surface: " + built.MinDeckWidth.ToString("F3") + " to " +
                          built.MaxDeckWidth.ToString("F3") + " m wide (measured on the mesh, " +
                          path.Samples.Count.ToString("N0") + " cross-sections) — about " +
                          Mathf.FloorToInt(built.MinDeckWidth / 1.65f) + " karts abreast at its narrowest");
            sb.AppendLine("  Tightest corner: " +
                          (float.IsInfinity(radius) ? "straight" : radius.ToString("F1") + " m radius"));
            sb.AppendLine("  Room left on the inside edge at its worst: " + (advance * 100f).ToString("F0") +
                          "%   " + (advance > 0.5f ? "(comfortable)"
                                  : advance > 0f ? "(buildable, but crowded on the inside)"
                                                 : "(FOLDED — the surface tears here)"));
            sb.AppendLine("  Steepest approach: " + path.SteepestGradient().ToString("F1") + " degrees");
            sb.AppendLine("  Ramp crest: " + path.MinVerticalRadius().ToString("F0") + " m vertical radius — " +
                          path.VerticalLoadAt(s.crossingSpeed).ToString("F2") + " g at " +
                          s.crossingSpeed.ToString("F0") + " m/s" +
                          (path.VerticalLoadAt(s.crossingSpeed) > 1f ? "  (KARTS WILL TAKE OFF)" : ""));
            sb.AppendLine("  Most bank: " + path.MaxBank().ToString("F1") + " degrees");

            if (s.heightMode != BridgeHeightMode.Free)
            {
                sb.AppendLine("  Span level: " + path.SpanLevel.ToString("F2") + " m, over a measured " +
                              path.Datum.ToString("F2") + " m" + (s.useFixedDatum ? " (fixed datum)" : ""));
            }
            if (clearSection >= 0)
                sb.AppendLine("  Least clearance under the slab: " + clearance.ToString("F2") + " m");

            sb.AppendLine("  Rock legs: " + built.PierCount + (built.PierCount > 0
                ? ", longest " + built.TallestPier.ToString("F1") + " m"
                : " (deck is close to the ground the whole way)"));
            sb.AppendLine("  Triangles: " + built.TriangleCount.ToString("N0") +
                          "  (deck " + built.TriangleCountIn(BridgeSlot.Deck).ToString("N0") +
                          ", verge " + built.TriangleCountIn(BridgeSlot.Verge).ToString("N0") +
                          ", parapet " + built.TriangleCountIn(BridgeSlot.Parapet).ToString("N0") +
                          ", rock " + built.TriangleCountIn(BridgeSlot.Rock).ToString("N0") + ")");

            if (built.DegenerateTriangles > 0)
            {
                sb.AppendLine("  " + built.DegenerateTriangles + " collapsed triangle(s) dropped — " +
                              "normal where a parapet is scaled to nothing, worth a look otherwise.");
            }

            if (advance <= 0f || (clearSection >= 0 && clearance < 0f)) Debug.LogError(sb.ToString(), gen);
            else Debug.Log(sb.ToString(), gen);
        }

        // ----------------------------------------------------------------- scene view

        void OnSceneGUI()
        {
            var gen = (RockBridgeGenerator)target;
            List<BridgeNode> nodes = gen.Nodes;
            if (nodes == null || nodes.Count == 0) return;

            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);

            DrawRibbon(gen);
            DrawLegMarks(gen);
            DrawCornerFlag(gen);
            DrawInsertButtons(gen);
            DrawNodeButtons(gen);

            if (_selected >= 0 && _selected < nodes.Count)
            {
                DrawMoveHandle(gen, _selected);
                DrawWidthHandle(gen, _selected);
            }

            DrawOverlay(gen);

            if (GUI.changed) SceneView.RepaintAll();
        }

        /// <summary>
        /// Traces the two edges of the driving surface and the line between them, taken from the
        /// solved path — so what you see is where the deck really goes, at the height the automatic
        /// mode put it, not a sketch of the node polygon.
        /// </summary>
        static void DrawRibbon(RockBridgeGenerator gen)
        {
            BridgePath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            int n = path.Samples.Count;
            var left = new Vector3[n];
            var right = new Vector3[n];
            var centre = new Vector3[n];
            Transform tf = gen.transform;

            for (int i = 0; i < n; i++)
            {
                BridgeSample s = path.Samples[i];
                left[i] = tf.TransformPoint(s.Position - s.Right * s.HalfWidth);
                right[i] = tf.TransformPoint(s.Position + s.Right * s.HalfWidth);
                centre[i] = tf.TransformPoint(s.Position);
            }

            Handles.color = CentreColor;
            Handles.DrawAAPolyLine(2f, centre);

            Handles.color = path.WorstEdgeAdvance(gen.Settings) <= 0f ? FoldingColor : EdgeColor;
            Handles.DrawAAPolyLine(3f, left);
            Handles.DrawAAPolyLine(3f, right);
        }

        /// <summary>
        /// A stroke from the deck down to the ground wherever a leg was built. Legs are not
        /// authored and have no handles, so without this there is no way to see where they landed
        /// short of orbiting under the bridge.
        /// </summary>
        static void DrawLegMarks(RockBridgeGenerator gen)
        {
            BridgePath path = gen.Path;
            BridgeSettings s = gen.Settings;
            if (path == null || path.Length < 1f || !s.buildPiers) return;

            Transform tf = gen.transform;
            Handles.color = LegColor;

            int count = Mathf.Max(1, Mathf.FloorToInt(path.Length / Mathf.Max(4f, s.pierSpacing)));
            for (int i = 0; i < count; i++)
            {
                BridgeSample sample = path.SampleAt(path.Length * (i + 1) / (count + 1));
                if (!sample.HasGround) continue;

                float top = sample.Position.y - s.deckThickness;
                if (top - sample.GroundFloor < Mathf.Max(0.2f, s.minPierHeight)) continue;

                Vector3 a = tf.TransformPoint(new Vector3(sample.Position.x, top, sample.Position.z));
                Vector3 b = tf.TransformPoint(new Vector3(sample.Position.x, sample.GroundFloor, sample.Position.z));
                Handles.DrawDottedLine(a, b, 4f);
                Handles.Label(b, "  " + (top - sample.GroundFloor).ToString("F0") + " m leg");
            }
        }

        /// <summary>
        /// Marks the one place that is actually the problem, on the curve where it happens rather
        /// than on the nearest node. Ringing every flagged node instead turns a long crossing into
        /// a field of overlapping circles and says nothing about which one to fix.
        /// </summary>
        static void DrawCornerFlag(RockBridgeGenerator gen)
        {
            BridgePath path = gen.Path;
            if (path == null || path.Samples.Count < 2) return;

            int section;
            float radius = path.TightestRadius(out section);
            if (section < 0 || radius >= gen.Settings.minCornerRadius) return;

            BridgeSample s = path.Samples[section];
            Vector3 world = gen.transform.TransformPoint(s.Position);
            bool folds = path.WorstEdgeAdvance(gen.Settings) <= 0f;

            Handles.color = folds ? FoldingColor : TightColor;
            Handles.DrawWireDisc(world, gen.transform.TransformDirection(s.Up), radius);
            Handles.Label(world, folds
                ? "  tightest corner: " + radius.ToString("F0") + " m — too tight to build"
                : "  tightest corner: " + radius.ToString("F0") + " m radius");
        }

        void DrawNodeButtons(RockBridgeGenerator gen)
        {
            for (int i = 0; i < gen.Nodes.Count; i++)
            {
                if (i == _selected) continue;

                Vector3 world = gen.transform.TransformPoint(DeckPointFor(gen, i));
                float size = HandleUtility.GetHandleSize(world) * 0.09f;

                Handles.color = NodeColor;
                if (Handles.Button(world, Quaternion.identity, size, size * 1.4f, Handles.SphereHandleCap))
                {
                    _selected = i;
                    Repaint();
                }
            }
        }

        void DrawInsertButtons(RockBridgeGenerator gen)
        {
            List<BridgeNode> nodes = gen.Nodes;

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Vector3 midLocal = (DeckPointFor(gen, i) + DeckPointFor(gen, i + 1)) * 0.5f;
                Vector3 world = gen.transform.TransformPoint(midLocal);
                float size = HandleUtility.GetHandleSize(world) * 0.055f;

                Handles.color = InsertColor;
                if (!Handles.Button(world, Quaternion.identity, size, size * 1.6f, Handles.DotHandleCap))
                    continue;

                Undo.RecordObject(gen, "Insert Bridge Node");
                BridgeNode a = nodes[i];
                BridgeNode b = nodes[i + 1];
                Vector3 flat = (a.position + b.position) * 0.5f;

                nodes.Insert(i + 1, new BridgeNode(flat, (a.width + b.width) * 0.5f)
                {
                    bank = (a.bank + b.bank) * 0.5f,
                    wallScale = (a.wallScale + b.wallScale) * 0.5f,
                    heightOffset = (a.heightOffset + b.heightOffset) * 0.5f
                });
                _selected = i + 1;
                gen.Generate();
                EditorUtility.SetDirty(gen);
                return; // the list just changed underneath us
            }
        }

        /// <summary>
        /// Moves a node, and does something useful with the vertical drag on every height mode.
        ///
        /// The handle sits at the deck's real height rather than at the node's stored Y, because on
        /// the automatic modes those are different numbers and a handle floating metres below the
        /// bridge reads as the tool being out of step. Dragging sideways moves the crossing;
        /// dragging up nudges the deck — through the node's own Y on Free, and through its Height
        /// Offset on the automatic modes, which is the field that still applies there.
        /// </summary>
        void DrawMoveHandle(RockBridgeGenerator gen, int index)
        {
            BridgeNode node = gen.Nodes[index];
            Vector3 anchor = DeckPointFor(gen, index);
            Vector3 world = gen.transform.TransformPoint(anchor);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Tools.pivotRotation == PivotRotation.Local
                ? gen.transform.rotation
                : Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 delta = gen.transform.InverseTransformPoint(moved) - anchor;

            Undo.RecordObject(gen, "Move Bridge Node");
            node.position += new Vector3(delta.x, 0f, delta.z);

            if (gen.Settings.heightMode == BridgeHeightMode.Free) node.position += Vector3.up * delta.y;
            else node.heightOffset += delta.y;

            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        /// <summary>
        /// Drags the right-hand edge of the deck out or in. With Uniform Width on this edits the one
        /// width every cross-section uses — the setting that keeps the bridge from ever narrowing —
        /// so widening from any node widens the whole crossing, on purpose.
        /// </summary>
        static void DrawWidthHandle(RockBridgeGenerator gen, int index)
        {
            BridgeSample frame;
            if (!FrameAt(gen, index, out frame)) return;

            BridgeNode node = gen.Nodes[index];
            Transform tf = gen.transform;
            float half = (gen.Settings.uniformWidth ? gen.Settings.deckWidth : node.width) * 0.5f;

            Vector3 local = frame.Position + frame.Right * half;
            Vector3 at = tf.TransformPoint(local);

            Handles.color = Color.white;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(at, tf.TransformDirection(frame.Right),
                HandleUtility.GetHandleSize(at) * 0.11f, Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            float reach = Vector3.Dot(tf.InverseTransformPoint(moved) - frame.Position, frame.Right);
            float width = Mathf.Max(3f, reach * 2f);

            Undo.RecordObject(gen, "Set Bridge Width");
            if (gen.Settings.uniformWidth) gen.Settings.deckWidth = width;
            else node.width = width;
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DrawOverlay(RockBridgeGenerator gen)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(10f, 10f, 280f, 112f), GUI.skin.box);

            BridgeNode node = gen.Nodes[_selected];
            BridgeSettings s = gen.Settings;

            GUILayout.Label(string.Format("Node {0} of {1}", _selected + 1, gen.Nodes.Count),
                            EditorStyles.boldLabel);
            GUILayout.Label(string.Format("{0:F1} m wide   deck at y = {1:F1}",
                s.uniformWidth ? s.deckWidth : node.width, DeckPointFor(gen, _selected).y));
            GUILayout.Label(s.heightMode == BridgeHeightMode.Free
                ? "drag up to move this node"
                : string.Format("drag up to nudge: offset {0:+0.0;-0.0;0} m", node.heightOffset));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selected <= 0))
                    if (GUILayout.Button("< Prev")) { _selected--; Repaint(); }

                using (new EditorGUI.DisabledScope(_selected >= gen.Nodes.Count - 1))
                    if (GUILayout.Button("Next >")) { _selected++; Repaint(); }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // --------------------------------------------------------------------- shared

        /// <summary>
        /// Where the deck actually is at a node, in local space.
        ///
        /// Not the node's own position: on the automatic height modes the solver rewrites every
        /// height, so the stored Y is only a starting point. Handles, buttons and the overlay all
        /// read this instead, which is what keeps them on the bridge rather than under it.
        /// </summary>
        static Vector3 DeckPointFor(RockBridgeGenerator gen, int index)
        {
            BridgeSample frame;
            if (!FrameAt(gen, index, out frame)) return gen.Nodes[index].position;
            return frame.Position;
        }

        /// <summary>
        /// The solved cross-section nearest a node, so the handles sit on the surface the generator
        /// actually built — banking, height and all — rather than on a guess made from the nodes.
        /// Matched on X and Z only, because the heights are exactly what disagree.
        /// </summary>
        static bool FrameAt(RockBridgeGenerator gen, int index, out BridgeSample frame)
        {
            frame = new BridgeSample();

            BridgePath path = gen.Path;
            if (path == null || path.Samples.Count == 0) return false;

            Vector3 target = gen.Nodes[index].position;
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < path.Samples.Count; i++)
            {
                Vector3 d = path.Samples[i].Position - target;
                d.y = 0f;
                if (d.sqrMagnitude >= best) continue;
                best = d.sqrMagnitude;
                nearest = i;
            }

            frame = path.Samples[nearest];
            return true;
        }

        void AddNodeAfter(RockBridgeGenerator gen, int index)
        {
            List<BridgeNode> nodes = gen.Nodes;
            Undo.RecordObject(gen, "Add Bridge Node");

            index = Mathf.Clamp(index, 0, nodes.Count - 1);
            BridgeNode from = nodes[index];

            Vector3 direction = index > 0
                ? from.position - nodes[index - 1].position
                : nodes[Mathf.Min(1, nodes.Count - 1)].position - from.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;

            // Far enough out that the new node cannot itself be the start of a corner too tight to
            // build.
            float step = Mathf.Max(gen.Settings.minCornerRadius, gen.OuterHalfWidthAt(index) * 2f);
            nodes.Insert(index + 1, new BridgeNode(from.position + direction.normalized * step, from.width)
            {
                bank = from.bank,
                wallScale = from.wallScale,
                heightOffset = from.heightOffset
            });

            _selected = index + 1;
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DeleteNode(RockBridgeGenerator gen, int index)
        {
            List<BridgeNode> nodes = gen.Nodes;
            if (nodes.Count <= 2 || index < 0 || index >= nodes.Count) return;

            Undo.RecordObject(gen, "Delete Bridge Node");
            nodes.RemoveAt(index);
            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        static void SaveMeshAsset(RockBridgeGenerator gen)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Bridge Mesh", gen.Mesh.name, "asset",
                "Bake the current bridge into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(gen.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved bridge mesh to " + path, copy);
        }
    }

    /// <summary>Adds the bridge to the GameObject creation menu with its materials already wired up.</summary>
    public static class RockBridgeMenu
    {
        const string MaterialFolder = "Assets/RockBridge/Materials/";

        // Calibrated against this map's ground rather than against real basalt. Under a night rig
        // the albedo is multiplied by a dim light, so physically dark rock renders pure black and
        // the facets — the entire point of the look — stop being visible. The terrain layers here
        // measure 0.145 basalt and 0.266 ash, and the volcano's own rock is 0.155; these sit in
        // that family deliberately, with the deck lifted just enough to read as a surface.
        static readonly Color DeckColor = new Color(0.200f, 0.195f, 0.210f, 1f);
        static readonly Color VergeColor = new Color(0.140f, 0.132f, 0.148f, 1f);
        static readonly Color ParapetColor = new Color(0.175f, 0.166f, 0.182f, 1f);
        static readonly Color RockColor = new Color(0.155f, 0.150f, 0.170f, 1f);

        /// <summary>
        /// Builds a bridge that crosses whatever is selected — a lava pool, a river, a chasm with a
        /// mesh on it — laid across the short axis of its bounds with a landing on each shore.
        ///
        /// This exists because placing the first bridge by hand is the fiddly part and it is the
        /// same fiddle every time: find the thing, measure it, work out which way across is
        /// shorter, put the end nodes far enough onto the bank that the ramps have somewhere to go.
        /// None of that is a judgement call, so none of it should be manual. Everything after —
        /// where it bends, how high it flies — is a judgement call, and is left alone.
        /// </summary>
        [MenuItem("GameObject/3D Object/Rock Bridge Across Selection", false, 16)]
        public static void CreateAcrossSelection(MenuCommand command)
        {
            GameObject target = Selection.activeGameObject;
            var renderer = target != null ? target.GetComponentInChildren<Renderer>() : null;

            if (renderer == null)
            {
                Debug.LogWarning("Select the thing you want bridged first — a lava pool, a river, " +
                                 "anything with a renderer. Its bounds are what the crossing is " +
                                 "measured from.");
                return;
            }

            Bounds b = renderer.bounds;
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);

            // Cross the short axis: the shorter way over is the one that needs less bridge, and on
            // a long pool it is also the one that reads as a crossing rather than a causeway.
            bool alongX = b.size.x <= b.size.z;
            Vector3 across = alongX ? Vector3.right : Vector3.forward;
            float half = (alongX ? b.size.x : b.size.z) * 0.5f;

            // Far enough onto the bank that the approach ramps have ground to come down onto. A
            // bridge whose end nodes sit on the shoreline has nowhere to put the climb.
            float reach = half + Mathf.Max(35f, half * 0.55f);

            var go = new GameObject("Rock Bridge");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            var gen = go.AddComponent<RockBridgeGenerator>();

            mr.sharedMaterials = new[]
            {
                LoadOrCreateMaterial("RB_Deck", DeckColor),
                LoadOrCreateMaterial("RB_Verge", VergeColor),
                LoadOrCreateMaterial("RB_Parapet", ParapetColor),
                LoadOrCreateMaterial("RB_Rock", RockColor)
            };

            // Local space is world space, which keeps the height solver exact and the node numbers
            // readable — see the tilt warning in the inspector for why that matters.
            go.transform.position = Vector3.zero;

            var centre = new Vector3(b.center.x, 0f, b.center.z);
            gen.Nodes.Clear();
            for (int i = 0; i < 5; i++)
                gen.Nodes.Add(new BridgeNode(centre + across * Mathf.Lerp(-reach, reach, i / 4f),
                                             RockBridgeGenerator.DefaultWidth));

            gen.Generate();

            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;

            BridgePath path = gen.Path;
            int section;
            float clearance = path.TightestClearance(gen.Settings, out section);

            Debug.Log(string.Format(
                "Bridged {0}: {1:F0} m across, deck at {2:F1} m ({3:F0} m over a measured {4:F1} m), " +
                "{5} legs, {6:F1} m of clearance, ramps at {7:F1} deg and {8:F2} g at {9:F0} m/s.\n" +
                "Drag the middle nodes to shape it, and Deck Height to fly it higher — the legs " +
                "follow on their own.",
                target.name, path.Length, path.SpanLevel, gen.Settings.deckHeight, path.Datum,
                gen.LastBuild != null ? gen.LastBuild.PierCount : 0, clearance,
                path.SteepestGradient(), path.VerticalLoadAt(gen.Settings.crossingSpeed),
                gen.Settings.crossingSpeed), go);
        }

        [MenuItem("GameObject/3D Object/Rock Bridge", false, 15)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Rock Bridge");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<RockBridgeGenerator>();

            renderer.sharedMaterials = new[]
            {
                LoadOrCreateMaterial("RB_Deck", DeckColor),
                LoadOrCreateMaterial("RB_Verge", VergeColor),
                LoadOrCreateMaterial("RB_Parapet", ParapetColor),
                LoadOrCreateMaterial("RB_Rock", RockColor)
            };

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>
        /// Fetches one of the bridge materials, making it if it is not there. Created rather than
        /// shipped so the tool survives being handed on as scripts alone: a missing .mat would leave
        /// every new bridge with empty material slots, which renders magenta and reads as the tool
        /// being broken on arrival.
        /// </summary>
        static Material LoadOrCreateMaterial(string name, Color color)
        {
            string path = MaterialFolder + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = SurfaceShader();
            if (shader == null) return null;

            if (!AssetDatabase.IsValidFolder("Assets/RockBridge"))
                AssetDatabase.CreateFolder("Assets", "RockBridge");
            if (!AssetDatabase.IsValidFolder("Assets/RockBridge/Materials"))
                AssetDatabase.CreateFolder("Assets/RockBridge", "Materials");

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
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// The shader to light a new bridge with.
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

            Debug.LogWarning("Rock Bridge: found no shader to build a material from. Assign " +
                             "materials to the Mesh Renderer yourself — the mesh itself is fine.");
            return null;
        }
    }
}
