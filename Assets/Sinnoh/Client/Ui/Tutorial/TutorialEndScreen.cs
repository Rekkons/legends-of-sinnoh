using System;
using Rekkon.UiKit;
using Sinnoh.Data;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 튜토리얼 끝 창(07 §2-2 · 결정 90 Q7 ③ 나): 각본의 마지막 단계 뒤. 마박사 작은 얼굴 + 「본편을 끝까지 마쳤습니다.」(확장 장은 「이 장을 끝까지 마쳤습니다.」)
    /// + 본편 [목차로] · [타이틀로] / 확장 장 [목차로]. 혼자 하기로 바로 가는 단추는 두지 않는다. 판은 버린다. 끝내기 창(<see cref="TutorialQuitScreen"/>)과 같은 틀.
    /// 「설명서에서 더 보기」(M9 — 07 T220): 그 장의 설명서 절마다 단추 하나(절 제목 — 누르면 끝 창 위에 룰 설명서 큰 창이 그 절로, 닫으면 끝 창). 설명서 데이터가 없으면 줄을 만들지 않는다.
    /// 요소 이름: <c>tut-end</c>·<c>tut-end-panel</c>·<c>tut-end-face</c>·<c>tut-end-text</c>·<c>tut-end-book</c>·<c>tut-end-book-&lt;n&gt;</c>·<c>tut-end-menu</c>·<c>tut-end-title</c>(본편만).
    /// </summary>
    public sealed class TutorialEndScreen
    {
        readonly TutorialMenu menu;
        readonly bool main;
        readonly TutorialChapter chapter;

        public TutorialEndScreen(TutorialMenu menu, bool main, TutorialChapter chapter = null)
        {
            this.menu = menu ?? throw new ArgumentNullException(nameof(menu));
            this.main = main;
            this.chapter = chapter;
        }

        public VisualElement Build(UiStage stage)
        {
            var page = Page("tut-end");
            var panel = Panel(stage, page, "tut-end-panel");
            var head = Class(new VisualElement(), "sn-tut-end-head");
            var face = TutorialMenuScreen.Face("tut-end-face");
            if (face != null) head.Add(face);
            head.Add(Named(Class(Kit.Wrapped(stage.T(main ? "screen.tutorial.menu.end.main" : "screen.tutorial.menu.end.extra"), Kit.F2), "sn-tut-end-text"), "tut-end-text"));
            panel.Add(head);
            var more = menu.BookOf(chapter);
            if (more.Count > 0)
            {
                var box = Named(Class(new VisualElement(), "sn-tut-end-book"), "tut-end-book");
                box.Add(Class(Kit.Text(stage.T("screen.tutorial.menu.book"), Kit.F2), "kit-muted"));
                var chips = Class(new VisualElement(), "sn-tut-end-chips");
                for (int i = 0; i < more.Count; i++)
                {
                    string key = more[i].Key;
                    var chip = Named(Kit.Button(more[i].Title, KitButtonKind.Normal, () => menu.OpenBook(key)), "tut-end-book-" + i);
                    stage.Tips.Hook(chip, null, more[i].ChapterTitle + " · " + more[i].Title, TipSide.Below, reading: true);   // 절 제목만으로는 어느 장인지 모른다(「개요」)
                    chips.Add(chip);
                }
                box.Add(chips);
                panel.Add(box);
            }
            var row = Class(new VisualElement(), "sn-foot-row");
            row.Add(Named(Kit.Button(stage.T("screen.tutorial.menu.to_menu"), KitButtonKind.Main, menu.ToMenu), "tut-end-menu"));
            if (main) row.Add(Named(Kit.Button(stage.T(menu.TitleKey), KitButtonKind.Normal, menu.ToTitle), "tut-end-title"));
            panel.Add(row);
            return page;
        }

        /// <summary>창 바탕: 화면을 덮는 어두운 막(뒤를 누르지 못하게) — 끝내기 창과 같이 쓴다.</summary>
        internal static VisualElement Page(string name)
        {
            var page = Named(new VisualElement(), name);
            page.AddToClassList("sn-screen");
            page.Add(Class(new VisualElement(), "kit-scrim"));
            return page;
        }

        /// <summary>가운데 창틀 판(제목 「튜토리얼」).</summary>
        internal static VisualElement Panel(UiStage stage, VisualElement page, string name)
        {
            var panel = Named(Kit.Panel(stage.Skin), name);
            panel.AddToClassList("sn-tut-dialog");
            panel.Add(Class(Kit.Text(stage.T("screen.tutorial.menu.title"), Kit.FTitle), "kit-title"));
            page.Add(panel);
            return panel;
        }

        internal static T Named<T>(T e, string name) where T : VisualElement
        {
            e.name = name;
            return e;
        }

        internal static T Class<T>(T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }
    }
}
