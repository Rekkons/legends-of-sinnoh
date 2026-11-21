using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-1c2: Gen 5 배틀 도트 렌더(Tools/assets/lib/gen5-sprites.mjs, rom.mjs bw·b2w2 → AssetSources/rom/&lt;게임&gt;/sprites5, 03 §2-1, 결정 8 보충).
    /// 시험 재료는 시험 때 원작 자산(pokegra/ 날 멤버, git 밖)에서 고른다 — 원작 자산에서 나온 바이트를 저장소에 넣지 않는다. 원작 자산이 없으면 건너뛴다.
    /// 여기서 Node 파서와 따로 NCGR·NCLR·NMAR을 읽어 렌더 결과(정지 PNG·애니 JSON)와 맞춰 본다.
    /// </summary>
    public class Gen5SpriteTests
    {
        static string Rom => Path.Combine(TestData.RepoRoot(), "AssetSources/rom");
        // 피카츄(회전 많음)·동미러(회전·확대 없음)·로토무·기라티나(멀티셀 큼)·프리지오(뒤만 회전·확대 없음)
        static readonly int[] Fixtures = { 25, 436, 479, 487, 615 };
        static readonly string[] Sides = { "front", "back" };

        static List<string> Games()
        {
            var g = new[] { "bw", "b2w2" }.Where(x => File.Exists(Path.Combine(Rom, x, "sprites5/_index.json"))).ToList();
            if (g.Count == 0) Assert.Ignore("M4 원작 자산 없음: node Tools/assets/rom.mjs bw b2w2 먼저");
            return g;
        }

        static string[] MemberExt(string game) =>
            File.ReadAllLines(Path.Combine(Rom, game, "pokegra/_members.tsv")).Skip(1).Select(l => l.Split('\t')[1]).ToArray();

        static byte[] Member(string game, string[] ext, int i) =>
            File.ReadAllBytes(Path.Combine(Rom, game, "pokegra", i.ToString("D5") + "." + ext[i]));

        /// <summary>Nitro 파일 첫 블록 몸통(머리 0x10 + 블록 머리 8).</summary>
        static int Block0(byte[] b) => BitConverter.ToUInt16(b, 0x0C) + 8;
        static int U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
        static int U32(byte[] b, int o) => BitConverter.ToInt32(b, o);

        static string Pad3(int k) => k.ToString("D3");

        [Test]
        public void M4_젠5도트멤버배치()
        {
            foreach (var g in Games())
            {
                var ext = MemberExt(g);
                int n = 0;
                while (n * 20 + 19 < ext.Length && ext[n * 20 + 4] == "NCER" && ext[n * 20 + 13] == "NCER") n++;
                Assert.AreEqual(g == "bw" ? 712 : 751, n, $"{g} a/0/0/4 그림 수(0 = 빈 그림, 1~649 = 도감, 나머지 = 폼)");
                string index = File.ReadAllText(Path.Combine(Rom, g, "sprites5/_index.json"), new UTF8Encoding(false));
                StringAssert.Contains($"\"entries\":{n},", index);
                Assert.AreEqual(0, Regex.Matches(index, "\"error\"").Count, $"{g} 렌더 오류 없음");

                var want = new Dictionary<int, string> { [0] = "NCGR", [2] = "NCGR", [4] = "NCER", [5] = "NANR", [6] = "NMCR", [7] = "NMAR", [8] = "bin" };
                foreach (int k in Fixtures)
                {
                    foreach (var kv in want)
                        foreach (int o in new[] { 0, 9 })
                            Assert.AreEqual(kv.Value, ext[k * 20 + o + kv.Key], $"{g} 그림 {k} 멤버 {o + kv.Key}");
                    Assert.AreEqual("NCLR", ext[k * 20 + 18]);
                    Assert.AreEqual("NCLR", ext[k * 20 + 19]);
                    foreach (int o in new[] { 0, 9 })
                    {
                        var still = Member(g, ext, k * 20 + o);
                        int d = Block0(still);
                        Assert.AreEqual((12, 12, 3, 0), (U16(still, d), U16(still, d + 2), U32(still, d + 4), U32(still, d + 12)), "정지 NCGR: 12×12조각 4bpp 조각 순서");
                        var parts = Member(g, ext, k * 20 + o + 2);
                        d = Block0(parts);
                        Assert.AreEqual((16, 32, 3, 1), (U16(parts, d), U16(parts, d + 2), U32(parts, d + 4), U32(parts, d + 12)), "조각 NCGR: 256×128 4bpp 줄 단위");
                        var ncer = Member(g, ext, k * 20 + o + 4);
                        Assert.AreEqual(4, U32(ncer, Block0(ncer) + 8), "NCER 매핑 4 = 2D(폭 32조각)");
                    }
                }
            }
        }

        // ---------- PNG(우리 인코더: 색인 4·8bpp, 필터 0) ----------
        static (int W, int H, byte[] Px, byte[] Plte) ReadPng(string file)
        {
            var b = File.ReadAllBytes(file);
            int o = 8, w = 0, h = 0, depth = 0;
            byte[] plte = null;
            var idat = new MemoryStream();
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            while (o < b.Length)
            {
                int len = Be(o);
                string type = Encoding.ASCII.GetString(b, o + 4, 4);
                if (type == "IHDR") { w = Be(o + 8); h = Be(o + 12); depth = b[o + 16]; Assert.AreEqual(3, b[o + 17], file + ": 색인 PNG"); }
                else if (type == "PLTE") plte = b.Skip(o + 8).Take(len).ToArray();
                else if (type == "IDAT") idat.Write(b, o + 8, len);
                o += 12 + len;
            }
            var z = idat.ToArray();
            var raw = new MemoryStream();
            using (var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress)) ds.CopyTo(raw);
            var r = raw.ToArray();
            int stride = (w * depth + 7) / 8;
            var px = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                Assert.AreEqual(0, r[y * (stride + 1)], file + ": 필터 0");
                for (int x = 0; x < w; x++)
                {
                    int v = r[y * (stride + 1) + 1 + (depth == 8 ? x : x >> 1)];
                    px[y * w + x] = (byte)(depth == 8 ? v : (x & 1) != 0 ? v & 15 : v >> 4);
                }
            }
            return (w, h, px, plte);
        }

        [Test]
        public void M4_젠5도트정지는멤버0과같다()
        {
            // 96×96 = OAM 64×64 · 32×64 · 64×32 · 32×32 조각을 1D 순서로(PokeAPI 정지 PNG와 픽셀이 같다, sprites5-compare)
            var layout = new[] { (0, 0, 8, 8), (64, 0, 4, 8), (0, 64, 8, 4), (64, 64, 4, 4) };
            foreach (var g in Games())
            {
                var ext = MemberExt(g);
                foreach (int k in Fixtures)
                    for (int s = 0; s < 2; s++)
                    {
                        var ncgr = Member(g, ext, k * 20 + s * 9);
                        int d = Block0(ncgr), size = U32(ncgr, d + 16), off = d + U32(ncgr, d + 20);
                        var want = new byte[96 * 96];
                        int t = 0;
                        foreach (var (bx, by, tw, th) in layout)
                            for (int ty = 0; ty < th; ty++)
                                for (int tx = 0; tx < tw; tx++, t++)
                                    for (int i = 0; i < 64; i++)
                                    {
                                        int v = ncgr[off + t * 32 + i / 2];
                                        want[(by + ty * 8 + i / 8) * 96 + bx + tx * 8 + i % 8] = (byte)((i & 1) != 0 ? v >> 4 : v & 15);
                                    }
                        Assert.AreEqual(144 * 32, size);
                        var png = ReadPng(Path.Combine(Rom, g, $"sprites5/static/{Pad3(k)}_{Sides[s]}.png"));
                        Assert.AreEqual((96, 96), (png.W, png.H));
                        CollectionAssert.AreEqual(want, png.Px, $"{g} {k} {Sides[s]} 정지 = 멤버 {s * 9}");

                        // 팔레트 = 멤버 18 NCLR 첫 16색(RGB555 → floor(c·255/31))
                        var nclr = Member(g, ext, k * 20 + 18);
                        int pd = Block0(nclr), poff = pd + U32(nclr, pd + 12);
                        for (int c = 0; c < 16; c++)
                        {
                            int v = U16(nclr, poff + c * 2);
                            var rgb = new[] { v & 31, v >> 5 & 31, v >> 10 & 31 }.Select(x => (byte)(x * 255 / 31)).ToArray();
                            CollectionAssert.AreEqual(rgb, png.Plte.Skip(c * 3).Take(3).ToArray(), $"{g} {k} 팔레트 {c}");
                        }
                    }
            }
        }

        static int Field(string json, string name) => int.Parse(Regex.Match(json, "\"" + name + "\":(-?[0-9]+)").Groups[1].Value);

        [Test]
        public void M4_젠5도트애니길이는NMAR합()
        {
            foreach (var g in Games())
            {
                var ext = MemberExt(g);
                foreach (int k in Fixtures)
                    for (int s = 0; s < 2; s++)
                    {
                        string stem = Path.Combine(Rom, g, $"sprites5/anim/{Pad3(k)}_{Sides[s]}");
                        string json = File.ReadAllText(stem + ".json", new UTF8Encoding(false));
                        string at = $"{g} {k} {Sides[s]}";

                        // NMAR 묶음 0: 반복 시작 뒤 프레임 길이 합 = 한 바퀴(DS 프레임, 60fps)
                        var nmar = Member(g, ext, k * 20 + s * 9 + 7);
                        int d = Block0(nmar), seqOff = d + U32(nmar, d + 4), frOff = d + U32(nmar, d + 8);
                        int nF = U16(nmar, seqOff), loopStart = U16(nmar, seqOff + 2), f0 = U32(nmar, seqOff + 12);
                        int intro = 0, loop = 0;
                        for (int f = 0; f < nF; f++) { int dur = U16(nmar, frOff + f0 + f * 8 + 4); if (f < loopStart) intro += dur; else loop += dur; }
                        Assert.AreEqual(loop, Field(json, "loopFrames"), at + " loopFrames = NMAR 묶음 0 길이 합");
                        Assert.AreEqual(intro, Field(json, "intro"), at);
                        int total = Field(json, "total"), frames = Field(json, "frames");
                        Assert.AreEqual(intro + loop * Field(json, "periodLoops"), total, at + " total = 들머리 + 한 바퀴 × 바퀴 수(CONTINUE 노드가 맞물릴 때까지)");

                        var seq = Regex.Matches(json, "^\\[([0-9]+),([0-9]+),([0-9]+)\\]", RegexOptions.Multiline).Cast<Match>()
                            .Select(m => (F: int.Parse(m.Groups[1].Value), Ds: int.Parse(m.Groups[2].Value), Ms: int.Parse(m.Groups[3].Value))).ToList();
                        Assert.AreEqual(total, seq.Sum(e => e.Ds), at + " 순서의 DS 프레임 합");
                        Assert.AreEqual((int)Math.Round(total * 1000 / 60.0, MidpointRounding.AwayFromZero), seq.Sum(e => e.Ms), at + " ms 합");
                        Assert.IsTrue(seq.All(e => e.F < frames && e.Ds > 0), at + " 프레임 번호·길이");
                        Assert.IsTrue(seq.Zip(seq.Skip(1), (a, b) => a.F != b.F).All(x => x), at + " 이웃한 같은 프레임은 합친다");
                        CollectionAssert.AreEquivalent(Enumerable.Range(0, frames), seq.Select(e => e.F).Distinct(), at + " 모든 고유 프레임이 순서에 나온다");

                        string affine = Regex.Match(json, "\"frameAffine\":\"([01]*)\"").Groups[1].Value;
                        Assert.AreEqual(frames, affine.Length, at);
                        bool exact = json.Contains("\"exact\":true");
                        Assert.AreEqual(exact, Field(json, "affineTime") == 0, at);
                        if (exact) Assert.IsFalse(affine.Contains('1'), at + " 정확한 면은 회전·확대 프레임이 없다");

                        var png = ReadPng(stem + ".png");
                        int w = Field(json, "w"), h = Field(json, "h"), cols = Field(json, "cols");
                        Assert.AreEqual((cols * w, (frames + cols - 1) / cols * h), (png.W, png.H), at + " 시트 = 격자 cols × 고유 프레임");
                        Assert.IsTrue(png.Px.Any(p => p != 0), at);
                    }
            }
        }

        [Test]
        public void M4_젠5도트로스터283종앞뒤()
        {
            var games = Games();
            if (!games.Contains("bw")) Assert.Ignore("M4 BW 변환 없음: node Tools/assets/rom.mjs bw 먼저");
            string pokemon = TestData.ReadText("pokemon.json");
            var dex = Regex.Matches(pokemon, "^\\{.*?\"dex\":([0-9]+),", RegexOptions.Multiline).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().Where(d => d <= 649).ToList();
            Assert.AreEqual(283, dex.Count, "#1~649 = 원작 자산 렌더 283종");
            string S5 = Path.Combine(Rom, "bw/sprites5");
            foreach (int d in dex)
                foreach (var s in Sides)
                {
                    Assert.IsTrue(File.Exists(Path.Combine(S5, $"static/{Pad3(d)}_{s}.png")), $"{d} {s} 정지");
                    Assert.IsTrue(File.Exists(Path.Combine(S5, $"anim/{Pad3(d)}_{s}.png")) && File.Exists(Path.Combine(S5, $"anim/{Pad3(d)}_{s}.json")), $"{d} {s} 애니");
                }

            // 회전·확대가 없는 면(렌더가 정확) = 동미러 앞·뒤, 저리어 앞·뒤, 프리지오 뒤 [확인 M4-1c2, 03 §2-1]
            string index = File.ReadAllText(Path.Combine(S5, "_index.json"), new UTF8Encoding(false));
            var exact = Regex.Matches(index, "^\\{\"entry\":([0-9]+),\"side\":\"(front|back)\".*\"exact\":true", RegexOptions.Multiline).Cast<Match>()
                .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value)).Where(x => dex.Contains(x.Item1)).Select(x => $"{x.Item1}{x.Item2[0]}").ToList();
            CollectionAssert.AreEquivalent(new[] { "436f", "436b", "602f", "602b", "615b" }, exact, "로스터에서 회전·확대 없는 면");

            // 폼: sprite_forms.json rom 열 = BW 칸(PokeAPI 폼 정지 그림과 픽셀이 같은 칸, sprites5-compare가 확인)
            string forms = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/assets/tables/sprite_forms.json"), new UTF8Encoding(false));
            var rom = Regex.Matches(forms, "^\\{\"key\":\"([^\"]+)\".*\"rom\":\"bw:([0-9]+)\"", RegexOptions.Multiline).Cast<Match>().ToList();
            Assert.AreEqual(13, rom.Count, "원작 자산에 있는 폼 13(도롱마담 2·체리꼬·깝질무·트리토돈·로토무 5·기라티나·쉐이미·불비달마), 나머지 7은 원작 자산에 없음");
            foreach (var m in rom)
            {
                int k = int.Parse(m.Groups[2].Value);
                Assert.IsTrue(k >= 650 && k < 712, $"{m.Groups[1].Value}: 칸 {k}(BW 폼 칸 650~711)");
                foreach (var s in Sides) Assert.IsTrue(File.Exists(Path.Combine(S5, $"anim/{Pad3(k)}_{s}.json")), $"{m.Groups[1].Value} {s}");
            }
        }

        [Test]
        public void M4_젠5도트정지PokeAPI대조566()
        {
            // plan M4-1 조건 ② · 03 §2-1: 원작 자산 정지 #1~649 283종 앞·뒤가 PokeAPI(커밋 고정)와 566/566 같다 — 원작 자산 정지가 정본인 근거.
            // 보고(sprites5-compare.mjs, rebuild-all 6단계 check가 늘 다시 만든다)가 지금 원작 자산으로 만든 것인지 inputs(_files.tsv sha1)로 본다(검토 반영 M4-1)
            Games();
            string file = Path.Combine(Rom, "sprites5-compare.json");
            if (!File.Exists(file)) Assert.Ignore("M4 대조 보고 없음: node Tools/assets/sprites5-compare.mjs 먼저");
            string t = File.ReadAllText(file, new UTF8Encoding(false));
            StringAssert.Contains("\"pokeapi\": \"PokeAPI/sprites@1b3b7c0\"", t, "PokeAPI 커밋 = fetch-lists PIN");
            var bw = Regex.Match(t, "\"static\": \\{\\s*\"bw\": \\{\\s*\"n\": ([0-9]+),\\s*\"same\": ([0-9]+),\\s*\"diff\": \\[\\]");
            Assert.IsTrue(bw.Success, "static.bw = {n, same, diff: []}");
            Assert.AreEqual(("566", "566"), (bw.Groups[1].Value, bw.Groups[2].Value), "BW 정지 앞·뒤 = PokeAPI");
            foreach (var g in new[] { "bw", "b2w2" })
            {
                string tsv = Path.Combine(Rom, g, "_files.tsv");
                if (!File.Exists(tsv)) continue;
                string sha;
                using (var h = System.Security.Cryptography.SHA1.Create()) sha = string.Concat(h.ComputeHash(File.ReadAllBytes(tsv)).Select(x => x.ToString("x2")));
                StringAssert.Contains($"\"{g}\": \"{sha}\"", t, g + " 보고가 지금 원작 자산으로 만든 것(낡은 보고면 sprites5-compare.mjs를 다시)");
            }
        }
    }
}
