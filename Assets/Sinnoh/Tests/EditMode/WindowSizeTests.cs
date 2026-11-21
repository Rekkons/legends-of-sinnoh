using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>고를 수 있는 창 크기(결정 59, 04 §9-3).</summary>
    public sealed class WindowSizeTests
    {
        [Test]
        public void M4_화면크기목록은모니터안에드는것만_결정59()
        {
            Assert.That(WindowSizes.All, Is.EqualTo(new[] { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160) }));
            Assert.That(WindowSizes.Fit(5120, 2880), Is.EqualTo(WindowSizes.All), "5K 모니터: 전부");
            Assert.That(WindowSizes.Fit(3840, 2160), Is.EqualTo(new[] { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440) }), "모니터와 같은 크기는 뺀다(제목 줄·테두리까지는 들어가지 않는다 — 전체 화면이 맡는다)");
            Assert.That(WindowSizes.Fit(2560, 1440), Is.EqualTo(new[] { (1280, 720), (1600, 900), (1920, 1080) }));
            Assert.That(WindowSizes.Fit(1920, 1080), Is.EqualTo(new[] { (1280, 720), (1600, 900) }));
            Assert.That(WindowSizes.Fit(1920, 1200), Is.EqualTo(new[] { (1280, 720), (1600, 900) }), "16:10 — 폭이 같아도 뺀다");
            Assert.That(WindowSizes.Fit(3440, 1440), Is.EqualTo(new[] { (1280, 720), (1600, 900), (1920, 1080) }), "울트라와이드 — 높이가 같아도 뺀다");
            Assert.That(WindowSizes.Fit(1366, 768), Is.EqualTo(new[] { (1280, 720) }));
            Assert.That(WindowSizes.Fit(2000, 800), Is.EqualTo(new[] { (1280, 720) }), "폭만 드는 크기는 뺀다");
            Assert.That(WindowSizes.Fit(1024, 768), Is.Empty, "드는 것이 없으면 빈 목록(전체 화면만 남는다)");
        }
    }
}
