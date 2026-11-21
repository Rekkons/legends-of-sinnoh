// 굴림판 바닥·테두리(단색)와 주사위 그림자(딱딱한 둥근 네모, _Blob 1). Sinnoh.Client.DiceBoard가 주사위보다 먼저 그린다(깊이 안 씀).
// 색은 Linear 값으로 받는다(_Color — 코드가 미리 바꿔 넣는다). 알파 채널에는 쓰지 않는다(0으로 남는다 = 주사위 칸이 아님, DiceLit 참고).
Shader "Sinnoh/DiceFlat"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _DiceVP;
            float4 _Color;
            float _Blob;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = mul(_DiceVP, mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 1.0)));
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = _Color;
                if (_Blob > 0.5)
                {
                    float2 p = abs(input.uv * 2.0 - 1.0);
                    c.a *= step(length(max(p - 0.62, 0.0)), 0.3);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
