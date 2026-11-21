using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class LinearAlphaTests
    {
        [Test]
        public void M4_어둡게덮기보정은1빼기1빼기a의2점2제곱()
        {
            Assert.AreEqual(0f, LinearAlpha.Dim(0f));
            Assert.AreEqual(1f, LinearAlpha.Dim(1f));
            Assert.AreEqual(0.7824f, LinearAlpha.Dim(0.5f), 1e-4, "sRGB에서 고른 0.5는 Linear에서 0.78이어야 같은 만큼 가린다");
            Assert.AreEqual(0.827f, LinearAlpha.Dim(0.55f), 5e-4, "토큰 생성기 scrim(…, 0.55) = 0.827과 같은 식");
            Assert.AreEqual(0.6124f, LinearAlpha.Dim(0.35f), 1e-4);
        }

        [Test]
        public void M4_흰빛보정은a의2점2제곱()
        {
            Assert.AreEqual(0f, LinearAlpha.Glow(0f));
            Assert.AreEqual(1f, LinearAlpha.Glow(1f));
            Assert.AreEqual(0.2176f, LinearAlpha.Glow(0.5f), 1e-4);
            Assert.AreEqual(0.0290f, LinearAlpha.Glow(0.2f), 1e-4);
        }

        [Test]
        public void M4_알파보정은0과1밖을잘라붙이고단조롭다()
        {
            Assert.AreEqual(0f, LinearAlpha.Dim(-0.3f));
            Assert.AreEqual(1f, LinearAlpha.Dim(1.7f));
            Assert.AreEqual(0f, LinearAlpha.Glow(-0.3f));
            Assert.AreEqual(1f, LinearAlpha.Glow(1.7f));
            float dim = 0f, glow = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float a = i / 20f;
                Assert.Greater(LinearAlpha.Dim(a), dim);
                Assert.Greater(LinearAlpha.Glow(a), glow);
                Assert.GreaterOrEqual(LinearAlpha.Dim(a), a, "덮개는 더 진하게");
                Assert.LessOrEqual(LinearAlpha.Glow(a), a, "빛은 더 옅게");
                dim = LinearAlpha.Dim(a);
                glow = LinearAlpha.Glow(a);
            }
        }
    }
}
