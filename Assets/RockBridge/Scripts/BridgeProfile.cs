using System.Collections.Generic;
using UnityEngine;

namespace RockBridge
{
    /// <summary>
    /// The cross-section of the bridge, in section space: x runs across the deck and y runs up from
    /// the driving surface.
    ///
    /// It is a single closed polygon traced clockwise — across the deck, up and over the right
    /// parapet, down the fascia, along the soffit, and back up over the left parapet — which is what
    /// makes the swept result a solid rather than a sheet. A solid is what a bridge needs: seen from
    /// below or from the side it has a real edge, the mesh collider is closed, and nothing can end
    /// up inside it.
    ///
    /// Traced clockwise, the outward-facing normal of a segment running (dx, dy) is (-dy, dx). That
    /// one fact is where every face direction in the finished mesh comes from; nothing here or in
    /// the builder decides a winding by hand.
    ///
    /// The polygon is split into <em>runs</em> — the deck, one verge, the inside face of a parapet.
    /// Within a run the sweep welds and smooths; between runs it leaves a hard edge. So the deck and
    /// its verges are dead flat and continuous, and the parapet still has crisp corners.
    ///
    /// The topology — how many runs, in what order — depends only on the settings, never on the
    /// per-section numbers. That is deliberate: a cross-section that gained or lost a run partway
    /// along could not be stitched to its neighbour. A section that drops its parapet to nothing
    /// collapses that run to zero height instead, and the degenerate triangles are dropped.
    /// </summary>
    public class BridgeProfile
    {
        /// <summary>Section-space points, in traversal order.</summary>
        public readonly List<Vector2> Points = new List<Vector2>();

        /// <summary>The across-the-section texture coordinate at each point.</summary>
        public readonly List<float> U = new List<float>();

        public readonly List<int> RunStart = new List<int>();
        public readonly List<int> RunLength = new List<int>();
        public readonly List<BridgeSlot> RunSlot = new List<BridgeSlot>();

        /// <summary>False when the settings ask for no parapet at all, which drops six runs.</summary>
        public bool HasParapet;

        /// <summary>Half-width of the outermost point of the section, at deck level.</summary>
        public float OuterHalf;

        public int RunCount { get { return RunStart.Count; } }
        public int PointCount { get { return Points.Count; } }

        /// <summary>
        /// Fills the profile for one cross-section. <paramref name="halfWidth"/> is half the driving
        /// surface; <paramref name="wallScale"/> multiplies the parapet height at this point;
        /// <paramref name="wallRelief"/> is the extra height of this stretch of parapet in metres,
        /// which is what breaks its top line up.
        /// </summary>
        public void Build(float halfWidth, float wallScale, float wallRelief, BridgeSettings s)
        {
            Points.Clear();
            U.Clear();
            RunStart.Clear();
            RunLength.Clear();
            RunSlot.Clear();

            float h = Mathf.Max(0.75f, halfWidth);
            float k = Mathf.Max(0f, s.vergeWidth);
            float d = Mathf.Max(0.05f, s.deckThickness);
            float camber = Mathf.Clamp(s.soffitCamber, 0f, d * 4f);

            HasParapet = s.parapetHeight > 0.01f;
            float wt = HasParapet ? Mathf.Max(0.05f, s.parapetThickness) : 0f;
            float lean = HasParapet ? Mathf.Max(0f, s.parapetLean) : 0f;
            float wh = HasParapet
                ? Mathf.Max(0f, s.parapetHeight * Mathf.Max(0f, wallScale) + wallRelief)
                : 0f;

            float edge = h + k;          // outer edge of the verge: the foot of the parapet
            float outer = edge + wt;     // outside face of the parapet, at deck level
            float tile = Mathf.Max(0.01f, s.uvMetresPerTile);

            OuterHalf = outer;

            // ---- top surface, left to right ------------------------------------------------
            // Both verges read 0 at the deck edge and 1 at the outer edge, so an edging texture
            // comes out mirrored rather than running the same way round both sides.
            BeginRun(BridgeSlot.Verge);
            Add(new Vector2(-edge, 0f), 1f);
            Add(new Vector2(-h, 0f), 0f);

            BeginRun(BridgeSlot.Deck);
            int spans = Mathf.Max(1, s.crossSegments);
            for (int i = 0; i <= spans; i++)
            {
                float x = Mathf.Lerp(-h, h, (float)i / spans);
                Add(new Vector2(x, 0f), (x + h) / tile);
            }

            BeginRun(BridgeSlot.Verge);
            Add(new Vector2(h, 0f), 0f);
            Add(new Vector2(edge, 0f), 1f);

            // ---- parapets and the slab under everything -------------------------------------
            // Parapet U is arc length from the foot of the inner face, so the texture runs up the
            // inside, over the top and down the outside in one piece — and both measure it from
            // their own inner foot, so they mirror.
            float faceLen = Mathf.Sqrt(lean * lean + wh * wh);
            float uInnerTop = faceLen / tile;
            float uOuterTop = (faceLen + wt) / tile;
            float uOuterFoot = (faceLen + wt + faceLen) / tile;

            if (HasParapet)
            {
                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(edge, 0f), 0f);
                Add(new Vector2(edge + lean, wh), uInnerTop);

                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(edge + lean, wh), uInnerTop);
                Add(new Vector2(edge + lean + wt, wh), uOuterTop);

                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(edge + lean + wt, wh), uOuterTop);
                Add(new Vector2(outer, 0f), uOuterFoot);
            }

            // ---- fascia and soffit ----------------------------------------------------------
            // The underside U keeps running across the whole slab so a tiled material meets itself
            // round the corners rather than restarting at every crease.
            BeginRun(BridgeSlot.Rock);
            Add(new Vector2(outer, 0f), 0f);
            Add(new Vector2(outer, -d), d / tile);

            // The soffit dips in the middle rather than running flat, which is what makes the slab
            // read as a beam carved out of rock instead of a poured one. sin() lands on exactly -d
            // at both ends, so the fascia below still meets it square whatever the camber is set to.
            BeginRun(BridgeSlot.Rock);
            float soffitU = d / tile;
            Vector2 previous = new Vector2(outer, -d);
            Add(previous, soffitU);

            int soffitSpans = Mathf.Max(2, s.crossSegments);
            for (int i = 1; i <= soffitSpans; i++)
            {
                float f = (float)i / soffitSpans;
                float x = Mathf.Lerp(outer, -outer, f);
                var p = new Vector2(x, -d - camber * Mathf.Sin(Mathf.PI * f));
                soffitU += Vector2.Distance(previous, p) / tile;
                Add(p, soffitU);
                previous = p;
            }

            BeginRun(BridgeSlot.Rock);
            Add(new Vector2(-outer, -d), soffitU);
            Add(new Vector2(-outer, 0f), soffitU + d / tile);

            if (HasParapet)
            {
                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(-outer, 0f), uOuterFoot);
                Add(new Vector2(-(edge + lean + wt), wh), uOuterTop);

                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(-(edge + lean + wt), wh), uOuterTop);
                Add(new Vector2(-(edge + lean), wh), uInnerTop);

                BeginRun(BridgeSlot.Parapet);
                Add(new Vector2(-(edge + lean), wh), uInnerTop);
                Add(new Vector2(-edge, 0f), 0f);
            }

        }

        /// <summary>
        /// The section as one closed polygon, with the duplicate points where two runs meet dropped.
        ///
        /// The runs deliberately repeat each other's end points so the sweep can leave a hard normal
        /// edge between them, but an end cap needs the boundary itself, once round.
        /// </summary>
        public List<Vector2> Outline()
        {
            var poly = new List<Vector2>(Points.Count);

            for (int i = 0; i < Points.Count; i++)
            {
                if (poly.Count > 0 && (poly[poly.Count - 1] - Points[i]).sqrMagnitude < 1e-8f) continue;
                poly.Add(Points[i]);
            }

            // The last run ends where the first began.
            while (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude < 1e-8f)
                poly.RemoveAt(poly.Count - 1);

            return poly;
        }

        /// <summary>
        /// Ear-clips a simple polygon into triangles, as index triples into
        /// <paramref name="poly"/>.
        ///
        /// This exists so an end cap can be built from the section's <em>own</em> boundary points.
        /// The obvious shortcut — running a strip from the soffit straight up to deck level — puts
        /// its top vertices at the soffit's x positions, which are not the positions the deck run
        /// uses. The two tessellate the same straight line differently, and a T-junction on a shared
        /// edge is the classic hairline crack: geometrically watertight, visibly not, and it only
        /// shows up at certain angles and certain distances.
        ///
        /// Handles either winding, and cannot hang: if a whole pass finds no ear — which collinear
        /// points along the deck line can cause — it clips the sharpest remaining corner and carries
        /// on. Any sliver that produces is dropped downstream for being degenerate.
        /// </summary>
        public static List<int> Triangulate(List<Vector2> poly)
        {
            var tris = new List<int>();
            int n = poly == null ? 0 : poly.Count;
            if (n < 3) return tris;

            // Ear clipping wants a known orientation; work anticlockwise and map back at the end.
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }

            var live = new List<int>(n);
            if (area >= 0f) { for (int i = 0; i < n; i++) live.Add(i); }
            else { for (int i = n - 1; i >= 0; i--) live.Add(i); }

            int guard = n * n + 16;
            while (live.Count > 3 && guard-- > 0)
            {
                int ear = FindEar(poly, live);
                if (ear < 0) ear = SharpestCorner(poly, live);

                int prev = live[(ear - 1 + live.Count) % live.Count];
                int next = live[(ear + 1) % live.Count];

                tris.Add(prev);
                tris.Add(live[ear]);
                tris.Add(next);
                live.RemoveAt(ear);
            }

            if (live.Count == 3)
            {
                tris.Add(live[0]);
                tris.Add(live[1]);
                tris.Add(live[2]);
            }
            return tris;
        }

        static int FindEar(List<Vector2> poly, List<int> live)
        {
            int m = live.Count;

            for (int i = 0; i < m; i++)
            {
                Vector2 a = poly[live[(i - 1 + m) % m]];
                Vector2 b = poly[live[i]];
                Vector2 c = poly[live[(i + 1) % m]];

                if (Cross(a, b, c) <= 1e-9f) continue; // reflex or collinear: not an ear

                bool clear = true;
                for (int j = 0; j < m && clear; j++)
                {
                    if (j == i || j == (i - 1 + m) % m || j == (i + 1) % m) continue;
                    if (InsideTriangle(poly[live[j]], a, b, c)) clear = false;
                }
                if (clear) return i;
            }
            return -1;
        }

        static int SharpestCorner(List<Vector2> poly, List<int> live)
        {
            int m = live.Count;
            int best = 0;
            float bestCross = float.NegativeInfinity;

            for (int i = 0; i < m; i++)
            {
                float c = Cross(poly[live[(i - 1 + m) % m]], poly[live[i]], poly[live[(i + 1) % m]]);
                if (c <= bestCross) continue;
                bestCross = c;
                best = i;
            }
            return best;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f;
        }

        /// <summary>
        /// Outward direction of the segment from point <paramref name="i"/> to point
        /// <paramref name="i"/>+1 within a run, in section space. Clockwise traversal, so it is the
        /// segment direction turned a quarter turn one way — see the class note.
        /// </summary>
        public Vector2 OutwardAt(int i)
        {
            Vector2 dir = Points[i + 1] - Points[i];
            float len = dir.magnitude;
            if (len < 1e-7f) return Vector2.up;
            dir /= len;
            return new Vector2(-dir.y, dir.x);
        }

        void BeginRun(BridgeSlot slot)
        {
            RunStart.Add(Points.Count);
            RunLength.Add(0);
            RunSlot.Add(slot);
        }

        void Add(Vector2 p, float u)
        {
            Points.Add(p);
            U.Add(u);
            RunLength[RunLength.Count - 1] = Points.Count - RunStart[RunStart.Count - 1];
        }
    }
}
