using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>셋업 흐름(01 §3-5, A20-1, R3·R15, rulings G12, M3-d).</summary>
    public class SetupTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Fixture(string key) => ctx.Fixtures.Ids.IndexOf(key);

        [Test]
        public void G12_마을은반시계_마지막고른좌석이첫턴()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 4, seed, log);
                var picks = Flow.Of<VillagePicked>(log);
                Assert.AreEqual(4, picks.Count);
                for (int i = 1; i < picks.Count; i++) Assert.AreEqual((picks[i - 1].Seat + 3) % 4, picks[i].Seat, "반시계 = 좌석 번호가 줄어든다");
                int last = picks[3].Seat;
                Assert.AreEqual(last, s.RoundStartSeat);
                Assert.AreEqual(last, s.ActiveSeat);
                var turn = Flow.Of<TurnStarted>(log)[0];
                Assert.AreEqual(last, turn.Seat, "마지막에 고른 좌석이 첫 턴(S2_5)");
                Assert.AreEqual(1, turn.Round);
                Assert.AreEqual(last, Flow.Only(s, ChoiceKind.MapAction).Seat);
                foreach (var p in picks)
                {
                    Assert.AreEqual(p.VillageKey, s.Seats[p.Seat].HomeVillage);
                    Assert.AreEqual(ctx.Fixtures.Fixtures[p.VillageKey].Cell, s.Seats[p.Seat].Cell, "말을 고향 마을에");
                    Assert.AreEqual("home_" + ctx.Fixtures.Fixtures[p.VillageKey].Key, ctx.Decks.CardIds[p.SeatColor], "좌석 색 = 고향 마을 카드");
                }
            }
        }

        [Test]
        public void G12_스타터는마을카드의두마리()
        {
            // 처음 고르는 좌석이 장막(Stark)을 고르면 그 좌석의 스타터 후보는 불꽃숭이·브케인(마을 카드 그림)
            int stark = Fixture("village.stark");
            int firstSeat = -1;
            List<int> offered = null;
            var log = new EventLog();
            var s = Flow.Started(ctx, 3, 11, log, answer: (st, c) =>
            {
                if (c.Kind == ChoiceKind.VillagePick && firstSeat < 0)
                {
                    firstSeat = c.Seat;
                    return ChoiceAnswer.Pick(stark);
                }
                if (c.Kind == ChoiceKind.StarterPick && c.Seat == firstSeat)
                {
                    offered = new List<int>(Scene.Legal(st, ctx, c).ConvertAll(x => x.Target));
                    return ChoiceAnswer.Pick(Scene.Def(ctx, "155:0"));
                }
                return null;
            });
            CollectionAssert.AreEqual(new[] { Scene.Def(ctx, "390:0"), Scene.Def(ctx, "155:0") }, offered);
            int mon = s.Seats[firstSeat].Party[0];
            Assert.AreEqual(Scene.Def(ctx, "155:0"), s.Pokemon[mon].Def);
            Assert.AreEqual(firstSeat, s.Pokemon[mon].Owner);
            Assert.AreEqual((int)DeckId.Weak, s.Pokemon[mon].Deck, "고른 스타터는 약한 덱에서 온 것(G12)");
            foreach (var seat in s.Seats) Assert.AreEqual(1, PokemonRules.PartyCount(seat), "좌석마다 스타터 1마리");
            var started = Flow.Of<StarterPicked>(log);
            Assert.AreEqual(3, started.Count);
            Assert.AreEqual(0, started.Find(x => x.Seat == firstSeat).PartySlot);
            // 비공개 동시: 모두 답할 때까지 SecretAnswered만, 공개는 마지막에 한 번
            Assert.AreEqual(6, Flow.Of<SecretAnswered>(log).Count, "3좌석 × (스타터 + 트레이너 카드)");
            Assert.AreEqual(1, Flow.Of<SecretRevealed>(log).Count);
        }

        [Test]
        public void G12_울트라메가로폴리스는울트라버스트에서만()
        {
            int um = Fixture("village.ultra_megalopolis");
            foreach (var campaign in new[] { Campaign.HeartOfSinnoh, Campaign.UltraBurst })
            {
                var s = Engine.NewGame(ctx, new RuleOptions(), 2, 3, campaign);
                Engine.Start(s, ctx, null);
                var legal = Scene.Legal(s, ctx, Flow.Only(s, ChoiceKind.VillagePick)).ConvertAll(x => x.Target);
                Assert.AreEqual(campaign == Campaign.UltraBurst, legal.Contains(um), campaign.ToString());
                Assert.AreEqual(campaign == Campaign.UltraBurst ? 9 : 8, legal.Count);
            }
        }

        [Test]
        public void A20_1_트레이너카드3장중1장_나머지는덱에섞음()
        {
            var log = new EventLog();
            var s = Engine.NewGame(ctx, new RuleOptions(), 3, 5);
            Engine.Start(s, ctx, log);
            Assert.AreEqual(51 - 9, s.Decks[(int)DeckId.TrainerCard].Count);
            var dealt = Flow.Of<TrainerCardsDealt>(log);
            Assert.AreEqual(3, dealt.Count);
            foreach (var d in dealt)
            {
                CollectionAssert.AreEqual(s.Seats[d.Seat].TrainerCardOffer, d.CardKeys);
                Assert.AreEqual(3, d.Count);
                var other = (TrainerCardsDealt)Engine.FilterFor(d, (d.Seat + 1) % 3);
                Assert.IsNull(other.CardKeys, "남에게는 장수만(01 §4)");
                Assert.AreEqual(3, other.Count);
            }
            s = Flow.Started(ctx, 3, 5, log);
            Assert.AreEqual(51 - 3, s.Decks[(int)DeckId.TrainerCard].Count, "고르지 않은 2장씩은 덱으로");
            var revealed = Flow.Of<TrainerCardsRevealed>(log);
            Assert.AreEqual(1, revealed.Count);
            Assert.AreEqual(6, revealed[0].ReturnedCount);
            foreach (var p in revealed[0].Picks)
            {
                Assert.AreEqual(p.CardKey, s.Seats[p.Seat].TrainerCard);
                Assert.AreEqual(1, s.Seats[p.Seat].TrainerCardTier, "브론즈(A20-2)");
                Assert.AreEqual(0, s.Seats[p.Seat].TrainerCardOffer.Count);
            }
            Assert.IsTrue(Flow.Of<PileShuffled>(log).Exists(x => x.Pile == DeckId.TrainerCard), "돌려받은 카드를 섞는다");

            // 확장을 끄면 배분도 고르기도 없다(결정 14 ④)
            var off = new EventLog();
            var t = Flow.Started(ctx, 3, 5, off, new RuleOptions { TrainerCards = false });
            Assert.AreEqual(51, t.Decks[(int)DeckId.TrainerCard].Count);
            Assert.AreEqual(0, Flow.Of<TrainerCardsDealt>(off).Count);
            Assert.AreEqual(0, Flow.Of<TrainerCardsRevealed>(off).Count);
            foreach (var seat in t.Seats) Assert.AreEqual(-1, seat.TrainerCard);
        }

        [Test]
        public void R15_남은스타터는약한덱에섞임()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 4, 9, log);
            Assert.AreEqual(0, s.Decks[(int)DeckId.Starter].Count);
            Assert.AreEqual(ctx.Deck(DeckId.Weak).Count + 18 - 4, s.Decks[(int)DeckId.Weak].Count);
            var ret = Flow.Of<CardsReturned>(log).Find(x => x.Returns.Length == 1 && x.Returns[0].Pile == DeckId.Weak);
            Assert.AreEqual(14, ret.Returns[0].Count);
            Assert.IsTrue(Flow.Of<PileShuffled>(log).Exists(x => x.Pile == DeckId.Weak));
        }

        [Test]
        public void R3_숨겨진장소52개는빈야생칸에지역이겹치지않게()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 2, seed, log);
                var fixtureCells = new HashSet<int>();
                foreach (var f in ctx.Fixtures.Fixtures) fixtureCells.Add(f.Cell);
                var open = new HashSet<string>(); // 빈 야생 칸이 있는 지역(57지역 중 51곳 — 고정물로 꽉 찬 지역 6곳)
                for (int c = 0; c < ctx.Board.Count; c++)
                    if (ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && !fixtureCells.Contains(c)) open.Add(ctx.Board[c].Region);
                var cells = new HashSet<int>();
                var regions = new HashSet<string>();
                int n = 0;
                foreach (var t in s.Tokens)
                {
                    if (t.Kind != TokenKind.HiddenSpot) continue;
                    n++;
                    Assert.GreaterOrEqual(t.Cell, 0, "모두 놓인다");
                    Assert.IsFalse(t.FaceUp);
                    var c = ctx.Board[t.Cell];
                    Assert.IsTrue(c.Wild && !c.DistortionWorld, "깨어진 세계가 아닌 야생 칸");
                    Assert.IsFalse(fixtureCells.Contains(t.Cell), "고정물 칸에는 놓지 않는다");
                    Assert.IsTrue(cells.Add(t.Cell), "한 칸에 하나");
                    regions.Add(c.Region);
                }
                Assert.AreEqual(52, n);
                Assert.AreEqual(51, open.Count);
                Assert.AreEqual(open.Count, regions.Count, "빈 칸이 있는 지역마다 하나씩 놓은 뒤에야 겹친다(B3 고침)");
                var placed = Flow.Of<TokensPlaced>(log).Find(x => x.Kind == TokenKind.HiddenSpot);
                Assert.AreEqual(52, placed.Cells.Length);
                var sorted = (int[])placed.Cells.Clone();
                System.Array.Sort(sorted);
                CollectionAssert.AreEqual(sorted, placed.Cells, "칸 색인 순서로 알린다(놓은 순서가 종류를 흘리지 않게, B2)");
                // 보기에서 뒷면 정체는 가려진다
                var v = Engine.ViewFor(s, 0);
                foreach (var t in v.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot) Assert.AreEqual(Engine.Unknown, t.Def);
            }
        }

        [Test]
        public void B3_놓을지역칸이없을때만지역기록을비움()
        {
            // 빈 야생 칸을 지역 X 두 칸·지역 Y 한 칸만 남기고 숨겨진 장소 3개를 놓는다.
            // 고친 배치: 둘째 토큰은 첫째와 다른 지역(남아 있으니까), 셋째는 지역 기록을 비운 뒤 남은 칸. Lua(B3)는 맞지 않는 칸 하나에서 바로 비워 둘째가 같은 지역에도 갔다.
            var board = ctx.Board;
            var fixtureCells = new HashSet<int>();
            foreach (var f in ctx.Fixtures.Fixtures) fixtureCells.Add(f.Cell);
            var byRegion = new Dictionary<string, List<int>>();
            var regionOrder = new List<string>();
            for (int c = 0; c < board.Count; c++)
            {
                if (!board[c].Wild || board[c].DistortionWorld || fixtureCells.Contains(c)) continue;
                if (!byRegion.TryGetValue(board[c].Region, out var l))
                {
                    byRegion[board[c].Region] = l = new List<int>();
                    regionOrder.Add(board[c].Region);
                }
                l.Add(c);
            }
            string rx = regionOrder.Find(r => byRegion[r].Count >= 2);
            string ry = regionOrder.Find(r => r != rx);
            var free = new List<int> { byRegion[rx][0], byRegion[rx][1], byRegion[ry][0] };

            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = Scene.Game(ctx, 2, seed);
                int spots = 0;
                foreach (var t in s.Tokens)
                {
                    if (t.Kind != TokenKind.HiddenSpot) continue;
                    if (ctx.Items.Items[t.Def].Kind != ItemKind.HiddenSpot || spots == 3) t.Destroyed = true; // 배치에서 뺀다
                    else spots++;
                }
                for (int c = 0; c < board.Count; c++)
                    if (board[c].Wild && !board[c].DistortionWorld && !fixtureCells.Contains(c) && !free.Contains(c))
                        s.Tokens.Add(new TokenState { Kind = TokenKind.Shrine, Def = 0, Cell = c, FaceUp = true }); // 칸을 막는 자리표시
                var placed = SetupFlow.PlaceHiddenSpots(s, ctx);
                Assert.AreEqual(3, placed.Count, $"시드 {seed}: 셋 다 놓인다");
                var order = new List<int>();
                foreach (var t in s.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot && !t.Destroyed) order.Add(t.Cell);
                Assert.AreNotEqual(board[order[0]].Region, board[order[1]].Region, $"시드 {seed}: 빈 지역 칸이 남아 있으면 지역이 겹치지 않는다");
            }
        }

        [Test]
        public void G12_1막공개때빨강규토리3()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 3, 4, log);
            Assert.AreEqual(1, s.Hos.Act);
            foreach (var seat in s.Seats) Assert.AreEqual(3, seat.Apricorns[(int)ApricornColor.Red]);
            var acts = Flow.Of<ActRevealed>(log);
            Assert.AreEqual(2, acts.Count, "서막 → 곧바로 1막");
            int incursion = ctx.Decks.CardIds.IndexOf("act.incursion"), journey = ctx.Decks.CardIds.IndexOf("act.journey");
            Assert.AreEqual(incursion, acts[0].ActKey);
            Assert.AreEqual(-1, acts[0].PrevActKey);
            Assert.AreEqual(-1, acts[0].FirstSeat);
            Assert.AreEqual(journey, acts[1].ActKey);
            Assert.AreEqual(incursion, acts[1].PrevActKey);
            CollectionAssert.AreEqual(new[] { incursion, journey }, s.Discards[(int)DeckId.Act], "공개한 막은 막 버림 더미(공개)");
            Assert.AreEqual(3, s.Decks[(int)DeckId.Act].Count);
            var gained = Flow.Of<ApricornsGained>(log);
            Assert.AreEqual(1, gained.Count);
            Assert.AreEqual(SourceKind.Act, gained[0].Source.Kind);
            Assert.AreEqual(journey, gained[0].Source.Id);
            Assert.AreEqual(3, gained[0].Changes.Length);
            foreach (var ch in gained[0].Changes)
            {
                Assert.AreEqual(ResKind.Apricorn, ch.Res);
                Assert.AreEqual((int)ApricornColor.Red, ch.Key);
                Assert.AreEqual(0, ch.Before);
                Assert.AreEqual(3, ch.After);
            }
        }

        [Test]
        public void S16_2_서막은모든고향마을에조무래기()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 3, 8, log);
            var homes = new List<int>();
            foreach (var seat in s.Seats) homes.Add(seat.HomeVillage);
            var grunts = new List<int>();
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Grunt)
                {
                    grunts.Add(t.Def);
                    Assert.AreEqual(ctx.Fixtures.Fixtures[t.Def].Cell, t.Cell);
                }
            CollectionAssert.AreEquivalent(homes, grunts);
            var placed = Flow.Of<TokensPlaced>(log).Find(x => x.Kind == TokenKind.Grunt);
            CollectionAssert.AreEqual(homes, placed.Villages);
            CollectionAssert.AreEqual(homes, Flow.Of<FlyBlocked>(log)[0].Villages);

            // 자동 단계마다 새 Seq(01 §3-6 ① (b)): 게임 시작 → 서막 카드 → 서막 토큰 → 1막 카드 → 라운드 → 턴
            var kinds = new List<ActionKind>();
            foreach (var a in Flow.Of<ActionStarted>(log))
                if (a.Auto == AutoKind.Auto) kinds.Add(a.Kind);
            CollectionAssert.AreEqual(new[] { ActionKind.Setup, ActionKind.Setup, ActionKind.ActReveal, ActionKind.Prologue, ActionKind.ActReveal, ActionKind.RoundStart, ActionKind.TurnStart }, kinds);
        }

        [Test]
        public void M3_셋업1to6인첫맵행동까지()
        {
            for (int n = 1; n <= SeatId.MaxSeats; n++)
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, n, 20261107, log, timeout: true);
                Assert.AreEqual(Phase.Turn, s.Phase, $"{n}인");
                Assert.AreEqual(1, s.Round);
                Assert.AreEqual(s.RoundStartSeat, Flow.Only(s, ChoiceKind.MapAction).Seat);
                var homes = new HashSet<int>();
                foreach (var seat in s.Seats)
                {
                    Assert.AreEqual(1, PokemonRules.PartyCount(seat));
                    Assert.IsTrue(homes.Add(seat.HomeVillage), "마을은 좌석마다 다르다");
                }
                // 카드 총수 보존: 포켓몬 카드 = 덱 + 버림 + 파티 인스턴스, 트레이너 카드 = 덱 + 앞면
                int pokemon = 0, start = 0;
                for (int d = 0; d <= (int)DeckId.GalacticBoss; d++)
                {
                    pokemon += s.Decks[d].Count + s.Discards[d].Count;
                    start += ctx.Deck((DeckId)d).Count;
                }
                foreach (var p in s.Pokemon)
                    if (p.Def >= 0) pokemon++;
                Assert.AreEqual(start, pokemon, $"{n}인 포켓몬 카드");
                int tc = s.Decks[(int)DeckId.TrainerCard].Count;
                foreach (var seat in s.Seats)
                    if (seat.TrainerCard >= 0) tc++;
                Assert.AreEqual(51, tc, $"{n}인 트레이너 카드");
                // 같은 시드·같은 명령 → 같은 해시, 이벤트 없이도 같다(01 §3-1)
                Assert.AreEqual(StateHash.Of(s), StateHash.Of(Flow.Started(ctx, n, 20261107, null, timeout: true)), $"{n}인 결정성");
                foreach (var t in Flow.Of<SecretAnswered>(log)) Assert.IsTrue(t.ByTimeout);
            }
        }
    }
}
