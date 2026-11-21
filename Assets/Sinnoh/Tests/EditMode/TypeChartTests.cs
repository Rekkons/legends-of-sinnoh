using System;
using NUnit.Framework;
using Sinnoh.Rules;
using static Sinnoh.Rules.PokemonType;

namespace Sinnoh.Tests.EditMode
{
    public class TypeChartTests
    {
        static PokemonType[] AllTypes => (PokemonType[])Enum.GetValues(typeof(PokemonType));

        [Test]
        public void S7_4_약점2_저항마이너스2_무효마이너스4()
        {
            Assert.AreEqual(2, (int)Effectiveness.SuperEffective);
            Assert.AreEqual(-2, (int)Effectiveness.NotVeryEffective);
            Assert.AreEqual(-4, (int)Effectiveness.NotEffective);
            Assert.AreEqual(0, (int)Effectiveness.Neutral);

            Assert.AreEqual(Effectiveness.SuperEffective, TypeChart.Of(Fire, Grass));
            Assert.AreEqual(Effectiveness.NotVeryEffective, TypeChart.Of(Grass, Fire));
            Assert.AreEqual(Effectiveness.NotEffective, TypeChart.Of(Normal, Ghost));
            Assert.AreEqual(Effectiveness.Neutral, TypeChart.Of(Fire, Normal));
            Assert.AreEqual(2, TypeChart.Bonus(Fire, Grass));
            Assert.AreEqual(-2, TypeChart.Bonus(Grass, Fire));
            Assert.AreEqual(-4, TypeChart.Bonus(Ground, Flying));
        }

        [Test]
        public void S7_4_복합타입합산()
        {
            Assert.AreEqual(4, TypeChart.Bonus(Ice, Dragon, Ground), "얼음 → 드래곤·땅: +2 +2");
            Assert.AreEqual(4, TypeChart.Bonus(Grass, Water, Ground), "풀 → 물·땅: +2 +2");
            Assert.AreEqual(-2, TypeChart.Bonus(Electric, Water, Ground), "전기 → 물·땅: +2 −4");
            Assert.AreEqual(0, TypeChart.Bonus(Fire, Water, Grass), "불꽃 → 물·풀: −2 +2");
            Assert.AreEqual(-4, TypeChart.Bonus(Grass, Fire, Flying), "풀 → 불꽃·비행: −2 −2");

            // 순서 무관, 단일 타입은 둘째 칸 Typeless(0)
            foreach (var a in AllTypes)
                foreach (var d1 in AllTypes)
                {
                    Assert.AreEqual((int)TypeChart.Of(a, d1), TypeChart.Bonus(a, d1), $"{a} → {d1} 단일");
                    foreach (var d2 in AllTypes)
                        Assert.AreEqual(TypeChart.Bonus(a, d1, d2), TypeChart.Bonus(a, d2, d1), $"{a} → {d1}·{d2}");
                }
        }

        [Test]
        public void S7_4_타입없음은상성무시()
        {
            foreach (var d1 in AllTypes)
                foreach (var d2 in AllTypes)
                    Assert.AreEqual(0, TypeChart.Bonus(Typeless, d1, d2), $"타입없음 → {d1}·{d2}");
            // Lua 표에 Typeless를 방어 쪽으로 적은 칸이 없다
            foreach (var a in AllTypes)
                Assert.AreEqual(Effectiveness.Neutral, TypeChart.Of(a, Typeless), $"{a} → 타입없음");
        }

        [Test]
        public void S7_4_상성표칸수_약점51_저항61_무효8()
        {
            Assert.AreEqual(19, AllTypes.Length);
            Assert.AreEqual(AllTypes.Length, TypeChart.TypeCount);

            // raw/lua/_global.lua:12-36 행마다 적힌 칸 수(Typeless 0, Normal 3 … Fairy 6)
            int[] perRow = { 0, 3, 8, 6, 6, 10, 8, 11, 7, 8, 6, 5, 10, 7, 4, 3, 5, 7, 6 };
            int weak = 0, resist = 0, immune = 0;
            foreach (var a in AllTypes)
            {
                int row = 0;
                foreach (var d in AllTypes)
                {
                    var e = TypeChart.Of(a, d);
                    if (e == Effectiveness.SuperEffective) weak++;
                    else if (e == Effectiveness.NotVeryEffective) resist++;
                    else if (e == Effectiveness.NotEffective) immune++;
                    else Assert.AreEqual(Effectiveness.Neutral, e, $"{a} → {d}");
                    if (e != Effectiveness.Neutral) row++;
                }
                Assert.AreEqual(perRow[(int)a], row, $"{a} 행 칸 수");
            }
            Assert.AreEqual(51, weak, "WEAK");
            Assert.AreEqual(61, resist, "RESIST");
            Assert.AreEqual(8, immune, "IMMUNE");

            // 무효 8칸 전부
            var immunities = new[]
            {
                (Normal, Ghost), (Electric, Ground), (Fighting, Ghost), (Poison, Steel),
                (Ground, Flying), (Psychic, Dark), (Ghost, Normal), (Dragon, Fairy),
            };
            foreach (var (a, d) in immunities)
                Assert.AreEqual(Effectiveness.NotEffective, TypeChart.Of(a, d), $"{a} → {d}");
        }
    }
}
