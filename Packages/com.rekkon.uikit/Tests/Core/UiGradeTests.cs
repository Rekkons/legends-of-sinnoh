using System;
using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class UiGradeTests
    {
        [TestCase(1280, 720, 720, 1)]
        [TestCase(1920, 1080, 1080, 1)]
        [TestCase(2560, 1440, 720, 2)]
        [TestCase(3840, 2160, 1080, 2)]
        public void M4_기준화면넷은제등급(int w, int h, int tokens, int scale)
        {
            var g = UiGrade.For(w, h);
            Assert.AreEqual(new UiGrade(tokens, scale), g);
            Assert.AreEqual(w, g.Width);
            Assert.AreEqual(h, g.Height);
        }

        [TestCase(1920, 950, 720, 1, TestName = "M4_등급사이는다들어가는가장큰등급_브라우저안쪽1920x950은720")]
        [TestCase(1600, 900, 720, 1, TestName = "M4_등급사이는다들어가는가장큰등급_1600x900은720")]
        [TestCase(1919, 1080, 720, 1, TestName = "M4_등급사이는다들어가는가장큰등급_폭이1px모자라면아래등급")]
        [TestCase(2560, 1080, 1080, 1, TestName = "M4_등급사이는다들어가는가장큰등급_21대9는높이로1080")]
        [TestCase(3440, 1440, 720, 2, TestName = "M4_등급사이는다들어가는가장큰등급_3440x1440은1440")]
        [TestCase(2560, 1600, 720, 2, TestName = "M4_등급사이는다들어가는가장큰등급_16대10은1440")]
        [TestCase(5120, 2880, 1080, 2, TestName = "M4_등급사이는다들어가는가장큰등급_4K보다커도2160")]
        public void M4_등급사이는다들어가는가장큰등급(int w, int h, int tokens, int scale)
        {
            Assert.AreEqual(new UiGrade(tokens, scale), UiGrade.For(w, h));
        }

        [TestCase(1024, 768)]
        [TestCase(1280, 600)]
        [TestCase(0, 0)]
        public void M4_가장작은등급보다작아도720(int w, int h)
        {
            Assert.AreEqual(new UiGrade(720, 1), UiGrade.For(w, h));
        }

        [Test]
        public void M4_등급은토큰벌과배율이모두같아야같다()
        {
            Assert.AreEqual(new UiGrade(720, 2), new UiGrade(720, 2));
            Assert.AreNotEqual(new UiGrade(720, 1), new UiGrade(720, 2));     // 720과 1440
            Assert.AreNotEqual(new UiGrade(1080, 2), new UiGrade(720, 2));    // 2160과 1440
            Assert.IsFalse(new UiGrade(1080, 1).Equals(new UiGrade(1080, 2)));
        }

        [Test]
        public void M4_없는토큰벌과배율은만들지못한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiGrade(1440, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiGrade(720, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiGrade(1080, 0));
        }
    }
}
