using System;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 해상도 등급 = 토큰 벌(720·1080) × 패널 정수 배율(1·2). 넷뿐이다: 1280×720, 1920×1080, 2560×1440(720 × 2), 3840×2160(1080 × 2).
    /// 토큰 벌은 <c>Kit720.uss</c>·<c>Kit1080.uss</c> 중 하나를, 배율은 PanelSettings <c>ConstantPixelSize</c>의 scale을 정한다.
    /// </summary>
    public readonly struct UiGrade : IEquatable<UiGrade>
    {
        /// <summary>토큰 벌: 720 또는 1080(패널 좌표로 본 화면 높이).</summary>
        public readonly int TokenHeight;

        /// <summary>PanelSettings 배율: 1 또는 2.</summary>
        public readonly int PanelScale;

        public UiGrade(int tokenHeight, int panelScale)
        {
            if (tokenHeight != 720 && tokenHeight != 1080) throw new ArgumentOutOfRangeException(nameof(tokenHeight), tokenHeight, "토큰 벌은 720·1080뿐");
            if (panelScale != 1 && panelScale != 2) throw new ArgumentOutOfRangeException(nameof(panelScale), panelScale, "배율은 1·2뿐");
            TokenHeight = tokenHeight;
            PanelScale = panelScale;
        }

        /// <summary>이 등급의 기준 화면 높이(물리 px).</summary>
        public int Height => TokenHeight * PanelScale;

        /// <summary>이 등급의 기준 화면 폭(16:9, 물리 px).</summary>
        public int Width => Height * 16 / 9;

        /// <summary>
        /// 화면(창 안쪽) 크기 → 등급: 기준 폭·높이가 모두 들어가는 가장 큰 등급. 남는 자리는 화면 쪽이 받는다(패널은 화면 전체를 덮는다).
        /// 가장 작은 등급(1280×720)도 안 들어가면 그 등급을 그대로 쓴다(화면이 잘린다 — 더 작은 벌은 없다).
        /// </summary>
        public static UiGrade For(int screenWidth, int screenHeight)
        {
            var best = new UiGrade(720, 1);
            // 작은 것부터: 720, 1080, 1440, 2160
            foreach (var g in new[] { new UiGrade(1080, 1), new UiGrade(720, 2), new UiGrade(1080, 2) })
                if (g.Width <= screenWidth && g.Height <= screenHeight) best = g;
            return best;
        }

        public bool Equals(UiGrade other) => TokenHeight == other.TokenHeight && PanelScale == other.PanelScale;
        public override bool Equals(object obj) => obj is UiGrade g && Equals(g);
        public override int GetHashCode() => TokenHeight * 4 + PanelScale;
        public override string ToString() => Height + "(" + TokenHeight + "×" + PanelScale + ")";
    }
}
