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
    /// 「규칙 판정」 화면(결정 213 — 룰 설명서 큰 창 <see cref="RulebookScreen"/>과 같은 판 · 토큰): 왼쪽에 「전체」 + 묶음 다섯과 고른 묶음의 절, 오른쪽 위에 찾기 칸 + 출처 거르기 셋(켜고 끄기 — 처음에는 모두 켬) + 찾은 수,
    /// 그 아래 물음 · 답 목록(항목마다 출처 꼬리표 — 마우스 오버로 그 뜻). 거르기 = <see cref="Rulings.Filter"/>(물음 + 답 · 대소문자 · 공백 무시 · 띄어 쓴 낱말은 모두).
    /// 목록은 엔진 <see cref="ListView"/>(보이는 줄만 만든다 — 줄 높이는 글 길이대로)이고, 찾기 · 거르기 · 묶음 고르기는 목록 내용만 바꾼다(글 칸을 다시 만들지 않는다 — 포커스 · 한글 조합이 끊긴다).
    /// 화면 언어 = 게임 언어. 타이틀 「규칙 판정」 줄 · 게임 메뉴 [규칙 판정]이 연다(<see cref="GameFlow.OpenTitleRulings"/> · <see cref="GameFlow.OpenMenuRulings"/>). 보기만 하는 창이라 판의 상태를 받지 않는다.
    /// 요소 이름: <c>rulings</c>·<c>rulings-panel</c>·<c>rulings-close</c>·<c>rulings-tree</c>·<c>rulings-group-all</c>·<c>rulings-group-&lt;묶음&gt;</c>·<c>rulings-section-&lt;절 키&gt;</c>·<c>rulings-find</c>·
    /// <c>rulings-source-&lt;creator|rulebook|house&gt;</c>·<c>rulings-count</c>·<c>rulings-list</c>·<c>rulings-none</c>, 줄 안: <c>rulings-item</c>(클래스)·<c>rulings-item-where</c>·<c>-q</c>·<c>-a</c>·<c>-source</c>.
    /// </summary>
    public sealed class RulingsScreen : IUiScreen
    {
        /// <summary>찾기 칸의 글자 수 상한.</summary>
        public const int FindMax = 30;

        static readonly RulingSource[] Sources = { RulingSource.Creator, RulingSource.Rulebook, RulingSource.House };

        readonly Rulings table;
        readonly Action close;
        readonly Button[] toggles = new Button[Sources.Length];
        List<Ruling> shown;
        UiStage stage;
        VisualElement panel, tree, track, thumb;
        ScrollView treeScroll, listScroll;
        TextField field;
        ListView list;
        Label count, none;

        /// <param name="table">보일 표(한 언어).</param>
        /// <param name="close">[닫기]·[뒤로]·Esc가 하는 일.</param>
        /// <param name="inGame">게임 중에 열었나(닫기 글 「닫기」 — 타이틀에서는 「뒤로」).</param>
        public RulingsScreen(Rulings table, Action close, bool inGame)
        {
            this.table = table ?? throw new ArgumentNullException(nameof(table));
            this.close = close ?? throw new ArgumentNullException(nameof(close));
            InGame = inGame;
            shown = table.Filter(null);
        }

        public string Name => "rulings";

        public bool InGame { get; }

        /// <summary>찾는 말(빈 글 = 거르지 않는다).</summary>
        public string Query { get; private set; } = "";

        /// <summary>고른 묶음 · 절 키(null = 전체 · 그 묶음의 모든 절).</summary>
        public string Group { get; private set; }
        public string Section { get; private set; }

        /// <summary>켠 출처(<see cref="Rulings.Bit"/>의 합).</summary>
        public int SourceMask { get; private set; } = Rulings.AllSources;

        /// <summary>지금 목록(표 순서).</summary>
        public IReadOnlyList<Ruling> Shown => shown;

        public static string SourceId(RulingSource source) => source.ToString().ToLowerInvariant();

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            RulebookView.Add(page, new VisualElement(), null, "kit-scrim");
            panel = RulebookView.Add(page, Kit.Panel(stage.Skin), "rulings-panel", "sn-book-panel");

            var head = RulebookView.Add(panel, new VisualElement(), null, "sn-book-head");
            RulebookView.Add(head, Kit.Text(stage.T("screen.rulings.title"), Kit.FTitle), null, "kit-title");
            RulebookView.Add(head, new VisualElement(), null, "sn-book-gap");
            RulebookView.Add(head, Kit.Button(stage.T(InGame ? "screen.rulebook.close" : "screen.common.back"), KitButtonKind.Main, close), "rulings-close", null);

            var bodyRow = RulebookView.Add(panel, new VisualElement(), null, "sn-book-body");
            var side = RulebookView.Add(bodyRow, new VisualElement(), null, "sn-book-side");
            treeScroll = RulebookScreen.Scroll(side, "rulings-tree", out tree, out _);

            var read = RulebookView.Add(bodyRow, new VisualElement(), "rulings-read", "sn-book-read");
            var bar = RulebookView.Add(read, new VisualElement(), null, "sn-rule-bar");
            var findLabel = RulebookView.Add(bar, Kit.Text(stage.T("screen.rulings.search"), Kit.F2), null, "sn-book-find-label");
            stage.Tips.Hook(findLabel, null, stage.T("screen.rulings.search.tip"), TipSide.Below, reading: true);
            field = RoomUi.Field(bar, "rulings-find", Query, FindMax);
            field.AddToClassList("sn-rule-find");
            field.RegisterValueChangedCallback(e => Find(e.newValue));
            for (int i = 0; i < Sources.Length; i++)
            {
                var source = Sources[i];
                string key = "screen.rulings.source." + SourceId(source);
                toggles[i] = RulebookView.Add(bar, Kit.Button(stage.T(key), KitButtonKind.Normal, () => Toggle(source)), "rulings-source-" + SourceId(source), "sn-rule-src");
                stage.Tips.Hook(toggles[i], stage.T(key), stage.T(key + ".tip") + "\n" + stage.T("screen.rulings.filter.tip"), TipSide.Below, reading: true);
            }
            RulebookView.Add(bar, new VisualElement(), null, "sn-book-gap");
            count = RulebookView.Add(bar, Kit.Text("", Kit.F2), "rulings-count", "kit-muted");

            none = RulebookView.Add(read, Kit.Wrapped(stage.T("screen.rulings.none"), Kit.F2), "rulings-none", "sn-rule-none");
            none.AddToClassList("kit-muted");
            var wrap = RulebookView.Add(read, new VisualElement(), null, "sn-book-scroll-wrap");
            list = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                makeItem = MakeItem,
                bindItem = BindItem,
                itemsSource = shown,
            };
            RulebookView.Add(wrap, list, "rulings-list", "sn-rule-list");
            // 엔진 스크롤 막대 없이 휠 + 얇은 위치 막대(L1 — 룰 설명서와 같다)
            listScroll = list.Q<ScrollView>();
            listScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            listScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            track = RulebookView.Add(wrap, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-book-track");
            thumb = RulebookView.Add(track, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-book-thumb");
            listScroll.verticalScroller.valueChanged += v => Bar();
            listScroll.RegisterCallback<GeometryChangedEvent>(e => Bar());
            listScroll.contentContainer.RegisterCallback<GeometryChangedEvent>(e => Bar());

            PaintTree();
            PaintTop();
            return page;
        }

        public void Close() => stage = null;

        /// <summary>찾는다(빈 글 = 거르지 않는다) — 찾기 칸의 글도 바꾼다.</summary>
        public void Find(string query)
        {
            query ??= "";
            if (field != null && field.value != query) field.SetValueWithoutNotify(query);
            Query = query;
            Refill();
        }

        /// <summary>묶음 · 절을 고른다(둘 다 null = 전체, 절 null = 그 묶음 전부). 모르는 키면 false.</summary>
        public bool Pick(string group, string section = null)
        {
            if (group == null && section != null) return false;
            if (group != null)
            {
                RulingGroup g = null;
                foreach (var x in table.Groups) if (x.Key == group) g = x;
                if (g == null) return false;
                if (section != null)
                {
                    bool has = false;
                    foreach (var s in g.Sections) has |= s.Key == section;
                    if (!has) return false;
                }
            }
            Group = group;
            Section = section;
            if (stage != null) PaintTree();
            Refill();
            return true;
        }

        /// <summary>그 출처를 켜거나 끈다.</summary>
        public void Toggle(RulingSource source)
        {
            SourceMask ^= Rulings.Bit(source);
            Refill();
        }

        /// <summary>그 출처가 켜져 있나.</summary>
        public bool On(RulingSource source) => (SourceMask & Rulings.Bit(source)) != 0;

        void Refill()
        {
            shown = table.Filter(Query, Group, Section, SourceMask);
            if (stage == null) return;
            list.itemsSource = shown;
            list.Rebuild();
            listScroll.scrollOffset = Vector2.zero;
            PaintTop();
        }

        // 위 줄: 출처 단추의 켜짐 · 찾은 수 · 「없음」 줄
        void PaintTop()
        {
            for (int i = 0; i < Sources.Length; i++) toggles[i].EnableInClassList("sn-rule-src-off", !On(Sources[i]));
            count.text = stage.T("screen.rulings.count").Replace("{n}", shown.Count.ToString(CultureInfo.InvariantCulture));
            none.style.display = shown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            list.style.display = shown.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            Bar();
        }

        // 목차: 「전체」 → 묶음(고른 묶음만 절을 편다)
        void PaintTree()
        {
            tree.Clear();
            var inv = CultureInfo.InvariantCulture;
            var all = Row(stage.T("screen.rulings.all"), table.Items.Count.ToString(inv), "rulings-group-all", "sn-book-row-chapter", Group == null);
            stage.OnTap(all, SoundKeys.Select, e => Pick(null));
            foreach (var g in table.Groups)
            {
                string group = g.Key;
                bool open = Group == group;
                var row = Row((open ? RulebookView.Opened : RulebookView.Folded) + " " + g.Title, g.Count.ToString(inv), "rulings-group-" + group, "sn-book-row-chapter", open && Section == null);
                stage.OnTap(row, SoundKeys.Select, e => Pick(group));
                if (!open) continue;
                foreach (var s in g.Sections)
                {
                    string section = s.Key;
                    var srow = Row(s.Title, s.Count.ToString(inv), "rulings-section-" + section, "sn-book-row-section", Section == section);
                    stage.OnTap(srow, SoundKeys.Select, e => Pick(group, section));
                }
            }
        }

        VisualElement Row(string text, string n, string name, string cls, bool picked)
        {
            var row = RulebookView.Add(tree, new VisualElement(), name, "sn-book-row");
            row.AddToClassList(cls);
            row.EnableInClassList("sn-book-row-picked", picked);
            RulebookView.Add(row, Kit.Wrapped(text, Kit.F2), null, "sn-book-row-text");
            RulebookView.Add(row, Kit.Text(n, Kit.F2), null, "kit-muted");
            return row;
        }

        // 목록의 줄 하나(다시 쓰인다 — 글은 BindItem이 채운다): 위치 줄(묶음 · 절 — 절 하나만 볼 때는 숨김) · 물음 + 출처 꼬리표 · 답
        VisualElement MakeItem()
        {
            var item = new VisualElement();
            item.AddToClassList("rulings-item");
            item.AddToClassList("sn-rule-item");
            RulebookView.Add(item, Kit.Text("", Kit.F2), "rulings-item-where", "kit-muted");
            var top = RulebookView.Add(item, new VisualElement(), null, "sn-rule-item-top");
            var q = RulebookView.Add(top, Kit.Wrapped("", Kit.F2), "rulings-item-q", "sn-rule-q");
            q.AddToClassList("kit-title");
            q.enableRichText = false;
            RulebookView.Add(top, Kit.Badge("", KitBadgeKind.Plain, Kit.F2), "rulings-item-source", "sn-rule-badge");
            var a = RulebookView.Add(item, Kit.Wrapped("", Kit.F2), "rulings-item-a", "sn-rule-a");
            a.enableRichText = false;
            return item;
        }

        void BindItem(VisualElement item, int index)
        {
            if (stage == null || index < 0 || index >= shown.Count) return;
            var r = shown[index];
            var where = item.Q<Label>("rulings-item-where");
            where.text = r.GroupTitle + " · " + r.SectionTitle;
            where.style.display = Section == null ? DisplayStyle.Flex : DisplayStyle.None;
            item.Q<Label>("rulings-item-q").text = KoText.JoinWords(r.Q);
            item.Q<Label>("rulings-item-a").text = KoText.JoinWords(r.A);
            var badge = item.Q<Label>("rulings-item-source");
            string key = "screen.rulings.source." + SourceId(r.Source);
            badge.text = stage.T(key);
            badge.EnableInClassList("kit-badge-gold", r.Source == RulingSource.Creator);
            badge.EnableInClassList("kit-badge-silver", r.Source == RulingSource.Rulebook);
            badge.EnableInClassList("kit-badge-ok", r.Source == RulingSource.House);
            stage.Tips.Hook(badge, stage.T(key), stage.T(key + ".tip"), TipSide.Below, reading: true, clear: null);
        }

        // 위치 막대: 내용이 칸보다 길 때만 — 보이는 몫(높이) · 굴린 자리(위)를 퍼센트로(목록은 줄 높이를 어림하므로 막대도 어림이다)
        void Bar()
        {
            if (listScroll == null) return;
            float view = listScroll.contentViewport.layout.height, max = listScroll.verticalScroller.highValue;
            bool show = shown.Count > 0 && !float.IsNaN(view) && view > 0f && !float.IsNaN(max) && max > 1f;
            track.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float size = view / (view + max) * 100f, at = Mathf.Clamp(listScroll.verticalScroller.value, 0f, max);
            thumb.style.height = Length.Percent(size);
            thumb.style.top = Length.Percent((100f - size) * (at / max));
        }
    }
}
