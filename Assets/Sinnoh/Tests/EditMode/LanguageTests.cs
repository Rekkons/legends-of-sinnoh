using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M9 「언어 ① 영어 문자열」 · 완료 조건 3: strings.ko의 모든 키가 strings.en에 있고, 영어 값에 한글이 없고, 자리표시자({이름})·&lt;b&gt; 짝이 두 언어에 같다
    /// (빌드 검사 lib/strings.mjs checkLanguagePair와 같은 규칙). 두 언어 글자 덮개: PC 층 글꼴 Galmuri11이 두 표의 모든 글자를 덮는다 —
    /// DS 층 원작 자산 글꼴 덮개는 <see cref="GenStageTests"/> M4_글꼴빠진글자0(DS 글상자 키까지 넓힘 — GenStageTests.DsKey)이 본다. Gen/이 없으면 글꼴 시험은 건너뛴다.
    /// </summary>
    public class LanguageTests
    {
        StringTable ko, en;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ko = TestData.LoadStrings("ko");
            en = TestData.LoadStrings("en");
        }

        static readonly Regex Holes = new Regex("\\{[A-Za-z0-9_]+\\}|</?b>", RegexOptions.CultureInvariant);
        static readonly Regex AnyHangul = new Regex("[ᄀ-ᇿ㄰-㆏ꥠ-꥿가-힯ힰ-퟿]", RegexOptions.CultureInvariant);
        static string HoleSet(string s) => string.Join(" ", Holes.Matches(s).Cast<Match>().Select(m => m.Value).OrderBy(v => v, System.StringComparer.Ordinal));

        [Test]
        public void M9_언어_ko키는모두en에있고_en에한글없음_자리표시자같음()
        {
            var missing = new List<string>();
            var hangul = new List<string>();
            var holes = new List<string>();
            foreach (var key in ko.Keys)
            {
                if (!en.TryGet(key, out var e)) { missing.Add(key); continue; }
                if (AnyHangul.IsMatch(e)) hangul.Add(key + " = " + e);
                if (HoleSet(e) != HoleSet(ko[key])) holes.Add($"{key}: ko [{HoleSet(ko[key])}] · en [{HoleSet(e)}]");
            }
            CollectionAssert.IsEmpty(missing, "ko에 있는데 en에 없는 키");
            CollectionAssert.IsEmpty(hangul, "en에 한글이 남은 키");
            CollectionAssert.IsEmpty(holes, "자리표시자가 두 언어에 다른 키");
            Assert.Greater(ko.Count, 3900);
        }

        /// <summary>ui-text-guide §6: 영어 화면 글(screen.* · tutorial.*)은 원작 영문 용어를 쓴다 — 보드 칸은 tile, 은행상회는 Ginkgo Merchants, 포획률은 Capture Rate(룰북).</summary>
        static readonly string[] BannedEnglish =
        {
            "hex", "Ginkgo Guild", "Twist of Fate", "catch rate", "Arceus's Blessing", "Celestial Rift", "Horde", "Poke Ball", "Initiative token", "Initiative Token",
        };

        [Test]
        public void M9_언어_영어화면글은원작영문용어()
        {
            int n = 0;
            foreach (var key in en.Keys)
            {
                if (!key.StartsWith("screen.", System.StringComparison.Ordinal) && !key.StartsWith("tutorial.", System.StringComparison.Ordinal)) continue;
                n++;
                string v = en[key];
                foreach (var w in BannedEnglish)
                    Assert.IsFalse(Regex.IsMatch(v, "\\b" + Regex.Escape(w) + "\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), $"{key}: 「{w}」(ui-text-guide §6) — {v}");
            }
            Assert.Greater(n, 2000);
            Assert.AreEqual("Ginkgo Merchants", en["screen.bank.title"]);
            Assert.AreEqual("Initiative Advantage", en["screen.battle.token"], "룰북 Initiative Advantage token");
            Assert.AreEqual("Capture rate", en["screen.dex.rate"], "룰북 Capture Rate");
        }

        [Test]
        public void M9_언어_PC층Galmuri글꼴은두언어모든글자를덮는다()
        {
            string ttf = Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Fonts/Galmuri11.ttf");
            if (!File.Exists(ttf)) Assert.Ignore("M9 Gen 없음: rebuild-all 먼저 (Fonts/Galmuri11.ttf)");
            var cmap = Cmap(File.ReadAllBytes(ttf));
            Assert.Greater(cmap.Count, 11172, "Galmuri11 = 한글 음절 전부 + 라틴");
            foreach (var table in new[] { ko, en })
            {
                var missing = new SortedDictionary<int, string>();
                foreach (var key in table.Keys)
                {
                    string s = table[key];
                    for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
                    {
                        int cp = char.ConvertToUtf32(s, i);
                        if (!cmap.Contains(cp) && !missing.ContainsKey(cp)) missing[cp] = key;
                    }
                }
                CollectionAssert.IsEmpty(missing.Select(kv => $"U+{kv.Key:X4} {char.ConvertFromUtf32(kv.Key)} ({kv.Value})").ToList(), $"strings.{table.Lang}: Galmuri11에 없는 글자");
            }
        }

        /// <summary>
        /// 완료 조건 3(M9 마감 대조): 문자열 표 밖에서 PC 층(Galmuri11 하나 — UiSkin)에 나오는 글 — 설명서 두 언어(장 · 절 제목 · 블록 글 · 고친 곳의 근거와 두 원문)와
        /// 크레딧 표(credits.json — 탭 이름 · 이름 · 역할 두 언어 · 작가 이름 목록). 튜토리얼 대사 · 동의 창 · 버그 신고 글은 문자열 표(tutorial.* · screen.*)라 위 시험이 본다.
        /// </summary>
        [Test]
        public void M9_언어_PC층Galmuri글꼴은설명서와크레딧글을덮는다()
        {
            string ttf = Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Fonts/Galmuri11.ttf");
            if (!File.Exists(ttf)) Assert.Ignore("M9 Gen 없음: rebuild-all 먼저 (Fonts/Galmuri11.ttf)");
            var cmap = Cmap(File.ReadAllBytes(ttf));
            var missing = new SortedDictionary<int, string>();
            int texts = 0;
            void Check(string where, string s)
            {
                if (s == null) return;
                texts++;
                for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
                {
                    int cp = char.ConvertToUtf32(s, i);
                    if (!cmap.Contains(cp) && !missing.ContainsKey(cp)) missing[cp] = where;
                }
            }
            foreach (var lang in new[] { "ko", "en" })
            {
                string file = "rulebook." + lang + ".json";
                foreach (var s in Rulebook.Load(TestData.ReadText(file), file).Sections)
                {
                    string at = file + " " + s.Key;
                    Check(at, s.ChapterTitle);
                    foreach (string t in Rulebook.PlainTexts(s)) Check(at, t);
                }
            }
            Check("Rulebook.Ellipsis", Rulebook.Ellipsis);
            foreach (var section in CreditsTable.Load(TestData.ReadText("credits.json")).Sections)
            {
                Check("credits.json " + section.Id, section.Title);
                Check("credits.json " + section.Id, section.TitleEn);
                foreach (var e in section.Entries)
                {
                    Check("credits.json " + section.Id, e.Who);
                    Check("credits.json " + section.Id, e.Role);
                    Check("credits.json " + section.Id, e.RoleEn);
                    if (e.Names != null) Check("credits.json " + section.Id, string.Join(" · ", e.Names));
                }
            }
            Assert.Greater(texts, 1500, "설명서 두 언어 + 크레딧 글");
            CollectionAssert.IsEmpty(missing.Select(kv => $"U+{kv.Key:X4} {char.ConvertFromUtf32(kv.Key)} ({kv.Value})").ToList(), "설명서 · 크레딧: Galmuri11에 없는 글자");
        }

        /// <summary>
        /// M9 언어 ②: 조사 고르기(<see cref="Sinnoh.Fx.KoJosa.Fix"/>)는 한국어 글에만 — 영어 표의 모든 글은 빈칸을 채워도 「숫자 + 낱말 + (s)」 복수형 말고는 바뀌지 않는다 [가정 §5 #381].
        /// 한국어 글은 그대로 조사를 고른다.
        /// </summary>
        [Test]
        public void M9_언어_조사고침은한국어글에만_영어는복수형한규칙만()
        {
            Assert.AreEqual("렌트라는 1을 받았다!", Sinnoh.Fx.KoJosa.Fix("렌트라은(는) 1을(를) 받았다!"));
            Assert.AreEqual("1 status symbol", Sinnoh.Fx.KoJosa.Fix(en["screen.card.pick.symbols"].Replace("{n}", "1")));
            Assert.AreEqual("2 status symbols", Sinnoh.Fx.KoJosa.Fix(en["screen.card.pick.symbols"].Replace("{n}", "2")));
            Assert.AreEqual("Choose 1 Apricorn to take.", Sinnoh.Fx.KoJosa.Fix(en["screen.battle.reward.coral"].Replace("{n}", "1")));
            Assert.AreEqual("Choose 3 Apricorns to take.", Sinnoh.Fx.KoJosa.Fix(en["screen.battle.reward.coral"].Replace("{n}", "3")));
            Assert.AreEqual("Pikachu (Rock Star)", Sinnoh.Fx.KoJosa.Fix("Pikachu (Rock Star)"), "영어의 다른 괄호는 그대로");
            int changed = 0, n = 0;
            foreach (var key in en.Keys)
            {
                string v = Holes.Replace(en[key], m => m.Value.StartsWith("{") ? (m.Value == "{n}" ? "2" : "Pikachu") : "");
                n++;
                string fixedText = Sinnoh.Fx.KoJosa.Fix(v);
                if (fixedText == v) continue;
                changed++;
                Assert.AreEqual(v.Replace("(s)", "s"), fixedText, key + ": 영어 글은 복수형 말고는 바뀌지 않는다");
            }
            Assert.Greater(n, 3900);
            Assert.AreEqual("Deleted 1 record.", Sinnoh.Fx.KoJosa.Fix(en["screen.settings.upload.deleted"].Replace("{n}", "1")));
            Assert.AreEqual(3, changed, "영어 표의 「(s)」 복수형 꼴 = 3키(screen.card.pick.symbols · screen.battle.reward.coral · screen.settings.upload.deleted — 결정 126 ④)");
        }

        /// <summary>TrueType cmap(형식 4 · 12)이 글리프를 주는 코드 포인트 모음.</summary>
        static HashSet<int> Cmap(byte[] b)
        {
            int U16(int p) => b[p] << 8 | b[p + 1];
            int S16(int p) => (short)U16(p);
            int U32(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            int tables = U16(4), cmap = -1;
            for (int i = 0; i < tables; i++)
                if (System.Text.Encoding.ASCII.GetString(b, 12 + i * 16, 4) == "cmap") cmap = U32(12 + i * 16 + 8);
            Assert.GreaterOrEqual(cmap, 0, "cmap 표");
            var set = new HashSet<int>();
            int subtables = U16(cmap + 2);
            for (int i = 0; i < subtables; i++)
            {
                int at = cmap + U32(cmap + 4 + i * 8 + 4);
                int format = U16(at);
                if (format == 4)
                {
                    int segX2 = U16(at + 6), ends = at + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                    for (int s = 0; s < segX2 / 2; s++)
                    {
                        int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = S16(deltas + s * 2), range = U16(ranges + s * 2);
                        for (int c = start; c <= end && c != 0xFFFF; c++)
                        {
                            int g = range == 0 ? (c + delta) & 0xFFFF : U16(ranges + s * 2 + range + (c - start) * 2);
                            if (range != 0 && g != 0) g = (g + delta) & 0xFFFF;
                            if (g != 0) set.Add(c);
                        }
                    }
                }
                else if (format == 12)
                {
                    int groups = U32(at + 12);
                    for (int k = 0; k < groups; k++)
                    {
                        int start = U32(at + 16 + k * 12), end = U32(at + 20 + k * 12), g = U32(at + 24 + k * 12);
                        for (int c = start; c <= end; c++) if (g + (c - start) != 0) set.Add(c);
                    }
                }
            }
            return set;
        }
    }
}
