using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6.5 확장 장 ② E7~E11(07 §5-2 · §8 완료 조건 1): 각본을 자동 진행으로 끝까지 — 단계마다 expect = 엔진 값(Failures 0) · 주사위 대본을 다 썼다 · 거르개 거절 0 · 봇 큐를 다 썼다 ·
    /// 끝 국면이 그 장이 다룬 요소대로다. 막 앞 판 손보기는 Ai 막 길(<see cref="SoloTrail"/> — <see cref="TutorialDataTests.TrailAfter"/>).
    /// </summary>
    public class TutorialExtra2Tests
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

        static int Cell(string fixture) => ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(fixture)].Cell;

        static bool TokenAt(GameState s, TokenKind kind, int cell) => s.Tokens.Any(t => t.Kind == kind && t.Cell == cell && !t.Destroyed);

        [Test]
        public void M65_E7_각본_끝까지()
        {
            var h = Played("e07").Session.HostCopy();
            Assert.IsTrue(TokenAt(h, TokenKind.Legend, Cell("legendary_shrine.firesong_shrine")), "불꽃의 노래 → 불꽃노래 사당에 전설 토큰(A27 ③)");
            Assert.IsNull(h.Shrine, "3장 중 2장을 처리하고 남은 1장은 버렸다");
            Assert.AreEqual(Phase.Turn, h.Phase);
        }

        [Test]
        public void M65_E8_각본_끝까지()
        {
            var h = Played("e08").Session.HostCopy();
            Assert.IsTrue(TokenAt(h, TokenKind.God, HosFlow.OriginShrineCell(ctx)), "펄기아 강림(A31)");
            Assert.AreEqual(SinnohGod.Palkia, h.Seats[0].Follower);
            Assert.AreEqual(SinnohGod.Palkia, h.Seats[1].Follower);
            Assert.AreEqual(SinnohGod.None, h.Seats[2].Follower, "처음은 안 고를 수 있다(결정 78)");
            Assert.AreEqual(0, h.ActiveSeat, "다음 라운드 내 턴");
        }

        [Test]
        public void M65_E9_각본_끝까지()
        {
            var h = Played("e09").Session.HostCopy();
            Assert.IsTrue(h.Seats[0].Stamps[StampRules.HeldItems], "스탬프 「지닌물건 셋」(A30)");
            Assert.AreEqual(StampRules.HeldNeeded, StampRules.HeldCount(h, h.Seats[0]));
        }

        [Test]
        public void M65_E10_각본_끝까지()
        {
            var h = Played("e10").Session.HostCopy();
            int stone = ctx.Items.Ids.IndexOf("item.magma_stone");
            CollectionAssert.Contains(h.Seats[0].Bag, stone, "트레이너전 보상 = 유물(A25 ④)");
            CollectionAssert.DoesNotContain(h.Seats[1].Bag, stone);
            Assert.IsTrue(h.Seats[0].Stamps[StampRules.TrainerWin], "스탬프 「트레이너 승리」(A30)");
            Assert.IsNull(h.Battle);
            Assert.AreEqual(1, h.ActiveSeat, "배틀이 끝나 내 턴이 끝났다");
        }

        [Test]
        public void M65_E11_각본_끝까지()
        {
            var h = Played("e11").Session.HostCopy();
            Assert.IsTrue(h.Seats[0].Stamps[StampRules.SwarmWin], "스탬프 「무리 배틀 승리」(A30)");
            Assert.AreEqual(BattleRules.SwarmRewardJp, h.Seats[0].Jp, "무리 배틀 보상 = 여행 점수 7 하나(A18)");
            Assert.IsNull(h.Battle);
        }
    }
}
