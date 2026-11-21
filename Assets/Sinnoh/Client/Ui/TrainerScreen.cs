using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S40 트레이너 카드 판(04 §2 「트레이너 카드 판(등급·잠김·해금·능력)」, rulings A20-2): 보드 HUD의 [트레이너 카드]로 여는 창. 그 좌석이 고른 트레이너 카드 한 장을 크게 보인다.
    /// <list type="bullet">
    /// <item>왼쪽: 직업 그림(주인공 얼굴이 아니라 여기서만 직업 그림 — 결정 54 ④) · 직업 이름 · 등급(브론즈 ★☆☆ → 실버 ★★☆ → 골드 ★★★).</item>
    /// <item>오른쪽: 능력 둘(파랑 · 빨강) — 색 칸 · 이름 · 「잠김」/「해금」 · 효과 문구(줄로 다 적는다). 잠긴 능력은 흐리게. 파랑은 실버, 빨강은 골드에서 해금된다.</item>
    /// <item>아래 띠: 다음 등급의 조건(51장 모두 같다 — 적당한 포켓몬 → 실버, 강한 포켓몬 → 골드).</item>
    /// </list>
    /// 원작 카드 틀(등급별 바탕 · 자물쇠 그림)은 Gen에 없어 키트 부품으로 조합한다 [가정]. 모두 보기에 있는 공개 정보다(카드는 모두가 본다) — 고치는 것은 없다.
    /// 능력을 쓰는 단추 · 등급이 오르는 연출(04 §8-8 48행) · 카드 전용 아이템은 M6이다. 트레이너 카드를 끈 판에서는 열리지 않는다.
    /// 요소 이름: <c>board-trainer</c>·<c>board-trainer-panel</c>·<c>board-trainer-name</c>·<c>board-trainer-tier</c>·<c>board-trainer-art</c>·
    /// <c>board-trainer-ability-&lt;1|2&gt;</c>·<c>board-trainer-ability-&lt;1|2&gt;-lock</c>·<c>board-trainer-next</c>·<c>board-trainer-close</c>.
    /// </summary>
    public sealed class TrainerScreen : IUiScreen
    {
        /// <summary>등급 수(rulings A20-2): 1 브론즈 · 2 실버 · 3 골드.</summary>
        public const int Tiers = 3;

        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        int painted = -1;

        /// <summary>카드의 주인 좌석.</summary>
        public readonly int Seat;

        public TrainerScreen(GameFlow flow, int seat)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Seat = seat;
        }

        public string Name => "board-trainer";

        /// <summary>지금 그린 등급(1 ~ 3).</summary>
        public int Tier { get; private set; }

        /// <summary>그 능력 칸(1 = 파랑 · 2 = 빨강)이 그 등급에서 해금돼 있나(A20-2: 파랑은 실버부터, 빨강은 골드부터).</summary>
        public static bool Unlocked(int tier, int slot) => tier > slot;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = Add(page, new VisualElement(), "board-trainer-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseTrainer());   // 창 밖(가운데 필드)을 누르면 닫힌다
            panel = Add(page, Kit.Panel(stage.Skin), "board-trainer-panel", "sn-trainer-panel");
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>보기가 바뀌었다: 다시 그린다(등급이 오르면 잠김 표시가 바뀐다).</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null) return;
            if (!flow.HasTrainerCard(Seat))
            {
                flow.CloseTrainer();
                return;
            }
            if (painted == flow.Client.Version) return;
            painted = flow.Client.Version;
            var view = flow.Client.View;
            var inv = CultureInfo.InvariantCulture;
            var seat = view.Seats[Seat];
            var def = flow.Ctx.Cards.TrainerCards[seat.TrainerCard];
            Tier = Mathf.Clamp(seat.TrainerCardTier, 1, Tiers);
            panel.Clear();

            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            bool mine = Seat == flow.Client.Seat;
            string title = mine ? stage.T("screen.trainer.title") : stage.T("screen.trainer.title.other").Replace("{name}", flow.SeatName(Seat));
            var titleText = Add(head, Kit.Text(title, Kit.FTitle), null, "kit-title");
            if (!mine) FaceBox.Owner(flow, Seat, titleText);
            Add(head, Kit.Button(stage.T("screen.trainer.close"), KitButtonKind.Main, () => flow.CloseTrainer()), "board-trainer-close", null);

            var body = Add(panel, new VisualElement(), null, "sn-trainer-body");
            // 왼쪽: 직업 그림 · 이름 · 등급
            var left = Add(body, new VisualElement(), null, "sn-trainer-left");
            var face = Add(left, new VisualElement(), "board-trainer-art", "sn-trainer-face");
            var portrait = Add(face, new VisualElement(), null, "sn-trainer-art");
            string frontKey = BoardPieces.FrontKey(def.Key);
            var tex = frontKey != null ? GenAssets.Load<Texture2D>(frontKey) : null;
            if (tex != null) portrait.style.backgroundImage = new StyleBackground(tex);
            Add(left, Kit.Text(stage.T("trainer_card." + def.Key), Kit.F2), "board-trainer-name", "sn-trainer-name");
            var tier = Add(left, Kit.Badge(stage.T("screen.trainer.tier." + Tier.ToString(inv)) + " " + Stars(Tier), Tier == 3 ? KitBadgeKind.Gold : Tier == 2 ? KitBadgeKind.Silver : KitBadgeKind.Plain, Kit.F2), "board-trainer-tier", null);
            stage.Tips.Hook(tier, null, stage.T("screen.trainer.tier.tip"), TipSide.Below, reading: true);
            // 카드가 쌓는 것(M6 — 공개 값): 앰프(guitarist) · 장작(kindler) · 랜드마크 투어·전설 스냅(tourist). 그 카드이거나 0보다 크면
            var state = new List<string>();
            if (seat.Amp >= 0 || def.Key == "guitarist") state.Add(stage.T("screen.trainer.state.amp").Replace("{n}", Math.Max(0, seat.Amp).ToString(inv)).Replace("{max}", TrainerRules.AmpMax.ToString(inv)));
            if (seat.Firewood > 0 || def.Key == "kindler") state.Add(stage.T("screen.trainer.state.firewood").Replace("{n}", seat.Firewood.ToString(inv)));
            if (seat.Tour != 0 || def.Key == "tourist") state.Add(stage.T("screen.trainer.state.tour").Replace("{n}", Bits(seat.Tour).ToString(inv)).Replace("{max}", TrainerRules.Landmarks(flow.Ctx).Count.ToString(inv)));
            if (seat.Snaps.Count > 0 || def.Key == "tourist") state.Add(stage.T("screen.trainer.state.snaps").Replace("{n}", seat.Snaps.Count.ToString(inv)));
            if (state.Count > 0)
            {
                var line = Add(left, new VisualElement(), "board-trainer-state", "sn-trainer-state");
                foreach (string s in state) line.Add(Kit.Badge(s, KitBadgeKind.Plain, Kit.F2));
            }

            // 오른쪽: 능력 둘(파랑 · 빨강)
            var right = Add(body, new VisualElement(), null, "sn-trainer-right");
            foreach (var ability in def.Abilities)
            {
                string slot = ability.Slot.ToString(inv);
                string key = "trainer_ability." + def.Key + "." + slot;
                bool on = Unlocked(Tier, ability.Slot);
                var block = Add(right, new VisualElement(), "board-trainer-ability-" + slot, "sn-trainer-ability");
                if (!on) block.AddToClassList("sn-trainer-locked");
                var line = Add(block, new VisualElement(), null, "sn-village-row");
                var name = Add(line, new VisualElement(), null, "sn-party-line");
                Add(name, new VisualElement(), null, ability.Color == AbilityColor.Blue ? "sn-ability-blue" : "sn-ability-red");
                Add(name, Kit.Text(stage.T(key), Kit.F2), null, "kit-title");
                var lockBadge = Add(line, Kit.Badge(stage.T(on ? "screen.trainer.unlocked" : "screen.trainer.locked"), on ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F2), "board-trainer-ability-" + slot + "-lock", null);
                if (!on) stage.Tips.Hook(lockBadge, null, stage.T("screen.trainer.locked.tip." + slot), TipSide.Below, reading: true);
                Add(block, Kit.Wrapped(stage.T(key + ".text"), Kit.F2), "board-trainer-ability-" + slot + "-text", "sn-trainer-text");
            }
            // 이 카드로 받은 전용 아이템(rulings G1 — 그림 + 이름, 마우스를 올리면 효과)
            if (seat.CardItems.Count > 0)
            {
                var items = Add(right, new VisualElement(), "board-trainer-items", "sn-party-line");
                Add(items, Kit.Text(stage.T("screen.party.carditems"), Kit.F2), null, "kit-muted");
                foreach (int item in seat.CardItems)
                {
                    if (item < 0 || item >= flow.Ctx.Items.Items.Count) continue;
                    var def2 = flow.Ctx.Items.Items[item];
                    var chip = Add(items, new VisualElement(), null, "sn-party-chip");
                    string icon = WorkUi.IconKey(def2);
                    if (icon != null) WorkUi.Icon(chip, icon, "sn-icon");
                    chip.Add(Kit.Text(stage.T(def2.Key), Kit.F2));
                    stage.Tips.Hook(chip, stage.T(def2.Key), stage.TOrNull(def2.Key + ".text"), TipSide.Below, reading: true);
                }
            }

            // 아래 띠: 다음 등급의 조건
            var next = Add(panel, new VisualElement(), "board-trainer-next", "sn-trainer-next");
            Add(next, Kit.Wrapped(stage.T("screen.trainer.next." + Tier.ToString(inv)), Kit.F2), null, null);
        }

        static int Bits(int mask)
        {
            int n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        /// <summary>등급의 별(★ = 채운 별, ☆ = 빈 별 — 기호라 문자열 표에 두지 않는다).</summary>
        public static string Stars(int tier)
        {
            tier = Mathf.Clamp(tier, 0, Tiers);
            return new string('★', tier) + new string('☆', Tiers - tier);
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
