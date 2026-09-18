Shader "Custom/SpriteShadowCaster"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color  ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.3
    }

    SubShader
    {
        // AlphaTest, not Transparent. URP only gathers OPAQUE-queue renderers into the shadow
        // pass, so a transparent-queue sprite is skipped for shadows however many ShadowCaster
        // passes it declares. Pixel art has hard alpha edges anyway, so clipping costs nothing.
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        // ------------------------------------------------------------------
        // Forward pass. Deliberately UNLIT, matching Sprites/Default, so the character stays
        // bright and readable wherever they stand - they are the primary gameplay object.
        // The only reason this shader exists is the shadow pass below.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SpriteForward"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(c.a - _Cutoff);   // opaque queue, so clip rather than blend
                return c;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Real shadow caster, alpha-clipped so the shadow takes the sprite's silhouette.
        // Cull Off because a sprite quad is single sided.
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Cutoff;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            // No NORMAL here on purpose. A SpriteRenderer builds position + uv + colour only,
            // so reading NORMAL gives garbage and ApplyShadowBias then shoves the geometry
            // somewhere unpredictable.
            struct SCAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct SCVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            SCVaryings ShadowVert(SCAttributes IN)
            {
                SCVaryings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                // Pass the light direction as the normal so the normal-bias term cancels out,
                // leaving only depth bias. The quad has no meaningful normal to offset along.
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, lightDirectionWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 ShadowFrag(SCVaryings IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
