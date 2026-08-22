using System.IO;
using UnityEditor;
using UnityEngine;

namespace LowPolyTerrain.EditorTools
{
    /// <summary>
    /// FarmWorld's dressing: the flat-colour ground layers, a procedural daytime skybox, and the
    /// bright lighting rig. The counterpart to <see cref="LavaWorldSetup"/>, and deliberately a
    /// separate file rather than a mode switch on that one - the two worlds share the terrain
    /// generator, not their art direction, and folding them together would mean every lava tweak
    /// risking the farm.
    ///
    /// Menu commands rather than something the shaper runs, for the same reason as LavaWorld:
    /// these write project assets and scene-wide render settings, which you want to happen
    /// deliberately, not on every slider nudge.
    /// </summary>
    public static class FarmWorldSetup
    {
        const string LayerFolder = "Assets/LowPolyTerrain/Layers";
        const string SkyFolder = "Assets/LowPolyTerrain/Sky";
        const int GroundTexSize = 512;

        /// <summary>
        /// Where the sun sits, in degrees of altitude and azimuth. One definition feeding both the
        /// light and the painted sun disc - see <see cref="ApplyDayLighting"/> for why they must not
        /// be allowed to drift apart.
        ///
        /// <b>Azimuth here is NOT a Unity yaw.</b> It is measured from +X turning toward +Z, the
        /// convention <c>atan2(z, x)</c> uses, because that is what the latlong sky maths wants.
        /// Unity's Euler-Y runs the other way, from +Z toward +X, so the two are related by
        /// <c>yaw = 90 - azimuth</c> - this sun is at azimuth 205, which is yaw 245. Point a camera
        /// at the raw number and the sun appears 40 degrees off, which looks exactly like a broken
        /// skybox and is not one.
        ///
        /// Mid-morning rather than noon. A sun directly overhead flattens the facets, which are the
        /// entire look; at 48 degrees the mountain wall and every roll in the pan get a lit face and
        /// a shaded one. Not lower, because a long shadow from a 75 m wall reaches 65 m into the
        /// playable bowl and puts the racing line in the dark.
        /// </summary>
        public const float SunAltitude = 48f;
        public const float SunAzimuth = 205f;

        /// <summary>
        /// Ground palette, in the order <see cref="LowPolyTerrainPainter"/> uses its four slots:
        /// flats, rises, steep ground, low basins.
        ///
        /// The painter's slot names are lava names - ash, scorched, basalt, molten - because that is
        /// the world it was written for, but the rules underneath are pure height and slope. Read
        /// them as "flat / medium slope / steep / low-lying" and the same four rules dress a farm,
        /// and they suit this terrain particularly well: meadow on the valley floor, dry pasture up
        /// the hill flanks, rock on the tops and the mountain wall, and marsh in the pond basin.
        ///
        /// The marsh slot is worth understanding rather than renaming. The molten rule pools its
        /// layer in low ground and refuses to climb a slope, which is exactly how standing water
        /// behaves - so the rule that put lava in the crater floor puts wet ground round the pond
        /// with no change beyond the colour.
        /// </summary>
        static readonly (string name, Color color, float tile, float variation)[] Palette =
        {
            // Yellow-green rather than a pure green. Saturated green under a bright sun clips
            // toward neon and stops reading as grass.
            ("LPT_Meadow",  new Color(0.375f, 0.510f, 0.215f), 14f, 0.12f),
            ("LPT_Pasture", new Color(0.520f, 0.520f, 0.290f), 16f, 0.13f),
            ("LPT_Rock",    new Color(0.430f, 0.415f, 0.385f), 18f, 0.10f),
            ("LPT_Marsh",   new Color(0.245f, 0.265f, 0.170f), 20f, 0.16f),
        };

        /// <summary>
        /// The farm layers in painter order, for assigning to a shaper. Derived from
        /// <see cref="Palette"/> rather than listed again - the two drifting apart would assign one
        /// set of layers and paint weights meant for another, which shows up as a plausible-looking
        /// map in the wrong colours rather than as an error.
        /// </summary>
        public static readonly string[] LayerPaths = BuildLayerPaths();

        static string[] BuildLayerPaths()
        {
            var paths = new string[Palette.Length];
            for (int i = 0; i < Palette.Length; i++)
                paths[i] = LayerFolder + "/" + Palette[i].name + ".terrainlayer";
            return paths;
        }

        // ---------------------------------------------------------------- ground layers

        [MenuItem("Tools/Low Poly Terrain/Farm World/Generate Farm Layers", false, 1)]
        public static void GenerateLayers()
        {
            Directory.CreateDirectory(LayerFolder);

            for (int i = 0; i < Palette.Length; i++)
            {
                var entry = Palette[i];

                // Mottle scale is in tiles, so the same number reads differently per layer. 4 keeps
                // the variation well below the facet size on all four, which is the point: it should
                // say "surface" without ever competing with the geometry.
                Color[] pixels = LowPolyTerrainTextures.FlatGround(
                    entry.color, GroundTexSize, 20260820 + i, entry.variation, 4f);

                Texture2D tex = WriteTexture(
                    LayerFolder + "/" + entry.name + ".png", pixels, GroundTexSize, GroundTexSize);

                string layerPath = LayerFolder + "/" + entry.name + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, layerPath);
                }

                layer.diffuseTexture = tex;
                layer.tileSize = new Vector2(entry.tile, entry.tile);
                layer.tileOffset = Vector2.zero;

                // Flat shading wants no specular highlight - a moving hotspot across a facet fights
                // the constant-shade look. Grass gets a trace of smoothness so it is not completely
                // inert under a bright sky; rock and soil get none.
                layer.specular = Color.black;
                layer.metallic = 0f;
                layer.smoothness = entry.name == "LPT_Meadow" ? 0.06f
                    : entry.name == "LPT_Marsh" ? 0.12f : 0f;
                EditorUtility.SetDirty(layer);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Low Poly Terrain: generated " + Palette.Length + " farm world ground layers in " + LayerFolder);
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

        // ---------------------------------------------------------------- terrain shape

        /// <summary>
        /// Shapes FarmWorld: hill country either side of a valley, with a basin scooped out of one
        /// corner for a pond.
        ///
        /// Deliberately NOT LavaWorld's numbers. That world is a crater - a broad agitated floor
        /// inside a ring wall, with the relief carried by high-frequency pan noise. Reusing it here
        /// gave a flat green field with 11 m of chop across 390 m, which is neither hills nor a
        /// valley. This is a different landform: a quiet floor, real hills standing off it, and one
        /// route through.
        ///
        /// The numbers live here rather than being typed into the inspector so the world can be
        /// rebuilt from scratch, and so the reasoning survives next to them.
        /// </summary>
        [MenuItem("Tools/Low Poly Terrain/Farm World/Shape Farm Valley", false, 0)]
        public static void ShapeValley()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Debug.LogError("Low Poly Terrain: no active terrain, so nothing was shaped.");
                return;
            }

            var shaper = terrain.GetComponent<LowPolyTerrainShaper>();
            if (shaper == null)
            {
                Debug.LogError("Low Poly Terrain: the terrain has no LowPolyTerrainShaper.");
                return;
            }

            GenerateLayers();

            Undo.RecordObject(shaper, "Shape Farm Valley");
            LowPolyTerrainSettings s = shaper.Settings;

            s.seed = 4471902;

            // The valley floor sits high in the height budget on purpose. The pond is carved DOWN
            // from it, and there is nothing below terrain zero to carve into - a floor at 6 m like
            // LavaWorld would clip the basin flat at the bottom.
            s.panHeight = 24f;
            s.panRelief = 7f;
            s.panWavelength = 140f;
            s.panOctaves = 2;
            s.panFlatten = 0.45f;

            s.hillHeight = 46f;
            s.hillWavelength = 165f;
            s.hillOctaves = 3;
            s.hillCoverage = 0.62f;

            // 46 m of hill over a 130 m blend is about 19 degrees of flank, which a kart can climb.
            // Halve the blend and the same hills become a 35 degree wall that only looks driveable.
            s.valleyWidth = 150f;
            s.valleyBlend = 130f;
            s.valleyBearingDegrees = 35f;
            s.valleyOffset = 0f;
            s.valleyWander = 45f;

            // Corner nearest terrain-local origin, on the valley floor and well inside the wall foot.
            s.pondCenter = new Vector2(0.22f, 0.19f);
            s.pondRadius = 58f;
            s.pondDepth = 11f;
            s.pondFloorFlat = 0.38f;

            // Marsh is the low-basin rule, so it paints the pond and nothing else: full below 13 m,
            // gone by 18 m, and it will not climb anything steeper than 20 degrees.
            s.moltenHeight = 18f;
            s.moltenBand = 5f;
            s.moltenMaxSlope = 20f;
            s.scorchedSlopeStart = 8f;
            s.scorchedSlopeFull = 20f;
            s.basaltSlopeStart = 28f;
            s.basaltSlopeFull = 42f;

            // Stamp the farm palette into the shaper's slots explicitly.
            //
            // Nulling the slots and letting the shaper refill them does NOT work, and the way it
            // fails is quiet: LowPolyTerrainShaper.EnsureLayers fills an empty slot from its own
            // DefaultLayerPaths, which is the lava set. So a farm valley comes out painted in ash
            // and basalt - a plausible-looking map in the wrong colours, which is exactly the
            // failure this assignment exists to prevent.
            //
            // EnsureLayers first, only to normalise the array to LowPolyTerrainPainter.LayerCount:
            // a terrain carrying an older palette can have a slot count that does not match, and
            // writing four layers into a five-slot array would leave a stale fifth behind.
            shaper.EnsureLayers();
            TerrainLayer[] slots = shaper.Layers;
            for (int i = 0; i < slots.Length && i < LayerPaths.Length; i++)
                slots[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPaths[i]);

            LowPolyTerrainBuilder.Result built = shaper.Apply();

            TerrainData data = terrain.terrainData;
            float px = s.pondCenter.x * data.size.x + terrain.transform.position.x;
            float pz = s.pondCenter.y * data.size.z + terrain.transform.position.z;
            float py = terrain.SampleHeight(new Vector3(px, 0f, pz)) + terrain.transform.position.y;

            EditorUtility.SetDirty(shaper);

            Debug.Log("Low Poly Terrain: shaped Farm World - heights "
                + built.MinHeight.ToString("F1") + " to " + built.MaxHeight.ToString("F1")
                + " m, steepest floor " + built.MaxPanSlopeDegrees.ToString("F1") + " deg. "
                + "Pond floor is at y = " + py.ToString("F2") + " at world ("
                + px.ToString("F0") + ", " + pz.ToString("F0") + "), radius " + s.pondRadius
                + " m - put the water plane a little above that.", terrain);
        }

        // ---------------------------------------------------------------- day sky

        [MenuItem("Tools/Low Poly Terrain/Farm World/Build Day Sky", false, 2)]
        public static void BuildDaySky()
        {
            Directory.CreateDirectory(SkyFolder);

            const int W = 4096, H = 2048;

            Color[] pixels = LowPolyTerrainTextures.DaySky(
                W, H,
                seed: 20260820,
                zenith: new Color(0.170f, 0.360f, 0.680f),
                horizon: new Color(0.690f, 0.800f, 0.880f),
                sunColor: new Color(1.000f, 0.955f, 0.830f),
                sunAltitudeDegrees: SunAltitude,
                sunAzimuthDegrees: SunAzimuth,
                cloudCover: 0.38f,
                cloudSharpness: 3.2f);

            string texPath = SkyFolder + "/LPT_DaySky.png";

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
                // A clear sky is one huge smooth gradient, which is the worst case for block
                // compression - DXT banding is far more visible here than on any ground texture.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var skyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            // Skybox/Panoramic is built in and renders correctly under URP, unlike the Standard
            // surface shader which is nominally supported and comes out magenta.
            Shader shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
            {
                Debug.LogError("Low Poly Terrain: shader 'Skybox/Panoramic' not found; day sky not built.");
                return;
            }

            string matPath = SkyFolder + "/LPT_DaySky.mat";
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
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", SkyboxRotation);
            EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Low Poly Terrain: built day sky at " + matPath, mat);
        }

        /// <summary>
        /// Yaw applied to the skybox material, in degrees.
        ///
        /// Zero, and it should stay zero: <see cref="LowPolyTerrainTextures.DaySky"/> paints
        /// directly in Skybox/Panoramic's own latlong convention, so the sun already lands where
        /// the directional light points. This exists as a named constant only so that anyone who
        /// finds the sky misaligned looks at the generator rather than "fixing" it here - the two
        /// conventions differ by a mirror, and a yaw cannot undo a mirror.
        /// </summary>
        public const float SkyboxRotation = 0f;

        // ---------------------------------------------------------------- lighting

        [MenuItem("Tools/Low Poly Terrain/Farm World/Apply Day Lighting", false, 3)]
        public static void ApplyDayLighting()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyFolder + "/LPT_DaySky.mat");
            if (mat == null)
                Debug.LogWarning("Low Poly Terrain: no day sky material yet - run Build Day Sky first.");
            else
                RenderSettings.skybox = mat;

            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }

            if (sun != null)
            {
                Undo.RecordObject(sun, "Day Lighting");
                Undo.RecordObject(sun.transform, "Day Lighting");

                sun.color = new Color(1f, 0.955f, 0.860f);
                sun.intensity = 1.45f;
                sun.shadows = LightShadows.Soft;

                // Softer than LavaWorld's 0.75. A bright sky fills shadows heavily in daylight, so
                // a full-strength shadow reads as night-time contrast pasted onto a day scene.
                sun.shadowStrength = 0.55f;

                // Aimed from the same two angles the skybox drew the sun disc from, rather than a
                // hand-typed euler. These have to agree: they are two independent representations of
                // one sun, and if they drift the shadows point somewhere the player can see the sun
                // is not. Deriving the rotation is what keeps that impossible.
                sun.transform.rotation = Quaternion.LookRotation(-SunDirection(), Vector3.up);

                EditorUtility.SetDirty(sun);
            }
            else
            {
                Debug.LogWarning("Low Poly Terrain: no directional light in the scene; sun not set up.");
            }

            // Trilight again, and the same trap as LavaWorld: ambientGroundColor lights DOWNWARD
            // facing normals, so it does nothing for the drivable floor. The floor faces up and is
            // lit by ambientSkyColor, which therefore has to carry real brightness - this is the
            // number that decides whether the bowl reads as sunlit or as overcast.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.620f, 0.680f, 0.780f);
            RenderSettings.ambientEquatorColor = new Color(0.545f, 0.560f, 0.480f);
            // Green, because on a farm the light bouncing up off the ground has come off grass.
            RenderSettings.ambientGroundColor = new Color(0.320f, 0.345f, 0.245f);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Pale blue haze, not grey: distance haze is scattered skylight, so it should sit
            // between the horizon colour and the ground rather than desaturating toward white.
            RenderSettings.fogColor = new Color(0.720f, 0.800f, 0.870f);

            // Per-metre, so it has to be scaled to the map or a value tuned on one size is wrong on
            // another. Thinner than LavaWorld's 1/(1.6 x span): night fog hides the far wall on
            // purpose, daylight should let you read the whole bowl and pick a line across it.
            var t = Object.FindAnyObjectByType<Terrain>();
            float span = t != null && t.terrainData != null
                ? Mathf.Max(t.terrainData.size.x, t.terrainData.size.z)
                : WorldMetrics.Span;
            RenderSettings.fogDensity = 1f / (3.2f * span);

            DynamicGI.UpdateEnvironment();
            Debug.Log("Low Poly Terrain: day lighting applied (sun at " + SunAltitude + " deg, trilight ambient, haze fog).");
        }

        /// <summary>
        /// Unit vector pointing from the world toward the sun, from the shared angles. Matches the
        /// convention <see cref="LowPolyTerrainTextures.DaySky"/> uses to place the disc.
        /// </summary>
        public static Vector3 SunDirection()
        {
            float lat = SunAltitude * Mathf.Deg2Rad;
            float lon = SunAzimuth * Mathf.Deg2Rad;
            float cl = Mathf.Cos(lat);
            return new Vector3(cl * Mathf.Cos(lon), Mathf.Sin(lat), cl * Mathf.Sin(lon));
        }

        // ---------------------------------------------------------------- everything

        [MenuItem("Tools/Low Poly Terrain/Farm World/Set Up Farm World (all of the above)", false, 20)]
        public static void SetUpEverything()
        {
            GenerateLayers();
            BuildDaySky();
            ApplyDayLighting();
        }
    }
}
