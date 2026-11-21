// DS 층(256×192 RenderTexture)의 그림 한 장을 찍는다. Sinnoh.Client.DsStage가 단위 네모를 _Rect(DS px, y 아래로)에 놓고 _Src(그림 안 조각, 0~1, y 아래로)를 입힌다.
// _Mode 0 = 그림 그대로 · 1 = 원작 자산 글꼴 아틀라스(R8 팔레트 번호: 1 글자 → _Ink, 2 그림자 → _Shadow, 나머지 투명) ·
//       2 = 두 색 그림 다시 칠하기(HP 숫자: 어두운 쪽 → _Ink, 밝은 쪽 → _Shadow) · 3 = 단색 _Ink ·
//       4 = 그림을 _Ink 색으로 물들이기(_Ink.a만큼 — 볼에 빨려 드는 포켓몬의 흰빛, 딸깍 뒤 어두워진 볼. 알파는 그림 그대로).
// 색은 Linear 값으로 받는다(코드가 미리 바꿔 넣는다). 깊이 안 씀.
Shader "Sinnoh/DsBlit"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DsTex);
            float4x4 _DsVP;
            float4 _Rect;
            float4 _Src;
            float4 _Ink;
            float4 _Shadow;
            float _Mode;

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
                float2 p = _Rect.xy + input.uv * _Rect.zw;
                o.positionCS = mul(_DsVP, float4(p, 0.0, 1.0));
                o.uv = float2(_Src.x + input.uv.x * _Src.z, 1.0 - (_Src.y + input.uv.y * _Src.w));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Mode > 2.5 && _Mode < 3.5) return _Ink;
                half4 c = SAMPLE_TEXTURE2D(_DsTex, sampler_PointClamp, input.uv);
                if (_Mode > 3.5) return half4(lerp(c.rgb, _Ink.rgb, _Ink.a), c.a);
                if (_Mode > 1.5)
                {
                    half lum = dot(c.rgb, half3(0.299, 0.587, 0.114));
                    half4 o = lum < 0.25 ? _Ink : _Shadow;
                    o.a *= c.a;
                    return o;
                }
                if (_Mode > 0.5)
                {
                    int index = (int)round(c.r * 255.0);
                    if (index == 1) return _Ink;
                    if (index == 2) return _Shadow;
                    return half4(0, 0, 0, 0);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
