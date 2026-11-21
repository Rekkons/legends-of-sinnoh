using System;

namespace Rekkon.UiKit
{
    /// <summary>툴팁이 대상의 어느 쪽에 붙나.</summary>
    public enum TipSide { Above, Below, Left, Right }

    /// <summary>툴팁 왼쪽 위 자리(패널 좌표, 정수)와 실제로 붙은 쪽.</summary>
    public readonly struct TipSpot
    {
        public readonly float X, Y;
        public readonly TipSide Side;
        public TipSpot(float x, float y, TipSide side) { X = x; Y = y; Side = side; }
    }

    /// <summary>툴팁 자리 계산(엔진 없음). 사각형은 모두 같은 좌표계(패널 좌표, 왼쪽 위가 0,0).</summary>
    public static class TipPlace
    {
        /// <summary>
        /// 대상 사각형(<paramref name="tx"/>·<paramref name="ty"/>·<paramref name="tw"/>·<paramref name="th"/> — transform을 뺀 배치 사각형)의
        /// <paramref name="prefer"/> 쪽에 <paramref name="gap"/>만큼 띄워 놓는다.
        /// <list type="bullet">
        /// <item>그쪽으로 화면 밖에 나가면 반대편으로 넘긴다. 반대편도 안 들어가면 자리가 더 넓은 쪽에 두고 화면 안으로 민다.</item>
        /// <item>위·아래에 붙을 때는 대상과 왼쪽 끝을, 왼쪽·오른쪽에 붙을 때는 위 끝을 맞추고 화면 안으로 민다.</item>
        /// <item>자리는 정수로 맞춘다(도트 글꼴이 반 픽셀에 걸치지 않게).</item>
        /// </list>
        /// </summary>
        public static TipSpot Place(float tx, float ty, float tw, float th, float tipW, float tipH, float panelW, float panelH, TipSide prefer, float gap)
        {
            bool vertical = prefer == TipSide.Above || prefer == TipSide.Below;
            // 주축(붙는 방향): 대상 앞쪽 끝·뒤쪽 끝, 툴팁 길이, 화면 길이
            float start = vertical ? ty : tx, end = vertical ? ty + th : tx + tw;
            float tip = vertical ? tipH : tipW, panel = vertical ? panelH : panelW;
            bool before = prefer == TipSide.Above || prefer == TipSide.Left;
            float roomBefore = start - gap, roomAfter = panel - end - gap;
            if (before ? tip > roomBefore && roomAfter > roomBefore : tip > roomAfter && roomBefore > roomAfter) before = !before;
            float main = Clamp(before ? start - gap - tip : end + gap, panel - tip);
            float cross = vertical ? Clamp(tx, panelW - tipW) : Clamp(ty, panelH - tipH);
            var side = vertical ? (before ? TipSide.Above : TipSide.Below) : (before ? TipSide.Left : TipSide.Right);
            return vertical ? new TipSpot(cross, main, side) : new TipSpot(main, cross, side);
        }

        // 0 ≤ v ≤ max로 밀고 정수로(0.5는 늘 올린다 — Math.Round는 짝수 쪽이라 이웃 대상의 툴팁이 1px 엇갈린다). 툴팁이 화면보다 크면(max < 0) 0.
        static float Clamp(float v, float max) => (float)Math.Floor(Math.Max(0, Math.Min(v, max)) + 0.5);
    }
}
