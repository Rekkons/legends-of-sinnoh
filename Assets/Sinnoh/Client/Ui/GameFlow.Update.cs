using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace Sinnoh.Client
{
    /// <summary>자동 업데이트 단계(결정 161 4-2): 알림만 · 견주기 · 받기 · 다 받음(도우미 넘기기) · 다시 시작 · 실패.</summary>
    public enum UpdateStep { Idle, Scan, Fetch, Ready, Restart, Failed }

    /// <summary>타이틀 알림 상자(<c>title-update</c>)가 그리는 것 — 흐름이 고치고 <see cref="TitleScreen.RefreshUpdate"/>로 다시 그린다.</summary>
    public sealed class UpdateView
    {
        public string Version;
        public UpdateStep Step;
        public long Got, Total;
        /// <summary>실패 까닭 한 줄(공간 부족 · 한도 — 그 밖은 null).</summary>
        public string FailLine;
        /// <summary>[받기](GitHub) · [업데이트](null이면 [받기]만) · [취소] · [다시 시도](null이면 없음).</summary>
        public Action Download, Start, Cancel, Retry;

        /// <summary>메뉴를 막는 단계(견주기 · 받기 · 다 받음 · 다시 시작).</summary>
        public bool Busy => Step == UpdateStep.Scan || Step == UpdateStep.Fetch || Step == UpdateStep.Ready || Step == UpdateStep.Restart;
    }

    // 자동 업데이트 2단계(결정 161 — 설계 4-2): 서명된 판 정보 → 설치 견주기(다른 스레드) → 받기 계획 → 받기(UpdateFetch · UpdateQueue) → 도우미 넘기기(plan.json · run/) → 「다시 시작합니다」 → 게임 끄기.
    // 사람이 하는 PC 플레이어의 타이틀에서만(InstallDir이 「우리 설치」일 때 — 에디터 · 웹 손님판은 null). 쓰기가 안 되거나 판이 자동을 허락하지 않으면 지금처럼 [받기].
    public sealed partial class GameFlow
    {
        /// <summary>지금 단계.</summary>
        public UpdateStep Updating { get; private set; }

        /// <summary>실패 까닭(space · later · 그 밖 — 로그용).</summary>
        public string UpdateWhy { get; private set; }

        /// <summary>이 판을 [업데이트]로 받을 수 있나(판 정보 · 설치 · 쓰기 권한을 본 뒤).</summary>
        public bool AutoUpdate { get; private set; }

        /// <summary>
        /// 설치 폴더(<c>Application.dataPath</c>의 부모가 「우리 설치」일 때 — <see cref="UpdateHandoff.InstallOf"/>, 아니면 null = 자동 업데이트 없음). 시험은 임시 폴더를 넣는다.
        /// </summary>
        public string InstallDir
        {
            get
            {
                if (!installSet)
                {
                    installSet = true;
                    if (!Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer)
                        installDir = UpdateHandoff.InstallOf(Application.dataPath, Application.version, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                }
                return installDir;
            }
            set
            {
                installSet = true;
                installDir = value;
            }
        }

        /// <summary>판 정보 서명 확인 키(기본 <see cref="ReleaseFeed.Key"/> — 끝에서 끝 프로브 · 시험만 RFC 8032 시험 키).</summary>
        public byte[] FeedKey { get; set; } = ReleaseFeed.Key;

        /// <summary>http://127.0.0.1 · localhost 주소를 받아들이나(끝에서 끝 프로브 · 시험 — 4-4).</summary>
        public bool FeedLoopback { get; set; }

        /// <summary>404 뒤 다시 읽을 판 정보 주소.</summary>
        public string FeedUrl { get; set; } = ReleaseFeed.Url;

        /// <summary>도우미를 띄운다(실행 파일, 인자 한 줄) — 기본 <c>Process.Start</c>(창 없음 · UseShellExecute 끔). 시험이 바꾼다. 실패면 거짓.</summary>
        public Func<string, string, bool> StartHelper { get; set; } = RunHelper;

        /// <summary>도우미를 띄운 뒤 게임을 끈다(기본 흐름의 quit 또는 <c>Application.Quit</c>). 시험이 바꾼다.</summary>
        public Action QuitGame
        {
            get => quitGame ?? quit ?? Application.Quit;
            set => quitGame = value;
        }

        /// <summary>설치 드라이브 여유 바이트(-1 = 모름). 시험이 바꾼다.</summary>
        public Func<string, long> FreeSpace { get; set; } = UpdateHandoff.FreeBytes;

        /// <summary>다시 켤 때 넘길 인자(기본 = 이 실행의 인자 — 실행 파일 이름 뺌).</summary>
        public Func<IList<string>> RestartArgs { get; set; } = () => new List<string>(Environment.GetCommandLineArgs()).GetRange(1, Math.Max(0, Environment.GetCommandLineArgs().Length - 1));

        /// <summary>재시도 간격(ms, null = <see cref="UpdateQueue.RetryMs"/>) · 진행 없음 끊기(ms) · 「다시 시작합니다」를 보이는 시간(ms) — 시험이 줄인다.</summary>
        public int[] UpdateRetryMs { get; set; }
        public int UpdateStallMs { get; set; } = 30000;
        public int RestartMs { get; set; } = 800;

        /// <summary>단계가 바뀔 때(단계, 까닭) — 끝에서 끝 프로브가 적는다.</summary>
        public event Action<UpdateStep, string> UpdateChanged;

        /// <summary>받은 바이트 · 받을 바이트(진행 막대).</summary>
        public long UpdateGot => queue?.Got ?? 0;
        public long UpdateTotal => plan?.Download ?? 0;

        Action quitGame;
        string installDir;
        bool installSet;
        ReleaseManifest release;
        readonly UpdateView updateView = new UpdateView();
        CancellationTokenSource updateCancel;
        Task<InstallScan> scanTask;
        Task<string> unzipTask;
        InstallScan scan;
        UpdatePlan plan;
        UpdateQueue queue;
        UpdateFetch fetcher;
        UnityWebRequest feedReq;
        bool refreshed;
        long updateMs, restartAt;
        string failLine;

        string Stage_ => StageStore.Dir(InstallDir);

        /// <summary>새 판 manifest를 받았다(<see cref="TakeRelease"/>): [업데이트]로 받을 수 있는지 정한다.</summary>
        void TakeManifest(ReleaseManifest m)
        {
            release = m;
            string why = WebGuest ? "web" : InstallDir == null ? "install" : UpdatePlan.Refuse(m, Application.version, FeedLoopback) ?? (UpdateHandoff.CanWrite(InstallDir) ? null : "write");
            AutoUpdate = why == null;
            Debug.Log(AutoUpdate ? "[Update] 자동 업데이트 가능 " + m.Version : "[Update] 자동 업데이트 안 함 — " + why);
        }

        /// <summary>[업데이트] · [다시 시도]: 견주기부터. 타이틀에서만 · 이미 하는 중이면 거짓.</summary>
        public bool StartUpdate()
        {
            if (!AutoUpdate || release == null || updateView.Busy || !(Stage.Current is TitleScreen)) return false;
            refreshed = false;
            Scan();
            return true;
        }

        /// <summary>[취소]: 견주기 · 받기를 멈추고 알림으로(받은 조각은 남는다 — 다음에 이어받는다).</summary>
        public bool CancelUpdate()
        {
            if (Updating != UpdateStep.Scan && Updating != UpdateStep.Fetch) return false;
            Halt();
            Step(UpdateStep.Idle, "cancel");
            return true;
        }

        /// <summary>켤 때 읽은 도우미 결과가 실패였다(되돌린 옛 판이 켜졌다) — 타이틀에 실패 알림.</summary>
        public void UpdateFailedBefore(UpdateResult r)
        {
            NewVersion = r.Version;
            Fail(string.IsNullOrEmpty(r.Why) ? "helper" : r.Why);
        }

        /// <summary>확인 · 캡처용: 서버 없이 알림 상자를 그 단계로 그린다(버튼은 아무 일도 하지 않는다).</summary>
        public void FakeUpdate(string version, UpdateStep step, long got = 0, long total = 0, string failLine = null)
        {
            NewVersion = version;
            AutoUpdate = true;
            Updating = step;
            this.failLine = failLine;
            fakeGot = got;
            fakeTotal = total;
            faking = true;
            SyncUpdate();
        }

        bool faking;
        long fakeGot, fakeTotal;

        // 타이틀이 그리는 상자 값을 맞추고 다시 그린다
        void SyncUpdate()
        {
            var v = updateView;
            v.Version = NewVersion;
            v.Step = Updating;
            v.Got = faking ? fakeGot : UpdateGot;
            v.Total = faking ? fakeTotal : UpdateTotal;
            v.FailLine = failLine;
            v.Download = DownloadRelease;
            v.Start = AutoUpdate ? (faking ? (Action)(() => { }) : () => StartUpdate()) : null;
            v.Retry = v.Start;
            v.Cancel = faking ? (Action)(() => { }) : () => CancelUpdate();
            (Stage.Current as TitleScreen)?.ShowUpdate(v);
        }

        UpdateView UpdateViewOrNull => NewVersion != null ? updateView : null;

        void Step(UpdateStep step, string why = null)
        {
            Updating = step;
            if (step != UpdateStep.Failed) failLine = null;
            SyncUpdate();
            UpdateChanged?.Invoke(step, why);
        }

        void Fail(string why)
        {
            Halt();
            UpdateWhy = why;
            failLine = why == "space" ? Stage.T("screen.title.update.fail.space").Replace("{mb}", Mb(plan?.Disk ?? 0, up: true))
                : why == "later" ? Stage.T("screen.title.update.fail.later") : null;
            Debug.Log("[Update] 실패 — " + why);
            Updating = UpdateStep.Failed;
            SyncUpdate();
            UpdateChanged?.Invoke(UpdateStep.Failed, why);
        }

        void Halt()
        {
            updateCancel?.Cancel();
            fetcher?.StopAll();
            queue?.Cancel();
            if (feedReq != null)
            {
                feedReq.Abort();
                feedReq.Dispose();
                feedReq = null;
            }
            scanTask = null;
            unzipTask = null;
        }

        /// <summary>MB 글(소수 한 자리 — 진행 막대 「12.3 / 28.8 MB」 · 공간 부족은 올림 정수).</summary>
        public static string Mb(long bytes, bool up = false) =>
            up ? ((bytes + (1 << 20) - 1) >> 20).ToString(System.Globalization.CultureInfo.InvariantCulture) : (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        void Scan()
        {
            Halt();
            plan = null;
            queue = null;
            fetcher = null;
            updateCancel = new CancellationTokenSource();
            var token = updateCancel.Token;
            var m = release;
            string dir = InstallDir;
            scanTask = Task.Run(() => InstallScan.Run(dir, m, null, token));
            Step(UpdateStep.Scan);
        }

        // 프레임마다(GameFlow.Tick 맨 앞 — 타이틀에는 판이 없다)
        void TickUpdate(int dtMs)
        {
            if (faking || !updateView.Busy) return;
            updateMs += Math.Max(0, dtMs);
            try
            {
                switch (Updating)
                {
                    case UpdateStep.Scan:
                        if (scanTask != null && scanTask.IsCompleted) Planned();
                        break;
                    case UpdateStep.Fetch:
                        Fetching();
                        break;
                    case UpdateStep.Restart:
                        if (updateMs >= restartAt)
                        {
                            restartAt = long.MaxValue;
                            QuitGame();
                        }
                        break;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.Log("[Update] 파일 오류 — " + e.Message);
                Fail("io");
            }
        }

        void Planned()
        {
            var t = scanTask;
            scanTask = null;
            if (t.Status != TaskStatus.RanToCompletion)
            {
                Debug.Log("[Update] 견주기 실패 — " + t.Exception?.GetBaseException().Message);
                Fail("scan");
                return;
            }
            scan = t.Result;
            string stage = Stage_;
            Directory.CreateDirectory(stage);
            plan = UpdatePlan.Make(release, Application.version, scan.Files, scan.Disk, StageStore.Staged(stage, release.Files), FeedLoopback);
            if (plan.Why != null)
            {
                Fail(plan.Why);
                return;
            }
            long free = FreeSpace(InstallDir);
            if (free >= 0 && plan.Disk > free)
            {
                Fail("space");
                return;
            }
            Debug.Log("[Update] 계획 " + plan.Version + " — 바꿈 " + plan.Put.Count + " · 받기 " + plan.Fetch.Count + (plan.UseZip ? "(zip)" : "") + " " + Mb(plan.Download) + " MB · 지움 " + plan.Delete.Count);
            var jobs = new List<UpdateJob>();
            if (plan.UseZip)
            {
                if (plan.Fetch.Count > 0 && !StageStore.Staged(stage, new[] { plan.Zip }).Contains(plan.Zip.Sha256)) jobs.Add(new UpdateJob(plan.Zip.Sha256, plan.Zip.Size, UpdatePlan.UrlOf(release, plan.Zip, true)));
            }
            else
                foreach (var f in plan.Fetch) jobs.Add(new UpdateJob(f.Sha256, f.Size, UpdatePlan.UrlOf(release, f, false)));
            fetcher = new UpdateFetch(stage, () => updateMs) { StallMs = UpdateStallMs };
            queue = new UpdateQueue(jobs, fetcher);
            if (UpdateRetryMs != null) queue.RetryMs = UpdateRetryMs;
            Step(UpdateStep.Fetch);
        }

        void Fetching()
        {
            if (feedReq != null)
            {
                Refreshing();
                return;
            }
            if (unzipTask != null)
            {
                if (!unzipTask.IsCompleted) return;
                var t = unzipTask;
                unzipTask = null;
                string why = t.Status == TaskStatus.RanToCompletion ? t.Result : "unzip";
                if (why != null)
                {
                    Fail("zip-" + why);
                    return;
                }
                TryDelete(StageStore.FileOf(Stage_, plan.Zip.Sha256));
                Handoff();
                return;
            }
            fetcher.Poll(queue);
            queue.Tick(updateMs);
            (Stage.Current as TitleScreen)?.Progress(UpdateGot, UpdateTotal);
            switch (queue.State)
            {
                case QueueState.Done:
                    if (plan.UseZip && plan.Fetch.Count > 0)
                    {
                        string zip = StageStore.FileOf(Stage_, plan.Zip.Sha256), stage = Stage_;
                        var m = release;
                        var want = plan.Fetch;
                        var token = updateCancel.Token;
                        unzipTask = Task.Run(() => StageStore.Unzip(zip, m, want, stage, token));
                    }
                    else Handoff();
                    break;
                case QueueState.Later:
                    Fail("later");
                    break;
                case QueueState.Failed:
                    Fail("fetch");
                    break;
                case QueueState.Refresh:
                    if (refreshed || string.IsNullOrEmpty(FeedUrl))
                    {
                        Fail("missing");
                        break;
                    }
                    refreshed = true;
                    Debug.Log("[Update] 404 — 판 정보를 다시 읽는다");
                    feedReq = UnityWebRequest.Get(FeedUrl);
                    feedReq.timeout = 15;
                    feedReq.SendWebRequest();
                    break;
            }
        }

        // 404 뒤 판 정보 다시 읽기: 서명이 맞고 지금보다 새 판이면 그 판으로 다시 견주기부터
        void Refreshing()
        {
            if (!feedReq.isDone) return;
            var req = feedReq;
            feedReq = null;
            string text = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
            req.Dispose();
            var m = text != null ? ReleaseFeed.Read(text, FeedKey, out _) : null;
            if (m == null || UpdatePlan.Refuse(m, Application.version, FeedLoopback) != null)
            {
                Fail("feed");
                return;
            }
            release = m;
            NewVersion = m.Version;
            Scan();
        }

        // 다 받고 확인함: plan.json · run/ 도우미 → 도우미 띄우기 → 「다시 시작합니다」 → 끄기
        void Handoff()
        {
            Step(UpdateStep.Ready);
            string exe = UpdateHandoff.RunHelper(InstallDir, release, scan.Files, Stage_, out string why);
            if (exe == null)
            {
                Fail(why);
                return;
            }
            string planFile = UpdateHandoff.Write(InstallDir, release, plan, System.Diagnostics.Process.GetCurrentProcess().Id, UpdatePlan.Exe, RestartArgs());
            PlayerPrefs.Save();
            if (!StartHelper(exe, UpdateHandoff.Args(InstallDir, planFile)))
            {
                Fail("start");
                return;
            }
            Debug.Log("[Update] 도우미를 띄웠다 — 다시 시작");
            restartAt = updateMs + RestartMs;
            Step(UpdateStep.Restart);
        }

        static bool RunHelper(string exe, string args)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
                using (System.Diagnostics.Process.Start(info)) { }
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[Update] 도우미를 띄우지 못함 — " + e.Message);   // 백신 · 스마트 앱 컨트롤
                return false;
            }
        }

        static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
        }
    }
}
