// Semantic ("representative") density cue: a small charged plasma orb.
//   Pass 1 (core):  dark indigo glass with a few THICK, bright, crawling electric arcs
//                   (domain-warped ridged noise, two layers), a hot view-facing core
//                   glow, a crisp rim, plus flicker and occasional per-orb surges.
//   Pass 2 (halo):  an inflated additive shell that glows just outside the
//                   silhouette and fades out -- gives each orb a visible glow WITHOUT
//                   needing post-processing bloom (the study cameras have post off).
// Per-instance phase/float seeded from the object's world position so 100 orbs never
// pulse in sync. No textures; SRP-Batcher + GPU-instancing + single-pass-stereo
// friendly. Queue is Transparent so the additive halo is drawn after the skybox
// (an additive pass in the opaque queue would be painted over by the sky).
Shader "EmbodiedDataVR/ElectricOrb"
{
    Properties
    {
        [Header(Core)]
        _CoreColor ("Glass Colour", Color) = (0.008, 0.015, 0.07, 1)
        [HDR] _CenterColor ("Inner Glow Colour (HDR)", Color) = (0.08, 0.2, 0.75, 1)
        _CenterPower ("Inner Glow Falloff", Range(0.5, 8)) = 3.5
        [HDR] _RimColor ("Rim Colour (HDR)", Color) = (0.35, 0.75, 1.6, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3

        [Header(Arcs)]
        [HDR] _ArcColor ("Arc Colour (HDR)", Color) = (0.55, 0.95, 1.9, 1)
        [HDR] _ArcHotColor ("Arc Hot Centre (HDR)", Color) = (2.2, 2.4, 2.6, 1)
        _ArcScale ("Arc Scale", Range(0.5, 12)) = 1.8
        _ArcSpeed ("Arc Speed", Range(0, 6)) = 1.8
        _ArcWidth ("Arc Width", Range(0.01, 0.4)) = 0.055
        _ArcWarp ("Arc Wiggle", Range(0, 4)) = 2.2
        _ArcIntensity ("Arc Intensity", Range(0, 6)) = 2.2

        [Header(Life)]
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.35
        _SurgeChance ("Surge Frequency", Range(0, 1)) = 0.35
        _SurgeBoost ("Surge Boost", Range(0, 4)) = 1.6
        _BobAmplitude ("Float Amplitude (m)", Range(0, 0.2)) = 0.04
        _BobSpeed ("Float Speed", Range(0, 3)) = 0.7

        [Header(Halo)]
        _HaloSize ("Halo Size (x radius)", Range(0, 2)) = 0.6
        [HDR] _HaloColor ("Halo Colour (HDR)", Color) = (0.25, 0.6, 1.4, 1)
        _HaloFalloff ("Halo Falloff", Range(0.5, 6)) = 2.6
        _HaloIntensity ("Halo Intensity", Range(0, 3)) = 0.9
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _CoreColor;
        half4 _CenterColor;
        half _CenterPower;
        half4 _RimColor;
        half _RimPower;
        half4 _ArcColor;
        half4 _ArcHotColor;
        half _ArcScale;
        half _ArcSpeed;
        half _ArcWidth;
        half _ArcWarp;
        half _ArcIntensity;
        half _FlickerAmount;
        half _SurgeChance;
        half _SurgeBoost;
        half _BobAmplitude;
        half _BobSpeed;
        half _HaloSize;
        half4 _HaloColor;
        half _HaloFalloff;
        half _HaloIntensity;
    CBUFFER_END

    float hash13(float3 p)
    {
        p = frac(p * 0.1031);
        p += dot(p, p.zyx + 31.32);
        return frac((p.x + p.y) * p.z);
    }

    float hash11(float x) { return frac(sin(x * 127.1) * 43758.5453); }

    float noise1(float x)
    {
        float i = floor(x), f = frac(x);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(hash11(i), hash11(i + 1.0), f);
    }

    float valueNoise(float3 p)
    {
        float3 i = floor(p);
        float3 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float n000 = hash13(i + float3(0,0,0));
        float n100 = hash13(i + float3(1,0,0));
        float n010 = hash13(i + float3(0,1,0));
        float n110 = hash13(i + float3(1,1,0));
        float n001 = hash13(i + float3(0,0,1));
        float n101 = hash13(i + float3(1,0,1));
        float n011 = hash13(i + float3(0,1,1));
        float n111 = hash13(i + float3(1,1,1));
        return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
    }

    // Per-object seed from the object's world position (stable while it exists).
    float ObjectSeed()
    {
        float3 originWS = GetObjectToWorldMatrix()._m03_m13_m23;
        return hash13(originWS * 7.13 + 0.5);
    }

    float3 BobOffset(float seed)
    {
        float t = _Time.y * _BobSpeed;
        float s = seed * 6.2831;
        return float3(sin(t * 0.7 + s * 1.3) * 0.35,
                      sin(t + s),
                      cos(t * 0.6 + s * 2.1) * 0.35) * _BobAmplitude;
    }

    // 1 = normal, rises during a surge, dips during flicker.
    float Energy(float seed)
    {
        float t = _Time.y;
        float flicker = 1.0 - _FlickerAmount * noise1(t * 18.0 + seed * 91.0)
                                             * noise1(t * 5.3 + seed * 17.0);
        float surge = smoothstep(1.0 - _SurgeChance * 0.35, 1.0, noise1(t * 1.3 + seed * 43.0));
        return flicker * (1.0 + surge * _SurgeBoost);
    }

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS   : TEXCOORD0;
        float3 viewDirWS  : TEXCOORD1;
        float3 positionOS : TEXCOORD2;
        nointerpolation float seed : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings VertCommon(Attributes IN, float inflate)
    {
        Varyings OUT = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(IN);
        UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

        float seed = ObjectSeed();
        float3 posOS = IN.positionOS.xyz * (1.0 + inflate);
        float3 positionWS = TransformObjectToWorld(posOS) + BobOffset(seed);

        OUT.positionCS = TransformWorldToHClip(positionWS);
        OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
        OUT.positionOS = IN.positionOS.xyz;
        OUT.seed = seed;
        return OUT;
    }

    // One layer of electric filaments on the sphere surface: thin bright lines where a
    // domain-warped noise field crosses 0.5. Returns (glow, hot core of the line).
    float2 ArcLayer(float3 dir, float scale, float speed, float seed, float t)
    {
        float3 p = dir * scale + seed * 13.0;
        float3 warp = float3(valueNoise(p * 0.8 + t * 0.35),
                             valueNoise(p * 0.8 + 17.3 - t * 0.3),
                             valueNoise(p * 0.8 + 31.7 + t * 0.25));
        float n = valueNoise(p + (warp - 0.5) * _ArcWarp + float3(0, t * speed, 0));
        float d = abs(n - 0.5);                         // distance to the filament centre
        float glow = saturate(1.0 - d / _ArcWidth);
        glow *= glow;
        float hot = saturate(1.0 - d / (_ArcWidth * 0.25));
        return float2(glow, hot * hot);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        // Halo shell (drawn first by URP: SRPDefaultUnlit precedes UniversalForward).
        Pass
        {
            Name "Halo"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertHalo
            #pragma fragment fragHalo
            #pragma multi_compile_instancing
            #pragma target 4.5

            Varyings vertHalo(Attributes IN) { return VertCommon(IN, _HaloSize); }

            half4 fragHalo(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                // Screen-space distance from the orb centre, in core radii: on the
                // inflated shell's front face, r = R * sqrt(1 - (n.v)^2).
                float R = 1.0 + _HaloSize;
                float ndv = saturate(dot(n, v));
                float r = R * sqrt(saturate(1.0 - ndv * ndv));
                float g = saturate((R - r) / max(R - 1.0, 1e-3));   // 1 at the silhouette, 0 at the shell edge
                g = pow(g, _HaloFalloff);
                g *= smoothstep(0.55, 1.0, r);                       // core covers the inside anyway

                float e = Energy(IN.seed);
                return half4(_HaloColor.rgb * g * _HaloIntensity * e, 1);
            }
            ENDHLSL
        }

        // Core.
        Pass
        {
            Name "Core"
            Tags { "LightMode"="UniversalForward" }
            Blend Off
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertCore
            #pragma fragment fragCore
            #pragma multi_compile_instancing
            #pragma target 4.5

            Varyings vertCore(Attributes IN) { return VertCommon(IN, 0.0); }

            half4 fragCore(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                float ndv = saturate(dot(n, v));
                float3 dir = normalize(IN.positionOS);
                float t = _Time.y;
                float e = Energy(IN.seed);

                half3 col = _CoreColor.rgb;

                // Hot plasma centre, facing the viewer.
                col += _CenterColor.rgb * pow(ndv, _CenterPower) * 0.8 * e;

                // Two filament layers at different scales/speeds.
                float2 a1 = ArcLayer(dir, _ArcScale, _ArcSpeed, IN.seed, t);
                float2 a2 = ArcLayer(dir.zxy, _ArcScale * 1.7, -_ArcSpeed * 1.3, IN.seed + 0.37, t * 1.2);
                float glow = max(a1.x, a2.x * 0.7);
                float hot = max(a1.y, a2.y * 0.6);
                col += (_ArcColor.rgb * glow + _ArcHotColor.rgb * hot) * _ArcIntensity * e;

                // Crisp charged rim.
                col += _RimColor.rgb * pow(1.0 - ndv, _RimPower) * e;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
