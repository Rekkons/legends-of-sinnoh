using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 무르기 · 되돌리기의 방 화면(결정 149 · 154 — Docs/Design/undo.md §2 · §3, 조각 D2): 2좌석 방을 실제 루프백(직접 연결 127.0.0.1 · NGO 둘)으로 —
    /// 이 흐름(방장 화면 — 타이틀 「방 만들기」로 연 방)과 다른 NetworkManager의 손님(<see cref="NetRoom.Join"/>, 화면 없이 명령만).
    /// 로비 방 설정의 「무르기」 줄(새 방 기본 끔 · 로비에서만) → 손님 무르기 = 방장 화면이 되돌림 표지를 받아 다시 서고 기록 줄 · 알림 띠, 손님 보기도 되돌린 판 ·
    /// 남의 턴에 보낸 무르기는 UNDO_DENIED 알림 · 일시정지 중 [무르기]가 꺼짐(받은 시계로) · 방장 「되돌리기」 쪽(목록을 달라고 보냄 → 받은 목록 → 확인 → 두 화면 모두 그 턴 처음, 정지는 남음) ·
    /// 끈 방에는 단추 · 메뉴 줄이 없고 게임 중 방 설정에도 줄이 없다. 검토 반영: 남의 무르기는 내 게임 메뉴 · 쓰던 버그 신고 글을 걷지 않고 도장 소리도 없다 ·
    /// 열린 「되돌리기」 쪽은 판이 흐르면 목록을 다시 받는다 · [무르기] 두 번 누름은 한 번만 보낸다. 실제 UGS를 부르지 않는다. Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RoomUndoFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class VClock
        {
            public long Now;
        }

        readonly List<string> errors = new List<string>();
        readonly List<NetRoom> guests = new List<NetRoom>();
        ScreenBoot boot;
        RenderTexture rt;
        VClock vc;
        GameFlow flow;
        NetRoom guest;
        HostSession hosted;
        int guestSent = -1, guestWait, guestRewinds;

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        IEnumerator Boot()
        {
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            flow = boot.Flow;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();   // 방 닫기
            foreach (var g in guests) g.Dispose();
            guests.Clear();
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
            Application.logMessageReceived -= Grab;
            if (rt != null)
            {
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
            boot = null;
            flow = null;
            guest = null;
            hosted = null;
            guestSent = -1;
            guestWait = guestRewinds = 0;
            errors.Clear();
        }

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M10_무르기_방_손님무르기와방장되돌리기는두화면을되돌리고알림띠_남의턴무르기는거절_일시정지중에는단추가꺼진다()
        {
            yield return Boot();
            yield return OpenRoom(47881, UndoMode.Free);
            Assert.That(flow.Undo, Is.Not.Null, "무르기를 켠 방은 제안이 온다");
            Assert.That(flow.Undo.HostList, Is.True, "방장은 되돌리기 목록을 연다");
            // 차례 순서는 셋업이 정한다 — 먼저 온 쪽부터
            yield return Until(() =>
            {
                Pump();
                return HostMap() || (GuestMap() != null && hosted.HoldLeftMs(1) == 0 && flow.BoardOpen);
            }, 60, "첫 턴");
            if (HostMap())
            {
                yield return HostTurn();
                yield return GuestTurn();
            }
            else
            {
                yield return GuestTurn();
                yield return HostTurn();
            }
            Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest, NUnit.Framework.Timeout(180000)]
        public IEnumerator M10_무르기_방_끈방에는단추와메뉴줄이없고_게임중방설정에도줄이없다()
        {
            yield return Boot();
            yield return OpenRoom(47882, UndoMode.Off);
            yield return Until(() =>
            {
                Pump();
                return HostMap() || (GuestMap() != null && flow.BoardOpen);
            }, 60, "첫 턴");
            var root = flow.Stage.Root;
            Assert.That(flow.Undo, Is.Null, "끈 방은 제안이 없다(갱신의 Undo = null)");
            Assert.That(flow.UndoShown, Is.False);
            Assert.That(root.Q(BoardHud.UndoButton), Is.Null, "[무르기]가 없다");
            flow.OpenMenu();
            yield return null;
            var menu = (BoardMenuScreen)flow.Stage.Current;
            Assert.That(root.Q("board-menu-undo"), Is.Null);
            Assert.That(root.Q("board-menu-rewind"), Is.Null);
            // 게임 메뉴의 방 설정 쪽(게임 중에 바꾸는 줄만)에는 「무르기」가 없고, 게임 중에 바꾸려 해도 방이 옮기지 않는다(로비에서만 — 결정 154 ②)
            menu.Show(BoardMenuScreen.SetPage);
            yield return null;
            Assert.That(root.Q("board-menu-room-set-panel"), Is.Not.Null);
            Assert.That(root.Q("room-set-row-" + RoomSettingsPanel.Undo), Is.Null, "게임 중 방 설정에 「무르기」 줄이 없다");
            var on = hosted.Room.Settings.Clone();
            on.Undo = UndoMode.Free;
            flow.Room.Live.Send(new ChangeSettings { Settings = on });
            for (int i = 0; i < 30; i++)
            {
                Pump();
                yield return null;
            }
            Assert.That(hosted.Room.Settings.Undo, Is.EqualTo(UndoMode.Off), "게임 중에는 무르기 설정이 바뀌지 않는다");
            Assert.That(flow.Undo, Is.Null);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // ── 방장 차례: 걷기 → 일시정지 = [무르기] 꺼짐 → 「되돌리기」 쪽(일시정지 중에도) → 두 화면 모두 이 턴 처음 → 풀고 턴 종료 ──

        IEnumerator HostTurn()
        {
            var live = flow.Room.Live;
            var stage = flow.Stage;
            var root = stage.Root;
            yield return Until(() =>
            {
                Pump();
                return HostMap() && flow.Undo != null && flow.Undo.Block == UndoBlock.TurnStart;
            }, 30, "방장 차례");
            Assert.That((flow.Undo.StepTo, flow.Undo.Block), Is.EqualTo((-1, UndoBlock.TurnStart)), "방장 턴 처음");
            int start = Cell(0), before = hosted.Game.CommandCount;
            ulong startHash = hosted.Game.Hash;
            int cell = CleanCell();
            Assert.That(flow.Hud.Walk(cell), Is.True, "칸 " + cell + "으로 걷기");
            yield return Until(() =>
            {
                Pump();
                return HostMap() && hosted.Game.CommandCount > before && flow.Undo != null && flow.Undo.StepTo >= 0;
            }, 20, "걸은 뒤 무를 수 있음");
            Assert.That(flow.Undo.StepTo, Is.EqualTo(before), "한 수 = 걷기 전 쉬는 때");
            Assert.That(flow.UndoOpen, Is.True);

            // 일시정지: 제안은 다음 갱신에야 Paused로 오니 화면이 받은 시계를 함께 본다 — 흐린 단추 + 까닭 툴팁, 누르기는 아무 일도 없다
            live.Send(new SetPaused { On = true });
            yield return Until(() =>
            {
                Pump();
                return live.Paused;
            }, 10, "일시정지");
            yield return null;
            yield return null;
            Assert.That(flow.UndoReady, Is.False, "일시정지 중에는 무르지 못한다");
            var undo = root.Q<Button>(BoardHud.UndoButton);
            Assert.That(undo, Is.Not.Null);
            Assert.That(undo.ClassListContains(UiStage.OffClass), Is.True, "흐린 단추");
            Assert.That(TipOf(stage, undo), Is.EqualTo(stage.T("screen.board.undo.block.paused").Replace("⁠", "")));
            stage.Tips.Hide();
            Assert.That(flow.PressUndo(), Is.False);
            Assert.That(hosted.Game.CommandCount, Is.GreaterThan(before), "판은 그대로");

            // 방장 「되돌리기」(일시정지 중에도 — [가정 U7]): 쪽을 열면 목록을 달라고 보내고 「불러오는 중」 → 받은 목록 → 지금 턴 줄 → 확인 → 두 화면 모두 이 턴 처음
            flow.OpenMenu();
            yield return null;
            var menu = (BoardMenuScreen)stage.Current;
            Assert.That(root.Q("board-menu-rewind"), Is.Not.Null, "자유 방의 방장 메뉴에 「되돌리기」");
            Assert.That(menu.OpenRewind(), Is.True);
            Assert.That(menu.RewindPoints, Is.Null, "받기 전");
            Assert.That(root.Q("board-menu-rewind-loading"), Is.Not.Null, "「불러오는 중」");
            yield return Until(() =>
            {
                Pump();
                return menu.RewindPoints != null;
            }, 10, "방장 세션이 보낸 목록");
            var points = menu.RewindPoints;
            Assert.That(points.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That((points[0].Current, points[0].To, points[0].Seat), Is.EqualTo((true, before, 0)), "맨 위 = 지금 턴(이 턴 처음)");
            Assert.That(root.Q("board-menu-rewind-row-0-now"), Is.Not.Null, "「지금」 꼬리표");
            Assert.That(root.Q("board-menu-rewind-loading"), Is.Null);
            Assert.That(menu.AskRewind(0), Is.True);
            yield return null;
            int epoch = hosted.Game.Epoch, saw = guestRewinds;
            var oldHud = flow.Hud;
            Assert.That(menu.ConfirmRewind(), Is.True);
            yield return Until(() =>
            {
                Pump();
                return hosted.Game.Epoch == epoch + 1 && flow.Client.LastRewind != null && flow.Client.LastRewind.Epoch == epoch + 1 && guestRewinds > saw && flow.BoardOpen;
            }, 15, "방장 되돌리기를 두 화면이 받는다");
            Assert.That(flow.MenuUp, Is.False, "확정하면 메뉴가 닫힌다");
            Assert.That((hosted.Game.CommandCount, hosted.Game.Hash), Is.EqualTo((before, startHash)), "이 턴 처음 상태 그대로");
            Assert.That(flow.Client.LastRewind.Kind, Is.EqualTo(RewindKind.Host));
            Assert.That(Cell(0), Is.EqualTo(start), "방장 화면의 말 칸");
            Assert.That(Piece(0), Is.EqualTo(start), "보드의 말이 제자리에 다시 섰다");
            Assert.That(flow.Hud, Is.Not.Null.And.Not.SameAs(oldHud), "HUD를 새로 세웠다");
            Assert.That(LastLine().Line.Key, Is.EqualTo(Sinnoh.Fx.BoardLog.K + "rewind"), "기록 줄 「○○은(는) n라운드로 되돌렸다」");
            string round = flow.Client.LastRewind.Round.ToString(System.Globalization.CultureInfo.InvariantCulture);
            yield return Until(() => Notices().Contains(stage.T("screen.room.notice.rewound").Replace("{name}", "방장").Replace("{round}", round).Replace("⁠", "")), 5, "알림 띠 「방장님이 n라운드로 되돌렸습니다.」");
            yield return GuestSees();
            Assert.That(live.Paused, Is.True, "방장 되돌리기는 일시정지를 풀지 않는다");
            Assert.That(flow.UndoReady, Is.False);

            // 풀고 턴 종료
            live.Send(new SetPaused { On = false });
            yield return Until(() =>
            {
                Pump();
                return !live.Paused && HostMap();
            }, 10, "일시정지 풀기");

            // 두 번 누름(HUD · Ctrl+Z 연타): 보낸 뒤 그 Epoch의 되돌림이 올 때까지 다시 보내지 않는다 — 방장이 둘째를 낡은 To로 거절해 「지금은 무를 수 없습니다」가 함께 뜨지 않게
            int walkAt = hosted.Game.CommandCount;
            Assert.That(flow.Hud.Walk(CleanCell()), Is.True, "다시 걷기");
            yield return Until(() =>
            {
                Pump();
                return HostMap() && hosted.Game.CommandCount > walkAt && flow.UndoOpen;
            }, 20, "다시 걸은 뒤 무를 수 있음");
            int epoch2 = hosted.Game.Epoch;
            long pressedAt = live.NowMs;
            Assert.That(flow.PressUndo(), Is.True, "첫 누름");
            Assert.That(flow.UndoWaiting, Is.True, "답을 기다린다");
            Assert.That(flow.UndoOpen, Is.False, "[무르기]가 꺼진다");
            Assert.That(flow.PressUndo(), Is.False, "둘째 누름은 보내지 않는다");
            Assert.That(flow.UndoKey(), Is.False, "Ctrl+Z도");
            yield return Until(() =>
            {
                Pump();
                return hosted.Game.Epoch == epoch2 + 1 && flow.Client.LastRewind != null && flow.Client.LastRewind.Epoch == epoch2 + 1 && HostMap();
            }, 15, "무르기를 받는다");
            Assert.That(flow.UndoWaiting, Is.False, "되돌림이 오면 기다림이 풀린다");
            Assert.That(hosted.Game.CommandCount, Is.EqualTo(walkAt), "한 수만 물렀다");
            for (int i = 0; i < 10; i++)
            {
                Pump();
                yield return null;
            }
            Assert.That(live.Notices.Count(n => n.Code == NetCode.UndoDenied && n.AtMs >= pressedAt), Is.EqualTo(0), "거절 알림이 없다");
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료");
            int ended = hosted.Game.CommandCount;
            yield return Until(() =>
            {
                Pump();
                return hosted.Game.CommandCount > ended && !HostMap();
            }, 15, "방장 턴 종료");
        }

        // ── 손님 차례: 방장 화면은 「내 턴에만」 · 방장이 보낸 무르기는 UNDO_DENIED → 손님 걷기 → 손님 무르기 = 두 화면 모두 걷기 전 · 알림 띠 → 손님 턴 종료(새 Epoch) ──

        IEnumerator GuestTurn()
        {
            var live = flow.Room.Live;
            var stage = flow.Stage;
            yield return Until(() =>
            {
                Pump();
                return GuestMap() != null && hosted.HoldLeftMs(1) == 0 && flow.BoardOpen && flow.Undo != null && flow.Undo.Block == UndoBlock.NotYourTurn;
            }, 30, "손님 차례");
            Assert.That(flow.UndoReady, Is.False);
            Assert.That(flow.UndoBlockText, Is.EqualTo(stage.T("screen.board.undo.block.not_your_turn")));
            int denied = live.Notices.Count(n => n.Code == NetCode.UndoDenied);
            live.Send(new UndoStep { To = 0 });   // 화면은 단추가 꺼져 보내지 않는다 — 남의 턴에 온 무르기는 방장이라도 거절
            yield return Until(() =>
            {
                Pump();
                return live.Notices.Count(n => n.Code == NetCode.UndoDenied) > denied;
            }, 10, "UNDO_DENIED");
            yield return Until(() => Notices().Contains(stage.T("screen.room.notice.undo_denied").Replace("⁠", "")), 5, "알림 띠 「지금은 무를 수 없습니다.」");

            // 손님 제안: 턴 처음 · 되돌리기 목록은 방장만
            var offer = guest.Seat.Latest.Undo;
            Assert.That(offer, Is.Not.Null);
            Assert.That((offer.StepTo, offer.Block, offer.HostList), Is.EqualTo((-1, UndoBlock.TurnStart, false)), "손님 턴 처음");
            var gv = guest.Seat.View;
            var gc = GuestMap();
            int start = gv.Seats[1].Cell, before = hosted.Game.CommandCount;
            ulong startHash = hosted.Game.Hash;

            // 방장은 남의 턴에 게임 메뉴 「되돌리기」 쪽을 열어 둔다 — 쪽이 열린 동안 판이 흐르면(손님 걷기) 목록을 다시 받는다
            flow.OpenMenu();
            yield return null;
            var menu = (BoardMenuScreen)stage.Current;
            Assert.That(menu.OpenRewind(), Is.True);
            yield return Until(() =>
            {
                Pump();
                return menu.RewindPoints != null;
            }, 10, "방장 세션이 보낸 목록");
            var listed = menu.RewindPoints;
            GuestSubmit(Command.AnswerTo(gc.Id, GuestMove(gv, gc)));
            yield return Until(() =>
            {
                Pump();
                var u = guest.Seat.Latest.Undo;
                return hosted.Game.CommandCount > before && u != null && u.StepTo >= 0 && GuestMap() != null && hosted.HoldLeftMs(1) == 0
                    && menu.RewindPoints != null && !ReferenceEquals(menu.RewindPoints, listed);
            }, 20, "손님 걷기 뒤 무를 수 있음 · 열린 되돌리기 쪽이 목록을 다시 받았다");
            Assert.That(guest.Seat.Latest.Undo.StepTo, Is.EqualTo(before), "한 수 = 걷기 전 쉬는 때");
            yield return Until(() =>
            {
                Pump();
                var p = menu.RewindPoints;
                return p != null && p.Count > 0 && p[0].Current && p[0].Seat == 1 && p[0].To == before;
            }, 10, "다시 받은 목록의 맨 위 = 손님의 지금 턴(「지금」 꼬리표)");
            Assert.That(stage.Root.Q("board-menu-rewind-row-0-now"), Is.Not.Null, "「지금」 꼬리표가 지금 턴 줄에");

            // 그 쪽을 닫고 버그 신고 글을 쓰는 중에 손님이 무른다 — 남의 무르기는 내 메뉴 · 쓰던 글을 걷지 않는다(판과 무관한 보기 창 — GameFlow.KeptOnRewind)
            menu.CloseRewind();
            flow.Uploads = PlayUploads.Quiet(() => false);   // 버그 신고 창구(에디터에는 없다 — 쪽을 여는 동안만)
            Assert.That(menu.OpenBug(), Is.True, "버그 신고 쪽");
            flow.Uploads = null;
            menu.Bug.SetText("남의 무르기 동안 쓰던 글");
            int marks = live.MarksSeen;
            int epoch = hosted.Game.Epoch, saw = guestRewinds;
            var oldHud = flow.Hud;
            guest.Send(new UndoStep { To = guest.Seat.Latest.Undo.StepTo });
            yield return Until(() =>
            {
                Pump();
                return hosted.Game.Epoch == epoch + 1 && guestRewinds > saw && flow.Client.LastRewind != null && flow.Client.LastRewind.Epoch == epoch + 1;
            }, 15, "손님 무르기를 두 화면이 받는다");
            Assert.That(stage.Current, Is.SameAs(menu), "게임 메뉴가 그대로");
            Assert.That(flow.MenuUp, Is.True);
            Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.BugPage), "버그 신고 쪽 그대로");
            Assert.That(menu.Bug.Text, Is.EqualTo("남의 무르기 동안 쓰던 글"), "쓰던 글 그대로");
            Assert.That(stage.Root.Q("bug-text"), Is.Not.Null);
            Assert.That(live.MarksSeen, Is.EqualTo(marks), "아무도 찍지 않은 도장 소리가 나지 않는다");
            menu.CloseBug();
            flow.CloseMenu();
            yield return Until(() =>
            {
                Pump();
                return flow.BoardOpen;
            }, 10, "메뉴를 닫으면 보드");
            Assert.That((hosted.Game.CommandCount, hosted.Game.Hash), Is.EqualTo((before, startHash)), "걷기 전 상태 그대로(Rng까지)");
            Assert.That(flow.Client.LastRewind.Kind, Is.EqualTo(RewindKind.Step));
            Assert.That(flow.Client.LastRewind.By, Is.EqualTo(1));
            Assert.That(Cell(1), Is.EqualTo(start), "방장 화면의 손님 말 칸");
            Assert.That(Piece(1), Is.EqualTo(start), "방장 화면 보드의 손님 말이 제자리에 다시 섰다");
            Assert.That(flow.Hud, Is.Not.Null.And.Not.SameAs(oldHud), "방장 화면도 HUD를 새로 세웠다");
            var last = LastLine();
            Assert.That((last.Line.Key, last.Seat), Is.EqualTo((Sinnoh.Fx.BoardLog.K + "undo", 1)), "기록 줄 「손님은(는) 무르기를 했다」");
            yield return Until(() => Notices().Contains(stage.T("screen.room.notice.undone").Replace("{name}", "손님").Replace("⁠", "")), 5, "알림 띠 「손님님이 무르기를 했습니다.」");
            yield return GuestSees();
            Assert.That(guest.Seat.View.Seats[1].Cell, Is.EqualTo(start), "손님 보기의 말 칸");

            // 손님 턴 종료 — 되돌린 뒤의 답은 새 Epoch를 싣는다(낡으면 Rewound로 거절)
            gc = GuestMap();
            Assert.That(gc, Is.Not.Null);
            int at = hosted.Game.CommandCount;
            GuestSubmit(Command.AnswerTo(gc.Id, ChoiceAnswer.Of(AnswerKind.EndTurn)));
            yield return Until(() =>
            {
                Pump();
                return hosted.Game.CommandCount > at && GuestMap() == null;
            }, 15, "손님 턴 종료");
        }

        // 손님 보기 = 방장이 준 손님 보기(되돌린 판 — 갱신이 닿을 때까지)
        IEnumerator GuestSees()
        {
            yield return Until(() =>
            {
                Pump();
                return guest.Seat.View != null && StateHash.Of(hosted.Game.ViewFor(1)) == StateHash.Of(guest.Seat.View);
            }, 10, "손님 보기 = 되돌린 판");
        }

        // ── 차림 ──

        // 타이틀 「방 만들기」 → 직접 연결 2자리 → 손님 참가 → (무르기 설정) → 준비 → [게임 시작] → 셋업을 지나 보드
        IEnumerator OpenRoom(ushort port, UndoMode undo)
        {
            vc = new VClock();
            var stage = flow.Stage;
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Board = RoomFlow.NoRooms;
            flow.Room.Clock = () => vc.Now;
            flow.Room.Create.GalacticAi = GalacticAiLevel.Principle;   // 강한 AI는 결정마다 수백 ms — 화면 시험은 원칙 AI로
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            create.SetInternet(false);
            create.SetSeats(2);
            create.SetNickname("방장");
            Assert.That(create.Start(), Is.True, "[방 만들기]");
            var lobby = (LobbyScreen)stage.Current;
            yield return null;
            Assert.That(stage.Root.Q("room-set-row-" + RoomSettingsPanel.Undo), Is.Not.Null, "방 설정의 「무르기」 줄");
            Assert.That(stage.Root.Q("room-set-opt-undo-off").ClassListContains("sn-opt-picked"), Is.True, "새 방 기본 = 사용 안 함(결정 154 ①)");
            Assert.That(flow.Room.Net.Room.Settings.Undo, Is.EqualTo(UndoMode.Off));
            Assert.That(JoinTarget.TryParse(Loopback + ":" + port, out var target), Is.True);
            guest = NetRoom.Join(target, flow.Room.HelloFor("guest-token", "손님"));
            guests.Add(guest);
            guest.Seat.Updated += u =>
            {
                if (u.Rewind == null) return;
                guestRewinds++;
                guestSent = -1;   // 되돌리면 선택 Id가 다시 쓰인다 — 보낸 답 기억을 비운다
            };
            yield return Until(() => guest.Seat.Status == NetStatus.Connected && guest.Room != null && guest.Room.Seats.Count(s => s != null) == 2, 15, "손님 접속");
            if (undo != UndoMode.Off)
            {
                Assert.That(lobby.Settings.Change(s => s.Undo = undo), Is.True, "방장이 「무르기」를 바꾼다");
                yield return Until(() => guest.Room.Settings.Undo == undo, 10, "방 설정이 손님에게");
                yield return null;
                string key = undo == UndoMode.Free ? "free" : "turn";
                Assert.That(stage.Root.Q("room-set-opt-undo-" + key).ClassListContains("sn-opt-picked"), Is.True);
            }
            guest.Send(new SetReady { Ready = true });
            yield return Until(() => stage.Root.Q<Button>("lobby-start") != null && stage.Root.Q<Button>("lobby-start").enabledSelf, 10, "손님 준비 → [게임 시작] 켜짐");
            Assert.That(lobby.Start(), Is.True, "[게임 시작]");
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            hosted = flow.Room.Net.Host.Host;
            Assert.That(hosted.Game.UndoMode, Is.EqualTo(undo), "방 설정이 핵의 무르기로");
            yield return Until(() =>
            {
                Pump();
                if (flow.Client.FirstChoice() != null && hosted.HoldLeftMs(0) == 0)
                {
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        village.Pick(0);
                        village.Confirm();
                    }
                    else if (stage.Current is SetupPickScreen pick && !pick.StarterSent)
                    {
                        pick.PickStarter(0);
                        pick.PickCard(0);
                        pick.Confirm();
                    }
                }
                return flow.OnBoard && flow.Hud != null;
            }, 90, "셋업 → 보드");
        }

        // 한 프레임: 손님은 보드 행동(맵 행동)이 아닌 자기 선택에 첫 합법 답 · 방 시각을 차단 컷신 · 연출 끝까지 · 화면 시각 200ms(RoomFlowTests 셋업 고리와 같은 꼴)
        void Pump()
        {
            var gv = guest.Seat.View;
            int me = guest.Seat.Seat;
            var gc = gv?.Pending.FirstOrDefault(c => c.Seat == me && !c.Answered && c.Kind != ChoiceKind.MapAction);
            if (gc != null && hosted != null && hosted.HoldLeftMs(me) == 0 && (gc.Id != guestSent || ++guestWait > 30))
            {
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(gv, flow.Ctx, gc, legal);
                if (legal.Count > 0) GuestSubmit(Command.AnswerTo(gc.Id, legal[0]));
                guestSent = gc.Id;
                guestWait = 0;
            }
            if (hosted != null)
            {
                long hold = 0;
                for (int s = 0; s < hosted.SeatCount; s++) hold = Math.Max(hold, hosted.HoldLeftMs(s));
                if (!hosted.AwaitingAi) vc.Now = Math.Max(vc.Now + 50 + hold, hosted.AnimEndMs);
            }
            flow.Skip(200);
        }

        // 손님의 명령: 받은 제안의 Epoch를 싣는다(되돌린 판에 낡은 답이 들어가지 않게 — 방이 거른다)
        void GuestSubmit(Command cmd) => guest.Send(new Submit { Command = cmd, Epoch = guest.Seat.Latest?.Undo?.Epoch ?? 0 });

        PendingChoice GuestMap()
        {
            var gv = guest.Seat.View;
            int me = guest.Seat.Seat;
            return gv?.Pending.FirstOrDefault(c => c.Seat == me && !c.Answered && c.Kind == ChoiceKind.MapAction);
        }

        bool HostMap() => flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null;

        int Cell(int seat) => flow.Client.View.Seats[seat].Cell;

        int Piece(int seat)
        {
            var board = UnityEngine.Object.FindAnyObjectByType<BoardBoot>();
            var p = board.Pieces.Pieces.FirstOrDefault(x => x.Seat == seat);
            return p != null && p.Spot != null ? p.Spot.Cell : -1;
        }

        Sinnoh.Fx.BoardLogLine LastLine() => flow.Log.Lines.Count > 0 ? flow.Log.Lines[flow.Log.Lines.Count - 1] : null;

        // 방 알림 띠의 지금 글(낱말 잇기 글자를 뺀다)
        List<string> Notices() =>
            flow.Stage.Root.Query<Label>(className: "sn-room-strip-text").ToList().Where(l => l.name != null && l.name.StartsWith("room-notice-", StringComparison.Ordinal)).Select(l => l.text.Replace("⁠", "")).ToList();

        // 방장이 걸어도 운이 드러나지 않는 칸: 길 가운데 · 도착 칸에 뒷면 숨겨진 장소가 없다(UndoFlowTests.CleanCell — 숨은 몫은 방장 세션의 원본)
        int CleanCell()
        {
            var me = flow.Client.View.Seats[0];
            var host = hosted.Game.HostCopy();
            var view = flow.Client.View;
            var steps = MoveRules.Reachable(flow.Ctx.Board, me.Cell, me.MovesLeft, view.Options.ElevationMode);
            foreach (int c in flow.Hud.Reach.OrderBy(c => steps[c]).ThenBy(c => c))
            {
                if (c == me.Cell || flow.Hud.StopsBefore(c).Count > 0) continue;
                if (host.Tokens.Any(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == c)) continue;
                return c;
            }
            Assert.Fail("걸을 칸이 없다(갈 수 있는 칸 " + flow.Hud.Reach.Count + ")");
            return -1;
        }

        // 손님의 걷기 답: 가까운 칸부터 — 토큰이 없는 칸(숨겨진 장소 · 조우에 서지 않는다)
        ChoiceAnswer GuestMove(GameState gv, PendingChoice gc)
        {
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(gv, flow.Ctx, gc, legal);
            var me = gv.Seats[guest.Seat.Seat];
            var steps = MoveRules.Reachable(flow.Ctx.Board, me.Cell, me.MovesLeft, gv.Options.ElevationMode);
            var host = hosted.Game.HostCopy();
            var move = legal.Where(a => a.Kind == AnswerKind.Move && a.Arg < 0 && a.Target >= 0 && a.Target != me.Cell && steps[a.Target] != MoveRules.Unreachable && !host.Tokens.Any(t => !t.Destroyed && t.Cell == a.Target))
                .OrderBy(a => steps[a.Target]).ThenBy(a => a.Target).FirstOrDefault();
            Assert.That(move, Is.Not.Null, "손님이 걸을 칸이 없다");
            return move;
        }

        // 떠 있는 툴팁의 본문(머리 · 본문 Label 가운데 마지막)
        static string TipOf(UiStage stage, VisualElement target)
        {
            stage.Tips.Show(target);
            if (stage.Tips.Target != target) return null;
            var labels = stage.Tips.Element.Query<Label>().ToList();
            return labels.Count > 0 ? labels[labels.Count - 1].text.Replace("⁠", "") : null;
        }

        static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }
    }
}
