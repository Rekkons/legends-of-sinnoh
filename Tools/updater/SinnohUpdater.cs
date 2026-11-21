// 신오의 전설들 업데이트 도우미(자동 업데이트 2단계 — 바뀐 파일만 받아 바꿔 끼우기).
// 게임이 바뀐 파일을 <설치>\_update\stage\<sha256>에 받아 확인하고, 서명된 판 정보(latest.json)와 바꿀 목록(plan.json)을 <설치>\_update\<판>\에 쓴 뒤
// 이 도우미를 <설치>\_update\run\에서 띄우고 끝난다. 도우미는 계획을 믿지 않고 박아 둔 공개 키로 판 정보 서명을 확인해 줄마다 대조한 다음,
// 게임이 끝나기를 기다려 파일을 바꿔 끼우고(옛 파일은 _update\<판>\backup\으로 — 실패하면 되돌림) 게임을 다시 켠다.
// 창 없음 · 네트워크 없음 · 설치 폴더 밖에 쓰지 않는다(저장 · 설정 · 임시 폴더 · 레지스트리를 건드리지 않는다). 사용자에게 보이는 글은 없다 — 결과는 게임이 result.json을 읽어 알린다.
//
// 빌드: node Tools/updater/build.mjs (Unity에 든 Roslyn · .NET Framework 4.x 참조 · 결정적 바이트). 시험: node Tools/updater/build.mjs --self-test
// 사용: SinnohUpdater.exe --install "<설치>" --plan "<설치>\_update\<판>\plan.json" [--wait <ms>]
// 종료 코드: 0 바꿈 · 1 바꾸다 실패해 되돌림 · 2 인자 · 계획 거부(설치 파일은 그대로) · 3 게임이 끝나지 않음 · 다른 도우미가 도는 중(그대로, 다시 켜지 않음)
// 인자 꼴은 판이 올라도 유지한다(옛 게임이 새 도우미를 부른다). 바꿔야 하면 plan.json format을 올리고 새 도우미가 옛 format도 읽는다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Sinnoh.Data;

[assembly: AssemblyTitle("Legends of Sinnoh Updater")]
[assembly: AssemblyDescription("Replaces changed game files for Legends of Sinnoh and restarts the game")]
[assembly: AssemblyCompany("Rekkons")]
[assembly: AssemblyProduct("Legends of Sinnoh")]
[assembly: AssemblyCopyright("Rekkons")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1")]
[assembly: ComVisible(false)]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Sinnoh.Updater
{
    static partial class Key
    {
        // PublicKey(base64 32바이트)는 빌드가 만드는 Key.g.cs에 있다 — 기본은 Assets/Sinnoh/Data/ReleaseFeed.cs PublicKey.
        public static byte[] Bytes => Convert.FromBase64String(PublicKey);
    }

    /// <summary>판 정보 · 계획 속 경로 규칙(K5). 게임의 InstallPath와 같은 표로 시험한다(build.mjs PATH_TABLE).</summary>
    static class InstallPath
    {
        public static readonly string[] Roots = { "Sinnoh_Data", "MonoBleedingEdge", "D3D12", "WebGL" };
        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        /// <summary>rel이 설치 폴더 안 파일 하나를 가리키는 안전한 경로인가. install이 null이면 전체 경로 확인을 건너뛴다.</summary>
        public static bool Safe(string rel, string install) => Why(rel, install) == null;

        public static string Why(string rel, string install)
        {
            if (string.IsNullOrEmpty(rel)) return "empty";
            if (rel.Length >= 260) return "long";
            if (rel[0] == '/') return "rooted";
            foreach (char c in rel)
                if (c < 32 || c == 127 || "\\:<>\"|?*".IndexOf(c) >= 0) return "char";
            var parts = rel.Split('/');
            foreach (var p in parts)
            {
                if (p.Length == 0) return "segment";
                if (p == "." || p == "..") return "dots";
                char last = p[p.Length - 1];
                if (last == '.' || last == ' ') return "trailing";
                int dot = p.IndexOf('.');
                string stem = (dot < 0 ? p : p.Substring(0, dot)).TrimEnd(' ').ToUpperInvariant();
                if (Array.IndexOf(Reserved, stem) >= 0) return "reserved";
            }
            if (string.Equals(parts[0], "_update", StringComparison.OrdinalIgnoreCase)) return "update";
            if (install != null)
            {
                try
                {
                    string full = Path.GetFullPath(Path.Combine(install, rel.Replace('/', '\\')));
                    if (!full.StartsWith(install + "\\", StringComparison.OrdinalIgnoreCase)) return "outside";
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return "outside"; }
            }
            return null;
        }

        /// <summary>지워도 되는 자리: Unity 폴더 넷 아래의 파일(폴더 자신 · 맨 위 파일은 아님).</summary>
        public static bool Deletable(string rel) =>
            Safe(rel, null) && Roots.Any(r => rel.Length > r.Length + 1 && rel.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>판 번호 주.부.수[-꼬리] (게임 Sinnoh.Data.ReleaseVersion과 같은 규칙 — 그쪽은 Newtonsoft에 기대는 파일에 있어 따로 둔다).</summary>
    static class ReleaseVer
    {
        static bool Parse(string s, out int[] n, out string tail)
        {
            n = new int[3];
            tail = "";
            if (string.IsNullOrEmpty(s)) return false;
            string core = s;
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                core = s.Substring(0, dash);
                tail = s.Substring(dash + 1);
                if (tail.Length == 0) return false;
                foreach (char c in tail) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.') return false;
            }
            var parts = core.Split('.');
            if (parts.Length != 3) return false;
            for (int i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 6) return false;
                foreach (char c in parts[i]) if (c < '0' || c > '9') return false;
                n[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            return true;
        }

        public static bool Valid(string s) => Parse(s, out _, out _);

        /// <summary>remote가 local보다 새 판인가(둘 중 하나라도 꼴이 아니면 거짓).</summary>
        public static bool Newer(string remote, string local)
        {
            if (!Parse(remote, out var r, out var rt) || !Parse(local, out var l, out var lt)) return false;
            for (int i = 0; i < 3; i++) if (r[i] != l[i]) return r[i] > l[i];
            if (rt.Length == 0) return lt.Length != 0;
            return lt.Length != 0 && string.CompareOrdinal(rt, lt) > 0;
        }
    }

    sealed class FileEntry
    {
        public string Path, Sha;
        public long Size;
    }

    static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 16 };

        public static Dictionary<string, object> Parse(string text) => S.DeserializeObject(text) as Dictionary<string, object>;
        public static string Serialize(object o) => S.Serialize(o);

        public static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        public static string Str(Dictionary<string, object> d, string k) => Get(d, k) as string;

        public static long? Num(Dictionary<string, object> d, string k)
        {
            switch (Get(d, k))
            {
                case int i: return i;
                case long l: return l;
                case decimal m when m == decimal.Truncate(m) && m >= long.MinValue && m <= long.MaxValue: return (long)m;
                default: return null;
            }
        }

        public static object[] Arr(Dictionary<string, object> d, string k) => Get(d, k) as object[];
    }

    /// <summary>도우미 로그 <c>_update\updater.log</c>(UTF-8 이어 쓰기, 256 KB 넘으면 앞 절반을 버린다). 경로는 설치 기준 상대 경로만 — 개인 정보 · 키 없음. 로그가 실패해도 일은 계속한다.</summary>
    sealed class Log
    {
        const int Max = 256 * 1024;
        readonly string file;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public Log(string file)
        {
            this.file = file;
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length > Max)
                {
                    var b = File.ReadAllBytes(file);
                    int from = b.Length / 2;
                    while (from < b.Length && b[from] != (byte)'\n') from++;
                    File.WriteAllBytes(file, b.Skip(from + 1).ToArray());
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        public void Line(string msg)
        {
            try
            {
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " +" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms " + msg + "\r\n", Program.Utf8);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }

    /// <summary>바꿔 끼우기 기록 <c>_update\&lt;판&gt;\journal.txt</c> — 일을 하기 전에 줄을 쓰고 디스크까지 내린다(되돌리기용). 줄 = 일 \t 경로 [\t sha].</summary>
    sealed class Journal : IDisposable
    {
        readonly FileStream fs;

        public Journal(string file) { fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough); }

        public void Add(string op, string rel, string sha = null)
        {
            var b = Program.Utf8.GetBytes(op + "\t" + rel + (sha != null ? "\t" + sha : "") + "\n");
            fs.Write(b, 0, b.Length);
            fs.Flush(true);
        }

        public void Dispose() => fs.Dispose();
    }

    static class Program
    {
        public const string HelperVersion = "1";
        public const string Exe = "Sinnoh.exe";
        public const string UpdateDir = "_update";
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--test-paths") return TestPaths(args[1]);
            string install = null, plan = null;
            int wait = 60000;
            for (int i = 0; i < args.Length; i++)
            {
                string v = i + 1 < args.Length ? args[i + 1] : null;
                if (v == null) return 2;
                if (args[i] == "--install") install = v;
                else if (args[i] == "--plan") plan = v;
                else if (args[i] == "--wait" && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out wait)) { }
                else return 2;
                i++;
            }
            if (install == null || plan == null) return 2;
            try
            {
                if (!Path.IsPathRooted(install) || install.StartsWith("\\\\?\\", StringComparison.Ordinal)) return 2;
                install = Path.GetFullPath(install).TrimEnd('\\');
                plan = Path.GetFullPath(plan);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is IOException) { return 2; }
            if (!File.Exists(Path.Combine(install, Exe)) || !Directory.Exists(Path.Combine(install, UpdateDir))) return 2;
            var log = new Log(Path.Combine(install, UpdateDir, "updater.log"));
            try { return new Session(install, plan, wait, log).Run(); }
            catch (Exception e)
            {
                log.Line("unexpected " + e.GetType().Name);
                return 1;
            }
        }

        /// <summary>시험 모드: JSON 글 배열 파일의 경로마다 [Safe, Deletable]을 stdout에 JSON으로(설치 폴더는 가짜 C:\SinnohTest).</summary>
        static int TestPaths(string file)
        {
            var list = (object[])new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file, Utf8));
            var outp = list.Select(o => new[] { InstallPath.Safe((string)o, @"C:\SinnohTest"), InstallPath.Deletable((string)o) }).ToArray();
            Console.Out.Write(Json.Serialize(outp));
            Console.Out.Flush();
            return 0;
        }

        /// <summary>바꾸는 동안 잡는 이름 있는 뮤텍스(게임이 켜질 때 잡혀 있으면 바로 끝낸다): Local\SinnohUpdate-&lt;설치 전체 경로(끝 \ 없이, 소문자) UTF-8 SHA-256 hex 앞 16자&gt;.</summary>
        public static string MutexName(string install)
        {
            using (var sha = SHA256.Create())
                return "Local\\SinnohUpdate-" + Hex(sha.ComputeHash(Utf8.GetBytes(install.TrimEnd('\\').ToLowerInvariant()))).Substring(0, 16);
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Windows 명령줄 규칙(CommandLineToArgvW)대로 인자 하나를 따옴표 처리.</summary>
        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            for (int i = 0; i < a.Length; i++)
            {
                int bs = 0;
                while (i < a.Length && a[i] == '\\') { bs++; i++; }
                if (i == a.Length) { sb.Append('\\', bs * 2); break; }
                if (a[i] == '"') sb.Append('\\', bs * 2 + 1).Append('"');
                else sb.Append('\\', bs).Append(a[i]);
            }
            return sb.Append('"').ToString();
        }
    }

    sealed class Session
    {
        const int Retries = 10, RetryMs = 300, LockWaitMs = 20000;
        static readonly string[] Locked = { "Sinnoh.exe", "UnityPlayer.dll" };
        static readonly string[] Reserved = { "stage", "run" };

        readonly string install, planFile, upd, stage;
        readonly int waitMs;
        readonly Log log;
        string version = "", from = "", verDir;
        int pid;
        List<string> args = new List<string>();
        readonly List<FileEntry> put = new List<FileEntry>();
        readonly List<string> delete = new List<string>();
        string step = "";

        public Session(string install, string planFile, int waitMs, Log log)
        {
            this.install = install;
            this.planFile = planFile;
            this.waitMs = waitMs;
            this.log = log;
            upd = Path.Combine(install, Program.UpdateDir);
            stage = Path.Combine(upd, "stage");
        }

        public int Run()
        {
            log.Line("start helper " + Program.HelperVersion + " plan " + Rel(planFile) + " wait " + waitMs.ToString(CultureInfo.InvariantCulture));
            string why = Load();
            log.Line(why == null ? "plan ok " + from + " -> " + version + " put " + put.Count + " delete " + delete.Count : "plan rejected: " + why + (step.Length > 0 ? " (" + step + ")" : ""));
            int code;
            using (var mutex = new Mutex(false, Program.MutexName(install)))
            {
                bool have;
                try { have = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { have = true; }
                if (!have) { log.Line("busy: another helper holds the lock"); return 3; }
                try { code = Work(why); }
                catch (Exception e)
                {
                    log.Line("unexpected " + e.GetType().Name + " at " + step);
                    code = 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
            if (code != 3) Restart();
            log.Line("end " + code.ToString(CultureInfo.InvariantCulture));
            return code;
        }

        int Work(string why)
        {
            if (!WaitGame()) { Result(false, "wait"); return 3; }
            if (!Recover()) { Result(false, "rollback"); return 1; }
            if (why != null) { Result(false, why); return 2; }
            why = VerifyStage();
            if (why != null) { Result(false, why); return 2; }
            bool ok = Apply(out why);
            Result(ok, ok ? "" : why);
            return ok ? 0 : 1;
        }

        // ── 1. 계획 · 판 정보 확인(아무것도 건드리지 않는다). 거부 까닭(로그 · result.json why) 또는 null ──
        string Load()
        {
            Dictionary<string, object> plan;
            try { plan = Json.Parse(File.ReadAllText(planFile, Program.Utf8)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException) { return "plan"; }
            if (plan == null || Json.Num(plan, "format") != 1) return "plan";
            version = Json.Str(plan, "version") ?? "";
            if (Json.Arr(plan, "args") is object[] a) args = a.OfType<string>().ToList();
            pid = (int)(Json.Num(plan, "pid") ?? 0);
            if (!ReleaseVer.Valid(version) || Json.Str(plan, "exe") != Program.Exe) return "plan";
            verDir = Path.Combine(upd, version);
            if (!string.Equals(planFile, Path.Combine(verDir, "plan.json"), StringComparison.OrdinalIgnoreCase)) return "plan";
            try { from = File.ReadAllText(Path.Combine(install, "version.txt"), Program.Utf8).Trim(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { from = ""; }

            // 서명된 판 정보(박아 둔 공개 키) — manifest 글 바이트 그대로 확인한다
            Dictionary<string, object> manifest;
            try
            {
                var outer = Json.Parse(File.ReadAllText(Path.Combine(verDir, "latest.json"), Program.Utf8));
                string text = Json.Str(outer, "manifest"), sig = Json.Str(outer, "signature");
                if (Json.Num(outer, "format") != 1 || text == null || sig == null) return "signature";
                if (!Ed25519.Verify(Key.Bytes, Program.Utf8.GetBytes(text), Convert.FromBase64String(sig))) return "signature";
                manifest = Json.Parse(text);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is FormatException) { return "signature"; }
            if (manifest == null || Json.Str(manifest, "app") != "sinnoh") return "manifest";
            if (Json.Str(manifest, "version") != version || !ReleaseVer.Newer(version, from)) return "version";
            if (!(Json.Get(manifest, "auto") is bool auto) || !auto) return "manual";

            var files = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Entries(Json.Arr(manifest, "files")))
            {
                if (f == null) return "manifest";
                if (!InstallPath.Safe(f.Path, install) || !folded.Add(f.Path)) { step = f.Path; return "path"; }
                files[f.Path] = f;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Entries(Json.Arr(plan, "put")))
            {
                if (p == null) return "plan";
                if (!InstallPath.Safe(p.Path, install)) { step = p.Path; return "path"; }
                if (!files.TryGetValue(p.Path, out var m) || m.Size != p.Size || m.Sha != p.Sha || !seen.Add(p.Path)) { step = p.Path; return "plan"; }
                put.Add(p);
            }
            if (put.GroupBy(p => p.Sha).Any(g => g.Select(p => p.Size).Distinct().Count() > 1)) return "manifest";
            foreach (var o in Json.Arr(plan, "delete") ?? new object[0])
            {
                if (!(o is string d)) return "plan";
                if (!InstallPath.Safe(d, install)) { step = d; return "path"; }
                if (!InstallPath.Deletable(d) || folded.Contains(d) || !seen.Add(d)) { step = d; return "plan"; }
                delete.Add(d);
            }
            // 재분석 지점(정션 · 심볼릭 링크): 대기 자리와 바꿀 경로 위의 이미 있는 폴더
            foreach (var dir in new[] { upd, verDir, stage })
                if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { step = Rel(dir); return "path"; }
            foreach (var rel in put.Select(p => p.Path).Concat(delete))
                if (ReparseAbove(rel) is string link) { step = link; return "path"; }
            return null;
        }

        static IEnumerable<FileEntry> Entries(object[] list)
        {
            foreach (var o in list ?? new object[0])
            {
                var d = o as Dictionary<string, object>;
                string path = Json.Str(d, "path"), sha = Json.Str(d, "sha256");
                long? size = Json.Num(d, "size");
                bool ok = path != null && size >= 0 && sha != null && sha.Length == 64 && sha.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
                yield return ok ? new FileEntry { Path = path, Size = size.Value, Sha = sha } : null;
            }
        }

        string ReparseAbove(string rel)
        {
            var parts = rel.Split('/');
            string cur = install;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                cur = Path.Combine(cur, parts[i]);
                if (Directory.Exists(cur) && (File.GetAttributes(cur) & FileAttributes.ReparsePoint) != 0) return string.Join("/", parts, 0, i + 1);
            }
            return null;
        }

        // ── 4. 게임이 끝나기를 기다림: pid 최대 waitMs → 실행 파일을 독점으로 열 수 있을 때까지(백신 검사 잠금) 최대 20초 ──
        bool WaitGame()
        {
            if (pid > 0)
            {
                Process p = null;
                try { p = Process.GetProcessById(pid); }
                catch (ArgumentException) { }
                if (p != null)
                    using (p)
                    {
                        bool ours;
                        try { ours = string.Equals(p.ProcessName, Path.GetFileNameWithoutExtension(Program.Exe), StringComparison.OrdinalIgnoreCase); }
                        catch (InvalidOperationException) { ours = false; }
                        if (ours && !p.WaitForExit(waitMs)) { log.Line("game still running after " + waitMs + "ms"); return false; }
                    }
            }
            var clock = Stopwatch.StartNew();
            while (true)
            {
                string busy = Locked.FirstOrDefault(f => IsLocked(Path.Combine(install, f)));
                if (busy == null) { log.Line("game ended"); return true; }
                if (clock.ElapsedMilliseconds > LockWaitMs) { log.Line("still locked: " + busy); return false; }
                Thread.Sleep(RetryMs);
            }
        }

        static bool IsLocked(string file)
        {
            if (!File.Exists(file)) return false;
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return false;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }
        }

        // ── 3. 앞 실행이 반쯤 남긴 일(닫히지 않은 journal.txt)을 먼저 되돌린다 ──
        bool Recover()
        {
            foreach (var dir in Directory.GetDirectories(upd))
            {
                if (Reserved.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase) || !File.Exists(Path.Combine(dir, "journal.txt"))) continue;
                log.Line("recover " + Rel(dir));
                if (!Rollback(dir)) return false;
            }
            return true;
        }

        // ── 5. 대기 파일 다시 재기(받은 뒤 바뀐 경우) ──
        string VerifyStage()
        {
            using (var sha = SHA256.Create())
                foreach (var g in put.GroupBy(p => p.Sha))
                {
                    string f = Path.Combine(stage, g.Key);
                    step = g.First().Path;
                    if (!File.Exists(f) || new FileInfo(f).Length != g.First().Size) return "stage";
                    using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                        if (Program.Hex(sha.ComputeHash(s)) != g.Key) return "stage";
                }
            step = "";
            return null;
        }

        // ── 6~8. 바꿔 끼우기 · 지우기 · 실패하면 되돌림 ──
        bool Apply(out string why)
        {
            string backup = Path.Combine(verDir, "backup"), jfile = Path.Combine(verDir, "journal.txt");
            var uses = put.GroupBy(p => p.Sha).ToDictionary(g => g.Key, g => g.Count());
            Journal j = null;
            try
            {
                step = "backup";
                if (Directory.Exists(backup)) Directory.Delete(backup, true);   // 앞 실행이 남긴 사본(되돌리기가 끝난 뒤라 쓰이지 않는다)
                j = new Journal(jfile);
                foreach (var p in put.OrderBy(p => p.Path, StringComparer.Ordinal))
                {
                    step = "put " + p.Path;
                    string target = Abs(p.Path), src = Path.Combine(stage, p.Sha);
                    if (ReparseAbove(p.Path) != null) throw new IOException("reparse point");
                    if (File.Exists(target))
                    {
                        j.Add("B", p.Path);
                        string b = Under(backup, p.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(b));
                        Retry(() => File.Move(target, b));
                    }
                    MakeDirs(j, p.Path);
                    j.Add("P", p.Path, p.Sha);
                    if (--uses[p.Sha] > 0) Retry(() => File.Copy(src, target));
                    else Retry(() => File.Move(src, target));
                }
                var emptied = new List<string>();
                foreach (var d in delete.OrderBy(d => d, StringComparer.Ordinal))
                {
                    step = "delete " + d;
                    string target = Abs(d);
                    if (!File.Exists(target)) continue;
                    if (ReparseAbove(d) != null) throw new IOException("reparse point");
                    j.Add("D", d);
                    string b = Under(backup, d);
                    Directory.CreateDirectory(Path.GetDirectoryName(b));
                    Retry(() => File.Move(target, b));
                    emptied.Add(d);
                }
                // 비게 된 폴더(지울 수 있는 자리 안 — Unity 폴더 자신은 남긴다), 깊은 것부터
                var dirs = emptied.SelectMany(Parents).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length).ToList();
                foreach (var d in dirs)
                {
                    step = "rmdir " + d;
                    string abs = Abs(d);
                    if (!Directory.Exists(abs) || Directory.EnumerateFileSystemEntries(abs).Any()) continue;
                    j.Add("RD", d);
                    Retry(() => Directory.Delete(abs));
                }
                j.Dispose();
                j = null;
                step = "commit";
                Close(jfile, "journal.done.txt");   // 여기가 확정 — 이 뒤에 꺼져도 다음 실행이 되돌리지 않는다
                log.Line("applied put " + put.Count + " delete " + emptied.Count);
                why = null;
                return true;
            }
            catch (Exception e)
            {
                log.Line("apply failed at " + step + ": " + e.GetType().Name + " 0x" + e.HResult.ToString("x8", CultureInfo.InvariantCulture));
                j?.Dispose();
                why = !File.Exists(jfile) || Rollback(verDir) ? "apply" : "rollback";
                return false;
            }
        }

        /// <summary>journal을 거꾸로: 놓은 파일은 대기 자리로 돌리거나 지우고, backup을 제자리로, 만든 폴더는 지우고 지운 폴더는 다시 만든다. 다 되면 journal을 닫는다.</summary>
        bool Rollback(string dir)
        {
            string jfile = Path.Combine(dir, "journal.txt"), backup = Path.Combine(dir, "backup");
            string[] lines;
            try { lines = File.ReadAllLines(jfile, Program.Utf8); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { log.Line("rollback: journal unreadable"); return false; }
            var left = new List<string>();
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var f = lines[i].Split('\t');
                if (f.Length < 2 || !InstallPath.Safe(f[1], install)) continue;   // 끊긴 마지막 줄 · 이상한 줄은 건너뛴다
                string rel = f[1], target = Abs(rel);
                try
                {
                    switch (f[0])
                    {
                        case "P":
                            if (!File.Exists(target)) break;
                            string staged = f.Length > 2 ? Path.Combine(stage, f[2]) : null;
                            if (staged != null && !File.Exists(staged)) { Directory.CreateDirectory(stage); Retry(() => File.Move(target, staged)); }
                            else Retry(() => File.Delete(target));
                            break;
                        case "B":
                        case "D":
                            string b = Under(backup, rel);
                            if (!File.Exists(b)) break;
                            if (File.Exists(target)) Retry(() => File.Delete(target));
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            Retry(() => File.Move(b, target));
                            break;
                        case "MD":
                            if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Retry(() => Directory.Delete(target));
                            break;
                        case "RD":
                            Directory.CreateDirectory(target);
                            break;
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    left.Add(rel);
                }
            }
            if (left.Count > 0)
            {
                log.Line("rollback incomplete, left: " + string.Join(", ", left.Distinct()));
                return false;
            }
            try { Close(jfile, "journal.undone.txt"); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { log.Line("rollback: journal not closed"); return false; }
            log.Line("rolled back " + Rel(dir));
            return true;
        }

        void MakeDirs(Journal j, string rel)
        {
            var parts = rel.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                string d = string.Join("/", parts, 0, i);
                if (Directory.Exists(Abs(d))) continue;
                j.Add("MD", d);
                Directory.CreateDirectory(Abs(d));
            }
        }

        /// <summary>rel의 부모 폴더들 중 지울 수 있는 자리 안(Unity 폴더 자신 아래)인 것.</summary>
        static IEnumerable<string> Parents(string rel)
        {
            var parts = rel.Split('/');
            for (int i = parts.Length - 1; i >= 2; i--) yield return string.Join("/", parts, 0, i);
        }

        static void Close(string jfile, string name)
        {
            string to = Path.Combine(Path.GetDirectoryName(jfile), name);
            if (File.Exists(to)) File.Delete(to);
            File.Move(jfile, to);
        }

        /// <summary>공유 위반 · 잠시 막힘은 300 ms 간격 10번 다시.</summary>
        static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (Exception e) when (i < Retries && ((e is IOException && !(e is FileNotFoundException) && !(e is DirectoryNotFoundException)) || e is UnauthorizedAccessException))
                {
                    Thread.Sleep(RetryMs);
                }
            }
        }

        // ── 9. 결과 · 게임 다시 켜기 ──
        void Result(bool ok, string why)
        {
            string file = Path.Combine(upd, "result.json"), tmp = file + ".tmp";
            var o = new Dictionary<string, object> { { "format", 1 }, { "version", version }, { "from", from }, { "ok", ok }, { "why", why ?? "" } };
            try
            {
                File.WriteAllText(tmp, Json.Serialize(o), Program.Utf8);
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { log.Line("result not written: " + e.GetType().Name); }
            log.Line("result ok " + (ok ? "true" : "false") + (ok ? "" : " why " + why));
        }

        void Restart()
        {
            var psi = new ProcessStartInfo(Path.Combine(install, Program.Exe), string.Join(" ", args.Select(Program.Quote))) { UseShellExecute = false, WorkingDirectory = install };
            try
            {
                using (Process.Start(psi)) { }
                log.Line("restarted game, args " + args.Count);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException) { log.Line("restart failed: " + e.GetType().Name); }
        }

        string Abs(string rel) => Path.Combine(install, rel.Replace('/', '\\'));
        static string Under(string dir, string rel) => Path.Combine(dir, rel.Replace('/', '\\'));

        string Rel(string full) =>
            full.StartsWith(install + "\\", StringComparison.OrdinalIgnoreCase) ? full.Substring(install.Length + 1).Replace('\\', '/') : "(outside install)";
    }
}
