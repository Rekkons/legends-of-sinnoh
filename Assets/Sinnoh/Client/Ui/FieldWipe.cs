using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 가운데 필드의 전환 줄무늬(04 §8-8 22행 조우 전환 · 70행 역전환): 가로 줄 열이 좌우에서 엇갈려 닫히고 열린다 — Pt 화면 전환을 본떠 코드로 그린 것이다(원작 자산에 그림이 없다).
    /// 화면들(HUD · 조우 창 · 배틀 화면) 위에 얹는 층 하나이고 마우스를 받지 않는다. 덮은 만큼(<see cref="Set"/>)은 <see cref="GameFlow"/>가 화면 대기열의 시각에서 셈해 넣는다 —
    /// 여기서는 줄을 가로로 늘이기만 한다(연출은 옮기기·늘이기·흐리기만, <see cref="FxUi"/>).
    /// 무늬는 상대에 따라 다르다(M9 다듬기 P14 — <see cref="Look"/>): 줄마다 자라는 쪽 · 늦음은 순수 셈 <see cref="WipeBeats"/>, 색은 USS(<c>sn-wipe-ub</c> 보라 · <c>sn-wipe-legend</c> 빛), 전설은 닫히기 앞의 빛 판(<c>field-wipe-light</c>)도.
    /// 요소 이름: <c>field-wipe</c>·<c>field-wipe-light</c>·<c>field-wipe-bar-&lt;n&gt;</c>.
    /// </summary>
    public sealed class FieldWipe
    {
        public const int Bars = 10;

        public readonly VisualElement Element;
        readonly VisualElement[] bars = new VisualElement[Bars];
        readonly VisualElement light;

        /// <summary>지금 덮은 만큼(0 = 안 보임, 1 = 가운데 필드를 다 덮음).</summary>
        public float Cover { get; private set; }

        /// <summary>전설 전환의 빛 판이 지금 보이는 만큼(0 ~ 1).</summary>
        public float Light { get; private set; }

        /// <summary>지금 무늬 갈래.</summary>
        public WipeStyle Style { get; private set; }

        public FieldWipe()
        {
            Element = new VisualElement { name = "field-wipe", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-wipe");
            light = new VisualElement { name = "field-wipe-light", pickingMode = PickingMode.Ignore };
            light.AddToClassList("sn-wipe-light");
            Element.Add(light);
            for (int i = 0; i < Bars; i++)
            {
                var bar = new VisualElement { name = "field-wipe-bar-" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), pickingMode = PickingMode.Ignore };
                bar.AddToClassList("sn-wipe-bar");
                bars[i] = bar;
                Element.Add(bar);
            }
            Look(WipeStyle.Wild);
            Set(0f);
        }

        /// <summary>패널 뿌리의 맨 위(화면·툴팁 위)에 얹는다. 뿌리가 바뀌었거나 다른 화면이 위로 올라왔으면 다시 올린다.</summary>
        public void Attach(VisualElement root)
        {
            if (root == null) return;
            if (Element.parent != root) root.Add(Element);
            else if (root.IndexOf(Element) != root.childCount - 1) Element.BringToFront();
        }

        /// <summary>
        /// 무늬 갈래를 고른다(상대에 따라 — <see cref="WipeBeats.StyleOf(Sinnoh.Rules.BattleKind)"/>): 줄마다 자라는 쪽(야생 = 짝수 줄은 왼쪽 · 홀수 줄은 오른쪽에서)과 색.
        /// 울트라 비스트·네크로즈마(87행 「균열이 찢어지는 전환」)는 보랏빛(USS <c>sn-wipe-ub</c>), 전설은 빛(<c>sn-wipe-legend</c>).
        /// </summary>
        public void Look(WipeStyle style)
        {
            Style = style;
            Element.EnableInClassList("sn-wipe-ub", style == WipeStyle.Rift);
            Element.EnableInClassList("sn-wipe-legend", style == WipeStyle.Legend);
            for (int i = 0; i < Bars; i++) bars[i].style.transformOrigin = new TransformOrigin(Length.Percent(WipeBeats.Origin(style, i)), Length.Percent(50));
        }

        public void Set(float cover, float lit = 0f)
        {
            Cover = Mathf.Clamp01(cover);
            Light = Mathf.Clamp01(lit);
            Element.style.display = Cover > 0f || Light > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < Bars; i++) FxUi.Scale(bars[i], WipeBeats.Bar(Style, i, Bars, Cover), 1f);
            FxUi.Fade(light, Light);
        }
    }
}
