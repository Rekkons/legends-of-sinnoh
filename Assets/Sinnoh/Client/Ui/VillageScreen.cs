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
    /// S23 마을 창(04 §3-4 「마을」 — S25 공중날기 목적지 고르기 포함): 마을 행동을 하는 곳(마을 · 울트라메가로폴리스 · 앞면 야영지)에 서 있을 때 보드 HUD의 [마을]로 여는 창.
    /// <list type="bullet">
    /// <item>[전체 회복](턴당 1회): 누르면 그 답을 낸다. 창은 그대로 있다.</item>
    /// <item>공중날기(턴당 1회): 목적지 마을 하나 + 소모할 규토리 색 하나를 고르고 [출발]. 받아들여지면 창을 닫는다 — 말이 날아가는 것이 보이게.
    /// 목적지 줄은 보드의 마을 전부(지금 선 마을 · 울트라메가로폴리스 제외)이고, 갈 수 없는 마을(조무래기 점거)은 흐리게 + 「점거 중」이다.</item>
    /// <item>못 쓰는 행동은 단추가 꺼지고 까닭 한 줄이 선다(이미 사용 · 조무래기 점거 · 야영지 · 규토리 없음).</item>
    /// </list>
    /// 규칙을 다시 세지 않는다: 단추·목적지·규토리 색을 켜는 것은 엔진의 합법 답(<c>Client.Legal</c>) 그대로이고, 까닭 줄만 보기(공개 정보)에서 고른다.
    /// 은행상회 거래(S24 — 04 §3-4)는 회복과 공중날기 사이의 칸(<see cref="BankBox"/>)이다. 떠 있는 동안 보드 입력은 꺼지지만 화면 대기열은 멈추지 않는다. 창 밖을 누르거나 [닫기]로 닫는다.
    /// 요소 이름: <c>board-village</c>·<c>board-village-panel</c>·<c>board-village-title</c>·<c>board-village-close</c>·<c>board-village-grunt</c>·
    /// <c>board-village-heal</c>·<c>board-village-heal-why</c>·<c>board-village-fly</c>·<c>board-village-fly-why</c>·<c>board-village-dest-&lt;마을 키&gt;</c>·<c>board-village-color-&lt;색&gt;</c> ·
    /// 보드에서 고르기(T18 ① — <see cref="Picking"/>): <c>board-village-map</c>([지도에서 고르기])·<c>board-village-cancel</c>([취소])·<c>board-village-hint</c>(안내 · 까닭 줄).
    /// </summary>
    public sealed class VillageScreen : IWorkScreen, IWatchScreen
    {
        readonly GameFlow flow;
        BankBox bank;   // S24 은행상회 칸(M5 「제작·은행·진화 화면」 — 18행 연출도 이 칸이 그린다)
        readonly List<ChoiceAnswer> flights = new List<ChoiceAnswer>();
        readonly List<int> dests = new List<int>();
        UiStage stage;
        VisualElement panel, scrim;
        Label hint;
        Button heal, fly;
        int painted = -1;
        bool paintedOpen;
        // T18 ① 보드에서 고르기: 칠한 갈 수 있는 마을 칸 · 흐린 칸, 마우스를 올린 칸(보드) · 마우스를 올린 목적지 칩의 칸, 고르기 전 카메라(되돌릴 자리), 날아갔나(그러면 되돌리지 않는다)
        readonly List<int> lit = new List<int>(), dim = new List<int>();
        int hoverCell = -1, chipCell = -1;
        GameFlow.CameraLook home;
        bool flown;

        /// <summary>whistle = bird_keeper B 새 휘슬(마을 밖 칸에서 연 창 — 공중날기 칸만, 감사 G1-1): 제목 「공중날기」 · 회복 · 은행상회 칸 없음 · 처음부터 보드에서 고르기 꼴(<see cref="Picking"/> — [취소] 없이 [닫기] · Esc = 닫기).</summary>
        /// <param name="watch">보는 좌석(결정 120 ① — 남의 은행상회 거래(18행)를 내 창의 은행상회 칸과 같은 꼴로 보기만, −1 = 내 창). 보기 창은 마을 이름 + 은행상회 칸만이고 그 거래 단계가 끝나면 닫힌다.</param>
        public VillageScreen(GameFlow flow, int watch = -1, bool whistle = false)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
            Whistle = whistle;
            Picking = whistle && watch < 0;   // 새 휘슬은 갈 곳 고르기뿐이라 처음부터 보드에서 고른다(T18 ①)
        }

        /// <summary>
        /// T18 ①(플레이 시험 「버튼이 아니라 맵에서 마을 선택하는 식으로」): 공중날기 목적지를 보드에서 고르는 중 — 창은 오른쪽 기둥(공중날기 칸만)으로 비키고 창 밖 가림막이 없다(보드 끌기 · 휠 · Q · E가 산다).
        /// 갈 수 있는 마을은 보드에 칠하고(칠 + 흰 테두리 — <see cref="Lit"/>), 갈 수 없는 마을 · 야영지는 흐리게 덮는다(<see cref="Dim"/>) — 마우스를 올리면 안내 줄 <c>board-village-hint</c>에 까닭(<see cref="WhyAt"/>).
        /// 마을 칸을 누르면 그 목적지(<see cref="ClickCell"/>), 목적지 칩에 올리면 카메라 + 보드 강조(<see cref="HoverDest"/>). 마을 창은 [지도에서 고르기](<see cref="BeginMap"/>)로 들어가
        /// [취소] · Esc(<see cref="EndMap"/>)로 창으로 돌아온다 — 새 휘슬 창(<see cref="Whistle"/>)은 처음부터 이 꼴이고 [닫기] · Esc가 닫는다. 카메라는 고르기 전 자리로 되돌린다(날아가면 그대로).
        /// </summary>
        public bool Picking { get; private set; }

        /// <summary>보드에서 고르는 동안 칠한 갈 수 있는 마을 칸 · 흐리게 덮은 칸(갈 수 없는 마을 · 앞면 야영지).</summary>
        public IReadOnlyList<int> Lit => lit;
        public IReadOnlyList<int> Dim => dim;

        readonly int watch;

        public int Watched => watch;

        /// <summary>보기 창: 그 좌석의 은행상회 단계가 아직 끝나지 않고 남았나.</summary>
        public bool Live
        {
            get
            {
                var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
                if (watch < 0 || seq == null) return false;
                foreach (var entry in seq.Queued)
                {
                    var s = entry.Step(BankBeats.Row);
                    if (s != null && WorkRows.SeatOf(s) == watch && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return true;
                }
                return false;
            }
        }

        /// <summary>마을 밖 칸에서 연 공중날기 창(bird_keeper B).</summary>
        public bool Whistle { get; }

        public string Name => "board-village";

        /// <summary>고른 목적지(마을의 고정물 색인 — 없으면 −1) · 고른 규토리 색(없으면 −1).</summary>
        public int Dest { get; private set; } = -1;
        public int Pay { get; private set; } = -1;

        /// <summary>목적지 줄에 선 마을(고정물 색인, 데이터 순서 — 갈 수 없는 마을 포함).</summary>
        public IReadOnlyList<int> Dests => dests;

        /// <summary>[전체 회복]을 지금 누를 수 있나 · 공중날기로 갈 수 있는 마을이 하나라도 있나(엔진의 합법 답).</summary>
        public bool CanHeal { get; private set; }
        public bool CanFly => flights.Count > 0 && flow.BoardOpen;

        /// <summary>회복 · 공중날기를 못 쓰는 까닭의 문자열 키(쓸 수 있으면 null).</summary>
        public string HealWhy { get; private set; }
        public string FlyWhy { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            if (watch < 0)   // 보기 창은 보드를 덮지 않는다
            {
                scrim = Add(page, new VisualElement(), "board-village-scrim", "sn-party-scrim");
                scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseVillage());   // 창 밖(가운데 필드)을 누르면 닫힌다(보드에서 고르는 동안은 숨는다)
            }
            panel = Add(page, Kit.Panel(stage.Skin), "board-village-panel", "sn-village-panel");
            painted = -1;
            if (Picking) Lay(true);
            Refresh();
            return page;
        }

        public void Close()
        {
            if (Picking) flow.BoardMarks?.Hide();
            if (!flown) flow.RestoreLook(home);   // 날아가지 않고 닫았다: 목적지를 보여 주느라 옮긴 카메라를 되돌린다
            home = default;
            hoverCell = chipCell = -1;
            stage = null;
            panel = scrim = null;
            hint = null;
            heal = fly = null;
            bank?.Close();
        }

        // 보드에서 고르기의 꼴(T18 ①): 오른쪽 기둥 + 가림막 없음 · 고르기 전 카메라를 쥐고 판 전체가 보이게 / 창 꼴 + 카메라 되돌리기
        void Lay(bool map)
        {
            if (scrim != null) scrim.style.display = map ? DisplayStyle.None : DisplayStyle.Flex;
            panel?.EnableInClassList("sn-village-side", map);
            if (map)
            {
                if (!home.Set) home = flow.SaveLook();
                flow.WideFly();
                return;
            }
            hoverCell = chipCell = -1;
            flow.BoardMarks?.Hide();
            flow.RestoreLook(home);
            home = default;
        }

        // 보기 창: 그 좌석이 선 마을 이름 + 은행상회 칸(단추 없음 — 거래 단계를 칸이 그린다)
        void PaintWatch(GameState view)
        {
            if (view == null || painted >= 0) return;
            painted = 0;
            panel.Clear();
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var title = Add(head, new VisualElement(), null, "sn-party-line");
            if (!MapActions.VillagePlace(view, flow.Ctx, view.Seats[watch].Cell, out _, out int village)) village = -1;
            string place = village >= 0 ? stage.T(flow.Ctx.Fixtures.Fixtures[village].Key) : stage.T("screen.bank.title");
            FaceBox.Owner(flow, watch, Add(title, Kit.Text(KoJosa.Fix(stage.T("screen.village.title.seat").Replace("{name}", flow.SeatName(watch)).Replace("{place}", place)), Kit.FTitle), "board-village-title", "kit-title"));
            Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Main, () => flow.CloseWatch()), "board-village-close", null);
            bank = new BankBox(flow, watch);
            bank.Paint(stage, panel, new List<ChoiceAnswer>(), false);
        }

        /// <summary>은행상회 칸(S24).</summary>
        public BankBox Bank => bank;

        public bool Draws(FxStep step) => step != null && step.Row == BankBeats.Row && flow.Client != null && WorkRows.SeatOf(step) == (watch >= 0 ? watch : flow.Client.Seat);

        public void ShowFx(FxSequencer seq) => bank?.Animate(seq);

        /// <summary>그 마을로 갈 수 있나(엔진의 합법 공중날기 답에 있다).</summary>
        public bool CanGo(int village) => flights.Exists(a => a.Target == village);

        /// <summary>그 색의 규토리를 낼 수 있나(엔진의 합법 공중날기 답에 있다).</summary>
        public bool CanPay(int color) => flights.Exists(a => a.Arg == color);

        /// <summary>보기·입력 상태가 바뀌었다: 다시 그린다. 마을 행동을 하는 곳을 떠났거나 내 맵 행동이 닫혔으면 창을 닫는다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null) return;
            var client = flow.Client;
            var view = client.View;
            if (watch >= 0)
            {
                PaintWatch(view);
                return;
            }
            var choice = client.Choice(ChoiceKind.MapAction);
            int village = -1;
            bool here = !Whistle && flow.AtVillage(out _, out village);
            if (view == null || choice == null || (Whistle ? !client.Legal(choice).Exists(a => a.Kind == AnswerKind.Fly) : !here))
            {
                flow.CloseVillage();
                return;
            }
            bool open = flow.BoardOpen;
            if (painted == client.Version && paintedOpen == open) return;
            painted = client.Version;
            paintedOpen = open;
            var ctx = flow.Ctx;
            var me = view.Seats[client.Seat];
            var legal = client.Legal(choice);
            flights.Clear();
            foreach (var a in legal) if (a.Kind == AnswerKind.Fly) flights.Add(a);
            bool grunt = !Whistle && SetupFlow.GruntAt(view, me.Cell);
            if (Dest >= 0 && !CanGo(Dest)) Dest = -1;
            if (Pay >= 0 && !CanPay(Pay)) Pay = -1;
            if (Pay < 0 && flights.Count > 0) Pay = flights[0].Arg;   // 가진 색 가운데 첫 색(등급이 낮은 색부터 — 엔진의 답 순서)
            if (Picking && flights.Count == 0)   // 보드에서 고르다 갈 곳이 없어졌다(남의 답으로 점거 등): 창 꼴로
            {
                Picking = false;
                Lay(false);
                flow.Refold();
            }
            Places(view);

            panel.Clear();
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var title = Add(head, new VisualElement(), null, "sn-party-line");
            string place = Whistle ? "screen.village.fly" : village >= 0 ? ctx.Fixtures.Fixtures[village].Key : "hidden_spot.campsite";
            Add(title, Kit.Text(stage.T(place), Kit.FTitle), "board-village-title", "kit-title");
            if (grunt)
            {
                var badge = Add(title, Kit.Badge(stage.T("screen.village.grunt"), KitBadgeKind.Red, Kit.F2), "board-village-grunt", null);
                stage.Tips.Hook(badge, null, stage.T("screen.token.grunt.tip"), TipSide.Below, reading: true);
            }
            if (Picking && !Whistle) Add(head, Kit.Button(stage.T("screen.village.fly.map.cancel"), KitButtonKind.Main, () => EndMap()), "board-village-cancel", null);   // 보드에서 고르기 → 창으로
            else Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Main, () => flow.CloseVillage()), "board-village-close", null);

            if (!Whistle && !Picking) PaintHeal(view, legal, open);   // 보드에서 고르는 동안은 공중날기 칸만(T18 ①)
            PaintFly(view, legal, open, village, grunt);
        }

        void PaintHeal(GameState view, List<ChoiceAnswer> legal, bool open)
        {
            var client = flow.Client;
            // 전체 회복
            CanHeal = open && legal.Exists(a => a.Kind == AnswerKind.VillageHeal);
            HealWhy = legal.Exists(a => a.Kind == AnswerKind.VillageHeal) ? null : Used(view, client.Seat, UsageKind.VillageHeal) ? "screen.village.used" : "screen.village.cannot";
            var healBox = Add(panel, new VisualElement(), null, "sn-village-box");
            var healHead = Add(healBox, new VisualElement(), null, "sn-village-row");
            var healName = Add(healHead, new VisualElement(), null, "sn-party-line");
            Add(healName, Kit.Text(stage.T("screen.village.heal"), Kit.F2), null, "kit-title");
            Once(healName);
            heal = Add(healHead, Kit.Button(stage.T("screen.village.heal"), KitButtonKind.Main, () => PressHeal()), "board-village-heal", null);
            heal.SetEnabled(CanHeal);
            Add(healBox, Kit.Text(stage.T("screen.village.heal.text"), Kit.F2), null, "kit-muted");
            if (HealWhy != null) Add(healBox, Kit.Text(stage.T(HealWhy), Kit.F2), "board-village-heal-why", "sn-village-why");

            // 은행상회(S24 — 04 §3-4의 순서: 전체 회복 · 은행상회 · 공중날기)
            (bank ?? (bank = new BankBox(flow))).Paint(stage, panel, legal, open);
        }

        void PaintFly(GameState view, List<ChoiceAnswer> legal, bool open, int village, bool grunt)
        {
            var client = flow.Client;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var me = view.Seats[client.Seat];

            // 공중날기
            FlyWhy = flights.Count > 0 ? null
                : Used(view, client.Seat, UsageKind.Fly) ? "screen.village.used"
                : village < 0 ? "screen.village.fly.camp"
                : ctx.Board[me.Cell].DistortionWorld ? "screen.village.cannot"
                : grunt ? "screen.village.fly.grunt"
                : Total(me.Apricorns) == 0 ? "screen.village.fly.no_apricorn"
                : "screen.village.fly.no_dest";
            var flyBox = Add(panel, new VisualElement(), null, "sn-village-box");
            var flyHead = Add(flyBox, new VisualElement(), null, "sn-village-row");
            var flyName = Add(flyHead, new VisualElement(), null, "sn-party-line");
            Add(flyName, Kit.Text(stage.T("screen.village.fly"), Kit.F2), null, "kit-title");
            Once(flyName);
            fly = Add(flyHead, Kit.Button(stage.T("screen.village.fly.go"), KitButtonKind.Main, () => PressFly()), "board-village-fly", null);
            Add(flyBox, Kit.Text(stage.T("screen.village.fly.text"), Kit.F2), null, "kit-muted").AddToClassList("sn-village-text");
            if (FlyWhy != null) Add(flyBox, Kit.Text(stage.T(FlyWhy), Kit.F2), "board-village-fly-why", "sn-village-why");
            if (Picking)
            {
                hint = Add(flyBox, Kit.Text("", Kit.F2), "board-village-hint", "sn-village-hint");   // T18 ①: 보드에서 고르는 안내 · 마우스를 올린 곳의 까닭 한 줄
                ShowHint();
            }

            // 목적지: 보드의 마을(지금 선 마을 · 깨어진 세계의 마을 제외). 갈 수 없는 마을은 흐리게 — 조무래기가 점거한 마을이면 「점거 중」
            var destHead = Add(flyBox, new VisualElement(), null, "sn-village-row");
            Add(destHead, Kit.Text(stage.T("screen.village.fly.dest"), Kit.F2), null, "sn-village-label");
            if (!Picking && flights.Count > 0)   // T18 ①: 보드에서 고르기로(창이 오른쪽으로 비킨다)
            {
                var map = Add(destHead, Kit.Button(stage.T("screen.village.fly.map"), KitButtonKind.Normal, () => BeginMap()), "board-village-map", "sn-village-map-button");
                map.SetEnabled(CanFly);
            }
            var destRow = Add(flyBox, new VisualElement(), null, "sn-village-dests");
            dests.Clear();
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || f == village || ctx.Board[fixtures[f].Cell].DistortionWorld) continue;
                dests.Add(f);
                int target = f;
                bool can = CanGo(f);
                var chip = Add(destRow, new VisualElement(), "board-village-dest-" + fixtures[f].Key.Replace('.', '-'), "sn-village-dest");
                var mark = Add(chip, new VisualElement(), null, "sn-seat-chip");
                mark.style.backgroundColor = new StyleColor((Color)SeatColors.ForVillage(fixtures[f].Key));
                chip.Add(Kit.Text(stage.T(fixtures[f].Key), Kit.F2));
                if (SetupFlow.GruntAt(view, fixtures[f].Cell))
                {
                    chip.Add(Kit.Badge(stage.T("screen.village.fly.dest.grunt"), KitBadgeKind.Red, Kit.F2));
                    stage.Tips.Hook(chip, null, stage.T("screen.token.grunt.tip"), TipSide.Above, reading: true);
                }
                if (can && open)
                {
                    chip.AddToClassList("sn-party-press");
                    stage.OnTap(chip, SoundKeys.Select, e => PickDest(target));
                }
                else chip.AddToClassList("sn-party-no");
                if (f == Dest) chip.AddToClassList("sn-party-picked");
                chip.RegisterCallback<PointerEnterEvent>(e => HoverDest(target));   // T18: 목적지 칩에 올리면 카메라가 그 마을을 보여 준다(보드에서 고르는 중이면 그 칸 강조도)
                chip.RegisterCallback<PointerLeaveEvent>(e => HoverDest(-1));
            }

            // 소모할 규토리: 색마다 아이콘 + 가진 수. 가진 색만 고를 수 있다
            Add(flyBox, Kit.Text(stage.T("screen.village.fly.pay"), Kit.F2), null, "sn-village-label");
            var payRow = Add(flyBox, new VisualElement(), null, "sn-village-dests");
            for (int c = 0; c < me.Apricorns.Length; c++)
            {
                int color = c;
                string colorName = ((ApricornColor)c).ToString().ToLowerInvariant();
                var chip = Add(payRow, new VisualElement(), "board-village-color-" + colorName, "sn-village-pay");
                var icon = Add(chip, new VisualElement(), null, "sn-icon");
                var tex = GenAssets.Load<Texture2D>(EncounterScreen.ApricornKey((ApricornColor)c));
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                chip.Add(Kit.Text(me.Apricorns[c].ToString(inv), Kit.F2));
                stage.Tips.Hook(chip, stage.T("screen.apricorn." + colorName), null, TipSide.Above);
                if (CanPay(c) && open)
                {
                    chip.AddToClassList("sn-party-press");
                    stage.OnTap(chip, SoundKeys.Select, e => PickColor(color));
                }
                else chip.AddToClassList("sn-party-no");
                if (c == Pay && flights.Count > 0) chip.AddToClassList("sn-party-picked");
            }
            fly.SetEnabled(CanFly && Dest >= 0 && Pay >= 0);
            if (Picking && flow.BoardMarks != null) Mark(flow.BoardMarks);
        }

        // 보드에서 고르기의 칠할 칸: 갈 수 있는 마을(합법 Fly 답의 목적지) · 흐린 칸(그 밖의 마을 — 깨어진 세계가 안 보이면 울트라메가로폴리스 빼고 · 앞면 야영지)
        void Places(GameState view)
        {
            lit.Clear();
            dim.Clear();
            var ctx = flow.Ctx;
            var fixtures = ctx.Fixtures.Fixtures;
            foreach (var a in flights)
            {
                int cell = fixtures[a.Target].Cell;
                if (!lit.Contains(cell)) lit.Add(cell);
            }
            bool distortion = flow.DistortionShown;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || CanGo(f) || (ctx.Board[fixtures[f].Cell].DistortionWorld && !distortion)) continue;
                if (!dim.Contains(fixtures[f].Cell)) dim.Add(fixtures[f].Cell);
            }
            int camp = ctx.Items.Ids.IndexOf("hidden_spot.campsite");
            foreach (var t in view.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && !t.Destroyed && t.Def == camp && t.Cell >= 0 && !lit.Contains(t.Cell) && !dim.Contains(t.Cell)) dim.Add(t.Cell);
        }

        // 「턴당 1회」 꼬리표
        void Once(VisualElement line)
        {
            var once = Kit.Badge(stage.T("screen.village.once"), KitBadgeKind.Plain, Kit.F2);
            line.Add(once);
            stage.Tips.Hook(once, null, stage.T("screen.board.used.tip"), TipSide.Below, reading: true);
        }

        static bool Used(GameState view, int seat, UsageKind kind)
        {
            foreach (var u in view.Used)
                if (u.Seat == seat && u.Kind == kind && u.Scope == view.TurnId) return true;
            return false;
        }

        static int Total(int[] counts)
        {
            int n = 0;
            foreach (int c in counts) n += c;
            return n;
        }

        // ── 입력 ──

        /// <summary>[전체 회복]: 그 답을 낸다. 받아들여졌으면 true(창은 그대로 — 단추가 꺼지고 「이번 턴에 이미 사용했습니다.」가 선다).</summary>
        public bool PressHeal()
        {
            if (stage == null || !CanHeal) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.VillageHeal) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>목적지 마을을 고른다(고정물 색인). 갈 수 없는 마을이면 false.</summary>
        public bool PickDest(int village)
        {
            if (stage == null || !CanFly || !CanGo(village)) return false;
            Dest = village;
            painted = -1;
            Refresh();
            if (!(Picking && flow.CellClear(flow.Ctx.Fixtures.Fixtures[village].Cell))) PeekDest(village);   // 보드에서 고르는 중이고 이미 보이면 카메라는 그대로
            return true;
        }

        /// <summary>
        /// T18(결정 126): 공중날기 목적지 마을(고정물 색인)을 카메라로 보여 준다(보는 점만 — 방향 · 줌 그대로 · 고르지는 않는다). 마을이 아니면 false.
        /// 처음 옮길 때의 카메라를 쥐어 두고, 날아가지 않고 창을 닫으면 되돌린다.
        /// </summary>
        public bool PeekDest(int village)
        {
            var fixtures = flow.Ctx.Fixtures.Fixtures;
            if (stage == null || village < 0 || village >= fixtures.Count || fixtures[village].Kind != FixtureKind.Village) return false;
            if (!home.Set) home = flow.SaveLook();
            return flow.PeekCell(fixtures[village].Cell);
        }

        /// <summary>
        /// 목적지 칩에 마우스를 올렸다(고정물 색인 — −1 = 칩을 떠났다): 카메라가 그 마을로 가고, 보드에서 고르는 중이면 그 칸을 강조 · 안내 줄에 그 마을.
        /// 보드에서 고르는 중(전체 줌)에 그 마을이 이미 가리지 않고 보이면 카메라는 두고 강조만 옮긴다(<see cref="GameFlow.CellClear"/>). 돌려주는 값 = 카메라를 옮겼나.
        /// </summary>
        public bool HoverDest(int village)
        {
            var fixtures = flow.Ctx.Fixtures.Fixtures;
            int cell = village >= 0 && village < fixtures.Count && fixtures[village].Kind == FixtureKind.Village ? fixtures[village].Cell : -1;
            bool peeked = cell >= 0 && !(Picking && flow.CellClear(cell)) && PeekDest(village);
            if (cell != chipCell)
            {
                chipCell = cell;
                Repaint();
            }
            return peeked;
        }

        /// <summary>[지도에서 고르기](T18 ①): 보드에서 목적지를 고르는 꼴로 — 창이 오른쪽 기둥으로 비키고 판 전체가 보인다. 공중날기를 지금 쓸 수 없거나 이미 그 꼴이거나 보기 창이면 false.</summary>
        public bool BeginMap()
        {
            if (stage == null || watch >= 0 || Picking || !CanFly) return false;
            stage.Tips.Hide();
            Picking = true;
            Lay(true);
            painted = -1;
            Refresh();
            flow.Refold();   // 보드 카메라 입력을 다시 맞춘다(끌기 · 휠 · Q · E)
            return true;
        }

        /// <summary>보드에서 고르기를 그만두고 창으로 돌아온다([취소] · Esc — 카메라는 고르기 전 자리로). 고르는 중이 아니거나 새 휘슬 창(돌아갈 창 꼴이 없다 — Esc = 닫기)이면 false.</summary>
        public bool EndMap()
        {
            if (stage == null || !Picking || Whistle) return false;
            stage.Tips.Hide();
            Picking = false;
            Lay(false);
            painted = -1;
            Refresh();
            flow.Refold();
            return true;
        }

        /// <summary>보드에서 고르는 중 마우스가 그 칸 위에 있다(−1 = 보드 밖 · 창 위): 갈 수 있는 마을이면 강조, 마을 · 야영지면 안내 줄에 이름과 까닭.</summary>
        public void HoverCell(int cell)
        {
            if (!Picking) return;
            int at = PlaceAt(cell) != null ? cell : -1;
            if (at == hoverCell) return;
            hoverCell = at;
            Repaint();
        }

        /// <summary>보드에서 고르는 중 그 칸을 눌렀다: 갈 수 있는 마을이면 그 목적지를 고르고 true(카메라는 그대로 — 누른 칸이 이미 보인다). 그 밖이면 아무것도 하지 않고 false.</summary>
        public bool ClickCell(int cell)
        {
            if (!Picking || stage == null || !CanFly || cell < 0) return false;
            var fixtures = flow.Ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || fixtures[f].Cell != cell || !CanGo(f)) continue;
                Dest = f;
                painted = -1;
                Refresh();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 보드에서 고르는 동안의 칠하기(<see cref="BoardHud"/>의 이동 표시 자리가 부른다): 갈 수 있는 마을 = 옅은 칠 + 흰 테두리, 마우스를 올린 마을(칩 · 보드) = 금색 칠,
        /// 고른 목적지 = 금색 테두리, 갈 수 없는 마을 · 야영지 = 흐린 칠.
        /// </summary>
        public void Mark(BoardMarks marks)
        {
            if (marks == null) return;
            int over = chipCell >= 0 ? chipCell : hoverCell;
            if (!lit.Contains(over)) over = -1;
            int picked = Dest >= 0 ? flow.Ctx.Fixtures.Fixtures[Dest].Cell : -1;
            int show = over >= 0 ? over : picked;
            var rings = lit.FindAll(c => c != show);
            marks.Show(lit, null, show, over < 0 && picked >= 0, rings, null, dim);
        }

        /// <summary>
        /// 그 칸(마을 · 앞면 야영지)으로 날 수 없는 까닭의 문자열 키 — 갈 수 있는 마을이거나 마을 · 야영지가 아니면 null.
        /// 지금 선 마을 · 울트라메가로폴리스 · 조무래기 점거 · 야영지 · 그 밖(지금은 갈 수 없음).
        /// </summary>
        public string WhyAt(int cell)
        {
            if (cell < 0 || flow.Client == null || flow.Client.View == null) return null;
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || fixtures[f].Cell != cell) continue;
                if (CanGo(f)) return null;
                if (view.Seats[flow.Client.Seat].Cell == cell) return "screen.village.fly.why.here";
                if (ctx.Board[cell].DistortionWorld) return "screen.village.fly.why.megalopolis";
                if (SetupFlow.GruntAt(view, cell)) return "screen.village.fly.why.grunt";
                return "screen.village.fly.why.cannot";
            }
            return dim.Contains(cell) ? "screen.village.fly.why.camp" : null;
        }

        // 그 칸의 마을 · 앞면 야영지 이름 키(칠한 칸 · 흐린 칸만 — 아니면 null)
        string PlaceAt(int cell)
        {
            if (cell < 0 || (!lit.Contains(cell) && !dim.Contains(cell))) return null;
            foreach (var f in flow.Ctx.Fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village && f.Cell == cell) return f.Key;
            return "hidden_spot.campsite";
        }

        /// <summary>안내 줄의 지금 글(보드에서 고르는 중이 아니면 null).</summary>
        public string HintText => hint != null ? hint.text : null;

        void ShowHint()
        {
            if (hint == null || stage == null) return;
            int at = chipCell >= 0 ? chipCell : hoverCell;
            string place = PlaceAt(at);
            if (place == null)
            {
                hint.text = stage.T("screen.village.fly.map.hint");
                hint.EnableInClassList("sn-village-hint-no", false);
                return;
            }
            string why = WhyAt(at);
            hint.text = stage.T(why ?? "screen.village.fly.map.pick").Replace("{place}", stage.T(place));
            hint.EnableInClassList("sn-village-hint-no", why != null);
        }

        // 안내 줄 · 보드 칠하기만 다시(창 전체를 다시 그리지 않는다 — 마우스를 따라 자주 바뀐다)
        void Repaint()
        {
            ShowHint();
            if (Picking && flow.BoardMarks != null && flow.FlyPick == this) Mark(flow.BoardMarks);
        }

        /// <summary>소모할 규토리 색을 고른다. 가지지 않은 색이면 false.</summary>
        public bool PickColor(int color)
        {
            if (stage == null || !CanFly || !CanPay(color)) return false;
            Pay = color;
            painted = -1;
            Refresh();
            return true;
        }

        /// <summary>[출발]: 고른 목적지 · 규토리 색으로 공중날기 답을 낸다. 받아들여졌으면 창을 닫고 true.</summary>
        public bool PressFly()
        {
            if (stage == null || !CanFly || Dest < 0 || Pay < 0) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var answer = flights.Find(a => a.Target == Dest && a.Arg == Pay);
            if (choice == null || answer == null) return false;
            stage.Tips.Hide();
            var gameFlow = flow;
            flown = true;   // 받아들여지면 카메라는 날아가는 말을 따라간다 — 닫을 때 되돌리지 않는다
            if (!gameFlow.Answer(choice, answer).Accepted)
            {
                flown = false;
                return false;
            }
            gameFlow.CloseVillage();
            return true;
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
