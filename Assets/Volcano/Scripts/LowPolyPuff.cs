using System.Collections.Generic;
using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// Builds the faceted blob used as the particle for smoke and mist.
    ///
    /// The usual way to do smoke is a soft billboard with a cloud texture, and next to a mountain
    /// made of flat triangles it looks like a photograph pasted onto a model. A lumpy solid, flat
    /// shaded and rendered as a mesh particle, is the same silhouette language as everything else
    /// on the map, and it turns and tumbles properly because it is actually three dimensional.
    /// </summary>
    public static class LowPolyPuff
    {
        /// <summary>
        /// A lumpy sphere. <paramref name="subdivisions"/> 0 gives the bare 20-face icosahedron,
        /// which is as low as this can go and still read as a cloud; 1 gives 80 faces and is the
        /// most that is worth paying for on something this transparent.
        /// </summary>
        public static Mesh Build(int seed, int subdivisions = 1, float lumpiness = 0.28f)
        {
            List<Vector3> verts;
            List<int> tris;
            Icosahedron(out verts, out tris);

            for (int i = 0; i < Mathf.Clamp(subdivisions, 0, 3); i++) Subdivide(ref verts, ref tris);

            // Push each vertex in or out along its own direction. Doing it per vertex rather than
            // per face keeps the surface closed, so the blob never shows a hole when it turns.
            var rng = new Rng(seed);
            for (int i = 0; i < verts.Count; i++)
                verts[i] = verts[i].normalized * (1f + rng.Signed(lumpiness));

            return Flatten(verts, tris, "LowPolyPuff_" + seed);
        }

        /// <summary>
        /// Splits every vertex out per triangle so each face keeps a hard normal, and makes sure the
        /// faces point outwards. The blob is convex enough for the test to be simply whether the
        /// normal agrees with the direction out of the middle, which beats trusting a hand-written
        /// index list to have the winding this project needs.
        /// </summary>
        static Mesh Flatten(List<Vector3> verts, List<int> tris, string name)
        {
            var outVerts = new List<Vector3>(tris.Count);
            var outNormals = new List<Vector3>(tris.Count);
            var outUVs = new List<Vector2>(tris.Count);
            var outTris = new List<int>(tris.Count);

            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]];
                Vector3 b = verts[tris[i + 1]];
                Vector3 c = verts[tris[i + 2]];

                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;

                if (Vector3.Dot(n, a + b + c) < 0f)
                {
                    Vector3 swap = b;
                    b = c;
                    c = swap;
                    n = Vector3.Cross(b - a, c - a);
                }
                n.Normalize();

                int at = outVerts.Count;
                outVerts.Add(a); outVerts.Add(b); outVerts.Add(c);
                outNormals.Add(n); outNormals.Add(n); outNormals.Add(n);

                // Nothing here samples a texture, but a particle shader will still read UV0 and an
                // unset one is a pile of zeroes at a corner of the atlas.
                outUVs.Add(new Vector2(0f, 0f));
                outUVs.Add(new Vector2(1f, 0f));
                outUVs.Add(new Vector2(0.5f, 1f));

                outTris.Add(at); outTris.Add(at + 1); outTris.Add(at + 2);
            }

            var mesh = new Mesh();
            mesh.name = name;
            mesh.SetVertices(outVerts);
            mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUVs);
            mesh.SetTriangles(outTris, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Icosahedron(out List<Vector3> verts, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            verts = new List<Vector3>
            {
                new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
                new Vector3(0f, -1f,  t), new Vector3(0f,  1f,  t),
                new Vector3(0f, -1f, -t), new Vector3(0f,  1f, -t),
                new Vector3( t, 0f, -1f), new Vector3( t, 0f,  1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f,  1f)
            };

            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            tris = new List<int>
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,    0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2,  10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,    3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,   8, 6, 7,   9, 8, 1
            };
        }

        static void Subdivide(ref List<Vector3> verts, ref List<int> tris)
        {
            var midpoints = new Dictionary<long, int>();
            var newTris = new List<int>(tris.Count * 4);
            var v = verts;

            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Midpoint(a, b, v, midpoints);
                int bc = Midpoint(b, c, v, midpoints);
                int ca = Midpoint(c, a, v, midpoints);

                newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
            }

            tris = newTris;
        }

        static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            int existing;
            if (cache.TryGetValue(key, out existing)) return existing;

            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            int index = verts.Count - 1;
            cache[key] = index;
            return index;
        }
    }
}
