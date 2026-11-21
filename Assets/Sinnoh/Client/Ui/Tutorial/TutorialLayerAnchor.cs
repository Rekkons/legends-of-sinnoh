using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 각본 튜토리얼 층의 앵커 찾기(07 §7 「앵커」 · T16): 단계의 앵커 글 하나 → 패널 사각형(왼쪽 위가 (0, 0)) 여럿.
    /// <list type="bullet">
    /// <item>요소 이름(<c>board-hud-act-search</c>): 문서에서 그 이름의 첫 요소 — 보이고(배치가 섰고 · display·visibility) 크기가 있어야 찾은 것이다.</item>
    /// <item><c>cell:n[,n…]</c>: 보드 칸 윗면 육각형을 카메라로 비춘 사각형(<see cref="GameFlow.CellRect"/>) — 칸마다 구멍 하나. 하나라도 못 비추면 못 찾은 것.</item>
    /// <item><c>ds:&lt;요소&gt;</c>: 배틀 화면 DS 층(256×192, <c>battle-ds</c>)의 자리(<see cref="Ds"/> — DS px × 정수배).</item>
    /// </list>
    /// 앵커 하나는 부분 여럿을 <c>+</c>로 잇는다(07 T130 — <c>setup-village-list+setup-village-confirm</c> · <c>board-craft-close+board-hud-act-search</c>): 부분마다 위 꼴 하나 = 누르기 한 번, 구멍은 지금 누를 한 부분에만(결정 99 ① · T180 — 층이 고른다 <c>TutorialLayer.Part</c>).
    /// 첫 부분은 단계가 뜰 때 보여야 하고(앵커 표), 뒤 부분은 그 단계 안의 앞 클릭이 열거나 드러내는 것이어도 된다(볼 줄 · 창을 닫은 뒤의 HUD 단추).
    /// 시각에 따라 움직이는 것(카메라 따라가기 · 배치)은 프레임마다 다시 찾는다.
    /// </summary>
    public static class TutorialLayerAnchor
    {
        public const string CellPrefix = "cell:", DsPrefix = "ds:";

        /// <summary>앵커의 부분을 잇는 글자(07 T130).</summary>
        public const char Join = '+';

        /// <summary>앵커 글 → 부분(<see cref="Join"/>으로 나눈 것, 빈 앵커는 빈 목록).</summary>
        public static string[] Parts(string anchor) => string.IsNullOrEmpty(anchor) ? new string[0] : anchor.Split(Join);

        /// <summary>
        /// 창 닫기 부분(07 T130 — 이름이 <c>…-close</c> · 배틀 결과 [확인] <c>battle-result-ok</c>): 앞 단계가 연 창이 이 단계의 앵커를 가릴 때 먼저 닫으라고 함께 가리킨다.
        /// [다음] 단계에서도 층이 이 단추의 클릭은 받는다.
        /// </summary>
        public static bool IsDismiss(string name) => name == "battle-result-ok" || (name != null && name.EndsWith("-close", StringComparison.Ordinal));

        /// <summary>
        /// 결과 창 단계(결정 104 ① · 07 T190): 앵커의 첫 부분이 배틀 결과 창 안(<c>battle-result…</c> · 보상 고르기 <c>battle-reward…</c>)인 단계 — 그 창을 말하는 단계라
        /// 결과 창이 [확인]을 기다리는 동안 뜬다. 그 밖의 단계는 결과 창이 닫힌 뒤에 뜬다.
        /// </summary>
        public static bool InResult(string anchor)
        {
            var parts = Parts(anchor);
            return parts.Length > 0 && (parts[0].StartsWith("battle-result", StringComparison.Ordinal) || parts[0].StartsWith("battle-reward", StringComparison.Ordinal));
        }

        /// <summary>
        /// DS 층 앵커(DS px — 256×192, 왼쪽 위 기준) [가정 T35]: 메시지 창 · 상대 도트(발판 고정점 위 96칸) · 내 도트 · 상대 체력 상자(숫자 칸까지) · 내 체력 상자.
        /// 그림의 실제 불투명 테두리가 아니라 그 자리의 상자다(구멍은 둥글게 넉넉히 판다).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, RectInt> Ds = new Dictionary<string, RectInt>
        {
            { "message", DsStage.MessageRect },
            { "foe", new RectInt(DsStage.EnemyFoot.x - 48, DsStage.EnemyFoot.y - 88, 96, 88) },
            { "me", new RectInt(DsStage.PlayerFoot.x - 48, DsStage.PlayerFoot.y - 96, 96, 96) },
            { "foe-box", new RectInt(0, DsStage.EnemyBoxAt.y, 128 + DsStage.EnemyBoxAt.x, 40 + DsStage.EnemyBoxGrow) },
            { "me-box", new RectInt(DsStage.PlayerBoxAt.x, DsStage.PlayerBoxAt.y, 128, 48) },
        };

        /// <summary>
        /// 앵커(부분 여럿 — <see cref="Join"/>)를 찾는다: 지금 보이는 부분마다 <paramref name="rects"/>에 사각형을 채우고, 요소 이름 부분이면 그 요소를 <paramref name="elements"/>에 넣는다
        /// (구멍 안 클릭 판정에 그 요소의 자식도 넣는다). 한 부분이라도 찾았으면 true. <paramref name="skip"/>(튜토리얼 층 자신) 안의 요소는 앵커로 치지 않는다.
        /// </summary>
        public static bool Find(GameFlow flow, string anchor, VisualElement skip, List<Rect> rects, List<VisualElement> elements)
        {
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            rects.Clear();
            elements.Clear();
            var part = new List<Rect>();
            foreach (string p in Parts(anchor))
            {
                if (!Find(flow, p, skip, part, out var el)) continue;
                rects.AddRange(part);
                if (el != null) elements.Add(el);
            }
            return rects.Count > 0;
        }

        /// <summary>
        /// 앵커 부분 하나를 찾는다: 찾았으면 <paramref name="rects"/>에 사각형을 채우고 true. 요소 이름이면 그 요소를 <paramref name="element"/>로 준다.
        /// <paramref name="skip"/>(튜토리얼 층 자신) 안의 요소는 앵커로 치지 않는다.
        /// </summary>
        public static bool Find(GameFlow flow, string anchor, VisualElement skip, List<Rect> rects, out VisualElement element)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            rects.Clear();
            element = null;
            var root = flow.Stage.Root;
            if (string.IsNullOrEmpty(anchor) || root == null) return false;
            if (anchor.StartsWith(CellPrefix, StringComparison.Ordinal))
            {
                foreach (string part in anchor.Substring(CellPrefix.Length).Split(','))
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int cell) || !flow.CellRect(cell, out var r))
                    {
                        rects.Clear();
                        return false;
                    }
                    rects.Add(r);
                }
                return rects.Count > 0;
            }
            if (anchor.StartsWith(DsPrefix, StringComparison.Ordinal))
            {
                if (!Ds.TryGetValue(anchor.Substring(DsPrefix.Length), out var at)) return false;
                var ds = root.Q("battle-ds");
                if (!Visible(ds, skip, out var wb)) return false;
                float k = wb.width / DsStage.Width;
                rects.Add(new Rect(wb.x + at.x * k, wb.y + at.y * k, at.width * k, at.height * k));
                return true;
            }
            // 같은 이름이 여럿이면(닫히는 중인 앞 창 · 감춘 칸) 보이는 첫 요소
            foreach (var el in root.Query(anchor).ToList())
            {
                if (!Visible(el, skip, out var bound)) continue;
                element = el;
                rects.Add(bound);
                return true;
            }
            return false;
        }

        /// <summary>요소가 보이는 자리에 서 있나(패널에 붙었고 · 배치가 섰고 · 크기가 있고 · 그 요소와 위가 감춰지지 않았고 투명하지 않다 — 다 흐려진 막 카드처럼 남아 있는 요소는 못 찾은 것).</summary>
        public static bool Visible(VisualElement el, VisualElement skip, out Rect bound)
        {
            bound = default;
            if (el == null || el.panel == null || (skip != null && (el == skip || skip.Contains(el)))) return false;
            for (var e = el; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden || e.resolvedStyle.opacity < 0.01f) return false;
            bound = el.worldBound;
            return !float.IsNaN(bound.width) && !float.IsNaN(bound.height) && bound.width >= 1f && bound.height >= 1f;
        }
    }
}
