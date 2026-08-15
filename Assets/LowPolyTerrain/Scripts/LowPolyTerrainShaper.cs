using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LowPolyTerrain
{
    /// <summary>
    /// Drives <see cref="LowPolyTerrainBuilder"/> onto a live Unity Terrain: a faceted floor pan
    /// with a mountain wall ringing the map.
    ///
    /// Unlike the mesh generators here this does not rebuild from OnValidate. A heightmap write is
    /// expensive and destructive, and OnValidate also fires on scene load and after every recompile,
    /// so shaping is always an explicit button press. The original heightmap is written to disk on
    /// the first apply and can be restored at any time.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("Low Poly Terrain/Low Poly Terrain Shaper")]
    public class LowPolyTerrainShaper : MonoBehaviour
    {
        [SerializeField]
        LowPolyTerrainSettings _settings = new LowPolyTerrainSettings();

        [SerializeField]
        [Tooltip("Ground layers in the order the painter uses them: ash, scorched, basalt, molten. " +
                 "Left empty, the generated lava-world set is loaded automatically - use " +
                 "Tools > Low Poly Terrain > Generate Lava World Layers to (re)create it.")]
        TerrainLayer[] _layers = new TerrainLayer[LowPolyTerrainPainter.LayerCount];

        [SerializeField, HideInInspector]
        string _backupPath;

        /// <summary>
        /// Where the lava-world ground layers live, in painter order. These are flat-colour layers
        /// generated into the project rather than borrowed from a texture pack: a photographed
        /// ground texture fights the faceted geometry, whereas flat colour is what the low poly look
        /// actually wants.
        /// </summary>
        public static readonly string[] DefaultLayerPaths =
        {
            "Assets/LowPolyTerrain/Layers/LPT_Ash.terrainlayer",
            "Assets/LowPolyTerrain/Layers/LPT_Scorched.terrainlayer",
            "Assets/LowPolyTerrain/Layers/LPT_Basalt.terrainlayer",
            "Assets/LowPolyTerrain/Layers/LPT_Molten.terrainlayer",
        };

        public TerrainLayer[] Layers { get { return _layers; } }

        public LowPolyTerrainSettings Settings { get { return _settings; } }
        public string BackupPath { get { return _backupPath; } }

        public Terrain Terrain { get { return GetComponent<Terrain>(); } }

        public bool HasBackup
        {
            get { return !string.IsNullOrEmpty(_backupPath) && File.Exists(_backupPath); }
        }

        /// <summary>Default backup location, derived from the TerrainData asset name.</summary>
        public string DefaultBackupPath
        {
            get
            {
                Terrain t = Terrain;
                string name = t != null && t.terrainData != null ? t.terrainData.name : "Terrain";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                return Path.Combine(
                    Application.dataPath, "LowPolyTerrain/Backups/" + name + ".heights.bytes");
            }
        }

        // ---------------------------------------------------------------- apply

        /// <summary>
        /// Shapes the terrain. Returns the build result so callers can report the stats.
        /// The original heightmap is captured first if it has not been captured already.
        /// </summary>
        public LowPolyTerrainBuilder.Result Apply()
        {
            Terrain terrain = Terrain;
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;

            float[,] original = EnsureBackup(data, res);

            List<ProtectedArea> areas = CollectProtectedAreas(terrain, original, res);

            LowPolyTerrainBuilder.Result result = LowPolyTerrainBuilder.Build(
                _settings, res, data.size.x, data.size.z, data.size.y, areas);

            data.SetHeights(0, 0, result.Heights);
            data.SyncHeightmap();

            if (_settings.paintLayers)
                Paint(data, result.Heights);

            return result;
        }

        /// <summary>
        /// Assigns the ground layers and paints them by height and slope. Split out so the texturing
        /// can be re-run on its own while tuning thresholds, without rebuilding the heightmap.
        /// </summary>
        public bool Paint(TerrainData data, float[,] heights)
        {
            if (!EnsureLayers())
            {
                Debug.LogWarning(
                    "Low Poly Terrain: no ground layers assigned and the default low poly set could " +
                    "not be found, so texturing was skipped.", this);
                return false;
            }

            // Compare contents, not just the count. Swapping one four-layer palette for another
            // leaves the length unchanged, so a length-only check silently keeps the old layers
            // while happily writing splat weights meant for the new ones.
            if (!SameLayers(data.terrainLayers, _layers))
                data.terrainLayers = (TerrainLayer[])_layers.Clone();

            float[,,] map = LowPolyTerrainPainter.Build(
                _settings, heights, data.alphamapResolution, data.size.x, data.size.z, data.size.y);

            data.SetAlphamaps(0, 0, map);
            return true;
        }

        static bool SameLayers(TerrainLayer[] a, TerrainLayer[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Fills any empty layer slot from the default low poly set.</summary>
        public bool EnsureLayers()
        {
            if (_layers == null || _layers.Length != LowPolyTerrainPainter.LayerCount)
            {
                var resized = new TerrainLayer[LowPolyTerrainPainter.LayerCount];
                if (_layers != null)
                    for (int i = 0; i < Mathf.Min(_layers.Length, resized.Length); i++)
                        resized[i] = _layers[i];
                _layers = resized;
            }

#if UNITY_EDITOR
            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i] != null) continue;
                _layers[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(DefaultLayerPaths[i]);
            }
#endif

            foreach (TerrainLayer l in _layers)
                if (l == null) return false;

            return true;
        }

        /// <summary>Builds without writing, so the inspector can report the numbers before you commit.</summary>
        public LowPolyTerrainBuilder.Result Preview()
        {
            Terrain terrain = Terrain;
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;

            float[,] original = HasBackup ? ReadBackup(_backupPath) : data.GetHeights(0, 0, res, res);
            List<ProtectedArea> areas = CollectProtectedAreas(terrain, original, res);

            return LowPolyTerrainBuilder.Build(
                _settings, res, data.size.x, data.size.z, data.size.y, areas);
        }

        /// <summary>Puts the heightmap back exactly as it was before the first apply.</summary>
        public bool Restore()
        {
            if (!HasBackup) return false;

            TerrainData data = Terrain.terrainData;
            float[,] original = ReadBackup(_backupPath);
            if (original == null) return false;

            int res = original.GetLength(0);
            if (res != data.heightmapResolution)
            {
                Debug.LogError(
                    "Low Poly Terrain: backup is " + res + " but the terrain is now " +
                    data.heightmapResolution + ". Refusing to restore.", this);
                return false;
            }

            data.SetHeights(0, 0, original);
            data.SyncHeightmap();
            return true;
        }

        // ---------------------------------------------------------------- protection

        /// <summary>
        /// Every renderer in the scene that stands on this terrain becomes a patch of ground held at
        /// its original height. Heights come from the backup rather than from the live terrain, so
        /// applying twice gives the same world instead of drifting a little further each time.
        /// </summary>
        public List<ProtectedArea> CollectProtectedAreas(Terrain terrain, float[,] original, int res)
        {
            var areas = new List<ProtectedArea>();
            if (!_settings.protectExistingObjects) return areas;

            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            foreach (Renderer r in renderers)
            {
                if (r == null || r.transform.IsChildOf(terrain.transform)) continue;
                // Particles and trails have meaningless bounds for this purpose.
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

                Bounds b = r.bounds;
                float cx = b.center.x - origin.x;
                float cz = b.center.z - origin.z;

                if (cx < 0f || cx > data.size.x || cz < 0f || cz > data.size.z) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z) + _settings.protectionMargin;
                float height = SampleNormalised(original, res, cx / data.size.x, cz / data.size.z) * data.size.y;

                areas.Add(new ProtectedArea(cx, cz, radius, height));
            }

            return MergeOverlapping(areas);
        }

        /// <summary>
        /// Two protected discs that overlap fight over the ground between them and leave a ridge.
        /// Collapsing them into one disc at the mean height keeps that ground flat.
        /// </summary>
        static List<ProtectedArea> MergeOverlapping(List<ProtectedArea> areas)
        {
            bool merged = true;
            while (merged && areas.Count > 1)
            {
                merged = false;
                for (int i = 0; i < areas.Count && !merged; i++)
                {
                    for (int j = i + 1; j < areas.Count && !merged; j++)
                    {
                        ProtectedArea a = areas[i], b = areas[j];
                        float dx = a.centerX - b.centerX;
                        float dz = a.centerZ - b.centerZ;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > a.radius + b.radius) continue;

                        var c = new ProtectedArea(
                            (a.centerX + b.centerX) * 0.5f,
                            (a.centerZ + b.centerZ) * 0.5f,
                            d * 0.5f + Mathf.Max(a.radius, b.radius),
                            (a.height + b.height) * 0.5f);

                        areas[i] = c;
                        areas.RemoveAt(j);
                        merged = true;
                    }
                }
            }
            return areas;
        }

        static float SampleNormalised(float[,] heights, int res, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * (res - 1);
            float fz = Mathf.Clamp01(v) * (res - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, res - 1);
            int x1 = Mathf.Min(x0 + 1, res - 1);
            int z1 = Mathf.Min(z0 + 1, res - 1);
            float tx = fx - x0, tz = fz - z0;

            float a = Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx);
            float b = Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        // ---------------------------------------------------------------- backup

        float[,] EnsureBackup(TerrainData data, int res)
        {
            if (HasBackup)
            {
                float[,] existing = ReadBackup(_backupPath);
                if (existing != null && existing.GetLength(0) == res) return existing;

                Debug.LogWarning(
                    "Low Poly Terrain: existing backup did not match the terrain and was replaced.", this);
            }

            float[,] heights = data.GetHeights(0, 0, res, res);
            _backupPath = DefaultBackupPath;
            WriteBackup(_backupPath, heights, res);

            Debug.Log("Low Poly Terrain: original heightmap backed up to " + _backupPath, this);
            return heights;
        }

        static void WriteBackup(string path, float[,] heights, int res)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var w = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write)))
            {
                w.Write(res);
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        w.Write(heights[z, x]);
            }
        }

        static float[,] ReadBackup(string path)
        {
            try
            {
                using (var r = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read)))
                {
                    int res = r.ReadInt32();
                    if (res <= 1 || res > 8193) return null;

                    var heights = new float[res, res];
                    for (int z = 0; z < res; z++)
                        for (int x = 0; x < res; x++)
                            heights[z, x] = r.ReadSingle();
                    return heights;
                }
            }
            catch (IOException e)
            {
                Debug.LogError("Low Poly Terrain: could not read backup - " + e.Message);
                return null;
            }
        }
    }
}
