Shader "UI/ColorBarVertical"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlipVertical ("Flip Vertical", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _FlipVertical;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;

                // 가로 LUT(x축 gradient)를 세로 컬러바로 보이게 90도 회전
                // 아래쪽이 낮은 값, 위쪽이 높은 값
                uv = float2(uv.y, 0.5);

                // 필요 시 상하 반전
                if (_FlipVertical > 0.5)
                {
                    uv.x = 1.0 - uv.x;
                }

                fixed4 col = tex2D(_MainTex, uv) * IN.color;
                return col;
            }
            ENDHLSL
        }
    }
}