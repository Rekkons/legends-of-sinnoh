using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 각본 튜토리얼 층의 덮개(07 §9-1): 화면을 어둡게 덮고 앵커 사각형마다 둥근 구멍을 낸다(Painter2D 짝홀 채우기 — 바깥 사각형 + 구멍 경로).
    /// 색 = USS <c>color</c>, 구멍 모서리 = <c>border-top-left-radius</c>, 앵커 둘레 틈 = <c>padding-left</c>(<c>sn-tut-dim</c>). 마우스를 받지 않는다 — 마우스 오버는 아래로 통과하고,
    /// 클릭은 층이 패널에서 거른다(<see cref="TutorialLayer"/>).
    /// </summary>
    public sealed class TutorialLayerDim : VisualElement
    {
        readonly List<Rect> holes = new List<Rect>();

        public TutorialLayerDim()
        {
            name = "tut-layer-dim";
            pickingMode = PickingMode.Ignore;
            AddToClassList("sn-tut-dim");
            generateVisualContent += Paint;
        }

        /// <summary>구멍(패널 좌표 — 앵커 사각형 그대로, 틈은 그릴 때 더한다).</summary>
        public IReadOnlyList<Rect> Holes => holes;

        /// <summary>앵커 둘레 틈(패널 px).</summary>
        public float Gap => resolvedStyle.paddingLeft;

        /// <summary>구멍을 바꾼다(같으면 다시 그리지 않는다).</summary>
        public void Set(IReadOnlyList<Rect> rects)
        {
            bool same = rects.Count == holes.Count;
            for (int i = 0; same && i < rects.Count; i++) same = rects[i] == holes[i];
            if (same) return;
            holes.Clear();
            for (int i = 0; i < rects.Count; i++) holes.Add(rects[i]);
            MarkDirtyRepaint();
        }

        /// <summary>틈을 더한 구멍(이 요소의 좌표).</summary>
        public Rect Local(Rect world)
        {
            float gap = Gap;
            var min = this.WorldToLocal(new Vector2(world.xMin, world.yMin));
            var max = this.WorldToLocal(new Vector2(world.xMax, world.yMax));
            return Rect.MinMaxRect(min.x - gap, min.y - gap, max.x + gap, max.y + gap);
        }

        void Paint(MeshGenerationContext mgc)
        {
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
            var p = mgc.painter2D;
            p.fillColor = resolvedStyle.color;
            p.BeginPath();
            p.MoveTo(new Vector2(0f, 0f));
            p.LineTo(new Vector2(w, 0f));
            p.LineTo(new Vector2(w, h));
            p.LineTo(new Vector2(0f, h));
            p.ClosePath();
            float radius = resolvedStyle.borderTopLeftRadius;
            foreach (var hole in holes)
            {
                var r = Local(hole);
                float k = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f));
                p.MoveTo(new Vector2(r.xMin + k, r.yMin));
                p.LineTo(new Vector2(r.xMax - k, r.yMin));
                p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + k), k);
                p.LineTo(new Vector2(r.xMax, r.yMax - k));
                p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - k, r.yMax), k);
                p.LineTo(new Vector2(r.xMin + k, r.yMax));
                p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - k), k);
                p.LineTo(new Vector2(r.xMin, r.yMin + k));
                p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + k, r.yMin), k);
                p.ClosePath();
            }
            p.Fill(FillRule.OddEven);
        }
    }
}
