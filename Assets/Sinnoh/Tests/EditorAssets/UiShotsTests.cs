using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Editor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// UI 배치 캡처 스모크(04 §9-6, plan M4-3 「PC 층 기반」): <see cref="UiShotPanel"/>이 부르는 내부 메서드(<c>UIElementsRuntimeUtility</c>)는
    /// 공개 API가 아니라 Unity를 올리면 이름·인자가 바뀌거나 사라질 수 있다 — 그때 여기서 바로 드러나게 한다.
    /// 이름·인자가 있는지, 작은 UI를 RT에 그리면 정한 자리에 정한 색이 나오는지, 키트 갤러리를 두 번 찍어 픽셀이 같고 「Layout update failed to stabilize」가 없는지.
    /// 그래픽 장치가 없으면(-nographics) 그리는 시험은 건너뛰고, Gen/이 없으면 갤러리 시험은 건너뛴다.
    /// </summary>
    public class UiShotsTests
    {
        static void NeedGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
        }

        [Test]
        public void M4_UI캡처가부르는내부메서드가있다()
        {
            Assert.IsNotNull(typeof(VisualElement).Assembly.GetType(UiShotPanel.UtilityType), UiShotPanel.UtilityType + " 형식이 없다 — 캡처 경로를 PlayMode 배치로 옮긴다(04 §9-6)");
            CollectionAssert.IsEmpty(UiShotPanel.MissingInternals(), "내부 메서드 이름·인자가 바뀌었다 — 캡처 경로를 PlayMode 배치로 옮긴다(04 §9-6)");
        }

        [Test]
        public void M4_UI캡처는작은UI를RT의정한자리에정한색으로그린다()
        {
            NeedGraphics();
            // 패널 좌표 (8, 4)에 16×8 상자 둘(위아래로 붙여): 불투명 단색, 그 아래 갤러리 바탕색. 배율 2면 물리 픽셀로 두 배 자리·크기.
            // 색은 채널마다 ±2까지 같다고 본다(Linear 색 공간을 거치며 255가 254로 읽힌다)
            var red = new Color32(255, 0, 0, 255);
            var blue = new Color32(0x58, 0x78, 0xA0, 255);
            foreach (int scale in new[] { 1, 2 })
            {
                const int W = 64, H = 48;
                using (var panel = new UiShotPanel(W * scale, H * scale, scale))
                {
                    Assert.AreEqual(W, panel.Root.layout.width, 0.01, "뿌리 폭(패널 좌표) ×" + scale);
                    Assert.AreEqual(H, panel.Root.layout.height, 0.01, "뿌리 높이(패널 좌표) ×" + scale);
                    panel.Root.Add(Box(8, 4, 16, 8, red));
                    panel.Root.Add(Box(8, 12, 16, 8, blue));
                    panel.Tick(3);
                    var px = panel.Read();
                    Assert.AreEqual(W * scale * H * scale, px.Length);

                    int reds = 0, blues = 0, blacks = 0;
                    for (int y = 0; y < H * scale; y++)
                        for (int x = 0; x < W * scale; x++)
                        {
                            var p = px[(H * scale - 1 - y) * W * scale + x];   // 위 줄부터 세는 자리 → 아래 줄부터인 배열
                            bool inRed = x >= 8 * scale && x < 24 * scale && y >= 4 * scale && y < 12 * scale;
                            bool inBlue = x >= 8 * scale && x < 24 * scale && y >= 12 * scale && y < 20 * scale;
                            string at = $"×{scale} ({x}, {y})";
                            if (inRed) { Assert.IsTrue(Near(red, p), at + " " + p); reds++; }
                            else if (inBlue) { Assert.IsTrue(Near(blue, p), at + " " + p); blues++; }
                            else { Assert.IsTrue(Near(new Color32(0, 0, 0, 255), p), at + " " + p); blacks++; }
                        }
                    Assert.AreEqual(16 * 8 * scale * scale, reds);
                    Assert.AreEqual(16 * 8 * scale * scale, blues);
                    Assert.AreEqual(W * H * scale * scale - reds - blues, blacks);
                }
            }
        }

        [Test]
        public void M4_UI캡처키트갤러리는두번찍어도같고배치가흔들리지않는다()
        {
            NeedGraphics();
            var layoutLogs = new List<string>();
            // 배율 1과 2, 툴팁을 올린 장면(툴팁은 배치가 끝난 뒤 자리를 다시 잡는다 — 배치가 되풀이되기 쉬운 곳)
            foreach (var shot in new[] { new KeyValuePair<string, UiGrade>("kit-gallery-tip-end", new UiGrade(720, 1)), new KeyValuePair<string, UiGrade>("kit-gallery-tip-speed", new UiGrade(720, 2)) })
            {
                string at = shot.Key + "-" + shot.Value.Height;
                var first = UiShots.Shoot(shot.Key, shot.Value, layoutLogs);
                if (first == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var again = UiShots.Shoot(shot.Key, shot.Value, layoutLogs);
                Assert.AreEqual(shot.Value.Width * shot.Value.Height, first.Length, at);

                int differing = 0;
                var colors = new HashSet<Color32>();
                for (int i = 0; i < first.Length; i++)
                {
                    if (!first[i].Equals(again[i])) differing++;
                    colors.Add(first[i]);
                }
                Assert.AreEqual(0, differing, at + " 두 번 찍어 다른 픽셀");
                // 바탕(#5878A0)·판·글자·단추 색이 모두 나온다 — 빈 그림·한 색 그림이 아니다
                Assert.Greater(colors.Count, 8, at + " 색 수");
                Assert.IsTrue(Near(new Color32(0x58, 0x78, 0xA0, 255), first[0]), at + " 갤러리 바탕색(왼쪽 아래 구석) " + first[0]);
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        static bool Near(Color32 want, Color32 got) =>
            Mathf.Abs(want.r - got.r) <= 2 && Mathf.Abs(want.g - got.g) <= 2 && Mathf.Abs(want.b - got.b) <= 2 && got.a == 255;

        static VisualElement Box(int left, int top, int width, int height, Color32 color)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = left;
            box.style.top = top;
            box.style.width = width;
            box.style.height = height;
            box.style.backgroundColor = new StyleColor(color);
            return box;
        }
    }
}
