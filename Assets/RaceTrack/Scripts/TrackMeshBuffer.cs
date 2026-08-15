using System.Collections.Generic;
using UnityEngine;

namespace RaceTrack
{
    /// <summary>Material slots on the generated track, in submesh order.</summary>
    public enum TrackSlot
    {
        /// <summary>The racing surface. This is the one whose texture you will change most.</summary>
        Road = 0,

        /// <summary>The rumble strips down either edge, flush with the road.</summary>
        Kerb = 1,

        /// <summary>The barrier walls, inside faces, tops and outside faces.</summary>
        Wall = 2,

        /// <summary>The underside of the slab and the aprons down to it — what you see from below.</summary>
        Underside = 3
    }

    /// <summary>
    /// Accumulates the track mesh.
    ///
    /// Unlike this project's rock and lava buffers this one welds and smooths rather than giving
    /// every triangle its own vertices. A faceted driving surface is not a stylistic choice at
    /// racing speed, it is a surface that flickers as the light moves across it, and the collider
    /// would inherit the same facets. So each run of the cross-section — the road, one kerb, the
    /// inside of a barrier — is emitted as a welded grid with normals averaged from its own faces,
    /// and hard edges appear only where two runs meet, which is exactly where a real edge is.
    ///
    /// Winding is never chosen by hand. <see cref="AddTriangleFacing"/> takes the direction the face
    /// is meant to point and picks the order itself, because a wrongly wound surface in Unity does
    /// not look wrong, it disappears — and a surface that disappeared would still pass every count,
    /// bounds and NaN check in the harness.
    /// </summary>
    public class TrackMeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<int>[] Submeshes;

        /// <summary>Length of the racing line this mesh was built along, in metres.</summary>
        public float Length;

        /// <summary>Triangles skipped for being degenerate. Non-zero is a hint that something is
        /// collapsed — a zero-height barrier, or a corner folding in on itself.</summary>
        public int DegenerateTriangles;

        /// <summary>
        /// Narrowest and widest the racing surface actually came out, in metres, measured between
        /// the emitted edge vertices of every cross-section. These are what answer "does it pinch
        /// anywhere" — the settings say what was asked for, these say what was built.
        /// </summary>
        public float MinRoadWidth = float.PositiveInfinity;
        public float MaxRoadWidth;

        public TrackMeshBuffer(int submeshCount)
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

        public int TriangleCountIn(TrackSlot slot)
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
        /// Adds one triangle, wound so that it faces <paramref name="facing"/>, and folds its normal
        /// into the three vertices it uses.
        /// </summary>
        public void AddTriangleFacing(int i0, int i1, int i2, Vector3 facing, TrackSlot slot)
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
        /// Adds the two triangles of a quad, both facing <paramref name="facing"/>. Corners are named
        /// by grid position: <paramref name="a"/> = (row, col), <paramref name="b"/> = (row, col+1),
        /// <paramref name="c"/> = (row+1, col), <paramref name="d"/> = (row+1, col+1).
        /// </summary>
        public void AddQuadFacing(int a, int b, int c, int d, Vector3 facing, TrackSlot slot)
        {
            AddTriangleFacing(a, b, c, facing, slot);
            AddTriangleFacing(b, d, c, facing, slot);
        }

        /// <summary>
        /// Gives two vertices the sum of both their normals. Used to weld the seam of a closed lap,
        /// where the last row of a run and the first row are the same place in space but different
        /// rows in the grid — without this the start line gets a faint lighting crack across it.
        /// </summary>
        public void ShareNormals(int a, int b)
        {
            Vector3 sum = Normals[a] + Normals[b];
            Normals[a] = sum;
            Normals[b] = sum;
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
