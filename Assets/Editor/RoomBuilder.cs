using UnityEditor;
using UnityEngine;

// Grid-based room assembler for modular kit pieces (floor/wall/ceiling tiles).
//
// Sizes are NOT typed in by hand - they're measured directly from each prefab's
// actual mesh bounds (including the prefab root's own scale), and wall placement
// uses the piece's bounding box AFTER rotation/scale to find exact flush contact
// with the floor edge. This works correctly no matter where a piece's pivot point
// sits (center, corner, wherever) since it's derived from real geometry instead of
// guessed tile-size numbers and offset hacks.
//
// Layout conventions:
//   - The walkable floor surface is y = 0. Floor slabs hang below it.
//   - The floor footprint is x in [0, roomWidth], z in [0, roomDepth].
//   - Walls stand just outside that footprint so the whole floor stays walkable.
//   - North/South runs are extended over the ends of the East/West runs so all
//     four corners are sealed - the result is a closed box, not four loose walls.
public class RoomBuilder : EditorWindow
{
    private GameObject floorPrefab;
    private GameObject wallPrefab;
    private GameObject ceilingPrefab;

    private float wallScale = 1f;
    private float seamOverlap = 0f;

    private int widthTiles = 5;
    private int depthTiles = 5;
    private int wallHeightTiles = 2;

    private enum Side { North, South, East, West }
    private Side entranceSide = Side.South;
    private int entranceWidthTiles = 1;
    private int entranceHeightTiles = 1;

    private float wallRotationNorthSouth = 0f;
    private float wallRotationEastWest = 90f;
    private float wallTiltX = 90f;

    private string groupName = "CaveRoom";
    private bool clearBeforeBuilding = true;

    // A run never subdivides below this fraction of a piece, so a silly overlap
    // value can't spiral into thousands of instances.
    private const float MinStepFraction = 0.1f;
    private const int MaxPiecesPerRun = 512;

    private Vector2 scroll;

    [MenuItem("Tools/Building/Room Builder")]
    public static void Open()
    {
        GetWindow<RoomBuilder>("Room Builder");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
        floorPrefab = (GameObject)EditorGUILayout.ObjectField("Floor", floorPrefab, typeof(GameObject), false);
        wallPrefab = (GameObject)EditorGUILayout.ObjectField("Wall", wallPrefab, typeof(GameObject), false);
        ceilingPrefab = (GameObject)EditorGUILayout.ObjectField("Ceiling (optional)", ceilingPrefab, typeof(GameObject), false);

        EditorGUILayout.Space();
        wallScale = EditorGUILayout.FloatField("Wall Scale Multiplier", wallScale);
        seamOverlap = Mathf.Max(0f, EditorGUILayout.FloatField("Seam Overlap", seamOverlap));
        EditorGUILayout.HelpBox(
            "Seam Overlap sinks every piece into its neighbour by this many world " +
            "units, on all three axes and for floors, walls and ceilings alike. " +
            "Runs are re-fitted around it, so raising it tightens the seams " +
            "without opening a gap at the end of a wall or lifting the ceiling " +
            "off the wall tops. Try 0.02-0.1 for hairline seams.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Room Size (in floor tiles)", EditorStyles.boldLabel);
        widthTiles = Mathf.Max(1, EditorGUILayout.IntField("Width", widthTiles));
        depthTiles = Mathf.Max(1, EditorGUILayout.IntField("Depth", depthTiles));
        wallHeightTiles = Mathf.Max(1, EditorGUILayout.IntField("Wall Height (wall tiles)", wallHeightTiles));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Entrance", EditorStyles.boldLabel);
        entranceSide = (Side)EditorGUILayout.EnumPopup("Side", entranceSide);
        entranceWidthTiles = Mathf.Max(0, EditorGUILayout.IntField("Width (wall tiles)", entranceWidthTiles));
        entranceHeightTiles = Mathf.Clamp(EditorGUILayout.IntField("Height (wall tiles)", entranceHeightTiles), 1, wallHeightTiles);
        EditorGUILayout.HelpBox(
            "Set Width to 0 for a fully sealed box. Height below Wall Height " +
            "leaves the courses above the opening in place, so the doorway gets a " +
            "lintel instead of slicing the wall open to the roof.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Wall Rotation (degrees)", EditorStyles.boldLabel);
        wallRotationNorthSouth = EditorGUILayout.FloatField("North/South Walls", wallRotationNorthSouth);
        wallRotationEastWest = EditorGUILayout.FloatField("East/West Walls", wallRotationEastWest);
        wallTiltX = EditorGUILayout.FloatField("Wall Tilt X (edge-up)", wallTiltX);
        EditorGUILayout.HelpBox(
            "Leave Tilt at 0 for a piece that's already upright (like a proper " +
            "Cliff wall block). Set to 90 to tip a flat piece (like a reused " +
            "Floor prefab) up onto its edge. If walls face the wrong way, try " +
            "90/180/270 on the rotation fields.", MessageType.None);

        EditorGUILayout.Space();
        groupName = EditorGUILayout.TextField("Group Name", groupName);
        clearBeforeBuilding = EditorGUILayout.Toggle("Clear Before Building", clearBeforeBuilding);

        EditorGUILayout.Space();
        bool ready = DrawMeasurements();

        EditorGUILayout.Space();
        GUI.enabled = ready;
        if (GUILayout.Button("Build Room", GUILayout.Height(30)))
            BuildRoom();
        GUI.enabled = true;

        EditorGUILayout.EndScrollView();
    }

    // Live readout of what the tool has actually measured and what it will
    // build, so a bad prefab or a bad rotation is obvious before pressing Build.
    private bool DrawMeasurements()
    {
        EditorGUILayout.LabelField("Measured", EditorStyles.boldLabel);

        if (floorPrefab == null || wallPrefab == null)
        {
            EditorGUILayout.HelpBox("Assign at least a Floor and a Wall prefab.", MessageType.Info);
            return false;
        }
        if (!HasMesh(floorPrefab) || !HasMesh(wallPrefab) || (ceilingPrefab != null && !HasMesh(ceilingPrefab)))
        {
            EditorGUILayout.HelpBox(
                "One of the assigned prefabs has no MeshFilter with a mesh, so it " +
                "can't be measured. Assign prefabs that contain actual geometry.",
                MessageType.Error);
            return false;
        }

        Layout layout = Solve();
        EditorGUILayout.HelpBox(
            $"Floor tile: {layout.floor.size.x:F3} x {layout.floor.size.y:F3} x {layout.floor.size.z:F3}\n" +
            $"Wall tile (N/S, after rotation+scale): {layout.ns.size.x:F3} x {layout.ns.size.y:F3} x {layout.ns.size.z:F3}\n" +
            $"Room interior: {layout.roomWidth:F3} x {layout.roomDepth:F3}, wall top at y {layout.wallTopY:F3}\n" +
            $"Walls per run: {layout.nsCount} (N/S), {layout.ewCount} (E/W), {wallHeightTiles} courses high",
            MessageType.None);

        if (layout.nsCount >= MaxPiecesPerRun || layout.ewCount >= MaxPiecesPerRun)
            EditorGUILayout.HelpBox(
                $"A wall run hit the {MaxPiecesPerRun}-piece cap, so the seams will " +
                "overlap by less than requested. The wall piece is tiny relative to " +
                "the room - raise Wall Scale Multiplier or lower Seam Overlap.",
                MessageType.Warning);

        if (entranceWidthTiles > 0)
        {
            int runCount = (entranceSide == Side.North || entranceSide == Side.South) ? layout.nsCount : layout.ewCount;
            if (entranceWidthTiles >= runCount)
                EditorGUILayout.HelpBox(
                    $"Entrance width {entranceWidthTiles} would remove the whole " +
                    $"{entranceSide} run ({runCount} pieces); it will be capped at {runCount - 1}.",
                    MessageType.Warning);
        }
        return true;
    }

    private Quaternion BuildWallRotation(float yaw)
    {
        return Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(wallTiltX, 0f, 0f);
    }

    // Everything the builder needs, solved once so the GUI preview and the build
    // can never disagree about what is going to be placed.
    private struct Layout
    {
        public float overlap;
        public Bounds floor;
        public Bounds ns;     // wall bounds in North/South orientation
        public Bounds ew;     // wall bounds in East/West orientation
        public Quaternion nsRotation, ewRotation;
        public float floorStepX, floorStepZ;
        public float roomWidth, roomDepth;
        public float wallStepY, wallTopY;
        public float nsInset, ewInset;   // how far each run sinks into the floor edge
        public int nsCount, ewCount;
        public float nsRunStart, nsRunSpan, nsStep;
        public float ewRunStart, ewRunSpan, ewStep;
    }

    private Layout Solve()
    {
        Layout l = new Layout();
        l.overlap = Mathf.Max(0f, seamOverlap);

        l.floor = GetPrefabBounds(floorPrefab, 1f);
        l.nsRotation = BuildWallRotation(wallRotationNorthSouth);
        l.ewRotation = BuildWallRotation(wallRotationEastWest);
        Bounds wallLocal = GetPrefabBounds(wallPrefab, wallScale);
        l.ns = RotateBounds(wallLocal, l.nsRotation);
        l.ew = RotateBounds(wallLocal, l.ewRotation);

        // The floor grid defines the room, and it overlaps too - so the room's
        // real size shrinks slightly as the overlap grows. Everything downstream
        // is fitted to these numbers rather than to widthTiles * tileSize.
        l.floorStepX = Step(l.floor.size.x, l.overlap);
        l.floorStepZ = Step(l.floor.size.z, l.overlap);
        l.roomWidth = (widthTiles - 1) * l.floorStepX + l.floor.size.x;
        l.roomDepth = (depthTiles - 1) * l.floorStepZ + l.floor.size.z;

        l.wallStepY = Step(l.ns.size.y, l.overlap);
        l.wallTopY = (wallHeightTiles - 1) * l.wallStepY + l.ns.size.y;

        // Each run sinks into the floor edge by the overlap, but never by more
        // than its own thickness - otherwise a big overlap value would drive the
        // walls straight through the room instead of just closing a seam.
        float ewThickness = l.ew.size.x;
        l.nsInset = EffectiveOverlap(l.ns.size.z, l.overlap);
        l.ewInset = EffectiveOverlap(ewThickness, l.overlap);

        // North/South runs stretch over the outer faces of the East/West runs so
        // the four corners are covered; East/West runs only span the interior.
        l.nsRunStart = l.ewInset - ewThickness;
        l.nsRunSpan = l.roomWidth + 2f * ewThickness - 2f * l.ewInset;
        l.ewRunStart = 0f;
        l.ewRunSpan = l.roomDepth;

        l.nsCount = CountForSpan(l.nsRunSpan, l.ns.size.x, l.overlap);
        l.ewCount = CountForSpan(l.ewRunSpan, l.ew.size.z, l.overlap);
        l.nsStep = StepForSpan(l.nsRunSpan, l.ns.size.x, l.nsCount);
        l.ewStep = StepForSpan(l.ewRunSpan, l.ew.size.z, l.ewCount);
        return l;
    }

    private void BuildRoom()
    {
        Layout l = Solve();

        Undo.SetCurrentGroupName("Build Room");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject parentObj = GameObject.Find(groupName);
        if (parentObj != null && clearBeforeBuilding)
        {
            for (int i = parentObj.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parentObj.transform.GetChild(i).gameObject);
        }
        if (parentObj == null)
        {
            parentObj = new GameObject(groupName);
            Undo.RegisterCreatedObjectUndo(parentObj, "Build Room");
        }
        Transform parent = parentObj.transform;

        // Floor - top surface sits on y = 0, so the walls have a clean base line.
        for (int x = 0; x < widthTiles; x++)
        {
            for (int z = 0; z < depthTiles; z++)
            {
                Vector3 pos = new Vector3(
                    x * l.floorStepX - l.floor.min.x,
                    -l.floor.max.y,
                    z * l.floorStepZ - l.floor.min.z);
                PlacePiece(floorPrefab, pos, Quaternion.identity, 1f, parent, $"Floor_{x}_{z}");
            }
        }

        PlaceWallRun(l, Side.South, parent);
        PlaceWallRun(l, Side.North, parent);
        PlaceWallRun(l, Side.West, parent);
        PlaceWallRun(l, Side.East, parent);

        if (ceilingPrefab != null)
            PlaceCeiling(l, parent);

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log(
            $"Built a {widthTiles}x{depthTiles} room ({l.roomWidth:F2} x {l.roomDepth:F2}) " +
            $"under '{groupName}' with {l.overlap:F3} seam overlap.", parentObj);
    }

    private void PlaceWallRun(Layout l, Side side, Transform parent)
    {
        bool isNorthSouth = side == Side.North || side == Side.South;
        Bounds b = isNorthSouth ? l.ns : l.ew;
        Quaternion rotation = isNorthSouth ? l.nsRotation : l.ewRotation;
        int count = isNorthSouth ? l.nsCount : l.ewCount;
        float runStart = isNorthSouth ? l.nsRunStart : l.ewRunStart;
        float runSpan = isNorthSouth ? l.nsRunSpan : l.ewRunSpan;
        float pieceRun = isNorthSouth ? b.size.x : b.size.z;
        float step = isNorthSouth ? l.nsStep : l.ewStep;

        int gapHeight = Mathf.Clamp(entranceHeightTiles, 1, wallHeightTiles);

        // Never let the entrance eat the entire run, or the box loses a wall.
        int gapStart = int.MaxValue, gapEnd = int.MinValue;
        if (side == entranceSide && entranceWidthTiles > 0 && count > 1)
        {
            int gapWidth = Mathf.Min(entranceWidthTiles, count - 1);
            gapStart = Mathf.Clamp((count - gapWidth) / 2, 0, count - gapWidth);
            gapEnd = gapStart + gapWidth - 1;
        }

        for (int i = 0; i < count; i++)
        {
            float along = count > 1 ? runStart + i * step : runStart + (runSpan - pieceRun) * 0.5f;

            for (int h = 0; h < wallHeightTiles; h++)
            {
                if (i >= gapStart && i <= gapEnd && h < gapHeight) continue;

                float up = h * l.wallStepY - b.min.y;
                Vector3 pos;
                switch (side)
                {
                    case Side.South:
                        pos = new Vector3(along - b.min.x, up, l.nsInset - b.max.z);
                        break;
                    case Side.North:
                        pos = new Vector3(along - b.min.x, up, l.roomDepth - l.nsInset - b.min.z);
                        break;
                    case Side.West:
                        pos = new Vector3(l.ewInset - b.max.x, up, along - b.min.z);
                        break;
                    default: // East
                        pos = new Vector3(l.roomWidth - l.ewInset - b.min.x, up, along - b.min.z);
                        break;
                }
                PlacePiece(wallPrefab, pos, rotation, wallScale, parent, $"Wall_{side}_{i}_{h}");
            }
        }
    }

    // Tiled from the ceiling piece's own measured size rather than borrowing the
    // floor's, so a mismatched ceiling kit still covers the room exactly.
    private void PlaceCeiling(Layout l, Transform parent)
    {
        Bounds c = GetPrefabBounds(ceilingPrefab, 1f);
        int countX = CountForSpan(l.roomWidth, c.size.x, l.overlap);
        int countZ = CountForSpan(l.roomDepth, c.size.z, l.overlap);
        float stepX = StepForSpan(l.roomWidth, c.size.x, countX);
        float stepZ = StepForSpan(l.roomDepth, c.size.z, countZ);

        // Drop the ceiling onto the wall tops by the overlap, but never so far
        // that it sinks past the base of the top course.
        float y = l.wallTopY - EffectiveOverlap(Mathf.Min(l.ns.size.y, c.size.y), l.overlap) - c.min.y;

        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                Vector3 pos = new Vector3(
                    (countX > 1 ? x * stepX : (l.roomWidth - c.size.x) * 0.5f) - c.min.x,
                    y,
                    (countZ > 1 ? z * stepZ : (l.roomDepth - c.size.z) * 0.5f) - c.min.z);
                PlacePiece(ceilingPrefab, pos, Quaternion.identity, 1f, parent, $"Ceiling_{x}_{z}");
            }
        }
    }

    private void PlacePiece(GameObject prefab, Vector3 position, Quaternion rotation, float scale, Transform parent, string name)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.localPosition = position;
        instance.transform.localRotation = rotation;
        instance.transform.localScale = prefab.transform.localScale * scale;
        Undo.RegisterCreatedObjectUndo(instance, "Build Room");
    }

    // An overlap can never eat more than most of the dimension it's sinking
    // into, so an oversized value degrades gracefully instead of turning the
    // layout inside out.
    private static float EffectiveOverlap(float size, float overlap)
    {
        return Mathf.Clamp(overlap, 0f, size * (1f - MinStepFraction));
    }

    // Distance from one piece to the next once the overlap is taken out.
    private static float Step(float size, float overlap)
    {
        return size - EffectiveOverlap(size, overlap);
    }

    // How many pieces it takes to cover 'span' when each pair overlaps by at
    // least 'overlap'. Rounds up - rounding down is what used to leave the last
    // stretch of every wall missing.
    private static int CountForSpan(float span, float size, float overlap)
    {
        if (size <= Mathf.Epsilon) return 1;
        int count = Mathf.CeilToInt((span - size) / Step(size, overlap) - 0.0001f) + 1;
        return Mathf.Clamp(count, 1, MaxPiecesPerRun);
    }

    // Even spacing that makes the run start at 0 and end exactly at 'span'. Any
    // slack from rounding the count up is shared out as extra overlap, so the run
    // is flush at both ends instead of overshooting or falling short.
    private static float StepForSpan(float span, float size, int count)
    {
        return count > 1 ? (span - size) / (count - 1) : 0f;
    }

    private static bool HasMesh(GameObject prefab)
    {
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            if (filter.sharedMesh != null) return true;
        return false;
    }

    // Combines every MeshFilter under the prefab into one bounds expressed in the
    // prefab root's space, WITH the root's own localScale and the extra multiplier
    // baked in - i.e. the size the piece will actually occupy once placed. Leaving
    // the root scale out is what made scaled prefabs lay out wrong.
    private static Bounds GetPrefabBounds(GameObject prefab, float extraScale)
    {
        var filters = prefab.GetComponentsInChildren<MeshFilter>();
        Bounds combined = new Bounds();
        bool started = false;

        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = TransformMeshBoundsToRoot(filter, prefab.transform);
            if (!started) { combined = b; started = true; }
            else combined.Encapsulate(b);
        }
        if (!started) return new Bounds(Vector3.zero, Vector3.one);

        Vector3 scale = prefab.transform.localScale * extraScale;
        Vector3 a = Vector3.Scale(combined.min, scale);
        Vector3 c = Vector3.Scale(combined.max, scale);
        Bounds result = new Bounds();
        result.SetMinMax(Vector3.Min(a, c), Vector3.Max(a, c));
        return result;
    }

    private static Bounds TransformMeshBoundsToRoot(MeshFilter filter, Transform root)
    {
        Bounds local = filter.sharedMesh.bounds;
        Vector3 min = local.min, max = local.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
        };

        Bounds result = new Bounds(root.InverseTransformPoint(filter.transform.TransformPoint(corners[0])), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            result.Encapsulate(root.InverseTransformPoint(filter.transform.TransformPoint(corners[i])));
        return result;
    }

    // The bounding box of an already-scaled box AFTER a rotation - this is what
    // lets wall placement fit flush regardless of pivot location or facing.
    private static Bounds RotateBounds(Bounds bounds, Quaternion rotation)
    {
        Vector3 min = bounds.min, max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
        };

        Vector3 rMin = rotation * corners[0];
        Vector3 rMax = rMin;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 p = rotation * corners[i];
            rMin = Vector3.Min(rMin, p);
            rMax = Vector3.Max(rMax, p);
        }
        Bounds result = new Bounds();
        result.SetMinMax(rMin, rMax);
        return result;
    }
}
