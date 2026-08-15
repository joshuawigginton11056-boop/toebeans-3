using RockBridge;
using UnityEditor;
using UnityEngine;

namespace RockBridge.EditorTools
{
    /// <summary>
    /// Reshapes the terrain under each landing so the driving surface meets the ground with no
    /// step and no terrain poking through.
    ///
    /// A flat deck cannot be made flush with a bumpy hillside by moving the deck: Landing Sink is
    /// one number and the ground differs at the two ends, so every value leaves the deck proud
    /// somewhere or buried somewhere else. On this project's own bridge the best a single slider
    /// could manage was 0.25 m of error. The ground is the thing that has to move.
    ///
    /// ---------------------------------------------------------------------------------------
    /// WHY THIS ONE DOES NOT RUN AWAY, WHERE THE PREVIOUS ATTEMPT DID
    ///
    /// A "reshape the terrain to meet the bridge" button was written for this tool on 2026-08-14
    /// and removed the same day after it damaged this project's terrain three times — each press
    /// grew its own footprint (157k, then 222k, then 287k heightmap samples) and one version built
    /// a 12 m mesa with 81-degree sides under an approach ramp. It was a feedback loop, not a
    /// tuning problem: the landing reads its height off the ground, so moving the ground moved the
    /// landing, which asked for the ground to move again.
    ///
    /// This version closes that loop by construction, and the reason it can is a detail of
    /// <see cref="BridgePath"/>: the landing height is
    ///
    ///     startY = path.Samples[0].GroundFloor - settings.landingSink
    ///
    /// — it depends on the ground at the two END SAMPLES ONLY, not on the corridor as a whole. So
    /// if the terrain under the endpoint is set to exactly the deck's top surface and
    /// <see cref="BridgeSettings.landingSink"/> is forced to zero, the next solve reads
    /// GroundFloor = deckTop, places the deck at deckTop - 0, and nothing moves. It is a fixed
    /// point, and pressing the button again recomputes the same target from the same deck.
    ///
    /// Three further guards, each aimed at one of the ways the old one failed:
    ///   * The footprint is FIXED, in metres, measured from the two ends of the path. It cannot
    ///     grow on a second press because it is never derived from what the last press did.
    ///   * Every cell is clamped to <see cref="MaxLift"/> of its pre-existing height, so a bad
    ///     target can raise a bump, never a mesa.
    ///   * The whole TerrainData goes through Undo before a single sample is written.
    ///
    /// TerrainData edits live in memory until the project is saved, so a mistake is recoverable
    /// by discarding without saving — and since 2026-08-15 the terrain assets are committed to
    /// git as well, so `git checkout` is a second net.
    /// </summary>
    public static class BridgeLandingBlender
    {
        /// <summary>How far in from each end of the deck the ground is reshaped, in metres.</summary>
        const float BlendLength = 30f;

        /// <summary>How far past the parapet the shelf eases back into untouched hillside.</summary>
        const float Falloff = 8f;

        /// <summary>Extra corridor beyond the outermost part of the section, in metres.</summary>
        const float Margin = 2f;

        /// <summary>
        /// Ceiling on how far any single sample may move from where it already was. This is the
        /// guard that turns a wrong answer into a visible bump instead of a mountain.
        /// </summary>
        const float MaxLift = 8f;

        public static void Blend(RockBridgeGenerator gen)
        {
            BridgePath path = gen.Path;
            if (path == null || path.Samples.Count < 2)
            {
                EditorUtility.DisplayDialog("Rock Bridge", "There is no bridge to blend under yet.", "OK");
                return;
            }

            Terrain terrain = gen.FindTerrainUnderBridge();
            if (terrain == null || terrain.terrainData == null)
            {
                EditorUtility.DisplayDialog("Rock Bridge",
                    "No terrain found under this bridge, so there is nothing to reshape.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Blend Terrain Into Landings",
                    "This edits the terrain heightmap under the last " + BlendLength +
                    " m of each end, raising or lowering it to meet the driving surface, and easing " +
                    "back out over " + Falloff + " m.\n\n" +
                    "Landing Sink is set to 0 as part of this — the sink and the blend pull against " +
                    "each other, and zero is what makes the result stable under repeated presses.\n\n" +
                    "It changes the terrain asset, not just the scene. Undo puts it back.",
                    "Blend", "Cancel")) return;

            BridgeSettings s = gen.Settings;

            // Zero the sink FIRST and re-solve, so the deck we measure is the deck we will keep.
            // With a non-zero sink the target would be the underside of a deck that is deliberately
            // buried, and the two corrections would fight each other on every press.
            Undo.RecordObject(gen, "Blend Terrain Into Landings");
            s.landingSink = 0f;
            gen.Generate();
            path = gen.Path;

            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Blend Terrain Into Landings");

            int res = data.heightmapResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            float cellX = size.x / (res - 1);
            float cellZ = size.z / (res - 1);

            Transform tf = gen.transform;
            float corridor = s.OuterHalfWidth(s.deckWidth * 0.5f) + Margin;
            float reach = corridor + Falloff + 1f;

            // Collect the deck's top surface over both landing zones, in world space.
            var pts = new System.Collections.Generic.List<Vector3>();
            CollectEnd(gen, path, true, pts);
            CollectEnd(gen, path, false, pts);
            if (pts.Count < 4)
            {
                EditorUtility.DisplayDialog("Rock Bridge", "The bridge is too short to blend.", "OK");
                return;
            }

            // Bounding box of everything we could touch, clamped to the terrain.
            int minX = res, maxX = -1, minZ = res, maxZ = -1;
            foreach (Vector3 w in pts)
            {
                minX = Mathf.Min(minX, Mathf.FloorToInt((w.x - reach - origin.x) / cellX));
                maxX = Mathf.Max(maxX, Mathf.CeilToInt((w.x + reach - origin.x) / cellX));
                minZ = Mathf.Min(minZ, Mathf.FloorToInt((w.z - reach - origin.z) / cellZ));
                maxZ = Mathf.Max(maxZ, Mathf.CeilToInt((w.z + reach - origin.z) / cellZ));
            }
            minX = Mathf.Clamp(minX, 0, res - 1); maxX = Mathf.Clamp(maxX, 0, res - 1);
            minZ = Mathf.Clamp(minZ, 0, res - 1); maxZ = Mathf.Clamp(maxZ, 0, res - 1);

            int w2 = maxX - minX + 1, h2 = maxZ - minZ + 1;
            if (w2 < 2 || h2 < 2)
            {
                EditorUtility.DisplayDialog("Rock Bridge",
                    "The landings do not overlap this terrain.", "OK");
                return;
            }

            float[,] heights = data.GetHeights(minX, minZ, w2, h2);
            var weight = new float[h2, w2];
            var target = new float[h2, w2];

            // Each landing zone is a chain of short segments; a cell takes the strongest claim.
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3 a = pts[k], b = pts[k + 1];
                // The two zones are concatenated, so skip the jump from one end to the other.
                if ((a - b).sqrMagnitude > 400f) continue;

                float outer = corridor + Falloff;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - outer - origin.x) / cellX), minX, maxX);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + outer - origin.x) / cellX), minX, maxX);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, b.z) - outer - origin.z) / cellZ), minZ, maxZ);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z, b.z) + outer - origin.z) / cellZ), minZ, maxZ);

                for (int z = z0; z <= z1; z++)
                {
                    float wz = origin.z + z * cellZ;
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = origin.x + x * cellX;

                        float t;
                        float dist = DistanceToSegment(wx, wz, a, b, out t);
                        if (dist > outer) continue;

                        float wgt = dist <= corridor ? 1f
                                  : Mathf.SmoothStep(1f, 0f, (dist - corridor) / Falloff);

                        int lz = z - minZ, lx = x - minX;
                        if (wgt <= weight[lz, lx]) continue;

                        weight[lz, lx] = wgt;
                        target[lz, lx] = Mathf.Lerp(a.y, b.y, t);
                    }
                }
            }

            int touched = 0;
            float biggest = 0f;
            for (int z = 0; z < h2; z++)
            {
                for (int x = 0; x < w2; x++)
                {
                    float wgt = weight[z, x];
                    if (wgt <= 0f) continue;

                    float current = origin.y + heights[z, x] * size.y;
                    float want = Mathf.Lerp(current, target[z, x], wgt);

                    // Hard clamp: a wrong target may raise a bump, never a mesa.
                    want = Mathf.Clamp(want, current - MaxLift, current + MaxLift);

                    heights[z, x] = Mathf.Clamp01((want - origin.y) / Mathf.Max(0.01f, size.y));
                    biggest = Mathf.Max(biggest, Mathf.Abs(want - current));
                    touched++;
                }
            }

            data.SetHeights(minX, minZ, heights);
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(gen);

            // Re-solve. If the fixed point holds, the deck does not move and a second press would
            // compute the same target.
            gen.Generate();

            Debug.Log(string.Format(
                "Rock Bridge: blended {0:N0} heightmap samples under the landings, moving the ground " +
                "by at most {1:F2} m (cap {2} m). Terrain here is {3:F1} m per sample, so anything " +
                "finer than that cannot be shaped. Landing Sink set to 0.",
                touched, biggest, MaxLift, Mathf.Max(cellX, cellZ)), gen);
        }

        /// <summary>
        /// The deck's top surface across one landing zone, as a chain of world-space points down
        /// the centreline. The top rather than the underside: it is the driving surface that has to
        /// meet the ground, and it is the one a kart catches an edge on.
        /// </summary>
        static void CollectEnd(RockBridgeGenerator gen, BridgePath path, bool atStart,
                               System.Collections.Generic.List<Vector3> into)
        {
            Transform tf = gen.transform;
            float span = Mathf.Min(BlendLength, path.Length * 0.4f);

            for (float d = 0f; d <= span; d += 2f)
            {
                BridgeSample bs = path.SampleAt(atStart ? d : path.Length - d);
                into.Add(tf.TransformPoint(bs.Position));
            }
        }

        /// <summary>Horizontal distance from a point to a segment, and how far along it landed.</summary>
        static float DistanceToSegment(float x, float z, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float lenSq = dx * dx + dz * dz;
            t = lenSq > 1e-6f ? Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / lenSq) : 0f;
            float px = a.x + dx * t - x, pz = a.z + dz * t - z;
            return Mathf.Sqrt(px * px + pz * pz);
        }
    }
}
