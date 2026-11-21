using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Sinnoh.Editor
{
    // 요약만 남기는 시험 실행기(isekai TestRunMenu에서 옮김).
    // MCP run_tests는 결과를 통째로 돌려주고(한 번에 수 MB) 동시에 부르면 에디터가 멈춘다(isekai 11-02). 이 메뉴는
    //   1) Temp/test-request.json을 읽어 TestRunnerApi로 한 번 돌리고
    //   2) 끝나면 Temp/test-summary.json에 개수와 실패 목록만 쓴다.
    //   3) 이미 시험이 돌고 있거나(이 메뉴·MCP·Test Runner 창) 컴파일·Play 중이면 시작하지 않고 Temp/test-rejected.json에 busy를 쓴다.
    // 요청: {"mode":"EditMode"|"PlayMode", "filters":[정규식(전체 이름 부분 일치)…], "assemblies":[…], "includeExplicit":false}
    //   모두 빼도 된다(기본 EditMode 전체). 모르는 키는 오류(오타로 전체가 도는 것을 막는다).
    //   filters·assemblies는 각각 하나라도 맞으면 되고, 둘 다 맞아야 고른다. 어셈블리는 이름 일치(.dll 없이, 대소문자 무시).
    //   고른 시험의 전체 이름을 넘겨 돌린다 — 어셈블리·이름 필터를 그대로 넘기면 NUnit이 그 필터가 가리키는 [Explicit]까지 돌린다.
    //   [Explicit]은 includeExplicit가 참일 때만 고른다.
    // 결과 파일에 요청 파일의 수정 시각(requestMs, 유닉스 ms)을 함께 적는다 — Tools/test-wait.mjs가 이것으로 자기 요청의 결과를 알아본다.
    // PlayMode는 Play 진입 때 도메인 리로드가 나므로 진행 중인 실행 정보는 SessionState에 두고 콜백은 [InitializeOnLoad]로 리로드마다 다시 붙인다.
    [InitializeOnLoad]
    public static class TestRunMenu
    {
        public const string RequestPath = "Temp/test-request.json";
        public const string SummaryPath = "Temp/test-summary.json";
        public const string RejectPath = "Temp/test-rejected.json";
        const int MessageMax = 300;
        const int StackLines = 3;
        // 메뉴가 요청을 받고 TestRunnerApi가 실행을 잡기까지(시험 목록 만들기)의 틈. 이 안의 두 번째 요청은 busy
        const double StartGraceSec = 60;
        const string PendingKey = "Sinnoh.TestRunMenu.Pending";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static TestRunMenu()
        {
            TestRunnerApi.RegisterTestCallback(new Collector());
        }

        [MenuItem("Sinnoh/Test/Run")]
        public static void Run()
        {
            long requestMs = 0;
            Request req;
            try
            {
                if (!File.Exists(RequestPath)) throw new FileNotFoundException(RequestPath + " 없음");
                requestMs = FileMs(RequestPath);
                req = Request.Parse(File.ReadAllText(RequestPath));
            }
            catch (Exception e)
            {
                Reject(requestMs, "error", "요청 해석 실패: " + e.Message);
                return;
            }

            double? pendingAge = null;
            var old = SessionState.GetString(PendingKey, "");
            if (old.Length > 0)
            {
                try { pendingAge = (DateTime.UtcNow - new DateTime(ParseJson(old).Value<long>("startedTicks"), DateTimeKind.Utc)).TotalSeconds; }
                catch (Exception) { pendingAge = 0; }
            }
            string busy = BusyReason(RunActive(), pendingAge, EditorApplication.isCompiling || EditorApplication.isUpdating, EditorApplication.isPlayingOrWillChangePlaymode);
            if (busy != null)
            {
                Reject(requestMs, "busy", busy);
                return;
            }

            string head = Git("rev-parse --short HEAD");
            var porcelain = Git("status --porcelain -- Assets Packages ProjectSettings");
            bool dirty = porcelain == null || porcelain.Length > 0;   // git 실패면 미커밋으로 본다
            SessionState.SetString(PendingKey, PendingInfo(requestMs, req, head, dirty, DateTime.UtcNow).ToString(Formatting.None));
            Debug.Log($"[TestRunMenu] 시작 {req.Mode} 필터 [{string.Join(", ", req.Filters)}] 어셈블리 [{string.Join(", ", req.Assemblies)}] → {SummaryPath}");

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                api.RetrieveTestList(req.Mode, root =>
                {
                    try
                    {
                        var all = new List<TestInfo>();
                        Leaves(root, false, all);
                        var names = Select(all, req);
                        if (names.Count == 0) { Finish(null, "필터에 맞는 시험이 없다(시험 " + all.Count + "개 중)"); return; }
                        api.Execute(new ExecutionSettings(new Filter { testMode = req.Mode, testNames = names.ToArray() }));
                    }
                    catch (Exception e) { Finish(null, "실행 시작 실패: " + e.Message); }
                });
            }
            catch (Exception e) { Finish(null, "시험 목록 실패: " + e.Message); }
        }

        // ── 요청 ──
        sealed class Request
        {
            static readonly string[] Keys = { "mode", "filters", "assemblies", "includeExplicit" };

            public TestMode Mode = TestMode.EditMode;
            public string[] Filters = new string[0];
            public string[] Assemblies = new string[0];
            public bool IncludeExplicit;

            public static Request Parse(string json)
            {
                var o = ParseJson(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                var unknown = o.Properties().Select(p => p.Name).Where(n => !Keys.Contains(n)).ToList();
                if (unknown.Count > 0) throw new FormatException("모르는 키 " + string.Join(", ", unknown) + " (받는 키: " + string.Join(", ", Keys) + ")");
                var r = new Request();
                var mode = o["mode"];
                if (mode != null && mode.Type != JTokenType.Null) r.Mode = ParseMode((string)mode);
                r.Filters = Strings(o["filters"], "filters");
                foreach (var f in r.Filters)
                {
                    try { new Regex(f); }
                    catch (ArgumentException e) { throw new FormatException($"filters 「{f}」 정규식 오류: {e.Message}"); }
                }
                r.Assemblies = Strings(o["assemblies"], "assemblies");
                var ex = o["includeExplicit"];
                r.IncludeExplicit = ex != null && ex.Type != JTokenType.Null && (bool)ex;
                return r;
            }

            static TestMode ParseMode(string s)
            {
                switch ((s ?? "").Trim().ToLowerInvariant())
                {
                    case "editmode": return TestMode.EditMode;
                    case "playmode": return TestMode.PlayMode;
                    default: throw new FormatException($"mode 「{s}」 — EditMode 또는 PlayMode");
                }
            }

            // 문자열 하나도 배열 하나로 받는다
            static string[] Strings(JToken t, string key)
            {
                if (t == null || t.Type == JTokenType.Null) return new string[0];
                if (t.Type == JTokenType.String) return string.IsNullOrWhiteSpace((string)t) ? new string[0] : new[] { ((string)t).Trim() };
                if (t.Type != JTokenType.Array) throw new FormatException(key + "는 문자열이나 문자열 배열");
                return t.Select(x => x.Type == JTokenType.String ? ((string)x).Trim() : throw new FormatException(key + "에 문자열이 아닌 값"))
                    .Where(x => x.Length > 0).ToArray();
            }
        }

        // ── 막기 ──
        // runActive: TestRunnerApi에 도는 실행이 있나(null = 알 수 없음), pendingAgeSec: 이 메뉴가 시작하고 아직 끝을 못 받은 실행이 몇 초 전인가(null = 없음)
        static string BusyReason(bool? runActive, double? pendingAgeSec, bool compiling, bool playing)
        {
            if (runActive == true) return "다른 시험 실행이 돌고 있다";
            if (pendingAgeSec != null && (runActive == null || pendingAgeSec < StartGraceSec)) return "앞 요청의 시험 실행이 아직 끝나지 않았다";
            if (compiling) return "에디터가 컴파일·임포트 중이다";
            if (playing) return "에디터가 Play 모드다";
            return null;
        }

        // TestRunnerApi.IsRunActive는 internal이다. 못 찾으면 null(알 수 없음 — 진행 중 표시가 남아 있으면 busy로 본다)
        static bool? RunActive()
        {
            var m = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return null;
            try { return (bool)m.Invoke(null, null); }
            catch (Exception) { return null; }
        }

        // ── 고르기 ──
        sealed class TestInfo
        {
            public string FullName;
            public string Assembly;
            public bool Explicit;   // 자기나 위 묶음에 [Explicit]
        }

        // 요청에 맞는 시험의 전체 이름(순서 유지)
        static List<string> Select(IEnumerable<TestInfo> tests, Request req)
        {
            var rx = req.Filters.Select(f => new Regex(f)).ToList();
            return tests.Where(t => (req.IncludeExplicit || !t.Explicit)
                    && (req.Assemblies.Length == 0 || req.Assemblies.Any(a => string.Equals(a, t.Assembly, StringComparison.OrdinalIgnoreCase)))
                    && (rx.Count == 0 || rx.Any(r => r.IsMatch(t.FullName ?? ""))))
                .Select(t => t.FullName).ToList();
        }

        static void Leaves(ITestAdaptor t, bool explicitAbove, List<TestInfo> into)
        {
            if (t == null) return;
            bool ex = explicitAbove || IsExplicit(t);
            if (!t.IsSuite)
            {
                into.Add(new TestInfo { FullName = t.FullName, Assembly = t.TypeInfo?.Assembly?.GetName().Name, Explicit = ex });
                return;
            }
            if (t.HasChildren) foreach (var c in t.Children) Leaves(c, ex, into);
        }

        // 묶음에 붙은 [Explicit]이 [UnityTest] 잎의 RunState까지 늘 전해지지는 않아 메서드·형식의 속성도 본다
        static bool IsExplicit(ITestAdaptor t)
        {
            if (t.RunState == RunState.Explicit) return true;
            bool Has(MemberInfo m) => m != null && m.GetCustomAttributes(true).Any(a => a.GetType().Name == "ExplicitAttribute");
            return Has(t.Method?.MethodInfo) || Has(t.TypeInfo?.Type);
        }

        // ── 요약 ──
        sealed class Leaf
        {
            public string Name;
            public TestStatus Status;
            public string Message;
            public string Stack;
        }

        static JObject PendingInfo(long requestMs, Request req, string head, bool dirty, DateTime startedUtc) => new JObject
        {
            ["requestMs"] = requestMs,
            ["mode"] = req.Mode.ToString(),
            ["head"] = head,
            ["dirty"] = dirty,
            ["startedTicks"] = startedUtc.Ticks,
            ["filters"] = new JArray(req.Filters),
            ["assemblies"] = new JArray(req.Assemblies),
            ["includeExplicit"] = req.IncludeExplicit,
        };

        // pending = PendingInfo, error = 실행이 시작·완료되지 못한 이유(없으면 null)
        static JObject Summary(JObject pending, IList<Leaf> leaves, DateTimeOffset at, double durationSec, string error)
        {
            int Count(TestStatus s) => leaves.Count(l => l.Status == s);
            var failed = new JArray(leaves.Where(l => l.Status == TestStatus.Failed).Select(l => new JObject
            {
                ["name"] = l.Name,
                ["message"] = Clip(l.Message),
                ["stack"] = StackHead(l.Stack),
            }));
            var o = new JObject
            {
                ["head"] = pending["head"]?.DeepClone(),
                ["dirty"] = pending["dirty"]?.DeepClone(),
                ["total"] = leaves.Count,
                ["passed"] = Count(TestStatus.Passed),
                ["failed"] = Count(TestStatus.Failed),
                ["skipped"] = Count(TestStatus.Skipped),
                ["inconclusive"] = Count(TestStatus.Inconclusive),
                ["failedTests"] = failed,
                ["mode"] = pending["mode"]?.DeepClone(),
                ["at"] = at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ["durationSec"] = Math.Round(durationSec, 1),
                ["requestMs"] = pending["requestMs"]?.DeepClone(),
                ["filters"] = pending["filters"]?.DeepClone(),
                ["assemblies"] = pending["assemblies"]?.DeepClone(),
                ["includeExplicit"] = pending["includeExplicit"]?.DeepClone(),
            };
            if (error != null) o["error"] = error;
            return o;
        }

        // 날짜처럼 생긴 문자열(필터 등)을 DateTime으로 바꾸지 않고 글자 그대로 읽는다.
        // 기본 JObject.Parse는 바꾸고, (string)으로 꺼내면 현재 로캘 서식("11/04/2026 12:00:00")이 된다.
        static JObject ParseJson(string json) =>
            JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });

        // 실패 메시지는 300자까지(넘으면 끝을 …로)
        static string Clip(string message)
        {
            var s = (message ?? "").Trim();
            if (s.Length <= MessageMax) return s;
            int cut = MessageMax - 1;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut) + "…";
        }

        // 스택은 빈 줄을 뺀 앞 3줄
        static string StackHead(string stack) =>
            string.Join("\n", (stack ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(StackLines));

        static void Collect(ITestResultAdaptor r, List<Leaf> into)
        {
            bool suite = r.Test != null ? r.Test.IsSuite : r.HasChildren;
            if (suite && r.HasChildren)
            {
                foreach (var c in r.Children) Collect(c, into);
                return;
            }
            // 자식 없이 실패한 묶음(시험 원본 오류 등)도 한 칸으로 센다
            if (suite && r.TestStatus != TestStatus.Failed) return;
            into.Add(new Leaf { Name = r.FullName, Status = r.TestStatus, Message = r.Message, Stack = r.StackTrace });
        }

        static void Finish(ITestResultAdaptor result, string error)
        {
            var raw = SessionState.GetString(PendingKey, "");
            if (raw.Length == 0) return;   // 이 메뉴가 시작한 실행이 아니다(MCP·Test Runner 창)
            SessionState.EraseString(PendingKey);
            try
            {
                var pending = ParseJson(raw);
                var leaves = new List<Leaf>();
                if (result != null) Collect(result, leaves);
                var started = new DateTime(pending.Value<long>("startedTicks"), DateTimeKind.Utc);
                var summary = Summary(pending, leaves, DateTimeOffset.Now, (DateTime.UtcNow - started).TotalSeconds, error);
                WriteAtomic(SummaryPath, summary.ToString(Formatting.Indented));
                string line = $"[TestRunMenu] {summary["mode"]} {summary["passed"]}/{summary["total"]} 통과, 실패 {summary["failed"]} → {SummaryPath}" + (error != null ? " 오류: " + error : "");
                if (error != null || (int)summary["failed"] > 0) Debug.LogWarning(line);
                else Debug.Log(line);
            }
            catch (Exception e) { Debug.LogError("[TestRunMenu] 요약 쓰기 실패: " + e); }
        }

        static void Reject(long requestMs, string status, string message)
        {
            var o = new JObject
            {
                ["status"] = status,
                ["message"] = message,
                ["requestMs"] = requestMs,
                ["at"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            };
            try { WriteAtomic(RejectPath, o.ToString(Formatting.Indented)); }
            catch (Exception e) { Debug.LogError("[TestRunMenu] " + RejectPath + " 쓰기 실패: " + e.Message); }
            Debug.LogWarning($"[TestRunMenu] {status}: {message} → {RejectPath}");
        }

        // 읽는 쪽(test-wait)이 반쯤 쓴 파일을 보지 않게 임시 파일에 쓰고 바꿔 끼운다
        static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Utf8);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (IOException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }

        // 유닉스 ms(내림) — node fs.statSync().mtimeMs 내림과 같은 값
        static long FileMs(string path) =>
            (File.GetLastWriteTimeUtc(path) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / TimeSpan.TicksPerMillisecond;

        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(10000)) return null;
                    return p.ExitCode == 0 ? output.Trim() : null;
                }
            }
            catch (Exception) { return null; }
        }

        sealed class Collector : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void RunFinished(ITestResultAdaptor result) => Finish(result, null);
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void OnError(string message) => Finish(null, message);
        }
    }
}
