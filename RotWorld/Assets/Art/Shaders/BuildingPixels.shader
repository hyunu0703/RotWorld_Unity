Shader "RotWorld/BuildingPixels"
{
    Properties
    {
        [MainTexture] _MainTex("Building pieces", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 pixel : TEXCOORD0;
                float4 region : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 pixel : TEXCOORD0;
                float4 region : TEXCOORD1;
                half4 color : COLOR;
            };

            // 공용 조각의 원본 픽셀 좌표를 메쉬 정점에서 전달
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.pixel = input.pixel;
                output.region = input.region;
                output.color = input.color;
                return output;
            }

            // 조각 영역 안에서 정수 반복 샘플링으로 픽셀 크기 유지
            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = floor(input.pixel);
                float2 repeated = pixel - floor(pixel / input.region.zw) * input.region.zw;
                float2 uv = (input.region.xy + repeated + .5) * _MainTex_TexelSize.xy;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * input.color;
            }
            ENDHLSL
        }
    }
}
