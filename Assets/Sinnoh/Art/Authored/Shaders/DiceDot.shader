// 굴림판 도트풍(결정 49, spikes/dice-roll §5 — 시제품은 CPU로 했다): 격자의 _SS배로 그린 판 그림(_HiTex, 알파 = 주사위 칸)을 격자 크기로 줄이며 색을 제한하고 외곽선을 두른다.
//   패스 0 「줄이기 + 색 제한」: 칸마다 _SS × _SS 평균(sRGB 값으로). 주사위가 절반 이상 덮은 칸은 주사위 팔레트(면 색 × 음영 3단 + 흰색)에서, 나머지는 바닥 팔레트에서 가까운 색.
//     글리치가 도는 둘레(_Glitch.xy 가운데 · z 반 폭, 격자 px · 왼쪽 위 원점)는 바닥 색에 가까우면 바닥 색, 아니면 채널마다 8단으로 뭉갠다(외곽선 없음).
//     알파 = 1 주사위 칸 · 0.5 글리치 칸 · 0 나머지.
//   패스 1 「외곽선」: 주사위 칸 바로 바깥 한 칸(위·아래·양옆)에 잉크색. 알파 = 1.
// 화면 가득 삼각형 하나(DrawProcedural 3), 칸 자리는 SV_Position으로 세고 텍스처는 LOAD로 읽는다(보간 없음).
Shader "Sinnoh/DiceDot"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Quantize"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_HiTex);
            float4 _Size;        // 격자 폭, 높이
            float4 _Glitch;      // 가운데 x, y(왼쪽 위 원점), 반 폭, 켬
            float _SS;
            float _DiePalCount, _FloorPalCount;
            float4 _DiePal[32];   // sRGB 0~255
            float4 _FloorPal[8];

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float Dist(float3 a, float3 b)
            {
                float3 d = a - b;
                return dot(d * d, float3(0.9, 1.2, 0.8));
            }

            half4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                int2 cell = int2(pos.xy);
                int ss = (int)_SS;
                float3 sum = 0.0, dieSum = 0.0;
                float dn = 0.0;
                for (int sy = 0; sy < ss; sy++)
                {
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float4 c = LOAD_TEXTURE2D(_HiTex, cell * ss + int2(sx, sy));
                        float3 g = LinearToSRGB(c.rgb) * 255.0;
                        sum += g;
                        if (c.a > 0.5)
                        {
                            dieSum += g;
                            dn += 1.0;
                        }
                    }
                }
                float m = (float)(ss * ss);
                float3 avg = sum / m;

                float3 floorColor = _FloorPal[0].rgb;
                float fd = 1e9;
                for (int i = 0; i < (int)_FloorPalCount; i++)
                {
                    float d = Dist(_FloorPal[i].rgb, avg);
                    if (d < fd) { fd = d; floorColor = _FloorPal[i].rgb; }
                }

                float yTop = _Size.y - pos.y;   // RT에 놓인 줄은 아래부터다(DiceGlitch 머리말)
                float3 result = floorColor;
                float flag = 0.0;
                if (_Glitch.w > 0.5 && abs(pos.x - _Glitch.x) < _Glitch.z && abs(yTop - _Glitch.y) < _Glitch.z)
                {
                    if (fd >= 500.0)
                    {
                        result = round(avg / 36.43) * 36.43;
                        flag = 0.5;
                    }
                }
                else if (dn * 2.0 >= m)
                {
                    float3 die = dieSum / dn;
                    float dd = 1e9;
                    for (int k = 0; k < (int)_DiePalCount; k++)
                    {
                        float d = Dist(_DiePal[k].rgb, die);
                        if (d < dd) { dd = d; result = _DiePal[k].rgb; }
                    }
                    flag = 1.0;
                }
                return half4(SRGBToLinear(result / 255.0), flag);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LowTex);
            float4 _Size;
            float4 _Ink;   // Linear

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float Die(int2 cell)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= (int)_Size.x || cell.y >= (int)_Size.y) return 0.0;
                return step(0.75, LOAD_TEXTURE2D(_LowTex, cell).a);
            }

            half4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                int2 cell = int2(pos.xy);
                float4 c = LOAD_TEXTURE2D(_LowTex, cell);
                if (c.a < 0.25 && Die(cell + int2(-1, 0)) + Die(cell + int2(1, 0)) + Die(cell + int2(0, -1)) + Die(cell + int2(0, 1)) > 0.5) return half4(_Ink.rgb, 1.0);
                return half4(c.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
