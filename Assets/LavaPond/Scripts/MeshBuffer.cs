using System.Collections.Generic;
using UnityEngine;

namespace LavaPond
{
    /// <summary>Material slots on the generated pond, in submesh order.</summary>
    public enum LavaSlot
    {
        /// <summary>Cooled basalt crust: the dark plates floating on the surface.</summary>
        CrustDark = 0,

        /// <summary>Crust that has not finished cooling. Broken edges and the ground nearest the heat.</summary>
        CrustWarm = 1,

        /// <summary>Molten rock. Everything in this slot wants an emissive material.</summary>
        Molten = 2,

        /// <summary>Dead rock: the rim, the boulders and the underside of the block.</summary>
        Rock = 3
    }

    /// <summary>
    /// Where the vent ended up, in the mesh's local space. Gameplay needs this to know where to
    /// hang particles, light and damage volumes, so it comes back with the mesh rather than being
    /// re-derived.
    /// </summary>
    public struct VentInfo
    {
        /// <summary>False when the pond has no vent.</summary>
        public bool Exists;

        /// <summary>Centre of the molten mouth, at the surface of the pool inside the cone.</summary>
        public Vector3 Mouth;

        /// <summary>Average radius of the mouth.</summary>
        public float Radius;

        /// <summary>How far the cone stands above the crust.</summary>
        public float Height;
    }

    /// <summary>
    /// Accumulates flat-shaded triangles. Every triangle gets its own three vertices so each face
    /// keeps a hard normal, which is the whole point of the low-poly look. UVs are projected on the
    /// dominant axis of the face normal and vertex colours carry a per-face shade for anyone who
    /// wants to drive a vertex-colour shader instead of the supplied materials.
    ///
    /// UV1 carries the same per-vertex flow data a Lava Flow writes, so the two packages can share
    /// a lava shader: x is a scroll-speed multiplier and y is how far the vertex is from the edge
    /// of the lava, 0 at the shore and 1 out in the open. Without it, a shader reading TEXCOORD1
    /// sees (0, 0) — every vertex claiming to be at the bank — and its bank crust covers the whole
    /// pond rather than ringing it.
    /// </summary>
    public class MeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Vector2> UV1 = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int>[] Submeshes;

        /// <summary>Where the vent ended up. <c>Exists</c> is false on a pond without one.</summary>
        public VentInfo Vent;

        /// <summary>Area inside the shoreline seen from above, in square metres of local space.</summary>
        public float PondArea;

        /// <summary>
        /// How much of that the crust plates cover, seen from the same place. Measured while the
        /// plates are laid down rather than read back off the finished mesh, because the molten
        /// sheet runs unbroken from shore to shore underneath them: from outside there is no way
        /// to tell the lava you can see from the lava a plate is sitting on.
        /// </summary>
        public float CrustArea;

        /// <summary>Fraction of the pond that has skinned over, 0 to 1.</summary>
        public float CrustCoverage
        {
            get { return PondArea < 1e-4f ? 0f : Mathf.Clamp01(CrustArea / PondArea); }
        }

        /// <summary>
        /// How far a point is from the edge of the lava: 0 on the shoreline, 1 out in the open pool.
        /// Set by the builder, which is the only thing that knows where the shore runs. Left null it
        /// writes 1 everywhere, which reads as "all open lava" rather than as "all bank".
        /// </summary>
        public System.Func<Vector3, float> Bank;

        readonly float _uvScale;

        /// <summary>Sine and cosine of the flow direction, so the top-down projection can be turned
        /// to face it without doing the trigonometry once per vertex.</summary>
        readonly float _flowSin;
        readonly float _flowCos;

        public MeshBuffer(int submeshCount, float uvScale, float flowAngleDegrees = 0f)
        {
            Submeshes = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) Submeshes[i] = new List<int>();
            _uvScale = uvScale <= 0f ? 1f : uvScale;

            float rad = flowAngleDegrees * Mathf.Deg2Rad;
            _flowSin = Mathf.Sin(rad);
            _flowCos = Mathf.Cos(rad);
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

        /// <summary>Adds one flat-shaded triangle. <paramref name="shade"/> multiplies the vertex colour.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, LavaSlot slot, float shade)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-9f) return; // degenerate, skip rather than emit NaN normals
            n /= len;

            Color tint = TintFor(slot);
            Color col = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);

            // Molten faces are always projected from above, whichever way they point. The open lava
            // is flat, so it takes the top-down axis anyway; a bubble's flank or the inside of a
            // crack does not, and the moment the dominant axis flips, that face gets the pattern at
            // a different scale, a different angle and — for a scrolling lava material — often a
            // reversed direction, which reads as a still, speckled patch sitting on moving lava.
            // Projecting the whole slot from above keeps one continuous surface. It is the same
            // choice NormalizeUVs already makes for every face it touches.
            bool fromAbove = slot == LavaSlot.Molten;

            int baseIndex = Vertices.Count;
            Push(a, n, col, fromAbove);
            Push(b, n, col, fromAbove);
            Push(c, n, col, fromAbove);

            List<int> tris = Submeshes[(int)slot];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
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
        /// Adds the two triangles of a quad. Corners are named by their grid position:
        /// <paramref name="a"/> = (i, j), <paramref name="b"/> = (i, j+1),
        /// <paramref name="c"/> = (i+1, j), <paramref name="d"/> = (i+1, j+1).
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, LavaSlot slot, float shade, bool flipDiagonal = false)
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

        /// <summary>
        /// Rewrites every UV as the vertex's place across the pond's own footprint, so the asset is
        /// covered exactly once in 0-1.
        ///
        /// The default projection is in world units and runs well outside 0-1 on anything but a
        /// tiny pond. That is fine for a texture that tiles, and wrong for a shader that treats UV
        /// as a mask: Shader Graph's Remap does not clamp, so a UV of 3 where the graph expected 1
        /// comes out the far side as an enormous colour and the surface renders pure white.
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

            // Projected from above for every face, including the walls. A wall is a thin sliver
            // seen edge-on, so giving it its own axis buys nothing and would only put a seam where
            // the mask jumps.
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vector3 v = Vertices[i];
                UVs[i] = new Vector2((v.x - minX) / spanX, (v.z - minZ) / spanZ);
            }
        }

        void Push(Vector3 p, Vector3 n, Color c, bool fromAbove)
        {
            Vertices.Add(p);
            Normals.Add(n);
            Colors.Add(c);
            UVs.Add(fromAbove ? FlowProject(p) : Project(p, n));

            // x is the scroll-speed multiplier the flow package writes. A pond does not travel, so
            // it is a flat 1: the shader runs one rate over the whole surface anyway.
            UV1.Add(new Vector2(1f, Bank != null ? Mathf.Clamp01(Bank(p)) : 1f));
        }

        /// <summary>
        /// Top-down projection, turned to face the flow direction: V measures distance along the way
        /// the lava is travelling and U measures across it. At an angle of zero this is plain
        /// (x, z), which is what it always was.
        ///
        /// A scrolling material runs its pattern down V, so this is the whole of what makes a pool
        /// read as fed by the river beside it rather than as drifting off along some world axis.
        /// </summary>
        Vector2 FlowProject(Vector3 p)
        {
            float u = p.x * _flowCos - p.z * _flowSin;
            float v = p.x * _flowSin + p.z * _flowCos;
            return new Vector2(u / _uvScale, v / _uvScale);
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
