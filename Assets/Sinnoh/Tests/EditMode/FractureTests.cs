using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「천상의 균열」(룰북 p.18 · 카드 shrine_event.celestial_fracture, rulings A31 · U11): 추종자 비밀 선택(모든 좌석에게 한꺼번에) → 공개 → 집계(빠진 신 제외 · 동률은 뽑은 좌석) →
    /// 시작의 사당에 신 토큰(있던 신은 치움) → 사당 처리를 잇는다. 신 토큰이 있는 동안 시작의 사당 공물 불가(3막 심장 포함) · 그 신의 추종자 이동력 +1.
    /// 장면은 실제 판(Flow.Started)의 차례 좌석을 보통 사당에 세우고 사당 덱 맨 위에 균열 카드를 올린 뒤 공물을 바친다.
    /// </summary>
    public class FractureTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Card(string key) => ShrineRules.CardKey(ctx, key);

        int Fracture => Card(ShrineRules.CelestialFracture);

        int PlainShrine()
        {
            var fs = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fs.Count; f++)
                if (fs[f].Kind == FixtureKind.Shrine) return f;
            Assert.Fail("사당이 없다");
            return -1;
        }

        /// <summary>실제 판의 첫 맵 행동, 차례 좌석은 보통 사당 칸(이동력 3). 사당 덱 맨 위 = 균열(cards가 있으면 그 순서로 — 첫 장이 먼저).</summary>
        GameState At(int seats, ulong seed = 12, params int[] cards)
        {
            var s = Flow.Started(ctx, seats, seed);
            var st = s.Seats[s.ActiveSeat];
            st.Cell = ctx.Fixtures.Fixtures[PlainShrine()].Cell;
            st.MovesLeft = 3;
            if (cards.Length == 0) cards = new[] { Fracture };
            var pile = s.Decks[(int)DeckId.ShrineEvent];
            for (int i = cards.Length - 1; i >= 0; i--)
            {
                Assert.IsTrue(pile.Remove(cards[i]) || s.Discards[(int)DeckId.ShrineEvent].Remove(cards[i]), "그 카드가 덱에 없다");
                pile.Add(cards[i]);
            }
            return s;
        }

        void Offer(GameState s, EventLog log = null, bool extra = false)
        {
            var st = s.Seats[s.ActiveSeat];
            st.Apricorns[(int)ApricornColor.Red] = 1;
            if (extra) st.Jp = ShrineRules.ExtraCost;
            var a = ChoiceAnswer.Of(AnswerKind.Offer, PlainShrine(), (int)ApricornColor.Red);
            if (extra) a.Values.Add(ShrineRules.ExtraCost);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, a, log);
        }

        static List<PendingChoice> Group(GameState s) => s.Pending.FindAll(c => c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower);

        static ChoiceAnswer God(SinnohGod g) => ChoiceAnswer.Pick((int)g);

        static ChoiceAnswer None => ChoiceAnswer.Of(AnswerKind.Decline);

        /// <summary>추종자 묶음에 좌석마다 답한다(picks[좌석]).</summary>
        void Pick(GameState s, EventLog log, params ChoiceAnswer[] picks)
        {
            var g = Group(s);
            Assert.AreEqual(picks.Length, g.Count, "모든 좌석에게 묻는다");
            for (int i = 0; i < picks.Length; i++)
            {
                var c = g.Find(x => x.Seat == i);
                Scene.Accept(Engine.Submit(s, ctx, i, Command.AnswerTo(c.Id, picks[i]), log), $"좌석 {i} {picks[i]}");
            }
        }

        TokenState OriginToken(GameState s) => FractureRules.OriginToken(s, ctx);

        void MapActionFor(GameState s, int seat)
        {
            Assert.IsNull(s.Shrine, "사당 처리가 끝났다");
            Assert.AreEqual(1, s.Pending.Count, "열린 선택 하나");
            Assert.AreEqual(ChoiceKind.MapAction, s.Pending[0].Kind);
            Assert.AreEqual(seat, s.Pending[0].Seat, "턴은 이어진다");
        }

        // ── ① 비밀 선택 ──

        [Test]
        public void A31_1_균열은모든좌석에게추종자를한꺼번에묻고다답해야공개한다()
        {
            var s = At(3);
            int drawer = s.ActiveSeat;
            var log = new EventLog();
            Offer(s, log);
            var g = Group(s);
            Assert.AreEqual(3, g.Count, "모든 좌석");
            Assert.AreEqual(3, s.Pending.Count, "추종자 묶음만 열렸다(맵 행동은 아직)");
            foreach (var c in g)
            {
                Assert.IsTrue(c.Secret, "비밀");
                Assert.AreEqual(g[0].GroupId, c.GroupId, "한 묶음");
                Assert.AreEqual(new SourceRef(SourceKind.ShrineEvent, Fracture), c.Source);
                Assert.AreEqual(drawer, c.Subject, "Subject = 뽑은 좌석");
                Assert.AreEqual(ClockKind.Respond, c.Clock);
            }
            Assert.AreEqual(3, log.OfType<ChoiceRequested>().FindAll(e => e.Kind == ChoiceKind.Simultaneous).Count, "한꺼번에 묻는다");

            Scene.Accept(Engine.Submit(s, ctx, 2, Command.AnswerTo(g.Find(c => c.Seat == 2).Id, God(SinnohGod.Giratina)), log));
            Assert.AreEqual(SinnohGod.None, s.Seats[2].Follower, "다 답할 때까지 공개하지 않는다");
            Assert.AreEqual(0, log.OfType<SecretRevealed>().Count);
            Assert.AreEqual(0, log.OfType<ChoiceAnswered>().FindAll(e => g.Exists(c => c.Id == e.Id)).Count, "비밀 답은 SecretAnswered로만");
            var v0 = Engine.ViewFor(s, 0);
            Assert.AreEqual(Engine.Unknown, ChoiceRules.Find(v0, g.Find(c => c.Seat == 2).Id).Answer.Target, "남의 보기에서 답이 가려진다");
            Assert.AreEqual((int)SinnohGod.Giratina, ChoiceRules.Find(Engine.ViewFor(s, 2), g.Find(c => c.Seat == 2).Id).Answer.Target, "본인은 자기 답을 본다");

            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(g.Find(c => c.Seat == 0).Id, God(SinnohGod.Dialga)), log));
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(g.Find(c => c.Seat == 1).Id, God(SinnohGod.Giratina)), log));
            var rev = log.OfType<SecretRevealed>();
            Assert.AreEqual(1, rev.Count, "한 번에 공개");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, new[] { rev[0].Answers[0].Seat, rev[0].Answers[1].Seat, rev[0].Answers[2].Seat }, "좌석 순서");
            Assert.AreEqual(SinnohGod.Dialga, s.Seats[0].Follower);
            Assert.AreEqual(SinnohGod.Giratina, s.Seats[1].Follower);
            Assert.AreEqual(SinnohGod.Giratina, s.Seats[2].Follower);
            Assert.IsTrue(log.Events.IndexOf(rev[0]) < log.Events.IndexOf(log.OfType<FollowerTally>()[0]), "공개 → 집계");
            MapActionFor(s, drawer);
        }

        [Test]
        public void A31_1_처음에는안고를수있고한번따르면못돌아간다()
        {
            var s = At(2);
            Offer(s);
            var c = Group(s).Find(x => x.Seat == 0);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(4, legal.Count);
            Assert.IsTrue(legal[0].SameAs(None), "안 고름이 첫 답(원작 커뮤니티 확인)");
            Assert.IsTrue(c.Default.SameAs(None), "기본 = 안 고름(처음)");
            Pick(s, null, None, God(SinnohGod.Palkia));
            Assert.AreEqual(SinnohGod.None, s.Seats[0].Follower, "고르지 않은 좌석은 뒷면 그대로");

            // 다음 균열: 따르던 좌석에는 「안 고름」이 없고 기본은 지금 신
            var t = At(2);
            t.Seats[0].Follower = SinnohGod.Palkia;
            Offer(t);
            var d = Group(t).Find(x => x.Seat == 0);
            var l2 = Scene.Legal(t, ctx, d);
            Assert.AreEqual(3, l2.Count, "「once enthralled … there is no turning back」");
            Assert.IsFalse(l2.Exists(a => a.Kind == AnswerKind.Decline));
            Assert.IsTrue(d.Default.SameAs(God(SinnohGod.Palkia)), "기본 = 지금 추종자");
            Assert.IsTrue(Group(t).Find(x => x.Seat == 1).Default.SameAs(None));
        }

        [Test]
        public void A31_2_공개된추종자는계속보이고비밀선택동안만가린다()
        {
            var s = At(3);
            s.Seats[1].Follower = SinnohGod.Dialga;
            Assert.AreEqual(SinnohGod.Dialga, Engine.ViewFor(s, 0).Seats[1].Follower, "공개된 추종자는 남에게도 보인다");
            Offer(s);
            var v0 = Engine.ViewFor(s, 0);
            Assert.AreEqual((SinnohGod)Engine.Unknown, v0.Seats[1].Follower, "고르는 동안 뒷면");
            Assert.AreEqual((SinnohGod)Engine.Unknown, Engine.ViewFor(s, SeatId.Observer).Seats[1].Follower, "관전자도");
            Assert.AreEqual(SinnohGod.Dialga, Engine.ViewFor(s, 1).Seats[1].Follower, "본인은 안다");
            Assert.AreEqual(SinnohGod.Dialga, s.Seats[1].Follower, "원본 = 공개된 값(거래 고리가 읽는 값)");
            Pick(s, null, God(SinnohGod.Palkia), God(SinnohGod.Giratina), None);
            var after = Engine.ViewFor(s, 2);
            Assert.AreEqual(SinnohGod.Palkia, after.Seats[0].Follower, "공개 뒤 계속 보인다");
            Assert.AreEqual(SinnohGod.Giratina, after.Seats[1].Follower, "바꾼 신");
            Assert.AreEqual(SinnohGod.Giratina, Engine.ViewFor(s, 0).Seats[1].Follower);
        }

        [Test]
        public void A31_1_운명6장째를버린뒤에추종자를묻는다()
        {
            var s = At(2);
            int full = 1 - s.ActiveSeat;
            var deck = s.Decks[(int)DeckId.ChangeOfFate];
            while (s.Seats[full].FateHand.Count < FateRules.HandLimit)
            {
                s.Seats[full].FateHand.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
            Offer(s);
            var discard = s.Pending.Find(c => c.Kind == ChoiceKind.Discard);
            Assert.IsNotNull(discard, "6장째 — 먼저 버린다(A12)");
            Assert.AreEqual(0, Group(s).Count, "추종자 묶음은 미뤄 둔다");
            Assert.IsTrue(FractureRules.Choosing(s), "미뤄진 동안에도 균열 중");
            Scene.Accept(Engine.Submit(s, ctx, full, Command.AnswerTo(discard.Id, discard.Default.Clone()), null));
            Assert.AreEqual(2, Group(s).Count, "버린 뒤 모두에게");
            Pick(s, null, God(SinnohGod.Dialga), God(SinnohGod.Dialga));
            Assert.AreEqual(SinnohGod.Dialga, FractureRules.OriginGod(s, ctx));
            MapActionFor(s, s.ActiveSeat);
        }

        // ── ③④ 집계 · 강림 ──

        [Test]
        public void A31_3_가장많은신이시작의사당에내려온다()
        {
            var s = At(3);
            int drawer = s.ActiveSeat;
            var log = new EventLog();
            Offer(s, log);
            Assert.IsNull(OriginToken(s));
            Pick(s, log, God(SinnohGod.Palkia), God(SinnohGod.Dialga), God(SinnohGod.Palkia));
            var tally = log.OfType<FollowerTally>()[0];
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 }, tally.Counts, "[없음, 디아루가, 펄기아, 기라티나]");
            CollectionAssert.AreEqual(new[] { (int)SinnohGod.Palkia }, tally.Tied);
            Assert.AreEqual(-1, tally.TieBreakSeat);
            Assert.AreEqual(drawer, tally.Seat);
            var token = OriginToken(s);
            Assert.IsNotNull(token);
            Assert.AreEqual(FractureRules.GodCard(ctx, SinnohGod.Palkia), token.Def, "폼 1 카드");
            Assert.AreEqual(SinnohGod.Palkia, FractureRules.OriginGod(s, ctx));
            Assert.IsTrue(HosFlow.GodAtOrigin(s, ctx));
            var d = log.OfType<GodDescended>()[0];
            Assert.AreEqual((int)SinnohGod.Palkia, d.God);
            Assert.AreEqual(HosFlow.OriginShrineCell(ctx), d.Cell);
            Assert.AreEqual((int)SinnohGod.None, d.ReplacedGod);
            Assert.IsFalse(d.Stayed);
            Assert.AreEqual(new SourceRef(SourceKind.ShrineEvent, Fracture), d.Source);
            Assert.AreEqual(0, log.OfType<TokenRemoved>().Count);
            MapActionFor(s, drawer);
        }

        [Test]
        public void A31_3_동률이면뽑은좌석이고른다()
        {
            var s = At(3);
            int drawer = s.ActiveSeat;
            var log = new EventLog();
            Offer(s, log);
            var picks = new ChoiceAnswer[3];
            picks[drawer] = God(SinnohGod.Giratina);
            picks[(drawer + 1) % 3] = God(SinnohGod.Dialga);
            picks[(drawer + 2) % 3] = None;
            Pick(s, log, picks);
            var tally = log.OfType<FollowerTally>()[0];
            CollectionAssert.AreEqual(new[] { (int)SinnohGod.Dialga, (int)SinnohGod.Giratina }, tally.Tied, "신 순서");
            Assert.AreEqual(drawer, tally.TieBreakSeat);
            Assert.IsNull(OriginToken(s), "고를 때까지 놓지 않는다");
            var tie = s.Pending.Find(c => c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.FractureTie);
            Assert.IsNotNull(tie, "「you choose if tied」");
            Assert.AreEqual(drawer, tie.Seat, "뽑은 좌석");
            Assert.AreEqual(1, s.Pending.Count);
            var legal = Scene.Legal(s, ctx, tie);
            Assert.AreEqual(2, legal.Count, "동률인 신만");
            Assert.IsTrue(legal[0].SameAs(God(SinnohGod.Dialga)) && legal[1].SameAs(God(SinnohGod.Giratina)));
            Assert.IsTrue(tie.Default.SameAs(God(SinnohGod.Giratina)), "기본 = 뽑은 좌석의 추종자 [가정]");
            Assert.AreEqual(new SourceRef(SourceKind.ShrineEvent, Fracture), tie.Source);
            Scene.Accept(Engine.Submit(s, ctx, drawer, Command.AnswerTo(tie.Id, God(SinnohGod.Dialga)), log));
            Assert.AreEqual(SinnohGod.Dialga, FractureRules.OriginGod(s, ctx), "고른 신");
            Assert.AreEqual((int)SinnohGod.Dialga, log.OfType<GodDescended>()[0].God);
            MapActionFor(s, drawer);
        }

        [Test]
        public void A31_3_아무도따르지않으면신이내려오지않고_빠진신은세지않는다()
        {
            // 결정 76 ① R3 나: 모두 안 고름 → 아무 신도 내려오지 않는다(시작의 사당은 비어 있다 — 동률로 묻지 않는다)
            var s = At(2);
            var l0 = new EventLog();
            Offer(s, l0);
            Pick(s, l0, None, None);
            Assert.IsNull(s.Pending.Find(c => c.Sub == ChoiceSub.FractureTie), "동률로 묻지 않는다");
            var t0 = l0.OfType<FollowerTally>()[0];
            Assert.AreEqual(0, t0.Tied.Length, "추종자가 0인 신은 「가장 많은 신」이 아니다");
            Assert.AreEqual(-1, t0.TieBreakSeat);
            Assert.AreEqual(0, l0.OfType<GodDescended>().Count);
            Assert.IsNull(OriginToken(s), "시작의 사당은 비어 있다");
            MapActionFor(s, s.ActiveSeat);

            // 이미 내려온 신이 있으면 그대로 둔다(놓을 신이 없다 — 치우지도 않는다) [가정]
            var w = At(2);
            int palkia = FractureRules.GodCard(ctx, SinnohGod.Palkia);
            w.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = palkia, Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
            var lw = new EventLog();
            Offer(w, lw);
            Pick(w, lw, None, None);
            Assert.AreEqual(SinnohGod.Palkia, FractureRules.OriginGod(w, ctx));
            Assert.AreEqual(0, lw.OfType<TokenRemoved>().Count);
            Assert.AreEqual(0, lw.OfType<GodDescended>().Count);

            // 기라티나가 잡혔다(전설 덱에 없다): 기라티나를 따르던 둘은 그대로 · 디아루가 하나 → 디아루가(원작 커뮤니티 확인)
            var t = At(3);
            t.Seats[0].Follower = t.Seats[2].Follower = SinnohGod.Giratina;
            t.Decks[(int)DeckId.Legendary].Remove(FractureRules.GodCard(ctx, SinnohGod.Giratina));
            Assert.IsFalse(FractureRules.InGame(t, ctx, SinnohGod.Giratina));
            var log = new EventLog();
            Offer(t, log);
            Pick(t, log, None, God(SinnohGod.Dialga), None);
            CollectionAssert.AreEqual(new[] { (int)SinnohGod.Dialga }, log.OfType<FollowerTally>()[0].Tied);
            Assert.AreEqual(SinnohGod.Dialga, FractureRules.OriginGod(t, ctx));
            Assert.AreEqual(SinnohGod.Giratina, t.Seats[0].Follower, "따르던 신이 빠져도 토큰은 그대로(세지 않는다) [가정 plan §5 #95 ②]");

            // 셋 다 빠졌으면 놓지 않고 턴이 이어진다(고를 신이 없어 안 고름만)
            var u = At(2);
            foreach (var g in new[] { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina }) u.Decks[(int)DeckId.Legendary].Remove(FractureRules.GodCard(ctx, g));
            var lu = new EventLog();
            Offer(u, lu);
            Pick(u, lu, None, None);
            Assert.AreEqual(0, lu.OfType<FollowerTally>()[0].Tied.Length);
            Assert.AreEqual(0, lu.OfType<GodDescended>().Count);
            Assert.IsNull(OriginToken(u));
            MapActionFor(u, u.ActiveSeat);
        }

        [Test]
        public void A31_3_이미빠진신은추종자로고를수없다_따르던신이빠졌으면그대로둘수있다()
        {
            // 원작 커뮤니티 확인(빠진 신 = 잡히거나 쓰러져 폼 1 카드가 전설 덱에 없다)
            var s = At(2);
            s.Seats[1].Follower = SinnohGod.Palkia;
            s.Decks[(int)DeckId.Legendary].Remove(FractureRules.GodCard(ctx, SinnohGod.Giratina));
            s.Decks[(int)DeckId.Legendary].Remove(FractureRules.GodCard(ctx, SinnohGod.Palkia));
            Offer(s);
            var g = Group(s);
            var fresh = Scene.Legal(s, ctx, g.Find(c => c.Seat == 0));
            Assert.AreEqual(2, fresh.Count, "안 고름 · 디아루가");
            Assert.IsTrue(fresh[0].SameAs(None) && fresh[1].SameAs(God(SinnohGod.Dialga)));
            var gone = Scene.Legal(s, ctx, g.Find(c => c.Seat == 1));
            Assert.IsTrue(gone.Exists(a => a.SameAs(None)), "따르던 펄기아가 빠졌으면 그대로 둘 수 있다 [가정 plan §5 #95 ②]");
            Assert.IsFalse(gone.Exists(a => a.SameAs(God(SinnohGod.Palkia)) || a.SameAs(God(SinnohGod.Giratina))), "빠진 신은 고르지 못한다");
            Assert.IsTrue(g.Find(c => c.Seat == 1).Default.SameAs(None), "기본(시간 초과)도 그대로");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(g.Find(c => c.Seat == 0).Id, God(SinnohGod.Giratina)), null), RejectReason.Illegal, "빠진 기라티나");
        }

        [Test]
        public void A31_4_다른신이있으면바꿔놓고같은신이면그대로둔다()
        {
            var s = At(2);
            int cell = HosFlow.OriginShrineCell(ctx);
            int palkia = FractureRules.GodCard(ctx, SinnohGod.Palkia);
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = palkia, Cell = cell, FaceUp = true });
            var log = new EventLog();
            Offer(s, log);
            Pick(s, log, God(SinnohGod.Dialga), God(SinnohGod.Dialga));
            Assert.AreEqual(SinnohGod.Dialga, FractureRules.OriginGod(s, ctx), "「replaces any God of Sinnoh token already there」");
            var removed = log.OfType<TokenRemoved>();
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(TokenKind.God, removed[0].TokenKind);
            Assert.AreEqual(cell, removed[0].Cell);
            Assert.AreEqual((int)SinnohGod.Palkia, log.OfType<GodDescended>()[0].ReplacedGod);
            Assert.AreEqual(-1, s.Tokens.Find(t => t.Kind == TokenKind.God && t.Def == palkia).Cell, "치운 토큰은 맵 밖");
            Assert.IsTrue(FractureRules.InGame(s, ctx, SinnohGod.Palkia), "치운 신은 게임에 남는다(다음 균열에 다시 올 수 있다)");
            Assert.AreEqual(1, s.Tokens.FindAll(t => t.Kind == TokenKind.God && t.Cell == cell).Count, "시작의 사당에 신 토큰 하나");

            // 다음 균열에 다시 같은 신 → 그대로(토큰을 더 만들지 않는다)
            var again = At(2);
            again.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = FractureRules.GodCard(ctx, SinnohGod.Dialga), Cell = cell, FaceUp = true });
            var l2 = new EventLog();
            Offer(again, l2);
            Pick(again, l2, God(SinnohGod.Dialga), God(SinnohGod.Dialga));
            Assert.IsTrue(l2.OfType<GodDescended>()[0].Stayed, "같은 신이면 그대로");
            Assert.AreEqual(0, l2.OfType<TokenRemoved>().Count);
            int gods = again.Tokens.FindAll(t => t.Kind == TokenKind.God).Count;
            Assert.AreEqual(1, gods, "토큰을 더 만들지 않는다");
        }

        // M5 마감 검토: 균열이 바꿔 놓은 신의 TokenRemoved가 「신 격파」 80행(1800ms · 「한 번 더!」)으로 재생됐다 — 두 표(PlayTiming.RowOf · FxRows)가 똑같이 틀려 교차 시험이 못 잡았다
        [Test]
        public void A31_4_균열로신이바뀌어도신격파연출은없다()
        {
            var s = At(2);
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = FractureRules.GodCard(ctx, SinnohGod.Palkia), Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
            var log = new EventLog();
            Offer(s, log);
            Pick(s, log, God(SinnohGod.Dialga), God(SinnohGod.Dialga));
            var removed = log.OfType<TokenRemoved>();
            Assert.AreEqual(1, removed.Count, "시험 준비: 펄기아 토큰을 치웠다");
            Assert.AreEqual(0, Sinnoh.Rules.Session.PlayTiming.RowOf(removed[0]), "방장 표: 행 없음");
            Assert.AreEqual(Sinnoh.Fx.FxRows.None, Sinnoh.Fx.FxRows.Of(removed[0]), "화면 표: 행 없음");
            bool descended = false;
            foreach (var entry in new Sinnoh.Fx.FxPlanner(0, 2).Plan(log.Events))
                foreach (var step in entry.Steps)
                {
                    Assert.AreNotEqual(Sinnoh.Fx.ResultRows.GodGone, step.Row, "균열 갱신에는 신 격파 단계가 없다");
                    descended |= step.Row == Sinnoh.Fx.ShrineRows.Descend;
                }
            Assert.IsTrue(descended, "강림 단계는 그대로");

            // 배틀로 치운 신(출처가 균열 카드가 아니다)은 그대로 80행
            var beaten = new TokenRemoved { Seat = 0, Cell = HosFlow.OriginShrineCell(ctx), TokenKind = TokenKind.God };
            Assert.AreEqual(Sinnoh.Fx.ResultRows.GodGone, Sinnoh.Rules.Session.PlayTiming.RowOf(beaten));
            Assert.AreEqual(Sinnoh.Fx.ResultRows.GodGone, Sinnoh.Fx.FxRows.Of(beaten));
        }

        [Test]
        public void A31_4_세장뽑기에서는균열을처리한뒤다음카드를고른다()
        {
            int calm = Card(ShrineRules.TranquilMoment), treasure = Card(ShrineRules.HiddenTreasure);
            var s = At(2, 12, Fracture, calm, treasure);
            int drawer = s.ActiveSeat;
            Offer(s, null, true);
            var pick = s.Pending.Find(c => c.Sub == ChoiceSub.ShrinePick);
            Scene.Accept(Engine.Submit(s, ctx, drawer, Command.AnswerTo(pick.Id, ChoiceAnswer.Pick(Fracture)), null));
            Pick(s, null, God(SinnohGod.Giratina), God(SinnohGod.Giratina));
            Assert.AreEqual(SinnohGod.Giratina, FractureRules.OriginGod(s, ctx));
            var next = s.Pending.Find(c => c.Sub == ChoiceSub.ShrinePick);
            Assert.IsNotNull(next, "균열 뒤 둘째 장을 고른다(ShrineRules.Resume)");
            Assert.AreEqual(2, Scene.Legal(s, ctx, next).Count);
            int jp = s.Seats[drawer].Jp;
            Scene.Accept(Engine.Submit(s, ctx, drawer, Command.AnswerTo(next.Id, ChoiceAnswer.Pick(calm)), null));
            Assert.AreEqual(jp + 2, s.Seats[drawer].Jp, "평온한 순간 1단계");
            MapActionFor(s, drawer);
        }

        // ── ⑤ 신 토큰이 있는 동안 ──

        [Test]
        public void A31_5_신토큰이시작의사당에있으면공물을못바치고_3막심장도못되찾는다()
        {
            var s = At(2);
            int drawer = s.ActiveSeat;
            Offer(s);
            Pick(s, null, God(SinnohGod.Dialga), God(SinnohGod.Dialga));
            var st = s.Seats[drawer];
            st.Cell = HosFlow.OriginShrineCell(ctx);
            st.MovesLeft = 3;
            st.Apricorns[(int)ApricornColor.Blue] = 1;
            s.Used.RemoveAll(u => u.Seat == drawer && u.Kind == UsageKind.ShrineOffer); // 턴당 1번은 따로 본다(S12_1)
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(Flow.Has(legal, AnswerKind.Offer), "「You cannot make a Shrine Offering at the Origin Shrine whilst it is there」");
            Assert.IsTrue(legal.Exists(a => a.Kind == AnswerKind.Search && a.Arg == HosFlow.OriginShrine(ctx)), "그 칸 찾아보기로 신 배틀(A28 ①)");

            // 3막: 조각 셋이어도 심장을 되찾지 못한다(p.18 「this is relevant for Act III」)
            s.Hos.Act = 3;
            st.ActProgress = 2;
            st.Heart = false;
            Assert.IsFalse(HosFlow.CanRestoreHeart(s, ctx, drawer));
            OriginToken(s).Cell = -1;
            Assert.IsTrue(HosFlow.CanRestoreHeart(s, ctx, drawer), "토큰이 없으면 된다");
        }

        [Test]
        public void A31_6_시작의사당신의추종자는턴마다이동력1을더받는다()
        {
            var s = At(2);
            int drawer = s.ActiveSeat;
            int other = 1 - drawer;
            Offer(s);
            var picks = new ChoiceAnswer[2];
            picks[drawer] = None;
            picks[other] = God(SinnohGod.Palkia);
            Pick(s, null, picks);
            Assert.AreEqual(SinnohGod.Palkia, FractureRules.OriginGod(s, ctx));
            Assert.AreEqual(3, s.Seats[drawer].MovesLeft, "턴 가운데 내려와도 이번 턴 이동력은 그대로 [가정]");

            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            var set = log.OfType<MoveAllowanceSet>()[0];
            Assert.AreEqual(other, set.Seat);
            Assert.IsTrue(System.Array.Exists(set.Mods, m => m.Source.Same(SourceRef.Rule(RuleSource.GodFollower)) && m.Delta == FractureRules.FollowerMoves), "「All of their Followers gain 1 extra movement per turn」");
            Assert.AreEqual(MoveRules.BaseMovement + 1, set.Total);
            Assert.AreEqual(MoveRules.BaseMovement + 1, s.Seats[other].MovesLeft);

            var l2 = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), l2);
            var mine = l2.OfType<MoveAllowanceSet>()[0];
            Assert.AreEqual(drawer, mine.Seat);
            Assert.IsFalse(System.Array.Exists(mine.Mods, m => m.Source.Same(SourceRef.Rule(RuleSource.GodFollower))), "추종자가 아니면 없다");
            Assert.AreEqual(MoveRules.BaseMovement, mine.Total);
        }

        // ── 보기 · 시간 초과 ──

        [Test]
        public void M5_균열선택은보기에서도같은합법답이고시간초과로끝난다()
        {
            var s = At(4);
            int drawer = s.ActiveSeat;
            s.Seats[(drawer + 1) % 4].Follower = SinnohGod.Giratina;
            Offer(s);
            for (int guard = 0; s.Shrine != null; guard++)
            {
                Assert.Less(guard, 20, "끝나지 않는다");
                var c = s.Pending.Find(p => !p.Answered);
                var view = Engine.ViewFor(s, c.Seat);
                var host = Scene.Legal(s, ctx, c);
                var seen = Scene.Legal(view, ctx, view.Pending.Find(p => p.Id == c.Id));
                Assert.AreEqual(host.Count, seen.Count, $"{c.Kind}/{c.Sub}: 보기의 합법 답");
                for (int i = 0; i < host.Count; i++) Assert.IsTrue(host[i].SameAs(seen[i]), $"{c.Sub} {i}");
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), null), c.Sub.ToString());
            }
            Assert.AreEqual(SinnohGod.Giratina, s.Seats[(drawer + 1) % 4].Follower, "시간 초과 = 지금 추종자");
            Assert.AreEqual(SinnohGod.Giratina, FractureRules.OriginGod(s, ctx), "기라티나 하나 · 나머지는 안 고름");
            MapActionFor(s, drawer);
        }
    }
}
