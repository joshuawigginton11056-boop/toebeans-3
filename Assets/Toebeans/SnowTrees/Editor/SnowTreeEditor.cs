using UnityEditor;
using UnityEngine;

namespace Toebeans.SnowTrees.Editor
{
    [CustomEditor(typeof(SnowTree))]
    [CanEditMultipleObjects]
    public class SnowTreeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild"))
                {
                    foreach (Object t in targets)
                    {
                        ((SnowTree)t).Rebuild();
                    }
                }

                if (GUILayout.Button("Randomise Seed"))
                {
                    foreach (Object t in targets)
                    {
                        Undo.RecordObject(t, "Randomise Snow Tree Seed");
                        ((SnowTree)t).RandomiseSeed();
                    }
                }
            }

            var tree = (SnowTree)target;
            var filter = tree.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                EditorGUILayout.HelpBox(
                    $"{filter.sharedMesh.triangles.Length / 3} triangles, " +
                    $"{filter.sharedMesh.vertexCount} vertices.",
                    MessageType.None);
            }
        }
    }
}
