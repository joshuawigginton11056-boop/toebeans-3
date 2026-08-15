using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates four low-poly tree prefabs (Oak, Pine, Cypress, Cedar) from
// procedural geometry only - no imported models, no textures, no license
// to track. Run via Tools > Trees > Generate All Trees.
public static class TreeGenerator
{
    const string RootFolder = "Assets/GeneratedTrees";
    const string PrefabFolder = RootFolder + "/Prefabs";
    const string MeshFolder = RootFolder + "/Meshes";
    const string MaterialFolder = RootFolder + "/Materials";

    struct Piece
    {
        public Mesh mesh;
        public Matrix4x4 transform;
        public bool isFoliage;
    }

    [MenuItem("Tools/Trees/Generate All Trees")]
    public static void GenerateAllTrees()
    {
        EnsureFolder(RootFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);

        Material bark = GetOrCreateMaterial("Bark", new Color(0.36f, 0.25f, 0.16f));

        BuildTree("Oak", bark, GetOrCreateMaterial("Foliage_Oak", new Color(0.29f, 0.50f, 0.15f)), BuildOakPieces());
        BuildTree("Pine", bark, GetOrCreateMaterial("Foliage_Pine", new Color(0.10f, 0.28f, 0.14f)), BuildPinePieces());
        BuildTree("Cypress", bark, GetOrCreateMaterial("Foliage_Cypress", new Color(0.08f, 0.24f, 0.16f)), BuildCypressPieces());
        BuildTree("Cedar", bark, GetOrCreateMaterial("Foliage_Cedar", new Color(0.42f, 0.45f, 0.20f)), BuildCedarPieces());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Generated 4 tree prefabs in {PrefabFolder}");
    }

    // ---- Species shape definitions ----

    static List<Piece> BuildOakPieces()
    {
        var pieces = new List<Piece>();
        pieces.Add(TrunkPiece(bottomRadius: 0.35f, topRadius: 0.28f, height: 2.2f));

        float mainY = 2.2f + 1.8f * 0.7f;
        pieces.Add(SpherePiece(radius: 1.8f, center: new Vector3(0f, mainY, 0f), scale: new Vector3(1.3f, 0.85f, 1.3f)));
        pieces.Add(SpherePiece(radius: 1.0f, center: new Vector3(0.9f, mainY + 0.2f, 0.3f), scale: new Vector3(1f, 0.8f, 1f)));
        pieces.Add(SpherePiece(radius: 1.0f, center: new Vector3(-0.7f, mainY - 0.1f, -0.5f), scale: new Vector3(1f, 0.8f, 1f)));
        return pieces;
    }

    static List<Piece> BuildPinePieces()
    {
        var pieces = new List<Piece>();
        pieces.Add(TrunkPiece(bottomRadius: 0.25f, topRadius: 0.18f, height: 1.0f));

        pieces.Add(ConePiece(bottomRadius: 1.4f, height: 2.2f, baseY: 0.6f));
        pieces.Add(ConePiece(bottomRadius: 1.05f, height: 1.9f, baseY: 1.9f));
        pieces.Add(ConePiece(bottomRadius: 0.7f, height: 1.6f, baseY: 3.2f));
        return pieces;
    }

    static List<Piece> BuildCypressPieces()
    {
        var pieces = new List<Piece>();
        pieces.Add(TrunkPiece(bottomRadius: 0.22f, topRadius: 0.18f, height: 0.8f));
        pieces.Add(ConePiece(bottomRadius: 0.55f, height: 6.0f, baseY: 0.6f));
        return pieces;
    }

    static List<Piece> BuildCedarPieces()
    {
        var pieces = new List<Piece>();
        pieces.Add(TrunkPiece(bottomRadius: 0.4f, topRadius: 0.3f, height: 2.6f));

        pieces.Add(FrustumPiece(bottomRadius: 1.6f, topRadius: 1.2f, height: 0.5f, baseY: 1.8f));
        pieces.Add(FrustumPiece(bottomRadius: 1.3f, topRadius: 0.95f, height: 0.45f, baseY: 2.7f));
        pieces.Add(FrustumPiece(bottomRadius: 1.0f, topRadius: 0.7f, height: 0.4f, baseY: 3.5f));
        pieces.Add(FrustumPiece(bottomRadius: 0.65f, topRadius: 0.35f, height: 0.35f, baseY: 4.2f));
        return pieces;
    }

    // ---- Piece helpers ----

    static Piece TrunkPiece(float bottomRadius, float topRadius, float height)
    {
        return new Piece
        {
            mesh = BuildTaperedCylinder(bottomRadius, topRadius, height),
            transform = Matrix4x4.identity,
            isFoliage = false
        };
    }

    static Piece ConePiece(float bottomRadius, float height, float baseY)
    {
        return new Piece
        {
            mesh = BuildTaperedCylinder(bottomRadius, 0f, height),
            transform = Matrix4x4.Translate(new Vector3(0f, baseY, 0f)),
            isFoliage = true
        };
    }

    static Piece FrustumPiece(float bottomRadius, float topRadius, float height, float baseY)
    {
        return new Piece
        {
            mesh = BuildTaperedCylinder(bottomRadius, topRadius, height),
            transform = Matrix4x4.Translate(new Vector3(0f, baseY, 0f)),
            isFoliage = true
        };
    }

    static Piece SpherePiece(float radius, Vector3 center, Vector3 scale)
    {
        return new Piece
        {
            mesh = GetSphereMesh(),
            // built-in sphere has radius 0.5, so scale by (radius / 0.5) on top of the requested squash
            transform = Matrix4x4.TRS(center, Quaternion.identity, scale * (radius / 0.5f)),
            isFoliage = true
        };
    }

    // ---- Assembly: combine pieces into one prefab with a bark submesh + a foliage submesh ----

    static void BuildTree(string speciesName, Material barkMat, Material foliageMat, List<Piece> pieces)
    {
        var barkCombine = new List<CombineInstance>();
        var foliageCombine = new List<CombineInstance>();

        foreach (var piece in pieces)
        {
            var ci = new CombineInstance { mesh = piece.mesh, transform = piece.transform };
            (piece.isFoliage ? foliageCombine : barkCombine).Add(ci);
        }

        var barkMesh = new Mesh();
        barkMesh.CombineMeshes(barkCombine.ToArray(), mergeSubMeshes: true, useMatrices: true);

        var foliageMesh = new Mesh();
        foliageMesh.CombineMeshes(foliageCombine.ToArray(), mergeSubMeshes: true, useMatrices: true);

        var finalCombine = new[]
        {
            new CombineInstance { mesh = barkMesh, transform = Matrix4x4.identity },
            new CombineInstance { mesh = foliageMesh, transform = Matrix4x4.identity }
        };

        var finalMesh = new Mesh { name = speciesName };
        finalMesh.CombineMeshes(finalCombine, mergeSubMeshes: false, useMatrices: true);
        finalMesh.RecalculateBounds();

        string meshPath = $"{MeshFolder}/{speciesName}Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(finalMesh, meshPath);

        var go = new GameObject(speciesName);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = finalMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { barkMat, foliageMat };

        string prefabPath = $"{PrefabFolder}/{speciesName}.prefab";
        AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
    }

    // ---- Low-level mesh builders ----

    // Built from Unity's own primitive meshes so winding/normals are guaranteed
    // correct - we only ever move vertex positions, never touch the triangle
    // index buffers.

    static Mesh BuildTaperedCylinder(float bottomRadius, float topRadius, float height)
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Mesh mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(temp);

        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            float t = (v.y + 1f) * 0.5f; // source cylinder spans y in [-1, 1]
            float radius = Mathf.Lerp(bottomRadius, topRadius, t);
            float srcRadius = Mathf.Sqrt(v.x * v.x + v.z * v.z); // 0.5 on the rim, 0 on cap centers
            float scale = srcRadius > 0.001f ? radius / 0.5f : 0f;
            verts[i] = new Vector3(v.x * scale, t * height, v.z * scale);
        }
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh GetSphereMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(temp);
        return mesh;
    }

    // ---- Asset plumbing ----

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        mat.color = color;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
