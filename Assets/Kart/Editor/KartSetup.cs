using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Toebeans.Karting;

namespace Toebeans.Karting.EditorTools
{
    /// <summary>
    /// One-click setup for driving the map: builds the kart and its seated driver at real size, hangs
    /// the kart's own raycast wheels off the same dimensions the model was built from (see
    /// [[unity-wheelcollider-total-velocity-lock]] for why it is not Unity's WheelCollider), saves it
    /// as a prefab and drops it into the open scene with a chase camera.
    ///
    /// The bodywork comes from a <see cref="KartStyle"/>. A mesh style hangs the exported Blender
    /// meshes on the rig; the primitive style builds the same rig out of Unity primitives and needs no
    /// imported assets, which is why it stays as the fallback and the reference.
    /// </summary>
    public static class KartSetup
    {
        const string PrefabPath = "Assets/Prefabs/Kart.prefab";
        const string MaterialFolder = "Assets/Kart/Generated";
        const string ModelFolder = "Assets/GeneratedModels";

        /// <summary>
        /// Which skin each submesh of an imported model wears, matched on the material name baked
        /// into the FBX rather than on slot order. The Blender palette owns that order, and matching
        /// on it would break silently the first time a slot was inserted.
        /// </summary>
        static readonly Dictionary<string, KartSkin> SkinsByMaterialName =
            new Dictionary<string, KartSkin>
            {
                ["KartFrame"] = KartSkin.Frame,
                ["KartBody"] = KartSkin.Body,
                ["KartSeat"] = KartSkin.Seat,
                ["KartRim"] = KartSkin.Rim,
                ["KartRubber"] = KartSkin.Rubber,
            };

        /// <summary>
        /// 50 Hz leaves a raycast suspension feeling soft and a step behind over bumps at kart speeds.
        /// 80 Hz is the cheapest fix that makes it read as suspension rather than a loose connection.
        /// </summary>
        const float TargetFixedTimestep = 0.0125f;

        [MenuItem("Tools/Toebeans/Set Up Drivable Kart %#k", false, 10)]
        public static void SetUp() => SetUp(KartStyle.Default);

        [MenuItem("Tools/Toebeans/Kart Style/Buggy", false, 30)]
        public static void SetUpBuggy() => SetUp(KartStyle.Buggy);

        [MenuItem("Tools/Toebeans/Kart Style/Primitives (no imported assets)", false, 31)]
        public static void SetUpPrimitives() => SetUp(KartStyle.Primitives);

        [MenuItem("Tools/Toebeans/Kart Style/Rebuild Prefab Only", false, 42)]
        public static void RebuildPrefabOnly() => RebuildPrefab(KartStyle.Default);

        /// <summary>
        /// Rebuilds the kart prefab and leaves the scene and the project settings alone.
        ///
        /// A kart already in a scene is a prefab instance, so it picks the new bodywork up on its
        /// own. That makes this the one to reach for when changing style mid-session: the full
        /// setup deletes and replaces the kart in the open scene, deactivates the on-foot player and
        /// raises the project-wide physics tick rate, none of which you want again on the fourth
        /// time you have flipped between two styles to compare them.
        /// </summary>
        public static GameObject RebuildPrefab(KartStyle style)
        {
            GameObject prefab = BuildPrefab(KartDimensions.Default, style);
            if (prefab != null)
                Debug.Log($"[Kart] Rebuilt {PrefabPath} in the '{style.name}' style. Instances in open " +
                          "scenes update themselves; the scene was not otherwise touched.");
            return prefab;
        }

        public static void SetUp(KartStyle style)
        {
            KartDimensions dimensions = KartDimensions.Default;

            GameObject prefab = BuildPrefab(dimensions, style);
            if (prefab == null)
                return;

            PlaceInScene(prefab);
            RaisePhysicsTickRate();
            EnableRunInBackground();
        }

        /// <summary>
        /// Without this the Editor halts the game the instant it loses focus, so clicking the Inspector
        /// mid-drive freezes the kart — and it looks exactly like the kart being stuck rather than the
        /// game being paused. It also makes engine audio cut out whenever you alt-tab.
        /// </summary>
        static void EnableRunInBackground()
        {
            if (PlayerSettings.runInBackground)
                return;

            PlayerSettings.runInBackground = true;
            Debug.Log("[Kart] Enabled Player Settings > Resolution and Presentation > Run In Background, " +
                      "so the kart keeps running when the Editor window loses focus.");
        }

        [MenuItem("Tools/Toebeans/Report Kart Surfaces In Scene", false, 11)]
        public static void ReportSurfaces()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("[Kart] How the kart will read the surfaces in this scene:");

            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (terrain.terrainData == null)
                    continue;

                report.AppendLine($"\nTerrain '{terrain.name}':");
                TerrainLayer[] layers = terrain.terrainData.terrainLayers;
                for (int i = 0; i < layers.Length; i++)
                {
                    string describedBy = KartSurfaceSampler.DescribeLayer(layers[i]);
                    KartSurface surface = KartSurfaceLibrary.Classify(describedBy);
                    string layerName = layers[i] != null ? layers[i].name : "(missing)";
                    report.AppendLine(
                        $"  {i,2}  {layerName,-14} via '{describedBy}'  ->  {surface.name} " +
                        $"(grip {surface.forwardGrip:0.00}/{surface.sidewaysGrip:0.00}, " +
                        $"roll {surface.rollingResistance:0.000})");
                }
            }

            report.AppendLine(
                "\nLayers reading as the wrong surface are matched on their texture name, because the " +
                "layers themselves are all called 'NewLayer N'. Rename the layer (Project window, select " +
                "the .terrainlayer asset, F2) to something containing e.g. 'gravel' or 'rock' and it will " +
                "be picked up by name instead.");

            Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------ prefab

        static GameObject BuildPrefab(KartDimensions d, KartStyle style)
        {
            var root = new GameObject("Kart");

            try
            {
                Dictionary<KartSkin, Material> materials = BuildMaterials();

                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);

                var partsByName = new Dictionary<string, Transform>();
                var pivots = new Dictionary<string, Transform>();

                foreach (KartPivot pivot in KartBlueprint.Pivots(d))
                {
                    var go = new GameObject(pivot.path);
                    go.transform.SetParent(body.transform, false);
                    go.transform.localPosition = pivot.position;
                    go.transform.localRotation = Quaternion.Euler(pivot.euler);
                    pivots[pivot.path] = go.transform;
                }

                foreach (KartPart part in KartBlueprint.Build(d))
                {
                    if (SupersededBy(style, part.group))
                        continue;

                    Transform parent = string.IsNullOrEmpty(part.parent)
                        ? body.transform
                        : pivots[part.parent];
                    partsByName[part.name] = Instantiate(part, parent, materials).transform;
                }

                Transform steering = pivots[KartBlueprint.SteeringPivotPath];
                Transform driver = pivots[KartBlueprint.DriverPivotPath];

                // Both meshes are authored about the transform they hang on - the body about the
                // kart's origin, the rim about the steering hub - so neither needs positioning here.
                if (style.UsesMeshes
                    && !AddStyleMesh(body.transform, style.bodyMesh, "BodyMesh", materials))
                    return null;

                if (style.UsesMeshSteeringWheel
                    && !AddStyleMesh(steering, style.steeringWheelMesh, "SteeringWheelMesh", materials))
                    return null;

                KartWheel[] wheels = BuildWheelAnchors(root, d);
                Transform[] wheelVisuals = BuildWheelVisuals(root, d, style, materials);
                if (wheelVisuals == null)
                    return null;

                BuildChassisColliders(root);

                var rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var controller = root.AddComponent<KartController>();
                controller.wheels = wheels;
                controller.wheelVisuals = wheelVisuals;
                controller.steeringWheel = steering;
                controller.inputActions = FindInputActions();
                rigidbody.mass = controller.TotalMass;
                rigidbody.centerOfMass = controller.centreOfMass;

                root.AddComponent<KartHud>();
                root.AddComponent<KartAudio>();
                BuildCamera(root, controller);

                var rig = driver.gameObject.AddComponent<KartDriverRig>();
                rig.kartRoot = root.transform;
                rig.handLeft = FindIn(steering, "HandL");
                rig.handRight = FindIn(steering, "HandR");
                rig.upperArmLeft = partsByName["UpperArmL"];
                rig.forearmLeft = partsByName["ForearmL"];
                rig.upperArmRight = partsByName["UpperArmR"];
                rig.forearmRight = partsByName["ForearmR"];

                EnsureFolder("Assets/Prefabs");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[Kart] Saved {PrefabPath} in the '{style.name}' style.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static KartWheel[] BuildWheelAnchors(GameObject root, KartDimensions d)
        {
            var holder = new GameObject("Wheels");
            holder.transform.SetParent(root.transform, false);

            // Read the suspension distance off a throwaway controller so the anchor height below stays
            // tied to the same default the controller will use at runtime.
            var defaults = new GameObject("defaults").AddComponent<KartController>();
            float suspensionDistance = defaults.suspensionDistance;
            Object.DestroyImmediate(defaults.gameObject);

            var wheels = new KartWheel[4];

            foreach (KartCorner corner in KartDimensions.Corners)
            {
                Vector3 centre = d.WheelCentre(corner);

                // The anchor is a fixed point on the chassis, suspensionDistance above the wheel's rest
                // centre — the ray for this wheel starts here and reaches straight down for
                // suspensionDistance + radius, so it can find ground anywhere between fully compressed
                // (hub at the anchor) and fully drooped (hub suspensionDistance below it).
                var go = new GameObject($"WheelAnchor_{Abbreviation(corner)}");
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = centre + Vector3.up * suspensionDistance;

                wheels[(int)corner] = new KartWheel { anchor = go.transform };
            }

            return wheels;
        }

        /// <summary>Returns null if a style's wheel mesh could not be loaded.</summary>
        static Transform[] BuildWheelVisuals(GameObject root, KartDimensions d, KartStyle style,
            Dictionary<KartSkin, Material> materials)
        {
            var holder = new GameObject("WheelVisuals");
            holder.transform.SetParent(root.transform, false);

            var visuals = new Transform[4];

            foreach (KartCorner corner in KartDimensions.Corners)
            {
                var go = new GameObject($"Wheel_{Abbreviation(corner)}");
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = d.WheelCentre(corner);

                if (style.UsesMeshes)
                {
                    // The controller drives this transform directly, spinning it about its own right
                    // axis, and the mesh is authored with its axle on local X to match.
                    if (!AddStyleMesh(go.transform, style.WheelMesh(corner), "Mesh", materials))
                        return null;
                }
                else
                {
                    foreach (KartPart part in KartBlueprint.BuildWheel(d, corner))
                        Instantiate(part, go.transform, materials);
                }

                visuals[(int)corner] = go.transform;
            }

            return visuals;
        }

        // ------------------------------------------------------------------ style meshes

        /// <summary>
        /// Whether this style's meshes supersede a group of primitives.
        ///
        /// The driver and their hands never are. KartDriverRig re-aims the arms at the wheel every
        /// frame and the hands orbit with the rim, and geometry baked into a static mesh can do
        /// neither — so a mesh style still gets its driver from primitives.
        /// </summary>
        static bool SupersededBy(KartStyle style, KartPartGroup group)
        {
            switch (group)
            {
                case KartPartGroup.Chassis: return style.UsesMeshes;
                case KartPartGroup.SteeringWheel: return style.UsesMeshSteeringWheel;
                default: return false;
            }
        }

        /// <summary>
        /// Hangs one exported mesh on a transform, wearing the kart's own materials rather than
        /// whatever Unity generated when it imported the FBX. Returns false, having explained itself,
        /// if the model is not there — which is the normal state of affairs until Blender has been
        /// run and the Editor has been focused once to import the result.
        /// </summary>
        static bool AddStyleMesh(Transform parent, string assetName, string objectName,
            Dictionary<KartSkin, Material> materials)
        {
            string path = $"{ModelFolder}/{assetName}.fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogError(
                    $"[Kart] No model at '{path}'. Build the kart's meshes with " +
                    ".\\Tools\\blender\\build-models.ps1 -Model kart_buggy, then focus the Editor " +
                    "once so Unity imports them. Tools > Toebeans > Kart Style > Primitives builds " +
                    "the kart without any imported assets in the meantime.");
                return false;
            }

            MeshFilter source = asset.GetComponentInChildren<MeshFilter>();
            if (source == null || source.sharedMesh == null)
            {
                Debug.LogError($"[Kart] '{path}' imported, but there is no mesh inside it.");
                return false;
            }

            var go = new GameObject(objectName);
            // The mesh is authored about this parent's origin, so no local offset is wanted.
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = SkinMaterials(source, materials, path);
            return true;
        }

        static Material[] SkinMaterials(MeshFilter source, Dictionary<KartSkin, Material> materials,
            string path)
        {
            var imported = source.GetComponent<MeshRenderer>();
            Material[] fromFbx = imported != null ? imported.sharedMaterials : new Material[0];

            var result = new Material[source.sharedMesh.subMeshCount];
            for (int i = 0; i < result.Length; i++)
            {
                string name = i < fromFbx.Length && fromFbx[i] != null ? fromFbx[i].name : null;

                if (name != null && SkinsByMaterialName.TryGetValue(name, out KartSkin skin))
                {
                    result[i] = materials[skin];
                    continue;
                }

                result[i] = materials[KartSkin.Body];
                Debug.LogWarning(
                    $"[Kart] '{path}' submesh {i} carries material '{name ?? "(none)"}', which is not " +
                    "one of the kart skins, so it fell back to KartBody. The names come from the " +
                    "palette in Tools/blender/models/kart_buggy.py.");
            }

            return result;
        }

        /// <summary>
        /// Gives the kart its own camera, so the prefab is the whole thing — drop it into any scene and
        /// it is drivable, with no scene camera to find, tag or repair. The rig detaches it from the
        /// kart on the first frame; parenting here is only so it travels with the prefab.
        /// </summary>
        static void BuildCamera(GameObject root, KartController controller)
        {
            var go = new GameObject("Kart Camera") { tag = "MainCamera" };
            go.transform.SetParent(root.transform, false);

            var camera = go.AddComponent<Camera>();
            // Far enough to see the far side of the mountain, near enough not to clip into the driver.
            camera.farClipPlane = 1500f;
            camera.nearClipPlane = 0.05f;

            go.AddComponent<AudioListener>();

            var rig = go.AddComponent<KartCameraRig>();
            rig.target = controller;
            camera.fieldOfView = rig.baseFieldOfView;

            // Park it where the rig will put it anyway, so the prefab thumbnail and the first frame of
            // Play both look right rather than snapping into place.
            float height = rig.pivotHeight + rig.height;
            go.transform.localPosition = new Vector3(0f, height, -rig.distance);
            go.transform.localRotation = Quaternion.LookRotation(
                new Vector3(0f, rig.pivotHeight, 0f) - go.transform.localPosition, Vector3.up);
        }

        static void BuildChassisColliders(GameObject root)
        {
            // Deliberately narrower than the track and well clear of the ground: the wheels should be
            // doing the colliding, and a body box that reaches past the tyres catches on scenery the
            // kart would otherwise drive straight over.
            //
            // The raycast suspension (see [[unity-wheelcollider-total-velocity-lock]] for why it
            // replaced WheelCollider) settles under load rather than being pinned to a target position,
            // which lowers real ride height by the static sag — confirmed live at 39-51 mm. That still
            // leaves comfortable flat-ground clearance (170 mm down to roughly 125 mm), so it is not
            // raised for that alone; doing so would lift this box above the visual floor pan it is meant
            // to stand in for. What it will not survive is a genuine grade break: LobbyIsland's mountain
            // has confirmed 25-40° edges over just a few metres (see [[lobbyisland-mountain-unroutable]]),
            // and no clearance margin worth keeping the visual alignment for clears a ledge the wheels
            // have only just started to drop into.
            var body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.62f, -0.05f);
            body.size = new Vector3(1.05f, 0.90f, 2.30f);

            // The roll hoop, so landing upside down rests on the bar rather than on the driver's head.
            var hoop = root.AddComponent<BoxCollider>();
            hoop.center = new Vector3(0f, 1.05f, KartBlueprint.RollHoopZ);
            hoop.size = new Vector3(0.90f, 0.86f, 0.20f);
        }

        // ------------------------------------------------------------------ primitives

        static GameObject Instantiate(KartPart part, Transform parent, Dictionary<KartSkin, Material> materials)
        {
            PrimitiveType type = part.shape switch
            {
                KartShape.Box => PrimitiveType.Cube,
                KartShape.Cylinder => PrimitiveType.Cylinder,
                KartShape.Capsule => PrimitiveType.Capsule,
                _ => PrimitiveType.Sphere,
            };

            GameObject go = GameObject.CreatePrimitive(type);
            go.name = part.name;

            // Every primitive arrives with a collider. The wheel raycasts and the two chassis boxes do
            // all the colliding here, and leaving forty more on the body would have the kart snagging
            // on its own driver.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(parent, false);
            go.transform.localPosition = part.position;
            go.transform.localRotation = Quaternion.Euler(part.euler);
            go.transform.localScale = KartBlueprint.LocalScale(part);

            go.GetComponent<Renderer>().sharedMaterial = materials[part.skin];
            return go;
        }

        static Dictionary<KartSkin, Material> BuildMaterials()
        {
            return new Dictionary<KartSkin, Material>
            {
                [KartSkin.Body] = GetOrCreate("KartBody", new Color(0.88f, 0.33f, 0.09f), 0.1f, 0.45f),
                [KartSkin.Frame] = GetOrCreate("KartFrame", new Color(0.22f, 0.23f, 0.26f), 0.65f, 0.45f),
                [KartSkin.Rubber] = GetOrCreate("KartRubber", new Color(0.07f, 0.07f, 0.08f), 0f, 0.22f),
                [KartSkin.Rim] = GetOrCreate("KartRim", new Color(0.72f, 0.74f, 0.78f), 0.9f, 0.7f),
                [KartSkin.Seat] = GetOrCreate("KartSeat", new Color(0.13f, 0.13f, 0.15f), 0f, 0.3f),
                [KartSkin.Suit] = GetOrCreate("DriverSuit", new Color(0.11f, 0.27f, 0.60f), 0f, 0.35f),
                [KartSkin.Helmet] = GetOrCreate("DriverHelmet", new Color(0.93f, 0.93f, 0.95f), 0.1f, 0.8f),
                [KartSkin.Visor] = GetOrCreate("DriverVisor", new Color(0.05f, 0.06f, 0.09f), 0.5f, 0.95f),
            };
        }

        static Material GetOrCreate(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            bool urp = shader.name.StartsWith("Universal");

            material.SetColor(urp ? "_BaseColor" : "_Color", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            else if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);

            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ------------------------------------------------------------------ scene

        static void PlaceInScene(GameObject prefab)
        {
            Scene scene = SceneManager.GetActiveScene();

            foreach (KartController existing in
                     Object.FindObjectsByType<KartController>(FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            // The on-foot player would otherwise stay live alongside the kart: it reads the same Move
            // action (so the character walks off while you drive), draws its readout over this one,
            // fights over the cursor, and is a solid collider sitting exactly where the kart spawns,
            // since both are placed at the Scene view's focus point. Deactivated rather than deleted,
            // so walking is one checkbox away again.
            foreach (Toebeans.ScaleTest.ThirdPersonController walker in
                     Object.FindObjectsByType<Toebeans.ScaleTest.ThirdPersonController>(
                         FindObjectsSortMode.None))
            {
                if (!walker.gameObject.activeSelf)
                    continue;

                Undo.RecordObject(walker.gameObject, "Set Up Drivable Kart");
                walker.gameObject.SetActive(false);
                Debug.Log($"[Kart] Deactivated '{walker.gameObject.name}' — it shares the Move action, " +
                          "the H key and the spawn point with the kart. Tick its checkbox at the top of " +
                          "the Inspector to walk around again.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.position = GuessPlacementPoint();
            Undo.RegisterCreatedObjectUndo(instance, "Set Up Drivable Kart");

            // The kart brings its own camera, so nothing in the scene needs finding or repairing. All
            // that is left is to make sure no other camera draws over the top of it.
            Camera camera = instance.GetComponentInChildren<Camera>(includeInactive: true);
            WarnAboutCompetingCameras(camera, instance, scene);

            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[Kart] Kart placed at {instance.transform.position}. Press Play to drive. " +
                      "W/S throttle and brake · A/D steer · Space handbrake · R recover · C look back · " +
                      "H hide the readout. Click the Game view once to capture the mouse for the camera.");
        }

        /// <summary>
        /// Two enabled cameras both drawing to the screen is settled by depth, and losing that race
        /// looks exactly like the kart's camera never attached. Win the race rather than deleting the
        /// designer's cameras behind their back, and say plainly which ones are competing.
        /// </summary>
        static void WarnAboutCompetingCameras(Camera ours, GameObject instance, Scene scene)
        {
            if (ours == null)
                return;

            Camera[] others = Object
                .FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c != ours
                            && c.gameObject.scene == scene
                            && !c.transform.IsChildOf(instance.transform)
                            && c.enabled
                            && c.gameObject.activeInHierarchy)
                .ToArray();

            var listeners = Object
                .FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(l => !l.transform.IsChildOf(instance.transform))
                .ToArray();

            if (others.Length == 0 && listeners.Length == 0)
                return;

            if (others.Length > 0)
            {
                float highest = others.Max(c => c.depth);
                if (ours.depth <= highest)
                {
                    Undo.RecordObject(ours, "Set Up Drivable Kart");
                    ours.depth = highest + 1f;
                }
            }

            var message = new System.Text.StringBuilder("[Kart] The kart carries its own camera. ");

            if (others.Length > 0)
            {
                message.Append("These cameras are also live and may draw over it: ")
                    .Append(string.Join(", ", others.Select(c => $"'{c.name}'")))
                    .Append($". The kart camera's depth was raised to {ours.depth} so it draws last. ");
            }

            if (listeners.Length > 0)
            {
                message.Append($"There are also {listeners.Length} other AudioListener(s) in the scene, " +
                               "which Unity will complain about. ");
            }

            message.Append("You can safely delete them — the kart no longer needs a camera from the scene.");
            Debug.LogWarning(message.ToString());
        }

        static void RaisePhysicsTickRate()
        {
            if (Time.fixedDeltaTime <= TargetFixedTimestep + 0.0001f)
                return;

            float previous = Time.fixedDeltaTime;
            Time.fixedDeltaTime = TargetFixedTimestep;
            Debug.LogWarning(
                $"[Kart] Raised the physics timestep from {previous:0.0000}s ({1f / previous:0} Hz) to " +
                $"{TargetFixedTimestep:0.0000}s ({1f / TargetFixedTimestep:0} Hz). The raycast suspension " +
                "misses contacts over bumps at 50 Hz, which reads as the kart skating instead of riding " +
                "them. This is a project-wide setting — Edit > Project Settings > Time > Fixed Timestep " +
                "to change it back.");
        }

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
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5000f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                // A little clearance so it drops onto its suspension rather than starting interpenetrated.
                return hit.point + Vector3.up * 0.35f;
            }

            if (Terrain.activeTerrain != null)
            {
                float height = Terrain.activeTerrain.SampleHeight(hint)
                               + Terrain.activeTerrain.transform.position.y;
                return new Vector3(hint.x, height + 0.35f, hint.z);
            }

            return hint;
        }

        // ------------------------------------------------------------------ helpers

        static Transform FindIn(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (child.name == name)
                    return child;
            return null;
        }

        static string Abbreviation(KartCorner corner) => corner switch
        {
            KartCorner.FrontLeft => "FL",
            KartCorner.FrontRight => "FR",
            KartCorner.RearLeft => "RL",
            _ => "RR",
        };

        static InputActionAsset FindInputActions()
        {
            string guid = AssetDatabase.FindAssets("t:InputActionAsset").FirstOrDefault();
            return guid == null
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
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
