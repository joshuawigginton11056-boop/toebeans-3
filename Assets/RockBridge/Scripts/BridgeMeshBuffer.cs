using System.Collections.Generic;
using UnityEngine;

namespace RockBridge
{
    /// <summary>Material slots on the generated bridge, in submesh order.</summary>
    public enum BridgeSlot
    {
        /// <summary>The driving surface. This is the one whose material you will change most.</summary>
        Deck = 0,

        /// <summary>The flush margin down either edge of the deck. Drivable, just a different stone.</summary>
        Verge = 1,

        /// <summary>The parapet walls — inside faces, tops and outside faces.</summary>
        Parapet = 2,

        /// <summary>
        /// Everything underneath: the fascia down the sides, the soffit, the legs and the landing
        /// fill. All one slot on purpose — it is all the same rock, and a bridge with its legs on a
        /// different material to its underside looks assembled rather than carved.
        /// </summary>
        Rock = 3
    }

    /// <summary>
    /// Accumulates the bridge mesh.
    ///
    /// It has to do two opposite things at once, which is why there are two ways to add a triangle:
    ///
    /// <b>The deck welds and smooths.</b> A faceted driving surface is not a stylistic choice at
    /// racing speed, it is a surface that flickers as the light moves across it — and the mesh is
    /// the collider here, so a facet is also a bump. Each run of the cross-section is emitted as a
    /// welded grid with normals averaged from its own faces, and hard edges appear only where two
    /// runs meet, which is exactly where a real edge is.
    ///
    /// <b>The rock facets.</b> The legs and the landing fill want the flat-shaded low-poly look the
    /// rest of this map is built in, so <see cref="AddFlatTriangle"/> gives every triangle its own
    /// three vertices and its own normal. Nothing drives on them, so nothing is lost.
    ///
    /// Winding is never chosen by hand. Both adders take the direction the face is meant to point
    /// and pick the order themselves, because a wrongly wound surface in Unity does not look wrong,
    /// it disappears — and a surface that disappeared would still pass every count, bounds and NaN
    /// check in the harness.
    ///
    /// Pure managed maths throughout — no scene objects, no asset loading, no native Unity calls —
    /// so the whole thing runs in the headless harness.
    /// </summary>
    public class BridgeMeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<int>[] Submeshes;

        /// <summary>Length of the crossing this mesh was built along, in metres.</summary>
        public float Length;

        /// <summary>Legs actually built. Zero on a bridge lying on the ground is normal.</summary>
        public int PierCount;

        /// <summary>
        /// How many metres of the crossing the landing fill actually covered.
        ///
        /// Reported because the fill is bounded by a depth rather than by a distance, and on a
        /// bridge that flies lower than <see cref="BridgeSettings.abutmentDepth"/> the whole way,
        /// that boundary never triggers — the "landings" quietly become two continuous walls
        /// running the length of the deck. It is obvious in the scene and invisible in the
        /// settings, so the number is worth surfacing.
        /// </summary>
        public float FillLength;

        /// <summary>Length of the longest leg, in metres.</summary>
        public float TallestPier;

        /// <summary>Triangles skipped for being degenerate. Non-zero is a hint that something is
        /// collapsed — a zero-height parapet, or a corner folding in on itself.</summary>
        public int DegenerateTriangles;

        /// <summary>
        /// Narrowest and widest the driving surface actually came out, in metres, measured between
        /// the emitted edge vertices of every cross-section. These are what answer "does it pinch
        /// anywhere" — the settings say what was asked for, these say what was built.
        /// </summary>
        public float MinDeckWidth = float.PositiveInfinity;
        public float MaxDeckWidth;

        public BridgeMeshBuffer(int submeshCount)
        {
            Submeshes = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) Submeshes[i] = new List<int>();
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

        public int TriangleCountIn(BridgeSlot slot)
        {
            return Submeshes[(int)slot].Count / 3;
        }

        /// <summary>Adds a vertex with a zero normal, to be filled in by the faces that use it.</summary>
        public int AddVertex(Vector3 position, Vector2 uv)
        {
            Vertices.Add(position);
            Normals.Add(Vector3.zero);
            UVs.Add(uv);
            return Vertices.Count - 1;
        }

        /// <summary>
        /// Adds one triangle over existing vertices, wound so that it faces
        /// <paramref name="facing"/>, and folds its normal into the three vertices it uses. Use this
        /// for anything that should come out smooth.
        /// </summary>
        public void AddTriangleFacing(int i0, int i1, int i2, Vector3 facing, BridgeSlot slot)
        {
            Vector3 a = Vertices[i0];
            Vector3 n = Vector3.Cross(Vertices[i1] - a, Vertices[i2] - a);

            float len = n.magnitude;
            if (len < 1e-10f) { DegenerateTriangles++; return; }
            n /= len;

            if (Vector3.Dot(n, facing) < 0f)
            {
                // Swapping two corners flips the face and keeps the shape.
                int swap = i1; i1 = i2; i2 = swap;
                n = -n;
            }

            List<int> tris = Submeshes[(int)slot];
            tris.Add(i0);
            tris.Add(i1);
            tris.Add(i2);

            Normals[i0] += n;
            Normals[i1] += n;
            Normals[i2] += n;
        }

        /// <summary>
        /// Adds the two triangles of a quad over existing vertices, both facing
        /// <paramref name="facing"/>. Corners are named by grid position: <paramref name="a"/> =
        /// (row, col), <paramref name="b"/> = (row, col+1), <paramref name="c"/> = (row+1, col),
        /// <paramref name="d"/> = (row+1, col+1).
        /// </summary>
        public void AddQuadFacing(int a, int b, int c, int d, Vector3 facing, BridgeSlot slot)
        {
            AddTriangleFacing(a, b, c, facing, slot);
            AddTriangleFacing(b, d, c, facing, slot);
        }

        /// <summary>
        /// Adds a triangle with three vertices of its own and one hard normal — a facet.
        ///
        /// <paramref name="facing"/> only has to be roughly right; it picks the winding, and the
        /// normal that gets stored is the triangle's own. Passing the outward direction of the whole
        /// solid is the usual way to call this, which is why a lumpy rock face can be built from a
        /// single "outwards" without working out each facet's own normal first.
        /// </summary>
        public void AddFlatTriangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 uv0, Vector2 uv1,
                                    Vector2 uv2, Vector3 facing, BridgeSlot slot)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            float len = n.magnitude;
            if (len < 1e-10f) { DegenerateTriangles++; return; }
            n /= len;

            if (Vector3.Dot(n, facing) < 0f)
            {
                Vector3 swap = p1; p1 = p2; p2 = swap;
                Vector2 swapUv = uv1; uv1 = uv2; uv2 = swapUv;
                n = -n;
            }

            int i0 = Vertices.Count;
            Vertices.Add(p0); Vertices.Add(p1); Vertices.Add(p2);
            Normals.Add(n); Normals.Add(n); Normals.Add(n);
            UVs.Add(uv0); UVs.Add(uv1); UVs.Add(uv2);

            List<int> tris = Submeshes[(int)slot];
            tris.Add(i0);
            tris.Add(i0 + 1);
            tris.Add(i0 + 2);
        }

        /// <summary>
        /// A faceted quad, as two independent facets. Deliberately not planarised first: a rock face
        /// whose four corners do not lie in a plane should break into two visible facets, which is
        /// the whole look.
        /// </summary>
        public void AddFlatQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3,
                                Vector3 facing, BridgeSlot slot)
        {
            AddFlatTriangle(p0, p1, p2, uv0, uv1, uv2, facing, slot);
            AddFlatTriangle(p0, p2, p3, uv0, uv2, uv3, facing, slot);
        }

        /// <summary>Normalises every accumulated normal. Call once, after all faces are in.</summary>
        public void NormaliseNormals()
        {
            for (int i = 0; i < Normals.Count; i++)
            {
                Vector3 n = Normals[i];
                float len = n.magnitude;
                Normals[i] = len > 1e-6f ? n / len : Vector3.up;
            }
        }
    }
}
