using System.Collections.Generic;
using UnityEngine;

namespace LavaPond
{
    /// <summary>
    /// Builds the low-poly lava pond geometry. Pure maths in, triangles out: no scene objects, no
    /// asset loading, no global state, so it can be called from the editor, at runtime, or from a
    /// test harness.
    ///
    /// Layout of the finished asset, all in local space with the crust surface at y = 0:
    ///   * a molten sheet a little below the crust, which is what shows through everything above it
    ///   * a cooled crust broken into plates, each plate pulled back from its neighbours so the
    ///     lava underneath reads as a network of glowing cracks
    ///   * plates dropped entirely here and there, leaving open pools
    ///   * a scorched rock rim ringing the shore, sloping back down to y = 0
    ///   * a solid skirt and floor beneath, so the asset reads as a block rather than a paper sheet
    ///   * an optional spatter cone with a molten mouth, plus tipped crust slabs, swelling bubbles
    ///     and boulders scattered on top
    ///
    /// The crack network is the whole trick. Rather than modelling the gaps, every plate keeps the
    /// vertices it owns outright and pulls back the ones it shares with a neighbour, so the sheet
    /// opens up exactly along the plate boundaries and nowhere else.
    /// </summary>
    public static class LavaPondMeshBuilder
    {
        public const int SubmeshCount = 4;

        /// <summary>How much wider the base of the spatter cone is than its mouth.</summary>
        const float VentBaseScale = 2.3f;

        public static MeshBuffer Build(LavaPondSettings settings)
        {
            LavaPondSettings s = settings ?? new LavaPondSettings();
            var rng = new Rng(s.seed);
            var buf = new MeshBuffer(SubmeshCount, s.uvScale, s.flowAngle);

            var shore = new PondShore(s, ref rng);

            // Takes no rng of its own and is not passed to the plates, so a pond with no rivers
            // running into it builds the same mesh it always did, down to the vertex.
            var inlets = new PondInletField(s, shore);

            var plates = new PlateField(s, shore, ref rng);
            VentShape vent = s.vent ? new VentShape(s, shore, ref rng) : null;

            // Set before a single triangle is added: every vertex asks it how far in from the shore
            // it is, and a shader reading UV1 uses that to keep its bank crust at the edge of the
            // pool. The band is a fraction of the pond rather than a fixed width so it reads the
            // same on a puddle and on a lake.
            float bankBand = Mathf.Max(0.5f, shore.MeanRadius * 0.3f);
            buf.Bank = p =>
            {
                float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                float shoreR = shore.Radius(Mathf.Atan2(p.z, p.x));
                return (shoreR - r) / bankBand;
            };

            Vector3[] shoreRing = BuildShoreRing(s, shore);
            buf.PondArea = FootprintArea(shoreRing);

            BuildMolten(buf, s, shore);
            BuildCrust(buf, s, shore, plates, inlets, shoreRing, vent, ref rng);
            BuildShoreLip(buf, s, shore, inlets, shoreRing);
            Vector3[] rimOuter = BuildRim(buf, s, shore, inlets, shoreRing, ref rng);
            BuildSkirtAndFloor(buf, s, rimOuter);
            if (vent != null) BuildVent(buf, s, vent, ref rng);
            BuildSlabs(buf, s, shore, plates, vent, ref rng);
            BuildBubbles(buf, s, shore, plates, vent, ref rng);
            BuildRocks(buf, s, shore, inlets, vent, ref rng);

            if (vent != null)
            {
                buf.Vent = new VentInfo
                {
                    Exists = true,
                    Mouth = new Vector3(vent.CenterX, vent.PoolY, vent.CenterZ),
                    Radius = vent.MeanRadius,
                    Height = vent.Height
                };
            }

            // Done last: the footprint it normalises against is only known once every prop has been
            // placed, since a boulder on the rim can be the thing that sets the outer edge.
            if (s.uvMode == PondUVMode.Normalized) buf.NormalizeUVs();

            return buf;
        }

        // ------------------------------------------------------------------ shore

        /// <summary>
        /// The wandering outline of the pond, as a radius per angle.
        ///
        /// Public because a river ending in this pond has to know where the edge of the lava
        /// actually is. A pond of radius 12 at the default irregularity is anywhere from 10.1 to
        /// 13.1 m out depending on the bearing, and it is turned and scaled besides, so the edge is
        /// not somewhere that can be judged from the scene view.
        /// </summary>
        public sealed class PondShore
        {
            readonly float _radius;
            readonly float[] _amp;
            readonly float[] _phase;
            readonly int[] _freq;

            public PondShore(LavaPondSettings s, ref Rng rng)
            {
                _radius = Mathf.Max(0.01f, s.radius);
                _freq = new[] { 2, 3, 5, 7 };
                _amp = new float[_freq.Length];
                _phase = new float[_freq.Length];

                // Weight the low harmonics heavily so the pond gets a readable overall shape
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
            /// Per-angle multiplier on the rim width. Spoil piles up deep on one shore and thins out
            /// on another, which is what stops the rim reading as a machined ring.
            /// </summary>
            public float RimScale(float angle, int seed)
            {
                float n = PondNoise.Signed(Mathf.Cos(angle) * 1.7f + 8.3f, Mathf.Sin(angle) * 1.7f - 3.1f, seed);
                return Mathf.Clamp(1f + n * 0.55f, 0.35f, 1.6f);
            }

            public Vector3 PointOnCrust(float angle, float radiusFraction, int seed)
            {
                float r = Radius(angle) * radiusFraction;
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;
                return new Vector3(x, CrustHeight(x, z, seed), z);
            }
        }

        // ------------------------------------------------------------------ shore queries

        /// <summary>
        /// The shoreline these settings produce, without building any geometry.
        ///
        /// The shore is the first thing <see cref="Build"/> takes off the rng, which is what makes
        /// re-rolling it here give back the same outline the mesh was built with. Anything added to
        /// the build ahead of it would silently break that.
        /// </summary>
        public static PondShore CreateShore(LavaPondSettings settings)
        {
            LavaPondSettings s = settings ?? new LavaPondSettings();
            var rng = new Rng(s.seed);
            return new PondShore(s, ref rng);
        }

        /// <summary>The inlets these settings carry, resolved against their shoreline.</summary>
        public static PondInletField CreateInlets(LavaPondSettings settings, PondShore shore)
        {
            return new PondInletField(settings ?? new LavaPondSettings(), shore);
        }

        /// <summary>True when a point in the pond's own space is over the lava.</summary>
        public static bool Contains(PondShore shore, Vector3 local)
        {
            return FlatRadius(local) <= shore.Radius(Mathf.Atan2(local.z, local.x));
        }

        /// <summary>
        /// Where a straight run from <paramref name="from"/> along <paramref name="dir"/> crosses
        /// the edge of the lava, in the pond's own space.
        ///
        /// It always answers with the crossing the run *enters* by, which is why it starts its
        /// search well behind the point it was given. A river drawn short of the pond, one drawn a
        /// little past the edge and one drawn clean across the pool all have to land on the same
        /// answer, or the join moves the moment anything is nudged; searching forward from the end
        /// of the route answers the last two with the shore the river leaves by.
        /// </summary>
        public static bool TryCrossShore(PondShore shore, Vector3 from, Vector3 dir, out Vector3 hit)
        {
            hit = from;

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) return false;
            dir.Normalize();

            // Far enough back to be outside the pond whatever was passed in — the shore never
            // reaches half again its mean radius — and far enough on to cross the whole pool.
            float back = shore.MeanRadius * 2f + FlatRadius(from) + 4f;
            float step = Mathf.Max(0.05f, shore.MeanRadius * 0.05f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(back * 2f / step), 1, 8192);

            float outside = -back;
            float inside = 0f;
            bool found = false;

            for (int i = 1; i <= steps; i++)
            {
                float d = -back + i * step;
                if (!Contains(shore, from + dir * d)) { outside = d; continue; }

                inside = d;
                found = true;
                break;
            }

            if (!found) return false;

            // Bisection: the shoreline is smooth between samples, and a centimetre is well past
            // anything the mesh can show.
            for (int i = 0; i < 32; i++)
            {
                float mid = (outside + inside) * 0.5f;
                if (Contains(shore, from + dir * mid)) inside = mid;
                else outside = mid;
            }

            hit = from + dir * inside;
            return true;
        }

        /// <summary>
        /// Local height of the molten surface at a point: the level a river feeding the pond has to
        /// arrive at for the two to read as one body of lava rather than two meshes.
        /// </summary>
        public static float LavaSurfaceY(LavaPondSettings settings, float x, float z)
        {
            return MoltenY(x, z, settings ?? new LavaPondSettings());
        }

        /// <summary>Local height of the crust surface at a point, which the lava sits below.</summary>
        public static float CrustSurfaceY(LavaPondSettings settings, float x, float z)
        {
            LavaPondSettings s = settings ?? new LavaPondSettings();
            return CrustHeight(x, z, s.seed);
        }

        static float FlatRadius(Vector3 local)
        {
            return Mathf.Sqrt(local.x * local.x + local.z * local.z);
        }

        // ------------------------------------------------------------------ inlets

        /// <summary>
        /// Where rivers pour in, and what that does to the shore around them.
        ///
        /// Deliberately narrow in what it can touch. It notches the rim's height, drops the shore
        /// lip and cuts crust triangles, and that is all: it never moves the shoreline, never
        /// changes the rim's footprint — so the pond still sits flush on the ground and its skirt
        /// stays put — and never consumes a random number, so nothing outside the mouth shifts when
        /// a river is added, moved or taken away.
        /// </summary>
        public sealed class PondInletField
        {
            struct Mouth
            {
                public float Angle;      // radians, Atan2(z, x) like the shore
                public float HalfWidth;  // metres across the mouth
                public float Reach;      // metres the melt fan runs into the pond
                public Vector2 Point;    // where the mouth sits on the shoreline
                public Vector2 Inward;   // unit vector from the mouth toward the middle
            }

            readonly Mouth[] _mouths;

            public PondInletField(LavaPondSettings s, PondShore shore)
            {
                List<PondInlet> list = s.inlets;
                int n = list != null ? list.Count : 0;
                var mouths = new List<Mouth>(n);

                for (int i = 0; i < n; i++)
                {
                    PondInlet inlet = list[i];
                    float half = Mathf.Max(0f, inlet.halfWidth);
                    if (half <= 1e-3f) continue;

                    float a = inlet.angleDeg * Mathf.Deg2Rad;
                    float r = shore.Radius(a);
                    var point = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                    Vector2 inward = point.sqrMagnitude > 1e-6f ? -point.normalized : Vector2.right;

                    // A mouth wider than the pond would open the whole shore at once, which is a
                    // pond with no bank rather than a pond with a river running into it.
                    half = Mathf.Min(half, shore.MeanRadius * 0.9f);

                    mouths.Add(new Mouth
                    {
                        Angle = a,
                        HalfWidth = half,
                        Reach = Mathf.Max(0f, inlet.reach),
                        Point = point,
                        Inward = inward
                    });
                }

                _mouths = mouths.ToArray();
            }

            public bool Any { get { return _mouths.Length > 0; } }

            /// <summary>
            /// How open the shore is at this angle: 0 where the bank is untouched, 1 in the middle
            /// of a mouth. Measured as an arc length rather than as an angle, so a mouth of a given
            /// width covers the same stretch of shore whatever size the pond is.
            /// </summary>
            public float Openness(float angle, PondShore shore)
            {
                float best = 0f;
                for (int i = 0; i < _mouths.Length; i++)
                {
                    Mouth m = _mouths[i];
                    float arc = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, m.Angle * Mathf.Rad2Deg))
                                * Mathf.Deg2Rad * Mathf.Max(0.01f, shore.Radius(angle));

                    // Fully open across the river itself, feathering out over half as much again,
                    // so the bank climbs back rather than stepping up beside the channel.
                    float edge = m.HalfWidth * 1.6f;
                    float t = Mathf.Clamp01((arc - m.HalfWidth) / Mathf.Max(0.01f, edge - m.HalfWidth));
                    float open = 1f - Mathf.SmoothStep(0f, 1f, t);
                    if (open > best) best = open;
                }
                return best;
            }

            /// <summary>
            /// How hard the arriving lava keeps the crust open at a point in the pond, 0 to 1. Full
            /// strength across the width of the river, fading with distance from the mouth, and the
            /// two measured separately.
            ///
            /// Not a radial falloff: that leaves crust standing along both sides of the channel from
            /// the moment it enters, so the river arrives through a gap narrower than itself.
            /// </summary>
            public float Melt(float x, float z)
            {
                float best = 0f;
                for (int i = 0; i < _mouths.Length; i++)
                {
                    Mouth m = _mouths[i];
                    if (m.Reach <= 1e-3f) continue;

                    float dx = x - m.Point.x;
                    float dz = z - m.Point.y;

                    float along = dx * m.Inward.x + dz * m.Inward.y;
                    float across = dx * -m.Inward.y + dz * m.Inward.x;

                    // Everything behind the shoreline counts as at the mouth: that is the river
                    // itself, and the fan should not begin halfway across the pond.
                    along = Mathf.Max(0f, along);

                    float u = Mathf.Clamp01(along / m.Reach);
                    float spread = m.HalfWidth * Mathf.Lerp(1f, 1.8f, u);
                    float v = Mathf.Abs(across) / Mathf.Max(0.01f, spread);

                    // Note Unity's SmoothStep interpolates between its first two arguments rather
                    // than treating them as edges, so the edges are applied to t by hand.
                    float lane = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.65f) / 0.6f));
                    float run = 1f - Mathf.SmoothStep(0f, 1f, u);

                    float melt = lane * run;
                    if (melt > best) best = melt;
                }
                return best;
            }
        }

        /// <summary>Gentle undulation of the crust surface, before per-plate offsets.</summary>
        static float CrustHeight(float x, float z, int seed)
        {
            return PondNoise.Signed(x * 0.18f, z * 0.18f, seed) * 0.035f;
        }

        /// <summary>
        /// Height of the molten sheet under the crust. Always strictly below y = 0 so the lava can
        /// never poke up through a plate, however hard the turbulence is pushed.
        /// </summary>
        static float MoltenY(float x, float z, LavaPondSettings s)
        {
            float n = PondNoise.Signed(x * 0.24f, z * 0.24f, s.seed + 2207);
            float y = -s.crustThickness + n * s.moltenTurbulence * s.crustThickness * 0.55f;
            return Mathf.Min(y, -0.02f);
        }

        // ------------------------------------------------------------------ vent

        /// <summary>
        /// The spatter cone, or null when the pond has none. A ragged mouth with a pool of lava
        /// sitting inside it, standing on a cone of the crust it threw up building itself.
        /// </summary>
        sealed class VentShape
        {
            readonly float _radius;
            readonly float[] _amp;
            readonly float[] _phase;
            readonly int[] _freq;

            public readonly float CenterX;
            public readonly float CenterZ;
            public readonly float Height;

            /// <summary>Height of the lava sitting in the mouth, below the lip.</summary>
            public readonly float PoolY;

            public VentShape(LavaPondSettings s, PondShore shore, ref Rng rng)
            {
                // Capped so the cone's base always lands on crust. A vent wider than the pond would
                // leave the cone hanging over the rim with nothing to stand on.
                _radius = Mathf.Clamp(s.ventRadius, 0.1f, shore.MeanRadius * 0.38f);
                Height = Mathf.Max(0.05f, s.ventHeight);
                PoolY = Height - Mathf.Min(Height * 0.3f, _radius * 0.4f);

                // Keep the whole base clear of the shoreline, whatever the offset and radius ask for.
                float maxOffset = Mathf.Max(0f, shore.MeanRadius * 0.55f - BaseRadius * 1.15f);
                float ox = Mathf.Clamp(s.ventOffsetX, -0.5f, 0.5f) * shore.MeanRadius;
                float oz = Mathf.Clamp(s.ventOffsetZ, -0.5f, 0.5f) * shore.MeanRadius;
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
                    _amp[i] = s.ventIrregularity * weights[i] * rng.Range(0.6f, 1.4f);
                    _phase[i] = rng.Value() * Mathf.PI * 2f;
                }
            }

            /// <summary>Radius of the mouth at the given angle.</summary>
            public float Radius(float angle)
            {
                float f = 1f;
                for (int i = 0; i < _freq.Length; i++)
                    f += _amp[i] * Mathf.Sin(angle * _freq[i] + _phase[i]);
                return _radius * Mathf.Max(0.4f, f);
            }

            /// <summary>Average radius of the mouth, after clamping.</summary>
            public float MeanRadius { get { return _radius; } }

            /// <summary>Average radius of the cone where it meets the crust.</summary>
            public float BaseRadius { get { return _radius * VentBaseScale; } }

            /// <summary>True when the point falls under the cone, optionally grown by a margin.</summary>
            public bool ContainsBase(float x, float z, float grow)
            {
                float dx = x - CenterX;
                float dz = z - CenterZ;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r < 0.0001f) return true;
                return r < Radius(Mathf.Atan2(dz, dx)) * VentBaseScale * grow;
            }

            /// <summary>True when a prop of the given horizontal reach would foul the cone.</summary>
            public bool Blocks(Vector3 at, float reach)
            {
                float dx = at.x - CenterX;
                float dz = at.z - CenterZ;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d - reach < BaseRadius) return true;
                return ContainsBase(at.x, at.z, 1.05f);
            }

            /// <summary>The mouth outline as a closed loop at the given height and radius scale.</summary>
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
        /// A scattering of Voronoi sites over the pond. Every triangle takes the height, material
        /// and identity of its nearest site, which is what turns a smooth sheet into a raft of
        /// separate cooled plates.
        /// </summary>
        sealed class PlateField
        {
            readonly Vector2[] _sites;
            readonly float[] _height;
            readonly Vector2[] _tilt;
            readonly LavaSlot[] _slot;
            readonly float[] _shade;
            readonly bool[] _open;
            readonly float _maxOffset;

            public PlateField(LavaPondSettings s, PondShore shore, ref Rng rng)
            {
                int n = Mathf.Max(1, s.plateCount);
                _sites = new Vector2[n];
                _height = new float[n];
                _tilt = new Vector2[n];
                _slot = new LavaSlot[n];
                _shade = new float[n];
                _open = new bool[n];
                _maxOffset = s.plateHeightVariation * 2.2f;

                for (int i = 0; i < n; i++)
                {
                    float angle = rng.Value() * Mathf.PI * 2f;
                    // sqrt keeps the sites area-uniform instead of bunching at the centre.
                    float frac = Mathf.Sqrt(rng.Value());
                    float r = shore.Radius(angle) * frac;
                    _sites[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                    _height[i] = rng.Signed(s.plateHeightVariation);

                    // Each plate is a slightly tipped raft rather than a flat step, so neighbours
                    // catch the light differently and the cracks between them read at a glance.
                    float slopeScale = s.plateHeightVariation / Mathf.Max(1f, shore.MeanRadius * 0.22f);
                    _tilt[i] = new Vector2(rng.Signed(slopeScale), rng.Signed(slopeScale));
                    _shade[i] = rng.Range(0.82f, 1.1f);

                    // Whether this plate skinned over at all. Coverage is the whole of the answer
                    // in the middle of the pond; out at the shore, the coolest part and the first
                    // to skin, it is biased toward crusting sooner. The boundary wanders by a
                    // fraction of the band rather than running as a clean circle.
                    //
                    // The bias is a power of the coverage rather than something added to it, and
                    // that is the load-bearing part: a power still lands on 0 at 0 and on 1 at 1,
                    // so the shore can never hold a ring of crust on a pond asked for none, nor
                    // leave a hole in one asked to be solid. Both ends of the slider stay
                    // reachable whatever the band is set to, which is what an amount control has
                    // to do.
                    float coverage = Mathf.Clamp01(s.crustCoverage);
                    float band = Mathf.Clamp01(s.shoreCrustBand);
                    float wander = PondNoise.Signed(_sites[i].x * 0.12f, _sites[i].y * 0.12f,
                                                    s.seed + 313) * band * 0.5f;
                    float shoreness = band <= 1e-4f
                        ? 0f
                        : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((frac + wander - (1f - band)) / band));
                    float crustChance = Mathf.Lerp(coverage, Mathf.Pow(coverage, 0.35f), shoreness);
                    _open[i] = !rng.Chance(crustChance);
                }

                // Second pass: crust next to open lava has had least time to cool, so it is the
                // crust that still glows. Needs every open flag settled first, hence the two passes.
                float heatRange = Mathf.Max(0.01f, shore.MeanRadius * 0.45f);
                for (int i = 0; i < n; i++)
                {
                    if (_open[i]) { _slot[i] = LavaSlot.Molten; continue; }

                    float nearestOpen = float.MaxValue;
                    for (int j = 0; j < n; j++)
                    {
                        if (!_open[j]) continue;
                        float dx = _sites[j].x - _sites[i].x;
                        float dz = _sites[j].y - _sites[i].y;
                        float d = dx * dx + dz * dz;
                        if (d < nearestOpen) nearestOpen = d;
                    }

                    float proximity = nearestOpen == float.MaxValue
                        ? 0f
                        : Mathf.Clamp01(1f - Mathf.Sqrt(nearestOpen) / heatRange);
                    float warmChance = s.warmCrustRatio * (0.35f + 0.65f * proximity);
                    _slot[i] = rng.Chance(warmChance) ? LavaSlot.CrustWarm : LavaSlot.CrustDark;
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

            /// <summary>Height of plate <paramref name="i"/>'s raft evaluated at a point on the crust.</summary>
            public float OffsetAt(int i, float x, float z)
            {
                float o = _height[i] + _tilt[i].x * (x - _sites[i].x) + _tilt[i].y * (z - _sites[i].y);
                return Mathf.Clamp(o, -_maxOffset, _maxOffset);
            }

            public Vector2 Site(int i) { return _sites[i]; }
            public LavaSlot Slot(int i) { return _slot[i]; }
            public float Shade(int i) { return _shade[i]; }

            /// <summary>True when no crust formed here at all, leaving open lava.</summary>
            public bool IsOpen(int i) { return _open[i]; }
        }

        // ------------------------------------------------------------------ molten sheet

        /// <summary>
        /// The lava itself: one disc sitting below the crust, seen through every crack and pool
        /// above it. It runs a little past the shoreline so the rim never has a seam to show.
        /// </summary>
        static void BuildMolten(MeshBuffer buf, LavaPondSettings s, PondShore shore)
        {
            int seg = Mathf.Max(3, s.angularSegments);
            int rings = Mathf.Max(2, s.radialRings);
            float angleStep = Mathf.PI * 2f / seg;

            // Tucked under the rim rather than merely reaching it, but never so far that it would
            // escape past the rim's outer edge and hang in the air.
            float pad = Mathf.Min(0.4f, Mathf.Max(0.02f, s.rimWidth * 0.5f));

            var grid = new Vector3[rings + 1][];
            for (int i = 0; i <= rings; i++)
            {
                grid[i] = new Vector3[seg];
                float t = (float)i / rings;
                for (int j = 0; j < seg; j++)
                {
                    float angle = j * angleStep;
                    float r = shore.Radius(angle) * t;
                    if (i == rings) r += pad;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;
                    grid[i][j] = new Vector3(x, MoltenY(x, z, s), z);
                }
            }

            // Shade the lava from noise rather than the rng: neighbouring facets agreeing is what
            // makes it read as a moving surface instead of static confetti.
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int j1 = (j + 1) % seg;
                    Vector3 a = grid[i][j];
                    Vector3 b = grid[i][j1];
                    Vector3 c = grid[i + 1][j];
                    Vector3 d = grid[i + 1][j1];

                    float qx = (a.x + b.x + c.x + d.x) * 0.25f;
                    float qz = (a.z + b.z + c.z + d.z) * 0.25f;
                    float shade = 0.88f + PondNoise.Value(qx * 0.3f, qz * 0.3f, s.seed + 6151) * 0.26f;

                    if (i == 0) buf.AddTriangle(a, d, c, LavaSlot.Molten, shade);
                    else buf.AddQuad(a, b, c, d, LavaSlot.Molten, shade, ((i + j) & 1) == 0);
                }
            }
        }

        // ------------------------------------------------------------------ crust

        /// <summary>The rim shared by the crust and the rock bank, so the two weld cleanly.</summary>
        static Vector3[] BuildShoreRing(LavaPondSettings s, PondShore shore)
        {
            int seg = Mathf.Max(3, s.angularSegments);
            var ring = new Vector3[seg];
            for (int j = 0; j < seg; j++)
            {
                float angle = j * Mathf.PI * 2f / seg;
                ring[j] = shore.PointOnCrust(angle, 1f, s.seed);
            }
            return ring;
        }

        static void BuildCrust(MeshBuffer buf, LavaPondSettings s, PondShore shore, PlateField plates,
                               PondInletField inlets, Vector3[] shoreRing, VentShape vent, ref Rng rng)
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
                    if (i == 0) grid[i][j] = new Vector3(0f, CrustHeight(0f, 0f, s.seed), 0f);
                    else if (i == rings) grid[i][j] = shoreRing[j];
                    else grid[i][j] = shore.PointOnCrust(j * angleStep, t, s.seed);
                }
            }

            // ...then shove the interior vertices around so the facets stop reading as a fan, and
            // so the cracks that open along them wander instead of running radially.
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
                    limit *= MaxOffsetFraction * Mathf.Clamp01(s.crustJitter);

                    float dir = rng.Value() * Mathf.PI * 2f;
                    float mag = rng.Value() * limit;
                    float x = p.x + Mathf.Cos(dir) * mag;
                    float z = p.z + Mathf.Sin(dir) * mag;
                    grid[i][j] = new Vector3(x, CrustHeight(x, z, s.seed), z);
                }
            }

            var sheet = new CrustSheet(seg, rings, grid);

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

            sheet.Emit(buf, plates, shore, s, inlets, vent);
        }

        /// <summary>
        /// The crust held as indices into a shared vertex grid. Working in indices rather than
        /// positions is what makes the cracks possible: a vertex can be recognised as shared
        /// between two plates, and each plate can then place its own copy of it.
        /// </summary>
        sealed class CrustSheet
        {
            readonly int _seg;
            readonly int _rings;
            readonly Vector3[] _verts;
            readonly List<int> _tris = new List<int>();

            public CrustSheet(int seg, int rings, Vector3[][] grid)
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

            /// <summary>True for the outermost ring, which welds to the rock rim.</summary>
            bool IsShore(int index)
            {
                return index >= 1 + (_rings - 1) * _seg;
            }

            public void Add(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return; // collapsed by the centre fan
                _tris.Add(a); _tris.Add(b); _tris.Add(c);
            }

            /// <summary>
            /// Splits the sheet into plates, pulls each plate back from its neighbours and hands the
            /// result to the mesh. Plates that never formed, and anything buried under the vent, are
            /// dropped on the way through.
            /// </summary>
            public void Emit(MeshBuffer buf, PlateField plates, PondShore shore,
                             LavaPondSettings s, PondInletField inlets, VentShape vent)
            {
                int triCount = _tris.Count / 3;
                var triPlate = new int[triCount];
                var triCentre = new Vector2[triCount];
                var firstPlate = new int[_verts.Length];
                var shared = new bool[_verts.Length];
                for (int i = 0; i < firstPlate.Length; i++) firstPlate[i] = -1;

                // Every vertex used by more than one plate sits on a boundary, and it is those, and
                // only those, that get pulled back. Worked out over the whole sheet before anything
                // is dropped, so a crack does not change shape just because the plate on the far
                // side of it turned out to be an open pool.
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = _tris[t * 3], i1 = _tris[t * 3 + 1], i2 = _tris[t * 3 + 2];
                    Vector3 a = _verts[i0], b = _verts[i1], c = _verts[i2];
                    var centre = new Vector2((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f);
                    int p = plates.Nearest(centre.x, centre.y);

                    triPlate[t] = p;
                    triCentre[t] = centre;
                    MarkShared(firstPlate, shared, i0, p);
                    MarkShared(firstPlate, shared, i1, p);
                    MarkShared(firstPlate, shared, i2, p);
                }

                var byPlate = new Dictionary<int, List<int>>();
                for (int t = 0; t < triCount; t++)
                {
                    int p = triPlate[t];
                    if (plates.IsOpen(p)) continue;
                    if (vent != null && vent.ContainsBase(triCentre[t].x, triCentre[t].y, 1f)) continue;

                    // The fan of open lava in front of a river's mouth, cut triangle by triangle.
                    // Dropping whole plates instead would be far too coarse — a plate is metres
                    // across, and one whose middle sits outside the fan still lays crust over most
                    // of it — and choosing which plates form would move every random number after
                    // it, reshuffling the whole pond. The noise stops the edge reading as a stencil.
                    if (inlets.Any)
                    {
                        float melt = inlets.Melt(triCentre[t].x, triCentre[t].y);
                        float ragged = PondNoise.Signed(triCentre[t].x * 0.32f, triCentre[t].y * 0.32f,
                                                        s.seed + 5471) * 0.22f;
                        if (melt + ragged > 0.5f) continue;
                    }

                    List<int> list;
                    if (!byPlate.TryGetValue(p, out list))
                    {
                        list = new List<int>();
                        byPlate[p] = list;
                    }
                    list.Add(t);
                }

                var placed = new Dictionary<int, Vector3>();
                var edges = new Dictionary<long, int>();

                foreach (var entry in byPlate)
                {
                    int plate = entry.Key;
                    List<int> tris = entry.Value;
                    placed.Clear();
                    edges.Clear();

                    for (int k = 0; k < tris.Count; k++)
                    {
                        int t = tris[k];
                        for (int c = 0; c < 3; c++)
                        {
                            int v = _tris[t * 3 + c];
                            if (!placed.ContainsKey(v))
                                placed[v] = Place(v, plate, shared[v], plates, shore, s);
                        }
                    }

                    LavaSlot slot = plates.Slot(plate);
                    float shade = plates.Shade(plate);
                    for (int k = 0; k < tris.Count; k++)
                    {
                        int t = tris[k];
                        int i0 = _tris[t * 3], i1 = _tris[t * 3 + 1], i2 = _tris[t * 3 + 2];
                        buf.AddTriangle(placed[i0], placed[i1], placed[i2], slot, shade);
                        buf.CrustArea += FlatArea(placed[i0], placed[i1], placed[i2]);
                        Bump(edges, i0, i1);
                        Bump(edges, i1, i2);
                        Bump(edges, i2, i0);
                    }

                    AddPlateEdge(buf, placed, edges, plates.Site(plate), s);
                }
            }

            /// <summary>
            /// Hangs a wall off every edge of the plate that no second triangle shares, down past
            /// the lava. Without it a plate is a sheet of paper floating over the pool; with it the
            /// crust has a thickness you can see down the side of, which is most of what sells the
            /// cracks.
            /// </summary>
            static void AddPlateEdge(MeshBuffer buf, Dictionary<int, Vector3> placed,
                                     Dictionary<long, int> edges, Vector2 site, LavaPondSettings s)
            {
                foreach (var pair in edges)
                {
                    if (pair.Value != 1) continue;
                    int p = (int)(pair.Key >> 32);
                    int q = (int)(pair.Key & 0xFFFFFFFFL);

                    Vector3 top0 = placed[p];
                    Vector3 top1 = placed[q];
                    var bot0 = new Vector3(top0.x, MoltenY(top0.x, top0.z, s) - 0.05f, top0.z);
                    var bot1 = new Vector3(top1.x, MoltenY(top1.x, top1.z, s) - 0.05f, top1.z);

                    // Boundary edges come out in arbitrary order, so orient by where the plate's
                    // centre is rather than trusting the winding.
                    float mx = (top0.x + top1.x) * 0.5f;
                    float mz = (top0.z + top1.z) * 0.5f;
                    var outward = new Vector3(mx - site.x, 0f, mz - site.y);
                    AddOrientedQuad(buf, top0, top1, bot0, bot1, outward, LavaSlot.CrustWarm, 0.9f);
                }
            }

            /// <summary>
            /// Puts one corner of a plate down. A corner the plate shares with a neighbour is pulled
            /// back toward the plate's own centre, opening half a crack; the neighbour pulls its own
            /// copy the other way and opens the other half. Corners on the shoreline are left alone,
            /// since that edge belongs to the rim and must stay welded to it.
            /// </summary>
            Vector3 Place(int index, int plate, bool isShared, PlateField plates, PondShore shore,
                          LavaPondSettings s)
            {
                Vector3 v = _verts[index];
                float x = v.x;
                float z = v.z;

                if (isShared && !IsShore(index) && s.crackWidth > 0f)
                {
                    Vector2 site = plates.Site(plate);
                    float dx = site.x - x;
                    float dz = site.y - z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > 0.0001f)
                    {
                        // Never more than part of the way to the site, so a small plate shrinks
                        // rather than turning itself inside out.
                        float pull = Mathf.Min(s.crackWidth * 0.5f, d * 0.45f);
                        x += dx / d * pull;
                        z += dz / d * pull;
                    }
                }

                // Fade the plate's height offset out at the shoreline so the crust always meets the
                // rim flush instead of stepping away from it.
                float r = Mathf.Sqrt(x * x + z * z);
                float shoreR = shore.Radius(Mathf.Atan2(z, x));
                float fade = Mathf.Clamp01((1f - r / Mathf.Max(0.001f, shoreR)) / 0.12f);
                float y = CrustHeight(x, z, s.seed) + plates.OffsetAt(plate, x, z) * fade;
                return new Vector3(x, y, z);
            }

            static void MarkShared(int[] firstPlate, bool[] shared, int v, int plate)
            {
                if (firstPlate[v] < 0) firstPlate[v] = plate;
                else if (firstPlate[v] != plate) shared[v] = true;
            }

            static void Bump(Dictionary<long, int> counts, int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int n;
                counts.TryGetValue(key, out n);
                counts[key] = n + 1;
            }
        }

        /// <summary>
        /// The inside face of the shoreline: a wall hanging from the shore ring down past the
        /// molten surface, facing in across the pond.
        ///
        /// On a pond that has skinned over this is buried under the crust and costs a few dozen
        /// triangles for nothing. It is there for the pond that has not: with the coverage turned
        /// down, the shoreline is an open edge with the lava sitting a plate's thickness below it,
        /// and without this you look under the rim and straight out through the far side of the
        /// block. It is also what gives an open lake a visible lip, so Crust Thickness still reads
        /// as a depth rather than as nothing at all.
        /// </summary>
        static void BuildShoreLip(MeshBuffer buf, LavaPondSettings s, PondShore shore,
                                  PondInletField inlets, Vector3[] shoreRing)
        {
            int seg = shoreRing.Length;
            float angleStep = Mathf.PI * 2f / seg;

            for (int j = 0; j < seg; j++)
            {
                // Where a river runs in there is no lip. The lava is continuous through the mouth,
                // and a wall standing across it is exactly the edge a river appears to overlap.
                // Nothing is left open by dropping it: the crust in front of the mouth has gone
                // too, so there is no plate edge here to look under.
                if (inlets.Any &&
                    Mathf.Max(inlets.Openness(j * angleStep, shore),
                              inlets.Openness((j + 1) * angleStep, shore)) > 0.5f) continue;

                Vector3 top0 = shoreRing[j];
                Vector3 top1 = shoreRing[(j + 1) % seg];
                var bot0 = new Vector3(top0.x, MoltenY(top0.x, top0.z, s) - 0.08f, top0.z);
                var bot1 = new Vector3(top1.x, MoltenY(top1.x, top1.z, s) - 0.08f, top1.z);

                // Faces in toward the middle, which is the only place it is ever seen from. The
                // pond is built around its own origin, so "inward" is simply back toward it.
                var inward = new Vector3(-(top0.x + top1.x) * 0.5f, 0f, -(top0.z + top1.z) * 0.5f);
                AddOrientedQuad(buf, top0, top1, bot0, bot1, inward, LavaSlot.CrustWarm, 0.85f);
            }
        }

        /// <summary>Adds a wall quad, flipping the winding so its face points along <paramref name="toward"/>.</summary>
        static void AddOrientedQuad(MeshBuffer buf, Vector3 top0, Vector3 top1, Vector3 bot0, Vector3 bot1,
                                    Vector3 toward, LavaSlot slot, float shade)
        {
            Vector3 n = Vector3.Cross(top1 - top0, bot0 - top0);
            if (Vector3.Dot(n, toward) < 0f)
                buf.AddQuad(top1, top0, bot1, bot0, slot, shade);
            else
                buf.AddQuad(top0, top1, bot0, bot1, slot, shade);
        }

        /// <summary>Area the shoreline encloses, seen from above. Shoelace over the ring.</summary>
        static float FootprintArea(Vector3[] ring)
        {
            float twice = 0f;
            for (int j = 0; j < ring.Length; j++)
            {
                Vector3 a = ring[j];
                Vector3 b = ring[(j + 1) % ring.Length];
                twice += a.x * b.z - b.x * a.z;
            }
            return Mathf.Abs(twice) * 0.5f;
        }

        /// <summary>Area of a triangle seen from above, which is 0 for anything standing on edge.</summary>
        static float FlatArea(Vector3 a, Vector3 b, Vector3 c)
        {
            return Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f;
        }

        /// <summary>Distance between two points ignoring height, i.e. across the pond plane.</summary>
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// True when a scattered prop would stand where the vent is. Reach is how far the prop's
        /// geometry can extend sideways from its centre.
        /// </summary>
        static bool AtVent(VentShape vent, Vector3 at, float reach)
        {
            return vent != null && vent.Blocks(at, reach);
        }

        // ------------------------------------------------------------------ rim

        /// <summary>Returns the outer ring of the rim, which the skirt hangs from.</summary>
        static Vector3[] BuildRim(MeshBuffer buf, LavaPondSettings s, PondShore shore,
                                  PondInletField inlets, Vector3[] shoreRing, ref Rng rng)
        {
            int seg = shoreRing.Length;
            int rings = Mathf.Max(1, s.rimRings);
            float angleStep = Mathf.PI * 2f / seg;

            var grid = new Vector3[rings + 1][];
            grid[0] = shoreRing;

            for (int i = 1; i <= rings; i++)
            {
                grid[i] = new Vector3[seg];
                float w = (float)i / rings;

                // Rises out of the crust, peaks at roughly 40% across, then settles back to y = 0 at
                // the outer edge so the rim sits flush on flat terrain.
                float profile = Mathf.Sin(Mathf.PI * Mathf.Pow(w, 0.7f));
                float noiseFade = Mathf.Sin(Mathf.PI * w);

                for (int j = 0; j < seg; j++)
                {
                    float angle = j * angleStep;
                    float jitterAngle = angle + rng.Signed(angleStep * 0.3f);

                    float width = s.rimWidth * shore.RimScale(angle, s.seed + 811);
                    float radial = width * w + rng.Signed(width / rings * 0.3f);

                    float r = shore.Radius(jitterAngle) + Mathf.Max(0f, radial);
                    float x = Mathf.Cos(jitterAngle) * r;
                    float z = Mathf.Sin(jitterAngle) * r;

                    float h = s.rimHeight * profile * shore.RimScale(angle, s.seed + 1607);
                    h += PondNoise.Signed(x * 0.35f, z * 0.35f, s.seed + 4001)
                         * s.rimHeight * 0.45f * s.rimRoughness * noiseFade;

                    // A river cuts its way through the bank rather than climbing over it, so the
                    // rim is notched down across the mouth.
                    //
                    // Height only, and faded out by the time it reaches the outer edge. The width
                    // and the outer ring are what the pond stands on: pull those in and the skirt
                    // comes with them, leaving the rim hanging off the ground beside the mouth with
                    // daylight under it. Notching the height alone leaves the footprint exactly
                    // where it was.
                    if (inlets.Any)
                    {
                        // Cut all the way across the bank, and released only over the last quarter
                        // of it. The river lies over the whole rim width at the mouth, so a notch
                        // that shallows out on the way across leaves a ridge under the middle of
                        // the channel for the lava to ride over. The outer ring is already back at
                        // ground level by design, so letting it go changes nothing but keeps the
                        // pond meeting the terrain exactly where it always did.
                        float release = Mathf.Clamp01((1f - w) / 0.25f);
                        float cut = inlets.Openness(angle, shore) * release * Mathf.Clamp01(s.inletRimCut);
                        h = Mathf.Lerp(h, -s.crustThickness * 0.5f, cut);
                    }

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

                    // The ground closest to the lava has not finished cooling, in patches rather
                    // than speckles: sampling noise at the quad keeps neighbouring faces agreeing.
                    float qx = (a.x + b.x + c.x + d.x) * 0.25f;
                    float qz = (a.z + b.z + c.z + d.z) * 0.25f;
                    float heat = 1f - (float)i / rings;
                    float scorch = PondNoise.Value(qx * 0.22f, qz * 0.22f, s.seed + 7919);
                    LavaSlot slot = scorch < heat * 0.55f ? LavaSlot.CrustWarm : LavaSlot.Rock;
                    float shade = rng.Range(0.88f, 1.06f);

                    buf.AddQuad(a, b, c, d, slot, shade, ((i + j) & 1) == 0);
                }
            }

            return grid[rings];
        }

        // ------------------------------------------------------------------ solid body

        static void BuildSkirtAndFloor(MeshBuffer buf, LavaPondSettings s, Vector3[] outer)
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
                buf.AddQuad(outer[j], outer[j1], floor[j], floor[j1], LavaSlot.Rock, 0.82f);
            }

            buf.AddFan(new Vector3(0f, floorY, 0f), floor, false, LavaSlot.Rock, 0.7f);
        }

        // ------------------------------------------------------------------ vent

        /// <summary>
        /// The spatter cone: a rough tower of crust thrown up around the mouth, with lava standing
        /// in it. Built after the crust so it can simply sit on top of the hole left for it.
        /// </summary>
        static void BuildVent(MeshBuffer buf, LavaPondSettings s, VentShape vent, ref Rng rng)
        {
            int seg = Mathf.Max(10, s.angularSegments / 2);
            const int rings = 3;
            float angleStep = Mathf.PI * 2f / seg;

            var grid = new Vector3[rings + 1][];
            for (int i = 0; i <= rings; i++)
            {
                grid[i] = new Vector3[seg];
                float t = (float)i / rings;
                float scale = Mathf.Lerp(VentBaseScale, 1f, t);
                // Concave rather than straight-sided, the way a cone built out of falling spatter
                // piles up steeper the closer it gets to the mouth.
                float y = vent.Height * Mathf.Pow(t, 1.45f);

                for (int j = 0; j < seg; j++)
                {
                    float angle = j * angleStep;
                    float wobble = 1f + PondNoise.Signed(Mathf.Cos(angle) * 2.3f + i * 3.7f,
                                                         Mathf.Sin(angle) * 2.3f - i * 1.9f,
                                                         s.seed + 2749) * 0.14f;
                    float r = vent.Radius(angle) * scale * wobble;
                    float x = vent.CenterX + Mathf.Cos(angle) * r;
                    float z = vent.CenterZ + Mathf.Sin(angle) * r;

                    // Sink the base a touch so the cone never leaves a hairline gap against the crust.
                    float yy = i == 0
                        ? CrustHeight(x, z, s.seed) - 0.06f
                        : y + rng.Signed(vent.Height * 0.06f);
                    grid[i][j] = new Vector3(x, yy, z);
                }
            }

            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int j1 = (j + 1) % seg;
                    Vector3 lo0 = grid[i][j], lo1 = grid[i][j1];
                    Vector3 hi0 = grid[i + 1][j], hi1 = grid[i + 1][j1];

                    var outward = new Vector3((lo0.x + lo1.x) * 0.5f - vent.CenterX, 0f,
                                              (lo0.z + lo1.z) * 0.5f - vent.CenterZ);
                    // The lip glows: it is the part still being resurfaced every time the vent spits.
                    LavaSlot slot = i == rings - 1 ? LavaSlot.CrustWarm : LavaSlot.CrustDark;
                    AddOrientedQuad(buf, hi0, hi1, lo0, lo1, outward, slot, rng.Range(0.85f, 1.05f));
                }
            }

            // Lava standing in the mouth, with a short throat so the pool is clearly down inside
            // the cone rather than painted across the top of it.
            Vector3[] lip = grid[rings];
            var pool = new Vector3[seg];
            for (int j = 0; j < seg; j++)
            {
                pool[j] = new Vector3(vent.CenterX + (lip[j].x - vent.CenterX) * 0.86f, vent.PoolY,
                                      vent.CenterZ + (lip[j].z - vent.CenterZ) * 0.86f);
            }

            for (int j = 0; j < seg; j++)
            {
                int j1 = (j + 1) % seg;
                var inward = new Vector3(vent.CenterX - (lip[j].x + lip[j1].x) * 0.5f, 0f,
                                         vent.CenterZ - (lip[j].z + lip[j1].z) * 0.5f);
                AddOrientedQuad(buf, lip[j], lip[j1], pool[j], pool[j1], inward, LavaSlot.CrustWarm, 0.8f);
            }

            buf.AddFan(new Vector3(vent.CenterX, vent.PoolY + 0.02f, vent.CenterZ), pool, true,
                       LavaSlot.Molten, 1.12f);
        }

        // ------------------------------------------------------------------ crust slabs

        /// <summary>
        /// Worst-case horizontal reach of a slab: its widest polygon radius plus its thickness,
        /// since tilting only ever pulls a slab in.
        /// </summary>
        static float SlabReach(LavaPondSettings s)
        {
            float maxSize = Mathf.Max(0.05f, s.slabSize * 1.5f);
            return maxSize * 1.38f;
        }

        /// <summary>
        /// Plates of crust that buckled and tipped up. Along a ridge for the most part, the way they
        /// pile up where two rafts of crust are being pushed together, with the rest scattered.
        /// </summary>
        static void BuildSlabs(MeshBuffer buf, LavaPondSettings s, PondShore shore, PlateField plates,
                               VentShape vent, ref Rng rng)
        {
            if (s.slabCount <= 0) return;

            float ridgeAngle = rng.Value() * Mathf.PI * 2f;
            float ridgeOffset = rng.Signed(0.35f);
            var along = new Vector3(Mathf.Cos(ridgeAngle), 0f, Mathf.Sin(ridgeAngle));
            var across = new Vector3(-along.z, 0f, along.x);
            int ridgeCount = Mathf.Max(1, Mathf.FloorToInt(s.slabCount * 0.6f));

            for (int i = 0; i < s.slabCount; i++)
            {
                Vector3 at;
                if (i < ridgeCount)
                {
                    float t = (i + 0.5f) / ridgeCount * 1.7f - 0.85f;
                    // Let the ridge wander instead of running dead straight.
                    float drift = ridgeOffset + PondNoise.Signed(t * 2.6f, 0.5f, s.seed + 5501) * 0.22f;
                    Vector3 p = (along * t + across * drift) * shore.MeanRadius;
                    p.x += rng.Signed(shore.MeanRadius * 0.05f);
                    p.z += rng.Signed(shore.MeanRadius * 0.05f);
                    at = ClampToCrust(p, shore, s.seed, 0.88f);
                }
                else
                {
                    float angle = rng.Value() * Mathf.PI * 2f;
                    at = shore.PointOnCrust(angle, Mathf.Sqrt(rng.Value()) * 0.9f, s.seed);
                }
                if (AtVent(vent, at, SlabReach(s))) continue;

                // A slab out in an open pool is floating on the lava, so it rides lower.
                int plate = plates.Nearest(at.x, at.z);
                float y = plates.IsOpen(plate) ? MoltenY(at.x, at.z, s) + 0.03f : at.y;
                AddSlab(buf, s, new Vector3(at.x, y, at.z), ref rng);
            }
        }

        /// <summary>Pulls a point back inside the shoreline if it strayed past it.</summary>
        static Vector3 ClampToCrust(Vector3 p, PondShore shore, int seed, float maxFraction)
        {
            float angle = Mathf.Atan2(p.z, p.x);
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float limit = shore.Radius(angle) * maxFraction;
            if (r > limit && r > 0.0001f)
            {
                float k = limit / r;
                p = new Vector3(p.x * k, p.y, p.z * k);
            }
            return new Vector3(p.x, CrustHeight(p.x, p.z, seed), p.z);
        }

        static void AddSlab(MeshBuffer buf, LavaPondSettings s, Vector3 at, ref Rng rng)
        {
            int n = rng.Range(4, 7);
            float size = Mathf.Max(0.05f, s.slabSize * rng.Range(0.65f, 1.5f));
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

            // Buckle the slab over on a random horizontal hinge.
            float tiltAxis = rng.Value() * Mathf.PI * 2f;
            var axis = new Vector3(Mathf.Cos(tiltAxis), 0f, Mathf.Sin(tiltAxis));
            float tilt = rng.Range(28f, 74f) * Mathf.Deg2Rad;
            Rotate(top, axis, tilt);
            Rotate(bottom, axis, tilt);

            // Drop it so the highest corner clears the surface by the requested amount and the rest
            // is buried, which is what sells "pushed up out of the sheet".
            float highest = float.MinValue;
            for (int j = 0; j < n; j++) if (top[j].y > highest) highest = top[j].y;
            float target = s.slabHeight * rng.Range(0.55f, 1.45f);
            var offset = new Vector3(at.x, at.y + target - highest, at.z);
            Translate(top, offset);
            Translate(bottom, offset);

            // A steeply tipped slab can dangle a long way under the surface. None of that is
            // visible, so flatten it against the floor rather than letting it pierce the underside.
            if (s.depth > 0f)
            {
                float floorLimit = -s.depth + 0.05f;
                ClampAbove(top, floorLimit);
                ClampAbove(bottom, floorLimit);
            }

            // Cooled on the face that has been in the air, still glowing on the side that was in
            // the lava until the moment it tipped.
            float shade = rng.Range(0.9f, 1.1f);
            buf.AddFan(Centroid(top), top, true, LavaSlot.CrustDark, shade);
            buf.AddFan(Centroid(bottom), bottom, false, LavaSlot.CrustWarm, shade * 0.85f);
            for (int j = 0; j < n; j++)
            {
                int j1 = (j + 1) % n;
                buf.AddQuad(top[j], top[j1], bottom[j], bottom[j1], LavaSlot.CrustWarm, shade * 0.9f);
            }
        }

        // ------------------------------------------------------------------ bubbles

        /// <summary>
        /// Domes swelling up out of the lava. Only ever placed where a plate failed to form, so a
        /// bubble never appears to be pushing up through solid crust.
        /// </summary>
        static void BuildBubbles(MeshBuffer buf, LavaPondSettings s, PondShore shore, PlateField plates,
                                 VentShape vent, ref Rng rng)
        {
            for (int i = 0; i < s.bubbleCount; i++)
            {
                float angle = rng.Value() * Mathf.PI * 2f;
                float frac = Mathf.Sqrt(rng.Value()) * 0.85f;
                Vector3 at = shore.PointOnCrust(angle, frac, s.seed);
                if (AtVent(vent, at, s.bubbleSize * 1.5f)) continue;
                if (!plates.IsOpen(plates.Nearest(at.x, at.z))) continue;

                AddBubble(buf, s, new Vector3(at.x, MoltenY(at.x, at.z, s), at.z), ref rng);
            }
        }

        static void AddBubble(MeshBuffer buf, LavaPondSettings s, Vector3 at, ref Rng rng)
        {
            int n = rng.Range(6, 10);
            float size = Mathf.Max(0.05f, s.bubbleSize * rng.Range(0.5f, 1.4f));
            float height = size * rng.Range(0.35f, 0.8f);

            var ring = new Vector3[n];
            float a0 = rng.Value() * Mathf.PI * 2f;
            for (int j = 0; j < n; j++)
            {
                float a = a0 + j * Mathf.PI * 2f / n + rng.Signed(0.2f);
                float r = size * rng.Range(0.7f, 1f);
                // Set the skirt just under the lava so the dome never shows a floating edge.
                ring[j] = new Vector3(at.x + Mathf.Cos(a) * r, at.y - 0.02f, at.z + Mathf.Sin(a) * r);
            }

            var crown = new Vector3(at.x, at.y + height, at.z);
            buf.AddFan(crown, ring, true, LavaSlot.Molten, rng.Range(1f, 1.15f));
        }

        // ------------------------------------------------------------------ rocks

        static void BuildRocks(MeshBuffer buf, LavaPondSettings s, PondShore shore,
                               PondInletField inlets, VentShape vent, ref Rng rng)
        {
            for (int i = 0; i < s.rockCount; i++)
            {
                float angle = rng.Value() * Mathf.PI * 2f;
                Vector3 at;
                if (rng.Chance(s.rockOnCrustRatio))
                {
                    at = shore.PointOnCrust(angle, Mathf.Sqrt(rng.Value()) * 0.85f, s.seed);
                }
                else
                {
                    // Sit it on the rim, biased toward the crest.
                    float w = rng.Range(0.22f, 0.85f);
                    float r = shore.Radius(angle) + s.rimWidth * w;
                    float h = s.rimHeight * Mathf.Sin(Mathf.PI * Mathf.Pow(w, 0.7f));
                    at = new Vector3(Mathf.Cos(angle) * r, h, Mathf.Sin(angle) * r);
                }
                if (AtVent(vent, at, s.rockSize * 2.1f)) continue;

                // Nothing stands in the mouth of a river of lava, and a boulder left there sits in
                // the notch looking like the bank was never opened at all.
                //
                // Shaped and thrown away rather than skipped: it takes its draws from the rng
                // either way, so the boulders further round the shore stay exactly where they are.
                // The vent case above keeps its old early-out so that ponds without a river build
                // byte for byte what they did before.
                bool inMouth = inlets.Any &&
                               (inlets.Openness(angle, shore) > 0.35f || inlets.Melt(at.x, at.z) > 0.35f);

                AddRock(buf, s, at, inMouth, ref rng);
            }
        }

        /// <summary>
        /// One boulder. <paramref name="discard"/> shapes it and then drops it rather than
        /// returning early, so it costs the rng exactly what it would have cost — see the call site.
        /// </summary>
        static void AddRock(MeshBuffer buf, LavaPondSettings s, Vector3 at, bool discard, ref Rng rng)
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
            if (discard) return;

            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int j1 = (j + 1) % lon;
                    buf.AddQuad(grid[i][j], grid[i][j1], grid[i + 1][j], grid[i + 1][j1],
                                LavaSlot.Rock, shade, ((i + j) & 1) == 0);
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
