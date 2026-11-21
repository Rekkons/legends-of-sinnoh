using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「트레이너 카드 화면」의 UI 캡처 장면(트레이너 카드를 골드로 쥐여 준 혼자 판 — <c>GameFlow.StartSeats</c> 1 + 손보기): 배틀 안의 카드 물음(머리 「카드 — 능력」 · 값 이름) ·
    /// 배틀 행동 [고급상처약] · 맵 능력 단추 · 칸 고르기 · 날씨 휠 · 보드에서 열린 카드 물음. 글자 넘침은 장면마다 단정한다(배틀 = <c>FitBattle</c>, 보드 = 아래 판 「행동」 칸).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] CardScreens =
        {
            Battle("tc-battle-pick", s => CardBattle(s, "ginkgo_merchant", 0)),    // ginkgo_merchant R: 기술을 고른 뒤 버릴 규토리 색(색 이름 단추) + 머리 「은행상인 — …」
            Battle("tc-battle-option", s => CardBattle(s, "chef", 1)),            // chef R: 「능력을 사용하겠습니까?」 [사용]·[넘기기]
            Battle("tc-battle-potion", s => CardBattle(s, "lady", 2)),            // lady R: 차례 칸 [고급상처약] → 회복할 포켓몬 줄
            Battle("tc-battle-psychic", s => CardBattle(s, "psychic", 3)),        // psychic R(결정 131): 굴린 뒤 반응 창 — 주사위 칩 하나 + 능력 줄 새 눈 단추
            Battle("tc-battle-amp", s => CardBattle(s, "guitarist", 4)),          // guitarist B·R 앰프(결정 134): 굴린 뒤 반응 창 — 앰프 줄 [데미지 +n] · 전기 기술이면 [데미지 +2n (2배)]
            Screen("tc-board-ability", Clean(s => CardBoard(s, "kindler", 0), "board-hud-actions")),   // kindler B: 행동 단추 줄 [턴 종료] 앞의 능력 단추(마우스 오버 = 카드 — 능력)
            Screen("tc-board-cells", Clean(s => CardBoard(s, "kindler", 1), "board-hud-actions")),     // 능력 단추 → 나무 칸 칠하기 + 「칸을 선택하십시오.」 + [취소]
            Screen("tc-board-weather", Clean(s => CardBoard(s, "parasol_lady", 2), "board-hud-actions")),   // 사당에 선 좌석: [날씨 휠] → 눈 · 비 · 모래바람
            Screen("tc-board-ask", Clean(s => CardBoard(s, "parasol_lady", 3), "board-hud-actions")),       // parasol_lady B 「When unlocked」 첫 날씨: 보드의 카드 물음(쾌청 · 눈 · 비 · 모래바람)
            // 묶음 2 — 정비 판의 카드 능력 줄 · 둘째 지닌물건 칸 · 전설 스냅(글자 넘침 = Fit)
            Screen("tc-party-afflict", Fit(s => CardParty(s, "black_belt", 0))),      // black_belt R [투혼] → 받는 포켓몬 카드만 빛난다
            Screen("tc-party-catch", Fit(s => PickedOneLine(s, CardParty(s, "fisherman", 1)))),   // fisherman R: 포켓몬을 고른 뒤 물음 줄 「여행 점수 2점을 소모해 …」 — 고른 카드도 체력·스피드 한 줄(결정 87 V1 ①)
            Screen("tc-party-convert", Fit(s => CardParty(s, "chef", 2))),           // chef B: 낼 색(빨강)을 고른 뒤 받을 색 단추
            Screen("tc-party-adopt", Fit(s => CardParty(s, "collector", 3))),        // collector R: 스탬프 보상 풀의 포켓몬 단추
            Screen("tc-party-pass", Fit(s => CardParty(s, "interviewers", 4))),      // interviewers R: 넘길 칸을 고른 뒤 받을 칸이 빛난다
            Screen("tc-party-pockets", Fit(s => CardParty(s, "backpacker", 5))),     // backpacker R: 지닌물건 두 칸 · 가방의 것을 집어 둘째 칸이 빛난다
            Screen("tc-party-snap", Fit(s => CardParty(s, "tourist", 6))),           // tourist R: 풀 뒤의 「전설 스냅」 · 스냅을 집어 배울 수 있는 칸이 빛난다
            Screen("tc-mon-pockets", Fit(s => CardParty(s, "backpacker", 7))),       // 카드 확대(S38): 지닌물건 둘
            Screen("tc-party-blessing", Fit(PartyBlessing)),                         // 울트라 버스트 판(좌석 둘 · 좌석 2 탈락): [아르세우스의 축복 n] → 건너뛰기 · 부활
            // 묶음 2b — 답
            Screen("tc-board-free", Clean(s => CardAnswer(s, "triathlete", 0), "board-hud-actions")),   // triathlete B: 무료 지형 이동 2칸을 고른 모습(안내 「무료 지형 이동 2칸 · 이동력 그대로」)
            Screen("tc-board-dive", Clean(s => CardAnswer(s, "team_aqua", 1), "board-hud-actions")),    // team_aqua B: 바다 칸에서 [능력] → 턴을 끝내며 옮길 바다 칸 칠하기
            Screen("tc-craft-bonus", Fit(s => CardAnswer(s, "picnicker", 2))),                          // picnicker R: 스튜 [제작] → 함께 받을 다른 스튜 단추
            // 트레이너 카드 감사 수정(11-20)
            Screen("tc-board-whistle", Fit(s => CardFix(s, "bird_keeper", 0))),                        // G1-1 bird_keeper B: 마을 밖 숲 칸 [공중날기] → 공중날기 칸만 있는 창(목적지 칩 · 규토리 색)
            Screen("tc-craft-team", Fit(s => CardFix(s, "ace_trainer", 1))),                           // G1-2 ace_trainer B: 제작 창 지닌물건 줄의 재료 = 색마다 하나
            Screen("tc-board-free-paid", Clean(s => CardFix(s, "triathlete", 2), "board-hud-actions")),   // G2-2 · G3-2: 무료로 닿는 칸 → [이동력으로 걷기]를 눌러 보통 걸음(안내 「이동력 n 사용」 · 단추 [무료로 걷기])
            Screen("tc-board-roads", Clean(s => CardFix(s, "worker", 3), "board-hud-actions")),        // G5-1 worker B: 놓인 길 칸이 보드에 칠해진다 · 칸 정보 줄 「길」
            Screen("tc-shrine-arceus", Fit(s => CardShrineArceus(s))),                                // G3-3 아르세우스의 추종자: 천상의 균열 — 「아르세우스의 추종자로 남기」
            // 묶음 2c — 답 나머지
            Screen("tc-enc-retry", Fit(s => CardRest(s, "ace_trainer", 0))),        // ace_trainer B: 조우 창 아래 줄 [다시 찾아보기](마우스 오버 = 카드 — 능력)
            Screen("tc-enc-again", Fit(s => CardRest(s, "ace_trainer", 1))),        // [다시 찾아보기] 뒤: 새 카드 공개 때 조우 창을 다시 그린다(조우당 한 번 — 단추 없음)
            Screen("tc-evolve-egg", Fit(s => CardRest(s, "pokemon_breeder", 2))),   // pokemon_breeder B: 진화 창 [교배 기술] → 진화형의 기술 + 넣을 칸
            Screen("tc-board-swap", Clean(s => CardRest(s, "youngster", 3), "board-hud-actions")),   // youngster R 「When unlocked」 카드 바꾸기: 카드 이름 단추(마우스 오버 = 두 능력)
            Screen("tc-card-release", Fit(s => CardRest(s, "youngster", 4))),       // 파티가 가득 찬 채 카드로 얻은 포켓몬: 놓아줄 포켓몬(보드 위 창)
            Screen("tc-card-teach", Fit(s => CardRest(s, "scientist", 5))),         // scientist B 현장 관찰: 찾은 포켓몬의 기술 가르치기(보드 위 창)
            Battle("tc-battle-tent", s => CardRest(s, "backpacker", 6)),            // backpacker B: 배틀 뒤 회복 고르기는 결과 창과 겹치지 않는다(결과 창은 [확인]만)
            Screen("tc-board-tent", Clean(s => CardRest(s, "backpacker", 7), "board-hud-actions")),  // 결과 창 [확인] 뒤 보드의 카드 물음: 회복할 포켓몬 단추
            Screen("tc-board-rocket", Clean(s => CardRest(s, "team_rocket", 8), "board-hud-actions")), // team_rocket R: 쓸 포켓몬이 바닥나면 배틀 화면을 걷고 날아갈 칸 칠하기 + [넘기기]
            Battle("tc-battle-rocket", s => CardRest(s, "team_rocket", 9)),         // 칸을 고른 뒤 배틀 화면이 다시 서고 결과 창
            Battle("tc-battle-type", s => TypePicks(s, CardRest(s, "swimmer", 10))),   // swimmer R: 물 기술 뒤 바꿀 타입 — 타입 그림 단추 둘을 빈 기술 칸 첫 줄에 붙여(결정 87 V1 ①)
            // 묶음 3 — 표시
            Screen("tc-show-hud", Clean(s => TagsClear(s, CardShow(s, "fisherman", 0)), "board-hud-party", "board-hud-status")),   // 아르세우스의 추종자 · 날씨 배지 · 파티 칸 꼬리표(배틀 불가 · 월척 — 도트를 덮지 않음, 결정 87 V1 ①) · 금색 ? 칸 정보 줄 + 금빛 뒷면 토큰
            Screen("tc-show-card", Fit(s => CardShow(s, "guitarist", 1))),          // 트레이너 카드 판: 앰프 · 장작 줄
            Screen("tc-show-jp", Clean(s => CardShow(s, "fisherman", 2), "board-hud-log")),   // fisherman R 월척 키우기(JP 2 — 이벤트 없이 줄어든다) → 기록 줄 「여행 점수가 2점 줄었다」
            // M6 화면 교차 검토 반영(review-screens #1 · #2 · #6 · #7 · 확인 A)
            Screen("tc-board-reach", Clean(s => CardReach(s, 0), "board-hud-actions")),          // psychic B: 두 칸 떨어진 마을 → 단추 = 전체 회복 · [은행상회] · [공중날기](묶음) — 같은 이름 없음
            Screen("tc-board-reach-fly", Clean(s => CardReach(s, 1), "board-hud-actions")),      // [공중날기] 안: 목적지 마을 단추(색이 여럿이면 묶음) + [뒤로]
            Screen("tc-board-reach-color", Clean(s => CardReach(s, 2), "board-hud-actions")),    // 목적지 안: 규토리 색 단추(색 아이콘)
            Screen("tc-board-reach-offer", Fit(s => CardReach(s, 3))),                            // 두 칸 떨어진 사당 → [공물] = 그 사당의 사당 판(색 · 「7점 더 바치기」 켬 — T33)
            Battle("tc-battle-potion-more", s => CardPotionMore(s)),                            // lady R: 다친 포켓몬 다섯 — 셋씩 쪽, 차례 칸 [다음 1/2] → 둘째 쪽
            // M6 마무리 — psychic B 염력 거래(결정 84 T12 ② 나)
            Screen("tc-board-trade", Clean(s => CardTrade(s, false), "board-hud-actions")),     // 두 칸 떨어진 좌석 2의 칸 → 값 단추에 「거래 좌석 2」
            Screen("tc-board-trade-open", Fit(s => CardTrade(s, true))),                        // 「거래 좌석 2」 → 거래 창이 그 좌석으로(상대 칩 툴팁 = 염력)
        };

        // 결정 87 V1 ①: 타입 값 단추는 그림만(이름 글 없음) · 둘이 붙어 있고 · 빈 기술 칸 첫 줄 안에 선다
        static void TypePicks(UiShot s, GameFlow flow)
        {
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            VisualElement a = root.Q("battle-pick-1"), b = root.Q("battle-pick-2"), moves = root.Q("battle-moves");
            if (a == null || b == null || moves == null) throw new InvalidOperationException("타입 단추 둘 · 기술 칸이 없다");
            if (a.Q(className: "sn-card-pick-text") != null || b.Q(className: "sn-card-pick-text") != null) throw new InvalidOperationException("타입 단추에 이름 글이 남았다(그림 안 글과 겹친다)");
            float gap = b.worldBound.xMin - a.worldBound.xMax;
            if (gap < 0f || gap > a.worldBound.width) throw new InvalidOperationException("타입 단추 둘이 붙어 있지 않다(사이 " + gap + "px)");
            var rows = moves.Query(className: "sn-battle-move-empty").ToList();
            if (rows.Count < BattleScreen.MoveRows) throw new InvalidOperationException("고르는 동안 빈 기술 칸이 " + rows.Count + "줄뿐이다");
            if (!rows[0].worldBound.Contains(a.worldBound.center) || !rows[0].worldBound.Contains(b.worldBound.center)) throw new InvalidOperationException("타입 단추가 첫 기술 칸 밖에 있다(" + a.worldBound + " · " + rows[0].worldBound + ")");
        }

        // 결정 87 V1 ①: 고른(빛나는) 포켓몬 카드도 체력 · 스피드가 한 줄
        static void PickedOneLine(UiShot s, GameFlow flow)
        {
            s.Panel.Tick(3);
            var card = flow.Stage.Root.Q("board-party-mon-0");
            if (card == null || !card.ClassListContains("sn-party-ok")) throw new InvalidOperationException("고른 포켓몬 카드(빛남)가 없다");
            var lines = card.Query(className: "sn-party-line").ToList();
            var stats = lines.Count >= 2 ? lines[1] : null;   // 타입 줄 · 체력·스피드 줄
            if (stats == null || stats.childCount < 2) throw new InvalidOperationException("고른 카드의 체력·스피드 줄이 없다");
            if (Math.Abs(stats[0].worldBound.y - stats[1].worldBound.y) > 0.5f) throw new InvalidOperationException("고른 카드의 체력 · 스피드가 두 줄로 갈렸다(" + stats[0].worldBound + " · " + stats[1].worldBound + ")");
        }

        // 결정 87 V1 ①: 파티 칸 꼬리표는 도트 칸을 덮지 않고 아래 판 안에 든다
        static void TagsClear(UiShot s, GameFlow flow)
        {
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var dock = root.Q("board-hud-dock");
            int n = 0;
            for (int i = 0; i < 6; i++)
            {
                var tag = root.Q("board-hud-mon-" + i + "-tag");
                if (tag == null) continue;
                n++;
                var art = root.Q("board-hud-mon-" + i).Q(className: "sn-mon");
                if (art != null && tag.worldBound.Overlaps(art.worldBound)) throw new InvalidOperationException("파티 칸 " + i + " 꼬리표가 도트를 덮는다(" + tag.worldBound + " · " + art.worldBound + ")");
                if (tag.worldBound.yMax > dock.worldBound.yMax - dock.resolvedStyle.paddingBottom + 0.5f) throw new InvalidOperationException("파티 칸 " + i + " 꼬리표가 아래 판 밖으로 나간다(" + tag.worldBound + " · " + dock.worldBound + ")");
            }
            if (n == 0) throw new InvalidOperationException("파티 칸 꼬리표가 없다");
        }

        // psychic B 염력 거래(M6 마무리 — 결정 84 T12 ② 나): 좌석 둘 · 거래 견본을 쥐여 주고 좌석 2를 두 칸 떨어진 칸에 세운 내 턴.
        // open = false: [능력] → 좌석 2의 칸 → 값 단추(「거래 좌석 2」) · true: 그 단추 → 거래 창(상대 = 좌석 2, 칩 툴팁 = 염력)
        static GameFlow CardTrade(UiShot s, bool open)
        {
            var ctx = s.Board.Ctx;
            int far = -1;
            var flow = MyTurn(s, 2, state =>
            {
                TradeKit(ctx, state, false);
                var me = state.Seats[0];
                me.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("psychic");
                me.TrainerCardTier = TrainerRules.Gold;
                foreach (int c in TrainerRules.ReachCells(ctx, me.Cell))
                {
                    if (c == me.Cell || MoveRules.Adjacent(ctx.Board, c, me.Cell) || !ctx.Board[c].Wild || ctx.Board[c].DistortionWorld) continue;
                    state.Seats[1].Cell = far = c;
                    break;
                }
            });
            var view = flow.Client.View;
            if (far < 0 || view.Seats[1].Cell != far) throw new InvalidOperationException("좌석 2가 두 칸 떨어진 칸에 서 있지 않다");
            if (TradeRules.ModeWith(view, ctx, 0, 1) != TradeRules.Telekinesis) throw new InvalidOperationException("좌석 2가 염력으로만 닿는 거래 상대가 아니다");
            var hud = flow.Hud;
            hud.Refresh();
            if (!hud.Shows(BoardHud.Trade)) throw new InvalidOperationException("[거래] 단추가 서지 않았다(염력 상대)");
            if (!hud.Press(BoardHud.Ability) || !hud.AbilityPicking || !new List<int>(hud.Reach).Contains(far)) throw new InvalidOperationException("[능력]이 좌석 2의 칸을 칠하지 않았다");
            if (!hud.AbilityAt(far)) throw new InvalidOperationException("좌석 2의 칸을 눌렀는데 아무 일이 없다");
            if (!(flow.Stage.Current is TradeScreen))
            {
                Unique(hud, "좌석 칸");
                string want = flow.Stage.T("screen.board.action.trade") + " " + flow.SeatName(1);
                int row = hud.AbilityRowOf(CardAsk.TradeMark(view, 1));
                for (int k = 0; k < hud.AbilityPages && row >= 0 && !hud.AbilityRowShown(row); k++) hud.NextAbilityPage();   // 이름이 길면 쪽(AbilityLineChars) — 「거래」가 있는 쪽으로
                if (row < 0 || !hud.AbilityRowShown(row) || !new List<string>(hud.AbilityLabels).Contains(want)) throw new InvalidOperationException("값 단추에 「" + want + "」이 없다(" + string.Join(" · ", hud.AbilityLabels) + ")");
                if (!open) return flow;
                if (!hud.PressAbilityRow(row)) throw new InvalidOperationException("「" + want + "」이 거절됐다");
            }
            else if (!open) throw new InvalidOperationException("좌석 2의 칸에 거래 말고 답이 없어 바로 거래 창이 열렸다(값 단추를 찍을 수 없다)");
            if (!(flow.Stage.Current is TradeScreen trade) || trade.Partner != 1 || hud.AbilityPicking) throw new InvalidOperationException("거래 창이 좌석 2로 열리지 않았다");
            s.Panel.Tick(3);
            flow.Stage.Tips.Show(flow.Stage.Root.Q("board-trade-partner-2"));
            return flow;
        }

        // psychic B 값 단추(검토 #1): stage 0 마을 칸의 단추 · 1 [공중날기] 안 · 2 목적지 안 · 3 사당 칸의 [공물] 안
        static GameFlow CardReach(UiShot s, int stage)
        {
            var ctx = s.Board.Ctx;
            int target = -1;
            var flow = CardSeat(s, "psychic", state =>
            {
                var st = state.Seats[0];
                for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = c < 2 ? 2 : 0;   // 두 색(공물 = 2색 × 보통·+7점 · 목적지마다 2색)
                st.Jp = Math.Max(st.Jp, 10);
                var fixtures = ctx.Fixtures.Fixtures;
                for (int f = 0; f < fixtures.Count && target < 0; f++)
                {
                    bool want = stage == 3 ? ShrineRules.ShrineAt(state, fixtures[f].Cell) >= 0 : fixtures[f].Kind == FixtureKind.Village && f != st.HomeVillage && !ctx.Board[fixtures[f].Cell].DistortionWorld;
                    if (!want) continue;
                    int cell = fixtures[f].Cell;
                    foreach (int c in TrainerRules.ReachCells(ctx, cell))
                    {
                        if (c == cell || MoveRules.Adjacent(ctx.Board, c, cell) || !ctx.Board[c].Wild || ctx.Board[c].DistortionWorld) continue;
                        st.Cell = c;
                        target = cell;
                        break;
                    }
                }
            });
            if (target < 0) throw new InvalidOperationException("두 칸 떨어진 마을·사당 칸을 못 찾았다");
            var hud = flow.Hud;
            var root = flow.Stage.Root;
            hud.Refresh();
            if (!hud.Press(BoardHud.Ability) || !hud.AbilityPicking || !new List<int>(hud.Reach).Contains(target)) throw new InvalidOperationException("[능력]이 그 칸을 칠하지 않았다");
            if (stage == 3) return ReachOffer(s, flow, target);
            if (!hud.AbilityAt(target) || !hud.AbilityPicking || hud.AbilityCell != target) throw new InvalidOperationException("그 칸의 단추가 서지 않았다");
            Unique(hud, "칸");
            if (stage == 0)
            {
                // #6: 창이 떠 입력이 잠깐 닫혀도(기록 창) 고르기가 풀리지 않는다
                flow.OpenLog();
                hud.Refresh();
                flow.CloseLog();
                hud.Refresh();
                if (!hud.AbilityPicking || hud.AbilityCell != target) throw new InvalidOperationException("기록 창을 열고 닫자 능력 고르기가 풀렸다");
                // 확인 A: 키보드 제출(Enter · Space)이 포커스된 단추를 누르지 않는다
                var first = root.Q<Button>("board-hud-ability-1");
                first.Focus();
                using (var e = NavigationSubmitEvent.GetPooled())
                {
                    e.target = first;
                    first.SendEvent(e);
                }
                s.Panel.Tick(3);
                if (!hud.AbilityPicking || hud.AbilityDepth != 0 || hud.AbilityCell != target) throw new InvalidOperationException("키보드 제출이 단추를 눌렀다");
                first.Blur();
                if (FindRow(hud, flow, AnswerKind.Fly) < 0) throw new InvalidOperationException("[공중날기] 묶음이 없다(" + string.Join(" · ", hud.AbilityLabels) + ")");
                return flow;
            }
            int fly = FindRow(hud, flow, AnswerKind.Fly);
            if (fly < 0 || !hud.PressAbilityRow(fly) || hud.AbilityDepth != 1) throw new InvalidOperationException("[공중날기] 안으로 들어가지 못했다");
            Unique(hud, "공중날기");
            if (hud.AbilityPages > 1 && (flow.Stage.Root.Q(BoardHud.AbilityNext) == null || hud.AbilityRowShown(hud.AbilityRows - 1) || hud.PressAbilityRow(hud.AbilityRows - 1))) throw new InvalidOperationException("목적지 쪽 넘김: 둘째 쪽 단추가 보이거나 눌렸다");
            if (stage == 1) return flow;
            int dest = -1;
            for (int i = 0; i < hud.AbilityRows && dest < 0; i++) if (hud.AbilityRowIsGroup(i)) dest = i;
            if (dest < 0 || !hud.PressAbilityRow(dest) || hud.AbilityDepth != 2) throw new InvalidOperationException("목적지 묶음이 없다");
            Unique(hud, "목적지");
            if (!hud.CancelAbility(false) || hud.AbilityDepth != 1 || !hud.CancelAbility(false) || hud.AbilityDepth != 0 || hud.AbilityCell != target) throw new InvalidOperationException("한 단계 물리기가 묶음을 하나씩 나오지 않는다");
            if (!hud.PressAbilityRow(fly) || !hud.PressAbilityRow(dest)) throw new InvalidOperationException("다시 들어가지 못했다");
            return flow;
        }

        // T33: 그 칸의 일이 공물뿐이면 칸을 누르자마자 그 사당의 사당 판(그 칸에 선 것과 같은 판 — 「7점 더 바치기」가 값 단추 쪽 넘김 뒤에 숨지 않는다)
        static GameFlow ReachOffer(UiShot s, GameFlow flow, int target)
        {
            var hud = flow.Hud;
            if (!hud.AbilityAt(target) || hud.AbilityPicking) throw new InvalidOperationException("사당 칸을 눌렀는데 사당 판이 열리지 않았다");
            if (!(flow.Stage.Current is ShrineScreen far) || far.Reach != target || far.Shown != ShrineScreen.Mode.Offer) throw new InvalidOperationException("떨어진 사당의 공물 판이 아니다");
            int colors = 0;
            foreach (int n in flow.Client.View.Seats[0].Apricorns) if (n > 0) colors++;
            if (far.Offers().Count != colors * 2) throw new InvalidOperationException("공물 답 = 색 × (보통 · +7점)이 아니다(" + far.Offers().Count + ")");
            s.Panel.Tick(3);
            if (!far.PickColor(far.Offers()[0].Arg) || !far.SetExtra(true) || !far.Extra) throw new InvalidOperationException("떨어진 사당에서 「7점 더 바치기」를 켜지 못했다");
            s.Panel.Tick(3);
            return flow;
        }

        // 지금 단추 줄에서 그 종류(psychic 안쪽 답)의 단추
        static int FindRow(BoardHud hud, GameFlow flow, AnswerKind kind)
        {
            var legal = flow.Client.Legal(flow.Client.Choice(ChoiceKind.MapAction));
            foreach (var a in legal)
                if (a.Kind == AnswerKind.UseAbility && a.Target == hud.AbilityCell && a.Arg == (int)kind) return hud.AbilityRowOf(a);
            return -1;
        }

        // 보이는 단추 이름이 서로 다르다(검토 #1 — 같은 이름 단추가 늘어서지 않는다)
        static void Unique(BoardHud hud, string where)
        {
            var seen = new HashSet<string>();
            foreach (var l in hud.AbilityLabels)
                if (!seen.Add(l)) throw new InvalidOperationException(where + ": 같은 이름 단추 「" + l + "」(" + string.Join(" · ", hud.AbilityLabels) + ")");
            if (hud.AbilityRows < 1) throw new InvalidOperationException(where + ": 단추가 없다");
            UnityEngine.Debug.Log("[UiShots] reach " + where + ": " + string.Join(" · ", hud.AbilityLabels));
        }

        // lady R 회복할 포켓몬 다섯(검토 #2 → 결정 87 V3): 2×3 한 쪽에 모두
        static GameFlow CardPotionMore(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, "lady", state =>
            {
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;   // 파티 칸을 모두 열고(스탬프로 여는 칸) 꼬링크 넷
                for (int k = 0; k < 4; k++) AddMon(state, ctx, "403:0");
                foreach (int p in st.Party) if (p >= 0) state.Pokemon[p].Damage = 1;
            });
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            var screen = Fight(flow, MyTurn);
            var root = flow.Stage.Root;
            // 결정 87 V3: 2×3 한 쪽 — 파티 여섯이 쪽 넘김 없이 모두 보이고 줄 판 안에 든다
            if (!screen.OpenAbility() || screen.AbilityPicks.Count < 5) throw new InvalidOperationException("회복할 포켓몬 줄이 다섯 미만이다(" + screen.AbilityPicks.Count + ")");
            s.Panel.Tick(3);
            var grid = root.Q(null, BattleScreen.AbilityGrid);
            if (grid == null) throw new InvalidOperationException("회복할 포켓몬 줄 판이 2×3 꼴이 아니다");
            for (int i = 1; i <= screen.AbilityPicks.Count; i++)
            {
                var cell = root.Q("battle-ability-" + i);
                if (cell == null) throw new InvalidOperationException("회복할 포켓몬 칸 " + i + "이 없다");
                var a = cell.worldBound;
                var g = grid.worldBound;
                if (a.width < 1 || a.xMin < g.xMin - 0.5f || a.xMax > g.xMax + 0.5f || a.yMin < g.yMin - 0.5f || a.yMax > g.yMax + 0.5f) throw new InvalidOperationException("회복할 포켓몬 칸 " + i + "이 줄 판 밖이다");
                if (WorkUi.Overflows(cell).Count > 0) throw new InvalidOperationException("회복할 포켓몬 칸 " + i + "의 글이 넘친다");
            }
            // 가로 먼저(1·2 / 3·4 / 5·6 — 파티 순서대로 읽힌다)
            UnityEngine.Rect c1 = root.Q("battle-ability-1").worldBound, c2 = root.Q("battle-ability-2").worldBound, c3 = root.Q("battle-ability-3").worldBound;
            if (Math.Abs(c2.y - c1.y) > 1f || c2.x <= c1.x || c3.y <= c1.y + 1f || Math.Abs(c3.x - c1.x) > 1f) throw new InvalidOperationException("회복할 포켓몬 칸이 가로 먼저로 채워지지 않았다: " + c1 + " · " + c2 + " · " + c3);
            return flow;
        }

        // 묶음 3 — stage 0 HUD(추종자 · 날씨 · 파티 칸 꼬리표 · 금색 ?) · 1 트레이너 카드 판 상태 줄 · 2 여행 점수 변화 기록 줄
        static GameFlow CardShow(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            int locked = -1, special = -1;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                if (stage == 0)
                {
                    st.Follower = SinnohGod.Arceus;
                    state.Weather = 2;
                    state.Pokemon[st.Party[0]].CatchSize = 3;
                    int def = -1;
                    for (int i = 0; i < ctx.Cards.Pokemon.Count && def < 0; i++) if (ctx.Cards.Pokemon[i].Key == "403:0") def = i;
                    locked = PokemonRules.Spawn(state, def, 0, 0);
                    st.Party[st.OpenSlots] = locked;   // 열리지 않은 칸의 포켓몬(pokemon_ranger B가 둔다)
                    int n = 0;
                    foreach (var t in state.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && (n++ % 3 == 0)) { t.Special = true; if (special < 0) { special = t.Cell; st.Cell = t.Cell; } }   // 셋에 하나만 금색 — 보통 뒷면과 견주어 보이게
                }
                if (stage == 1) { st.Amp = 3; st.Firewood = 2; }
                if (stage == 2) { state.Pokemon[st.Party[0]].CatchSize = 1; st.Jp = Math.Max(st.Jp, 5); }
            });
            var hud = flow.Hud;
            hud.Refresh();
            var root = flow.Stage.Root;
            switch (stage)
            {
                case 0:
                    if (hud.FollowTone(0) != SinnohGod.Arceus || !hud.FollowerShown(0)) throw new InvalidOperationException("좌석 칩에 아르세우스의 추종자가 없다");
                    if (root.Q(BoardHud.WeatherBadge) == null || root.Q(BoardHud.WeatherBadge).resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("날씨 배지가 없다");
                    if (root.Q("board-hud-mon-0-tag") == null || root.Q("board-hud-mon-" + flow.Client.View.Seats[0].OpenSlots + "-tag") == null) throw new InvalidOperationException("파티 칸 꼬리표(월척 · 배틀 불가)가 없다");
                    if (!hud.CellText(special).Contains(flow.Stage.T("screen.token.hidden.special"))) throw new InvalidOperationException("칸 정보 줄에 금색 ?가 없다(" + hud.CellText(special) + ")");
                    break;
                case 1:
                    if (!flow.OpenTrainer() || root.Q("board-trainer-state") == null) throw new InvalidOperationException("트레이너 카드 판에 상태 줄이 없다");
                    break;
                case 2:
                {
                    if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
                    if (!party.PressAbility(MaintKind.CatchSize) || !party.PressMon(0) || !party.Asking || !party.Confirm()) throw new InvalidOperationException("월척 키우기가 거절됐다");
                    flow.CloseParty();
                    for (int i = 0; i < BattleStepMax && !(flow.InputOpen && JpLine(flow)); i++) Pass(flow, BattleStepMs);
                    if (!JpLine(flow)) throw new InvalidOperationException("기록에 여행 점수 줄이 없다: " + string.Join(" / ", LastKeys(flow)));
                    hud.Refresh();
                    break;
                }
            }
            return flow;
        }

        // 묶음 2c — stage 0 조우 [다시 찾아보기] · 1 다시 찾은 뒤 · 2 교배 기술 · 3 카드 바꾸기 · 4 놓아주기 · 5 현장 관찰 가르치기 · 6 텐트(결과 창) · 7 텐트(보드) · 8 날아갈 칸 · 9 날아간 뒤 결과 창 · 10 타입 고르기
        static GameFlow CardRest(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            int water = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count && water < 0; m++) if (ctx.Cards.Moves[m].Type == PokemonType.Water && ctx.Cards.Moves[m].AttackStrength.HasValue) water = m;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                if (stage <= 1 || stage == 5)
                    for (int c = 0; c < ctx.Board.Count; c++)
                    {
                        int t = MapActions.EncounterTokenAt(state, c);
                        if (t >= 0 && state.Tokens[t].Kind == TokenKind.ApricornTree) { st.Cell = c; break; }
                    }
                if (stage == 2) st.Jp = Math.Max(st.Jp, 30);
                if (stage == 3)
                {
                    st.TrainerCardOffer.Clear();
                    foreach (string k in new[] { "hiker", "swimmer", "chef" }) st.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
                    st.UnlockDue.Add(TrainerRules.DueSwap);
                    st.UnlockDue.Add(0);
                }
                if (stage == 4)
                {
                    int def = -1;
                    for (int i = 0; i < ctx.Cards.Pokemon.Count && def < 0; i++) if (ctx.Cards.Pokemon[i].Key == "403:0") def = i;
                    for (int guard = 0; guard < 8; guard++)
                    {
                        int p = PokemonRules.Spawn(state, def, 0, 0);
                        if (PokemonRules.AddToParty(st, p) >= 0) continue;
                        st.UnlockDue.Add(TrainerRules.DueRelease);
                        st.UnlockDue.Add(p);
                        break;
                    }
                }
                if (stage == 6 || stage == 7) state.Pokemon[st.Party[0]].Damage = 1;
                if (stage == 8 || stage == 9) state.Pokemon[st.Party[0]].Damage = PokemonRules.MaxHp(ctx, state.Pokemon[st.Party[0]]) - 1;
                if (stage == 10) state.Pokemon[st.Party[0]].Moves[0] = water;
            });
            var hud = flow.Hud;
            hud.Refresh();
            var root = flow.Stage.Root;
            switch (stage)
            {
                case 0:
                case 1:
                {
                    if (!hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(나무 칸)");
                    var enc = (EncounterScreen)Wait(flow, () => flow.Stage.Current is EncounterScreen e && e.Open);
                    if (!enc.RetryOn || root.Q("encounter-retry") == null) throw new InvalidOperationException("조우 창에 [다시 찾아보기]가 없다");
                    if (stage == 0) break;
                    int old = enc.Built;
                    if (!enc.Retry()) throw new InvalidOperationException("[다시 찾아보기]가 거절됐다");
                    var again = (EncounterScreen)Wait(flow, () => flow.Stage.Current is EncounterScreen e && e.Built != old && e.Open);
                    if (again.RetryOn || root.Q("encounter-retry") != null) throw new InvalidOperationException("다시 찾은 뒤에도 [다시 찾아보기]가 남았다");
                    break;
                }
                case 2:
                {
                    if (!flow.OpenEvolve() || !(flow.Stage.Current is EvolveScreen evolve)) throw new InvalidOperationException("진화 창이 열리지 않았다");
                    int mon = evolve.Listed.Count > 0 ? evolve.Listed[0] : -1;
                    if (root.Q("board-evolve-egg-0") == null || !evolve.OpenEgg(mon) || evolve.Now != EvolveScreen.Mode.Egg || root.Q("board-evolve-egg-0-0") == null) throw new InvalidOperationException("[교배 기술]이 칸 고르기를 열지 않았다");
                    break;
                }
                case 3:
                    WalkOne(flow, hud);
                {
                    // 결정 87 V6 나: 뽑은 카드를 카드 그림으로 부채꼴 — 하나를 앞으로 꺼낸 순간(바로 선 카드 + [이 카드로 교체])
                    var swap = (CardSwapScreen)Wait(flow, () => flow.Stage.Current is CardSwapScreen w && flow.BoardOpen && w.Answers.Count > 0);
                    if (hud.CardAsking || root.Q("board-hud-card-1") != null) throw new InvalidOperationException("카드 바꾸기 창이 떴는데 「행동」 칸에도 카드 단추가 있다");
                    if (swap.Answers.Count != 3 || root.Q("board-card-swap-card-3") == null) throw new InvalidOperationException("카드 바꾸기 카드 셋이 없다(" + swap.Answers.Count + ")");
                    if (root.Q(CardSwapScreen.Go).enabledSelf) throw new InvalidOperationException("고르기 전인데 [이 카드로 교체]가 켜져 있다");
                    if (!swap.Pick(1) || swap.Picked != 1 || !root.Q("board-card-swap-card-2").ClassListContains(CardSwapScreen.OnClass) || !root.Q(CardSwapScreen.Go).enabledSelf) throw new InvalidOperationException("카드를 앞으로 꺼내지 못했다");
                    s.Panel.Tick(3);
                    var page = root.Q("board-card-swap-page").worldBound;
                    foreach (string n in new[] { "board-card-swap-card-1", "board-card-swap-card-2", "board-card-swap-card-3", "board-card-swap-head", CardSwapScreen.Go })
                    {
                        var b = root.Q(n).worldBound;
                        if (b.width < 1 || b.xMin < page.xMin - 0.5f || b.xMax > page.xMax + 0.5f || b.yMin < page.yMin - 0.5f || b.yMax > page.yMax + 0.5f) throw new InvalidOperationException("카드 바꾸기 창의 " + n + "이 화면 밖이다");
                    }
                    break;
                }
                case 4:
                    WalkOne(flow, hud);
                    Wait(flow, () => flow.Stage.Current is CardRewardScreen r && flow.BoardOpen && r.Answers.Count > 0);
                    if (root.Q("board-card-reward-1") == null || !root.Q("board-card-reward-1").ClassListContains("sn-party-press")) throw new InvalidOperationException("놓아줄 포켓몬 칸이 없거나 눌리지 않는다");
                    break;
                case 5:
                    if (!hud.Shows(BoardHud.Ability) || !hud.Press(BoardHud.Ability)) throw new InvalidOperationException("현장 관찰 단추가 없다");
                    if (hud.AbilityPicking && hud.AbilityShown.Count > 0) hud.PickAbility(0);
                    Wait(flow, () => flow.Stage.Current is CardRewardScreen r && flow.BoardOpen && r.Answers.Count > 1);
                    if (root.Q("board-card-reward-move") == null || root.Q("board-card-reward-2") == null || !root.Q("board-card-reward-2").enabledSelf) throw new InvalidOperationException("가르칠 기술 줄이 없거나 칸 단추가 꺼져 있다");
                    break;
                case 6:
                case 7:
                {
                    if (!hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
                    var screen = Fight(flow, b => b.ShowsResult && b.Ready);
                    if (root.Q("battle-result-ok") == null || screen.AskingPick || root.Q("battle-pick-1") != null) throw new InvalidOperationException("결과 창에 [확인]이 없거나 독에 회복 고르기가 겹쳤다");
                    var tent = flow.Client.FirstChoice();
                    if (tent == null || !BoardHud.BoardCardAsk(tent)) throw new InvalidOperationException("배틀 뒤 회복 고르기가 열리지 않았다(" + (tent != null ? tent.Kind + "/" + tent.Sub : "없음") + ")");
                    if (stage == 6) break;
                    if (!screen.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
                    Wait(flow, () => flow.InputOpen && hud.CardAsking);
                    if (root.Q("board-hud-card-1") == null) throw new InvalidOperationException("보드에 회복할 포켓몬 단추가 없다");
                    break;
                }
                case 8:
                case 9:
                {
                    if (!hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
                    for (int i = 0; i < BattleStepMax * 10 && !(flow.InputOpen && hud.CardCells); i++)
                    {
                        var b = flow.Stage.Current as BattleScreen;
                        if (b == null || !b.Open) { Pass(flow, BattleStepMs); continue; }
                        if (b.ShowsResult) throw new InvalidOperationException("날아갈 칸을 묻기 전에 배틀이 끝났다");
                        if (b.AskingToken) b.PressToken(true);
                        else if (b.AskingFate) b.PressFatePass();
                        else if (b.AskingAsk) b.PressAsk(false);
                        else if (b.AskingOrder) b.PressOrder(0);
                        else if (b.Bench.Count > 0) { if (!b.PressStay()) b.PressBench(0); }
                        else if (b.Moves.Count > 0) b.PressMove(0);
                        else Pass(flow, BattleStepMs);
                    }
                    if (!hud.CardCells || flow.Stage.Current != null || hud.Reach.Count < 300 || root.Q(BoardHud.CardHead) == null) throw new InvalidOperationException("배틀 화면을 걷고 날아갈 칸을 칠하지 않았다(칸 " + hud.Reach.Count + ")");
                    if (stage == 8) break;
                    int to = -1;
                    foreach (int c in hud.Reach) if (c != flow.Client.View.Seats[0].Cell) { to = c; break; }
                    if (!hud.ClickCell(to)) throw new InvalidOperationException("날아갈 칸을 누르지 못했다");
                    Wait(flow, () => flow.Stage.Current is BattleScreen b && b.ShowsResult && b.Ready);
                    if (flow.Client.View.Seats[0].Cell != to) throw new InvalidOperationException("고른 칸으로 날아가지 않았다");
                    break;
                }
                case 10:
                {
                    if (!hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
                    var screen = Fight(flow, MyTurn);
                    int row = -1;
                    for (int k = 0; k < screen.Moves.Count; k++) if (screen.Moves[k].Target == water) row = k;
                    if (row < 0 || !screen.PressMove(row)) throw new InvalidOperationException("물 기술 줄이 없다");
                    Fight(flow, b => b.Open && b.AskingPick);
                    var first = root.Q("battle-pick-1");
                    if (first == null || first.Q(className: "sn-card-pick-type") == null) throw new InvalidOperationException("타입 단추에 타입 그림이 없다");
                    break;
                }
            }
            return flow;
        }

        static bool JpLine(GameFlow flow) => flow.Log != null && flow.Log.Lines.Count > 0 && LastKeys(flow).Contains(Sinnoh.Fx.BoardLog.K + "jp_down");

        static List<string> LastKeys(GameFlow flow)
        {
            var keys = new List<string>();
            if (flow.Log == null) return keys;
            for (int i = Math.Max(0, flow.Log.Lines.Count - 5); i < flow.Log.Lines.Count; i++) keys.Add(flow.Log.Lines[i].Line != null ? flow.Log.Lines[i].Line.Key : "-");
            return keys;
        }

        // 한 칸 걷는다(길 위에 숨겨진 장소가 없는 칸) — 다음 맵 행동 앞 물음까지는 부르는 쪽이 기다린다
        static void WalkOne(GameFlow flow, BoardHud hud)
        {
            int to = -1;
            foreach (int c in flow.ActiveReach())
                if (hud.StopsBefore(c).Count == 0) { to = c; break; }
            if (to < 0 || !hud.Walk(to)) throw new InvalidOperationException("걸을 칸이 없다");
        }

        // 조건이 설 때까지 보낸다(BattleStepMs씩) — 서지 않으면 실패. 그때의 맨 위 화면을 돌려준다
        static IUiScreen Wait(GameFlow flow, Func<bool> until)
        {
            for (int i = 0; i < BattleStepMax && !until(); i++) Pass(flow, BattleStepMs);
            if (!until()) throw new InvalidOperationException("기다린 모습이 서지 않았다(맨 위 화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "없음") + ")");
            return flow.Stage.Current;
        }

        // 묶음 2b의 답 — stage 0 무료 지형 이동 · 1 턴 끝에 옮길 칸 · 2 제작의 공짜 몫
        static GameFlow CardAnswer(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                if (stage == 0) st.FreeLeft = TrainerRules.FreeTiles;
                if (stage == 1)
                    for (int c = 0; c < ctx.Board.Count; c++)
                        if (ctx.Board[c].Biome == Biome.Ocean && !ctx.Board[c].DistortionWorld) { st.Cell = c; break; }
                if (stage == 2) for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 3;
            });
            var hud = flow.Hud;
            hud.Refresh();
            var view = flow.Client.View;
            switch (stage)
            {
                case 0:
                {
                    var steps = TrainerRules.FreeReach(view, ctx, 0);
                    int to = -1;
                    for (int c = 0; c < steps.Length && to < 0; c++) if (steps[c] == TrainerRules.FreeTiles && hud.StopsBefore(c).Count == 0) to = c;
                    if (to < 0 || !hud.PickCell(to) || !hud.PickedFree) throw new InvalidOperationException("무료 지형 이동 칸을 고르지 못했다(" + to + ")");
                    break;
                }
                case 1:
                    if (!hud.Press(BoardHud.Dive) || !hud.AbilityPicking || hud.Reach.Count == 0 || flow.Stage.Root.Q(BoardHud.CardHead) == null) throw new InvalidOperationException("[능력]이 옮길 바다 칸을 칠하지 않았다");
                    break;
                case 2:
                {
                    if (!hud.Press(BoardHud.Craft) || !(flow.Stage.Current is CraftScreen craft)) throw new InvalidOperationException("[제작]이 제작 창을 열지 않았다");
                    int stew = -1;
                    for (int i = 0; i < ctx.Items.Items.Count && stew < 0; i++) if (ctx.Items.Items[i].Kind == ItemKind.Stew && craft.CanMake(i)) stew = i;
                    if (stew < 0 || !craft.PressMake(stew) || craft.BonusFor != stew || flow.Stage.Root.Q("board-craft-bonus-1") == null) throw new InvalidOperationException("스튜의 공짜 몫 단추가 없다");
                    break;
                }
            }
            return flow;
        }

        // 트레이너 카드 감사 수정(11-20) — stage 0 새 휘슬 · 1 팀 빌더 제작 재료 · 2 무료 ↔ 이동력 걷기 · 3 놓인 길
        static GameFlow CardFix(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            int forest = -1;
            for (int c = 0; c < ctx.Board.Count && forest < 0; c++)
            {
                if (ctx.Board[c].Biome != Biome.Forest || ctx.Board[c].DistortionWorld) continue;
                bool fixture = false;
                foreach (var f in ctx.Fixtures.Fixtures) if (f.Cell == c) fixture = true;
                if (!fixture) forest = c;
            }
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = stage == 1 ? 1 : 2;
                if (stage == 0) st.Cell = forest;
                if (stage == 2) st.FreeLeft = TrainerRules.FreeTiles;
                if (stage == 3)
                {
                    // 말 둘레의 바다 아닌 칸 몇에 길(worker B가 지난 턴에 놓았다)
                    int at = st.Cell;
                    for (int d = 0; d < Sinnoh.Rules.Board.Directions && state.Roads.Count < 3; d++)
                    {
                        int n = ctx.Board.Neighbor(at, d);
                        if (n != Sinnoh.Rules.Board.NoCell && ctx.Board[n].Biome != Biome.Ocean) state.Roads.Add(n);
                    }
                }
            });
            var hud = flow.Hud;
            hud.Refresh();
            var root = flow.Stage.Root;
            switch (stage)
            {
                case 0:
                {
                    if (!hud.Shows(BoardHud.Fly)) throw new InvalidOperationException("마을 밖 칸에 [공중날기]가 없다(새 휘슬)");
                    if (!hud.Press(BoardHud.Fly) || !(flow.Stage.Current is VillageScreen v) || !v.Whistle || !v.CanFly) throw new InvalidOperationException("[공중날기]가 공중날기 창을 열지 않았다");
                    int dest = v.Dests.Count > 0 ? v.Dests[0] : -1;
                    foreach (int d in v.Dests) if (v.CanGo(d)) { dest = d; break; }
                    if (!v.PickDest(dest) || root.Q("board-village-heal") != null) throw new InvalidOperationException("목적지를 고르지 못했다 · 회복 칸이 섰다");
                    // T18 ①: 새 휘슬 창은 처음부터 보드에서 고르기(오른쪽 기둥 · 가림막 없음 · [취소] 없이 [닫기] · 갈 수 있는 마을 칠)
                    if (!v.Picking || flow.FlyPick != v || root.Q("board-village-cancel") != null || root.Q("board-village-close") == null) throw new InvalidOperationException("새 휘슬 창이 보드에서 고르기 꼴이 아니다");
                    if (v.Lit.Count == 0 || flow.BoardMarks.Reach.Count != v.Lit.Count) throw new InvalidOperationException("새 휘슬 창의 갈 수 있는 마을이 보드에 칠해지지 않았다");
                    s.Panel.Tick(3);
                    if (root.Q("board-village-scrim").resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("새 휘슬 창에 가림막이 덮는다");
                    Apart(root, "board-village-panel", "board-hud-dock");
                    Apart(root, "board-village-panel", "board-hud-status");
                    break;
                }
                case 1:
                {
                    if (!hud.Press(BoardHud.Craft) || !(flow.Stage.Current is CraftScreen)) throw new InvalidOperationException("[제작]이 제작 창을 열지 않았다");
                    var view = flow.Client.View;
                    foreach (var d in ctx.Items.Items)
                    {
                        if (d.Kind != ItemKind.Held || d.Recipe == null) continue;
                        var row = root.Q("board-craft-row-" + CraftScreen.Short(d));
                        int shown = row != null ? row.Query(className: "sn-craft-apricorn").ToList().Count : -1;
                        int want = TrainerRules.Recipe(view, ctx, 0, d).Count;
                        if (shown != want || want >= d.Recipe.Count) throw new InvalidOperationException(d.Key + ": 재료 " + shown + "개 그림(엔진 " + want + " · 레시피 " + d.Recipe.Count + ")");
                    }
                    break;
                }
                case 2:
                {
                    var steps = TrainerRules.FreeReach(flow.Client.View, ctx, 0);
                    int to = -1;
                    for (int c = 0; c < steps.Length && to < 0; c++)
                        if (steps[c] == 1 && hud.StopsBefore(c).Count == 0 && hud.PickCell(c) && hud.FreeChoosable) to = c;
                    if (to < 0 || !hud.PickedFree) throw new InvalidOperationException("무료로도 · 이동력으로도 닿는 칸을 고르지 못했다");
                    if (root.Q(BoardHud.MoveFree) == null || !hud.ToggleFree() || hud.PickedFree) throw new InvalidOperationException("[이동력으로 걷기]가 보통 걸음으로 바꾸지 않았다");
                    break;
                }
                case 3:
                {
                    Pass(flow, BoardRoads.GrowMs + 100);
                    var roads = flow.BoardRoads;
                    var view = flow.Client.View;
                    if (roads == null || roads.Cells.Count != view.Roads.Count || roads.Cells.Count == 0 || roads.Growing) throw new InvalidOperationException("놓인 길이 보드에 칠해지지 않았다");
                    if (hud.CellText(view.Roads[0]).IndexOf(flow.Stage.T("screen.cell.road"), StringComparison.Ordinal) < 0) throw new InvalidOperationException("칸 정보 줄에 「길」이 없다");
                    break;
                }
            }
            return flow;
        }

        // G3-3: 아르세우스의 추종자(undivided_acolyte 실버) 좌석의 천상의 균열 — 사당 길(SoloTrail.Shrine)을 첫 맵 행동에서 손봐 그대로 간다
        static GameFlow CardShrineArceus(UiShot s)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Shrine);
            var ctx = flow.Ctx;
            flow.Rebase(st =>
            {
                var me = st.Seats[0];
                me.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("undivided_acolyte");
                me.TrainerCardTier = TrainerRules.Silver;
                me.Follower = SinnohGod.Arceus;
            });
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => flow.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Follower && sh.Open, ms => Pass(flow, ms), BattleStepMs, BattleStepMax * 10, true))
                throw new InvalidOperationException("천상의 균열 추종자 고르기까지 가지 못했다(화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + ")");
            s.Panel.Tick(3);
            var none = flow.Stage.Root.Q("board-shrine-nogod");
            if (none == null || none.Q<Label>() == null || none.Q<Label>().text != flow.Stage.T("screen.shrine.follower.arceus.keep")) throw new InvalidOperationException("「아르세우스의 추종자로 남기」가 없다");
            return flow;
        }

        // 내 파티에 그 포켓몬을 더한다(pokemon.json 키)
        static int AddMon(GameState state, Ctx ctx, string key)
        {
            int def = -1;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++) if (ctx.Cards.Pokemon[i].Key == key) def = i;
            if (def < 0) throw new InvalidOperationException("포켓몬 없음: " + key);
            int p = PokemonRules.Spawn(state, def, 0, 0);
            if (PokemonRules.AddToParty(state.Seats[0], p) < 0) throw new InvalidOperationException("파티 칸이 없다: " + key);
            return p;
        }

        static int ItemOf(Ctx ctx, string key)
        {
            for (int i = 0; i < ctx.Items.Items.Count; i++) if (ctx.Items.Items[i].Key == key) return i;
            throw new InvalidOperationException("아이템 없음: " + key);
        }

        // 그 포켓몬이 배울 수 있는 n번째 기술(시그니처 아님)
        static int Learnable(Ctx ctx, PokemonState p, int n)
        {
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
                if (m != ctx.Cards.Pokemon[p.Def].Signature && ChoiceRules.CanLearn(ctx, p, ctx.Cards.Moves[m].Type) && n-- == 0) return m;
            throw new InvalidOperationException("배울 기술이 없다");
        }

        // 정비 판의 카드 능력 — stage 0 Afflict · 1 CatchSize 물음 · 2 Convert 받을 색 · 3 Adopt · 4 PassMove 받을 칸 · 5 둘째 지닌물건 · 6 전설 스냅 · 7 카드 확대
        static GameFlow CardParty(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                int a = AddMon(state, ctx, "403:0"), b = AddMon(state, ctx, "403:0");   // 꼬링크 둘(같은 종 — 서로 배울 수 있다)
                var pa = state.Pokemon[a];
                var pb = state.Pokemon[b];
                pa.Moves[0] = Learnable(ctx, pa, 0);
                pb.Moves[0] = Learnable(ctx, pb, 1);
                if (stage == 1) { state.Pokemon[st.Party[0]].CatchSize = 1; st.Jp = Math.Max(st.Jp, 5); }
                if (stage == 2) for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 1;
                if (stage == 3)
                {
                    int def = state.Pokemon[a].Def;
                    st.OpenSlots = Math.Max(st.OpenSlots, 4);   // 받을 빈 칸(조각 하나)
                    st.StampCards.Add(0);
                    st.StampCards.Add(def);
                    st.StampMoves.Add(ctx.Cards.Pokemon[def].Signature);
                }
                if (stage == 5 || stage == 7)
                {
                    Sinnoh.Ai.SoloTrail.GiveKit(ctx, state);
                    var first = state.Pokemon[st.Party[0]];
                    first.HeldItem = ItemOf(ctx, "item.choice_band");
                    first.HeldItem2 = ItemOf(ctx, "item.lucky_egg");
                    st.Bag.Remove(first.HeldItem);
                    st.Bag.Remove(first.HeldItem2);
                    pa.HeldItem = ItemOf(ctx, "item.swiftness_sash");
                    st.Bag.Remove(pa.HeldItem);
                    st.Bag.Add(ItemOf(ctx, "item.choice_band"));   // 같은 것을 하나 더 — 첫 포켓몬의 둘째 칸에는 못 넣는다
                }
                if (stage == 6)
                    foreach (string legend in new[] { "480:0", "481:0" })   // 유크시(얼음 — 팽도리가 배운다) · 엠라이트(전기 — 꼬링크가 배운다)
                        for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
                            if (ctx.Cards.Pokemon[i].Key == legend) st.Snaps.Add(i);
            });
            if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            var root = flow.Stage.Root;
            switch (stage)
            {
                case 0:
                    if (!party.PressAbility(MaintKind.Afflict) || root.Q("board-party-abil-afflict") == null || !root.Q("board-party-mon-0").ClassListContains("sn-party-ok")) throw new InvalidOperationException("[투혼]이 포켓몬 고르기를 열지 않았다");
                    break;
                case 1:
                    if (!party.PressAbility(MaintKind.CatchSize) || !party.PressMon(0) || !party.Asking) throw new InvalidOperationException("월척 물음이 뜨지 않았다");
                    break;
                case 2:
                    if (!party.PressAbility(MaintKind.Convert) || !party.PressOption(0) || party.Options.Count == 0 || root.Q("board-party-opt-1") == null) throw new InvalidOperationException("받을 색 단추가 없다");
                    break;
                case 3:
                    if (!party.PressAbility(MaintKind.Adopt) || party.Options.Count != 1) throw new InvalidOperationException("풀의 포켓몬 단추가 없다(" + party.Options.Count + ")");
                    break;
                case 4:
                    if (!party.PressAbility(MaintKind.PassMove) || !party.PressSlot(1, 0) || !root.Q("board-party-slot-2-1").ClassListContains("sn-party-ok")) throw new InvalidOperationException("받을 칸이 빛나지 않았다");
                    {
                        // 검토 #7: 다시 그려도 후보를 처음부터 세지 않는다(같은 보기면 한 번)
                        int counted = party.CandidateCounts;
                        party.Refresh();
                        party.Refresh();
                        if (party.CandidateCounts != counted) throw new InvalidOperationException("다시 그릴 때 정비 후보를 다시 셌다(" + counted + " → " + party.CandidateCounts + ")");
                    }
                    break;
                case 5:
                    if (root.Q("board-party-item2-0") == null || !party.PickItem(0) || !root.Q("board-party-item2-1").ClassListContains("sn-party-ok")) throw new InvalidOperationException("둘째 지닌물건 칸이 빛나지 않았다");
                    break;
                case 6:
                    if (party.SnapFrom != party.Pool.Count - 2 || root.Q("board-party-snaps") == null || !party.PickMove(party.SnapFrom) || !party.CanPut(0, 0)) throw new InvalidOperationException("전설 스냅을 가르칠 칸이 없다(풀 " + party.Pool.Count + " · 스냅 " + party.SnapFrom + ")");
                    break;
                case 7:
                    if (!party.PressMon(0) || !(flow.Stage.Current is MonScreen)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
                    break;
            }
            return flow;
        }

        // 울트라 버스트 판(좌석 둘 — 좌석 2 탈락): 정비 판의 [아르세우스의 축복 n] → 값 단추(건너뛰기 · 부활)
        static void PartyBlessing(UiShot s)
        {
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Campaign = Campaign.UltraBurst }, 2, Seed, AiSeed, state => state.Seats[1].Eliminated = true);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            if (!party.PressAbility(MaintKind.UseBlessing) || party.Options.Count != 2) throw new InvalidOperationException("축복 단추 둘(건너뛰기 · 부활)이 없다(" + party.Options.Count + ")");
        }

        // 그 카드를 골드로 쥐여 준 혼자 판(고향 마을의 첫 턴)
        static GameFlow CardSeat(UiShot s, string card, Action<GameState> more = null)
        {
            var ctx = s.Board.Ctx;
            return Seats(s, 1, state =>
            {
                var st = state.Seats[0];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf(card);
                st.TrainerCardTier = TrainerRules.Gold;
                more?.Invoke(state);
            });
        }

        // 배틀 안의 카드 물음 — stage 0 고르기(기술을 고른 뒤) · 1 「쓸까」(기술을 고른 뒤) · 2 차례 칸의 [고급상처약]을 연 모습
        // · 3 psychic R(결정 131): 에스퍼 기술 뒤 굴린 뒤 반응 창 — 운명 줄의 주사위 칩 하나를 고른 모습(능력 줄 새 눈 단추)
        // · 4 guitarist 앰프(결정 134): 전기 기술 뒤 굴린 뒤 반응 창 — 운명 줄의 앰프 줄(1배 · 2배 단추)
        static GameFlow CardBattle(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 1;   // ginkgo R 버릴 색 · chef R 모든 색 하나씩
                state.Pokemon[st.Party[0]].Damage = 1;                              // lady R 고칠 것
                if (stage == 3) state.Pokemon[st.Party[0]].Moves[0] = ctx.Cards.MoveIds.IndexOf("confusion");
                if (stage == 4)
                {
                    state.Pokemon[st.Party[0]].Moves[0] = ctx.Cards.MoveIds.IndexOf("electro_wheel");
                    st.Amp = 3;
                }
            });
            if (stage == 4)
            {
                if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
                var me = Fight(flow, MyTurn);
                int slot = -1, wheel = ctx.Cards.MoveIds.IndexOf("electro_wheel");
                for (int i = 0; i < me.Moves.Count && slot < 0; i++) if (me.Moves[i].Kind == AnswerKind.UseMove && me.Moves[i].Target == wheel) slot = i;
                if (slot < 0 || !me.PressMove(slot)) throw new InvalidOperationException("전기 기술 줄이 없거나 거절됐다");
                me = Fight(flow, b => b.Open && b.AskingFate);
                var q = flow.Stage.Root;
                if (q.Q(BattleScreen.AmpRow) == null || q.Q(BattleScreen.AmpRow + "-1") == null || q.Q(BattleScreen.AmpRow + "-2") == null) throw new InvalidOperationException("운명 줄에 앰프 줄(1배 · 2배)이 없다");
                return flow;
            }
            if (stage == 3)
            {
                if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
                var me = Fight(flow, MyTurn);
                int slot = -1, confusion = ctx.Cards.MoveIds.IndexOf("confusion");
                for (int i = 0; i < me.Moves.Count && slot < 0; i++) if (me.Moves[i].Kind == AnswerKind.UseMove && me.Moves[i].Target == confusion) slot = i;
                if (slot < 0 || !me.PressMove(slot)) throw new InvalidOperationException("염동력 기술 줄이 없거나 거절됐다");
                me = Fight(flow, b => b.Open && b.AskingFate);
                var q = flow.Stage.Root;
                if (q.Q(BattleScreen.PsychicRow) == null) throw new InvalidOperationException("운명 줄에 psychic R 줄이 없다");
                if (!me.ToggleFateDie(0)) throw new InvalidOperationException("주사위 칩이 거절됐다");
                int on = 0;
                for (int to = 0; to < 3; to++) if (me.PsychicAnswer(to) != null) on++;
                if (on != 2) throw new InvalidOperationException("고른 주사위의 새 눈 단추가 둘이 아니다(" + on + ")");
                return flow;
            }
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            var screen = Fight(flow, MyTurn);
            var root = flow.Stage.Root;
            if (stage == 2)
            {
                if (!screen.AbilityOn || root.Q(BattleScreen.AbilityButton) == null) throw new InvalidOperationException("차례 칸에 [고급상처약]이 없다");
                if (!screen.OpenAbility() || screen.AbilityPicks.Count == 0 || root.Q("battle-ability-1") == null || root.Q(BattleScreen.CardHead) == null) throw new InvalidOperationException("회복할 포켓몬 줄이 열리지 않았다");
                return flow;
            }
            if (!screen.PressMove(0)) throw new InvalidOperationException("기술 줄이 거절됐다");
            screen = Fight(flow, b => b.Open && (stage == 0 ? b.AskingPick : b.AskingAsk));
            if (root.Q(BattleScreen.CardHead) == null) throw new InvalidOperationException("카드 물음에 머리 「카드 — 능력」이 없다");
            if (stage == 0)
            {
                var choice = flow.Client.Choice(ChoiceKind.Reaction);
                string want = flow.Stage.T("screen.apricorn." + ((ApricornColor)screen.Picks[0].Target).ToString().ToLowerInvariant());
                if (screen.PickLabel(choice, screen.Picks[0].Target) != want) throw new InvalidOperationException("고르기 값이 규토리 색 이름이 아니다(" + screen.PickLabel(choice, screen.Picks[0].Target) + ")");
            }
            return flow;
        }

        // 보드의 카드 능력 — stage 0 능력 단추 · 1 능력 단추를 누른 뒤(칸 고르기) · 2 사당에서 [날씨 휠]을 누른 뒤 · 3 한 칸 걸은 뒤 열린 「When unlocked」 첫 날씨 물음
        static GameFlow CardBoard(UiShot s, string card, int stage)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                if (stage == 2)
                {
                    state.Weather = 0;
                    st.Jp = Math.Max(st.Jp, TrainerRules.WeatherCost);
                    for (int c = 0; c < ctx.Board.Count; c++)
                        if (ShrineRules.ShrineAt(state, c) >= 0) { st.Cell = c; break; }
                }
                if (stage == 3)
                {
                    state.Weather = -1;
                    st.UnlockDue.Add(TrainerRules.DueWeather);
                    st.UnlockDue.Add(0);
                }
            });
            var hud = flow.Hud;
            hud.Refresh();
            if (stage == 0)
            {
                if (!hud.Shows(BoardHud.Ability)) throw new InvalidOperationException("행동 단추 줄에 능력 단추가 없다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.Ability));
                return flow;
            }
            if (stage == 1)
            {
                if (!hud.Press(BoardHud.Ability) || !hud.AbilityPicking || flow.Stage.Root.Q(BoardHud.CardHead) == null || flow.Stage.Root.Q(BoardHud.AbilityCancel) == null) throw new InvalidOperationException("능력 단추가 칸 고르기를 열지 않았다");
                if (hud.Reach.Count == 0 || flow.BoardMarks == null || flow.BoardMarks.Reach.Count != hud.Reach.Count) throw new InvalidOperationException("나무 칸이 칠해지지 않았다(" + hud.Reach.Count + ")");
                UnityEngine.Debug.Log("[UiShots] tc-board-cells 칠한 칸 " + hud.Reach.Count + ": " + string.Join(",", hud.Reach));
                return flow;
            }
            if (stage == 2)
            {
                if (!hud.Press(BoardHud.Weather) || !hud.AbilityPicking || hud.AbilityShown.Count != TrainerRules.WeatherTypes.Length - 1) throw new InvalidOperationException("[날씨 휠]이 날씨 단추 셋을 열지 않았다");
                return flow;
            }
            // 한 칸 걷고(길 위에 숨겨진 장소가 없는 칸) 다음 맵 행동 앞의 물음까지
            int to = -1;
            foreach (int c in flow.ActiveReach())
                if (hud.StopsBefore(c).Count == 0) { to = c; break; }
            if (to < 0 || !hud.Walk(to)) throw new InvalidOperationException("걸을 칸이 없다");
            for (int i = 0; i < BattleStepMax && !(flow.InputOpen && hud.CardAsking); i++) Pass(flow, BattleStepMs);
            if (!hud.CardAsking || hud.CardAnswers.Count != TrainerRules.WeatherTypes.Length || flow.Stage.Root.Q(BoardHud.CardHead) == null) throw new InvalidOperationException("보드에 첫 날씨 물음이 뜨지 않았다");
            return flow;
        }
    }
}
