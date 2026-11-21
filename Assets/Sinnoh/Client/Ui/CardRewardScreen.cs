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
    /// 보드에서 트레이너 카드가 연 배틀 밖 선택(M6 「트레이너 카드 화면」 2c — 출처 = 카드, rulings A37·A40): 파티가 가득 찬 채 카드로 얻은 포켓몬의 놓아주기(Reward/PartyFull — 「When unlocked」 등) ·
    /// scientist B 현장 관찰의 기술 가르치기(Reward/TeachMove). 배틀 결과 창의 같은 물음(<see cref="BattleScreen"/>)과 같은 꼴 — 포켓몬 칸 · 기술 줄 · 칸 단추이고, 답은 엔진의 합법 답 그대로.
    /// 결과 창 없이 남은 배틀 뒤 놓아주기 · 가르치기(재접속 · 다시 연 판 — <see cref="Orphan"/>)도 받는다(머리 = 「배틀 보상」). 답해야 하는 창이라 [닫기]·Esc가 없다. 요소 이름: <c>board-card-reward</c>·<c>-panel</c>·<c>-head</c>·<c>-ask</c>·<c>-move</c>·<c>-&lt;n&gt;</c>(답 색인 + 1).
    /// </summary>
    public sealed class CardRewardScreen : IUiScreen
    {
        readonly GameFlow flow;
        readonly List<ChoiceAnswer> answers = new List<ChoiceAnswer>();
        UiStage stage;
        VisualElement panel;
        int painted = -1;
        bool paintedOpen;

        public CardRewardScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-card-reward";

        /// <summary>이 창이 받는 선택인가: 출처가 카드인 배틀 밖 놓아주기 · 기술 가르치기.</summary>
        public static bool Owns(PendingChoice c) => CardAsk.FromCard(c) && c.Kind == ChoiceKind.Reward && c.Window != Sinnoh.Rules.Window.BattleEnd && (c.Sub == ChoiceSub.PartyFull || c.Sub == ChoiceSub.TeachMove);

        /// <summary>
        /// 결과 창 없이 남은 배틀 뒤 놓아주기 · 기술 가르치기(Window = BattleEnd): 따라가는 배틀이 없다(재접속 · 손본 판으로 다시 연 판 — 결과 창이 받을 배틀이 없다). 이 창이 받는다(결정 185).
        /// </summary>
        public static bool Orphan(GameFlow flow, PendingChoice c) => flow != null && flow.Battle == null && c != null && c.Kind == ChoiceKind.Reward && c.Window == Sinnoh.Rules.Window.BattleEnd && (c.Sub == ChoiceSub.PartyFull || c.Sub == ChoiceSub.TeachMove);

        /// <summary>이 창이 지금 받는 선택인가: 카드가 연 것(<see cref="Owns"/>) 또는 결과 창 없이 남은 배틀 뒤 선택(<see cref="Orphan"/>).</summary>
        public static bool Takes(GameFlow flow, PendingChoice c) => Owns(c) || Orphan(flow, c);

        /// <summary>창에 놓인 답(엔진 순서).</summary>
        public IReadOnlyList<ChoiceAnswer> Answers => answers;

        bool Open => flow.BoardOpen;

        PendingChoice Choice => flow.Client != null ? flow.Client.Choice(ChoiceKind.Reward) : null;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            Add(page, new VisualElement(), "board-card-reward-scrim", "sn-party-scrim");
            panel = Add(page, Kit.Panel(stage.Skin), "board-card-reward-panel", "sn-evolve-panel");
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>선택이 바뀌었거나 입력이 열리고 닫혔으면 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null || flow.Client.View == null) return;
            var choice = Choice;
            if (!Takes(flow, choice)) return;
            bool open = Open;
            if (painted == flow.Client.Version && paintedOpen == open) return;
            painted = flow.Client.Version;
            paintedOpen = open;
            panel.Clear();
            answers.Clear();
            answers.AddRange(flow.Client.Legal(choice));
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var inv = CultureInfo.InvariantCulture;
            int card = choice.Source.Id;
            // 머리: 놓아주기는 카드 이름(어느 능력으로 얻었는지는 카드마다 다르다), 가르치기는 「카드 — 능력」
            bool fromCard = Owns(choice);
            string headText = !fromCard ? stage.T("screen.battle.reward.head") : choice.Sub == ChoiceSub.TeachMove ? CardAsk.Head(stage, ctx, choice) : stage.T("trainer_card." + CardAsk.CardKey(ctx, card));
            var head = Add(panel, Kit.Wrapped(headText, Kit.FTitle), "board-card-reward-head", "kit-title");
            if (fromCard && choice.Sub == ChoiceSub.TeachMove) stage.Tips.Hook(head, null, CardAsk.Text(stage, ctx, choice), TipSide.Below, reading: true);
            var box = Add(panel, new VisualElement(), "board-card-reward-box", "sn-battle-reward");
            var mons = MonArt.Load();
            var heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            VisualElement Tile(VisualElement parent, int id, int index, bool click)
            {
                var tile = Add(parent, new VisualElement(), click ? "board-card-reward-" + (index + 1).ToString(inv) : null, "sn-battle-benchmon");
                var p = view.Pokemon[id];
                var def = ctx.Cards.Pokemon[p.Def];
                tile.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var col = Add(tile, new VisualElement(), null, "sn-battle-mon-info");
                col.Add(Kit.Text(BoardHud.ShortName(stage.T("pokemon." + def.Key)), Kit.F2));
                int hp = PokemonRules.Hp(ctx, p), max = PokemonRules.MaxHp(ctx, p);
                col.Add(Kit.Pill(heart, hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
                if (click)
                {
                    tile.AddToClassList(open ? "sn-party-press" : "sn-party-no");
                    if (open) stage.OnTap(tile, SoundKeys.Decide, e => Press(index));
                }
                return tile;
            }
            if (choice.Sub == ChoiceSub.PartyFull)
            {
                Add(box, Kit.Wrapped(stage.T("screen.battle.reward.partyfull"), Kit.F2), "board-card-reward-ask", null);
                var row = Add(box, new VisualElement(), null, "sn-battle-reward-row");
                for (int i = 0; i < answers.Count; i++)
                {
                    var tile = Tile(row, answers[i].Target, i, true);
                    if (answers[i].Target == choice.Subject) tile.Q(className: "sn-battle-mon-info").Add(Kit.Badge(stage.T(fromCard ? "screen.card.reward.new" : "screen.battle.reward.caught"), KitBadgeKind.Gold, Kit.F1));
                }
                return;
            }
            // 기술 가르치기: 기술 줄 + 포켓몬마다 칸 단추(합법 답의 (포켓몬, 칸)만) + [가르치지 않기]
            string moveKey = BattleFeed.MoveNameKey(ctx, choice.Subject);
            Add(box, Kit.Wrapped(KoJosa.Fix(stage.T("screen.battle.reward.teach").Replace("{move}", stage.T(moveKey))), Kit.F2), "board-card-reward-ask", null);
            Add(box, MonParts.MoveRow(stage, ctx, choice.Subject, false, TipSide.Below, panel), "board-card-reward-move", null);
            Add(box, Kit.Wrapped(stage.T("screen.battle.reward.teach.slot"), Kit.F1), null, "kit-muted");
            var mons2 = Add(box, new VisualElement(), null, "sn-battle-reward-row");
            VisualElement slots = null;
            int tileOf = -1;
            for (int i = 0; i < answers.Count; i++)
            {
                if (answers[i].Kind != AnswerKind.Pick) continue;
                int index = i, id = answers[i].Target, slot = answers[i].Arg;
                if (id != tileOf)
                {
                    tileOf = id;
                    var tile = Tile(mons2, id, i, false);
                    slots = Add(tile.Q(className: "sn-battle-mon-info"), new VisualElement(), null, "sn-battle-slots");
                }
                int old = slot >= 0 && slot < view.Pokemon[id].Moves.Length ? view.Pokemon[id].Moves[slot] : -1;
                var tactic = old < 0 ? LegendRules.TacticAt(view.Pokemon[id], slot) : LegendTactic.None;   // 결정 195: 전설 특수기술이 차지한 빈 칸 — 가르치면 덮인다
                string label = old >= 0 ? stage.T(BattleFeed.MoveNameKey(ctx, old)) : tactic != LegendTactic.None ? stage.T(MonParts.TacticKey(tactic)) : stage.T("screen.battle.reward.teach.empty");
                var button = Add(slots, Kit.Button(label, KitButtonKind.Normal, () => Press(index)), "board-card-reward-" + (i + 1).ToString(inv), null);
                button.SetEnabled(open);
                if (tactic != LegendTactic.None)
                {
                    button.AddToClassList(MonParts.LegendSlotClass);
                    stage.Tips.Hook(button, label, MonParts.TacticTip(stage, tactic), TipSide.Below, reading: true);
                }
                if (open && old >= 0) MonParts.MoveTip(stage, ctx, button, old, TipSide.Below, panel);   // 바꿀 원래 기술의 이름 + 효과(결정 186)
            }
            for (int i = 0; i < answers.Count; i++)
            {
                if (answers[i].Kind != AnswerKind.Decline) continue;
                int index = i;
                Add(box, Kit.Button(stage.T("screen.battle.reward.teach.decline"), KitButtonKind.Normal, () => Press(index)), "board-card-reward-" + (i + 1).ToString(inv), null).SetEnabled(open);
            }
        }

        /// <summary>그 답(<see cref="Answers"/>의 색인)을 낸다. 입력이 닫혀 있거나 없는 답이면 false.</summary>
        public bool Press(int index)
        {
            var choice = Choice;
            if (stage == null || !Open || !Takes(flow, choice) || index < 0 || index >= answers.Count) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answers[index]).Accepted;
        }

        /// <summary>그 답과 같은 단추를 누른다(확인용 길잡이). 없으면 false.</summary>
        public bool Press(ChoiceAnswer answer) => answer != null && Press(answers.FindIndex(a => a.SameAs(answer)));

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
