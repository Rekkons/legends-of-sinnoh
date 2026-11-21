using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S36 거래 창(04 §2 · U13 — M5 「스탬프·도전·거래 화면」, rulings A29): 보드 HUD의 [거래]로 여는 가운데 창. 상대(같은 칸 · 같은 신의 추종자 — <see cref="TradeRules.Partners"/>)를 고르고
    /// 양쪽이 주고받을 것을 올린 뒤 [제안] — 내 턴이면 맵 행동의 답, 남의 턴이면 명령 Propose(결정 75 B5 나). 상대의 응답(수락·거절)은 S41 프롬프트가 받는다.
    /// <list type="bullet">
    /// <item>두 기둥(내가 줄 것 · 상대에게 받을 것): 규토리 일곱 색 · 여행 점수 · 볼 · 가방의 아이템 · 파티 포켓몬 · 운명의 변화(내 것은 카드마다, 상대 것은 장수만 — 손패는 안 보인다).
    /// 가진 것만 칩으로 선다. 칩을 누르면 1개씩 올라가고 가진 수를 넘으면 0으로 돌아간다(포켓몬은 올리기 · 내리기).</item>
    /// <item>[제안]은 그 내용이 지금 맞을 때만 켜진다(<see cref="TradeRules.CanOffer"/> — 규칙을 다시 세지 않는다). 받는 것 없는 선물도 된다(A29 ①).</item>
    /// </list>
    /// 요소 이름: <c>board-trade</c>·<c>board-trade-panel</c>·<c>board-trade-close</c>·<c>board-trade-partner-&lt;좌석&gt;</c>·<c>board-trade-give</c>·<c>board-trade-ask</c>·
    /// <c>board-trade-&lt;give|ask&gt;-&lt;종류&gt;-&lt;키&gt;</c>(종류 = apricorn·jp·ball·item·pokemon·fate)·<c>board-trade-guide</c>·<c>board-trade-reset</c>·<c>board-trade-propose</c>.
    /// </summary>
    public sealed class TradeScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        readonly List<int> partners = new List<int>();
        // 올린 것: [주는 쪽 0 = 나 · 1 = 상대] (종류, 키) → 수
        readonly Dictionary<long, int>[] put = { new Dictionary<long, int>(), new Dictionary<long, int>() };

        /// <param name="partner">처음 고를 상대(거래할 수 없는 좌석이면 첫 상대).</param>
        public TradeScreen(GameFlow flow, int partner = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Partner = partner;
        }

        public string Name => "board-trade";

        /// <summary>지금 고른 상대(없으면 −1).</summary>
        public int Partner { get; private set; } = -1;

        /// <summary>거래할 수 있는 상대(다음 좌석부터).</summary>
        public IReadOnlyList<int> Partners => partners;

        static long Id(TradeKind kind, int key) => ((long)kind << 32) | (uint)key;

        /// <summary>그 쪽(0 = 내가 줌 · 1 = 상대가 줌)에 올린 수.</summary>
        public int Put(int side, TradeKind kind, int key) => put[side].TryGetValue(Id(kind, key), out int n) ? n : 0;

        /// <summary>지금 올린 내용의 거래 답(빈 거래면 null).</summary>
        public ChoiceAnswer Offer()
        {
            if (Partner < 0) return null;
            var quads = new List<int>();
            for (int side = 0; side < 2; side++)
                foreach (var kv in put[side])
                    if (kv.Value > 0) quads.AddRange(new[] { side, (int)(kv.Key >> 32), (int)(uint)kv.Key, kv.Value });
            return quads.Count == 0 ? null : TradeRules.Offer(Partner, quads.ToArray());
        }

        /// <summary>지금 내용으로 제안할 수 있나(엔진의 검사 그대로).</summary>
        public bool CanPropose
        {
            get
            {
                var offer = Offer();
                var client = flow.Client;
                return offer != null && client != null && client.View != null && TradeRules.CanOffer(client.View, flow.Ctx, client.Seat, offer);
            }
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = Add(page, new VisualElement(), "board-trade-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseTrade());   // 창 밖을 누르면 닫힌다(올린 것은 버린다)
            panel = Add(page, Kit.Panel(stage.Skin), "board-trade-panel", "sn-trade-panel");
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        // 그 좌석이 가진 수(보기에서 — 상대의 운명 손패는 장수만)
        int Have(GameState view, int seat, TradeKind kind, int key)
        {
            var st = view.Seats[seat];
            switch (kind)
            {
                case TradeKind.Apricorn: return st.Apricorns[key];
                case TradeKind.Jp: return st.Jp;
                case TradeKind.Ball: return st.Balls[key];
                case TradeKind.Item: return st.Bag.FindAll(x => x == key).Count;
                case TradeKind.Pokemon: return PokemonRules.PartyIndexOf(st, key) >= 0 ? 1 : 0;
                default: return key < 0 ? st.FateHand.Count : st.FateHand.FindAll(x => x == key).Count;
            }
        }

        /// <summary>보기·입력이 바뀌었다: 상대 목록을 다시 세고(없어졌으면 닫는다) 올린 것을 가진 수에 맞춰 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            TradeRules.Partners(view, ctx, client.Seat, partners);
            if (partners.Count == 0)
            {
                flow.CloseTrade();
                return;
            }
            if (!partners.Contains(Partner))
            {
                Partner = partners[0];
                put[0].Clear();
                put[1].Clear();
            }
            int[] who = { client.Seat, Partner };
            for (int side = 0; side < 2; side++)
                foreach (long id in new List<long>(put[side].Keys))
                {
                    int have = Have(view, who[side], (TradeKind)(id >> 32), (int)(uint)id);
                    if (put[side][id] > have) put[side][id] = have;
                }
            stage.Tips.Hide();
            panel.Clear();
            bool open = flow.BoardOpen;

            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var left = Add(head, new VisualElement(), "board-trade-partners", "sn-trade-partners");
            Add(left, Kit.Text(stage.T("screen.trade.title"), Kit.FTitle), "board-trade-title", "kit-title");
            foreach (int p in partners)
            {
                int seat = p;
                var chip = Add(left, new VisualElement(), "board-trade-partner-" + (p + 1).ToString(inv), "sn-trade-partner");
                var face = Add(chip, FaceBox.Make(), null, null);
                FaceBox.Paint(face, p, flow.SeatColor(p), flow.Protagonist(p));
                chip.Add(Kit.Text(flow.SeatName(p), Kit.F2));
                if (p == Partner) chip.AddToClassList("sn-party-picked");
                stage.OnTap(chip, SoundKeys.Select, e => PickPartner(seat));
                if (TradeRules.ModeWith(view, ctx, client.Seat, p) == TradeRules.Telekinesis) stage.Tips.Hook(chip, null, stage.T("screen.trade.reach.tip"), TipSide.Above, reading: true, clear: panel);   // psychic B 염력으로만 닿는 상대(결정 84 T12 ②)
            }
            Add(head, Kit.Button(stage.T("screen.trade.close"), KitButtonKind.Normal, () => flow.CloseTrade()), "board-trade-close", null);

            var cols = Add(panel, new VisualElement(), null, "sn-trade-cols");
            Column(cols, view, 0, who[0], "board-trade-give", stage.T("screen.trade.give"));
            Column(cols, view, 1, who[1], "board-trade-ask", KoJosa.Fix(stage.T("screen.trade.ask").Replace("{name}", flow.SeatName(Partner))));

            var foot = Add(panel, new VisualElement(), null, "sn-trade-foot");
            var offer = Offer();
            bool can = offer != null && TradeRules.CanOffer(view, ctx, client.Seat, offer);
            var guide = Add(foot, Kit.Text(stage.T(offer == null ? "screen.trade.guide" : can ? "screen.trade.guide.ok" : "screen.trade.guide.bad"), Kit.F2), "board-trade-guide", null);
            if (offer != null && !can) stage.Tips.Hook(guide, null, stage.T("screen.trade.guide.bad.tip"), TipSide.Above, reading: true, clear: panel);
            var buttons = Add(foot, new VisualElement(), null, "sn-hud-line");
            var reset = Add(buttons, Kit.Button(stage.T("screen.trade.reset"), KitButtonKind.Normal, () => Reset()), "board-trade-reset", null);
            reset.SetEnabled(offer != null);
            var go = Add(buttons, Kit.Button(stage.T("screen.trade.propose"), KitButtonKind.Main, () => Propose()), "board-trade-propose", null);
            go.SetEnabled(open && can);
            if (open && can) stage.Tips.Hook(go, null, stage.T("screen.trade.propose.tip"), TipSide.Above, reading: true, clear: panel);
        }

        void Column(VisualElement cols, GameState view, int side, int seat, string name, string title)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var st = view.Seats[seat];
            var col = Add(cols, new VisualElement(), name, "sn-trade-col");
            Add(col, Kit.Text(title, Kit.F2), null, "kit-title");
            int shown = 0;

            // 규토리(가진 색만)
            var sec = Section(col, "screen.trade.apricorns");
            for (int c = 0; c < st.Apricorns.Length; c++)
            {
                if (st.Apricorns[c] <= 0) continue;
                var chip = Chip(sec, side, TradeKind.Apricorn, c, st.Apricorns[c], stage.T(WorkUi.ColorKey((ApricornColor)c)), "screen.trade.chip.tip");
                WorkUi.Apricorn(chip, (ApricornColor)c).SendToBack();
                shown++;
            }
            Trim(sec);

            // 여행 점수 · 볼
            sec = Section(col, "screen.trade.stock");
            if (st.Jp > 0)
            {
                Chip(sec, side, TradeKind.Jp, -1, st.Jp, stage.T("screen.trade.jp"), "screen.trade.chip.tip");
                // 여행 점수는 한 번에 여러 점씩(결정 80 N2 나): 칩 옆의 [+5] — 다섯 점이 넘게 있을 때만
                if (st.Jp > JpStep)
                {
                    var more = Add(sec, Kit.Button(stage.T("screen.trade.more"), KitButtonKind.Normal, () => More(side)), "board-trade-" + (side == 0 ? "give" : "ask") + "-jp-more", "sn-trade-more");
                    if (flow.BoardOpen) stage.Tips.Hook(more, null, stage.T("screen.trade.more.tip"), TipSide.Above, reading: true, clear: panel);
                }
                shown++;
            }
            for (int b = 0; b < st.Balls.Length; b++)
            {
                if (st.Balls[b] <= 0) continue;
                int item = CraftRules.BallItem(ctx, (Ball)b);
                var chip = Chip(sec, side, TradeKind.Ball, b, st.Balls[b], item >= 0 ? stage.T(ctx.Items.Items[item].Key) : stage.T("screen.prompt.kind.ball"), "screen.trade.chip.tip");
                WorkUi.Icon(chip, GenAssets.ItemIcons + BattleFeed.BallItem(b) + ".png", "sn-icon").SendToBack();
                shown++;
            }
            Trim(sec);

            // 가방의 아이템(신오의 심장은 넣지 않는다 — A29 ①)
            sec = Section(col, "screen.trade.items");
            int heart = ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey);
            var seen = new List<int>();
            foreach (int it in st.Bag)
            {
                if (it < 0 || it >= ctx.Items.Items.Count || it == heart || seen.Contains(it)) continue;
                seen.Add(it);
                var chip = Chip(sec, side, TradeKind.Item, it, Have(view, seat, TradeKind.Item, it), stage.T(ctx.Items.Items[it].Key), "screen.trade.chip.tip");
                string icon = WorkUi.IconKey(ctx.Items.Items[it]);
                if (icon != null) WorkUi.Icon(chip, icon, "sn-icon").SendToBack();   // 그림이 없는 아이템은 아이콘 자리를 잡지 않는다(M5 마감 검토 — 「수호신의 서」 칩 앞이 비던 것)
                shown++;
            }
            Trim(sec);

            // 파티 포켓몬
            sec = Section(col, "screen.trade.mons");
            foreach (int id in st.Party)
            {
                if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                var def = ctx.Cards.Pokemon[view.Pokemon[id].Def];
                // 칩에는 폼 꼬리 「(…)」를 뺀 이름만(아래 판의 파티 칸과 같다 — 여섯 마리 둘이 서도 창이 아래 판을 덮지 않게 도트는 싣지 않는다) — 전체 이름은 마우스 오버
                var chip = Chip(sec, side, TradeKind.Pokemon, id, 1, stage.T("pokemon." + def.Key), "screen.trade.mon.tip", BoardHud.ShortName(stage.T("pokemon." + def.Key)));
                shown++;
            }
            Trim(sec);

            // 운명의 변화: 내 것은 카드마다, 상대 것은 장수만(받아들일 때 상대가 고른다)
            sec = Section(col, "screen.trade.fates");
            if (side == 0)
            {
                var kinds = new List<int>();
                foreach (int card in st.FateHand)
                {
                    if (card < 0 || kinds.Contains(card)) continue;
                    kinds.Add(card);
                    Chip(sec, side, TradeKind.Fate, card, Have(view, seat, TradeKind.Fate, card), stage.T(FateCards.NameKey(FateRules.KindOf(ctx, card))), "screen.trade.chip.tip");
                    shown++;
                }
            }
            else if (st.FateHand.Count > 0)
            {
                Chip(sec, side, TradeKind.Fate, -1, st.FateHand.Count, stage.T("screen.trade.fates"), "screen.trade.fate.theirs.tip");
                shown++;
            }
            Trim(sec);
            if (shown == 0) Add(col, Kit.Text(stage.T("screen.trade.none"), Kit.F2), null, "kit-muted");
        }

        VisualElement Section(VisualElement col, string titleKey)
        {
            var sec = Add(col, new VisualElement(), null, "sn-trade-sec");
            Add(sec, Kit.Text(stage.T(titleKey), Kit.F2), null, "sn-trade-sec-name");
            return Add(sec, new VisualElement(), null, "sn-trade-chips");
        }

        // 칩이 하나도 없는 갈래는 줄째로 뺀다
        static void Trim(VisualElement chips)
        {
            if (chips.childCount == 0) chips.parent.RemoveFromHierarchy();
        }

        static string KindName(TradeKind kind) => kind.ToString().ToLowerInvariant();

        VisualElement Chip(VisualElement parent, int side, TradeKind kind, int key, int have, string label, string tipKey, string shown = null)
        {
            var inv = CultureInfo.InvariantCulture;
            int n = Put(side, kind, key);
            var chip = Add(parent, new VisualElement(), "board-trade-" + (side == 0 ? "give-" : "ask-") + KindName(kind) + "-" + key.ToString(inv), "sn-trade-chip");
            if (n > 0) chip.AddToClassList("sn-trade-chip-on");
            // 포켓몬은 이름만(올렸으면 금색), 나머지는 「이름 올린 수/가진 수」
            chip.Add(Kit.Text(kind == TradeKind.Pokemon ? shown ?? label : label + " " + n.ToString(inv) + "/" + have.ToString(inv), Kit.F2));
            stage.OnTap(chip, SoundKeys.Select, e => Bump(side, kind, key));
            stage.Tips.Hook(chip, label, stage.T(tipKey), TipSide.Above, reading: true, clear: panel);
            return chip;
        }

        /// <summary>상대를 고른다(올린 것은 비운다).</summary>
        public bool PickPartner(int seat)
        {
            if (!partners.Contains(seat)) return false;
            if (seat != Partner)
            {
                Partner = seat;
                put[0].Clear();
                put[1].Clear();
            }
            Refresh();
            return true;
        }

        /// <summary>여행 점수 [+5]가 한 번에 올리는 점수.</summary>
        public const int JpStep = 5;

        /// <summary>칩을 누른다: step개(기본 1 · 여행 점수 [+5]는 <see cref="JpStep"/>) 더 올리되 가진 수에서 멈추고, 이미 다 올렸으면 0으로. 그 쪽이 가진 것이 아니면 false.</summary>
        public bool Bump(int side, TradeKind kind, int key, int step = 1)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || Partner < 0 || side < 0 || side > 1 || step < 1) return false;
            int have = Have(view, side == 0 ? flow.Client.Seat : Partner, kind, key);
            if (have <= 0) return false;
            int now = Put(side, kind, key);
            put[side][Id(kind, key)] = now >= have ? 0 : Math.Min(have, now + step);
            Refresh();
            return true;
        }

        /// <summary>여행 점수 칩 옆의 [+5점](결정 80 N2 나): <see cref="JpStep"/>점 더 올린다(가진 점수에서 멈추고, 다 올렸으면 0으로).</summary>
        public bool More(int side) => Bump(side, TradeKind.Jp, -1, JpStep);

        /// <summary>확인용(길잡이·시험): 그 쪽에 그 수만큼 올린다.</summary>
        public bool Set(int side, TradeKind kind, int key, int count)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || Partner < 0 || count < 0 || count > Have(view, side == 0 ? flow.Client.Seat : Partner, kind, key)) return false;
            put[side][Id(kind, key)] = count;
            Refresh();
            return true;
        }

        /// <summary>[초기화]: 올린 것을 모두 내린다.</summary>
        public void Reset()
        {
            put[0].Clear();
            put[1].Clear();
            Refresh();
        }

        /// <summary>[제안]: 거래를 제안하고 창을 닫는다(내 턴 = 맵 행동의 답 · 남의 턴 = 명령 Propose). 받아들여졌으면 true.</summary>
        public bool Propose()
        {
            var offer = Offer();
            if (offer == null || !flow.BoardOpen || !CanPropose) return false;
            stage?.Tips.Hide();
            bool ok = flow.ProposeTrade(offer);
            if (ok) flow.CloseTrade();
            return ok;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }

    /// <summary>
    /// 거래 수락 때 줄 운명의 변화 카드 고르기(S36 — rulings A29 ③ 「받아들일 때 고른다」): 남의 제안이 내 운명 카드 n장을 바랄 때 S41 프롬프트의 [수락]이 이 창을 연다
    /// (고를 것이 없으면 — 받아들이는 답이 하나뿐이면 — 프롬프트가 바로 낸다). 제안 내용(받기 · 주기) · 내 손패 · [수락](n장을 골랐을 때) · [거절].
    /// 요소 이름: <c>board-trade-pick</c>·<c>board-trade-pick-panel</c>·<c>board-trade-pick-card-&lt;i&gt;</c>·<c>board-trade-pick-accept</c>·<c>board-trade-pick-decline</c>.
    /// </summary>
    public sealed class TradePickScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        readonly List<int> hand = new List<int>();
        readonly List<int> picked = new List<int>();   // 손패 자리

        public TradePickScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-trade-pick";

        /// <summary>내게 열린 거래 응답(없으면 null).</summary>
        public static PendingChoice ChoiceOf(GameFlow flow)
        {
            var c = flow.Client != null ? flow.Client.Choice(ChoiceKind.Respond) : null;
            return c != null && c.Sub == ChoiceSub.Trade ? c : null;
        }

        /// <summary>그 응답에서 내가 줄 운명 카드 장수(받아들이는 답의 Values 수 — 없으면 0).</summary>
        public static int Asked(GameFlow flow, PendingChoice choice)
        {
            if (choice == null) return 0;
            var accept = flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Accept);
            return accept != null ? accept.Values.Count : 0;
        }

        /// <summary>고른 손패 자리(누른 순서).</summary>
        public IReadOnlyList<int> Picked => picked;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            if (flow.Stage.Current is BattleScreen) page.AddToClassList("sn-party-battle");   // 배틀 화면 위: 어둠이 독까지 덮는다(보드 HUD 높이에서 끊겨 독 윗줄을 가로지르지 않게)
            Add(page, new VisualElement(), "board-trade-pick-scrim", "sn-party-scrim");
            panel = Add(page, Kit.Panel(stage.Skin), "board-trade-pick-panel", "sn-fate-panel");
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        // 고른 카드로 받아들이는 합법 답(없으면 null)
        ChoiceAnswer Answer(PendingChoice choice)
        {
            var vals = new List<int>();
            foreach (int i in picked) vals.Add(hand[i]);
            vals.Sort();
            foreach (var a in flow.Client.Legal(choice))
            {
                if (a.Kind != AnswerKind.Accept || a.Values.Count != vals.Count) continue;
                bool same = true;
                for (int k = 0; k < vals.Count && same; k++) same = a.Values[k] == vals[k];
                if (same) return a;
            }
            return null;
        }

        /// <summary>보기·입력이 바뀌었다: 응답이 닫혔으면(시간 초과 · 거래가 맞지 않게 됨) 창을 닫고, 아니면 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var choice = ChoiceOf(flow);
            int ask = Asked(flow, choice);
            if (choice == null || ask == 0)
            {
                flow.CloseTradePick();
                return;
            }
            var view = flow.Client.View;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            stage.Tips.Hide();
            panel.Clear();
            hand.Clear();
            hand.AddRange(view.Seats[flow.Client.Seat].FateHand);
            picked.RemoveAll(i => i >= hand.Count);
            bool open = flow.AnswerOpen;

            var head = Add(panel, new VisualElement(), null, "sn-fate-head");
            Add(head, Kit.Text(KoJosa.Fix(stage.T("screen.trade.respond.title").Replace("{name}", flow.SeatName(choice.Subject))), Kit.FTitle), "board-trade-pick-title", "kit-title");
            // 제안 내용: Options = [방식, 거래 줄(주는 쪽 0 = 제안한 좌석 · 1 = 나, 종류, 키, 수)…]
            for (int side = 0; side < 2; side++)
            {
                var line = Add(panel, new VisualElement(), side == 0 ? "board-trade-pick-get" : "board-trade-pick-give", "sn-trade-offer");
                Add(line, Kit.Text(stage.T(side == 0 ? "screen.trade.respond.get" : "screen.trade.respond.give"), Kit.F2), null, "kit-muted");
                int n = 0;
                for (int i = 1; i + TradeRules.Stride - 1 < choice.Options.Count; i += TradeRules.Stride)
                {
                    if (choice.Options[i] != side) continue;
                    line.Add(Kit.Badge(DealFx.ItemText(stage, flow, (TradeKind)choice.Options[i + 1], choice.Options[i + 2], choice.Options[i + 3]), KitBadgeKind.Plain, Kit.F2));
                    n++;
                }
                if (n == 0) line.Add(Kit.Text(stage.T("screen.prompt.nothing"), Kit.F2));
            }

            var row = Add(panel, new VisualElement(), "board-trade-pick-cards", "sn-fate-cards");
            for (int i = 0; i < hand.Count; i++)
            {
                int index = i;
                var card = FateRules.KindOf(ctx, hand[i]);
                var el = FateCards.Make(stage, card, "board-trade-pick-card-" + (i + 1).ToString(inv));
                row.Add(el);
                if (picked.Contains(i)) el.AddToClassList("sn-fate-card-picked");
                stage.OnTap(el, SoundKeys.Select, e => Pick(index));
                stage.Tips.Hook(el, stage.T(FateCards.NameKey(card)), FateCards.Tip(stage, card), TipSide.Above, reading: true, clear: panel);
            }

            var foot = Add(panel, new VisualElement(), null, "sn-fate-foot");
            Add(foot, Kit.Text(stage.T("screen.trade.respond.pick").Replace("{n}", ask.ToString(inv)), Kit.F2), "board-trade-pick-line", null);
            var buttons = Add(foot, new VisualElement(), null, "sn-hud-line");
            var yes = Add(buttons, Kit.Button(stage.T("screen.prompt.accept"), KitButtonKind.Main, () => Accept()), "board-trade-pick-accept", null);
            yes.SetEnabled(open && picked.Count == ask && Answer(choice) != null);
            var no = Add(buttons, Kit.Button(stage.T("screen.prompt.decline"), KitButtonKind.Normal, () => Decline()), "board-trade-pick-decline", null);
            no.SetEnabled(open);
        }

        /// <summary>손패 i번째 카드를 고른다(다시 누르면 푼다 — 바라는 장수를 넘으면 가장 먼저 고른 것이 풀린다).</summary>
        public bool Pick(int index)
        {
            var choice = ChoiceOf(flow);
            if (choice == null || index < 0 || index >= hand.Count) return false;
            if (!picked.Remove(index))
            {
                picked.Add(index);
                if (picked.Count > Asked(flow, choice)) picked.RemoveAt(0);
            }
            Refresh();
            return true;
        }

        /// <summary>[수락]: 고른 카드로 받아들인다. 받아들여졌으면 true.</summary>
        public bool Accept()
        {
            var choice = ChoiceOf(flow);
            if (choice == null || !flow.AnswerOpen || picked.Count != Asked(flow, choice)) return false;
            var answer = Answer(choice);
            if (answer == null) return false;
            stage?.Tips.Hide();
            picked.Clear();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>[거절].</summary>
        public bool Decline()
        {
            var choice = ChoiceOf(flow);
            if (choice == null || !flow.AnswerOpen) return false;
            var answer = flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline);
            if (answer == null) return false;
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
