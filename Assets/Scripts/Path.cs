using UnityEngine;
using UnityEngine.Splines;

[ExecuteInEditMode]
public class SplineTerrainCarver : MonoBehaviour
{
    public Terrain terrain;
    public float trackWidth = 5f;
    public float depthOffset = -0.5f;

    [ContextMenu("Carve Terrain Along Spline")]
    public void Carve()
    {
        if (!terrain) terrain = Terrain.activeTerrain;
        SplineContainer container = GetComponent<SplineContainer>();
        if (!container || !terrain) return;

        TerrainData data = terrain.terrainData;
        int mapWidth = data.heightmapResolution;
        int mapHeight = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, mapWidth, mapHeight);

        // Sample points along the spline curve
        float length = container.CalculateLength();
        int samples = Mathf.CeilToInt(length);

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            Vector3 worldPos = container.EvaluatePosition(t);
            
            // Convert World Position to Terrain Heightmap Coordinates
            Vector3 terrainPos = worldPos - terrain.transform.position;
            int x = Mathf.RoundToInt((terrainPos.x / data.size.x) * mapWidth);
            int z = Mathf.RoundToInt((terrainPos.z / data.size.z) * mapHeight);

            float targetHeight = (worldPos.y + depthOffset - terrain.transform.position.y) / data.size.y;

            // Apply flattened height around the spline radius
            int radius = Mathf.RoundToInt((trackWidth / data.size.x) * mapWidth);
            for (int rx = -radius; rx <= radius; rx++)
            {
                for (int rz = -radius; rz <= radius; rz++)
                {
                    int px = Mathf.Clamp(x + rx, 0, mapWidth - 1);
                    int pz = Mathf.Clamp(z + rz, 0, mapHeight - 1);
                    heights[pz, px] = Mathf.Min(heights[pz, px], targetHeight);
                }
            }
        }

        data.SetHeights(0, 0, heights);
    }
}