// 디아루가 재굴림의 제자리 글리치(결정 48 ②·49, spikes/dice-roll §4 — 시제품 proto/DiceGlitch.shader를 옮김): 고른 주사위 한 개만 따로 그린 RT(_DieTex)를
// 판 그림 위에 얹으면서 가로 띠 밀림, 색 분리, 잔상, 파란 색조, 가로줄, 시계 무늬(고리·눈금·바늘)를 준다. 미리 곱한 알파로 섞고, 판 그림의 알파(주사위 칸 표시)는 건드리지 않는다.
// 화면 가득 삼각형 하나(DrawProcedural 3). 자리는 SV_Position으로 센다(_Size = RT 크기 px). RT에 놓인 줄은 어느 플랫폼에서나 아래부터다
// (DiceBoard가 GL.GetGPUProjectionMatrix(…, true)로 그려 Unity 관례대로 v = 0이 그림 아래) — 그래서 SV_Position.y가 그대로 「위가 큰 값」이다.
Shader "Sinnoh/DiceGlitch"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DieTex);
            float4 _Size;       // 폭, 높이
            float4 _Center;     // 시계 가운데(0~1, y는 위가 1)
            float _Amt, _Split, _Tint, _Ghost, _Clock, _Angle, _Seed, _Bands, _Radius, _Aspect, _Px, _Lines;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float H1(float x) { return frac(sin(x * 12.9898 + _Seed * 78.233) * 43758.5453); }

            float Seg(float2 p, float ang, float len, float w)
            {
                float2 d = float2(sin(ang), cos(ang));
                float t = clamp(dot(p, d), 0.0, len);
                return step(length(p - d * t), w);
            }

            half4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                float2 st = pos.xy / _Size.xy;
                float2 uv = st;
                float band = floor(uv.y * _Bands);
                float sel = step(0.45, H1(band * 1.7 + 3.1));
                float shift = (H1(band) - 0.5) * 2.0 * _Amt * sel;
                float2 u = st + float2(shift, 0.0);
                half4 cr = SAMPLE_TEXTURE2D(_DieTex, sampler_PointClamp, u + float2(_Split, 0.0));
                half4 cg = SAMPLE_TEXTURE2D(_DieTex, sampler_PointClamp, u);
                half4 cb = SAMPLE_TEXTURE2D(_DieTex, sampler_PointClamp, u - float2(_Split, 0.0));
                float a = max(cg.a, max(cr.a, cb.a));
                float3 col = float3(cr.r * cr.a, cg.g * cg.a, cb.b * cb.a);
                float lum = dot(col, float3(0.3, 0.6, 0.1));
                col = lerp(col, lum * float3(0.5, 0.85, 1.6) + float3(0.0, 0.02, 0.08) * a, _Tint);
                // 잔상(양옆으로 옅은 파란 그림자)
                float ga = max(SAMPLE_TEXTURE2D(_DieTex, sampler_PointClamp, st + float2(_Ghost, 0.0)).a, SAMPLE_TEXTURE2D(_DieTex, sampler_PointClamp, st - float2(_Ghost * 0.6, 0.0)).a)
                    * step(0.00001, _Ghost) * 0.4 * (1.0 - a);
                col += float3(0.25, 0.55, 1.0) * ga;
                a = max(a, ga);
                // 가로줄
                float ln = step(0.5, frac(uv.y * _Lines * 0.5));
                col *= lerp(1.0, 0.7, ln * step(0.00001, _Amt));
                // 시계 무늬
                float2 p = (uv - _Center.xy) * float2(_Aspect, 1.0) / _Radius;
                float r = length(p), w = _Px / _Radius;
                float ring = step(abs(r - 1.0), w * 0.6);
                float ang = atan2(p.x, p.y);
                float tick = step(abs(frac(ang / 6.2831853 * 12.0 + 0.5) - 0.5), 0.1) * step(abs(r - 0.84), 0.09);
                float cl = saturate(ring + tick + Seg(p, _Angle, 0.72, w * 0.6) + Seg(p, _Angle * 0.0833, 0.45, w * 0.6)) * _Clock;
                col = lerp(col, float3(0.5, 0.82, 1.0), cl * 0.9);
                a = max(a, cl * 0.9);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
