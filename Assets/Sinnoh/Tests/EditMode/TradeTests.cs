using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「거래·원격 도전」(rulings A29 — p.3 「freely trade」 · p.19 추종자 · 사용자 결정 75 B4 가 · B5 나 · ⑤ 옵션 · 원작 커뮤니티 확인): 같은 칸 거래 · 같은 신 원격 거래(남의 턴 제안 포함) ·
    /// 다른 신 원격 도전(늘 거절 가능). 추종자는 SeatState.Follower를 바로 놓는다(천상의 균열 묶음 전 — TradeRules.SameGod·DifferentGod 고리).
    /// 포켓몬: 모부기 387 · 불꽃숭이 390 · 팽도리 393 · 찌르꼬 396 · 비버니 399.
    /// </summary>
    public class TradeTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Give(GameState s, int seat, string key) => Scene.Give(s, ctx, seat, key);

        const int Me = 0, Them = 1;
        static readonly int Apricorn = (int)TradeKind.Apricorn, Jp = (int)TradeKind.Jp, Ball = (int)TradeKind.Ball,
            Item = (int)TradeKind.Item, Poke = (int)TradeKind.Pokemon, Fate = (int)TradeKind.Fate;

        /// <summary>좌석 둘(이상)을 마을 밖 한 칸에 세운다. 차례 좌석 0의 MapAction을 연다.</summary>
        GameState SameTile(int seats, IEventSink log, RuleOptions opt = null)
        {
            var s = Battles.Game(ctx, seats, 7, opt);
            int cell = Flow.StepInto(s, ctx, s.Seats[0].Cell);
            for (int i = 0; i < seats; i++) s.Seats[i].Cell = cell;
            Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            return s;
        }

        static PendingChoice Map(GameState s) => s.Pending.Find(c => c.Kind == ChoiceKind.MapAction);

        static PendingChoice Respond(GameState s) => s.Pending.Find(c => c.Kind == ChoiceKind.Respond);

        SubmitResult Propose(GameState s, ChoiceAnswer offer, IEventSink log)
        {
            var map = Map(s);
            return Engine.Submit(s, ctx, map.Seat, Command.AnswerTo(map.Id, offer), log);
        }

        void Reply(GameState s, ChoiceAnswer a, IEventSink log)
        {
            var c = Respond(s);
            Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, a), log), $"거래 응답 {a}");
        }

        static ChoiceAnswer Accept() => ChoiceAnswer.Of(AnswerKind.Accept);

        static T Last<T>(EventLog log) where T : GameEvent
        {
            var l = log.OfType<T>();
            return l.Count > 0 ? l[l.Count - 1] : null;
        }

        List<int> FateValues(GameState s, int n)
        {
            var r = new List<int>();
            foreach (int c in s.Decks[(int)DeckId.ChangeOfFate])
                if (!r.Contains(c) && r.Count < n) r.Add(c);
            Assert.AreEqual(n, r.Count, "서로 다른 운명 카드");
            return r;
        }

        // ── 같은 칸 거래 ──

        [Test]
        public void M5_거래_같은칸에서포켓몬과자원을통째로주고받음()
        {
            var log = new EventLog();
            var s = SameTile(2, log);
            int a1 = Give(s, Me, "387:0");
            int a2 = Give(s, Me, "390:0");
            int b1 = Give(s, Them, "393:0");
            s.Pokemon[a2].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            s.Pokemon[a2].Alpha = true;
            s.Pokemon[a2].Moves[0] = Scene.Move(ctx, "tackle");
            s.Pokemon[a2].Damage = 2;
            s.Seats[Me].Apricorns[(int)ApricornColor.Red] = 3;
            s.Seats[Me].Balls[(int)Rules.Ball.Greater] = 1;
            s.Seats[Them].Jp = 5;
            int sash = Scene.Item(ctx, "item.choice_band");
            s.Seats[Them].Bag.Add(sash);

            var partners = new List<int>();
            TradeRules.Partners(s, ctx, Me, partners);
            CollectionAssert.AreEqual(new[] { Them }, partners, "같은 칸의 트레이너(p.3, G7)");
            Assert.IsFalse(Scene.Legal(s, ctx, Map(s)).Exists(x => x.Kind == AnswerKind.ProposeTrade), "거래 내용은 Legal에 늘어놓지 않는다(IsLegal이 받는다)");

            // 정규 순서가 아니어도 Offer가 맞춘다
            var offer = TradeRules.Offer(Them, Them, Item, sash, 1, Me, Poke, a2, 1, Me, Apricorn, (int)ApricornColor.Red, 2, Me, Ball, (int)Rules.Ball.Greater, 1, Them, Jp, -1, 4);
            Scene.Accept(Propose(s, offer, log), "거래 제안");
            var proposed = Last<TradeProposed>(log);
            Assert.AreEqual(Me, proposed.From);
            Assert.AreEqual(Them, proposed.To);
            Assert.IsFalse(proposed.Remote, "같은 칸");
            Assert.AreEqual(3, proposed.Give.Length);
            Assert.AreEqual(2, proposed.Ask.Length);
            var respond = Battles.Only(s, ChoiceKind.Respond, ChoiceSub.Trade);
            Assert.AreEqual(Them, respond.Seat);
            Assert.AreEqual(Me, respond.Subject);
            Assert.AreEqual(AnswerKind.Decline, respond.Default.Kind, "시간 초과 = 거절 [가정]");
            Assert.AreEqual(2, Scene.Legal(s, ctx, respond).Count, "거절 → 받아들임");

            Reply(s, Accept(), log);
            Assert.IsTrue(Last<TradeAnswered>(log).Accept);
            Assert.AreEqual(proposed.Seq, Last<ActionStarted>(log).Continues, "제안 → 응답(01 §3-6 ①-2)");
            var done = Last<TradeDone>(log);
            Assert.AreEqual(Me, done.A);
            Assert.AreEqual(Them, done.B);
            Assert.IsFalse(done.Remote);

            Assert.AreEqual(-1, PokemonRules.PartyIndexOf(s.Seats[Me], a2));
            Assert.AreEqual(1, PokemonRules.PartyIndexOf(s.Seats[Them], a2), "받은 파티 맨 뒤");
            Assert.AreEqual(Them, s.Pokemon[a2].Owner);
            Assert.AreEqual(Scene.Item(ctx, "item.swiftness_sash"), s.Pokemon[a2].HeldItem, "지닌물건째로(결정 75 B4 가)");
            Assert.IsTrue(s.Pokemon[a2].Alpha, "우두머리 토큰째로");
            Assert.AreEqual(Scene.Move(ctx, "tackle"), s.Pokemon[a2].Moves[0], "배운 기술째로");
            Assert.AreEqual(2, s.Pokemon[a2].Damage, "피해도 그대로");
            Assert.AreEqual(0, PokemonRules.PartyIndexOf(s.Seats[Me], a1));
            Assert.AreEqual(0, PokemonRules.PartyIndexOf(s.Seats[Them], b1));
            Assert.AreEqual(1, s.Seats[Me].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(2, s.Seats[Them].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(0, s.Seats[Me].Balls[(int)Rules.Ball.Greater]);
            Assert.AreEqual(1, s.Seats[Them].Balls[(int)Rules.Ball.Greater]);
            Assert.AreEqual(4, s.Seats[Me].Jp);
            Assert.AreEqual(1, s.Seats[Them].Jp);
            CollectionAssert.Contains(s.Seats[Me].Bag, sash);
            CollectionAssert.DoesNotContain(s.Seats[Them].Bag, sash);
            Assert.AreEqual(4 * 2, done.Changes.Length, "규토리·볼·JP·아이템마다 두 좌석");
            Assert.AreEqual(Me, Battles.Only(s, ChoiceKind.MapAction).Seat, "거래는 턴을 끝내지 않는다(p.3)");
        }

        [Test]
        public void M5_거래_거절과시간초과는아무것도바꾸지않음()
        {
            foreach (bool timeout in new[] { false, true })
            {
                var log = new EventLog();
                var s = SameTile(2, log);
                Give(s, Me, "387:0");
                Give(s, Them, "393:0");
                s.Seats[Me].Jp = 3;
                var before = StateHash.Of(s);
                Scene.Accept(Propose(s, TradeRules.Offer(Them, Me, Jp, -1, 3), log));
                var r = Respond(s);
                if (timeout) Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(r.Id), log));
                else Reply(s, ChoiceAnswer.Of(AnswerKind.Decline), log);
                var ans = Last<TradeAnswered>(log);
                Assert.IsFalse(ans.Accept);
                Assert.AreEqual(timeout, ans.ByTimeout);
                Assert.IsNull(Last<TradeDone>(log));
                Assert.AreEqual(3, s.Seats[Me].Jp);
                Assert.AreEqual(Me, Battles.Only(s, ChoiceKind.MapAction).Seat, "거절당하면 턴이 이어진다");
                Assert.AreNotEqual(before, StateHash.Of(s), "Seq·선택 번호는 나아간다");
            }
        }

        [Test]
        public void M5_거래_내용이맞지않으면받지않음()
        {
            var s = SameTile(2, null);
            int a1 = Give(s, Me, "387:0");
            int a2 = Give(s, Me, "390:0");
            int b1 = Give(s, Them, "393:0");
            Give(s, Them, "396:0");
            Give(s, Them, "399:0");
            s.Seats[Me].Apricorns[0] = 1;
            s.Seats[Them].FateHand.AddRange(FateValues(s, 1));
            void No(ChoiceAnswer a, string why) => Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, Map(s), a), why);

            No(TradeRules.Offer(Them), "빈 거래");
            var unsorted = ChoiceAnswer.Of(AnswerKind.ProposeTrade, Them);
            unsorted.Values.AddRange(new[] { Them, Jp, -1, 1, Me, Apricorn, 0, 1 });
            No(unsorted, "정규 순서가 아님");
            No(TradeRules.Offer(Them, Me, Apricorn, 0, 2), "가진 것보다 많이");
            No(TradeRules.Offer(Them, Me, Poke, b1, 1), "남의 포켓몬");
            No(TradeRules.Offer(Them, Me, Poke, a1, 1, Me, Poke, a2, 1), "파티가 비면 안 된다");
            No(TradeRules.Offer(Them, Me, Apricorn, 0, 1, Them, Fate, s.Seats[Them].FateHand[0], 1), "상대 운명 카드는 장수만(Key −1)");
            No(TradeRules.Offer(Them, Them, Fate, -1, 2), "상대 손패보다 많이");
            No(TradeRules.Offer(Them, Me, Poke, a2, 1), "받는 파티가 열린 칸(3)을 넘는다");
            No(TradeRules.Offer(Me, Me, Apricorn, 0, 1), "자기 자신");
            Assert.IsTrue(ChoiceRules.IsLegal(s, ctx, Map(s), TradeRules.Offer(Them, Me, Apricorn, 0, 1)), "선물(받는 것 없음)은 된다 — 원작 커뮤니티 확인");
            Assert.IsTrue(ChoiceRules.IsLegal(s, ctx, Map(s), TradeRules.Offer(Them, Me, Poke, a2, 1, Them, Poke, b1, 1)), "한 마리씩 맞바꿈");
            s.Pokemon[a2].Breach[0] = 0;
            No(TradeRules.Offer(Them, Me, Poke, a2, 1, Them, Poke, b1, 1), "균열 카드가 붙은 포켓몬은 파티를 떠나지 않는다(p.24)");
        }

        [Test]
        public void A29_신오의심장은거래에넣지않는다_지닌포켓몬도()
        {
            // rulings A29 ① 「넣지 않는 것 [가정]: … 신오의 심장」(제작자도 트레이너전 보상에서 심장을 못 가져간다 — 원작 커뮤니티 확인)
            var s = SameTile(2, null);
            Give(s, Me, "387:0");
            int a2 = Give(s, Me, "390:0");
            Give(s, Them, "393:0");
            int heart = Scene.Item(ctx, HosFlow.HeartItemKey);
            bool Ok(ChoiceAnswer a) => ChoiceRules.IsLegal(s, ctx, Map(s), a);
            s.Seats[Me].Bag.Add(heart);
            Assert.IsFalse(Ok(TradeRules.Offer(Them, Me, Item, heart, 1)), "가방의 심장");
            s.Seats[Me].Bag.Remove(heart);
            s.Pokemon[a2].HeldItem = heart;
            Assert.IsFalse(Ok(TradeRules.Offer(Them, Me, Poke, a2, 1)), "심장을 지닌 포켓몬 [가정 — 지닌물건은 배틀 밖에서 바꿀 수 있으니 먼저 뗀다]");
            s.Pokemon[a2].HeldItem = -1;
            Assert.IsTrue(Ok(TradeRules.Offer(Them, Me, Poke, a2, 1)), "떼면 그 포켓몬은 거래한다");
        }

        [Test]
        public void M5_거래_옵션을끄면거래가없다()
        {
            var opt = new RuleOptions { Trading = false };
            var s = SameTile(3, null, opt);
            Give(s, Me, "387:0");
            Give(s, Them, "393:0");
            Give(s, 2, "396:0");
            s.Seats[Me].Jp = 1;
            s.Seats[1].Follower = s.Seats[2].Follower = SinnohGod.Dialga;
            var partners = new List<int>();
            TradeRules.Partners(s, ctx, Me, partners);
            Assert.AreEqual(0, partners.Count);
            Scene.Reject(Propose(s, TradeRules.Offer(Them, Me, Jp, -1, 1), null), RejectReason.Illegal, "내 턴");
            Scene.Reject(Engine.Submit(s, ctx, 1, Command.ProposeTrade(TradeRules.Offer(2, Them, Jp, -1, 1)), null), RejectReason.Timing, "남의 턴");
            Assert.IsTrue(new RuleOptions().Trading, "기본은 켬(결정 75 ⑤)");
        }

        [Test]
        public void G7_같은칸에멈추면거래뒤남은이동()
        {
            var s = Battles.Game(ctx, 2);
            Give(s, Me, "387:0");
            Give(s, Them, "393:0");
            s.Seats[Me].MovesLeft = 3;
            s.Seats[Them].Cell = Flow.StepInto(s, ctx, s.Seats[Me].Cell); // 내 고향 마을 옆 칸
            s.Seats[Me].Jp = 2;
            var log = new EventLog();
            Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = Me });
            var partners = new List<int>();
            TradeRules.Partners(s, ctx, Me, partners);
            Assert.AreEqual(0, partners.Count, "다른 칸이면 거래 없음");
            Battles.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, s.Seats[Them].Cell), log);
            Assert.AreEqual(2, s.Seats[Me].MovesLeft);
            Scene.Accept(Propose(s, TradeRules.Offer(Them, Me, Jp, -1, 2), log));
            Reply(s, Accept(), log);
            Assert.AreEqual(2, s.Seats[Them].Jp);
            var map = Battles.Only(s, ChoiceKind.MapAction);
            Assert.AreEqual(2, s.Seats[Me].MovesLeft, "거래는 이동력을 쓰지 않는다");
            Assert.IsTrue(Scene.Legal(s, ctx, map).Exists(x => x.Kind == AnswerKind.Move), "남은 이동으로 계속 간다(p.3)");
        }

        // ── 원격 거래(같은 신) ──

        [Test]
        public void M5_거래_다른칸이면같은신의추종자끼리만()
        {
            foreach (var them in new[] { SinnohGod.None, SinnohGod.Palkia, SinnohGod.Dialga })
            {
                var log = new EventLog();
                var s = Battles.Game(ctx, 2); // 각자 고향 마을 — 다른 칸
                Give(s, Me, "387:0");
                Give(s, Them, "393:0");
                s.Seats[Me].Jp = 1;
                s.Seats[Me].Follower = SinnohGod.Dialga;
                s.Seats[Them].Follower = them;
                Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = Me });
                bool same = them == SinnohGod.Dialga;
                Assert.AreEqual(same, TradeRules.SameGod(s, Me, Them));
                var r = Propose(s, TradeRules.Offer(Them, Me, Jp, -1, 1), log);
                if (!same)
                {
                    Scene.Reject(r, RejectReason.Illegal, $"{them}: 같은 신이 아니면 원격 거래 없음(p.19)");
                    continue;
                }
                Scene.Accept(r);
                Assert.IsTrue(Last<TradeProposed>(log).Remote, "원격(p.19 「They may trade remotely」)");
                Reply(s, Accept(), log);
                Assert.IsTrue(Last<TradeDone>(log).Remote);
                Assert.AreEqual(1, s.Seats[Them].Jp);
            }
        }

        [Test]
        public void M5_거래_남의턴에도원격으로제안하고응답이끝날때까지막는다()
        {
            var log = new EventLog();
            var s = Battles.Game(ctx, 3);
            Give(s, Me, "387:0");
            int p1 = Give(s, 1, "393:0");
            Give(s, 1, "396:0");
            Give(s, 2, "399:0");
            s.Seats[Me].MovesLeft = 3;
            s.Seats[1].Follower = s.Seats[2].Follower = SinnohGod.Giratina;
            s.Seats[1].Cell = s.Seats[2].Cell; // 같은 칸이어도 남의 턴에는 같은 신일 때만
            Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = Me });
            var offer = TradeRules.Offer(2, 0, Poke, p1, 1);

            var partners = new List<int>();
            TradeRules.Partners(s, ctx, 1, partners);
            CollectionAssert.AreEqual(new[] { 2 }, partners, "남의 턴: 같은 신의 추종자(결정 75 B5 나)");
            Scene.Reject(Engine.Submit(s, ctx, Me, Command.ProposeTrade(TradeRules.Offer(1, 0, Jp, -1, 1)), log), RejectReason.Timing, "차례 좌석은 MapAction으로 낸다");
            s.Seats[2].Follower = SinnohGod.Dialga;
            Scene.Reject(Engine.Submit(s, ctx, 1, Command.ProposeTrade(offer), log), RejectReason.Illegal, "남의 턴에는 같은 칸이어도 같은 신이어야");
            s.Seats[2].Follower = SinnohGod.Giratina;

            Scene.Accept(Engine.Submit(s, ctx, 1, Command.ProposeTrade(offer), log), "남의 턴 제안");
            var proposed = Last<TradeProposed>(log);
            Assert.IsTrue(proposed.OffTurn);
            Assert.AreEqual(ActionKind.ProposeTrade, Last<ActionStarted>(log).Kind);
            Assert.AreEqual(2, s.Pending.Count);
            Assert.AreEqual(ChoiceSub.Trade, s.Pending[0].Sub, "응답이 열린 선택의 맨 앞");
            Assert.AreEqual(ChoiceKind.MapAction, s.Pending[1].Kind, "차례 좌석의 MapAction은 그대로");
            Scene.Reject(Engine.Submit(s, ctx, 2, Command.ProposeTrade(TradeRules.Offer(1, 1, Poke, p1, 1)), log), RejectReason.Timing, "응답이 열려 있는 동안 또 제안하지 못함");

            // 차례 좌석이 움직이면 다음 MapAction은 거래가 끝날 때까지 미뤄 둔다(Held)
            var map = Map(s);
            var move = Scene.Legal(s, ctx, map).Find(x => x.Kind == AnswerKind.Move);
            Scene.Accept(Engine.Submit(s, ctx, Me, Command.AnswerTo(map.Id, move), log));
            Assert.AreEqual(1, s.Pending.Count, "거래 응답만 열려 있다");
            Assert.AreEqual(1, s.Held.Count, "MapAction은 미뤄 뒀다");
            Assert.AreEqual(0, Engine.ViewFor(s, Me).Held.Count, "보기에서는 비운다");

            Reply(s, Accept(), log);
            Assert.AreEqual(2, s.Pokemon[p1].Owner);
            Assert.AreEqual(0, s.Held.Count);
            Assert.AreEqual(Me, Battles.Only(s, ChoiceKind.MapAction).Seat, "거래가 끝나면 미뤄 둔 MapAction이 열린다");
        }

        [Test]
        public void M5_거래_응답중에내용이맞지않게되면거절만남는다()
        {
            var log = new EventLog();
            var s = SameTile(2, log);
            Give(s, Me, "387:0");
            int a2 = Give(s, Me, "390:0");
            Give(s, Them, "393:0");
            Scene.Accept(Propose(s, TradeRules.Offer(Them, Me, Poke, a2, 1), log));
            int before = Respond(s).Id;
            Assert.AreEqual(2, Scene.Legal(s, ctx, Respond(s)).Count);
            // 제안한 좌석이 그 포켓몬을 정비로 놓아준다 → 응답이 다시 열리고 거절만 남는다(01 §3-4 ChoiceReissued)
            Scene.Accept(Engine.Submit(s, ctx, Me, Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = a2, Value = s.Pokemon[a2].Def }), log));
            var after = Respond(s);
            Assert.AreNotEqual(before, after.Id, "다시 열림");
            var legal = Scene.Legal(s, ctx, after);
            Assert.AreEqual(1, legal.Count);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind);
        }

        // ── 운명 카드(숨은 정보) ──

        [Test]
        public void M5_거래_운명카드는당사자만보고받는쪽이고른다()
        {
            var log = new EventLog();
            var s = SameTile(3, log);
            Give(s, Me, "387:0");
            Give(s, Them, "393:0");
            Give(s, 2, "399:0");
            var cards = FateValues(s, 3);
            s.Seats[Me].FateHand.Add(cards[0]);
            s.Seats[Them].FateHand.AddRange(new[] { cards[1], cards[2], cards[2] });

            Scene.Accept(Propose(s, TradeRules.Offer(Them, Me, Fate, cards[0], 1, Them, Fate, -1, 1), log));
            var respond = Respond(s);
            var legal = Scene.Legal(s, ctx, respond);
            Assert.AreEqual(3, legal.Count, "거절 → 받아들임 × 줄 카드(같은 카드는 한 번)");
            Assert.AreEqual(1, legal[1].Values.Count);
            // 받는 좌석의 보기(제안한 좌석의 손패는 가려져 있다)에서도 같은 답 — ChoiceRules.Legal 「보기 사본에서도 자기 선택이면 같은 답」
            var themView = Engine.ViewFor(s, Them);
            var viewLegal = Scene.Legal(themView, ctx, themView.Pending.Find(c => c.Sub == ChoiceSub.Trade));
            Assert.AreEqual(legal.Count, viewLegal.Count, "받는 좌석의 보기에서도 받아들임이 있다");
            for (int i = 0; i < legal.Count; i++) Assert.IsTrue(legal[i].SameAs(viewLegal[i]), $"답 {i}");

            // 남의 보기: 응답의 거래 내용과 제안 이벤트의 운명 카드는 가린다(장수는 남는다). 당사자는 본다
            var third = Engine.ViewFor(s, 2).Pending.Find(c => c.Sub == ChoiceSub.Trade);
            Assert.IsTrue(third.Options.TrueForAll(x => x == Engine.Unknown));
            CollectionAssert.AreEqual(respond.Options, Engine.ViewFor(s, Me).Pending.Find(c => c.Sub == ChoiceSub.Trade).Options, "제안한 좌석은 본다");
            var proposed = Last<TradeProposed>(log);
            Assert.AreEqual(-1, ((TradeProposed)Engine.FilterFor(proposed, 2)).Give[0].Key);
            Assert.AreEqual(1, ((TradeProposed)Engine.FilterFor(proposed, 2)).Give[0].Count);
            Assert.AreEqual(cards[0], ((TradeProposed)Engine.FilterFor(proposed, Them)).Give[0].Key);
            var mapAnswer = log.OfType<ChoiceAnswered>().Find(e => e.Answer.Kind == AnswerKind.ProposeTrade);
            Assert.IsTrue(((ChoiceAnswered)Engine.FilterFor(mapAnswer, 2)).Answer.Values.TrueForAll(x => x == Engine.Unknown), "거래 답의 Values도 가린다");
            CollectionAssert.AreEqual(mapAnswer.Answer.Values, ((ChoiceAnswered)Engine.FilterFor(mapAnswer, Them)).Answer.Values);

            Reply(s, legal.Find(a => a.Kind == AnswerKind.Accept && a.Values[0] == cards[2]), log);
            CollectionAssert.AreEquivalent(new[] { cards[2] }, s.Seats[Me].FateHand);
            CollectionAssert.AreEquivalent(new[] { cards[1], cards[2], cards[0] }, s.Seats[Them].FateHand);
            var accepted = Last<ChoiceAnswered>(log);
            Assert.AreEqual(Engine.Unknown, ((ChoiceAnswered)Engine.FilterFor(accepted, 2)).Answer.Values[0], "고른 카드는 답한 좌석만");
            var done = Last<TradeDone>(log);
            var hidden = (TradeDone)Engine.FilterFor(done, 2);
            Assert.AreEqual(-1, hidden.GiveA[0].Key);
            Assert.AreEqual(-1, hidden.GiveB[0].Key);
            Assert.AreEqual(cards[2], ((TradeDone)Engine.FilterFor(done, Me)).GiveB[0].Key);
            Assert.AreEqual(2, System.Array.FindAll(done.Changes, c => c.Res == ResKind.Fate).Length, "운명은 장수만(Key −1)");
        }

        [Test]
        public void M5_거래_받은운명카드로6장이되면버리기를묻는다()
        {
            var log = new EventLog();
            var s = SameTile(2, log);
            Give(s, Me, "387:0");
            Give(s, Them, "393:0");
            var cards = FateValues(s, 2);
            s.Seats[Me].FateHand.Add(cards[0]);
            for (int i = 0; i < FateRules.HandLimit; i++) s.Seats[Them].FateHand.Add(cards[1]);
            Scene.Accept(Propose(s, TradeRules.Offer(Them, Me, Fate, cards[0], 1), log));
            Reply(s, Accept(), log);
            Assert.AreEqual(6, s.Seats[Them].FateHand.Count);
            Assert.AreEqual(ChoiceKind.Discard, s.Pending[0].Kind, "6장째(A12) — 버리기가 맨 앞");
            Assert.AreEqual(Them, s.Pending[0].Seat);
            Assert.AreEqual(ChoiceKind.MapAction, s.Pending[1].Kind, "제안한 좌석의 턴은 이어진다");
        }

        // ── 원격 도전(다른 신) ──

        [Test]
        public void A29_원격도전은다른신의추종자끼리_늘거절할수있다()
        {
            foreach (bool accept in new[] { false, true })
            {
                var log = new EventLog();
                var s = Battles.Game(ctx, 2); // 각자 고향 마을 — 다른 칸
                Give(s, Me, "387:0");
                Give(s, Them, "393:0");
                s.Seats[Me].Follower = SinnohGod.Dialga;
                s.Seats[Them].Follower = SinnohGod.Dialga;
                var map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = Me });
                Assert.IsFalse(Scene.Legal(s, ctx, map).Exists(x => x.Kind == AnswerKind.Challenge), "같은 신이면 원격 도전 없음");
                s.Seats[Them].Follower = SinnohGod.Palkia;
                Assert.IsTrue(TradeRules.DifferentGod(s, Me, Them));
                var legal = Scene.Legal(s, ctx, map);
                var remote = legal.Find(x => x.Kind == AnswerKind.Challenge && x.Target == Them);
                Assert.IsNotNull(remote, "다른 신의 추종자는 멀리서 도전(p.19)");
                Assert.AreEqual(TradeRules.RemoteArg, remote.Arg);

                Battles.Answer(s, ctx, map, remote, log);
                var issued = Last<ChallengeIssued>(log);
                Assert.IsTrue(issued.Remote);
                Assert.AreEqual(-1, issued.Cell);
                Assert.IsTrue(issued.CanRefuse, "마을 밖이어도 거절할 수 있다(원작 커뮤니티 확인");
                var respond = Battles.Only(s, ChoiceKind.Respond, ChoiceSub.Challenge);
                Assert.AreEqual(Them, respond.Seat);
                Assert.AreEqual(AnswerKind.Decline, respond.Default.Kind);
                Battles.Answer(s, ctx, respond, ChoiceAnswer.Of(accept ? AnswerKind.Accept : AnswerKind.Decline), log);
                if (accept)
                {
                    Assert.AreEqual(BattleKind.Trainer, s.Battle.Kind, "같은 칸 도전과 같은 트레이너전");
                    continue;
                }
                Assert.IsNull(s.Battle);
                var again = Battles.Only(s, ChoiceKind.MapAction);
                Assert.AreEqual(Me, again.Seat, "거절당하면 턴이 이어진다");
                Assert.IsFalse(Scene.Legal(s, ctx, again).Exists(x => x.Kind == AnswerKind.Challenge && x.Target == Them), "그 턴에 다시 도전 못함(G13)");
            }
        }

        [Test]
        public void A29_원격도전은보호중인상대와같은칸상대에게는없다()
        {
            var s = SameTile(3, null);
            Give(s, Me, "387:0");
            Give(s, 1, "393:0");
            Give(s, 2, "399:0");
            s.Seats[Me].Follower = SinnohGod.Dialga;
            s.Seats[1].Follower = s.Seats[2].Follower = SinnohGod.Palkia;
            s.Seats[2].Cell = Flow.StepInto(s, ctx, s.Seats[Me].Cell);
            s.Seats[2].Protected = true;
            var legal = Scene.Legal(s, ctx, Map(s));
            var to1 = legal.FindAll(x => x.Kind == AnswerKind.Challenge && x.Target == 1);
            Assert.AreEqual(1, to1.Count, "같은 칸이면 같은 칸 도전 하나");
            Assert.AreEqual(-1, to1[0].Arg);
            Assert.IsFalse(legal.Exists(x => x.Kind == AnswerKind.Challenge && x.Target == 2), "보호 중(p.9)");
        }

        [Test]
        public void A20_6_psychic_파랑_두칸떨어진좌석과도거래한다_능력을쓴다_결정84()
        {
            // 사용자 결정 84 T12 ② 나 — 원작 커뮤니티 확인(원작 커뮤니티 확인을 물은 말)
            var log = new EventLog();
            var s = Battles.Game(ctx, 2, 7);
            Give(s, Me, "387:0");
            Give(s, Them, "393:0");
            int x = Flow.StepInto(s, ctx, s.Seats[Me].Cell);
            int y = TrainerRules.ReachCells(ctx, x).Find(c => !MoveRules.Adjacent(ctx.Board, c, x) && !ctx.Board[c].DistortionWorld);
            s.Seats[Me].Cell = x;
            s.Seats[Them].Cell = y;
            s.Seats[Me].Jp = 3;
            Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = Me });
            var offer = TradeRules.Offer(Them, Me, Jp, -1, 3);
            Assert.IsFalse(TradeRules.OfferAllowed(s, ctx, Me, offer), "카드가 없으면 떨어진 좌석과는 못 한다");
            s.Seats[Me].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("psychic");
            s.Seats[Me].TrainerCardTier = TrainerRules.Silver;
            TrainerRules.Refresh(s, ctx);
            var partners = new List<int>();
            TradeRules.Partners(s, ctx, Me, partners);
            CollectionAssert.AreEqual(new[] { Them }, partners, "거래 단추가 켜진다");
            Assert.AreEqual(TradeRules.Telekinesis, TradeRules.ModeWith(s, ctx, Me, Them), "화면이 염력 거래를 가린다(값 단추 「거래」)");
            Scene.Accept(Propose(s, offer, log));
            var r = Respond(s);
            Assert.AreEqual(TradeRules.Telekinesis, r.Options[0]);
            Assert.AreEqual(1, log.OfType<AbilityUsed>().Count, "염력을 이 제안으로 쓴다(턴당 한 번)");
            Assert.IsTrue(Last<TradeProposed>(log).Remote);
            Reply(s, Accept(), log);
            Assert.IsNotNull(Last<TradeDone>(log));
            Assert.AreEqual(0, s.Seats[Me].Jp);
            Assert.AreEqual(3, s.Seats[Them].Jp);
            Assert.AreEqual(x, s.Seats[Me].Cell, "말은 움직이지 않는다");
            Assert.IsFalse(TradeRules.OfferAllowed(s, ctx, Me, TradeRules.Offer(Them, Them, Jp, -1, 1)), "턴당 한 번 — 이미 썼다");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility && a.Target >= 0 && a.Target != TrainerRules.WeatherTarget), "염력의 다른 상호작용도 이번 턴에는 없다");
        }
    }
}
