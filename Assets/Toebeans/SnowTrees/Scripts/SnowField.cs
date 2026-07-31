using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.SnowTrees
{
    /// <summary>
    /// A coarse signed distance field that snow primitives are smooth-unioned
    /// into, then meshed with naive surface nets.
    /// </summary>
    /// <remarks>
    /// Modelling snow as spheres and capsules and meshing the whole field at
    /// once is what makes drifts merge: two lumps that overlap come out as one
    /// continuous rounded mantle with a soft fillet between them, instead of
    /// two intersecting shells. Surface nets (rather than marching cubes) keeps
    /// it to one vertex per crossed cell and needs no case tables.
    /// </remarks>
    public sealed class SnowField
    {
        const float Unset = 1e6f;

        readonly float _cell;
        readonly Vector3 _min;
        readonly int _nx, _ny, _nz;
        readonly float[] _f;

        public SnowField(Vector3 min, Vector3 max, float cell)
        {
            _cell = Mathf.Max(1e-4f, cell);
            float pad = _cell * 3f;
            _min = min - new Vector3(pad, pad, pad);
            Vector3 size = max - min + new Vector3(pad, pad, pad) * 2f;
            _nx = Mathf.Max(2, Mathf.FloorToInt(size.x / _cell) + 2);
            _ny = Mathf.Max(2, Mathf.FloorToInt(size.y / _cell) + 2);
            _nz = Mathf.Max(2, Mathf.FloorToInt(size.z / _cell) + 2);
            _f = new float[_nx * _ny * _nz];
            for (int i = 0; i < _f.Length; i++)
            {
                _f[i] = Unset;
            }
        }

        public int SampleCount => _f.Length;

        int Index(int i, int j, int k) => (j * _nz + k) * _nx + i;

        Vector3 Position(int i, int j, int k) =>
            new Vector3(_min.x + i * _cell, _min.y + j * _cell, _min.z + k * _cell);

        /// <summary>Polynomial smooth minimum - the fillet where two drifts meet.</summary>
        void Blend(int index, float d, float k)
        {
            float a = _f[index];
            if (a > Unset * 0.5f || k <= 0f)
            {
                _f[index] = Mathf.Min(a, d);
                return;
            }

            float h = Mathf.Max(k - Mathf.Abs(a - d), 0f) / k;
            _f[index] = Mathf.Min(a, d) - h * h * k * 0.25f;
        }

        bool CellRange(Vector3 lo, Vector3 hi, out int i0, out int j0, out int k0,
                       out int i1, out int j1, out int k1)
        {
            i0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - _min.x) / _cell));
            j0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - _min.y) / _cell));
            k0 = Mathf.Max(0, Mathf.FloorToInt((lo.z - _min.z) / _cell));
            i1 = Mathf.Min(_nx - 1, Mathf.FloorToInt((hi.x - _min.x) / _cell) + 1);
            j1 = Mathf.Min(_ny - 1, Mathf.FloorToInt((hi.y - _min.y) / _cell) + 1);
            k1 = Mathf.Min(_nz - 1, Mathf.FloorToInt((hi.z - _min.z) / _cell) + 1);
            return i0 <= i1 && j0 <= j1 && k0 <= k1;
        }

        /// <summary>Ellipsoid lump: <paramref name="scale"/> squashes it flat.</summary>
        public void AddSphere(Vector3 centre, float radius, Vector3 scale, float blend)
        {
            Vector3 extent = new Vector3(radius * scale.x, radius * scale.y, radius * scale.z) +
                             Vector3.one * blend;
            if (!CellRange(centre - extent, centre + extent,
                           out int i0, out int j0, out int k0, out int i1, out int j1, out int k1))
            {
                return;
            }

            float smallest = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            for (int j = j0; j <= j1; j++)
            {
                for (int k = k0; k <= k1; k++)
                {
                    int row = (j * _nz + k) * _nx;
                    for (int i = i0; i <= i1; i++)
                    {
                        Vector3 p = Position(i, j, k) - centre;
                        Vector3 q = new Vector3(p.x / scale.x, p.y / scale.y, p.z / scale.z);
                        Blend(row + i, (q.magnitude - radius) * smallest, blend);
                    }
                }
            }
        }

        /// <summary>Tapered capsule - a drift running out along a bough.</summary>
        public void AddCapsule(Vector3 a, Vector3 b, float ra, float rb, float squash, float blend)
        {
            float rMax = Mathf.Max(ra, rb) + blend;
            Vector3 lo = Vector3.Min(a, b) - Vector3.one * rMax;
            Vector3 hi = Vector3.Max(a, b) + Vector3.one * rMax;
            if (!CellRange(lo, hi, out int i0, out int j0, out int k0,
                           out int i1, out int j1, out int k1))
            {
                return;
            }

            Vector3 ab = b - a;
            float ab2 = Mathf.Max(1e-9f, Vector3.Dot(ab, ab));
            squash = Mathf.Max(0.05f, squash);

            for (int j = j0; j <= j1; j++)
            {
                for (int k = k0; k <= k1; k++)
                {
                    int row = (j * _nz + k) * _nx;
                    for (int i = i0; i <= i1; i++)
                    {
                        Vector3 p = Position(i, j, k);
                        // Squash vertically about the capsule's own height.
                        Vector3 q = new Vector3(p.x, a.y + (p.y - a.y) / squash, p.z);
                        float t = Mathf.Clamp01(Vector3.Dot(q - a, ab) / ab2);
                        float d = (Vector3.Distance(q, a + ab * t) - Mathf.Lerp(ra, rb, t)) * squash;
                        Blend(row + i, d, blend);
                    }
                }
            }
        }

        float Sample(int i, int j, int k) => _f[Index(i, j, k)];

        Vector3 Gradient(int i, int j, int k)
        {
            float gx = Sample(Mathf.Min(i + 1, _nx - 1), j, k) - Sample(Mathf.Max(i - 1, 0), j, k);
            float gy = Sample(i, Mathf.Min(j + 1, _ny - 1), k) - Sample(i, Mathf.Max(j - 1, 0), k);
            float gz = Sample(i, j, Mathf.Min(k + 1, _nz - 1)) - Sample(i, j, Mathf.Max(k - 1, 0));
            Vector3 g = new Vector3(gx, gy, gz);
            return g.sqrMagnitude > 1e-12f ? g.normalized : Vector3.up;
        }

        static readonly int[,] EdgeCorners =
        {
            {0, 1}, {0, 2}, {0, 4}, {1, 3}, {1, 5}, {2, 3},
            {2, 6}, {3, 7}, {4, 5}, {4, 6}, {5, 7}, {6, 7},
        };

        static readonly Vector3[] CornerOffset =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 1), new Vector3(1, 1, 1),
        };

        /// <summary>Meshes the field into <paramref name="mesh"/> as one smooth surface.</summary>
        public void Polygonize(SnowTreeMeshBuilder.MeshScratch mesh, int submesh)
        {
            var cellVertex = new Dictionary<int, int>(4096);
            var values = new float[8];
            var inside = new bool[8];

            for (int j = 0; j < _ny - 1; j++)
            {
                for (int k = 0; k < _nz - 1; k++)
                {
                    for (int i = 0; i < _nx - 1; i++)
                    {
                        int crossings = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 o = CornerOffset[c];
                            values[c] = Sample(i + (int)o.x, j + (int)o.y, k + (int)o.z);
                            inside[c] = values[c] < 0f;
                            if (inside[c])
                            {
                                crossings++;
                            }
                        }

                        if (crossings == 0 || crossings == 8)
                        {
                            continue;
                        }

                        Vector3 sum = Vector3.zero;
                        int hits = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int ea = EdgeCorners[e, 0];
                            int eb = EdgeCorners[e, 1];
                            if (inside[ea] == inside[eb])
                            {
                                continue;
                            }

                            float va = values[ea];
                            float vb = values[eb];
                            float t = Mathf.Approximately(va, vb) ? 0.5f : va / (va - vb);
                            sum += Vector3.Lerp(CornerOffset[ea], CornerOffset[eb], t);
                            hits++;
                        }

                        if (hits == 0)
                        {
                            continue;
                        }

                        Vector3 local = sum / hits;
                        Vector3 p = new Vector3(_min.x + (i + local.x) * _cell,
                                                _min.y + (j + local.y) * _cell,
                                                _min.z + (k + local.z) * _cell);
                        cellVertex[Index(i, j, k)] = mesh.AddVertex(p, Gradient(i, j, k),
                                                                    new Vector2(p.x * 0.35f, p.y * 0.35f));
                    }
                }
            }

            for (int j = 0; j < _ny - 1; j++)
            {
                for (int k = 0; k < _nz - 1; k++)
                {
                    for (int i = 0; i < _nx - 1; i++)
                    {
                        bool solid = Sample(i, j, k) < 0f;

                        if (j > 0 && k > 0 && solid != (Sample(i + 1, j, k) < 0f))
                        {
                            Quad(mesh, submesh, cellVertex,
                                 Index(i, j - 1, k - 1), Index(i, j, k - 1),
                                 Index(i, j, k), Index(i, j - 1, k), solid);
                        }

                        if (i > 0 && k > 0 && j + 1 < _ny && solid != (Sample(i, j + 1, k) < 0f))
                        {
                            Quad(mesh, submesh, cellVertex,
                                 Index(i - 1, j, k - 1), Index(i, j, k - 1),
                                 Index(i, j, k), Index(i - 1, j, k), !solid);
                        }

                        if (i > 0 && j > 0 && k + 1 < _nz && solid != (Sample(i, j, k + 1) < 0f))
                        {
                            Quad(mesh, submesh, cellVertex,
                                 Index(i - 1, j - 1, k), Index(i, j - 1, k),
                                 Index(i, j, k), Index(i - 1, j, k), solid);
                        }
                    }
                }
            }
        }

        static void Quad(SnowTreeMeshBuilder.MeshScratch mesh, int submesh,
                         Dictionary<int, int> cells, int a, int b, int c, int d, bool flip)
        {
            if (!cells.TryGetValue(a, out int va) || !cells.TryGetValue(b, out int vb) ||
                !cells.TryGetValue(c, out int vc) || !cells.TryGetValue(d, out int vd))
            {
                return;
            }

            if (flip)
            {
                mesh.Quad(submesh, va, vb, vc, vd);
            }
            else
            {
                mesh.Quad(submesh, vd, vc, vb, va);
            }
        }
    }
}
