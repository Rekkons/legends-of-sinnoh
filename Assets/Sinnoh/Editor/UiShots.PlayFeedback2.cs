using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 해 보기 피드백 2(사용자 결정 104 · 105 · 106)의 확인 장면: 배틀 결과 창이 떠 있는 동안에는 그 창을 말하는 단계만 뜨고([확인]으로 끝난다) 나머지는 창이 닫힌 뒤 ·
    /// 본편 배틀 설명(스피드 · 주사위 6면 · 계산 줄 · 상성 · 우선권 토큰)과 포획 설명(등급별 포획률 · 볼별 주사위 수) · E6 우선권 토큰 · 내가 고른 트레이너 카드는 앞면 그대로 ·
    /// 고향 마을 고르기의 자리 점. 그림은 <c>Logs/Review/play-feedback2/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] PlayFeedback2Screens =
        {
            // 결정 104 ①: 결과 창을 말하는 단계 = 구멍이 결과 창([확인] 포함), [다음] 없음 → [확인]이 끝낸다
            Screen("pf2-tut-s308", s => Pf2Tut(s, "main", "S308", closing: true)),
            Screen("pf2-tut-s407", s => Pf2Tut(s, "main", "S407", closing: true)),
            Screen("pf2-tut-s610", s => Pf2Tut(s, "main", "S610", closing: true)),
            // 그 뒤 단계는 결과 창이 닫힌 보드에서 뜬다
            Screen("pf2-tut-s307", s => Pf2Tut(s, "main", "S307", hole: false)),
            Screen("pf2-tut-s309", s => Pf2Tut(s, "main", "S309")),
            Screen("pf2-tut-s413", s => Pf2Tut(s, "main", "S413")),
            Screen("pf2-tut-s614", s => Pf2Tut(s, "main", "S614")),
            Screen("pf2-tut-e06-s015", s => Pf2Tut(s, "e06", "S015")),
            Screen("pf2-tut-e12-s011", s => Pf2Tut(s, "e12", "S011")),
            // 결과 창이 [확인]을 기다리는 동안 말풍선이 없다(E6 마지막 공격 뒤 — 다음 단계 S015는 보드의 막 배지)
            Screen("pf2-tut-wait", Pf2Wait),
            // 결정 104 ②: 본편 3장 배틀 설명
            Screen("pf2-tut-s305", s => Pf2Tut(s, "main", "S305")),
            Screen("pf2-tut-s310", s => Pf2Tut(s, "main", "S310")),
            Screen("pf2-tut-s311", s => Pf2Tut(s, "main", "S311")),
            Screen("pf2-tut-s312", s => Pf2Tut(s, "main", "S312", hole: false)),
            Screen("pf2-tut-s306", s => Pf2Tut(s, "main", "S306")),
            // 6장: 우선권 토큰 · 델빌 상성 · 본편 끝의 「배틀 더 알기」 안내
            Screen("pf2-tut-s613", s => Pf2Tut(s, "main", "S613")),
            Screen("pf2-tut-s604", s => Pf2Tut(s, "main", "S604")),
            Screen("pf2-tut-s607", s => Pf2Tut(s, "main", "S607", hole: false)),
            Screen("pf2-tut-s615", s => Pf2Tut(s, "main", "S615", hole: false)),
            // E6: 우선권 토큰(누가 갖나 · 언제 쓰나 · 쓰면 넘어간다) · 타입 둘 상성
            Screen("pf2-tut-e06-s017", s => Pf2Tut(s, "e06", "S017")),
            Screen("pf2-tut-e06-s018", s => Pf2Tut(s, "e06", "S018")),
            Screen("pf2-tut-e06-s009", s => Pf2Tut(s, "e06", "S009")),
            Screen("pf2-tut-e06-s019", s => Pf2Tut(s, "e06", "S019")),
            Screen("pf2-tut-e06-s020", s => Pf2Tut(s, "e06", "S020")),
            // 결정 105: 포획 설명(등급별 포획률 — 조우 카드 · 상대 정보 기둥의 포획률 칩 · 볼별 주사위 수)
            Screen("pf2-tut-s410", s => Pf2Tut(s, "main", "S410")),
            Screen("pf2-tut-s411", s => Pf2Tut(s, "main", "S411")),
            Screen("pf2-tut-s412", s => Pf2Tut(s, "main", "S412")),
            Screen("pf2-tut-s406", s => Pf2Tut(s, "main", "S406")),
            // 창 닫기 부분이 먼저인 단계(대사가 「창을 닫고」를 말한다)
            Screen("pf2-tut-s404", s => Pf2Tut(s, "main", "S404")),
            Screen("pf2-tut-e04-s010", s => Pf2Tut(s, "e04", "S010")),
            // 결정 104 ③: [확정] 뒤 내가 고른 트레이너 카드는 앞면 그대로(뒷면으로 뒤집었다 다시 뒤집지 않는다)
            Screen("pf2-card-locked", s => Pf2Card(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Chosen && Equals(s2.Tag, ChoiceKind.TrainerCardPick), 1000)),
            Screen("pf2-card-reveal", s => Pf2Card(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Reveal, 300)),
            Screen("pf2-card-gloss", s => Pf2Card(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Reveal, 850)),
            // 결정 106: 고향 마을 고르기 — 보드 위 마을 자리 점(가리킨 · 고른 마을은 금색 고리 + 이름표)
            Screen("pf2-village", s => Pf2Village(s, -1, -1)),
            Screen("pf2-village-hover", s => Pf2Village(s, 3, -1)),
            Screen("pf2-village-picked", s => Pf2Village(s, -1, 0)),
            Screen("pf2-village-both", s => Pf2Village(s, 5, 0)),
            Screen("pf2-tut-s101", s => Pf2Tut(s, "main", "S101")),
        };

        // 그 단계가 뜬 꼴: 결과 창이 떠 있으면 그 창을 말하는 단계뿐이고, closing이면 [다음] 없이 구멍이 결과 창 [확인]을 품는다
        static void Pf2Tut(UiShot s, string script, string stepId, bool closing = false, bool hole = true)
        {
            var t = TutStart(s, TutorialScriptAsset(s.Board.Ctx, script), stepId);
            for (int i = 0; i < 4; i++) TutSettle(s);
            var flow = s.Made;
            var step = t.Runner.Step;
            string key = script + "." + stepId;
            if (hole != (t.Holes.Count > 0)) throw new InvalidOperationException($"{key}: 구멍 {t.Holes.Count}(앵커 {step.Anchor})");
            if (flow.BattleOver && !TutorialLayerAnchor.InResult(step.Anchor)) throw new InvalidOperationException($"{key}: 배틀 결과 창이 떠 있는데 그 창 밖을 말하는 단계가 떴다");
            if (t.Closing != closing) throw new InvalidOperationException($"{key}: 결과 창을 닫는 단계 {t.Closing}(기대 {closing})");
            var next = t.Element.Q("tut-layer-next");
            bool nextShown = next != null && next.resolvedStyle.display != DisplayStyle.None;
            if (closing)
            {
                if (!flow.BattleOver) throw new InvalidOperationException(key + ": 결과 창이 없다");
                if (nextShown) throw new InvalidOperationException(key + ": 결과 창을 닫는 단계에 [다음]이 있다");
                var ok = flow.Stage.Root.Q("battle-result-ok") ?? throw new InvalidOperationException(key + ": 결과 창 [확인]이 없다");
                if (!t.Holes[0].Overlaps(ok.worldBound)) throw new InvalidOperationException($"{key}: 구멍 {t.Holes[0]}이 [확인] {ok.worldBound}을 품지 않는다");
                if (!t.ShownText.Contains("[" + Strings()["screen.battle.result.ok"] + "]")) throw new InvalidOperationException(key + ": 대사가 [확인]을 말하지 않는다");
            }
            else if (step.Advance == StepAdvance.Next)
            {
                // 창 닫기 부분(…-close)을 비추는 동안은 [다음]이 없다 — 창을 닫아야 [다음]이 선다(결정 107 ② · 07 T201). 그 밖의 [다음] 단계에는 있다
                bool held = t.PartName != null && t.PartName != "battle-result-ok" && TutorialLayerAnchor.IsDismiss(t.PartName);
                if (held && nextShown) throw new InvalidOperationException(key + ": 창 닫기 부분(" + t.PartName + ")을 비추는데 [다음]이 있다(결정 107 ②)");
                if (!held && !nextShown) throw new InvalidOperationException(key + ": [다음]이 없다");
            }
            TutApart(s, t);
            Debug.Log($"[UiShots] pf2-tut {s.Grade.Height} {key}: 앵커 {step.Anchor ?? "-"} · 비추는 부분 {t.PartName ?? "-"} · 구멍 {t.Holes.Count} · 자리 {t.DockAt} · 결과 창 {flow.BattleOver} · 닫는 단계 {t.Closing} · 맨 위 {flow.Top?.Name ?? "-"}");
        }

        // E6 마지막 공격(S014) 뒤: 결과 창이 [확인]을 기다리는 동안 다음 단계(S015 — 보드의 막 배지)는 뜨지 않는다 → [확인] 뒤에 뜬다
        static void Pf2Wait(UiShot s)
        {
            var t = TutStart(s, TutorialScriptAsset(s.Board.Ctx, "e06"), "S014");
            var flow = s.Made;
            var tut = flow.Tutorial;
            tut.Runner.Play();
            for (int i = 0; i < 600 && !flow.BattleOver; i++) Pass(flow, 100);
            if (!flow.BattleOver) throw new InvalidOperationException("결과 창이 뜨지 않았다");
            if (tut.Runner.Step == null || tut.Runner.Step.Id != "S015") throw new InvalidOperationException("다음 단계가 S015가 아니다: " + tut.Runner.Step?.Id);
            for (int i = 0; i < 50; i++)
            {
                Pass(flow, 100);
                if (tut.Layer != null && tut.Layer.Shown) throw new InvalidOperationException("배틀 결과 창이 떠 있는데 다음 말풍선(S015)이 떴다");
            }
            if (tut.Layer.Element.resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("층이 보인다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            Debug.Log($"[UiShots] pf2-tut-wait {s.Grade.Height}: 결과 창 {flow.BattleOver} · 말풍선 {tut.Layer.Shown} · 다음 단계 {tut.Runner.Step.Id}({tut.Runner.Step.Anchor})");
        }

        // [확정] 뒤 내 카드: 앞면 그대로(뒷면 요소가 보이지 않는다) · 고르지 않은 둘은 물러난다
        static void Pf2Card(UiShot s, Func<Sinnoh.Fx.FxStep, bool> step, int ms)
        {
            var flow = Picked(s, step, ms);
            s.Panel.Tick(3);
            var pick = (SetupPickScreen)flow.Stage.Current;
            if (pick.CardsFaceUp < 1) throw new InvalidOperationException("고른 카드가 앞면이 아니다(앞면 " + pick.CardsFaceUp + ")");
            int backs = 0;
            foreach (var b in flow.Stage.Root.Query(className: "sn-pick-back").ToList())
                if (b.resolvedStyle.display != DisplayStyle.None && b.resolvedStyle.opacity > 0f && b.worldBound.width > 1f && b.parent != null && b.parent.resolvedStyle.opacity > 0.05f) backs++;
            if (backs > 0) throw new InvalidOperationException("뒷면으로 뒤집힌 카드가 보인다(" + backs + ") — 결정 104 ③");
            Debug.Log($"[UiShots] pf2-card {s.Grade.Height}: 앞면 {pick.CardsFaceUp} · 보이는 뒷면 {backs}");
        }

        // 고향 마을 고르기: 마을마다 자리 점이 보드 위(왼쪽 판 밖 · 화면 안)에 찍히고, 가리킨(hover) · 고른(pick) 마을의 점만 커진다
        static void Pf2Village(UiShot s, int hover, int pick)
        {
            var flow = Started(s);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            Pass(flow, 16);
            var screen = (SetupVillageScreen)flow.Stage.Current;
            if (pick >= 0) screen.Pick(pick);
            if (hover >= 0) screen.Point(hover);
            s.Panel.Tick(3);
            screen.PlacePins();
            s.Panel.Tick(2);
            var root = flow.Stage.Root;
            if (screen.PinsShown != screen.Villages.Count) throw new InvalidOperationException($"자리 점 {screen.PinsShown} ≠ 마을 {screen.Villages.Count}");
            var col = root.Q("setup-village-col").worldBound;
            var all = root.worldBound;
            int on = 0;
            var seen = new List<Rect>();
            for (int i = 0; i < screen.Villages.Count; i++)
            {
                string name = "setup-village-pin-" + flow.Ctx.Fixtures.Fixtures[screen.Villages[i]].Key.Substring("village.".Length);
                var pin = root.Q(name) ?? throw new InvalidOperationException(name + " 없음");
                var b = pin.worldBound;
                if (b.width < 1f || b.xMin < col.xMax || b.xMax > all.xMax || b.yMin < all.yMin || b.yMax > all.yMax) throw new InvalidOperationException($"{name}이 보드 자리 밖이다 {b}(판 {col})");
                foreach (var o in seen)
                    if (o.center == b.center) throw new InvalidOperationException(name + ": 다른 점과 같은 자리");
                seen.Add(b);
                bool big = pin.ClassListContains("sn-village-pin-on");
                if (big != (i == hover || i == pick)) throw new InvalidOperationException(name + ": 강조 " + big);
                var tag = root.Q(name + "-name");
                if ((tag.resolvedStyle.display != DisplayStyle.None) != big) throw new InvalidOperationException(name + ": 이름표는 강조한 점에만");
                if (big)
                {
                    on++;
                    var tb = tag.worldBound;
                    if (tb.xMax > all.xMax + 0.5f || tb.yMin < all.yMin - 0.5f || tb.yMax > all.yMax + 0.5f) throw new InvalidOperationException(name + ": 이름표가 화면 밖 " + tb);
                }
            }
            if (on != (hover >= 0 ? 1 : 0) + (pick >= 0 && pick != hover ? 1 : 0)) throw new InvalidOperationException("강조한 점 수 " + on);
            Debug.Log($"[UiShots] pf2-village {s.Grade.Height}: 점 {screen.PinsShown} · 가리킴 {hover} · 고름 {pick}");
        }
    }
}
