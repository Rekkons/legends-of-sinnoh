using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 영문 아이콘(결정 115 — 영문판 Pt에서 뽑은 타입 · 상태 그림 <c>Stage/Icons/en/</c>)과 그림 키를 고르는 한 곳 <see cref="IconArt"/>.
    /// Gen/이 없으면 건너뛴다.
    /// </summary>
    public class IconArtTests
    {
        const string Gen = "Assets/Sinnoh/Gen/";

        [TearDown]
        public void Restore() => IconArt.Lang = null;

        [Test]
        public void M9_영문아이콘_언어값으로그림키를고른다()
        {
            // 한국어 · 값 없음 = 지금까지의 키 그대로(한국어 화면은 바뀌지 않는다)
            foreach (string lang in new[] { null, "ko", "", "ja" })
            {
                Assert.AreEqual("Stage/Icons/type_Fire.png", IconArt.Type(PokemonType.Fire, lang), "타입 " + lang);
                Assert.AreEqual("Stage/Icons/status_Paralysed.png", IconArt.Status(StatusEffect.Paralysed, lang), "상태 " + lang);
            }
            Assert.AreEqual("Stage/Icons/en/type_Fire.png", IconArt.Type(PokemonType.Fire, "en"));
            Assert.AreEqual("Stage/Icons/en/status_Paralysed.png", IconArt.Status(StatusEffect.Paralysed, "en"));
            // 갈고리: 언어 값을 넣으면 화면이 부르는 인자 없는 꼴이 따라간다(DS 층의 상태 배지 키도 같은 곳)
            Assert.AreEqual("Stage/Icons/type_Fairy.png", IconArt.Type(PokemonType.Fairy));
            Assert.AreEqual("Stage/Icons/status_Drowsy.png", DsStage.StatusKey(StatusEffect.Drowsy));
            IconArt.Lang = "en";
            Assert.AreEqual("Stage/Icons/en/type_Fairy.png", IconArt.Type(PokemonType.Fairy));
            Assert.AreEqual("Stage/Icons/en/status_Drowsy.png", DsStage.StatusKey(StatusEffect.Drowsy));
            Assert.AreEqual(TypeChart.TypeCount + 6, IconArt.All("en").Count, "타입 19 + 상태 6");
            foreach (string key in IconArt.All("en")) Assert.AreEqual("stage", GenAssets.BundleFor(key), key + " 번들");
        }

        [Test]
        public void M9_영문아이콘_수크기들여오기설정이한국어그림과같다()
        {
            if (!AssetDatabase.IsValidFolder(Gen + "Stage/Icons")) Assert.Ignore("Gen/Stage/Icons 없음");
            var ko = IconArt.All("ko");
            var en = IconArt.All("en");
            Assert.AreEqual(ko.Count, en.Count, "영문 그림 수 = 한국어 그림 수");
            Assert.AreEqual(en.Count, Directory.GetFiles(Gen + "Stage/Icons/en", "*.png").Length, "en 폴더에 다른 그림 없음");
            var bad = new List<string>();
            int differ = 0;
            for (int i = 0; i < ko.Count; i++)
            {
                var a = AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + ko[i]);
                var b = AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + en[i]);
                if (a == null || b == null) { bad.Add((a == null ? ko[i] : en[i]) + " 없음"); continue; }
                if (a.width != b.width || a.height != b.height || b.width != 32 || b.height != 16) bad.Add($"{en[i]} {b.width}×{b.height} ≠ {a.width}×{a.height}");
                var ia = (TextureImporter)AssetImporter.GetAtPath(Gen + ko[i]);
                var ib = (TextureImporter)AssetImporter.GetAtPath(Gen + en[i]);
                string Settings(TextureImporter t) => string.Join("/", t.textureType, t.filterMode, t.mipmapEnabled, t.textureCompression, t.npotScale, t.wrapMode, t.alphaIsTransparency, t.sRGBTexture, t.maxTextureSize, t.isReadable,
                    Platform(t, "Standalone"), Platform(t, "WebGL"));
                if (Settings(ia) != Settings(ib)) bad.Add($"{en[i]} 들여오기 {Settings(ib)} ≠ {Settings(ia)}");
                if (a.format != b.format) bad.Add($"{en[i]} 형식 {b.format} ≠ {a.format}");
                // 영문 그림은 한국어 그림과 다른 그림이다(글자가 없는 「???」만 같다)
                bool same = Convert.ToBase64String(File.ReadAllBytes(Gen + ko[i])) == Convert.ToBase64String(File.ReadAllBytes(Gen + en[i]));
                bool typeless = ko[i] == IconArt.Type(PokemonType.Typeless, "ko");
                if (same != typeless) bad.Add(en[i] + (same ? " 한국어 그림과 같다" : " 「???」가 한국어 그림과 다르다"));
                if (!same) differ++;
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
            Assert.AreEqual(ko.Count - 1, differ);
        }

        [Test]
        public void M9_영문아이콘_영어값이면화면의타입상태칩이영문그림이다()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("그래픽 없음(-nographics): 렌더 시험 건너뜀");
            // 장면이 갈고리에 영어를 넣고, 화면에 한국어 타입 · 상태 그림이 하나라도 있으면 던진다(UiShots.EnIcons) — 도감(타입 18) · 배틀 화면(기둥 · 기술 줄 · 상태 칩)
            var fails = new List<string>();
            foreach (string screen in new[] { "en-icons-dex", "battle-en-icons" })
            {
                var px = Sinnoh.Editor.UiShots.Shoot(screen, new Rekkon.UiKit.UiGrade(720, 1), new List<string>(), fails: fails);
                if (px == null) Assert.Ignore("Gen 없음: rebuild-all 먼저");
                Assert.IsNull(IconArt.Lang, screen + " 뒤 갈고리를 되돌린다");
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        static string Platform(TextureImporter t, string platform)
        {
            var s = t.GetPlatformTextureSettings(platform);
            return platform + ":" + s.overridden + ":" + s.format + ":" + s.maxTextureSize;
        }
    }
}
