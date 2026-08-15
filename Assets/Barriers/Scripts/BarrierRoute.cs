using System.Collections.Generic;
using UnityEngine;

namespace Barriers
{
    /// <summary>One resampled point on a barrier line: where it is, which way it runs, what it stands on.</summary>
    public struct BarrierStation
    {
        /// <summary>World position, already dropped onto the ground.</summary>
        public Vector3 Position;

        /// <summary>Unit vector along the run, pointing towards the end of the line.</summary>
        public Vector3 Tangent;

        /// <summary>Ground normal here. <see cref="Vector3.up"/> where nothing was found.</summary>
        public Vector3 Normal;

        /// <summary>Metres from the start of the line, measured along it.</summary>
        public float Distance;

        /// <summary>False where the ground sampler found nothing — off the terrain, or over a hole.</summary>
        public bool Grounded;

        /// <summary>Horizontal right of the run. This is what a lateral offset moves along.</summary>
        public Vector3 Right
        {
            get
            {
                Vector3 r = Vector3.Cross(Vector3.up, Tangent);
                return r.sqrMagnitude < 1e-8f ? Vector3.right : r.normalized;
            }
        }

        /// <summary>How steep the ground is here, in degrees from flat.</summary>
        public float SlopeDegrees { get { return Vector3.Angle(Normal, Vector3.up); } }
    }

    /// <summary>
    /// A drawn polyline turned into something you can walk along at a fixed spacing: smoothed,
    /// resampled at a fine interval, draped onto the ground, and optionally shifted sideways.
    ///
    /// The sideways shift is done here rather than at placement time on purpose. Offsetting each
    /// placement off the centreline stretches the gaps on the outside of a bend and bunches them on
    /// the inside; offsetting the whole line first and then measuring along *that* keeps the spacing
    /// even on the row you actually see.
    /// </summary>
    public sealed class BarrierRoute
    {
        public readonly List<BarrierStation> Stations = new List<BarrierStation>();

        /// <summary>Total length of the line in metres.</summary>
        public float Length { get; private set; }

        /// <summary>Which way this route was pushed off the centreline: -1 left, +1 right, 0 centre.</summary>
        public float SideSign { get; private set; }

        public bool IsValid { get { return Stations.Count >= 2 && Length > 0.01f; } }

        /// <summary>
        /// Builds a route from hand-placed control points.
        /// </summary>
        /// <param name="controlWorld">The drawn points, in world space.</param>
        /// <param name="ground">Where the ground is. Pass null to leave the points where they are.</param>
        /// <param name="sampleSpacing">Resample interval. Finer follows the ground more closely.</param>
        /// <param name="smoothingPasses">How much the corners are eased before anything is placed.</param>
        /// <param name="lateralOffset">Metres to shift the whole line sideways. Positive is right.</param>
        /// <param name="closed">Join the last point back to the first.</param>
        public static BarrierRoute Build(IList<Vector3> controlWorld, IBarrierGround ground,
                                         float sampleSpacing, int smoothingPasses,
                                         float lateralOffset, bool closed)
        {
            var route = new BarrierRoute();
            route.SideSign = Mathf.Approximately(lateralOffset, 0f) ? 0f : Mathf.Sign(lateralOffset);

            List<Vector3> control = Clean(controlWorld);
            if (control.Count < 2) return route;

            // Smooth the ring first and close it afterwards. Closing it first leaves the first point
            // duplicated at the end, and a wrapping smooth then pulls those two copies apart — the
            // loop comes back with a visible notch where it is supposed to join.
            control = Densify(control, smoothingPasses, closed);
            control = Smooth(control, smoothingPasses, closed);
            if (closed && control.Count >= 3) control.Add(control[0]);

            List<Vector3> sampled = Resample(control, Mathf.Max(0.1f, sampleSpacing));
            if (sampled.Count < 2) return route;

            // Drape, then find the tangents from where the points actually ended up. Taking them
            // from the flat polyline instead would tilt every post the wrong way on a slope.
            for (int i = 0; i < sampled.Count; i++)
            {
                var st = new BarrierStation { Position = sampled[i], Normal = Vector3.up, Grounded = true };
                Vector3 p, n;
                if (ground != null && ground.Sample(sampled[i], out p, out n))
                {
                    st.Position = p;
                    st.Normal = n;
                }
                else if (ground != null)
                {
                    st.Grounded = false;
                }
                route.Stations.Add(st);
            }

            route.BridgeUngroundedHeights();
            route.ComputeTangents(closed);

            if (!Mathf.Approximately(lateralOffset, 0f))
            {
                for (int i = 0; i < route.Stations.Count; i++)
                {
                    BarrierStation st = route.Stations[i];
                    Vector3 moved = st.Position + st.Right * lateralOffset;

                    Vector3 p, n;
                    if (ground != null && ground.Sample(moved, out p, out n))
                    {
                        st.Position = p;
                        st.Normal = n;
                        st.Grounded = true;
                    }
                    else
                    {
                        st.Position = moved;
                        st.Grounded = ground == null;
                    }

                    route.Stations[i] = st;
                }

                route.BridgeUngroundedHeights();
                route.ComputeTangents(closed);
            }

            route.ComputeDistances();
            return route;
        }

        /// <summary>Position, direction and ground at a distance along the line.</summary>
        public bool SampleAt(float distance, out BarrierStation station)
        {
            station = default(BarrierStation);
            if (!IsValid) return false;

            distance = Mathf.Clamp(distance, 0f, Length);

            // The stations are evenly spaced in the polyline's own parameter but not in arc length
            // once they have been draped, so this walks rather than divides.
            int lo = 0, hi = Stations.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Stations[mid].Distance <= distance) lo = mid; else hi = mid;
            }

            BarrierStation a = Stations[lo];
            BarrierStation b = Stations[hi];
            float span = b.Distance - a.Distance;
            float t = span > 1e-5f ? (distance - a.Distance) / span : 0f;

            station.Position = Vector3.Lerp(a.Position, b.Position, t);
            station.Tangent = BlendDirection(a.Tangent, b.Tangent, t);
            station.Normal = BlendDirection(a.Normal, b.Normal, t);
            station.Distance = distance;
            station.Grounded = a.Grounded && b.Grounded;
            return true;
        }

        /// <summary>
        /// Blends two unit directions.
        ///
        /// Deliberately not <c>Vector3.Slerp</c>: that is a native call, so it throws outside the
        /// player and would take the whole route builder out of reach of a headless test. For two
        /// directions this close together the normalised lerp traces the same arc anyway.
        /// </summary>
        public static Vector3 BlendDirection(Vector3 a, Vector3 b, float t)
        {
            if (t <= 0f) return a.normalized;
            if (t >= 1f) return b.normalized;

            // Opposite directions have no shortest arc between them; picking one beats a zero vector.
            if (Vector3.Dot(a.normalized, b.normalized) < -0.9999f) return a.normalized;

            Vector3 blended = Vector3.Lerp(a, b, t);
            return blended.sqrMagnitude < 1e-10f ? a.normalized : blended.normalized;
        }

        // ------------------------------------------------------------------ construction helpers

        /// <summary>Drops points that sit on top of each other; two of them make a zero tangent.</summary>
        static List<Vector3> Clean(IList<Vector3> src)
        {
            var pts = new List<Vector3>();
            if (src == null) return pts;

            for (int i = 0; i < src.Count; i++)
            {
                if (pts.Count > 0 && (src[i] - pts[pts.Count - 1]).sqrMagnitude < 1e-4f) continue;
                pts.Add(src[i]);
            }
            return pts;
        }

        /// <summary>How many points the line is spread over before it is smoothed.</summary>
        const int SmoothingResolution = 160;

        /// <summary>
        /// Spreads the drawn points evenly along the line before any smoothing happens.
        ///
        /// Two things fall out of this, and both are worth the pass.
        ///
        /// A moving average pulls a closed ring towards its centroid, and how hard depends on how
        /// many points the ring has: a four-click loop round a plateau loses three quarters of its
        /// size per pass and simply vanishes at a high smoothing setting, while a fifty-click one
        /// barely moves. Spreading the ring over a couple of hundred points first drops the
        /// shrinkage to a fraction of a percent, so the slider rounds the corners instead of eating
        /// the loop.
        ///
        /// It also makes the setting mean the same thing everywhere. Smoothing over clicked points
        /// works in units of "how often did you click", so the same value gave a sweeping curve on
        /// a sparsely drawn run and did nothing at all on a carefully drawn one. Over an even
        /// spread it works in units of the line's own length, which is what anyone dragging the
        /// slider is expecting.
        /// </summary>
        static List<Vector3> Densify(List<Vector3> pts, int passes, bool closed)
        {
            if (passes <= 0 || pts.Count < 2) return pts;

            var work = new List<Vector3>(pts);
            if (closed && pts.Count >= 3) work.Add(pts[0]);

            float length = 0f;
            for (int i = 0; i < work.Count - 1; i++) length += Vector3.Distance(work[i], work[i + 1]);
            if (length < 1e-3f) return pts;

            List<Vector3> dense = Resample(work, Mathf.Max(0.5f, length / SmoothingResolution));
            if (dense.Count < 3) return pts;

            // Resample always finishes on the last input point, which for a ring is the first one
            // over again. Smooth wraps, so that copy has to go or the join gets counted twice.
            if (closed && pts.Count >= 3) dense.RemoveAt(dense.Count - 1);
            return dense;
        }

        /// <summary>
        /// Moving average over the polyline. Endpoints are pinned on an open line so the run still
        /// starts and finishes where it was drawn; a closed loop wraps instead, or it would develop
        /// a kink at the join.
        /// </summary>
        static List<Vector3> Smooth(List<Vector3> pts, int passes, bool closed)
        {
            passes = Mathf.Clamp(passes, 0, 12);
            if (passes <= 0 || pts.Count < 3) return pts;

            var current = pts;
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<Vector3>(current.Count);
                for (int i = 0; i < current.Count; i++)
                {
                    if (!closed && (i == 0 || i == current.Count - 1)) { next.Add(current[i]); continue; }

                    int prev = (i - 1 + current.Count) % current.Count;
                    int nxt = (i + 1) % current.Count;
                    next.Add(current[prev] * 0.25f + current[i] * 0.5f + current[nxt] * 0.25f);
                }
                current = next;
            }
            return current;
        }

        /// <summary>Walks the polyline emitting a point every <paramref name="spacing"/> metres.</summary>
        static List<Vector3> Resample(List<Vector3> pts, float spacing)
        {
            var outPts = new List<Vector3>();
            if (pts.Count < 2) return outPts;

            outPts.Add(pts[0]);
            float carry = 0f;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i];
                Vector3 b = pts[i + 1];
                float legLength = Vector3.Distance(a, b);
                if (legLength < 1e-5f) continue;

                Vector3 dir = (b - a) / legLength;
                float travelled = spacing - carry;

                while (travelled <= legLength)
                {
                    outPts.Add(a + dir * travelled);
                    travelled += spacing;
                }

                carry = legLength - (travelled - spacing);
            }

            // Always finish on the drawn end, however the spacing divided up.
            if ((outPts[outPts.Count - 1] - pts[pts.Count - 1]).sqrMagnitude > 1e-4f)
                outPts.Add(pts[pts.Count - 1]);

            return outPts;
        }

        /// <summary>
        /// Carries the height across any stretch the ground sampler could not answer for.
        ///
        /// Without this an ungrounded station keeps the height of the drawn point, which on a
        /// hillside is nowhere near the surface either side of it. The line then plunges and climbs
        /// back over the gap, and because arc length is measured along it, every placement *after*
        /// the gap comes out at the wrong spacing — the visible symptom is a barrier run that goes
        /// out of step past a hole in the terrain, which reads as a spacing bug rather than a
        /// draping one. The stations stay flagged ungrounded, so the skip rule still sees them.
        /// </summary>
        void BridgeUngroundedHeights()
        {
            int n = Stations.Count;

            int firstGood = -1, lastGood = -1;
            for (int i = 0; i < n; i++)
            {
                if (!Stations[i].Grounded) continue;
                if (firstGood < 0) firstGood = i;
                lastGood = i;
            }
            if (firstGood < 0) return; // nothing to interpolate from; leave the line as drawn

            for (int i = 0; i < n; i++)
            {
                if (Stations[i].Grounded) continue;

                float y;
                if (i < firstGood) y = Stations[firstGood].Position.y;
                else if (i > lastGood) y = Stations[lastGood].Position.y;
                else
                {
                    int before = i - 1;
                    while (before > 0 && !Stations[before].Grounded) before--;
                    int after = i + 1;
                    while (after < n - 1 && !Stations[after].Grounded) after++;

                    // Distances are not filled in yet, and the stations are evenly spaced along the
                    // drawn line anyway, so the run is measured in stations rather than metres.
                    float t = after == before ? 0f : (i - before) / (float)(after - before);
                    y = Mathf.Lerp(Stations[before].Position.y, Stations[after].Position.y, t);
                }

                BarrierStation st = Stations[i];
                st.Position = new Vector3(st.Position.x, y, st.Position.z);
                Stations[i] = st;
            }
        }

        void ComputeTangents(bool closed)
        {
            int n = Stations.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = Stations[closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)].Position;
                Vector3 next = Stations[closed ? (i + 1) % n : Mathf.Min(n - 1, i + 1)].Position;

                Vector3 t = next - prev;
                if (t.sqrMagnitude < 1e-8f) t = Stations[i].Tangent;
                if (t.sqrMagnitude < 1e-8f) t = Vector3.forward;

                BarrierStation st = Stations[i];
                st.Tangent = t.normalized;
                Stations[i] = st;
            }
        }

        void ComputeDistances()
        {
            float total = 0f;
            for (int i = 0; i < Stations.Count; i++)
            {
                if (i > 0) total += Vector3.Distance(Stations[i - 1].Position, Stations[i].Position);
                BarrierStation st = Stations[i];
                st.Distance = total;
                Stations[i] = st;
            }
            Length = total;
        }
    }
}
