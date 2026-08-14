using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace LavaFlow
{
    /// <summary>
    /// Reads control points off a Spline Container. Kept in its own file because it is the only
    /// thing in the package that depends on com.unity.splines: delete this file and the one line
    /// that calls it and the rest still builds.
    /// </summary>
    public static class LavaFlowSplineSource
    {
        /// <summary>World-space points along the spline, evenly spaced in spline parameter.</summary>
        public static List<Vector3> Sample(SplineContainer container, int samples)
        {
            var pts = new List<Vector3>();
            if (container == null || container.Spline == null || container.Spline.Count < 2) return pts;

            samples = Mathf.Clamp(samples, 2, 4096);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                Unity.Mathematics.float3 p = container.EvaluatePosition(t);
                pts.Add(new Vector3(p.x, p.y, p.z));
            }
            return pts;
        }
    }
}
