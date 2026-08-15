using UnityEditor;
using UnityEngine;

namespace FrozenLake.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="FrozenLakeGenerator"/>: live stats, a regenerate/randomise pair, and
    /// a bake button for turning the current lake into a mesh asset.
    /// </summary>
    [CustomEditor(typeof(FrozenLakeGenerator))]
    [CanEditMultipleObjects]
    public class FrozenLakeGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (FrozenLakeGenerator)target;

            EditorGUILayout.Space();
            DrawStats(generator);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate"))
                {
                    foreach (Object t in targets) ((FrozenLakeGenerator)t).Generate();
                }

                if (GUILayout.Button("Randomise Seed"))
                {
                    foreach (Object t in targets)
                    {
                        var g = (FrozenLakeGenerator)t;
                        Undo.RecordObject(g, "Randomise Frozen Lake");
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

            EditorGUILayout.HelpBox(
                "Submeshes are ordered: 0 pale ice, 1 deep ice, 2 snow, 3 rock. " +
                "Assign four materials on the Mesh Renderer in that order.",
                MessageType.None);
        }

        static void DrawStats(FrozenLakeGenerator generator)
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
            EditorGUILayout.LabelField("Size",
                string.Format("{0:F1} x {1:F1} x {2:F1} m", size.x, size.y, size.z));
        }

        static void SaveMeshAsset(FrozenLakeGenerator generator)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Frozen Lake Mesh", generator.Mesh.name, "asset",
                "Bake the current lake into a mesh asset.");
            if (string.IsNullOrEmpty(path)) return;

            // Instantiate so the saved asset is independent of the live generated mesh.
            var copy = Object.Instantiate(generator.Mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
            Debug.Log("Saved frozen lake mesh to " + path, copy);
        }
    }

    /// <summary>Adds the lake to the GameObject creation menu with its materials already wired up.</summary>
    public static class FrozenLakeMenu
    {
        const string MaterialFolder = "Assets/FrozenLake/Materials/";

        static readonly string[] MaterialNames =
        {
            "FL_Ice_Pale", "FL_Ice_Deep", "FL_Snow", "FL_Rock"
        };

        [MenuItem("GameObject/3D Object/Frozen Lake (Low Poly)", false, 12)]
        public static void Create(MenuCommand command)
        {
            var go = new GameObject("Frozen Lake");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<FrozenLakeGenerator>();

            var materials = new Material[MaterialNames.Length];
            for (int i = 0; i < MaterialNames.Length; i++)
            {
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(
                    MaterialFolder + MaterialNames[i] + ".mat");
            }
            renderer.sharedMaterials = materials;

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}
