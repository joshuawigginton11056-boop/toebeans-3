using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The spacing model behind TreeScatter, kept out of the window so it can be
// exercised without an IMGUI event loop (see TreeScatterTests).
//
// The rule is measured between canopy edges, not between pivots. A single
// pivot distance cannot serve a mixed prefab list: the dead-tree pack runs
// from 0.34 m wide to 2.31 m wide, so any one number either buries the big
// trees or strands the small ones.
internal enum TreeSpacingMode
{
    Canopy = 0,
    FixedDistance = 1,
}

internal struct TreeSpacingRule
{
    public TreeSpacingMode mode;
    // 1.0 = canopies just touch. Below that they interlock (thicket), above it
    // they stand off with clear ground between.
    public float canopySpacing;
    public float extraGap;
    public float fixedDistance;

    public float Required(float radiusA, float radiusB)
    {
        if (mode == TreeSpacingMode.FixedDistance) return fixedDistance;
        return (radiusA + radiusB) * canopySpacing + extraGap;
    }
}

// Which of a prefab's own transform the footprint is measured through. It has
// to match how the placement orients the instance, or the numbers describe a
// shape that never gets placed.
internal enum FootprintSpace
{
    // The root's rotation stripped. Tree placement discards that rotation, so
    // the footprint does too. The dead-tree pack's roots hold the pose each
    // tree had in the scene it was extracted from - one carries a 51 degree
    // yaw - and honouring scene leftovers would only inflate the spacing.
    RootLocal = 0,

    // The root's rotation applied. Prop placement composes with it, because
    // models authored Z-up are stood upright by a -90 degree X rotation on the
    // prefab root and nothing else. Measured in root-local space, such a
    // prefab reports its depth where its height belongs - and no amount of
    // correct seating maths recovers from that.
    Prefab = 1,
}

// Footprint of a prefab at scale 1, in world metres.
internal struct TreeMeasurement
{
    // Half the wider horizontal axis of the mesh bounds.
    public float radius;
    // How far the lowest mesh point sits below the pivot. Positive when the
    // mesh hangs below the pivot, negative when it floats above it. Adding
    // this along the placement up-axis puts the base exactly on the ground,
    // which is what upright placement gets from an axis-aligned bounds test
    // but a tilted prop cannot.
    public float baseOffset;
}

internal static class TreeFootprint
{
    // One cache per space - the same prefab measures differently in each, and
    // a brush switching modes must not be handed the other mode's numbers.
    private static readonly Dictionary<GameObject, TreeMeasurement>[] caches =
    {
        new Dictionary<GameObject, TreeMeasurement>(),
        new Dictionary<GameObject, TreeMeasurement>(),
    };

    public static void ClearCache()
    {
        foreach (Dictionary<GameObject, TreeMeasurement> cache in caches) cache.Clear();
    }

    public static float Radius(GameObject prefab, FootprintSpace space) => Of(prefab, space).radius;

    public static float BaseOffset(GameObject prefab, FootprintSpace space) =>
        Of(prefab, space).baseOffset;

    public static TreeMeasurement Of(GameObject prefab, FootprintSpace space)
    {
        if (prefab == null) return default;
        Dictionary<GameObject, TreeMeasurement> cache = caches[(int)space];
        if (cache.TryGetValue(prefab, out TreeMeasurement cached)) return cached;
        TreeMeasurement measured = Measure(prefab, space);
        cache[prefab] = measured;
        return measured;
    }

    // Measured from mesh bounds rather than Renderer.bounds so it needs no live
    // instance and can't read back stale for a frame after a transform change.
    private static TreeMeasurement Measure(GameObject prefab, FootprintSpace space)
    {
        Transform root = prefab.transform;

        // Both spaces drop the root's position, putting the pivot at the
        // origin, and both bake in its scale so the result is world metres at
        // prefab scale. They differ only in the root's rotation: applied for
        // Prefab, stripped for RootLocal - which is exactly how the two
        // placement paths differ.
        Quaternion rotation = space == FootprintSpace.Prefab ? root.localRotation : Quaternion.identity;
        Matrix4x4 toFootprint = Matrix4x4.TRS(Vector3.zero, rotation, root.localScale)
                                * root.worldToLocalMatrix;
        bool any = false;
        Bounds bounds = new Bounds();

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            EncapsulateTransformed(ref bounds, ref any, mf.sharedMesh.bounds,
                toFootprint * mf.transform.localToWorldMatrix);
        }
        foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            EncapsulateTransformed(ref bounds, ref any, smr.sharedMesh.bounds,
                toFootprint * smr.transform.localToWorldMatrix);
        }

        if (!any) return default;

        // Already in world metres - the root's scale went in with the matrix.
        // Trees get a random Y rotation, so the wider horizontal axis is the one
        // that can end up facing a neighbour - take that as the radius.
        return new TreeMeasurement
        {
            radius = 0.5f * Mathf.Max(bounds.size.x, bounds.size.z),
            baseOffset = -bounds.min.y,
        };
    }

    private static void EncapsulateTransformed(ref Bounds acc, ref bool any, Bounds b, Matrix4x4 m)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z));
            Vector3 p = m.MultiplyPoint3x4(corner);
            if (!any)
            {
                acc = new Bounds(p, Vector3.zero);
                any = true;
            }
            else
            {
                acc.Encapsulate(p);
            }
        }
    }

    // Footprint of a tree already in the scene. These have settled transforms,
    // so world renderer bounds are both valid and cheaper than re-deriving from
    // the prefab plus its instance scale.
    public static float InstanceRadius(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return 0.5f * Mathf.Max(b.size.x, b.size.z);
    }
}

// A uniform grid over what's already planted. The old code rebuilt a flat list
// and scanned it linearly on every placement attempt - a full O(n) sweep per
// dart thrown, which costs most exactly when a forest is getting dense and the
// brush most needs to stay responsive.
//
// Cell size is a performance knob only: the search span is derived from the
// caller's search range, so a badly sized cell costs time, never correctness.
internal sealed class TreeOccupancyGrid
{
    private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
    private readonly List<Vector3> positions = new List<Vector3>();
    private readonly List<float> radii = new List<float>();
    private float cellSize = 4f;

    public int Count => positions.Count;
    public float MaxRadius { get; private set; }

    public void Reset(float suggestedCellSize)
    {
        cells.Clear();
        positions.Clear();
        radii.Clear();
        MaxRadius = 0f;
        cellSize = Mathf.Max(0.5f, suggestedCellSize);
    }

    public void Add(Vector3 p, float radius)
    {
        int index = positions.Count;
        positions.Add(p);
        radii.Add(radius);
        if (radius > MaxRadius) MaxRadius = radius;

        long key = Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
        if (!cells.TryGetValue(key, out List<int> bucket))
        {
            bucket = new List<int>();
            cells[key] = bucket;
        }
        bucket.Add(index);
    }

    private static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;

    // True when nothing already planted sits closer than the rule allows.
    // searchRange must cover the largest distance the rule can demand of this
    // candidate - Required(radius, MaxRadius) - or neighbours sitting outside
    // the scanned cells get missed.
    public bool IsClear(float x, float z, float candidateRadius, float searchRange, TreeSpacingRule rule)
    {
        if (positions.Count == 0) return true;

        int span = Mathf.Max(1, Mathf.CeilToInt(searchRange / cellSize));
        int cx = Mathf.FloorToInt(x / cellSize);
        int cz = Mathf.FloorToInt(z / cellSize);

        for (int gx = cx - span; gx <= cx + span; gx++)
        {
            for (int gz = cz - span; gz <= cz + span; gz++)
            {
                if (!cells.TryGetValue(Key(gx, gz), out List<int> bucket)) continue;
                foreach (int i in bucket)
                {
                    float need = rule.Required(candidateRadius, radii[i]);
                    if (need <= 0f) continue;
                    float dx = x - positions[i].x;
                    float dz = z - positions[i].z;
                    if (dx * dx + dz * dz < need * need) return false;
                }
            }
        }
        return true;
    }

    // Brute-force equivalent of IsClear, for the self-test to check the grid
    // against. Never used by the brush.
    public bool IsClearBruteForce(float x, float z, float candidateRadius, TreeSpacingRule rule)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            float need = rule.Required(candidateRadius, radii[i]);
            if (need <= 0f) continue;
            float dx = x - positions[i].x;
            float dz = z - positions[i].z;
            if (dx * dx + dz * dz < need * need) return false;
        }
        return true;
    }

    public void CollectNear(Vector3 center, float range, int limit,
        List<Vector3> outPositions, List<float> outRadii)
    {
        outPositions.Clear();
        outRadii.Clear();
        float sqr = range * range;
        int span = Mathf.Max(1, Mathf.CeilToInt(range / cellSize));
        int cx = Mathf.FloorToInt(center.x / cellSize);
        int cz = Mathf.FloorToInt(center.z / cellSize);

        for (int gx = cx - span; gx <= cx + span; gx++)
        {
            for (int gz = cz - span; gz <= cz + span; gz++)
            {
                if (!cells.TryGetValue(Key(gx, gz), out List<int> bucket)) continue;
                foreach (int i in bucket)
                {
                    float dx = center.x - positions[i].x;
                    float dz = center.z - positions[i].z;
                    if (dx * dx + dz * dz > sqr) continue;
                    outPositions.Add(positions[i]);
                    outRadii.Add(radii[i]);
                    if (outPositions.Count >= limit) return;
                }
            }
        }
    }
}
