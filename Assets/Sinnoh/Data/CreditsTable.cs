using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sinnoh.Data
{
    /// <summary>크레딧 항목 하나 = 한 사람(곳): 이름 · 역할 한 줄 · 작가 이름 목록(있으면). 출처 링크 · 사용 조건은 CREDITS.md에만.</summary>
    public sealed class CreditEntry
    {
        public string Who, Role, RoleEn;
        /// <summary>작가 이름 목록(null = 없음 — Smogon · 쇼다운 항목만 있다).</summary>
        public IReadOnlyList<string> Names;

        /// <summary>그 언어의 역할 줄(영어가 아니면 한국어).</summary>
        public string RoleIn(string lang) => lang == "en" ? RoleEn : Role;
    }

    /// <summary>크레딧 탭 하나(화면 S03의 왼쪽 줄).</summary>
    public sealed class CreditSection
    {
        public string Id, Title, TitleEn;
        public IReadOnlyList<CreditEntry> Entries;

        /// <summary>그 언어의 탭 이름(영어가 아니면 한국어).</summary>
        public string TitleIn(string lang) => lang == "en" ? TitleEn : Title;
    }

    /// <summary>
    /// 크레딧 화면 S03의 표(`Data/Json/credits.json` — Tools/data/build.mjs가 `Tools/assets/credits/roll.json`(CREDITS.md와 같은 보이는 글)에서 만든다, M9). 엄격하다:
    /// 빠진·모르는 필드 · 빈 id · 겹친 id · 빈 제목 · 빈 탭 · 빈 who · role · 빈 이름 목록 = FormatException. 순서는 표 그대로(CREDITS.md의 절 순서).
    /// </summary>
    public sealed class CreditsTable
    {
        public readonly IReadOnlyList<CreditSection> Sections;

        CreditsTable(List<CreditSection> sections) => Sections = sections;

        [JsonObject(ItemRequired = Required.Always)]
        sealed class Row
        {
            public string Id, Title, TitleEn;
            public List<EntryRow> Entries;
        }

        sealed class EntryRow
        {
            [JsonProperty(Required = Required.Always)] public string Who;
            [JsonProperty(Required = Required.Always)] public string Role;
            [JsonProperty(Required = Required.Always)] public string RoleEn;
            [JsonProperty(Required = Required.AllowNull)] public List<string> Names;
        }

        public static CreditsTable Load(string json, string file = "credits.json")
        {
            var rows = TableJson.Parse<Row>(json, file);
            var sections = new List<CreditSection>(rows.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string at = $"{file} 행 {i} {r.Id}";
                if (string.IsNullOrEmpty(r.Id) || !ids.Add(r.Id)) throw new FormatException(at + ": id가 비었거나 겹친다");
                if (string.IsNullOrEmpty(r.Title) || string.IsNullOrEmpty(r.TitleEn)) throw new FormatException(at + ": 제목이 비었다");
                if (r.Entries.Count == 0) throw new FormatException(at + ": 항목이 없다");
                var entries = new List<CreditEntry>(r.Entries.Count);
                foreach (var e in r.Entries)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Who) || string.IsNullOrEmpty(e.Role) || string.IsNullOrEmpty(e.RoleEn)) throw new FormatException(at + ": 빈 칸");
                    if (e.Names != null && (e.Names.Count == 0 || e.Names.Exists(string.IsNullOrWhiteSpace))) throw new FormatException(at + ": 빈 이름 목록");
                    entries.Add(new CreditEntry { Who = e.Who, Role = e.Role, RoleEn = e.RoleEn, Names = e.Names });
                }
                sections.Add(new CreditSection { Id = r.Id, Title = r.Title, TitleEn = r.TitleEn, Entries = entries });
            }
            return new CreditsTable(sections);
        }
    }
}
