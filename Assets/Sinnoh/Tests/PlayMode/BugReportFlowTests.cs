using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 게임 안 [버그 신고](M9 — 결정 132 ①)를 화면 흐름으로: 창구가 없는 화면(에디터 · 웹)은 「PC판에서만」으로 잠김 · 게임 메뉴 → 신고 쪽 · 설명 없으면 [보내기] 꺼짐 ·
    /// 가짜 창구(<see cref="Fake"/>)로 실패 → [다시 보내기](같은 묶음) → 보냄 · 보낸 묶음 내용(설명 · 판 · 로그 끝부분 · 지금 판 기록, 닉네임 · 토큰 지움) · 플레이 기록 설정이 꺼져 있어도 보낸다 ·
    /// Esc · 타이틀 설정에서 연 창. 저장 · 로그는 임시 폴더(사용자 저장 폴더 · 실제 Player.log를 읽지 않는다 — <see cref="GameFlow.BugLogPath"/>를 시험 파일로).
    /// </summary>
    public sealed class BugReportFlowTests
    {
        sealed class Fake : IPlayUploader
        {
            public readonly List<KeyValuePair<string, byte[]>> Sent = new List<KeyValuePair<string, byte[]>>();
            public int Calls, FailNext;

            public async Task UploadAsync(string name, byte[] bytes)
            {
                Calls++;
                await Task.Yield();
                if (FailNext > 0)
                {
                    FailNext--;
                    throw new InvalidOperationException("오프라인(시험)");
                }
                Sent.Add(new KeyValuePair<string, byte[]>(name, bytes));
            }

            public Task<int> DeleteAllAsync() => Task.FromResult(0);
        }

        static IEnumerator Wait(Task task)
        {
            for (int i = 0; i < 2000 && !task.IsCompleted; i++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "보내기가 끝나지 않았다");
            Assert.That(task.IsFaulted, Is.False, "보내기 실패가 밖으로 샜다");
        }

        [UnityTest]
        public IEnumerator M9_버그신고_PC판만_설명없으면잠김_실패뒤다시보내기_묶음내용_닉네임토큰지움_로그상한_결정132()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-bug-test-" + Guid.NewGuid().ToString("N"));
            string nickWas = RoomIdentity.Nickname;
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                ScreenBoot boot = null;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                boot.PointerFromMouse = false;
                for (int i = 0; i < 4; i++) yield return null;
                boot.UseSaves(root, () => 1796000000000);
                var flow = boot.Flow;
                var stage = flow.Stage;
                Assert.That(flow.BugLogPath, Is.Null, "에디터는 로그를 싣지 않는다(Editor.log를 읽지 않는다)");

                // ── 창구가 없는 화면(에디터 · 웹 손님판): 타이틀 설정 · 게임 메뉴의 [버그 신고]가 「PC판에서만」 흐린 단추 ──
                Assert.That(flow.BugReady, Is.False);
                flow.OpenSettings();
                var lockedSet = stage.Root.Q<Button>("settings-bug");
                Assert.That(lockedSet != null && lockedSet.ClassListContains(UiStage.OffClass) && lockedSet.text == stage.T("screen.bug.button.pc"), Is.True, "설정의 잠긴 [버그 신고]");
                flow.OpenTitleBug();
                Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>(), "잠긴 단추는 창을 열지 않는다");

                Directory.CreateDirectory(root);
                string log = Path.Combine(root, "Player.log");
                var sb = new StringBuilder("FIRST-LINE-MARK\n");
                for (int i = 0; sb.Length < BugReport.MaxLogBytes * 2; i++) sb.Append("[Room] 시험닉네임 줄 ").Append(i).Append('\n');
                sb.Append("LAST-LINE-MARK\n");
                File.WriteAllText(log, sb.ToString(), new UTF8Encoding(false));
                flow.BugLogPath = log;
                RoomIdentity.Nickname = "시험닉네임";   // 에디터는 실행하는 동안만 쥔다(PlayerPrefs에 쓰지 않는다)

                FlowPilot.Start(flow, SoloTrail.Defeat);
                for (int i = 0; i < 200 && !flow.InputOpen; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True, "보드에 닿지 못했다");
                flow.OpenMenu();
                var menu = stage.Current as BoardMenuScreen;
                Assert.That(menu, Is.Not.Null, "게임 메뉴");
                Assert.That(stage.Root.Q<Button>("board-menu-bug").ClassListContains(UiStage.OffClass), Is.True, "에디터의 메뉴 [버그 신고]도 잠김");
                Assert.That(menu.OpenBug(), Is.False);
                flow.CloseMenu();

                // ── PC 플레이어처럼: 창구를 넣는다 — 플레이 기록 설정은 꺼 둔다(버그 신고는 그래도 보낸다 [가정 #680]) ──
                var fake = new Fake();
                flow.Uploads = new PlayUploads(fake, "0.0.test", () => false);
                flow.OpenMenu();
                menu = (BoardMenuScreen)stage.Current;
                var bugButton = stage.Root.Q<Button>("board-menu-bug");
                Assert.That(bugButton.ClassListContains(UiStage.OffClass), Is.False);
                Assert.That(bugButton.text, Is.EqualTo(stage.T("screen.bug.button")));
                Assert.That(menu.OpenBug(), Is.True);
                var panel = menu.Bug;
                Assert.That(panel, Is.Not.Null);
                foreach (string name in new[] { "bug-title", "bug-prompt", "bug-text", "bug-attach", "bug-send", "bug-cancel" })
                    Assert.That(stage.Root.Q(name), Is.Not.Null, name);
                Assert.That(stage.Root.Q("board-menu-resume"), Is.Null, "메뉴 판이 신고 쪽으로 바뀌었다");

                // ── 설명이 없으면 [보내기]가 꺼져 있고 눌러도 보내지 않는다 ──
                Assert.That(stage.Root.Q<Button>("bug-send").enabledSelf, Is.False, "빈 설명");
                var none = panel.Send();
                yield return Wait(none);
                Assert.That(none.Result, Is.False);
                panel.SetText("  \n  ");
                Assert.That(stage.Root.Q<Button>("bug-send").enabledSelf, Is.False, "빈칸만");
                Assert.That(fake.Calls, Is.EqualTo(0));
                panel.SetText("배틀 화면에서 멈췄습니다.");
                Assert.That(stage.Root.Q<Button>("bug-send").enabledSelf, Is.True);

                // ── 실패: 「보내지 못했습니다 …」 + [다시 보내기] [닫기] ──
                fake.FailNext = 1;
                var first = panel.Send();
                Assert.That(panel.State, Is.EqualTo(BugReportPanel.Step.Sending));
                Assert.That(stage.Root.Q<Label>("bug-status").text, Is.EqualTo(stage.T("screen.bug.sending")));
                yield return Wait(first);
                Assert.That(first.Result, Is.False);
                Assert.That(panel.State, Is.EqualTo(BugReportPanel.Step.Failed));
                Assert.That(stage.Root.Q<Label>("bug-status").text, Is.EqualTo(stage.T("screen.bug.failed")));
                Assert.That(stage.Root.Q("bug-retry") != null && stage.Root.Q("bug-close") != null && stage.Root.Q("bug-send") == null, Is.True, "실패 단추 줄");
                Assert.That((fake.Calls, fake.Sent.Count), Is.EqualTo((1, 0)));
                var packed = panel.Pack.Bytes;

                // ── [다시 보내기]: 같은 묶음 → 「보냈습니다. 고맙습니다.」 + [닫기] ──
                var again = panel.Retry();
                yield return Wait(again);
                Assert.That(again.Result, Is.True);
                Assert.That(panel.State, Is.EqualTo(BugReportPanel.Step.Sent));
                Assert.That(stage.Root.Q<Label>("bug-status").text, Is.EqualTo(stage.T("screen.bug.sent")));
                Assert.That(stage.Root.Q("bug-close") != null && stage.Root.Q("bug-retry") == null, Is.True);
                Assert.That(fake.Sent.Count, Is.EqualTo(1));
                Assert.That(fake.Sent[0].Value, Is.SameAs(packed), "다시 보내기는 같은 묶음");
                Assert.That(fake.Sent[0].Key, Does.StartWith(BugReport.Prefix).And.Match("^[A-Za-z0-9_-]+$"));

                // ── 보낸 묶음: 설명 · 판 · 로그 끝부분(상한 · 닉네임 가림) · 지금 판 기록(지운 머리 + 명령 줄) ──
                var file = BugReport.Unpack(fake.Sent[0].Value);
                Assert.That(file.Info.Description, Is.EqualTo("배틀 화면에서 멈췄습니다."));
                Assert.That((file.Info.Role, file.Info.Lang, file.Info.RulesVersion, file.Info.ProtocolVersion), Is.EqualTo(("solo", flow.Lang, Sinnoh.Rules.Session.SaveRecord.CurrentRulesVersion, Sinnoh.Rules.Session.Protocol.Version)));
                Assert.That(file.Info.Screen, Does.Match("^[0-9]+x[0-9]+"));
                Assert.That(file.Info.SentAtMs, Is.EqualTo(1796000000000));
                Assert.That(file.Info.LogBytes, Is.GreaterThan(0).And.LessThanOrEqualTo(BugReport.MaxLogBytes), "로그 상한");
                Assert.That(file.Log, Does.EndWith("LAST-LINE-MARK\n").And.Not.Contain("FIRST-LINE-MARK").And.Not.Contain("시험닉네임"));
                Assert.That(file.Log, Does.Contain(BugReport.Redacted));
                Assert.That(file.Info.RecordNote, Is.Null, "솔로 판의 기록을 싣는다");
                var rec = PlayRecord.Unpack(PlayRecord.Gzip(file.Record));
                Assert.That(rec.Head.Seed, Is.EqualTo(SoloTrail.Defeat.Seed));
                Assert.That(rec.Lines.Count, Is.EqualTo(file.Info.RecordLines).And.GreaterThan(0));
                Assert.That(Gunzip(fake.Sent[0].Value), Does.Not.Contain("시험닉네임"));

                // ── [닫기] · Esc: 신고 쪽 → 메뉴 → 보드 ──
                Assert.That(menu.OpenBug(), Is.False, "신고 쪽이 이미 열려 있다");
                menu.CloseBug();   // [닫기]
                Assert.That(menu.Page == BoardMenuScreen.MainPage && stage.Root.Q("board-menu-bug") != null, Is.True, "[닫기] = 메뉴로");
                Assert.That(menu.OpenBug(), Is.True, "다시 열면 새 창");
                Assert.That(menu.Bug.State, Is.EqualTo(BugReportPanel.Step.Edit));
                Assert.That(flow.Escape(), Is.True);
                Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.MainPage));
                Assert.That(stage.Root.Q("board-menu-resume"), Is.Not.Null, "Esc = 메뉴로");
                Assert.That(flow.Escape() && stage.Current == null, Is.True, "Esc 한 번 더 = 보드");

                // ── 타이틀 설정에서: 창 → 보내기(판 없음 — 기록 없이) → Esc = 설정으로 ──
                flow.OpenTitle();
                flow.OpenSettings();
                Assert.That(stage.Root.Q<Button>("settings-bug").ClassListContains(UiStage.OffClass), Is.False);
                flow.OpenTitleBug();
                var screen = stage.Current as BugReportScreen;
                Assert.That(screen, Is.Not.Null, "타이틀 설정의 신고 창");
                screen.Panel.SetText("Title screen music stops.");
                var titleSend = screen.Panel.Send();
                yield return Wait(titleSend);
                Assert.That(titleSend.Result, Is.True);
                var titleFile = BugReport.Unpack(fake.Sent[1].Value);
                Assert.That((titleFile.Info.Role, titleFile.Info.RecordNote, titleFile.Record), Is.EqualTo(("title", "none", (string)null)));
                Assert.That(flow.Escape(), Is.True);
                Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>(), "Esc = 설정으로");
                Assert.That(flow.Typing, Is.False, "창을 닫으면 글 치기 표시를 끈다");

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                RoomIdentity.Nickname = nickWas;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// 결정 136: 설명 4,000자(한국어 · 영어 한 문단 · 줄바꿈 많은 글) × 720 · 1080 — ① 4,000자를 넘는 글은 앞 4,000자만(코드 · 붙여넣기) · 끝에서 치는 글자는 막힘 [가정 #686]
        /// ② 글 칸 높이가 빈 칸과 같고 판 안(판이 커지지 않는다) ③ 글 칸 안 ScrollView: 맨 위 = 첫 줄이 보이고 끝 줄은 안 보임 · 스크롤 막대 값을 끝으로(막대 끌기와 같은 길) = 끝 줄이 보이고 첫 줄은 안 보임 · 휠 한 번이 위아래로 굴린다
        /// ④ 맨 위로 굴린 채 끝에 글자를 치면(줄바꿈 · 글자) 끝 줄 · 커서가 보이는 영역 안 · 위 · 아래 화살표로 보이는 줄 밖까지 옮기면 커서를 따라 굴린다 ⑤ 보낸 묶음의 설명이 4,000자 그대로.
        /// </summary>
        [UnityTest]
        public IEnumerator M9_버그신고_4000자_글칸높이고정_스크롤로첫줄끝줄_커서따라감_넘는글자름_묶음그대로_결정136()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-bug4000-test-" + Guid.NewGuid().ToString("N"));
            var targets = new List<RenderTexture>();
            ScreenBoot boot = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                boot.PointerFromMouse = false;
                for (int i = 0; i < 4; i++) yield return null;
                boot.UseSaves(root, () => 1796000000000);
                var flow = boot.Flow;
                var stage = flow.Stage;
                var fake = new Fake();
                flow.Uploads = new PlayUploads(fake, "0.0.test", () => false);
                FlowPilot.Start(flow, SoloTrail.Defeat);
                for (int i = 0; i < 200 && !flow.InputOpen; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True, "보드에 닿지 못했다");

                var texts = new[] { ("ko", Fill4000Ko()), ("en", Fill4000En()), ("lines", Fill4000Lines()) };
                foreach (var (kind, full) in texts)
                    Assert.That(full.Length == BugReport.MaxDescription && full == full.Trim(), Is.True, kind + " 견본이 4,000자(앞뒤 빈칸 없음)");

                foreach (var size in new[] { (1280, 720), (1920, 1080) })
                {
                    var rt = new RenderTexture(size.Item1, size.Item2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    rt.Create();
                    targets.Add(rt);
                    boot.Root.SetTarget(rt);
                    for (int i = 0; i < 4; i++) yield return null;

                    foreach (var (kind, full) in texts)
                    {
                        string at = size.Item2 + " " + kind;
                        flow.OpenMenu();
                        var menu = (BoardMenuScreen)stage.Current;
                        Assert.That(menu.OpenBug(), Is.True, at);
                        var panel = menu.Bug;
                        for (int i = 0; i < 3; i++) yield return null;
                        var field = stage.Root.Q<TextField>("bug-text");
                        float emptyHeight = field.worldBound.height;
                        Assert.That(emptyHeight, Is.GreaterThan(20f), at + " 빈 글 칸 높이");

                        // ① 넘는 글: 코드로 넣은 글 · 글 칸 값(붙여넣기와 같은 값 바꾸기) 모두 앞 4,000자
                        panel.SetText(full + "초과분");
                        Assert.That(panel.Text, Is.EqualTo(full), at + " SetText가 4,000자에서 자른다");
                        field.value = full + "붙여넣기";
                        Assert.That((panel.Text, field.value), Is.EqualTo((full, full)), at + " 글 칸 값이 4,000자에서 잘린다");
                        Assert.That(stage.Root.Q<Label>("bug-count").text, Does.StartWith("4,000 / 4,000"), at + " 글자 수 표시");
                        for (int i = 0; i < 4; i++) yield return null;

                        // ② 글 칸 높이 그대로 · 판 안
                        field = stage.Root.Q<TextField>("bug-text");
                        var box = stage.Root.Q("board-menu-panel").worldBound;
                        Assert.That(field.worldBound.height, Is.EqualTo(emptyHeight).Within(0.5f), at + " 4,000자에도 글 칸 높이가 그대로");
                        foreach (string name in new[] { "bug-text", "bug-count", "bug-foot" })
                        {
                            var e = stage.Root.Q(name).worldBound;
                            Assert.That(e.yMin >= box.yMin - 0.5f && e.yMax <= box.yMax + 0.5f && e.xMax <= box.xMax + 0.5f, Is.True, at + " " + name + "이(가) 판 안: " + e + " / " + box);
                        }
                        Assert.That(box.yMin >= 0 && box.yMax <= stage.Root.worldBound.yMax + 0.5f, Is.True, at + " 판이 화면 안: " + box);

                        // ③ 스크롤: 맨 위 = 첫 줄 · 끝(막대 값) = 끝 줄 · 휠
                        var sv = field.Q<ScrollView>();
                        Assert.That(sv, Is.Not.Null, at + " 글 칸 안 ScrollView");
                        var scroller = sv.verticalScroller;
                        Assert.That(scroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), at + " 넘치면 세로 막대가 보인다");
                        Assert.That(scroller.highValue, Is.GreaterThan(emptyHeight), at + " 굴릴 거리");
                        sv.scrollOffset = Vector2.zero;
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(Shown(field, true), Is.True, at + " 맨 위에서 첫 줄이 보인다 " + Lines(field));
                        Assert.That(Shown(field, false), Is.False, at + " 맨 위에서 끝 줄은 안 보인다 " + Lines(field));
                        scroller.value = scroller.highValue;   // 막대를 끝까지 끈 것과 같은 길(Scroller.value → ScrollView.scrollOffset)
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(sv.scrollOffset.y, Is.EqualTo(scroller.highValue).Within(0.5f), at + " 막대 값 = 굴린 자리");
                        Assert.That(Shown(field, false), Is.True, at + " 끝으로 굴리면 끝 줄이 보인다 " + Lines(field));
                        Assert.That(Shown(field, true), Is.False, at + " 끝에서는 첫 줄이 안 보인다 " + Lines(field));
                        float end = sv.scrollOffset.y;
                        float now = Wheel(TextOf(field), sv, -3f);
                        for (int i = 0; i < 2; i++) yield return null;
                        Assert.That(sv.scrollOffset.y, Is.LessThan(end - 1f), at + " 휠 위로 = 위로 굴림(보낸 직후 " + now + ", 프레임 뒤 되돌지 않는다)");
                        float up = sv.scrollOffset.y;
                        Wheel(TextOf(field), sv, 3f);
                        for (int i = 0; i < 2; i++) yield return null;
                        Assert.That(sv.scrollOffset.y, Is.GreaterThan(up + 1f), at + " 휠 아래로 = 아래로 굴림");

                        // ④ 맨 위로 굴린 채 끝에서 치기: 두 글자 덜 적은 글 → 줄바꿈 · 글자 → 4,000자, 끝 줄 · 커서가 보인다. 한 글자 더는 막힌다
                        string less = full.Substring(0, BugReport.MaxDescription - 2);
                        panel.SetText(less);
                        field.Focus();
                        for (int i = 0; i < 2; i++) yield return null;
                        field.SelectRange(less.Length, less.Length);
                        sv.scrollOffset = Vector2.zero;
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(Shown(field, false), Is.False, at + " 치기 전: 맨 위라 끝 줄이 안 보인다");
                        Key(field, '\n', KeyCode.None, EventModifiers.None);
                        Key(field, 'Z', KeyCode.None, EventModifiers.None);
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(panel.Text, Is.EqualTo(less + "\nZ"), at + " 끝에 친 줄바꿈 · 글자");
                        Assert.That(Shown(field, false), Is.True, at + " 친 뒤 끝 줄이 보인다(커서를 따라 굴렀다) " + Lines(field));
                        Assert.That(Caret(field), Is.True, at + " 친 뒤 커서가 보이는 영역 안 " + Lines(field));
                        Key(field, 'Q', KeyCode.None, EventModifiers.None);
                        for (int i = 0; i < 2; i++) yield return null;
                        Assert.That(panel.Text.Length, Is.EqualTo(BugReport.MaxDescription), at + " 4,000자에서 치는 글자는 막힌다");
                        Assert.That(panel.Text, Does.EndWith("\nZ"), at + " 막힌 글자는 들어가지 않는다");

                        // 화살표(IMGUI 글쇠 이벤트 꼴 — 실제 글쇠처럼. 글쇠 코드만 넣은 엔진 이벤트 · 내비게이션 이벤트 · Ctrl+Home은 이 시험 패널에서 커서를 옮기지 않았다): 끝에서 위로 보이는 줄보다 더 올라가면 굴러 따라가고, 다시 내려가면 따라 내려간다
                        field.SelectRange(BugReport.MaxDescription, BugReport.MaxDescription);
                        for (int i = 0; i < 3; i++) yield return null;
                        float atEnd = sv.scrollOffset.y;
                        int rows = Mathf.CeilToInt(sv.contentViewport.worldBound.height / LineHeight(field)) + 5;
                        for (int r = 0; r < rows; r++) KeyOp(field, "up");
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(field.cursorIndex, Is.LessThan(BugReport.MaxDescription), at + " 위 화살표가 커서를 옮겼다");
                        Assert.That(sv.scrollOffset.y, Is.LessThan(atEnd - 1f), at + " 보이는 줄보다 더 올라가면 위로 굴렀다 " + Lines(field));
                        Assert.That(Caret(field), Is.True, at + " 위 화살표로 옮긴 커서가 보이는 영역 안 " + Lines(field));
                        float atUp = sv.scrollOffset.y;
                        int upAt = field.cursorIndex;
                        for (int r = 0; r < rows; r++) KeyOp(field, "down");
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.That(field.cursorIndex, Is.GreaterThan(upAt), at + " 아래 화살표가 커서를 옮겼다");
                        Assert.That(sv.scrollOffset.y, Is.GreaterThan(atUp + 1f), at + " 보이는 줄보다 더 내려가면 아래로 굴렀다 " + Lines(field));
                        Assert.That(Caret(field), Is.True, at + " 화살표로 옮긴 커서가 보이는 영역 안 " + Lines(field));

                        // ⑤ 보낸 묶음에 4,000자 그대로(로그 · 기록 상한을 줄여도 설명은 줄이지 않는다)
                        panel.SetText(full);
                        int before = fake.Sent.Count;
                        var send = panel.Send();
                        yield return Wait(send);
                        Assert.That(send.Result, Is.True, at + " 보냄");
                        var file = BugReport.Unpack(fake.Sent[before].Value);
                        Assert.That(file.Info.Description, Is.EqualTo(full), at + " 묶음의 설명이 4,000자 그대로");
                        Assert.That(fake.Sent[before].Value.Length, Is.LessThanOrEqualTo(BugReport.MaxPackBytes), at + " 묶음 상한");
                        menu.CloseBug();
                        flow.CloseMenu();
                        for (int i = 0; i < 2; i++) yield return null;
                    }
                }

                // 묶음 상한에 걸려 로그를 줄이는 판에서도 설명은 그대로(엔진 무관 몫 — 로그 → 판 기록 순서로 줄인다)
                Directory.CreateDirectory(root);
                string log = Path.Combine(root, "big.log");
                var rnd = new System.Random(7);
                var sb = new StringBuilder();
                while (sb.Length < 600 * 1024) sb.Append(rnd.Next().ToString("X8")).Append(rnd.Next(1000)).Append('\n');   // gzip이 잘 줄이지 못하는 글
                File.WriteAllText(log, sb.ToString(), new UTF8Encoding(false));
                var info = new BugReportInfo { Id = "a1", Description = texts[0].Item2, GameVersion = "0.0.test" };
                var pack = BugReport.Pack(info, log, null, null, maxLog: 512 * 1024, maxPack: 64 * 1024);
                var small = BugReport.Unpack(pack.Bytes);
                Assert.That(pack.Bytes.Length, Is.LessThanOrEqualTo(64 * 1024), "줄인 묶음이 상한 안");
                Assert.That(small.Info.LogBytes, Is.LessThan(512 * 1024), "로그를 줄였다");
                Assert.That(small.Info.Description, Is.EqualTo(texts[0].Item2), "로그를 줄여도 설명은 4,000자 그대로");

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                foreach (var t in targets) t.Release();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>한국어 4,000자: 첫 줄 표지 · 문장 줄 · 끝 줄 표지.</summary>
        static string Fill4000Ko() => Fill("첫 줄 FIRST 표지입니다.\n", i => "배틀에서 [가방]을 누른 뒤 화면이 멈췄습니다. " + i + "번째 줄 — Esc도 듣지 않습니다.\n", "\n끝 줄 LAST 표지입니다.");

        /// <summary>영어 4,000자: 줄바꿈 없는 한 문단(글 칸이 줄을 접는다).</summary>
        static string Fill4000En() => Fill("FIRST words of the report. ", i => "After pressing Bag in battle number " + i + " the screen froze and Escape did nothing. ", " The LAST words.");

        /// <summary>줄바꿈 많은 글 4,000자: 짧은 줄 천여 개.</summary>
        static string Fill4000Lines() => Fill("FIRST\n", i => i + "\n", "\nLAST");

        static string Fill(string head, Func<int, string> line, string tail)
        {
            var sb = new StringBuilder(head);
            for (int i = 1; sb.Length < BugReport.MaxDescription; i++) sb.Append(line(i));
            string body = sb.ToString(0, BugReport.MaxDescription - tail.Length);
            if (char.IsWhiteSpace(body[body.Length - 1])) body = body.Substring(0, body.Length - 1) + "x";   // 잘린 자리가 빈칸이면 글자로(길이 그대로)
            return body + tail;
        }

        /// <summary>글 칸의 글 요소(글 칸 안 ScrollView의 내용 안 — 글 전체 높이를 갖는다).</summary>
        static TextElement TextOf(TextField field) =>
            field.Q<ScrollView>()?.contentContainer.Q<TextElement>() ?? throw new InvalidOperationException("글 칸 ScrollView 안에 글 요소가 없다");

        static float LineHeight(TextField field) => Mathf.Max(8f, TextOf(field).resolvedStyle.fontSize * 1.1f);

        /// <summary>첫 줄(true) · 끝 줄(false)의 띠가 글 칸의 보이는 영역(ScrollView 창) 안에 다 드나.</summary>
        static bool Shown(TextField field, bool first)
        {
            var te = TextOf(field);
            var view = field.Q<ScrollView>().contentViewport.worldBound;
            var r = te.worldBound;
            float lh = LineHeight(field);
            float top = first ? r.yMin + te.resolvedStyle.paddingTop : r.yMax - te.resolvedStyle.paddingBottom - lh;
            return top >= view.yMin - 1f && top + lh <= view.yMax + 1f;
        }

        /// <summary>커서 자리(<see cref="TextInputBaseField{T}.cursorPosition"/> — 글 요소 안 좌표)가 보이는 영역 안인가.</summary>
        static bool Caret(TextField field)
        {
            var te = TextOf(field);
            var view = field.Q<ScrollView>().contentViewport.worldBound;
            var p = te.LocalToWorld(field.cursorPosition);
            return p.y >= view.yMin - 1f && p.y <= view.yMax + 1f;
        }

        static string Lines(TextField field)
        {
            var sv = field.Q<ScrollView>();
            return "(글 " + TextOf(field).worldBound + " · 창 " + sv.contentViewport.worldBound + " · 굴림 " + sv.scrollOffset.y + " · 커서 " + field.cursorIndex + " " + field.cursorPosition + ")";
        }

        /// <summary>
        /// 휠 한 번을 글 요소(마우스 아래 가장 깊은 요소 — 실제 휠이 가는 곳)에 보낸다. 엔진 이벤트(IMGUI 없이)에 delta만 넣는다(설정자가 보호돼 있어 반사로).
        /// 돌려주는 값: 보낸 직후의 굴림 변화(진단용 — 프레임 뒤 되돌아가면 글 칸이 커서 쪽으로 다시 굴린 것).
        /// </summary>
        static float Wheel(VisualElement target, ScrollView sv, float lines)
        {
            float was = sv.scrollOffset.y;
            using (var w = WheelEvent.GetPooled())
            {
                typeof(WheelEvent).GetProperty("delta").SetValue(w, new Vector3(0, lines, 0));
                typeof(MouseEventBase<WheelEvent>).GetProperty("mousePosition").SetValue(w, (Vector2)target.worldBound.center);
                w.target = target;
                target.SendEvent(w);
            }
            return sv.scrollOffset.y - was;
        }

        /// <summary>글쇠 하나(눌림 이벤트를 포커스를 가진 요소에 — 글 칸 안의 글 요소).</summary>
        static void Key(TextField field, char c, KeyCode code, EventModifiers mods)
        {
            var target = field.panel.focusController.focusedElement as VisualElement ?? TextOf(field);
            using (var e = KeyDownEvent.GetPooled(c, code, mods)) target.SendEvent(e);
        }

        /// <summary>이동 글쇠 하나(<see cref="Event.KeyboardEvent"/> 꼴 — 「up」 · 「down」 = 위 · 아래 화살표).</summary>
        static void KeyOp(TextField field, string spec)
        {
            var target = field.panel.focusController.focusedElement as VisualElement ?? TextOf(field);
            using (var e = KeyDownEvent.GetPooled(Event.KeyboardEvent(spec))) target.SendEvent(e);
        }

        static string Gunzip(byte[] bytes)
        {
            using (var gz = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
