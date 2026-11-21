using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M4-2d(d3-3 §2-3 ③, 04 §3-1): 플레이어 말의 칸 안 자리(PieceLayout)를 실제 board·fixtures로 본다. 엔진 없이 돈다(dotnet = Unity).
    /// 화면 공간 정수배·그림은 PlayMode `M4_직업그림말6은전체줌얼굴조각이고지역1배근접2배다_결정61`가 본다.
    /// </summary>
    public class PieceLayoutTests
    {
        [Test]
        public void M4_말여럿은칸안6자리로벌린다()
        {
            var board = BoardLoader.Load(TestData.ReadText("board.json"), out var looks);
            int c = Cell(board, "-8,-2"); // 운하시티(좌석 1 고향, 시험 판)
            int other = Cell(board, "-2,4"); // 영원시티
            for (int n = 1; n <= 6; n++)
            {
                var seats = Enumerable.Range(0, 6).Select(s => s < n ? c : -1).ToList();
                var spots = PieceLayout.Place(board, looks, seats);
                Assert.AreEqual(n, spots.Count, n + "명");
                CollectionAssert.AreEqual(Enumerable.Range(0, n), spots.Select(s => s.Seat), "좌석 순서");
                if (n == 1) Assert.AreEqual(-1, spots[0].Slot, "혼자면 가운데");
                else CollectionAssert.AreEqual(Enumerable.Range(0, n).Select(i => i * 6 / n), spots.Select(s => s.Slot), n + "명 → 자리 i·6/n");
                Assert.AreEqual(n, spots.Select(s => (s.X, s.Z)).Distinct().Count(), n + "명 자리가 겹치지 않는다");
                foreach (var s in spots)
                {
                    decimal dx = s.X - looks[c].X, dz = s.Z - looks[c].Z;
                    Assert.AreEqual(n == 1 ? 0m : 0.25m, decimal.Round(dx * dx + dz * dz, 12), "칸 중심에서 0 / 0.5");
                    Assert.AreEqual(BoardLayout.TopY(board[c]), s.Y, "칸 윗면");
                }
            }
            // 6명이면 6자리를 다 쓰고 이웃 자리 사이는 0.5(정육각형)
            var six = PieceLayout.Place(board, looks, Enumerable.Repeat(c, 6).ToList());
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 6), six.Select(s => s.Slot));
            decimal min = decimal.MaxValue;
            for (int a = 0; a < 6; a++)
                for (int b = a + 1; b < 6; b++)
                {
                    decimal dx = six[a].X - six[b].X, dz = six[a].Z - six[b].Z;
                    min = System.Math.Min(min, dx * dx + dz * dz);
                }
            Assert.AreEqual(0.25m, decimal.Round(min, 12), "이웃 자리 거리 0.5");

            // 칸이 다르면 저마다 가운데, 판 밖(−1)은 빠진다, 좌석 수·칸 범위는 엄격
            var mixed = PieceLayout.Place(board, looks, new[] { c, -1, other, c });
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, mixed.Select(s => s.Seat));
            CollectionAssert.AreEqual(new[] { 0, -1, 3 }, mixed.Select(s => s.Slot), "운하 2명 → 0·3, 영원 혼자 → 가운데");
            Assert.Throws<System.ArgumentException>(() => PieceLayout.Place(board, looks, Enumerable.Repeat(c, 7).ToList()));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => PieceLayout.Place(board, looks, new[] { board.Count }));

            // 시험 판 고향 = fixtures 순서 앞 6 마을(운하·장막·영원·꽃향기·선단·봉신)
            var ctx = TestData.LoadCtx();
            var homes = PieceLayout.DemoHomes(ctx.Fixtures, 6);
            CollectionAssert.AreEqual(new[] { "village.canalave", "village.nesburgh", "village.eterna", "village.sinnome", "village.snowpoint", "village.celestic" },
                homes.Select(h => ctx.Fixtures.Fixtures[h].Key));
            Assert.AreEqual(c, ctx.Fixtures.Fixtures[homes[0]].Cell);
        }

        [Test]
        public void M4_말은피규어가선칸에서피규어자리를비운다()
        {
            var board = BoardLoader.Load(TestData.ReadText("board.json"), out var looks);
            int c = Cell(board, "-2,-6"); // 장막시티 = 택시 칸(찌르호크 피규어 가운데)
            var centre = new List<FigureSpot> { new FigureSpot("398:1", c, -1, looks[c].X, BoardLayout.TopY(board[c]), looks[c].Z) };
            Assert.AreEqual(new[] { 0 }, PieceLayout.Place(board, looks, new[] { c }, centre).Select(s => s.Slot).ToArray(), "가운데 피규어 + 말 1 → 바깥 자리 0");
            Assert.AreEqual(new[] { 0, 3 }, PieceLayout.Place(board, looks, new[] { c, c }, centre).Select(s => s.Slot).ToArray(), "말 2 → 0·3");
            Assert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, PieceLayout.Place(board, looks, Enumerable.Repeat(c, 6).ToList(), centre).Select(s => s.Slot).ToArray());

            // 시작의 사당처럼 피규어가 0·2·4에 서면 말은 1·3·5부터
            var three = new[] { 0, 2, 4 }.Select(k => new FigureSpot("g" + k, c, k, 0, 0, 0)).ToList();
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, PieceLayout.FreeSlots(three, c));
            Assert.AreEqual(new[] { 1 }, PieceLayout.Place(board, looks, new[] { c }, three).Select(s => s.Slot).ToArray());
            Assert.AreEqual(new[] { 1, 3, 5 }, PieceLayout.Place(board, looks, new[] { c, c, c }, three).Select(s => s.Slot).ToArray());
            Assert.AreEqual(new[] { 0, 1, 3, 4 }, PieceLayout.Place(board, looks, new[] { c, c, c, c }, three).Select(s => s.Slot).ToArray(), "빈 자리보다 많으면 SlotFor로 벌린다");
            Assert.IsNull(PieceLayout.FreeSlots(three, Cell(board, "-8,-2")), "피규어 없는 칸");

            // 실제 피규어 자리(FigureLayout)와 같이: 택시 칸에 선 말은 가운데를 비운다
            var ctx = TestData.LoadCtx();
            var figs = FigureLayout.Place(board, looks, ctx.Fixtures, ctx.Cards, new[] { "398:1" }, new[] { "Taxi" });
            Assert.AreEqual(c, figs.Single().Cell, "택시 = 장막시티 칸");
            Assert.AreEqual(0, PieceLayout.Place(board, looks, new[] { c }, figs).Single().Slot);
        }

        static int Cell(Board board, string key)
        {
            var p = key.Split(',');
            Assert.IsTrue(board.TryGetIndex(new HexCoord(int.Parse(p[0]), int.Parse(p[1])), out int i), key);
            return i;
        }
    }
}
