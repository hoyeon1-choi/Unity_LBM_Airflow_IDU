Shader "Unlit/ThermalDiscretizedSliceShader"
{
    // 재질(Material) 인스펙터 창에서 조절할 수 있는 변수
    Properties
    {
        // _ColorSteps ("Color Steps", Float) = 15.0
    }

    SubShader
    {
        // 렌더링 관련 설정 (반투명, 후면 컬링 끄기 등)
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            // --- 셰이더 코드 시작 ---
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP 셰이더 라이브러리 포함 (오타 수정 완료)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 1. C# 스크립트에서 받아올 텍스처 및 변수 선언
            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _WorldToVolume;
                // float _ColorSteps;
            CBUFFER_END

            // 2. 구조체(struct) 선언
            // 함수에서 사용하기 전에 반드시 먼저 선언해야 합니다.
            struct Attributes
            {
                float4 positionOS    : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
            };
            
            // 3. 정점 셰이더(Vertex Shader) 구현
            // 3D 모델의 각 꼭짓점(vertex) 위치를 계산합니다.
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            // // 4. Jet 컬러맵 함수 구현
            // // 온도 값(0~1)을 입력받아 시각화를 위한 색상(RGB)을 반환합니다.
            // float3 JetColormap(float t)
            // {
            //     if (t <= 0.25f)
            //     {
            //         // 구간 1: 파랑(Blue) -> 청록(Cyan)
            //         return lerp(float3(0, 0, 1), float3(0, 1, 1), t * 4.0f);
            //     }
            //     else if (t <= 0.5f)
            //     {
            //         // 구간 2: 청록(Cyan) -> 초록(Green)
            //         return lerp(float3(0, 1, 1), float3(0, 1, 0), (t - 0.25f) * 4.0f);
            //     }
            //     else if (t <= 0.75f)
            //     {
            //         // 구간 3: 초록(Green) -> 노랑(Yellow)
            //         return lerp(float3(0, 1, 0), float3(1, 1, 0), (t - 0.5f) * 4.0f);
            //     }
            //     else
            //     {
            //         // 구간 4: 노랑(Yellow) -> 빨강(Red)
            //         return lerp(float3(1, 1, 0), float3(1, 0, 0), (t - 0.75f) * 4.0f);
            //     }
            // }

            float3 colormap(float t)
            {
                float _step = 1.0 / 14.0;

                float a = 0.0;
                float b = 0.164706/3; // 42.0 / 255.0;
                float c = 0.5;      // 128.0 / 255.0;
                float d = 0.831373; // 212.0 / 255.0;
                float e = 1.0;

                if      (t <  1.0 * _step) return float3(a, a, e);
                else if (t <  2.0 * _step) return float3(a, b, e);
                else if (t <  3.0 * _step) return float3(a, c, e);
                else if (t <  4.0 * _step) return float3(a, d, e);
                else if (t <  5.0 * _step) return float3(a, e, d);
                else if (t <  6.0 * _step) return float3(a, e, c);
                else if (t <  7.0 * _step) return float3(a, e, b);
                else if (t <  8.0 * _step) return float3(b, e, a);
                else if (t <  9.0 * _step) return float3(c, e, a);
                else if (t < 10.0 * _step) return float3(d, e, a);
                else if (t < 11.0 * _step) return float3(e, d, a);
                else if (t < 12.0 * _step) return float3(e, c, a);
                else if (t < 13.0 * _step) return float3(e, b, a);
                else if (t <= 1.0)         return float3(e, a, a);
                else                       return float3(1, 1, 1); 
            }

            // 5. 픽셀 셰이더(Fragment Shader) 구현
            // 화면의 각 픽셀 색상을 계산합니다.
            half4 frag(Varyings IN) : SV_Target
            {
                // 3D 텍스처에서 현재 픽셀 위치에 해당하는 온도 값을 샘플링
                float3 uvw = mul(_WorldToVolume, float4(IN.worldPos, 1.0)).xyz;
                float thermal = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, uvw).r;
                thermal = saturate(thermal);
                
                // // 온도 값을 설정된 단계(_ColorSteps)에 맞춰 이산화(Discretize)
                // float discretized_thermal = floor(thermal * _ColorSteps) / _ColorSteps;
                // 
                // // 이산화된 온도 값으로 Jet 컬러맵 함수를 호출하여 최종 색상 계산
                // float3 jetColor = JetColormap(discretized_thermal);
                // half4 color = half4(jetColor, 1.0);

                half4 color = half4(colormap(thermal), 1.0);

                return color;
            }
            ENDHLSL
        }
    }
}