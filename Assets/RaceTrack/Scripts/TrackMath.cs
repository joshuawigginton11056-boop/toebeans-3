using UnityEngine;

namespace RaceTrack
{
    /// <summary>
    /// The handful of rotations the track needs, hand-rolled.
    ///
    /// Deliberately not <c>Quaternion.AngleAxis</c> or <c>Vector3.Slerp</c>: those are native calls
    /// and throw outside the player, which would put the whole path solver out of reach of the
    /// headless test harness the rest of this project's generators are checked with. Rodrigues costs
    /// three lines and keeps everything from node list to triangle list as pure managed maths.
    /// </summary>
    public static class TrackMath
    {
        /// <summary>
        /// Rotates <paramref name="v"/> about a unit <paramref name="axis"/> by
        /// <paramref name="degrees"/>.
        ///
        /// Sign convention, and it matters everywhere banking is concerned: with the axis pointing
        /// along the direction of travel, a positive angle carries the right-hand vector up. So a
        /// positive bank raises the right-hand edge of the track.
        /// </summary>
        public static Vector3 Rotate(Vector3 v, Vector3 axis, float degrees)
        {
            if (Mathf.Abs(degrees) < 1e-6f) return v;

            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        /// <summary>
        /// Angle from <paramref name="from"/> to <paramref name="to"/> measured about
        /// <paramref name="axis"/>, in degrees, in (-180, 180]. Rotating <paramref name="from"/> by
        /// this angle with <see cref="Rotate"/> lands on <paramref name="to"/>.
        /// </summary>
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            float y = Vector3.Dot(Vector3.Cross(from, to), axis);
            float x = Vector3.Dot(from, to);
            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Carries <paramref name="v"/> from one tangent to the next by the smallest rotation that
        /// takes <paramref name="fromAxis"/> onto <paramref name="toAxis"/> — parallel transport.
        /// This is what keeps a swept frame from spinning as the path climbs and turns, so any twist
        /// in the finished track is twist somebody asked for.
        /// </summary>
        public static Vector3 Transport(Vector3 v, Vector3 fromAxis, Vector3 toAxis)
        {
            Vector3 cross = Vector3.Cross(fromAxis, toAxis);
            float sin = cross.magnitude;
            if (sin < 1e-7f) return v; // same direction, or exactly reversed: nothing sensible to do

            float cos = Mathf.Clamp(Vector3.Dot(fromAxis, toAxis), -1f, 1f);
            return Rotate(v, cross / sin, Mathf.Atan2(sin, cos) * Mathf.Rad2Deg);
        }

        /// <summary>Re-squares <paramref name="v"/> against <paramref name="axis"/> and normalises it.</summary>
        public static Vector3 OrthoNormal(Vector3 v, Vector3 axis)
        {
            Vector3 o = v - Vector3.Dot(v, axis) * axis;
            float len = o.magnitude;
            return len > 1e-6f ? o / len : Vector3.zero;
        }
    }
}
