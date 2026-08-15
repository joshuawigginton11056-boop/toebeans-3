using System.Collections.Generic;
using UnityEngine;

namespace CaveTunnel
{
    /// <summary>Material slots on the generated cave, in submesh order.</summary>
    public enum CaveSlot
    {
        /// <summary>Walls, ceiling and the rock lip around each mouth.</summary>
        Rock = 0,

        /// <summary>The drivable floor. Split out so it can take a different surface material.</summary>
        Floor = 1
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look.
    ///
    /// Unlike the frozen lake's buffer this takes explicit UVs rather than projecting on the
    /// dominant axis: a tunnel doubles back on itself, and a planar projection smears badly the
    /// moment the path turns. The builder passes UVs that run around the bore and along the path.
    /// </summary>
    public class CaveMeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int>[] Submeshes;

        /// <summary>Centreline of the finished cave, in local space. Handy for gameplay queries.</summary>
        public readonly List<Vector3> Centerline = new List<Vector3>();

        /// <summary>Length of the centreline in metres.</summary>
        public float Length;

        /// <summary>Solid-body query for the interior. Null when nothing was swept.</summary>
        public CaveVolume Volume;

        public CaveMeshBuffer(int submeshCount)
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

        static readonly Color[] SlotTint =
        {
            new Color(0.55f, 0.56f, 0.60f, 1f), // Rock
            new Color(0.78f, 0.80f, 0.84f, 1f)  // Floor
        };

        public static Color TintFor(CaveSlot slot)
        {
            int i = (int)slot;
            return (i >= 0 && i < SlotTint.Length) ? SlotTint[i] : Color.white;
        }

        /// <summary>Adds one flat-shaded triangle. <paramref name="shade"/> multiplies the vertex colour.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c,
                                Vector2 uvA, Vector2 uvB, Vector2 uvC,
                                CaveSlot slot, float shade)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return; // degenerate, skip rather than emit NaN normals
            n /= len;

            Color tint = TintFor(slot);
            var col = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);

            int baseIndex = Vertices.Count;
            Push(a, n, uvA, col);
            Push(b, n, uvB, col);
            Push(c, n, uvC, col);

            List<int> tris = Submeshes[(int)slot];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        /// <summary>
        /// Adds the two triangles of a quad. Corners are named by their grid position:
        /// <paramref name="a"/> = (ring, j), <paramref name="b"/> = (ring, j+1),
        /// <paramref name="c"/> = (ring+1, j), <paramref name="d"/> = (ring+1, j+1).
        /// Winding is chosen so the face looks back towards the centreline.
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD,
                            CaveSlot slot, float shade, bool flip)
        {
            if (flip)
            {
                AddTriangle(a, b, c, uvA, uvB, uvC, slot, shade);
                AddTriangle(b, d, c, uvB, uvD, uvC, slot, shade);
            }
            else
            {
                AddTriangle(a, c, b, uvA, uvC, uvB, slot, shade);
                AddTriangle(b, c, d, uvB, uvC, uvD, slot, shade);
            }
        }

        void Push(Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            Vertices.Add(p);
            Normals.Add(n);
            UVs.Add(uv);
            Colors.Add(c);
        }
    }
}
