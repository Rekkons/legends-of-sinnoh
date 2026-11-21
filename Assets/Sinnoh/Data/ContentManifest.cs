using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sinnoh.Data
{
    /// <summary>
    /// manifest.json(build.mjs가 씀, 03 §1-2): 표마다 파일·행 수·sha256, 엔진이 읽는 표 목록(contentTables)과 그 묶음 해시
    /// contentHash(세이브 ContentHash, 01 §10), 입력 파일 sha1. 경로는 받지 않는다. 엄격하다: 모르는 필드·빠진 필드·같은 필드 두 번은 FormatException.
    /// </summary>
    public sealed class ContentManifest
    {
        public const int Schema = 1;

        public sealed class Table
        {
            public readonly string Name;
            public readonly string File;
            public readonly int Rows;
            public readonly string Sha256;

            public Table(string name, string file, int rows, string sha256)
            {
                Name = name;
                File = file;
                Rows = rows;
                Sha256 = sha256;
            }
        }

        /// <summary>manifest에 적힌 순서(이름순).</summary>
        public readonly IReadOnlyList<Table> Tables;
        /// <summary>contentHash에 묶는 표 이름(이름순).</summary>
        public readonly IReadOnlyList<string> ContentTables;
        public readonly string ContentHash;

        ContentManifest(Table[] tables, string[] contentTables, string contentHash)
        {
            Tables = tables;
            ContentTables = contentTables;
            ContentHash = contentHash;
        }

        public Table Find(string name)
        {
            foreach (var t in Tables)
                if (t.Name == name) return t;
            throw new KeyNotFoundException($"manifest에 표 {name}이 없다");
        }

        /// <summary>적힌 표 sha256으로 contentHash를 다시 계산한다(기록값과 비교하는 데 쓴다).</summary>
        public string ComputeContentHash()
        {
            var shas = new string[ContentTables.Count];
            for (int i = 0; i < shas.Length; i++) shas[i] = Find(ContentTables[i]).Sha256;
            return ComputeContentHash(ContentTables, shas);
        }

        /// <summary>
        /// contentHash = 표 이름순(서수)으로 "이름\tsha256\n"을 이은 UTF-8의 sha256(소문자 16진). build.mjs와 같은 식이다.
        /// names와 sha256s는 같은 순서·같은 길이.
        /// </summary>
        public static string ComputeContentHash(IReadOnlyList<string> names, IReadOnlyList<string> sha256s)
        {
            if (names.Count != sha256s.Count) throw new ArgumentException("이름과 해시 수가 다르다");
            var order = new int[names.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => string.CompareOrdinal(names[a], names[b]));
            var sb = new StringBuilder();
            foreach (int i in order) sb.Append(names[i]).Append('\t').Append(sha256s[i]).Append('\n');
            return Sha256Hex(new UTF8Encoding(false).GetBytes(sb.ToString()));
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append("0123456789abcdef"[b >> 4]).Append("0123456789abcdef"[b & 15]);
                return sb.ToString();
            }
        }

        public static ContentManifest Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true, 4096, true))
                return Load(reader.ReadToEnd());
        }

        public static ContentManifest Load(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JObject root;
            try
            {
                using (var r = new JsonTextReader(new StringReader(json)) { FloatParseHandling = FloatParseHandling.Decimal })
                    root = JObject.Load(r, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException e) { throw new FormatException($"manifest.json: {e.Message}", e); }

            Only(root, "manifest", "schema", "tables", "contentTables", "contentHash", "inputs");
            if (Int(root, "schema", "manifest") != Schema) throw new FormatException($"manifest.json schema 기대 {Schema}");

            var tables = new List<Table>();
            foreach (var p in Obj(root, "tables", "manifest").Properties())
            {
                var t = p.Value as JObject ?? throw new FormatException($"manifest.json tables.{p.Name}이 객체가 아니다");
                Only(t, "tables." + p.Name, "file", "rows", "sha256");
                tables.Add(new Table(p.Name, Str(t, "file", p.Name), Int(t, "rows", p.Name), Str(t, "sha256", p.Name)));
            }
            var contentTables = new List<string>();
            foreach (var v in root["contentTables"] as JArray ?? throw new FormatException("manifest.json contentTables가 배열이 아니다"))
                contentTables.Add(v.Type == JTokenType.String ? (string)v : throw new FormatException("manifest.json contentTables 값이 문자열이 아니다"));
            foreach (var p in Obj(root, "inputs", "manifest").Properties())
                if (p.Value.Type != JTokenType.String) throw new FormatException($"manifest.json inputs.{p.Name}이 문자열이 아니다");

            var m = new ContentManifest(tables.ToArray(), contentTables.ToArray(), Str(root, "contentHash", "manifest"));
            foreach (var name in m.ContentTables)
                if (!m.HasTable(name)) throw new FormatException($"manifest.json contentTables의 {name}이 tables에 없다");
            return m;
        }

        bool HasTable(string name)
        {
            foreach (var t in Tables)
                if (t.Name == name) return true;
            return false;
        }

        static void Only(JObject o, string where, params string[] names)
        {
            foreach (var n in names)
                if (o.Property(n) == null) throw new FormatException($"manifest.json {where}: {n}이 없다");
            foreach (var p in o.Properties())
                if (Array.IndexOf(names, p.Name) < 0) throw new FormatException($"manifest.json {where}: 모르는 필드 {p.Name}");
        }

        static JObject Obj(JObject o, string name, string where) =>
            o[name] as JObject ?? throw new FormatException($"manifest.json {where}.{name}이 객체가 아니다");

        static string Str(JObject o, string name, string where) =>
            o[name]?.Type == JTokenType.String ? (string)o[name] : throw new FormatException($"manifest.json {where}.{name}이 문자열이 아니다");

        static int Int(JObject o, string name, string where) =>
            o[name]?.Type == JTokenType.Integer ? (int)o[name] : throw new FormatException($"manifest.json {where}.{name}이 정수가 아니다");
    }
}
