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
    /// M6 「트레이너 카드 해금 연출」의 UI 캡처 장면(04 §8-8 35·48·49·50·70행 — 결정 84 M5 가): 브론즈 카드를 쥔 혼자 판이 적당한 나무에서 [찾아보기] → 야생과 싸워 이긴다(전설 다섯을 쥐여 줬다) →
    /// 결과 창의 「○○ 카드 실버 해금!」 줄(70행) → [확인] 뒤 보드에서 카드 뒤집기(48행 — 자물쇠 · 뒤집힘 · 등급 테두리, 실버 → 골드) · 남 좌석의 해금(좌석 칩 꼬리표 — 좌석 둘 판, 좌석 2의 진화) ·
    /// 카드 바꾸기(49행) · 능력 사용(50행 — 작은 카드) · 날씨 휠(35행 — 위 띠 배지 돌림판). 글자 넘침은 장면마다 단정한다(<c>WorkUi.Overflows</c> — 덮개 <c>card-fx</c> · 결과 창 · 좌석 칩 · 위 띠).
    /// 그림은 <c>Logs/Review/m6/unlock/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] UnlockScreens =
        {
            Battle("row-70-unlock", Clean(s => UnlockFight(s, TrainerRules.Bronze, EncounterTier.Moderate), "battle-result-panel")),   // 결과 창: 「초능력자 카드 실버 해금!」 한 줄(스탬프 줄 꼴)
            Screen("row-48-lock", Clean(s => UnlockAt(s, TrainerRules.Bronze, EncounterTier.Moderate, 1000), "card-fx-holder")),         // [확인] 뒤 보드: 브론즈 카드 · 등급 아이콘이 박히고 파랑 자물쇠가 흔들림
            Screen("row-48-flip", s => UnlockAt(s, TrainerRules.Bronze, EncounterTier.Moderate, UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs / 2 - 40)),   // 자물쇠가 깨져 떨어지며 카드가 옆으로 선다
            Screen("row-48-border", Clean(s => UnlockAt(s, TrainerRules.Bronze, EncounterTier.Moderate, UnlockBeats.LitAt(1) + 300), "card-fx-holder")),   // 실버 틀 + 금속광 테두리 · 파랑 칸 금 테두리 · 「실버 ★★☆」「「염력」 해금」
            Screen("row-48-gold", Clean(s => UnlockAt(s, TrainerRules.Silver, EncounterTier.Strong, UnlockBeats.LitAt(1) + 300), "card-fx-holder")),       // 실버 → 골드(강한 포켓몬): 골드 틀 · 빨강 칸
            Screen("row-48-other", OtherUnlock),                                                                                           // 좌석 둘: 좌석 2의 진화로 오른 해금 — 좌석 2 칩 아래 「카드 실버!」 + 큰 덮개(결정 120 ①) · 제목 앞 그 좌석 얼굴 + 「○○의 트레이너 카드」(#707)
            Screen("row-48-other-lit", s => OtherUnlock(s, UnlockBeats.LitAt(1) + 300)),                                                   // 남 좌석 해금의 착지: 「○○의 트레이너 카드 실버!」(남도 같은 길이 — #708)
            Screen("owner-long-unlock", s => OtherUnlock(s, UnlockBeats.LitAt(1) + 300, LongNick)),                                        // 좌석 2 닉네임 12자(방 판)의 해금 착지 제목
            Screen("owner-swap", OtherSwap),                                                                                               // 좌석 2(반바지 꼬마 빨강)의 카드 바꾸기 착지: 그 좌석 얼굴 + 「○○의 새 트레이너 카드!」(#707 · #708)
            Screen("row-49-swap", Clean(s => CardFxAt(s, "youngster", 49, SwapBeats.LitAt + 200), "card-fx-holder")),                       // 반바지 꼬마 빨강: 새 카드(등산가) 골드 + 「새 트레이너 카드!」
            Screen("row-50-ability", Clean(s => CardFxAt(s, "gardener", 50, AbilityBeats.BounceAt + AbilityBeats.BounceMs / 2), "card-fx-mini")),   // 정원사 파랑: 왼쪽 위 작은 카드 · 파랑 칸 빛 · 「녹색 손가락」
            Screen("row-50-ability-log3", Clean(s => CardFxAt(s, "gardener", 50, AbilityBeats.BounceAt + AbilityBeats.BounceMs / 2, logLines: 3), "card-fx-mini")),   // 기록 판이 세 줄일 때도 작은 카드는 판 아래(M6 마감 감사 「찾았지만 고치지 않은 것」 2)
            Screen("row-35-spin", Clean(s => CardFxAt(s, "parasol_lady", 35, 500), "board-hud-status")),                                     // 사당에서 날씨 바꾸기: 위 띠 날씨 배지가 돌림판처럼 돈다
            Screen("row-35-stop", s => CardFxAt(s, "parasol_lady", 35, WeatherBeats.SpinMs + WeatherBeats.PopMs / 2)),                       // 고른 날씨(모래바람)에 멈추며 톡
        };

        // 브론즈(또는 실버) 초능력자 카드 · 전설 다섯을 쥔 혼자 판을 그 등급의 나무에 세우고 [찾아보기] → 첫 카드와 싸워 이긴 결과 창([확인] 전)까지
        static GameFlow UnlockFight(UiShot s, int tier, EncounterTier tree)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                var st = state.Seats[0];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("psychic");
                st.TrainerCardTier = tier;
                GiveTeam(ctx, state);
                foreach (var t in state.Tokens)
                    if (t.Kind == TokenKind.ApricornTree && !t.Destroyed && t.Cell >= 0 && ctx.Fixtures.Fixtures[t.Def].Tier == tree) { st.Cell = t.Cell; break; }
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(" + tree + " 나무)");
            var enc = (EncounterScreen)Wait(flow, () => flow.Stage.Current is EncounterScreen e && e.Open);
            if (!enc.Pick(0)) throw new InvalidOperationException("조우 카드 고르기가 거절됐다");
            var result = Fight(flow, b => b.ShowsResult && b.Ready);
            for (int i = 0; i < 20 && result.AskingReward; i++)
            {
                result.PressReward(result.Rewards.Count - 1);   // 기술 가르치기는 마지막 답(가르치지 않기)
                Pass(flow, BattleStepMs);
            }
            for (int i = 0; i < 40 && !(flow.BattleOver && result.Ready && !result.AskingReward); i++) Pass(flow, BattleStepMs);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.Battle == null || flow.Battle.Unlocks.Count != 1) throw new InvalidOperationException("이 배틀로 트레이너 카드가 오르지 않았다(" + (flow.Battle != null ? flow.Battle.Unlocks.Count : -1) + ")");
            var line = flow.Stage.Root.Q<Label>("battle-result-unlock");
            string want = flow.Stage.T("screen.trainer.tier." + (tier + 1));
            if (line == null || !line.text.Replace("⁠", "").Contains(want)) throw new InvalidOperationException("결과 창에 해금 줄이 없다(" + (line != null ? line.text : "없음") + ")");
            if (Queued(flow, st => st.Row == UnlockBeats.Row) == null) throw new InvalidOperationException("해금 단계가 대기열에 없다");
            return flow;
        }

        // 결과 창 [확인] 뒤 보드의 해금 단계(48행) 안 ms
        static void UnlockAt(UiShot s, int tier, EncounterTier tree, int ms)
        {
            var flow = UnlockFight(s, tier, tree);
            var screen = (BattleScreen)flow.Stage.Current;
            var step = Queued(flow, st => st.Row == UnlockBeats.Row);
            if (flow.Fx.Sequencer.Current != null && flow.Fx.Sequencer.Current.Steps.Contains(step)) throw new InvalidOperationException("해금이 결과 창 [확인] 전에 돌았다");
            if (!screen.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            Laid(s, flow, step, ms);
            if (flow.Stage.Current != null) throw new InvalidOperationException("해금이 보드가 아닌 곳에서 돈다: " + flow.Stage.Current.Name);
            var fx = flow.Cards;
            if (fx == null || !fx.UnlockShown) throw new InvalidOperationException("해금 덮개가 보이지 않는다");
            int lit = UnlockBeats.LitAt(1);
            if (ms >= lit && (!fx.LitShown || fx.ShownTier != tier + 1)) throw new InvalidOperationException("착지에 다음 등급이 아니다(" + fx.ShownTier + ")");
            if (ms < UnlockBeats.BreakAt(0) && (fx.ShownTier != tier || fx.Broken != 0)) throw new InvalidOperationException("깨지기 전에 지금 등급이 아니다(" + fx.ShownTier + ")");
            if (flow.Stage.Root.Q("card-fx-cell-1") == null || flow.Stage.Root.Q("card-fx-lock-2") == null) throw new InvalidOperationException("카드 칸·자물쇠가 없다");
            CardMine(flow);
        }

        // 좌석 둘: 좌석 2(브론즈 카드 · 모부기)의 턴에 모부기를 수풀모(적당한 포켓몬)로 진화 → 해금(48행)은 내 화면에서 좌석 2 칩의 꼬리표 + 내 것과 같은 큰 덮개(창이 없을 때 — 결정 120 ①)
        static void OtherUnlock(UiShot s) => OtherUnlock(s, 400);

        // ms = 해금 단계 안 시각, nick = 좌석 2의 방 닉네임(가짜 방 — null이면 기본 「플레이어 2」)
        static void OtherUnlock(UiShot s, int ms, string nick = null)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 2, state =>
            {
                var st = state.Seats[1];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("hiker");
                st.TrainerCardTier = TrainerRules.Bronze;
                st.Party[0] = PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf("387:0"), 1, 0);
                st.Jp = 20;
            });
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            if (nick != null) Nick2(flow, nick);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Evolve)) throw new InvalidOperationException("좌석 2의 진화가 거절됐다");
            var step = Queued(flow, st => st.Row == UnlockBeats.Row);
            if (step == null) throw new InvalidOperationException("해금 단계가 대기열에 없다");
            if (step.Ms != PlayTiming.UnlockMsOf(CardRows.First<CardUnlocked>(step))) throw new InvalidOperationException("남 좌석의 해금 길이가 내 것과 다르다: " + step.Ms + "(#708)");
            ShrineInto(s, UnlockBeats.Row, ms);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            CardOwner(flow, "card-fx-owner", 1);
            if (ms >= UnlockBeats.LitAt(1)) CardLit(flow, "screen.card.fx.tier.seat", flow.Stage.T("screen.trainer.tier.2"));
            var tag = flow.Stage.Root.Q<Label>(BoardHud.DidName(1));
            if (tag == null || flow.Hud.DidSeat != 1 || flow.Hud.DidKey != "unlock_2") throw new InvalidOperationException("좌석 2 칩의 해금 꼬리표가 없다(" + flow.Hud.DidKey + ")");
            // 결정 120 ①: 내 창 · 메뉴가 없으면 남 좌석의 해금도 내 것처럼 큰 덮개(GameFlow.ShowsOthers) — 칩 꼬리표는 그대로
            if (!flow.ShowsOthers || flow.Cards == null || !flow.Cards.UnlockShown) throw new InvalidOperationException("남 좌석의 해금에 큰 덮개가 뜨지 않았다(보기 판 " + flow.ShowsOthers + ")");
            var need = tag.MeasureTextSize(tag.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            if (need.x > tag.contentRect.width + 1f) throw new InvalidOperationException("꼬리표 글이 잘린다: 칸 " + tag.contentRect.width + " < 글 " + need.x);
            var chip = flow.Stage.Root.Q("board-hud-seat-2").worldBound;
            if (tag.worldBound.xMax > chip.xMax + 0.5f || tag.worldBound.xMin < chip.xMin - 0.5f) throw new InvalidOperationException("꼬리표가 칩 폭 밖이다: " + tag.worldBound + " · 칩 " + chip);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-hud-seats"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(좌석 칩) " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        // 좌석 둘: 좌석 2(반바지 꼬마 골드 — 후보 등산가 · 수영 선수 · 요리사)의 「When unlocked」 카드 바꾸기를 좌석 2의 답으로 → 49행 착지(남도 2000 — #708) — 내 것과 같은 큰 덮개 + 그 좌석 얼굴 · 「○○의 새 트레이너 카드!」
        static void OtherSwap(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 2, state =>
            {
                var two = state.Seats[1];
                two.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("youngster");
                two.TrainerCardTier = TrainerRules.Gold;
                two.TrainerCardOffer.Clear();
                foreach (string k in new[] { "hiker", "swimmer", "chef" }) two.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
                two.UnlockDue.Add(TrainerRules.DueSwap);
                two.UnlockDue.Add(0);
            });
            // 물음은 맵 행동 앞에 열린다(좌석 번호 순 — 내 턴일 수도 있다): 열릴 때까지 턴을 넘기고 좌석 2는 한 칸 걷는다
            bool picked = false;
            for (int i = 0; i < 12 && !picked; i++)
            {
                picked = flow.AnswerSeat(1, a => a.Kind == AnswerKind.Pick);
                if (picked) break;
                if (flow.Client.View.ActiveSeat == 1) flow.AnswerSeat(1, a => a.Kind == AnswerKind.Move && ctx.Board[a.Target].Biome != Biome.Ocean);
                else EndActive(flow);
                Pass(flow, BoardReadyMs * 4);
            }
            if (!picked) throw new InvalidOperationException("좌석 2의 카드 바꾸기 물음이 열리지 않았다");
            var step = Queued(flow, st => st.Row == SwapBeats.Row);
            if (step == null) throw new InvalidOperationException("카드 바꾸기 단계가 대기열에 없다");
            if (step.Ms != PlayTiming.SwapMs) throw new InvalidOperationException("남 좌석의 카드 바꾸기 길이가 내 것과 다르다: " + step.Ms + "(#708)");
            ShrineInto(s, SwapBeats.Row, SwapBeats.LitAt + 200);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!flow.Cards.SwapShown) throw new InvalidOperationException("남 좌석의 카드 바꾸기에 큰 덮개가 뜨지 않았다(보기 판 " + flow.ShowsOthers + ")");
            CardOwner(flow, "card-fx-owner", 1);
            CardLit(flow, "screen.card.fx.swap.seat", null);
        }

        // 남 좌석 덮개의 착지: 착지 불이 켜졌고 제목이 「○○의 …!」(그 좌석 이름 · tier가 있으면 그 등급 이름)
        static void CardLit(GameFlow flow, string key, string tier)
        {
            if (!flow.Cards.LitShown) throw new InvalidOperationException("남 좌석 덮개가 착지 전에 걷혔다(#708)");
            string want = KoJosa.Fix(flow.Stage.T(key).Replace("{name}", flow.SeatName(1)));
            if (tier != null) want = want.Replace("{tier}", tier);
            var title = flow.Stage.Root.Q<Label>("card-fx-title");
            if (title == null || KoJosa.Fix(title.text.Replace("⁠", "")) != want) throw new InvalidOperationException("착지 제목이 「" + want + "」이 아니다: " + (title != null ? title.text : "없음"));
        }

        /// <summary>긴 이름 확인용 방 닉네임(12자 = RoomFlow.NicknameMax — 한글 가장 넓은 꼴).</summary>
        const string LongNick = "빛나는포켓몬트레이너다요";

        // 가짜 방을 띄워 좌석 2의 이름을 nick으로(그 뒤에 만드는 창 · 덮개 머리가 이 이름을 읽는다)
        static void Nick2(GameFlow flow, string nick)
        {
            var live = FakeGame(flow, true);
            live.Room.Seats[1].Nickname = nick;
            flow.Room.ShowGame(live);
            if (flow.SeatName(1) != nick) throw new InvalidOperationException("좌석 2 이름이 방 닉네임이 아니다: " + flow.SeatName(1));
        }

        // 덮개의 주인이 그 좌석이고 그 얼굴 상자(좌석 색) + 이름이 든 글이 한 줄에 있다(결정 120 · #707)
        static void CardOwner(GameFlow flow, string face, int seat)
        {
            if (flow.Cards == null || flow.Cards.OwnerShown != seat) throw new InvalidOperationException("덮개의 주인이 좌석 " + (seat + 1) + "이 아니다: " + (flow.Cards != null ? flow.Cards.OwnerShown : -9));
            OwnerCheck(flow, flow.Stage.Root.Q(face), seat, face);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("card-fx"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(덮개) " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        // 내 덮개에는 얼굴 상자가 없다
        static void CardMine(GameFlow flow)
        {
            var root = flow.Stage.Root;
            if (flow.Cards == null || flow.Cards.OwnerShown != -1) throw new InvalidOperationException("내 덮개에 주인 좌석이 섰다: " + (flow.Cards != null ? flow.Cards.OwnerShown : -9));
            foreach (string n in new[] { "card-fx-owner", "card-fx-mini-owner" })
                if (root.Q(n).resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("내 덮개에 얼굴 상자가 보인다: " + n);
        }

        /// <summary>남 좌석 창 · 덮개 머리(결정 120 · #707): 얼굴 상자가 보이고 테두리가 그 좌석 색이며, 같은 줄에 그 좌석 이름이 든 글이 있다.</summary>
        internal static void OwnerCheck(GameFlow flow, VisualElement face, int seat, string where)
        {
            if (face == null || face.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException(where + ": 머리에 그 좌석 얼굴 상자가 없다");
            if (!FaceBox.SeatColorOf(face).Equals(flow.SeatColor(seat))) throw new InvalidOperationException(where + ": 얼굴 상자 색이 좌석 " + (seat + 1) + " 색이 아니다");
            if (FaceBox.Shown(face) != flow.Protagonist(seat)) throw new InvalidOperationException(where + ": 얼굴이 좌석 " + (seat + 1) + "의 주인공이 아니다: " + FaceBox.Shown(face));
            string name = flow.SeatName(seat);
            if (!face.parent.Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "").Contains(name))) throw new InvalidOperationException(where + ": 얼굴 상자 줄에 이름 「" + name + "」이 없다");
        }

        // 보드의 트레이너 카드 연출 — row 49 카드 바꾸기(youngster R — 등산가를 고른다) · 50 능력 사용(gardener B — 선 약한 나무) · 35 날씨 휠(사당에서 모래바람으로), 그 단계 안 ms
        static void CardFxAt(UiShot s, string card, int row, int ms, int logLines = 0)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, card, state =>
            {
                var st = state.Seats[0];
                if (row == 49)
                {
                    st.TrainerCardOffer.Clear();
                    foreach (string k in new[] { "hiker", "swimmer", "chef" }) st.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
                    st.UnlockDue.Add(TrainerRules.DueSwap);
                    st.UnlockDue.Add(0);
                }
                if (row == 50)
                    foreach (var t in state.Tokens)
                        if (t.Kind == TokenKind.ApricornTree && !t.Destroyed && t.Cell >= 0 && ctx.Fixtures.Fixtures[t.Def].Tier == EncounterTier.Weak) { st.Cell = t.Cell; break; }
                if (row == 35)
                {
                    state.Weather = 0;
                    st.Jp = Math.Max(st.Jp, TrainerRules.WeatherCost);
                    for (int c = 0; c < ctx.Board.Count; c++)
                        if (ShrineRules.ShrineAt(state, c) >= 0) { st.Cell = c; break; }
                }
            });
            var hud = flow.Hud;
            // 기록 판을 줄로 채운다(늘 보이는 「이어서 시작」 줄 — 판 높이만 본다)
            for (int i = 0; i < logLines; i++) flow.Log.Resumed(flow.Client.View.Round, 1);
            hud.Refresh();
            if (logLines > 0 && flow.Stage.Root.Q("board-hud-log-lines").childCount != Math.Min(logLines, BoardHud.RecentLines)) throw new InvalidOperationException("기록 판이 " + logLines + "줄이 아니다");
            int target;
            switch (row)
            {
                case 49:
                    WalkOne(flow, hud);
                    var swap = (CardSwapScreen)Wait(flow, () => flow.Stage.Current is CardSwapScreen w && flow.BoardOpen && w.Answers.Count > 0);
                    if (!swap.Pick(0) || !swap.Confirm()) throw new InvalidOperationException("카드 바꾸기 창의 카드가 거절됐다");
                    target = SwapBeats.Row;
                    break;
                case 50:
                    if (!hud.Press(BoardHud.Ability)) throw new InvalidOperationException("정원사 능력 단추가 거절됐다");
                    if (hud.AbilityPicking && hud.AbilityShown.Count > 0) hud.PickAbility(0);
                    target = AbilityBeats.Row;
                    break;
                default:
                    if (!hud.Press(BoardHud.Weather) || !hud.AbilityPicking) throw new InvalidOperationException("[날씨 휠]이 열리지 않았다");
                    int sand = -1;
                    for (int i = 0; i < hud.AbilityShown.Count && sand < 0; i++) if (hud.AbilityShown[i].Arg == 3) sand = i;
                    if (sand < 0 || !hud.PickAbility(sand)) throw new InvalidOperationException("모래바람 단추가 거절됐다");
                    target = WeatherBeats.Row;
                    break;
            }
            var step = Queued(flow, st => st.Row == target);
            if (step == null) throw new InvalidOperationException(target + "행 단계가 대기열에 없다");
            Laid(s, flow, step, ms);
            var fx = flow.Cards;
            if (row == 49 && (fx == null || !fx.SwapShown || (ms >= SwapBeats.LitAt && !fx.LitShown))) throw new InvalidOperationException("카드 바꾸기 덮개가 보이지 않는다");
            if (row == 50 && (fx == null || !fx.AbilityShown || flow.Stage.Root.Q("card-fx-mini") == null)) throw new InvalidOperationException("능력 사용의 작은 카드가 보이지 않는다");
            if (row == 50) Apart(flow.Stage.Root, "card-fx-mini", "board-hud-log");   // 작은 카드가 기록 판을 덮지 않는다
            if (row != 35) CardMine(flow);
            if (row == 35)
            {
                if (hud.WeatherShown < 0) throw new InvalidOperationException("날씨 배지 돌림판이 돌지 않는다");
                if (ms < WeatherBeats.SpinMs && !hud.WeatherSpinning) throw new InvalidOperationException("아직 돌아야 한다");
                if (ms >= WeatherBeats.SpinMs && hud.WeatherShown != 3) throw new InvalidOperationException("모래바람에 멈추지 않았다(" + hud.WeatherShown + ")");
            }
        }
    }
}
