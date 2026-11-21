using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 배틀 화면 묶음(2026-11-21 — 결정 169 · 170 · 171 + E 기권 확인): 포켓몬 고르기 칸의 마우스 오버 카드 · 차례 칸으로 옮긴 [운명의 변화] ·
    /// 맞는 순간(59행)에 서는 계산 줄과 누가 맞았나(이름표 · 작은 도트 · 준/받은 피해) · E 한 번 = 안내. 모두 배틀 화면(보드 위) — Inspect가 카드 · 단추 자리도 본다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] BattleUiScreens =
        {
            Battle("battle-mon-tip", s => MonTipAt(s, SendInBench(s), "battle-bench-2")),                    // 첫 출전 고르기: 둘째 칸 마우스 오버 = 그 포켓몬 카드(기술 효과까지) — 기록 기둥 위
            Battle("battle-mon-tip-next", s => MonTipAt(s, NextBench(s), "battle-bench-1")),                // 쓰러진 뒤 다음 포켓몬 고르기
            Battle("battle-mon-tip-partymove", s => MonTipAt(s, LeftBattle(s, 1, false), "battle-partymove-1")),   // 대기 포켓몬 기술 줄: 쓰는 포켓몬의 카드
            Battle("battle-mon-tip-potion", s => MonTipAt(s, PotionRow(s), "battle-ability-1")),             // lady R 회복할 포켓몬 줄
            Battle("battle-fate-turn", s => FateInTurn(s)),                                                   // 결정 170: [운명의 변화]가 차례 칸 맨 아래(스피드 줄 아래)
            Battle("battle-calc-dealt", s => CalcAt(s, false)),                                               // 결정 171: 내가 준 피해 — 맞는 순간 뒤 상성 단계 중에 이미 데미지 · 왼쪽 · 붉은 이름표
            Battle("battle-calc-taken", s => CalcAt(s, true)),                                                // 받은 피해 — 오른쪽 · 내 좌석 색 이름표
            Battle("battle-forfeit-key", s => ForfeitKey(s)),                                                 // E 한 번: 기권하지 않고 차례 칸에 「E를 한 번 더 누르면 기권합니다.」
            Battle("battle-alpha-foe-types", s => AlphaTypesAt(s, false)),                                    // 결정 182: 우두머리 상대의 기술 칩 줄 — 알파 스트라이크 옆 배울 수 있는 타입 그림
            Battle("battle-alpha-foe-tip", s => AlphaTypesAt(s, true)),                                       // 상대 포켓몬 그림에 마우스 오버 = 그 카드(「배우는 타입」 줄)
            Battle("battle-alpha-move-types", s => AlphaMoveTypes(s)),                                        // 내 기술 줄의 알파 스트라이크 행: 「타입 선택」 옆 고를 수 있는 타입 그림
        };

        // 결정 182: 우두머리 조우의 첫 출전 고르기 — 상대 기둥의 알파 스트라이크 칩 옆 타입 그림 수 = 그 포켓몬의 배울 수 있는 타입 수, 기둥 폭 안. hover = 상대 그림의 카드
        static void AlphaTypesAt(UiShot s, bool hover)
        {
            AlphaSendIn(s);
            var flow = s.Made;
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            var box = root.Q("battle-foe-alpha-types") ?? throw new InvalidOperationException("상대 알파 스트라이크 옆에 타입 그림이 없다");
            var battle = flow.Client.View.Battle;
            var side = battle.Sides[battle.Sides[0].Owner == flow.Client.Seat ? 1 : 0];
            int foe = side.Active >= 0 ? side.Active : side.Roster[0];   // 첫 출전 전 미리 보기(T14)는 명단의 첫 포켓몬
            var want = new List<int>();
            LegendRules.AlphaTypes(flow.Ctx, flow.Client.View.Pokemon[foe], want);
            int icons = box.Query(className: "sn-type-icon").ToList().Count;
            if (icons != want.Count) throw new InvalidOperationException("타입 그림 수 " + icons + " ≠ 배울 수 있는 타입 " + want.Count);
            var pillar = root.Q("battle-info-foe").worldBound;
            if (box.worldBound.xMax > pillar.xMax + 0.5f) throw new InvalidOperationException("타입 그림이 상대 기둥 밖이다(" + box.worldBound + " · " + pillar + ")");
            if (!hover) return;
            if (!screen.HoverMon("battle-foe-art")) throw new InvalidOperationException("상대 그림에 카드가 걸려 있지 않다");
            s.Panel.Tick(3);
            var tip = screen.MonTipElement;
            if (screen.MonTipShown < 0 || tip == null || tip.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("상대 포켓몬 카드가 뜨지 않았다");
            int learn = tip.Q("mon-tip-learn")?.Query(className: "sn-type-icon").ToList().Count ?? -1;
            if (learn != flow.Ctx.Cards.Pokemon[flow.Client.View.Pokemon[foe].Def].LearnTypes.Count) throw new InvalidOperationException("카드의 배우는 타입 그림 수가 다르다: " + learn);
        }

        // 결정 182: 전설 특수기술을 가진 디아루가의 차례 — 알파 스트라이크 행에 고를 수 있는 타입 그림, 행 폭 안
        static void AlphaMoveTypes(UiShot s)
        {
            var flow = AlphaStrike(s, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var box = flow.Stage.Root.Q("battle-move-alpha-types") ?? throw new InvalidOperationException("알파 스트라이크 행에 타입 그림이 없다");
            var row = box.parent.worldBound;
            if (box.worldBound.xMax > row.xMax + 0.5f) throw new InvalidOperationException("타입 그림이 기술 행 밖이다(" + box.worldBound + " · " + row + ")");
            UnityEngine.Debug.Log($"[UiShots] battle-alpha-move-types {s.Grade.Height}: 그림 {box.childCount} · 행 {row}");
        }

        // 조무래기 배틀 · 파티 셋: 첫 출전을 묻는 순간
        static GameFlow SendInBench(UiShot s)
        {
            var flow = Grunts(s, "483:2", false, 0, "484:2", "487:2");
            Fight(flow, b => b.Open && b.Bench.Count >= 2 && flow.Client.FirstChoice() != null && flow.Client.FirstChoice().Kind == ChoiceKind.SendIn);
            return flow;
        }

        // 체력 1인 잉어킹이 쓰러진 뒤 다음 포켓몬을 묻는 순간
        static GameFlow NextBench(UiShot s)
        {
            var flow = Grunts(s, "129:0", false, 1, "483:2", "484:2");
            Fight(flow, b => b.Open && b.Bench.Count > 0 && flow.Client.FirstChoice() != null && flow.Client.FirstChoice().Kind == ChoiceKind.NextPokemon);
            return flow;
        }

        static GameFlow PotionRow(UiShot s)
        {
            var flow = CardPotionMore(s);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.AbilityOpen && !screen.OpenAbility()) throw new InvalidOperationException("회복할 포켓몬 줄이 열리지 않았다");
            return flow;
        }

        // 그 칸에 마우스를 올린 카드: 떠 있고 · 기술 칸(★ + 배운 칸) 모두 · 기술 효과 글이 있다(화면 안 · 독 · DS를 가리지 않는지는 Inspect)
        static void MonTipAt(UiShot s, GameFlow flow, string cell)
        {
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.HoverMon(cell)) throw new InvalidOperationException(cell + "에 포켓몬 카드가 걸려 있지 않다");
            s.Panel.Tick(3);
            var tip = screen.MonTipElement;
            if (screen.MonTipShown < 0 || tip == null || tip.resolvedStyle.display == DisplayStyle.None || tip.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("포켓몬 카드가 뜨지 않았다");
            if (tip.Q("mon-tip-move-0") == null || tip.Q("mon-tip-move-1") == null) throw new InvalidOperationException("카드에 기술 칸(★ · 배운 칸)이 없다");
            if (tip.Q(className: "sn-mon-text") == null) throw new InvalidOperationException("카드에 기술 효과 글이 없다");
            var log = flow.Stage.Root.Q("battle-log").worldBound;
            if (tip.worldBound.xMin < log.xMin - 0.5f || tip.worldBound.xMax > log.xMax + 0.5f) throw new InvalidOperationException("포켓몬 카드가 기록 기둥 폭 밖이다(" + tip.worldBound + " · 기둥 " + log + ")");
            UnityEngine.Debug.Log($"[UiShots] battle-mon-tip {s.Grade.Height} {cell}: 카드 {tip.worldBound} · 기둥 {log}");
        }

        // 결정 170: 내 차례 — [운명의 변화]는 차례 칸 안, 스피드 줄 아래
        static void FateInTurn(UiShot s)
        {
            var flow = Grunts(s, "483:2", false, 0, "484:2", "487:2");
            Fight(flow, MyTurn);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var fate = root.Q(BattleScreen.FateButton) ?? throw new InvalidOperationException("[운명의 변화]가 없다");
            var turn = root.Q("battle-turn");
            var speed = root.Q("battle-turn-speed") ?? throw new InvalidOperationException("차례 칸 스피드 줄이 없다");
            if (!turn.Contains(fate)) throw new InvalidOperationException("[운명의 변화]가 차례 칸에 있지 않다(" + fate.parent?.name + ")");
            if (fate.worldBound.yMin < speed.worldBound.yMax - 0.5f) throw new InvalidOperationException("[운명의 변화]가 스피드 줄 아래에 있지 않다");
            if (root.Q("battle-info").Q(BattleScreen.FateButton) != null) throw new InvalidOperationException("기둥에 [운명의 변화]가 남아 있다");
        }

        // 결정 171: 그 쪽이 맞는 공격의 상성 단계(60행) + 200ms(= 맞는 순간 + 500) — 계산 단계(61행) 전에 데미지가 이미 서 있다.
        // 받는 쪽은 잉어킹이 앞에 선 판(조무래기가 먼저 쓰러지지 않고 때린다)
        static void CalcAt(UiShot s, bool taken)
        {
            var flow = taken ? Grunts(s, "129:0", false, 0, "483:2") : Grunts(s, "483:2", false, 0, "484:2", "487:2");
            bool Mine(int id) => Array.IndexOf(flow.Client.View.Seats[flow.Client.Seat].Party, id) >= 0;
            FightInto(s, flow, st => st.Row == 600 && st.Events.Count > 0 && st.Events[0] is TypeMatchup m && Mine(m.DefenderId) == taken, 200);
            var screen = (BattleScreen)flow.Stage.Current;
            var seq = flow.Fx.Sequencer;
            var calc = seq.Current != null ? seq.Current.Step(611) : null;
            if (calc == null || seq.LocalMs >= calc.StartMs) throw new InvalidOperationException("계산 단계 전이 아니다(" + seq.LocalMs + ")");
            var total = flow.Stage.Root.Q("battle-calc-total");
            if (total == null || total.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("맞는 순간 뒤 상성 단계 중에 데미지가 서 있지 않다(결정 171 ①)");
            if (screen.CalcTaken != taken) throw new InvalidOperationException("준/받은 피해가 다르다(" + screen.CalcTaken + ")");
            var tag = flow.Stage.Root.Q("battle-calc-target");
            if (tag == null || tag.ClassListContains("sn-battle-calc-tag-foe") == taken) throw new InvalidOperationException("이름표 색 갈래가 다르다");
            if (flow.Stage.Root.Q("battle-calc-face")?.Q(className: "sn-mon") == null) throw new InvalidOperationException("맞은 포켓몬의 작은 도트가 없다");
            var body = flow.Stage.Root.Q("battle-calc-body");
            UnityEngine.Debug.Log($"[UiShots] battle-calc {(taken ? "taken" : "dealt")} {s.Grade.Height}: 몸 {body.worldBound} · 칸 {flow.Stage.Root.Q("battle-calc").worldBound} · 시각 {seq.LocalMs} · 61행 {calc.StartMs}");
        }

        // E 한 번: 기권하지 않고 안내 · 답 없음
        static void ForfeitKey(UiShot s)
        {
            var flow = Grunts(s, "129:0", false, 1, "483:2", "484:2");
            var screen = Fight(flow, b => MyTurn(b) && b.ForfeitOn);
            int commands = flow.Session.CommandCount;
            if (!flow.BattleKey('E')) throw new InvalidOperationException("E를 받지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!screen.ForfeitArmed || flow.Session.CommandCount != commands || flow.Stage.Current != screen) throw new InvalidOperationException("E 한 번에 기권했거나 안내 상태가 아니다");
            var guide = flow.Stage.Root.Q("battle-guide") as TextElement;
            // Kit.Wrapped의 단어 이음표는 빼고 견준다
            if (guide == null || guide.text.Replace("⁠", "") != flow.Stage.T("screen.battle.forfeit.again")) throw new InvalidOperationException("차례 칸에 「E를 한 번 더」 안내가 없다(" + guide?.text + ")");
        }
    }
}
