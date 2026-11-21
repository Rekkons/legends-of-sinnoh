using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S43 룰 설명서 큰 창(결정 100 · 111 ① B안 — 목업 <c>Logs/Review/m9/mockup/*/S43-B-*.png</c>): 화면을 덮는 판 — 왼쪽 위 찾기 칸 + 목차 나무(기본 룰북의 장 · 확장 장, 접기 — 보는 절의 장은 펼침),
    /// 오른쪽에 절 하나씩(위치 줄 「장 · 룰북 n쪽」 · 제목 · 블록 — <see cref="RulebookView"/>) + 아래 [◀ 이전 절] 「장 i/n」 [다음 절 ▶].
    /// 찾기(plan §5 #303): 글자를 넣으면 목차 자리에 결과(「「말」 n곳 · m절」 + 절마다 「n곳」과 첫 맞은 곳 조각) — 결과를 누르면 그 절, 본문에서 찾은 말을 칠한다. 칸을 비우면 목차로.
    /// 설명서 언어 = 게임 언어(#304 — 화면 문자열 표의 언어, 언어를 바꾸면 다시 열 때 그 언어로). 타이틀 「룰 설명서」 줄 · 게임 메뉴 [룰 설명서] · 게임 중 옆 판 [크게 보기]가 연다(<see cref="GameFlow.OpenTitleRulebook"/> 등).
    /// 본문 · 목차는 휠로 굴린다 — 엔진 기본 스크롤 막대를 쓰지 않고(L1, 04 §9 · S21과 같다) 얇은 위치 막대만 그린다. 보기만 하는 창이라 판의 상태를 받지 않는다.
    /// 요소 이름: <c>rulebook</c>·<c>rulebook-panel</c>·<c>rulebook-close</c>·<c>rulebook-find</c>·<c>rulebook-tree</c>·<c>rulebook-chapter-&lt;장&gt;</c>·<c>rulebook-section-&lt;절 키&gt;</c>·
    /// <c>rulebook-count</c>·<c>rulebook-hit-&lt;절 키&gt;</c>·<c>rulebook-read</c>·<c>rulebook-crumb</c>·<c>rulebook-heading</c>·<c>rulebook-body</c>·<c>rulebook-prev</c>·<c>rulebook-pos</c>·<c>rulebook-next</c> (+ <see cref="RulebookView"/>의 이름).
    /// </summary>
    public sealed class RulebookScreen : IUiScreen
    {
        /// <summary>찾기 칸의 글자 수 상한.</summary>
        public const int FindMax = 20;

        readonly Rulebook book;
        readonly Ctx ctx;
        readonly Action close;
        readonly Action<string> remember;
        readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);
        UiStage stage;
        RulebookView view;
        VisualElement side, tree, read, body, thumb;
        ScrollView treeScroll, bodyScroll;
        TextField field;
        Label crumb, heading, pos;
        Button prev, next;

        /// <param name="book">보일 설명서(한 언어).</param>
        /// <param name="lang">설명서 언어(그림의 언어 꼬리).</param>
        /// <param name="start">처음 볼 절 키(null · 모르는 키 = 첫 절).</param>
        /// <param name="close">[닫기]·[뒤로]·Esc가 하는 일.</param>
        /// <param name="inGame">게임 중에 열었나(닫기 글 「닫기」 — 타이틀에서는 「뒤로」).</param>
        /// <param name="remember">절을 옮길 때마다 그 키를 알린다(다시 열 때 그 절 — 마지막 쪽 기억). null이면 알리지 않는다.</param>
        public RulebookScreen(Rulebook book, Ctx ctx, string lang, string start, Action close, bool inGame, Action<string> remember = null)
        {
            this.book = book ?? throw new ArgumentNullException(nameof(book));
            this.ctx = ctx;
            this.close = close ?? throw new ArgumentNullException(nameof(close));
            this.remember = remember;
            Lang = lang;
            InGame = inGame;
            Current = book.Find(start) ?? book.Sections[0];
        }

        public string Name => "rulebook";

        public bool InGame { get; }

        public string Lang { get; }

        /// <summary>보고 있는 절.</summary>
        public RulebookSection Current { get; private set; }

        /// <summary>찾는 말(빈 글 = 목차)과 그 결과.</summary>
        public string Query { get; private set; } = "";
        public IReadOnlyList<RulebookHit> Hits { get; private set; } = Array.Empty<RulebookHit>();

        /// <summary>펼친 장.</summary>
        public IReadOnlyCollection<string> Open => open;

        /// <summary>절을 그리는 부품(지금 절의 「고침」 배지 · 링크 · 그림 칸).</summary>
        public RulebookView View => view;

        /// <summary>지금 절의 표 순서 색인 · 그 장 안의 순서(1부터)와 장의 절 수.</summary>
        public int Index => IndexOf(Current);
        public (int At, int Of) InChapter
        {
            get
            {
                int at = 0, of = 0;
                foreach (var s in book.Sections)
                    if (s.Chapter == Current.Chapter)
                    {
                        of++;
                        if (s == Current) at = of;
                    }
                return (at, of);
            }
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            view = new RulebookView(stage, ctx, Lang, key => Show(key));
            var page = new VisualElement();
            RulebookView.Add(page, new VisualElement(), null, "kit-scrim");
            var panel = RulebookView.Add(page, Kit.Panel(stage.Skin), "rulebook-panel", "sn-book-panel");
            view.WatchColors(panel, Paint);

            var head = RulebookView.Add(panel, new VisualElement(), null, "sn-book-head");
            RulebookView.Add(head, Kit.Text(stage.T("screen.rulebook.title"), Kit.FTitle), null, "kit-title");
            RulebookView.Add(head, new VisualElement(), null, "sn-book-gap");
            RulebookView.Add(head, Kit.Button(stage.T(InGame ? "screen.rulebook.close" : "screen.common.back"), KitButtonKind.Main, close), "rulebook-close", null);

            var bodyRow = RulebookView.Add(panel, new VisualElement(), null, "sn-book-body");
            side = RulebookView.Add(bodyRow, new VisualElement(), null, "sn-book-side");
            var find = RulebookView.Add(side, new VisualElement(), null, "sn-book-find");
            var findLabel = RulebookView.Add(find, Kit.Text(stage.T("screen.rulebook.search"), Kit.F2), null, "sn-book-find-label");
            stage.Tips.Hook(findLabel, null, stage.T("screen.rulebook.search.tip"), TipSide.Below, reading: true);
            field = RoomUi.Field(find, "rulebook-find", Query, FindMax);
            field.AddToClassList("sn-book-find-field");
            field.RegisterValueChangedCallback(e => Find(e.newValue));
            treeScroll = Scroll(side, "rulebook-tree", out tree, out _);

            read = RulebookView.Add(bodyRow, new VisualElement(), "rulebook-read", "sn-book-read");
            crumb = RulebookView.Add(read, Kit.Text("", Kit.F2), "rulebook-crumb", "kit-muted");
            heading = RulebookView.Add(read, Kit.Text("", Kit.F3), "rulebook-heading", "sn-book-h");
            heading.AddToClassList("kit-title");
            bodyScroll = Scroll(read, "rulebook-body", out body, out thumb);
            var foot = RulebookView.Add(read, new VisualElement(), null, "sn-book-foot");
            prev = RulebookView.Add(foot, Kit.Button("", KitButtonKind.Normal, () => Step(-1)), "rulebook-prev", null);
            pos = RulebookView.Add(foot, Kit.Text("", Kit.F2), "rulebook-pos", "sn-book-pos");
            next = RulebookView.Add(foot, Kit.Button("", KitButtonKind.Normal, () => Step(1)), "rulebook-next", null);

            open.Add(Current.Chapter);
            GenAssets.Arrived += OnArrived;
            Paint();
            return page;
        }

        public void Close()
        {
            GenAssets.Arrived -= OnArrived;
            stage = null;
        }

        // 웹 손님판: 설명서 그림 번들이 왔다 — 자리표시를 그림으로
        void OnArrived(string bundle)
        {
            if (stage != null && bundle == GenAssets.RulebookBundle && view.Waiting > 0) PaintRead(false);
        }

        /// <summary>
        /// 휠로 굴리는 칸(엔진 스크롤 막대를 숨긴다 — L1) + 오른쪽 얇은 위치 막대. 안쪽 칸을 돌려준다.
        /// </summary>
        internal static ScrollView Scroll(VisualElement parent, string name, out VisualElement inner, out VisualElement bar)
        {
            var wrap = RulebookView.Add(parent, new VisualElement(), null, "sn-book-scroll-wrap");
            var sv = RulebookView.Add(wrap, new ScrollView(ScrollViewMode.Vertical), name, "sn-book-scroll");
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var track = RulebookView.Add(wrap, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-book-track");
            var t = RulebookView.Add(track, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-book-thumb");
            bar = t;
            void Sync() => Bar(sv, track, t);
            sv.verticalScroller.valueChanged += v => Sync();
            sv.RegisterCallback<GeometryChangedEvent>(e => Sync());
            sv.contentContainer.RegisterCallback<GeometryChangedEvent>(e => Sync());
            inner = sv.contentContainer;
            return sv;
        }

        /// <summary>위치 막대: 내용이 칸보다 길 때만 — 보이는 몫(높이) · 굴린 자리(위)를 퍼센트로.</summary>
        static void Bar(ScrollView sv, VisualElement track, VisualElement thumb)
        {
            float view = sv.contentViewport.layout.height, all = sv.contentContainer.layout.height;
            bool show = !float.IsNaN(view) && !float.IsNaN(all) && all > view + 1f;
            track.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float max = all - view, at = Mathf.Clamp(sv.scrollOffset.y, 0f, max);
            float size = view / all * 100f;
            thumb.style.height = Length.Percent(size);
            thumb.style.top = Length.Percent((100f - size) * (max > 0 ? at / max : 0f));
        }

        /// <summary>그 절로 간다(목차 · 결과 · 링크 · 이전/다음 — 그 장을 펼친다). 모르는 키면 false.</summary>
        public bool Show(string key)
        {
            var s = book.Find(key);
            if (s == null) return false;
            Current = s;
            open.Add(s.Chapter);
            remember?.Invoke(s.Key);
            if (stage != null) Paint();
            return true;
        }

        /// <summary>표 순서로 앞(−1) · 뒤(+1) 절. 끝이면 false.</summary>
        public bool Step(int by)
        {
            int to = Index + by;
            return to >= 0 && to < book.Sections.Count && Show(book.Sections[to].Key);
        }

        /// <summary>장을 접거나 편다.</summary>
        public void Toggle(string chapter)
        {
            if (!open.Remove(chapter)) open.Add(chapter);
            if (stage != null) PaintSide();
        }

        /// <summary>찾는다(빈 글 = 목차로) — 찾기 칸의 글도 바꾼다.</summary>
        public void Find(string query)
        {
            query ??= "";
            if (field != null && field.value != query) field.SetValueWithoutNotify(query);
            Query = query;
            var st = stage;
            Hits = book.Search(query, st != null ? s => RulebookView.CardTexts(st, s) : (Func<RulebookSection, IEnumerable<string>>)null);
            if (stage != null) Paint();
        }

        int IndexOf(RulebookSection s)
        {
            for (int i = 0; i < book.Sections.Count; i++) if (book.Sections[i] == s) return i;
            return -1;
        }

        void Paint()
        {
            if (stage == null) return;
            PaintSide();
            PaintRead(true);
        }

        void PaintSide()
        {
            tree.Clear();
            if (Query.Trim().Length > 0) PaintHits();
            else PaintTree();
        }

        // 목차 나무: 「기본 룰북」 머리 + 기본 룰북의 장(접기) → 확장 장(맨 위 단계) · 펼친 장의 절
        void PaintTree()
        {
            bool group = false;
            foreach (string chapter in book.Chapters())
            {
                RulebookSection first = null;
                foreach (var s in book.Sections) if (s.Chapter == chapter) { first = s; break; }
                bool baseBook = first.Source == Rulebook.BaseSource;
                if (baseBook && !group)
                {
                    RulebookView.Add(tree, Kit.Text(stage.T("screen.rulebook.book"), Kit.F2), null, "sn-book-group").AddToClassList("kit-title");
                    group = true;
                }
                bool isOpen = open.Contains(chapter);
                string ch = chapter;
                var row = Row((isOpen ? RulebookView.Opened : RulebookView.Folded) + " " + first.ChapterTitle, "rulebook-chapter-" + chapter, baseBook ? "sn-book-row-chapter" : "sn-book-row-book", false, false);
                stage.OnTap(row, SoundKeys.Select, e => Toggle(ch));
                if (!isOpen) continue;
                foreach (var s in book.Sections)
                {
                    if (s.Chapter != chapter) continue;
                    string key = s.Key;
                    var srow = Row(s.Title, "rulebook-section-" + s.Key, baseBook ? "sn-book-row-section" : "sn-book-row-chapter", s == Current, s != Current);
                    stage.OnTap(srow, SoundKeys.Select, e => Show(key));
                }
            }
        }

        VisualElement Row(string text, string name, string cls, bool picked, bool muted)
        {
            var row = RulebookView.Add(tree, new VisualElement(), name, "sn-book-row");
            row.AddToClassList(cls);
            row.EnableInClassList("sn-book-row-picked", picked);
            var label = RulebookView.Add(row, Kit.Wrapped(text, Kit.F2), null, "sn-book-row-text");
            if (muted) label.AddToClassList("kit-muted");
            return row;
        }

        // 찾기 결과: 머리 줄 + 절마다(장 · 절 「n곳」 + 조각)
        void PaintHits()
        {
            var inv = CultureInfo.InvariantCulture;
            int total = 0;
            foreach (var h in Hits) total += h.Count;
            string q = Query.Trim();
            string headText = Hits.Count == 0 ? stage.T("screen.rulebook.search.none")
                : stage.T("screen.rulebook.search.count").Replace("{q}", q).Replace("{n}", total.ToString(inv)).Replace("{m}", Hits.Count.ToString(inv));
            RulebookView.Add(tree, Kit.Wrapped(headText, Kit.F2), "rulebook-count", "sn-book-group").AddToClassList("kit-title");
            foreach (var h in Hits)
            {
                string key = h.Section.Key;
                var row = RulebookView.Add(tree, new VisualElement(), "rulebook-hit-" + key, "sn-book-hit");
                row.EnableInClassList("sn-book-row-picked", h.Section == Current);
                var top = RulebookView.Add(row, new VisualElement(), null, "sn-book-hit-top");
                RulebookView.Add(top, Kit.Wrapped(h.Section.ChapterTitle + " · " + h.Section.Title, Kit.F2), null, "sn-book-hit-name");
                RulebookView.Add(top, Kit.Text(stage.T("screen.rulebook.hit").Replace("{n}", h.Count.ToString(inv)), Kit.F2), null, "kit-muted");
                if (h.Snippet != null) RulebookView.Add(row, Kit.Wrapped(view.Rich(h.Snippet), Kit.F2), null, "kit-muted");
                stage.OnTap(row, SoundKeys.Select, e => Show(key));
            }
        }

        void PaintRead(bool top)
        {
            var inv = CultureInfo.InvariantCulture;
            crumb.text = RulebookView.Where(stage, Current);
            heading.text = Current.Title;
            view.Query = Query;
            body.Clear();
            view.Blocks(body, Current);
            if (top) bodyScroll.scrollOffset = Vector2.zero;
            int i = Index;
            prev.SetEnabled(i > 0);
            next.SetEnabled(i < book.Sections.Count - 1);
            prev.text = i > 0 ? RulebookView.Prev + " " + book.Sections[i - 1].Title : RulebookView.Prev;
            next.text = i < book.Sections.Count - 1 ? book.Sections[i + 1].Title + " " + RulebookView.Next : RulebookView.Next;
            var (at, of) = InChapter;
            pos.text = Current.ChapterTitle + " " + at.ToString(inv) + "/" + of.ToString(inv);
        }
    }
}
