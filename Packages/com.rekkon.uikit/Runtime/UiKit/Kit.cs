using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Rekkon.UiKit
{
    public enum KitButtonKind { Normal, Main, End }

    /// <summary>숫자 색: 기본값 그대로 · 기본보다 낮음 · 높음.</summary>
    public enum KitTone { Base, Low, High }

    public enum KitBadgeKind { Plain, Gold, Silver, Warn, Ok, Red }

    /// <summary>
    /// 기본 요소를 만드는 곳. 겉모양은 토큰 USS(<c>Kit720</c>·<c>Kit1080</c>, 원본 <c>Styles~/Kit.uss</c>)가 정하고, 글꼴·창틀 그림은 <see cref="KitSkin"/>이 준다.
    /// 화면 뿌리에 <see cref="Attach"/>를 한 번 부른 뒤 그 아래에 요소를 붙인다.
    /// </summary>
    public static class Kit
    {
        public const string RootClass = "kit-root";

        /// <summary>글자 크기 클래스: 늘 12px · 본문(720 벌 12 · 1080 벌 24) · 늘 24px. 한 줄 높이가 함께 고정된다.</summary>
        public const string F1 = "kit-f1", F2 = "kit-f2", F3 = "kit-f3";

        /// <summary>
        /// 창·판 제목: 720 벌 24px · 1080 벌 36px(1080 벌 본문이 24라 <see cref="F3"/>로는 제목이 본문과 같다). 36px는 표본 36 글꼴이라야 선명해서,
        /// <see cref="Text"/>로 만든 이 크기의 글에는 1080 벌에서 <see cref="KitSkin.TitleFont"/>를 건다(없으면 뿌리 글꼴).
        /// </summary>
        public const string FTitle = "kit-ftitle";

        /// <summary>뿌리마다 지금 벌의 제목 글꼴(1080 벌이고 <see cref="KitSkin.TitleFont"/>가 있을 때만, 아니면 null). 뿌리가 사라지면 함께 사라진다.</summary>
        static readonly ConditionalWeakTable<VisualElement, TitleFontBox> TitleFonts = new ConditionalWeakTable<VisualElement, TitleFontBox>();

        sealed class TitleFontBox { public FontAsset Font; }

        /// <summary>토큰 벌(720·1080)의 USS. 패키지 Resources에서 읽는다.</summary>
        public static StyleSheet Sheet(int tokenHeight) => Resources.Load<StyleSheet>("RekkonUiKit/Kit" + tokenHeight);

        /// <summary>
        /// 화면 뿌리에 토큰 벌과 글꼴을 건다. 등급이 바뀌면 다시 부른다(다른 벌의 USS는 뗀다).
        /// 패널 배율(<see cref="UiGrade.PanelScale"/>)은 PanelSettings를 가진 쪽이 넣는다.
        /// </summary>
        public static void Attach(VisualElement root, KitSkin skin, UiGrade grade)
        {
            foreach (int tokens in new[] { 720, 1080 })
            {
                var sheet = Sheet(tokens);
                if (sheet == null) throw new InvalidOperationException("키트 USS 없음: Resources/RekkonUiKit/Kit" + tokens);
                if (tokens != grade.TokenHeight) root.styleSheets.Remove(sheet);
                else if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }
            root.AddToClassList(RootClass);
            root.style.unityFontDefinition = new StyleFontDefinition(skin.Font);
            TitleFonts.Remove(root);
            TitleFonts.Add(root, new TitleFontBox { Font = grade.TokenHeight == 1080 ? skin.TitleFont : null });
            root.Query<Label>(className: FTitle).ForEach(SetTitleFont);   // 등급이 바뀌어 다시 걸 때 이미 있는 제목
        }

        /// <summary>한 줄 글.</summary>
        public static Label Text(string text, string size = F2)
        {
            var label = new Label(text);
            label.AddToClassList(size);
            if (size == FTitle) label.RegisterCallback<AttachToPanelEvent>(_ => SetTitleFont(label));
            return label;
        }

        /// <summary>제목 글에 가장 가까운 키트 뿌리의 제목 글꼴을 건다(그 뿌리에 제목 글꼴이 없으면 뿌리 글꼴을 물려받게 비운다).</summary>
        static void SetTitleFont(Label label)
        {
            for (var e = label.parent; e != null; e = e.parent)
                if (TitleFonts.TryGetValue(e, out var box))
                {
                    label.style.unityFontDefinition = box.Font != null ? new StyleFontDefinition(box.Font) : new StyleFontDefinition(StyleKeyword.Null);
                    return;
                }
        }

        /// <summary>여러 줄로 감기는 글. 한글이 낱말 가운데서 끊기지 않게 <see cref="KoText.JoinWords"/>를 거친다.</summary>
        public static Label Wrapped(string text, string size = F2)
        {
            var label = Text(KoText.JoinWords(text), size);
            label.AddToClassList("kit-wrap");
            return label;
        }

        /// <summary>창틀 판.</summary>
        public static VisualElement Panel(KitSkin skin)
        {
            var panel = new VisualElement();
            panel.AddToClassList("kit-panel");
            SetFrame(panel, skin.Panel);
            return panel;
        }

        /// <summary>요소 바탕에 9-slice 창틀을 건다.</summary>
        public static void SetFrame(VisualElement element, KitFrame frame)
        {
            element.style.backgroundImage = new StyleBackground(frame.Image);
            element.style.unitySliceLeft = frame.Slice;
            element.style.unitySliceRight = frame.Slice;
            element.style.unitySliceTop = frame.Slice;
            element.style.unitySliceBottom = frame.Slice;
        }

        /// <summary>단추. 꺼짐은 <c>SetEnabled(false)</c>.</summary>
        public static Button Button(string text, KitButtonKind kind = KitButtonKind.Normal, Action clicked = null)
        {
            var button = new UnityEngine.UIElements.Button(clicked) { text = text };
            // 엔진 기본 테마의 단추 모양을 받지 않는다
            button.RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            button.AddToClassList("kit-btn");
            if (kind == KitButtonKind.Main) button.AddToClassList("kit-btn-main");
            else if (kind == KitButtonKind.End) button.AddToClassList("kit-btn-end");
            return button;
        }

        /// <summary>알약: 아이콘 + 숫자(+ 「/최대」). 예: ♥ 8/8, 스피드 10. 아이콘 그림은 프로젝트가 준다(없으면 숫자만).</summary>
        public static VisualElement Pill(Texture2D icon, string value, KitTone tone = KitTone.Base, string max = null)
        {
            var pill = new VisualElement();
            pill.AddToClassList("kit-pill");
            if (icon != null)
            {
                var image = new VisualElement();
                image.AddToClassList("kit-pill-icon");
                image.style.backgroundImage = new StyleBackground(icon);
                pill.Add(image);
            }
            var number = new Label(value);
            if (tone == KitTone.Low) number.AddToClassList("kit-pill-low");
            else if (tone == KitTone.High) number.AddToClassList("kit-pill-high");
            pill.Add(number);
            if (max != null)
            {
                var of = new Label("/" + max);
                of.AddToClassList("kit-pill-max");
                pill.Add(of);
            }
            return pill;
        }

        /// <summary>배지(짧은 꼬리표).</summary>
        public static Label Badge(string text, KitBadgeKind kind = KitBadgeKind.Plain, string size = F1)
        {
            var badge = Text(text, size);
            badge.AddToClassList("kit-badge");
            if (kind != KitBadgeKind.Plain) badge.AddToClassList("kit-badge-" + kind.ToString().ToLowerInvariant());
            return badge;
        }
    }
}
