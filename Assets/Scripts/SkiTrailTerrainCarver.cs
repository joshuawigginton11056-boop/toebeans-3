using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;

namespace SkiGameTools
{
    [ExecuteInEditMode]
    public class SkiTrailTerrainCarver : MonoBehaviour
    {
        [Header("Terrain & Spline References")]
        public Terrain terrain;
        public SplineContainer splineContainer;

        [Header("Carving Parameters")]
        [Tooltip("Width of the carved channel in meters.")]
        public float trackWidth = 8f;

        [Tooltip("How deep to lower the terrain beneath the spline path (in meters).")]
        public float depthOffset = 0.05f;

        [Tooltip("Distance along spline per sampling step (lower = smoother gradient).")]
        public float stepDistance = 0.5f;

        [ContextMenu("Carve Downhill Terrain")]
        public void CarveTerrain()
        {
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();

            if (terrain == null || splineContainer == null)
            {
                Debug.LogError("SkiTrailTerrainCarver: Missing Terrain or SplineContainer reference.");
                return;
            }

            TerrainData data = terrain.terrainData;
            int mapWidth = data.heightmapResolution;
            int mapHeight = data.heightmapResolution;
            
            // Declare and fetch the 2D heightmap array
            float[,] heights = data.GetHeights(0, 0, mapWidth, mapHeight);

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = data.size;

            float splineLength = splineContainer.CalculateLength();
            int steps = Mathf.Max(2, Mathf.CeilToInt(splineLength / stepDistance));

            #if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(data, "Carve Terrain Along Spline");
            #endif

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                
                // Get world position along spline
                Vector3 worldPos = splineContainer.EvaluatePosition(t);

                // Map world coordinate to terrain normalized coordinate (0 to 1)
                float normX = (worldPos.x - terrainPos.x) / terrainSize.x;
                float normZ = (worldPos.z - terrainPos.z) / terrainSize.z;

                if (normX < 0 || normX > 1 || normZ < 0 || normZ > 1) continue;

                int centerX = Mathf.RoundToInt(normX * (mapWidth - 1));
                int centerZ = Mathf.RoundToInt(normZ * (mapHeight - 1));

                int radiusPixels = Mathf.RoundToInt((trackWidth / terrainSize.x) * mapWidth * 0.5f);

                // Use spline world position directly for Y level
                float targetWorldY = worldPos.y - depthOffset;
                float targetNormY = (targetWorldY - terrainPos.y) / terrainSize.y;

                for (int rx = -radiusPixels; rx <= radiusPixels; rx++)
                {
                    for (int rz = -radiusPixels; rz <= radiusPixels; rz++)
                    {
                        int px = Mathf.Clamp(centerX + rx, 0, mapWidth - 1);
                        int pz = Mathf.Clamp(centerZ + rz, 0, mapHeight - 1);

                        float distRatio = Vector2.Distance(new Vector2(rx, rz), Vector2.zero) / Mathf.Max(1, radiusPixels);
                        if (distRatio <= 1.0f)
                        {
                            // Smoothstep creates a clean, parabolic trench shape
                            float falloff = Mathf.SmoothStep(1.0f, 0.0f, distRatio);
                            float blendedTarget = Mathf.Lerp(heights[pz, px], targetNormY, falloff);

                            if (heights[pz, px] > blendedTarget)
                            {
                                heights[pz, px] = blendedTarget;
                            }
                        }
                    }
                }
            }

            // Apply modifications back to heightmap
            data.SetHeights(0, 0, heights);
            Debug.Log("Terrain carving complete along spline gradient.");
        }
    }
}