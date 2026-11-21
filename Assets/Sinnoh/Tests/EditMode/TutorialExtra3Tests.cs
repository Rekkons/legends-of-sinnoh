using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6.5 확장 장 ③ E12~E15(07 §5-2 · §8 완료 조건 1): 각본을 자동 진행으로 끝까지 — 단계마다 expect = 엔진 값(Failures 0) · 주사위 대본을 다 썼다 · 거르개 거절 0 · 봇 큐를 다 썼다 ·
    /// 끝 국면이 그 장이 다룬 요소대로다. 막 앞 판 손보기는 Ai 막 길(<see cref="SoloTrail"/> — <see cref="TutorialDataTests.TrailAfter"/>).
    /// </summary>
    public class TutorialExtra3Tests
    {
        static Ctx ctx;

        [OneTimeSetUp]
        public void Load() => ctx = TestData.LoadCtx();

        static TutorialRunner Played(string id)
        {
            var s = TutorialLoader.Load(TestData.ReadText($"tutorial/{id}.json"), ctx, $"tutorial/{id}.json");
            var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TutorialDataTests.TrailAfter);
            run.PlayToEnd();
            Assert.IsTrue(run.Finished, id);
            CollectionAssert.IsEmpty(run.Failures, id + "\n" + string.Join("\n", run.Failures));
            Assert.AreEqual(0, run.Deal.DiceLeft, id + ": 주사위 대본을 다 썼다");
            Assert.AreEqual(0, run.Blocked, id + ": 자동 답은 거르개를 지난다");
            Assert.IsTrue(run.Rebased, id + ": 막 앞 판 손보기를 걸었다");
            for (int seat = 1; seat < s.Start.Seats; seat++) Assert.AreEqual(0, run.Bot(seat).Left, $"{id}: 좌석 {seat} 봇 큐를 다 썼다");
            return run;
        }

        static int Home(GameState h, int seat) => ctx.Fixtures.Fixtures[h.Seats[seat].HomeVillage].Cell;

        [Test]
        public void M65_E12_각본_끝까지()
        {
            var h = Played("e12").Session.HostCopy();
            var me = h.Seats[0];
            Assert.AreEqual(ctx.Cards.TrainerCardIds.IndexOf(SoloTrail.CardSwapNext), me.TrainerCard, "반바지 꼬마 빨강으로 캠프파이어로 바꿨다(A20-5 · A40)");
            Assert.AreEqual(TrainerRules.Gold, me.TrainerCardTier, "바꾼 카드는 골드");
            Assert.AreEqual(2, me.Firewood, "[장작 모으기] 3 − 「점화」 1(A35 · A37)");
            Assert.Less(h.Tokens[0].Cell, 0, "[장작 모으기]가 옆 나무를 없앴다");
            Assert.IsNull(h.Battle);
        }

        [Test]
        public void M65_E13_각본_끝까지()
        {
            var h = Played("e13").Session.HostCopy();
            Assert.AreEqual(UbState.PrismArmourTotal, h.Ub.PrismUltraBurst);
            Assert.AreEqual(3, h.Ub.Blessings, "다수결 동률은 건너뛰지 않는다(결정 87 R3)");
            Assert.AreEqual(13, UbTurn.Breach(h), "UB 턴 하나 뒤 균열(A51)");
            for (int seat = 0; seat < 2; seat++)
                Assert.IsTrue(h.Seats[seat].Party.Any(id => id >= 0 && h.Pokemon[id].Breach.Any(c => c >= 0)), $"좌석 {seat}: 균열 카드를 붙였다(A52)");
            CollectionAssert.IsEmpty(h.Ub.BreachHands, "손에 남은 균열 카드가 없다");
            Assert.AreEqual(0, h.ActiveSeat, "다음 라운드 내 턴");
        }

        [Test]
        public void M65_E14_각본_끝까지()
        {
            var h = Played("e14").Session.HostCopy();
            Assert.AreEqual(1, h.Ub.Defeated, "페로코체를 쓰러뜨렸다(A53 ②)");
            Assert.AreEqual(UbState.PrismArmourTotal - 1, h.Ub.PrismUltraBurst);
            Assert.AreEqual(1, h.Ub.PrismUltraBeast, "프리즘 아머 1개가 UB 쪽으로(A53 ③)");
            Assert.AreEqual(1, h.Ub.NecrozmaPhase, "1페이즈에서 졌다");
            Assert.AreEqual(2, h.Ub.Blessings, "부활에 축복 1개(A54 ⑥)");
            Assert.IsFalse(h.Seats[0].Eliminated, "리타이어했다가 부활했다");
            Assert.AreEqual(Home(h, 0), h.Seats[0].Cell, "부활은 고향 마을에서(A54 ⑥)");
            Assert.IsTrue(h.Options.UltraOnslaught, "울트라 온슬롯을 켠 판(A21)");
            Assert.AreEqual(0, h.ActiveSeat);
        }

        [Test]
        public void M65_E15_각본_끝까지()
        {
            var h = Played("e15").Session.HostCopy();
            Assert.IsTrue(h.Ub.TrialsOvercome[(int)Shard.Willpower], "의지의 시련을 넘겼다(A55 ④)");
            Assert.IsTrue(h.Ub.GuardiansDefeated[(int)Shard.Willpower], "아그놈을 쓰러뜨렸다");
            Assert.AreEqual(5, h.Seats[0].MovesLeft, "수호신 이동력 +2(A55 ⑤)");
            Assert.AreEqual(3, h.Ub.Blessings, "실버디는 대사로만 — 축복 그대로");
            Assert.IsNull(h.Battle);
        }
    }
}
