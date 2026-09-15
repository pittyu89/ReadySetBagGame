Shader "Custom/StencilWall"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.878,0.878,0.878,1)
        _BaseMap ("Base Texture", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float  _Smoothness;
            float  _Metallic;
        CBUFFER_END
        ENDHLSL

        // ------------------------------------------------------------------
        // Main lit pass
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // --- Shadows ---
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // --- Additional lights ---
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            // --- Baked lighting / lightmaps ---
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            // --- Ambient occlusion ---
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // --- Fog ---
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;   // lightmap UVs
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 positionWS   : TEXCOORD2;
                float  fogCoord     : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs  = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord    = ComputeFogFactor(posInputs.positionCS.z);

                OUTPUT_LIGHTMAP_UV(IN.staticLightmapUV, unity_LightmapST, OUT.staticLightmapUV);
                OUTPUT_SH(OUT.normalWS, OUT.vertexSH);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 albedo   = texColor * _BaseColor;

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS      = IN.positionWS;
                lightingInput.normalWS        = normalize(IN.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                lightingInput.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);

                // Use baked lightmaps when available (consistent across platforms),
                // fall back to SH probes for non-lightmapped objects.
                #if defined(LIGHTMAP_ON)
                    lightingInput.bakedGI = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, lightingInput.normalWS);
                #else
                    lightingInput.bakedGI = SampleSH(lightingInput.normalWS);
                #endif

                // Required for screen-space ambient occlusion to be sampled correctly.
                lightingInput.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);

                #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                    lightingInput.shadowMask = SAMPLE_SHADOWMASK(IN.staticLightmapUV);
                #endif

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = albedo.rgb;
                surfaceData.alpha       = albedo.a;
                surfaceData.smoothness  = _Smoothness;
                surfaceData.metallic    = _Metallic;
                surfaceData.normalTS    = half3(0,0,1);
                surfaceData.occlusion   = 1;

                half4 color = UniversalFragmentPBR(lightingInput, surfaceData);

                // Aerial perspective: distant walls blend toward the fog colour so they sit
                // back from the room the player is actually standing in.
                color.rgb = MixFog(color.rgb, IN.fogCoord);

                return color;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Shadow casting.
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct ShadowVaryings   { float4 positionCS : SV_POSITION; };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth + normals. Feeds screen-space ambient occlusion.
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct DNAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DNVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            DNVaryings DepthNormalsVert(DNAttributes IN)
            {
                DNVaryings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs  = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                return OUT;
            }

            half4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth only. Used by depth prepass / depth priming.
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct DOAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DOVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            DOVaryings DepthOnlyVert(DOAttributes IN)
            {
                DOVaryings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs  = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                return OUT;
            }

            half4 DepthOnlyFrag(DOVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Meta pass — lets the lightmapper bake indirect light from these
        // surfaces (bounce colour, emissive). Without it, walls contribute
        // nothing to lightmaps, which is why the baked GI around them was
        // dimmer than expected.
        // ------------------------------------------------------------------
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex MetaVert
            #pragma fragment MetaFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct MetaAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uvLM       : TEXCOORD1;
                float2 uvDLM      : TEXCOORD2;
            };

            struct MetaVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            MetaVaryings MetaVert(MetaAttributes IN)
            {
                MetaVaryings OUT;
                OUT.positionHCS = UnityMetaVertexPosition(IN.positionOS.xyz, IN.uvLM, IN.uvDLM);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 MetaFrag(MetaVaryings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 albedo   = texColor * _BaseColor;

                MetaInput metaInput;
                metaInput.Albedo    = albedo.rgb;
                metaInput.Emission  = 0;
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }
}
