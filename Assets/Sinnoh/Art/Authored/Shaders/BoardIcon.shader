// 고정물 평면 아이콘(M4-2c, d3-3 §2-2): 칸 윗면 위에 눕힌 그림(Gen/Board/Icons, 투명 가장자리). 빛 없이 그림 그대로(칸 윗면이 틴트 약 1.0배로 보이게 맞춘 빛과 같은 밝기, d3-3 §3),
// 알파 블렌드(먼 줌에서 밉맵 가장자리가 깎이지 않게), 깊이는 쓰지 않고 견주기만 한다 — 앞 칸·피규어가 가린다.
Shader "Sinnoh/BoardIcon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 공간 일그러짐(M9 다듬기 P39): 칸(BoardHex)과 같은 함수 · 같은 전역 값 — 아이콘이 일렁이는 칸 윗면을 따라간다. 함께 고친다
            float4 _SinnohRipple;
            float4 _SinnohRippleWave;
            float3 SinnohRipple(float3 ws)
            {
                float m = saturate(1.0 - distance(ws.xz, _SinnohRipple.xz) / max(_SinnohRipple.w, 0.0001));
                float a = _SinnohRippleWave.x * m * m * (3.0 - 2.0 * m);
                float k = _SinnohRippleWave.z, ph = _SinnohRippleWave.y;
                return ws + float3(sin(ws.z * k + ph), 0.5 * sin((ws.x + ws.z) * k * 0.7 + 2.0 * ph), cos(ws.x * k + ph)) * a;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(SinnohRipple(TransformObjectToWorld(input.positionOS.xyz)));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                return half4(MixFog(c.rgb, input.fogFactor), c.a);
            }
            ENDHLSL
        }
    }
}
