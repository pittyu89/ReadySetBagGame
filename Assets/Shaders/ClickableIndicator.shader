// Draws interaction markers on top of the scene.
//
// ZTest Always is the point of this shader: anchored at a readable height, a marker would
// otherwise clip through the countertop or cabinet it belongs to and render as a half-buried
// triangle. Walls are handled by an explicit line-of-sight raycast in
// ClickableHighlightManager instead of by the depth buffer.
Shader "ReadySetBag/ClickableIndicator"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // SpriteRenderer.color arrives here, not in the vertex colour channel. Without this
        // the markers render plain white whatever colour the manager asks for.
        [PerRendererData] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha   // sprites arrive premultiplied by the SpriteRenderer

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            // Set per-renderer by SpriteRenderer, so it lives outside the material CBUFFER.
            float4 _RendererColor;

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * half4(_Color) * half4(_RendererColor);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 c = texel * input.color;
                c.rgb *= c.a;      // premultiply to match the Blend mode above
                return c;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
