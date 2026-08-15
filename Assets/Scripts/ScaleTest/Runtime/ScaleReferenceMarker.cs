using UnityEngine;

namespace Toebeans.ScaleTest
{
    /// <summary>
    /// Scene-view only ruler: a human-sized silhouette with metre ticks. Drop one next to a prop to
    /// check its size without entering play mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ScaleReferenceMarker : MonoBehaviour
    {
        [Tooltip("Height of the reference figure in metres.")]
        public float height = 1.8f;
        [Tooltip("Number of metre ticks drawn up the vertical ruler.")]
        public int rulerMetres = 10;
        public Color color = new Color(0.15f, 1f, 0.55f, 1f);

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Vector3 origin = transform.position;
            Quaternion rotation = transform.rotation;

            float headRadius = height * 0.07f;
            float shoulderWidth = height * 0.25f;
            float hipHeight = height * 0.52f;
            float shoulderHeight = height * 0.82f;

            Vector3 Local(float x, float y, float z = 0f) => origin + rotation * new Vector3(x, y, z);

            // Head
            Gizmos.DrawWireSphere(Local(0f, height - headRadius), headRadius);
            // Spine
            Gizmos.DrawLine(Local(0f, hipHeight), Local(0f, height - headRadius * 2f));
            // Shoulders and arms
            Gizmos.DrawLine(Local(-shoulderWidth * 0.5f, shoulderHeight), Local(shoulderWidth * 0.5f, shoulderHeight));
            Gizmos.DrawLine(Local(-shoulderWidth * 0.5f, shoulderHeight), Local(-shoulderWidth * 0.6f, hipHeight * 0.85f));
            Gizmos.DrawLine(Local(shoulderWidth * 0.5f, shoulderHeight), Local(shoulderWidth * 0.6f, hipHeight * 0.85f));
            // Hips and legs
            Gizmos.DrawLine(Local(-shoulderWidth * 0.35f, hipHeight), Local(shoulderWidth * 0.35f, hipHeight));
            Gizmos.DrawLine(Local(-shoulderWidth * 0.35f, hipHeight), Local(-shoulderWidth * 0.28f, 0f));
            Gizmos.DrawLine(Local(shoulderWidth * 0.35f, hipHeight), Local(shoulderWidth * 0.28f, 0f));

            // Vertical ruler with a tick every metre and a longer tick every five.
            Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
            float rulerX = shoulderWidth;
            Gizmos.DrawLine(Local(rulerX, 0f), Local(rulerX, Mathf.Max(rulerMetres, 1)));
            for (int metre = 0; metre <= Mathf.Max(rulerMetres, 1); metre++)
            {
                float tick = metre % 5 == 0 ? 0.3f : 0.12f;
                Gizmos.DrawLine(Local(rulerX, metre), Local(rulerX + tick, metre));
            }
        }
    }
}
