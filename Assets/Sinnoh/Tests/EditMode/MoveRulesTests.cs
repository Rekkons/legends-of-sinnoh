using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class MoveRulesTests
    {
        /// <summary>(0, row) 칸. levelQuarters = level × 4(숲 1.25 = 5, 호수 0.75 = 3).</summary>
        static Cell At(int row, int levelQuarters) =>
            new Cell(new HexCoord(0, row), levelQuarters, Biome.None, Climate.None, true, false, null);

        /// <summary>세로 한 줄 (0,0)-(0,2)-(0,4)…. 칸 i와 i+1이 이웃이다(방향 (0,+2)).</summary>
        static Board Line(params int[] levelQuarters)
        {
            var cells = new Cell[levelQuarters.Length];
            for (int i = 0; i < cells.Length; i++) cells[i] = At(2 * i, levelQuarters[i]);
            return new Board(cells);
        }

        [Test]
        public void G12_결정71_길은한칸씩이어지고이동력안_높이로막힌걸음은거절()
        {
            var mode = ElevationMode.Floor;
            var b = Line(4, 4, 4, 4, 12);   // 마지막 칸은 산 3층(1층에서 못 오름)
            Assert.IsTrue(MoveRules.PathAllows(b, 0, new[] { 1, 2, 3 }, 3, mode));
            Assert.IsTrue(MoveRules.PathAllows(b, 0, new[] { 1, 0, 1 }, 3, mode), "되밟아도 된다(걸음마다 1)");
            Assert.IsFalse(MoveRules.PathAllows(b, 0, new[] { 1, 2, 3 }, 2, mode), "이동력을 넘는다");
            Assert.IsFalse(MoveRules.PathAllows(b, 0, new[] { 2 }, 3, mode), "이웃이 아니다");
            Assert.IsFalse(MoveRules.PathAllows(b, 2, new[] { 3, 4 }, 3, mode), "층 차 2");
            Assert.IsFalse(MoveRules.PathAllows(b, 0, new int[0], 3, mode), "빈 길");
        }

        [Test]
        public void G12_결정71_경유칸길은구간마다최단으로잇고_이동력을넘으면null()
        {
            var mode = ElevationMode.Floor;
            var b = Line(4, 4, 4, 4, 4);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, MoveRules.Route(b, 0, null, 2, 3, mode), "경유 칸 없음 = Path");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 2 }, MoveRules.Route(b, 1, new[] { 3 }, 2, 3, mode), "3을 거쳐 2로(최단이 아닌 길)");
            Assert.IsNull(MoveRules.Route(b, 1, new[] { 4 }, 2, 3, mode), "1 → 4 → 2 는 5걸음");
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, MoveRules.Route(b, 0, new[] { 1, 0 }, 1, 3, mode), "경유 칸 둘(되밟기)");
        }

        [Test]
        public void S3_1_기본이동력3()
        {
            Assert.AreEqual(3, MoveRules.BaseMovement);
            var b = Line(4, 4, 4, 4, 4); // 평원 1층 다섯 칸
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, MoveRules.Unreachable },
                MoveRules.Reachable(b, 0, MoveRules.BaseMovement, ElevationMode.Floor));
        }

        [Test]
        public void A6_층은floor_level()
        {
            Assert.AreEqual(0, At(0, 0).Floor, "바다 0");
            Assert.AreEqual(0, At(0, 3).Floor, "호수 0.75");
            Assert.AreEqual(1, At(0, 4).Floor, "평원 1");
            Assert.AreEqual(1, At(0, 5).Floor, "숲 1.25");
            Assert.AreEqual(2, At(0, 9).Floor, "천관숲 2.25");
            Assert.AreEqual(3, At(0, 12).Floor, "산 3");
        }

        [Test]
        public void A6_층차2이상이동불가()
        {
            var mode = new RuleOptions().ElevationMode;
            Assert.AreEqual(ElevationMode.Floor, mode, "기본값 = rulings A6");

            Assert.IsTrue(MoveRules.ElevationAllows(At(0, 4), At(2, 8), mode), "1층 → 2층");
            Assert.IsTrue(MoveRules.ElevationAllows(At(0, 8), At(2, 4), mode), "2층 → 1층");
            Assert.IsFalse(MoveRules.ElevationAllows(At(0, 4), At(2, 12), mode), "1층 → 3층");
            Assert.IsFalse(MoveRules.ElevationAllows(At(0, 12), At(2, 4), mode), "3층 → 1층(내리기도 막힘)");
            Assert.IsFalse(MoveRules.ElevationAllows(At(0, 5), At(2, 12), mode), "숲 1.25 → 산 3: level 차 1.75지만 층 차 2");
            Assert.IsFalse(MoveRules.ElevationAllows(At(0, 3), At(2, 8), mode), "호수 0.75 → 산 2: level 차 1.25지만 층 차 2");

            // 절벽 너머는 이동력이 남아도 못 간다
            CollectionAssert.AreEqual(new[] { 0, 1, MoveRules.Unreachable, MoveRules.Unreachable },
                MoveRules.Reachable(Line(4, 4, 12, 12), 0, MoveRules.BaseMovement, mode));
        }

        [Test]
        public void A6_RawDiff와갈리는방향24()
        {
            var b = TestData.LoadBoard();
            int floorBlocked = 0, rawBlocked = 0, differ = 0;
            for (int i = 0; i < b.Count; i++)
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = b.Neighbor(i, d);
                    if (n == Board.NoCell) continue;
                    bool floor = MoveRules.ElevationAllows(b[i], b[n], ElevationMode.Floor);
                    bool raw = MoveRules.ElevationAllows(b[i], b[n], ElevationMode.RawDiff);
                    if (!floor) floorBlocked++;
                    if (!raw) rawBlocked++;
                    if (floor == raw) continue;
                    differ++;
                    Assert.IsTrue(raw, $"{b[i].Coord}→{b[n].Coord}: RawDiff만 막는 방향은 없어야 한다");
                }
            Assert.AreEqual(24, differ, "rulings A6: 숲 1.25↔산 3, 호수 0.75↔산 2");
            // 막힌 방향 수(방향 있는 쌍 1880개 중, board.json에서 셈). RawDiff 막힘 ⊂ Floor 막힘이라 122 − 98 = 24
            Assert.AreEqual(122, floorBlocked);
            Assert.AreEqual(98, rawBlocked);

            // RawDiff 쪽: 숲 1.25 → 산 3은 지나가고 1 → 3(차 2.0)은 막힌다
            Assert.IsTrue(MoveRules.ElevationAllows(At(0, 5), At(2, 12), ElevationMode.RawDiff));
            Assert.IsFalse(MoveRules.ElevationAllows(At(0, 4), At(2, 12), ElevationMode.RawDiff));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 },
                MoveRules.Reachable(Line(5, 12, 12), 0, MoveRules.BaseMovement, ElevationMode.RawDiff));
        }
    }
}
