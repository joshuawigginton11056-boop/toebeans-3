using System.Collections.Generic;
using UnityEngine;

namespace PlayerPath
{
    /// <summary>
    /// Builds the path geometry from a solved <see cref="PathRoute"/>. Pure maths in, triangles out:
    /// no scene objects, no asset loading, no global state, so it can be called from the editor, at
    /// runtime, or from a test harness outside Unity altogether.
    ///
    /// The cross-section, from the middle outwards:
    ///   * an underlay running the full width, a joint's depth below the paving. This is the heat
    ///     trapped under the path, and it is all that is ever seen of it
    ///   * flagstones laid on that underlay, each pulled back from its neighbours so the joints
    ///     between them open onto the glow, and each hanging a wall down into it so a joint is a
    ///     slot with depth rather than a line painted on a flat sheet
    ///   * a seam at each edge where the paving stops short of the wall, so the glow runs down both
    ///     sides of the path as a continuous line
    ///   * a low wall on each side: a solid core, faced with brick courses laid in a running bond,
    ///     capped with a coping stone
    ///   * a foundation dropping from the outer face into the ground, so nothing shows daylight
    ///     underneath on the downhill side
    ///
    /// Lengthways the deck either ramps with the ground or breaks into treads and risers, which the
    /// route solver has already decided; the walls follow whichever it did, so the brickwork steps
    /// down a staircase rather than sliding past it on a ramp.
    /// </summary>
    public static class PathMeshBuilder
    {
        public const int SubmeshCount = 4;

        public static PathMeshBuffer Build(PathSettings settings, PathRoute route)
        {
            PathSettings s = settings ?? new PathSettings();
            var buf = new PathMeshBuffer(SubmeshCount, s.uvScale);
            if (route == null || !route.IsValid) return buf;

            BuildUnderlay(buf, s, route);
            BuildFlagstones(buf, s, route);
            BuildRisers(buf, s, route);

            if (s.wallHeight > 0.001f)
            {
                BuildWall(buf, s, route, -1);
                BuildWall(buf, s, route, +1);
            }

            BuildFoundation(buf, s, route);
            BuildEndCaps(buf, s, route);

            // Done last: both alternatives need the finished footprint.
            if (s.uvMode == PathUVMode.Normalized) buf.NormalizeUVs();
            else if (s.uvMode == PathUVMode.WorldPlanar) buf.WorldPlanarUVs();

            return buf;
        }

        // ================================================================== geometry helpers

        /// <summary>
        /// A point on the cross-section: <paramref name="across"/> metres from the centreline of
        /// station <paramref name="i"/> and <paramref name="up"/> metres above the deck — but taking
        /// its height from station <paramref name="heightOf"/>.
        ///
        /// That last part is what makes a staircase a staircase. A tread is level, so the far end of
        /// it sits at the near end's height even though it is at the next station's position; the
        /// riser then drops between the two. Pass the same station for both and the deck ramps.
        /// </summary>
        static Vector3 P(PathRoute r, int i, int heightOf, float across, float up)
        {
            PathStation st = r.Stations[i];
            float lift = Vector3.Dot(r.Stations[heightOf].Center - st.Center, r.Up);
            return st.Center + st.Right * across + r.Up * (up + lift);
        }

        /// <summary>Which station's height the far end of interval <paramref name="k"/> takes.</summary>
        static int EndHeight(PathRoute r, int k)
        {
            return r.Level != null && k < r.Level.Length && r.Level[k] ? k : k + 1;
        }

        /// <summary>Deck UV: metres across the path, metres along it.</summary>
        static Vector2 DeckUV(PathSettings s, PathRoute r, int i, float across)
        {
            float scale = s.uvScale <= 0f ? 1f : s.uvScale;
            return new Vector2(across / scale, r.Stations[i].Distance / scale);
        }

        /// <summary>Wall-face UV: metres along the path, metres up it. A brick texture put on this
        /// stands its courses the right way up.</summary>
        static Vector2 FaceUV(PathSettings s, float along, float up)
        {
            float scale = s.uvScale <= 0f ? 1f : s.uvScale;
            return new Vector2(along / scale, up / scale);
        }

        /// <summary>UV1: how far this vertex is from the edge of the path, 0 at the wall and 1 in
        /// the middle of the deck.</summary>
        static Vector2 Edge(float across, float halfWidth)
        {
            float t = halfWidth > 1e-4f ? 1f - Mathf.Clamp01(Mathf.Abs(across) / halfWidth) : 0f;
            return new Vector2(1f, t);
        }

        // ================================================================== underlay

        /// <summary>
        /// The sheet under the paving. It is emitted whole, under everything else: the flagstones
        /// above only cover part of it, and what is left showing between them and along both seams
        /// is the glow.
        /// </summary>
        static void BuildUnderlay(PathMeshBuffer buf, PathSettings s, PathRoute r)
        {
            PathSlot slot = s.glowingJoints ? PathSlot.Glow : PathSlot.Trim;
            float depth = -s.jointDepth;
            int columns = Mathf.Max(2, s.lateralSegments);

            for (int k = 0; k < r.Count - 1; k++)
            {
                int hEnd = EndHeight(r, k);
                float spanA = r.Stations[k].HalfWidth + s.seamWidth;
                float spanB = r.Stations[k + 1].HalfWidth + s.seamWidth;

                for (int q = 0; q < columns; q++)
                {
                    float t0 = -1f + 2f * q / columns;
                    float t1 = -1f + 2f * (q + 1) / columns;

                    Vector3 a = P(r, k, k, t0 * spanA, depth);
                    Vector3 b = P(r, k, k, t1 * spanA, depth);
                    Vector3 c = P(r, k + 1, hEnd, t0 * spanB, depth);
                    Vector3 d = P(r, k + 1, hEnd, t1 * spanB, depth);

                    float shade = 0.9f + PathNoise.Value(k * 0.4f, q * 0.7f, s.seed + 61) * 0.35f;

                    buf.AddQuad(a, b, c, d,
                                DeckUV(s, r, k, t0 * spanA), DeckUV(s, r, k, t1 * spanA),
                                DeckUV(s, r, k + 1, t0 * spanB), DeckUV(s, r, k + 1, t1 * spanB),
                                r.Up, slot, shade);
                }
            }
        }

        // ================================================================== paving

        /// <summary>
        /// The flagstones.
        ///
        /// The joint network is the trick the lava crust uses, and it works here for the same
        /// reason: rather than modelling the gaps, every stone keeps the corners it owns outright
        /// and pulls back the ones it shares with a neighbour, so the surface opens up exactly along
        /// the stone boundaries and nowhere else. Each stone then hangs a short wall down to the
        /// underlay, so a joint has depth and the glow reads as coming from beneath the path rather
        /// than being drawn on it.
        /// </summary>
        static void BuildFlagstones(PathMeshBuffer buf, PathSettings s, PathRoute r)
        {
            int n = r.Count;
            int cols = Mathf.Max(1, s.lateralSegments);
            if (n < 2) return;

            var field = new PaveField(s);
            var stoneOf = new int[n - 1, cols];
            var rise = new float[n - 1, cols];

            float coverage = 1f - Mathf.Clamp01(s.brokenStones);

            for (int k = 0; k < n - 1; k++)
            {
                float along = 0.5f * (r.Stations[k].Distance + r.Stations[k + 1].Distance);

                for (int q = 0; q < cols; q++)
                {
                    float across = AcrossOf(r, k, q + 0.5f, cols);
                    int id = field.Id(along, across);

                    if (PathNoise.Hash1(id, s.seed + 3) > coverage)
                    {
                        stoneOf[k, q] = int.MinValue; // this stone is missing: the underlay shows
                        continue;
                    }

                    stoneOf[k, q] = id;
                    rise[k, q] = (PathNoise.Hash1(id, s.seed + 29) - 0.5f) * 2f * s.flagstoneRelief;
                }
            }

            // A corner shared by two stones is one that has to pull back; a corner in the middle of
            // a stone stays put, which is what keeps each stone welded to itself.
            var shared = new bool[n, cols + 1];
            for (int i = 0; i < n; i++)
                for (int q = 0; q <= cols; q++)
                    shared[i, q] = IsShared(stoneOf, n - 1, cols, i, q);

            float pull = s.jointWidth * 0.5f;

            for (int k = 0; k < n - 1; k++)
            {
                int hEnd = EndHeight(r, k);
                int id = 0;

                for (int q = 0; q < cols; q++)
                {
                    id = stoneOf[k, q];
                    if (id == int.MinValue) continue;

                    float a0 = AcrossOf(r, k, q, cols);
                    float a1 = AcrossOf(r, k, q + 1, cols);
                    float b0 = AcrossOf(r, k + 1, q, cols);
                    float b1 = AcrossOf(r, k + 1, q + 1, cols);
                    float h = rise[k, q];

                    Vector3 a = P(r, k, k, a0, h);
                    Vector3 b = P(r, k, k, a1, h);
                    Vector3 c = P(r, k + 1, hEnd, b0, h);
                    Vector3 d = P(r, k + 1, hEnd, b1, h);

                    Vector3 mid = (a + b + c + d) * 0.25f;
                    if (shared[k, q]) a = PullIn(a, mid, pull);
                    if (shared[k, q + 1]) b = PullIn(b, mid, pull);
                    if (shared[k + 1, q]) c = PullIn(c, mid, pull);
                    if (shared[k + 1, q + 1]) d = PullIn(d, mid, pull);

                    float shade = 0.82f + PathNoise.Hash1(id, s.seed + 97) * 0.4f;
                    float hwA = r.Stations[k].HalfWidth;
                    float hwB = r.Stations[k + 1].HalfWidth;

                    buf.AddQuad(a, b, c, d,
                                DeckUV(s, r, k, a0), DeckUV(s, r, k, a1),
                                DeckUV(s, r, k + 1, b0), DeckUV(s, r, k + 1, b1),
                                Edge(a0, hwA), Edge(a1, hwA), Edge(b0, hwB), Edge(b1, hwB),
                                r.Up, PathSlot.Deck, shade);

                    // Sides, wherever this quad's edge is also the stone's edge, dropped to the
                    // underlay so the joint is a slot rather than a stripe.
                    float floor = -s.jointDepth;
                    Vector3 aB = P(r, k, k, a0, floor);
                    Vector3 bB = P(r, k, k, a1, floor);
                    Vector3 cB = P(r, k + 1, hEnd, b0, floor);
                    Vector3 dB = P(r, k + 1, hEnd, b1, floor);

                    if (Differs(stoneOf, n - 1, cols, k - 1, q, id)) AddJointWall(buf, s, mid, a, b, aB, bB, shade);
                    if (Differs(stoneOf, n - 1, cols, k + 1, q, id)) AddJointWall(buf, s, mid, c, d, cB, dB, shade);
                    if (Differs(stoneOf, n - 1, cols, k, q - 1, id)) AddJointWall(buf, s, mid, a, c, aB, cB, shade);
                    if (Differs(stoneOf, n - 1, cols, k, q + 1, id)) AddJointWall(buf, s, mid, b, d, bB, dB, shade);
                }
            }
        }

        /// <summary>Metres from the centreline of column <paramref name="q"/> of the paving grid.</summary>
        static float AcrossOf(PathRoute r, int i, float q, int cols)
        {
            return (-1f + 2f * q / cols) * r.Stations[i].HalfWidth;
        }

        /// <summary>True when the quad next door belongs to a different stone, or to none at all.</summary>
        static bool Differs(int[,] stoneOf, int rows, int cols, int i, int q, int id)
        {
            if (i < 0 || i >= rows || q < 0 || q >= cols) return true;
            return stoneOf[i, q] != id;
        }

        /// <summary>True when the corner at (i, q) is on a boundary between stones.</summary>
        static bool IsShared(int[,] stoneOf, int rows, int cols, int i, int q)
        {
            int found = int.MinValue + 1;
            for (int di = -1; di <= 0; di++)
            {
                for (int dq = -1; dq <= 0; dq++)
                {
                    int ii = i + di;
                    int qq = q + dq;
                    if (ii < 0 || ii >= rows || qq < 0 || qq >= cols) return true; // edge of the deck
                    int id = stoneOf[ii, qq];
                    if (found == int.MinValue + 1) found = id;
                    else if (id != found) return true;
                }
            }
            return false;
        }

        static Vector3 PullIn(Vector3 corner, Vector3 center, float amount)
        {
            Vector3 d = center - corner;
            float len = d.magnitude;
            if (len < 1e-5f) return corner;
            return corner + d * (Mathf.Min(amount, len * 0.45f) / len);
        }

        /// <summary>The side of a flagstone, facing away from the stone it belongs to.</summary>
        static void AddJointWall(PathMeshBuffer buf, PathSettings s, Vector3 stoneCenter,
                                 Vector3 topA, Vector3 topB, Vector3 botA, Vector3 botB, float shade)
        {
            Vector3 outward = (topA + topB) * 0.5f - stoneCenter;
            Vector2 uv = FaceUV(s, 0f, 0f);
            buf.AddQuad(topA, topB, botA, botB, uv, uv, uv, uv, outward, PathSlot.Deck, shade * 0.75f);
        }

        // ================================================================== risers

        /// <summary>
        /// The face of each step. It runs from the tread above down past the underlay of the tread
        /// below, so a staircase is closed from every angle it can be looked at.
        /// </summary>
        static void BuildRisers(PathMeshBuffer buf, PathSettings s, PathRoute r)
        {
            int cols = Mathf.Max(1, s.lateralSegments);

            for (int i = 1; i < r.Count; i++)
            {
                if (Mathf.Abs(r.Stations[i].Riser) < 1e-4f) continue;

                // The tread above is at station i-1's height; the one below at station i's.
                for (int q = 0; q < cols; q++)
                {
                    float a0 = AcrossOf(r, i, q, cols) - s.jointWidth * 0.25f;
                    float a1 = AcrossOf(r, i, q + 1, cols) + s.jointWidth * 0.25f;

                    Vector3 topA = P(r, i, i - 1, a0, 0f);
                    Vector3 topB = P(r, i, i - 1, a1, 0f);
                    Vector3 botA = P(r, i, i, a0, -s.jointDepth);
                    Vector3 botB = P(r, i, i, a1, -s.jointDepth);

                    float shade = 0.8f + PathNoise.Value(i * 0.7f, q * 0.5f, s.seed + 211) * 0.3f;
                    float drop = Mathf.Abs(r.Stations[i].Riser);

                    buf.AddQuad(topA, topB, botA, botB,
                                FaceUV(s, a0, drop), FaceUV(s, a1, drop),
                                FaceUV(s, a0, 0f), FaceUV(s, a1, 0f),
                                -r.Stations[i].Forward, PathSlot.Trim, shade);
                }
            }
        }

        // ================================================================== walls

        /// <summary>
        /// How tall the wall is at this distance along the path, and how far it bulges outwards
        /// there. This one function is the whole difference between the edge styles: a plain wall is
        /// a constant, a battlement is a square wave, and a run of pillars is the same square wave
        /// with the posts also standing proud.
        ///
        /// The posts only grow outwards, so whichever style is chosen the player keeps exactly the
        /// same walkable width.
        /// </summary>
        static float WallTopAt(PathSettings s, float distance, out float bulge)
        {
            bulge = 0f;

            switch (s.edgeStyle)
            {
                case PathEdgeStyle.Battlement:
                {
                    float period = Mathf.Max(0.3f, s.featureSpacing);
                    float phase = distance / period;
                    phase -= Mathf.Floor(phase);
                    // The crenels drop to half height rather than to the deck: this is a railing
                    // first and a silhouette second, and a gap the player can walk out of is not a
                    // railing at all.
                    return phase < s.featureSize ? s.wallHeight : s.wallHeight * 0.5f;
                }

                case PathEdgeStyle.Pillars:
                {
                    float period = Mathf.Max(0.3f, s.featureSpacing);
                    float phase = distance / period;
                    phase -= Mathf.Floor(phase);
                    if (phase < s.featureSize)
                    {
                        bulge = s.featureBulge;
                        return s.wallHeight * 1.3f;
                    }
                    return s.wallHeight * 0.55f;
                }

                default:
                    return s.wallHeight;
            }
        }

        static float MaxWallTop(PathSettings s)
        {
            return s.edgeStyle == PathEdgeStyle.Pillars ? s.wallHeight * 1.3f : s.wallHeight;
        }

        /// <summary>
        /// One edge of the path: the core, the brick facing and the coping.
        ///
        /// <paramref name="side"/> is -1 for the left edge and +1 for the right, looking along the
        /// path. Everything below is written once and mirrored by that sign, which is exactly the
        /// sort of place a hand-wound triangle ends up facing into the hillside — so every quad here
        /// is told which way it should face and works out its own winding.
        /// </summary>
        static void BuildWall(PathMeshBuffer buf, PathSettings s, PathRoute r, int side)
        {
            List<float> breaks = WallBreaks(s, r);
            float baseZ = -s.jointDepth;
            float recess = 0.015f + s.mortarGap * 0.5f;

            for (int b = 0; b < breaks.Count - 1; b++)
            {
                float d0 = breaks[b];
                float d1 = breaks[b + 1];
                if (d1 - d0 < 1e-3f) continue;

                float mid = (d0 + d1) * 0.5f;
                float bulge;
                float top = WallTopAt(s, mid, out bulge);
                float capBase = Mathf.Max(baseZ + 0.01f, top - s.capHeight);

                // Read both ends out of the interval this segment sits in. A break lands exactly on
                // a station, and a station on a staircase belongs to two treads a riser apart.
                int k = r.IntervalAt(mid);

                Vector3 c0, f0, rt0, c1, f1, rt1;
                float hw0, hw1;
                r.FrameIn(k, d0, out c0, out f0, out rt0, out hw0);
                r.FrameIn(k, d1, out c1, out f1, out rt1, out hw1);

                float in0 = side * (hw0 + s.seamWidth);
                float in1 = side * (hw1 + s.seamWidth);
                float out0 = in0 + side * (s.wallThickness + bulge);
                float out1 = in1 + side * (s.wallThickness + bulge);

                // The core is set back from the brick faces, so the mortar joints between the
                // bricks are recesses with something behind them rather than holes through the wall.
                float coreIn0 = in0 + side * recess;
                float coreIn1 = in1 + side * recess;
                float coreOut0 = out0 - side * recess;
                float coreOut1 = out1 - side * recess;

                float shade = 0.85f + PathNoise.Value(d0 * 0.4f, side * 3.1f, s.seed + 401) * 0.25f;

                // Inner and outer faces of the core.
                AddFace(buf, s,
                        c0 + rt0 * coreIn0 + r.Up * baseZ, c1 + rt1 * coreIn1 + r.Up * baseZ,
                        c0 + rt0 * coreIn0 + r.Up * capBase, c1 + rt1 * coreIn1 + r.Up * capBase,
                        d0, d1, baseZ, capBase, -side * rt0, PathSlot.Trim, shade);

                AddFace(buf, s,
                        c0 + rt0 * coreOut0 + r.Up * baseZ, c1 + rt1 * coreOut1 + r.Up * baseZ,
                        c0 + rt0 * coreOut0 + r.Up * capBase, c1 + rt1 * coreOut1 + r.Up * capBase,
                        d0, d1, baseZ, capBase, side * rt0, PathSlot.Trim, shade * 0.92f);

                // Ends of the core, wherever the wall next door is a different height. This is what
                // closes the side of a merlon and the end of a run of wall at a step.
                AddWallEnd(buf, s, r, k, d0, side, in0, out0, baseZ, capBase, top, -f0, breaks, b, true);
                AddWallEnd(buf, s, r, k, d1, side, in1, out1, baseZ, capBase, top, f1, breaks, b, false);

                BuildCap(buf, s, r, k, d0, d1, side, in0, in1, out0, out1, capBase, top, shade);
            }

            BuildBricks(buf, s, r, side, breaks, baseZ, recess);
        }

        /// <summary>
        /// Where the wall has to be cut. Every station, so it follows the route; every step, so the
        /// brickwork breaks cleanly at a riser instead of stretching over one; and every merlon or
        /// post boundary, so those come out square rather than sampled.
        /// </summary>
        static List<float> WallBreaks(PathSettings s, PathRoute r)
        {
            var breaks = new List<float>(r.Count + 32);
            for (int i = 0; i < r.Count; i++) breaks.Add(r.Stations[i].Distance);

            if (s.edgeStyle == PathEdgeStyle.Battlement || s.edgeStyle == PathEdgeStyle.Pillars)
            {
                float period = Mathf.Max(0.3f, s.featureSpacing);
                int count = Mathf.Min(4000, Mathf.CeilToInt(r.Length / period) + 1);
                for (int k = 0; k <= count; k++)
                {
                    breaks.Add(k * period);
                    breaks.Add(k * period + period * s.featureSize);
                }
            }

            breaks.Sort();

            var cleaned = new List<float>(breaks.Count);
            for (int i = 0; i < breaks.Count; i++)
            {
                float d = Mathf.Clamp(breaks[i], 0f, r.Length);
                if (cleaned.Count > 0 && d - cleaned[cleaned.Count - 1] < 0.02f) continue;
                cleaned.Add(d);
            }

            if (cleaned.Count < 2) cleaned.Add(r.Length);
            return cleaned;
        }

        /// <summary>
        /// Closes the end of a wall segment where the wall beside it is lower or has run out: the
        /// side of every merlon, the end of every post, and the two ends of the whole path.
        ///
        /// It comes in two pieces, because the coping overhangs the wall it sits on and a single
        /// face for both would either leave the coping's end open or stand proud of the brickwork.
        /// </summary>
        static void AddWallEnd(PathMeshBuffer buf, PathSettings s, PathRoute r, int interval,
                               float d, int side, float inner, float outer, float baseZ,
                               float capBase, float top, Vector3 outward,
                               List<float> breaks, int index, bool before)
        {
            float neighbourTop;
            if (before)
            {
                if (index == 0) neighbourTop = baseZ;
                else
                {
                    float bulge;
                    neighbourTop = WallTopAt(s, (breaks[index - 1] + breaks[index]) * 0.5f, out bulge);
                }
            }
            else
            {
                if (index + 2 >= breaks.Count) neighbourTop = baseZ;
                else
                {
                    float bulge;
                    neighbourTop = WallTopAt(s, (breaks[index + 1] + breaks[index + 2]) * 0.5f, out bulge);
                }
            }

            if (neighbourTop >= top - 1e-3f) return; // the wall carries on at least this high

            // Set the face a hair inside the segment. The last brick of the run ends on exactly
            // this plane, and two coplanar faces fight for the same pixels — which reads as a
            // scattering of speckle over the end of every merlon.
            Vector3 c, f, rt;
            float hw;
            r.FrameIn(interval, d + (before ? 0.012f : -0.012f), out c, out f, out rt, out hw);

            // The brickwork below the coping.
            float wallFrom = Mathf.Clamp(neighbourTop, baseZ, capBase);
            if (capBase - wallFrom > 1e-3f)
            {
                buf.AddQuad(c + rt * inner + r.Up * wallFrom, c + rt * outer + r.Up * wallFrom,
                            c + rt * inner + r.Up * capBase, c + rt * outer + r.Up * capBase,
                            FaceUV(s, inner, wallFrom), FaceUV(s, outer, wallFrom),
                            FaceUV(s, inner, capBase), FaceUV(s, outer, capBase),
                            outward, PathSlot.Trim, 0.8f);
            }

            // The end of the coping stone itself, out at its overhang.
            float capFrom = Mathf.Max(neighbourTop, capBase);
            if (top - capFrom <= 1e-3f) return;

            float ci = inner - side * s.capOverhang;
            float co = outer + side * s.capOverhang;

            buf.AddQuad(c + rt * ci + r.Up * capFrom, c + rt * co + r.Up * capFrom,
                        c + rt * ci + r.Up * top, c + rt * co + r.Up * top,
                        FaceUV(s, ci, capFrom), FaceUV(s, co, capFrom),
                        FaceUV(s, ci, top), FaceUV(s, co, top),
                        outward, PathSlot.Trim, 0.85f);
        }

        /// <summary>The coping stone along the top of the wall. It overhangs both faces, which is
        /// what gives the edge a hard line to read against the ground behind it.</summary>
        static void BuildCap(PathMeshBuffer buf, PathSettings s, PathRoute r, int interval,
                             float d0, float d1,
                             int side, float in0, float in1, float out0, float out1,
                             float capBase, float top, float shade)
        {
            if (s.capHeight <= 0.001f) return;

            Vector3 c0, f0, rt0, c1, f1, rt1;
            float hw0, hw1;
            r.FrameIn(interval, d0, out c0, out f0, out rt0, out hw0);
            r.FrameIn(interval, d1, out c1, out f1, out rt1, out hw1);

            float ci0 = in0 - side * s.capOverhang;
            float ci1 = in1 - side * s.capOverhang;
            float co0 = out0 + side * s.capOverhang;
            float co1 = out1 + side * s.capOverhang;

            // Top.
            buf.AddQuad(c0 + rt0 * ci0 + r.Up * top, c0 + rt0 * co0 + r.Up * top,
                        c1 + rt1 * ci1 + r.Up * top, c1 + rt1 * co1 + r.Up * top,
                        FaceUV(s, ci0, d0), FaceUV(s, co0, d0),
                        FaceUV(s, ci1, d1), FaceUV(s, co1, d1),
                        r.Up, PathSlot.Trim, shade * 1.05f);

            // The two faces of the coping.
            AddFace(buf, s,
                    c0 + rt0 * ci0 + r.Up * capBase, c1 + rt1 * ci1 + r.Up * capBase,
                    c0 + rt0 * ci0 + r.Up * top, c1 + rt1 * ci1 + r.Up * top,
                    d0, d1, capBase, top, -side * rt0, PathSlot.Trim, shade);

            AddFace(buf, s,
                    c0 + rt0 * co0 + r.Up * capBase, c1 + rt1 * co1 + r.Up * capBase,
                    c0 + rt0 * co0 + r.Up * top, c1 + rt1 * co1 + r.Up * top,
                    d0, d1, capBase, top, side * rt0, PathSlot.Trim, shade * 0.9f);
        }

        /// <summary>A vertical face running along the path, from (d0, low) to (d1, high).</summary>
        static void AddFace(PathMeshBuffer buf, PathSettings s, Vector3 a0, Vector3 a1,
                            Vector3 b0, Vector3 b1, float d0, float d1, float low, float high,
                            Vector3 outward, PathSlot slot, float shade)
        {
            buf.AddQuad(a0, a1, b0, b1,
                        FaceUV(s, d0, low), FaceUV(s, d1, low),
                        FaceUV(s, d0, high), FaceUV(s, d1, high),
                        outward, slot, shade);
        }

        // ================================================================== brickwork

        /// <summary>
        /// The brick facing. Courses are walked in their own units along the path rather than
        /// station by station, with every other course offset half a brick so the joints break —
        /// stack them and the wall reads as tiling immediately.
        ///
        /// Bricks are stopped at every break in the wall, so none of them spans a step or hangs off
        /// the end of a merlon.
        /// </summary>
        static void BuildBricks(PathMeshBuffer buf, PathSettings s, PathRoute r, int side,
                                List<float> breaks, float baseZ, float recess)
        {
            bool kerb = s.edgeStyle == PathEdgeStyle.Kerb;

            float maxTop = MaxWallTop(s);
            float courseHeight = kerb ? Mathf.Max(0.06f, maxTop - s.capHeight - baseZ) : s.brickCourse;
            float brickLength = kerb ? s.brickLength * 1.8f : s.brickLength;
            float step = courseHeight + s.mortarGap;
            int courses = Mathf.Clamp(Mathf.CeilToInt((maxTop - baseZ) / Mathf.Max(0.01f, step)), 1, 64);

            var rng = new Rng(s.seed ^ (side * 7919));

            for (int c = 0; c < courses; c++)
            {
                float z0 = baseZ + c * step;
                float z1 = z0 + courseHeight;
                if (z0 >= maxTop - 1e-3f) break;

                // Running bond: every other course starts half a brick along.
                float offset = (c & 1) == 0 ? 0f : brickLength * 0.5f;

                for (int b = 0; b < breaks.Count - 1; b++)
                {
                    float segStart = breaks[b];
                    float segEnd = breaks[b + 1];
                    if (segEnd - segStart < 0.03f) continue;

                    float bulge;
                    float top = WallTopAt(s, (segStart + segEnd) * 0.5f, out bulge);
                    if (z1 > top - s.capHeight + 1e-3f) continue; // above the wall here

                    // Every brick in this run belongs to one interval, whatever its ends land on.
                    int interval = r.IntervalAt((segStart + segEnd) * 0.5f);

                    // Start the run at the same phase everywhere, so the bond does not restart at
                    // every station and give away where the cross-sections are.
                    float first = Mathf.Floor((segStart - offset) / (brickLength + s.mortarGap))
                                  * (brickLength + s.mortarGap) + offset;

                    for (float d = first; d < segEnd; d += brickLength + s.mortarGap)
                    {
                        float d0 = Mathf.Max(d, segStart);
                        float d1 = Mathf.Min(d + brickLength, segEnd);
                        if (d1 - d0 < 0.04f) continue;

                        AddBrick(buf, s, r, interval, side, d0, d1, z0, z1, bulge, recess, ref rng);
                    }
                }
            }
        }

        /// <summary>One brick: five faces, the sixth being the one against the course below.</summary>
        static void AddBrick(PathMeshBuffer buf, PathSettings s, PathRoute r, int interval, int side,
                             float d0, float d1, float z0, float z1, float bulge, float recess,
                             ref Rng rng)
        {
            Vector3 c0, f0, rt0, c1, f1, rt1;
            float hw0, hw1;
            r.FrameIn(interval, d0, out c0, out f0, out rt0, out hw0);
            r.FrameIn(interval, d1, out c1, out f1, out rt1, out hw1);

            // A hand-laid wall has every brick sitting a little proud, a little low, a little back.
            float jitter = s.brickJitter;
            float outward = rng.Signed(0.02f * jitter);
            float lift = rng.Signed(0.012f * jitter);
            float shrink = rng.Range(0f, 0.02f * jitter);

            float in0 = side * (hw0 + s.seamWidth) - side * outward;
            float in1 = side * (hw1 + s.seamWidth) - side * outward;
            float out0 = in0 + side * (s.wallThickness + bulge);
            float out1 = in1 + side * (s.wallThickness + bulge);

            float lo = z0 + lift + shrink;
            float hi = z1 + lift - shrink;
            if (hi - lo < 0.01f) return;

            bool hot = rng.Chance(s.hotBrickChance);
            PathSlot slot = hot ? PathSlot.Glow : PathSlot.Edge;
            float shade = hot ? 1f : rng.Range(0.75f, 1.2f);

            Vector3 iLo0 = c0 + rt0 * in0 + r.Up * lo;
            Vector3 oLo0 = c0 + rt0 * out0 + r.Up * lo;
            Vector3 iLo1 = c1 + rt1 * in1 + r.Up * lo;
            Vector3 oLo1 = c1 + rt1 * out1 + r.Up * lo;
            Vector3 iHi0 = c0 + rt0 * in0 + r.Up * hi;
            Vector3 oHi0 = c0 + rt0 * out0 + r.Up * hi;
            Vector3 iHi1 = c1 + rt1 * in1 + r.Up * hi;
            Vector3 oHi1 = c1 + rt1 * out1 + r.Up * hi;

            // Top.
            buf.AddQuad(iHi0, oHi0, iHi1, oHi1,
                        FaceUV(s, in0, d0), FaceUV(s, out0, d0),
                        FaceUV(s, in1, d1), FaceUV(s, out1, d1),
                        r.Up, slot, shade * 1.06f);

            // The face the player walks past, and the one hanging over the drop.
            buf.AddQuad(iLo0, iLo1, iHi0, iHi1,
                        FaceUV(s, d0, lo), FaceUV(s, d1, lo),
                        FaceUV(s, d0, hi), FaceUV(s, d1, hi),
                        -side * rt0, slot, shade);

            buf.AddQuad(oLo0, oLo1, oHi0, oHi1,
                        FaceUV(s, d0, lo), FaceUV(s, d1, lo),
                        FaceUV(s, d0, hi), FaceUV(s, d1, hi),
                        side * rt0, slot, shade * 0.88f);

            // The two ends, which is what makes the mortar gap read as a gap.
            buf.AddQuad(iLo0, oLo0, iHi0, oHi0,
                        FaceUV(s, in0, lo), FaceUV(s, out0, lo),
                        FaceUV(s, in0, hi), FaceUV(s, out0, hi),
                        -f0, slot, shade * 0.8f);

            buf.AddQuad(iLo1, oLo1, iHi1, oHi1,
                        FaceUV(s, in1, lo), FaceUV(s, out1, lo),
                        FaceUV(s, in1, hi), FaceUV(s, out1, hi),
                        f1, slot, shade * 0.8f);
        }

        // ================================================================== foundation

        /// <summary>
        /// Drops a wall from the outer edge into the ground. A path cut across a hillside is holding
        /// itself up on the downhill side, and without this it shows daylight under its own edge on
        /// every bump it crosses — which on a mountain is all of them.
        /// </summary>
        static void BuildFoundation(PathMeshBuffer buf, PathSettings s, PathRoute r)
        {
            for (int k = 0; k < r.Count - 1; k++)
            {
                int hEnd = EndHeight(r, k);
                BuildFoundationSide(buf, s, r, k, hEnd, -1);
                BuildFoundationSide(buf, s, r, k, hEnd, +1);
            }
        }

        static void BuildFoundationSide(PathMeshBuffer buf, PathSettings s, PathRoute r, int k,
                                        int hEnd, int side)
        {
            float a0 = side * (r.Stations[k].HalfWidth + s.seamWidth + s.wallThickness);
            float a1 = side * (r.Stations[k + 1].HalfWidth + s.seamWidth + s.wallThickness);

            float drop0 = FoundationDepth(s, r, k, a0);
            float drop1 = FoundationDepth(s, r, k + 1, a1);

            Vector3 topA = P(r, k, k, a0, -s.jointDepth);
            Vector3 topB = P(r, k + 1, hEnd, a1, -s.jointDepth);

            // The top of the foundation follows the tread, but its bottom is buried in the ground,
            // so it is measured from the station's own height rather than the tread's. Taking both
            // from the tread leaves the foundation hanging a riser short of the hill under a
            // staircase, which shows as daylight beneath every step.
            Vector3 botA = P(r, k, k, a0, -drop0);
            Vector3 botB = P(r, k + 1, k + 1, a1, -drop1);

            float shade = 0.65f + PathNoise.Value(k * 0.3f, side * 5.7f, s.seed + 733) * 0.25f;

            buf.AddQuad(topA, topB, botA, botB,
                        FaceUV(s, r.Stations[k].Distance, 0f), FaceUV(s, r.Stations[k + 1].Distance, 0f),
                        FaceUV(s, r.Stations[k].Distance, -drop0), FaceUV(s, r.Stations[k + 1].Distance, -drop1),
                        side * r.Stations[k].Right, PathSlot.Trim, shade);
        }

        /// <summary>How far the foundation has to reach at this station to bury itself.</summary>
        static float FoundationDepth(PathSettings s, PathRoute r, int i, float across)
        {
            float drop = r.GroundDrop(i, across) + s.embedDepth;
            return Mathf.Clamp(drop, s.jointDepth + 0.05f, Mathf.Max(0.1f, s.maxFoundation));
        }

        // ================================================================== ends

        /// <summary>Closes both ends of the path, so it does not read as a hollow shell where it
        /// meets a doorway or the flat ground at the bottom.</summary>
        static void BuildEndCaps(PathMeshBuffer buf, PathSettings s, PathRoute r)
        {
            CapEnd(buf, s, r, 0, -r.Stations[0].Forward);
            CapEnd(buf, s, r, r.Count - 1, r.Stations[r.Count - 1].Forward);
        }

        static void CapEnd(PathMeshBuffer buf, PathSettings s, PathRoute r, int i, Vector3 outward)
        {
            int cols = Mathf.Max(2, s.lateralSegments);
            float span = r.Stations[i].HalfWidth + s.seamWidth + s.wallThickness;

            for (int q = 0; q < cols; q++)
            {
                float a0 = (-1f + 2f * q / cols) * span;
                float a1 = (-1f + 2f * (q + 1) / cols) * span;

                float d0 = FoundationDepth(s, r, i, a0);
                float d1 = FoundationDepth(s, r, i, a1);

                Vector3 topA = P(r, i, i, a0, 0f);
                Vector3 topB = P(r, i, i, a1, 0f);
                Vector3 botA = P(r, i, i, a0, -d0);
                Vector3 botB = P(r, i, i, a1, -d1);

                buf.AddQuad(topA, topB, botA, botB,
                            FaceUV(s, a0, 0f), FaceUV(s, a1, 0f),
                            FaceUV(s, a0, -d0), FaceUV(s, a1, -d1),
                            outward, PathSlot.Trim, 0.7f);
            }
        }

        // ================================================================== paving field

        /// <summary>
        /// Breaks the deck into flagstones: a jittered Voronoi field measured in metres along and
        /// across the path, so the stones keep their real size whatever the path is doing.
        /// </summary>
        public sealed class PaveField
        {
            readonly float _size;
            readonly float _jitter;
            readonly int _seed;

            public PaveField(PathSettings s)
            {
                _size = Mathf.Max(0.15f, s.flagstoneSize);
                _jitter = Mathf.Clamp01(s.flagstoneJitter);
                _seed = s.seed + 1013;
            }

            /// <summary>Id of the stone covering this point. Stable for a given seed.</summary>
            public int Id(float alongMetres, float acrossMetres)
            {
                float x = alongMetres / _size;
                float y = acrossMetres / _size;
                int xi = Mathf.FloorToInt(x);
                int yi = Mathf.FloorToInt(y);

                float best = float.MaxValue;
                int bestId = 0;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int cx = xi + dx;
                        int cy = yi + dy;
                        float sx = cx + 0.5f + (PathNoise.Hash(cx, cy, _seed) - 0.5f) * _jitter;
                        float sy = cy + 0.5f + (PathNoise.Hash(cx, cy, _seed + 517) - 0.5f) * _jitter;

                        float ddx = x - sx;
                        float ddy = y - sy;
                        float d = ddx * ddx + ddy * ddy;
                        if (d < best)
                        {
                            best = d;
                            bestId = unchecked(cx * 73856093 ^ cy * 19349663);
                        }
                    }
                }

                return bestId;
            }
        }
    }
}
