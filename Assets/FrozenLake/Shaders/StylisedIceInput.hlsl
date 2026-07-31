#ifndef FROZENLAKE_STYLISED_ICE_INPUT_INCLUDED
#define FROZENLAKE_STYLISED_ICE_INPUT_INCLUDED

#include "IceCracks.hlsl"

// One constant buffer shared by every pass. The SRP batcher requires the layout to match exactly
// across passes, so it lives here rather than being repeated per pass.
CBUFFER_START(UnityPerMaterial)
    float4 _DeepColor;
    float4 _ShallowColor;
    float4 _CrackColor;

    float  _MottleSize;

    float  _CrackSizeA;
    float  _CrackWidthA;
    float  _CrackWeightA;

    float  _CrackSizeB;
    float  _CrackWidthB;
    float  _CrackWeightB;

    float  _CrackSharpness;
    float  _CrackWander;
    float  _CrackWanderSize;
    float  _Seed;

    float  _Smoothness;
    float  _CrackSmoothness;
    float  _NormalStrength;
    float  _NormalSampleDistance;
CBUFFER_END

IceCrackParams IceParamsFromMaterial(float3 positionWS)
{
    IceCrackParams p;
    p.cellSize  = float2(_CrackSizeA, _CrackSizeB);
    p.width     = float2(_CrackWidthA, _CrackWidthB);
    p.weight    = float2(_CrackWeightA, _CrackWeightB);
    p.sharpness = _CrackSharpness;
    p.warp      = _CrackWander;
    p.warpSize  = _CrackWanderSize;
    p.seed      = (int)_Seed;
    p.footprint = IceSurfaceFootprint(positionWS);
    return p;
}

// Albedo, smoothness and world normal for the ice at a point. Shared so the lit pass and any
// future pass agree on the surface.
void IceSurface(float3 positionWS, float3 geometricNormalWS,
                out half3 albedo, out half smoothness, out float3 normalWS)
{
    float2 world = positionWS.xz;
    IceCrackParams p = IceParamsFromMaterial(positionWS);

    float2 wander = IceCrackWarp(world, p);
    float  mask   = saturate(IceCrackMaskWarped(world, wander, p));
    float3 relief = IceCrackNormal(world, wander, p, _NormalStrength, _NormalSampleDistance);

    // Mottling in the body of the ice, faded toward flat once a pixel covers more than one blob.
    // Without this it boils into noise in the distance exactly the way the fine cracks would.
    float mottleFade = saturate(_MottleSize / max(p.footprint * 4.0, 1e-5));
    float mottle = lerp(0.5, IceFbm(world / max(_MottleSize, 1e-3), (int)_Seed + 91, 4), mottleFade);

    albedo = lerp(_DeepColor.rgb, _ShallowColor.rgb, mottle);
    albedo = lerp(albedo, _CrackColor.rgb, mask);

    // Clear ice is a mirror; a crack is full of crushed ice and snow, so it is not.
    smoothness = lerp(_Smoothness, _CrackSmoothness, mask);

    normalWS = normalize(relief);
    if (geometricNormalWS.y < 0.0)
        normalWS = -normalWS;
}

#endif // FROZENLAKE_STYLISED_ICE_INPUT_INCLUDED
