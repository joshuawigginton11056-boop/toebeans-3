using System.Collections.Generic;
using UnityEngine;

namespace Barriers
{
    /// <summary>Vertices and triangles, before anything native is touched.</summary>
    public sealed class BarrierWallBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();

        public bool IsEmpty { get { return Vertices.Count == 0 || Triangles.Count == 0; } }
    }

    /// <summary>
    /// Sweeps an invisible solid wall along a route, for a <see cref="MeshCollider"/>.
    ///
    /// Spaced props do not actually stop anything: a kart fits between two rocks 4 m apart, and the
    /// gap is exactly where a player aiming off the edge is heading. This closes the line without
    /// changing what it looks like — the wall has no renderer.
    ///
    /// It is built as a closed box tube rather than a single ribbon so it has real thickness. A
    /// zero-thickness collider is a coin flip at kart speeds; a 40 cm one is not.
    ///
    /// The three things that make it something a kart can lean on and slide along, rather than
    /// catch on, are all in here:
    ///
    /// <list type="bullet">
    /// <item>The rings are <b>welded</b> — consecutive segments share their corner vertices, so the
    /// inside face is one continuous surface instead of a stack of separate quads meeting at
    /// coincident-but-distinct edges.</item>
    /// <item>The cross-section is <b>mitered</b> onto the bisector of the turn, so a corner joins
    /// edge to edge. Offsetting each ring along its own right vector, which is the obvious thing to
    /// do, gaps the outside of a corner and folds the inside through itself — and a fold is the
    /// single most reliable way to stop a kart dead.</item>
    /// <item>Sampling is <b>adaptive</b>: the sweep subdivides until no two rings turn more than a
    /// few degrees apart, so a hairpin gets the density it needs and a straight does not pay for
    /// it.</item>
    /// </list>
    ///
    /// Nothing here touches the scene or a native type, so the whole thing can be run and asserted
    /// outside the Editor — same rule as the other mesh builders in this project.
    /// </summary>
    public static class BarrierWallBuilder
    {
        /// <summary>Shortest the adaptive sweep will subdivide to, in metres.</summary>
        const float MinStep = 0.15f;

        /// <summary>Ceiling on ring count, so a pathological route cannot hang the editor.</summary>
        const int MaxRings = 20000;

        /// <summary>
        /// How far a miter is allowed to push the wall out on a tight corner. Past this the corner
        /// is sharper than the wall is thick and the join is squared off instead, which loses a few
        /// centimetres of coverage but does not spike geometry across the track.
        /// </summary>
        const float MaxMiter = 4f;

        /// <summary>
        /// Smoothing passes over the swept positions. Enough to spread a polyline vertex across
        /// its neighbours, few enough that the wall still sits where the line was drawn — at this
        /// count a corner is eased by a few centimetres and a straight not at all.
        /// </summary>
        const int RelaxPasses = 12;

        /// <summary>
        /// Builds the wall in the space of <paramref name="toLocal"/>.
        /// </summary>
        /// <param name="route">Line to sweep along.</param>
        /// <param name="height">How far the wall stands above the ground.</param>
        /// <param name="thickness">Wall thickness across the line.</param>
        /// <param name="embed">How far the wall is buried, so a bumpy surface has no gap under it.</param>
        /// <param name="segmentLength">Longest sweep interval. Corners subdivide below it.</param>
        /// <param name="maxTurnDegrees">Most a corner may turn between two rings.</param>
        /// <param name="toLocal">World to the object the collider will sit on.</param>
        public static BarrierWallBuffer Build(BarrierRoute route, float height, float thickness,
                                              float embed, float segmentLength,
                                              float maxTurnDegrees, Matrix4x4 toLocal)
        {
            var buffer = new BarrierWallBuffer();
            if (route == null || !route.IsValid) return buffer;

            height = Mathf.Max(0.05f, height);
            float half = Mathf.Max(0.01f, thickness) * 0.5f;
            segmentLength = Mathf.Max(0.25f, segmentLength);
            float maxTurn = Mathf.Clamp(maxTurnDegrees, 0.5f, 30f);

            List<BarrierStation> stations = Sample(route, segmentLength, maxTurn);
            if (stations.Count < 2) return buffer;

            // A run that rings the whole area comes back to its own start. Dropping the duplicate
            // and closing the sweep is what stops the loop having a seam across the finish.
            bool closed = (stations[0].Position - stations[stations.Count - 1].Position).sqrMagnitude < 1e-4f;
            if (closed)
            {
                stations.RemoveAt(stations.Count - 1);
                if (stations.Count < 3) closed = false;
            }

            int n = stations.Count;
            if (n < 2) return buffer;

            Relax(stations, closed);
            Vector3[] offsets = MiterOffsets(stations, closed, half);

            // Ring corners, in order: inner-bottom, outer-bottom, outer-top, inner-top.
            Vector3 rise = Vector3.up * (height + embed);
            var ringBase = new int[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 foot = stations[i].Position - Vector3.up * embed;
                Vector3 o = offsets[i];

                ringBase[i] = buffer.Vertices.Count;
                buffer.Vertices.Add(toLocal.MultiplyPoint3x4(foot - o));
                buffer.Vertices.Add(toLocal.MultiplyPoint3x4(foot + o));
                buffer.Vertices.Add(toLocal.MultiplyPoint3x4(foot + o + rise));
                buffer.Vertices.Add(toLocal.MultiplyPoint3x4(foot - o + rise));
            }

            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = ringBase[i];
                int b = ringBase[(i + 1) % n];
                Vector3 axis = (Centre(buffer, a) + Centre(buffer, b)) * 0.5f;

                for (int e = 0; e < 4; e++)
                {
                    int next = (e + 1) % 4;
                    Vector3 faceMid = (buffer.Vertices[a + e] + buffer.Vertices[a + next]
                                     + buffer.Vertices[b + next] + buffer.Vertices[b + e]) * 0.25f;
                    // Outward hint: away from the middle of the segment, which is what lets each of
                    // the four sides pick its own winding without a table of special cases.
                    AddQuad(buffer, a + e, a + next, b + next, b + e, faceMid - axis);
                }
            }

            if (!closed)
            {
                // Caps, so the tube is a closed solid rather than a shell open at both ends.
                Vector3 first = toLocal.MultiplyVector(stations[0].Tangent).normalized;
                Vector3 last = toLocal.MultiplyVector(stations[n - 1].Tangent).normalized;
                int f = ringBase[0], l = ringBase[n - 1];
                AddQuad(buffer, f, f + 1, f + 2, f + 3, -first);
                AddQuad(buffer, l, l + 1, l + 2, l + 3, last);
            }

            return buffer;
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>
        /// Walks the route, halving the step wherever the line turns faster than
        /// <paramref name="maxTurn"/> allows. A straight costs one ring per
        /// <paramref name="maxStep"/>; a hairpin gets as many as it needs, down to
        /// <see cref="MinStep"/>.
        /// </summary>
        static List<BarrierStation> Sample(BarrierRoute route, float maxStep, float maxTurn)
        {
            var stations = new List<BarrierStation>();

            BarrierStation st;
            if (!route.SampleAt(0f, out st)) return stations;
            stations.Add(st);

            float cosLimit = Mathf.Cos(maxTurn * Mathf.Deg2Rad);
            float d = 0f;

            while (d < route.Length && stations.Count < MaxRings)
            {
                float step = Mathf.Min(maxStep, route.Length - d);
                Vector3 here = Flat(stations[stations.Count - 1].Tangent, Vector3.forward);

                while (step > MinStep)
                {
                    BarrierStation probe;
                    if (!route.SampleAt(d + step, out probe)) break;
                    if (Vector3.Dot(Flat(probe.Tangent, here), here) >= cosLimit) break;
                    step *= 0.5f;
                }

                d = Mathf.Min(d + Mathf.Max(step, MinStep), route.Length);

                BarrierStation next;
                if (route.SampleAt(d, out next)) stations.Add(next);
            }

            return stations;
        }

        // ------------------------------------------------------------------ relaxing

        /// <summary>
        /// Eases the sweep sideways, in the horizontal plane only.
        ///
        /// Subdividing a corner alone does not smooth it. The route is a polyline, and every ring
        /// lands *on* it, so extra rings between two polyline vertices are simply collinear — the
        /// whole direction change still happens in one jump at the vertex, and that jump is what a
        /// kart feels. Averaging the ring positions spreads that jump across the rings either side
        /// of it, which is what turns a corner into something continuous.
        ///
        /// Heights are left alone so the wall still follows the ground, and the ends are pinned so
        /// a run still starts and finishes exactly where it was drawn. On a straight this is a
        /// no-op: the average of three collinear points is the middle one.
        /// </summary>
        static void Relax(List<BarrierStation> stations, bool closed)
        {
            int n = stations.Count;
            if (n < 3) return;

            var xz = new Vector3[n];
            for (int i = 0; i < n; i++) xz[i] = stations[i].Position;

            var next = new Vector3[n];
            for (int pass = 0; pass < RelaxPasses; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1)) { next[i] = xz[i]; continue; }

                    Vector3 a = xz[(i - 1 + n) % n];
                    Vector3 b = xz[i];
                    Vector3 c = xz[(i + 1) % n];

                    next[i] = new Vector3(
                        (a.x + 2f * b.x + c.x) * 0.25f,
                        b.y,
                        (a.z + 2f * b.z + c.z) * 0.25f);
                }
                (xz, next) = (next, xz);
            }

            for (int i = 0; i < n; i++)
            {
                BarrierStation st = stations[i];
                st.Position = xz[i];
                stations[i] = st;
            }
        }

        // ------------------------------------------------------------------ mitering

        /// <summary>
        /// Sideways offset for each ring, on the bisector of the turn and lengthened so the two
        /// faces meet exactly at the corner rather than gapping or overlapping.
        /// </summary>
        static Vector3[] MiterOffsets(List<BarrierStation> stations, bool closed, float half)
        {
            int n = stations.Count;
            var offsets = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 p = stations[i].Position;
                Vector3 fallback = Flat(stations[i].Tangent, Vector3.forward);

                Vector3 dIn, dOut;
                if (i == 0 && !closed)
                {
                    dOut = Flat(stations[1].Position - p, fallback);
                    dIn = dOut;
                }
                else if (i == n - 1 && !closed)
                {
                    dIn = Flat(p - stations[n - 2].Position, fallback);
                    dOut = dIn;
                }
                else
                {
                    dIn = Flat(p - stations[(i - 1 + n) % n].Position, fallback);
                    dOut = Flat(stations[(i + 1) % n].Position - p, fallback);
                }

                Vector3 rIn = RightOf(dIn);
                Vector3 rOut = RightOf(dOut);
                Vector3 sum = rIn + rOut;

                if (sum.sqrMagnitude < 1e-6f)
                {
                    // A dead reversal has no bisector. Square the end off rather than divide by ~0.
                    offsets[i] = rOut * half;
                    continue;
                }

                Vector3 miter = sum.normalized;
                float cos = Vector3.Dot(miter, rOut);
                float scale = cos > 1e-3f ? Mathf.Min(1f / cos, MaxMiter) : MaxMiter;
                offsets[i] = miter * (half * scale);
            }

            return offsets;
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-8f ? fallback : v.normalized;
        }

        static Vector3 RightOf(Vector3 dir)
        {
            Vector3 r = Vector3.Cross(Vector3.up, dir);
            return r.sqrMagnitude < 1e-8f ? Vector3.right : r.normalized;
        }

        static Vector3 Centre(BarrierWallBuffer buffer, int ringStart)
        {
            return (buffer.Vertices[ringStart] + buffer.Vertices[ringStart + 1]
                  + buffer.Vertices[ringStart + 2] + buffer.Vertices[ringStart + 3]) * 0.25f;
        }

        /// <summary>
        /// Adds a quad over existing vertices, wound so it faces <paramref name="outward"/>.
        ///
        /// Indices rather than fresh vertices is the whole point: the rings are shared between
        /// neighbouring segments, so the swept faces form one continuous surface with no interior
        /// edges for a kart to catch on.
        ///
        /// Cross is left-handed in Unity, so the corner order that looks natural builds half of
        /// these inside out. Letting the helper choose its own winding is how the other generators
        /// here avoid that.
        /// </summary>
        static void AddQuad(BarrierWallBuffer buffer, int a, int b, int c, int d, Vector3 outward)
        {
            Vector3 va = buffer.Vertices[a];
            Vector3 vb = buffer.Vertices[b];
            Vector3 vc = buffer.Vertices[c];

            bool flip = Vector3.Dot(Vector3.Cross(vb - va, vc - va), outward) < 0f;
            if (flip)
            {
                buffer.Triangles.Add(a); buffer.Triangles.Add(c); buffer.Triangles.Add(b);
                buffer.Triangles.Add(a); buffer.Triangles.Add(d); buffer.Triangles.Add(c);
            }
            else
            {
                buffer.Triangles.Add(a); buffer.Triangles.Add(b); buffer.Triangles.Add(c);
                buffer.Triangles.Add(a); buffer.Triangles.Add(c); buffer.Triangles.Add(d);
            }
        }
    }
}
