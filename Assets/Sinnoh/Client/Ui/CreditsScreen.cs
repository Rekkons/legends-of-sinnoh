using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S03 크레딧(04 §1 타이틀 → 크레딧, M9 「크레딧」 — 결정 93 · 94 ② 원작 출처 표기): 화면을 덮는 판 — 위에 라이선스 · 배포 문구 자리(결정 대기 15 — 정해질 때까지 자리 문구만),
    /// 왼쪽에 탭(CREDITS.md의 절 순서 — 원작 · 번역 · 포켓몬 · 그림 · 소리 · 글꼴 · 프로그램), 오른쪽에 그 탭의 항목(한 사람 = 한 칸: 이름 · 역할 한 줄(게임 언어) · 작가 이름 목록).
    /// 표 = <see cref="CreditsTable"/>(`Data/Json/credits.json` — CREDITS.md와 같은 보이는 글). 출처 링크 · 사용 조건은 화면에 내지 않는다(CREDITS.md에). 항목은 휠로 굴린다(엔진 스크롤 막대 없음 — L1).
    /// 요소 이름: <c>credits</c>·<c>credits-panel</c>·<c>credits-back</c>·<c>credits-notice</c>·<c>credits-license</c>(라이선스 전문이 있는 파일 이름 — 배포본에 BoardBuild.CopyNotices가 넣는다)·<c>credits-privacy</c>(개인 정보 처리 고지 PRIVACY.md — 결정 126 ④)·<c>credits-tab-&lt;묶음&gt;</c>·<c>credits-list</c>·<c>credits-entry-&lt;n&gt;</c>.
    /// </summary>
    public sealed class CreditsScreen : IUiScreen
    {
        readonly CreditsTable table;
        readonly Action back;
        UiStage stage;
        VisualElement tabs, list;
        ScrollView scroll;

        public CreditsScreen(CreditsTable table, Action back)
        {
            this.table = table ?? throw new ArgumentNullException(nameof(table));
            this.back = back ?? throw new ArgumentNullException(nameof(back));
            Picked = table.Sections[0];
        }

        public string Name => "credits";

        /// <summary>보고 있는 묶음.</summary>
        public CreditSection Picked { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            RulebookView.Add(page, new VisualElement(), null, "kit-scrim");
            var panel = RulebookView.Add(page, Kit.Panel(stage.Skin), "credits-panel", "sn-book-panel");
            var head = RulebookView.Add(panel, new VisualElement(), null, "sn-book-head");
            RulebookView.Add(head, Kit.Text(stage.T("screen.credits.title"), Kit.FTitle), null, "kit-title");
            RulebookView.Add(head, new VisualElement(), null, "sn-book-gap");
            RulebookView.Add(head, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Main, back), "credits-back", null);
            RulebookView.Add(panel, Kit.Wrapped(stage.T("screen.credits.notice"), Kit.F2), "credits-notice", "sn-credits-notice").AddToClassList("kit-muted");
            RulebookView.Add(panel, Kit.Wrapped(stage.T("screen.credits.license"), Kit.F2), "credits-license", "sn-credits-notice").AddToClassList("kit-muted");
            // 개인 정보 처리 고지(결정 126 ④ — 뿌리 PRIVACY.md, 배포본에 BoardBuild.CopyNotices가 넣는다): 파일 이름 한 줄 + 마우스 오버 요약
            var privacy = RulebookView.Add(panel, Kit.Wrapped(stage.T("screen.credits.privacy"), Kit.F2), "credits-privacy", "sn-credits-notice");
            privacy.AddToClassList("kit-muted");
            stage.Tips.Hook(privacy, stage.T("screen.title.notice.title"), stage.T("screen.privacy.tip"), TipSide.Below, reading: true);
            var body = RulebookView.Add(panel, new VisualElement(), null, "sn-book-body");
            tabs = RulebookView.Add(body, new VisualElement(), null, "sn-book-side");
            var read = RulebookView.Add(body, new VisualElement(), null, "sn-book-read");
            var wrap = RulebookView.Add(read, new VisualElement(), null, "sn-book-scroll-wrap");
            scroll = RulebookView.Add(wrap, new ScrollView(ScrollViewMode.Vertical), "credits-list", "sn-book-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list = scroll.contentContainer;
            Paint();
            return page;
        }

        public void Close() => stage = null;

        /// <summary>묶음을 고른다(id). 모르는 id면 false.</summary>
        public bool Pick(string id)
        {
            foreach (var s in table.Sections)
                if (s.Id == id)
                {
                    Picked = s;
                    if (stage != null) Paint();
                    return true;
                }
            return false;
        }

        void Paint()
        {
            var inv = CultureInfo.InvariantCulture;
            string lang = stage.Lang;
            tabs.Clear();
            foreach (var s in table.Sections)
            {
                var row = RulebookView.Add(tabs, new VisualElement(), "credits-tab-" + s.Id, "sn-book-row");
                row.AddToClassList("sn-credits-tab");
                row.EnableInClassList("sn-book-row-picked", s == Picked);
                RulebookView.Add(row, Kit.Wrapped(s.TitleIn(lang), Kit.F2), null, "sn-credits-tab-name");
                RulebookView.Add(row, Kit.Text(stage.T("screen.credits.count").Replace("{n}", s.Entries.Count.ToString(inv)), Kit.F2), null, "kit-muted");
                string id = s.Id;
                stage.OnTap(row, SoundKeys.Select, e => Pick(id));
            }
            list.Clear();
            scroll.scrollOffset = Vector2.zero;
            RulebookView.Add(list, Kit.Text(Picked.TitleIn(lang), Kit.F3), null, "sn-book-h").AddToClassList("kit-title");
            for (int i = 0; i < Picked.Entries.Count; i++)
            {
                var e = Picked.Entries[i];
                var box = RulebookView.Add(list, new VisualElement(), "credits-entry-" + i.ToString(inv), "sn-book-card");
                var top = RulebookView.Add(box, new VisualElement(), null, "sn-book-card-head");
                RulebookView.Add(top, Kit.Wrapped(e.Who, Kit.F2), null, "sn-book-card-name").AddToClassList("kit-title");
                RulebookView.Add(box, Kit.Wrapped(e.RoleIn(lang), Kit.F2), null, "sn-book-card-text");
                if (e.Names != null)
                    RulebookView.Add(box, Kit.Wrapped(stage.T("screen.credits.names").Replace("{names}", string.Join(" · ", e.Names)), Kit.F2), null, "sn-book-card-text").AddToClassList("kit-muted");
            }
        }
    }
}
