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
    /// 정비 판(04 §3-5 「정비」·§10-3 — S18 「내 판」 + S28 기술 가르치기 + S29 지닌물건): 보드 HUD의 [정비](단축키 T)로 여는 창. 내 좌석이 아니면 같은 판을 읽기만 한다(S39 트레이너 상세 — 좌석 칩을 눌러 연다).
    /// <list type="bullet">
    /// <item>파티 칸 여섯 열(결정 206 ③ — 목업 r10-party-row6): 원작 세로 쌓기 카드(<see cref="CardStack"/> — 카드 앞면 + 끼운 띠, 빈 기술 칸은 그리지 않는다 · 누르면 카드 확대 S38) · 열 맨 아래 같은 높이의 [◀][▶] 순서 · [놓아주기].
    /// 기술 칸 = 띠의 자리, 지닌물건 칸 = 카드의 지닌물건 동그라미 자리, 기술 카드를 집거나 끄는 동안 받을 수 있는 빈 칸에는 띠 크기의 홈(찬 칸은 금색 테). 카드 그림이 없으면 도트 카드 + 기술 줄.
    /// 열리지 않은 칸은 「잠긴 칸」(조각을 얻으면 열린다 — rulings A3).</item>
    /// <item>오른쪽 기둥: 카드 능력 줄 · 물음 줄 · 기술 카드 풀(여행 스탬프 보상으로 받은 기술 — 보기의 <c>StampMoves</c>) · 가방(지닌물건 · 볼 · 규토리 · 여행 점수 · 스탬프 · 조각 · 운명의 변화 장수).</item>
    /// <item>조작은 두 번 누르기 + 끌어 놓기(결정 70 ⑪ #42 「정비에 끌어 놓기도 넣는다」): 기술 카드나 지닌물건을 누르면 집히고, 넣을 수 있는 칸이 빛난다 → 칸을 누르면 낸다.
    /// 눌러서 문턱(<c>BoardDrag.Threshold</c> — 보드 끌기와 같다)보다 멀리 끌면 집힌 채 그 카드가 포인터를 따라오고(<see cref="BeginDrag"/>·<see cref="DragTo"/>), 빛나는 칸 위에서 놓으면 그 칸을 누른 것과 같은 명령이 나간다(<see cref="EndDrag"/>) — 다른 곳에서 놓으면 내려놓는다(제자리로).
    /// 포켓몬 카드를 끌어 다른 칸의 카드 위(열)에 놓으면 그 자리로 옮긴다(결정 107 ① — 순서 정비, [◀][▶]와 같은 명령). 찬 기술 칸에 넣거나, 아무것도 집지 않고 배운 기술을 누르면(잊게 하기) 판 아래의 물음 줄에서 한 번 더 묻는다(「○○을(를) 잊게 하겠습니까?」 — 원작 자산 0367:7). 놓아주기도 묻는다.</item>
    /// </list>
    /// 규칙을 다시 세지 않는다: 칸을 켜고 끄는 것은 엔진의 정비 검사(<see cref="GameFlow.CanMaintain"/> — <c>MaintenanceRules.Check</c>) 그대로이고, 누르면 그 정비를 낸다(<see cref="GameFlow.Maintain"/>).
    /// 떠 있는 동안 보드 입력은 꺼지지만 화면 대기열은 멈추지 않는다(정비는 none — 04 §8-1 P2). 창 밖을 누르거나 [닫기]·T로 닫는다.
    /// 요소 이름: <c>board-party</c>·<c>board-party-panel</c>·<c>board-party-title</c>·<c>board-party-guide</c>·<c>board-party-close</c>·<c>board-party-col-&lt;i&gt;</c>·<c>board-party-mon-&lt;i&gt;</c>·
    /// <c>board-party-sig-&lt;i&gt;</c>·<c>board-party-status-&lt;i&gt;-&lt;상태&gt;</c>·<c>board-party-slot-&lt;i&gt;-&lt;1|2&gt;</c>(띠 · 홈 — 빈 칸은 받을 때만)·<c>board-party-item-&lt;i&gt;</c>·<c>board-party-item2-&lt;i&gt;</c>·<c>board-party-form-&lt;i&gt;</c>(폼 바꾸기 — 갈 수 있는 폼이 있을 때만)·<c>board-party-ask-next</c>·<c>board-party-left-&lt;i&gt;</c>·<c>board-party-right-&lt;i&gt;</c>·<c>board-party-release-&lt;i&gt;</c>·
    /// <c>board-party-ghost</c>(끄는 동안 따라오는 카드)·<c>board-party-pool</c>·<c>board-party-pool-&lt;n&gt;</c>·<c>board-party-bag</c>·<c>board-party-bag-&lt;n&gt;</c>·<c>board-party-ask</c>·<c>board-party-ask-yes</c>·<c>board-party-ask-no</c>(i = 파티 칸 0~5, n = 1부터).
    /// </summary>
    public sealed class PartyScreen : IWatchScreen
    {
        /// <summary>집은 것의 종류(Mon = 끌어서 파티 순서를 바꾸는 포켓몬 — 끄는 동안만, 결정 107 ①).</summary>
        public enum Hold { None, Move, Item, Mon }

        public const int Columns = 6;

        readonly GameFlow flow;
        readonly List<int> pool = new List<int>();
        readonly List<int> bag = new List<int>();
        readonly List<int> backs = new List<int>();   // 풀의 기술 카드마다 앞면 포켓몬 카드(카드 표 색인 — 모르면 −1, T30)
        readonly List<DeckId> backDecks = new List<DeckId>();   // 그 카드를 뽑은 덱(등급 — 스탬프 보상은 약함 · 보통 · 강함 덱, 스냅은 전설)
        UiStage stage;
        VisualElement page, cols, poolBox, bagBox, ask, panelBox, recipes;
        Label guide;
        VisualElement ghost;
        // 끌어 놓기: 누른 자리·누른 것(문턱을 넘으면 끌기가 된다) · 놓을 수 있는 칸(다시 그릴 때마다 — 빛나는 칸과 같다)
        readonly List<KeyValuePair<VisualElement, Func<bool>>> targets = new List<KeyValuePair<VisualElement, Func<bool>>>();
        int armedPointer = -1, armedIndex = -1, armedSlot, dragEndFrame = -1, armedFrame = -1;
        Hold armedKind;
        bool armedFromSlot;
        Vector2 armedAt;
        Maintenance asking;
        string askText, askYes;
        bool rejected;
        // 카드 능력(M6 「트레이너 카드 화면」 2 — 정비 판의 능력 줄): 고르는 중인 정비 · 첫 고름(PassMove = 기술을 낼 포켓몬·칸 · Convert = 낼 색)
        MaintKind? picking;
        int pickFrom = -1, pickSlot = -1;   // PassMove = 넘길 포켓몬 · 칸 / Convert = 낼 색(pickSlot) / AttachBreach = 고른 보관 카드(pickFrom — 카드 표 색인)
        VisualElement abil;
        readonly List<Maintenance> options = new List<Maintenance>();

        /// <summary>이 판의 주인 좌석.</summary>
        public readonly int Seat;

        readonly bool watching;

        /// <param name="watch">남의 정비 보기(결정 120 — 그 좌석의 정비 38·40·41행 단계 동안 보기 창으로, 바뀐 카드에 좌석 색 테두리). 가림막 · 창 밖 누르기 없음, [닫기] = 접기.</param>
        public PartyScreen(GameFlow flow, int seat, bool watch = false)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Seat = seat;
            watching = watch;
        }

        /// <summary>보는 좌석(남의 정비 보기면 그 좌석, 아니면 −1).</summary>
        public int Watched => watching ? Seat : -1;

        /// <summary>그 좌석의 정비 단계가 아직 재생 중인가(보기 창).</summary>
        public bool Live => watching && MaintStep() != null;

        public void ShowFx(FxSequencer seq) { }

        // 지금 재생 중인 이 좌석의 정비 단계(없으면 null)
        FxStep MaintStep()
        {
            var step = WorkRows.Playing(flow.Fx != null ? flow.Fx.Sequencer : null, WorkRows.Maint, out int local);
            return step != null && local < step.Ms && WorkRows.SeatOf(step) == Seat ? step : null;
        }

        public string Name => "board-party";

        /// <summary>남의 정비 보기에서 바뀐 카드 열의 클래스.</summary>
        public const string DidClass = "sn-party-col-did";

        /// <summary>내 판인가(고칠 수 있다). 남 좌석의 판은 읽기만 한다.</summary>
        public bool Mine => flow.Client != null && Seat == flow.Client.Seat;

        /// <summary>
        /// 보기만 하는 까닭(글 키 — 결정 125): 물음 창 · 배틀 · 결과 창 위의 정보 창 층에 연 내 판은 조작을 받지 않는다(<see cref="GameFlow.PeekLock"/> — 안내 줄 · 마우스 오버에 한 줄). 받으면 null.
        /// </summary>
        public string Lock { get; private set; }

        // 고칠 수 있나: 내 판이고 잠기지 않았다
        bool Edit => Mine && Lock == null;

        /// <summary>지금 집은 것(없으면 <see cref="Hold.None"/>) · 그 값(기술 색인 / 아이템 색인 / 끄는 포켓몬) · 지닌물건을 집은 포켓몬(가방에서 집었으면 −1).</summary>
        public Hold Held { get; private set; }
        public int HeldValue { get; private set; } = -1;
        public int HeldFrom { get; private set; } = -1;

        /// <summary>포켓몬에게서 집은 지닌물건의 칸(0 · backpacker R의 둘째 칸 1).</summary>
        public int HeldSlot { get; private set; }

        /// <summary>고르는 중인 카드 능력(없으면 null) · 값 단추 줄의 정비들(Convert·Adopt·UseBlessing — 보기 순서).</summary>
        public MaintKind? Picking => picking;
        public IReadOnlyList<Maintenance> Options => options;

        /// <summary>끄는 중인가(카드가 포인터를 따라온다) · 카드의 자리(판 좌표 — 포인터 자리).</summary>
        public bool Dragging { get; private set; }
        public Vector2 GhostAt { get; private set; }

        /// <summary>물음 줄이 떠 있나 · 그 글.</summary>
        public bool Asking => asking != null;
        public string AskText => asking != null ? askText : null;

        /// <summary>기술 카드 풀(기술 색인, 보기 순서) · 가방의 지닌물건(아이템 색인, 보기 순서).</summary>
        public IReadOnlyList<int> Pool => pool;
        public IReadOnlyList<int> Bag => bag;

        /// <summary>풀에서 전설 스냅(tourist R)이 시작되는 자리 — 그 앞은 스탬프 보상 기술 카드.</summary>
        public int SnapFrom { get; private set; }

        /// <summary>안내 줄에 지금 적힌 글.</summary>
        public string GuideText => guide != null ? guide.text : null;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            overBattle = flow.Stage.Current is BattleScreen;
            if (overBattle) page.AddToClassList("sn-party-battle");   // 배틀 화면 위: 판은 독 위에서 끝나고 어둠이 독까지 덮는다(독 단추는 눌리지 않는다)
            if (!watching)   // 보기 창(남의 정비)은 보드를 덮지 않는다
            {
                var scrim = Add(page, new VisualElement(), "board-party-scrim", "sn-party-scrim");
                scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseParty());   // 창 밖(가운데 필드)을 누르면 닫힌다
            }
            var panel = Add(page, Kit.Panel(stage.Skin), "board-party-panel", "sn-party-panel");
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var titleText = Add(head, Kit.Text(KoJosa.Fix(stage.T(Mine ? "screen.party.title.mine" : watching ? (WorkRows.Gift(MaintStep()) ? "screen.party.title.gift" : "screen.party.title.watch") : "screen.party.title.other").Replace("{name}", flow.SeatName(Seat))), Kit.FTitle), "board-party-title", "kit-title");
            if (!Mine) FaceBox.Owner(flow, Seat, titleText);
            guide = Add(head, Kit.Text("", Kit.F2), "board-party-guide", "sn-party-guide");
            panelBox = panel;
            if (Mine && !watching)   // 조합표(결정 191): 내 판이면 보드 · 배틀 중 언제든 — 다시 누르면 닫힌다(결정 176)
            {
                var book = Add(head, Kit.Button(stage.T("screen.party.recipes"), KitButtonKind.Normal, ToggleRecipes), "board-party-recipes-button", null);
                stage.Tips.Hook(book, null, stage.T("screen.party.recipes.tip"), TipSide.Below, reading: true);
            }
            // 배틀 화면 위에 연 판(배틀 독의 [정비])이면 닫기 = 「← 배틀로」(용어표)
            Add(head, Kit.Button(stage.T(flow.Stage.Current is BattleScreen ? "screen.party.close.battle" : "screen.party.close"), KitButtonKind.Main, () => { if (watching) flow.CloseWatch(); else flow.CloseParty(); }), "board-party-close", null);
            // 결정 206 ③(목업 r10-party-row6): 왼쪽 = 포켓몬 카드 여섯 칸 한 줄 · 오른쪽 기둥 = 능력 줄 · 물음 줄 · 기술 카드 · 가방 [가정 ③-①]
            panel.AddToClassList("sn-pcard-panel");
            var body = Add(panel, new VisualElement(), null, "sn-pcard-body");
            cols = Add(body, new VisualElement(), "board-party-cols", "sn-pcard-cols");
            var side = Add(body, new VisualElement(), "board-party-side", "sn-pcard-side");
            abil = Add(side, new VisualElement(), "board-party-abil", "sn-party-abil");   // 카드 능력 줄(내 판 · 쓸 능력이 있을 때만)
            ask = Add(side, new VisualElement(), "board-party-ask", "sn-party-ask");
            poolBox = Add(side, new VisualElement(), "board-party-pool", "sn-party-box");
            bagBox = Add(side, new VisualElement(), "board-party-bag", "sn-party-box");
            bagBox.RegisterCallback<ClickEvent>(e => { if (Held == Hold.Item && HeldFrom >= 0) PressBag(); });
            // 끄는 동안 포인터를 따라오는 카드(결정 73 ④ — 마우스를 받지 않는다) — 판이 포인터를 잡고(capture) 옮김·뗌을 받는다
            ghost = Add(page, new VisualElement(), "board-party-ghost", "sn-party-ghost");
            ghost.pickingMode = PickingMode.Ignore;
            ghost.style.display = DisplayStyle.None;
            page.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            page.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            page.RegisterCallback<PointerCaptureOutEvent>(e => { armedPointer = -1; if (Dragging) CancelDrag(); });
            Refresh();
            return page;
        }

        public void Close()
        {
            Dragging = false;
            armedPointer = -1;
            targets.Clear();
            stage = null;
            guide = null;
            ghost = null;
            abil = null;
            page = null;
            panelBox = recipes = null;
            RecipeCount = 0;
        }

        // ── 조합표(결정 191): 판 위에 덮는 보기 창 — 제작 창의 레시피 줄(그림 · 이름 · 재료)을 보기 전용으로, 효과는 마우스 오버 ──

        /// <summary>조합표가 열려 있나 · 그 줄 수(확인용).</summary>
        public bool RecipesOpen => recipes != null;
        public int RecipeCount { get; private set; }

        /// <summary>[조합표]: 닫혀 있으면 열고 열려 있으면 닫는다.</summary>
        public void ToggleRecipes()
        {
            if (RecipesOpen) CloseRecipes();
            else OpenRecipes();
        }

        public void OpenRecipes()
        {
            if (stage == null || panelBox == null || RecipesOpen) return;
            stage.Tips.Hide();
            recipes = Add(panelBox, Kit.Panel(stage.Skin), "board-party-recipes", "sn-party-recipes");
            var head = Add(recipes, new VisualElement(), null, "sn-party-head");
            Add(head, Kit.Text(stage.T("screen.party.recipes"), Kit.FTitle), "board-party-recipes-title", "kit-title");
            Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Main, CloseRecipes), "board-party-recipes-close", null);
            var scroll = Add(recipes, new ScrollView(ScrollViewMode.Vertical), "board-party-recipes-scroll", "sn-party-recipes-scroll");
            RecipeCount = CraftScreen.Book(stage, flow, Seat, scroll.contentContainer);
        }

        public void CloseRecipes()
        {
            if (!RecipesOpen) return;
            stage?.Tips.Hide();
            recipes.RemoveFromHierarchy();
            recipes = null;
            RecipeCount = 0;
        }

        // ── 그리기 ──

        /// <summary>보기가 바뀌었다(또는 집은 것·물음이 바뀌었다): 판을 지금 값으로 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || cols == null) return;
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || Seat < 0 || Seat >= view.Seats.Length) return;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var seat = view.Seats[Seat];
            stage.Tips.Hide();
            Lock = Mine ? flow.PeekLock(this) : null;
            if (Lock != null && (Held != Hold.None || asking != null || picking != null)) Drop();
            if (Lock != null) asking = null;

            pool.Clear();
            pool.AddRange(seat.StampMoves);
            // 기술 카드 = 스탬프 보상으로 받은 포켓몬 카드의 뒷면(StampRules — StampCards = 덱 · 카드 짝): 기술마다 아직 짝짓지 않은 그 카드(T30 — 등급 · 어느 포켓몬의 뒷면인지)
            backs.Clear();
            backDecks.Clear();
            var paired = new List<int>();
            foreach (int move in seat.StampMoves)
            {
                int card = -1;
                for (int i = 1; i < seat.StampCards.Count && card < 0; i += 2)
                {
                    int c = seat.StampCards[i];
                    if (!paired.Contains(i) && c >= 0 && c < ctx.Cards.Pokemon.Count && ctx.Cards.Pokemon[c].Signature == move)
                    {
                        paired.Add(i);
                        card = c;
                    }
                }
                backs.Add(card);
                backDecks.Add(card >= 0 ? (DeckId)seat.StampCards[paired[paired.Count - 1] - 1] : DeckId.Starter);
            }
            // tourist R: 모은 전설 스냅도 기술 카드처럼 가르친다(정비 SetMove — TrainerRules.SnapOf, rulings A41) — 풀 뒤에 잇는다
            SnapFrom = pool.Count;
            foreach (int card in seat.Snaps)
                if (card >= 0 && card < ctx.Cards.Pokemon.Count && TrainerRules.SnapOf(view, ctx, Seat, ctx.Cards.Pokemon[card].Signature) >= 0)
                {
                    pool.Add(ctx.Cards.Pokemon[card].Signature);
                    backs.Add(card);
                    backDecks.Add(DeckId.Legendary);
                }
            bag.Clear();
            foreach (int item in seat.Bag)
                if (item >= 0 && item < ctx.Items.Items.Count && (ctx.Items.Items[item].Kind == ItemKind.Held || RelicRules.IsRelic(ctx, item) || TrainerRules.Holdable(ctx, item))) bag.Add(item); // 유물도 지닌다(RelicRules) · 대타출동(전용 아이템, rulings A41)
            // 집은 것·물음이 보기에서 사라졌으면(남이 바꿀 수는 없지만 — 내가 낸 정비가 적용된 뒤) 내려놓는다
            if (Held == Hold.Move && !pool.Contains(HeldValue)) Drop();
            if (Held == Hold.Item && (HeldFrom >= 0 ? PokemonRules.PartyIndexOf(seat, HeldFrom) < 0 || HeldOf(view.Pokemon[HeldFrom], HeldSlot) != HeldValue : !bag.Contains(HeldValue))) Drop();
            if (Held == Hold.Mon && PokemonRules.PartyIndexOf(seat, HeldValue) < 0) Drop();
            if (asking != null && flow.CanMaintain(asking) != RejectReason.None) asking = null;
            if (picking != null && asking == null && Candidates(picking.Value).Count == 0) Drop();

            // 파티 칸 여섯 열(결정 206 ③ — 원작 세로 쌓기 카드: 카드 + 끼운 띠, 빈 기술 칸은 그리지 않는다 · 맨 아래 같은 높이의 [◀][▶][놓아주기])
            targets.Clear();
            cols.Clear();
            int count = PokemonRules.PartyCount(seat);
            var changed = watching ? WorkRows.MaintChanged(MaintStep()) : null;
            float width = CardWidth;
            for (int i = 0; i < Columns; i++)
            {
                var col = Add(cols, new VisualElement(), "board-party-col-" + i.ToString(inv), "sn-pcard-col");
                col.style.width = width;
                int id = i < seat.Party.Length ? seat.Party[i] : -1;
                if (i >= seat.OpenSlots)
                {
                    col.AddToClassList("sn-pcard-off");
                    Add(col, Kit.Text(stage.T("screen.party.locked"), Kit.F2), null, "kit-muted");
                    stage.Tips.Hook(col, stage.T("screen.party.locked"), stage.T("screen.party.locked.tip"), TipSide.Below, reading: true);
                    continue;
                }
                if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0)
                {
                    col.AddToClassList("sn-pcard-off");
                    Add(col, Kit.Text(stage.T("screen.party.empty"), Kit.F2), null, "kit-muted");
                    continue;
                }
                var pk = view.Pokemon[id];
                var def = ctx.Cards.Pokemon[pk.Def];
                int index = i, pokemon = id;
                int hp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(id), PokemonRules.Hp(ctx, pk)) : PokemonRules.Hp(ctx, pk);
                var top = Add(col, new VisualElement(), null, "sn-pcard-top");
                // 카드: 원작 카드 그림(체력은 연출이 착지한 표시 값) — 끄는 동안 받을 수 있는 빈 칸에는 그 자리에 홈 띠
                var stack = CardStack.Build(stage, ctx, CardMon.Of(ctx, view, id, hp: hp, hole: Edit ? (Func<int, bool>)(k => SlotOpen(pokemon, k)) : null), width);
                var slots = new VisualElement[PokemonState.MoveSlots];
                var parts = new CardPart[PokemonState.MoveSlots];
                var card = stack.Art ? ArtCard(top, stack, index, pokemon, pk, slots, parts, width) : DotCard(top, index, pokemon, pk, hp, slots);
                if (changed != null && changed.Contains(id))   // 남의 정비 보기: 바뀐 카드(지닌물건 · 기술)에 좌석 색 테두리
                {
                    col.AddToClassList(DidClass);
                    var frame = Add(card, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pcard-did");
                    frame.style.borderLeftColor = frame.style.borderRightColor = frame.style.borderTopColor = frame.style.borderBottomColor = new StyleColor((Color)flow.SeatColor(Seat));
                }

                // 카드 누르기 = 확대(S38) · 끌어 다른 칸(열)에 놓으면 그 자리로(결정 107 ① — [◀][▶]와 같은 정비 PartyOrder)
                stage.OnTap(card, SoundKeys.Select, e => { if (!JustDragged) PressMon(index); });
                if (Edit && (picking == MaintKind.Afflict || picking == MaintKind.CatchSize))   // 포켓몬을 고르는 카드 능력: 받는 포켓몬만 빛난다(엔진 검사 그대로)
                    card.AddToClassList(Candidates(picking.Value).Exists(c => c.Pokemon == pokemon) ? "sn-party-ok" : "sn-party-no");
                if (Edit && Held == Hold.Mon)
                {
                    var order = MovedTo(HeldValue, index);
                    if (pokemon == HeldValue) card.AddToClassList("sn-party-picked");
                    else if (order != null)
                    {
                        card.AddToClassList("sn-party-ok");
                        Target(col, () => Submit(order));
                    }
                }
                else if (Edit) Arm(card, Hold.Mon, index, false);
                if (stack.Art && (card.ClassListContains("sn-party-ok") || card.ClassListContains("sn-party-picked")))   // 빛나는 카드: 앞면 둘레의 테(그림을 덮지 않는다)
                {
                    var ring = Add(card, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pcard-ring");
                    ring.style.width = Mathf.Round(width);
                    ring.style.height = Mathf.Round(CardStack.HeightCm * width / CardStack.WidthCm);
                }
                string fullName = stage.T("pokemon." + def.Key);
                stage.Tips.Hook(card, fullName, stage.T("screen.party.mon.tip"), TipSide.Below, reading: true);

                // 기술 칸: 끼운 띠(배운 기술 · 균열 · 전설 특수기술) · 홈 — 칸마다 가르치기 · 잊게 하기 · 카드 능력 고르기
                for (int k = 0; k < PokemonState.MoveSlots; k++)
                    if (slots[k] != null)
                    {
                        slots[k].name = "board-party-slot-" + i.ToString(inv) + "-" + (k + 1).ToString(inv);
                        WireSlot(slots[k], parts[k], index, pokemon, pk, k);
                    }

                // 순서 · 놓아주기(내 판만): 기술 수와 상관없이 열 맨 아래 같은 높이(결정 206 Q10)
                if (!Edit) continue;
                bool forms = Forms(pokemon).Count > 0;
                var foot = Add(col, new VisualElement(), null, "sn-party-foot");
                foot.AddToClassList("sn-pcard-foot");
                if (forms) foot.AddToClassList("sn-pcard-foot-tight");
                var left = Add(foot, Kit.Button(LeftMark, KitButtonKind.Normal, () => Shift(index, -1)), "board-party-left-" + i.ToString(inv), null);
                var right = Add(foot, Kit.Button(RightMark, KitButtonKind.Normal, () => Shift(index, 1)), "board-party-right-" + i.ToString(inv), null);
                left.SetEnabled(Shifted(index, -1) != null);
                right.SetEnabled(Shifted(index, 1) != null);
                stage.Tips.Hook(left, null, stage.T("screen.party.order.left.tip"), TipSide.Below, reading: true);
                stage.Tips.Hook(right, null, stage.T("screen.party.order.right.tip"), TipSide.Below, reading: true);
                var release = Add(foot, Kit.Button(stage.T("screen.party.release"), KitButtonKind.Normal, () => PressRelease(index)), "board-party-release-" + i.ToString(inv), null);
                bool canRelease = flow.CanMaintain(Release(pokemon, pk.Def)) == RejectReason.None;
                // 꺼진 요소는 마우스를 받지 못해 툴팁이 뜨지 않는다 — 켜 둔 채 흐린 모양만 입히고 누름은 PressRelease가 거른다(보드 HUD의 준비 중 단추와 같다)
                if (!canRelease) release.AddToClassList("sn-hud-off");
                stage.Tips.Hook(release, null, stage.T(canRelease ? "screen.party.release.tip" : count < 2 ? "screen.party.release.last" : "screen.party.guide.rejected"), TipSide.Below, reading: true);
                // 폼체인지(유물·수호신의 서를 지녀 갈 수 있는 폼이 있을 때만): 그림을 덮지 않게 [◀][▶][놓아주기] 줄 끝에 짧은 [폼](검토 지적 3 — 이름은 마우스 오버 머리).
                // 누르면 물음 줄에서 한 번 더 묻는다(폼이 여럿이면 [다른 폼]으로 넘긴다)
                if (forms)
                {
                    // 글 대신 폼을 여는 지닌물건(유물 · 수호신의 서)의 그림 단추 — 폭이 좁은 카드 열(720 160 · 영어 1080)에도 든다. 그림이 없으면(기본 폼으로 되돌리기 등) 짧은 「폼」
                    int relic = pk.HeldItem >= 0 ? pk.HeldItem : pk.HeldItem2;
                    string relicIcon = relic >= 0 && relic < ctx.Items.Items.Count ? WorkUi.IconKey(ctx.Items.Items[relic]) : null;
                    if (relicIcon != null && GenAssets.Load<Texture2D>(relicIcon) == null) relicIcon = null;
                    var form = Add(foot, relicIcon != null ? CardAsk.Button("", relicIcon, KitButtonKind.Normal, () => PressForm(index)) : Kit.Button(stage.T("screen.party.form.short"), KitButtonKind.Normal, () => PressForm(index)), "board-party-form-" + i.ToString(inv), null);
                    if (relicIcon != null)
                    {
                        form.AddToClassList("sn-pcard-formicon");
                        form.Q<Label>(className: "sn-card-pick-text")?.RemoveFromHierarchy();   // 그림만
                    }
                    bool canForm = flow.CanMaintain(ChangeForm(pokemon, Forms(pokemon)[0])) == RejectReason.None;
                    if (!canForm) form.AddToClassList("sn-hud-off");   // 배틀 중: 켜 둔 채 흐리게(툴팁이 뜨게) — 누름은 PressForm이 거른다
                    stage.Tips.Hook(form, stage.T("screen.party.form"), stage.T(canForm ? "screen.party.form.tip" : "screen.party.form.off"), TipSide.Below, reading: true);
                }
            }

            // 기술 카드 풀
            poolBox.Clear();
            var poolHead = Add(poolBox, Kit.Text(stage.T("screen.party.pool").Replace("{n}", SnapFrom.ToString(inv)), Kit.F2), null, "kit-title");
            stage.Tips.Hook(poolHead, null, stage.T("screen.party.pool.tip"), TipSide.Above, reading: true);
            // 기술 카드가 많으면 판 아래로 넘친다 — 휠로 굴리는 칸 + 얇은 위치 막대(T30 — 룰 설명서와 같은 부품)
            RulebookScreen.Scroll(poolBox, "board-party-pool-scroll", out var poolInner, out _);
            var poolRows = Add(poolInner, new VisualElement(), null, "sn-party-pool");
            for (int n = 0; n < pool.Count; n++)
            {
                int at = n;
                if (n == SnapFrom)
                {
                    var snaps = Add(poolRows, Kit.Text(stage.T("screen.party.snaps"), Kit.F2), "board-party-snaps", "sn-party-snaps");
                    stage.Tips.Hook(snaps, null, stage.T("screen.party.snaps.tip"), TipSide.Above, reading: true);
                }
                var row = MonParts.MoveRow(stage, ctx, pool[n], false, TipSide.Above);
                row.name = "board-party-pool-" + (n + 1).ToString(inv);
                row.AddToClassList("sn-party-poolcard");
                PoolBack(row, n);
                if (Edit)
                {
                    row.AddToClassList("sn-party-press");
                    stage.OnTap(row, SoundKeys.Select, e => { if (!JustDragged) PickMove(at); });
                    Arm(row, Hold.Move, at, false);
                }
                if (Held == Hold.Move && pool.IndexOf(HeldValue) == n) row.AddToClassList("sn-party-picked");
                poolRows.Add(row);
            }
            if (pool.Count == 0) Add(poolInner, Kit.Text(stage.T("screen.party.pool.none"), Kit.F2), null, "kit-muted");

            // 가방: 지닌물건 · 볼 · 규토리 · 여행 점수 · 스탬프 · 조각 · 운명의 변화 장수
            bagBox.Clear();
            bagBox.EnableInClassList("sn-party-ok", Edit && Held == Hold.Item && HeldFrom >= 0);
            if (Edit && Held == Hold.Item && HeldFrom >= 0) Target(bagBox, () => PressBag());
            Add(bagBox, Kit.Text(stage.T("screen.party.bag"), Kit.F2), null, "kit-title");
            var items = Add(bagBox, new VisualElement(), null, "sn-party-line");
            for (int n = 0; n < bag.Count; n++)
            {
                int at = n;
                string key = ctx.Items.Items[bag[n]].Key;
                var chip = Add(items, new VisualElement(), "board-party-bag-" + (n + 1).ToString(inv), "sn-party-chip");
                ItemIcon(chip, ctx.Items.Items[bag[n]]);
                chip.Add(Kit.Text(stage.T(key), Kit.F2));
                if (Edit)
                {
                    chip.AddToClassList("sn-party-press");
                    stage.OnTap(chip, SoundKeys.Select, e => { if (!JustDragged) PickItem(at); e.StopPropagation(); });
                    Arm(chip, Hold.Item, at, false);
                }
                if (Held == Hold.Item && HeldFrom < 0 && bag.IndexOf(HeldValue) == n) chip.AddToClassList("sn-party-picked");
                stage.Tips.Hook(chip, stage.T(key), stage.TOrNull(key + ".text"), TipSide.Above, reading: true);
            }
            if (bag.Count == 0) Add(items, Kit.Text(stage.T("screen.party.bag.none"), Kit.F2), null, "kit-muted");
            var balls = Add(bagBox, new VisualElement(), null, "sn-party-line");
            Add(balls, Kit.Text(stage.T("screen.info.balls"), Kit.F2), null, "kit-muted");
            for (int b = 0; b < seat.Balls.Length; b++)
            {
                Icon(balls, GenAssets.ItemIcons + BattleFeed.BallItem(b) + ".png");
                balls.Add(Kit.Text(Res(ResKind.Ball, b, seat.Balls[b]).ToString(inv), Kit.F2));
            }
            // 규토리는 볼과 한 줄(좁으면 줄바꿈 — 열이 긴 판에서 가방 칸 높이를 아낀다)
            Add(balls, Kit.Text(stage.T("screen.info.apricorns"), Kit.F2), null, "kit-muted").AddToClassList("sn-party-gap");
            for (int c = 0; c < seat.Apricorns.Length; c++)
            {
                Icon(balls, EncounterScreen.ApricornKey((ApricornColor)c));
                balls.Add(Kit.Text(Res(ResKind.Apricorn, c, seat.Apricorns[c]).ToString(inv), Kit.F2));
            }
            var foot2 = Add(bagBox, new VisualElement(), null, "sn-party-line");
            foot2.AddToClassList("sn-party-counts");
            foot2.Add(Kit.Text(stage.T("screen.info.jp").Replace("{n}", Res(ResKind.Jp, -1, seat.Jp).ToString(inv)), Kit.F2));
            int stamps = 0, shards = 0;
            foreach (bool on in seat.Stamps) if (on) stamps++;
            foreach (bool on in seat.Shards) if (on) shards++;
            // 누르면 그 좌석의 스탬프 판(S37 — 결정 80 N2 나: 남의 스탬프도 칸까지 본다). 배틀 화면 위의 판에서는 누르지 않는다
            var stampLine = Add(foot2, Kit.Text(stage.T("screen.info.stamps").Replace("{n}", stamps.ToString(inv)), Kit.F2), "board-party-stamps", "kit-muted");
            if (flow.Stage.Current is PartyScreen)
            {
                stampLine.AddToClassList("sn-party-press");
                int owner = Seat;
                stage.OnTap(stampLine, SoundKeys.Select, e => { flow.OpenSeatStamp(owner); e.StopPropagation(); });
                stage.Tips.Hook(stampLine, null, stage.T("screen.party.stamps.tip"), TipSide.Above, reading: true);
            }
            Add(foot2, Kit.Text(stage.T("screen.party.shards").Replace("{n}", shards.ToString(inv)), Kit.F2), null, "kit-muted");
            Add(foot2, Kit.Text(stage.T("screen.info.fate").Replace("{n}", seat.FateHand.Count.ToString(inv)), Kit.F2), null, "kit-muted");
            var owned = RelicRules.Owned(view, ctx, Seat); // 가방 · 지닌 유물
            if (owned.Count > 0)
            {
                var relics = Add(bagBox, new VisualElement(), null, "sn-party-line");
                Add(relics, Kit.Text(stage.T("screen.party.relics"), Kit.F2), null, "kit-muted");
                foreach (int relic in owned)
                    if (relic >= 0 && relic < ctx.Items.Items.Count)
                    {
                        ItemIcon(relics, ctx.Items.Items[relic]);
                        string relicKey = ctx.Items.Items[relic].Key;
                        var relicBadge = Add(relics, Kit.Badge(stage.T(relicKey), KitBadgeKind.Gold, Kit.F2), "board-party-relic-" + (relics.Query(className: "kit-badge").ToList().Count + 1).ToString(inv), null);
                        stage.Tips.Hook(relicBadge, stage.T(relicKey), stage.TOrNull(relicKey + ".text"), TipSide.Above, reading: true);   // 유물 효과(T17)
                    }
            }
            // 트레이너 카드 전용 아이템(rulings G1 — 지닐 수 있는 대타출동은 위 가방 칩에 있어 뺀다)
            var cardItems = Add(bagBox, new VisualElement(), "board-party-carditems", "sn-party-line");
            foreach (int item in seat.CardItems)
                if (item >= 0 && item < ctx.Items.Items.Count && !TrainerRules.Holdable(ctx, item))
                {
                    if (cardItems.childCount == 0) Add(cardItems, Kit.Text(stage.T("screen.party.carditems"), Kit.F2), null, "kit-muted");
                    var chip = Add(cardItems, new VisualElement(), null, "sn-party-chip");
                    ItemIcon(chip, ctx.Items.Items[item]);
                    chip.Add(Kit.Text(stage.T(ctx.Items.Items[item].Key), Kit.F2));
                    stage.Tips.Hook(chip, stage.T(ctx.Items.Items[item].Key), stage.TOrNull(ctx.Items.Items[item].Key + ".text"), TipSide.Above, reading: true);
                }
            if (cardItems.childCount == 0) cardItems.RemoveFromHierarchy();

            // 물음 줄 · 안내 줄
            ask.Clear();
            ask.style.display = asking != null || picking != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (asking != null)
            {
                Add(ask, Kit.Wrapped(askText, Kit.F2), "board-party-ask-text", "sn-party-ask-text");   // 오른쪽 기둥(결정 206 ③): 글 위 · 단추 줄 아래
                var row = Add(ask, new VisualElement(), "board-party-ask-row", "sn-pcard-ask-row");
                if (asking.Kind == MaintKind.ChangeForm && Forms(asking.Pokemon).Count > 1)
                    Add(row, Kit.Button(stage.T("screen.party.form.next"), KitButtonKind.Normal, () => NextForm()), "board-party-ask-next", null);
                Add(row, Kit.Button(askYes, KitButtonKind.Main, () => Confirm()), "board-party-ask-yes", null);
                Add(row, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => Cancel()), "board-party-ask-no", null);
            }
            else if (picking != null)
            {
                // 카드 능력을 고르는 중: 안내 + (값을 고르는 능력이면) 값 단추 + [취소]
                string prompt = picking == MaintKind.AttachBreach
                    ? KoJosa.Fix(stage.T(PickPrompt()).Replace("{card}", pickFrom >= 0 ? BreachName(pickFrom) : ""))
                    : stage.T(PickPrompt()).Replace("{from}", pickSlot >= 0 ? Apricorn(pickSlot) : "");
                Add(ask, Kit.Wrapped(prompt, Kit.F2), "board-party-ask-text", "sn-party-ask-text");
                var row = Add(ask, new VisualElement(), "board-party-ask-row", "sn-pcard-ask-row");
                for (int n = 0; n < options.Count; n++)
                {
                    int at = n;
                    var opt = Add(row, Kit.Button(OptionLabel(options[n]), KitButtonKind.Normal, () => PressOption(at)), "board-party-opt-" + (n + 1).ToString(inv), null);
                    if (picking == MaintKind.AttachBreach)   // 보관한 카드의 효과는 마우스 오버
                        stage.Tips.Hook(opt, BreachName(options[n].Value), MonParts.BreachText(stage, ctx.Decks.CardIds[options[n].Value]), TipSide.Above, reading: true);
                }
                Add(row, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => Cancel()), "board-party-ask-no", null);
            }
            PaintAbilities(view);
            guide.text = !Mine ? stage.T("screen.party.guide.other")
                : Lock != null ? stage.T(Lock)
                : rejected ? stage.T("screen.party.guide.rejected")
                : picking != null ? AbilityHead(picking.Value)
                : Held == Hold.Move ? KoJosa.Fix(stage.T("screen.party.guide.move").Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, HeldValue))))
                : Held == Hold.Item ? KoJosa.Fix(stage.T("screen.party.guide.item").Replace("{item}", stage.T(ctx.Items.Items[HeldValue].Key)))
                : stage.T("screen.party.guide.idle");
            guide.EnableInClassList("sn-party-guide-lock", Lock != null);
            if (Lock != null) stage.Tips.Hook(guide, stage.T("screen.peek.lock"), stage.T(Lock + ".tip"), TipSide.Below, reading: true);
            // 끄던 것이 보기에서 사라졌으면(위에서 내려놓았다) 끌기도 끝낸다
            if (Dragging && Held == Hold.None) CancelDrag();
        }

        // ── 끌어 놓기(결정 70 ⑪) ──

        // 끌 수 있는 것(기술 카드 · 가방의 지닌물건 · 포켓몬이 지닌 것): 누른 자리만 적어 둔다 — 문턱을 넘기 전에는 누르기(두 번 누르기) 그대로다
        void Arm(VisualElement element, Hold kind, int index, bool fromSlot, int itemSlot = 0)
        {
            element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !Edit || Dragging || asking != null || picking != null) return;
                if (armedPointer == e.pointerId && armedFrame == Time.frameCount) return;   // 카드 안의 지닌물건이 먼저 집혔다(누름이 카드까지 올라온다)
                armedFrame = Time.frameCount;
                armedPointer = e.pointerId;
                armedKind = kind;
                armedIndex = index;
                armedFromSlot = fromSlot;
                armedSlot = itemSlot;
                armedAt = e.position;
            });
        }

        void Target(VisualElement element, Func<bool> put) => targets.Add(new KeyValuePair<VisualElement, Func<bool>>(element, put));

        // 끌기가 끝난 프레임의 클릭은 누르기로 치지 않는다(끌다 제자리에 놓은 것이 「다시 집기」가 되지 않게)
        bool JustDragged => dragEndFrame == Time.frameCount;

        void OnPointerMove(PointerMoveEvent e)
        {
            if (Dragging)
            {
                if (e.pointerId == armedPointer) DragTo(e.position);
                return;
            }
            if (armedPointer != e.pointerId) return;
            if ((e.pressedButtons & 1) == 0) { armedPointer = -1; return; }
            float height = stage != null && stage.Root != null ? stage.Root.resolvedStyle.height : 0f;
            double threshold = BoardDrag.Threshold(float.IsNaN(height) ? 0 : Mathf.RoundToInt(height));
            Vector2 d = (Vector2)e.position - armedAt;
            if (d.sqrMagnitude < threshold * threshold) return;
            if (!BeginDrag(armedKind, armedIndex, armedFromSlot, armedSlot)) { armedPointer = -1; return; }
            flow.PlaySound(SoundKeys.Select);
            page.CapturePointer(e.pointerId);
            DragTo(e.position);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (e.pointerId != armedPointer) return;
            armedPointer = -1;
            if (!Dragging) return;
            if (page.HasPointerCapture(e.pointerId)) page.ReleasePointer(e.pointerId);
            if (EndDrag(e.position)) flow.PlaySound(SoundKeys.Select);
            e.StopPropagation();
        }

        /// <summary>
        /// 끌기 시작(마우스가 문턱을 넘었을 때 — 시험·확인도 이것을 부른다): 그것을 집고(두 번 누르기의 첫 누름과 같다 — 넣을 수 있는 칸이 빛난다) 그 카드를 띄운다.
        /// kind = 기술이면 n = 기술 카드 풀의 n번째, 지닌물건이면 가방의 n번째(fromSlot이면 파티 칸 n의 포켓몬이 지닌 것). 집을 수 없으면 false.
        /// </summary>
        public bool BeginDrag(Hold kind, int n, bool fromSlot = false, int itemSlot = 0)
        {
            if (stage == null || !Edit || Dragging || asking != null || picking != null) return false;
            if (kind == Hold.Move)
            {
                if (n < 0 || n >= pool.Count) return false;
                if (!(Held == Hold.Move && HeldValue == pool[n]) && !PickMove(n)) return false;
            }
            else if (kind == Hold.Item && !fromSlot)
            {
                if (n < 0 || n >= bag.Count) return false;
                if (!(Held == Hold.Item && HeldFrom < 0 && HeldValue == bag[n]) && !PickItem(n)) return false;
            }
            else if (kind == Hold.Item)
            {
                int id = PokemonAt(n);
                if (id < 0) return false;
                if (!(Held == Hold.Item && HeldFrom == id && HeldSlot == itemSlot))
                {
                    Drop();
                    if (!PressItemSlot(n, itemSlot) || Held != Hold.Item || HeldFrom != id || HeldSlot != itemSlot) { Refresh(); return false; }
                }
            }
            else if (kind == Hold.Mon)
            {
                // 파티 칸 n의 포켓몬(결정 107 ①): 옮길 수 있는 자리가 하나라도 있어야 집는다(마지막 한 마리 · 순서를 못 바꾸는 때는 끌리지 않는다)
                int id = PokemonAt(n);
                if (id < 0 || picking != null) return false;
                bool any = false;
                for (int to = 0; to < Columns && !any; to++) any = to != n && MovedTo(id, to) != null;
                if (!any) return false;
                Drop();
                Held = Hold.Mon;
                HeldValue = id;
                Refresh();
            }
            else return false;
            Dragging = true;
            stage.Tips.Hide();
            PaintGhost();
            ghost.style.display = DisplayStyle.Flex;
            ghost.BringToFront();
            return true;
        }

        // 결정 73 ④(C7 ① 나 「카드 그림 전체가 따라온다」): 집은 것의 카드를 판에 그린 꼴 그대로 — 기술 카드는 풀의 카드(타입 · 이름 · 위력, 같은 폭), 지닌물건은 가방의 칩. 집은 모양(sn-party-picked)을 입힌다
        void PaintGhost()
        {
            ghost.Clear();
            VisualElement card;
            var ctx = flow.Ctx;
            float width = CardWidth;
            // 결정 206 ③: 기술 카드는 그 기술 띠(포켓몬 카드 뒷면의 홈 ~ 아래 끝 — 판의 띠와 같은 폭, 위 모서리 둥글게) · 포켓몬은 카드 앞면이 따라온다. 그림이 없으면 판의 줄 꼴
            var strip = Held == Hold.Move ? GenAssets.Load<Texture2D>(GenAssets.CardMoves + (stage.Lang == "en" ? "en" : "ko") + "/" + ctx.Cards.Moves[HeldValue].Key + ".png") : null;
            var face = Held == Hold.Mon ? CardStack.Build(stage, ctx, CardMon.Wild(ctx, flow.Client.View.Pokemon[HeldValue].Def), Mathf.Round(width * 0.6f)) : null;
            // 지닌물건: 카드 그림 판이면 카드 동그라미에 얹히는 토큰 그림 하나(포인터 가운데 — 가방 칩이 카드 타입 줄 위에 얹히지 않게, 검토 지적 14)
            string token = Held == Hold.Item && CardArt ? CardStack.TokenOrIcon(ctx, HeldValue) : null;
            var tokenTex = token != null ? GenAssets.Load<Texture2D>(token) : null;
            ghostSize = Vector2.zero;
            ghostCenter = ghostKeep = false;
            if (strip != null)
            {
                card = new VisualElement();
                card.AddToClassList("sn-pcard-ghost");
                card.style.width = Mathf.Round(width);
                card.style.height = Mathf.Round(width * strip.height / strip.width);
                card.style.backgroundImage = new StyleBackground(strip);
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius = Mathf.Round(0.55f * width / CardStack.WidthCm);
                ghostSize = new Vector2(Mathf.Round(width), Mathf.Round(width * strip.height / strip.width));
                ghostKeep = true;   // 반투명 띠는 판의 카드 자리 안에 가둔다(단추 줄 · 오른쪽 기둥을 덮지 않게 — 목업 r10-party-drag)
            }
            else if (tokenTex != null)
            {
                CardStack.ItemSpot(0, out _, out _, out float size);
                float d = Mathf.Round(size * width / CardStack.WidthCm);
                card = new VisualElement();
                card.AddToClassList("sn-pcard-ghost");
                card.style.width = card.style.height = d;
                card.style.backgroundImage = new StyleBackground(tokenTex);
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = Mathf.Round(d / 2f);
                ghostSize = new Vector2(d, d);
                ghostCenter = true;
            }
            else if (face != null && face.Art)
            {
                card = new VisualElement();
                card.AddToClassList("sn-pcard-ghost");
                card.Add(face.Root);
            }
            else if (Held == Hold.Move)
            {
                card = MonParts.MoveRow(stage, ctx, HeldValue, false, null);
                card.AddToClassList("sn-party-poolcard");
                card.AddToClassList("sn-party-picked");
            }
            else if (Held == Hold.Mon)
            {
                // 끄는 포켓몬: 판의 카드처럼 도트 + 이름
                string key = ctx.Cards.Pokemon[flow.Client.View.Pokemon[HeldValue].Def].Key;
                var mons = MonArt.Load();
                card = new VisualElement();
                card.AddToClassList("sn-party-card");
                card.Add(mons != null ? mons.Front(key) : new VisualElement());
                card.Add(Kit.Text(BoardHud.ShortName(stage.T("pokemon." + key)), Kit.F2));
                card.AddToClassList("sn-party-picked");
            }
            else
            {
                card = new VisualElement();
                card.AddToClassList("sn-party-chip");
                ItemIcon(card, flow.Ctx.Items.Items[HeldValue]);
                card.Add(Kit.Text(stage.T(flow.Ctx.Items.Items[HeldValue].Key), Kit.F2));
                card.AddToClassList("sn-party-picked");
            }
            ghost.Add(card);
            card.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
        }

        /// <summary>끄는 중: 카드를 포인터 자리(판 좌표)로 옮긴다.</summary>
        public void DragTo(Vector2 at)
        {
            if (!Dragging) return;
            GhostAt = at;
            Vector2 local = page.WorldToLocal(at);
            if (ghostCenter)
            {
                local -= ghostSize / 2f;
                local.x -= float.IsNaN(ghost.resolvedStyle.left) ? 0f : ghost.resolvedStyle.left;   // 토큰 가운데 = 포인터
                local.y -= float.IsNaN(ghost.resolvedStyle.top) ? 0f : ghost.resolvedStyle.top;
            }
            if (ghostKeep && cols != null && cols.panel != null)
            {
                // 카드 여섯 열의 자리(위 = 열 위 끝 · 아래 = 단추 줄 위 끝 · 오른쪽 = 열 오른쪽 끝) 안에 — 놓는 칸은 포인터 자리로 고른다(EndDrag)
                var area = cols.worldBound;
                var foot = cols.Q(className: "sn-pcard-foot");
                float bottom = foot != null && !float.IsNaN(foot.worldBound.yMin) ? foot.worldBound.yMin : area.yMax;
                var min = page.WorldToLocal(new Vector2(area.xMin, area.yMin));
                var max = page.WorldToLocal(new Vector2(area.xMax, bottom));
                float ox = float.IsNaN(ghost.resolvedStyle.left) ? 0f : ghost.resolvedStyle.left, oy = float.IsNaN(ghost.resolvedStyle.top) ? 0f : ghost.resolvedStyle.top;   // USS 자리(sn-party-ghost) 몫
                local.x = Mathf.Clamp(local.x, min.x - ox, Mathf.Max(min.x - ox, max.x - ghostSize.x - ox));
                local.y = Mathf.Clamp(local.y, min.y - oy, Mathf.Max(min.y - oy, max.y - ghostSize.y - oy));
            }
            ghost.style.translate = new Translate(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        // 끄는 것의 크기(px) · 포인터를 가운데에 두나(토큰) · 카드 자리 안에 가두나(기술 띠)
        Vector2 ghostSize;
        bool ghostCenter, ghostKeep;

        // 이 판이 원작 카드 그림으로 그려졌나(첫 카드 열 기준)
        bool CardArt => cols != null && cols.Q("card-face") != null;

        /// <summary>
        /// 놓았다(판 좌표): 빛나는 칸(기술 칸 · 지닌물건 칸 · 가방) 위면 그 칸을 누른 것과 같은 명령을 낸다(찬 기술 칸이면 물음 줄 — 두 번 누르기와 같다).
        /// 그 밖에서 놓으면 내려놓는다(카드는 제자리에 그대로). 낸 것(또는 물음)이 있으면 true.
        /// </summary>
        public bool EndDrag(Vector2 at)
        {
            if (!Dragging) return false;
            Func<bool> put = null;
            foreach (var t in targets)
                if (t.Key.panel != null && t.Key.worldBound.Contains(at)) { put = t.Value; break; }
            StopDrag();
            if (put != null && put()) return true;
            if (asking == null && Held != Hold.None)
            {
                Drop();
                Refresh();
            }
            return false;
        }

        /// <summary>끌기를 거둔다(판이 닫히거나 포인터를 잃었을 때): 집은 것을 내려놓는다.</summary>
        public void CancelDrag()
        {
            if (!Dragging) return;
            StopDrag();
            if (asking == null && Held != Hold.None)
            {
                Drop();
                Refresh();
            }
        }

        void StopDrag()
        {
            Dragging = false;
            dragEndFrame = Time.frameCount;
            if (ghost != null) ghost.style.display = DisplayStyle.None;
        }

        /// <summary>순서 단추의 글(기호라 문자열 표에 두지 않는다 — 뜻은 툴팁).</summary>
        public const string LeftMark = "◀", RightMark = "▶";

        static string Lines(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "\n" + b;

        // 자원의 표시 값: 연출이 착지하기 전에는 붙잡아 둔 값(04 §8-3 — 공개 정보 카드와 같다)
        int Res(ResKind res, int key, int now) => flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(Seat, res, key), now) : now;

        // ── 정비 명령(결과를 적는 절대형 — rulings G8) ──

        static Maintenance SetMove(int pokemon, int slot, int move) => new Maintenance { Kind = MaintKind.SetMove, Pokemon = pokemon, Slot = slot, Value = move };

        static Maintenance HoldItem(int pokemon, int item, int slot = 0) => new Maintenance { Kind = MaintKind.HoldItem, Pokemon = pokemon, Slot = slot, Value = item };

        // 그 칸(0 · 둘째 칸 1)에 지닌 것
        static int HeldOf(PokemonState p, int slot) => slot == 1 ? p.HeldItem2 : p.HeldItem;

        static Maintenance ChangeForm(int pokemon, int to) => new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = pokemon, Value = to };

        /// <summary>
        /// 그 포켓몬이 정비로 갈 수 있는 폼(포켓몬 표 색인, 표 순서 — 지금 폼은 빼고): 엔진의 정비 검사가 보는 것 그대로(<c>FormRules.ItemFormAllowed</c> — 유물 폼 · 수호신 폼 · 기본 폼으로 돌아가기).
        /// 때(배틀 밖)는 여기서 보지 않는다 — 배틀 중에는 줄이 흐리게 남는다.
        /// </summary>
        public List<int> Forms(int pokemon)
        {
            var forms = new List<int>();
            var view = flow.Client != null ? flow.Client.View : null;
            if (!Edit || view == null || pokemon < 0 || pokemon >= view.Pokemon.Count || view.Pokemon[pokemon].Def < 0) return forms;
            var p = view.Pokemon[pokemon];
            for (int to = 0; to < flow.Ctx.Cards.Pokemon.Count; to++)
                if (to != p.Def && FormRules.ItemFormAllowed(flow.Ctx, p, to)) forms.Add(to);
            return forms;
        }

        // 폼의 이름: 카드 이름의 괄호 안(「쉐이미 (스카이폼)」 → 「스카이폼」), 괄호가 없으면(기본 폼) 「기본 폼」
        string FormName(int def)
        {
            string full = stage.T("pokemon." + flow.Ctx.Cards.Pokemon[def].Key);
            int open = full.IndexOf('('), close = full.LastIndexOf(')');
            return open > 0 && close > open ? full.Substring(open + 1, close - open - 1) : stage.T("screen.party.form.base");
        }

        bool AskForm(int pokemon, int to)
        {
            var view = flow.Client.View;
            string mon = BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[pokemon].Def].Key));
            return Ask(ChangeForm(pokemon, to), KoJosa.Fix(stage.T("screen.party.ask.form").Replace("{mon}", mon).Replace("{form}", FormName(to))), stage.T("screen.party.ask.form.yes"));
        }

        static Maintenance Release(int pokemon, int species) => new Maintenance { Kind = MaintKind.Release, Pokemon = pokemon, Value = species };

        // 그 칸의 포켓몬을 한 자리 옮긴 파티 순서(엔진이 받는 것만 — 못 옮기면 null)
        Maintenance Shifted(int index, int by)
        {
            var seat = Edit ? flow.Client.View.Seats[Seat] : null;
            if (seat == null || index < 0 || index >= seat.Party.Length || seat.Party[index] < 0) return null;
            var order = new List<int>();
            foreach (int p in seat.Party) if (p >= 0) order.Add(p);
            int at = order.IndexOf(seat.Party[index]), to = at + by;
            if (to < 0 || to >= order.Count) return null;
            int moved = order[at];
            order[at] = order[to];
            order[to] = moved;
            var m = new Maintenance { Kind = MaintKind.PartyOrder, Values = order };
            return flow.CanMaintain(m) == RejectReason.None ? m : null;
        }

        // 그 포켓몬을 파티 칸 to의 포켓몬 자리로 옮긴 파티 순서(빼서 그 자리에 끼운다 — 사이 포켓몬은 한 칸씩 밀린다, 07 T200). 엔진이 받는 것만 — 못 옮기면 null
        Maintenance MovedTo(int pokemon, int to)
        {
            var seat = Edit ? flow.Client.View.Seats[Seat] : null;
            int target = PokemonAt(to);
            if (seat == null || target < 0 || target == pokemon) return null;
            var order = new List<int>();
            foreach (int p in seat.Party) if (p >= 0) order.Add(p);
            int at = order.IndexOf(pokemon), dest = order.IndexOf(target);
            if (at < 0 || dest < 0) return null;
            order.RemoveAt(at);
            order.Insert(dest, pokemon);
            var m = new Maintenance { Kind = MaintKind.PartyOrder, Values = order };
            return flow.CanMaintain(m) == RejectReason.None ? m : null;
        }

        int PokemonAt(int index)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || Seat >= view.Seats.Length || index < 0 || index >= view.Seats[Seat].Party.Length) return -1;
            return view.Seats[Seat].Party[index];
        }

        // ── 입력(마우스가 누르는 것과 같은 함수 — 캡처·시험·길잡이도 이것을 부른다) ──

        /// <summary>기술 카드 풀의 n번째(0부터) 카드를 집는다. 집은 카드를 다시 누르면 내려놓는다.</summary>
        public bool PickMove(int n)
        {
            if (!Edit || n < 0 || n >= pool.Count) return false;
            bool same = Held == Hold.Move && HeldValue == pool[n];
            Drop();
            if (!same)
            {
                Held = Hold.Move;
                HeldValue = pool[n];
            }
            Refresh();
            return true;
        }

        /// <summary>가방의 n번째(0부터) 지닌물건을 집는다. 집은 것을 다시 누르면 내려놓는다.</summary>
        public bool PickItem(int n)
        {
            if (!Edit || n < 0 || n >= bag.Count) return false;
            bool same = Held == Hold.Item && HeldFrom < 0 && HeldValue == bag[n];
            Drop();
            if (!same)
            {
                Held = Hold.Item;
                HeldValue = bag[n];
            }
            Refresh();
            return true;
        }

        /// <summary>
        /// 기술 칸(파티 칸 index, 칸 slot 0·1)을 눌렀다: 기술 카드를 집고 있으면 그 칸에 가르친다(찬 칸이면 묻는다) · 집은 것이 없고 찬 칸이면 잊게 할지 묻는다.
        /// 낸 것(또는 물음)이 있으면 true.
        /// </summary>
        public bool PressSlot(int index, int slot)
        {
            int id = PokemonAt(index);
            if (!Edit || id < 0 || slot < 0 || slot >= PokemonState.MoveSlots) return false;
            if (picking == MaintKind.PassMove) return PassAt(id, slot);
            if (picking == MaintKind.AttachBreach) return AttachAt(id, slot);
            if (picking != null) return false;
            var ctx = flow.Ctx;
            int old = flow.Client.View.Pokemon[id].Moves[slot];
            if (Held == Hold.Move)
            {
                var m = SetMove(id, slot, HeldValue);
                if (flow.CanMaintain(m) != RejectReason.None) return false;
                if (old < 0) return Submit(m);
                return Ask(m, KoJosa.Fix(stage.T("screen.party.ask.replace").Replace("{old}", stage.T(BattleFeed.MoveNameKey(ctx, old))).Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, HeldValue)))),
                    stage.T("screen.party.ask.replace.yes"));
            }
            if (Held != Hold.None || old < 0) return false;
            var forget = SetMove(id, slot, -1);
            if (flow.CanMaintain(forget) != RejectReason.None) return false;
            return Ask(forget, KoJosa.Fix(stage.T("screen.party.ask.forget").Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, old)))), stage.T("screen.party.ask.forget.yes"));
        }

        /// <summary>
        /// 지닌물건 칸(파티 칸 index)을 눌렀다: 지닌물건을 집고 있으면 그 포켓몬에게 지니게 한다(원래 것은 가방으로) · 집은 것이 없고 지닌 것이 있으면 그것을 집는다(다시 누르면 내려놓는다).
        /// </summary>
        public bool PressItemSlot(int index, int slot = 0)
        {
            int id = PokemonAt(index);
            if (!Edit || id < 0 || picking != null) return false;
            if (Held == Hold.Item)
            {
                if (HeldFrom == id && HeldSlot == slot)
                {
                    Drop();
                    Refresh();
                    return true;
                }
                var m = HoldItem(id, HeldValue, slot);
                return flow.CanMaintain(m) == RejectReason.None && Submit(m);
            }
            int item = HeldOf(flow.Client.View.Pokemon[id], slot);
            if (Held != Hold.None || item < 0 || flow.CanMaintain(HoldItem(id, -1, slot)) != RejectReason.None) return false;
            Drop();
            Held = Hold.Item;
            HeldValue = item;
            HeldFrom = id;
            HeldSlot = slot;
            Refresh();
            return true;
        }

        /// <summary>집은 것을 그 칸(파티 칸 index — 기술이면 칸 slot 0·1, 지닌물건이면 slot 무시)에 넣을 수 있나(엔진 검사 그대로 — 빛나는 칸).</summary>
        public bool CanPut(int index, int slot = 0)
        {
            int id = PokemonAt(index);
            if (!Edit || id < 0) return false;
            if (Held == Hold.Move) return flow.CanMaintain(SetMove(id, slot, HeldValue)) == RejectReason.None;
            return Held == Hold.Item && HeldFrom != id && flow.CanMaintain(HoldItem(id, HeldValue)) == RejectReason.None;
        }

        /// <summary>가방을 눌렀다: 포켓몬에게서 집은 지닌물건을 가방에 넣는다.</summary>
        public bool PressBag()
        {
            if (!Edit || Held != Hold.Item || HeldFrom < 0) return false;
            var m = HoldItem(HeldFrom, -1, HeldSlot);
            return flow.CanMaintain(m) == RejectReason.None && Submit(m);
        }

        /// <summary>파티 칸 index의 포켓몬을 한 자리 앞(by = −1)·뒤(+1)로 옮긴다.</summary>
        public bool Shift(int index, int by)
        {
            var m = Shifted(index, by);
            return m != null && Submit(m);
        }

        /// <summary>[놓아주기]: 한 번 더 묻는다. 놓아줄 수 없으면(마지막 포켓몬 등) false.</summary>
        public bool PressRelease(int index)
        {
            int id = PokemonAt(index);
            if (!Edit || id < 0) return false;
            int species = flow.Client.View.Pokemon[id].Def;
            var m = Release(id, species);
            if (flow.CanMaintain(m) != RejectReason.None) return false;
            Drop();
            return Ask(m, KoJosa.Fix(stage.T("screen.party.ask.release").Replace("{mon}", stage.T("pokemon." + flow.Ctx.Cards.Pokemon[species].Key))), stage.T("screen.party.release"));
        }

        /// <summary>[폼 바꾸기](파티 칸 index): 갈 수 있는 첫 폼으로 바꿀지 묻는다(지금은 할 수 없으면 — 배틀 중 — false).</summary>
        public bool PressForm(int index)
        {
            int id = PokemonAt(index);
            if (!Edit || id < 0) return false;
            var forms = Forms(id);
            if (forms.Count == 0 || flow.CanMaintain(ChangeForm(id, forms[0])) != RejectReason.None) return false;
            Drop();
            return AskForm(id, forms[0]);
        }

        /// <summary>폼 바꾸기 물음의 [다른 폼]: 갈 수 있는 다음 폼으로 물음을 바꾼다(끝이면 처음으로).</summary>
        public bool NextForm()
        {
            if (asking == null || asking.Kind != MaintKind.ChangeForm) return false;
            var forms = Forms(asking.Pokemon);
            if (forms.Count < 2) return false;
            return AskForm(asking.Pokemon, forms[(forms.IndexOf(asking.Value) + 1) % forms.Count]);
        }

        /// <summary>포켓몬 카드를 눌렀다: 카드 확대(S38)를 연다.</summary>
        public bool PressMon(int index)
        {
            int id = PokemonAt(index);
            if (id < 0) return false;
            if (picking == MaintKind.Afflict || picking == MaintKind.CatchSize) return AskTarget(id);
            return picking == null && flow.OpenMon(id, Seat);
        }

        /// <summary>물음 줄의 확인 단추: 물은 정비를 낸다.</summary>
        public bool Confirm() => asking != null && Submit(asking);

        /// <summary>물음을 거두고 집은 것을 내려놓는다.</summary>
        public void Cancel()
        {
            asking = null;
            Drop();
            Refresh();
        }

        bool Ask(Maintenance m, string text, string yes)
        {
            asking = m;
            askText = text;
            askYes = yes;
            rejected = false;
            Refresh();
            return true;
        }

        void Drop()
        {
            Held = Hold.None;
            HeldValue = HeldFrom = -1;
            HeldSlot = 0;
            rejected = false;
            picking = null;
            pickFrom = pickSlot = -1;
            options.Clear();
        }

        // 정비를 낸다. 받아들여지면 갱신이 바로 와 판이 다시 그려진다(GameFlow.Sync) — 그 전에 집은 것·물음을 비운다
        bool Submit(Maintenance m)
        {
            asking = null;
            Drop();
            var result = flow.Maintain(m);
            rejected = !result.Accepted;
            Refresh();
            return result.Accepted;
        }

        /// <summary>
        /// 정비 판 카드 폭(px — 목업 9차 표: 720 벌 160 · 1080 벌 232, 카드 16 cm). 배틀 화면 위에 연 판은 독 위에서 끝나 낮으니(sn-party-battle) 가장 긴 쌓기(카드 + 띠 둘 ≈ 47 cm)가
        /// 단추 줄 위에 들게 줄인다 — 720 벌 150 · 1080 벌 216 [가정 ③-④].
        /// </summary>
        float CardWidth => stage != null && stage.Grade.TokenHeight >= 1080 ? (overBattle ? 216f : 232f) : (overBattle ? 150f : 160f);

        bool overBattle;

        // 그 기술 칸이 지금 받는 칸인가(기술 카드를 집었거나 · 카드 능력으로 칸을 고르는 중 — 엔진 검사 그대로): 빈 칸이면 이때만 홈이 선다
        bool SlotOpen(int pokemon, int slot)
        {
            if (!Edit) return false;
            if (picking == MaintKind.AttachBreach) return pickFrom >= 0 && Candidates(MaintKind.AttachBreach).Exists(c => c.Value == pickFrom && c.Pokemon == pokemon && c.Slot == slot);
            if (picking == MaintKind.PassMove) return (pokemon == pickFrom && slot == pickSlot) || PassOk(pokemon, slot);
            if (picking != null) return false;
            return Held == Hold.Move && flow.CanMaintain(SetMove(pokemon, slot, HeldValue)) == RejectReason.None;
        }

        // 원작 카드 그림의 칸(결정 206 ③): 카드 쌓기를 감싸 누르기 · 끌기를 받고, 쌓기의 자리(★ 고유 기술 · 상태 주사위 · 띠 · 홈)에 이름과 툴팁을, 지닌물건 동그라미 자리에 받는 칸을 얹는다
        VisualElement ArtCard(VisualElement top, CardStack stack, int index, int pokemon, PokemonState pk, VisualElement[] slots, CardPart[] parts, float width)
        {
            var inv = CultureInfo.InvariantCulture;
            var card = Add(top, new VisualElement(), "board-party-mon-" + index.ToString(inv), "sn-pcard-mon");
            card.Add(stack.Root);
            var statuses = new List<StatusEffect>(2);
            StatusRules.Statuses(pk, statuses);
            var held = new List<CardPart>(4);
            int status = 0;
            foreach (var p in stack.Parts)
                switch (p.Kind)
                {
                    case CardPartKind.Signature:
                        p.Element.name = "board-party-sig-" + index.ToString(inv);
                        Hook(p);
                        break;
                    case CardPartKind.Status:   // 결정 180: 걸린 상태 = 카드 그림 판 왼쪽 위 주사위(이름은 아래 판 파티 칸 아이콘과 같은 꼴)
                        p.Element.name = "board-party-status-" + index.ToString(inv) + "-" + (status < statuses.Count ? statuses[status++].ToString().ToLowerInvariant() : "x");
                        Hook(p);
                        break;
                    case CardPartKind.Move:
                    case CardPartKind.Breach:
                    case CardPartKind.AlphaStrike:
                    case CardPartKind.BeastBoost:
                    case CardPartKind.Hole:
                        held.Add(p);
                        break;
                }
            // 기술 칸 k → 그 칸의 띠 자리(쌓기 순서 = 칸 순서 · 전설 전술 판 한 장은 칸 둘) — 빈 칸은 홈이 섰을 때만
            for (int k = 0, at = 0; k < PokemonState.MoveSlots && at < held.Count; k++)
            {
                bool filled = pk.Moves[k] >= 0 || pk.Breach[k] >= 0 || LegendRules.TacticAt(pk, k) != LegendTactic.None;
                if (!filled && !SlotOpen(pokemon, k)) continue;
                var p = held[at++];
                p.Element.pickingMode = PickingMode.Position;
                p.Element.AddToClassList("sn-pcard-hit");
                p.Element.userData = p.Title;
                if (p.Kind == CardPartKind.AlphaStrike || p.Kind == CardPartKind.BeastBoost) p.Element.AddToClassList(MonParts.LegendSlotClass);
                slots[k] = p.Element;
                parts[k] = p;
            }
            // 지닌물건 동그라미(받는 자리 — 우두머리 토큰 · 지닌물건 순서는 쌓기 그대로, 빈 칸은 그 뒤 동그라미 자리)
            var face = stack.Root.Q("card-face");
            if (face == null) return card;
            float u = width / CardStack.WidthCm;
            bool two = TrainerRules.Pockets(flow.Client.View, flow.Ctx, Seat);
            int present = (pk.Alpha ? 1 : 0) + (pk.HeldItem >= 0 ? 1 : 0) + (pk.HeldItem2 >= 0 ? 1 : 0), empty = 0;
            for (int s = 0; s < (two ? 2 : 1); s++)
            {
                int circle = s == 0 && pk.Alpha ? 0 : HeldOf(pk, s) >= 0 ? (pk.Alpha ? 1 : 0) + (s == 1 && pk.HeldItem >= 0 ? 1 : 0) : present + empty++;
                CardStack.ItemSpot(Mathf.Min(circle, 2), out float x, out float y, out float size);
                var spot = Add(face, new VisualElement(), (s == 1 ? "board-party-item2-" : "board-party-item-") + index.ToString(inv), "sn-pcard-item");
                spot.style.left = Mathf.Round(x * u);
                spot.style.top = Mathf.Round(y * u);
                spot.style.width = spot.style.height = Mathf.Round(size * u);
                ItemSlot(spot, index, pokemon, pk, s, true);
            }
            return card;
        }

        // 띠 위의 칸 자리(쌓기의 보이지 않는 칸)를 안쪽으로 줄이고 둥글게: 받는 칸 · 찬 칸 테가 칸마다 따로 둥근 윤곽(이웃 칸과 붙은 네모 덩어리가 아니다 — 홈 판과 같은 몫, 검토 지적 6)
        void Inset(VisualElement hit)
        {
            float u = CardWidth / CardStack.WidthCm, g = CardStack.SlotInset(u);
            if (hit.style.position.value != Position.Absolute || hit.style.width.keyword != StyleKeyword.Undefined) return;
            hit.style.left = hit.style.left.value.value + g;
            hit.style.top = hit.style.top.value.value + g;
            hit.style.width = Mathf.Max(0f, hit.style.width.value.value - 2f * g);
            hit.style.height = Mathf.Max(0f, hit.style.height.value.value - 2f * g);
            float r = CardStack.SlotRadius(u);
            hit.style.borderTopLeftRadius = hit.style.borderTopRightRadius = hit.style.borderBottomLeftRadius = hit.style.borderBottomRightRadius = r;
        }

        void Hook(CardPart p)
        {
            if (p.Title == null) return;
            p.Element.pickingMode = PickingMode.Position;
            stage.Tips.Hook(p.Element, p.Title, p.Text, TipSide.Below, reading: true);
        }

        // 카드 그림이 없을 때(짝 없는 카드): 도트 카드 + 기술 줄(빈 칸은 받을 때만 홈 줄) + 지닌물건 칸
        VisualElement DotCard(VisualElement top, int index, int pokemon, PokemonState pk, int hp, VisualElement[] slots)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var def = ctx.Cards.Pokemon[pk.Def];
            var mons = MonArt.Load();
            var card = Add(top, new VisualElement(), "board-party-mon-" + index.ToString(inv), "sn-party-card");
            card.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
            var info = Add(card, new VisualElement(), null, "sn-party-card-info");
            Add(info, Kit.Text(BoardHud.ShortName(stage.T("pokemon." + def.Key)), Kit.F2), "board-party-name-" + index.ToString(inv), "kit-title");
            var stats = Add(info, new VisualElement(), null, "sn-party-line");
            int max = PokemonRules.MaxHp(ctx, pk);
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), def.Initiative.ToString(inv)));
            if (pk.Status != StatusEffect.None && hp > 0)
            {
                var line = Add(card, new VisualElement(), "board-party-status-" + index.ToString(inv), "sn-party-status");
                MonParts.StatusIcons(stage, line, pk, line.name);
            }
            var sig = MonParts.MoveRow(stage, ctx, def.Signature, true);
            sig.name = "board-party-sig-" + index.ToString(inv);
            sig.AddToClassList("sn-party-move");
            top.Add(sig);
            for (int k = 0; k < PokemonState.MoveSlots; k++)
            {
                VisualElement row;
                bool quiet = Held == Hold.Move;   // 집은 동안은 칸이 툴팁을 건다(WireSlot)
                if (pk.Moves[k] >= 0) row = MonParts.MoveRow(stage, ctx, pk.Moves[k], false, null);
                else if (pk.Breach[k] >= 0) row = MonParts.BreachRow(stage, ctx, pk.Breach[k], quiet ? (TipSide?)null : TipSide.Below);
                else if (LegendRules.TacticAt(pk, k) != LegendTactic.None) row = MonParts.TacticRow(stage, ctx, pk, k, quiet ? (TipSide?)null : TipSide.Below, types: false);
                else if (SlotOpen(pokemon, k))
                {
                    row = new VisualElement();
                    row.AddToClassList("sn-pcard-dot-hole");
                }
                else continue;   // 빈 칸은 그리지 않는다(결정 206 Q10)
                row.AddToClassList("sn-party-move");
                top.Add(row);
                slots[k] = row;
            }
            var itemRow = Add(top, new VisualElement(), null, "sn-party-itemrow");
            for (int s = 0; s < (TrainerRules.Pockets(flow.Client.View, ctx, Seat) ? 2 : 1); s++)
                ItemSlot(Add(itemRow, new VisualElement(), (s == 1 ? "board-party-item2-" : "board-party-item-") + index.ToString(inv), "sn-party-item"), index, pokemon, pk, s, false);
            return card;
        }

        // 기술 칸 하나(띠 · 홈 · 도트 카드의 줄): 기술 카드를 집었으면 받는 칸만 빛나고(찬 칸 = 금색 테 — 바꿀 수 있음), 아무것도 집지 않았으면 배운 기술을 눌러 잊게 한다 · 카드 능력의 칸 고르기
        void WireSlot(VisualElement row, CardPart part, int index, int pokemon, PokemonState pk, int slot)
        {
            var ctx = flow.Ctx;
            int old = pk.Moves[slot];
            string name = part != null ? part.Title : old >= 0 ? stage.T(BattleFeed.MoveNameKey(ctx, old)) : null;
            string text = part != null ? part.Text : old >= 0 ? stage.TOrNull(BattleFeed.MoveNameKey(ctx, old) + ".text") : null;
            // 카드 안의 칸(받지 않는 띠 · 보기만 하는 전술 판 포함): 누름이 카드(확대)까지 올라가지 않게(검토 지적 20)
            if (part != null) row.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            if (part != null) Inset(row);
            void Press(ClickEvent e)
            {
                e.StopPropagation();
                PressSlot(index, slot);
            }
            if (Edit && picking == MaintKind.AttachBreach)
            {
                // pokemon_ranger B: 보관한 균열 카드를 붙일 칸(결정 87 R4) — 카드를 고른 뒤 엔진이 받는 칸만 빛난다
                bool ok = SlotOpen(pokemon, slot);
                row.AddToClassList(ok ? "sn-party-ok" : "sn-party-no");
                if (ok) stage.OnTap(row, SoundKeys.Select, Press);
            }
            else if (Edit && picking == MaintKind.PassMove)
            {
                // interviewers R: 넘길 칸(첫 고름) → 받을 칸 — 엔진이 받는 칸만 빛난다
                bool picked = pokemon == pickFrom && slot == pickSlot;
                bool ok = !picked && PassOk(pokemon, slot);
                row.AddToClassList(picked ? "sn-party-picked" : ok ? "sn-party-ok" : "sn-party-no");
                if (picked || ok) stage.OnTap(row, SoundKeys.Select, Press);
            }
            else if (Edit && Held == Hold.Move)
            {
                // 집은 기술 카드를 넣을 수 있는 칸만 빛난다(엔진 검사 그대로 — 배울 수 없는 타입 · 이미 그 기술)
                var why = flow.CanMaintain(SetMove(pokemon, slot, HeldValue));
                row.AddToClassList(why == RejectReason.None ? "sn-party-ok" : "sn-party-no");
                if (why == RejectReason.None)
                {
                    stage.OnTap(row, SoundKeys.Select, Press);
                    Target(row, () => PressSlot(index, slot));
                    var tactic = LegendRules.TacticAt(pk, slot);   // 결정 195: 가르치면 덮일 전설 특수기술
                    if (tactic != LegendTactic.None) stage.Tips.Hook(row, stage.T(MonParts.TacticKey(tactic)), MonParts.TacticTip(stage, tactic), TipSide.Below, reading: true);
                    else if (name != null) stage.Tips.Hook(row, name, text, TipSide.Below, reading: true);
                }
                else stage.Tips.Hook(row, name, stage.T(why == RejectReason.NoChange ? "screen.party.slot.same" : why == RejectReason.Timing ? "screen.party.guide.rejected" : "screen.party.slot.no"), TipSide.Below, reading: true);
            }
            else if (old >= 0)
            {
                bool can = Edit && flow.CanMaintain(SetMove(pokemon, slot, -1)) == RejectReason.None;
                if (can)
                {
                    row.AddToClassList("sn-party-press");
                    stage.OnTap(row, SoundKeys.Select, Press);
                }
                stage.Tips.Hook(row, name, can ? Lines(text, stage.T("screen.party.slot.forget.tip")) : text, TipSide.Below, reading: true);
            }
            else if (part != null && name != null) stage.Tips.Hook(row, name, text, TipSide.Below, reading: true);   // 균열 카드 · 전설 특수기술(보기만 — 줄은 MonParts가 건다)
        }

        // 지닌물건 칸 하나(slot 0 · backpacker R의 둘째 칸 1 — art = 카드의 지닌물건 동그라미, 그림은 카드 쌓기가 그린다): 집은 것을 넣을 수 있으면 빛나고, 지닌 것이 있으면 집는다(끌기도)
        void ItemSlot(VisualElement itemSlot, int index, int pokemon, PokemonState pk, int slot, bool art)
        {
            var ctx = flow.Ctx;
            itemSlot.pickingMode = PickingMode.Position;
            if (art) itemSlot.RegisterCallback<ClickEvent>(e => e.StopPropagation());   // 카드 앞면의 동그라미(우두머리 · 받지 않는 칸 포함): 누름이 카드(확대)까지 올라가지 않게(검토 지적 20)
            if (pk.Alpha && slot == 0)
            {
                // 우두머리 토큰(rulings A17 · T23): 뗄 수 없는 지닌물건 — 이 칸을 차지한다(다른 물건은 지니지 못한다 — 엔진 검사 MaintenanceRules)
                if (!art) MonParts.AlphaToken(stage, itemSlot);
                else
                {
                    itemSlot.userData = stage.T("item.alpha_pokemon");
                    stage.Tips.Hook(itemSlot, stage.T("item.alpha_pokemon"), stage.TOrNull("item.alpha_pokemon.text"), TipSide.Below, reading: true);
                }
                itemSlot.AddToClassList("sn-party-alpha");
                if (Edit && Held == Hold.Item) itemSlot.AddToClassList("sn-party-no");
                return;
            }
            int held = HeldOf(pk, slot);
            string itemKey = held >= 0 && held < ctx.Items.Items.Count ? ctx.Items.Items[held].Key : null;
            if (art) itemSlot.userData = itemKey != null ? stage.T(itemKey) : null;
            else
            {
                if (itemKey != null) ItemIcon(itemSlot, ctx.Items.Items[held]);   // 아이템 그림(유물 포함 — 결정 80 L14 나)
                Add(itemSlot, Kit.Text(itemKey != null ? stage.T(itemKey) : stage.T(slot == 1 ? "screen.party.item.second" : "screen.party.item.none"), Kit.F2), null, itemKey != null ? null : "kit-muted");
            }
            void Press(ClickEvent e)
            {
                e.StopPropagation();
                if (!JustDragged) PressItemSlot(index, slot);
            }
            if (Edit && Held == Hold.Item)
            {
                if (HeldFrom == pokemon && HeldSlot == slot)
                {
                    itemSlot.AddToClassList("sn-party-picked");
                    stage.OnTap(itemSlot, SoundKeys.Select, Press);   // 다시 누르면 내려놓는다
                    Arm(itemSlot, Hold.Item, index, true, slot);
                }
                else
                {
                    var why = flow.CanMaintain(HoldItem(pokemon, HeldValue, slot));
                    itemSlot.AddToClassList(why == RejectReason.None ? "sn-party-ok" : "sn-party-no");
                    if (why == RejectReason.None)
                    {
                        stage.OnTap(itemSlot, SoundKeys.Select, Press);
                        Target(itemSlot, () => PressItemSlot(index, slot));
                    }
                }
            }
            else if (itemKey != null)
            {
                bool can = Edit && picking == null && flow.CanMaintain(HoldItem(pokemon, -1, slot)) == RejectReason.None;
                if (can)
                {
                    itemSlot.AddToClassList("sn-party-press");
                    stage.OnTap(itemSlot, SoundKeys.Select, Press);
                    Arm(itemSlot, Hold.Item, index, true, slot);
                }
                stage.Tips.Hook(itemSlot, stage.T(itemKey), can ? Lines(stage.TOrNull(itemKey + ".text"), stage.T("screen.party.item.take.tip")) : stage.TOrNull(itemKey + ".text"), TipSide.Below, reading: true);
            }
            else if (slot == 1) stage.Tips.Hook(itemSlot, stage.T("screen.party.item.second.kind"), stage.T("screen.party.item.second.tip"), TipSide.Below, reading: true);
            else stage.Tips.Hook(itemSlot, stage.T("screen.party.item.kind"), stage.T("screen.party.item.kind.tip"), TipSide.Below, reading: true);   // 결정 87 V5
        }

        // 기술 카드의 앞면(T30): 그 카드 등급 배지 + 마우스 오버 = 「○○의 뒷면 · 등급」
        void PoolBack(VisualElement row, int n)
        {
            int card = n < backs.Count ? backs[n] : -1;
            if (card < 0) return;
            var def = flow.Ctx.Cards.Pokemon[card];
            // 등급 = 그 카드를 뽑은 덱(약함 · 보통 · 강함 · 전설 — 약함 덱의 스타팅 포켓몬 카드도 「약함」)
            var deck = n < backDecks.Count ? backDecks[n] : DeckId.Starter;
            string grade = deck == DeckId.Weak || deck == DeckId.Moderate || deck == DeckId.Strong || deck == DeckId.Legendary ? deck.ToString() : def.Tier.ToString();
            var made = MonParts.GradeBadge(stage, grade.ToLowerInvariant());   // 등급 색(결정 192)
            if (made == null) return;
            string tier = made.text;
            var badge = Add(row, made, row.name + "-back", "sn-party-pooltier");
            stage.Tips.Hook(badge, stage.T("screen.party.pool.back").Replace("{mon}", stage.T("pokemon." + def.Key)), stage.T("screen.party.pool.back.tip").Replace("{tier}", tier), TipSide.Above, reading: true);
        }

        // ── 카드 능력(M6 「트레이너 카드 화면」 2): 정비 판의 능력 줄 — 카드가 주는 정비 명령(rulings A35·A38·A39) · 아르세우스의 축복(울트라 버스트, A54 ⑤) ──

        // 카드 키 → 그 카드가 주는 정비 명령과 능력 칸(1 파랑 · 2 빨강)
        static readonly (string Card, MaintKind Kind, int Slot)[] CardMaint =
        {
            ("black_belt", MaintKind.Afflict, 2),        // 화상
            ("fairy_tale_girl", MaintKind.Afflict, 2),   // 졸음
            ("fisherman", MaintKind.CatchSize, 2),       // 여행 점수 2 → 월척 한 단계
            ("chef", MaintKind.Convert, 1),              // 같은 단계 규토리 바꾸기
            ("collector", MaintKind.Adopt, 2),           // 스탬프 보상 풀의 포켓몬을 파티로
            ("interviewers", MaintKind.PassMove, 2),     // 배운 기술 맞바꾸기
        };

        string CardKey(GameState view) => CardAsk.CardKey(flow.Ctx, CardAsk.CardOf(view, Seat));

        int AbilitySlot(GameState view, MaintKind kind)
        {
            string key = CardKey(view);
            foreach (var e in CardMaint) if (e.Card == key && e.Kind == kind) return e.Slot;
            return 0;
        }

        /// <summary>이 판의 능력 줄에 서는 정비(내 판만 — 그 좌석 카드의 풀린 능력 · 울트라 버스트 판의 축복). 지금 쓸 수 없어도 선다(흐리게).</summary>
        public List<MaintKind> CardMaints()
        {
            var list = new List<MaintKind>();
            var view = flow.Client != null ? flow.Client.View : null;
            if (!Edit || view == null) return list;
            string key = CardKey(view);
            foreach (var e in CardMaint)
                if (e.Card == key && TrainerRules.Has(view, flow.Ctx, Seat, key, e.Slot == 1 ? AbilityColor.Blue : AbilityColor.Red)) list.Add(e.Kind);
            if (view.Ub != null && BreachRules.StoredCount(view, Seat) > 0) list.Add(MaintKind.AttachBreach);   // 보관한 균열 카드가 있을 때만(pokemon_ranger B — 결정 87 R4)
            if (view.Ub != null) list.Add(MaintKind.UseBlessing);
            return list;
        }

        /// <summary>그 정비의 엔진이 받는 명령들(보기 순서 — 칸을 켜고 끄는 것은 정비 검사 그대로).</summary>
        public List<Maintenance> Candidates(MaintKind kind)
        {
            // 같은 보기(Version)·좌석이면 한 번만 센다 — interviewers R PassMove는 후보 270개를 엔진 정비 검사로 거르고, 칸마다 다시 세면 다시 그릴 때 약 4,900번이었다(M6 화면 교차 검토 #7)
            int version = flow.Client != null ? flow.Client.Version : -1;
            if (version != candidatesVersion || Seat != candidatesSeat)
            {
                candidates.Clear();
                candidatesVersion = version;
                candidatesSeat = Seat;
            }
            if (!candidates.TryGetValue(kind, out var list)) candidates[kind] = list = CountCandidates(kind);
            return list;
        }

        readonly Dictionary<MaintKind, List<Maintenance>> candidates = new Dictionary<MaintKind, List<Maintenance>>();
        int candidatesVersion = -1, candidatesSeat = -1;

        /// <summary>후보를 처음부터 센 횟수(확인용 — UiShots가 다시 그릴 때 늘지 않는지 본다).</summary>
        public int CandidateCounts { get; private set; }

        List<Maintenance> CountCandidates(MaintKind kind)
        {
            CandidateCounts++;
            var all = new List<Maintenance>();
            var view = flow.Client != null ? flow.Client.View : null;
            if (!Edit || view == null) return all;
            var st = view.Seats[Seat];
            switch (kind)
            {
                case MaintKind.Afflict:
                {
                    var status = CardKey(view) == "fairy_tale_girl" ? StatusEffect.Drowsy : StatusEffect.Burned;
                    foreach (int p in st.Party) if (p >= 0) all.Add(new Maintenance { Kind = kind, Pokemon = p, Value = (int)status });
                    break;
                }
                case MaintKind.CatchSize:
                    foreach (int p in st.Party) if (p >= 0) all.Add(new Maintenance { Kind = kind, Pokemon = p, Value = view.Pokemon[p].CatchSize + 1 });
                    break;
                case MaintKind.Convert:
                    for (int from = 0; from < SeatState.ApricornColors; from++)
                        for (int to = 0; to < SeatState.ApricornColors; to++) all.Add(new Maintenance { Kind = kind, Slot = from, Value = to });
                    break;
                case MaintKind.Adopt:
                    for (int i = 0; i + 1 < st.StampCards.Count; i += 2) all.Add(new Maintenance { Kind = kind, Value = st.StampCards[i + 1] });
                    break;
                case MaintKind.PassMove:
                    foreach (int a in st.Party)
                        foreach (int b in st.Party)
                            if (a >= 0 && b >= 0 && a != b)
                                for (int sa = 0; sa < PokemonState.MoveSlots; sa++)
                                    for (int sb = 0; sb < PokemonState.MoveSlots; sb++) all.Add(new Maintenance { Kind = kind, Pokemon = a, Slot = sa, Value = b, Values = new List<int> { sb } });
                    break;
                case MaintKind.AttachBreach:
                    foreach (int card in Stored(view))
                        foreach (int p in st.Party)
                            for (int k = 0; p >= 0 && k < PokemonState.MoveSlots; k++) all.Add(new Maintenance { Kind = kind, Pokemon = p, Slot = k, Value = card });
                    break;
                case MaintKind.UseBlessing:
                    all.Add(UbBlessing.SkipCommand());
                    for (int seat = 0; seat < view.Seats.Length; seat++) all.Add(UbBlessing.ReviveCommand(seat));
                    break;
            }
            return all.FindAll(m => flow.CanMaintain(m) == RejectReason.None);
        }

        // interviewers R: 넘길 칸으로 고를 수 있나(첫 고름 전) · 받을 칸인가(첫 고름 뒤)
        bool PassOk(int pokemon, int slot) => pickFrom < 0
            ? Candidates(MaintKind.PassMove).Exists(c => c.Pokemon == pokemon && c.Slot == slot)
            : Candidates(MaintKind.PassMove).Exists(c => c.Pokemon == pickFrom && c.Slot == pickSlot && c.Value == pokemon && c.Values[0] == slot);

        // 이 좌석이 보관한 균열 카드(카드 표 색인 — 같은 카드는 한 번만, 보관한 순서)
        List<int> Stored(GameState view)
        {
            var cards = new List<int>();
            var h = view.Ub != null ? view.Ub.BreachStored : null;
            for (int i = 0; h != null && i + 1 < h.Count; i += 2)
                if (h[i] == Seat && !cards.Contains(h[i + 1])) cards.Add(h[i + 1]);
            return cards;
        }

        string BreachName(int card) => stage.T(flow.Ctx.Decks.CardIds[card]);

        string AbilityHead(MaintKind kind)
        {
            var view = flow.Client.View;
            if (kind == MaintKind.AttachBreach) return stage.T("screen.party.breach.name");
            return kind == MaintKind.UseBlessing ? stage.T("screen.party.blessing.name") : CardAsk.Head(stage, flow.Ctx, CardAsk.CardOf(view, Seat), AbilitySlot(view, kind));
        }

        void PaintAbilities(GameState view)
        {
            abil.Clear();
            var kinds = CardMaints();
            abil.style.display = kinds.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (kinds.Count == 0) return;
            Add(abil, Kit.Text(stage.T("screen.party.abil"), Kit.F2), null, "kit-muted");
            int card = CardAsk.CardOf(view, Seat);
            foreach (var kind in kinds)
            {
                var k = kind;
                bool blessing = kind == MaintKind.UseBlessing;
                int slot = AbilitySlot(view, kind);
                bool breach = kind == MaintKind.AttachBreach;
                string label = blessing ? stage.T("screen.party.blessing").Replace("{n}", view.Ub.Blessings.ToString(CultureInfo.InvariantCulture))
                    : breach ? stage.T("screen.party.breach").Replace("{n}", BreachRules.StoredCount(view, Seat).ToString(CultureInfo.InvariantCulture))
                    : stage.T(CardAsk.AbilityKey(flow.Ctx, card, slot));
                var b = Add(abil, Kit.Button(label, KitButtonKind.Normal, () => PressAbility(k)), "board-party-abil-" + kind.ToString().ToLowerInvariant(), null);
                bool can = Candidates(kind).Count > 0;
                if (!can) b.AddToClassList(UiStage.OffClass);   // 켜 둔 채 흐리게(툴팁이 뜨게) — 누름은 PressAbility가 거른다
                if (picking == kind) b.AddToClassList("sn-party-picked");
                string text = blessing ? stage.T("screen.party.blessing.tip") : breach ? stage.T("screen.party.breach.tip") : CardAsk.Text(stage, flow.Ctx, card, slot);
                stage.Tips.Hook(b, AbilityHead(kind), can ? text : Lines(text, stage.T("screen.party.guide.rejected")), TipSide.Above, reading: true);
            }
        }

        /// <summary>능력 줄의 단추(그 정비)를 눌렀다: 고르기를 연다(포켓몬 · 기술 칸 · 값 단추). 다시 누르면 거둔다. 받는 명령이 없으면 false.</summary>
        public bool PressAbility(MaintKind kind)
        {
            if (!Edit || !CardMaints().Contains(kind) || Candidates(kind).Count == 0) return false;
            bool same = picking == kind;
            asking = null;
            Drop();
            if (!same)
            {
                picking = kind;
                FillOptions();
            }
            Refresh();
            return true;
        }

        // 값 단추 줄(Convert = 낼 색 → 받을 색 · Adopt = 포켓몬 · UseBlessing = 건너뛰기·부활)
        void FillOptions()
        {
            options.Clear();
            if (picking == MaintKind.Convert)
            {
                foreach (var m in Candidates(MaintKind.Convert))
                    if (pickSlot < 0 ? !options.Exists(o => o.Slot == m.Slot) : m.Slot == pickSlot) options.Add(m);
            }
            else if (picking == MaintKind.Adopt || picking == MaintKind.UseBlessing) options.AddRange(Candidates(picking.Value));
            else if (picking == MaintKind.AttachBreach)
            {
                // 보관한 카드가 한 가지면 바로 칸 고르기로, 여럿이면 카드부터(값 단추)
                var cards = Stored(flow.Client.View).FindAll(c => Candidates(MaintKind.AttachBreach).Exists(m => m.Value == c));
                if (pickFrom < 0 && cards.Count == 1) pickFrom = cards[0];
                if (pickFrom < 0) foreach (int c in cards) options.Add(new Maintenance { Kind = MaintKind.AttachBreach, Value = c });
            }
        }

        string PickPrompt()
        {
            switch (picking)
            {
                case MaintKind.PassMove: return pickFrom < 0 ? "screen.party.abil.from" : "screen.party.abil.to";
                case MaintKind.Convert: return pickSlot < 0 ? "screen.party.abil.give" : "screen.party.abil.take";
                case MaintKind.Adopt: return "screen.party.abil.adopt";
                case MaintKind.UseBlessing: return "screen.party.abil.blessing";
                case MaintKind.AttachBreach: return pickFrom < 0 ? "screen.party.abil.breach" : "screen.party.abil.breach.slot";
                default: return "screen.party.abil.mon";
            }
        }

        string Apricorn(int color) => stage.T("screen.apricorn." + ((ApricornColor)color).ToString().ToLowerInvariant());

        /// <summary>값 단추 하나의 이름.</summary>
        public string OptionLabel(Maintenance m)
        {
            switch (m.Kind)
            {
                case MaintKind.Convert: return Apricorn(pickSlot < 0 ? m.Slot : m.Value);
                case MaintKind.Adopt: return BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[m.Value].Key));
                case MaintKind.AttachBreach: return BreachName(m.Value);
                default: return m.Value == UbBlessing.SkipTurn ? stage.T("screen.party.blessing.skip") : stage.T("screen.party.blessing.revive").Replace("{name}", flow.SeatName(m.Value));
            }
        }

        /// <summary>값 단추 줄의 n번째(0부터): Convert 첫 고름이면 받을 색으로 넘기고, 축복은 한 번 더 묻고, 나머지는 바로 낸다.</summary>
        public bool PressOption(int n)
        {
            if (picking == null || n < 0 || n >= options.Count) return false;
            var m = options[n];
            if (picking == MaintKind.Convert && pickSlot < 0)
            {
                pickSlot = m.Slot;
                FillOptions();
                Refresh();
                return true;
            }
            if (picking == MaintKind.AttachBreach)
            {
                pickFrom = m.Value;
                FillOptions();
                Refresh();
                return true;
            }
            if (picking == MaintKind.UseBlessing)
                return Ask(m, KoJosa.Fix(m.Value == UbBlessing.SkipTurn ? stage.T("screen.party.ask.skip") : stage.T("screen.party.ask.revive").Replace("{name}", flow.SeatName(m.Value))), stage.T("screen.card.use"));
            return Submit(m);
        }

        // 포켓몬을 고르는 능력(Afflict · CatchSize): 그 포켓몬이면 한 번 더 묻는다
        bool AskTarget(int pokemon)
        {
            var m = Candidates(picking.Value).Find(c => c.Pokemon == pokemon);
            if (m == null) return false;
            var view = flow.Client.View;
            string mon = BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[pokemon].Def].Key));
            string text = m.Kind == MaintKind.Afflict
                ? stage.T("screen.party.ask.afflict").Replace("{mon}", mon).Replace("{status}", stage.T(MonParts.StatusKey((StatusEffect)m.Value)))
                : stage.T("screen.party.ask.catch").Replace("{mon}", mon);
            return Ask(m, KoJosa.Fix(text), stage.T("screen.card.use"));
        }

        // pokemon_ranger B: 고른 보관 카드를 그 칸에 — 붙이면 다시 보관하지 못하니 한 번 더 묻는다(그 칸의 가르친 기술은 사라진다)
        bool AttachAt(int pokemon, int slot)
        {
            var m = pickFrom < 0 ? null : Candidates(MaintKind.AttachBreach).Find(c => c.Value == pickFrom && c.Pokemon == pokemon && c.Slot == slot);
            if (m == null) return false;
            var ctx = flow.Ctx;
            var pk = flow.Client.View.Pokemon[pokemon];
            string mon = BoardHud.ShortName(stage.T("pokemon." + ctx.Cards.Pokemon[pk.Def].Key));
            int old = pk.Moves[slot];
            string text = old >= 0
                ? stage.T("screen.party.ask.breach.replace").Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, old)))
                : stage.T("screen.party.ask.breach");
            return Ask(m, KoJosa.Fix(text.Replace("{card}", BreachName(pickFrom)).Replace("{mon}", mon)), stage.T("screen.breach.attach"));
        }

        // interviewers R: 넘길 칸 고르기(다시 누르면 내려놓는다) → 받을 칸이면 바로 낸다(되돌릴 수 있는 맞바꾸기라 묻지 않는다)
        bool PassAt(int pokemon, int slot)
        {
            if (pokemon == pickFrom && slot == pickSlot)
            {
                pickFrom = pickSlot = -1;
                Refresh();
                return true;
            }
            if (!PassOk(pokemon, slot)) return false;
            if (pickFrom < 0)
            {
                pickFrom = pokemon;
                pickSlot = slot;
                Refresh();
                return true;
            }
            var m = Candidates(MaintKind.PassMove).Find(c => c.Pokemon == pickFrom && c.Slot == pickSlot && c.Value == pokemon && c.Values[0] == slot);
            return m != null && Submit(m);
        }

        // 아이템 그림(WorkUi.IconKey — 지닌물건 · 유물): 그림이 없는 아이템은 자리를 잡지 않는다
        static void ItemIcon(VisualElement parent, ItemDef item)
        {
            string key = WorkUi.IconKey(item);
            if (key != null) Icon(parent, key);
        }

        static void Icon(VisualElement parent, string key)
        {
            var icon = Add(parent, new VisualElement(), null, "sn-icon");
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
