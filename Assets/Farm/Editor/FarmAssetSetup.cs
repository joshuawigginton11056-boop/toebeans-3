using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farm.EditorTools
{
    /// <summary>
    /// Turns the generated farm FBXs into things you can drag into a scene: the shared materials,
    /// the importer remaps that point every model at them, and a prefab per model with its
    /// collider, its rebuilt rig and whichever component makes it move.
    ///
    /// Everything it does comes out of the Blender build's manifests — see <see cref="FarmManifest"/>
    /// for why. There is no table of model names in this file, so adding a prop to the pack is a
    /// Blender-side change and nothing else.
    ///
    /// Idempotent, the same way <c>BarrierAssetSetup</c> is: re-run it after re-exporting and it
    /// repairs whatever drifted rather than making a second copy of everything. Material *colours*
    /// are the one exception — those are only stamped when a material is first created, so a colour
    /// tweaked by hand in Unity survives a re-run.
    ///
    ///     Tools > Toebeans > Farm > Set Up Generated Farm Assets
    /// </summary>
    public static class FarmAssetSetup
    {
        const string ModelDir = "Assets/GeneratedModels";
        const string MaterialDir = "Assets/GeneratedModels/Materials";
        const string PrefabDir = "Assets/Prefabs/Farm";

        [MenuItem("Tools/Toebeans/Farm/Set Up Generated Farm Assets")]
        public static void Run()
        {
            List<FarmManifest.Model> models;
            Dictionary<string, FarmManifest.Material> wanted;
            if (!FarmManifest.TryLoadAll(out models, out wanted)) return;

            Dictionary<string, Material> mats = EnsureMaterials(wanted);
            if (mats.Count == 0) return;

            // Only bakes what is missing, so a quack retuned by hand is not overwritten by a
            // routine re-run. Tools > Toebeans > Farm > Bake Duck Quacks forces them all.
            FarmQuackBaker.Bake(force: false);

            // Deliberately not inside StartAssetEditing, for the reason BarrierAssetSetup gives:
            // each remap has to reimport before the prefab step can load the model, and a reimport
            // inside a batched edit does not run.
            int remapped = 0;
            for (int i = 0; i < models.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Farm assets", $"Importing {models[i].name}", (float)i / models.Count))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }
                if (RemapModel(models[i], mats)) remapped++;
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            int prefabs = 0;
            for (int i = 0; i < models.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Farm assets", $"Building {models[i].name}", (float)i / models.Count))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }
                if (BuildPrefab(models[i])) prefabs++;

                // The pond duck is the same model wearing a different component. It gets its own
                // prefab rather than a checkbox on the walking one, because "drag the duck onto the
                // pond" should not also require knowing which box to tick.
                if (models[i].name == "Farm_Duck" && BuildPondDuck(models[i])) prefabs++;
            }
            EditorUtility.ClearProgressBar();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Farm: {mats.Count} materials, {remapped}/{models.Count} models remapped, " +
                      $"{prefabs} prefabs written to {PrefabDir}.");
        }

        // ------------------------------------------------------------------ materials

        static Dictionary<string, Material> EnsureMaterials(
            Dictionary<string, FarmManifest.Material> wanted)
        {
            EnsureFolder(MaterialDir);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("Farm: URP Lit shader not found — is the render pipeline set up?");
                return new Dictionary<string, Material>();
            }

            var made = new Dictionary<string, Material>();

            foreach (KeyValuePair<string, FarmManifest.Material> kv in wanted)
            {
                FarmManifest.Material spec = kv.Value;
                string path = $"{MaterialDir}/{spec.name}.mat";

                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = m == null;
                if (isNew)
                {
                    m = new Material(lit) { name = spec.name };
                    AssetDatabase.CreateAsset(m, path);

                    // Only stamped on creation. A re-run must not undo a colour somebody chose in
                    // the inspector — the manifest is where a colour *starts*, not where it lives.
                    m.SetColor("_BaseColor", spec.Colour);
                    m.SetColor("_Color", spec.Colour);
                    m.SetFloat("_Smoothness", spec.Smoothness);
                    m.SetFloat("_Metallic", spec.metallic);
                }

                // Instancing so a field of forty identical fence sections still batches.
                m.enableInstancing = true;

                if (spec.emissive)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    // Left enabled but black, the way the barrier accent is: the keyword is what
                    // costs a shader variant, and turning it on later at runtime would not take.
                    if (isNew) m.SetColor("_EmissionColor", Color.black);
                }

                EditorUtility.SetDirty(m);
                made[spec.name] = m;
            }

            AssetDatabase.SaveAssets();
            return made;
        }

        // ------------------------------------------------------------------ importer

        static bool RemapModel(FarmManifest.Model model, Dictionary<string, Material> mats)
        {
            string path = $"{ModelDir}/{model.name}.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Farm: no model at {path} — build it from Blender first.");
                return false;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importNormals = ModelImporterNormals.Import;

            // Lightmap UVs cost import time and are only worth it for things that hold still. A
            // cow is never lightmapped, and unwrapping forty animals would add minutes to a
            // re-import for nothing.
            importer.generateSecondaryUV = IsStatic(model);

            // Only the slots this model actually wears. The Blender side compacts each mesh's
            // palette down to the colours it uses, so remapping the whole pack onto every model
            // would add slots the mesh has no faces in.
            if (model.materials != null)
            {
                foreach (string name in model.materials)
                {
                    Material m;
                    if (!mats.TryGetValue(name, out m)) continue;
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), m);
                }
            }

            importer.SaveAndReimport();
            return true;
        }

        /// <summary>Anything that never moves, and so can be batched, occluded and lightmapped.</summary>
        static bool IsStatic(FarmManifest.Model model)
        {
            return model.tag != "animal" && model.tag != "vehicle" && model.tag != "windpump";
        }

        // ------------------------------------------------------------------ prefabs

        static bool BuildPrefab(FarmManifest.Model model)
        {
            return Build(model, model.name, pond: false);
        }

        static bool BuildPondDuck(FarmManifest.Model model)
        {
            return Build(model, "Farm_PondDuck", pond: true);
        }

        static bool Build(FarmManifest.Model model, string prefabName, bool pond)
        {
            EnsureFolder(PrefabDir);

            string modelPath = $"{ModelDir}/{model.name}.fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null) return false;

            GameObject root = new GameObject(prefabName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.transform.SetParent(root.transform, false);

                if (model.IsRig)
                {
                    // A rig has to be restructured — the FBX carries every part as a direct child
                    // of the root because the exporter cannot carry a deeper hierarchy through the
                    // Y-up bake (see toebeans_blender.build_hierarchy). Reparenting is a structural
                    // change, and Unity forbids those on a model prefab instance, so the link has
                    // to go. Re-running this tool after a re-export rebuilds it, which is the
                    // documented workflow anyway.
                    PrefabUtility.UnpackPrefabInstance(
                        instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                    // Unity names an imported FBX's root after the file, so the root part —
                    // "Body" on an animal, "Tower" on the windpump — arrives called "Farm_Cow"
                    // and every rig link naming it as a parent fails to resolve. Rename it back
                    // to what the model script called it, which is the name the manifest, the
                    // rig rebuild below and FarmAnimal at runtime all agree on.
                    foreach (FarmManifest.Part part in model.parts)
                    {
                        if (!string.IsNullOrEmpty(part.parent)) continue;
                        instance.name = part.name;
                        break;
                    }

                    RebuildRig(instance, model);
                }

                AddCollider(root, instance, model);
                AddBehaviour(root, model, pond);

                if (IsStatic(model))
                {
                    GameObjectUtility.SetStaticEditorFlags(root,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
                }

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{prefabName}.prefab");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Puts the parts back into the hierarchy the model script described.
        ///
        /// Every part's origin is already its joint in world space, so re-parenting with
        /// worldPositionStays leaves every joint exactly where Blender put it — the transform
        /// values change, the geometry does not move at all.
        /// </summary>
        static void RebuildRig(GameObject instance, FarmManifest.Model model)
        {
            var found = new Dictionary<string, Transform>();
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!found.ContainsKey(t.name)) found[t.name] = t;
            }

            foreach (FarmManifest.Part part in model.parts)
            {
                if (string.IsNullOrEmpty(part.parent)) continue;

                Transform child, parent;
                bool haveChild = found.TryGetValue(part.name, out child);
                bool haveParent = found.TryGetValue(part.parent, out parent);
                if (!haveChild || !haveParent)
                {
                    // Says which end is missing. The first version of this message did not, and
                    // a wall of "Body > Head is missing" hid the fact that Head was fine and it
                    // was Body — renamed by the importer — that could not be found.
                    string missing = !haveChild && !haveParent ? $"{part.parent} and {part.name}"
                                   : haveChild ? part.parent : part.name;
                    Debug.LogWarning(
                        $"Farm: {model.name} wants the rig link {part.parent} > {part.name}, but " +
                        $"the imported model has no '{missing}'. Re-export it from Blender.");
                    continue;
                }

                if (child.parent == parent) continue;
                child.SetParent(parent, worldPositionStays: true);
            }
        }

        static void AddCollider(GameObject root, GameObject instance, FarmManifest.Model model)
        {
            switch (model.collider)
            {
                case FarmManifest.ColliderNone:
                    // Fence sections and the scarecrow. A barrier section must not collide at all:
                    // BarrierLine's swept Blocking Wall is the only thing a kart should touch on a
                    // run, and a collider per section is the saw of corners that system exists to
                    // avoid. Do not helpfully add one.
                    return;

                case FarmManifest.ColliderBox:
                {
                    Bounds b = LocalBounds(root, instance, null);
                    var col = root.AddComponent<BoxCollider>();
                    col.center = b.center;
                    col.size = b.size;
                    return;
                }

                case FarmManifest.ColliderCapsule:
                {
                    // Fitted to the Body part's own renderer, not to its children. A capsule round
                    // the full bounds of a cow takes in its head, tail and the swing of its legs —
                    // a collider half a metre bigger than the animal in every direction, which on
                    // a race track reads as an invisible wall standing next to the cow.
                    //
                    // "Its own renderer" is the operative part: Body is the *root* of the rig, so
                    // scoping to it and walking its children is scoping to the whole animal. The
                    // first version of this did exactly that and gave the cow a 2.14 m capsule.
                    Bounds b = LocalBounds(root, instance, "Body", selfOnly: true);
                    var col = root.AddComponent<CapsuleCollider>();
                    col.center = b.center;
                    // Along the animal's length, which is the axis a body is actually shaped like.
                    col.direction = 2;
                    col.radius = Mathf.Max(b.extents.x, b.extents.y) * 0.9f;
                    col.height = Mathf.Max(b.size.z, col.radius * 2f);

                    // Kinematic body so a moving collider is cheap. Without one, Unity treats every
                    // animal as a static collider being teleported each frame and rebuilds the
                    // broadphase for it — which a herd of forty makes very obvious.
                    var rb = root.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    return;
                }

                default:
                {
                    foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) continue;
                        var col = mf.gameObject.AddComponent<MeshCollider>();
                        col.sharedMesh = mf.sharedMesh;
                        // Never convex: these are static scenery and the mesh is the collider, the
                        // same as everything else this project generates. A convex hull would fill
                        // in the barn's doorway, which is the one thing it is for.
                        col.convex = false;
                    }
                    return;
                }
            }
        }

        static void AddBehaviour(GameObject root, FarmManifest.Model model, bool pond)
        {
            if (pond)
            {
                PondDuck duck = root.AddComponent<PondDuck>();
                duck.waterline = model.waterline;
                duck.quacks = LoadQuacks();

                // Configured here rather than left to the AudioSource defaults, because the
                // default is 2D — and a 2D quack plays at full volume in the player's ear from
                // anywhere on the map, which is the single most common way an ambient sound
                // ships broken.
                var audio = root.GetComponent<AudioSource>();
                if (audio == null) audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 4f;
                audio.maxDistance = 34f;
                return;
            }

            switch (model.tag)
            {
                case "animal":
                    root.AddComponent<FarmAnimal>();
                    break;
                case "vehicle":
                    root.AddComponent<FarmVehicle>();
                    break;
                case "windpump":
                    root.AddComponent<FarmWindpump>();
                    break;
            }
        }

        static AudioClip[] LoadQuacks()
        {
            var clips = new List<AudioClip>(FarmQuackBaker.Variants);
            for (int i = 0; i < FarmQuackBaker.Variants; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"{FarmQuackBaker.AudioDir}/Quack_{i + 1}.wav");
                if (clip != null) clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning("Farm: no quacks found — the pond duck will float in silence. " +
                                 "Run Tools > Toebeans > Farm > Bake Duck Quacks.");
            }
            return clips.ToArray();
        }

        /// <summary>
        /// Renderer bounds in the prefab root's space, optionally for one named part.
        /// `selfOnly` measures that part's own renderer and ignores everything hanging off it.
        /// </summary>
        static Bounds LocalBounds(GameObject root, GameObject instance, string partName,
                                  bool selfOnly = false)
        {
            Transform scope = instance.transform;
            if (!string.IsNullOrEmpty(partName))
            {
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != partName) continue;
                    scope = t;
                    break;
                }
            }

            Renderer[] renderers = selfOnly
                ? scope.GetComponents<Renderer>()
                : scope.GetComponentsInChildren<Renderer>(true);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            bool any = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            foreach (Renderer r in renderers)
            {
                if (!r.enabled) continue;
                Bounds b = r.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = toRoot.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }

            if (!any) return new Bounds(Vector3.zero, Vector3.one);
            return new Bounds((min + max) * 0.5f, max - min);
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
