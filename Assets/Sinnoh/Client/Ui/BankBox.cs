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
    /// S24 은행상회 거래(04 §3-4 「마을」 · §8-8 18행, rulings A4 — BankRules): 마을 창(<see cref="VillageScreen"/>)의 셋째 칸. 덱 맨 위 거래 카드의 두 줄(낼 규토리 → 받을 규토리)마다 [거래],
    /// 두 줄을 다 할 수 있으면 [두 줄 모두](어느 순서로든 되면 된다 — 엔진이 순서를 고른다). 턴당 1회. 규칙을 다시 세지 않는다 — 단추는 합법 은행상회 답 그대로, 까닭만 보기에서 고른다.
    /// 거래하면 이 칸이 18행을 그린다: 고른 줄이 빛남 → 낸 규토리가 빨려 듦 → 받은 규토리가 튀어나옴 → 순증감 → 카드가 흔들리며 섞이고 새 맨 위 카드로 뒤집힘.
    /// 그동안 카드는 거래한 카드(이벤트의 카드)를 보이고, 뒤집기 가운데부터 새 맨 위 카드다.
    /// 요소 이름: <c>board-village-bank</c>·<c>board-village-bank-card</c>·<c>board-village-bank-row-&lt;0|1&gt;</c>·<c>board-village-bank-trade-&lt;0|1&gt;</c>·<c>board-village-bank-both</c>·<c>board-village-bank-why</c>·<c>board-village-bank-net</c>.
    /// </summary>
    public sealed class BankBox
    {
        readonly GameFlow flow;
        readonly List<ChoiceAnswer> trades = new List<ChoiceAnswer>();
        readonly List<VisualElement>[] gives = { new List<VisualElement>(), new List<VisualElement>() };
        readonly List<VisualElement>[] gets = { new List<VisualElement>(), new List<VisualElement>() };
        readonly VisualElement[] glows = new VisualElement[2];
        UiStage stage;
        VisualElement box, card, net;
        int paintedCard = -2, paintedStep = -1;
        bool open;

        readonly int watch;

        /// <param name="watch">보는 좌석(결정 120 ① — 보기 창, −1 = 내 칸): 단추는 꺼진 채 그 좌석의 거래 단계를 그린다.</param>
        public BankBox(GameFlow flow, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
        }

        int Seat => watch >= 0 ? watch : flow.Client.Seat;

        /// <summary>칸이 보여 주는 거래 카드(cards 색인 — 거래 연출 동안은 거래한 카드) · 못 쓰는 까닭의 키(쓸 수 있으면 null).</summary>
        public int ShownCard { get; private set; } = -1;
        public string Why { get; private set; }

        /// <summary>지금 그 답(위 줄 · 아래 줄 · 두 줄 — <see cref="BankRules"/>의 Top·Bottom·Both)을 낼 수 있나.</summary>
        public bool CanTrade(int rows) => open && flow.BoardOpen && trades.Exists(a => a.Target == rows);

        /// <summary>마을 창이 다시 그릴 때 칸을 새로 짓는다.</summary>
        public void Paint(UiStage stage, VisualElement panel, List<ChoiceAnswer> legal, bool open)
        {
            this.stage = stage;
            this.open = open;
            trades.Clear();
            foreach (var a in legal) if (a.Kind == AnswerKind.Ginkgo) trades.Add(a);
            var view = flow.Client.View;
            int seat = Seat;
            Why = watch >= 0 ? null : trades.Count > 0 ? null : CraftScreen.Used(view, seat, UsageKind.Ginkgo) ? "screen.village.used" : BankRules.Card(view) < 0 ? "screen.bank.empty" : "screen.bank.cannot";
            box = Add(panel, new VisualElement(), "board-village-bank", "sn-village-box");
            var head = Add(box, new VisualElement(), null, "sn-village-row");
            var name = Add(head, new VisualElement(), null, "sn-party-line");
            Add(name, Kit.Text(stage.T("screen.bank.title"), Kit.F2), null, "kit-title");
            var once = Kit.Badge(stage.T("screen.village.once"), KitBadgeKind.Plain, Kit.F2);
            name.Add(once);
            stage.Tips.Hook(once, null, stage.T("screen.board.used.tip"), TipSide.Below, reading: true);
            var both = Add(head, Kit.Button(stage.T("screen.bank.both"), KitButtonKind.Normal, () => Press(BankRules.Both)), "board-village-bank-both", null);
            both.SetEnabled(CanTrade(BankRules.Both));
            // 남의 고르기 보기(P09): 은행상회 줄(덱 맨 위 거래 카드 — 공개)에 마우스를 올린 것을 방에 알린다
            both.RegisterCallback<PointerEnterEvent>(e => Hover(BankRules.Both));
            both.RegisterCallback<PointerLeaveEvent>(e => Hover(-1));
            if (open) stage.Tips.Hook(both, null, stage.T("screen.bank.both.tip"), TipSide.Above, reading: true);
            Add(box, Kit.Text(stage.T("screen.bank.text"), Kit.F2), null, "kit-muted");
            card = Add(box, new VisualElement(), "board-village-bank-card", "sn-bank-card");
            net = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, "board-village-bank-net", "sn-bank-net");
            if (Why != null) Add(box, Kit.Text(stage.T(Why), Kit.F2), "board-village-bank-why", "sn-village-why");
            paintedCard = -2;
            Animate(flow.Fx != null ? flow.Fx.Sequencer : null);
        }

        public void Close()
        {
            stage = null;
            box = card = net = null;
        }

        // 거래 카드의 두 줄
        void PaintCard(int cardId, IReadOnlyList<GinkgoTraded> traded)
        {
            card.Clear();
            for (int r = 0; r < 2; r++)
            {
                gives[r].Clear();
                gets[r].Clear();
                glows[r] = null;
            }
            var ctx = flow.Ctx;
            var t = cardId >= 0 && cardId < ctx.Decks.Cards.Count ? ctx.Decks.Cards[cardId].Trades : null;
            if (t == null) return;
            for (int r = 0; r < 2 && r < t.Count; r++)
            {
                int rowId = r;
                var line = Add(card, new VisualElement(), "board-village-bank-row-" + r, "sn-bank-row");
                line.RegisterCallback<PointerEnterEvent>(e => Hover(rowId));
                line.RegisterCallback<PointerLeaveEvent>(e => Hover(-1));
                glows[r] = Add(line, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-craft-glow");
                var give = Add(line, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-bank-side");
                foreach (var c in t[r].Give) gives[r].Add(Apricorn(give, c));
                Add(line, Kit.Text(Arrow, Kit.F2), null, "sn-bank-arrow").pickingMode = PickingMode.Ignore;
                var get = Add(line, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-bank-side");
                foreach (var c in t[r].Get) gets[r].Add(Apricorn(get, c));
                var go = Add(line, Kit.Button(stage.T("screen.bank.trade"), KitButtonKind.Normal, () => Press(rowId)), "board-village-bank-trade-" + r, "sn-craft-make");
                go.SetEnabled(traded == null && CanTrade(r));
                if (open && traded == null) stage.Tips.Hook(go, null, stage.T("screen.bank.trade.tip"), TipSide.Above, reading: true);
            }
            // 순증감(18행 착지): 거래한 줄의 규토리 색마다 낸 뒤 − 낸 전
            net.Clear();
            if (traded == null) return;
            var first = new Dictionary<int, int>();
            var last = new Dictionary<int, int>();
            foreach (var g in traded)
                if (g.Changes != null)
                    foreach (var c in g.Changes)
                    {
                        if (c == null || c.Res != ResKind.Apricorn) continue;
                        if (!first.ContainsKey(c.Key)) first[c.Key] = c.Before;
                        last[c.Key] = c.After;
                    }
            var inv = CultureInfo.InvariantCulture;
            for (int color = 0; color < SeatState.ApricornColors; color++)
            {
                if (!first.ContainsKey(color)) continue;
                int d = last[color] - first[color];
                if (d == 0) continue;
                var chip = Add(net, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-enc-chip");
                Apricorn(chip, (ApricornColor)color);
                chip.Add(Kit.Text(Signed(d), Kit.F2));
            }
        }

        /// <summary>화면 대기열의 그 순간(18행): 아직 끝나지 않은 내 은행상회 단계가 있으면 그 카드 · 빛 · 규토리 · 순증감 · 흔들림 · 뒤집기.</summary>
        public void Animate(FxSequencer seq)
        {
            if (stage == null || card == null) return;
            var step = Pending(seq, out int ms);
            var traded = new List<GinkgoTraded>();
            if (step != null) foreach (var e in step.Events) if (e is GinkgoTraded g) traded.Add(g);
            bool fresh = true;
            float sx = step != null ? BankBeats.Flip(ms, out fresh) : 1f;
            int want = step != null && !fresh && traded.Count > 0 ? traded[0].CardKey : BankRules.Card(flow.Client.View);
            int stepId = step != null && !fresh ? step.StartMs ^ (traded.Count > 0 ? traded[0].Seq : 0) : -1;
            if (want != paintedCard || stepId != paintedStep)
            {
                paintedCard = want;
                paintedStep = stepId;
                PaintCard(want, step != null && !fresh ? traded : null);
            }
            ShownCard = want;
            FxUi.Scale(card, sx, 1f);
            FxUi.Move(card, step != null ? BankBeats.Shake(ms) * ShakePx : 0f, 0f);
            FxUi.Fade(net, step != null && !fresh ? BankBeats.Net(ms) : 0f);
            int give = 0, get = 0;
            for (int r = 0; r < 2; r++)
            {
                bool did = step != null && !fresh && traded.Exists(g => g.Row == r);
                if (glows[r] != null) FxUi.Fade(glows[r], did ? BankBeats.Glow(ms) : 0f);
                foreach (var icon in gives[r])
                {
                    float t = did ? BankBeats.Give(ms, give++) : 0f;
                    FxUi.Move(icon, -t * SuckPx, 0f);
                    FxUi.Scale(icon, 1f - 0.7f * t, 1f - 0.7f * t);
                    FxUi.Fade(icon, 1f - t);
                }
                foreach (var icon in gets[r])
                {
                    float t = did ? BankBeats.Get(ms, get++) : 1f;
                    float s = t < 1f ? 0.4f + 0.8f * t : 1f;
                    FxUi.Move(icon, (1f - t) * SuckPx, 0f);
                    FxUi.Scale(icon, s, s);
                    FxUi.Fade(icon, did ? Mathf.Clamp01(t * 2f) : 1f);
                }
            }
        }

        /// <summary>규토리가 빨려 들고 튀어나오는 거리 · 섞일 때 흔들리는 폭(패널 px — 720 벌).</summary>
        public const float SuckPx = 18f, ShakePx = 4f;

        // 아직 새 카드로 뒤집히지 않은 내 은행상회 단계(재생 중이면 단계 안 시각, 기다리는 중이면 0)
        FxStep Pending(FxSequencer seq, out int ms)
        {
            ms = 0;
            if (seq == null) return null;
            foreach (var entry in seq.Queued)
            {
                var step = entry.Step(BankBeats.Row);
                if (step == null || WorkRows.SeatOf(step) != Seat) continue;
                if (ReferenceEquals(entry, seq.Current))
                {
                    int local = seq.LocalMs - step.StartMs;
                    if (local >= step.Ms) continue;
                    ms = Math.Max(0, local);
                }
                return step;
            }
            return null;
        }

        /// <summary>[거래]·[두 줄 모두]: 그 은행상회 답을 낸다. 받아들여졌으면 true(창은 그대로 — 이 칸이 거래를 그린다).</summary>
        public bool Press(int rows)
        {
            if (!CanTrade(rows)) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var answer = trades.Find(a => a.Target == rows);
            if (choice == null || answer == null) return false;
            stage.Tips.Hide();
            flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Bank, rows, true);   // 누른 줄(받아들여지기 전 — 답한 뒤에는 맵 행동이 닫혀 방장이 넘기지 않을 수 있다)
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>
        /// 그 줄(0 위 · 1 아래 · 2 두 줄 모두, −1 = 떠남)에 마우스를 올렸다 — 방 판의 내 턴이면 다른 좌석의 화면에 내 좌석 색으로 보인다(M9 다듬기 P09). 내 턴이 아니면(창을 보기만) 보내지 않는다.
        /// </summary>
        public bool Hover(int rows)
        {
            if (stage == null || !open || rows > BankRules.Both || flow.Client.Choice(ChoiceKind.MapAction) == null) return false;
            return flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Bank, rows < 0 ? -1 : rows);
        }
    }
}
