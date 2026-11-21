using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 5(P18 · P19 — <c>Logs/Review/m9/polish/README.md</c>)의 장면.
    /// P19 = 배틀 결과 창의 스탬프 보상 규토리 줄(결정 80).
    /// P18 = 1막 목표 달성(04 §8-8 94행 941): 조무래기를 이긴 판의 결과 창 [확인] 뒤 보드 — 토큰이 움찔 → 날아오름 → 「이동력 +2」 칩 → 그 좌석의 자리로.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Polish5Screens =
        {
            Screen("p18-flinch", s => Freed18(s, 480)),     // 결과 창을 걷는 줄무늬가 열린 뒤 움찔(눌림)
            Screen("p18-lift", s => Freed18(s, 800)),       // 날아오르는 중
            Screen("p18-chip", s => Freed18(s, 1050)),      // 토큰이 사라지고 「이동력 +2」 칩이 마을 위에
            Screen("p18-chip-fly", s => Freed18(s, 1280)),  // 칩이 내 남은 이동력 배지로 날아가는 중
            Screen("p18-chip-land", s => Freed18(s, 1430)), // 배지에 닿아 흐려진다
            Screen("p19-result-stamp", s => StampReward19(s)), // P19: 이긴 판의 결과 창 — 스탬프 줄 · 조건 줄 아래 「스탬프 보상」 규토리 줄(첫 스탬프 = 빨강 · 파랑 · 노랑 +1)
        };

        // P19: 결과 창의 스탬프 보상 규토리 줄 — 칩 = 그 스탬프가 준 규토리 그대로, 줄이 결과 창 안에 들고 글이 넘치지 않는다
        static void StampReward19(UiShot s)
        {
            var flow = Win(s);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var battle = (BattleScreen)flow.Stage.Current;
            var got = flow.Battle.Stamps.Count == 1 ? flow.Battle.Stamps[0] : throw new InvalidOperationException("이 배틀로 받은 스탬프가 하나가 아니다");
            var row = root.Q("battle-result-stamp-reward") ?? throw new InvalidOperationException("결과 창에 스탬프 보상 줄이 없다");
            int want = 0;
            foreach (var c in got.Changes) if (c.Res == Sinnoh.Rules.ResKind.Apricorn) want += c.After - c.Before;
            int shown = 0;
            foreach (var kv in battle.ResultStampApricorns)
            {
                shown += kv.Value;
                var chip = root.Q("battle-result-stamp-apricorn-" + kv.Key.ToString().ToLowerInvariant()) ?? throw new InvalidOperationException("보상 줄에 " + kv.Key + " 칩이 없다");
                if (chip.childCount < 2) throw new InvalidOperationException(kv.Key + " 칩에 아이콘 + 수가 없다");
            }
            if (want == 0 || shown != want) throw new InvalidOperationException("보상 줄의 규토리 " + shown + " ≠ 스탬프가 준 " + want);
            var panel = root.Q("battle-result-panel");
            var box = panel.worldBound;
            foreach (var e in row.Query<VisualElement>().ToList())
                if (e.worldBound.xMin < box.xMin - 0.5f || e.worldBound.xMax > box.xMax + 0.5f) throw new InvalidOperationException("스탬프 보상 줄이 결과 창 밖으로 나간다");
            if (row.worldBound.yMin < root.Q("battle-result-stamp-cond").worldBound.yMax - 0.5f) throw new InvalidOperationException("보상 줄이 조건 줄과 겹친다");
            var bad = WorkUi.Overflows(panel);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            Debug.Log($"[UiShots] p19-result-stamp {s.Grade.Height}: 보상 규토리 {shown}(색 {battle.ResultStampApricorns.Count})");
        }

        // 조무래기를 이긴 판(Win)의 결과 창 [확인] 뒤 ms — HUD 요소의 자리를 읽으니 1ms 앞에서 배치를 세운다(RULE-CAPTURE)
        static GameFlow Freed18(UiShot s, int ms)
        {
            var flow = Win(s);
            var battle = (BattleScreen)flow.Stage.Current;
            for (int i = 0; i < BattleStepMax && !(battle.ShowsResult && !battle.AskingReward); i++) Pass(flow, BattleStepMs);
            if (!battle.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            Pass(flow, Math.Max(0, ms - 1));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            Pass(flow, 1);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            // 단정: 토큰은 사라지는 때(GoneAt)까지 보드에 있고, 칩은 튀어나오는 때부터 보이며 글이 칩 안에 든다
            int home = flow.Ctx.Fixtures.Fixtures[flow.Client.View.Seats[0].HomeVillage].Cell;
            var grunt = s.Board != null && s.Board.Tokens != null ? s.Board.Tokens.GruntAt(home) : null;
            bool there = grunt != null && grunt.gameObject.activeSelf;
            if (s.Board != null && there != (ms < LiberateBeats.GoneAt)) throw new InvalidOperationException("p18 " + ms + "ms: 조무래기 토큰이 " + (there ? "아직 있다" : "없다"));
            var fx = flow.Results ?? throw new InvalidOperationException("결과 층이 없다");
            if (fx.MoveShown != (ms >= LiberateBeats.ChipAtMs)) throw new InvalidOperationException("p18 " + ms + "ms: 칩이 " + (fx.MoveShown ? "보인다" : "안 보인다"));
            if (ms > LiberateBeats.ChipFlyAtMs && fx.MoveFly <= 0f) throw new InvalidOperationException("p18 " + ms + "ms: 칩이 자리로 날아가지 않는다");
            if (fx.MoveShown)
            {
                var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-result-move"));
                if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            }
            Debug.Log($"[UiShots] p18 {ms}ms {s.Grade.Height}: 토큰 {(there ? "있음" : "없음")} · 칩 {(fx.MoveShown ? "보임" : "없음")} · 날기 {fx.MoveFly:0.00}");
            return flow;
        }
    }
}
