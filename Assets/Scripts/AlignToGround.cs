using UnityEngine;

[ExecuteAlways]
public class AlignToGround : MonoBehaviour
{
    [SerializeField] private LayerMask terrainLayer = ~0;
    [SerializeField] private float raycastDistance = 100f;
    [SerializeField] private bool matchTerrainSlope = true;

    private void Start()
    {
        SnapToTerrain();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            SnapToTerrain();
        }
    }

    public void SnapToTerrain()
    {
        // 1. Temporarily disable colliders so the raycast doesn't hit ITSELF
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (var col in colliders) col.enabled = false;

        // 2. Cast down from high above to find terrain surface
        Vector3 rayStart = transform.position + Vector3.up * 20f;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, terrainLayer))
        {
            transform.position = hit.point;

            if (matchTerrainSlope)
            {
                // Align Y-axis to ground normal while preserving original forward heading
                Vector3 currentForward = transform.forward;
                Vector3 projectedForward = Vector3.ProjectOnPlane(currentForward, hit.normal).normalized;
                
                if (projectedForward != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(projectedForward, hit.normal);
                }
            }
        }

        // 3. Re-enable colliders
        foreach (var col in colliders) col.enabled = true;
    }
}