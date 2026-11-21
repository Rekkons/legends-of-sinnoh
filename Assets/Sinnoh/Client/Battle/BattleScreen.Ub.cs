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
    /// 배틀 화면의 울트라 버스트 몫(M6 「UB 배틀 화면 묶음」 — 04 §8-8 87~92행 가운데 배틀 쪽, 박자 <see cref="UbBattleRows"/>):
    /// 기둥의 덧칩(네크로즈마 페이즈 · 포획 불가 · 프리즘 아머 · 시련 진행 · 면역 · 이번 차례의 온슬롯 카드) · DS 위의 떠 있는 것(프리즘 −n · 프리즘이 옮겨 감 · 온슬롯 그림 카드 · 「시련 통과!」 · 「이동력 +2」) ·
    /// DS 오라(면역 막 · 프리즘 방패 · 온슬롯 효과 빛). 87행 등장은 행이 아니다 — 문구(<see cref="BattleFeed"/>) · 기둥 머리 · 「포획 불가」 단추 · 조우 전환 줄무늬 색(<see cref="FieldWipe"/>).
    /// 요소 이름: <c>battle-foe-phase</c> · <c>battle-foe-uncatch</c> · <c>battle-foe-prism</c> · <c>battle-foe-trial</c> · <c>battle-foe-immune</c> · <c>battle-foe-onslaught</c> ·
    /// <c>battle-ub-prism-block</c> · <c>battle-ub-prism-shard</c>(위 띠 프리즘 칩에서 상대 도트로 나는 조각 — 결정 87 V23) · <c>battle-ub-card</c>(<c>-art</c> · <c>-text</c>) · <c>battle-ub-banner</c> · <c>battle-ub-onslaught-tip</c>(온슬롯 칩의 마우스 오버 — 그림 카드, 결정 87 V24 나).
    /// </summary>
    public sealed partial class BattleScreen
    {
        VisualElement ubBlock, ubShard, ubCard, ubCardArt, ubBanner, ubTrialChip, ubTip, ubTipFor;
        Label ubBlockText, ubCardText, ubBannerText, ubTipName, ubTipText;
        bool ubMoving;
        /// <summary>881 조각이 날아가 닿는 자리(DS 화면 안 비율 — 상대 도트의 가운데쯤).</summary>
        public const float PrismToX = 0.72f, PrismToY = 0.32f;

        /// <summary>온슬롯 칩의 마우스 오버 카드(그림 + 이름 + 효과 문구)가 떠 있나 — 확인용.</summary>
        public bool OnslaughtTipShown => ubTipFor != null;

        /// <summary>기둥에 보이는 UB 칩(확인용): 페이즈(0 = 없음) · 포획 불가 · 프리즘 개수(0 = 없음) · 시련 진행 「n/max」(없으면 null) · 시련 통과 · 면역 · 온슬롯 카드 번호(0 = 없음).</summary>
        public int PhaseShown { get; private set; }
        public bool UncatchShown { get; private set; }
        public int PrismShown { get; private set; }
        public string TrialShown { get; private set; }
        public bool TrialPassed { get; private set; }
        public bool ImmuneShown { get; private set; }
        public int OnslaughtShown { get; private set; }

        /// <summary>DS 위 떠 있는 것이 보이는 만큼(확인용): 프리즘 −n · 옮겨 감 판 · 온슬롯 카드 · 가운데 배너.</summary>
        public float BlockShown { get; private set; }
        public float MoveShown { get; private set; }
        public float CardShown { get; private set; }
        public float BannerShown { get; private set; }

        /// <summary>실버디가 빛으로 흩어진 정도(0 ~ 1 — 확인용, 결정 87 V21 나).</summary>
        public float ScatterShown { get; private set; }
        const int ScatterMotes = 14;
        static readonly Color32 ScatterLight = new Color32(0xFF, 0xE8, 0x80, 0xFF);
        static readonly Color32 ImmuneAura = new Color32(0x98, 0xD8, 0xF8, 0xFF), PrismAura = new Color32(0xF0, 0xE0, 0xFF, 0xFF), OnslaughtAura = new Color32(0xE0, 0x30, 0x28, 0xFF);

        // 배틀 화면의 포켓몬 이름(RULE-BATTLE 「폼 꼬리를 뺀다」): 「(…)」 꼬리를 빼고, 꼬리 없이 앞에 붙은 폼 이름(「울트라네크로즈마」)도 같은 종의 첫 폼 이름으로 줄인다 — 전체 이름은 기둥 이름의 마우스 오버
        string BattleName(string key)
        {
            string name = BoardHud.ShortName(stage.T(key));
            if (key == null || !key.StartsWith(PokemonKey, StringComparison.Ordinal)) return name;
            string card = key.Substring(PokemonKey.Length);
            int colon = card.IndexOf(':');
            if (colon <= 0) return name;
            string dex = card.Substring(0, colon + 1);
            // 영어 이름은 폼이 앞에 붙는다(「East Ocean Shellos」 — xlsx 원문, M9 언어 ②): 종 이름(species.<번호>)으로 끝나면 종 이름만
            int digits = 0;
            while (digits < card.Length && char.IsDigit(card[digits])) digits++;
            string species = digits > 0 ? stage.TOrNull("species." + card.Substring(0, digits)) : null;
            if (!string.IsNullOrEmpty(species) && name.Length > species.Length && name.EndsWith(" " + species, StringComparison.Ordinal)) return species;
            var ids = flow.Ctx.Cards.PokemonIds;
            for (int k = 0; k < ids.Count; k++)
            {
                string id = ids[k];
                if (!id.StartsWith(dex, StringComparison.Ordinal)) continue;
                if (id == card) return name;   // 그 종의 첫 폼이다
                string first = BoardHud.ShortName(stage.T(PokemonKey + id));
                return first.Length > 0 && name.Length > first.Length && name.EndsWith(first, StringComparison.Ordinal) ? first : name;
            }
            return name;
        }

        // DS 체력 상자의 이름(결정 87 V22 나): 앞에 붙은 폼 이름은 줄이지 않는다(「울트라네크로즈마」 — 칸에 안 들면 DsMon.Narrow가 글자 폭을 좁힌다). 「(…)」 꼬리는 그대로 뺀다
        string BoxName(BattleMon mon) => mon.Exists ? BoardHud.ShortName(stage.T(PokemonKey + flow.Ctx.Cards.Pokemon[mon.Def].Key)) : "";

        // [가방] 자리의 회색 「포획 불가」 까닭(없으면 null — 평소의 「던질 수 있는 볼이 없다」)
        string UncatchKey(BattleFeed feed, BattleMon foe)
        {
            if (feed == null) return null;
            if (feed.Kind == BattleKind.UltraBeast || feed.Kind == BattleKind.Necrozma) return "screen.battle.bag.off.ub";
            var state = feed.Source;
            if (feed.Kind == BattleKind.Trial && state != null && state.Battle != null && foe.Exists && UbTrial.Guards(state, flow.Ctx, state.Battle, foe.Id)) return "screen.battle.bag.off.trial";
            return null;
        }

        void BuildUb()
        {
            ubBlock = Add(dsBox, Kit.Badge("", KitBadgeKind.Silver, Kit.F3), "battle-ub-prism-block", "sn-battle-ub-block");
            ubBlockText = (Label)ubBlock;
            // 881 옮겨 감(결정 87 V23 다): DS 위의 얇은 판 대신 위 띠 프리즘 칩(BoardUb — 배틀 화면이 덮지 않는다)에서 조각이 상대 UB로 날아간다. 조각은 화면 전체 좌표로 옮긴다
            ubShard = Add(page, new VisualElement(), "battle-ub-prism-shard", "sn-battle-ub-shard");
            ubShard.AddToClassList("sn-battle-ub-shard-free");
            Unpick(ubShard);
            FxUi.Fade(ubShard, 0f);
            ubCard = Add(dsBox, Kit.Panel(stage.Skin), "battle-ub-card", "sn-battle-ub-card");
            // 원작 온슬롯 카드의 그림(여섯 장이 같은 그림 — 결정 87 V24 나) · 제목 · 그 카드의 짧은 이름
            var art = GenAssets.Load<Texture2D>(GenAssets.OnslaughtArt);
            ubCardArt = Add(ubCard, new VisualElement(), "battle-ub-card-art", "sn-battle-ub-card-art");
            if (art != null) ubCardArt.style.backgroundImage = new StyleBackground(art);
            var title = Add(ubCard, Kit.Text(stage.T("screen.battle.onslaught.title"), Kit.F2), null, "kit-title");
            title.AddToClassList("sn-battle-ub-card-title");
            ubCardText = Add(ubCard, Kit.Text("", Kit.F2), "battle-ub-card-text", "sn-battle-ub-card-text");
            // 온슬롯 칩의 마우스 오버: 키트 툴팁(글뿐) 대신 그림 카드 — 툴팁 창틀 · 칩 아래
            ubTip = Add(page, new VisualElement(), "battle-ub-onslaught-tip", "kit-tip");
            ubTip.AddToClassList("sn-battle-ub-tip");
            Kit.SetFrame(ubTip, stage.Skin.Tip);
            var tipArt = Add(ubTip, new VisualElement(), "battle-ub-onslaught-tip-art", "sn-battle-ub-tip-art");
            if (art != null) tipArt.style.backgroundImage = new StyleBackground(art);
            var tipTitle = Add(ubTip, Kit.Text(stage.T("screen.battle.onslaught.title"), Kit.F2), null, "kit-title");
            tipTitle.AddToClassList("sn-battle-ub-card-title");
            ubTipName = Add(ubTip, Kit.Text("", Kit.F2), "battle-ub-onslaught-tip-name", null);
            ubTipText = Add(ubTip, Kit.Wrapped("", Kit.F2), "battle-ub-onslaught-tip-text", "sn-battle-ub-tip-text");
            Unpick(ubTip);
            ubTip.style.display = DisplayStyle.None;
            ubTip.RegisterCallback<GeometryChangedEvent>(e => PlaceOnslaughtTip());
            ubTipFor = null;
            ubBanner = Add(dsBox, Kit.Badge("", KitBadgeKind.Gold, Kit.F3), "battle-ub-banner", "sn-battle-ub-banner");
            ubBannerText = (Label)ubBanner;
            foreach (var e in new[] { ubBlock, ubCard, ubBanner })
            {
                Unpick(e);
                FxUi.Fade(e, 0f);
            }
        }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        // 드러난(대기열이 이미 지난) 이벤트 가운데 마지막 T
        T LastShown<T>(BattleFeed feed) where T : GameEvent
        {
            T last = null;
            foreach (var e in feed.Events)
                if (e is T t && !hidden.Contains(e)) last = t;
            return last;
        }

        // 상대 기둥의 UB 덧칩(FillExtras가 상대 줄에 부른다)
        void FillUb(BattleFeed feed, VisualElement line, BattleMon foe, GameState state)
        {
            var inv = CultureInfo.InvariantCulture;
            PhaseShown = PrismShown = OnslaughtShown = 0;
            UncatchShown = TrialPassed = ImmuneShown = false;
            TrialShown = null;
            ubTrialChip = null;
            var ub = state.Ub;
            if (ub == null) return;
            bool beast = feed.Kind == BattleKind.UltraBeast || feed.Kind == BattleKind.Necrozma;
            if (feed.Kind == BattleKind.Necrozma)
            {
                PhaseShown = ub.NecrozmaPhase;
                var chip = Add(line, Kit.Badge(stage.T("screen.battle.phase.chip").Replace("{n}", PhaseShown.ToString(inv)).Replace("{max}", UbNecrozma.Phases.ToString(inv)), KitBadgeKind.Gold, Kit.F2), "battle-foe-phase", null);
                stage.Tips.Hook(chip, stage.T("screen.battle.foe.necrozma"), stage.T("screen.battle.phase.tip"), TipSide.Below, reading: true);
            }
            if (beast)
            {
                UncatchShown = true;
                var chip = Add(line, Kit.Badge(stage.T("screen.battle.uncatch.chip"), KitBadgeKind.Silver, Kit.F2), "battle-foe-uncatch", null);
                stage.Tips.Hook(chip, stage.T("screen.battle.uncatch.chip"), stage.T("screen.battle.uncatch.tip"), TipSide.Below, reading: true);
                int prism = feed.Kind == BattleKind.Necrozma ? ub.PrismUltraBurst : ub.PrismUltraBeast;
                if (prism > 0)
                {
                    PrismShown = prism;
                    var p = Add(line, Kit.Badge(stage.T("screen.battle.prism.chip").Replace("{n}", prism.ToString(inv)), KitBadgeKind.Silver, Kit.F2), "battle-foe-prism", null);
                    stage.Tips.Hook(p, stage.T("screen.battle.prism.title"), stage.T("screen.battle.prism.tip").Replace("{cut}", UbBattle.Reduction(prism).ToString(inv)), TipSide.Below, reading: true);
                }
            }
            if (feed.Kind == BattleKind.Trial && feed.TrialLake >= 0)
            {
                int lake = feed.TrialLake;
                var last = LastShown<TrialProgress>(feed);
                bool before = ub.TrialsOvercome[lake] && !feed.Events.Exists(e => e is TrialProgress);   // 앞의 배틀에서 이미 넘겼다(판에 한 번 — A55 ②)
                TrialPassed = before || (last != null && last.Done);
                int goal = lake == (int)Shard.Emotion ? 1 : lake == (int)Shard.Knowledge ? UbTrial.KnowledgeGoal : UbTrial.WillpowerGoal;
                if (TrialPassed)
                {
                    ubTrialChip = Add(line, Kit.Badge(stage.T("screen.battle.trial.passed"), KitBadgeKind.Ok, Kit.F2), "battle-foe-trial", null);
                    stage.Tips.Hook(ubTrialChip, stage.T("screen.battle.trial." + lake.ToString(inv)), stage.T("screen.battle.trial.passed.tip"), TipSide.Below, reading: true);
                }
                else
                {
                    int n = last != null ? Math.Min(last.Count, goal) : 0;
                    TrialShown = n.ToString(inv) + "/" + goal.ToString(inv);   // 글리프 「/」만 코드
                    ubTrialChip = Add(line, Kit.Badge(stage.T("screen.battle.trial." + lake.ToString(inv)) + " " + TrialShown, KitBadgeKind.Plain, Kit.F2), "battle-foe-trial", null);
                    stage.Tips.Hook(ubTrialChip, stage.T("screen.battle.trial." + lake.ToString(inv)), stage.T("screen.battle.trial.tip." + lake.ToString(inv)), TipSide.Below, reading: true);
                }
                FxUi.OriginLeft(ubTrialChip);
                // 면역: 넘기기 전 · 넘긴 그 차례까지(p.26 「until the turn after」)
                ImmuneShown = foe.Exists && (!TrialPassed || (state.Battle != null && UbTrial.Guards(state, flow.Ctx, state.Battle, foe.Id)));
                if (ImmuneShown)
                {
                    var chip = Add(line, Kit.Badge(stage.T("screen.battle.immune.chip"), KitBadgeKind.Gold, Kit.F2), "battle-foe-immune", null);
                    stage.Tips.Hook(chip, stage.T("screen.battle.immune.chip"), stage.T("screen.battle.immune.tip"), TipSide.Below, reading: true);
                }
            }
            // 이번 차례의 온슬롯 카드(89행): 뽑힌 뒤 내 쪽의 다음 기술까지(「다음에 공격받을 때」 카드도 그 공격까지 — A56 ⑤)
            OnslaughtDrawn drawn = null;
            foreach (var e in feed.Events)
            {
                if (hidden.Contains(e)) continue;
                if (e is OnslaughtDrawn od) drawn = od;
                else if (e is MoveUsed mu && mu.Side == feed.MySide) drawn = null;
            }
            if (drawn != null && !feed.Ended)
            {
                OnslaughtShown = drawn.Effect;
                var chip = Add(line, Kit.Badge(stage.T(BattleFeed.OnslaughtKey(drawn.Effect)), KitBadgeKind.Red, Kit.F2), "battle-foe-onslaught", null);
                chip.RegisterCallback<PointerEnterEvent>(e => HoverOnslaught(true));
                chip.RegisterCallback<PointerLeaveEvent>(e => { if (ubTipFor == e.currentTarget) HoverOnslaught(false); });
                chip.RegisterCallback<DetachFromPanelEvent>(e => { if (ubTipFor == e.currentTarget) HoverOnslaught(false); });
            }
        }

        /// <summary>온슬롯 칩(<c>battle-foe-onslaught</c>)의 마우스 오버 카드를 띄우거나 걷는다(칩의 포인터 콜백 · 캡처). 칩이 없으면 걷기만 한다.</summary>
        public void HoverOnslaught(bool on)
        {
            if (ubTip == null || page == null) return;
            var chip = on && OnslaughtShown > 0 ? page.Q("battle-foe-onslaught") : null;
            ubTipFor = chip;
            if (chip == null) { ubTip.style.display = DisplayStyle.None; return; }
            ubTipName.text = stage.T(BattleFeed.OnslaughtKey(OnslaughtShown));
            ubTipText.text = KoText.JoinWords(OnslaughtText(OnslaughtShown));
            // 크기가 정해지기 전에는 보이지 않게 두고, 배치가 끝나면(GeometryChanged) 자리를 잡아 보인다 — 키트 툴팁과 같은 순서
            ubTip.style.visibility = Visibility.Hidden;
            ubTip.style.display = DisplayStyle.Flex;
            ubTip.BringToFront();
            PlaceOnslaughtTip();
        }

        void PlaceOnslaughtTip()
        {
            if (ubTipFor == null || ubTipFor.panel == null || ubTip.panel == null) return;
            var size = ubTip.layout;
            if (float.IsNaN(size.width) || float.IsNaN(size.height) || size.width <= 0f) return;   // 아직 배치 전
            Rect target = page.WorldToLocal(ubTipFor.worldBound);
            if (!ubTip.customStyle.TryGetValue(TipGap, out float gap)) gap = 0;
            var spot = TipPlace.Place(target.x, target.y, target.width, target.height, size.width, size.height, page.layout.width, page.layout.height, TipSide.Below, gap);
            ubTip.style.left = spot.X;
            ubTip.style.top = spot.Y;
            ubTip.style.visibility = Visibility.Visible;
        }

        static readonly CustomStyleProperty<float> TipGap = new CustomStyleProperty<float>("--kit-tip-gap");

        // 온슬롯 카드의 효과 문구(데이터 ultra_onslaught.<n>.text.1 · .2 — 줄마다)
        string OnslaughtText(int effect)
        {
            string n = Math.Max(1, Math.Min(6, effect)).ToString(CultureInfo.InvariantCulture);
            string one = stage.TOrNull("ultra_onslaught." + n + ".text.1") ?? "", two = stage.TOrNull("ultra_onslaught." + n + ".text.2");
            return two != null ? one + "\n" + two : one;
        }

        // 떠 있는 것의 박자(프레임마다 — AnimateRows가 부른다)
        void AnimateUb()
        {
            if (ubBlock == null) return;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var inv = CultureInfo.InvariantCulture;
            float unit = dsBox != null && dsBox.layout.width > 0f ? dsBox.layout.width / DsStage.Width : 2f;   // DS px 하나의 화면 px
            // 88행 프리즘 −n(880)
            BlockShown = 0f;
            var block = UbBattleRows.Playing(seq, UbBattleRows.Prism);
            if (block != null && UbBattleRows.First<PrismReduced>(block) is PrismReduced pr)
            {
                PrismBeats.Block(local - block.StartMs, block.Ms, out _, out float chip, out float scale);
                ubBlockText.text = stage.T("screen.battle.prism.block").Replace("{n}", pr.Amount.ToString(inv));
                BlockShown = chip;
                FxUi.Scale(ubBlock, scale, scale);
            }
            FxUi.Fade(ubBlock, BlockShown);
            // 88행 옮겨 감(881): 「네크로즈마 a → a−1」 · 조각이 날아 「울트라 비스트 b → b+1」
            MoveShown = 0f;
            var move = UbBattleRows.Playing(seq, UbBattleRows.PrismMove);
            if (move != null && UbBattleRows.First<PrismMoved>(move) is PrismMoved pm && pm.Before != null && pm.After != null)
            {
                PrismBeats.Move(local - move.StartMs, move.Ms, out float panel, out float fly, out bool arrived);
                MoveShown = panel;
                // 위 띠 칩: 조각이 떠나면 네크로즈마 쪽 −1, 닿으면 UB 쪽 +1 · 그동안 칩이 커져 눈에 띈다
                var hudUb = flow.Hud != null ? flow.Hud.Ub : null;
                var chip = hudUb != null ? hudUb.PrismChip : null;
                if (hudUb != null) hudUb.ShowPrismMove(true, fly >= 0f ? pm.After[0] : pm.Before[0], arrived ? pm.After[1] : pm.Before[1], 1f + 0.25f * panel);
                ubMoving = true;
                var from = chip != null ? chip.worldBound : default;
                var box = dsBox.worldBound;
                bool laid = chip != null && !float.IsNaN(from.width) && from.width > 0f && !float.IsNaN(box.width) && box.width > 0f;
                FxUi.Fade(ubShard, fly >= 0f && fly < 1f && laid ? 1f : 0f);
                if (laid && fly >= 0f)
                {
                    var a = page.WorldToLocal(from.center);
                    var b = page.WorldToLocal(new Vector2(box.xMin + box.width * PrismToX, box.yMin + box.height * PrismToY));
                    float x = Mathf.Lerp(a.x, b.x, fly);
                    float y = Mathf.Lerp(a.y, b.y, fly) - Mathf.Sin(Mathf.PI * fly) * 10f * unit;
                    FxUi.Move(ubShard, x, y);
                }
            }
            else if (ubMoving)
            {
                ubMoving = false;
                FxUi.Fade(ubShard, 0f);
                if (flow.Hud != null && flow.Hud.Ub != null) flow.Hud.Ub.ShowPrismMove(false, 0, 0, 1f);
            }
            // 89행 온슬롯 카드(890): 상자 옆에서 떨며 떠오름 → 가로 뒤집힘 → 효과 빛 → 줄어 상자 옆에 붙음
            CardShown = 0f;
            var card = UbBattleRows.Playing(seq, UbBattleRows.Onslaught);
            if (card != null && UbBattleRows.First<OnslaughtDrawn>(card) is OnslaughtDrawn od)
            {
                var pose = OnslaughtBeats.At(local - card.StartMs, card.Ms);
                CardShown = pose.Alpha;
                ubCardText.text = pose.Front ? stage.T(BattleFeed.OnslaughtKey(od.Effect)) : "";
                ubCardText.style.visibility = ubCardArt.style.visibility = pose.Front ? Visibility.Visible : Visibility.Hidden;
                ubCard.EnableInClassList("sn-battle-ub-card-back", !pose.Front);
                FxUi.Scale(ubCard, pose.ScaleX * pose.Scale, pose.Scale);
                FxUi.Move(ubCard, (pose.Shake - 24f * pose.Dock) * unit, ((1f - pose.Rise) * 12f - 14f * pose.Dock) * unit);
            }
            FxUi.Fade(ubCard, CardShown);
            // 92행 시련(920): 진행 칩이 톡 · 넘기면 가운데 「시련 통과!」 / 수호신 격파(921): 「모든 트레이너 이동력 +2」
            BannerShown = 0f;
            float bannerScale = 1f;
            var trial = UbBattleRows.Playing(seq, UbBattleRows.Trial);
            var tp = trial != null ? UbBattleRows.First<TrialProgress>(trial) : null;
            if (ubTrialChip != null) { float s = tp != null ? TrialBeats.Pop(local - trial.StartMs) : 1f; FxUi.Scale(ubTrialChip, s, s); }
            if (tp != null && tp.Done)
            {
                TrialBeats.Pass(local - trial.StartMs, trial.Ms, out _, out _, out float banner, out bannerScale);
                BannerShown = banner;
                ubBannerText.text = stage.T("screen.battle.trial.banner");
            }
            var guardian = UbBattleRows.Playing(seq, UbBattleRows.Guardian);
            if (guardian != null)
            {
                TrialBeats.Guardian(local - guardian.StartMs, guardian.Ms, out float banner, out bannerScale);
                BannerShown = banner;
                ubBannerText.text = stage.T("screen.battle.guardian.banner").Replace("{n}", UbTrial.MovePerGuardian.ToString(inv));
            }
            FxUi.Fade(ubBanner, BannerShown);
            FxUi.Scale(ubBanner, bannerScale, bannerScale);
        }

        // DS 오라: 시련 전의 면역 막(옅은 하늘색 1px — 넘기면 하얗게 번쩍이며 깨진다) · 프리즘 방패(880 — 막은 쪽) · 온슬롯 효과 빛(890 — 붉은 오라 한 번)
        void PaintUb(DsScene scene)
        {
            var enemy = scene.Enemy;
            if (enemy == null || flow.Battle == null) return;
            ScatterShown = 0f;
            if (enemy.Key == UbTrial.Silvally && enemy.Sink > 0f)
            {
                // 실버디 격파(결정 87 V21 나 — 93행 축복 930의 앞): 가라앉는 대신 금빛으로 물들어 빛 알갱이로 흩어진다(기절 단계의 박자 그대로 — 새 행 없음)
                float t = Mathf.Clamp01(enemy.Sink);
                ScatterShown = t;
                enemy.Sink = 0f;
                enemy.Aura = ScatterLight;
                enemy.AuraPx = t > 0.5f ? 3 : 2;
                enemy.Tint = new Color32(ScatterLight.r, ScatterLight.g, ScatterLight.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(t * 1.6f) * 255f));
                if (t >= 0.85f) enemy.Standing = false;
                if (t < 1f)
                {
                    if (scene.Rects == null) scene.Rects = new List<DsRect>();
                    for (int k = 0; k < ScatterMotes; k++)
                    {
                        float angle = (k * 137.5f + 20f) * Mathf.Deg2Rad, r = (10f + (k % 4) * 9f) * t * 1.8f;
                        int size = t > 0.7f ? 1 : 2;
                        scene.Rects.Add(new DsRect { X = DsStage.EnemyFoot.x + Mathf.RoundToInt(Mathf.Cos(angle) * r), Y = DsStage.EnemyFoot.y - 30 - Mathf.RoundToInt(Mathf.Abs(Mathf.Sin(angle)) * r + t * 14f), W = size, H = size, Color = k % 3 == 0 ? new Color32(0xFF, 0xFF, 0xFF, 0xFF) : ScatterLight });
                    }
                }
            }
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var foe = Shown(seen.Active[1 - flow.Battle.MySide]);
            bool auraOn = enemy.AuraPx > 0 && EnemyAura != AuraKind.Fan;
            var trial = UbBattleRows.Playing(seq, UbBattleRows.Trial);
            var tp = trial != null ? UbBattleRows.First<TrialProgress>(trial) : null;
            if (tp != null && tp.Done)
            {
                TrialBeats.Pass(local - trial.StartMs, trial.Ms, out float shield, out float flash, out _, out _);
                if (shield > 0f)
                {
                    enemy.Aura = flash > 0.5f ? new Color32(0xFF, 0xFF, 0xFF, 0xFF) : ImmuneAura;
                    enemy.AuraPx = flash > 0.5f ? TrialBeats.CrackPx : TrialBeats.ShieldPx;
                    enemy.Tint = new Color32(0xFF, 0xFF, 0xFF, (byte)Mathf.RoundToInt(Mathf.Clamp01(flash) * 160f));
                }
            }
            else if (ImmuneShown && !auraOn && enemy.Standing)
            {
                enemy.Aura = ImmuneAura;
                enemy.AuraPx = TrialBeats.ShieldPx;
            }
            var block = UbBattleRows.Playing(seq, UbBattleRows.Prism);
            if (block != null && UbBattleRows.First<PrismReduced>(block) is PrismReduced pr && pr.PokemonId == foe.Id)
            {
                PrismBeats.Block(local - block.StartMs, block.Ms, out float shield, out _, out _);
                if (shield > 0f)
                {
                    enemy.Aura = PrismAura;
                    enemy.AuraPx = shield > 0.5f ? 2 : 1;
                    enemy.Tint = new Color32(PrismAura.r, PrismAura.g, PrismAura.b, (byte)Mathf.RoundToInt(shield * 110f));
                }
            }
            var card = UbBattleRows.Playing(seq, UbBattleRows.Onslaught);
            if (card != null)
            {
                var pose = OnslaughtBeats.At(local - card.StartMs, card.Ms);
                if (pose.Glow > 0f)
                {
                    enemy.Aura = OnslaughtAura;
                    enemy.AuraPx = pose.Glow > 0.5f ? 2 : 1;
                }
            }
        }
    }
}
