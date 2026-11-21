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
    /// plan M4-1d: Tools/assets/build.mjs가 만든 Gen/(git 밖, 본체는 결정 27로 추적 안 함)과 추적 표를 System.IO로만 본다(03 §7).
    /// Gen/이 없는 작업 트리(rebuild-all 전)에서는 Gen을 읽는 시험을 건너뛴다. 표만 보는 부분은 늘 돈다.
    /// 숫자는 03 §2-1·§2-3·§7과 plan §3 M4-1 완료 조건 ②의 문서 값이다(바뀌면 문서와 함께 고친다).
    /// </summary>
    public class GenAssetTests
    {
        static string Root => TestData.RepoRoot();
        static string Gen => Path.Combine(Root, "Assets/Sinnoh/Gen");
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        static string GenText(string rel)
        {
            string p = Path.Combine(Gen, rel);
            if (!File.Exists(p)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (" + rel + ")");
            return File.ReadAllText(p, Utf8);
        }
        static string Table(string rel) => File.ReadAllText(Path.Combine(Root, "Tools/assets/tables", rel), Utf8);
        static bool HasGen(string rel) => File.Exists(Path.Combine(Gen, rel));

        /// <summary>PNG 크기(IHDR).</summary>
        static (int W, int H) PngSize(string file)
        {
            Assert.IsTrue(File.Exists(file), "없음: " + file);
            var b = new byte[24];
            using (var f = File.OpenRead(file)) Assert.AreEqual(24, f.Read(b, 0, 24));
            Assert.AreEqual(0x89, b[0], file + ": PNG");
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            return (Be(16), Be(20));
        }

        static List<Match> Lines(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.Multiline).Cast<Match>().ToList();
        static int[] Ints(string csv) => csv.Split(',').Select(int.Parse).ToArray();

        // ---------- 로스터·표 ----------
        /// <summary>그림 키: 종 = 도감 번호, 폼 = sprite_forms 키. 원작 자산 칸 = #1~649 종은 bw:도감 번호, 폼은 rom 열.</summary>
        static Dictionary<string, string> SpriteRom()
        {
            string pokemon = TestData.ReadText("pokemon.json");
            var d = new Dictionary<string, string>();
            foreach (var m in Lines(pokemon, "^\\{\"key\":\"[^\"]+\",\"dex\":([0-9]+),"))
            {
                int dex = int.Parse(m.Groups[1].Value);
                d[dex.ToString()] = dex <= 649 ? "bw:" + dex : null;
            }
            foreach (var m in Lines(Table("sprite_forms.json"), "^\\{\"key\":\"([^\"]+)\",\"pokeapi\":\"[^\"]+\",\"form\":\"[^\"]+\"(?:,\"rom\":\"([^\"]+)\")?\\}"))
                d[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : null;
            return d;
        }

        // battle-assets §4-4(PokeAPI·쇼다운 gen5ani 공백, 모두 #650 이후) + 펄기아 오리진폼 — 원작 자산으로 메워지지 않는다(03 §2-1)
        static readonly string[] NoAnimFront = { "680", "690", "723", "724", "757", "758", "799", "803", "484:2" };
        static readonly string[] NoAnimBack = NoAnimFront.Concat(new[] { "686", "691", "706", "793", "794", "805" }).ToArray();
        // 회전·확대 없는 원작 자산 애니 면(M4-1c2, sprites5 exact): 동미러 436 앞·뒤, 저리어 602 앞·뒤, 프리지오 615 뒤
        static readonly string[] RomExactSides = { "436f", "436b", "602f", "602b", "615b" };

        // ---------- 정지 ----------
        [Test]
        public void M4_정지도트모든키앞뒤()
        {
            string json = GenText("Sprites/Static/static.json");
            var rom = SpriteRom();
            Assert.AreEqual(322 + 20, rom.Count, "그림 = 종 322 + 폼 20");

            var pages = Lines(json, "\\{\"file\":\"(static_[0-9]+\\.png)\",\"w\":([0-9]+),\"h\":([0-9]+)\\}");
            Assert.GreaterOrEqual(pages.Count, 1);
            var pageSize = pages.Select(p => (int.Parse(p.Groups[2].Value), int.Parse(p.Groups[3].Value))).ToList();
            for (int i = 0; i < pages.Count; i++)
            {
                Assert.AreEqual($"static_{i}.png", pages[i].Groups[1].Value);
                Assert.AreEqual(pageSize[i], PngSize(Path.Combine(Gen, "Sprites/Static", pages[i].Groups[1].Value)), "아틀라스 크기 = static.json");
                Assert.AreEqual(2048, pageSize[i].Item1, "폭 2048(03 §2-1)");
            }

            var rows = Lines(json, "^\\{\"sprite\":\"([^\"]+)\",\"side\":\"(front|back)\",\"page\":([0-9]+),\"rect\":\\[([0-9,]+)\\],\"off\":\\[([0-9,]+)\\],\"canvas\":\\[([0-9,]+)\\],\"pivot\":\\[([0-9,]+)\\],\"kind\":\"(rom|public)\",\"source\":\"([^\"]+)\"\\}");
            Assert.AreEqual(Regex.Matches(json, "\"sprite\":\"[^\"]+\",\"side\"").Count, rows.Count, "static.json 모든 행을 읽었다");
            Assert.AreEqual(rom.Count * 2, rows.Count, "그림마다 앞·뒤");
            var have = new HashSet<string>(rows.Select(r => r.Groups[1].Value + "/" + r.Groups[2].Value));
            foreach (var s in rom.Keys) { Assert.IsTrue(have.Contains(s + "/front"), s + " 정지 앞"); Assert.IsTrue(have.Contains(s + "/back"), s + " 정지 뒤"); }

            // 사각형: 장 안, 겹치지 않음, 피벗은 사각형 안
            var rects = new List<(int Page, int X, int Y, int W, int H, string Id)>();
            foreach (var r in rows)
            {
                int page = int.Parse(r.Groups[3].Value);
                var a = Ints(r.Groups[4].Value); var off = Ints(r.Groups[5].Value); var canvas = Ints(r.Groups[6].Value); var pv = Ints(r.Groups[7].Value);
                string id = r.Groups[1].Value + "/" + r.Groups[2].Value;
                Assert.Less(page, pages.Count, id);
                Assert.IsTrue(a[2] > 0 && a[3] > 0 && a[0] + a[2] <= pageSize[page].Item1 && a[1] + a[3] <= pageSize[page].Item2, id + " 사각형이 장 안");
                Assert.IsTrue(off[0] + a[2] <= canvas[0] && off[1] + a[3] <= canvas[1], id + " 원래 캔버스 안");
                Assert.AreEqual(a[3] - 1, pv[1], id + " 피벗 y = 가장 아래 불투명 줄");
                rects.Add((page, a[0], a[1], a[2], a[3], id));
            }
            var byPage = rects.GroupBy(r => r.Page);
            foreach (var g in byPage)
            {
                var list = g.OrderBy(r => r.Y).ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count && list[j].Y < list[i].Y + list[i].H; j++)
                        Assert.IsFalse(list[i].X < list[j].X + list[j].W && list[j].X < list[i].X + list[i].W, $"{list[i].Id} ↔ {list[j].Id} 겹침");
            }

            // 키: pokemon.json의 모든 (dex, state)(@트레이너는 뗌) → 그림
            var dexOf = new Dictionary<string, string>();
            foreach (var m in Lines(TestData.ReadText("pokemon.json"), "^\\{\"key\":\"([^\"@]+)(?:@[^\"]+)?\",\"dex\":([0-9]+),")) dexOf[m.Groups[1].Value] = m.Groups[2].Value;
            var keyRows = Lines(json, "^\\{\"key\":\"([^\"]+)\",\"sprite\":\"([^\"]+)\"\\}");
            CollectionAssert.AreEquivalent(dexOf.Keys, keyRows.Select(k => k.Groups[1].Value), "static.json keys = pokemon.json (dex, state)");
            foreach (var k in keyRows)
            {
                string key = k.Groups[1].Value, sprite = k.Groups[2].Value;
                // 폼 그림이 있는 키는 그 그림, 나머지(팬 폼·등급만 다른 state)는 종 기본 그림(결정 5)
                Assert.AreEqual(rom.ContainsKey(key) ? key : dexOf[key], sprite, key + " → 그림");
            }

            // 문서 값(03 §2-1): 종 정지 322 = 원작 자산 283 + 공개 39, 폼 20 = 원작 자산 13 + 공개 7
            int RomSprites(bool form) => rows.Count(r => r.Groups[2].Value == "front" && r.Groups[1].Value.Contains(":") == form && r.Groups[8].Value == "rom");
            Assert.AreEqual(283, RomSprites(false), "종 정지 원작 자산");
            Assert.AreEqual(39, rows.Count(r => r.Groups[2].Value == "front" && !r.Groups[1].Value.Contains(":") && r.Groups[8].Value == "public"), "종 정지 공개");
            Assert.AreEqual(13, RomSprites(true), "폼 정지 원작 자산");
            Assert.AreEqual(7, rows.Count(r => r.Groups[2].Value == "front" && r.Groups[1].Value.Contains(":") && r.Groups[8].Value == "public"), "폼 정지 공개");
        }

        // ---------- 애니 ----------
        static List<(string Sprite, string Side, string Kind, string Source, string Line)> AnimSides(out Dictionary<string, (int W, int H)> sheets)
        {
            string dir = Path.Combine(Gen, "Sprites/Anim");
            // 폴더가 아니라 본체로 본다: 폴더와 .meta는 추적이라 rebuild-all 전 새 작업 트리에도 있다(검토 반영 M4-1)
            var jsons = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : new string[0];
            if (jsons.Length == 0) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (Sprites/Anim)");
            var list = new List<(string, string, string, string, string)>();
            sheets = new Dictionary<string, (int, int)>();
            foreach (var f in jsons.OrderBy(f => f, StringComparer.Ordinal))
            {
                string t = File.ReadAllText(f, Utf8);
                var head = Regex.Match(t, "^\\{\"schema\":1,\"sprite\":\"([^\"]+)\",\"file\":\"([^\"]+)\",\"w\":([0-9]+),\"h\":([0-9]+),\"sides\":\\[");
                Assert.IsTrue(head.Success, f);
                string sprite = head.Groups[1].Value, stem = sprite.Contains(":") ? sprite.Replace(':', '_') : sprite + "_0";
                Assert.AreEqual(stem + ".json", Path.GetFileName(f), "파일 이름 = <dex>_<state>");
                Assert.AreEqual(stem + ".png", head.Groups[2].Value);
                var size = (int.Parse(head.Groups[3].Value), int.Parse(head.Groups[4].Value));
                Assert.AreEqual(size, PngSize(Path.Combine(dir, stem + ".png")), sprite + " 시트 크기");
                sheets[sprite] = size;
                foreach (var m in Lines(t, "^\\{\"side\":\"(front|back)\",\"kind\":\"(rom|temp|public)\",\"source\":\"([^\"]+)\",.*\\}"))
                    list.Add((sprite, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Value));
                Assert.AreEqual(Regex.Matches(t, "\"side\":").Count, list.Count(x => x.Item1 == sprite), sprite + " 면 모두 읽음");
            }
            return list;
        }

        [Test]
        public void M4_애니도트수와예외표()
        {
            var sides = AnimSides(out var sheets);
            var rom = SpriteRom();
            foreach (var s in sides)
            {
                // 프레임 자리는 시트 안, 순서는 [고유 프레임, ms>0]
                var fw = int.Parse(Regex.Match(s.Line, "\"fw\":([0-9]+)").Groups[1].Value);
                var fh = int.Parse(Regex.Match(s.Line, "\"fh\":([0-9]+)").Groups[1].Value);
                var frames = Regex.Matches(Regex.Match(s.Line, "\"frames\":\\[(.*?)\\]\\],").Groups[1].Value + "]", "\\[?([0-9]+),([0-9]+)\\]").Cast<Match>().Select(m => (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))).ToList();
                Assert.Greater(frames.Count, 0, s.Sprite + s.Side);
                var sheet = sheets[s.Sprite];
                foreach (var (x, y) in frames) Assert.IsTrue(x + fw <= sheet.W && y + fh <= sheet.H, $"{s.Sprite} {s.Side} 프레임 ({x},{y}) {fw}×{fh}가 시트 안");
                var seq = Regex.Matches(Regex.Match(s.Line, "\"sequence\":\\[(.*)\\]\\}$").Groups[1].Value, "\\[([0-9]+),([0-9]+)\\]").Cast<Match>().ToList();
                Assert.Greater(seq.Count, 0, s.Sprite + s.Side + " 순서");
                foreach (var e in seq) { Assert.Less(int.Parse(e.Groups[1].Value), frames.Count); Assert.Greater(int.Parse(e.Groups[2].Value), 0); }
                var foot = Ints(Regex.Match(s.Line, "\"foot\":\\[([0-9,]+)\\]").Groups[1].Value);
                Assert.IsTrue(foot[0] >= 0 && foot[0] <= fw && foot[1] == fh - 1, s.Sprite + s.Side + " 발 기준점 = 가장 아래 불투명 줄");
            }

            // 문서 값(03 §2-1, plan M4-1 조건 ②): 종 애니 앞 314 = 원작 자산 2 + PokeAPI 임시 281 + 공개 31, 뒤 308 = 3 + 280 + 25. 폼 앞·뒤 19 = 임시 13 + 공개 6
            int N(string side, bool form, string kind = null) => sides.Count(s => s.Side == side && s.Sprite.Contains(":") == form && (kind == null || s.Kind == kind));
            Assert.AreEqual((314, 2, 281, 31), (N("front", false), N("front", false, "rom"), N("front", false, "temp"), N("front", false, "public")), "종 애니 앞");
            Assert.AreEqual((308, 3, 280, 25), (N("back", false), N("back", false, "rom"), N("back", false, "temp"), N("back", false, "public")), "종 애니 뒤");
            Assert.AreEqual((19, 0, 13, 6), (N("front", true), N("front", true, "rom"), N("front", true, "temp"), N("front", true, "public")), "폼 애니 앞");
            Assert.AreEqual((19, 0, 13, 6), (N("back", true), N("back", true, "rom"), N("back", true, "temp"), N("back", true, "public")), "폼 애니 뒤");

            // 빠진 면 = sprite_exceptions.json(build가 다시 계산한 표) = battle-assets §4-4 + 펄기아 오리진폼
            var have = new HashSet<string>(sides.Select(s => s.Sprite + "/" + s.Side));
            var noFront = rom.Keys.Where(k => !have.Contains(k + "/front")).ToList();
            var noBack = rom.Keys.Where(k => !have.Contains(k + "/back")).ToList();
            CollectionAssert.AreEquivalent(NoAnimFront, noFront, "애니 앞 없음");
            CollectionAssert.AreEquivalent(NoAnimBack, noBack, "애니 뒤 없음");
            var ex = Lines(Table("sprite_exceptions.json"), "^\\{\"sprite\":\"([^\"]+)\",\"missing\":\\[([^\\]]*)\\]\\}");
            CollectionAssert.AreEquivalent(noFront, ex.Where(m => m.Groups[2].Value.Contains("\"animFront\"")).Select(m => m.Groups[1].Value), "sprite_exceptions 앞");
            CollectionAssert.AreEquivalent(noBack, ex.Where(m => m.Groups[2].Value.Contains("\"animBack\"")).Select(m => m.Groups[1].Value), "sprite_exceptions 뒤");
            Assert.AreEqual(rom.Keys.Count(k => have.Contains(k + "/front") || have.Contains(k + "/back")), sheets.Count, "애니 파일 = 애니가 한 면이라도 있는 그림(333)");
            Assert.AreEqual(333, sheets.Count);
        }

        /// <summary>
        /// M9 크레딧(완료 조건 4): Gen이 실제로 쓴 공개 도트 파일(정지 static.json · 애니 Anim/*.json의 kind public) = smogon-sprites.json 파일 전부(작가는 smogon-authors.json —
        /// AssetSourceTests), 트레이너 앞모습의 쇼다운 · 색 바꿈 바탕 파일 = showdown-trainers.json 행.
        /// </summary>
        [Test]
        public void M9_크레딧_Gen이쓴Smogon쇼다운파일마다항목()
        {
            var used = new HashSet<string>(Lines(GenText("Sprites/Static/static.json"), "\"kind\":\"public\",\"source\":\"pokeapi:([^\"]+)\"").Select(m => m.Groups[1].Value));
            foreach (var f in Directory.GetFiles(Path.Combine(Gen, "Sprites/Anim"), "*.json"))
                foreach (Match m in Regex.Matches(File.ReadAllText(f, Utf8), "\"kind\":\"public\",\"source\":\"pokeapi:([^\"]+)\"")) used.Add(m.Groups[1].Value);
            var listed = Lines(File.ReadAllText(Path.Combine(Root, "Tools/assets/credits/smogon-sprites.json"), Utf8), "\"files\":\\[([^\\]]*)\\]")
                .SelectMany(m => Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value)).ToList();
            CollectionAssert.AreEquivalent(listed, used, "Gen이 쓴 공개 도트 = 크레딧 파일(빠짐 · 남음 0)");
            Assert.AreEqual(160, used.Count);

            var trainers = new HashSet<string>(Lines(GenText("Trainers/trainers.json"), "\"source\":\"(?:showdown|recolor):([^\"]+)\"").Select(m => m.Groups[1].Value + ".png"));
            var credited = Lines(File.ReadAllText(Path.Combine(Root, "Tools/assets/credits/showdown-trainers.json"), Utf8), "^\\{\"file\":\"([^\"]+)\"").Select(m => m.Groups[1].Value);
            CollectionAssert.AreEquivalent(credited, trainers, "Gen 트레이너 그림의 쇼다운 파일 = 크레딧 행");
        }

        [Test]
        public void M4_도트원천은원작자산먼저()
        {
            // 결정 8 보충: 원작 자산에 있는 그림(#1~649 종 + BW 원작 자산 폼 13)은 원작 자산에서. 정지는 모두 원작 자산, 애니는 회전·확대 없는 면만 원작 자산이고 나머지는 PokeAPI 임시(plan §5 #13).
            // 원작 자산에 없는 그림(#650 이후 39종 + 폼 7)만 공개(PokeAPI) 원천을 쓴다.
            string json = GenText("Sprites/Static/static.json");
            var rom = SpriteRom();
            // 공개 원천을 쓴 그림 = Smogon 크레딧 행(03 §6: 실제로 쓴 팬 도트마다 작가 표기 — 디아루가·펄기아 오리진폼 포함, 검토 반영 M4-1)
            var credits = Regex.Matches(File.ReadAllText(Path.Combine(Root, "Tools/assets/credits/smogon-sprites.json"), Utf8), "^\\{\"sprite\":\"([^\"]+)\"", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value);
            var publicSprites = Lines(json, "^\\{\"sprite\":\"([^\"]+)\",\"side\":\"front\",.*\"kind\":\"public\"").Select(m => m.Groups[1].Value);
            CollectionAssert.AreEquivalent(publicSprites, credits, "정지 공개 그림 = smogon-sprites.json 행");
            foreach (var r in Lines(json, "^\\{\"sprite\":\"([^\"]+)\",\"side\":\"(front|back)\",.*\"kind\":\"(rom|public)\",\"source\":\"([^\"]+)\"\\}"))
            {
                string s = r.Groups[1].Value, side = r.Groups[2].Value, kind = r.Groups[3].Value, src = r.Groups[4].Value;
                if (rom[s] != null) { Assert.AreEqual("rom", kind, s + side + " 정지"); Assert.AreEqual("rom:" + rom[s], src, s + side + " 정지 원작 자산 칸"); }
                else
                {
                    Assert.AreEqual("public", kind, s + side + " 정지");
                    StringAssert.StartsWith("pokeapi:" + (side == "back" ? "back/" : ""), src);
                }
            }
            var sides = AnimSides(out _);
            // 원작 자산 렌더 색인이 있으면 exact와 맞춰 본다(없으면 문서 값만)
            string ix = Path.Combine(Root, "AssetSources/rom/bw/sprites5/_index.json");
            var exact = File.Exists(ix)
                ? new HashSet<string>(Lines(File.ReadAllText(ix, Utf8), "^\\{\"entry\":([0-9]+),\"side\":\"(front|back)\",.*\"exact\":true").Where(m => int.Parse(m.Groups[1].Value) <= 649 && rom.ContainsKey(m.Groups[1].Value)).Select(m => m.Groups[1].Value + m.Groups[2].Value[0]))
                : null;
            if (exact != null) CollectionAssert.AreEquivalent(RomExactSides, exact, "sprites5 회전·확대 없는 로스터 면");
            foreach (var s in sides)
            {
                string id = s.Sprite + s.Side[0];
                if (rom[s.Sprite] != null)
                {
                    Assert.AreNotEqual("public", s.Kind, id + " 원작 자산이 덮는 그림에 공개 원천");
                    Assert.AreEqual(RomExactSides.Contains(id) ? "rom" : "temp", s.Kind, id + " 애니: 회전·확대 없는 면만 원작 자산");
                    if (s.Kind == "rom") Assert.AreEqual("rom:" + rom[s.Sprite], s.Source);
                    else StringAssert.StartsWith("pokeapi:animated/", s.Source, id);
                }
                else { Assert.AreEqual("public", s.Kind, id); StringAssert.StartsWith("pokeapi:animated/", s.Source, id); }
            }
        }

        [Test]
        public void M4_팬폼바탕은있는그림()
        {
            // 03 §2-1·결정 5: 수호신 폼 18종 + 호수 3신 고대 폼 3 → 기본 도트 + 효과. 값(color·strength)은 M4-3 화면 확인(plan §5 #3)
            string pokemon = TestData.ReadText("pokemon.json");
            var noble = Lines(pokemon, "^\\{\"key\":\"([0-9]+:2)\",.*\"climate\":\"Noble\"").Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(18, noble.Count, "수호신 폼(Noble)");
            var rows = Lines(Table("fan_forms.json"), "^\\{\"key\":\"([^\"]+)\",\"base\":\"([^\"]+)\",\"effect\":\"(tint|glow|aura)\",\"color\":\"#[0-9a-f]{6}\",\"strength\":(0\\.[0-9]+|1)\\}");
            Assert.AreEqual(Regex.Matches(Table("fan_forms.json"), "\"key\":").Count, rows.Count, "fan_forms 모든 행 형식");
            CollectionAssert.AreEquivalent(noble.Concat(new[] { "480:0", "481:0", "482:0" }), rows.Select(r => r.Groups[1].Value), "수호신 18 + 고대 3");
            var rom = SpriteRom();
            foreach (var r in rows)
            {
                string key = r.Groups[1].Value, b = r.Groups[2].Value;
                Assert.AreEqual(key.Split(':')[0], b, key + " base = 종 기본 그림");
                Assert.IsTrue(rom.ContainsKey(b), key + " base " + b);
            }
            if (!HasGen("Sprites/Static/static.json")) return;
            string json = GenText("Sprites/Static/static.json");
            foreach (var r in rows)
                foreach (var side in new[] { "front", "back" })
                    StringAssert.Contains("{\"sprite\":\"" + r.Groups[2].Value + "\",\"side\":\"" + side + "\"", json, r.Groups[1].Value + " base 정지 " + side);
        }

        // ---------- 트레이너 ----------
        [Test]
        public void M4_주인공앞모습8의얼굴조각은표그대로실린다_결정61()
        {
            // 얼굴 상자(결정 54 ④·61 ③): 추적 표 hero_faces.json 8행(키 = 고를 수 있는 주인공) → trainers.json heroes 행, 그림은 80×80 그대로
            var faces = Lines(Table("hero_faces.json"), "^\\{\"key\":\"([a-z0-9_]+)\",\"source\":\"(rom:[a-z0-9]+:[a-z0-9_]+)\",\"face\":\\[([0-9]+),([0-9]+)\\]\\}");
            CollectionAssert.AreEqual(Sinnoh.Rules.Session.Protagonists.Keys.Concat(Sinnoh.Rules.Session.Protagonists.Guests), faces.Select(f => f.Groups[1].Value), "hero_faces = 주인공 8 + 손님 인물(결정 96 Q5 나 — 용식)");
            var heroes = Lines(GenText("Trainers/trainers.json"), "^\\{\"key\":\"([a-z0-9_]+)\",\"file\":\"Hero/([a-z0-9_]+)\\.png\",\"source\":\"([^\"]+)\",\"w\":80,\"h\":80,\"face\":\\[([0-9]+),([0-9]+),30,30\\]\\}");
            Assert.AreEqual(faces.Count, heroes.Count, "heroes 행 8");
            for (int i = 0; i < faces.Count; i++)
            {
                string key = faces[i].Groups[1].Value;
                Assert.AreEqual(key, heroes[i].Groups[1].Value);
                Assert.AreEqual(key, heroes[i].Groups[2].Value, "파일 이름 = 주인공 키");
                Assert.AreEqual(faces[i].Groups[2].Value, heroes[i].Groups[3].Value, key + " 원천 = 표");
                Assert.AreEqual(faces[i].Groups[3].Value + "," + faces[i].Groups[4].Value, heroes[i].Groups[4].Value + "," + heroes[i].Groups[5].Value, key + " 얼굴 조각 = 표");
                Assert.LessOrEqual(int.Parse(faces[i].Groups[3].Value), 50, key);
                Assert.LessOrEqual(int.Parse(faces[i].Groups[4].Value), 50, key);
                Assert.AreEqual((80, 80), PngSize(Path.Combine(Gen, "Trainers/Hero", key + ".png")));
            }
        }

        [Test]
        public void M4_트레이너앞51집계()
        {
            string table = Table("trainer_sprites.json");
            var rows = Lines(table, "^\\{\"key\":\"([a-z_]+)\",\"class\":\"[^\"]+\",\"source\":\"([^\"]+)\"(,\"style\":\"gen6\")?(?:,\"colorMap\":(\\{[^}]*\\}))?\\}");
            Assert.AreEqual(51, rows.Count, "trainer_sprites 51행 형식");
            foreach (var r in rows.Where(r => r.Groups[2].Value.StartsWith("recolor:")))
                Assert.IsTrue(r.Groups[4].Success && Regex.IsMatch(r.Groups[4].Value, "^\\{(\"#[0-9a-f]{6}\":\"#[0-9a-f]{6}\",?)+\\}$"), r.Groups[1].Value + " 색 대응표 초안(03 §8 #7)");
            string json = GenText("Trainers/trainers.json");
            var fronts = Lines(json, "^\\{\"key\":\"([a-z_]+)\",\"file\":\"Front/([a-z_]+)\\.png\",\"source\":\"([^\"]+)\",\"w\":80,\"h\":80,\"foot\":\\[[0-9]+,[0-9]+\\],\"face\":\\[([0-9]+),([0-9]+),30,30\\]\\}");
            CollectionAssert.AreEqual(rows.Select(r => r.Groups[1].Value), fronts.Select(f => f.Groups[1].Value), "앞모습 51 = trainer_cards 순서");
            // 얼굴 조각(결정 61 ② 크기 안 E): 추적 표 trainer_faces.json 51행이 그대로 실리고 30×30 조각이 그림 안이다
            var faces = Lines(Table("trainer_faces.json"), "^\\{\"key\":\"([a-z_]+)\",\"face\":\\[([0-9]+),([0-9]+)\\]\\}");
            CollectionAssert.AreEqual(rows.Select(r => r.Groups[1].Value), faces.Select(f => f.Groups[1].Value), "trainer_faces 51행 = trainer_cards 순서");
            for (int i = 0; i < faces.Count; i++)
            {
                Assert.AreEqual(faces[i].Groups[2].Value + "," + faces[i].Groups[3].Value, fronts[i].Groups[4].Value + "," + fronts[i].Groups[5].Value, faces[i].Groups[1].Value + " 얼굴 조각 = 표");
                Assert.LessOrEqual(int.Parse(faces[i].Groups[2].Value), 50, faces[i].Groups[1].Value);
                Assert.LessOrEqual(int.Parse(faces[i].Groups[3].Value), 50, faces[i].Groups[1].Value);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual(rows[i].Groups[2].Value, fronts[i].Groups[3].Value, rows[i].Groups[1].Value + " 원천 = 표");
                Assert.AreEqual((80, 80), PngSize(Path.Combine(Gen, "Trainers/Front", fronts[i].Groups[2].Value + ".png")));
            }
            // 03 §2-3: 원작 자산 31(Pt 28·HGSS 2·BW 1)·쇼다운 14·화풍 다름 4·색 바꿈 2
            int C(Func<Match, bool> f) => rows.Count(f);
            Assert.AreEqual(28, C(r => r.Groups[2].Value.StartsWith("rom:pt:")));
            Assert.AreEqual(2, C(r => r.Groups[2].Value.StartsWith("rom:hgss:")));
            Assert.AreEqual(1, C(r => r.Groups[2].Value.StartsWith("rom:bw:")));
            Assert.AreEqual(14, C(r => r.Groups[2].Value.StartsWith("showdown:") && !r.Groups[3].Success));
            Assert.AreEqual(4, C(r => r.Groups[2].Value.StartsWith("showdown:") && r.Groups[3].Success));
            Assert.AreEqual(2, C(r => r.Groups[2].Value.StartsWith("recolor:")));
            Assert.AreEqual(51, Directory.GetFiles(Path.Combine(Gen, "Trainers/Front"), "*.png").Length, "Front에 다른 그림 없음");
        }

        [Test]
        public void M4_트레이너뒷모습대응51()
        {
            // 결정 31 ④: 직업마다 받아 둔 뒷모습 중 어울리는 것을 임시로(원작 자산 공식 그림 먼저, 출처·크레딧 유지). 확인은 plan §5 #2
            string table = Table("trainer_backs.json");
            var cards = Lines(TestData.ReadText("trainer_cards.json"), "^\\{\"key\":\"([a-z_]+)\"").Select(m => m.Groups[1].Value).ToList();
            var rows = Lines(table, "^\\{\"key\":\"([a-z_]+)\",\"class\":\"[^\"]+\",\"source\":\"((rom:(pt|hgss|bw|b2w2):[a-z0-9_]+)|research:[^\"]+)\",.*\"why\":\"[^\"]+\",\"author\":\"[^\"]+\",\"licence\":\"[^\"]+\"(,\"url\":\"https://[^\"]+\")?\\}");
            Assert.AreEqual(Regex.Matches(table, "\"key\":").Count, rows.Count, "trainer_backs 모든 행 형식(원천·이유·작가·조건)");
            CollectionAssert.AreEqual(cards, rows.Select(r => r.Groups[1].Value), "51직업 = trainer_cards 순서");
            foreach (var r in rows.Where(r => r.Groups[2].Value.StartsWith("research:")))
                Assert.IsTrue(r.Groups[5].Success, r.Groups[1].Value + " research 원천은 url(trainer-backs sources.json)");
            Assert.Greater(rows.Count(r => r.Groups[3].Success), rows.Count / 2, "원작 자산 공식 그림이 먼저");

            string json = GenText("Trainers/trainers.json");
            var backs = Lines(json, "^\\{\"set\":\"(official|temp)\",(?:\"key\":\"([a-z_]+)\",)?\"file\":\"(Back/[^\"]+)\",\"source\":\"([^\"]+)\",\"fw\":([0-9]+),\"fh\":([0-9]+),\"frames\":([0-9]+),");
            var official = backs.Where(b => b.Groups[1].Value == "official").ToList();
            var temp = backs.Where(b => b.Groups[1].Value == "temp").ToList();
            Assert.AreEqual(11 + 6, official.Count, "공식 뒷모습 Pt 11 + HGSS·BW·B2W2 주인공 둘씩");
            CollectionAssert.AreEqual(cards, temp.Select(t => t.Groups[2].Value), "임시 뒷모습 51");
            foreach (var b in backs)
            {
                int fw = int.Parse(b.Groups[5].Value), fh = int.Parse(b.Groups[6].Value), n = int.Parse(b.Groups[7].Value);
                // 던지기 행(주인공 8)은 cols 칸 격자, 그 밖은 가로 한 줄
                string line = json.Substring(b.Index, json.IndexOf('\n', b.Index) - b.Index);
                var cm = Regex.Match(line, "\"cols\":([0-9]+),");
                int cols = cm.Success ? int.Parse(cm.Groups[1].Value) : n;
                Assert.AreEqual((fw * cols, fh * ((n + cols - 1) / cols)), PngSize(Path.Combine(Gen, "Trainers", b.Groups[3].Value)), b.Groups[3].Value + " 프레임 시트(격자)");
                bool gen4 = b.Groups[4].Value.StartsWith("rom:pt:") || b.Groups[4].Value.StartsWith("rom:hgss:");
                if (gen4) Assert.AreEqual((80, 80), (fw, fh), b.Groups[3].Value + " Gen 4 80×80");
                // 결정 55 ①: origin = 원작 자산이 세우는 자리 하나 — 던지기 없는 행도 같다. Gen 4는 셀 아래 가운데, 원작 자산 밖 원천은 null.
                // Gen 5(멀티셀 원점)는 값의 정답을 시험이 모르므로 발 기준점 근처(가로 12 · 세로 30px 안 — 지금 값은 11 · 25까지)인지만 본다
                var om = Regex.Match(line, "\"frames\":[0-9]+,\"origin\":(?:\\[([0-9]+),([0-9]+)\\]|null),\"foot\":\\[([0-9]+),([0-9]+)\\]");
                Assert.IsTrue(om.Success, b.Groups[3].Value + " origin·foot 칸");
                if (b.Groups[4].Value.StartsWith("research:")) Assert.IsFalse(om.Groups[1].Success, b.Groups[3].Value + " 원작 자산 밖 원천은 origin null");
                else
                {
                    Assert.IsTrue(om.Groups[1].Success, b.Groups[3].Value + " 원작 자산 원천은 origin이 있다");
                    int ox = int.Parse(om.Groups[1].Value), oy = int.Parse(om.Groups[2].Value), fx = int.Parse(om.Groups[3].Value), fy = int.Parse(om.Groups[4].Value);
                    if (gen4) Assert.AreEqual((40, 80), (ox, oy), b.Groups[3].Value + " Gen 4 원점 = 셀 아래 가운데");
                    else Assert.IsTrue(ox < fw && Math.Abs(ox - fx) <= 12 && Math.Abs(oy - fy) <= 30, b.Groups[3].Value + $" Gen 5 원점 ({ox},{oy})이 발 기준점 ({fx},{fy}) 근처");
                }
            }
            for (int i = 0; i < rows.Count; i++) Assert.AreEqual(rows[i].Groups[2].Value, temp[i].Groups[4].Value, cards[i] + " 원천 = 표");
        }

        [Test]
        public void M4_주인공뒷모습던지기8()
        {
            // M4-3(결정 53 ④·54 ②, 03 §2-3): 주인공 8명 공식 뒷모습 = 볼 던지기 한 형식(cols·fps·total·release·still·head·sequence). 틱 = 60fps [가정]. origin = 원작 자산이 세우는 자리(결정 55 ①)
            string json = GenText("Trainers/trainers.json");
            var rows = Lines(json, "^\\{\"set\":\"official\",\"file\":\"Back/official/((pt|hgss|bw|b2w2)_[a-z0-9_]+)\\.png\",\"source\":\"[^\"]+\",\"fw\":([0-9]+),\"fh\":([0-9]+),\"frames\":([0-9]+),\"origin\":\\[([0-9]+),([0-9]+)\\],\"foot\":\\[[0-9]+,[0-9]+\\],"
                + "\"cols\":([0-9]+),\"fps\":60,\"total\":([0-9]+),\"release\":([0-9]+),\"still\":([0-9]+),\"head\":([0-9]+),\"hand\":\\[[0-9]+,[0-9]+\\],\"leave\":[0-9]+,(?:\"frameAffine\":\"([01]+)\",)?\"sequence\":\\[((?:\\[[0-9]+,[0-9]+\\],?)+)\\]\\}");
            StringAssert.DoesNotContain("originFoot", json, "origin 뜻은 하나다(결정 55 ① — 옛 칸 originFoot 없음)");
            StringAssert.StartsWith("{\"schema\":2,", json);
            CollectionAssert.AreEqual(new[] { "pt_player_male", "pt_player_female", "hgss_000_ethan", "hgss_001_lyra", "bw_000_hilbert", "bw_001_hilda", "b2w2_000_nate", "b2w2_001_rosa" },
                rows.Select(r => r.Groups[1].Value).ToList(), "던지기 행 = 주인공 8(Pt 광휘·빛나, HGSS 심향·금선, BW 투지·투희, B2W2 공명·명희)");
            Assert.AreEqual(rows.Count, Regex.Matches(json, "\"sequence\":").Count, "던지기 칸은 주인공 8행에만(임시 대응은 그대로)");
            var total = new Dictionary<string, int> { ["pt"] = 98, ["hgss"] = 94, ["bw"] = 100, ["b2w2"] = 100 };
            foreach (var r in rows)
            {
                string name = r.Groups[1].Value, game = r.Groups[2].Value;
                int I(int g) => int.Parse(r.Groups[g].Value);
                int fw = I(3), fh = I(4), n = I(5), cols = I(8), ticks = I(9), release = I(10), still = I(11), head = I(12);
                bool gen4 = game == "pt" || game == "hgss";
                Assert.AreEqual((fw * cols, fh * ((n + cols - 1) / cols)), PngSize(Path.Combine(Gen, "Trainers/Back/official", name + ".png")), name + " 시트 = 프레임 크기 × 격자");
                var seq = Regex.Matches(r.Groups[14].Value, "\\[([0-9]+),([0-9]+)\\]").Cast<Match>().Select(m => (F: int.Parse(m.Groups[1].Value), T: int.Parse(m.Groups[2].Value))).ToList();
                Assert.AreEqual(total[game], ticks, name + " 틱 합(Pt 98 · HGSS 94 · Gen 5 100)");
                Assert.AreEqual(ticks, seq.Sum(e => e.T), name + " sequence 틱 합 = total");
                Assert.IsTrue(seq.All(e => e.F >= 0 && e.F < n && e.T > 0), name + " sequence 프레임이 시트 안");
                Assert.IsTrue(release > 0 && release < ticks, name + " release가 범위 안");
                Assert.AreEqual(gen4 ? 30 : 38, release, 2, name + " 손 떠나는 틱(Gen 4 30 · Gen 5 38~40)");
                Assert.IsTrue(seq.Select((e, i) => seq.Take(i).Sum(x => x.T)).Contains(release), name + " release = 프레임이 바뀌는 틱");
                Assert.IsTrue(still >= 0 && still < n, name + " still 프레임이 있다");
                Assert.IsTrue(head >= 0 && head < fh && I(6) < fw, name + " head·origin이 프레임 안");
                if (gen4)
                {
                    Assert.AreEqual((80, 80, 8, 8, 0), (fw, fh, n, cols, still), name + " Gen 4 = 8셀 시트 그대로, 서 있기 = 셀 0");
                    Assert.AreEqual((40, 80), (I(6), I(7)), name + " Gen 4 원점 = 셀 아래 가운데 [가정 M4-3]");
                    Assert.IsFalse(r.Groups[13].Success, name + " Gen 4에는 frameAffine 없음");
                }
                else
                {
                    Assert.AreEqual(8, cols, name + " Gen 5 격자 8칸");
                    Assert.IsTrue(n >= 26 && n <= 32, name + " Gen 5 고유 프레임 26~31(+ 정지 그림)");
                    Assert.AreEqual(n, r.Groups[13].Value.Length, name + " frameAffine 길이 = 프레임 수");
                    StringAssert.Contains("1", r.Groups[13].Value, name + " 회전 프레임까지 싣는다");
                    // 결정 55 ①(B안): Gen 5 origin = 원작 자산 원점(발) 그대로 — Gen 4 머리 높이(기준점 위 70px)에 맞추지 않아 선 머리가 33~47px 더 높다
                    Assert.IsTrue(I(7) - head >= 70 + 33 && I(7) - head <= 70 + 47 && I(7) <= fh, name + " origin = 원작 자산 원점(발): 선 머리가 기준점 위 103~117px");
                    // B2W2는 던지기에 서 있는 자세가 없어 정지 그림을 시트 끝에 더한다(순서에는 안 나온다)
                    if (game == "b2w2") { Assert.AreEqual(n - 1, still, name); Assert.IsFalse(seq.Any(e => e.F == still), name + " 정지 그림은 순서 밖"); }
                    else Assert.AreEqual(seq[0].F, still, name + " BW는 던지기 첫 프레임 = 서 있기");
                }
            }
        }

        [Test]
        public void M65_튜토리얼마박사그림()
        {
            // 결정 90 Q2 가: 안내 = Pt 소개 장면 전신 도트(intro.narc 19·20·23을 불투명 상자로 자른 것), 목차 얼굴 = 필드 도트 prof_rowan 남쪽 선 모습 한 칸
            string full = Path.Combine(Gen, "Tutorial/rowan_full.png");
            if (!File.Exists(full)) Assert.Ignore("M6.5 Gen 없음: node Tools/assets/build.mjs --only tutorial 먼저");
            Assert.AreEqual((64, 123), PngSize(full));
            Assert.AreEqual((32, 32), PngSize(Path.Combine(Gen, "Tutorial/rowan_face.png")));
        }

        [Test]
        public void M4_필드도트주인공8()
        {
            // 보드의 주인공 도트 말(04 §3-1 양식 ①, 결정 52·60): 네 게임 주인공 남·여. 행의 key = 같은 인물의 공식 뒷모습 시트 = 고를 수 있는 주인공(Protagonists)
            string json = GenText("Field/field.json");
            StringAssert.StartsWith("{\"schema\":2,", json);
            var rows = Lines(json, "^\\{\"name\":\"((pt|hgss|bw|b2w2)_(m|f|rival))\",\"key\":\"([a-z0-9_]+)\",\"file\":\"([^\"]+)\",.*\"fw\":32,\"fh\":32,\"frames\":([0-9]+),\"foot\":\\[([0-9]+),([0-9]+)\\],\"head\":([0-9]+),\"n\":\\[([0-9,]+)\\],\"s\":\\[([0-9,]+)\\],\"w\":\\[([0-9,]+)\\],\"e\":\\[([0-9,]+)\\]\\}");
            CollectionAssert.AreEquivalent(new[] { "pt_m", "pt_f", "hgss_m", "hgss_f", "bw_m", "bw_f", "b2w2_m", "b2w2_f", "pt_rival" }, rows.Select(r => r.Groups[1].Value));
            CollectionAssert.AreEqual(Sinnoh.Rules.Session.Protagonists.Keys.Concat(Sinnoh.Rules.Session.Protagonists.Guests).ToArray(), rows.Select(r => r.Groups[4].Value).ToArray(), "행의 key = 고를 수 있는 주인공 8(같은 순서) + 손님 인물(결정 96 Q5 나)");
            string trainers = GenText("Trainers/trainers.json");
            foreach (var r in rows)
            {
                string name = r.Groups[1].Value;
                int frames = int.Parse(r.Groups[6].Value);
                Assert.AreEqual((32, 32 * frames), PngSize(Path.Combine(Gen, "Field", r.Groups[5].Value)), name);
                StringAssert.Contains("\"file\":\"Back/official/" + r.Groups[4].Value + ".png\"", trainers, name + ": 같은 인물의 공식 뒷모습이 있다");
                int foot = int.Parse(r.Groups[8].Value), head = int.Parse(r.Groups[9].Value);
                Assert.That(head, Is.LessThan(foot), name);
                Assert.That(foot, Is.LessThan(32), name);
                var all = new List<int>();
                for (int d = 10; d <= 13; d++)
                {
                    var cycle = r.Groups[d].Value.Split(',').Select(int.Parse).ToArray();
                    Assert.AreEqual(4, cycle.Length, name + ": 걷기 한 바퀴 = 그림 넷");
                    Assert.AreNotEqual(cycle[1], cycle[3], name + ": 걸음 A와 걸음 B는 다른 그림");
                    Assert.AreNotEqual(cycle[0], cycle[1], name);
                    foreach (int f in cycle) Assert.That(f, Is.InRange(0, frames - 1), name);
                    all.AddRange(cycle);
                }
                // Gen 4는 방향마다 네 프레임(선 모습이 두 장), Gen 5는 세 프레임(선 모습 한 장을 두 번 쓴다)
                Assert.AreEqual(name.StartsWith("pt") || name.StartsWith("hgss") ? 16 : 12, all.Distinct().Count(), name + ": 방향마다 다른 프레임");
            }
        }
    }
}
