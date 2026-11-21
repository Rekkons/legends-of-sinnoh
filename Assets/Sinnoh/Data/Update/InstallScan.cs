using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sinnoh.Data
{
    /// <summary>설치 폴더에 있는 게임 파일 하나(견주기 결과). <see cref="Sha256"/>이 null이면 크기가 manifest와 달라 재지 않았다(바뀜).</summary>
    public sealed class LocalFile
    {
        public string Path;
        public long Size;
        public string Sha256;
    }

    /// <summary>
    /// 설치 폴더 견주기(결정 161 4-1): manifest의 파일마다 디스크의 크기 · 지문, 그리고 지울 자리(<see cref="InstallPath.Roots"/>) 아래 디스크 파일 목록.
    /// 크기가 다르면 지문을 재지 않는다. 크기가 같으면 지문 캐시(<c>_update/hashes.json</c> — <c>{"경로":[크기, 수정 시각 ticks, "sha"]}</c>)의 (크기, 수정 시각)이 같을 때 캐시 지문, 아니면 SHA-256을 잰다.
    /// 다 잰 뒤 캐시를 다시 쓴다. 다른 스레드에서 부른다(첫 스캔 362 MB 약 1~3초) — 진행률은 (잰 바이트, 잴 바이트).
    /// 수정 시각은 파일 상태(캐시 열쇠)이지 시계가 아니다.
    /// </summary>
    public sealed class InstallScan
    {
        public const string CacheFile = "hashes.json";

        public readonly List<LocalFile> Files = new List<LocalFile>();

        /// <summary>지울 자리 아래 디스크 파일(설치 기준 상대 경로, <c>/</c>).</summary>
        public readonly List<string> Disk = new List<string>();

        public static InstallScan Run(string install, ReleaseManifest manifest, Action<long, long> progress = null, CancellationToken cancel = default)
        {
            var scan = new InstallScan();
            string cacheFile = Path.Combine(install, InstallPath.UpdateDir, CacheFile);
            var cache = ReadCache(cacheFile);
            var fresh = new Dictionary<string, CacheRow>(StringComparer.Ordinal);
            var measure = new List<KeyValuePair<LocalFile, FileInfo>>();
            long total = 0;
            foreach (var f in manifest.Files)
            {
                cancel.ThrowIfCancellationRequested();
                string full = InstallPath.Full(install, f.Path);
                if (full == null || !File.Exists(full)) continue;
                var info = new FileInfo(full);
                var local = new LocalFile { Path = f.Path, Size = info.Length };
                scan.Files.Add(local);
                if (info.Length != f.Size) continue;
                long stamp = info.LastWriteTimeUtc.Ticks;
                if (cache.TryGetValue(f.Path, out var row) && row.Size == info.Length && row.Stamp == stamp && row.Sha != null)
                {
                    local.Sha256 = row.Sha;
                    fresh[f.Path] = row;
                    continue;
                }
                measure.Add(new KeyValuePair<LocalFile, FileInfo>(local, info));
                total += info.Length;
            }
            long done = 0;
            progress?.Invoke(0, total);
            foreach (var m in measure)
            {
                long before = done;
                m.Key.Sha256 = Sha256File(m.Value.FullName, cancel, n => progress?.Invoke(before + n, total));
                done += m.Value.Length;
                m.Value.Refresh();
                fresh[m.Key.Path] = new CacheRow { Size = m.Value.Length, Stamp = m.Value.LastWriteTimeUtc.Ticks, Sha = m.Key.Sha256 };
            }
            foreach (var root in InstallPath.Roots)
            {
                string dir = Path.Combine(install, root.TrimEnd('/'));
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    cancel.ThrowIfCancellationRequested();
                    scan.Disk.Add(InstallPath.Rel(install, file));
                }
            }
            scan.Disk.Sort(StringComparer.Ordinal);
            WriteCache(cacheFile, fresh);
            return scan;
        }

        struct CacheRow
        {
            public long Size, Stamp;
            public string Sha;
        }

        static Dictionary<string, CacheRow> ReadCache(string file)
        {
            var map = new Dictionary<string, CacheRow>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(file)) return map;
                foreach (var p in JObject.Parse(File.ReadAllText(file, Encoding.UTF8)).Properties())
                    if (p.Value is JArray a && a.Count == 3)
                        map[p.Name] = new CacheRow { Size = (long)a[0], Stamp = (long)a[1], Sha = (string)a[2] };
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is InvalidCastException || e is ArgumentException || e is FormatException || e is UnauthorizedAccessException)
            {
                map.Clear();   // 망가진 캐시는 버리고 다시 잰다
            }
            return map;
        }

        static void WriteCache(string file, Dictionary<string, CacheRow> rows)
        {
            var o = new JObject();
            var keys = new List<string>(rows.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) o[k] = new JArray(rows[k].Size, rows[k].Stamp, rows[k].Sha);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                WriteAtomic(file, o.ToString(Formatting.None));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 캐시를 못 써도 견주기는 맞다(다음에 다시 잴 뿐)
            }
        }

        /// <summary>임시 파일에 쓰고 이름을 바꾼다(UTF-8, BOM 없음).</summary>
        internal static void WriteAtomic(string file, string text)
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        /// <summary>파일의 SHA-256(소문자 hex 64자). progress = 읽은 바이트.</summary>
        public static string Sha256File(string file, CancellationToken cancel = default, Action<long> progress = null)
        {
            using (var sha = SHA256.Create())
            using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var buf = new byte[1 << 20];
                long read = 0;
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    sha.TransformBlock(buf, 0, n, null, 0);
                    read += n;
                    progress?.Invoke(read);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return Hex(sha.Hash);
            }
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>소문자 hex 64자인가(K1 sha256 꼴).</summary>
        public static bool IsSha(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
