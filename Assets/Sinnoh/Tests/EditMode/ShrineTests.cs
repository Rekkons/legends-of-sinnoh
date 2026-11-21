using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「사당 공물·카드」: 공물(룰북 p.17, rules-summary §12.1) · 사당 이벤트 카드 35장(§12.2~12.4, shrine_event.json) · 4인 이상 공유(rulings A19) — ShrineRules.
    /// 장면은 실제 판(Flow.Started)의 차례 좌석을 사당 칸에 세우고, 사당 덱 맨 위에 원하는 카드를 올린 뒤 공물을 바친다.
    /// </summary>
    public class ShrineTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Card(string key) => ShrineRules.CardKey(ctx, key);

        int Fixture(string key) => ctx.Fixtures.Ids.IndexOf(key);

        /// <summary>보통 사당(고정물 데이터 첫 Shrine) 고정물 색인.</summary>
        int PlainShrine()
        {
            var fs = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fs.Count; f++)
                if (fs[f].Kind == FixtureKind.Shrine) return f;
            Assert.Fail("사당이 없다");
            return -1;
        }

        /// <summary>실제 판의 첫 맵 행동. 차례 좌석을 그 사당 칸에 세운다(이동력 3).</summary>
        GameState At(int seats, int shrine, ulong seed = 12)
        {
            var s = Flow.Started(ctx, seats, seed);
            Stand(s, shrine);
            return s;
        }

        void Stand(GameState s, int shrine)
        {
            var st = s.Seats[s.ActiveSeat];
            st.Cell = ctx.Fixtures.Fixtures[shrine].Cell;
            st.MovesLeft = 3;
        }

        /// <summary>사당 덱 맨 위에 그 카드들을 올린다(마지막이 맨 위 — 첫 장이 먼저 뽑힌다).</summary>
        static void Top(GameState s, params int[] cards)
        {
            var pile = s.Decks[(int)DeckId.ShrineEvent];
            for (int i = cards.Length - 1; i >= 0; i--)
            {
                Assert.IsTrue(pile.Remove(cards[i]) || s.Discards[(int)DeckId.ShrineEvent].Remove(cards[i]), "그 카드가 덱에 없다");
                pile.Add(cards[i]);
            }
        }

        static ChoiceAnswer OfferAnswer(int shrine, ApricornColor color, bool extra = false)
        {
            var a = ChoiceAnswer.Of(AnswerKind.Offer, shrine, (int)color);
            if (extra) a.Values.Add(ShrineRules.ExtraCost);
            return a;
        }

        void Offer(GameState s, int shrine, ApricornColor color, EventLog log = null, bool extra = false)
        {
            var st = s.Seats[s.ActiveSeat];
            if (st.Apricorns[(int)color] == 0) st.Apricorns[(int)color] = 1;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, OfferAnswer(shrine, color, extra), log);
        }

        static PendingChoice Open(GameState s, ChoiceSub sub)
        {
            var c = s.Pending.Find(p => !p.Answered && p.Kind == ChoiceKind.Reward && p.Sub == sub);
            Assert.IsNotNull(c, $"{sub} 선택이 열리지 않았다");
            return c;
        }

        void Reply(GameState s, ChoiceSub sub, ChoiceAnswer a, EventLog log = null)
        {
            var c = Open(s, sub);
            Assert.IsTrue(ChoiceRules.Contains(Scene.Legal(s, ctx, c), c.Default), $"{sub}: Default ∈ Legal");
            Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, a), log), $"{sub} {a}");
        }

        /// <summary>사당 처리 중 남은 선택(천상의 균열 추종자 · 동률)을 시간 초과로 끝낸다.</summary>
        void Settle(GameState s)
        {
            for (int guard = 0; s.Shrine != null; guard++)
            {
                Assert.Less(guard, 20, "사당 처리가 끝나지 않는다");
                var c = s.Pending.Find(p => !p.Answered);
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), null), c.Sub.ToString());
            }
        }

        void MapActionFor(GameState s, int seat)
        {
            Assert.IsNull(s.Shrine, "사당 처리가 끝났다");
            Assert.AreEqual(1, s.Pending.Count, "열린 선택 하나");
            Assert.AreEqual(ChoiceKind.MapAction, s.Pending[0].Kind);
            Assert.AreEqual(seat, s.Pending[0].Seat, "턴은 이어진다");
        }

        // ── 공물(§12.1) ──

        [Test]
        public void S12_1_공물은사당칸에서규토리색마다_단계는등급_턴당1번()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            for (int c = 0; c < SeatState.ApricornColors; c++) st.Apricorns[c] = 0;
            st.Jp = 0;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "바칠 규토리가 없다");
            st.Apricorns[(int)ApricornColor.Red] = 1;
            st.Apricorns[(int)ApricornColor.White] = 1;
            st.Apricorns[(int)ApricornColor.Black] = 1;
            var offers = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Offer);
            CollectionAssert.AreEqual(new[] { (int)ApricornColor.Red, (int)ApricornColor.White, (int)ApricornColor.Black }, offers.ConvertAll(a => a.Arg), "가진 색마다(색 순서)");
            Assert.IsTrue(offers.TrueForAll(a => a.Target == shrine && a.Values.Count == 0), "JP가 7 미만이면 3장 뽑기가 없다");

            st.MovesLeft = 0;
            s.Options.InteractAtZeroMovement = false;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "상호작용할 수 없다(G11 옵션 — 이동력 0)");
            s.Options.InteractAtZeroMovement = true;
            st.MovesLeft = 3;

            foreach (var (color, tier) in new[] { (ApricornColor.Black, 3), (ApricornColor.White, 2), (ApricornColor.Red, 1) })
            {
                var log = new EventLog();
                var t = Flow.Started(ctx, 2, 12);
                Stand(t, shrine);
                Top(t, Card(ShrineRules.TranquilMoment));
                Offer(t, shrine, color, log);
                var e = log.OfType<ShrineOffered>()[0];
                Assert.AreEqual(tier, e.Tier, color.ToString());
                Assert.AreEqual(shrine, e.ShrineKey);
                Assert.AreEqual(new SourceRef(SourceKind.Shrine, shrine), e.Source);
                Assert.AreEqual(ResKind.Apricorn, e.Changes[0].Res);
                Assert.AreEqual((int)color, e.Changes[0].Key);
                CollectionAssert.AreEqual(new[] { Card(ShrineRules.TranquilMoment) }, log.OfType<ShrineCardsDrawn>()[0].CardKeys);
                Assert.AreEqual(2 * tier, log.OfType<ShrineBlessing>()[0].Changes[0].After - log.OfType<ShrineBlessing>()[0].Changes[0].Before, "평온한 순간 2/4/6");
                MapActionFor(t, t.ActiveSeat);
                CollectionAssert.Contains(t.Discards[(int)DeckId.ShrineEvent], Card(ShrineRules.TranquilMoment), "처리한 카드는 버림 더미로");

                // 턴당 1번(p.17 「Once per turn」) — 다른 사당에서도
                t.Seats[t.ActiveSeat].Apricorns[0] = 2;
                Assert.IsFalse(Flow.Has(Flow.MapLegal(t, ctx), AnswerKind.Offer), "같은 턴 같은 사당");
                var other = ctx.Fixtures.Fixtures.Count;
                for (int f = shrine + 1; f < ctx.Fixtures.Fixtures.Count; f++)
                    if (ctx.Fixtures.Fixtures[f].Kind == FixtureKind.Shrine) { other = f; break; }
                Stand(t, other);
                Assert.IsFalse(Flow.Has(Flow.MapLegal(t, ctx), AnswerKind.Offer), "같은 턴 다른 사당");
            }
        }

        [Test]
        public void S12_1_다음턴에는다시바칠수있다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            Top(s, Card(ShrineRules.TranquilMoment));
            Offer(s, shrine, ApricornColor.Red);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn));
            Assert.AreEqual(seat, s.ActiveSeat);
            s.Seats[seat].Apricorns[0] = 1;
            Stand(s, shrine);
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "다음 턴(원작 커뮤니티 확인)");
        }

        [Test]
        public void S12_1_전설토큰이있는사당과신토큰이있는시작의사당에는바칠수없다()
        {
            int frost = Fixture("legendary_shrine.frostsong_shrine");
            var s = At(2, frost);
            s.Seats[s.ActiveSeat].Apricorns[0] = 1;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "전설 사당도 사당이다");
            s.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = Scene.Def(ctx, "144:0"), Cell = ctx.Fixtures.Fixtures[frost].Cell, FaceUp = true });
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "「cannot make a Shrine Offering at their Shrine whilst the token is there」");

            int origin = HosFlow.OriginShrine(ctx);
            Stand(s, origin);
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "시작의 사당도 보통 공물을 받는다");
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = Scene.Def(ctx, "483:1"), Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "신 토큰이 있는 동안(p.18)");
        }

        [Test]
        public void S12_1_JP7을더내면3장뽑아한장씩골라처리하고남은한장은버린다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            st.Jp = 9;
            st.Apricorns[(int)ApricornColor.Pink] = 1;
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(legal.Exists(a => a.SameAs(OfferAnswer(shrine, ApricornColor.Pink, true))), "JP 7 이상이면 3장 뽑기");
            int calm = Card(ShrineRules.TranquilMoment), treasure = Card(ShrineRules.HiddenTreasure), fate = Card(ShrineRules.FatefulEncounter);
            Top(s, calm, treasure, fate);
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Pink, log, true);
            Assert.AreEqual(2, st.Jp, "JP 7을 낸다");
            var offered = log.OfType<ShrineOffered>()[0];
            Assert.IsTrue(offered.Extra);
            Assert.AreEqual(2, offered.Changes.Length, "규토리 + JP");
            CollectionAssert.AreEqual(new[] { calm, treasure, fate }, log.OfType<ShrineCardsDrawn>()[0].CardKeys);

            var pick = Open(s, ChoiceSub.ShrinePick);
            CollectionAssert.AreEqual(new[] { calm, treasure, fate }, Scene.Legal(s, ctx, pick).ConvertAll(a => a.Target), "뽑은 셋 중 하나");
            Reply(s, ChoiceSub.ShrinePick, ChoiceAnswer.Pick(calm));
            Assert.AreEqual(6, st.Jp, "한 장을 처리한 뒤에 다음 장을 고른다(원작 커뮤니티 확인) — 단계 2 = JP 4");
            pick = Open(s, ChoiceSub.ShrinePick);
            CollectionAssert.AreEqual(new[] { treasure, fate }, Scene.Legal(s, ctx, pick).ConvertAll(a => a.Target), "남은 둘");
            int greater = st.Balls[(int)Ball.Ultimate];
            Reply(s, ChoiceSub.ShrinePick, ChoiceAnswer.Pick(treasure));
            Assert.AreEqual(greater + 1, st.Balls[(int)Ball.Ultimate], "숨겨진 보물 단계 2 = 하이퍼볼");
            MapActionFor(s, seat);
            var discards = s.Discards[(int)DeckId.ShrineEvent];
            foreach (int c in new[] { calm, treasure, fate }) CollectionAssert.Contains(discards, c, "고르지 않은 카드도 버린다");
            Assert.IsFalse(log.OfType<FateDealt>().Exists(d => d.Source.Kind == SourceKind.ShrineEvent), "고르지 않은 운명적 조우는 처리하지 않는다");
        }

        [Test]
        public void S12_1_덱이바닥나면버림더미를섞어다시뽑는다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            var deck = s.Decks[(int)DeckId.ShrineEvent];
            var discards = s.Discards[(int)DeckId.ShrineEvent];
            int calm = Card(ShrineRules.TranquilMoment);
            discards.AddRange(deck);
            deck.Clear();
            discards.RemoveAll(c => c != calm);
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Red, log);
            Assert.IsTrue(log.OfType<PileShuffled>().Exists(p => p.Pile == DeckId.ShrineEvent));
            CollectionAssert.AreEqual(new[] { calm }, log.OfType<ShrineCardsDrawn>()[0].CardKeys);
        }

        [Test]
        public void S12_1_3장뽑기에서덱이바닥나도방금뽑은카드는다시섞지않는다()
        {
            // p.17 「draw 3 cards in total and choose 2 of them」 · 「If the deck is out of cards, shuffle the discard pile back into the deck」: 버림 더미는 그 전에 버린 카드다 — 손 앞의 카드는 들지 않는다
            int shrine = PlainShrine();
            var s = At(2, shrine);
            var deck = s.Decks[(int)DeckId.ShrineEvent];
            var discards = s.Discards[(int)DeckId.ShrineEvent];
            int calm = Card(ShrineRules.TranquilMoment);
            deck.Clear();
            discards.Clear();
            deck.Add(calm);
            s.Seats[s.ActiveSeat].Jp = ShrineRules.ExtraCost + 2;
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Red, log, extra: true);
            CollectionAssert.AreEqual(new[] { calm }, log.OfType<ShrineCardsDrawn>()[0].CardKeys, "한 장뿐인 카드는 한 번만 뽑힌다");
            Settle(s);
            CollectionAssert.AreEqual(new[] { calm }, discards, "처리 뒤 버림 더미에 한 장");
        }

        [Test]
        public void S16_2_3막시작의사당공물은심장을회복하고카드도뽑으며턴당1번을함께센다()
        {
            var s = Battles.Game(ctx, 2, 7);
            var fx = Scene.Fx(s, null);
            while (s.Hos.Act < 3) SetupFlow.RevealAct(s, ctx, fx, -1);
            var st = s.Seats[0];
            st.ActProgress = 2;
            for (int i = 0; i < SeatState.ShardCount; i++) st.Shards[i] = true;
            st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, SeatState.ShardCount);
            Scene.Give(s, ctx, 0, "387:0");
            st.Cell = HosFlow.OriginShrineCell(ctx);
            st.MovesLeft = 1;
            st.Apricorns[(int)ApricornColor.Black] = 1;
            st.Jp = 7;
            s.Pending.Clear();
            TurnFlow.OpenMapAction(s, ctx, Scene.Fx(s, null, 0, ActionKind.TurnStart), 0);
            int calm = Card(ShrineRules.TranquilMoment);
            Top(s, calm, Card(ShrineRules.HiddenTreasure), Card(ShrineRules.FatefulEncounter));
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, OfferAnswer(HosFlow.OriginShrine(ctx), ApricornColor.Black, true), log);
            Assert.IsTrue(st.Heart, "심장 회복");
            Assert.AreEqual(1, log.OfType<HeartRestored>().Count);
            Assert.AreEqual(1, log.OfType<ApricornSpent>().Count, "규토리는 심장 공물이 한 번 낸다");
            var offered = log.OfType<ShrineOffered>()[0];
            Assert.AreEqual(3, offered.Tier);
            Assert.AreEqual(ResKind.Jp, offered.Changes[0].Res, "ShrineOffered는 JP만");
            Reply(s, ChoiceSub.ShrinePick, ChoiceAnswer.Pick(calm));
            Assert.AreEqual(6, st.Jp, "검정 = 단계 3 → JP 6");
        }

        // ── 사당의 축복(§12.2) · 4인 이상(A19) ──

        [Test]
        public void A19_1_4인이상이면축복을다른트레이너하나와나누고_바친사람이먼저()
        {
            int shrine = PlainShrine();
            var s = At(4, shrine);
            int seat = s.ActiveSeat;
            Top(s, Card(ShrineRules.TranquilMoment));
            var jp = new int[4];
            for (int i = 0; i < 4; i++) jp[i] = s.Seats[i].Jp;
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Green, log);
            Assert.AreEqual(jp[seat] + 4, s.Seats[seat].Jp, "「You always get to apply the effect of the event first」");
            var share = Open(s, ChoiceSub.SharedBlessing);
            Assert.AreEqual(seat, share.Seat);
            var legal = Scene.Legal(s, ctx, share);
            CollectionAssert.AreEqual(new[] { (seat + 1) % 4, (seat + 2) % 4, (seat + 3) % 4 }, legal.ConvertAll(a => a.Target), "다른 트레이너(다음부터 시계 방향)");
            int to = (seat + 2) % 4;
            log.Clear();
            Reply(s, ChoiceSub.SharedBlessing, ChoiceAnswer.Pick(to), log);
            Assert.AreEqual(jp[to] + 4, s.Seats[to].Jp, "같은 단계");
            var b = log.OfType<ShrineBlessing>()[0];
            Assert.AreEqual(to, b.Seat);
            Assert.AreEqual(seat, b.From);
            Assert.AreEqual(jp[(seat + 1) % 4], s.Seats[(seat + 1) % 4].Jp, "한 명만");
            MapActionFor(s, seat);
        }

        [Test]
        public void A19_2_3인이하는나누지않는다()
        {
            int shrine = PlainShrine();
            foreach (int seats in new[] { 2, 3 })
            {
                var s = At(seats, shrine);
                Top(s, Card(ShrineRules.TranquilMoment));
                Offer(s, shrine, ApricornColor.Red);
                MapActionFor(s, s.ActiveSeat);
            }
        }

        [Test]
        public void A19_3_전설이벤트와천상의균열은나누지않는다()
        {
            int shrine = PlainShrine();
            foreach (string key in new[] { "shrine_event.song_of_frost", ShrineRules.CelestialFracture })
            {
                var s = At(4, shrine);
                Top(s, Card(key));
                Offer(s, shrine, ApricornColor.Red);
                Assert.IsFalse(s.Pending.Exists(p => p.Sub == ChoiceSub.SharedBlessing), key);
                Settle(s); // 균열의 추종자 고르기(FractureTests)는 시간 초과로
                Assert.IsFalse(s.Pending.Exists(p => p.Sub == ChoiceSub.SharedBlessing), key);
                MapActionFor(s, s.ActiveSeat);
            }
        }

        [Test]
        public void A19_4_나눠받은사람이자기선택을고른다_카드마다따로나눈다()
        {
            int shrine = PlainShrine();
            var s = At(4, shrine);
            int seat = s.ActiveSeat;
            s.Seats[seat].Jp = 7;
            int gift = Card(ShrineRules.UnexpectedGift), calm = Card(ShrineRules.TranquilMoment);
            Top(s, gift, calm, Card(ShrineRules.HiddenTreasure));
            Offer(s, shrine, ApricornColor.Red, null, true);
            Reply(s, ChoiceSub.ShrinePick, ChoiceAnswer.Pick(gift));
            Assert.AreEqual(seat, Open(s, ChoiceSub.ShrineApricorns).Seat, "바친 사람이 먼저 고른다");
            int blackBefore = s.Seats[seat].Apricorns[(int)ApricornColor.Black];
            Reply(s, ChoiceSub.ShrineApricorns, ChoiceAnswer.PickMany(new List<int> { (int)ApricornColor.Black }));
            Assert.AreEqual(blackBefore + 1, s.Seats[seat].Apricorns[(int)ApricornColor.Black], "단계 1 = 1개");
            int to = (seat + 1) % 4;
            Reply(s, ChoiceSub.SharedBlessing, ChoiceAnswer.Pick(to));
            var theirs = Open(s, ChoiceSub.ShrineApricorns);
            Assert.AreEqual(to, theirs.Seat, "받은 사람이 자기 색을 고른다(결정 75 ②)");
            int greenBefore = s.Seats[to].Apricorns[(int)ApricornColor.Green];
            Reply(s, ChoiceSub.ShrineApricorns, ChoiceAnswer.PickMany(new List<int> { (int)ApricornColor.Green }));
            Assert.AreEqual(greenBefore + 1, s.Seats[to].Apricorns[(int)ApricornColor.Green]);
            // 둘째 카드도 따로 나눈다
            Reply(s, ChoiceSub.ShrinePick, ChoiceAnswer.Pick(calm));
            var share = Open(s, ChoiceSub.SharedBlessing);
            int other = (seat + 3) % 4, jp = s.Seats[other].Jp;
            Reply(s, ChoiceSub.SharedBlessing, ChoiceAnswer.Pick(other));
            Assert.AreEqual(jp + 2, s.Seats[other].Jp);
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_운명적조우는운명의변화1_2_3장()
        {
            int shrine = PlainShrine();
            foreach (var (color, n) in new[] { (ApricornColor.Blue, 1), (ApricornColor.Pink, 2), (ApricornColor.Black, 3) })
            {
                var s = At(2, shrine);
                var hand = s.Seats[s.ActiveSeat].FateHand;
                hand.Clear();
                Top(s, Card(ShrineRules.FatefulEncounter));
                var log = new EventLog();
                Offer(s, shrine, color, log);
                Assert.AreEqual(n, hand.Count, color.ToString());
                Assert.AreEqual(new SourceRef(SourceKind.ShrineEvent, Card(ShrineRules.FatefulEncounter)), log.OfType<FateDealt>()[0].Source);
                MapActionFor(s, s.ActiveSeat);
            }
        }

        [Test]
        public void S12_2_뜻밖의선물은아무색1_2_3개_같은색도된다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            Top(s, Card(ShrineRules.UnexpectedGift));
            Offer(s, shrine, ApricornColor.Black);
            var c = Open(s, ChoiceSub.ShrineApricorns);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(84, legal.Count, "7색 중복 조합 3개");
            Assert.IsTrue(legal[0].SameAs(ChoiceAnswer.PickMany(new List<int> { 0, 0, 0 })), "첫 답 = 빨강 셋(G9)");
            int black = s.Seats[seat].Apricorns[(int)ApricornColor.Black];
            var log = new EventLog();
            Reply(s, ChoiceSub.ShrineApricorns, ChoiceAnswer.PickMany(new List<int> { 6, 6, 6 }), log);
            Assert.AreEqual(black + 3, s.Seats[seat].Apricorns[(int)ApricornColor.Black], "검정 셋도 된다(「any Apricorn」)");
            Assert.AreEqual(SourceKind.ShrineEvent, log.OfType<ApricornsGained>()[0].Source.Kind);
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_동등한교환은단계이하포켓몬을같은등급야생카드로_붙은것은그대로_옛카드는꺼낸더미로()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int mod = Scene.Give(s, ctx, seat, "388:0"); // 적당한(그로우)
            int weak = st.Party[0];                       // 스타터(약한으로 센다)
            var p = s.Pokemon[weak];
            p.Moves[0] = Scene.Move(ctx, "bite");
            p.HeldItem = Scene.Item(ctx, "item.choice_band");
            p.Damage = 1;
            p.Status = StatusEffect.Burned;
            Top(s, Card(ShrineRules.EquivalentExchange));
            Offer(s, shrine, ApricornColor.Red);
            var pick = Open(s, ChoiceSub.ShrineExchange);
            var legal = Scene.Legal(s, ctx, pick);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "「You may」");
            Assert.IsTrue(legal.Exists(a => a.Target == weak), "약한(스타터) 포켓몬");
            Assert.IsFalse(legal.Exists(a => a.Target == mod), "단계 1은 약한까지");
            Reply(s, ChoiceSub.ShrineExchange, ChoiceAnswer.Pick(weak));
            var cards = Open(s, ChoiceSub.ShrineExchangeCard);
            var options = Scene.Legal(s, ctx, cards);
            Assert.AreEqual(AnswerKind.Decline, options[0].Kind);
            var fromDeck = options.Find(a => a.Kind == AnswerKind.Pick && a.Arg == 0);
            Assert.IsNotNull(fromDeck, "약한 덱에서 찾는다");
            foreach (var a in options)
                if (a.Kind == AnswerKind.Pick) Assert.AreEqual(EncounterTier.Weak, MapActions.GradeOf(ctx, a.Target), "같은 등급");
            // 덱 안을 찾는 것은 고르는 좌석뿐(01 §4 — 다른 좌석의 보기에서 후보를 가린다), 고르는 좌석의 보기는 같은 답
            var otherView = Engine.ViewFor(s, 1 - seat).Pending.Find(c => c.Sub == ChoiceSub.ShrineExchangeCard);
            Assert.IsTrue(otherView.Options.TrueForAll(x => x == Engine.Unknown), "남의 보기에서는 조우 덱 내용이 보이지 않는다");
            var ownView = Engine.ViewFor(s, seat);
            Assert.AreEqual(options.Count, Scene.Legal(ownView, ctx, ownView.Pending.Find(c => c.Sub == ChoiceSub.ShrineExchangeCard)).Count, "고르는 좌석의 보기");
            int old = p.Def;
            int weakDeck = s.Decks[(int)DeckId.Weak].Count;
            var log = new EventLog();
            Reply(s, ChoiceSub.ShrineExchangeCard, fromDeck, log);
            Assert.AreEqual(fromDeck.Target, p.Def);
            Assert.AreEqual((int)DeckId.Weak, p.Deck);
            Assert.AreEqual(Scene.Move(ctx, "bite"), p.Moves[0], "기술은 그대로(배울 수 없는 타입이어도, p.19)");
            Assert.AreEqual(Scene.Item(ctx, "item.choice_band"), p.HeldItem, "지닌물건");
            Assert.AreEqual(1, p.Damage, "피해");
            Assert.AreEqual(StatusEffect.Burned, p.Status, "상태");
            Assert.AreEqual(weakDeck, s.Decks[(int)DeckId.Weak].Count, "새 카드를 빼고 옛 카드를 넣는다");
            CollectionAssert.Contains(s.Decks[(int)DeckId.Weak], old, "옛 카드는 꺼낸 쪽(덱)으로(원작 커뮤니티 확인)");
            var e = log.OfType<PokemonExchanged>()[0];
            Assert.AreEqual(old, e.From);
            Assert.IsFalse(e.FromDiscard);
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_동등한교환은버림더미에서도찾고_우두머리토큰은그대로()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            int alpha = Scene.Give(s, ctx, seat, "445:0"); // 한카리아스(강한)
            var p = s.Pokemon[alpha];
            p.Alpha = true;
            p.Deck = (int)DeckId.Strong;
            int strong = s.Decks[(int)DeckId.Strong][0];
            s.Decks[(int)DeckId.Strong].RemoveAt(0);
            s.Discards[(int)DeckId.Strong].Add(strong);
            Top(s, Card(ShrineRules.EquivalentExchange));
            Offer(s, shrine, ApricornColor.Black);
            Reply(s, ChoiceSub.ShrineExchange, ChoiceAnswer.Pick(alpha));
            Reply(s, ChoiceSub.ShrineExchangeCard, ChoiceAnswer.Pick(strong, 1));
            Assert.AreEqual(strong, p.Def);
            Assert.IsTrue(p.Alpha, "「Notably, any Alpha Pokémon tokens are kept」");
            CollectionAssert.Contains(s.Discards[(int)DeckId.Strong], Scene.Def(ctx, "445:0"), "옛 카드는 버림 더미로(꺼낸 쪽)");
            CollectionAssert.DoesNotContain(s.Discards[(int)DeckId.Strong], strong);
        }

        [Test]
        public void A20_3_동등한교환으로받은보통포켓몬은얻음_실버()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            Assert.AreEqual(TrainerRules.Bronze, st.TrainerCardTier, "셋업 뒤 브론즈");
            int mod = Scene.Give(s, ctx, seat, "388:0"); // 적당한(그로우)
            s.Pokemon[mod].Deck = (int)DeckId.Moderate;
            Top(s, Card(ShrineRules.EquivalentExchange));
            Offer(s, shrine, ApricornColor.Black);
            Reply(s, ChoiceSub.ShrineExchange, ChoiceAnswer.Pick(mod));
            var card = Scene.Legal(s, ctx, Open(s, ChoiceSub.ShrineExchangeCard)).Find(a => a.Kind == AnswerKind.Pick);
            var log = new EventLog();
            Reply(s, ChoiceSub.ShrineExchangeCard, card, log);
            Assert.AreEqual(EncounterTier.Moderate, ctx.Cards.Pokemon[s.Pokemon[mod].Def].Tier);
            Assert.AreEqual(TrainerRules.Silver, st.TrainerCardTier, "카드 효과로 얻은 것도 「obtain」(rulings A20-3)");
            var e = log.OfType<CardUnlocked>();
            Assert.AreEqual(1, e.Count);
            Assert.AreEqual(mod, e[0].CausePokemonId);
            Assert.Less(log.Events.IndexOf(log.OfType<PokemonExchanged>()[0]), log.Events.IndexOf(e[0]), "교환 뒤");
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_동등한교환은안해도되고_바꿀것이없으면묻지않는다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            Top(s, Card(ShrineRules.EquivalentExchange));
            Offer(s, shrine, ApricornColor.Red);
            int starter = s.Seats[seat].Party[0], def = s.Pokemon[starter].Def;
            var c = Open(s, ChoiceSub.ShrineExchange);
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), null), "시간 초과 = 안 함");
            Assert.AreEqual(def, s.Pokemon[starter].Def);
            MapActionFor(s, seat);

            // 전설만 가진 파티: 바꿀 것이 없다
            var t = At(2, shrine);
            int me = t.ActiveSeat;
            var party = t.Seats[me].Party;
            for (int i = 0; i < party.Length; i++) party[i] = -1;
            Scene.Give(t, ctx, me, "144:0");
            Top(t, Card(ShrineRules.EquivalentExchange));
            Offer(t, shrine, ApricornColor.Black);
            MapActionFor(t, me);
        }

        [Test]
        public void S12_2_고대의지혜는단계이하버림더미포켓몬의기술을배운다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var party = s.Seats[seat].Party;
            for (int i = 0; i < party.Length; i++) party[i] = -1;
            int mon = Scene.Give(s, ctx, seat, "387:0"); // 모부기: 풀·땅·악·노말
            int moderate = Scene.Def(ctx, "388:0");     // 그로우(적당한) — 시그니처 bite(악)
            int strong = Scene.Def(ctx, "445:0");       // 한카리아스(강한) — dragon_rush(드래곤, 못 배움)
            int weak = -1;
            foreach (int d in s.Decks[(int)DeckId.Weak])
                if (ctx.Cards.Moves[ctx.Cards.Pokemon[d].Signature].Type == PokemonType.Normal) { weak = d; break; }
            Assert.GreaterOrEqual(weak, 0);
            s.Discards[(int)DeckId.Evolution].Add(moderate);
            s.Discards[(int)DeckId.Strong].Add(strong);
            s.Discards[(int)DeckId.Weak].Add(weak);
            Top(s, Card(ShrineRules.AncientWisdom));
            Offer(s, shrine, ApricornColor.Red);
            var c = Open(s, ChoiceSub.ShrineMove);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "「You may」");
            int bite = ctx.Cards.Pokemon[moderate].Signature;
            Assert.IsTrue(legal.Exists(a => a.Target == bite), "적당한까지");
            Assert.IsTrue(legal.Exists(a => a.Target == ctx.Cards.Pokemon[weak].Signature), "「up to」 — 더 낮은 등급도(원작 커뮤니티 확인)");
            Assert.IsFalse(legal.Exists(a => a.Target == ctx.Cards.Pokemon[strong].Signature), "단계 1은 강한을 못 쓴다");
            Reply(s, ChoiceSub.ShrineMove, ChoiceAnswer.Pick(bite));
            var teach = Open(s, ChoiceSub.TeachMove);
            Assert.AreEqual(bite, teach.Subject);
            var log = new EventLog();
            Reply(s, ChoiceSub.TeachMove, ChoiceAnswer.Pick(mon, 1), log);
            Assert.AreEqual(bite, s.Pokemon[mon].Moves[1]);
            Assert.AreEqual(SourceKind.ShrineEvent, log.OfType<MoveTaught>()[0].Source.Kind);
            CollectionAssert.Contains(s.Discards[(int)DeckId.Evolution], moderate, "카드는 버림 더미에 남는다 [가정]");
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_고대의지혜는배울기술이없으면묻지않는다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            foreach (var d in new[] { DeckId.Starter, DeckId.Weak, DeckId.Moderate, DeckId.Strong, DeckId.Evolution, DeckId.Legendary })
            {
                s.Decks[(int)d].AddRange(s.Discards[(int)d]);
                s.Discards[(int)d].Clear();
            }
            Top(s, Card(ShrineRules.AncientWisdom));
            Offer(s, shrine, ApricornColor.Black);
            MapActionFor(s, s.ActiveSeat);
        }

        [Test]
        public void S12_2_화려한만찬은서로다른스튜를공짜로_만든스튜와겹쳐도된다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int zesty = Scene.Item(ctx, CraftRules.ZestyStew), restoring = Scene.Item(ctx, CraftRules.RestoringStew);
            Top(s, Card(ShrineRules.SplendidBanquet));
            Offer(s, shrine, ApricornColor.Green);
            var c = Open(s, ChoiceSub.ShrineStews);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(6, legal.Count, "스튜 4개 중 서로 다른 2개");
            foreach (var a in legal) Assert.AreNotEqual(a.Values[0], a.Values[1], "같은 스튜 두 번은 안 된다");
            int moves = st.MovesLeft;
            var pick = new List<int> { zesty, restoring };
            pick.Sort();
            var log = new EventLog();
            Reply(s, ChoiceSub.ShrineStews, ChoiceAnswer.PickMany(pick), log);
            Assert.AreEqual(moves + CraftRules.ZestyMoves, st.MovesLeft, "자극적인 스튜");
            Assert.AreEqual(2, log.OfType<StewEaten>().Count);
            Assert.AreEqual(1, log.OfType<Healed>().Count, "회복스튜");
            foreach (var color in ctx.Items.Items[zesty].Recipe) st.Apricorns[(int)color] += 1;
            Assert.IsTrue(CraftRules.CanCraft(s, ctx, seat, zesty), "만찬으로 먹어도 자극적인 스튜를 또 만든다(원작 커뮤니티 확인)");
            Assert.IsTrue(CraftRules.AlphaWarded(s, ctx, seat) == false, "만찬 스튜는 만든 것으로 적지 않는다");
            MapActionFor(s, seat);
        }

        [Test]
        public void S12_2_화려한만찬의매혹적인스튜와우러난스튜도만든것과둘다쓴다()
        {
            // 원작 커뮤니티 확인(만찬으로 자극적인 스튜를 받고 또 만들기) — 먹은 스튜마다 한 몫(전에는 매혹적인 · 우러난 스튜를 한 몫으로 묶었다)
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int alluring = Scene.Item(ctx, CraftRules.AlluringStew), infused = Scene.Item(ctx, CraftRules.InfusedStew);
            Top(s, Card(ShrineRules.SplendidBanquet));
            Offer(s, shrine, ApricornColor.Green);
            var pick = new List<int> { alluring, infused };
            pick.Sort();
            Reply(s, ChoiceSub.ShrineStews, ChoiceAnswer.PickMany(pick));
            MapActionFor(s, seat);
            int Left(UsageKind k) => s.Used.FindAll(u => u.Kind == k && u.Seat == seat).Count;
            Assert.AreEqual(1, Left(UsageKind.AlluringStew));
            Assert.AreEqual(1, Left(UsageKind.InfusedStew));
            foreach (int stew in new[] { alluring, infused })
            {
                foreach (var color in ctx.Items.Items[stew].Recipe) st.Apricorns[(int)color] += 1;
                Assert.IsTrue(CraftRules.CanCraft(s, ctx, seat, stew));
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, stew));
            }
            Assert.AreEqual(2, Left(UsageKind.AlluringStew), "만찬 것 + 만든 것");
            Assert.AreEqual(2, Left(UsageKind.InfusedStew), "만찬 것 + 만든 것");
        }

        [Test]
        public void A19_5_나눠받은화려한만찬은받은사람의다음턴시작에고른다()
        {
            int shrine = PlainShrine();
            var s = At(4, shrine);
            int seat = s.ActiveSeat;
            Top(s, Card(ShrineRules.SplendidBanquet));
            Offer(s, shrine, ApricornColor.Black);
            Reply(s, ChoiceSub.ShrineStews, Scene.Legal(s, ctx, Open(s, ChoiceSub.ShrineStews))[0]);
            int to = (seat + 1) % 4;
            Reply(s, ChoiceSub.SharedBlessing, ChoiceAnswer.Pick(to));
            MapActionFor(s, seat);
            Assert.IsTrue(s.Used.Exists(u => u.Kind == UsageKind.Banquet && u.Seat == to), "다음 자기 턴까지 기다린다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn));
            Assert.AreEqual(to, s.ActiveSeat);
            var c = Open(s, ChoiceSub.ShrineStews);
            Assert.AreEqual(to, c.Seat);
            Assert.AreEqual(3, c.Count, "단계 3 = 스튜 3개");
            Assert.IsFalse(s.Pending.Exists(p => p.Kind == ChoiceKind.MapAction), "맵 행동 전에");
            int moves = s.Seats[to].MovesLeft;
            var zesty = Scene.Legal(s, ctx, c).Find(a => a.Values.Contains(Scene.Item(ctx, CraftRules.ZestyStew)));
            Reply(s, ChoiceSub.ShrineStews, zesty);
            Assert.AreEqual(moves + CraftRules.ZestyMoves, s.Seats[to].MovesLeft, "자기 턴의 이동력");
            Assert.IsFalse(s.Used.Exists(u => u.Kind == UsageKind.Banquet));
            MapActionFor(s, to);
        }

        [Test]
        public void S12_2_숨겨진보물은수퍼볼_하이퍼볼_키스톤볼()
        {
            int shrine = PlainShrine();
            foreach (var (color, ball) in new[] { (ApricornColor.Yellow, Ball.Greater), (ApricornColor.White, Ball.Ultimate), (ApricornColor.Black, Ball.Keystone) })
            {
                var s = At(2, shrine);
                var st = s.Seats[s.ActiveSeat];
                var before = (int[])st.Balls.Clone();
                Top(s, Card(ShrineRules.HiddenTreasure));
                var log = new EventLog();
                Offer(s, shrine, color, log);
                for (int b = 0; b < SeatState.BallKinds; b++) Assert.AreEqual(before[b] + (b == (int)ball ? 1 : 0), st.Balls[b], $"{color} {(Ball)b}");
                Assert.AreEqual(ResKind.Ball, log.OfType<ShrineBlessing>()[0].Changes[0].Res);
            }
        }

        [Test]
        public void S12_2_평온한순간은여행점수2_4_6()
        {
            int shrine = PlainShrine();
            foreach (var (color, jp) in new[] { (ApricornColor.Red, 2), (ApricornColor.Pink, 4), (ApricornColor.Black, 6) })
            {
                var s = At(2, shrine);
                var st = s.Seats[s.ActiveSeat];
                int before = st.Jp;
                Top(s, Card(ShrineRules.TranquilMoment));
                Offer(s, shrine, color);
                Assert.AreEqual(before + jp, st.Jp, color.ToString());
            }
        }

        // ── 전설 이벤트(§12.3) ──

        [TestCase("shrine_event.song_of_frost", "legendary_shrine.frostsong_shrine", "144:0", ApricornColor.Blue, 3)]
        [TestCase("shrine_event.song_of_thunder", "legendary_shrine.thundersong_shrine", "145:0", ApricornColor.Yellow, 3)]
        [TestCase("shrine_event.song_of_fire", "legendary_shrine.firesong_shrine", "146:0", ApricornColor.Red, 3)]
        [TestCase("shrine_event.visitor_from_afar", "legendary_shrine.timeless_shrine", "251:0", ApricornColor.Green, 2)]
        [TestCase("shrine_event.shadow_emerges", "legendary_shrine.moonglade_shrine", "491:0", ApricornColor.Black, 1)]
        [TestCase("shrine_event.beacon_of_light", "legendary_shrine.moonglade_shrine", "488:0", ApricornColor.Pink, 2)]
        [TestCase("shrine_event.giant_awakens", "legendary_shrine.colossus_shrine", "486:0", ApricornColor.White, 2)]
        public void S12_3_전설이벤트는모두가규토리를얻고전설토큰을그사당에놓는다(string card, string shrineKey, string species, ApricornColor color, int count)
        {
            int shrine = PlainShrine();
            var s = At(3, shrine);
            s.Seats[s.ActiveSeat].Apricorns[(int)ApricornColor.Red] = 1; // 바칠 빨강 하나
            var before = new int[3];
            for (int i = 0; i < 3; i++) before[i] = s.Seats[i].Apricorns[(int)color];
            Top(s, Card(card));
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Red, log);
            for (int i = 0; i < 3; i++)
            {
                int paid = i == s.ActiveSeat && color == ApricornColor.Red ? 1 : 0;
                Assert.AreEqual(before[i] + count - paid, s.Seats[i].Apricorns[(int)color], $"좌석 {i} — 「All trainers gain」");
            }
            int cell = ctx.Fixtures.Fixtures[Fixture(shrineKey)].Cell;
            Assert.IsTrue(s.Tokens.Exists(t => t.Kind == TokenKind.Legend && t.Def == Scene.Def(ctx, species) && t.Cell == cell), "전설 토큰");
            var e = log.OfType<LegendEvent>()[0];
            Assert.IsTrue(e.Placed);
            Assert.AreEqual(Fixture(shrineKey), e.ShrineKey);
            Assert.AreEqual(SourceKind.ShrineEvent, log.OfType<ApricornsGained>()[0].Source.Kind);
            MapActionFor(s, s.ActiveSeat);
        }

        [Test]
        public void S12_3_달빛공터는다크라이와크레세리아를바꿔놓고_게임에서빠진전설은놓지않는다()
        {
            int shrine = PlainShrine();
            var s = At(2, shrine);
            int cell = ctx.Fixtures.Fixtures[Fixture("legendary_shrine.moonglade_shrine")].Cell;
            int darkrai = Scene.Def(ctx, "491:0"), cresselia = Scene.Def(ctx, "488:0");
            Top(s, Card("shrine_event.beacon_of_light"));
            Offer(s, shrine, ApricornColor.Red);
            Assert.IsTrue(s.Tokens.Exists(t => t.Kind == TokenKind.Legend && t.Def == cresselia && t.Cell == cell));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn));
            Stand(s, shrine);
            Top(s, Card("shrine_event.shadow_emerges"));
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Red, log);
            Assert.IsTrue(s.Tokens.Exists(t => t.Kind == TokenKind.Legend && t.Def == darkrai && t.Cell == cell), "다크라이");
            Assert.IsFalse(s.Tokens.Exists(t => t.Kind == TokenKind.Legend && t.Def == cresselia && t.Cell >= 0), "「replaces the Cresselia token」");
            Assert.AreEqual(cresselia, log.OfType<LegendEvent>()[0].ReplacedSpecies);

            // 잡히거나 쓰러져 게임에서 빠진 전설(전설 덱에 없다)은 놓지 않는다 — 규토리는 그대로(p.18)
            var t2 = At(2, shrine);
            t2.Decks[(int)DeckId.Legendary].Remove(Scene.Def(ctx, "144:0"));
            int blue = t2.Seats[0].Apricorns[(int)ApricornColor.Blue];
            Top(t2, Card("shrine_event.song_of_frost"));
            var log2 = new EventLog();
            Offer(t2, shrine, ApricornColor.Red, log2);
            Assert.IsFalse(log2.OfType<LegendEvent>()[0].Placed);
            Assert.IsFalse(t2.Tokens.Exists(t => t.Kind == TokenKind.Legend));
            Assert.AreEqual(blue + 3, t2.Seats[0].Apricorns[(int)ApricornColor.Blue]);
        }

        // ── 천상의 균열(§12.4) ──

        [Test]
        public void S12_4_천상의균열은모두가운명1장을받고_추종자고리로넘긴다()
        {
            int shrine = PlainShrine();
            var s = At(3, shrine);
            var hands = new int[3];
            for (int i = 0; i < 3; i++) hands[i] = s.Seats[i].FateHand.Count;
            Top(s, Card(ShrineRules.CelestialFracture));
            var log = new EventLog();
            Offer(s, shrine, ApricornColor.Red, log);
            for (int i = 0; i < 3; i++) Assert.AreEqual(hands[i] + 1, s.Seats[i].FateHand.Count, $"좌석 {i}");
            Assert.AreEqual(new SourceRef(SourceKind.ShrineEvent, Card(ShrineRules.CelestialFracture)), log.OfType<FateDealt>()[0].Source);
            Assert.AreEqual(3, s.Pending.FindAll(p => p.Kind == ChoiceKind.Simultaneous && p.Sub == ChoiceSub.Follower).Count, "추종자 비밀 선택(ShrineRules.FractureFollowers → FractureRules — 자세한 시험은 FractureTests)");
            Settle(s);
            MapActionFor(s, s.ActiveSeat);
        }

        // ── 보기 · 시간 초과 ──

        [Test]
        public void M5_사당선택은보기에서도같은합법답이고시간초과로끝난다()
        {
            int shrine = PlainShrine();
            var s = At(4, shrine);
            int seat = s.ActiveSeat;
            s.Seats[seat].Jp = 7;
            Top(s, Card(ShrineRules.EquivalentExchange), Card(ShrineRules.AncientWisdom), Card(ShrineRules.UnexpectedGift));
            Offer(s, shrine, ApricornColor.Black, null, true);
            for (int guard = 0; s.Shrine != null; guard++)
            {
                Assert.Less(guard, 40, "끝나지 않는다");
                var c = s.Pending.Find(p => !p.Answered);
                var view = Engine.ViewFor(s, c.Seat);
                var host = Scene.Legal(s, ctx, c);
                var seen = Scene.Legal(view, ctx, view.Pending.Find(p => p.Id == c.Id));
                Assert.AreEqual(host.Count, seen.Count, $"{c.Sub}: 보기의 합법 답");
                for (int i = 0; i < host.Count; i++) Assert.IsTrue(host[i].SameAs(seen[i]), $"{c.Sub} {i}");
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), null), c.Sub.ToString());
            }
            MapActionFor(s, seat);
        }
    }
}
