using UnityEngine;

namespace LavaFlow
{
    /// <summary>
    /// Builds the lava flow geometry from a solved <see cref="FlowPath"/>. Pure maths in, triangles
    /// out: no scene objects, no asset loading, no global state, so it can be called from the
    /// editor, at runtime, or from a test harness.
    ///
    /// The cross-section, from the middle outwards:
    ///   * a molten channel, its surface sitting a little below the top of its banks
    ///   * a crust of plates rafting on that channel, each plate pulled back from its neighbours so
    ///     the lava underneath reads as a network of glowing cracks, and each hanging a wall down
    ///     past the lava so the crust shows thickness
    ///   * arcs of buckled crust bowing downstream, where the middle of the channel has outrun the
    ///     edges and the skin has had to concertina
    ///   * a levee of cooled rock on each side, built out of what the flow froze at its own margin
    ///   * a skirt running down into the ground, so nothing shows daylight underneath
    ///
    /// Everything above responds to the slope of the ground under it. Steep ground gives a narrow,
    /// fast, barely-crusted cascade with low banks; flat ground gives a broad, crusted, meandering
    /// river with tall ones. One route therefore produces both, with the change happening exactly
    /// where the terrain flattens out rather than anywhere a person had to mark.
    /// </summary>
    public static class LavaFlowMeshBuilder
    {
        public const int SubmeshCount = 4;

        public static MeshBuffer Build(LavaFlowSettings settings, FlowPath path)
        {
            LavaFlowSettings s = settings ?? new LavaFlowSettings();
            var buf = new MeshBuffer(SubmeshCount, s.uvScale);
            if (path == null || !path.IsValid) return buf;

            var rng = new Rng(s.seed);
            var surface = new Surface(s, path);

            BuildMolten(buf, s, path, surface);
            BuildCrust(buf, s, path, surface);
            BuildLevees(buf, s, path, surface);
            BuildSkirt(buf, s, path, surface);
            BuildCaps(buf, s, path, surface);
            BuildSlabs(buf, s, path, surface, ref rng);
            BuildBubbles(buf, s, path, surface, ref rng);
            BuildRocks(buf, s, path, surface, ref rng);

            // Done last: both alternatives need the finished footprint, and the props are part of it.
            if (s.uvMode == FlowUVMode.Normalized) buf.NormalizeUVs();
            else if (s.uvMode == FlowUVMode.WorldPlanar) buf.WorldPlanarUVs();

            return buf;
        }

        // ================================================================== surface

        /// <summary>
        /// The heights everything else is measured from, worked out once per (station, lateral)
        /// sample: where the lava surface sits, where the bank crest sits, and which lateral samples
        /// count as channel rather than bank.
        ///
        /// Heights are along the ground normal at that sample, not along world up. On a cliff face
        /// those differ by nearly ninety degrees, and measuring along world up would slide the whole
        /// cross-section downhill and open the upslope bank.
        /// </summary>
        sealed class Surface
        {
            public readonly LavaFlowSettings S;
            public readonly FlowPath Path;

            /// <summary>Lateral index of the centreline.</summary>
            public readonly int Center;

            /// <summary>How many lateral samples either side of the centreline are channel.</summary>
            public readonly int ChannelHalf;

            /// <summary>Height of the lava surface above the ground, per [station, lateral].</summary>
            public readonly float[,] MoltenHeight;

            /// <summary>Height of the flow's own surface above the ground, per [station, lateral]:
            /// the lava in the channel, the bank crest outside it.</summary>
            public readonly float[,] SurfaceHeight;

            /// <summary>Metres from the centreline, signed, per [station, lateral].</summary>
            public readonly float[,] Across;

            /// <summary>
            /// The downstream UV coordinate, per station. Not metres travelled — metres divided by
            /// how fast the lava is moving there, so it measures the *time* the lava has been in
            /// transit rather than the distance.
            ///
            /// That is what lets one scroll rate serve the whole flow. The material used to be handed
            /// a per-vertex speed and told to multiply time by it, which cannot work: two neighbouring
            /// points scrolling at different rates pull the pattern between them apart, and the tear
            /// grows for as long as the game is running. Measured on a spillway river seven minutes
            /// in, a speed changing by 0.09 per metre had sheared the UVs 130x faster than the surface
            /// itself, so one pixel covered forty tiles of texture and the flow smeared into a hard
            /// band wherever the slope changed.
            ///
            /// Dividing here instead puts the same speed difference into the *spacing* of the UVs,
            /// where it is a fixed stretch that never grows. Scrolled at one rate, lava on a cascade
            /// still crosses the ground faster than lava on the flat, by exactly the ratio it did
            /// before — it is only the tearing that is gone.
            /// </summary>
            public readonly float[] FlowV;

            public readonly PlateField Plates;

            public Surface(LavaFlowSettings s, FlowPath path)
            {
                S = s;
                Path = path;

                int n = path.Count;
                int lateral = path.Lateral;
                Center = (lateral - 1) / 2;

                // Snap the channel/bank boundary onto a grid line so plates and banks never have to
                // share a quad.
                int half = Mathf.RoundToInt((1f - Mathf.Clamp01(s.leveeFraction)) * Center);
                ChannelHalf = Mathf.Clamp(half, 1, Center - 1);

                MoltenHeight = new float[n, lateral];
                SurfaceHeight = new float[n, lateral];
                Across = new float[n, lateral];
                FlowV = new float[n];

                Plates = new PlateField(s);

                for (int i = 0; i < n; i++)
                {
                    FlowStation st = path.Stations[i];

                    // Each step contributes the time it takes to cross, not the distance.
                    if (i > 0)
                    {
                        float run = st.Distance - path.Stations[i - 1].Distance;
                        FlowV[i] = FlowV[i - 1] + run / Mathf.Max(0.05f, Speed(i));
                    }

                    // A flow moving fast down a steep face has no time to freeze a bank at its
                    // margin, so the levees fade out exactly where the cascades are.
                    float leveeScale = Mathf.Lerp(1f, 0.3f, st.SlopeNorm);
                    float crest = s.leveeHeight * leveeScale;
                    float fill = Mathf.Max(0.05f, crest - s.channelDepth * leveeScale);

                    for (int j = 0; j < lateral; j++)
                    {
                        float lat = Lat(j, lateral);
                        Across[i, j] = lat * st.HalfWidth;

                        float molten = MoltenAt(s, st, fill, lat);
                        MoltenHeight[i, j] = molten;

                        int fromCenter = Mathf.Abs(j - Center);
                        SurfaceHeight[i, j] = fromCenter <= ChannelHalf
                            ? molten
                            : BankAt(s, st, crest, fill, fromCenter, j);
                    }
                }
            }

            public int Stations { get { return Path.Count; } }
            public int Lateral { get { return Path.Lateral; } }

            /// <summary>Lateral parameter of sample j, running -1 at the left bank to +1 at the right.</summary>
            public static float Lat(int j, int lateral)
            {
                return -1f + 2f * j / (lateral - 1);
            }

            /// <summary>A point at <paramref name="height"/> above the ground under sample (i, j).</summary>
            public Vector3 P(int i, int j, float height)
            {
                return Path.Ground[i, j] + Path.Normal[i, j] * height;
            }

            /// <summary>The lava surface at sample (i, j).</summary>
            public Vector3 Molten(int i, int j)
            {
                return P(i, j, MoltenHeight[i, j]);
            }

            /// <summary>How much of the surface has skinned over here, 0 to 1.</summary>
            public float Coverage(int i)
            {
                FlowStation st = Path.Stations[i];
                float coverage = Mathf.Lerp(S.crustCoverageRiver, S.crustCoverageCascade, st.SlopeNorm);

                // Lava comes out of the ground molten and takes a while to skin over. A flow that
                // is continuing another one has not just come out of the ground, though, and the
                // bright patch would sit at the join like a join.
                if (Path.ContinuesUpstream) return coverage;

                return coverage * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((st.Distance - 3f) / 14f));
            }

            /// <summary>
            /// How much faster than the river the lava is moving here: 1 on the flat, up to
            /// <c>cascadeSpeedBoost</c> on a cascade. It is baked into the spacing of <see cref="FlowV"/>
            /// rather than handed to the material as a rate to multiply time by — see the note there.
            /// </summary>
            public float Speed(int i)
            {
                return Mathf.Lerp(1f, Mathf.Max(1f, S.cascadeSpeedBoost), Path.Stations[i].SlopeNorm);
            }

            /// <summary>Metres per UV unit. Never zero, so it is always safe to divide by.</summary>
            public float UVScale { get { return S.uvScale <= 0f ? 1f : S.uvScale; } }

            /// <summary>UV for a ribbon sample: metres across, and downstream travel time, over uvScale.</summary>
            public Vector2 UV(int i, int j)
            {
                return new Vector2(Across[i, j] / UVScale, FlowV[i] / UVScale);
            }

            /// <summary>UV1 for a ribbon sample: the local speed, and 0 at the bank to 1 mid-channel.</summary>
            public Vector2 Flow(int i, int j)
            {
                float bank = 1f - Mathf.Clamp01(Mathf.Abs(j - Center) / (float)Mathf.Max(1, ChannelHalf));
                return new Vector2(Speed(i), bank);
            }

            /// <summary>Height of the lava surface, bulging slightly in the middle and rolling as it goes.</summary>
            static float MoltenAt(LavaFlowSettings s, FlowStation st, float fill, float lat)
            {
                float inner = Mathf.Max(0.05f, 1f - s.leveeFraction);
                float t = Mathf.Clamp01(Mathf.Abs(lat) / inner);

                // The middle of a channel outruns its edges and stands a little proud of them.
                float bulge = s.channelDepth * 0.18f * (1f - t * t);

                float roll = FlowNoise.Fbm(st.Distance * 0.22f, lat * 2.3f + 13.7f, s.seed + 211)
                             * s.moltenTurbulence * 0.16f;

                return Mathf.Max(0.04f, fill + bulge + roll);
            }

            /// <summary>
            /// Height of the bank. It lifts off the lip of the lava, crests a short way out, then
            /// runs back down the outside and buries itself in the ground.
            /// </summary>
            float BankAt(LavaFlowSettings s, FlowStation st, float crest, float fill, int fromCenter, int j)
            {
                // 0 at the lava's edge, 1 at the outer rim of the ribbon.
                float span = Mathf.Max(1, Center - ChannelHalf);
                float u = Mathf.Clamp01((fromCenter - ChannelHalf) / span);

                float rough = 1f + FlowNoise.Fbm(st.Distance * 0.35f, j * 1.7f, s.seed + 307)
                                   * s.leveeRoughness * 0.55f;
                float top = crest * rough;

                // The inner lip stands proud of the lava but below the crest, which is what stops
                // the channel reading as a trench cut into a flat slab.
                float lip = fill + (crest - fill) * 0.5f;

                if (u <= 0.4f)
                    return Mathf.Lerp(lip, top, Mathf.SmoothStep(0f, 1f, u / 0.4f));
                if (u <= 0.8f)
                    return Mathf.Lerp(top, crest * 0.15f, Mathf.SmoothStep(0f, 1f, (u - 0.4f) / 0.4f));

                // The last ring dips under the ground so the edge is buried rather than butted up
                // against it.
                return Mathf.Lerp(crest * 0.15f, -s.skirtDepth * 0.35f, (u - 0.8f) / 0.2f);
            }
        }

        // ================================================================== orientation

        /// <summary>
        /// Adds a quad wound so that it faces along <paramref name="outward"/>.
        ///
        /// Every surface here is built from a grid whose axes flip meaning depending on which side
        /// of the channel, which end of the flow and which face of a boulder it belongs to. Rather
        /// than tracking that by hand and finding out at runtime that half the flow is inside out,
        /// each quad is handed the direction it is meant to face and picks its own winding.
        /// </summary>
        static void AddOrientedQuadUV(MeshBuffer buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                      Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD,
                                      Vector2 flowA, Vector2 flowB, Vector2 flowC, Vector2 flowD,
                                      Vector3 outward, LavaSlot slot, float shade)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), outward) < 0f)
            {
                // Swapping the two off-diagonal corners reverses the winding and keeps the shape.
                buf.AddQuadUV(a, c, b, d, uvA, uvC, uvB, uvD, flowA, flowC, flowB, flowD, slot, shade);
                return;
            }

            buf.AddQuadUV(a, b, c, d, uvA, uvB, uvC, uvD, flowA, flowB, flowC, flowD, slot, shade);
        }

        /// <summary>As above, for quads that take a planar-projected UV.</summary>
        static void AddOrientedQuad(MeshBuffer buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                    Vector3 outward, LavaSlot slot, float shade)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), outward) < 0f)
                buf.AddQuad(a, c, b, d, slot, shade);
            else
                buf.AddQuad(a, b, c, d, slot, shade);
        }

        // ================================================================== molten

        /// <summary>
        /// The sheet of lava running down the channel. It is emitted whole, under everything else:
        /// the crust above only covers part of it, and what is left showing between the plates is
        /// the glow.
        /// </summary>
        static void BuildMolten(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf)
        {
            int c = surf.Center;
            int lo = c - surf.ChannelHalf;
            int hi = c + surf.ChannelHalf;

            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int j = lo; j < hi; j++)
                {
                    Vector3 a = surf.Molten(i, j);
                    Vector3 b = surf.Molten(i, j + 1);
                    Vector3 cc = surf.Molten(i + 1, j);
                    Vector3 d = surf.Molten(i + 1, j + 1);

                    float shade = 1f + FlowNoise.Signed(i * 0.6f, j * 0.9f, s.seed + 55) * 0.08f;

                    AddOrientedQuadUV(buf, a, b, cc, d,
                                      surf.UV(i, j), surf.UV(i, j + 1), surf.UV(i + 1, j), surf.UV(i + 1, j + 1),
                                      surf.Flow(i, j), surf.Flow(i, j + 1), surf.Flow(i + 1, j), surf.Flow(i + 1, j + 1),
                                      path.Normal[i, j], LavaSlot.Molten, shade);
                }
            }
        }

        // ================================================================== crust

        /// <summary>
        /// The rafts of cooled crust floating on the channel.
        ///
        /// The crack network is the whole trick, and it is the same one the pond uses: rather than
        /// modelling the gaps, every plate keeps the corners it owns outright and pulls back the
        /// ones it shares with a neighbour, so the skin opens up exactly along the plate boundaries
        /// and nowhere else. Each plate then hangs a wall down past the lava, so a crack is a slot
        /// with depth rather than a stripe painted on a flat sheet.
        /// </summary>
        static void BuildCrust(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf)
        {
            int n = path.Count;
            int c = surf.Center;
            int lo = c - surf.ChannelHalf;
            int hi = c + surf.ChannelHalf;
            int quadsAcross = hi - lo;
            if (quadsAcross < 1 || n < 2) return;

            // Plate id per quad, and whether that quad's plate survived at all. -1 means open lava.
            var plateOf = new int[n - 1, quadsAcross];
            var lift = new float[n - 1, quadsAcross];
            var warm = new bool[n - 1, quadsAcross];

            for (int i = 0; i < n - 1; i++)
            {
                float coverage = 0.5f * (surf.Coverage(i) + surf.Coverage(i + 1));
                float slope = 0.5f * (path.Stations[i].SlopeNorm + path.Stations[i + 1].SlopeNorm);

                for (int q = 0; q < quadsAcross; q++)
                {
                    int j = lo + q;
                    float along = 0.5f * (path.Stations[i].Distance + path.Stations[i + 1].Distance);
                    float across = 0.25f * (surf.Across[i, j] + surf.Across[i, j + 1] +
                                            surf.Across[i + 1, j] + surf.Across[i + 1, j + 1]);

                    int id = surf.Plates.Id(along, across);
                    float roll = FlowNoise.Hash1(id, s.seed + 3);

                    if (roll > coverage)
                    {
                        plateOf[i, q] = -1; // this plate never formed: open lava
                        continue;
                    }

                    plateOf[i, q] = id;

                    // Plates sit at slightly different heights, and the skin buckles into arcs that
                    // bow downstream where the middle of the channel has outrun the edges.
                    //
                    // The sign matters more than it looks, and it used to be the other way round. A
                    // crest is a line of constant phase: subtracting the across term solves to
                    // `along = k + |across|`, which puts the crest further downstream at the banks
                    // than in the middle, so the apex points upstream. Chevrons are the cue the eye
                    // uses to read which way a surface is travelling, so a channel full of arcs
                    // aimed back up it looks like it is flowing backwards however the material
                    // scrolls. Adding it solves to `along = k - |across|` and points the apex
                    // downstream, the way lava that is fastest in the middle actually buckles.
                    float step = (FlowNoise.Hash1(id, s.seed + 29) - 0.5f) * 2f * s.plateHeightVariation;
                    float phase = (along + Mathf.Abs(across) * s.ridgeCurvature) / Mathf.Max(1f, s.ridgeSpacing);
                    float arc = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f));
                    float ridge = s.ridgeHeight * arc * arc * (1f - slope);

                    lift[i, q] = s.crustThickness + step + ridge;

                    // Fast lava has not had time to cool black, so more of its skin is still glowing.
                    float warmChance = Mathf.Clamp01(s.warmCrustRatio + slope * 0.3f);
                    warm[i, q] = FlowNoise.Hash1(id, s.seed + 71) < warmChance;
                }
            }

            // A corner shared by two plates is one that has to pull back; a corner in the middle of
            // a plate stays put, which is what keeps each raft welded to itself.
            var shared = new bool[n, quadsAcross + 1];
            for (int i = 0; i < n; i++)
            {
                for (int q = 0; q <= quadsAcross; q++)
                    shared[i, q] = IsShared(plateOf, n - 1, quadsAcross, i, q);
            }

            float pull = s.crackWidth * 0.5f;

            for (int i = 0; i < n - 1; i++)
            {
                for (int q = 0; q < quadsAcross; q++)
                {
                    int id = plateOf[i, q];
                    if (id < 0) continue;

                    int j = lo + q;
                    float h = lift[i, q];

                    Vector3 a = surf.P(i, j, surf.MoltenHeight[i, j] + h);
                    Vector3 b = surf.P(i, j + 1, surf.MoltenHeight[i, j + 1] + h);
                    Vector3 cc = surf.P(i + 1, j, surf.MoltenHeight[i + 1, j] + h);
                    Vector3 d = surf.P(i + 1, j + 1, surf.MoltenHeight[i + 1, j + 1] + h);

                    Vector3 mid = (a + b + cc + d) * 0.25f;
                    if (shared[i, q]) a = PullIn(a, mid, pull);
                    if (shared[i, q + 1]) b = PullIn(b, mid, pull);
                    if (shared[i + 1, q]) cc = PullIn(cc, mid, pull);
                    if (shared[i + 1, q + 1]) d = PullIn(d, mid, pull);

                    LavaSlot slot = warm[i, q] ? LavaSlot.CrustWarm : LavaSlot.CrustDark;
                    float shade = 0.85f + FlowNoise.Hash1(id, s.seed + 97) * 0.35f;

                    AddOrientedQuadUV(buf, a, b, cc, d,
                                      surf.UV(i, j), surf.UV(i, j + 1), surf.UV(i + 1, j), surf.UV(i + 1, j + 1),
                                      surf.Flow(i, j), surf.Flow(i, j + 1), surf.Flow(i + 1, j), surf.Flow(i + 1, j + 1),
                                      path.Normal[i, j], slot, shade);

                    // Walls, wherever this quad's edge is also the plate's edge. Dropped down past
                    // the lava so the crust has visible thickness when you look into a crack.
                    float wallBase = -s.crustThickness * 0.6f;
                    Vector3 aB = surf.P(i, j, surf.MoltenHeight[i, j] + wallBase);
                    Vector3 bB = surf.P(i, j + 1, surf.MoltenHeight[i, j + 1] + wallBase);
                    Vector3 cB = surf.P(i + 1, j, surf.MoltenHeight[i + 1, j] + wallBase);
                    Vector3 dB = surf.P(i + 1, j + 1, surf.MoltenHeight[i + 1, j + 1] + wallBase);

                    if (Differs(plateOf, n - 1, quadsAcross, i - 1, q, id)) AddWall(buf, surf, i, j, mid, a, b, aB, bB);
                    if (Differs(plateOf, n - 1, quadsAcross, i + 1, q, id)) AddWall(buf, surf, i, j, mid, cc, d, cB, dB);
                    if (Differs(plateOf, n - 1, quadsAcross, i, q - 1, id)) AddWall(buf, surf, i, j, mid, a, cc, aB, cB);
                    if (Differs(plateOf, n - 1, quadsAcross, i, q + 1, id)) AddWall(buf, surf, i, j, mid, b, d, bB, dB);
                }
            }
        }

        /// <summary>True when the quad next door belongs to a different plate, or to no plate at all.</summary>
        static bool Differs(int[,] plateOf, int rows, int cols, int i, int q, int id)
        {
            if (i < 0 || i >= rows || q < 0 || q >= cols) return true;
            return plateOf[i, q] != id;
        }

        /// <summary>True when the corner at (i, q) is on a boundary between plates.</summary>
        static bool IsShared(int[,] plateOf, int rows, int cols, int i, int q)
        {
            int found = int.MinValue;
            for (int di = -1; di <= 0; di++)
            {
                for (int dq = -1; dq <= 0; dq++)
                {
                    int ii = i + di;
                    int qq = q + dq;
                    if (ii < 0 || ii >= rows || qq < 0 || qq >= cols) return true; // channel edge
                    int id = plateOf[ii, qq];
                    if (found == int.MinValue) found = id;
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

        /// <summary>
        /// The side of a plate, dropped from the given top edge down past the lava. Faces away from
        /// the plate it belongs to, whichever of its four edges this is.
        /// </summary>
        static void AddWall(MeshBuffer buf, Surface surf, int i, int j, Vector3 plateCenter,
                            Vector3 topA, Vector3 topB, Vector3 botA, Vector3 botB)
        {
            Vector3 outward = (topA + topB) * 0.5f - plateCenter;

            // Always warm: this is the face you see glowing when you look down into a crack.
            AddOrientedQuadUV(buf, topA, topB, botA, botB,
                              surf.UV(i, j), surf.UV(i, j), surf.UV(i, j), surf.UV(i, j),
                              surf.Flow(i, j), surf.Flow(i, j), surf.Flow(i, j), surf.Flow(i, j),
                              outward, LavaSlot.CrustWarm, 1.05f);
        }

        // ================================================================== levees

        /// <summary>
        /// The banks. A flow builds its own walls out of whatever freezes at its margin, and they
        /// are most of what makes it read as something that poured rather than something that was
        /// painted onto the hillside.
        /// </summary>
        static void BuildLevees(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf)
        {
            int c = surf.Center;

            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int j = 0; j < surf.Lateral - 1; j++)
                {
                    int fromCenter = Mathf.Min(Mathf.Abs(j - c), Mathf.Abs(j + 1 - c));
                    if (fromCenter < surf.ChannelHalf) continue; // channel, handled by the crust

                    Vector3 a = surf.P(i, j, surf.SurfaceHeight[i, j]);
                    Vector3 b = surf.P(i, j + 1, surf.SurfaceHeight[i, j + 1]);
                    Vector3 cc = surf.P(i + 1, j, surf.SurfaceHeight[i + 1, j]);
                    Vector3 d = surf.P(i + 1, j + 1, surf.SurfaceHeight[i + 1, j + 1]);

                    // Nearest the lava the bank is still scorched; further out it is dead rock.
                    int outer = Mathf.Max(Mathf.Abs(j - c), Mathf.Abs(j + 1 - c));
                    float across = (outer - surf.ChannelHalf) / Mathf.Max(1f, c - surf.ChannelHalf);
                    LavaSlot slot = across < 0.25f ? LavaSlot.CrustWarm
                                  : across < 0.7f ? LavaSlot.CrustDark
                                  : LavaSlot.Rock;

                    float shade = 0.85f + FlowNoise.Value(i * 0.35f, j * 0.7f, s.seed + 133) * 0.4f;

                    AddOrientedQuadUV(buf, a, b, cc, d,
                                      surf.UV(i, j), surf.UV(i, j + 1), surf.UV(i + 1, j), surf.UV(i + 1, j + 1),
                                      surf.Flow(i, j), surf.Flow(i, j + 1), surf.Flow(i + 1, j), surf.Flow(i + 1, j + 1),
                                      path.Normal[i, j], slot, shade);
                }
            }
        }

        // ================================================================== skirt and caps

        /// <summary>
        /// Drops a wall from the outer edge into the ground. Terrain is never as smooth as the
        /// ribbon draped over it, and without this the flow shows daylight under its own edge on
        /// every bump it crosses.
        /// </summary>
        static void BuildSkirt(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf)
        {
            if (s.skirtDepth <= 0.001f) return;

            int last = surf.Lateral - 1;
            for (int i = 0; i < path.Count - 1; i++)
            {
                // Outward is away from the centreline, which is the opposite side for each bank.
                AddSkirtQuad(buf, s, path, surf, i, 0, -path.Stations[i].Right);
                AddSkirtQuad(buf, s, path, surf, i, last, path.Stations[i].Right);
            }
        }

        static void AddSkirtQuad(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf,
                                 int i, int j, Vector3 outward)
        {
            Vector3 topA = surf.P(i, j, surf.SurfaceHeight[i, j]);
            Vector3 topB = surf.P(i + 1, j, surf.SurfaceHeight[i + 1, j]);
            Vector3 botA = surf.P(i, j, -s.skirtDepth);
            Vector3 botB = surf.P(i + 1, j, -s.skirtDepth);

            AddOrientedQuad(buf, topA, topB, botA, botB, outward, LavaSlot.Rock, 0.8f);
        }

        /// <summary>
        /// Caps the two ends. The head is molten, because that is where the lava is arriving from;
        /// the toe is the cooled snout the flow stalled behind.
        /// </summary>
        static void BuildCaps(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf)
        {
            int n = path.Count;
            CapEnd(buf, s, surf, 0, -path.Stations[0].Forward, LavaSlot.Molten, -1f);
            CapEnd(buf, s, surf, n - 1, path.Stations[n - 1].Forward, LavaSlot.CrustDark, 1f);
        }

        /// <summary>
        /// One end wall, from the flow's surface down past the skirt.
        ///
        /// The head of a flow is molten, so this wall has to carry flow UVs like the rest of the
        /// channel. It gets them by carrying on past the end of the ribbon: a vertex a metre down
        /// the face is a metre further along V in whichever direction the cap faces, so the pattern
        /// runs off the end of the lava and down the wall rather than stopping dead at the lip.
        /// </summary>
        static void CapEnd(MeshBuffer buf, LavaFlowSettings s, Surface surf, int i,
                           Vector3 outward, LavaSlot slot, float downstream)
        {
            float scale = surf.UVScale;

            for (int j = 0; j < surf.Lateral - 1; j++)
            {
                Vector3 topA = surf.P(i, j, surf.SurfaceHeight[i, j]);
                Vector3 topB = surf.P(i, j + 1, surf.SurfaceHeight[i, j + 1]);
                Vector3 botA = surf.P(i, j, -s.skirtDepth);
                Vector3 botB = surf.P(i, j + 1, -s.skirtDepth);

                Vector2 uvA = surf.UV(i, j);
                Vector2 uvB = surf.UV(i, j + 1);
                float vPerMetre = 1f / (Mathf.Max(0.05f, surf.Speed(i)) * scale);
                float dropA = (surf.SurfaceHeight[i, j] + s.skirtDepth) * vPerMetre;
                float dropB = (surf.SurfaceHeight[i, j + 1] + s.skirtDepth) * vPerMetre;

                AddOrientedQuadUV(buf, topA, topB, botA, botB,
                                  uvA, uvB,
                                  uvA + new Vector2(0f, downstream * dropA),
                                  uvB + new Vector2(0f, downstream * dropB),
                                  surf.Flow(i, j), surf.Flow(i, j + 1),
                                  surf.Flow(i, j), surf.Flow(i, j + 1),
                                  outward, slot, 0.95f);
            }
        }

        // ================================================================== props

        /// <summary>Slabs of crust tipped up on edge, where rafts have jammed against each other.</summary>
        static void BuildSlabs(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf, ref Rng rng)
        {
            int c = surf.Center;
            for (int k = 0; k < s.slabCount; k++)
            {
                int i = rng.Range(1, Mathf.Max(2, path.Count - 1));
                int j = rng.Range(c - surf.ChannelHalf, c + surf.ChannelHalf + 1);

                // Slabs are wreckage of a crust, so only where there is enough crust to wreck.
                if (rng.Value() > surf.Coverage(i)) continue;

                FlowStation st = path.Stations[i];
                Vector3 baseP = surf.P(i, j, surf.MoltenHeight[i, j] + s.crustThickness * 0.5f);
                Vector3 normal = path.Normal[i, j];

                float size = s.slabSize * rng.Range(0.6f, 1.5f);
                float height = s.slabHeight * rng.Range(0.5f, 1.4f);
                float tiltAngle = rng.Range(35f, 85f);

                // Tipped up facing back upstream, the way a raft rides up over the one behind it.
                Vector3 lift = RotateAround(-st.Forward, st.Right, tiltAngle);
                Vector3 wide = st.Right * (size * 0.5f);
                Vector3 thick = st.Forward * (size * 0.12f);

                Vector3 b0 = baseP - wide - thick;
                Vector3 b1 = baseP + wide - thick;
                Vector3 b2 = baseP - wide + thick;
                Vector3 b3 = baseP + wide + thick;
                Vector3 up = lift.normalized * height;

                Vector3 t0 = b0 + up + normal * (height * 0.15f);
                Vector3 t1 = b1 + up + normal * (height * 0.15f);
                Vector3 t2 = b2 + up + normal * (height * 0.15f);
                Vector3 t3 = b3 + up + normal * (height * 0.15f);

                bool glowing = rng.Chance(0.35f);
                LavaSlot slot = glowing ? LavaSlot.CrustWarm : LavaSlot.CrustDark;
                float shade = rng.Range(0.8f, 1.2f);

                // Every face turned away from the middle of the slab, so a tipped-up plate is solid
                // from whichever side you walk past it.
                Vector3 core = (b0 + b1 + b2 + b3 + t0 + t1 + t2 + t3) * 0.125f;
                AddSlabFace(buf, b0, b1, b2, b3, core, slot, shade * 0.7f);        // underside
                AddSlabFace(buf, t0, t1, t2, t3, core, slot, shade);               // top
                AddSlabFace(buf, b0, b1, t0, t1, core, slot, shade * 0.9f);        // upstream face
                AddSlabFace(buf, b2, b3, t2, t3, core, slot, shade * 0.8f);        // downstream face
                AddSlabFace(buf, b0, b2, t0, t2, core, slot, shade * 0.85f);       // sides
                AddSlabFace(buf, b1, b3, t1, t3, core, slot, shade * 0.85f);
            }
        }

        /// <summary>One face of a slab, turned away from the middle of the box it belongs to.</summary>
        static void AddSlabFace(MeshBuffer buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                Vector3 core, LavaSlot slot, float shade)
        {
            Vector3 outward = (a + b + c + d) * 0.25f - core;
            AddOrientedQuad(buf, a, b, c, d, outward, slot, shade);
        }

        /// <summary>
        /// Rotates <paramref name="v"/> about a unit <paramref name="axis"/>, in degrees. Written
        /// out rather than going through Quaternion.AngleAxis, which is a native call: keeping the
        /// builder pure managed is what lets it be run and asserted against outside the Editor.
        /// </summary>
        static Vector3 RotateAround(Vector3 v, Vector3 axis, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1f - cos));
        }

        /// <summary>Domes swelling out of the open lava. Only on the slow stretches; nothing has
        /// time to bubble on a cascade.
        ///
        /// A bubble is molten, so it takes the channel's own UVs rather than a projection: it is
        /// offset from its station in the same two directions the ribbon is parameterised by, which
        /// makes its UV nothing more than the surface's UV shifted by that offset. The pattern then
        /// runs over the dome the way it runs over the lava around it, in the same direction and at
        /// the same speed, instead of sitting on it as a still patch of its own.</summary>
        static void BuildBubbles(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf, ref Rng rng)
        {
            int c = surf.Center;
            float scale = surf.UVScale;

            for (int k = 0; k < s.bubbleCount; k++)
            {
                int i = rng.Range(1, Mathf.Max(2, path.Count - 1));
                if (path.Stations[i].SlopeNorm > 0.4f) continue;

                int j = rng.Range(c - surf.ChannelHalf + 1, c + surf.ChannelHalf);
                Vector3 center = surf.Molten(i, j);
                Vector3 normal = path.Normal[i, j];
                Vector3 right = path.Stations[i].Right;
                Vector3 forward = Vector3.Cross(right, normal).normalized;

                Vector2 centerUV = surf.UV(i, j);
                Vector2 flow = surf.Flow(i, j);

                float r = s.bubbleSize * rng.Range(0.5f, 1.5f);
                float h = r * rng.Range(0.5f, 1.1f);

                const int Sides = 6;
                var ring = new Vector3[Sides];
                var ringUV = new Vector2[Sides];
                for (int t = 0; t < Sides; t++)
                {
                    float ang = t / (float)Sides * Mathf.PI * 2f;
                    float rr = r * (0.8f + rng.Value() * 0.4f);

                    // Across the channel and downstream, which is exactly what U and V measure.
                    // V is in travel time, so the downstream offset converts through the local speed.
                    float across = Mathf.Cos(ang) * rr;
                    float along = Mathf.Sin(ang) * rr;

                    ring[t] = center + right * across + forward * along;
                    ringUV[t] = centerUV + new Vector2(across / scale,
                                                       along / (Mathf.Max(0.05f, surf.Speed(i)) * scale));
                }

                buf.AddFanUV(center + normal * h, centerUV, ring, ringUV, flow, true,
                             LavaSlot.Molten, rng.Range(0.95f, 1.25f));
            }
        }

        /// <summary>Boulders stranded on the banks, rafted down and left behind.</summary>
        static void BuildRocks(MeshBuffer buf, LavaFlowSettings s, FlowPath path, Surface surf, ref Rng rng)
        {
            int c = surf.Center;
            for (int k = 0; k < s.rockCount; k++)
            {
                int i = rng.Range(0, Mathf.Max(1, path.Count));
                int side = rng.Chance(0.5f) ? -1 : 1;
                int j = c + side * rng.Range(surf.ChannelHalf, c + 1);
                j = Mathf.Clamp(j, 0, surf.Lateral - 1);

                Vector3 seat = surf.P(i, j, surf.SurfaceHeight[i, j]);
                Vector3 normal = path.Normal[i, j];
                Vector3 right = path.Stations[i].Right;
                Vector3 forward = Vector3.Cross(right, normal).normalized;

                float r = s.rockSize * rng.Range(0.45f, 1.6f);
                float h = r * rng.Range(0.6f, 1.3f);

                const int Sides = 5;
                var ring = new Vector3[Sides];
                for (int t = 0; t < Sides; t++)
                {
                    float ang = t / (float)Sides * Mathf.PI * 2f + rng.Value() * 0.4f;
                    float rr = r * (0.7f + rng.Value() * 0.6f);
                    ring[t] = seat + right * (Mathf.Cos(ang) * rr) + forward * (Mathf.Sin(ang) * rr)
                              - normal * (r * 0.2f);
                }

                Vector3 apex = seat + normal * h + right * rng.Signed(r * 0.25f);
                buf.AddFan(apex, ring, true, LavaSlot.Rock, rng.Range(0.75f, 1.15f));
                // The underside, wound the other way so the boulder is closed from below too.
                buf.AddFan(seat - normal * (r * 0.3f), ring, false, LavaSlot.Rock, rng.Range(0.6f, 0.9f));
            }
        }

        // ================================================================== plates

        /// <summary>
        /// Breaks the channel into plates: a jittered Voronoi field measured in metres along and
        /// across the flow, with cells stretched down-flow because that is the direction the skin is
        /// being dragged in.
        /// </summary>
        public sealed class PlateField
        {
            readonly float _along;
            readonly float _across;
            readonly float _jitter;
            readonly int _seed;

            public PlateField(LavaFlowSettings s)
            {
                _along = Mathf.Max(0.2f, s.plateLength);
                _across = Mathf.Max(0.2f, s.plateWidth);
                _jitter = Mathf.Clamp01(s.plateJitter);
                _seed = s.seed + 1013;
            }

            /// <summary>Id of the plate covering this point. Stable for a given seed.</summary>
            public int Id(float alongMetres, float acrossMetres)
            {
                float x = alongMetres / _along;
                float y = acrossMetres / _across;
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
                        float sx = cx + 0.5f + (FlowNoise.Hash(cx, cy, _seed) - 0.5f) * _jitter;
                        float sy = cy + 0.5f + (FlowNoise.Hash(cx, cy, _seed + 517) - 0.5f) * _jitter;

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
