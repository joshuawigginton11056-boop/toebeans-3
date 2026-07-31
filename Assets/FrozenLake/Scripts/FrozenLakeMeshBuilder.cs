using System.Collections.Generic;
using UnityEngine;

namespace FrozenLake
{
    /// <summary>
    /// Builds the low-poly frozen lake geometry. Pure maths in, triangles out: no scene objects,
    /// no asset loading, no global state, so it can be called from the editor, at runtime, or from
    /// a test harness.
    ///
    /// Layout of the finished asset, all in local space with the ice surface at y = 0:
    ///   * a faceted ice sheet broken into plates, each plate at a slightly different height
    ///   * a snow berm ringing the shore, sloping back down to y = 0 so it blends onto flat ground
    ///   * a solid skirt and floor beneath, so the asset reads as a block rather than a paper sheet
    ///   * heaved ice shards, snow drifts and boulders scattered on top
    /// </summary>
    public static class FrozenLakeMeshBuilder
    {
        public const int SubmeshCount = 4;

        public static MeshBuffer Build(FrozenLakeSettings settings)
        {
            FrozenLakeSettings s = settings ?? new FrozenLakeSettings();
            var rng = new Rng(s.seed);
            var buf = new MeshBuffer(SubmeshCount, s.uvScale);

            var shore = new ShoreShape(s, ref rng);
            var plates = new PlateField(s, shore, ref rng);
            HoleShape hole = s.hole ? new HoleShape(s, shore, ref rng) : null;

            Vector3[] shoreRing = BuildShoreRing(s, shore);

            BuildIce(buf, s, shore, plates, shoreRing, hole, ref rng);
            Vector3[] bankOuter = BuildBank(buf, s, shore, shoreRing, ref rng);
            BuildSkirtAndFloor(buf, s, bankOuter, hole);
            BuildShards(buf, s, shore, hole, ref rng);
            BuildSnowPatches(buf, s, shore, hole, ref rng);
            BuildRocks(buf, s, shore, hole, ref rng);
            if (hole != null) BuildHoleDebris(buf, s, hole, ref rng);

            if (hole != null)
            {
                buf.Hole = new HoleInfo
                {
                    Exists = true,
                    Center = new Vector3(hole.CenterX, 0f, hole.CenterZ),
                    Radius = hole.MeanRadius,
                    ClearRadius = hole.ClearRadius,
                    ShaftDepth = s.holeOpensThrough ? s.depth : 0f
                };
            }

            return buf;
        }

        // ------------------------------------------------------------------ shore

        /// <summary>The wandering outline of the lake, as a radius per angle.</summary>
        sealed class ShoreShape
        {
            readonly float _radius;
            readonly float[] _amp;
            readonly float[] _phase;
            readonly int[] _freq;

            public ShoreShape(FrozenLakeSettings s, ref Rng rng)
            {
                _radius = Mathf.Max(0.01f, s.radius);
                _freq = new[] { 2, 3, 5, 7 };
                _amp = new float[_freq.Length];
                _phase = new float[_freq.Length];

                // Weight the low harmonics heavily so the lake gets a readable overall shape
                // rather than a uniformly crinkled edge.
                float[] weights = { 0.45f, 0.28f, 0.17f, 0.10f };
                for (int i = 0; i < _freq.Length; i++)
                {
                    _amp[i] = s.shoreIrregularity * weights[i] * rng.Range(0.6f, 1.4f);
                    _phase[i] = rng.Value() * Mathf.PI * 2f;
                }
            }

            public float Radius(float angle)
            {
                float f = 1f;
                for (int i = 0; i < _freq.Length; i++)
                    f += _amp[i] * Mathf.Sin(angle * _freq[i] + _phase[i]);
                return _radius * Mathf.Max(0.35f, f);
            }

            public float MeanRadius { get { return _radius; } }

            /// <summary>
            /// Per-angle multiplier on the berm width. Snow piles deep on one shore and thins out
            /// on another, which is what stops the berm reading as a machined ring.
            /// </summary>
            public float BankScale(float angle, int seed)
            {
                float n = LakeNoise.Signed(Mathf.Cos(angle) * 1.7f + 8.3f, Mathf.Sin(angle) * 1.7f - 3.1f, seed);
                return Mathf.Clamp(1f + n * 0.55f, 0.35f, 1.6f);
            }

            public Vector3 PointOnIce(float angle, float radiusFraction, int seed)
            {
                float r = Radius(angle) * radiusFraction;
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;
                return new Vector3(x, IceHeight(x, z, seed), z);
            }
        }

        /// <summary>Gentle undulation of the ice surface, before per-plate offsets.</summary>
        static float IceHeight(float x, float z, int seed)
        {
            return LakeNoise.Signed(x * 0.18f, z * 0.18f, seed) * 0.035f;
        }

        // ------------------------------------------------------------------ hole

        /// <summary>
        /// The smashed opening in the ice. Null when the lake is intact.
        ///
        /// The rim is deliberately not built to this outline. Ice triangles are dropped wholesale
        /// when their centre falls inside it, which leaves a ragged edge along the existing facets;
        /// that reads far more like something gave way than a clean cut ever would.
        /// </summary>
        sealed class HoleShape
        {
            readonly float _radius;
            readonly float[] _amp;
            readonly float[] _phase;
            readonly int[] _freq;

            public readonly float CenterX;
            public readonly float CenterZ;

            public HoleShape(FrozenLakeSettings s, ShoreShape shore, ref Rng rng)
            {
                // Capped so the rim always lands on ice. A hole wider than the lake would leave the
                // shaft hanging outside the block with no sheet left to break.
                _radius = Mathf.Clamp(s.holeRadius, 0.1f, shore.MeanRadius * 0.62f);

                // Keep the whole rim clear of the shoreline, whatever the offset and radius ask for.
                float maxOffset = Mathf.Max(0f, shore.MeanRadius * 0.5f - _radius * 1.35f);
                float ox = Mathf.Clamp(s.holeOffsetX, -0.5f, 0.5f) * shore.MeanRadius;
                float oz = Mathf.Clamp(s.holeOffsetZ, -0.5f, 0.5f) * shore.MeanRadius;
                float len = Mathf.Sqrt(ox * ox + oz * oz);
                if (len > maxOffset && len > 0.0001f)
                {
                    ox *= maxOffset / len;
                    oz *= maxOffset / len;
                }
                CenterX = ox;
                CenterZ = oz;

                _freq = new[] { 2, 3, 5 };
                _amp = new float[_freq.Length];
                _phase = new float[_freq.Length];
                float[] weights = { 0.5f, 0.32f, 0.18f };
                for (int i = 0; i < _freq.Length; i++)
                {
                    _amp[i] = s.holeIrregularity * weights[i] * rng.Range(0.6f, 1.4f);
                    _phase[i] = rng.Value() * Mathf.PI * 2f;
                }
            }

            public float Radius(float angle)
            {
                float f = 1f;
                for (int i = 0; i < _freq.Length; i++)
                    f += _amp[i] * Mathf.Sin(angle * _freq[i] + _phase[i]);
                return _radius * Mathf.Max(0.4f, f);
            }

            /// <summary>True when the point falls inside the opening, optionally grown by a margin.</summary>
            public bool Contains(float x, float z, float grow)
            {
                float dx = x - CenterX;
                float dz = z - CenterZ;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r < 0.0001f) return true;
                return r < Radius(Mathf.Atan2(dz, dx)) * grow;
            }

            /// <summary>Average radius of the opening, after clamping.</summary>
            public float MeanRadius { get { return _radius; } }

            /// <summary>
            /// Radius of the column down the middle that nothing is allowed to intrude on. The
            /// ragged outline never comes closer than 0.4 of the mean radius, so half of it leaves
            /// a comfortable margin.
            /// </summary>
            public float ClearRadius { get { return _radius * 0.5f; } }

            /// <summary>
            /// True when a prop of the given horizontal reach, placed here, would either stand on
            /// ice that is gone or lean far enough in to foul the fall path.
            /// </summary>
            public bool Blocks(Vector3 at, float reach)
            {
                float dx = at.x - CenterX;
                float dz = at.z - CenterZ;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d - reach < ClearRadius) return true;
                return Contains(at.x, at.z, 1.05f);
            }

            /// <summary>The outline as a closed loop at the given height and radius scale.</summary>
            public Vector3[] Loop(int segments, float y, float scale)
            {
                var loop = new Vector3[segments];
                for (int j = 0; j < segments; j++)
                {
                    float a = j * Mathf.PI * 2f / segments;
                    float r = Radius(a) * scale;
                    loop[j] = new Vector3(CenterX + Mathf.Cos(a) * r, y, CenterZ + Mathf.Sin(a) * r);
                }
                return loop;
            }
        }

        // ------------------------------------------------------------------ plates

        /// <summary>
        /// A scattering of Voronoi sites over the ice. Every triangle takes the height and material
        /// of its nearest site, which is what turns a smooth sheet into cracked plates.
        /// </summary>
        sealed class PlateField
        {
            readonly Vector2[] _sites;
            readonly float[] _height;
            readonly Vector2[] _tilt;
            readonly LakeSlot[] _slot;
            readonly float[] _shade;
            readonly float _maxOffset;

            public PlateField(FrozenLakeSettings s, ShoreShape shore, ref Rng rng)
            {
                int n = Mathf.Max(1, s.plateCount);
                _sites = new Vector2[n];
                _height = new float[n];
                _tilt = new Vector2[n];
                _slot = new LakeSlot[n];
                _shade = new float[n];
                _maxOffset = s.plateHeightVariation * 2.2f;

                for (int i = 0; i < n; i++)
                {
                    float angle = rng.Value() * Mathf.PI * 2f;
                    // sqrt keeps the sites area-uniform instead of bunching at the centre.
                    float frac = Mathf.Sqrt(rng.Value());
                    float r = shore.Radius(angle) * frac;
                    _sites[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                    _height[i] = rng.Signed(s.plateHeightVariation);

                    // Each plate is a slightly tipped slab rather than a flat step, so neighbours
                    // catch the light differently and the cracks between them read at a glance.
                    float slopeScale = s.plateHeightVariation / Mathf.Max(1f, shore.MeanRadius * 0.22f);
                    _tilt[i] = new Vector2(rng.Signed(slopeScale), rng.Signed(slopeScale));
                    _shade[i] = rng.Range(0.84f, 1.08f);

                    // Snow drifts gather round the shore, while the middle freezes into darker,
                    // clearer ice. A little noise on the threshold keeps it off a perfect bullseye.
                    float wobble = LakeNoise.Signed(_sites[i].x * 0.12f, _sites[i].y * 0.12f, s.seed + 313) * 0.18f;
                    float shoreness = Mathf.Clamp01(frac + wobble);
                    float snowChance = s.shoreSnowRatio * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1.02f, shoreness));
                    float deepChance = s.deepIceRatio * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.85f, shoreness))) * 1.8f;

                    if (rng.Chance(snowChance)) _slot[i] = LakeSlot.Snow;
                    else if (rng.Chance(deepChance)) _slot[i] = LakeSlot.IceDeep;
                    else _slot[i] = LakeSlot.IcePale;
                }
            }

            public int Nearest(float x, float z)
            {
                int best = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < _sites.Length; i++)
                {
                    float dx = _sites[i].x - x;
                    float dz = _sites[i].y - z;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = i; }
                }
                return best;
            }

            /// <summary>Height of plate <paramref name="i"/>'s slab evaluated at a point on the ice.</summary>
            public float OffsetAt(int i, float x, float z)
            {
                float o = _height[i] + _tilt[i].x * (x - _sites[i].x) + _tilt[i].y * (z - _sites[i].y);
                return Mathf.Clamp(o, -_maxOffset, _maxOffset);
            }

            public LakeSlot Slot(int i) { return _slot[i]; }
            public float Shade(int i) { return _shade[i]; }
        }

        // ------------------------------------------------------------------ ice sheet

        /// <summary>The shared rim shared by the ice sheet and the snow berm, so the two weld cleanly.</summary>
        static Vector3[] BuildShoreRing(FrozenLakeSettings s, ShoreShape shore)
        {
            int seg = Mathf.Max(3, s.angularSegments);
            var ring = new Vector3[seg];
            for (int j = 0; j < seg; j++)
            {
                float angle = j * Mathf.PI * 2f / seg;
                ring[j] = shore.PointOnIce(angle, 1f, s.seed);
            }
            return ring;
        }

        static void BuildIce(MeshBuffer buf, FrozenLakeSettings s, ShoreShape shore, PlateField plates,
                             Vector3[] shoreRing, HoleShape hole, ref Rng rng)
        {
            int seg = Mathf.Max(3, s.angularSegments);
            int rings = Mathf.Max(2, s.radialRings);

            float angleStep = Mathf.PI * 2f / seg;

            // Lay down a clean radial grid first...
            var grid = new Vector3[rings + 1][];
            for (int i = 0; i <= rings; i++)
            {
                grid[i] = new Vector3[seg];
                float t = (float)i / rings;

                for (int j = 0; j < seg; j++)
                {
                    if (i == 0) grid[i][j] = new Vector3(0f, IceHeight(0f, 0f, s.seed), 0f);
                    else if (i == rings) grid[i][j] = shoreRing[j];
                    else grid[i][j] = shore.PointOnIce(j * angleStep, t, s.seed);
                }
            }

            // ...then shove the interior vertices around so the facets stop reading as a fan.
            // The offset is capped at a fraction of the distance to the nearest neighbour, which
            // keeps the grid from ever folding over itself and flipping a triangle's normal.
            const float MaxOffsetFraction = 0.28f;
            for (int i = 1; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int jPrev = (j + seg - 1) % seg;
                    int jNext = (j + 1) % seg;
                    Vector3 p = grid[i][j];

                    float limit = FlatDistance(p, grid[i - 1][j]);
                    limit = Mathf.Min(limit, FlatDistance(p, grid[i + 1][j]));
                    limit = Mathf.Min(limit, FlatDistance(p, grid[i][jPrev]));
                    limit = Mathf.Min(limit, FlatDistance(p, grid[i][jNext]));
                    limit *= MaxOffsetFraction * Mathf.Clamp01(s.iceJitter);

                    float dir = rng.Value() * Mathf.PI * 2f;
                    float mag = rng.Value() * limit;
                    float x = p.x + Mathf.Cos(dir) * mag;
                    float z = p.z + Mathf.Sin(dir) * mag;
                    grid[i][j] = new Vector3(x, IceHeight(x, z, s.seed), z);
                }
            }

            // Emit through a helper that records grid topology, so the hole rim can be found later.
            var sheet = new IceSheet(seg, rings, grid);

            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int j1 = (j + 1) % seg;
                    int a = sheet.Index(i, j);
                    int b = sheet.Index(i, j1);
                    int c = sheet.Index(i + 1, j);
                    int d = sheet.Index(i + 1, j1);

                    bool flip = ((i + j) & 1) == 0;
                    if (flip)
                    {
                        sheet.Add(a, b, d);
                        sheet.Add(a, d, c);
                    }
                    else
                    {
                        sheet.Add(a, b, c);
                        sheet.Add(b, d, c);
                    }
                }
            }

            sheet.Emit(buf, plates, shore, s, hole);
            if (hole != null) sheet.AddBrokenEdge(buf, s, hole, ref rng);
        }

        /// <summary>
        /// The ice sheet held as indices into a shared vertex grid, so triangles can be dropped for
        /// the hole and the resulting boundary edges recovered afterwards. A boundary edge is simply
        /// one used by a single surviving triangle.
        /// </summary>
        sealed class IceSheet
        {
            readonly int _seg;
            readonly int _rings;
            readonly Vector3[] _verts;
            readonly List<int> _tris = new List<int>();

            public IceSheet(int seg, int rings, Vector3[][] grid)
            {
                _seg = seg;
                _rings = rings;
                // The centre collapses to one vertex so the fan there is topologically sound.
                _verts = new Vector3[1 + rings * seg];
                _verts[0] = grid[0][0];
                for (int i = 1; i <= rings; i++)
                    for (int j = 0; j < seg; j++)
                        _verts[1 + (i - 1) * seg + j] = grid[i][j];
            }

            public int Index(int ring, int j)
            {
                return ring == 0 ? 0 : 1 + (ring - 1) * _seg + (j % _seg);
            }

            public Vector3 Position(int index) { return _verts[index]; }

            /// <summary>True for the outermost ring, which welds to the snow berm.</summary>
            bool IsShore(int index)
            {
                return index >= 1 + (_rings - 1) * _seg;
            }

            public void Add(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return; // collapsed by the centre fan
                _tris.Add(a); _tris.Add(b); _tris.Add(c);
            }

            /// <summary>Drops triangles inside the hole and hands the rest to the mesh.</summary>
            public void Emit(MeshBuffer buf, PlateField plates, ShoreShape shore,
                             FrozenLakeSettings s, HoleShape hole)
            {
                var kept = new List<int>(_tris.Count);
                for (int t = 0; t < _tris.Count; t += 3)
                {
                    Vector3 a = _verts[_tris[t]], b = _verts[_tris[t + 1]], c = _verts[_tris[t + 2]];
                    if (hole != null)
                    {
                        float cx = (a.x + b.x + c.x) / 3f;
                        float cz = (a.z + b.z + c.z) / 3f;
                        if (hole.Contains(cx, cz, 1f)) continue;
                    }
                    kept.Add(_tris[t]); kept.Add(_tris[t + 1]); kept.Add(_tris[t + 2]);
                    EmitIceTriangle(buf, plates, shore, s.seed, a, b, c);
                }
                _tris.Clear();
                _tris.AddRange(kept);
            }

            /// <summary>
            /// Hangs a broken edge off every boundary the hole opened up, so the sheet shows its
            /// thickness instead of looking like cut paper. The shoreline is excluded: that edge
            /// belongs to the snow berm, not the hole.
            /// </summary>
            public void AddBrokenEdge(MeshBuffer buf, FrozenLakeSettings s, HoleShape hole, ref Rng rng)
            {
                var used = new Dictionary<long, int>();
                for (int t = 0; t < _tris.Count; t += 3)
                {
                    Bump(used, _tris[t], _tris[t + 1]);
                    Bump(used, _tris[t + 1], _tris[t + 2]);
                    Bump(used, _tris[t + 2], _tris[t]);
                }

                foreach (var pair in used)
                {
                    if (pair.Value != 1) continue;
                    int p = (int)(pair.Key >> 32);
                    int q = (int)(pair.Key & 0xFFFFFFFFL);
                    if (IsShore(p) && IsShore(q)) continue;

                    Vector3 top0 = _verts[p];
                    Vector3 top1 = _verts[q];
                    float d0 = s.iceThickness * rng.Range(0.65f, 1.35f);
                    float d1 = s.iceThickness * rng.Range(0.65f, 1.35f);
                    Vector3 bot0 = new Vector3(top0.x, top0.y - d0, top0.z);
                    Vector3 bot1 = new Vector3(top1.x, top1.y - d1, top1.z);

                    // Boundary edges come out in arbitrary order, so orient by where the hole is
                    // rather than trusting the winding.
                    float mx = (top0.x + top1.x) * 0.5f;
                    float mz = (top0.z + top1.z) * 0.5f;
                    var toward = new Vector3(hole.CenterX - mx, 0f, hole.CenterZ - mz);
                    AddOrientedQuad(buf, top0, top1, bot0, bot1, toward, LakeSlot.IceDeep, rng.Range(0.7f, 0.95f));
                }
            }

            static void Bump(Dictionary<long, int> counts, int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int n;
                counts.TryGetValue(key, out n);
                counts[key] = n + 1;
            }
        }

        /// <summary>Adds a wall quad, flipping the winding so its face points along <paramref name="toward"/>.</summary>
        static void AddOrientedQuad(MeshBuffer buf, Vector3 top0, Vector3 top1, Vector3 bot0, Vector3 bot1,
                                    Vector3 toward, LakeSlot slot, float shade)
        {
            Vector3 n = Vector3.Cross(top1 - top0, bot0 - top0);
            if (Vector3.Dot(n, toward) < 0f)
                buf.AddQuad(top1, top0, bot1, bot0, slot, shade);
            else
                buf.AddQuad(top0, top1, bot0, bot1, slot, shade);
        }

        /// <summary>
        /// Offsets a single ice triangle onto its plate. Any three points are coplanar, so the
        /// per-corner offset still yields a perfectly flat facet, and neighbouring plates end up
        /// with a small vertical step between them: the cracks.
        /// </summary>
        static void EmitIceTriangle(MeshBuffer buf, PlateField plates, ShoreShape shore, int seed,
                                    Vector3 a, Vector3 b, Vector3 c)
        {
            float cx = (a.x + b.x + c.x) / 3f;
            float cz = (a.z + b.z + c.z) / 3f;
            int plate = plates.Nearest(cx, cz);

            buf.AddTriangle(Lift(a, plates, plate, shore), Lift(b, plates, plate, shore), Lift(c, plates, plate, shore),
                            plates.Slot(plate), plates.Shade(plate));
        }

        /// <summary>
        /// Puts a corner onto its plate's slab, fading the offset out at the waterline so the ice
        /// always meets the snow berm flush instead of stepping away from it.
        /// </summary>
        static Vector3 Lift(Vector3 p, PlateField plates, int plate, ShoreShape shore)
        {
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float shoreR = shore.Radius(Mathf.Atan2(p.z, p.x));
            float fade = Mathf.Clamp01((1f - r / Mathf.Max(0.001f, shoreR)) / 0.12f);
            return new Vector3(p.x, p.y + plates.OffsetAt(plate, p.x, p.z) * fade, p.z);
        }

        /// <summary>
        /// True when a scattered prop would stand on ice that is gone, or would overhang the fall
        /// path. Reach is how far the prop's geometry can extend sideways from its centre.
        /// </summary>
        static bool InHole(HoleShape hole, Vector3 at, float reach)
        {
            return hole != null && hole.Blocks(at, reach);
        }

        /// <summary>Distance between two points ignoring height, i.e. across the ice plane.</summary>
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ snow berm

        /// <summary>Returns the outer ring of the berm, which the skirt hangs from.</summary>
        static Vector3[] BuildBank(MeshBuffer buf, FrozenLakeSettings s, ShoreShape shore,
                                   Vector3[] shoreRing, ref Rng rng)
        {
            int seg = shoreRing.Length;
            int rings = Mathf.Max(1, s.bankRings);
            float angleStep = Mathf.PI * 2f / seg;

            var grid = new Vector3[rings + 1][];
            grid[0] = shoreRing;

            for (int i = 1; i <= rings; i++)
            {
                grid[i] = new Vector3[seg];
                float w = (float)i / rings;

                // Rises out of the ice, peaks at roughly 40% across, then settles back to y = 0 at
                // the outer edge so the berm sits flush on flat terrain.
                float profile = Mathf.Sin(Mathf.PI * Mathf.Pow(w, 0.7f));
                float noiseFade = Mathf.Sin(Mathf.PI * w);

                for (int j = 0; j < seg; j++)
                {
                    float angle = j * angleStep;
                    float jitterAngle = angle + rng.Signed(angleStep * 0.3f);

                    float width = s.bankWidth * shore.BankScale(angle, s.seed + 811);
                    float radial = width * w + rng.Signed(width / rings * 0.3f);

                    float r = shore.Radius(jitterAngle) + Mathf.Max(0f, radial);
                    float x = Mathf.Cos(jitterAngle) * r;
                    float z = Mathf.Sin(jitterAngle) * r;

                    float h = s.bankHeight * profile * shore.BankScale(angle, s.seed + 1607);
                    h += LakeNoise.Signed(x * 0.35f, z * 0.35f, s.seed + 4001)
                         * s.bankHeight * 0.45f * s.bankRoughness * noiseFade;

                    grid[i][j] = new Vector3(x, h, z);
                }
            }

            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int j1 = (j + 1) % seg;
                    Vector3 a = grid[i][j];
                    Vector3 b = grid[i][j1];
                    Vector3 c = grid[i + 1][j];
                    Vector3 d = grid[i + 1][j1];

                    // Bare rock pokes through where the drift is thin, in patches rather than
                    // speckles: sampling noise at the quad keeps neighbouring faces agreeing.
                    float qx = (a.x + b.x + c.x + d.x) * 0.25f;
                    float qz = (a.z + b.z + c.z + d.z) * 0.25f;
                    float exposure = LakeNoise.Value(qx * 0.22f, qz * 0.22f, s.seed + 7919);
                    float threshold = 0.72f - 0.14f * ((float)i / rings);
                    LakeSlot slot = exposure > threshold ? LakeSlot.Rock : LakeSlot.Snow;
                    float shade = rng.Range(0.9f, 1.05f);

                    buf.AddQuad(a, b, c, d, slot, shade, ((i + j) & 1) == 0);
                }
            }

            return grid[rings];
        }

        // ------------------------------------------------------------------ solid body

        static void BuildSkirtAndFloor(MeshBuffer buf, FrozenLakeSettings s, Vector3[] outer, HoleShape hole)
        {
            if (s.depth <= 0f) return;

            int seg = outer.Length;
            float floorY = -s.depth;

            // Draw the floor in a little so the block tapers like a chunk of lifted ground rather
            // than a cylinder stamped out of the terrain.
            const float Taper = 0.86f;
            var floor = new Vector3[seg];
            for (int j = 0; j < seg; j++)
                floor[j] = new Vector3(outer[j].x * Taper, floorY, outer[j].z * Taper);

            for (int j = 0; j < seg; j++)
            {
                int j1 = (j + 1) % seg;
                buf.AddQuad(outer[j], outer[j1], floor[j], floor[j1], LakeSlot.Rock, 0.82f);
            }

            bool shaft = hole != null && s.holeOpensThrough;
            if (!shaft)
            {
                buf.AddFan(new Vector3(0f, floorY, 0f), floor, false, LakeSlot.Rock, 0.7f);
                return;
            }

            // Sunk a touch inside the ragged ice edge so the shaft walls are never left poking
            // through the sheet where the two outlines disagree.
            const float ShaftScale = 0.82f;
            int shaftSegments = Mathf.Max(8, seg / 2);
            Vector3[] top = hole.Loop(shaftSegments, -s.iceThickness * 0.5f, ShaftScale);
            Vector3[] bottom = hole.Loop(shaftSegments, floorY, ShaftScale);

            // Walls face inward: the player only ever sees them from inside the shaft.
            for (int j = 0; j < shaftSegments; j++)
            {
                int j1 = (j + 1) % shaftSegments;
                var inward = new Vector3(hole.CenterX - (top[j].x + top[j1].x) * 0.5f, 0f,
                                         hole.CenterZ - (top[j].z + top[j1].z) * 0.5f);
                AddOrientedQuad(buf, top[j], top[j1], bottom[j], bottom[j1], inward, LakeSlot.Rock, 0.55f);
            }

            AddFloorWithHole(buf, floor, bottom, hole);
        }

        /// <summary>
        /// Floors the block as a ring between its outer edge and the shaft mouth. Both loops wind the
        /// same way around the hole centre, so walking them in step by angle triangulates the gap
        /// without any general-purpose polygon work.
        /// </summary>
        static void AddFloorWithHole(MeshBuffer buf, Vector3[] outer, Vector3[] inner, HoleShape hole)
        {
            int no = outer.Length;
            int ni = inner.Length;

            var outerAngle = new float[no];
            for (int j = 0; j < no; j++)
                outerAngle[j] = Mathf.Atan2(outer[j].z - hole.CenterZ, outer[j].x - hole.CenterX);
            var innerAngle = new float[ni];
            for (int j = 0; j < ni; j++)
                innerAngle[j] = Mathf.Atan2(inner[j].z - hole.CenterZ, inner[j].x - hole.CenterX);

            int io = NearestAngle(outerAngle, innerAngle[0]);
            int ii = 0;
            int tookOuter = 0, tookInner = 0;
            float outerTravel = 0f, innerTravel = 0f;

            // Walk both loops once, each step advancing whichever one has swept the smaller angle
            // so far. That keeps the strip fanning evenly and closes it exactly.
            for (int step = 0; step < no + ni; step++)
            {
                int ioNext = (io + 1) % no;
                int iiNext = (ii + 1) % ni;

                bool advanceOuter;
                if (tookOuter >= no) advanceOuter = false;
                else if (tookInner >= ni) advanceOuter = true;
                else
                {
                    float nextOuter = outerTravel + AngleGap(outerAngle[io], outerAngle[ioNext]);
                    float nextInner = innerTravel + AngleGap(innerAngle[ii], innerAngle[iiNext]);
                    advanceOuter = nextOuter <= nextInner;
                }

                // Wound to face down: this is the underside of the block.
                if (advanceOuter)
                {
                    buf.AddTriangle(inner[ii], outer[io], outer[ioNext], LakeSlot.Rock, 0.7f);
                    outerTravel += AngleGap(outerAngle[io], outerAngle[ioNext]);
                    io = ioNext;
                    tookOuter++;
                }
                else
                {
                    buf.AddTriangle(inner[ii], outer[io], inner[iiNext], LakeSlot.Rock, 0.7f);
                    innerTravel += AngleGap(innerAngle[ii], innerAngle[iiNext]);
                    ii = iiNext;
                    tookInner++;
                }
            }
        }

        static int NearestAngle(float[] angles, float target)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < angles.Length; i++)
            {
                float d = Mathf.Abs(Mathf.Atan2(Mathf.Sin(angles[i] - target), Mathf.Cos(angles[i] - target)));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Forward angular distance from a to b, always in [0, 2pi).</summary>
        static float AngleGap(float a, float b)
        {
            float d = b - a;
            while (d < 0f) d += Mathf.PI * 2f;
            while (d >= Mathf.PI * 2f) d -= Mathf.PI * 2f;
            return d;
        }

        // ------------------------------------------------------------------ hole debris

        /// <summary>
        /// Worst-case horizontal reach of a shard: its widest polygon radius plus its thickness,
        /// since tilting only ever pulls a slab in.
        /// </summary>
        static float ShardReach(FrozenLakeSettings s)
        {
            float maxSize = Mathf.Max(0.05f, s.shardSize * 1.5f);
            return maxSize * 1.38f;
        }

        /// <summary>
        /// Slabs thrown clear when the sheet gave way. They sit on the ice around the rim, pushed
        /// far enough out that even the widest one cannot lean into the fall path.
        /// </summary>
        static void BuildHoleDebris(MeshBuffer buf, FrozenLakeSettings s, HoleShape hole, ref Rng rng)
        {
            float reach = ShardReach(s);
            float minRadius = hole.ClearRadius + reach;

            for (int i = 0; i < s.holeDebrisCount; i++)
            {
                float angle = rng.Value() * Mathf.PI * 2f;
                float r = Mathf.Max(hole.Radius(angle) * rng.Range(1f, 1.35f), minRadius);
                var at = new Vector3(hole.CenterX + Mathf.Cos(angle) * r, 0f,
                                     hole.CenterZ + Mathf.Sin(angle) * r);
                at.y = IceHeight(at.x, at.z, s.seed);
                AddShard(buf, s, at, ref rng);
            }
        }

        // ------------------------------------------------------------------ ice shards

        static void BuildShards(MeshBuffer buf, FrozenLakeSettings s, ShoreShape shore, HoleShape hole, ref Rng rng)
        {
            if (s.shardCount <= 0) return;

            // Most of the heaved ice lines up along a pressure ridge running across the lake, the
            // way it does where two sheets grind together. The rest is scattered debris.
            float ridgeAngle = rng.Value() * Mathf.PI * 2f;
            float ridgeOffset = rng.Signed(0.35f);
            Vector3 along = new Vector3(Mathf.Cos(ridgeAngle), 0f, Mathf.Sin(ridgeAngle));
            Vector3 across = new Vector3(-along.z, 0f, along.x);
            int ridgeCount = Mathf.Max(1, Mathf.FloorToInt(s.shardCount * 0.6f));

            for (int i = 0; i < s.shardCount; i++)
            {
                Vector3 at;
                if (i < ridgeCount)
                {
                    float t = (i + 0.5f) / ridgeCount * 1.7f - 0.85f;
                    // Let the ridge wander instead of running dead straight.
                    float drift = ridgeOffset + LakeNoise.Signed(t * 2.6f, 0.5f, s.seed + 5501) * 0.22f;
                    Vector3 p = (along * t + across * drift) * shore.MeanRadius;
                    p.x += rng.Signed(shore.MeanRadius * 0.05f);
                    p.z += rng.Signed(shore.MeanRadius * 0.05f);
                    at = ClampToIce(p, shore, s.seed, 0.88f);
                }
                else
                {
                    float angle = rng.Value() * Mathf.PI * 2f;
                    at = shore.PointOnIce(angle, Mathf.Sqrt(rng.Value()) * 0.9f, s.seed);
                }
                // Anything that would have landed in the opening fell through with the ice.
                if (InHole(hole, at, ShardReach(s))) continue;
                AddShard(buf, s, at, ref rng);
            }
        }

        /// <summary>Pulls a point back inside the shoreline if it strayed past it.</summary>
        static Vector3 ClampToIce(Vector3 p, ShoreShape shore, int seed, float maxFraction)
        {
            float angle = Mathf.Atan2(p.z, p.x);
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float limit = shore.Radius(angle) * maxFraction;
            if (r > limit && r > 0.0001f)
            {
                float k = limit / r;
                p = new Vector3(p.x * k, p.y, p.z * k);
            }
            return new Vector3(p.x, IceHeight(p.x, p.z, seed), p.z);
        }

        static void AddShard(MeshBuffer buf, FrozenLakeSettings s, Vector3 at, ref Rng rng)
        {
            int n = rng.Range(4, 7);
            float size = Mathf.Max(0.05f, s.shardSize * rng.Range(0.65f, 1.5f));
            float thickness = size * rng.Range(0.18f, 0.38f);

            var top = new Vector3[n];
            var bottom = new Vector3[n];
            float a0 = rng.Value() * Mathf.PI * 2f;
            for (int j = 0; j < n; j++)
            {
                float angle = a0 + j * Mathf.PI * 2f / n + rng.Signed(0.28f);
                float r = size * rng.Range(0.55f, 1f);
                top[j] = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                bottom[j] = new Vector3(top[j].x, -thickness, top[j].z);
            }

            // Heave the slab over on a random horizontal hinge.
            float tiltAxis = rng.Value() * Mathf.PI * 2f;
            Vector3 axis = new Vector3(Mathf.Cos(tiltAxis), 0f, Mathf.Sin(tiltAxis));
            float tilt = rng.Range(28f, 74f) * Mathf.Deg2Rad;
            Rotate(top, axis, tilt);
            Rotate(bottom, axis, tilt);

            // Drop it so the highest corner clears the ice by the requested amount and the rest is
            // buried, which is what sells "pushed up through the sheet".
            float highest = float.MinValue;
            for (int j = 0; j < n; j++) if (top[j].y > highest) highest = top[j].y;
            float target = s.shardHeight * rng.Range(0.55f, 1.45f);
            Vector3 offset = new Vector3(at.x, at.y + target - highest, at.z);
            Translate(top, offset);
            Translate(bottom, offset);

            // A steeply heaved slab can dangle a long way under the ice. None of that is visible,
            // so flatten it against the floor rather than letting it pierce the underside.
            if (s.depth > 0f)
            {
                float floorLimit = -s.depth + 0.05f;
                ClampAbove(top, floorLimit);
                ClampAbove(bottom, floorLimit);
            }

            LakeSlot slot = rng.Chance(0.45f) ? LakeSlot.IceDeep : LakeSlot.IcePale;
            float shade = rng.Range(0.92f, 1.1f);

            buf.AddFan(Centroid(top), top, true, slot, shade);
            buf.AddFan(Centroid(bottom), bottom, false, slot, shade * 0.8f);
            for (int j = 0; j < n; j++)
            {
                int j1 = (j + 1) % n;
                buf.AddQuad(top[j], top[j1], bottom[j], bottom[j1], slot, shade * 0.9f);
            }
        }

        // ------------------------------------------------------------------ snow drifts

        static void BuildSnowPatches(MeshBuffer buf, FrozenLakeSettings s, ShoreShape shore, HoleShape hole, ref Rng rng)
        {
            for (int i = 0; i < s.snowPatchCount; i++)
            {
                float angle = rng.Value() * Mathf.PI * 2f;
                float frac = Mathf.Sqrt(rng.Value()) * 0.92f;
                Vector3 at = shore.PointOnIce(angle, frac, s.seed);
                if (InHole(hole, at, s.snowPatchSize * 1.5f)) continue;

                int n = rng.Range(6, 10);
                float size = Mathf.Max(0.05f, s.snowPatchSize * rng.Range(0.55f, 1.5f));
                // Clear the tallest plate step so the drift never z-fights with the ice under it.
                float baseY = at.y + s.plateHeightVariation + 0.02f;

                var ring = new Vector3[n];
                float a0 = rng.Value() * Mathf.PI * 2f;
                for (int j = 0; j < n; j++)
                {
                    float a = a0 + j * Mathf.PI * 2f / n + rng.Signed(0.2f);
                    float r = size * rng.Range(0.6f, 1f);
                    ring[j] = new Vector3(at.x + Mathf.Cos(a) * r, baseY, at.z + Mathf.Sin(a) * r);
                }

                float shade = rng.Range(0.95f, 1.05f);
                Vector3 crown = new Vector3(at.x, baseY + size * rng.Range(0.1f, 0.22f), at.z);
                buf.AddFan(crown, ring, true, LakeSlot.Snow, shade);

                // A short skirt tucks the drift under the ice line instead of leaving a floating edge.
                float hemY = baseY - s.plateHeightVariation - 0.06f;
                for (int j = 0; j < n; j++)
                {
                    int j1 = (j + 1) % n;
                    Vector3 c = new Vector3(ring[j].x, hemY, ring[j].z);
                    Vector3 d = new Vector3(ring[j1].x, hemY, ring[j1].z);
                    buf.AddQuad(ring[j], ring[j1], c, d, LakeSlot.Snow, shade * 0.92f);
                }
            }
        }

        // ------------------------------------------------------------------ rocks

        static void BuildRocks(MeshBuffer buf, FrozenLakeSettings s, ShoreShape shore, HoleShape hole, ref Rng rng)
        {
            for (int i = 0; i < s.rockCount; i++)
            {
                float angle = rng.Value() * Mathf.PI * 2f;
                Vector3 at;
                if (rng.Chance(s.rockOnIceRatio))
                {
                    at = shore.PointOnIce(angle, Mathf.Sqrt(rng.Value()) * 0.85f, s.seed);
                }
                else
                {
                    // Sit it on the berm, biased toward the crest.
                    float w = rng.Range(0.22f, 0.85f);
                    float r = shore.Radius(angle) + s.bankWidth * w;
                    float h = s.bankHeight * Mathf.Sin(Mathf.PI * Mathf.Pow(w, 0.7f));
                    at = new Vector3(Mathf.Cos(angle) * r, h, Mathf.Sin(angle) * r);
                }
                if (InHole(hole, at, s.rockSize * 2.1f)) continue;
                AddRock(buf, s, at, ref rng);
            }
        }

        static void AddRock(MeshBuffer buf, FrozenLakeSettings s, Vector3 at, ref Rng rng)
        {
            const int lat = 4;
            const int lon = 6;

            float size = Mathf.Max(0.02f, s.rockSize * rng.Range(0.55f, 1.6f));
            float sx = size * rng.Range(0.8f, 1.3f);
            float sy = size * rng.Range(0.45f, 0.85f);
            float sz = size * rng.Range(0.8f, 1.3f);
            float spin = rng.Value() * Mathf.PI * 2f;

            var grid = new Vector3[lat + 1][];
            for (int i = 0; i <= lat; i++)
            {
                grid[i] = new Vector3[lon];
                float phi = Mathf.PI * i / lat;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                // Poles share one bump so they collapse to a single point instead of fraying into
                // a crown of slivers.
                bool pole = (i == 0 || i == lat);
                float poleBump = rng.Range(0.78f, 1.18f);

                for (int j = 0; j < lon; j++)
                {
                    float theta = spin + j * Mathf.PI * 2f / lon;
                    float bump = pole ? poleBump : rng.Range(0.78f, 1.18f);
                    float x = sinPhi * Mathf.Cos(theta) * sx * bump;
                    float y = cosPhi * sy * bump;
                    float z = sinPhi * Mathf.Sin(theta) * sz * bump;
                    grid[i][j] = new Vector3(at.x + x, at.y + y, at.z + z);
                }
            }

            // Bury part of it so boulders read as embedded rather than dropped on the surface,
            // but never so far that the hidden underside pierces the floor of the block.
            float sink = sy * rng.Range(0.25f, 0.8f);
            float lowest = float.MaxValue;
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j < lon; j++)
                    lowest = Mathf.Min(lowest, grid[i][j].y);
            if (s.depth > 0f) sink = Mathf.Min(sink, Mathf.Max(0f, lowest + s.depth - 0.05f));

            for (int i = 0; i <= lat; i++)
                for (int j = 0; j < lon; j++)
                    grid[i][j] = new Vector3(grid[i][j].x, grid[i][j].y - sink, grid[i][j].z);

            float shade = rng.Range(0.8f, 1.15f);
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int j1 = (j + 1) % lon;
                    buf.AddQuad(grid[i][j], grid[i][j1], grid[i + 1][j], grid[i + 1][j1],
                                LakeSlot.Rock, shade, ((i + j) & 1) == 0);
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Centroid(IList<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return sum / points.Count;
        }

        static void Translate(Vector3[] points, Vector3 offset)
        {
            for (int i = 0; i < points.Length; i++) points[i] += offset;
        }

        static void ClampAbove(Vector3[] points, float minY)
        {
            for (int i = 0; i < points.Length; i++)
                if (points[i].y < minY)
                    points[i] = new Vector3(points[i].x, minY, points[i].z);
        }

        /// <summary>Rodrigues rotation. A proper rotation, so triangle winding is preserved.</summary>
        static void Rotate(Vector3[] points, Vector3 axis, float radians)
        {
            Vector3 k = axis.normalized;
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 v = points[i];
                points[i] = v * c + Vector3.Cross(k, v) * s + k * (Vector3.Dot(k, v) * (1f - c));
            }
        }
    }
}
