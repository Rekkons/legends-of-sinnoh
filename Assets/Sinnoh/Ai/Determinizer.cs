using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>채우기 보고(시험·시험대가 본다): 공개 정보로 세운 풀이 모자라 메운 수. 0이면 채운 판의 카드 구성이 원본과 같은 묶음이다.</summary>
    public sealed class FillReport
    {
        /// <summary>덱마다, 같은 종류 다른 덱의 남는 카드에서 빌린 장수(남은 스타팅 포켓몬이 약한 덱에 섞이는 R15 같은 정상 경우 포함).</summary>
        public readonly int[] Borrowed = new int[GameState.DeckCount];
        /// <summary>덱마다, 빌릴 것도 없어 덱 구성에서 아무 카드나 넣은 장수(셈이 어긋난 것).</summary>
        public readonly int[] Padded = new int[GameState.DeckCount];
        /// <summary>풀이 모자라 아무 종류나 넣은 숨겨진 장소 토큰 수.</summary>
        public int PaddedTokens;
        /// <summary>되살리지 못한 남의 비공개 선택 수(거래 응답 · 동등한 교환 — 후보가 가려진 채 남는다).</summary>
        public int Unresolved;

        public void Clear()
        {
            Array.Clear(Borrowed, 0, Borrowed.Length);
            Array.Clear(Padded, 0, Padded.Length);
            PaddedTokens = 0;
            Unresolved = 0;
        }
    }

    /// <summary>
    /// 숨은 정보 채우기(01 §9-3·§9-4, 결정 26, M8). 보기 사본(<see cref="Engine.ViewFor"/>)의 가려진 칸(<see cref="Engine.Unknown"/>)을
    /// 「보기와 어긋나지 않는 값」으로 채워 엔진이 그대로 도는 온전한 판을 만든다. 보기와 AI 난수만 읽는다 — 호스트 원본을 받으면 예외(엿보기 없음: 보기가 같으면 채운 판도 같다).
    /// 채우는 것(1단계 = 공개 정보만으로 세운 조건부 균등, 01 §13 M8-1):
    /// ① 뒷면 숨겨진 장소 = 토큰 구성에서 앞면으로 드러난 것을 뺀 나머지(금색 ?와 보통을 따로 — 금색 여부는 공개 값).
    /// ② 덱·남의 운명 손패·남의 트레이너 카드 후보 = 덱 구성 − 버림 더미 − 판에 보이는 카드(포켓몬 인스턴스·밑에 둔 카드·스탬프 카드·고른 트레이너 카드·균열 카드·열린 조우 고르기의 카드)에서 비복원으로.
    ///    섞지 않는 덱(스타팅·고향 마을·막)은 데이터 순서, 나머지는 무작위 순서. 내용이 보이는 덱(전설)은 순서만 섞는다.
    /// ③ 가려진 추종자 = 세 신 중 균등. ④ 게임 난수 = AI 난수에서 새로(<see cref="SampledRng.Seed"/>). ⑤ 남의 비공개 선택(버리기·스타팅 포켓몬·트레이너 카드 고르기·비공개 반응·동시 선택)의 기본 답(첫 합법 답)·받아 둔 답(합법 답 중 균등).
    /// 미뤄 둔 선택(Held)은 보기에 없어 되살리지 않는다.
    /// </summary>
    public static class Determinizer
    {
        /// <summary>보기를 채운 새 판(호스트 원본 꼴 — Viewer = HostViewer라 <see cref="Engine.Submit"/>을 받는다).</summary>
        public static GameState Fill(GameState view, Ctx ctx, SampledRng rng, FillReport report = null)
        {
            var sim = new GameState();
            FillInto(sim, view, ctx, rng, report);
            return sim;
        }

        /// <summary>sim을 보기로 덮어쓴 뒤 채운다(탐색은 판을 새로 만들지 않고 돌려 쓴다 — 01 §13 #12).</summary>
        public static void FillInto(GameState sim, GameState view, Ctx ctx, SampledRng rng, FillReport report = null)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (view.Viewer == SeatId.HostViewer)
                throw new InvalidOperationException("Determinizer는 보기 사본(ViewFor)만 채운다 — 호스트 원본을 받았다(01 §9-4)");
            sim.CopyFrom(view);
            int viewer = view.Viewer;
            FillTokens(sim, ctx, rng, report);
            FillPiles(sim, ctx, rng, report);
            for (int i = 0; i < sim.Seats.Length; i++)
                if ((int)sim.Seats[i].Follower == Engine.Unknown) sim.Seats[i].Follower = (SinnohGod)(1 + rng.Next(3)); // 디아루가·펄기아·기라티나
            FillChoices(sim, ctx, rng, viewer, report);
            rng.Seed(sim);
            sim.Viewer = SeatId.HostViewer;
        }

        // ── ① 숨겨진 장소 ──

        static void FillTokens(GameState sim, Ctx ctx, SampledRng rng, FillReport report)
        {
            var items = ctx.Items.Items;
            var regular = new List<int>();
            var special = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                var k = items[i].Kind;
                if (k != ItemKind.HiddenSpot && k != ItemKind.AlphaPokemon && k != ItemKind.Artifact && k != ItemKind.CelestialArtifact) continue;
                var pool = k == ItemKind.HiddenSpot ? regular : special;
                for (int c = 0; c < (items[i].Copies ?? 0); c++) pool.Add(i);
            }
            bool any = false;
            foreach (var t in sim.Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot) continue;
                if (t.Def < 0) any = true;
                else (items[t.Def].Kind == ItemKind.HiddenSpot ? regular : special).Remove(t.Def);
            }
            if (!any) return;
            Shuffle(regular, rng);
            Shuffle(special, rng);
            foreach (var t in sim.Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot || t.Def >= 0) continue;
                var pool = t.Special ? special : regular;
                if (pool.Count > 0)
                {
                    t.Def = pool[pool.Count - 1];
                    pool.RemoveAt(pool.Count - 1);
                    continue;
                }
                // 셈이 어긋났다: 그 갈래의 아무 종류
                if (report != null) report.PaddedTokens++;
                var all = new List<int>();
                for (int i = 0; i < items.Count; i++)
                    if (t.Special ? (items[i].Kind == ItemKind.AlphaPokemon || items[i].Kind == ItemKind.Artifact || items[i].Kind == ItemKind.CelestialArtifact) : items[i].Kind == ItemKind.HiddenSpot)
                        all.Add(i);
                t.Def = all[rng.Next(all.Count)];
            }
        }

        // ── ② 덱 · 손 ──

        static bool Ordered(DeckId d) => d == DeckId.Starter || d == DeckId.HomeVillage || d == DeckId.Act;

        /// <summary>
        /// 보이는 카드 한 장을 그 덱의 풀에서 뺀다. 그 덱 구성에 없는 카드면 같은 종류의 다른 덱 풀에서 뺀다 — 스타팅 포켓몬은 고른 것도 남은 것도 약한 덱 카드로 친다(R15, rulings G12).
        /// 어느 덱 구성에도 없는 카드(폼·복제한 진화 카드)는 그냥 지나간다.
        /// </summary>
        static void Take(List<int>[] pools, int deck, int card)
        {
            if (deck < 0 || deck >= pools.Length || card < 0 || pools[deck].Remove(card)) return;
            var kind = Ctx.KindOf((DeckId)deck);
            for (int d = 0; d < pools.Length; d++)
                if (d != deck && Ctx.KindOf((DeckId)d) == kind && pools[d].Remove(card)) return;
        }

        static void FillPiles(GameState sim, Ctx ctx, SampledRng rng, FillReport report)
        {
            int n = GameState.DeckCount;
            var pools = new List<int>[n];
            for (int d = 0; d < n; d++)
            {
                var pool = pools[d] = new List<int>();
                foreach (var e in ctx.Deck((DeckId)d).Cards)
                    for (int k = 0; k < e.Copies; k++) pool.Add(e.Card);
            }
            for (int d = 0; d < n; d++)
            {
                foreach (int card in sim.Discards[d]) Take(pools, d, card);
                foreach (int card in sim.Decks[d]) Take(pools, d, card);
            }
            // 판에 보이는 카드
            foreach (var p in sim.Pokemon)
            {
                if (p.Def < 0) continue;
                Take(pools, p.Deck, p.FormBeforeBattle >= 0 ? p.FormBeforeBattle : p.Def);
                for (int i = 0; i + 2 < p.Under.Count; i += 3) Take(pools, p.Under[i], p.Under[i + 1]);
                foreach (int b in p.Breach) Take(pools, (int)DeckId.DistortionBreach, b);
            }
            foreach (var st in sim.Seats)
            {
                for (int i = 0; i + 1 < st.StampCards.Count; i += 2) Take(pools, st.StampCards[i], st.StampCards[i + 1]);
                Take(pools, (int)DeckId.TrainerCard, st.TrainerCard);
                foreach (int card in st.FateHand) Take(pools, (int)DeckId.ChangeOfFate, card);
                foreach (int card in st.TrainerCardOffer) Take(pools, (int)DeckId.TrainerCard, card);
            }
            // 열린 조우 고르기의 뽑은 카드(고를 때까지 Options에 있다 — 공개): 그 카드가 든 포켓몬 덱에서 뺀다
            foreach (var c in sim.Pending)
            {
                if (c.Kind != ChoiceKind.EncounterPick || c.Answered) continue;
                foreach (int card in c.Options)
                    for (int d = 0; d < n; d++)
                        if (Ctx.KindOf((DeckId)d) == DeckKind.Pokemon && pools[d].Remove(card)) break;
            }
            if (sim.Ub != null)
            {
                for (int i = 0; i + 1 < sim.Ub.BreachHands.Count; i += 2) Take(pools, (int)DeckId.DistortionBreach, sim.Ub.BreachHands[i + 1]);
                for (int i = 0; i + 1 < sim.Ub.BreachStored.Count; i += 2) Take(pools, (int)DeckId.DistortionBreach, sim.Ub.BreachStored[i + 1]);
            }

            // 덱마다 채울 자리(손이 먼저, 덱이 다음)를 세고 풀에서 뽑는다. 남는 카드는 같은 종류 덱이 빌려 쓴다
            var spare = new List<int>[Enum.GetValues(typeof(DeckKind)).Length];
            for (int k = 0; k < spare.Length; k++) spare[k] = new List<int>();
            var lack = new List<int>[n]; // 덱마다 못 채운 자리: 둘씩 [목록 번호(−1 = 덱, 0 이상 = 좌석), 자리]
            for (int d = 0; d < n; d++)
            {
                var id = (DeckId)d;
                var pool = pools[d];
                var pile = sim.Decks[d];
                int need = Count(pile);
                if (id == DeckId.ChangeOfFate) foreach (var st in sim.Seats) need += Count(st.FateHand);
                if (id == DeckId.TrainerCard) foreach (var st in sim.Seats) need += Count(st.TrainerCardOffer);
                if (Ordered(id))
                {
                    // 데이터 순서를 지킨다: 넘치면 아무 카드나 덜어 낸다
                    while (pool.Count > need)
                    {
                        int at = rng.Next(pool.Count);
                        spare[(int)Ctx.KindOf(id)].Add(pool[at]);
                        pool.RemoveAt(at);
                    }
                    if (id != DeckId.Act) pool.Reverse(); // 아래에서 끝부터 꺼낸다 — 막은 끝 = 맨 위 = 데이터 첫 카드
                }
                else
                {
                    Shuffle(pool, rng);
                    if (need == 0 && pile.Count > 1) Shuffle(pile, rng); // 내용이 보이는 덱(전설): 순서만 가려져 있다
                }
                lack[d] = new List<int>();
                if (id == DeckId.ChangeOfFate)
                    for (int i = 0; i < sim.Seats.Length; i++) Deal(sim.Seats[i].FateHand, pool, lack[d], i);
                if (id == DeckId.TrainerCard)
                    for (int i = 0; i < sim.Seats.Length; i++) Deal(sim.Seats[i].TrainerCardOffer, pool, lack[d], i);
                Deal(pile, pool, lack[d], -1);
                spare[(int)Ctx.KindOf(id)].AddRange(pool);
                pool.Clear();
            }
            for (int k = 0; k < spare.Length; k++) Shuffle(spare[k], rng);
            for (int d = 0; d < n; d++)
            {
                var id = (DeckId)d;
                var rest = spare[(int)Ctx.KindOf(id)];
                var cards = ctx.Deck(id).Cards;
                for (int i = 0; i + 1 < lack[d].Count; i += 2)
                {
                    int who = lack[d][i], at = lack[d][i + 1];
                    var list = who < 0 ? sim.Decks[d] : id == DeckId.ChangeOfFate ? sim.Seats[who].FateHand : sim.Seats[who].TrainerCardOffer;
                    if (rest.Count > 0)
                    {
                        list[at] = rest[rest.Count - 1];
                        rest.RemoveAt(rest.Count - 1);
                        if (report != null) report.Borrowed[d]++;
                    }
                    else
                    {
                        list[at] = cards[rng.Next(cards.Count)].Card;
                        if (report != null) report.Padded[d]++;
                    }
                }
            }
        }

        static int Count(List<int> list)
        {
            int n = 0;
            foreach (int v in list)
                if (v == Engine.Unknown) n++;
            return n;
        }

        /// <summary>가려진 자리를 풀 끝에서 꺼내 채운다. 풀이 바닥나면 못 채운 자리를 lack에 적는다.</summary>
        static void Deal(List<int> list, List<int> pool, List<int> lack, int who)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != Engine.Unknown) continue;
                if (pool.Count == 0)
                {
                    lack.Add(who);
                    lack.Add(i);
                    continue;
                }
                list[i] = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
        }

        // ── ⑤ 남의 비공개 선택 ──

        static void FillChoices(GameState sim, Ctx ctx, SampledRng rng, int viewer, FillReport report)
        {
            var legal = new List<ChoiceAnswer>();
            foreach (var c in sim.Pending)
            {
                if (c.Seat == viewer || !ChoiceRules.HidesFromOthers(c) || (c.Sub == ChoiceSub.Trade && c.Subject == viewer)) continue; // Engine.ViewFor가 가린 것만
                // 후보가 가려진 채로는 답을 셀 수 없는 것: 거래 응답(거래 내용) · 동등한 교환(조우 덱에서 고른 카드)
                if ((c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade) || (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.ShrineExchangeCard) || c.Seat < 0 || c.Seat >= sim.Seats.Length)
                {
                    if (report != null) report.Unresolved++;
                    continue;
                }
                if (c.Kind == ChoiceKind.StarterPick && sim.Seats[c.Seat].HomeVillage >= 0)
                {
                    // 스타팅 포켓몬 후보 = 그 좌석 고향 마을의 스타팅 포켓몬(공개 — SetupFlow)
                    c.Options.Clear();
                    c.Options.AddRange(ctx.Fixtures.Fixtures[sim.Seats[c.Seat].HomeVillage].Starters);
                }
                else if (c.Options.Contains(Engine.Unknown))
                {
                    if (report != null) report.Unresolved++;
                    continue;
                }
                legal.Clear();
                Engine.Legal(sim, ctx, c, legal);
                if (legal.Count == 0)
                {
                    if (report != null) report.Unresolved++;
                    continue;
                }
                // 보기에는 가린 답의 값 개수(Values.Count)가 남는다 — 그 개수에 맞는 답만 넣는다(채운 판의 보기가 처음 보기와 같게)
                int want = c.Default.Values.Count;
                var first = legal.Find(x => x.Values.Count == want);
                if (first != null) c.Default = first.Clone();
                else if (report != null) report.Unresolved++;
                if (!c.Answered) continue;
                want = c.Answer.Values.Count;
                legal.RemoveAll(x => x.Values.Count != want);
                if (legal.Count > 0) c.Answer = legal[rng.Next(legal.Count)].Clone();
                else if (report != null) report.Unresolved++;
            }
        }

        /// <summary>Fisher–Yates(끝에서 앞으로). <see cref="Piles.Shuffle"/>과 달리 판 손보기 갈고리(<see cref="Rig"/>)를 부르지 않는다 — AI 표본은 튜토리얼 각본과 무관하다.</summary>
        static void Shuffle(List<int> pile, SampledRng rng)
        {
            for (int i = pile.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = pile[i];
                pile[i] = pile[j];
                pile[j] = t;
            }
        }
    }
}
