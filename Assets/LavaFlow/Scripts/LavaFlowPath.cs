using System.Collections.Generic;
using UnityEngine;

namespace LavaFlow
{
    /// <summary>
    /// Anything that can answer "where is the ground under here, and which way does it face".
    /// Kept as an interface so the path solver never touches the scene: the generator passes a
    /// terrain or a raycast sampler, a test passes an analytic hillside.
    /// </summary>
    public interface IGroundSampler
    {
        /// <summary>Ground point and unit normal under <paramref name="worldPos"/>, in world space.
        /// Returns false when there is nothing there to stand on.</summary>
        bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal);
    }

    /// <summary>A flat plane at a fixed height. What the flow falls back to with no terrain.</summary>
    public sealed class FlatGround : IGroundSampler
    {
        readonly float _y;
        public FlatGround(float y) { _y = y; }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            point = new Vector3(worldPos.x, _y, worldPos.z);
            normal = Vector3.up;
            return true;
        }
    }

    /// <summary>One cross-section of the flow. All vectors are in the generator's local space.</summary>
    public struct FlowStation
    {
        /// <summary>Centre of the channel, sitting on the draped ground.</summary>
        public Vector3 Center;

        /// <summary>Unit vector downstream.</summary>
        public Vector3 Forward;

        /// <summary>Unit vector across the channel, left to right looking downstream.</summary>
        public Vector3 Right;

        /// <summary>Ground normal at the centre.</summary>
        public Vector3 Up;

        /// <summary>Metres travelled from the source.</summary>
        public float Distance;

        /// <summary>0 at the source, 1 at the toe.</summary>
        public float T;

        /// <summary>How steep the ground is here: 0 on the flat, 1 at or past <c>steepAngle</c>.</summary>
        public float SlopeNorm;

        /// <summary>Half the channel's width here, in metres.</summary>
        public float HalfWidth;
    }

    /// <summary>
    /// The route the lava takes, resolved and draped onto the ground, plus the ground it was draped
    /// onto sampled across the full width. The mesh builder works entirely from this, which is what
    /// lets it stay pure maths with no scene access.
    /// </summary>
    public sealed class FlowPath
    {
        public FlowStation[] Stations;

        /// <summary>Ground point per [station, lateral sample], local space, already lifted by
        /// <c>surfaceOffset</c>.</summary>
        public Vector3[,] Ground;

        /// <summary>Ground normal per [station, lateral sample], local space.</summary>
        public Vector3[,] Normal;

        /// <summary>Number of lateral samples per station: <c>lateralSegments + 1</c>.</summary>
        public int Lateral;

        /// <summary>Total length in metres.</summary>
        public float Length;

        /// <summary>
        /// True when this route carries on from another flow rather than starting fresh. The head
        /// then keeps the width it was handed and skips the narrowing and the molten patch that a
        /// real source has, both of which would read as a seam at every join.
        /// </summary>
        public bool ContinuesUpstream;

        public int Count { get { return Stations != null ? Stations.Length : 0; } }

        public bool IsValid { get { return Count >= 2 && Lateral >= 3; } }
    }

    /// <summary>
    /// Turns a start point (or a set of authored points) into a draped, framed centreline with a
    /// width that responds to the slope: narrow and fast down the steep ground, broad and slow once
    /// it reaches the flat. That single rule is what makes one component produce both the cascade
    /// and the river at the bottom of it.
    /// </summary>
    public static class LavaFlowPathSolver
    {
        /// <summary>
        /// Ceiling on how many cross-sections an authored route may produce. Not a length limit —
        /// it is there so a runaway spline cannot try to build a million-triangle mesh — and at the
        /// default spacing it is several kilometres of river.
        /// </summary>
        public const int MaxAuthoredStations = 2500;

        /// <summary>
        /// Solves the route.
        /// </summary>
        /// <param name="s">Shape settings.</param>
        /// <param name="ground">Ground under the flow. Null falls back to a flat plane through the origin.</param>
        /// <param name="origin">World-space source of the flow, normally the generator's position.</param>
        /// <param name="startHeading">World-space direction the lava is thrown in, used until the
        /// slope takes over. Normally the generator's forward.</param>
        /// <param name="worldToLocal">Matrix taking the result back into the generator's space.</param>
        /// <param name="controlPointsWorld">Authored route for the non-downhill modes.</param>
        /// <param name="entryHalfWidth">Half width the lava arrives at, in metres, when this flow
        /// continues another one. Zero or less means it starts at its own source.</param>
        public static FlowPath Solve(LavaFlowSettings s, IGroundSampler ground, Vector3 origin,
                                     Vector3 startHeading, Matrix4x4 worldToLocal,
                                     IList<Vector3> controlPointsWorld, float entryHalfWidth = 0f)
        {
            s = s ?? new LavaFlowSettings();
            if (ground == null) ground = new FlatGround(origin.y);

            float spacing = Mathf.Max(0.2f, s.stationSpacing);
            bool authored = s.pathMode != FlowPathMode.Downhill;

            // Max Length is a budget for the downhill walk: it decides how far lava released at the
            // source is allowed to run. It must never cut an authored route short. Someone who has
            // drawn a river across the map has already said exactly how long it is, and silently
            // dropping everything past a default 220 m makes it look as though the later points
            // were never placed.
            float lengthLimit = authored ? spacing * MaxAuthoredStations : s.maxLength;

            List<Vector3> centers = authored
                ? ResampleControlPoints(controlPointsWorld, spacing, lengthLimit)
                : WalkDownhill(s, ground, origin, startHeading, spacing);

            if (centers.Count < 2) return new FlowPath { Stations = new FlowStation[0], Lateral = 0 };

            Drape(centers, ground, s.groundFollow);
            SmoothPositions(centers, 2);

            var path = new FlowPath();
            path.ContinuesUpstream = entryHalfWidth > 0f;
            path.Stations = BuildStations(s, ground, centers);

            // Meander is for a route nobody drew: it stops an automatic river reading as a canal.
            // On an authored route it fights the person drawing it, swinging the channel off the
            // line they put it on, which looks like the tool wandering off on its own.
            if (!authored) ApplyMeander(s, ground, path.Stations);

            // Widths first without the curvature clamp, so the corners are opened out to fit the
            // width the river actually wants rather than to fit the width a sharp corner had
            // already forced it down to. Then the frames are rebuilt on the eased centreline and
            // the widths redone, this time with the clamp left in as a safety net.
            AssignWidths(s, path.Stations, entryHalfWidth, false);
            if (RoundTightCorners(path.Stations))
                RebuildFrames(s, ground, path.Stations);

            AssignWidths(s, path.Stations, entryHalfWidth, true);

            SampleGrid(s, ground, path, worldToLocal);

            for (int i = 0; i < path.Stations.Length; i++)
            {
                FlowStation st = path.Stations[i];
                st.Center = worldToLocal.MultiplyPoint3x4(st.Center);
                st.Forward = worldToLocal.MultiplyVector(st.Forward).normalized;
                st.Right = worldToLocal.MultiplyVector(st.Right).normalized;
                st.Up = worldToLocal.MultiplyVector(st.Up).normalized;
                path.Stations[i] = st;
            }

            path.Length = path.Stations[path.Stations.Length - 1].Distance;
            return path;
        }

        // ------------------------------------------------------------------ route

        /// <summary>
        /// Releases lava at the source and lets the terrain steer it. Each step turns toward the
        /// steepest way down, but only partly: real lava carries its momentum through the bends
        /// instead of snapping into every rut it passes.
        /// </summary>
        static List<Vector3> WalkDownhill(LavaFlowSettings s, IGroundSampler ground, Vector3 origin,
                                          Vector3 startHeading, float spacing)
        {
            var pts = new List<Vector3>();
            var rng = new Rng(s.seed ^ 0x1B9C3);

            Vector3 pos;
            Vector3 n;
            if (!ground.Sample(origin, out pos, out n)) { pos = origin; n = Vector3.up; }

            float flatGrade = Mathf.Max(0.01f, Mathf.Tan(s.flatSlopeAngle * Mathf.Deg2Rad));

            // A flow feels the ground at its own scale. Reading the slope from one point would let
            // any bump narrower than the channel steer the whole thing, so the descent direction is
            // measured across a disc about as wide as the lava is.
            float probe = Mathf.Max(spacing * 1.5f, s.cascadeWidth);

            Vector3 dir = Flatten(startHeading);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;

            // Only take the heading from the ground if the ground is actually leaning somewhere.
            float grade0;
            Vector3 descent0 = ProbeDescent(ground, pos, probe, out grade0);
            if (grade0 > flatGrade && descent0.sqrMagnitude > 1e-6f) dir = descent0;

            pts.Add(pos);

            float travelled = 0f;
            float flatRun = -1f;             // metres run since the ground went flat
            float flatLimit = Mathf.Max(0f, s.riverRunLength);
            float momentum = Mathf.Clamp01(s.momentum);
            bool hasCascaded = false;        // has the flow found real fall yet
            int guard = Mathf.CeilToInt(s.maxLength / spacing) * 3 + 16;

            for (int step = 0; step < guard && travelled < s.maxLength; step++)
            {
                // How hard the ground pulls depends on how steep it is. Level ground's gradient is
                // mostly noise, and a flow that chased it would spend its whole length circling the
                // nearest dip instead of getting to the edge of the cliff.
                float grade;
                Vector3 descent = ProbeDescent(ground, pos, probe, out grade);
                float pull = Mathf.Clamp01(grade / flatGrade);
                if (descent.sqrMagnitude > 1e-6f)
                    dir = Vector3.Lerp(dir, descent, (1f - momentum) * pull);

                // Wander is sampled from position rather than from the step counter, so the same
                // ground always bends the flow the same way.
                if (s.wander > 0f)
                {
                    float w = FlowNoise.Signed(pos.x * 0.035f, pos.z * 0.035f, s.seed + 61);
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                    dir += side * (w * s.wander * 0.7f);
                }

                dir = Flatten(dir);
                if (dir.sqrMagnitude < 1e-6f) break;
                dir.Normalize();

                Vector3 next = pos + dir * spacing;
                Vector3 nextPoint, nextNormal;
                if (!ground.Sample(next, out nextPoint, out nextNormal))
                {
                    nextPoint = next;
                    nextNormal = Vector3.up;
                }

                float drop = pos.y - nextPoint.y;
                float slopeAngle = Mathf.Atan2(Mathf.Max(0f, drop), spacing) * Mathf.Rad2Deg;

                // Uphill means the route has run into a wall. Nudge sideways rather than climbing
                // it, which is what lava does when it ponds against an obstacle.
                if (drop < -0.4f * spacing)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                    dir = (dir + side * (rng.Value() < 0.5f ? -0.8f : 0.8f)).normalized;
                    continue;
                }

                pos = nextPoint;
                n = nextNormal;
                travelled += spacing;
                pts.Add(pos);

                if (slopeAngle >= s.flatSlopeAngle) hasCascaded = true;

                // Once the ground stops falling away the cascade is over and the rest of the length
                // is the river along the base. Nothing counts as the river until the flow has found
                // some fall first: released on a plateau above a cliff, it has to be allowed to
                // reach the edge rather than stopping in the middle of the flat.
                if (hasCascaded)
                {
                    if (slopeAngle < s.flatSlopeAngle)
                    {
                        if (flatRun < 0f) flatRun = 0f;
                        flatRun += spacing;
                    }
                    else if (flatRun >= 0f)
                    {
                        // A short steepening partway along the river does not end it.
                        flatRun += spacing * 0.5f;
                    }

                    if (flatRun >= flatLimit) break;
                }
            }

            return pts;
        }

        /// <summary>
        /// Horizontal direction of steepest descent, measured by sampling a ring of points around
        /// <paramref name="pos"/> rather than by reading the normal where it stands. Returns the
        /// gradient it found as a rise over run in <paramref name="grade"/>.
        /// </summary>
        static Vector3 ProbeDescent(IGroundSampler ground, Vector3 pos, float radius, out float grade)
        {
            const int Samples = 8;

            grade = 0f;
            Vector3 here, hereNormal;
            if (!ground.Sample(pos, out here, out hereNormal)) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int hits = 0;

            for (int k = 0; k < Samples; k++)
            {
                float ang = k / (float)Samples * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

                Vector3 p, nrm;
                if (!ground.Sample(pos + dir * radius, out p, out nrm)) continue;

                sum += dir * (here.y - p.y);
                hits++;
            }

            if (hits == 0 || sum.sqrMagnitude < 1e-10f)
            {
                // Nothing to go on from the ring: fall back to the normal where we are standing.
                Vector3 d = new Vector3(-hereNormal.x, 0f, -hereNormal.z);
                grade = Mathf.Sqrt(hereNormal.x * hereNormal.x + hereNormal.z * hereNormal.z);
                return d.sqrMagnitude < 1e-8f ? Vector3.zero : d.normalized;
            }

            // On a plane of gradient m the ring sums to radius * (samples / 2) * m, so dividing it
            // back out recovers the slope as a rise over run.
            grade = sum.magnitude / (radius * (Samples * 0.5f));
            return sum.normalized;
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>
        /// Runs a Catmull-Rom through the authored points and lays a station down every
        /// <paramref name="spacing"/> metres of it.
        ///
        /// Both halves of that matter. The curve is sampled at a fixed number of steps per metre
        /// rather than per leg, because points clicked two hundred metres apart sampled in a fixed
        /// two dozen steps give a coarse polyline with a visible corner at every one. And stations
        /// are then placed by interpolating to the exact arc length rather than by taking whichever
        /// sample happened to land past it, because rounding each one up to the next sample is what
        /// turns a river drawn with wide clicks into flat slabs metres long: the crust plates are
        /// smaller than one quad, so the cracks between them have nowhere to exist.
        /// </summary>
        static List<Vector3> ResampleControlPoints(IList<Vector3> control, float spacing, float maxLength)
        {
            var pts = new List<Vector3>();
            if (control == null || control.Count < 2) return pts;

            // Duplicate the ends so the spline actually reaches the first and last point.
            var cps = new List<Vector3>(control.Count + 2);
            cps.Add(control[0] + (control[0] - control[1]));
            for (int i = 0; i < control.Count; i++) cps.Add(control[i]);
            cps.Add(control[control.Count - 1] + (control[control.Count - 1] - control[control.Count - 2]));

            // A fine polyline first, stepped in metres so a long leg is not sampled coarsely.
            var fine = new List<Vector3>();
            fine.Add(cps[1]);

            float step = Mathf.Max(0.05f, spacing * 0.25f);
            int segments = cps.Count - 3;

            for (int seg = 0; seg < segments; seg++)
            {
                float chord = Vector3.Distance(cps[seg + 1], cps[seg + 2]);
                int sub = Mathf.Clamp(Mathf.CeilToInt(chord / step), 8, 8192);

                for (int k = 1; k <= sub; k++)
                {
                    fine.Add(CatmullRom(cps[seg], cps[seg + 1], cps[seg + 2], cps[seg + 3],
                                        k / (float)sub));
                }
            }

            // Then walk it, landing exactly on each multiple of the spacing.
            pts.Add(fine[0]);

            float carried = 0f;
            float total = 0f;

            for (int i = 1; i < fine.Count; i++)
            {
                Vector3 a = fine[i - 1];
                Vector3 b = fine[i];

                float legLength = Vector3.Distance(a, b);
                if (legLength < 1e-6f) continue;

                float travelled = 0f;
                float toNext = spacing - carried;

                while (travelled + toNext <= legLength)
                {
                    travelled += toNext;
                    pts.Add(Vector3.Lerp(a, b, travelled / legLength));

                    total += spacing;
                    if (total >= maxLength) return pts;

                    toNext = spacing;
                    carried = 0f;
                }

                carried += legLength - travelled;
            }

            Vector3 last = fine[fine.Count - 1];
            if (Vector3.Distance(pts[pts.Count - 1], last) > spacing * 0.4f) pts.Add(last);
            return pts;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------------ draping

        static void Drape(List<Vector3> pts, IGroundSampler ground, float follow)
        {
            follow = Mathf.Clamp01(follow);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p, n;
                if (!ground.Sample(pts[i], out p, out n)) continue;
                pts[i] = Vector3.Lerp(pts[i], p, follow);
            }
        }

        /// <summary>Moving average over the centreline. Lava has enough surface tension and enough
        /// mass that it does not reproduce every pixel of the heightmap.</summary>
        static void SmoothPositions(List<Vector3> pts, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i < pts.Count - 1; i++)
                    pts[i] = (pts[i - 1] + pts[i] * 2f + pts[i + 1]) * 0.25f;
            }
        }

        // ------------------------------------------------------------------ frames

        static FlowStation[] BuildStations(LavaFlowSettings s, IGroundSampler ground, List<Vector3> centers)
        {
            int n = centers.Count;
            var stations = new FlowStation[n];
            float steep = Mathf.Max(1f, s.steepAngle);

            float distance = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) distance += Vector3.Distance(centers[i - 1], centers[i]);

                Vector3 forward = Tangent(centers, i);
                Vector3 up;
                Vector3 hit;
                if (!ground.Sample(centers[i], out hit, out up)) up = Vector3.up;

                Vector3 right = Vector3.Cross(up, forward);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                right.Normalize();

                // Re-square the frame so forward, right and up stay orthogonal on steep ground.
                up = Vector3.Cross(forward, right).normalized;
                if (up.y < 0f) up = -up;

                float slopeAngle = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;

                stations[i] = new FlowStation
                {
                    Center = centers[i],
                    Forward = forward,
                    Right = right,
                    Up = up,
                    Distance = distance,
                    SlopeNorm = Mathf.Clamp01(slopeAngle / steep)
                };
            }

            // Slope drives width, and width that jitters station to station looks like a mistake.
            SmoothSlope(stations, 3);

            float total = Mathf.Max(1e-4f, stations[n - 1].Distance);
            for (int i = 0; i < n; i++) stations[i].T = stations[i].Distance / total;
            return stations;
        }

        static Vector3 Tangent(List<Vector3> pts, int i)
        {
            Vector3 t;
            if (i == 0) t = pts[1] - pts[0];
            else if (i == pts.Count - 1) t = pts[i] - pts[i - 1];
            else t = pts[i + 1] - pts[i - 1];

            if (t.sqrMagnitude < 1e-8f) t = Vector3.forward;
            return t.normalized;
        }

        static void SmoothSlope(FlowStation[] stations, int passes)
        {
            int n = stations.Length;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i < n - 1; i++)
                {
                    stations[i].SlopeNorm = (stations[i - 1].SlopeNorm +
                                             stations[i].SlopeNorm * 2f +
                                             stations[i + 1].SlopeNorm) * 0.25f;
                }
            }
        }

        /// <summary>
        /// Swings the channel from side to side on the slow stretches. A steep cascade runs straight
        /// because gravity beats everything else; a river on the flat wanders.
        /// </summary>
        static void ApplyMeander(LavaFlowSettings s, IGroundSampler ground, FlowStation[] stations)
        {
            if (s.meander <= 0.001f) return;
            float wavelength = Mathf.Max(1f, s.meanderLength);
            float follow = Mathf.Clamp01(s.groundFollow);

            for (int i = 0; i < stations.Length; i++)
            {
                float flatness = 1f - stations[i].SlopeNorm;
                float phase = stations[i].Distance / wavelength * Mathf.PI * 2f;
                float wobble = Mathf.Sin(phase) * 0.7f + FlowNoise.Signed(stations[i].Distance * 0.05f, 3.1f, s.seed + 17) * 0.3f;
                float offset = wobble * s.meander * flatness * flatness;

                Vector3 moved = stations[i].Center + stations[i].Right * offset;
                Vector3 p, n;
                if (ground.Sample(moved, out p, out n)) moved = Vector3.Lerp(moved, p, follow);
                stations[i].Center = moved;
            }

            // The centreline moved, so the frames that were built from it are stale.
            for (int i = 0; i < stations.Length; i++)
            {
                Vector3 forward;
                if (i == 0) forward = stations[1].Center - stations[0].Center;
                else if (i == stations.Length - 1) forward = stations[i].Center - stations[i - 1].Center;
                else forward = stations[i + 1].Center - stations[i - 1].Center;
                if (forward.sqrMagnitude < 1e-8f) continue;

                forward.Normalize();
                Vector3 right = Vector3.Cross(stations[i].Up, forward);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-6f) continue;

                stations[i].Forward = forward;
                stations[i].Right = right.normalized;
                stations[i].Up = Vector3.Cross(forward, stations[i].Right).normalized;
                if (stations[i].Up.y < 0f) stations[i].Up = -stations[i].Up;
            }
        }

        /// <summary>
        /// Opens out any corner too sharp for the river to get round, by easing the centreline
        /// rather than by narrowing the channel.
        ///
        /// A drawn route has hard corners in it, because people click points and expect a river,
        /// not a polygon. Clamping the width at those corners does keep the mesh valid, but it
        /// pinches the flow to a thread exactly where the eye is drawn and the river stops looking
        /// like one thing. Rounding the corner to a radius the full width can travel keeps the
        /// river the same size the whole way along, which is what a river does.
        ///
        /// Returns true when it moved anything.
        /// </summary>
        static bool RoundTightCorners(FlowStation[] stations)
        {
            int n = stations.Length;
            if (n < 5) return false;

            // Same margin the width clamp uses, plus a little over, so the clamp afterwards has
            // nothing left to do. Rounding to exactly the clamp's threshold leaves every corner
            // sitting on the boundary, where the smallest change in width trims it again.
            const float Safety = 0.65f;
            const float Margin = 1.2f;
            const int MaxPasses = 120;

            bool movedAnything = false;
            var next = new Vector3[n];

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                for (int i = 0; i < n; i++) next[i] = stations[i].Center;

                bool tight = false;

                for (int i = 1; i < n - 1; i++)
                {
                    float needed = stations[i].HalfWidth / Safety * Margin;
                    if (RadiusAt(stations, i) >= needed) continue;

                    tight = true;
                    // Pull the corner toward the line between its neighbours. Repeated, this is
                    // what turns a hard angle into an arc.
                    Vector3 average = (stations[i - 1].Center + stations[i + 1].Center) * 0.5f;
                    next[i] = Vector3.Lerp(stations[i].Center, average, 0.5f);
                }

                if (!tight) break;

                // The ends stay where they were put: a river that walked away from where it was
                // drawn to start would be worse than a sharp corner.
                for (int i = 1; i < n - 1; i++) stations[i].Center = next[i];
                movedAnything = true;
            }

            return movedAnything;
        }

        /// <summary>Radius of the arc the centreline follows through station i.</summary>
        static float RadiusAt(FlowStation[] stations, int i)
        {
            Vector3 back = stations[i].Center - stations[i - 1].Center;
            Vector3 forward = stations[i + 1].Center - stations[i].Center;

            float backLength = back.magnitude;
            float forwardLength = forward.magnitude;
            if (backLength < 1e-4f || forwardLength < 1e-4f) return float.MaxValue;

            float cos = Mathf.Clamp(Vector3.Dot(back / backLength, forward / forwardLength), -1f, 1f);
            float turn = Mathf.Acos(cos);
            if (turn < 1e-4f) return float.MaxValue;

            return (backLength + forwardLength) * 0.5f / turn;
        }

        /// <summary>
        /// Recomputes tangents, side vectors and slope after the centreline has been moved.
        /// </summary>
        static void RebuildFrames(LavaFlowSettings s, IGroundSampler ground, FlowStation[] stations)
        {
            int n = stations.Length;
            float steep = Mathf.Max(1f, s.steepAngle);
            float follow = Mathf.Clamp01(s.groundFollow);
            float distance = 0f;

            for (int i = 0; i < n; i++)
            {
                // Rounding a corner cuts the inside of it, so the route is shorter than it was.
                if (i > 0) distance += Vector3.Distance(stations[i - 1].Center, stations[i].Center);
                stations[i].Distance = distance;

                Vector3 forward;
                if (i == 0) forward = stations[1].Center - stations[0].Center;
                else if (i == n - 1) forward = stations[i].Center - stations[i - 1].Center;
                else forward = stations[i + 1].Center - stations[i - 1].Center;
                if (forward.sqrMagnitude < 1e-8f) continue;
                forward.Normalize();

                Vector3 hit, up;
                if (!ground.Sample(stations[i].Center, out hit, out up)) up = Vector3.up;
                stations[i].Center = Vector3.Lerp(stations[i].Center, hit, follow);

                Vector3 right = Vector3.Cross(up, forward);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-6f) continue;
                right.Normalize();

                up = Vector3.Cross(forward, right).normalized;
                if (up.y < 0f) up = -up;

                stations[i].Forward = forward;
                stations[i].Right = right;
                stations[i].Up = up;
                stations[i].SlopeNorm = Mathf.Clamp01(
                    Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg / steep);
            }

            SmoothSlope(stations, 3);

            float total = Mathf.Max(1e-4f, stations[n - 1].Distance);
            for (int i = 0; i < n; i++) stations[i].T = stations[i].Distance / total;
        }

        // ------------------------------------------------------------------ width

        static void AssignWidths(LavaFlowSettings s, FlowStation[] stations, float entryHalfWidth,
                                 bool limitToCurvature)
        {
            int n = stations.Length;
            float cascade = Mathf.Max(0.2f, s.cascadeWidth) * 0.5f;
            float river = Mathf.Max(0.2f, s.riverWidth) * 0.5f;
            bool continues = entryHalfWidth > 0f;

            for (int i = 0; i < n; i++)
            {
                float w = Mathf.Lerp(river, cascade, stations[i].SlopeNorm);
                float noise = FlowNoise.Signed(stations[i].Distance * 0.06f, 11.7f, s.seed + 401);
                w *= 1f + noise * s.widthVariation;

                // The last few metres are the toe, where the flow has run out of push and piles up.
                float toe = Mathf.Clamp01((stations[i].T - 0.94f) / 0.06f);
                w *= 1f + toe * 0.35f;

                if (continues)
                {
                    // Handed a width by the flow upstream: start there exactly and ease into
                    // whatever this stretch of ground wants, so the join has no step in it.
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stations[i].Distance / 10f));
                    w = Mathf.Lerp(entryHalfWidth, w, blend);
                }
                else
                {
                    // The very first section is still inside whatever fed it.
                    float startTaper = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stations[i].Distance / 4f));
                    w *= Mathf.Lerp(0.72f, 1f, startTaper);
                }

                stations[i].HalfWidth = Mathf.Max(0.15f, w);
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < n - 1; i++)
                {
                    stations[i].HalfWidth = (stations[i - 1].HalfWidth +
                                             stations[i].HalfWidth * 2f +
                                             stations[i + 1].HalfWidth) * 0.25f;
                }
            }

            if (limitToCurvature) LimitWidthToCurvature(stations);
        }

        /// <summary>
        /// Stops the flow turning itself inside out on a bend.
        ///
        /// A ribbon swept round a corner tighter than it is wide folds its inner bank back through
        /// itself: the inner edge doubles over, the normals invert and the mesh renders as a mess of
        /// shards right where the eye is drawn. Real lava does not do this because a flow that wide
        /// cannot turn that sharply. Clamping the width to the radius it is actually going round
        /// gets the same result, and it narrows the flow through tight bends, which is what happens
        /// anyway when lava is forced through a gap.
        /// </summary>
        static void LimitWidthToCurvature(FlowStation[] stations)
        {
            int n = stations.Length;
            if (n < 3) return;

            // The continuous limit is the full radius: at a half width of R the inner bank collapses
            // to a point. Sitting near it is not enough, though, because the ribbon is a row of
            // discrete stations — at 0.9R the inner edge creeps forward only a tenth of a station
            // per step, and draping it onto uneven ground can then push it backwards and turn the
            // quad over. Two thirds keeps the inner edge moving forwards at a third of the pace of
            // the centreline, which survives the terrain.
            const float Safety = 0.65f;

            var limit = new float[n];
            for (int i = 0; i < n; i++) limit[i] = float.MaxValue;

            for (int i = 1; i < n - 1; i++)
            {
                float radius = RadiusAt(stations, i);
                if (radius == float.MaxValue) continue; // straight here, nothing to limit
                limit[i] = radius * Safety;
            }

            // A bend limits the stations either side of it too, and it has to do so gradually: a
            // width that steps from full to pinched between two stations puts a near-vertical wall
            // down the outside of the channel, which reads as a broken mesh even though nothing has
            // actually folded. Widening at most a third of a metre per metre travelled keeps the
            // approach to a bend a taper rather than a step.
            const float Flare = 0.35f;

            for (int i = 1; i < n; i++)
            {
                float run = Vector3.Distance(stations[i - 1].Center, stations[i].Center);
                limit[i] = Mathf.Min(limit[i], limit[i - 1] + Flare * run);
            }

            for (int i = n - 2; i >= 0; i--)
            {
                float run = Vector3.Distance(stations[i].Center, stations[i + 1].Center);
                limit[i] = Mathf.Min(limit[i], limit[i + 1] + Flare * run);
            }

            // Clamp and smooth in turn: smoothing alone would lift the pinched stations back over
            // the limit, and clamping alone leaves the corners of the taper sharp.
            for (int pass = 0; pass < 4; pass++)
            {
                Clamp(stations, limit);

                for (int i = 1; i < n - 1; i++)
                {
                    stations[i].HalfWidth = (stations[i - 1].HalfWidth +
                                             stations[i].HalfWidth * 2f +
                                             stations[i + 1].HalfWidth) * 0.25f;
                }
            }

            Clamp(stations, limit);
        }

        static void Clamp(FlowStation[] stations, float[] limit)
        {
            for (int i = 0; i < stations.Length; i++)
                stations[i].HalfWidth = Mathf.Max(0.15f, Mathf.Min(stations[i].HalfWidth, limit[i]));
        }

        // ------------------------------------------------------------------ ground grid

        /// <summary>
        /// Samples the ground under every point of the ribbon, not just under the centreline. On a
        /// slope as steep as a cliff face the two differ by metres, and a cross-section built from
        /// the centreline alone would bury one bank and leave the other hanging in the air.
        /// </summary>
        static void SampleGrid(LavaFlowSettings s, IGroundSampler ground, FlowPath path, Matrix4x4 worldToLocal)
        {
            int n = path.Stations.Length;

            // Rounded up to an even number of segments, so there is a sample exactly on the
            // centreline and the channel is symmetrical about it.
            int segments = Mathf.Max(4, s.lateralSegments);
            if ((segments & 1) != 0) segments++;
            int lateral = segments + 1;

            path.Lateral = lateral;
            path.Ground = new Vector3[n, lateral];
            path.Normal = new Vector3[n, lateral];

            float follow = Mathf.Clamp01(s.groundFollow);

            for (int i = 0; i < n; i++)
            {
                FlowStation st = path.Stations[i];
                for (int j = 0; j < lateral; j++)
                {
                    float lat = -1f + 2f * j / (lateral - 1);
                    Vector3 flat = st.Center + st.Right * (lat * st.HalfWidth);

                    Vector3 p, nrm;
                    if (!ground.Sample(flat, out p, out nrm)) { p = flat; nrm = st.Up; }

                    Vector3 draped = Vector3.Lerp(flat, p, follow);
                    // Normalised lerp rather than Slerp: these two normals are never far apart, and
                    // Slerp is a native call, which would stop the solver running outside the player.
                    Vector3 normal = Vector3.Lerp(st.Up, nrm, follow * 0.75f).normalized;
                    if (normal.sqrMagnitude < 0.5f) normal = st.Up;

                    path.Ground[i, j] = worldToLocal.MultiplyPoint3x4(draped + normal * s.surfaceOffset);
                    path.Normal[i, j] = worldToLocal.MultiplyVector(normal).normalized;
                }
            }
        }
    }
}
