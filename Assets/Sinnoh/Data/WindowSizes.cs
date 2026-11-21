using System.Collections.Generic;

namespace Sinnoh.Data
{
    /// <summary>
    /// 고를 수 있는 창 크기(결정 59 — 창 테두리 끌기 없이 정해 둔 해상도만): 등급 넷의 기준 크기(04 §9-3) + 1600×900 [가정].
    /// 설정 화면(S02 「화면 크기」)은 이 가운데 모니터 안에 드는 것만 보이고, 「전체 화면」을 따로 둔다.
    /// 모니터와 같은 크기는 뺀다 [가정] — 창에는 제목 줄·테두리가 붙어 창 전체가 모니터 안에 들어가지 않는다. 그 크기는 「전체 화면」이 맡는다.
    /// </summary>
    public static class WindowSizes
    {
        public static readonly (int W, int H)[] All = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160) };

        /// <summary>모니터(폭 × 높이, 물리 px) 안에 드는 크기 — 폭·높이가 둘 다 모니터보다 작다. <see cref="All"/> 순서 그대로.</summary>
        public static List<(int W, int H)> Fit(int monitorW, int monitorH)
        {
            var fit = new List<(int W, int H)>();
            foreach (var s in All)
                if (s.W < monitorW && s.H < monitorH) fit.Add(s);
            return fit;
        }
    }
}
