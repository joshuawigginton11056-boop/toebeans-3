using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaveTunnel.EditorTools
{
    /// <summary>
    /// Inspector and scene-view handles for <see cref="CaveTunnelGenerator"/>.
    ///
    /// The scene view is the point of this tool: click a node to select it, drag it to bend the
    /// tunnel, pull the two square handles to widen or heighten it, and click the small dots between
    /// nodes to insert a new one. Everything rebuilds live.
    /// </summary>
    [CustomEditor(typeof(CaveTunnelGenerator))]
    public class CaveTunnelGeneratorEditor : UnityEditor.Editor
    {
        const int OutlineSegments = 28;

        static readonly Color PathColor = new Color(1f, 0.78f, 0.25f, 1f);
        static readonly Color OutlineColor = new Color(0.45f, 0.85f, 1f, 0.85f);
        static readonly Color SelectedOutlineColor = new Color(1f, 0.6f, 0.2f, 1f);
        static readonly Color InsertColor = new Color(0.5f, 1f, 0.6f, 0.9f);
        static readonly Color BlockedInsertColor = new Color(0.55f, 0.3f, 0.3f, 0.7f);
        static readonly Color FoldingColor = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color PinchedColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        int _selected;

        // ------------------------------------------------------------ inspector

        public override void OnInspectorGUI()
        {
            var gen = (CaveTunnelGenerator)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawStats(gen);
            DrawTurnWarnings(gen);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Node At End")) AddNodeAtEnd(gen);

                using (new EditorGUI.DisabledScope(gen.Nodes.Count <= 2))
                {
                    if (GUILayout.Button("Delete Selected"))
                        DeleteNode(gen, _selected);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spread Nodes Evenly"))
                {
                    Undo.RecordObject(gen, "Spread Cave Nodes Evenly");
                    gen.RedistributeNodes();
                    _selected = Mathf.Clamp(_selected, 0, gen.Nodes.Count - 1);
                    EditorUtility.SetDirty(gen);
                }

                using (new EditorGUI.DisabledScope(gen.FindTightTurns().Count == 0))
                {
                    if (GUILayout.Button("Relax Tight Turns"))
                    {
                        Undo.RecordObject(gen, "Relax Cave Turns");
                        int moved = gen.RelaxTightTurns();
                        EditorUtility.SetDirty(gen);

                        // Report what is actually left, not what was attempted. Easing one corner
                        // eases its neighbours less, so the count of flagged corners can rise even
                        // as every radius improves — the tightest ratio is the number that says
                        // whether the cave can be built, so that is the number reported.
                        float worst = gen.TightestTurnRatio();

                        Debug.Log(moved == 0
                            ? "No turns needed easing."
                            : "Eased " + moved + " corner node(s). Tightest turn is now " +
                              worst.ToString("0.00") + "x its half-width" +
                              (worst >= 2f
                                  ? " — clear."
                                  : worst >= 1f
                                      ? " — buildable but pinched. Spread the corner over more distance, or narrow the cave through it."
                                      : " — still below 1x, so the mesh folds here. This corner is tighter than the cave is wide; no setting can build it."), gen);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Snap Nodes To Ground"))
                {
                    Undo.RecordObject(gen, "Snap Cave To Ground");
                    gen.SnapNodesToGround();
                    EditorUtility.SetDirty(gen);
                }

                if (GUILayout.Button("Randomise Rock"))
                {
                    Undo.RecordObject(gen, "Randomise Cave Rock");
                    gen.Settings.seed = Random.Range(int.MinValue, int.MaxValue);
                    gen.Generate();
                    EditorUtility.SetDirty(gen);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate")) gen.Generate();

                using (new EditorGUI.DisabledScope(gen.Mesh == null))
                {
                    if (GUILayout.Button("Save Mesh Asset...")) SaveMeshAsset(gen);
                }
            }

            EditorGUILayout.Space();
            DrawTerrainSection(gen);

            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 rock, 1 floor. Assign two materials on the Mesh Renderer " +
                "in that order.\n\n" +
                "Walls face inwards, so from outside you see straight through the cave. Bury the " +
                "mouths in a hillside and only the openings will read.",
                MessageType.None);
        }

        /// <summary>
        /// The cave mesh alone is not enterable — the terrain surface runs through the bore at each
        /// mouth and plugs it. This is where that gets fixed.
        /// </summary>
        void DrawTerrainSection(CaveTunnelGenerator gen)
        {
            EditorGUILayout.LabelField("Terrain", EditorStyles.boldLabel);

            var terrains = CaveTerrainHoles.TerrainsUnder(gen);
            if (terrains.Count == 0)
            {
                EditorGUILayout.HelpBox("No terrain under this cave.", MessageType.None);
                return;
            }

            Terrain terrain = terrains[0];
            Material mat = terrain.materialTemplate;
            bool capable = mat != null && mat.shader != null
                        && (mat.shader.name.Contains("Holes") || mat.shader.name.StartsWith("Nature/Terrain"));

            bool haveHoleShader = Shader.Find("Cave/Terrain Flat With Holes") != null;

            if (!capable)
            {
                EditorGUILayout.HelpBox(
                    "The terrain uses '" + (mat != null ? mat.shader.name : "no material") +
                    "', which ignores terrain holes. Holes would work for physics but the mouth " +
                    "would still look plugged.\n\n" +
                    (haveHoleShader
                        ? "Switching swaps in a copy of the current look on a shader that clips " +
                          "holes. It writes a new material rather than editing this one, since a " +
                          "terrain material is usually shared."
                        : "The included hole shader is not in this project. Unity's own terrain " +
                          "materials clip holes already, so leaving the terrain on its default " +
                          "material also works — this only comes up when a terrain has been put " +
                          "onto a non-terrain shader."),
                    MessageType.Warning);

                using (new EditorGUI.DisabledScope(!haveHoleShader))
                if (GUILayout.Button("Switch Terrain To Hole-Capable Material"))
                {
                    Undo.RecordObject(terrain, "Swap Terrain Material");
                    Material created = CaveTerrainHoles.CreateHoleCapableMaterial(
                        terrain, "Assets/Cave/Materials/Terrain_Holes.mat");
                    if (created != null)
                    {
                        terrain.materialTemplate = created;
                        EditorUtility.SetDirty(terrain);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Punch Terrain Holes"))
                {
                    foreach (Terrain t in terrains)
                    {
                        CaveTerrainHoles.Result r = CaveTerrainHoles.Punch(gen, t);
                        Debug.Log(t.name + ": " + r.Message, t);
                    }
                }

                if (GUILayout.Button("Clear All Holes"))
                {
                    if (EditorUtility.DisplayDialog("Clear terrain holes",
                            "Fill in every hole on " + terrain.name +
                            ", including any not made by this cave?", "Clear", "Cancel"))
                    {
                        CaveTerrainHoles.ClearAll(terrain);
                    }
                }
            }

            if (GUILayout.Button("Check Bore For Obstructions"))
            {
                var blockers = CaveTerrainHoles.FindBoreObstructions(gen);
                blockers.Remove(terrain.name); // terrain is the hole puncher's job, not a scene fix
                if (blockers.Count == 0)
                {
                    Debug.Log("Cave bore is clear — nothing but the cave itself inside it.", gen);
                }
                else
                {
                    var sb = new System.Text.StringBuilder(
                        "Scenery standing inside the cave bore. Terrain holes cannot clear these — " +
                        "move the cave, move the prop, or remove the prop's collider:\n");
                    foreach (KeyValuePair<string, int> kv in blockers)
                        sb.AppendLine("  " + kv.Key + "  (blocks " + kv.Value + " probes)");
                    Debug.LogWarning(sb.ToString(), gen);
                }
            }

            EditorGUILayout.HelpBox(
                "Holes are stored in the TerrainData asset, not the scene, so they persist through a " +
                "scene revert. Re-punch after moving a mouth — old holes are not withdrawn.",
                MessageType.None);
        }

        /// <summary>
        /// Names the corners that are too tight to build. Everything else the generator can smooth
        /// its way out of; this one it cannot, so the only useful thing to do is say which nodes and
        /// by how much.
        /// </summary>
        void DrawTurnWarnings(CaveTunnelGenerator gen)
        {
            List<KeyValuePair<int, float>> tight = gen.FindTightTurns();
            if (tight.Count == 0) return;

            var folding = new List<string>();
            var pinched = new List<string>();

            foreach (KeyValuePair<int, float> t in tight)
            {
                string entry = string.Format("{0} ({1:F1} m radius, {2:F1} m wall)",
                    t.Key, gen.TurnRadiusAt(t.Key), gen.Nodes[t.Key].width);
                if (t.Value < 1f) folding.Add(entry); else pinched.Add(entry);
            }

            if (folding.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "These turns are tighter than the passage is wide, so the inner wall folds " +
                    "through itself and the mesh tears:\n  node " + string.Join("\n  node ", folding.ToArray()) +
                    "\n\nNo smoothing can build this — the corridor would have to overlap itself. " +
                    "Either widen the turn (spread those nodes further apart) or narrow the cave " +
                    "through the corner. Aim for a radius of at least twice the half-width.",
                    MessageType.Error);

                if (GUILayout.Button("Select First Folding Node"))
                {
                    _selected = int.Parse(folding[0].Split(' ')[0]);
                    SceneView.RepaintAll();
                    Repaint();
                }
            }

            if (pinched.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Buildable but pinched — the inside of these corners will look crowded:\n  node "
                    + string.Join("\n  node ", pinched.ToArray()),
                    MessageType.Warning);
            }
        }

        static void DrawStats(CaveTunnelGenerator gen)
        {
            Mesh mesh = gen.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)(mesh.GetIndexCount(i) / 3);

            EditorGUILayout.LabelField("Triangles", tris.ToString("N0"));
            EditorGUILayout.LabelField("Vertices", mesh.vertexCount.ToString("N0"));
            EditorGUILayout.LabelField("Cave length", gen.Length.ToString("F1") + " m");

            if (gen.GetComponent<MeshCollider>() == null)
            {
                EditorGUILayout.HelpBox(
                    "No MeshCollider on this object, so nothing can drive through the cave.",
                    MessageType.Warning);
            }
        }

        // ----------------------------------------------------------- scene view

        void OnSceneGUI()
        {
            var gen = (CaveTunnelGenerator)target;
            var nodes = gen.Nodes;
            if (nodes == null || nodes.Count == 0) return;

            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);

            DrawPath(gen);

            // Corners too tight to build get flagged where they are, not just in the inspector —
            // on a 28-node path a list of numbers does not tell you where to look.
            var trouble = new Dictionary<int, float>();
            foreach (KeyValuePair<int, float> t in gen.FindTightTurns()) trouble[t.Key] = t.Value;

            for (int i = 0; i < nodes.Count; i++)
                DrawSectionOutline(gen, i, i == _selected, trouble);

            DrawInsertButtons(gen);
            DrawNodeButtons(gen);

            if (_selected >= 0 && _selected < nodes.Count)
            {
                DrawMoveHandle(gen, _selected);
                DrawSizeHandles(gen, _selected);
            }

            DrawOverlay(gen);

            // Keep the handles responsive to inspector edits.
            if (GUI.changed) SceneView.RepaintAll();
        }

        static void DrawPath(CaveTunnelGenerator gen)
        {
            var nodes = gen.Nodes;
            if (nodes.Count < 2) return;

            Handles.color = PathColor;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Handles.DrawAAPolyLine(3f,
                    gen.transform.TransformPoint(nodes[i].position),
                    gen.transform.TransformPoint(nodes[i + 1].position));
            }
        }

        /// <summary>
        /// Traces the cross-section at a node so its width, height, roll and floor flatness are all
        /// visible at a glance, without having to look at the mesh from inside.
        /// </summary>
        static void DrawSectionOutline(CaveTunnelGenerator gen, int index, bool selected,
                                       Dictionary<int, float> trouble)
        {
            Vector3 axis, up, right;
            LocalFrame(gen, index, out axis, out up, out right);

            CaveNode node = gen.Nodes[index];
            var pts = new Vector3[OutlineSegments + 1];
            for (int j = 0; j <= OutlineSegments; j++)
            {
                float theta = Mathf.PI * 2f * j / OutlineSegments;
                Vector2 sec = CaveMeshBuilder.Section(theta, node.width, node.height, node.floorFlatten);
                Vector3 local = node.position + right * sec.x + up * sec.y;
                pts[j] = gen.transform.TransformPoint(local);
            }

            float ratio;
            bool folds = trouble != null && trouble.TryGetValue(index, out ratio) && ratio < 1f;
            bool pinched = trouble != null && trouble.ContainsKey(index) && !folds;

            if (folds) Handles.color = FoldingColor;
            else if (pinched) Handles.color = PinchedColor;
            else Handles.color = selected ? SelectedOutlineColor : OutlineColor;

            Handles.DrawAAPolyLine(selected || folds ? 4f : 2f, pts);

            if (folds)
            {
                Handles.Label(gen.transform.TransformPoint(gen.Nodes[index].position),
                              " node " + index + ": turn too tight to build");
            }
        }

        void DrawNodeButtons(CaveTunnelGenerator gen)
        {
            var nodes = gen.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == _selected) continue;

                Vector3 world = gen.transform.TransformPoint(nodes[i].position);
                float size = HandleUtility.GetHandleSize(world) * 0.09f;

                Handles.color = PathColor;
                if (Handles.Button(world, Quaternion.identity, size, size * 1.4f, Handles.SphereHandleCap))
                {
                    _selected = i;
                    Repaint();
                }
            }
        }

        void DrawInsertButtons(CaveTunnelGenerator gen)
        {
            var nodes = gen.Nodes;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Vector3 midLocal = (nodes[i].position + nodes[i + 1].position) * 0.5f;
                Vector3 world = gen.transform.TransformPoint(midLocal);
                float size = HandleUtility.GetHandleSize(world) * 0.055f;

                // A blocked spot still draws and still responds — it explains itself on click.
                // A button that silently vanishes reads as the tool being broken.
                bool allowed = gen.CanInsertBefore(i);
                Handles.color = allowed ? InsertColor : BlockedInsertColor;

                if (!Handles.Button(world, Quaternion.identity, size, size * 1.6f, Handles.DotHandleCap))
                    continue;

                if (!allowed)
                {
                    float gap = Vector3.Distance(nodes[i].position, nodes[i + 1].position);
                    Debug.LogWarning(string.Format(
                        "Not inserting between nodes {0} and {1}: they are {2:F1} m apart and the " +
                        "guard needs {3:F1} m here, for a passage {4:F1} m wide.\n" +
                        "Packing nodes close together is what forces a turn radius too tight to " +
                        "build. To smooth this corner, move the existing nodes further apart — or " +
                        "use Spread Nodes Evenly — rather than adding more between them. Lower Min " +
                        "Node Spacing if you really do want detail this fine.",
                        i, i + 1, gap, gen.MinimumGapBefore(i),
                        (nodes[i].width + nodes[i + 1].width)), gen);
                    return;
                }

                Undo.RecordObject(gen, "Insert Cave Node");
                CaveNode a = nodes[i];
                CaveNode b = nodes[i + 1];
                var inserted = new CaveNode(midLocal, (a.width + b.width) * 0.5f, (a.height + b.height) * 0.5f)
                {
                    roll = (a.roll + b.roll) * 0.5f,
                    floorFlatten = (a.floorFlatten + b.floorFlatten) * 0.5f,
                    roughness = (a.roughness + b.roughness) * 0.5f
                };
                nodes.Insert(i + 1, inserted);
                _selected = i + 1;
                gen.Generate();
                EditorUtility.SetDirty(gen);
                return; // the list just changed underneath us
            }
        }

        static void DrawMoveHandle(CaveTunnelGenerator gen, int index)
        {
            CaveNode node = gen.Nodes[index];
            Vector3 world = gen.transform.TransformPoint(node.position);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Tools.pivotRotation == PivotRotation.Local
                ? gen.transform.rotation
                : Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(gen, "Move Cave Node");
            node.position = gen.transform.InverseTransformPoint(moved);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        /// <summary>
        /// Two slider handles on the selected node: one out to the wall, one up to the apex. This is
        /// how a plain tunnel becomes a cavern — drag them out on a middle node and the swelling
        /// eases in and out along the curve on its own.
        /// </summary>
        static void DrawSizeHandles(CaveTunnelGenerator gen, int index)
        {
            Vector3 axis, up, right;
            LocalFrame(gen, index, out axis, out up, out right);

            CaveNode node = gen.Nodes[index];
            Transform tf = gen.transform;

            Vector3 widthWorld = tf.TransformPoint(node.position + right * node.width);
            Vector3 heightWorld = tf.TransformPoint(node.position + up * node.height);

            Handles.color = Color.white;

            EditorGUI.BeginChangeCheck();
            Vector3 newWidth = Handles.Slider(widthWorld, tf.TransformDirection(right),
                HandleUtility.GetHandleSize(widthWorld) * 0.11f, Handles.CubeHandleCap, 0f);
            bool widthChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            Vector3 newHeight = Handles.Slider(heightWorld, tf.TransformDirection(up),
                HandleUtility.GetHandleSize(heightWorld) * 0.11f, Handles.CubeHandleCap, 0f);
            bool heightChanged = EditorGUI.EndChangeCheck();

            if (!widthChanged && !heightChanged) return;

            Undo.RecordObject(gen, "Resize Cave Node");
            if (widthChanged)
            {
                Vector3 local = tf.InverseTransformPoint(newWidth) - node.position;
                node.width = Mathf.Max(0.1f, Vector3.Dot(local, right));
            }
            if (heightChanged)
            {
                Vector3 local = tf.InverseTransformPoint(newHeight) - node.position;
                node.height = Mathf.Max(0.1f, Vector3.Dot(local, up));
            }
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DrawOverlay(CaveTunnelGenerator gen)
        {
            Handles.BeginGUI();
            var rect = new Rect(10f, 10f, 250f, 78f);
            GUILayout.BeginArea(rect, GUI.skin.box);

            CaveNode node = gen.Nodes[_selected];
            GUILayout.Label(string.Format("Node {0} of {1}", _selected + 1, gen.Nodes.Count),
                            EditorStyles.boldLabel);
            GUILayout.Label(string.Format("{0:F1} m wide, {1:F1} m high",
                                          node.width * 2f, node.height));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selected == 0))
                    if (GUILayout.Button("< Prev")) { _selected--; Repaint(); }

                using (new EditorGUI.DisabledScope(_selected >= gen.Nodes.Count - 1))
                    if (GUILayout.Button("Next >")) { _selected++; Repaint(); }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // --------------------------------------------------------------- shared

        /// <summary>
        /// The same world-up frame the builder uses, so the outline and handles line up with the
        /// mesh instead of drifting from it.
        /// </summary>
        static void LocalFrame(CaveTunnelGenerator gen, int index,
                               out Vector3 axis, out Vector3 up, out Vector3 right)
        {
            var nodes = gen.Nodes;
            Vector3 prev = nodes[Mathf.Max(index - 1, 0)].position;
            Vector3 next = nodes[Mathf.Min(index + 1, nodes.Count - 1)].position;

            axis = next - prev;
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.forward;
            axis.Normalize();

            up = Vector3.up - Vector3.Dot(Vector3.up, axis) * axis;
            if (up.sqrMagnitude < 1e-4f) up = Vector3.Cross(axis, Vector3.right);
            up.Normalize();

            right = Vector3.Cross(up, axis).normalized;

            float roll = nodes[index].roll;
            if (Mathf.Abs(roll) > 0.001f)
            {
                Quaternion q = Quaternion.AngleAxis(roll, axis);
                up = q * up;
                right = q * right;
            }
        }

        static void AddNodeAtEnd(CaveTunnelGenerator gen)
        {
            var nodes = gen.Nodes;
            Undo.RecordObject(gen, "Add Cave Node");

            CaveNode last = nodes[nodes.Count - 1];
            Vector3 direction = nodes.Count >= 2
                ? (last.position - nodes[nodes.Count - 2].position).normalized
                : Vector3.forward;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;

            CaveNode added = last.Clone();
            // Far enough out that the new node cannot itself be the start of a corner too tight to
            // build, using the same measure the insert guard applies.
            float step = Mathf.Max(20f, last.width * Mathf.Max(1f, gen.Settings.minNodeSpacing) * 2f);
            added.position = last.position + direction * step;
            nodes.Add(added);

            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        void DeleteNode(CaveTunnelGenerator gen, int index)
        {
            var nodes = gen.Nodes;
            if (nodes.Count <= 2 || index < 0 || index >= nodes.Count) return;

            Undo.RecordObject(gen, "Delete Cave Node");
            nodes.RemoveAt(index);
            _selected = Mathf.Clamp(_selected, 0, nodes.Count - 1);
            gen.Generate();
            EditorUtility.SetDirty(gen);
        }

        static void SaveMeshAsset(CaveTunnelGenerator gen)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Cave Mesh", gen.Mesh.name, "asset",
                "Bake the current cave into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(gen.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved cave mesh to " + path, copy);
        }
    }

    /// <summary>Adds the cave to the GameObject creation menu with its materials already wired up.</summary>
    public static class CaveTunnelMenu
    {
        const string MaterialFolder = "Assets/Cave/Materials/";

        static readonly Color RockColor = new Color(0.72f, 0.72f, 0.76f, 1f);
        static readonly Color FloorColor = new Color(0.92f, 0.93f, 0.96f, 1f);

        [MenuItem("GameObject/3D Object/Cave Tunnel (Low Poly)", false, 13)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Cave Tunnel");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<CaveTunnelGenerator>();

            renderer.sharedMaterials = new[]
            {
                LoadOrCreateMaterial("Cave_Rock", RockColor),
                LoadOrCreateMaterial("Cave_Floor", FloorColor)
            };

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>
        /// Fetches one of the cave materials, making it if it is not there. Created rather than
        /// shipped so the tool can be handed to someone else as scripts and shaders alone: a
        /// missing .mat would otherwise leave every new cave with empty material slots, which
        /// renders magenta and reads as the tool being broken on arrival.
        /// </summary>
        static Material LoadOrCreateMaterial(string name, Color color)
        {
            string path = MaterialFolder + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = FindSurfaceShader();
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
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// The shader to light a new cave with.
        ///
        /// The active render pipeline is asked first, and it is asked because
        /// <c>Shader.isSupported</c> cannot answer this: under URP or HDRP the built-in Standard
        /// shader is still found and still reports supported, and still renders magenta. Preferring
        /// it — as this did — meant every cave created in an SRP project arrived unusable, which
        /// reads as the generator being broken rather than as one wrong material.
        ///
        /// The vertex-colour shader is only preferred where it can actually run, which is the
        /// built-in pipeline. The generator never depends on it: the mesh is just positions,
        /// normals, UVs and colours, and on a pipeline shader the cave loses nothing but the
        /// per-face shade variation baked into those colours.
        /// </summary>
        static Shader FindSurfaceShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;

            if (pipeline == null)
            {
                Shader preferred = Shader.Find("Cave/Vertex Colored Rock");
                if (preferred != null) return preferred;
            }
            else if (pipeline.defaultShader != null)
            {
                return pipeline.defaultShader;
            }

            string[] fallbacks = { "Universal Render Pipeline/Lit", "HDRP/Lit", "Standard" };
            foreach (string name in fallbacks)
            {
                Shader s = Shader.Find(name);
                if (s == null) continue;
                return s;
            }

            Debug.LogWarning("Cave: found no shader to build a material from. Assign one to the " +
                             "Mesh Renderer yourself — the mesh itself is fine.");
            return null;
        }

        internal static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Cave"))
                AssetDatabase.CreateFolder("Assets", "Cave");
            if (!AssetDatabase.IsValidFolder("Assets/Cave/Materials"))
                AssetDatabase.CreateFolder("Assets/Cave", "Materials");
        }
    }
}
