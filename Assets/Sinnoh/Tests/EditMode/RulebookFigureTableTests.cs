using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 「설명서 그림」: 자를 칸 표(`Tools/assets/tables/rulebook_figures.json` — rulebook.mjs가 읽는다)가 설명서 글의 그림 자리와 한 줄씩 맞는다.
    /// 그림 본체(Gen — git 밖)는 보지 않는다(EditorAssets `RulebookFigureTests`).
    /// </summary>
    public class RulebookFigureTableTests
    {
        static readonly Regex Row = new Regex("^\\{\"key\":\"(?<key>[^\"]+)\",\"page\":(?<page>\\d+),(?<rest>.*)\\},?$", RegexOptions.Multiline);

        static List<(string key, int page, string rest)> Table()
        {
            string text = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/assets/tables/rulebook_figures.json"));
            return Row.Matches(text).Cast<Match>().Select(m => (m.Groups["key"].Value, int.Parse(m.Groups["page"].Value), m.Groups["rest"].Value)).ToList();
        }

        [Test]
        public void M9_설명서그림표는그림자리와한줄씩맞는다()
        {
            var table = Table();
            Assert.AreEqual(table.Count, table.Select(r => r.key).Distinct().Count(), "표 키 겹침 없음");
            foreach (var file in new[] { "rulebook.ko.json", "rulebook.en.json" })
            {
                var book = Rulebook.Load(TestData.ReadText(file), file);
                var pages = new Dictionary<string, IReadOnlyList<int>>();
                foreach (var s in book.Sections)
                    foreach (var b in s.Blocks)
                    {
                        if (b.Kind == RulebookBlockKind.Figure) pages[b.Figure] = s.Pages;
                        foreach (string icon in b.Icons) pages[icon] = s.Pages;   // 표 줄 머리 아이콘(결정 143 ③)
                    }
                CollectionAssert.AreEquivalent(pages.Keys, table.Select(r => r.key), file + ": 그림 키마다 표 줄 하나, 남는 줄 없음");
                foreach (var r in table) CollectionAssert.Contains(pages[r.key], r.page, $"{r.key}: 쪽 {r.page}은 그 절의 원본 쪽 안");
            }
            Assert.AreEqual(71, table.Count, "그림 자리 71(상태 아이콘 띠 1 → 줄 머리 아이콘 6 — 결정 143 ③)");
            CollectionAssert.AreEquivalent(new[] { "battle.status/confused", "battle.status/paralysed", "battle.status/poisoned", "battle.status/burned", "battle.status/frozen", "battle.status/drowsy" },
                table.Where(r => r.rest.Contains("\"round\":true")).Select(r => r.key), "둥근 아이콘(내접 원 밖 투명)으로 자르는 그림");
            foreach (var r in table)
                Assert.That(r.rest.StartsWith("\"rect\":[") ^ r.rest.StartsWith("\"parts\":[["), r.key + ": rect 하나 또는 parts");
            // 결정 163: 그림은 모두 영문 원본 PDF 한 벌(한국어판 PDF의 그림 — 표지 · 체리꼬 · 쉐이미 — 은 쓰지 않는다)
            CollectionAssert.IsEmpty(table.Where(r => r.rest.Contains("\"lang\":")).Select(r => r.key), "언어별로 자르는 그림 없음");
        }

        [Test]
        public void M10_결정143_상태이상표는줄마다그상태의아이콘을이름앞에두고아래아이콘띠는없다()
        {
            IReadOnlyList<string> en = null;
            foreach (var file in new[] { "rulebook.en.json", "rulebook.ko.json" })
            {
                var s = Rulebook.Load(TestData.ReadText(file), file).Find("battle.status");
                Assert.IsFalse(s.Blocks.Any(b => b.Kind == RulebookBlockKind.Figure), file + ": 표 아래 아이콘 띠(그림 블록)가 없다");
                var t = s.Blocks.Single(b => b.Kind == RulebookBlockKind.Table);
                Assert.AreEqual(t.Rows.Count, t.Icons.Count, file + ": 줄마다 아이콘");
                if (en == null)
                {
                    // 영문 원본의 줄 이름 = 규칙의 상태 이름 → 그 상태의 아이콘
                    for (int i = 0; i < t.Rows.Count; i++)
                    {
                        Assert.IsTrue(System.Enum.TryParse<Sinnoh.Rules.StatusEffect>(t.Rows[i][0], out var st) && st != Sinnoh.Rules.StatusEffect.None, t.Rows[i][0]);
                        Assert.AreEqual("battle.status/" + st.ToString().ToLowerInvariant(), t.Icons[i], t.Rows[i][0] + " 줄의 아이콘");
                    }
                    en = t.Icons;
                }
                else CollectionAssert.AreEqual(en, t.Icons, file + ": 두 언어 같은 줄 순서 · 같은 아이콘");
            }
        }
    }
}
