using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 게임 안 [버그 신고](M9 — 결정 132 ①): 게임 메뉴의 [버그 신고] · 신고 쪽(빈 설명 · 적은 설명 · 보내는 것 마우스 오버 · 보내는 중 · 보냄 · 실패) · 방 판 메뉴 · 타이틀 설정에서 연 창 ·
    /// 창구가 없는 화면(웹 손님판 · 에디터)의 「PC판에서만」. 흐름에 서버를 부르지 않는 창구(<see cref="PlayUploads.Quiet"/> + <see cref="ShotUploader"/>)를 넣어 PC 플레이어처럼 열고,
    /// 로그는 임시 파일(실제 Player.log를 읽지 않는다). 장면마다 판이 화면 안 · 글 넘침을 단정한다. 모두 보드 위 화면이다. 그림은 <c>Logs/Review/m9/bugreport/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] BugScreens =
        {
            Screen("bug-menu", s => BugMenu(s, null)),                                         // 게임 메뉴: [설정] 아래 [버그 신고]
            Screen("bug-menu-tip", s => BugMenu(s, f => f.Stage.Tips.Show(f.Stage.Root.Q("board-menu-bug")))),   // [버그 신고]에 마우스
            Screen("bug-open", s => BugPage(s, null, p =>                                        // 신고 쪽: 빈 설명 = [보내기] 꺼짐
            {
                if (s.Made.Stage.Root.Q<Button>("bug-send").enabledSelf) throw new InvalidOperationException("빈 설명인데 [보내기]가 켜져 있다");
            })),
            Screen("bug-typed", s => BugPage(s, BugText, p =>                                    // 설명을 적은 모습 = [보내기] 켜짐
            {
                if (!s.Made.Stage.Root.Q<Button>("bug-send").enabledSelf) throw new InvalidOperationException("설명을 적었는데 [보내기]가 꺼져 있다");
            })),
            Screen("bug-attach-tip", s => BugPage(s, BugText, p => s.Made.Stage.Tips.Show(s.Made.Stage.Root.Q("bug-attach")))),   // 「지금 판 기록과 로그를 함께 보냅니다.」에 마우스 = 보내는 것 목록
            Screen("bug-long", s => BugPage(s, BugLong(BugLongKo), p => BugScroll(s, false))),              // 4,000자 한국어(결정 136) — 맨 위: 첫 줄 · 세로 막대 · 「4,000 / 4,000자」
            Screen("bug-long-end", s => BugPage(s, BugLong(BugLongKo), p => BugScroll(s, true))),           // 같은 글을 끝까지 굴림: 끝 줄
            Screen("bug-long-en", s => BugPage(s, BugLong(BugLongEn), p => BugScroll(s, true))),            // 영어 한 문단 4,000자 — 끝
            Screen("bug-long-lines", s => BugPage(s, BugLong(BugLongLines), p => BugScroll(s, true))),      // 줄바꿈 많은 글 4,000자 — 끝
            Screen("bug-sending", s => BugPage(s, BugText, p => BugSend(s, p, ShotUploader.Mode.Hang, BugReportPanel.Step.Sending))),   // 보내는 중…
            Screen("bug-sent", s => BugPage(s, BugText, p => BugSend(s, p, ShotUploader.Mode.Ok, BugReportPanel.Step.Sent))),          // 보냈습니다. 고맙습니다. + [닫기]
            Screen("bug-failed", s => BugPage(s, BugText, p => BugSend(s, p, ShotUploader.Mode.Fail, BugReportPanel.Step.Failed))),    // 보내지 못했습니다 … + [다시 보내기] [닫기]
            Screen("bug-room-host", s => BugRoom(s, true, false)),                              // 방장 게임 메뉴(단추가 가장 많다): [버그 신고]가 판 안
            Screen("bug-room-guest", s => BugRoom(s, false, true)),                             // 손님의 신고 쪽
            Screen("bug-title", s => BugTitle(s, false)),                                       // 타이틀 설정: 아래 줄 [뒤로] [버그 신고]
            Screen("bug-title-open", s => BugTitle(s, true)),                                   // 타이틀 설정에서 연 신고 창
            Screen("bug-web", s => BugLocked(s, false)),                                        // 창구 없는 화면(웹 손님판): 메뉴의 흐린 「버그 신고(PC판에서만)」 + 이유
            Screen("bug-web-title", s => BugLocked(s, true)),                                   // 같은 것 — 타이틀 설정
        };

        const string BugText = "배틀에서 [가방]을 누른 뒤 화면이 멈췄습니다.\nEsc도 듣지 않습니다.";

        /// <summary>캡처용 가짜 창구의 보내기: 바로 끝남(보냄 · 실패) · 끝나지 않음(보내는 중). 서버는 부르지 않는다(<see cref="PlayUploads.Quiet"/>).</summary>
        sealed class ShotUploader
        {
            public enum Mode { Ok, Fail, Hang }
            public Mode Now = Mode.Ok;

            public Task Upload(string name, byte[] bytes) =>
                Now == Mode.Ok ? Task.CompletedTask
                : Now == Mode.Fail ? Task.FromException(new InvalidOperationException("오프라인(캡처)"))
                : new TaskCompletionSource<bool>().Task;
        }

        static string bugLog;

        // 캡처용 로그 파일(임시 폴더 — 실제 Player.log 대신)
        static string BugLog()
        {
            if (bugLog != null && File.Exists(bugLog)) return bugLog;
            bugLog = Path.Combine(Application.temporaryCachePath, "sinnoh-uishots-bug-Player.log");
            var sb = new StringBuilder();
            for (int i = 0; i < 400; i++) sb.Append("[UiShots] 캡처용 로그 줄 ").Append(i).Append('\n');
            File.WriteAllText(bugLog, sb.ToString(), new UTF8Encoding(false));
            return bugLog;
        }

        static ShotUploader shotUploader;   // 지금 장면이 넣은 창구(PlayUploads는 창구를 드러내지 않는다)

        static void BugReady(GameFlow flow)
        {
            shotUploader = new ShotUploader();
            flow.Uploads = PlayUploads.Quiet(() => false, upload: shotUploader.Upload);
            flow.BugLogPath = BugLog();
        }

        static GameFlow BugMenu(UiShot s, Action<GameFlow> act)
        {
            var flow = ToBoard(s, BoardReadyMs);
            BugReady(flow);
            flow.OpenMenu();
            if (!(flow.Stage.Current is BoardMenuScreen)) throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            var b = flow.Stage.Root.Q<Button>("board-menu-bug") ?? throw new InvalidOperationException("메뉴에 [버그 신고]가 없다");
            if (b.ClassListContains(UiStage.OffClass)) throw new InvalidOperationException("창구가 있는데 [버그 신고]가 흐리다");
            RoomFit(s, flow, "board-menu-panel");
            act?.Invoke(flow);
            return flow;
        }

        static void BugPage(UiShot s, string text, Action<BugReportPanel> act)
        {
            var flow = BugMenu(s, null);
            var menu = (BoardMenuScreen)flow.Stage.Current;
            if (!menu.OpenBug()) throw new InvalidOperationException("신고 쪽이 열리지 않았다");
            var p = menu.Bug;
            p.Inline = true;
            if (text != null) p.SetText(text);
            act?.Invoke(p);
            BugFit(s, flow);
        }

        static void BugSend(UiShot s, BugReportPanel p, ShotUploader.Mode mode, BugReportPanel.Step want)
        {
            var flow = s.Made;
            shotUploader.Now = mode;
            var t = p.Send();
            if (p.State != want) throw new InvalidOperationException("신고 창 단계 " + p.State + " ≠ " + want);
            if (want != BugReportPanel.Step.Sending && !t.IsCompleted) throw new InvalidOperationException("보내기가 그 자리에서 끝나지 않았다");
            var status = flow.Stage.Root.Q<Label>("bug-status");
            string key = want == BugReportPanel.Step.Sending ? "screen.bug.sending" : want == BugReportPanel.Step.Sent ? "screen.bug.sent" : "screen.bug.failed";
            if (status == null || status.text != flow.Stage.T(key)) throw new InvalidOperationException("단계 줄이 「" + flow.Stage.T(key) + "」가 아니다: " + status?.text);
            var file = BugReport.Unpack(p.Pack.Bytes);
            if (file.Info.Description != BugText.Trim() || file.Info.Role != "solo" || file.Log == null)   // 캡처 판은 저장하지 않는다 — 판 기록 몫은 PlayMode BugReportFlowTests
                throw new InvalidOperationException("묶음 내용이 다르다: " + file.Info.Role + " / " + file.Info.RecordNote);
            Debug.Log($"[UiShots] bug-{want} {s.Grade.Height}: 묶음 {p.Pack.Bytes.Length} B · 로그 {file.Info.LogBytes} B · 판 기록 {file.Info.RecordLines}줄 · {p.Pack.FileName}");
        }


        static readonly (string head, Func<int, string> line, string tail)
            BugLongKo = ("첫 줄 — 배틀 화면이 멈췄습니다.\n", i => "배틀에서 [가방]을 누른 뒤 화면이 멈췄습니다. " + i + "번째 줄 — Esc도 듣지 않습니다.\n", "\n끝 줄 — 여기까지입니다."),
            BugLongEn = ("First words of the report. ", i => "After pressing Bag in battle number " + i + " the screen froze and Escape did nothing. ", " These are the last words."),
            BugLongLines = ("first line\n", i => i + "\n", "\nlast line");

        /// <summary>정확히 <see cref="BugReport.MaxDescription"/>자인 긴 설명(머리 · 줄 되풀이 · 꼬리 — 앞뒤 빈칸 없음).</summary>
        static string BugLong((string head, Func<int, string> line, string tail) k)
        {
            var sb = new StringBuilder(k.head);
            for (int i = 1; sb.Length < BugReport.MaxDescription; i++) sb.Append(k.line(i));
            string body = sb.ToString(0, BugReport.MaxDescription - k.tail.Length);
            if (char.IsWhiteSpace(body[body.Length - 1])) body = body.Substring(0, body.Length - 1) + "x";
            return body + k.tail;
        }

        /// <summary>긴 설명의 글 칸: 높이 고정(116 · 174 — 빈 칸과 같다) · 세로 막대 · 글자 수 줄 · 맨 위(false) / 끝(true)으로 굴린다.</summary>
        static void BugScroll(UiShot s, bool end)
        {
            var root = s.Made.Stage.Root;
            s.Panel.Tick(3);
            var field = root.Q<TextField>("bug-text") ?? throw new InvalidOperationException("글 칸 없음");
            if (field.value.Length != BugReport.MaxDescription) throw new InvalidOperationException("글 칸이 4,000자가 아니다: " + field.value.Length);
            var count = root.Q<Label>("bug-count");
            if (count == null || !count.text.StartsWith("4,000 / 4,000")) throw new InvalidOperationException("글자 수 줄: " + count?.text);
            var sv = field.Q<ScrollView>() ?? throw new InvalidOperationException("글 칸 안 ScrollView 없음");
            float want = (s.Grade.Height >= 1080 ? 174f : 116f);
            if (Math.Abs(field.layout.height - want) > 1f)
                throw new InvalidOperationException("글 칸 높이가 " + field.layout.height + " (기대 " + want + ")");
            if (sv.verticalScroller.resolvedStyle.display != DisplayStyle.Flex || sv.verticalScroller.highValue <= 0f) throw new InvalidOperationException("세로 막대가 없다");
            sv.scrollOffset = new Vector2(0, end ? sv.verticalScroller.highValue : 0f);
            s.Panel.Tick(3);
            Debug.Log($"[UiShots] bug-long {s.Grade.Height}: 글 칸 {field.layout.height} · 굴릴 거리 {sv.verticalScroller.highValue} · 굴림 {sv.scrollOffset.y}");
        }

        static void BugFit(UiShot s, GameFlow flow)
        {
            RoomFit(s, flow, "board-menu-panel");
            foreach (string name in new[] { "bug-text", "bug-foot" })
            {
                var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
                var box = flow.Stage.Root.Q("board-menu-panel").worldBound;
                if (e.worldBound.xMax > box.xMax + 0.5f || e.worldBound.yMax > box.yMax + 0.5f) throw new InvalidOperationException(name + "이(가) 판 밖으로 나간다");
            }
        }

        static void BugRoom(UiShot s, bool host, bool open)
        {
            var flow = RoomGame(s, l => RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0), host);
            BugReady(flow);
            flow.OpenMenu();
            var menu = flow.Stage.Current as BoardMenuScreen ?? throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            Pass(flow, 0);
            if (flow.Stage.Root.Q("board-menu-bug") == null) throw new InvalidOperationException("방 판 메뉴에 [버그 신고]가 없다");
            RoomFit(s, flow, "board-menu-panel");
            if (!open) return;
            if (!menu.OpenBug()) throw new InvalidOperationException("신고 쪽이 열리지 않았다");
            menu.Bug.SetText(BugText);
            if (flow.BugRole != (host ? "host" : "guest")) throw new InvalidOperationException("어디서 = " + flow.BugRole);
            BugFit(s, flow);
        }

        static void BugTitle(UiShot s, bool open)
        {
            var flow = s.Flow();
            BugReady(flow);
            flow.SoundPrefs.PlayNotice = true;
            flow.OpenSettings();
            var b = flow.Stage.Root.Q<Button>("settings-bug") ?? throw new InvalidOperationException("설정에 [버그 신고]가 없다");
            if (b.ClassListContains(UiStage.OffClass)) throw new InvalidOperationException("창구가 있는데 설정의 [버그 신고]가 흐리다");
            RoomFit(s, flow, "settings-foot");
            if (!open) return;
            flow.OpenTitleBug();
            var screen = flow.Stage.Current as BugReportScreen ?? throw new InvalidOperationException("신고 창이 열리지 않았다");
            screen.Panel.SetText(BugText);
            RoomFit(s, flow, "bug-report-panel");
        }

        static void BugLocked(UiShot s, bool title)
        {
            GameFlow flow;
            Button b;
            if (title)
            {
                flow = s.Flow();
                flow.OpenSettings();
                b = flow.Stage.Root.Q<Button>("settings-bug");
            }
            else
            {
                flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                b = flow.Stage.Root.Q<Button>("board-menu-bug");
            }
            if (flow.BugReady) throw new InvalidOperationException("창구 없는 흐름이 아니다");
            if (b == null || !b.ClassListContains(UiStage.OffClass) || b.text != flow.Stage.T("screen.bug.button.pc")) throw new InvalidOperationException("「PC판에서만」 흐린 단추가 아니다: " + b?.text);
            RoomFit(s, flow, title ? "settings-foot" : "board-menu-panel");
            flow.Stage.Tips.Show(b);
        }
    }
}
