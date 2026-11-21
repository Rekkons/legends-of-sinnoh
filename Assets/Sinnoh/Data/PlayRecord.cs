using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Data
{
    /// <summary>올릴 묶음의 첫 줄: 묶음 형식 판 · 게임 판(빌드) · 사람을 가리키는 칸을 지운 머리.</summary>
    public sealed class PlayRecordHead
    {
        public int Record = PlayRecord.Version;
        public string GameVersion;
        public SaveHead Head;
    }

    /// <summary>저장 폴더 하나를 묶은 결과. 올리지 않을 판이면 <see cref="Bytes"/>가 null이고 <see cref="Skip"/>에 까닭.</summary>
    public sealed class PlayRecordPack
    {
        public string FileName;
        public byte[] Bytes;
        /// <summary>묶은 명령 줄 수와 그 줄들의 지문(<see cref="PlayRecord.Fingerprint"/>) — 올린 뒤 <see cref="PlayRecord.MarkUploaded"/>에 함께 적는다.</summary>
        public int Lines;
        public ulong Fingerprint;
        public string Skip;
    }

    /// <summary>묶음 하나를 푼 것. 형식 판이 이 빌드와 다르면 Head에는 FormatVersion만 있고 줄은 비어 있다(HumanRecord가 「Format」으로 건너뛴다).</summary>
    public sealed class PlayRecordFile
    {
        public string GameVersion;
        public SaveHead Head;
        public List<SaveLogLine> Lines = new List<SaveLogLine>();
    }

    /// <summary>
    /// 플레이 기록 올리기의 엔진 무관 몫(02 §12): 저장 폴더 하나 → 올릴 묶음(gzip 한 파일 — 첫 줄 <see cref="PlayRecordHead"/>, 그 뒤 log.jsonl 줄 그대로).
    /// 머리에서 사람을 가리킬 수 있는 칸을 지운다(<see cref="Scrub"/>). 명령 줄에는 숫자 · 열거값뿐이라 그대로 둔다. 갤럭시단 배틀이 없던 판은 묶지 않는다.
    /// 받은 쪽은 <see cref="Unpack"/> → 머리 + 줄을 HumanRecord.Read에 그대로 넣는다(스냅샷은 올리지 않는다 — 시드에서 다시 적용한다).
    /// </summary>
    public static class PlayRecord
    {
        /// <summary>묶음 형식 판.</summary>
        public const int Version = 1;
        /// <summary>올린 파일 이름의 앞 · 내려받은 파일의 끝.</summary>
        public const string Prefix = "rec_", Extension = ".gz";
        /// <summary>저장 폴더 안 표시 파일: 그 판에서 이미 올린 명령 줄 수(같은 판을 두 번 올리지 않는다).</summary>
        public const string MarkFile = "upload.mark";

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 사람을 가리킬 수 있는 칸을 지운 머리 사본: SaveId(방 저장은 연 날짜 · 시각, 옛 솔로 저장도 날짜 — 판 ID로 바꾼다) · 저장 시각 Summary.SavedAtMs ·
        /// 방 좌석의 재접속 토큰 Token · 닉네임 Nickname · 금지 토큰 목록 Banned. 나머지(시드 · 옵션 · 주인공 키 · 방 설정 · PC/웹 · 강퇴 여부 · 뱅크)는 그대로.
        /// </summary>
        public static SaveHead Scrub(SaveHead head)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            var r = SaveStore.Deserialize<SaveHead>(SaveStore.Serialize(head, false));
            r.SaveId = GameId(head);
            if (r.Summary != null) r.Summary.SavedAtMs = 0;
            if (r.Room != null)
            {
                if (r.Room.Seats != null)
                    foreach (var seat in r.Room.Seats)
                    {
                        if (seat == null) continue;
                        seat.Token = null;
                        seat.Nickname = null;
                    }
                r.Room.Banned = new List<string>();
            }
            return r;
        }

        /// <summary>판 ID = 게임 시드 16진 16자(시드는 판마다 새로 뽑는다).</summary>
        public static string GameId(SaveHead head) => head.Seed.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>
        /// 파일 이름: <c>rec_&lt;판 ID&gt;_f&lt;저장 형식 판&gt;_r&lt;규칙 판&gt;_v&lt;게임 판&gt;</c> — 글자 · 숫자 · _ · - 만(Cloud Save 키 규칙 [추정] — 게임 판의 다른 글자는 -로).
        /// 같은 판은 같은 이름이라 다시 올리면 덮어쓴다.
        /// </summary>
        public static string FileNameOf(SaveHead head, string gameVersion)
        {
            var v = new StringBuilder();
            foreach (char c in gameVersion ?? "")
                v.Append(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ? c : '-');
            if (v.Length == 0) v.Append('0');
            return Prefix + GameId(head) + "_f" + head.FormatVersion.ToString(CultureInfo.InvariantCulture) + "_r" + head.RulesVersion.ToString(CultureInfo.InvariantCulture) + "_v" + v;
        }

        /// <summary>
        /// 갤럭시단 배틀이 있었나: 갤럭시단 자리(SeatId.Galactic — AI든 사람 조종이든 그 자리로 답한다)의 명령 줄이 하나라도 있나.
        /// 다시 적용하지 않고 보는 어림이다 — 갤럭시단 쪽이 답하기 전에 끝난 판은 빠진다 [가정 plan §5 #550].
        /// </summary>
        public static bool HadGalacticBattle(IReadOnlyList<SaveLogLine> lines)
        {
            foreach (var line in lines)
                if (line.Seat == SeatId.Galactic) return true;
            return false;
        }

        /// <summary>
        /// 저장 폴더 하나를 묶는다(읽기만 한다). 올리지 않는 판: 머리를 못 읽음(다른 형식 판 포함) · 올린 것과 같음(<see cref="MarkFile"/>의 줄 수 · 지문) ·
        /// 갤럭시단 배틀 없음 — 다만 이 판을 올린 적이 있고 지금 로그가 그것과 다르면(무르기 · 쉬는 때로 돌아간 이어하기로 그 갈래를 버림) 갤럭시단 배틀이 없어도 같은 이름으로 다시 올려
        /// 서버의 버린 갈래를 덮어쓴다(결정 149, undo.md §4).
        /// 파일을 읽다 난 IOException(쓰는 중인 로그와 겹침)은 던진다 — 부르는 쪽이 그 판을 다음에 다시 본다.
        /// </summary>
        public static PlayRecordPack Pack(string saveDir, string gameVersion)
        {
            var pack = new PlayRecordPack();
            var entry = SaveStore.Peek(saveDir);
            if (!entry.Readable)
            {
                pack.Skip = "head:" + entry.Problem;
                return pack;
            }
            var lines = SaveStore.ReadLog(saveDir);
            pack.Lines = lines.Count;
            pack.Fingerprint = Fingerprint(lines);
            pack.FileName = FileNameOf(entry.Head, gameVersion);
            int uploaded = ReadMark(saveDir, out ulong mark);
            if (uploaded == lines.Count && (mark == 0 || mark == pack.Fingerprint))
            {
                pack.Skip = "uploaded";
                return pack;
            }
            if (uploaded < 0 && !HadGalacticBattle(lines))
            {
                pack.Skip = "no-galactic";
                return pack;
            }
            pack.Bytes = Gzip(Text(entry.Head, lines, gameVersion));
            return pack;
        }

        /// <summary>묶음의 글(gzip 앞): 첫 줄 = 지운 머리(<see cref="Scrub"/>), 그 뒤 = 명령 줄. 버그 신고(<see cref="BugReport"/>)도 지금 판 기록을 이 꼴로 싣는다.</summary>
        public static string Text(SaveHead head, IReadOnlyList<SaveLogLine> lines, string gameVersion)
        {
            var text = new StringBuilder();
            text.Append(SaveStore.Serialize(new PlayRecordHead { GameVersion = gameVersion, Head = Scrub(head) }, false)).Append('\n');
            foreach (var line in lines) text.Append(SaveStore.Serialize(line, false)).Append('\n');
            return text.ToString();
        }

        /// <summary>글을 UTF-8 gzip으로.</summary>
        public static byte[] Gzip(string text)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
                {
                    var raw = Utf8.GetBytes(text);
                    gz.Write(raw, 0, raw.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>이 게임이 올린 파일인가(플레이 기록 <see cref="Prefix"/> · 버그 신고 <see cref="BugReport.Prefix"/>) — 200개 한도의 오래된 것 지우기 · [내 기록 지우기]가 본다.</summary>
        public static bool Ours(string key) => key != null && (key.StartsWith(Prefix, StringComparison.Ordinal) || key.StartsWith(BugReport.Prefix, StringComparison.Ordinal));

        /// <summary>묶음을 푼다. gzip · 첫 줄이 깨졌으면 InvalidDataException. 명령 줄은 처음 깨진 줄에서 멈춘다(SaveStore.ReadLog와 같다).</summary>
        public static PlayRecordFile Unpack(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string text;
            try
            {
                using (var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
                using (var reader = new StreamReader(gz, Utf8))
                    text = reader.ReadToEnd();
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                throw new InvalidDataException("플레이 기록 묶음을 풀지 못했다: " + e.Message, e);
            }
            var rows = text.Split('\n');
            var file = new PlayRecordFile();
            try
            {
                var first = JObject.Parse(rows[0]);
                file.GameVersion = (string)first["GameVersion"];
                var format = first["Head"]?["FormatVersion"];
                if (format == null || format.Type != JTokenType.Integer) throw new InvalidDataException("플레이 기록 묶음의 머리에 형식 판이 없다");
                if ((int)format != SaveRecord.CurrentFormat)
                {
                    file.Head = new SaveHead { FormatVersion = (int)format };
                    return file;
                }
                file.Head = SaveStore.Deserialize<PlayRecordHead>(rows[0]).Head;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("플레이 기록 묶음의 머리가 깨졌다: " + e.Message, e);
            }
            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i].Length == 0) continue;
                try
                {
                    var line = SaveStore.Deserialize<SaveLogLine>(rows[i]);
                    if (line?.Command == null) break;
                    file.Lines.Add(line);
                }
                catch (JsonException)
                {
                    break;
                }
            }
            return file;
        }

        /// <summary>그 저장에서 이미 올린 명령 줄 수(표시가 없거나 깨졌으면 −1).</summary>
        public static int Uploaded(string saveDir) => ReadMark(saveDir, out _);

        /// <summary>표시 파일 「줄 수 지문(16진)」을 읽는다 — 지문이 없으면(옛 표시) 0. 표시가 없거나 깨졌으면 −1.</summary>
        static int ReadMark(string saveDir, out ulong fingerprint)
        {
            fingerprint = 0;
            string path = Path.Combine(saveDir, MarkFile);
            if (!File.Exists(path)) return -1;
            var parts = File.ReadAllText(path, Utf8).Trim().Split(' ');
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return -1;
            if (parts.Length > 1 && !ulong.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out fingerprint)) return -1;
            return n;
        }

        /// <summary>
        /// 올린 줄 수와 그 줄들의 지문(<see cref="PlayRecordPack.Fingerprint"/> — 0이면 줄 수만)을 적는다(저장 폴더가 그새 지워졌으면 아무것도 하지 않는다).
        /// 올리는 사이에 로그가 잘렸어도(결과 화면에서 되돌림) 올린 그대로를 적으므로, 다음 <see cref="Pack"/>이 지금 로그와 달라진 것을 보고 다시 올린다.
        /// </summary>
        public static void MarkUploaded(string saveDir, int lines, ulong fingerprint = 0)
        {
            if (!Directory.Exists(saveDir)) return;
            string text = lines.ToString(CultureInfo.InvariantCulture);
            if (fingerprint != 0) text += " " + fingerprint.ToString("x16", CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(saveDir, MarkFile), text, Utf8);
        }

        /// <summary>명령 줄들의 지문(직렬화한 줄 글의 FNV-1a 64) — 같은 줄 수라도 갈래가 다르면 다르다.</summary>
        public static ulong Fingerprint(IReadOnlyList<SaveLogLine> lines)
        {
            var h = new Fnv1a64();
            foreach (var line in lines)
            {
                foreach (byte b in Utf8.GetBytes(SaveStore.Serialize(line, false))) h.Byte(b);
                h.Byte((byte)'\n');
            }
            return h.Value;
        }
    }
}
