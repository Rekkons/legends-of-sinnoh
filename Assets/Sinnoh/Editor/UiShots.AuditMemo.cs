using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M8 · M9 마감 감사 2차 「실패 아님 · 메모」(<PROJECT>\Logs\Review\m9\close-audit\README.md 2차 절)의 장면.
    /// ② 턴 배지 = 넘김 연출이 시작하는 순간 넘겨받은 좌석(plan §5 #776) · ④ 내 선택이 시간 초과로 닫히면 막 안내 창도 닫힌다(#777).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] AuditMemoScreens =
        {
            Screen("memo-turn-pass", s => TurnPassMemo(s)),          // 내가 [턴 종료]를 누른 뒤 넘김 띠 한가운데: 턴 배지 · 칩 강조는 이미 좌석 2
            Screen("memo-timeout-open", s => TimeoutMemo(s, false)), // 내 턴에 막 안내 창이 떠 있고 시간이 거의 다 됐다(고치기 전에는 이 창이 시간 초과 뒤에도 남았다)
            Screen("memo-timeout-shut", s => TimeoutMemo(s, true)),  // 시간 초과(기본 답 = 턴 종료) 뒤: 막 안내 창이 닫혔다
        };

        static string Plain(string text) => text?.Replace("\u2060", "");

        static void TurnPassMemo(UiShot s)
        {
            var flow = MyTurn(s, 2, null);
            int other = 1 - flow.Client.Seat;
            EndActive(flow);
            for (int i = 0; i < 200 && flow.Hud.Band.ActiveRow != FxRows.Handoff; i++) Pass(flow, 50);
            if (flow.Hud.Band.ActiveRow != FxRows.Handoff) throw new InvalidOperationException("넘김 띠가 돌지 않았다");
            Pass(flow, 400);   // 띠 한가운데
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            string badge = Plain(root.Q<Label>("board-hud-turn")?.text);
            if (flow.Hud.Band.ActiveRow != FxRows.Handoff) throw new InvalidOperationException("넘김 띠가 아니다(" + flow.Hud.Band.ActiveRow + ")");
            if (badge != Plain(flow.SeatName(other))) throw new InvalidOperationException("넘김 띠 동안 턴 배지가 넘겨받은 좌석이 아니다: " + badge);
            if (!root.Q("board-hud-seat-" + (other + 1)).ClassListContains("sn-hud-seat-turn") || root.Q("board-hud-seat-" + (flow.Client.Seat + 1)).ClassListContains("sn-hud-seat-turn"))
                throw new InvalidOperationException("칩 강조가 넘겨받은 좌석이 아니다");
            UnityEngine.Debug.Log($"[UiShots] memo-turn-pass {s.Grade.Height}: 띠 {flow.Fx.Sequencer.LocalMs} ms · 배지 {badge}");
        }

        // 좌석 둘 판 · 맵 턴 시계 30초: 내 턴에 막 안내 창(막 배지)을 열고 시간 초과 직전(shut = 시간 초과 뒤)까지 보낸다
        static void TimeoutMemo(UiShot s, bool shut)
        {
            var flow = s.Flow();
            var clocks = new int[(int)ClockKind.Respond + 1];
            clocks[(int)ClockKind.MapTurn] = 30000;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 2, Seed, AiSeed, null, false, clocks);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Client.View.ActiveSeat == f.Client.Seat && f.Client.Choice(ChoiceKind.MapAction) != null);
            if (!flow.OpenAct()) throw new InvalidOperationException("막 안내 창이 열리지 않았다");
            long was = flow.TimedOutAtMs;
            int left = flow.ClockLeftMs(flow.Client.Choice(ChoiceKind.MapAction), out _);
            if (left <= 0) throw new InvalidOperationException("맵 턴 시계가 흐르지 않는다");
            Pass(flow, left - 2000);
            if (!shut)
            {
                if (!(flow.Stage.Current is ActScreen) || flow.TimedOutAtMs != was) throw new InvalidOperationException("시간 초과 전인데 막 안내 창이 없다");
            }
            else
            {
                for (int i = 0; i < 100 && flow.TimedOutAtMs == was; i++) Pass(flow, 100);
                if (flow.TimedOutAtMs == was) throw new InvalidOperationException("시간 초과가 나지 않았다");
                Pass(flow, 300);
                if (flow.Stage.Current is ActScreen) throw new InvalidOperationException("시간 초과 뒤에도 막 안내 창이 남았다");
                if (flow.Client.View.ActiveSeat == flow.Client.Seat) throw new InvalidOperationException("시간 초과 기본 답(턴 종료)으로 턴이 넘어가지 않았다");
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            UnityEngine.Debug.Log($"[UiShots] memo-timeout-{(shut ? "shut" : "open")} {s.Grade.Height}: 창 {flow.Stage.Current?.Name ?? "보드"} · 턴 좌석 {flow.Client.View.ActiveSeat + 1}");
        }
    }
}
