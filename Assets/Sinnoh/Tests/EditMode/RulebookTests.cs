using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 「설명서 글 ① 한국어」: 설명서 표(`Data/Json/rulebook.ko.json` — build.mjs가 `Tools/data/authored/rulebook/`에서 만든다) · 로더(<see cref="Rulebook"/>) ·
    /// 완료 조건 2의 한국어 몫(고친 오역마다 rulings 근거 — 근거 ID가 rulings에 있는지는 build.mjs가 본다. 시험은 `Research/`를 읽지 않는다).
    /// </summary>
    public class RulebookTests
    {
        const string File = "rulebook.ko.json";
        const int Pages = 28;

        static string Json() => TestData.ReadText(File);
        static Rulebook Ko() => Rulebook.Load(Json(), File);
        static Rulebook En() => Rulebook.Load(TestData.ReadText("rulebook.en.json"), "rulebook.en.json");

        static IEnumerable<string> FiguresOf(RulebookSection s) => s.Blocks.Where(b => b.Kind == RulebookBlockKind.Figure).Select(b => b.Figure);
        static IEnumerable<string> CardsOf(RulebookSection s) => s.Blocks.Where(b => b.Kind == RulebookBlockKind.Cards).SelectMany(b => b.Keys);

        static string ReplaceFirst(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(i, 0, $"견본에 「{from}」가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }

        [Test]
        public void M9_설명서_로더가읽고_링크가끊기지않는다()
        {
            var book = Ko();
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            Assert.AreEqual(book.Sections.Count, manifest.Find("rulebook.ko").Rows, "manifest 행 = 절 수");
            CollectionAssert.DoesNotContain(manifest.ContentTables, "rulebook.ko", "엔진은 설명서를 읽지 않는다");
            int links = 0;
            foreach (var s in book.Sections)
                foreach (string to in s.Links)
                {
                    links++;
                    Assert.IsNotNull(book.Find(to), $"{s.Key} → {to}");
                    Assert.AreNotEqual(s.Key, to, $"{s.Key}: 자기 절 링크");
                }
            Assert.GreaterOrEqual(links, 10, "원본의 「[… 참조]」 링크(4·5·7·14·16·17쪽)");
            Assert.AreEqual("[제작 참조]", Rulebook.Plain("[[crafting.balls|제작 참조]]"));
            var capture = book.Find("pokemon.capture");
            Assert.IsNotNull(capture);
            CollectionAssert.Contains(capture.Links, "crafting.balls", "몬스터볼 [제작 참조]");
            Assert.IsTrue(book.Sections.All(s => s.Blocks.SelectMany(b => b.Texts()).All(t => Rulebook.Plain(t).IndexOf("[[", StringComparison.Ordinal) < 0)), "보이는 글에 링크 표시가 남지 않는다");
        }

        [Test]
        public void M9_설명서_절키유일_원본쪽빠짐없음_장순서()
        {
            var book = Ko();
            Assert.AreEqual(book.Sections.Count, book.Sections.Select(s => s.Key).Distinct().Count());
            var pages = new HashSet<int>(book.Sections.Where(s => s.Source == Rulebook.BaseSource).SelectMany(s => s.Pages));
            for (int p = 1; p <= Pages; p++) Assert.IsTrue(pages.Contains(p), $"원본 {p}쪽이 어느 절에도 없다");
            Assert.IsTrue(pages.All(p => p >= 1 && p <= Pages));
            CollectionAssert.AreEqual(new[] { "intro", "overview", "pokemon", "automation", "battle", "battle_faq", "crafting", "stamps", "hidden_spots", "shrines", "heart", "ultra_burst", "faq", "credits", "trainer_cards", "ultra_onslaught", "recipes" },
                book.Chapters(), "장 순서(원본 목차 → 확장 장) — 장마다 절이 이어져 있다");
            Assert.AreEqual(book.Chapters().Count, book.Sections.Select(s => s.Chapter).Distinct().Count(), "한 장의 절이 흩어지지 않는다");
        }

        [Test]
        public void M9_설명서_고친곳마다근거와원문_고친글이절글에있다()
        {
            var book = Ko();
            var fixes = book.Sections.SelectMany(s => s.Fixes.Select(f => (s, f))).ToList();
            Assert.GreaterOrEqual(fixes.Count, 18, "rulings 근거 오역(A4·A5·A6·A13·A14·A15·A22·A25·A26·A30·A51·A53·A55)");
            CollectionAssert.AreEqual(Enumerable.Range(1, fixes.Count).ToArray(), fixes.Select(x => x.f.No).ToArray(), "번호는 1부터 차례로");
            foreach (var (s, f) in fixes)
            {
                StringAssert.IsMatch(@"^(A[0-9]+(-[0-9]+)?|G[0-9]+)$", f.Ruling, $"{s.Key} 고침 {f.No}");
                Assert.IsTrue(Regex.IsMatch(f.En, "[A-Za-z]"), $"{s.Key} 고침 {f.No}: 영문 원본");
                Assert.IsTrue(Regex.IsMatch(f.Ko, "[가-힣]"), $"{s.Key} 고침 {f.No}: 한국어판 원문");
                string shown = string.Join("\n", s.Blocks.SelectMany(b => b.Texts()).Select(Rulebook.Plain));
                StringAssert.Contains(f.Now, shown, $"{s.Key} 고침 {f.No}: 고친 글");
            }
            // 대표 고침: A14 마스터 트레이너 축복 0 · A4 은행상회 「거래 1개 또는 2개」
            Assert.IsTrue(fixes.Any(x => x.s.Key == "ultra_burst.setup" && x.f.Ruling == "A14"));
            Assert.IsTrue(fixes.Any(x => x.s.Key == "overview.village" && x.f.Ruling == "A4"));
        }

        [Test]
        public void M9_설명서_그림자리_디지털판꼬리표_크레딧()
        {
            var book = Ko();
            var figures = book.Sections.SelectMany(s => s.Blocks.Where(b => b.Kind == RulebookBlockKind.Figure).Select(b => (s, b))).ToList();
            Assert.Greater(figures.Count, 0);
            Assert.AreEqual(figures.Count, figures.Select(x => x.b.Figure).Distinct().Count(), "그림 키 유일");
            foreach (var (s, b) in figures) StringAssert.StartsWith(s.Key + "/", b.Figure);
            Assert.IsTrue(book.Sections.Any(s => s.Blocks.Any(b => b.Kind == RulebookBlockKind.Digital)), "TTS 조작 문장에 「디지털판」 꼬리표(결정 111 ④)");
            Assert.IsTrue(book.Find("automation.moving").Blocks.Any(b => b.Kind == RulebookBlockKind.Digital), "자동화 장은 절마다 꼬리표");
            string credits = string.Join("\n", book.Find("credits.credits").Blocks.SelectMany(b => b.Texts()));
            StringAssert.Contains("3JA2", credits, "번역자 크레딧 유지(결정 35)");
            StringAssert.Contains("로씨온", credits);
        }

        [Test]
        public void M9_설명서_로더는엄격하다()
        {
            string json = Json();
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "[[crafting.balls|", "[[crafting.nowhere|"), File), "링크 대상 없음");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"kind\":\"p\"", "\"kind\":\"para\""), File), "모르는 kind");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"key\":\"overview.turn\"", "\"key\":\"overview.goal\""), File), "절 키 겹침");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"key\":\"overview.turn\"", "\"key\":\"pokemon.turn\""), File), "절 키의 장 ≠ chapter");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"ruling\":\"A14\"", "\"ruling\":\"14\""), File), "근거 문법");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"now\":\"", "\"now\":\"없는 글 "), File), "고친 글이 절 글에 없다");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"chapter\":\"overview\"", "\"chapter\":\"overview\",\"extra\":1"), File), "모르는 필드");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"source\":\"rulebook\"", "\"source\":\"\""), File), "원천이 빔");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"item.poke_ball\"", "\"Item Poke Ball\""), File), "카드 키 문법");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"item.greater_ball\"", "\"item.poke_ball\""), File), "카드 키 겹침");
            Assert.Throws<FormatException>(() => Rulebook.Load(ReplaceFirst(json, "\"kind\":\"cards\",\"keys\"", "\"kind\":\"p\",\"keys\""), File), "p에 keys");
        }

        [Test]
        public void M9_설명서_두언어_절키_그림키_카드키가같다_완료조건2()
        {
            Rulebook ko = Ko(), en = En();
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            Assert.AreEqual(en.Sections.Count, manifest.Find("rulebook.en").Rows, "manifest 행 = 절 수");
            CollectionAssert.DoesNotContain(manifest.ContentTables, "rulebook.en");
            CollectionAssert.AreEqual(ko.Sections.Select(s => s.Key).ToArray(), en.Sections.Select(s => s.Key).ToArray(), "절 키 · 순서");
            CollectionAssert.AreEqual(ko.Chapters().ToArray(), en.Chapters().ToArray(), "장 순서");
            for (int i = 0; i < ko.Sections.Count; i++)
            {
                RulebookSection k = ko.Sections[i], e = en.Sections[i];
                Assert.AreEqual(k.Source, e.Source, $"{k.Key}: 원천");
                CollectionAssert.AreEqual(k.Pages.ToArray(), e.Pages.ToArray(), $"{k.Key}: 쪽");
                CollectionAssert.AreEqual(FiguresOf(k).ToArray(), FiguresOf(e).ToArray(), $"{k.Key}: 그림 키");
                CollectionAssert.AreEqual(CardsOf(k).ToArray(), CardsOf(e).ToArray(), $"{k.Key}: 카드 키");            }
            int links = 0;
            foreach (var s in en.Sections)
                foreach (string to in s.Links) { links++; Assert.IsNotNull(en.Find(to), $"en {s.Key} → {to}"); }
            Assert.GreaterOrEqual(links, 10, "영문 원본의 「[See …]」 링크");
            Assert.IsTrue(en.Sections.All(s => s.Fixes.Count == 0), "영어는 원본 그대로 — 고친 곳 없음");
            Assert.IsTrue(en.Sections.SelectMany(s => s.Blocks).Where(b => b.Kind == RulebookBlockKind.Digital).All(b => b.Text.StartsWith("In this game, ", StringComparison.Ordinal)), "꼬리표 영어판(plan §5 #307)");
            Assert.IsFalse(en.Sections.SelectMany(s => s.Blocks).SelectMany(b => b.Texts()).Any(t => Regex.IsMatch(t, "[가-힣]")), "영어판에 한글 없음");
        }

        [Test]
        public void M9_설명서_확장장_카드참조가게임표에있고_빠짐없다()
        {
            StringTable ko = TestData.LoadStrings("ko"), en = TestData.LoadStrings("en");
            var cards = Ko().Sections.SelectMany(CardsOf).ToList();
            foreach (string key in cards)
            {
                Assert.IsTrue(ko.Contains(key), $"strings.ko {key}");
                Assert.IsTrue(en.Contains(key), $"strings.en {key}");
            }
            var trainers = ko.Keys.Where(k => Regex.IsMatch(k, @"^trainer_card\.[a-z_]+$")).ToList();
            Assert.AreEqual(51, trainers.Count);
            CollectionAssert.IsSubsetOf(trainers, cards, "트레이너 카드 51장 모두");
            CollectionAssert.IsSubsetOf(ko.Keys.Where(k => Regex.IsMatch(k, @"^ultra_onslaught\.[0-9]+$")).ToList(), cards, "울트라 온슬롯 카드 모두");
            var recipes = TestData.LoadItems().Items.Where(i => i.Recipe != null && i.Recipe.Count > 0).Select(i => i.Key).ToList();
            Assert.AreEqual(14, recipes.Count, "레시피 14(볼 4 · 스튜 4 · 배틀 아이템 6)");
            CollectionAssert.IsSubsetOf(recipes, cards, "레시피 있는 아이템 모두");
            var book = Ko();
            foreach (var (chapter, source, pages) in new[] { ("trainer_cards", "trainer_card_board", 1), ("ultra_onslaught", "ultra_onslaught_deck", 1), ("recipes", "crafting_recipes", 5) })
            {
                var secs = book.Sections.Where(s => s.Chapter == chapter).ToList();
                Assert.IsTrue(secs.Count > 0 && secs.All(s => s.Source == source), $"{chapter}: 원천 {source}");
                CollectionAssert.AreEquivalent(Enumerable.Range(1, pages).ToArray(), secs.SelectMany(s => s.Pages).Distinct().ToArray(), $"{chapter}: 원천 쪽 빠짐 없음");
            }
            Assert.IsTrue(book.Find("trainer_cards.rules").Fixes.Any(f => f.Ruling == "A20-1"), "보드 한국어 「앞면」 빠짐(A20-1)");
        }
    }
}
