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
    /// 공개 정보 카드(04 §3-3 「남의 공개 정보 — 마우스 오버」, 목업 r3 board-hover, 결정 16): 위 띠의 좌석 칩이나 보드 위의 말·토큰에 마우스를 올리면 뜨는 판이다.
    /// <list type="bullet">
    /// <item>좌석: 얼굴 상자 · 이름 · 고향 마을 · 트레이너 카드 · 막 진행 / 파티(포켓몬마다 도트 · 이름 · 타입 · ♥ · 스피드 · 상태 · ★ 고유 기술과 배운 기술 2칸 · 지닌물건) /
    /// 규토리 7색 · 볼 넷 · 여행 점수 · 스탬프 · 운명의 변화 장수 · 도전 보호 · (턴인 좌석이면) 남은 이동력. 모두 보기(<c>ViewFor</c>)에 있는 값 그대로 —
    /// 남의 운명의 변화는 장수만 보인다(보기가 내용을 가려서 준다).</item>
    /// <item>토큰: 숨겨진 장소(뒷면은 정체 없이 한 줄 · 공개됐으면 이름) · 갤럭시단 조무래기(공중날기가 막힌 까닭).</item>
    /// <item>피규어(울트라 버스트 판 — 결정 179): 그 칸에 보이는 UB · 네크로즈마의 이름(<see cref="GameFlow.BeastsAt"/>) · 서 있는 호수 수호신의 시련(<see cref="GameFlow.GuardianAt"/> — 넘긴 뒤에는 「시련 통과」).</item>
    /// </list>
    /// 누르면 여는 상세(S38·S39)는 아직 없다. 마우스를 받지 않는다(요소마다 <c>PickingMode.Ignore</c>) — 카드가 보드의 칸 고르기·휠을 막지 않는다.
    /// 화면들 위(툴팁 층 바로 아래)에 얹는다 — 배틀 화면이 떠 있어도 위 띠의 좌석 칩에서 뜬다(04 §4-6). 자리는 키트 툴팁과 같은 셈(<see cref="TipPlace"/>)이다.
    /// 요소 이름: <c>info-card</c>·<c>info-card-seat-&lt;n&gt;</c>·<c>info-card-mon-&lt;i&gt;</c>·<c>info-card-token-&lt;hidden|revealed|grunt&gt;</c>·<c>info-card-beast</c>·<c>info-card-trial</c>(<c>-badge</c>·<c>-goal</c>·<c>-immune</c>·<c>-reward</c>).
    /// </summary>
    public sealed class InfoCard
    {
        /// <summary>대상과 카드 사이 틈(패널 px).</summary>
        public const float Gap = 6f;

        static readonly string[] StatusNames = { null, "confused", "paralysed", "poisoned", "burned", "frozen", "drowsy" };

        readonly UiStage stage;
        readonly GameFlow flow;
        readonly VisualElement card;
        Rect target;
        TipSide side;
        int version = -1, look, figures;
        readonly List<string> beasts = new List<string>();

        public VisualElement Element => card;

        /// <summary>지금 떠 있는 대상(「seat-1」·「cell-123」). 없으면 null.</summary>
        public string Subject { get; private set; }

        public InfoCard(UiStage stage, GameFlow flow)
        {
            this.stage = stage;
            this.flow = flow;
            card = Kit.Panel(stage.Skin);
            card.name = "info-card";
            card.AddToClassList("sn-info");
            card.pickingMode = PickingMode.Ignore;
            card.style.display = DisplayStyle.None;
            card.RegisterCallback<GeometryChangedEvent>(e => Place());
        }

        /// <summary>
        /// 그 칸에 서 있는 공개 대상: 보는 좌석이 아닌 좌석의 말(내 말은 뺀다 — 04 §4-6 「보는 사람 자신의 칩만 뺀다」 [가정: 판 위 말에도 같은 규칙]) · 숨겨진 장소 · 조무래기 토큰.
        /// 한 칸에 말이 둘 이상이면(묶음 — 결정 66 ①) 내 좌석도 넣는다 [가정]: 표지의 사람 수(「×3」)와 카드의 좌석 수가 같아야 누가 있는지 읽힌다. 좌석 번호 순.
        /// </summary>
        /// <param name="shown">토큰이 지금 보드 위에 보이나(<c>GameFlow.TokenShown</c> — 아직 놓이지 않은 조무래기를 뺀다). null = 보기 그대로.</param>
        public static void Subjects(GameState view, int viewer, int cell, List<int> seats, List<TokenState> tokens, System.Func<TokenState, bool> shown = null)
        {
            seats.Clear();
            tokens.Clear();
            if (view == null || cell < 0) return;
            for (int s = 0; s < view.Seats.Length; s++)
                if (view.Seats[s].Cell == cell && !view.Seats[s].Eliminated) seats.Add(s);
            if (seats.Count < 2) seats.Remove(viewer);
            foreach (var t in view.Tokens)
                if (t.Cell == cell && !t.Destroyed && (t.Kind == TokenKind.HiddenSpot || t.Kind == TokenKind.Grunt) && (shown == null || shown(t))) tokens.Add(t);
        }

        /// <summary>좌석의 공개 정보 카드를 대상(뿌리 좌표의 사각형) 옆에 띄운다.</summary>
        public void ShowSeat(int seat, Rect target, TipSide side = TipSide.Below)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || seat < 0 || seat >= view.Seats.Length) { Hide(); return; }
            string subject = "seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture);
            if (subject == Subject && version == flow.Client.Version) { Aim(target, side); return; }
            card.Clear();
            card.RemoveFromClassList("sn-info-slim");
            Seat(view, seat);
            Open(subject, target, side);
        }

        /// <summary>그 칸의 말·토큰 정보를 대상 옆에 띄운다. 띄울 것이 없으면 걷고 false.</summary>
        public bool ShowCell(int cell, Rect target, TipSide side = TipSide.Right)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            var seats = new List<int>();
            var tokens = new List<TokenState>();
            if (view != null) Subjects(view, flow.Client.Seat, cell, seats, tokens, flow.TokenShown);
            flow.BeastsAt(cell, beasts);   // 결정 179: 그 칸에 보이는 UB · 네크로즈마 · 시련이 남은 호수 수호신
            int guardian = flow.GuardianAt(cell);
            if (seats.Count == 0 && tokens.Count == 0 && beasts.Count == 0 && guardian < 0) { Hide(); return false; }
            string subject = "cell-" + cell.ToString(CultureInfo.InvariantCulture);
            if (subject == Subject && version == flow.Client.Version && look == flow.TokenLook && figures == flow.FigureLook()) { Aim(target, side); return true; }
            card.Clear();
            // 묶음(좌석 둘 이상): 머리 줄 + 좌석마다 줄인 칸(머리 · 파티 한 줄) — 여섯이어도 화면 안에 든다. 자세한 것은 좌석 칩의 카드
            bool stack = seats.Count >= 2;
            if (stack) Add(card, Kit.Text(stage.T("screen.info.stack").Replace("{n}", seats.Count.ToString(CultureInfo.InvariantCulture)), Kit.F2), "info-card-stack", "kit-title");
            foreach (int s in seats) Seat(view, s, stack);
            foreach (var t in tokens) Token(t);
            foreach (string key in beasts) Beast(key);
            if (guardian >= 0) Guardian(view, guardian);
            card.EnableInClassList("sn-info-slim", seats.Count == 0);   // 토큰 · 피규어뿐이면 좁은 카드
            Open(subject, target, side);
            return true;
        }

        public void Hide()
        {
            if (Subject == null) return;
            Subject = null;
            card.style.display = DisplayStyle.None;
        }

        /// <summary>뿌리에서 뗀다(HUD가 닫힐 때).</summary>
        public void Detach()
        {
            Hide();
            card.RemoveFromHierarchy();
        }

        void Open(string subject, Rect at, TipSide where)
        {
            Subject = subject;
            version = flow.Client.Version;
            look = flow.TokenLook;
            figures = flow.FigureLook();
            target = at;
            side = where;
            Unpick(card);
            var root = stage.Root;
            if (root == null) return;
            if (card.parent != root) root.Add(card);
            card.BringToFront();
            stage.Tips.Element.BringToFront();   // 툴팁은 카드 위
            card.style.visibility = Visibility.Hidden;   // 크기가 정해지면(GeometryChanged) 자리를 잡아 보인다
            card.style.display = DisplayStyle.Flex;
            Place();
        }

        void Aim(Rect at, TipSide where)
        {
            if (at == target && where == side) return;
            target = at;
            side = where;
            Place();
        }

        void Place()
        {
            var root = stage.Root;
            if (Subject == null || root == null || card.panel == null) return;
            var size = card.layout;
            if (float.IsNaN(size.width) || float.IsNaN(size.height) || size.width <= 0f) return;
            var spot = TipPlace.Place(target.x, target.y, target.width, target.height, size.width, size.height, root.layout.width, root.layout.height, side, Gap);
            card.style.left = Mathf.Round(spot.X);
            card.style.top = Mathf.Round(spot.Y);
            card.style.visibility = Visibility.Visible;
        }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        // ── 좌석 ──

        void Seat(GameState view, int s, bool brief = false)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var seat = view.Seats[s];
            var block = Add(card, new VisualElement(), "info-card-seat-" + (s + 1).ToString(inv), "sn-info-block");
            if (brief) block.AddToClassList("sn-info-brief");

            // 머리: 얼굴 · 이름 · 고향 마을 · 트레이너 카드 · 막
            var head = Add(block, new VisualElement(), null, "sn-info-head");
            var face = FaceBox.Make();
            FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
            head.Add(face);
            var who = Add(head, new VisualElement(), null, "sn-info-who");
            Add(who, Kit.Text(flow.SeatName(s), Kit.F2), null, "kit-title");
            var sub = Add(who, new VisualElement(), null, "sn-info-line");
            if (seat.HomeVillage >= 0) Add(sub, Kit.Text(stage.T("screen.info.home").Replace("{village}", stage.T(ctx.Fixtures.Fixtures[seat.HomeVillage].Key)), Kit.F1), null, "kit-muted");
            if (seat.TrainerCard >= 0) sub.Add(Kit.Badge(stage.T("trainer_card." + ctx.Cards.TrainerCards[seat.TrainerCard].Key), KitBadgeKind.Silver, Kit.F1));
            var marks = Add(head, new VisualElement(), null, "sn-info-marks");
            if (view.Hos != null && view.Hos.Act >= 1)
            {
                // 막 진행: 공개된 막의 목표를 이 좌석이 달성했나(좌석마다 다르다 — 처음 달성한 좌석만 다음 막으로 넘긴다)
                int act = Mathf.Min(view.Hos.Act, seat.ActProgress + 1);
                bool done = seat.ActProgress >= view.Hos.Act;
                string name = stage.T(BoardLog.ActKey(done ? view.Hos.Act : act));
                marks.Add(Kit.Badge(stage.T(done ? "screen.info.act.done" : "screen.info.act.doing").Replace("{act}", name), done ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F1));
            }
            if (seat.Protected) marks.Add(Kit.Badge(stage.T("screen.info.protected"), KitBadgeKind.Warn, Kit.F1));
            if (view.ActiveSeat == s && view.Phase != Phase.Setup) marks.Add(Kit.Badge(stage.T("screen.info.moves").Replace("{n}", seat.MovesLeft.ToString(inv)), KitBadgeKind.Gold, Kit.F1));

            if (brief)
            {
                // 줄인 칸: 파티를 한 줄로(이름 · ♥)
                var line = Add(block, new VisualElement(), null, "sn-info-line");
                var heartIcon = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
                for (int i = 0; i < seat.Party.Length; i++)
                {
                    int id = seat.Party[i];
                    if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                    var pk = view.Pokemon[id];
                    int max = PokemonRules.MaxHp(ctx, pk);
                    int hp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(id), PokemonRules.Hp(ctx, pk)) : PokemonRules.Hp(ctx, pk);
                    line.Add(Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[pk.Def].Key), Kit.F2));
                    line.Add(Kit.Pill(heartIcon, hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
                }
                return;
            }

            // 파티
            Add(block, Kit.Text(stage.T("screen.info.party").Replace("{n}", PokemonRules.PartyCount(seat).ToString(inv)).Replace("{max}", seat.OpenSlots.ToString(inv)), Kit.F2), null, "kit-title");
            var mons = MonArt.Load();
            var heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            var speed = GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey);
            for (int i = 0; i < seat.Party.Length; i++)
            {
                int id = seat.Party[i];
                if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                var pk = view.Pokemon[id];
                var def = ctx.Cards.Pokemon[pk.Def];
                var row = Add(block, new VisualElement(), "info-card-mon-" + i.ToString(inv), "sn-info-mon");
                row.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var info = Add(row, new VisualElement(), null, "sn-info-mon-info");
                var top = Add(info, new VisualElement(), null, "sn-info-line");
                top.Add(Kit.Text(stage.T("pokemon." + def.Key), Kit.F2));
                foreach (var t in def.Types) Icon(top, IconArt.Type(t), "sn-type-icon");
                int max = PokemonRules.MaxHp(ctx, pk);
                int hp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(id), PokemonRules.Hp(ctx, pk)) : PokemonRules.Hp(ctx, pk);
                var stats = Add(top, new VisualElement(), null, "sn-info-stats");
                stats.Add(Kit.Pill(heart, hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
                stats.Add(Kit.Pill(speed, def.Initiative.ToString(inv)));
                if (hp <= 0) top.Add(Kit.Badge(stage.T("screen.info.fainted"), KitBadgeKind.Red, Kit.F1));
                else if (pk.Status != StatusEffect.None) top.Add(Kit.Badge(stage.T("screen.status." + StatusNames[(int)pk.Status]), KitBadgeKind.Warn, Kit.F1));

                // 기술 줄: ★ 고유 기술 + 배운 칸 2(빈 칸은 점선), 지닌물건
                var moves = Add(info, new VisualElement(), null, "sn-info-line");
                Add(moves, Kit.Text("★" + stage.T(BattleFeed.MoveNameKey(ctx, def.Signature)), Kit.F2), null, "sn-info-move");
                for (int k = 0; k < pk.Moves.Length; k++)
                {
                    int m = pk.Moves[k];
                    var tactic = m < 0 ? LegendRules.TacticAt(pk, k) : LegendTactic.None;   // 전설 특수기술이 차지한 빈 칸(결정 195)
                    if (m >= 0) Add(moves, Kit.Text(stage.T(BattleFeed.MoveNameKey(ctx, m)), Kit.F2), null, "sn-info-move");
                    else if (tactic != LegendTactic.None)
                    {
                        var chip = Add(moves, Kit.Text(stage.T(MonParts.TacticKey(tactic)), Kit.F2), null, "sn-info-move");
                        chip.AddToClassList(MonParts.LegendSlotClass);
                        stage.Tips.Hook(chip, stage.T(MonParts.TacticKey(tactic)), MonParts.TacticTip(stage, tactic), TipSide.Above, reading: true);
                    }
                    else Add(moves, new VisualElement(), null, "sn-info-move-empty");
                }
                foreach (int held in new[] { pk.HeldItem, pk.HeldItem2 })   // 둘째 칸 = backpacker R(rulings A39)
                {
                    if (held < 0) continue;
                    string itemIcon = WorkUi.IconKey(ctx.Items.Items[held]);   // 지닌물건 · 유물 그림(결정 80 L14 나)
                    if (itemIcon != null) Icon(moves, itemIcon, "sn-icon");
                    Add(moves, Kit.Text(stage.T(ctx.Items.Items[held].Key), Kit.F2), null, "kit-muted");
                }
                if (pk.Ball >= 0) Icon(moves, GenAssets.ItemIcons + BattleFeed.BallItem(pk.Ball) + ".png", "sn-icon");
            }

            // 가방: 규토리 7색 · 볼 넷(한 줄씩)
            var bag = Add(block, new VisualElement(), null, "sn-info-line");
            Add(bag, Kit.Text(stage.T("screen.info.apricorns"), Kit.F2), null, "kit-title");
            for (int c = 0; c < seat.Apricorns.Length; c++)
            {
                Icon(bag, EncounterScreen.ApricornKey((ApricornColor)c), "sn-icon");
                bag.Add(Kit.Text(Res(s, ResKind.Apricorn, c, seat.Apricorns[c]).ToString(inv), Kit.F2));
            }
            var balls = Add(block, new VisualElement(), null, "sn-info-line");
            Add(balls, Kit.Text(stage.T("screen.info.balls"), Kit.F2), null, "kit-title");
            for (int b = 0; b < seat.Balls.Length; b++)
            {
                Icon(balls, GenAssets.ItemIcons + BattleFeed.BallItem(b) + ".png", "sn-icon");
                balls.Add(Kit.Text(Res(s, ResKind.Ball, b, seat.Balls[b]).ToString(inv), Kit.F2));
            }

            // 여행 점수 · 스탬프 · 운명의 변화 장수
            var foot = Add(block, new VisualElement(), null, "sn-info-line");
            foot.Add(Kit.Text(stage.T("screen.info.jp").Replace("{n}", Res(s, ResKind.Jp, -1, seat.Jp).ToString(inv)), Kit.F2));
            int stamps = 0;
            foreach (bool on in seat.Stamps) if (on) stamps++;
            Add(foot, Kit.Text(stage.T("screen.info.stamps").Replace("{n}", stamps.ToString(inv)), Kit.F2), null, "kit-muted");
            Add(foot, Kit.Text(stage.T("screen.info.fate").Replace("{n}", seat.FateHand.Count.ToString(inv)), Kit.F2), null, "kit-muted");
        }

        // 자원의 표시 값: 연출이 착지하기 전에는 붙잡아 둔 값(04 §8-3)
        int Res(int seat, ResKind res, int key, int now) => flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(seat, res, key), now) : now;

        // ── 토큰 ──

        void Token(TokenState t)
        {
            var ctx = flow.Ctx;
            bool faceUp = flow.TokenFaceUp(t);   // 뒤집히기 전에는 뒷면 그대로 적는다
            string kind = t.Kind == TokenKind.Grunt ? "grunt" : faceUp ? "revealed" : "hidden";
            var block = Add(card, new VisualElement(), "info-card-token-" + kind, "sn-info-block");
            var head = Add(block, new VisualElement(), null, "sn-info-line");
            string front = t.Kind != TokenKind.Grunt && faceUp && t.Def >= 0 && t.Def < ctx.Items.Items.Count ? GenAssets.SpotTokenKey(ctx.Items.Items[t.Def].Key) : null;   // 뒤집힌 유물·우두머리 = 토큰 앞면(결정 80 L14 나)
            Icon(head, t.Kind == TokenKind.Grunt ? BoardTokens.GruntKey : front ?? BoardTokens.SpotBackKey, "sn-icon");
            if (t.Kind == TokenKind.Grunt)
            {
                Add(head, Kit.Text(stage.T("screen.token.grunt"), Kit.F2), null, "kit-title");
                block.Add(Kit.Wrapped(stage.T("screen.token.grunt.tip"), Kit.F2));
            }
            else if (faceUp && t.Def >= 0 && t.Def < ctx.Items.Items.Count)
            {
                Add(head, Kit.Text(stage.T(ctx.Items.Items[t.Def].Key), Kit.F2), null, "kit-title");
                Add(block, Kit.Text(stage.T("screen.token.revealed"), Kit.F2), null, "kit-muted");
            }
            else
            {
                Add(head, Kit.Text(stage.T("screen.token.hidden"), Kit.F2), null, "kit-title");
                block.Add(Kit.Wrapped(stage.T("screen.token.hidden.tip"), Kit.F2));
            }
        }

        // ── 피규어(결정 179) ──

        // UB · 네크로즈마: 이름(폼 이름까지)
        void Beast(string key)
        {
            var block = Add(card, new VisualElement(), "info-card-beast", "sn-info-block");
            var head = Add(block, new VisualElement(), null, "sn-info-line");
            Add(head, Kit.Text(stage.T("pokemon." + key), Kit.F2), null, "kit-title");
        }

        // 호수 수호신: 이름 + 시련(이름 배지 · 넘기는 조건 · 면역) — 넘긴 뒤에는 「시련 통과」 · 보상 한 줄
        void Guardian(GameState view, int def)
        {
            var ctx = flow.Ctx;
            int lake = UbTrial.LakeOf(ctx, def);
            bool passed = view.Ub.TrialsOvercome[lake];
            string n = lake.ToString(CultureInfo.InvariantCulture);
            var block = Add(card, new VisualElement(), "info-card-trial", "sn-info-block");
            var head = Add(block, new VisualElement(), null, "sn-info-line");
            Add(head, Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[def].Key), Kit.F2), null, "kit-title");
            Add(head, Kit.Badge(stage.T(passed ? "screen.battle.trial.passed" : "screen.battle.trial." + n), passed ? KitBadgeKind.Ok : KitBadgeKind.Warn, Kit.F1), "info-card-trial-badge", null);
            Add(block, Kit.Wrapped(stage.T(passed ? "screen.info.trial.passed" : "screen.battle.trial.tip." + n), Kit.F2), "info-card-trial-goal", null);
            if (!passed) Add(block, Kit.Wrapped(stage.T("screen.battle.immune.tip"), Kit.F2), "info-card-trial-immune", "kit-muted");
            Add(block, Kit.Wrapped(stage.T("screen.info.trial.reward"), Kit.F2), "info-card-trial-reward", "kit-muted");
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
