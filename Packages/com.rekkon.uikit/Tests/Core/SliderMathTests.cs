using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class SliderMathTests
    {
        [Test]
        public void M4_슬라이더손잡이는양끝사이를정수자리로움직인다()
        {
            // 홈 120, 손잡이 8, 0~10: 움직이는 폭 112
            Assert.AreEqual(0, SliderMath.KnobLeft(0, 120, 8, 10));
            Assert.AreEqual(112, SliderMath.KnobLeft(10, 120, 8, 10));
            Assert.AreEqual(56, SliderMath.KnobLeft(5, 120, 8, 10));
            Assert.AreEqual(34, SliderMath.KnobLeft(3, 120, 8, 10), "33.6 → 34");
            Assert.AreEqual(112, SliderMath.KnobLeft(99, 120, 8, 10), "값은 잘린다");
            Assert.AreEqual(0, SliderMath.KnobLeft(-1, 120, 8, 10));
        }

        [Test]
        public void M4_슬라이더는누른자리에서가장가까운단계를고른다()
        {
            Assert.AreEqual(0, SliderMath.ValueAt(0, 120, 8, 10));
            Assert.AreEqual(0, SliderMath.ValueAt(-50, 120, 8, 10), "홈 밖 왼쪽");
            Assert.AreEqual(10, SliderMath.ValueAt(120, 120, 8, 10));
            Assert.AreEqual(10, SliderMath.ValueAt(999, 120, 8, 10), "홈 밖 오른쪽");
            Assert.AreEqual(5, SliderMath.ValueAt(60, 120, 8, 10), "가운데");
            // 손잡이의 가운데를 누르면 그 값 그대로다
            for (int v = 0; v <= 10; v++)
                Assert.AreEqual(v, SliderMath.ValueAt(SliderMath.KnobLeft(v, 120, 8, 10) + 4, 120, 8, 10), "값 " + v);
        }

        [Test]
        public void M4_슬라이더는폭이없거나단계가없으면0이다()
        {
            Assert.AreEqual(0, SliderMath.ValueAt(50, 0, 8, 10));
            Assert.AreEqual(0, SliderMath.ValueAt(50, float.NaN, 8, 10), "배치 전");
            Assert.AreEqual(0, SliderMath.KnobLeft(5, float.NaN, 8, 10));
            Assert.AreEqual(0, SliderMath.ValueAt(50, 120, 8, 0));
            Assert.AreEqual(0, SliderMath.KnobLeft(5, 120, 8, 0));
        }
    }
}
