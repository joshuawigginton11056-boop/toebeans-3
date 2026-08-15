using UnityEngine;
using UnityEditor;

public class CubemapBaker : ScriptableWizard
{
    public Transform renderFromPosition;
    public Cubemap cubemap;

    [MenuItem("Tools/Render Cubemap")]
    static void RenderCubemap()
    {
        ScriptableWizard.DisplayWizard<CubemapBaker>("Render Cubemap", "Render");
    }

    void OnWizardUpdate()
    {
        helpString = "Select position and target Cubemap asset";
        isValid = (renderFromPosition != null) && (cubemap != null);
    }

    void OnWizardCreate()
    {
        GameObject go = new GameObject("CubemapCamera");
        Camera cam = go.AddComponent<Camera>();
        go.transform.position = renderFromPosition.position;
        cam.RenderToCubemap(cubemap);
        DestroyImmediate(go);
    }
}
