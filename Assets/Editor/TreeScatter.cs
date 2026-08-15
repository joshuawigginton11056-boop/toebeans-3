using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Places regular tree prefabs on a terrain as plain GameObjects (not Terrain
// TreeInstances), so there's no LOD Group / Nature-Soft-Occlusion shader
// requirement. Two modes: random Scatter across the whole terrain, or Paint
// Mode - a brush you drag over the Scene view, so different areas can use
// different tree prefabs (e.g. snowy trees on one slope, dead trees on
// another) just by swapping the Tree Prefabs list between strokes.
//
// Spacing lives in TreeSpacing.cs and is measured between canopy edges rather
// than between pivots; see the note there for why a single pivot distance
// cannot space a mixed prefab list.
public class TreeScatter : EditorWindow
{
    private Terrain terrain;
    [SerializeField] private List<GameObject> prefabs = new List<GameObject>();
    [SerializeField] private int count = 50;
    [SerializeField] private float minScale = 0.85f;
    [SerializeField] private float maxScale = 1.15f;
    [SerializeField] private string parentName = "ScatteredTrees";
    [SerializeField] private float maxSlope = 35f;

    [SerializeField] private TreeSpacingMode spacingMode = TreeSpacingMode.Canopy;
    [SerializeField] private float canopySpacing = 1f;
    [SerializeField] private float extraGap = 0f;
    [SerializeField] private float minSpacing = 3f;

    [SerializeField] private bool paintMode;
    [SerializeField] private float brushRadius = 5f;
    [SerializeField] private int treesPerDab = 1;
    // Fraction of the brush radius the cursor must travel before the next dab.
    // Gating on distance rather than on elapsed time is what stops a slow drag
    // (or a stationary cursor) from emptying the whole brush into one spot.
    [SerializeField] private float dabStep = 0.5f;
    [SerializeField] private bool showSpacingGizmos = true;

    private bool strokeActive;
    private int strokeUndoGroup;
    private Vector3 lastDabPos;
    private int strokePlaced;
    private int strokeRejected;

    private TreeSpacingRule Rule => new TreeSpacingRule
    {
        mode = spacingMode,
        canopySpacing = canopySpacing,
        extraGap = extraGap,
        fixedDistance = minSpacing,
    };

    // Loadouts ("forest stacks"): named sets of tree prefabs plus the tuning
    // values they were dialled in with, so a 14-tree list survives closing the
    // window, a domain reload, or starting a fresh area from scratch. Prefabs
    // are stored as asset GUIDs rather than paths so moving or renaming a
    // prefab in the project doesn't break a saved stack.
    [System.Serializable]
    private class Loadout
    {
        public string name = "";
        public List<string> prefabGuids = new List<string>();
        public string parentName = "ScatteredTrees";
        public float minSpacing = 3f;
        public float maxSlope = 35f;
        public int count = 50;
        public float brushRadius = 5f;
        public int treesPerDab = 1;
        public float minScale = 0.85f;
        public float maxScale = 1.15f;
        // Added by the canopy-spacing rewrite. Stacks saved before it have no
        // such keys, and JsonUtility leaves these initialisers in place, so an
        // old loadout comes back as Canopy mode with sane defaults rather than
        // dragging its useless pivot distance forward.
        public int spacingMode = (int)TreeSpacingMode.Canopy;
        public float canopySpacing = 1f;
        public float extraGap = 0f;
        public float dabStep = 0.5f;
    }

    [System.Serializable]
    private class LoadoutLibrary
    {
        public List<Loadout> loadouts = new List<Loadout>();
    }

    private const string LoadoutFileName = "TreeScatterLoadouts.json";
    private LoadoutLibrary library;
    [SerializeField] private int selectedLoadout = -1;
    [SerializeField] private string newLoadoutName = "";
    [SerializeField] private bool loadoutsExpanded = true;
    private Vector2 scroll;

    private static string LoadoutFilePath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", LoadoutFileName));

    [MenuItem("Tools/Trees/Scatter Trees on Terrain")]
    public static void Open()
    {
        GetWindow<TreeScatter>("Tree Scatter");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        LoadLibrary();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EndStroke();
    }

    // ------------------------------------------------------------- occupancy

    private readonly TreeOccupancyGrid occupancy = new TreeOccupancyGrid();
    private Transform occupancyParent;
    private int occupancyChildCount = -1;

    private void EnsureOccupancy(Transform parent)
    {
        if (parent == occupancyParent && parent.childCount == occupancyChildCount) return;

        // Cell size is a performance hint only - size it around the widest
        // interaction the current list can ask for.
        float biggestPrefab = 0f;
        foreach (GameObject p in prefabs)
            biggestPrefab = Mathf.Max(biggestPrefab, TreeFootprint.Radius(p));
        biggestPrefab *= Mathf.Max(1f, Mathf.Abs(maxScale));

        occupancy.Reset(Mathf.Max(2f, Rule.Required(biggestPrefab, biggestPrefab)));
        foreach (Transform child in parent)
            occupancy.Add(child.position, TreeFootprint.InstanceRadius(child));

        occupancyParent = parent;
        occupancyChildCount = parent.childCount;
    }

    private void InvalidateOccupancy()
    {
        occupancyParent = null;
        occupancyChildCount = -1;
    }

    // ---------------------------------------------------------------- window

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();
        terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);

        EditorGUILayout.Space();
        DrawLoadouts();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Tree Prefabs ({prefabs.Count})", EditorStyles.boldLabel);
        int removeAt = -1;
        for (int i = 0; i < prefabs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            prefabs[i] = (GameObject)EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
            float r = TreeFootprint.Radius(prefabs[i]);
            EditorGUILayout.LabelField(prefabs[i] == null ? "" : $"{r * 2f:F2} m", GUILayout.Width(52));
            if (GUILayout.Button("X", GUILayout.Width(24))) removeAt = i;
            EditorGUILayout.EndHorizontal();
        }
        if (removeAt >= 0) prefabs.RemoveAt(removeAt);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Prefab Slot")) prefabs.Add(null);
        if (GUILayout.Button("Remeasure", GUILayout.Width(80))) TreeFootprint.ClearCache();
        GUI.enabled = prefabs.Count > 0;
        bool clearListRequested = GUILayout.Button("Clear List", GUILayout.Width(90));
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // Modal dialogs are raised outside the layout group they were
        // requested in - opening one mid-group upsets IMGUI's control count.
        if (clearListRequested && EditorUtility.DisplayDialog("Clear Prefab List",
                $"Remove all {prefabs.Count} prefabs from the list? Saved loadouts are not affected.",
                "Clear", "Cancel"))
        {
            prefabs.Clear();
        }

        // Dropping several prefabs at once beats clicking "Add Prefab Slot" 14 times.
        Rect dropArea = GUILayoutUtility.GetRect(0f, 32f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag prefabs here to add them", EditorStyles.helpBox);
        HandlePrefabDrop(dropArea);

        DrawSizeSummary();

        EditorGUILayout.Space();
        parentName = EditorGUILayout.TextField("Group Name", parentName);
        maxSlope = EditorGUILayout.Slider("Max Slope (degrees)", maxSlope, 0f, 90f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spacing", EditorStyles.boldLabel);
        spacingMode = (TreeSpacingMode)EditorGUILayout.EnumPopup("Mode", spacingMode);
        if (spacingMode == TreeSpacingMode.Canopy)
        {
            canopySpacing = EditorGUILayout.Slider("Canopy Spacing", canopySpacing, 0.25f, 3f);
            extraGap = EditorGUILayout.FloatField("Extra Gap (m)", extraGap);
            string feel = canopySpacing < 0.85f ? "canopies interlock - dense thicket"
                : canopySpacing < 1.15f ? "canopies just touch - closed forest"
                : "clear ground between canopies - open woodland";
            EditorGUILayout.HelpBox(
                $"Measured between canopy edges, so a 2 m tree keeps its distance and a " +
                $"0.4 m sapling doesn't have to. At {canopySpacing:F2}: {feel}.",
                MessageType.None);
        }
        else
        {
            minSpacing = EditorGUILayout.FloatField("Min Spacing (m)", minSpacing);
            EditorGUILayout.HelpBox(
                "Fixed pivot-to-pivot distance, ignoring how wide each tree is. " +
                "Only useful when every prefab in the list is the same size - a " +
                "mixed list will either bury the big trees or strand the small ones.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        paintMode = EditorGUILayout.Toggle("Paint Mode", paintMode);

        if (paintMode)
        {
            brushRadius = EditorGUILayout.FloatField("Brush Radius", brushRadius);
            treesPerDab = Mathf.Max(1, EditorGUILayout.IntField("Trees Per Dab", treesPerDab));
            dabStep = EditorGUILayout.Slider("Dab Step (x radius)", dabStep, 0.05f, 1.5f);
            showSpacingGizmos = EditorGUILayout.Toggle("Show Spacing", showSpacingGizmos);
            EditorGUILayout.HelpBox(
                "Drag on the terrain to paint. Hold Ctrl to erase under the brush. " +
                "Alt still orbits. Dabs are gated on how far the cursor has moved, " +
                "not on time, so holding still won't pile trees into one spot.\n\n" +
                "Swap the Tree Prefabs list between strokes to use different trees " +
                "in different areas.", MessageType.Info);
        }
        else
        {
            count = EditorGUILayout.IntField("How Many Trees", count);
            EditorGUILayout.HelpBox(
                "Spacing checks against every tree already under the Group Name, " +
                "including ones from earlier runs with a different prefab.",
                MessageType.None);
        }

        minScale = EditorGUILayout.FloatField("Min Scale", minScale);
        maxScale = EditorGUILayout.FloatField("Max Scale", maxScale);

        if (EditorGUI.EndChangeCheck())
        {
            // Spacing rule or prefab list changed - the grid's cell size was
            // derived from both, so rebuild before the next dab.
            InvalidateOccupancy();
            SceneView.RepaintAll();
        }

        if (!paintMode)
        {
            EditorGUILayout.Space();
            GUI.enabled = terrain != null && prefabs.Count > 0;
            if (GUILayout.Button("Scatter Trees", GUILayout.Height(30)))
            {
                ScatterTrees();
            }
            GUI.enabled = true;
        }

        if (terrain == null)
            EditorGUILayout.HelpBox("Assign a Terrain.", MessageType.Info);
        else if (prefabs.Count == 0)
            EditorGUILayout.HelpBox("Add at least one tree prefab.", MessageType.Info);

        EditorGUILayout.Space();
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Clear Placed Trees", GUILayout.Height(24)))
        {
            GameObject existing = GameObject.Find(parentName);
            int childCount = existing != null ? existing.transform.childCount : 0;
            if (childCount == 0)
            {
                Debug.Log($"No trees found under '{parentName}'.");
            }
            else if (EditorUtility.DisplayDialog("Clear Placed Trees",
                $"Delete all {childCount} trees under '{parentName}'? (Ctrl+Z to undo)",
                "Delete", "Cancel"))
            {
                ClearTrees(existing);
            }
        }
        GUI.backgroundColor = prevColor;

        EditorGUILayout.EndScrollView();
    }

    // Surfaces the two things that silently ruin a stroke: prefabs with no
    // renderable mesh, and a size spread wide enough that no fixed distance
    // can serve the whole list.
    private void DrawSizeSummary()
    {
        float smallest = float.MaxValue;
        float largest = 0f;
        int valid = 0;
        var empty = new List<string>();

        foreach (GameObject p in prefabs)
        {
            if (p == null) continue;
            float r = TreeFootprint.Radius(p);
            if (r <= 0.0001f)
            {
                empty.Add(p.name);
                continue;
            }
            valid++;
            smallest = Mathf.Min(smallest, r);
            largest = Mathf.Max(largest, r);
        }

        if (valid > 0)
        {
            float lo = smallest * 2f * Mathf.Min(minScale, maxScale);
            float hi = largest * 2f * Mathf.Max(minScale, maxScale);
            EditorGUILayout.LabelField($"Widths at current scale: {lo:F2} m - {hi:F2} m",
                EditorStyles.miniLabel);
        }

        if (empty.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"{empty.Count} prefab(s) have no renderable mesh and will be skipped: " +
                string.Join(", ", empty), MessageType.Warning);
        }
    }

    private void DrawLoadouts()
    {
        loadoutsExpanded = EditorGUILayout.Foldout(loadoutsExpanded, "Loadouts (Forest Stacks)", true, EditorStyles.foldoutHeader);
        if (!loadoutsExpanded) return;

        if (library == null) LoadLibrary();

        EditorGUI.indentLevel++;

        if (library.loadouts.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No saved loadouts yet. Build a tree list below, type a name, and " +
                "hit Save New to keep it for next time.", MessageType.Info);
        }
        else
        {
            var names = new string[library.loadouts.Count];
            for (int i = 0; i < library.loadouts.Count; i++)
                names[i] = $"{library.loadouts[i].name} ({library.loadouts[i].prefabGuids.Count})";

            selectedLoadout = Mathf.Clamp(selectedLoadout, 0, library.loadouts.Count - 1);
            selectedLoadout = EditorGUILayout.Popup("Saved Loadout", selectedLoadout, names);

            EditorGUILayout.BeginHorizontal();
            bool loadRequested = GUILayout.Button("Load");
            bool overwriteRequested = GUILayout.Button("Overwrite");
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            bool deleteRequested = GUILayout.Button("Delete", GUILayout.Width(70));
            GUI.backgroundColor = prev;
            EditorGUILayout.EndHorizontal();

            Loadout selected = library.loadouts[selectedLoadout];
            if (loadRequested)
            {
                ApplyLoadout(selected);
                GUI.FocusControl(null);
            }
            else if (overwriteRequested && EditorUtility.DisplayDialog("Overwrite Loadout",
                $"Replace '{selected.name}' with the current {CountValidPrefabs()} prefabs and settings?",
                "Overwrite", "Cancel"))
            {
                CaptureInto(selected);
                SaveLibrary();
            }
            else if (deleteRequested && EditorUtility.DisplayDialog("Delete Loadout",
                $"Delete the loadout '{selected.name}'? This can't be undone.",
                "Delete", "Cancel"))
            {
                library.loadouts.RemoveAt(selectedLoadout);
                selectedLoadout = Mathf.Max(0, selectedLoadout - 1);
                SaveLibrary();
            }
        }

        EditorGUILayout.BeginHorizontal();
        newLoadoutName = EditorGUILayout.TextField("New Loadout Name", newLoadoutName);
        GUI.enabled = !string.IsNullOrWhiteSpace(newLoadoutName) && CountValidPrefabs() > 0;
        bool saveNewRequested = GUILayout.Button("Save New", GUILayout.Width(80));
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (saveNewRequested)
        {
            SaveAsNewLoadout(newLoadoutName.Trim());
            GUI.FocusControl(null);
        }

        EditorGUI.indentLevel--;
    }

    private int CountValidPrefabs()
    {
        int valid = 0;
        foreach (var p in prefabs)
            if (p != null) valid++;
        return valid;
    }

    private void HandlePrefabDrop(Rect dropArea)
    {
        Event e = Event.current;
        if (!dropArea.Contains(e.mousePosition)) return;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (Object dragged in DragAndDrop.objectReferences)
            {
                if (dragged is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go) && !prefabs.Contains(go))
                    prefabs.Add(go);
            }
            InvalidateOccupancy();
        }
        e.Use();
    }

    // -------------------------------------------------------------- loadouts

    private void SaveAsNewLoadout(string name)
    {
        if (library == null) LoadLibrary();

        int existingIndex = library.loadouts.FindIndex(l => l.name == name);
        if (existingIndex >= 0)
        {
            if (!EditorUtility.DisplayDialog("Loadout Exists",
                $"A loadout named '{name}' already exists. Replace it?", "Replace", "Cancel"))
                return;
            CaptureInto(library.loadouts[existingIndex]);
            selectedLoadout = existingIndex;
        }
        else
        {
            var loadout = new Loadout { name = name };
            CaptureInto(loadout);
            library.loadouts.Add(loadout);
            selectedLoadout = library.loadouts.Count - 1;
        }

        newLoadoutName = "";
        SaveLibrary();
    }

    private void CaptureInto(Loadout loadout)
    {
        loadout.prefabGuids.Clear();
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null) continue;
            string path = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"'{prefab.name}' isn't a saved project asset, so it can't go in a loadout - skipped.");
                continue;
            }
            if (!loadout.prefabGuids.Contains(guid))
                loadout.prefabGuids.Add(guid);
        }

        loadout.parentName = parentName;
        loadout.maxSlope = maxSlope;
        loadout.count = count;
        loadout.brushRadius = brushRadius;
        loadout.treesPerDab = treesPerDab;
        loadout.minScale = minScale;
        loadout.maxScale = maxScale;
        loadout.spacingMode = (int)spacingMode;
        loadout.canopySpacing = canopySpacing;
        loadout.extraGap = extraGap;
        loadout.minSpacing = minSpacing;
        loadout.dabStep = dabStep;
    }

    private void ApplyLoadout(Loadout loadout)
    {
        prefabs.Clear();
        int missing = 0;
        foreach (string guid in loadout.prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                missing++;
                continue;
            }
            prefabs.Add(prefab);
        }

        parentName = loadout.parentName;
        maxSlope = loadout.maxSlope;
        count = loadout.count;
        brushRadius = loadout.brushRadius;
        treesPerDab = Mathf.Max(1, loadout.treesPerDab);
        minScale = loadout.minScale;
        maxScale = loadout.maxScale;
        spacingMode = (TreeSpacingMode)loadout.spacingMode;
        canopySpacing = loadout.canopySpacing;
        extraGap = loadout.extraGap;
        minSpacing = loadout.minSpacing;
        dabStep = Mathf.Clamp(loadout.dabStep, 0.05f, 1.5f);

        InvalidateOccupancy();

        string missingNote = missing > 0
            ? $" ({missing} prefab(s) no longer exist in the project and were skipped)"
            : "";
        Debug.Log($"Loaded loadout '{loadout.name}' with {prefabs.Count} trees{missingNote}.");
        Repaint();
    }

    private void LoadLibrary()
    {
        library = new LoadoutLibrary();
        try
        {
            if (File.Exists(LoadoutFilePath))
            {
                string json = File.ReadAllText(LoadoutFilePath);
                LoadoutLibrary parsed = JsonUtility.FromJson<LoadoutLibrary>(json);
                if (parsed?.loadouts != null) library = parsed;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Couldn't read tree loadouts from {LoadoutFilePath}: {ex.Message}");
        }

        if (library.loadouts.Count > 0)
            selectedLoadout = Mathf.Clamp(selectedLoadout, 0, library.loadouts.Count - 1);
    }

    private void SaveLibrary()
    {
        try
        {
            string dir = Path.GetDirectoryName(LoadoutFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LoadoutFilePath, JsonUtility.ToJson(library, true));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Couldn't save tree loadouts to {LoadoutFilePath}: {ex.Message}");
        }
    }

    // ----------------------------------------------------------- scene brush

    private readonly List<Vector3> gizmoPositions = new List<Vector3>();
    private readonly List<float> gizmoRadii = new List<float>();

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!paintMode || terrain == null || prefabs.Count == 0) return;

        Event e = Event.current;

        // Handled before the raycast: a stroke that ends off the terrain, or
        // with the cursor over a gizmo, still has to close its undo group.
        if (strokeActive && (e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow))
        {
            EndStroke();
            if (e.type == EventType.MouseUp) e.Use();
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Collider col = terrain.GetComponent<Collider>();
        if (col == null || !col.Raycast(ray, out RaycastHit hit, 5000f))
            return;

        bool erasing = e.control;
        DrawBrush(hit.point, erasing);
        sceneView.Repaint();

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        bool paintDown = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
        bool paintDrag = e.type == EventType.MouseDrag && e.button == 0 && !e.alt;
        if (!paintDown && !paintDrag) return;

        if (paintDown)
        {
            BeginStroke(erasing);
            Dab(hit.point, erasing);
            lastDabPos = hit.point;
        }
        else if (strokeActive)
        {
            // Distance gate. This is the fix for dabs landing on top of each
            // other: the old code fired every 0.08 s regardless of whether the
            // cursor had moved, so a slow drag emptied the brush into one spot.
            float step = Mathf.Max(0.01f, brushRadius * dabStep);
            Vector3 delta = hit.point - lastDabPos;
            delta.y = 0f;
            if (delta.sqrMagnitude >= step * step)
            {
                Dab(hit.point, erasing);
                lastDabPos = hit.point;
            }
        }

        e.Use();
    }

    private void DrawBrush(Vector3 center, bool erasing)
    {
        Handles.color = erasing
            ? new Color(1f, 0.4f, 0.3f, 0.8f)
            : new Color(0.2f, 1f, 0.4f, 0.6f);
        Handles.DrawWireDisc(center, Vector3.up, brushRadius);

        if (!showSpacingGizmos || erasing) return;

        // Draw the footprint each existing tree is claiming. Seeing the discs
        // is what makes the spacing setting legible - otherwise a brush that
        // correctly refuses to place anything just looks broken.
        Transform parent = GameObject.Find(parentName)?.transform;
        if (parent == null) return;
        EnsureOccupancy(parent);

        occupancy.CollectNear(center, brushRadius * 2f, 200, gizmoPositions, gizmoRadii);
        Handles.color = new Color(0.3f, 0.7f, 1f, 0.35f);
        for (int i = 0; i < gizmoPositions.Count; i++)
        {
            float claimed = spacingMode == TreeSpacingMode.FixedDistance
                ? minSpacing * 0.5f
                : gizmoRadii[i] * canopySpacing + extraGap * 0.5f;
            Handles.DrawWireDisc(gizmoPositions[i], Vector3.up, claimed);
        }
    }

    private void BeginStroke(bool erasing)
    {
        Undo.SetCurrentGroupName(erasing ? "Erase Trees" : "Paint Trees");
        strokeUndoGroup = Undo.GetCurrentGroup();
        strokeActive = true;
        strokePlaced = 0;
        strokeRejected = 0;
    }

    private void EndStroke()
    {
        if (!strokeActive) return;
        strokeActive = false;
        Undo.CollapseUndoOperations(strokeUndoGroup);

        // Only worth saying when the stroke did nothing at all - that's the
        // case where the tool looks dead and the reason is just spacing.
        if (strokePlaced == 0 && strokeRejected > 0)
        {
            Debug.Log($"Painted no trees - every spot was inside the spacing of a " +
                      $"neighbour ({strokeRejected} attempts). Lower Canopy Spacing, " +
                      $"or raise Brush Radius to reach open ground.");
        }
    }

    private void Dab(Vector3 center, bool erasing)
    {
        if (erasing) EraseDab(center);
        else PaintDab(center);
    }

    private void EraseDab(Vector3 center)
    {
        Transform parent = GameObject.Find(parentName)?.transform;
        if (parent == null) return;

        float sqr = brushRadius * brushRadius;
        bool removed = false;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            float dx = child.position.x - center.x;
            float dz = child.position.z - center.z;
            if (dx * dx + dz * dz > sqr) continue;
            Undo.DestroyObjectImmediate(child.gameObject);
            removed = true;
        }
        if (removed) InvalidateOccupancy();
    }

    private void PaintDab(Vector3 center)
    {
        var validPrefabs = CollectPlaceablePrefabs();
        if (validPrefabs.Count == 0) return;

        Transform parent = GetOrCreateParent();
        EnsureOccupancy(parent);

        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        // Dart throwing gets harder as the brush fills, so give each tree a
        // budget rather than a flat 10 tries - a nearly-full brush would
        // otherwise place nothing long before it is actually saturated.
        int attemptsPerTree = Mathf.Clamp(12 + treesPerDab * 4, 12, 64);

        for (int i = 0; i < treesPerDab; i++)
        {
            GameObject prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            float scale = Random.Range(minScale, maxScale);
            float radius = TreeFootprint.Radius(prefab) * scale;

            bool placed = false;
            for (int attempt = 0; attempt < attemptsPerTree; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Mathf.Sqrt(Random.value) * brushRadius;
                float worldX = center.x + Mathf.Cos(angle) * dist;
                float worldZ = center.z + Mathf.Sin(angle) * dist;

                if (!IsValidSpot(worldX, worldZ, radius, data, terrainPos))
                {
                    strokeRejected++;
                    continue;
                }

                PlaceOneTree(parent, prefab, scale, radius, worldX, worldZ, terrainPos);
                strokePlaced++;
                placed = true;
                break;
            }

            // Brush is saturated for this size of tree; the rest of the dab
            // would only burn the same attempts again.
            if (!placed) break;
        }
    }

    // ------------------------------------------------------------ scattering

    private void ScatterTrees()
    {
        var validPrefabs = CollectPlaceablePrefabs();
        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("No prefabs with a renderable mesh assigned.");
            return;
        }

        Transform parent = GetOrCreateParent();
        EnsureOccupancy(parent);

        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int placed = 0;
        int skipped = 0;
        const int maxAttemptsPerTree = 30;
        Undo.SetCurrentGroupName("Scatter Trees");
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            float scale = Random.Range(minScale, maxScale);
            float radius = TreeFootprint.Radius(prefab) * scale;

            bool foundSpot = false;
            float finalX = 0f, finalZ = 0f;

            for (int attempt = 0; attempt < maxAttemptsPerTree; attempt++)
            {
                float x = terrainPos.x + Random.Range(0f, data.size.x);
                float z = terrainPos.z + Random.Range(0f, data.size.z);
                if (IsValidSpot(x, z, radius, data, terrainPos))
                {
                    finalX = x;
                    finalZ = z;
                    foundSpot = true;
                    break;
                }
            }

            if (!foundSpot)
            {
                skipped++;
                continue;
            }

            PlaceOneTree(parent, prefab, scale, radius, finalX, finalZ, terrainPos);
            placed++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"Placed {placed} trees under '{parentName}' ({skipped} skipped - no spot " +
                  $"clear of a neighbour within {maxAttemptsPerTree} tries).");
    }

    private List<GameObject> CollectPlaceablePrefabs()
    {
        var result = new List<GameObject>();
        foreach (GameObject p in prefabs)
        {
            if (p == null) continue;
            if (TreeFootprint.Radius(p) <= 0.0001f) continue;
            result.Add(p);
        }
        return result;
    }

    private Transform GetOrCreateParent()
    {
        Transform parent = GameObject.Find(parentName)?.transform;
        if (parent == null)
        {
            parent = new GameObject(parentName).transform;
            Undo.RegisterCreatedObjectUndo(parent.gameObject, "Create Tree Group");
            InvalidateOccupancy();
        }
        return parent;
    }

    private bool IsValidSpot(float worldX, float worldZ, float radius, TerrainData data, Vector3 terrainPos)
    {
        float u = (worldX - terrainPos.x) / data.size.x;
        float v = (worldZ - terrainPos.z) / data.size.z;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

        if (data.GetSteepness(u, v) > maxSlope) return false;

        TreeSpacingRule rule = Rule;
        float searchRange = rule.Required(radius, occupancy.MaxRadius);
        return occupancy.IsClear(worldX, worldZ, radius, searchRange, rule);
    }

    private void PlaceOneTree(Transform parent, GameObject prefab, float scale, float radius,
        float worldX, float worldZ, Vector3 terrainPos)
    {
        float worldY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainPos.y;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.position = new Vector3(worldX, worldY, worldZ);
        instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        instance.transform.localScale = prefab.transform.localScale * scale;

        SnapBaseToGround(instance, worldY);
        Undo.RegisterCreatedObjectUndo(instance, "Place Tree");

        // Insert directly rather than dirtying the grid: a rebuild per placed
        // tree would re-measure the whole forest mid-stroke.
        occupancy.Add(instance.transform.position, radius);
        occupancyChildCount = parent.childCount;
    }

    private void ClearTrees(GameObject existing)
    {
        int childCount = existing.transform.childCount;
        Undo.SetCurrentGroupName("Clear Scattered Trees");
        int undoGroup = Undo.GetCurrentGroup();
        for (int i = existing.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(existing.transform.GetChild(i).gameObject);
        Undo.CollapseUndoOperations(undoGroup);
        InvalidateOccupancy();
        Debug.Log($"Cleared {childCount} trees from '{parentName}'.");
    }

    // Prefab pivots aren't always at the base of the mesh (common when a
    // prefab was extracted from a larger combined source scene), so trust
    // the rendered bounds instead of the pivot to decide how far to shift
    // the object so its lowest point actually touches the ground.
    private static void SnapBaseToGround(GameObject instance, float groundY)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float verticalCorrection = groundY - bounds.min.y;
        instance.transform.position += new Vector3(0f, verticalCorrection, 0f);
    }
}
