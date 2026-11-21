using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;
using static Sinnoh.Client.WorkUi;

namespace Sinnoh.Client
{
    /// <summary>
    /// S26 제작 창(04 §3-4 「내 차례에만」 · §8-8 36·37행, 룰북 p.14 — CraftRules): 레시피 14개(볼 4 · 스튜 4 · 지닌물건 6)를 보고 만든다. 보드 HUD의 [제작]이나
    /// 조우 창(S45)의 [제작](21행 — 고르기 전, 룰북 p.2)으로 연다. 위 줄 = 가진 규토리 일곱 색(보이는 값), 왼쪽 = 볼 · 스튜(턴당 1회), 오른쪽 = 지닌물건.
    /// 줄마다 그림 · 이름 · 재료 규토리 · 가진 수 · [제작]. 효과 문구는 줄의 툴팁이다.
    /// 규칙을 다시 세지 않는다: [제작]을 켜는 것은 열린 선택(맵 행동 · 조우 고르기)의 합법 제작 답 그대로이고, 「이번 턴 사용」 꼬리표만 보기(공개 정보)에서 고른다.
    /// 만든 것은 이 창이 그린다(36행 — 레시피 칸이 빛나고 재료가 빨려 들고 펑 → 「+1」, 37행 — 스튜 그릇이 튀고 효과 꼬리표). 창은 만든 뒤에도 그대로다 — 창 밖을 누르거나 [닫기]로 닫는다.
    /// 조우 창 위에서 열었으면 닫을 때 조우 창이 그대로 다시 선다(카드는 이미 앞면).
    /// 요소 이름: <c>board-craft</c>·<c>board-craft-panel</c>·<c>board-craft-close</c>·<c>board-craft-have-&lt;색&gt;</c>·<c>board-craft-row-&lt;아이템&gt;</c>·<c>board-craft-make-&lt;아이템&gt;</c>·
    /// <c>board-craft-count-&lt;아이템&gt;</c>·<c>board-craft-plus</c>·<c>board-craft-badge</c>(아이템 = items.json 키의 「item.」 뒤).
    /// </summary>
    public sealed class CraftScreen : IWorkScreen, IWatchScreen
    {
        readonly GameFlow flow;
        readonly int watch;
        readonly List<ChoiceAnswer> crafts = new List<ChoiceAnswer>();
        readonly Dictionary<int, Row> rows = new Dictionary<int, Row>();
        readonly Label[] have = new Label[SeatState.ApricornColors];
        UiStage stage;
        VisualElement panel;
        int painted = -1;
        int bonusFor = -1;   // 공짜 몫을 고르는 중인 아이템(picnicker R · poke_fan R — 제작 답의 Arg, rulings A39)
        bool paintedOpen;

        sealed class Row
        {
            public VisualElement Box, Icon, Glow, Flash;
            public Label Count, Plus;
            /// <summary>스튜 줄: 쉴 때 적는 글(「이번 턴 사용」 또는 빈칸)과 37행 동안 그 자리에 뜨는 효과 꼬리표 글.</summary>
            public string Rest, Effect;
            public readonly List<VisualElement> Costs = new List<VisualElement>();
            public Button Make;
        }

        /// <param name="watch">보는 좌석(결정 120 ① — 남의 제작을 내 창과 같은 꼴로 보기만, −1 = 내 창). 보기 창은 [제작]이 없고 그 좌석의 제작 단계가 끝나면 닫힌다(<see cref="GameFlow.Watch"/>).</param>
        public CraftScreen(GameFlow flow, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
        }

        public int Watched => watch;

        // 그리는 좌석(보기 창이면 그 좌석)
        int Seat => watch >= 0 ? watch : flow.Client.Seat;

        /// <summary>보기 창: 그 좌석의 제작 · 스튜 단계가 대기열에 아직 끝나지 않고 남았나.</summary>
        public bool Live
        {
            get
            {
                var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
                if (watch < 0 || seq == null) return false;
                foreach (var entry in seq.Queued)
                    foreach (var s in entry.Steps)
                        if ((s.Row == CraftBeats.Row || s.Row == StewBeats.Row) && WorkRows.SeatOf(s) == watch && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return true;
                return false;
            }
        }

        /// <summary>보기 창의 머리 꼬리표에 지금 적힌 것(만든 것 · 먹은 스튜 — 없으면 null) — 확인용.</summary>
        public string MadeText { get; private set; }
        Label made;

        public string Name => "board-craft";

        /// <summary>지금 그리는 제작 단계의 아이템(36·37행 — 없으면 −1)과 그 단계 안 시각 — 확인용.</summary>
        public int Playing { get; private set; } = -1;
        public int PlayingMs { get; private set; } = -1;

        /// <summary>제작 답을 받는 선택(조우 고르기가 열려 있으면 그것 — 고르기 전 제작, 아니면 맵 행동). 없으면 null.</summary>
        public static PendingChoice ChoiceOf(GameFlow flow)
        {
            var client = flow.Client;
            if (client == null) return null;
            return client.Choice(ChoiceKind.EncounterPick) ?? client.Choice(ChoiceKind.MapAction);
        }

        /// <summary>지금 그 아이템을 만들 수 있나(열린 선택의 합법 답 · 입력이 열림).</summary>
        public bool CanMake(int item) => Open && crafts.Exists(a => a.Target == item);

        /// <summary>입력을 받나: 보드에 있고 화면 대기열이 비어 클릭 막기 시간이 지났다.</summary>
        public bool Open => watch < 0 && stage != null && flow.BoardOpen;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            if (watch < 0)   // 보기 창은 보드를 덮지 않는다(카메라는 그대로)
            {
                var scrim = Add(page, new VisualElement(), "board-craft-scrim", "sn-party-scrim");
                scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseCraft());
            }
            panel = Add(page, Kit.Panel(stage.Skin), "board-craft-panel", "sn-craft-panel");
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
            made = null;
            rows.Clear();
        }

        public bool Draws(FxStep step) => step != null && (step.Row == CraftBeats.Row || step.Row == StewBeats.Row) && flow.Client != null && WorkRows.SeatOf(step) == Seat;

        /// <summary>다시 그린다. 내 선택이 닫혔고(턴이 끝났다) 그릴 연출도 없으면 창을 닫는다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null || flow.Client.View == null) return;
            var client = flow.Client;
            var choice = watch < 0 ? ChoiceOf(flow) : null;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (watch < 0 && choice == null && (seq == null || seq.IsEmpty))
            {
                flow.CloseCraft();
                return;
            }
            bool open = Open;
            if (painted == client.Version && paintedOpen == open)
            {
                Animate(seq);
                return;
            }
            painted = client.Version;
            paintedOpen = open;
            var ctx = flow.Ctx;
            var view = client.View;
            int seat = Seat;
            var inv = CultureInfo.InvariantCulture;
            crafts.Clear();
            if (choice != null) foreach (var a in client.Legal(choice)) if (a.Kind == AnswerKind.Craft) crafts.Add(a);

            panel.Clear();
            rows.Clear();
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var titleLine = Add(head, new VisualElement(), null, "sn-party-line");
            var titleText = Add(titleLine, Kit.Text(watch >= 0 ? KoJosa.Fix(stage.T("screen.craft.title.seat").Replace("{name}", flow.SeatName(watch))) : stage.T("screen.craft.title"), Kit.FTitle), "board-craft-title", "kit-title");
            if (watch >= 0)   // 보기 창(T6): 만든 것 · 먹은 스튜를 머리 줄에 크게 — 단계 동안만 보인다
            {
                FaceBox.Owner(flow, watch, titleText);
                made = Add(titleLine, Kit.Badge("", KitBadgeKind.Gold, Kit.F3), "board-craft-made", "sn-craft-made");
                made.pickingMode = PickingMode.Ignore;
                FxUi.Fade(made, 0f);
            }
            Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Main, () => { if (watch >= 0) flow.CloseWatch(); else flow.CloseCraft(); }), "board-craft-close", null);

            // 가진 규토리(보이는 값 — 만든 연출이 끝날 때 줄어든다)
            var haveRow = Add(panel, new VisualElement(), "board-craft-have", "sn-craft-have");
            Add(haveRow, Kit.Text(stage.T("screen.craft.have"), Kit.F2), null, "kit-muted");
            for (int c = 0; c < SeatState.ApricornColors; c++)
            {
                var color = (ApricornColor)c;
                var chip = Add(haveRow, new VisualElement(), "board-craft-have-" + color.ToString().ToLowerInvariant(), "sn-enc-chip");
                Apricorn(chip, color);
                have[c] = Add(chip, Kit.Text(ShownApricorns(flow, seat, c).ToString(inv), Kit.F2), null, null);
                stage.Tips.Hook(chip, stage.T(ColorKey(color)), null, TipSide.Below);
            }

            var cols = Add(panel, new VisualElement(), null, "sn-craft-cols");
            var left = Add(cols, new VisualElement(), null, "sn-craft-col");
            var right = Add(cols, new VisualElement(), null, "sn-craft-col");
            Section(left, ItemKind.Ball, "screen.craft.balls", false);
            Section(left, ItemKind.Stew, "screen.craft.stews", true);
            Section(right, ItemKind.Held, "screen.craft.held", false);
            var bonus = crafts.FindAll(a => a.Target == bonusFor && a.Arg >= 0);
            if (bonus.Count == 0) bonusFor = -1;
            else
            {
                // 공짜 몫 고르기: 안내 + 받을 것 단추 + [받지 않음] + [취소]
                var box = Add(panel, new VisualElement(), "board-craft-bonus", "sn-craft-bonus-box");   // 안내 한 줄 + 단추 줄(1080에서 한 줄에 다 들어가지 않는다)
                Add(box, Kit.Text(KoJosa.Fix(stage.T("screen.craft.bonus").Replace("{item}", stage.T(ctx.Items.Items[bonusFor].Key))), Kit.F2), "board-craft-bonus-text", null);
                var row = Add(box, new VisualElement(), null, "sn-party-ask");
                row.AddToClassList("sn-craft-bonus");
                for (int n = 0; n < bonus.Count; n++)
                {
                    int j = bonus[n].Arg;
                    Add(row, Kit.Button(stage.T(ctx.Items.Items[j].Key), KitButtonKind.Normal, () => PressBonus(j)), "board-craft-bonus-" + (n + 1).ToString(inv), null);
                }
                Add(row, Kit.Button(stage.T("screen.craft.bonus.none"), KitButtonKind.Normal, () => PressBonus(-1)), "board-craft-bonus-none", null);
                Add(row, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => { bonusFor = -1; painted = -1; Refresh(); }), "board-craft-bonus-cancel", null);
            }
            Animate(seq);
        }

        void Section(VisualElement col, ItemKind kind, string title, bool once)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            int seat = Seat;
            var inv = CultureInfo.InvariantCulture;
            var head = Add(col, new VisualElement(), null, "sn-party-line");
            Add(head, Kit.Text(stage.T(title), Kit.F2), null, "kit-title");
            if (once)
            {
                var badge = Kit.Badge(stage.T("screen.village.once"), KitBadgeKind.Plain, Kit.F2);
                head.Add(badge);
                stage.Tips.Hook(badge, null, stage.T("screen.craft.stew.tip"), TipSide.Below, reading: true);
            }
            var items = ctx.Items.Items;
            foreach (int i in Order(ctx, kind))
            {
                var d = items[i];
                int item = i;
                string id = Short(d);
                var row = new Row();
                row.Box = Add(col, new VisualElement(), "board-craft-row-" + id, "sn-craft-row");
                row.Glow = Add(row.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-glow");
                var iconBox = Add(row.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-iconbox");
                row.Icon = Icon(iconBox, IconKey(d), "sn-icon");
                row.Flash = Add(iconBox, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-flash");
                row.Plus = Add(iconBox, Kit.Text(Signed(1), Kit.F2), "board-craft-plus-" + id, "sn-craft-plus");
                row.Plus.pickingMode = PickingMode.Ignore;
                var name = Add(row.Box, Kit.Text(stage.T(d.Key), Kit.F2), null, "sn-craft-name");
                name.pickingMode = PickingMode.Ignore;
                var cost = Add(row.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-cost");
                foreach (var color in TrainerRules.Recipe(view, ctx, seat, d)) row.Costs.Add(Apricorn(cost, color, "sn-craft-apricorn"));   // 엔진이 받는 재료(ace_trainer B 배틀 아이템은 색마다 하나 — 감사 G1-2)
                if (kind == ItemKind.Stew)
                {
                    bool used = Used(view, seat, UsageKind.Stew, item);
                    // 37행 효과 꼬리표는 이 칸에 뜬다(다른 요소를 덮지 않게 — 줄 안 같은 자리)
                    row.Rest = used ? stage.T("screen.craft.stew.used") : "";
                    row.Effect = stage.T("screen.craft.effect." + Short(d));
                    row.Count = Add(row.Box, Kit.Text(row.Rest, Kit.F1), "board-craft-count-" + id, "sn-craft-count");
                }
                else row.Count = Add(row.Box, Kit.Text(Times + Owned(d, item).ToString(inv), Kit.F1), "board-craft-count-" + id, "sn-craft-count");
                if (watch < 0)   // 보기 창에는 [제작]이 없다(보기 전용)
                {
                    row.Make = Add(row.Box, Kit.Button(stage.T("screen.craft.make"), KitButtonKind.Normal, () => PressMake(item)), "board-craft-make-" + id, "sn-craft-make");
                    row.Make.SetEnabled(CanMake(item));
                }
                stage.Tips.Hook(row.Box, stage.T(d.Key), stage.TOrNull(d.Key + ".text"), TipSide.Above, reading: true);
                rows[item] = row;
            }
        }

        /// <summary>그 종류의 레시피 아이템(볼은 등급 순 — 몬스터볼 → 키스톤볼, 나머지는 데이터 순).</summary>
        public static List<int> Order(Ctx ctx, ItemKind kind)
        {
            var items = ctx.Items.Items;
            var order = new List<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].Kind == kind && items[i].Recipe != null) order.Add(i);
            if (kind == ItemKind.Ball) order.Sort((a, b) => CraftRules.BallOf(ctx, a).CompareTo(CraftRules.BallOf(ctx, b)));
            return order;
        }

        /// <summary>
        /// 조합표(결정 191 — 정비 판의 [조합표]): 이 창의 레시피 줄과 같은 꼴(그림 · 이름 · 재료 규토리 — 그 좌석이 내는 재료)을 보기 전용으로 into에 단다.
        /// 누를 단추 · 가진 수 · 연출은 없고, 효과 문구는 줄의 마우스 오버. 요소 이름 <c>board-party-recipe-&lt;아이템&gt;</c>. 돌려주는 값 = 줄 수.
        /// </summary>
        public static int Book(UiStage stage, GameFlow flow, int seat, VisualElement into)
        {
            var cols = Add(into, new VisualElement(), null, "sn-craft-cols");
            var left = Add(cols, new VisualElement(), null, "sn-craft-col");
            var right = Add(cols, new VisualElement(), null, "sn-craft-col");
            return BookSection(stage, flow, seat, left, ItemKind.Ball, "screen.craft.balls")
                 + BookSection(stage, flow, seat, left, ItemKind.Stew, "screen.craft.stews")
                 + BookSection(stage, flow, seat, right, ItemKind.Held, "screen.craft.held");
        }

        static int BookSection(UiStage stage, GameFlow flow, int seat, VisualElement col, ItemKind kind, string title)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var head = Add(col, new VisualElement(), null, "sn-party-line");
            Add(head, Kit.Text(stage.T(title), Kit.F2), null, "kit-title");
            var order = Order(ctx, kind);
            foreach (int i in order)
            {
                var d = ctx.Items.Items[i];
                var box = Add(col, new VisualElement(), "board-party-recipe-" + Short(d), "sn-craft-row");
                var iconBox = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-iconbox");
                Icon(iconBox, IconKey(d), "sn-icon");
                Add(box, Kit.Text(stage.T(d.Key), Kit.F2), null, "sn-craft-name").pickingMode = PickingMode.Ignore;
                var cost = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-cost");
                foreach (var color in TrainerRules.Recipe(view, ctx, seat, d)) Apricorn(cost, color, "sn-craft-apricorn");
                stage.Tips.Hook(box, stage.T(d.Key), stage.TOrNull(d.Key + ".text"), TipSide.Above, reading: true);
            }
            return order.Count;
        }

        /// <summary>그 좌석이 이번 턴에 그 기록(행동·스튜)을 남겼나(보기의 공개 기록).</summary>
        public static bool Used(GameState view, int seat, UsageKind kind, int key = -1)
        {
            foreach (var u in view.Used)
                if (u.Seat == seat && u.Kind == kind && u.Key == key && u.Scope == view.TurnId) return true;
            return false;
        }

        /// <summary>items.json 키의 「item.」 뒤(요소 이름 · 스튜 효과 꼬리표 키).</summary>
        public static string Short(ItemDef d) => d.Key.StartsWith("item.", StringComparison.Ordinal) ? d.Key.Substring("item.".Length) : d.Key;

        // 가진 수(보이는 값): 볼 = 볼 칸, 지닌물건 = 가방 + 지닌 것
        int Owned(ItemDef d, int item)
        {
            var view = flow.Client.View;
            int seat = Seat;
            var st = view.Seats[seat];
            int value;
            ShownKey key;
            if (d.Kind == ItemKind.Ball)
            {
                int ball = CraftRules.BallOf(flow.Ctx, item);
                value = st.Balls[ball];
                key = ShownKey.Resource(seat, ResKind.Ball, ball);
            }
            else
            {
                value = 0;
                foreach (int b in st.Bag) if (b == item) value++;
                key = ShownKey.Resource(seat, ResKind.Item, item);
            }
            return flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(key, value) : value;
        }

        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || panel == null) return;
            if (Open != paintedOpen) Refresh();
            else Animate(seq);
        }

        // 36·37행의 그 순간: 만든 줄만 움직이고 나머지는 제자리. 가진 수는 보이는 값(단계 끝에 착지)
        void Animate(FxSequencer seq)
        {
            var inv = CultureInfo.InvariantCulture;
            int seat = Seat;
            for (int c = 0; c < have.Length; c++) if (have[c] != null) have[c].text = ShownApricorns(flow, seat, c).ToString(inv);
            var craft = WorkRows.Playing(seq, CraftBeats.Row, out int craftMs);
            var stew = WorkRows.Playing(seq, StewBeats.Row, out int stewMs);
            var made = Draws(craft) ? WorkRows.EventOf<Crafted>(craft) : null;
            var eaten = Draws(stew) ? WorkRows.EventOf<StewEaten>(stew) : null;
            Playing = made != null ? made.ItemKey : eaten != null ? eaten.ItemKey : -1;
            PlayingMs = made != null ? craftMs : eaten != null ? stewMs : -1;
            ShowMade(made, eaten, craft, stew, craftMs, stewMs);
            foreach (var kv in rows)
            {
                var row = kv.Value;
                var d = flow.Ctx.Items.Items[kv.Key];
                if (d.Kind != ItemKind.Stew) row.Count.text = Times + Owned(d, kv.Key).ToString(inv);
                bool here = made != null && made.ItemKey == kv.Key;
                int ms = here ? craftMs : -1;
                FxUi.Fade(row.Glow, here ? CraftBeats.Glow(ms) : 0f);
                FxUi.Fade(row.Flash, here ? CraftBeats.Flash(ms) : 0f);
                float pop = here ? CraftBeats.Pop(ms) : 1f;
                bool ate = eaten != null && eaten.ItemKey == kv.Key;
                float bounce = ate ? StewBeats.Bounce(stewMs) : 0f;
                FxUi.Scale(row.Icon, pop, pop);
                FxUi.Move(row.Icon, 0f, -bounce * BouncePx);
                FxUi.Fade(row.Plus, here && d.Kind != ItemKind.Stew ? CraftBeats.Plus(ms) : 0f);
                FxUi.Move(row.Plus, 0f, here ? -CraftBeats.PlusRise(ms) * RisePx : 0f);
                for (int i = 0; i < row.Costs.Count; i++)
                {
                    float t = here ? CraftBeats.Suck(ms, i) : 0f;
                    var icon = row.Costs[i];
                    FxUi.Move(icon, -t * SuckPx, 0f);   // 완성품 쪽(왼쪽)으로 — 배치에 기대지 않는 고정 거리(한 번에 보내도 나눠 보내도 같은 그림)
                    FxUi.Scale(icon, 1f - 0.7f * t, 1f - 0.7f * t);
                    FxUi.Fade(icon, 1f - t);
                }
                if (row.Effect != null)
                {
                    float badge = ate ? StewBeats.Badge(stewMs) : 0f;
                    bool on = badge > 0f;
                    row.Count.text = on ? row.Effect : row.Rest;
                    row.Count.EnableInClassList("sn-craft-badge", on);
                    float s = on ? StewBeats.BadgeScale(stewMs) : 1f;
                    FxUi.Scale(row.Count, s, s);
                }
            }
        }

        // 보기 창의 머리 꼬리표(T6 — 남이 무엇을 만들었는지 · 스튜는 뚜렷이): 만든 단계 = 「○○ 제작!」 · 스튜 효과 단계 = 그 스튜 · 효과 글(스튜는 붉은 바탕).
        // 단계 처음에 떠오르고 끝에 사라진다(좌석 칩 꼬리표와 같은 박자 — OtherActs.Alpha)
        void ShowMade(Crafted c, StewEaten e, FxStep craft, FxStep stew, int craftMs, int stewMs)
        {
            if (made == null) return;
            var items = flow.Ctx.Items.Items;
            string text = null;
            float alpha = 0f;
            bool isStew = false;
            if (c != null && c.ItemKey >= 0 && c.ItemKey < items.Count)
            {
                text = KoJosa.Fix(stage.T("screen.craft.made").Replace("{item}", stage.T(items[c.ItemKey].Key)));
                alpha = OtherActs.Alpha(craftMs, craft.Ms, out _);
                isStew = items[c.ItemKey].Kind == ItemKind.Stew;
            }
            else if (e != null && e.ItemKey >= 0 && e.ItemKey < items.Count)
            {
                text = stage.T(items[e.ItemKey].Key) + " · " + stage.T("screen.craft.effect." + Short(items[e.ItemKey]));
                alpha = OtherActs.Alpha(stewMs, stew.Ms, out _);
                isStew = true;
            }
            if (text != null) made.text = text;
            MadeText = text;
            made.EnableInClassList("sn-craft-made-stew", isStew);
            FxUi.Fade(made, alpha);
        }

        /// <summary>스튜 그릇이 튀는 높이 · 「+1」이 떠오르는 높이(패널 px — 720 벌).</summary>
        public const float BouncePx = 8f, RisePx = 12f, SuckPx = 24f;

        // ── 입력 ──

        /// <summary>[제작]: 그 아이템(아이템 색인)의 제작 답을 낸다. 받아들여졌으면 true(창은 그대로 — 만든 연출이 이 창에서 돈다).</summary>
        public bool PressMake(int item)
        {
            if (!CanMake(item)) return false;
            if (bonusFor != item && crafts.Exists(a => a.Target == item && a.Arg >= 0))
            {
                bonusFor = item;   // 공짜로 더 받을 것을 먼저 고른다(PressBonus)
                painted = -1;
                Refresh();
                return true;
            }
            var choice = ChoiceOf(flow);
            var answer = crafts.Find(a => a.Target == item && a.Arg < 0);
            if (choice == null || answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>공짜 몫을 고르는 중인 아이템(없으면 −1).</summary>
        public int BonusFor => bonusFor;

        /// <summary>공짜 몫 단추: 그 아이템(−1 = 받지 않음)을 붙여 제작 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressBonus(int bonus)
        {
            var choice = ChoiceOf(flow);
            var answer = crafts.Find(a => a.Target == bonusFor && a.Arg == bonus);
            if (!Open || choice == null || answer == null) return false;
            bonusFor = -1;
            painted = -1;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>items.json 키로 그 아이템 색인(없으면 −1) — 확인용.</summary>
        public int ItemOf(string key) => flow.Ctx.Items.Ids.IndexOf(key);
    }
}
