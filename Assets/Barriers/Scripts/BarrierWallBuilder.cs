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
    /// Nothing here touches the scene or a native type, so the whole thing can be run and asserted
    /// outside the Editor — same rule as the other mesh builders in this project.
    /// </summary>
    public static class BarrierWallBuilder
    {
        /// <summary>
        /// Builds the wall in the space of <paramref name="toLocal"/>.
        /// </summary>
        /// <param name="route">Line to sweep along.</param>
        /// <param name="height">How far the wall stands above the ground.</param>
        /// <param name="thickness">Wall thickness across the line.</param>
        /// <param name="embed">How far the wall is buried, so a bumpy surface has no gap under it.</param>
        /// <param name="segmentLength">Sweep interval. Coarser is cheaper and cuts corners more.</param>
        /// <param name="toLocal">World to the object the collider will sit on.</param>
        public static BarrierWallBuffer Build(BarrierRoute route, float height, float thickness,
                                              float embed, float segmentLength, Matrix4x4 toLocal)
        {
            var buffer = new BarrierWallBuffer();
            if (route == null || !route.IsValid) return buffer;

            height = Mathf.Max(0.05f, height);
            float half = Mathf.Max(0.01f, thickness) * 0.5f;
            segmentLength = Mathf.Max(0.25f, segmentLength);

            // Ring corners, in order: inner-bottom, outer-bottom, outer-top, inner-top.
            var rings = new List<Vector3[]>();
            var tangents = new List<Vector3>();

            float d = 0f;
            while (true)
            {
                BarrierStation st;
                if (route.SampleAt(d, out st))
                {
                    Vector3 right = st.Right;
                    Vector3 foot = st.Position - Vector3.up * embed;
                    Vector3 rise = Vector3.up * (height + embed);

                    rings.Add(new[]
                    {
                        toLocal.MultiplyPoint3x4(foot - right * half),
                        toLocal.MultiplyPoint3x4(foot + right * half),
                        toLocal.MultiplyPoint3x4(foot + right * half + rise),
                        toLocal.MultiplyPoint3x4(foot - right * half + rise)
                    });
                    tangents.Add(toLocal.MultiplyVector(st.Tangent).normalized);
                }

                if (d >= route.Length) break;
                d = Mathf.Min(d + segmentLength, route.Length);
            }

            if (rings.Count < 2) return buffer;

            for (int i = 0; i < rings.Count - 1; i++)
            {
                Vector3[] a = rings[i];
                Vector3[] b = rings[i + 1];
                Vector3 axis = (Centre(a) + Centre(b)) * 0.5f;

                for (int e = 0; e < 4; e++)
                {
                    int n = (e + 1) % 4;
                    Vector3 faceMid = (a[e] + a[n] + b[n] + b[e]) * 0.25f;
                    // Outward hint: away from the middle of the segment, which is what lets each of
                    // the four sides pick its own winding without a table of special cases.
                    AddQuad(buffer, a[e], a[n], b[n], b[e], faceMid - axis);
                }
            }

            // Caps, so the tube is a closed solid rather than a shell open at both ends.
            AddQuad(buffer, rings[0][0], rings[0][1], rings[0][2], rings[0][3], -tangents[0]);
            int last = rings.Count - 1;
            AddQuad(buffer, rings[last][0], rings[last][1], rings[last][2], rings[last][3],
                    tangents[last]);

            return buffer;
        }

        static Vector3 Centre(Vector3[] ring)
        {
            return (ring[0] + ring[1] + ring[2] + ring[3]) * 0.25f;
        }

        /// <summary>
        /// Adds a quad wound so it faces <paramref name="outward"/>.
        ///
        /// Cross is left-handed in Unity, so the corner order that looks natural builds half of
        /// these inside out. Letting the helper choose its own winding is how the other generators
        /// here avoid that.
        /// </summary>
        static void AddQuad(BarrierWallBuffer buffer,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = buffer.Vertices.Count;
            buffer.Vertices.Add(a);
            buffer.Vertices.Add(b);
            buffer.Vertices.Add(c);
            buffer.Vertices.Add(d);

            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (flip)
            {
                buffer.Triangles.Add(i); buffer.Triangles.Add(i + 2); buffer.Triangles.Add(i + 1);
                buffer.Triangles.Add(i); buffer.Triangles.Add(i + 3); buffer.Triangles.Add(i + 2);
            }
            else
            {
                buffer.Triangles.Add(i); buffer.Triangles.Add(i + 1); buffer.Triangles.Add(i + 2);
                buffer.Triangles.Add(i); buffer.Triangles.Add(i + 2); buffer.Triangles.Add(i + 3);
            }
        }
    }
}
