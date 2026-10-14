// Semantic ("representative") density cue, v4 (M54): a SPARK STAR -- no ball. A tiny
// white-hot core with many thin, sharp, straight spikes in all directions (mesh built by
// Editor/SparkStarMeshBuilder), matched to Sources/spark_reference.png. Colour runs
// white-yellow at the core -> yellow -> orange at the tips. Each spike flickers and
// crackles in length on its own; each star has its own random orientation and spins
// slowly. Unlit (self-lit like a real spark), no textures; SRP-Batcher + GPU-instancing
// + single-pass-stereo friendly.
Shader "EmbodiedDataVR/SparkStar"
{
    Properties
    {
        _CoreColor ("Core Colour", Color) = (1.0, 0.98, 0.85, 1)
        _MidColor ("Mid Colour", Color) = (1.0, 0.88, 0.15, 1)
        _TipColor ("Tip Colour", Color) = (1.0, 0.5, 0.04, 1)
        _MidPoint ("Core -> Mid Point", Range(0.05, 0.9)) = 0.45
        _FacetShade ("Facet Shading", Range(0, 1)) = 0.18
        _LengthFlicker ("Spike Length Flicker", Range(0, 1)) = 0.35
        _FlickerSpeed ("Flicker Speed", Range(0, 30)) = 12
        _BrightFlicker ("Brightness Flicker", Range(0, 1)) = 0.25
        _SpinSpeed ("Spin Speed", Range(0, 3)) = 0.4
        _BobAmplitude ("Float Amplitude (m)", Range(0, 0.2)) = 0.04
        _BobSpeed ("Float Speed", Range(0, 3)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "SparkStar"
            Tags { "LightMode"="UniversalForward" }
            Cull Off           // thin needles: draw both faces so they never vanish edge-on
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _MidColor;
                half4 _TipColor;
                half _MidPoint;
                half _FacetShade;
                half _LengthFlicker;
                half _FlickerSpeed;
                half _BrightFlicker;
                half _SpinSpeed;
                half _BobAmplitude;
                half _BobSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;   // x: 0 core .. 1 tip, y: per-spike random
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float  radial     : TEXCOORD2;
                nointerpolation float2 seeds : TEXCOORD3;   // x: star seed, y: spike seed
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float hash11(float x) { return frac(sin(x * 127.1) * 43758.5453); }
            float noise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(hash11(i), hash11(i + 1.0), f); }

            float3x3 AxisAngle(float3 axis, float ang)
            {
                float s = sin(ang), c = cos(ang), t = 1.0 - c;
                float3 a = normalize(axis);
                return float3x3(t*a.x*a.x + c,     t*a.x*a.y - s*a.z, t*a.x*a.z + s*a.y,
                                t*a.x*a.y + s*a.z, t*a.y*a.y + c,     t*a.y*a.z - s*a.x,
                                t*a.x*a.z - s*a.y, t*a.y*a.z + s*a.x, t*a.z*a.z + c);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 originWS = GetObjectToWorldMatrix()._m03_m13_m23;
                float seed = hash13(originWS * 7.13 + 0.5);
                float t = _Time.y;

                // Per-spike crackle: spikes shrink/grow in quick random steps (tips only).
                float spike = IN.uv.y;
                float crackle = noise1(t * _FlickerSpeed * 0.5 + spike * 53.0 + seed * 91.0);
                float lenScale = 1.0 - _LengthFlicker * crackle * IN.uv.x;
                float3 p = IN.positionOS.xyz * lenScale;

                // Each star: its own fixed orientation + a slow spin about its own axis.
                float3 axis = float3(hash11(seed * 3.1) - 0.5, hash11(seed * 5.7) - 0.5, hash11(seed * 9.3) - 0.5) + 0.01;
                float3x3 rot = AxisAngle(axis, seed * 6.2831 + t * _SpinSpeed * (0.5 + hash11(seed * 2.2)));
                p = mul(rot, p);
                float3 n = mul(rot, IN.normalOS);

                float bt = t * _BobSpeed, s2 = seed * 6.2831;
                float3 bob = float3(sin(bt * 0.7 + s2 * 1.3) * 0.35, sin(bt + s2), cos(bt * 0.6 + s2 * 2.1) * 0.35) * _BobAmplitude;

                float3 positionWS = TransformObjectToWorld(p) + bob;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(n);
                OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
                OUT.radial = IN.uv.x;
                OUT.seeds = float2(seed, spike);
                return OUT;
            }

            half4 frag(Varyings IN, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float r = saturate(IN.radial);
                half3 col = r < _MidPoint
                    ? lerp(_CoreColor.rgb, _MidColor.rgb, r / _MidPoint)
                    : lerp(_MidColor.rgb, _TipColor.rgb, (r - _MidPoint) / (1.0 - _MidPoint));

                // A little facet shading so the 3D needles read as solid, sharp shapes.
                float3 nrm = normalize(IN.normalWS) * (front ? 1.0 : -1.0);
                float facing = saturate(dot(nrm, normalize(IN.viewDirWS)));
                col *= (1.0 - _FacetShade) + _FacetShade * facing;

                float t = _Time.y * _FlickerSpeed;
                float flick = 1.0 - _BrightFlicker * noise1(t + IN.seeds.x * 71.0 + IN.seeds.y * 13.0);
                return half4(col * flick, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
