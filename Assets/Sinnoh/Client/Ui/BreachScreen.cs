using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S58 왜곡 균열 카드 창(04 §2 — M6 「UB 화면 묶음 2」, 사용자 결정 84 M3 다): 보드를 어둡게 덮는 가운데 창. 두 꼴이 같은 창을 쓴다.
    /// <list type="bullet">
    /// <item>붙이기(턴 시작의 동시 선택 Simultaneous/BreachAttach — rulings A52): 왼쪽 큰 카드(그림 · 이름 · 「반드시 붙임」 · 효과) · 가운데 「붙일 기술 칸」(파티마다 기술 칸 둘 —
    /// 붙일 수 있는 칸을 누르면 고름) · 아래 모두의 고르기 현황(얼굴 상자 · 「고르는 중」/「완료」) · [보관](pokemon_ranger B — 결정 84 U11, 레인저 파랑) · [붙이기].</item>
    /// <item>버리기·옮기기(UB 격파 뒤 — 같은 선택의 Subject = <see cref="BreachRules.Release"/>, 결정 84 U10 나): 붙은 카드를 누르면 고르고 → [버리기], 또는 다른 포켓몬의 빈 기술 칸을 더 누르면 → [옮기기].
    /// 왼쪽에는 손·보관 카드(누르면 고름 → [버리기]) · [안 함].</item>
    /// </list>
    /// 답한 뒤에는 묶음이 다 답할 때까지 「다른 플레이어를 기다리는 중」으로 남고, 묶음이 끝나면 닫힌다(<see cref="GameFlow"/>의 SyncBreach). 닫는 단추는 없다(Esc도 받지 않는다 — 답해야 하는 화면).
    /// 요소 이름: <c>board-breach</c>·<c>board-breach-panel</c>·<c>board-breach-card</c>·<c>board-breach-slot-&lt;포켓몬&gt;-&lt;칸&gt;</c>·<c>board-breach-loose-&lt;n&gt;</c>·<c>board-breach-seat-&lt;n&gt;</c>·
    /// <c>board-breach-store</c>·<c>board-breach-go</c>·<c>board-breach-discard</c>·<c>board-breach-move</c>·<c>board-breach-skip</c>·<c>board-breach-clock</c>·<c>board-breach-wait</c>.
    /// </summary>
    public sealed class BreachScreen : IUiScreen
    {
        const string CardHead = "distortion_breach.";
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel, clock, clockBar;
        Label clockLeft;
        MonArt mons;
        ChoiceAnswer picked;          // 붙이기: 고른 칸 · 버리기·옮기기: 고른 카드(붙은 카드 또는 손·보관 카드)
        int toMon = -1, toSlot = -1;  // 버리기·옮기기: 옮길 칸
        int pickedFor = -1;           // 고른 것이 속한 선택 Id(선택이 바뀌면 푼다)

        public BreachScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-breach";

        /// <summary>내가 아직 답하지 않은 균열 카드 선택(붙이기 · 버리기·옮기기). 없으면 null.</summary>
        public static PendingChoice ChoiceOf(Sinnoh.Rules.Session.SeatClient client)
        {
            var view = client != null ? client.View : null;
            if (view == null) return null;
            foreach (var c in view.Pending)
                if (c.Seat == client.Seat && !c.Answered && Owns(c)) return c;
            return null;
        }

        /// <summary>이 창이 받는 선택인가.</summary>
        public static bool Owns(PendingChoice c) => c != null && c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.BreachAttach;

        /// <summary>균열 카드 묶음이 열려 있나(내가 답했어도 남이 고르는 중이면 참).</summary>
        public static bool Active(GameState view)
        {
            if (view == null) return false;
            foreach (var c in view.Pending) if (Owns(c)) return true;
            return false;
        }

        /// <summary>묶음의 꼴: 버리기·옮기기면 true.</summary>
        public static bool Releasing(GameState view)
        {
            if (view == null) return false;
            foreach (var c in view.Pending) if (Owns(c)) return c.Subject == BreachRules.Release;
            return false;
        }

        /// <summary>고른 답(확인용).</summary>
        public ChoiceAnswer Picked => picked;

        /// <summary>접혔나(결정 87 V19 ① — 머리 줄만 남은 띠: 보드의 말이 보인다). [접기]·[펼치기] <c>board-breach-fold</c>.</summary>
        public bool Folded { get; private set; }
        VisualElement scrim;

        public bool ToggleFold()
        {
            if (stage == null) return false;
            Folded = !Folded;
            Refresh();
            flow.Refold();   // 보드 위에 연 창이 접힌 동안 보드 카메라가 움직인다(T21)
            return true;
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            mons = MonArt.Load();
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 · 어둠 밖(위 띠 · 아래 판)의 입력을 먹지 않는다(T35) — 접으면 어둠도 걷혀 결과 창 [확인]이 눌린다(T22)
            scrim = Add(page, new VisualElement(), "board-breach-scrim", "sn-breach-scrim");
            panel = Add(page, Kit.Panel(stage.Skin), "board-breach-panel", "sn-breach-panel");
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
            clock = clockBar = null;
            clockLeft = null;
        }

        /// <summary>보기·입력이 바뀌었다: 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return;
            var ctx = flow.Ctx;
            var choice = ChoiceOf(client);
            bool release = Releasing(view);
            var legal = choice != null ? client.Legal(choice) : new List<ChoiceAnswer>();
            if (choice == null || choice.Id != pickedFor)
            {
                picked = choice != null && !release ? DefaultPick(view, legal) : null;
                toMon = toSlot = -1;
                pickedFor = choice != null ? choice.Id : -1;
            }
            bool open = choice != null && flow.AnswerOpen;
            stage.Tips.Hide();
            panel.Clear();

            var head = Add(panel, new VisualElement(), null, "sn-breach-head");
            Add(head, Kit.Text(stage.T(release ? "screen.breach.release.title" : "screen.breach.title"), Kit.FTitle), "board-breach-title", "kit-title");
            Add(head, Kit.Badge(stage.T("screen.breach.together"), KitBadgeKind.Plain, Kit.F2), null, null);
            Add(head, new VisualElement(), null, "sn-breach-grow");
            clock = Add(head, new VisualElement(), "board-breach-clock", "sn-breach-clock");
            var track = Add(clock, new VisualElement(), null, "sn-breach-track");
            clockBar = Add(track, new VisualElement(), null, "sn-breach-bar");
            clockLeft = Add(clock, Kit.Text("", Kit.F2), null, null);
            Tick();
            Add(head, Kit.Button(stage.T(Folded ? "screen.common.unfold" : "screen.common.fold"), KitButtonKind.Normal, () => ToggleFold()), "board-breach-fold", null);
            if (scrim != null) scrim.style.display = Folded ? DisplayStyle.None : DisplayStyle.Flex;
            if (Folded) return;   // 띠만 — 다시 누르면 펼친다

            var body = Add(panel, new VisualElement(), null, "sn-breach-body");
            var left = Add(body, new VisualElement(), "board-breach-card", "sn-breach-card");
            if (!release)
            {
                int card = choice != null ? choice.Options[0] : BreachRules.FirstInHand(view, client.Seat);
                if (card >= 0) CardFace(left, card);
            }
            else Loose(left, legal, open);

            var slots = Add(body, new VisualElement(), null, "sn-breach-slots");
            var sub = Add(slots, Kit.Wrapped(stage.T(release ? "screen.breach.release.text" : "screen.breach.slots"), Kit.F2), null, "kit-title");
            sub.style.whiteSpace = WhiteSpace.Normal;
            foreach (int p in view.Seats[client.Seat].Party)
            {
                if (p < 0) continue;
                var row = Add(slots, new VisualElement(), null, "sn-breach-mon");
                string key = ctx.Cards.Pokemon[view.Pokemon[p].Def].Key;
                var art = Add(row, new VisualElement(), null, "sn-breach-mon-art");
                if (mons != null && mons.Has(key)) art.Add(mons.Front(key));
                var col = Add(row, new VisualElement(), null, "sn-breach-mon-col");
                col.Add(Kit.Text(BoardHud.ShortName(stage.T("pokemon." + key)), Kit.F2));
                var line = Add(col, new VisualElement(), null, "sn-breach-chips");
                for (int slot = 0; slot < PokemonState.MoveSlots; slot++) SlotChip(line, view, legal, p, slot, release, open);
            }

            var foot = Add(panel, new VisualElement(), null, "sn-breach-foot");
            var seats = Add(foot, new VisualElement(), null, "sn-breach-seats");
            foreach (var c in view.Pending)
            {
                if (!Owns(c)) continue;
                string n = (c.Seat + 1).ToString(CultureInfo.InvariantCulture);
                var chip = Add(seats, new VisualElement(), "board-breach-seat-" + n, "sn-breach-seat");
                var face = Add(chip, FaceBox.Make(), "board-breach-seat-" + n + "-face", null);
                FaceBox.Paint(face, c.Seat, flow.SeatColor(c.Seat), flow.Protagonist(c.Seat));
                stage.Tips.Hook(chip, flow.SeatName(c.Seat), null, TipSide.Above, clear: panel);   // 좌석 현황은 한 줄(결정 87 V19 ①) — 이름은 마우스 오버
                chip.Add(Kit.Badge(stage.T(c.Answered ? "screen.breach.done" : "screen.breach.picking"), c.Answered ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F2));
            }
            var buttons = Add(foot, new VisualElement(), null, "sn-breach-buttons");
            if (choice == null)
            {
                Add(buttons, Kit.Text(stage.T("screen.breach.wait"), Kit.F2), "board-breach-wait", "kit-muted");
                return;
            }
            if (!release)
            {
                if (legal.Count > 0 && legal[0].Kind == AnswerKind.Decline)
                {
                    var store = Add(buttons, Kit.Button(stage.T("screen.breach.store"), KitButtonKind.Normal, () => PressStore()), "board-breach-store", "sn-breach-ranger");
                    store.SetEnabled(open);
                    if (open) stage.Tips.Hook(store, stage.T("screen.breach.store.title"), stage.T("screen.breach.store.tip"), TipSide.Above, reading: true, clear: panel);
                }
                var go = Add(buttons, Kit.Button(stage.T("screen.breach.attach"), KitButtonKind.Main, () => PressAttach()), "board-breach-go", null);
                go.SetEnabled(open && picked != null);
                return;
            }
            var discard = Add(buttons, Kit.Button(stage.T("screen.breach.discard"), KitButtonKind.Normal, () => PressDiscard()), "board-breach-discard", null);
            discard.SetEnabled(open && picked != null && toMon < 0);
            var move = Add(buttons, Kit.Button(stage.T("screen.breach.move"), KitButtonKind.Main, () => PressMove()), "board-breach-move", null);
            move.SetEnabled(open && MoveAnswer(legal) != null);
            var skip = Add(buttons, Kit.Button(stage.T("screen.breach.skip"), KitButtonKind.Normal, () => PressSkip()), "board-breach-skip", null);
            skip.SetEnabled(open);
            if (open) stage.Tips.Hook(skip, null, stage.T("screen.breach.skip.tip"), TipSide.Above, reading: true, clear: panel);
        }

        /// <summary>프레임마다: 남은 시간 막대 · 초(시계가 없는 판 · 답한 뒤면 숨긴다).</summary>
        public void Tick()
        {
            if (stage == null || clock == null) return;
            var choice = ChoiceOf(flow.Client);
            int total = 0;
            int left = choice != null ? flow.ClockLeftMs(choice, out total) : -1;
            clock.style.display = left >= 0 && total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (left < 0 || total <= 0) return;
            clockBar.style.width = Length.Percent(100f * left / total);
            clockLeft.text = stage.T("screen.prompt.left").Replace("{n}", ((left + 999) / 1000).ToString(CultureInfo.InvariantCulture));
        }

        // 시간 초과 기본 답과 같은 칸을 먼저 골라 둔다(빈 기술 칸 → 첫 칸 — BreachRules.Default). 보관이 첫 답이면 고르지 않는다
        static ChoiceAnswer DefaultPick(GameState view, List<ChoiceAnswer> legal)
        {
            if (legal.Count > 0 && legal[0].Kind == AnswerKind.Decline) return null;
            ChoiceAnswer first = null;
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick) continue;
                if (view.Pokemon[a.Target].Moves[a.Arg] < 0) return a;
                if (first == null) first = a;
            }
            return first;
        }

        void CardFace(VisualElement parent, int card)
        {
            var ctx = flow.Ctx;
            string key = card < ctx.Decks.CardIds.Count ? ctx.Decks.CardIds[card] : null;
            if (key == null) return;
            var art = Add(parent, new VisualElement(), "board-breach-card-art", "sn-breach-art-big");
            var tex = key.StartsWith(CardHead, StringComparison.Ordinal) ? GenAssets.Load<Texture2D>(GenAssets.BreachArt + key.Substring(CardHead.Length) + ".png") : null;
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            Add(parent, Kit.Text(stage.T(key), Kit.F3), "board-breach-card-name", "kit-title");
            var tags = Add(parent, new VisualElement(), null, "sn-breach-tags");
            tags.Add(Kit.Badge(stage.T("screen.breach.tag"), KitBadgeKind.Red, Kit.F2));
            tags.Add(Kit.Badge(stage.T("screen.breach.must"), KitBadgeKind.Plain, Kit.F2));
            var text = Add(parent, Kit.Wrapped(MonParts.BreachText(stage, key), Kit.F2), "board-breach-card-text", "sn-breach-text");
            text.style.whiteSpace = WhiteSpace.Normal;
        }

        // 버리기·옮기기의 왼쪽: 손·보관 카드(누르면 고른다 → [버리기])
        void Loose(VisualElement parent, List<ChoiceAnswer> legal, bool open)
        {
            Add(parent, Kit.Text(stage.T("screen.breach.loose"), Kit.F2), null, "kit-title");
            int n = 0;
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick || a.Target != BreachRules.HandTarget) continue;
                var answer = a;
                string id = (++n).ToString(CultureInfo.InvariantCulture);
                var chip = Add(parent, MonParts.BreachRow(stage, flow.Ctx, a.Arg, TipSide.Below), "board-breach-loose-" + id, "sn-breach-chip");
                chip.AddToClassList("sn-breach-chip-lit");
                if (picked != null && picked.SameAs(a)) chip.AddToClassList("sn-breach-chip-picked");
                stage.OnTap(chip, SoundKeys.Select, e => { picked = answer; toMon = toSlot = -1; Refresh(); }, () => open);
            }
            if (n == 0) Add(parent, Kit.Text(stage.T("screen.breach.loose.none"), Kit.F2), null, "kit-muted");
        }

        // 기술 칸 하나: 붙은 균열 카드(카드 그림 · 이름) / 배운 기술 / 빈 칸. 누를 수 있는 칸(합법 답)은 테두리, 고른 칸은 금색
        void SlotChip(VisualElement parent, GameState view, List<ChoiceAnswer> legal, int p, int slot, bool release, bool open)
        {
            var mon = view.Pokemon[p];
            int card = mon.Breach[slot], move = mon.Moves[slot];
            VisualElement chip;
            if (card >= 0) chip = MonParts.BreachRow(stage, flow.Ctx, card, TipSide.Below);
            else if (move >= 0) chip = MonParts.MoveRow(stage, flow.Ctx, move, false, TipSide.Below, panel);
            else { chip = new VisualElement(); chip.Add(Kit.Text(stage.T("screen.breach.empty"), Kit.F2)); }
            chip.name = SlotName(p, slot);
            chip.AddToClassList("sn-breach-chip");
            parent.Add(chip);
            Action press = null;
            bool on = false;
            var direct = legal.Find(x => x.Kind == AnswerKind.Pick && x.Target == p && x.Arg == slot && x.Values.Count == 0);
            if (direct != null && (!release || card >= 0))
            {
                on = picked != null && picked.SameAs(direct);
                press = () => { picked = direct; toMon = toSlot = -1; Refresh(); };
            }
            else if (release && card < 0 && MoveTo(legal, p, slot) != null)
            {
                on = toMon == p && toSlot == slot;
                press = () => { toMon = p; toSlot = slot; Refresh(); };
            }
            if (press == null) return;
            chip.AddToClassList("sn-breach-chip-lit");
            if (on) chip.AddToClassList("sn-breach-chip-picked");
            stage.OnTap(chip, SoundKeys.Select, e => press(), () => open);
        }

        static string SlotName(int p, int slot) => "board-breach-slot-" + p.ToString(CultureInfo.InvariantCulture) + "-" + slot.ToString(CultureInfo.InvariantCulture);

        // 고른 붙은 카드를 그 칸으로 옮기는 답(없으면 null)
        ChoiceAnswer MoveTo(List<ChoiceAnswer> legal, int p, int slot) =>
            picked == null || picked.Target < 0 ? null : legal.Find(x => x.Kind == AnswerKind.Pick && x.Target == picked.Target && x.Arg == picked.Arg && x.Values.Count == 2 && x.Values[0] == p && x.Values[1] == slot);

        ChoiceAnswer MoveAnswer(List<ChoiceAnswer> legal) => toMon < 0 ? null : MoveTo(legal, toMon, toSlot);

        // ── 누르기(길잡이·시험도 부른다) ──

        /// <summary>그 포켓몬의 그 기술 칸을 누른다(붙이기 = 고름 · 버리기·옮기기 = 붙은 카드 고름 / 옮길 칸 고름). 누를 수 있었으면 true.</summary>
        public bool PressSlot(int pokemon, int slot)
        {
            var e = panel != null ? panel.Q(SlotName(pokemon, slot)) : null;
            if (e == null || !e.ClassListContains("sn-breach-chip-lit") || !flow.AnswerOpen) return false;
            var choice = ChoiceOf(flow.Client);
            if (choice == null) return false;
            var legal = flow.Client.Legal(choice);
            var direct = legal.Find(x => x.Kind == AnswerKind.Pick && x.Target == pokemon && x.Arg == slot && x.Values.Count == 0);
            if (direct != null && (!Releasing(flow.Client.View) || flow.Client.View.Pokemon[pokemon].Breach[slot] >= 0)) { picked = direct; toMon = toSlot = -1; }
            else { toMon = pokemon; toSlot = slot; }
            Refresh();
            return true;
        }

        /// <summary>버리기·옮기기: 손·보관의 n번째 카드(1부터)를 고른다.</summary>
        public bool PressLoose(int n)
        {
            var choice = ChoiceOf(flow.Client);
            if (choice == null) return false;
            var loose = flow.Client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Pick && a.Target == BreachRules.HandTarget);
            if (n < 1 || n > loose.Count) return false;
            picked = loose[n - 1];
            toMon = toSlot = -1;
            Refresh();
            return true;
        }

        public bool PressAttach() => picked != null && Submit(picked);

        public bool PressStore() => Submit(Decline());

        public bool PressDiscard() => picked != null && toMon < 0 && Submit(picked);

        public bool PressMove()
        {
            var choice = ChoiceOf(flow.Client);
            var a = choice != null ? MoveAnswer(flow.Client.Legal(choice)) : null;
            return a != null && Submit(a);
        }

        public bool PressSkip() => Submit(Decline());

        ChoiceAnswer Decline()
        {
            var choice = ChoiceOf(flow.Client);
            return choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline) : null;
        }

        bool Submit(ChoiceAnswer answer)
        {
            var choice = ChoiceOf(flow.Client);
            if (choice == null || answer == null || !flow.AnswerOpen) return false;
            stage?.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
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
