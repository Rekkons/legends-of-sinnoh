using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 막 안내 창(사용자 결정 101 ② — S14 막 카드의 뒤 절반): 막 카드 연출(4행 <see cref="ActCard"/> — 길이는 방장과 같은 3600)이 걷힌 뒤 내 화면에 뜨고 <b>[확인]을 누를 때까지 머문다</b>.
    /// 「목표 / 달성하면 얻는 것 / 이번 막에서 달라지는 것」으로 나눠 적고, 내가 막 목표를 이뤄 열린 것이면 맨 위에 「얻은 것」 줄이 선다. 위 띠의 막 배지(<c>board-hud-act</c>)를 누르면 다시 본다.
    /// 기록 창과 같은 꼴의 보기 창이다 — 떠 있는 동안 보드 입력은 꺼지지만 화면 대기열은 멈추지 않는다(방에서 남을 기다리게 하지 않는다). 그사이 열 때가 된 화면(조우 창 · 배틀 화면)은 이 창을 닫고 열린다.
    /// 저절로 뜨는 것은 <see cref="GameFlow.ActNotice"/>가 켜진 판만(사람이 하는 판 — 확인·시험의 길잡이는 끈다), 튜토리얼 판에서는 뜨지 않는다(마박사가 말한다).
    /// 글은 <c>screen.act.goal.&lt;막&gt;</c>·<c>screen.act.gain.&lt;막&gt;</c>·<c>screen.act.change.&lt;막&gt;</c>·<c>screen.act.got.&lt;막&gt;</c>(줄 여럿은 줄바꿈으로 — 없는 키의 칸은 만들지 않는다).
    /// 요소 이름: <c>board-act</c>·<c>board-act-panel</c>·<c>board-act-name</c>·<c>board-act-title</c>·<c>board-act-new</c>·<c>board-act-got</c>·<c>board-act-goal</c>·<c>board-act-gain</c>·<c>board-act-change</c>·<c>board-act-ok</c>.
    /// </summary>
    public sealed class ActScreen : IUiScreen
    {
        readonly GameFlow flow;
        readonly List<string> sections = new List<string>();

        /// <param name="act">보일 막(0 서막 · 1~3 · 4 종막).</param>
        /// <param name="got">내가 방금 목표를 이룬 막(「얻은 것」 줄 — 없으면 −1).</param>
        /// <param name="changed">막이 방금 바뀌어 저절로 뜬 것인가(「막이 바뀌었습니다」 배지).</param>
        public ActScreen(GameFlow flow, int act, int got = -1, bool changed = false)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Act = act;
            Got = got;
            Changed = changed;
        }

        public string Name => "board-act";

        /// <summary>보이는 막 · 「얻은 것」 줄의 막(없으면 −1) · 막이 방금 바뀌어 뜬 것인가.</summary>
        public int Act { get; }
        public int Got { get; }
        public bool Changed { get; }

        /// <summary>지금 창에 선 칸(위에서 아래로 — <c>got</c>·<c>goal</c>·<c>gain</c>·<c>change</c>).</summary>
        public IReadOnlyList<string> Sections => sections;

        public VisualElement Build(UiStage stage)
        {
            var inv = CultureInfo.InvariantCulture;
            string n = Act.ToString(inv);
            var page = new VisualElement { name = "board-act-back", pickingMode = PickingMode.Ignore };   // 위 띠의 [메뉴] 등 HUD 단추는 어둠 너머로도 눌린다(T35)
            page.AddToClassList("sn-actinfo-back");
            var panel = Kit.Panel(stage.Skin);
            panel.name = "board-act-panel";
            panel.AddToClassList("sn-actinfo-panel");
            page.Add(panel);

            var head = Add(panel, new VisualElement(), null, "sn-actinfo-head");
            Add(head, Kit.Badge(stage.T(BoardLog.ActKey(Act)), KitBadgeKind.Silver, Kit.F2), "board-act-name", null);
            Add(head, Kit.Text(stage.T(BoardLog.ActTitleKey(Act)), Kit.FTitle), "board-act-title", "kit-title");
            if (Changed) Add(head, Kit.Badge(stage.T("screen.act.info.new"), KitBadgeKind.Gold, Kit.F2), "board-act-new", null);

            sections.Clear();
            if (Got >= 0) Section(stage, panel, "got", stage.T("screen.act.info.got").Replace("{n}", Got.ToString(inv)), Lines(stage, "screen.act.got." + Got.ToString(inv)), "sn-actinfo-got");
            Section(stage, panel, "goal", stage.T("screen.act.info.goal"), Lines(stage, "screen.act.goal." + n), null);
            Section(stage, panel, "gain", stage.T("screen.act.info.gain"), Lines(stage, "screen.act.gain." + n), null);
            Section(stage, panel, "change", stage.T("screen.act.info.change"), Lines(stage, "screen.act.change." + n), null);

            var foot = Add(panel, new VisualElement(), null, "sn-actinfo-foot");
            Add(foot, Kit.Button(stage.T("screen.act.info.ok"), KitButtonKind.Main, () => flow.CloseAct()), "board-act-ok", null);
            return page;
        }

        public void Close()
        {
        }

        // 칸 하나: 머리(작은 제목) + 줄마다 「· 글」(글이 없으면 칸을 만들지 않는다)
        void Section(UiStage stage, VisualElement panel, string id, string title, List<string> body, string cls)
        {
            if (body.Count == 0) return;
            var box = Add(panel, new VisualElement(), "board-act-" + id, "sn-actinfo-section");
            if (cls != null) box.AddToClassList(cls);
            Add(box, Kit.Text(title, Kit.F2), null, "kit-title");
            foreach (string line in body) Add(box, Kit.Wrapped(KoText.JoinWords(line), Kit.F2), null, "sn-actinfo-line");
            sections.Add(id);
        }

        // 그 키의 줄들: 키 하나(「…goal.1」)이거나 번호를 단 줄 여럿(「…gain.1.1」 · 「…gain.1.2」 …)
        static List<string> Lines(UiStage stage, string key)
        {
            var lines = new List<string>();
            string one = stage.TOrNull(key);
            if (one != null) lines.Add(one);
            for (int k = 1; ; k++)
            {
                string line = stage.TOrNull(key + "." + k.ToString(CultureInfo.InvariantCulture));
                if (line == null) break;
                lines.Add(line);
            }
            return lines;
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
