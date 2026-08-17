using UnityEngine;

// Adds movement to a URP/Lit water material without any external textures or
// Shader Graph: bakes a seamless ripple normal map at runtime from a sum of
// sine waves (integer frequencies guarantee it tiles perfectly), scrolls it
// over time, and gently bobs the whole plane up and down.
[RequireComponent(typeof(Renderer))]
public class WaterAnimator : MonoBehaviour
{
    [Header("Ripple")]
    public Vector2 scrollSpeed = new Vector2(0.03f, 0.02f);
    [Range(0f, 2f)] public float normalStrength = 0.4f;
    public int noiseTextureSize = 128;

    [Header("Bob")]
    public float bobHeight = 0.15f;
    public float bobSpeed = 0.5f;

    private Material material;
    private Vector3 startPosition;

    private void Start()
    {
        material = GetComponent<Renderer>().material; // .material (not sharedMaterial) makes a per-instance copy
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_BumpMap", GenerateRippleNormalMap());
        material.SetFloat("_BumpScale", normalStrength);
        startPosition = transform.position;
    }

    private void Update()
    {
        Vector2 offset = new Vector2(Time.time * scrollSpeed.x, Time.time * scrollSpeed.y);
        material.SetTextureOffset("_BumpMap", offset);

        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = startPosition + new Vector3(0f, bob, 0f);
    }

    // Integer frequency coefficients here are what guarantee this tiles with
    // zero seam - each term repeats exactly once (or an integer number of
    // times) across the 0..1 texture range in u and v.
    private static float SampleHeight(float u, float v)
    {
        float h = 0f;
        h += 0.5f * Mathf.Sin(2f * Mathf.PI * (3f * u + 2f * v));
        h += 0.3f * Mathf.Sin(2f * Mathf.PI * (-4f * u + 1f * v + 0.3f));
        h += 0.2f * Mathf.Sin(2f * Mathf.PI * (7f * u - 5f * v + 0.6f));
        return h;
    }

    private Texture2D GenerateRippleNormalMap()
    {
        int size = noiseTextureSize;
        var heights = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                heights[x, y] = SampleHeight((float)x / size, (float)y / size);

        var tex = new Texture2D(size, size, TextureFormat.RGB24, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float hL = heights[(x - 1 + size) % size, y];
                float hR = heights[(x + 1) % size, y];
                float hD = heights[x, (y - 1 + size) % size];
                float hU = heights[x, (y + 1) % size];

                Vector3 normal = new Vector3(-(hR - hL), -(hU - hD), 1f).normalized;
                tex.SetPixel(x, y, new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f));
            }
        }

        tex.Apply();
        return tex;
    }
}
