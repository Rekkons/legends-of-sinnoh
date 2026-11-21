using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Sinnoh.Data
{
    /// <summary>버그 신고 묶음의 머리(M9 — 결정 132 ①): 부르는 쪽이 판 · 화면 몫을 채우고, <see cref="BugReport.Pack"/>이 실은 로그 · 기록 몫을 채운다.</summary>
    public sealed class BugReportInfo
    {
        public int Report = BugReport.Version;
        /// <summary>신고 하나의 이름표(16진 — 파일 이름에 들어간다).</summary>
        public string Id;
        /// <summary>사람이 적은 설명(앞뒤 빈칸을 떼고 <see cref="BugReport.MaxDescription"/>자까지).</summary>
        public string Description;
        public string GameVersion;
        public int RulesVersion;
        public int ProtocolVersion;
        public string Lang;
        /// <summary>창 크기(「1280x720」, 전체 화면이면 뒤에 「 full」).</summary>
        public string Screen;
        public string Platform;
        /// <summary>어디서 보냈나: title · solo · tutorial · host(방장) · guest(손님).</summary>
        public string Role;
        public long SentAtMs;
        /// <summary>로그 파일의 원래 크기(B — 0 = 로그 없음).</summary>
        public long LogSize;
        /// <summary>실은 로그(B, UTF-8 — 끝에서부터 상한까지).</summary>
        public int LogBytes;
        /// <summary>지금 판 기록: null = 실음 · none(판 없음 · 저장하지 않는 판 · 손님) · head:&lt;까닭&gt; · busy(쓰는 중이라 못 읽음) · too-big.</summary>
        public string RecordNote;
        public int RecordLines;
    }

    /// <summary>묶은 결과(올릴 이름 · gzip 바이트 · 채운 머리).</summary>
    public sealed class BugReportPack
    {
        public string FileName;
        public byte[] Bytes;
        public BugReportInfo Info;
    }

    /// <summary>묶음을 푼 것: 머리 · 로그(없으면 null) · 지금 판 기록 글(<see cref="PlayRecord.Text"/> 꼴 — 없으면 null).</summary>
    public sealed class BugReportFile
    {
        public BugReportInfo Info;
        public string Log;
        public string Record;
    }

    /// <summary>
    /// 게임 안 [버그 신고]의 엔진 무관 몫(M9 — 결정 132 ①): 설명 · 판 정보 · 로그 끝부분 · 지금 판 기록을 gzip 한 파일(JSON <c>{ Info, Log, Record }</c>)로 묶는다.
    /// 판 기록은 플레이 기록 올리기와 같은 지운 머리(<see cref="PlayRecord.Scrub"/> — 닉네임 · 재접속 토큰 · 금지 토큰 · 저장 이름 · 시각)를 쓴다.
    /// 로그는 끝에서 <see cref="MaxLogBytes"/>까지 읽고, 부르는 쪽이 준 글(닉네임 · 토큰 · 사용자 폴더 경로)을 <see cref="Redacted"/>로 바꾼다.
    /// 묶음이 <see cref="MaxPackBytes"/>를 넘으면 로그를 반씩 줄이고, 그래도 넘으면 판 기록을 뺀다. 파일 이름 앞머리는 <see cref="Prefix"/>(플레이 기록 rec_와 나눈다).
    /// </summary>
    public static class BugReport
    {
        public const int Version = 1;
        public const string Prefix = "bug_";
        /// <summary>설명 글자 수 상한(글 칸의 maxLength — 결정 136: 1,000 → 4,000).</summary>
        public const int MaxDescription = 4000;
        /// <summary>실을 로그의 상한(B) — 끝에서부터.</summary>
        public const int MaxLogBytes = 256 * 1024;
        /// <summary>묶음(gzip) 하나의 상한(B) — Cloud Save Player Files 한도(플레이어당 1 GiB · 200개)보다 훨씬 작게.</summary>
        public const int MaxPackBytes = 1024 * 1024;
        /// <summary>로그를 줄일 때 이보다는 남긴다(B).</summary>
        public const int MinLogBytes = 16 * 1024;
        public const string Redacted = "<redacted>";

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        sealed class Body
        {
            public BugReportInfo Info;
            public string Log;
            public string Record;
        }

        /// <summary>[보내기]를 켤 설명인가(빈칸만이 아니다).</summary>
        public static bool CanSend(string description) => !string.IsNullOrWhiteSpace(description);

        /// <summary>파일 이름: <c>bug_&lt;보낸 시각 ms(유닉스)&gt;_&lt;이름표&gt;_v&lt;게임 판&gt;</c> — 글자 · 숫자 · _ · - 만(<see cref="PlayRecord.FileNameOf"/>와 같은 규칙). 시각은 부르는 쪽이 넣은 값 그대로(엔진 무관 몫은 시계를 읽지 않는다).</summary>
        public static string FileNameOf(BugReportInfo info) =>
            Prefix + Math.Max(0, info.SentAtMs).ToString(CultureInfo.InvariantCulture) + "_" + Safe(info.Id) + "_v" + Safe(info.GameVersion);

        static string Safe(string s)
        {
            var v = new StringBuilder();
            foreach (char c in s ?? "")
                v.Append(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ? c : '-');
            if (v.Length == 0) v.Append('0');
            return v.ToString();
        }

        /// <summary>
        /// 로그 파일의 끝부분(<paramref name="maxBytes"/> B까지 — 잘랐으면 처음 줄바꿈 뒤부터, 깨진 글자 · 반 줄 없이). 엔진이 쓰는 중인 파일이라 쓰기 공유로 연다.
        /// 파일이 없으면 null(<paramref name="size"/> 0).
        /// </summary>
        public static string Tail(string path, int maxBytes, out long size)
        {
            size = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                size = fs.Length;
                long start = Math.Max(0, size - Math.Max(0, maxBytes));
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[size - start];
                int got = 0;
                while (got < buf.Length)
                {
                    int n = fs.Read(buf, got, buf.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                string text = Utf8.GetString(buf, 0, got);
                return start > 0 ? AfterNewline(text) : text;
            }
        }

        static string AfterNewline(string text)
        {
            int nl = text.IndexOf('\n');
            return nl >= 0 ? text.Substring(nl + 1) : "";
        }

        /// <summary>글에서 가릴 것(두 글자 이상 — 긴 것부터)을 <see cref="Redacted"/>로 바꾼다.</summary>
        public static string Redact(string text, IEnumerable<string> secrets)
        {
            if (string.IsNullOrEmpty(text) || secrets == null) return text;
            foreach (string s in secrets.Where(s => s != null && s.Length >= 2).Distinct().OrderByDescending(s => s.Length))
                text = text.Replace(s, Redacted);
            return text;
        }

        /// <summary>
        /// 묶는다(읽기만 한다). <paramref name="info"/>의 설명을 다듬고 로그 · 기록 몫(<see cref="BugReportInfo.LogSize"/> · <see cref="BugReportInfo.LogBytes"/> · <see cref="BugReportInfo.RecordNote"/> · <see cref="BugReportInfo.RecordLines"/>)을 채운다.
        /// <paramref name="logPath"/> · <paramref name="saveDir"/>가 null이거나 없으면 그 몫 없이. 판 기록을 읽다 난 IOException(쓰는 중)은 「busy」로 적고 기록 없이 묶는다.
        /// </summary>
        public static BugReportPack Pack(BugReportInfo info, string logPath, string saveDir, IEnumerable<string> secrets, int maxLog = MaxLogBytes, int maxPack = MaxPackBytes)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            string desc = (info.Description ?? "").Trim();
            info.Description = desc.Length > MaxDescription ? desc.Substring(0, MaxDescription) : desc;

            string log = Redact(Tail(logPath, maxLog, out long size), secrets);
            info.LogSize = size;

            string record = null;
            info.RecordLines = 0;
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) info.RecordNote = "none";
            else
            {
                var entry = SaveStore.Peek(saveDir);
                if (!entry.Readable) info.RecordNote = "head:" + entry.Problem;
                else
                {
                    try
                    {
                        var lines = SaveStore.ReadLog(saveDir);
                        record = PlayRecord.Text(entry.Head, lines, info.GameVersion);
                        info.RecordLines = lines.Count;
                        info.RecordNote = null;
                    }
                    catch (IOException)
                    {
                        info.RecordNote = "busy";
                    }
                }
            }

            // 상한: 로그를 반씩(최소 MinLogBytes까지) → 판 기록 빼기 → 로그 빼기
            while (true)
            {
                info.LogBytes = log == null ? 0 : Utf8.GetByteCount(log);
                var bytes = PlayRecord.Gzip(SaveStore.Serialize(new Body { Info = info, Log = log, Record = record }, false));
                if (bytes.Length <= maxPack || (log == null && record == null))
                    return new BugReportPack { FileName = FileNameOf(info), Bytes = bytes, Info = info };
                if (log != null && info.LogBytes > MinLogBytes) log = AfterNewline(log.Substring(log.Length / 2));
                else if (record != null)
                {
                    record = null;
                    info.RecordNote = "too-big";
                    info.RecordLines = 0;
                }
                else log = null;
            }
        }

        /// <summary>묶음을 푼다(걷기 · 시험). gzip · JSON이 깨졌으면 InvalidDataException.</summary>
        public static BugReportFile Unpack(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            try
            {
                string text;
                using (var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
                using (var reader = new StreamReader(gz, Utf8))
                    text = reader.ReadToEnd();
                var body = SaveStore.Deserialize<Body>(text) ?? throw new InvalidDataException("버그 신고 묶음이 비었다");
                return new BugReportFile { Info = body.Info, Log = body.Log, Record = body.Record };
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is JsonException)
            {
                throw new InvalidDataException("버그 신고 묶음을 풀지 못했다: " + e.Message, e);
            }
        }
    }
}
