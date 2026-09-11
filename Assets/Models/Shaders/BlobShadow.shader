Shader "Custom/BlobShadow"
{
    Properties
    {
        _Color      ("Shadow Color", Color) = (0.06, 0.05, 0.05, 0.45)
        _Falloff    ("Edge Falloff", Float) = 1.1
        _ShadowTex  ("Sprite Texture", 2D) = "white" {}
        _SpriteRect ("Sprite Rect", Vector) = (0, 0, 1, 1)
        _UseSprite  ("Use Sprite (0/1)", Float) = 0
        _Softness   ("Silhouette Softness", Float) = 0.15
        _BlurRadius ("Edge Blur", Float) = 0.03
        _LengthFade ("Length Fade", Float) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "BlobShadow"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half   _Falloff;
                float4 _SpriteRect;
                half   _UseSprite;
                half   _Softness;
                half   _BlurRadius;
                half   _LengthFade;
            CBUFFER_END

            TEXTURE2D(_ShadowTex);
            SAMPLER(sampler_ShadowTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = _Color;

                if (_UseSprite > 0.5)
                {
                    // Remap UV into the sprite's atlas rect
                    float2 spriteUV = float2(
                        _SpriteRect.x + i.uv.x * _SpriteRect.z,
                        _SpriteRect.y + i.uv.y * _SpriteRect.w
                    );

                    half spriteAlpha = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, spriteUV).a;

                    // Soften the silhouette edges
                    half softened = smoothstep(0.0, _Softness + _BlurRadius, spriteAlpha);

                    // Fade along length (V axis — 0 is near feet, 1 is far end)
                    half lengthMask = saturate(1.0 - i.uv.y * _LengthFade);

                    col.a *= softened * lengthMask;
                }
                else
                {
                    // Round blob falloff
                    float2 centred = i.uv - 0.5;
                    float dist = length(centred) * 2.0;
                    float alpha = saturate(1.0 - pow(dist, _Falloff));
                    col.a *= alpha;
                }

                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
