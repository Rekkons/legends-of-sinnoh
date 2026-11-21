using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// M9 「설명서 그림」: Tools/assets/rulebook.mjs가 원작 룰북 PDF에서 자른 Gen/Rulebook을 들인 모습으로 본다 —
    /// 설명서의 그림 키마다 그림이 있고(언어마다 · 한 벌), 들여오기는 일반 그림 규칙(Bilinear · 밉맵 없음 · 4의 배수라 PC BC7 / WebGL DXT1 · DXT5, plan §5 #341), 크기는 문턱 안.
    /// Gen/Rulebook이 없으면 건너뛴다(node Tools/assets/build.mjs --only rulebook).
    /// </summary>
    public class RulebookFigureTests
    {
        const string Gen = "Assets/Sinnoh/Gen/";

        static JToken Rows()
        {
            if (!File.Exists(Gen + GenAssets.RulebookTable)) Assert.Ignore("설명서 그림 없음: node Tools/assets/build.mjs --only rulebook");
            return JObject.Parse(File.ReadAllText(Gen + GenAssets.RulebookTable))["rows"];
        }

        static (int w, int h, bool alpha) Png(string path)
        {
            var b = new byte[26];
            using (var f = File.OpenRead(path)) Assert.AreEqual(26, f.Read(b, 0, 26), path);
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            return (Be(16), Be(20), !(b[25] == 0 || b[25] == 2));
        }

        [Test]
        public void M9_설명서그림_그림키마다언어별그림이있다()
        {
            var rows = Rows();
            var keys = new HashSet<string>();
            foreach (var lang in new[] { "ko", "en" })
                foreach (var r in JObject.Parse(File.ReadAllText($"Assets/Sinnoh/Data/Json/rulebook.{lang}.json"))["rows"])
                    foreach (var b in r["blocks"])
                    {
                        if ((string)b["kind"] == "figure") keys.Add((string)b["figure"]);
                        if ((string)b["kind"] == "table" && b["icons"] != null) foreach (var icon in b["icons"]) keys.Add((string)icon);   // 표 줄 머리 아이콘(결정 143 ③)
                    }
            CollectionAssert.AreEquivalent(keys, rows.Select(r => (string)r["key"]), "그림 표 = 설명서 그림 키(빠짐 · 남는 줄 없음)");
            var bad = new List<string>();
            int shared = 0, perLang = 0;
            foreach (var r in rows)
            {
                string key = (string)r["key"], ko = (string)r["ko"], en = (string)r["en"];
                if (ko == en) { shared++; if (ko != GenAssets.RulebookFolder + key + ".png") bad.Add($"{key}: 한 벌 이름 {ko}"); }
                else { perLang++; if (ko != GenAssets.RulebookFolder + key + ".ko.png" || en != GenAssets.RulebookFolder + key + ".en.png") bad.Add($"{key}: 언어별 이름 {ko} · {en}"); }
                foreach (string file in new[] { ko, en }.Distinct())
                {
                    if (!File.Exists(Gen + file)) { bad.Add("없음 " + file); continue; }
                    var (w, h, _) = Png(Gen + file);
                    if (w != (int)r["w"] || h != (int)r["h"]) bad.Add($"{file}: {w}×{h} ≠ 표 {r["w"]}×{r["h"]}");
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + file) == null) bad.Add("들이지 않음 " + file);
                }
            }
            var onDisk = Directory.GetFiles(Gen + "Rulebook", "*.png", SearchOption.AllDirectories).Length;
            Assert.IsEmpty(bad, string.Join("\n", bad));
            Assert.AreEqual(71, shared + perLang, "그림 자리 71");
            Assert.AreEqual(shared + 2 * perLang, onDisk, "Gen/Rulebook의 그림 = 표가 가리키는 것뿐");
            TestContext.WriteLine($"설명서 그림 {shared + perLang}: 한 벌 {shared} · 언어별 {perLang}(파일 {onDisk})");
        }

        [Test]
        public void M9_설명서그림_들여오기는일반그림규칙이고크기는문턱안()
        {
            var rows = Rows();
            var bad = new List<string>();
            long pixels = 0;
            foreach (string file in rows.SelectMany(r => new[] { (string)r["ko"], (string)r["en"] }).Distinct())
            {
                var ti = AssetImporter.GetAtPath(Gen + file) as TextureImporter;
                if (ti == null) { bad.Add("들이지 않음 " + file); continue; }
                var (w, h, alpha) = Png(Gen + file);
                pixels += (long)w * h;
                if (w % 4 != 0 || h % 4 != 0) bad.Add($"{file}: {w}×{h} — 4의 배수가 아니면 압축하지 않는다");
                if (w > 2048 || h > 2048) bad.Add($"{file}: {w}×{h} — 한 변 2048 넘음");
                if (ti.filterMode != FilterMode.Bilinear || ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp || ti.npotScale != TextureImporterNPOTScale.None
                    || ti.textureCompression != TextureImporterCompression.Compressed || !ti.alphaIsTransparency || ti.isReadable)
                    bad.Add($"{file}: {ti.filterMode}/밉맵 {ti.mipmapEnabled}/{ti.wrapMode}/{ti.npotScale}/{ti.textureCompression}/알파 투명 {ti.alphaIsTransparency}/읽기 {ti.isReadable}");
                var pc = ti.GetPlatformTextureSettings("Standalone");
                var web = ti.GetPlatformTextureSettings("WebGL");
                if (!pc.overridden || pc.format != TextureImporterFormat.BC7) bad.Add($"{file} Standalone: {pc.format}");
                if (!web.overridden || web.format != (alpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1)) bad.Add($"{file} WebGL: {web.format}(알파 {alpha})");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad.Take(20)));
            // BC7 = 1바이트/픽셀 — 번들(나눠 받기)이 10 MB를 넘지 않게(plan §5 #340: 쪽 폭 2400 px 렌더)
            TestContext.WriteLine($"설명서 그림 픽셀 {pixels:N0} ≈ BC7 {pixels / 1048576.0:F1} MB");
            Assert.Less(pixels, 10L * 1024 * 1024, "설명서 그림 픽셀 합(BC7 바이트) 10 MB 미만");
        }
    }
}
