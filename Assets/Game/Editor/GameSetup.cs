using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Toebeans.Game;

namespace Toebeans.Game.EditorTools
{
    /// <summary>
    /// One-click setup for the game flow: makes the mode and track catalogs, fills them with a
    /// starting set, and builds the Bootstrap scene that carries <see cref="GameDirector"/>.
    ///
    /// Built from a script rather than by hand for the same reason the kart and the barriers are:
    /// scene and asset YAML merges badly, and a setup you can re-run is one you can fix by
    /// re-running. Everything here is idempotent - existing assets are found and updated rather
    /// than replaced, so running it twice does not duplicate the catalog or reset a track someone
    /// has already tuned.
    ///
    /// The Bootstrap scene is created ADDITIVELY on purpose. This project usually has a large,
    /// dirty scene open, and a setup tool that swaps the active scene out from under an unsaved
    /// LavaWorld would cost more than it saves.
    /// </summary>
    public static class GameSetup
    {
        const string ContentFolder = "Assets/Game/Content";
        const string ModeFolder = ContentFolder + "/Modes";
        const string TrackFolder = ContentFolder + "/Tracks";
        const string ModeCatalogPath = ContentFolder + "/GameModeCatalog.asset";
        const string TrackCatalogPath = ContentFolder + "/TrackCatalog.asset";
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("Tools/Toebeans/Game/Set Up Game Flow", false, 10)]
        public static void SetUpGameFlow()
        {
            EnsureFolders();

            GameModeCatalog modes = EnsureModeCatalog();
            TrackCatalog tracks = EnsureTrackCatalog();

            AssetDatabase.SaveAssets();

            bool createdScene = EnsureBootstrapScene(modes, tracks);
            EnsureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogFormat(
                "Game flow ready. {0} modes, {1} tracks, Bootstrap scene {2}.\n" +
                "Press Play from Assets/Scenes/Bootstrap.unity to walk the phases.",
                modes.modes.Count, tracks.tracks.Count, createdScene ? "created" : "already existed");

            Selection.activeObject = modes;
        }

        /// <summary>
        /// Turns the scene that is open right now into a track the map picker can offer. This is
        /// the whole "add a map" workflow: open the scene, run this, fill in the preview image.
        /// </summary>
        [MenuItem("Tools/Toebeans/Game/Add Open Scene As Track", false, 11)]
        public static void AddOpenSceneAsTrack()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Add Open Scene As Track",
                    "Save the scene first - an unsaved scene has no path to point a track at.", "OK");
                return;
            }

            EnsureFolders();
            TrackCatalog catalog = EnsureTrackCatalog();

            string trackId = MakeId(scene.name);
            TrackDefinition existing = catalog.Find(trackId);
            if (existing != null)
            {
                EditorUtility.DisplayDialog("Add Open Scene As Track",
                    string.Format("'{0}' is already in the catalog as '{1}'.", scene.name, existing.name), "OK");
                Selection.activeObject = existing;
                return;
            }

            TrackDefinition track = CreateTrack(trackId, scene.name, scene.path, 3, 8, true);
            if (!catalog.tracks.Contains(track))
                catalog.tracks.Add(track);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AddSceneToBuildSettings(scene.path);

            Debug.LogFormat(track, "Added '{0}' to the track catalog and to Build Settings.", scene.name);
            Selection.activeObject = track;
        }

        /// <summary>
        /// Checks the content a lobby depends on without needing to press Play: duplicate ids,
        /// tracks pointing at scenes that are not in the build, empty catalogs.
        /// </summary>
        [MenuItem("Tools/Toebeans/Game/Validate Game Content", false, 22)]
        public static void ValidateGameContent()
        {
            var modes = AssetDatabase.LoadAssetAtPath<GameModeCatalog>(ModeCatalogPath);
            var tracks = AssetDatabase.LoadAssetAtPath<TrackCatalog>(TrackCatalogPath);
            int problems = 0;

            if (modes == null)
            {
                Debug.LogError("No mode catalog at " + ModeCatalogPath);
                problems++;
            }
            if (tracks == null)
            {
                Debug.LogError("No track catalog at " + TrackCatalogPath);
                problems++;
            }

            if (modes != null)
            {
                foreach (string id in modes.FindDuplicateIds())
                {
                    Debug.LogErrorFormat(modes, "Two modes share the id '{0}'.", id);
                    problems++;
                }
                if (modes.Default() == null)
                {
                    Debug.LogError("The mode catalog has no valid default mode.", modes);
                    problems++;
                }
            }

            if (tracks != null)
            {
                foreach (string id in tracks.FindDuplicateIds())
                {
                    Debug.LogErrorFormat(tracks, "Two tracks share the id '{0}'.", id);
                    problems++;
                }

                foreach (TrackDefinition track in tracks.tracks)
                {
                    if (track == null)
                    {
                        Debug.LogError("The track catalog has an empty entry.", tracks);
                        problems++;
                        continue;
                    }

                    if (!track.IsValid)
                    {
                        Debug.LogErrorFormat(track, "Track '{0}' has no id or no scene.", track.name);
                        problems++;
                        continue;
                    }

                    if (!IsSceneInBuildSettings(track.scenePath))
                    {
                        Debug.LogErrorFormat(track,
                            "Track '{0}' points at '{1}', which is not in Build Settings - it will fail to load.",
                            track.displayName, track.scenePath);
                        problems++;
                    }
                }
            }

            if (problems == 0)
                Debug.Log("Game content is valid.");
            else
                Debug.LogWarningFormat("Game content has {0} problem(s) - see above.", problems);
        }

        // ------------------------------------------------------------------ content

        static GameModeCatalog EnsureModeCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameModeCatalog>(ModeCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<GameModeCatalog>();
                AssetDatabase.CreateAsset(catalog, ModeCatalogPath);
            }

            // A starting set that exercises the branches the phase machine actually has: one
            // ordinary race, one that skips track select, and one that refuses AI entirely.
            AddModeIfMissing(catalog, CreateMode(
                "vs_race", "VS Race",
                "A single race on a track everyone agrees on.",
                sortOrder: 0, minRacers: 1, maxRacers: 8,
                allowsAiFill: true, usesTrackSelect: true, itemsEnabled: true, laps: 3));

            AddModeIfMissing(catalog, CreateMode(
                "grand_prix", "Grand Prix",
                "Four tracks, points after each. The cup picks the tracks, not you.",
                sortOrder: 1, minRacers: 1, maxRacers: 8,
                allowsAiFill: true, usesTrackSelect: false, itemsEnabled: true, laps: 3));

            AddModeIfMissing(catalog, CreateMode(
                "time_trial", "Time Trial",
                "You, one track, and the clock. No items, no bots.",
                sortOrder: 2, minRacers: 1, maxRacers: 1,
                allowsAiFill: false, usesTrackSelect: true, itemsEnabled: false, laps: 3));

            if (string.IsNullOrEmpty(catalog.defaultModeId))
                catalog.defaultModeId = "vs_race";

            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static TrackCatalog EnsureTrackCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(TrackCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrackCatalog>();
                AssetDatabase.CreateAsset(catalog, TrackCatalogPath);
            }

            // LavaWorld is the only scene in the project that is somewhere you could actually
            // drive, so it seeds the catalog. It is marked playable so the flow can be walked
            // end to end today; real circuits replace it.
            const string lavaWorld = "Assets/Scenes/LavaWorld.unity";
            if (System.IO.File.Exists(lavaWorld) && catalog.Find("lava_world") == null)
            {
                TrackDefinition track = CreateTrack(
                    "lava_world", "LavaWorld", lavaWorld,
                    defaultLapCount: 3, maxRacers: 8, playable: true);
                track.description = "Volcanic home world. Placeholder circuit while the real tracks are built.";
                track.cupId = "test";

                // Marked dirty explicitly: CreateTrack saves the asset, so these two fields are
                // set after it exists and would otherwise be dropped on the next reimport.
                EditorUtility.SetDirty(track);
                catalog.tracks.Add(track);
            }

            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static void AddModeIfMissing(GameModeCatalog catalog, GameModeDefinition mode)
        {
            if (mode != null && !catalog.modes.Contains(mode))
                catalog.modes.Add(mode);
        }

        static GameModeDefinition CreateMode(
            string modeId, string displayName, string description, int sortOrder,
            int minRacers, int maxRacers, bool allowsAiFill, bool usesTrackSelect,
            bool itemsEnabled, int laps)
        {
            string path = string.Format("{0}/Mode_{1}.asset", ModeFolder, ToPascal(modeId));
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(path);
            if (mode != null)
                return mode;

            mode = ScriptableObject.CreateInstance<GameModeDefinition>();
            mode.modeId = modeId;
            mode.displayName = displayName;
            mode.description = description;
            mode.sortOrder = sortOrder;
            mode.minRacers = minRacers;
            mode.maxRacers = maxRacers;
            mode.allowsAiFill = allowsAiFill;
            mode.usesTrackSelect = usesTrackSelect;
            mode.itemsEnabled = itemsEnabled;
            mode.defaultLapCount = laps;

            AssetDatabase.CreateAsset(mode, path);
            return mode;
        }

        static TrackDefinition CreateTrack(
            string trackId, string displayName, string scenePath,
            int defaultLapCount, int maxRacers, bool playable)
        {
            string path = string.Format("{0}/Track_{1}.asset", TrackFolder, ToPascal(trackId));
            var track = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
            if (track != null)
                return track;

            track = ScriptableObject.CreateInstance<TrackDefinition>();
            track.trackId = trackId;
            track.displayName = displayName;
            track.scenePath = scenePath;
            track.sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            track.defaultLapCount = defaultLapCount;
            track.maxRacers = maxRacers;
            track.isPlayable = playable;

            AssetDatabase.CreateAsset(track, path);
            return track;
        }

        // ------------------------------------------------------------------ scene

        static bool EnsureBootstrapScene(GameModeCatalog modes, TrackCatalog tracks)
        {
            if (System.IO.File.Exists(BootstrapScenePath))
            {
                WireExistingDirector(modes, tracks);
                return false;
            }

            // Additive, so whatever the user has open and dirty stays open and dirty.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var root = new GameObject("GameDirector");
            SceneManager.MoveGameObjectToScene(root, scene);

            GameDirector director = root.AddComponent<GameDirector>();
            director.modeCatalog = modes;
            director.trackCatalog = tracks;
            director.frontEndScenePath = BootstrapScenePath;
            director.startPhase = GamePhase.MainMenu;

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
            EditorSceneManager.CloseScene(scene, true);
            return true;
        }

        /// <summary>
        /// Re-point an already-saved Bootstrap scene at the catalogs, for the case where the scene
        /// exists but the references were lost - which is what a broken merge looks like.
        /// </summary>
        static void WireExistingDirector(GameModeCatalog modes, TrackCatalog tracks)
        {
            Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
            GameDirector director = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                director = root.GetComponentInChildren<GameDirector>(true);
                if (director != null)
                    break;
            }

            if (director == null)
            {
                var host = new GameObject("GameDirector");
                SceneManager.MoveGameObjectToScene(host, scene);
                director = host.AddComponent<GameDirector>();
                director.frontEndScenePath = BootstrapScenePath;
            }

            bool changed = false;
            if (director.modeCatalog != modes)
            {
                director.modeCatalog = modes;
                changed = true;
            }
            if (director.trackCatalog != tracks)
            {
                director.trackCatalog = tracks;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(director);
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.CloseScene(scene, true);
        }

        // ------------------------------------------------------------------ build settings

        /// <summary>
        /// Bootstrap has to be index 0 - it is the scene that carries the director, so it is the
        /// scene a build must open on. Everything already listed is kept.
        /// </summary>
        static void EnsureBuildSettings()
        {
            AddSceneToBuildSettings(BootstrapScenePath);

            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == BootstrapScenePath);
            if (index > 0)
            {
                EditorBuildSettingsScene bootstrap = scenes[index];
                scenes.RemoveAt(index);
                scenes.Insert(0, bootstrap);
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(TrackCatalogPath);
            if (catalog == null)
                return;

            foreach (TrackDefinition track in catalog.tracks)
            {
                if (track != null && !string.IsNullOrEmpty(track.scenePath))
                    AddSceneToBuildSettings(track.scenePath);
            }
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || IsSceneInBuildSettings(scenePath))
                return;

            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static bool IsSceneInBuildSettings(string scenePath)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == scenePath && scene.enabled)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolders()
        {
            EnsureFolder("Assets/Game", "Content");
            EnsureFolder(ContentFolder, "Modes");
            EnsureFolder(ContentFolder, "Tracks");
        }

        static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>Scene name to a stable lower_snake id, which is what ids in this game look like.</summary>
        static string MakeId(string source)
        {
            var builder = new System.Text.StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c))
                {
                    // An id boundary at a capital keeps "LavaWorld" reading as "lava_world"
                    // rather than "lavaworld".
                    if (char.IsUpper(c) && builder.Length > 0 && builder[builder.Length - 1] != '_')
                        builder.Append('_');
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }
            return builder.ToString().Trim('_');
        }

        /// <summary>lower_snake id back to PascalCase, for asset file names.</summary>
        static string ToPascal(string id)
        {
            string[] parts = id.Split('_');
            var builder = new System.Text.StringBuilder(id.Length);
            foreach (string part in parts)
            {
                if (part.Length == 0)
                    continue;
                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    builder.Append(part.Substring(1));
            }
            return builder.ToString();
        }
    }
}
