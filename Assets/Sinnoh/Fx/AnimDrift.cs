using System;
using System.Globalization;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 연출 실접속의 자(plan M7 완료 조건 6, 02 §5-7): 방 판에서 갱신을 받을 때마다 방장 멈춤의 끝(<c>ClockState.AnimLeftMs</c> — 방장이 그 갱신을 보낸 때 남은 ms)과
    /// 내 화면 대기열의 끝(그 갱신을 넣은 뒤 남은 길이 ms)의 차이를 적는다. 차이 = 방장 − 내 화면:
    /// 양수 = 방장이 더 오래 멈춘다(내 화면은 먼저 끝나 기다린다) · 음수 = 내 화면이 아직 보이는 중에 방장 멈춤이 끝난다(다음 선택의 시계가 먼저 돈다).
    /// 화면은 줄(<see cref="Line"/>)을 로그에 남기고 최댓값(<see cref="MaxMs"/> · <see cref="MinMs"/> · <see cref="WorstMs"/>)을 쥔다 — 확인용 판·시험이 읽는다.
    /// </summary>
    public sealed class AnimDrift
    {
        /// <summary>적은 갱신 수.</summary>
        public int Count { get; private set; }
        /// <summary>가장 큰 양수 차이(방장이 더 긴 쪽, 없으면 0) · 가장 큰 음수 차이(내 화면이 더 긴 쪽, 없으면 0) · 마지막 차이.</summary>
        public long MaxMs { get; private set; }
        public long MinMs { get; private set; }
        public long LastMs { get; private set; }
        /// <summary>절댓값이 가장 큰 차이(부호 그대로).</summary>
        public long WorstMs => Math.Abs(MinMs) > MaxMs ? MinMs : MaxMs;

        /// <summary>갱신 하나를 적고 차이(방장 − 내 화면, ms)를 돌려준다.</summary>
        public long Note(long hostLeftMs, long mineLeftMs)
        {
            long d = Math.Max(0, hostLeftMs) - Math.Max(0, mineLeftMs);
            Count++;
            LastMs = d;
            if (d > MaxMs) MaxMs = d;
            if (d < MinMs) MinMs = d;
            return d;
        }

        /// <summary>로그 한 줄: <c>[Sync] seat=1 v=42 events=3 host=1200 mine=1180 diff=+20 max=+35 min=-12</c>.</summary>
        public string Line(int seat, int version, int events, long hostLeftMs, long mineLeftMs)
        {
            var c = CultureInfo.InvariantCulture;
            return "[Sync] seat=" + seat.ToString(c) + " v=" + version.ToString(c) + " events=" + events.ToString(c) + " host=" + Math.Max(0, hostLeftMs).ToString(c) + " mine=" + Math.Max(0, mineLeftMs).ToString(c)
                + " diff=" + Signed(LastMs) + " max=" + Signed(MaxMs) + " min=" + Signed(MinMs);
        }

        static string Signed(long v) => (v > 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture);
    }
}
