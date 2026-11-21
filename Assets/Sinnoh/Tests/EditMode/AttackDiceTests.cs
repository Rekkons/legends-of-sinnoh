using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class AttackDiceTests
    {
        static long Pow6(int n)
        {
            long v = 1;
            for (int i = 0; i < n; i++) v *= 6;
            return v;
        }

        [Test]
        public void R9_공격주사위면_0_1_1_1_1_2()
        {
            var values = new int[AttackDice.Faces];
            for (int i = 0; i < values.Length; i++) values[i] = (int)AttackDice.FaceOf(i);
            CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 1, 2 }, values);
            Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.FaceOf(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.FaceOf(AttackDice.Faces));
            // 한 개: 상태 1/6 · 일반 4/6 · 크리 1/6
            CollectionAssert.AreEqual(new[] { 1, 4, 1 }, AttackDice.SumWeights(1));
            Assert.AreEqual(6, AttackDice.TotalWeight(1));
        }

        [Test]
        public void R9_3개합분포_1_12_51_88_51_12_1()
        {
            var w = AttackDice.SumWeights(3);
            CollectionAssert.AreEqual(new[] { 1, 12, 51, 88, 51, 12, 1 }, w);
            Assert.AreEqual(216, AttackDice.TotalWeight(3));
            Assert.AreEqual(10, AttackDice.Outcomes(3).Length);
            // 01 §6-3 예: 몬스터볼 3개로 포획률 3+ → 152/216
            Assert.AreEqual(152, w[3] + w[4] + w[5] + w[6]);
        }

        [Test]
        public void R9_묶음무게합은6의n승_1에서9()
        {
            long[] poly = { 1 }; // (1 + 4x + x²)^n
            for (int n = AttackDice.MinDice; n <= AttackDice.MaxDice; n++)
            {
                var next = new long[poly.Length + 2];
                for (int i = 0; i < poly.Length; i++) { next[i] += poly[i]; next[i + 1] += 4 * poly[i]; next[i + 2] += poly[i]; }
                poly = next;

                var outcomes = AttackDice.Outcomes(n);
                Assert.AreEqual((n + 1) * (n + 2) / 2, outcomes.Length, $"n={n} 묶음 수");
                var seen = new HashSet<int>();
                long total = 0;
                foreach (var o in outcomes)
                {
                    Assert.AreEqual(n, o.Dice, $"n={n} {o}");
                    Assert.Greater(o.Weight, 0, $"n={n} {o}");
                    Assert.IsTrue(seen.Add(o.StatusSymbols * 100 + o.RegularHits * 10 + o.CriticalHits), $"n={n} 겹친 묶음 {o}");
                    total += o.Weight;
                }
                Assert.AreEqual(Pow6(n), total, $"n={n} 무게 합");
                Assert.AreEqual(Pow6(n), AttackDice.TotalWeight(n), $"n={n} 분모");

                var sums = AttackDice.SumWeights(n);
                Assert.AreEqual(poly.Length, sums.Length, $"n={n} 합 칸 수");
                long m1 = 0, m2 = 0;
                for (int k = 0; k < sums.Length; k++)
                {
                    Assert.AreEqual(poly[k], sums[k], $"n={n} 합 {k}");
                    m1 += (long)k * sums[k];
                    m2 += (long)(k - n) * (k - n) * sums[k];
                }
                Assert.AreEqual(n * total, m1, $"n={n} 평균 n");
                Assert.AreEqual(n * total, 3 * m2, $"n={n} 분산 n/3");
            }
        }

        // χ² 분포 상위 0.1% 임계값, 색인 = 자유도
        static readonly double[] Chi2Critical001 =
            { double.NaN, 10.828, 13.816, 16.266, 18.467, 20.515, 22.458, 24.322, 26.124, 27.877, 29.588, 31.264, 32.909, 34.528, 36.123, 37.697, 39.252, 40.790, 42.312 };

        [Test]
        public void R9_표본빈도검정_PCG32_1에서9()
        {
            const int Rolls = 30000;
            var rng = new Pcg32(20261106, 9);
            for (int n = AttackDice.MinDice; n <= AttackDice.MaxDice; n++)
            {
                var observed = new long[2 * n + 1];
                for (int t = 0; t < Rolls; t++)
                {
                    int sum = 0;
                    foreach (var f in AttackDice.Roll(rng, n)) sum += (int)f;
                    observed[sum]++;
                }

                // 기대 빈도 5 미만인 꼬리는 이웃 칸에 합친다
                var w = AttackDice.SumWeights(n);
                long total = AttackDice.TotalWeight(n);
                var binW = new List<long>();
                var binO = new List<long>();
                long accW = 0, accO = 0;
                for (int k = 0; k < w.Length; k++)
                {
                    accW += w[k];
                    accO += observed[k];
                    if (Rolls * accW >= 5 * total) { binW.Add(accW); binO.Add(accO); accW = 0; accO = 0; }
                }
                binW[binW.Count - 1] += accW;
                binO[binO.Count - 1] += accO;

                double chi2 = 0;
                for (int i = 0; i < binW.Count; i++)
                {
                    double e = (double)Rolls * binW[i] / total;
                    double d = binO[i] - e;
                    chi2 += d * d / e;
                }
                Assert.Less(chi2, Chi2Critical001[binW.Count - 1], $"n={n} 칸 {binW.Count} χ²={chi2:F2}");
            }
        }

        [Test]
        public void R10_주사위수1에서9()
        {
            Assert.AreEqual(1, AttackDice.MinDice);
            Assert.AreEqual(9, AttackDice.MaxDice);
            var rng = new Pcg32(1);
            foreach (int bad in new[] { -1, 0, 10 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.Roll(rng, bad), $"Roll({bad})");
                Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.Outcomes(bad), $"Outcomes({bad})");
                Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.SumWeights(bad), $"SumWeights({bad})");
                Assert.Throws<ArgumentOutOfRangeException>(() => AttackDice.TotalWeight(bad), $"TotalWeight({bad})");
            }
            for (int n = 1; n <= 9; n++) Assert.AreEqual(n, AttackDice.Roll(rng, n).Length);
            Assert.AreEqual(55, AttackDice.Outcomes(9).Length, "01 §6-3: 9개면 묶음 55");
            Assert.AreEqual(10077696, AttackDice.TotalWeight(9), "6^9가 int에 들어간다");
        }
    }
}
