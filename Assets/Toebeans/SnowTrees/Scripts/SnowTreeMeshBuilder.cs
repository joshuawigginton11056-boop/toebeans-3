using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.SnowTrees
{
    /// <summary>
    /// Procedural generator for the snow-laden conifers used across Toebeans 3.
    /// </summary>
    /// <remarks>
    /// Three kinds of geometry make a tree:
    ///
    /// * <b>Wood</b> - swept tubes for the tapered trunk and the roots that
    ///   splay out of the ground.
    /// * <b>Needles</b> - each bough is a thin twig carrying a feathered spray
    ///   of small needle blades, plus tufts that push up through the snow.
    /// * <b>Snow</b> - a flattened shelf lying along each bough with a rim
    ///   curling off its outer end, pushed into one <see cref="SnowField"/>,
    ///   smooth-unioned and meshed in a single pass. Shelves that touch fuse
    ///   with a soft fillet instead of intersecting as separate shells, but
    ///   their height is capped against tier spacing so the dark gaps between
    ///   tiers survive - those gaps are what make the tiers readable.
    ///
    /// Everything is driven by a deterministic LCG, so the same settings always
    /// produce the same mesh - no baked binary asset needed to keep the trees
    /// stable across machines.
    ///
    /// Submeshes: 0 = bark, 1 = foliage, 2 = snow.
    /// </remarks>
    public static class SnowTreeMeshBuilder
    {
        public const int SubmeshBark = 0;
        public const int SubmeshFoliage = 1;
        public const int SubmeshSnow = 2;
        public const int SubmeshCount = 3;

        /// <summary>Small LCG - matched bit for bit by the authoring prototype.</summary>
        public struct Rng
        {
            uint _state;

            public Rng(int seed)
            {
                _state = (uint)seed;
            }

            public float Value()
            {
                _state = _state * 1664525u + 1013904223u;
                return _state / 4294967296f;
            }

            public float Range(float min, float max)
            {
                return min + (max - min) * Value();
            }
        }

        /// <summary>Vertex buffers being filled while a tree is grown.</summary>
        public sealed class MeshScratch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(16384);
            public readonly List<Vector3> Normals = new List<Vector3>(16384);
            public readonly List<Vector2> Uvs = new List<Vector2>(16384);
            public readonly List<int>[] Indices =
            {
                new List<int>(2048), new List<int>(8192), new List<int>(32768),
            };

            public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
            {
                Vertices.Add(p);
                Normals.Add(n);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            public void Tri(int submesh, int a, int b, int c)
            {
                List<int> list = Indices[submesh];
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }

            public void Quad(int submesh, int a, int b, int c, int d)
            {
                Tri(submesh, a, b, c);
                Tri(submesh, a, c, d);
            }

            public int TriangleCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Indices.Length; i++)
                    {
                        n += Indices[i].Count / 3;
                    }

                    return n;
                }
            }
        }

        // ----------------------------------------------------------- helpers
        static void Basis(Vector3 forward, out Vector3 right, out Vector3 up)
        {
            Vector3 f = forward.sqrMagnitude > 1e-12f ? forward.normalized : Vector3.up;
            Vector3 hint = Mathf.Abs(f.y) < 0.95f ? Vector3.up : Vector3.right;
            right = Vector3.Cross(hint, f).normalized;
            up = Vector3.Cross(f, right);
        }

        static Vector3 SafeNormal(Vector3 v, Vector3 fallback)
        {
            return v.sqrMagnitude > 1e-12f ? v.normalized : fallback;
        }

        /// <summary>Sweeps a ring of <paramref name="segments"/> verts along a path.</summary>
        static void AddTube(MeshScratch mesh, int submesh, IList<Vector3> path, IList<float> radii,
                            int segments, bool capStart = true, bool capEnd = true)
        {
            int sections = path.Count;
            var rings = new int[sections][];
            float travelled = 0f;

            for (int i = 0; i < sections; i++)
            {
                Vector3 dir = path[Mathf.Min(i + 1, sections - 1)] - path[Mathf.Max(i - 1, 0)];
                Basis(dir, out Vector3 right, out Vector3 up);
                if (i > 0)
                {
                    travelled += Vector3.Distance(path[i - 1], path[i]);
                }

                float r = radii[i];
                var ring = new int[segments];
                for (int s = 0; s < segments; s++)
                {
                    float a = Mathf.PI * 2f * s / segments;
                    Vector3 outward = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    ring[s] = mesh.AddVertex(path[i] + outward * r, outward,
                                             new Vector2((float)s / segments, travelled));
                }

                rings[i] = ring;
            }

            for (int i = 0; i < sections - 1; i++)
            {
                int[] a = rings[i];
                int[] b = rings[i + 1];
                for (int s = 0; s < segments; s++)
                {
                    int n = (s + 1) % segments;
                    mesh.Quad(submesh, a[s], a[n], b[n], b[s]);
                }
            }

            if (capStart)
            {
                Vector3 n = SafeNormal(path[0] - path[Mathf.Min(1, sections - 1)], Vector3.down);
                int centre = mesh.AddVertex(path[0], n, new Vector2(0.5f, 0f));
                for (int s = 0; s < segments; s++)
                {
                    mesh.Tri(submesh, centre, rings[0][(s + 1) % segments], rings[0][s]);
                }
            }

            if (capEnd)
            {
                Vector3 n = SafeNormal(path[sections - 1] - path[Mathf.Max(0, sections - 2)], Vector3.up);
                int centre = mesh.AddVertex(path[sections - 1], n, new Vector2(0.5f, travelled));
                for (int s = 0; s < segments; s++)
                {
                    mesh.Tri(submesh, centre, rings[sections - 1][s],
                             rings[sections - 1][(s + 1) % segments]);
                }
            }
        }

        /// <summary>
        /// One needle blade, emitted twice with opposing windings and normals so
        /// it lights correctly from either side under backface culling.
        /// </summary>
        static void AddNeedle(MeshScratch mesh, Vector3 root, Vector3 direction, Vector3 side,
                              float length, float width)
        {
            Vector3 d = SafeNormal(direction, Vector3.up);
            Vector3 s = SafeNormal(side, Vector3.right);
            Vector3 tip = root + d * length;
            Vector3 a = root + s * (width * 0.5f);
            Vector3 b = root - s * (width * 0.5f);
            Vector3 n = SafeNormal(Vector3.Cross(b - a, tip - a), Vector3.up);

            int a0 = mesh.AddVertex(a, n, new Vector2(0f, 0f));
            int b0 = mesh.AddVertex(b, n, new Vector2(1f, 0f));
            int c0 = mesh.AddVertex(tip, n, new Vector2(0.5f, 1f));
            mesh.Tri(SubmeshFoliage, a0, b0, c0);

            int a1 = mesh.AddVertex(a, -n, new Vector2(0f, 0f));
            int b1 = mesh.AddVertex(b, -n, new Vector2(1f, 0f));
            int c1 = mesh.AddVertex(tip, -n, new Vector2(0.5f, 1f));
            mesh.Tri(SubmeshFoliage, b1, a1, c1);
        }

        /// <summary>Feathered spray of needles running the length of a bough.</summary>
        static void AddNeedleSpray(MeshScratch mesh, IList<Vector3> path, ref Rng rng,
                                   float needleLength, float needleWidth, int rows, int perRow,
                                   float droop)
        {
            int sections = path.Count - 1;
            for (int i = 0; i < rows; i++)
            {
                float t = 0.05f + 0.95f * (i / Mathf.Max(1f, rows - 1f));
                int fi = Mathf.Min(sections - 1, Mathf.FloorToInt(t * sections));
                float ft = t * sections - fi;
                Vector3 p = Vector3.Lerp(path[fi], path[fi + 1], ft);
                Vector3 axis = SafeNormal(path[fi + 1] - path[fi], Vector3.forward);
                Vector3 side = SafeNormal(Vector3.Cross(Vector3.up, axis), Vector3.right);
                Vector3 upv = Vector3.Cross(axis, side);
                // Widest at the outer end, so green fringes past the snow rim.
                float taper = 0.55f + 0.75f * t;

                for (int k = 0; k < perRow; k++)
                {
                    float spread = (k + 0.5f) / perRow * 2f - 1f;
                    Vector3 d = axis * rng.Range(0.25f, 0.5f) +
                                side * (spread * rng.Range(1f, 1.35f));
                    // Kept near-horizontal: the frond has to read as a flat pad,
                    // not a bottle brush, or the green looks like fern blades.
                    d += upv * rng.Range(-droop * 0.25f, droop * 0.1f);
                    AddNeedle(mesh, p, d, upv,
                              needleLength * taper * rng.Range(0.8f, 1.25f),
                              needleWidth * taper);
                }
            }
        }

        static Vector3[] BoughPath(Vector3 origin, Vector3 direction, float length, float droop,
                                   int sections)
        {
            Vector3 flat = SafeNormal(new Vector3(direction.x, 0f, direction.z), Vector3.forward);
            var pts = new Vector3[sections];
            for (int i = 0; i < sections; i++)
            {
                float t = (float)i / (sections - 1);
                Vector3 p = origin + flat * (length * t);
                p.y = origin.y + direction.y * length * t - droop * length * t * t;
                pts[i] = p;
            }

            return pts;
        }

        /// <summary>Silhouette radius multiplier at height fraction <paramref name="t"/>.</summary>
        static float Profile(float t, SnowTreeShape shape)
        {
            switch (shape)
            {
                case SnowTreeShape.Steeple:
                    return Mathf.Max(0.13f, Mathf.Pow(1f - t * 0.96f, 1.15f)) *
                           (0.35f + 0.65f * Mathf.Min(1f, t * 5f));
                case SnowTreeShape.Slim:
                    return Mathf.Max(0.13f, 1f - t * 0.93f) *
                           (0.35f + 0.65f * Mathf.Min(1f, t * 4f));
                default:
                    return Mathf.Max(0.14f, Mathf.Pow(1f - t * 0.94f, 0.9f)) *
                           (0.4f + 0.6f * Mathf.Min(1f, t * 6f));
            }
        }

        static Vector3 TrunkAt(IList<Vector3> path, float t)
        {
            float f = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, path.Count - 2);
            return Vector3.Lerp(path[i], path[i + 1], f - i);
        }

        // -------------------------------------------------------------- build
        public static Mesh Build(SnowTreeVariant variant)
        {
            Mesh mesh = Build(SnowTreeSettings.ForVariant(variant));
            mesh.name = variant.AssetName();
            return mesh;
        }

        public static Mesh Build(SnowTreeSettings settings)
        {
            var mesh = new Mesh { name = "SnowTree" };
            Build(settings, mesh);
            return mesh;
        }

        /// <summary>Rebuilds <paramref name="target"/> in place.</summary>
        public static void Build(SnowTreeSettings settings, Mesh target)
        {
            settings = settings.Sanitised();
            var rng = new Rng(settings.seed);
            var m = new MeshScratch();

            float height = settings.height;
            float radius = settings.radius;
            float trunkBaseRadius = radius * 0.1f;
            float trunkTopRadius = trunkBaseRadius * 0.16f;

            // Trunk ------------------------------------------------------
            const int trunkSections = 12;
            var trunkPath = new Vector3[trunkSections];
            var trunkRadii = new float[trunkSections];
            float leanX = rng.Range(-0.03f, 0.03f) * height;
            float leanZ = rng.Range(-0.03f, 0.03f) * height;
            for (int i = 0; i < trunkSections; i++)
            {
                float t = (float)i / (trunkSections - 1);
                trunkPath[i] = new Vector3(leanX * t * t, height * t * 1.005f, leanZ * t * t);
                trunkRadii[i] = trunkBaseRadius * Mathf.Pow(1f - t, 0.75f) + trunkTopRadius;
            }

            AddTube(m, SubmeshBark, trunkPath, trunkRadii, 8);

            // Exposed roots ----------------------------------------------
            for (int i = 0; i < settings.rootCount; i++)
            {
                float a = Mathf.PI * 2f * (i + rng.Range(-0.2f, 0.2f)) / settings.rootCount;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float length = radius * rng.Range(0.4f, 0.68f);
                const int rootSections = 6;
                var rootPath = new Vector3[rootSections];
                var rootRadii = new float[rootSections];
                for (int k = 0; k < rootSections; k++)
                {
                    float t = (float)k / (rootSections - 1);
                    float reach = length * t * (0.55f + 0.45f * t);
                    rootPath[k] = new Vector3(
                        dir.x * reach,
                        trunkBaseRadius * 0.8f - length * 0.5f * t * t +
                        rng.Range(-0.012f, 0.012f) * radius,
                        dir.z * reach);
                    rootRadii[k] = trunkBaseRadius * (0.45f - 0.4f * t) + 0.0025f * radius;
                }

                AddTube(m, SubmeshBark, rootPath, rootRadii, 5);
            }

            // Snow field sized to the crown ------------------------------
            float cell = Mathf.Max(0.01f, radius * settings.snowCellScale);
            var field = new SnowField(new Vector3(-radius * 1.35f, 0f, -radius * 1.35f),
                                      new Vector3(radius * 1.35f, height * 1.12f, radius * 1.35f),
                                      cell);
            float blend = cell * 1f;

            // Tiers of boughs --------------------------------------------
            const float tierHigh = 0.9f;
            float gap = (tierHigh - settings.lowestTier) * height / Mathf.Max(1, settings.tiers - 1);

            for (int ti = 0; ti < settings.tiers; ti++)
            {
                float tt = settings.tiers > 1 ? (float)ti / (settings.tiers - 1) : 0f;
                float t = settings.lowestTier + (tierHigh - settings.lowestTier) * tt;
                Vector3 origin = TrunkAt(trunkPath, t);
                float span = radius * Profile(t, settings.shape) * rng.Range(0.82f, 1.18f);

                int count = Mathf.Max(3, Mathf.RoundToInt(settings.boughsPerTier * (0.62f + 0.38f * (1f - tt))));
                float phase = rng.Value() * Mathf.PI * 2f;

                for (int bi = 0; bi < count; bi++)
                {
                    float a = phase + Mathf.PI * 2f * (bi + rng.Range(-0.25f, 0.25f)) / count;
                    Vector3 dir = new Vector3(Mathf.Cos(a), rng.Range(0.18f, 0.36f), Mathf.Sin(a));
                    // Fuller, droopier skirt on the lowest tiers.
                    float length = span * rng.Range(1f, 1.3f) *
                                   (1f + 0.22f * Mathf.Max(0f, 1f - tt * 4f));
                    float droop = rng.Range(0.55f, 0.8f);
                    Vector3[] bough = BoughPath(origin, dir, length, droop, 5);

                    var twig = new float[5];
                    for (int k = 0; k < 5; k++)
                    {
                        twig[k] = length * 0.028f * (1f - 0.7f * (k / 4f)) + length * 0.004f;
                    }

                    AddTube(m, SubmeshFoliage, bough, twig, 4, capStart: false);
                    AddNeedleSpray(m, bough, ref rng,
                                   needleLength: length * rng.Range(0.1f, 0.15f),
                                   needleWidth: length * 0.055f,
                                   rows: 12, perRow: 7, droop: 0.8f);

                    if (rng.Value() < settings.snowCoverage)
                    {
                        // A shelf of snow lying along the bough: wide, flat, and
                        // never tall enough to close the gap to the tier above -
                        // those dark gaps are what make the tiers readable.
                        Vector3 outward = SafeNormal(new Vector3(dir.x, 0f, dir.z), Vector3.forward);
                        Vector3 inner = Vector3.Lerp(bough[0], bough[1], 0.5f);
                        Vector3 outer = bough[3];
                        float thick = Mathf.Min(length * 0.55f, gap * 0.66f) * settings.snowScale *
                                      (0.55f + 0.45f * (1f - tt)) * rng.Range(0.85f, 1.15f);
                        // Not wider than the bough it sits on, or the upper tiers
                        // wrap the trunk into one smooth tube.
                        float r0 = Mathf.Min(thick * rng.Range(1.7f, 2.1f), length * 0.42f);
                        float r1 = Mathf.Min(thick * rng.Range(1.05f, 1.35f), length * 0.3f);
                        float squash = thick / Mathf.Max(1e-4f, r0);
                        field.AddCapsule(inner + Vector3.up * (thick * 0.55f),
                                         outer + Vector3.up * (thick * 0.45f),
                                         r0, r1, squash, blend);

                        // The rim curling down off the outer end.
                        Vector3 lip = outer + outward * (r1 * 0.35f) -
                                      Vector3.up * (thick * rng.Range(0.25f, 0.6f));
                        field.AddSphere(lip, r1 * rng.Range(0.55f, 0.8f),
                                        new Vector3(1f, rng.Range(0.75f, 1f), 1f), blend);
                    }
                }

                // Needle tufts pushing up through the snow surface.
                int tufts = Mathf.Max(1, count / 5);
                for (int si = 0; si < tufts; si++)
                {
                    float a = phase + Mathf.PI / count +
                              Mathf.PI * 2f * (si + rng.Range(-0.3f, 0.3f)) / tufts;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float offset = span * rng.Range(0.35f, 0.8f);
                    Vector3 p = origin + new Vector3(dir.x * offset,
                                                     span * rng.Range(0.16f, 0.3f),
                                                     dir.z * offset);
                    Vector3 side = SafeNormal(Vector3.Cross(dir, Vector3.up), Vector3.right);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 nd = dir * rng.Range(0.2f, 0.9f) + Vector3.up * rng.Range(0.35f, 1f);
                        nd += side * rng.Range(-0.45f, 0.45f);
                        AddNeedle(m, p, nd, Vector3.Cross(nd, Vector3.up),
                                  span * rng.Range(0.1f, 0.18f), span * 0.04f);
                    }
                }

                // Cushion packed around the trunk at the tier.
                float cushion = Mathf.Min(span * rng.Range(0.34f, 0.46f), gap * 0.55f) *
                                settings.snowScale;
                field.AddSphere(origin + Vector3.up * (cushion * 0.35f), cushion,
                                new Vector3(1f, rng.Range(0.42f, 0.6f), 1f), blend);
            }

            // Crown ------------------------------------------------------
            Vector3 spireBase = TrunkAt(trunkPath, tierHigh - 0.07f);
            Vector3 trunkTip = TrunkAt(trunkPath, 1f);
            Vector3 spireTip = new Vector3(trunkTip.x, trunkTip.y + height * 0.05f, trunkTip.z);
            float crownRadius = radius * Profile(tierHigh - 0.07f, settings.shape);

            const int spireSections = 6;
            var spirePath = new Vector3[spireSections];
            var spireRadii = new float[spireSections];
            for (int i = 0; i < spireSections; i++)
            {
                float t = (float)i / (spireSections - 1);
                spirePath[i] = Vector3.Lerp(spireBase, spireTip, t);
                spireRadii[i] = crownRadius * 0.16f * (1f - t) + crownRadius * 0.01f;
            }

            AddTube(m, SubmeshFoliage, spirePath, spireRadii, 5);

            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                Vector3 p = Vector3.Lerp(spireBase, spireTip, t);
                for (int k = 0; k < 5; k++)
                {
                    float a = rng.Range(0f, Mathf.PI * 2f);
                    Vector3 d = new Vector3(Mathf.Cos(a), rng.Range(-0.55f, -0.1f), Mathf.Sin(a));
                    AddNeedle(m, p, d, Vector3.Cross(d, Vector3.up),
                              crownRadius * (0.55f - 0.4f * t) * rng.Range(0.7f, 1.1f),
                              crownRadius * 0.1f);
                }
            }

            // Lumps of snow caught on the crown, shrinking to the point.
            const int crownSteps = 5;
            for (int i = 0; i < crownSteps; i++)
            {
                float ct = 0.04f + 0.78f * i / (crownSteps - 1f);
                Vector3 p = Vector3.Lerp(spireBase, spireTip, ct);
                float r = settings.snowScale *
                          Mathf.Min(crownRadius * (0.62f - 0.5f * ct),
                                    gap * 0.3f * (1f - ct * 0.8f)) *
                          rng.Range(0.85f, 1.15f);
                float wobble = crownRadius * 0.3f;
                Vector3 c = p + new Vector3(rng.Range(-wobble, wobble), r * 0.3f,
                                            rng.Range(-wobble, wobble));
                field.AddSphere(c, r, new Vector3(1f, rng.Range(0.75f, 1f), 1f), blend);
            }

            field.Polygonize(m, SubmeshSnow);
            Upload(m, target);
        }

        static void Upload(MeshScratch scratch, Mesh target)
        {
            target.Clear();
            target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            target.subMeshCount = SubmeshCount;
            target.SetVertices(scratch.Vertices);
            target.SetNormals(scratch.Normals);
            target.SetUVs(0, scratch.Uvs);
            for (int s = 0; s < SubmeshCount; s++)
            {
                target.SetTriangles(scratch.Indices[s], s, calculateBounds: false);
            }

            target.RecalculateBounds();
            target.RecalculateTangents();
        }
    }
}
