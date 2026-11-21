using System;
using NUnit.Framework;
using Sinnoh.Fx;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 마우스로 보드 끌기(결정 66 ②, 04 §3-2)의 순수 셈: 끌기 문턱(끌었나 눌렀나) · 화면 광선 → 보드 평면 · 보는 점의 범위.
    /// </summary>
    public class BoardDragTests
    {
        [Test]
        public void M4_문턱안에서떼면칸누르기_문턱을넘으면끌기이고누르기가아니다()
        {
            double thr = BoardDrag.Threshold(1080);
            Assert.AreEqual(6.0, thr, 1e-9);
            var drag = new BoardDrag();
            Assert.AreEqual(DragEvent.None, drag.Step(100, 100, false, false, false, thr), "단추를 누르지 않았다");
            Assert.AreEqual(DragEvent.Began, drag.Step(100, 100, true, false, false, thr));
            Assert.AreEqual(DragEvent.None, drag.Step(104, 103, true, false, false, thr), "문턱 안(5px)");
            Assert.IsFalse(drag.Panning);
            Assert.AreEqual(DragEvent.Click, drag.Step(104, 103, false, false, false, thr), "끌지 않고 뗐다");
            Assert.IsFalse(drag.Held);

            Assert.AreEqual(DragEvent.Began, drag.Step(100, 100, true, false, false, thr));
            Assert.AreEqual(DragEvent.Pan, drag.Step(100, 107, true, false, false, thr), "문턱을 넘었다");
            Assert.AreEqual(DragEvent.Pan, drag.Step(100, 101, true, false, false, thr), "한 번 넘으면 누른 자리로 돌아와도 끌기다");
            Assert.AreEqual(DragEvent.Ended, drag.Step(100, 100, false, false, false, thr), "끌다가 누른 자리에서 떼도 누르기가 아니다");
            Assert.AreEqual(DragEvent.None, drag.Step(100, 100, false, false, false, thr));
        }

        [Test]
        public void M4_가운데오른쪽단추는끌기만하고_HUD위에서시작한끌기는뗄때까지무시한다()
        {
            double thr = BoardDrag.Threshold(720);
            Assert.AreEqual(4.0, thr, 1e-9, "작은 창의 문턱은 아래 한계");
            Assert.AreEqual(12.0, BoardDrag.Threshold(2160), 1e-9);
            var drag = new BoardDrag();
            // 오른쪽·가운데: 끌지 않고 떼도 누르기가 아니다
            Assert.AreEqual(DragEvent.Began, drag.Step(50, 50, false, true, false, thr));
            Assert.IsFalse(drag.ByLeft);
            Assert.AreEqual(DragEvent.Ended, drag.Step(50, 50, false, false, false, thr));
            Assert.AreEqual(DragEvent.Began, drag.Step(50, 50, false, true, false, thr));
            Assert.AreEqual(DragEvent.Pan, drag.Step(70, 50, false, true, false, thr));
            Assert.AreEqual(DragEvent.Ended, drag.Step(70, 50, false, false, false, thr));
            // HUD 위에서 시작: 보드 위로 나와도 옮기지 않고, 떼도 누르기가 아니다
            Assert.AreEqual(DragEvent.Began, drag.Step(50, 50, true, false, true, thr));
            Assert.IsTrue(drag.Dead);
            Assert.AreEqual(DragEvent.None, drag.Step(300, 300, true, false, false, thr));
            Assert.IsFalse(drag.Panning);
            Assert.AreEqual(DragEvent.Ended, drag.Step(300, 300, false, false, false, thr));
            Assert.IsFalse(drag.Dead, "떼면 다음 끌기는 새로 센다");
            // 보드 위에서 시작한 끌기는 HUD 위로 가도 이어진다
            Assert.AreEqual(DragEvent.Began, drag.Step(50, 50, true, false, false, thr));
            Assert.AreEqual(DragEvent.Pan, drag.Step(90, 50, true, false, true, thr));
            // 도중에 끊으면(창이 떴다) 뗄 때까지 옮기지도 누르지도 않는다
            drag.Kill();
            Assert.AreEqual(DragEvent.None, drag.Step(120, 50, true, false, false, thr));
            Assert.AreEqual(DragEvent.Ended, drag.Step(120, 50, false, false, false, thr));
            // 문턱 안에서 끊어도 누르기가 아니다
            Assert.AreEqual(DragEvent.Began, drag.Step(50, 50, true, false, false, thr));
            drag.Kill();
            Assert.AreEqual(DragEvent.Ended, drag.Step(50, 50, false, false, false, thr));
        }

        [Test]
        public void M4_누르고멀리서바로떼면누르기가아니다_왼쪽을먼저떼고다른단추를떼도누르기가아니다()
        {
            double thr = BoardDrag.Threshold(1080);
            var drag = new BoardDrag();
            // 눌린 채 들어온 프레임이 없어도(빠른 끌기 · 끊긴 프레임) 뗀 자리가 문턱 밖이면 끌기로 친다 — 이동 답은 되돌릴 수 없다
            Assert.AreEqual(DragEvent.Began, drag.Step(100, 100, true, false, false, thr));
            Assert.AreEqual(DragEvent.Ended, drag.Step(400, 300, false, false, false, thr), "멀리서 바로 뗐다");
            Assert.IsFalse(drag.Held);
            // 문턱 안에서 바로 떼면 누르기 그대로
            Assert.AreEqual(DragEvent.Began, drag.Step(100, 100, true, false, false, thr));
            Assert.AreEqual(DragEvent.Click, drag.Step(103, 102, false, false, false, thr));
            // 왼쪽으로 누르고 오른쪽을 더 누른 뒤 왼쪽 → 오른쪽 순서로 뗀다
            Assert.AreEqual(DragEvent.Began, drag.Step(100, 100, true, false, false, thr));
            Assert.AreEqual(DragEvent.None, drag.Step(100, 100, true, true, false, thr));
            Assert.AreEqual(DragEvent.None, drag.Step(100, 100, false, true, false, thr));
            Assert.AreEqual(DragEvent.Ended, drag.Step(100, 100, false, false, false, thr), "오른쪽을 마저 뗀 것은 누르기가 아니다");
        }

        [Test]
        public void M4_전체줌의보는점_판이위띠와아래판사이의가운데에온다()
        {
            // 찌그러진 판(가운데가 원점이 아니다) + 높이가 다른 점들, 보드 카메라의 값(기울기 55° · 시야 30° · 전체 줌 거리)
            var pts = new double[] { -30, 0.5, -18, 26, 2.0, -20, 31, 0.5, 12, -22, 3.5, 25, 4, 1.0, 30, 0, 0.5, 0 };
            double d = 2.2 * 540.0 / (51.0 * Math.Tan(15.0 * Math.PI / 180.0));
            foreach (double yaw in new[] { 0.0, 60.0, 240.0 })
            {
                double w = 1920, h = 1080, left = 0, right = 1920, bottom = 180, top = 985;
                BoardDrag.Frame(pts, 0.0, 55.0, yaw, 30.0, d, w, h, left, bottom, right, top, out double tx, out double tz);
                double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
                for (int i = 0; i < pts.Length; i += 3)
                {
                    Assert.IsTrue(BoardDrag.Project(pts[i], pts[i + 1], pts[i + 2], tx, 0.0, tz, 55.0, yaw, 30.0, d, w, h, out double sx, out double sy));
                    minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
                    minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
                }
                Assert.AreEqual((left + right) * 0.5, (minX + maxX) * 0.5, 0.5, "가로 가운데(yaw " + yaw + ")");
                Assert.AreEqual((bottom + top) * 0.5, (minY + maxY) * 0.5, 0.5, "세로 가운데(yaw " + yaw + ")");
            }
            // 보는 점 자체는 화면 한가운데에 비친다
            Assert.IsTrue(BoardDrag.Project(3, 0, -7, 3, 0, -7, 55.0, 120.0, 30.0, d, 1280, 720, out double cx, out double cy));
            Assert.AreEqual(640.0, cx, 1e-6);
            Assert.AreEqual(360.0, cy, 1e-6);
        }

        [Test]
        public void M4_끈만큼땅이커서를따라온다_광선과평면_보는점은범위안()
        {
            // 보드 카메라와 같은 기울기 55°로 내려다보는 광선
            double tilt = 55.0 * Math.PI / 180.0;
            double dy = -Math.Sin(tilt), dz = Math.Cos(tilt);
            Assert.IsTrue(BoardDrag.Ground(3.0, 10.0, -2.0, 0.0, dy, dz, 0.5, out double x, out double z));
            Assert.AreEqual(3.0, x, 1e-9);
            Assert.AreEqual(-2.0 + 9.5 * dz / -dy, z, 1e-9);
            Assert.IsFalse(BoardDrag.Ground(0, 10, 0, 0, 0, 1, 0.5, out _, out _), "수평 광선");
            Assert.IsFalse(BoardDrag.Ground(0, 10, 0, 0, 1, 0, 0.5, out _, out _), "면을 등진 광선");

            // 카메라 = 보는 점 + 고정된 어긋남. 잡은 점이 커서 광선 아래에 오도록 보는 점을 옮기면, 옮긴 카메라에서 같은 광선이 잡은 점에 닿는다
            double offY = 20.0, offZ = -14.0;   // 보는 점에서 카메라까지
            double tx = 5.0, tz = 7.0, ty = 0.5;
            double rdx = 0.2, rdy = -0.8, rdz = 0.56;   // 커서 광선(방향은 카메라와 함께 옮겨도 그대로)
            double grabX = 1.5, grabZ = 9.0, grabY = 1.25;
            Assert.IsTrue(BoardDrag.Ground(tx, ty + offY, tz + offZ, rdx, rdy, rdz, grabY, out double hx, out double hz));
            BoardDrag.Pan(ref tx, ref tz, grabX, grabZ, hx, hz, -100, 100, -100, 100);
            Assert.IsTrue(BoardDrag.Ground(tx, ty + offY, tz + offZ, rdx, rdy, rdz, grabY, out double ax, out double az));
            Assert.AreEqual(grabX, ax, 1e-9, "잡은 땅이 커서 아래에 있다");
            Assert.AreEqual(grabZ, az, 1e-9);

            // 범위 밖으로는 나가지 않는다
            double cx = 5.0, cz = 7.0;
            BoardDrag.Pan(ref cx, ref cz, 50.0, -50.0, 0.0, 0.0, -10, 10, -8, 8);
            Assert.AreEqual(10.0, cx, 1e-9);
            Assert.AreEqual(-8.0, cz, 1e-9);
        }
    }
}
