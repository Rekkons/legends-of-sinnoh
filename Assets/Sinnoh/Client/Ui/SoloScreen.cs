using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S09 솔로 설정(04 §1·§2, 02 §3-3): 주인공 · 캠페인 · 트레이너 카드 · 울트라 온슬롯 · 난이도(울트라 버스트) · 갤럭시단 AI · 거래 · 무르기 · 세부 규칙 → [게임 시작]. 자리·조종·제한 시간 줄은 없다(솔로).
    /// 「주인공」 줄(결정 52·60 [가정 — plan §5]): 주인공 8명의 필드 도트(앞모습)를 두 줄로 놓고 하나를 고른다 — 보드의 주인공 도트 말과 배틀의 뒷모습이 그 인물이다.
    /// 줄과 값은 <see cref="SoloSettings"/>(= RuleOptions·AiSettings가 받는 값)에서만 온다. 아직 고를 수 없는 값은 흐리게 두고 마우스 오버에 준비 중이라고 적는다:
    /// 강한 AI(M8), 세부 규칙 직접 설정(화면 없음). 울트라 버스트는 고를 수 있다(M6 「UB 시작」 — <see cref="SoloGame.CampaignReady"/>). 트레이너 카드 「사용 안 함」은 고를 수 있다 — 그 판의 말은 주인공 도트다(04 §3-1).
    /// 울트라 버스트에서만 듣는 줄(울트라 온슬롯·난이도)은 신오의 심장이면 줄째 흐리다(02 §3-2).
    /// 뒤의 보드 전경은 타이틀과 같다. 자리·크기는 <c>Styles~/Sinnoh.uss</c>의 <c>sn-solo-*</c>, 글은 문자열 표 <c>screen.solo.*</c>. 설명은 마우스 오버(툴팁).
    /// 요소 이름: <c>solo</c>·<c>solo-panel</c>·<c>solo-row-&lt;줄&gt;</c>·<c>solo-opt-&lt;줄&gt;-&lt;값&gt;</c>(주인공 줄의 값 = 주인공 키)·<c>solo-back</c>·<c>solo-start</c>.
    /// </summary>
    public sealed class SoloScreen : IUiScreen, IOverBoard
    {
        public const string Protagonist = "protagonist", Campaign = "campaign", TrainerCards = "trainer_cards", Onslaught = "onslaught", UbDifficulty = "ub_difficulty", GalacticAi = "galactic_ai", Rules = "rules", Trading = "trading", Undo = "undo";

        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-solo-col</c>의 left 60 + width 400).</summary>
        public const float ColumnRight = 460f;

        /// <summary>영어 화면에서 판을 넓힌 만큼(720 토큰 px — USS <c>.sn-lang-en .sn-solo-col</c> · <c>.sn-room-col</c> 440 = 400 + 40, M9 언어 ②). 한국어 · 흐름 없음 = 0.</summary>
        public static float EnWiden(GameFlow flow) => flow != null && flow.Lang == "en" ? 40f : 0f;

        /// <summary>트레이너 카드를 끈 판을 지금 시작할 수 있나: 된다 — 그 판의 말은 주인공 도트다(04 §3-1, 직업 그림을 골라 둔 화면에서도).</summary>
        public static readonly bool TrainerCardsOffReady = true;

        sealed class Opt
        {
            public string Id, TextKey, TipKey;
            /// <summary>글 대신 그릴 주인공(키) — 주인공 줄의 값.</summary>
            public string Art;
            public bool Ready;
            public Func<SoloSettings, bool> Picked;
            public Action<SoloSettings> Pick;
            public VisualElement Element;
        }

        sealed class Row
        {
            public string Id;
            public bool UbOnly;
            public readonly List<Opt> Opts = new List<Opt>();
            public VisualElement Element;
        }

        readonly GameFlow flow;
        readonly SoloSettings set;
        readonly List<Row> rows = new List<Row>();
        UiStage stage;

        public SoloScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            set = flow.Settings.Clone();
            // 지난 값이 지금 고를 수 없는 것이면 기본으로 되돌린다
            if (!SoloGame.CampaignReady(set.Campaign)) set.Campaign = Sinnoh.Rules.Campaign.HeartOfSinnoh;
            if (!SoloGame.AiReady(set.GalacticAi)) set.GalacticAi = GalacticAiLevel.Principle;
            if (!TrainerCardsOffReady) set.TrainerCards = true;
            if (!GameFlow.TradingReady) set.Trading = false;   // 결정 124: 거래는 준비 중
            if (!Protagonists.Has(set.Protagonist)) set.Protagonist = Protagonists.Default;

            var row = AddRow(Protagonist);
            foreach (string key in Protagonists.Keys)
            {
                string k = key;
                AddOpt(row, k, "screen.solo.protagonist." + k, true, s => s.Protagonist == k, s => s.Protagonist = k, null);   // 마우스 오버는 그 주인공의 이름만(결정 70 ⑤ — 설명문 없음)
                row.Opts[row.Opts.Count - 1].Art = k;
            }
            row = AddRow(Campaign);
            AddOpt(row, "hos", "screen.solo.campaign.hos", true, s => s.Campaign == Sinnoh.Rules.Campaign.HeartOfSinnoh, s => s.Campaign = Sinnoh.Rules.Campaign.HeartOfSinnoh, "screen.solo.campaign.hos.tip");
            AddOpt(row, "ub", "screen.solo.campaign.ub", SoloGame.CampaignReady(Sinnoh.Rules.Campaign.UltraBurst), s => s.Campaign == Sinnoh.Rules.Campaign.UltraBurst, s => s.Campaign = Sinnoh.Rules.Campaign.UltraBurst, "screen.solo.campaign.ub.tip");
            row = AddRow(TrainerCards);
            AddOpt(row, "on", "screen.common.on", true, s => s.TrainerCards, s => s.TrainerCards = true, "screen.solo.trainer_cards.on.tip");   // 「테스트 중인 기능」(결정 134)
            AddOpt(row, "off", "screen.common.off", TrainerCardsOffReady, s => !s.TrainerCards, s => s.TrainerCards = false, "screen.solo.trainer_cards.tip");
            row = AddRow(Onslaught, ubOnly: true);
            AddOpt(row, "off", "screen.common.off", true, s => !s.UltraOnslaught, s => s.UltraOnslaught = false, "screen.solo.onslaught.tip");
            AddOpt(row, "on", "screen.common.on", true, s => s.UltraOnslaught, s => s.UltraOnslaught = true, "screen.solo.onslaught.tip");
            row = AddRow(UbDifficulty, ubOnly: true);
            AddOpt(row, "new", "screen.solo.ub_difficulty.new", true, s => s.UbDifficulty == Sinnoh.Rules.UbDifficulty.NewTrainer, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.NewTrainer, "screen.solo.ub_difficulty.new.tip");
            AddOpt(row, "veteran", "screen.solo.ub_difficulty.veteran", true, s => s.UbDifficulty == Sinnoh.Rules.UbDifficulty.VeteranTrainer, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.VeteranTrainer, "screen.solo.ub_difficulty.veteran.tip");
            AddOpt(row, "master", "screen.solo.ub_difficulty.master", true, s => s.UbDifficulty == Sinnoh.Rules.UbDifficulty.MasterTrainer, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.MasterTrainer, "screen.solo.ub_difficulty.master.tip");
            row = AddRow(GalacticAi);
            AddOpt(row, "basic", "screen.solo.galactic_ai.basic", SoloGame.AiReady(GalacticAiLevel.Principle), s => s.GalacticAi == GalacticAiLevel.Principle, s => s.GalacticAi = GalacticAiLevel.Principle, "screen.solo.galactic_ai.basic.tip");
            AddOpt(row, "strong", "screen.solo.galactic_ai.strong", SoloGame.AiReady(GalacticAiLevel.Strong), s => s.GalacticAi == GalacticAiLevel.Strong, s => s.GalacticAi = GalacticAiLevel.Strong, "screen.solo.galactic_ai.strong.tip");   // M8 「연결」부터 고를 수 있다
            // 거래(결정 75 ⑤ — RuleOptions.Trading): 판을 만들 때 켜고 끈다. 혼자 하는 판에는 상대가 없어 값만 실린다(여럿 판 로비 M7이 같은 줄을 쓴다).
            // 결정 124: 지금은 「켜기」가 준비 중(회색 · 고를 수 없음) — 끔에 고정
            row = AddRow(Trading);
            AddOpt(row, "on", "screen.common.on", GameFlow.TradingReady, s => s.Trading, s => s.Trading = true, "screen.solo.trading.tip");
            AddOpt(row, "off", "screen.common.off", true, s => !s.Trading, s => s.Trading = false, "screen.solo.trading.tip");
            // 무르기(결정 149 · 154): 판을 만들 때 고른다 — 새 판의 처음 값은 「내 턴만」(SoloGame.NewGameUndo, GameFlow.Settings). 규칙이 아니라 세부 규칙 줄과 따로 둔다
            row = AddRow(Undo);
            AddOpt(row, "off", "screen.solo.undo.off", true, s => s.Undo == UndoMode.Off, s => s.Undo = UndoMode.Off, "screen.solo.undo.off.tip");
            AddOpt(row, "turn", "screen.solo.undo.turn", true, s => s.Undo == UndoMode.Turn, s => s.Undo = UndoMode.Turn, "screen.solo.undo.turn.tip");
            AddOpt(row, "free", "screen.solo.undo.free", true, s => s.Undo == UndoMode.Free, s => s.Undo = UndoMode.Free, "screen.solo.undo.free.tip");
            row = AddRow(Rules);
            AddOpt(row, "default", "screen.solo.rules.default", true, s => true, s => { }, "screen.solo.rules.default.tip");
            AddOpt(row, "custom", "screen.solo.rules.custom", false, s => false, s => { }, null);
        }

        Row AddRow(string id, bool ubOnly = false)
        {
            var row = new Row { Id = id, UbOnly = ubOnly };
            rows.Add(row);
            return row;
        }

        static void AddOpt(Row row, string id, string textKey, bool ready, Func<SoloSettings, bool> picked, Action<SoloSettings> pick, string tipKey) =>
            row.Opts.Add(new Opt { Id = id, TextKey = textKey, Ready = ready, Picked = picked, Pick = pick, TipKey = tipKey });

        public string Name => "solo";

        public float PanelRight => ColumnRight + EnWiden(flow);

        /// <summary>지금 고른 값(사본이 아니다 — 읽기만).</summary>
        public SoloSettings Settings => set;

        /// <summary>그 줄이 지금 듣는가(울트라 버스트 전용 줄은 울트라 버스트에서만).</summary>
        public bool RowEnabled(string row) => !Find(row).UbOnly || set.Campaign == Sinnoh.Rules.Campaign.UltraBurst;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            var col = Add(page, new VisualElement(), "solo-col", "sn-solo-col");
            Add(col, Kit.Text(stage.T("screen.solo.title"), Kit.FTitle), "solo-head", "sn-head");

            var panel = Add(col, Kit.Panel(stage.Skin), "solo-panel", "sn-solo-panel");
            foreach (var row in rows)
            {
                var r = row;
                bool art = row.Opts[0].Art != null;   // 그림 값의 줄(주인공)은 값이 두 줄로 넘어간다
                row.Element = Add(panel, new VisualElement(), "solo-row-" + row.Id, art ? "sn-set-row" : "sn-solo-row");
                Add(row.Element, Kit.Text(stage.T("screen.solo." + row.Id), Kit.F2), null, "sn-solo-label");
                var opts = Add(row.Element, new VisualElement(), null, art ? "sn-proto-opts" : "sn-solo-opts");
                foreach (var opt in row.Opts)
                {
                    var o = opt;
                    opt.Element = Add(opts, new VisualElement(), "solo-opt-" + row.Id + "-" + opt.Id, "sn-opt");
                    if (opt.Art != null)
                    {
                        opt.Element.AddToClassList("sn-proto-opt");
                        opt.Element.Add(FieldDot(opt.Art));
                    }
                    else opt.Element.Add(Kit.Text(stage.T(opt.TextKey), Kit.F2));
                    stage.OnTap(opt.Element, SoundKeys.Select, e => Set(r.Id, o.Id));
                }
            }

            // 자동 저장 한 칸(결정 70 ③): 이어할 수 있는 판이 저장돼 있으면 새 판이 그것을 덮는다고 알린다
            Overwrites = flow.SoloSlotInUse;
            if (Overwrites) Add(col, Kit.Wrapped(stage.T("screen.solo.overwrite"), Kit.F2), "solo-overwrite", "sn-solo-note");
            var foot = Add(col, new VisualElement(), "solo-foot", "sn-foot-row");
            Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "solo-back", null);
            Add(foot, Kit.Button(stage.T("screen.solo.start"), KitButtonKind.Main, Start), "solo-start", null);
            Refresh();
            return page;
        }

        public void Close() { }

        /// <summary>[게임 시작]이 자동 저장된 판을 덮는다고 알리고 있나(<c>solo-overwrite</c>).</summary>
        public bool Overwrites { get; private set; }

        /// <summary>값을 고른다. 아직 고를 수 없는 값이거나 듣지 않는 줄이면 아무 일도 없다(false).</summary>
        public bool Set(string row, string opt)
        {
            var r = Find(row);
            var o = r.Opts.Find(x => x.Id == opt) ?? throw new ArgumentException("없는 값 " + row + "/" + opt, nameof(opt));
            if (!o.Ready || !RowEnabled(row)) return false;
            o.Pick(set);
            Refresh();
            return true;
        }

        /// <summary>[게임 시작]: 고른 값으로 솔로 게임을 시작한다 — 셋업 화면으로 넘어간다.</summary>
        public void Start() => flow.StartSolo(set);

        /// <summary>[뒤로]: 타이틀로.</summary>
        public void Back() => flow.OpenTitle();

        Row Find(string id) => rows.Find(r => r.Id == id) ?? throw new ArgumentException("없는 줄 " + id, nameof(id));

        // 고른 값 표시·흐림·툴팁을 지금 값에 맞춘다(화면을 만든 뒤에만)
        void Refresh()
        {
            if (stage == null) return;
            foreach (var row in rows)
            {
                if (row.Element == null) continue;
                bool on = RowEnabled(row.Id);
                row.Element.EnableInClassList("sn-off", !on);
                foreach (var opt in row.Opts)
                {
                    opt.Element.EnableInClassList("sn-opt-picked", on && opt.Picked(set));
                    opt.Element.EnableInClassList("sn-off", on && !opt.Ready);
                    string body = !on ? stage.T("screen.solo.ub_only") : !opt.Ready ? stage.T("screen.common.not_ready") : opt.TipKey != null ? stage.T(opt.TipKey) : null;
                    stage.Tips.Hook(opt.Element, stage.T(opt.TextKey), body, TipSide.Below, reading: true);
                }
            }
        }

        /// <summary>
        /// 주인공의 필드 도트 앞모습 칸(<c>sn-proto</c> — USS가 벌마다 정수배 크기를 준다): 걷기 시트를 통째로 깔고 선 프레임만 보이게 자른다(<see cref="MonArt"/>와 같은 방식).
        /// 그림이 없으면 빈 칸(자리만).
        /// </summary>
        public static VisualElement FieldDot(string protagonist)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("sn-proto");
            if (!BoardPieces.FieldFront(protagonist, out var sheet, out int frame, out int size)) return box;
            var canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            canvas.AddToClassList("sn-proto-canvas");
            canvas.style.width = size;
            canvas.style.height = size;
            canvas.style.overflow = Overflow.Hidden;
            box.Add(canvas);
            var strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.top = -frame * size;
            strip.style.width = sheet.width;
            strip.style.height = sheet.height;
            strip.style.backgroundImage = new StyleBackground(sheet);
            canvas.Add(strip);
            return box;
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
