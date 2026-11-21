using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Data
{
    /// <summary>
    /// 세이브 파일(02 §7): 폴더 하나 = 저장 하나(경로는 인자, persistentDataPath/saves/&lt;SaveId&gt;는 Client가 정한다). 저장 폴더의 목록·지우기는 <see cref="List"/>·<see cref="Delete"/>.
    /// head.json(머리·요약) · snap-&lt;CommandCount&gt;.json(호스트 원본 GameState + Checkpoints, 최근 2개) · log.jsonl(명령 한 줄씩).
    /// head·snap은 임시 파일에 쓰고 바꿔치기(원자적), log는 덧붙이고 flush한다. 직렬화는 public 인스턴스 필드만(Rules 상태 클래스는 필드뿐이다).
    /// 부른 스레드에서 쓴다(솔로) — 방은 쓰기 스레드 <see cref="SaveWriter"/>로 감싼다(02 §7 「파일 쓰기는 호스트 작업 스레드에서」).
    /// </summary>
    public enum SaveProblem
    {
        None,
        /// <summary>머리가 없거나 깨졌다 · 스냅샷이 없다.</summary>
        Broken,
        /// <summary>옛 형식 판(옮기는 길이 없다).</summary>
        OldFormat,
        /// <summary>이 빌드보다 새 형식 판.</summary>
        NewFormat,
        /// <summary>데이터 표가 그때와 다르다(목록을 읽는 쪽 — Client — 이 지금 데이터와 견줘 단다).</summary>
        DataChanged,
    }

    /// <summary>저장 목록의 한 줄(04 S04): 폴더 이름 = 저장 이름, 머리(요약 포함). 읽지 못했으면 Head가 null이고 Problem·Detail에 까닭.</summary>
    public sealed class SaveEntry
    {
        public string SaveId;
        public string Directory;
        public SaveHead Head;
        public SaveProblem Problem;
        /// <summary>읽지 못한 까닭(로그용 — 화면에는 「읽을 수 없음」만).</summary>
        public string Detail;
        public bool Readable => Problem == SaveProblem.None && Head != null;

        public void Fail(SaveProblem problem, string detail)
        {
            Problem = problem;
            Detail = detail;
        }
    }

    public sealed class SaveStore : ISaveStore
    {
        public const string HeadFile = "head.json";
        public const string LogFile = "log.jsonl";
        const string SnapPrefix = "snap-";
        const string SnapSuffix = ".json";
        /// <summary>남겨 둘 스냅샷 수(02 §7 「최근 2개만」).</summary>
        public const int KeepSnapshots = 2;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new FieldsOnly(),
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Error,
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Include,
        };

        readonly string dir;
        bool logRepaired;

        /// <summary>그 폴더에 쓴다(없으면 만든다). 이어 쓰는 세이브면 첫 덧붙이기 전에 잘린 마지막 줄을 잘라 낸다.</summary>
        public SaveStore(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("세이브 폴더가 없다", nameof(directory));
            dir = directory;
            Directory.CreateDirectory(dir);
        }

        public void WriteHead(SaveHead head) => WriteHeadJson(HeadJson(head));

        /// <summary>머리 JSON. 쓰기 스레드(<see cref="SaveWriter"/>)는 부르는 쪽 스레드에서 미리 만든다 — 머리는 방이 뒤에 고친다.</summary>
        public static string HeadJson(SaveHead head) => Serialize(head, true);

        public void WriteHeadJson(string json) => AtomicWrite(Path.Combine(dir, HeadFile), json);

        public void WriteSnapshot(SaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            AtomicWrite(Path.Combine(dir, SnapPrefix + snapshot.CommandCount + SnapSuffix), Serialize(snapshot, false));
            // 되돌림(무르기, undo.md §4): 이보다 뒤의 스냅샷은 버린 미래다 — 새 스냅샷을 다 쓴 뒤에 지운다(쓰기가 실패하면 옛 스냅샷 + 로그로 원래 판이 열린다).
            // 평소에는 늘 커지는 순서라 걸리지 않는다. 그 뒤 최근 2개만
            var snaps = Snapshots(dir);
            int kept = 0;
            foreach (var s in snaps)
                if (s.Number > snapshot.CommandCount || ++kept > KeepSnapshots) File.Delete(s.Path);
        }

        public void AppendCommand(SaveLogLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            AppendLineJson(Serialize(line, false));
        }

        /// <summary>로그 한 줄(직렬화한 <see cref="SaveLogLine"/>)을 덧붙이고 flush한다.</summary>
        public void AppendLineJson(string json)
        {
            string path = Path.Combine(dir, LogFile);
            if (!logRepaired)
            {
                RepairTail(path);
                logRepaired = true;
            }
            var bytes = Utf8.GetBytes(json + "\n");
            try
            {
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
            }
            catch
            {
                logRepaired = false; // 쓰다 만 조각이 남았을 수 있다 — 다음 덧붙이기 전에 다시 잘라 낸다
                throw;
            }
        }

        public void TruncateLog(int commandCount)
        {
            string path = Path.Combine(dir, LogFile);
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path, Utf8);
            int keep = 0;
            long last = long.MinValue;
            while (keep < text.Length)
            {
                int nl = text.IndexOf('\n', keep);
                if (nl < 0) break;   // 쓰다 끊긴 마지막 줄도 버린다
                SaveLogLine line;
                try
                {
                    line = Deserialize<SaveLogLine>(text.Substring(keep, nl - keep));
                }
                catch (JsonException)
                {
                    break;   // 가운데가 깨진 줄: 읽기(ReadLog)가 여기서 멈추므로 그 뒤는 쓰이지 않는다
                }
                // Seq가 앞줄보다 커지지 않는 줄(겹침·뒤섞임)도 끊는다. 건너뛴 것(빈칸)은 둔다 — 로그 앞쪽이 잘린 세이브는 스냅샷 뒤 줄만 이어지면 읽힌다(Load)
                if (line?.Command == null || line.Seq >= commandCount || line.Seq <= last) break;
                last = line.Seq;
                keep = nl + 1;
            }
            if (keep < text.Length) AtomicWrite(path, text.Substring(0, keep));
            logRepaired = true;
            // 올림 표시(PlayRecord.MarkFile)는 그대로 둔다: 표시의 지문이 잘린 로그와 달라져 다음 올리기가 이어진 갈래로 서버의 버린 갈래를 덮어쓴다(undo.md §4)
        }

        /// <summary>끝이 줄바꿈이 아니면(쓰다 끊긴 줄) 마지막 줄바꿈 뒤를 잘라 낸다 — 다음 줄이 그 조각에 붙지 않게.</summary>
        static void RepairTail(string path)
        {
            if (!File.Exists(path)) return;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                long n = fs.Length;
                if (n == 0) return;
                fs.Position = n - 1;
                if (fs.ReadByte() == '\n') return;
                long keep = 0;
                for (long p = n - 2; p >= 0; p--)
                {
                    fs.Position = p;
                    if (fs.ReadByte() == '\n')
                    {
                        keep = p + 1;
                        break;
                    }
                }
                fs.SetLength(keep);
                fs.Flush(true);
            }
        }

        /// <summary>
        /// 이어하기용으로 읽는다(02 §7): 머리 + 가장 새 성한 스냅샷 + 그 뒤 로그 줄. 깨진 스냅샷은 건너뛰고 앞 것을 쓴다. 로그는 처음 깨진 줄(쓰다 끊긴 마지막 줄 포함)에서
        /// 멈춘다. 스냅샷 뒤 줄의 Seq가 이어지지 않으면 그 앞까지만 쓴다. 고친 것은 Warnings에 남는다.
        /// </summary>
        public static SaveRecord Load(string directory)
        {
            var rec = new SaveRecord();
            string headText = File.ReadAllText(Path.Combine(directory, HeadFile), Utf8);
            int format = FormatOf(headText);
            if (format != SaveRecord.CurrentFormat) throw new NotSupportedException($"세이브 형식 {format}은 이 빌드({SaveRecord.CurrentFormat})가 열지 못한다");
            rec.Head = Deserialize<SaveHead>(headText);
            if (rec.Head == null) throw new InvalidDataException($"{directory}: 머리가 비었다");
            if (rec.Head.RulesVersion != SaveRecord.CurrentRulesVersion)
                rec.Warnings.Add($"규칙 판이 다르다(세이브 {rec.Head.RulesVersion} / 지금 {SaveRecord.CurrentRulesVersion}) — 스냅샷에서 이어 간다");
            foreach (var f in Snapshots(directory))
            {
                try
                {
                    var snap = Deserialize<SaveSnapshot>(File.ReadAllText(f.Path, Utf8));
                    if (snap?.State == null || snap.CommandCount != f.Number || snap.State.CommandCount != f.Number)
                        throw new InvalidDataException("스냅샷 내용이 파일 이름과 맞지 않는다");
                    rec.Snapshot = snap;
                    break;
                }
                catch (Exception e) when (e is JsonException || e is InvalidDataException || e is IOException)
                {
                    rec.Warnings.Add($"깨진 스냅샷 {Path.GetFileName(f.Path)}을 건너뜀: {e.Message}");
                }
            }
            if (rec.Snapshot == null) throw new InvalidDataException($"{directory}: 성한 스냅샷이 없다");

            int from = rec.Snapshot.CommandCount;
            foreach (var line in ReadLog(directory, rec.Warnings))
            {
                if (line.Seq < from) continue;
                int want = from + rec.Commands.Count;
                if (line.Seq != want)
                {
                    rec.Warnings.Add($"세이브 로그가 이어지지 않아(Seq {line.Seq}, 기대 {want}) 그 앞까지만 씀");
                    break;
                }
                rec.Commands.Add(line);
            }
            return rec;
        }

        // ── 저장 목록(04 S04) ──

        /// <summary>
        /// 저장 폴더(root — 그 아래 폴더 하나가 저장 하나) 안의 저장을 머리만 읽어 늘어놓는다(스냅샷·로그는 풀지 않는다). 읽을 수 있는 것을 저장 시각이 새것부터, 그 뒤에 읽을 수 없는 것을 이름 순서로.
        /// 깨진 머리·다른 형식 판·스냅샷 없는 폴더는 <see cref="SaveEntry.Problem"/>에 까닭을 달아 돌려준다 — 던지지 않는다. 폴더가 없으면 빈 목록.
        /// </summary>
        public static List<SaveEntry> List(string root)
        {
            var r = new List<SaveEntry>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return r;
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(root);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return r;
            }
            foreach (var d in dirs) r.Add(Peek(d));
            Sort(r);
            return r;
        }

        /// <summary>목록 순서: 읽을 수 있는 것을 저장 시각이 새것부터(같으면 이름), 그 뒤에 읽을 수 없는 것을 이름 순서로.</summary>
        public static void Sort(List<SaveEntry> list)
        {
            list.Sort((a, b) =>
            {
                if (a.Readable != b.Readable) return a.Readable ? -1 : 1;
                if (a.Readable && a.Head.Summary.SavedAtMs != b.Head.Summary.SavedAtMs) return b.Head.Summary.SavedAtMs.CompareTo(a.Head.Summary.SavedAtMs);
                return string.CompareOrdinal(a.SaveId, b.SaveId);
            });
        }

        /// <summary>저장 하나의 머리만 읽는다(목록 한 줄). 읽지 못하면 Problem에 까닭 — 던지지 않는다.</summary>
        public static SaveEntry Peek(string directory)
        {
            var entry = new SaveEntry { SaveId = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), Directory = directory };
            try
            {
                string text = File.ReadAllText(Path.Combine(directory, HeadFile), Utf8);
                // 형식 판부터 본다: 다른 판의 머리는 필드가 달라 엄격하게 읽으면 「깨짐」으로 보인다
                int format = FormatOf(text);
                if (format != SaveRecord.CurrentFormat)
                {
                    entry.Fail(format < SaveRecord.CurrentFormat ? SaveProblem.OldFormat : SaveProblem.NewFormat, $"형식 {format}(이 빌드 {SaveRecord.CurrentFormat})");
                    return entry;
                }
                var head = Deserialize<SaveHead>(text);
                if (head == null || head.Summary == null || head.Options == null) entry.Fail(SaveProblem.Broken, "머리가 비었다");
                else if (Snapshots(directory).Count == 0) entry.Fail(SaveProblem.Broken, "스냅샷이 없다");
                else entry.Head = head;
            }
            catch (Exception e)
            {
                // 남이 고쳤거나 쓰다 끊긴 파일이다: 어떤 꼴로 깨졌든(JSON·값 범위·권한) 목록은 그 줄만 「읽을 수 없음」으로 둔다
                entry.Fail(SaveProblem.Broken, e.Message);
            }
            return entry;
        }

        /// <summary>
        /// 머리 글에서 형식 판만 느슨하게 읽는다(다른 필드는 보지 않는다 — 형식이 다르면 필드도 다르다). 머리가 객체가 아니거나 FormatVersion이 없으면·정수가 아니면
        /// 지금 판으로 친다(엄격한 읽기가 까닭을 가린다). JSON이 깨졌으면 JsonException.
        /// </summary>
        static int FormatOf(string headJson)
        {
            JToken v;
            using (var r = new JsonTextReader(new StringReader(headJson)))
                v = (JToken.ReadFrom(r) as JObject)?["FormatVersion"];
            return v != null && v.Type == JTokenType.Integer ? (int)v : SaveRecord.CurrentFormat;
        }

        /// <summary>저장 하나를 지운다(그 폴더째). 이름에 경로 조각이 섞였으면 ArgumentException. 없으면 false.</summary>
        public static bool Delete(string root, string saveId)
        {
            string d = PathOf(root, saveId);
            if (!Directory.Exists(d)) return false;
            Directory.Delete(d, true);
            return true;
        }

        /// <summary>저장 폴더 안 그 저장의 자리. 이름은 폴더 이름 하나여야 한다(경로 조각이 섞이면 ArgumentException).</summary>
        public static string PathOf(string root, string saveId)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("저장 폴더가 없다", nameof(root));
            if (string.IsNullOrEmpty(saveId) || saveId == "." || saveId == ".." || saveId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || saveId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"저장 이름이 폴더 이름이 아니다: {saveId}", nameof(saveId));
            return Path.Combine(root, saveId);
        }

        /// <summary>log.jsonl 전체(리플레이 = 시드와 첫 상태에서 전부 다시 적용, 01 §10). 처음 깨진 줄에서 멈추고 warnings에 적는다.</summary>
        public static List<SaveLogLine> ReadLog(string directory, List<string> warnings = null)
        {
            var r = new List<SaveLogLine>();
            string path = Path.Combine(directory, LogFile);
            if (!File.Exists(path)) return r;
            string text = File.ReadAllText(path, Utf8);
            int start = 0, lineNo = 0;
            while (start < text.Length)
            {
                int nl = text.IndexOf('\n', start);
                bool complete = nl >= 0;
                string s = complete ? text.Substring(start, nl - start) : text.Substring(start);
                lineNo++;
                try
                {
                    var line = Deserialize<SaveLogLine>(s);
                    if (line?.Command == null) throw new InvalidDataException("명령이 없는 줄");
                    if (!complete) throw new InvalidDataException("줄바꿈 없이 끝남");
                    r.Add(line);
                }
                catch (Exception e) when (e is JsonException || e is InvalidDataException)
                {
                    warnings?.Add(complete ? $"로그 {lineNo}번째 줄이 깨져 그 뒤를 버림: {e.Message}" : $"로그 마지막 줄이 잘려 버림({lineNo}번째)");
                    break;
                }
                if (!complete) break;
                start = nl + 1;
            }
            return r;
        }

        struct SnapFile
        {
            public int Number;
            public string Path;
        }

        /// <summary>snap-&lt;n&gt;.json을 n이 큰 것부터.</summary>
        static List<SnapFile> Snapshots(string directory)
        {
            var r = new List<SnapFile>();
            if (!Directory.Exists(directory)) return r;
            foreach (var p in Directory.GetFiles(directory, SnapPrefix + "*" + SnapSuffix))
            {
                string name = Path.GetFileName(p);
                string num = name.Substring(SnapPrefix.Length, name.Length - SnapPrefix.Length - SnapSuffix.Length);
                if (int.TryParse(num, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
                    r.Add(new SnapFile { Number = n, Path = p });
            }
            r.Sort((a, b) => b.Number.CompareTo(a.Number));
            return r;
        }

        static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp";
            var bytes = Utf8.GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static string Serialize(object value, bool indented) =>
            JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>public 인스턴스 필드만 쓰고 읽는다(속성·static 제외). 상태 클래스는 필드뿐이고 기본값을 필드 초기값으로 둔다.</summary>
        sealed class FieldsOnly : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var r = new List<MemberInfo>();
                foreach (var f in objectType.GetFields(BindingFlags.Public | BindingFlags.Instance)) r.Add(f);
                return r;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var p = base.CreateProperty(member, memberSerialization);
                p.Readable = true;
                p.Writable = member is FieldInfo f && !f.IsInitOnly;
                return p;
            }
        }
    }
}
