using System;
using Rekkon.UiKit;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 타이틀 설정에서 연 버그 신고 창(M9 — 결정 132 ①): 게임 메뉴의 신고 쪽과 같은 몸(<see cref="BugReportPanel"/>)을 가운데 판에 싣는다. [취소] · [닫기] · Esc = 설정으로.
    /// 요소 이름: <c>bug-report</c> · <c>bug-report-panel</c>(+ 몸의 이름들).
    /// </summary>
    public sealed class BugReportScreen : IUiScreen
    {
        readonly Action back;

        public BugReportScreen(GameFlow flow, Action back)
        {
            this.back = back ?? throw new ArgumentNullException(nameof(back));
            Panel = new BugReportPanel(flow, back);
        }

        public string Name => "bug-report";

        /// <summary>창의 몸(단계 · 보내기 — 확인 · 시험).</summary>
        public BugReportPanel Panel { get; }

        public VisualElement Build(UiStage stage)
        {
            var page = new VisualElement();
            var scrim = new VisualElement();
            scrim.AddToClassList("kit-scrim");
            page.Add(scrim);
            var panel = Kit.Panel(stage.Skin);
            panel.name = "bug-report-panel";
            panel.AddToClassList("sn-menu-panel");
            panel.AddToClassList("sn-menu-panel-bug");
            page.Add(panel);
            Panel.Build(stage, panel);
            return page;
        }

        public void Close() => Panel.Close();

        /// <summary>[취소] · [닫기] · Esc: 설정으로.</summary>
        public void Back() => back();
    }
}
