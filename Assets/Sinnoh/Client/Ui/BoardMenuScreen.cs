using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S22 게임 메뉴(04 §2, 결정 56 ⑥): 보드 HUD 위에 여는 창. 저장 상태 줄 · [게임으로 돌아가기] · [설정](결정 70 ⑨ — 타이틀의 설정과 같은 화면, 닫으면 이 메뉴로) · [타이틀로 나가기].
    /// 저장 상태 줄(04 §9-12): 「자동 저장됨 · 09:00」(현지 시각) · 쓰기에 실패했으면 「저장하지 못했습니다.」 · 저장하지 않는 판(확인용)이면 「저장하지 않는 게임」. 설명은 마우스 오버.
    /// 나가기는 한 번 더 묻는다 [가정] — 따로 쓰는 것은 없다(명령마다 이미 저장돼 있다). 묻는 글 아래 줄이 나가면 어떻게 되는지 적는다:
    /// 저장된 판 「진행한 내용은 저장되어 있습니다.」 · 배틀·조우 한가운데 「배틀 중에 나가면 배틀 전부터 다시 시작합니다.」 · 저장하지 않는 판(확인용 · 저장 폴더를 만들지 못한 판) 「저장되지 않아 진행한 내용이 사라집니다.」 ·
    /// 저장하는 판인데 마지막 쓰기가 실패한 때 「마지막으로 저장된 곳부터 이어할 수 있습니다.」(사라지지 않는다 — 로그 줄은 받아들인 명령마다 쓰여 있다).
    /// 규칙서·투표는 그 기능이 생길 때 줄을 더한다.
    /// 혼자 하는 판에서는 열려 있는 동안 화면 대기열이 다음 항목을 시작하지 않는다(일시정지 — <see cref="GameFlow.OpenMenu"/>가 <c>FxPlayer.SetPaused</c>로 건다). 방 판은 흐른다(플레이 시험 T4).
    /// 요소 이름: <c>board-menu</c>·<c>board-menu-panel</c>·<c>board-menu-save</c>·<c>board-menu-resume</c>·<c>board-menu-settings</c>·<c>board-menu-quit</c>·<c>board-menu-bug</c>(버그 신고 — 결정 132 ①, 누르면 이 판이 신고 쪽 <see cref="BugPage"/>)·<c>board-menu-ask</c>·<c>board-menu-quit-yes</c>·<c>board-menu-quit-no</c>,
    /// 무르기를 켠 판(결정 149): <c>board-menu-undo</c>(한 수 무르기) · <c>board-menu-rewind</c>(자유 + 솔로 플레이어 · 방장 — 누르면 지난 턴 목록 쪽 <see cref="RewindPage"/>: <c>board-menu-rewind-row-&lt;n&gt;</c>(<c>-face</c> · <c>-head</c> · <c>-deeds</c> · <c>-now</c> · <c>-go</c>) · <c>board-menu-rewind-ask</c> · <c>-yes</c> · <c>-no</c> · <c>-back</c>).
    /// </summary>
    public sealed class BoardMenuScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;

        public BoardMenuScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-menu";

        /// <summary>「타이틀로 나가겠습니까?」를 묻는 중인가.</summary>
        public bool Asking { get; private set; }

        /// <summary>저장 상태 줄에 지금 적힌 글.</summary>
        public string SaveText { get; private set; }

        /// <summary>
        /// 나가기를 물을 때 아래 줄에 적는 글의 키: 저장하지 않는 판(나가면 사라진다) · 배틀 중(그 앞으로 돌아간다) · 쓰기 실패(마지막으로 쓰인 곳까지는 남아 있다) · 저장됨.
        /// 「사라집니다」는 세이브에 쓰지 않는 판(<see cref="GameFlow.SaveId"/>가 null)에서만 적는다.
        /// </summary>
        public string QuitNoteKey =>
            Live != null ? (Live.IsHost ? (Live.Saving ? "screen.room.menu.close.saved" : "screen.board.menu.quit.unsaved") : "screen.room.menu.leave.note")
            : flow.SaveId == null ? "screen.board.menu.quit.unsaved"
            : flow.LeaveRewinds ? "screen.board.menu.quit.battle"
            : flow.SaveStatus == SaveStatus.Failed ? "screen.board.menu.quit.failed" : "screen.board.menu.quit.saved";

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            page.Add(Class(new VisualElement(), "kit-scrim"));   // 뒤의 HUD·보드를 누르지 못하게 덮는다
            panel = Kit.Panel(stage.Skin);
            panel.name = "board-menu-panel";
            panel.AddToClassList("sn-menu-panel");
            page.Add(panel);
            Fill();
            return page;
        }

        public void Close() => bug?.Close();

        void Fill()
        {
            panel.Clear();
            panel.EnableInClassList("sn-menu-panel-room", Live != null && Page != BugPage && Page != RewindPage);
            panel.EnableInClassList("sn-menu-panel-bug", Page == BugPage);
            panel.EnableInClassList("sn-menu-panel-rewind", Page == RewindPage && RewindAsking == null);
            panel.EnableInClassList("sn-menu-panel-rewind-ask", Page == RewindPage && RewindAsking != null);   // 확인 물음은 가운데 작은 판
            if (Page == BugPage && bug != null)
            {
                bug.Build(stage, panel);
                return;
            }
            if (Page == RewindPage)
            {
                FillRewind();
                return;
            }
            if (Live != null)
            {
                FillRoom();
                return;
            }
            panel.Add(Class(Kit.Text(stage.T("screen.board.menu.title"), Kit.FTitle), "kit-title"));
            if (!Asking && flow.Tutorial.Running)
            {
                // 튜토리얼 판(M6.5 — 07 §2-2·§2-3): 저장 줄이 없다(결정 96 Q8 — 「저장되지 않습니다」 문구를 뺐다), 나가기는 끝내기 창(말풍선의 [튜토리얼 끝내기]와 같은 창)
                SaveText = null;
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.resume"), KitButtonKind.Main, Resume), "board-menu-resume"));
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulebook"), KitButtonKind.Normal, () => flow.OpenMenuRulebook()), "board-menu-rulebook"));   // 룰 설명서 S43(M9)
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulings"), KitButtonKind.Normal, () => flow.OpenMenuRulings()), "board-menu-rulings"));   // 규칙 판정(결정 213)
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.settings"), KitButtonKind.Normal, OpenSettings), "board-menu-settings"));
                AddBug();
                panel.Add(Named(Kit.Button(stage.T("screen.tutorial.menu.quit"), KitButtonKind.Normal, AskQuit), "board-menu-quit"));
                return;
            }
            if (!Asking)
            {
                var status = flow.SaveStatus;
                SaveText = status == SaveStatus.Saved ? stage.T("screen.board.menu.save.saved").Replace("{time}", GameFlow.SaveTime(flow.SavedAtMs, date: false))
                    : stage.T(status == SaveStatus.Failed ? "screen.board.menu.save.failed" : "screen.board.menu.save.off");
                var save = Named(Class(Kit.Text(SaveText, Kit.F2), status == SaveStatus.Failed ? "sn-menu-warn" : "kit-muted"), "board-menu-save");
                if (status != SaveStatus.Off)
                    stage.Tips.Hook(save, null, stage.T(status == SaveStatus.Failed ? "screen.board.menu.save.failed.tip" : "screen.board.menu.save.tip"), TipSide.Below, reading: true);
                panel.Add(save);
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.resume"), KitButtonKind.Main, Resume), "board-menu-resume"));
                AddUndo();
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulebook"), KitButtonKind.Normal, () => flow.OpenMenuRulebook()), "board-menu-rulebook"));   // 룰 설명서 S43(M9)
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulings"), KitButtonKind.Normal, () => flow.OpenMenuRulings()), "board-menu-rulings"));   // 규칙 판정(결정 213)
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.settings"), KitButtonKind.Normal, OpenSettings), "board-menu-settings"));
                AddBug();
                panel.Add(Named(Kit.Button(stage.T("screen.board.menu.quit"), KitButtonKind.Normal, AskQuit), "board-menu-quit"));
                return;
            }
            var ask = Named(new VisualElement(), "board-menu-ask");
            ask.Add(Kit.Text(stage.T("screen.board.menu.quit.ask"), Kit.F2));
            ask.Add(Class(Kit.Wrapped(stage.T(QuitNoteKey), Kit.F2), "kit-muted"));
            panel.Add(ask);
            var row = Class(new VisualElement(), "sn-foot-row");
            row.Add(Named(Kit.Button(stage.T("screen.board.menu.quit.yes"), KitButtonKind.End, Quit), "board-menu-quit-yes"));
            row.Add(Named(Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, Cancel), "board-menu-quit-no"));
            panel.Add(row);
        }

        // ── 방 판(M7 화면 ② — 02 §5-7 · §6-2 · §3-2 · §6-3, [가정 M7-56]) ──

        /// <summary>메뉴의 쪽: 0 = 메뉴 · <see cref="VotePage"/> 투표 열기 · <see cref="SetPage"/> 게임 중 방 설정(둘은 방 판만) · <see cref="BugPage"/> 버그 신고(모든 판).</summary>
        public const int MainPage = 0, VotePage = 1, SetPage = 2, BugPage = 3, RewindPage = 4;

        /// <summary>방 판이면 그 방(아니면 null — 혼자 하는 판 · 튜토리얼).</summary>
        RoomLive Live => flow.Room.InGame ? flow.Room.Live : null;

        /// <summary>메뉴의 지금 쪽.</summary>
        public int Page { get; private set; }

        // 게임 중 방 설정 쪽(결정 140 ①): 방 상태가 바뀌면(방장의 스테퍼 · 프리셋 → 방이 돌려준 새 상태) 다시 그린다 — 판은 한 벌(손님의 「변경됨」을 잇는다)
        RoomSettingsPanel setPanel;
        RoomState setShown;

        // 되돌리기 쪽이 목록을 달라고 한 때의 제안(갱신마다 새 객체) — 쪽이 열린 동안 판이 흐르면(새 갱신 · 턴 바뀜) 다시 달라고 한다
        UndoOffer pointsFor;

        /// <summary>
        /// 방 판의 매 프레임(<see cref="RoomLayer"/>): 방 설정 쪽이 그린 방 상태가 낡았으면 · 되돌리기 쪽이 기다리던 목록이 왔으면 다시 그린다.
        /// 되돌리기 쪽이 열린 동안 새 갱신이 오면 목록을 다시 받는다(받은 목록은 새것이 올 때까지 그대로 — 「지금」 꼬리표 · 지금 턴 줄이 판을 따라간다).
        /// </summary>
        public void SyncRoom(RoomState room)
        {
            if (Page == RewindPage && flow.RewindShown && !ReferenceEquals(pointsFor, flow.Undo))
            {
                pointsFor = flow.Undo;
                flow.RequestUndoPoints(keep: true);
            }
            if (Page == RewindPage && RewindAsking == null && flow.RewindShown && !ReferenceEquals(RewindPoints, flow.UndoPoints()))
            {
                Fill();
                return;
            }
            if (Page != SetPage || Asking || room == null || ReferenceEquals(room, setShown)) return;
            Fill();
        }

        // ── 버그 신고(M9 — 결정 132 ①): 메뉴 판이 신고 쪽으로 바뀐다(창 몸 BugReportPanel — 타이틀 설정의 BugReportScreen과 같다) ──

        BugReportPanel bug;

        /// <summary>신고 쪽이 열려 있으면 그 몸(아니면 null).</summary>
        public BugReportPanel Bug => Page == BugPage ? bug : null;

        // [버그 신고]: 보낼 창구가 없는 화면(에디터)은 흐린 단추 「버그 신고(PC판에서만)」 + 이유 툴팁(누르면 아무 일도 없다)
        void AddBug()
        {
            bool ready = flow.BugReady;
            var b = Named(Kit.Button(stage.T(ready ? "screen.bug.button" : "screen.bug.button.pc"), KitButtonKind.Normal, () => OpenBug()), "board-menu-bug");
            if (!ready) b.AddToClassList(UiStage.OffClass);
            stage.Tips.Hook(b, null, stage.T(ready ? "screen.bug.button.tip" : "screen.bug.button.pc.tip"), TipSide.Below, reading: true);
            panel.Add(b);
        }

        /// <summary>[버그 신고]: 메뉴 판을 신고 쪽으로(보낼 창구가 없거나 나가기를 묻는 중이면 false).</summary>
        public bool OpenBug()
        {
            if (!flow.BugReady || Asking || Page == BugPage) return false;
            bug = new BugReportPanel(flow, CloseBug);
            Page = BugPage;
            stage.Tips.Hide();
            Fill();
            return true;
        }

        /// <summary>신고 쪽의 [취소] · [닫기] · Esc: 메뉴로(보내는 중이면 뒤에서 마저 보낸다).</summary>
        public void CloseBug()
        {
            if (Page != BugPage) return;
            bug?.Close();
            bug = null;
            Page = MainPage;
            Fill();
        }

        // ── 무르기 · 되돌리기(결정 149 · 154 — undo.md §3-2): 「무르기」 줄 = 보드 HUD [무르기]와 같은 함수(배틀 중에도) · 「되돌리기」 = 지난 턴 목록 쪽 ──

        /// <summary>되돌리기 쪽 한 쪽의 줄 수.</summary>
        public const int RewindRows = 10;

        /// <summary>되돌리기 쪽에서 확인을 묻는 줄(없으면 null).</summary>
        public UndoPoint RewindAsking { get; private set; }

        /// <summary>되돌리기 쪽의 지금 쪽 번호(0부터).</summary>
        public int RewindShownPage { get; private set; }

        /// <summary>되돌리기 쪽에 그린 목록(받기 전이면 null).</summary>
        public List<UndoPoint> RewindPoints { get; private set; }

        // 무르기를 켠 판의 메뉴 줄 둘: 「무르기」(못 무르면 흐린 단추 + 까닭) · 「되돌리기」(자유 + 솔로 플레이어 · 방장만)
        void AddUndo()
        {
            if (!flow.UndoShown) return;
            bool ready = flow.UndoReady;
            var u = Named(Kit.Button(stage.T("screen.board.undo"), KitButtonKind.Normal, () => PressUndo()), "board-menu-undo");
            if (!ready) u.AddToClassList(UiStage.OffClass);
            stage.Tips.Hook(u, null, ready ? stage.T("screen.board.undo.tip") : flow.UndoBlockText, TipSide.Below, reading: true);
            panel.Add(u);
            if (!flow.RewindShown) return;
            var r = Named(Kit.Button(stage.T("screen.menu.rewind"), KitButtonKind.Normal, () => OpenRewind()), "board-menu-rewind");
            stage.Tips.Hook(r, null, stage.T("screen.menu.rewind.tip"), TipSide.Below, reading: true);
            panel.Add(r);
        }

        /// <summary>메뉴 「무르기」: 메뉴를 닫고 한 수 무른다(<see cref="GameFlow.PressUndo"/>). 못 무르면 아무 일도 없다(false).</summary>
        public bool PressUndo()
        {
            if (!flow.UndoReady || Asking) return false;
            flow.CloseMenu();
            return flow.PressUndo();
        }

        /// <summary>메뉴 「되돌리기」: 메뉴 판을 지난 턴 목록 쪽으로(목록은 <see cref="GameFlow.UndoPoints"/>).</summary>
        public bool OpenRewind()
        {
            if (!flow.RewindShown || Asking || Page == RewindPage) return false;
            bug?.Close();
            bug = null;
            Page = RewindPage;
            RewindAsking = null;
            RewindShownPage = 0;
            stage.Tips.Hide();
            pointsFor = flow.Undo;
            flow.RequestUndoPoints();   // 방: 방장 세션에 목록을 달라고 한다 — 받기 전엔 「불러오는 중」, 받으면 SyncRoom이 다시 그린다
            Fill();
            return true;
        }

        /// <summary>되돌리기 쪽의 [뒤로] · Esc: 메뉴로.</summary>
        public void CloseRewind()
        {
            if (Page != RewindPage) return;
            RewindAsking = null;
            Page = MainPage;
            stage.Tips.Hide();
            Fill();
        }

        /// <summary>목록의 줄(전체 목록의 색인)의 [되돌리기]: 확인을 묻는다.</summary>
        public bool AskRewind(int index)
        {
            if (Page != RewindPage || RewindPoints == null || index < 0 || index >= RewindPoints.Count) return false;
            RewindAsking = RewindPoints[index];
            stage.Tips.Hide();
            Fill();
            return true;
        }

        /// <summary>확인 물음의 [되돌리기]: 메뉴를 닫고 그 턴 시작으로 되돌린다(<see cref="GameFlow.RewindTo"/>).</summary>
        public bool ConfirmRewind()
        {
            var point = RewindAsking;
            if (Page != RewindPage || point == null) return false;
            flow.CloseMenu();
            return flow.RewindTo(point.To);
        }

        /// <summary>확인 물음의 [취소] · Esc: 목록으로.</summary>
        public void CancelRewind()
        {
            if (RewindAsking == null) return;
            RewindAsking = null;
            Fill();
        }

        /// <summary>목록 쪽 넘김(앞 −1 · 뒤 +1). 넘겼으면 true.</summary>
        public bool TurnRewindPage(int delta)
        {
            if (Page != RewindPage || RewindPoints == null) return false;
            int pages = (RewindPoints.Count + RewindRows - 1) / RewindRows;
            int next = RewindShownPage + delta;
            if (next < 0 || next >= pages) return false;
            RewindShownPage = next;
            Fill();
            return true;
        }

        /// <summary>목록 줄의 머리 「n라운드 · ○○의 턴」.</summary>
        public string RewindRowText(UndoPoint p) =>
            stage.T("screen.menu.rewind.row").Replace("{round}", p.Round.ToString(CultureInfo.InvariantCulture)).Replace("{name}", flow.SeatName(p.Seat));

        /// <summary>그 턴에 한 일(max개까지 + 넘치면 「외 n」 — max &lt; 0이면 모두). 없으면 빈 글.</summary>
        public string RewindDeeds(UndoPoint p, int max)
        {
            var parts = new List<string>();
            int n = p.Deeds != null ? p.Deeds.Count : 0;
            int shown = max < 0 ? n : Math.Min(n, max);
            for (int i = 0; i < shown; i++) parts.Add(DeedText(p.Deeds[i]));
            string s = string.Join(" · ", parts);
            if (shown < n) s += " " + stage.T("screen.menu.rewind.more").Replace("{n}", (n - shown).ToString(CultureInfo.InvariantCulture));
            return s;
        }

        string DeedText(UndoDeed d)
        {
            string s = stage.T(UndoRules.DeedKey(d.Kind));
            var mons = flow.Ctx != null ? flow.Ctx.Cards.Pokemon : null;
            return s.Replace("{mon}", d.Def >= 0 && mons != null && d.Def < mons.Count ? stage.T("pokemon." + mons[d.Def].Key) : "");
        }

        void FillRewind()
        {
            panel.Add(Class(Kit.Text(stage.T("screen.menu.rewind.title"), Kit.FTitle), "kit-title"));
            var point = RewindAsking;
            if (point != null)
            {
                // 확인 물음: 「n라운드 ○○의 턴 처음으로 되돌리겠습니까?」 + 「그 뒤의 진행은 모두 사라집니다.」
                var ask = Named(new VisualElement(), "board-menu-rewind-ask");
                ask.Add(Kit.Wrapped(stage.T("screen.menu.rewind.ask").Replace("{round}", point.Round.ToString(CultureInfo.InvariantCulture)).Replace("{name}", flow.SeatName(point.Seat)), Kit.F2));
                ask.Add(Class(Kit.Wrapped(stage.T("screen.menu.rewind.ask.note"), Kit.F2), "kit-muted"));
                panel.Add(ask);
                var foot = Class(new VisualElement(), "sn-foot-row");
                foot.Add(Named(Kit.Button(stage.T("screen.menu.rewind.ok"), KitButtonKind.End, () => ConfirmRewind()), "board-menu-rewind-yes"));
                foot.Add(Named(Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, CancelRewind), "board-menu-rewind-no"));
                panel.Add(foot);
                return;
            }
            RewindPoints = flow.UndoPoints();
            if (RewindPoints == null) panel.Add(Named(Class(Kit.Text(stage.T("screen.menu.rewind.loading"), Kit.F2), "kit-muted"), "board-menu-rewind-loading"));
            else if (RewindPoints.Count == 0) panel.Add(Named(Class(Kit.Wrapped(stage.T("screen.menu.rewind.empty"), Kit.F2), "kit-muted"), "board-menu-rewind-empty"));
            else
            {
                int pages = (RewindPoints.Count + RewindRows - 1) / RewindRows;
                RewindShownPage = Math.Max(0, Math.Min(RewindShownPage, pages - 1));
                var list = Named(Class(new VisualElement(), "sn-menu-rewind-list"), "board-menu-rewind-list");
                int from = RewindShownPage * RewindRows, to = Math.Min(RewindPoints.Count, from + RewindRows);
                for (int i = from; i < to; i++)
                {
                    int index = i;
                    var p = RewindPoints[i];
                    string name = "board-menu-rewind-row-" + i.ToString(CultureInfo.InvariantCulture);
                    var row = Named(Class(new VisualElement(), "sn-menu-rewind-row"), name);
                    var face = Named(FaceBox.Make(), name + "-face");
                    FaceBox.Paint(face, p.Seat, flow.SeatColor(p.Seat), flow.Protagonist(p.Seat));
                    row.Add(face);
                    var text = Class(new VisualElement(), "sn-menu-rewind-text");
                    string head = RewindRowText(p);
                    text.Add(Named(Class(Kit.Text(head, Kit.F2), "sn-menu-rewind-head"), name + "-head"));
                    string deeds = RewindDeeds(p, 3);
                    if (deeds.Length > 0) text.Add(Named(Class(Class(Kit.Text(deeds, Kit.F2), "kit-muted"), "sn-menu-rewind-deeds"), name + "-deeds"));
                    row.Add(text);
                    if (p.Current) row.Add(Named(Class(Kit.Badge(stage.T("screen.menu.rewind.now"), KitBadgeKind.Gold, Kit.F2), "sn-menu-rewind-now"), name + "-now"));
                    row.Add(Named(Kit.Button(stage.T("screen.menu.rewind.ok"), KitButtonKind.Normal, () => AskRewind(index)), name + "-go"));
                    string all = RewindDeeds(p, -1);
                    stage.Tips.Hook(text, head, all.Length > 0 ? all : null, TipSide.Below, reading: true);
                    list.Add(row);
                }
                panel.Add(list);
                if (pages > 1)
                {
                    var pager = Named(Class(new VisualElement(), "sn-menu-rewind-pager"), "board-menu-rewind-pager");
                    var prev = Named(Kit.Button(stage.T("screen.menu.rewind.prev"), KitButtonKind.Normal, () => TurnRewindPage(-1)), "board-menu-rewind-prev");
                    prev.SetEnabled(RewindShownPage > 0);
                    pager.Add(prev);
                    pager.Add(Named(Kit.Text(stage.T("screen.menu.rewind.page").Replace("{n}", (RewindShownPage + 1).ToString(CultureInfo.InvariantCulture)).Replace("{max}", pages.ToString(CultureInfo.InvariantCulture)), Kit.F2), "board-menu-rewind-page"));
                    var next = Named(Kit.Button(stage.T("screen.menu.rewind.next"), KitButtonKind.Normal, () => TurnRewindPage(1)), "board-menu-rewind-next");
                    next.SetEnabled(RewindShownPage < pages - 1);
                    pager.Add(next);
                    panel.Add(pager);
                }
            }
            panel.Add(Named(Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, CloseRewind), "board-menu-rewind-back"));
        }

        void FillRoom()
        {
            var live = Live;
            if (Asking)
            {
                panel.Add(Class(Kit.Text(stage.T("screen.board.menu.title"), Kit.FTitle), "kit-title"));
                var ask = Named(new VisualElement(), "board-menu-ask");
                ask.Add(Kit.Text(stage.T(live.IsHost ? "screen.room.menu.close.ask" : "screen.room.menu.leave.ask"), Kit.F2));
                ask.Add(Class(Kit.Wrapped(stage.T(QuitNoteKey), Kit.F2), "kit-muted"));
                panel.Add(ask);
                var row = Class(new VisualElement(), "sn-foot-row");
                row.Add(Named(Kit.Button(stage.T(live.IsHost ? "screen.room.lobby.close" : "screen.room.lobby.leave"), KitButtonKind.End, Quit), "board-menu-quit-yes"));
                row.Add(Named(Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, Cancel), "board-menu-quit-no"));
                panel.Add(row);
                return;
            }
            if (Page == VotePage)
            {
                FillVote(live);
                return;
            }
            if (Page == SetPage)
            {
                panel.Add(Class(Kit.Text(stage.T("screen.room.menu.room_set"), Kit.FTitle), "kit-title"));
                var box = Named(new VisualElement(), "board-menu-room-set-panel");
                setPanel ??= new RoomSettingsPanel(stage, live);
                setShown = live.Room;
                setPanel.Build(box, live.Room, liveOnly: true);
                box.Q("room-set-head")?.RemoveFromHierarchy();   // 제목은 메뉴 제목 줄
                panel.Add(box);
                panel.Add(Named(Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => Show(MainPage)), "board-menu-back"));
                return;
            }
            panel.Add(Class(Kit.Text(stage.T("screen.board.menu.title"), Kit.FTitle), "kit-title"));
            // 저장 줄: 손님 = 방장 PC에 저장 · 방장 = 자동 저장됨(시각)/저장하지 못했습니다(SaveWriter.Problems)/저장하지 않는 게임
            string saveTip = null;
            bool warn = false;
            if (!live.IsHost)
            {
                SaveText = stage.T("screen.room.menu.save.guest");
                saveTip = stage.T("screen.room.menu.save.guest.tip");
            }
            else if (!live.Saving) SaveText = stage.T("screen.board.menu.save.off");
            else if (live.SaveProblems.Count > 0)
            {
                SaveText = stage.T("screen.board.menu.save.failed");
                saveTip = stage.T("screen.room.menu.save.failed.tip");
                warn = true;
            }
            else
            {
                SaveText = live.SavedAtMs > 0 ? stage.T("screen.board.menu.save.saved").Replace("{time}", GameFlow.SaveTime(live.SavedAtMs, date: false)) : stage.T("screen.room.menu.save.on");
                saveTip = stage.T("screen.room.menu.save.tip");
            }
            var save = Named(Class(Kit.Text(SaveText, Kit.F2), warn ? "sn-menu-warn" : "kit-muted"), "board-menu-save");
            if (saveTip != null) stage.Tips.Hook(save, null, saveTip, TipSide.Below, reading: true);
            panel.Add(save);
            // 핑 찍기 안내(결정 97, 02 §4-8)
            var ping = Named(Class(Kit.Wrapped(stage.T("screen.room.menu.ping"), Kit.F2), "kit-muted"), "board-menu-ping");   // 감정 표현(내 칩 오른쪽 클릭 — 결정 112 Q12)까지 한 줄에 다 들지 않으면 두 줄
            stage.Tips.Hook(ping, null, stage.T("screen.room.menu.ping.tip"), TipSide.Below, reading: true);
            panel.Add(ping);
            panel.Add(Named(Kit.Button(stage.T("screen.board.menu.resume"), KitButtonKind.Main, Resume), "board-menu-resume"));
            AddUndo();
            // 일시정지(결정 112 Q6 — isekai 규칙: 방 설정 「방장만 · 모두 · 투표」, 내 정지 → [일시정지 풀기] · 내 예약 · 요청 → [예약 취소], 방장은 남의 것이 있으면 [모두 해제])
            var p = Named(Kit.Button(stage.T(PauseKey(live)), KitButtonKind.Normal, () => TogglePause()), "board-menu-pause");
            p.SetEnabled(live.CanUnpause || live.PauseMine || live.CanPause);
            stage.Tips.Hook(p, null, stage.T(PauseTipKey(live)), TipSide.Below, reading: true);
            panel.Add(p);
            if (live.CanClearAll)
            {
                var all = Named(Kit.Button(stage.T("screen.room.pause.all"), KitButtonKind.Normal, () => ClearPauses()), "board-menu-pause-all");
                stage.Tips.Hook(all, null, stage.T("screen.room.pause.all.tip"), TipSide.Below, reading: true);
                panel.Add(all);
            }
            var v = Named(Kit.Button(stage.T("screen.room.menu.vote"), KitButtonKind.Normal, () => Show(VotePage)), "board-menu-vote");
            v.SetEnabled(live.Room != null && live.Room.Phase == RoomPhase.InGame && live.Room.Vote == null);
            stage.Tips.Hook(v, null, stage.T(live.Room != null && live.Room.Vote != null ? "screen.room.menu.vote.busy" : "screen.room.menu.vote.tip"), TipSide.Below, reading: true);
            panel.Add(v);
            panel.Add(Named(Kit.Button(stage.T("screen.room.menu.room_set"), KitButtonKind.Normal, () => Show(SetPage)), "board-menu-room-set"));
            panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulebook"), KitButtonKind.Normal, () => flow.OpenMenuRulebook()), "board-menu-rulebook"));   // 룰 설명서 S43(M9)
            panel.Add(Named(Kit.Button(stage.T("screen.board.menu.rulings"), KitButtonKind.Normal, () => flow.OpenMenuRulings()), "board-menu-rulings"));   // 규칙 판정(결정 213)
            panel.Add(Named(Kit.Button(stage.T("screen.board.menu.settings"), KitButtonKind.Normal, OpenSettings), "board-menu-settings"));
            AddBug();
            panel.Add(Named(Kit.Button(stage.T(live.IsHost ? "screen.room.lobby.close" : "screen.room.menu.leave"), KitButtonKind.Normal, AskQuit), "board-menu-quit"));
        }

        // 투표 열기 쪽(02 §6-2): 나 밖의 좌석마다 [강퇴 투표] · [자리 비움 투표] — 방장은 끊긴 좌석을 바로 [자리 비움으로](MarkAway, 투표 없음)
        void FillVote(RoomLive live)
        {
            panel.Add(Class(Kit.Text(stage.T("screen.room.menu.vote"), Kit.FTitle), "kit-title"));
            var room = live.Room;
            int n = room != null ? room.Seats.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var s = room.Seats[i];
                if (i == live.Seat || s == null || s.Kicked) continue;
                int seat = i;
                var row = Named(Class(new VisualElement(), "sn-menu-vote-row"), "board-menu-vote-" + i);
                var name = Class(Kit.Text(s.Nickname ?? flow.SeatName(i), Kit.F2), "sn-menu-vote-name");
                row.Add(name);
                if (!s.Connected || s.Away) row.Add(Class(Kit.Badge(stage.T(s.Away ? "screen.room.menu.vote.away_now" : "screen.room.menu.vote.offline"), KitBadgeKind.Plain), "sn-menu-vote-tag"));
                var kick = Named(Kit.Button(stage.T("screen.room.menu.vote.kick"), KitButtonKind.End, () => StartVote(seat, VoteKind.Kick)), "board-menu-vote-kick-" + i);
                kick.SetEnabled(!s.Host);
                stage.Tips.Hook(kick, null, stage.T(s.Host ? "screen.room.menu.vote.kick.host" : "screen.room.vote.kick.tip"), TipSide.Below, reading: true);
                row.Add(kick);
                if (live.IsHost && !s.Connected && !s.Away)
                {
                    var mark = Named(Kit.Button(stage.T("screen.room.menu.vote.mark_away"), KitButtonKind.Normal, () => MarkAway(seat)), "board-menu-vote-mark-" + i);
                    stage.Tips.Hook(mark, null, stage.T("screen.room.menu.vote.mark_away.tip"), TipSide.Below, reading: true);
                    row.Add(mark);
                }
                else
                {
                    var away = Named(Kit.Button(stage.T("screen.room.menu.vote.away"), KitButtonKind.Normal, () => StartVote(seat, VoteKind.Away)), "board-menu-vote-away-" + i);
                    away.SetEnabled(s.Connected && !s.Away);
                    stage.Tips.Hook(away, null, stage.T("screen.room.vote.away.tip"), TipSide.Below, reading: true);
                    row.Add(away);
                }
                panel.Add(row);
            }
            panel.Add(Named(Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => Show(MainPage)), "board-menu-back"));
        }

        /// <summary>방 판 메뉴의 쪽을 바꾼다(메뉴 · 투표 · 방 설정).</summary>
        public void Show(int page)
        {
            if (Live == null || page == BugPage || page == RewindPage) return;
            bug?.Close();
            bug = null;
            Page = page;
            Asking = false;
            Fill();
        }

        /// <summary>일시정지 단추의 글 키: 내 정지 → 풀기 · 내 예약 → 예약 취소 · 내 요청(투표) → 요청 취소 · 「투표」 방 → 일시정지 투표 · 그 밖 → 일시정지.</summary>
        public static string PauseKey(RoomLive live) =>
            live.CanUnpause ? "screen.room.pause.off"
            : live.PauseReserved.Contains(live.Seat) ? "screen.room.pause.cancel"
            : live.PauseMine ? "screen.room.pause.withdraw"
            : live.PauseMode == PauseMode.Vote && !live.Paused ? "screen.room.menu.pause.vote" : "screen.room.menu.pause";

        static string PauseTipKey(RoomLive live) =>
            live.CanUnpause || live.PauseMine ? "screen.room.menu.pause.mine"
            : live.PauseMode == PauseMode.HostOnly ? (live.IsHost ? "screen.room.menu.pause.host" : "screen.room.menu.pause.host_only")
            : live.PauseMode == PauseMode.Everyone ? "screen.room.menu.pause.everyone"
            : live.Room?.Vote != null && !live.Paused ? "screen.room.menu.vote.busy" : "screen.room.menu.pause.vote.tip";

        /// <summary>[일시정지] · [일시정지 투표] · [일시정지 풀기] · [예약 취소] · [요청 취소](방 판): 걸기 또는 내 것 풀기를 보내고 메뉴를 닫는다. 보냈으면 true.</summary>
        public bool TogglePause()
        {
            var live = Live;
            if (live == null) return false;
            bool off = live.CanUnpause || live.PauseMine;
            if (!off && !live.CanPause) return false;
            live.Send(new SetPaused { On = !off });
            flow.CloseMenu();
            return true;
        }

        /// <summary>방장의 [모두 해제]: 정지와 예약을 모두 푼다. 보냈으면 true.</summary>
        public bool ClearPauses()
        {
            var live = Live;
            if (live == null || !live.IsHost) return false;
            live.Send(new SetPaused { All = true });
            flow.CloseMenu();
            return true;
        }

        /// <summary>투표를 연다(나는 찬성으로 친다 — 02 §6-2): 메뉴를 닫아 투표 판이 보이게 한다. 보냈으면 true.</summary>
        public bool StartVote(int seat, VoteKind kind)
        {
            var live = Live;
            if (live == null || seat == live.Seat) return false;
            live.Send(new Sinnoh.Rules.Session.StartVote { Target = seat, Kind = kind });
            flow.CloseMenu();
            return true;
        }

        /// <summary>방장: 끊긴 좌석을 바로 자리 비움으로(02 §6-2 — 투표 없음). 보냈으면 true.</summary>
        public bool MarkAway(int seat)
        {
            var live = Live;
            if (live == null || !live.IsHost) return false;
            live.Send(new Sinnoh.Rules.Session.MarkAway { Seat = seat });
            Show(VotePage);
            return true;
        }

        /// <summary>[게임으로 돌아가기]: 메뉴를 닫는다.</summary>
        public void Resume() => flow.CloseMenu();

        /// <summary>Esc(M9 다듬기 P23): 한 단계만 — 나가기 물음이면 [취소], 되돌리기 확인 물음이면 그 쪽으로, 안쪽 쪽(되돌리기 · 버그 신고 · 방 판의 투표 · 방 설정)이면 메뉴로, 메뉴면 [게임으로 돌아가기].</summary>
        public void Escape()
        {
            if (Asking) Cancel();
            else if (Page == RewindPage && RewindAsking != null) CancelRewind();
            else if (Page == RewindPage) CloseRewind();
            else if (Page == BugPage) CloseBug();
            else if (Live != null && Page != MainPage) Show(MainPage);
            else Resume();
        }

        /// <summary>[설정]: 설정 화면을 이 자리에 연다(<see cref="GameFlow.OpenMenuSettings"/> — [뒤로]는 이 메뉴로).</summary>
        public void OpenSettings() => flow.OpenMenuSettings();

        /// <summary>[타이틀로 나가기]: 나가기 전에 한 번 묻는다. 튜토리얼 판의 [튜토리얼 끝내기]는 이 메뉴를 닫고 끝내기 창(<see cref="TutorialMenu.OpenQuit"/>)을 연다.</summary>
        public void AskQuit()
        {
            if (flow.Tutorial.Running)
            {
                flow.Tutorial.OpenQuit();
                return;
            }
            Asking = true;
            Fill();
        }

        /// <summary>[취소]: 묻기를 닫고 메뉴로.</summary>
        public void Cancel()
        {
            Asking = false;
            Fill();
        }

        /// <summary>[나가기]: 타이틀로(저장하는 판은 그때까지가 이미 저장돼 있다 — 타이틀의 「이어하기」가 켜진다). 방 판은 방에서 나가기 · 방장은 방 닫기(마지막 쓰기 — 02 §6-3).</summary>
        public void Quit()
        {
            if (Live != null) flow.Room.Leave();
            else flow.OpenTitle();
        }

        static T Named<T>(T e, string name) where T : VisualElement
        {
            e.name = name;
            return e;
        }

        static T Class<T>(T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }
    }
}
