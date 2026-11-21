// DS 층의 공간 일그러짐(M9 다듬기 P39 — 기라티나의 그림자 · 울트라 비스트 배틀의 균열): 다 그린 DS 그림(_DsTex, 256×192)을 줄마다 가로로 밀어 옮겨 찍는다(Pt 깨어진 세계의 일렁임).
// 밀림은 DS 픽셀 단위로 반올림한다(도트가 뭉개지지 않게). _Warp = (진폭 px, 위상 0~1, 물결 길이 줄 수, 그대로 둘 아래쪽 줄 수 — 메시지 창).
// 화면 가득 삼각형 하나(DrawProcedural 3) — DiceDot·DiceGlitch와 같은 꼴이라 WebGL2에서도 돈다(sin · floor · 텍스처 한 번, 분기 없음).
// RT에 놓인 줄은 어느 플랫폼에서나 아래부터다(DsStage가 GL.GetGPUProjectionMatrix(…, true)로 그린다) — SV_Position.y가 그대로 「위가 큰 값」.
Shader "Sinnoh/DsWarp"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DsTex);
            float4 _Size;   // 폭, 높이(px)
            float4 _Warp;   // 진폭(px), 위상(0~1), 물결 길이(줄), 그대로 둘 아래쪽 줄 수

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            half4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                float row = floor(pos.y);                    // 아래에서부터 센 줄
                float on = step(_Warp.w, row);               // 메시지 창 줄은 그대로
                float shift = floor(sin((row / max(_Warp.z, 1.0) + _Warp.y) * 6.2831853) * _Warp.x + 0.5) * on;
                float2 uv = float2((floor(pos.x) + 0.5 + shift) / _Size.x, (row + 0.5) / _Size.y);
                return SAMPLE_TEXTURE2D(_DsTex, sampler_PointClamp, uv);
            }
            ENDHLSL
        }
    }
}
