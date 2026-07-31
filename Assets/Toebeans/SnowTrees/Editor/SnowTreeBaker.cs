using System.IO;
using UnityEditor;
using UnityEngine;

namespace Toebeans.SnowTrees.Editor
{
    /// <summary>
    /// Turns the procedural trees into plain mesh assets plus prefabs, for when
    /// a scene wants static geometry instead of a live <see cref="SnowTree"/>.
    /// </summary>
    public static class SnowTreeBaker
    {
        const string Root = "Assets/Toebeans/SnowTrees";
        const string BakedFolder = Root + "/Baked";

        [MenuItem("Tools/Toebeans/Snow Trees/Bake Meshes and Prefabs")]
        public static void BakeAll()
        {
            if (!AssetDatabase.IsValidFolder(BakedFolder))
            {
                AssetDatabase.CreateFolder(Root, "Baked");
            }

            Material[] materials =
            {
                LoadMaterial("SnowTree_Bark"),
                LoadMaterial("SnowTree_Foliage"),
                LoadMaterial("SnowTree_Snow"),
            };

            foreach (SnowTreeVariant variant in System.Enum.GetValues(typeof(SnowTreeVariant)))
            {
                string name = variant.AssetName();
                Mesh mesh = SnowTreeMeshBuilder.Build(variant);
                string meshPath = Path.Combine(BakedFolder, name + ".asset").Replace('\\', '/');

                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    // Keep the GUID so anything already pointing at this mesh survives.
                    existing.Clear();
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                    EditorUtility.SetDirty(mesh);
                }
                else
                {
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }

                var go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                AddTrunkCollider(go, SnowTreeSettings.ForVariant(variant));

                string prefabPath = Path.Combine(BakedFolder, name + ".prefab").Replace('\\', '/');
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);

                Debug.Log($"[SnowTrees] Baked {name}: {mesh.triangles.Length / 3} triangles -> {prefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("GameObject/3D Object/Toebeans/Snow Tree", false, 10)]
        public static void CreateInScene(MenuCommand command)
        {
            var go = new GameObject("Snow Tree");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                LoadMaterial("SnowTree_Bark"),
                LoadMaterial("SnowTree_Foliage"),
                LoadMaterial("SnowTree_Snow"),
            };
            go.AddComponent<SnowTree>();

            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Snow Tree");
            Selection.activeObject = go;
        }

        static void AddTrunkCollider(GameObject go, SnowTreeSettings settings)
        {
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = Mathf.Max(0.08f, settings.radius * 0.13f);
            capsule.height = settings.height;
            capsule.center = new Vector3(0f, settings.height * 0.5f, 0f);
        }

        static Material LoadMaterial(string name)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Debug.LogWarning($"[SnowTrees] Missing material at {path}.");
            }

            return material;
        }
    }
}
