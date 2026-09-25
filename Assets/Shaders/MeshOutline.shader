// Dark outline around 3D props (inverted hull). Drawn by HouseOutlines on a copy of each prop's
// mesh: back faces only, pushed out along the smoothed normal so the silhouette grows by a fixed
// number of screen pixels whatever the distance - matching the pixel-art outlines of the sprites.
//
// Low-poly meshes have split (hard-edged) normals, which would tear the hull apart at every
// corner, so HouseImportSettings bakes an averaged normal per vertex position into UV3 on import.
// Meshes without it fall back to their own normals.
Shader "ReadySetBag/MeshOutline"
{
    Properties
    {
        _OutlineColor ("Outline Colour", Color) = (0.07, 0.05, 0.04, 1)
        [Tooltip(Thickness in pixels at 1080p. Scales with the screen height.)]
        _OutlineWidth ("Outline Width (px @1080p)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormalOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 n = dot(input.smoothNormalOS, input.smoothNormalOS) > 0.01 ? input.smoothNormalOS : input.normalOS;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(n);
                float2 dirCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float len = length(dirCS);
                if (len > 1e-5)
                {
                    // pixels -> clip space: 2/screen size per pixel, times w to undo the perspective divide
                    float px = _OutlineWidth * (_ScreenParams.y / 1080.0);
                    positionCS.xy += (dirCS / len) * px * 2.0 / _ScreenParams.xy * positionCS.w;
                }
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
