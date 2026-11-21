using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;
using static Sinnoh.Client.WorkUi;

namespace Sinnoh.Client
{
    /// <summary>
    /// 사당 판(S31 공물 · S32 사당 이벤트 카드와 4인 이상 나눔 · S33 천상의 균열 — 결정 75 A1 가: 보드를 덮지 않는 오른쪽 기둥 판, 목업 B안).
    /// <list type="bullet">
    /// <item>공물(S31): 보드 HUD의 [공물]로 연다. 바칠 규토리 한 색(단계 = 규토리 등급)과 「여행 점수 7 더 바치기」(3장 뽑아 2장)를 고르고 [바치기] — 3막 시작의 사당의 심장 회복도 같은 답이다.</item>
    /// <item>카드(S32): 뽑은 카드가 내려와 뒤집히고(31행), 고를 것이 있으면 같은 판에서 받는다 — 3장 중 고르기 · 받을 규토리 · 스튜 · 교환할 포켓몬과 야생 카드 · 배울 기술과 칸 · 함께 받을 트레이너(4인 이상).
    /// 나눠 받은 좌석 · 다음 턴의 만찬 스튜처럼 내 선택이 열리면 저절로 열린다.</item>
    /// <item>천상의 균열(S33): 모든 좌석에 열린다. 내 추종자(비공개)를 고르고 [확정] → 좌석 줄이 「선택 중 / 완료」 → 다 고르면 한꺼번에 뒤집히고 신마다 막대(34행) →
    /// 동률이면 뽑은 좌석이 같은 판에서 이어 고른다(결정 75 A4 가) → 신 강림은 보드에서.</item>
    /// </list>
    /// 규칙을 다시 세지 않는다 — 고를 수 있는 것은 엔진의 합법 답 그대로이고 누르면 그 답을 낸다. 할 일이 없고(내 선택 · 남은 연출 · 남의 추종자 선택) 공물도 못 바치면 스스로 닫힌다.
    /// 사당 카드 그림은 Gen에 없어 글 카드다 [가정 — plan §5 #115]. 요소 이름: <c>board-shrine</c>·<c>board-shrine-panel</c>·<c>board-shrine-title</c>·<c>board-shrine-close</c>·
    /// <c>board-shrine-go</c>·<c>board-shrine-skip</c>·<c>board-shrine-color-&lt;색&gt;</c>·<c>board-shrine-extra</c>·<c>board-shrine-card-&lt;i&gt;</c>·<c>board-shrine-opt-&lt;i&gt;</c>·
    /// <c>board-shrine-god-&lt;신&gt;</c>·<c>board-shrine-nogod</c>·<c>board-shrine-seat-&lt;n&gt;</c>·<c>board-shrine-bar-&lt;신&gt;</c>·<c>board-shrine-prev</c>·<c>board-shrine-next</c>.
    /// </summary>
    public sealed class ShrineScreen : IWorkScreen, IWatchScreen
    {
        /// <summary>판이 지금 받는 것.</summary>
        public enum Mode { None, Offer, Fx, Cards, Apricorns, Stews, Exchange, ExchangeCard, Move, Teach, Share, Follower, Tie, Wait }

        /// <summary>긴 목록(교환할 야생 카드 · 배울 기술)의 한 쪽 줄 수.</summary>
        public const int PageSize = 15;

        /// <summary>좌석 줄에 얼굴 상자를 그리는 좌석 수의 상한(넘으면 좌석 색 점 — 판이 아래 판을 덮지 않게).</summary>
        public const int FaceSeats = 4;

        /// <summary>연출이 옮기는 거리(720 벌 px): 바친 규토리가 오르는 높이 · 카드가 내려오는 높이 · 꼬리표가 뜨는 높이.</summary>
        public const float FlyPx = 28f, DropPx = 16f;

        readonly GameFlow flow;
        readonly bool manual;
        UiStage stage;
        VisualElement panel;
        long painted = long.MinValue;

        // 고른 것
        int color = -1;
        bool extra;
        int selected = -1, mon = -1, page;

        // 긴 야생 카드 목록의 찾기(M9 다듬기 P22) — 찾는 글 · 타입, 거른 뒤의 줄(opts 색인), 목록만 다시 그리는 자리
        string find = "";
        PokemonType? findType;
        readonly List<int> shownOpts = new List<int>();
        readonly Dictionary<int, VisualElement> typeChips = new Dictionary<int, VisualElement>();
        VisualElement listBox, pager, typeAll;
        Button goButton;
        Mode listMode;
        MonTip monTip;
        readonly List<int> multi = new List<int>();
        readonly List<ChoiceAnswer> opts = new List<ChoiceAnswer>();
        int optsFor = -1;

        // 지난 카드(연출이 끝난 뒤에도 판에 남긴다)
        int lastCard = -1, lastTier;
        FollowerTally lastTally;

        // 연출이 움직이는 요소
        readonly List<CardEls> cardEls = new List<CardEls>();
        readonly List<SeatEls> seatEls = new List<SeatEls>();
        readonly Dictionary<int, BarEls> barEls = new Dictionary<int, BarEls>();
        VisualElement offered, pillar;
        readonly List<VisualElement> giftEls = new List<VisualElement>();
        Label giftText;

        /// <summary>지금 그리는 축복의 갈래 · 보이는 그림 수 · 여행 점수 숫자 글(32행의 고유 착지 — P35, 확인용).</summary>
        public BlessGift GiftKind { get; private set; }
        public int GiftsShown { get; private set; }
        public string GiftText { get; private set; }

        /// <summary>축복 그림이 움직이는 단위(720 화면 px — 그림 한 칸).</summary>
        public const float GiftPx = 24f;

        static readonly string[] GiftStews = { "stew_alluring.png", "stew_infused.png", "stew_refreshing.png" };
        static readonly string[] GiftBalls = { "greater_ball.png", "ultimate_ball.png", "keystone_ball.png" };

        // 축복 그림 하나의 Gen 키(기술 카드는 그림이 번들에 없다 — 기술 줄 꼴 칸 + 타입 그림, null)
        static string GiftArt(BlessGift gift, int tier, int i)
        {
            switch (gift)
            {
                case BlessGift.Fate: return GenAssets.FateArt + "back.png";
                case BlessGift.Apricorn: return EncounterScreen.ApricornKey((ApricornColor)(i % 3));   // 고를 색은 아직 모른다 — 빨강 · 파랑 · 노랑
                case BlessGift.Exchange: return GenAssets.ItemIcons + "poke_ball.png";
                case BlessGift.Banquet: return GenAssets.ItemIcons + GiftStews[i % GiftStews.Length];
                case BlessGift.Treasure: return GenAssets.ItemIcons + GiftBalls[Mathf.Clamp(tier, 1, 3) - 1];
                default: return null;
            }
        }
        Label jpRoll, blessBadge;
        int jpFrom;

        sealed class CardEls { public VisualElement Box, Front, Back, Glow; }
        sealed class SeatEls { public VisualElement Row, Flip, Icon; public Label Badge; public int Seat; }
        sealed class BarEls { public VisualElement Row, Fill; public Label Count; }

        /// <param name="manual">보드 HUD의 [공물]로 열었다 — 공물을 바칠 수 있는 동안 남는다(저절로 열린 판은 할 일이 끝나면 닫힌다).</param>
        /// <param name="reach">psychic B 「염력」으로 바칠 떨어진 사당 칸(−1 = 내가 선 칸) — 그 칸에 선 것과 같은 판(색 · 여행 점수 7, T33).</param>
        /// <param name="watch">보는 좌석(결정 120 ① · 플레이 시험 T9 — 남의 공물(31행) · 사당 카드 고르기 · 축복(32행)을 내 판과 같은 꼴로 보기만, −1 = 내 판). 보기 판은 단추가 없고
        /// (고르는 중이면 「○○이 고르는 중」 · 방 판이면 그 좌석이 가리키는 카드에 좌석 색 테두리) 그 좌석의 판 단계 · 사당 카드 고르기가 끝나면 닫힌다.</param>
        public ShrineScreen(GameFlow flow, bool manual = false, int reach = -1, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.manual = manual;
            this.reach = reach;
            this.watch = watch;
        }

        readonly int watch;

        public int Watched => watch;

        int Seat => watch >= 0 ? watch : flow.Client.Seat;

        // 보기 판: 그 좌석의 열린 사당 카드 고르기(공개 — 없으면 null)
        PendingChoice WatchChoice()
        {
            var view = flow.Client != null ? flow.Client.View : null;
            var c = view != null ? PeekRules.Open(view, watch, PeekSpot.Shrine) : null;
            return c != null && c.Options.Count > 0 && !c.Options.Contains(Engine.Unknown) ? c : null;
        }

        /// <summary>보기 판: 그 좌석의 사당 카드 고르기가 열려 있거나, 그 좌석의 공물 · 축복 단계가 아직 끝나지 않고 남았나.</summary>
        public bool Live
        {
            get
            {
                if (watch < 0) return false;
                var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
                if (GameFlow.PartyPlaying(seq, watch)) return false;   // 그 좌석의 사당 보상 · 정비 보기(380행)가 재생 중이면 판 보기에 자리를 내준다(결정 120)
                if (WatchChoice() != null) return true;
                if (seq == null) return false;
                foreach (var entry in seq.Queued)
                    foreach (var s in entry.Steps)
                        if ((s.Row == ShrineRows.Offer || s.Row == ShrineRows.Blessing) && Draws(s) && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return true;
                return false;
            }
        }

        /// <summary>보기 판에서 그 좌석이 지금 가리키는 카드(0부터, 없으면 −1 — 방 판의 「남의 고르기 보기」) — 확인용.</summary>
        public int Pointed { get; private set; } = -1;

        readonly int reach;

        /// <summary>염력으로 연 떨어진 사당 칸(−1 = 내 칸).</summary>
        public int Reach => reach;

        /// <summary>그 칸의 사당에 바치는 psychic B 답인가(안쪽 답이 공물 — <see cref="TrainerRules.ReachAnswer"/>).</summary>
        public static bool IsReachOffer(ChoiceAnswer a, int cell) =>
            a != null && a.Kind == AnswerKind.UseAbility && a.Target == cell && a.Arg == (int)AnswerKind.Offer && a.Values.Count >= 2;

        public string Name => "board-shrine";

        /// <summary>지금 그린 꼴(확인용).</summary>
        public Mode Shown { get; private set; }

        /// <summary>지금 판에 보이는 카드(카드 표 색인 — 3장 고르기면 여러 장).</summary>
        public IReadOnlyList<int> Cards => cards;
        readonly List<int> cards = new List<int>();

        /// <summary>고를 수 있는 답(하나를 고르는 꼴 — 카드 · 포켓몬 · 야생 카드 · 기술 · 좌석 · 신). 묶음을 고르는 꼴(규토리 · 스튜)은 비어 있다.</summary>
        public IReadOnlyList<ChoiceAnswer> Options => opts;

        /// <summary>찾기 칸의 글자 수 상한.</summary>
        public const int FindMax = 12;

        /// <summary>찾기 줄이 선 목록의 한 쪽 줄 수(찾기 칸 · 타입 칩 줄만큼 줄인다 — 1080 벌 · 영어에서도 판이 아래 판을 덮지 않게) · 지금 목록의 한 쪽 줄 수.</summary>
        public const int FindPageSize = 6;
        public int PageRows => Filtering ? FindPageSize : PageSize;

        /// <summary>
        /// 야생 카드 목록의 찾기 줄이 떠 있나(교환할 야생 카드가 한 쪽 <see cref="PageSize"/>을 넘을 때 — M9 다듬기 P22) · 찾는 글 · 타입(null = 전체) ·
        /// 거른 뒤 보이는 답(<see cref="Options"/> 색인, 쪽 넘김 앞) · 지금 쪽(0부터) · 쪽 수.
        /// </summary>
        public bool Filtering { get; private set; }
        public string Search => find;
        public PokemonType? TypeFilter => findType;
        public IReadOnlyList<int> ShownOptions => shownOpts;
        public int Page => page;
        public int Pages { get; private set; } = 1;

        /// <summary>야생 카드 목록의 마우스 오버 카드에 지금 떠 있는 포켓몬(포켓몬 표 색인 — 없으면 −1).</summary>
        public int MonShown => monTip != null ? monTip.Shown : -1;

        /// <summary>공물로 고른 색(없으면 −1) · 여행 점수 7을 더 바치나.</summary>
        public int OfferColor => color;
        public bool Extra => extra;

        /// <summary>입력을 받나: 보드에 있고 화면 대기열이 비어 클릭 막기 시간이 지났다.</summary>
        public bool Open => watch < 0 && stage != null && flow.BoardOpen;

        /// <summary>내 좌석에 열린 사당·균열 선택(없으면 null).</summary>
        public static PendingChoice ChoiceOf(SeatClient client)
        {
            var choice = client != null ? client.FirstChoice() : null;
            return choice != null && GameFlow.ShrineWaits(choice) ? choice : null;
        }

        /// <summary>닫아도 되나: 내 선택도, 그릴 연출도, 남의 추종자 선택도 없다.</summary>
        public bool CanClose
        {
            get
            {
                var client = flow.Client;
                if (watch >= 0 || client == null || client.View == null) return true;
                var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
                return ChoiceOf(client) == null && (seq == null || seq.IsEmpty) && !FractureRules.Choosing(client.View);
            }
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            panel = Add(page, Kit.Panel(stage.Skin), "board-shrine-panel", "sn-shrine-panel");
            panel.EnableInClassList("sn-shrine-watch", watch >= 0);   // 보기 판: 머리의 얼굴 상자 몫만큼 넓게(#707)
            monTip = new MonTip(stage, flow.Ctx);
            painted = long.MinValue;
            Refresh();
            return page;
        }

        public void Close()
        {
            monTip?.Detach();
            monTip = null;
            flow.Typing = false;
            listBox = pager = typeAll = null;
            goButton = null;
            stage = null;
            panel = null;
            cardEls.Clear();
            seatEls.Clear();
            barEls.Clear();
        }

        // 판 행(31·32행 · 34행 집계) 가운데 내 것: 바친 좌석 · 받은 좌석 · 나눠 준 좌석, 집계는 모든 좌석
        public bool Draws(FxStep step)
        {
            if (watch < 0) return Mine(flow, step);
            if (step == null || !ShrineRows.InPanel(step.Row)) return false;
            foreach (var e in step.Events)
                if (e.Seat == watch || (e is ShrineBlessing b && b.From == watch)) return true;
            return false;
        }

        /// <summary>그 단계(사당 판 행)가 내 화면에 그려지나: 공물 = 바친 좌석 · 축복 = 받은 좌석과 나눠 준 좌석 · 집계 = 모든 좌석. 남의 것은 기록 줄만 남는다.</summary>
        public static bool Mine(GameFlow flow, FxStep step)
        {
            if (step == null || flow.Client == null || !ShrineRows.InPanel(step.Row)) return false;
            if (step.Row == ShrineRows.Tally) return true;
            int me = flow.Client.Seat;
            foreach (var e in step.Events)
                if (e.Seat == me || (e is ShrineBlessing b && b.From == me)) return true;
            return false;
        }

        // ── 그리기 ──

        /// <summary>보기·입력이 바뀌었다: 다시 그린다. 할 일이 없으면 닫는다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var choice = watch >= 0 ? WatchChoice() : ChoiceOf(client);
            bool busy = seq != null && !seq.IsEmpty;
            if (watch < 0 && choice == null && !busy && !FractureRules.Choosing(view) && Offers().Count == 0)
            {
                flow.CloseShrine();
                return;
            }
            bool open = Open;
            var mode = ModeOf(view, choice, open, busy);
            long key = ((long)client.Version << 8) ^ (open ? 1 : 0) ^ ((long)mode << 1) ^ ((long)(choice != null ? choice.Id : -1) << 40) ^ ((long)StepKey(seq) << 24);
            if (key == painted)
            {
                Animate(seq);
                return;
            }
            painted = key;
            Shown = mode;
            Paint(view, choice, mode, seq);
            Animate(seq);
        }

        // 그리는 판 행이 바뀌면 다시 그린다(공물 → 축복 → 집계)
        static int StepKey(FxSequencer seq)
        {
            if (seq == null) return 0;
            return (ShrineRows.Pending(seq, ShrineRows.Offer) != null ? 1 : 0) | (ShrineRows.Pending(seq, ShrineRows.Blessing) != null ? 2 : 0)
                | (ShrineRows.Pending(seq, ShrineRows.Tally) != null ? 4 : 0) | (ShrineRows.Pending(seq, ShrineRows.Descend) != null ? 8 : 0) | (ShrineRows.Pending(seq, ShrineRows.Legend) != null ? 16 : 0);
        }

        Mode ModeOf(GameState view, PendingChoice choice, bool open, bool busy)
        {
            if (watch >= 0) return choice != null && choice.Sub == ChoiceSub.ShrinePick && !ShrineRows.PanelPending(flow.Fx != null ? flow.Fx.Sequencer : null) ? Mode.Cards : Mode.Fx;
            if (!open && busy) return Mode.Fx;
            if (choice == null) return FractureRules.Choosing(view) ? Mode.Wait : busy ? Mode.Fx : Mode.Offer;
            if (choice.Kind == ChoiceKind.Simultaneous) return Mode.Follower;
            switch (choice.Sub)
            {
                case ChoiceSub.ShrinePick: return Mode.Cards;
                case ChoiceSub.ShrineApricorns: return Mode.Apricorns;
                case ChoiceSub.ShrineStews: return Mode.Stews;
                case ChoiceSub.ShrineExchange: return Mode.Exchange;
                case ChoiceSub.ShrineExchangeCard: return Mode.ExchangeCard;
                case ChoiceSub.ShrineMove: return Mode.Move;
                case ChoiceSub.TeachMove: return Mode.Teach;
                case ChoiceSub.SharedBlessing: return Mode.Share;
                case ChoiceSub.FractureTie: return Mode.Tie;
                default: return Mode.Wait;
            }
        }

        /// <summary>지금 바칠 수 있는 공물 답(내 맵 행동의 합법 답 — 없으면 비어 있다). 염력으로 연 판이면 그 칸 psychic 답의 안쪽 답(낼 때 다시 감싼다 — <see cref="PressOffer"/>).</summary>
        public List<ChoiceAnswer> Offers()
        {
            var r = new List<ChoiceAnswer>();
            var client = flow.Client;
            var choice = manual && client != null ? client.Choice(ChoiceKind.MapAction) : null;
            if (choice == null || client.FirstChoice() != choice) return r;
            foreach (var a in client.Legal(choice))
            {
                if (reach < 0 && a.Kind == AnswerKind.Offer) r.Add(a);
                else if (reach >= 0 && IsReachOffer(a, reach)) r.Add(TrainerRules.ReachInner(a));
            }
            return r;
        }

        void Paint(GameState view, PendingChoice choice, Mode mode, FxSequencer seq)
        {
            var client = flow.Client;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            int me = Seat;
            stage.Tips.Hide();
            monTip.Hide();
            flow.Typing = false;   // 찾기 칸이 판과 함께 사라진다(포커스를 잃는 알림이 오지 않을 수 있다)
            panel.Clear();
            listBox = pager = typeAll = null;
            goButton = null;
            typeChips.Clear();
            shownOpts.Clear();
            Filtering = false;
            cardEls.Clear();
            seatEls.Clear();
            barEls.Clear();
            offered = pillar = null;
            jpRoll = blessBadge = giftText = null;
            giftEls.Clear();
            bool open = Open;

            // 고른 것은 그 선택에만 남는다
            int id = choice != null ? choice.Id : -1;
            if (id != optsFor)
            {
                optsFor = id;
                selected = mon = -1;
                page = 0;
                find = "";
                findType = null;
                multi.Clear();
            }
            opts.Clear();
            var legal = choice != null && watch < 0 ? client.Legal(choice) : new List<ChoiceAnswer>();

            // 머리 줄: 사당 이름(공물) 또는 「사당 이벤트」
            var head = Add(panel, new VisualElement(), null, "sn-shrine-head");
            var left = Add(head, new VisualElement(), null, "sn-hud-line");
            int shrine = reach >= 0 && view.Shrine == null ? ShrineRules.ShrineAt(view, reach) : ShrineHere(view, me);
            string title = watch >= 0 ? KoJosa.Fix(stage.T("screen.shrine.title.seat").Replace("{name}", flow.SeatName(watch)))
                : mode == Mode.Offer && shrine >= 0 ? stage.T(ctx.Fixtures.Fixtures[shrine].Key) : stage.T("screen.shrine.title");
            var titleText = Add(left, Kit.Text(title, Kit.FTitle), "board-shrine-title", "kit-title");
            if (watch >= 0) FaceBox.Owner(flow, watch, titleText);
            if (mode == Mode.Offer)
            {
                var once = Add(left, Kit.Badge(stage.T("screen.village.once"), KitBadgeKind.Plain, Kit.F2), "board-shrine-once", null);
                stage.Tips.Hook(once, null, stage.T("screen.shrine.once.tip"), TipSide.Left, reading: true, clear: panel);
                Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Normal, () => flow.CloseShrine()), "board-shrine-close", null);
            }
            if (watch >= 0) Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Normal, () => flow.CloseWatch()), "board-shrine-close", null);

            // 카드
            Remember(view, choice, seq);
            cards.Clear();
            var offerStep = ShrineRows.Pending(seq, ShrineRows.Offer);
            var drawn = ShrineRows.First<ShrineCardsDrawn>(offerStep);
            if (mode == Mode.Offer) { }
            else if (mode == Mode.Fx && drawn != null && drawn.CardKeys != null) cards.AddRange(drawn.CardKeys);
            else if (mode == Mode.Cards && watch >= 0) cards.AddRange(choice.Options);   // 보기 판: 공개 후보 그대로(고를 수 없다)
            else if (mode == Mode.Cards)
            {
                foreach (var a in legal) if (a.Kind == AnswerKind.Pick) { cards.Add(a.Target); opts.Add(a); }
            }
            else if (lastCard >= 0) cards.Add(lastCard);

            if (mode == Mode.Fx && offerStep != null) OfferStrip(ShrineRows.First<ShrineOffered>(offerStep));
            if (mode == Mode.Cards) Section("screen.shrine.pick", null, out _);
            for (int i = 0; i < cards.Count; i++) CardBox(cards[i], i, mode == Mode.Cards && watch < 0, open);
            if (mode == Mode.Fx && cards.Count == 1 && ShrineRows.Pending(seq, ShrineRows.Blessing) != null)
            {
                // 축복(32행)의 꼬리표: 카드 아래 한 줄(연출 동안만 보인다)
                blessBadge = Add(panel, Kit.Badge("", KitBadgeKind.Gold, Kit.F2), "board-shrine-bless", "sn-shrine-bless");
                FxUi.Fade(blessBadge, 0f);
                // 축복마다의 고유 착지(P35): 꼬리표 아래 그림 줄 — 운명 카드 · 규토리 · 볼 · 기술 카드 · 스튜 그릇(셋까지) + 여행 점수 숫자
                var gifts = Add(panel, new VisualElement(), "board-shrine-gifts", "sn-shrine-gifts");
                for (int i = 0; i < BlessBeats.MaxGifts; i++)
                {
                    var g = Add(gifts, new VisualElement(), "board-shrine-gift-" + i.ToString(inv), "sn-shrine-gift");
                    g.pickingMode = PickingMode.Ignore;
                    giftEls.Add(g);
                }
                giftText = Add(gifts, Kit.Badge("", KitBadgeKind.Ok, Kit.F2), "board-shrine-gift-text", "sn-shrine-gift-text");
            }

            switch (mode)
            {
                case Mode.Offer: PaintOffer(view, me, open); break;
                case Mode.Apricorns: PaintApricorns(choice, open); break;
                case Mode.Stews: PaintStews(choice, legal, open); break;
                case Mode.Exchange:
                case Mode.ExchangeCard:
                case Mode.Move:
                    PaintList(view, choice, legal, mode, open);
                    break;
                case Mode.Teach: PaintTeach(view, choice, legal, open); break;
                case Mode.Share: PaintShare(legal, open); break;
                case Mode.Follower: PaintGods(view, choice, legal, open, "screen.shrine.follower", true); PaintSeats(view); break;
                case Mode.Tie: PaintBars(view, null); PaintGods(view, choice, legal, open, "screen.shrine.tie", false); break;
                case Mode.Wait: if (FractureRules.Choosing(view)) { PaintMine(view, me); PaintSeats(view); } break;
                case Mode.Fx:
                    if (lastTally != null || ShrineRows.Pending(seq, ShrineRows.Tally) != null) { PaintSeats(view); PaintBars(view, lastTally); }
                    break;
            }

            // 발 줄: [하지 않기] · [확정](보기 판은 「○○이 고르는 중」)
            if (watch >= 0)
            {
                if (mode == Mode.Cards)
                {
                    var foot = Add(panel, new VisualElement(), null, "sn-shrine-foot");
                    Add(foot, Kit.Text(KoJosa.Fix(stage.T("screen.encounter.watch").Replace("{name}", flow.SeatName(watch))), Kit.F2), "board-shrine-watch", "kit-muted");
                }
            }
            else if (choice != null && mode != Mode.Fx && mode != Mode.Wait || mode == Mode.Offer)
            {
                var foot = Add(panel, new VisualElement(), null, "sn-shrine-foot");
                var decline = mode == Mode.Follower ? null : legal.Find(a => a.Kind == AnswerKind.Decline);
                if (decline != null)
                {
                    var skip = Add(foot, Kit.Button(stage.T(mode == Mode.Teach || mode == Mode.Move ? "screen.shrine.skip.learn" : "screen.shrine.skip.exchange"), KitButtonKind.Normal, () => PressDecline()), "board-shrine-skip", null);
                    skip.SetEnabled(open);
                }
                var go = Add(foot, Kit.Button(stage.T(mode == Mode.Offer ? "screen.shrine.offer" : "screen.common.confirm"), KitButtonKind.Main, () => { if (Shown == Mode.Offer) PressOffer(); else Confirm(); }), "board-shrine-go", null);
                go.SetEnabled(open && (mode == Mode.Offer ? OfferAnswer() != null : Answer() != null));
                if (mode != Mode.Offer) goButton = go;
                if (mode == Mode.Offer && open) stage.Tips.Hook(go, null, stage.T(flow.HeartOffer ? "screen.shrine.offer.heart.tip" : "screen.shrine.offer.tip"), TipSide.Left, reading: true, clear: panel);
            }
            else if (mode == Mode.Wait)
            {
                var foot = Add(panel, new VisualElement(), null, "sn-shrine-foot");
                Add(foot, Kit.Text(stage.T("screen.shrine.waiting"), Kit.F2), "board-shrine-waiting", "kit-muted");
            }
        }

        // 지금 처리하는 카드·단계를 기억한다(선택 → 보기 → 연출 단계 순)
        void Remember(GameState view, PendingChoice choice, FxSequencer seq)
        {
            if (view.Shrine != null)
            {
                if (view.Shrine.Tier > 0) lastTier = view.Shrine.Tier;
                if (view.Shrine.Card >= 0) lastCard = view.Shrine.Card;
            }
            if (choice != null && choice.Source.Kind == SourceKind.ShrineEvent && choice.Source.Id >= 0) lastCard = choice.Source.Id;
            if (choice != null && choice.Sub == ChoiceSub.ShrineStews && choice.Subject == 1) lastTier = 0;   // 나눠 받은 만찬(다음 턴): 단계 대신 고를 수만
            if (seq == null) return;
            foreach (var entry in seq.Queued)
                foreach (var s in entry.Steps)
                {
                    if (ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs && s.Row != ShrineRows.Tally) continue;
                    foreach (var e in s.Events)
                    {
                        if (e is ShrineCardsDrawn d && d.Tier > 0) lastTier = d.Tier;
                        else if (e is ShrineBlessing b) { lastCard = b.CardKey; lastTier = b.Tier; }
                        else if (e is LegendEvent || e is GodDescended) { if (e.Source.Kind == SourceKind.ShrineEvent) lastCard = e.Source.Id; }
                        else if (e is FollowerTally t) { lastTally = t; if (e.Source.Kind == SourceKind.ShrineEvent) lastCard = e.Source.Id; }
                    }
                }
        }

        static int ShrineHere(GameState view, int me)
        {
            int seat = view.Shrine != null ? view.Shrine.Seat : me;
            return seat >= 0 && seat < view.Seats.Length ? ShrineRules.ShrineAt(view, view.Seats[seat].Cell) : -1;
        }

        VisualElement Section(string titleKey, string badge, out VisualElement head, KitBadgeKind kind = KitBadgeKind.Plain)
        {
            var box = Add(panel, new VisualElement(), null, "sn-shrine-sec");
            head = Add(box, new VisualElement(), null, "sn-hud-line");
            Add(head, Kit.Text(stage.T(titleKey), Kit.F2), null, "kit-title");
            if (badge != null) Add(head, Kit.Badge(badge, kind, Kit.F2), null, null);
            return box;
        }

        // 누를 칸: 고름 = 금색, 못 누름 = 흐림
        VisualElement Chip(VisualElement parent, string name, bool picked, bool off, Action tap)
        {
            var chip = Add(parent, new VisualElement(), name, "sn-shrine-chip");
            if (picked) chip.AddToClassList("sn-shrine-chip-picked");
            if (off) chip.AddToClassList("sn-shrine-chip-off");
            if (tap != null && !off) stage.OnTap(chip, SoundKeys.Select, e => tap(), () => Open);
            return chip;
        }

        /// <summary>카드의 짧은 효과 글(단계가 정해졌으면 그 단계의 값).</summary>
        public string Effect(int card, int tier)
        {
            var ctx = flow.Ctx;
            string key = ctx.Decks.CardIds[card];
            if (ShrineRules.LegendOf(ctx, card, out int species, out _, out var c, out int count))
                return stage.T("screen.shrine.effect.legend").Replace("{color}", stage.T(ColorKey(c))).Replace("{n}", count.ToString(CultureInfo.InvariantCulture)).Replace("{mon}", stage.T("pokemon." + ctx.Cards.Pokemon[species].Key));
            string id = key.StartsWith("shrine_event.", StringComparison.Ordinal) ? key.Substring("shrine_event.".Length) : key;
            if (key == ShrineRules.CelestialFracture) return stage.T("screen.shrine.effect." + id);
            return stage.T("screen.shrine.effect." + id + "." + Mathf.Clamp(tier, 1, 3).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>그 사당 카드의 그림 키(원작 카드의 그림 부분 — 결정 80 L12 나): 사당의 축복은 일곱 장이 같은 그림, 전설 이벤트·천상의 균열은 카드마다.</summary>
        public static string ArtKey(string cardKey, bool blessing)
        {
            if (blessing) return GenAssets.ShrineBlessingArt;
            const string head = "shrine_event.";
            return cardKey != null && cardKey.StartsWith(head, StringComparison.Ordinal) ? GenAssets.ShrineArt + cardKey.Substring(head.Length) + ".png" : null;
        }

        // 카드 한 장: 원작 그림(왼쪽 작은 칸) + 이름 + 종류 꼬리표 · (단계) 효과 한 줄. 카드 글 전체는 마우스 오버(기둥이 좁다 — 결정 80 L12 나)
        void CardBox(int card, int i, bool pickable, bool open)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            string key = ctx.Decks.CardIds[card];
            bool blessing = ShrineRules.IsBlessing(ctx, card), fracture = key == ShrineRules.CelestialFracture;
            var els = new CardEls();
            els.Box = Add(panel, new VisualElement(), "board-shrine-card-" + (i + 1).ToString(inv), "sn-shrine-card");
            if (pickable && selected == i) els.Box.AddToClassList("sn-shrine-card-picked");
            els.Glow = Add(els.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-shrine-card-glow");
            FxUi.Fade(els.Glow, 0f);
            els.Front = Add(els.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-shrine-card-front");
            var body = els.Front;
            var art = GenAssets.Load<Texture2D>(ArtKey(key, blessing));
            if (art != null)
            {
                els.Front.AddToClassList("sn-shrine-card-front-art");
                Add(els.Front, new Image { image = art, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore }, "board-shrine-card-art-" + (i + 1).ToString(inv), "sn-shrine-card-art");
                body = Add(els.Front, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-shrine-card-body");
            }
            var top = Add(body, new VisualElement(), null, "sn-hud-line");
            Add(top, Kit.Text(stage.T(key), Kit.F2), null, "kit-title");
            Add(top, Kit.Badge(stage.T(blessing ? "screen.shrine.kind.blessing" : fracture ? "screen.shrine.kind.fracture" : "screen.shrine.kind.legend"), blessing ? KitBadgeKind.Ok : fracture ? KitBadgeKind.Gold : KitBadgeKind.Warn, Kit.F2), null, null);
            var line = Add(body, new VisualElement(), null, "sn-shrine-card-line");
            if (blessing && lastTier > 0) Add(line, Kit.Badge(stage.T("screen.shrine.tier").Replace("{n}", lastTier.ToString(inv)), KitBadgeKind.Gold, Kit.F2), null, "sn-shrine-tier");
            Add(line, Kit.Wrapped(Effect(card, lastTier), Kit.F2), null, "sn-shrine-card-text");
            els.Back = Add(els.Box, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-shrine-card-back");
            FxUi.Fade(els.Back, 0f);
            string text1 = stage.TOrNull(key + ".text.1"), text2 = stage.TOrNull(key + ".text.2");
            string text = text1 != null && text2 != null ? text1 + "\n" + text2 : text2 ?? text1;   // 카드 글 전체(축복 = 4인 이상 줄 + 효과)
            stage.Tips.Hook(els.Box, stage.T(key), text, TipSide.Left, reading: true, clear: panel);
            if (pickable)
            {
                int index = i;
                stage.OnTap(els.Box, SoundKeys.Select, e => PickOption(index), () => Open);
                // 남의 고르기 보기(P09): 사당 카드 고르기(공개 — 뽑은 카드는 모두가 본다)에서 마우스를 올린 카드를 방에 알린다
                els.Box.RegisterCallback<PointerEnterEvent>(e => HoverCard(index));
                els.Box.RegisterCallback<PointerLeaveEvent>(e => HoverCard(-1));
            }
            cardEls.Add(els);
        }

        // 공물 연출(31행)의 줄: 바친 규토리 · 단계 빛기둥 · (7점을 더 냈으면) 줄어드는 여행 점수
        void OfferStrip(ShrineOffered so)
        {
            if (so == null) return;
            var inv = CultureInfo.InvariantCulture;
            var sec = Section("screen.shrine.offered", stage.T("screen.shrine.tier").Replace("{n}", so.Tier.ToString(inv)), out _, KitBadgeKind.Gold);
            var strip = Add(sec, new VisualElement(), "board-shrine-offered", "sn-shrine-strip");
            int c = -1, jp = -1;
            if (so.Changes != null)
                foreach (var ch in so.Changes)
                {
                    if (ch.Res == ResKind.Apricorn) c = ch.Key;
                    else if (ch.Res == ResKind.Jp) jp = ch.Before;
                }
            var slot = Add(strip, new VisualElement(), null, "sn-shrine-slot");
            offered = c >= 0 ? Apricorn(slot, (ApricornColor)c) : Add(slot, new VisualElement(), null, "sn-icon");
            var col = Add(strip, new VisualElement(), null, "sn-shrine-pillar-box");
            pillar = Add(col, new VisualElement(), "board-shrine-pillar", "sn-shrine-pillar");
            pillar.AddToClassList("sn-shrine-pillar-" + Mathf.Clamp(so.Tier, 1, 3).ToString(inv));
            FxUi.Fade(pillar, 0f);
            if (so.Extra && jp >= 0)
            {
                jpFrom = jp;
                jpRoll = Add(strip, Kit.Text("", Kit.F2), "board-shrine-jp", null);
            }
        }

        // S31 공물: 단계 줄 셋(규토리 등급) + 여행 점수 7
        void PaintOffer(GameState view, int me, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var offers = Offers();
            var st = view.Seats[me];
            if (color >= 0 && !offers.Exists(a => a.Arg == color)) color = -1;
            bool canExtra = offers.Exists(a => a.Values.Count > 0);
            if (!canExtra) extra = false;
            if (flow.HeartOffer)
            {
                var heart = Add(panel, Kit.Badge(stage.T("screen.shrine.heart"), KitBadgeKind.Gold, Kit.F2), "board-shrine-heart", "sn-shrine-note");
                stage.Tips.Hook(heart, null, stage.T("screen.shrine.heart.tip"), TipSide.Left, reading: true, clear: panel);
            }
            var give = Section("screen.shrine.give", stage.T("screen.shrine.give.count"), out _);
            for (int tier = 1; tier <= 3; tier++)
            {
                var line = Add(give, new VisualElement(), null, "sn-shrine-line");
                var badge = Add(line, Kit.Badge(stage.T("screen.shrine.tier").Replace("{n}", tier.ToString(inv)), KitBadgeKind.Plain, Kit.F2), null, "sn-shrine-tier");
                stage.Tips.Hook(badge, null, stage.T("screen.shrine.tier.tip"), TipSide.Left, reading: true, clear: panel);
                for (int c = 0; c < SeatState.ApricornColors; c++)
                {
                    if (MapActions.ApricornGrade((ApricornColor)c) != tier) continue;
                    int pick = c;
                    bool can = offers.Exists(a => a.Arg == c);
                    var chip = Chip(line, "board-shrine-color-" + ((ApricornColor)c).ToString().ToLowerInvariant(), c == color, !can, () => PickColor(pick));
                    Apricorn(chip, (ApricornColor)c);
                    Add(chip, Kit.Text(ShownApricorns(flow, me, c).ToString(inv), Kit.F2), null, null);
                    stage.Tips.Hook(chip, stage.T(ColorKey((ApricornColor)c)), null, TipSide.Left, clear: panel);
                }
            }
            var jp = Section("screen.shrine.jp", stage.T("screen.shrine.jp.have").Replace("{n}", st.Jp.ToString(inv)), out _);
            var use = Chip(jp, "board-shrine-extra", extra, !canExtra, () => SetExtra(!extra));
            Add(use, new VisualElement(), null, extra ? "sn-shrine-check-on" : "sn-shrine-check");
            Add(use, Kit.Text(stage.T("screen.shrine.jp.extra").Replace("{n}", ShrineRules.ExtraCost.ToString(inv)), Kit.F2), null, null);
            stage.Tips.Hook(use, null, stage.T(canExtra ? "screen.shrine.jp.extra.tip" : "screen.shrine.jp.short.tip"), TipSide.Left, reading: true, clear: panel);
        }

        // 뜻밖의 선물: 아무 색 Count개(같은 색 여러 개 됨)
        void PaintApricorns(PendingChoice choice, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var sec = Section("screen.shrine.apricorns", multi.Count.ToString(inv) + "/" + choice.Count.ToString(inv), out var head, multi.Count == choice.Count ? KitBadgeKind.Ok : KitBadgeKind.Warn);
            if (multi.Count > 0)
            {
                var reset = Add(head, Kit.Button(stage.T("screen.shrine.reset"), KitButtonKind.Normal, () => ResetPicks()), "board-shrine-reset", "sn-shrine-reset");
                reset.SetEnabled(open);
            }
            var line = Add(sec, new VisualElement(), null, "sn-shrine-wrap");
            for (int c = 0; c < SeatState.ApricornColors; c++)
            {
                int pick = c, n = multi.FindAll(x => x == c).Count;
                var chip = Chip(line, "board-shrine-color-" + ((ApricornColor)c).ToString().ToLowerInvariant(), n > 0, false, () => AddUnit(pick));
                Apricorn(chip, (ApricornColor)c);
                if (n > 0) Add(chip, Kit.Text(n.ToString(inv), Kit.F2), null, null);
                stage.Tips.Hook(chip, stage.T(ColorKey((ApricornColor)c)), null, TipSide.Left, clear: panel);
            }
        }

        // 화려한 만찬: 서로 다른 스튜 Count개
        void PaintStews(PendingChoice choice, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var sec = Section("screen.shrine.stews", multi.Count.ToString(inv) + "/" + choice.Count.ToString(inv), out _, multi.Count == choice.Count ? KitBadgeKind.Ok : KitBadgeKind.Warn);
            var items = new List<int>();
            foreach (var a in legal)
                foreach (int v in a.Values)
                    if (!items.Contains(v)) items.Add(v);
            items.Sort();
            for (int i = 0; i < items.Count; i++)
            {
                int item = items[i];
                var d = ctx.Items.Items[item];
                var chip = Chip(sec, "board-shrine-opt-" + (i + 1).ToString(inv), multi.Contains(item), false, () => ToggleUnit(item));
                Icon(chip, IconKey(d), "sn-icon");
                Add(chip, Kit.Text(stage.T(d.Key), Kit.F2), null, null);
                stage.Tips.Hook(chip, stage.T(d.Key), stage.TOrNull(d.Key + ".text"), TipSide.Left, reading: true, clear: panel);
            }
        }

        // 한 줄에 하나씩 고르는 목록(교환할 포켓몬 · 야생 카드 · 배울 기술) — 길면 쪽을 넘긴다
        void PaintList(GameState view, PendingChoice choice, List<ChoiceAnswer> legal, Mode mode, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            foreach (var a in legal) if (a.Kind == AnswerKind.Pick) opts.Add(a);
            string title = mode == Mode.Exchange ? "screen.shrine.exchange" : mode == Mode.ExchangeCard ? "screen.shrine.exchange_card" : "screen.shrine.move";
            var sec = Section(title, null, out var head);
            pager = Add(head, new VisualElement(), null, "sn-shrine-pager");
            if (mode == Mode.ExchangeCard && choice.Subject >= 0 && choice.Subject < view.Pokemon.Count)
                Add(sec, Kit.Text(stage.T("screen.shrine.exchange_from").Replace("{mon}", stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[choice.Subject].Def].Key)), Kit.F2), "board-shrine-from", "kit-muted");
            // 긴 야생 카드 목록: 이름 찾기 + 타입 거르기(도감의 거르기 꼴 — M9 다듬기 P22). 목록에 있는 타입만 칩으로
            Filtering = mode == Mode.ExchangeCard && opts.Count > PageSize;
            if (Filtering)
            {
                var bar = Add(sec, new VisualElement(), null, "sn-shrine-find");
                Add(bar, Kit.Text(stage.T("screen.dex.name"), Kit.F2), null, "sn-shrine-find-label");
                var field = RoomUi.Field(bar, "board-shrine-find", find, FindMax);
                field.SetEnabled(open);
                field.RegisterValueChangedCallback(e => Find(e.newValue));
                field.RegisterCallback<FocusInEvent>(e => flow.Typing = true);
                field.RegisterCallback<FocusOutEvent>(e => flow.Typing = false);
                var types = Add(sec, new VisualElement(), "board-shrine-types", "sn-shrine-wrap");
                typeAll = Chip(types, "board-shrine-type-all", false, false, () => PickType(null));
                Add(typeAll, Kit.Text(stage.T("screen.dex.type.all"), Kit.F2), null, null);
                typeAll.AddToClassList("sn-shrine-type");
                foreach (PokemonType t in Enum.GetValues(typeof(PokemonType)))
                {
                    if (t == PokemonType.Typeless || !opts.Exists(a => DexBook.Matches(ctx, a.Target, t, null, null))) continue;
                    var type = t;
                    var chip = Chip(types, "board-shrine-type-" + t.ToString().ToLowerInvariant(), false, false, () => PickType(type));
                    chip.AddToClassList("sn-shrine-type");
                    MonParts.TypeIcon(chip, t);
                    stage.Tips.Hook(chip, null, stage.T("ui.types." + t), TipSide.Left, clear: panel);
                    typeChips[(int)t] = chip;
                }
            }
            else
            {
                find = "";
                findType = null;
            }
            listBox = Add(sec, new VisualElement(), "board-shrine-list", "sn-shrine-wrap");
            listMode = mode;
            FillList();
        }

        // 목록 줄 · 쪽 넘김만 다시 그린다(찾기 칸은 그대로 둔다 — 글을 치는 동안 칸이 다시 만들어지면 포커스 · 한글 조합이 끊긴다)
        void FillList()
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var mode = listMode;
            bool open = Open;
            stage.Tips.Hide();
            monTip.Hide();
            shownOpts.Clear();
            for (int i = 0; i < opts.Count; i++)
                if (!Filtering || DexBook.Matches(ctx, opts[i].Target, findType, find, stage.T)) shownOpts.Add(i);
            if (selected >= 0 && !shownOpts.Contains(selected)) selected = -1;   // 걸러져 안 보이는 카드를 고른 채 [확정]하지 않게
            Pages = Math.Max(1, (shownOpts.Count + PageRows - 1) / PageRows);
            page = Mathf.Clamp(page, 0, Pages - 1);
            pager.Clear();
            if (Pages > 1)
            {
                Add(pager, Kit.Badge((page + 1).ToString(inv) + "/" + Pages.ToString(inv), KitBadgeKind.Plain, Kit.F2), "board-shrine-page", null);
                var prev = Add(pager, Kit.Button(stage.T("screen.shrine.prev"), KitButtonKind.Normal, () => Turn(-1)), "board-shrine-prev", "sn-shrine-reset");
                var next = Add(pager, Kit.Button(stage.T("screen.shrine.next"), KitButtonKind.Normal, () => Turn(1)), "board-shrine-next", null);
                prev.SetEnabled(page > 0);
                next.SetEnabled(page < Pages - 1);
            }
            if (typeAll != null) typeAll.EnableInClassList("sn-shrine-chip-picked", !findType.HasValue);
            foreach (var kv in typeChips) kv.Value.EnableInClassList("sn-shrine-chip-picked", findType.HasValue && (int)findType.Value == kv.Key);
            if (goButton != null) goButton.SetEnabled(open && Answer() != null);
            listBox.Clear();
            if (shownOpts.Count == 0) Add(listBox, Kit.Text(stage.T("screen.dex.empty"), Kit.F2), "board-shrine-none", "kit-muted");
            var list = listBox;
            for (int k = page * PageRows; k < Math.Min(shownOpts.Count, (page + 1) * PageRows); k++)
            {
                int i = shownOpts[k];
                int index = i;
                var a = opts[i];
                var chip = Chip(list, "board-shrine-opt-" + (i + 1).ToString(inv), i == selected, false, () => PickOption(index));
                if (mode == Mode.Move)
                {
                    string name = BattleFeed.MoveNameKey(ctx, a.Target);
                    Add(chip, Kit.Text(stage.T(name), Kit.F2), null, null);
                    stage.Tips.Hook(chip, stage.T(name), stage.TOrNull(name + ".text"), TipSide.Left, reading: true, clear: panel);
                }
                else
                {
                    int def = mode == Mode.Exchange ? view.Pokemon[a.Target].Def : a.Target;
                    Add(chip, Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[def].Key), Kit.F2), null, null);
                    if (mode == Mode.ExchangeCard)
                    {
                        Add(chip, Kit.Badge(stage.T(a.Arg == 1 ? "screen.shrine.pile.discard" : "screen.shrine.pile.deck"), KitBadgeKind.Plain, Kit.F2), null, null);
                        monTip.Hook(chip, def, TipSide.Left, panel);   // 야생 카드의 모습 · 스펙(결정 116)
                    }
                }
            }
        }

        /// <summary>야생 카드 목록을 이름으로 찾는다(빈 글 = 전체 — 화면 이름이나 영문 이름의 한 부분). 찾기 줄이 없으면 false.</summary>
        public bool Find(string name)
        {
            if (stage == null || !Filtering || listBox == null) return false;
            find = name ?? "";
            page = 0;
            FillList();
            return true;
        }

        /// <summary>야생 카드 목록을 타입으로 거른다(null = 전체 — 같은 타입을 다시 누르면 푼다).</summary>
        public bool PickType(PokemonType? type)
        {
            if (stage == null || !Filtering || listBox == null) return false;
            findType = type.HasValue && findType == type ? null : type;
            page = 0;
            FillList();
            return true;
        }

        /// <summary>확인용(캡처 · 시험): 목록의 그 답(<see cref="Options"/> 색인)에 마우스를 올린 것처럼 야생 카드의 마우스 오버 카드를 띄운다(−1 = 걷는다). 그 줄이 지금 쪽에 없으면 false.</summary>
        public bool HoverMon(int index)
        {
            if (stage == null || monTip == null) return false;
            monTip.Hide();
            if (index < 0) return true;
            var chip = Shown == Mode.ExchangeCard && listBox != null && index < opts.Count ? listBox.Q("board-shrine-opt-" + (index + 1).ToString(CultureInfo.InvariantCulture)) : null;
            if (chip == null) return false;
            monTip.Show(chip, opts[index].Target, TipSide.Left, panel);
            return true;
        }

        // 고대의 지혜의 가르치기: 배울 포켓몬 → 바꿀 기술 칸
        void PaintTeach(GameState view, PendingChoice choice, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            foreach (var a in legal) if (a.Kind == AnswerKind.Pick) opts.Add(a);
            string move = stage.T(BattleFeed.MoveNameKey(ctx, choice.Subject));
            var who = Section("screen.shrine.teach", move, out _, KitBadgeKind.Gold);
            var mons = Add(who, new VisualElement(), null, "sn-shrine-wrap");
            var seen = new List<int>();
            foreach (var a in opts) if (!seen.Contains(a.Target)) seen.Add(a.Target);
            if (mon < 0 && seen.Count == 1) mon = seen[0];
            for (int i = 0; i < seen.Count; i++)
            {
                int p = seen[i];
                var chip = Chip(mons, "board-shrine-mon-" + (i + 1).ToString(inv), p == mon, false, () => PickMon(p));
                Add(chip, Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[p].Def].Key), Kit.F2), null, null);
            }
            if (mon < 0) return;
            var slots = Section("screen.shrine.slot", null, out _);
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Target != mon) continue;
                int index = i, old = view.Pokemon[mon].Moves[opts[i].Arg];
                var chip = Chip(slots, "board-shrine-opt-" + (i + 1).ToString(inv), i == selected, false, () => PickOption(index));
                Add(chip, Kit.Text(old >= 0 ? stage.T(BattleFeed.MoveNameKey(ctx, old)) : stage.T("screen.shrine.slot.empty"), Kit.F2), null, null);
                if (old >= 0) Add(chip, Kit.Text(Arrow, Kit.F2), null, "kit-muted");
                if (old >= 0) Add(chip, Kit.Text(move, Kit.F2), null, null);
            }
        }

        // 4인 이상: 함께 받을 트레이너 1명
        void PaintShare(List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            foreach (var a in legal) if (a.Kind == AnswerKind.Pick) opts.Add(a);
            var sec = Section("screen.shrine.share", stage.T("screen.shrine.share.count"), out var head);
            stage.Tips.Hook(head, null, stage.T("screen.shrine.share.tip"), TipSide.Left, reading: true, clear: panel);
            for (int i = 0; i < opts.Count; i++)
            {
                int index = i, seat = opts[i].Target;
                var chip = Chip(sec, "board-shrine-seat-" + (seat + 1).ToString(inv), i == selected, false, () => PickOption(index));
                var face = Add(chip, FaceBox.Make(), "board-shrine-seat-" + (seat + 1).ToString(inv) + "-face", null);
                FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
                Add(chip, Kit.Text(flow.SeatName(seat), Kit.F2), null, "sn-hud-seat-name");
            }
        }

        /// <summary>신오의 신의 아이콘 키(Gen <c>Cards/Tokens/celestial_icons/</c>).</summary>
        public static string GodIcon(SinnohGod god) => GenAssets.GodIcons + god.ToString().ToLowerInvariant() + ".png";

        static readonly SinnohGod[] Gods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        // 좌석 배지가 칠하는 추종자(고르는 신 셋 + 아르세우스의 추종자 — undivided_acolyte B)
        static readonly SinnohGod[] ShownGods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina, SinnohGod.Arceus };

        // 신 셋의 칸(추종자 고르기 · 동률 고르기): 합법 답에 없는 신(게임에서 빠졌다 · 동률이 아니다)은 흐리게
        void PaintGods(GameState view, PendingChoice choice, List<ChoiceAnswer> legal, bool open, string titleKey, bool secret)
        {
            foreach (var a in legal) opts.Add(a);
            if (selected < 0 && secret)
            {
                // 처음 보이는 고름 = 지금 추종자(기본 답) — 처음이면 안 고름
                selected = opts.FindIndex(a => a.SameAs(choice.Default));
            }
            var sec = Section(titleKey, secret ? stage.T("screen.shrine.secret") : null, out var head, KitBadgeKind.Silver);
            if (secret) stage.Tips.Hook(head, null, stage.T("screen.shrine.follower.tip"), TipSide.Left, reading: true, clear: panel);
            // 아르세우스의 추종자(undivided_acolyte B — 「for the rest of the game」): 신이 빠진 것이 아니라 바꿀 수 없다 — 그대로 남는다(감사 G3-3)
            bool arceus = secret && view.Seats[choice.Seat].Follower == SinnohGod.Arceus;
            var row = Add(sec, new VisualElement(), null, "sn-shrine-gods");
            foreach (var god in Gods)
            {
                int index = opts.FindIndex(a => a.Kind == AnswerKind.Pick && a.Target == (int)god);
                var tile = Chip(row, "board-shrine-god-" + god.ToString().ToLowerInvariant(), index >= 0 && index == selected, index < 0, () => PickOption(index));
                tile.AddToClassList("sn-shrine-god");
                Icon(tile, GodIcon(god), "sn-shrine-god-icon");
                Add(tile, Kit.Text(stage.T(BoardLog.GodKey((int)god)), Kit.F2), null, null);
                if (index < 0 && secret) stage.Tips.Hook(tile, null, stage.T(arceus ? "screen.shrine.follower.arceus.keep.tip" : "screen.shrine.god.gone.tip"), TipSide.Left, reading: true, clear: panel);
            }
            int none = opts.FindIndex(a => a.Kind == AnswerKind.Decline);
            if (none >= 0)
            {
                var chip = Chip(sec, "board-shrine-nogod", none == selected, false, () => PickOption(none));
                Add(chip, Kit.Text(stage.T(arceus ? "screen.shrine.follower.arceus.keep" : "screen.shrine.follower.none"), Kit.F2), null, null);
                if (arceus) stage.Tips.Hook(chip, null, stage.T("screen.shrine.follower.arceus.keep.tip"), TipSide.Left, reading: true, clear: panel);
            }
        }

        // 내가 낸 추종자(남을 기다리는 동안 — 내 것만 보인다)
        void PaintMine(GameState view, int me)
        {
            PendingChoice mine = null;
            foreach (var c in view.Pending)
                if (c.Seat == me && c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower && c.Answered) mine = c;
            var sec = Section("screen.shrine.follower", stage.T("screen.shrine.secret"), out _, KitBadgeKind.Silver);
            var row = Add(sec, new VisualElement(), null, "sn-shrine-gods");
            foreach (var god in Gods)
            {
                bool picked = mine != null && mine.Answer.Kind == AnswerKind.Pick && mine.Answer.Target == (int)god;
                var tile = Chip(row, "board-shrine-god-" + god.ToString().ToLowerInvariant(), picked, !picked, null);
                tile.AddToClassList("sn-shrine-god");
                Icon(tile, GodIcon(god), "sn-shrine-god-icon");
                Add(tile, Kit.Text(stage.T(BoardLog.GodKey((int)god)), Kit.F2), null, null);
            }
        }

        // 좌석 줄(34행): 고르는 동안 「선택 중 / 완료」, 공개(340)에서 한꺼번에 뒤집혀 고른 신으로
        void PaintSeats(GameState view)
        {
            var inv = CultureInfo.InvariantCulture;
            int done = 0;
            for (int s = 0; s < view.Seats.Length; s++) if (FollowerDone(view, s)) done++;
            var sec = Section("screen.shrine.seats", done.ToString(inv) + "/" + view.Seats.Length.ToString(inv), out _);
            for (int s = 0; s < view.Seats.Length; s++)
            {
                var els = new SeatEls { Seat = s };
                els.Row = Add(sec, new VisualElement(), "board-shrine-seat-" + (s + 1).ToString(inv), "sn-shrine-seat");
                if (view.Seats.Length <= FaceSeats)
                {
                    var face = Add(els.Row, FaceBox.Make(), "board-shrine-seat-" + (s + 1).ToString(inv) + "-face", null);
                    FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
                }
                else
                {
                    // 다섯 이상: 얼굴 상자 대신 좌석 색 점 — 줄을 낮춰 판이 아래 판을 덮지 않게(1080 벌의 얼굴 상자는 60px)
                    var dot = Add(els.Row, new VisualElement(), "board-shrine-seat-" + (s + 1).ToString(inv) + "-dot", "sn-shrine-seat-dot");
                    dot.style.backgroundColor = (Color)flow.SeatColor(s);
                }
                Add(els.Row, Kit.Text(flow.SeatName(s), Kit.F2), null, "sn-hud-seat-name");
                els.Flip = Add(els.Row, new VisualElement(), null, "sn-shrine-seat-flip");
                els.Icon = Add(els.Flip, new VisualElement(), null, "sn-shrine-seat-icon");
                els.Badge = Add(els.Flip, Kit.Badge("", KitBadgeKind.Plain, Kit.F2), "board-shrine-seat-" + (s + 1).ToString(inv) + "-state", null);
                seatEls.Add(els);
            }
        }

        static bool FollowerDone(GameState view, int seat)
        {
            foreach (var c in view.Pending)
                if (c.Seat == seat && c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower) return c.Answered;
            foreach (var c in view.Held)
                if (c.Seat == seat && c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower) return c.Answered;
            return true;
        }

        // 신마다 추종자 막대(공개 뒤 — 집계 이벤트가 있으면 그 값, 없으면 보기의 값)
        void PaintBars(GameState view, FollowerTally tally)
        {
            var inv = CultureInfo.InvariantCulture;
            var counts = tally != null && tally.Counts != null ? tally.Counts : FractureRules.Counts(view);
            var sec = Section("screen.shrine.tally", null, out _);
            foreach (var god in Gods)
            {
                var els = new BarEls();
                els.Row = Add(sec, new VisualElement(), "board-shrine-bar-" + god.ToString().ToLowerInvariant(), "sn-shrine-bar-row");
                Icon(els.Row, GodIcon(god), "sn-icon");
                Add(els.Row, Kit.Text(stage.T(BoardLog.GodKey((int)god)), Kit.F2), null, "sn-shrine-bar-name");
                var track = Add(els.Row, new VisualElement(), null, "sn-shrine-bar");
                els.Fill = Add(track, new VisualElement(), null, "sn-shrine-bar-fill");
                FxUi.OriginLeft(els.Fill);
                int n = (int)god < counts.Length ? counts[(int)god] : 0;
                els.Count = Add(els.Row, Kit.Text(n.ToString(inv), Kit.F2), null, "sn-shrine-bar-count");
                FxUi.Scale(els.Fill, view.Seats.Length > 0 ? n / (float)view.Seats.Length : 0f, 1f);
                barEls[(int)god] = els;
            }
        }

        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || panel == null) return;
            Refresh();   // 그리는 행이 넘어가면(공물 → 축복 → 집계) 꼴이 바뀐다 — 같으면 Refresh가 움직이기만 한다
        }

        // 보기 판: 그 좌석이 가리키는 사당 카드에 좌석 색 테두리(방 판의 「남의 고르기 보기」 — P09와 같은 소식)
        void WatchMarks()
        {
            var mark = Shown == Mode.Cards ? flow.PeekOf(watch, PeekSpot.Shrine) : null;
            Pointed = mark != null && mark.Index < cardEls.Count ? mark.Index : -1;
            var tone = new StyleColor((Color)flow.SeatColor(watch));
            for (int i = 0; i < cardEls.Count; i++)
            {
                var box = cardEls[i].Box;
                bool on = i == Pointed;
                box.EnableInClassList("sn-shrine-card-picked", on);
                var color = on ? tone : new StyleColor(StyleKeyword.Null);
                box.style.borderLeftColor = box.style.borderRightColor = box.style.borderTopColor = box.style.borderBottomColor = color;
            }
        }

        // 31·32·34행의 그 순간
        void Animate(FxSequencer seq)
        {
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return;
            var inv = CultureInfo.InvariantCulture;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            var offer = ShrineRows.Playing(seq, ShrineRows.Offer, out int offerMs);
            bool offering = offer != null && Shown == Mode.Fx;
            if (watch >= 0) WatchMarks();
            if (offered != null)
            {
                float fly = offering ? OfferBeats.Fly(offerMs) : offer == null && ShrineRows.Pending(seq, ShrineRows.Offer) != null ? 0f : 1f;
                FxUi.Move(offered, 0f, -fly * FlyPx * unit);
                FxUi.Fade(offered, 1f - fly);
            }
            if (pillar != null)
            {
                float light = 0f;
                float h = offering ? OfferBeats.Pillar(offerMs, out light) : 0f;
                FxUi.Scale(pillar, 1f, Mathf.Max(0.02f, h));
                FxUi.Fade(pillar, light);
            }
            if (jpRoll != null)
            {
                int gone = offering ? OfferBeats.JpRoll(offerMs, ShrineRules.ExtraCost) : offer == null && ShrineRows.Pending(seq, ShrineRows.Offer) != null ? 0 : ShrineRules.ExtraCost;
                jpRoll.text = stage.T("screen.shrine.jp.roll").Replace("{n}", (jpFrom - gone).ToString(inv));
            }
            bool dropping = Shown == Mode.Fx && ShrineRows.Pending(seq, ShrineRows.Offer) != null;
            var bless = ShrineRows.Playing(seq, ShrineRows.Blessing, out int blessMs);
            var blessed = Shown == Mode.Fx ? ShrineRows.First<ShrineBlessing>(bless) : null;
            for (int i = 0; i < cardEls.Count; i++)
            {
                var c = cardEls[i];
                float show = 1f, drop = 0f, sx = 1f;
                bool face = true;
                if (dropping)
                {
                    if (offer == null) { show = 0f; face = false; }
                    else sx = OfferBeats.Card(offerMs, i, out show, out drop, out face);
                }
                FxUi.Fade(c.Box, show);
                FxUi.Move(c.Box, 0f, -drop * DropPx * unit);
                FxUi.Scale(c.Box, Mathf.Max(0.02f, sx), 1f);
                FxUi.Fade(c.Front, face ? 1f : 0f);
                FxUi.Fade(c.Back, face ? 0f : 1f);
                FxUi.Fade(c.Glow, blessed != null ? BlessBeats.Pulse(blessMs) : 0f);
            }
            if (blessBadge != null)
            {
                float b = blessed != null ? BlessBeats.Badge(blessMs) : 0f;
                if (blessed != null)
                {
                    int me = Seat;
                    blessBadge.text = blessed.Seat == me && blessed.From == me ? stage.T("screen.shrine.blessed")
                        : blessed.Seat == me ? KoJosa.Fix(stage.T("screen.shrine.blessed.from").Replace("{name}", flow.SeatName(blessed.From)))
                        : KoJosa.Fix(stage.T("screen.shrine.blessed.to").Replace("{name}", flow.SeatName(blessed.Seat)));
                }
                FxUi.Fade(blessBadge, b);
                float s = blessed != null ? BlessBeats.BadgeScale(blessMs) : 1f;
                FxUi.Scale(blessBadge, s, s);
            }
            // 축복마다의 고유 착지(P35)
            GiftKind = BlessGift.None;
            GiftsShown = 0;
            GiftText = null;
            if (giftText != null)
            {
                var ids = flow.Ctx.Decks.CardIds;
                var gift = blessed != null && blessed.CardKey >= 0 && blessed.CardKey < ids.Count ? BlessBeats.GiftOf(ids[blessed.CardKey]) : BlessGift.None;
                int n = BlessBeats.Gifts(gift, blessed != null ? blessed.Tier : 0);
                GiftKind = gift;
                for (int i = 0; i < giftEls.Count; i++)
                {
                    var g = giftEls[i];
                    var pose = i < n ? BlessBeats.Gift(gift, blessMs, bless.Ms, i) : default(GiftPose);
                    g.style.display = i < n ? DisplayStyle.Flex : DisplayStyle.None;
                    if (pose.Show <= 0f) { FxUi.Fade(g, 0f); continue; }
                    string key = GiftArt(gift, blessed.Tier, i);
                    var tex = key != null ? GenAssets.Load<Texture2D>(key) : null;
                    g.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
                    g.EnableInClassList("sn-shrine-gift-card", gift == BlessGift.Wisdom);
                    // 고대의 지혜: 기술 카드 그림이 번들에 없다 — 기술 줄 꼴 칸에 타입 그림(어느 기술일지는 고르기 전이라 「???」)
                    if (gift != BlessGift.Wisdom) g.Clear();
                    else if (g.childCount == 0) MonParts.TypeIcon(g, PokemonType.Typeless);
                    FxUi.Move(g, pose.X * GiftPx * unit, pose.Y * GiftPx * unit);
                    FxUi.Scale(g, pose.Wide * pose.Size, pose.Size);
                    FxUi.Fade(g, pose.Show);
                    GiftsShown++;
                }
                var roll = gift == BlessGift.Tranquil ? BlessBeats.Gift(gift, blessMs, bless.Ms, 0) : default(GiftPose);
                if (roll.Show > 0f)
                {
                    int jp = 0;
                    if (blessed.Changes != null)
                        foreach (var ch in blessed.Changes)
                            if (ch.Res == ResKind.Jp) jp += ch.After - ch.Before;
                    if (jp <= 0) jp = 2 * Mathf.Clamp(blessed.Tier, 1, 3);   // 카드 글 「2/4/6점」
                    GiftText = stage.T("screen.shrine.jp.roll").Replace("{n}", WorkUi.Signed(BlessBeats.Roll(blessMs, jp)));
                    giftText.text = GiftText;
                    FxUi.Scale(giftText, roll.Size, roll.Size);
                }
                FxUi.Fade(giftText, roll.Show);
            }

            // 좌석 줄: 공개 전 = 선택 중 / 완료, 공개(340)에서 뒤집혀 고른 신
            var tally = ShrineRows.Playing(seq, ShrineRows.Tally, out int tallyMs);
            bool before = ShrineRows.Pending(seq, ShrineRows.Tally) != null && tally == null;   // 집계가 대기열에서 기다린다: 아직 뒷면
            bool hidden = FractureRules.Choosing(view) || before;
            for (int i = 0; i < seatEls.Count; i++)
            {
                var s = seatEls[i];
                bool up = !hidden;
                float sx = 1f;
                if (tally != null) sx = TallyBeats.Flip(tallyMs, i, out up);
                var god = s.Seat < view.Seats.Length ? view.Seats[s.Seat].Follower : SinnohGod.None;
                bool known = up && (int)god >= 0 && (int)god <= (int)SinnohGod.Arceus;   // 아르세우스의 추종자도(M6)
                // 공개된 추종자: 배지 바탕을 그 신의 색으로(결정 80 L13 나)
                foreach (var g in ShownGods)
                    s.Badge.EnableInClassList(BoardHud.FollowClass + "-" + g.ToString().ToLowerInvariant(), known && god == g);
                s.Badge.EnableInClassList(BoardHud.FollowClass, known && god != SinnohGod.None);
                if (known)
                {
                    s.Badge.text = god == SinnohGod.None ? stage.T("screen.shrine.follower.nobody") : stage.T(BoardLog.GodKey((int)god));
                    var tex = god != SinnohGod.None ? GenAssets.Load<Texture2D>(GodIcon(god)) : null;
                    s.Icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
                    FxUi.Fade(s.Icon, tex != null ? 1f : 0f);
                    s.Badge.EnableInClassList("sn-pick-seat-done", false);
                }
                else
                {
                    bool done = FollowerDone(view, s.Seat);
                    s.Badge.text = stage.T(done ? "screen.setup.seat.done" : "screen.setup.seat.picking");
                    s.Badge.EnableInClassList("sn-pick-seat-done", done);
                    FxUi.Fade(s.Icon, 0f);
                }
                FxUi.Scale(s.Flip, Mathf.Max(0.02f, sx), 1f);
            }
            if (barEls.Count > 0)
            {
                var counts = lastTally != null && lastTally.Counts != null ? lastTally.Counts : FractureRules.Counts(view);
                float fill = tally != null ? TallyBeats.Bar(tallyMs) : before ? 0f : 1f;
                float lead = tally != null ? TallyBeats.Lead(tallyMs) : before ? 0f : 1f;
                foreach (var kv in barEls)
                {
                    int n = kv.Key < counts.Length ? counts[kv.Key] : 0;
                    FxUi.Scale(kv.Value.Fill, Mathf.Max(0.001f, fill * n / Mathf.Max(1, view.Seats.Length)), 1f);
                    kv.Value.Count.text = (before ? 0 : tally != null ? Mathf.RoundToInt(fill * n) : n).ToString(inv);
                    bool top = lastTally != null && lastTally.Tied != null && Array.IndexOf(lastTally.Tied, kv.Key) >= 0;
                    kv.Value.Row.EnableInClassList("sn-shrine-bar-lead", top && lead >= 1f);
                }
            }
        }

        // ── 입력 ──

        void Repaint()
        {
            painted = long.MinValue;
            Refresh();
        }

        /// <summary>공물로 바칠 색을 고른다(합법 답에 있는 색만).</summary>
        public bool PickColor(int c)
        {
            if (!Open || !Offers().Exists(a => a.Arg == c)) return false;
            color = c;
            Repaint();
            return true;
        }

        /// <summary>여행 점수 7을 더 바칠지(3장 뽑아 2장 — 점수가 모자라면 켜지지 않는다).</summary>
        public bool SetExtra(bool on)
        {
            if (!Open || (on && !Offers().Exists(a => a.Values.Count > 0))) return false;
            extra = on;
            Repaint();
            return true;
        }

        ChoiceAnswer OfferAnswer() => color < 0 ? null : Offers().Find(a => a.Arg == color && (a.Values.Count > 0) == extra);

        /// <summary>[바치기]: 고른 색(+ 여행 점수 7)의 공물 답을 낸다. 받아들여졌으면 true — 판은 그대로 남아 카드가 내려온다.</summary>
        public bool PressOffer()
        {
            var client = flow.Client;
            var choice = client != null ? client.Choice(ChoiceKind.MapAction) : null;
            var answer = OfferAnswer();
            if (!Open || choice == null || answer == null) return false;
            stage.Tips.Hide();
            color = -1;
            extra = false;
            if (reach >= 0) answer = TrainerRules.ReachAnswer(reach, answer);   // 염력: 그 칸의 psychic 답으로 낸다
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>목록의 i번째 답을 고른다(카드 · 포켓몬 · 야생 카드 · 기술 · 기술 칸 · 좌석 · 신).</summary>
        public bool PickOption(int index)
        {
            if (!Open || index < 0 || index >= opts.Count) return false;
            selected = index;
            if (Shown == Mode.Cards) PeekCard(index, true);
            if (Shown == Mode.ExchangeCard || Shown == Mode.Move || Shown == Mode.Exchange)
            {
                int at = shownOpts.IndexOf(index);
                if (at < 0)
                {
                    // 걸러져 안 보이는 답을 골랐다(길잡이) — 거르기를 풀고 그 답의 쪽으로
                    find = "";
                    findType = null;
                    at = index;
                }
                page = at / PageRows;
            }
            Repaint();
            return true;
        }

        /// <summary>
        /// 사당 카드 고르기에서 그 카드(목록의 i번째, −1 = 떠남)에 마우스를 올렸다 — 방 판이면 다른 좌석의 화면에 내 좌석 색으로 보인다(M9 다듬기 P09). 카드 고르기가 아니면(규토리 · 추종자 · 야생 카드 …) 보내지 않는다.
        /// </summary>
        public bool HoverCard(int index) => Open && Shown == Mode.Cards && PeekCard(index, false);

        bool PeekCard(int index, bool press)
        {
            var choice = ChoiceOf(flow.Client);
            if (choice == null || choice.Kind != ChoiceKind.Reward || choice.Sub != ChoiceSub.ShrinePick || index >= opts.Count) return false;
            return flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Shrine, index < 0 ? -1 : choice.Options.IndexOf(opts[index].Target), press);
        }

        /// <summary>가르칠 포켓몬을 고른다(고대의 지혜).</summary>
        public bool PickMon(int pokemon)
        {
            if (!Open || Shown != Mode.Teach) return false;
            mon = pokemon;
            selected = -1;
            Repaint();
            return true;
        }

        /// <summary>규토리 하나를 더한다(뜻밖의 선물 — 고를 수를 넘지 않게).</summary>
        public bool AddUnit(int unit)
        {
            var choice = ChoiceOf(flow.Client);
            if (!Open || choice == null || Shown != Mode.Apricorns || multi.Count >= choice.Count) return false;
            multi.Add(unit);
            Repaint();
            return true;
        }

        /// <summary>스튜 하나를 고르거나 푼다(화려한 만찬 — 서로 다른 것만).</summary>
        public bool ToggleUnit(int unit)
        {
            var choice = ChoiceOf(flow.Client);
            if (!Open || choice == null || Shown != Mode.Stews) return false;
            if (!multi.Remove(unit))
            {
                if (multi.Count >= choice.Count) return false;
                multi.Add(unit);
            }
            Repaint();
            return true;
        }

        /// <summary>고른 규토리·스튜를 비운다.</summary>
        public bool ResetPicks()
        {
            if (!Open) return false;
            multi.Clear();
            Repaint();
            return true;
        }

        bool Turn(int by)
        {
            page += by;
            Repaint();
            return true;
        }

        // 지금 고른 것이 만드는 답(합법 답 그대로 — 다 고르지 않았으면 null)
        ChoiceAnswer Answer()
        {
            var client = flow.Client;
            var choice = ChoiceOf(client);
            if (choice == null) return null;
            if (Shown == Mode.Apricorns || Shown == Mode.Stews)
            {
                if (multi.Count != choice.Count) return null;
                var want = new List<int>(multi);
                want.Sort();
                return client.Legal(choice).Find(a => a.Kind != AnswerKind.Decline && Same(a.Values, want));
            }
            return selected >= 0 && selected < opts.Count ? opts[selected] : null;
        }

        static bool Same(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>[확정]: 고른 답을 낸다. 받아들여졌으면 true.</summary>
        public bool Confirm()
        {
            var choice = ChoiceOf(flow.Client);
            var answer = Answer();
            if (!Open || choice == null || answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>[하지 않기]: 「안 함」 답을 낸다(교환 · 배우기 — 합법 답에 있을 때만).</summary>
        public bool PressDecline()
        {
            var client = flow.Client;
            var choice = ChoiceOf(client);
            var answer = choice != null ? client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline) : null;
            if (!Open || answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>확인용(길잡이 · 시험): 그 답이 되도록 판에서 고르고 낸다 — 공물이면 색 · 7점 → [바치기], 그 밖은 고르고 [확정](「안 함」은 [하지 않기], 추종자의 「안 고름」은 그 칸).</summary>
        public bool Press(ChoiceAnswer answer)
        {
            if (answer == null || !Open) return false;
            if (answer.Kind == AnswerKind.Offer) return Shown == Mode.Offer && PickColor(answer.Arg) && SetExtra(answer.Values.Count > 0) && PressOffer();
            if (Shown == Mode.Apricorns || Shown == Mode.Stews)
            {
                multi.Clear();
                Repaint();
                foreach (int v in answer.Values)
                    if (!(Shown == Mode.Apricorns ? AddUnit(v) : ToggleUnit(v))) return false;
                return Confirm();
            }
            if (answer.Kind == AnswerKind.Decline && Shown != Mode.Follower) return PressDecline();
            if (Shown == Mode.Teach && !PickMon(answer.Target)) return false;
            int index = opts.FindIndex(a => a.SameAs(answer));
            return index >= 0 && PickOption(index) && Confirm();
        }
    }
}
