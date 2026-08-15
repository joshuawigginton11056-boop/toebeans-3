using System.Collections.Generic;
using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Turns a <see cref="VolcanoShape"/> into flat-shaded triangles: the cone and its crater, the
    /// passage cut through the base, and the loose rock on top.
    ///
    /// Pure maths in, triangles out. No scene objects, no asset loading, no global state and none of
    /// Unity's native calls, so the whole thing runs outside the Editor and can be asserted against.
    /// </summary>
    public static class VolcanoMeshBuilder
    {
        const int SubmeshCount = 4;

        public static VolcanoMeshBuffer Build(VolcanoSettings settings)
        {
            return Build(settings, new VolcanoShape(settings ?? new VolcanoSettings()));
        }

        public static VolcanoMeshBuffer Build(VolcanoSettings settings, VolcanoShape shape)
        {
            if (settings == null) settings = new VolcanoSettings();
            if (shape == null) shape = new VolcanoShape(settings);

            var buf = new VolcanoMeshBuffer(SubmeshCount, settings.uvScale);

            BuildSurface(buf, settings, shape);
            BuildFissures(buf, settings, shape);
            BuildCrags(buf, settings, shape);
            BuildBoulders(buf, settings, shape);
            BuildRimSpires(buf, settings, shape);
            BuildPassage(buf, settings, shape);

            if (settings.uvMode == VolcanoUVMode.Normalized) buf.NormalizeUVs();
            return buf;
        }

        // ================================================================== the cone

        static void BuildSurface(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            int segments = Mathf.Max(3, s.angularSegments);
            float[] radii = BuildRingRadii(s);
            var rng = new Rng(s.seed + 313);

            float step = Mathf.PI * 2f / segments;

            // Every ring gets its own angular phase and every vertex a nudge in and out. Without it
            // the grid is a perfect radial fan and the eye picks the spokes out immediately, however
            // rough the heights are. Same reason the terrain lattice is warped in XZ and not only
            // in height.
            var grid = new Vector3[radii.Length][];
            for (int i = 0; i < radii.Length; i++)
            {
                grid[i] = new Vector3[segments];
                float r = radii[i];
                if (r <= 1e-4f)
                {
                    // The crater floor's middle vertex. One point, repeated, so the fan below can
                    // treat ring 0 like any other ring.
                    Vector3 c = new Vector3(0f, shape.Height(0f, 0f), 0f);
                    for (int j = 0; j < segments; j++) grid[i][j] = c;
                    continue;
                }

                float phase = rng.Value() * step;
                float spacing = RingSpacing(radii, i);

                for (int j = 0; j < segments; j++)
                {
                    float a = phase + j * step;
                    float rr = Mathf.Max(0.2f, r + rng.Signed(spacing * 0.22f));
                    float x = rr * Mathf.Cos(a);
                    float z = rr * Mathf.Sin(a);
                    grid[i][j] = new Vector3(x, shape.Height(x, z), z);
                }
            }

            var faceRng = new Rng(s.seed + 787);

            for (int i = 0; i < radii.Length - 1; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int j2 = (j + 1) % segments;

                    Vector3 a = grid[i][j];
                    Vector3 b = grid[i][j2];
                    Vector3 c = grid[i + 1][j];
                    Vector3 d = grid[i + 1][j2];

                    Vector3 mid = (a + b + c + d) * 0.25f;
                    VolcanoSlot slot = SlotFor(s, shape, mid);
                    float shade = 0.84f + faceRng.Value() * 0.34f;

                    if (radii[i] <= 1e-4f)
                    {
                        // Fan out of the middle: a and b are the same point.
                        Emit(buf, shape, a, d, c, slot, shade);
                        continue;
                    }

                    // Wound so the surface faces up and out. A ring going counter-clockwise in XZ
                    // with radius increasing outward gives that with this corner order.
                    Emit(buf, shape, a, b, c, slot, shade);
                    Emit(buf, shape, b, d, c, slot, shade);
                }
            }
        }

        static float RingSpacing(float[] radii, int i)
        {
            if (radii.Length < 2) return 1f;
            if (i == 0) return radii[1] - radii[0];
            if (i == radii.Length - 1) return radii[i] - radii[i - 1];
            return Mathf.Min(radii[i] - radii[i - 1], radii[i + 1] - radii[i]);
        }

        /// <summary>
        /// The radii of every ring, from the middle of the crater floor to the outside of the buried
        /// skirt. Each section gets its own distribution: the crater and the rim are short and want
        /// even spacing, the flank wants its rings bunched towards the summit where the profile bends.
        /// </summary>
        static float[] BuildRingRadii(VolcanoSettings s)
        {
            float lip = s.CraterLipRadius;
            float rim = s.rimRadius;
            float foot = Mathf.Max(rim + 2f, s.baseRadius);

            var radii = new List<float>();

            int craterRings = Mathf.Max(2, s.craterRings);
            for (int k = 0; k <= craterRings; k++)
                radii.Add(lip * Mathf.Pow(k / (float)craterRings, 0.85f));

            const int rimRings = 2;
            for (int k = 1; k <= rimRings; k++)
                radii.Add(Mathf.Lerp(lip, rim, k / (float)rimRings));

            int flankRings = Mathf.Max(3, s.radialRings);
            for (int k = 1; k <= flankRings; k++)
                radii.Add(rim + (foot - rim) * Mathf.Pow(k / (float)flankRings, Mathf.Max(0.1f, s.ringBias)));

            if (s.skirtWidth > 0.01f)
            {
                const int skirtRings = 2;
                for (int k = 1; k <= skirtRings; k++)
                    radii.Add(foot + s.skirtWidth * (k / (float)skirtRings));
            }

            // Duplicated radii would only make degenerate quads.
            for (int i = radii.Count - 1; i > 0; i--)
                if (radii[i] - radii[i - 1] < 1e-3f) radii.RemoveAt(i);

            return radii.ToArray();
        }

        /// <summary>
        /// Which material a face belongs to. Basalt low down, ash up the cone, scorched rock around
        /// the summit and anywhere a spillway has scoured a channel.
        /// </summary>
        static VolcanoSlot SlotFor(VolcanoSettings s, VolcanoShape shape, Vector3 p)
        {
            float frac = s.height > 1e-3f ? p.y / s.height : 0f;

            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float th = Mathf.Atan2(p.z, p.x);
            for (int i = 0; i < shape.SpillwayCount; i++)
            {
                if (shape.SpillwayWeight(i, r, th) > 0.4f && frac > 0.25f) return VolcanoSlot.Ember;
            }

            if (frac >= s.emberHeightFraction) return VolcanoSlot.Ember;
            if (frac >= s.ashHeightFraction) return VolcanoSlot.Ash;
            return VolcanoSlot.Rock;
        }

        // ================================================================== cutting the passage

        /// <summary>
        /// Adds a triangle of the mountain's surface, minus whatever the passage takes out of it.
        ///
        /// The hole is the intersection of one half-space per face of the arch, so subtracting it is
        /// a run of clips: the part outside the first plane is kept, whatever is left is fed to the
        /// second, and anything still standing when the planes run out was inside the passage and is
        /// dropped. Exact, and it leaves the arch outline on the cut edge rather than a staircase of
        /// whole triangles.
        /// </summary>
        static void Emit(VolcanoMeshBuffer buf, VolcanoShape shape, Vector3 a, Vector3 b, Vector3 c,
                         VolcanoSlot slot, float shade)
        {
            if (!shape.HasPassage)
            {
                buf.AddTriangle(a, b, c, slot, shade);
                return;
            }

            int planes = shape.CutPlaneCount;

            // Nearly every triangle on the mountain is nowhere near the passage. One plane with all
            // three corners on its outside proves the triangle misses the prism entirely.
            for (int p = 0; p < planes; p++)
            {
                if (shape.CutPlaneDistance(p, a) > 0f &&
                    shape.CutPlaneDistance(p, b) > 0f &&
                    shape.CutPlaneDistance(p, c) > 0f)
                {
                    buf.AddTriangle(a, b, c, slot, shade);
                    return;
                }
            }

            var remaining = new List<Vector3>(4) { a, b, c };
            var outside = new List<Vector3>(8);
            var inside = new List<Vector3>(8);

            for (int p = 0; p < planes && remaining.Count >= 3; p++)
            {
                SplitByPlane(shape, p, remaining, outside, inside);

                if (outside.Count >= 3) buf.AddPolygon(outside, slot, shade);

                remaining.Clear();
                remaining.AddRange(inside);
            }

            // Anything still standing was inside the passage and is not part of the mountain.
        }

        static void SplitByPlane(VolcanoShape shape, int plane, List<Vector3> poly,
                                 List<Vector3> outside, List<Vector3> inside)
        {
            outside.Clear();
            inside.Clear();

            const float eps = 1e-5f;
            int n = poly.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 cur = poly[i];
                Vector3 nxt = poly[(i + 1) % n];

                float dc = shape.CutPlaneDistance(plane, cur);
                float dn = shape.CutPlaneDistance(plane, nxt);

                if (dc >= -eps) outside.Add(cur);
                if (dc <= eps) inside.Add(cur);

                if ((dc > eps && dn < -eps) || (dc < -eps && dn > eps))
                {
                    float t = dc / (dc - dn);
                    Vector3 crossing = cur + (nxt - cur) * t;
                    outside.Add(crossing);
                    inside.Add(crossing);
                }
            }
        }

        // ================================================================== the passage

        static void BuildPassage(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            if (s.passage != PassageMode.Bore) return;

            float spanFrom, spanTo;
            if (!shape.TryGetBoreSpan(out spanFrom, out spanTo)) return;

            float apron = Mathf.Max(0f, s.boreApronLength);
            float from = spanFrom - apron;
            float to = spanTo + apron;

            float spacing = Mathf.Max(0.5f, s.boreStationSpacing);
            int stations = Mathf.Max(2, Mathf.CeilToInt((to - from) / spacing) + 1);

            Vector2[] profile = shape.ArchProfile;
            int corners = profile.Length;

            var alongs = new float[stations];
            for (int i = 0; i < stations; i++)
                alongs[i] = Mathf.Lerp(from, to, i / (float)(stations - 1));

            // Every corner of every cross-section, roughened, plus how deep under the surface it is.
            // The depth is what the walls are clipped against: they exist exactly where there is
            // rock to hold them up, which is what puts the mouth on the right outline.
            var pts = new Vector3[stations][];
            var depth = new float[stations][];

            for (int i = 0; i < stations; i++)
            {
                pts[i] = new Vector3[corners];
                depth[i] = new float[corners];

                float lift = ApronOffset(s, alongs[i], spanFrom, spanTo);

                for (int k = 0; k < corners; k++)
                {
                    Vector2 sec = profile[k];

                    // The floor is never roughened. This mesh is the collider and karts drive on it.
                    //
                    // The walls are only ever pushed outwards, never in. Two things depend on that:
                    // the passage is guaranteed to be at least as wide as it says it is, whatever
                    // the roughness is set to, and the wall can never wander inside the hole cut in
                    // the mountain, which is what stops the overlap at the mouth being undone.
                    if (sec.y > 0.01f && s.boreWallRoughness > 0f)
                    {
                        float n = VolcanoNoise.Value(alongs[i] * 0.11f, sec.y * 0.23f + k * 3.7f, s.seed + 907);
                        Vector2 pivot = new Vector2(0f, Mathf.Max(0.5f, s.boreHeight * 0.35f));
                        Vector2 outward = sec - pivot;
                        float len = outward.magnitude;
                        if (len > 1e-3f) sec += outward / len * (n * s.boreWallRoughness);
                    }

                    Vector3 p = shape.BorePoint(alongs[i], sec) + Vector3.up * lift;
                    pts[i][k] = p;
                    depth[i][k] = shape.DepthBelowSurface(p);
                }
            }

            var rng = new Rng(s.seed + 4021);

            // ---- walls and arch --------------------------------------------------------------
            // Edge 0 of the profile is the floor and is handled separately, because it carries on
            // past both mouths as the apron and must never be clipped away.
            for (int i = 0; i < stations - 1; i++)
            {
                for (int k = 1; k < corners; k++)
                {
                    int k2 = (k + 1) % corners;

                    float shade = 0.78f + rng.Value() * 0.34f;

                    ClipQuadToRock(buf, pts[i][k], pts[i][k2], pts[i + 1][k], pts[i + 1][k2],
                                   depth[i][k], depth[i][k2], depth[i + 1][k], depth[i + 1][k2],
                                   VolcanoSlot.Rock, shade);
                }
            }

            // ---- floor ------------------------------------------------------------------------
            float halfWidth = Mathf.Max(0.5f, s.boreWidth * 0.5f);
            for (int i = 0; i < stations - 1; i++)
            {
                float shade = 0.80f + rng.Value() * 0.26f;

                Vector3 a = pts[i][0];
                Vector3 b = pts[i][1];
                Vector3 c = pts[i + 1][0];
                Vector3 d = pts[i + 1][1];
                buf.AddQuad(a, b, c, d, VolcanoSlot.Rock, shade);

                // A lip down each side so the apron is not a paper edge where it runs out over open
                // ground. Under the mountain it is buried in rock and costs nothing to look at.
                Vector3 down = Vector3.down * Mathf.Max(1f, s.skirtSink);
                buf.AddQuad(a + down, a, c + down, c, VolcanoSlot.Rock, shade * 0.9f);
                buf.AddQuad(b, b + down, d, d + down, VolcanoSlot.Rock, shade * 0.9f);
            }

            // ---- glowing seam -----------------------------------------------------------------
            if (s.boreLavaSeam)
            {
                float seam = Mathf.Max(0.05f, s.boreSeamHeight);
                float inset = 0.06f;

                for (int i = 0; i < stations - 1; i++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        float x = side == 0 ? halfWidth - inset : -halfWidth + inset;
                        float lift0 = ApronOffset(s, alongs[i], spanFrom, spanTo);
                        float lift1 = ApronOffset(s, alongs[i + 1], spanFrom, spanTo);

                        Vector3 a = shape.BorePoint(alongs[i], new Vector2(x, 0.02f)) + Vector3.up * lift0;
                        Vector3 b = shape.BorePoint(alongs[i], new Vector2(x, seam)) + Vector3.up * lift0;
                        Vector3 c = shape.BorePoint(alongs[i + 1], new Vector2(x, 0.02f)) + Vector3.up * lift1;
                        Vector3 d = shape.BorePoint(alongs[i + 1], new Vector2(x, seam)) + Vector3.up * lift1;

                        float da = shape.DepthBelowSurface(a);
                        float db = shape.DepthBelowSurface(b);
                        float dc = shape.DepthBelowSurface(c);
                        float dd = shape.DepthBelowSurface(d);

                        // Wound to face into the tunnel on both sides.
                        if (side == 0) ClipQuadToRock(buf, a, b, c, d, da, db, dc, dd, VolcanoSlot.Molten, 1f);
                        else ClipQuadToRock(buf, b, a, d, c, db, da, dd, dc, VolcanoSlot.Molten, 1f);
                    }
                }
            }
        }

        /// <summary>
        /// How far the passage floor has sunk at this point along it. Zero everywhere under the
        /// mountain; past each mouth it dives, so the floor runs out under the surrounding ground
        /// instead of ending on a step a kart would be launched off.
        /// </summary>
        static float ApronOffset(VolcanoSettings s, float along, float spanFrom, float spanTo)
        {
            if (s.boreApronLength <= 0.01f || s.boreApronDrop <= 0f) return 0f;

            float outside = 0f;
            if (along < spanFrom) outside = spanFrom - along;
            else if (along > spanTo) outside = along - spanTo;
            if (outside <= 0f) return 0f;

            return -s.boreApronDrop * VolcanoNoise.SmoothStep01(outside / s.boreApronLength);
        }

        /// <summary>
        /// Adds a quad of tunnel surface, keeping only the part with rock behind it. The corner
        /// depths are the signed distance under the mountain's surface, so the cut runs along the
        /// same curve the hole in the mountain was cut on and the two meet at the mouth.
        /// </summary>
        static void ClipQuadToRock(VolcanoMeshBuffer buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                   float da, float db, float dc, float dd, VolcanoSlot slot, float shade)
        {
            ClipTriangleToRock(buf, a, b, c, da, db, dc, slot, shade);
            ClipTriangleToRock(buf, b, d, c, db, dd, dc, slot, shade);
        }

        static readonly List<Vector3> _clipPts = new List<Vector3>(6);

        static void ClipTriangleToRock(VolcanoMeshBuffer buf, Vector3 a, Vector3 b, Vector3 c,
                                       float da, float db, float dc, VolcanoSlot slot, float shade)
        {
            if (da >= 0f && db >= 0f && dc >= 0f)
            {
                buf.AddTriangle(a, b, c, slot, shade);
                return;
            }
            if (da < 0f && db < 0f && dc < 0f) return;

            _clipPts.Clear();
            ClipEdge(a, b, da, db);
            ClipEdge(b, c, db, dc);
            ClipEdge(c, a, dc, da);

            buf.AddPolygon(_clipPts, slot, shade);
        }

        static void ClipEdge(Vector3 p0, Vector3 p1, float d0, float d1)
        {
            if (d0 >= 0f) _clipPts.Add(p0);
            if ((d0 >= 0f) != (d1 >= 0f))
            {
                float t = d0 / (d0 - d1);
                _clipPts.Add(p0 + (p1 - p0) * t);
            }
        }

        // ================================================================== detail

        /// <summary>
        /// Glowing fissures raked down from the rim. Laid just above the surface rather than cut
        /// into it, so they cost a ribbon of triangles each and nothing in the height field.
        /// </summary>
        static void BuildFissures(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            if (s.fissureCount <= 0) return;

            var rng = new Rng(s.seed + 1607);
            float step = 2.5f;

            for (int i = 0; i < s.fissureCount; i++)
            {
                float th = rng.Value() * Mathf.PI * 2f;

                // Never down a spillway: that channel already has real lava running in it.
                bool clash = false;
                for (int k = 0; k < shape.SpillwayCount; k++)
                    if (shape.SpillwayWeight(k, s.rimRadius, th) > 0.2f) clash = true;
                if (clash) continue;

                float length = s.fissureLength * rng.Range(0.6f, 1.25f);
                float start = s.rimRadius + rng.Range(0.5f, 4f);
                float drift = rng.Signed(0.12f / Mathf.Max(1f, s.rimRadius));

                int steps = Mathf.Max(2, Mathf.CeilToInt(length / step));
                Vector3 prevL = Vector3.zero, prevR = Vector3.zero;

                for (int k = 0; k <= steps; k++)
                {
                    float f = k / (float)steps;
                    float r = start + length * f;
                    float ang = th + drift * (r - start);

                    // Fades to nothing at both ends, so no hard rectangle sitting on the rock.
                    float w = s.fissureWidth * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(f));
                    float dx = -Mathf.Sin(ang) * w;
                    float dz = Mathf.Cos(ang) * w;

                    float cx = r * Mathf.Cos(ang);
                    float cz = r * Mathf.Sin(ang);

                    Vector3 l = new Vector3(cx + dx, shape.Height(cx + dx, cz + dz) + s.fissureLift, cz + dz);
                    Vector3 rr = new Vector3(cx - dx, shape.Height(cx - dx, cz - dz) + s.fissureLift, cz - dz);

                    // Right edge first: a ring of points taken counter-clockwise in atan2 winds
                    // clockwise seen from above in Unity's left-handed space, so this is the order
                    // that leaves the ribbon facing the sky.
                    if (k > 0)
                    {
                        Emit(buf, shape, prevR, prevL, rr, VolcanoSlot.Molten, 1f);
                        Emit(buf, shape, prevL, l, rr, VolcanoSlot.Molten, 1f);
                    }

                    prevL = l;
                    prevR = rr;
                }
            }
        }

        static void BuildCrags(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            if (s.cragCount <= 0) return;
            var rng = new Rng(s.seed + 2203);

            for (int i = 0; i < s.cragCount; i++)
            {
                float th = rng.Value() * Mathf.PI * 2f;
                float r = Mathf.Lerp(s.rimRadius + 4f, s.baseRadius - 6f, Mathf.Sqrt(rng.Value()));

                // Out of the lava channels, and out of the way of the passage mouths.
                if (InSpillway(shape, r, th, 0.25f)) continue;

                float x = r * Mathf.Cos(th);
                float z = r * Mathf.Sin(th);
                Vector3 basePt = new Vector3(x, shape.Height(x, z), z);

                // Size first: the margin has to cover the crag that actually gets built, tip and
                // all, not the average one.
                float size = s.cragSize * rng.Range(0.45f, 1.3f);
                if (NearPassage(shape, basePt, size * 2.2f)) continue;

                Vector3 tip = basePt + shape.Normal(x, z) * size * rng.Range(0.8f, 1.6f);
                tip += new Vector3(rng.Signed(size * 0.3f), 0f, rng.Signed(size * 0.3f));

                AddSpike(buf, shape, basePt, tip, size * 0.45f, 5, ref rng,
                         SlotFor(s, shape, basePt), 0.8f + rng.Value() * 0.3f);
            }
        }

        static void BuildBoulders(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            if (s.boulderCount <= 0) return;
            var rng = new Rng(s.seed + 3307);

            for (int i = 0; i < s.boulderCount; i++)
            {
                float th = rng.Value() * Mathf.PI * 2f;
                float r = Mathf.Lerp(s.rimRadius + 2f, s.baseRadius + s.skirtWidth * 0.4f,
                                     Mathf.Pow(rng.Value(), 0.65f));

                if (InSpillway(shape, r, th, 0.2f)) continue;

                float x = r * Mathf.Cos(th);
                float z = r * Mathf.Sin(th);
                Vector3 p = new Vector3(x, shape.Height(x, z), z);

                float size = s.boulderSize * rng.Range(0.35f, 1.4f);
                if (NearPassage(shape, p, size * 2.2f)) continue;

                // Sunk a third of the way in, so it sits in the ground rather than on it.
                AddRock(buf, p - Vector3.up * size * 0.35f, size, ref rng,
                        r > s.baseRadius * 0.75f ? VolcanoSlot.Rock : SlotFor(s, shape, p),
                        0.78f + rng.Value() * 0.34f);
            }
        }

        static void BuildRimSpires(VolcanoMeshBuffer buf, VolcanoSettings s, VolcanoShape shape)
        {
            if (s.rimSpireCount <= 0) return;
            var rng = new Rng(s.seed + 4409);

            float mid = (s.CraterLipRadius + s.rimRadius) * 0.5f;

            for (int i = 0; i < s.rimSpireCount; i++)
            {
                float th = rng.Value() * Mathf.PI * 2f;
                if (InSpillway(shape, mid, th, 0.05f)) continue;

                float r = mid + rng.Signed(s.rimWidth * 0.3f);
                float x = r * Mathf.Cos(th);
                float z = r * Mathf.Sin(th);

                Vector3 basePt = new Vector3(x, shape.Height(x, z), z);
                float h = s.rimSpireHeight * rng.Range(0.5f, 1.5f);

                // Normally nowhere near the passage, but a tall bore through a short cone reaches
                // all the way to the summit and a spire would then be hanging in the opening.
                if (NearPassage(shape, basePt, h * 1.2f)) continue;

                Vector3 tip = basePt + new Vector3(rng.Signed(h * 0.25f), h, rng.Signed(h * 0.25f));
                AddSpike(buf, shape, basePt, tip, Mathf.Max(0.6f, h * 0.32f), 5, ref rng,
                         VolcanoSlot.Ember, 0.8f + rng.Value() * 0.3f);
            }
        }

        static bool InSpillway(VolcanoShape shape, float r, float th, float threshold)
        {
            for (int i = 0; i < shape.SpillwayCount; i++)
                if (shape.SpillwayWeight(i, r, th) > threshold) return true;
            return false;
        }

        /// <summary>
        /// Keeps loose rock away from the passage. Props are added after the surface has been cut
        /// and are not cut themselves, so a boulder sitting on a mouth would hang in the opening.
        /// </summary>
        static bool NearPassage(VolcanoShape shape, Vector3 p, float margin)
        {
            if (!shape.HasPassage) return false;

            for (int i = 0; i < shape.CutPlaneCount; i++)
                if (shape.CutPlaneDistance(i, p) > margin) return false;

            return true;
        }

        /// <summary>A tapered spike: a ring of jittered corners on the ground, closing to one tip.</summary>
        static void AddSpike(VolcanoMeshBuffer buf, VolcanoShape shape, Vector3 basePt, Vector3 tip,
                             float radius, int sides, ref Rng rng, VolcanoSlot slot, float shade)
        {
            sides = Mathf.Max(3, sides);
            var ring = new Vector3[sides];
            float phase = rng.Value() * Mathf.PI * 2f;

            for (int i = 0; i < sides; i++)
            {
                float a = phase + i * Mathf.PI * 2f / sides;
                float rr = radius * rng.Range(0.6f, 1.35f);
                float x = basePt.x + Mathf.Cos(a) * rr;
                float z = basePt.z + Mathf.Sin(a) * rr;

                // Dropped a little into the ground so no daylight shows under the base.
                ring[i] = new Vector3(x, shape.Height(x, z) - radius * 0.25f, z);
            }

            // p1 before p0: a ring taken counter-clockwise in atan2 reads clockwise from above here,
            // so this is the order that puts the faces on the outside.
            for (int i = 0; i < sides; i++)
            {
                Vector3 p0 = ring[i];
                Vector3 p1 = ring[(i + 1) % sides];
                buf.AddTriangle(p1, p0, tip, slot, shade * (0.9f + 0.2f * (i % 2)));
            }
        }

        /// <summary>A boulder: two fans off a jittered waist, which is the cheapest thing that still
        /// reads as a rock rather than a ball.</summary>
        static void AddRock(VolcanoMeshBuffer buf, Vector3 center, float size, ref Rng rng,
                            VolcanoSlot slot, float shade)
        {
            const int sides = 6;
            var ring = new Vector3[sides];
            float phase = rng.Value() * Mathf.PI * 2f;

            for (int i = 0; i < sides; i++)
            {
                float a = phase + i * Mathf.PI * 2f / sides;
                float rr = size * rng.Range(0.55f, 1.1f);
                ring[i] = center + new Vector3(Mathf.Cos(a) * rr, rng.Signed(size * 0.18f), Mathf.Sin(a) * rr);
            }

            Vector3 top = center + new Vector3(rng.Signed(size * 0.3f), size * rng.Range(0.5f, 1.1f), rng.Signed(size * 0.3f));
            Vector3 bottom = center - Vector3.up * size * rng.Range(0.5f, 0.9f);

            for (int i = 0; i < sides; i++)
            {
                Vector3 p0 = ring[i];
                Vector3 p1 = ring[(i + 1) % sides];
                buf.AddTriangle(p1, p0, top, slot, shade * (0.92f + 0.16f * (i % 2)));
                buf.AddTriangle(p0, p1, bottom, slot, shade * 0.75f);
            }
        }
    }
}
