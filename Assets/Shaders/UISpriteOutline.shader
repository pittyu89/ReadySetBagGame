// A sprite reduced to a flat block of colour, keeping only its alpha.
//
// Used to mark the tool the player is currently using: an Image with this material sits
// just behind the real one, carrying the same sprite and sized a few units larger, so the
// colour shows only as an edge peeking out around it.
//
// The stock UI Outline component cannot do this job. It redraws the graphic at four
// offsets and tints those copies with effectColor, but a tint multiplies against the
// texture — so tinting a green bottle white leaves it green, and the "outline" comes out
// as a coloured smear of the sprite rather than an edge. Throwing the sprite's own colour
// away and keeping only its shape is the part that has to happen in a shader.
//
// Drawing behind rather than dilating the alpha in UV space is deliberate: the alcohol
// sprite runs right to its texture border on two sides, and a dilation clamps at the edge,
// so a ring traced that way would come out missing exactly there.
Shader "Custom/UISpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Outline Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "IgnoreProjector"  = "True"
            "RenderType"       = "Transparent"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float  _Cutoff;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Only the alpha is read; the sprite's own colour is never sampled.
                // Stepped rather than used as-is, so a sprite with soft or dirty edges
                // still gives a clean edge instead of a faint wash around it.
                float a = step(_Cutoff, tex2D(_MainTex, IN.uv).a);
                return fixed4(_Color.rgb, a * _Color.a * IN.color.a);
            }
            ENDCG
        }
    }
}
