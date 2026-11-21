using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Client;
using Sinnoh.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// PC 층 기반(04 §9, plan M4-3): 공용 UI 키트의 토큰 USS 두 벌이 오류 없이 들어오고, Galmuri 글꼴이 정한 설정의 동적 FontAsset으로 만들어지며,
    /// 키트 갤러리(<see cref="KitGallery"/>)가 등급 넷 모두 예외·오류 로그 없이 만들어지나. 그려진 모습은 여기서 보지 않는다(배치 캡처가 본다).
    /// Gen/이 없으면 글꼴·갤러리 시험은 건너뛴다.
    /// </summary>
    public class UiKitTests
    {
        static KitSkin Skin()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            return skin;
        }

        [Test]
        public void M4_키트토큰USS두벌은오류없이들여온다()
        {
            foreach (int tokens in new[] { 720, 1080 })
            {
                var sheet = Kit.Sheet(tokens);
                Assert.IsNotNull(sheet, "Kit" + tokens);
                Assert.IsFalse(sheet.importedWithErrors, "Kit" + tokens + " 들여오기 오류");
                Assert.IsFalse(sheet.importedWithWarnings, "Kit" + tokens + " 들여오기 경고");
            }
            Assert.AreNotSame(Kit.Sheet(720), Kit.Sheet(1080));
        }

        [Test]
        public void M4_Galmuri글꼴은실행중만든동적FontAsset()
        {
            var skin = Skin();
            try
            {
                var font = skin.Font;
                Assert.AreEqual(AtlasPopulationMode.Dynamic, font.atlasPopulationMode);
                Assert.AreEqual(GlyphRenderMode.RASTER_HINTED, font.atlasRenderMode);
                Assert.AreEqual(24, font.faceInfo.pointSize, 0.001);
                Assert.AreEqual(8, font.atlasPadding);
                Assert.AreEqual(2048, font.atlasWidth);
                Assert.AreEqual(2048, font.atlasHeight);
                Assert.AreEqual(FilterMode.Point, font.atlasTexture.filterMode);
                Assert.AreEqual("", AssetDatabase.GetAssetPath(font), "글꼴 자산은 파일로 두지 않는다(03 §2-2)");
                // 화면 글자가 글꼴에 있다. U+2060(한글 줄바꿈 막기)은 여기서 보지 않는다 — TextCore가 모든 FontAsset의 글자 표에 만들어 넣어
                // 어느 글꼴에서나 참이다(Galmuri11에는 그 글리프가 없다). 그 글자가 든 글은 아래 줄바꿈 시험이 패널에서 잰다
                foreach (char c in "가뷁A9·−「」♥★→")
                    Assert.IsTrue(font.HasCharacter(c, false, true), "U+" + ((int)c).ToString("X4"));
            }
            finally { UiSkin.Release(skin); }
        }

        [Test]
        public void M4_한글줄바꿈글은폭이늘지않고띄어쓰기에서만줄이바뀐다()
        {
            // KoText.JoinWords가 넣는 U+2060을 글자 엔진이 폭 0으로 다루고(네모 글자로 그리면 폭이 는다) 그 자리에서 줄을 바꾸지 않나.
            // 줄 수는 높이 / 16(12px 글의 줄 간격)으로 센다 — U+2060이 든 줄은 1px쯤 높다(plan §5 #25)
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 패널 배치 시험 건너뜀");
            const string text = "가 나다라 마";
            var skin = Skin();
            try
            {
                using (var panel = new UiShotPanel(320, 240, 1))
                {
                    Kit.Attach(panel.Root, skin, new UiGrade(720, 1));
                    Label Add(Label label)
                    {
                        label.style.alignSelf = Align.FlexStart;
                        panel.Root.Add(label);
                        return label;
                    }
                    var plainLine = Add(Kit.Text(text, Kit.F1));
                    var joinedLine = Add(Kit.Text(KoText.JoinWords(text), Kit.F1));
                    var head = Add(Kit.Text("가 나다", Kit.F1));
                    panel.Tick(3);
                    Assert.Greater(plainLine.layout.width, 0f);
                    Assert.AreEqual(plainLine.layout.width, joinedLine.layout.width, 0.01f, "U+2060은 폭이 없다");

                    // 「가 나다」까지만 들어가는 폭: 그냥 글은 낱말 가운데(다|라)에서 끊겨 두 줄, 거친 글은 띄어쓰기에서만 끊겨 세 줄(가 / 나다라 / 마)
                    float width = head.layout.width + 1;
                    var plain = Add(Kit.Text(text, Kit.F1));
                    plain.AddToClassList("kit-wrap");
                    var joined = Add(Kit.Wrapped(text, Kit.F1));
                    plain.style.width = width;
                    joined.style.width = width;
                    panel.Tick(3);
                    Assert.AreEqual(2, Mathf.RoundToInt(plain.layout.height / 16f), "그냥 글 높이 " + plain.layout.height);
                    Assert.AreEqual(3, Mathf.RoundToInt(joined.layout.height / 16f), "거친 글 높이 " + joined.layout.height);
                }
            }
            finally { UiSkin.Release(skin); }
        }

        [Test]
        public void M4_키트갤러리는등급넷모두예외없이만들어진다()
        {
            var skin = Skin();
            try
            {
                foreach (var grade in new[] { new UiGrade(720, 1), new UiGrade(1080, 1), new UiGrade(720, 2), new UiGrade(1080, 2) })
                {
                    var root = new VisualElement();
                    var tips = KitGallery.Build(root, skin, grade);
                    string at = grade.ToString();

                    Assert.IsTrue(root.ClassListContains(Kit.RootClass), at);
                    Assert.AreEqual(1, root.styleSheets.count, at);
                    Assert.AreSame(Kit.Sheet(grade.TokenHeight), root.styleSheets[0], at);

                    // 판 다섯: Pt 창틀이 주입됐다(키트에는 그림이 없다)
                    foreach (var name in new[] { "gallery-buttons", "gallery-pills", "gallery-badges", "gallery-text", "gallery-scrim" })
                    {
                        var panel = root.Q(name);
                        Assert.IsNotNull(panel, name);
                        Assert.AreSame(skin.Panel.Image, panel.style.backgroundImage.value.texture, name);
                        Assert.AreEqual(UiSkin.FrameSlice, panel.style.unitySliceLeft.value, name);
                    }
                    Assert.IsTrue(root.Q<Button>("gallery-btn-main").ClassListContains("kit-btn-main"), at);
                    Assert.IsTrue(root.Q<Button>("gallery-btn-end").ClassListContains("kit-btn-end"), at);
                    Assert.IsFalse(root.Q<Button>("gallery-btn-off").enabledSelf, at);
                    Assert.IsNotNull(root.Q("gallery-scrim-cover"), at);
                    // 여러 줄 글은 낱말 안에 U+2060이 들어 있다
                    StringAssert.Contains("졸⁠음", root.Q<Label>("gallery-wrapped").text);

                    // 툴팁: 걸린 대상에만 뜨고, 다시 걸면 내용만 바뀐다. 자리는 패널에서 배치된 뒤에 잡힌다(캡처에서 본다)
                    var end = root.Q("gallery-btn-end");
                    Assert.AreEqual(DisplayStyle.None, tips.Element.style.display.value, at);
                    tips.Show(end);
                    Assert.AreSame(end, tips.Target, at);
                    Assert.AreEqual(DisplayStyle.Flex, tips.Element.style.display.value, at);
                    Assert.AreSame(tips.Element, root.Children().Last(), at);
                    Assert.AreSame(skin.Tip.Image, tips.Element.style.backgroundImage.value.texture, at);
                    tips.Hook(end, "턴 종료", "바뀐 글");
                    Assert.AreEqual("바⁠뀐 글", tips.Element.Q<Label>(className: "kit-wrap").text, at);
                    tips.Hide();
                    Assert.IsNull(tips.Target, at);
                    Assert.AreEqual(DisplayStyle.None, tips.Element.style.display.value, at);
                    tips.Show(root.Q("gallery-btn"));   // 걸지 않은 대상
                    Assert.IsNull(tips.Target, at);
                    tips.Show(root.Q("gallery-pill-heart-low"));   // 제목만 있는 툴팁
                    Assert.AreEqual(DisplayStyle.None, tips.Element.Q<Label>(className: "kit-wrap").style.display.value, at);
                }

                // 등급이 바뀌어 다시 걸면 토큰 벌이 바뀐다(두 벌이 함께 걸리지 않는다)
                var again = new VisualElement();
                Kit.Attach(again, skin, new UiGrade(720, 1));
                Kit.Attach(again, skin, new UiGrade(1080, 2));
                Kit.Attach(again, skin, new UiGrade(1080, 2));
                Assert.AreEqual(1, again.styleSheets.count);
                Assert.AreSame(Kit.Sheet(1080), again.styleSheets[0]);
            }
            finally { UiSkin.Release(skin); }
        }

        [Test]
        public void M5_창제목은1080벌에서표본36글꼴로36px다()
        {
            // 결정 83 ①: 제목 36px는 표본 36 글꼴이라야 선명하다. 720 벌은 뿌리 글꼴(표본 24)로 24px, 등급을 바꿔 다시 걸면 이미 있는 제목도 바뀐다
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("M5 그래픽 없음(-nographics): 패널 배치 시험 건너뜀");
            var skin = Skin();
            try
            {
                Assert.AreEqual(UiSkin.TitleSampling, skin.TitleFont.faceInfo.pointSize, 0.001);
                Assert.AreEqual(FilterMode.Point, skin.TitleFont.atlasTexture.filterMode);
                using (var panel = new UiShotPanel(320, 240, 1))
                {
                    Kit.Attach(panel.Root, skin, new UiGrade(1080, 1));
                    var title = Kit.Text("제작", Kit.FTitle);
                    panel.Root.Add(title);
                    panel.Tick(3);
                    Assert.AreEqual(36f, title.resolvedStyle.fontSize, "1080 크기");
                    Assert.AreSame(skin.TitleFont, title.resolvedStyle.unityFontDefinition.fontAsset, "1080 글꼴");

                    Kit.Attach(panel.Root, skin, new UiGrade(720, 1));
                    panel.Tick(3);
                    Assert.AreEqual(24f, title.resolvedStyle.fontSize, "720 크기");
                    Assert.AreSame(skin.Font, title.resolvedStyle.unityFontDefinition.fontAsset, "720 글꼴 = 뿌리 글꼴");
                }
            }
            finally { UiSkin.Release(skin); }
        }

        [Test]
        public void M4_키트배지안쪽여백은Label기본값에덮이지않는다()
        {
            // 배지는 Label이다. 바탕 규칙 `.kit-root Label { padding: 0 }`이 `.kit-badge`의 여백보다 세서 배지 바탕이 글자에 딱 붙었다(갤러리 캡처에서 봄)
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 패널 배치 시험 건너뜀");
            var skin = Skin();
            try
            {
                foreach (var want in new[] { new KeyValuePair<UiGrade, float>(new UiGrade(720, 1), 5f), new KeyValuePair<UiGrade, float>(new UiGrade(1080, 1), 8f) })
                    using (var panel = new UiShotPanel(want.Key.Width, want.Key.Height, want.Key.PanelScale))
                    {
                        KitGallery.Build(panel.Root, skin, want.Key);
                        panel.Tick(3);
                        var badge = panel.Root.Q<Label>(className: "kit-badge");
                        Assert.IsNotNull(badge, want.Key.ToString());
                        Assert.AreEqual(want.Value, badge.resolvedStyle.paddingLeft, want.Key + " 왼쪽");
                        Assert.AreEqual(want.Value, badge.resolvedStyle.paddingRight, want.Key + " 오른쪽");
                        Assert.AreEqual(0f, badge.resolvedStyle.paddingTop, want.Key + " 위");
                    }
            }
            finally { UiSkin.Release(skin); }
        }

        [Test]
        public void M4_글꼴은글자를더한직후에없애도다음패널의글자가만들어진다()
        {
            // 글자를 더하면(패널이 그리기 전) 아틀라스가 TextCore의 올릴 그림 대기열에 남는다. UiSkin.Release가 대기열을 비우지 않고 없애면
            // 다음 패널의 글자 만들기가 MissingReferenceException 로그를 낸다(그러면 이 시험은 예상하지 않은 오류 로그로 실패한다)
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 패널 배치 시험 건너뜀");
            var skin = Skin();
            Assert.IsTrue(skin.Font.HasCharacter('가', false, true));
            UiSkin.Release(skin);

            skin = Skin();
            try
            {
                using (var panel = new UiShotPanel(64, 32, 1))
                {
                    Kit.Attach(panel.Root, skin, new UiGrade(720, 1));
                    var label = Kit.Text("가나다", Kit.F1);
                    panel.Root.Add(label);
                    panel.Tick(3);
                    Assert.Greater(label.layout.width, 0f);
                }
            }
            finally { UiSkin.Release(skin); }
        }
    }
}
