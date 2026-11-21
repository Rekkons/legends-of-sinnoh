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
    /// <item>갤럭시단 상대(결정 201 ②): 조무래기 토큰 칸 · 2막 호수(간부) · 3막 신오신전(태홍)에서 만날 포켓몬 명단(<see cref="GalacticFoe"/> — 덱 구성 그대로, 남은 것 · 순서는 비밀이라 없다).
    /// 줄마다 마우스를 올리면 포켓몬 카드(<see cref="MonTip"/>).</item>
    /// </list>
    /// 누르면 여는 상세(S38·S39)는 아직 없다. 마우스를 받지 않는다(요소마다 <c>PickingMode.Ignore</c>) — 카드가 보드의 칸 고르기·휠을 막지 않는다.
    /// 예외: 명단이 실린 카드(<see cref="HasRoster"/>)는 카드 · 명단 줄이 마우스를 받는다(줄의 포켓몬 카드) — 보드 쪽으로는 없는 것처럼 지나간다(<c>GameFlow.Picked</c>가 뺀다 · 그동안 카드는 그 칸 그대로 <c>BoardHud.HoldInfo</c>).
    /// 화면들 위(툴팁 층 바로 아래)에 얹는다 — 배틀 화면이 떠 있어도 위 띠의 좌석 칩에서 뜬다(04 §4-6). 자리는 키트 툴팁과 같은 셈(<see cref="TipPlace"/>)이다.
    /// 요소 이름: <c>info-card</c>·<c>info-card-seat-&lt;n&gt;</c>·<c>info-card-mon-&lt;i&gt;</c>·<c>info-card-token-&lt;hidden|revealed|grunt&gt;</c>·<c>info-card-beast</c>·<c>info-card-trial</c>(<c>-badge</c>·<c>-goal</c>·<c>-immune</c>·<c>-reward</c>)·
    /// <c>info-card-galactic</c>(<c>-head</c>·<c>-pick</c>·<c>-mon-&lt;i&gt;</c>).
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
        readonly List<int> roster = new List<int>();
        readonly List<VisualElement> rosterRows = new List<VisualElement>();
        MonTip monTip;

        public VisualElement Element => card;

        /// <summary>지금 떠 있는 대상(「seat-1」·「cell-123」). 없으면 null.</summary>
        public string Subject { get; private set; }

        /// <summary>지금 카드에 갤럭시단 명단이 실렸나(결정 201 ② — 그동안 카드 · 명단 줄이 마우스를 받는다).</summary>
        public bool HasRoster { get; private set; }

        /// <summary>명단 줄의 마우스 오버 카드에 지금 떠 있는 포켓몬(포켓몬 표 색인 — 없으면 −1).</summary>
        public int MonShown => monTip != null ? monTip.Shown : -1;

        /// <summary>명단 줄의 마우스 오버 카드(<c>mon-tip</c> — 캡처의 단정). 아직 없으면 null.</summary>
        public VisualElement MonTipElement => monTip?.Element;

        /// <summary>
        /// 그 칸에서 만날 갤럭시단 상대(결정 201 ②). 판별은 토큰 종류 · 칸 · 막으로 한다(이름 글을 견주지 않는다):
        /// <list type="bullet">
        /// <item>조무래기 토큰이 보이는 칸(<paramref name="grunt"/>): 조무래기 덱 전체 — 무작위 <see cref="BattleFlow.GruntLineup"/>마리. leader = None.</item>
        /// <item>2막이 공개된 호수(<see cref="HosFlow.LakeShard"/>): 그 호수 간부의 카드 — (보는 좌석이 가진 조각 수 + 2)마리(<see cref="HosFlow.CommanderLineup"/>).</item>
        /// <item>3막이 공개되고 아직 아무도 태홍을 이기지 않은 신오신전(<see cref="HosFlow.TempleCell"/>): 태홍의 카드 — <see cref="HosFlow.BossLineup"/>마리.</item>
        /// </list>
        /// cards = 덱 데이터의 카드(구성 — 덱 순서). 덱에 남은 것 · 순서는 비밀이라 보지 않는다. 상대가 없으면 false.
        /// </summary>
        public static bool GalacticFoe(Ctx ctx, GameState view, int viewer, int cell, bool grunt, List<int> cards, out GalacticTrainer leader, out int count)
        {
            cards.Clear();
            leader = GalacticTrainer.None;
            count = 0;
            if (ctx == null || cell < 0) return false;
            DeckId deck;
            if (grunt)
            {
                deck = DeckId.GalacticGrunt;
                count = BattleFlow.GruntLineup;
            }
            else
            {
                if (view == null || view.Hos == null) return false;
                int shard = HosFlow.LakeShard(ctx, cell);
                if (shard >= 0 && view.Hos.Act >= 2)
                {
                    deck = DeckId.GalacticCommander;
                    leader = HosFlow.CommanderOf((Shard)shard);
                    count = HosFlow.CommanderLineup(viewer >= 0 && viewer < view.Seats.Length ? HosFlow.ShardCount(view.Seats[viewer]) : 0);
                }
                else if (cell == HosFlow.TempleCell(ctx) && view.Hos.Act >= 3 && view.Hos.FirstSeat[3] < 0)
                {
                    deck = DeckId.GalacticBoss;
                    leader = GalacticTrainer.Cyrus;
                    count = HosFlow.BossLineup;
                }
                else return false;
            }
            foreach (var e in ctx.Deck(deck).Cards)
                if (leader == GalacticTrainer.None || ctx.Cards.Pokemon[e.Card].Trainer == leader) cards.Add(e.Card);
            return cards.Count > 0;
        }

        /// <summary>간부 · 태홍의 이름(「갤럭시단 간부 마스」 — 주인 이름 키는 카드의 <c>galactic.&lt;주인&gt;</c>, <see cref="DexBook.OwnerKey"/>). 조무래기면 「갤럭시단 조무래기」.</summary>
        public static string FoeName(UiStage stage, Ctx ctx, GalacticTrainer leader, List<int> cards)
        {
            if (leader == GalacticTrainer.None || cards.Count == 0) return stage.T("screen.token.grunt");
            string owner = stage.T(DexBook.OwnerKey(ctx.Cards.Pokemon[cards[0]]));
            return stage.T(leader == GalacticTrainer.Cyrus ? "screen.info.galactic.boss" : "screen.info.galactic.commander").Replace("{name}", owner);
        }

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
            monTip?.Hide();
            card.Clear();
            rosterRows.Clear();
            HasRoster = false;
            card.RemoveFromClassList("sn-info-slim");
            card.RemoveFromClassList("sn-info-wide");
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
            // 결정 201 ②: 그 칸에서 만날 갤럭시단 상대의 명단(조무래기 토큰 · 2막 호수 · 3막 신오신전) — 남의 말이 선 칸은 좌석 칸이 카드를 채우니(묶음 여섯이면 화면 높이) 명단을 싣지 않는다 [가정]
            var leader = GalacticTrainer.None;
            int lineup = 0;
            bool foe = seats.Count == 0 && GalacticFoe(flow.Ctx, view, flow.Client != null ? flow.Client.Seat : -1, cell, tokens.Exists(t => t.Kind == TokenKind.Grunt), roster, out leader, out lineup);
            if (seats.Count == 0 && tokens.Count == 0 && beasts.Count == 0 && guardian < 0 && !foe) { Hide(); return false; }
            string subject = "cell-" + cell.ToString(CultureInfo.InvariantCulture);
            if (subject == Subject && version == flow.Client.Version && look == flow.TokenLook && figures == flow.FigureLook()) { Aim(target, side); return true; }
            monTip?.Hide();
            card.Clear();
            rosterRows.Clear();
            HasRoster = foe;
            // 묶음(좌석 둘 이상): 머리 줄 + 좌석마다 줄인 칸(머리 · 파티 한 줄) — 여섯이어도 화면 안에 든다. 자세한 것은 좌석 칩의 카드
            bool stack = seats.Count >= 2;
            if (stack) Add(card, Kit.Text(stage.T("screen.info.stack").Replace("{n}", seats.Count.ToString(CultureInfo.InvariantCulture)), Kit.F2), "info-card-stack", "kit-title");
            foreach (int s in seats) Seat(view, s, stack);
            foreach (var t in tokens) Token(t);
            foreach (string key in beasts) Beast(key);
            if (guardian >= 0) Guardian(view, guardian);
            if (foe) Roster(leader, lineup);
            card.EnableInClassList("sn-info-slim", seats.Count == 0);   // 토큰 · 피규어뿐이면 좁은 카드
            card.EnableInClassList("sn-info-wide", foe);   // 명단이 있으면 1080 벌에서 조금 넓게(결정 201 ②)
            Open(subject, target, side);
            return true;
        }

        /// <summary>그 칸에 띄울 것이 있나(<see cref="ShowCell"/>가 카드를 띄울 칸인가 — 띄우지는 않는다).</summary>
        public bool Has(int cell)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || cell < 0) return false;
            var seats = new List<int>();
            var tokens = new List<TokenState>();
            Subjects(view, flow.Client.Seat, cell, seats, tokens, flow.TokenShown);
            if (seats.Count > 0 || tokens.Count > 0 || flow.GuardianAt(cell) >= 0) return true;
            flow.BeastsAt(cell, beasts);
            return beasts.Count > 0 || GalacticFoe(flow.Ctx, view, flow.Client.Seat, cell, false, new List<int>(), out _, out _);
        }

        public void Hide()
        {
            monTip?.Hide();
            HasRoster = false;
            if (Subject == null) return;
            Subject = null;
            card.style.display = DisplayStyle.None;
        }

        /// <summary>뿌리에서 뗀다(HUD가 닫힐 때).</summary>
        public void Detach()
        {
            Hide();
            card.RemoveFromHierarchy();
            monTip?.Detach();
            monTip = null;
        }

        /// <summary>명단의 그 줄에 마우스를 올린 것처럼 포켓몬 카드를 띄운다(캡처 · 시험 — 실제 마우스는 줄의 <c>PointerEnterEvent</c>). 줄이 없으면 false.</summary>
        public bool ShowRosterMon(int index)
        {
            if (monTip == null || index < 0 || index >= rosterRows.Count || index >= roster.Count) return false;
            monTip.Show(rosterRows[index], roster[index], TipSide.Right, card);
            return true;
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
            if (HasRoster)
            {
                // 결정 201 ②: 카드로 옮겨 온 마우스를 받는다(줄마다 포켓몬 카드) — 보드 쪽으로는 GameFlow.Picked가 이 카드를 뺀다
                card.pickingMode = PickingMode.Position;
                foreach (var row in rosterRows) row.pickingMode = PickingMode.Position;
            }
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
            // 명단 카드(결정 201 ② — 마우스를 받는다)는 위 띠와 아래 판(+ 그 위의 자원 줄 · 돌리기 단추)을 덮지 않는다: 덮으면 그 단추를 누르지 못한다
            float top = 0f, bottom = root.layout.height;
            if (HasRoster)
            {
                var band = root.Q("board-hud-top");
                if (band != null && band.panel != null && band.resolvedStyle.display == DisplayStyle.Flex) top = root.WorldToLocal(band.worldBound).yMax;
                foreach (string name in new[] { "board-hud-dock", "board-hud-res", "board-hud-rotate" })
                {
                    var e = root.Q(name);
                    if (e != null && e.panel != null && e.resolvedStyle.display == DisplayStyle.Flex && e.worldBound.height > 0f) bottom = Mathf.Min(bottom, root.WorldToLocal(e.worldBound).yMin);
                }
                if (bottom - top < size.height) { top = 0f; bottom = root.layout.height; }
            }
            var spot = TipPlace.Place(target.x, target.y - top, target.width, target.height, size.width, size.height, root.layout.width, bottom - top, side, Gap);
            card.style.left = Mathf.Round(spot.X);
            card.style.top = Mathf.Round(spot.Y + top);
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

        // ── 갤럭시단 명단(결정 201 ②) ──

        // 간부 · 태홍이면 이름 줄, 「이 중 n마리」 줄, 명단(두 열 — 도트 · 이름 · 타입), 마우스 오버 안내. 조무래기는 위 토큰 칸이 이름을 적었다
        void Roster(GalacticTrainer leader, int count)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var block = Add(card, new VisualElement(), "info-card-galactic", "sn-info-block");
            if (leader != GalacticTrainer.None) Add(Add(block, new VisualElement(), null, "sn-info-line"), Kit.Text(FoeName(stage, ctx, leader, roster), Kit.F2), "info-card-galactic-head", "kit-title");
            string pick = leader == GalacticTrainer.None ? "screen.info.galactic.pick.random" : "screen.info.galactic.pick";
            Add(block, Kit.Wrapped(stage.T(pick).Replace("{n}", count.ToString(inv)), Kit.F2), "info-card-galactic-pick", null);
            if (leader != GalacticTrainer.None && leader != GalacticTrainer.Cyrus) Add(block, Kit.Wrapped(stage.T("screen.info.galactic.shards"), Kit.F2), null, "kit-muted");
            var list = Add(block, new VisualElement(), null, "sn-info-roster");
            var mons = MonArt.Load();
            monTip ??= new MonTip(stage, ctx);
            for (int i = 0; i < roster.Count; i++)
            {
                var def = ctx.Cards.Pokemon[roster[i]];
                var row = Add(list, new VisualElement(), "info-card-galactic-mon-" + i.ToString(inv), "sn-info-roster-mon");
                row.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var info = Add(row, new VisualElement(), null, "sn-info-roster-info");
                Add(info, Kit.Text(stage.T("pokemon." + def.Key), Kit.F2), null, null);
                var types = Add(info, new VisualElement(), null, "sn-info-line");
                foreach (var t in def.Types) Icon(types, IconArt.Type(t), "sn-type-icon");
                monTip.Hook(row, roster[i], TipSide.Right, card);   // 도감 · 사당 교환 목록과 같은 포켓몬 카드
                rosterRows.Add(row);
            }
            Add(block, Kit.Wrapped(stage.T("screen.info.galactic.hover"), Kit.F2), null, "kit-muted");
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
