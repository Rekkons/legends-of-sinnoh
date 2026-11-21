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
    /// S12 스타팅 포켓몬 + S13 트레이너 카드 고르기(04 §1·§2·§7 — 목업 r4/setup ③, A20-1, 결정 25): 엔진이 한 묶음으로 연 두 선택
    /// (<see cref="ChoiceKind.StarterPick"/> — 고른 마을의 둘 중 하나, <see cref="ChoiceKind.TrainerCardPick"/> — 받은 3장 중 하나)의 합법 답을 그대로 올리고, [확정]이 둘을 함께 낸다.
    /// 트레이너 카드를 끈 판이면 스타팅 포켓몬만 고른다(A20-8). 보드 위를 어둡게 덮는다.
    /// 포켓몬 칸 = 도트 · 이름 · 타입 · 체력·스피드 · 고유 기술(이름 · 타입 · 위력 — 효과 문구는 마우스 오버),
    /// 트레이너 카드 칸 = 직업 그림 · 직업 이름 · 능력 둘(파랑·빨강)의 이름과 그 아래 효과 문구(결정 56 ② — 칸에 바로 적는다. 문구가 가장 긴 무녀 카드도 칸 안에 들어가게 칸 폭을 넓혔다).
    /// 글은 문자열 표(<c>pokemon.*</c>·<c>move.*</c>·<c>trainer_card.*</c>·<c>trainer_ability.*</c>·<c>ui.types.*</c>), 값은 카드 데이터(<see cref="CardDb"/>), 그림은 Gen.
    /// <para>연출(04 §8-8 1·3행, 결정 66 ③ — 모습은 화면 대기열의 시각에서만 나온다, <see cref="Animate"/>): 화면이 열리면 트레이너 카드 3장이 덱 쪽(오른쪽)에서 뒷면으로 어긋나게 미끄러져 들어와 앞면으로 뒤집힌다(<see cref="DealBeats"/>).
    /// [확정] 뒤 고르지 않은 것은 덱 쪽으로 쓸려 가고(<see cref="ChosenBeats"/>) 고른 포켓몬은 볼로 가려진다 — 고른 카드는 앞면 그대로 남는다(결정 104 ③ — 내 화면에서 내 카드를 뒷면으로 뒤집었다 다시 뒤집지 않는다. 남의 것은 좌석 줄이 모두 고를 때까지 가린다).
    /// 이어서 볼이 흔들리다 튀어 오르며 열리고 흰 빛이 걷히며 도트가 나타났다 볼로 줄어 아래로 떨어지고(<see cref="StarterBeats"/>), 카드가 들리고 금속광이 한 번 지나간 뒤 내려앉는다(<see cref="RevealBeats"/> — 뒤집기 값 <c>ScaleX</c>·<c>Face</c>는 쓰지 않는다).
    /// 그 연출이 끝나면 화면 흐름이 이 화면을 닫고 보드 HUD를 연다. 연출이 도는 동안에는 고르기·[확정]이 듣지 않는다(<see cref="GameFlow.SetupOpen"/>).
    /// 카드 뒷면은 원작 뒷면의 테두리·볼 문양을 세로 칸에 맞춰 다시 짠 것(결정 70 ④ A8 ②), 볼은 볼 던지기 그림(<c>Stage/Ball/poke_ball*.png</c>)이다 [가정]. 내 판의 카드 칸·파티 칸은 아직 없어(HUD는 셋업 뒤에 뜬다) 내려앉는 것은 제자리에서 줄어들며 사라진다 [가정].</para>
    /// 요소 이름: <c>setup-pick</c>·<c>setup-pick-starters</c>·<c>setup-pick-starter-&lt;키&gt;</c>·<c>setup-pick-cards</c>·<c>setup-pick-card-&lt;키&gt;</c>(+ <c>-ability-&lt;1|2&gt;</c>·<c>-ability-&lt;1|2&gt;-text</c>·<c>-back</c>)·<c>setup-pick-ball-&lt;n&gt;</c>·<c>setup-pick-confirm</c>.
    /// </summary>
    public sealed class SetupPickScreen : IUiScreen
    {
        readonly GameFlow flow;
        readonly List<int> starters = new List<int>(), cards = new List<int>();
        readonly List<VisualElement> starterTiles = new List<VisualElement>(), cardTiles = new List<VisualElement>();
        // 연출 부품(칸마다): 스타팅 포켓몬 = 도트 · 볼을 옮기는 틀 · 닫힌 볼 · 열린 볼 · 흰 빛, 트레이너 카드 = 뒷면 · 금속광
        readonly List<VisualElement> monArts = new List<VisualElement>(), ballMoves = new List<VisualElement>(), balls = new List<VisualElement>(), opens = new List<VisualElement>(), flashes = new List<VisualElement>();
        /// <summary>트레이너 카드 뒷면 그림(Gen <c>Cards/Trainer</c> — cards 묶음이 원작 브론즈 뒷면에서 만든다): 9칸으로 늘이는 테두리 · 가운데 볼 문양.</summary>
        public const string BackFrameKey = "Cards/Trainer/back_frame.png", BackEmblemKey = "Cards/Trainer/back_emblem.png";

        readonly List<VisualElement> backs = new List<VisualElement>(), glosses = new List<VisualElement>();
        // 좌석 줄(99행 — 좌석이 둘 이상인 판만): 좌석마다 칩 하나(얼굴 상자 + 글)
        readonly List<VisualElement> seatChips = new List<VisualElement>();
        readonly List<Label> seatTexts = new List<Label>();

        /// <summary>좌석 줄에서 「선택 완료」로 적힌 좌석 수 · 고른 것이 드러난(앞면) 좌석 수 — 확인·시험이 본다(한 좌석 판은 줄이 없어 0).</summary>
        public int SeatsDone { get; private set; }
        public int SeatsRevealed { get; private set; }
        UiStage stage;
        SheetLayer sheets;

        /// <summary>도트 이펙트 판(볼 열림 빛 — 결정 69).</summary>
        public SheetLayer Sheets => sheets;
        Button confirm;
        Label guide;

        /// <summary>볼 던지기 그림(Gen — 닫힌 볼 시트 16px × 9장 가로 · 열린 볼 32px).</summary>
        public const string BallKey = "Stage/Ball/poke_ball.png", BallOpenKey = "Stage/Ball/poke_ball_open.png";

        /// <summary>고르지 않은 칸이 덱 쪽으로 쓸려 가는 거리 · 받은 카드가 부채꼴로 어긋나는 세로 폭 · 볼이 흔들리는 폭 · 튀는 높이 · 떨어지는 거리(720 벌 px — 1080 벌은 ×1.5).</summary>
        public const float AwayPx = 80f, FanPx = 24f, WobblePx = 4f, HopPx = 28f, DropPx = 48f;
        /// <summary>공개 때 카드가 들리는 배수.</summary>
        public const float LiftScale = 1.06f;

        /// <summary>[확정]으로 답을 냈나(스타팅 포켓몬 · 트레이너 카드) — 낸 뒤에는 다시 고르지 못하고 연출만 남는다.</summary>
        public bool StarterSent { get; private set; }
        public bool CardSent { get; private set; }

        /// <summary>지금 앞면이 보이는 트레이너 카드 수 · 닫힌 볼이 보이나 · 고른 스타팅 포켓몬의 도트가 보이는 크기(0 ~ 1) — 확인·시험이 본다.</summary>
        public int CardsFaceUp { get; private set; }
        public bool BallShown { get; private set; }
        public float MonShown { get; private set; }

        public SetupPickScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "setup-pick";

        /// <summary>고를 수 있는 스타팅 포켓몬(pokemon 색인)과 트레이너 카드(trainer_cards 색인) — 엔진의 합법 답 순서. 트레이너 카드를 끈 판이면 카드는 비어 있다.</summary>
        public IReadOnlyList<int> Starters => starters;
        public IReadOnlyList<int> Cards => cards;

        /// <summary>지금 고른 것(<see cref="Starters"/>·<see cref="Cards"/> 색인). 아직 없으면 −1.</summary>
        public int Starter { get; private set; } = -1;
        public int Card { get; private set; } = -1;

        /// <summary>[확정]을 누를 수 있나: 스타팅 포켓몬을 골랐고, 트레이너 카드가 있는 판이면 카드도 골랐다.</summary>
        public bool CanConfirm => Starter >= 0 && (cards.Count == 0 || Card >= 0);

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            int wasStarter = Starter, wasCard = Card;
            Starter = Card = -1;
            starterTiles.Clear();
            cardTiles.Clear();
            cardRow = null;
            monArts.Clear();
            ballMoves.Clear();
            balls.Clear();
            opens.Clear();
            flashes.Clear();
            backs.Clear();
            glosses.Clear();
            seatChips.Clear();
            seatTexts.Clear();
            var client = flow.Client;
            var ctx = flow.Ctx;
            var starterChoice = client.Choice(ChoiceKind.StarterPick);
            var cardChoice = client.Choice(ChoiceKind.TrainerCardPick);
            // 답을 낸 뒤(연출 중)에 다시 만들 때(패널이 뿌리를 다시 준다)는 선택이 이미 닫혀 있다 — 보여 주던 목록을 그대로 쓴다
            if (!StarterSent)
            {
                starters.Clear();
                if (starterChoice != null) foreach (var a in client.Legal(starterChoice)) starters.Add(a.Target);
            }
            if (!CardSent)
            {
                cards.Clear();
                if (cardChoice != null) foreach (var a in client.Legal(cardChoice)) cards.Add(a.Target);
            }

            var page = new VisualElement();
            Add(page, new VisualElement(), null, "kit-scrim").pickingMode = PickingMode.Ignore;
            var col = Add(page, new VisualElement(), "setup-pick-col", "sn-pick-col");
            Add(col, Kit.Text(stage.T("screen.setup.title"), Kit.FTitle), null, "sn-head");
            guide = Add(col, Kit.Text(GuideText(), Kit.F2), "setup-pick-guide", "sn-guide");

            var area = Add(col, new VisualElement(), null, "sn-pick-area");
            var mons = MonArt.Load();
            var heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            var speed = GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey);
            var inv = CultureInfo.InvariantCulture;
            var ballTex = GenAssets.Load<Texture2D>(BallKey);
            var openTex = GenAssets.Load<Texture2D>(BallOpenKey);

            var starterPanel = Add(area, Kit.Panel(stage.Skin), "setup-pick-starters", "sn-pick-panel");
            Add(starterPanel, Kit.Text(stage.T("screen.setup.pick.starter"), Kit.F2), null, "kit-title");
            var starterRow = Add(starterPanel, new VisualElement(), null, "sn-pick-tiles");
            for (int i = 0; i < starters.Count; i++)
            {
                int index = i;
                var def = ctx.Cards.Pokemon[starters[i]];
                var tile = Add(starterRow, new VisualElement(), "setup-pick-starter-" + def.Key.Replace(':', '-'), "sn-pick-tile");
                var art = mons != null ? mons.Front(def.Key) : new VisualElement();
                tile.Add(art);
                monArts.Add(art);
                Add(tile, Kit.Text(stage.T("pokemon." + def.Key), Kit.F2), null, "sn-pick-name");
                var types = Add(tile, new VisualElement(), null, "sn-pick-line");
                foreach (var t in def.Types) types.Add(Kit.Badge(stage.T("ui.types." + t), KitBadgeKind.Plain, Kit.F2));
                var stats = Add(tile, new VisualElement(), null, "sn-pick-line");
                var hp = Kit.Pill(heart, def.Health.ToString(inv));
                var sp = Kit.Pill(speed, def.Initiative.ToString(inv));
                stats.Add(hp);
                stats.Add(sp);
                stage.Tips.Hook(hp, stage.T("screen.common.health"), null, TipSide.Above);
                stage.Tips.Hook(sp, stage.T("screen.common.speed"), null, TipSide.Above);
                var move = ctx.Cards.Moves[def.Signature];
                // 고유 기술: 이름 한 줄 + 타입·위력 한 줄(효과 문구는 마우스 오버)
                var moveBox = Add(tile, new VisualElement(), "setup-pick-starter-" + def.Key.Replace(':', '-') + "-move", "sn-pick-move");
                Add(moveBox, Kit.Text(stage.T("move." + move.Key), Kit.F2), null, null);
                var moveLine = Add(moveBox, new VisualElement(), null, "sn-pick-line");
                moveLine.Add(Kit.Badge(stage.T("ui.types." + move.Type), KitBadgeKind.Plain, Kit.F2));
                moveLine.Add(Kit.Badge(stage.T("screen.battle.power") + " " + (move.AttackStrength.HasValue ? move.AttackStrength.Value.ToString(inv) : "?"), KitBadgeKind.Gold, Kit.F2));
                stage.Tips.Hook(moveBox, stage.T("move." + move.Key), stage.T("move." + move.Key + ".text"), TipSide.Below, reading: true);   // 효과 문장은 본문 크기로(1080 벌 24px)
                // 볼(도트 자리 위 — 연출 때만 보인다): 흰 빛 · 열린 볼 · 닫힌 볼
                var ballBox = Add(tile, new VisualElement { pickingMode = PickingMode.Ignore }, "setup-pick-ball-" + i.ToString(inv), "sn-pick-ballbox");
                var flash = Add(ballBox, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-flash");
                var stack = Add(ballBox, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-ballstack");
                var open = Add(stack, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-ball-open");
                if (openTex != null) open.style.backgroundImage = new StyleBackground(openTex);
                var ballMove = Add(stack, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-ballmove");
                var ball = Add(ballMove, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-ball");
                var sheet = Add(ball, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-ball-sheet");
                if (ballTex != null) sheet.style.backgroundImage = new StyleBackground(ballTex);
                flashes.Add(flash);
                opens.Add(open);
                ballMoves.Add(ballMove);
                balls.Add(ball);
                stage.OnTap(tile, SoundKeys.Select, e => PickStarter(index), () => !StarterSent && flow.SetupOpen);
                starterTiles.Add(tile);
            }

            if (cards.Count > 0)
            {
                var cardPanel = Add(area, Kit.Panel(stage.Skin), "setup-pick-cards", "sn-pick-panel");
                var title = Add(cardPanel, Kit.Text(stage.T("screen.setup.pick.card"), Kit.F2), null, "kit-title");
                stage.Tips.Hook(title, null, stage.T("screen.setup.pick.card.tip"), TipSide.Below, reading: true);
                cardRow = Add(cardPanel, new VisualElement(), "setup-pick-card-row", "sn-pick-tiles");
                for (int i = 0; i < cards.Count; i++)
                {
                    int index = i;
                    var def = ctx.Cards.TrainerCards[cards[i]];
                    var tile = Add(cardRow, new VisualElement(), "setup-pick-card-" + def.Key, "sn-pick-tile");
                    tile.AddToClassList("sn-pick-card");
                    var face = Add(tile, new VisualElement(), null, "sn-trainer-face");
                    var portrait = Add(face, new VisualElement(), null, "sn-trainer-art");
                    string frontKey = BoardPieces.FrontKey(def.Key);
                    var tex = frontKey != null ? GenAssets.Load<Texture2D>(frontKey) : null;
                    if (tex != null) portrait.style.backgroundImage = new StyleBackground(tex);
                    Add(tile, Kit.Text(stage.T("trainer_card." + def.Key), Kit.F2), null, "sn-pick-name");
                    foreach (var ability in def.Abilities)
                    {
                        string key = "trainer_ability." + def.Key + "." + ability.Slot.ToString(inv);
                        // 능력 하나 = 이름 줄(색 칸 + 이름) + 그 아래 효과 문구(여러 줄로 감긴다 — 툴팁 없이 바로 읽는다)
                        string name = "setup-pick-card-" + def.Key + "-ability-" + ability.Slot.ToString(inv);
                        var block = Add(tile, new VisualElement(), name, "sn-pick-ability");
                        var line = Add(block, new VisualElement(), null, "sn-pick-line");
                        Add(line, new VisualElement(), null, ability.Color == AbilityColor.Blue ? "sn-ability-blue" : "sn-ability-red");
                        line.Add(Kit.Text(stage.T(key), Kit.F2));
                        Add(block, Kit.Wrapped(stage.T(key + ".text"), Kit.F2), name + "-text", "sn-pick-ability-text");
                    }
                    // 뒷면(결정 70 ④ A8 ②): 원작 뒷면에서 딴 테두리(9칸으로 늘인다)와 가운데 볼 문양 — Gen이 없으면 짙은 판만
                    var back = Add(tile, new VisualElement { pickingMode = PickingMode.Ignore }, "setup-pick-card-" + def.Key + "-back", "sn-pick-back");
                    var emblem = Add(back, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-back-in");
                    var frameTex = GenAssets.Load<Texture2D>(BackFrameKey);
                    var emblemTex = GenAssets.Load<Texture2D>(BackEmblemKey);
                    if (frameTex != null) back.style.backgroundImage = new StyleBackground(frameTex);
                    if (emblemTex != null) emblem.style.backgroundImage = new StyleBackground(emblemTex);
                    backs.Add(back);
                    glosses.Add(Add(tile, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-pick-gloss"));
                    stage.OnTap(tile, SoundKeys.Select, e => PickCard(index), () => !CardSent && flow.SetupOpen);
                    cardTiles.Add(tile);
                }
            }

            var foot = Add(col, new VisualElement(), "setup-pick-foot", "sn-foot-row");
            confirm = Add(foot, Kit.Button(stage.T("screen.common.confirm"), KitButtonKind.Main, () => Confirm()), "setup-pick-confirm", null);
            // 좌석 줄(04 §8-8 99행 — 좌석이 둘 이상인 판): 고르는 동안 「고르는 중」/「선택 완료」만, 모두 고르면 한꺼번에 뒤집혀 고른 스타팅 포켓몬이 드러난다(RevealAllBeats)
            int seatCount = client.View != null ? client.View.Seats.Length : 0;
            if (seatCount > 1)
            {
                var seatRow = Add(col, new VisualElement(), "setup-pick-seats", "sn-pick-seats");
                for (int s = 0; s < seatCount; s++)
                {
                    var chip = Add(seatRow, Kit.Panel(stage.Skin), "setup-pick-seat-" + (s + 1).ToString(inv), "sn-pick-seat");
                    var face = Add(chip, FaceBox.Make(), "setup-pick-seat-" + (s + 1).ToString(inv) + "-face", null);
                    FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
                    seatTexts.Add(Add(chip, Kit.Text("", Kit.F2), "setup-pick-seat-" + (s + 1).ToString(inv) + "-text", "sn-pick-seat-text"));
                    seatChips.Add(chip);
                }
            }
            if (wasStarter >= 0 && wasStarter < starters.Count) Mark(starterTiles, wasStarter, true);
            if (wasCard >= 0 && wasCard < cards.Count) Mark(cardTiles, wasCard, false);
            // 도트 이펙트 판(결정 69 — 볼 열림 빛): 화면 맨 위, 마우스를 받지 않는다
            sheets = new SheetLayer("setup-pick-sheets");
            page.Add(sheets.Element);
            painted = false;
            Animate();
            return page;
        }

        public void Close() { }

        string GuideText() => stage.T(StarterSent ? "screen.setup.pick.done" : cards.Count > 0 ? "screen.setup.pick.guide" : "screen.setup.pick.guide_starter");

        // 다시 만든 화면에 고른 칸을 잇는다(입력이 닫혀 있어도)
        void Mark(List<VisualElement> tiles, int index, bool starter)
        {
            tiles[index].AddToClassList("sn-picked");
            if (starter) Starter = index;
            else Card = index;
        }

        /// <summary>스타팅 포켓몬 칸을 고른다(눌렀다). 아직 답을 내지 않는다. 연출이 도는 동안 · 답을 낸 뒤에는 듣지 않는다.</summary>
        public void PickStarter(int index)
        {
            if (index < 0 || index >= starterTiles.Count || StarterSent || !flow.SetupOpen) return;
            if (Starter >= 0) starterTiles[Starter].RemoveFromClassList("sn-picked");
            Starter = index;
            starterTiles[index].AddToClassList("sn-picked");
            confirm.SetEnabled(CanConfirm);
        }

        /// <summary>트레이너 카드 칸을 고른다(눌렀다). 아직 답을 내지 않는다. 연출이 도는 동안 · 답을 낸 뒤에는 듣지 않는다.</summary>
        public void PickCard(int index)
        {
            if (index < 0 || index >= cardTiles.Count || CardSent || !flow.SetupOpen) return;
            if (Card >= 0) cardTiles[Card].RemoveFromClassList("sn-picked");
            Card = index;
            cardTiles[index].AddToClassList("sn-picked");
            confirm.SetEnabled(CanConfirm);
        }

        bool painted, paintedOpen;

        /// <summary>
        /// 그 순간의 모습으로 그린다(화면 대기열이 프레임마다 · 화면을 맞출 때 부른다). 시각은 대기열의 것뿐이다 — 같은 항목·같은 ms면 같은 그림이다.
        /// 연출에 걸리지 않은 칸은 손대지 않은 모습(USS — 마우스를 올리면 들린다)으로 돌려놓는다.
        /// </summary>
        /// <summary>트레이너 카드가 들어오는 동안 카드 줄(<c>setup-pick-card-row</c>)에 붙는 USS 클래스 — 줄 밖을 잘라 낸다.</summary>
        public const string DealClip = "sn-pick-clip";

        VisualElement cardRow;

        public void Animate()
        {
            if (stage == null || confirm == null) return;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            bool open = flow.SetupOpen && !StarterSent;
            if (!painted || open != paintedOpen)
            {
                painted = true;
                paintedOpen = open;
                confirm.SetEnabled(open && CanConfirm);
                guide.text = GuideText();
            }
            bool past = flow.Client == null || flow.Client.View == null || flow.Client.View.Phase != Phase.Setup;   // 모두 골랐다 — 공개 단계가 대기열에 없으면 이미 지난 것이다
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            float width = stage.Root != null && !float.IsNaN(stage.Root.layout.width) && stage.Root.layout.width > 0f ? stage.Root.layout.width : 1280f * unit;

            // 스타팅 포켓몬: 고른 뒤(11행) → 공개(30행)
            int choseMs = SetupRows.Ms(SetupRows.At(seq, SetupRows.Chosen, ChoiceKind.StarterPick, out var choseStep), choseStep, StarterSent, PlayTiming.RowMs(null, SetupRows.Chosen), out int choseTotal);
            int showMs = SetupRows.Ms(SetupRows.At(seq, SetupRows.Starter, null, out var showStep), showStep, StarterSent && past, PlayTiming.RowMs(null, SetupRows.Starter), out int showTotal);
            var chose = StarterSent ? ChosenBeats.At(choseMs, choseTotal) : default;
            var show = StarterBeats.At(showMs, showTotal);
            BallShown = false;
            MonShown = 1f;
            sheets?.Begin();
            for (int i = 0; i < starterTiles.Count; i++)
            {
                var tile = starterTiles[i];
                bool mine = StarterSent && i == Starter;
                if (StarterSent && !mine) Shift(tile, chose.Away * AwayPx * unit, 0f, 1f, 1f, 1f - chose.Away);
                else Rest(tile);
                float mon = !mine ? 1f : showMs >= 0 ? show.Mon : 1f - chose.Hide;
                bool ball = mine && (showMs >= 0 ? show.Ball : chose.Hide >= 1f);
                FxUi.Scale(monArts[i], mon, mon);
                FxUi.Fade(monArts[i], mon > 0f ? 1f : 0f);
                FxUi.Fade(balls[i], ball ? 1f - show.Drop : 0f);
                FxUi.Move(ballMoves[i], (showMs >= 0 ? show.Wobble : chose.Wobble) * WobblePx * unit, (show.Drop * DropPx - show.Hop * HopPx) * unit);
                FxUi.Fade(opens[i], mine ? show.Open : 0f);
                // 흰 빛: 볼이 열릴 때(30행)와 볼로 들어가는 순간(11행 — 가려지는 끝 무렵 잠깐)
                FxUi.Fade(flashes[i], !mine ? 0f : showMs >= 0 ? show.Flash : chose.Hide > 0f && chose.Hide < 1f ? chose.Hide * 0.8f : 0f);
                if (mine)
                {
                    // 볼 열림 빛(결정 69): 볼이 열릴 때 볼 자리에서 터진다
                    var box = flashes[i].worldBound;
                    if (showMs >= 0 && sheets != null && !float.IsNaN(box.width) && box.width > 0f)
                    {
                        var at = sheets.Element.WorldToLocal(box.center);
                        sheets.Add(SheetBeats.Open, showMs - SheetBeats.StarterOpenMs(showTotal), Mathf.Round(at.x), Mathf.Round(at.y), stage.Grade.TokenHeight >= 1080 ? 3f : 2f);
                    }
                    BallShown = ball && show.Drop < 1f;
                    MonShown = mon;
                }
            }

            sheets?.End();

            // 좌석 줄(99행): 답을 다 낸 좌석은 「선택 완료」, 공개 단계에서 좌석마다 60ms 어긋나게 뒤집혀 고른 스타팅 포켓몬의 이름으로
            SeatsDone = SeatsRevealed = 0;
            if (seatChips.Count > 0)
            {
                int need = cards.Count > 0 ? 2 : 1;
                int allMs = SetupRows.Ms(SetupRows.At(seq, RevealAllBeats.Row, null, out var allStep), allStep, past, PlayTiming.RowMs(null, RevealAllBeats.Row), out _);
                var view = flow.Client != null ? flow.Client.View : null;
                for (int s = 0; s < seatChips.Count; s++)
                {
                    bool done = flow.SecretAnswers(s) >= need || past;
                    float sx = RevealAllBeats.Flip(allMs, s, out bool up);
                    string text = stage.T(done ? "screen.setup.seat.done" : "screen.setup.seat.picking");
                    if (up && view != null && s < view.Seats.Length)
                    {
                        int id = view.Seats[s].Party.Length > 0 ? view.Seats[s].Party[0] : -1;
                        if (id >= 0 && id < view.Pokemon.Count) text = stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[id].Def].Key);
                    }
                    seatTexts[s].text = text;
                    seatTexts[s].EnableInClassList("sn-pick-seat-done", done && !up);
                    FxUi.Scale(seatChips[s], Mathf.Max(0.02f, sx), 1f);
                    if (up) SeatsRevealed++;
                    else if (done) SeatsDone++;
                }
            }

            // 트레이너 카드: 받기(10행) → 고른 뒤(11행) → 공개(12행)
            int dealAt = SetupRows.At(seq, SetupRows.Deal, null, out var dealStep);
            int dealMs = SetupRows.Ms(dealAt, dealStep, true, 0, out int dealTotal);
            bool dealing = dealAt != SetupRows.Gone && dealMs < dealTotal;
            // 들어오는 동안에는 카드를 판 안에서 잘라 낸다(결정 70 ④ — 판 밖 오른쪽으로 그려지지 않게). 다 들어온 뒤에는 풀어 둔다: 마우스를 올린 카드·공개 때 들리는 카드는 줄 밖으로 나간다
            cardRow?.EnableInClassList(DealClip, dealing);
            int lockMs = SetupRows.Ms(SetupRows.At(seq, SetupRows.Chosen, ChoiceKind.TrainerCardPick, out var lockStep), lockStep, CardSent, PlayTiming.RowMs(null, SetupRows.Chosen), out int lockTotal);
            int openMs = SetupRows.Ms(SetupRows.At(seq, SetupRows.Reveal, null, out var openStep), openStep, CardSent && past, PlayTiming.RowMs(null, SetupRows.Reveal), out int openTotal);
            var locked = CardSent ? ChosenBeats.At(lockMs, lockTotal) : default;
            var reveal = RevealBeats.At(openMs, openTotal);
            CardsFaceUp = 0;
            for (int i = 0; i < cardTiles.Count; i++)
            {
                var tile = cardTiles[i];
                bool face = true;
                float gloss = -1f, alpha = 1f;
                if (dealing)
                {
                    var pose = DealBeats.Card(i, dealMs, dealTotal);
                    face = pose.Face;
                    alpha = pose.Shown ? 1f : 0f;
                    Shift(tile, pose.Slide * width, pose.Slide * (i - (cardTiles.Count - 1) * 0.5f) * FanPx * unit, pose.ScaleX, 1f, alpha);
                }
                else if (!CardSent) Rest(tile);
                else if (i != Card)
                {
                    alpha = 1f - locked.Away;
                    Shift(tile, locked.Away * AwayPx * unit, 0f, 1f, 1f, alpha);
                }
                else
                {
                    // 고른 카드: 내가 고른 것을 나는 이미 안다 — 뒷면으로 가렸다가 공개 때 다시 뒤집지 않는다(결정 104 ③: 「이미 선택한게 뒤집혔다 한바퀴 더 도는 연출」을 뺌).
                    // 앞면 그대로 기다리다 공개 때 들리고(12행) 금속광이 한 번 지나간 뒤 내려앉는다. 남에게 가리는 것은 좌석 줄(「선택 완료」 → 공개 때 뒤집힘)의 몫
                    float lift = 1f + (LiftScale - 1f) * reveal.Lift * (1f - reveal.Land), size = lift * (1f - 0.4f * reveal.Land);
                    alpha = 1f - reveal.Land;
                    Shift(tile, 0f, 0f, size, size, alpha);
                    gloss = reveal.Gloss;
                }
                backs[i].style.display = face ? DisplayStyle.None : DisplayStyle.Flex;
                if (face && alpha > 0f) CardsFaceUp++;
                var bar = glosses[i];
                bar.style.display = gloss >= 0f ? DisplayStyle.Flex : DisplayStyle.None;
                if (gloss >= 0f)
                {
                    float w = !float.IsNaN(tile.layout.width) && tile.layout.width > 0f ? tile.layout.width : 250f * unit;
                    FxUi.Move(bar, Mathf.Lerp(-0.4f * w, w, gloss), 0f);
                }
            }
        }

        // 연출에 걸린 칸: 옮기기·늘이기·흐리기(04 §8-6). 걸리지 않은 칸은 USS의 모습으로(마우스 오버가 듣게 인라인 값을 지운다)
        static void Shift(VisualElement e, float x, float y, float sx, float sy, float opacity)
        {
            FxUi.Move(e, x, y);
            FxUi.Scale(e, sx, sy);
            FxUi.Fade(e, opacity);
        }

        static void Rest(VisualElement e)
        {
            e.style.translate = StyleKeyword.Null;
            e.style.scale = StyleKeyword.Null;
            e.style.opacity = StyleKeyword.Null;
        }

        /// <summary>
        /// [확정]: 고른 스타팅 포켓몬과 트레이너 카드를 엔진에 낸다(묶음이라 둘 다 답해야 넘어간다 — 그 사이 화면은 그대로다). 덜 골랐거나 거절되면 false.
        /// 연출이 도는 동안(카드가 들어오는 중) · 이미 낸 뒤에는 듣지 않는다. 낸 뒤 이 화면은 고른 뒤·공개 연출을 끝까지 보이고 닫힌다.
        /// </summary>
        public bool Confirm()
        {
            if (!CanConfirm || StarterSent || !flow.SetupOpen) return false;
            var client = flow.Client;
            var starterChoice = client.Choice(ChoiceKind.StarterPick);
            var cardChoice = client.Choice(ChoiceKind.TrainerCardPick);
            int starter = starters[Starter], card = cards.Count > 0 ? cards[Card] : -1;
            // 답을 내면 갱신이 바로 와 화면이 그려진다 — 낸 것으로 먼저 적어 두고, 거절되면 되돌린다
            StarterSent = true;
            if (starterChoice != null && !flow.Answer(starterChoice, ChoiceAnswer.Pick(starter)).Accepted)
            {
                StarterSent = false;
                Animate();
                return false;
            }
            CardSent = cardChoice != null;
            if (cardChoice != null && !flow.Answer(cardChoice, ChoiceAnswer.Pick(card)).Accepted)
            {
                CardSent = false;
                Animate();
                return false;
            }
            if (stage != null) stage.Tips.Hide();
            Animate();
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
