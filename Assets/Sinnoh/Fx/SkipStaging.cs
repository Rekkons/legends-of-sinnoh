using System;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 스페이스 바로 건너뛰는 내 긴 연출(사용자 결정 87 V12 다 — 진화 39행의 결정 84 L11을 넓힌 것): 트레이너 카드 해금(48) · 카드 바꾸기(49) · 도전 VS(51) ·
    /// 거래(52) · 사당 전설 이벤트(33) · 신 강림(34) · 전설 장소(23 — 조우 전환 단계의 앞). 그 단계의 착지(끝에서 <see cref="LandMs"/> 앞)로 소리 없이 보낸다
    /// (<see cref="FxSequencer.SkipTo"/>) — 지금 항목 안에서만 가므로 뒤 단계·뒤 항목과 방장 길이는 그대로다. 남 좌석의 항목은 받지 않는다(부르는 쪽 <c>GameFlow.SkipKey</c>).
    /// </summary>
    public static class SkipBeats
    {
        /// <summary>건너뛴 뒤 남기는 꼬리(끝 모습을 잠깐 본다).</summary>
        public const int LandMs = 400;

        static readonly int[] Rows = { CardRows.Unlock, CardRows.Swap, DealRows.Vs, DealRows.Trade, ShrineRows.Legend, ShrineRows.Descend };

        /// <summary>그 행이 건너뛸 수 있는 긴 연출인가.</summary>
        public static bool Skips(int row) => Array.IndexOf(Rows, row) >= 0;

        /// <summary>재생 중인 항목의 지금 시각에서 스페이스 바가 보낼 항목 안 시각. 건너뛸 것이 없으면 −1.</summary>
        public static int TargetOf(FxEntry entry, int localMs)
        {
            if (entry == null) return -1;
            var site = LegendSiteBeats.StepOf(entry, out _);
            if (site != null && localMs >= site.StartMs && localMs < site.StartMs + Math.Min(site.Ms, LegendSiteBeats.Ms)) return site.StartMs + Math.Min(site.Ms, LegendSiteBeats.Ms);
            foreach (var step in entry.Steps)
            {
                if (localMs < step.StartMs || localMs >= step.EndMs || !Skips(step.Row)) continue;
                int to = step.EndMs - Math.Min(LandMs, step.Ms);
                return localMs < to ? to : -1;
            }
            return -1;
        }
    }
}
