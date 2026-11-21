using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class FatigueTests
    {
        static readonly RuleOptions Default = new RuleOptions();

        [Test]
        public void S7_7_5번뒤굴림마다3피해()
        {
            int fatigue = 0;
            for (int roll = 1; roll <= 5; roll++)
            {
                Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, false, Default), $"{roll}번째 굴림은 피해 없음");
                Assert.AreEqual(roll, fatigue);
            }
            for (int roll = 6; roll <= 8; roll++)
            {
                Assert.AreEqual(FatigueRules.Damage, FatigueRules.Roll(ref fatigue, false, Default), $"{roll}번째 굴림은 3 피해(rulings A10 피로 경계)");
                Assert.AreEqual(FatigueRules.Max, fatigue, "피로는 5에 머문다");
            }
            Assert.AreEqual(3, FatigueRules.Damage);
        }

        [Test]
        public void S7_7_교체하면피로0()
        {
            int fatigue = 0;
            for (int i = 0; i < 5; i++) FatigueRules.Roll(ref fatigue, false, Default);
            Assert.AreEqual(FatigueRules.Max, fatigue);

            FatigueRules.SwitchOut(ref fatigue);
            Assert.AreEqual(0, fatigue);
            Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, false, Default), "다시 나와 첫 굴림은 피해 없음");
            Assert.AreEqual(1, fatigue);
        }

        [Test]
        public void A10_포획굴림도피로증가()
        {
            // 확정(rulings A10, 원작 커뮤니티 확인): 원작 커뮤니티 확인
            Assert.IsTrue(Default.CaptureAddsFatigue, "기본값 = rulings A10");

            int fatigue = 0;
            for (int i = 0; i < 3; i++) FatigueRules.Roll(ref fatigue, false, Default);
            Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, true, Default));
            Assert.AreEqual(4, fatigue, "포획 굴림도 +1");
            Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, true, Default));
            Assert.AreEqual(5, fatigue, "공격 3 + 포획 2 = 5번 연속");
            Assert.AreEqual(3, FatigueRules.Roll(ref fatigue, true, Default), "피로 상태의 포획 굴림도 3 피해");
        }

        [Test]
        public void A10_포획피로끔이면공격굴림만센다()
        {
            var off = new RuleOptions { CaptureAddsFatigue = false };

            int fatigue = 0;
            for (int i = 0; i < 3; i++) FatigueRules.Roll(ref fatigue, false, off);
            Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, true, off));
            Assert.AreEqual(3, fatigue, "포획 굴림은 세지 않고 연속도 끊지 않는다");
            FatigueRules.Roll(ref fatigue, false, off);
            FatigueRules.Roll(ref fatigue, false, off);
            Assert.AreEqual(5, fatigue, "공격 5번");
            Assert.AreEqual(0, FatigueRules.Roll(ref fatigue, true, off), "피로 상태여도 포획 굴림은 피해 없음");
            Assert.AreEqual(3, FatigueRules.Roll(ref fatigue, false, off), "공격 굴림은 3 피해");
        }
    }
}
