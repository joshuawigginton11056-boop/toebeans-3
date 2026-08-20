using System.Collections.Generic;
using UnityEngine;

namespace Barriers
{
    /// <summary>One vertex of a barrier section, with everything that has to survive a bend.</summary>
    public struct BarrierVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 Uv0;
        public Vector2 Uv1;
        public Color Colour;

        /// <summary>
        /// Halfway between two vertices.
        ///
        /// Straight linear interpolation on purpose: the midpoint is made before anything is bent,
        /// while the edge is still a straight line, so a plain lerp lands exactly on the surface and
        /// the model is untouched until the bend itself moves it.
        /// </summary>
        public static BarrierVertex Lerp(BarrierVertex a, BarrierVertex b, float t)
        {
            BarrierVertex v;
            v.Position = Vector3.Lerp(a.Position, b.Position, t);
            v.Normal = Vector3.Lerp(a.Normal, b.Normal, t).normalized;
            v.Tangent = Vector4.Lerp(a.Tangent, b.Tangent, t);
            v.Uv0 = Vector2.Lerp(a.Uv0, b.Uv0, t);
            v.Uv1 = Vector2.Lerp(a.Uv1, b.Uv1, t);
            v.Colour = Color.Lerp(a.Colour, b.Colour, t);
            return v;
        }
    }

    /// <summary>
    /// A barrier section as plain lists, before and after bending. One submesh list per material,
    /// in the same order as the materials the section was read off.
    /// </summary>
    public sealed class BarrierSectionBuffer
    {
        public readonly List<BarrierVertex> Vertices = new List<BarrierVertex>();
        public readonly List<List<int>> Submeshes = new List<List<int>>();

        /// <summary>Whether the source had these at all, so an empty channel is not written back.</summary>
        public bool HasUv1;
        public bool HasColour;

        public bool IsEmpty { get { return Vertices.Count == 0 || Submeshes.Count == 0; } }

        public void Clear()
        {
            Vertices.Clear();
            Submeshes.Clear();
            HasUv1 = false;
            HasColour = false;
        }

        /// <summary>Copies another buffer into this one, so a template can be bent again and again.</summary>
        public void CopyFrom(BarrierSectionBuffer other)
        {
            Clear();
            if (other == null) return;

            Vertices.AddRange(other.Vertices);
            for (int s = 0; s < other.Submeshes.Count; s++)
            {
                var tris = new List<int>(other.Submeshes[s].Count);
                tris.AddRange(other.Submeshes[s]);
                Submeshes.Add(tris);
            }

            HasUv1 = other.HasUv1;
            HasColour = other.HasColour;
        }
    }

    /// <summary>
    /// Bends a straight barrier section so it follows the drawn line instead of cutting the corner.
    ///
    /// A fence section is a rigid model. Dropped on a line at its own length it meets the next one
    /// end to end down a straight, but a corner is longer round the outside than it is down the
    /// middle: the sections keep their length, the line does not, and they pile into each other at
    /// the bend. Shortening the spacing only makes them overlap sooner. The section itself has to
    /// give.
    ///
    /// So it is warped instead. Every vertex is read as a distance along the section, that distance
    /// is looked up on the route, and the vertex is rebuilt in the frame the route has there —
    /// sideways from the line, up from the ground. A 4 m section laid over 4 m of a hairpin comes
    /// out curved through the same 4 m, with its ends exactly on the ends of its slot, so the next
    /// section starts where this one finished however hard the corner turns.
    ///
    /// Two things this depends on:
    ///
    /// <list type="bullet">
    /// <item>The model has to have geometry along its length to bend. A box with vertices only at
    /// its two ends bends into the same box: the ends move, and there is nothing in between to
    /// follow the curve. <see cref="SubdivideAlong"/> cuts the section into rings first, and it cuts
    /// on the <b>edge</b> rather than the triangle, so two triangles sharing an edge always agree
    /// about its midpoint and the bend cannot open a crack along a seam.</item>
    /// <item>The frame has to come off the route, not off the vertex. Taking a rotation per vertex
    /// from its own neighbourhood twists a section wherever the model is dense; taking it from the
    /// line means every vertex at the same distance is moved by the same rigid transform, which is
    /// what keeps a flat rail face flat.</item>
    /// </list>
    ///
    /// Nothing here touches a native type — no Mesh, no Renderer, no Quaternion — so the whole bend
    /// can be run and asserted outside the Editor, same rule as <see cref="BarrierWallBuilder"/>.
    /// </summary>
    public static class BarrierSectionBender
    {
        /// <summary>How many times the subdivider will go round. Each pass halves an over-long edge.</summary>
        const int MaxSubdivisionPasses = 7;

        /// <summary>Shortest ring spacing that is worth cutting to, in metres.</summary>
        const float MinRingSpacing = 0.02f;

        // ================================================================= subdivision

        /// <summary>
        /// Cuts the section into rings along <paramref name="axis"/>, so there is something between
        /// its ends for the bend to move.
        /// </summary>
        /// <param name="buffer">Section to cut, in the section's own space. Edited in place.</param>
        /// <param name="axis">Which local axis runs along the line: 0 for X, 2 for Z.</param>
        /// <param name="maxRingSpacing">Longest an edge may reach along that axis, in metres.</param>
        /// <param name="vertexBudget">Stop once the section is this big, however coarse it still is.</param>
        public static void SubdivideAlong(BarrierSectionBuffer buffer, int axis, float maxRingSpacing,
                                          int vertexBudget)
        {
            if (buffer == null || buffer.IsEmpty) return;

            maxRingSpacing = Mathf.Max(MinRingSpacing, maxRingSpacing);
            var midpoints = new Dictionary<long, int>();
            var built = new List<int>();

            for (int pass = 0; pass < MaxSubdivisionPasses; pass++)
            {
                if (buffer.Vertices.Count >= vertexBudget) break;

                bool cut = false;
                midpoints.Clear();

                for (int s = 0; s < buffer.Submeshes.Count; s++)
                {
                    List<int> tris = buffer.Submeshes[s];
                    built.Clear();

                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];

                        int ab = Span(buffer, axis, a, b) > maxRingSpacing ? Midpoint(buffer, midpoints, a, b) : -1;
                        int bc = Span(buffer, axis, b, c) > maxRingSpacing ? Midpoint(buffer, midpoints, b, c) : -1;
                        int ca = Span(buffer, axis, c, a) > maxRingSpacing ? Midpoint(buffer, midpoints, c, a) : -1;

                        if (ab < 0 && bc < 0 && ca < 0) { Tri(built, a, b, c); continue; }
                        cut = true;

                        // Every case keeps the winding of the triangle it came from: the sub
                        // triangles are listed in the same a to b to c order the original ran in.
                        if (ab >= 0 && bc >= 0 && ca >= 0)
                        {
                            Tri(built, a, ab, ca);
                            Tri(built, ab, b, bc);
                            Tri(built, ca, bc, c);
                            Tri(built, ab, bc, ca);
                        }
                        else if (ab >= 0 && bc >= 0) { Tri(built, a, ab, c); Tri(built, ab, bc, c); Tri(built, ab, b, bc); }
                        else if (bc >= 0 && ca >= 0) { Tri(built, b, bc, a); Tri(built, bc, ca, a); Tri(built, bc, c, ca); }
                        else if (ca >= 0 && ab >= 0) { Tri(built, c, ca, b); Tri(built, ca, ab, b); Tri(built, ca, a, ab); }
                        else if (ab >= 0) { Tri(built, a, ab, c); Tri(built, ab, b, c); }
                        else if (bc >= 0) { Tri(built, b, bc, a); Tri(built, bc, c, a); }
                        else { Tri(built, c, ca, b); Tri(built, ca, a, b); }
                    }

                    tris.Clear();
                    tris.AddRange(built);
                }

                if (!cut) break;
            }
        }

        static void Tri(List<int> tris, int a, int b, int c)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        static float Span(BarrierSectionBuffer buffer, int axis, int a, int b)
        {
            return Mathf.Abs(Component(buffer.Vertices[a].Position, axis) -
                             Component(buffer.Vertices[b].Position, axis));
        }

        /// <summary>
        /// The midpoint of an edge, made once and shared.
        ///
        /// Keyed on the edge rather than the triangle: the neighbour across that edge asks for the
        /// same key and gets the same vertex back, which is what stops the cut leaving a T-junction
        /// that opens into a visible crack the moment the section is bent.
        /// </summary>
        static int Midpoint(BarrierSectionBuffer buffer, Dictionary<long, int> cache, int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;

            int found;
            if (cache.TryGetValue(key, out found)) return found;

            buffer.Vertices.Add(BarrierVertex.Lerp(buffer.Vertices[lo], buffer.Vertices[hi], 0.5f));
            int index = buffer.Vertices.Count - 1;
            cache[key] = index;
            return index;
        }

        // ======================================================================= bend

        /// <summary>How a section's own axes line up with the line it is being bent onto.</summary>
        public struct SectionAxes
        {
            /// <summary>Which local axis runs along the line: 0 for X, 2 for Z.</summary>
            public int Along;

            /// <summary>Where the section starts on that axis, in its own space.</summary>
            public float Min;

            /// <summary>How long it is on that axis, in its own space.</summary>
            public float Length;
        }

        /// <summary>
        /// Bends a section onto a stretch of route.
        ///
        /// The section is laid over <paramref name="span"/> metres of line starting at
        /// <paramref name="startDistance"/>, whatever its own length is: a section given a slot
        /// shorter than itself is squeezed into it and one given a longer slot is stretched to fill
        /// it, so a run always joins up.
        /// </summary>
        /// <param name="buffer">Section to bend, in its own space. Rewritten in place.</param>
        /// <param name="route">Line to bend it onto.</param>
        /// <param name="axes">Which way the section runs and how long it is.</param>
        /// <param name="startDistance">Metres along the route where this section starts.</param>
        /// <param name="span">Metres of route this section covers.</param>
        /// <param name="lateralScale">Scale across the line.</param>
        /// <param name="verticalScale">Scale up the line.</param>
        /// <param name="lateralOffset">Metres to shift the whole section sideways.</param>
        /// <param name="verticalOffset">Metres to raise or bury it.</param>
        /// <param name="groundBlend">How much the section leans with the ground, 0 upright to 1 flat.</param>
        /// <param name="toLocal">World to the space the mesh will live in.</param>
        /// <param name="localOrigin">Point in that space the mesh is measured from, so it has a
        /// pivot at the section rather than back at the line's own origin.</param>
        public static bool Bend(BarrierSectionBuffer buffer, BarrierRoute route, SectionAxes axes,
                                float startDistance, float span,
                                float lateralScale, float verticalScale,
                                float lateralOffset, float verticalOffset,
                                float groundBlend, Matrix4x4 toLocal, Vector3 localOrigin)
        {
            if (buffer == null || buffer.IsEmpty || route == null || !route.IsValid) return false;
            if (axes.Length <= 1e-4f || span <= 1e-4f) return false;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                BarrierVertex v = buffer.Vertices[i];

                float t = Mathf.Clamp01((Component(v.Position, axes.Along) - axes.Min) / axes.Length);

                Vector3 centre, right, up, forward;
                if (!Frame(route, startDistance + t * span, groundBlend, out centre, out right, out up, out forward))
                    return false;

                // The along component is spent on picking the frame; only the cross-section is
                // carried into it. That is the whole trick — the model's length becomes distance
                // travelled, so the mesh follows the line instead of cutting across it.
                float lateral = Lateral(v.Position, axes.Along);

                Vector3 world = centre
                              + right * (lateral * lateralScale + lateralOffset)
                              + up * (v.Position.y * verticalScale)
                              + Vector3.up * verticalOffset;

                v.Position = toLocal.MultiplyPoint3x4(world) - localOrigin;
                v.Normal = toLocal.MultiplyVector(
                    Rotate(v.Normal, axes.Along, right, up, forward)).normalized;

                Vector3 tan = toLocal.MultiplyVector(
                    Rotate(new Vector3(v.Tangent.x, v.Tangent.y, v.Tangent.z),
                           axes.Along, right, up, forward)).normalized;
                v.Tangent = new Vector4(tan.x, tan.y, tan.z, v.Tangent.w);

                buffer.Vertices[i] = v;
            }

            return true;
        }

        /// <summary>
        /// The frame at a distance along the line: where it is, and which way is sideways, up and on.
        /// </summary>
        public static bool Frame(BarrierRoute route, float distance, float groundBlend,
                                 out Vector3 position, out Vector3 right, out Vector3 up,
                                 out Vector3 forward)
        {
            position = Vector3.zero;
            right = Vector3.right;
            up = Vector3.up;
            forward = Vector3.forward;

            BarrierStation st;
            if (route == null || !route.SampleAt(distance, out st)) return false;

            position = st.Position;
            up = BarrierRoute.BlendDirection(Vector3.up, st.Normal, groundBlend);

            forward = Vector3.ProjectOnPlane(st.Tangent, up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward = forward.normalized;

            right = Vector3.Cross(up, forward).normalized;
            return true;
        }

        /// <summary>
        /// Degrees a section authored along <paramref name="along"/> has to be turned so that axis
        /// points down the line. Zero for the usual model, which already runs along its Z.
        /// </summary>
        public static float YawCorrection(int along)
        {
            return along == 0 ? -90f : 0f;
        }

        // ==================================================================== plumbing

        static float Component(Vector3 v, int axis)
        {
            return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
        }

        /// <summary>
        /// The across-the-line component of a section-space vector.
        ///
        /// A model authored along X has its cross-section on Z, and the turn that puts +X down the
        /// line carries +Z onto the line's left — hence the sign, which is the same turn
        /// <see cref="YawCorrection"/> reports.
        /// </summary>
        static float Lateral(Vector3 v, int along)
        {
            return along == 0 ? -v.z : v.x;
        }

        static Vector3 Rotate(Vector3 v, int along, Vector3 right, Vector3 up, Vector3 forward)
        {
            return right * Lateral(v, along) + up * v.y + forward * Component(v, along);
        }
    }
}
