using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Volcano.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="VolcanoGenerator"/>: live stats, the warnings that catch the two or
    /// three ways of building a volcano that quietly does not work, and the buttons that hang the
    /// rest of the set piece off it — the lava in the crater, the rivers out of the spillways, the
    /// smoke and the mist.
    /// </summary>
    [CustomEditor(typeof(VolcanoGenerator))]
    public class VolcanoGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (VolcanoGenerator)target;

            EditorGUILayout.Space();
            DrawStats(generator);

            EditorGUILayout.Space();
            DrawWarnings(generator);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate")) generator.Generate();

                if (GUILayout.Button("Randomise Seed"))
                {
                    Undo.RecordObject(generator, "Randomise Volcano");
                    generator.Randomize();
                    EditorUtility.SetDirty(generator);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Snap To Ground")) SnapToGround(generator);
                if (GUILayout.Button("Save Mesh Asset...")) SaveMeshAsset(generator);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build the rest of it", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Crater Lava")) VolcanoDressing.AddCraterLava(generator);
            if (GUILayout.Button("Add Spillway Rivers")) VolcanoDressing.AddSpillwayRivers(generator);
            if (GUILayout.Button("Add Smoke And Mist")) VolcanoDressing.AddSmokeAndMist(generator);
            if (GUILayout.Button("Add Passage Lights")) VolcanoDressing.AddPassageLights(generator);

            DrawRiverTools(generator);

            EditorGUILayout.Space();
            if (GUILayout.Button("Build Everything"))
            {
                VolcanoDressing.AddCraterLava(generator);
                VolcanoDressing.AddSpillwayRivers(generator);
                VolcanoDressing.AddSmokeAndMist(generator);
                VolcanoDressing.AddPassageLights(generator);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 rock, 1 ash, 2 ember, 3 molten. Assign four materials on " +
                "the Mesh Renderer in that order. Slot 3 is the only one that wants an emissive " +
                "material; it is the fissures, the notch floors and the seam in the passage.",
                MessageType.None);
        }

        /// <summary>
        /// The rivers, and what state each one's route is in. Which river is which and whether the
        /// volcano is still allowed to move it is exactly the thing that was invisible before, so it
        /// is spelled out rather than left to be discovered by pressing a button and losing work.
        /// </summary>
        static void DrawRiverTools(VolcanoGenerator generator)
        {
            var rivers = generator.GetComponentsInChildren<VolcanoRiver>(true);
            if (rivers.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rivers", EditorStyles.boldLabel);

            int edited = 0;
            foreach (VolcanoRiver river in rivers)
            {
                var flow = river.GetComponent<LavaFlow.LavaFlowGenerator>();
                if (flow == null) continue;

                bool wasEdited = river.RouteWasEdited;
                if (wasEdited) edited++;

                float length = flow.Path != null && flow.Path.IsValid ? flow.Path.Length : 0f;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        river.name,
                        length.ToString("F0") + " m, " + flow.Settings.waypoints.Count + " waypoints" +
                        (wasEdited ? "  (yours)" : "  (generated)"));

                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        Selection.activeGameObject = river.gameObject;
                }
            }

            EditorGUILayout.HelpBox(
                "To take a river somewhere else on the map, select it and drag its waypoints in the " +
                "scene view, or click the small dots between them to insert new ones. Once a route " +
                "has been moved it is marked \"yours\" and the buttons above stop rewriting it, " +
                "though they still refresh widths, look and the barrier.\n\n" +
                "Run Out Length under Rivers is the quick way to send them further out before you " +
                "start dragging.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Kart Barriers"))
                {
                    foreach (VolcanoRiver river in rivers) river.RebuildBarrier(generator.Rivers);
                }

                using (new EditorGUI.DisabledScope(edited == 0))
                {
                    if (GUILayout.Button("Re-route Rivers From Spillways") &&
                        EditorUtility.DisplayDialog(
                            "Re-route rivers",
                            edited + " river route(s) have been edited by hand. Putting them back in " +
                            "their spillway channels will discard those routes.",
                            "Re-route", "Cancel"))
                    {
                        VolcanoDressing.AddSpillwayRivers(generator, true);
                    }
                }
            }
        }

        static void DrawStats(VolcanoGenerator generator)
        {
            Mesh mesh = generator.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)(mesh.GetIndexCount(i) / 3);

            VolcanoSettings s = generator.Settings;
            VolcanoShape shape = generator.Shape;

            EditorGUILayout.LabelField("Triangles", tris.ToString("N0") + "   (" +
                                       mesh.vertexCount.ToString("N0") + " vertices)");
            EditorGUILayout.LabelField("Footprint", (shape.OuterRadius * 2f).ToString("F0") + " m across");
            EditorGUILayout.LabelField("Summit", s.height.ToString("F0") + " m above the foot");
            EditorGUILayout.LabelField("Lava pool", generator.PoolRadius().ToString("F1") +
                                       " m radius at " + s.LavaLevel.ToString("F1") + " m");

            if (shape.HasPassage)
            {
                Vector3 a, b, da, db;
                if (generator.TryGetPortalWorld(0, out a, out da) &&
                    generator.TryGetPortalWorld(1, out b, out db))
                {
                    EditorGUILayout.LabelField("Passage", (b - a).magnitude.ToString("F0") + " m long, " +
                                               s.boreWidth.ToString("F0") + " m wide, " +
                                               s.boreHeight.ToString("F0") + " m to the crown");
                }
            }
        }

        static void DrawWarnings(VolcanoGenerator generator)
        {
            VolcanoSettings s = generator.Settings;
            VolcanoShape shape = generator.Shape;

            // The single most common way to build a volcano that does nothing: a rim notched down
            // to somewhere the lava never reaches.
            if (s.spillwayCount > 0 && s.notchDrop <= s.lavaDepthBelowRim)
            {
                EditorGUILayout.HelpBox(
                    "The spillway notches are cut to " + s.NotchLevel.ToString("F1") +
                    " m and the lava stands at " + s.LavaLevel.ToString("F1") +
                    " m, so nothing can pour out of them. Raise Notch Drop above Lava Depth Below Rim.",
                    MessageType.Warning);
            }

            for (int i = 0; i < shape.SpillwayCount; i++)
            {
                if (!shape.SpillwayHitsPassage(i)) continue;
                EditorGUILayout.HelpBox(
                    "Spillway " + i + " runs down onto a passage mouth, so a river of lava lands on " +
                    "the road. Move it with Spillway Angle, or turn the passage with Bore Yaw.",
                    MessageType.Warning);
            }

            if (s.passage != PassageMode.None)
            {
                float from, to;
                if (!shape.TryGetBoreSpan(out from, out to))
                {
                    EditorGUILayout.HelpBox(
                        "The passage misses the mountain completely. Bore Offset is larger than the " +
                        "cone is wide at that height.", MessageType.Warning);
                }
                else if (s.boreFloorHeight + s.boreHeight > s.height * 0.6f)
                {
                    EditorGUILayout.HelpBox(
                        "The passage reaches most of the way up the cone, so it is more of a cutting " +
                        "than a tunnel. Lower Bore Height or raise the mountain.", MessageType.Info);
                }
            }

            var renderer = generator.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterials.Length != 4)
            {
                EditorGUILayout.HelpBox(
                    "The Mesh Renderer has " + renderer.sharedMaterials.Length +
                    " materials and the mesh has 4 submeshes. Use the button below to fill them in.",
                    MessageType.Warning);

                if (GUILayout.Button("Assign Default Materials"))
                {
                    Undo.RecordObject(renderer, "Assign Volcano Materials");
                    renderer.sharedMaterials = VolcanoMaterials.EnsureSurfaceMaterials();
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (generator.GetComponent<MeshCollider>() == null)
            {
                EditorGUILayout.HelpBox(
                    "No Mesh Collider. Nothing can drive through the passage or stand on the mountain.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Drops the mountain onto whatever is under it. The object's origin is the middle of the
        /// foot of the cone, so this puts the passage floor level with the surrounding ground.
        /// </summary>
        static void SnapToGround(VolcanoGenerator generator)
        {
            Transform t = generator.transform;
            float y;

            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                y = terrain.SampleHeight(t.position) + terrain.transform.position.y;
            }
            else
            {
                RaycastHit hit;
                if (!Physics.Raycast(t.position + Vector3.up * 500f, Vector3.down, out hit, 2000f))
                {
                    Debug.LogWarning("Nothing under the volcano to snap it to.", generator);
                    return;
                }
                y = hit.point.y;
            }

            Undo.RecordObject(t, "Snap Volcano To Ground");
            t.position = new Vector3(t.position.x, y, t.position.z);
            generator.Generate();
        }

        static void SaveMeshAsset(VolcanoGenerator generator)
        {
            if (generator.Mesh == null) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Volcano Mesh", generator.Mesh.name, "asset",
                "Bake the current volcano into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(generator.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved volcano mesh to " + path, copy);
        }
    }

    /// <summary>
    /// Adds the volcano to the GameObject creation menu with its materials already wired up. The
    /// materials are written on first use rather than shipped, so the set always matches whichever
    /// render pipeline the project is actually on.
    /// </summary>
    public static class VolcanoMenu
    {
        [MenuItem("GameObject/3D Object/Volcano (Low Poly)", false, 13)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Volcano");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            var generator = go.AddComponent<VolcanoGenerator>();

            renderer.sharedMaterials = VolcanoMaterials.EnsureSurfaceMaterials();
            generator.Generate();

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }

    /// <summary>
    /// The material set, written into the project on first use. Kept in one place because the
    /// dressing, the menu item and the inspector's repair button all need the same materials and
    /// must not each invent their own.
    /// </summary>
    public static class VolcanoMaterials
    {
        const string RootFolder = "Assets/Volcano";
        const string MaterialFolder = RootFolder + "/Materials";

        /// <summary>The four surface materials, in submesh order: rock, ash, ember, molten.</summary>
        public static Material[] EnsureSurfaceMaterials()
        {
            EnsureFolders();

            // Albedo is lighter than volcanic rock really is, because under a night rig it gets
            // multiplied by a dim light and physically dark basalt renders black, taking the facets
            // with it. But only a little lighter: measured against the ground it stands on, whose
            // basalt layer is 0.145 and ash 0.266 in sRGB. Twice those values reads as a snowy
            // mountain dropped into a lava field, which is what the first pass looked like.
            //
            // Emission keeps red just over 1 and the other two well under. Push more than one
            // channel past 1 and they both clip to full, which turns the glow yellow and then white
            // however orange the base colour is.
            return new[]
            {
                Lit("VLC_Rock",   new Color(0.155f, 0.150f, 0.170f), Color.black, 0.10f),
                Lit("VLC_Ash",    new Color(0.215f, 0.190f, 0.180f), Color.black, 0.06f),
                Lit("VLC_Ember",  new Color(0.260f, 0.130f, 0.090f), new Color(0.42f, 0.07f, 0.01f), 0.14f),
                Lit("VLC_Molten", new Color(0.850f, 0.280f, 0.030f), new Color(1.5f, 0.42f, 0.05f), 0.35f)
            };
        }

        public static Material SmokeMaterial() { return Particle("VLC_Smoke", false); }
        public static Material MistMaterial() { return Particle("VLC_Mist", false); }
        public static Material EmberMaterial() { return Particle("VLC_Ember_Particle", true); }

        /// <summary>Loads a lava material from an existing package, or falls back to one of ours.</summary>
        public static Material[] LavaFlowMaterials()
        {
            return new[]
            {
                LoadOr("Assets/LavaFlow/Materials/LF_Crust_Dark.mat", "VLC_Rock"),
                LoadOr("Assets/LavaFlow/Materials/LF_Crust_Warm.mat", "VLC_Ember"),
                RiverLavaMaterial(),
                LoadOr("Assets/LavaFlow/Materials/LF_Rock.mat", "VLC_Rock")
            };
        }

        /// <summary>
        /// The Lava Flow package's scrolling molten shader, retuned for the cascades down the cone.
        ///
        /// A separate material rather than a retune of LF_Molten, which belongs to that package and
        /// may be feeding flows elsewhere. What is retuned: the shipped white-hot colour is
        /// (4.5, 1.5, 0.25), and two channels over 1 both clip to full, so the hottest lava comes
        /// out yellow however orange the ramp under it is. On gentle terrain that barely shows,
        /// because most of the surface has crusted over. On a cascade almost nothing crusts, so the
        /// white-hot band is most of what you see and the whole river reads as gold.
        /// </summary>
        public static Material RiverLavaMaterial()
        {
            EnsureFolders();

            const string path = MaterialFolder + "/VLC_River_Lava.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("LavaFlow/Molten Lava");
            if (shader == null) return LoadOr("Assets/LavaFlow/Materials/LF_Molten.mat", "VLC_Molten");

            var material = new Material(shader) { name = "VLC_River_Lava" };
            material.SetColor("_DeepColor", new Color(0.42f, 0.040f, 0.007f));
            material.SetColor("_HotColor", new Color(2.00f, 0.360f, 0.040f));
            material.SetColor("_WhiteHot", new Color(3.20f, 0.950f, 0.150f));
            material.SetFloat("_EmissionBoost", 0.85f);
            ApplyRiverLook(material, new VolcanoRiverSettings());

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Writes the volcano's river look onto the material.
        ///
        /// How fast lava appears to move is a property of the shader, not of the mesh, so there is
        /// no way to expose "slower" on the generator without reaching the material from here. This
        /// only ever touches the properties the volcano exposes; colours and emission stay wherever
        /// they were set, including by hand.
        /// </summary>
        public static void ApplyRiverLook(Material material, VolcanoRiverSettings r)
        {
            if (material == null || r == null) return;
            if (!material.HasProperty("_FlowSpeed")) return;

            Undo.RecordObject(material, "Retune River Lava");
            material.SetFloat("_FlowSpeed", r.flowSpeed);
            if (material.HasProperty("_NoiseScale")) material.SetFloat("_NoiseScale", r.patternScale);
            if (material.HasProperty("_WarpStrength")) material.SetFloat("_WarpStrength", r.swirl);
            if (material.HasProperty("_StretchAlongFlow"))
                material.SetFloat("_StretchAlongFlow", r.stretchAlongFlow);
            if (material.HasProperty("_CrustAmount")) material.SetFloat("_CrustAmount", r.moltenCrust);

            // The banks carry extra skin on top of whatever the channel has, and the crust response
            // is steep enough that this needs a low ceiling: measured over a real flow, another
            // 0.35 on top of the channel's own setting takes the bank from open lava to solid black
            // crust. 0.18 is as far as it can go and still read as a margin rather than a wall.
            if (material.HasProperty("_BankCrust"))
                material.SetFloat("_BankCrust", Mathf.Min(0.18f, r.moltenCrust * 0.9f));

            // The edge of the shader crust is the single biggest source of "ripply". It is a
            // smoothstep across a noise field, so a narrow edge turns every wiggle in that noise
            // into a hard black fringe, and on a channel seen at a glancing angle those fringes
            // stack into ripples. Widening it with the crust means a low setting fades out instead
            // of breaking up. Measured by turning the shader crust off entirely: the ripples went
            // with it and the geometry plates alone read fine.
            if (material.HasProperty("_CrustSharpness"))
                material.SetFloat("_CrustSharpness", Mathf.Lerp(0.4f, 0.13f, Mathf.Clamp01(r.moltenCrust)));

            EditorUtility.SetDirty(material);
        }

        public static Material[] LavaPondMaterials()
        {
            return new[]
            {
                LoadOr("Assets/LavaPond/Materials/LP_Crust_Dark.mat", "VLC_Rock"),
                LoadOr("Assets/LavaPond/Materials/LP_Crust_Warm.mat", "VLC_Ember"),
                LoadOr("Assets/LavaPond/Materials/LP_Molten.mat", "VLC_Molten"),
                LoadOr("Assets/LavaPond/Materials/LP_Rock.mat", "VLC_Rock")
            };
        }

        static Material LoadOr(string path, string fallbackName)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Material[] ours = EnsureSurfaceMaterials();
            foreach (Material m in ours)
                if (m != null && m.name == fallbackName) return m;
            return ours.Length > 0 ? ours[0] : null;
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "Volcano");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");
        }

        static Material Lit(string name, Color baseColor, Color emission, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);

                // Deliberately kept out of global illumination. A mountain-sized emitter that
                // bounces light turns everything standing near it orange whether or not that is
                // wanted; the point lights do the lighting.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// A textureless transparent particle material. No texture is the point: the puffs are
        /// geometry, and a cloud texture on top of them would put photographic detail on the one
        /// thing in the scene that is supposed to read as facets.
        /// </summary>
        static Material Particle(string name, bool additive)
        {
            EnsureFolders();

            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // Written out by hand because the material is being created from script: the shader's
            // own inspector is what normally sets these, and it never runs here.
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0f); // multiply

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }

    /// <summary>
    /// Everything that hangs off the mountain: the lava standing in the crater, the rivers running
    /// out of the spillways, the smoke, the mist, and the lights in the passage.
    ///
    /// Each of these is rebuilt in place rather than added again, so pressing a button twice tidies
    /// up rather than piling up a second copy.
    /// </summary>
    public static class VolcanoDressing
    {
        /// <summary>A Lava Pond sitting in the crater with its vent up, which is the source.</summary>
        public static void AddCraterLava(VolcanoGenerator volcano)
        {
            Vector3 center;
            float radius;
            if (!volcano.TryGetCraterLava(out center, out radius) || radius < 0.5f)
            {
                Debug.LogWarning("The crater holds no lava at the current lava level.", volcano);
                return;
            }

            GameObject go = Child(volcano.transform, "Crater Lava");
            go.transform.position = center;
            go.transform.rotation = volcano.transform.rotation;

            Ensure<MeshFilter>(go);
            var renderer = Ensure<MeshRenderer>(go);
            var pond = Ensure<LavaPond.LavaPondGenerator>(go);

            LavaPond.LavaPondSettings s = pond.Settings;
            s.radius = radius;
            s.shoreIrregularity = 0.16f;
            s.angularSegments = 42;
            s.radialRings = 9;
            s.plateCount = 22;
            s.crustCoverage = 0.55f;        // less crust than a cooling pond: this one is live
            s.warmCrustRatio = 0.45f;
            s.crackWidth = 0.3f;
            s.rimWidth = 0f;                // the crater wall is already the rim
            s.rimHeight = 0f;
            s.depth = 2.5f;
            s.vent = true;                  // the spatter cone that is visibly feeding everything
            s.ventRadius = Mathf.Clamp(radius * 0.22f, 1f, 8f);
            s.ventHeight = Mathf.Clamp(radius * 0.16f, 0.8f, 6f);
            s.ventIrregularity = 0.4f;
            s.bubbleCount = 26;
            s.bubbleSize = Mathf.Clamp(radius * 0.09f, 0.4f, 2.5f);
            s.rockCount = 10;
            s.uvMode = LavaPond.PondUVMode.WorldPlanar;

            renderer.sharedMaterials = VolcanoMaterials.LavaPondMaterials();
            pond.Generate();

            EditorUtility.SetDirty(pond);
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// One Lava Flow per spillway, routed down the channel that was cut for it rather than
        /// released at the top and left to find its own way. The channel is real geometry, so a
        /// downhill solve would probably follow it, but "probably" is not a thing to build a set
        /// piece on: the waypoints come straight off the same maths that cut the channel.
        ///
        /// Past the foot of the mountain the route is only a starting suggestion. Drag the waypoints
        /// anywhere on the map and this stops rewriting them: <see cref="VolcanoRiver"/> keeps a
        /// signature of what was generated, and a route that no longer matches it belongs to whoever
        /// moved it. Everything else — widths, look, materials, the barrier — is still refreshed, so
        /// retuning the volcano still reaches a hand-drawn river.
        /// </summary>
        public static void AddSpillwayRivers(VolcanoGenerator volcano)
        {
            AddSpillwayRivers(volcano, false);
        }

        /// <param name="forceReroute">
        /// Throw away hand-drawn routes and put every river back in its spillway channel.
        /// </param>
        public static void AddSpillwayRivers(VolcanoGenerator volcano, bool forceReroute)
        {
            VolcanoShape shape = volcano.Shape;
            if (shape.SpillwayCount == 0)
            {
                Debug.LogWarning("No spillways to run rivers out of.", volcano);
                return;
            }

            Transform group = Child(volcano.transform, "Lava Rivers").transform;
            VolcanoRiverSettings r = volcano.Rivers;

            Material[] materials = VolcanoMaterials.LavaFlowMaterials();
            VolcanoMaterials.ApplyRiverLook(VolcanoMaterials.RiverLavaMaterial(), r);

            int kept = 0;

            for (int i = 0; i < shape.SpillwayCount; i++)
            {
                GameObject go = Child(group, "Lava River " + (i + 1));

                var renderer = Ensure<MeshRenderer>(go);
                Ensure<MeshFilter>(go);
                var flow = Ensure<LavaFlow.LavaFlowGenerator>(go);
                var river = Ensure<VolcanoRiver>(go);
                river.SpillwayIndex = i;

                LavaFlow.LavaFlowSettings s = flow.Settings;
                s.pathMode = LavaFlow.FlowPathMode.Waypoints;

                bool rewriteRoute = forceReroute || !river.RouteWasEdited;
                if (rewriteRoute)
                {
                    List<Vector3> route = BuildRiverRoute(volcano, i, r);
                    if (route.Count < 3) continue;

                    go.transform.position = route[0];
                    go.transform.rotation = Quaternion.LookRotation(
                        Flat(route[1] - route[0]), Vector3.up);

                    s.waypoints.Clear();
                    for (int k = 1; k < route.Count; k++)
                        s.waypoints.Add(go.transform.InverseTransformPoint(route[k]));
                }
                else
                {
                    kept++;
                }

                ApplyRiverSettings(s, r);

                renderer.sharedMaterials = materials;

                // Waypoints are draped onto whatever is under them, and what is under them is the
                // mountain, not the terrain. Terrain mode would sink the whole river into the
                // ground the mountain is standing on.
                //
                // The flow's own collider stays off: the mesh is a ribbon lying about a metre off
                // the ground, so a kart drives up onto it rather than being stopped by it. Blocking
                // is the barrier's job, and VolcanoRiver builds that.
                var so = new SerializedObject(flow);
                SetEnum(so, "groundMode", (int)LavaFlow.GroundMode.Raycast);
                SetInt(so, "groundLayers", ~0);
                SetBool(so, "updateCollider", false);
                so.ApplyModifiedPropertiesWithoutUndo();

                flow.Generate();
                if (rewriteRoute) river.CaptureRoute();
                river.RebuildBarrier(r);

                EditorUtility.SetDirty(flow);
                EditorUtility.SetDirty(river);
                EditorUtility.SetDirty(renderer);
            }

            if (kept > 0)
            {
                Debug.Log(kept + " river route(s) left alone because they have been edited by hand. " +
                          "Use \"Re-route Rivers From Spillways\" to put them back in their channels.",
                          volcano);
            }
        }

        /// <summary>
        /// Everything about a river that is not its route. Split out because it runs whether or not
        /// the route was rewritten: retuning the volcano has to reach a hand-drawn river too.
        /// </summary>
        static void ApplyRiverSettings(LavaFlow.LavaFlowSettings s, VolcanoRiverSettings r)
        {
            s.stationSpacing = r.stationSpacing;
            s.cascadeWidth = r.cascadeWidth;
            s.riverWidth = r.riverWidth;
            s.steepAngle = 30f;
            s.groundFollow = r.groundFollow;

            // The mountain is built out of large flat faces, so a flow hugging it needs more
            // clearance and a deeper skirt than one draped over smooth terrain, or the facet
            // edges cut through it.
            s.surfaceOffset = 0.25f;
            s.skirtDepth = 2.2f;
            s.leveeHeight = 0.9f;
            s.channelDepth = 0.5f;
            // Not the package default of 0.12: a cascade with almost no crust is one unbroken
            // glowing band down the cone, and the plates are what give it any sense of scale.
            s.crustCoverageCascade = 0.25f;
            s.crustCoverageRiver = 0.78f;
            s.uvMode = LavaFlow.FlowUVMode.FlowAligned;
            s.cascadeSpeedBoost = r.cascadeSpeedBoost;

            // One knob over everything that makes the surface uneven. The package defaults are tuned
            // for a slow field of `a'a spreading over rough ground, where a churned, broken surface
            // is the point; a pour down a volcano wants the other end of the same range. Lerping
            // from a smooth floor rather than scaling the defaults means 0 is genuinely smooth
            // instead of merely quieter.
            float ripple = Mathf.Clamp01(r.surfaceRipple);
            s.moltenTurbulence = Mathf.Lerp(0.02f, 0.9f, ripple);
            s.ridgeHeight = Mathf.Lerp(0.02f, 0.9f, ripple);
            s.plateHeightVariation = Mathf.Lerp(0.01f, 0.4f, ripple);
            s.widthVariation = Mathf.Lerp(0.03f, 0.5f, ripple);
            s.leveeRoughness = Mathf.Lerp(0.1f, 0.9f, ripple);
            s.slabHeight = Mathf.Lerp(0.15f, 1.4f, ripple);
            s.slabCount = Mathf.RoundToInt(Mathf.Lerp(4f, 60f, ripple));
            s.bubbleCount = Mathf.RoundToInt(Mathf.Lerp(3f, 45f, ripple));

            // Ridges are the arcs of buckled crust, and they are the cue that says which way the
            // river is going. Spreading them out as the surface smooths keeps a few long, low swells
            // rather than fading them into nothing.
            s.ridgeSpacing = Mathf.Lerp(26f, 9f, ripple);
        }

        /// <summary>
        /// The channel route in world space, carried on past the foot of the mountain and out over
        /// whatever is around it so the river runs away rather than stopping at the skirt.
        ///
        /// The run-out is deliberately coarse: these are the points you drag to take a river
        /// somewhere else on the map, and a route sampled every couple of metres is impossible to
        /// author with. The flow re-samples at its own station spacing anyway.
        /// </summary>
        static List<Vector3> BuildRiverRoute(VolcanoGenerator volcano, int spillway,
                                             VolcanoRiverSettings r)
        {
            VolcanoShape shape = volcano.Shape;
            VolcanoSettings s = volcano.Settings;

            List<Vector3> route = volcano.SpillwayRouteWorld(spillway, s.baseRadius + s.skirtWidth * 0.35f, 5f);

            if (r.runOutLength <= 0.01f) return route;

            // Out onto the ground. Heights come from what is actually there, because past the foot
            // the volcano's own height field is only the buried skirt.
            float heading = shape.SpillwayHeading(spillway);
            Vector3 outward = volcano.transform.TransformDirection(
                new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading))).normalized;

            Vector3 last = route[route.Count - 1];
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;

            float step = Mathf.Max(2f, r.runOutStep);
            float wavelength = Mathf.Max(5f, r.meanderLength);

            for (float d = step; d <= r.runOutLength; d += step)
            {
                // A radial line is what a channel down a cone gives you, and out on the flat it
                // reads as a canal. The swing eases in from the foot so the join stays straight
                // where the channel is still doing the steering.
                float swing = Mathf.Sin(d * Mathf.PI * 2f / wavelength + spillway * 2.1f)
                              * r.meanderAmplitude * Mathf.Clamp01(d / 25f);

                Vector3 p = last + outward * d + side * swing;
                route.Add(new Vector3(p.x, GroundHeight(p), p.z));
            }

            return route;
        }

        static float GroundHeight(Vector3 world)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (world.x >= origin.x && world.x <= origin.x + size.x &&
                    world.z >= origin.z && world.z <= origin.z + size.z)
                    return terrain.SampleHeight(world) + origin.y;
            }

            RaycastHit hit;
            if (Physics.Raycast(world + Vector3.up * 400f, Vector3.down, out hit, 2000f))
                return hit.point.y;

            return world.y;
        }

        /// <summary>Smoke out of the crater, embers with it, and mist off every piece of lava.</summary>
        public static void AddSmokeAndMist(VolcanoGenerator volcano)
        {
            Vector3 craterCentre;
            float poolRadius;
            volcano.TryGetCraterLava(out craterCentre, out poolRadius);
            poolRadius = Mathf.Max(2f, poolRadius);

            Transform group = Child(volcano.transform, "Volcano Effects").transform;

            // --- the column out of the crater -------------------------------------------------
            GameObject plume = Child(group, "Crater Smoke");
            plume.transform.position = craterCentre;
            var smoke = ConfigureSmoke(plume, PlumeStyle.Smoke, VolcanoMaterials.SmokeMaterial());
            smoke.radius = poolRadius * 0.95f;
            smoke.rate = 7f;
            smoke.riseSpeed = Mathf.Max(5f, volcano.Settings.height * 0.13f);
            smoke.lifetime = 16f;
            smoke.startSize = poolRadius * 0.62f;
            smoke.growth = 3.6f;
            smoke.drift = 2.4f;
            smoke.turbulence = 1.7f;
            smoke.Rebuild();

            GameObject embers = Child(group, "Crater Embers");
            embers.transform.position = craterCentre;
            var ember = ConfigureSmoke(embers, PlumeStyle.Embers, VolcanoMaterials.EmberMaterial());
            ember.radius = poolRadius * 0.75f;
            ember.rate = 22f;
            ember.riseSpeed = Mathf.Max(9f, volcano.Settings.height * 0.30f);
            ember.lifetime = 4.5f;
            ember.startSize = 0.7f;
            ember.growth = 1f;
            ember.drift = 1.4f;
            ember.turbulence = 2.4f;
            ember.Rebuild();

            // --- mist off everything that is molten -------------------------------------------
            Material mistMaterial = VolcanoMaterials.MistMaterial();

            // Rates are low on purpose. A few big slow wisps cost almost nothing and read as a fog
            // bank; turning the rate up does not thicken the fog so much as replace the mountain
            // with it, because every wisp is another translucent solid drawn over the last one.
            var pond = volcano.GetComponentInChildren<LavaPond.LavaPondGenerator>();
            if (pond != null)
            {
                AddMist(group, "Crater Mist", pond.GetComponent<MeshRenderer>(), 2, mistMaterial,
                        poolRadius * 0.5f, 7f, 0.9f);
            }

            var flows = volcano.GetComponentsInChildren<LavaFlow.LavaFlowGenerator>();
            for (int i = 0; i < flows.Length; i++)
            {
                AddMist(group, "River Mist " + (i + 1), flows[i].GetComponent<MeshRenderer>(), 2,
                        mistMaterial, 9f, 9f, 0.85f);
            }

            // The mountain's own molten slot: the fissures near the summit and the seam in the
            // passage. Thin, but it is what stops those reading as painted-on stripes.
            AddMist(group, "Fissure Mist", volcano.GetComponent<MeshRenderer>(), 3, mistMaterial,
                    3f, 4f, 0.5f);

        }

        static VolcanoSmoke ConfigureSmoke(GameObject go, PlumeStyle style, Material material)
        {
            Ensure<ParticleSystem>(go);
            var smoke = Ensure<VolcanoSmoke>(go);

            smoke.style = style;
            smoke.material = material;
            smoke.ApplyDefaultTint();

            EditorUtility.SetDirty(smoke);
            return smoke;
        }

        static void AddMist(Transform parent, string name, MeshRenderer source, int submesh,
                            Material material, float width, float rate, float rise)
        {
            if (source == null) return;

            GameObject go = Child(parent, name);

            // Sat exactly on the source's own transform. The shape module places particles from the
            // mesh, and lining the two transforms up means local and world agree however Unity
            // chooses to compose them.
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = Vector3.one;

            Ensure<ParticleSystem>(go);
            var mist = Ensure<LavaMist>(go);

            mist.source = source;
            mist.material = material;
            mist.ApplyDefaultTint();
            mist.moltenSubmesh = submesh;
            mist.width = width;
            mist.rate = rate;
            mist.rise = rise;
            mist.growth = 1.7f;
            mist.Rebuild();

            EditorUtility.SetDirty(mist);
        }

        /// <summary>
        /// Warm point lights down the passage. The seam in the wall is emissive but emission does
        /// not light anything, so without these the tunnel is a black hole in the middle of the map.
        /// </summary>
        public static void AddPassageLights(VolcanoGenerator volcano)
        {
            if (volcano.Settings.passage == PassageMode.None) return;

            Vector3 a, b, da, db;
            if (!volcano.TryGetPortalWorld(0, out a, out da) ||
                !volcano.TryGetPortalWorld(1, out b, out db)) return;

            Transform group = Child(volcano.transform, "Passage Glow").transform;
            for (int i = group.childCount - 1; i >= 0; i--) Object.DestroyImmediate(group.GetChild(i).gameObject);

            float length = (b - a).magnitude;
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 22f), 2, 20);
            float lift = Mathf.Max(1.5f, volcano.Settings.boreHeight * 0.35f);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                var go = new GameObject("Passage Light " + (i + 1));
                go.transform.SetParent(group, false);
                go.transform.position = Vector3.Lerp(a, b, t) + Vector3.up * lift;

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.52f, 0.20f);
                light.intensity = 3.2f;
                light.range = 34f;
                light.shadows = LightShadows.None;
            }

        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Finds the child by name or makes it. Reusing it is what lets every button here be
        /// pressed twice without stacking up a second copy of everything, and registering the undo
        /// only on the press that actually created something keeps the undo stack honest.
        /// </summary>
        static GameObject Child(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Add " + name);
            return go;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // The Lava Flow generator keeps its ground mode private, which is right: it is a component
        // setting rather than part of the flow's settings object. Reaching it through the
        // serialised object is the supported way in, and beats loosening the API for one caller.
        static void SetEnum(SerializedObject so, string path, int value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.enumValueIndex = value;
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
        }
    }
}
