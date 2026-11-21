using System;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class BoardTests
    {
        static int Count(Board b, Func<Cell, bool> p)
        {
            int n = 0;
            for (int i = 0; i < b.Count; i++)
                if (p(b[i])) n++;
            return n;
        }

        [Test]
        public void S1_1_칸수351_본맵326_깨어진세계25()
        {
            var b = TestData.LoadBoard();
            Assert.AreEqual(351, b.Count);
            Assert.AreEqual(326, Count(b, c => !c.DistortionWorld));
            Assert.AreEqual(25, Count(b, c => c.DistortionWorld));
        }

        [Test]
        public void S1_1_야생칸343()
        {
            var b = TestData.LoadBoard();
            Assert.AreEqual(343, Count(b, c => c.Wild));
            Assert.AreEqual(24, Count(b, c => c.Wild && c.DistortionWorld), "깨어진 세계 25칸 중 (-10,16)만 야생이 아니다");
        }

        [Test]
        public void S3_1_깨어진세계와본맵인접0()
        {
            var b = TestData.LoadBoard();
            int pairs = 0, cross = 0;
            for (int i = 0; i < b.Count; i++)
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = b.Neighbor(i, d);
                    if (n == Board.NoCell) continue;
                    pairs++;
                    if (b[n].DistortionWorld != b[i].DistortionWorld) cross++;
                }
            Assert.AreEqual(0, cross);
            // 이웃 표가 비면 0이 공짜로 나오므로 쌍 수도 본다(방향 있는 쌍, map_cells.csv에서 셈)
            Assert.AreEqual(1880, pairs);
        }
    }
}
