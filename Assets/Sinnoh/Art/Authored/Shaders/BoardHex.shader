// 보드 헥스 프리즘(M4-2b, d3-3 §2-1). 기본색 = 정점색(board.json 틴트, 8비트 sRGB 그대로) × _BaseMap(Gen/Board/hex_*) × _BaseColor.
// 정점색은 sRGB 바이트를 그대로 싣고 여기서 선형으로 바꾼다(시험이 메시 색 = board.json 바이트를 견준다).
// 윗면 칸 경계: uv1.x = 가운데 0 → 테두리 1(옆면은 0). 원작 조각 사이 틈(외접 1.088 vs 칸 간격 2.2) 대신 테두리를 어둡게 칠한다 [가정 M4-2b].
// 빛: 주광(그림자 받음) + 주변광 SH + SSAO(렌더러에 켜져 있다). 바다·호수 반투명 비교용으로 _UseAlpha·블렌드 속성을 둔다(기본 불투명).
Shader "Sinnoh/BoardHex"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Edge Width (inradius fraction)", Range(0, 0.2)) = 0.03
        _EdgeShade ("Edge Shade", Range(0, 1)) = 0.72
        [HideInInspector] _UseAlpha ("Use Alpha", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher: 모든 패스가 같은 UnityPerMaterial
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _EdgeWidth;
            half _EdgeShade;
            half _UseAlpha;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // 공간 일그러짐(M9 다듬기 P39 — Sinnoh.Client.BoardRipple이 전역 값으로 넣는다, 꺼져 있으면 0 = 그대로): 가운데에서 닿는 거리 안의 꼭짓점을 물결로 민다.
        // 꼭짓점 단계의 sin 셋뿐이라(화면 긁어 오기 · 덧 RT 없음) WebGL에서도 그대로 돈다. BoardIcon에 같은 함수가 있다 — 함께 고친다
        float4 _SinnohRipple;       // xyz 가운데(월드) · w 닿는 거리(0 = 꺼짐)
        float4 _SinnohRippleWave;   // x 진폭(유닛) · y 위상(rad) · z 물결 수(rad/유닛)
        float3 SinnohRipple(float3 ws)
        {
            float m = saturate(1.0 - distance(ws.xz, _SinnohRipple.xz) / max(_SinnohRipple.w, 0.0001));
            float a = _SinnohRippleWave.x * m * m * (3.0 - 2.0 * m);
            float k = _SinnohRippleWave.z, ph = _SinnohRippleWave.y;
            return ws + float3(sin(ws.z * k + ph), 0.5 * sin((ws.x + ws.z) * k * 0.7 + 2.0 * ph), cos(ws.x * k + ph)) * a;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 rim : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float rim : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 ws = SinnohRipple(TransformObjectToWorld(input.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(ws);
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.rim = input.rim.x;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tint = input.color;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                tint.rgb = SRGBToLinear(tint.rgb);
            #endif
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * tint * _BaseColor;

                float w = max(fwidth(input.rim), 1e-4);
                float edgeStart = 1.0 - _EdgeWidth;
                albedo.rgb *= lerp(1.0, _EdgeShade, smoothstep(edgeStart - w, edgeStart + w, input.rim));

                half3 n = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 direct = mainLight.color * (saturate(dot(n, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                half3 ambient = SampleSH(n);
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                ambient *= ao.indirectAmbientOcclusion;
                direct *= ao.directAmbientOcclusion;
            #endif
                half3 color = MixFog(albedo.rgb * (ambient + direct), input.fogFactor);
                return half4(color, lerp(1.0, albedo.a, _UseAlpha));
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformWorldToHClip(SinnohRipple(TransformObjectToWorld(positionOS.xyz))); }
            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // SSAO(렌더러 기능, 원천 DepthNormals)가 _CameraNormalsTexture를 이 패스로 그린다
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings NormalsVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(SinnohRipple(TransformObjectToWorld(input.positionOS.xyz)));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(normalize(input.normalWS)) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
