using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 타입 상성 · 상태 이상 창(플레이 시험 T5 「상성표를 항상 볼 수 있어야 해」 · 「상태이상표도.」 — 결정 125의 「어떤 상황에서든 열린다」): 위 띠의 [상성](<see cref="GameFlow.ToggleTypes"/>)으로
    /// 보드 · 물음 창 · 배틀 · 결과 창 · 남의 턴 어디서든 연다(창이 떠 있으면 그 위 정보 창 층에 — 아래 창은 그대로).
    /// 왼쪽 = 상성표: 규칙 표 그대로(<see cref="TypeChart"/> — 원작 Lua <c>config.type_chart</c>: 줄 = 공격 기술 타입, 칸 = 받는 포켓몬 타입, 피해 +2 · −2 · −4, 빈 칸 = 보통).
    /// 축 이름은 왼쪽 위 모서리 칸에 대각선으로 나눠 「공격」(왼쪽 아래) · 「방어」(오른쪽 위)만 적는다 — 표 위 설명 줄은 없다(결정 143 ②).
    /// 오른쪽 = 룰 설명서의 「타입 상성」 · 「상태 이상」 절(원작 룰북 p.8 · p.9 — 상태 이상 6종의 효과 · 면역 표)을 설명서와 같은 그리기로(본가 관습으로 채우지 않는다).
    /// 요소 이름: <c>board-types</c>·<c>board-types-panel</c>·<c>board-types-close</c>·<c>board-types-chart</c>·<c>board-types-corner</c>(+ <c>-attack</c>·<c>-defend</c>)·<c>board-types-cell-&lt;공격&gt;-&lt;받는&gt;</c>(타입 이름 소문자)·<c>board-types-side</c>.
    /// </summary>
    public sealed class TypeChartScreen : IUiScreen
    {
        /// <summary>오른쪽에 그리는 설명서 절(타입 상성 · 상태 이상).</summary>
        public static readonly string[] Sections = { "battle.types", "battle.status" };

        /// <summary>칸 글(기호 — 두 언어 화면에 같은 글).</summary>
        public const string Weak = "+2", Resist = "-2", Immune = "-4";

        readonly GameFlow flow;
        UiStage stage;

        public TypeChartScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-types";

        /// <summary>그 칸의 글(보통이면 빈 글).</summary>
        public static string CellText(PokemonType attack, PokemonType defender)
        {
            switch (TypeChart.Of(attack, defender))
            {
                case Effectiveness.SuperEffective: return Weak;
                case Effectiveness.NotVeryEffective: return Resist;
                case Effectiveness.NotEffective: return Immune;
                default: return "";
            }
        }

        public static string CellName(PokemonType attack, PokemonType defender) =>
            "board-types-cell-" + attack.ToString().ToLowerInvariant() + "-" + defender.ToString().ToLowerInvariant();

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다(T35)
            if (flow.Stage.Current is BattleScreen) page.AddToClassList("sn-party-battle");   // 배틀 위: 어둠이 독까지 덮는다
            var scrim = Add(page, new VisualElement(), "board-types-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseTypes());   // 창 밖을 누르면 닫힌다
            var panel = Add(page, Kit.Panel(stage.Skin), "board-types-panel", "sn-types-panel");
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            Add(head, Kit.Text(stage.T("screen.types.title"), Kit.FTitle), "board-types-title", "kit-title");
            Add(head, Kit.Button(stage.T("screen.types.close"), KitButtonKind.Main, () => flow.CloseTypes()), "board-types-close", null);
            var body = Add(panel, new VisualElement(), null, "sn-types-body");
            Chart(Add(body, new VisualElement(), "board-types-chart", "sn-types-chart"));
            Side(Add(body, new VisualElement(), "board-types-side", "sn-types-side"));
            return page;
        }

        public void Close()
        {
            stage = null;
        }

        // 상성표: 맨 윗줄 = 받는 포켓몬 타입, 왼쪽 칸 = 공격 기술 타입(타입 없음은 상성을 무시해 뺀다). 축 이름은 왼쪽 위 칸의 대각선 양쪽(결정 143 ② — 설명 줄 없음)
        void Chart(VisualElement chart)
        {
            var top = Add(chart, new VisualElement(), null, "sn-types-row");
            Corner(top);
            for (var d = PokemonType.Normal; d <= PokemonType.Fairy; d++) Head(top, d, "screen.types.defend").AddToClassList("sn-types-colhead");
            for (var a = PokemonType.Normal; a <= PokemonType.Fairy; a++)
            {
                var row = Add(chart, new VisualElement(), null, "sn-types-row");
                Head(row, a, "screen.types.attack").AddToClassList("sn-types-rowhead");
                for (var d = PokemonType.Normal; d <= PokemonType.Fairy; d++)
                {
                    string text = CellText(a, d);
                    var cell = Add(row, new VisualElement(), CellName(a, d), "sn-types-cell");
                    if (text.Length == 0) continue;
                    cell.AddToClassList(text == Weak ? "sn-types-weak" : text == Resist ? "sn-types-resist" : "sn-types-immune");
                    var label = Add(cell, Kit.Text(text, Kit.F1), null, "sn-types-num");
                    label.pickingMode = PickingMode.Ignore;
                }
            }
        }

        // 머리 칸: 타입 그림 + 마우스 오버 = 타입 이름 · 쪽(공격 / 받는)
        VisualElement Head(VisualElement row, PokemonType type, string sideKey)
        {
            var cell = Add(row, new VisualElement(), null, "sn-types-cell");
            cell.AddToClassList("sn-types-head");
            MonParts.TypeIcon(cell, type).pickingMode = PickingMode.Ignore;
            stage.Tips.Hook(cell, stage.T("ui.types." + type), stage.T(sideKey), TipSide.Below);
            return cell;
        }

        // 왼쪽 위 모서리 칸: 왼쪽 위 → 오른쪽 아래 대각선 + 오른쪽 위 「방어」(열 머리 쪽) · 왼쪽 아래 「공격」(행 머리 쪽). 폭 = 행 머리 칸, 높이 = 열 머리 줄
        void Corner(VisualElement row)
        {
            var corner = Add(row, new Slash(), "board-types-corner", "sn-types-cell");
            corner.AddToClassList("sn-types-head");
            corner.AddToClassList("sn-types-rowhead");
            corner.AddToClassList("sn-types-colhead");
            corner.AddToClassList("sn-types-corner");
            Add(corner, Kit.Text(stage.T("screen.types.corner.defend"), Kit.F2), "board-types-corner-defend", "sn-types-corner-defend").AddToClassList("kit-muted");
            Add(corner, Kit.Text(stage.T("screen.types.corner.attack"), Kit.F2), "board-types-corner-attack", "sn-types-corner-attack").AddToClassList("kit-muted");
        }

        /// <summary>모서리 칸의 대각선(왼쪽 위 → 오른쪽 아래): 색 = USS <c>border-left-color</c>(칸 선과 같은 <c>--sn-line</c>).</summary>
        sealed class Slash : VisualElement
        {
            public Slash()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext mgc)
            {
                float w = layout.width, h = layout.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
                var p = mgc.painter2D;
                p.strokeColor = resolvedStyle.borderLeftColor;
                p.lineWidth = UnityEngine.Mathf.Max(1f, UnityEngine.Mathf.Round(h / 32f));   // 720 벌 1 · 1080 벌 2(칸 선과 같은 굵기로)
                p.BeginPath();
                p.MoveTo(new UnityEngine.Vector2(0f, 0f));
                p.LineTo(new UnityEngine.Vector2(w, h));
                p.Stroke();
            }
        }

        // 설명서의 두 절(지금 언어 — 없으면 쓰지 않는다)
        void Side(VisualElement side)
        {
            var book = flow.Book(flow.BookLang);
            if (book == null) return;
            var inner = RulebookScreen.Scroll(side, "board-types-scroll", out var into, out _);
            inner.AddToClassList("sn-book");
            var view = new RulebookView(stage, flow.Ctx, flow.BookLang, null);
            foreach (string key in Sections)
            {
                var s = book.Find(key);
                if (s == null) continue;
                Add(into, Kit.Text(s.Title, Kit.F2), null, "sn-book-sub").AddToClassList("kit-title");
                view.Blocks(into, s);
            }
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
