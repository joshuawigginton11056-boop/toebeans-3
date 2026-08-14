using System.Collections.Generic;
using UnityEngine;

namespace LavaFlow
{
    /// <summary>Material slots on the generated flow, in submesh order. Matches LavaPond's order,
    /// so the same four materials can be dropped on both.</summary>
    public enum LavaSlot
    {
        /// <summary>Cooled basalt crust: the dark plates rafting down the channel.</summary>
        CrustDark = 0,

        /// <summary>Crust that has not finished cooling: plate edges, levee tops nearest the heat.</summary>
        CrustWarm = 1,

        /// <summary>Molten rock. Everything in this slot wants an emissive, scrolling material.</summary>
        Molten = 2,

        /// <summary>Dead rock: the outer banks, the boulders and the buried skirt.</summary>
        Rock = 3
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look.
    ///
    /// Unlike the pond's buffer, this one takes explicit UVs for anything on the channel ribbon.
    /// A flow needs its UVs aligned to the direction of travel — U across the channel, V in metres
    /// along it — or a scrolling lava shader has no idea which way "downstream" is. Props that are
    /// not part of the ribbon still fall back to a planar projection.
    ///
    /// UV1 carries per-vertex flow data for the shader: x is a scroll-speed multiplier (fast on the
    /// cascades, slow on the river) and y is how far the vertex sits from the bank, 0 at the levee
    /// and 1 mid-channel.
    /// </summary>
    public class MeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Vector2> UV1 = new List<Vector2>();
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
            new Color(0.16f, 0.14f, 0.15f, 1f), // CrustDark
            new Color(0.52f, 0.20f, 0.09f, 1f), // CrustWarm
            new Color(1.00f, 0.53f, 0.12f, 1f), // Molten
            new Color(0.34f, 0.31f, 0.30f, 1f)  // Rock
        };

        public static Color TintFor(LavaSlot slot)
        {
            int i = (int)slot;
            return (i >= 0 && i < SlotTint.Length) ? SlotTint[i] : Color.white;
        }

        /// <summary>Adds one flat-shaded triangle with planar-projected UVs. For props.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, LavaSlot slot, float shade)
        {
            Vector3 n;
            if (!TryNormal(a, b, c, out n)) return;

            Color col = Shade(slot, shade);
            int baseIndex = Vertices.Count;
            Push(a, n, col, Project(a, n), Vector2.zero);
            Push(b, n, col, Project(b, n), Vector2.zero);
            Push(c, n, col, Project(c, n), Vector2.zero);
            Emit(slot, baseIndex);
        }

        /// <summary>Adds one flat-shaded triangle with authored UVs. For anything on the ribbon.</summary>
        public void AddTriangleUV(Vector3 a, Vector3 b, Vector3 c,
                                  Vector2 uvA, Vector2 uvB, Vector2 uvC,
                                  Vector2 flowA, Vector2 flowB, Vector2 flowC,
                                  LavaSlot slot, float shade)
        {
            Vector3 n;
            if (!TryNormal(a, b, c, out n)) return;

            Color col = Shade(slot, shade);
            int baseIndex = Vertices.Count;
            Push(a, n, col, uvA, flowA);
            Push(b, n, col, uvB, flowB);
            Push(c, n, col, uvC, flowC);
            Emit(slot, baseIndex);
        }

        /// <summary>
        /// Adds the two triangles of a quad with authored UVs. Corners are named by grid position:
        /// <paramref name="a"/> = (i, j), <paramref name="b"/> = (i, j+1),
        /// <paramref name="c"/> = (i+1, j), <paramref name="d"/> = (i+1, j+1).
        ///
        /// The quad faces along <c>Cross(c - a, b - a)</c>. For the flow's ribbon, where b is one
        /// step across the channel and c is one step downstream, that works out as up out of the
        /// surface. Swap b and c to flip it.
        /// </summary>
        public void AddQuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                              Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD,
                              Vector2 flowA, Vector2 flowB, Vector2 flowC, Vector2 flowD,
                              LavaSlot slot, float shade)
        {
            AddTriangleUV(a, c, b, uvA, uvC, uvB, flowA, flowC, flowB, slot, shade);
            AddTriangleUV(b, c, d, uvB, uvC, uvD, flowB, flowC, flowD, slot, shade);
        }

        /// <summary>Adds the two triangles of a quad with planar-projected UVs. For props.
        /// Faces the same way <see cref="AddQuadUV"/> does.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, LavaSlot slot, float shade)
        {
            AddTriangle(a, c, b, slot, shade);
            AddTriangle(b, c, d, slot, shade);
        }

        /// <summary>Adds a triangle fan around <paramref name="center"/> through <paramref name="ring"/>.</summary>
        public void AddFan(Vector3 center, IList<Vector3> ring, bool faceUp, LavaSlot slot, float shade)
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
        /// A fan with authored UVs, one per ring point, and one flow value for the whole thing.
        ///
        /// Anything in the molten slot needs this rather than <see cref="AddFan"/>, props included.
        /// The planar projection picks its axis per face from that face's normal, so on a dome the
        /// axis changes part way round and the pattern arrives at a different scale and a different
        /// angle on each patch — and on roughly half of them the flow direction comes out reversed,
        /// so a scrolling material runs those patches upstream while the river goes down. Handed the
        /// ribbon's own UVs, a bubble carries the same current as the lava it swelled out of.
        /// </summary>
        public void AddFanUV(Vector3 center, Vector2 centerUV, IList<Vector3> ring, IList<Vector2> ringUV,
                             Vector2 flow, bool faceUp, LavaSlot slot, float shade)
        {
            int n = ring.Count;
            for (int j = 0; j < n; j++)
            {
                int k = (j + 1) % n;
                if (faceUp)
                    AddTriangleUV(center, ring[k], ring[j], centerUV, ringUV[k], ringUV[j],
                                  flow, flow, flow, slot, shade);
                else
                    AddTriangleUV(center, ring[j], ring[k], centerUV, ringUV[j], ringUV[k],
                                  flow, flow, flow, slot, shade);
            }
        }

        /// <summary>
        /// Rewrites every UV as the vertex's place across the flow's own footprint, so the asset is
        /// covered exactly once in 0-1.
        ///
        /// Only wanted by shaders that treat UV as a mask rather than as a tiling coordinate —
        /// <c>Assets/Shaders/Lava/Lava.shadergraph</c> is one: its Remap node does not clamp, so a
        /// UV of 40 where the graph expected 1 comes out the far side as an enormous colour and the
        /// surface renders pure white. It also throws away the flow direction, so a scrolling
        /// material will scroll across the channel rather than down it.
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
                UVs[i] = Project(Vertices[i], Normals[i]);
        }

        static bool TryNormal(Vector3 a, Vector3 b, Vector3 c, out Vector3 n)
        {
            n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return false; // degenerate, skip rather than emit NaN normals
            n /= len;
            return true;
        }

        static Color Shade(LavaSlot slot, float shade)
        {
            Color tint = TintFor(slot);
            return new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        }

        void Emit(LavaSlot slot, int baseIndex)
        {
            List<int> tris = Submeshes[(int)slot];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        void Push(Vector3 p, Vector3 n, Color c, Vector2 uv, Vector2 flow)
        {
            Vertices.Add(p);
            Normals.Add(n);
            Colors.Add(c);
            UVs.Add(uv);
            UV1.Add(flow);
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
