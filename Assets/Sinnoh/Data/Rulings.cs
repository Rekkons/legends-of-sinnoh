using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Sinnoh.Data
{
    /// <summary>판정의 출처(build.mjs lib/rulings.mjs의 source — creator · rulebook · house).</summary>
    public enum RulingSource { Creator, Rulebook, House }

    /// <summary>「규칙 판정」 화면의 항목 하나: 물음 · 답 · 출처와 그 항목이 든 묶음 · 절.</summary>
    public sealed class Ruling
    {
        public string Id, Group, GroupTitle, Section, SectionTitle, Q, A;
        public RulingSource Source;
        /// <summary>찾기용으로 접은 글(물음 + 줄바꿈 + 답 — <see cref="Rulings.Fold"/>).</summary>
        internal string Folded;
    }

    /// <summary>절 하나(목차 줄): 키 · 제목 · 항목 수.</summary>
    public sealed class RulingSection
    {
        public string Key, Title;
        public int Count;
    }

    /// <summary>묶음 하나(목차의 위 단계): 키 · 제목 · 절 · 항목 수.</summary>
    public sealed class RulingGroup
    {
        public string Key, Title;
        public IReadOnlyList<RulingSection> Sections;
        public int Count;
    }

    /// <summary>
    /// 「규칙 판정」 화면의 표(`Data/Json/rulings.&lt;언어&gt;.json` — Tools/data/build.mjs lib/rulings.mjs, 결정 213): 항목 하나 = 한 줄, 묶음 → 절 → 항목 순서. 엄격하다:
    /// 빠진·모르는 필드 · id 문법(r + 숫자 3자리) · 겹친 id · 절 키 문법(&lt;묶음&gt;.&lt;이름&gt;) · 빈 글 · 모르는 출처 · 흩어진 절(같은 절 · 묶음의 줄은 붙어 있다) · 빈 표 = FormatException.
    /// 찾기 · 거르기(<see cref="Filter"/>)는 엔진 없이 여기서 한다.
    /// </summary>
    public sealed class Rulings
    {
        static readonly Regex ItemId = new Regex(@"^r[0-9]{3}$", RegexOptions.CultureInvariant),
            SectionKey = new Regex(@"^([a-z]+)\.[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        /// <summary>출처 셋을 모두 켠 거르기 값(<see cref="Bit"/>의 합).</summary>
        public const int AllSources = 7;

        /// <summary>낱말 이음표 U+2060(감기는 화면 글에 들어간다 — 찾기는 없는 것으로 친다).</summary>
        public const char WordJoiner = (char)0x2060;

        public readonly IReadOnlyList<Ruling> Items;
        public readonly IReadOnlyList<RulingGroup> Groups;

        Rulings(List<Ruling> items, List<RulingGroup> groups)
        {
            Items = items;
            Groups = groups;
        }

        /// <summary>그 출처의 거르기 비트.</summary>
        public static int Bit(RulingSource source) => 1 << (int)source;

        /// <summary>찾기용으로 접는다: 공백 · U+2060을 빼고 영문은 소문자로(한글은 글자 그대로).</summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c) && c != WordJoiner) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>찾는 말을 띄어 쓴 낱말로 나눠 접는다(빈 낱말은 뺀다 · U+2060은 낱말을 나누지 않는다).</summary>
        public static IReadOnlyList<string> Terms(string query)
        {
            var terms = new List<string>();
            if (query == null) return terms;
            int from = -1;
            for (int i = 0; i <= query.Length; i++)
            {
                bool gap = i == query.Length || char.IsWhiteSpace(query[i]);
                if (!gap && from < 0) from = i;
                if (gap && from >= 0)
                {
                    string t = Fold(query.Substring(from, i - from));
                    if (t.Length > 0) terms.Add(t);
                    from = -1;
                }
            }
            return terms;
        }

        /// <summary>그 항목의 물음 + 답에 낱말이 모두 들어 있나(낱말은 <see cref="Terms"/>로 접은 것 — 없으면 참).</summary>
        public static bool Matches(Ruling r, IReadOnlyList<string> terms)
        {
            for (int i = 0; i < terms.Count; i++)
                if (r.Folded.IndexOf(terms[i], StringComparison.Ordinal) < 0) return false;
            return true;
        }

        /// <summary>
        /// 거른다(표 순서 그대로): <paramref name="group"/> · <paramref name="section"/> = 그 묶음 · 절만(null = 전체), <paramref name="sources"/> = 켠 출처(<see cref="Bit"/>의 합),
        /// <paramref name="query"/> = 물음 + 답에서 찾을 말 — 대소문자 · 공백 · U+2060 무시, 띄어 쓴 낱말은 모두 들어 있어야 한다(순서 무관).
        /// </summary>
        public List<Ruling> Filter(string query, string group = null, string section = null, int sources = AllSources)
        {
            var terms = Terms(query);
            var list = new List<Ruling>();
            foreach (var r in Items)
            {
                if ((sources & Bit(r.Source)) == 0) continue;
                if (group != null && r.Group != group) continue;
                if (section != null && r.Section != section) continue;
                if (Matches(r, terms)) list.Add(r);
            }
            return list;
        }

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class Row
        {
            public string Id, Group, GroupTitle, Section, SectionTitle, Q, A, Source;
        }
#pragma warning restore 0649

        public static Rulings Load(string json, string file = "rulings.ko.json")
        {
            var rows = TableJson.Parse<Row>(json, file);
            if (rows.Count == 0) throw new FormatException(file + ": 항목이 없다");
            var items = new List<Ruling>(rows.Count);
            var groups = new List<RulingGroup>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<RulingSection> sections = null;
            RulingGroup group = null;
            RulingSection section = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string at = $"{file} 행 {i} {r.Id}";
                if (!ItemId.IsMatch(r.Id)) throw new FormatException(at + ": id 문법 r + 숫자 3자리");
                if (!ids.Add(r.Id)) throw new FormatException(at + ": id가 겹친다");
                var m = SectionKey.Match(r.Section);
                if (!m.Success || m.Groups[1].Value != r.Group) throw new FormatException(at + ": 절 키 문법 <묶음>.<이름>");
                if (string.IsNullOrWhiteSpace(r.Q) || string.IsNullOrWhiteSpace(r.A) || string.IsNullOrWhiteSpace(r.GroupTitle) || string.IsNullOrWhiteSpace(r.SectionTitle))
                    throw new FormatException(at + ": 빈 글");
                RulingSource source = r.Source switch
                {
                    "creator" => RulingSource.Creator, "rulebook" => RulingSource.Rulebook, "house" => RulingSource.House,
                    _ => throw new FormatException(at + ": 모르는 source " + r.Source),
                };
                if (group == null || group.Key != r.Group)
                {
                    if (!seen.Add(r.Group)) throw new FormatException(at + ": 묶음 " + r.Group + "의 줄이 흩어져 있다");
                    sections = new List<RulingSection>();
                    group = new RulingGroup { Key = r.Group, Title = r.GroupTitle, Sections = sections };
                    groups.Add(group);
                    section = null;
                }
                if (section == null || section.Key != r.Section)
                {
                    if (!seen.Add(r.Section)) throw new FormatException(at + ": 절 " + r.Section + "의 줄이 흩어져 있다");
                    section = new RulingSection { Key = r.Section, Title = r.SectionTitle };
                    sections.Add(section);
                }
                if (group.Title != r.GroupTitle || section.Title != r.SectionTitle) throw new FormatException(at + ": 같은 묶음 · 절의 제목이 다르다");
                group.Count++;
                section.Count++;
                items.Add(new Ruling
                {
                    Id = r.Id, Group = r.Group, GroupTitle = r.GroupTitle, Section = r.Section, SectionTitle = r.SectionTitle, Q = r.Q, A = r.A, Source = source,
                    Folded = Fold(r.Q) + "\n" + Fold(r.A),
                });
            }
            return new Rulings(items, groups);
        }
    }
}
