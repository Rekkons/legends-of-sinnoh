using NUnit.Framework;
using Sinnoh.Rules;
using static Sinnoh.Rules.AttackDieFace;

namespace Sinnoh.Tests.EditMode
{
    public class CaptureTests
    {
        static AttackDieFace[] Throw(Ball ball, params AttackDieFace[] script)
        {
            var rng = ScriptedRng.Dice(script);
            var faces = AttackDice.Roll(rng, CaptureRules.Dice(ball));
            Assert.IsTrue(rng.Exhausted, "볼 등급만큼 굴린다");
            return faces;
        }

        [Test]
        public void A7_눈합이포획률이상()
        {
            Assert.AreEqual(3, CaptureRules.Dice(Ball.Regular));
            Assert.AreEqual(4, CaptureRules.Dice(Ball.Greater));
            Assert.AreEqual(5, CaptureRules.Dice(Ball.Ultimate));
            Assert.AreEqual(6, CaptureRules.Dice(Ball.Keystone));

            var faces = CaptureRules.ApplyAccuracy(Throw(Ball.Regular, StatusSymbol, RegularHit, CriticalHit), false);
            Assert.IsTrue(CaptureRules.Succeeds(faces, 3), "0 + 1 + 2 = 3 ≥ 3(같아도 성공)");
            Assert.IsFalse(CaptureRules.Succeeds(faces, 4), "3 < 4");

            // 타격 수(2)가 아니라 눈 합(4)으로 본다
            faces = CaptureRules.ApplyAccuracy(Throw(Ball.Regular, StatusSymbol, CriticalHit, CriticalHit), false);
            Assert.IsTrue(CaptureRules.Succeeds(faces, 4));

            // 01 §6-3: 몬스터볼 3개로 3+ → 합 0~2(1 + 12 + 51)만 실패
            Assert.AreEqual(152, CaptureRules.SuccessWeight(3, 3, false));
            Assert.AreEqual(216, AttackDice.TotalWeight(3));
        }

        [Test]
        public void A7_상태이상이면상태심볼하나를일반으로()
        {
            var faces = new[] { StatusSymbol, StatusSymbol, RegularHit };
            CollectionAssert.AreEqual(new[] { RegularHit, StatusSymbol, RegularHit }, CaptureRules.ApplyAccuracy(faces, true), "심볼 1개만 바뀐다");
            CollectionAssert.AreEqual(faces, CaptureRules.ApplyAccuracy(faces, false), "상태이상이 아니면 그대로");
            CollectionAssert.AreEqual(new[] { StatusSymbol, StatusSymbol, RegularHit }, faces, "원본 배열은 안 바뀐다");
            CollectionAssert.AreEqual(new[] { RegularHit, RegularHit, CriticalHit },
                CaptureRules.ApplyAccuracy(new[] { RegularHit, RegularHit, CriticalHit }, true), "심볼이 없으면 바꿀 것이 없다");

            faces = Throw(Ball.Regular, StatusSymbol, RegularHit, RegularHit);
            Assert.IsFalse(CaptureRules.Succeeds(CaptureRules.ApplyAccuracy(faces, false), 3), "합 2");
            Assert.IsTrue(CaptureRules.Succeeds(CaptureRules.ApplyAccuracy(faces, true), 3), "심볼 → 일반으로 합 3");

            // 01 §6-3: 몬스터볼 3개·3+, 대상 상태이상 → (s3) 1 + (s2, r1) 12만 실패
            Assert.AreEqual(203, CaptureRules.SuccessWeight(3, 3, true));
        }

        [Test]
        public void A7_신오의신유물없으면7()
        {
            Assert.AreEqual(7, CaptureRules.Rate(6, celestialAura: true));
            Assert.AreEqual(6, CaptureRules.Rate(6, celestialAura: false), "유물이 있으면 카드 포획률");

            var faces = CaptureRules.ApplyAccuracy(Throw(Ball.Keystone, StatusSymbol, StatusSymbol, StatusSymbol, CriticalHit, CriticalHit, CriticalHit), false);
            Assert.IsTrue(CaptureRules.Succeeds(faces, 6), "합 6");
            Assert.IsFalse(CaptureRules.Succeeds(faces, CaptureRules.Rate(6, true)), "6 < 7");

            Assert.AreEqual(0, CaptureRules.SuccessWeight(3, 7, true), "몬스터볼 3개는 최대 6이라 천상의 오라를 못 넘는다");
            Assert.AreEqual(1, CaptureRules.SuccessWeight(3, 6, false), "크리 3개뿐");
        }
    }
}
