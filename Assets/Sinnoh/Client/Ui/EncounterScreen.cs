using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 조우 고르기(S45, 04 §4-1 — 목업 r3 encounter): 찾아보기로 나온 조우 카드 1~3장 가운데 배틀할 한 마리를 고른다. 보드 HUD 위에 여는 덮개 창이다 —
    /// 위 띠와 아래 판은 그대로 보이고(HUD의 입력은 창이 열려 있는 동안 닫힌다) 가운데 필드만 덮는다.
    /// <list type="bullet">
    /// <item>머리: 묻는 말 · 칸의 지형·기후 · 「반드시 배틀합니다」 · 공통 값(포획률 · 여행 점수 · 보상 규토리 — 한 번에 나온 카드는 등급이 같아 값도 같다. 등급이 섞이면 적지 않는다).</item>
    /// <item>카드: 등급 배지 · 이름 · 도트 · 타입 · ♥·⌃⌃ · ★ 고유 기술 줄(효과문은 줄 아래 툴팁, §10-2) · 배울 수 있는 타입 · [배틀!].</item>
    /// <item>아래 줄: 「배틀 전 준비」 볼 재고 + [제작](21행 — 만들 수 있는 것이 있을 때. 제작 창 S26을 이 창 위에 열고, 닫으면 카드가 그대로 남는다).</item>
    /// </list>
    /// 규칙을 세지 않는다: 카드와 [배틀!]은 엔진의 합법 답(<c>Client.Legal</c> — 카드, 줄 안 자리) 그대로이고, 공통 값은 엔진 함수(<see cref="BattleRules.CatchRate"/>·<see cref="BattleRules.RewardJp"/>)와
    /// 그 칸 조우 토큰의 고정물 데이터에서 읽는다.
    /// <para>연출(04 §8-8 21행): 창은 카드 공개 단계가 시작할 때 열린다(<see cref="GameFlow"/>가 연다). 카드는 뒷면(등급 그림)으로 놓였다가 왼쪽부터 250ms씩 뒤집히고(가로 늘이기),
    /// 강한 · 전설 카드는 그 뒤에 떠올라 느리게 뒤집히고 쿵 떨어져 눌렸다 편다(M9 다듬기 2). 고르면 그 카드가 커지고 나머지는 뒷면으로 뒤집혀 아래(덱)로 쓸려 간 채
    /// 조우 전환(22행)의 줄무늬가 닫힐 때까지 그 모습이다. 모습은 화면 대기열의 시각에서만 나온다(<see cref="EncounterCards"/>). 마우스를 올린 카드는 살짝 들린다(내 화면만 — 「끄기」면 없다).</para>
    /// 요소 이름: <c>encounter</c>·<c>encounter-panel</c>·<c>encounter-ask</c>·<c>encounter-place</c>·<c>encounter-rate</c>·<c>encounter-jp</c>·<c>encounter-reward</c>·
    /// <c>encounter-card-&lt;n&gt;</c>·<c>encounter-card-&lt;n&gt;-rate</c>(카드별 포획률 — 결정 105)·<c>encounter-card-&lt;n&gt;-back</c>·<c>encounter-card-&lt;n&gt;-move</c>·<c>encounter-pick-&lt;n&gt;</c>·<c>encounter-prep</c>·<c>encounter-ball-&lt;키&gt;</c>.
    /// </summary>
    public sealed class EncounterScreen : IWatchScreen
    {
        readonly GameFlow flow;
        readonly int watch;
        readonly List<ChoiceAnswer> cards = new List<ChoiceAnswer>();
        readonly List<Button> buttons = new List<Button>();
        readonly List<VisualElement> cardElements = new List<VisualElement>(), backs = new List<VisualElement>();
        readonly List<bool> heavy = new List<bool>();
        readonly List<float> lifts = new List<float>();
        readonly List<int> liftTweens = new List<int>();
        UiStage stage;
        bool paintedOpen;

        /// <summary>고른 뒤 그 카드가 커지는 만큼.</summary>
        public const float PickScale = 1.06f;

        /// <summary>M9 다듬기 2(720 벌 px — 1080 벌은 1.5배): 무거운 카드가 떠오르는 높이 · 마우스 오버로 들리는 높이 · 고르지 않은 카드가 덱으로 쓸려 내려가는 거리.</summary>
        public const float RisePx = 24f, LiftPx = 6f, AwayPx = 48f;

        /// <summary>그 카드가 마우스 오버로 들린 만큼(0 ~ 1).</summary>
        public float LiftOf(int index) => index >= 0 && index < lifts.Count ? lifts[index] : 0f;

        /// <summary>그 카드가 강한 · 전설(늦게 · 무겁게)인가.</summary>
        public bool HeavyAt(int index) => index >= 0 && index < heavy.Count && heavy[index];

        /// <summary>지금 앞면이 보이는 카드 수(왼쪽부터 뒤집힌다).</summary>
        public int FaceUp { get; private set; }

        /// <summary>고른 카드(왼쪽에서부터 0, 아직 안 골랐으면 −1) — 고른 뒤 그 카드가 커지고 나머지는 흐려진다.</summary>
        public int Picked { get; private set; } = -1;

        /// <param name="watch">보는 좌석(결정 120 ① · 플레이 시험 T7 — 남의 조우 고르기를 내 창과 같은 카드로 보기만, −1 = 내 창). 보기 창은 [배틀!] · [제작] 대신 「○○이 고르는 중」이고,
        /// 방 판이면 그 좌석이 가리키는 카드에 좌석 색 테두리(P09), 고르면 내 창처럼 그 카드가 커지고 나머지는 덱으로 쓸려 간다 — 전환 22행이 닫히면 닫힌다.</param>
        public EncounterScreen(GameFlow flow, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
        }

        public int Watched => watch;

        /// <summary>보기 창: 그 좌석의 이 조우 고르기가 아직 열려 있거나, 고른 뒤 전환(22행)이 아직 닫히지 않았다.</summary>
        public bool Live
        {
            get
            {
                if (watch < 0 || flow.Client == null || flow.Client.View == null) return false;
                var c = PeekRules.Open(flow.Client.View, watch, PeekSpot.Encounter);
                if (c != null) return c.Id == Built;
                var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
                if (seq != null)   // 고른 단계(21행 「고른 뒤」)가 아직 남았다 — 그 카드가 커지는 것까지 보인다
                    foreach (var entry in seq.Queued)
                    {
                        var step = entry.Step(EnterRows.Picked);
                        if (step == null || (ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.EndMs)) continue;
                        foreach (var ev in step.Events)
                            if (ev is EncounterPicked p && p.Seat == watch) return true;
                    }
                return !EnterRows.Reached(seq, EnterRows.Wipe, EnterBeats.WipeSwapAt);
            }
        }

        /// <summary>보기 창에서 그 좌석이 지금 가리키는 카드(왼쪽에서부터 0, 없으면 −1 — 방 판의 「남의 고르기 보기」) — 확인용.</summary>
        public int Pointed { get; private set; } = -1;

        public string Name => "encounter";

        /// <summary>이 창이 그린 조우 고르기(선택 Id). 그 뒤 다른 조우 고르기가 열렸으면(ace_trainer B 다시 찾아보기 — 새 카드) <see cref="Stale"/>다.</summary>
        public int Built { get; private set; } = -1;

        /// <summary>그린 뒤 다른 조우 고르기가 열렸다 — GameFlow가 새 카드 공개(21행) 때 창을 다시 연다.</summary>
        public bool Stale
        {
            get
            {
                var c = watch < 0 && flow.Client != null ? flow.Client.Choice(ChoiceKind.EncounterPick) : null;
                return c != null && Built >= 0 && c.Id != Built;
            }
        }

        /// <summary>[다시 찾아보기](ace_trainer B — 합법 답에 UseAbility가 있을 때)가 섰나.</summary>
        public bool RetryOn { get; private set; }

        /// <summary>창에 놓인 카드의 답(엔진의 합법 답 그대로, 왼쪽 → 오른쪽 — Target = 카드, Arg = 줄 안 자리).</summary>
        public IReadOnlyList<ChoiceAnswer> Cards => cards;

        /// <summary>내 입력을 받아도 되나: 화면 대기열이 비어 클릭 막기 시간이 지났다(창 자신의 입력은 대기열로 본다 — <see cref="GameFlow.InputOpen"/>은 창이 열려 있으면 늘 false다).</summary>
        public bool Open => watch < 0 && flow.Client != null && flow.Client.Choice(ChoiceKind.EncounterPick) != null && (flow.Fx == null || flow.Fx.Sequencer.InputOpen);

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            cards.Clear();
            buttons.Clear();
            cardElements.Clear();
            backs.Clear();
            StopLifts();
            heavy.Clear();
            Picked = -1;
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var client = flow.Client;
            var view = client.View;
            var choice = watch >= 0 ? PeekRules.Open(view, watch, PeekSpot.Encounter) : client.Choice(ChoiceKind.EncounterPick);
            int seat = watch >= 0 ? watch : client.Seat;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 위 띠·아래 판(HUD)은 덮지 않는다
            if (choice == null) return page;
            Built = choice.Id;
            if (watch >= 0)   // 보기 창: 공개 후보(줄 안 자리 순 — 고른 카드의 Position과 같은 자리)
                for (int i = 0; i < choice.Options.Count; i++) cards.Add(new ChoiceAnswer(AnswerKind.Pick, choice.Options[i], i));
            else cards.AddRange(client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Pick));   // 조우 고르기 동안의 제작 답(룰북 p.2)은 카드가 아니다 — 제작 화면 묶음이 받는다

            if (watch < 0) Add(page, new VisualElement(), "encounter-scrim", "sn-enc-scrim");   // 가운데 필드를 덮어 보드를 누르지 못하게 한다(보기 창은 덮지 않는다)
            var panel = Add(page, Kit.Panel(stage.Skin), "encounter-panel", "sn-enc-panel");

            // 머리: 묻는 말 + 칸(지형 · 기후) / 공통 값 + 안내
            var head = Add(panel, new VisualElement(), null, "sn-enc-head");
            var ask = Add(head, Kit.Text(watch >= 0 ? KoJosa.Fix(stage.T("screen.encounter.ask.seat").Replace("{name}", flow.SeatName(watch))) : stage.T("screen.encounter.ask"), Kit.FTitle), "encounter-ask", "kit-title");
            if (watch >= 0) FaceBox.Owner(flow, watch, ask);
            int cell = choice.Subject;
            if (cell >= 0 && cell < ctx.Board.Count)
            {
                var c = ctx.Board[cell];
                string place = stage.T("screen.encounter.biome." + c.Biome.ToString().ToLowerInvariant()) + " · " + stage.T("screen.encounter.climate." + c.Climate.ToString().ToLowerInvariant());
                Add(head, Kit.Text(place, Kit.F2), "encounter-place", "kit-muted");
            }
            var common = Add(panel, new VisualElement(), null, "sn-enc-head");
            var chips = Add(common, new VisualElement(), "encounter-common", "sn-enc-line");
            bool sameGrade = cards.Count > 0;
            var grade = cards.Count > 0 ? MapActions.GradeOf(ctx, cards[0].Target) : EncounterTier.Weak;
            foreach (var a in cards) if (MapActions.GradeOf(ctx, a.Target) != grade) sameGrade = false;
            if (sameGrade)
            {
                int rate = BattleRules.CatchRate(grade, false), jp = BattleRules.RewardJp(grade, false, false), jpDefeat = BattleRules.RewardJp(grade, false, true);
                var rateChip = Add(chips, Kit.Badge(stage.T("screen.encounter.rate").Replace("{n}", rate.ToString(inv)), KitBadgeKind.Silver, Kit.F2), "encounter-rate", null);
                stage.Tips.Hook(rateChip, null, stage.T("screen.encounter.rate.tip").Replace("{n}", rate.ToString(inv)), TipSide.Below, reading: true);
                var jpChip = Add(chips, Kit.Badge(stage.T("screen.encounter.jp").Replace("{n}", jp.ToString(inv)), KitBadgeKind.Silver, Kit.F2), "encounter-jp", null);
                stage.Tips.Hook(jpChip, null, stage.T("screen.encounter.jp.tip").Replace("{n}", jp.ToString(inv)).Replace("{m}", jpDefeat.ToString(inv)), TipSide.Below, reading: true);
            }
            int token = MapActions.EncounterTokenAt(view, cell);
            if (token >= 0 && view.Tokens[token].Def >= 0)
            {
                var apricorns = ctx.Fixtures.Fixtures[view.Tokens[token].Def].Apricorns;
                if (apricorns.Count > 0)
                {
                    var reward = Add(chips, new VisualElement(), "encounter-reward", "sn-enc-chip");
                    reward.Add(Kit.Text(stage.T("screen.encounter.reward"), Kit.F2));
                    foreach (var color in apricorns) Icon(reward, ApricornKey(color), "sn-icon");
                    stage.Tips.Hook(reward, null, stage.T("screen.encounter.reward.tip"), TipSide.Below, reading: true);
                }
            }
            Add(common, Kit.Text(stage.T("screen.encounter.must"), Kit.F2), "encounter-must", "kit-muted");

            // 카드
            var row = Add(panel, new VisualElement(), "encounter-cards", "sn-enc-cards");
            var mons = MonArt.Load();
            var heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            var speed = GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey);
            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                var def = ctx.Cards.Pokemon[cards[i].Target];
                var cardGrade = MapActions.GradeOf(ctx, cards[i].Target);
                string n = (i + 1).ToString(inv);
                // 자리 칸(옮기지 않는다)이 마우스 오버를 받는다 — 카드가 들려 포인터 밑을 벗어나도 들림이 깜빡이지 않게
                var slot = Add(row, new VisualElement(), "encounter-slot-" + n, "sn-enc-slot");
                slot.RegisterCallback<PointerEnterEvent>(_ => { Hover(index, true); Hover(index); });   // 들림(다듬기 2) + 남의 고르기 보기(P09 — 방 판이 아니면 아무 일도 없다)
                slot.RegisterCallback<PointerLeaveEvent>(_ => { Hover(index, false); Hover(-1); });
                var card = Add(slot, new VisualElement(), "encounter-card-" + n, "sn-enc-card");
                card.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100));   // 늘이기 기준 = 아래 가운데(닿을 때 바닥에서 눌린다)
                var top = Add(card, new VisualElement(), null, "sn-enc-top");
                Icon(top, "Cards/Generator/encounter_icons/" + cardGrade.ToString().ToLowerInvariant() + ".png", "sn-enc-grade");
                top.Add(Kit.Badge(stage.T("screen.encounter.grade." + cardGrade.ToString().ToLowerInvariant()), KitBadgeKind.Ok, Kit.F2));
                // 카드마다 포획률(결정 105 — 등급 배지 옆: 등급이 섞인 왜곡된 나무에서도 카드별로 보인다. 위 줄의 공통 칩은 등급이 같을 때만)
                int cardRate = BattleRules.CatchRate(cardGrade, false);
                var cardRateChip = Add(top, Kit.Badge(stage.T("screen.encounter.rate").Replace("{n}", cardRate.ToString(inv)), KitBadgeKind.Silver, Kit.F2), "encounter-card-" + n + "-rate", null);
                stage.Tips.Hook(cardRateChip, null, stage.T("screen.encounter.rate.tip").Replace("{n}", cardRate.ToString(inv)), TipSide.Below, reading: true);
                Add(card, Kit.Text(stage.T("pokemon." + def.Key), Kit.F2), "encounter-card-" + n + "-name", null);
                card.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var types = Add(card, new VisualElement(), null, "sn-enc-line");
                foreach (var t in def.Types) Icon(types, IconArt.Type(t), "sn-type-icon");
                var stats = Add(card, new VisualElement(), null, "sn-enc-line");
                var hp = Kit.Pill(heart, def.Health.ToString(inv), KitTone.Base, def.Health.ToString(inv));
                var sp = Kit.Pill(speed, def.Initiative.ToString(inv));
                stats.Add(hp);
                stats.Add(sp);
                stage.Tips.Hook(hp, stage.T("screen.common.health"), null, TipSide.Below);
                stage.Tips.Hook(sp, stage.T("screen.common.speed"), null, TipSide.Below);

                // ★ 고유 기술 줄: 타입 · 이름 · 위력(효과문은 줄 아래 툴팁)
                var move = ctx.Cards.Moves[def.Signature];
                string moveKey = BattleFeed.MoveNameKey(ctx, def.Signature);
                var moveRow = Add(card, new VisualElement(), "encounter-card-" + n + "-move", "sn-enc-move");
                Icon(moveRow, IconArt.Type(move.Type), "sn-type-icon");
                Add(moveRow, Kit.Text("★" + stage.T(moveKey), Kit.F2), null, "sn-enc-move-name");
                Add(moveRow, Kit.Text(stage.T("screen.battle.power"), Kit.F2), null, "kit-muted");
                moveRow.Add(Kit.Text(move.AttackStrength.HasValue ? move.AttackStrength.Value.ToString(inv) : "?", Kit.F2));
                stage.Tips.Hook(moveRow, stage.T(moveKey), stage.TOrNull(moveKey + ".text"), TipSide.Below, reading: true);

                var learn = Add(card, new VisualElement(), "encounter-card-" + n + "-learn", "sn-enc-line");
                Add(learn, Kit.Text(stage.T("screen.encounter.learn"), Kit.F1), null, "kit-muted");
                foreach (var t in def.LearnTypes) Icon(learn, IconArt.Type(t), "sn-type-icon");

                if (watch < 0)   // 보기 창에는 [배틀!]이 없다(보기 전용)
                {
                    var button = Add(card, Kit.Button(stage.T("screen.encounter.battle"), KitButtonKind.Main, () => Pick(index)), "encounter-pick-" + n, "sn-enc-pick");
                    buttons.Add(button);
                }

                // 뒷면(뒤집히기 전): 카드를 덮는 판 + 등급 그림
                var back = Add(card, new VisualElement { pickingMode = PickingMode.Ignore }, "encounter-card-" + n + "-back", "sn-enc-back");
                Icon(back, "Cards/Generator/encounter_icons/" + cardGrade.ToString().ToLowerInvariant() + ".png", "sn-enc-back-icon");
                cardElements.Add(card);
                backs.Add(back);
                heavy.Add(EncounterCards.Heavy(cardGrade));
                lifts.Add(0f);
                liftTweens.Add(-1);
            }

            // 아래 줄: 배틀 전 준비 — 볼 재고(가진 것만이 아니라 네 등급 모두: 0이면 던질 볼이 없다는 것이 보인다)
            var prep = Add(panel, new VisualElement(), "encounter-prep", "sn-enc-prep");
            Add(prep, Kit.Text(stage.T("screen.encounter.prep"), Kit.F2), null, "kit-title");
            var balls = view.Seats[seat].Balls;
            for (int b = 0; b < balls.Length; b++)
            {
                string item = BattleFeed.BallItem(b);
                var chip = Add(prep, new VisualElement(), "encounter-ball-" + item, "sn-enc-chip");
                Icon(chip, GenAssets.ItemIcons + item + ".png", "sn-icon");
                chip.Add(Kit.Text(balls[b].ToString(inv), Kit.F2));
                stage.Tips.Hook(chip, stage.T(BattleFeed.BallKey(b)), null, TipSide.Above);
            }
            if (watch >= 0)
            {
                Add(prep, Kit.Text(KoJosa.Fix(stage.T("screen.encounter.watch").Replace("{name}", flow.SeatName(watch))), Kit.F2), "encounter-watch", "kit-muted");
                Refresh();
                return page;
            }
            // [제작](21행 — 고르기 전, 룰북 p.2): 조우 고르기의 제작 답이 있을 때만
            if (client.Legal(choice).Exists(a => a.Kind == AnswerKind.Craft))
            {
                var craft = Add(prep, Kit.Button(stage.T("screen.board.action.craft"), KitButtonKind.Normal, () => flow.OpenCraft()), "encounter-craft", "sn-enc-craft");
                buttons.Add(craft);
                stage.Tips.Hook(craft, null, stage.T("screen.encounter.craft.tip"), TipSide.Above, reading: true);
            }
            // [다시 찾아보기](ace_trainer B — 조우당 한 번, rulings A39): 나온 카드를 모두 덱으로 돌려보내고 다시 찾아본다. 마우스 오버 = 카드 — 능력 + 문구
            RetryOn = client.Legal(choice).Exists(a => a.Kind == AnswerKind.UseAbility);
            if (RetryOn)
            {
                int card = CardAsk.CardOf(view, client.Seat);
                var retry = Add(prep, Kit.Button(stage.T("screen.encounter.retry"), KitButtonKind.Normal, () => Retry()), "encounter-retry", "sn-enc-craft");
                buttons.Add(retry);
                stage.Tips.Hook(retry, CardAsk.Head(stage, ctx, card, 1), CardAsk.Text(stage, ctx, card, 1), TipSide.Above, reading: true);
            }
            Refresh();
            return page;
        }

        public void Close()
        {
            StopLifts();
            stage = null;
            buttons.Clear();
            cardElements.Clear();
            backs.Clear();
        }

        /// <summary>규토리 색 → 그림 키.</summary>
        public static string ApricornKey(ApricornColor color) => GenAssets.ItemIcons + "apricorn_" + color.ToString().ToLowerInvariant() + ".png";

        /// <summary>입력이 열리고 닫히는 때 단추를 켜고 끈다.</summary>
        public void Refresh()
        {
            if (stage == null) return;
            bool open = Open;
            paintedOpen = open;
            foreach (var b in buttons) b.SetEnabled(open);
            Animate();
        }

        /// <summary>화면 대기열이 프레임마다 부른다(연출이 도는 동안).</summary>
        public void ShowFx(FxSequencer seq)
        {
            if (stage == null) return;
            if (Open != paintedOpen) Refresh();
            else Animate();
        }

        // 카드의 그 순간 모습(21행): 공개 단계가 재생 중이면 그 안 시각으로 뒤집히고(강한 · 전설은 늦게 떠올라 쿵), 아니면(이미 지났다 · 대기열 없음) 모두 앞면.
        // 고른 뒤에는 고른 카드가 커지고 나머지는 뒷면으로 덱(아래)에 쓸려 간다 — 「고른 뒤」 단계(600)가 지나도 전환 줄무늬가 닫힐 때까지 그 모습으로 남는다.
        // 고른 답의 갱신이 대기열에 들어왔지만 그 단계가 아직이면 고르기 전 모습(한 프레임 먼저 사라졌다 다시 나타나지 않게)
        void Animate()
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var cur = seq != null ? seq.Current : null;
            var reveal = cur != null ? cur.Step(EnterRows.Reveal) : null;
            var chosen = cur != null ? cur.Step(EnterRows.Picked) : null;
            int t = reveal != null ? seq.LocalMs - reveal.StartMs : int.MaxValue;
            int pickT = Picked < 0 ? -1 : chosen != null ? seq.LocalMs - chosen.StartMs : EnterRows.Reached(seq, EnterRows.Picked) ? int.MaxValue : 0;
            float pop = Picked < 0 ? 0f : Tween.Apply(Ease.OutQuad, Tween.Progress(pickT, 0, EnterBeats.PickPopMs));
            float unit = stage != null && stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            if (watch >= 0) WatchMarks(seq);
            FaceUp = 0;
            for (int i = 0; i < cardElements.Count; i++)
            {
                var shown = EncounterCards.Reveal(heavy, i, t);
                var swept = EncounterCards.Sweep(Picked, i, pickT);
                if (shown.Face) FaceUp++;
                backs[i].style.display = shown.Face && swept.Face ? DisplayStyle.None : DisplayStyle.Flex;
                float grow = i == Picked ? 1f + (PickScale - 1f) * pop : 1f;
                FxUi.Scale(cardElements[i], shown.ScaleX * swept.ScaleX * grow, shown.ScaleY * swept.ScaleY * grow);
                // 늘이기 기준이 아래 가운데라 고른 카드가 위로만 자란다 — 그 절반만큼 내려 가운데에서 자란 것처럼(머리 줄을 덮지 않게)
                float h = cardElements[i].layout.height, settle = grow > 1f && !float.IsNaN(h) ? (grow - 1f) * h * 0.5f : 0f;
                FxUi.Move(cardElements[i], 0f, (swept.Away * AwayPx - shown.Rise * RisePx - LiftOf(i) * LiftPx) * unit + settle);
                FxUi.Fade(cardElements[i], swept.Opacity);
                // 덱으로 쓸려 가는 뒷면은 흐려지는 동안 앞면이 비쳐 보이지 않게 앞면 칸을 숨긴다(visibility — 배치는 그대로)
                cardElements[i].EnableInClassList("sn-enc-card-down", swept.Away > 0f);
                cardElements[i].EnableInClassList("sn-enc-card-picked", i == Picked);
            }
        }

        // 보기 창: 그 좌석이 고른 카드(고른 단계가 대기열에 들어오면 — 내 창처럼 그 카드가 커진다) · 가리키는 카드의 좌석 색 테두리(방 판)
        void WatchMarks(FxSequencer seq)
        {
            if (Picked < 0 && seq != null)
                foreach (var entry in seq.Queued)
                {
                    var step = entry.Step(EnterRows.Picked);
                    if (step == null) continue;
                    foreach (var ev in step.Events)
                        if (ev is EncounterPicked p && p.Seat == watch && p.Position >= 0 && p.Position < cardElements.Count) Picked = p.Position;
                }
            var mark = Picked < 0 ? flow.PeekOf(watch, PeekSpot.Encounter) : null;
            Pointed = mark != null && mark.Index < cardElements.Count ? mark.Index : -1;
            var tone = new StyleColor((Color)flow.SeatColor(watch));
            for (int i = 0; i < cardElements.Count; i++)
            {
                var c = cardElements[i];
                bool on = i == Pointed;
                c.EnableInClassList("sn-enc-card-peek", on);
                var color = on ? tone : new StyleColor(StyleKeyword.Null);
                c.style.borderLeftColor = c.style.borderRightColor = c.style.borderTopColor = c.style.borderBottomColor = color;
            }
        }

        /// <summary>
        /// 마우스 오버(M9 다듬기 2 — 내 화면만, 대기열 밖): 고를 수 있는 동안 올린 카드가 살짝 들리고 벗어나면 내려앉는다. 길이는 연출 속도를 따르고(<see cref="EncounterCards.HoverMs"/>)
        /// 「끄기」면 들리지 않는다. 자리 칸의 포인터 들어옴 · 나감이 부르고, 확인 · 캡처는 직접 부른다. 무음(마우스 이동 — 04 §8-4).
        /// </summary>
        public void Hover(int index, bool on)
        {
            if (stage == null || index < 0 || index >= lifts.Count) return;
            if (on && (!Open || Picked >= 0)) return;
            float from = lifts[index], to = on ? 1f : 0f;
            int length = EncounterCards.HoverMs(flow.FxSpeed);
            if (liftTweens[index] >= 0) stage.Tweens.Stop(liftTweens[index]);
            liftTweens[index] = -1;
            if (length <= 0 || from == to)
            {
                lifts[index] = length <= 0 ? 0f : to;
                Animate();
                return;
            }
            liftTweens[index] = stage.Tweens.Play(new LiftPart(this, index, from, to, length), length);
        }

        sealed class LiftPart : ISeekable
        {
            readonly EncounterScreen screen;
            readonly int index, length;
            readonly float from, to;

            public LiftPart(EncounterScreen screen, int index, float from, float to, int length)
            {
                this.screen = screen;
                this.index = index;
                this.from = from;
                this.to = to;
                this.length = length;
            }

            public void Seek(int ms)
            {
                if (screen.stage == null || index >= screen.lifts.Count) return;
                screen.lifts[index] = EncounterCards.Lift(from, to, ms, length);
                screen.Animate();
            }
        }

        void StopLifts()
        {
            if (stage != null)
                foreach (int id in liftTweens)
                    if (id >= 0) stage.Tweens.Stop(id);
            liftTweens.Clear();
            lifts.Clear();
        }

        /// <summary>[배틀!]: 그 카드(왼쪽에서부터 0)의 답을 낸다. 입력이 닫혀 있거나 없는 카드면 false.</summary>
        public bool Pick(int index)
        {
            if (stage == null || !Open || index < 0 || index >= cards.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.EncounterPick);
            if (choice == null) return false;
            stage.Tips.Hide();
            Picked = index;   // 답이 받아들여지면 갱신이 바로 온다(이 화면은 전환이 닫힐 때까지 남는다) — 거절되면 되돌린다
            int slot = choice.Options.IndexOf(cards[index].Target);
            bool ok = flow.Answer(choice, cards[index]).Accepted;
            if (!ok) Picked = -1;
            else flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Encounter, slot, true);
            if (stage != null) Animate();
            return ok;
        }

        /// <summary>
        /// 그 카드(왼쪽에서부터 0, −1 = 떠남)에 마우스를 올렸다 — 방 판이면 다른 좌석의 화면에 내 좌석 색으로 보인다(M9 다듬기 P09: 자리 = 그 선택의 공개 후보 안 자리).
        /// 입력이 닫혀 있으면(뒤집히는 중 · 고른 뒤) 보내지 않는다. 보냈으면(쥐었으면) true.
        /// </summary>
        public bool Hover(int index)
        {
            if (stage == null || !Open || index >= cards.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.EncounterPick);
            if (choice == null) return false;
            return flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Encounter, index < 0 ? -1 : choice.Options.IndexOf(cards[index].Target));
        }

        /// <summary>[다시 찾아보기]: UseAbility 답을 낸다(나온 카드는 덱으로 — 새 카드가 나오면 GameFlow가 창을 다시 연다). 입력이 닫혀 있거나 답이 없으면 false.</summary>
        public bool Retry()
        {
            if (stage == null || !Open || !RetryOn) return false;
            var choice = flow.Client.Choice(ChoiceKind.EncounterPick);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.UseAbility) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        static void Icon(VisualElement parent, string key, string cls)
        {
            var icon = Add(parent, new VisualElement(), null, cls);
            var tex = GenAssets.Load<Texture2D>(key);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
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
