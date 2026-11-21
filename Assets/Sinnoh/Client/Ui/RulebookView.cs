using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 룰 설명서(S43 — 결정 100 · 111 ①) 절 하나를 그리는 부품: 큰 창(<see cref="RulebookScreen"/>, B안)과 게임 중 옆 판(<see cref="RulebookSide"/>, C안)이 같이 쓴다.
    /// 블록(<see cref="RulebookBlockKind"/>)마다: 문단 · 작은 제목 · 목록(두 단계) · 번호 목록 · 표 · 그림(Gen <c>Rulebook/</c> — 웹 손님판은 나눠 받기라 오기 전에는 자리표시, 오면 다시 그린다) ·
    /// 묻고 답 · 「디지털판」 꼬리표(배지 + 글 — plan §5 #307) · cards 블록(게임 문자열 표의 이름 · 문구 · 아이템 그림 · 레시피 규토리 — #315).
    /// 글 안 링크 <c>[[절 키|글]]</c>는 원본처럼 「[글]」에 밑줄을 그어 누르면 그 절로 간다(UI Toolkit 링크 태그 — <c>UnityEngine.UIElements.Experimental.PointerUpLinkTagEvent</c>).
    /// 찾는 말은 칠한다(#303).
    /// 링크 · 칠하기 색은 화면 USS의 <c>--sn-book-link</c> · <c>--sn-book-mark</c>(<c>sn-book</c> 클래스 요소에서 읽는다 — 코드에 색을 적지 않는다).
    /// </summary>
    public sealed class RulebookView
    {
        /// <summary>기호(두 언어 같은 글리프 — 문자열 표에 두지 않는다): 위 띠 [설명서] 단추 · 목록 머리 · 묻고 답 머리 · 이전/다음 · 접기 화살표.</summary>
        public const string Help = "?", Bullet = "·", Q = "Q.", A = "A.", Prev = "◀", Next = "▶", Opened = "▼", Folded = "▶";

        /// <summary>그림 크기 = 원본 렌더(쪽 폭 2400 px — plan §5 #340) 픽셀 × 이 배수(720 벌 기준 — 1080 벌은 1.5배). 칸 폭 상한은 USS(<c>sn-book-figure-img</c> max-width).</summary>
        public const float FigureScale = 0.5f;

        static readonly CustomStyleProperty<Color> LinkColor = new CustomStyleProperty<Color>("--sn-book-link"), MarkColor = new CustomStyleProperty<Color>("--sn-book-mark");
        static readonly Regex LinkMark = new Regex(@"\[\[([^\]|]+)\|([^\]]+)\]\]", RegexOptions.CultureInvariant), Tag = new Regex("(<[^>]*>)", RegexOptions.CultureInvariant);

        readonly UiStage stage;
        readonly Ctx ctx;
        readonly Action<string> follow;
        string link, mark;

        /// <param name="follow">링크를 눌렀을 때(절 키). null이면 링크는 글만.</param>
        public RulebookView(UiStage stage, Ctx ctx, string lang, Action<string> follow)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.ctx = ctx;
            Lang = lang;
            this.follow = follow;
        }

        /// <summary>설명서 언어(그림 키의 언어 꼬리 — <c>.ko</c>·<c>.en</c>).</summary>
        public string Lang { get; }

        /// <summary>칠할 말(빈 글 = 칠하지 않는다).</summary>
        public string Query { get; set; } = "";

        /// <summary>마지막으로 그린 절의 링크(절 키, 나온 순서).</summary>
        public List<string> Links { get; } = new List<string>();

        /// <summary>마지막으로 그린 절의 그림 칸(그림 키 → 칸)과 아직 그림이 없는 칸 수(웹 손님판의 나눠 받기 — 오면 다시 그린다).</summary>
        public Dictionary<string, VisualElement> Figures { get; } = new Dictionary<string, VisualElement>();
        public int Waiting { get; private set; }

        /// <summary>
        /// 색을 읽을 요소(<c>sn-book</c> 클래스)를 건다: 스타일이 풀리면 링크 · 칠하기 색을 읽고 <paramref name="repaint"/>를 부른다(처음 그릴 때는 아직 색이 없을 수 있다).
        /// </summary>
        public void WatchColors(VisualElement element, Action repaint)
        {
            element.AddToClassList("sn-book");
            element.RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                string l = e.customStyle.TryGetValue(LinkColor, out var lc) ? "#" + ColorUtility.ToHtmlStringRGBA(lc) : null;
                string m = e.customStyle.TryGetValue(MarkColor, out var mc) ? "#" + ColorUtility.ToHtmlStringRGBA(mc) : null;
                if (l == link && m == mark) return;
                link = l;
                mark = m;
                repaint?.Invoke();
            });
        }

        /// <summary>절의 블록을 <paramref name="into"/>에 그린다(제목은 부르는 쪽이).</summary>
        public void Blocks(VisualElement into, RulebookSection s)
        {
            Links.Clear();
            Figures.Clear();
            Waiting = 0;
            foreach (var b in s.Blocks) Block(into, s, b);
        }

        void Block(VisualElement into, RulebookSection s, RulebookBlock b)
        {
            switch (b.Kind)
            {
                case RulebookBlockKind.P: Line(into, s, b.Text, "sn-book-p"); break;
                case RulebookBlockKind.H: Add(into, Kit.Text(Rulebook.Plain(b.Text), Kit.F2), null, "sn-book-sub").AddToClassList("kit-title"); break;
                case RulebookBlockKind.List:
                case RulebookBlockKind.Ol:
                    for (int i = 0; i < b.Items.Count; i++)
                    {
                        var it = b.Items[i];
                        Item(into, s, b.Kind == RulebookBlockKind.Ol ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : Bullet, it.Text, false);
                        foreach (var sub in it.Items) Item(into, s, Bullet, sub.Text, true);
                    }
                    break;
                case RulebookBlockKind.Table: Table(into, b); break;
                case RulebookBlockKind.Figure: Figure(into, b); break;
                case RulebookBlockKind.Qa:
                    Item(into, s, Q, b.Question, false).AddToClassList("sn-book-q");
                    Item(into, s, A, b.Text, false);
                    break;
                case RulebookBlockKind.Digital:
                    var row = Add(into, new VisualElement(), "rulebook-digital", "sn-book-digital");
                    var badge = Add(row, Kit.Badge(stage.T("screen.rulebook.digital"), KitBadgeKind.Silver, Kit.F2), null, "sn-book-digital-badge");
                    stage.Tips.Hook(badge, null, stage.T("screen.rulebook.digital.tip"), TipSide.Below, reading: true);
                    var text = Line(row, s, b.Text, "sn-book-digital-text");
                    text.style.flexGrow = 1;
                    break;
                case RulebookBlockKind.Cards: foreach (string key in b.Keys) Card(into, key); break;
            }
        }

        // 목록 한 줄: 머리(· · 1. · Q. · A.) + 글
        VisualElement Item(VisualElement into, RulebookSection s, string head, string raw, bool second)
        {
            var row = Add(into, new VisualElement(), null, second ? "sn-book-li-2" : "sn-book-li");
            var mark = Add(row, Kit.Text(head, Kit.F2), null, "sn-book-li-mark");
            if (head == Q) mark.AddToClassList("kit-title");
            Line(row, s, raw, "sn-book-li-text").style.flexGrow = 1;
            return row;
        }

        /// <summary>글 한 줄(감기는 글 — 링크 · 칠하기).</summary>
        VisualElement Line(VisualElement into, RulebookSection s, string raw, string cls)
        {
            var label = Kit.Wrapped(Rich(raw), Kit.F2);
            foreach (var (key, _) in Rulebook.LinksIn(raw)) if (!Links.Contains(key)) Links.Add(key);
            if (follow != null) label.RegisterCallback<UnityEngine.UIElements.Experimental.PointerUpLinkTagEvent>(e => { stage.Tap(SoundKeys.Select); follow(e.linkID); });
            return Add(into, label, null, cls);
        }

        /// <summary>보이는 리치 글: 링크 → 밑줄 「[글]」(색 · 링크 태그), 찾는 말 → 칠하기(태그 밖 글에서만).</summary>
        public string Rich(string raw)
        {
            if (raw == null) return "";
            string text = LinkMark.Replace(raw, m =>
            {
                string shown = "[" + m.Groups[2].Value + "]";
                string body = link != null ? "<color=" + link + "><u>" + shown + "</u></color>" : "<u>" + shown + "</u>";
                return follow != null ? "<link=\"" + m.Groups[1].Value + "\">" + body + "</link>" : body;
            });
            return Marked(text, Query, mark);
        }

        /// <summary>리치 글의 태그 밖 글에서 query를 칠한다(대소문자 무시 — 찾기와 같은 규칙). 색이 없으면 그대로. 설명서 글에는 꺾쇠가 없다(태그만 꺾쇠).</summary>
        public static string Marked(string rich, string query, string color)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query) || color == null || string.IsNullOrEmpty(rich)) return rich;
            var sb = new StringBuilder(rich.Length + 32);
            foreach (string part in Tag.Split(rich))
            {
                if (part.Length == 0) continue;
                if (part[0] == '<' && part[part.Length - 1] == '>')
                {
                    sb.Append(part);
                    continue;
                }
                int at = 0;
                for (int i = part.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0; i = part.IndexOf(query, at, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(part, at, i - at).Append("<mark=").Append(color).Append('>').Append(part, i, query.Length).Append("</mark>");
                    at = i + query.Length;
                }
                sb.Append(part, at, part.Length - at);
            }
            return sb.ToString();
        }

        void Table(VisualElement into, RulebookBlock b)
        {
            var table = Add(into, new VisualElement(), null, "sn-book-table");
            TableRow(table, b.Head, true, null);
            for (int i = 0; i < b.Rows.Count; i++) TableRow(table, b.Rows[i], false, i < b.Icons.Count ? b.Icons[i] : null);
        }

        // 표 한 줄. icon = 첫 칸 글 앞에 같은 줄로 놓을 그림 키(원본 룰북처럼 「◎ 혼란」 — 결정 143 ③)
        void TableRow(VisualElement table, IReadOnlyList<string> cells, bool head, string icon)
        {
            var row = Add(table, new VisualElement(), null, "sn-book-tr");
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = Add(row, new VisualElement(), null, head ? "sn-book-th" : "sn-book-td");
                if (i == 0 && icon != null)
                {
                    cell.AddToClassList("sn-book-td-head");
                    Icon(cell, icon);
                }
                var text = Add(cell, Kit.Wrapped(Rich(cells[i]), Kit.F2), null, null);
                if (i == 0 && icon != null) text.style.flexShrink = 1;
            }
        }

        // 표 줄 머리 아이콘(그림 키 — 그림이 아직 없으면 자리만, 웹 손님판의 나눠 받기는 오면 다시 그린다)
        void Icon(VisualElement cell, string figure)
        {
            var img = Add(cell, new VisualElement(), "rulebook-icon-" + figure, "sn-book-td-icon");
            Figures[figure] = img;
            bool pending = false;
            foreach (string key in FigureKeys(figure, Lang))
            {
                var tex = GenAssets.Load<Texture2D>(key);
                if (tex != null)
                {
                    img.style.backgroundImage = new StyleBackground(tex);
                    return;
                }
                pending |= GenAssets.Pending(key);
            }
            if (pending) Waiting++;
        }

        /// <summary>그림 키 → Gen 키(그림 속 글이 언어마다 다르면 <c>.&lt;언어&gt;</c> 꼬리가 붙은 것을 먼저 — 「설명서 그림」 묶음, plan §5 #340).</summary>
        public static string[] FigureKeys(string figure, string lang) =>
            new[] { GenAssets.RulebookFolder + figure + "." + lang + ".png", GenAssets.RulebookFolder + figure + ".png" };

        void Figure(VisualElement into, RulebookBlock b)
        {
            var box = Add(into, new VisualElement(), "rulebook-figure-" + b.Figure, "sn-book-figure");
            Figures[b.Figure] = box;
            Texture2D tex = null;
            bool pending = false;
            foreach (string key in FigureKeys(b.Figure, Lang))
            {
                tex = GenAssets.Load<Texture2D>(key);
                if (tex != null) break;
                pending |= GenAssets.Pending(key);
            }
            if (tex != null)
            {
                var img = Add(box, new VisualElement(), null, "sn-book-figure-img");
                float aspect = (float)tex.height / tex.width;
                if (aspect < 1f / 3f) img.AddToClassList("sn-book-figure-wide");   // 아이콘을 가로로 이은 띠
                img.style.backgroundImage = new StyleBackground(tex);
                img.style.width = Mathf.Round(tex.width * FigureScale * stage.Grade.TokenHeight / 720f);   // 원본 렌더 크기에 비례(칸 폭 상한은 USS max-width)
                img.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    float h = Mathf.Round(img.resolvedStyle.width * aspect);
                    if (!float.IsNaN(h) && Mathf.Abs(img.resolvedStyle.height - h) > 0.5f) img.style.height = h;
                });
            }
            else
            {
                if (pending) Waiting++;
                var ph = Add(box, new VisualElement(), null, "sn-book-figure-ph");
                Add(ph, Kit.Text(stage.T(pending ? "screen.rulebook.figure.wait" : "screen.rulebook.figure.none"), Kit.F2), null, "kit-muted");
            }
            if (!string.IsNullOrEmpty(b.Text)) Add(box, Kit.Wrapped(Rich(b.Text), Kit.F2), null, "sn-book-caption").AddToClassList("kit-muted");
        }

        // cards 블록 한 줄(#315): 트레이너 카드 = 이름 + 능력 둘(이름 · 문구) · 울트라 온슬롯 = 이름 + 문구 줄 · 아이템 = 그림 + 이름 + 레시피 규토리 + 효과 문구
        void Card(VisualElement into, string key)
        {
            var card = Add(into, new VisualElement(), "rulebook-card-" + key, "sn-book-card");
            var head = Add(card, new VisualElement(), null, "sn-book-card-head");
            if (key.StartsWith("item.", StringComparison.Ordinal) && ctx != null && ctx.Items.Ids.TryIndexOf(key, out int id))
            {
                var item = ctx.Items.Items[id];
                string icon = WorkUi.IconKey(item);
                if (icon != null) WorkUi.Icon(head, icon, "sn-icon");
                Add(head, Kit.Text(Marked(stage.T(key), Query, mark), Kit.F2), null, "sn-book-card-name").AddToClassList("kit-title");
                if (item.Recipe != null)
                {
                    var recipe = Add(head, new VisualElement(), null, "sn-book-recipe");
                    Add(recipe, Kit.Text(stage.T("screen.rulebook.cards.recipe"), Kit.F2), null, "kit-muted");
                    foreach (var color in item.Recipe)
                    {
                        var a = WorkUi.Apricorn(recipe, color);
                        stage.Tips.Hook(a, null, stage.T(WorkUi.ColorKey(color)), TipSide.Below);
                    }
                }
                Text(card, key + ".text");
                return;
            }
            Add(head, Kit.Text(Marked(stage.T(key), Query, mark), Kit.F2), null, "sn-book-card-name").AddToClassList("kit-title");
            if (key.StartsWith("trainer_card.", StringComparison.Ordinal))
            {
                string id2 = key.Substring("trainer_card.".Length);
                for (int n = 1; n <= 2; n++)
                {
                    string ability = "trainer_ability." + id2 + "." + n.ToString(CultureInfo.InvariantCulture);
                    if (stage.TOrNull(ability) == null) continue;
                    var row = Add(card, new VisualElement(), null, "sn-book-ability");
                    Add(row, Kit.Text(Marked(stage.T(ability), Query, mark), Kit.F2), null, n == 1 ? "sn-book-ability-blue" : "sn-book-ability-red");
                    Text(row, ability + ".text").style.flexGrow = 1;
                }
                return;
            }
            for (int n = 1; stage.TOrNull(key + ".text." + n.ToString(CultureInfo.InvariantCulture)) != null; n++) Text(card, key + ".text." + n.ToString(CultureInfo.InvariantCulture));
        }

        Label Text(VisualElement into, string key)
        {
            string t = stage.TOrNull(key);
            return t == null ? null : Add(into, Kit.Wrapped(Marked(t, Query, mark), Kit.F2), null, "sn-book-card-text");
        }

        /// <summary>cards 블록이 가리키는 게임 표의 글(찾기가 함께 훑는다 — <see cref="Rulebook.Search"/>의 more): 이름 · 능력 이름과 문구 · 효과 문구.</summary>
        public static IEnumerable<string> CardTexts(UiStage stage, RulebookSection s)
        {
            foreach (var b in s.Blocks)
            {
                if (b.Kind != RulebookBlockKind.Cards) continue;
                foreach (string key in b.Keys)
                {
                    yield return stage.TOrNull(key) ?? "";
                    if (key.StartsWith("trainer_card.", StringComparison.Ordinal))
                    {
                        string id = key.Substring("trainer_card.".Length);
                        for (int n = 1; n <= 2; n++)
                        {
                            string a = "trainer_ability." + id + "." + n.ToString(CultureInfo.InvariantCulture);
                            yield return stage.TOrNull(a) ?? "";
                            yield return stage.TOrNull(a + ".text") ?? "";
                        }
                    }
                    else if (key.StartsWith("item.", StringComparison.Ordinal)) yield return stage.TOrNull(key + ".text") ?? "";
                    else
                        for (int n = 1; stage.TOrNull(key + ".text." + n.ToString(CultureInfo.InvariantCulture)) != null; n++) yield return stage.T(key + ".text." + n.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>절의 원천 쪽 글(「개요 · 룰북 3쪽」의 뒤 — 원천마다 문자열 표 <c>screen.rulebook.source.&lt;원천&gt;</c>, 쪽이 여럿이면 「1~2」).</summary>
        public static string Where(UiStage stage, RulebookSection s)
        {
            var inv = CultureInfo.InvariantCulture;
            string pages = s.Pages.Count == 1 ? s.Pages[0].ToString(inv) : s.Pages[0].ToString(inv) + "~" + s.Pages[s.Pages.Count - 1].ToString(inv);
            string t = stage.TOrNull("screen.rulebook.source." + s.Source) ?? s.Source;
            return s.ChapterTitle + " · " + t.Replace("{p}", pages);
        }

        public static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
