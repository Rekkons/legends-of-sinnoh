using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「UB 화면 묶음 2」의 탈락·부활 연출 셈(<c>FallStaging.cs</c> — 엔진 무관, dotnet에서도 돈다): 91행의 방장·화면 행과 길이, 탈락은 제 색 → 회색 · 부활은 회색 → 제 색,
    /// 대기열에 남은 단계로 되돌려 센 모습(보기는 이미 끝 값), 축복 쓰기·건너뛰기·축복 +1은 아직 단계가 없다(위 띠 축복 칩 몫).
    /// </summary>
    public class UbFallTests
    {
        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        [Test]
        public void M6_탈락부활행_방장과화면이같은행과길이()
        {
            foreach (var e in new GameEvent[] { new Eliminated { Seat = 1 }, new Revived { Seat = 1, Cell = 3 } })
            {
                Assert.AreEqual(FallRows.Row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(FallRows.Row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(1500, PlayTiming.RowMs(e, FallRows.Row), e.GetType().Name + " 길이");
            }
            Assert.IsTrue(FxBuilt.Drawn(FallRows.Row), "91행은 화면이 그린다");
            Assert.IsTrue(SoundCues.Covers(FallRows.Row), "91행 소리 표");
            foreach (var e in new GameEvent[] { new BlessingUsed(), new UbTurnSkipped(), new BlessingGained() })
            {
                Assert.AreEqual(0, PlayTiming.RowOf(e), e.GetType().Name);
                Assert.AreEqual(FxRows.None, FxRows.Of(e), e.GetType().Name);
            }
        }

        [Test]
        public void M6_탈락박자_휘청뒤회색으로바래고금이간다()
        {
            const int total = 1500;
            var start = FallBeats.Out(0, total);
            Assert.AreEqual(0f, start.Grey, 1e-4f);
            Assert.IsFalse(start.Crack);
            var mid = FallBeats.Out(total * FallBeats.SwayPermille / 1000 + 100, total);
            Assert.That(mid.Grey, Is.GreaterThan(0f).And.LessThan(1f), "바래는 중");
            Assert.IsTrue(FallBeats.Out(total * FallBeats.CrackPermille / 1000, total).Crack, "금은 맞음 순간부터");
            var end = FallBeats.Out(total, total);
            Assert.AreEqual(1f, end.Grey, 1e-4f);
            Assert.IsTrue(end.Crack);
        }

        [Test]
        public void M6_부활박자_고리가내려온뒤색을되찾는다()
        {
            const int total = 1500;
            var start = FallBeats.Back(0, total);
            Assert.AreEqual(1f, start.Grey, 1e-4f, "고리가 내려오는 동안은 아직 회색");
            var ring = FallBeats.Back(total * FallBeats.RingPermille / 2000, total);
            Assert.That(ring.RingAlpha, Is.GreaterThan(0f));
            Assert.That(ring.Ring, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, FallBeats.Back(total * FallBeats.RingPermille / 1000, total).Grey, 1e-3f);
            var end = FallBeats.Back(total, total);
            Assert.AreEqual(0f, end.Grey, 1e-4f);
            Assert.IsFalse(end.Crack);
            Assert.AreEqual(0f, end.RingAlpha, 1e-4f);
        }

        [Test]
        public void M6_보이는탈락모습_대기열이없으면보기값()
        {
            Assert.AreEqual(1f, FallBeats.Shown(null, 1, true).Grey, 1e-4f);
            Assert.IsTrue(FallBeats.Shown(null, 1, true).Crack);
            Assert.AreEqual(0f, FallBeats.Shown(null, 1, false).Grey, 1e-4f);
        }
    }
}
