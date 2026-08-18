using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Self-test for the tree spacing model. The brush itself needs an IMGUI event
// loop to drive, but everything that decides *where* a tree may go is in
// TreeSpacing.cs and can be checked here.
//
// The load-bearing check is GridMatchesBruteForce: TreeOccupancyGrid only scans
// the cells within a computed span, so a search range that under-reaches would
// silently miss neighbours and let trees overlap again - the exact failure this
// rewrite exists to remove.
internal static class TreeScatterTests
{
    [MenuItem("Tools/Trees/Run Tree Scatter Self-Test")]
    public static void Run()
    {
        var log = new StringBuilder();
        int failures = 0;

        failures += FootprintMatchesKnownPrefabs(log);
        failures += EmptyPrefabsMeasureZero(log);
        failures += GridMatchesBruteForce(log);
        failures += AcceptedPlacementsNeverOverlap(log);
        failures += FixedModeIgnoresCanopySize(log);
        failures += BaseOffsetFindsLowestMeshPoint(log);
        failures += PropOrientationFollowsGround(log);
        failures += PropSeatingLandsOnSurface(log);
        failures += ZeroSpacingAllowsOverlap(log);
        failures += BrushRayWindowStaysLocal(log);

        if (failures == 0)
            Debug.Log("Tree scatter self-test PASSED\n" + log);
        else
            Debug.LogError($"Tree scatter self-test FAILED ({failures} check(s))\n" + log);
    }

    private static int Check(StringBuilder log, bool condition, string what)
    {
        log.AppendLine((condition ? "  PASS  " : "  FAIL  ") + what);
        return condition ? 0 : 1;
    }

    // Vector3.Angle goes through acos, which throws away almost all of its
    // precision near zero: two directions agreeing to the last float bit still
    // measure ~0.02 degrees apart, and whether they do depends on the slope.
    // Compare the perpendicular component instead - it is the sine of the
    // angle, and it stays well conditioned exactly where acos doesn't. (Cross
    // alone can't tell parallel from opposed, hence the dot.)
    private static bool SameDirection(Vector3 a, Vector3 b)
    {
        Vector3 na = a.normalized;
        Vector3 nb = b.normalized;
        return Vector3.Cross(na, nb).magnitude < 1e-5f && Vector3.Dot(na, nb) > 0f;
    }

    private static GameObject Load(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // Footprints measured off the mesh must match what the prefabs actually are.
    // These expectations came from measuring the dead-tree pack directly.
    private static int FootprintMatchesKnownPrefabs(StringBuilder log)
    {
        log.AppendLine("Footprint measurement:");
        int fails = 0;

        // guid, expected width in metres at prefab scale, tolerance
        var cases = new (string guid, float expectedWidth, float tolerance)[]
        {
            // This one's prefab root carries a ~51 degree yaw, and the footprint
            // is measured in the frame that rotation puts the model in - so what
            // comes back is the 0.34 x 0.55 box turned in place, somewhere
            // between its own wider side and the 0.64 m envelope of the turn.
            // Hence the looser band here: the number is the art, not the maths.
            ("3e6544a86819bf942aafae3c6157f692", 0.60f, 0.09f), // 0.34 x 0.55
            ("9adfa627fd4f72d4ab615012349e1825", 0.81f, 0.05f), // 0.81 x 0.58
            ("45f6e9c008a8e944fb033319b4d388c1", 1.44f, 0.05f), // 1.44 x 0.66
            ("3809b7ab36ff6c341a14d87904ba4ed0", 2.31f, 0.05f), // 1.67 x 2.31
        };

        foreach (var c in cases)
        {
            GameObject p = Load(c.guid);
            if (p == null)
            {
                fails += Check(log, false, $"prefab {c.guid} missing from project");
                continue;
            }
            float width = TreeFootprint.Radius(p) * 2f;
            bool ok = Mathf.Abs(width - c.expectedWidth) < c.tolerance;
            fails += Check(log, ok,
                $"{p.name} width {width:F2} m (expected {c.expectedWidth:F2} " +
                $"+/- {c.tolerance:F2} m, the wider horizontal axis since trees " +
                "get a random Y rotation)");
        }
        return fails;
    }

    // Two prefabs in the user's saved loadout have no geometry. They must
    // measure zero so the brush skips them instead of planting invisible trees
    // that still claim ground.
    private static int EmptyPrefabsMeasureZero(StringBuilder log)
    {
        log.AppendLine("Empty prefabs:");
        int fails = 0;
        // LowPoly Tree .016 and .031 - both in the saved "Deadtrees" loadout.
        string[] emptyGuids = { "419bf2150c2037f439235655e2e98859", "678dc5d855de7884c9fd461ea304aca7" };

        foreach (string guid in emptyGuids)
        {
            GameObject p = Load(guid);
            if (p == null) continue;
            fails += Check(log, TreeFootprint.Radius(p) <= 0.0001f,
                $"{p.name} measures zero and will be skipped");
        }
        return fails;
    }

    // The grid must agree with an exhaustive scan on every query. A cell span
    // that under-reaches shows up here as a spot the grid calls clear and the
    // brute force calls blocked.
    private static int GridMatchesBruteForce(StringBuilder log)
    {
        log.AppendLine("Grid vs brute force:");
        int fails = 0;

        var rules = new[]
        {
            new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 1f, extraGap = 0f },
            new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 3f, extraGap = 4f },
            new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 0.25f, extraGap = 0f },
            // Prop mode opens the dial down to zero, which has to mean "place
            // anywhere" rather than "reject everything".
            new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 0f, extraGap = 0f },
            new TreeSpacingRule { mode = TreeSpacingMode.FixedDistance, fixedDistance = 12f },
        };

        Random.InitState(20260814);

        for (int r = 0; r < rules.Length; r++)
        {
            TreeSpacingRule rule = rules[r];
            var grid = new TreeOccupancyGrid();

            // Deliberately undersize the cell relative to the rule's reach, so
            // the span arithmetic is the thing under test rather than a
            // generously large cell hiding the bug.
            grid.Reset(2f);

            for (int i = 0; i < 600; i++)
            {
                var p = new Vector3(Random.Range(-120f, 120f), 0f, Random.Range(-120f, 120f));
                grid.Add(p, Random.Range(0.15f, 2.5f));
            }

            int mismatches = 0;
            for (int q = 0; q < 4000; q++)
            {
                float x = Random.Range(-140f, 140f);
                float z = Random.Range(-140f, 140f);
                float radius = Random.Range(0.15f, 2.5f);
                float searchRange = rule.Required(radius, grid.MaxRadius);

                bool viaGrid = grid.IsClear(x, z, radius, searchRange, rule);
                bool viaScan = grid.IsClearBruteForce(x, z, radius, rule);
                if (viaGrid != viaScan) mismatches++;
            }

            string label = rule.mode == TreeSpacingMode.FixedDistance
                ? $"fixed {rule.fixedDistance} m"
                : $"canopy x{rule.canopySpacing} +{rule.extraGap} m";
            fails += Check(log, mismatches == 0,
                $"{label}: 4000 queries against 600 trees, {mismatches} mismatch(es)");
        }
        return fails;
    }

    // The actual guarantee the user cares about: run the same accept/reject
    // loop the brush runs, then verify no two accepted trees overlap.
    private static int AcceptedPlacementsNeverOverlap(StringBuilder log)
    {
        log.AppendLine("Accepted placements:");
        int fails = 0;

        // Mixed sizes spanning the real pack's 7x spread.
        var rule = new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 1f, extraGap = 0f };
        var grid = new TreeOccupancyGrid();
        grid.Reset(6f);

        var pos = new List<Vector3>();
        var rad = new List<float>();

        Random.InitState(99);
        for (int i = 0; i < 6000; i++)
        {
            float x = Random.Range(0f, 60f);
            float z = Random.Range(0f, 60f);
            float radius = Random.Range(0.17f, 1.16f) * Random.Range(1f, 2f);
            float searchRange = rule.Required(radius, grid.MaxRadius);
            if (!grid.IsClear(x, z, radius, searchRange, rule)) continue;

            grid.Add(new Vector3(x, 0f, z), radius);
            pos.Add(new Vector3(x, 0f, z));
            rad.Add(radius);
        }

        int overlaps = 0;
        float worst = 1f;
        for (int i = 0; i < pos.Count; i++)
        for (int j = i + 1; j < pos.Count; j++)
        {
            float dx = pos[i].x - pos[j].x, dz = pos[i].z - pos[j].z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float need = rad[i] + rad[j];
            if (d < need * 0.999f)
            {
                overlaps++;
                worst = Mathf.Min(worst, d / need);
            }
        }

        fails += Check(log, pos.Count > 100,
            $"{pos.Count} trees accepted into a 60x60 m patch (spacing did not choke the brush)");
        fails += Check(log, overlaps == 0,
            $"{overlaps} overlapping pair(s) among {pos.Count} accepted trees" +
            (overlaps > 0 ? $", worst at {worst:P0} of required" : ""));

        // Canopy spacing must actually respond to the dial: a tighter setting
        // has to fit more trees into the same patch, or the control is a no-op.
        int dense = CountAccepted(0.5f);
        int normal = CountAccepted(1f);
        int sparse = CountAccepted(2f);
        fails += Check(log, dense > normal && normal > sparse,
            $"density responds to the dial: x0.5 -> {dense}, x1.0 -> {normal}, x2.0 -> {sparse} trees");

        return fails;
    }

    private static int CountAccepted(float canopySpacing)
    {
        var rule = new TreeSpacingRule
        {
            mode = TreeSpacingMode.Canopy,
            canopySpacing = canopySpacing,
            extraGap = 0f,
        };
        var grid = new TreeOccupancyGrid();
        grid.Reset(6f);

        Random.InitState(4242);
        int accepted = 0;
        for (int i = 0; i < 6000; i++)
        {
            float x = Random.Range(0f, 60f);
            float z = Random.Range(0f, 60f);
            float radius = Random.Range(0.17f, 1.16f);
            float searchRange = rule.Required(radius, grid.MaxRadius);
            if (!grid.IsClear(x, z, radius, searchRange, rule)) continue;
            grid.Add(new Vector3(x, 0f, z), radius);
            accepted++;
        }
        return accepted;
    }

    // ------------------------------------------------------------ prop mode

    // Upright placement can seat a prefab off its world-axis-aligned bounds; a
    // tilted one cannot, so prop mode needs to know how far the mesh hangs
    // below the pivot. Measured on built-in-place geometry rather than a
    // project prefab so the check holds whether or not the art packs are
    // present (they're excluded from the repo).
    private static int BaseOffsetFindsLowestMeshPoint(StringBuilder log)
    {
        log.AppendLine("Base offset:");
        int fails = 0;

        // Pivot at the base: nothing hangs below it.
        GameObject onBase = MakeBox(new Vector3(2f, 3f, 1f), new Vector3(0f, 1.5f, 0f), 1f);
        fails += Check(log, Mathf.Abs(TreeFootprint.BaseOffset(onBase)) < 0.001f,
            $"pivot at mesh base -> offset {TreeFootprint.BaseOffset(onBase):F3} m (expected 0)");
        fails += Check(log, Mathf.Abs(TreeFootprint.Radius(onBase) - 1f) < 0.001f,
            $"radius still the wider horizontal half-axis: {TreeFootprint.Radius(onBase):F3} m (expected 1)");

        // Mesh hangs 0.5 m below the pivot - the prop has to rise by that much.
        GameObject hanging = MakeBox(new Vector3(2f, 3f, 1f), new Vector3(0f, 1f, 0f), 1f);
        fails += Check(log, Mathf.Abs(TreeFootprint.BaseOffset(hanging) - 0.5f) < 0.001f,
            $"mesh below pivot -> offset {TreeFootprint.BaseOffset(hanging):F3} m (expected 0.5)");

        // Mesh floats 0.5 m above the pivot - a negative offset, which has to
        // survive as a negative rather than being clamped, or the prop hovers.
        GameObject floating = MakeBox(new Vector3(2f, 3f, 1f), new Vector3(0f, 2f, 0f), 1f);
        fails += Check(log, Mathf.Abs(TreeFootprint.BaseOffset(floating) + 0.5f) < 0.001f,
            $"mesh above pivot -> offset {TreeFootprint.BaseOffset(floating):F3} m (expected -0.5)");

        // Root scale is baked into the measurement, exactly as Radius does it,
        // because callers multiply by the per-instance scale on top.
        GameObject scaled = MakeBox(new Vector3(2f, 3f, 1f), new Vector3(0f, 1f, 0f), 2f);
        fails += Check(log, Mathf.Abs(TreeFootprint.BaseOffset(scaled) - 1f) < 0.001f,
            $"root scale x2 -> offset {TreeFootprint.BaseOffset(scaled):F3} m (expected 1)");

        // The Z-up case, which is what every mushroom and rock in the art packs
        // is: the mesh stands 3 m along its own +Z from a base at z = 0, and a
        // -90 degree X rotation on the root is what turns that into upright. The
        // measurement has to follow the root's rotation - taken unrotated, this
        // box reads as 0.5 m of overhang (half its 1 m thickness) and 1.5 m of
        // radius (half its height), and the prop gets seated on its side.
        GameObject zUp = MakeBox(new Vector3(2f, 1f, 3f), new Vector3(0f, 0f, 1.5f), 1f,
            Quaternion.AngleAxis(-90f, Vector3.right));
        fails += Check(log, Mathf.Abs(TreeFootprint.BaseOffset(zUp)) < 0.001f,
            $"Z-up prefab, pivot at its base -> offset {TreeFootprint.BaseOffset(zUp):F3} m (expected 0)");
        fails += Check(log, Mathf.Abs(TreeFootprint.Radius(zUp) - 1f) < 0.001f,
            $"...and its height is not mistaken for width: radius {TreeFootprint.Radius(zUp):F3} m (expected 1)");

        // Cache is keyed on the GameObject, so it has to be dropped before the
        // test objects are - otherwise it holds destroyed keys.
        TreeFootprint.ClearCache();
        DestroyBox(onBase);
        DestroyBox(hanging);
        DestroyBox(floating);
        DestroyBox(scaled);
        DestroyBox(zUp);
        return fails;
    }

    // A box of the given size, centred at `center` in root-local space, under a
    // root scaled uniformly by `scale`.
    private static GameObject MakeBox(Vector3 size, Vector3 center, float scale) =>
        MakeBox(size, center, scale, Quaternion.identity);

    private static GameObject MakeBox(Vector3 size, Vector3 center, float scale, Quaternion rootRotation)
    {
        var root = new GameObject("TestProp") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.localScale = Vector3.one * scale;
        root.transform.localRotation = rootRotation;

        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        Vector3 e = size * 0.5f;
        var verts = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            verts[i] = center + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
        }
        // Assigning vertices is what recomputes mesh.bounds, which is the only
        // thing the measurement reads - no triangles needed.
        mesh.vertices = verts;

        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        return root;
    }

    // Meshes aren't collected with the GameObject holding them, so a run of the
    // self-test would otherwise leak one per box.
    private static void DestroyBox(GameObject box)
    {
        MeshFilter mf = box.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
        Object.DestroyImmediate(box);
    }

    // The lean must come from the ground and nothing else. The trap here is
    // rotation order: yawing after the lean turns a random heading into a
    // random *tip direction*, so identical props on one slope fall different
    // ways instead of all leaning downhill.
    private static int PropOrientationFollowsGround(StringBuilder log)
    {
        log.AppendLine("Prop orientation:");
        int fails = 0;

        // A 30 degree bank, written out rather than built with Quaternion.Euler
        // so this check runs headlessly like the maths it exercises.
        Vector3 slope = new Vector3(0f, Mathf.Cos(30f * Mathf.Deg2Rad), Mathf.Sin(30f * Mathf.Deg2Rad));

        fails += Check(log, SameDirection(PropPlacement.UpAxis(slope, 0f), Vector3.up),
            "tilt 0 stands the prop plumb, like a tree");
        fails += Check(log, SameDirection(PropPlacement.UpAxis(slope, 1f), slope),
            "tilt 1 lays the prop flush with the ground");

        // Far enough from zero that acos is well behaved, so this one can stay
        // an angle - and it has to, since the value itself is the point.
        float halfway = Vector3.Angle(PropPlacement.UpAxis(slope, 0.5f), Vector3.up);
        fails += Check(log, Mathf.Abs(halfway - 15f) < 0.5f,
            $"tilt 0.5 leans halfway: {halfway:F1} degrees into a 30 degree slope");

        Vector3 upA = PropPlacement.Rotation(slope, 0f, 0.8f) * Vector3.up;
        Vector3 upB = PropPlacement.Rotation(slope, 137f, 0.8f) * Vector3.up;
        fails += Check(log, SameDirection(upA, upB),
            "lean direction is independent of the random yaw (rotation order is right)");

        // Two props on the same slope that lean the same way must still face
        // different directions, or the yaw has been swallowed entirely.
        Quaternion rotA = PropPlacement.Rotation(slope, 0f, 0.8f);
        Quaternion rotB = PropPlacement.Rotation(slope, 137f, 0.8f);
        float headingSpread = Vector3.Angle(rotA * Vector3.forward, rotB * Vector3.forward);
        fails += Check(log, headingSpread > 100f,
            $"yaw still varies the heading: {headingSpread:F0} degrees apart");

        // A Z-up model, the shape every mushroom in the art packs takes: the -90
        // degree X rotation on its prefab root is the only thing standing it up,
        // so the brush has to turn that rotation rather than overwrite it. Its
        // own +Z is the axis that must end up plumb at tilt 0 - overwriting laid
        // the mushrooms flat on their sides whatever the tilt dial said.
        var zUpModel = new Quaternion(-Mathf.Sqrt(0.5f), 0f, 0f, Mathf.Sqrt(0.5f));
        Vector3 plumb = PropPlacement.Rotation(slope, 40f, 0f, zUpModel) * Vector3.forward;
        fails += Check(log, SameDirection(plumb, Vector3.up),
            "tilt 0 stands a Z-up model's own up axis plumb, on a slope");
        Vector3 flush = PropPlacement.Rotation(slope, 40f, 1f, zUpModel) * Vector3.forward;
        fails += Check(log, SameDirection(flush, slope),
            "tilt 1 lays that same axis along the ground normal");

        // Degenerate normals: flat ground, and a normal opposed to up. Neither
        // may produce a NaN quaternion - one NaN transform corrupts a whole
        // stroke and the errors surface far from here.
        foreach ((string label, Vector3 n) in new[]
        {
            ("flat ground", Vector3.up),
            ("fully inverted", Vector3.down),
            ("vertical wall", Vector3.forward),
            ("zero-length normal", Vector3.zero),
        })
        {
            Quaternion q = PropPlacement.Rotation(n, 45f, 1f);
            bool finite = !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                          && Mathf.Abs(new Vector4(q.x, q.y, q.z, q.w).magnitude - 1f) < 0.01f;
            fails += Check(log, finite, $"{label}: rotation stays a finite unit quaternion");
        }

        return fails;
    }

    // Seating is measured along the leaned axis, not world Y. On a slope those
    // differ, and using world Y is what leaves a tilted prop floating on its
    // uphill side.
    private static int PropSeatingLandsOnSurface(StringBuilder log)
    {
        log.AppendLine("Prop seating:");
        int fails = 0;

        Vector3 surface = new Vector3(10f, 4f, -3f);
        Vector3 slope = new Vector3(0f, Mathf.Cos(25f * Mathf.Deg2Rad), Mathf.Sin(25f * Mathf.Deg2Rad));
        Vector3 up = PropPlacement.UpAxis(slope, 1f);

        // A prefab whose mesh hangs 0.4 m below its pivot has to rise 0.4 m
        // along the leaned axis for its base to touch.
        Vector3 seated = PropPlacement.Position(surface, up, 0.4f, 0f);
        fails += Check(log, Mathf.Abs(Vector3.Distance(seated, surface) - 0.4f) < 0.001f,
            "base offset lifts the prop by exactly its overhang");
        fails += Check(log, SameDirection(seated - surface, up),
            "the lift runs along the leaned axis, not along world Y");
        fails += Check(log, !SameDirection(seated - surface, Vector3.up),
            "...and that axis is genuinely not world Y on a 25 degree slope");

        // Sink pushes into the ground, and must be able to take the prop below
        // the surface rather than clamping at it.
        Vector3 sunk = PropPlacement.Position(surface, up, 0.4f, 0.6f);
        fails += Check(log, Vector3.Dot(sunk - surface, up) < 0f,
            $"sink 0.6 m beds the prop below the surface (offset {Vector3.Dot(sunk - surface, up):F2} m)");

        Vector3 flat = PropPlacement.Position(surface, Vector3.up, 0f, 0f);
        fails += Check(log, Vector3.Distance(flat, surface) < 0.001f,
            "a base-pivoted prefab on flat ground lands exactly on the hit point");

        return fails;
    }

    // The point of prop mode's spacing floor: grass and mushroom clumps read as
    // a polka-dot pattern if their footprints are kept apart, so zero has to
    // switch the check off rather than reject everything.
    private static int ZeroSpacingAllowsOverlap(StringBuilder log)
    {
        log.AppendLine("Zero spacing:");
        var rule = new TreeSpacingRule { mode = TreeSpacingMode.Canopy, canopySpacing = 0f, extraGap = 0f };
        var grid = new TreeOccupancyGrid();
        grid.Reset(2f);

        Random.InitState(7);
        int accepted = 0;
        for (int i = 0; i < 500; i++)
        {
            // Deliberately piled into a 1 m spot - every candidate overlaps.
            float x = Random.Range(0f, 1f);
            float z = Random.Range(0f, 1f);
            float radius = Random.Range(0.2f, 1.5f);
            if (!grid.IsClear(x, z, radius, rule.Required(radius, grid.MaxRadius), rule)) continue;
            grid.Add(new Vector3(x, 0f, z), radius);
            accepted++;
        }

        return Check(log, accepted == 500,
            $"{accepted}/500 fully overlapping props accepted with spacing at 0");
    }

    // Props find ground by casting down. Casting from the top of the world
    // would be wrong wherever the map has something overhead - a cave, or the
    // volcano's passage - because the topmost surface is the roof, not the
    // floor the cursor is on. The brush window has to stay near the brush.
    private static int BrushRayWindowStaysLocal(StringBuilder log)
    {
        log.AppendLine("Ray window:");
        int fails = 0;

        // Painting a cave floor at y=100 with a roof 40 m overhead.
        const float floorY = 100f;
        const float roofY = 140f;
        PropRayWindow brush = PropRayWindow.AroundBrush(floorY, 5f);

        fails += Check(log, brush.startY < roofY,
            $"brush window starts at {brush.startY:F0} m, below a roof at {roofY:F0} m");
        fails += Check(log, brush.startY > floorY && brush.BottomY < floorY,
            $"brush window brackets the floor it was opened on ({brush.BottomY:F0} to {brush.startY:F0} m)");

        // A big brush on a steep face has to reach ground well below its centre.
        PropRayWindow wide = PropRayWindow.AroundBrush(floorY, 30f);
        fails += Check(log, floorY - wide.BottomY >= 30f * 2f,
            $"a 30 m brush reaches {floorY - wide.BottomY:F0} m below its centre for steep ground");

        // Whole-map scatter has no cursor, so it must span everything.
        var terrainPos = new Vector3(0f, 12f, 0f);
        const float terrainHeight = 600f;
        PropRayWindow map = PropRayWindow.WholeMap(terrainPos, terrainHeight);
        fails += Check(log, map.BottomY <= terrainPos.y && map.startY >= terrainPos.y + terrainHeight,
            $"whole-map window spans {map.BottomY:F0} to {map.startY:F0} m over a " +
            $"{terrainHeight:F0} m terrain based at {terrainPos.y:F0} m");
        fails += Check(log, map.startY - (terrainPos.y + terrainHeight) >= PropRayWindow.Headroom,
            "whole-map window clears the terrain top by the full headroom, for meshes above it");

        return fails;
    }

    // Fixed mode is kept for same-size lists; it must stay size-blind so the
    // two modes are genuinely different tools.
    private static int FixedModeIgnoresCanopySize(StringBuilder log)
    {
        log.AppendLine("Fixed mode:");
        var rule = new TreeSpacingRule { mode = TreeSpacingMode.FixedDistance, fixedDistance = 7f };
        bool sizeBlind = Mathf.Approximately(rule.Required(0.1f, 0.1f), rule.Required(5f, 9f));
        return Check(log, sizeBlind, "distance is independent of tree size (7 m either way)");
    }
}
