using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>이름 붙은 메시지 하나의 본문(02 §4-1): V = 프로토콜 판, Seq = 보낸 쪽 일련번호, Type = 메시지 클래스 이름, Body = 메시지 객체.</summary>
    public sealed class Envelope
    {
        public int V;
        public int Seq;
        public string Type;
        public object Body;
    }

    /// <summary>
    /// 전선 위 메시지 ↔ JSON(02 §4-1, isekai Net/MessageCodec을 고쳐 옮김). 엔진 무관이라 Data에 둔다 — dotnet·EditMode가 바이트 왕복을 시험하고, Net(NGO)은 이 바이트만 나른다 [가정 M7-28].
    /// 손님 → 호스트 = <see cref="RoomMsg"/>, 호스트 → 손님 = <see cref="ServerMsg"/>(한 프로세스 꼴 <see cref="GameUpdate"/>는 보내지 않는다 — 방은 스냅샷·델타로 바꿔 보낸다).
    /// 공개 인스턴스 필드만 싣는다(Rules 메시지·상태 클래스는 필드뿐). 다형 값(<see cref="GameEvent"/>)은 TypeNameHandling 없이 맨 앞 <c>"type"</c> 필드(클래스 이름)로 고른다.
    /// 받는 쪽은 바이트를 믿지 않는다: 판이 다르면 E_VERSION, 모양이 틀리면(깨진 JSON · 모르는 Type·type · 없는 필드) E_FORMAT — 받은 쪽은 그 연결을 끊는다.
    /// 줄이기 [가정 M7-29]: <see cref="ChoiceRequested.Legal"/>은 싣지 않는다(본인 몫도 — 손님은 보기로 <c>Engine.Legal</c>을 센다, SeatClient.Legal).
    /// </summary>
    public static class MessageCodec
    {
        /// <summary>읽을 수 없는 메시지(깨진 JSON · 모르는 Type · 모양이 틀림). 받은 쪽은 그 연결을 끊는다(02 §4-1).</summary>
        public const string BadFormat = "E_FORMAT";

        /// <summary>스냅샷·델타가 아닌 메시지의 크기 경고(isekai 64 KB).</summary>
        public const int WarnBytes = 64 * 1024;

        static readonly Dictionary<string, Type> roomTypes = Subtypes(typeof(RoomMsg));
        static readonly Dictionary<string, Type> serverTypes = Wire(Subtypes(typeof(ServerMsg)));

        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new WireFields(),
            Converters = { new TypeField(typeof(GameEvent)) },
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Error,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DateParseHandling = DateParseHandling.None,
            Formatting = Formatting.None,
            Culture = CultureInfo.InvariantCulture,
        };

        static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static IEnumerable<Type> RoomTypes => roomTypes.Values;
        public static IEnumerable<Type> ServerTypes => serverTypes.Values;

        // ── 보내기 ──

        /// <summary>손님 → 호스트.</summary>
        public static string Encode(RoomMsg msg, int seq) => Write(msg, seq, roomTypes);

        /// <summary>호스트 → 손님.</summary>
        public static string Encode(ServerMsg msg, int seq = 0) => Write(msg, seq, serverTypes);

        /// <summary>접속 인사(NGO 승인 데이터, 02 §4-6). 판·데이터 비교는 HostSession.Approve가 한다.</summary>
        public static string EncodeHello(Hello hello)
        {
            if (hello == null) throw new ArgumentNullException(nameof(hello));
            return JsonConvert.SerializeObject(hello, Settings);
        }

        // ── 받기 — error = 「코드|까닭」 ──

        public static bool TryDecodeRoom(string json, out RoomMsg msg, out int seq, out string error)
        {
            msg = null;
            seq = 0;
            if (!TryRead(json, roomTypes, out var body, out var env, out error)) return false;
            msg = (RoomMsg)body;
            seq = env.Seq;
            return true;
        }

        public static bool TryDecodeServer(string json, out ServerMsg msg, out string error)
        {
            msg = null;
            if (!TryRead(json, serverTypes, out var body, out _, out error)) return false;
            msg = (ServerMsg)body;
            return true;
        }

        public static bool TryDecodeHello(string json, out Hello hello, out string error)
        {
            hello = null;
            error = null;
            try
            {
                hello = JsonConvert.DeserializeObject<Hello>(json ?? "", Settings);
            }
            catch (Exception e)
            {
                error = BadFormat + "|" + e.Message;
                return false;
            }
            if (hello != null) return true;
            error = BadFormat + "|빈 Hello";
            return false;
        }

        /// <summary>
        /// 보낼 바이트 수가 경고 크기 이상이면 경고 문구, 아니면 null(02 §4-7 [가정 M7-16]): 스냅샷 <see cref="StreamBudget.SnapshotWarnBytes"/> · 델타 <see cref="StreamBudget.DeltaWarnBytes"/> ·
        /// 그 밖 <see cref="WarnBytes"/>. 실제 바이트를 아는 Net(NetHostLink)이 보낼 때 부르고 로그에 남긴다.
        /// </summary>
        public static string SizeWarning(ServerMsg msg, int bytes)
        {
            int limit = msg is GameSnapshot ? StreamBudget.SnapshotWarnBytes : msg is GameDelta ? StreamBudget.DeltaWarnBytes : WarnBytes;
            return bytes >= limit ? $"[Net] {msg?.GetType().Name} {bytes} B — 경고 크기 {limit} B 이상(02 §4-7)" : null;
        }

        // ── 안 ──

        static string Write(object msg, int seq, Dictionary<string, Type> known)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            var type = msg.GetType();
            if (!known.TryGetValue(type.Name, out var t) || t != type) throw new ArgumentException("보낼 수 없는 메시지 " + type.FullName, nameof(msg));
            var env = new Envelope { V = Protocol.Version, Seq = seq, Type = type.Name, Body = msg };
            var sw = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var w = new JsonTextWriter(sw)) Serializer.Serialize(w, env);
            return sw.ToString();
        }

        // 봉투를 앞에서부터 읽는다. 보내는 쪽은 늘 V · Seq · Type · Body 순서라 Body에 닿을 때 형식을 안다 — 순서가 다르면(손으로 쓴 JSON) Body를 글 그대로 받아 두었다가 끝에서 읽는다.
        // 오류 판정 순서: 깨진 JSON → 판 → Type → 본문
        static bool TryRead(string json, Dictionary<string, Type> known, out object msg, out Envelope env, out string error)
        {
            msg = null;
            env = null;
            error = null;
            var e = new Envelope();
            string late = null;
            try
            {
                using (var r = new JsonTextReader(new StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None })
                {
                    if (!r.Read() || r.TokenType == JsonToken.Null)
                    {
                        error = BadFormat + "|빈 메시지";
                        return false;
                    }
                    if (r.TokenType != JsonToken.StartObject) throw new JsonSerializationException("봉투가 객체가 아니다: " + r.TokenType);
                    while (true)
                    {
                        if (!r.Read()) throw new JsonSerializationException("봉투가 끝나지 않았다");
                        if (r.TokenType == JsonToken.EndObject) break;
                        if (r.TokenType != JsonToken.PropertyName) throw new JsonSerializationException("봉투 속성이 아니다: " + r.TokenType);
                        var name = (string)r.Value;
                        if (name == "V") e.V = Int(r, name);
                        else if (name == "Seq") e.Seq = Int(r, name);
                        else if (name == "Type") e.Type = r.ReadAsString();
                        else if (name == "Body")
                        {
                            if (!r.Read()) throw new JsonSerializationException("Body 값 없음");
                            msg = null;
                            late = null;
                            if (r.TokenType == JsonToken.Null) continue;
                            if (r.TokenType != JsonToken.StartObject) throw new JsonSerializationException("Body가 객체가 아니다: " + r.TokenType);
                            if (e.V == Protocol.Version && e.Type != null && known.TryGetValue(e.Type, out var early))
                            {
                                try
                                {
                                    msg = Serializer.Deserialize(r, early);
                                }
                                catch (Exception x)
                                {
                                    error = $"{BadFormat}|{e.Type}: {x.Message}";
                                    return false;
                                }
                            }
                            else late = (string)JRaw.Create(r).Value;
                        }
                        else throw new JsonSerializationException("봉투에 모르는 속성 " + name);
                    }
                    if (r.Read()) throw new JsonSerializationException("봉투 뒤에 남은 내용: " + r.TokenType);
                }
            }
            catch (Exception x)
            {
                msg = null;
                error = BadFormat + "|" + x.Message;
                return false;
            }
            if (e.V != Protocol.Version)
            {
                msg = null;
                error = $"{NetCode.Version}|{Protocol.Version}/{e.V}";
                return false;
            }
            if (e.Type == null || !known.TryGetValue(e.Type, out var type))
            {
                msg = null;
                error = $"{BadFormat}|모르는 Type {e.Type}";
                return false;
            }
            if (msg == null && late != null)
            {
                try
                {
                    using (var lr = new JsonTextReader(new StringReader(late)) { DateParseHandling = DateParseHandling.None }) msg = Serializer.Deserialize(lr, type);
                }
                catch (Exception x)
                {
                    msg = null;
                    error = $"{BadFormat}|{e.Type}: {x.Message}";
                    return false;
                }
            }
            if (msg == null || msg.GetType() != type)
            {
                msg = null;
                error = $"{BadFormat}|{e.Type} 본문 없음";
                return false;
            }
            e.Body = msg;
            env = e;
            return true;
        }

        static int Int(JsonReader r, string name) => r.ReadAsInt32() ?? throw new JsonSerializationException(name + " 값이 null");

        // root를 상속한 Rules의 공개 클래스(이름 → 형식). 같은 이름이 둘이면 여기서 터진다
        static Dictionary<string, Type> Subtypes(Type root)
        {
            var map = new Dictionary<string, Type>();
            foreach (var t in root.Assembly.GetTypes())
                if (t.IsClass && !t.IsAbstract && t.IsPublic && root.IsAssignableFrom(t)) map.Add(t.Name, t);
            return map;
        }

        // 전선에 싣지 않는 호스트 메시지: 한 프로세스 꼴 GameUpdate(보기 사본 전체 — 방은 스냅샷·델타로 바꿔 보낸다)
        static Dictionary<string, Type> Wire(Dictionary<string, Type> all)
        {
            all.Remove(nameof(GameUpdate));
            return all;
        }

        /// <summary>공개 인스턴스 필드만(속성·static 제외 — SaveStore와 같은 꼴). <see cref="ChoiceRequested.Legal"/>은 싣지 않는다 [가정 M7-29].</summary>
        sealed class WireFields : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var r = new List<MemberInfo>();
                foreach (var f in objectType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!(f.DeclaringType == typeof(ChoiceRequested) && f.Name == nameof(ChoiceRequested.Legal))) r.Add(f);
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

        /// <summary>다형 값: <c>{"type":"클래스 이름", 필드…}</c>. root의 하위 클래스면 선언 형식과 관계없이 이것을 탄다. 모르는 type · type 두 번이면 E_FORMAT.</summary>
        sealed class TypeField : JsonConverter
        {
            const string Field = "type";
            static readonly JsonLoadSettings Strict = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
            readonly Type root;
            readonly Dictionary<string, Type> byName;

            public TypeField(Type root)
            {
                this.root = root;
                byName = Subtypes(root);
            }

            public override bool CanConvert(Type objectType) => root.IsAssignableFrom(objectType);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }
                var type = value.GetType();
                if (!byName.TryGetValue(type.Name, out var known) || known != type) throw new JsonSerializationException($"{root.Name}: 보낼 수 없는 형식 {type.FullName}");
                var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(type);
                writer.WriteStartObject();
                writer.WritePropertyName(Field);
                writer.WriteValue(type.Name);
                foreach (var p in contract.Properties)
                {
                    if (p.Ignored || !p.Readable) continue;
                    writer.WritePropertyName(p.PropertyName);
                    serializer.Serialize(writer, p.ValueProvider.GetValue(value));
                }
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                if (reader.TokenType != JsonToken.StartObject) throw new JsonSerializationException($"{root.Name}: 객체가 아니다 {reader.TokenType}");
                var o = JObject.Load(reader, Strict);
                var name = o[Field]?.Type == JTokenType.String ? (string)o[Field] : null;
                if (name == null || !byName.TryGetValue(name, out var type) || !objectType.IsAssignableFrom(type))
                    throw new JsonSerializationException($"{root.Name}: 모르는 type '{name}'");
                o.Remove(Field);
                var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(type);
                var value = contract.DefaultCreator();
                using (var r = o.CreateReader()) serializer.Populate(r, value);
                return value;
            }
        }
    }
}
