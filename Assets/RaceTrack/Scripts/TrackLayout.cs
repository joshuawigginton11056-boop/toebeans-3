using System.Collections.Generic;
using UnityEngine;

namespace RaceTrack
{
    /// <summary>
    /// Operations on the node list itself — corner radii, and opening out corners too tight to use.
    ///
    /// Kept apart from <see cref="RaceTrackGenerator"/> and free of scene objects on purpose. The
    /// corner solver is the one piece here that iterates towards an answer rather than computing one,
    /// which makes it the piece most able to go quietly wrong, and keeping it as plain managed maths
    /// is what lets the headless harness press it a hundred times on a hundred layouts and check it
    /// never returns something worse than it was given.
    /// </summary>
    public static class TrackLayout
    {
        /// <summary>
        /// Radius of the circle through a node and its two neighbours, in metres. Infinity on a
        /// straight, and on the two end nodes of a track that is not a loop.
        /// </summary>
        public static float TurnRadiusAt(IList<TrackNode> nodes, int index, bool closed)
        {
            int n = nodes.Count;
            if (n < 3) return float.PositiveInfinity;
            if (!closed && (index <= 0 || index >= n - 1)) return float.PositiveInfinity;

            Vector3 a = nodes[TrackPath.Prev(index, n, closed)].position;
            Vector3 b = nodes[index].position;
            Vector3 c = nodes[TrackPath.Next(index, n, closed)].position;

            float ab = Vector3.Distance(a, b);
            float bc = Vector3.Distance(b, c);
            float ca = Vector3.Distance(c, a);

            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            if (area < 1e-5f) return float.PositiveInfinity; // collinear

            return (ab * bc * ca) / (4f * area);
        }

        /// <summary>Tightest corner on the node polygon, in metres. Infinity when nothing bends.</summary>
        public static float TightestRadius(IList<TrackNode> nodes, bool closed)
        {
            float worst = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
                worst = Mathf.Min(worst, TurnRadiusAt(nodes, i, closed));
            return worst;
        }

        /// <summary>Corners tighter than <paramref name="target"/>, as (node index, radius).</summary>
        public static List<KeyValuePair<int, float>> FindTightCorners(IList<TrackNode> nodes, bool closed,
                                                                     float target)
        {
            var tight = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                float radius = TurnRadiusAt(nodes, i, closed);
                if (float.IsInfinity(radius)) continue;
                if (radius < target) tight.Add(new KeyValuePair<int, float>(i, radius));
            }
            return tight;
        }

        /// <summary>
        /// Opens out corners tighter than <paramref name="target"/> metres, leaving everything already
        /// within tolerance alone. Straightening a node towards the line between its neighbours is the
        /// direct lever on turn radius.
        ///
        /// Two rules keep it from making the circuit worse, both learned the hard way on this
        /// project's cave generator, where the naive version diverged over repeated presses
        /// (0.78, 1.43, 0.51, 0.41) while reporting success:
        ///
        /// The node moves only <em>across</em> the chord between its neighbours, never along it. Turn
        /// radius scales with node spacing while the track width does not, so the along-chord half of
        /// a move towards the midpoint shortens the run and tightens the very radius it was called on
        /// to open.
        ///
        /// And the best arrangement seen is kept, not the last one reached. Easing one corner
        /// necessarily sharpens its neighbours until they are eased in their turn, so the tightest
        /// radius dips before it climbs; a pass-by-pass accept/reject rule refuses the first move and
        /// does nothing at all. Recording the best and restoring it at the end works through that dip
        /// and can never return worse than the input, which is what makes this safe to press
        /// repeatedly.
        ///
        /// On an open track the two end nodes never move; they are usually joined to something.
        /// Returns the number of nodes that actually ended up somewhere new.
        /// </summary>
        public static int RelaxTightCorners(IList<TrackNode> nodes, bool closed, float target,
                                            int iterations = 60, float strength = 0.25f)
        {
            int n = nodes.Count;
            if (n < 3) return 0;

            var current = new Vector3[n];
            var original = new Vector3[n];
            var best = new Vector3[n];

            for (int i = 0; i < n; i++) original[i] = best[i] = nodes[i].position;
            float bestRadius = TightestRadius(nodes, closed);

            for (int pass = 0; pass < iterations; pass++)
            {
                List<KeyValuePair<int, float>> tight = FindTightCorners(nodes, closed, target);
                if (tight.Count == 0) break;

                for (int i = 0; i < n; i++) current[i] = nodes[i].position;

                foreach (KeyValuePair<int, float> t in tight)
                {
                    int i = t.Key;
                    if (!closed && (i == 0 || i == n - 1)) continue;

                    Vector3 a = current[TrackPath.Prev(i, n, closed)];
                    Vector3 b = current[TrackPath.Next(i, n, closed)];

                    Vector3 chord = b - a;
                    float span = chord.magnitude;
                    if (span < 1e-5f) continue;
                    chord /= span;

                    Vector3 toMiddle = (a + b) * 0.5f - current[i];
                    Vector3 across = toMiddle - Vector3.Dot(toMiddle, chord) * chord;

                    // How far short of the target the corner is decides how hard it is pulled, so a
                    // corner that only just fails barely moves.
                    float deficit = Mathf.Clamp01(1f - t.Value / Mathf.Max(0.01f, target));
                    nodes[i].position = current[i] + across * (strength * deficit);
                }

                float radius = TightestRadius(nodes, closed);
                if (radius > bestRadius)
                {
                    bestRadius = radius;
                    for (int i = 0; i < n; i++) best[i] = nodes[i].position;
                }
            }

            int moved = 0;
            for (int i = 0; i < n; i++)
            {
                nodes[i].position = best[i];
                // Counted against where the nodes started, not against the last pass tried, so a
                // search that wandered and came back reports honestly as having changed nothing.
                if (best[i] != original[i]) moved++;
            }
            return moved;
        }
    }
}
