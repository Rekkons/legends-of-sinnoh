using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class KoTextTests
    {
        const string J = "⁠";

        // 읽기 쉽게: 기대값의 「|」를 U+2060으로 바꾼다
        static string E(string s) => s.Replace("|", J);

        [Test]
        public void M4_빈문자열과null은빈문자열()
        {
            Assert.AreEqual("", KoText.JoinWords(""));
            Assert.AreEqual("", KoText.JoinWords(null));
        }

        [Test]
        public void M4_어절안글자사이에만넣는다()
        {
            Assert.AreEqual(E("턴 종|료"), KoText.JoinWords("턴 종료"));
            Assert.AreEqual(E("잠|든 포|켓|몬|은"), KoText.JoinWords("잠든 포켓몬은"));
            Assert.AreEqual("가", KoText.JoinWords("가"));
        }

        [Test]
        public void M4_공백은그대로두고곁에넣지않는다()
        {
            Assert.AreEqual(E("  가|나  다|라 "), KoText.JoinWords("  가나  다라 "));
            Assert.AreEqual(E("첫|줄\n둘|째\t셋"), KoText.JoinWords("첫줄\n둘째\t셋"));
            Assert.AreEqual(" \n ", KoText.JoinWords(" \n "));
        }

        [Test]
        public void M4_문장부호도어절에붙는다()
        {
            Assert.AreEqual(E("바|뀝|니|다|. (|기|본 8 · 띠 +|2|)"), KoText.JoinWords("바뀝니다. (기본 8 · 띠 +2)"));
            Assert.AreEqual(E("「|턴 종|료|」"), KoText.JoinWords("「턴 종료」"));
        }

        [Test]
        public void M4_영문과숫자가섞여도어절은하나()
        {
            Assert.AreEqual(E("H|P 8|/|8"), KoText.JoinWords("HP 8/8"));
            Assert.AreEqual(E("L|v|5|가 1|턴|째"), KoText.JoinWords("Lv5가 1턴째"));
        }

        [Test]
        public void M4_이미있는U2060곁에는더넣지않는다()
        {
            Assert.AreEqual(E("가|나|다"), KoText.JoinWords(E("가|나다")));
            Assert.AreEqual(E("|가|나|"), KoText.JoinWords(E("|가나|")));
        }

        [Test]
        public void M4_두번거쳐도같다()
        {
            string once = KoText.JoinWords("스피드 10 (<b>민첩의 띠</b> +2)");
            Assert.AreEqual(once, KoText.JoinWords(once));
        }

        [Test]
        public void M4_리치텍스트태그는그대로두고건너뛴다()
        {
            // 태그 안에는 넣지 않고, 태그를 사이에 둔 글자는 이어진 것으로 본다(조사가 태그 뒤에서 갈리지 않게)
            Assert.AreEqual(E("<b>불|꽃</b>|은 강|하|다"), KoText.JoinWords("<b>불꽃</b>은 강하다"));
            Assert.AreEqual(E("<color=#ff5050>독</color> 상|태"), KoText.JoinWords("<color=#ff5050>독</color> 상태"));
            Assert.AreEqual(E("가<b>|나</b>"), KoText.JoinWords("가<b>나</b>"));
        }

        [Test]
        public void M4_닫히지않은꺾쇠는글자로친다()
        {
            Assert.AreEqual(E("H|P|<|3"), KoText.JoinWords("HP<3"));
        }

        [Test]
        public void M4_서로게이트쌍은가르지않는다()
        {
            string smile = "😀";
            Assert.AreEqual(E("가|" + smile + "|나"), KoText.JoinWords("가" + smile + "나"));
        }

        [Test]
        public void M4_넣은것을빼면원문이다()
        {
            const string src = "잠든 포켓몬은 일반 타격 1개가 <b>상태 이상</b> 심볼로 바뀝니다.";
            Assert.AreEqual(src, KoText.JoinWords(src).Replace(J, ""));
        }
    }
}
