using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S43 룰 설명서 게임 중 옆 판(결정 111 ① C안 — 목업 <c>Logs/Review/m9/mockup/*/S43-C-side.png</c>): 보드 오른쪽(위 띠와 아래 판 사이)의 판 — 창이 아니라서 보드 입력을 막지 않고 화면 대기열도 세우지 않는다.
    /// 머리(제목 · [크게 보기] · [닫기]) · 찾기 칸 · 「지금 칸」 칩(내 칸의 고정물 · 토큰 · 맨 위 창 → 절 키, <see cref="RulebookChips"/> [가정 plan §5 #370]) · 접는 절 줄(고른 절의 장 — 고른 절만 펼쳐 블록을 그린다).
    /// 칩이 바뀌면(칸을 옮기거나 창이 열림) 첫 칩의 절을 펼친다. 찾는 말이 있으면 접는 줄 = 찾은 절(「n곳」). [크게 보기] = 큰 창(B안) — 펼친 절에서.
    /// 판은 보드 HUD 요소의 마지막 자식으로 붙는다(HUD 위 · 창 아래 — 창이 열리면 창이 덮는다). HUD가 다시 지어지면 다음 프레임에 다시 붙고(<see cref="Sync"/> — <see cref="GameFlow.Ticked"/>), 보드를 떠나면 HUD와 함께 사라진다.
    /// 위 띠의 [설명서](<c>board-hud-book</c>)가 열고 닫는다. 요소 이름: <c>rulebook-side</c>·<c>rulebook-side-big</c>·<c>rulebook-side-close</c>·<c>rulebook-side-find</c>·<c>rulebook-chip-&lt;절 키&gt;</c>·
    /// <c>rulebook-fold-&lt;절 키&gt;</c>·<c>rulebook-side-open</c>(펼친 절의 블록 칸) (+ <see cref="RulebookView"/>의 이름).
    /// </summary>
    public sealed class RulebookSide
    {
        readonly GameFlow flow;
        VisualElement panel, chipRow, folds;
        ScrollView scroll;
        TextField field;
        UiStage built;
        RulebookView view;
        Rulebook book;
        string chipSig;

        public RulebookSide(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            // 웹 손님판: 설명서 그림 번들이 왔다 — 펼친 절의 자리표시를 그림으로
            GenAssets.Arrived += bundle => { if (bundle == GenAssets.RulebookBundle && IsOpen && view != null && view.Waiting > 0) Paint(); };
        }

        /// <summary>열려 있나(보드에 있을 때 보인다).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>판 요소(열린 적이 없으면 null).</summary>
        public VisualElement Element => panel;

        /// <summary>지금 칩(절 키)과 펼친 절 키.</summary>
        public IReadOnlyList<string> Chips { get; private set; } = Array.Empty<string>();
        public string Picked { get; private set; }

        /// <summary>찾는 말과 그 결과.</summary>
        public string Query { get; private set; } = "";
        public IReadOnlyList<RulebookHit> Hits { get; private set; } = Array.Empty<RulebookHit>();

        /// <summary>절을 그리는 부품(펼친 절의 「고침」 배지 · 링크).</summary>
        public RulebookView View => view;

        public void Open()
        {
            IsOpen = true;
            chipSig = null;
            Sync();
        }

        public void Close()
        {
            IsOpen = false;
            panel?.RemoveFromHierarchy();
        }

        /// <summary>프레임마다(<see cref="GameFlow.Ticked"/>): 보드 HUD에 붙어 있게 하고, 칩이 바뀌었으면 다시 그린다.</summary>
        public void Sync()
        {
            var stage = flow.Stage;
            var hud = IsOpen && flow.OnBoard && stage.Hud != null && stage.Root != null ? stage.Root.Q(stage.Hud.Name) : null;
            if (hud == null)
            {
                panel?.RemoveFromHierarchy();
                return;
            }
            if (panel == null || built != stage || book != flow.Book(flow.BookLang)) Build(stage);
            if (panel.parent != hud || hud.IndexOf(panel) != hud.childCount - 1) hud.Add(panel);
            var chips = RulebookChips.For(flow.BookSituations());
            chips.RemoveAll(k => book.Find(k) == null);
            string sig = string.Join(",", chips);
            if (sig == chipSig) return;
            chipSig = sig;
            Chips = chips;
            if (chips.Count > 0) Picked = chips[0];
            Paint();
        }

        void Build(UiStage stage)
        {
            built = stage;
            book = flow.Book(flow.BookLang);
            view = new RulebookView(stage, flow.Ctx, flow.BookLang, key => Pick(key));
            panel?.RemoveFromHierarchy();
            panel = Kit.Panel(stage.Skin);
            panel.name = "rulebook-side";
            panel.AddToClassList("sn-book-side-panel");
            view.WatchColors(panel, Paint);
            var head = RulebookView.Add(panel, new VisualElement(), null, "sn-book-head");
            RulebookView.Add(head, Kit.Text(stage.T("screen.rulebook.title"), Kit.FTitle), null, "kit-title");
            RulebookView.Add(head, new VisualElement(), null, "sn-book-gap");
            var big = RulebookView.Add(head, Kit.Button(stage.T("screen.rulebook.big"), KitButtonKind.Normal, () => Big()), "rulebook-side-big", null);
            stage.Tips.Hook(big, null, stage.T("screen.rulebook.big.tip"), TipSide.Below, reading: true);
            RulebookView.Add(head, Kit.Button(stage.T("screen.rulebook.close"), KitButtonKind.Normal, () => flow.ToggleBookSide()), "rulebook-side-close", null);
            var find = RulebookView.Add(panel, new VisualElement(), null, "sn-book-find");
            RulebookView.Add(find, Kit.Text(stage.T("screen.rulebook.search"), Kit.F2), null, "sn-book-find-label");
            field = RoomUi.Field(find, "rulebook-side-find", Query, RulebookScreen.FindMax);
            field.AddToClassList("sn-book-find-field");
            field.RegisterValueChangedCallback(e => Find(e.newValue));
            chipRow = RulebookView.Add(panel, new VisualElement(), null, "sn-book-chips");
            var wrap = RulebookView.Add(panel, new VisualElement(), null, "sn-book-scroll-wrap");
            scroll = RulebookView.Add(wrap, new ScrollView(ScrollViewMode.Vertical), null, "sn-book-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            folds = scroll.contentContainer;
            if (!string.IsNullOrEmpty(Query)) Hits = book.Search(Query, s => RulebookView.CardTexts(stage, s));
        }

        /// <summary>그 절을 펼친다(칩 · 접는 줄 · 링크). 펼친 절을 다시 누르면 접는다. 모르는 키면 false.</summary>
        public bool Pick(string key)
        {
            if (book == null || book.Find(key) == null) return false;
            Picked = Picked == key && folds != null && folds.Q("rulebook-side-open") != null ? null : key;
            Paint();
            return true;
        }

        /// <summary>찾는다(빈 글 = 지금 칸의 장) — 찾기 칸의 글도 바꾼다.</summary>
        public void Find(string query)
        {
            Query = query ?? "";
            if (field != null && field.value != Query) field.SetValueWithoutNotify(Query);
            var stage = flow.Stage;
            Hits = book != null ? book.Search(Query, s => RulebookView.CardTexts(stage, s)) : Array.Empty<RulebookHit>();
            bool has = false;
            foreach (var h in Hits) has |= h.Section.Key == Picked;
            if (Hits.Count > 0 && !has) Picked = Hits[0].Section.Key;
            Paint();
        }

        /// <summary>[크게 보기]: 큰 창(B안)을 펼친 절에서 연다.</summary>
        public bool Big() => flow.OpenGameRulebook(Picked);

        void Paint()
        {
            if (panel == null || built == null) return;
            var stage = built;
            var inv = CultureInfo.InvariantCulture;
            chipRow.Clear();
            var chipsLabel = RulebookView.Add(chipRow, Kit.Text(stage.T("screen.rulebook.chips"), Kit.F2), null, "sn-book-chips-label");
            chipsLabel.AddToClassList("kit-muted");
            stage.Tips.Hook(chipsLabel, null, stage.T("screen.rulebook.chips.tip"), TipSide.Below, reading: true);
            foreach (string key in Chips)
            {
                var s = book.Find(key);
                var chip = RulebookView.Add(chipRow, new VisualElement(), "rulebook-chip-" + key, "sn-opt");
                chip.AddToClassList("sn-book-chip");
                chip.EnableInClassList("sn-opt-picked", key == Picked);
                chip.Add(Kit.Text(s.Title, Kit.F2));
                string k = key;
                stage.OnTap(chip, SoundKeys.Select, e => Pick(k));
            }
            folds.Clear();
            view.Query = Query;
            bool searching = Query.Trim().Length > 0;
            var rows = new List<(RulebookSection Section, int Count)>();
            if (searching) foreach (var h in Hits) rows.Add((h.Section, h.Count));
            else
            {
                var at = Picked != null ? book.Find(Picked) : null;
                string chapter = at != null ? at.Chapter : Chips.Count > 0 ? book.Find(Chips[0]).Chapter : book.Sections[0].Chapter;
                foreach (var s in book.Sections) if (s.Chapter == chapter) rows.Add((s, 0));
            }
            if (searching && rows.Count == 0) RulebookView.Add(folds, Kit.Text(stage.T("screen.rulebook.search.none"), Kit.F2), null, "kit-muted");
            foreach (var (s, count) in rows)
            {
                bool isOpen = s.Key == Picked;
                var row = RulebookView.Add(folds, new VisualElement(), "rulebook-fold-" + s.Key, "sn-book-fold");
                row.EnableInClassList("sn-book-row-picked", isOpen);
                string tail = isOpen ? RulebookView.Where(stage, s) : s.ChapterTitle;
                RulebookView.Add(row, Kit.Wrapped((isOpen ? RulebookView.Opened : RulebookView.Folded) + " " + s.Title, Kit.F2), null, "sn-book-fold-name");
                RulebookView.Add(row, Kit.Text(count > 0 ? stage.T("screen.rulebook.hit").Replace("{n}", count.ToString(inv)) : tail, Kit.F2), null, "sn-book-fold-tail").AddToClassList("kit-muted");
                string key = s.Key;
                stage.OnTap(row, SoundKeys.Select, e => Pick(key));
                if (!isOpen) continue;
                var box = RulebookView.Add(folds, new VisualElement(), "rulebook-side-open", "sn-book-fold-open");
                view.Blocks(box, s);
            }
        }
    }
}
