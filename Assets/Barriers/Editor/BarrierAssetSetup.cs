using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Barriers.EditorTools
{
    /// <summary>
    /// Wires the generated barrier FBXs up into something droppable onto a <see cref="BarrierLine"/>:
    /// the shared materials, the importer remaps that point each model at them, and a prefab per
    /// section with a collider and a <see cref="BarrierTint"/> on it.
    ///
    /// Idempotent. Re-run it after re-exporting from Blender and it repairs whatever drifted rather
    /// than making a second copy of everything.
    /// </summary>
    public static class BarrierAssetSetup
    {
        const string ModelDir = "Assets/GeneratedModels";
        const string MaterialDir = "Assets/GeneratedModels/Materials";
        const string PrefabDir = "Assets/Prefabs/Barriers";

        struct MatSpec
        {
            public string Name;
            public Color Colour;
            public float Smoothness;
            public float Metallic;
            public bool Emissive;
        }

        /// <summary>Matches the palette the Blender build writes, so the FBX arrives looking right.</summary>
        static readonly MatSpec[] Materials =
        {
            new MatSpec { Name = "Barrier_Paint",    Colour = new Color(0.60f, 0.26f, 0.09f), Smoothness = 0.22f, Metallic = 0f },
            new MatSpec { Name = "Barrier_Accent",   Colour = new Color(0.72f, 0.70f, 0.64f), Smoothness = 0.30f, Metallic = 0f, Emissive = true },
            new MatSpec { Name = "Barrier_Metal",    Colour = new Color(0.20f, 0.21f, 0.24f), Smoothness = 0.38f, Metallic = 0.2f },
            new MatSpec { Name = "Barrier_Wood",     Colour = new Color(0.20f, 0.15f, 0.12f), Smoothness = 0.10f, Metallic = 0f },
            new MatSpec { Name = "Barrier_Rubber",   Colour = new Color(0.07f, 0.07f, 0.08f), Smoothness = 0.14f, Metallic = 0f },
            new MatSpec { Name = "Barrier_Concrete", Colour = new Color(0.24f, 0.24f, 0.27f), Smoothness = 0.10f, Metallic = 0f },
        };

        static readonly string[] Sections =
        {
            "Barrier_GuardRail",
            "Barrier_TyreWall",
            "Barrier_Timber",
            "Barrier_Jersey",
            "Barrier_MarkerPost",
        };

        [MenuItem("Tools/Barriers/Set Up Generated Barrier Assets")]
        public static void Run()
        {
            Dictionary<string, Material> mats = EnsureMaterials();
            EnsurePhysicsMaterial();

            int prefabs = 0;

            // Deliberately not inside StartAssetEditing: each remap has to reimport before the next
            // step can load the model, and a reimport inside a batched edit does not run.
            foreach (string section in Sections) RemapModel(section, mats);

            AssetDatabase.Refresh();

            foreach (string section in Sections)
                if (BuildPrefab(section)) prefabs++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Barriers: {mats.Count} materials ready, {prefabs} prefabs written to {PrefabDir}.");
        }

        // ------------------------------------------------------------------ materials

        static Dictionary<string, Material> EnsureMaterials()
        {
            EnsureFolder(MaterialDir);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("Barriers: URP Lit shader not found — is the render pipeline set up?");
                return new Dictionary<string, Material>();
            }

            var made = new Dictionary<string, Material>();

            foreach (MatSpec spec in Materials)
            {
                string path = $"{MaterialDir}/{spec.Name}.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = m == null;
                if (isNew)
                {
                    m = new Material(lit) { name = spec.Name };
                    AssetDatabase.CreateAsset(m, path);
                }

                // Only the look is (re)stamped. If the material already exists, its colour is left
                // alone — a re-run must not undo a colour the artist chose here.
                if (isNew)
                {
                    m.SetColor("_BaseColor", spec.Colour);
                    m.SetColor("_Color", spec.Colour);
                    m.SetFloat("_Smoothness", spec.Smoothness);
                    m.SetFloat("_Metallic", spec.Metallic);
                }

                // Instancing so a long run of tinted sections still batches, and emission left
                // enabled-but-black on the accent so BarrierTint can drive it per instance.
                m.enableInstancing = true;
                if (spec.Emissive)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (isNew) m.SetColor("_EmissionColor", Color.black);
                }

                EditorUtility.SetDirty(m);
                made[spec.Name] = m;
            }

            AssetDatabase.SaveAssets();
            return made;
        }

        // ------------------------------------------------------------------ importer

        /// <summary>
        /// Points the FBX's material slots at the shared assets instead of letting Unity extract a
        /// fresh copy per model, which is what leaves five near-identical greys in the project.
        /// </summary>
        static void RemapModel(string section, Dictionary<string, Material> mats)
        {
            string path = $"{ModelDir}/{section}.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Barriers: no model at {path} — export it from Blender first.");
                return;
            }

            // No materialLocation here: it is gone in Unity 6, and the explicit remaps below are
            // what actually keep the model pointed at the shared assets.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.generateSecondaryUV = true;

            foreach (KeyValuePair<string, Material> kv in mats)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                importer.AddRemap(id, kv.Value);
            }

            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ prefabs

        static bool BuildPrefab(string section)
        {
            EnsureFolder(PrefabDir);

            string modelPath = $"{ModelDir}/{section}.fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return false;

            GameObject root = new GameObject(section);
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform, false);

                root.AddComponent<BarrierTint>();

                // Deliberately no collider. A rotated box per placed section turns a curve into a
                // saw of protruding corners, which is exactly what catches a kart sliding along the
                // edge. The line's Blocking Wall is one continuous swept surface and is what the
                // player should ever touch.

                string prefabPath = $"{PrefabDir}/{section}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The frictionless material the blocking wall wears.
        ///
        /// This is the difference between a barrier a player drifts along and one that eats their
        /// speed the moment they touch it. Combine is Minimum on both, so it wins regardless of
        /// what the kart's own collider is set to.
        /// </summary>
        static void EnsurePhysicsMaterial()
        {
            // .asset rather than .physicsMaterial: CreateAsset refuses to author the native
            // extension, and Unity treats the two as the same asset type either way.
            const string path = "Assets/Barriers/Barrier_Slide.asset";
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path) != null) return;

            var mat = new PhysicsMaterial("Barrier_Slide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            AssetDatabase.CreateAsset(mat, path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
