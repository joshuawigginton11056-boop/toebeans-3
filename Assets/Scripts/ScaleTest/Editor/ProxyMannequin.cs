using UnityEditor;
using UnityEngine;

namespace Toebeans.ScaleTest.EditorTools
{
    /// <summary>
    /// Builds a 1.8 m mannequin out of primitives, using real adult proportions. It stands in until
    /// a proper character pack is imported, so the map is walkable at correct scale immediately.
    /// </summary>
    public static class ProxyMannequin
    {
        const string MaterialFolder = "Assets/Characters/Generated";
        const float Height = 1.8f;

        public static GameObject Build()
        {
            Material body = GetOrCreateMaterial("ProxyMannequinBody", new Color(0.62f, 0.63f, 0.66f));
            Material accent = GetOrCreateMaterial("ProxyMannequinAccent", new Color(0.85f, 0.32f, 0.24f));

            var root = new GameObject("ProxyMannequin");

            // Proportions expressed as fractions of total height, the way figure drawing references
            // describe them, so changing Height keeps the shape correct.
            float hip = Height * 0.51f;
            float shoulder = Height * 0.82f;
            float headRadius = Height * 0.064f;

            AddCapsule(root, body, "Leg_L", new Vector3(-0.10f, hip * 0.5f, 0f), hip, 0.075f);
            AddCapsule(root, body, "Leg_R", new Vector3(0.10f, hip * 0.5f, 0f), hip, 0.075f);
            AddCapsule(root, body, "Torso", new Vector3(0f, (hip + shoulder) * 0.5f, 0f), shoulder - hip, 0.17f);
            AddCapsule(root, body, "Arm_L", new Vector3(-0.23f, shoulder - Height * 0.20f, 0f), Height * 0.40f, 0.06f);
            AddCapsule(root, body, "Arm_R", new Vector3(0.23f, shoulder - Height * 0.20f, 0f), Height * 0.40f, 0.06f);
            AddCapsule(root, body, "Neck", new Vector3(0f, shoulder + Height * 0.03f, 0f), Height * 0.06f, 0.05f);

            AddSphere(root, body, "Head", new Vector3(0f, Height - headRadius, 0f), headRadius);

            AddCube(root, body, "Foot_L", new Vector3(-0.10f, 0.03f, 0.04f), new Vector3(0.10f, 0.06f, 0.26f));
            AddCube(root, body, "Foot_R", new Vector3(0.10f, 0.03f, 0.04f), new Vector3(0.10f, 0.06f, 0.26f));

            // A blunt nose makes the facing direction unmistakable while checking sightlines.
            AddCube(root, accent, "Facing", new Vector3(0f, Height - headRadius, headRadius + 0.04f),
                new Vector3(0.05f, 0.05f, 0.10f));

            return root;
        }

        static void AddCapsule(GameObject parent, Material material, string name, Vector3 centre,
            float height, float radius)
        {
            GameObject go = CreatePrimitive(PrimitiveType.Capsule, parent, material, name);
            go.transform.localPosition = centre;
            // Unity's capsule primitive is 2 units tall with a 1 unit diameter at unit scale.
            go.transform.localScale = new Vector3(radius * 2f, Mathf.Max(height, radius * 2f) * 0.5f, radius * 2f);
        }

        static void AddSphere(GameObject parent, Material material, string name, Vector3 centre, float radius)
        {
            GameObject go = CreatePrimitive(PrimitiveType.Sphere, parent, material, name);
            go.transform.localPosition = centre;
            go.transform.localScale = Vector3.one * (radius * 2f);
        }

        static void AddCube(GameObject parent, Material material, string name, Vector3 centre, Vector3 size)
        {
            GameObject go = CreatePrimitive(PrimitiveType.Cube, parent, material, name);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
        }

        static GameObject CreatePrimitive(PrimitiveType type, GameObject parent, Material material, string name)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            // The CharacterController on the player root does all the colliding.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            material.SetColor(shader.name.StartsWith("Universal") ? "_BaseColor" : "_Color", color);

            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
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
