using System;
using System.Collections.Generic;
using System.IO;

namespace Sinnoh.Data
{
    /// <summary>
    /// 설치 폴더 안 경로 규칙(결정 161 K5 — 게임 · 도우미가 같은 표로 시험한다). manifest · plan의 경로 하나(<c>rel</c>)가 하나라도 어기면 판 전체를 거부한다(부분 적용 없음).
    /// 경로 위 재분석 지점(정션 · 심볼릭 링크) 검사는 도우미 몫(쓰기 직전).
    /// </summary>
    public static class InstallPath
    {
        /// <summary>
        /// 지워도 되는 자리(Inno <c>[InstallDelete]</c>와 같은 넷) — 그 아래에서 새 manifest에 없는 파일만 지운다. 맨 위 파일은 지우지 않는다.
        /// <c>WebGL/</c>은 옛 판(0.2.2까지)이 깔아 둔 웹 손님판 폴더 — 결정 210으로 뺀 뒤에도 남겨 두어 자동 업데이트가 그 안 파일을 지운다(도우미 SinnohUpdater와 같은 표).
        /// </summary>
        public static readonly string[] Roots = { "Sinnoh_Data/", "MonoBleedingEdge/", "D3D12/", "WebGL/" };

        /// <summary>설치 폴더 안 대기 자리(K3) — 견주기 · 지우기 · 포장은 늘 뺀다.</summary>
        public const string UpdateDir = "_update";

        public const int MaxLength = 260;

        static readonly HashSet<string> Reserved = MakeReserved();

        static HashSet<string> MakeReserved()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
            for (int i = 1; i <= 9; i++)
            {
                set.Add("COM" + i);
                set.Add("LPT" + i);
            }
            return set;
        }

        /// <summary>K5 경로 규칙. 어기면 거짓과 까닭(로그용 짧은 말).</summary>
        public static bool Safe(string rel, out string why)
        {
            why = Check(rel);
            return why == null;
        }

        static string Check(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "empty";
            if (rel.Length >= MaxLength) return "long";
            if (rel[0] == '/') return "rooted";
            foreach (char c in rel)
            {
                if (c == '\\') return "backslash";
                if (c < 32 || c == 127) return "control";
                if (c == ':' || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*') return "char";
            }
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0) return "empty-part";
                if (p == "." || p == "..") return "dot";
                char last = p[p.Length - 1];
                if (last == '.' || last == ' ') return "trail";
                int dot = p.IndexOf('.');
                if (Reserved.Contains((dot < 0 ? p : p.Substring(0, dot)).TrimEnd(' '))) return "reserved";
                if (i == 0 && string.Equals(p, UpdateDir, StringComparison.OrdinalIgnoreCase)) return "update";
            }
            return null;
        }

        /// <summary>지워도 되는 자리 아래의 파일인가(<see cref="Roots"/> 넷 중 하나의 아래 — 폴더 자신 · 맨 위 파일은 아니다).</summary>
        public static bool Deletable(string rel)
        {
            if (!Safe(rel, out _)) return false;
            foreach (var root in Roots)
                if (rel.Length > root.Length && rel.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>대소문자만 다른 두 경로가 있나(K5 — 있으면 판 전체 거부). 있으면 그 경로.</summary>
        public static string Duplicate(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
                if (p != null && !seen.Add(p)) return p;
            return null;
        }

        /// <summary>설치 폴더 + rel의 전체 경로. 합친 경로가 설치 폴더 밖이면(또는 규칙을 어기면) null.</summary>
        public static string Full(string install, string rel)
        {
            if (!Safe(rel, out _)) return null;
            string root = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>설치 폴더 기준 상대 경로(구분자 <c>/</c>).</summary>
        public static string Rel(string install, string full) => Path.GetRelativePath(install, full).Replace('\\', '/');
    }
}
