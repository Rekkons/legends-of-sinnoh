using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 보드의 공간 일그러짐(M9 다듬기 P39): 보드 셰이더(BoardHex · BoardIcon)의 꼭짓점 단계가 읽는 전역 값 둘을 넣는다 — 가운데에서 닿는 거리 안의 칸 · 아이콘이 물결로 밀린다.
    /// 화면을 긁어 오지 않는다(URP 불투명 텍스처에는 투명 대기열의 아이콘 · 말이 없다) — 꼭짓점의 sin뿐이라 어느 플랫폼 · 캡처에서도 같은 그림이다.
    /// 값은 <see cref="GameFlow"/>가 순수 박자 <c>RippleBeats.Board</c>에서 셈해 넣고, 꺼질 때 <see cref="Clear"/>.
    /// </summary>
    public static class BoardRipple
    {
        static readonly int Id = Shader.PropertyToID("_SinnohRipple"), WaveId = Shader.PropertyToID("_SinnohRippleWave");

        /// <summary>지금 켜져 있나 · 가운데(월드) · 닿는 거리 · 진폭(유닛) — 확인용.</summary>
        public static bool On { get; private set; }
        public static Vector3 Centre { get; private set; }
        public static float Reach { get; private set; }
        public static float Amp { get; private set; }

        /// <param name="phase">0 ~ 1(한 바퀴)</param>
        /// <param name="wave">물결 길이(유닛)</param>
        public static void Set(Vector3 centre, float reach, float amp, float phase, float wave)
        {
            On = reach > 0f && amp > 0f;
            Centre = centre;
            Reach = reach;
            Amp = amp;
            Shader.SetGlobalVector(Id, new Vector4(centre.x, centre.y, centre.z, On ? reach : 0f));
            Shader.SetGlobalVector(WaveId, new Vector4(On ? amp : 0f, phase * 2f * Mathf.PI, 2f * Mathf.PI / Mathf.Max(0.01f, wave), 0f));
        }

        public static void Clear() => Set(Vector3.zero, 0f, 0f, 0f, 1f);

        // 도메인 리로드를 끈 Play에서 앞 판의 값이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Clear();
    }
}
