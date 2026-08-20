using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Toebeans;

namespace Toebeans.EditorTools
{
    /// <summary>
    /// Moves procedurally generated meshes out of the scene file and into asset files.
    ///
    /// A mesh with no asset behind it is serialised inline wherever it is referenced, so every
    /// generator in this project was writing its geometry into the scene. LavaWorld reached 56 MB
    /// that way, of which 51 MB was 124 embedded meshes - 117 of them barrier sections. GitHub
    /// warns past 50 MB and refuses past 100, and every commit touching the scene rewrote all of it.
    ///
    /// The mechanism is <see cref="AssetDatabase.AddObjectToAsset"/>, which turns the mesh instance
    /// that is already in memory into a sub-asset **in place**. Nothing is copied and nothing is
    /// repointed: the MeshFilter still holds the same reference, that reference is simply an asset
    /// afterwards, and the scene writes a GUID instead of a vertex array.
    ///
    /// This runs on scene save rather than living inside each generator. There are five generators
    /// today and every new track will bring more, and a rule enforced at the point of serialisation
    /// cannot be forgotten by the next one. It is also self-limiting: once a scene is clean, a save
    /// finds nothing embedded and touches no files at all.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneMeshExtractor
    {
        const string RootFolder = "Assets/GeneratedMeshes";
        const string AutoExtractPref = "Toebeans.SceneMeshExtractor.AutoExtract";
        const string AutoExtractMenu = "Tools/Toebeans/Meshes/Extract Automatically On Save";

        static SceneMeshExtractor()
        {
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public static bool AutoExtract
        {
            get { return EditorPrefs.GetBool(AutoExtractPref, true); }
            set { EditorPrefs.SetBool(AutoExtractPref, value); }
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Toebeans/Meshes/Extract Embedded Meshes From Open Scenes", false, 10)]
        public static void ExtractOpenScenes()
        {
            var total = new Report();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    total.Add(Extract(scene));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(total.Describe("Extraction complete"));
        }

        [MenuItem("Tools/Toebeans/Meshes/Report Embedded Meshes In Open Scenes", false, 11)]
        public static void ReportOpenScenes()
        {
            var sb = new StringBuilder("Embedded (scene-serialised) meshes:\n");
            long totalVerts = 0;
            int totalMeshes = 0;

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var group in CollectEmbedded(scene))
                {
                    long verts = 0;
                    foreach (Mesh m in group.Value)
                        verts += m.vertexCount;
                    totalVerts += verts;
                    totalMeshes += group.Value.Count;
                    sb.AppendFormat("  {0} / {1}: {2} meshes, {3:N0} verts\n",
                        scene.name, group.Key.name, group.Value.Count, verts);
                }
            }

            if (totalMeshes == 0)
                sb.Append("  none - every mesh in the open scenes is asset-backed.");
            else
                sb.AppendFormat("  TOTAL {0} meshes, {1:N0} verts", totalMeshes, totalVerts);

            Debug.Log(sb.ToString());
        }

        [MenuItem(AutoExtractMenu, false, 30)]
        static void ToggleAutoExtract()
        {
            AutoExtract = !AutoExtract;
        }

        [MenuItem(AutoExtractMenu, true)]
        static bool ToggleAutoExtractValidate()
        {
            Menu.SetChecked(AutoExtractMenu, AutoExtract);
            return true;
        }

        // ------------------------------------------------------------------ the save hook

        static void OnSceneSaving(Scene scene, string path)
        {
            if (!AutoExtract)
                return;

            // Meshes built during play are throwaway - they belong to a session, not to the
            // project, and writing them to disk would litter the folder with every test run.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Report report = Extract(scene);
            if (report.meshes == 0)
                return;

            // Deferred: the scene is mid-serialisation, and flushing the asset database inside
            // that is asking for re-entrancy. The references are already correct by now, so the
            // scene writes GUIDs whether or not the .asset file has hit disk yet.
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.SaveAssets();
                Debug.Log(report.Describe("Extracted generated meshes while saving " + scene.name));
            };
        }

        // ------------------------------------------------------------------ the work

        /// <summary>
        /// Pull every scene-serialised mesh in <paramref name="scene"/> into a library asset,
        /// one per root object. Safe to run repeatedly - a scene with nothing embedded is a no-op.
        /// </summary>
        public static Report Extract(Scene scene)
        {
            var report = new Report();
            if (!scene.IsValid() || !scene.isLoaded)
                return report;

            foreach (var group in CollectEmbedded(scene))
            {
                GameObject owner = group.Key;
                List<Mesh> embedded = group.Value;

                string folder = EnsureSceneFolder(scene.name);
                string path = string.Format("{0}/{1}.asset", folder, LibraryName(owner));

                var library = AssetDatabase.LoadAssetAtPath<GeneratedMeshLibrary>(path);
                if (library == null)
                {
                    library = ScriptableObject.CreateInstance<GeneratedMeshLibrary>();
                    library.sourceScene = scene.name;
                    library.ownerObject = owner.name;
                    AssetDatabase.CreateAsset(library, path);
                    report.librariesCreated++;
                }
                else if (!string.IsNullOrEmpty(library.ownerObject) && library.ownerObject != owner.name)
                {
                    // Belt and braces behind LibraryName's id. Pruning a library that belongs to a
                    // different object would delete that object's geometry, so say so loudly
                    // rather than quietly doing it.
                    Debug.LogWarningFormat(library,
                        "Mesh library '{0}' was written for '{1}' but is now being reused for '{2}'. " +
                        "Skipping it rather than risk pruning the other object's meshes.",
                        path, library.ownerObject, owner.name);
                    continue;
                }

                foreach (Mesh mesh in embedded)
                {
                    // A mesh flagged DontSave silently refuses to serialise, which would leave the
                    // reference dangling rather than extracted.
                    mesh.hideFlags = HideFlags.None;
                    if (string.IsNullOrEmpty(mesh.name))
                        mesh.name = owner.name + "_Mesh";

                    AssetDatabase.AddObjectToAsset(mesh, library);
                    if (!library.meshes.Contains(mesh))
                        library.meshes.Add(mesh);

                    report.meshes++;
                    report.vertices += mesh.vertexCount;
                }

                PruneUnreferenced(library, path, owner, report);

                library.sourceScene = scene.name;
                library.ownerObject = owner.name;
                EditorUtility.SetDirty(library);
                report.libraries++;
            }

            return report;
        }

        /// <summary>
        /// Drop sub-assets nothing in the owner's hierarchy points at any more. Without this a
        /// library only ever grows: regenerating a barrier line replaces all 117 meshes, and the
        /// previous 117 would sit in the file forever.
        /// </summary>
        static void PruneUnreferenced(GeneratedMeshLibrary library, string path, GameObject owner, Report report)
        {
            var live = new HashSet<Mesh>();
            foreach (var mf in owner.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) live.Add(mf.sharedMesh);
            foreach (var mc in owner.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null) live.Add(mc.sharedMesh);

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var mesh = sub as Mesh;
                if (mesh == null || live.Contains(mesh))
                    continue;

                library.meshes.Remove(mesh);
                AssetDatabase.RemoveObjectFromAsset(mesh);
                Object.DestroyImmediate(mesh, true);
                report.pruned++;
            }

            library.meshes.RemoveAll(m => m == null);
        }

        /// <summary>
        /// Every scene-serialised mesh, grouped by the root object it hangs under. Grouping by
        /// root keeps one generator's rebuild from rewriting another's file.
        /// </summary>
        static Dictionary<GameObject, List<Mesh>> CollectEmbedded(Scene scene)
        {
            var byOwner = new Dictionary<GameObject, List<Mesh>>();
            var seen = new HashSet<Mesh>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    Consider(mf.sharedMesh, root, byOwner, seen);
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    Consider(mc.sharedMesh, root, byOwner, seen);
            }

            return byOwner;
        }

        static void Consider(Mesh mesh, GameObject root,
                             Dictionary<GameObject, List<Mesh>> byOwner, HashSet<Mesh> seen)
        {
            if (mesh == null)
                return;

            // Already an asset - a model, a primitive, or something extracted on an earlier pass.
            if (AssetDatabase.Contains(mesh))
                return;

            // A collider sharing its filter's mesh must only be counted once.
            if (!seen.Add(mesh))
                return;

            if (!byOwner.TryGetValue(root, out List<Mesh> list))
            {
                list = new List<Mesh>();
                byOwner[root] = list;
            }
            list.Add(mesh);
        }

        /// <summary>
        /// File name for one root object's library: its name, plus an id derived from the object
        /// itself.
        ///
        /// The id is not decoration. Scene root names are not unique - LavaWorld has several
        /// objects called "Barrier Line" - and naming libraries after them alone made those roots
        /// share one file. <see cref="PruneUnreferenced"/> only knows about the owner it was
        /// handed, so the next regeneration would have deleted every OTHER root's meshes out of
        /// the shared file as unreferenced, leaving those objects with no geometry.
        ///
        /// <see cref="GlobalObjectId"/> is the stable choice here: it is built from the object's
        /// local file id, so it survives saves, reloads and renames of the object.
        /// </summary>
        static string LibraryName(GameObject owner)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(owner);
            uint shortId = (uint)(id.targetObjectId ^ (id.targetObjectId >> 32));
            return string.Format("{0}_{1:x8}", Sanitise(owner.name), shortId);
        }

        static string EnsureSceneFolder(string sceneName)
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");

            string folder = RootFolder + "/" + Sanitise(sceneName);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(RootFolder, Sanitise(sceneName));

            return folder;
        }

        static string Sanitise(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "Unnamed" : result;
        }

        /// <summary>What one extraction pass did, so the log says something useful.</summary>
        public struct Report
        {
            public int meshes;
            public int libraries;
            public int librariesCreated;
            public int pruned;
            public long vertices;

            public void Add(Report other)
            {
                meshes += other.meshes;
                libraries += other.libraries;
                librariesCreated += other.librariesCreated;
                pruned += other.pruned;
                vertices += other.vertices;
            }

            public string Describe(string headline)
            {
                if (meshes == 0 && pruned == 0)
                    return headline + ": nothing embedded - the scene was already clean.";

                return string.Format(
                    "{0}: {1} meshes ({2:N0} verts) moved into {3} librarie(s), {4} new, {5} stale pruned.",
                    headline, meshes, vertices, libraries, librariesCreated, pruned);
            }
        }
    }
}
