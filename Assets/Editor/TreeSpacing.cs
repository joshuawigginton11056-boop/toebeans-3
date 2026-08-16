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

// Footprint of a prefab at scale 1, in world metres.
internal struct TreeMeasurement
{
    // Half the wider horizontal axis of the mesh bounds.
    public float radius;
    // How far the lowest mesh point sits below the pivot in the prefab's rest
    // pose, along the axis the rest pose makes vertical. Positive when the
    // mesh hangs below the pivot, negative when it floats above it. Adding
    // this along the placement up-axis puts the base exactly on the ground,
    // which is what upright placement gets from an axis-aligned bounds test
    // but a tilted prop cannot.
    public float baseOffset;
}

internal static class TreeFootprint
{
    private static readonly Dictionary<GameObject, TreeMeasurement> cache =
        new Dictionary<GameObject, TreeMeasurement>();

    public static void ClearCache() => cache.Clear();

    public static float Radius(GameObject prefab) => Of(prefab).radius;

    public static float BaseOffset(GameObject prefab) => Of(prefab).baseOffset;

    public static TreeMeasurement Of(GameObject prefab)
    {
        if (prefab == null) return default;
        if (cache.TryGetValue(prefab, out TreeMeasurement cached)) return cached;
        TreeMeasurement measured = Measure(prefab);
        cache[prefab] = measured;
        return measured;
    }

    // Measured from mesh bounds rather than Renderer.bounds so it needs no live
    // instance and can't read back stale for a frame after a transform change.
    //
    // The bounds themselves are taken in the root's own local space, scaled but
    // unrotated. What the root's rotation decides is only which way is *up* in
    // there - see UpInLocalSpace - because that is the one thing the caller
    // can't recover afterwards.
    private static TreeMeasurement Measure(GameObject prefab)
    {
        Transform root = prefab.transform;
        // Scale rides in on the matrix rather than being multiplied back in per
        // axis at the end: Unity applies it before the root's rotation, so this
        // is the space the rotation actually turns, and a mirrored root can't
        // invert the extents because the bounds grow from transformed corners.
        Matrix4x4 toRoot = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
        bool any = false;
        Bounds local = new Bounds();

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            EncapsulateTransformed(ref local, ref any, mf.sharedMesh.bounds,
                toRoot * mf.transform.localToWorldMatrix);
        }
        foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            EncapsulateTransformed(ref local, ref any, smr.sharedMesh.bounds,
                toRoot * smr.transform.localToWorldMatrix);
        }

        if (!any) return default;

        Vector3 up = UpInLocalSpace(root.localRotation);
        // Sign doesn't enter either answer - only which axis carries the height
        // and how much of each extent leans against the up direction.
        var reach = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));

        // Exact for a box: the lowest corner along `up` is the one that puts
        // every extent on the far side of the pivot from it.
        Vector3 e = local.extents;
        float baseOffset = e.x * reach.x + e.y * reach.y + e.z * reach.z
                           - Vector3.Dot(local.center, up);

        // Trees get a random Y rotation, so the wider horizontal axis is the one
        // that can end up facing a neighbour - take that as the radius. Which
        // two axes are horizontal is what the rest pose settles: for the Z-up
        // mushrooms it is X and Y, and reading X and Z there took the mushroom's
        // own height for a footprint and spaced them out like trees.
        //
        // Snapping to the nearest axis rather than re-boxing the bounds through
        // the rotation keeps a root that sits a fraction of a degree off plumb
        // - the tree pack is 0.45 degrees out - from quietly widening every
        // trunk by a slice of its own height.
        Vector3 size = local.size;
        float across;
        if (reach.y >= reach.x && reach.y >= reach.z) across = Mathf.Max(size.x, size.z);
        else if (reach.x >= reach.z) across = Mathf.Max(size.y, size.z);
        else across = Mathf.Max(size.x, size.y);

        return new TreeMeasurement
        {
            radius = 0.5f * across,
            baseOffset = baseOffset,
        };
    }

    // Which direction inside the prefab's local space ends up pointing at the
    // sky once the root's own rotation is applied - the rest pose being the
    // shape the prefab is in when you drag it into a scene, not decoration to
    // be measured through. A model authored Z-up (most of the mushrooms here)
    // carries a -90 degree X correction on its root, which puts its up at local
    // +Z; a model already Y-up gives back plain +Y and nothing changes.
    //
    // Any spin the rest pose has about world Y drops out on its own, since that
    // axis is fixed by the rotation - so a prefab saved turned 50 degrees
    // measures the same as one saved facing front, which is right: scatter is
    // about to yaw it randomly about that very axis anyway.
    private static Vector3 UpInLocalSpace(Quaternion rest)
    {
        return Quaternion.Inverse(rest) * Vector3.up;
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
