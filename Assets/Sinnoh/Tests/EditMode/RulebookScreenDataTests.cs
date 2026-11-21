using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 「설명서 화면 S43 · 크레딧 S03」의 엔진 무관 부분: 설명서 찾기(plan §5 #303 — <see cref="Rulebook.Search"/>) · 게임 중 옆 판의 칩 표(<see cref="RulebookChips"/>, #370) ·
    /// 크레딧 화면 표(`Data/Json/credits.json` — <see cref="CreditsTable"/>, CREDITS.md와 같은 보이는 글 roll.json).
    /// </summary>
    public class RulebookScreenDataTests
    {
        static Rulebook Book(string lang) => Rulebook.Load(TestData.ReadText($"rulebook.{lang}.json"), $"rulebook.{lang}.json");

        [Test]
        public void M9_설명서찾기_절마다곳수와조각_보이는글그대로()
        {
            var ko = Book("ko");
            var hits = ko.Search("규토리");
            Assert.Greater(hits.Count, 10, "「규토리」는 여러 장에 나온다");
            // 곳 수 = 그 절의 보이는 글(제목 + 블록, 링크는 「[글]」)에서 센 수 · 표 순서
            foreach (var h in hits)
            {
                Assert.AreEqual(Rulebook.PlainTexts(h.Section).Sum(t => Rulebook.Count(t, "규토리")), h.Count, h.Section.Key);
                StringAssert.Contains("규토리", h.Snippet, h.Section.Key + " 조각에 찾은 말");
                Assert.LessOrEqual(h.Snippet.Length, Rulebook.SnippetBefore + "규토리".Length + Rulebook.SnippetAfter + 2 * Rulebook.Ellipsis.Length, "조각 길이");
            }
            var order = ko.Sections.Select(s => s.Key).ToList();
            CollectionAssert.AreEqual(hits.Select(h => h.Section.Key).OrderBy(k => order.IndexOf(k)), hits.Select(h => h.Section.Key), "표 순서(장 → 절)");
            Assert.IsTrue(hits.Any(h => h.Section.Key == "pokemon.apricorns"));
            // 링크는 보이는 글(「[제작 참조]」)로 찾는다 — 표시 문법 [[키|글]]의 키는 찾지 않는다
            Assert.IsTrue(ko.Search("제작 참조").Count > 0);
            Assert.AreEqual(0, ko.Search("crafting.balls").Count, "링크 대상 키는 보이는 글이 아니다");
            // 빈 말 · 공백만 = 결과 없음 · 없는 말 = 없음
            Assert.AreEqual(0, ko.Search("").Count);
            Assert.AreEqual(0, ko.Search("   ").Count);
            Assert.AreEqual(0, ko.Search("존재하지않는말").Count);
            // 영어: 대소문자 무시(글자 그대로 부분 일치 — 낱말 경계 없음)
            var en = Book("en");
            Assert.AreEqual(en.Search("apricorn").Sum(h => h.Count), en.Search("APRICORN").Sum(h => h.Count));
            Assert.Greater(en.Search("Apricorn").Count, 10);
            // cards 블록의 글은 게임 표에서 — more가 주는 글도 센다
            var strings = TestData.LoadStrings("ko");
            string name = strings["trainer_card.ace_trainer"];
            Assert.AreEqual(0, ko.Search(name).Count(h => h.Section.Key == "trainer_cards.cards"), "설명서에는 카드 이름이 없다(표 참조)");
            var withCards = ko.Search(name, s => s.Blocks.Where(b => b.Kind == RulebookBlockKind.Cards).SelectMany(b => b.Keys).Select(k => strings[k]));
            Assert.AreEqual(1, withCards.Single(h => h.Section.Key == "trainer_cards.cards").Count);
        }

        [Test]
        public void M9_설명서칩_표의절키가두언어에있다_겹침없이네개까지()
        {
            foreach (string lang in new[] { "ko", "en" })
            {
                var book = Book(lang);
                foreach (var kv in RulebookChips.Table)
                {
                    Assert.IsNotEmpty(kv.Value, kv.Key);
                    foreach (string key in kv.Value) Assert.IsNotNull(book.Find(key), $"{lang} {kv.Key} → {key}");
                }
            }
            // 앞 상황이 먼저 · 겹친 키는 한 번 · 많아도 Max개
            var chips = RulebookChips.For(new[] { "screen.encounter", "cell.apricorn_tree", RulebookChips.Turn });
            CollectionAssert.AreEqual(new[] { "pokemon.encounter", "pokemon.capture", "pokemon.apricorns", "pokemon.tiers" }, chips);
            Assert.AreEqual(RulebookChips.Max, RulebookChips.For(new[] { "screen.battle", "screen.craft" }).Count);
            CollectionAssert.AreEqual(new[] { "overview.village", "automation.fly", "overview.turn", "overview.goal" }, RulebookChips.For(new[] { "cell.village", "모르는 상황", null, RulebookChips.Turn }));
            CollectionAssert.AreEqual(new[] { "overview.turn", "overview.goal" }, RulebookChips.For(new[] { RulebookChips.Turn }), "아무 일도 없는 칸");
            // 고정물 종류마다 상황 줄이 있다(cell.<종류 snake>)
            foreach (var kind in Enum.GetNames(typeof(Sinnoh.Rules.FixtureKind)))
                Assert.IsTrue(RulebookChips.Table.ContainsKey("cell." + Snake(kind)), kind);
        }

        // JSON 문자열 글(따옴표 · 역슬래시만 — 손 표는 ASCII 밖 글자를 그대로 쓴다)
        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static string Snake(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        [Test]
        public void M9_크레딧표_로더가읽고_손표와묶음이같다_엄격하다()
        {
            string json = TestData.ReadText("credits.json");
            var table = CreditsTable.Load(json);
            CollectionAssert.AreEqual(new[] { "original", "pokemon", "art", "sound", "program" }, table.Sections.Select(s => s.Id), "CREDITS.md의 절 순서");
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            Assert.AreEqual(table.Sections.Count, manifest.Find("credits").Rows);
            CollectionAssert.DoesNotContain(manifest.ContentTables, "credits", "엔진은 크레딧을 읽지 않는다");
            // 화면 표 = 보이는 글 roll.json 그대로(항목 수 · 탭 이름 · 이름 · 역할이 그 글에 있다 — 시험 어셈블리는 JSON 라이브러리를 쓰지 않아 글로 본다)
            string root = TestData.RepoRoot();
            string Credits(string f) => File.ReadAllText(Path.Combine(root, "Tools/assets/credits", f), new UTF8Encoding(false));
            string roll = Credits("roll.json");
            Assert.AreEqual(Regex.Matches(roll, "\"who\"\\s*:").Count, table.Sections.Sum(s => s.Entries.Count), "항목 수");
            foreach (var s in table.Sections)
            {
                StringAssert.Contains(Quote(s.Title), roll, s.Id + " 제목");
                StringAssert.Contains(Quote(s.TitleEn), roll, s.Id + " 영어 제목");
                foreach (var e in s.Entries)
                    foreach (string t in new[] { e.Who, e.Role, e.RoleEn }) StringAssert.Contains(Quote(t), roll, s.Id);
            }
            var all = table.Sections.SelectMany(s => s.Entries).ToList();
            CollectionAssert.AllItemsAreUnique(all.Select(e => e.Who), "한 사람(곳) = 항목 하나");
            // 작가 이름 목록: Smogon 작가 25 · 쇼다운 작가 둘(빠짐없이 — 작가 표의 이름)
            var smogon = all.Single(e => e.Who == "Smogon Sprite Project");
            Assert.AreEqual(25, smogon.Names.Count, "Smogon 작가");
            string authors = Credits("smogon-authors.json");
            foreach (string n in smogon.Names) StringAssert.Contains(n, authors);
            CollectionAssert.AreEquivalent(new[] { "kyledove", "ZacWeavile" }, all.Single(e => e.Who == "Pokémon Showdown").Names);
            Assert.AreEqual(2, all.Count(e => e.Names != null), "이름 목록은 Smogon · 쇼다운만");
            // 보이는 글에 URL(줄이 바뀌지 않는다) · 개발 메모(결정 번호 · 추정 표지 · 내부 경로 · 원작 자산을 뽑은 과정)가 없다
            var dev = new Regex(@"://|결정 \d|\[(가정|추정)|(Tools|Assets|Gen)/|(?<![프크])원작 자산|변환|원작 자산");
            foreach (var s in table.Sections)
            {
                foreach (string t in new[] { s.Title, s.TitleEn }) Assert.IsFalse(dev.IsMatch(t), $"{s.Id}: {t}");
                foreach (var e in s.Entries)
                    foreach (string t in new[] { e.Who, e.Role, e.RoleEn }.Concat(e.Names ?? new List<string>()))
                        Assert.IsFalse(dev.IsMatch(t), $"{s.Id}: {t}");
            }
            // 결정 172: 한국어는 우리 번역 — 참고한 한국어판(번역자)은 크레딧에 적지 않는다
            foreach (string gone in new[] { "3JA2", "로씨온" }) Assert.IsFalse(all.Any(e => e.Who == gone), gone);
            Assert.IsFalse(all.Any(e => e.Role.Contains("한국어판") || e.RoleEn.Contains("Korean edition")), "한국어판 역할 줄");
            // 엄격: 빈 이름 목록 · 빈 탭 · 겹친 id · 빠진 who · 빠진 영어 제목
            const string entry = "{\"who\":\"w\",\"role\":\"r\",\"roleEn\":\"r\",\"names\":null}";
            Assert.Throws<FormatException>(() => CreditsTable.Load(json.Replace("\"names\":null", "\"names\":[]")));
            Assert.Throws<FormatException>(() => CreditsTable.Load("{\"schema\":1,\"rows\":[{\"id\":\"a\",\"title\":\"t\",\"titleEn\":\"t\",\"entries\":[]}]}"));
            Assert.Throws<FormatException>(() => CreditsTable.Load("{\"schema\":1,\"rows\":[{\"id\":\"a\",\"title\":\"t\",\"titleEn\":\"t\",\"entries\":[" + entry + "]},{\"id\":\"a\",\"title\":\"t\",\"titleEn\":\"t\",\"entries\":[" + entry + "]}]}"));
            Assert.Throws<FormatException>(() => CreditsTable.Load("{\"schema\":1,\"rows\":[{\"id\":\"a\",\"title\":\"t\",\"titleEn\":\"t\",\"entries\":[{\"role\":\"r\",\"roleEn\":\"r\",\"names\":null}]}]}"));
            Assert.Throws<FormatException>(() => CreditsTable.Load("{\"schema\":1,\"rows\":[{\"id\":\"a\",\"title\":\"t\",\"entries\":[" + entry + "]}]}"));
            Assert.DoesNotThrow(() => CreditsTable.Load("{\"schema\":1,\"rows\":[{\"id\":\"a\",\"title\":\"t\",\"titleEn\":\"t\",\"entries\":[" + entry + "]}]}"));
        }
    }
}
