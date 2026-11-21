using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 87(M6 마감 질문 페이지 답)의 화면 몫 장면: 내 긴 연출 건너뛰기(V12) · 접는 창(V19 ①) · 배틀 기둥 날씨 · 아래 판 앰프·장작(V7 · V9) ·
    /// 빈 칸 툴팁(V5) · 실버디가 빛으로 흩어짐(V21). 다른 항목은 원래 장면이 본다(tc-board-swap · tc-battle-potion-more · tc-board-reach · ub-gauge · row-88-move · battle-necrozma-ultra).
    /// 그림은 <c>Logs/Review/m6/answers-87/ui/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Answers87Screens =
        {
            Screen("a87-skip-hint", s => SkipUnlock(s, false)),      // 내 해금 연출 중: 안내 줄 「스페이스 바: 건너뛰기」
            Screen("a87-skip-unlock", s => SkipUnlock(s, true)),     // 스페이스 바 → 착지(다음 등급 · 꼬리표)로 건너뜀, 한 번 더는 받지 않는다
            // 남은 긴 연출(V12): 건너뛴 순간(착지) · 「-end」 = 건너뛴 판과 건너뛰지 않고 같은 시각까지 보낸 판을 대기열이 빌 때까지 보내 화면 요소 · 카메라 · 대기열을 견준다
            Screen("a87-skip-vs", s => SkipLong(s, x => Versus(x, 200), false)),
            Screen("a87-skip-vs-end", s => SkipLong(s, x => Versus(x, 200), true)),
            Screen("a87-skip-trade", s => SkipLong(s, x => SeatTraded(x, 200), false)),
            Screen("a87-skip-trade-end", s => SkipLong(s, x => SeatTraded(x, 200), true)),
            Screen("a87-skip-legend", s => SkipLong(s, x => ShrineLegend(x, 200), false)),
            Screen("a87-skip-legend-end", s => SkipLong(s, x => ShrineLegend(x, 200), true)),
            Screen("a87-skip-descend", s => SkipLong(s, x => ShrineDescend(x, 200), false)),
            Screen("a87-skip-descend-end", s => SkipLong(s, x => ShrineDescend(x, 200), true)),
            Screen("a87-skip-swap", s => SkipLong(s, x => CardFxAt(x, "youngster", 49, 200), false)),
            Screen("a87-skip-swap-end", s => SkipLong(s, x => CardFxAt(x, "youngster", 49, 200), true)),
            Screen("a87-skip-site", s => SkipLong(s, x => LegendSearch(x, 300), false)),
            Screen("a87-skip-site-end", s => SkipLong(s, x => LegendSearch(x, 300), true)),
            Screen("a87-bless-fold", s => Folded(s, true)),          // 「UB 턴을 건너뛰겠습니까?」 창을 접은 띠
            Screen("a87-breach-fold", s => Folded(s, false)),        // 왜곡 균열 카드 창을 접은 띠
            Battle("a87-battle-state", BattleState),                 // 배틀 기둥의 날씨 칩 + 차례 칸의 앰프 칩
            Screen("a87-board-state", Clean(BoardState, "board-hud-actions")),   // 아래 판 「행동」 머리 줄의 장작 칩 + 위 띠 날씨 배지(이름만)
            Screen("a87-party-slot-tip", s => SlotTip(s)),           // 정비 판 빈 기술 칸의 툴팁(「기술 칸」)
            Battle("a87-silvally-scatter", SilvallyScatter),         // 실버디 격파: 금빛으로 물들어 흩어지는 중
        };

        static void SkipUnlock(UiShot s, bool skip)
        {
            UnlockAt(s, TrainerRules.Bronze, EncounterTier.Moderate, 600);
            var flow = s.Made;
            var root = flow.Stage.Root;
            var hint = root.Q("fx-skip-hint");
            if (!flow.CanSkip || hint == null || hint.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("내 해금 연출인데 건너뛰기 안내 줄이 없다");
            if (!skip) return;
            var seq = flow.Fx.Sequencer;
            var entry = seq.Current;
            var step = entry.Step(UnlockBeats.Row);
            if (!flow.SkipKey()) throw new InvalidOperationException("스페이스 바가 해금 연출을 건너뛰지 않았다");
            if (!ReferenceEquals(seq.Current, entry) || seq.LocalMs != step.EndMs - SkipBeats.LandMs) throw new InvalidOperationException("착지가 아닌 곳으로 갔다(" + seq.LocalMs + " / " + step.EndMs + ")");
            if (flow.SkipKey() || flow.CanSkip) throw new InvalidOperationException("착지 뒤에도 건너뛴다");
            s.Panel.Tick(3);
            if (flow.Cards == null || !flow.Cards.LitShown || flow.Cards.ShownTier != TrainerRules.Bronze + 1) throw new InvalidOperationException("건너뛴 뒤 다음 등급이 보이지 않는다");
            if (hint.resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("착지 뒤에도 안내 줄이 남았다");
        }

        // 내 긴 연출 하나를 스페이스 바로 건너뛴다: 같은 항목의 착지 시각으로 갔나 · 남은 대기열 길이가 건너뛴 만큼만 줄었나 · 한 번 더는 받지 않나.
        // end면 건너뛰지 않고 같은 시각까지 보낸 판(견줄 판)을 먼저 돌리고, 두 판을 대기열이 빌 때까지 똑같이 보낸 뒤의 화면 요소 · 카메라 · 대기열 · 열린 선택이 같은지 본다
        static void SkipLong(UiShot s, Action<UiShot> open, bool end)
        {
            string seek = null;
            if (end)
            {
                open(s);
                var plain = s.Made;
                var q = plain.Fx.Sequencer;
                int target = SkipBeats.TargetOf(q.Current, q.LocalMs);
                if (target < 0) throw new InvalidOperationException("건너뛸 단계가 재생 중이 아니다(견줄 판)");
                plain.Stage.Tweens.Advance(target - q.LocalMs);
                seek = SkipSettle(s, plain);
                s.Panel.Root.Clear();
            }
            open(s);
            var flow = s.Made;
            var seq = flow.Fx.Sequencer;
            var entry = seq.Current;
            var hint = flow.Stage.Root.Q("fx-skip-hint");
            int at = seq.LocalMs, to = SkipBeats.TargetOf(entry, at);
            long left = seq.LeftMs;
            if (to < 0 || !flow.CanSkip || hint == null || hint.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("내 긴 연출인데 건너뛰기 안내 줄이 없다(갈 곳 " + to + " · 항목 좌석 " + (entry != null ? entry.Seat : -9) + " · 내 좌석 " + flow.Client.Seat + " · 안 " + at + ")");
            if (!flow.SkipKey()) throw new InvalidOperationException("스페이스 바가 연출을 건너뛰지 않았다");
            if (!ReferenceEquals(seq.Current, entry) || seq.LocalMs != to) throw new InvalidOperationException("착지가 아닌 곳으로 갔다(" + seq.LocalMs + " / " + to + ")");
            if (seq.LeftMs != left - (to - at)) throw new InvalidOperationException("남은 대기열 길이가 어긋났다(" + seq.LeftMs + " / " + (left - (to - at)) + ")");
            if (flow.SkipKey() || flow.CanSkip) throw new InvalidOperationException("착지 뒤에도 건너뛴다");
            s.Panel.Tick(3);
            if (hint.resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("착지 뒤에도 안내 줄이 남았다");
            if (!end) return;
            string skipped = SkipSettle(s, flow);
            if (seek == skipped) return;
            string[] a = seek.Split('\n'), b = skipped.Split('\n');
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            throw new InvalidOperationException("건너뛴 판이 건너뛰지 않은 판과 다르다(" + i + "번째 줄): " + (i < a.Length ? a[i] : "-") + " ≠ " + (i < b.Length ? b[i] : "-"));
        }

        // 대기열이 빌 때까지 + 1초를 보낸 뒤의 모습을 글로: 대기열 · 맨 위 화면 · 열린 선택 · 카메라 · 보이는 화면 요소(이름 · 클래스 · 글 · 자리 · 불투명도)
        static string SkipSettle(UiShot s, GameFlow flow)
        {
            var seq = flow.Fx.Sequencer;
            int n = 0;
            for (; n < 400 && !seq.IsEmpty; n++) Pass(flow, 250);
            Pass(flow, 1000);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var choice = flow.Client.FirstChoice();
            var text = new System.Text.StringBuilder();
            text.Append("대기열 ").Append(seq.IsEmpty).Append(' ').Append(n).Append(" · 화면 ").Append(flow.Stage.Current != null ? flow.Stage.Current.Name : "보드").Append(" · 입력 ").Append(flow.BoardOpen)
                .Append(" · 선택 ").Append(choice != null ? choice.Kind + "/" + choice.Sub : "없음").Append('\n');
            if (s.Rig != null) text.Append("카메라 ").Append(s.Rig.transform.position.ToString("F2")).Append(' ').Append(s.Rig.transform.eulerAngles.ToString("F1")).Append('\n');
            SkipDump(flow.Stage.Root, text);
            return text.ToString();
        }

        static void SkipDump(VisualElement e, System.Text.StringBuilder text)
        {
            if (e.resolvedStyle.display == DisplayStyle.None) return;
            var box = e.worldBound;
            text.Append(e.name).Append('|').Append(string.Join(" ", e.GetClasses())).Append('|').Append(e is TextElement t ? t.text : "").Append('|')
                .Append(Math.Round(box.x)).Append(',').Append(Math.Round(box.y)).Append(',').Append(Math.Round(box.width)).Append(',').Append(Math.Round(box.height)).Append('|')
                .Append(Math.Round(e.resolvedStyle.opacity, 2)).Append('\n');
            foreach (var child in e.Children()) SkipDump(child, text);
        }

        static void Folded(UiShot s, bool bless)
        {
            if (bless) BlessAsk(s); else BreachAttach(s, false);
            var flow = s.Made;
            var root = flow.Stage.Root;
            string fold = bless ? "board-bless-fold" : "board-breach-fold", inside = bless ? "board-bless-yes" : "board-breach-go";
            if (root.Q(fold) == null || root.Q(inside) == null) throw new InvalidOperationException("[접기]가 없다(" + fold + ")");
            Func<bool> toggle = () => bless ? ((BlessScreen)flow.Top).ToggleFold() : ((BreachScreen)flow.Top).ToggleFold();
            bool ok = toggle();
            s.Panel.Tick(3);
            if (!ok || root.Q(fold) == null || root.Q(inside) != null) throw new InvalidOperationException("접히지 않았다(" + fold + ")");
            var panel = root.Q(bless ? "board-bless-panel" : "board-breach-panel").worldBound;
            if (panel.height > s.Height * 0.12f) throw new InvalidOperationException("접은 띠가 높다(" + panel.height + ")");
            // 다시 누르면 펼친다 → 한 번 더 접어 찍는다
            toggle();
            if (root.Q(inside) == null) throw new InvalidOperationException("펼쳐지지 않았다");
            toggle();
            s.Panel.Tick(3);
        }

        static void BattleState(UiShot s)
        {
            var flow = CardSeat(s, "guitarist", state =>
            {
                state.Weather = 3;
                state.Seats[0].Amp = 2;
            });
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            Fight(flow, MyTurn);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var sky = root.Q<Label>(BattleScreen.WeatherChip);
            if (sky == null || sky.text != flow.Stage.T("screen.card.weather.3")) throw new InvalidOperationException("배틀 기둥에 날씨 칩 「모래바람」이 없다");
            if (root.Q(BattleScreen.CardState + "-amp") == null) throw new InvalidOperationException("차례 칸에 앰프 칩이 없다");
        }

        static void BoardState(UiShot s)
        {
            var flow = CardSeat(s, "kindler", state =>
            {
                state.Weather = 3;
                state.Seats[0].Firewood = 3;
            });
            flow.Hud.Refresh();
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var badge = root.Q<Label>(BoardHud.WeatherBadge);
            if (badge == null || badge.text != flow.Stage.T("screen.card.weather.3")) throw new InvalidOperationException("날씨 배지가 이름만이 아니다(" + (badge != null ? badge.text : "없음") + ")");
            if (root.Q(BoardHud.CardState + "-firewood") == null) throw new InvalidOperationException("아래 판에 장작 칩이 없다");
            Apart(root, BoardHud.CardState + "-firewood", BoardHud.StampButton);
            flow.Stage.Tips.Show(badge);
        }

        static void SlotTip(UiShot s)
        {
            var flow = CardParty(s, "backpacker", 5);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var empty = root.Q(className: "sn-party-slot-empty");   // 빈 기술 칸(가방의 지닌물건을 집은 채여도 기술 칸의 툴팁은 그대로)
            if (empty == null) throw new InvalidOperationException("빈 기술 칸이 없다");
            flow.Stage.Tips.Show(empty);
        }

        static void SilvallyScatter(UiShot s)
        {
            var flow = UbAt(s, Silvally);
            FightInto(s, flow, st => UbBattleRows.First<Fainted>(st) is Fainted f && flow.Battle != null && f.PokemonId == flow.Battle.FoeId, 330);
            Seen(flow, b => b.ScatterShown > 0f && b.ScatterShown < 1f, "실버디가 빛으로 흩어지는 중");
        }
    }
}
