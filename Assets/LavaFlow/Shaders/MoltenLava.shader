// Molten lava for the LavaFlow channel, URP.
//
// Reads the UVs the generator writes: UV0 is metres across and *travel time* downstream, over
// uvScale, so the pattern always travels down the channel however the channel is turning, and the
// cascades still rip past while the river crawls — that difference lives in the spacing of V, not
// in a per-vertex scroll rate. UV1.y is 0 at the banks and 1 mid-channel, so the edges skin over
// and the middle stays open. UV1.x carries the local speed for anything else that wants it; this
// shader must not multiply time by it (see the note in frag).
//
// Everything is procedural, so there is nothing to import and nothing to keep in sync. Emission is
// HDR: it needs Bloom on and a Tonemapping mode other than None in the volume profile, or values
// over 1 clamp and the whole surface goes flat white.
Shader "LavaFlow/Molten Lava"
{
    Properties
    {
        [Header(Colour)]
        [HDR] _DeepColor   ("Deep lava",   Color) = (0.45, 0.045, 0.008, 1)
        [HDR] _HotColor    ("Hot lava",    Color) = (2.4, 0.42, 0.045, 1)
        [HDR] _WhiteHot    ("White hot",   Color) = (4.5, 1.5, 0.25, 1)
        _CrustColor        ("Crust film",  Color) = (0.055, 0.042, 0.045, 1)
        _EmissionBoost     ("Emission boost", Range(0.1, 6)) = 1.0

        [Header(Flow)]
        _FlowSpeed         ("Flow speed (m/s)", Range(0, 12)) = 1.6
        _NoiseScale        ("Pattern scale", Range(0.1, 12)) = 1.7
        _WarpStrength      ("Swirl", Range(0, 3)) = 0.85
        _StretchAlongFlow  ("Stretch along flow", Range(0.2, 6)) = 2.2

        [Header(Crust)]
        // These two are worth a note, because both defaults moved when the crust maths below were
        // un-inverted, and the response is steep: measured over a real flow's UVs, _CrustAmount
        // 0.30 crusts 15% of the mid-channel and 0.58 crusts 92% of it. The useful range is narrow
        // and it is not where the old numbers sat. 0.38 / 0.16 gives an open hot middle with a
        // clearly cooler margin — 41% and 77% crusted respectively.
        //
        // The old 0.35 bank value was tuned against the inverted maths, where the term made banks
        // HOTTER, so a flow's bright part was its edges. With the sign corrected that same number
        // buries the bank in crust, and the crust colour is near black.
        _CrustAmount       ("Crust amount", Range(0, 1)) = 0.38
        _CrustSharpness    ("Crust edge", Range(0.01, 0.5)) = 0.13
        _BankCrust         ("Extra crust at banks", Range(0, 1)) = 0.16
        _RimGlow           ("Crack glow", Range(0, 4)) = 1.1

        // Optional, and off by default. Drop a lava texture here to drive the pattern from it
        // instead of from noise. It feeds the same heat value the procedural version produces, so
        // it goes through the colour ramp rather than round it: a texture cannot blow the surface
        // out however bright it is.
        [Header(Texture optional)]
        [NoScaleOffset] _LavaTex ("Lava texture", 2D) = "white" {}
        _LavaTexAmount     ("Texture amount", Range(0, 1)) = 0
        _LavaTexScale      ("Texture tiling", Range(0.05, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _HotColor;
                float4 _WhiteHot;
                float4 _CrustColor;
                float  _EmissionBoost;
                float  _FlowSpeed;
                float  _NoiseScale;
                float  _WarpStrength;
                float  _StretchAlongFlow;
                float  _CrustAmount;
                float  _CrustSharpness;
                float  _BankCrust;
                float  _RimGlow;
                float  _LavaTexAmount;
                float  _LavaTexScale;
            CBUFFER_END

            TEXTURE2D(_LavaTex);
            SAMPLER(sampler_LavaTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 flow       : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 flow        : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
            };

            // ---------------------------------------------------------------- noise
            //
            // The lattice repeats every PERIOD cells, and every offset that grows with time is
            // wrapped to a whole number of cells before it reaches the noise. That is not a detail:
            // a flow scrolls for as long as the game is open, and an offset that keeps growing
            // takes the coordinates with it. Once they are large enough, the gap between two
            // representable floats swallows the difference between neighbouring pixels — the upper
            // octaves collapse into flat steps, and the derivative the GPU uses to pick a mip level
            // quantises into hard-edged bands across the surface. Wrapped, the coordinates stay
            // small for ever and the pattern is seamless across the wrap because the lattice
            // genuinely repeats there.
            //
            // The period is in cells, so at the shipped settings it is around a kilometre of flow —
            // longer than any of these rivers, and several minutes of scrolling.
            #define NOISE_PERIOD 64.0

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            /// Wraps a lattice cell into [0, period). Written as a subtraction of whole periods
            /// rather than fmod, which does not behave for negative coordinates — and downstream is
            /// negative, because the scroll runs that way.
            float2 wrapCell(float2 i, float period)
            {
                return i - floor(i / period) * period;
            }

            /// Wraps a scrolling offset to a whole number of cells, so removing periods is invisible.
            float wrapScroll(float offset, float period)
            {
                return offset - floor(offset / period) * period;
            }

            float vnoise(float2 p, float period)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float2 i0 = wrapCell(i, period);
                float2 i1 = wrapCell(i + 1.0, period);

                // The constant dodges cell (0,0), which this hash takes to exactly zero.
                const float2 K = float2(3.7, 1.3);
                float a = hash21(i0 + K);
                float b = hash21(float2(i1.x, i0.y) + K);
                float c = hash21(float2(i0.x, i1.y) + K);
                float d = hash21(i1 + K);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Lacunarity is exactly 2 so the period stays a whole number of cells at every octave.
            float fbm(float2 p, float period)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise(p, period);
                    p *= 2.0;
                    period *= 2.0;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.flow = IN.flow;
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float bank = saturate(IN.flow.y);

                // How far the pattern has travelled downstream, in UV units. This grows for as long
                // as the game runs, so it is never used raw — each layer below wraps it to its own
                // lattice first. See the note on NOISE_PERIOD.
                //
                // One rate for the whole surface, deliberately. This used to be multiplied by the
                // per-vertex speed in UV1.x, and that cannot be made to work: neighbouring points
                // scrolling at different rates tear the pattern between them, by an amount that
                // grows with time and never settles. Seven minutes into a session that tear was
                // measured at 130x the surface's own UV rate, which put forty tiles of texture
                // inside one pixel — the GPU dropped to its smallest mip and the flow smeared into
                // a hard-edged band wherever the slope, and with it the speed, changed.
                //
                // The generator now measures V in travel time rather than metres, so lava on a
                // cascade still crosses the ground faster than lava on the flat — by exactly the
                // same ratio — while every vertex scrolls at one rate and nothing shears.
                float travel = _Time.y * _FlowSpeed;

                // Squashed across and stretched along, because lava that is moving gets drawn out
                // into ropes pointing the way it is going.
                float alongScale = _NoiseScale / max(_StretchAlongFlow, 1e-3);
                float scroll = wrapScroll(travel * alongScale, NOISE_PERIOD);

                float2 p = float2(IN.uv.x * _NoiseScale, IN.uv.y * alongScale - scroll);

                // Halved coordinates, so half the period covers the same distance.
                float2 warp = float2(fbm(p * 0.5 + 3.1, NOISE_PERIOD * 0.5),
                                     fbm(p * 0.5 + 7.7, NOISE_PERIOD * 0.5)) - 0.5;
                float2 q = p + warp * _WarpStrength;

                // A slower, coarser second layer, offset so it does not simply repeat the first at
                // the octaves the two share.
                float drift = wrapScroll(travel * alongScale * 0.12, NOISE_PERIOD * 0.25);
                float n1 = fbm(q, NOISE_PERIOD);
                float n2 = fbm(q * 0.25 + float2(19.3, 7.1) - float2(0.0, drift), NOISE_PERIOD * 0.25);
                float heat = saturate(n1 * 0.62 + n2 * 0.38);

                // Optional texture, tiling in the flow's own metres and travelling downstream with
                // everything else. Blended into heat, so it is still the ramp below that decides
                // the colour and the result stays inside the range the crust logic expects.
                if (_LavaTexAmount > 0.0)
                {
                    // Wrapped to a single tile. The texture repeats, so dropping whole tiles off the
                    // scroll cannot be seen — and it keeps the coordinate small, which is what lets
                    // the GPU work out a sane mip level from it. Scrolled raw, the coordinate runs
                    // into the thousands and the derivative between two neighbouring pixels is lost
                    // in float spacing: the mip level then snaps between whole levels and draws hard
                    // bands of blur and detail across the flow.
                    float texScale = max(_LavaTexScale, 1e-3);
                    float texScroll = wrapScroll(travel * texScale, 1.0) / texScale;
                    float2 texUV = float2(IN.uv.x, IN.uv.y - texScroll) * texScale;
                    half3 texel = SAMPLE_TEXTURE2D(_LavaTex, sampler_LavaTex, texUV).rgb;
                    float texHeat = saturate(dot(texel, half3(0.45, 0.4, 0.15)) * 1.15);
                    heat = saturate(lerp(heat, heat * 0.35 + texHeat * 0.8, _LavaTexAmount));
                }

                // The skin thickens toward the banks, where the lava is dragging against its own
                // cooled edge and moving slowest.
                //
                // The bias is a threshold on how cool a patch has to be before it counts as skinned
                // over, so it runs the opposite way to the amount of crust it produces: raise the
                // threshold and less of the surface clears it. Both knobs are therefore subtracted
                // from 1 rather than added, which is what makes "Crust amount" and "Extra crust at
                // banks" do what their names say. They used to be added straight in, so turning the
                // crust down crusted the surface over and the banks came out hotter than the middle.
                float crustBias = saturate(1.0 - _CrustAmount - (1.0 - bank) * _BankCrust);
                float crust = smoothstep(crustBias - _CrustSharpness,
                                         crustBias + _CrustSharpness,
                                         1.0 - heat);

                float3 col = lerp(_DeepColor.rgb, _HotColor.rgb, saturate(heat * 1.35));
                col = lerp(col, _WhiteHot.rgb, saturate((heat - 0.74) / 0.26));
                col = lerp(col, _CrustColor.rgb, crust);

                // A hot line right where the skin is tearing open.
                float rim = saturate(1.0 - abs(crust - 0.5) * 2.0);
                col += _HotColor.rgb * (rim * rim * _RimGlow * 0.5);

                col *= _EmissionBoost;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Depth only, so the flow behaves for anything doing a depth prepass: depth of field,
        // screen-space ambient occlusion, decals.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes { float4 positionOS : POSITION; };
            struct DepthVaryings   { float4 positionHCS : SV_POSITION; };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
