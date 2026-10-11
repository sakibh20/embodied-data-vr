// Generic ("abstract") density cue: a clean, softly lit pearl orb.
// Deliberately neutral -- no colour temperature, no glow, nothing that suggests
// energy -- so it stays the GENERIC counterpart to the semantic ElectricOrb.
// Unlit pass with its own soft lighting (wrap diffuse + sky ambient + highlight +
// rim) so it looks the same in Desktop, Simulator and VR regardless of scene light
// tweaks. Per-instance gentle float (seeded from the object's position), no
// textures, SRP-Batcher + GPU-instancing + single-pass-stereo friendly.
Shader "EmbodiedDataVR/SoftOrb"
{
    Properties
    {
        _BaseColor ("Lit Colour", Color) = (0.9, 0.9, 0.92, 1)
        _ShadeColor ("Shade Colour", Color) = (0.38, 0.40, 0.47, 1)
        _AmbientStrength ("Sky Ambient Strength", Range(0, 1)) = 0.25
        _SpecPower ("Highlight Tightness", Range(4, 256)) = 90
        _SpecIntensity ("Highlight Intensity", Range(0, 2)) = 0.75
        _RimColor ("Rim Colour", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.35
        _BobAmplitude ("Float Amplitude (m)", Range(0, 0.2)) = 0.04
        _BobSpeed ("Float Speed", Range(0, 3)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "SoftOrb"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadeColor;
                half _AmbientStrength;
                half _SpecPower;
                half _SpecIntensity;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _BobAmplitude;
                half _BobSpeed;
            CBUFFER_END

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Whole-object float: every vertex gets the same offset, seeded by the
                // object's own world position so neighbours drift out of sync.
                float3 originWS = GetObjectToWorldMatrix()._m03_m13_m23;
                float seed = hash13(originWS * 7.13) * 6.2831;
                float t = _Time.y * _BobSpeed;
                float3 bob = float3(sin(t * 0.7 + seed * 1.3) * 0.35,
                                    sin(t + seed),
                                    cos(t * 0.6 + seed * 2.1) * 0.35) * _BobAmplitude;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz) + bob;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);

                Light mainLight = GetMainLight();
                float3 l = normalize(mainLight.direction);

                // Wrap diffuse -> soft, porcelain-like falloff instead of a hard terminator.
                float wrap = saturate(dot(n, l) * 0.5 + 0.5);
                wrap = wrap * wrap;
                half3 col = lerp(_ShadeColor.rgb, _BaseColor.rgb, wrap);

                // A little sky light so the shadow side isn't dead flat.
                col += SampleSH(n) * _AmbientStrength * _BaseColor.rgb;

                // Soft highlight + rim.
                float3 h = normalize(l + v);
                col += pow(saturate(dot(n, h)), _SpecPower) * _SpecIntensity;
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                col += rim * _RimIntensity * _RimColor.rgb;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
