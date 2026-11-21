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
    /// plan M4-1c: 원작 자산 넷 변환(Tools/assets/rom.mjs, 결정 8 + 2026-11-07 보충).
    /// 표 시험은 늘 돌고, 원작 자산 시험은 AssetSources/rom/(git 밖)이 없으면 건너뛴다(원작 자산은 이 PC에만 있다).
    /// </summary>
    public class RomSourceTests
    {
        static string Root => TestData.RepoRoot();

        /// <summary>트레이너 카드 키 → 원작 자산 앞모습(원작 자산 31 = Pt 28·HGSS 2·BW 1). 남녀 그림이 있는 직업은 카드 그림에 맞춤 [가정 M4-1c, 03 §2-3].</summary>
        static readonly Dictionary<string, string> RomFronts = new Dictionary<string, string>
        {
            ["ace_trainer"] = "rom:pt:ace_trainer_male", ["aroma_lady"] = "rom:pt:aroma_lady", ["artist"] = "rom:pt:artist",
            ["backpacker"] = "rom:bw:027_backpacker_m", ["beauty"] = "rom:pt:beauty", ["bird_keeper"] = "rom:pt:bird_keeper",
            ["black_belt"] = "rom:pt:black_belt", ["bug_catcher"] = "rom:pt:bug_catcher", ["collector"] = "rom:pt:collector",
            ["dragon_tamer"] = "rom:pt:dragon_tamer", ["fisherman"] = "rom:pt:fisherman", ["gentleman"] = "rom:pt:gentleman",
            ["guitarist"] = "rom:pt:guitarist", ["hiker"] = "rom:pt:hiker", ["interviewers"] = "rom:pt:interviewers",
            ["lady"] = "rom:pt:lady", ["ninja_boy"] = "rom:pt:ninja_boy", ["parasol_lady"] = "rom:pt:parasol_lady",
            ["picnicker"] = "rom:pt:picnicker", ["poke_fan"] = "rom:pt:pokefan_female", ["poke_maniac"] = "rom:hgss:068_poke_maniac",
            ["pokemon_breeder"] = "rom:pt:breeder_female", ["pokemon_ranger"] = "rom:pt:ranger_male", ["psychic"] = "rom:pt:psychic_female",
            ["ruin_maniac"] = "rom:pt:ruin_maniac", ["scientist"] = "rom:pt:scientist", ["swimmer"] = "rom:pt:swimmer_male",
            ["team_rocket"] = "rom:hgss:055_team_rocket", ["veteran"] = "rom:pt:veteran", ["worker"] = "rom:pt:worker",
            ["youngster"] = "rom:pt:youngster",
        };

        static Dictionary<string, string> TableRomRows()
        {
            string table = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/trainer_sprites.json"), new UTF8Encoding(false));
            return Regex.Matches(table, "^\\{\"key\":\"([^\"]+)\",\"class\":\"[^\"]*\",\"source\":\"(rom:[^\"]*)\"", RegexOptions.Multiline)
                .Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        }

        [Test]
        public void M4_원작자산트레이너원천은클래스까지()
        {
            var rows = TableRomRows();
            CollectionAssert.AreEquivalent(RomFronts, rows, "trainer_sprites.json 원작 자산 행 = rom:<게임>:<png 이름>");
            Assert.AreEqual(28, rows.Values.Count(s => s.StartsWith("rom:pt:")));
            Assert.AreEqual(2, rows.Values.Count(s => s.StartsWith("rom:hgss:")));
            Assert.AreEqual(1, rows.Values.Count(s => s.StartsWith("rom:bw:")));
        }

        static int Pngs(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Count(f => !Path.GetFileName(f).StartsWith("_contact")) : -1;

        static (int W, int H) PngSize(string file)
        {
            var b = File.ReadAllBytes(file);
            Assert.AreEqual(0x89, b[0], file);
            int Be(int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];
            return (Be(16), Be(20));
        }

        [Test]
        public void M4_원작자산묶음수와트레이너그림()
        {
            string rom = Path.Combine(Root, "AssetSources/rom");
            var games = new[] { "pt", "hgss", "bw", "b2w2" }.Where(g => File.Exists(Path.Combine(rom, g, "_files.tsv"))).ToList();
            if (games.Count == 0) Assert.Ignore("M4 원작 자산 없음: node Tools/assets/rom.mjs all 먼저");
            string P(string g, string rel) => Path.Combine(rom, g, "png", rel);

            // 묶음 수(nds README·03 §2-2): 배경 35·발판 82·트레이너 앞 105·공식 뒷모습 11·글꼴 5장·창 틀 25·볼 20, 필드 도트 Pt 420(+동굴 벽화 폴더 1)·HGSS 832·BW 758·B2W2 968
            var want = new Dictionary<string, (string Rel, int N)[]>
            {
                ["pt"] = new[] { ("battle_bg", 35), ("battle_platform", 82), ("trainer_front", 105), ("trainer_back/batt_obj", 8), ("font", 5), ("window", 25), ("ball_throw", 20), ("field", 420) },
                ["hgss"] = new[] { ("trainer_front", 129), ("trainer_back", 17), ("field", 832) },
                ["bw"] = new[] { ("trainer_front", 95), ("trainer_back", 6), ("field", 758) },
                ["b2w2"] = new[] { ("trainer_front", 188), ("trainer_back", 31), ("field", 968) },
            };
            foreach (var g in games)
                foreach (var (rel, n) in want[g]) Assert.AreEqual(n, Pngs(P(g, rel)), $"{g} png/{rel}");

            if (games.Contains("pt"))
            {
                var backs = Directory.GetFiles(P("pt", "trainer_back"), "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
                Assert.AreEqual(11, backs.Count(b => !b.EndsWith("_scan")), "Pt 공식 뒷모습 11");
                string verify = File.ReadAllText(Path.Combine(rom, "pt/_verify.json"), new UTF8Encoding(false));
                StringAssert.Contains("\"d3Same81\": 81", verify, "D3 ② pret PNG 81장 픽셀 일치(pngcmp)");
                // 더한 묶음(카드 28종 앞모습·창 틀·볼 던지기 등)도 기대값 밖 결과 0(rom.mjs도 이걸로 실패한다, 검토 반영 M4-1)
                var groups = Regex.Matches(verify, "^  \"([A-Za-z0-9]+)\": \\{\\s*\"n\": [0-9]+,", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                CollectionAssert.IsSupersetOf(groups, new[] { "trainerFronts", "windows", "ballThrows" }, "_verify.json 묶음");
                var unexpected = Regex.Matches(verify, "\"unexpected\": \\[\\s*\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                Assert.IsEmpty(unexpected, "pret 대조 예상 밖: " + string.Join(" / ", unexpected));
                Assert.AreEqual(groups.Count, Regex.Matches(verify, "\"unexpected\": \\[\\]").Count, "묶음마다 예상 밖 0");
            }
            foreach (var g in games.Where(x => x == "bw" || x == "b2w2"))
            {
                int members = File.ReadAllLines(Path.Combine(rom, g, "pokegra/_members.tsv")).Length - 1;
                Assert.AreEqual(g == "bw" ? 14285 : 15065, members, $"{g} 포켓몬 배틀 그림 NARC a/0/0/4 멤버");
            }

            // 카드 직업 원작 자산 앞모습 31: 80×80
            foreach (var kv in TableRomRows())
            {
                var p = kv.Value.Split(':');
                if (!games.Contains(p[1])) continue;
                string file = P(p[1], "trainer_front/" + p[2] + ".png");
                Assert.IsTrue(File.Exists(file), $"{kv.Key}: {file}");
                Assert.AreEqual((80, 80), PngSize(file), kv.Key);
            }

            // 네 게임 주인공 공식 뒷모습(내 트레이너 뒷모습 임시 대응·말 대안, plan §5 #1)
            var mains = new Dictionary<string, string[]>
            {
                ["pt"] = new[] { "player_male", "player_female", "dp_player_male", "dp_player_female" },
                ["hgss"] = new[] { "000_ethan", "001_lyra" }, ["bw"] = new[] { "000_hilbert", "001_hilda" }, ["b2w2"] = new[] { "000_nate", "001_rosa" },
            };
            foreach (var g in games)
                foreach (var m in mains[g]) Assert.IsTrue(File.Exists(P(g, "trainer_back/" + m + ".png")), $"{g} 뒷모습 {m}");

            // 주인공 8명 볼 던지기(M4-3, 결정 53 ④·54 ②): 순서 JSON은 넷 다, 그림(격자 시트)은 Gen 5만 — Gen 4는 위 8셀 시트를 그대로 쓴다
            foreach (var g in games)
                foreach (var m in mains[g].Where(x => !x.StartsWith("dp_")))
                {
                    string t = File.ReadAllText(P(g, "trainer_back/throw/" + m + ".json"), new UTF8Encoding(false));
                    StringAssert.IsMatch("^\\{\"schema\":1,\"game\":\"" + g + "\",\"name\":\"" + m + "\",\"anim\":\"throw \\((NANR|NMAR) seq 1\\)\",\"fw\":[0-9]+,\"fh\":[0-9]+,\"cols\":8,\"frames\":[0-9]+,.*\"fps\":60,\"total\":(98|94|100),\"release\":[0-9]+,\"still\":[0-9]+,.*\"sequence\":\\[(\\[[0-9]+,[0-9]+\\],?)+\\]\\}\n$", t, $"{g} 던지기 {m}");
                    Assert.AreEqual(g == "bw" || g == "b2w2", File.Exists(P(g, "trainer_back/throw/" + m + ".png")), $"{g} 던지기 시트 {m}");
                }
            foreach (var g in games.Where(x => x == "bw" || x == "b2w2"))
                Assert.AreEqual(2, Regex.Matches(File.ReadAllText(P(g, "trainer_index.json")), "\"throw\": \"throw/00[01]_[a-z]+\\.png\"").Count, $"{g} trainer_index.json throw 칸");
        }
    }
}
