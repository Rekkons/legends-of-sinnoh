using System;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 결정 213 화면의 표(`Data/Json/rulings.&lt;언어&gt;.json` — build.mjs lib/rulings.mjs) · 로더 · 찾기와 거르기(<see cref="Rulings"/>).
    /// </summary>
    public class RulingsTests
    {
        static Rulings Of(string lang) => Rulings.Load(TestData.ReadText("rulings." + lang + ".json"), "rulings." + lang + ".json");

        const string Head = "{\"schema\":1,\"rows\":[\n";

        static string Row(string id, string section, string q, string a, string source = "rulebook", string group = "basic") =>
            "{\"id\":\"" + id + "\",\"group\":\"" + group + "\",\"groupTitle\":\"G\",\"section\":\"" + section + "\",\"sectionTitle\":\"S\",\"q\":\"" + q + "\",\"a\":\"" + a + "\",\"source\":\"" + source + "\"}";

        static Rulings Sample(params string[] rows) => Rulings.Load(Head + string.Join(",\n", rows) + "\n]}\n", "sample");

        [Test]
        public void M10_결정213_판정표를읽고_두언어의묶음절항목이같다()
        {
            var ko = Of("ko");
            var en = Of("en");
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            Assert.AreEqual(ko.Items.Count, manifest.Find("rulings.ko").Rows, "manifest 행 = 항목 수");
            CollectionAssert.DoesNotContain(manifest.ContentTables, "rulings.ko", "엔진은 판정 표를 읽지 않는다");
            CollectionAssert.AreEqual(new[] { "basic", "battle", "campaign", "trainer", "ultra" }, ko.Groups.Select(g => g.Key).ToList(), "묶음 다섯");
            CollectionAssert.AreEqual(ko.Items.Select(r => r.Section + "/" + r.Id + "/" + r.Source).ToList(), en.Items.Select(r => r.Section + "/" + r.Id + "/" + r.Source).ToList());
            Assert.Greater(ko.Items.Count, 100);
            Assert.AreEqual(ko.Items.Count, ko.Groups.Sum(g => g.Count));
            foreach (var g in ko.Groups) Assert.AreEqual(g.Count, g.Sections.Sum(s => s.Count), g.Key);
            foreach (RulingSource s in Enum.GetValues(typeof(RulingSource))) Assert.That(ko.Items.Any(r => r.Source == s), s + " 출처의 항목이 있다");
        }

        [Test]
        public void M10_결정213_찾기는대소문자공백이음표를무시하고_낱말은모두들어있어야한다()
        {
            var t = Sample(
                Row("r001", "basic.a", "Alpha Strike는 몇 번 쓰나요?", "한 번만 씁니다."),
                Row("r002", "basic.a", "공중날기 비용", "규토리 1개입니다."),
                Row("r003", "basic.b", "규토리를 한 번에 여러 개 낼 수 있나요?", "네."));
            Assert.AreEqual("alphastrike는", Rulings.Fold(" Alpha\u2060 Strike는\n"));
            CollectionAssert.AreEqual(new[] { "alpha", "strike" }, Rulings.Terms("  ALPHA\tstr\u2060ike "));
            CollectionAssert.IsEmpty(Rulings.Terms(" \u2060 "));
            Assert.AreEqual(3, t.Filter(null).Count, "빈 말 = 전부");
            Assert.AreEqual(3, t.Filter("   ").Count);
            CollectionAssert.AreEqual(new[] { "r001" }, t.Filter("alpha strike").Select(r => r.Id).ToList(), "대소문자 무시");
            CollectionAssert.AreEqual(new[] { "r001" }, t.Filter("ALPHASTRIKE").Select(r => r.Id).ToList(), "글 쪽 공백 무시");
            CollectionAssert.AreEqual(new[] { "r001", "r003" }, t.Filter("한번").Select(r => r.Id).ToList(), "띄어 쓴 글을 붙여 찾는다(물음 · 답 모두)");
            CollectionAssert.AreEqual(new[] { "r002" }, t.Filter("규\u2060토\u2060리 비용").Select(r => r.Id).ToList(), "U+2060 무시 · 낱말은 모두(AND) · 물음과 답에 나뉘어 있어도");
            CollectionAssert.AreEqual(new[] { "r002" }, t.Filter("비용 규토리").Select(r => r.Id).ToList(), "낱말 순서 무관");
            CollectionAssert.IsEmpty(t.Filter("규토리 없는말"));
            CollectionAssert.IsEmpty(t.Filter("비용규토리"), "물음 끝과 답 처음은 이어 붙지 않는다");
        }

        [Test]
        public void M10_결정213_묶음절출처로거른다()
        {
            var t = Sample(
                Row("r001", "basic.a", "q1", "a", "creator"),
                Row("r002", "basic.b", "q2", "a", "rulebook"),
                Row("r003", "battle.a", "q3", "a", "house", "battle"),
                Row("r004", "battle.a", "q4", "a", "creator", "battle"));
            CollectionAssert.AreEqual(new[] { "basic", "battle" }, t.Groups.Select(g => g.Key).ToList());
            CollectionAssert.AreEqual(new[] { 1, 1 }, t.Groups[0].Sections.Select(s => s.Count).ToList());
            Assert.AreEqual(Rulings.AllSources, Rulings.Bit(RulingSource.Creator) | Rulings.Bit(RulingSource.Rulebook) | Rulings.Bit(RulingSource.House));
            CollectionAssert.AreEqual(new[] { "r003", "r004" }, t.Filter("", "battle").Select(r => r.Id).ToList());
            CollectionAssert.AreEqual(new[] { "r002" }, t.Filter("", "basic", "basic.b").Select(r => r.Id).ToList());
            CollectionAssert.AreEqual(new[] { "r001", "r004" }, t.Filter("", sources: Rulings.Bit(RulingSource.Creator)).Select(r => r.Id).ToList());
            CollectionAssert.AreEqual(new[] { "r004" }, t.Filter("q4", "battle", null, Rulings.Bit(RulingSource.Creator) | Rulings.Bit(RulingSource.House)).Select(r => r.Id).ToList());
            CollectionAssert.IsEmpty(t.Filter("", sources: 0));
        }

        [Test]
        public void M10_결정213_로더는엄격하다()
        {
            string ok = Row("r001", "basic.a", "q", "a");
            Assert.DoesNotThrow(() => Sample(ok));
            Assert.Throws<FormatException>(() => Rulings.Load(Head + "]}\n", "sample"), "빈 표");
            Assert.Throws<FormatException>(() => Sample(ok, ok), "겹친 id");
            Assert.Throws<FormatException>(() => Sample(Row("r1", "basic.a", "q", "a")), "id 문법");
            Assert.Throws<FormatException>(() => Sample(Row("r001", "battle.a", "q", "a")), "절 키의 묶음");
            Assert.Throws<FormatException>(() => Sample(Row("r001", "basic.a", "q", "a", "guess")), "모르는 출처");
            Assert.Throws<FormatException>(() => Sample(Row("r001", "basic.a", " ", "a")), "빈 물음");
            Assert.Throws<FormatException>(() => Sample(ok, Row("r002", "basic.b", "q", "a"), Row("r003", "basic.a", "q", "a")), "흩어진 절");
            Assert.Throws<FormatException>(() => Sample(ok.Replace(",\"source\":\"rulebook\"", "")), "빠진 필드");
            Assert.Throws<FormatException>(() => Sample(ok.Replace("\"q\":", "\"extra\":1,\"q\":")), "모르는 필드");
        }
    }
}
