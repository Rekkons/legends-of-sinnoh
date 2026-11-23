using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 206 1단계 「카드 쌓기 부품」(<see cref="CardStack"/>): 견본 판 <see cref="SoloTrail.PrepareCards"/>(목업 r10-stats의 다섯 + 갤럭시단 조무래기)의 카드 여섯을 한 줄에 —
    /// 부품만 보는 시트라 화면을 덮는 판 위에 그린다(화면에 붙이는 것은 다음 단계). <c>card-sheet</c> = 시트 · <c>card-sheet-tip</c> = 토대부기 첫 띠 마우스 오버(깨물어부수기 효과 글).
    /// 영어는 <c>-uiLang en</c>(en 카드). 단정 <see cref="CardSheetCheck"/>: 카드 그림 · 띠 수 · 토큰 · 상자 값 · 꼬리표 · 기절 · 응징 · 카드가 판 안. 그림 <c>Logs/Review/card/impl1/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] CardStackScreens =
        {
            Screen("card-sheet", s => CardSheet(s, false)),
            Screen("card-sheet-tip", s => CardSheet(s, true)),
        };

        // 견본 여섯의 기대값: 띠 수 · 지닌물건 토큰 수 · ♥ 상자 글(없으면 null) · ♥ 꼬리표 · ≫ 상자 글 · ≫ 꼬리표
        static readonly (int strips, int items, string hp, string hpTag, string speed, string speedTag)[] CardSheetWant =
        {
            (2, 2, "4/10", null, null, null),     // 토대부기: 피해 6 · 독 · 기술 둘 · 구애머리띠 + 화산의 돌
            (2, 1, null, null, "8", "−3"),        // 초염몽: 마비(스피드 11 → 8) · 화염방사 + 균열 카드
            (1, 1, null, null, null, null),       // 디아루가: 전설 전술 판 한 장(알파 스트라이크 + 비스트 부스트)
            (2, 1, "10/12", "+4", null, null),    // 렌트라: 우두머리 토큰(+4) · 피해 2 · 깜짝베기 + 비스트 부스트만
            (0, 0, "0/7", null, null, null),      // 찌르호크: 기절
            (1, 0, null, null, null, null),       // 동미러(갤럭시단): 갤럭시단 전술 판 · 응징 2
        };

        static void CardSheet(UiShot s, bool tip)
        {
            var ctx = s.Board.Ctx;
            int[] ids = null;
            var flow = Seats(s, 1, st => ids = SoloTrail.PrepareCards(ctx, st));
            var stage = flow.Stage;
            var layer = Kit.Panel(stage.Skin);
            layer.name = "card-sheet";
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            layer.style.flexDirection = FlexDirection.Row;
            layer.style.alignItems = Align.FlexStart;
            stage.SetOver(layer);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(2);
            float gap = Mathf.Round(stage.Root.layout.width / 160f), width = Mathf.Floor((stage.Root.layout.width - gap * 8f) / ids.Length);
            var view = flow.Client.View;
            var stacks = new List<CardStack>();
            for (int i = 0; i < ids.Length; i++)
            {
                var mon = CardMon.Of(ctx, view, ids[i], retribution: i == ids.Length - 1 ? 2 : (int?)null);
                var stack = CardStack.Build(stage, ctx, mon, width);
                stack.Root.style.marginLeft = gap;
                stack.HookTips(TipSide.Right);
                layer.Add(stack.Root);
                stacks.Add(stack);
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            CardSheetCheck(stage, layer, stacks);
            if (!tip) return;
            CardPart first = null;
            foreach (var p in stacks[0].Parts) if (first == null && p.Kind == CardPartKind.Move) first = p;
            if (first == null) throw new InvalidOperationException("토대부기 기술 띠 자리 없음");
            stage.Tips.Show(first.Element);
            s.Panel.Tick(3);
            var box = stage.Tips.Element;
            if (box.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("띠 툴팁이 뜨지 않았다");
            var labels = box.Query<Label>().ToList();
            string want = stage.T("move.crunch");
            if (labels.Count < 2 || labels[0].text.Replace("⁠", "") != want) throw new InvalidOperationException("툴팁 머리가 " + want + "가 아니다(" + (labels.Count > 0 ? labels[0].text : "-") + ")");
        }

        static void CardSheetCheck(UiStage stage, VisualElement layer, List<CardStack> stacks)
        {
            var area = layer.worldBound;
            for (int i = 0; i < stacks.Count; i++)
            {
                var st = stacks[i];
                var want = CardSheetWant[i];
                string at = "카드 " + (i + 1) + "(" + st.FaceKey + ")";
                if (!st.Art) throw new InvalidOperationException(at + ": 카드 그림이 없다(도트 카드로 대신 — Gen Cards/Faces · cardfaces 묶음)");
                if (st.StripCount != want.strips) throw new InvalidOperationException(at + ": 띠 " + st.StripCount + " ≠ " + want.strips);
                var root = st.Root;
                int items = 0;
                for (int n = 1; root.Q("card-item-" + n.ToString(CultureInfo.InvariantCulture)) != null; n++) items++;
                if (items != want.items) throw new InvalidOperationException(at + ": 지닌물건 토큰 " + items + " ≠ " + want.items);
                Same(at + " ♥", Shown(root, "card-hp"), want.hp);
                Same(at + " ♥ 꼬리표", Shown(root, "card-hp-tag"), want.hpTag);
                Same(at + " ≫", Shown(root, "card-speed"), want.speed);
                Same(at + " ≫ 꼬리표", Shown(root, "card-speed-tag"), want.speedTag);
                bool fainted = root.ClassListContains("sn-card-out");
                if (fainted != (want.hp == "0/7") || fainted != (root.Q("card-fainted") != null)) throw new InvalidOperationException(at + ": 기절 표시가 어긋남");
                var box = root.worldBound;
                if (box.width <= 0f || box.xMin < area.xMin - 0.5f || box.xMax > area.xMax + 0.5f || box.yMax > area.yMax + 0.5f) throw new InvalidOperationException(at + ": 판 밖 " + box + " · 판 " + area);
                for (int n = 1; n <= st.StripCount; n++)
                    if (root.Q("card-strip-" + n.ToString(CultureInfo.InvariantCulture)) == null) throw new InvalidOperationException(at + ": card-strip-" + n + " 없음");
            }
            Same("디아루가 타입 그림", stacks[2].Root.Q("card-alpha-type-1") != null ? "있음" : null, "있음");
            Same("렌트라 표지", stacks[3].Root.Q("card-emblem") != null ? "있음" : null, "있음");
            Same("동미러 응징", Shown(stacks[5].Root, "card-retribution"), "2");
        }

        static string Shown(VisualElement root, string name) => root.Q(name)?.userData as string;

        static void Same(string what, string got, string want)
        {
            if (got != want) throw new InvalidOperationException(what + ": " + (got ?? "없음") + " ≠ " + (want ?? "없음"));
        }
    }
}
