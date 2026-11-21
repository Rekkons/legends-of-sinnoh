using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 손님 화면의 시계 표시(02 §5-7 끝 줄 — isekai ClockView, M7 「화면 ②」 응답 시계): 시계는 호스트만 센다. 화면은 받은 <see cref="ClockState"/>의 남은 ms를 받은 뒤 지난 시간만큼 자기 화면에서만 줄인다.
    /// 방장 멈춤(연출 — <see cref="ClockState.AnimLeftMs"/>)과 내 차단 컷신(<see cref="ClockState.HoldLeftMs"/>) 가운데 늦게 끝나는 쪽까지는 줄지 않고, 일시정지 중에는 줄지 않는다.
    /// 기본 시간이 다 되면 뱅크를 쓰는 시계(MapTurn · BattleAction — 02 §5-5)는 뱅크가 준다. 손님은 BattleAction이 자기 포켓몬인지 모르므로 BattleAction은 늘 뱅크를 쓰는 것으로 보인다 [가정 M7-51].
    /// </summary>
    public static class ClockView
    {
        /// <summary>남은 시간이 이 밑이면 빨갛게 하고 소리를 낸다(02 §5-3 끝 줄 「남은 10초」 — 기본 시간 + 쓸 수 있는 뱅크).</summary>
        public const long LowMs = 10000;

        /// <summary>받은 시계로부터 지난 ms 가운데 시계가 실제로 흐른 ms(일시정지 = 0, 연출·차단 컷신이 끝나기 전 = 0).</summary>
        public static long Elapsed(ClockState c, long sinceMs)
        {
            if (c == null || sinceMs <= 0) return 0;
            if (c.PausedBy != null && c.PausedBy.Count > 0) return 0;
            long frozen = Math.Max(c.AnimLeftMs, c.HoldLeftMs);
            return Math.Max(0, sinceMs - frozen);
        }

        /// <summary>기본 시간이 다 된 뒤 뱅크를 쓰는 시계인가(02 §5-5 — MapTurn과 BattleAction).</summary>
        public static bool UsesBank(ClockKind kind) => kind == ClockKind.MapTurn || kind == ClockKind.BattleAction;

        /// <summary>그 좌석의 기본 시간 남은 ms(흐르는 시계가 없으면 0).</summary>
        public static long LeftMs(SeatClock s, long elapsed) => s == null || s.Running == ClockKind.None ? 0 : Math.Max(0, s.LeftMs - Math.Max(0, elapsed));

        /// <summary>그 좌석의 뱅크 남은 ms: 뱅크를 쓰는 시계의 기본 시간이 다 된 뒤부터 준다.</summary>
        public static long BankMs(SeatClock s, long elapsed)
        {
            if (s == null) return 0;
            if (s.Running == ClockKind.None || !UsesBank(s.Running)) return Math.Max(0, s.BankMs);
            long over = Math.Max(0, elapsed) - s.LeftMs;
            return over <= 0 ? Math.Max(0, s.BankMs) : Math.Max(0, s.BankMs - over);
        }

        /// <summary>그 좌석이 이 선택에 쓸 수 있는 남은 ms(기본 시간 + 뱅크를 쓰는 시계면 뱅크). 흐르는 시계가 없으면 0.</summary>
        public static long TotalMs(SeatClock s, long elapsed) =>
            s == null || s.Running == ClockKind.None ? 0 : LeftMs(s, elapsed) + (UsesBank(s.Running) ? BankMs(s, elapsed) : 0);

        /// <summary>남은 10초 안인가(흐르는 시계가 있을 때만).</summary>
        public static bool Low(SeatClock s, long elapsed) => s != null && s.Running != ClockKind.None && TotalMs(s, elapsed) <= LowMs;

        /// <summary>흐르는 시계가 있는 좌석(나를 뺀다 — me = −1이면 모두), 좌석 순서. 응답 대기 줄 「응답 대기 중: ○○ 0:23」의 좌석들.</summary>
        public static List<int> Waiting(ClockState c, int me)
        {
            var r = new List<int>();
            if (c?.Seats == null) return r;
            for (int i = 0; i < c.Seats.Count; i++)
                if (i != me && c.Seats[i] != null && c.Seats[i].Running != ClockKind.None) r.Add(i);
            return r;
        }

        /// <summary>그 시계 종류의 한 단위 길이(ms — 막대의 전체 길이). 길이 표는 방 설정의 <see cref="RoomSettings.ClockLengths"/>(직접 설정 포함 — 결정 140 ①). 시계가 꺼졌거나 모르는 종류면 0.</summary>
        public static int LengthMs(ClockSettings s, ClockKind kind)
        {
            if (s == null || !s.On) return 0;
            switch (kind)
            {
                case ClockKind.MapTurn: return s.MapTurnMs;
                case ClockKind.BattleAction: return s.BattleActionMs;
                case ClockKind.Reaction: return s.ReactionMs;
                case ClockKind.Respond: return s.RespondMs;
                case ClockKind.Setup: return Math.Max(s.VillageMs, s.SetupPickMs);
                default: return 0;
            }
        }

        /// <summary>
        /// 그 선택 단계의 한 단위 길이(ms — 막대의 전체 길이, M8 · M9 마감 감사 2차 참고 ①): 셋업은 고향 마을 · 스타터(트레이너 카드) 고르기가 각자 길이, 출전 순서는 출전 길이 —
        /// 방의 <see cref="TurnClock"/>이 단위를 만드는 것과 같다. 그 밖은 <see cref="LengthMs(ClockSettings, ClockKind)"/>.
        /// </summary>
        public static int LengthMs(ClockSettings s, ClockKind kind, ChoiceKind choice)
        {
            if (s == null || !s.On) return 0;
            if (kind == ClockKind.Setup) return choice == ChoiceKind.VillagePick ? s.VillageMs : s.SetupPickMs;
            if (kind == ClockKind.BattleAction && choice == ChoiceKind.Lineup) return s.LineupMs;
            return LengthMs(s, kind);
        }

        /// <summary>화면의 「분:초」(0:23 · 2:00). 초는 올림(0.2초 남음 = 0:01) — 0이면 0:00.</summary>
        public static string Text(long ms)
        {
            long secs = ms <= 0 ? 0 : (ms + 999) / 1000;
            return (secs / 60).ToString(CultureInfo.InvariantCulture) + ":" + (secs % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
