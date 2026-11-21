using System.Reflection;
using Rekkon.UiKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 공용 UI 키트에 넣는 이 게임의 겉모양(04 §9-2 L1 도트, 결정 43 ①): Galmuri11 도트 글꼴과 Pt 원작 자산 창틀. 토큰(색·크기)은 키트 것이다.
    /// 글꼴·그림은 Gen/ 키로 읽는다(직렬화로 참조하지 않는다, 03 §0) — 키트 저장소에는 원작 자산 그림을 두지 않는다.
    /// 글꼴 자산은 파일로 두지 않고 실행 중에 TTF에서 만든다(03 §2-2): 동적 FontAsset, 표본 24pt `RASTER_HINTED`, 패딩 8, 아틀라스 2048² Point —
    /// 이것 하나로 12px·24px가 선명하다(04 §9-5).
    /// </summary>
    public static class UiSkin
    {
        public const string FontKey = "Fonts/Galmuri11.ttf";
        public const string PanelFrameKey = "Stage/Window/standard_system.png";
        public const string TipFrameKey = "Stage/Window/standard_field.png";

        /// <summary>Pt 창틀 24×24의 조각 크기(px).</summary>
        public const int FrameSlice = 8;

        public const int FontSampling = 24, FontPadding = 8;

        /// <summary>1080 벌 창 제목 36px(`Kit.FTitle`, 결정 83 ①)의 글꼴 표본 — 표본 24 글꼴로 36을 그리면 1.5배 늘인 그림이라 따로 만든다(제목 글자만 들어간다).</summary>
        public const int TitleSampling = 36;

        /// <summary>
        /// 겉모양을 읽어 만든다. Gen 본체가 없으면 null. 부를 때마다 글꼴 자산을 새로 만드니 화면 뿌리를 쥔 쪽이 하나만 만들어 들고 있다가 <see cref="Release"/>한다.
        /// </summary>
        public static KitSkin Load()
        {
            var font = GenAssets.Load<Font>(FontKey);
            var panel = GenAssets.Load<Texture2D>(PanelFrameKey);
            var tip = GenAssets.Load<Texture2D>(TipFrameKey);
            if (font == null || panel == null || tip == null) return null;
            return new KitSkin
            {
                Font = KitFont.CreatePixel(font, FontSampling, FontPadding),
                TitleFont = KitFont.CreatePixel(font, TitleSampling, FontPadding),
                Panel = new KitFrame(panel, FrameSlice),
                Tip = new KitFrame(tip, FrameSlice),
            };
        }

        /// <summary>이 게임의 화면 USS(자리·크기·게임 색) 한 벌. 원본 <c>Client/Ui/Styles~/Sinnoh.uss</c>에서 키트 생성기가 만든다.</summary>
        public static StyleSheet Sheet(int tokenHeight) => Resources.Load<StyleSheet>("SinnohUi/Sinnoh" + tokenHeight);

        /// <summary>
        /// 화면 뿌리에 키트 토큰 벌 + 글꼴(<see cref="Kit.Attach"/>)과 이 게임의 화면 USS를 건다. 등급이 바뀌면 다시 부른다.
        /// 화면 USS는 늘 키트 USS 뒤에 온다(같은 세기의 규칙이면 화면 것이 이긴다).
        /// </summary>
        public static void Attach(VisualElement root, KitSkin skin, UiGrade grade)
        {
            foreach (int tokens in new[] { 720, 1080 })
            {
                var other = Sheet(tokens);
                if (other != null) root.styleSheets.Remove(other);
            }
            Kit.Attach(root, skin, grade);
            var sheet = Sheet(grade.TokenHeight);
            if (sheet == null) throw new System.InvalidOperationException("화면 USS 없음: Resources/SinnohUi/Sinnoh" + grade.TokenHeight);
            root.styleSheets.Add(sheet);
        }

        /// <summary><see cref="Load"/>가 만든 글꼴 자산(아틀라스·재질 포함)을 없앤다. 창틀 그림은 Gen 에셋이라 그대로 둔다.</summary>
        public static void Release(KitSkin skin)
        {
            if (skin == null || skin.Font == null) return;
            // 글자를 막 더한 글꼴(HasCharacter·TryAddCharacters — 패널이 아직 그리기 전)은 아틀라스가 TextCore의 「올릴 그림」 대기열에 남아 있다.
            // 그대로 없애면 대기열이 없어진 그림을 올리려다 예외를 내고 비워지지 않아, 그 뒤 모든 패널의 글자 만들기가 예외를 낸다(6000.6.3f1 —
            // FontAsset.OnDestroy가 대기열을 정리하지 않는다). 그래서 먼저 대기열을 비운다. 공개 API가 없어 내부 메서드를 부른다(없으면 건너뜀 — UiKitTests가 본다).
            FlushFontQueue?.Invoke(null, null);
            foreach (var font in new[] { skin.Font, skin.TitleFont })
            {
                if (font == null) continue;
                foreach (var atlas in font.atlasTextures) Kill(atlas);
                Kill(font.material);
                Kill(font);
            }
            skin.Font = skin.TitleFont = null;
        }

        static readonly MethodInfo FlushFontQueue = typeof(UnityEngine.TextCore.Text.FontAsset).GetMethod(
            "UpdateFontAssetsInUpdateQueue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
