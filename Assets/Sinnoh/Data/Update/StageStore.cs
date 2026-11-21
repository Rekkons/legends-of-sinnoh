using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Sinnoh.Data
{
    /// <summary>
    /// 대기 자리 <c>_update/stage/</c>(결정 161 K3): 받는 중 <c>&lt;sha&gt;.part</c> → 크기 · 지문 확인 → <c>&lt;sha&gt;</c>로 이름 바꿈(틀리면 지우고 재시도 차례로).
    /// 지문 하나 = 파일 하나(같은 내용 여러 경로는 한 번만). zip 모드는 묶음 zip을 같은 자리에 받아 필요한 항목만 풀어 같은 확인.
    /// </summary>
    public static class StageStore
    {
        public const string Folder = "stage", PartSuffix = ".part";

        public static string Dir(string install) => Path.Combine(install, InstallPath.UpdateDir, Folder);

        public static string FileOf(string stage, string sha) => Path.Combine(stage, sha);

        public static string PartOf(string stage, string sha) => Path.Combine(stage, sha + PartSuffix);

        /// <summary>stage/에 크기가 맞게 이미 있는 지문(받아 확인한 것만 이 이름이 된다).</summary>
        public static HashSet<string> Staged(string stage, IEnumerable<ReleaseFile> files)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                if (!InstallScan.IsSha(f.Sha256)) continue;
                var info = new FileInfo(FileOf(stage, f.Sha256));
                if (info.Exists && info.Length == f.Size) set.Add(f.Sha256);
            }
            return set;
        }

        /// <summary>받는 중인 조각의 길이(없으면 0).</summary>
        public static long PartLength(string stage, string sha)
        {
            var info = new FileInfo(PartOf(stage, sha));
            return info.Exists ? info.Length : 0;
        }

        /// <summary>.part의 크기 · 지문을 확인해 stage/&lt;sha&gt;로 옮긴다. 틀리면 .part를 지우고 거짓(why = size · sha).</summary>
        public static bool Commit(string stage, string sha, long size, out string why, CancellationToken cancel = default)
        {
            string part = PartOf(stage, sha), file = FileOf(stage, sha);
            var info = new FileInfo(part);
            if (!info.Exists || info.Length != size)
            {
                why = "size";
                if (info.Exists) File.Delete(part);
                return false;
            }
            if (InstallScan.Sha256File(part, cancel) != sha)
            {
                why = "sha";
                File.Delete(part);
                return false;
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(part, file);
            why = null;
            return true;
        }

        /// <summary>
        /// 묶음 zip(pack의 <c>writeZip</c> — 항목 이름 <c>&lt;맨 위 폴더&gt;/&lt;경로&gt;</c>)에서 want의 지문만 stage/로 푼다. 항목 경로는 맨 위 폴더 한 조각을 떼고 manifest 경로와 맞춘다.
        /// 다 풀면 null, 아니면 까닭(missing · sha · zip).
        /// </summary>
        public static string Unzip(string zipFile, ReleaseManifest m, IEnumerable<ReleaseFile> want, string stage, CancellationToken cancel = default)
        {
            try
            {
                using (var file = new FileStream(zipFile, FileMode.Open, FileAccess.Read, FileShare.Read))   // zip이 아니면 ZipArchive가 던져 파일을 닫지 않는다 — 따로 닫는다
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var byPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in zip.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        int cut = name.IndexOf('/');
                        string rel = cut >= 0 ? name.Substring(cut + 1) : name;
                        if (rel.Length > 0 && !byPath.ContainsKey(rel)) byPath[rel] = e;
                    }
                    var paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    foreach (var f in m.Files)
                    {
                        if (!paths.TryGetValue(f.Sha256, out var list)) paths[f.Sha256] = list = new List<string>();
                        list.Add(f.Path);
                    }
                    foreach (var w in want)
                    {
                        cancel.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = null;
                        if (paths.TryGetValue(w.Sha256, out var list))
                            foreach (var p in list)
                                if (byPath.TryGetValue(p, out entry)) break;
                        if (entry == null) return "missing";
                        using (var src = entry.Open())
                        using (var dst = new FileStream(PartOf(stage, w.Sha256), FileMode.Create, FileAccess.Write))
                            src.CopyTo(dst);
                        if (!Commit(stage, w.Sha256, w.Size, out _, cancel)) return "sha";
                    }
                }
                return null;
            }
            catch (InvalidDataException)
            {
                return "zip";
            }
        }
    }
}
