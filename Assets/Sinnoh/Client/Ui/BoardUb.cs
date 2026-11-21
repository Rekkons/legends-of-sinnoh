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
    /// 보드 HUD의 울트라 버스트 몫(M6 「UB 화면 묶음 1」 — 결정 84 M1 가 · M2 가). <see cref="BoardHud"/>가 만들고 갱신·프레임마다 부른다. 신오의 심장 판에서는 아무것도 보이지 않는다.
    /// <list type="bullet">
    /// <item>S56 균열 칩: 위 띠 오른쪽 상태 판의 [메뉴] 앞 — 「균열 ▮▮▯ 23%」(10% 일곱 칸, 마지막 칸 = 70% 패배 앞 붉은 칸) · 「프리즘 n」 · 「축복 n」. 마우스 오버 = 균열 강도 · 패배선 · 구간 카드 · 다음 10%까지 남은 파괴 수 · 다음 UB 턴 이동 수.
    /// 숫자는 표시 값이다(<see cref="UbGauge.Shown"/> — 파괴 단계 동안 굴러 오른다).</item>
    /// <item>S57 UB 턴(82행 예비 821): 균열 칩이 눌리고, 위 가운데 띠 「울트라 비스트의 턴 · 자동 · 이동 n/m」(<see cref="UbTurnBeats"/>), 움직이는 UB 머리 위 「이름 · 자동」 꼬리표. 가운데 페이즈 띠(102행)는 <see cref="TurnBand"/>.</item>
    /// <item>84행 네크로즈마 빛 고리(840) · 칸마다 빛줄기(830) · 85행 붉은 가장자리 맥동과 좌석 칩으로 나는 균열 카드(850) · 86행 패배 덮개(860 — 게임이 끝난 뒤에도 남는다).</item>
    /// </list>
    /// 요소 이름: <c>board-hud-ub</c>(균열 칩 — 막대 <c>board-hud-ub-bar</c> · 값 <c>board-hud-ub-value</c>) · <c>board-hud-ub-prism</c> · <c>board-hud-ub-bless</c> · <c>board-hud-ub-band</c>(<c>-band-moves</c>) · <c>board-hud-ub-tag</c> ·
    /// <c>board-hud-ub-glow</c> · <c>board-hud-ub-beam</c> · <c>board-hud-ub-edge</c> · <c>board-hud-ub-card-&lt;i&gt;</c> · <c>board-hud-ub-end</c>. 모두 마우스를 받지 않는다(칩 · 작은 칩은 툴팁 때문에 받는다).
    /// </summary>
    public sealed class BoardUb
    {
        public const string Gauge = "board-hud-ub", Prism = "board-hud-ub-prism", Bless = "board-hud-ub-bless", Band = "board-hud-ub-band", Tag = "board-hud-ub-tag",
            Beam = "board-hud-ub-beam", Glow = "board-hud-ub-glow", Edge = "board-hud-ub-edge", End = "board-hud-ub-end";

        /// <summary>균열 카드가 날며 위 띠 아래로 처지는 깊이(720 벌 px).</summary>
        public const float CardArcPx = 40f;

        /// <summary>좌석이 이 수 이상이면 균열 칩 · 작은 칩을 위 띠 아래 오른쪽 판(<c>board-hud-ub-row</c>)에 둔다(<see cref="BoardHud.FollowIconSeats"/>와 같은 금).</summary>
        public const int RowSeats = BoardHud.FollowIconSeats;

        readonly VisualElement status, menu, row;
        readonly GameFlow flow;
        readonly UiStage stage;
        readonly VisualElement gauge, bar, band, tag, glow, beam, edge, end, endWash, endNight, endWords;
        readonly VisualElement phase, phaseFill, coin, coinFace;   // 90행 네크로즈마 페이즈 판 · 썬/문 동전(M6 「UB 배틀 화면 묶음」)
        readonly Label phaseTitle, phaseSub, phaseHp, coinText, coinLabel;
        readonly VisualElement gold, blessFly, blessRing;   // 93행 실버디 → 축복 +1(M6 「연출 목록 채우기」)
        readonly Label value, prism, bless, bandMoves, tagName;
        readonly List<VisualElement> fills = new List<VisualElement>();
        readonly List<VisualElement> cards = new List<VisualElement>();
        int paintedBreach = int.MinValue, paintedPrism = int.MinValue, paintedBless = int.MinValue, paintedNeed = int.MinValue;

        /// <summary>지금 균열 칩이 보이는 값(%) · 위 가운데 띠의 진하기 · 머리 위 꼬리표가 붙은 UB(없으면 −1) · 빛줄기 · 맥동 · 날고 있는 카드 수 · 패배 덮개 — 확인용.</summary>
        public int ShownBreach { get; private set; }
        public float BandShown { get; private set; }
        public int TagFor { get; private set; } = -1;
        public float BeamShown { get; private set; }
        public float GlowShown { get; private set; }
        public float EdgeShown { get; private set; }
        public int CardsFlying { get; private set; }
        public float EndShown { get; private set; }
        /// <summary>네크로즈마 페이즈 판 · 체력 막대가 찬 만큼 · 동전 · 동전이 보이는 면(0 썬 · 1 문) — 확인용(M6 「UB 배틀 화면 묶음」).</summary>
        public float PhaseShown { get; private set; }
        public float PhaseFill { get; private set; }
        public float CoinShown { get; private set; }
        public int CoinFace { get; private set; } = -1;
        /// <summary>위 띠 축복 칩이 보이는 수 · 실버디 축복 아이콘이 날아간 정도(−1 = 안 남 · 0 ~ 1) · 신전 칸 금빛 · 칩의 금빛 고리 — 확인용(93행).</summary>
        public int ShownBless { get; private set; } = -1;
        public float BlessFly { get; private set; } = -1f;
        public float GoldShown { get; private set; }
        public float RingShown { get; private set; }

        /// <summary>
        /// status = 위 띠 상태 판(칩을 [메뉴] 앞에 끼운다), menu = [메뉴], page = HUD 판(덮개·꼬리표를 얹는다 — 위 띠·아래 판보다 뒤에 더하면 그 위에 그려진다).
        /// </summary>
        public BoardUb(GameFlow flow, UiStage stage, VisualElement status, VisualElement menu, VisualElement page)
        {
            this.flow = flow;
            this.stage = stage;
            int at = status.IndexOf(menu);
            gauge = new VisualElement { name = Gauge };
            gauge.AddToClassList("sn-ub-gauge");
            gauge.Add(Kit.Text(stage.T("screen.board.ub.breach"), Kit.F2));
            bar = Add(gauge, "board-hud-ub-bar", "sn-ub-bar");
            for (int i = 0; i < UbGauge.Segments; i++)
            {
                var seg = Add(bar, null, i == UbGauge.Segments - 1 ? "sn-ub-seg-last" : "sn-ub-seg");
                seg.AddToClassList("sn-ub-seg-any");
                fills.Add(Add(seg, null, i == UbGauge.Segments - 1 ? "sn-ub-fill-last" : "sn-ub-fill"));
            }
            value = Kit.Text("", Kit.F2);
            value.name = "board-hud-ub-value";
            value.AddToClassList("kit-title");
            value.AddToClassList("sn-ub-value");
            gauge.Add(value);
            status.Insert(at, gauge);
            prism = Kit.Badge("", KitBadgeKind.Silver, Kit.F2);
            prism.name = Prism;
            status.Insert(at + 1, prism);
            bless = Kit.Badge("", KitBadgeKind.Gold, Kit.F2);
            bless.name = Bless;
            status.Insert(at + 2, bless);
            // 축복 칩을 누르면 축복 판(S59 — 결정 84 M4 가, M6). 균열 칩 · 프리즘 칩은 마우스 오버만
            stage.OnTap(bless, SoundKeys.Decide, e => PressBless(), () => flow.CanOpenBless);
            foreach (var e in new VisualElement[] { gauge, prism, bless }) e.style.display = DisplayStyle.None;
            // 좌석이 RowSeats 이상이면 위 띠 폭이 모자라(1080 벌 좌석 칩 다섯·여섯 + 상태 판) 칩을 상태 판 바로 아래 오른쪽의 작은 판으로 옮긴다
            this.status = status;
            this.menu = menu;
            row = Kit.Panel(stage.Skin);
            row.name = "board-hud-ub-row";
            row.AddToClassList("sn-ub-row");
            row.style.display = DisplayStyle.None;
            page.Add(row);

            // 84행 빛 고리 · 빛줄기(보드 위 — 위 띠·아래 판 아래에 깔리도록 page 앞쪽에) · 85행 가장자리 맥동 · 카드 · 82행 위 가운데 띠 · 머리 위 꼬리표 · 86행 덮개
            glow = Layer(page, Glow, "sn-ub-glow");
            beam = Layer(page, Beam, "sn-ub-beam");
            int under = page.IndexOf(status.parent);   // 위 띠(좌석 칩 · 상태 판) 바로 앞 — 위 띠 · 기록 판 · 아래 판이 그 위에 그려진다
            page.Insert(under, beam);
            page.Insert(under, glow);
            edge = Layer(page, Edge, "sn-ub-edge");
            var holder = Layer(page, null, "sn-ub-band-row");
            FxUi.Fade(holder, 1f);
            band = Kit.Panel(stage.Skin);
            band.name = Band;
            band.AddToClassList("sn-ub-band");
            band.pickingMode = PickingMode.Ignore;
            holder.Add(band);
            var title = Kit.Text(stage.T("screen.board.ub.turn"), Kit.FTitle);
            title.AddToClassList("kit-title");
            band.Add(title);
            band.Add(Kit.Badge(stage.T("screen.board.ub.auto"), KitBadgeKind.Silver, Kit.F2));
            bandMoves = Kit.Badge("", KitBadgeKind.Warn, Kit.F2);
            bandMoves.name = "board-hud-ub-band-moves";
            band.Add(bandMoves);
            FxUi.Fade(band, 0f);
            tag = Layer(page, Tag, "sn-ub-tag");
            tagName = Kit.Badge("", KitBadgeKind.Plain, Kit.F2);
            tag.Add(tagName);
            tag.Add(Kit.Badge(stage.T("screen.board.ub.auto"), KitBadgeKind.Silver, Kit.F2));
            for (int i = 0; i < SeatId.MaxSeats; i++) cards.Add(Layer(page, "board-hud-ub-card-" + i.ToString(CultureInfo.InvariantCulture), "sn-ub-card"));   // 위 가운데 띠 위로 지나간다
            // 90행: 가운데 판 「네크로즈마 n페이즈」 · 다음 폼 · 체력 막대 / 썬·문 동전과 결과 글
            var phaseRow = Layer(page, null, "sn-ub-phase-row");
            FxUi.Fade(phaseRow, 1f);
            phase = Kit.Panel(stage.Skin);
            phase.name = "board-hud-ub-phase";
            phase.AddToClassList("sn-ub-phase");
            phaseRow.Add(phase);
            phaseTitle = Kit.Text("", Kit.FTitle);
            phaseTitle.AddToClassList("kit-title");
            phase.Add(phaseTitle);
            phaseSub = Kit.Text("", Kit.F2);
            phase.Add(phaseSub);
            var hpBar = Add(phase, "board-hud-ub-phase-bar", "sn-ub-phase-bar");
            phaseFill = Add(hpBar, null, "sn-ub-phase-fill");
            phaseHp = Kit.Badge("", KitBadgeKind.Plain, Kit.F2);
            phase.Add(phaseHp);
            FxUi.Fade(phase, 0f);
            coin = Layer(page, "board-hud-ub-coin", "sn-ub-coin");
            coinFace = Add(coin, "board-hud-ub-coin-face", "sn-ub-coin-face");
            coinText = Kit.Text("", Kit.F3);
            coinText.AddToClassList("sn-ub-coin-text");
            coinFace.Add(coinText);
            coinLabel = Kit.Badge("", KitBadgeKind.Gold, Kit.F2);
            coinLabel.name = "board-hud-ub-coin-label";
            coinLabel.AddToClassList("sn-ub-coin-label");
            coin.Add(coinLabel);
            Unpick(phaseRow);
            Unpick(coin);
            // 93행: 신전 칸의 금빛 → 위 띠 축복 칩으로 나는 아르세우스 아이콘(전용 아이템 「아르세우스의 추종자」 그림) → 칩의 금빛 고리
            gold = Layer(page, "board-hud-ub-gold", "sn-ub-gold");
            blessFly = Layer(page, "board-hud-ub-bless-fly", "sn-ub-bless-fly");
            var icon = GenAssets.Load<Texture2D>(GenAssets.TrainerItemIcons + "follower_of_arceus.png");
            if (icon != null) blessFly.style.backgroundImage = new StyleBackground(icon);
            blessRing = Layer(page, "board-hud-ub-bless-ring", "sn-ub-bless-ring");
            end = Layer(page, End, "sn-ub-end");
            endWash = Add(end, null, "sn-ub-end-wash");
            endNight = Add(end, null, "sn-ub-end-night");
            endWords = Add(end, null, "sn-ub-end-words");
            var lost = Kit.Text(stage.T("screen.board.ub.lost"), Kit.F3);
            lost.AddToClassList("sn-ub-end-title");
            endWords.Add(lost);
            var why = Kit.Text(stage.T("screen.board.ub.lost.why"), Kit.F2);
            why.AddToClassList("sn-ub-end-line");
            endWords.Add(why);
            Unpick(holder);
            foreach (var e in new[] { glow, beam, edge, tag, end, gold, blessFly, blessRing }) Unpick(e);
            foreach (var c in cards) Unpick(c);
        }

        static VisualElement Add(VisualElement parent, string name, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (name != null) e.name = name;
            if (cls != null) e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        static VisualElement Layer(VisualElement page, string name, string cls)
        {
            var e = Add(page, name, cls);
            FxUi.Fade(e, 0f);
            return e;
        }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        /// <summary>균열 칩 · 작은 칩을 지금 보기 값으로(갱신마다 — HUD Refresh가 부른다).</summary>
        public void Refresh()
        {
            var view = flow.Client != null ? flow.Client.View : null;
            bool on = view != null && view.Ub != null;
            foreach (var e in new VisualElement[] { gauge, prism, bless }) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            bool below = on && view.Seats.Length >= RowSeats;
            row.style.display = below ? DisplayStyle.Flex : DisplayStyle.None;
            if (below && gauge.parent != row)
                foreach (var e in new VisualElement[] { gauge, prism, bless }) row.Add(e);
            else if (!below && gauge.parent != status)
            {
                int at = status.IndexOf(menu);
                status.Insert(at, gauge);
                status.Insert(at + 1, prism);
                status.Insert(at + 2, bless);
            }
            if (!on) return;
            var inv = CultureInfo.InvariantCulture;
            var ub = view.Ub;
            if (ub.PrismUltraBurst != paintedPrism || ub.PrismUltraBeast != paintedPrism2)
            {
                paintedPrism = ub.PrismUltraBurst;
                paintedPrism2 = ub.PrismUltraBeast;
                prism.text = PrismText(stage, ub.PrismUltraBurst, ub.PrismUltraBeast);   // 「프리즘 n / UB m」 — UB 쪽 수도 칩에(결정 87 V15)
                stage.Tips.Hook(prism, stage.T("screen.board.ub.prism.title"), Lines(stage.T("screen.board.ub.prism.tip.1").Replace("{a}", ub.PrismUltraBurst.ToString(inv)).Replace("{b}", ub.PrismUltraBeast.ToString(inv)), stage.T("screen.board.ub.prism.tip.2")), TipSide.Below, reading: true);
            }
            paintedBreach = int.MinValue;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            PaintBless(seq);
            PaintGauge(seq);
            if (seq == null || seq.IsEmpty) Settle(view);   // 대기열이 다 돌았다: 연출 요소를 걷고(패배한 판이면 덮개는 남긴다)
        }

        void Settle(GameState view)
        {
            Hide();
            FxUi.Scale(gauge, 1f, 1f);
            gauge.RemoveFromClassList("sn-ub-gauge-hot");
            if (view.Phase != Phase.GameOver || view.Ub.Result != UbResult.Lost) return;
            EndShown = 1f;
            FxUi.Fade(end, 1f);
            FxUi.Fade(endWash, 1f);
            FxUi.Fade(endNight, 1f);
            FxUi.Fade(endWords, 1f);
        }

        int paintedPrism2 = int.MinValue;

        /// <summary>위 띠 프리즘 칩(881행 — 배틀 화면이 조각을 여기서 날린다, 결정 87 V23 다).</summary>
        public VisualElement PrismChip => prism;

        /// <summary>881행 동안 배틀 화면이 칩을 몬다: 조각이 떠나고 닿는 때에 맞춘 두 수 + 커진 정도. 끝나면(on = false) 보기 값으로 되돌린다.</summary>
        public void ShowPrismMove(bool on, int burst, int beast, float scale)
        {
            if (on) prism.text = PrismText(stage, burst, beast);
            else if (paintedPrism != int.MinValue) prism.text = PrismText(stage, paintedPrism, paintedPrism2);
            prism.EnableInClassList("sn-hud-ub-prism-lit", on && scale > 1f);   // 옆 칩을 덮지 않게 늘리지 않고 밝힌다
        }

        /// <summary>프리즘 칩의 글(「프리즘 n / UB m」 — 네크로즈마 쪽 · 울트라 비스트 쪽, 결정 87 V15). 배틀 화면 위에 잠깐 뜨는 칩(881행)도 같은 글이다.</summary>
        public static string PrismText(UiStage stage, int burst, int beast)
        {
            var inv = CultureInfo.InvariantCulture;
            return stage.T("screen.board.ub.prism").Replace("{n}", burst.ToString(inv)).Replace("{m}", beast.ToString(inv));
        }

        // 축복 칩: 보기 값에서 대기열에 남은 실버디 축복(930)의 아이콘이 닿기 전이면 얻기 전 값(93행 「착지: 축복 수 +1」)
        void PaintBless(FxSequencer seq)
        {
            var ub = flow.Client.View.Ub;
            int shown = SilvallyBeats.Shown(ub.Blessings, seq != null ? seq.Queued : null, seq != null ? seq.Current : null, seq != null ? seq.LocalMs : 0);
            ShownBless = shown;
            if (shown == paintedBless) return;
            paintedBless = shown;
            bless.text = stage.T("screen.board.ub.bless").Replace("{n}", shown.ToString(CultureInfo.InvariantCulture));
            stage.Tips.Hook(bless, stage.T("screen.board.ub.bless.title"), Lines(stage.T("screen.board.ub.bless.tip"), stage.T("screen.board.ub.bless.tip.open")), TipSide.Below, reading: true);
        }

        void PaintGauge(FxSequencer seq)
        {
            var view = flow.Client.View;
            int shown = UbGauge.Shown(view.Tokens, seq != null ? seq.Queued : null, seq != null ? seq.Current : null, seq != null ? seq.LocalMs : 0);
            int need = UbGauge.NextNeed(view.Tokens);
            ShownBreach = shown;
            if (shown == paintedBreach && need == paintedNeed) return;
            paintedBreach = shown;
            paintedNeed = need;
            var inv = CultureInfo.InvariantCulture;
            value.text = shown.ToString(inv) + "%";   // 글리프 「%」만 코드(두 언어 같다 — 문자열 표는 한글 없는 값을 받지 않는다)
            for (int i = 0; i < fills.Count; i++) fills[i].style.width = Length.Percent(Mathf.Round(UbGauge.Fill(shown, i) * 100f));
            // 마우스 오버의 「다음 10%까지 · 다음 UB 턴」은 보기 값으로 센다(연출 중에는 끝 값 — 다 보인 뒤 바로 맞는다)
            int now = UbGauge.Breach(view.Tokens);
            string tip = Lines(stage.T("screen.board.ub.breach.tip.1"), stage.T("screen.board.ub.breach.tip.2"),
                stage.T("screen.board.ub.breach.tip.3").Replace("{need}", need.ToString(inv)), stage.T("screen.board.ub.breach.tip.4").Replace("{moves}", UltraBurstRules.TotalMoves(now).ToString(inv)));
            stage.Tips.Hook(gauge, stage.T("screen.board.ub.breach.title").Replace("{n}", shown.ToString(inv)), tip, TipSide.Below, reading: true);
        }

        /// <summary>축복 칩을 눌렀다: 축복 판을 연다 — 이미 열린 판이면 닫는다. 열었거나 닫았으면 true(길잡이·시험도 부른다).</summary>
        public bool PressBless()
        {
            if (flow.Top is BlessScreen panel && panel.IsPanel)
            {
                flow.CloseBless();
                return true;
            }
            return flow.OpenBless();
        }

        // 툴팁 본문의 줄들(문자열 표는 줄바꿈을 받지 않는다 — 줄마다 키 하나)
        static string Lines(params string[] lines) => string.Join("\n", lines);

        /// <summary>대기열의 지금 모습(프레임마다 — HUD ShowFx가 부른다).</summary>
        public void ShowFx(FxSequencer seq)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || view.Ub == null)
            {
                Hide();
                return;
            }
            PaintGauge(seq);
            PaintBless(seq);
            var entry = seq.Current;
            int local = seq.LocalMs;
            // 82행: 균열 칩 눌림 · 위 가운데 띠 · 머리 위 꼬리표
            var turn = UbTurnBeats.At(entry, local);
            FxUi.Scale(gauge, turn.Press, turn.Press);
            BandShown = turn.Band;
            FxUi.Fade(band, turn.Band);
            if (turn.Band > 0f) bandMoves.text = stage.T("screen.board.ub.moves").Replace("{n}", Math.Min(turn.Landed, Math.Max(turn.Total, turn.Landed)).ToString(CultureInfo.InvariantCulture)).Replace("{max}", turn.Total.ToString(CultureInfo.InvariantCulture));
            TagFor = -1;
            if (turn.Moving >= 0 && flow.BeastHead(turn.Moving, out var head))
            {
                TagFor = turn.Moving;
                tagName.text = BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[turn.Moving].Key));
                FxUi.Move(tag, head.x, head.y);
            }
            FxUi.Fade(tag, TagFor >= 0 ? 1f : 0f);
            ShowStrike(entry, local);
            ShowBracket(entry, local);
            ShowPhase(entry, local);
            ShowCoin(entry, local);
            ShowSilvally(entry, local);
            ShowEnd(seq);
        }

        // 93행 실버디 → 축복 +1(930 — 결과 창 [확인] 뒤): 이긴 좌석이 선 신전 칸에서 금빛이 터지고 → 아르세우스 아이콘이 위 띠 축복 칩으로 호를 그려 날아가 → 칩에 박히며 금빛 고리 · 칩이 톡 커지고 수 +1
        void ShowSilvally(FxEntry entry, int local)
        {
            GoldShown = RingShown = 0f;
            BlessFly = -1f;
            var step = SilvallyBeats.StepOf(entry);
            var e = SilvallyBeats.Event(step);
            var view = flow.Client.View;
            bool live = step != null && e != null && local >= step.StartMs && local < step.EndMs;
            var pose = live ? SilvallyBeats.At(local - step.StartMs, step.Ms) : new SilvallyPose { Fly = -1f, Pop = 1f };
            int cell = live && e.Seat >= 0 && e.Seat < view.Seats.Length ? view.Seats[e.Seat].Cell : -1;
            Vector2 from = default;
            bool at = cell >= 0 && flow.CellPoint(cell, out from);
            if (pose.Light > 0f && at)
            {
                GoldShown = pose.Light;
                FxUi.Move(gold, from.x, from.y);
                FxUi.Scale(gold, 0.6f + pose.Light, 0.6f + pose.Light);
            }
            FxUi.Fade(gold, GoldShown);
            var chip = bless.worldBound;
            Vector2 to = float.IsNaN(chip.width) ? from : chip.center;   // 85행 카드처럼 판 좌표 = worldBound
            if (pose.Fly >= 0f && pose.Fly <= 1f && at)
            {
                BlessFly = pose.Fly;
                float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
                float x = Mathf.Lerp(from.x, to.x, pose.Fly), y = Mathf.Lerp(from.y, to.y, pose.Fly) - Mathf.Sin(Mathf.PI * pose.Fly) * CardArcPx * 2f * unit;   // 위로 호를 그린다(위 띠 아래에서 닿는다)
                FxUi.Move(blessFly, x, y);
            }
            FxUi.Fade(blessFly, BlessFly >= 0f ? 1f : 0f);
            if (pose.Ring > 0f)
            {
                RingShown = pose.Ring;
                float r = 1f + 1.5f * (1f - pose.Ring);
                FxUi.Move(blessRing, to.x, to.y);
                FxUi.Scale(blessRing, r, r);
            }
            FxUi.Fade(blessRing, RingShown);
            FxUi.Scale(bless, pose.Pop, pose.Pop);
        }

        void Hide()
        {
            BandShown = BeamShown = GlowShown = EdgeShown = EndShown = PhaseShown = PhaseFill = CoinShown = 0f;
            CoinFace = -1;
            GoldShown = RingShown = 0f;
            BlessFly = -1f;
            FxUi.Fade(phase, 0f);
            FxUi.Fade(coin, 0f);
            foreach (var e in new[] { gold, blessFly, blessRing }) FxUi.Fade(e, 0f);
            FxUi.Scale(bless, 1f, 1f);
            TagFor = -1;
            CardsFlying = 0;
            foreach (var e in new[] { band, tag, glow, beam, edge, end }) FxUi.Fade(e, 0f);
            foreach (var c in cards) FxUi.Fade(c, 0f);
        }

        // 84행: 빛을 모으는 동안 네크로즈마 둘레의 빛 고리(840) · 네크로즈마의 파괴 단계(830)마다 그 칸에 위에서 내리꽂히는 빛줄기
        void ShowStrike(FxEntry entry, int local)
        {
            GlowShown = BeamShown = 0f;
            var strike = StrikeBeats.StepOf(entry);
            if (strike != null && local >= strike.StartMs && local < strike.EndMs && flow.NecrozmaCell() is int n && n >= 0 && flow.CellPoint(n, out var at))
            {
                GlowShown = Math.Max(0.05f, StrikeBeats.Glow(local - strike.StartMs, strike.Ms));
                FxUi.Move(glow, at.x, at.y);
                FxUi.Scale(glow, 0.6f + GlowShown, 0.6f + GlowShown);
            }
            FxUi.Fade(glow, GlowShown);
            if (entry != null)
                foreach (var s in entry.Steps)
                {
                    if (!StrikeBeats.ByNecrozma(s) || local < s.StartMs || local >= s.EndMs) continue;
                    var d = UbRows.First<CellDestroyed>(s);
                    var pose = StrikeBeats.Beam(local - s.StartMs, s.Ms);
                    if (pose.Alpha <= 0f || !flow.CellPoint(d.Cell, out var cell)) break;
                    BeamShown = pose.Alpha;
                    FxUi.Move(beam, cell.x, cell.y);
                    FxUi.Scale(beam, 1f, Math.Max(0.02f, pose.Reach));
                    break;
                }
            FxUi.Fade(beam, BeamShown);
        }

        // 85행: 붉은 가장자리 맥동 → 균열 칩에서 좌석 칩마다 카드가 난다
        void ShowBracket(FxEntry entry, int local)
        {
            EdgeShown = 0f;
            CardsFlying = 0;
            var step = BracketBeats.StepOf(entry);
            int t = step != null ? local - step.StartMs : -1;
            bool live = step != null && t >= 0 && t < step.Ms;
            if (live) EdgeShown = BracketBeats.Pulse(t);
            FxUi.Fade(edge, EdgeShown);
            gauge.EnableInClassList("sn-ub-gauge-hot", live && t < BracketBeats.CardFromMs);
            var dealt = live ? BracketBeats.Dealt(step) : null;
            var from = gauge.worldBound;
            for (int i = 0; i < cards.Count; i++)
            {
                float k = -1f;
                VisualElement chip = null;
                if (dealt != null && dealt.PerSeat != null && i < dealt.PerSeat.Length && dealt.PerSeat[i] != null && dealt.PerSeat[i].CardKeys != null && dealt.PerSeat[i].CardKeys.Length > 0)
                {
                    k = BracketBeats.Card(t, i);
                    chip = stage.Root.Q("board-hud-seat-" + (dealt.PerSeat[i].Seat + 1).ToString(CultureInfo.InvariantCulture));
                }
                bool fly = k >= 0f && k <= 1f && chip != null && !float.IsNaN(from.width);
                if (fly)
                {
                    var to = chip.worldBound;
                    float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
                    float x = Mathf.Lerp(from.center.x, to.center.x, k), y = Mathf.Lerp(from.center.y, to.center.y, k) + Mathf.Sin(Mathf.PI * k) * CardArcPx * unit;   // 위 띠 아래로 처졌다 오른다(위로 뜨면 화면 밖)
                    FxUi.Move(cards[i], x, y);
                    CardsFlying++;
                }
                FxUi.Fade(cards[i], fly ? 1f : 0f);
            }
        }

        // 90행 네크로즈마 페이즈 격파(900 — 결과 창 [확인] 뒤): 네크로즈마에서 빛이 터지고(빛 고리를 함께 쓴다) 가운데 판 「네크로즈마 n페이즈」 · 다음 폼 · 체력 막대가 다시 찬다
        void ShowPhase(FxEntry entry, int local)
        {
            PhaseShown = PhaseFill = 0f;
            var step = PhaseBeats.StepOf(entry);
            var np = PhaseBeats.Event(step);
            if (step == null || np == null || local < step.StartMs || local >= step.EndMs)
            {
                FxUi.Fade(phase, 0f);
                return;
            }
            var inv = CultureInfo.InvariantCulture;
            var pose = PhaseBeats.At(local - step.StartMs, step.Ms);
            if (pose.Burst > 0f && flow.NecrozmaCell() is int n && n >= 0 && flow.CellPoint(n, out var at))
            {
                GlowShown = pose.Burst;
                FxUi.Move(glow, at.x, at.y);
                FxUi.Scale(glow, pose.Ring, pose.Ring);
                FxUi.Fade(glow, GlowShown);
            }
            PhaseShown = pose.Panel;
            PhaseFill = pose.Fill;
            var view = flow.Client.View;
            int next = Math.Max(1, Math.Min(UbNecrozma.Phases, np.Next));
            phaseTitle.text = stage.T("screen.board.ub.phase").Replace("{n}", next.ToString(inv));
            // 다음 폼: 3페이즈 = 울트라네크로즈마, 2페이즈는 동전을 던지기 전이면 그 안내(던졌으면 그 폼)
            int card = next >= UbNecrozma.Phases || view.Ub.SunMoon >= 0 ? UbNecrozma.Card(flow.Ctx, next, view.Ub.SunMoon) : -1;
            phaseSub.text = card >= 0 ? stage.T("pokemon." + flow.Ctx.Cards.Pokemon[card].Key) : stage.T("screen.board.ub.phase.coin");
            int hp = flow.Ctx.Cards.Pokemon[card >= 0 ? card : UbNecrozma.Card(flow.Ctx, next, UbNecrozma.Sun)].Health;
            phaseHp.text = stage.T("screen.board.ub.phase.hp").Replace("{n}", Mathf.RoundToInt(hp * pose.Fill).ToString(inv));
            phaseFill.style.width = Length.Percent(Mathf.Round(pose.Fill * 100f));
            FxUi.Fade(phase, PhaseShown);
        }

        // 90행 썬/문 동전(901 — 2페이즈 첫 도전, 조우 전환 앞): 화면 가운데에서 돌며 솟았다 떨어지고 → 「썬 — 황혼의 갈기」·「문 — 새벽의 날개」
        void ShowCoin(FxEntry entry, int local)
        {
            CoinShown = 0f;
            CoinFace = -1;
            var step = CoinBeats.StepOf(entry);
            var flip = CoinBeats.Event(step);
            if (step == null || flip == null || local < step.StartMs || local >= step.EndMs)
            {
                FxUi.Fade(coin, 0f);
                return;
            }
            var pose = CoinBeats.At(local - step.StartMs, step.Ms, flip.Result);
            CoinShown = pose.Alpha;
            CoinFace = pose.Face;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            FxUi.Move(coin, 0f, -pose.Lift * 120f * unit);
            FxUi.Scale(coinFace, pose.ScaleX * pose.Scale, pose.Scale);
            coinFace.EnableInClassList("sn-ub-coin-moon", pose.Face == UbNecrozma.Moon);
            coinText.text = stage.T(pose.Face == UbNecrozma.Moon ? "screen.board.ub.coin.moon" : "screen.board.ub.coin.sun");
            coinLabel.text = stage.T("screen.board.ub.coin." + (flip.Result == UbNecrozma.Moon ? "1" : "0"));
            FxUi.Fade(coinLabel, pose.Label);
            FxUi.Fade(coin, CoinShown);
        }

        // 86행: 패배 덮개 — 단계 동안 박자대로, 끝난 판(균열 패배)이면 다 보인 채
        void ShowEnd(FxSequencer seq)
        {
            var view = flow.Client.View;
            LostBeats.Pose pose = default;
            bool any = false;
            foreach (var entry in seq.Queued)
            {
                var step = LostBeats.StepOf(entry);
                if (step == null) continue;
                any = true;
                if (ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs)
                    pose = seq.LocalMs >= step.EndMs ? LostBeats.Done : LostBeats.At(seq.LocalMs - step.StartMs);
                break;
            }
            if (!any && view.Phase == Phase.GameOver && view.Ub.Result == UbResult.Lost) pose = LostBeats.Done;
            EndShown = Math.Max(pose.Wash, Math.Max(pose.Dark, pose.Title));
            FxUi.Fade(end, EndShown > 0f ? 1f : 0f);
            FxUi.Fade(endWash, pose.Wash);
            FxUi.Fade(endNight, pose.Dark);
            FxUi.Fade(endWords, pose.Title);
        }

        /// <summary>균열 칩 · 작은 칩 · 위 가운데 띠의 글 넘침(확인용 — <see cref="WorkUi.Overflows"/>) + 상태 판이 화면 안에 드는지.</summary>
        public List<string> Overflows()
        {
            var found = new List<string>();
            foreach (var e in new VisualElement[] { gauge, prism, bless, band, phase })
                if (e.resolvedStyle.display != DisplayStyle.None) found.AddRange(WorkUi.Overflows(e));
            var root = stage.Root.worldBound;
            var holder = gauge.parent;
            if (holder != null && holder.worldBound.xMax > root.xMax + 0.5f) found.Add(holder.name + ": 화면 오른쪽 밖으로 나간다");
            // 위 띠: 좌석 칩 줄과 상태 판이 겹치지 않는다(상태 판이 밀려 줄면 칩 글이 넘친다 — 위에서 함께 본다)
            var seats = stage.Root.Q("board-hud-seats");
            if (seats != null && seats.worldBound.xMax > status.worldBound.xMin + 0.5f) found.Add("board-hud-status: 좌석 칩 줄과 겹친다");
            if (status.worldBound.xMax > root.xMax + 0.5f) found.Add("board-hud-status: 화면 오른쪽 밖으로 나간다");
            return found;
        }
    }
}
