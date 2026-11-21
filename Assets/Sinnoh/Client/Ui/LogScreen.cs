using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S21 기록(04 §3-3 「기록 펼치기」, §8-1 P18): 보드에서 지금까지 일어난 일을 다시 읽는 창. 보드 HUD의 기록 판 [펼치기]로 연다.
    /// 줄은 <see cref="BoardLog"/>(받은 이벤트 → 문자열 키 + 빈칸)이 만들고, 그 행동의 연출이 끝난 줄만 보인다(결과가 먼저 새지 않는다).
    /// 라운드마다 머리 줄(「n라운드」 — 셋업은 「게임 준비」)이 서고, 줄 머리의 번호 칸은 그 줄의 주인 좌석 색이다.
    /// 한 쪽에 <see cref="PageLines"/>줄 — 가장 최근 쪽부터 보이고 [이전]·[다음](또는 휠)으로 넘긴다(스크롤 막대를 쓰지 않는다 — 겉모양 L1에 엔진 기본 막대가 섞이지 않게 [가정]).
    /// 창 밖을 누르거나 [닫기]를 누르면 닫힌다. 떠 있는 동안 보드 입력은 꺼지지만 화면 대기열은 멈추지 않는다(04 §8-2 「보기·기록 다시 보기는 막지 않는다」) —
    /// 그사이 열 때가 된 화면(조우 창·배틀 화면)은 이 창을 닫고 열린다. 줄을 눌러 다시 재생하기(P18)는 아직 없다.
    /// 요소 이름: <c>board-log</c>·<c>board-log-panel</c>·<c>board-log-lines</c>·<c>board-log-page</c>·<c>board-log-older</c>·<c>board-log-newer</c>·<c>board-log-close</c>.
    /// </summary>
    public sealed class LogScreen : IUiScreen
    {
        /// <summary>한 쪽의 줄 수(라운드 머리 포함).</summary>
        public const int PageLines = 14;

        readonly GameFlow flow;
        readonly List<BoardLogLine> all = new List<BoardLogLine>();
        readonly List<string> texts = new List<string>();
        UiStage stage;
        VisualElement lines;
        Label pageLabel;
        Button older, newer;
        int painted = -1;

        public LogScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-log";

        /// <summary>지금 보는 쪽(0 = 가장 최근)과 쪽 수.</summary>
        public int Page { get; private set; }
        public int Pages { get; private set; } = 1;

        /// <summary>지금 쪽에 보이는 줄의 글(위에서 아래로, 라운드 머리 포함).</summary>
        public IReadOnlyList<string> Shown => texts;

        /// <summary>보이는 줄 전체의 수(모든 쪽).</summary>
        public int Total => all.Count;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { name = "board-log-back", pickingMode = PickingMode.Ignore };   // 위 띠 · 아래 판(HUD)의 입력을 먹지 않는다(T35)
            // 창 밖(가운데 필드)을 누르면 닫힌다 — 보이지 않는 막
            var back = new VisualElement { name = "board-log-catch" };
            back.AddToClassList("sn-field-catch");
            back.RegisterCallback<PointerDownEvent>(e => flow.CloseLog());
            page.Add(back);
            var panel = Kit.Panel(stage.Skin);
            panel.name = "board-log-panel";
            panel.AddToClassList("sn-log-panel");
            page.Add(panel);
            var head = Add(panel, new VisualElement(), null, "sn-log-head");
            Add(head, Kit.Text(stage.T("screen.log.title"), Kit.FTitle), null, "kit-title");
            pageLabel = Add(head, Kit.Text("", Kit.F2), "board-log-page", "kit-muted");
            lines = Add(panel, new VisualElement(), "board-log-lines", "sn-log-lines");
            var foot = Add(panel, new VisualElement(), null, "sn-foot-row");
            older = Add(foot, Kit.Button(stage.T("screen.log.older"), KitButtonKind.Normal, () => Turn(1)), "board-log-older", null);
            newer = Add(foot, Kit.Button(stage.T("screen.log.newer"), KitButtonKind.Normal, () => Turn(-1)), "board-log-newer", null);
            Add(foot, Kit.Button(stage.T("screen.log.close"), KitButtonKind.Main, () => flow.CloseLog()), "board-log-close", null);
            panel.RegisterCallback<WheelEvent>(e =>
            {
                Turn(e.delta.y < 0f ? 1 : -1);   // 위로 굴리면 이전 쪽
                e.StopPropagation();
            });
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
        }

        /// <summary>쪽을 넘긴다(+1 = 이전(더 오래된) 쪽, −1 = 다음 쪽). 넘어갔으면 true.</summary>
        public bool Turn(int by)
        {
            int next = Mathf.Clamp(Page + by, 0, Pages - 1);
            if (next == Page) return false;
            Page = next;
            painted = -1;
            Refresh();
            return true;
        }

        /// <summary>보이는 줄이 늘었으면 다시 그린다(가장 최근 쪽을 보고 있으면 새 줄이 따라 붙는다).</summary>
        public void Refresh()
        {
            if (stage == null || flow.Log == null) return;
            all.Clear();
            flow.Log.Shown(flow.Fx != null ? flow.Fx.Sequencer : null, all);
            if (all.Count == painted) return;
            painted = all.Count;
            Pages = Math.Max(1, (all.Count + PageLines - 1) / PageLines);
            Page = Mathf.Clamp(Page, 0, Pages - 1);
            int end = all.Count - Page * PageLines, start = Math.Max(0, end - PageLines);
            lines.Clear();
            texts.Clear();
            for (int i = start; i < end; i++)
            {
                var line = all[i];
                string text = line.Text(stage.T, flow.SeatName);
                texts.Add(text);
                if (line.Kind == BoardLogKind.Round)
                {
                    var head = Kit.Text(text, Kit.F2);
                    head.AddToClassList("kit-title");
                    head.AddToClassList("sn-log-round");
                    lines.Add(head);
                }
                else lines.Add(Row(flow, line, text, Kit.F2));
            }
            if (all.Count == 0) Add(lines, Kit.Text(stage.T("screen.log.empty"), Kit.F2), null, "kit-muted");
            var inv = CultureInfo.InvariantCulture;
            pageLabel.text = (Pages - Page).ToString(inv) + "/" + Pages.ToString(inv);
            older.SetEnabled(Page < Pages - 1);
            newer.SetEnabled(Page > 0);
        }

        /// <summary>기록 한 줄의 요소: 주인 좌석의 번호 칸(좌석 색 — 좌석 없는 줄은 빈 칸) + 글. HUD의 기록 판과 이 창이 같이 쓴다.</summary>
        public static VisualElement Row(GameFlow flow, BoardLogLine line, string text, string size)
        {
            var row = new VisualElement();
            row.AddToClassList("sn-log-row");
            // 소통(결정 102): 기록 줄은 핑의 공개 대상(행동 번호)이고, 방 판에서 남의 행동 줄을 누르면 도장 메뉴가 뜬다(혼자 하기 · 튜토리얼은 아무 일도 없다)
            if (line.Seq >= 0)
            {
                row.name = RoomLayer.LogRowName(line.Seq);
                row.userData = line;
                row.RegisterCallback<ClickEvent>(_ => flow.RoomLayer?.RowClicked(line, row));
            }
            var mark = Kit.Text(line.Seat >= 0 ? (line.Seat + 1).ToString(CultureInfo.InvariantCulture) : "", Kit.F1);
            mark.AddToClassList("sn-log-mark");
            if (line.Seat >= 0)
            {
                var color = flow.SeatColor(line.Seat);
                mark.style.backgroundColor = new StyleColor((Color)color);
                mark.style.color = new StyleColor((Color)SeatColors.Ink(color));
            }
            else mark.style.visibility = Visibility.Hidden;   // 좌석 없는 줄(막 · 조무래기)은 자리만 둔다
            row.Add(mark);
            var label = Kit.Text(text, size);
            label.AddToClassList("sn-log-text");
            row.Add(label);
            return row;
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
