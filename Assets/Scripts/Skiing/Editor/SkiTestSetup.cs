using System.Linq;
using Toebeans.ScaleTest;
using Toebeans.ScaleTest.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Toebeans.Skiing.EditorTools
{
    /// <summary>
    /// One-click setup for feeling out the ski mechanics: drops a skier onto the terrain under the
    /// scene view's focus, points it down the fall line, and puts the chase camera behind it.
    ///
    /// Re-run it to move the skier — reframe the scene view over a different pitch and go again.
    /// That is the tuning loop: the mechanics are driven by real terrain, so which slope you test
    /// on changes the answer.
    /// </summary>
    public static class SkiTestSetup
    {
        const float TargetHeight = 1.8f;

        [MenuItem("Tools/Toebeans/Set Up Skier %#k", false, 10)]
        public static void SetUp()
        {
            Scene scene = SceneManager.GetActiveScene();
            Physics.SyncTransforms();

            GameObject skier = FindOrCreateSkier(out SkiController ski);
            if (skier == null)
                return;

            Vector3 spawn = GuessPlacementPoint(out Vector3 normal);
            skier.transform.position = spawn;
            skier.transform.rotation = Quaternion.Euler(0f, FallLineYaw(normal), 0f);

            ParkWalkingPlayer();
            SetUpCamera(ski);

            Selection.activeGameObject = skier;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[Skiing] Skier placed at {spawn} on a {Vector3.Angle(normal, Vector3.up):0}° pitch, " +
                      "pointing downhill. Press Play.\n" +
                      "A/D carve · W tuck · S brake · Shift edge · Space hold-to-charge jump · R respawn · H readout.");
        }

        [MenuItem("Tools/Toebeans/Set Up Skier %#k", true)]
        static bool SetUpValidate() => !Application.isPlaying;

        // ---------------------------------------------------------------- the skier

        static GameObject FindOrCreateSkier(out SkiController ski)
        {
            ski = Object.FindAnyObjectByType<SkiController>();
            if (ski != null)
            {
                Undo.RecordObject(ski.transform, "Set Up Skier");
                return ski.gameObject;
            }

            var root = new GameObject("Skier");
            Undo.RegisterCreatedObjectUndo(root, "Set Up Skier");

            GameObject visual = BuildVisual();
            visual.name = "Model";
            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            var controller = root.AddComponent<CharacterController>();
            controller.height = TargetHeight;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, TargetHeight * 0.5f, 0f);
            controller.skinWidth = 0.02f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 89f;
            controller.minMoveDistance = 0f;

            ski = root.AddComponent<SkiController>();
            ski.standingHeight = TargetHeight;
            ski.model = visual.transform;
            ski.inputActions = FindInputActions();

            root.AddComponent<SkiHud>();
            return root;
        }

        /// <summary>
        /// Reuses whatever character the scale-test rig already found, so the skier looks like the
        /// game rather than like a debug capsule. Falls back to the grey mannequin, which is fine:
        /// skis are not the point yet, feel is.
        /// </summary>
        static GameObject BuildVisual()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            Transform existingModel = playerPrefab != null ? playerPrefab.transform.Find("Model") : null;

            if (existingModel != null)
            {
                var copy = Object.Instantiate(existingModel.gameObject);
                // The animator would fight the code-driven pose; there is no ski clip to play yet.
                foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true))
                    animator.enabled = false;
                return copy;
            }

            return ProxyMannequin.Build();
        }

        // ---------------------------------------------------------------- scene wiring

        /// <summary>
        /// Disables the walking player rather than deleting it — the scale rig is still worth having,
        /// it just cannot share a camera or an input map with the skier.
        /// </summary>
        static void ParkWalkingPlayer()
        {
            foreach (ThirdPersonController walker in Object.FindObjectsByType<ThirdPersonController>())
            {
                if (!walker.gameObject.activeSelf)
                    continue;
                Undo.RecordObject(walker.gameObject, "Set Up Skier");
                walker.gameObject.SetActive(false);
                Debug.Log($"[Skiing] Disabled the walking player '{walker.name}' so it does not fight the skier " +
                          "for the camera. Re-enable it in the Hierarchy to go back to the scale test.");
            }
        }

        static void SetUpCamera(SkiController ski)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>();
            Camera camera = Camera.main ?? cameras.FirstOrDefault();

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                Undo.RegisterCreatedObjectUndo(cameraObject, "Set Up Skier");
            }

            if (!camera.CompareTag("MainCamera"))
            {
                Undo.RecordObject(camera.gameObject, "Set Up Skier");
                camera.gameObject.tag = "MainCamera";
            }

            if (cameras.Length > 1)
            {
                Debug.LogWarning($"[Skiing] The scene has {cameras.Length} cameras. The chase rig went on " +
                                 $"'{camera.name}'. If the Game view does not follow the skier, another camera " +
                                 "is rendering over it.");
            }

            // The walking rig's orbit camera and this one would both write the transform every frame.
            if (camera.TryGetComponent(out PlayerCameraRig walkingRig))
                Undo.DestroyObjectImmediate(walkingRig);

            SkiCameraRig rig = camera.GetComponent<SkiCameraRig>()
                               ?? Undo.AddComponent<SkiCameraRig>(camera.gameObject);
            rig.target = ski;
            EditorUtility.SetDirty(rig);

            // Frame it now so the Game view is already correct before entering Play.
            Undo.RecordObject(camera.transform, "Set Up Skier");
            Vector3 pivot = ski.transform.position + Vector3.up * rig.pivotHeight;
            Quaternion rotation = Quaternion.Euler(rig.pitch, ski.transform.eulerAngles.y, 0f);
            camera.transform.SetPositionAndRotation(
                pivot + Vector3.up * rig.height + rotation * Vector3.back * rig.distance, rotation);

            camera.fieldOfView = rig.baseFov;
            if (camera.farClipPlane < 500f)
                camera.farClipPlane = 2000f;
        }

        // ---------------------------------------------------------------- placement

        static Vector3 GuessPlacementPoint(out Vector3 normal)
        {
            normal = Vector3.up;
            Vector3 hint = Vector3.zero;

            if (SceneView.lastActiveSceneView != null)
                hint = SceneView.lastActiveSceneView.pivot;
            else if (Terrain.activeTerrain != null)
                hint = Terrain.activeTerrain.transform.position
                       + new Vector3(Terrain.activeTerrain.terrainData.size.x * 0.5f, 0f,
                           Terrain.activeTerrain.terrainData.size.z * 0.5f);

            var origin = new Vector3(hint.x, hint.y + 500f, hint.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                return hit.point + Vector3.up * 0.05f;
            }

            if (Terrain.activeTerrain != null)
            {
                Terrain terrain = Terrain.activeTerrain;
                float height = terrain.SampleHeight(hint) + terrain.transform.position.y;
                Vector3 local = hint - terrain.transform.position;
                normal = terrain.terrainData.GetInterpolatedNormal(
                    Mathf.Clamp01(local.x / terrain.terrainData.size.x),
                    Mathf.Clamp01(local.z / terrain.terrainData.size.z));
                return new Vector3(hint.x, height + 0.05f, hint.z);
            }

            return hint;
        }

        /// <summary>The world yaw pointing straight down the fall line of a surface.</summary>
        static float FallLineYaw(Vector3 normal)
        {
            Vector3 fall = Vector3.ProjectOnPlane(Vector3.down, normal);
            if (fall.sqrMagnitude < 1e-5f)
                return 0f;
            return Mathf.Atan2(fall.x, fall.z) * Mathf.Rad2Deg;
        }

        static InputActionAsset FindInputActions()
        {
            string guid = AssetDatabase.FindAssets("t:InputActionAsset").FirstOrDefault();
            return guid == null
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}
