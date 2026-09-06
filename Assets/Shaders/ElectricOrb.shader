Shader "EmbodiedDataVR/ElectricOrb"
{
    Properties
    {
        _CoreColor ("Core Color (HDR)", Color) = (0.03, 0.08, 0.4, 1)
        _EdgeColor ("Edge / Arc Color (HDR)", Color) = (0.55, 0.9, 1.0, 1)
        _Brightness ("Overall Brightness", Range(0.5, 8)) = 2.4
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5
        _FresnelIntensity ("Fresnel Intensity", Range(0, 4)) = 1.1
        _NoiseScale ("Arc Noise Scale", Range(1, 60)) = 12
        _NoiseSpeed ("Arc Noise Speed", Range(0, 5)) = 1.4
        _ArcSharpness ("Arc Sharpness", Range(1, 40)) = 10
        _ArcIntensity ("Arc Intensity", Range(0, 6)) = 2.2
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 2.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half _Brightness;
                half _FresnelPower;
                half _FresnelIntensity;
                half _NoiseScale;
                half _NoiseSpeed;
                half _ArcSharpness;
                half _ArcIntensity;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            // Cheap hash-based value noise -- no texture sampling needed, keeps this
            // instancing-friendly and light on a 100-instance density field in VR.
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i + float3(0,0,0));
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            // Ridged: bright thin veins where the underlying noise crosses ~0.5,
            // instead of soft blobs at the noise peaks -- reads as crackling filaments
            // rather than a mottled/marble surface.
            float ridge(float3 p)
            {
                float n = valueNoise(p);
                return 1.0 - abs(n * 2.0 - 1.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vpi.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetCameraPositionWS() - vpi.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float fresnel = pow(saturate(1.0 - dot(N, V)), _FresnelPower);

                // Per-instance desync seed from the object's world position, so 100
                // spawned orbs don't all crackle/pulse in lockstep. Free (no CPU work,
                // no MaterialPropertyBlock) since unity_ObjectToWorld is already
                // resolved per-instance by GPU instancing.
                float seed = hash31(floor(unity_ObjectToWorld._m03_m13_m23 * 97.0));
                float t = _Time.y * _NoiseSpeed + seed * 37.0;

                // Two crossing ridged-noise layers (different scale/rotation/drift) ->
                // thin branching lightning-like filaments crawling over the surface,
                // instead of one smooth mottled blob layer.
                float3 p1 = N * _NoiseScale + float3(t, -t * 0.7, t * 0.3);
                float3 p2 = N.yzx * (_NoiseScale * 1.6) + float3(-t * 0.5, t * 0.8, -t * 0.35) + 11.3;
                float r1 = pow(saturate(ridge(p1)), _ArcSharpness);
                float r2 = pow(saturate(ridge(p2)), _ArcSharpness) * 0.7;
                float arcs = saturate(r1 + r2) * _ArcIntensity;

                // Slow overall pulse, desynced per instance.
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed + seed * 6.2831853) * _PulseAmount;

                half3 col = lerp(_CoreColor.rgb, _EdgeColor.rgb, saturate(fresnel * _FresnelIntensity));
                col += _EdgeColor.rgb * arcs;
                col *= _Brightness * pulse;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
