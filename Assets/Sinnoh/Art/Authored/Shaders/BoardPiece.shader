// 플레이어 말(M4-2d, d3-3 §2-3 ③): 화면 공간 정수배 픽셀 그림. 메시 꼭짓점 xy = 발 점에서의 텍셀 오프셋(위가 +), uv = (텍셀 x, 텍셀 y, 부분).
// 꼭짓점: 물체 원점(= 발 점, 칸 윗면 + 칸 안 자리)을 투영해 픽셀로 내린 뒤 오프셋 × k를 더한다 → 사각형 꼭짓점이 늘 정수 픽셀이고 원근 크기 변화가 없다.
//   k = max(1, round(max(1, _SinnohPieceZoomK × _ZoomMul + _ZoomAdd) × 화면 높이 / 1080)) — C# BoardPieces.KFor·Scale과 같은 식. _SinnohPieceZoomK = 줌 단계(1·2·3),
//   _ZoomMul·_ZoomAdd = 말 양식의 줌 단계별 배수(직업 그림 1·−1 → 1·1·2 = 크기 안 E, 주인공 도트 2·−1 → 1·3·5 = B1 — 04 §3-1).
//   얼굴 조각(크기 안 E, 결정 61 ②): _FaceCell 폭이 0이 아니고 줌 단계가 1(전체 줌)이면 꼭짓점이 uv2(조각 쪽 오프셋·텍셀)를 고르고 조각은 _Cell 대신 _FaceCell만 읽는다 — C# BoardPieces.ShapeAt과 같은 규칙. y는 _ProjectionParams.x로 뒤집힘을 맞춰 C#(아래가 0행)과 같은 쪽으로 내린다.
//   깊이(M4-2 검토 반영): 그림(부분 1)은 꼭짓점마다 「발에서 카메라 쪽으로 _BodyPull만큼 당긴 점을 지나고 카메라 오른쪽·월드 위로 펼친 세운 평면」과
//   그 꼭짓점 화면 자리를 지나는 광선의 교점 깊이 — 실제 세운 스탠디처럼 위로 갈수록 가깝다(앞 칸·피규어는 가리고, 뒤의 높은 칸은 머리를 가리지 않는다).
//   고리(부분 0)·핀(부분 2)은 발 점에서 _DepthBias만큼 당긴 점 하나의 깊이(고리 아래쪽이 제 칸 윗면에 묻히지 않게). _BodyPull > _DepthBias라 그림이 제 고리 위에 선다.
//   w는 네 꼭짓점 모두 같게 두고 z만 바꾼다 — 텍셀 보간이 화면에서 선형으로 남아 정수배 그림이 그대로다(평면의 깊이는 화면 좌표에 아핀이라 z 선형 보간이 정확).
// 조각: 텍셀을 Load로 그대로 읽는다(걸러짐 없음). 부분 0 고리(_RingTex, 반투명), 1 그림(_MainTex 불투명 텍셀 + 둘레 종이 테두리: 8이웃 흰 · 그 밖 2텍셀 어두운), 2 핀(_MainTex).
//   그림은 _MainTex의 한 칸 _Cell(x, y, 폭, 높이 — 텍셀, 아래가 0행)만 읽는다: 직업 그림은 그림 전체, 주인공 도트는 걷기 시트의 프레임 하나(칸 밖은 투명으로 친다).
// 빛·안개 없음. 투명 대기열(아이콘 뒤)이라 SSAO·그림자를 받지 않는다.
Shader "Sinnoh/BoardPiece"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _RingTex ("Ring", 2D) = "black" {}
        _PaperColor ("Paper", Color) = (0.9647, 0.9490, 0.9020, 1)
        _EdgeColor ("Edge", Color) = (0.0627, 0.0784, 0.1216, 1)
        _DepthBias ("Depth Bias", Float) = 0.5
        _BodyPull ("Body Plane Pull", Float) = 0.75
        _Cell ("Frame (x, y, w, h) texels", Vector) = (0, 0, 0, 0)
        _FaceCell ("Face crop (x, y, w, h) texels, w 0 = none", Vector) = (0, 0, 0, 0)
        _ZoomMul ("Zoom step multiplier", Float) = 1
        _ZoomAdd ("Zoom step offset", Float) = 0
        _Grey ("Grey (eliminated seat)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                float4 _RingTex_TexelSize;
                half4 _PaperColor;
                half4 _EdgeColor;
                float _DepthBias;
                float _BodyPull;
                float4 _Cell;
                float4 _FaceCell;
                float _ZoomMul;
                float _ZoomAdd;
                half _Grey;
            CBUFFER_END

            // 탈락한 좌석(91행): 밝기만 남기고 옅게 — _Grey 0이면 그대로
            half3 Bleach(half3 c)
            {
                half y = dot(c, half3(0.299, 0.587, 0.114));
                return lerp(c, (half3)(y * 0.6 + 0.3), _Grey);
            }
            TEXTURE2D(_MainTex);
            TEXTURE2D(_RingTex);
            float _SinnohPieceZoomK;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 uv : TEXCOORD0;
                float4 alt : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texel : TEXCOORD0;
                nointerpolation float part : TEXCOORD1;
                nointerpolation float face : TEXCOORD2;
            };

            // 화면 자리 ndc(클립 공간 xy/w)를 지나는 광선과 세운 평면(originWS를 지나고 카메라 오른쪽·월드 위로 펼침)의 교점 깊이(z/w).
            // 평면 위 점 = origin + a·오른쪽 + b·위 → 클립 좌표가 a·b에 선형이라 xy/w = ndc를 2×2로 푼다(역행렬을 쓰지 않아 뒤집힘이 VP와 늘 같다).
            float PlaneDepth(float3 originWS, float2 ndc, float fallback)
            {
                float4 c0 = mul(UNITY_MATRIX_VP, float4(originWS, 1.0));
                float4 cr = mul(UNITY_MATRIX_VP, float4(UNITY_MATRIX_V[0].xyz, 0.0));
                float4 cy = mul(UNITY_MATRIX_VP, float4(0.0, 1.0, 0.0, 0.0));
                float m00 = cr.x - ndc.x * cr.w, m01 = cy.x - ndc.x * cy.w;
                float m10 = cr.y - ndc.y * cr.w, m11 = cy.y - ndc.y * cy.w;
                float r0 = ndc.x * c0.w - c0.x, r1 = ndc.y * c0.w - c0.y;
                float det = m00 * m11 - m01 * m10;
                if (abs(det) < 1e-6) return fallback;
                float a = (r0 * m11 - m01 * r1) / det;
                float b = (m00 * r1 - m10 * r0) / det;
                float4 c = c0 + a * cr + b * cy;
                return c.w > 1e-4 ? c.z / c.w : fallback;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 footWS = TransformObjectToWorld(float3(0, 0, 0));
                float4 footCS = TransformWorldToHClip(footWS);
                float3 toCam = normalize(_WorldSpaceCameraPos - footWS);
                float4 nearCS = TransformWorldToHClip(footWS + toCam * _DepthBias);
                float flip = _ProjectionParams.x;
                float2 screen = _ScreenParams.xy;
                float2 ndc = footCS.xy / footCS.w;
                ndc.y *= flip;
                float2 px = floor((ndc * 0.5 + 0.5) * screen);
                float k = max(1.0, floor(max(1.0, _SinnohPieceZoomK * _ZoomMul + _ZoomAdd) * screen.y / 1080.0 + 0.5));
                bool face = _FaceCell.z > 0.5 && _SinnohPieceZoomK < 1.5;
                float2 v = (px + (face ? input.alt.xy : input.positionOS.xy) * k) / screen * 2.0 - 1.0;
                v.y *= flip;
                float depth = nearCS.z / nearCS.w;
                if (abs(input.uv.z - 1.0) < 0.5) depth = PlaneDepth(footWS + toCam * _BodyPull, v, depth);
                o.positionCS = float4(v * nearCS.w, depth * nearCS.w, nearCS.w);
                o.texel = face ? input.alt.zw : input.uv.xy;
                o.part = input.uv.z;
                o.face = face ? 1.0 : 0.0;
                return o;
            }

            bool Inside(int2 t, float4 texelSize)
            {
                return t.x >= 0 && t.y >= 0 && t.x < (int)texelSize.z && t.y < (int)texelSize.w;
            }

            bool Opaque(int2 t, float4 cell)
            {
                return t.x >= 0 && t.y >= 0 && t.x < (int)cell.z && t.y < (int)cell.w && LOAD_TEXTURE2D(_MainTex, t + (int2)cell.xy).a >= 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                int2 t = (int2)floor(input.texel);
                if (input.part < 0.5)
                {
                    if (!Inside(t, _RingTex_TexelSize)) discard;
                    half4 r = LOAD_TEXTURE2D(_RingTex, t);
                    if (r.a <= 0.001) discard;
                    return r;
                }
                if (input.part > 1.5)
                {
                    if (!Inside(t, _MainTex_TexelSize)) discard;
                    half4 p = LOAD_TEXTURE2D(_MainTex, t);
                    if (p.a <= 0.001) discard;
                    p.rgb = Bleach(p.rgb);
                    return p;
                }
                float4 cell = input.face > 0.5 ? _FaceCell : _Cell;
                if (Opaque(t, cell)) return half4(Bleach(LOAD_TEXTURE2D(_MainTex, t + (int2)cell.xy).rgb), 1);
                bool near1 = false, near2 = false;
                [unroll] for (int dy = -2; dy <= 2; dy++)
                {
                    [unroll] for (int dx = -2; dx <= 2; dx++)
                    {
                        int ax = abs(dx), ay = abs(dy);
                        if ((ax == 2 && ay == 2) || (ax == 0 && ay == 0)) continue;
                        if (Opaque(t + int2(dx, dy), cell))
                        {
                            if (ax <= 1 && ay <= 1) near1 = true;
                            else near2 = true;
                        }
                    }
                }
                if (near1) return _PaperColor;
                if (near2) return _EdgeColor;
                discard;
                return 0;
            }
            ENDHLSL
        }
    }
}
