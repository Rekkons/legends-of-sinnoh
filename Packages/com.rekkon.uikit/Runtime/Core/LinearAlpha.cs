using System;

namespace Rekkon.UiKit
{
    /// <summary>
    /// Linear 색 공간 알파 보정. 프로젝트가 Linear면 반투명은 sRGB 화면(목업)에서 고른 알파보다 옅게 섞인다(0.5 덮개가 0.27만 가린다).
    /// 목업에서 고른 알파 a를 여기 넣어 나온 값을 Unity에 쓴다. 토큰 USS의 <c>scrim()</c>(Tools~/build-uss.mjs)과 같은 식이다 —
    /// 이쪽은 실행 중에 알파가 바뀌는 연출(덮개가 어두워지는 트윈, 빛)이 쓴다.
    /// </summary>
    public static class LinearAlpha
    {
        public const double Gamma = 2.2;

        /// <summary>어둡게 덮기(검정에 가까운 반투명 판): <c>1 − (1 − a)^2.2</c>.</summary>
        public static float Dim(float a) => (float)(1.0 - Math.Pow(1.0 - Clamp(a), Gamma));

        /// <summary>어두운 바탕 위 흰 빛: <c>a^2.2</c>.</summary>
        public static float Glow(float a) => (float)Math.Pow(Clamp(a), Gamma);

        static double Clamp(float a) => a <= 0f ? 0.0 : a >= 1f ? 1.0 : a;
    }
}
