// Draws the terrain brush preview mesh (see BrushProjector.cs).
// Unity's Projector component does not render in URP, so the brush is drawn as a terrain-following mesh instead.
Shader "RuntimeTerrainEditor/BrushPreview"
{
    Properties
    {
        _MainTex ("Brush (alpha = strength)", 2D) = "white" {}
        _Color ("Color", Color) = (0.3, 0.75, 1, 0.85)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BrushPreview"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Outside [0,1] is outside the (rotated) brush square
                float2 inside = step(0.0, input.uv) * step(input.uv, 1.0);
                half brush = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(input.uv)).a * inside.x * inside.y;
                return half4(_Color.rgb, brush * _Color.a);
            }
            ENDHLSL
        }
    }
}
