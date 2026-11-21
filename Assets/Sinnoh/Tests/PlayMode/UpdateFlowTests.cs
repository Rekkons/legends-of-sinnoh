using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 자동 업데이트 2단계(결정 161 7-2)를 화면 흐름 + 진짜 <see cref="UpdateFetch"/>(UnityWebRequest)로: 시험 안의 127.0.0.1 HTTP 서버(<see cref="FakeHttp"/>) · 임시 설치 폴더 ·
    /// RFC 8032 시험 키로 그 자리에서 서명한 판 정보. 도우미 띄우기(<see cref="GameFlow.StartHelper"/>) · 게임 끄기(<see cref="GameFlow.QuitGame"/>)는 가짜 — 진짜 프로세스를 띄우지 않는다.
    /// 사용자 저장 폴더 · 레지스트리 · 설치 폴더에 쓰지 않는다. 실제 R2 · 네트워크에 나가지 않는다.
    /// </summary>
    public sealed class UpdateFlowTests
    {
        const string NewVersion = "99.0.0";

        // Kit.Wrapped가 한글 낱말 안에 넣는 줄바꿈 막기 글자
        static readonly string WordJoiner = ((char)0x2060).ToString();

        sealed class Rig
        {
            public ScreenBoot Boot;
            public GameFlow Flow;
            public RenderTexture Rt;
            public FakeHttp Http;
            public string Install;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Helper = new List<string>();
            public int Quits;
            public readonly List<string> Opened = new List<string>();
            public VisualElement Root => Flow.Stage.Root;
            public TitleScreen Title => Flow.Stage.Current as TitleScreen;

            public void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(type + ": " + msg);
            }
        }

        static string ShaOf(byte[] b) => InstallScan.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(b));

        static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        static byte[] Pattern(int n, int seed)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(i * 31 + seed);
            return b;
        }

        // manifest 글(K1 — store · auto) — files는 (경로, 내용)
        static string Manifest(string url, IEnumerable<KeyValuePair<string, byte[]>> files, KeyValuePair<string, byte[]>? zip = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\"app\":\"sinnoh\",\"version\":\"" + NewVersion + "\",\"date\":\"2026-11-21\",\"page\":\"" + ReleaseFeed.Page + "\",\"base\":\"" + url + "releases/" + NewVersion + "/\",\"store\":\"" + url + "files/\",\"auto\":true,\"packages\":[");
            if (zip.HasValue) sb.Append("{\"path\":\"" + zip.Value.Key + "\",\"size\":" + zip.Value.Value.Length + ",\"sha256\":\"" + ShaOf(zip.Value.Value) + "\"}");
            sb.Append("],\"files\":[");
            bool first = true;
            foreach (var f in files)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"path\":\"" + f.Key + "\",\"size\":" + f.Value.Length + ",\"sha256\":\"" + ShaOf(f.Value) + "\"}");
            }
            return sb.Append("]}").ToString();
        }

        static IEnumerator Open(Rig rig)
        {
            Application.logMessageReceived += rig.Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                rig.Boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (rig.Boot != null && rig.Boot.Root != null && rig.Boot.Root.Stage.Root != null) break;
            }
            Assert.That(rig.Boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            rig.Boot.PointerFromMouse = false;
            rig.Rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.Rt.Create();
            rig.Boot.Root.SetTarget(rig.Rt);
            for (int i = 0; i < 4; i++) yield return null;
            var flow = rig.Flow = rig.Boot.Flow;
            Assert.That(flow.InstallDir, Is.Null, "에디터는 「우리 설치」가 아니다 — 자동 업데이트 없음");
            rig.Http = new FakeHttp();
            rig.Install = Path.Combine(Path.GetTempPath(), "sinnoh-update-" + Guid.NewGuid().ToString("N"), "Legends of Sinnoh");
            Directory.CreateDirectory(rig.Install);
            flow.InstallDir = rig.Install;
            flow.FeedKey = TestEd25519.PublicKey(TestEd25519.Seed);
            flow.FeedLoopback = true;
            flow.FeedUrl = rig.Http.Url + "latest.json";
            flow.StartHelper = (exe, args) =>
            {
                rig.Helper.Add(exe);
                rig.Helper.Add(args);
                return true;
            };
            flow.QuitGame = () => rig.Quits++;
            flow.OpenUrl = rig.Opened.Add;
            flow.UpdateRetryMs = new[] { 100, 100, 100 };
            flow.RestartMs = 50;
            flow.UpdateStallMs = 5000;
            flow.OpenTitle(TitleScreen.Solo);
        }

        static void Close(Rig rig)
        {
            Application.logMessageReceived -= rig.Grab;
            rig.Http?.Dispose();
            if (rig.Rt != null)
            {
                rig.Rt.Release();
                Object.Destroy(rig.Rt);
            }
            if (rig.Install != null)
                try { Directory.Delete(Path.GetDirectoryName(rig.Install), true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            rig.Flow?.OpenTitle();
        }

        static IEnumerator Until(Func<bool> done, string what, float seconds = 20f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }

        static void Lay(string install, string rel, byte[] data)
        {
            string full = Path.Combine(install, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, data);
        }

        [UnityTest]
        public IEnumerator M10_자동업데이트_업데이트단추_받기_끊긴뒤이어받기_지문확인_메뉴막힘_도우미인자_다시시작()
        {
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                string install = rig.Install;
                // 옛 설치(0.1.0 — 에디터 판) · 맨 위 사용자 파일 · 지울 자리의 낡은 파일
                byte[] same = Pattern(5000, 3), big = Pattern(300000, 7);
                Lay(install, "Sinnoh.exe", Bytes("old-exe"));
                Lay(install, UpdatePlan.Helper, Bytes("old-helper"));
                Lay(install, "version.txt", Bytes(Application.version));
                Lay(install, "Sinnoh_Data/a.bin", Bytes("old-a"));
                Lay(install, "Sinnoh_Data/same.bin", same);
                Lay(install, "Sinnoh_Data/old.bin", Bytes("stale"));
                Lay(install, "notes.txt", Bytes("사용자 메모"));
                var files = new Dictionary<string, byte[]>
                {
                    ["Sinnoh.exe"] = Bytes("new-exe"), [UpdatePlan.Helper] = Bytes("new-helper"), ["version.txt"] = Bytes(NewVersion),
                    ["Sinnoh_Data/a.bin"] = big, ["Sinnoh_Data/same.bin"] = same, ["WebGL/dup.bin"] = big,
                };
                var bySha = new Dictionary<string, byte[]>();
                foreach (var f in files) bySha[ShaOf(f.Value)] = f.Value;
                string latest = TestEd25519.Latest(Manifest(rig.Http.Url, files));
                int bigHits = 0;
                rig.Http.Handle = path =>
                {
                    if (path == "/latest.json") return new FakeHttp.Reply { Body = Bytes(latest) };
                    string sha = path.StartsWith("/files/", StringComparison.Ordinal) ? path.Substring(7) : null;
                    if (sha == null || !bySha.TryGetValue(sha, out var body)) return new FakeHttp.Reply { Code = 404 };
                    if (body == big && System.Threading.Interlocked.Increment(ref bigHits) == 1) return new FakeHttp.Reply { Body = body, CutAfter = 100000 };   // 처음엔 중간에 끊는다
                    if (body.Length == 10) return new FakeHttp.Reply { Body = body, DelayMs = 1500 };   // 도우미 — 느린 응답(받는 동안 메뉴가 막혔는지 본다)
                    return new FakeHttp.Reply { Body = body };
                };

                // 서명이 맞고 새 판 · 쓰기 되는 설치 → [업데이트](마우스 오버 = 바뀐 파일만 받고 다시 시작)
                Assert.That(TestEd25519.PublicKey(TestEd25519.Seed), Is.EqualTo(TestEd25519.Hex(UpdateProbe.TestKeyHex)), "RFC 8032 시험 1 공개 키");
                Assert.That(Ed25519.Verify(flow.FeedKey, Bytes("x"), TestEd25519.Sign(TestEd25519.Seed, Bytes("x"))), Is.True, "시험 서명기 = 게임의 확인");
                flow.TakeRelease(latest);
                yield return null;
                Assert.That(flow.AutoUpdate, Is.True);
                Assert.That(rig.Root.Q<Button>("title-update-do"), Is.Not.Null, "[업데이트]");
                Assert.That(rig.Root.Q<Button>("title-update-get"), Is.Null, "자동 업데이트가 되면 [받기] 대신");
                Assert.That(rig.Root.Q<Label>("title-update-text").text, Does.Contain(NewVersion));

                Assert.That(rig.Title.PressUpdate(), Is.True);
                Assert.That(flow.Updating, Is.EqualTo(UpdateStep.Scan));
                yield return Until(() => flow.Updating == UpdateStep.Fetch, "받기 시작");
                // 받는 동안: 같은 상자가 진행 판 · 덮개가 메뉴를 막는다
                Assert.That(rig.Root.Q("title-update-block"), Is.Not.Null, "메뉴를 막는 덮개");
                Assert.That(rig.Root.Q("title-update-bar"), Is.Not.Null);
                Assert.That(rig.Root.Q<Label>("title-update-amount").text, Does.EndWith(" MB"));
                Assert.That(rig.Title.UpdateBusy, Is.True);
                int solo = new List<TitleScreen.Item>(rig.Title.Shown).FindIndex(i => i.Id == TitleScreen.Solo);
                Assert.That(rig.Title.Activate(solo), Is.False, "받는 동안 「혼자 하기」를 누르지 못한다");
                Assert.That(flow.StartUpdate(), Is.False, "이미 하는 중");

                yield return Until(() => flow.Updating == UpdateStep.Restart || flow.Updating == UpdateStep.Failed, "다 받기", 30f);
                Assert.That(flow.Updating, Is.EqualTo(UpdateStep.Restart), "실패: " + flow.UpdateWhy);
                Assert.That(rig.Root.Q<Label>("title-update-text").text, Is.EqualTo(flow.Stage.T("screen.title.update.restart")));
                yield return Until(() => rig.Quits > 0, "게임 끄기");
                Assert.That(rig.Quits, Is.EqualTo(1), "「다시 시작합니다」 뒤 한 번");

                // 받기: 같은 내용 두 경로는 한 번 · 바뀌지 않은 파일은 안 받음 · 끊긴 것은 Range로 이어받음
                var log = rig.Http.Log;
                var fileGets = log.FindAll(l => l.StartsWith("/files/", StringComparison.Ordinal));
                Assert.That(fileGets.Count, Is.EqualTo(5), string.Join("\n", log));
                Assert.That(fileGets.Exists(l => l.StartsWith("/files/" + ShaOf(same), StringComparison.Ordinal)), Is.False, "바뀌지 않은 파일");
                Assert.That(fileGets.FindAll(l => l.StartsWith("/files/" + ShaOf(big), StringComparison.Ordinal)).Count, Is.EqualTo(2), "끊긴 뒤 한 번 더");
                Assert.That(fileGets.Exists(l => l.StartsWith("/files/" + ShaOf(big) + " range=bytes=", StringComparison.Ordinal)), Is.True, "이어받기(Range): " + string.Join("\n", log));
                string stage = StageStore.Dir(install);
                foreach (var f in files)
                    if (f.Value != same) Assert.That(File.ReadAllBytes(StageStore.FileOf(stage, ShaOf(f.Value))), Is.EqualTo(f.Value), "stage/" + f.Key);
                Assert.That(Directory.GetFiles(stage, "*.part"), Is.Empty);

                // 도우미: run/의 새 도우미를 K3 인자로 · plan.json
                string planFile = Path.Combine(install, "_update", NewVersion, "plan.json");
                string runExe = Path.Combine(install, "_update", "run", UpdatePlan.Helper);
                Assert.That(rig.Helper, Is.EqualTo(new[] { runExe, UpdateHandoff.Args(install, planFile) }));
                Assert.That(File.ReadAllText(runExe), Is.EqualTo("new-helper"), "늘 새 manifest의 도우미");
                string plan = File.ReadAllText(planFile);
                foreach (var p in new[] { "Sinnoh.exe", UpdatePlan.Helper, "version.txt", "Sinnoh_Data/a.bin", "WebGL/dup.bin" }) Assert.That(plan, Does.Contain("\"path\":\"" + p + "\""), p);
                Assert.That(plan, Does.Not.Contain("same.bin"));
                Assert.That(plan, Does.Contain("\"delete\":[\"Sinnoh_Data/old.bin\"]"), "지울 자리의 낡은 파일만(notes.txt는 남김)");
                Assert.That(plan, Does.Contain("\"from\":\"" + Application.version + "\""));
                Assert.That(File.ReadAllText(Path.Combine(install, "_update", NewVersion, "latest.json")), Is.EqualTo(latest), "받은 판 정보 그대로");
                Assert.That(File.ReadAllText(Path.Combine(install, "Sinnoh.exe")), Is.EqualTo("old-exe"), "게임은 설치 파일을 바꾸지 않는다(도우미 몫)");
            }
            finally
            {
                Close(rig);
            }
            Assert.That(rig.Errors, Is.Empty, string.Join("\n", rig.Errors));
        }

        [UnityTest]
        public IEnumerator M10_자동업데이트_받을것이60개를넘으면zip하나_필요한항목만풀어확인()
        {
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                string install = rig.Install;
                Lay(install, "Sinnoh.exe", Bytes("old-exe"));
                Lay(install, UpdatePlan.Helper, Bytes("helper"));
                Lay(install, "version.txt", Bytes(Application.version));
                var files = new Dictionary<string, byte[]> { ["Sinnoh.exe"] = Bytes("new-exe"), [UpdatePlan.Helper] = Bytes("helper"), ["version.txt"] = Bytes(NewVersion) };
                for (int i = 0; i < 60; i++) files["Sinnoh_Data/f" + i + ".bin"] = Pattern(200 + i, i);
                files["Sinnoh_Data/글꼴.txt"] = Bytes("한글 이름");
                byte[] zip;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        foreach (var f in files)
                            using (var s = z.CreateEntry("Legends of Sinnoh/" + f.Key).Open())
                                s.Write(f.Value, 0, f.Value.Length);
                    zip = ms.ToArray();
                }
                string zipName = "Sinnoh-" + NewVersion + "-win64.zip";
                string latest = TestEd25519.Latest(Manifest(rig.Http.Url, files, new KeyValuePair<string, byte[]>(zipName, zip)));
                rig.Http.Handle = path => path == "/releases/" + NewVersion + "/" + zipName ? new FakeHttp.Reply { Body = zip } : new FakeHttp.Reply { Code = 404 };
                flow.TakeRelease(latest);
                Assert.That(rig.Title.PressUpdate(), Is.True);
                yield return Until(() => flow.Updating == UpdateStep.Restart || flow.Updating == UpdateStep.Failed, "zip 받기", 30f);
                Assert.That(flow.Updating, Is.EqualTo(UpdateStep.Restart), "실패: " + flow.UpdateWhy);
                Assert.That(rig.Http.Log, Is.EqualTo(new[] { "/releases/" + NewVersion + "/" + zipName }), "요청 하나(2-5 요청 수 예산)");
                string stage = StageStore.Dir(install);
                foreach (var f in files)
                    if (f.Key != UpdatePlan.Helper) Assert.That(File.ReadAllBytes(StageStore.FileOf(stage, ShaOf(f.Value))), Is.EqualTo(f.Value), f.Key);
                Assert.That(File.Exists(StageStore.FileOf(stage, ShaOf(files[UpdatePlan.Helper]))), Is.False, "바뀌지 않은 도우미는 풀지 않는다");
                Assert.That(File.Exists(StageStore.FileOf(stage, ShaOf(zip))), Is.False, "푼 뒤 zip은 지운다");
                Assert.That(File.ReadAllText(Path.Combine(install, "_update", "run", UpdatePlan.Helper)), Is.EqualTo("helper"), "같은 지문이면 설치된 도우미를 run/으로");
            }
            finally
            {
                Close(rig);
            }
            Assert.That(rig.Errors, Is.Empty, string.Join("\n", rig.Errors));
        }

        [UnityTest]
        public IEnumerator M10_자동업데이트_실패알림_한도_404_지문틀림_공간부족_취소_쓰기안되면받기()
        {
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                string install = rig.Install;
                Lay(install, "Sinnoh.exe", Bytes("old-exe"));
                Lay(install, UpdatePlan.Helper, Bytes("helper"));
                Lay(install, "version.txt", Bytes(Application.version));
                var files = new Dictionary<string, byte[]> { ["Sinnoh.exe"] = Bytes("new-exe"), [UpdatePlan.Helper] = Bytes("helper"), ["version.txt"] = Bytes(NewVersion) };
                string exeSha = ShaOf(files["Sinnoh.exe"]);
                string latest = TestEd25519.Latest(Manifest(rig.Http.Url, files));
                string mode = "later";
                int feedHits = 0;
                rig.Http.Handle = path =>
                {
                    if (path == "/latest.json")
                    {
                        System.Threading.Interlocked.Increment(ref feedHits);
                        return new FakeHttp.Reply { Body = Bytes(latest) };
                    }
                    if (mode == "later") return new FakeHttp.Reply { Code = 429 };
                    if (mode == "404") return new FakeHttp.Reply { Code = 404 };
                    if (mode == "bad" && path.EndsWith(exeSha, StringComparison.Ordinal)) return new FakeHttp.Reply { Body = Bytes("BAD-exe") };   // 같은 길이 · 다른 내용
                    return new FakeHttp.Reply { Body = path.EndsWith(exeSha, StringComparison.Ordinal) ? files["Sinnoh.exe"] : Bytes(NewVersion), DelayMs = mode == "slow" ? 3000 : 0 };
                };
                flow.TakeRelease(latest);
                Assert.That(rig.Root.Q<Button>("title-update-do"), Is.Not.Null);

                // 한도(429): 재시도 없이 「지금은 받을 수 없습니다」 + [다시 시도] · [받기]
                Assert.That(rig.Title.PressUpdate(), Is.True);
                yield return Until(() => flow.Updating == UpdateStep.Failed, "429");
                Assert.That(flow.UpdateWhy, Is.EqualTo("later"));
                Assert.That(rig.Root.Q<Label>("title-update-text").text, Is.EqualTo(flow.Stage.T("screen.title.update.fail")));
                Assert.That(rig.Root.Q<Label>("title-update-why").text.Replace(WordJoiner, ""), Is.EqualTo(flow.Stage.T("screen.title.update.fail.later")));
                Assert.That(rig.Root.Q<Button>("title-update-retry"), Is.Not.Null);
                Assert.That(rig.Root.Q("title-update-block"), Is.Null, "실패면 메뉴를 막지 않는다");
                Assert.That(rig.Http.Log.FindAll(l => l.StartsWith("/files/" + exeSha, StringComparison.Ordinal)).Count, Is.EqualTo(1), "한도는 재시도하지 않는다");
                Assert.That(rig.Title.PressFailed(false), Is.True);
                Assert.That(rig.Opened, Is.EqualTo(new[] { ReleaseFeed.Page }), "[받기] = GitHub");

                // 404: 판 정보를 한 번 다시 읽고 다시 계획 → 또 404면 실패(까닭 줄 없음)
                mode = "404";
                Assert.That(rig.Title.PressFailed(true), Is.True, "[다시 시도]");
                yield return Until(() => flow.Updating == UpdateStep.Failed, "404");
                Assert.That((flow.UpdateWhy, feedHits), Is.EqualTo(("missing", 1)), "판 정보를 한 번만 다시 읽는다");
                Assert.That(rig.Root.Q("title-update-why"), Is.Null, "그 밖은 까닭 줄 없음");

                // 지문 틀림: 재시도 3번 뒤 실패 · 틀린 조각은 남기지 않는다
                mode = "bad";
                int before = rig.Http.Log.Count;
                Assert.That(rig.Title.PressFailed(true), Is.True);
                yield return Until(() => flow.Updating == UpdateStep.Failed, "지문 틀림");
                Assert.That(flow.UpdateWhy, Is.EqualTo("fetch"));
                Assert.That(rig.Http.Log.GetRange(before, rig.Http.Log.Count - before).FindAll(l => l.StartsWith("/files/" + exeSha, StringComparison.Ordinal)).Count, Is.EqualTo(4), "처음 + 재시도 3번");
                Assert.That(File.Exists(StageStore.FileOf(StageStore.Dir(install), exeSha)) || File.Exists(StageStore.PartOf(StageStore.Dir(install), exeSha)), Is.False);

                // 공간 부족: 「저장 공간이 부족합니다(xx MB 필요)」 — 받지 않는다
                mode = "ok";
                flow.FreeSpace = _ => 1;
                before = rig.Http.Log.Count;
                Assert.That(rig.Title.PressFailed(true), Is.True);
                yield return Until(() => flow.Updating == UpdateStep.Failed, "공간 부족");
                Assert.That(flow.UpdateWhy, Is.EqualTo("space"));
                Assert.That(rig.Root.Q<Label>("title-update-why").text.Replace(WordJoiner, ""), Is.EqualTo(flow.Stage.T("screen.title.update.fail.space").Replace("{mb}", "65")));
                Assert.That(rig.Http.Log.Count, Is.EqualTo(before), "받지 않는다");
                flow.FreeSpace = UpdateHandoff.FreeBytes;

                // 취소: 받는 중 [취소] → 알림으로([업데이트]) · 덮개 걷힘 · 도우미를 띄우지 않음
                mode = "slow";
                Assert.That(rig.Title.PressFailed(true), Is.True);
                yield return Until(() => flow.Updating == UpdateStep.Fetch, "받기 시작");
                yield return null;
                Assert.That(rig.Title.PressCancel(), Is.True);
                Assert.That(flow.Updating, Is.EqualTo(UpdateStep.Idle));
                Assert.That(rig.Root.Q<Button>("title-update-do"), Is.Not.Null, "알림으로 돌아온다");
                Assert.That(rig.Root.Q("title-update-block"), Is.Null);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.That((rig.Helper.Count, rig.Quits), Is.EqualTo((0, 0)), "실패 · 취소는 도우미를 띄우지 않고 끄지 않는다");

                // 쓰기가 안 되는 설치(_update 자리에 파일) → 지금처럼 [받기]
                string locked = Path.Combine(Path.GetDirectoryName(install), "locked");
                Directory.CreateDirectory(locked);
                File.WriteAllText(Path.Combine(locked, "_update"), "");
                flow.InstallDir = locked;
                flow.TakeRelease(latest);
                Assert.That(flow.AutoUpdate, Is.False);
                Assert.That(rig.Root.Q<Button>("title-update-get"), Is.Not.Null, "[받기]");
                Assert.That(rig.Root.Q<Button>("title-update-do"), Is.Null);
                Assert.That(rig.Title.PressUpdate(), Is.True);
                Assert.That(rig.Opened.Count, Is.EqualTo(2));

                // 켤 때 도우미가 되돌림 실패를 남겼다 → 실패 알림(판 정보 전에는 [받기]만)
                flow.InstallDir = null;
                flow.UpdateFailedBefore(new UpdateResult { Ok = false, Version = NewVersion, Why = "share" });
                Assert.That(rig.Root.Q<Label>("title-update-text").text, Is.EqualTo(flow.Stage.T("screen.title.update.fail")));
                Assert.That(rig.Root.Q<Button>("title-update-get"), Is.Not.Null);
            }
            finally
            {
                Close(rig);
            }
            Assert.That(rig.Errors, Is.Empty, string.Join("\n", rig.Errors));
        }
    }
}
