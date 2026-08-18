using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Reseats trees TreeScatter has already placed, so no part of a trunk base
// hangs above the terrain.
//
// TreeScatter seats a tree by dropping the lowest point of its whole mesh onto
// the terrain height sampled at the pivot (SnapBaseToGround). That is correct
// only when the pivot stands over the trunk and the mesh holds a single tree.
// In this project neither always holds:
//
//   - The pivot is not over the trunk. These prefabs carry the trunk a couple
//     of metres from the pivot horizontally, so the height sampled at the pivot
//     is not the height under the wood. On a slope the two differ by the
//     gradient times that offset, and the downhill side of the base lifts clear
//     of the ground.
//   - One mesh can hold more than one tree. "LowPoly Tree .021" is a grove of
//     17 trunks welded into a single mesh, so seating its lowest point leaves
//     the other 16 hanging - the worst of them by about seven metres.
//
// The rule here is therefore measured per connected mesh island, and against
// the terrain sampled under each base vertex rather than under the pivot: drop
// the instance until the highest point of its contact rim meets the ground
// beneath it.
//
// Trees only ever move down. Lifting one to meet its deepest buried vertex
// would strand the rest of its base in the air, and a trunk running a little
// way into a bank reads as rooted - which is the direction this error should
// fall when it cannot be zero.

// What a prefab looks like from underneath, cached per prefab because every
// instance of one shares it.
internal sealed class TreeBaseProfile
{
    // Vertices in the prefab root's local space, so an instance maps them into
    // world space with its own localToWorldMatrix and needs nothing else.
    public Vector3[] rootLocal;

    // One contact rim per connected island - the vertices low enough in that
    // island to be what meets the ground.
    public readonly List<int[]> rims = new List<int[]>();
}

internal static class TreeBaseFootprint
{
    // The bottom slice of an island counted as its contact rim. Small on
    // purpose: it wants the ring of wood the trunk stands on, not the low
    // branches, which sit at a similar height but reach far enough sideways
    // that including them would sink the tree by the slope across the canopy.
    private const float RimFraction = 0.02f;

    private static readonly Dictionary<GameObject, TreeBaseProfile> cache =
        new Dictionary<GameObject, TreeBaseProfile>();

    public static void ClearCache() => cache.Clear();

    public static TreeBaseProfile Of(GameObject prefab)
    {
        if (prefab == null) return null;
        if (cache.TryGetValue(prefab, out TreeBaseProfile cached)) return cached;
        TreeBaseProfile measured = Measure(prefab);
        cache[prefab] = measured;
        return measured;
    }

    private static TreeBaseProfile Measure(GameObject prefab)
    {
        Transform root = prefab.transform;

        // Two frames are needed. Root-local is what an instance transform
        // consumes. Placement space adds the prefab root's own rotation and
        // scale, which is the frame the tree actually stands up in - packs
        // whose source art is not Y-up carry that rotation on the root, so
        // "low" has to be judged after it, exactly as TreeFootprint does.
        Matrix4x4 toPlacement = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale);

        var rootLocal = new List<Vector3>();
        var placement = new List<Vector3>();
        var triangles = new List<int>();

        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            UnityEngine.Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            // Imported meshes are commonly not marked read/write. The editor
            // still hands back their data, but guard anyway rather than take
            // the whole pass down over one awkward asset.
            Vector3[] vertices;
            int[] indices;
            try
            {
                vertices = mesh.vertices;
                indices = mesh.triangles;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Cannot read mesh '{mesh.name}' on '{prefab.name}': {e.Message}");
                continue;
            }

            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            int offset = rootLocal.Count;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(vertices[i]);
                rootLocal.Add(p);
                placement.Add(toPlacement.MultiplyPoint3x4(p));
            }
            for (int i = 0; i < indices.Length; i++) triangles.Add(indices[i] + offset);
        }

        var profile = new TreeBaseProfile { rootLocal = rootLocal.ToArray() };
        if (rootLocal.Count == 0) return profile;

        foreach (List<int> island in Islands(rootLocal, triangles))
        {
            float lowest = island.Min(i => placement[i].y);
            float highest = island.Max(i => placement[i].y);
            float cut = lowest + (highest - lowest) * RimFraction;

            int[] rim = island.Where(i => placement[i].y <= cut).ToArray();
            if (rim.Length == 0) rim = new[] { island.OrderBy(i => placement[i].y).First() };
            profile.rims.Add(rim);
        }
        return profile;
    }

    // Connected components over the triangle graph, so a mesh holding several
    // trees is read as several trees. Vertices are welded by position first:
    // a UV or normal seam splits one trunk into two index sets that share no
    // triangle, and without the weld each half would be seated on its own.
    private static List<List<int>> Islands(List<Vector3> positions, List<int> triangles)
    {
        int count = positions.Count;
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;

        var welded = new Dictionary<(long, long, long), int>(count);
        for (int i = 0; i < count; i++)
        {
            var key = ((long)Mathf.RoundToInt(positions[i].x * 10000f),
                       (long)Mathf.RoundToInt(positions[i].y * 10000f),
                       (long)Mathf.RoundToInt(positions[i].z * 10000f));
            if (!welded.TryGetValue(key, out int first)) { first = i; welded[key] = first; }
            Union(parent, i, first);
        }

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Union(parent, triangles[i], triangles[i + 1]);
            Union(parent, triangles[i + 1], triangles[i + 2]);
        }

        var islands = new Dictionary<int, List<int>>();
        for (int i = 0; i < count; i++)
        {
            int rep = Find(parent, i);
            if (!islands.TryGetValue(rep, out List<int> members))
            {
                members = new List<int>();
                islands[rep] = members;
            }
            members.Add(i);
        }
        return islands.Values.ToList();
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b) parent[a] = b;
    }
}

public static class TreeReseat
{
    private const string GroupName = "ScatteredTrees";

    // Movement below this is not worth dirtying a transform for - it is inside
    // the error of the heightfield sample itself.
    private const float Tolerance = 0.02f;

    [MenuItem("Tools/Reseat Trees On Terrain")]
    private static void ReseatTrees() => Run(false);

    [MenuItem("Tools/Reseat Trees On Terrain (Report Only)")]
    private static void ReportTrees() => Run(true);

    private static void Run(bool reportOnly)
    {
        GameObject group = GameObject.Find(GroupName);
        if (group == null)
        {
            Debug.LogError($"No '{GroupName}' group in the scene.");
            return;
        }

        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (terrains.Length == 0)
        {
            Debug.LogError("No terrain in the scene to seat against.");
            return;
        }

        TreeBaseFootprint.ClearCache();

        Undo.SetCurrentGroupName("Reseat Trees On Terrain");
        int undoGroup = Undo.GetCurrentGroup();

        int moved = 0, seated = 0, unmeasured = 0;
        float worst = 0f, total = 0f;
        string worstName = null;

        foreach (Transform tree in group.transform)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(tree.gameObject);
            GameObject prefab = source != null ? source.transform.root.gameObject : tree.gameObject;

            TreeBaseProfile profile = TreeBaseFootprint.Of(prefab);
            if (profile == null || profile.rims.Count == 0 || !Drop(profile, tree, terrains, out float drop))
            {
                unmeasured++;
                continue;
            }

            if (drop <= Tolerance)
            {
                seated++;
                continue;
            }

            if (!reportOnly)
            {
                Undo.RecordObject(tree, "Reseat Trees On Terrain");
                tree.position -= new Vector3(0f, drop, 0f);
            }

            moved++;
            total += drop;
            if (drop > worst) { worst = drop; worstName = tree.name; }
        }

        if (!reportOnly) Undo.CollapseUndoOperations(undoGroup);

        string verb = reportOnly ? "would drop" : "dropped";
        Debug.Log($"Reseat trees: {verb} {moved} of {group.transform.childCount} under '{GroupName}' " +
                  $"(already seated {seated}, unmeasured {unmeasured}). " +
                  $"Average drop {(moved > 0 ? total / moved : 0f):F2} m, worst {worst:F2} m" +
                  (worstName != null ? $" on '{worstName}'." : "."));
    }

    // How far this instance has to come down for its contact rims to meet the
    // ground under them.
    private static bool Drop(TreeBaseProfile profile, Transform instance, Terrain[] terrains, out float drop)
    {
        drop = 0f;
        Matrix4x4 toWorld = instance.localToWorldMatrix;
        var perIsland = new List<float>(profile.rims.Count);

        foreach (int[] rim in profile.rims)
        {
            float highest = float.MinValue;
            foreach (int i in rim)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(profile.rootLocal[i]);
                if (!Ground(terrains, world, out float groundY)) continue;
                float clearance = world.y - groundY;
                if (clearance > highest) highest = clearance;
            }
            if (highest > float.MinValue) perIsland.Add(highest);
        }

        if (perIsland.Count == 0) return false;

        // One island is one tree, and its rim has to reach the ground. A mesh
        // holding several cannot be seated that way: the trunks sit at
        // different heights in the source art, so no single move lands them
        // all. The median splits the difference, leaving about as many trunks
        // slightly buried as slightly proud, which reads far better than
        // hanging the whole grove off whichever trunk happens to reach lowest.
        perIsland.Sort();
        drop = perIsland[perIsland.Count / 2];
        return true;
    }

    // Terrains do not overlap, so the first one covering this column owns it.
    private static bool Ground(Terrain[] terrains, Vector3 world, out float y)
    {
        y = 0f;
        foreach (Terrain terrain in terrains)
        {
            if (terrain.terrainData == null) continue;
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (world.x < origin.x || world.x > origin.x + size.x) continue;
            if (world.z < origin.z || world.z > origin.z + size.z) continue;
            y = terrain.SampleHeight(world) + origin.y;
            return true;
        }
        return false;
    }
}
