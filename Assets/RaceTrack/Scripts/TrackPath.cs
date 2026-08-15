using System.Collections.Generic;
using UnityEngine;

namespace RaceTrack
{
    /// <summary>One cross-section's worth of the solved racing line.</summary>
    public struct TrackSample
    {
        /// <summary>Middle of the racing surface, in the generator's local space.</summary>
        public Vector3 Position;

        /// <summary>Direction of travel.</summary>
        public Vector3 Tangent;

        /// <summary>Surface normal after levelling and banking.</summary>
        public Vector3 Up;

        /// <summary>Across the track, towards the right-hand edge. Always <c>Cross(Up, Tangent)</c>.</summary>
        public Vector3 Right;

        /// <summary>Half the width of the racing surface here, kerbs and barriers not included.</summary>
        public float HalfWidth;

        /// <summary>Per-node barrier height multiplier, splined along the path.</summary>
        public float WallScale;

        /// <summary>Total bank actually applied here, in degrees. Positive raises the right edge.</summary>
        public float Bank;

        /// <summary>
        /// The part of <see cref="Bank"/> that came off the nodes rather than out of the corner.
        /// Kept apart so respacing the nodes can hand the authored lean back without also baking in
        /// the automatic banking — which would then be banked a second time on the next rebuild.
        /// </summary>
        public float AuthoredBank;

        /// <summary>Signed curvature in 1/m — positive turns right. Its reciprocal is the corner radius.</summary>
        public float Curvature;

        /// <summary>Distance from the first sample, along the track, in metres.</summary>
        public float Distance;
    }

    /// <summary>
    /// Turns a list of nodes into the solved racing line: positions, frames, banking and widths,
    /// sampled densely enough that a corner reads as a curve.
    ///
    /// Three decisions here are the ones that make the surface drivable, and all three are worth
    /// understanding before touching anything:
    ///
    /// <b>The frame is parallel-transported, then levelled.</b> Transport alone never spins the
    /// section as the path climbs and turns, so the track carries no twist nobody asked for — but
    /// transport has no opinion about which way is down, and a ribbon that slowly rolls over is
    /// useless to race on. So the transported frame is then rotated towards level by
    /// <see cref="TrackSettings.keepLevel"/>. At 1 the surface is dead level side to side however it
    /// climbs, which is what driving wants. Below 1 the ribbon keeps some of its own twist, which is
    /// what a corkscrew or a full vertical loop is made of. The levelling fades itself out where the
    /// track goes near-vertical, because "level" has no meaning there and forcing it would snap the
    /// section round.
    ///
    /// <b>A closed loop is closed in the frame as well as in position.</b> Transporting a frame all
    /// the way round a closed curve does not generally bring it back to where it started — the
    /// residual twist is real geometry, not floating-point error — so the seam would meet at an
    /// angle and show as a step across the road. The residual is measured at the seam and unwound
    /// evenly around the lap, which spreads a couple of degrees over a kilometre and closes the
    /// surface exactly.
    ///
    /// <b>Banking is smoothed over distance, not applied per section.</b> Corner curvature off a
    /// Catmull-Rom spline is twitchy, and feeding it straight into the bank angle ripples the
    /// surface. Averaging it over <see cref="TrackSettings.bankSmoothing"/> metres is what turns it
    /// into a long lean into the corner and back out again.
    /// </summary>
    public class TrackPath
    {
        /// <summary>The solved cross-sections, in order. On a closed loop the last one does
        /// <em>not</em> repeat the first — the seam is the gap between them.</summary>
        public readonly List<TrackSample> Samples = new List<TrackSample>();

        /// <summary>Length of the racing line in metres. Includes the closing segment on a loop.</summary>
        public float Length;

        /// <summary>True when the last section joins back to the first.</summary>
        public bool Closed;

        const float Gravity = 9.81f;

        // ------------------------------------------------------------------ build

        /// <summary>Solves the path. Returns an empty path when there is nothing to sweep.</summary>
        public static TrackPath Build(IList<TrackNode> nodes, TrackSettings settings)
        {
            var path = new TrackPath();
            if (nodes == null || settings == null || nodes.Count < 2) return path;

            // Two nodes cannot describe a loop — there is no way round. Fall back rather than
            // producing a degenerate ribbon that folds onto itself.
            path.Closed = settings.closedLoop && nodes.Count >= 3;

            SamplePositions(nodes, settings, path);
            int n = path.Samples.Count;
            if (n < 2) { path.Samples.Clear(); return path; }

            var segLen = new float[n];
            MeasureDistances(path, segLen);
            if (path.Length < 1e-4f) { path.Samples.Clear(); return path; }

            var tangents = new Vector3[n];
            BuildTangents(path, tangents);

            var refUp = new Vector3[n];
            BuildTransportedFrame(path, tangents, refUp);

            var curvature = new float[n];
            MeasureCurvature(path, tangents, refUp, curvature);

            var bank = new float[n];
            BuildBanking(path, nodes, settings, curvature, segLen, bank);

            ApplyFrames(path, settings, tangents, refUp, curvature, bank);
            return path;
        }

        // --------------------------------------------------------------- sampling

        static void SamplePositions(IList<TrackNode> nodes, TrackSettings settings, TrackPath path)
        {
            int n = nodes.Count;
            bool closed = path.Closed;
            int spans = closed ? n : n - 1;
            float spacing = Mathf.Max(0.05f, settings.sectionSpacing);

            for (int i = 0; i < spans; i++)
            {
                TrackNode b = nodes[i];
                TrackNode c = nodes[closed ? (i + 1) % n : i + 1];

                TrackNode a, d;
                Vector3 p0, p3;

                if (closed)
                {
                    a = nodes[(i - 1 + n) % n];
                    d = nodes[(i + 2) % n];
                    p0 = a.position;
                    p3 = d.position;
                }
                else
                {
                    a = nodes[Mathf.Max(i - 1, 0)];
                    d = nodes[Mathf.Min(i + 2, n - 1)];
                    // Mirror the missing control point at each end so the curve does not kink there.
                    p0 = i == 0 ? b.position + (b.position - c.position) : a.position;
                    p3 = i == n - 2 ? c.position + (c.position - b.position) : d.position;
                }

                Knots knots = Knots.For(p0, b.position, c.position, p3, settings.curveAlpha);

                float length, turnDegrees, peakTurnRate;
                MeasureSpan(p0, b.position, c.position, p3, knots,
                            out length, out turnDegrees, out peakTurnRate);

                // Section density comes from whichever is most demanding: covering the distance,
                // resolving the total bend, or resolving the sharpest part of it.
                //
                // That last one is not redundant. Sections are spread evenly in t, so dividing a
                // span's *total* turn by the allowance only works if the span turns at a steady
                // rate — and through a chicane it does not. A span that turns 40 degrees, nearly all
                // of it in one place, gets ten sections and still steps 10 degrees at the sharp bit,
                // which is a visible flat in the middle of the very corner the setting exists to
                // smooth. Sizing off the peak rate instead holds the promise everywhere.
                float allowance = Mathf.Max(0.1f, settings.degreesPerSection);
                int byLength = Mathf.CeilToInt(length / spacing);
                int byTurn = Mathf.CeilToInt(turnDegrees / allowance);
                int byPeak = Mathf.CeilToInt(peakTurnRate / allowance);
                int steps = Mathf.Clamp(Mathf.Max(byLength, Mathf.Max(byTurn, byPeak)), 1, 2048);

                for (int k = 0; k < steps; k++)
                {
                    // The last sample of a span is the first of the next one, so stop short of t = 1
                    // and let the following span emit it. On a loop the wrap-around span emits the
                    // start again, which is exactly what closes it.
                    float t = (float)k / steps;
                    path.Samples.Add(Interpolate(a, b, c, d, p0, p3, knots, t, settings));
                }
            }

            if (!path.Closed)
            {
                // The loop above stops one short of each span's end, so an open track still needs its
                // final node. It is a node, so nothing to interpolate.
                TrackNode tail = nodes[n - 1];
                path.Samples.Add(new TrackSample
                {
                    Position = tail.position,
                    HalfWidth = HalfWidthOf(tail, settings),
                    WallScale = Mathf.Max(0f, tail.wallScale),
                    AuthoredBank = tail.bank
                });
            }
        }

        static float HalfWidthOf(TrackNode node, TrackSettings settings)
        {
            float full = settings.uniformWidth ? settings.trackWidth : node.width;
            return Mathf.Max(1f, full) * 0.5f;
        }

        static TrackSample Interpolate(TrackNode a, TrackNode b, TrackNode c, TrackNode d,
                                       Vector3 p0, Vector3 p3, Knots k, float t, TrackSettings settings)
        {
            float half;
            if (settings.uniformWidth)
            {
                // Not splined, not interpolated, not derived — literally the same number at every
                // cross-section. This is what makes "the track never narrows" a guarantee rather
                // than a tolerance.
                half = Mathf.Max(1f, settings.trackWidth) * 0.5f;
            }
            else
            {
                // Clamped to the two nodes this span actually runs between, not merely to a positive
                // number. A Catmull-Rom through 20, 20, 34, 34 dips under 19 on the approach before
                // it swells, and a dip is a pinch in the road — the one thing the track must never
                // do. Held inside its own end points, a widening can only ever widen.
                float lo = Mathf.Min(b.width, c.width) * 0.5f;
                float hi = Mathf.Max(b.width, c.width) * 0.5f;
                float splined = Spline(a.width, b.width, c.width, d.width, k, t) * 0.5f;
                half = Mathf.Clamp(splined, Mathf.Max(0.5f, lo), Mathf.Max(0.5f, hi));
            }

            return new TrackSample
            {
                Position = Spline(p0, b.position, c.position, p3, k, t),
                HalfWidth = half,
                WallScale = Mathf.Max(0f, Spline(a.wallScale, b.wallScale, c.wallScale, d.wallScale, k, t)),
                AuthoredBank = Spline(a.bank, b.bank, c.bank, d.bank, k, t)
            };
        }

        // -------------------------------------------------------------- distances

        static void MeasureDistances(TrackPath path, float[] segLen)
        {
            int n = path.Samples.Count;
            float dist = 0f;

            for (int i = 0; i < n; i++)
            {
                TrackSample s = path.Samples[i];
                s.Distance = dist;
                path.Samples[i] = s;

                int next = i + 1;
                float step = next < n
                    ? Vector3.Distance(path.Samples[next].Position, s.Position)
                    : (path.Closed ? Vector3.Distance(path.Samples[0].Position, s.Position) : 0f);

                segLen[i] = step;
                dist += step;
            }

            path.Length = dist;
        }

        // --------------------------------------------------------------- tangents

        static void BuildTangents(TrackPath path, Vector3[] tangents)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 next = path.Samples[Next(i, n, path.Closed)].Position;
                Vector3 prev = path.Samples[Prev(i, n, path.Closed)].Position;

                Vector3 t = next - prev;
                if (t.sqrMagnitude < 1e-12f) t = i > 0 ? tangents[i - 1] : Vector3.forward;
                tangents[i] = t.normalized;
            }
        }

        /// <summary>
        /// Parallel transport of the surface normal along the path, then — on a loop — the residual
        /// twist unwound evenly so the frame arrives back exactly where it set off.
        /// </summary>
        static void BuildTransportedFrame(TrackPath path, Vector3[] tangents, Vector3[] refUp)
        {
            int n = path.Samples.Count;

            Vector3 start = TrackMath.OrthoNormal(Vector3.up, tangents[0]);
            if (start == Vector3.zero)
            {
                // Setting off straight up or straight down: world up says nothing, so pick any
                // perpendicular and let the levelling pass sort it out once the track tips over.
                start = TrackMath.OrthoNormal(Vector3.forward, tangents[0]);
                if (start == Vector3.zero) start = TrackMath.OrthoNormal(Vector3.right, tangents[0]);
            }
            refUp[0] = start;

            for (int i = 1; i < n; i++)
            {
                Vector3 carried = TrackMath.Transport(refUp[i - 1], tangents[i - 1], tangents[i]);
                carried = TrackMath.OrthoNormal(carried, tangents[i]);
                refUp[i] = carried == Vector3.zero ? refUp[i - 1] : carried;
            }

            if (!path.Closed || path.Length < 1e-4f) return;

            Vector3 arriving = TrackMath.OrthoNormal(
                TrackMath.Transport(refUp[n - 1], tangents[n - 1], tangents[0]), tangents[0]);
            if (arriving == Vector3.zero) return;

            float residual = TrackMath.SignedAngle(refUp[0], arriving, tangents[0]);
            if (Mathf.Abs(residual) < 1e-4f) return;

            for (int i = 0; i < n; i++)
            {
                float f = path.Samples[i].Distance / path.Length;
                refUp[i] = TrackMath.OrthoNormal(
                    TrackMath.Rotate(refUp[i], tangents[i], -residual * f), tangents[i]);
            }
        }

        // -------------------------------------------------------------- curvature

        static void MeasureCurvature(TrackPath path, Vector3[] tangents, Vector3[] refUp, float[] curvature)
        {
            int n = path.Samples.Count;

            for (int i = 0; i < n; i++)
            {
                int a = Prev(i, n, path.Closed);
                int b = Next(i, n, path.Closed);

                float span = Vector3.Distance(path.Samples[i].Position, path.Samples[a].Position)
                           + Vector3.Distance(path.Samples[b].Position, path.Samples[i].Position);
                if (span < 1e-5f) { curvature[i] = 0f; continue; }

                Vector3 dT = (tangents[b] - tangents[a]) / span;

                // Measured against the frame's own right, so what counts as "turning right" is what
                // the track thinks is right — which stays correct through a banked or inverted
                // section, where the horizontal projection would not.
                Vector3 right = Vector3.Cross(refUp[i], tangents[i]);
                curvature[i] = Vector3.Dot(dT, right);
            }
        }

        // ---------------------------------------------------------------- banking

        static void BuildBanking(TrackPath path, IList<TrackNode> nodes, TrackSettings settings,
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

            float[] smoothed = SmoothByDistance(auto, segLen, path.Length, path.Closed, settings.bankSmoothing);

            for (int i = 0; i < n; i++)
            {
                // Authored bank is added raw. It is a deliberate instruction about one place, and
                // smearing it over 25 m of track would blunt exactly the thing it was set for.
                bank[i] = smoothed[i] + path.Samples[i].AuthoredBank;
            }
        }

        /// <summary>
        /// Moving average over a window measured in metres of track rather than in samples, so the
        /// result does not change when the section spacing does. Wraps around a closed loop, which
        /// is what keeps the banking continuous across the start line.
        /// </summary>
        static float[] SmoothByDistance(float[] values, float[] segLen, float length, bool closed, float window)
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

                // Backwards. segLen[j] is the gap from j to j+1, so stepping back from i crosses
                // segLen[j] where j is the sample we are stepping onto.
                float walked = 0f;
                int cursor = i;
                while (walked < half)
                {
                    int prev = Prev(cursor, n, closed);
                    if (prev == cursor) break;
                    walked += segLen[prev];
                    if (walked > half) break;
                    sum += values[prev];
                    count++;
                    cursor = prev;
                    if (cursor == i) break; // all the way round a short loop
                }

                walked = 0f;
                cursor = i;
                while (walked < half)
                {
                    int next = Next(cursor, n, closed);
                    if (next == cursor) break;
                    walked += segLen[cursor];
                    if (walked > half) break;
                    sum += values[next];
                    count++;
                    cursor = next;
                    if (cursor == i) break;
                }

                result[i] = sum / count;
            }

            return result;
        }

        // ----------------------------------------------------------------- frames

        static void ApplyFrames(TrackPath path, TrackSettings settings, Vector3[] tangents,
                                Vector3[] refUp, float[] curvature, float[] bank)
        {
            int n = path.Samples.Count;
            float keep = Mathf.Clamp01(settings.keepLevel);

            for (int i = 0; i < n; i++)
            {
                Vector3 t = tangents[i];
                Vector3 up = refUp[i];

                if (keep > 0.001f)
                {
                    Vector3 level = TrackMath.OrthoNormal(Vector3.up, t);
                    // |level| before normalising is how horizontal the track is here. Near-vertical
                    // track has no meaningful "level", so the pull towards it is faded out rather
                    // than allowed to snap the section round.
                    float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - t.y * t.y));
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.20f, horizontal));

                    if (level != Vector3.zero && fade > 0.001f)
                    {
                        float toLevel = TrackMath.SignedAngle(up, level, t);

                        // Upside down, level is 180 degrees away and which way to turn is a coin
                        // toss; rolling the surface at random there would tear the mesh. Inverted
                        // track is deliberate, so it is left alone.
                        float invert = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(180f, 150f, Mathf.Abs(toLevel)));

                        up = TrackMath.OrthoNormal(
                            TrackMath.Rotate(up, t, toLevel * keep * fade * invert), t);
                    }
                }

                if (up == Vector3.zero) up = refUp[i];
                if (Mathf.Abs(bank[i]) > 0.001f) up = TrackMath.OrthoNormal(TrackMath.Rotate(up, t, bank[i]), t);
                if (up == Vector3.zero) up = refUp[i];

                TrackSample s = path.Samples[i];
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
        /// How much room the inside edge of the sweep has left, at its worst point on the track.
        ///
        /// This is the number that answers "does the track pinch anywhere". A swept ribbon keeps its
        /// width by construction, so the only way it can narrow is by folding: turn tighter than the
        /// section is wide and the inner edge sweeps backwards through itself. The measure is
        /// <c>1 - outerHalfWidth / cornerRadius</c> — 1 on a straight, 0 at the exact radius where
        /// the outermost point of the barrier stops advancing, negative once it has folded.
        ///
        /// Anything above about 0.5 is comfortable.
        /// </summary>
        public float WorstEdgeAdvance(TrackSettings settings)
        {
            int ignored;
            return WorstEdgeAdvance(settings, out ignored);
        }

        /// <summary>As <see cref="WorstEdgeAdvance(TrackSettings)"/>, and where it happens.</summary>
        public float WorstEdgeAdvance(TrackSettings settings, out int section)
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
        /// through three nodes. A curve through unevenly spaced nodes bends harder between them than
        /// the node polygon suggests, so a layout whose every node looks legal can still have a
        /// corner the karts cannot hold. Where the two disagree, believe this one — and spread the
        /// nodes out, which is what closes the gap.
        /// </summary>
        public float TightestRadius()
        {
            int ignored;
            return TightestRadius(out ignored);
        }

        /// <summary>
        /// As <see cref="TightestRadius()"/>, and also says which cross-section it happens at, so the
        /// tool can point at the place rather than just quoting a number.
        /// </summary>
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

        /// <summary>Steepest climb or drop anywhere on the track, in degrees from horizontal.</summary>
        public float SteepestGradient()
        {
            float worst = 0f;
            for (int i = 0; i < Samples.Count; i++)
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.Asin(Mathf.Clamp(Samples[i].Tangent.y, -1f, 1f)) * Mathf.Rad2Deg));
            return worst;
        }

        /// <summary>Largest bank angle applied anywhere, in degrees.</summary>
        public float MaxBank()
        {
            float worst = 0f;
            for (int i = 0; i < Samples.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(Samples[i].Bank));
            return worst;
        }

        /// <summary>
        /// Position and frame at a distance along the track, wrapping on a loop. Handy for placing a
        /// start grid, a checkpoint or a respawn without hand-placing anything.
        /// </summary>
        public TrackSample SampleAt(float distance)
        {
            int n = Samples.Count;
            if (n == 0) return new TrackSample();
            if (n == 1) return Samples[0];

            if (Closed && Length > 1e-4f)
            {
                distance -= Mathf.Floor(distance / Length) * Length;
            }
            else
            {
                if (distance <= 0f) return Samples[0];
                if (distance >= Samples[n - 1].Distance) return Samples[n - 1];
            }

            int i = 0;
            while (i < n - 1 && Samples[i + 1].Distance <= distance) i++;

            int j = Next(i, n, Closed);
            float from = Samples[i].Distance;
            float to = j > i ? Samples[j].Distance : Length;
            float span = to - from;
            float f = span > 1e-6f ? Mathf.Clamp01((distance - from) / span) : 0f;

            TrackSample a = Samples[i];
            TrackSample b = Samples[j];

            var result = new TrackSample
            {
                Position = Vector3.Lerp(a.Position, b.Position, f),
                Tangent = Vector3.Lerp(a.Tangent, b.Tangent, f).normalized,
                HalfWidth = Mathf.Lerp(a.HalfWidth, b.HalfWidth, f),
                WallScale = Mathf.Lerp(a.WallScale, b.WallScale, f),
                Bank = Mathf.Lerp(a.Bank, b.Bank, f),
                AuthoredBank = Mathf.Lerp(a.AuthoredBank, b.AuthoredBank, f),
                Curvature = Mathf.Lerp(a.Curvature, b.Curvature, f),
                Distance = distance
            };
            result.Up = TrackMath.OrthoNormal(Vector3.Lerp(a.Up, b.Up, f), result.Tangent);
            if (result.Up == Vector3.zero) result.Up = a.Up;
            result.Right = Vector3.Cross(result.Up, result.Tangent).normalized;
            return result;
        }

        // ---------------------------------------------------------------- helpers

        public static int Next(int i, int n, bool closed)
        {
            if (i + 1 < n) return i + 1;
            return closed ? 0 : i;
        }

        public static int Prev(int i, int n, bool closed)
        {
            if (i - 1 >= 0) return i - 1;
            return closed ? n - 1 : i;
        }

        /// <summary>
        /// Walks a span once to get its length, how far it turns in total, and how fast it turns at
        /// its sharpest point — the last expressed as degrees per unit of the span's parameter, so it
        /// can be divided straight into the per-section allowance.
        ///
        /// Measured on the actual curve rather than on the control polygon, because the two disagree
        /// most precisely where the curve is bending hardest, which is the only place any of this
        /// matters.
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

            // One probe covers 1/probes of the span, so scaling up gives the turn rate per whole span
            // at the sharpest point.
            peakTurnRate = peakStep * probes;
        }

        /// <summary>
        /// Knot times for one Catmull-Rom span, spaced by chord length raised to
        /// <paramref name="alpha"/>.
        ///
        /// Centripetal spacing (alpha 0.5) is provably free of cusps and self-intersections whatever
        /// the node spacing, which is exactly the guarantee wanted from something a person is
        /// dragging nodes around in. The uniform form assumes evenly spaced control points and
        /// overshoots hard when they are not — on a race track that overshoot is a corner that bulges
        /// somewhere nobody put a node.
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
        /// the current curve, preserving the shape. Uneven node spacing is the main cause of a corner
        /// that bulges or flicks, so this is the first thing to reach for when a bend looks wrong.
        /// </summary>
        public static List<TrackNode> Redistribute(IList<TrackNode> nodes, TrackSettings settings, int count)
        {
            var result = new List<TrackNode>();
            if (nodes == null || nodes.Count < 2) return result;

            count = Mathf.Max(MinimumNodes(settings), count);

            TrackPath path = Build(nodes, settings);
            if (path.Samples.Count < 2 || path.Length < 1e-4f) return result;

            // A loop has no last point to land on — sample n points around it and let the wrap close
            // it. An open track has to hit both ends exactly, so it uses n-1 gaps.
            int gaps = path.Closed ? count : count - 1;

            for (int i = 0; i < count; i++)
            {
                TrackSample s = path.SampleAt(path.Length * i / gaps);
                result.Add(new TrackNode
                {
                    position = s.Position,
                    width = s.HalfWidth * 2f,
                    wallScale = s.WallScale,
                    // Only the authored part of the bank belongs on a node. Copying the total back
                    // would bake the automatic banking in, and the next rebuild would bank it again.
                    bank = s.AuthoredBank
                });
            }

            if (!path.Closed)
            {
                // Resampling lands close to the originals but not exactly on them, and the two ends
                // of an open track are usually joined to something.
                result[0] = nodes[0].Clone();
                result[result.Count - 1] = nodes[nodes.Count - 1].Clone();
            }

            return result;
        }

        /// <summary>Fewest nodes a layout can be reduced to and still describe what it is: three for
        /// a loop, since two can only describe a line there and back.</summary>
        static int MinimumNodes(TrackSettings settings)
        {
            return settings != null && settings.closedLoop ? 3 : 2;
        }
    }
}
