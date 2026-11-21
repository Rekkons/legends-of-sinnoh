using System;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class RngTests
    {
        static uint[] First(Pcg32 rng, int count)
        {
            var v = new uint[count];
            for (int i = 0; i < count; i++) v[i] = rng.NextUInt();
            return v;
        }

        [Test]
        public void M1_PCG32참조값_시드42_수열54()
        {
            // pcg-c-basic pcg32-demo: pcg32_srandom_r(&rng, 42u, 54u) 뒤 pcg32_random_r 6번. dotnet과 Unity가 같은 값을 내야 한다
            CollectionAssert.AreEqual(new uint[] { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e },
                First(new Pcg32(42, 54), 6));
        }

        [Test]
        public void M1_시드42수열54주사위열고정()
        {
            // pcg32_boundedrand_r(6)와 같은 거부 방식. 바뀌면 세이브 리플레이가 어긋난다
            var a = new Pcg32(42, 54);
            var idx = new int[12];
            for (int i = 0; i < idx.Length; i++) idx[i] = a.Next(AttackDice.Faces);
            CollectionAssert.AreEqual(new[] { 3, 3, 2, 1, 1, 4, 5, 3, 0, 2, 0, 1 }, idx);

            var faces = AttackDice.Roll(new Pcg32(42, 54), 9);
            var values = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++) values[i] = (int)faces[i];
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1, 1, 2, 1, 0 }, values);
        }

        [Test]
        public void M1_같은시드같은수열_복사하면이어짐()
        {
            var a = new Pcg32(20261106, 3);
            var b = new Pcg32(20261106, 3);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Next(1000), b.Next(1000), $"{i}번째");

            var c = new Pcg32(0);
            c.CopyFrom(a);
            CollectionAssert.AreEqual(First(a, 100), First(c, 100), "CopyFrom 뒤 같은 수열");

            CollectionAssert.AreNotEqual(First(new Pcg32(1, 3), 4), First(new Pcg32(2, 3), 4), "시드가 다르면");
            CollectionAssert.AreNotEqual(First(new Pcg32(1, 3), 4), First(new Pcg32(1, 4), 4), "수열이 다르면");
        }

        [Test]
        public void M1_Next는0이상상한미만()
        {
            var rng = new Pcg32(7);
            foreach (int bound in new[] { 1, 2, 6, 7, 1000, int.MaxValue })
                for (int i = 0; i < 1000; i++)
                {
                    int v = rng.Next(bound);
                    Assert.That(v, Is.InRange(0, bound - 1), $"Next({bound})");
                }
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(-1));
        }

        [Test]
        public void M1_ScriptedRng는대본대로_모자라면예외()
        {
            var rng = new ScriptedRng(new[] { 3, 0 }, new[] { AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol });
            Assert.AreEqual(3, rng.Next(6));
            Assert.AreEqual(AttackDieFace.CriticalHit, rng.RollAttackDie());
            Assert.AreEqual(0, rng.Next(2));
            Assert.IsFalse(rng.Exhausted);
            Assert.AreEqual(AttackDieFace.StatusSymbol, rng.RollAttackDie());
            Assert.IsTrue(rng.Exhausted);
            Assert.Throws<InvalidOperationException>(() => rng.Next(6), "Next 대본 끝");
            Assert.Throws<InvalidOperationException>(() => rng.RollAttackDie(), "주사위 대본 끝");

            CollectionAssert.AreEqual(new[] { AttackDieFace.RegularHit, AttackDieFace.CriticalHit },
                AttackDice.Roll(ScriptedRng.Dice(AttackDieFace.RegularHit, AttackDieFace.CriticalHit), 2));
            Assert.Throws<InvalidOperationException>(() => ScriptedRng.Ints(6).Next(6), "대본 값이 상한 이상");
            Assert.Throws<InvalidOperationException>(() => AttackDice.Roll(ScriptedRng.Dice(AttackDieFace.RegularHit), 2), "예상보다 많이 굴림");
        }
    }
}
