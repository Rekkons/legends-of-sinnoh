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
    /// 반바지 꼬마 빨강의 카드 바꾸기(사용자 결정 87 V6 나 → 결정 101 ① — 출처 = 카드인 보드의 고르기, 값 = 트레이너 카드): 뽑은 카드를 작은 카드 그림(<see cref="CardFx.Face"/> 틀 + 이름)으로
    /// <b>한 줄에 겹치지 않게</b> 펴고(고른 카드가 남을 가리지 않는다), 카드를 누르면 그 카드가 제자리에서 위로 올라오며 금색 테두리가 서고 아래의 고정된 큰 미리보기(카드 그림 + 두 능력의 이름과 전체 문구)에 실린다.
    /// [이 카드로 교체]로 답한다. 마우스 오버 = 두 능력의 이름과 문구. 답해야 하는 창이라 [닫기]·Esc가 없다(<see cref="CardRewardScreen"/>과 같다).
    /// 요소 이름: <c>board-card-swap</c>·<c>-head</c>·<c>-fan</c>(카드 줄)·<c>-card-&lt;n&gt;</c>(답 색인 + 1)·<c>-preview</c>·<c>-preview-card</c>·<c>-preview-name</c>·<c>-preview-ability-&lt;1|2&gt;</c>·<c>-go</c>·<c>-pass</c>.
    /// </summary>
    public sealed class CardSwapScreen : IUiScreen
    {
        /// <summary>한 줄에 놓이는 카드 수(뽑는 카드는 다섯까지 — 줄 폭이 다섯 장에 맞춰 있다).</summary>
        public const int Slots = 5;
        public const string Go = "board-card-swap-go", Pass = "board-card-swap-pass", OnClass = "sn-swap-card-on";

        readonly GameFlow flow;
        readonly List<ChoiceAnswer> answers = new List<ChoiceAnswer>();
        UiStage stage;
        VisualElement panel;
        int painted = -1, paintedPick = -2;
        bool paintedOpen;

        public CardSwapScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-card-swap";

        /// <summary>이 창이 받는 선택인가: 보드에서 카드가 연 고르기 가운데 값이 트레이너 카드인 것.</summary>
        public static bool Owns(Ctx ctx, PendingChoice c) => BoardHud.BoardCardAsk(c) && c.Sub == ChoiceSub.EffectPick && CardAsk.KindOf(ctx, c) == CardAsk.Value.Card;

        /// <summary>창에 놓인 답(엔진 순서) · 지금 앞으로 나온 카드(답 색인, 없으면 −1).</summary>
        public IReadOnlyList<ChoiceAnswer> Answers => answers;
        public int Picked { get; private set; } = -1;

        bool Open => flow.BoardOpen;

        PendingChoice Choice => flow.Client != null ? flow.Client.FirstChoice() : null;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            Add(page, new VisualElement(), "board-card-swap-scrim", "sn-party-scrim");
            panel = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-card-swap-page", "sn-swap-page");
            painted = -1;
            Picked = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>선택이 바뀌었거나 입력이 열리고 닫혔거나 고른 카드가 바뀌었으면 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null || flow.Client.View == null) return;
            var choice = Choice;
            if (!Owns(flow.Ctx, choice)) return;
            bool open = Open;
            if (painted == flow.Client.Version && paintedOpen == open && paintedPick == Picked) return;
            if (painted != flow.Client.Version) Picked = -1;
            painted = flow.Client.Version;
            paintedOpen = open;
            paintedPick = Picked;
            panel.Clear();
            answers.Clear();
            answers.AddRange(flow.Client.Legal(choice));
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var head = Add(panel, Kit.Text(CardAsk.Head(stage, ctx, choice), Kit.FTitle), "board-card-swap-head", "kit-title");
            stage.Tips.Hook(head, null, CardAsk.Text(stage, ctx, choice), TipSide.Below, reading: true);
            Add(panel, Kit.Badge(stage.T("screen.card.swap.guide"), KitBadgeKind.Plain, Kit.F2), "board-card-swap-guide", null);   // 어두운 덮개 위라 배지 바탕에

            // 카드 줄(결정 101 ①): 작은 카드 그림 + 이름을 한 줄에 겹치지 않게 — 고른 카드는 제자리에서 위로 올라오고 금색 테두리가 선다(남을 가리지 않는다)
            var fan = Add(panel, new VisualElement { pickingMode = PickingMode.Ignore }, "board-card-swap-fan", "sn-swap-fan");
            var cards = new List<int>();
            for (int i = 0; i < answers.Count; i++) if (answers[i].Kind == AnswerKind.Pick) cards.Add(i);
            for (int k = 0; k < cards.Count; k++)
            {
                int index = cards[k], key = answers[index].Target;
                string name = stage.T("trainer_card." + CardAsk.CardKey(ctx, key));
                var card = Add(fan, new VisualElement(), "board-card-swap-card-" + (index + 1).ToString(inv), "sn-swap-card");
                var thumb = Add(card, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-swap-thumb");
                flow.Cards.Face(thumb, key, TrainerRules.Gold, false, null, null, null);
                Unpick(thumb);
                Add(card, Kit.Badge(name, KitBadgeKind.Plain, Kit.F2), null, "sn-swap-name").pickingMode = PickingMode.Ignore;
                if (index == Picked) card.AddToClassList(OnClass);
                if (open) stage.OnTap(card, SoundKeys.Select, e => Pick(index));
                stage.Tips.Hook(card, name, AbilityLines(ctx, key), TipSide.Below, reading: true, clear: fan);
            }
            int at = cards.IndexOf(Picked);

            // 큰 미리보기(자리 고정): 고른 카드의 카드 그림 + 이름 + 두 능력의 이름과 전체 문구 + [이 카드로 교체]. 고르기 전에는 안내만
            var detail = Add(panel, new VisualElement { pickingMode = PickingMode.Ignore }, "board-card-swap-preview", "sn-swap-detail");
            var big = Add(detail, new VisualElement(), "board-card-swap-preview-card", "sn-swap-preview");
            // 고르기 전에는 상자(큰 미리보기 테두리 · 옆 판 창틀)를 그리지 않는다 — 자리만 지킨다(결정 103)
            var side = Add(detail, at >= 0 ? (VisualElement)Kit.Panel(stage.Skin) : new VisualElement(), "board-card-swap-side", "sn-swap-side");
            if (at >= 0)
            {
                int key = answers[Picked].Target;
                flow.Cards.Face(big, key, TrainerRules.Gold, true, null, null, null);
                Add(side, Kit.Text(stage.T("trainer_card." + CardAsk.CardKey(ctx, key)), Kit.F3), "board-card-swap-preview-name", null);
                for (int slot = 1; slot <= 2; slot++)
                {
                    var box = Add(side, new VisualElement(), "board-card-swap-preview-ability-" + slot.ToString(inv), "sn-swap-ability");
                    Add(box, Kit.Badge(stage.T(CardAsk.AbilityKey(ctx, key, slot)), slot == 1 ? KitBadgeKind.Plain : KitBadgeKind.Red, Kit.F2), null, null);
                    string text = CardAsk.Text(stage, ctx, key, slot);
                    if (text != null) Add(box, Kit.Wrapped(KoText.JoinWords(text), Kit.F2), null, "sn-swap-ability-text");
                }
            }
            else
            {
                big.style.visibility = Visibility.Hidden;
                Add(side, Kit.Wrapped(KoText.JoinWords(stage.T("screen.card.swap.empty")), Kit.F2), "board-card-swap-preview-empty", "kit-muted");
            }

            var foot = Add(side, new VisualElement(), "board-card-swap-foot", "sn-swap-foot");
            var go = Add(foot, Kit.Button(stage.T("screen.card.swap.go"), KitButtonKind.Main, () => Confirm()), Go, null);
            go.SetEnabled(open && at >= 0);
            for (int i = 0; i < answers.Count; i++)
            {
                if (answers[i].Kind == AnswerKind.Pick) continue;
                int index = i;
                Add(foot, Kit.Button(stage.T("screen.battle.fate.pass"), KitButtonKind.Normal, () => Press(index)), Pass, null).SetEnabled(open);
            }
        }

        // 마우스 오버: 능력 둘의 이름과 문구(카드 그림에는 48자 넘는 문구가 실리지 않는다)
        string AbilityLines(Ctx ctx, int card)
        {
            var lines = new List<string>();
            for (int slot = 1; slot <= 2; slot++)
            {
                lines.Add("[" + stage.T(CardAsk.AbilityKey(ctx, card, slot)) + "]");
                string text = CardAsk.Text(stage, ctx, card, slot);
                if (text != null) lines.Add(text);
            }
            return string.Join("\n", lines);
        }

        /// <summary>카드(<see cref="Answers"/>의 색인)를 앞으로 꺼낸다 — 답은 아직 내지 않는다. 같은 카드를 다시 누르면 도로 넣는다.</summary>
        public bool Pick(int index)
        {
            if (stage == null || !Open || index < 0 || index >= answers.Count || answers[index].Kind != AnswerKind.Pick) return false;
            Picked = Picked == index ? -1 : index;
            stage.Tips.Hide();
            Refresh();
            return true;
        }

        /// <summary>[이 카드로 교체]: 앞으로 꺼낸 카드로 답한다.</summary>
        public bool Confirm() => Picked >= 0 && Press(Picked);

        /// <summary>그 답(<see cref="Answers"/>의 색인)을 낸다. 입력이 닫혀 있거나 없는 답이면 false.</summary>
        public bool Press(int index)
        {
            var choice = Choice;
            if (stage == null || !Open || !Owns(flow.Ctx, choice) || index < 0 || index >= answers.Count) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answers[index]).Accepted;
        }

        /// <summary>그 답과 같은 단추를 누른다(확인용 길잡이). 없으면 false.</summary>
        public bool Press(ChoiceAnswer answer) => answer != null && Press(answers.FindIndex(a => a.SameAs(answer)));

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
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
