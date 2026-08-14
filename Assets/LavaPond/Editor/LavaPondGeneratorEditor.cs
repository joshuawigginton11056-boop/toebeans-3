using UnityEditor;
using UnityEngine;

namespace LavaPond.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="LavaPondGenerator"/>: live stats, a regenerate/randomise pair, and
    /// a bake button for turning the current pond into a mesh asset.
    /// </summary>
    [CustomEditor(typeof(LavaPondGenerator))]
    [CanEditMultipleObjects]
    public class LavaPondGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (LavaPondGenerator)target;

            EditorGUILayout.Space();
            DrawStats(generator);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate"))
                {
                    foreach (Object t in targets) ((LavaPondGenerator)t).Generate();
                }

                if (GUILayout.Button("Randomise Seed"))
                {
                    foreach (Object t in targets)
                    {
                        var g = (LavaPondGenerator)t;
                        Undo.RecordObject(g, "Randomise Lava Pond");
                        g.Randomize();
                        EditorUtility.SetDirty(g);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(targets.Length != 1 || generator.Mesh == null))
            {
                if (GUILayout.Button("Save Mesh Asset..."))
                    SaveMeshAsset(generator);
            }

            EditorGUILayout.Space();
            DrawEffects(generator);

            EditorGUILayout.Space();
            DrawShaderCrust(generator);

            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 dark crust, 1 warm crust, 2 molten lava, 3 rock. " +
                "Assign four materials on the Mesh Renderer in that order. Slot 2 is the one that " +
                "wants an emissive or scrolling lava shader; the rest are ordinary rock.",
                MessageType.None);
        }

        static void DrawStats(LavaPondGenerator generator)
        {
            Mesh mesh = generator.Mesh;
            if (mesh == null)
            {
                EditorGUILayout.LabelField("Mesh", "not generated yet");
                return;
            }

            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                tris += (int)(mesh.GetIndexCount(i) / 3);

            EditorGUILayout.LabelField("Triangles", tris.ToString("N0"));
            EditorGUILayout.LabelField("Vertices", mesh.vertexCount.ToString("N0"));
            Vector3 size = mesh.bounds.size;
            float scale = Mathf.Max(Mathf.Abs(generator.transform.lossyScale.x),
                                    Mathf.Abs(generator.transform.lossyScale.z));
            EditorGUILayout.LabelField("Size",
                string.Format("{0:F1} x {1:F1} x {2:F1} m", size.x * scale, size.y * scale, size.z * scale));
            // The two crust knobs pull against each other, so the number they add up to is worth
            // showing rather than leaving to be judged by eye through a crack network.
            float crust = generator.CrustCoverage * 100f;
            EditorGUILayout.LabelField("Crust cover", crust.ToString("F0") + "% skinned over, " +
                                       (100f - crust).ToString("F0") + "% open lava");
        }

        /// <summary>
        /// The particles that hang off the pond. Each button builds its effect in place, so pressing
        /// one twice retunes what is there rather than stacking a second copy on top of it.
        /// </summary>
        void DrawEffects(LavaPondGenerator generator)
        {
            EditorGUILayout.LabelField("Smoke and steam", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEffectButton(generator, PondEffect.HeatHaze);
                DrawEffectButton(generator, PondEffect.SteamBank);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEffectButton(generator, PondEffect.SmokeColumn);
                DrawEffectButton(generator, PondEffect.Embers);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add All Four"))
                {
                    foreach (Object t in targets)
                    {
                        var g = (LavaPondGenerator)t;
                        LavaPondEffects.Add(g, PondEffect.HeatHaze);
                        LavaPondEffects.Add(g, PondEffect.SteamBank);
                        LavaPondEffects.Add(g, PondEffect.SmokeColumn);
                        LavaPondEffects.Add(g, PondEffect.Embers);
                    }
                }

                if (GUILayout.Button("Remove Effects"))
                {
                    foreach (Object t in targets) LavaPondEffects.RemoveAll((LavaPondGenerator)t);
                }
            }

            EditorGUILayout.HelpBox(
                "Heat Haze is a thin shimmer on the lava, Steam Bank is the slow billowing one, " +
                "Smoke Column climbs off the pond (out of the vent, if it has one) and Embers spits " +
                "sparks. They stack, and each one lands under \"Pond Effects\" with every setting " +
                "live on its own component.",
                MessageType.None);
        }

        void DrawEffectButton(LavaPondGenerator generator, PondEffect effect)
        {
            string label = LavaPondEffects.NameOf(effect);
            if (LavaPondEffects.Has(generator, effect)) label += "  ✓";

            if (!GUILayout.Button(label)) return;
            foreach (Object t in targets) LavaPondEffects.Add((LavaPondGenerator)t, effect);
        }

        /// <summary>
        /// The other crust, and the one that is easy to spend an afternoon looking for in the wrong
        /// place: the molten material can skin the lava over by itself, whatever the mesh is doing.
        /// Only shown when the material assigned actually has those knobs.
        /// </summary>
        static void DrawShaderCrust(LavaPondGenerator generator)
        {
            var renderer = generator.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Material[] materials = renderer.sharedMaterials;
            if (materials.Length < 3) return;

            Material molten = materials[2];
            if (molten == null || !molten.HasProperty("_CrustAmount")) return;

            float amount = molten.GetFloat("_CrustAmount");
            float bank = molten.HasProperty("_BankCrust") ? molten.GetFloat("_BankCrust") : 0f;
            if (amount <= 0.001f && bank <= 0.001f) return;

            EditorGUILayout.LabelField("Shader crust", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                molten.name + " skins the lava over on top of whatever the mesh does: Crust Amount " +
                amount.ToString("F2") + " over the whole surface, Extra Crust At Banks " +
                bank.ToString("F2") + " ringing the shore. If the pond still looks crusted with the " +
                "plate settings turned right down, it is these.",
                MessageType.None);

            if (GUILayout.Button("Turn Shader Crust Off (" + molten.name + ")"))
            {
                Undo.RecordObject(molten, "Turn Shader Crust Off");
                molten.SetFloat("_CrustAmount", 0f);
                if (molten.HasProperty("_BankCrust")) molten.SetFloat("_BankCrust", 0f);
                EditorUtility.SetDirty(molten);
            }
        }

        static void SaveMeshAsset(LavaPondGenerator generator)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Lava Pond Mesh", generator.Mesh.name, "asset",
                "Bake the current pond into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(generator.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved lava pond mesh to " + path, copy);
        }
    }

    /// <summary>
    /// Adds the pond to the GameObject creation menu with its materials already wired up. The
    /// materials are written on first use rather than shipped, so the set always matches whichever
    /// render pipeline the project is actually on.
    /// </summary>
    public static class LavaPondMenu
    {
        const string RootFolder = "Assets/LavaPond";
        const string MaterialFolder = RootFolder + "/Materials";

        [MenuItem("GameObject/3D Object/Lava Pond (Low Poly)", false, 13)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Lava Pond");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<LavaPondGenerator>();

            renderer.sharedMaterials = EnsureMaterials();

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>Loads the four submesh materials, creating any that are not there yet.</summary>
        static Material[] EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "LavaPond");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");

            var materials = new Material[4];
            // Emission runs the red channel just over 1 so the lava still blooms, while green and
            // blue stay well under it. Push more than one channel past 1 and they both clip to full,
            // which turns the glow yellow and then white however orange the base colour is.
            materials[0] = Load("LP_Crust_Dark", new Color(0.09f, 0.08f, 0.09f), Color.black, 0.18f);
            materials[1] = Load("LP_Crust_Warm", new Color(0.24f, 0.10f, 0.06f),
                                new Color(0.55f, 0.12f, 0.02f), 0.22f);
            materials[2] = Load("LP_Molten", new Color(0.85f, 0.28f, 0.03f),
                                new Color(1.5f, 0.42f, 0.05f), 0.35f);
            materials[3] = Load("LP_Rock", new Color(0.26f, 0.24f, 0.23f), Color.black, 0.12f);
            return materials;
        }

        static Material Load(string name, Color baseColor, Color emission, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", emission);
                // Deliberately kept out of global illumination. A pond this size is a large emitter,
                // and letting it bounce light turns everything standing near it orange whether or
                // not that is wanted. Add a point light if the pond should actually light the scene.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
