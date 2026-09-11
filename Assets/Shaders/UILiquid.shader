// Liquid inside a UI glass, for the water-pouring minigame.
//
// The usual liquid-shader trick clips a 3D mesh against a world-space plane, which needs a
// real mesh and a real camera. This minigame is flat UI over a blurred still, so the whole
// effect is done in the quad's own UV space instead: everything below a wobbling surface
// line is water, everything above it is cut away.
//
// Cuts are hard, not feathered, and the surface line is snapped to a pixel grid, so the
// water reads as part of the same pixel art as the cup drawn over it.
Shader "Custom/UILiquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Mask Texture", 2D) = "white" {}

        _Color        ("Top Colour", Color) = (0.36, 0.66, 0.92, 1)
        _DeepColor    ("Bottom Colour", Color) = (0.16, 0.40, 0.74, 1)
        _SurfaceColor ("Surface Colour", Color) = (0.72, 0.90, 1.0, 1)

        _Fill      ("Fill (0-1)", Range(0, 1)) = 0.0
        _Thickness ("Surface Band", Range(0, 0.3)) = 0.045

        _WaveAmp   ("Wave Amplitude", Range(0, 0.2)) = 0.018
        _WaveFreq  ("Wave Frequency", Range(0, 12)) = 3.0
        _WaveSpeed ("Wave Speed", Range(0, 20)) = 4.0
        _Tilt      ("Surface Tilt", Range(-0.3, 0.3)) = 0.0

        // 0 leaves the surface line smooth; anything else snaps it to that many rows,
        // which is what keeps the waterline on the same grid as the pixel-art cup.
        _PixelRows ("Pixel Rows", Range(0, 128)) = 48

        // Standard UI plumbing — masks and RectMask2D need these to work.
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID", Float) = 0
        _StencilOp        ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask        ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "IgnoreProjector"  = "True"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas" = "True"
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
            Name "UILiquid"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half4  _DeepColor;
                half4  _SurfaceColor;
                half   _Fill;
                half   _Thickness;
                half   _WaveAmp;
                half   _WaveFreq;
                half   _WaveSpeed;
                half   _Tilt;
                half   _PixelRows;
            CBUFFER_END

            // Set per-canvas by the UI system, so it lives outside the material buffer.
            float4 _ClipRect;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 worldPos   : TEXCOORD1;
            };

            // RectMask2D hands the shader a rectangle in canvas space; anything outside it
            // has to be dropped by hand, the same way Unity's own UI shader does it.
            half RectClip(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.worldPos = input.positionOS;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Two sines at unrelated frequencies: the surface never visibly repeats,
                // which one sine on its own always does.
                float t = _Time.y * _WaveSpeed;
                float wave = sin(i.uv.x * _WaveFreq * 6.2831853 + t) * 0.6
                           + sin(i.uv.x * _WaveFreq * 3.7011 - t * 1.37) * 0.4;

                // Waves die out at both extremes: a full glass has a flat meniscus, and an
                // empty one has nothing left to slosh.
                float settle = saturate(_Fill * 6.0) * saturate((1.0 - _Fill) * 6.0);

                float surface = _Fill
                              + wave * _WaveAmp * settle
                              + (i.uv.x - 0.5) * _Tilt;

                if (_PixelRows > 0.5)
                    surface = floor(surface * _PixelRows + 0.5) / _PixelRows;

                // Hard cut — a smoothstep here would leave a soft gradient edge that reads
                // as blur next to the pixel-art cup.
                clip(surface - i.uv.y);

                // Depth gradient, then the brighter band riding on the waterline.
                half4 col = lerp(_DeepColor, _Color, saturate(i.uv.y / max(surface, 0.0001)));

                if (surface - i.uv.y < _Thickness)
                    col.rgb = _SurfaceColor.rgb;

                col *= i.color;

                // The mask sprite carves the liquid to the glass interior. A white texture
                // (the default) leaves the full rect, which is right for a plain quad.
                col.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                col.a *= RectClip(i.worldPos.xy, _ClipRect);

                clip(col.a - 0.001);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
