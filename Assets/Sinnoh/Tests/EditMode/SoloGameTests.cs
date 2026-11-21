using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 솔로 게임 잇기(plan §4 「솔로 설정·셋업」, 02 §1-3·§3-3, 04 S09·S12·S13): 설정 → 세션(<see cref="SoloGame"/>), 좌석 손님(<see cref="SeatClient"/>)이 받는 보기·선택·합법 답,
    /// 셋업을 화면과 같은 길(링크로 답)로 끝까지.
    /// </summary>
    public class SoloGameTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        sealed class Ints : IStateWriter
        {
            public readonly List<long> Values = new List<long>();
            public void Int(int v) => Values.Add(v);
            public void ULong(ulong v) => Values.Add((long)v);
        }

        static List<long> Walk(RuleOptions o)
        {
            var w = new Ints();
            o.Walk(w);
            return w.Values;
        }

        SeatClient Start(SoloSettings set, ulong seed, out LocalSession session, List<GameEvent> events = null)
        {
            session = SoloGame.Start(ctx, set, seed, 99, new PrincipleAgent(), 0);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            if (events != null) client.Updated += u => events.AddRange(u.Events);
            return client;
        }

        [Test]
        public void M4_솔로설정기본값은규칙기본값이고고른값만바뀐다()
        {
            var set = new SoloSettings();
            Assert.AreEqual(Campaign.HeartOfSinnoh, set.Campaign);
            Assert.IsTrue(set.TrainerCards, "트레이너 카드 기본 켬(결정 14 ④)");
            Assert.IsFalse(set.UltraOnslaught);
            Assert.AreEqual(UbDifficulty.NewTrainer, set.UbDifficulty, "기본 신인(결정 29 ⑤)");
            CollectionAssert.AreEqual(Walk(new RuleOptions { NonPlayerMovePolicy = NonPlayerMovePolicy.Principle }), Walk(set.ToOptions()), "기본 설정 = rulings 기본값(결정 14 ①) — UB · 야생 기술 선택만 이 형의 기본이 원칙(확인용 판, 사람이 여는 새 판은 NewGameNonPlayer — 결정 122)");

            set.TrainerCards = false;
            set.UbDifficulty = UbDifficulty.MasterTrainer;
            set.UltraOnslaught = true;
            var o = set.ToOptions();
            Assert.IsFalse(o.TrainerCards);
            Assert.AreEqual(UbDifficulty.MasterTrainer, o.UbDifficulty);
            Assert.IsFalse(o.UltraOnslaught, "울트라 온슬롯은 울트라 버스트에서만(A21)");
            set.Campaign = Campaign.UltraBurst;
            Assert.IsTrue(set.ToOptions().UltraOnslaught);
            var want = new RuleOptions { TrainerCards = false, UbDifficulty = UbDifficulty.MasterTrainer, UltraOnslaught = true, NonPlayerMovePolicy = NonPlayerMovePolicy.Principle };
            CollectionAssert.AreEqual(Walk(want), Walk(set.ToOptions()), "고른 셋 말고는 기본값");

            var ai = set.ToAi(1234);
            Assert.AreEqual(SeatId.Galactic, ai.GalacticController, "솔로는 조종이 AI 고정(02 §3-3)");
            Assert.AreEqual(SeatId.UltraBeast, ai.UbController);
            Assert.AreEqual(GalacticAiLevel.Principle, ai.GalacticAiLevel);
            Assert.AreEqual(1234UL, ai.Seed);

            var copy = set.Clone();
            copy.TrainerCards = true;
            Assert.IsFalse(set.TrainerCards, "Clone은 따로 논다");
        }

        [Test]
        public void M4_아직없는캠페인과AI세기는시작하지않는다()
        {
            Assert.IsTrue(SoloGame.CampaignReady(Campaign.HeartOfSinnoh));
            Assert.IsTrue(SoloGame.CampaignReady(Campaign.UltraBurst), "울트라 버스트는 M6 「UB 시작」부터 솔로 설정에서 연다");
            Assert.IsTrue(SoloGame.AiReady(GalacticAiLevel.Principle));
            Assert.IsTrue(SoloGame.AiReady(GalacticAiLevel.Strong), "강한 AI는 M8 「연결」부터 고를 수 있다");
            Assert.IsFalse(SoloGame.AiReady((GalacticAiLevel)99), "없는 세기");
            Assert.Throws<NotSupportedException>(() => SoloGame.Start(ctx, new SoloSettings { GalacticAi = (GalacticAiLevel)99 }, 1, 2, new PrincipleAgent(), 0));
            Assert.Throws<ArgumentNullException>(() => SoloGame.Start(ctx, null, 1, 2, new PrincipleAgent(), 0));
        }

        [Test]
        public void M4_솔로게임은좌석하나로시작해고향마을고르기가열린다_A20_1()
        {
            var client = Start(new SoloSettings(), 11, out var session);
            Assert.AreEqual(1, session.SeatCount, "솔로 = 좌석 하나(결정 14 ②)");
            Assert.IsNull(session.Head.SaveId);
            Assert.AreEqual(Campaign.HeartOfSinnoh, session.Head.Campaign);
            Assert.AreEqual(99UL, session.Head.Ai.Seed, "설정의 AI 값이 세션(저장 머리)까지 간다 — AI 난수 씨(01 §4·§9-1)");
            Assert.AreEqual(GalacticAiLevel.Principle, session.Head.Ai.GalacticAiLevel);
            Assert.AreEqual(SeatId.Galactic, session.Head.Ai.GalacticController);
            Assert.IsNotNull(client.View, "붙는 즉시 지금 보기가 온다");
            Assert.AreEqual(SoloGame.Seat, client.View.Viewer, "화면은 자기 보기만 받는다");
            Assert.AreEqual(Phase.Setup, client.View.Phase);
            Assert.AreEqual(SetupFlow.TrainerCardOffer, client.View.Seats[0].TrainerCardOffer.Count, "트레이너 카드 3장(A20-1)");
            foreach (int card in client.View.Seats[0].TrainerCardOffer) Assert.That(card, Is.InRange(0, ctx.Cards.TrainerCards.Count - 1), "내 후보는 보인다");

            var pick = client.Choice(ChoiceKind.VillagePick);
            Assert.IsNotNull(pick);
            Assert.AreSame(pick, client.FirstChoice());
            Assert.IsNull(client.Choice(ChoiceKind.StarterPick));
            var legal = client.Legal(pick);
            Assert.AreEqual(8, legal.Count, "신오의 심장: 마을 8곳(울트라메가로폴리스 제외, G12)");
            foreach (var a in legal)
            {
                var f = ctx.Fixtures.Fixtures[a.Target];
                Assert.AreEqual(FixtureKind.Village, f.Kind);
                Assert.AreNotEqual("village.ultra_megalopolis", f.Key);
                Assert.AreEqual(2, f.Starters.Count, f.Key + ": 스타팅 포켓몬 2마리");
            }
        }

        [Test]
        public void M4_솔로셋업을끝까지고르면첫턴이열린다_A20_1()
        {
            var events = new List<GameEvent>();
            var client = Start(new SoloSettings(), 11, out var session, events);
            var village = client.Legal(client.Choice(ChoiceKind.VillagePick))[3];
            Assert.IsTrue(client.Answer(client.Choice(ChoiceKind.VillagePick), village, 0).Accepted);
            Assert.AreEqual(village.Target, client.View.Seats[0].HomeVillage);
            Assert.AreEqual(ctx.Fixtures.Fixtures[village.Target].Cell, client.View.Seats[0].Cell, "말을 고향 마을에");
            Assert.AreEqual(Phase.Setup, client.View.Phase);

            // 스타팅 포켓몬 + 트레이너 카드: 한 묶음(결정 25) — 둘 다 답해야 넘어간다
            var starter = client.Choice(ChoiceKind.StarterPick);
            var card = client.Choice(ChoiceKind.TrainerCardPick);
            Assert.IsNotNull(starter);
            Assert.IsNotNull(card);
            var starters = client.Legal(starter);
            var cards = client.Legal(card);
            CollectionAssert.AreEqual(ctx.Fixtures.Fixtures[village.Target].Starters, starters.ConvertAll(a => a.Target), "고른 마을의 스타팅 포켓몬 둘");
            CollectionAssert.AreEquivalent(client.View.Seats[0].TrainerCardOffer, cards.ConvertAll(a => a.Target), "받은 3장 중 1장");
            long now = Math.Max(1, client.AnimEndMs);
            Assert.IsTrue(client.Answer(starter, starters[1], now).Accepted);
            Assert.AreEqual(Phase.Setup, client.View.Phase, "카드까지 골라야 시작한다");
            Assert.IsNull(client.Choice(ChoiceKind.StarterPick), "답한 선택은 다시 나오지 않는다");
            Assert.IsTrue(client.Answer(client.Choice(ChoiceKind.TrainerCardPick), cards[2], now).Accepted);

            var v = client.View;
            Assert.AreEqual(Phase.Turn, v.Phase);
            var seat = v.Seats[0];
            Assert.AreEqual(cards[2].Target, seat.TrainerCard);
            Assert.AreEqual(0, seat.TrainerCardOffer.Count, "남은 2장은 덱으로");
            Assert.That(seat.Party[0], Is.GreaterThanOrEqualTo(0));
            Assert.AreEqual(starters[1].Target, v.Pokemon[seat.Party[0]].Def);
            Assert.AreEqual(1, v.Hos.Act, "서막 → 1막 공개");
            Assert.AreEqual(SetupFlow.ActOneRedApricorns, seat.Apricorns[(int)ApricornColor.Red]);
            Assert.IsTrue(SetupFlow.GruntAt(v, seat.Cell), "서막: 고향 마을에 조무래기(S16_2)");
            var turn = client.Choice(ChoiceKind.MapAction);
            Assert.IsNotNull(turn, "첫 턴의 행동 선택이 열린다");
            Assert.AreEqual(0, v.ActiveSeat);

            Assert.AreEqual(1, events.FindAll(e => e is VillagePicked).Count);
            Assert.AreEqual(1, events.FindAll(e => e is StarterPicked).Count);
            Assert.AreEqual(1, events.FindAll(e => e is TrainerCardsRevealed).Count);
            Assert.AreEqual(2, events.FindAll(e => e is ActRevealed).Count, "서막·1막");
            Assert.AreEqual(1, events.FindAll(e => e is TurnStarted).Count);
            Assert.That(client.AnimEndMs, Is.GreaterThan(now), "셋업 연출 길이만큼 방장 멈춤(02 §5-7)");
            Assert.AreEqual(session.ViewFor(0).CommandCount, v.CommandCount);
            Assert.AreEqual(3, session.CommandCount, "답 셋");
        }

        [Test]
        public void M4_트레이너카드를끄면스타팅포켓몬만고른다_A20_8()
        {
            var client = Start(new SoloSettings { TrainerCards = false }, 5, out _);
            Assert.AreEqual(0, client.View.Seats[0].TrainerCardOffer.Count);
            client.Answer(client.Choice(ChoiceKind.VillagePick), client.Legal(client.Choice(ChoiceKind.VillagePick))[0], 0);
            Assert.IsNull(client.Choice(ChoiceKind.TrainerCardPick));
            var starter = client.Choice(ChoiceKind.StarterPick);
            Assert.IsTrue(client.Answer(starter, client.Legal(starter)[0], client.AnimEndMs).Accepted);
            Assert.AreEqual(Phase.Turn, client.View.Phase);
            Assert.AreEqual(-1, client.View.Seats[0].TrainerCard);
        }

        [Test]
        public void M4_같은시드는같은판이고시드가다르면받는카드가다르다()
        {
            ulong Play(ulong seed, out List<int> offer)
            {
                var client = Start(new SoloSettings(), seed, out var session);
                offer = new List<int>(client.View.Seats[0].TrainerCardOffer);
                client.Answer(client.Choice(ChoiceKind.VillagePick), client.Legal(client.Choice(ChoiceKind.VillagePick))[0], 0);
                var starter = client.Choice(ChoiceKind.StarterPick);
                client.Answer(starter, client.Legal(starter)[0], 5000);
                var card = client.Choice(ChoiceKind.TrainerCardPick);
                client.Answer(card, client.Legal(card)[0], 5000);
                return session.Hash;
            }
            ulong a = Play(21, out var offerA), b = Play(21, out var offerB);
            Assert.AreEqual(a, b, "같은 시드·같은 답 = 같은 상태");
            CollectionAssert.AreEqual(offerA, offerB);
            var seen = new HashSet<string> { string.Join(",", offerA) };
            for (ulong seed = 22; seed < 30; seed++)
            {
                Play(seed, out var offer);
                seen.Add(string.Join(",", offer));
            }
            Assert.Greater(seen.Count, 4, "시드마다 받는 3장이 다르다");
        }

        [Test]
        public void M4_좌석손님은틀린답을거절로받고보기는그대로다()
        {
            var client = Start(new SoloSettings(), 3, out var session);
            var rejects = new List<RejectedMsg>();
            client.Rejected += rejects.Add;
            var pick = client.Choice(ChoiceKind.VillagePick);
            int megalopolis = ctx.Fixtures.Ids.IndexOf("village.ultra_megalopolis");
            int updates = client.Updates;
            var r = client.Answer(pick, ChoiceAnswer.Pick(megalopolis), 0);
            Assert.IsFalse(r.Accepted, "울트라메가로폴리스는 울트라 버스트에서만(G12)");
            Assert.AreEqual(RejectReason.Illegal, r.Reason);
            Assert.AreEqual(1, rejects.Count);
            Assert.AreEqual(pick.Id, client.LastRejected.ChoiceId);
            Assert.AreEqual(updates, client.Updates, "거절은 보기를 바꾸지 않는다");
            Assert.AreEqual(-1, client.View.Seats[0].HomeVillage);
            Assert.AreEqual(0, session.CommandCount);

            Assert.IsTrue(client.Answer(pick, client.Legal(pick)[0], 0).Accepted);
            Assert.IsNull(client.LastRejected, "다음 갱신이 오면 거절을 지운다");
            Assert.IsFalse(client.Answer(pick, client.Legal(client.Choice(ChoiceKind.StarterPick))[0], 0).Accepted, "닫힌 선택에는 답하지 못한다");

            // 떨어진 뒤에는 갱신이 오지 않는다 — 같은 좌석에 새 손님을 붙이지 않고(붙이면 링크가 옛 손님을 덮어써 Close 없이도 끊긴다) 호스트에 바로 답을 내 갱신을 일으킨다
            int before = client.Updates;
            var starter = client.Choice(ChoiceKind.StarterPick);
            var starterAnswer = client.Legal(starter)[0];
            client.Close();
            int commands = session.CommandCount;
            Assert.IsTrue(session.Submit(SoloGame.Seat, Command.AnswerTo(starter.Id, starterAnswer), 10000).Accepted);
            Assert.AreEqual(commands + 1, session.CommandCount, "호스트는 답을 받아 갱신을 냈다");
            Assert.AreEqual(before, client.Updates);
            Assert.IsNull(client.Choice(ChoiceKind.VillagePick), "옛 손님의 보기는 떨어질 때 그대로다");
            Assert.IsNotNull(client.Choice(ChoiceKind.StarterPick));

            // 다시 붙으면 지금 보기가 온다
            var other = new SeatClient(session.Link, ctx, SoloGame.Seat);
            Assert.AreEqual(1, other.Updates);
            Assert.IsNull(other.Choice(ChoiceKind.StarterPick), "이미 답한 선택");
            Assert.IsNotNull(other.Choice(ChoiceKind.TrainerCardPick));
        }
    }
}
