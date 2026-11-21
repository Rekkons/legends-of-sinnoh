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
    /// S30 운명의 변화 손패 창(결정 75 A2 가 — 목업 B안: 평소에는 닫혀 있고 보드 HUD의 [운명의 변화]로 여는 가운데 큰 카드 창).
    /// <list type="bullet">
    /// <item>손패: 카드를 크게 늘어놓는다(그림 · 이름 · 큰 글씨, 마우스 오버 = 1장·3장 효과). 고른 카드는 금색, 지금 못 쓰는 카드는 흐리게 + 까닭 꼬리표(「배틀 중」·「3장용」·「내 턴에만」·「사용함」).
    /// 고른 카드의 [1장 사용]·[3장 사용]은 엔진의 합법 맵 행동 답(PlayFate)에 있을 때만 켜진다. 3장에 함께 낼 아르세우스 장수가 여럿이면 칩으로 고른다(결정 76 ② F1 나 — 첫 칩 = 자동 나눔).
    /// 펄기아는 누르면 창을 닫고 보드에서 갈 칸을 고른다(<see cref="BoardHud.Warping"/>).</item>
    /// <item>6장째 버리기(rulings A12 · 결정 75 B1): 버릴 카드가 열리면 저절로 열린다(남의 턴 · 배틀 결과 창 위에도). 가장 나중에 받은 카드에 「새 카드」. 고르고 [버리기] — 버릴 때까지 닫히지 않는다.</item>
    /// </list>
    /// 배틀 안의 운명 창(디아루가 · 기라티나)은 배틀 독이 받는다(<see cref="BattleScreen.AskingFate"/>). 규칙을 다시 세지 않는다 — 단추는 합법 답 그대로.
    /// 요소 이름: <c>board-fate</c>·<c>board-fate-panel</c>·<c>board-fate-title</c>·<c>board-fate-close</c>·<c>board-fate-card-&lt;i&gt;</c>(i = 손패 순서, 1부터)·<c>board-fate-line</c>·
    /// <c>board-fate-use-1</c>·<c>board-fate-use-3</c>·<c>board-fate-wild-&lt;n&gt;</c>·<c>board-fate-discard</c>.
    /// </summary>
    public sealed class FateScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        readonly List<int> hand = new List<int>();

        public FateScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-fate";

        /// <summary>버릴 카드를 고르는 중인가(6장째 — 내 Discard 선택이 열려 있다).</summary>
        public bool Discarding => !Peeking && flow.Client != null && flow.Client.Choice(ChoiceKind.Discard) != null;

        /// <summary>물음 창 · 배틀 · 결과 창 위의 정보 창 층에 연 창인가(결정 125): 보기만 한다 — 카드를 내거나 버리지 않는다(아래 창이 닫혀 보드만 남으면 화면 자리로 옮겨 다시 받는다).</summary>
        public bool Peeking => stage != null && ReferenceEquals(flow.Stage.Info, this);

        /// <summary>지금 그린 손패(카드 색인, 받은 순서 — 끝이 가장 나중에 받은 카드).</summary>
        public IReadOnlyList<int> Hand => hand;

        /// <summary>고른 카드(손패 순서, 없으면 −1).</summary>
        public int Picked { get; private set; } = -1;

        /// <summary>3장 효과에 함께 낼 아르세우스 칩(0 = 첫 칩 = 자동 나눔).</summary>
        public int Wild { get; private set; }

        /// <summary>고른 카드의 1장·3장 답(합법 맵 행동 답 — 펄기아면 칸마다 여럿). 없으면 비어 있다.</summary>
        public List<ChoiceAnswer> Uses(int n)
        {
            var r = new List<ChoiceAnswer>();
            var client = flow.Client;
            var choice = client != null ? client.Choice(ChoiceKind.MapAction) : null;
            if (choice == null || Picked < 0 || Picked >= hand.Count) return r;
            var card = FateRules.KindOf(flow.Ctx, hand[Picked]);
            var splits = n == FateRules.Play3Count ? Splits(card) : null;
            int wild = splits != null && Wild > 0 && Wild < splits.Count ? splits[Wild] : -1;
            foreach (var a in client.Legal(choice))
                if (a.Kind == AnswerKind.PlayFate && a.Target == (int)card && a.Arg == n && (n != FateRules.Play3Count || FateRules.WildsOf(a) == wild)) r.Add(a);
            return r;
        }

        /// <summary>그 카드의 3장 효과에 함께 낼 수 있는 아르세우스 장수(첫 값 = 자동 나눔 — <see cref="FateRules.WildSplits"/>, 내 보기에서).</summary>
        public List<int> Splits(FateCard card)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            return view != null ? FateRules.WildSplits(view, flow.Ctx, flow.Client.Seat, card) : new List<int>();
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            if (flow.Stage.Current is BattleScreen) page.AddToClassList("sn-party-battle");   // 정보 창 층(결정 125)에서도 배틀 위면 같다   // 배틀 화면 위: 어둠이 독까지 덮는다(보드 HUD 높이에서 끊겨 독 윗줄을 가로지르지 않게)
            var scrim = Add(page, new VisualElement(), "board-fate-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => { if (!Discarding) flow.CloseFate(); });   // 창 밖을 누르면 닫힌다(버리는 중에는 닫히지 않는다)
            panel = Add(page, Kit.Panel(stage.Skin), "board-fate-panel", "sn-fate-panel");
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>보기·입력이 바뀌었다: 손패와 단추를 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            stage.Tips.Hide();
            panel.Clear();
            var me = view.Seats[client.Seat];
            hand.Clear();
            hand.AddRange(me.FateHand);
            if (Picked >= hand.Count) Picked = -1;
            bool discarding = Discarding;
            bool open = flow.AnswerOpen;
            // 배틀 화면 위에서는 보기만 한다(결정 80 N3 나 — 배틀 안에서 내는 카드는 독의 물음이 받는다)
            bool viewing = !discarding && (flow.Stage.Current is BattleScreen || Peeking);

            var head = Add(panel, new VisualElement(), null, "sn-fate-head");
            var left = Add(head, new VisualElement(), null, "sn-hud-line");
            Add(left, Kit.Text(stage.T("screen.fate.title").Replace("{n}", hand.Count.ToString(inv)).Replace("{max}", FateRules.HandLimit.ToString(inv)), Kit.FTitle), "board-fate-title", "kit-title");
            if (!discarding)
            {
                var limit = Add(left, Kit.Badge(stage.T(FateRules.CanUse(view, client.Seat) ? "screen.fate.limit" : "screen.fate.limit.used"), KitBadgeKind.Plain, Kit.F2), "board-fate-limit", null);
                stage.Tips.Hook(limit, null, stage.T("screen.fate.limit.tip"), TipSide.Below, reading: true, clear: panel);
                Add(head, Kit.Button(stage.T("screen.fate.close"), KitButtonKind.Normal, () => flow.CloseFate()), "board-fate-close", null);
            }

            var row = Add(panel, new VisualElement(), "board-fate-cards", "sn-fate-cards");
            if (hand.Count == 0) Add(row, Kit.Text(stage.T("screen.fate.empty"), Kit.F2), "board-fate-empty", "kit-muted");
            var choice = client.Choice(ChoiceKind.MapAction);
            var legal = !discarding && choice != null ? client.Legal(choice) : new List<ChoiceAnswer>();
            for (int i = 0; i < hand.Count; i++)
            {
                var card = FateRules.KindOf(ctx, hand[i]);
                string tag = null;
                var kind = KitBadgeKind.Plain;
                bool off = false;
                if (discarding)
                {
                    if (i == hand.Count - 1) { tag = stage.T("screen.fate.tag.new"); kind = KitBadgeKind.Ok; }
                }
                else if (!legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)card))
                {
                    off = true;
                    tag = stage.T(OffWhy(view, client.Seat, card));
                }
                int index = i;
                var el = FateCards.Make(stage, card, "board-fate-card-" + (i + 1).ToString(inv), tag, kind);
                row.Add(el);
                if (off) el.AddToClassList("sn-fate-card-off");
                if (i == Picked) el.AddToClassList("sn-fate-card-picked");
                stage.OnTap(el, SoundKeys.Select, e => Pick(index));
                stage.Tips.Hook(el, stage.T(FateCards.NameKey(card)), FateCards.Tip(stage, card), TipSide.Above, reading: true, clear: panel);
            }

            var foot = Add(panel, new VisualElement(), null, "sn-fate-foot");
            if (discarding)
            {
                Add(foot, Kit.Text(stage.T("screen.fate.discard.guide"), Kit.F2), "board-fate-line", null);
                var drop = Add(foot, Kit.Button(stage.T("screen.fate.discard"), KitButtonKind.End, () => Discard()), "board-fate-discard", null);
                drop.SetEnabled(open && Picked >= 0);
                return;
            }
            string line = Picked < 0 ? stage.T(!viewing ? "screen.fate.pick" : flow.Stage.Current is BattleScreen ? "screen.fate.view" : "screen.fate.view.peek") : stage.T(FateCards.NameKey(FateRules.KindOf(ctx, hand[Picked]))) + " · " + stage.T(FateCards.NameKey(FateRules.KindOf(ctx, hand[Picked])) + ".text.1");
            Add(foot, Kit.Text(line, Kit.F2), "board-fate-line", null);
            if (viewing) return;
            var buttons = Add(foot, new VisualElement(), null, "sn-hud-line");
            if (Picked >= 0)
            {
                var card = FateRules.KindOf(ctx, hand[Picked]);
                var splits = Splits(card);
                if (Uses(FateRules.Play3Count).Count > 0 || (Wild > 0 && splits.Count > 1))
                    if (splits.Count > 1)
                        for (int k = 0; k < splits.Count; k++)
                        {
                            int chip = k;
                            var b = Add(buttons, Kit.Button(stage.T("screen.fate.wild").Replace("{n}", splits[k].ToString(inv)), k == Wild ? KitButtonKind.Main : KitButtonKind.Normal, () => PickWild(chip)), "board-fate-wild-" + splits[k].ToString(inv), null);
                            if (open) stage.Tips.Hook(b, null, stage.T("screen.fate.wild.tip"), TipSide.Above, reading: true, clear: panel);
                        }
            }
            var one = Add(buttons, Kit.Button(stage.T("screen.fate.use.1"), KitButtonKind.Main, () => Use(1)), "board-fate-use-1", null);
            one.SetEnabled(open && Uses(1).Count > 0);
            var three = Add(buttons, Kit.Button(stage.T("screen.fate.use.3"), KitButtonKind.Main, () => Use(FateRules.Play3Count)), "board-fate-use-3", null);
            three.SetEnabled(open && Uses(FateRules.Play3Count).Count > 0);
            if (open && Picked >= 0)
            {
                stage.Tips.Hook(one, null, stage.T(FateCards.NameKey(FateRules.KindOf(ctx, hand[Picked])) + ".text.1"), TipSide.Above, reading: true, clear: panel);
                stage.Tips.Hook(three, null, stage.T(FateCards.NameKey(FateRules.KindOf(ctx, hand[Picked])) + ".text.2"), TipSide.Above, reading: true, clear: panel);
            }
        }

        // 맵 행동으로 못 내는 까닭(공개 정보 + 내 손패): 배틀 안에서 내는 카드 · 3장용 · 남의 턴 · 이번 턴에 냈음 · 3장이 모자람
        static string OffWhy(GameState view, int seat, FateCard card)
        {
            if (card == FateCard.Dialga || card == FateCard.Giratina) return "screen.fate.tag.battle";
            if (card == FateCard.Arceus) return "screen.fate.tag.play3";
            if (view.ActiveSeat != seat || view.Battle != null) return "screen.fate.tag.my_turn";
            if (!FateRules.CanUse(view, seat)) return "screen.fate.tag.used";
            return "screen.fate.tag.my_turn";
        }

        /// <summary>손패 i번째 카드를 고른다(다시 누르면 푼다). 고르기만 한다.</summary>
        public bool Pick(int index)
        {
            if (index < 0 || index >= hand.Count) return false;
            Picked = Picked == index ? -1 : index;
            Wild = 0;
            Refresh();
            return true;
        }

        /// <summary>3장 효과에 함께 낼 아르세우스 칩을 고른다(0 = 자동 나눔).</summary>
        public bool PickWild(int chip)
        {
            if (Picked < 0 || chip < 0 || chip >= Splits(FateRules.KindOf(flow.Ctx, hand[Picked])).Count) return false;
            Wild = chip;
            Refresh();
            return true;
        }

        /// <summary>
        /// [1장 사용]·[3장 사용]: 고를 것이 없는 효과면 답을 바로 내고, 펄기아면 창을 닫고 보드에서 갈 칸을 고른다(<see cref="GameFlow.BeginWarp"/>). 받아들여졌으면 true.
        /// </summary>
        public bool Use(int n)
        {
            if (!flow.BoardOpen || Peeking) return false;
            var uses = Uses(n);
            if (uses.Count == 0) return false;
            stage?.Tips.Hide();
            if (uses.Count > 1 || uses[0].Target == (int)FateCard.Palkia) return flow.BeginWarp(uses);
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            flow.CloseFate();
            return flow.Answer(choice, uses[0]).Accepted;
        }

        /// <summary>[버리기]: 고른 카드를 버리는 답을 낸다(합법 답 그대로). 받아들여졌으면 true.</summary>
        public bool Discard()
        {
            var client = flow.Client;
            var choice = client != null ? client.Choice(ChoiceKind.Discard) : null;
            if (choice == null || !flow.AnswerOpen || Picked < 0 || Picked >= hand.Count) return false;
            var answer = client.Legal(choice).Find(a => a.Kind == AnswerKind.Pick && a.Target == hand[Picked]);
            if (answer == null) return false;
            stage?.Tips.Hide();
            Picked = -1;
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>확인용(길잡이): 그 카드 색인을 골라 버린다.</summary>
        public bool DiscardCard(int cardKey)
        {
            int at = hand.LastIndexOf(cardKey);
            if (at < 0) return false;
            Picked = at;
            return Discard();
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
