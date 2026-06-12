Shader "Unlit/ThermalSliceShader"
{
    Properties
    {
        _ColormapTex ("Colormap (LUT)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);
            TEXTURE2D(_ColormapTex);
            SAMPLER(sampler_ColormapTex);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _WorldToVolume;
                float _TempMinDegC;
                float _TempMaxDegC;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 uvw = mul(_WorldToVolume, float4(IN.worldPos, 1.0)).xyz;

                float thermalLBM = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, uvw).r;
                float tempDegC = lerp(_TempMinDegC, _TempMaxDegC, saturate(thermalLBM));

                float denom = max(_TempMaxDegC - _TempMinDegC, 1e-6);
                float ratio = (tempDegC - _TempMinDegC) / denom;
                ratio = saturate(ratio);

                half3 rgb = SAMPLE_TEXTURE2D(_ColormapTex, sampler_ColormapTex, float2(ratio, 0.5)).rgb;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}