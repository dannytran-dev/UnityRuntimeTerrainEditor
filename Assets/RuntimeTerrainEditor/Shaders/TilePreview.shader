// Draws the terrain tile previews (see TerrainTilePreview.cs): free tile slots as outlined squares with a plus, and the
// tile that would be removed as an outlined box. Every face has uv0 0-1 across it and its size in meters in uv1, so the
// outline keeps the same width in meters on faces of any size. Vertex alpha dims faces (e.g. slots not under the pointer).
Shader "RuntimeTerrainEditor/TilePreview"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _FillAlpha ("Fill Opacity", Range(0, 1)) = 0.12
        _LineWidth ("Outline Width (meters)", Float) = 0.8
        [MaterialToggle] _ShowPlus ("Show Plus", Float) = 1
        _PlusSize ("Plus Size (fraction of the face)", Range(0, 0.5)) = 0.14
        _PlusWidth ("Plus Line Width (fraction of the face)", Range(0, 0.2)) = 0.035
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TilePreview"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FillAlpha;
                float _LineWidth;
                half _ShowPlus;
                float _PlusSize;
                float _PlusWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.size = input.size;
                output.color = input.color;
                return output;
            }

            // Signed distance to an axis-aligned box around the origin (negative inside)
            float BoxDistance(float2 position, float2 halfSize)
            {
                float2 distance = abs(position) - halfSize;
                return length(max(distance, 0.0)) + min(max(distance.x, distance.y), 0.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 meters = input.uv * input.size;

                // Outline: the distance to the nearest edge of the face, smoothed over one pixel
                float2 toEdges = min(meters, input.size - meters);
                float edgeDistance = min(toEdges.x, toEdges.y);
                float edgePixel = max(fwidth(edgeDistance), 1e-5);
                float outline = 1.0 - smoothstep(_LineWidth - edgePixel, _LineWidth + edgePixel, edgeDistance);

                // Plus in the middle, sized by the shorter side of the face
                float shortSide = min(input.size.x, input.size.y);
                float2 fromCenter = meters - input.size * 0.5;
                float armLength = _PlusSize * shortSide;
                float armWidth = _PlusWidth * shortSide * 0.5;
                float plusDistance = min(BoxDistance(fromCenter, float2(armLength, armWidth)), BoxDistance(fromCenter, float2(armWidth, armLength)));
                float plusPixel = max(fwidth(plusDistance), 1e-5);
                float plus = (1.0 - smoothstep(-plusPixel, plusPixel, plusDistance)) * _ShowPlus;

                half alpha = max(_FillAlpha, max(outline, plus)) * _Color.a * input.color.a;
                return half4(_Color.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
