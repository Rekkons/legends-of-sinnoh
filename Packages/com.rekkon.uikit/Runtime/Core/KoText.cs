using System.Text;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 한글 줄바꿈. UI Toolkit은 한글을 음절마다 끊을 수 있는 글자로 봐서 「턴 종 / 료」처럼 낱말 가운데서 줄이 바뀐다.
    /// 여러 줄로 감기는 글(툴팁·기록·설명)은 <see cref="JoinWords"/>를 거쳐 띄어쓰기에서만 바뀌게 한다. 한 줄 글(nowrap)에는 필요 없다.
    /// </summary>
    public static class KoText
    {
        /// <summary>WORD JOINER(U+2060): 폭이 없고 앞뒤에서 줄이 바뀌지 않는다.</summary>
        public const char WordJoiner = (char)0x2060;

        /// <summary>
        /// 어절(공백으로 나뉜 글자 덩어리) 안 글자 사이마다 <see cref="WordJoiner"/>를 넣는다. 한글만이 아니라 영문·숫자·문장부호 사이에도 넣는다
        /// (「8/8」·「+2)」가 부호 뒤에서 갈리지 않게 — 어절이 통째로 다음 줄로 간다).
        /// <list type="bullet">
        /// <item>공백(띄어쓰기·줄바꿈·탭)은 그대로 두고 그 앞뒤에는 넣지 않는다.</item>
        /// <item>리치 텍스트 태그(<c>&lt;…&gt;</c>)는 그대로 옮기고 없는 것처럼 건너뛴다: 「&lt;b&gt;불꽃&lt;/b&gt;은」의 조사가 태그 뒤에서 갈리지 않는다. 닫는 「&gt;」가 없는 「&lt;」는 글자로 친다.</item>
        /// <item>이미 들어 있는 U+2060 곁에는 더 넣지 않는다(두 번 거쳐도 같다). 서로게이트 쌍은 가르지 않는다.</item>
        /// </list>
        /// null·빈 문자열은 빈 문자열.
        /// </summary>
        public static string JoinWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length * 2);
            bool inWord = false;   // 바로 앞 보이는 글자가 어절 안(공백도 U+2060도 아님)
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    int close = text.IndexOf('>', i);
                    if (close >= 0) { sb.Append(text, i, close - i + 1); i = close; continue; }
                }
                if (c == WordJoiner || char.IsWhiteSpace(c)) { sb.Append(c); inWord = false; continue; }
                if (inWord) sb.Append(WordJoiner);
                sb.Append(c);
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) sb.Append(text[++i]);
                inWord = true;
            }
            return sb.ToString();
        }
    }
}
