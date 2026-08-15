using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SnapSplineToTerrain : EditorWindow
{
    [MenuItem("Tools/Subdivide and Snap Spline to Terrain")]
    public static void SubdivideAndSnap()
    {
        GameObject selectedObj = Selection.activeGameObject;
        if (selectedObj == null)
        {
            Debug.LogError("Please select a GameObject with a SplineContainer.");
            return;
        }

        SplineContainer container = selectedObj.GetComponent<SplineContainer>();
        if (container == null)
        {
            Debug.LogError("Selected object does not have a SplineContainer component.");
            return;
        }

        Undo.RecordObject(container, "Subdivide and Snap Spline");

        Spline spline = container.Spline;

        // Step 1: Sample points along the spline and double the knot resolution
        int currentKnotCount = spline.Count;
        int targetKnotCount = currentKnotCount * 2; // Doubles the resolution
        
        Vector3[] sampledLocalPositions = new Vector3[targetKnotCount];

        for (int i = 0; i < targetKnotCount; i++)
        {
            float t = (float)i / (targetKnotCount - (spline.Closed ? 0 : 1));
            // Evaluate position along the spline (returns local coordinates)
            sampledLocalPositions[i] = spline.EvaluatePosition(t);
        }

        // Clear existing knots and rebuild with new resolution
        spline.Clear();
        foreach (Vector3 localPos in sampledLocalPositions)
        {
            spline.Add(new BezierKnot((float3)localPos));
        }

        // Step 2: Raycast every knot straight down to snap to the terrain
        for (int i = 0; i < spline.Count; i++)
        {
            BezierKnot knot = spline[i];
            Vector3 worldPos = selectedObj.transform.TransformPoint(knot.Position);

            Vector3 rayStart = new Vector3(worldPos.x, worldPos.y + 500f, worldPos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1000f))
            {
                Vector3 localHitPos = selectedObj.transform.InverseTransformPoint(hit.point);
                knot.Position = (float3)localHitPos;
                spline[i] = knot;
            }
        }

        EditorUtility.SetDirty(container);
        Debug.Log($"Spline subdivided and snapped! New knot count: {spline.Count}");
    }
}