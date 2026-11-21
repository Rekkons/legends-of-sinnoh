using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sinnoh.Data
{
    /// <summary>
    /// strings.ko·strings.en.json(Tools/data/build.mjs가 씀, 03 §1-3·§10): 평평한 {키: 문장}. 키는 ff1ce0 묶음(ui.&lt;묶음&gt;.&lt;키&gt;)과
    /// 데이터 키(species.·pokemon.·move.·trainer_card.·galactic.·region.·고정물 village.·legendary_site.·legendary_shrine.·shrine.·gate.·아이템 item.·hidden_spot.)다. 엔진은 읽지 않는다(01 §11) — 화면과 시험이 읽는다.
    /// 경로는 받지 않는다. 엄격하다: 필드는 schema·lang·strings만, schema 1, lang ko·en, 값은 빈 문자열이 아닌 문자열,
    /// 키는 서수(UTF-16) 오름차순이고 겹치지 않는다 — 어기면 FormatException.
    /// Write는 build.mjs와 같은 바이트를 쓴다(키 하나 = 한 줄) — 읽고 다시 쓰면 같은 파일이다(plan M2 조건 3).
    /// </summary>
    public sealed class StringTable
    {
        public const int Schema = 1;
        static readonly string[] Langs = { "en", "ko" };

        public readonly string Lang;
        readonly string[] keys;   // 서수 오름차순(이진 탐색)
        readonly string[] values;

        StringTable(string lang, string[] keys, string[] values)
        {
            Lang = lang;
            this.keys = keys;
            this.values = values;
        }

        public int Count => keys.Length;

        /// <summary>서수 오름차순.</summary>
        public IReadOnlyList<string> Keys => keys;

        public bool Contains(string key) => IndexOf(key) >= 0;

        public bool TryGet(string key, out string text)
        {
            int i = IndexOf(key);
            text = i >= 0 ? values[i] : null;
            return i >= 0;
        }

        public string this[string key] =>
            TryGet(key, out var text) ? text : throw new KeyNotFoundException($"strings.{Lang}에 키 {key}가 없다");

        int IndexOf(string key) => key == null ? -1 : Math.Max(-1, Array.BinarySearch(keys, key, StringComparer.Ordinal));

        public static StringTable Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true, 4096, true))
                return Load(reader.ReadToEnd());
        }

        public static StringTable Load(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JObject root;
            try
            {
                using (var r = new JsonTextReader(new StringReader(json)) { FloatParseHandling = FloatParseHandling.Decimal })
                    root = JObject.Load(r, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException e) { throw new FormatException($"strings: {e.Message}", e); }

            foreach (var p in root.Properties())
                if (p.Name != "schema" && p.Name != "lang" && p.Name != "strings") throw new FormatException($"strings: 모르는 필드 {p.Name}");
            if (root["schema"]?.Type != JTokenType.Integer || (int)root["schema"] != Schema) throw new FormatException($"strings: schema 기대 {Schema}");
            string lang = root["lang"]?.Type == JTokenType.String ? (string)root["lang"] : null;
            if (Array.IndexOf(Langs, lang) < 0) throw new FormatException($"strings: 모르는 lang {lang ?? "(없음)"}");
            var obj = root["strings"] as JObject ?? throw new FormatException($"strings.{lang}: strings가 객체가 아니다");

            var keys = new List<string>();
            var values = new List<string>();
            foreach (var p in obj.Properties())
            {
                if (p.Name.Length == 0 || HasSpaceOrControl(p.Name)) throw new FormatException($"strings.{lang}: 키 \"{p.Name}\" 형식");
                if (keys.Count > 0 && string.CompareOrdinal(keys[keys.Count - 1], p.Name) >= 0)
                    throw new FormatException($"strings.{lang}: 키 {p.Name}이 서수 순서가 아니다(앞 키 {keys[keys.Count - 1]})");
                if (p.Value.Type != JTokenType.String || ((string)p.Value).Length == 0) throw new FormatException($"strings.{lang} {p.Name}: 값이 빈 문자열이 아닌 문자열이 아니다");
                keys.Add(p.Name);
                values.Add((string)p.Value);
            }
            return new StringTable(lang, keys.ToArray(), values.ToArray());
        }

        static bool HasSpaceOrControl(string s)
        {
            foreach (char c in s)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return true;
            return false;
        }

        /// <summary>build.mjs stringsFile(): {"schema":1,"lang":"ko","strings":{\n"키":"문장",\n…\n}}\n</summary>
        public string Write()
        {
            var sb = new StringBuilder();
            sb.Append("{\"schema\":").Append(Schema.ToString(CultureInfo.InvariantCulture)).Append(",\"lang\":").Append(JsonConvert.ToString(Lang)).Append(",\"strings\":{\n");
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append(JsonConvert.ToString(keys[i])).Append(':').Append(JsonConvert.ToString(values[i]));
            }
            sb.Append("\n}}\n");
            return sb.ToString();
        }
    }
}
