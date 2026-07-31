#ifndef FROZENLAKE_ICE_CRACKS_INCLUDED
#define FROZENLAKE_ICE_CRACKS_INCLUDED

// Procedural crack network for the frozen lake surface.
//
// Everything is evaluated from world-space XZ, so the pattern does not stretch when the plane is
// scaled, does not tile, and stays put when the object moves. No textures are involved, which also
// keeps the asset out of Git LFS.
//
// Nothing in here depends on URP or on any Unity include, so it can be compiled and checked on its
// own. Distances are in metres.

struct IceCrackParams
{
    // Two crack layers. x = the coarse network, y = the finer one laid over it.
    float2 cellSize;    // metres between cracks, per layer
    float2 width;       // crack width as a fraction of the cell, per layer
    float2 weight;      // how strongly each layer shows, per layer
    float  sharpness;   // higher = tighter, harder-edged lines
    float  warp;        // how far the pattern wanders, in metres
    float  warpSize;    // metres per unit of warp noise
    int    seed;

    // Width of one screen pixel on the surface, in metres. A crack thinner than a pixel cannot be
    // drawn without aliasing, so it is faded out instead of left to shimmer. Set this from the
    // world-space derivatives; see IceSurfaceFootprint.
    float  footprint;
};

// Roughly how much world space one pixel covers, for the fade above.
float IceSurfaceFootprint(float3 positionWS)
{
    float3 dx = ddx(positionWS);
    float3 dz = ddy(positionWS);
    return max(length(dx), length(dz));
}

// Deterministic per-cell hash. Unsigned throughout so the wraparound is well defined.
float2 IceHash2(int2 c, int seed)
{
    uint h = (uint)c.x * 374761393u + (uint)c.y * 668265263u + (uint)seed * 1274126177u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h = h ^ (h >> 16);
    return float2(h & 0xFFFFu, (h >> 16) & 0xFFFFu) * (1.0 / 65535.0);
}

float IceValueNoise(float2 p, int seed)
{
    float2 fl = floor(p);
    int2 i = (int2)fl;
    float2 f = p - fl;
    f = f * f * (3.0 - 2.0 * f);

    float v00 = IceHash2(i + int2(0, 0), seed).x;
    float v10 = IceHash2(i + int2(1, 0), seed).x;
    float v01 = IceHash2(i + int2(0, 1), seed).x;
    float v11 = IceHash2(i + int2(1, 1), seed).x;

    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

float IceFbm(float2 p, int seed, int octaves)
{
    float total = 0.0;
    float amp = 1.0;
    float freq = 1.0;
    float norm = 0.0;

    for (int o = 0; o < octaves; o++)
    {
        total += IceValueNoise(p * freq, seed + o * 101) * amp;
        norm += amp;
        amp *= 0.5;
        freq *= 2.03;
    }
    return total / norm;
}

// F2 - F1 over a Voronoi grid: zero exactly on a cell boundary and rising inward. That gives one
// unbroken line everywhere two cells meet, which is the shape a pressure crack actually makes.
// Plain nearest-point distance would give blobs instead.
float IceWorleyEdge(float2 p, int seed)
{
    int2 ip = (int2)floor(p);
    float f1 = 1e9;
    float f2 = 1e9;

    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 c = ip + int2(dx, dy);
            float2 site = (float2)c + IceHash2(c, seed);
            float d = length(site - p);

            if (d < f1) { f2 = f1; f1 = d; }
            else        { f2 = min(f2, d); }
        }
    }
    return f2 - f1;
}

// The domain warp is smooth and slow, so it can be computed once per pixel and reused for the
// gradient taps below rather than recomputed three times.
float2 IceCrackWarp(float2 world, IceCrackParams p)
{
    float2 q = world / max(p.warpSize, 1e-4);
    float a = IceFbm(q, p.seed + 7, 3) - 0.5;
    float b = IceFbm(q + float2(5.1, -3.7), p.seed + 19, 3) - 0.5;
    return float2(a, b) * p.warp;
}

// Crack coverage at a point, 0 on clear ice and 1 in the middle of a crack.
float IceCrackMaskWarped(float2 world, float2 warp, IceCrackParams p)
{
    float mask = 0.0;

    for (int i = 0; i < 2; i++)
    {
        // Warp is applied in metres, before dividing into cells, so both layers wander together
        // as one sheet rather than sliding against each other.
        float cell = max(p.cellSize[i], 1e-3);
        float e = IceWorleyEdge((world + warp) / cell, p.seed + i * 977);
        // "line" is a reserved word in HLSL, hence "band".
        float band = pow(saturate(1.0 - e / max(p.width[i], 1e-4)), p.sharpness);

        // Fade a layer out once its cracks are narrower than a pixel. Without this the fine layer
        // breaks into crawling dashes toward the horizon.
        float widthMeters = cell * p.width[i];
        float fade = saturate(widthMeters / max(p.footprint * 2.0, 1e-5));

        mask = max(mask, band * p.weight[i] * fade);
    }
    return mask;
}

float IceCrackMask(float2 world, IceCrackParams p)
{
    return IceCrackMaskWarped(world, IceCrackWarp(world, p), p);
}

// Surface normal for the crack relief, sampled in world space rather than with ddx/ddy so it does
// not fall apart at grazing angles or in the distance. Assumes a roughly horizontal surface, which
// is what a lake is.
float3 IceCrackNormal(float2 world, float2 warp, IceCrackParams p, float strength, float eps)
{
    eps = max(eps, 1e-3);
    float c = IceCrackMaskWarped(world, warp, p);
    float dx = IceCrackMaskWarped(world + float2(eps, 0.0), warp, p) - c;
    float dz = IceCrackMaskWarped(world + float2(0.0, eps), warp, p) - c;
    return normalize(float3(-dx * strength, eps, -dz * strength));
}

#endif // FROZENLAKE_ICE_CRACKS_INCLUDED
