using System.Collections.Generic;
using UnityEngine;

namespace RockBridge
{
    /// <summary>One cross-section's worth of the solved crossing.</summary>
    public struct BridgeSample
    {
        /// <summary>Middle of the driving surface, in the generator's local space.</summary>
        public Vector3 Position;

        /// <summary>Direction of travel.</summary>
        public Vector3 Tangent;

        /// <summary>Surface normal after levelling and banking.</summary>
        public Vector3 Up;

        /// <summary>Across the deck, towards the right-hand edge. Always <c>Cross(Up, Tangent)</c>.</summary>
        public Vector3 Right;

        /// <summary>Half the width of the driving surface here, verge and parapet not included.</summary>
        public float HalfWidth;

        /// <summary>Per-node parapet height multiplier, splined along the path.</summary>
        public float WallScale;

        /// <summary>Total bank actually applied here, in degrees. Positive raises the right edge.</summary>
        public float Bank;

        /// <summary>
        /// The part of <see cref="Bank"/> that came off the nodes rather than out of the corner.
        /// Kept apart so respacing the nodes can hand the authored lean back without also baking in
        /// the automatic banking — which would then be banked a second time on the next rebuild.
        /// </summary>
        public float AuthoredBank;

        /// <summary>Per-node height nudge, splined along the path, in metres.</summary>
        public float HeightOffset;

        /// <summary>Signed curvature in 1/m — positive turns right. Its reciprocal is the corner radius.</summary>
        public float Curvature;

        /// <summary>Distance from the first sample, along the bridge, in metres.</summary>
        public float Distance;

        /// <summary>True when a ground probe found anything at all under this cross-section.</summary>
        public bool HasGround;

        /// <summary>Top of whatever is under here — lava, rock, hillside — in local Y.</summary>
        public float GroundSurface;

        /// <summary>Top of the solid floor under here, in local Y. This is where a leg foots.</summary>
        public float GroundFloor;
    }

    /// <summary>
    /// Turns a list of nodes into the solved crossing: positions, heights, frames, banking and
    /// widths, sampled densely enough that a curve reads as a curve.
    ///
    /// A bridge is point-to-point — there is no closed-loop case here, and dropping it takes the
    /// frame-seam problem with it.
    ///
    /// Four decisions make the surface drivable, and all four are worth understanding before
    /// touching anything:
    ///
    /// <b>The height is a profile, not a set of node heights.</b> This is the difference between
    /// this package and <c>RaceTrack</c>, which it otherwise mirrors. The deck holds one level
    /// across the span and eases down onto the real ground at both ends, so the landing is tied to
    /// the shore at whatever height the middle is flying at. Raising
    /// <see cref="BridgeSettings.deckHeight"/> lifts the span and lengthens every leg without
    /// touching anything else, and the ends stay put.
    ///
    /// <b>The frame is parallel-transported, then levelled.</b> Transport alone never spins the
    /// section as the path climbs and turns, so the deck carries no twist nobody asked for — but
    /// transport has no opinion about which way is down. The transported frame is rotated back
    /// towards level, then banked.
    ///
    /// <b>Sampling density comes off the peak turn rate, not the total turn.</b> Sections are
    /// spread evenly in the span's parameter, so dividing a span's total bend by the allowance only
    /// holds if it bends at a steady rate — through an S-bend it does not, and the flat lands in
    /// the middle of the very corner the setting exists to smooth.
    ///
    /// <b>Banking is smoothed over distance, not applied per section.</b> Curvature off a
    /// Catmull-Rom spline is twitchy, and feeding it straight into the bank angle ripples the
    /// surface.
    /// </summary>
    public class BridgePath
    {
        /// <summary>The solved cross-sections, in order.</summary>
        public readonly List<BridgeSample> Samples = new List<BridgeSample>();

        /// <summary>Length of the crossing in metres.</summary>
        public float Length;

        /// <summary>The level the span settled at, in local Y. Meaningless on Free height mode.</summary>
        public float SpanLevel;

        /// <summary>The surface height the span was held clear of, in local Y.</summary>
        public float Datum;

        /// <summary>True when at least one cross-section found nothing underneath it.</summary>
        public bool HasGaps;

        const float Gravity = 9.81f;

        // ------------------------------------------------------------------ build

        /// <summary>
        /// Solves the crossing. <paramref name="ground"/> may be null, in which case the height
        /// modes that measure the world fall back to <see cref="BridgeSettings.flatGroundHeight"/>.
        ///
        /// <paramref name="toWorld"/> and <paramref name="toLocal"/> carry the generator's
        /// transform, because the nodes are local and the ground is not. Height automation assumes
        /// the object's up is world up; the generator warns when it is not.
        /// </summary>
        public static BridgePath Build(IList<BridgeNode> nodes, BridgeSettings settings,
                                       IBridgeGround ground, Matrix4x4 toWorld, Matrix4x4 toLocal)
        {
            var path = new BridgePath();
            if (nodes == null || settings == null || nodes.Count < 2) return path;

            SamplePositions(nodes, settings, path);
            int n = path.Samples.Count;
            if (n < 2) { path.Samples.Clear(); return path; }

            var segLen = new float[n];
            MeasureDistances(path, segLen);
            if (path.Length < 1e-4f) { path.Samples.Clear(); return path; }

            // Heights first: everything after this measures the shape the deck actually has.
            ProbeGround(path, settings, ground, toWorld, toLocal);
            ApplyHeightProfile(path, settings, segLen);
            MeasureDistances(path, segLen);

            var tangents = new Vector3[n];
            BuildTangents(path, tangents);

            var refUp = new Vector3[n];
            BuildTransportedFrame(path, tangents, refUp);

            var curvature = new float[n];
            MeasureCurvature(path, tangents, refUp, curvature);

            var bank = new float[n];
            BuildBanking(path, settings, curvature, segLen, bank);

            ApplyFrames(path, tangents, refUp, curvature, bank);
            return path;
        }

        /// <summary>Convenience overload for tests and for anything with no transform to speak of.</summary>
        public static BridgePath Build(IList<BridgeNode> nodes, BridgeSettings settings, IBridgeGround ground)
        {
            return Build(nodes, settings, ground, Matrix4x4.identity, Matrix4x4.identity);
        }

        // --------------------------------------------------------------- sampling

        static void SamplePositions(IList<BridgeNode> nodes, BridgeSettings settings, BridgePath path)
        {
            int n = nodes.Count;
            float spacing = Mathf.Max(0.05f, settings.sectionSpacing);

            for (int i = 0; i < n - 1; i++)
            {
                BridgeNode b = nodes[i];
                BridgeNode c = nodes[i + 1];
                BridgeNode a = nodes[Mathf.Max(i - 1, 0)];
                BridgeNode d = nodes[Mathf.Min(i + 2, n - 1)];

                // Mirror the missing control point at each end so the curve does not kink there.
                Vector3 p0 = i == 0 ? b.position + (b.position - c.position) : a.position;
                Vector3 p3 = i == n - 2 ? c.position + (c.position - b.position) : d.position;

                Knots knots = Knots.For(p0, b.position, c.position, p3, settings.curveAlpha);

                float length, turnDegrees, peakTurnRate;
                MeasureSpan(p0, b.position, c.position, p3, knots,
                            out length, out turnDegrees, out peakTurnRate);

                float allowance = Mathf.Max(0.1f, settings.degreesPerSection);
                int byLength = Mathf.CeilToInt(length / spacing);
                int byTurn = Mathf.CeilToInt(turnDegrees / allowance);
                int byPeak = Mathf.CeilToInt(peakTurnRate / allowance);
                int steps = Mathf.Clamp(Mathf.Max(byLength, Mathf.Max(byTurn, byPeak)), 1, 2048);

                for (int k = 0; k < steps; k++)
                {
                    // The last sample of a span is the first of the next one, so stop short of t = 1
                    // and let the following span emit it.
                    path.Samples.Add(Interpolate(a, b, c, d, p0, p3, knots, (float)k / steps, settings));
                }
            }

            // The loop above stops one short of each span's end, so the final node is still owed.
            BridgeNode tail = nodes[n - 1];
            path.Samples.Add(new BridgeSample
            {
                Position = tail.position,
                HalfWidth = HalfWidthOf(tail, settings),
                WallScale = Mathf.Max(0f, tail.wallScale),
                AuthoredBank = tail.bank,
                HeightOffset = tail.heightOffset
            });
        }

        static float HalfWidthOf(BridgeNode node, BridgeSettings settings)
        {
            float full = settings.uniformWidth ? settings.deckWidth : node.width;
            return Mathf.Max(1.5f, full) * 0.5f;
        }

        static BridgeSample Interpolate(BridgeNode a, BridgeNode b, BridgeNode c, BridgeNode d,
                                        Vector3 p0, Vector3 p3, Knots k, float t, BridgeSettings settings)
        {
            float half;
            if (settings.uniformWidth)
            {
                // Not splined, not interpolated, not derived — literally the same number at every
                // cross-section. This is what makes "the deck never narrows" a guarantee rather
                // than a tolerance.
                half = Mathf.Max(1.5f, settings.deckWidth) * 0.5f;
            }
            else
            {
                // Clamped to the two nodes this span actually runs between, not merely to a positive
                // number. A Catmull-Rom through 20, 20, 34, 34 dips under 19 on the approach before
                // it swells, and a dip is a pinch in the road.
                float lo = Mathf.Min(b.width, c.width) * 0.5f;
                float hi = Mathf.Max(b.width, c.width) * 0.5f;
                float splined = Spline(a.width, b.width, c.width, d.width, k, t) * 0.5f;
                half = Mathf.Clamp(splined, Mathf.Max(0.75f, lo), Mathf.Max(0.75f, hi));
            }

            return new BridgeSample
            {
                Position = Spline(p0, b.position, c.position, p3, k, t),
                HalfWidth = half,
                WallScale = Mathf.Max(0f, Spline(a.wallScale, b.wallScale, c.wallScale, d.wallScale, k, t)),
                AuthoredBank = Spline(a.bank, b.bank, c.bank, d.bank, k, t),
                HeightOffset = Spline(a.heightOffset, b.heightOffset, c.heightOffset, d.heightOffset, k, t)
            };
        }

        // ----------------------------------------------------------------- ground

        /// <summary>
        /// Fills in what is under every cross-section.
        ///
        /// The probe is taken in world space and the answer brought back into local, because the
        /// nodes are local and the terrain is not. Only the height is carried across, which is exact
        /// while the object's own up is world up — a tilted bridge would need the whole frame
        /// rotated, and the generator warns rather than pretending otherwise.
        /// </summary>
        static void ProbeGround(BridgePath path, BridgeSettings settings, IBridgeGround ground,
                                Matrix4x4 toWorld, Matrix4x4 toLocal)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                BridgeSample s = path.Samples[i];
                float fallback = LocalHeightOf(settings.flatGroundHeight, s.Position, toWorld, toLocal);

                GroundSample g;
                if (ground != null && ground.Sample(toWorld.MultiplyPoint3x4(s.Position), out g) && g.Found)
                {
                    s.HasGround = true;
                    s.GroundSurface = LocalHeightOf(g.Surface, s.Position, toWorld, toLocal);
                    s.GroundFloor = LocalHeightOf(g.Floor, s.Position, toWorld, toLocal);
                }
                else
                {
                    s.HasGround = false;
                    s.GroundSurface = fallback;
                    s.GroundFloor = fallback;
                    path.HasGaps = true;
                }

                path.Samples[i] = s;
            }
        }

        /// <summary>Local Y of the point directly above or below <paramref name="local"/> at world height.</summary>
        static float LocalHeightOf(float worldY, Vector3 local, Matrix4x4 toWorld, Matrix4x4 toLocal)
        {
            Vector3 world = toWorld.MultiplyPoint3x4(local);
            return toLocal.MultiplyPoint3x4(new Vector3(world.x, worldY, world.z)).y;
        }

        // ---------------------------------------------------------------- heights

        /// <summary>
        /// Rewrites every cross-section's height according to the height mode. This is the part of
        /// the package that is not <c>RaceTrack</c>.
        ///
        /// Both automatic modes end the same way: the deck is pulled down onto the real ground over
        /// the last <see cref="BridgeSettings.approachLength"/> metres at each end, on an eased ramp
        /// that leaves and arrives with no kink. That is what makes the landing safe to drive at any
        /// span height — nothing about the ends changes when the middle is raised.
        /// </summary>
        static void ApplyHeightProfile(BridgePath path, BridgeSettings settings, float[] segLen)
        {
            int n = path.Samples.Count;
            if (settings.heightMode == BridgeHeightMode.Free)
            {
                for (int i = 0; i < n; i++)
                {
                    BridgeSample s = path.Samples[i];
                    s.Position = new Vector3(s.Position.x, s.Position.y + s.HeightOffset, s.Position.z);
                    path.Samples[i] = s;
                }
                path.SpanLevel = path.Samples[n / 2].Position.y;
                path.Datum = path.Samples[n / 2].GroundSurface;
                return;
            }

            // Both ramps together cannot be longer than the bridge, or there is no span left to be
            // level and the two pull-downs fight each other over the middle.
            float approach = Mathf.Clamp(settings.approachLength, 1f, path.Length * 0.45f);

            var target = new float[n];

            if (settings.heightMode == BridgeHeightMode.FollowGround)
            {
                for (int i = 0; i < n; i++) target[i] = path.Samples[i].GroundSurface + settings.deckHeight;
                target = SmoothByDistance(target, segLen, settings.heightSmoothing);
                path.Datum = path.Samples[n / 2].GroundSurface;
                path.SpanLevel = target[n / 2];
            }
            else
            {
                // The datum is the highest thing the bridge actually has to clear: the top of
                // whatever is *lying on* the ground, anywhere along the route. Bare ground is
                // skipped — its surface and its floor are the same height — so the landings never
                // drag the span up with them.
                //
                // The earlier rule measured over the level span only and excluded the approaches.
                // That has a hole in it, and it opened on this project's own pond: the pool carries
                // crust slabs up to 15 m, one of them sat under an approach ramp, and the span
                // settled 2.6 m lower than it needed to. Clearance came out at 0.48 m while the
                // datum cheerfully reported the bridge was 5 m above what it crossed. Using the
                // same test here as TightestClearance uses is what stops those two disagreeing.
                float datum = float.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    BridgeSample s = path.Samples[i];
                    if (s.GroundSurface - s.GroundFloor < 0.5f) continue;
                    datum = Mathf.Max(datum, s.GroundSurface);
                }

                // Nothing lying on the ground anywhere: a bridge over a dry gorge. Clear the ground
                // itself over the span instead, which is the sensible reading of "how high".
                if (float.IsNegativeInfinity(datum))
                {
                    for (int i = 0; i < n; i++)
                    {
                        BridgeSample s = path.Samples[i];
                        if (s.Distance < approach || path.Length - s.Distance < approach) continue;
                        datum = Mathf.Max(datum, s.GroundSurface);
                    }
                }
                if (float.IsNegativeInfinity(datum))
                    for (int i = 0; i < n; i++) datum = Mathf.Max(datum, path.Samples[i].GroundSurface);

                if (settings.useFixedDatum) datum = settings.fixedDatum;

                path.Datum = datum;
                path.SpanLevel = datum + settings.deckHeight;
                for (int i = 0; i < n; i++) target[i] = path.SpanLevel;
            }

            // The landings come down to the solid floor, not to the surface.
            //
            // Those differ by exactly the thing lying on the ground, and at a landing that thing is
            // a prop rather than an obstacle to be cleared: a bridge whose end node happened to
            // fall beside a 40 m boulder took the boulder as its ground and tried to land on top of
            // it, dragging the whole approach ramp up with it. What has to be cleared is measured
            // over the span; what has to be met is measured here.
            float startY = path.Samples[0].GroundFloor - settings.landingSink;
            float endY = path.Samples[n - 1].GroundFloor - settings.landingSink;

            for (int i = 0; i < n; i++)
            {
                BridgeSample s = path.Samples[i];

                float toStart = BridgeMath.Ease01(s.Distance / approach);
                float toEnd = BridgeMath.Ease01((path.Length - s.Distance) / approach);

                float y = target[i];
                y = Mathf.Lerp(startY, y, toStart);
                y = Mathf.Lerp(endY, y, toEnd);

                // sin^2 leaves and arrives with zero value *and* zero slope, so the hump can never
                // tilt a landing however large it is set. A parabola would arrive at an angle.
                if (settings.arch > 0.001f && path.Length > 1e-3f)
                {
                    float ramp = Mathf.Sin(Mathf.PI * s.Distance / path.Length);
                    y += settings.arch * ramp * ramp;
                }

                s.Position = new Vector3(s.Position.x, y + s.HeightOffset, s.Position.z);
                path.Samples[i] = s;
            }
        }

        // -------------------------------------------------------------- distances

        static void MeasureDistances(BridgePath path, float[] segLen)
        {
            int n = path.Samples.Count;
            float dist = 0f;

            for (int i = 0; i < n; i++)
            {
                BridgeSample s = path.Samples[i];
                s.Distance = dist;
                path.Samples[i] = s;

                float step = i + 1 < n ? Vector3.Distance(path.Samples[i + 1].Position, s.Position) : 0f;
                segLen[i] = step;
                dist += step;
            }

            path.Length = dist;
        }

        // --------------------------------------------------------------- tangents

        static void BuildTangents(BridgePath path, Vector3[] tangents)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 next = path.Samples[Next(i, n)].Position;
                Vector3 prev = path.Samples[Prev(i, n)].Position;

                Vector3 t = next - prev;
                if (t.sqrMagnitude < 1e-12f) t = i > 0 ? tangents[i - 1] : Vector3.forward;
                tangents[i] = t.normalized;
            }
        }

        /// <summary>Parallel transport of the surface normal along the path.</summary>
        static void BuildTransportedFrame(BridgePath path, Vector3[] tangents, Vector3[] refUp)
        {
            int n = path.Samples.Count;

            Vector3 start = BridgeMath.OrthoNormal(Vector3.up, tangents[0]);
            if (start == Vector3.zero)
            {
                start = BridgeMath.OrthoNormal(Vector3.forward, tangents[0]);
                if (start == Vector3.zero) start = BridgeMath.OrthoNormal(Vector3.right, tangents[0]);
            }
            refUp[0] = start;

            for (int i = 1; i < n; i++)
            {
                Vector3 carried = BridgeMath.Transport(refUp[i - 1], tangents[i - 1], tangents[i]);
                carried = BridgeMath.OrthoNormal(carried, tangents[i]);
                refUp[i] = carried == Vector3.zero ? refUp[i - 1] : carried;
            }
        }

        // -------------------------------------------------------------- curvature

        static void MeasureCurvature(BridgePath path, Vector3[] tangents, Vector3[] refUp, float[] curvature)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                int a = Prev(i, n);
                int b = Next(i, n);

                float span = Vector3.Distance(path.Samples[i].Position, path.Samples[a].Position)
                           + Vector3.Distance(path.Samples[b].Position, path.Samples[i].Position);
                if (span < 1e-5f) { curvature[i] = 0f; continue; }

                Vector3 dT = (tangents[b] - tangents[a]) / span;

                // Measured against the frame's own right, so what counts as "turning right" is what
                // the deck thinks is right — which stays correct through a banked section.
                Vector3 right = Vector3.Cross(refUp[i], tangents[i]);
                curvature[i] = Vector3.Dot(dT, right);
            }
        }

        // ---------------------------------------------------------------- banking

        static void BuildBanking(BridgePath path, BridgeSettings settings,
                                 float[] curvature, float[] segLen, float[] bank)
        {
            int n = path.Samples.Count;
            var auto = new float[n];

            float v = Mathf.Max(0.1f, settings.bankSpeed);
            float cap = Mathf.Clamp(settings.maxAutoBank, 0f, 89f);

            for (int i = 0; i < n; i++)
            {
                // The angle at which a kart at bankSpeed would sit flat in its seat through this
                // corner: tan(angle) = v^2 / (g * radius), and 1/radius is the curvature.
                float ideal = Mathf.Atan(v * v * curvature[i] / Gravity) * Mathf.Rad2Deg;

                // Turning right means the inside of the corner is on the right, so the right edge is
                // the one that drops — hence the negative.
                auto[i] = Mathf.Clamp(-ideal * settings.autoBank, -cap, cap);
            }

            float[] smoothed = SmoothByDistance(auto, segLen, settings.bankSmoothing);

            // Authored bank is added raw. It is a deliberate instruction about one place, and
            // smearing it over 30 m of deck would blunt exactly the thing it was set for.
            for (int i = 0; i < n; i++) bank[i] = smoothed[i] + path.Samples[i].AuthoredBank;
        }

        /// <summary>
        /// Moving average over a window measured in metres of bridge rather than in samples, so the
        /// result does not change when the section spacing does.
        /// </summary>
        static float[] SmoothByDistance(float[] values, float[] segLen, float window)
        {
            int n = values.Length;
            var result = new float[n];
            if (window <= 0.01f || n < 3)
            {
                System.Array.Copy(values, result, n);
                return result;
            }

            float half = window * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float sum = values[i];
                int count = 1;

                // segLen[j] is the gap from j to j+1, so stepping back from i crosses segLen[j]
                // where j is the sample being stepped onto.
                float walked = 0f;
                for (int cursor = i; cursor > 0; )
                {
                    int prev = cursor - 1;
                    walked += segLen[prev];
                    if (walked > half) break;
                    sum += values[prev];
                    count++;
                    cursor = prev;
                }

                walked = 0f;
                for (int cursor = i; cursor < n - 1; )
                {
                    walked += segLen[cursor];
                    if (walked > half) break;
                    sum += values[cursor + 1];
                    count++;
                    cursor++;
                }

                result[i] = sum / count;
            }

            return result;
        }

        // ----------------------------------------------------------------- frames

        static void ApplyFrames(BridgePath path, Vector3[] tangents, Vector3[] refUp,
                                float[] curvature, float[] bank)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 t = tangents[i];
                Vector3 up = refUp[i];

                // A bridge deck is always levelled — a rock crossing has no reason to corkscrew, and
                // the legs have to stand up straight underneath it either way.
                Vector3 level = BridgeMath.OrthoNormal(Vector3.up, t);

                // |level| before normalising is how horizontal the deck is here. A near-vertical
                // deck has no meaningful "level", so the pull is faded out rather than allowed to
                // snap the section round. Nothing sane gets there, but a dragged node can.
                float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - t.y * t.y));
                float fade = BridgeMath.Ease01(Mathf.InverseLerp(0.05f, 0.20f, horizontal));

                if (level != Vector3.zero && fade > 0.001f)
                {
                    float toLevel = BridgeMath.SignedAngle(up, level, t);
                    up = BridgeMath.OrthoNormal(BridgeMath.Rotate(up, t, toLevel * fade), t);
                }

                if (up == Vector3.zero) up = refUp[i];
                if (Mathf.Abs(bank[i]) > 0.001f) up = BridgeMath.OrthoNormal(BridgeMath.Rotate(up, t, bank[i]), t);
                if (up == Vector3.zero) up = refUp[i];

                BridgeSample s = path.Samples[i];
                s.Tangent = t;
                s.Up = up;
                s.Right = Vector3.Cross(up, t).normalized;
                s.Bank = bank[i];
                s.Curvature = curvature[i];
                path.Samples[i] = s;
            }
        }

        // ------------------------------------------------------------- inspection

        /// <summary>
        /// How much room the inside edge of the sweep has left, at its worst point.
        ///
        /// This is the number that answers "does it pinch anywhere". A swept ribbon keeps its width
        /// by construction, so the only way it can narrow is by folding: turn tighter than the
        /// section is wide and the inner edge sweeps backwards through itself. The measure is
        /// <c>1 - outerHalfWidth / cornerRadius</c> — 1 on a straight, 0 at the exact radius where
        /// the outermost point of the parapet stops advancing, negative once it has folded.
        /// </summary>
        public float WorstEdgeAdvance(BridgeSettings settings)
        {
            int ignored;
            return WorstEdgeAdvance(settings, out ignored);
        }

        /// <summary>As <see cref="WorstEdgeAdvance(BridgeSettings)"/>, and where it happens.</summary>
        public float WorstEdgeAdvance(BridgeSettings settings, out int section)
        {
            float worst = 1f;
            section = -1;

            for (int i = 0; i < Samples.Count; i++)
            {
                float outer = settings.OuterHalfWidth(Samples[i].HalfWidth);
                float advance = 1f - outer * Mathf.Abs(Samples[i].Curvature);
                if (advance >= worst) continue;
                worst = advance;
                section = i;
            }
            return Samples.Count == 0 ? 1f : worst;
        }

        /// <summary>
        /// Tightest corner radius anywhere on the solved line, in metres. Infinity if straight.
        ///
        /// This is the authoritative number, and it is not the same as the radius of the circle
        /// through three nodes: a curve through unevenly spaced nodes bends harder between them than
        /// the node polygon suggests, so a layout whose every node looks legal can still have a
        /// corner the karts cannot hold. Where the two disagree, believe this one.
        /// </summary>
        public float TightestRadius()
        {
            int ignored;
            return TightestRadius(out ignored);
        }

        /// <summary>As <see cref="TightestRadius()"/>, and which cross-section it happens at.</summary>
        public float TightestRadius(out int section)
        {
            float worst = float.PositiveInfinity;
            section = -1;

            for (int i = 0; i < Samples.Count; i++)
            {
                float k = Mathf.Abs(Samples[i].Curvature);
                if (k <= 1e-6f) continue;

                float radius = 1f / k;
                if (radius >= worst) continue;
                worst = radius;
                section = i;
            }
            return worst;
        }

        /// <summary>Steepest climb or drop anywhere, in degrees from horizontal.</summary>
        public float SteepestGradient()
        {
            int ignored;
            return SteepestGradient(out ignored);
        }

        /// <summary>As <see cref="SteepestGradient()"/>, and where — almost always on an approach.</summary>
        public float SteepestGradient(out int section)
        {
            float worst = 0f;
            section = -1;

            for (int i = 0; i < Samples.Count; i++)
            {
                float g = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Samples[i].Tangent.y, -1f, 1f)) * Mathf.Rad2Deg);
                if (g <= worst) continue;
                worst = g;
                section = i;
            }
            return worst;
        }

        /// <summary>
        /// Tightest vertical curve anywhere, in metres of radius — where the ramp stops climbing and
        /// the span begins, almost always.
        ///
        /// This is the number that decides whether a kart flies off the top of the approach, and it
        /// is not the gradient. A ramp can be gentle everywhere and still throw a car at the crest
        /// if it flattens out over too short a distance: what the suspension feels is
        /// <c>v^2 / R</c>, so 26 m/s over a 70 m radius is a full g of lift and the wheels leave the
        /// deck. Gradient alone says nothing about it.
        ///
        /// Measured on the curve as it will be driven — run along the ground, rise in Y — rather
        /// than in 3D, so a bend that is also a climb does not read as a crest.
        /// </summary>
        public float MinVerticalRadius()
        {
            int ignored;
            return MinVerticalRadius(out ignored);
        }

        /// <summary>As <see cref="MinVerticalRadius()"/>, and where it happens.</summary>
        public float MinVerticalRadius(out int section)
        {
            float worst = float.PositiveInfinity;
            section = -1;

            for (int i = 1; i < Samples.Count - 1; i++)
            {
                Vector3 a = Samples[i - 1].Position;
                Vector3 b = Samples[i].Position;
                Vector3 c = Samples[i + 1].Position;

                float run1 = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                float run2 = new Vector2(c.x - b.x, c.z - b.z).magnitude;
                if (run1 < 1e-4f || run2 < 1e-4f) continue;

                float dSlope = Mathf.Abs((c.y - b.y) / run2 - (b.y - a.y) / run1);
                if (dSlope < 1e-7f) continue;

                float r = (run1 + run2) * 0.5f / dSlope;
                if (r >= worst) continue;
                worst = r;
                section = i;
            }
            return worst;
        }

        /// <summary>
        /// How much lift or squash a kart at <paramref name="speed"/> m/s feels at the tightest
        /// vertical curve, in g. Past about 1 the wheels leave the deck on a crest.
        /// </summary>
        public float VerticalLoadAt(float speed)
        {
            float r = MinVerticalRadius();
            if (float.IsInfinity(r) || r < 1e-3f) return 0f;
            return speed * speed / r / 9.81f;
        }

        /// <summary>Largest bank angle applied anywhere, in degrees.</summary>
        public float MaxBank()
        {
            float worst = 0f;
            for (int i = 0; i < Samples.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(Samples[i].Bank));
            return worst;
        }

        /// <summary>
        /// Highest the underside of the deck gets above the floor beneath it, in metres — the
        /// longest leg the bridge is asking for.
        /// </summary>
        public float TallestDrop(BridgeSettings settings)
        {
            float worst = 0f;
            for (int i = 0; i < Samples.Count; i++)
            {
                if (!Samples[i].HasGround) continue;
                worst = Mathf.Max(worst, Samples[i].Position.y - settings.deckThickness - Samples[i].GroundFloor);
            }
            return worst;
        }

        /// <summary>
        /// Least room between the underside of the deck and the thing being crossed.
        ///
        /// Measured only where there actually <em>is</em> something to clear — a station whose
        /// surface stands above its own solid floor, which is exactly what a pool of lava lying in
        /// a basin looks like to the probe. On bare ground the two are the same height and the
        /// station is skipped, because a deck is meant to be in the ground at its landings.
        ///
        /// That rule replaced "ignore the first and last <c>approachLength</c> metres", which had a
        /// hole in it: on a crossing with no room for long approaches the ramps run out over the
        /// pool, and the one place the deck can dip into the lava is precisely the place that
        /// version refused to look at.
        ///
        /// Negative means the deck is inside whatever it is crossing.
        /// </summary>
        public float TightestClearance(BridgeSettings settings, out int section)
        {
            float worst = float.PositiveInfinity;
            section = -1;

            for (int i = 0; i < Samples.Count; i++)
            {
                BridgeSample s = Samples[i];
                if (!s.HasGround) continue;

                // What has to be cleared here, and whether there is anything at all.
                float over;
                if (settings.useFixedDatum)
                {
                    // A fixed datum is for something the probe cannot see, so there is no raised
                    // surface to key off. Fall back to the span, where the deck is meant to be
                    // flying rather than landing.
                    float approach = Mathf.Clamp(settings.approachLength, 1f, Mathf.Max(2f, Length * 0.45f));
                    if (s.Distance < approach || Length - s.Distance < approach) continue;
                    over = settings.fixedDatum;
                }
                else
                {
                    if (s.GroundSurface - s.GroundFloor < 0.5f) continue;
                    over = s.GroundSurface;
                }

                float clearance = s.Position.y - settings.deckThickness - over;
                if (clearance >= worst) continue;
                worst = clearance;
                section = i;
            }
            return section < 0 ? 0f : worst;
        }

        /// <summary>Position and frame at a distance along the bridge. Clamped at both ends.</summary>
        public BridgeSample SampleAt(float distance)
        {
            int n = Samples.Count;
            if (n == 0) return new BridgeSample();
            if (n == 1) return Samples[0];
            if (distance <= 0f) return Samples[0];
            if (distance >= Samples[n - 1].Distance) return Samples[n - 1];

            int i = 0;
            while (i < n - 1 && Samples[i + 1].Distance <= distance) i++;

            int j = Mathf.Min(i + 1, n - 1);
            float from = Samples[i].Distance;
            float span = Samples[j].Distance - from;
            float f = span > 1e-6f ? Mathf.Clamp01((distance - from) / span) : 0f;

            BridgeSample a = Samples[i];
            BridgeSample b = Samples[j];

            var result = new BridgeSample
            {
                Position = Vector3.Lerp(a.Position, b.Position, f),
                Tangent = Vector3.Lerp(a.Tangent, b.Tangent, f).normalized,
                HalfWidth = Mathf.Lerp(a.HalfWidth, b.HalfWidth, f),
                WallScale = Mathf.Lerp(a.WallScale, b.WallScale, f),
                Bank = Mathf.Lerp(a.Bank, b.Bank, f),
                AuthoredBank = Mathf.Lerp(a.AuthoredBank, b.AuthoredBank, f),
                HeightOffset = Mathf.Lerp(a.HeightOffset, b.HeightOffset, f),
                Curvature = Mathf.Lerp(a.Curvature, b.Curvature, f),
                HasGround = a.HasGround && b.HasGround,
                GroundSurface = Mathf.Lerp(a.GroundSurface, b.GroundSurface, f),
                GroundFloor = Mathf.Lerp(a.GroundFloor, b.GroundFloor, f),
                Distance = distance
            };
            result.Up = BridgeMath.OrthoNormal(Vector3.Lerp(a.Up, b.Up, f), result.Tangent);
            if (result.Up == Vector3.zero) result.Up = a.Up;
            result.Right = Vector3.Cross(result.Up, result.Tangent).normalized;
            return result;
        }

        // ---------------------------------------------------------------- helpers

        public static int Next(int i, int n) { return i + 1 < n ? i + 1 : i; }
        public static int Prev(int i, int n) { return i - 1 >= 0 ? i - 1 : i; }

        /// <summary>
        /// Walks a span once to get its length, how far it turns in total, and how fast it turns at
        /// its sharpest point — the last expressed as degrees per unit of the span's parameter, so
        /// it can be divided straight into the per-section allowance.
        ///
        /// Measured on the actual curve rather than on the control polygon, because the two disagree
        /// most precisely where the curve is bending hardest, which is the only place it matters.
        /// </summary>
        static void MeasureSpan(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Knots k,
                                out float length, out float turnDegrees, out float peakTurnRate)
        {
            const int probes = 24;

            length = 0f;
            turnDegrees = 0f;
            float peakStep = 0f;

            Vector3 previous = p1;
            Vector3 lastDirection = Vector3.zero;
            bool hasDirection = false;

            for (int i = 1; i <= probes; i++)
            {
                Vector3 current = Spline(p0, p1, p2, p3, k, (float)i / probes);
                Vector3 step = current - previous;
                float d = step.magnitude;
                length += d;

                if (d > 1e-5f)
                {
                    Vector3 direction = step / d;
                    if (hasDirection)
                    {
                        float turn = Vector3.Angle(lastDirection, direction);
                        turnDegrees += turn;
                        peakStep = Mathf.Max(peakStep, turn);
                    }
                    lastDirection = direction;
                    hasDirection = true;
                }

                previous = current;
            }

            // One probe covers 1/probes of the span, so scaling up gives the turn rate per whole
            // span at the sharpest point.
            peakTurnRate = peakStep * probes;
        }

        /// <summary>
        /// Knot times for one Catmull-Rom span, spaced by chord length raised to
        /// <paramref name="alpha"/>.
        ///
        /// Centripetal spacing (alpha 0.5) is provably free of cusps and self-intersections whatever
        /// the node spacing, which is exactly the guarantee wanted from something a person is
        /// dragging nodes around in.
        /// </summary>
        struct Knots
        {
            public float T0, T1, T2, T3;

            public static Knots For(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float alpha)
            {
                var k = new Knots();
                k.T0 = 0f;
                k.T1 = k.T0 + Step(p0, p1, alpha);
                k.T2 = k.T1 + Step(p1, p2, alpha);
                k.T3 = k.T2 + Step(p2, p3, alpha);
                return k;
            }

            // Coincident nodes would give a zero-length span and divide by zero downstream.
            static float Step(Vector3 a, Vector3 b, float alpha)
            {
                float d = Vector3.Distance(a, b);
                return Mathf.Max(Mathf.Pow(d, alpha), 1e-4f);
            }
        }

        /// <summary>Barry-Goldman evaluation of the span between p1 and p2.</summary>
        static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Knots k, float t)
        {
            float tt = Mathf.Lerp(k.T1, k.T2, t);

            Vector3 a1 = ((k.T1 - tt) * p0 + (tt - k.T0) * p1) / (k.T1 - k.T0);
            Vector3 a2 = ((k.T2 - tt) * p1 + (tt - k.T1) * p2) / (k.T2 - k.T1);
            Vector3 a3 = ((k.T3 - tt) * p2 + (tt - k.T2) * p3) / (k.T3 - k.T2);

            Vector3 b1 = ((k.T2 - tt) * a1 + (tt - k.T0) * a2) / (k.T2 - k.T0);
            Vector3 b2 = ((k.T3 - tt) * a2 + (tt - k.T1) * a3) / (k.T3 - k.T1);

            return ((k.T2 - tt) * b1 + (tt - k.T1) * b2) / (k.T2 - k.T1);
        }

        /// <summary>
        /// The same evaluation for a scalar, driven by the knots taken from the positions so widths
        /// and banks stay in step with the shape rather than drifting against it.
        /// </summary>
        static float Spline(float p0, float p1, float p2, float p3, Knots k, float t)
        {
            float tt = Mathf.Lerp(k.T1, k.T2, t);

            float a1 = ((k.T1 - tt) * p0 + (tt - k.T0) * p1) / (k.T1 - k.T0);
            float a2 = ((k.T2 - tt) * p1 + (tt - k.T1) * p2) / (k.T2 - k.T1);
            float a3 = ((k.T3 - tt) * p2 + (tt - k.T2) * p3) / (k.T3 - k.T2);

            float b1 = ((k.T2 - tt) * a1 + (tt - k.T0) * a2) / (k.T2 - k.T0);
            float b2 = ((k.T3 - tt) * a2 + (tt - k.T1) * a3) / (k.T3 - k.T1);

            return ((k.T2 - tt) * b1 + (tt - k.T1) * b2) / (k.T2 - k.T1);
        }

        /// <summary>
        /// Rebuilds the node list as <paramref name="count"/> points spread evenly by distance along
        /// the current curve, preserving the shape. Uneven node spacing is the main cause of a
        /// corner that bulges or flicks, so this is the first thing to reach for when a bend looks
        /// wrong.
        /// </summary>
        public static List<BridgeNode> Redistribute(IList<BridgeNode> nodes, BridgeSettings settings,
                                                    IBridgeGround ground, Matrix4x4 toWorld,
                                                    Matrix4x4 toLocal, int count)
        {
            var result = new List<BridgeNode>();
            if (nodes == null || nodes.Count < 2) return result;

            count = Mathf.Max(2, count);

            BridgePath path = Build(nodes, settings, ground, toWorld, toLocal);
            if (path.Samples.Count < 2 || path.Length < 1e-4f) return result;

            for (int i = 0; i < count; i++)
            {
                BridgeSample s = path.SampleAt(path.Length * i / (count - 1));
                result.Add(new BridgeNode
                {
                    position = s.Position,
                    width = s.HalfWidth * 2f,
                    wallScale = s.WallScale,
                    heightOffset = s.HeightOffset,
                    // Only the authored part of the bank belongs on a node. Copying the total back
                    // would bake the automatic banking in, and the next rebuild would bank it again.
                    bank = s.AuthoredBank
                });
            }

            // Resampling lands close to the originals but not exactly on them, and the two ends of
            // a bridge are the whole point of where it was put.
            result[0] = nodes[0].Clone();
            result[result.Count - 1] = nodes[nodes.Count - 1].Clone();
            return result;
        }
    }
}
