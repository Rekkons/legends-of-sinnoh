using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// build.mjs 표 파일 {"schema":1,"rows":[…]}을 엄격하게 읽고 같은 바이트로 쓰는 공통 부분(BoardLoader·CardDbLoader·ItemDbLoader·
    /// FixtureDbLoader·DeckDbLoader). 읽기: 빠진 필드·모르는 필드·같은 필드 두 번·형이 다른 값(문자열 "6"을 정수로, 5를 문자열로 바꾸지 않는다)은
    /// FormatException(파일 이름이 붙음), 수는 double로 읽지 않는다(01 §0).
    /// 쓰기: 행 하나 = 한 줄, 행은 JSON.stringify와 같은 꼴(공백 없음, ASCII 밖 글자는 그대로).
    /// </summary>
    static class TableJson
    {
        public const int Schema = 1;

        /// <summary>"&lt;종류&gt;.&lt;snake&gt;"(03 §1-2 카드·고정물·아이템·덱 키).</summary>
        public static readonly Regex KindKey = new Regex(@"^([a-z0-9]+(?:_[a-z0-9]+)*)\.[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        public static string ReadAll(Stream stream, string what)
        {
            if (stream == null) throw new ArgumentNullException(what);
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true, 4096, true))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// 먼저 JToken으로 읽어(같은 필드 두 번 = 오류, 날짜 해석 안 함) 값 토큰 형을 행 클래스 필드 형과 맞춰 본 뒤(CheckTokens) 행 클래스로 바꾼다.
        /// Newtonsoft 역직렬화만 쓰면 뒤 값이 이기고 "6" → int 같은 형 변환을 받아 버린다.
        /// </summary>
        public static List<T> Parse<T>(string json, string file)
        {
            if (json == null) throw new ArgumentNullException(file);
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                FloatParseHandling = FloatParseHandling.Decimal,
            };
            TableFile<T> table;
            try
            {
                var root = LoadToken(json, file);
                CheckTokens(root, typeof(TableFile<T>), file, "$");
                table = root.ToObject<TableFile<T>>(JsonSerializer.Create(settings));
            }
            catch (JsonException e) { throw new FormatException($"{file}: {e.Message}", e); }
            if (table == null) throw new FormatException($"{file}이 비었다");
            if (table.Schema != Schema) throw new FormatException($"{file} schema {table.Schema}, 기대 {Schema}");
            for (int i = 0; i < table.Rows.Count; i++)
                if (table.Rows[i] == null) throw new FormatException($"{file} 행 {i}이 null이다");
            return table.Rows;
        }

        static readonly JsonLoadSettings StrictLoad = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };

        /// <summary>JSON 하나를 엄격하게 읽는다: 같은 필드 두 번은 오류, 수는 decimal, 날짜처럼 보이는 문자열도 문자열, 뒤에 남은 글자는 오류.</summary>
        public static JToken LoadToken(string json, string file)
        {
            using (var r = new JsonTextReader(new StringReader(json)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None })
            {
                var root = JToken.ReadFrom(r, StrictLoad);
                if (r.Read()) throw new FormatException($"{file}: JSON 뒤에 글자가 남았다");
                return root;
            }
        }

        /// <summary>
        /// 값 토큰 형이 필드 형과 같은지 본다: string = 문자열, int = 정수, bool = 참거짓, decimal = 수, List = 배열(항목마다), 행 클래스 = 객체(필드마다).
        /// null 허용 여부는 역직렬화의 Required(Always·AllowNull)가, 모르는 필드는 MissingMemberHandling이 본다.
        /// </summary>
        static void CheckTokens(JToken t, Type type, string file, string path)
        {
            if (t.Type == JTokenType.Null) return;
            var u = Nullable.GetUnderlyingType(type) ?? type;
            JTokenType want;
            if (u == typeof(string)) want = JTokenType.String;
            else if (u == typeof(int)) want = JTokenType.Integer;
            else if (u == typeof(bool)) want = JTokenType.Boolean;
            else if (u == typeof(decimal))
            {
                if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return;
                want = JTokenType.Float;
            }
            else if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (t.Type != JTokenType.Array) throw new FormatException($"{file} {path}: 배열이어야 하는데 {t.Type}");
                var item = u.GetGenericArguments()[0];
                int i = 0;
                foreach (var x in (JArray)t) CheckTokens(x, item, file, $"{path}[{i++}]");
                return;
            }
            else
            {
                if (t.Type != JTokenType.Object) throw new FormatException($"{file} {path}: 객체여야 하는데 {t.Type}");
                foreach (var p in ((JObject)t).Properties())
                {
                    // Newtonsoft처럼 필드 이름을 대소문자 무시로 찾는다(JSON key ↔ 필드 Key)
                    var f = u.GetField(p.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (f != null) CheckTokens(p.Value, f.FieldType, file, path + "." + p.Name);
                }
                return;
            }
            if (t.Type != want) throw new FormatException($"{file} {path}: {want}이어야 하는데 {t.Type}");
        }

        /// <summary>Rules 생성자의 ArgumentException(행 검사)을 파일 이름이 붙은 FormatException으로 바꾼다.</summary>
        public static T Wrap<T>(string file, Func<T> f)
        {
            try { return f(); }
            catch (ArgumentException e) { throw new FormatException($"{file}: {e.Message}", e); }
        }

        /// <summary>enum 이름과 정확히 같아야 한다(숫자·쉼표 조합·대소문자 다름 거부).</summary>
        public static T ParseEnum<T>(string value, string where) where T : struct, Enum
        {
            if (value != null && Enum.TryParse(value, false, out T parsed) && parsed.ToString() == value) return parsed;
            throw new FormatException($"{where}: 모르는 {typeof(T).Name} 값 \"{value ?? "null"}\"");
        }

        /// <summary>키 형식과 종류 접두("item."·"village." 등)를 확인한다.</summary>
        public static void CheckKey(string key, string prefix, string file, int row)
        {
            var m = key == null ? null : KindKey.Match(key);
            if (m == null || !m.Success || m.Groups[1].Value != prefix)
                throw new FormatException($"{file} 행 {row}: 키 \"{key}\"가 \"{prefix}.<snake>\" 꼴이 아니다");
        }

        public static int Index(IdTable ids, string key, string where)
        {
            if (ids.TryIndexOf(key, out int id)) return id;
            throw new FormatException($"{where}: {ids.Name}에 키 \"{key}\"가 없다");
        }

        /// <summary>ApricornTree → apricorn_tree(키 접두).</summary>
        public static string Snake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (c >= 'A' && c <= 'Z')
                {
                    if (i > 0) sb.Append('_');
                    sb.Append((char)(c - 'A' + 'a'));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        // ── 쓰기 ──

        /// <summary>build.mjs table(): {"schema":1,"rows":[\n행,\n행\n]}\n.</summary>
        public static string WriteTable<T>(IReadOnlyList<T> rows, Action<JsonWriter, T> writeRow)
        {
            var sb = new StringBuilder();
            sb.Append("{\"schema\":").Append(Num(Schema)).Append(",\"rows\":[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                using (var sw = new StringWriter(sb, CultureInfo.InvariantCulture))
                using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None, CloseOutput = false })
                    writeRow(w, rows[i]);
            }
            sb.Append("\n]}\n");
            return sb.ToString();
        }

        public static void Prop(JsonWriter w, string name, string value) { w.WritePropertyName(name); w.WriteValue(value); }
        public static void Prop(JsonWriter w, string name, int value) { w.WritePropertyName(name); w.WriteValue(value); }
        public static void Prop(JsonWriter w, string name, int? value) { w.WritePropertyName(name); w.WriteValue(value); }
        public static void Prop(JsonWriter w, string name, bool value) { w.WritePropertyName(name); w.WriteValue(value); }

        /// <summary>decimal을 읽은 글자 그대로(JS 수 표기, 1.0이 아니라 1) 쓴다. 읽을 때 decimal이 자릿수를 지켜 둔다.</summary>
        public static void Prop(JsonWriter w, string name, decimal value)
        {
            w.WritePropertyName(name);
            w.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void Strings(JsonWriter w, string name, IEnumerable<string> values)
        {
            w.WritePropertyName(name);
            w.WriteStartArray();
            foreach (var v in values) w.WriteValue(v);
            w.WriteEndArray();
        }

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class TableFile<T>
        {
            public int Schema;
            public List<T> Rows;
        }
#pragma warning restore 0649
    }
}
