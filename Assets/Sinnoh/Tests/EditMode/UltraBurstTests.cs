using System;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class UltraBurstTests
    {
        [Test]
        public void R4_UB이동총량4더하기균열10분의1()
        {
            Assert.AreEqual(4, UltraBurstRules.TotalMoves(0));
            Assert.AreEqual(4, UltraBurstRules.TotalMoves(9));
            Assert.AreEqual(5, UltraBurstRules.TotalMoves(10));
            Assert.AreEqual(5, UltraBurstRules.TotalMoves(19));
            Assert.AreEqual(6, UltraBurstRules.TotalMoves(20));
            Assert.AreEqual(10, UltraBurstRules.TotalMoves(69));
        }

        [Test]
        public void R6_네크로즈마파괴수_인원1에서6_112233()
        {
            var expected = new[] { 1, 1, 2, 2, 3, 3 };
            for (int seats = 1; seats <= 6; seats++)
                Assert.AreEqual(expected[seats - 1], UltraBurstRules.NecrozmaDestroyCount(seats), $"{seats}인");
            Assert.Throws<ArgumentOutOfRangeException>(() => UltraBurstRules.NecrozmaDestroyCount(7));
        }

        [Test]
        public void R7_균열강도131분모_반올림()
        {
            Assert.AreEqual(131, UltraBurstRules.BreachTargets, "사당 13 + 나무·산호 118");

            // Lua measure_breach_strength: round(100 * n / total), round(x) = math.floor(x + 0.5)
            for (int destroyed = 0; destroyed <= 131; destroyed++)
                Assert.AreEqual((int)Math.Floor(100.0 * destroyed / 131 + 0.5), UltraBurstRules.BreachStrength(destroyed), $"파괴 {destroyed}");

            Assert.AreEqual(1, UltraBurstRules.BreachStrength(1), "0.76 → 1");
            Assert.AreEqual(5, UltraBurstRules.BreachStrength(7), "5.34 → 5");
            Assert.AreEqual(10, UltraBurstRules.BreachStrength(13), "9.92 → 10");
            Assert.AreEqual(100, UltraBurstRules.BreachStrength(131));
            Assert.AreEqual(13, UltraBurstRules.BreachStrength(1, 8), "12.5는 올림(floor(x + 0.5))");
        }

        [Test]
        public void R8_균열70이면패배()
        {
            Assert.IsFalse(UltraBurstRules.IsLost(69));
            Assert.IsTrue(UltraBurstRules.IsLost(70));
            Assert.AreEqual(69, UltraBurstRules.BreachStrength(91));
            Assert.IsFalse(UltraBurstRules.IsLost(UltraBurstRules.BreachStrength(91)), "파괴 91 = 69%");
            Assert.AreEqual(70, UltraBurstRules.BreachStrength(92));
            Assert.IsTrue(UltraBurstRules.IsLost(UltraBurstRules.BreachStrength(92)), "파괴 92 = 70%");
        }

        [Test]
        public void B4_구간여러개넘으면넘은만큼균열카드()
        {
            var mode = new RuleOptions().BreachCardsPerBracket;
            Assert.AreEqual(BreachCardsPerBracket.EachBracketCrossed, mode, "기본값 = rulings B4 [가정]");

            Assert.AreEqual(3, UltraBurstRules.BreachCardsPerSeat(9, 31, mode), "10·20·30 구간");
            Assert.AreEqual(1, UltraBurstRules.BreachCardsPerSeat(15, 25, mode));
            Assert.AreEqual(0, UltraBurstRules.BreachCardsPerSeat(10, 19, mode), "같은 구간 안");
            Assert.AreEqual(0, UltraBurstRules.BreachCardsPerSeat(59, 70, mode), "패배면 주지 않는다(Lua)");
        }

        [Test]
        public void B4_Lua옵션이면턴에1장()
        {
            var mode = BreachCardsPerBracket.OncePerTurn;
            Assert.AreEqual(1, UltraBurstRules.BreachCardsPerSeat(9, 31, mode), "구간 3개를 넘어도 1장");
            Assert.AreEqual(1, UltraBurstRules.BreachCardsPerSeat(15, 25, mode));
            Assert.AreEqual(0, UltraBurstRules.BreachCardsPerSeat(10, 19, mode));
            Assert.AreEqual(0, UltraBurstRules.BreachCardsPerSeat(55, 72, mode));
        }
    }
}
