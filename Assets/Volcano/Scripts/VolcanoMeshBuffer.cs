using System.Collections.Generic;
using UnityEngine;

namespace Volcano
{
    /// <summary>Material slots on the generated volcano, in submesh order.</summary>
    public enum VolcanoSlot
    {
        /// <summary>Cold basalt: the lower flanks, the boulders and the tunnel walls.</summary>
        Rock = 0,

        /// <summary>Ash and scoria: the upper cone and the rim.</summary>
        Ash = 1,

        /// <summary>Scorched rock close to the heat. Wants a dark material with a warm tint.</summary>
        Ember = 2,

        /// <summary>Molten rock: the fissures, the notch floors and the seam in the tunnel.
        /// Everything in this slot wants an emissive material.</summary>
        Molten = 3
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look. UVs are projected on the
    /// dominant axis of the face normal and vertex colours carry a per-face shade, for anyone who
    /// would rather drive a vertex-colour shader than the supplied materials.
    ///
    /// This is the same buffer the Lava Pond and Lava Flow packages use, with the addition of
    /// <see cref="AddPolygon"/>, which the passage cutting needs: clipping a triangle against the
    /// tunnel leaves convex polygons of three to eight corners rather than triangles.
    /// </summary>
    public class VolcanoMeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int>[] Submeshes;

        readonly float _uvScale;

        public VolcanoMeshBuffer(int submeshCount, float uvScale)
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
            new Color(0.30f, 0.28f, 0.29f, 1f), // Rock
            new Color(0.38f, 0.33f, 0.31f, 1f), // Ash
            new Color(0.42f, 0.20f, 0.12f, 1f), // Ember
            new Color(1.00f, 0.50f, 0.11f, 1f)  // Molten
        };

        public static Color TintFor(VolcanoSlot slot)
        {
            int i = (int)slot;
            return (i >= 0 && i < SlotTint.Length) ? SlotTint[i] : Color.white;
        }

        /// <summary>Adds one flat-shaded triangle. <paramref name="shade"/> multiplies the vertex colour.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, VolcanoSlot slot, float shade)
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

        /// <summary>
        /// Adds a convex polygon as a triangle fan, keeping its winding. Silently ignores anything
        /// with fewer than three corners, which is what clipping hands back most of the time.
        /// </summary>
        public void AddPolygon(List<Vector3> poly, VolcanoSlot slot, float shade)
        {
            if (poly == null || poly.Count < 3) return;
            for (int i = 1; i < poly.Count - 1; i++)
                AddTriangle(poly[0], poly[i], poly[i + 1], slot, shade);
        }

        /// <summary>Adds a triangle fan around <paramref name="center"/> through <paramref name="ring"/>.</summary>
        public void AddFan(Vector3 center, IList<Vector3> ring, bool faceUp, VolcanoSlot slot, float shade)
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
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, VolcanoSlot slot, float shade)
        {
            AddTriangle(a, b, c, slot, shade);
            AddTriangle(b, d, c, slot, shade);
        }

        /// <summary>
        /// Rewrites every UV as the vertex's place across the mountain's own footprint, so the asset
        /// is covered exactly once in 0-1.
        ///
        /// The default projection is in world units and runs far outside 0-1 on anything this size.
        /// That is fine for a texture that tiles and wrong for a shader that treats UV as a mask:
        /// Shader Graph's Remap does not clamp, so a UV of 11 where the graph expected 1 comes out
        /// the far side as an enormous colour and the surface renders pure white.
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

            // Guard the degenerate case rather than dividing by zero and filling the mesh with NaN.
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
