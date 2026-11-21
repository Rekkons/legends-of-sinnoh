using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// PC 층(UI Toolkit) 연출 부품이 요소에 쓰는 것은 이 셋뿐이다: 옮기기·늘이기·흐리기(04 §8-6 — UI 연출은 transform·opacity만, 크기·자리는 만들 때 한 번).
    /// 배치를 다시 돌리는 style(width·left·margin …)을 연출에서 바꾸지 않는다(「Layout update failed to stabilize」, isekai PT6).
    /// </summary>
    public static class FxUi
    {
        /// <summary>제자리에서 (x, y)px 옮긴다. 도트가 번지지 않게 정수 자리로 맞춘다.</summary>
        public static void Move(VisualElement e, float x, float y) => e.style.translate = new Translate(Mathf.Round(x), Mathf.Round(y));

        public static void Scale(VisualElement e, float x, float y) => e.style.scale = new Scale(new Vector3(x, y, 1f));

        public static void Fade(VisualElement e, float opacity) => e.style.opacity = Mathf.Clamp01(opacity);

        /// <summary>늘이기의 기준을 왼쪽 가운데로(막대가 오른쪽에서 줄어든다). 만들 때 한 번.</summary>
        public static void OriginLeft(VisualElement e) => e.style.transformOrigin = new TransformOrigin(0, Length.Percent(50));
    }
}
