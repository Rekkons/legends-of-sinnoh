using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-1b: 받기 원천 표(추적)만 읽는 시험 — Gen/·AssetSources/(git 밖)가 없어도 돈다.
    /// sources.lock.json이 설계가 정한 원천만 담는지(03 §2, 받기 규칙), 트레이너 원천 집계(03 §2-3, 결정 8 보충),
    /// 도트 폼·애니 예외 표(03 §2-1·§7, battle-assets §4-4), 크레딧 자료(03 §6).
    /// </summary>
    public class AssetSourceTests
    {
        static string ToolsText(string rel)
        {
            string file = Path.Combine(TestData.RepoRoot(), "Tools/assets", rel);
            if (!File.Exists(file)) Assert.Ignore("공개본에는 원작 자산 도구가 없다: Tools/assets/" + rel);
            return File.ReadAllText(file, new UTF8Encoding(false));
        }

        /// <summary>한 줄 = 한 행인 표에서 열 하나(문자열)를 줄마다 뽑는다.</summary>
        static List<string> Column(string text, string field) =>
            Regex.Matches(text, "^\\{.*?\"" + field + "\":\"([^\"]*)\"", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        /// <summary>받기 원천(03 §2-1~§2-5·§9) → 받는 파일 수(fetch.mjs --list). 깃허브는 커밋 고정(fetch-lists.mjs PIN).</summary>
        static readonly (string Prefix, int Files, string What)[] Origins =
        {
            ("https://raw.githubusercontent.com/PokeAPI/sprites/1b3b7c0a538dd2226ae08d0d2f8955125075bbd4/sprites/pokemon/versions/generation-v/black-white/", 1344,
                "PokeAPI BW: 종 322 정지 앞·뒤 + 애니 앞 314·뒤 308, 폼 20 정지 앞·뒤 + 애니 19·19"),
            ("https://play.pokemonshowdown.com/sprites/trainers/", 18, "쇼다운 트레이너: 원작 자산에 없는 직업 14 + 화풍 다름 4(색 바꿈 바탕 2는 14 안)"),
            ("https://raw.githubusercontent.com/Levatius/pokemon-legends/c0b717c515751d3237ac0d68ed24e5ccbad991c8/assets/", 184, "원작 GitHub 카드 부품 25 + 마을 카드 1 + 운명 카드·카드 아이콘·토큰·주사위 151(결정 43 ⑪) + 사당 카드 시트 2 · 막 카드 뒷면 시트 1(결정 80 L12·L1 나) + 왜곡 균열 카드 시트 1(M6 그림 에셋) + 울트라 온슬롯 카드 시트 1(결정 87 V24 나) + 여행 스탬프 시트 · 도장 2(M9 다듬기 P12)"),
            ("https://raw.githubusercontent.com/quiple/galmuri/71e1cacf1437a11220307120e63e30bc275312d4/dist/", 3, "Galmuri11 TTF·BDF + OFL"),
            ("https://steamusercontent-a.akamaihd.net/ugc/", 141, "TTS: 피규어 번들 37 + 헥스 디퓨즈 5·토큰 그림 68 + 원작 mp3 31"),
            ("https://raw.githubusercontent.com/PokeAPI/pokeapi/6ea97b5641faa12e090866e8144ae8f57764fc64/data/v2/csv/", 1, "PokeAPI move_names.csv: 현행 공식 한국어 기술명(결정 43 ⑩·44 ②, 원작 자산 이름은 대조용)"),
            ("https://raw.githubusercontent.com/PokeAPI/cries/b3a9044bb839b5e3152aaa536c7b463a0fcf0140/cries/pokemon/legacy/", 283, "PokeAPI 울음 legacy #1~649(결정 31 ⑤ · 67 ① — M9 소리 ③)"),
            ("https://raw.githubusercontent.com/PokeAPI/cries/b3a9044bb839b5e3152aaa536c7b463a0fcf0140/cries/pokemon/latest/", 39 + 3, "PokeAPI 울음 latest #650+(legacy 없음) + 네크로즈마 폼 울음 3(10155~10157 — 결정 143 ④)"),
        };

        [Test]
        public void M4_소스잠금은설계원천만()
        {
            string text = ToolsText("sources.lock.json");
            Assert.IsFalse(text.Contains("\r"), "LF만");
            var lines = text.Split('\n');
            Assert.AreEqual("{\"schema\":1,\"rows\":[", lines[0]);
            Assert.AreEqual("]}", lines[lines.Length - 2]);
            Assert.AreEqual("", lines[lines.Length - 1], "끝 줄바꿈");
            var row = new Regex("^\\{\"url\":\"(https://[^\"]+)\",\"sha256\":\"[0-9a-f]{64}\",\"bytes\":([1-9][0-9]*)\\}(,?)$");
            var urls = new List<string>();
            for (int i = 1; i < lines.Length - 2; i++)
            {
                var m = row.Match(lines[i]);
                Assert.IsTrue(m.Success, $"lock {i + 1}행 형식: {lines[i]}");
                Assert.AreEqual(i < lines.Length - 3 ? "," : "", m.Groups[3].Value, $"lock {i + 1}행 쉼표");
                urls.Add(m.Groups[1].Value);
            }
            var sorted = urls.OrderBy(u => u, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(sorted, urls, "url 순(서수)");
            Assert.AreEqual(urls.Count, urls.Distinct().Count(), "url 겹침 없음");
            foreach (var u in urls)
                Assert.IsTrue(Origins.Any(o => u.StartsWith(o.Prefix, StringComparison.Ordinal)), $"설계에 없는 원천: {u}");
            foreach (var o in Origins)
                Assert.AreEqual(o.Files, urls.Count(u => u.StartsWith(o.Prefix, StringComparison.Ordinal)), o.What);
            Assert.AreEqual(1691 + 322 + 3, urls.Count, "fetch.mjs --list all 합계(+ 울음 322 — M9 소리 ③ · + 폼 울음 3 — 결정 143 ④)");
        }

        [Test]
        public void M4_트레이너원천집계51()
        {
            string table = ToolsText("tables/trainer_sprites.json");
            var keys = Column(table, "key");
            var sources = Column(table, "source");
            CollectionAssert.AreEqual(Column(TestData.ReadText("trainer_cards.json"), "key"), keys, "trainer_cards 51 순서 그대로");
            Assert.AreEqual(51, sources.Count);
            Assert.AreEqual(28, sources.Count(s => s.StartsWith("rom:pt")), "Pt 원작 자산");
            Assert.AreEqual(2, sources.Count(s => s.StartsWith("rom:hgss")), "HGSS 원작 자산(포켓몬 매니아·로켓단)");
            Assert.AreEqual(1, sources.Count(s => s.StartsWith("rom:bw")), "BW 원작 자산(백팩커)");
            var showdown = sources.Where(s => s.StartsWith("showdown:")).Select(s => s.Substring(9)).ToList();
            var styled = Regex.Matches(table, "^\\{.*\"style\":\"gen6\"", RegexOptions.Multiline).Count;
            Assert.AreEqual(14, showdown.Count - styled, "쇼다운(원작 자산에 없는 직업)");
            Assert.AreEqual(4, styled, "화풍 다름(Expert·Hex Maniac·Kindler·Triathlete)");
            var recolor = sources.Where(s => s.StartsWith("recolor:")).Select(s => s.Substring(8)).ToList();
            CollectionAssert.AreEqual(new[] { "diamondclanmember", "pearlclanmember" }, recolor, "색 바꿈 2(Platinum·Undivided Acolyte)의 바탕");
            foreach (var b in recolor) CollectionAssert.Contains(showdown, b, "색 바꿈 바탕은 받는 쇼다운 그림");
            Assert.AreEqual(showdown.Count, showdown.Distinct().Count(), "쇼다운 파일 겹침 없음");

            string credits = ToolsText("credits/showdown-trainers.json");
            CollectionAssert.AreEquivalent(showdown.Select(f => f + ".png"), Column(credits, "file"), "쓰는 쇼다운 파일마다 크레딧 행(03 §6)");
        }

        [Test]
        public void M4_도트폼표와애니예외()
        {
            string pokemon = TestData.ReadText("pokemon.json");
            var keys = new HashSet<string>(Column(pokemon, "key"));
            var dex = Regex.Matches(pokemon, "^\\{.*?\"dex\":([0-9]+),", RegexOptions.Multiline).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
            Assert.AreEqual(322, dex.Count, "종 수");
            Assert.AreEqual(39, dex.Count(d => d >= 650), "#650 이후 = 공개 그림(정지 322 = 원작 자산 283 + 공개 39)");

            string forms = ToolsText("tables/sprite_forms.json");
            var formKeys = Column(forms, "key");
            Assert.AreEqual(20, formKeys.Count, "원작 자산·PokeAPI에 그림이 있는 폼");
            Assert.AreEqual(formKeys.Count, formKeys.Distinct().Count());
            Assert.AreEqual(formKeys.Count, Column(forms, "pokeapi").Distinct().Count(), "폼 파일 겹침 없음");
            foreach (var k in formKeys) Assert.IsTrue(keys.Contains(k), $"sprite_forms {k}가 pokemon.json에 없다");

            // battle-assets §4-4(PokeAPI·쇼다운 gen5ani 모두 같다, 2026-11-07 트리로 다시 확인) + 펄기아 오리진폼(애니 없음)
            var noFront = new[] { "680", "690", "723", "724", "757", "758", "799", "803", "484:2" };
            var noBack = noFront.Concat(new[] { "686", "691", "706", "793", "794", "805" }).ToArray();
            string ex = ToolsText("tables/sprite_exceptions.json");
            var rows = Regex.Matches(ex, "^\\{\"sprite\":\"([^\"]+)\",\"missing\":\\[([^\\]]*)\\]\\}", RegexOptions.Multiline).Cast<Match>().ToList();
            CollectionAssert.AreEquivalent(noFront, rows.Where(m => m.Groups[2].Value.Contains("\"animFront\"")).Select(m => m.Groups[1].Value), "애니 앞 없음 = 8 + 펄기아 오리진");
            CollectionAssert.AreEquivalent(noBack, rows.Where(m => m.Groups[2].Value.Contains("\"animBack\"")).Select(m => m.Groups[1].Value), "애니 뒤 없음 = 14 + 펄기아 오리진");
            Assert.AreEqual(rows.Count, Regex.Matches(ex, "\"sprite\":").Count, "sprite_exceptions 모든 행을 읽었다");
            foreach (var m in rows)
            {
                string s = m.Groups[1].Value;
                Assert.IsTrue(s.Contains(":") ? formKeys.Contains(s) : int.Parse(s) >= 650, $"예외 {s}: #650 이후 종이거나 폼(원작 자산 렌더로 바뀌지 않는다)");
            }

            // 크레딧(03 §6): 원작 자산에 없는 그림 = #650 이후 종 39 + 원작 자산 칸(rom 열)이 없는 폼 7(디아루가·펄기아 오리진, 킬가르도 블레이드·네크로즈마 3·모르페코 배고픈 모양).
            // 기준은 build.mjs sprites의 공개 원천과 같다(sprites.mjs romOf) — 디아루가·펄기아 오리진폼도 BW풍 팬 도트라 작가 표기 대상(검토 반영 M4-1)
            var smogon = Column(ToolsText("credits/smogon-sprites.json"), "sprite");
            var noRomForms = Regex.Matches(forms, "^\\{\"key\":\"([^\"]+)\",\"pokeapi\":\"[^\"]+\",\"form\":\"[^\"]+\"\\}", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(7, noRomForms.Count, "원작 자산 칸이 없는 폼");
            CollectionAssert.AreEquivalent(dex.Where(d => d >= 650).Select(d => d.ToString()).Concat(noRomForms), smogon, "Smogon 도트 크레딧 = 원작 자산에 없는 그림");
            Assert.AreEqual(46, smogon.Count, "Smogon 도트 크레딧 행");
        }

        // ---------- 크레딧(M9 「크레딧」 항목 몫, 03 §6 · 완료 조건 4) — 같은 검사를 build.mjs credits 묶음(lib/credits.mjs)도 한다 ----------
        static readonly Regex Url = new Regex("^https://[^\\s\"<>]+$");

        static Dictionary<string, List<string>> SmogonFiles() =>
            Regex.Matches(ToolsText("credits/smogon-sprites.json"), "^\\{\"sprite\":\"([^\"]+)\",.*\"files\":\\[([^\\]]*)\\]\\}", RegexOptions.Multiline).Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value).ToList());

        /// <summary>smogon-authors.json 행 → (그림, 파일 → 작가(null = 시트에서 못 찾음), note 있음)</summary>
        static List<(string Sprite, Dictionary<string, string> Files, bool Note)> SmogonAuthors() =>
            Regex.Matches(ToolsText("credits/smogon-authors.json"), "^\\{\"sprite\":\"([^\"]+)\",.*\"files\":\\{([^}]*)\\}(,\"note\":\"[^\"]+\")?\\},?$", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (m.Groups[1].Value,
                    Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\":(?:null|\"([^\"]+)\")").Cast<Match>().ToDictionary(p => p.Groups[1].Value, p => p.Groups[2].Success ? p.Groups[2].Value : null),
                    m.Groups[3].Success)).ToList();

        [Test]
        public void M9_크레딧_Smogon파일마다작가()
        {
            var files = SmogonFiles();
            Assert.AreEqual(46, files.Count, "원작 자산에 없는 그림");
            var rows = SmogonAuthors();
            CollectionAssert.AreEqual(files.Keys, rows.Select(r => r.Sprite), "smogon-authors 행 = smogon-sprites 행(같은 순서)");
            int unknown = 0;
            foreach (var r in rows)
            {
                CollectionAssert.AreEquivalent(files[r.Sprite], r.Files.Keys, r.Sprite + " 파일 목록");
                foreach (var kv in r.Files)
                {
                    if (kv.Key.EndsWith(".png")) Assert.IsNotNull(kv.Value, $"{r.Sprite} {kv.Key}: 정지 그림은 작가가 꼭 있다");
                    else if (kv.Value == null) { Assert.IsTrue(r.Note, $"{r.Sprite} {kv.Key}: 작가 null이면 note"); unknown++; }
                }
            }
            Assert.AreEqual(160, files.Values.Sum(f => f.Count), "쓴 팬 도트 파일(정지 92 · 애니 68)");
            Assert.AreEqual(9, unknown, "애니 작가 미확인(시트에 예약만 — Honedge · Aegislash 두 폼 · Inkay · Malamar)");
        }

        [Test]
        public void M9_크레딧_출처URL꼴과근거파일()
        {
            string root = TestData.RepoRoot();
            int urls = 0, entries = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Tools/assets/credits"), "*.json"))
            {
                string text = File.ReadAllText(file, new UTF8Encoding(false));
                string name = Path.GetFileName(file);
                foreach (Match m in Regex.Matches(text, "\"(http[^\"]*)\""))
                {
                    Assert.IsTrue(Url.IsMatch(m.Groups[1].Value), $"{name}: 출처 URL 꼴 {m.Groups[1].Value}");
                    urls++;
                }
                foreach (Match m in Regex.Matches(text, "^\\{\"id\":\"[^\"]+\",.*$", RegexOptions.Multiline))
                {
                    entries++;
                    foreach (var k in new[] { "what", "who", "usedFor", "evidence" }) StringAssert.Contains($"\"{k}\":", m.Value, $"{name} 항목 칸 {k}");
                    var ev = Regex.Match(m.Value, "\"evidence\":\\[([^\\]]+)\\]");
                    foreach (Match p in Regex.Matches(ev.Groups[1].Value, "\"([^\"]+)\""))
                    {
                        string rel = p.Groups[1].Value;
                        Assert.IsFalse(rel.StartsWith("Research/") || rel.Contains("/Gen/"), $"{name}: 근거 파일은 Research · Gen 밖 추적 파일 {rel}");
                        if (rel.StartsWith("Docs/") || rel.StartsWith("Tools/assets/lib/") || rel == "Tools/sources.json" || rel == "Tools/rebuild-all.mjs" || Regex.IsMatch(rel, "^Tools/assets/[^/]+\\.mjs$")) continue;   // 공개본에 없는 경로(원작 자산 도구 · Docs)
                        Assert.IsTrue(File.Exists(Path.Combine(root, rel)), $"{name}: 근거 파일 {rel} 없음");
                    }
                }
            }
            Assert.AreEqual(32, entries, "손 표 항목(원작 9 · 원작 자산 6 · 공개 6 · 도구 11 — 한국어판은 결정 172로 뺌)");
            Assert.Greater(urls, 40);
        }

        [Test]
        public void M9_크레딧_문서에파일마다작가()
        {
            // 언어별 두 벌(README · LICENSE · PRIVACY처럼): 영어 CREDITS.md · 한국어 CREDITS.ko.md, 맨 위에 서로 링크
            foreach (var (file, other, notice, license, artists) in new[] {
                ("CREDITS.ko.md", "[English](CREDITS.md)", "포켓몬스터 비공식 팬 게임 · 비영리", "LICENSE.ko.md", "- 작가: "),
                ("CREDITS.md", "[한국어](CREDITS.ko.md)", "Unofficial, non-profit Pokémon fan game", "LICENSE.md", "- Artists: ") })
            {
                string md = File.ReadAllText(Path.Combine(TestData.RepoRoot(), file), new UTF8Encoding(false));
                Assert.IsTrue(md.StartsWith(other + "\n"), file + ": 맨 위 다른 언어 링크");
                StringAssert.Contains(notice, md, file + ": 맨 위 고지(결정 118 ⑤)");
                StringAssert.Contains("`" + license + "`", md, file + ": 라이선스 가리킴");
                foreach (string word in new[] { "추출", "덤프", "결정 대기 15", "결정 ", "[가정", "[추정", "근거 파일", "쓰는 곳", "손으로 고치지", "Tools/", "Gen/" })
                    StringAssert.DoesNotContain(word, md, file + ": 읽을 사람에게 쓴 글 — 만든 과정 · 결정 번호 · 내부 경로를 쓰지 않는다");
                Assert.IsFalse(Regex.IsMatch(md, @"\b(?:roms?|dump(?:s|ed|ing)?|extract\w*|ripp(?:ed|ing)|decompil\w*)\b", RegexOptions.IgnoreCase), file + ": 원작 자산을 뽑은 과정의 영어 낱말");
                // 작가 이름: Smogon 그림 · 애니 작가와 쇼다운 트레이너 그림 작가가 작가 줄에 빠짐없이
                var lines = md.Split('\n').Where(l => l.TrimStart().StartsWith(artists)).ToList();
                Assert.AreEqual(2, lines.Count, file + ": Smogon · 쇼다운 작가 줄");
                var listed = new HashSet<string>(lines.SelectMany(l => l.Substring(l.IndexOf(':') + 1).Split('·').Select(n => n.Trim())));
                foreach (var r in SmogonAuthors())
                    foreach (var who in r.Files.Values.Where(v => v != null).SelectMany(v => v.Split(',')).Select(n => n.Trim()))
                        Assert.IsTrue(listed.Contains(who), $"{file} {r.Sprite}: 작가 {who}");
                foreach (Match m in Regex.Matches(ToolsText("credits/showdown-trainers.json"), "^\\{\"file\":\"([^\"]+)\",.*\"artist\":(?:null|\"([^\"]+)\")\\}", RegexOptions.Multiline))
                    if (m.Groups[2].Success) Assert.IsTrue(listed.Contains(m.Groups[2].Value), $"{file} {m.Groups[1].Value} 작가");
            }
        }
    }
}
