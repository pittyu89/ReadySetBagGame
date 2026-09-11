// The dark room for the flashlight minigame, with a hole where the beam is pointing.
//
// The people are drawn underneath this and are genuinely hidden by it, rather than being
// faded in and out by script. That is the whole difference between a torch uncovering
// somebody and a sprite appearing out of nowhere: the person is always there, and the
// light simply stops covering them.
//
// The hole is measured in the quad's own UV space with the x axis corrected by the aspect,
// so it stays a circle on a panel that is not square.
Shader "Custom/UIDarknessHole"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Darkness Colour", Color) = (0.02, 0.02, 0.02, 1)

        // Set every frame by FlashlightBeam. Parked off-screen so the room is solid dark
        // before the torch is switched on.
        _LightPos ("Light Position (UV)", Vector) = (-10, -10, 0, 0)
        _Radius   ("Hole Radius (fraction of height)", Range(0, 2)) = 0.0
        _Soft     ("Edge Softness", Range(0.001, 0.5)) = 0.06
        _Aspect   ("Width / Height", Float) = 1.777

        // 0 cuts a circle, 1 cuts a square. Defaults to the circle so the flashlight, which
        // never sets it, keeps the beam it always had.
        _Square   ("Square Hole", Range(0, 1)) = 0

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

            fixed4 _Color;
            float4 _LightPos;
            float  _Radius;
            float  _Soft;
            float  _Aspect;
            float  _Square;

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
                // Aspect-corrected so the hole is round rather than an ellipse
                float2 d = float2((IN.uv.x - _LightPos.x) * _Aspect, IN.uv.y - _LightPos.y);

                // Two ways of measuring how far out we are. Straight-line distance draws a
                // circle — the torch beam. Taking the larger of the two axes instead draws
                // a square, every point on its edge being _Radius from the middle along one
                // axis or the other: that is the glowstick, which lights a patch of floor
                // rather than throwing a cone.
                float round  = length(d);
                float boxed  = max(abs(d.x), abs(d.y));
                float dist   = lerp(round, boxed, saturate(_Square));

                // 0 inside the hole, 1 out in the dark, with a soft rim so the beam does
                // not end on a hard cut.
                float dark = smoothstep(_Radius - _Soft, _Radius + _Soft, dist);

                fixed4 c = _Color * IN.color;
                c.a *= dark;
                return c;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
