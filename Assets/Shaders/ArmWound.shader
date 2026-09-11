// The wounded arm for the first-aid minigame.
//
// Three textures, one lerp: clean skin, the same skin with the bruises painted on, and a
// mask that decides which of the two shows through at any given texel. White mask means
// bruise, black means clean skin, so wiping a bruise away is just painting black into the
// mask — see WoundMaskPainter, which owns the runtime copy this samples.
//
// Lighting is faked from a fixed direction in the material rather than taken from the
// scene. The arm is rendered by its own camera into a RenderTexture, away from the room's
// lights, so a real light would make it depend on whatever the level happens to have lit;
// a baked direction keeps it looking the same every run and on every device.
Shader "Custom/ArmWound"
{
    Properties
    {
        _CleanSkin ("Clean Skin", 2D) = "white" {}
        _Wound     ("Wound (bruised skin)", 2D) = "white" {}
        [NoScaleOffset] _WoundMask ("Wound Mask (white = bruise)", 2D) = "black" {}

        _Ambient   ("Ambient", Range(0, 1)) = 0.55
        _LightDir  ("Fake Light Direction", Vector) = (0.35, 0.75, -0.55, 0)
        _Tint      ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            TEXTURE2D(_CleanSkin); SAMPLER(sampler_CleanSkin);
            TEXTURE2D(_Wound);     SAMPLER(sampler_Wound);
            TEXTURE2D(_WoundMask); SAMPLER(sampler_WoundMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _CleanSkin_ST;
                float4 _LightDir;
                float4 _Tint;
                float  _Ambient;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _CleanSkin);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half3 clean = SAMPLE_TEXTURE2D(_CleanSkin, sampler_CleanSkin, IN.uv).rgb;
                half3 wound = SAMPLE_TEXTURE2D(_Wound,     sampler_Wound,     IN.uv).rgb;

                // Only the red channel: the mask is authored black and white, and reading
                // one channel keeps the runtime copy a single-channel texture.
                half  mask  = SAMPLE_TEXTURE2D(_WoundMask, sampler_WoundMask, IN.uv).r;

                half3 albedo = lerp(clean, wound, saturate(mask));

                float3 n = normalize(IN.normalWS);
                float3 l = normalize(_LightDir.xyz);
                half   ndotl = saturate(dot(n, l));

                // Never fully dark: the unlit side still has to read as an arm.
                half3 lighting = _Ambient + ndotl * (1.0h - _Ambient);

                return half4(albedo * lighting * _Tint.rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
