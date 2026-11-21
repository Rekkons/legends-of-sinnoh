using NUnit.Framework;
using Sinnoh.Rules;
using static Sinnoh.Rules.AttackDieFace;
using static Sinnoh.Rules.PokemonType;

namespace Sinnoh.Tests.EditMode
{
    public class DamageTests
    {
        static readonly DamageCapStage Default = new RuleOptions().DamageCapStage;

        static int Sum(AttackDieFace[] faces)
        {
            int s = 0;
            foreach (var f in faces) s += (int)f;
            return s;
        }

        static int Count(AttackDieFace[] faces, AttackDieFace face)
        {
            int n = 0;
            foreach (var f in faces) if (f == face) n++;
            return n;
        }

        [Test]
        public void S7_3_불대문자예시_데미지6()
        {
            // 룰북 p.10: 부스터(불꽃)의 불대문자(불꽃, 공격력 5) → 야생 리피아(풀). 상태 2 · 일반 2 · 크리 1
            var rng = ScriptedRng.Dice(StatusSymbol, StatusSymbol, RegularHit, RegularHit, CriticalHit);
            var faces = AttackDice.Roll(rng, DamageRules.AttackStrength(5, 0));
            Assert.IsTrue(rng.Exhausted, "공격력 5 = 주사위 5개");
            Assert.AreEqual(4, Sum(faces), "주사위 합 4");
            Assert.AreEqual(2, Count(faces, StatusSymbol), "불대문자 효과(심볼 2개 이상이면 화상) 조건");

            int damage = DamageRules.Calculate(new DamageInput
            {
                DiceSum = Sum(faces),
                MoveType = Fire,
                DefenderType1 = Grass,
            }, Default);
            Assert.AreEqual(6, damage, "4 + 상성 2");
        }

        [Test]
        public void S7_3_리프블레이드예시_데미지2()
        {
            // 룰북 p.11: 화상 입은 리피아(풀)의 리프블레이드(풀, 공격력 4, 「크리티컬 히트는 +1」) → 부스터(불꽃). 일반 3 · 크리 1
            var rng = ScriptedRng.Dice(RegularHit, RegularHit, RegularHit, CriticalHit);
            var faces = AttackDice.Roll(rng, DamageRules.AttackStrength(4, 0));
            Assert.IsTrue(rng.Exhausted);
            Assert.AreEqual(5, Sum(faces), "주사위 합 5");

            int damage = DamageRules.Calculate(new DamageInput
            {
                DiceSum = Sum(faces),
                EffectDamage = Count(faces, CriticalHit), // 크리마다 +1
                MoveType = Grass,
                DefenderType1 = Fire,
                Reduction = 2, // 공격하는 쪽 화상
            }, Default);
            Assert.AreEqual(2, damage, "5 + 효과 1 − 상성 2 − 화상 2");
        }

        [Test]
        public void S7_3_주사위0이어도상성과효과피해는들어감()
        {
            var input = new DamageInput { DiceSum = 0, EffectDamage = 1, MoveType = Water, DefenderType1 = Fire };
            Assert.AreEqual(3, DamageRules.Calculate(input, Default), "0 + 효과 1 + 상성 2");

            input = new DamageInput { DiceSum = 0, MoveType = Fire, DefenderType1 = Water, DefenderType2 = Rock };
            Assert.AreEqual(0, DamageRules.Calculate(input, Default), "0 − 4는 0");
        }

        [Test]
        public void S7_3_두배는상성뒤_화상은두배뒤()
        {
            // FAQ p.13: (주사위 + 효과 + 상성) × 2, 화상·프리즘 아머는 2배한 뒤 뺀다
            var input = new DamageInput { DiceSum = 3, EffectDamage = 1, MoveType = Fire, DefenderType1 = Grass, Double = true, Reduction = 2 };
            Assert.AreEqual(10, DamageRules.Calculate(input, Default), "(3 + 1 + 2) × 2 − 2");
        }

        [Test]
        public void S7_3_공격력은1에서9()
        {
            Assert.AreEqual(5, DamageRules.AttackStrength(5, 0));
            Assert.AreEqual(7, DamageRules.AttackStrength(5, 2), "빙결 대상 +2");
            Assert.AreEqual(1, DamageRules.AttackStrength(2, -3), "최소 1");
            Assert.AreEqual(9, DamageRules.AttackStrength(8, 3), "최대 9");
        }

        [Test]
        public void S7_4_타입없음공격은상성0()
        {
            var input = new DamageInput { DiceSum = 3, MoveType = Typeless, DefenderType1 = Ghost, DefenderType2 = Rock };
            Assert.AreEqual(3, DamageRules.Calculate(input, Default));
        }

        [Test]
        public void G4_상한은맨마지막()
        {
            Assert.AreEqual(DamageCapStage.Last, Default, "기본값 = rulings G4");

            // 보호 장비(상한 6) + 공격하는 쪽 화상: (5 + 2) × 2 = 14 → −2 = 12 → 상한 6
            var input = new DamageInput { DiceSum = 5, MoveType = Fire, DefenderType1 = Grass, Double = true, Reduction = 2, Cap = 6 };
            Assert.AreEqual(6, DamageRules.Calculate(input, DamageCapStage.Last));

            // 깜찍이들(상한 3) + 회피 기동(2 이하 무효): 상한은 문턱 뒤
            input = new DamageInput { DiceSum = 9, NullifyAtOrBelow = 2, Cap = 3 };
            Assert.AreEqual(3, DamageRules.Calculate(input, DamageCapStage.Last));
            input = new DamageInput { DiceSum = 9, NullifyAtOrBelow = 2, Cap = 2 }; // 상한이 문턱 앞이면 0이 된다
            Assert.AreEqual(2, DamageRules.Calculate(input, DamageCapStage.Last));
        }

        [Test]
        public void G4_상한감소전옵션이면감소를상한뒤에뺀다()
        {
            var input = new DamageInput { DiceSum = 5, MoveType = Fire, DefenderType1 = Grass, Double = true, Reduction = 2, Cap = 6 };
            Assert.AreEqual(4, DamageRules.Calculate(input, DamageCapStage.BeforeReduction), "14 → 상한 6 → −2");

            // 깜찍이들(상한 3) + 회피 기동(2 이하 무효) + 화상
            input = new DamageInput { DiceSum = 5, Reduction = 2, NullifyAtOrBelow = 2, Cap = 3 };
            Assert.AreEqual(0, DamageRules.Calculate(input, DamageCapStage.BeforeReduction), "5 → 상한 3 → −2 = 1 → 문턱 0");
            Assert.AreEqual(3, DamageRules.Calculate(input, DamageCapStage.Last), "5 − 2 = 3 → 문턱 위 → 상한 3");
        }

        [Test]
        public void G4_문턱은감소뒤_상한전()
        {
            // 회피 기동: 비행 포켓몬은 2 이하의 공격에 피해를 입지 않는다
            var input = new DamageInput { DiceSum = 3, MoveType = Normal, DefenderType1 = Flying, NullifyAtOrBelow = 2 };
            Assert.AreEqual(3, DamageRules.Calculate(input, Default), "3은 문턱 위");

            input.Reduction = 2; // 공격하는 쪽 화상
            Assert.AreEqual(0, DamageRules.Calculate(input, Default), "3 − 2 = 1 → 문턱 0(감소 뒤에 본다)");

            input = new DamageInput { DiceSum = 2, Double = true, Reduction = 1, NullifyAtOrBelow = 2 };
            Assert.AreEqual(3, DamageRules.Calculate(input, Default), "2 × 2 − 1 = 3 → 문턱 위");

            input = new DamageInput { DiceSum = 2, NullifyAtOrBelow = 0 };
            Assert.AreEqual(2, DamageRules.Calculate(input, Default), "문턱 0 = 없음");
        }
    }
}
