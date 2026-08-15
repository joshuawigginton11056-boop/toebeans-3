using System.Collections.Generic;
using UnityEngine;

namespace CaveTunnel
{
    /// <summary>
    /// Sweeps a tunnel cross-section along a Catmull-Rom curve through the nodes.
    ///
    /// Two decisions here are worth knowing about before you tune anything:
    ///
    /// The frame is built from world up rather than by parallel transport. Parallel transport is the
    /// textbook answer because it never twists, but it also lets the floor roll over as the path
    /// climbs and turns, and a rolled floor is useless to drive on. Keeping "up" pinned to world up
    /// means the floor stays level through every corner, and the per-node roll is there for when you
    /// actually want camber. Near-vertical sections carry the previous frame forward instead, since
    /// world up has nothing to say about them.
    ///
    /// Roughness is sampled from 3D noise at each vertex's own position, not from an angle around
    /// the ring. That costs nothing extra and means the lumps join up with themselves where the ring
    /// closes and flow continuously along the path, with no seam to hide.
    /// </summary>
    public static class CaveMeshBuilder
    {
        const int SubmeshCount = 2; // Rock, Floor

        /// <summary>How far above the floor a point must be before roughness reaches full strength.</summary>
        const float WallBlendHeight = 0.3f;

        struct Sample
        {
            public Vector3 Position;
            public float Width;
            public float Height;
            public float Roll;
            public float FloorFlatten;
            public float Roughness;
            public float Distance;
        }

        /// <summary>
        /// One point on the cross-section, in section space: x runs across the passage, y runs up
        /// from the floor. <paramref name="theta"/> sweeps the closed loop, 0 at the right wall and
        /// pi/2 at the apex; the lower half is the floor.
        /// </summary>
        public static Vector2 Section(float theta, float halfWidth, float height, float floorFlatten)
        {
            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            float y = s >= 0f
                ? height * s                                   // arch over the top
                : height * 0.5f * (1f - floorFlatten) * s;     // trough below, flat at floorFlatten 1
            return new Vector2(halfWidth * c, y);
        }

        /// <summary>Outward normal of the section, found from the local tangent so it stays correct
        /// on the flat part of the floor as well as around the arch.</summary>
        public static Vector2 SectionNormal(float theta, float halfWidth, float height, float floorFlatten)
        {
            const float d = 0.01f;
            Vector2 tangent = Section(theta + d, halfWidth, height, floorFlatten)
                            - Section(theta - d, halfWidth, height, floorFlatten);
            if (tangent.sqrMagnitude < 1e-12f) return Vector2.up;
            tangent.Normalize();
            return new Vector2(tangent.y, -tangent.x);
        }

        /// <summary>Builds the cave. Returns an empty buffer when there is nothing to sweep.</summary>
        public static CaveMeshBuffer Build(IList<CaveNode> nodes, CaveSettings settings)
        {
            var buf = new CaveMeshBuffer(SubmeshCount);
            if (nodes == null || nodes.Count < 2 || settings == null) return buf;

            List<Sample> samples = SamplePath(nodes, settings);
            if (samples.Count < 2) return buf;

            buf.Length = samples[samples.Count - 1].Distance;
            for (int i = 0; i < samples.Count; i++) buf.Centerline.Add(samples[i].Position);

            int segs = Mathf.Max(6, settings.radialSegments);
            int ringCount = samples.Count;

            var points = new Vector3[ringCount][];
            var wallWeights = new float[ringCount][];
            var ringUV = new float[ringCount][];
            var axes = new Vector3[ringCount];
            var ups = new Vector3[ringCount];
            var rights = new Vector3[ringCount];

            BuildFrames(samples, axes, ups, rights);

            for (int i = 0; i < ringCount; i++)
            {
                points[i] = new Vector3[segs];
                wallWeights[i] = new float[segs];
                ringUV[i] = new float[segs + 1];
                BuildRing(samples[i], buf.Length, settings, ups[i], rights[i],
                          points[i], wallWeights[i], ringUV[i]);
            }

            buf.Volume = BuildVolume(samples, axes, ups, rights);

            StitchRings(buf, settings, points, wallWeights, ringUV, samples);

            if (settings.mouthRim > 0.001f)
            {
                AddMouthRim(buf, settings, points[0], wallWeights[0], ringUV[0],
                            ups[0], rights[0], samples[0], true);
                int last = ringCount - 1;
                AddMouthRim(buf, settings, points[last], wallWeights[last], ringUV[last],
                            ups[last], rights[last], samples[last], false);
            }

            return buf;
        }

        /// <summary>
        /// Snapshots the swept path as a solid-body query. Built from the same samples and frames
        /// the mesh uses, so the volume can never disagree with the geometry it describes.
        /// </summary>
        static CaveVolume BuildVolume(List<Sample> samples, Vector3[] axes, Vector3[] ups, Vector3[] rights)
        {
            int n = samples.Count;
            var positions = new Vector3[n];
            var widths = new float[n];
            var heights = new float[n];
            var flattens = new float[n];

            for (int i = 0; i < n; i++)
            {
                positions[i] = samples[i].Position;
                widths[i] = samples[i].Width;
                heights[i] = samples[i].Height;
                flattens[i] = samples[i].FloorFlatten;
            }

            return new CaveVolume(positions, axes, ups, rights, widths, heights, flattens);
        }

        /// <summary>
        /// Rebuilds the node list as <paramref name="count"/> points spread evenly by distance along
        /// the current curve. The shape is preserved — this walks the curve the generator actually
        /// builds and resamples it, so the cave comes back the same, just with its control points
        /// tidied. Width, height, roll and the rest come along interpolated.
        ///
        /// The two end nodes are pinned exactly. They are the mouths, and a mouth that drifts is a
        /// mouth whose terrain holes no longer line up with it.
        /// </summary>
        public static List<CaveNode> Redistribute(IList<CaveNode> nodes, CaveSettings settings, int count)
        {
            var result = new List<CaveNode>();
            if (nodes == null || nodes.Count < 2) return result;

            count = Mathf.Max(2, count);

            List<Sample> samples = SamplePath(nodes, settings);
            if (samples.Count < 2) return result;

            float total = samples[samples.Count - 1].Distance;
            if (total < 1e-4f) return result;

            int cursor = 0;
            for (int i = 0; i < count; i++)
            {
                float target = total * i / (count - 1);

                while (cursor < samples.Count - 2 && samples[cursor + 1].Distance < target) cursor++;

                Sample a = samples[cursor];
                Sample b = samples[Mathf.Min(cursor + 1, samples.Count - 1)];
                float span = b.Distance - a.Distance;
                float f = span > 1e-6f ? Mathf.Clamp01((target - a.Distance) / span) : 0f;

                result.Add(new CaveNode
                {
                    position = Vector3.Lerp(a.Position, b.Position, f),
                    width = Mathf.Lerp(a.Width, b.Width, f),
                    height = Mathf.Lerp(a.Height, b.Height, f),
                    roll = Mathf.Lerp(a.Roll, b.Roll, f),
                    floorFlatten = Mathf.Lerp(a.FloorFlatten, b.FloorFlatten, f),
                    roughness = Mathf.Lerp(a.Roughness, b.Roughness, f)
                });
            }

            // Resampling lands close to the originals but not exactly on them; the mouths have to be
            // exact, so they are copied over rather than approximated.
            result[0] = nodes[0].Clone();
            result[result.Count - 1] = nodes[nodes.Count - 1].Clone();

            return result;
        }

        // ---------------------------------------------------------------- path

        static List<Sample> SamplePath(IList<CaveNode> nodes, CaveSettings settings)
        {
            var samples = new List<Sample>();
            int n = nodes.Count;
            float spacing = Mathf.Max(0.05f, settings.ringSpacing);

            for (int i = 0; i < n - 1; i++)
            {
                CaveNode a = nodes[Mathf.Max(i - 1, 0)];
                CaveNode b = nodes[i];
                CaveNode c = nodes[i + 1];
                CaveNode d = nodes[Mathf.Min(i + 2, n - 1)];

                // Mirror the missing control point at each end so the curve does not kink there.
                Vector3 p0 = i == 0 ? b.position + (b.position - c.position) : a.position;
                Vector3 p3 = i == n - 2 ? c.position + (c.position - b.position) : d.position;

                Knots knots = Knots.For(p0, b.position, c.position, p3, settings.curveAlpha);

                float length, turnDegrees;
                MeasureSegment(p0, b.position, c.position, p3, knots, out length, out turnDegrees);

                // Ring density comes from whichever is more demanding: covering the distance, or
                // resolving the bend. Spacing alone leaves a corner faceted into a few long quads,
                // and those are what visibly kink when the path turns hard.
                int byLength = Mathf.CeilToInt(length / spacing);
                int byTurn = Mathf.CeilToInt(turnDegrees / Mathf.Max(1f, settings.degreesPerRing));
                int steps = Mathf.Clamp(Mathf.Max(byLength, byTurn), 1, 512);

                for (int k = 0; k < steps; k++)
                {
                    // The last sample of a segment is the first of the next one, so stop short of
                    // t = 1 and let the following segment emit it. The very end is added below.
                    float t = (float)k / steps;
                    samples.Add(Interpolate(a, b, c, d, p0, p3, knots, t));
                }
            }

            // The loop above stops one short of each segment end to avoid duplicate rings, so the
            // very last node still needs its own sample. It is a node, so no interpolation needed.
            CaveNode tail = nodes[n - 1];
            samples.Add(new Sample
            {
                Position = tail.position,
                Width = Mathf.Max(0.05f, tail.width),
                Height = Mathf.Max(0.05f, tail.height),
                Roll = tail.roll,
                FloorFlatten = Mathf.Clamp01(tail.floorFlatten),
                Roughness = Mathf.Max(0f, tail.roughness)
            });

            // Cumulative arc length, used for UVs and for the mouth roughness fade.
            float dist = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0) dist += Vector3.Distance(samples[i].Position, samples[i - 1].Position);
                Sample s = samples[i];
                s.Distance = dist;
                samples[i] = s;
            }

            return samples;
        }

        static Sample Interpolate(CaveNode a, CaveNode b, CaveNode c, CaveNode d,
                                  Vector3 p0, Vector3 p3, Knots k, float t)
        {
            return new Sample
            {
                Position = Spline(p0, b.position, c.position, p3, k, t),
                // The spline still overshoots on scalars where neighbouring nodes differ sharply,
                // so the dimensions are clamped rather than trusted.
                Width = Mathf.Max(0.05f, Spline(a.width, b.width, c.width, d.width, k, t)),
                Height = Mathf.Max(0.05f, Spline(a.height, b.height, c.height, d.height, k, t)),
                Roll = Spline(a.roll, b.roll, c.roll, d.roll, k, t),
                FloorFlatten = Mathf.Clamp01(
                    Spline(a.floorFlatten, b.floorFlatten, c.floorFlatten, d.floorFlatten, k, t)),
                Roughness = Mathf.Max(0f,
                    Spline(a.roughness, b.roughness, c.roughness, d.roughness, k, t))
            };
        }

        /// <summary>
        /// Walks the span once to get both its length and how far it turns, so ring density can be
        /// driven by whichever matters more. Measured on the actual curve rather than the control
        /// polygon, because the two disagree most precisely where the curve is bending hardest.
        /// </summary>
        static void MeasureSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Knots k,
                                   out float length, out float turnDegrees)
        {
            const int probes = 12;

            length = 0f;
            turnDegrees = 0f;

            Vector3 previous = p1;
            Vector3 lastDirection = Vector3.zero;
            bool hasDirection = false;

            for (int i = 1; i <= probes; i++)
            {
                Vector3 current = Spline(p0, p1, p2, p3, k, (float)i / probes);
                Vector3 step = current - previous;
                float d = step.magnitude;
                length += d;

                if (d > 1e-5f)
                {
                    Vector3 direction = step / d;
                    if (hasDirection) turnDegrees += Vector3.Angle(lastDirection, direction);
                    lastDirection = direction;
                    hasDirection = true;
                }

                previous = current;
            }
        }

        /// <summary>
        /// Knot times for one Catmull-Rom span, spaced by chord length raised to
        /// <paramref name="alpha"/>.
        ///
        /// This is what stops the curve tying itself in knots. The uniform form (alpha 0) assumes
        /// the control points are evenly spaced, and when they are not — three nodes a metre apart
        /// followed by a jump of twelve — it overshoots hard enough to loop the path back through
        /// itself before the sweep ever sees it. Centripetal spacing (alpha 0.5) is provably free of
        /// cusps and self-intersections whatever the spacing, which is exactly the guarantee wanted
        /// from something a person is dragging nodes around in.
        /// </summary>
        struct Knots
        {
            public float T0, T1, T2, T3;

            public static Knots For(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float alpha)
            {
                var k = new Knots();
                k.T0 = 0f;
                k.T1 = k.T0 + Step(p0, p1, alpha);
                k.T2 = k.T1 + Step(p1, p2, alpha);
                k.T3 = k.T2 + Step(p2, p3, alpha);
                return k;
            }

            // Coincident nodes would give a zero-length span and divide by zero downstream.
            static float Step(Vector3 a, Vector3 b, float alpha)
            {
                float d = Vector3.Distance(a, b);
                return Mathf.Max(Mathf.Pow(d, alpha), 1e-4f);
            }
        }

        /// <summary>Barry-Goldman evaluation of the span between p1 and p2.</summary>
        static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Knots k, float t)
        {
            float tt = Mathf.Lerp(k.T1, k.T2, t);

            Vector3 a1 = ((k.T1 - tt) * p0 + (tt - k.T0) * p1) / (k.T1 - k.T0);
            Vector3 a2 = ((k.T2 - tt) * p1 + (tt - k.T1) * p2) / (k.T2 - k.T1);
            Vector3 a3 = ((k.T3 - tt) * p2 + (tt - k.T2) * p3) / (k.T3 - k.T2);

            Vector3 b1 = ((k.T2 - tt) * a1 + (tt - k.T0) * a2) / (k.T2 - k.T0);
            Vector3 b2 = ((k.T3 - tt) * a2 + (tt - k.T1) * a3) / (k.T3 - k.T1);

            return ((k.T2 - tt) * b1 + (tt - k.T1) * b2) / (k.T2 - k.T1);
        }

        /// <summary>
        /// The same evaluation for a scalar. Driven by the knots taken from the positions so the
        /// dimensions stay in step with the shape rather than drifting against it.
        /// </summary>
        static float Spline(float p0, float p1, float p2, float p3, Knots k, float t)
        {
            float tt = Mathf.Lerp(k.T1, k.T2, t);

            float a1 = ((k.T1 - tt) * p0 + (tt - k.T0) * p1) / (k.T1 - k.T0);
            float a2 = ((k.T2 - tt) * p1 + (tt - k.T1) * p2) / (k.T2 - k.T1);
            float a3 = ((k.T3 - tt) * p2 + (tt - k.T2) * p3) / (k.T3 - k.T2);

            float b1 = ((k.T2 - tt) * a1 + (tt - k.T0) * a2) / (k.T2 - k.T0);
            float b2 = ((k.T3 - tt) * a2 + (tt - k.T1) * a3) / (k.T3 - k.T1);

            return ((k.T2 - tt) * b1 + (tt - k.T1) * b2) / (k.T2 - k.T1);
        }

        // -------------------------------------------------------------- frames

        static void BuildFrames(List<Sample> samples, Vector3[] axes, Vector3[] ups, Vector3[] rights)
        {
            int n = samples.Count;
            Vector3 carried = Vector3.up;

            for (int i = 0; i < n; i++)
            {
                Vector3 next = samples[Mathf.Min(i + 1, n - 1)].Position;
                Vector3 prev = samples[Mathf.Max(i - 1, 0)].Position;
                Vector3 axis = next - prev;
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.forward;
                axis.Normalize();

                Vector3 up = Vector3.up - Vector3.Dot(Vector3.up, axis) * axis;
                if (up.sqrMagnitude < 1e-4f)
                {
                    // Near vertical: world up says nothing useful, so carry the last frame forward.
                    up = carried - Vector3.Dot(carried, axis) * axis;
                    if (up.sqrMagnitude < 1e-4f) up = Vector3.Cross(axis, Vector3.right);
                }
                up.Normalize();
                carried = up;

                Vector3 right = Vector3.Cross(up, axis).normalized;

                float roll = samples[i].Roll;
                if (Mathf.Abs(roll) > 0.001f)
                {
                    Quaternion q = Quaternion.AngleAxis(roll, axis);
                    up = q * up;
                    right = q * right;
                }

                axes[i] = axis;
                ups[i] = up;
                rights[i] = right;
            }
        }

        // --------------------------------------------------------------- rings

        static void BuildRing(Sample s, float totalLength, CaveSettings settings,
                              Vector3 up, Vector3 right,
                              Vector3[] points, float[] wallWeights, float[] uvAround)
        {
            int segs = points.Length;
            float halfWidth = s.Width;
            float height = s.Height;
            float noiseScale = Mathf.Max(0.05f, settings.roughnessScale);

            // Roughness fades out towards both mouths so each opening is a clean ring.
            float toEnd = Mathf.Min(s.Distance, totalLength - s.Distance);
            float endFade = settings.mouthSmoothing > 0.001f
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toEnd / settings.mouthSmoothing))
                : 1f;

            for (int j = 0; j < segs; j++)
            {
                float theta = Mathf.PI * 2f * j / segs;
                Vector2 sec = Section(theta, halfWidth, height, s.FloorFlatten);

                float wallWeight = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(sec.y / Mathf.Max(0.001f, WallBlendHeight * height)));
                wallWeights[j] = wallWeight;

                Vector3 p = s.Position + right * sec.x + up * sec.y;

                float strength = Mathf.Lerp(settings.floorRoughness, 1f, wallWeight)
                               * s.Roughness * endFade * settings.roughness;
                if (strength > 0.0001f)
                {
                    float noise = CaveNoise.Signed(p.x / noiseScale, p.y / noiseScale, p.z / noiseScale,
                                                   settings.seed);
                    Vector2 n2 = SectionNormal(theta, halfWidth, height, s.FloorFlatten);
                    Vector3 outward = right * n2.x + up * n2.y;
                    p += outward * (noise * strength * halfWidth);
                }

                points[j] = p;
            }

            if (settings.uvMode == CaveUvMode.Proportional)
            {
                // A whole number of tiles per ring, whatever size the ring is. Every ring agrees
                // with its neighbours, so widening a node cannot shear the mapping, and the last
                // step lands exactly on the tile count so the seam meets.
                for (int j = 0; j <= segs; j++)
                    uvAround[j] = (float)j / segs * Mathf.Max(1, settings.uvTilesAround);
            }
            else
            {
                // Real distance around the ring: constant texture size, at the cost of shearing
                // wherever the circumference changes from one ring to the next.
                float scale = Mathf.Max(0.01f, settings.uvScaleAround);
                uvAround[0] = 0f;
                for (int j = 1; j <= segs; j++)
                    uvAround[j] = uvAround[j - 1] + Vector3.Distance(points[j % segs], points[j - 1]) / scale;
            }
        }

        static void StitchRings(CaveMeshBuffer buf, CaveSettings settings,
                                Vector3[][] points, float[][] wallWeights, float[][] ringUV,
                                List<Sample> samples)
        {
            int ringCount = points.Length;
            int segs = points[0].Length;
            float scale = Mathf.Max(0.01f, settings.uvScaleAlong);
            float noiseScale = Mathf.Max(0.05f, settings.roughnessScale);

            for (int i = 0; i < ringCount - 1; i++)
            {
                float v0 = samples[i].Distance / scale;
                float v1 = samples[i + 1].Distance / scale;

                for (int j = 0; j < segs; j++)
                {
                    int k = (j + 1) % segs;

                    Vector3 a = points[i][j];
                    Vector3 b = points[i][k];
                    Vector3 c = points[i + 1][j];
                    Vector3 d = points[i + 1][k];

                    // A quad is floor only when neither of its edges has climbed the wall yet.
                    bool isFloor = wallWeights[i][j] < 0.02f && wallWeights[i][k] < 0.02f
                                && wallWeights[i + 1][j] < 0.02f && wallWeights[i + 1][k] < 0.02f;
                    CaveSlot slot = isFloor ? CaveSlot.Floor : CaveSlot.Rock;

                    buf.AddQuad(a, b, c, d,
                                new Vector2(ringUV[i][j], v0),
                                new Vector2(ringUV[i][j + 1], v0),
                                new Vector2(ringUV[i + 1][j], v1),
                                new Vector2(ringUV[i + 1][j + 1], v1),
                                slot, Shade(a, settings, noiseScale), settings.flipWinding);
                }
            }
        }

        /// <summary>
        /// Per-face brightness scatter. Without this a single-material flat-shaded cave reads as a
        /// grey pipe; with it the facets separate the way the POLY_Mountain rock does.
        /// </summary>
        static float Shade(Vector3 at, CaveSettings settings, float noiseScale)
        {
            if (settings.shadeVariation < 0.001f) return 1f;
            float n = CaveNoise.Signed(at.x / noiseScale, at.y / noiseScale, at.z / noiseScale,
                                       settings.seed + 5171);
            return 1f + n * settings.shadeVariation;
        }

        // --------------------------------------------------------------- mouths

        /// <summary>
        /// A flat lip of rock ringing an opening, so the mouth has visible thickness rather than
        /// showing the paper edge of a one-sided wall. Only the arch is thickened; the floor runs
        /// straight out, because a lip across the entrance is something you would hit.
        /// </summary>
        static void AddMouthRim(CaveMeshBuffer buf, CaveSettings settings,
                                Vector3[] ring, float[] wallWeights, float[] uvAround,
                                Vector3 up, Vector3 right, Sample sample, bool isStart)
        {
            int segs = ring.Length;
            float scale = Mathf.Max(0.01f, settings.uvScaleAlong);
            float noiseScale = Mathf.Max(0.05f, settings.roughnessScale);
            float v0 = sample.Distance / scale;
            float v1 = v0 + settings.mouthRim / scale;

            var outer = new Vector3[segs];
            for (int j = 0; j < segs; j++)
            {
                float theta = Mathf.PI * 2f * j / segs;
                Vector2 n2 = SectionNormal(theta, sample.Width, sample.Height, sample.FloorFlatten);
                Vector3 outward = right * n2.x + up * n2.y;
                outer[j] = ring[j] + outward * (settings.mouthRim * wallWeights[j]);
            }

            for (int j = 0; j < segs; j++)
            {
                int k = (j + 1) % segs;
                Vector3 a = ring[j];
                Vector3 b = ring[k];
                Vector3 c = outer[j];
                Vector3 d = outer[k];

                var uvA = new Vector2(uvAround[j], v0);
                var uvB = new Vector2(uvAround[j + 1], v0);
                var uvC = new Vector2(uvAround[j], v1);
                var uvD = new Vector2(uvAround[j + 1], v1);

                float shade = Shade(a, settings, noiseScale);

                // The lip faces out of the tunnel, so its winding is the opposite of the wall's and
                // flips again between the two ends.
                bool flip = isStart ? !settings.flipWinding : settings.flipWinding;
                buf.AddQuad(a, b, c, d, uvA, uvB, uvC, uvD, CaveSlot.Rock, shade, flip);
            }
        }
    }
}
