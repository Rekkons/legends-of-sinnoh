using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-1e: FigureImport(Unity)·build.mjs audio가 만든 Gen/ 표를 System.IO로만 본다(03 §7, dotnet test와 같은 소스).
    /// 프리팹·클립·렌더는 엔진 시험(Sinnoh.Tests.EditorAssets)이 본다. Gen/이 없으면 건너뛴다.
    /// </summary>
    public class GenFigureTableTests
    {
        static string Root => TestData.RepoRoot();
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly string[] EffectKeys = { "Idle", "Walk", "Run", "Faint", "PhysicalAttack", "SpecialAttack", "StatusAttack", "TakeDamage" };

        static string GenText(string rel)
        {
            string p = Path.Combine(Root, "Assets/Sinnoh/Gen", rel);
            if (!File.Exists(p)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (" + rel + ")");
            return File.ReadAllText(p, Utf8);
        }

        static List<string> Rows(string json) => json.Split('\n').Where(l => l.StartsWith("{\"") && !l.StartsWith("{\"schema\"")).Select(l => l.TrimEnd(',')).ToList();

        [Test]
        public void M4_피규어표는목록37행과효과8()
        {
            string list = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/figure_list.json"), Utf8);
            var want = Regex.Matches(list, "\\{\"dex\":([0-9]+),\"state\":([0-9]+),[^\n]*\"required\":(true|false)").Cast<Match>()
                .Select(m => (key: m.Groups[1].Value + ":" + m.Groups[2].Value, required: m.Groups[3].Value == "true")).ToList();
            Assert.AreEqual(37, want.Count, "figure_list = 33종 + 폼 4");
            Assert.AreEqual(21, want.Count(w => w.required), "필수 21");

            string json = GenText("Figures/figures.json");
            StringAssert.StartsWith("{\"schema\":1,\"importer\":", json);
            var rows = Rows(json);
            Assert.AreEqual(want.Select(w => w.key).ToList(), rows.Select(r => Regex.Match(r, "^\\{\"key\":\"([0-9]+:[0-9]+)\"").Groups[1].Value).ToList(), "figures.json 키 = figure_list 순서");
            foreach (var r in rows)
            {
                string key = Regex.Match(r, "^\\{\"key\":\"([^\"]+)\"").Groups[1].Value;
                Assert.Greater(double.Parse(Regex.Match(r, "\"fitScale\":([0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0, key + " fitScale");
                long bundle = long.Parse(Regex.Match(r, "\"bundle\":(-?[0-9]+)").Groups[1].Value);
                Assert.That(bundle, Is.InRange(1L, 3_000_000L), key + ": 번들 크기 기록(바이트), 목표 3 MB = 3,000,000바이트 이하(03 §2-5)");
                Assert.That(Regex.Match(r, "\"shaders\":\\[([^\\]]*)\\]").Groups[1].Value, Does.Not.Contain("→other"), key + ": 모르는 셰이더");
                string clips = Regex.Match(r, "\"clips\":\\{(.*)\\}\\}$").Groups[1].Value;
                foreach (var e in EffectKeys)
                {
                    var m = Regex.Match(clips, "\"" + e + "\":\\{\"clip\":\"([^\"]+)\",\"length\":([0-9.]+)\\}");
                    Assert.IsTrue(m.Success, key + ": 효과 " + e);
                    Assert.Greater(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 0.1, key + ": " + e + " 길이");
                }
            }
        }
    }
}
