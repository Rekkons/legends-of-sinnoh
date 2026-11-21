using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 03 §2-0 들여오기 표를 Gen/ 실제 들여오기 설정으로 본다(Sinnoh.Editor.GenImport가 정한 값이 .meta에 들어갔나).
    /// 기대값은 문서 표에서 따로 적는다(규칙 코드를 다시 부르지 않는다). Gen/이 없으면 건너뛴다.
    /// </summary>
    public class GenImportTests
    {
        const string Gen = "Assets/Sinnoh/Gen/";

        static List<string> Textures(string dir)
        {
            if (!AssetDatabase.IsValidFolder((Gen + dir).TrimEnd('/'))) return new List<string>();
            return AssetDatabase.FindAssets("t:Texture2D", new[] { (Gen + dir).TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".png")).Distinct().OrderBy(p => p).ToList();   // 글꼴(.ttf)의 글리프 텍스처는 뺀다
        }

        /// <summary>PNG IHDR: 크기, 색 형식(0 회색·2 RGB = 알파 없음).</summary>
        static (int w, int h, bool alpha) Png(string path)
        {
            var b = new byte[26];
            using (var f = File.OpenRead(path)) Assert.AreEqual(26, f.Read(b, 0, 26), path);
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            return (Be(16), Be(20), !(b[25] == 0 || b[25] == 2));
        }

        static void Platform(TextureImporter ti, string platform, TextureImporterFormat format, int max, List<string> bad)
        {
            var s = ti.GetPlatformTextureSettings(platform);
            if (!s.overridden || s.format != format || s.maxTextureSize != max) bad.Add($"{ti.assetPath} {platform}: {s.format}/{s.maxTextureSize}(덮어씀 {s.overridden}), 기대 {format}/{max}");
        }

        [Test]
        public void M4_들여오기설정폴더별()
        {
            // 폴더가 아니라 들인 본체로 본다: 폴더 .meta는 추적이라 rebuild-all 전 새 작업 트리에도 폴더가 있다(검토 반영 M4-1)
            if (Textures("Sprites").Count == 0) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            var bad = new List<string>();
            int pixel = 0, font = 0, general = 0, figure = 0;

            // 픽셀 그림: Point, 밉맵 없음, 무압축 RGBA32(글꼴 아틀라스 R8), Clamp, 최대 4096, 크기 그대로
            foreach (var dir in new[] { "Sprites", "Stage", "Trainers", "Fonts", "Field", "Tutorial" })
                foreach (var p in Textures(dir))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    bool isFont = p.StartsWith(Gen + "Fonts/pt_");
                    if (ti.textureType != TextureImporterType.Default || ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp
                        || ti.npotScale != TextureImporterNPOTScale.None || ti.maxTextureSize != 4096 || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.sRGBTexture == isFont
                        || ti.alphaIsTransparency)
                        bad.Add($"{p}: 픽셀 그림 설정 {ti.textureType}/{ti.filterMode}/밉맵 {ti.mipmapEnabled}/{ti.wrapMode}/{ti.npotScale}/{ti.maxTextureSize}/{ti.textureCompression}/sRGB {ti.sRGBTexture}/알파 투명 {ti.alphaIsTransparency}");
                    var fmt = isFont ? TextureImporterFormat.R8 : TextureImporterFormat.RGBA32;
                    Platform(ti, "Standalone", fmt, 4096, bad);
                    Platform(ti, "WebGL", fmt, 4096, bad);
                    if (isFont) font++; else pixel++;
                }

            // 일반 그림: Bilinear, Clamp, alphaIsTransparency 켬(투명 칸에 이웃 색 번지기 — 밉맵·쌍선형 가장자리가 검게 번지지 않게, 검토 반영 M4-1). 4의 배수면 PC BC7 / WebGL DXT1(불투명)·DXT5, 아니면 무압축. Board만 밉맵, 헥스 디퓨즈는 2의 거듭제곱·최대 1024
            foreach (var dir in new[] { "Cards", "Board" })
                foreach (var p in Textures(dir))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    var (w, h, alpha) = Png(p);
                    bool hex = p.StartsWith(Gen + "Board/hex_");
                    bool compress = hex || (w % 4 == 0 && h % 4 == 0);
                    int max = hex ? 1024 : 4096;
                    if (ti.filterMode != FilterMode.Bilinear || ti.wrapMode != TextureWrapMode.Clamp || ti.mipmapEnabled != (dir == "Board") || ti.maxTextureSize != max
                        || ti.npotScale != (hex ? TextureImporterNPOTScale.ToNearest : TextureImporterNPOTScale.None)
                        || ti.textureCompression != (compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed) || !ti.alphaIsTransparency)
                        bad.Add($"{p}: 일반 그림 설정 {ti.filterMode}/{ti.wrapMode}/밉맵 {ti.mipmapEnabled}/{ti.maxTextureSize}/{ti.npotScale}/{ti.textureCompression}/알파 투명 {ti.alphaIsTransparency}");
                    Platform(ti, "Standalone", compress ? TextureImporterFormat.BC7 : TextureImporterFormat.RGBA32, max, bad);
                    Platform(ti, "WebGL", compress ? (alpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1) : TextureImporterFormat.RGBA32, max, bad);
                    general++;
                }

            // 피규어 텍스처: 밉맵, Repeat, Bilinear, PC·WebGL DXT1(불투명)·DXT5
            foreach (var p in Textures("Figures"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var (w, h, alpha) = Png(p);
                bool compress = w % 4 == 0 && h % 4 == 0;
                if (!ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Repeat || ti.filterMode != FilterMode.Bilinear || ti.maxTextureSize != 2048)
                    bad.Add($"{p}: 피규어 텍스처 설정 밉맵 {ti.mipmapEnabled}/{ti.wrapMode}/{ti.filterMode}/{ti.maxTextureSize}");
                var fmt = compress ? (alpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1) : TextureImporterFormat.RGBA32;
                Platform(ti, "Standalone", fmt, 2048, bad);
                Platform(ti, "WebGL", fmt, 2048, bad);
                figure++;
            }

            // 임시 BGM: Vorbis 0.5, Streaming(03 §3-2)
            int bgm = 0;
            if (AssetDatabase.IsValidFolder("Assets/Sinnoh/Gen/Audio/Bgm"))
                foreach (var p in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Sinnoh/Gen/Audio/Bgm" }).Select(AssetDatabase.GUIDToAssetPath))
                {
                    var s = ((AudioImporter)AssetImporter.GetAtPath(p)).defaultSampleSettings;
                    if (s.loadType != AudioClipLoadType.Streaming || s.compressionFormat != AudioCompressionFormat.Vorbis || Mathf.Abs(s.quality - 0.5f) > 0.01f)
                        bad.Add($"{p}: BGM 설정 {s.loadType}/{s.compressionFormat}/{s.quality}");
                    bgm++;
                }

            // 임시 효과음(결정 66 ④): ADPCM·Decompress On Load·모노(03 §3-2)
            int se = 0;
            if (AssetDatabase.IsValidFolder("Assets/Sinnoh/Gen/Audio/Se"))
                foreach (var p in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Sinnoh/Gen/Audio/Se" }).Select(AssetDatabase.GUIDToAssetPath))
                {
                    var ai = (AudioImporter)AssetImporter.GetAtPath(p);
                    var s = ai.defaultSampleSettings;
                    if (s.loadType != AudioClipLoadType.DecompressOnLoad || s.compressionFormat != AudioCompressionFormat.ADPCM || !ai.forceToMono)
                        bad.Add($"{p}: 효과음 설정 {s.loadType}/{s.compressionFormat}/모노 {ai.forceToMono}");
                    se++;
                }

            TestContext.WriteLine($"픽셀 {pixel}, 글꼴 아틀라스 {font}, 일반 {general}, 피규어 {figure}, BGM {bgm}, 효과음 {se}");
            Assert.AreEqual(3, font, "글꼴 아틀라스 pt_system·message·subscreen");
            Assert.AreEqual(334 + 277 + 128 + 9 + 2, pixel, "픽셀 그림 수(도트 334·무대 277(공중날기 컷인 띠 1(결정 167)·진화 화면 배경 1(P10)·영문 아이콘 25(결정 115)·볼 8·조무래기 앞모습 2·간부·태홍 앞모습 4(결정 80 L4 나)·pixelfx 이펙트 시트 42·저녁·밤 무대 그림 48·우선권 토큰 앞뒤 2 포함)·트레이너 128(주인공 앞모습 8 + 손님 인물 용식 1 포함 — 결정 96 Q5 나)·필드 9(용식 포함)·튜토리얼 마박사 2(M6.5))");
            Assert.AreEqual(38 + 161 + 10 + 17 + 73 + 1 + 2, general, "일반 그림 수(+ 2 = 여행 스탬프 시트 · 도장(P12) · + 1 = 울트라 온슬롯 카드 그림 — 결정 87 V24 나 · 카드 38(트레이너 카드 세로 뒷면 부품 2 포함) + 결정 43 ⑪ 운명·아이콘·토큰·주사위 161 + 사당 카드 그림 9 · 종막 문양 1(결정 80 L12·L1 나) + 전용 아이템 아이콘 11 · 균열 카드 그림 6(M6 그림 에셋) + 보드 73)");
            Assert.AreEqual(125, figure, "피규어 텍스처(03 §2-5: 번들 125장)");
            Assert.AreEqual(41, bgm, "곡 23의 클립 41(Pt 원곡 21곡 = 반복 19 + 앞부분 18 + 끝 곡 1 + 통째 되풀이 1(타이틀 SEQ_TITLE00 — 결정 130) · 원작 TTS 곡 2 = 통짜 반복 2 — M9 소리 ②, plan §5 #525 · #666) 들여오기 설정");
            Assert.AreEqual(Sinnoh.Fx.SoundKeys.All.Length, se, "임시 효과음(키마다 하나) 들여오기 설정");
            Assert.IsEmpty(bad, string.Join("\n", bad.Take(20)) + (bad.Count > 20 ? $"\n… {bad.Count}개" : ""));
        }
    }
}
