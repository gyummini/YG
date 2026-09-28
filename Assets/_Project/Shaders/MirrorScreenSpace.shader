// Planar mirror: samples the reflection camera's render texture in screen space (x flipped).
Shader "NightOffice/MirrorScreenSpace"
{
    Properties
    {
        _MainTex ("Reflection", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.85, 0.9, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                uv.x = 1.0 - uv.x;
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return half4(c.rgb * _Tint.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
