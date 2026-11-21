using System;
using Rekkon.UiKit;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 튜토리얼 끝내기 창(07 §2-2): 「튜토리얼을 끝내겠습니까?」 + [타이틀로] [목차로] [계속하기](「튜토리얼 판은 저장되지 않습니다.」 줄은 뺐다 — 결정 96 Q8, 07 T171).
    /// 말풍선의 [튜토리얼 끝내기]와 게임 메뉴의 [튜토리얼 끝내기]가 같은 창을 연다(<see cref="TutorialMenu.OpenQuit"/>). 무대의 화면을 닫지 않고 그 위에 덮는다 — 뒤는 어둡게 덮어 누르지 못한다.
    /// 요소 이름: <c>tut-quit</c>·<c>tut-quit-panel</c>·<c>tut-quit-title</c>·<c>tut-quit-menu</c>·<c>tut-quit-continue</c>. 자리·크기는 <c>Sinnoh.uss</c>의 <c>sn-tut-dialog</c>.
    /// </summary>
    public sealed class TutorialQuitScreen
    {
        readonly TutorialMenu menu;

        public TutorialQuitScreen(TutorialMenu menu)
        {
            this.menu = menu ?? throw new ArgumentNullException(nameof(menu));
        }

        public VisualElement Build(UiStage stage)
        {
            var page = TutorialEndScreen.Page("tut-quit");
            var panel = TutorialEndScreen.Panel(stage, page, "tut-quit-panel");
            panel.Add(TutorialEndScreen.Class(Kit.Text(stage.T("screen.tutorial.menu.quit.ask"), Kit.F2), "sn-tut-dialog-ask"));
            var row = TutorialEndScreen.Class(new VisualElement(), "sn-foot-row");
            row.Add(TutorialEndScreen.Named(Kit.Button(stage.T(menu.TitleKey), KitButtonKind.Normal, menu.ToTitle), "tut-quit-title"));
            row.Add(TutorialEndScreen.Named(Kit.Button(stage.T("screen.tutorial.menu.to_menu"), KitButtonKind.Normal, menu.ToMenu), "tut-quit-menu"));
            row.Add(TutorialEndScreen.Named(Kit.Button(stage.T("screen.tutorial.menu.continue"), KitButtonKind.Main, menu.Continue), "tut-quit-continue"));
            panel.Add(row);
            return page;
        }
    }
}
