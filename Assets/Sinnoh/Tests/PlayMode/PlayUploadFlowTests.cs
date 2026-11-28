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
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Unity.Services.CloudSave;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 「플레이 기록 올리기 뼈대」(02 §12)를 화면 흐름으로: 가짜 올리기 창구(<see cref="Fake"/>)로 설정이 꺼졌을 때 아무것도 부르지 않는지, 켜졌을 때 판을 떠나며 한 번 올리는지,
    /// 실패를 삼키고 다음에 한 번 더 해 보는지 본다. 동의 화면(결정 126 ④): 첫 실행 알림 한 번 · 기본 켜짐 · 설정 줄 끔 · [내 기록 지우기] · 방장이 켠 방의 로비 줄.
    /// 저장은 임시 폴더에 쓰고 지운다. 실제 UGS는 아래 Explicit 시험만 친다(관문 밖).
    /// </summary>
    public sealed class PlayUploadFlowTests
    {
        sealed class Fake : IPlayUploader
        {
            public readonly List<KeyValuePair<string, byte[]>> Sent = new List<KeyValuePair<string, byte[]>>();
            public int Calls, FailNext, Deleted;

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

            public Task<int> DeleteAllAsync()
            {
                Deleted++;
                int n = Sent.Count;
                Sent.Clear();
                return Task.FromResult(n);
            }
        }

        static IEnumerator Wait(Task task)
        {
            for (int i = 0; i < 2000 && !task.IsCompleted; i++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "올리기가 끝나지 않았다");
            Assert.That(task.IsFaulted, Is.False, "올리기 실패가 밖으로 샜다");
        }

        // 갤럭시단 배틀이 있었던 판처럼: 갤럭시단 자리의 명령 줄 하나를 기록에 덧붙인다(묶기는 다시 적용하지 않는다)
        static void AddGalacticLine(string dir, int seq)
        {
            var line = new SaveLogLine { Seq = seq, Seat = SeatId.Galactic, Command = Command.AiAnswer(1, ChoiceAnswer.Of(AnswerKind.Decline), default) };
            File.AppendAllText(Path.Combine(dir, SaveStore.LogFile), SaveStore.Serialize(line, false) + "\n", new UTF8Encoding(false));
        }

        [UnityTest]
        public IEnumerator M9_플레이기록_꺼져있으면부르지않고_켜면떠날때한번올리고_실패는삼키고다음에한번더_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning && msg.Contains("PlayUploads")) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-upload-test-" + Guid.NewGuid().ToString("N"));
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
                boot.UseSaves(root, () => 1794000000000);
                var flow = boot.Flow;
                Assert.That(flow.Uploads, Is.Null, "에디터(플레이 시험)의 흐름은 올리기 창구가 없다");
                Assert.That(flow.SoundPrefs.PlayUpload, Is.True, "기본 켜짐(결정 126 ④)");

                var fake = new Fake();
                bool on = false;
                var uploads = flow.Uploads = new PlayUploads(fake, "0.0.test", () => on);
                string dir = Path.Combine(root, GameFlow.SoloSlot);

                // ── 꺼짐(기본): 판을 떠나도 창구를 부르지 않고 표시 파일도 없다 ──
                flow.StartSolo(new SoloSettings(), 11, 12);
                Assert.That(flow.SaveId, Is.EqualTo(GameFlow.SoloSlot));
                AddGalacticLine(dir, 0);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(0));
                Assert.That(PlayRecord.Uploaded(dir), Is.EqualTo(-1));

                // ── 켬 + 오프라인: 실패를 삼킨다(게임은 그대로 타이틀) · 표시 없음 ──
                on = true;
                fake.FailNext = 1;
                flow.StartSolo(new SoloSettings(), 21, 22);
                AddGalacticLine(dir, 0);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(1));
                Assert.That(fake.Sent.Count, Is.EqualTo(0));
                Assert.That(PlayRecord.Uploaded(dir), Is.EqualTo(-1));
                Assert.That(flow.Session, Is.Null);

                // ── 다음에 한 번 더: 같은 폴더를 다시 받으면 올린다 · 묶음은 지운 머리 + 줄 · 표시가 남는다 ──
                uploads.Offer(dir);
                yield return Wait(uploads.Idle);
                Assert.That(fake.Sent.Count, Is.EqualTo(1));
                Assert.That(fake.Sent[0].Key, Does.StartWith(PlayRecord.Prefix + ((ulong)21).ToString("x16") + "_f" + SaveRecord.CurrentFormat));
                var file = PlayRecord.Unpack(fake.Sent[0].Value);
                Assert.That(file.GameVersion, Is.EqualTo("0.0.test"));
                Assert.That(file.Head.Seed, Is.EqualTo(21UL));
                Assert.That(file.Lines.Count, Is.GreaterThan(0));
                Assert.That(PlayRecord.Uploaded(dir), Is.EqualTo(file.Lines.Count));

                // ── 같은 판을 두 번 올리지 않는다 ──
                uploads.Offer(dir);
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(2));

                // ── 갤럭시단 배틀이 없던 판은 올리지 않는다 ──
                flow.StartSolo(new SoloSettings(), 31, 32);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(2));

                // ── 지우기 호출 ──
                var del = uploads.DeleteMine();
                yield return Wait(del);
                Assert.That(del.Result, Is.EqualTo(1));
                Assert.That(fake.Deleted, Is.EqualTo(1));

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>올리기를 시험이 풀 때까지 붙잡는 가짜 창구(올리는 사이에 되돌림이 끼는 경우).</summary>
        sealed class HeldUploader : IPlayUploader
        {
            public readonly List<KeyValuePair<string, byte[]>> Sent = new List<KeyValuePair<string, byte[]>>();
            public TaskCompletionSource<bool> Hold;
            public int Started;

            public async Task UploadAsync(string name, byte[] bytes)
            {
                Started++;
                if (Hold != null) await Hold.Task;
                Sent.Add(new KeyValuePair<string, byte[]>(name, bytes));
            }

            public Task<int> DeleteAllAsync() => Task.FromResult(0);
        }

        [UnityTest]
        public IEnumerator M10_무르기_올리는중에로그가잘리면다음에이어진갈래를다시올린다()
        {
            // 검토 반영(결정 149): 결과 화면에서 올리는 사이에 되돌려 로그가 잘려도, 다음 Offer가 이어진 갈래를 같은 이름으로 다시 올린다
            // (이어진 갈래에 갤럭시단 배틀이 없어도 — 서버의 버린 갈래를 덮는다). 실제 UGS는 부르지 않는다
            string dir = Path.Combine(Application.temporaryCachePath, "sinnoh-upload-undo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(dir);
                store.WriteHead(new SaveHead { Seed = 0x154 });
                File.WriteAllText(Path.Combine(dir, "snap-0.json"), "{}"); // 묶기(Peek)는 스냅샷이 있는지만 본다
                for (int i = 0; i < 10; i++) store.AppendCommand(new SaveLogLine { Seq = i, Seat = i == 7 ? SeatId.Galactic : 0, Command = Command.TimeoutOf(i) });
                var up = new HeldUploader { Hold = new TaskCompletionSource<bool>() };
                var uploads = new PlayUploads(up, "0.0.test", () => true);
                uploads.Offer(dir);
                for (int i = 0; i < 2000 && up.Started == 0; i++) yield return null;
                Assert.That(up.Started, Is.EqualTo(1), "묶고 올리기를 시작했다");
                // 올리는 사이에 되돌림: 갤럭시단 줄(7) 앞 6줄로 잘리고 새 갈래가 10줄까지
                store.TruncateLog(6);
                for (int i = 6; i < 10; i++) store.AppendCommand(new SaveLogLine { Seq = i, Seat = 0, Command = Command.TimeoutOf(100 + i) });
                up.Hold.SetResult(true);
                yield return Wait(uploads.Idle);
                Assert.That(up.Sent.Count, Is.EqualTo(1));
                Assert.That(PlayRecord.Uploaded(dir), Is.EqualTo(10), "늦게 끝난 올리기는 올린 그대로(옛 갈래의 지문)를 적는다");

                up.Hold = null;
                uploads.Offer(dir);
                yield return Wait(uploads.Idle);
                Assert.That(up.Sent.Count, Is.EqualTo(2), "지문이 달라 이어진 갈래를 다시 올린다");
                Assert.That(up.Sent[1].Key, Is.EqualTo(up.Sent[0].Key), "같은 이름 — 덮어쓴다");
                var file = PlayRecord.Unpack(up.Sent[1].Value);
                Assert.That(file.Lines.Count, Is.EqualTo(10));
                Assert.That(PlayRecord.HadGalacticBattle(file.Lines), Is.False, "올라간 것은 이어진 갈래");

                uploads.Offer(dir);
                yield return Wait(uploads.Idle);
                Assert.That(up.Sent.Count, Is.EqualTo(2), "같은 갈래는 두 번 올리지 않는다");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // 로비 알림 줄 시험용 가짜 방(방 상태만 — 보낸 것은 버린다)
        sealed class FakeLobby : ILobbySource
        {
            public RoomState Room { get; set; }
            public int Seat { get; set; } = 1;
            public bool IsHost { get; set; }
            public string JoinCode => null;
            public bool Listed => false;
            public string ListFail => null;
            public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; } = new KeyValuePair<string, string>[0];
            public int Port => 0;
            public bool Tunneled => false;
            public void Send(RoomMsg msg) { }

            public static FakeLobby With(bool records, bool host) => new FakeLobby
            {
                IsHost = host, Seat = host ? 0 : 1,
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-test", Settings = new RoomSettings { Seats = 3 }, Records = records,
                    Seats = new List<RoomSeat> { new RoomSeat { Nickname = "방장", Host = true, Connected = true }, new RoomSeat { Nickname = "손님", Connected = true }, null },
                },
            };
        }

        [UnityTest]
        public IEnumerator M9_플레이기록동의_첫실행알림한번_기본켜짐_끄면안올림_지우기_방장켬이면로비줄_결정126()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-consent-test-" + Guid.NewGuid().ToString("N"));
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
                boot.UseSaves(root, () => 1794000000000);
                var flow = boot.Flow;
                var prefs = flow.SoundPrefs;
                Assert.That(prefs, Is.Not.InstanceOf<StoredSoundPrefs>(), "시험은 사용자의 PlayerPrefs에 쓰지 않는다");
                Assert.That(prefs.PlayUpload, Is.True, "기본 켜짐");
                Assert.That(prefs.PlayNotice, Is.False, "아직 알림을 보지 않았다");
                Assert.That(flow.PlayNoticeDue, Is.False, "올리는 창구가 없는 흐름(에디터)은 알림을 띄우지 않는다");
                Assert.That(ScreenBoot.MakesUploads(true), Is.False, "에디터도 만들지 않는다");
                Assert.That(ScreenBoot.MakesUploads(false), Is.True, "PC 플레이어만 만든다");

                var fake = new Fake();
                var uploads = flow.Uploads = new PlayUploads(fake, "0.0.test", () => PlayUploads.Allowed(prefs));
                string dir = Path.Combine(root, GameFlow.SoloSlot);
                var stage = flow.Stage;

                // ── 첫 실행: 타이틀 위에 알림 창 · 메뉴는 눌리지 않는다 · 알림을 보기 전의 판은 올리지 않는다 ──
                flow.OpenTitle();
                var title = stage.Current as TitleScreen;
                Assert.That(title != null && title.NoticeShown && stage.Root.Q("title-notice") != null, Is.True, "첫 실행 알림 창");
                foreach (string name in new[] { "title-notice-what", "title-notice-why", "title-notice-off", "title-notice-more", "title-notice-ok", "title-notice-settings" })
                    Assert.That(stage.Root.Q(name), Is.Not.Null, name);
                Assert.That(title.Activate(0), Is.False, "알림이 떠 있는 동안 메뉴를 누르지 않는다");
                Assert.That(flow.RecordsOn, Is.False, "알림 전에는 보내지 않는다(방 로비 줄도 없음)");
                flow.StartSolo(new SoloSettings(), 41, 42);
                AddGalacticLine(dir, 0);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(0), "알림을 보기 전의 판은 올리지 않는다 [가정 #650]");
                title = stage.Current as TitleScreen;
                Assert.That(title.NoticeShown, Is.True, "답하기 전에는 다시 뜬다");

                // ── [확인]: 창이 걷히고 다시 뜨지 않는다(한 번) · 이제 올린다(기본 켜짐) ──
                Assert.That(title.AnswerNotice(false), Is.True);
                Assert.That(prefs.PlayNotice, Is.True);
                Assert.That(stage.Root.Q("title-notice"), Is.Null, "창이 걷혔다");
                Assert.That(title.Activate(0), Is.True, "메뉴가 다시 눌린다");
                flow.OpenTitle();
                Assert.That(((TitleScreen)stage.Current).NoticeShown, Is.False, "알림은 한 번만");
                Assert.That(flow.RecordsOn, Is.True);
                flow.StartSolo(new SoloSettings(), 51, 52);
                AddGalacticLine(dir, 0);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Sent.Count, Is.EqualTo(1), "기본 켜짐 — 판을 떠날 때 올린다");

                // ── 설정 줄: 켬이 골라져 있다 → 끔 · 끄면 올리지 않는다 ──
                flow.OpenSettings();
                var set = (SettingsScreen)stage.Current;
                Assert.That(stage.Root.Q("settings-row-upload"), Is.Not.Null, "「플레이 기록 보내기」 줄");
                Assert.That(stage.Root.Q("settings-opt-upload-on").ClassListContains("sn-opt-picked"), Is.True);
                set.PickUpload(false);
                Assert.That(prefs.PlayUpload, Is.False);
                Assert.That(stage.Root.Q("settings-opt-upload-off").ClassListContains("sn-opt-picked") && !stage.Root.Q("settings-opt-upload-on").ClassListContains("sn-opt-picked"), Is.True, "끔이 골라졌다");
                Assert.That(flow.RecordsOn, Is.False);
                int calls = fake.Calls;
                flow.StartSolo(new SoloSettings(), 61, 62);
                AddGalacticLine(dir, 0);
                flow.OpenTitle();
                yield return Wait(uploads.Idle);
                Assert.That(fake.Calls, Is.EqualTo(calls), "끄면 올리지 않는다");

                // ── [내 기록 지우기]: 물음 → [취소] → 다시 → [지우기] → 결과 한 줄 ──
                flow.OpenSettings();
                set = (SettingsScreen)stage.Current;
                Assert.That(set.AskDelete(), Is.True);
                Assert.That(stage.Root.Q("settings-upload-ask") != null && stage.Root.Q("settings-upload-delete-yes") != null, Is.True, "지우기 물음");
                set.CancelDelete();
                Assert.That(stage.Root.Q("settings-upload-ask"), Is.Null);
                Assert.That(fake.Deleted, Is.EqualTo(0), "[취소]는 지우지 않는다");
                set.AskDelete();
                var del = set.DeleteRecords();
                yield return Wait(del);
                Assert.That(del.Result, Is.EqualTo(1));
                Assert.That(fake.Deleted, Is.EqualTo(1), "지우기 호출");
                Assert.That(set.Deletion, Is.EqualTo(SettingsScreen.DeleteStep.Done));
                var result = stage.Root.Q<Label>("settings-upload-result");
                Assert.That(result != null && result.text.Contains("1"), Is.True, "결과 한 줄: " + result?.text);

                // ── [설정 열기]: 알림을 본 것으로 적고 설정 화면으로 ──
                prefs.PlayNotice = false;
                prefs.PlayUpload = true;
                flow.OpenTitle();
                Assert.That(((TitleScreen)stage.Current).AnswerNotice(true), Is.True);
                Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>(), "[설정 열기] → 설정");
                Assert.That(prefs.PlayNotice, Is.True);

                // ── 방 로비: 방장이 켰으면 알림 줄(손님 · 방장 모두), 끄면 없다 ──
                foreach (bool host in new[] { false, true })
                {
                    flow.OpenTitle();
                    flow.Room.ShowLobby(FakeLobby.With(true, host));
                    yield return null;
                    var line = stage.Root.Q<Label>("lobby-records");
                    Assert.That(line != null && line.text.Length > 0, Is.True, (host ? "방장" : "손님") + " 로비의 알림 줄");
                    flow.OpenTitle();
                    flow.Room.ShowLobby(FakeLobby.With(false, host));
                    yield return null;
                    Assert.That(stage.Root.Q("lobby-records"), Is.Null, "보내지 않는 방에는 줄이 없다");
                }
                flow.OpenTitle();

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static IEnumerator WaitReal(Task task, float seconds, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < seconds) yield return null;
            Assert.That(task.IsCompleted, Is.True, what + "이(가) " + seconds + "초 안에 끝나지 않았다");
            Assert.That(task.IsFaulted, Is.False, what + ": " + task.Exception);
        }

        /// <summary>
        /// 실제 UGS 한 바퀴(관문 밖 — 인터넷 · 대시보드 Cloud Save 켬 필요): 시험 프로필의 익명 로그인 → 실제 솔로 판(SoloTrail.Defeat — 갤럭시단 [도전]까지)을 화면 흐름으로 →
        /// 게임 메뉴 [버그 신고](bug_ — 시험 로그 파일) → 타이틀로 떠나며 플레이 기록(rec_) → 서버 목록에 둘 다 → [내 기록 지우기] → 목록 0.
        /// 환경 변수 SINNOH_UGS_E2E_HOLD=&lt;폴더&gt;를 주면 올린 뒤 그 폴더에 uploaded.txt(키 · 크기)를 쓰고 go.txt가 생길 때까지(최대 20분) 지우기를 미룬다 — 그 사이 걷기 도구(Tools/play-records.mjs)를 돌린다.
        /// 저장 · 로그는 임시 폴더, 설정은 메모리(사용자 PlayerPrefs에 쓰지 않음 — 세션 토큰만 UGS가 시험 프로필 키로 둔다).
        /// </summary>
        [UnityTest, Explicit("관문 밖 — 실제 UGS Cloud Save를 친다(대시보드에서 Cloud Save를 켠 뒤 손으로)"), Category("Online"), NUnit.Framework.Timeout(1500000)]
        public IEnumerator M9_Online_플레이기록버그신고_실제UGS에올리고목록보고지운다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || msg.StartsWith("[PlayUploads]")) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-ugs-e2e-" + Guid.NewGuid().ToString("N"));
            string hold = Environment.GetEnvironmentVariable("SINNOH_UGS_E2E_HOLD");
            UgsBoot.ProfileOverride = "sinnoh-upload-test";
            var up = new UgsPlayUploader();
            try
            {
                // ── 시작 전: 이 시험 플레이어의 남은 파일을 비운다(앞 실행이 중간에 멈췄을 때) ──
                var clean = up.DeleteAllAsync();
                yield return WaitReal(clean, 90, "익명 로그인 + 처음 비우기");
                Debug.Log($"[UGS e2e] 익명 로그인: 플레이어 {UgsBoot.PlayerId} · 남아 있던 파일 {clean.Result}개 지움");
                Assert.That(UgsBoot.PlayerId, Is.Not.Null.And.Not.Empty, "익명 로그인");

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
                boot.UseSaves(root, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var flow = boot.Flow;
                var stage = flow.Stage;
                Assert.That(flow.SoundPrefs, Is.Not.InstanceOf<StoredSoundPrefs>(), "시험은 사용자의 PlayerPrefs에 쓰지 않는다");
                Directory.CreateDirectory(root);
                string log = Path.Combine(root, "Player.log");
                File.WriteAllText(log, "[UGS e2e] 시험 로그 첫 줄\n[UGS e2e] 시험 로그 끝 줄\n", new UTF8Encoding(false));
                flow.BugLogPath = log;
                var uploads = flow.Uploads = new PlayUploads(up, "0.0.e2e", () => true);

                // ── 실제 솔로 판: 갤럭시단이 답한 [도전] 배틀이 끝나고 보드 입력이 열릴 때까지 ──
                bool galactic = false;
                flow.EventsSeen += evs =>
                {
                    foreach (var e in evs)
                        if (e is ChoiceAnswered ca && ca.Seat == SeatId.Galactic) galactic = true;
                };
                var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                string dir = Path.Combine(root, GameFlow.SoloSlot);
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && !(galactic && flow.InputOpen) && Time.realtimeSinceStartup - t0 < 300f; guard++)
                {
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                    if (!galactic && guard % 200 == 199) galactic = PlayRecord.HadGalacticBattle(SaveStore.ReadLog(dir));
                }
                Assert.That(galactic && flow.InputOpen, Is.True, "갤럭시단 배틀 뒤 보드로 돌아오지 못했다");

                // ── 버그 신고: 게임 메뉴 → 신고 쪽 → 설명 → [보내기] = 실제 업로드 ──
                flow.OpenMenu();
                var menu = (BoardMenuScreen)stage.Current;
                Assert.That(menu.OpenBug(), Is.True, "메뉴의 [버그 신고]");
                menu.Bug.SetText("[시험] 실제 UGS 한 바퀴 확인 — 지워질 신고");
                var sent = menu.Bug.Send();
                yield return WaitReal(sent, 90, "버그 신고 보내기");
                Assert.That(sent.Result, Is.True, "버그 신고를 보냈다");
                string bugKey = menu.Bug.Pack.FileName;
                int bugBytes = menu.Bug.Pack.Bytes.Length;
                Assert.That(menu.Bug.Pack.Info.RecordNote, Is.Null, "지금 판 기록이 실렸다");
                menu.CloseBug();
                flow.CloseMenu();

                // ── 플레이 기록: 타이틀로 떠나면 올린다(실제 경로 GameFlow → PlayUploads.Offer) ──
                var pack = PlayRecord.Pack(dir, "0.0.e2e");
                Assert.That(pack.Bytes, Is.Not.Null, "갤럭시단 배틀이 있는 판: " + pack.Skip);
                flow.OpenTitle();
                yield return WaitReal(uploads.Idle, 90, "플레이 기록 올리기");
                Assert.That(PlayRecord.Uploaded(dir), Is.GreaterThan(0), "올린 표시(올리기 성공) — 실패 로그: " + string.Join(" | ", errors));
                string recKey = pack.FileName;

                // ── 서버 목록: 둘 다 있다 ──
                var list = CloudSaveService.Instance.Files.Player.ListAllAsync();
                yield return WaitReal(list, 60, "목록");
                var keys = new List<string>();
                foreach (var f in list.Result) keys.Add(f.Key + " " + f.Size + "B");
                Debug.Log("[UGS e2e] 올린 뒤 목록: " + string.Join(", ", keys));
                Assert.That(list.Result.Exists(f => f.Key == recKey), Is.True, "rec_ 파일이 목록에: " + string.Join(", ", keys));
                Assert.That(list.Result.Exists(f => f.Key == bugKey && f.Size == bugBytes), Is.True, "bug_ 파일이 목록에(크기 그대로): " + string.Join(", ", keys));

                // ── (걷기 도구를 돌릴 동안) 지우기를 미룬다 ──
                if (!string.IsNullOrEmpty(hold))
                {
                    Directory.CreateDirectory(hold);
                    string go = Path.Combine(hold, "go.txt");
                    if (File.Exists(go)) File.Delete(go);
                    File.WriteAllText(Path.Combine(hold, "uploaded.txt"), "player " + UgsBoot.PlayerId + "\n" + string.Join("\n", keys) + "\n", new UTF8Encoding(false));
                    float h0 = Time.realtimeSinceStartup;
                    while (!File.Exists(go) && Time.realtimeSinceStartup - h0 < 1200f) yield return null;
                }

                // ── [내 기록 지우기] → 목록 0 ──
                var del = uploads.DeleteMine();
                yield return WaitReal(del, 90, "지우기");
                Assert.That(del.Result, Is.GreaterThanOrEqualTo(2), "rec_ · bug_ 둘 다 지웠다");
                var after = CloudSaveService.Instance.Files.Player.ListAllAsync();
                yield return WaitReal(after, 60, "지운 뒤 목록");
                Assert.That(after.Result.FindAll(f => PlayRecord.Ours(f.Key)).Count, Is.EqualTo(0), "지운 뒤 이 게임의 파일 0");
                Debug.Log($"[UGS e2e] 지움 {del.Result}개 · 남은 파일 {after.Result.Count}개");
                if (!string.IsNullOrEmpty(hold)) File.WriteAllText(Path.Combine(hold, "deleted.txt"), "deleted " + del.Result + "\nleft " + after.Result.Count + "\n", new UTF8Encoding(false));

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
