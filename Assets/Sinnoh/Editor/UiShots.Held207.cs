using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 207(2026-11-23 「배틀화면에서 지닌물건 항상 볼 수 있도록 하자」): 배틀 기둥의 나와 있는 포켓몬마다 지닌물건이 늘 보인다 — 결정 206 ②(Q9)부터 카드의 지닌물건 동그라미
    /// (<c>battle-me-item-n</c> · <c>battle-foe-item-n</c>, 원작 토큰 그림) + 카드 마우스 오버 글 판의 이름 · 효과 글. 판은 좌석 둘의 트레이너전 — 내 첫 포켓몬(체력 1 잉어킹)이 둘, 둘째(디아루가)가 하나, 좌석 2의 스타팅 포켓몬이 하나를 지녔다.
    /// 그림 <c>Logs/Review/held/</c>. PlayMode <c>BattleHeldFlowTests</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] HeldScreens =
        {
            Battle("battle-held", s => HeldCheck(HeldDuel(s, false), 2, 1)),        // 내 차례: 내 쪽 둘 · 상대 쪽 하나
            Battle("battle-held-tip", s => HeldTip(s, HeldCheck(HeldDuel(s, false), 2, 1))),   // 내 첫 칸 마우스 오버 = 결단의 머리띠 · 효과 글
            Battle("battle-held-next", s => HeldCheck(HeldDuel(s, true), 1, 1)),    // 잉어킹이 쓰러진 뒤 나온 디아루가 — 그 포켓몬의 하나로 바뀐다
        };

        static int HeldId(Ctx ctx, string key)
        {
            int id = ctx.Items.Ids.IndexOf(key);
            if (id < 0) throw new InvalidOperationException("아이템 " + key + "가 없다");
            return id;
        }

        // 좌석 둘을 첫 좌석 고향 마을 옆의 마을 밖 칸에(도전을 거절하지 못한다 — 응답 없이 바로 배틀) · 지닌물건을 쥐여 주고 좌석 2에게 [도전]. next면 잉어킹이 쓰러진 뒤 내 차례까지
        static GameFlow HeldDuel(UiShot s, bool next)
        {
            var ctx = s.Board.Ctx;
            int lead = -1;
            var flow = Seats(s, 2, st =>
            {
                int home = st.Seats[0].Cell, cell = -1;
                for (int dir = 0; dir < 6 && cell < 0; dir++)
                {
                    int n = ctx.Board.Neighbor(home, dir);
                    if (n >= 0 && !BattleRules.InVillage(ctx, n)) cell = n;
                }
                if (cell < 0) throw new InvalidOperationException("고향 마을 옆에 마을 밖 칸이 없다");
                foreach (var seat in st.Seats) seat.Cell = cell;
                GiveTeam(ctx, st, "129:0", "483:2");
                var first = st.Pokemon[lead = st.Seats[0].Party[0]];
                first.Damage = Math.Max(0, PokemonRules.MaxHp(ctx, first) - 1);
                first.HeldItem = HeldId(ctx, HeldItems.ChoiceBand);
                first.HeldItem2 = HeldId(ctx, HeldItems.LuckyEgg);   // 둘째 칸(backpacker R의 칸 — 그림 확인용으로 쥐여 줌)
                st.Pokemon[st.Seats[0].Party[1]].HeldItem = HeldId(ctx, HeldItems.StarShield);
                st.Pokemon[st.Seats[1].Party[0]].HeldItem = HeldId(ctx, HeldItems.Eviolite);
            });
            var mine = flow.Client.FirstChoice();
            for (int i = 0; i < BattleStepMax && !(mine != null && mine.Kind == ChoiceKind.MapAction && flow.InputOpen); i++)
            {
                if (flow.InputOpen && flow.Client.View.ActiveSeat != flow.Client.Seat) EndActive(flow);   // 좌석 2가 먼저면 그 턴을 넘긴다
                Pass(flow, BattleStepMs);
                mine = flow.Client.FirstChoice();
            }
            if (mine == null || mine.Kind != ChoiceKind.MapAction)
                throw new InvalidOperationException("내 맵 행동 차례가 아니다(" + (mine != null ? mine.Kind.ToString() : "선택 없음") + " · 턴 좌석 " + flow.Client.View.ActiveSeat + " · 선택 " + string.Join(",", flow.Client.View.Pending.ConvertAll(p => p.Seat + ":" + p.Kind)) + ")");
            var challenge = flow.Client.Legal(mine).Find(a => a.Kind == AnswerKind.Challenge && a.Target == 1);
            if (challenge == null) throw new InvalidOperationException("좌석 2에게 [도전]할 수 없다");
            flow.Answer(mine, challenge);
            Duel(flow, lead, b => MyTurn(b) && (b.ShownMon(flow.Battle.MySide).Id == lead) != next);
            return flow;
        }

        // 내 쪽은 화면 단추로(첫 출전 = lead), 좌석 2는 첫 합법 답(기권 · 대기 포켓몬 기술 빼고)으로 싸운다
        static BattleScreen Duel(GameFlow flow, int lead, Func<BattleScreen, bool> until)
        {
            for (int i = 0; i < BattleStepMax * 10; i++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen != null && until(screen)) return screen;
                if (screen != null && screen.ShowsResult) throw new InvalidOperationException("그 모습이 되기 전에 배틀이 끝났다");
                if (screen != null && screen.Open)
                {
                    if (screen.AskingToken) screen.PressToken(true);
                    else if (screen.AskingOrder) screen.PressOrder(0);
                    else if (screen.AskingFate) screen.PressFatePass();
                    else if (screen.AskingAsk) screen.PressAsk(false);
                    else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                    else if (screen.Bench.Count > 0)
                    {
                        int k = 0;
                        for (int j = 0; j < screen.Bench.Count; j++) if (screen.Bench[j].Target == lead) k = j;
                        if (!screen.PressStay()) screen.PressBench(k);
                    }
                    else if (screen.Moves.Count > 0) screen.PressMove(0);
                }
                else flow.AnswerSeat(1, a => a.Kind != AnswerKind.Forfeit && !(a.Kind == AnswerKind.UseMove && a.Arg >= 0));
                Pass(flow, BattleStepMs);
            }
            throw new InvalidOperationException("그 모습까지 가지 못했다");
        }

        // 쪽마다 지닌물건 칸 수 = 그 포켓몬의 지닌물건 수 · 칸은 그 쪽 기둥 판 안 · 그림(그림 없는 아이템은 이름 배지)
        static GameFlow HeldCheck(GameFlow flow, int mine, int foe)
        {
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            var view = flow.Client.View;
            foreach (var (side, name, want) in new[] { (flow.Battle.MySide, "battle-me", mine), (1 - flow.Battle.MySide, "battle-foe", foe) })
            {
                var pk = view.Pokemon[screen.ShownMon(side).Id];
                int held = (pk.HeldItem >= 0 ? 1 : 0) + (pk.HeldItem2 >= 0 ? 1 : 0);
                if (held != want) throw new InvalidOperationException(name + " 포켓몬의 지닌물건 " + held + " ≠ " + want);
                int cells = 0;
                while (root.Q(name + "-item-" + (cells + 1)) != null) cells++;
                if (cells != want) throw new InvalidOperationException(name + " 지닌물건 동그라미 " + cells + " ≠ " + want);
                var block = root.Q(name == "battle-me" ? "battle-info-me" : "battle-info-foe").worldBound;
                for (int n = 1; n <= want; n++)
                {
                    var cell = root.Q(name + "-item-" + n) ?? throw new InvalidOperationException(name + "-item-" + n + "이 없다");
                    var at = cell.worldBound;
                    if (at.width <= 0f || at.xMin < block.xMin - 0.5f || at.xMax > block.xMax + 0.5f || at.yMin < block.yMin - 0.5f || at.yMax > block.yMax + 0.5f)
                        throw new InvalidOperationException(name + "-item-" + n + "이 기둥 판 밖이다(" + at + " · " + block + ")");
                    if (cell.resolvedStyle.backgroundImage.texture == null) throw new InvalidOperationException(name + "-item-" + n + "에 토큰 그림이 없다");
                }
            }
            return flow;
        }

        // 내 카드 마우스 오버: 글 판이 뜨고 지닌물건 첫 줄 머리 = 그 아이템 이름 · 효과 글이 있다 · 화면 안(결정 206 ② — 칸 툴팁 대신 카드 글 판)
        static void HeldTip(UiShot s, GameFlow flow)
        {
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var stage = flow.Stage;
            var screen = (BattleScreen)stage.Current;
            var pk = flow.Client.View.Pokemon[screen.ShownMon(flow.Battle.MySide).Id];
            string key = flow.Ctx.Items.Items[pk.HeldItem].Key;
            if (!screen.HoverCard(true)) throw new InvalidOperationException("내 카드에 글 판이 걸려 있지 않다");
            s.Panel.Tick(3);
            var tip = screen.CardTipElement;
            if (tip == null || tip.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("카드 글 판이 뜨지 않았다");
            var row = tip.Q("battle-card-tip-item-1") ?? throw new InvalidOperationException("글 판에 지닌물건 줄이 없다");
            var labels = row.Query<Label>().ToList();
            if (labels.Count < 2 || labels[0].text.Replace("⁠", "") != stage.T(key)) throw new InvalidOperationException("지닌물건 줄 머리가 " + stage.T(key) + "가 아니다(" + (labels.Count > 0 ? labels[0].text : "-") + ")");
            var bad = WorkUi.Overflows(tip);
            if (bad.Count > 0) throw new InvalidOperationException("글 판 글자 넘침 " + string.Join(" / ", bad));
            var box = tip.worldBound;
            var all = stage.Root.worldBound;
            if (box.xMin < all.xMin || box.xMax > all.xMax + 0.5f || box.yMin < all.yMin || box.yMax > all.yMax + 0.5f) throw new InvalidOperationException("글 판이 화면 밖: " + box);
        }
    }
}
