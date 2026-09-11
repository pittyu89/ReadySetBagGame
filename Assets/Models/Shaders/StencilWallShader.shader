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

        // Set globally from RevealSphere.cs. Kept outside UnityPerMaterial because they are
        // per-frame globals, not per-material data.
        float4 _RevealCenter;      // xyz = character world position
        float  _RevealRadius;      // 0 = no cutout
        float  _RevealDepthPad;    // extends the cut past the character, through wall thickness
        float  _RevealGrazeCutoff; // 0 = keep every wall seen through the hole, 1 = drop all

        // Cuts a cone running from the camera to the character, rather than a sphere sitting on
        // the character. A world-space sphere centres its hole where it is perpendicularly
        // nearest the wall, which is a different screen position from the character behind it -
        // so the character drifts off-centre in the opening. Testing against the
        // camera->character axis keeps the hole centred on the character, always.
        void ApplyRevealCutout(float3 positionWS, float3 normalWS)
        {
            if (_RevealRadius <= 0.0)
                return;

            float3 axis        = _RevealCenter.xyz - _WorldSpaceCameraPos;
            float  camToPlayer = length(axis);
            float3 axisDir     = axis / max(camToPlayer, 1e-4);

            float3 toFrag = positionWS - _WorldSpaceCameraPos;
            float  along  = dot(toFrag, axisDir);

            if (along <= 0.0)
                return;

            float perp = length(toFrag - axisDir * along);

            // A cone, not a cylinder: the opening narrows toward the camera in step with
            // perspective, so the hole holds a constant size on screen however near the wall is.
            float allowed = _RevealRadius * (along / camToPlayer);

            if (perp >= allowed)
                return;

            // Between the camera and the character, plus enough to clear the thickness of the
            // wall they are stood against: cut straight out.
            if (along < camToPlayer + _RevealDepthPad)
                discard;

            // Past the character, inside the opening. Walls we see face-on are the room's own
            // walls and should stay - they are what makes the hole read as a room. Walls we
            // only catch edge-on are slivers of wall side poking in, so drop those.
            float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
            float  facing  = abs(dot(normalize(normalWS), viewDir));

            if (facing < _RevealGrazeCutoff)
                discard;
        }
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
                ApplyRevealCutout(IN.positionWS, IN.normalWS);

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
        // Shadow casting. Deliberately does NOT apply the reveal cutout: the cutaway is a
        // camera-side effect, so the wall must keep blocking light. Cutting it here too would
        // drag a patch of sunlight across the floor wherever the character walked.
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
        // Depth + normals. Feeds screen-space ambient occlusion. This one DOES apply the
        // cutout, so AO matches what is actually visible through the opening.
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
                ApplyRevealCutout(IN.positionWS, IN.normalWS);
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
                ApplyRevealCutout(IN.positionWS, IN.normalWS);
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
