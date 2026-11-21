using System;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 슬라이더의 셈(값은 0 ~ max의 정수 단계): 손잡이가 움직이는 폭 = 홈 폭 − 손잡이 폭이고, 누른 자리는 손잡이의 가운데가 된다.
    /// 자리는 정수 px(0.5는 올림) — 도트 그림이 반 픽셀에 걸치지 않게.
    /// </summary>
    public static class SliderMath
    {
        public static int Clamp(int value, int max) => Math.Max(0, Math.Min(Math.Max(0, max), value));

        /// <summary>홈 안의 자리 x(홈 왼쪽 끝이 0)를 누르거나 끌었을 때의 값(가장 가까운 단계).</summary>
        public static int ValueAt(float x, float trackWidth, float knobWidth, int max)
        {
            float run = trackWidth - knobWidth;
            if (max <= 0 || !(run > 0f)) return 0;
            return Clamp((int)Math.Floor((x - knobWidth * 0.5f) / run * max + 0.5f), max);
        }

        /// <summary>그 값일 때 손잡이의 왼쪽 끝(홈 왼쪽 끝이 0).</summary>
        public static int KnobLeft(int value, float trackWidth, float knobWidth, int max)
        {
            float run = trackWidth - knobWidth;
            if (max <= 0 || !(run > 0f)) return 0;
            return (int)Math.Floor(run * Clamp(value, max) / max + 0.5f);
        }
    }
}
