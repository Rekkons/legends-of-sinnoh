using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-1d2: build.mjs의 stage·icons·fonts·cards 묶음이 만든 Gen/(git 밖)과 추적 표를 System.IO로만 본다(03 §2-2·§2-4·§7).
    /// 표만 보는 부분(지형 → 무대 짝)은 늘 돌고, Gen을 읽는 부분은 Gen/이 없으면 건너뛴다.
    /// </summary>
    public class GenStageTests
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

        /// <summary>build.mjs가 쓴 RGBA PNG(8비트 색 형식 6, 필터 0)의 알파 0 픽셀 수.</summary>
        static int TransparentPixels(string file)
        {
            var b = File.ReadAllBytes(file);
            int o = 8, w = 0, h = 0;
            var idat = new MemoryStream();
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            while (o < b.Length)
            {
                int len = Be(o);
                string type = Encoding.ASCII.GetString(b, o + 4, 4);
                if (type == "IHDR") { w = Be(o + 8); h = Be(o + 12); Assert.AreEqual((8, 6), ((int)b[o + 16], (int)b[o + 17]), file + ": RGBA 8비트"); }
                else if (type == "IDAT") idat.Write(b, o + 8, len);
                o += 12 + len;
            }
            var z = idat.ToArray();
            var raw = new MemoryStream();
            using (var ds = new System.IO.Compression.DeflateStream(new MemoryStream(z, 2, z.Length - 2), System.IO.Compression.CompressionMode.Decompress)) ds.CopyTo(raw);
            var r = raw.ToArray();
            int stride = w * 4, n = 0;
            for (int y = 0; y < h; y++)
            {
                Assert.AreEqual(0, r[y * (stride + 1)], file + ": 필터 0");
                for (int x = 0; x < w; x++) if (r[y * (stride + 1) + 1 + x * 4 + 3] == 0) n++;
            }
            return n;
        }

        [Test]
        public void M4_창틀과HP숫자는색인0투명()
        {
            // DS BG·OBJ의 팔레트 색인 0은 늘 투명이다. nitrogfx가 -object 없이 그린 창 틀(pl_winframe)·HP·Lv 숫자(font_special_chars)도
            // Gen에서는 색인 0이 투명이어야 한다 — 틀 바깥 여백·둥근 모서리, 글자 바탕이 연두색으로 남지 않게(검토 반영 M4-1, 03 §2-2)
            string json = GenText("Stage/stage.json");
            var windows = Regex.Match(json, "\"window\":\\[([^\\]]*)\\]").Groups[1].Value.Split(',').Select(s => s.Trim('"')).ToList();
            Assert.AreEqual(25, windows.Count, "창 틀 25");
            var counts = windows.ToDictionary(w => w, w => TransparentPixels(Path.Combine(Gen, "Stage", w)));
            Assert.AreEqual(276, counts["Window/message_box_00.png"], "message_box_00 바깥 2px·모서리 = 목업 r4 투명 칸");
            Assert.Greater(counts["Window/standard_system.png"], 0, "standard_system");
            // 색인 0을 아예 쓰지 않는 둘(pokemon_preview — 원래 -object로 그림, message_box_17)만 투명 픽셀이 없다
            CollectionAssert.AreEquivalent(new[] { "Window/pokemon_preview.png", "Window/message_box_17.png" }, counts.Where(kv => kv.Value == 0).Select(kv => kv.Key), "투명 픽셀 없는 창 틀");
            Assert.AreEqual(463, TransparentPixels(Path.Combine(Gen, "Stage/Hud/special_chars.png")), "HP·Lv 숫자 바탕(색인 0) 투명");
        }

        [Test]
        public void M4_무대짝빠짐0()
        {
            // 03 §7 무대: board.json에 나오는 (biome, climate, distortion) 조합마다 terrain.json 짝이 하나, 쓰이지 않는 짝 0
            var board = Lines(TestData.ReadText("board.json"), "^\\{\"key\":\"[^\"]+\",.*\"biome\":(null|\"[A-Za-z]+\"),\"climate\":(null|\"[A-Za-z]+\"),.*\"distortion\":(true|false),");
            Assert.AreEqual(351, board.Count, "board.json 351칸을 모두 읽었다");
            var combos = board.GroupBy(m => m.Groups[1].Value + "|" + m.Groups[2].Value + "|" + m.Groups[3].Value).ToDictionary(g => g.Key, g => g.Count());
            string table = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/terrain.json"), Utf8);
            var rows = Lines(table, "^\\{\"biome\":(null|\"[A-Za-z]+\"),\"climate\":(null|\"[A-Za-z]+\"),\"distortion\":(true|false),\"bg\":\"([0-9]{2}_[a-z0-9_]+)\",\"platform\":\"([a-z_]+)\"\\}");
            Assert.AreEqual(Regex.Matches(table, "\"biome\":").Count, rows.Count, "terrain.json 모든 행 형식");
            var keys = rows.Select(r => r.Groups[1].Value + "|" + r.Groups[2].Value + "|" + r.Groups[3].Value).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "조합마다 한 줄");
            var missing = combos.Keys.Where(k => !keys.Contains(k)).ToList();
            Assert.AreEqual(0, missing.Count, "짝 없는 조합: " + string.Join(", ", missing));
            CollectionAssert.IsSubsetOf(keys, combos.Keys, "board.json에 없는 짝");
            Assert.AreEqual(14, rows.Count, "지형 12 + 테마 없는 칸(동굴) + 깨어진 세계(03 §2-2)");

            string stage = GenText("Stage/stage.json");
            var terrain = Lines(stage, "^\\{\"biome\":(null|\"[A-Za-z]+\"),\"climate\":(null|\"[A-Za-z]+\"),\"distortion\":(true|false),\"cells\":([0-9]+),\"bg\":\"(Bg/[^\"]+)\",\"enemy\":\"(Platform/[^\"]+)\",\"player\":\"(Platform/[^\"]+)\"\\}");
            Assert.AreEqual(rows.Count, terrain.Count, "stage.json terrain = 표");
            Assert.AreEqual(351, terrain.Sum(t => int.Parse(t.Groups[4].Value)), "짝이 351칸을 모두 덮는다");
            foreach (var t in terrain)
            {
                Assert.AreEqual((256, 192), PngSize(Path.Combine(Gen, "Stage", t.Groups[5].Value)), t.Groups[5].Value + " DS 위 화면");
                Assert.AreEqual((128, 64), PngSize(Path.Combine(Gen, "Stage", t.Groups[6].Value)), t.Groups[6].Value);
                Assert.AreEqual((256, 32), PngSize(Path.Combine(Gen, "Stage", t.Groups[7].Value)), t.Groups[7].Value + " 내 쪽 발판은 넓고 아래가 메시지 창에 잘린다(원작 자산 셀 그대로)");
            }
            // Pt 원작 자산 묶음 수(03 §2-2): 배경 35 중 낮 고정으로 23, 발판 82 중 23짝
            Assert.AreEqual(23 + 6 * 2, Directory.GetFiles(Path.Combine(Gen, "Stage/Bg"), "*.png").Length, "배경 23 + 야외 여섯의 저녁·밤(결정 70 ⑥)");
            Assert.AreEqual(46 + 9 * 2 * 2, Directory.GetFiles(Path.Combine(Gen, "Stage/Platform"), "*.png").Length, "발판 23짝 + 아홉 지형의 저녁·밤");

            // 시간대(결정 70 ⑥): 표의 순서 = Sinnoh.Fx DayTime, timed의 그림마다 저녁·밤 그림이 같은 크기로 있다(화면은 DayTimes.Art로 이름만 바꿔 읽는다)
            var times = Regex.Match(stage, "\"times\":\\[([^\\]]*)\\]").Groups[1].Value.Split(',').Select(s => s.Trim('"')).ToList();
            CollectionAssert.AreEqual(System.Enum.GetValues(typeof(Sinnoh.Fx.DayTime)).Cast<Sinnoh.Fx.DayTime>().Select(Sinnoh.Fx.DayTimes.Name).ToList(), times);
            Assert.AreEqual(Sinnoh.Fx.DayTimes.Count, times.Count);
            var timed = Regex.Match(stage, "\"timed\":\\[([^\\]]*)\\]").Groups[1].Value.Split(',').Select(s => s.Trim('"')).ToList();
            Assert.AreEqual(6 + 9 * 2, timed.Count, "야외 배경 여섯 + 발판 아홉 지형 × 두 쪽");
            foreach (string day in timed)
                foreach (Sinnoh.Fx.DayTime t in System.Enum.GetValues(typeof(Sinnoh.Fx.DayTime)))
                    Assert.AreEqual(PngSize(Path.Combine(Gen, "Stage", day)), PngSize(Path.Combine(Gen, "Stage", Sinnoh.Fx.DayTimes.Art(day, t, true))), day + " " + t);
            // 야외 지형 짝(동굴·깨어진 세계 밖)의 그림은 모두 시간대가 있다
            foreach (var t in terrain)
                if (t.Groups[1].Value != "null")
                    foreach (int g in new[] { 5, 6, 7 }) CollectionAssert.Contains(timed, t.Groups[g].Value, "시간대 그림이 있어야 한다");
        }

        [Test]
        public void M4_볼던지기그림은이게임의볼넷이고조무래기앞모습이있다()
        {
            // 04 §8-8 68·72행: 포획 연출의 볼(도는 프레임 9장 16×16 가로 + 열린 볼 32×32)과 갤럭시단 조무래기 앞모습(80×80) — build.mjs ball 묶음, 표 Tools/assets/tables/ball_throws.json
            string json = GenText("Stage/Ball/ball.json");
            var balls = Lines(json, "^\\{\"key\":\"([a-z_]+)\",\"source\":\"([a-z_]+)\",\"file\":\"(Ball/[a-z_]+\\.png)\",\"open\":\"(Ball/[a-z_]+\\.png)\"\\}");
            // 볼 등급 순서 = 규칙의 Ball 열거형 = 화면이 쓰는 아이템 키(BattleFeed.BallItem)
            var want = Enumerable.Range(0, System.Enum.GetValues(typeof(Sinnoh.Rules.Ball)).Length).Select(Sinnoh.Fx.BattleFeed.BallItem).ToList();
            CollectionAssert.AreEqual(want, balls.Select(b => b.Groups[1].Value).ToList(), "ball.json의 볼 = 이 게임의 볼 넷");
            foreach (var b in balls)
            {
                Assert.AreEqual((144, 16), PngSize(Path.Combine(Gen, "Stage", b.Groups[3].Value)), b.Groups[3].Value + " 도는 프레임 9 × 16");
                Assert.AreEqual((32, 32), PngSize(Path.Combine(Gen, "Stage", b.Groups[4].Value)), b.Groups[4].Value + " 열린 볼");
            }
            var foes = Lines(json, "^\\{\"key\":\"([a-z_]+)\",\"source\":\"([a-z_]+)\",\"file\":\"(Foe/[a-z_]+\\.png)\",\"foot\":\\[([0-9]+),([0-9]+)\\]\\}");
            CollectionAssert.AreEqual(new[] { "galactic_grunt", "galactic_grunt_f", "commander_mars", "commander_jupiter", "commander_saturn", "cyrus" }, foes.Select(f => f.Groups[1].Value).ToList(), "상대 트레이너 앞모습(조무래기 남·여 — 결정 70 ⑩ · 간부 셋 · 태홍 — 결정 80 L4 나)");
            // 화면이 고르는 그림(FoeArt)이 그 표의 파일이고 발 자리가 같다(화면은 발 (40, 78)로 세운다 — 태홍은 원작 자산 그림이 80줄을 다 써서 발이 한 줄 아래)
            CollectionAssert.AreEquivalent(new[] { Sinnoh.Fx.FoeArt.GruntMale, Sinnoh.Fx.FoeArt.GruntFemale, Sinnoh.Fx.FoeArt.Mars, Sinnoh.Fx.FoeArt.Jupiter, Sinnoh.Fx.FoeArt.Saturn, Sinnoh.Fx.FoeArt.Cyrus }, foes.Select(f => "Stage/" + f.Groups[3].Value).ToList());
            foreach (var f in foes) Assert.AreEqual(f.Groups[1].Value == "cyrus" ? "40,79" : "40,78", f.Groups[4].Value + "," + f.Groups[5].Value, f.Groups[1].Value + " 발 자리");
            foreach (var f in foes) Assert.AreEqual((80, 80), PngSize(Path.Combine(Gen, "Stage", f.Groups[3].Value)), f.Groups[3].Value);
            Assert.AreEqual(9, Directory.GetFiles(Path.Combine(Gen, "Stage/Ball")).Count(f => !f.EndsWith(".meta")), "Ball 폴더 = 볼 넷 × 2 + 표");
            Assert.AreEqual(6, Directory.GetFiles(Path.Combine(Gen, "Stage/Foe")).Count(f => !f.EndsWith(".meta")), "Foe 폴더 = 조무래기 남·여 + 간부 셋 + 태홍");
        }

        [Test]
        public void M9_진화화면배경은원작자산에서짜맞춘DS한화면이다_P10()
        {
            // 결정 80 L11 나: 진화 창 바탕 = 원작 Pt 진화 화면 배경(stage 묶음 — egg_data.narc의 화면을 256×192로)
            const string key = "Stage/Evolve/bg.png";   // = Sinnoh.Client EvolveScreen.BgKey(PlayerBuildTests가 번들에 있는지 본다)
            string file = Path.Combine(Gen, key);
            if (!File.Exists(file)) Assert.Ignore("Gen 없음: node Tools/assets/build.mjs --only stage 먼저 (" + key + ")");   // 새 사본(git archive)에는 Gen 본체가 없다 — 다른 Gen 시험과 같은 꼴
            Assert.AreEqual((256, 192), PngSize(file));
        }

        [Test]
        public void M10_결정167_공중날기컷인띠는원작자산화면의가운데80줄이다()
        {
            // 결정 167: 공중날기 앞 컷인의 띠 = Pt 비전기술 컷인 BG(field_cutin.narc — 화면의 96 ± 40줄, stage 묶음)
            const string key = "Stage/Cutin/band.png";   // = Sinnoh.Client FlyCutin.BandKey(PlayerBuildTests가 번들에 있는지 본다)
            string file = Path.Combine(Gen, key);
            if (!File.Exists(file)) Assert.Ignore("Gen 없음: node Tools/assets/build.mjs --only stage 먼저 (" + key + ")");
            Assert.AreEqual((256, Sinnoh.Fx.FlyCutinBeats.BandTexels), PngSize(file));
        }

        // fonts.mjs DS_KEYS와 같은 규칙: DS 화면(256×192) 안에 나오는 글 — 포켓몬·기술 이름, 타입, 배틀 문장 키.
        // M9 언어 ①: DS 글상자(BattleScreen.DsMessage)가 그리는 배틀 기록 줄 screen.battle.log.* · 기술 고르기 안내 · 우선권 물음과 그 줄에 끼는 이름(볼 = item.* · 온슬롯 · 시련 · 플랜 B·알파 스트라이크)
        internal static bool DsKey(string k) =>
            k.StartsWith("species.") || k.StartsWith("pokemon.") || Regex.IsMatch(k, "^move\\.[^.]+$") || k.StartsWith("galactic.") ||
            k.StartsWith("trainer_card.") || k.StartsWith("ui.types.") || k.StartsWith("ui.arena_dice_roller.") || k.StartsWith("battle.") ||
            k.StartsWith("screen.battle.log.") || k == "screen.battle.guide.token" || k.StartsWith("screen.battle.ds.") ||
            Regex.IsMatch(k, "^screen\\.battle\\.(onslaught|trial)\\.[0-9]$") || Regex.IsMatch(k, "^screen\\.battle\\.move\\.[a-z_]+$") || Regex.IsMatch(k, "^item\\.[a-z0-9_]+$");

        [Test]
        public void M4_글꼴빠진글자0()
        {
            // 03 §7 글꼴: DS 화면 글자가 모두 원작 자산 글꼴 또는 Galmuri 대체 글리프에 있다. 빠진 글자 0, 대체 글리프 수를 보고한다
            var used = new SortedDictionary<int, string>();
            foreach (var lang in new[] { "ko", "en" })
            {
                var t = TestData.LoadStrings(lang);
                foreach (var k in t.Keys.Where(DsKey))
                {
                    string s = Regex.Replace(t[k], "\\{[A-Za-z0-9_]+\\}", "");   // 자리표시자({mon} 등)는 코드가 이름으로 채운다 — fonts.mjs dsChars와 같다
                    for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
                    {
                        int cp = char.ConvertToUtf32(s, i);
                        if (!used.ContainsKey(cp)) used[cp] = k;
                    }
                }
            }
            Assert.Greater(used.Count, 500, "DS 글자(포켓몬·기술 이름 두 언어)");
            var fallbacks = new List<int>();
            foreach (var font in new[] { "system", "message", "subscreen" })
            {
                string json = GenText($"Fonts/pt_{font}.json");
                var head = Regex.Match(json, "^\\{\"schema\":1,\"font\":\"pt_" + font + "\",\"cell\":16,\"columns\":16,\"glyphs\":([0-9]+),\"rom\":3440,\"hangulWidth\":(11|12|13),");
                Assert.IsTrue(head.Success, font + " 머리");
                int glyphs = int.Parse(head.Groups[1].Value);
                var map = new Dictionary<int, int>();
                foreach (var m in Lines(json, "^\\[([0-9]+),([0-9]+),([0-9]+)\\],?$"))
                {
                    int g = int.Parse(m.Groups[2].Value), w = int.Parse(m.Groups[3].Value);
                    Assert.Less(g, glyphs, font + " 글리프 번호");
                    Assert.LessOrEqual(w, 16, font + " 폭");
                    map.Add(int.Parse(m.Groups[1].Value), g);
                }
                Assert.Greater(map.Count, 2350 + 400, font + " 문자표 = 기본 글자 + KS X 1001 2350자 + 대체");
                var missing = used.Keys.Where(cp => !map.ContainsKey(cp)).Select(cp => char.ConvertFromUtf32(cp) + "(" + used[cp] + ")").ToList();
                Assert.AreEqual(0, missing.Count, font + " 빠진 글자: " + string.Join(" ", missing));
                Assert.AreEqual((256, (glyphs + 15) / 16 * 16), PngSize(Path.Combine(Gen, "Fonts", $"pt_{font}.png")), font + " 아틀라스 = 16칸 × 줄");
                var fb = map.Where(kv => kv.Value >= 3440).Select(kv => kv.Key).OrderBy(x => x).ToList();
                Assert.AreEqual(glyphs - 3440, fb.Count, font + " 대체 글리프 = 아틀라스 끝");
                if (font == "system") fallbacks = fb; else CollectionAssert.AreEqual(fallbacks, fb, font + " 대체 글자가 세 글꼴에 같다");
            }
            CollectionAssert.IsSubsetOf(fallbacks, used.Keys, "대체 글리프는 DS 화면 글자만");
            TestContext.WriteLine($"글꼴: DS 글자 {used.Count}개, 대체 글리프(Galmuri) {fallbacks.Count}개: {string.Concat(fallbacks.Select(char.ConvertFromUtf32))}");
            Assert.IsTrue(File.Exists(Path.Combine(Gen, "Fonts/Galmuri11.ttf")) && File.Exists(Path.Combine(Gen, "Fonts/Galmuri11-OFL.txt")), "Galmuri + OFL 전문(03 §4-1)");
        }

        [Test]
        public void M4_아이콘타입상태분류모두()
        {
            // 04 §4-2: TypeChart의 타입 19·상태이상 6(None 뺌)·기술 분류 3 아이콘이 모두 Pt 틀(32×16)로 있다. 글자 = 우리 용어(결정 14 ⑤)
            string json = GenText("Stage/Icons/icons.json");
            var rows = Lines(json, "^\\{\"key\":\"((type|status|category)\\.([A-Za-z]+))\",\"file\":\"([A-Za-z_]+\\.png)\",\"source\":\"((rom|redrawn|new):pt:[a-z]+)\",\"text\":(null|\"([^\"]+)\")(?:,\"term\":\"([^\"]+)\")?");
            Assert.AreEqual(Regex.Matches(json, "\"key\":").Count, rows.Count, "icons.json 모든 행 형식");
            var byKey = rows.ToDictionary(r => r.Groups[1].Value);
            var want = Enum.GetNames(typeof(PokemonType)).Select(n => "type." + n)
                .Concat(Enum.GetNames(typeof(StatusEffect)).Where(n => n != nameof(StatusEffect.None)).Select(n => "status." + n))
                .Concat(Enum.GetNames(typeof(MoveCategory)).Select(n => "category." + n)).ToList();
            Assert.AreEqual(TypeChart.TypeCount + 6 + 3, want.Count);
            CollectionAssert.AreEquivalent(want, byKey.Keys, "아이콘 = 타입 + 상태이상 + 기술 분류");
            foreach (var r in rows) Assert.AreEqual((32, 16), PngSize(Path.Combine(Gen, "Stage/Icons", r.Groups[4].Value)), r.Groups[1].Value);

            var ko = TestData.LoadStrings("ko");
            foreach (var n in Enum.GetNames(typeof(PokemonType)))
            {
                var r = byKey["type." + n];
                string term = ko["ui.types." + n];
                if (r.Groups[9].Success) Assert.AreEqual(term, r.Groups[9].Value, n + " 예외 행의 우리 용어");
                else Assert.AreEqual(term, r.Groups[8].Value, n + " 아이콘 글자 = 우리 용어");
            }
            // 결정 43 ⑥: 혼란·마비·독·화상·얼음·졸음(공식 용어, 전에는 3JA2 「빙결」「졸림」)
            var status = new Dictionary<string, string> { ["Confused"] = "혼란", ["Paralysed"] = "마비", ["Poisoned"] = "독", ["Burned"] = "화상", ["Frozen"] = "얼음", ["Drowsy"] = "졸음" };
            foreach (var kv in status) Assert.AreEqual(kv.Value, byKey["status." + kv.Key].Groups[8].Value, kv.Key);
            Assert.AreEqual("new:pt:normal", byKey["type.Fairy"].Groups[5].Value, "페어리는 Pt에 없어 Pt 틀로 새로");
            Assert.AreEqual("rom:pt:fire", byKey["type.Fire"].Groups[5].Value, "불꽃은 원작 자산 그림 그대로(결정 43, 전에는 「불」로 다시 씀)");
            Assert.IsFalse(rows.Any(r => r.Groups[6].Value == "redrawn"), "다시 쓴 타입 아이콘 없음: 우리 용어 = 원작 자산 타입 이름");
            Assert.AreEqual(want.Count, Directory.GetFiles(Path.Combine(Gen, "Stage/Icons"), "*.png").Length, "Icons에 다른 그림 없음");
        }

        [Test]
        public void M9_영문아이콘_타입상태모두영문판그림()
        {
            // 결정 115: 영문판 Pt(pt_en)에서 뽑은 타입 19 · 상태 6이 한국어 그림과 같은 이름 · 같은 크기(32×16)로 Stage/Icons/en/에 있다(글자 없는 분류 3은 한 벌)
            string json = GenText("Stage/Icons/en/icons.json");
            StringAssert.StartsWith("{\"schema\":1,\"lang\":\"en\",", json);
            var rows = Lines(json, "^\\{\"key\":\"((type|status)\\.([A-Za-z]+))\",\"file\":\"([A-Za-z_]+\\.png)\",\"source\":\"((rom|redrawn|new):pt_en:[a-z]+)\",\"text\":\"([^\"]+)\"(?:,\"term\":\"([^\"]+)\")?");
            Assert.AreEqual(Regex.Matches(json, "\"key\":").Count, rows.Count, "en/icons.json 모든 행 형식");
            var byKey = rows.ToDictionary(r => r.Groups[1].Value);
            var want = Enum.GetNames(typeof(PokemonType)).Select(n => "type." + n)
                .Concat(Enum.GetNames(typeof(StatusEffect)).Where(n => n != nameof(StatusEffect.None)).Select(n => "status." + n)).ToList();
            CollectionAssert.AreEquivalent(want, byKey.Keys, "영문 아이콘 = 타입 + 상태이상");
            foreach (var r in rows)
            {
                Assert.AreEqual(r.Groups[1].Value.Replace('.', '_') + ".png", r.Groups[4].Value, "한국어 그림과 같은 파일 이름");
                Assert.AreEqual(PngSize(Path.Combine(Gen, "Stage/Icons", r.Groups[4].Value)), PngSize(Path.Combine(Gen, "Stage/Icons/en", r.Groups[4].Value)), r.Groups[1].Value + " 크기 = 한국어 그림");
            }
            Assert.AreEqual(want.Count, Directory.GetFiles(Path.Combine(Gen, "Stage/Icons/en"), "*.png").Length, "en에 다른 그림 없음");

            // 글자 = 영문 용어의 대문자(원작 자산 타입 이름과 같아 다시 쓴 것 없음), 페어리 · 상태만 Pt 틀로 새로
            var en = TestData.LoadStrings("en");
            foreach (var n in Enum.GetNames(typeof(PokemonType)))
            {
                var r = byKey["type." + n];
                if (r.Groups[8].Success) Assert.AreEqual(en["ui.types." + n], r.Groups[8].Value, n + " 예외 행의 용어");
                else Assert.AreEqual(en["ui.types." + n].ToUpperInvariant(), r.Groups[7].Value, n + " 아이콘 글자 = 영문 용어");
                Assert.AreEqual(n == "Fairy" ? "new" : "rom", r.Groups[6].Value, n);
            }
            // [가정 #430] Pt 영문판 알약의 세 글자(PAR · PSN · BRN · FRZ) + 혼란 CNF · 졸음 DRW
            var status = new Dictionary<string, string> { ["Confused"] = "CNF", ["Paralysed"] = "PAR", ["Poisoned"] = "PSN", ["Burned"] = "BRN", ["Frozen"] = "FRZ", ["Drowsy"] = "DRW" };
            foreach (var kv in status) Assert.AreEqual(kv.Value, byKey["status." + kv.Key].Groups[7].Value, kv.Key);
        }

        [Test]
        public void M4_마을카드9()
        {
            // 03 §2-4: 고향 마을 카드 9 = cards.json home_village = 마을 고정물, 가운데 그림 1524×1236 → 640폭
            var villages = Lines(TestData.ReadText("cards.json"), "^\\{\"key\":\"home_village\\.([a-z_]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(9, villages.Count);
            CollectionAssert.AreEquivalent(villages, Lines(TestData.ReadText("fixtures.json"), "^\\{\"key\":\"village\\.([a-z_]+)\"").Select(m => m.Groups[1].Value), "마을 카드 = 마을 고정물");
            string json = GenText("Cards/cards.json");
            var rows = Lines(json, "^\\{\"file\":\"(Village/([a-z_]+)\\.png)\",\"w\":([0-9]+),\"h\":([0-9]+),");
            CollectionAssert.AreEquivalent(villages, rows.Select(r => r.Groups[2].Value), "Village 9");
            foreach (var r in rows) Assert.AreEqual((640, 519), PngSize(Path.Combine(Gen, "Cards", r.Groups[1].Value)), r.Groups[1].Value);
            Assert.AreEqual(9, Directory.GetFiles(Path.Combine(Gen, "Cards/Village"), "*.png").Length, "Village에 다른 그림 없음");
            // 트레이너 카드 부품(목업 tc 방법): 등급 3 × base·bar·back + 등급 아이콘 2 + locked 2 + items 14
            foreach (var t in new[] { "bronze", "silver", "gold" })
                foreach (var p in new[] { "base", "bar", "back" }) Assert.AreEqual(1120, PngSize(Path.Combine(Gen, "Cards/Trainer", $"{t}_{p}.png")).W, $"{t}_{p} 1120폭");
            Assert.AreEqual((64, 64), PngSize(Path.Combine(Gen, "Cards/Trainer/icon_moderate.png")));
            Assert.AreEqual((64, 64), PngSize(Path.Combine(Gen, "Cards/Trainer/icon_strong.png")));
            Assert.AreEqual(9 + 2 + 2 + 2, Directory.GetFiles(Path.Combine(Gen, "Cards/Trainer"), "*.png").Length);
            // 세로 뒷면 부품(결정 70 ④ A8 ②): 9칸 테두리 96²(USS 자르는 자리 47) · 볼 문양 256²
            Assert.AreEqual((96, 96), PngSize(Path.Combine(Gen, "Cards/Trainer/back_frame.png")));
            Assert.AreEqual((256, 256), PngSize(Path.Combine(Gen, "Cards/Trainer/back_emblem.png")));
            Assert.AreEqual(14, Directory.GetFiles(Path.Combine(Gen, "Cards/Trainer/items"), "*.png").Length);
        }

        [Test]
        public void M4_운명카드주사위면타입아이콘()
        {
            // 결정 43 ⑪(ui-text-audit §6): 원작 카드 그림·아이콘. cards.json 행 = Cards/ 아래 그림 전부(크기 = PNG 머리), key는 게임 식별자를 모두 덮는다
            string json = GenText("Cards/cards.json");
            var rows = Lines(json, "^\\{\"file\":\"([A-Za-z0-9_/]+\\.png)\",\"w\":([0-9]+),\"h\":([0-9]+),\"source\":\"levatius:assets/[a-z0-9_/.]+\"(?:,\"key\":\"([A-Za-z_.]+)\")?\\}");
            Assert.AreEqual(Regex.Matches(json, "\"file\":").Count, rows.Count, "cards.json 모든 행 형식");
            string cards = Path.Combine(Gen, "Cards");
            var onDisk = Directory.GetFiles(cards, "*.png", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/').Substring(cards.Replace('\\', '/').Length + 1));
            CollectionAssert.AreEquivalent(onDisk, rows.Select(r => r.Groups[1].Value), "Cards/ 그림 = cards.json 행");
            foreach (var r in rows) Assert.AreEqual((int.Parse(r.Groups[2].Value), int.Parse(r.Groups[3].Value)), PngSize(Path.Combine(cards, r.Groups[1].Value)), r.Groups[1].Value);
            List<Match> In(string dir) => rows.Where(r => r.Groups[1].Value.StartsWith(dir + "/")).ToList();
            List<string> Keys(string dir) => In(dir).Where(r => r.Groups[4].Success).Select(r => r.Groups[4].Value).ToList();
            void AllSize(string dir, int count, int w, int h)
            {
                Assert.AreEqual(count, In(dir).Count, dir);
                foreach (var r in In(dir)) Assert.AreEqual((w, h), (int.Parse(r.Groups[2].Value), int.Parse(r.Groups[3].Value)), r.Groups[1].Value);
            }

            // 운명 카드 4장(시트를 자름) + 뒷면 = cards.json change_of_fate
            var fate = Lines(TestData.ReadText("cards.json"), "^\\{\"key\":\"(change_of_fate\\.[a-z]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(4, fate.Count);
            CollectionAssert.AreEquivalent(fate, Keys("Fate"), "운명 카드 앞면 = 데이터 카드");
            AllSize("Fate", 5, 560, 980);
            // 주사위 면: 공격 주사위 그림이 다른 면 3, 상태 주사위 6(None 뺌)
            var dice = Enum.GetNames(typeof(AttackDieFace)).Where(n => n != nameof(AttackDieFace.None)).Select(n => "AttackDieFace." + n)
                .Concat(Enum.GetNames(typeof(StatusEffect)).Where(n => n != nameof(StatusEffect.None)).Select(n => "StatusEffect." + n));
            CollectionAssert.AreEquivalent(dice, Keys("Dice"), "주사위 면 = AttackDieFace + StatusEffect");
            AllSize("Dice", 9, 256, 256);
            // 카드 생성기 부품(원작 생성기 64 px/cm 크기): 타입 19 = PokemonType
            CollectionAssert.AreEquivalent(Enum.GetNames(typeof(PokemonType)).Select(n => "PokemonType." + n), Keys("Generator/types"), "타입 아이콘 = PokemonType");
            AllSize("Generator/types", TypeChart.TypeCount, 128, 128);
            AllSize("Generator/encounter_icons", 9, 128, 128);
            AllSize("Generator/location_icons", 22, 192, 192);
            AllSize("Generator/stats_bases", 2, 224, 128);
            Assert.AreEqual(2, In("Generator/stats_icons").Count, "체력·스피드 아이콘");
            Assert.AreEqual((928, 480), PngSize(Path.Combine(cards, "Generator/move_base.png")));
            // 토큰: 긴 변 256 이하, 정렬 토큰 5·우선권 토큰 앞뒤
            Assert.AreEqual((38, 50, 4), (In("Tokens/item_icons").Count, In("Tokens/item_tokens").Count, In("Tokens/celestial_icons").Count));
            foreach (var r in In("Tokens")) Assert.LessOrEqual(Math.Max(int.Parse(r.Groups[2].Value), int.Parse(r.Groups[3].Value)), 256, r.Groups[1].Value);
            Assert.AreEqual(5, In("Tokens/item_tokens").Count(r => r.Groups[1].Value.EndsWith("_alignment.png")), "정렬 토큰");
            Assert.IsTrue(File.Exists(Path.Combine(cards, "Tokens/item_tokens/initiative_advantage.png")) && File.Exists(Path.Combine(cards, "Tokens/item_tokens/initiative_advantage_back.png")), "우선권 토큰");
        }
    }
}
