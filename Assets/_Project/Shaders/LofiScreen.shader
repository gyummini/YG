// Lo-fi screen for the art-direction preview (LofiPreview / LofiScreenPass): samples the camera image on a coarse
// grid (point sampling, no filtering), adds 4x4 ordered dithering and quantises each channel in display space.
Shader "NightOffice/LofiScreen"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "LofiScreen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _LofiRes;    // xy: low-res size in pixels
            float _LofiLevels;  // colour steps per channel
            float _LofiDither;  // 0..1

            static const float kBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 cell = floor(input.texcoord * _LofiRes.xy);
                float2 uv = (cell + 0.5) / _LofiRes.xy;
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                c = LinearToSRGB(saturate(c));
                int2 p = int2(cell) & 3;
                float levels = max(_LofiLevels, 1.0);
                float d = (kBayer[p.y * 4 + p.x] / 16.0 - 0.5) / levels * _LofiDither;
                c = round(saturate(c + d) * levels) / levels;
                return half4(SRGBToLinear(c), 1.0);
            }
            ENDHLSL
        }
    }
}
