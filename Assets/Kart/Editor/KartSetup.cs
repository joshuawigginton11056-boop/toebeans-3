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
                ["KartLens"] = KartSkin.Lens,
            };

        /// <summary>
        /// 50 Hz leaves a raycast suspension feeling soft and a step behind over bumps at kart speeds.
        /// 80 Hz is the cheapest fix that makes it read as suspension rather than a loose connection.
        /// </summary>
        const float TargetFixedTimestep = 0.0125f;

        [MenuItem("Tools/Toebeans/Set Up Drivable Kart %#k", false, 10)]
        public static void SetUp() => SetUp(KartStyle.Default);

        // One hand-written entry per style, because [MenuItem] is an attribute and attributes cannot
        // be generated from a list at runtime. This is the reason KartStyle.All stays hand-written
        // while the palettes come from Blender manifests: a style needs a menu entry either way, and
        // a wrong mesh name here fails loudly the moment you click it.
        [MenuItem("Tools/Toebeans/Kart Style/Buggy", false, 30)]
        public static void SetUpBuggy() => SetUp(KartStyle.Buggy);

        [MenuItem("Tools/Toebeans/Kart Style/Cinder hauler (lava)", false, 31)]
        public static void SetUpCinderHauler() => SetUp(KartStyle.CinderHauler);

        [MenuItem("Tools/Toebeans/Kart Style/Overgrowth (jungle)", false, 32)]
        public static void SetUpOvergrowth() => SetUp(KartStyle.Overgrowth);

        [MenuItem("Tools/Toebeans/Kart Style/Piste basher (snow)", false, 33)]
        public static void SetUpPisteBasher() => SetUp(KartStyle.PisteBasher);

        [MenuItem("Tools/Toebeans/Kart Style/Mine cart (cave)", false, 34)]
        public static void SetUpMineCart() => SetUp(KartStyle.MineCart);

        [MenuItem("Tools/Toebeans/Kart Style/Field marshal (farm)", false, 35)]
        public static void SetUpFieldMarshal() => SetUp(KartStyle.FieldMarshal);

        [MenuItem("Tools/Toebeans/Kart Style/Log racer (woodland)", false, 36)]
        public static void SetUpLogRacer() => SetUp(KartStyle.LogRacer);

        [MenuItem("Tools/Toebeans/Kart Style/Bone chariot (hell)", false, 37)]
        public static void SetUpBoneChariot() => SetUp(KartStyle.BoneChariot);

        [MenuItem("Tools/Toebeans/Kart Style/Pit rat (unlock)", false, 38)]
        public static void SetUpPitRat() => SetUp(KartStyle.PitRat);

        [MenuItem("Tools/Toebeans/Kart Style/Primitives (no imported assets)", false, 49)]
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
                Dictionary<KartSkin, Material> materials = BuildMaterials(style);

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
                GameObject bodyMesh = null;
                if (style.UsesMeshes)
                {
                    bodyMesh = AddStyleMesh(body.transform, style.bodyMesh, "BodyMesh", materials);
                    if (bodyMesh == null)
                        return null;
                }

                if (style.UsesMeshSteeringWheel
                    && AddStyleMesh(steering, style.steeringWheelMesh, "SteeringWheelMesh", materials) == null)
                    return null;

                BuildLights(root, body.transform, style, materials, bodyMesh);

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
                    if (AddStyleMesh(go.transform, style.WheelMesh(corner), "Mesh", materials) == null)
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

        // ------------------------------------------------------------------ lamps

        /// <summary>
        /// Hangs real Lights on the lamps the bodywork already has, and wires the glass up to them.
        ///
        /// Nothing is built here if the style has no lamps — see <see cref="KartStyle.headlights"/>.
        /// The housings are part of the model, so this only ever adds the Lights and the switch; if
        /// the glass cannot be found the Lights are still built, because a beam that works out of a
        /// dull lens is a smaller problem than no headlights at all, and the warning says which.
        /// </summary>
        static void BuildLights(GameObject root, Transform body, KartStyle style,
            Dictionary<KartSkin, Material> materials, GameObject bodyMesh)
        {
            if (!style.headlights)
                return;

            var holder = new GameObject("Headlights");
            holder.transform.SetParent(body, false);

            var headlamps = new List<Light>();
            foreach (KartLamp lamp in KartBlueprint.Lamps())
            {
                if (lamp.kind != KartLampKind.Headlamp || !style.noseLamps)
                    continue;

                // On the front face of the glass, not inside the housing: a spot light behind its own
                // bodywork lights the bodywork.
                headlamps.Add(Lamp(holder.transform, $"{lamp.name}Light",
                    lamp.LensCentre + new Vector3(0f, 0f, KartBlueprint.LensThickness * 0.5f)));
            }

            var lights = root.AddComponent<KartLights>();
            lights.headlamps = headlamps.ToArray();
            // Only if the bodywork has something to hang it on. The mine cart's single carbide lamp
            // sits on this exact point and wants it; the piste basher has no roof bar at all, and a
            // light here would throw a beam out of thin air above the driver's head.
            lights.roofBar = style.roofBar
                ? Lamp(holder.transform, "RoofBarLight", KartBlueprint.RoofBarLightCentre)
                : null;
            lights.lensOff = materials[KartSkin.Lens];
            lights.lensLit = LitLensMaterial();
            lights.lenses = FindLenses(bodyMesh, materials[KartSkin.Lens], style);
            lights.Apply();
            lights.Set(lights.onAtStart);
        }

        static Light Lamp(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            // Everything else about the light — cone, range, intensity, how far it is tipped down — is
            // KartLights' to set, so all of it stays tunable in one Inspector rather than half here.
            return go.AddComponent<Light>();
        }

        /// <summary>
        /// Which submesh of the bodywork is the glass. Found by material rather than by index, for the
        /// same reason <see cref="SkinMaterials"/> matches on names: the Blender palette owns the slot
        /// order, and an index here would break silently the first time a slot was inserted.
        /// </summary>
        static KartLights.Lens[] FindLenses(GameObject bodyMesh, Material lensMaterial, KartStyle style)
        {
            var renderer = bodyMesh != null ? bodyMesh.GetComponent<MeshRenderer>() : null;
            int submesh = renderer != null
                ? System.Array.IndexOf(renderer.sharedMaterials, lensMaterial)
                : -1;

            if (submesh < 0)
            {
                Debug.LogWarning(
                    $"[Kart] The '{style.name}' style has headlights, but nothing in its bodywork wears " +
                    "the KartLens material, so the lamp glass will not light up with the beams. Rebuild " +
                    "the model with .\\Tools\\blender\\build-models.ps1 -Model kart_buggy and focus the " +
                    "Editor once so Unity re-imports it.");
                return new KartLights.Lens[0];
            }

            return new[] { new KartLights.Lens { renderer = renderer, submesh = submesh } };
        }

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
        /// whatever Unity generated when it imported the FBX. Returns null, having explained itself,
        /// if the model is not there — which is the normal state of affairs until Blender has been
        /// run and the Editor has been focused once to import the result.
        /// </summary>
        static GameObject AddStyleMesh(Transform parent, string assetName, string objectName,
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
                return null;
            }

            MeshFilter source = asset.GetComponentInChildren<MeshFilter>();
            if (source == null || source.sharedMesh == null)
            {
                Debug.LogError($"[Kart] '{path}' imported, but there is no mesh inside it.");
                return null;
            }

            var go = new GameObject(objectName);
            // The mesh is authored about this parent's origin, so no local offset is wanted.
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = SkinMaterials(source, materials, path);
            return go;
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
            // to stand in for. What it will not survive is a genuine grade break: LavaWorld's mountain
            // has confirmed 25-40° edges over just a few metres (see [[lobbyisland-mountain-unroutable]]),
            // and no clearance margin worth keeping the visual alignment for clears a ledge the wheels
            // have only just started to drop into.
            // Raised out of the visual floor pan on purpose, reversing the earlier judgement above.
            // Two things forced it: the softer offroad springs sag about 97 mm rather than 45, and
            // catching on terrain lips was a named complaint about how the kart drives. At the old
            // centre the box's underside sat 170 mm up and barely 70 mm clear once settled, so every
            // bridge edge and volcano approach caught the chassis before the wheels ever reached it.
            //
            // The cost is honest and accepted: the floor pan now hangs about 130 mm below the box, so
            // on a hard compression the pan can visually clip a surface it is not colliding with.
            // Brief cosmetic intersection is a far better trade than a kart that beaches on a kerb —
            // the wheels are supposed to be what meets the world, and now they are.
            var body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.70f, -0.05f);
            body.size = new Vector3(1.05f, 0.80f, 2.30f);

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

        /// <summary>
        /// The kart's materials, for one style.
        ///
        /// The five bodywork slots plus the lens come from the style when it has a palette, and from
        /// the shared defaults below when it does not. The driver's three do not: the driver is the
        /// same person whichever kart they are sitting in, and their suit belongs to them rather than
        /// to the bodywork they happen to be strapped into.
        ///
        /// A style's materials are their own assets, named for the style, because these are project
        /// assets shared by every kart of that style — writing a colour onto the one global KartBody
        /// would repaint the entire grid, which is the same trap <see cref="LitLensMaterial"/> is
        /// split out to avoid.
        /// </summary>
        static Dictionary<KartSkin, Material> BuildMaterials(KartStyle style)
        {
            KartStyleManifest.Apply(style);

            var materials = new Dictionary<KartSkin, Material>
            {
                [KartSkin.Suit] = GetOrCreate("DriverSuit", new Color(0.11f, 0.27f, 0.60f), 0f, 0.35f),
                [KartSkin.Helmet] = GetOrCreate("DriverHelmet", new Color(0.93f, 0.93f, 0.95f), 0.1f, 0.8f),
                [KartSkin.Visor] = GetOrCreate("DriverVisor", new Color(0.05f, 0.06f, 0.09f), 0.5f, 0.95f),
            };

            foreach (KeyValuePair<KartSkin, KartSkinColour> slot in DefaultBodywork)
            {
                KartSkin skin = slot.Key;
                KartSkinColour look = slot.Value;
                string name = DefaultBodyworkNames[skin];

                if (style?.palette != null && style.palette.TryGetValue(skin, out KartSkinColour own))
                {
                    look = own;
                    name = $"Kart{style.key}_{name}";
                }

                materials[skin] = GetOrCreate(name, look.color, look.metallic, look.smoothness,
                    look.emission);
            }

            return materials;
        }

        /// <summary>
        /// The buggy's palette, which doubles as the fallback for any style whose Blender manifest has
        /// not been built yet. Kept here rather than in a manifest so that a checkout with no
        /// generated assets in it still produces a kart that looks like something.
        /// </summary>
        static readonly Dictionary<KartSkin, KartSkinColour> DefaultBodywork =
            new Dictionary<KartSkin, KartSkinColour>
            {
                [KartSkin.Body] = Look(0.88f, 0.33f, 0.09f, 0.1f, 0.45f),
                [KartSkin.Frame] = Look(0.22f, 0.23f, 0.26f, 0.65f, 0.45f),
                [KartSkin.Rubber] = Look(0.07f, 0.07f, 0.08f, 0f, 0.22f),
                [KartSkin.Rim] = Look(0.72f, 0.74f, 0.78f, 0.9f, 0.7f),
                [KartSkin.Seat] = Look(0.13f, 0.13f, 0.15f, 0f, 0.3f),
                // Cold glass, which is what a headlamp looks like switched off — pale and glossy, not
                // white. KartLights swaps this submesh for KartLensLit when the lamps come on.
                [KartSkin.Lens] = Look(0.62f, 0.64f, 0.62f, 0.2f, 0.95f),
            };

        static readonly Dictionary<KartSkin, string> DefaultBodyworkNames =
            new Dictionary<KartSkin, string>
            {
                [KartSkin.Body] = "KartBody",
                [KartSkin.Frame] = "KartFrame",
                [KartSkin.Rubber] = "KartRubber",
                [KartSkin.Rim] = "KartRim",
                [KartSkin.Seat] = "KartSeat",
                [KartSkin.Lens] = "KartLens",
            };

        static KartSkinColour Look(float r, float g, float b, float metallic, float smoothness)
        {
            return new KartSkinColour
            {
                color = new Color(r, g, b),
                metallic = metallic,
                smoothness = smoothness,
                emission = Color.black,
            };
        }

        /// <summary>
        /// The lit half of the lens pair. Its own asset rather than emission written onto KartLens at
        /// runtime: the materials here are project assets shared by every kart, so writing to one
        /// would switch on the whole grid — and in the Editor it would stay switched on after Play.
        /// </summary>
        static Material LitLensMaterial()
        {
            return GetOrCreate("KartLensLit", new Color(1f, 0.97f, 0.86f), 0f, 0.9f,
                // Beyond 1 so the lens blows out rather than just being pale, which is what makes it
                // read as a lamp that is on from the low chase camera and in daylight.
                emission: new Color(3.2f, 2.9f, 2.2f));
        }

        static Material GetOrCreate(string name, Color color, float metallic, float smoothness,
            Color emission = default)
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

            if (emission.maxColorComponent > 0f)
            {
                // URP reads emission off the keyword as well as the colour, and a material that has
                // the colour without the keyword renders exactly as if it had neither.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

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
                      "L headlights · H hide the readout. Click the Game view once to capture the mouse " +
                      "for the camera.");
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
