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
    /// 튜토리얼 목차(07 §2-1 · §9-3): 머리(마박사 작은 얼굴 — 필드 도트 · 「튜토리얼」 · 안내 한 줄) → 본편 판(장 줄 = 「제n장」 · 장 이름 · 단계 수 · 완료/이어서) →
    /// 확장 장 판(칸 = 아이콘 · 이름 · 완료, 세 칸씩) → [뒤로](타이틀의 「튜토리얼」 줄로). 장 줄·칸을 누르면 그 장의 처음부터(<see cref="TutorialMenu.Start"/>).
    /// 끝까지 본 장만 「완료」(결정 90 Q7 ① — 글꼴에 ✓가 없어 배지 글 [가정 T42]), 마지막으로 들어갔다 나온 장은 「이어서」(그 장 처음부터 — 판 저장 없음). 단계가 없는 장(각본이 아직 없다)은 흐리게 「준비 중」.
    /// 뒤의 보드 전경은 타이틀과 같다(<see cref="IOverBoard"/>). 자리·크기는 <c>Sinnoh.uss</c>의 <c>sn-tut-*</c>, 글은 <c>screen.tutorial.menu.*</c>.
    /// 완료한 장에는 「설명서에서 더 보기」 단추 [?](M9 — 그 장의 첫 설명서 절로 룰 설명서 큰 창을 목차 위에 덮어 연다, <see cref="More"/>).
    /// 요소 이름: <c>tut-menu</c>·<c>tut-menu-col</c>·<c>tut-menu-face</c>·<c>tut-menu-main</c>·<c>tut-menu-extra</c>·<c>tut-menu-ch-&lt;장 id&gt;</c>(줄·칸)·<c>tut-menu-book-&lt;장 id&gt;</c>·<c>tut-menu-back</c>.
    /// </summary>
    public sealed class TutorialMenuScreen : IUiScreen, IOverBoard
    {
        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-tut-col</c>의 left 60 + width 560).</summary>
        public const float ColumnRight = 620f;

        /// <summary>1080 벌의 판 오른쪽 변(720 토큰 px로 — <c>sn-tut-col</c>의 1080 벌 width 1020 = 680 토큰, left 60): 글자가 큰 벌만 판이 넓다(plan §5 #441).</summary>
        public const float ColumnRightBig = 740f;

        /// <summary>1080 벌 · 영어의 판 오른쪽 변(720 토큰 px — <c>.sn-lang-en .sn-tut-col</c>의 1080 벌 width 1110 = 740 토큰): 영어 장 이름이 더 길다.</summary>
        public const float ColumnRightBigEn = 800f;

        /// <summary>확장 장 판 한 줄의 칸 수.</summary>
        public const int ExtraColumns = 3;

        readonly GameFlow flow;
        readonly Dictionary<string, VisualElement> cells = new Dictionary<string, VisualElement>();
        UiStage stage;

        public TutorialMenuScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "tut-menu";

        public float PanelRight => flow.Stage.Grade.TokenHeight < 1080 ? ColumnRight : flow.Lang == "en" ? ColumnRightBigEn : ColumnRightBig;

        /// <summary>장 id → 그 장의 줄·칸 요소(만든 뒤).</summary>
        public IReadOnlyDictionary<string, VisualElement> Cells => cells;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            cells.Clear();
            var tut = flow.Tutorial;
            var page = new VisualElement();
            var col = Add(page, new VisualElement(), "tut-menu-col", "sn-tut-col");

            var head = Add(col, new VisualElement(), "tut-menu-head", "sn-tut-head");
            var face = Face("tut-menu-face");
            if (face != null) head.Add(face);
            var words = Add(head, new VisualElement(), null, "sn-tut-head-words");
            Add(words, Kit.Text(stage.T("screen.tutorial.menu.title"), Kit.FTitle), null, "sn-head");
            Add(words, Kit.Text(stage.T("screen.tutorial.menu.guide"), Kit.F2), null, "sn-guide");

            var chapters = tut.Chapters ?? new List<TutorialChapter>();
            var main = Add(col, Kit.Panel(stage.Skin), "tut-menu-main", "sn-tut-panel");
            Section(main, "screen.tutorial.menu.main");
            var extra = Add(col, Kit.Panel(stage.Skin), "tut-menu-extra", "sn-tut-panel");
            Section(extra, "screen.tutorial.menu.extra");
            VisualElement line = null;
            int n = 0;
            foreach (var ch in chapters)
            {
                if (ch.IsMain)
                {
                    MainRow(main, ch, tut.Record.MarkOf(ch.Id));
                    continue;
                }
                if (n++ % ExtraColumns == 0) line = Add(extra, new VisualElement(), null, "sn-tut-cells");
                ExtraCell(line, ch, tut.Record.MarkOf(ch.Id));
            }

            var foot = Add(col, new VisualElement(), "tut-menu-foot", "sn-foot-row");
            Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "tut-menu-back", null);
            return page;
        }

        public void Close() => flow.Tutorial.CloseBook();

        /// <summary>[뒤로]: 타이틀로(「튜토리얼」 줄을 고른 채).</summary>
        public void Back() => flow.OpenTitle(TitleScreen.Tutorial);

        /// <summary>
        /// 완료한 장의 「설명서에서 더 보기」 단추([?] — 누르기와 같다): 목차 위에 룰 설명서 큰 창을 그 장의 첫 절로 덮어 연다(닫으면 목차 — 07 T220). 단추가 없는 장이면 false.
        /// </summary>
        public bool More(string chapterId)
        {
            var ch = flow.Tutorial.Chapters != null ? TutorialChapters.Find(flow.Tutorial.Chapters, chapterId) : null;
            if (ch == null || flow.Tutorial.Record.MarkOf(chapterId) != TutorialMark.Seen) return false;
            var more = flow.Tutorial.BookOf(ch);
            return more.Count > 0 && flow.Tutorial.OpenBook(more[0].Key);
        }

        // 완료한 장의 [?] 단추(위 띠의 설명서 단추와 같은 글리프 — 칸이 좁다): 줄 · 칸 누르기(장 시작)로 번지지 않게 클릭을 여기서 멈춘다
        void BookButton(VisualElement parent, TutorialChapter ch, TutorialMark mark)
        {
            if (mark != TutorialMark.Seen) return;
            var more = flow.Tutorial.BookOf(ch);
            if (more.Count == 0) return;
            string id = ch.Id;
            var button = Add(parent, Kit.Button(RulebookView.Help, KitButtonKind.Normal, () => More(id)), "tut-menu-book-" + ch.Id, "sn-tut-book");
            button.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            stage.Tips.Hook(button, stage.T("screen.tutorial.menu.book"), more[0].ChapterTitle + " · " + more[0].Title, TipSide.Below, reading: true);
        }

        /// <summary>그 장을 고른다(줄·칸 누르기와 같다). 고를 수 없는 장이면 false.</summary>
        public bool Pick(string chapterId) => flow.Tutorial.Start(chapterId);

        void Section(VisualElement panel, string key)
        {
            var title = Add(panel, Kit.Text(stage.T(key), Kit.FTitle), null, "sn-tut-section");
            stage.Tips.Hook(title, stage.T(key), stage.T(key + ".tip"), TipSide.Below, reading: true);
        }

        void MainRow(VisualElement panel, TutorialChapter ch, TutorialMark mark)
        {
            var row = Add(panel, new VisualElement(), "tut-menu-ch-" + ch.Id, "sn-tut-row");
            cells[ch.Id] = row;
            Add(row, Kit.Text(stage.T("screen.tutorial.menu.ch_no").Replace("{n}", ch.Chapter.ToString(CultureInfo.InvariantCulture)), Kit.F2), null, "sn-tut-no");
            Add(row, Kit.Text(stage.T(ch.Name), Kit.F2), null, "sn-tut-name");
            Add(row, Kit.Text(ch.Ready ? stage.T("screen.tutorial.menu.steps").Replace("{n}", ch.Steps.ToString(CultureInfo.InvariantCulture)) : stage.T("screen.tutorial.menu.not_ready"), Kit.F2), null, "sn-tut-steps");
            var state = Add(row, new VisualElement(), null, "sn-tut-state");
            Mark(state, mark);
            BookButton(state, ch, mark);
            Hook(row, ch, mark);
        }

        void ExtraCell(VisualElement line, TutorialChapter ch, TutorialMark mark)
        {
            var cell = Add(line, new VisualElement(), "tut-menu-ch-" + ch.Id, "sn-tut-cell");
            cells[ch.Id] = cell;
            var icon = Add(cell, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-tut-icon");
            var tex = ch.Icon != null ? GenAssets.Load<Texture2D>(ch.Icon) : null;
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            Add(cell, Kit.Wrapped(stage.T(ch.Name), Kit.F2), null, "sn-tut-cell-name");
            if (mark == TutorialMark.Seen)
            {
                // 「완료」 배지 아래에 [?]를 세로로 쌓는다 — 옆으로 놓으면 이름 칸이 좁아져 이름이 서너 줄로 감긴다(칸은 화면 폭의 1/3)
                var side = Add(cell, new VisualElement(), null, "sn-tut-cell-side");
                Mark(side, mark);
                BookButton(side, ch, mark);
            }
            Hook(cell, ch, mark);
        }

        void Mark(VisualElement parent, TutorialMark mark)
        {
            if (mark == TutorialMark.Seen) Add(parent, Kit.Badge(stage.T("screen.tutorial.menu.done"), KitBadgeKind.Ok, Kit.F2), null, "sn-tut-badge");
            else if (mark == TutorialMark.Resume) Add(parent, Kit.Badge(stage.T("screen.tutorial.menu.resume"), KitBadgeKind.Gold, Kit.F2), null, "sn-tut-badge");
        }

        // 누르기 · 흐림 · 툴팁(장 이름 + 단계 수 · 완료 · 이어서 · 준비 중)
        void Hook(VisualElement e, TutorialChapter ch, TutorialMark mark)
        {
            string id = ch.Id;
            if (!ch.Ready) e.AddToClassList("sn-off");
            stage.OnTap(e, SoundKeys.Decide, _ => Pick(id), () => ch.Ready);
            string body = !ch.Ready ? stage.T("screen.common.not_ready")
                : mark == TutorialMark.Seen ? stage.T("screen.tutorial.menu.done.tip")
                : mark == TutorialMark.Resume ? stage.T("screen.tutorial.menu.resume.tip")
                : stage.T("screen.tutorial.menu.steps").Replace("{n}", ch.Steps.ToString(CultureInfo.InvariantCulture));
            stage.Tips.Hook(e, stage.T(ch.Name), body, TipSide.Below, reading: true);
        }

        /// <summary>
        /// 마박사 작은 얼굴(필드 도트 앞모습 32×32 통째 — 07 T29, 결정 90 Q2 가): 정수배(720 ×2 · 1080 ×3 — <c>sn-tut-face</c>). 그림이 없으면 null(칸을 만들지 않는다).
        /// </summary>
        public static VisualElement Face(string name)
        {
            var tex = GenAssets.Load<Texture2D>(GenAssets.RowanFace);
            if (tex == null) return null;
            var face = new Image { name = name, image = tex, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            face.AddToClassList("sn-tut-face");
            return face;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
