using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sinnoh.Data
{
    /// <summary>목차의 장 한 줄 표시(07 §2-1 — 끝까지 본 장만 ✓, 들어갔다 나온 장은 [이어서]).</summary>
    public enum TutorialMark { None, Resume, Seen }

    /// <summary>
    /// 튜토리얼 기록(07 §2-2 · [가정 T4]): 마지막으로 들어간 장 `tutorial.last` · 끝까지 본 장 `tutorial.seen.&lt;장&gt;`. 판은 저장하지 않으므로(결정 90 Q6) 이것뿐이다.
    /// 자리는 사용자 저장 폴더(`saves/`)가 아니라 설정 쪽의 작은 파일 하나(<see cref="FileName"/> — 한 줄에 `키=값`). 폴더가 null이면 실행하는 동안만 쥔다
    /// (에디터·캡처·확인용 플레이어 — 시험은 임시 폴더를 준다). 파일을 읽지 못하거나 모르는 줄이 있으면 그 줄만 버린다(튜토리얼 기록 때문에 게임이 서지 않게),
    /// 쓰기 실패는 <see cref="Error"/>에 남기고 던지지 않는다.
    /// </summary>
    public sealed class TutorialRecord
    {
        public const string FileName = "tutorial.txt";
        public const string LastKey = "tutorial.last", SeenPrefix = "tutorial.seen.";

        readonly string folder;
        string last;
        readonly SortedSet<string> seen = new SortedSet<string>(StringComparer.Ordinal);

        TutorialRecord(string folder)
        {
            this.folder = string.IsNullOrEmpty(folder) ? null : folder;
        }

        /// <summary>실행하는 동안만 쥐는 기록(파일 없음).</summary>
        public static TutorialRecord Memory() => new TutorialRecord(null);

        /// <summary>그 폴더의 기록 파일을 읽는다(없으면 빈 기록). null이면 <see cref="Memory"/>.</summary>
        public static TutorialRecord Open(string folder)
        {
            var r = new TutorialRecord(folder);
            if (r.folder == null) return r;
            try
            {
                if (File.Exists(r.PathOf)) r.Parse(File.ReadAllText(r.PathOf, new UTF8Encoding(false)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                r.Error = e.Message;
            }
            return r;
        }

        /// <summary>기록 파일 경로(폴더가 없으면 null).</summary>
        public string PathOf => folder != null ? Path.Combine(folder, FileName) : null;

        /// <summary>마지막 읽기·쓰기 실패(없으면 null).</summary>
        public string Error { get; private set; }

        /// <summary>기록이 하나라도 있나 — 타이틀 [튜토리얼]이 없으면 본편 첫 장으로 바로, 있으면 목차를 연다(07 §2-2).</summary>
        public bool Any => last != null || seen.Count > 0;

        /// <summary>마지막으로 들어간 장(없으면 null).</summary>
        public string Last => last;

        public bool Seen(string chapter) => chapter != null && seen.Contains(chapter);

        public IEnumerable<string> SeenChapters => seen;

        /// <summary>그 장의 표시: 끝까지 봤으면 ✓, 마지막으로 들어갔다 나왔으면 [이어서](그 장 처음부터), 아니면 없음.</summary>
        public TutorialMark MarkOf(string chapter) => Seen(chapter) ? TutorialMark.Seen : chapter != null && chapter == last ? TutorialMark.Resume : TutorialMark.None;

        /// <summary>타이틀 「처음이라면!」 딱지(07 T3): 본편의 마지막 장을 끝까지 보기 전까지.</summary>
        public bool FirstTime(TutorialChapter lastMain) => lastMain == null || !Seen(lastMain.Id);

        /// <summary>장에 들어갔다(처음부터 · 빨리감기로 · 이어서) — `tutorial.last`.</summary>
        public void Enter(string chapter)
        {
            Check(chapter);
            if (last == chapter) return;
            last = chapter;
            Save();
        }

        /// <summary>그 장을 끝까지 봤다(건너뛴 장은 부르지 않는다 — 결정 90 Q7 ①).</summary>
        public void MarkSeen(string chapter)
        {
            Check(chapter);
            if (seen.Add(chapter)) Save();
        }

        static void Check(string chapter)
        {
            if (string.IsNullOrEmpty(chapter) || chapter.IndexOf('=') >= 0 || chapter.IndexOf('\n') >= 0 || chapter.IndexOf('\r') >= 0)
                throw new ArgumentException("장 id \"" + chapter + "\"", nameof(chapter));
        }

        /// <summary>파일 내용(한 줄에 `키=값`, 키 순서 고정 · LF).</summary>
        public string Text()
        {
            var sb = new StringBuilder();
            if (last != null) sb.Append(LastKey).Append('=').Append(last).Append('\n');
            foreach (var c in seen) sb.Append(SeenPrefix).Append(c).Append("=1\n");
            return sb.ToString();
        }

        void Parse(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                if (key == LastKey && value.Length > 0) last = value;
                else if (key.StartsWith(SeenPrefix, StringComparison.Ordinal) && key.Length > SeenPrefix.Length && value == "1") seen.Add(key.Substring(SeenPrefix.Length));
            }
        }

        void Save()
        {
            if (folder == null) return;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(PathOf, Text(), new UTF8Encoding(false));
                Error = null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Error = e.Message;
            }
        }
    }
}
