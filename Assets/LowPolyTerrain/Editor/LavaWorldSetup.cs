using System.IO;
using UnityEditor;
using UnityEngine;

namespace LowPolyTerrain.EditorTools
{
    /// <summary>
    /// Builds the lava-world dressing that the shaper alone cannot: the flat-colour ground layers,
    /// a procedural starfield skybox, and the night lighting rig.
    ///
    /// Kept as menu commands rather than folded into the shaper because these write project assets
    /// and scene-wide render settings - things you want to run deliberately, once, not every time
    /// you nudge a terrain slider.
    /// </summary>
    public static class LavaWorldSetup
    {
        const string LayerFolder = "Assets/LowPolyTerrain/Layers";
        const string SkyFolder = "Assets/LowPolyTerrain/Sky";
        const int GroundTexSize = 512;

        /// <summary>Ground palette, in painter order: ash, scorched, basalt, molten.</summary>
        /// <summary>
        /// Albedo is deliberately lighter than volcanic rock "really" is. Under a night rig these
        /// values are multiplied by a dim light, so a physically dark rock reads as pure black and
        /// the facets stop being visible at all - which loses the whole look.
        /// </summary>
        static readonly (string name, Color color, float tile)[] Palette =
        {
            ("LPT_Ash",      new Color(0.265f, 0.230f, 0.215f), 14f),
            ("LPT_Scorched", new Color(0.330f, 0.140f, 0.085f), 16f),
            ("LPT_Basalt",   new Color(0.145f, 0.142f, 0.165f), 18f),
            ("LPT_Molten",   new Color(1.000f, 0.420f, 0.080f), 22f),
        };

        // ---------------------------------------------------------------- ground layers

        [MenuItem("Tools/Low Poly Terrain/Generate Lava World Layers", false, 1)]
        public static void GenerateLayers()
        {
            Directory.CreateDirectory(LayerFolder);

            for (int i = 0; i < Palette.Length; i++)
            {
                var (name, color, tile) = Palette[i];

                Color[] pixels;
                if (name == "LPT_Molten")
                {
                    pixels = LowPolyTerrainTextures.MoltenCrust(
                        color, new Color(0.34f, 0.125f, 0.075f), GroundTexSize, 20260811 + i, 0.60f);
                }
                else
                {
                    pixels = LowPolyTerrainTextures.FlatGround(
                        color, GroundTexSize, 20260811 + i, 0.10f, 4f);
                }

                Texture2D tex = WriteTexture(LayerFolder + "/" + name + ".png", pixels, GroundTexSize, GroundTexSize);

                string layerPath = LayerFolder + "/" + name + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, layerPath);
                }

                layer.diffuseTexture = tex;
                layer.tileSize = new Vector2(tile, tile);
                layer.tileOffset = Vector2.zero;
                // Volcanic rock is not shiny; a specular highlight would fight the flat shading.
                layer.specular = Color.black;
                layer.metallic = 0f;
                layer.smoothness = 0f;
                EditorUtility.SetDirty(layer);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Low Poly Terrain: generated " + Palette.Length + " lava world ground layers in " + LayerFolder);
        }

        static Texture2D WriteTexture(string path, Color[] pixels, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- night sky

        [MenuItem("Tools/Low Poly Terrain/Build Night Sky", false, 2)]
        public static void BuildNightSky()
        {
            Directory.CreateDirectory(SkyFolder);

            const int W = 4096, H = 2048;

            Color[] pixels = LowPolyTerrainTextures.Starfield(
                W, H,
                seed: 20260811,
                starCount: 5200,
                zenith: new Color(0.010f, 0.012f, 0.030f),
                horizon: new Color(0.030f, 0.020f, 0.032f),
                emberGlow: new Color(0.115f, 0.030f, 0.008f),
                galaxyStrength: 0.55f);

            string texPath = SkyFolder + "/LPT_NightSky.png";

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 4096;
                // A skybox is viewed directly, so block compression artefacts show up badly in the
                // large flat dark areas between stars.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var skyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            // Skybox/Panoramic is a built-in shader and renders correctly under URP - unlike the
            // Standard surface shader, which is "supported" and comes out magenta.
            Shader shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
            {
                Debug.LogError("Low Poly Terrain: shader 'Skybox/Panoramic' not found; skybox not built.");
                return;
            }

            string matPath = SkyFolder + "/LPT_NightSky.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = shader;
            mat.SetTexture("_MainTex", skyTex);
            mat.SetFloat("_Mapping", 1f);      // latitude/longitude layout
            mat.SetFloat("_ImageType", 0f);    // full 360
            mat.SetFloat("_Exposure", 1.15f);
            mat.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Low Poly Terrain: built night sky at " + matPath, mat);
        }

        // ---------------------------------------------------------------- lighting

        [MenuItem("Tools/Low Poly Terrain/Apply Night Lighting", false, 3)]
        public static void ApplyNightLighting()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyFolder + "/LPT_NightSky.mat");
            if (mat == null)
            {
                Debug.LogWarning("Low Poly Terrain: no night sky material yet - run Build Night Sky first.");
            }
            else
            {
                Undo.RecordObject(RenderSettings.skybox != null ? (Object)RenderSettings.skybox : mat, "Night Lighting");
                RenderSettings.skybox = mat;
            }

            // Moonlight: cool, and high in the sky. A low moon looks more dramatic in a still, but
            // a 55 m crater wall then shadows the entire floor - which is the surface you actually
            // drive on. Elevation here is a playability decision, not an aesthetic one.
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }

            if (sun != null)
            {
                Undo.RecordObject(sun, "Night Lighting");
                Undo.RecordObject(sun.transform, "Night Lighting");
                sun.color = new Color(0.60f, 0.68f, 0.95f);
                // Bright enough to drive by. A physically plausible moon reads as night on a
                // screenshot and as unplayable in a kart - this is lit for legibility, not realism.
                sun.intensity = 0.85f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
                sun.transform.rotation = Quaternion.Euler(52f, 205f, 0f);
                EditorUtility.SetDirty(sun);
            }

            // Trilight ambient keys off the surface normal, and this is the part that catches people
            // out: ambientGroundColor lights DOWNWARD-facing normals. The crater floor faces up, so
            // it is lit by ambientSkyColor - a warm "bounce from below" does nothing to it. The sky
            // colour therefore carries some warmth too, or the drivable floor goes black and the
            // whole map becomes unreadable at kart height.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.175f, 0.140f, 0.205f);
            RenderSettings.ambientEquatorColor = new Color(0.240f, 0.140f, 0.130f);
            RenderSettings.ambientGroundColor = new Color(0.420f, 0.160f, 0.060f);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.075f, 0.035f, 0.032f);

            // Fog density is per-metre, so a value tuned on one map size is wrong on another - the
            // same number that reads as haze across 250 m swallows a 500 m map whole. Scale it to
            // the terrain so the far wall stays visible at any size.
            var t = Object.FindAnyObjectByType<Terrain>();
            float span = t != null && t.terrainData != null
                ? Mathf.Max(t.terrainData.size.x, t.terrainData.size.z)
                : 250f;
            RenderSettings.fogDensity = 1f / (1.6f * span);

            DynamicGI.UpdateEnvironment();
            Debug.Log("Low Poly Terrain: night lighting applied (moonlight, trilight ambient, ember fog).");
        }

        // ---------------------------------------------------------------- lava glow

        /// <summary>
        /// Terrain layers have no emission channel, so painted lava cannot light anything. These
        /// point lights are what turn the molten basins from orange paint into a light source.
        /// </summary>
        [MenuItem("Tools/Low Poly Terrain/Place Lava Glow Lights", false, 4)]
        public static void PlaceLavaGlowLights()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null) { Debug.LogWarning("Low Poly Terrain: no terrain in the scene."); return; }

            var shaper = terrain.GetComponent<LowPolyTerrainShaper>();
            float moltenHeight = shaper != null ? shaper.Settings.moltenHeight : 9f;

            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            const string RootName = "Lava Glow";
            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Place Lava Glow Lights");

            int res = data.heightmapResolution;
            float[,] h = data.GetHeights(0, 0, res, res);

            // Walk a coarse grid and drop a light in each low, flat pocket, keeping them apart so a
            // single wide basin does not collect a dozen overlapping lights.
            // URP's forward path only takes the nearest 4 additional lights per renderer, so a
            // dense grid of glow lights makes terrain patches pop between them as the camera moves.
            // Fewer, wider, stronger lights stay inside that budget instead of fighting it.
            var placed = new System.Collections.Generic.List<Vector3>();
            const float MinSpacing = 46f;
            int step = Mathf.Max(1, res / 48);

            for (int jz = 1; jz < res - 1; jz += step)
            {
                for (int ix = 1; ix < res - 1; ix += step)
                {
                    float height = h[jz, ix] * data.size.y;
                    if (height > moltenHeight) continue;

                    float wx = (float)ix / (res - 1) * data.size.x;
                    float wz = (float)jz / (res - 1) * data.size.z;
                    var p = new Vector3(origin.x + wx, origin.y + height, origin.z + wz);

                    bool tooClose = false;
                    foreach (var q in placed)
                        if ((q - p).sqrMagnitude < MinSpacing * MinSpacing) { tooClose = true; break; }
                    if (tooClose) continue;

                    placed.Add(p);

                    var go = new GameObject("LavaGlow_" + placed.Count);
                    go.transform.SetParent(root.transform);
                    go.transform.position = p + Vector3.up * 3.5f;

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.40f, 0.10f);
                    light.intensity = 11f;
                    light.range = 78f;
                    light.shadows = LightShadows.None;
                }
            }

            Debug.Log("Low Poly Terrain: placed " + placed.Count + " lava glow lights under " + RootName, root);
        }

        // ---------------------------------------------------------------- everything

        [MenuItem("Tools/Low Poly Terrain/Set Up Lava World (all of the above)", false, 20)]
        public static void SetUpEverything()
        {
            GenerateLayers();
            BuildNightSky();
            ApplyNightLighting();
            PlaceLavaGlowLights();
        }
    }
}
