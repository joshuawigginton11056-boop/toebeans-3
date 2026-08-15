using System.Collections.Generic;
using UnityEngine;

namespace RaceTrack
{
    /// <summary>
    /// The cross-section of the track, in section space: x runs across the track and y runs up from
    /// the racing surface.
    ///
    /// It is a single closed polygon traced clockwise — across the road, up and over the right
    /// barrier, down the outside, back along the underside, and up over the left barrier — which is
    /// what makes the swept result a solid rather than a sheet. A solid is what a track hanging in
    /// mid-air needs: seen from below or from the side it has a real edge, and the mesh collider is
    /// closed, so nothing can end up inside it.
    ///
    /// Traced clockwise, the outward-facing normal of a segment running (dx, dy) is (-dy, dx). That
    /// one fact is where every face direction in the finished mesh comes from; nothing here or in the
    /// builder decides a winding by hand.
    ///
    /// The polygon is split into <em>runs</em> — the road, one kerb, the inside face of a barrier.
    /// Within a run the sweep welds and smooths; between runs it leaves a hard edge. So the road and
    /// its kerbs are dead flat and continuous, and the barrier still has crisp corners.
    ///
    /// The topology — how many runs, in what order — depends only on the settings, never on the
    /// per-node numbers. That is deliberate: a cross-section that gained or lost a run partway along
    /// the track could not be stitched to its neighbour. A node that drops its barrier to nothing
    /// collapses that run to zero height instead, and the degenerate triangles are dropped.
    /// </summary>
    public class TrackProfile
    {
        /// <summary>Section-space points, in traversal order.</summary>
        public readonly List<Vector2> Points = new List<Vector2>();

        /// <summary>The across-the-section texture coordinate at each point.</summary>
        public readonly List<float> U = new List<float>();

        public readonly List<int> RunStart = new List<int>();
        public readonly List<int> RunLength = new List<int>();
        public readonly List<TrackSlot> RunSlot = new List<TrackSlot>();

        /// <summary>False when the settings ask for no barrier at all, which drops six runs.</summary>
        public bool HasWalls;

        /// <summary>Corners of the slab's end cap, for an open-ended track: top left, top right,
        /// bottom right, bottom left.</summary>
        public readonly Vector2[] DeckCap = new Vector2[4];

        /// <summary>Corners of each barrier's end cap, inner foot first, going up and over.</summary>
        public readonly Vector2[] LeftWallCap = new Vector2[4];
        public readonly Vector2[] RightWallCap = new Vector2[4];

        public int RunCount { get { return RunStart.Count; } }
        public int PointCount { get { return Points.Count; } }

        /// <summary>
        /// Fills the profile for one cross-section. <paramref name="halfWidth"/> is half the racing
        /// surface; <paramref name="wallScale"/> multiplies the barrier height at this point.
        /// </summary>
        public void Build(float halfWidth, float wallScale, TrackSettings s)
        {
            Points.Clear();
            U.Clear();
            RunStart.Clear();
            RunLength.Clear();
            RunSlot.Clear();

            float h = Mathf.Max(0.5f, halfWidth);
            float k = Mathf.Max(0f, s.kerbWidth);
            float d = Mathf.Max(0.02f, s.deckThickness);

            HasWalls = s.wallHeight > 0.01f;
            float wt = HasWalls ? Mathf.Max(0.02f, s.wallThickness) : 0f;
            float lean = HasWalls ? Mathf.Max(0f, s.wallLean) : 0f;
            float wh = HasWalls ? Mathf.Max(0f, s.wallHeight * Mathf.Max(0f, wallScale)) : 0f;

            float edge = h + k;          // outer edge of the kerb: the foot of the barrier
            float outer = edge + wt;     // outside face of the barrier, at road level
            float tile = Mathf.Max(0.01f, s.uvMetresPerTile);

            // ---- top surface, left to right ------------------------------------------------
            // Both kerbs read 0 at the road edge and 1 at the outer edge, so a rumble strip texture
            // comes out mirrored rather than running the same way round the whole lap.
            BeginRun(TrackSlot.Kerb);
            Add(new Vector2(-edge, 0f), 1f);
            Add(new Vector2(-h, 0f), 0f);

            BeginRun(TrackSlot.Road);
            int spans = Mathf.Max(1, s.crossSegments);
            for (int i = 0; i <= spans; i++)
            {
                float x = Mathf.Lerp(-h, h, (float)i / spans);
                float u = s.roadUvMode == RoadUvMode.Metres
                    ? (x + h) / tile
                    : (x + h) / (2f * h) * Mathf.Max(1, s.uvTilesAcross);
                Add(new Vector2(x, 0f), u);
            }

            BeginRun(TrackSlot.Kerb);
            Add(new Vector2(h, 0f), 0f);
            Add(new Vector2(edge, 0f), 1f);

            // ---- barriers and the slab under everything -------------------------------------
            // Wall U is arc length from the foot of the inner face, so the texture runs up the
            // inside, over the top and down the outside in one piece — and both barriers measure it
            // from their own inner foot, so they mirror.
            float faceLen = Mathf.Sqrt(lean * lean + wh * wh);
            float uInnerTop = faceLen / tile;
            float uOuterTop = (faceLen + wt) / tile;
            float uOuterFoot = (faceLen + wt + faceLen) / tile;

            if (HasWalls)
            {
                BeginRun(TrackSlot.Wall);
                Add(new Vector2(edge, 0f), 0f);
                Add(new Vector2(edge + lean, wh), uInnerTop);

                BeginRun(TrackSlot.Wall);
                Add(new Vector2(edge + lean, wh), uInnerTop);
                Add(new Vector2(edge + lean + wt, wh), uOuterTop);

                BeginRun(TrackSlot.Wall);
                Add(new Vector2(edge + lean + wt, wh), uOuterTop);
                Add(new Vector2(outer, 0f), uOuterFoot);
            }

            // Underside U keeps running across the whole slab so a tiled material meets itself round
            // the corners rather than restarting at every crease.
            BeginRun(TrackSlot.Underside);
            Add(new Vector2(outer, 0f), 0f);
            Add(new Vector2(outer, -d), d / tile);

            BeginRun(TrackSlot.Underside);
            Add(new Vector2(outer, -d), d / tile);
            Add(new Vector2(-outer, -d), (d + outer * 2f) / tile);

            BeginRun(TrackSlot.Underside);
            Add(new Vector2(-outer, -d), (d + outer * 2f) / tile);
            Add(new Vector2(-outer, 0f), (d + outer * 2f + d) / tile);

            if (HasWalls)
            {
                BeginRun(TrackSlot.Wall);
                Add(new Vector2(-outer, 0f), uOuterFoot);
                Add(new Vector2(-(edge + lean + wt), wh), uOuterTop);

                BeginRun(TrackSlot.Wall);
                Add(new Vector2(-(edge + lean + wt), wh), uOuterTop);
                Add(new Vector2(-(edge + lean), wh), uInnerTop);

                BeginRun(TrackSlot.Wall);
                Add(new Vector2(-(edge + lean), wh), uInnerTop);
                Add(new Vector2(-edge, 0f), 0f);
            }

            // ---- end caps, for a track that does not close into a loop ----------------------
            DeckCap[0] = new Vector2(-edge, 0f);
            DeckCap[1] = new Vector2(edge, 0f);
            DeckCap[2] = new Vector2(outer, -d);
            DeckCap[3] = new Vector2(-outer, -d);

            RightWallCap[0] = new Vector2(edge, 0f);
            RightWallCap[1] = new Vector2(edge + lean, wh);
            RightWallCap[2] = new Vector2(edge + lean + wt, wh);
            RightWallCap[3] = new Vector2(outer, 0f);

            LeftWallCap[0] = new Vector2(-edge, 0f);
            LeftWallCap[1] = new Vector2(-(edge + lean), wh);
            LeftWallCap[2] = new Vector2(-(edge + lean + wt), wh);
            LeftWallCap[3] = new Vector2(-outer, 0f);
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

        void BeginRun(TrackSlot slot)
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
