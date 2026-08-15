using System.Collections.Generic;
using UnityEngine;

namespace PlayerPath
{
    /// <summary>One cross-section of the path. All vectors are in the generator's local space.</summary>
    public struct PathStation
    {
        /// <summary>Centre of the deck, at the height the player walks at.</summary>
        public Vector3 Center;

        /// <summary>Unit vector along the path, horizontal. The deck may climb, but the frame does
        /// not pitch with it: a built path is level across its width however steep the hill is.</summary>
        public Vector3 Forward;

        /// <summary>Unit vector across the path, left to right looking forwards. Horizontal.</summary>
        public Vector3 Right;

        /// <summary>Metres travelled from the start.</summary>
        public float Distance;

        /// <summary>0 at the start, 1 at the end.</summary>
        public float T;

        /// <summary>How steep the ground is along the route here, in degrees.</summary>
        public float Grade;

        /// <summary>Half the walkable width here, in metres. The walls are built outside this.</summary>
        public float HalfWidth;

        /// <summary>Metres the deck drops at this station, as a step. 0 anywhere the deck ramps.</summary>
        public float Riser;
    }

    /// <summary>
    /// The route the path takes, resolved and draped onto the ground, plus the ground it was draped
    /// onto sampled across the full footprint. The mesh builder works entirely from this, which is
    /// what lets it stay pure maths with no scene access.
    /// </summary>
    public sealed class PathRoute
    {
        public PathStation[] Stations;

        /// <summary>Ground point per [station, column], in local space.</summary>
        public Vector3[,] Ground;

        /// <summary>Metres across the path, signed, for each column of that grid.</summary>
        public float[,] Offset;

        /// <summary>Number of ground columns per station.</summary>
        public int Lateral;

        /// <summary>Total length in metres.</summary>
        public float Length;

        /// <summary>World up, in local space. Everything vertical is measured along this.</summary>
        public Vector3 Up = Vector3.up;

        /// <summary>Per interval i to i+1: true when it is a level tread with a riser at the far
        /// end, false when the deck simply ramps from one station to the next.</summary>
        public bool[] Level;

        public int Count { get { return Stations != null ? Stations.Length : 0; } }

        public bool IsValid { get { return Count >= 2 && Lateral >= 3; } }

        /// <summary>Interval containing <paramref name="distance"/>, clamped into range.</summary>
        public int IntervalAt(float distance)
        {
            int lo = 0;
            int hi = Count - 2;
            if (hi <= 0) return 0;

            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Stations[mid].Distance <= distance) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>
        /// The frame anywhere along the path, not only at a station. The brickwork is laid in its
        /// own units and has no reason to line up with the cross-sections, so it asks for this.
        ///
        /// On a flight of steps the height does not interpolate: the whole interval sits at the
        /// height of the tread it belongs to, so the wall steps with the stairs instead of sliding
        /// down a ramp beside them.
        /// </summary>
        public void Frame(float distance, out Vector3 center, out Vector3 forward, out Vector3 right,
                          out float halfWidth)
        {
            FrameIn(IntervalAt(Mathf.Clamp(distance, 0f, Length)), distance,
                    out center, out forward, out right, out halfWidth);
        }

        /// <summary>
        /// The frame at a distance, read from an interval named outright rather than looked up.
        ///
        /// Anything built in runs — the wall core, the coping, every brick — has to say which
        /// interval it belongs to, because a distance sitting exactly on a station belongs to both
        /// of them and the two answers differ by a whole riser. Looked up, a brick ending on a step
        /// takes its far end from the tread below: the wall then shatters into a shingle of tilted
        /// blocks all the way down every staircase, which is a great deal harder to diagnose from
        /// the picture than it is to prevent here.
        /// </summary>
        public void FrameIn(int interval, float distance, out Vector3 center, out Vector3 forward,
                            out Vector3 right, out float halfWidth)
        {
            int k = Mathf.Clamp(interval, 0, Mathf.Max(0, Count - 2));
            PathStation a = Stations[k];
            PathStation b = Stations[k + 1];

            float span = b.Distance - a.Distance;
            float t = span > 1e-5f ? Mathf.Clamp01((distance - a.Distance) / span) : 0f;

            center = Vector3.Lerp(a.Center, b.Center, t);
            forward = Vector3.Lerp(a.Forward, b.Forward, t);
            forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : a.Forward;
            right = Vector3.Lerp(a.Right, b.Right, t);
            right = right.sqrMagnitude > 1e-8f ? right.normalized : a.Right;
            halfWidth = Mathf.Lerp(a.HalfWidth, b.HalfWidth, t);

            if (Level != null && k < Level.Length && Level[k])
                center += Up * Vector3.Dot(a.Center - center, Up);
        }

        /// <summary>
        /// How far below the deck the ground is, at <paramref name="across"/> metres from the
        /// centreline of station <paramref name="i"/>. Negative where the hillside stands above the
        /// deck, which is exactly what happens on the uphill side of a path cut across a slope.
        /// </summary>
        public float GroundDrop(int i, float across)
        {
            if (Ground == null || Lateral < 2) return 0f;
            i = Mathf.Clamp(i, 0, Count - 1);

            int last = Lateral - 1;
            float lo = Offset[i, 0];
            float hi = Offset[i, last];

            float t = hi - lo > 1e-5f ? Mathf.Clamp01((across - lo) / (hi - lo)) : 0f;
            float f = t * last;
            int j = Mathf.Clamp(Mathf.FloorToInt(f), 0, last - 1);

            Vector3 ground = Vector3.Lerp(Ground[i, j], Ground[i, j + 1], f - j);
            return Vector3.Dot(Stations[i].Center - ground, Up);
        }
    }

    /// <summary>
    /// Turns the points someone clicked along a hillside into a draped, framed centreline with a
    /// deck height profile: ramping where the ground is walkable, breaking into flights of steps
    /// where it is not.
    ///
    /// Two things here matter more than they look. Corners are rounded rather than narrowed, so a
    /// hairpin on a switchback stays the full width of the path instead of pinching to a thread
    /// exactly where the player is looking. And the frame is kept horizontal rather than laid on the
    /// terrain normal, because a path is built: it is level across its width even where the hill it
    /// crosses is not.
    /// </summary>
    public static class PathRouteSolver
    {
        /// <summary>
        /// Ceiling on how many cross-sections a route may produce. Not a length limit — it is there
        /// so a runaway spline cannot try to build a million-triangle mesh — and at the default
        /// spacing it is a couple of kilometres of path.
        /// </summary>
        public const int MaxStations = 3000;

        /// <summary>Narrowest the walkable deck is ever allowed to get, in metres.</summary>
        public const float MinHalfWidth = 0.35f;

        public static PathRoute Solve(PathSettings s, IPathGround ground, Vector3 origin,
                                      Matrix4x4 worldToLocal, IList<Vector3> controlPointsWorld)
        {
            s = s ?? new PathSettings();
            if (ground == null) ground = new FlatPathGround(origin.y);

            float spacing = Mathf.Max(0.15f, s.stationSpacing);

            List<Vector3> centers = ResampleControlPoints(controlPointsWorld, spacing,
                                                          spacing * MaxStations);
            if (centers.Count < 2)
                return new PathRoute { Stations = new PathStation[0], Lateral = 0 };

            Drape(centers, ground);
            SmoothPositions(centers, s.routeSmoothing);

            var route = new PathRoute();
            route.Stations = BuildStations(centers);

            // Everything outside the walkable deck still has to get round the same bend, so the
            // curvature limit is set on the outside of the wall, not on the deck.
            float outerExtra = s.wallThickness + s.capOverhang + 0.2f;

            // Widths first without the curvature clamp, so a corner is opened out to fit the width
            // the path actually wants rather than to fit the width a sharp corner had already
            // forced it down to. Then the frames are rebuilt on the eased centreline and the widths
            // redone, this time with the clamp left in as a safety net.
            AssignWidths(s, route.Stations, outerExtra, false);
            if (RoundTightCorners(route.Stations, outerExtra))
            {
                ReDrape(route.Stations, ground);
                RebuildFrames(route.Stations);
            }
            AssignWidths(s, route.Stations, outerExtra, true);

            MeasureGrade(route.Stations);
            BuildHeightProfile(s, route);
            SampleGrid(s, ground, route, worldToLocal);

            for (int i = 0; i < route.Stations.Length; i++)
            {
                PathStation st = route.Stations[i];
                st.Center = worldToLocal.MultiplyPoint3x4(st.Center);
                st.Forward = worldToLocal.MultiplyVector(st.Forward).normalized;
                st.Right = worldToLocal.MultiplyVector(st.Right).normalized;
                route.Stations[i] = st;
            }

            route.Up = worldToLocal.MultiplyVector(Vector3.up).normalized;
            route.Length = route.Stations[route.Stations.Length - 1].Distance;
            return route;
        }

        // ------------------------------------------------------------------ route

        /// <summary>
        /// Runs a Catmull-Rom through the clicked points and lays a station down every
        /// <paramref name="spacing"/> metres of it.
        ///
        /// Both halves of that matter. The curve is sampled at a fixed number of steps per metre
        /// rather than per leg, because points clicked a hundred metres apart sampled in a fixed two
        /// dozen steps give a coarse polyline with a visible corner at every one. And stations are
        /// then placed by interpolating to the exact arc length rather than by taking whichever
        /// sample happened to land past it, because rounding each one up to the next sample is what
        /// turns a path drawn with wide clicks into slabs metres long, with paving joints that have
        /// nowhere to exist.
        /// </summary>
        static List<Vector3> ResampleControlPoints(IList<Vector3> control, float spacing, float maxLength)
        {
            var pts = new List<Vector3>();
            if (control == null || control.Count < 2) return pts;

            // Duplicate the ends so the curve actually reaches the first and last point.
            var cps = new List<Vector3>(control.Count + 2);
            cps.Add(control[0] + (control[0] - control[1]));
            for (int i = 0; i < control.Count; i++) cps.Add(control[i]);
            cps.Add(control[control.Count - 1] + (control[control.Count - 1] - control[control.Count - 2]));

            var fine = new List<Vector3>();
            fine.Add(cps[1]);

            float step = Mathf.Max(0.05f, spacing * 0.25f);
            int segments = cps.Count - 3;

            for (int seg = 0; seg < segments; seg++)
            {
                float chord = Vector3.Distance(cps[seg + 1], cps[seg + 2]);
                int sub = Mathf.Clamp(Mathf.CeilToInt(chord / step), 8, 8192);

                for (int k = 1; k <= sub; k++)
                    fine.Add(CatmullRom(cps[seg], cps[seg + 1], cps[seg + 2], cps[seg + 3], k / (float)sub));
            }

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

        static void Drape(List<Vector3> pts, IPathGround ground)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p, n;
                if (ground.Sample(pts[i], out p, out n)) pts[i] = p;
            }
        }

        static void ReDrape(PathStation[] stations, IPathGround ground)
        {
            for (int i = 0; i < stations.Length; i++)
            {
                Vector3 p, n;
                if (ground.Sample(stations[i].Center, out p, out n)) stations[i].Center = p;
            }
        }

        /// <summary>Moving average over the centreline. A path bridges the small bumps rather than
        /// reproducing every pixel of the heightmap.</summary>
        static void SmoothPositions(List<Vector3> pts, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i < pts.Count - 1; i++)
                    pts[i] = (pts[i - 1] + pts[i] * 2f + pts[i + 1]) * 0.25f;
            }
        }

        // ------------------------------------------------------------------ frames

        static PathStation[] BuildStations(List<Vector3> centers)
        {
            int n = centers.Count;
            var stations = new PathStation[n];

            for (int i = 0; i < n; i++) stations[i].Center = centers[i];
            RebuildFrames(stations);
            return stations;
        }

        /// <summary>
        /// Recomputes headings, side vectors and arc length from the centreline.
        ///
        /// The frame is deliberately horizontal. Right is across the hill and level, up is world up,
        /// and only the deck's height changes as the path climbs — which is what a path cut into a
        /// hillside does. Laying the frame on the terrain normal instead would bank every traverse
        /// like a racetrack.
        /// </summary>
        static void RebuildFrames(PathStation[] stations)
        {
            int n = stations.Length;
            float distance = 0f;

            for (int i = 0; i < n; i++)
            {
                if (i > 0) distance += Vector3.Distance(stations[i - 1].Center, stations[i].Center);
                stations[i].Distance = distance;

                Vector3 forward;
                if (i == 0) forward = stations[1].Center - stations[0].Center;
                else if (i == n - 1) forward = stations[i].Center - stations[i - 1].Center;
                else forward = stations[i + 1].Center - stations[i - 1].Center;

                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-8f)
                    forward = i > 0 ? stations[i - 1].Forward : Vector3.forward;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-8f) right = Vector3.right;

                stations[i].Forward = forward;
                stations[i].Right = right.normalized;
            }

            float total = Mathf.Max(1e-4f, stations[n - 1].Distance);
            for (int i = 0; i < n; i++) stations[i].T = stations[i].Distance / total;
        }

        /// <summary>Ground slope along the route, in degrees, smoothed so stairs do not switch on
        /// and off over a single bump.</summary>
        static void MeasureGrade(PathStation[] stations)
        {
            int n = stations.Length;
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1);
                int b = Mathf.Min(n - 1, i + 1);

                float run = Vector3.Distance(
                    new Vector3(stations[a].Center.x, 0f, stations[a].Center.z),
                    new Vector3(stations[b].Center.x, 0f, stations[b].Center.z));
                float rise = Mathf.Abs(stations[b].Center.y - stations[a].Center.y);

                stations[i].Grade = run > 1e-4f ? Mathf.Atan2(rise, run) * Mathf.Rad2Deg : 0f;
            }

            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < n - 1; i++)
                    stations[i].Grade = (stations[i - 1].Grade + stations[i].Grade * 2f +
                                         stations[i + 1].Grade) * 0.25f;
            }
        }

        // ------------------------------------------------------------------ width

        static void AssignWidths(PathSettings s, PathStation[] stations, float outerExtra,
                                 bool limitToCurvature)
        {
            int n = stations.Length;
            float half = Mathf.Max(0.5f, s.pathWidth) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float noise = PathNoise.Signed(stations[i].Distance * 0.05f, 7.3f, s.seed + 401);
                stations[i].HalfWidth = Mathf.Max(MinHalfWidth, half * (1f + noise * s.widthVariation));
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < n - 1; i++)
                    stations[i].HalfWidth = (stations[i - 1].HalfWidth + stations[i].HalfWidth * 2f +
                                             stations[i + 1].HalfWidth) * 0.25f;
            }

            if (limitToCurvature) LimitWidthToCurvature(stations, outerExtra);
        }

        /// <summary>
        /// Opens out any corner too sharp for the path to get round, by easing the centreline rather
        /// than by narrowing the deck.
        ///
        /// A drawn route has hard corners in it, because people click points and expect a path, not
        /// a polygon. Clamping the width at those corners does keep the mesh valid, but it pinches
        /// the path to a thread exactly where the eye is drawn — and on a switchback that is at
        /// every hairpin, which is the whole route.
        ///
        /// Returns true when it moved anything.
        /// </summary>
        static bool RoundTightCorners(PathStation[] stations, float outerExtra)
        {
            int n = stations.Length;
            if (n < 5) return false;

            const float Safety = 0.65f;
            const float Margin = 1.2f;
            const int MaxPasses = 160;

            bool movedAnything = false;
            var next = new Vector3[n];

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                for (int i = 0; i < n; i++) next[i] = stations[i].Center;

                bool tight = false;

                for (int i = 1; i < n - 1; i++)
                {
                    float needed = (stations[i].HalfWidth + outerExtra) / Safety * Margin;
                    if (RadiusAt(stations, i) >= needed) continue;

                    tight = true;
                    Vector3 average = (stations[i - 1].Center + stations[i + 1].Center) * 0.5f;
                    next[i] = Vector3.Lerp(stations[i].Center, average, 0.5f);
                }

                if (!tight) break;

                // The ends stay where they were put: a path that walked away from the doorway it
                // was drawn to start at would be worse than a sharp corner.
                for (int i = 1; i < n - 1; i++) stations[i].Center = next[i];
                movedAnything = true;
            }

            return movedAnything;
        }

        /// <summary>Radius of the arc the centreline follows through station i.</summary>
        static float RadiusAt(PathStation[] stations, int i)
        {
            Vector3 back = stations[i].Center - stations[i - 1].Center;
            Vector3 forward = stations[i + 1].Center - stations[i].Center;
            back.y = 0f;
            forward.y = 0f;

            float backLength = back.magnitude;
            float forwardLength = forward.magnitude;
            if (backLength < 1e-4f || forwardLength < 1e-4f) return float.MaxValue;

            float cos = Mathf.Clamp(Vector3.Dot(back / backLength, forward / forwardLength), -1f, 1f);
            float turn = Mathf.Acos(cos);
            if (turn < 1e-4f) return float.MaxValue;

            return (backLength + forwardLength) * 0.5f / turn;
        }

        /// <summary>
        /// Stops the path turning itself inside out on a bend.
        ///
        /// A ribbon swept round a corner tighter than it is wide folds its inner edge back through
        /// itself: the edge doubles over, the normals invert, and the mesh renders as a mess of
        /// shards right at the hairpin. Corner rounding above should have made this unnecessary; it
        /// stays as the safety net for a route that could not be eased any further.
        /// </summary>
        static void LimitWidthToCurvature(PathStation[] stations, float outerExtra)
        {
            int n = stations.Length;
            if (n < 3) return;

            // The continuous limit is the full radius: at a half width of R the inner edge collapses
            // to a point. Sitting near it is not enough, though, because the ribbon is a row of
            // discrete stations, and draping onto uneven ground can push the inner edge backwards
            // and turn a quad over. Two thirds keeps it moving forwards.
            const float Safety = 0.65f;
            const float Flare = 0.35f;

            var limit = new float[n];
            for (int i = 0; i < n; i++) limit[i] = float.MaxValue;

            for (int i = 1; i < n - 1; i++)
            {
                float radius = RadiusAt(stations, i);
                if (radius == float.MaxValue) continue;
                limit[i] = Mathf.Max(MinHalfWidth, radius * Safety - outerExtra);
            }

            // A bend limits the stations either side of it too, and gradually: a width that steps
            // from full to pinched between two stations puts a near-vertical wall down the outside
            // of the deck, which reads as a broken mesh even though nothing has actually folded.
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
                    stations[i].HalfWidth = (stations[i - 1].HalfWidth + stations[i].HalfWidth * 2f +
                                             stations[i + 1].HalfWidth) * 0.25f;
            }
            Clamp(stations, limit);
        }

        static void Clamp(PathStation[] stations, float[] limit)
        {
            for (int i = 0; i < stations.Length; i++)
                stations[i].HalfWidth = Mathf.Max(MinHalfWidth,
                                                  Mathf.Min(stations[i].HalfWidth, limit[i]));
        }

        // ------------------------------------------------------------------ height

        /// <summary>
        /// Decides what height the deck walks at, and where it breaks into steps.
        ///
        /// This is the one rule that lets a single drawn route serve a whole mountain: below
        /// <c>stepAngle</c> the deck simply ramps with the ground, and above it the deck holds level
        /// for a tread and then drops a whole number of risers. So the gentle traverses come out as
        /// ramps and the steep pitches between them come out as stairs, with the change happening
        /// where the hill actually steepens rather than anywhere someone had to mark.
        /// </summary>
        static void BuildHeightProfile(PathSettings s, PathRoute route)
        {
            PathStation[] stations = route.Stations;
            int n = stations.Length;

            route.Level = new bool[Mathf.Max(1, n - 1)];

            var groundY = new float[n];
            for (int i = 0; i < n; i++) groundY[i] = stations[i].Center.y;

            var stepped = new bool[n];
            if (s.stepMode != PathStepMode.None)
            {
                for (int i = 0; i < n; i++)
                    stepped[i] = s.stepMode == PathStepMode.Always || stations[i].Grade >= s.stepAngle;

                // A single station of stairs in the middle of a ramp is a trip hazard, not a
                // staircase. Fill in the one-station gaps and drop the one-station flights.
                Despeckle(stepped);
            }

            float rise = Mathf.Max(0.02f, s.stepRise);
            float tread = Mathf.Max(0.05f, s.stepTread);

            // The deck is a slab, not a sheet: the stones stand a joint's depth above the underlay
            // they are laid on. The centreline records the top of the stones, so it has to clear
            // the ground by the whole thickness — otherwise the underlay ends up below the terrain,
            // and the glow that is supposed to show between the stones is buried under the hill.
            // That reads as the paving simply not glowing, which sends you looking at the material.
            float deckTop = s.surfaceLift + s.jointDepth;

            float treadY = groundY[0];
            float lastRiserAt = float.NegativeInfinity;

            for (int i = 0; i < n; i++)
            {
                if (!stepped[i])
                {
                    // Ramping: the deck is simply the ground, and a flight starting later starts
                    // from wherever the ramp left off.
                    treadY = groundY[i];
                    lastRiserAt = float.NegativeInfinity;
                    stations[i].Center.y = groundY[i] + deckTop;
                    stations[i].Riser = 0f;
                    continue;
                }

                if (float.IsNegativeInfinity(lastRiserAt))
                {
                    // First station of a flight: start the stairs from the ground rather than from
                    // wherever the ramp behind it happened to end, or the path drops a whole riser
                    // in one station at the top of every staircase.
                    treadY = groundY[i];
                    lastRiserAt = stations[i].Distance;
                }
                else if (stations[i].Distance - lastRiserAt >= WantedTread(s, stations[i].Grade, rise, tread) - 1e-3f ||
                         Mathf.Abs(treadY - groundY[i]) >= rise)
                {
                    // Two ways to earn a step: the tread has run its length, or a full rise of
                    // ground has gone by underneath it. The second is what keeps the top of a
                    // staircase honest — the grade is smoothed, so where a gentle traverse meets a
                    // steep pitch it still reads as gentle, and without this the first tread runs
                    // on into the steep ground and then has to drop everything it banked up in one
                    // unclimbable step.
                    //
                    // Whole risers only, and as many as the ground has actually fallen by, so the
                    // stairs track the hill rather than drifting off it.
                    int steps = Mathf.RoundToInt((treadY - groundY[i]) / rise);
                    if (steps != 0)
                    {
                        treadY -= steps * rise;
                        stations[i].Riser = steps * rise;
                        lastRiserAt = stations[i].Distance;
                    }
                }

                stations[i].Center.y = treadY + deckTop;
            }

            // An interval is a level tread when the flight is running through it. The riser itself
            // lives at the far end of that interval, which is why the flag is read one back.
            for (int k = 0; k < n - 1; k++) route.Level[k] = stepped[k] && stepped[k + 1];

            // The first station of a flight has no tread behind it to step down from.
            if (n > 0) stations[0].Riser = 0f;
            for (int i = 1; i < n; i++)
                if (!route.Level[i - 1]) stations[i].Riser = 0f;
        }

        /// <summary>
        /// How deep a tread should be on ground of this steepness.
        ///
        /// Holding a step's depth at the authored tread however steep the hill gets is what makes a
        /// staircase unclimbable: on a 30 degree pitch a one-metre tread has to swallow 0.58 m of
        /// fall, so the riser comes out taller than the player can step. The tread is therefore cut
        /// down to whatever keeps the riser near one rise — but never below the station spacing,
        /// because a riser can only be placed at a cross-section. That last limit is the real one,
        /// and it is why very fine stairs need the spacing brought down as well.
        /// </summary>
        static float WantedTread(PathSettings s, float grade, float rise, float tread)
        {
            float slope = Mathf.Tan(Mathf.Clamp(grade, 0.5f, 80f) * Mathf.Deg2Rad);
            float ideal = rise / Mathf.Max(0.01f, slope);
            return Mathf.Clamp(ideal, Mathf.Max(0.15f, s.stationSpacing), tread);
        }

        /// <summary>Removes runs of a single station from a boolean profile, in both directions.</summary>
        static void Despeckle(bool[] flags)
        {
            int n = flags.Length;
            if (n < 3) return;

            var copy = (bool[])flags.Clone();
            for (int i = 1; i < n - 1; i++)
            {
                if (copy[i - 1] == copy[i + 1] && copy[i] != copy[i - 1]) flags[i] = copy[i - 1];
            }
        }

        // ------------------------------------------------------------------ ground grid

        /// <summary>
        /// Samples the ground under the whole footprint, not just under the centreline. On a
        /// hillside as steep as this one the two differ by metres across the width of the path,
        /// and the foundation needs to know how far it has to reach on each side.
        /// </summary>
        static void SampleGrid(PathSettings s, IPathGround ground, PathRoute route, Matrix4x4 worldToLocal)
        {
            PathStation[] stations = route.Stations;
            int n = stations.Length;

            int segments = Mathf.Max(4, s.lateralSegments + 4);
            if ((segments & 1) != 0) segments++;
            int lateral = segments + 1;

            route.Lateral = lateral;
            route.Ground = new Vector3[n, lateral];
            route.Offset = new float[n, lateral];

            float extra = s.wallThickness + s.capOverhang + s.seamWidth + 0.3f;

            for (int i = 0; i < n; i++)
            {
                PathStation st = stations[i];
                float footprint = st.HalfWidth + extra;

                for (int j = 0; j < lateral; j++)
                {
                    float across = (-1f + 2f * j / (lateral - 1)) * footprint;
                    Vector3 at = st.Center + st.Right * across;

                    Vector3 p, nrm;
                    // Off the end of the terrain, assume the ground carries on at the height the
                    // deck is at, so the foundation stops rather than reaching for the sky.
                    if (!ground.Sample(at, out p, out nrm)) p = at;

                    route.Offset[i, j] = across;
                    route.Ground[i, j] = worldToLocal.MultiplyPoint3x4(p);
                }
            }
        }
    }
}
