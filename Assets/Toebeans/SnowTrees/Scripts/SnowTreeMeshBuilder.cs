using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.SnowTrees
{
    /// <summary>
    /// Procedural generator for the snow-laden conifers used across Toebeans 3.
    /// </summary>
    /// <remarks>
    /// A tree is grown out of three primitives: a swept tube (trunk, roots,
    /// boughs, needle sprigs), a lumpy squashed dome (every pillow of snow) and
    /// the tier loop that places them. Everything is driven by a deterministic
    /// LCG, so the same settings always produce the same mesh - no baked binary
    /// asset needed to keep the trees stable across machines.
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

        sealed class Scratch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(4096);
            public readonly List<Vector2> Uvs = new List<Vector2>(4096);
            public readonly List<int>[] Indices =
            {
                new List<int>(2048), new List<int>(4096), new List<int>(8192),
            };

            public int AddVertex(Vector3 p, Vector2 uv)
            {
                Vertices.Add(p);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            public void Tri(int submesh, int a, int b, int c)
            {
                var list = Indices[submesh];
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }

            public void Quad(int submesh, int a, int b, int c, int d)
            {
                Tri(submesh, a, b, c);
                Tri(submesh, a, c, d);
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

        /// <summary>Sweeps a ring of <paramref name="segments"/> verts along a path.</summary>
        static void AddTube(Scratch mesh, int submesh, IList<Vector3> path, IList<Vector2> radii,
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

                Vector2 r = radii[i];
                var ring = new int[segments];
                for (int s = 0; s < segments; s++)
                {
                    float a = Mathf.PI * 2f * s / segments;
                    Vector3 offset = right * (Mathf.Cos(a) * r.x) + up * (Mathf.Sin(a) * r.y);
                    ring[s] = mesh.AddVertex(path[i] + offset, new Vector2((float)s / segments, travelled));
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
                int centre = mesh.AddVertex(path[0], new Vector2(0.5f, 0f));
                for (int s = 0; s < segments; s++)
                {
                    mesh.Tri(submesh, centre, rings[0][(s + 1) % segments], rings[0][s]);
                }
            }

            if (capEnd)
            {
                int centre = mesh.AddVertex(path[sections - 1], new Vector2(0.5f, travelled));
                for (int s = 0; s < segments; s++)
                {
                    mesh.Tri(submesh, centre, rings[sections - 1][s], rings[sections - 1][(s + 1) % segments]);
                }
            }
        }

        /// <summary>
        /// A squashed, jittered dome with a lip tucked under it - one pillow of
        /// snow. Optionally stretched along a bough so it reads as a drift that
        /// slid outwards rather than a ball dropped on top.
        /// </summary>
        static void AddSnowBlob(Scratch mesh, Vector3 centre, float radius, float squash,
                                int segments, int rings, ref Rng rng,
                                float jitter = 0.12f, Vector3? stretchAlong = null, float stretch = 1f)
        {
            Vector3 axis = Vector3.zero;
            bool stretched = false;
            if (stretchAlong.HasValue && !Mathf.Approximately(stretch, 1f) &&
                stretchAlong.Value.sqrMagnitude > 1e-10f)
            {
                axis = stretchAlong.Value.normalized;
                stretched = true;
            }

            var grid = new int[rings + 1][];
            for (int ri = 0; ri <= rings; ri++)
            {
                float t = (float)ri / rings;                    // 0 = pole, 1 = rim
                float phi = t * Mathf.PI * 0.62f;               // a dome, not a full sphere
                float y = Mathf.Cos(phi);
                float ringRadius = Mathf.Sin(phi);
                var row = new int[segments];
                for (int s = 0; s < segments; s++)
                {
                    float a = Mathf.PI * 2f * s / segments;
                    Vector3 p = new Vector3(Mathf.Cos(a) * ringRadius, y * squash, Mathf.Sin(a) * ringRadius);
                    p *= radius * (1f + rng.Range(-jitter, jitter));
                    if (stretched)
                    {
                        p += axis * (Vector3.Dot(p, axis) * (stretch - 1f));
                    }

                    row[s] = mesh.AddVertex(centre + p, new Vector2((float)s / segments, 1f - t * 0.5f));
                }

                grid[ri] = row;
            }

            for (int ri = 0; ri < rings; ri++)
            {
                int[] a = grid[ri];
                int[] b = grid[ri + 1];
                for (int s = 0; s < segments; s++)
                {
                    int n = (s + 1) % segments;
                    if (ri == 0)
                    {
                        mesh.Tri(SubmeshSnow, a[s], b[n], b[s]);
                    }
                    else
                    {
                        mesh.Quad(SubmeshSnow, a[s], a[n], b[n], b[s]);
                    }
                }
            }

            // Tuck the rim in and down so the pillow overhangs its bough.
            int[] rim = grid[rings];
            var skirt = new int[segments];
            for (int s = 0; s < segments; s++)
            {
                Vector3 d = mesh.Vertices[rim[s]] - centre;
                Vector3 p = centre + new Vector3(d.x * 0.82f, d.y - radius * squash * 0.35f, d.z * 0.82f);
                skirt[s] = mesh.AddVertex(p, new Vector2((float)s / segments, 0.4f));
            }

            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                mesh.Quad(SubmeshSnow, rim[s], rim[n], skirt[n], skirt[s]);
            }
        }

        static Vector3[] BoughPath(Vector3 origin, Vector3 direction, float length, float droop, int sections)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
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

        // -------------------------------------------------------------- build
        public static Mesh Build(SnowTreeVariant variant, bool flatShading = true)
        {
            Mesh mesh = Build(SnowTreeSettings.ForVariant(variant), flatShading);
            mesh.name = variant.AssetName();
            return mesh;
        }

        public static Mesh Build(SnowTreeSettings settings, bool flatShading = true)
        {
            var mesh = new Mesh { name = "SnowTree" };
            Build(settings, mesh, flatShading);
            return mesh;
        }

        /// <summary>Rebuilds <paramref name="target"/> in place, reusing its buffers.</summary>
        public static void Build(SnowTreeSettings settings, Mesh target, bool flatShading = true)
        {
            settings = settings.Sanitised();
            var rng = new Rng(settings.seed);
            var m = new Scratch();

            float height = settings.height;
            float radius = settings.radius;
            float trunkBaseRadius = radius * 0.115f;
            float trunkTopRadius = trunkBaseRadius * 0.18f;

            // Trunk ------------------------------------------------------
            const int trunkSections = 10;
            var trunkPath = new Vector3[trunkSections];
            var trunkRadii = new Vector2[trunkSections];
            float leanX = rng.Range(-0.03f, 0.03f) * height;
            float leanZ = rng.Range(-0.03f, 0.03f) * height;
            for (int i = 0; i < trunkSections; i++)
            {
                float t = (float)i / (trunkSections - 1);
                trunkPath[i] = new Vector3(leanX * t * t, height * t * 1.005f, leanZ * t * t);
                float r = trunkBaseRadius * Mathf.Pow(1f - t, 0.75f) + trunkTopRadius;
                trunkRadii[i] = new Vector2(r, r);
            }

            AddTube(m, SubmeshBark, trunkPath, trunkRadii, 7);

            // Exposed roots ----------------------------------------------
            for (int i = 0; i < settings.rootCount; i++)
            {
                float a = Mathf.PI * 2f * (i + rng.Range(-0.18f, 0.18f)) / settings.rootCount;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float length = radius * rng.Range(0.32f, 0.55f);
                const int rootSections = 5;
                var rootPath = new Vector3[rootSections];
                var rootRadii = new Vector2[rootSections];
                for (int k = 0; k < rootSections; k++)
                {
                    float t = (float)k / (rootSections - 1);
                    float reach = length * t * (0.6f + 0.4f * t);
                    rootPath[k] = new Vector3(
                        dir.x * reach,
                        trunkBaseRadius * 0.9f - length * 0.55f * t * t + rng.Range(-0.01f, 0.01f) * radius,
                        dir.z * reach);
                    float r = trunkBaseRadius * (0.55f - 0.45f * t) + 0.004f * radius;
                    rootRadii[k] = new Vector2(r, r);
                }

                AddTube(m, SubmeshBark, rootPath, rootRadii, 4);
            }

            // Tiers of boughs --------------------------------------------
            const float tierHigh = 0.9f;
            for (int ti = 0; ti < settings.tiers; ti++)
            {
                float tt = settings.tiers > 1 ? (float)ti / (settings.tiers - 1) : 0f;
                float t = settings.lowestTier + (tierHigh - settings.lowestTier) * tt;
                Vector3 origin = TrunkAt(trunkPath, t);
                float span = radius * Profile(t, settings.shape) * rng.Range(0.9f, 1.06f);
                if (span <= radius * 0.05f)
                {
                    continue;
                }

                int count = Mathf.Max(3, Mathf.RoundToInt(settings.boughsPerTier * (0.6f + 0.4f * (1f - tt))));
                float phase = rng.Value() * Mathf.PI * 2f;

                for (int bi = 0; bi < count; bi++)
                {
                    float a = phase + Mathf.PI * 2f * (bi + rng.Range(-0.22f, 0.22f)) / count;
                    Vector3 dir = new Vector3(Mathf.Cos(a), rng.Range(0.2f, 0.4f), Mathf.Sin(a));
                    float length = span * rng.Range(1f, 1.28f);
                    float droop = rng.Range(0.5f, 0.72f);
                    const int boughSections = 4;
                    Vector3[] path = BoughPath(origin, dir, length, droop, boughSections);
                    var radii = new Vector2[boughSections];
                    for (int k = 0; k < boughSections; k++)
                    {
                        float tk = (float)k / (boughSections - 1);
                        float w = length * 0.34f * Mathf.Pow(1f - tk, 0.6f) + length * 0.02f;
                        radii[k] = new Vector2(w, w * 0.5f);
                    }

                    AddTube(m, SubmeshFoliage, path, radii, 5, capStart: false);

                    // Snow rides the inner half of the bough; the tip stays green.
                    if (rng.Value() < settings.snowCoverage)
                    {
                        Vector3 seat = path[1];
                        Vector3 outward = path[2] - path[1];
                        float lump = length * settings.snowScale * rng.Range(0.38f, 0.48f);
                        Vector3 centre = seat + Vector3.up * (radii[1].y * 0.5f);
                        AddSnowBlob(m, centre, lump, rng.Range(0.8f, 1f), 6, 2, ref rng,
                                    stretchAlong: outward, stretch: rng.Range(1.2f, 1.55f));
                    }
                }

                // Needle sprigs poking up between the pillows.
                int sprigs = Mathf.Max(3, count * 2 / 3);
                for (int si = 0; si < sprigs; si++)
                {
                    float a = phase + Mathf.PI / count + Mathf.PI * 2f * (si + rng.Range(-0.3f, 0.3f)) / sprigs;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float offset = span * rng.Range(0.3f, 0.62f);
                    Vector3 basePoint = origin + new Vector3(dir.x * offset, span * rng.Range(0.05f, 0.2f), dir.z * offset);
                    float sprigHeight = span * rng.Range(0.4f, 0.62f);
                    Vector3 tip = basePoint + new Vector3(dir.x * sprigHeight * 0.35f, sprigHeight, dir.z * sprigHeight * 0.35f);
                    AddTube(m, SubmeshFoliage, new[] { basePoint, tip },
                            new[] { new Vector2(span * 0.1f, span * 0.1f), new Vector2(span * 0.01f, span * 0.01f) }, 4);
                }

                // Cushion of snow packed around the trunk at the tier.
                AddSnowBlob(m, origin + Vector3.up * (span * 0.12f),
                            span * settings.snowScale * rng.Range(0.44f, 0.56f),
                            rng.Range(0.8f, 1f), 6, 2, ref rng);
            }

            // Crown: one continuous needle spire above the last tier -------
            Vector3 spireBase = TrunkAt(trunkPath, tierHigh - 0.07f);
            Vector3 trunkTip = TrunkAt(trunkPath, 1f);
            Vector3 spireTip = new Vector3(trunkTip.x, trunkTip.y + height * 0.05f, trunkTip.z);
            float crownRadius = radius * Profile(tierHigh - 0.07f, settings.shape) * 1.05f;
            const int spireSections = 5;
            var spirePath = new Vector3[spireSections];
            var spireRadii = new Vector2[spireSections];
            for (int i = 0; i < spireSections; i++)
            {
                float t = (float)i / (spireSections - 1);
                spirePath[i] = Vector3.Lerp(spireBase, spireTip, t);
                float r = crownRadius * Mathf.Pow(1f - t, 0.9f) + crownRadius * 0.04f;
                spireRadii[i] = new Vector2(r, r);
            }

            AddTube(m, SubmeshFoliage, spirePath, spireRadii, 5);

            for (int i = 0; i < 3; i++)
            {
                float t = 0.16f + 0.32f * i;
                AddSnowBlob(m, Vector3.Lerp(spireBase, spireTip, t),
                            crownRadius * settings.snowScale * (1.15f - 0.3f * i),
                            rng.Range(0.9f, 1.15f), 6, 2, ref rng);
            }

            Upload(m, target, flatShading);
        }

        static Vector3 TrunkAt(IList<Vector3> path, float t)
        {
            float f = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, path.Count - 2);
            return Vector3.Lerp(path[i], path[i + 1], f - i);
        }

        static void Upload(Scratch scratch, Mesh target, bool flatShading)
        {
            target.Clear();
            target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            target.subMeshCount = SubmeshCount;

            if (flatShading)
            {
                // Split every triangle so each face keeps its own hard normal -
                // the faceted look the stylised kit is built around.
                int total = 0;
                for (int s = 0; s < SubmeshCount; s++)
                {
                    total += scratch.Indices[s].Count;
                }

                var vertices = new Vector3[total];
                var uvs = new Vector2[total];
                var normals = new Vector3[total];
                var submeshes = new int[SubmeshCount][];
                int write = 0;

                for (int s = 0; s < SubmeshCount; s++)
                {
                    List<int> src = scratch.Indices[s];
                    var indices = new int[src.Count];
                    for (int i = 0; i < src.Count; i += 3)
                    {
                        Vector3 a = scratch.Vertices[src[i]];
                        Vector3 b = scratch.Vertices[src[i + 1]];
                        Vector3 c = scratch.Vertices[src[i + 2]];
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;

                        for (int k = 0; k < 3; k++)
                        {
                            vertices[write] = scratch.Vertices[src[i + k]];
                            uvs[write] = scratch.Uvs[src[i + k]];
                            normals[write] = n;
                            indices[i + k] = write;
                            write++;
                        }
                    }

                    submeshes[s] = indices;
                }

                target.SetVertices(vertices);
                target.SetUVs(0, uvs);
                target.SetNormals(normals);
                for (int s = 0; s < SubmeshCount; s++)
                {
                    target.SetTriangles(submeshes[s], s, calculateBounds: false);
                }
            }
            else
            {
                target.SetVertices(scratch.Vertices);
                target.SetUVs(0, scratch.Uvs);
                for (int s = 0; s < SubmeshCount; s++)
                {
                    target.SetTriangles(scratch.Indices[s], s, calculateBounds: false);
                }

                target.RecalculateNormals();
            }

            target.RecalculateBounds();
            target.RecalculateTangents();
        }
    }
}
