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

        // guid, expected width in metres at prefab scale
        var cases = new (string guid, float expectedWidth)[]
        {
            ("3e6544a86819bf942aafae3c6157f692", 0.55f), // 0.34 x 0.55
            ("9adfa627fd4f72d4ab615012349e1825", 0.81f), // 0.81 x 0.58
            ("45f6e9c008a8e944fb033319b4d388c1", 1.44f), // 1.44 x 0.66
            ("3809b7ab36ff6c341a14d87904ba4ed0", 2.31f), // 1.67 x 2.31
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
            bool ok = Mathf.Abs(width - c.expectedWidth) < 0.05f;
            fails += Check(log, ok,
                $"{p.name} width {width:F2} m (expected ~{c.expectedWidth:F2} m, " +
                "the wider horizontal axis since trees get a random Y rotation)");
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
