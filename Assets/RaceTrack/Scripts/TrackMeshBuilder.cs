using System.Collections.Generic;
using UnityEngine;

namespace RaceTrack
{
    /// <summary>
    /// Sweeps the cross-section from <see cref="TrackProfile"/> along the racing line from
    /// <see cref="TrackPath"/>.
    ///
    /// The two hard parts are both solved elsewhere and deliberately so: the path owns the frames and
    /// the banking, the profile owns the shape and every face direction. What is left here is
    /// bookkeeping — one row of vertices per cross-section, stitched run by run, welded along the
    /// length and left hard between runs.
    ///
    /// Two details in that bookkeeping are load-bearing:
    ///
    /// A closed lap emits one extra row on top of the first. The two rows sit in the same place but
    /// carry different distances, which is the only way the texture can run continuously round the
    /// lap instead of snapping back to zero at the start line. Their normals are then shared, or the
    /// seam shows as a faint crease under a moving light even though the geometry meets exactly.
    ///
    /// The width the finished mesh actually has is measured here, from the emitted vertices, rather
    /// than assumed from the settings. A swept ribbon holds its width by construction and can only
    /// lose it by folding, so a measurement taken off the real geometry is the only one worth
    /// quoting back.
    ///
    /// Pure managed maths throughout — no scene objects, no asset loading, no native Unity calls —
    /// so the whole thing runs in the headless harness.
    /// </summary>
    public static class TrackMeshBuilder
    {
        const int SubmeshCount = 4; // Road, Kerb, Wall, Underside

        public static TrackMeshBuffer Build(IList<TrackNode> nodes, TrackSettings settings)
        {
            if (settings == null) return new TrackMeshBuffer(SubmeshCount);
            return Build(TrackPath.Build(nodes, settings), settings);
        }

        public static TrackMeshBuffer Build(TrackPath path, TrackSettings settings)
        {
            var buf = new TrackMeshBuffer(SubmeshCount);
            if (path == null || settings == null || path.Samples.Count < 2) return buf;

            buf.Length = path.Length;

            int n = path.Samples.Count;
            int rows = path.Closed ? n + 1 : n;
            float sign = settings.flipWinding ? -1f : 1f;

            float tileAlong = TileAlong(Mathf.Max(0.01f, settings.uvMetresPerTile), path, settings);
            float tileKerb = TileAlong(Mathf.Max(0.01f, settings.kerbMetresPerStripe), path, settings);

            var profile = new TrackProfile();
            profile.Build(path.Samples[0].HalfWidth, path.Samples[0].WallScale, settings);
            int pointCount = profile.PointCount;

            var firstRow = new int[pointCount];
            var previous = new int[pointCount];
            var current = new int[pointCount];
            bool havePrevious = false;

            for (int r = 0; r < rows; r++)
            {
                TrackSample s = path.Samples[r % n];

                // The extra row on a closed lap is the start line again, but a whole lap further
                // along as far as the texture is concerned.
                float distance = (path.Closed && r == n) ? path.Length : s.Distance;

                profile.Build(s.HalfWidth, s.WallScale, settings);
                EmitRow(buf, profile, s, distance, tileAlong, tileKerb, current);

                if (r == 0) System.Array.Copy(current, firstRow, pointCount);

                if (havePrevious) Stitch(buf, profile, s, previous, current, sign);

                int[] swap = previous;
                previous = current;
                current = swap;
                havePrevious = true;

                MeasureWidth(buf, profile, previous);
            }

            if (path.Closed)
            {
                // `previous` is the last row emitted, which is the seam row sitting on top of row 0.
                for (int i = 0; i < pointCount; i++) buf.ShareNormals(firstRow[i], previous[i]);
            }
            else
            {
                AddEndCaps(buf, profile, path, settings, sign);
            }

            buf.NormaliseNormals();
            return buf;
        }

        // -------------------------------------------------------------------- rows

        static void EmitRow(TrackMeshBuffer buf, TrackProfile profile, TrackSample s,
                            float distance, float tileAlong, float tileKerb, int[] row)
        {
            for (int run = 0; run < profile.RunCount; run++)
            {
                TrackSlot slot = profile.RunSlot[run];
                float v = distance / (slot == TrackSlot.Kerb ? tileKerb : tileAlong);

                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run];

                for (int i = start; i < end; i++)
                {
                    Vector2 p = profile.Points[i];
                    Vector3 world = s.Position + s.Right * p.x + s.Up * p.y;
                    row[i] = buf.AddVertex(world, new Vector2(profile.U[i], v));
                }
            }
        }

        static void Stitch(TrackMeshBuffer buf, TrackProfile profile, TrackSample s,
                           int[] previous, int[] current, float sign)
        {
            for (int run = 0; run < profile.RunCount; run++)
            {
                TrackSlot slot = profile.RunSlot[run];
                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run] - 1;

                for (int i = start; i < end; i++)
                {
                    Vector2 out2 = profile.OutwardAt(i);
                    Vector3 facing = (s.Right * out2.x + s.Up * out2.y) * sign;

                    buf.AddQuadFacing(previous[i], previous[i + 1], current[i], current[i + 1],
                                      facing, slot);
                }
            }
        }

        /// <summary>
        /// Records the width of the racing surface as built, taken between the two outermost vertices
        /// of the road run. Measured off the emitted geometry on purpose: the settings say what was
        /// asked for, and this says what arrived.
        /// </summary>
        static void MeasureWidth(TrackMeshBuffer buf, TrackProfile profile, int[] row)
        {
            for (int run = 0; run < profile.RunCount; run++)
            {
                if (profile.RunSlot[run] != TrackSlot.Road) continue;

                int start = profile.RunStart[run];
                int end = start + profile.RunLength[run] - 1;
                float width = Vector3.Distance(buf.Vertices[row[start]], buf.Vertices[row[end]]);

                buf.MinRoadWidth = Mathf.Min(buf.MinRoadWidth, width);
                buf.MaxRoadWidth = Mathf.Max(buf.MaxRoadWidth, width);
                return;
            }
        }

        // -------------------------------------------------------------------- caps

        /// <summary>
        /// Closes the two ends of a track that is not a loop. Without these the slab is an open shell
        /// — you can see up inside it, and a mesh collider made from it has nothing to stop a kart
        /// driving in through the end.
        ///
        /// The cross-section is a U, so it cannot be fanned from a centre point: a fan would run
        /// straight through the open air above the road. It decomposes into three flat quads instead
        /// — the slab, and one barrier each side — which is exact and needs no triangulator.
        /// </summary>
        static void AddEndCaps(TrackMeshBuffer buf, TrackProfile profile, TrackPath path,
                               TrackSettings settings, float sign)
        {
            float tile = Mathf.Max(0.01f, settings.uvMetresPerTile);

            TrackSample head = path.Samples[0];
            profile.Build(head.HalfWidth, head.WallScale, settings);
            AddCapQuad(buf, head, profile.DeckCap, -head.Tangent * sign, TrackSlot.Underside, tile);
            if (profile.HasWalls)
            {
                AddCapQuad(buf, head, profile.LeftWallCap, -head.Tangent * sign, TrackSlot.Wall, tile);
                AddCapQuad(buf, head, profile.RightWallCap, -head.Tangent * sign, TrackSlot.Wall, tile);
            }

            TrackSample tail = path.Samples[path.Samples.Count - 1];
            profile.Build(tail.HalfWidth, tail.WallScale, settings);
            AddCapQuad(buf, tail, profile.DeckCap, tail.Tangent * sign, TrackSlot.Underside, tile);
            if (profile.HasWalls)
            {
                AddCapQuad(buf, tail, profile.LeftWallCap, tail.Tangent * sign, TrackSlot.Wall, tile);
                AddCapQuad(buf, tail, profile.RightWallCap, tail.Tangent * sign, TrackSlot.Wall, tile);
            }
        }

        static void AddCapQuad(TrackMeshBuffer buf, TrackSample s, Vector2[] corners,
                               Vector3 facing, TrackSlot slot, float tile)
        {
            var idx = new int[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 world = s.Position + s.Right * corners[i].x + s.Up * corners[i].y;
                idx[i] = buf.AddVertex(world, corners[i] / tile);
            }

            buf.AddTriangleFacing(idx[0], idx[1], idx[2], facing, slot);
            buf.AddTriangleFacing(idx[0], idx[2], idx[3], facing, slot);
        }

        // --------------------------------------------------------------------- uvs

        /// <summary>
        /// The along-track tile size to actually use.
        ///
        /// On a closed lap the asked-for size is nudged so a whole number of tiles fits the lap.
        /// Without that the texture arrives back at the start line a fraction of a tile out of step
        /// and draws a hard line straight across the road — which reads as a hole in the mesh rather
        /// than as a texture that did not quite meet. The nudge is under half a tile over a whole
        /// lap, so nothing looks stretched.
        /// </summary>
        static float TileAlong(float asked, TrackPath path, TrackSettings settings)
        {
            if (!path.Closed || !settings.matchSeamTiling || path.Length < asked) return asked;

            int tiles = Mathf.Max(1, Mathf.RoundToInt(path.Length / asked));
            return path.Length / tiles;
        }
    }
}
