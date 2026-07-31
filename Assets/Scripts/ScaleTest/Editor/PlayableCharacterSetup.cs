using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Toebeans.ScaleTest.EditorTools
{
    /// <summary>
    /// One-click setup for walking around the map at human scale: finds a character model, gives it
    /// a locomotion controller, wraps it in a player prefab and drops it into the open scene with a
    /// follow camera.
    /// </summary>
    public static class PlayableCharacterSetup
    {
        const string PrefabPath = "Assets/Prefabs/Player.prefab";
        const string ControllerPath = "Assets/Characters/Generated/PlayerLocomotion.controller";
        const float TargetHeight = 1.8f;
        const float WalkSpeed = 2.0f;
        const float RunSpeed = 5.5f;

        static readonly string[] ModelExtensions = { ".fbx", ".glb", ".gltf", ".dae", ".blend", ".obj" };

        [MenuItem("Tools/Toebeans/Set Up Playable Character %#p", false, 0)]
        public static void SetUp()
        {
            GameObject model = FindBestCharacterModel();
            Run(model);
        }

        [MenuItem("Tools/Toebeans/Set Up Playable Character From Selection", false, 1)]
        public static void SetUpFromSelection()
        {
            GameObject model = Selection.activeObject as GameObject;
            if (model == null || !AssetDatabase.Contains(model))
            {
                EditorUtility.DisplayDialog("Set Up Playable Character",
                    "Select a character model (an .fbx/.glb asset) in the Project window first.", "OK");
                return;
            }
            Run(model);
        }

        [MenuItem("Tools/Toebeans/Add Scale Reference Marker", false, 20)]
        public static void AddScaleReferenceMarker()
        {
            var marker = new GameObject("Scale Reference (1.8 m)");
            marker.AddComponent<ScaleReferenceMarker>();
            marker.transform.position = GuessPlacementPoint();
            Undo.RegisterCreatedObjectUndo(marker, "Add Scale Reference Marker");
            Selection.activeGameObject = marker;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static void Run(GameObject model)
        {
            string modelPath = model != null ? AssetDatabase.GetAssetPath(model) : null;
            bool usingProxy = model == null;

            if (usingProxy)
            {
                Debug.LogWarning(
                    "[ScaleTest] No character model found, so a grey stand-in mannequin was built at 1.8 m instead. " +
                    "Drop the Quaternius characters into Assets/Characters/ and run this menu item again to swap it in " +
                    "(see Assets/Characters/README.md).");
            }
            else
            {
                Debug.Log($"[ScaleTest] Using character model: {modelPath}");
                ConfigureModelImporter(modelPath);
            }

            AnimatorController controller = usingProxy ? null : BuildController(modelPath);

            GameObject prefab = BuildPlayerPrefab(model, controller, usingProxy);
            if (prefab == null)
                return;

            PlaceInScene(prefab);
        }

        // ---------------------------------------------------------------- model discovery

        static GameObject FindBestCharacterModel()
        {
            var scored = new List<(GameObject asset, int score, string path)>();

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = path.ToLowerInvariant();
                if (!ModelExtensions.Any(lower.EndsWith))
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                    continue;

                int score = ScoreCandidate(asset, lower);
                if (score > 0)
                    scored.Add((asset, score, path));
            }

            if (scored.Count == 0)
                return null;

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            if (scored.Count > 1)
            {
                Debug.Log("[ScaleTest] Character candidates found:\n  " +
                          string.Join("\n  ", scored.Take(8).Select(c => $"{c.score,4}  {c.path}")) +
                          "\nUsing the highest scoring one. To pick a different character, select it in the " +
                          "Project window and use Tools ▸ Toebeans ▸ Set Up Playable Character From Selection.");
            }

            return scored[0].asset;
        }

        static int ScoreCandidate(GameObject asset, string lowerPath)
        {
            int score = 0;

            // A skinned mesh is the strongest signal that this is a character and not scenery.
            if (asset.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null)
                score += 100;
            else
                return 0;

            if (asset.GetComponentInChildren<Animator>(includeInactive: true) != null)
                score += 10;

            if (lowerPath.Contains("quaternius")) score += 60;
            if (lowerPath.Contains("character")) score += 30;
            if (lowerPath.Contains("/characters/")) score += 30;

            // Environment packs occasionally ship skinned props; push them down the list.
            if (lowerPath.Contains("environment") || lowerPath.Contains("/props/") || lowerPath.Contains("tree"))
                score -= 40;

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset)) as ModelImporter;
            if (importer != null && importer.animationType == ModelImporterAnimationType.Human)
                score += 20;

            return score;
        }

        // ---------------------------------------------------------------- import settings

        static void ConfigureModelImporter(string modelPath)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                return;

            bool dirty = false;

            if (importer.animationType == ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                dirty = true;
            }

            // Locomotion clips almost always ship un-looped, which reads as the character freezing
            // mid-stride after one cycle.
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            bool clipsChanged = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (!ShouldLoop(clip.name) || clip.loopTime)
                    continue;
                clip.loopTime = true;
                clipsChanged = true;
            }

            if (clipsChanged)
            {
                importer.clipAnimations = clips;
                dirty = true;
            }

            if (!dirty)
                return;

            importer.SaveAndReimport();

            if (importer.animationType == ModelImporterAnimationType.Human)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid)
                {
                    Debug.LogWarning($"[ScaleTest] Humanoid rig mapping failed for {modelPath}; " +
                                     "falling back to a Generic rig. Animations will still play.");
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.SaveAndReimport();
                }
            }
        }

        static bool ShouldLoop(string clipName)
        {
            string name = clipName.ToLowerInvariant();
            return name.Contains("idle") || name.Contains("walk") || name.Contains("run")
                   || name.Contains("jog") || name.Contains("sprint") || name.Contains("fall")
                   || name.Contains("crouch") || name.Contains("swim") || name.Contains("climb");
        }

        // ---------------------------------------------------------------- animator

        static AnimatorController BuildController(string modelPath)
        {
            List<AnimationClip> clips = LoadClips(modelPath);

            // Packs sometimes split animations into sibling files rather than takes in one file.
            if (clips.Count < 2)
            {
                string folder = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/');
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path != modelPath)
                        clips.AddRange(LoadClips(path));
                }
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[ScaleTest] No animation clips found in {modelPath}. " +
                                 "The character will move but stay in its bind pose.");
                return null;
            }

            AnimatorController controller =
                LocomotionControllerBuilder.Build(ControllerPath, clips, WalkSpeed, RunSpeed);

            if (controller == null)
            {
                Debug.LogWarning("[ScaleTest] Found clips but none named like idle/walk/run, so no " +
                                 "animator controller was generated. Clips available:\n  " +
                                 string.Join(", ", clips.Select(c => c.name)));
            }
            else
            {
                Debug.Log($"[ScaleTest] Generated {ControllerPath} from {clips.Count} clip(s).");
            }

            return controller;
        }

        static List<AnimationClip> LoadClips(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToList();
        }

        // ---------------------------------------------------------------- prefab

        static GameObject BuildPlayerPrefab(GameObject model, AnimatorController controller, bool usingProxy)
        {
            var root = new GameObject("Player");
            try
            {
                GameObject visual = usingProxy
                    ? ProxyMannequin.Build()
                    : (GameObject)PrefabUtility.InstantiatePrefab(model);

                if (visual == null)
                {
                    Debug.LogError("[ScaleTest] Could not instantiate the character model.");
                    return null;
                }

                visual.name = "Model";
                visual.transform.SetParent(root.transform, worldPositionStays: false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                NormaliseHeight(visual, TargetHeight);

                var characterController = root.AddComponent<CharacterController>();
                characterController.height = TargetHeight;
                characterController.radius = 0.3f;
                characterController.center = new Vector3(0f, TargetHeight * 0.5f, 0f);
                characterController.skinWidth = 0.02f;
                characterController.stepOffset = 0.4f;
                characterController.slopeLimit = 50f;
                characterController.minMoveDistance = 0f;

                var animator = visual.GetComponent<Animator>() ?? visual.GetComponentInChildren<Animator>();
                if (controller != null)
                {
                    if (animator == null)
                        animator = visual.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    // Always animate: in first person the character is shadow-only, and culling it
                    // there would freeze the shadow that is doing the scale-reading work.
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                var movement = root.AddComponent<ThirdPersonController>();
                movement.standingHeight = TargetHeight;
                movement.walkSpeed = WalkSpeed;
                movement.runSpeed = RunSpeed;
                movement.model = visual.transform;
                movement.inputActions = FindInputActions();

                root.AddComponent<ScaleHud>();

                EnsureFolder("Assets/Prefabs");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[ScaleTest] Saved {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Scales the visual so it stands exactly <paramref name="targetHeight"/> metres tall and
        /// rests on the origin. Character packs are authored at wildly different unit scales, and
        /// getting this wrong is precisely what makes a map read as the wrong size.
        /// </summary>
        static void NormaliseHeight(GameObject visual, float targetHeight)
        {
            if (!TryGetBounds(visual, out Bounds bounds) || bounds.size.y <= 0.0001f)
            {
                Debug.LogWarning("[ScaleTest] Could not measure the character's bounds; left its scale alone.");
                return;
            }

            float factor = targetHeight / bounds.size.y;
            visual.transform.localScale = Vector3.one * factor;

            if (TryGetBounds(visual, out Bounds scaled))
            {
                Vector3 position = visual.transform.localPosition;
                position.y -= scaled.min.y - visual.transform.parent.position.y;
                visual.transform.localPosition = position;
            }

            if (Mathf.Abs(factor - 1f) > 0.01f)
            {
                Debug.Log($"[ScaleTest] Character measured {bounds.size.y:0.00} m tall; " +
                          $"scaled by {factor:0.000} to reach {targetHeight:0.00} m.");
            }
        }

        static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                Bounds rendererBounds = renderer.bounds;

                // Skinned meshes can report a stale bind-pose bounds of zero size in edit mode.
                if (renderer is SkinnedMeshRenderer skinned && rendererBounds.size.y <= 0.0001f
                                                            && skinned.sharedMesh != null)
                {
                    rendererBounds = skinned.sharedMesh.bounds;
                    rendererBounds.center = skinned.transform.TransformPoint(rendererBounds.center);
                    rendererBounds.extents = Vector3.Scale(rendererBounds.extents, skinned.transform.lossyScale);
                }

                if (rendererBounds.size == Vector3.zero)
                    continue;

                if (!found)
                {
                    bounds = rendererBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return found;
        }

        static InputActionAsset FindInputActions()
        {
            string guid = AssetDatabase.FindAssets("t:InputActionAsset").FirstOrDefault();
            return guid == null
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
        }

        // ---------------------------------------------------------------- scene

        static void PlaceInScene(GameObject prefab)
        {
            Scene scene = SceneManager.GetActiveScene();

            foreach (ThirdPersonController existing in
                     Object.FindObjectsByType<ThirdPersonController>(FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.position = GuessPlacementPoint();
            Undo.RegisterCreatedObjectUndo(instance, "Set Up Playable Character");

            Camera camera = Camera.main
                            ?? Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                Undo.RegisterCreatedObjectUndo(cameraObject, "Set Up Playable Character");
            }

            PlayerCameraRig rig = camera.GetComponent<PlayerCameraRig>()
                                  ?? Undo.AddComponent<PlayerCameraRig>(camera.gameObject);
            rig.target = instance.GetComponent<ThirdPersonController>();
            EditorUtility.SetDirty(rig);

            if (camera.farClipPlane < 500f)
                camera.farClipPlane = 1000f;
            // A tight near plane keeps the first person view from clipping through nearby geometry.
            camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f);

            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[ScaleTest] Player placed at {instance.transform.position}. Press Play to walk around. " +
                      "WASD move · Shift sprint · Space jump · Ctrl crouch · V first/third person · H toggle readout.");
        }

        /// <summary>
        /// Drops onto whatever is under the scene view's focus point so the character spawns where
        /// the level designer is already looking.
        /// </summary>
        static Vector3 GuessPlacementPoint()
        {
            Vector3 hint = Vector3.zero;

            if (SceneView.lastActiveSceneView != null)
                hint = SceneView.lastActiveSceneView.pivot;
            else if (Terrain.activeTerrain != null)
                hint = Terrain.activeTerrain.transform.position
                       + new Vector3(Terrain.activeTerrain.terrainData.size.x * 0.5f, 0f,
                           Terrain.activeTerrain.terrainData.size.z * 0.5f);

            Physics.SyncTransforms();

            var origin = new Vector3(hint.x, hint.y + 500f, hint.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            if (Terrain.activeTerrain != null)
            {
                float height = Terrain.activeTerrain.SampleHeight(hint) + Terrain.activeTerrain.transform.position.y;
                return new Vector3(hint.x, height + 0.05f, hint.z);
            }

            return hint;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
