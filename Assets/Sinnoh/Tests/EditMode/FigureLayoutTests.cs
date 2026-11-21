using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M4-2c(d3-3 §2-2 ②): 피규어 33이 서는 칸(FigureLayout)을 실제 데이터(board·fixtures·pokemon + figure_list)로 본다. 엔진 없이 돈다(dotnet = Unity).
    /// 씬에서 그 자리에 섰는지·크기·분홍은 PlayMode `M4_피규어33맞춘크기와자리`·`M4_피규어근접33장분홍0과Idle재생`이 본다.
    /// </summary>
    public class FigureLayoutTests
    {
        [Test]
        public void M4_피규어자리33_고정물칸과UB맞는야생칸()
        {
            var ctx = TestData.LoadCtx();
            var board = BoardLoader.Load(TestData.ReadText("board.json"), out var looks);
            string list = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/assets/tables/figure_list.json"), new UTF8Encoding(false));
            var rows = Regex.Matches(list, "\\{\"dex\":([0-9]+),\"state\":([0-9]+),\"group\":\"([A-Za-z]+)\"").Cast<Match>()
                .Select(m => (key: m.Groups[1].Value + ":" + m.Groups[2].Value, group: m.Groups[3].Value)).ToList();
            Assert.AreEqual(37, rows.Count, "figure_list = 33종 + 폼 4");

            var spots = FigureLayout.Place(board, looks, ctx.Fixtures, ctx.Cards, rows.Select(r => r.key).ToList(), rows.Select(r => r.group).ToList());
            Assert.AreEqual(33, spots.Count, "판에 서는 피규어 33(결정 31 ③)");
            CollectionAssert.AreEquivalent(new[] { "800:2", "800:3", "800:4", "487:2" }, rows.Select(r => r.key).Except(spots.Select(s => s.Key)), "폼 모델 4는 판에 서지 않는다(d3-3 §2-2 [가정 M4-2a])");
            CollectionAssert.AreEqual(rows.Select(r => r.key).Where(k => spots.Any(s => s.Key == k)), spots.Select(s => s.Key), "결과 순서 = 목록 순서");

            var fixtures = ctx.Fixtures.Fixtures;
            var group = rows.ToDictionary(r => r.key, r => r.group);
            foreach (var s in spots)
            {
                var cell = board[s.Cell];
                string at = s.Key + " @" + cell.Coord;
                switch (group[s.Key])
                {
                    case "LegendarySite":
                    case "LegendaryShrine":
                        var kind = group[s.Key] == "LegendarySite" ? FixtureKind.LegendarySite : FixtureKind.LegendaryShrine;
                        int p = ctx.Cards.PokemonIds.IndexOf(s.Key);
                        Assert.IsTrue(fixtures.Any(f => f.Kind == kind && f.Cell == s.Cell && f.Legendary.Contains(p)), at + ": 그 종을 가진 " + kind + " 칸");
                        break;
                    case "Taxi":
                        Assert.IsTrue(fixtures.Any(f => f.Kind == FixtureKind.Taxi && f.Cell == s.Cell), at + ": 택시 칸");
                        break;
                    case "UltraBurst":
                        Assert.AreEqual("800:1", s.Key, "네크로즈마 1페이즈만");
                        Assert.IsTrue(cell.DistortionWorld, at + ": 깨어진 세계");
                        Assert.AreEqual(FigureLayout.DistortionCenter(board, looks), s.Cell);
                        break;
                    case "UltraBeast":
                        Assert.IsTrue(FigureLayout.UltraBeastHabitat(s.Key, out var b, out var c), at);
                        Assert.IsTrue(cell.Wild && !cell.DistortionWorld, at + ": 본맵 야생 칸");
                        Assert.AreEqual(b, cell.Biome, at + " 지형");
                        Assert.AreEqual(c, cell.Climate, at + " 기후");
                        Assert.IsFalse(fixtures.Any(f => f.Cell == s.Cell), at + ": 고정물 없는 칸");
                        break;
                    default: Assert.Fail(at + " group " + group[s.Key]); break;
                }
                // 자리 = 칸 윗면 + 칸 안 자리(안접원 안)
                Assert.AreEqual(BoardLayout.TopY(cell), s.Y, at);
                decimal dx = s.X - looks[s.Cell].X, dz = s.Z - looks[s.Cell].Z;
                FigureLayout.SlotOffset(s.Slot, out decimal wx, out decimal wz);
                Assert.AreEqual(wx, dx, at); Assert.AreEqual(wz, dz, at);
                Assert.Less(dx * dx + dz * dz, 0.9526m * 0.9526m, at + ": 칸 안접원 안");
            }

            var ub = spots.Where(s => group[s.Key] == "UltraBeast").ToList();
            Assert.AreEqual(9, ub.Count, "UB 9");
            Assert.AreEqual(9, ub.Select(s => s.Cell).Distinct().Count(), "UB 칸이 겹치지 않는다");
            Assert.AreEqual(9, ub.Select(s => board[s.Cell].Region).Distinct().Count(), "UB마다 다른 지역(원작 claimed_wild_area_descriptions)");
            Assert.AreEqual(12, spots.Count(s => group[s.Key] == "LegendarySite"), "전설 장소 12(11 + 신오신전 실버디)");
            Assert.AreEqual(12, spots.Where(s => group[s.Key] == "LegendarySite").Select(s => s.Cell).Distinct().Count(), "전설 장소마다 하나");
            Assert.AreEqual(10, spots.Count(s => group[s.Key] == "LegendaryShrine"), "전설 사당의 전설 10");
            Assert.AreEqual(7, spots.Where(s => group[s.Key] == "LegendaryShrine").Select(s => s.Cell).Distinct().Count(), "전설 사당 7칸");

            // 한 칸 여럿: 시작의 사당 3신(0·2·4), 달빛공터 2(0·3), 나머지는 가운데
            var byCell = spots.GroupBy(s => s.Cell).ToDictionary(g => g.Key, g => g.Select(s => s.Slot).ToArray());
            Assert.AreEqual(new[] { 0, 2, 4 }, byCell[Cell(board, "0,2")], "시작의 사당 디아루가·펄기아·기라티나");
            Assert.AreEqual(new[] { 0, 3 }, byCell[Cell(board, "-5,3")], "달빛공터 다크라이·크레세리아");
            Assert.AreEqual(30, byCell.Count, "피규어가 선 칸 30 = 33 − 2(3신) − 1(달빛공터)");
            Assert.AreEqual(28, byCell.Count(kv => kv.Value.Length == 1 && kv.Value[0] == -1), "혼자 선 칸은 가운데");

            // 같은 입력·시드면 같은 자리(결정성)
            var again = FigureLayout.Place(board, looks, ctx.Fixtures, ctx.Cards, rows.Select(r => r.key).ToList(), rows.Select(r => r.group).ToList());
            CollectionAssert.AreEqual(spots.Select(s => s.Cell), again.Select(s => s.Cell));
        }

        [Test]
        public void M4_칸안자리6은반지름05_한칸n개는고르게()
        {
            var seen = new HashSet<(decimal, decimal)>();
            for (int k = 0; k < 6; k++)
            {
                FigureLayout.SlotOffset(k, out decimal x, out decimal z);
                Assert.AreEqual(0.25m, decimal.Round(x * x + z * z, 12), "자리 " + k + " 반지름 0.5");
                Assert.IsTrue(seen.Add((x, z)), "자리가 겹치지 않는다");
            }
            FigureLayout.SlotOffset(-1, out decimal cx, out decimal cz);
            Assert.AreEqual(0m, cx); Assert.AreEqual(0m, cz);
            Assert.AreEqual(-1, FigureLayout.SlotFor(0, 1));
            Assert.AreEqual(new[] { 0, 3 }, new[] { FigureLayout.SlotFor(0, 2), FigureLayout.SlotFor(1, 2) });
            Assert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, Enumerable.Range(0, 6).Select(i => FigureLayout.SlotFor(i, 6)).ToArray());
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FigureLayout.SlotFor(0, 7));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FigureLayout.SlotOffset(6, out _, out _));
        }

        static int Cell(Board board, string key)
        {
            var p = key.Split(',');
            Assert.IsTrue(board.TryGetIndex(new HexCoord(int.Parse(p[0]), int.Parse(p[1])), out int i), key);
            return i;
        }
    }
}
