using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「UB 화면 묶음 2」(사용자 결정 84 M3 다 · M4 가 · U1 · U2 · U10 · U11)의 UI 캡처 장면: 울트라 버스트 판(시드 20261117 — UB 목업과 같은 판)에서
    /// S58 균열 카드 가운데 창(붙이기 · 레인저 [보관] · UB 격파 뒤 버리기·옮기기) · S59 축복 판 · 라운드 끝 건너뛰기 물음 · 위 띠 동의 물음(묻는 쪽 · 기다리는 쪽) · S61 탈락 칩·회색 말 · 91행 부활.
    /// 창 장면은 글자 넘침을 단정한다(Fit · Clean). 남 좌석의 답은 <c>AnswerSeat</c>로 낸다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Ub2Screens =
        {
            // S58 붙이기: 좌석 셋이 손에 카드 — 좌석 2는 이미 골랐다(「완료」) · 첫 빈 칸이 골라져 있다
            Screen("ub-breach-attach", Fit(s => BreachAttach(s, false))),
            // U11 나: pokemon_ranger B(골드) — [보관](레인저 파랑)이 첫 답
            Screen("ub-breach-ranger", Fit(s => BreachAttach(s, true))),
            // U10 나: UB 격파 뒤 — 붙은 카드를 고른 모습 · 왼쪽에 손 카드
            Screen("ub-breach-release", Fit(BreachRelease)),
            // S59 M4 가: 축복 판(좌석 2 탈락) — [건너뛰기] · [부활]
            Screen("ub-bless-panel", Fit(BlessPanel)),
            // U2 나: 혼자 하는 판의 라운드 끝 「UB 턴을 건너뛰겠습니까?」
            Screen("ub-bless-ask", Fit(BlessAsk)),
            // U1 나: 좌석 2가 축복으로 건너뛰기를 냈다 → 내 위 띠 동의 물음 [막기]·[허용]
            Screen("ub-bless-consent", Clean(s => BlessConsent(s, false), BoardHud.BlessAsk)),
            // 내가 축복 판에서 건너뛰기를 냈다 → 위 띠 「다른 플레이어의 확인을 기다리는 중」
            Screen("ub-bless-consent-wait", Clean(s => BlessConsent(s, true), BoardHud.BlessAsk)),
            // S61: 탈락한 좌석 2 — 칩 회색 + 「탈락」 · 말 회색
            Screen("ub-seat-out", Clean(SeatOut, "board-hud-seats")),
            // 91행 부활: 금빛 고리가 내려오는 중 → 색을 되찾는 중
            Screen("row-91-ring", s => ReviveInto(s, 400)),
            Screen("row-91-back", s => ReviveInto(s, 1100)),
            // 91행 탈락(4인 — 네크로즈마에게 진 판): 결과 창 [확인] 뒤 보드에서 회색으로 바래는 중 → 칩에 금 · 회색 말(휘청은 하지 않는다 — §5 #161 ⑧)
            Screen("row-91-out-fade", s => FallInto(s, 600)),
            Screen("row-91-out", Clean(s => FallInto(s, 1300), "board-hud-seats")),
            Screen("row-91-out-next", Clean(FallNext, "board-hud-seats")),   // 탈락 연출 뒤 넘김 띠까지 — 위 띠 턴 · 칩 강조가 탈락하지 않은 다음 좌석으로(M6 마감 감사 「찾았지만 고치지 않은 것」 5)
            // 위 띠 축복 칩을 누르면 축복 판(M4 가): 4인 = 칩이 상태 판 안 · 6인 = 칩이 아래 오른쪽 UB 칩 판 — 판이 칩·상태 판에 겹치지 않는다
            Screen("ub-bless-chip", Fit(s => BlessChip(s, UbSeats))),
            Screen("ub-bless-chip-6", Fit(s => BlessChip(s, 6))),
            // 동의 물음의 남은 시간 막대(응답 시계 30초 판 · 9초 지남)
            Screen("ub-bless-consent-clock", Clean(s => BlessConsent(s, false, 9000), BoardHud.BlessAsk)),
            // 결정 87 R3(rulings A44 ②): 4인 판의 라운드 끝 다수결 — 좌석 2 「건너뛰기」 · 좌석 3 「그대로」 · 좌석 4 「고르는 중」 + 과반 규칙 한 줄
            Screen("ub-bless-vote", Fit(s => BlessVote(s, 0))),
            // 내가 답한 뒤: 창이 남고, 모두 고르기 전에는 내 표를 바꿀 수 있다(T29 — 고른 쪽은 꺼진 단추 · 「고르는 중 n명」 · 바꿀 수 있다는 줄)
            Screen("ub-bless-vote-wait", Fit(s => BlessVote(s, 1))),
            // 표를 바꿨다(그대로 → 건너뛰기): 내 줄이 「건너뛰기」로(T29)
            Screen("ub-bless-vote-change", Fit(s => BlessVote(s, 3))),
            // 과반(3 대 1)이 건너뛰기 → 창이 닫히고 축복 −1
            Screen("ub-bless-vote-done", s => BlessVote(s, 2)),
            // 결정 87 R4(rulings A44 ③): 레인저 파랑이 보관한 균열 카드 둘 — 정비 판 「능력」 줄 [보관한 균열 카드 2] → 카드 값 단추
            Screen("ub-party-breach", Fit(s => PartyBreach(s, 0))),
            // 카드를 고른 뒤: 붙일 수 있는 기술 칸이 빛난다
            Screen("ub-party-breach-slot", Fit(s => PartyBreach(s, 1))),
            // 칸을 눌렀다: 「…붙이겠습니까? 다시 보관할 수 없습니다.」 [붙이기]
            Screen("ub-party-breach-ask", Fit(s => PartyBreach(s, 2))),
            // [붙이기] 뒤: 그 칸에 균열 카드 · 단추는 [보관한 균열 카드 1]
            Screen("ub-party-breach-done", Fit(s => PartyBreach(s, 3))),
        };

        /// <summary>응답 시계(초)를 건 판의 시계 표 — 동의 물음 남은 시간 장면.</summary>
        static int[] RespondClock(int ms)
        {
            var clocks = new int[(int)ClockKind.Respond + 1];
            clocks[(int)ClockKind.Respond] = ms;
            return clocks;
        }

        // 울트라 버스트 판을 셋업 뒤(prepare로 손봐) 연 채로 — 첫 화면 연출만 보낸다
        static GameFlow UbOpen(UiShot s, int seats, Action<GameState> prepare, int[] clocks = null)
        {
            var flow = s.Flow();
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, seats, UbSeed, AiSeed, prepare, false, clocks);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            return flow;
        }

        // 내 턴의 맵 행동이 열릴 때까지 남 좌석의 턴을 넘긴다(라운드 끝 물음은 건너뛰지 않음 — EndActive)
        static void ToMyTurn(UiShot s, GameFlow flow)
        {
            for (int k = 0; k < 16 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, MyMap);
        }

        // 라운드 끝의 「UB 턴을 건너뛰겠습니까?」(결정 84 U2)가 열려 있으면 건너뛰지 않는다고 답한다(EndActive가 부른다 — 다른 UB 장면이 라운드 끝을 지나도록). 답했으면 true
        // 탈락하지 않은 모든 좌석에 한꺼번에 묻는다(결정 87 R3 — 다수결) — 묶음을 모두 「건너뛰지 않음」으로
        static bool DeclineUbSkip(GameFlow flow)
        {
            bool any = false;
            foreach (var c in flow.Client.View.Pending.FindAll(x => !x.Answered && x.Kind == ChoiceKind.Respond && x.Sub == ChoiceSub.UbSkip))
            {
                if (c.Seat == flow.Client.Seat)
                {
                    var no = flow.Client.Legal(c).Find(a => a.Kind == AnswerKind.Decline);
                    any |= no != null && flow.Answer(c, no).Accepted;
                }
                else any |= flow.AnswerSeat(c.Seat, a => a.Kind == AnswerKind.Decline);
            }
            return any;
        }

        static bool SkipVoteOpen(GameFlow flow) => flow.Client.View.Pending.Exists(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip);

        // 4인 판의 라운드 끝 다수결(결정 87 R3) — stage 0 = 좌석 2 건너뛰기 · 좌석 3 그대로 · 좌석 4 고르는 중(내 단추가 열려 있다) · 1 = 내가 [건너뛰지 않음]을 누른 뒤(기다리는 중) ·
        // 2 = 나 · 좌석 2 · 좌석 3 건너뛰기, 좌석 4 그대로 → 과반이라 축복 1개로 건너뛴다(창이 닫힌다)
        static void BlessVote(UiShot s, int stage)
        {
            var flow = UbOpen(s, UbSeats, null);
            ToMyTurn(s, flow);
            int before = flow.Client.View.Ub.Blessings;
            for (int k = 0; k < UbSeats * 2 && !SkipVoteOpen(flow); k++)
            {
                if (flow.Client.View.Pending.Exists(c => !c.Answered && c.Kind == ChoiceKind.MapAction)) EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.Top is BlessScreen b && !b.IsPanel && f.BoardOpen);
            var w = (BlessScreen)flow.Top;
            var root = s.Made.Stage.Root;
            bool third = stage == 2;
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Accept) || !flow.AnswerSeat(2, a => a.Kind == (third ? AnswerKind.Accept : AnswerKind.Decline)))
                throw new InvalidOperationException("좌석 2 · 3의 건너뛰기 답이 거절됐다");
            Pass(flow, 1);
            DealSettle(s, flow, f => f.Top is BlessScreen && f.BoardOpen);
            if (w.VoteShown(0) != 0 || w.VoteShown(1) != 1 || w.VoteShown(2) != (third ? 1 : 2) || w.VoteShown(3) != 0 || root.Q("board-bless-rule") == null || root.Q("board-bless-seat-4") == null)
                throw new InvalidOperationException("다수결 현황: " + w.VoteShown(0) + " " + w.VoteShown(1) + " " + w.VoteShown(2) + " " + w.VoteShown(3));
            if (root.Q("board-bless-yes") == null || root.Q("board-bless-wait") != null) throw new InvalidOperationException("내 답 단추가 없다");
            if (stage == 0) return;
            if (!w.PressAsk(third)) throw new InvalidOperationException("내 답이 거절됐다");
            Pass(flow, 400);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!(flow.Top is BlessScreen) || w.IsPanel || BlessScreen.ChoiceOf(flow.Client) != null || root.Q("board-bless-change") == null || root.Q("board-bless-left") == null || w.VoteShown(0) != (third ? 1 : 2) || w.VoteShown(3) != 0)
                throw new InvalidOperationException("답한 뒤 표를 바꿀 수 있는 창이 아니다(" + (flow.Top != null ? flow.Top.Name : "보드") + " · 내 줄 " + w.VoteShown(0) + ")");
            if (root.Q<Button>(third ? "board-bless-yes" : "board-bless-no").enabledSelf || !root.Q<Button>(third ? "board-bless-no" : "board-bless-yes").enabledSelf)
                throw new InvalidOperationException("고른 쪽 단추는 꺼지고 다른 쪽은 켜져 있어야 한다");
            if (stage == 1) return;
            if (stage == 3)
            {
                if (!w.Vote(true)) throw new InvalidOperationException("표 바꾸기가 거절됐다");
                Pass(flow, 400);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                if (w.VoteShown(0) != 1 || !(flow.Top is BlessScreen)) throw new InvalidOperationException("바꾼 표가 현황에 없다: " + w.VoteShown(0));
                return;
            }
            if (!flow.AnswerSeat(3, a => a.Kind == AnswerKind.Decline)) throw new InvalidOperationException("좌석 4의 답이 거절됐다");
            DealSettle(s, flow, f => !(f.Top is BlessScreen) && f.InputOpen);
            if (flow.Client.View.Ub.Blessings != before - 1) throw new InvalidOperationException("과반(3 대 1)인데 축복이 줄지 않았다: " + before + " → " + flow.Client.View.Ub.Blessings);
        }

        // 레인저 파랑(골드)이 균열 카드 둘을 보관한 혼자 판의 정비 판(결정 87 R4 — 정비 AttachBreach) — stage 0 = [보관한 균열 카드 2]를 눌러 카드 값 단추 둘 · 1 = 둘째 카드를 고름(빈 기술 칸이 빛난다) ·
        // 2 = 첫 포켓몬의 첫 칸을 누름(물음) · 3 = [붙이기](그 칸에 카드 · 보관 1장)
        static void PartyBreach(UiShot s, int stage)
        {
            var ctx = s.Board.Ctx;
            int first = BreachRules.CardOf(ctx, BreachKind.StatusOverload), second = BreachRules.CardOf(ctx, BreachKind.RapidStrikes);
            var flow = UbOpen(s, 1, st =>
            {
                st.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("pokemon_ranger");
                st.Seats[0].TrainerCardTier = TrainerRules.Gold;
                st.Ub.BreachStored.AddRange(new[] { 0, first, 0, second });
            });
            ToMyTurn(s, flow);
            if (BreachRules.StoredCount(flow.Client.View, 0) != 2) throw new InvalidOperationException("보관한 카드가 둘이 아니다(" + BreachRules.StoredCount(flow.Client.View, 0) + ")");
            if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            var root = s.Made.Stage.Root;
            if (root.Q("board-party-abil-attachbreach") == null) throw new InvalidOperationException("능력 줄에 [보관한 균열 카드]가 없다");
            if (!party.PressAbility(MaintKind.AttachBreach) || party.Options.Count != 2) throw new InvalidOperationException("보관한 카드 값 단추 둘이 없다(" + party.Options.Count + ")");
            if (stage == 0) return;
            if (!party.PressOption(1) || party.Options.Count != 0) throw new InvalidOperationException("둘째 카드를 고르지 못했다");
            s.Panel.Tick(3);
            int lit = root.Query(className: "sn-party-ok").ToList().Count;
            if (lit != PokemonState.MoveSlots) throw new InvalidOperationException("빛나는 기술 칸이 " + lit + "개다(한 마리 × 두 칸이어야 한다)");
            if (stage == 1) return;
            int mon = flow.Client.View.Seats[0].Party[0];
            if (!party.PressSlot(0, 0) || !party.Asking) throw new InvalidOperationException("붙일 칸을 눌렀는데 묻지 않는다");
            if (stage == 2) return;
            if (!party.Confirm()) throw new InvalidOperationException("[붙이기]가 거절됐다");
            Pass(flow, 400);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var view = flow.Client.View;
            if (view.Pokemon[mon].Breach[0] != second || BreachRules.StoredCount(view, 0) != 1 || party.Picking != null || root.Q("board-party-abil-attachbreach") == null)
                throw new InvalidOperationException("붙인 뒤: 칸 " + view.Pokemon[mon].Breach[0] + " · 보관 " + BreachRules.StoredCount(view, 0));
        }

        static void BreachAttach(UiShot s, bool ranger)
        {
            var ctx = s.Board.Ctx;
            var flow = UbOpen(s, UbSeats, st =>
            {
                for (int seat = 0; seat < 3; seat++)
                {
                    st.Ub.BreachHands.Add(seat);
                    st.Ub.BreachHands.Add(BreachRules.CardOf(ctx, (BreachKind)(seat + 1 + (ranger ? 3 : 0))));
                }
                if (!ranger) return;
                st.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("pokemon_ranger");
                st.Seats[0].TrainerCardTier = TrainerRules.Gold;
            });
            // 붙이기 묶음은 턴 시작에 열린다 — 지금 턴을 넘긴다
            for (int k = 0; k < UbSeats * 2 && !(flow.Top is BreachScreen); k++)
            {
                if (flow.Client.View.Pending.Exists(c => !c.Answered && c.Kind == ChoiceKind.MapAction)) EndActive(flow);
                Pass(flow, BoardReadyMs);
            }
            DealSettle(s, flow, f => f.Top is BreachScreen && f.BoardOpen);
            var w = (BreachScreen)flow.Top;
            if (ranger)
            {
                if (s.Made.Stage.Root.Q("board-breach-store") == null) throw new InvalidOperationException("레인저인데 [보관]이 없다");
                return;
            }
            if (w.Picked == null) throw new InvalidOperationException("첫 빈 칸이 골라져 있지 않다");
            if (!flow.AnswerSeat(1)) throw new InvalidOperationException("좌석 2가 붙이지 못했다");
            Pass(flow, 1);
            DealSettle(s, flow, f => f.Top is BreachScreen && f.BoardOpen);
        }

        static void BreachRelease(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = UbOpen(s, UbSeats, null);
            ToMyTurn(s, flow);
            int mon = -1;
            flow.Rebase(st =>
            {
                foreach (int p in st.Seats[0].Party) if (p >= 0 && mon < 0) mon = p;
                st.Pokemon[mon].Breach[0] = BreachRules.CardOf(ctx, BreachKind.StatusOverload);
                st.Ub.BreachHands.Add(0);
                st.Ub.BreachHands.Add(BreachRules.CardOf(ctx, BreachKind.RapidStrikes));
                if (!BreachRules.OpenRelease(st, ctx, new Emitter(st, null), 0, new List<int>())) throw new InvalidOperationException("버리기·옮기기가 열리지 않았다");
            });
            flow.Layout(s.Width, s.Height);
            DealSettle(s, flow, f => f.Top is BreachScreen && f.BoardOpen);
            var w = (BreachScreen)flow.Top;
            if (!w.PressSlot(mon, 0)) throw new InvalidOperationException("붙은 카드를 고르지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        static void BlessPanel(UiShot s)
        {
            var flow = UbOpen(s, UbSeats, st => st.Seats[1].Eliminated = true);
            ToMyTurn(s, flow);
            if (!flow.OpenBless()) throw new InvalidOperationException("축복 판이 열리지 않았다");
            DealSettle(s, flow, f => f.Top is BlessScreen);
        }

        static void BlessAsk(UiShot s)
        {
            var flow = UbOpen(s, 1, null);
            ToMyTurn(s, flow);
            EndActive(flow);
            DealSettle(s, flow, f => f.Top is BlessScreen b && !b.IsPanel && f.BoardOpen);
        }

        // waiting = 내가 축복 판에서 건너뛰기를 낸 쪽 · 아니면 좌석 2가 축복(건너뛰기)을 냄 → 나에게 동의 물음(clockMs > 0 = 응답 시계 30초 판에서 그만큼 지난 뒤)
        static void BlessConsent(UiShot s, bool waiting, int clockMs = 0)
        {
            const int Respond = 30000;
            var flow = UbOpen(s, 2, null, clockMs > 0 ? RespondClock(Respond) : null);
            ToMyTurn(s, flow);
            if (waiting)
            {
                if (!flow.OpenBless() || !(flow.Top is BlessScreen b) || !b.PressSkip()) throw new InvalidOperationException("축복 판의 [건너뛰기]가 거절됐다");
                DealSettle(s, flow, f => f.Hud != null && f.Hud.BlessWaiting);
                return;
            }
            // 좌석 2가 내 턴의 쉬는 때에 축복으로 건너뛰기를 낸다(라운드 끝 물음은 다수결이라 동의를 거치지 않는다 — 결정 87 R3)
            if (!flow.MaintainSeat(1, UbBlessing.SkipCommand())) throw new InvalidOperationException("좌석 2의 축복(건너뛰기)이 거절됐다");
            DealSettle(s, flow, f => f.Hud != null && f.Hud.BlessAsking && f.InputOpen);
            var root = s.Made.Stage.Root;
            Apart(root, BoardHud.BlessAsk, "board-hud-seats");
            Apart(root, BoardHud.BlessAsk, "board-hud-status");
            var clock = root.Q(BoardHud.BlessAsk + "-clock");
            if (clockMs <= 0)
            {
                if (clock != null && clock.resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("시계가 없는 판인데 동의 물음에 남은 시간 막대가 있다");
                return;
            }
            int before = flow.Hud.BlessClockShown;   // 물음이 열린 뒤 이미 조금 지났다(답 · 연출)
            Pass(flow, clockMs);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            int left = flow.Hud.BlessClockShown;
            if (!flow.Hud.BlessAsking || clock == null || clock.resolvedStyle.display == DisplayStyle.None || before <= 0 || before > Respond || left <= 0 || Math.Abs(before - clockMs - left) > 100)
                throw new InvalidOperationException("동의 물음의 남은 시간 막대: " + before + " → " + left + "ms(" + clockMs + "ms 지남)");
        }

        // 두 요소가 다 보이면 겹치지 않는다(위 띠 자리 단언 — 가장자리가 닿는 것은 괜찮다)
        static void Apart(VisualElement root, string a, string b)
        {
            var ea = root.Q(a);
            var eb = root.Q(b);
            if (ea == null || eb == null || ea.resolvedStyle.display == DisplayStyle.None || eb.resolvedStyle.display == DisplayStyle.None) return;
            if (ea.worldBound.Overlaps(eb.worldBound)) throw new InvalidOperationException(a + "이(가) " + b + "와(과) 겹친다(" + ea.worldBound + " · " + eb.worldBound + ")");
        }

        // 위 띠 축복 칩을 누른다(UI Toolkit 클릭 — 칩에 건 OnTap) → 축복 판(S59). 판이 칩 · 상태 판 · UB 칩 판(6인) · 좌석 칩 줄과 겹치지 않는다
        static void BlessChip(UiShot s, int seats)
        {
            var flow = UbOpen(s, seats, st => st.Seats[1].Eliminated = true);
            ToMyTurn(s, flow);
            s.Panel.Tick(3);
            var root = s.Made.Stage.Root;
            var chip = root.Q(BoardUb.Bless);
            if (chip == null || chip.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("축복 칩이 보이지 않는다");
            if ((chip.parent != null && chip.parent.name == "board-hud-ub-row") != (seats >= BoardUb.RowSeats)) throw new InvalidOperationException("축복 칩 자리: " + (chip.parent != null ? chip.parent.name : "없음"));
            using (var e = ClickEvent.GetPooled())
            {
                e.target = chip;
                chip.SendEvent(e);
            }
            if (!(flow.Top is BlessScreen b) || !b.IsPanel) throw new InvalidOperationException("축복 칩을 눌렀는데 축복 판이 열리지 않았다");
            DealSettle(s, flow, f => f.Top is BlessScreen);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            foreach (var other in new[] { "board-hud-status", "board-hud-ub-row", "board-hud-seats", BoardUb.Band }) Apart(root, "board-bless-panel", other);
        }

        // 4인 판 — 내 말을 네크로즈마 칸에 세우고 파티를 한 마리로 → [찾아보기] → 질 때까지 싸워 결과 창 [확인] → 보드의 91행 탈락 단계 ms
        // 탈락은 내 턴 안의 일이라(배틀 끝 → 91행 → 턴 끝 넘김) 91행 동안 위 띠는 아직 「내 턴」이다 — 넘김 띠(+ 컷신)가 끝나면 탈락하지 않은 다음 좌석으로 넘어간다(탈락 좌석은 턴이 없다 — TurnFlow, rulings A54 ④)
        static void FallNext(UiShot s)
        {
            var flow = FallInto(s, 1300);
            var root = s.Made.Stage.Root;
            bool Lit(int seat) => root.Q("board-hud-seat-" + (seat + 1)) is VisualElement chip && chip.ClassListContains("sn-hud-seat-turn");
            if (!Lit(0)) throw new InvalidOperationException("91행 동안은 끝나 가는 내 턴이어야 한다");
            for (int i = 0; i < 600 && Lit(0); i++) Pass(flow, 50);
            int shown = -1;
            for (int k = 0; k < flow.Client.View.Seats.Length; k++) if (Lit(k)) shown = k;
            if (shown <= 0 || flow.Client.View.Seats[shown].Eliminated) throw new InvalidOperationException("넘김 뒤 위 띠 턴이 탈락하지 않은 다음 좌석이 아니다(" + shown + ")");
            if (root.Q<Label>("board-hud-turn") is Label turn && turn.text == flow.Stage.T("screen.board.my_turn")) throw new InvalidOperationException("넘김 뒤에도 턴 배지가 「내 턴」이다");
        }

        static GameFlow FallInto(UiShot s, int ms)
        {
            var flow = UbGame(s, st =>
            {
                foreach (var t in st.Tokens)
                    if (t.Kind == TokenKind.Necrozma && t.Cell >= 0) st.Seats[0].Cell = t.Cell;
                var party = st.Seats[0].Party;
                for (int i = 1; i < party.Length; i++) party[i] = -1;
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("네크로즈마 칸에서 [찾아보기]가 거절됐다");
            var screen = Fight(flow, b => b.ShowsResult);
            Wait(flow, () => screen.ShowsResult && screen.Ready && !screen.AskingReward);
            if (flow.Battle != null && flow.Battle.Won) throw new InvalidOperationException("네크로즈마에게 이겼다(지는 판이어야 한다)");
            if (!flow.Client.View.Seats[0].Eliminated) throw new InvalidOperationException("네크로즈마에게 졌는데 탈락하지 않았다");
            if (!screen.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            FxStep step = null;
            for (int i = 0; i < 40 && (step = Queued(flow, st => st.Row == FallRows.Row && FallRows.Event(st) is Eliminated)) == null; i++) Pass(flow, 1);
            if (step == null) throw new InvalidOperationException("탈락(91행) 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
            if (flow.Stage.Current != null) throw new InvalidOperationException("탈락 단계인데 보드 위에 창이 있다(" + flow.Stage.Current.Name + ")");
            var pose = flow.FallShown(0);
            float grey = flow.BoardPieces.GreyOf(0);
            var chip = s.Made.Stage.Root.Q("board-hud-seat-1");
            bool crack = s.Made.Stage.Root.Q("board-hud-seat-1-out") is VisualElement tag && tag.resolvedStyle.display != DisplayStyle.None;
            bool ok = ms < step.Ms * FallBeats.CrackPermille / 1000 ? grey > 0.05f && grey < 0.99f && !crack
                : grey > 0.99f && crack && chip != null && chip.ClassListContains(BoardHud.OutClass);
            if (!ok || Math.Abs(grey - pose.Grey) > 0.01f) throw new InvalidOperationException("탈락 " + ms + "ms: 말 회색 " + grey + "(보일 값 " + pose.Grey + ") · 칩 금 " + crack);
            return flow;
        }

        static void SeatOut(UiShot s)
        {
            var flow = UbOpen(s, UbSeats, st => st.Seats[1].Eliminated = true);
            ToMyTurn(s, flow);
            var chip = s.Made.Stage.Root.Q("board-hud-seat-2");
            if (chip == null || !chip.ClassListContains(BoardHud.OutClass) || s.Made.Stage.Root.Q("board-hud-seat-2-out") == null) throw new InvalidOperationException("탈락한 좌석 칩이 회색 · 「탈락」이 아니다");
            if (flow.BoardPieces.GreyOf(1) < 0.99f) throw new InvalidOperationException("탈락한 좌석의 말이 회색이 아니다(" + flow.BoardPieces.GreyOf(1) + ")");
            s.Panel.Tick(3);
            var tag = s.Made.Stage.Root.Q("board-hud-seat-2-out").worldBound;   // 결정 87 V20: 꼬리표가 화면 가장자리에 잘리지 않는다
            if (tag.yMin < 0f || tag.xMin < 0f || tag.height < 1f) throw new InvalidOperationException("「리타이어」 꼬리표가 화면 밖에 걸린다: " + tag);
        }

        // 좌석 2(탈락)를 축복 판에서 부활 → 좌석 2가 허용 → 91행 단계의 ms
        static void ReviveInto(UiShot s, int ms)
        {
            var flow = UbOpen(s, 2, st => st.Seats[1].Eliminated = true);
            ToMyTurn(s, flow);
            if (!flow.OpenBless() || !(flow.Top is BlessScreen b) || !b.PressRevive(1)) throw new InvalidOperationException("축복 판의 [부활]이 거절됐다");
            // 탈락한 좌석에게는 동의를 묻지 않는다(결정 87 R2) — 다른 좌석이 없으니 바로 쓴다
            if (flow.Client.View.Pending.Exists(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing)) throw new InvalidOperationException("탈락한 좌석에게 동의를 물었다");
            FxStep step = null;
            for (int i = 0; i < 20 && (step = Queued(flow, st => st.Row == FallRows.Row)) == null; i++) Pass(flow, 1);
            if (step == null) throw new InvalidOperationException("부활(91행) 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
        }
    }
}
