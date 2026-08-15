using System.Collections.Generic;
using UnityEngine;

namespace Volcano
{
    /// <summary>
    /// The volcano as pure maths: one height field over the ground plane, plus the prism cut through
    /// it for the passage. The mesh builder turns this into triangles, the inspector draws gizmos
    /// from it, and gameplay asks it where the lava is; none of them need to agree about anything
    /// else, because there is only one definition of the shape here.
    ///
    /// Local space. The origin is the middle of the foot of the cone and y = 0 is the ground the
    /// mountain is standing on, so the summit is at <c>settings.height</c> and the buried skirt is
    /// the only thing below zero.
    ///
    /// Deliberately free of scene objects, asset loading and Unity's native calls, so it can be
    /// compiled and run outside the Editor and asserted against.
    /// </summary>
    public class VolcanoShape
    {
        const float Tau = 6.28318530718f;

        readonly VolcanoSettings _s;
        readonly float[] _spillwayHeadings;

        // The passage, resolved once in the constructor. Everything downstream reads these.
        readonly Vector3 _boreAxis;
        readonly Vector3 _boreRight;
        readonly Vector3 _boreOrigin;
        readonly Vector2[] _archProfile;   // (across, up), counter-clockwise, closed implicitly
        readonly Vector3[] _cutNormals;    // one outward plane normal per arch edge
        readonly Vector3[] _cutPoints;     // a point on each of those planes, inset by mouthOverlap

        public VolcanoShape(VolcanoSettings settings)
        {
            _s = settings ?? new VolcanoSettings();

            _spillwayHeadings = BuildSpillwayHeadings(_s);

            float yaw = _s.boreYaw * Mathf.Deg2Rad;
            _boreAxis = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            _boreRight = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            _boreOrigin = _boreRight * _s.boreOffset + Vector3.up * _s.boreFloorHeight;

            _archProfile = BuildArchProfile(_s);
            BuildCutPlanes(_archProfile, _boreRight, _boreOrigin, _s.mouthOverlap,
                           out _cutNormals, out _cutPoints);
        }

        public VolcanoSettings Settings { get { return _s; } }

        // ------------------------------------------------------------------ height field

        /// <summary>Surface height at a point on the ground plane, in local space.</summary>
        public float Height(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float th = Mathf.Atan2(z, x);
            return HeightPolar(r, th, x, z);
        }

        public float Height(Vector3 localPoint)
        {
            return Height(localPoint.x, localPoint.z);
        }

        /// <summary>Surface height in polar coordinates. <paramref name="th"/> is in radians.</summary>
        public float HeightPolar(float r, float th)
        {
            return HeightPolar(r, th, r * Mathf.Cos(th), r * Mathf.Sin(th));
        }

        float HeightPolar(float r, float th, float x, float z)
        {
            float turns = th * (1f / Tau);

            // --- the cone itself -------------------------------------------------------------
            //
            // Two independent wobbles: a few big lobes that make the mountain an irregular lump
            // rather than a traffic cone, and a faster one that only shows on the rim crest. Both
            // are folded into the summit height and then scaled down the profile, so the flank
            // stays continuous with the rim instead of stepping at it.
            float coneHeight = _s.height * (1f + _s.coneVariation *
                               VolcanoNoise.RingSigned(turns, Mathf.Max(2, _s.coneVariationLobes), _s.seed + 11));
            float summit = coneHeight + _s.rimRoughness *
                           VolcanoNoise.RingSigned(turns, Mathf.Max(4, _s.gullyCount), _s.seed + 29);

            float rim = _s.rimRadius;
            float foot = Mathf.Max(rim + 1f, _s.baseRadius);

            float h;
            if (r <= rim)
            {
                h = summit;
            }
            else if (r >= foot)
            {
                h = 0f;
            }
            else
            {
                float t = (foot - r) / (foot - rim);
                h = summit * Mathf.Pow(t, _s.coneCurve);
            }

            // --- how much of a spillway channel is here --------------------------------------
            //
            // Worked out before the flank detail, because a channel scours its own bed: gullies and
            // roughness are suppressed inside one. That is not only how it looks, it is what keeps
            // the channel floor descending all the way down. Roughness at the default amplitude is
            // easily steep enough to put a lip across a lava channel near the foot, where the cone
            // itself has flattened off.
            float channel = 0f;
            float channelCut = 0f;
            for (int i = 0; i < _spillwayHeadings.Length; i++)
            {
                float w = SpillwayWeight(i, r, th);
                if (w <= channel) continue;
                channel = w;
                channelCut = w;
            }

            float clean = 1f - channel;

            // --- flank detail ----------------------------------------------------------------
            if (r > rim && r < foot + _s.skirtWidth)
            {
                float detail = VolcanoNoise.SmoothStep(rim, rim + 6f, r) *
                               (1f - VolcanoNoise.SmoothStep(foot - 14f, foot, r));

                if (_s.gullyDepth > 0f)
                {
                    float g = VolcanoNoise.RingRidged(turns, Mathf.Max(3, _s.gullyCount),
                                                      _s.seed + 101, _s.gullySharpness);
                    h -= _s.gullyDepth * g * detail * clean;
                }

                if (_s.roughness > 0f)
                {
                    float inv = 1f / Mathf.Max(1f, _s.roughnessScale);
                    h += _s.roughness * VolcanoNoise.Fbm(x * inv, z * inv, _s.seed + 53) * detail * clean;
                }
            }

            // --- the crater ------------------------------------------------------------------
            float lip = _s.CraterLipRadius;
            float floorRadius = lip * _s.craterFloorFraction;
            if (r < lip)
            {
                float craterFloor = _s.height - _s.craterDepth;
                float bowl;
                if (r <= floorRadius)
                {
                    bowl = craterFloor;
                }
                else
                {
                    float v = (r - floorRadius) / Mathf.Max(1e-3f, lip - floorRadius);
                    bowl = craterFloor + (summit - craterFloor) * VolcanoNoise.SmoothStep01(v);
                }

                // A little relief on the floor so the pool is not sitting on a machined disc.
                if (_s.roughness > 0f && r <= floorRadius)
                {
                    float inv = 1f / Mathf.Max(1f, _s.roughnessScale * 0.35f);
                    bowl += _s.roughness * 0.3f * VolcanoNoise.Fbm(x * inv, z * inv, _s.seed + 71);
                }

                h = Mathf.Min(h, bowl);
            }

            // --- spillways -------------------------------------------------------------------
            if (channelCut > 0f)
            {
                // Through the rim: a hard ceiling rather than a subtraction, so the notch is cut to
                // a level the lava can pour over regardless of how tall the crest happens to be at
                // that bearing. Kept outside the crater floor so it never lowers the floor itself.
                if (r >= floorRadius)
                {
                    float cap = summit - _s.notchDrop * channelCut;
                    h = Mathf.Min(h, cap);
                }

                // Down the flank: an ordinary gully. It fades in as the rim cut runs out and fades
                // out well before the foot, slowly enough that the taper never climbs faster than
                // the cone descends.
                if (r > rim && _s.spillwayChannelDepth > 0f)
                {
                    float fadeInEnd = rim + 10f;
                    float fadeOutStart = Mathf.Max(fadeInEnd + 5f, foot - 30f);
                    float fade = VolcanoNoise.SmoothStep(rim, fadeInEnd, r) *
                                 (1f - VolcanoNoise.SmoothStep(fadeOutStart, foot, r));
                    h -= _s.spillwayChannelDepth * channelCut * fade;
                }
            }

            // --- buried skirt ----------------------------------------------------------------
            if (r >= foot)
            {
                float k = _s.skirtWidth > 1e-3f ? (r - foot) / _s.skirtWidth : 1f;
                h = -_s.skirtSink * VolcanoNoise.SmoothStep01(k);
            }

            return h;
        }

        /// <summary>Local-space point on the surface directly above or below a ground position.</summary>
        public Vector3 SurfacePoint(float x, float z)
        {
            return new Vector3(x, Height(x, z), z);
        }

        /// <summary>
        /// Surface normal, by finite difference. Only used for scattering props and for gizmos;
        /// the mesh gets its normals from the triangles themselves.
        /// </summary>
        public Vector3 Normal(float x, float z, float epsilon = 0.5f)
        {
            float hx = Height(x + epsilon, z) - Height(x - epsilon, z);
            float hz = Height(x, z + epsilon) - Height(x, z - epsilon);
            return new Vector3(-hx, 2f * epsilon, -hz).normalized;
        }

        /// <summary>Outside edge of the buried skirt, past which the shape is flat.</summary>
        public float OuterRadius
        {
            get { return _s.baseRadius + _s.skirtWidth; }
        }

        // ------------------------------------------------------------------ spillways

        public int SpillwayCount { get { return _spillwayHeadings.Length; } }

        /// <summary>Bearing of a spillway, in radians.</summary>
        public float SpillwayHeading(int index)
        {
            if (index < 0 || index >= _spillwayHeadings.Length) return 0f;
            return _spillwayHeadings[index];
        }

        /// <summary>
        /// A point on the floor of a spillway channel, at the given distance from the axis. Walk
        /// this outwards to get a route for a lava flow that is guaranteed to be in the channel
        /// rather than hoping a downhill solve finds it.
        /// </summary>
        public Vector3 SpillwayPoint(int index, float radius)
        {
            float th = SpillwayHeading(index);
            float x = radius * Mathf.Cos(th);
            float z = radius * Mathf.Sin(th);
            return new Vector3(x, Height(x, z), z);
        }

        /// <summary>
        /// The channel route, from inside the lava pool out to the given radius. This is exactly
        /// what a Lava Flow generator wants as its waypoint list.
        /// </summary>
        public List<Vector3> SpillwayRoute(int index, float outerRadius, float spacing)
        {
            var pts = new List<Vector3>();
            float step = Mathf.Max(1f, spacing);

            // Starts just inside the crater lip rather than at the middle of the floor. A flow
            // routed from the floor would have to climb the whole crater wall to reach the notch,
            // and a river running visibly uphill under the pool is not worth the two metres of
            // extra overlap. Here it reads as lava leaving the pool and going over the edge.
            float start = _s.CraterLipRadius * 0.72f;
            float end = Mathf.Max(start + step, outerRadius);

            for (float r = start; r < end; r += step) pts.Add(SpillwayPoint(index, r));
            pts.Add(SpillwayPoint(index, end));
            return pts;
        }

        /// <summary>How much of a spillway channel is at this point, 0 outside it and 1 down the middle.</summary>
        public float SpillwayWeight(int index, float r, float th)
        {
            if (index < 0 || index >= _spillwayHeadings.Length) return 0f;
            if (_s.spillwayWidth <= 0f) return 0f;

            float d = DeltaAngle(th, _spillwayHeadings[index]);

            // Metres across the channel, not degrees. A constant angle would open the notch into a
            // 40 m gash by the time it reached the foot.
            float lateral = Mathf.Abs(d) * Mathf.Max(r, _s.rimRadius);
            float half = HalfWidthAt(r);
            if (half <= 1e-3f) return 0f;

            float a = lateral / half;
            if (a >= 1f) return 0f;

            // Flat-bottomed rather than a V, so the channel has a floor to run lava down.
            return 1f - VolcanoNoise.SmoothStep(0.35f, 1f, a);
        }

        float HalfWidthAt(float r)
        {
            float t = Mathf.Clamp01((r - _s.rimRadius) / Mathf.Max(1f, _s.baseRadius - _s.rimRadius));
            return _s.spillwayWidth * 0.5f * (1f + _s.spillwayWiden * t);
        }

        static float[] BuildSpillwayHeadings(VolcanoSettings s)
        {
            int n = Mathf.Max(0, s.spillwayCount);
            var headings = new float[n];
            if (n == 0) return headings;

            var rng = new Rng(s.seed + 5501);
            float step = Tau / n;
            float baseAngle = s.spillwayAngle * Mathf.Deg2Rad;

            for (int i = 0; i < n; i++)
                headings[i] = baseAngle + i * step + rng.Signed(step * 0.35f * s.spillwayScatter);

            return headings;
        }

        static float DeltaAngle(float a, float b)
        {
            float d = Mathf.Repeat(a - b + Mathf.PI, Tau) - Mathf.PI;
            return d;
        }

        /// <summary>
        /// True when a spillway pours straight onto a passage mouth. Nothing stops it working, but
        /// it means a river of lava landing on the road, so the inspector says so.
        /// </summary>
        public bool SpillwayHitsPassage(int index)
        {
            if (_s.passage == PassageMode.None) return false;

            float th = SpillwayHeading(index);
            float boreTh = Mathf.Atan2(_boreAxis.z, _boreAxis.x);

            // Both mouths, so compare against the axis and its opposite.
            float a = Mathf.Abs(DeltaAngle(th, boreTh));
            float b = Mathf.Abs(DeltaAngle(th, boreTh + Mathf.PI));
            float nearest = Mathf.Min(a, b);

            // Half the mouth as an angle at the foot, plus half the channel, plus a little room.
            float span = Mathf.Atan2(_s.boreWidth * 0.5f + HalfWidthAt(_s.baseRadius),
                                     Mathf.Max(1f, _s.baseRadius));
            return nearest < span;
        }

        // ------------------------------------------------------------------ passage

        public bool HasPassage { get { return _s.passage != PassageMode.None; } }

        /// <summary>Direction the passage runs, in local space. Horizontal and normalised.</summary>
        public Vector3 BoreAxis { get { return _boreAxis; } }

        /// <summary>Across the passage, in local space. Horizontal and normalised.</summary>
        public Vector3 BoreRight { get { return _boreRight; } }

        /// <summary>Middle of the passage floor where it crosses the axis of the cone.</summary>
        public Vector3 BoreOrigin { get { return _boreOrigin; } }

        /// <summary>The arch, as (across, up) corners in the passage's own cross-section.</summary>
        public Vector2[] ArchProfile { get { return _archProfile; } }

        /// <summary>A point on the passage surface, from a distance along it and a corner of the arch.</summary>
        public Vector3 BorePoint(float along, Vector2 section)
        {
            return _boreOrigin + _boreAxis * along + _boreRight * section.x + Vector3.up * section.y;
        }

        /// <summary>
        /// Whether a point is inside the hole cut through the mountain. Note this is the *cut*,
        /// which is inset inside the tunnel by <see cref="VolcanoSettings.mouthOverlap"/> so the
        /// two surfaces overlap at the mouth instead of meeting exactly and risking a crack.
        /// </summary>
        public bool InsideCut(Vector3 local)
        {
            for (int i = 0; i < _cutNormals.Length; i++)
                if (Vector3.Dot(local - _cutPoints[i], _cutNormals[i]) > 0f) return false;
            return true;
        }

        /// <summary>
        /// Signed distance to each cut plane, positive outside. The mesh builder clips against these
        /// directly rather than asking the yes/no question, so it can split triangles on the boundary.
        /// </summary>
        public int CutPlaneCount { get { return _cutNormals.Length; } }

        public float CutPlaneDistance(int plane, Vector3 local)
        {
            return Vector3.Dot(local - _cutPoints[plane], _cutNormals[plane]);
        }

        /// <summary>How deep under the surface a point is. Negative means it is out in the open air.</summary>
        public float DepthBelowSurface(Vector3 local)
        {
            return Height(local.x, local.z) - local.y;
        }

        /// <summary>
        /// How far along the passage the rock starts and stops, measured from
        /// <see cref="BoreOrigin"/>. Returns false when the passage misses the mountain entirely,
        /// which is what an offset larger than the cone gives you.
        /// </summary>
        public bool TryGetBoreSpan(out float from, out float to)
        {
            from = 0f;
            to = 0f;

            float reach = OuterRadius + Mathf.Abs(_s.boreOffset) + 10f;
            float step = 1f;
            bool found = false;

            for (float s = -reach; s <= reach; s += step)
            {
                if (!SectionMeetsRock(s)) continue;
                if (!found) { from = s; found = true; }
                to = s;
            }

            if (!found) return false;

            // The march found the first and last whole metre with rock in it. Close on the real
            // boundary from just outside, so the sweep starts fractionally clear of the rock rather
            // than up to a whole station inside it, which would leave a notch at the mouth.
            from = RefineSpanEdge(from - step, from);
            to = RefineSpanEdge(to + step, to);
            return true;
        }

        /// <summary>Bisects towards the edge of the rock and returns the last point outside it.</summary>
        float RefineSpanEdge(float outside, float inside)
        {
            for (int i = 0; i < 12; i++)
            {
                float mid = (outside + inside) * 0.5f;
                if (SectionMeetsRock(mid)) inside = mid;
                else outside = mid;
            }
            return outside;
        }

        /// <summary>True when any corner of the arch at this station is buried in the mountain.</summary>
        bool SectionMeetsRock(float along)
        {
            for (int i = 0; i < _archProfile.Length; i++)
            {
                Vector3 p = BorePoint(along, _archProfile[i]);
                if (Height(p.x, p.z) > p.y) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a track drives in: the point on the middle of the passage floor where the ground
        /// first rises above it, and the direction pointing out of the mountain from there.
        /// Index 0 is the mouth on the negative side of the axis, 1 the positive.
        /// </summary>
        public bool TryGetPortal(int index, out Vector3 floorCentre, out Vector3 outward)
        {
            floorCentre = _boreOrigin;
            outward = index == 0 ? -_boreAxis : _boreAxis;

            float from, to;
            if (!TryGetBoreSpan(out from, out to)) return false;

            float sign = index == 0 ? -1f : 1f;
            float outer = index == 0 ? from : to;
            float inner = (from + to) * 0.5f;

            // Close on the point where the floor centreline goes under the surface. Anything before
            // that is apron running over open ground.
            for (int i = 0; i < 24; i++)
            {
                float mid = (outer + inner) * 0.5f;
                Vector3 p = BorePoint(mid, new Vector2(0f, 0f));
                if (Height(p.x, p.z) > p.y) inner = mid;
                else outer = mid;
            }

            floorCentre = BorePoint(inner, Vector2.zero);
            outward = _boreAxis * sign;
            return true;
        }

        // ------------------------------------------------------------------ arch and planes

        static Vector2[] BuildArchProfile(VolcanoSettings s)
        {
            float w = Mathf.Max(0.5f, s.boreWidth * 0.5f);
            float crown = Mathf.Max(1f, s.boreHeight);
            float wall = Mathf.Clamp(s.boreWallHeight, 0.2f, crown - 0.5f);
            int segs = Mathf.Max(2, s.boreArchSegments);

            var pts = new List<Vector2>(segs + 4);
            pts.Add(new Vector2(-w, 0f));
            pts.Add(new Vector2(w, 0f));
            pts.Add(new Vector2(w, wall));

            // Over the top, from the right-hand wall to the left. The end points are already in the
            // list, so only the intermediate corners are added.
            for (int k = 1; k < segs; k++)
            {
                float a = Mathf.PI * k / segs;
                pts.Add(new Vector2(w * Mathf.Cos(a), wall + (crown - wall) * Mathf.Sin(a)));
            }

            pts.Add(new Vector2(-w, wall));
            return pts.ToArray();
        }

        /// <summary>
        /// One outward-facing plane per edge of the arch, all of them containing the passage axis.
        /// Their intersection is the prism the mountain is cut by.
        /// </summary>
        static void BuildCutPlanes(Vector2[] profile, Vector3 right, Vector3 origin, float inset,
                                   out Vector3[] normals, out Vector3[] points)
        {
            int n = profile.Length;
            normals = new Vector3[n];
            points = new Vector3[n];

            // The profile is wound counter-clockwise in (across, up), so (dy, -dx) points out.
            for (int i = 0; i < n; i++)
            {
                Vector2 a = profile[i];
                Vector2 b = profile[(i + 1) % n];
                Vector2 e = b - a;

                Vector3 nrm = (right * e.y + Vector3.up * -e.x).normalized;
                Vector3 pt = origin + right * a.x + Vector3.up * a.y;

                // Pull the plane inwards so the hole is slightly smaller than the tunnel that fills
                // it. Two independently solved surfaces meeting exactly is how you get a hairline
                // of daylight round the arch.
                //
                // Except the floor. Insetting that one lifts it, so any mountainside crossing the
                // floor level inside the passage survives as a slab lying on the road: measured at
                // exactly the inset, 0.3 m, which at kart speed is a ramp. Left where it is, the
                // ground is cut away right down to the floor and whatever is under the floor stays
                // under the floor, hidden by the slab.
                float use = nrm.y < -0.7f ? 0f : Mathf.Max(0f, inset);

                normals[i] = nrm;
                points[i] = pt - nrm * use;
            }
        }
    }
}
