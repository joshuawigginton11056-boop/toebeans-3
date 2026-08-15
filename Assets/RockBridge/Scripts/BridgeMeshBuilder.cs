using System.Collections.Generic;
using UnityEngine;

namespace RockBridge
{
    /// <summary>
    /// Looks up the floor under a point in the generator's local space, falling back to what the
    /// solved path already measured at the nearest cross-section.
    ///
    /// It exists so the legs and the landing fill can follow ground that varies <em>across</em> the
    /// bridge as well as along it: a leg six metres wide standing on a slope has one corner metres
    /// higher than the other, and a flat foot cut at the centre height either floats on one side or
    /// buries itself on the other. Both look like a bug in the generator.
    ///
    /// A null sampler is fine and is what the headless harness passes — every query then answers
    /// with the fallback, which is the path's own per-section measurement.
    /// </summary>
    public struct GroundProbe
    {
        public IBridgeGround Ground;
        public Matrix4x4 ToWorld;
        public Matrix4x4 ToLocal;

        public static GroundProbe None
        {
            get { return new GroundProbe { ToWorld = Matrix4x4.identity, ToLocal = Matrix4x4.identity }; }
        }

        /// <summary>Local Y of the solid floor under <paramref name="local"/>.</summary>
        public float FloorAt(Vector3 local, float fallback)
        {
            if (Ground == null) return fallback;

            GroundSample g;
            Vector3 world = ToWorld.MultiplyPoint3x4(local);
            if (!Ground.Sample(world, out g) || !g.Found) return fallback;

            return ToLocal.MultiplyPoint3x4(new Vector3(world.x, g.Floor, world.z)).y;
        }
    }

    /// <summary>
    /// Sweeps the cross-section from <see cref="BridgeProfile"/> along the crossing from
    /// <see cref="BridgePath"/>, then hangs the rock off it — the legs down to the ground and the
    /// fill where it lands on the shore.
    ///
    /// The two hard parts of the deck are solved elsewhere and deliberately so: the path owns the
    /// frames, the heights and the banking; the profile owns the shape and every face direction.
    /// What is left of the deck here is bookkeeping — one row of vertices per cross-section,
    /// stitched run by run, welded along the length and left hard between runs.
    ///
    /// The rock is this file's own work, and three things about it are load-bearing:
    ///
    /// <b>A leg's top is cut parallel to the deck, not level.</b> The leg stands up world-vertical,
    /// as a leg must, but its cap follows the deck's own plane — so on a banked or climbing section
    /// it stays buried inside the slab at both edges instead of poking out of the low side.
    ///
    /// <b>A leg's foot follows the ground under each corner</b>, through <see cref="GroundProbe"/>,
    /// rather than sitting flat at the height measured at its centre.
    ///
    /// <b>Nothing rock ever lands exactly on another surface.</b> Legs push up into the slab by
    /// <see cref="BridgeSettings.pierTopEmbed"/> and down into the ground by
    /// <see cref="BridgeSettings.footingDepth"/>; the landing fill sinks the same way. Two surfaces
    /// meeting in the same plane is what flickers as the camera moves, and no amount of tuning
    /// elsewhere fixes it.
    ///
    /// Pure managed maths — no scene objects and no asset loading — so this runs in the headless
    /// harness with a synthetic hillside handed in.
    /// </summary>
    public static class BridgeMeshBuilder
    {
        const int SubmeshCount = 4; // Deck, Verge, Parapet, Rock

        public static BridgeMeshBuffer Build(IList<BridgeNode> nodes, BridgeSettings settings)
        {
            if (settings == null) return new BridgeMeshBuffer(SubmeshCount);
            return Build(BridgePath.Build(nodes, settings, null), settings, GroundProbe.None);
        }

        public static BridgeMeshBuffer Build(BridgePath path, BridgeSettings settings, GroundProbe probe)
        {
            var buf = new BridgeMeshBuffer(SubmeshCount);
            if (path == null || settings == null || path.Samples.Count < 2) return buf;

            buf.Length = path.Length;

            BuildDeck(buf, path, settings);
            BuildEndCaps(buf, path, settings);
            if (settings.buildAbutments) BuildLandingFill(buf, path, settings, probe);
            if (settings.buildPiers) BuildPiers(buf, path, settings, probe);

            buf.NormaliseNormals();
            return buf;
        }

        // ================================================================== the deck

        static void BuildDeck(BridgeMeshBuffer buf, BridgePath path, BridgeSettings settings)
        {
            int n = path.Samples.Count;
            float sign = settings.flipWinding ? -1f : 1f;
            float tile = Mathf.Max(0.01f, settings.uvMetresPerTile);

            var profile = new BridgeProfile();
            profile.Build(path.Samples[0].HalfWidth, path.Samples[0].WallScale,
                          ParapetRelief(path.Samples[0].Distance, settings), settings);
            int pointCount = profile.PointCount;

            var previous = new int[pointCount];
            var current = new int[pointCount];
            bool havePrevious = false;

            for (int r = 0; r < n; r++)
            {
                BridgeSample s = path.Samples[r];

                profile.Build(s.HalfWidth, s.WallScale, ParapetRelief(s.Distance, settings), settings);
                EmitRow(buf, profile, s, s.Distance, tile, current);

                if (havePrevious) Stitch(buf, profile, s, previous, current, sign);

                int[] swap = previous;
                previous = current;
                current = swap;
                havePrevious = true;

                MeasureWidth(buf, profile, previous);
            }
        }

        /// <summary>
        /// How much taller this stretch of parapet is than the height it was set to, in metres.
        ///
        /// Quantised into blocks rather than varied smoothly: a smoothly waving wall top reads as a
        /// melted one, while a wall that holds a height for six metres and then steps reads as
        /// masonry. Only ever positive, so the containment the parapet exists for is never reduced
        /// below the height that was asked for.
        /// </summary>
        static float ParapetRelief(float distance, BridgeSettings settings)
        {
            if (settings.parapetRelief <= 0.001f) return 0f;

            int block = Mathf.FloorToInt(distance / Mathf.Max(0.5f, settings.parapetBlockLength));
            return BridgeNoise.Hash(block, 17, settings.seed) * settings.parapetRelief;
        }

        static void EmitRow(BridgeMeshBuffer buf, BridgeProfile profile, BridgeSample s,
                            float distance, float tile, int[] row)
        {
            float v = distance / tile;

            for (int run = 0; run < profile.RunCount; run++)
            {
                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run];

                for (int i = start; i < end; i++)
                {
                    Vector2 p = profile.Points[i];
                    Vector3 local = s.Position + s.Right * p.x + s.Up * p.y;
                    row[i] = buf.AddVertex(local, new Vector2(profile.U[i], v));
                }
            }
        }

        static void Stitch(BridgeMeshBuffer buf, BridgeProfile profile, BridgeSample s,
                           int[] previous, int[] current, float sign)
        {
            for (int run = 0; run < profile.RunCount; run++)
            {
                BridgeSlot slot = profile.RunSlot[run];
                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run] - 1;

                for (int i = start; i < end; i++)
                {
                    Vector2 out2 = profile.OutwardAt(i);
                    Vector3 facing = (s.Right * out2.x + s.Up * out2.y) * sign;

                    buf.AddQuadFacing(previous[i], previous[i + 1], current[i], current[i + 1],
                                      facing, slot);
                }
            }
        }

        /// <summary>
        /// Records the width of the driving surface as built, taken between the two outermost
        /// vertices of the deck run. Measured off the emitted geometry on purpose: the settings say
        /// what was asked for, and this says what arrived.
        /// </summary>
        static void MeasureWidth(BridgeMeshBuffer buf, BridgeProfile profile, int[] row)
        {
            for (int run = 0; run < profile.RunCount; run++)
            {
                if (profile.RunSlot[run] != BridgeSlot.Deck) continue;

                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run] - 1;
                float width = Vector3.Distance(buf.Vertices[row[start]], buf.Vertices[row[end]]);

                buf.MinDeckWidth = Mathf.Min(buf.MinDeckWidth, width);
                buf.MaxDeckWidth = Mathf.Max(buf.MaxDeckWidth, width);
                return;
            }
        }

        // ==================================================================== end caps

        /// <summary>
        /// Closes the two ends of the deck. Without these the slab is an open shell — you can see up
        /// inside it, and a mesh collider made from it has nothing to stop a kart driving in through
        /// the end.
        ///
        /// The cap is a triangulation of the section's <em>own</em> outline — the same points the
        /// sweep's first and last rows are built from. Anything else introduces T-junctions along
        /// the shared edge, and a T-junction is the classic hairline crack: watertight on paper,
        /// visible in the game at some angles and not others.
        /// </summary>
        static void BuildEndCaps(BridgeMeshBuffer buf, BridgePath path, BridgeSettings settings)
        {
            float sign = settings.flipWinding ? -1f : 1f;
            var profile = new BridgeProfile();

            BridgeSample head = path.Samples[0];
            profile.Build(head.HalfWidth, head.WallScale, ParapetRelief(head.Distance, settings), settings);
            AddCap(buf, profile, head, -head.Tangent * sign, settings);

            BridgeSample tail = path.Samples[path.Samples.Count - 1];
            profile.Build(tail.HalfWidth, tail.WallScale, ParapetRelief(tail.Distance, settings), settings);
            AddCap(buf, profile, tail, tail.Tangent * sign, settings);
        }

        static void AddCap(BridgeMeshBuffer buf, BridgeProfile profile, BridgeSample s,
                           Vector3 facing, BridgeSettings settings)
        {
            float tile = Mathf.Max(0.01f, settings.uvMetresPerTile);

            List<Vector2> outline = profile.Outline();
            List<int> tris = BridgeProfile.Triangulate(outline);

            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector2 a = outline[tris[i]];
                Vector2 b = outline[tris[i + 1]];
                Vector2 c = outline[tris[i + 2]];

                buf.AddFlatTriangle(
                    s.Position + s.Right * a.x + s.Up * a.y,
                    s.Position + s.Right * b.x + s.Up * b.y,
                    s.Position + s.Right * c.x + s.Up * c.y,
                    a / tile, b / tile, c / tile,
                    facing, BridgeSlot.Rock);
            }
        }

        // ==================================================================== the legs

        /// <summary>
        /// Stands a rock leg under the deck wherever there is a real drop beneath it.
        ///
        /// The legs are not authored and there is no list of them: the spacing decides where, and
        /// each one measures its own length from the deck above it to the ground below. That is the
        /// whole point of the component — raise <see cref="BridgeSettings.deckHeight"/> and every
        /// leg grows to meet the ground that is now further away, without anything else being
        /// touched.
        ///
        /// A leg also gets thicker as it gets taller, by <see cref="BridgeSettings.pierBatter"/> per
        /// metre of its own height. That is what stops a tall span reading as a deck on stilts: real
        /// stone carrying a real load spreads towards its foot, and a 30 m leg that is the same
        /// width as a 4 m one looks wrong in a way that is hard to name but easy to see.
        /// </summary>
        static void BuildPiers(BridgeMeshBuffer buf, BridgePath path, BridgeSettings settings,
                               GroundProbe probe)
        {
            if (path.Length < 1f) return;

            int count = Mathf.Max(1, Mathf.FloorToInt(path.Length / Mathf.Max(4f, settings.pierSpacing)));
            for (int i = 0; i < count; i++)
            {
                // Evenly spread with no leg sitting on either end — the landing fill owns the ends.
                BridgeSample s = path.SampleAt(path.Length * (i + 1) / (count + 1));
                if (!s.HasGround) continue;

                BuildPier(buf, s, settings, probe, i);
            }
        }

        static void BuildPier(BridgeMeshBuffer buf, BridgeSample s, BridgeSettings settings,
                              GroundProbe probe, int index)
        {
            int sides = Mathf.Clamp(settings.pierSides, 3, 24);
            float thickness = Mathf.Max(0.05f, settings.deckThickness);
            float embed = Mathf.Clamp(settings.pierTopEmbed, 0.05f, thickness * 0.9f);

            // Horizontal frame. The leg stands up world-vertical however the deck above it is
            // banked or climbing, so its own axes are the deck's flattened onto the horizontal.
            Vector3 right = Flatten(s.Right);
            Vector3 forward = Flatten(s.Tangent);
            if (right == Vector3.zero || forward == Vector3.zero) return;

            Vector3 centre = s.Position;
            float topHalfW = Mathf.Max(0.3f, s.HalfWidth * Mathf.Clamp01(settings.pierWidthRatio));
            float topHalfT = Mathf.Max(0.3f, settings.pierThickness * 0.5f);
            float outerHalf = settings.OuterHalfWidth(s.HalfWidth);

            // How far below the deck's *top* surface the cap sits. Every top vertex is cut to the
            // deck's own plane less this, which is what keeps the leg inside the slab across its
            // whole width on a banked section.
            float capDrop = thickness - embed;

            float topCentreY = centre.y - capDrop;
            float floorCentre = probe.FloorAt(centre, s.GroundFloor);
            float height = topCentreY - floorCentre;
            if (height < Mathf.Max(0.2f, settings.minPierHeight)) return;

            int bands = Mathf.Max(1, Mathf.CeilToInt(height / Mathf.Max(0.5f, settings.pierBandHeight)));
            int rings = bands + 1;

            var ring = new Vector3[rings][];
            var ringUv = new float[rings][];

            for (int r = 0; r < rings; r++)
            {
                float f = (float)r / bands;
                float depth = height * f;

                // Thicker with height, then flared again where it meets the deck.
                float spread = Mathf.Min(settings.pierMaxSpread, 1f + settings.pierBatter * depth);
                if (settings.haunchHeight > 0.01f && depth < settings.haunchHeight)
                {
                    spread *= 1f + settings.haunchSpread * (1f - depth / settings.haunchHeight);
                }

                ring[r] = new Vector3[sides];
                ringUv[r] = new float[sides];
                float perimeter = 0f;

                for (int i = 0; i < sides; i++)
                {
                    float angle = Mathf.PI * 2f * i / sides;
                    float rough = 1f + settings.pierRoughness
                                * BridgeNoise.Ring(i, r * 0.9f + index * 37f, sides, settings.seed);

                    float ox = Mathf.Cos(angle) * topHalfW * spread * rough;
                    float oz = Mathf.Sin(angle) * topHalfT * spread * rough;

                    // Only the very top ring sits inside the slab, and it is the only one that must
                    // not reach past the deck's own edge — a wide leg with a big haunch would
                    // otherwise push its cap out through the parapet and hang there in mid-air.
                    // Rings below are free to flare wider; that is a corbel, not a fault.
                    if (r == 0) ox = Mathf.Clamp(ox, -outerHalf, outerHalf);

                    Vector3 offset = right * ox + forward * oz;

                    // Top of this column of the leg, cut to the deck's plane; bottom, cut to the
                    // ground under this very corner.
                    Vector3 at = centre + offset;
                    float topY = DeckHeightAt(s, offset) - capDrop;
                    float footY = probe.FloorAt(new Vector3(at.x, floorCentre, at.z), s.GroundFloor)
                                - Mathf.Max(0.1f, settings.footingDepth);

                    ring[r][i] = new Vector3(at.x, Mathf.Lerp(topY, footY, f), at.z);

                    if (i > 0) perimeter += Vector3.Distance(ring[r][i], ring[r][i - 1]);
                    ringUv[r][i] = perimeter;
                }
            }

            EmitPierShell(buf, ring, ringUv, centre, settings, sides, rings);

            buf.PierCount++;
            buf.TallestPier = Mathf.Max(buf.TallestPier, height);
        }

        static void EmitPierShell(BridgeMeshBuffer buf, Vector3[][] ring, float[][] ringUv,
                                  Vector3 centre, BridgeSettings settings, int sides, int rings)
        {
            float tile = Mathf.Max(0.01f, settings.uvMetresPerTile);

            for (int r = 0; r < rings - 1; r++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;

                    Vector3 a = ring[r][i], b = ring[r][j];
                    Vector3 c = ring[r + 1][j], d = ring[r + 1][i];

                    // Outward is measured from the leg's own axis at this height, not from its top,
                    // so a heavily battered foot still faces its faces outwards.
                    Vector3 mid = (a + b + c + d) * 0.25f;
                    Vector3 axis = new Vector3(centre.x, mid.y, centre.z);
                    Vector3 outward = mid - axis;
                    if (outward.sqrMagnitude < 1e-8f) outward = Vector3.up;

                    float u0 = ringUv[r][i] / tile, u1 = ringUv[r][j] / tile;
                    float v0 = -a.y / tile, v1 = -d.y / tile;

                    buf.AddFlatQuad(a, b, c, d,
                                    new Vector2(u0, v0), new Vector2(u1, v0),
                                    new Vector2(u1, v1), new Vector2(u0, v1),
                                    outward, BridgeSlot.Rock);
                }
            }

            AddRingCap(buf, ring[0], centre, true, tile);
            AddRingCap(buf, ring[rings - 1], centre, false, tile);
        }

        /// <summary>
        /// Closes one end of a leg with a fan. Both ends are buried — the top inside the slab, the
        /// foot inside the ground — but a mesh collider built from an open tube is a tube you can
        /// drive into, so they are closed anyway.
        /// </summary>
        static void AddRingCap(BridgeMeshBuffer buf, Vector3[] ring, Vector3 centre, bool up, float tile)
        {
            float y = 0f;
            for (int i = 0; i < ring.Length; i++) y += ring[i].y;
            var hub = new Vector3(centre.x, y / ring.Length, centre.z);

            Vector3 facing = up ? Vector3.up : Vector3.down;

            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 a = ring[i];
                Vector3 b = ring[(i + 1) % ring.Length];

                buf.AddFlatTriangle(hub, a, b,
                                    new Vector2(hub.x / tile, hub.z / tile),
                                    new Vector2(a.x / tile, a.z / tile),
                                    new Vector2(b.x / tile, b.z / tile),
                                    facing, BridgeSlot.Rock);
            }
        }

        // ================================================================== the landings

        /// <summary>
        /// Fills the wedge between the underside of the deck and the ground at each end, so the
        /// bridge rises out of the bank instead of ending as a slab hanging over it.
        ///
        /// It walks in from each end and stops as soon as the drop underneath grows past
        /// <see cref="BridgeSettings.abutmentDepth"/> — from there the gap belongs to a leg. So
        /// there is no boundary to keep in step between the two: raise the deck and the fill
        /// naturally shortens as the legs take over, and lower it and the fill runs further in as
        /// the legs stop being built at all.
        /// </summary>
        static void BuildLandingFill(BridgeMeshBuffer buf, BridgePath path, BridgeSettings settings,
                                     GroundProbe probe)
        {
            int n = path.Samples.Count;

            int headEnd = 0;
            while (headEnd < n - 1 && WithinFill(path.Samples[headEnd], settings, probe)) headEnd++;

            int tailStart = n - 1;
            while (tailStart > 0 && WithinFill(path.Samples[tailStart], settings, probe)) tailStart--;

            if (headEnd > 0) EmitFill(buf, path, settings, probe, 0, headEnd);

            // On a bridge lying close to the ground the whole length qualifies, and the two walks
            // pass each other — without this the fill is built twice, one copy inside the other,
            // which doubles the triangles and puts coincident faces in plain sight underneath.
            int tailFrom = Mathf.Max(headEnd, tailStart);
            if (tailFrom < n - 1) EmitFill(buf, path, settings, probe, tailFrom, n - 1);
        }

        static bool WithinFill(BridgeSample s, BridgeSettings settings, GroundProbe probe)
        {
            if (!s.HasGround) return false;
            float floor = probe.FloorAt(s.Position, s.GroundFloor);
            return s.Position.y - settings.deckThickness - floor <= settings.abutmentDepth;
        }

        static void EmitFill(BridgeMeshBuffer buf, BridgePath path, BridgeSettings settings,
                             GroundProbe probe, int from, int to)
        {
            float tile = Mathf.Max(0.01f, settings.uvMetresPerTile);
            int span = to - from;
            if (span < 1) return;

            buf.FillLength += path.Samples[to].Distance - path.Samples[from].Distance;

            var leftTop = new Vector3[span + 1];
            var rightTop = new Vector3[span + 1];
            var leftFoot = new Vector3[span + 1];
            var rightFoot = new Vector3[span + 1];

            for (int i = 0; i <= span; i++)
            {
                BridgeSample s = path.Samples[from + i];
                float outer = settings.OuterHalfWidth(s.HalfWidth);

                // The top edge is the bottom of the fascia exactly, so the fill meets the deck along
                // a shared line rather than overlapping it — an overlap here is what would z-fight
                // in full view at the mouth of the bridge.
                Vector3 lt = s.Position - s.Right * outer - s.Up * settings.deckThickness;
                Vector3 rt = s.Position + s.Right * outer - s.Up * settings.deckThickness;

                float floorL = probe.FloorAt(lt, s.GroundFloor);
                float floorR = probe.FloorAt(rt, s.GroundFloor);
                float sink = Mathf.Max(0.1f, settings.footingDepth);

                // Splayed outwards in proportion to how far it has to drop — a buttress rather than
                // a slab on legs. Roughened per station so the two flanks are not mirror-smooth.
                float flareL = Mathf.Max(0f, lt.y - floorL) * settings.abutmentFlare;
                float flareR = Mathf.Max(0f, rt.y - floorR) * settings.abutmentFlare;
                flareL *= 1f + 0.25f * BridgeNoise.Fbm(s.Distance * 0.12f, 3.1f, settings.seed);
                flareR *= 1f + 0.25f * BridgeNoise.Fbm(s.Distance * 0.12f, 9.7f, settings.seed + 51);

                Vector3 flat = Flatten(s.Right);
                if (flat == Vector3.zero) flat = Vector3.right;

                leftTop[i] = lt;
                rightTop[i] = rt;
                leftFoot[i] = new Vector3(lt.x, floorL - sink, lt.z) - flat * flareL;
                rightFoot[i] = new Vector3(rt.x, floorR - sink, rt.z) + flat * flareR;
            }

            for (int i = 0; i < span; i++)
            {
                BridgeSample s = path.Samples[from + i];
                Vector3 outLeft = -Flatten(s.Right);
                Vector3 outRight = Flatten(s.Right);
                if (outLeft == Vector3.zero) { outLeft = Vector3.left; outRight = Vector3.right; }

                float v0 = s.Distance / tile;
                float v1 = path.Samples[from + i + 1].Distance / tile;

                buf.AddFlatQuad(leftTop[i], leftTop[i + 1], leftFoot[i + 1], leftFoot[i],
                                new Vector2(v0, 0f), new Vector2(v1, 0f),
                                new Vector2(v1, 1f), new Vector2(v0, 1f), outLeft, BridgeSlot.Rock);

                buf.AddFlatQuad(rightTop[i], rightTop[i + 1], rightFoot[i + 1], rightFoot[i],
                                new Vector2(v0, 0f), new Vector2(v1, 0f),
                                new Vector2(v1, 1f), new Vector2(v0, 1f), outRight, BridgeSlot.Rock);

                // The floor of the fill. Buried, but it closes the solid so the collider has no way
                // in from underneath.
                buf.AddFlatQuad(leftFoot[i], rightFoot[i], rightFoot[i + 1], leftFoot[i + 1],
                                new Vector2(v0, 0f), new Vector2(v0, 1f),
                                new Vector2(v1, 1f), new Vector2(v1, 0f), Vector3.down, BridgeSlot.Rock);
            }

            // Both ends get a face rather than being left open: one is buried in the bank, the other
            // is where the legs take over and is in plain sight. The tangent runs from `from`
            // towards `to`, so the two caps face out of the fill the same way at either end of the
            // bridge — there is no head-versus-tail case here, and treating it as one gets the
            // inboard face of the tail fill pointing into the solid.
            AddFillCap(buf, leftTop[0], rightTop[0], rightFoot[0], leftFoot[0],
                       -path.Samples[from].Tangent);
            AddFillCap(buf, leftTop[span], rightTop[span], rightFoot[span], leftFoot[span],
                       path.Samples[to].Tangent);
        }

        static void AddFillCap(BridgeMeshBuffer buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                               Vector3 facing)
        {
            buf.AddFlatQuad(a, b, c, d, Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                            facing, BridgeSlot.Rock);
        }

        // ==================================================================== helpers

        /// <summary>
        /// The deck's top surface at a horizontal offset from a cross-section's centre.
        ///
        /// Reading the height off the frame rather than assuming it is flat is what lets a leg's cap
        /// stay inside the slab through a banked corner. The deck is a plane through
        /// <c>s.Position</c> with normal <c>s.Up</c>, so the height at an offset is a straight
        /// projection onto it — and it stays exact for the tilt from a climb as well as from bank.
        /// </summary>
        static float DeckHeightAt(BridgeSample s, Vector3 offset)
        {
            // Vertical distance from the plane down to the offset point: solve for y such that
            // (offset + y*up_component) lies on the plane through the origin with normal Up.
            float denom = s.Up.y;
            if (Mathf.Abs(denom) < 1e-4f) return s.Position.y;

            return s.Position.y - Vector3.Dot(new Vector3(offset.x, 0f, offset.z), s.Up) / denom;
        }

        /// <summary>Horizontal part of a direction, renormalised. Zero when it points straight up.</summary>
        static Vector3 Flatten(Vector3 v)
        {
            var flat = new Vector3(v.x, 0f, v.z);
            float len = flat.magnitude;
            return len > 1e-5f ? flat / len : Vector3.zero;
        }
    }
}
