using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// rulings에서 정한 옵션 기본값. 양쪽 값의 동작 시험은 그 기능을 만드는 M3 작업에서 쓴다(rulings G5·G6 「시험」).
    /// </summary>
    public class RuleOptionsTests
    {
        [Test]
        public void G5_은하의응징기본값은턴끝()
        {
            // 옛 기본 BeforeDamage [가정] → 원작 커뮤니티 확인) [가정 — 2023 답과 엇갈림]
            Assert.AreEqual(GalacticRetributionTiming.TurnEnd, new RuleOptions().GalacticRetributionTiming);
        }

        [Test]
        public void A20_4_해금기본값은진화인정_전설우두머리불인정()
        {
            // 원작 커뮤니티 확인 ·
            // 원작 커뮤니티 확인(전설·우두머리원작 커뮤니티 확인)
            var o = new RuleOptions();
            Assert.IsTrue(o.UnlockByEvolution, "진화로 된 보통·강함 = 얻음");
            Assert.IsFalse(o.UnlockLegendaryAsStrong, "순수 전설은 강함이 아니다");
            Assert.IsFalse(o.UnlockAlphaAsStrong, "우두머리도 강함이 아니다(전설로 재분류)");
            Assert.IsFalse(o.UnlockTrade, "결정 76 ⑦ T3 나: 거래로 받은 포켓몬은 「얻음」이 아니다(포획 · 카드 효과로 얻은 것만)");
            Assert.IsTrue(o.UnlockDefeatOtherTrainer, "원작 커뮤니티 확인");
        }

        [Test]
        public void A20_5_해금순서기본값은순서대로_먼저한강함은기억하지않음()
        {
            // 원작 커뮤니티 확인 — 소급 여부는 엇갈려 [가정]
            Assert.AreEqual(UnlockOrder.SilverOnly, new RuleOptions().UnlockOrder);
        }

        [Test]
        public void G21_둘째기술기본값은안써도쓴것_다음턴의직전기술은둘째기술만()
        {
            // 결정 76 ⑤ B1 ② 나 · ⑥ B1 ③ 나(rulings G21 — 둘째 기술 넷 가운데 둘). ①④는 그대로(가).
            var o = new RuleOptions();
            Assert.IsTrue(o.SecondMoveDeclineSpends, "「안 씀」으로 넘겨도 「한 번」을 쓴 것 — 교체할 때까지 다시 없다");
            Assert.AreEqual(SecondMoveLastTurn.SecondOnly, o.SecondMoveLastTurn, "다음 턴 「직전에 쓴 기술」은 둘째로 쓴 기술만");
            Assert.IsFalse(o.SecondMoveAfterKo, "① 가: 첫 공격으로 쓰러지면 둘째 기술은 없다");
            Assert.AreEqual(SecondMoveTurnEnd.Once, o.SecondMoveTurnEnd, "④ 가: 턴 끝은 두 공격 뒤 한 번");
        }

        [Test]
        public void G6_반응묻기기본값은한꺼번에()
        {
            Assert.AreEqual(ReactionAsk.Simultaneous, new RuleOptions().ReactionAsk);
        }
    }
}
