// Semantic ("representative") density cue, v3 (M53, supervisor feedback): a small
// yellow SPARK (matched to Sources/spark_reference.png) -- a hot yellow-white ball
// surrounded by many thin, straight, tapering spark streaks that flicker and change length, like the sparks of an electrical discharge.
//   Pass 1 (rays): drawn on an inflated shell around the ball. For each pixel the view
//                  ray's closest approach to the ball centre gives a radius + angle in
//                  the view plane, so the rays always fan out around the ball as seen
//                  from the eye (works per eye in VR). Additive, no post-processing needed.
//   Pass 2 (core): opaque yellow ball, white-hot centre, orange rim, flicker.
// Per-orb seed from the object's world position (no two orbs flicker in sync). No
// textures; SRP-Batcher + GPU-instancing + single-pass-stereo friendly.
Shader "EmbodiedDataVR/SparkOrb"
{
    Properties
    {
        [Header(Core)]
        [HDR] _CoreHot ("Centre Colour (HDR)", Color) = (1.0, 0.98, 0.85, 1)
        [HDR] _CoreColor ("Ball Colour (HDR)", Color) = (1.0, 0.75, 0.08, 1)
        [HDR] _RimColor ("Rim Colour (HDR)", Color) = (0.95, 0.4, 0.02, 1)
        _CentrePower ("Centre Falloff", Range(0.5, 8)) = 3.0
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2

        [Header(Spark Rays)]
        _RayCount ("Ray Count", Range(3, 24)) = 18
        _RayLength ("Ray Length (x radius)", Range(1, 4)) = 3.0
        _RayWidth ("Ray Width At Base", Range(0.02, 1)) = 0.28
        _RayJag ("Ray Jaggedness", Range(0, 1)) = 0.08
        [HDR] _RayBaseColor ("Ray Base Colour (HDR)", Color) = (1.0, 0.95, 0.72, 1)
        [HDR] _RayTipColor ("Ray Tip Colour (HDR)", Color) = (1.0, 0.5, 0.06, 1)
        _RayIntensity ("Ray Intensity", Range(0, 4)) = 1.15
        _GlowIntensity ("Soft Glow Intensity", Range(0, 2)) = 0.3
        _SpinSpeed ("Spin Speed", Range(0, 2)) = 0.25

        [Header(Life)]
        _FlickerSpeed ("Flicker Speed", Range(0, 30)) = 14
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.35
        _BobAmplitude ("Float Amplitude (m)", Range(0, 0.2)) = 0.04
        _BobSpeed ("Float Speed", Range(0, 3)) = 0.7
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _CoreHot;
        half4 _CoreColor;
        half4 _RimColor;
        half _CentrePower;
        half _RimPower;
        half _RayCount;
        half _RayLength;
        half _RayWidth;
        half _RayJag;
        half4 _RayBaseColor;
        half4 _RayTipColor;
        half _RayIntensity;
        half _GlowIntensity;
        half _SpinSpeed;
        half _FlickerSpeed;
        half _FlickerAmount;
        half _BobAmplitude;
        half _BobSpeed;
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

    float3 ObjectOriginWS() { return GetObjectToWorldMatrix()._m03_m13_m23; }
    float ObjectRadiusWS()  { return 0.5 * length(GetObjectToWorldMatrix()._m00_m10_m20); } // unit sphere mesh r = 0.5
    float ObjectSeed()      { return hash13(ObjectOriginWS() * 7.13 + 0.5); }

    float3 BobOffset(float seed)
    {
        float t = _Time.y * _BobSpeed;
        float s = seed * 6.2831;
        return float3(sin(t * 0.7 + s * 1.3) * 0.35, sin(t + s), cos(t * 0.6 + s * 2.1) * 0.35) * _BobAmplitude;
    }

    float Flicker(float seed)
    {
        float t = _Time.y * _FlickerSpeed;
        return 1.0 - _FlickerAmount * noise1(t + seed * 91.0) * noise1(t * 0.37 + seed * 17.0);
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
        float3 positionWS : TEXCOORD1;
        float3 centreWS   : TEXCOORD2;
        // nointerpolation: the seed feeds hash functions that amplify the tiny
        // interpolation error between vertices into per-pixel noise (grainy rays).
        nointerpolation float2 seedRadius : TEXCOORD3;   // x = seed, y = ball radius (world)
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
        float3 bob = BobOffset(seed);
        float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz * inflate) + bob;

        OUT.positionCS = TransformWorldToHClip(positionWS);
        OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
        OUT.positionWS = positionWS;
        OUT.centreWS = ObjectOriginWS() + bob;
        OUT.seedRadius = float2(seed, ObjectRadiusWS());
        return OUT;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        // Spark rays (SRPDefaultUnlit is drawn before UniversalForward in URP).
        Pass
        {
            Name "Rays"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertRays
            #pragma fragment fragRays
            #pragma multi_compile_instancing
            #pragma target 4.5

            Varyings vertRays(Attributes IN) { return VertCommon(IN, _RayLength); }

            half4 fragRays(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float seed = IN.seedRadius.x;
                float radius = max(IN.seedRadius.y, 1e-4);
                float3 camPos = GetCameraPositionWS();
                float3 d = normalize(IN.positionWS - camPos);
                float3 toC = IN.centreWS - camPos;
                float3 closest = camPos + d * dot(toC, d);
                float3 offWS = closest - IN.centreWS;           // perpendicular to the view ray
                float r = length(offWS) / radius;                // in ball radii (1 = ball edge)

                float2 offVS = mul((float3x3)UNITY_MATRIX_V, offWS).xy;
                float t = _Time.y;
                float ang = atan2(offVS.y, offVS.x) / 6.2831853 + 0.5;     // 0..1
                ang += t * _SpinSpeed * (hash11(seed * 3.7) - 0.5) + seed;

                float n = max(3.0, floor(_RayCount));
                float k = floor(ang * n);                        // ray index
                float a = frac(ang * n) - 0.5;                   // position across the sector, -0.5..0.5

                // Per-ray randomness that changes in quick steps -> crackling sparks.
                float tick = floor(t * _FlickerSpeed * 0.6 + hash11(k + seed * 13.0) * 7.0);
                float rnd = hash11(k * 7.31 + tick * 1.17 + seed * 37.0);
                float len = lerp(1.25, _RayLength, rnd);        // each ray its own length
                float alive = step(0.18, hash11(k * 3.3 + tick * 0.71 + seed * 11.0)); // some rays drop out

                // Jagged: the ray's centre line kinks as it goes out (lightning-like zig-zag).
                float kink = (noise1(r * 5.0 + k * 4.1 + tick * 2.3) - 0.5) * _RayJag;
                float across = abs(a - kink * 0.5);

                // Tapering spike: full width at the ball, sharp point at its tip.
                float along = saturate((r - 0.9) / max(len - 0.9, 1e-3));   // 0 at ball edge -> 1 at tip
                float halfW = _RayWidth * 0.5 * (1.0 - along);
                float spike = saturate(1.0 - across / max(halfW, 1e-4));
                spike = spike * spike * (1.0 - along) * alive * step(0.9, r) * step(r, len);

                half3 rayCol = lerp(_RayBaseColor.rgb, _RayTipColor.rgb, along);

                // Soft warm glow just outside the ball.
                float glow = saturate(1.0 - (r - 1.0) / 0.9);
                glow = glow * glow * step(0.95, r);

                float f = Flicker(seed);
                half3 col = rayCol * spike * _RayIntensity * f + _RayTipColor.rgb * glow * _GlowIntensity * f;
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Core ball.
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

            Varyings vertCore(Attributes IN) { return VertCommon(IN, 1.0); }

            half4 fragCore(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(GetCameraPositionWS() - IN.positionWS);
                float ndv = saturate(dot(n, v));
                float f = Flicker(IN.seedRadius.x);

                half3 col = lerp(_CoreColor.rgb, _CoreHot.rgb, pow(ndv, _CentrePower));
                col += _RimColor.rgb * pow(1.0 - ndv, _RimPower);
                return half4(col * f, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
