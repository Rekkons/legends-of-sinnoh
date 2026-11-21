using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Sinnoh.Data
{
    /// <summary>설명서 블록의 종류(build.mjs lib/rulebook.mjs의 kind와 같다).</summary>
    public enum RulebookBlockKind { P, H, List, Ol, Table, Figure, Qa, Digital, Cards }

    /// <summary>목록 항목(둘째 단계는 <see cref="Items"/> — 한 단계만).</summary>
    public sealed class RulebookItem
    {
        public string Text;
        public IReadOnlyList<RulebookItem> Items = Array.Empty<RulebookItem>();
    }

    /// <summary>
    /// 설명서 블록 하나. 글(<see cref="Text"/>)에는 링크 표시 <c>[[절 키|보이는 글]]</c>가 남아 있다 — 보이는 글은 <see cref="Rulebook.Plain"/>.
    /// P·H·Digital = Text · Qa = <see cref="Question"/> + Text(답) · List·Ol = <see cref="Items"/> · Table = <see cref="Head"/> + <see cref="Rows"/> · Figure = <see cref="Figure"/>(그림 키) + Text(그림 밑 글) ·
    /// Cards = <see cref="Keys"/>(게임 표 참조 — strings의 `trainer_card.*`·`ultra_onslaught.*`·`item.*` 키, 글은 표에 있다 · 설명서 글 ②).
    /// </summary>
    public sealed class RulebookBlock
    {
        public RulebookBlockKind Kind;
        public string Text, Question, Figure;
        public IReadOnlyList<RulebookItem> Items = Array.Empty<RulebookItem>();
        public IReadOnlyList<string> Head = Array.Empty<string>();
        public IReadOnlyList<IReadOnlyList<string>> Rows = Array.Empty<IReadOnlyList<string>>();
        /// <summary>Table 줄마다 첫 칸 글 앞에 놓을 그림 키(<see cref="Figure"/>와 같은 문법 · 같은 그림 표 — 결정 143 ③). 없으면 빈 목록.</summary>
        public IReadOnlyList<string> Icons = Array.Empty<string>();
        /// <summary>Cards 블록이 가리키는 문자열 키(나온 순서).</summary>
        public IReadOnlyList<string> Keys = Array.Empty<string>();

        /// <summary>이 블록의 모든 글(링크 표시 그대로 — 찾기·링크·글자 덮개 검사용).</summary>
        public IEnumerable<string> Texts()
        {
            if (Question != null) yield return Question;
            if (Text != null) yield return Text;
            foreach (var h in Head) yield return h;
            foreach (var r in Rows)
                foreach (var c in r) yield return c;
            foreach (var it in Items)
            {
                yield return it.Text;
                foreach (var x in it.Items) yield return x.Text;
            }
        }
    }

    /// <summary>찾기 결과 한 절(plan §5 #303): 그 절 · 맞은 곳 수 · 첫 맞은 곳 둘레 글 조각.</summary>
    public sealed class RulebookHit
    {
        public RulebookSection Section;
        public int Count;
        public string Snippet;
    }

    /// <summary>설명서의 절 하나(화면 S43의 쪽 — 결정 111 ① B안 「절 하나씩」).</summary>
    public sealed class RulebookSection
    {
        /// <summary>절 키 `&lt;장&gt;.&lt;이름&gt;` — 두 언어가 같다(완료 조건 2).</summary>
        public string Key, Chapter, ChapterTitle, Title;
        /// <summary>원천 id — 기본 룰북 = <see cref="Rulebook.BaseSource"/>, 확장 장은 저마다(`trainer_card_board`·`ultra_onslaught_deck`·`crafting_recipes`, chapters.json).</summary>
        public string Source;
        /// <summary>원천의 쪽(룰북 v1.4a — 한국어판·영문 원본이 같은 쪽).</summary>
        public IReadOnlyList<int> Pages;
        public IReadOnlyList<RulebookBlock> Blocks;
        /// <summary>이 절 글이 가리키는 절 키(나온 순서, 겹침 없음).</summary>
        public IReadOnlyList<string> Links;

        public override string ToString() => Key;
    }

    /// <summary>
    /// 룰 설명서 한 언어(`Data/Json/rulebook.&lt;lang&gt;.json` — build.mjs가 `Tools/data/authored/rulebook/`에서 만든다, M9 「설명서 글 ①②」 — 두 언어의 절 키 · 그림 키 · 카드 키가 같다). 엄격하다:
    /// 빠진·모르는 필드 · 모르는 kind · kind에 맞지 않는 칸 · 절 키 문법(`&lt;장&gt;.&lt;이름&gt;`, 장 = chapter) · 절 키 겹침 · 링크 대상 없음 · 그림 키(`&lt;절 키&gt;/&lt;이름&gt;`) 겹침 ·
    /// 원천이 빔 · 쪽이 1보다 작음 · 카드 키 문법 · 겹침 = FormatException. 순서는 표 그대로(장 → 절).
    /// 카드 키가 strings 표에 있는지는 표를 함께 읽는 쪽(시험 · 화면)이 본다 — 이 로더는 strings를 모른다.
    /// </summary>
    public sealed class Rulebook
    {
        static readonly Regex SectionKey = new Regex(@"^([a-z]+(?:_[a-z]+)*)\.[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant),
            FigureKey = new Regex(@"^([a-z]+(?:_[a-z]+)*\.[a-z0-9]+(?:_[a-z0-9]+)*)/[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant),
            LinkMark = new Regex(@"\[\[([^\]|]+)\|([^\]]+)\]\]", RegexOptions.CultureInvariant),
            StringKey = new Regex(@"^[a-z0-9_]+(\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant);

        /// <summary>기본 룰북 장의 원천 id.</summary>
        public const string BaseSource = "rulebook";

        public readonly IReadOnlyList<RulebookSection> Sections;
        readonly Dictionary<string, RulebookSection> byKey;

        Rulebook(List<RulebookSection> sections)
        {
            Sections = sections;
            byKey = new Dictionary<string, RulebookSection>(StringComparer.Ordinal);
            foreach (var s in sections) byKey[s.Key] = s;
        }

        /// <summary>그 키의 절. 없으면 null.</summary>
        public RulebookSection Find(string key) => key != null && byKey.TryGetValue(key, out var s) ? s : null;

        /// <summary>장 키 순서(처음 나온 순서).</summary>
        public IReadOnlyList<string> Chapters()
        {
            var list = new List<string>();
            foreach (var s in Sections)
                if (list.Count == 0 || list[list.Count - 1] != s.Chapter) list.Add(s.Chapter);
            return list;
        }

        /// <summary>보이는 글 — 링크 표시 <c>[[키|글]]</c> → 원본처럼 「[글]」.</summary>
        public static string Plain(string text) => text == null ? null : LinkMark.Replace(text, "[$2]");

        /// <summary>글 안의 링크(절 키 · 보이는 글) 나온 순서.</summary>
        public static IEnumerable<(string Key, string Label)> LinksIn(string text)
        {
            if (text == null) yield break;
            foreach (Match m in LinkMark.Matches(text)) yield return (m.Groups[1].Value, m.Groups[2].Value);
        }

        /// <summary>그 절의 보이는 글(제목 → 블록 글 순서, 링크는 「[글]」) — 찾기가 훑는다.</summary>
        public static IEnumerable<string> PlainTexts(RulebookSection s)
        {
            yield return Plain(s.Title);
            foreach (var b in s.Blocks)
                foreach (string t in b.Texts()) yield return Plain(t);
        }

        /// <summary>text 안에서 query가 나온 수(겹치지 않게 · 대소문자 무시 — 한글은 글자 그대로). 빈 query = 0.</summary>
        public static int Count(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;
            int n = 0;
            for (int i = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>찾기 결과 조각의 맞은 곳 앞 · 뒤 글자 수(plan §5 #303 「첫 문장 조각」).</summary>
        public const int SnippetBefore = 12, SnippetAfter = 30;

        /// <summary>
        /// 찾기(plan §5 #303): 보고 있는 언어의 보이는 글에서 query를 글자 그대로 부분 일치로 찾아 절마다 묶는다(형태소 · 초성 찾기 없음, 영문 대소문자만 무시) — 표 순서(장 → 절).
        /// <paramref name="more"/> = 그 절의 글 밖에서 함께 훑을 글(cards 블록이 가리키는 게임 표의 이름 · 문구 — 이 로더는 strings를 모른다). 조각 = 첫 맞은 곳 둘레(앞뒤가 잘리면 「…」).
        /// </summary>
        public IReadOnlyList<RulebookHit> Search(string query, Func<RulebookSection, IEnumerable<string>> more = null)
        {
            var hits = new List<RulebookHit>();
            query = query?.Trim();
            if (string.IsNullOrEmpty(query)) return hits;
            foreach (var s in Sections)
            {
                int n = 0;
                string snippet = null;
                var texts = new List<string>(PlainTexts(s));
                if (more != null) texts.AddRange(more(s));
                foreach (string t in texts)
                {
                    int c = Count(t, query);
                    if (c == 0) continue;
                    if (snippet == null) snippet = Snippet(t, query);
                    n += c;
                }
                if (n > 0) hits.Add(new RulebookHit { Section = s, Count = n, Snippet = snippet });
            }
            return hits;
        }

        /// <summary>찾기 결과 조각에 붙이는 생략 표시(두 언어 같은 글리프).</summary>
        public const string Ellipsis = "…";

        static string Snippet(string text, string query)
        {
            int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            int from = Math.Max(0, at - SnippetBefore), to = Math.Min(text.Length, at + query.Length + SnippetAfter);
            string cut = text.Substring(from, to - from).Replace('\n', ' ');
            return (from > 0 ? Ellipsis : "") + cut + (to < text.Length ? Ellipsis : "");
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class Row
        {
            public string Key, Chapter, ChapterTitle, Title, Source;
            public List<int> Pages;
            public List<BlockRow> Blocks;
        }

        sealed class BlockRow
        {
            [JsonProperty(Required = Required.Always)] public string Kind;
            public string Text, Q, Figure;
            public List<ItemRow> Items;
            public List<string> Head;
            public List<List<string>> Rows;
            public List<string> Icons;
            public List<string> Keys;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class ItemRow
        {
            public string Text;
            public List<ItemRow> Items;
        }

        public static Rulebook Load(string json, string file = "rulebook.ko.json")
        {
            var rows = TableJson.Parse<Row>(json, file);
            var sections = new List<RulebookSection>(rows.Count);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var figures = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string at = $"{file} 행 {i} {r.Key}";
                var m = SectionKey.Match(r.Key);
                if (!m.Success || m.Groups[1].Value != r.Chapter) throw new FormatException(at + ": 절 키 문법 <장>.<이름>");
                if (!keys.Add(r.Key)) throw new FormatException(at + ": 절 키가 겹친다");
                if (string.IsNullOrEmpty(r.Title) || string.IsNullOrEmpty(r.ChapterTitle)) throw new FormatException(at + ": 제목이 비었다");
                if (string.IsNullOrEmpty(r.Source)) throw new FormatException(at + ": 원천이 비었다");
                if (r.Pages.Count == 0 || r.Pages.Exists(p => p < 1)) throw new FormatException(at + ": 쪽");
                if (r.Blocks.Count == 0) throw new FormatException(at + ": 블록이 없다");
                var blocks = new List<RulebookBlock>(r.Blocks.Count);
                for (int k = 0; k < r.Blocks.Count; k++) blocks.Add(Block(r.Blocks[k], $"{at} 블록 {k}", r.Key, figures));
                var links = new List<string>();
                foreach (var b in blocks)
                    foreach (string t in b.Texts())
                    {
                        if (string.IsNullOrEmpty(t)) throw new FormatException(at + ": 빈 글");
                        foreach (var (key, _) in LinksIn(t))
                            if (!links.Contains(key)) links.Add(key);
                    }
                sections.Add(new RulebookSection
                {
                    Key = r.Key, Chapter = r.Chapter, ChapterTitle = r.ChapterTitle, Title = r.Title, Source = r.Source, Pages = r.Pages.ToArray(),
                    Blocks = blocks, Links = links,
                });
            }
            foreach (var s in sections)
                foreach (string l in s.Links)
                    if (!keys.Contains(l)) throw new FormatException($"{file} {s.Key}: 링크 대상 {l}가 없다");
            return new Rulebook(sections);
        }

        static RulebookBlock Block(BlockRow b, string at, string section, HashSet<string> figures)
        {
            var kind = b.Kind switch
            {
                "p" => RulebookBlockKind.P, "h" => RulebookBlockKind.H, "list" => RulebookBlockKind.List, "ol" => RulebookBlockKind.Ol,
                "table" => RulebookBlockKind.Table, "figure" => RulebookBlockKind.Figure, "qa" => RulebookBlockKind.Qa, "digital" => RulebookBlockKind.Digital, "cards" => RulebookBlockKind.Cards,
                _ => throw new FormatException(at + ": 모르는 kind " + b.Kind),
            };
            bool cards = kind == RulebookBlockKind.Cards;
            bool text = kind != RulebookBlockKind.List && kind != RulebookBlockKind.Ol && kind != RulebookBlockKind.Table && !cards;
            bool items = kind == RulebookBlockKind.List || kind == RulebookBlockKind.Ol;
            if ((b.Text != null) != text || (b.Q != null) != (kind == RulebookBlockKind.Qa) || (b.Figure != null) != (kind == RulebookBlockKind.Figure)
                || (b.Items != null) != items || (b.Head != null) != (kind == RulebookBlockKind.Table) || (b.Rows != null) != (kind == RulebookBlockKind.Table)
                || (b.Keys != null) != cards || (b.Icons != null && kind != RulebookBlockKind.Table))
                throw new FormatException(at + ": " + b.Kind + "에 맞지 않는 칸");
            var block = new RulebookBlock { Kind = kind, Text = b.Text, Question = b.Q, Figure = b.Figure };
            if (kind == RulebookBlockKind.Figure)
            {
                var m = FigureKey.Match(b.Figure);
                if (!m.Success || m.Groups[1].Value != section) throw new FormatException(at + ": 그림 키 문법 <절 키>/<이름> " + b.Figure);
                if (!figures.Add(b.Figure)) throw new FormatException(at + ": 그림 키가 겹친다 " + b.Figure);
            }
            if (cards)
            {
                if (b.Keys.Count == 0) throw new FormatException(at + ": 빈 카드 목록");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string k in b.Keys)
                    if (k == null || !StringKey.IsMatch(k) || !seen.Add(k)) throw new FormatException(at + ": 카드 키 " + k);
                block.Keys = b.Keys.ToArray();
            }
            if (items)
            {
                if (b.Items.Count == 0) throw new FormatException(at + ": 빈 목록");
                block.Items = b.Items.ConvertAll(it => Item(it, at, 0));
            }
            if (kind == RulebookBlockKind.Table)
            {
                if (b.Head.Count == 0 || b.Rows.Count == 0) throw new FormatException(at + ": 빈 표");
                foreach (var row in b.Rows)
                    if (row.Count != b.Head.Count) throw new FormatException(at + ": 표 칸 수");
                block.Head = b.Head.ToArray();
                block.Rows = b.Rows.ConvertAll(row => (IReadOnlyList<string>)row.ToArray());
                if (b.Icons != null)
                {
                    if (b.Icons.Count != b.Rows.Count) throw new FormatException(at + ": 표 아이콘 수 ≠ 줄 수");
                    foreach (string icon in b.Icons)
                    {
                        var m = FigureKey.Match(icon ?? "");
                        if (!m.Success || m.Groups[1].Value != section) throw new FormatException(at + ": 그림 키 문법 <절 키>/<이름> " + icon);
                        if (!figures.Add(icon)) throw new FormatException(at + ": 그림 키가 겹친다 " + icon);
                    }
                    block.Icons = b.Icons.ToArray();
                }
            }
            return block;
        }

        static RulebookItem Item(ItemRow it, string at, int depth)
        {
            if (depth > 0 && it.Items.Count > 0) throw new FormatException(at + ": 목록은 두 단계까지");
            return new RulebookItem { Text = it.Text, Items = it.Items.ConvertAll(x => Item(x, at, depth + 1)) };
        }
    }
}
