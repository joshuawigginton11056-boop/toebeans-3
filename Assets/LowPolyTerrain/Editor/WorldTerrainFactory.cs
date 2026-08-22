using System.IO;
using UnityEditor;
using UnityEngine;

namespace LowPolyTerrain.EditorTools
{
    /// <summary>
    /// Creates a terrain at the canonical <see cref="WorldMetrics"/> size, with a shaper already on
    /// it, and its TerrainData saved as a project asset.
    ///
    /// The asset has to exist on disk before anything is written into it. A TerrainData created in
    /// memory and assigned to a Terrain is serialised inline into the scene - the same trap
    /// SceneMeshExtractor exists to undo for meshes - and a 1025 heightmap plus four splat maps is
    /// several megabytes of it. Creating the asset first also means the heightmap survives if the
    /// scene is closed without saving.
    /// </summary>
    public static class WorldTerrainFactory
    {
        const string TerrainFolder = "Assets/Terrain";

        /// <summary>
        /// URP's stock terrain material. LavaWorld references this package asset directly rather
        /// than a project copy, so new worlds do too - a per-world copy would drift.
        /// </summary>
        const string TerrainMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

        /// <summary>
        /// Builds the terrain for <paramref name="worldName"/> into the open scene and returns it.
        /// Fails rather than overwrites if the TerrainData asset already exists, because that asset
        /// is the map - silently replacing it would discard a shaped world.
        /// </summary>
        public static Terrain Create(string worldName)
        {
            Directory.CreateDirectory(TerrainFolder);

            string path = TerrainFolder + "/" + worldName + "_Terrain.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null)
            {
                Debug.LogError(
                    "World Terrain Factory: " + path + " already exists. Delete it first if you " +
                    "really mean to rebuild " + worldName + " from scratch.");
                return null;
            }

            var data = new TerrainData();
            data.name = worldName + "_Terrain";

            // Order matters: assigning heightmapResolution resets size back to Unity's default, so
            // resolution has to be set first and size second. Doing it the other way round leaves a
            // 1000 m terrain wearing the right resolution, which looks fine in the inspector and is
            // wrong by a factor of two everywhere else.
            data.heightmapResolution = WorldMetrics.HeightmapResolution;
            data.size = WorldMetrics.Size;

            data.alphamapResolution = WorldMetrics.AlphamapResolution;
            data.baseMapResolution = WorldMetrics.BaseMapResolution;
            data.SetDetailResolution(WorldMetrics.DetailResolution, WorldMetrics.DetailResolutionPerPatch);

            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = WorldMetrics.Origin;
            Undo.RegisterCreatedObjectUndo(go, "Create World Terrain");

            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = WorldMetrics.HeightmapPixelError;
            terrain.basemapDistance = WorldMetrics.BasemapDistance;
            terrain.drawInstanced = true;

            // Two-sided, so the mountain wall still casts into the bowl when the sun is behind it.
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning("World Terrain Factory: URP TerrainLit not found; leaving the pipeline default.");

            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);

            go.AddComponent<LowPolyTerrainShaper>();

            Debug.Log(
                "World Terrain Factory: created " + worldName + " terrain at " + WorldMetrics.Size +
                " (heightmap " + WorldMetrics.HeightmapResolution + ") -> " + path, go);

            return terrain;
        }
    }
}
