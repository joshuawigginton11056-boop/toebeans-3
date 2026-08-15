using System.Collections.Generic;
using UnityEngine;

namespace PlayerPath
{
    /// <summary>Material slots on the generated path, in submesh order.</summary>
    public enum PathSlot
    {
        /// <summary>The flagstones the player walks on.</summary>
        Deck = 0,

        /// <summary>The bricks along the edges.</summary>
        Edge = 1,

        /// <summary>Cut stone: coping along the top of the wall, step risers, the wall core and the
        /// foundation. Everything that is masonry but is not a face brick or a flagstone.</summary>
        Trim = 2,

        /// <summary>The heat under the path: what shows through the joints, along the seam at the
        /// foot of each wall, and in the bricks that have not finished cooling. Wants an emissive
        /// material.</summary>
        Glow = 3
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look.
    ///
    /// UVs are authored rather than projected, because the two surfaces here want different ones:
    /// the deck wants metres across and metres along, and a wall face wants metres along and metres
    /// up, or brick courses come out lying on their sides. UV1 carries (1, distance from the edge)
    /// for any shader that wants to fade something in towards the middle of the path.
    /// </summary>
    public class PathMeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Vector2> UV1 = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int>[] Submeshes;

        readonly float _uvScale;

        public PathMeshBuffer(int submeshCount, float uvScale)
        {
            Submeshes = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) Submeshes[i] = new List<int>();
            _uvScale = uvScale <= 0f ? 1f : uvScale;
        }

        public int TriangleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Submeshes.Length; i++) n += Submeshes[i].Count;
                return n / 3;
            }
        }

        static readonly Color[] SlotTint =
        {
            new Color(0.34f, 0.32f, 0.33f, 1f), // Deck
            new Color(0.30f, 0.19f, 0.17f, 1f), // Edge
            new Color(0.24f, 0.23f, 0.24f, 1f), // Trim
            new Color(1.00f, 0.45f, 0.10f, 1f)  // Glow
        };

        public static Color TintFor(PathSlot slot)
        {
            int i = (int)slot;
            return (i >= 0 && i < SlotTint.Length) ? SlotTint[i] : Color.white;
        }

        /// <summary>Adds one flat-shaded triangle with authored UVs.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c,
                                Vector2 uvA, Vector2 uvB, Vector2 uvC,
                                Vector2 edgeA, Vector2 edgeB, Vector2 edgeC,
                                PathSlot slot, float shade)
        {
            Vector3 n;
            if (!TryNormal(a, b, c, out n)) return;

            Color col = Shade(slot, shade);
            int baseIndex = Vertices.Count;
            Push(a, n, col, uvA, edgeA);
            Push(b, n, col, uvB, edgeB);
            Push(c, n, col, uvC, edgeC);

            List<int> tris = Submeshes[(int)slot];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        /// <summary>
        /// Adds the two triangles of a quad, wound so the face points along
        /// <paramref name="outward"/>.
        ///
        /// Every surface here is built from a grid whose axes flip meaning depending on which side
        /// of the path, which end of it and which face of a brick they belong to. Rather than
        /// tracking that by hand and finding out later that half the path is inside out — which is
        /// invisible rather than visibly wrong, since a back face is simply culled — each quad is
        /// handed the direction it is meant to face and picks its own winding.
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD,
                            Vector3 outward, PathSlot slot, float shade)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), outward) < 0f)
            {
                // Swapping the two off-diagonal corners reverses the winding and keeps the shape.
                Vector3 tv = b; b = c; c = tv;
                Vector2 tu = uvB; uvB = uvC; uvC = tu;
            }

            AddTriangle(a, c, b, uvA, uvC, uvB, Vector2.one, Vector2.one, Vector2.one, slot, shade);
            AddTriangle(b, c, d, uvB, uvC, uvD, Vector2.one, Vector2.one, Vector2.one, slot, shade);
        }

        /// <summary>As above, with the across-the-path distance recorded in UV1.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD,
                            Vector2 edgeA, Vector2 edgeB, Vector2 edgeC, Vector2 edgeD,
                            Vector3 outward, PathSlot slot, float shade)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, b - a), outward) < 0f)
            {
                Vector3 tv = b; b = c; c = tv;
                Vector2 tu = uvB; uvB = uvC; uvC = tu;
                Vector2 te = edgeB; edgeB = edgeC; edgeC = te;
            }

            AddTriangle(a, c, b, uvA, uvC, uvB, edgeA, edgeC, edgeB, slot, shade);
            AddTriangle(b, c, d, uvB, uvC, uvD, edgeB, edgeC, edgeD, slot, shade);
        }

        /// <summary>
        /// Rewrites every UV as the vertex's place across the path's own footprint, so the asset is
        /// covered exactly once in 0-1. Only wanted by shaders that treat UV as a mask rather than
        /// as a tiling coordinate.
        /// </summary>
        public void NormalizeUVs()
        {
            if (Vertices.Count == 0) return;

            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vector3 v = Vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }

            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            if (spanX < 1e-6f) spanX = 1f;
            if (spanZ < 1e-6f) spanZ = 1f;

            for (int i = 0; i < Vertices.Count; i++)
            {
                Vector3 v = Vertices[i];
                UVs[i] = new Vector2((v.x - minX) / spanX, (v.z - minZ) / spanZ);
            }
        }

        /// <summary>Replaces every UV with a planar world projection, tiling every uvScale metres.</summary>
        public void WorldPlanarUVs()
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vector3 p = Vertices[i];
                Vector3 n = Normals[i];

                float ax = n.x < 0f ? -n.x : n.x;
                float ay = n.y < 0f ? -n.y : n.y;
                float az = n.z < 0f ? -n.z : n.z;

                float u, v;
                if (ay >= ax && ay >= az) { u = p.x; v = p.z; }
                else if (ax >= az) { u = p.z; v = p.y; }
                else { u = p.x; v = p.y; }

                UVs[i] = new Vector2(u / _uvScale, v / _uvScale);
            }
        }

        static bool TryNormal(Vector3 a, Vector3 b, Vector3 c, out Vector3 n)
        {
            n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return false; // degenerate, skip rather than emit NaN normals
            n /= len;
            return true;
        }

        static Color Shade(PathSlot slot, float shade)
        {
            Color tint = TintFor(slot);
            return new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        }

        void Push(Vector3 p, Vector3 n, Color c, Vector2 uv, Vector2 edge)
        {
            Vertices.Add(p);
            Normals.Add(n);
            Colors.Add(c);
            UVs.Add(uv);
            UV1.Add(edge);
        }
    }
}
