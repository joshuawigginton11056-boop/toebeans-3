using System.Collections.Generic;
using UnityEngine;

namespace FrozenLake
{
    /// <summary>Material slots on the generated lake, in submesh order.</summary>
    public enum LakeSlot
    {
        IcePale = 0,
        IceDeep = 1,
        Snow = 2,
        Rock = 3
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look. UVs are projected on the
    /// dominant axis of the face normal and vertex colours carry a per-face shade for anyone who
    /// wants to drive a vertex-colour shader instead of the supplied materials.
    /// </summary>
    public class MeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int>[] Submeshes;

        readonly float _uvScale;

        public MeshBuffer(int submeshCount, float uvScale)
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
            new Color(0.72f, 0.85f, 0.93f, 1f), // IcePale
            new Color(0.36f, 0.60f, 0.76f, 1f), // IceDeep
            new Color(0.95f, 0.97f, 1.00f, 1f), // Snow
            new Color(0.42f, 0.44f, 0.48f, 1f)  // Rock
        };

        public static Color TintFor(LakeSlot slot)
        {
            int i = (int)slot;
            return (i >= 0 && i < SlotTint.Length) ? SlotTint[i] : Color.white;
        }

        /// <summary>Adds one flat-shaded triangle. <paramref name="shade"/> multiplies the vertex colour.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, LakeSlot slot, float shade)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return; // degenerate, skip rather than emit NaN normals
            n /= len;

            Color tint = TintFor(slot);
            Color col = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);

            int baseIndex = Vertices.Count;
            Push(a, n, col);
            Push(b, n, col);
            Push(c, n, col);

            List<int> tris = Submeshes[(int)slot];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        /// <summary>Adds a triangle fan around <paramref name="center"/> through <paramref name="ring"/>.</summary>
        public void AddFan(Vector3 center, IList<Vector3> ring, bool faceUp, LakeSlot slot, float shade)
        {
            int n = ring.Count;
            for (int j = 0; j < n; j++)
            {
                Vector3 p0 = ring[j];
                Vector3 p1 = ring[(j + 1) % n];
                if (faceUp) AddTriangle(center, p1, p0, slot, shade);
                else AddTriangle(center, p0, p1, slot, shade);
            }
        }

        /// <summary>
        /// Adds the two triangles of a quad. Corners are named by their grid position:
        /// <paramref name="a"/> = (i, j), <paramref name="b"/> = (i, j+1),
        /// <paramref name="c"/> = (i+1, j), <paramref name="d"/> = (i+1, j+1).
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, LakeSlot slot, float shade, bool flipDiagonal = false)
        {
            if (flipDiagonal)
            {
                AddTriangle(a, b, d, slot, shade);
                AddTriangle(a, d, c, slot, shade);
            }
            else
            {
                AddTriangle(a, b, c, slot, shade);
                AddTriangle(b, d, c, slot, shade);
            }
        }

        void Push(Vector3 p, Vector3 n, Color c)
        {
            Vertices.Add(p);
            Normals.Add(n);
            Colors.Add(c);
            UVs.Add(Project(p, n));
        }

        /// <summary>Planar projection on whichever axis the face normal points along most strongly.</summary>
        Vector2 Project(Vector3 p, Vector3 n)
        {
            float ax = n.x < 0f ? -n.x : n.x;
            float ay = n.y < 0f ? -n.y : n.y;
            float az = n.z < 0f ? -n.z : n.z;

            float u, v;
            if (ay >= ax && ay >= az) { u = p.x; v = p.z; }
            else if (ax >= az) { u = p.z; v = p.y; }
            else { u = p.x; v = p.y; }

            return new Vector2(u / _uvScale, v / _uvScale);
        }
    }
}
