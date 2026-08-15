using UnityEngine;

namespace PlayerPath
{
    /// <summary>
    /// Anything that can answer "where is the ground under here, and which way does it face".
    ///
    /// Kept as an interface so the route solver never touches the scene: the generator passes a
    /// terrain or a raycast sampler, a test passes an analytic mountain. That is what lets the whole
    /// solver and mesh builder be run and asserted against outside the Editor.
    /// </summary>
    public interface IPathGround
    {
        /// <summary>Ground point and unit normal under <paramref name="worldPos"/>, in world space.
        /// Returns false when there is nothing there to stand on.</summary>
        bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal);
    }

    /// <summary>A flat plane at a fixed height. What the path falls back to with no terrain.</summary>
    public sealed class FlatPathGround : IPathGround
    {
        readonly float _y;
        public FlatPathGround(float y) { _y = y; }

        public bool Sample(Vector3 worldPos, out Vector3 point, out Vector3 normal)
        {
            point = new Vector3(worldPos.x, _y, worldPos.z);
            normal = Vector3.up;
            return true;
        }
    }
}
