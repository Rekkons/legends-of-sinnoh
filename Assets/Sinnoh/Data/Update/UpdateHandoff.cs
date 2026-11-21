using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sinnoh.Data
{
    /// <summary>도우미가 남긴 결과(<c>_update/result.json</c> — K3).</summary>
    public sealed class UpdateResult
    {
        public bool Ok;
        public string Version, From, Why;
    }

    /// <summary>
    /// 게임 → 업데이트 도우미 넘기기(결정 161 K3 · 4-1): <c>_update/&lt;판&gt;/latest.json</c>(받은 글 그대로) · <c>plan.json</c> 쓰기, <c>run/SinnohUpdater.exe</c> 준비(늘 새 manifest의 도우미),
    /// 도우미 인자(Windows 따옴표 규칙), 켤 때 <c>result.json</c> 읽기 · 정리, 쓰기 판정 · 우리 설치 판정 · 디스크 여유 · 뮤텍스 이름.
    /// </summary>
    public static class UpdateHandoff
    {
        public const string ResultFile = "result.json", PlanFile = "plan.json", LatestFile = "latest.json", RunDir = "run", VersionFile = "version.txt";
        public const int PlanFormat = 1;

        public static string Root(string install) => Path.Combine(install, InstallPath.UpdateDir);

        public static string VersionDir(string install, string version) => Path.Combine(Root(install), version);

        /// <summary>plan.json 글(format 1 — K3).</summary>
        public static string PlanJson(UpdatePlan plan, int pid, string exe, IEnumerable<string> args)
        {
            var put = new JArray();
            foreach (var f in plan.Put) put.Add(new JObject { ["path"] = f.Path, ["sha256"] = f.Sha256, ["size"] = f.Size });
            return new JObject
            {
                ["format"] = PlanFormat, ["version"] = plan.Version, ["from"] = plan.From, ["pid"] = pid, ["exe"] = exe,
                ["put"] = put, ["delete"] = new JArray(plan.Delete.ToArray()), ["args"] = new JArray(new List<string>(args ?? new string[0]).ToArray()),
            }.ToString(Formatting.None);
        }

        /// <summary><c>&lt;판&gt;/latest.json</c>(받은 글 그대로) · <c>plan.json</c>을 쓰고 plan.json 경로를 돌려준다.</summary>
        public static string Write(string install, ReleaseManifest m, UpdatePlan plan, int pid, string exe, IEnumerable<string> args)
        {
            string dir = VersionDir(install, plan.Version);
            Directory.CreateDirectory(dir);
            InstallScan.WriteAtomic(Path.Combine(dir, LatestFile), m.Text);
            string file = Path.Combine(dir, PlanFile);
            InstallScan.WriteAtomic(file, PlanJson(plan, pid, exe, args));
            return file;
        }

        /// <summary>
        /// <c>run/SinnohUpdater.exe</c>를 놓는다: 늘 새 manifest의 도우미 — 설치된 것의 지문이 같으면 그것을, 다르면 받아 둔 stage/&lt;sha&gt;를 복사한다.
        /// 놓은 경로, 못 놓으면 null(why = helper · missing).
        /// </summary>
        public static string RunHelper(string install, ReleaseManifest m, IReadOnlyList<LocalFile> local, string stage, out string why)
        {
            var entry = m.Files.Find(f => f.Path == UpdatePlan.Helper);
            if (entry == null) { why = "helper"; return null; }
            string src = null;
            foreach (var l in local)
                if (l.Path == UpdatePlan.Helper && l.Size == entry.Size && l.Sha256 == entry.Sha256) src = Path.Combine(install, UpdatePlan.Helper);
            if (src == null)
            {
                var staged = new FileInfo(StageStore.FileOf(stage, entry.Sha256));
                if (staged.Exists && staged.Length == entry.Size) src = staged.FullName;
            }
            if (src == null) { why = "missing"; return null; }
            string dir = Path.Combine(Root(install), RunDir), exe = Path.Combine(dir, UpdatePlan.Helper);
            Directory.CreateDirectory(dir);
            if (File.Exists(exe)) File.Delete(exe);
            File.Copy(src, exe);
            why = null;
            return exe;
        }

        /// <summary>도우미 인자 한 줄: <c>--install "&lt;설치&gt;" --plan "&lt;plan.json&gt;"</c>(K3 — 판이 올라도 이 꼴을 지킨다).</summary>
        public static string Args(string install, string planFile) => Join(new[] { "--install", install, "--plan", planFile });

        public static string Join(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(a));
            }
            return sb.ToString();
        }

        /// <summary>인자 하나를 Windows 규칙(CommandLineToArgvW)대로 따옴표 처리한다: 공백 · 탭 · 따옴표가 없으면 그대로, 있으면 감싸고 따옴표 앞 역슬래시 · 끝 역슬래시를 두 배로.</summary>
        public static string Quote(string a)
        {
            if (a == null) a = "";
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\v' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') sb.Append('\\', slashes * 2 + 1);
                else sb.Append('\\', slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        /// <summary>도우미 결과를 읽는다(없거나 망가졌으면 null).</summary>
        public static UpdateResult ReadResult(string install)
        {
            string file = Path.Combine(Root(install), ResultFile);
            try
            {
                if (!File.Exists(file)) return null;
                var o = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                return new UpdateResult { Ok = (bool?)o["ok"] ?? false, Version = (string)o["version"], From = (string)o["from"], Why = (string)o["why"] ?? "" };
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is InvalidCastException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// 켤 때 정리. 성공이고 판이 지금 판이면 stage/ · 판 폴더 · run/ · result.json을 지운다(hashes.json · updater.log는 남김).
        /// 실패(되돌림 성공)면 판 폴더 · run/ · result.json만 지우고 받아 둔 stage/는 남긴다(다시 하면 이어서). 되돌림마저 실패(why = rollback)면 아무것도 지우지 않는다(backup이 남는다 — 다시 설치 안내).
        /// result가 없으면 run/만 지운다(앞 실행의 도우미 사본).
        /// </summary>
        public static void Tidy(string install, string current, UpdateResult r)
        {
            string root = Root(install);
            if (!Directory.Exists(root)) return;
            if (r != null && !r.Ok && r.Why == "rollback") return;
            Gone(Path.Combine(root, RunDir));
            if (r == null) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (ReleaseVersion.TryParse(name, out _)) Gone(dir);
            }
            if (r.Ok && r.Version == current) Gone(Path.Combine(root, StageStore.Folder));
            try { File.Delete(Path.Combine(root, ResultFile)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        static void Gone(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }   // 도우미가 아직 run/에서 끝나는 중 — 다음에
        }

        /// <summary>설치 폴더에 쓸 수 있나: _update/를 만들고 임시 파일 쓰기 · 이름 바꾸기 · 지우기(Program Files · 제어된 폴더 액세스 · 읽기 전용이면 거짓 → [받기]).</summary>
        public static bool CanWrite(string install)
        {
            try
            {
                string root = Root(install);
                Directory.CreateDirectory(root);
                string a = Path.Combine(root, "write-test.tmp"), b = Path.Combine(root, "write-test.ok");
                if (File.Exists(b)) File.Delete(b);
                File.WriteAllText(a, "ok");
                File.Move(a, b);
                File.Delete(b);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 「우리 설치」면 설치 폴더(<c>Application.dataPath</c>의 부모), 아니면 null: <c>Sinnoh.exe</c>가 있고 <c>version.txt</c> = 지금 판이어야 한다.
        /// 드라이브 뿌리 · Windows 폴더 아래면 null.
        /// </summary>
        public static string InstallOf(string dataPath, string version, string windows = null)
        {
            try
            {
                if (string.IsNullOrEmpty(dataPath)) return null;
                var dir = Directory.GetParent(Path.GetFullPath(dataPath));
                if (dir == null || dir.Parent == null) return null;
                string install = dir.FullName;
                if (!string.IsNullOrEmpty(windows) && (install + "\\").StartsWith(windows.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
                if (!File.Exists(Path.Combine(install, UpdatePlan.Exe))) return null;
                string v = Path.Combine(install, VersionFile);
                if (!File.Exists(v) || File.ReadAllText(v).Trim() != version) return null;
                return install;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>설치 드라이브의 여유 바이트(모르면 -1).</summary>
        public static long FreeBytes(string install)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(install))).AvailableFreeSpace; }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) { return -1; }
        }

        /// <summary>
        /// 도우미가 바꾸는 동안 잡는 이름 있는 뮤텍스(3-2 ② — 게임이 켜질 때 잡혀 있으면 바로 끝낸다): <c>Local\SinnohUpdate-</c> + SHA-256(UTF-8(<c>GetFullPath(설치)</c>에서 끝 구분자를 떼고 소문자))의 hex 앞 16자.
        /// </summary>
        public static string MutexName(string install)
        {
            string key = Path.GetFullPath(install).TrimEnd('\\', '/').ToLowerInvariant();
            using (var sha = SHA256.Create())
                return @"Local\SinnohUpdate-" + InstallScan.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
        }
    }
}
