// 굴림판 주사위(결정 49·50, spikes/dice-roll §5): 면 그림(3×2칸 아틀라스 — 아래 줄 타입 색, 윗줄 회색. _Dim 0 → 1로 윗줄로 넘어간다) × 단계 음영 3단(1 / 0.56 / 0.33).
// 카메라 없이 그린다: Sinnoh.Client.DiceBoard가 CommandBuffer.DrawMesh로 큰 RT에 그리고, 보는·투영 행렬은 _DiceVP로 직접 넣는다(파이프라인 무관).
// 알파 = 1(주사위 칸 표시 — DiceDot이 「주사위가 절반 이상 덮은 칸」을 이 값으로 센다). 볼록한 한 덩어리라 Cull Off + 깊이로 충분하다(투영을 뒤집어도 감기는 방향을 신경 쓰지 않는다).
Shader "Sinnoh/DiceLit"
{
    Properties
    {
        _MainTex ("Face atlas", 2D) = "white" {}
    }

    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            float4x4 _DiceVP;
            float4 _LightDir;
            float _Dim;
            float _Flash;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = mul(_DiceVP, mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 1.0)));
                o.uv = input.uv;
                o.normalWS = mul((float3x3)unity_ObjectToWorld, input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 c = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, input.uv).rgb, SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, input.uv + float2(0.0, 0.5)).rgb, _Dim);
                float nl = dot(normalize(input.normalWS), normalize(_LightDir.xyz));
                float s = nl > 0.7 ? 1.0 : (nl > 0.12 ? 0.56 : 0.33);
                return half4(lerp(c * s, half3(1.0, 1.0, 1.0), _Flash), 1.0);
            }
            ENDHLSL
        }
    }
}
