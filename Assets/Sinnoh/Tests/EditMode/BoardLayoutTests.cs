using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M4-2b(d3-3 §2-1 ①): board.json 351칸의 헥스 프리즘 층 띠(BoardLayout)가 원작 조각과 같다 — 윗면 높이·기둥 틴트 층·밑면.
    /// 엔진 없이 돈다(dotnet = Unity). 메시가 이 띠대로 그려졌는지는 PlayMode `M4_보드칸351과높이`가 본다.
    /// </summary>
    public class BoardLayoutTests
    {
        Board board;
        IReadOnlyList<CellLook> looks;

        [OneTimeSetUp]
        public void LoadOnce() => board = BoardLoader.Load(TestData.ReadText("board.json"), out looks);

        [Test]
        public void M4_칸프리즘층은기둥과덮개_윗면은층곱하기078()
        {
            Assert.AreEqual(351, board.Count);
            int main = 0, distortion = 0, bands = 0;
            var pillarTints = new HashSet<string>();
            var allTints = new HashSet<string>();
            var patterns = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < board.Count; i++)
            {
                var cell = board[i];
                var look = looks[i];
                var b = BoardLayout.Bands(cell, look);
                string at = cell.Coord.ToString();
                Assert.AreEqual(look.Pillars.Count + 1, b.Length, at);
                Assert.AreEqual(cell.LevelQuarters * 0.78m / 4, b[b.Length - 1].Top, at + " 윗면 = level × 0.78");
                Assert.AreEqual(look.Tint, b[b.Length - 1].Tint, at + " 덮개 틴트");
                for (int k = 0; k < b.Length; k++)
                {
                    Assert.Less(b[k].Bottom, b[k].Top, at);
                    if (k > 0) Assert.AreEqual(b[k - 1].Top, b[k].Bottom, at + " 띠가 이어진다");
                    if (k < b.Length - 1)
                    {
                        Assert.AreEqual(0.78m, b[k].Top - b[k].Bottom, at + " 기둥 두께");
                        Assert.AreEqual(look.Pillars[k], b[k].Tint, at + " 기둥 틴트 아래 → 위");
                    }
                }
                if (cell.DistortionWorld)
                {
                    // 원작: 기둥 하나 + 얇은 덮개가 떠 있다(바닥판 위, 울트라 버스트에서 +5 올림)
                    distortion++;
                    Assert.AreEqual(1, look.Pillars.Count, at);
                    Assert.AreEqual(0.78m * (cell.Floor - 1) - 0.04m, b[0].Bottom, at);
                    Assert.AreEqual(0.04m, b[1].Top - b[1].Bottom, at + " 얇은 덮개");
                }
                else
                {
                    main++;
                    Assert.AreEqual(-0.04m, b[0].Bottom, at + " 본맵은 탁자 위");
                }
                bands += b.Length;
                foreach (var p in look.Pillars) { pillarTints.Add(p); allTints.Add(p); }
                allTints.Add(look.Tint);
                string key = (cell.DistortionWorld ? "DW " : "") + "L" + (cell.LevelQuarters / 4m).ToString(CultureInfo.InvariantCulture) + " 기둥" + look.Pillars.Count;
                patterns[key] = patterns.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            Assert.AreEqual(326, main);
            Assert.AreEqual(25, distortion);
            // 원작 조각 634 = 띠 631 + 호수 덮개 밑 얇은 조각 3(덮개에 합침)
            Assert.AreEqual(631, bands);
            Assert.AreEqual(16, pillarTints.Count, "기둥 틴트");
            Assert.AreEqual(45, allTints.Count, "덮개 37 + 기둥에만 있는 8");
            var want = new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["L0 기둥0"] = 112, ["L0.75 기둥0"] = 3, ["L1 기둥1"] = 92, ["L1.25 기둥1"] = 52, ["L2 기둥1"] = 35, ["L2.25 기둥2"] = 3,
                ["L3 기둥2"] = 21, ["L4 기둥3"] = 5, ["L5 기둥4"] = 2, ["L6 기둥5"] = 1,
                ["DW L2 기둥1"] = 6, ["DW L3 기둥1"] = 16, ["DW L4 기둥1"] = 3,
            };
            CollectionAssert.AreEqual(want, patterns);
        }

        [Test]
        public void M4_기둥틴트형식은엄격()
        {
            string bd = TestData.ReadText("board.json");
            Assert.That(bd, Does.Contain("\"pillars\":[\"#7d647dff\"]"));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"pillars\":[\"#7d647dff\"]", "\"pillars\":[\"#7d647d\"]")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"pillars\":[\"#7d647dff\"]", "\"pillars\":[null]")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"pillars\":[\"#7d647dff\"]", "\"pillars\":\"#7d647dff\"")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, ",\"pillars\":[\"#7d647dff\"]", "")));
        }

        static string ReplaceFirst(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }
    }
}
