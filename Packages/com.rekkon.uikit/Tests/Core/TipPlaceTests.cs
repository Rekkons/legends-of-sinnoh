using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class TipPlaceTests
    {
        // 화면 1280×720, 툴팁 300×80, 간격 4
        static TipSpot Place(float tx, float ty, float tw, float th, TipSide prefer, float tipW = 300, float tipH = 80)
            => TipPlace.Place(tx, ty, tw, th, tipW, tipH, 1280, 720, prefer, 4);

        static void AssertSpot(float x, float y, TipSide side, TipSpot s)
        {
            Assert.AreEqual(side, s.Side);
            Assert.AreEqual(x, s.X);
            Assert.AreEqual(y, s.Y);
        }

        [Test]
        public void M4_들어가면고른쪽에간격만큼띄운다()
        {
            // 대상 (400, 300) 200×30
            AssertSpot(400, 216, TipSide.Above, Place(400, 300, 200, 30, TipSide.Above));
            AssertSpot(400, 334, TipSide.Below, Place(400, 300, 200, 30, TipSide.Below));
            AssertSpot(96, 300, TipSide.Left, Place(400, 300, 200, 30, TipSide.Left));
            AssertSpot(604, 300, TipSide.Right, Place(400, 300, 200, 30, TipSide.Right));
        }

        [Test]
        public void M4_화면밖으로나가면반대편으로넘긴다()
        {
            AssertSpot(400, 54, TipSide.Below, Place(400, 20, 200, 30, TipSide.Above));     // 위에 자리 없음
            AssertSpot(400, 586, TipSide.Above, Place(400, 670, 200, 30, TipSide.Below));    // 독 맨 아래 줄
            AssertSpot(254, 300, TipSide.Right, Place(50, 300, 200, 30, TipSide.Left));
            AssertSpot(696, 300, TipSide.Left, Place(1000, 300, 200, 30, TipSide.Right));
        }

        [Test]
        public void M4_가로세로끝은화면안으로민다()
        {
            AssertSpot(980, 334, TipSide.Below, Place(1200, 300, 60, 30, TipSide.Below));   // 오른쪽 끝: 1280 − 300
            AssertSpot(604, 640, TipSide.Right, Place(400, 700, 200, 15, TipSide.Right));   // 아래 끝: 720 − 80
        }

        [Test]
        public void M4_양쪽다안들어가면넓은쪽에두고화면안으로민다()
        {
            // 툴팁 높이 500: 위 296·아래 386 → 아래, 720 − 500 = 220으로 밀림(대상을 덮는다)
            AssertSpot(400, 220, TipSide.Below, Place(400, 300, 200, 30, TipSide.Above, 300, 500));
            // 위가 더 넓으면 위에 두고 0으로 밀림
            AssertSpot(400, 0, TipSide.Above, Place(400, 450, 200, 30, TipSide.Below, 300, 500));
        }

        [Test]
        public void M4_고른쪽이안들어가도반대편이더좁으면넘기지않는다()
        {
            AssertSpot(400, 0, TipSide.Above, Place(400, 450, 200, 30, TipSide.Above, 300, 500));
        }

        [Test]
        public void M4_아래를골라안들어가도위가더좁으면넘기지않는다()
        {
            // 툴팁 높이 500: 위 196·아래 486 → 아래에 두고 720 − 500 = 220으로 밀림
            AssertSpot(400, 220, TipSide.Below, Place(400, 200, 200, 30, TipSide.Below, 300, 500));
        }

        [Test]
        public void M4_간격까지딱맞으면고른쪽에둔다()
        {
            // 위: 대상 위 84 = 간격 4 + 툴팁 80. 아래: 대상 아래 끝 636 + 4 + 80 = 720
            AssertSpot(400, 0, TipSide.Above, Place(400, 84, 200, 30, TipSide.Above));
            AssertSpot(400, 640, TipSide.Below, Place(400, 606, 200, 30, TipSide.Below));
        }

        [Test]
        public void M4_툴팁은들어가도간격이안들어가면넘긴다()
        {
            // 위 82·83: 툴팁 80은 들어가지만 간격 4까지는 안 들어간다
            AssertSpot(400, 116, TipSide.Below, Place(400, 82, 200, 30, TipSide.Above));
            AssertSpot(400, 117, TipSide.Below, Place(400, 83, 200, 30, TipSide.Above));
            AssertSpot(400, 523, TipSide.Above, Place(400, 607, 200, 30, TipSide.Below));
        }

        [Test]
        public void M4_자리는정수로맞춘다()
        {
            var s = Place(400.4f, 300.6f, 200, 30, TipSide.Below);
            AssertSpot(400, 335, TipSide.Below, s);
        }

        [Test]
        public void M4_반픽셀자리는늘올린다()
        {
            // 배율 2에서는 대상이 0.5 자리에 놓인다. 짝수 쪽 반올림이면 400.5 → 400, 401.5 → 402로 이웃 대상의 툴팁이 엇갈린다
            AssertSpot(401, 335, TipSide.Below, Place(400.5f, 300.5f, 200, 30, TipSide.Below));
            AssertSpot(402, 336, TipSide.Below, Place(401.5f, 301.5f, 200, 30, TipSide.Below));
        }

        [Test]
        public void M4_화면보다큰툴팁은왼쪽위에붙인다()
        {
            var s = TipPlace.Place(10, 10, 20, 20, 2000, 900, 1280, 720, TipSide.Below, 4);
            Assert.AreEqual(0, s.X);
            Assert.AreEqual(0, s.Y);
        }
    }
}
