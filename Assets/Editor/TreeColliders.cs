using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The tree models ship with no collider of any kind, so a kart drives straight
// through the forest. This fits one CapsuleCollider per tree prefab, sized off
// the mesh rather than by hand, and writes it to the prefab asset so every
// instance already scattered in the scene picks it up without being touched.
//
// Capsules, not MeshColliders: these meshes are imported non-readable, so a
// MeshCollider would have nothing to build from in a player, and a thousand-odd
// concave colliders is a bill nobody wants to pay for scenery.
//
// Three things make the fit non-obvious.
//
// The radius is measured only across the band a kart body can actually sweep -
// the first KartBand metres above the base - and not the whole tree. These
// canopies are several times wider than what sits at bumper height, and a
// radius taken from the whole mesh would stop a kart out in clear air. Measured
// this way the capsule also stays inside the silhouette at every height,
// because on all of these models the canopy is wider than the base.
//
// Within that band the outermost ring of verts is the flat disc the models
// blend into the ground with - a single fan sitting a clear gap beyond the real
// geometry. RadiusPercentile is what steps over it, and p90 lands under the
// disc on every prefab in this scene while still clearing the real trunk.
//
// The capsule is then hung radius-deep below the mesh base. A capsule's bottom
// cap curves inward, so one ending at the base would be at its narrowest
// exactly where the kart meets it; dropping the cap underground puts the full
// radius at bumper height. Same reasoning as the barrier wall's Embed.
public static class TreeColliders
{
    // Height above the base that a kart body can reach, in metres.
    const float KartBand = 1.5f;

    // Radial percentile within that band. See the note above about the ground disc.
    const float RadiusPercentile = 0.90f;

    const string TreeGroup = "ScatteredTrees";

    [MenuItem("Tools/Trees/Fit Colliders To Tree Prefabs")]
    public static void Fit() { Run(apply: true); }

    [MenuItem("Tools/Trees/Report Tree Collider Fit")]
    public static void Report() { Run(apply: false); }

    static void Run(bool apply)
    {
        // Scale matters: the band is a world distance but the mesh is measured in
        // local units, and these prefabs are instanced anywhere from 200x to 570x.
        // The median scale of what is actually placed is the honest conversion.
        Dictionary<GameObject, List<float>> scales = ScalesByPrefab();
        if (scales.Count == 0)
        {
            Debug.LogWarning($"TreeColliders: no prefab instances found under '{TreeGroup}'.");
            return;
        }

        var log = new System.Text.StringBuilder(
            apply ? "TreeColliders - fitted:\n" : "TreeColliders - report only (nothing written):\n");

        foreach (KeyValuePair<GameObject, List<float>> entry in scales.OrderBy(e => e.Key.name))
        {
            GameObject prefab = entry.Key;
            List<float> placed = entry.Value;
            placed.Sort();
            float scale = placed[placed.Count / 2];

            MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
            {
                log.AppendLine($"  {prefab.name}: no mesh, skipped");
                continue;
            }

            if (!Measure(filter.sharedMesh, scale, out Vector3 center, out float radius, out float height))
            {
                log.AppendLine($"  {prefab.name}: no vertices in the kart band, skipped");
                continue;
            }

            log.AppendLine(
                $"  {prefab.name}: {placed.Count} placed, median scale {scale:F0}, " +
                $"radius {radius * scale:F2} m, standing {height * scale:F1} m");

            if (apply) Write(prefab, filter, center, radius, height);
        }

        if (apply) AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    // Every distinct source prefab under the scatter group, with the scale of
    // each instance placed from it.
    static Dictionary<GameObject, List<float>> ScalesByPrefab()
    {
        var found = new Dictionary<GameObject, List<float>>();

        GameObject group = GameObject.Find(TreeGroup);
        if (group == null) return found;

        foreach (Transform child in group.transform)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (source == null) continue;

            if (!found.TryGetValue(source, out List<float> list))
            {
                list = new List<float>();
                found[source] = list;
            }
            list.Add(child.lossyScale.x);
        }

        return found;
    }

    // Capsule in the mesh's own local space, so it scales with each instance.
    static bool Measure(Mesh mesh, float scale, out Vector3 center, out float radius, out float height)
    {
        center = Vector3.zero;
        radius = 0f;
        height = 0f;

        Vector3[] verts = mesh.vertices;
        Bounds bounds = mesh.bounds;
        float baseY = bounds.min.y;
        float standing = bounds.size.y;
        if (verts.Length == 0 || standing <= 0f) return false;

        // The trunk is not always on the pivot. Averaging the lowest slice finds
        // the axis the tree actually stands on, so the capsule is not offset from
        // a tree whose mesh was baked away from its origin.
        Vector3[] lowest = verts.Where(v => v.y <= baseY + standing * 0.08f).ToArray();
        float axisX = lowest.Length > 0 ? lowest.Average(v => v.x) : bounds.center.x;
        float axisZ = lowest.Length > 0 ? lowest.Average(v => v.z) : bounds.center.z;

        float[] radii = verts
            .Where(v => v.y <= baseY + KartBand / scale)
            .Select(v => new Vector2(v.x - axisX, v.z - axisZ).magnitude)
            .OrderBy(r => r)
            .ToArray();
        if (radii.Length == 0) return false;

        int index = Mathf.Clamp(Mathf.RoundToInt((radii.Length - 1) * RadiusPercentile), 0, radii.Length - 1);
        radius = radii[index];
        if (radius <= 0f) return false;

        // Top stays on the mesh top; the bottom cap is buried a radius deep.
        height = standing + radius;
        center = new Vector3(axisX, bounds.center.y - radius * 0.5f, axisZ);
        return true;
    }

    // Written through prefab contents so the change lands on the asset and every
    // instance in every scene inherits it.
    static void Write(GameObject prefab, MeshFilter filter, Vector3 center, float radius, float height)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // The collider goes on whichever object carries the mesh, so the
            // numbers just measured are in the space it reads them in.
            Transform target = contents.transform;
            MeshFilter inContents = contents.GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(m => m.sharedMesh == filter.sharedMesh);
            if (inContents != null) target = inContents.transform;

            CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = target.gameObject.AddComponent<CapsuleCollider>();

            capsule.direction = 1; // Y
            capsule.center = center;
            capsule.radius = radius;
            capsule.height = height;
            capsule.isTrigger = false;

            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
