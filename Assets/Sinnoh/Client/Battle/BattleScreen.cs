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
    /// 배틀 화면(04 §4-3·§4-4 r3 가운데 필드, S46·S47·S52 최소): 보드 HUD 위에 여는 창이다 — 위 띠(좌석 칩)는 HUD 그대로 보이고, 가운데 필드와 아래 독을 덮는다.
    /// <list type="bullet">
    /// <item>가운데: 어둡게 깐 막 + DS 화면(<see cref="DsStage"/> 256×192 RT를 등급의 정수배로, Pt 창틀 9-slice) + 왼쪽 기둥 「배틀 정보」 + 오른쪽 기둥 「배틀 기록」.</item>
    /// <item>독: 차례 칸 · 기술 세로 3줄(04 §10-2 — 타입 · 이름 · 위력 · 상성 칩, 효과문은 줄 위 툴팁) · [가방]·[기권] · 계산 줄(마지막 피해 계산 · 포획 시도).
    /// 기술 줄 자리는 [가방]을 누르면 가진 볼 줄(합법 <c>ThrowBall</c>만) + [뒤로]로, 출전·다음 포켓몬·교체를 고를 때는 대기 포켓몬 칸(합법 <c>Pick</c>만, 교체 창이면 [교체 안 함])으로 바뀐다.</item>
    /// <item>결과 창: 배틀이 끝나면 「승리/패배/무승부/○○을(를) 잡았다!/도망쳤다!」 + 받은 것 + [확인]. [확인]을 눌러야 화면이 닫힌다(<see cref="GameFlow.CloseBattle"/> — 그때 턴 넘김 띠가 이어진다).
    /// 배틀 뒤 선택(<c>Reward</c> — 파티 가득 놓아주기 · 기술 가르치기 · 받을 규토리 색)이 열려 있으면 결과 창 안에서 그것을 먼저 묻고, 답한 뒤에 [확인]이 나온다.</item>
    /// </list>
    /// 규칙을 세지 않는다: 기술 줄·단추는 엔진의 합법 답(<c>Client.Legal</c>)에 있는 것만 만들고, 누르면 그 답을 낸다(<see cref="GameFlow.Answer"/>).
    /// <para>연출(04 §8-8 54~67행, §8-9 「배틀 연출」): 화면은 보기의 마지막 상태가 아니라 화면 대기열이 지금까지 드러낸 모습을 그린다 —
    /// 나와 있는 포켓몬·상태 이상·기록 줄·계산 줄은 <see cref="BattleFeed.See"/>(아직 닿지 않은 이벤트를 뺀 것), 체력·전투 피로는 표시 값(<c>FxShown</c> — 착지 때 바뀐다).
    /// 그리는 값을 읽는 곳은 <see cref="Shown"/> 하나다. 굴림 단계가 시작하면 기술 3줄 자리가 굴림판(<see cref="DiceBoard"/> — 녹화본 재생)으로 바뀌고 그 항목이 끝나면 돌아온다.
    /// DS 쪽 움직임(상자 흔들림 · 돌진 · 깜빡임 · 깎이는 막대 · 내려가는 도트)은 <see cref="BattlePose"/>가 항목 시각만으로 정한다 — 같은 시각이면 같은 그림이다.
    /// 구조(줄 수 · 칸 · 단추)가 바뀔 때만 요소를 다시 짓고(<see cref="Refresh"/>), 그 사이 프레임에는 그림과 transform·opacity만 바꾼다(<see cref="Animate"/>).
    /// 연출이 도는 동안 단추는 꺼져 있고(<see cref="Ready"/>), 결과 창은 배틀 끝이 드러날 때(결과 단계 70행) 떠서 받은 것이 하나씩 내려앉으며 [확인]은 그 항목이 다 재생된 뒤에 나온다(<see cref="GameFlow.BattleOver"/>).</para>
    /// <para>들어가고 나오는 연출(04 §8-8 53·68·70·72행, §4-10): 인트로에서 포켓몬이 옆에서 미끄러져 들어오고 체력 상자가 뒤따른다(<see cref="BattlePose"/>).
    /// 볼을 던지면 내 주인공 뒷모습(<see cref="BackThrow"/> — Gen 표의 던지기 순서)과 볼이 DS에 그려지고(<see cref="CaptureStage.Pose"/>), 독의 굴림판에서 포획 주사위가 함께 구른다 —
    /// 세기는 판정에서 거꾸로 맞추고, 계산 줄의 성공·실패 칸은 판정 때 켜진다. 갤럭시단 배틀을 열면 조무래기 앞모습이 상대 자리에 섰다가 물러난다.</para>
    /// 받을 화면이 아직 없는 선택(진화 분기·진화 옛 기술·종막 … — M5)이 열리면 안내 줄에 준비 중이라고만 적는다(대신 답하지 않는다, <see cref="GameFlow.NotReadyShown"/>).
    /// 기본값으로 넘기는 선택(트레이너전 보상·운명 창·효과 고르기 …)은 <c>GameFlow.PassEmptyReaction</c>이 맡는다.
    /// 요소 이름: <c>battle</c>·<c>battle-ds</c>·<c>battle-ds-frame</c>·<c>battle-info</c>·<c>battle-info-me</c>·<c>battle-info-foe</c>·<c>battle-log</c>·<c>battle-log-lines</c>·
    /// <c>battle-dock</c>·<c>battle-turn</c>·<c>battle-turn-title</c>·<c>battle-guide</c>·<c>battle-token-use</c>·<c>battle-token-pass</c>·<c>battle-moves</c>·<c>battle-move-&lt;n&gt;</c>·
    /// <c>battle-roll</c>·<c>battle-roll-board</c>·<c>battle-roll-head</c>·<c>battle-roll-faces</c>·<c>battle-roll-face-&lt;n&gt;</c>·<c>battle-roll-sum</c>·<c>battle-roll-ring-&lt;n&gt;</c>·<c>battle-roll-tag-&lt;n&gt;</c>·<c>battle-calc-hp</c>·
    /// <c>battle-bag</c>·<c>battle-ball-&lt;n&gt;</c>·<c>battle-bag-back</c>·<c>battle-bench-&lt;n&gt;</c>·<c>battle-bench-stay</c>·<c>battle-forfeit</c>·<c>battle-calc</c>·<c>battle-calc-capture</c>·
    /// <c>battle-result</c>·<c>battle-result-title</c>·<c>battle-result-lines</c>·<c>battle-result-jp</c>·<c>battle-result-apricorn</c>·<c>battle-result-goal</c>·<c>battle-result-foot</c>·
    /// <c>battle-reward</c>·<c>battle-reward-&lt;n&gt;</c>·<c>battle-result-ok</c>·<c>battle-roll-stock</c>·<c>battle-calc-capture-sum</c>·<c>battle-order-&lt;n&gt;</c>·<c>battle-order-faces-&lt;n&gt;</c>(결정 72 효과 순서 줄)·<c>battle-me-token</c>·<c>battle-foe-token</c>(우선권 토큰 그림, 결정 73 ⑦)·<c>battle-partymoves</c>·<c>battle-partymove-&lt;n&gt;</c>·<c>battle-partymove-back</c>·<c>battle-partymove-next</c>(대기 포켓몬의 기술 — rulings G20).
    /// </summary>
    public sealed partial class BattleScreen : IUiScreen
    {
        /// <summary>기술 줄 수(★ 고유 1 + 배운 칸 2, 04 §10-2).</summary>
        public const int MoveRows = 3;

        /// <summary>기술의 카드 공격력 글(상대 기술 칩): 숫자. 카드에 공격력이 없는 기술(효과가 정함)은 내 기술 줄·조우 카드와 같이 「?」.</summary>
        public static string CardStrength(Ctx ctx, int move) =>
            !BattleRules.IsTactic(move) && ctx.Cards.Moves[move].AttackStrength == null ? "?" : BattleRules.MoveStrength(ctx, move).ToString(CultureInfo.InvariantCulture);

        /// <summary>기둥에 올리는 기록 줄 수(끝에서부터 — 넘치는 것은 위로 잘린다).</summary>
        public const int ShownLines = 40;

        /// <summary>포획 주사위의 몸 색(결정 54 ① — 타입 없는 밝은 회색).</summary>
        public static readonly Color32 CaptureBody = new Color32(0xE0, 0xE4, 0xE8, 0xFF);

        /// <summary>지금 배틀의 상대 트레이너 앞모습(갤럭시단 등장 72행) — Gen <c>Stage/Foe/</c>, 80×80, 발 (40, 78). 간부·태홍은 그 인물(결정 80 L4 나), 조무래기의 남·여는 선 칸으로 고른다(<see cref="FoeArt"/>, 결정 70 ⑩).</summary>
        public string FoeKey => flow.Battle != null ? FoeArt.For(flow.Battle.Foe, flow.Battle.Cell) : FoeArt.Grunt(-1);

        static readonly string[] MoveKeys = { "1", "2", "3" };

        readonly GameFlow flow;
        readonly List<ChoiceAnswer> moves = new List<ChoiceAnswer>();
        readonly List<ChoiceAnswer> balls = new List<ChoiceAnswer>();
        readonly List<ChoiceAnswer> bench = new List<ChoiceAnswer>();
        readonly List<ChoiceAnswer> rewards = new List<ChoiceAnswer>();
        bool bag, moveRows;
        UiStage stage;
        DsStage ds;
        VisualElement dsBox, info, logLines, dock, result, aiLine;
        Label aiText;
        Texture2D heart, speed;

        // 지금 드러난 모습(Read가 채운다)
        readonly HashSet<GameEvent> hidden = new HashSet<GameEvent>();
        BattleSeen seen;
        int local;
        FxEntry rollEntry, rollOf;
        List<AttackDieFace> rollFinal;
        DiceRollScene rollScene;
        int rollCountMs;
        // 포획(68행): 지금 항목의 포획 단계와 그 시도(시작한 뒤로 그 항목이 끝날 때까지), 굴림판이 포획 굴림인가, 세기 간격과 판정 시각(항목 시각)
        FxStep capStep;
        CaptureAttempt cap;
        bool rollIsCapture;
        int capEveryMs, capVerdictAt;
        BackThrow thrower;
        bool throwerTried;
        // 결과 창의 줄: 내려앉기 시작하는 때(항목 시각) → 줄
        readonly List<KeyValuePair<int, VisualElement>> resultDrops = new List<KeyValuePair<int, VisualElement>>();
        VisualElement resultFoot;
        // 굴림판
        readonly DiePose[] poses = new DiePose[DiceGeo.MaxDice];
        readonly List<VisualElement> rollRings = new List<VisualElement>(), rollFaces = new List<VisualElement>();
        readonly List<KeyValuePair<int, VisualElement>> rollTags = new List<KeyValuePair<int, VisualElement>>();
        DiceBoard board;
        bool boardFailed, boardCapture;
        FxEntry boardFor, renderedFor;
        int renderedMs = -1;
        VisualElement rollPanel, rollBoard;
        // DS
        string bg, enemyPlatform, playerPlatform;
        bool terrain;
        BattleLine messageLine;
        string messageText;
        bool actingNow;
        int bobSide = -1, ambient = -1, ambientMs, paintedBob = -1, paintedGlow = -1, paintedMote = -1;
        (bool, bool, bool, int, int, int, int, long) painted;

        public BattleScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "battle";

        /// <summary>지금 독에 올린 기술 줄의 답(엔진의 합법 <c>UseMove</c> 답 그대로, 위 → 아래).</summary>
        public IReadOnlyList<ChoiceAnswer> Moves => moves;

        /// <summary>[가방]을 열어 둔 동안 독에 올린 볼 줄의 답(합법 <c>ThrowBall</c> 그대로 — 볼 등급 순서). 닫혀 있으면 비어 있다.</summary>
        public IReadOnlyList<ChoiceAnswer> Balls => balls;

        /// <summary>[가방]이 열려 있나(볼 줄이 보인다).</summary>
        public bool BagOpen => bag;

        /// <summary>[가방]이 켜져 있나(합법 답에 볼 던지기가 있고 입력이 열려 있다).</summary>
        public bool BagOn { get; private set; }

        /// <summary>출전·다음 포켓몬·교체를 고르는 중에 독에 올린 대기 포켓몬 칸의 답(합법 <c>Pick</c> 그대로). 아니면 비어 있다.</summary>
        public IReadOnlyList<ChoiceAnswer> Bench => bench;

        /// <summary>교체 창인가([교체 안 함]이 있다).</summary>
        public bool CanStay { get; private set; }

        /// <summary>배틀 뒤 선택을 묻는 중에 결과 창에 올린 단추의 답(합법 답 그대로 — <c>battle-reward-&lt;n&gt;</c>, n은 1부터). 아니면 비어 있다.</summary>
        public IReadOnlyList<ChoiceAnswer> Rewards => rewards;

        /// <summary>배틀 뒤 선택(놓아주기 · 기술 가르치기 · 규토리 색)을 묻는 중인가. 그동안에는 [확인]이 없다.</summary>
        public bool AskingReward => rewards.Count > 0;

        /// <summary>[기권]이 켜져 있나(합법 답에 기권이 있고 입력이 열려 있다).</summary>
        public bool ForfeitOn { get; private set; }

        /// <summary>우선권 토큰을 쓸지 묻는 중인가.</summary>
        public bool AskingToken { get; private set; }

        /// <summary>
        /// 굴린 뒤 면 바꾸기의 순서를 묻는 중인가(결정 72 — 순서에 따라 결과가 갈릴 때만 엔진이 묻는다). 그동안 독의 기술 줄 자리는 순서 줄(<see cref="Orders"/>)이다.
        /// </summary>
        public bool AskingOrder { get; private set; }

        /// <summary>
        /// 운명의 변화를 쓸지 묻는 중인가(Reaction/Fate — 디아루가 = 굴린 뒤 창 · 기라티나 = 굴리기 전 창, M5 「운명·응답 화면」): 독의 기술 줄 자리가 운명 줄이다 —
        /// 디아루가면 다시 굴릴 주사위 칩(<see cref="ToggleFateDie"/>) · 카드마다 [1장]·[3장](<see cref="PressFate"/>) · 3장에 함께 낼 아르세우스 칩 · 차례 칸의 [넘기기](<see cref="PressFatePass"/>) [가정 — 결정 72 순서 줄 꼴].
        /// 같은 창의 psychic R(결정 131)은 주사위 칩 하나 + 능력 줄의 새 눈 단추(<see cref="PressPsychic"/>).
        /// </summary>
        public bool AskingFate { get; private set; }

        /// <summary>「쓸까」를 묻는 중인가(Reaction/AlluringStew 매혹적인 스튜 · EffectOption 기술 효과의 「You may …」) — 차례 칸의 [사용]·[넘기기](<see cref="PressAsk"/>).</summary>
        public bool AskingAsk { get; private set; }

        /// <summary>기술 효과의 고르기를 묻는 중인가(Reaction/EffectPick — 타입 · 상태 · 규토리 색 · 기술 · 대상 · 미래예지 맞히기): 독의 기술 줄 자리에 고를 것 단추(<see cref="PressPick"/>), 「안 함」이 있으면 [넘기기].</summary>
        public bool AskingPick { get; private set; }

        /// <summary>남의 도전·거래 응답(Respond)이 배틀 화면이 떠 있는 동안 열렸나 — 차례 칸의 [수락]·[거절](<see cref="PressRespond"/>).</summary>
        public bool AskingRespond { get; private set; }

        /// <summary>운명 줄에서 고른 주사위(엔진 순서).</summary>
        public IReadOnlyCollection<int> FateDice => fateDice;
        readonly SortedSet<int> fateDice = new SortedSet<int>();
        int fateWild, askedId = -1;

        /// <summary>고르기 단추의 답(합법 답 순서 — <c>battle-pick-&lt;n&gt;</c>, 「안 함」은 빼고).</summary>
        public IReadOnlyList<ChoiceAnswer> Picks => picks;
        readonly List<ChoiceAnswer> picks = new List<ChoiceAnswer>();

        /// <summary>디아루가 재굴림(43행)의 주사위 줄이 지금 독에 떠 있나 · 기라티나 희생(44행)의 그림자가 보이는 만큼(확인용).</summary>
        public bool RerollShown { get; private set; }
        public float ShadowShown => fateShadow != null ? fateShadow.style.opacity.value : 0f;

        /// <summary>운명 카드 연출 층(카드 지나가기 45행 · 배틀 안 받기).</summary>
        public FateFx FateFx => fateFx;
        FateFx fateFx;
        VisualElement fateShadow;
        Label fatePower;
        readonly List<KeyValuePair<int, VisualElement>> rerollDice = new List<KeyValuePair<int, VisualElement>>();
        FxStep rerollStep;

        /// <summary>순서 줄의 답(엔진의 합법 답 그대로 — 위 → 아래, 첫 줄 = 기본 순서 · <c>battle-order-&lt;n&gt;</c>, 단축키 1~3). 묻지 않으면 비어 있다.</summary>
        public IReadOnlyList<ChoiceAnswer> Orders => orders;
        readonly List<ChoiceAnswer> orders = new List<ChoiceAnswer>();

        /// <summary>결과 창이 떠 있나(배틀이 끝났고 그 끝까지 화면에 다 보였다).</summary>
        public bool ShowsResult => flow.BattleOver;

        /// <summary>결과 창이 보이나: 배틀 끝이 화면에 드러난 때부터(70행 — 받은 것이 하나씩 내려앉는다). [확인]은 그 항목이 다 재생된 뒤(<see cref="ShowsResult"/>)에 나온다.</summary>
        public bool ResultShown { get; private set; }

        /// <summary>결과 창에 지금 내려앉은(보이기 시작한) 줄 수와 전체 줄 수.</summary>
        public int ResultLanded
        {
            get
            {
                int n = 0, now = ResultTime;
                foreach (var d in resultDrops) if (now >= d.Key) n++;
                return n;
            }
        }
        public int ResultLines => resultDrops.Count;

        /// <summary>굴림판이 포획 굴림인가(68행 — 몸 색 <see cref="CaptureBody"/>, 세기는 판정에서 거꾸로 맞춘다).</summary>
        public bool RollIsCapture => rollEntry != null && rollIsCapture;

        /// <summary>DS에 지금 그린 포획의 모습(뒷모습 · 볼 · 별 — 포획 중이 아니면 모두 꺼져 있다).</summary>
        /// <summary>결과 창의 규토리 줄에 선 색과 수(결정 101 ③ — 얻은 색마다 아이콘 + 수).</summary>
        public readonly List<KeyValuePair<ApricornColor, int>> ResultApricorns = new List<KeyValuePair<ApricornColor, int>>();

        /// <summary>결과 창의 스탬프 보상 줄에 실은 규토리(색 · 수 — 이 배틀로 받은 스탬프 모두, M9 다듬기 P19) — 확인용.</summary>
        public readonly List<KeyValuePair<ApricornColor, int>> ResultStampApricorns = new List<KeyValuePair<ApricornColor, int>>();

        public CapturePose Capture { get; private set; }

        /// <summary>뒷모습을 던지는 좌석(아래쪽 = <c>BattleFeed.MySide</c>의 주인 — 남의 배틀을 볼 때는 그 좌석). 뒷모습을 아직 찾지 않았으면 −1.</summary>
        public int ThrowerSeat { get; private set; } = -1;

        /// <summary>기둥 「내 쪽」(아래쪽 = <c>BattleFeed.MySide</c>)에 그린 좌석 — 남의 배틀을 볼 때는 그 쪽에서 싸우는 좌석(M7 마감 감사 1). 아직 그리지 않았으면 −1.</summary>
        public int InfoSeat { get; private set; } = -1;

        // 아래쪽(feed.MySide)에서 싸우는 좌석: 내가 당사자면 나, 남의 배틀이면 그 쪽 주인. 주인이 없으면(좌석이 아닌 쪽) 내 좌석
        int SideSeat(BattleFeed feed)
        {
            var state = feed.Source;
            int owner = state != null && state.Battle != null ? state.Battle.Sides[feed.MySide].Owner : -1;
            return owner >= 0 ? owner : flow.Client.Seat;
        }

        /// <summary>DS에 상대 트레이너(갤럭시단 조무래기)가 서 있나(72행).</summary>
        public bool FoeTrainerShown { get; private set; }

        /// <summary>갤럭시단 라인업 공개(72행 S54)에서 지금 놓여 보이는 명단 볼 수 — 확인용.</summary>
        public int LineupShown { get; private set; }

        /// <summary>라인업 볼 그림(Pt 「interface_stock_enemy」 16×67 — 빈 칸 · 볼 · 상태 · 기절이 17줄 간격, 볼 = 둘째 칸) · 첫 볼의 왼쪽 위 · 볼 사이(DS px) [가정 — plan §5 #96].</summary>
        public const string LineupBallKey = "Stage/Hud/interface_stock_enemy.png";
        public const int LineupBallSy = 17, LineupGap = 13;
        public static readonly Vector2Int LineupAt = new Vector2Int(10, 30);

        /// <summary>이 배틀 무대의 시간대(라운드에서 셈 — <see cref="DayTimes"/>) · 그린 배경의 Gen 키.</summary>
        public DayTime Time { get; private set; }
        public string StageBg => bg;

        /// <summary>독의 기술 줄 자리에 굴림판이 떠 있나(굴림 단계가 시작한 항목이 재생 중이다).</summary>
        public bool Rolling => rollEntry != null;

        /// <summary>굴림판의 결과 줄에 지금 서 있는 면(왼쪽부터 = 엔진 순서, 면 바꾸기 뒤의 면). 굴림판이 없으면 비어 있다.</summary>
        public IReadOnlyList<AttackDieFace> RollFaces => shownFaces;
        readonly List<AttackDieFace> shownFaces = new List<AttackDieFace>();

        /// <summary>굴림판의 합계 칸에 지금 적힌 값(센 주사위까지의 합). 굴림판이 없으면 −1.</summary>
        public int RollSum { get; private set; } = -1;

        /// <summary>굴림판에 주사위 그림(녹화본 재생)이 붙었나 — 녹화본 묶음이나 셰이더가 없으면 결과 줄만 선다.</summary>
        public bool RollBoardOn => rollEntry != null && rollBoard != null && board != null && rollScene != null;

        /// <summary>계산 줄에 지금 켜진 칸 수(총합 칸 포함)와 체력 변화 줄이 보이나.</summary>
        public int CalcLit { get; private set; }
        public bool CalcHpShown { get; private set; }

        /// <summary>
        /// 계산 줄이 앞 공격의 계산을 남겨 두고 있나(결정 70 ⑧ [가정 — plan §5]): 다음 공격의 굴림판이 떠도 그 공격의 계산 단계(61행)가
        /// 시작할 때까지는 마지막 피해 계산과 그 체력 변화가 그대로 보인다(전에는 굴림이 시작하면 바로 비웠다). 박자는 그대로다 — 남겨 두는 것은 그림뿐.
        /// 남겨 둔 동안 <see cref="CalcLit"/>·<see cref="CalcHpShown"/>(이 공격의 계산)은 0·false다.
        /// </summary>
        public bool CalcHeld { get; private set; }

        /// <summary>계산 줄의 피해가 내 쪽(아래쪽 — <c>BattleFeed.MySide</c>)이 받은 것인가(결정 171 ② — 이름표 · 오른쪽 정렬). 계산이 없으면 false.</summary>
        public bool CalcTaken { get; private set; }

        // 그 포켓몬이 아래쪽(내 쪽) 포켓몬인가: 나와 있는 쪽 → 아래쪽 좌석의 파티
        bool OnMySide(BattleFeed feed, int id)
        {
            if (seen.Active[feed.MySide] == id) return true;
            if (seen.Active[1 - feed.MySide] == id) return false;
            int seat = SideSeat(feed);
            var state = feed.Source;
            return state != null && seat >= 0 && seat < state.Seats.Length && Array.IndexOf(state.Seats[seat].Party, id) >= 0;
        }

        /// <summary>계산 줄의 포획 칸: 합 칸이 켜졌나(다 센 뒤) · 성공·실패 칸이 켜졌나(판정 뒤). 포획 칸이 없으면 둘 다 false.</summary>
        public bool CaptureSumLit { get; private set; }
        public bool CaptureVerdictLit { get; private set; }

        /// <summary>DS 체력 상자에 지금 그린 값(쪽 = 배틀의 쪽 0·1 — 없으면 Id −1).</summary>
        public BattleMon ShownMon(int side) => seen != null && side >= 0 && side < 2 ? Shown(seen.Active[side]) : new BattleMon { Id = -1, Def = -1 };

        /// <summary>AI 자막 띠(73행)에 지금 보이는 글(띠가 걷혀 있으면 null).</summary>
        public string AiLine { get; private set; }

        /// <summary>DS 메시지 창에 지금 찍힌 글.</summary>
        public string Message { get; private set; } = "";

        /// <summary>기둥에 지금 올라 있는 기록 줄 수(연출이 드러낸 것만).</summary>
        public int ShownLineCount => seen != null ? seen.Lines : 0;

        /// <summary>DS 층(Gen 본체가 없으면 null).</summary>
        public DsStage Ds => ds;
        /// <summary>마지막으로 DS 층에 찍은 장면(시험·확인 도구가 본다 — 체력 상자를 그렸나, 뒷모습의 프레임).</summary>
        public DsScene Painted { get; private set; }

        /// <summary>
        /// 내 입력을 받아도 되나: 배틀이 살아 있고 화면 대기열이 비어 클릭 막기 시간이 지났다(04 §8-1 P4 — 창 자신의 입력은 대기열로 본다. <see cref="GameFlow.InputOpen"/>은 창이 열려 있으면 늘 false다).
        /// </summary>
        public bool Open => flow.Battle != null && !flow.Battle.Ended && Ready;

        /// <summary>
        /// 이 화면의 단추가 들어도 되나: 화면 대기열이 비어 클릭 막기 시간이 지났다. 결과 창이 뜬 뒤에는 늘 듣는다 — 그동안 대기열은 일부러 세워 둔 것이라
        /// (턴 넘김이 [확인]을 기다린다) 비기를 기다리면 배틀 뒤 선택의 단추가 영영 열리지 않는다. [확인]도 같은 규칙이다.
        /// </summary>
        public bool Ready => flow.Fx == null || flow.Fx.Sequencer.InputOpen || flow.BattleOver;

        /// <summary>
        /// 독의 [정비](T, 04 §3-5)가 켜져 있나: 내 입력을 받고(<see cref="Open"/> — 연출 중이 아니고 배틀이 끝나지 않았다) 엔진이 정비를 막지 않는 때다 —
        /// 배틀 참가 중에 「언제든」 정비(순서·기술 가르치기)가 막히는 것은 내 포켓몬의 공격을 처리하는 동안뿐이다(<see cref="MaintenanceRules.OwnAttackProcessing"/>, rulings G10).
        /// 놓아주기·지닌물건은 배틀 밖에서만이라 판 안에서 엔진 검사가 칸을 끈다.
        /// </summary>
        public bool MaintOn => Open && flow.Client != null && flow.Client.View != null && !MaintenanceRules.OwnAttackProcessing(flow.Client.View, flow.Client.Seat);

        /// <summary>기둥의 [운명의 변화 n/5](결정 80 N3 나 — 손패 훑어보기)의 요소 이름 · 지금 켜져 있나.</summary>
        public const string FateButton = "battle-fate";
        /// <summary>차례 칸의 앰프 · 장작 줄(결정 87 V9 — 칩 <c>battle-card-amp</c>·<c>-firewood</c>) · 기둥의 날씨 칩(V7).</summary>
        public const string CardState = "battle-card", WeatherChip = "battle-weather";
        /// <summary>독의 고르기 단추 줄(빈 기술 칸 위에 얹는다 — 결정 87 V1 ①).</summary>
        public const string PickRow = "battle-picks";
        public bool FateOn { get; private set; }

        /// <summary>이 화면 위에 띄운 창(배틀 중 정비 판 · 그 카드 확대 · 손패 창 — <see cref="GameFlow.Top"/>). 없으면 null.</summary>
        public IUiScreen Over { get; private set; }

        /// <summary>그 창을 이 화면 위(독·결과 창보다 위)에 띄운다. 떠 있던 창은 닫는다. 배틀 화면은 그대로 남는다.</summary>
        public void ShowOver(IUiScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            ShutOver();
            Over = screen;
            if (stage == null || page == null) return;
            overElement = screen.Build(stage);
            overElement.name = screen.Name;
            overElement.AddToClassList("sn-screen");
            page.Add(overElement);
        }

        /// <summary>위에 띄운 창을 닫는다(배틀로 돌아온다).</summary>
        public void ShutOver()
        {
            if (Over == null) return;
            stage?.Tips.Hide();
            overElement?.RemoveFromHierarchy();
            overElement = null;
            var was = Over;
            Over = null;
            was.Close();
        }

        VisualElement page, overElement, logBox;
        MonTip monTip;

        /// <summary>포켓몬 고르기 줄의 마우스 오버 카드에 지금 뜬 포켓몬(종 색인 — 없으면 −1, 결정 169) · 그 카드 요소.</summary>
        public int MonTipShown => monTip != null ? monTip.Shown : -1;
        public VisualElement MonTipElement => monTip?.Element;

        /// <summary>
        /// 독의 포켓몬 칸(이름 = 출전 · 교체 · 다음 포켓몬 <c>battle-bench-n</c> · 회복할 포켓몬 <c>battle-ability-n</c> · 운명 창의 회복 단추 <c>battle-fate-potion-n</c> · 대기 포켓몬 기술 <c>battle-partymove-n</c>)에 마우스를 올린 것처럼 카드를 띄운다 — 캡처 · 시험.
        /// 그 칸이 없으면 false.
        /// </summary>
        public bool HoverMon(string cell)
        {
            var el = (dock != null ? dock.Q(cell) : null) ?? (page != null ? page.Q(cell) : null);   // 독의 칸 · 상대 기둥의 포켓몬 그림(battle-foe-art — 결정 182)
            if (el == null || monTip == null || !monCells.TryGetValue(el, out var show)) return false;
            show();
            return true;
        }

        readonly Dictionary<VisualElement, Action> monCells = new Dictionary<VisualElement, Action>();

        // 포켓몬 칸에 마우스 오버 카드를 건다(결정 169 — 기록 기둥 위에: 독 · DS를 가리지 않는다)
        void HookMon(VisualElement el, int id)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (monTip == null || el == null || view == null || id < 0 || id >= view.Pokemon.Count) return;
            var mon = Shown(id);
            if (!mon.Exists) return;
            var pk = view.Pokemon[id];
            monTip.HookMon(el, pk, mon.Hp, mon.Speed, logBox);
            monCells[el] = () => monTip.ShowMon(el, pk, mon.Hp, mon.Speed, logBox);
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            flow.Ticked -= PaintPeek;
            flow.Ticked += PaintPeek;
            heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            speed = GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey);
            ds = DsStage.Create();

            page = new VisualElement { pickingMode = PickingMode.Ignore };   // 위 띠(HUD)는 덮지 않는다
            Add(page, new VisualElement(), "battle-scrim", "sn-battle-scrim");   // 가운데 필드와 HUD 아래 판을 덮어 누르지 못하게 한다

            var field = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-field", "sn-battle-field");
            var frame = Add(field, new VisualElement(), "battle-ds-frame", "sn-battle-ds-frame");
            Kit.SetFrame(frame, stage.Skin.Panel);
            dsBox = Add(frame, new VisualElement(), "battle-ds", "sn-battle-ds");
            if (ds != null) dsBox.style.backgroundImage = Background.FromRenderTexture(ds.Output);
            // AI 자막(73행): DS 위 가장자리의 띠(마우스를 받지 않는다). 사유가 있는 AI 답의 단계 동안만 보인다
            aiLine = Add(dsBox, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-ai-line", "sn-battle-ai");
            aiText = Add(aiLine, Kit.Text("", Kit.F2), "battle-ai-text", null);
            aiText.pickingMode = PickingMode.Ignore;
            FxUi.Fade(aiLine, 0f);
            // 기라티나 희생(44행): DS를 덮는 검은 그림자 + 위력 칩(마우스를 받지 않는다)
            fateShadow = Add(dsBox, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-fate-shadow", "sn-battle-fate-shadow");
            fatePower = Add(fateShadow, Kit.Badge("", KitBadgeKind.Red, Kit.F3), "battle-fate-power", "sn-battle-fate-power");
            fatePower.pickingMode = PickingMode.Ignore;
            FxUi.Fade(fateShadow, 0f);

            info = Add(field, Kit.Panel(stage.Skin), "battle-info", "sn-battle-col");
            info.AddToClassList("sn-battle-info");
            info.RegisterCallback<GeometryChangedEvent>(e => InfoMoved());   // 엔트리 판 · 카드 글 판은 기둥 옆에 붙는다(결정 206 ②)
            var log = logBox = Add(field, Kit.Panel(stage.Skin), "battle-log", "sn-battle-col");
            log.AddToClassList("sn-battle-log");
            monTip?.Detach();
            monTip = new MonTip(stage, flow.Ctx);   // 포켓몬 고르기 줄의 마우스 오버 카드(결정 169) — 기록 기둥 위에 얹는다
            var logHead = Add(log, new VisualElement(), null, "sn-battle-head");
            Add(logHead, Kit.Text(stage.T("screen.battle.title.log"), Kit.F2), null, "kit-title");
            logLines = Add(log, new VisualElement(), "battle-log-lines", "sn-battle-lines");

            dock = Add(page, Kit.Panel(stage.Skin), "battle-dock", "sn-battle-dock");
            result = Add(page, new VisualElement(), "battle-result", "sn-battle-result");
            fateFx = new FateFx(stage, "battle");
            page.Add(fateFx.Element);
            BuildUb();   // 프리즘 아머 · 온슬롯 카드 · 시련 통과 · 이동력 +2(M6 「UB 배틀 화면 묶음」 — BattleScreen.Ub.cs)
            orb = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-orb", "sn-battle-orb");   // 은하의 응징 구슬(74행 — BattleScreen.Rows.cs)
            FxUi.Fade(orb, 0f);
            Refresh();
            ambient = stage.Tweens.Ambient(new Bob(this));   // 차례인 쪽 체력 상자의 흔들림(55행 — 늘 도는 장식, 바쁨으로 세지 않는다)
            return page;
        }

        public void Close()
        {
            flow.Ticked -= PaintPeek;
            watchRows.Clear();
            ShutOver();
            monTip?.Detach();
            monTip = null;
            ShutInfo();
            logBox = null;
            page = null;
            if (stage != null && ambient >= 0) stage.Tweens.Stop(ambient);
            ambient = -1;
            if (ds != null) ds.Dispose();
            ds = null;
            if (board != null) board.Dispose();
            board = null;
            boardFor = renderedFor = null;
            rollPanel = rollBoard = null;
            rollRings.Clear();
            rollFaces.Clear();
            rollTags.Clear();
            resultDrops.Clear();
            resultFoot = null;
            capStep = null;
            cap = null;
            stage = null;
        }

        sealed class Bob : ISeekable
        {
            readonly BattleScreen screen;
            public Bob(BattleScreen screen) { this.screen = screen; }
            public void Seek(int ms) => screen.Ambient(ms);
        }

        void Ambient(int ms)
        {
            ambientMs = ms;
            if (stage == null || seen == null || flow.Battle == null) return;
            if (staying && MotePhase != paintedMote) { PaintDs(); return; }   // 비스트 부스트의 오르는 알갱이(결정 184)
            if (bobSide < 0) return;
            if (BobPhase != paintedBob || GlowPhase != paintedGlow) PaintDs();
        }

        int MotePhase => ambientMs / BoostMotes.StepMs;

        int BobPhase => ambientMs / BattleBeats.TurnBobEveryMs % 2;
        // 우선권 토큰을 묻는 동안 빛남·반짝이가 바뀌는 박자(묻지 않으면 −1)
        int GlowPhase => AskingToken ? ambientMs / InitiativeBeats.TwinkleEveryMs : -1;

        /// <summary>화면 대기열이 프레임마다 부른다(연출이 도는 동안): 구조가 바뀌었으면 다시 짓고, 아니면 그림과 transform·opacity만 그 시각으로 보낸다.</summary>
        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || flow.Battle == null || flow.Client == null || flow.Client.View == null) return;
            Read();
            if (!Signature().Equals(painted)) Refresh();
            else Animate();
            ShowFate(seq);
        }

        // 배틀 안 운명 연출: 카드 지나가기(45행) · 받기(배틀 보상) — 내 손 = 독의 왼쪽 아래 · 기라티나 그림자(44행) · 디아루가 주사위 줄(43행 — Animate)
        void ShowFate(FxSequencer seq)
        {
            if (fateFx == null || flow.Client == null) return;
            var box = dock != null ? dock.worldBound : default(Rect);
            var hand = float.IsNaN(box.width) || box.width <= 0f ? new Vector2(160f, 640f) : fateFx.Element.WorldToLocal(new Vector2(box.xMin + 60f, box.yMin + 20f));
            float w = fateFx.Element.layout.width;
            var from = new Vector2(float.IsNaN(w) || w <= 0f ? 640f : w * 0.5f, 60f);
            fateFx.Show(seq, flow.Client.Seat, flow.Ctx, hand, s => (Vector2?)null, from, from);
            var step = SacrificeBeats.StepOf(seq.Current);
            if (step == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs)
            {
                FxUi.Fade(fateShadow, 0f);
                return;
            }
            var pose = SacrificeBeats.At(seq.LocalMs - step.StartMs, step.Ms);
            int power = 1;
            foreach (var e in seq.Current.Events)
                if (e is FatePlayed p && p.Card == FateCard.Giratina && p.Play3)
                    foreach (var h in seq.Current.Events)
                        if (h is HpChanged hc && hc.Source.Kind == SourceKind.Fate) power = Math.Max(0, (hc.Before - hc.After) / 2);
            fatePower.text = stage.T("screen.battle.fate.power").Replace("{n}", power.ToString(CultureInfo.InvariantCulture));
            FxUi.Fade(fateShadow, pose.Shadow);
            FxUi.Fade(fatePower, pose.Chip > 0f ? 1f : 0f);
            FxUi.Scale(fatePower, Math.Max(1f, pose.Chip), Math.Max(1f, pose.Chip));
        }

        // ── 지금 드러난 모습 ──

        // 화면 대기열에서 아직 닿지 않은 이벤트를 빼고 다시 센다. 굴림판이 떠 있는 항목이 바뀌면 그 굴림의 면·세기 시각을 다시 읽는다
        void Read()
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (seq != null) FxReveal.Hidden(seq, hidden);
            else hidden.Clear();
            seen = flow.Battle.See(hidden);
            var cur = seq != null ? seq.Current : null;
            local = cur != null ? seq.LocalMs : 0;
            rerollStep = RerollBeats.StepOf(cur);
            if (rerollStep != null && (local < rerollStep.StartMs || local >= rerollStep.EndMs || RerollBeats.Event(rerollStep) == null)) rerollStep = null;
            capStep = CaptureStage.StepOf(cur);
            if (capStep != null && local < capStep.StartMs) capStep = null;
            cap = capStep != null ? (CaptureAttempt)capStep.Events[0] : null;
            // 굴림판: 공격 굴림(56행)이 시작했으면 그것, 아니면 포획 단계의 포획 굴림(실패한 포획 뒤에 상대의 공격이 같은 항목에 이어질 수 있다)
            bool attack = seq != null && BattleRoll.Showing(seq);
            bool capture = !attack && cap != null && cap.Faces != null && cap.Faces.Length >= DiceGeo.MinDice;
            rollEntry = attack || capture ? cur : null;
            if (ReferenceEquals(rollEntry, rollOf) && capture == rollIsCapture) return;
            rollOf = rollEntry;
            rollIsCapture = capture;
            if (capture)
            {
                // 포획 굴림: 던짐과 함께 시작하고, 합계가 판정 100ms 전에 서도록 세기를 뒤에서부터 맞춘다(결정 54 ①)
                rollFinal = CaptureStage.FinalFaces(cap);
                rollScene = capStep.Tag as DiceRollScene;
                int verdict = CaptureStage.VerdictMs(cap);
                int rollMs = rollScene != null ? rollScene.ThrowMs - capStep.StartMs + rollScene.Clip.LengthMs : PlayTiming.DiceRollMaxMs.Value;
                capEveryMs = CaptureStage.CountEveryMs(verdict, rollMs, rollFinal.Count);
                capVerdictAt = capStep.StartMs + verdict;
                rollCountMs = capStep.StartMs + CaptureStage.CountAtMs(verdict, capEveryMs, rollFinal.Count, 0);
                return;
            }
            rollFinal = rollEntry != null ? BattleRoll.FinalFaces(rollEntry) : null;
            rollScene = rollEntry != null ? BattleRoll.StepOf(rollEntry).Tag as DiceRollScene : null;
            rollCountMs = rollEntry == null ? 0 : rollScene != null ? rollScene.CountMs : BattleRoll.CountMs(rollEntry);
        }

        /// <summary>그 포켓몬의 지금 그릴 값: 보기의 값에 표시 값(체력·전투 피로 — 착지 전이면 앞 값)과 드러난 상태 이상을 입힌다. 화면의 모든 칸이 여기서 읽는다.</summary>
        /// <summary>남의 배틀의 기술 줄을 보기 전용으로 그렸나(T12) — 확인용.</summary>
        public bool WatchMoves { get; private set; }

        /// <summary>남의 배틀 기술 줄에서 그 좌석이 지금 가리키는 기술(방 판의 「남이 가리키는 것」 — 결정 120 · Protocol 10, 없으면 −1).</summary>
        public int PeekMove { get; private set; } = -1;

        /// <summary>남이 가리키는 기술 줄의 클래스.</summary>
        public const string PeekClass = "sn-battle-move-peek";

        readonly List<KeyValuePair<VisualElement, int>> watchRows = new List<KeyValuePair<VisualElement, int>>();

        /// <summary>남의 배틀 기술 줄(보기 전용)의 기술들(보기 순서 — 시험 · 확인).</summary>
        public List<int> WatchedMoves => watchRows.ConvertAll(r => r.Value);
        int watchSeat = -1;

        // 남의 배틀 기술 줄(보기 전용): 그 좌석이 가리키는 기술에 좌석 색 테두리(프레임마다 — 소식은 대기열과 무관하게 온다). 줄을 다시 지으면 PeekMove를 −1로 되돌려 다시 칠한다
        void PaintPeek()
        {
            var m = WatchMoves && watchSeat >= 0 ? flow.PeekOf(watchSeat, PeekSpot.Move) : null;
            int move = m != null && watchRows.Exists(r => r.Value == m.Index) ? m.Index : -1;
            if (move == PeekMove) return;
            PeekMove = move;
            var tone = watchSeat >= 0 ? new StyleColor((Color)flow.SeatColor(watchSeat)) : new StyleColor(StyleKeyword.Null);
            foreach (var r in watchRows)
            {
                bool on = r.Value == move;
                r.Key.EnableInClassList(PeekClass, on);
                var color = on ? tone : new StyleColor(StyleKeyword.Null);
                r.Key.style.borderLeftColor = r.Key.style.borderRightColor = r.Key.style.borderTopColor = r.Key.style.borderBottomColor = color;
            }
        }

        /// <summary>상대가 드러나기 전(첫 출전을 고르는 중)인데 기둥에 미리 보였나(T14) — 확인용.</summary>
        public bool FoePreviewed { get; private set; }

        /// <summary>
        /// 플레이 시험 T14(룰북 p.16 · rulings A17 — 우두머리는 공개한 칸에서 「찾아보기」로 카드를 뽑아 모두가 본 뒤 배틀한다, 전설 · 무리 · 야생 카드도 뽑은 카드가 공개):
        /// 야생 쪽 배틀에서 아래쪽 좌석이 첫 출전을 고르는 동안(상대 도트가 아직 나오지 않았다) 상대 기둥에 그 야생 포켓몬(명단 첫 마리)을 미리 보인다. 아니면 −1.
        /// </summary>
        int FoePreview(BattleFeed feed, BattleMon foe)
        {
            var state = feed.Source;
            if (foe.Exists || seen.Ended || state == null || state.Battle == null || !FxRows.WildLike(feed.Kind)) return -1;
            var side = state.Battle.Sides[1 - feed.MySide];
            int id = side.Active >= 0 ? side.Active : side.Roster.Count > 0 ? side.Roster[0] : -1;
            if (id < 0) return -1;
            int seat = SideSeat(feed);
            return state.Pending.Exists(p => p.Seat == seat && !p.Answered && p.Kind == ChoiceKind.SendIn) ? id : -1;
        }

        BattleMon Shown(int id)
        {
            var mon = flow.Battle.MonOf(id);
            if (!mon.Exists) return mon;
            if (flow.Fx != null)
            {
                var shown = flow.Fx.Sequencer.Shown;
                mon.Hp = shown.Get(ShownKey.Hp(id), mon.Hp);
                mon.Fatigue = shown.Get(ShownKey.Fatigue(id), mon.Fatigue);
            }
            if (seen != null)
            {
                mon.Status = seen.Status(id, mon.Status);
                mon.Status2 = mon.Status == StatusEffect.None ? StatusEffect.None : seen.Status2(id, mon.Status2);
            }
            return Reform(mon);   // 폼 바꾸기(78·79행)가 닿기 전에는 옛 폼
        }

        // k번째(0부터) 주사위를 세는 항목 시각: 공격 굴림은 세기 시작에서 고른 간격으로, 포획 굴림은 판정에서 거꾸로 맞춘 간격으로
        int CountAt(int k) => rollIsCapture ? rollCountMs + k * capEveryMs : BattleRoll.CountedAt(rollCountMs, k);

        int Counted
        {
            get
            {
                if (rollEntry == null || rollFinal == null) return 0;
                if (!rollIsCapture) return BattleRoll.Counted(rollCountMs, rollFinal.Count, local);
                int n = 0;
                for (int k = 0; k < rollFinal.Count; k++) if (local >= CountAt(k)) n++;
                return n;
            }
        }

        /// <summary>포획 굴림의 세는 간격(ms) · 합계가 서는 항목 시각 · 판정이 나는 항목 시각 — 포획 굴림이 아니면 0·−1(확인용).</summary>
        public int CaptureCountEveryMs => RollIsCapture ? capEveryMs : 0;
        public int CaptureSumAtMs => RollIsCapture ? CountAt(rollFinal.Count - 1) : -1;
        public int CaptureVerdictAtMs => RollIsCapture ? capVerdictAt : -1;

        // 결과 창의 박자를 재는 시각: 배틀 끝이 든 항목이 재생 중이면 그 항목 시각, 다 재생됐으면 끝(모든 줄이 내려앉은 모습)
        int ResultTime
        {
            get
            {
                var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
                return cur != null && cur.Step(700) != null ? local : int.MaxValue;
            }
        }

        // 던진 볼의 재고(표시 값 — 손을 떠나는 순간 줄어든다)
        int BallStock(int ball)
        {
            var seat = flow.Client.View.Seats[flow.Client.Seat];
            int have = ball >= 0 && ball < seat.Balls.Length ? seat.Balls[ball] : 0;
            return flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(flow.Client.Seat, ResKind.Ball, ball), have) : have;
        }

        // 지금 항목의 계산 단계(61행 — 피해 계산이 든 것, 없으면 null). 포획 굴림이 떠 있으면 계산 줄은 포획 칸이라 보지 않는다
        FxStep CalcStep
        {
            get
            {
                if (rollEntry != null && rollIsCapture) return null;
                var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
                var step = cur != null ? cur.Step(611) : null;
                return step != null && step.Events.Count > 0 ? step : null;
            }
        }

        // 계산 줄의 박자(결정 171 ①): 그 공격이 맞는 순간(59행 단계 시작 — 결정 143 ⑦)부터 잰 시각, 아직이면 −1.
        // 전에는 계산 단계(61행) 시작부터 쟀다 — 그 앞의 상성 단계(60행 900~1600)만큼 합계가 늦게 떴다. 59행이 없는 항목이면 61행 시작부터
        int CalcMs
        {
            get
            {
                int from = CalcFromMs;
                return from >= 0 && local >= from ? local - from : -1;
            }
        }

        /// <summary>계산 줄이 그 공격의 맞는 순간부터 재는 시각(지금 항목의 59행 단계 시작 — 없으면 61행 시작, 계산 단계가 없으면 −1) — 확인용(결정 171 ①).</summary>
        public int CalcFromMs
        {
            get
            {
                var step = CalcStep;
                if (step == null) return -1;
                var hit = flow.Fx.Sequencer.Current.Step(590);
                return hit != null && hit.StartMs <= step.StartMs ? hit.StartMs : step.StartMs;
            }
        }

        // 다시 지어야 하는 것이 바뀌었나: 입력 · 결과 창 · 굴림판 · 드러난 이벤트 수 · 항목 · 센 주사위 · 계산 줄의 켜진 칸 · 그리는 체력·피로·상태
        (bool, bool, bool, int, int, int, int, long) Signature()
        {
            var feed = flow.Battle;
            var me = Shown(seen.Active[feed.MySide]);
            var foe = Shown(seen.Active[1 - feed.MySide]);
            int calcMs = CalcMs;
            int calc = calcMs < 0 ? -1 : BattleRoll.CalcLit(8, calcMs) + (BattleRoll.TotalLit(calcMs) ? 16 : 0);
            if (seen.DamageHp != null) calc += 32;
            if (seen.Damage != null) calc += 1024;
            if (rerollStep != null) calc += 4096;
            if (rollIsCapture && cap != null) calc += 64 + 128 * BallStock(cap.BallKey);
            long values = ((long)(me.Id + 2) << 50) ^ ((long)(foe.Id + 2) << 40) ^ ((long)(int)me.Status << 36) ^ ((long)(int)foe.Status << 32)
                ^ ((long)(me.Hp & 0xFF) << 24) ^ ((long)(foe.Hp & 0xFF) << 16) ^ ((long)(me.Fatigue & 0xFF) << 8) ^ (long)(foe.Fatigue & 0xFF)
                ^ (seen.Out[0] ? 1L << 60 : 0) ^ (seen.Out[1] ? 1L << 61 : 0);
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            return (Ready, flow.BattleOver, rollEntry != null, hidden.Count, seq != null && seq.Current != null ? seq.Current.Seq : -1, Counted, calc, values);
        }

        // ── 그리기 ──

        /// <summary>보기·입력 상태가 바뀌었다: DS 화면 · 기둥 둘 · 독 · 결과 창을 지금 값으로 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null) return;
            var feed = flow.Battle;
            var client = flow.Client;
            if (feed == null || client == null || client.View == null) return;
            stage.Tips.Hide();
            Read();
            painted = Signature();
            bool open = Open;
            var choice = client.FirstChoice();
            if (feed.Ended && choice != null && choice.Kind != ChoiceKind.Reward) choice = null;   // 끝난 배틀: 배틀 뒤 선택만 받는다(다음 턴의 선택은 [확인] 뒤 보드가 받는다)
            var legal = choice != null ? client.Legal(choice) : new List<ChoiceAnswer>();
            var me = Shown(seen.Active[feed.MySide]);
            var foe = Shown(seen.Active[1 - feed.MySide]);
            int preview = FoePreview(feed, foe);
            if (preview >= 0) foe = flow.Battle.MonOf(preview);
            FoePreviewed = preview >= 0;
            bool acting = choice != null && choice.Kind == ChoiceKind.BattleAction;
            AskingToken = choice != null && choice.Kind == ChoiceKind.Reaction && choice.Sub == ChoiceSub.InitiativeToken;
            AskingOrder = choice != null && choice.Kind == ChoiceKind.Reaction && choice.Sub == ChoiceSub.EffectOrder;
            AskingFate = choice != null && choice.Kind == ChoiceKind.Reaction && choice.Sub == ChoiceSub.Fate;
            AskingAsk = choice != null && choice.Kind == ChoiceKind.Reaction && (choice.Sub == ChoiceSub.AlluringStew || choice.Sub == ChoiceSub.EffectOption);
            AskingPick = choice != null && choice.Kind == ChoiceKind.Reaction && choice.Sub == ChoiceSub.EffectPick;
            AskingRespond = choice != null && choice.Kind == ChoiceKind.Respond;
            if (choice == null || choice.Id != askedId)
            {
                fateDice.Clear();
                fateWild = 0;
                askedId = choice != null ? choice.Id : -1;
            }
            if (!acting || !legal.Exists(a => a.Kind == AnswerKind.ThrowBall)) bag = false;
            if (!acting || !legal.Exists(a => a.Kind == AnswerKind.UseMove && a.Target == BattleRules.AlphaStrike)) alphaOpen = false;
            if (!acting || bag || alphaOpen || !legal.Exists(IsPartyMove)) partyOpen = false;
            if (!acting || bag || alphaOpen || partyOpen || !legal.Exists(IsPotion)) abilityOpen = false;
            actingNow = acting;
            // 차례인 쪽(55행): 입력이 열려 있고 내가 고를 것이 있으면 내 쪽, 상대(AI)의 답을 기다리는 중이면 상대 쪽 상자가 흔들린다
            bobSide = !Ready || seen.Ended || flow.BattleOver ? -1 : choice != null ? feed.MySide : 1 - feed.MySide;

            monCells.Clear();   // 기둥(상대 그림 — 결정 182)과 독의 포켓몬 칸이 다시 건다
            FillInfo(feed, me, foe);
            FillExtras(feed, me, foe);   // 우두머리 · 응징 · 교체 불가 · 둘째 상태 이상 · 무리 진행(M5 「맵 결과·배틀 안 연출」)
            FillLog(feed);
            FillDock(feed, me, foe, choice, legal, open);
            FillResult(feed, choice, legal);
            Animate();
        }

        /// <summary>
        /// 다시 짓지 않고 그 시각의 모습으로 보낸다: DS 화면을 다시 찍고, 굴림판이 떠 있으면 주사위를 그 시각의 자세로 그리고 고리·꼬리표·결과 줄 아이콘의 opacity·scale을 맞춘다.
        /// 값은 모두 지금 항목과 그 안 시각에서만 나온다(앞 호출에 기대지 않는다 — 한 번에 보내도 나눠 보내도 같다).
        /// </summary>
        void Animate()
        {
            if (rollEntry != null && rollPanel != null)
            {
                if (rollIsCapture) FxUi.Fade(rollPanel, Tween.Progress(local, capStep.StartMs, BattleBeats.RingMs));   // 포획 굴림은 던짐과 함께 뜬다
                else FxUi.Fade(rollPanel, Tween.Progress(local, BattleRoll.StepOf(rollEntry).StartMs + BattleBeats.DockSwapStartMs, BattleBeats.DockSwapMs));
                if (board != null && rollScene != null && rollBoard != null && !(ReferenceEquals(renderedFor, rollEntry) && renderedMs == local))
                {
                    rollScene.Pose(local, poses, out var glitch);
                    board.Render(poses, rollScene.Dice, glitch);
                    rollBoard.MarkDirtyRepaint();
                    renderedFor = rollEntry;
                    renderedMs = local;
                }
                for (int k = 0; k < rollRings.Count; k++)
                {
                    int at = CountAt(k);
                    FxUi.Fade(rollRings[k], local >= at && local < at + BattleBeats.RingMs ? 1f : 0f);
                }
                for (int k = 0; k < rollFaces.Count; k++)
                {
                    float s = Tween.At(local, CountAt(k), BattleBeats.CountPopMs, 1.4f, 1f, Ease.OutQuad);
                    FxUi.Scale(rollFaces[k], s, s);
                }
                foreach (var tag in rollTags) FxUi.Fade(tag.Value, local >= tag.Key ? 1f : 0f);
            }
            AnimateReroll();
            // 결과 창(70·94행): 받은 것이 하나씩 내려앉는다. [확인]은 그 항목이 다 재생된 뒤에
            int resultMs = ResultTime;
            foreach (var drop in resultDrops)
            {
                float u = Tween.Apply(Ease.OutQuad, Tween.Progress(resultMs, drop.Key, EnterBeats.ResultDropMs));
                FxUi.Fade(drop.Value, u);
                FxUi.Move(drop.Value, 0f, -EnterBeats.ResultDropPx * (1f - u));
            }
            if (resultFoot != null) resultFoot.style.visibility = flow.BattleOver ? Visibility.Visible : Visibility.Hidden;
            PaintAiLine();
            AnimateRows();
            AnimateSpoils();
            PaintDs();
        }

        // AI 자막(73행): 사유가 있는 AI 답이면 DS 위 가장자리 띠가 왼쪽에서 미끄러져 들어와 머물다 걷힌다(그 뒤 기술 연출)
        void PaintAiLine()
        {
            if (aiLine == null) return;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var step = seq != null ? AiLineBeats.StepOf(seq.Current) : null;
            float slide = -1f, alpha = 0f;
            if (step != null && local >= step.StartMs) AiLineBeats.At(local - step.StartMs, step.Ms, out slide, out alpha);
            AiLine = alpha > 0f ? AiText(AiLineBeats.Answer(step)) : null;
            if (AiLine != null && aiText.text != AiLine) aiText.text = AiLine;
            FxUi.Fade(aiLine, alpha);
            aiLine.style.translate = new Translate(Length.Percent(Mathf.Round(slide * 100f)), 0f);
        }

        string AiText(ChoiceAnswered a)
        {
            var inv = CultureInfo.InvariantCulture;
            string ub = flow.Battle != null ? BattleFeed.UbFoe(flow.Battle.Kind) : null;   // 울트라 비스트 · 네크로즈마 · 호수의 수호신(M6 「UB 배틀 화면 묶음」)
            string who = stage.T(a.Seat == SeatId.Galactic ? "screen.battle.foe.galactic" + (flow.Battle != null ? flow.Battle.LeaderSuffix : "") : ub != null ? "screen.battle.foe." + ub : a.Seat == SeatId.Wild ? "screen.battle.foe.wild" : "screen.battle.foe.other");
            int bonus = a.Reason.Bonus;
            string head = a.Reason.Rule == AiRule.Lookahead ? "screen.battle.ai.line.read" : "screen.battle.ai.line";   // 강한 AI 「수읽기(승산 n%)」(M8 「연결」)
            string line = stage.T(bonus != 0 ? head + ".bonus" : head);
            return line.Replace("{who}", who).Replace("{rule}", stage.T(AiLineBeats.RuleKey(a.Reason.Rule))).Replace("{power}", a.Reason.Power.ToString(inv))
                .Replace("{bonus}", (bonus > 0 ? "+" : "−") + Math.Abs(bonus).ToString(inv));
        }

        // 배틀 화면의 포켓몬 이름은 폼 꼬리 「(…)」를 뺀다(결정 — 이름 잘림: 독 차례 칸 · DS 체력 상자·메시지 · 기록 줄. 전체 이름은 왼쪽 기둥 이름의 마우스 오버 — FullName)
        string MonName(BattleMon mon) => mon.Exists ? BattleName("pokemon." + flow.Ctx.Cards.Pokemon[mon.Def].Key) : "";
        string FullName(BattleMon mon) => mon.Exists ? stage.T("pokemon." + flow.Ctx.Cards.Pokemon[mon.Def].Key) : "";

        const string PokemonKey = "pokemon.";

        /// <summary>기록 줄·DS 메시지의 글 찾기: 포켓몬 이름은 폼 꼬리를 뺀다. 「form:」을 앞에 단 키(폼 바꾸기 줄의 새 폼)는 폼 꼬리만(없으면 이름).</summary>
        string LineT(string key)
        {
            if (key != null && key.StartsWith(BattleFeed.FormArg, StringComparison.Ordinal))
            {
                string full = stage.T(key.Substring(BattleFeed.FormArg.Length));
                int open = full.IndexOf('('), close = full.LastIndexOf(')');
                return open > 0 && close > open ? full.Substring(open + 1, close - open - 1) : full;
            }
            return key != null && key.StartsWith(PokemonKey, StringComparison.Ordinal) ? BattleName(key) : stage.T(key);
        }

        // DS 화면의 포켓몬 하나: 그리는 값 + 그 순간의 움직임(지금 항목의 단계 · 차례인 쪽의 흔들림)
        DsMon ToDs(BattleMon mon, int side, bool enemy)
        {
            if (!mon.Exists) return null;
            var pose = BattlePose.Of(flow.Fx != null ? flow.Fx.Sequencer : null, mon.Id, side, enemy, seen != null ? seen.Winding : -1);
            return new DsMon
            {
                Key = flow.Ctx.Cards.Pokemon[mon.Def].Key, Name = BoxName(mon), Narrow = BoxName(mon) != MonName(mon), Hp = mon.Hp, MaxHp = mon.MaxHp, Speed = mon.Speed, Fatigue = mon.Fatigue, Status = mon.Status, Status2 = mon.Status2,
                Standing = !seen.Out[side] && !pose.Blink, Dx = pose.Dx, Dy = pose.Dy, Sink = pose.Sink, Bar = pose.Draining ? pose.Bar : -1f, BoxDx = pose.BoxDx, BoxShown = !pose.BoxHidden,
                BoxDy = pose.BoxDy + (side == bobSide && BobPhase == 1 ? 1 : 0),
            };
        }

        // DS 화면을 지금 드러난 모습으로 다시 찍는다(값은 모두 Shown에서)
        void PaintDs()
        {
            if (ds == null || seen == null) return;
            var feed = flow.Battle;
            int mine = feed.MySide, other = 1 - mine;
            var me = Shown(seen.Active[mine]);
            var foe = Shown(seen.Active[other]);
            // 선출 중(결정 143 ⑤): 첫 출전을 고르는 동안에도 상대 자리에 야생 쪽 상대 포켓몬(체력 상자 없이 — 기둥이 미리 보인다, T14)
            lineupNow = Lineup(feed);
            int ahead = lineupNow && !foe.Exists && FxRows.WildLike(feed.Kind) ? LineupFoe(feed) : -1;
            if (ahead >= 0) foe = Shown(ahead);
            // 잡았거나 도망친 야생은 무대에 없다(마지막으로 살아 있던 보기에는 아직 서 있다) — 끝이 화면에 드러난 뒤에 체력 상자도 뺀다
            bool gone = seen.Ended && (feed.Result == BattleResult.Captured || feed.Result == BattleResult.Fled);
            // 무대는 배틀이 시작된 라운드의 시간대를 따른다(결정 70 ⑥ — 배틀 하나는 한 라운드 안에서 끝난다, 한 번 고르면 그대로)
            if (!terrain)
            {
                Time = DayTimes.Of(flow.Client != null && flow.Client.View != null ? flow.Client.View.Round : 0);
                terrain = DsStage.Terrain(feed.Biome, feed.Climate, Time, out bg, out enemyPlatform, out playerPlatform);
            }
            var scene = new DsScene
            {
                Enemy = gone ? null : ToDs(foe, other, true), Player = ToDs(me, mine, false), Message = DsMessage(feed, me), Bg = bg, EnemyPlatform = enemyPlatform, PlayerPlatform = playerPlatform,
            };
            if (ahead >= 0 && scene.Enemy != null) scene.Enemy.BoxShown = false;
            LineupFoeShown = ahead >= 0 && scene.Enemy != null;
            Message = scene.Message ?? "";
            PaintCapture(feed, scene);
            PaintIntro(feed, scene);
            PaintFoeBall(feed, scene);
            PaintFoeTrainer(scene);
            PaintLineup(feed, scene);
            PaintSheets(feed, scene, me, foe);
            PaintToken(feed, scene);
            PaintRows(feed, scene, me, foe);   // 오라(76·77행 · 팬 폼) · 폼 바꾸기(78·79행)
            // 공간 일그러짐(M9 다듬기 P39): 기라티나의 그림자(44행)가 덮인 만큼 · 울트라 비스트 배틀이 열린 직후(22행의 뒤 절반) — 연출 「끄기」에서는 없다
            var warpSeq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (warpSeq != null && flow.FxSpeed != FxSpeed.Off)
            {
                scene.WarpPx = RippleBeats.Ds(warpSeq.Current, warpSeq.LocalMs);
                scene.WarpPhase = RippleBeats.Phase(warpSeq.NowMs, RippleBeats.DsPeriodMs);
            }
            ds.Draw(scene);
            Painted = scene;
            dsBox.MarkDirtyRepaint();
            paintedBob = BobPhase;
            paintedGlow = GlowPhase;
            paintedMote = MotePhase;
        }

        // 우선권 토큰(결정 73 ⑦ — 04 §4-2 「상자 옆」, 53·54행): 가진 쪽 체력 상자 옆에 놓이고(상자와 함께 움직이고 상자가 없으면 없다), 넘길 때는 두 자리 사이를 돌며 포물선으로 간다
        void PaintToken(BattleFeed feed, DsScene scene)
        {
            var pose = InitiativeBeats.Of(flow.Fx != null ? flow.Fx.Sequencer : null, seen.TokenSide, AskingToken && Open, ambientMs);
            if (pose.From < 0 || pose.To < 0) return;
            bool Spot(int side, out Vector2Int at)
            {
                bool mine = side == feed.MySide;
                var mon = mine ? scene.Player : scene.Enemy;
                at = (mine ? DsStage.PlayerBoxAt + DsStage.PlayerTokenFromBox : DsStage.EnemyBoxAt + DsStage.EnemyTokenFromBox);
                if (mon == null || !mon.BoxShown) return false;
                at += new Vector2Int(mon.BoxDx, mon.BoxDy);
                return true;
            }
            bool fromOk = Spot(pose.From, out var from), toOk = Spot(pose.To, out var to);
            if (pose.From == pose.To ? !fromOk : !(fromOk || toOk)) return;
            if (!fromOk) from = to;
            if (!toOk) to = from;
            scene.Token = new DsToken
            {
                X = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, pose.Travel)), Y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, pose.Travel)) - pose.Lift,
                Width = Mathf.RoundToInt(DsStage.TokenSize * pose.Width), Back = pose.Back, White = pose.White, Twinkle = pose.Twinkle,
            };
        }

        // 포획(68행): 내 주인공 뒷모습 · 볼 · 빨려 드는 포켓몬. 포획 단계가 시작한 뒤로 그 항목이 재생되는 동안, 그리고 잡은 배틀의 결과 창 동안 끝 모습이 남는다
        // (성공 = 뒷모습과 닫힌 볼 · 실패 = 아무것도 — 결정 54 ②). 뒷모습이 있는 동안 체력 상자 둘은 그리지 않는다(결정 62)
        void PaintCapture(BattleFeed feed, DsScene scene)
        {
            var shot = cap ?? (seen.Ended && feed.Result == BattleResult.Captured ? feed.LastCapture : null);
            Capture = default(CapturePose);
            if (shot == null) return;
            int t = cap != null ? local - capStep.StartMs : int.MaxValue / 2;
            // 볼이 나오는 자리 = 그 주인공의 손(표의 hand — 결정 61 ①). 뒷모습 표가 없으면 4세대 손 자리
            var back = Thrower();
            int handX = DsStage.TrainerAt.x + DsStage.HandFrom.x, handY = DsStage.TrainerAt.y + DsStage.HandFrom.y;
            if (back != null) back.HandAt(DsStage.TrainerAt.x, DsStage.TrainerAt.y, out handX, out handY);
            var pose = CaptureStage.Pose(shot, t, cap != null ? capStep.Ms : 0, handX, handY, DsStage.EnemyFoot.x, DsStage.EnemyFoot.y);
            Capture = pose;
            if (scene.Enemy != null)
            {
                scene.Enemy.Standing = scene.Enemy.Standing && pose.Mon;
                scene.Enemy.White = pose.MonWhite;
                scene.Enemy.Scale = pose.MonScale;
                scene.Enemy.ShrinkAbove = EnterBeats.OpenAbovePx;
            }
            // 뒷모습이 나와 있는 동안 체력 상자 둘은 없다(결정 62) — 상대 것은 왼쪽, 내 것은 오른쪽으로 물러났다 돌아온다
            if (scene.Enemy != null) { scene.Enemy.BoxShown = scene.Enemy.BoxShown && !pose.BoxHidden; scene.Enemy.BoxDx -= pose.BoxAway; }
            if (scene.Player != null) { scene.Player.BoxShown = scene.Player.BoxShown && !pose.BoxHidden; scene.Player.BoxDx += pose.BoxAway; }
            if (pose.Trainer && back != null)
            {
                back.Cell(back.FrameAt(pose.TrainerMs, EnterBeats.ReleaseMs), out int sx, out int sy);
                scene.Back = new DsSprite
                {
                    Key = "Trainers/" + back.File, Sx = sx, Sy = sy, Sw = back.Fw, Sh = back.Fh,
                    X = DsStage.TrainerAt.x - back.OriginX + pose.TrainerDx, Y = DsStage.TrainerAt.y - back.OriginY,
                };
            }
            if (!pose.Ball) return;
            string item = BattleFeed.BallItem(shot.BallKey);
            scene.Ball = pose.BallFrame < 0
                ? new DsSprite { Key = DsStage.BallFolder + item + "_open.png", Sw = DsStage.BallOpen, Sh = DsStage.BallOpen, X = pose.BallX - DsStage.BallOpen / 2, Y = pose.BallY - 11 }
                : new DsSprite { Key = DsStage.BallFolder + item + ".png", Sx = pose.BallFrame * DsStage.BallCell, Sw = DsStage.BallCell, Sh = DsStage.BallCell, X = pose.BallX - DsStage.BallCell / 2, Y = pose.BallY - DsStage.BallCell / 2, Dark = pose.BallDark };
            scene.Stars = pose.Stars;
            scene.StarsAt = new Vector2Int(pose.BallX, pose.BallY);
        }

        /// <summary>인트로의 첫 출전(53행 — 결정 99 ②)의 지금 모습: 주인공 뒷모습 · 볼(없으면 기본값). DS에 찍은 것은 <see cref="Painted"/>의 <c>Back</c>·<c>Ball</c>.</summary>
        public IntroPose Intro { get; private set; }

        // 배틀 시작의 첫 출전(53행 — 결정 99 ②, 원작 Pt): 내 쪽 주인공 뒷모습이 들어와 서 있다가 볼을 던지고, 볼이 열리며 포켓몬이 나온 뒤 물러난다(IntroStage).
        // 남의 배틀을 볼 때도 아래쪽(feed.MySide) 좌석의 주인공이 던진다. 쓰러진 뒤의 다음 포켓몬·교체에는 없다
        void PaintIntro(BattleFeed feed, DsScene scene)
        {
            Intro = default(IntroPose);
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var step = cur != null ? cur.Step(FxRows.Intro) : null;
            if (step == null || local < step.StartMs || local >= step.EndMs) return;
            SentIn mine = null;
            foreach (var e in step.Events)
                if (e is SentIn si && si.Side == feed.MySide && IntroStage.Thrown(cur, step, si)) mine = si;
            if (mine == null) return;
            int at = FxReveal.At(cur, mine);
            var back = Thrower();
            int handX = DsStage.TrainerAt.x + DsStage.HandFrom.x, handY = DsStage.TrainerAt.y + DsStage.HandFrom.y;
            if (back != null) back.HandAt(DsStage.TrainerAt.x, DsStage.TrainerAt.y, out handX, out handY);
            // 첫 출전을 고른 항목이면 고르는 동안 뒷모습이 이미 서 있었다(결정 143 ⑤ — PaintLineup) — 들어오지 않고 단계 처음부터 서 있다
            var pose = IntroStage.Pose(local - at, at - step.StartMs, handX, handY, DsStage.PlayerFoot.x, DsStage.PlayerFoot.y, cur.Kind == ActionKind.SendIn);
            Intro = pose;
            if (scene.Player != null && pose.MonScale < 1f)
            {
                scene.Player.White = pose.MonWhite;
                scene.Player.Scale = pose.MonScale;
                scene.Player.ShrinkAbove = EnterBeats.IntroOpenAbovePx;
            }
            if (pose.Trainer && back != null)
            {
                // 던지기 전에는 서 있는 프레임, 던진 뒤는 포획과 같은 박자(손을 떠나는 틱 = 260ms)
                back.Cell(pose.ThrowMs < 0 ? back.Still : back.FrameAt(pose.ThrowMs, EnterBeats.ReleaseMs), out int sx, out int sy);
                scene.Back = new DsSprite
                {
                    Key = "Trainers/" + back.File, Sx = sx, Sy = sy, Sw = back.Fw, Sh = back.Fh,
                    X = DsStage.TrainerAt.x - back.OriginX + pose.TrainerDx, Y = DsStage.TrainerAt.y - back.OriginY,
                };
            }
            if (!pose.Ball) return;
            const string item = "poke_ball";   // 출전의 볼은 늘 몬스터볼(SentIn은 볼 종류를 싣지 않는다)
            scene.Ball = pose.BallFrame < 0
                ? new DsSprite { Key = DsStage.BallFolder + item + "_open.png", Sw = DsStage.BallOpen, Sh = DsStage.BallOpen, X = pose.BallX - DsStage.BallOpen / 2, Y = pose.BallY - 11 }
                : new DsSprite { Key = DsStage.BallFolder + item + ".png", Sx = pose.BallFrame * DsStage.BallCell, Sw = DsStage.BallCell, Sh = DsStage.BallCell, X = pose.BallX - DsStage.BallCell / 2, Y = pose.BallY - DsStage.BallCell / 2 };
        }

        /// <summary>인트로의 상대 트레이너 볼(53행 — M9 다듬기 P05)의 지금 모습(없으면 기본값). DS에 찍은 볼은 <see cref="Painted"/>의 <c>Fx</c>에 든다.</summary>
        public IntroPose FoeIntro { get; private set; }

        // 상대 트레이너의 첫 출전(53행 — M9 다듬기 P05): 원작 자산에 상대의 던지는 앞모습이 없어 트레이너는 물러나고 볼만 오른쪽 화면 밖에서 날아와 열린다(IntroStage.FoePose).
        // 갤럭시단 · 도전 · 트레이너전 · 배신자 — 야생 · 울트라 비스트는 그대로 미끄러져 들어온다
        void PaintFoeBall(BattleFeed feed, DsScene scene)
        {
            FoeIntro = default(IntroPose);
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var step = cur != null ? cur.Step(FxRows.Intro) : null;
            if (step == null || local < step.StartMs || local >= step.EndMs) return;
            SentIn foe = null;
            foreach (var e in step.Events)
                if (e is SentIn si && si.Side != feed.MySide && IntroStage.FoeThrown(cur, step, si)) foe = si;
            if (foe == null) return;
            int at = FxReveal.At(cur, foe);
            var pose = IntroStage.FoePose(local - at, at - step.StartMs, DsStage.EnemyFoot.x, DsStage.EnemyFoot.y);
            FoeIntro = pose;
            if (scene.Enemy != null && pose.MonScale < 1f)
            {
                scene.Enemy.White = pose.MonWhite;
                scene.Enemy.Scale = pose.MonScale;
                scene.Enemy.ShrinkAbove = EnterBeats.OpenAbovePx;
            }
            if (!pose.Ball) return;
            const string item = "poke_ball";
            if (scene.Fx == null) scene.Fx = new List<DsSprite>();
            scene.Fx.Add(pose.BallFrame < 0
                ? new DsSprite { Key = DsStage.BallFolder + item + "_open.png", Sw = DsStage.BallOpen, Sh = DsStage.BallOpen, X = pose.BallX - DsStage.BallOpen / 2, Y = pose.BallY - 11 }
                : new DsSprite { Key = DsStage.BallFolder + item + ".png", Sx = pose.BallFrame * DsStage.BallCell, Sw = DsStage.BallCell, Sh = DsStage.BallCell, X = pose.BallX - DsStage.BallCell / 2, Y = pose.BallY - DsStage.BallCell / 2 });
        }

        // 도트 이펙트(결정 69 — SheetBeats): 맞는 불꽃 · 기절 먼지 · 볼 열림 빛 · 포획 반짝임. 대기열 시각만 읽는다(빠름·끄기를 따라온다)
        void PaintSheets(BattleFeed feed, DsScene scene, BattleMon me, BattleMon foe)
        {
            stamps.Clear();
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            // 야생은 볼에서 나오지 않는다 — 상대 쪽 빛은 트레이너(갤럭시단) 배틀에서만
            if (foe.Exists) SheetBeats.Battle(seq, foe.Id, true, feed.Kind == BattleKind.Galactic, DsStage.EnemyFoot.x, DsStage.EnemyFoot.y, stamps);
            if (me.Exists) SheetBeats.Battle(seq, me.Id, false, true, DsStage.PlayerFoot.x, DsStage.PlayerFoot.y, stamps);
            if (cap != null) SheetBeats.Capture(cap, local - capStep.StartMs, DsStage.EnemyFoot.x, DsStage.EnemyFoot.y, stamps);
            foreach (var st in stamps)
            {
                var sprite = SheetArt.Ds(st);
                if (sprite == null) continue;
                if (scene.Fx == null) scene.Fx = new List<DsSprite>();
                scene.Fx.Add(sprite);
            }
        }

        readonly List<FxStamp> stamps = new List<FxStamp>();

        // 갤럭시단 등장(72행): 조무래기 앞모습이 상대 자리로 미끄러져 들어와 섰다가 물러난다(그 뒤 인트로에서 포켓몬이 나온다)
        void PaintFoeTrainer(DsScene scene)
        {
            FoeTrainerShown = false;
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var step = cur != null ? cur.Step(EnterRows.Galactic) : null;
            if (step == null || local < step.StartMs || local >= step.EndMs) return;
            int u = local - step.StartMs;
            // 첫 출전을 고르는 동안이면 물러나지 않고 서 있는다(결정 143 ⑤ — 그 뒤는 PaintLineup이 같은 자리에 세운다)
            float away = 1f - Tween.Apply(Ease.OutQuad, Tween.Progress(u, 0, EnterBeats.FoeInMs)) + (lineupNow ? 0f : Tween.Apply(Ease.InQuad, Tween.Progress(u, step.Ms - EnterBeats.FoeOutMs, EnterBeats.FoeOutMs)));
            scene.Foe = new DsSprite { Key = FoeKey, Sw = 80, Sh = 80, X = DsStage.EnemyFoot.x - 40 + Mathf.RoundToInt(EnterBeats.FoeSlidePx * away), Y = DsStage.EnemyFoot.y - 78 };
            FoeTrainerShown = true;
            // 라인업 공개(72행 S54 — 결정 74): 명단의 볼이 하나씩 위에서 톡 떨어져 줄로 놓인다(간부 = 조각 수 + 2 · 태홍 6). 물러날 때 함께 걷힌다
            LineupShown = 0;
            var lineup = LineupBeats.Lineup(step);
            if (lineup == null || (!lineupNow && u >= step.Ms - EnterBeats.FoeOutMs)) return;
            for (int i = 0; i < lineup.LineupCount; i++)
            {
                float drop = LineupBeats.Drop(u, i);
                if (drop < 0f) break;
                if (scene.Fx == null) scene.Fx = new List<DsSprite>();
                scene.Fx.Add(new DsSprite { Key = LineupBallKey, Sx = 0, Sy = LineupBallSy, Sw = DsStage.BallCell, Sh = DsStage.BallCell,
                    X = LineupAt.x + LineupGap * i, Y = LineupAt.y - Mathf.RoundToInt(LineupBeats.DropPx * drop) });
                LineupShown++;
            }
        }

        // 아래쪽(내 쪽) 좌석 주인공의 뒷모습 던지기 한 벌(Gen 표에서 — 없으면 null: 뒷모습 없이 볼만 난다). 남의 배틀을 볼 때는 그 좌석의 주인공(결정 53 ④)
        BackThrow Thrower()
        {
            if (throwerTried) return thrower;
            throwerTried = true;
            var table = GenAssets.Load<TextAsset>(BoardPieces.TableKey);
            var state = flow.Battle.Source;
            int owner = state != null && state.Battle != null ? state.Battle.Sides[flow.Battle.MySide].Owner : -1;
            ThrowerSeat = owner >= 0 ? owner : flow.Client.Seat;
            thrower = table != null ? BackThrow.Find(table.text, flow.Protagonist(ThrowerSeat)) : null;
            return thrower;
        }

        bool lineupNow;

        /// <summary>선출 중(결정 143 ⑤)에 DS 상대 자리에 그린 것: 야생 쪽 상대 포켓몬 · 트레이너 앞모습(갤럭시단 · 좌석 주인공)의 그림 키(없으면 null) · 내 자리의 주인공 뒷모습 — 확인용.</summary>
        public bool LineupFoeShown { get; private set; }
        public string LineupFrontShown { get; private set; }
        public bool LineupBackShown { get; private set; }

        /// <summary>
        /// 첫 출전을 고르는 중인가(결정 143 ⑤): 배틀의 누군가가 첫 출전(SendIn)을 아직 답하지 않았거나, 그 답의 항목(인트로)이 재생 중이다.
        /// 쓰러진 뒤의 다음 포켓몬(NextPokemon)은 아니다.
        /// </summary>
        bool Lineup(BattleFeed feed)
        {
            if (seen.Ended || flow.BattleOver) return false;
            var state = feed.Source;
            if (state != null && state.Battle != null && state.Pending.Exists(p => !p.Answered && p.Kind == ChoiceKind.SendIn)) return true;
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            return cur != null && cur.Kind == ActionKind.SendIn && cur.Step(FxRows.Intro) != null;
        }

        // 선출 중의 야생 쪽 상대(T14 기둥 미리 보기와 같은 마리): 나와 있으면 그것, 아니면 명단 첫 마리
        int LineupFoe(BattleFeed feed)
        {
            var state = feed.Source;
            if (state == null || state.Battle == null) return -1;
            var side = state.Battle.Sides[1 - feed.MySide];
            return side.Active >= 0 ? side.Active : side.Roster.Count > 0 ? side.Roster[0] : -1;
        }

        /// <summary>
        /// 선출 중(결정 143 ⑤ — 사용자):
        /// 아직 출전이 드러나지 않은 쪽 자리에 — 내 쪽 = 그 좌석 주인공의 뒷모습(서 있는 프레임 · <see cref="GameFlow.Protagonist"/>), 상대 쪽 = 트레이너의 앞모습
        /// (갤럭시단 = 조무래기 · 간부 · 태홍, 좌석 = 그 플레이어 주인공 — 야생 쪽 포켓몬은 <see cref="PaintDs"/>가 Enemy로). 상대 트레이너는 상대 첫 출전의 인트로가 시작하면 물러난다(볼이 날아온다).
        /// </summary>
        void PaintLineup(BattleFeed feed, DsScene scene)
        {
            LineupFrontShown = null;
            LineupBackShown = false;
            if (!lineupNow) return;
            int mine = feed.MySide, other = 1 - mine;
            if (seen.Active[mine] < 0 && scene.Back == null && !Intro.Trainer)
            {
                var back = Thrower();
                if (back != null)
                {
                    back.Cell(back.Still, out int sx, out int sy);
                    scene.Back = new DsSprite
                    {
                        Key = "Trainers/" + back.File, Sx = sx, Sy = sy, Sw = back.Fw, Sh = back.Fh,
                        X = DsStage.TrainerAt.x - back.OriginX, Y = DsStage.TrainerAt.y - back.OriginY,
                    };
                    LineupBackShown = true;
                }
            }
            if (seen.Active[other] >= 0 || FxRows.WildLike(feed.Kind) || FoeTrainerShown || scene.Foe != null) return;
            string key = null;
            if (feed.Kind == BattleKind.Galactic) key = FoeKey;
            else
            {
                var state = feed.Source;
                int owner = state != null && state.Battle != null ? state.Battle.Sides[other].Owner : -1;
                if (owner >= 0) key = FaceBox.ArtKey(flow.Protagonist(owner));
            }
            if (key == null) return;
            // 상대 첫 출전의 인트로(볼이 오른쪽 밖에서 날아온다 — IntroStage.FoePose)가 시작하면 트레이너는 오른쪽으로 물러난다
            float away = 0f;
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var intro = cur != null ? cur.Step(FxRows.Intro) : null;
            if (intro != null && local >= intro.StartMs && intro.Events.Exists(e => e is SentIn si && si.Side == other))
                away = Tween.Apply(Ease.InQuad, Tween.Progress(local - intro.StartMs, 0, EnterBeats.FoeOutMs));
            if (away >= 1f) return;
            scene.Foe = new DsSprite { Key = key, Sw = 80, Sh = 80, X = DsStage.EnemyFoot.x - 40 + Mathf.RoundToInt(EnterBeats.FoeSlidePx * away), Y = DsStage.EnemyFoot.y - 78 };
            LineupFrontShown = key;
        }

        // 메시지 창에 올리지 않는 기록 줄: 숫자 줄(주사위 눈 · 피해 · 남은 체력 · 포획 주사위) — 메시지 창은 Pt 문장만(결정 43 ⑧), 숫자는 굴림판·계산 줄·체력 상자가 보인다
        static bool Quiet(string key) =>
            key == BattleFeed.K + "dice" || key == BattleFeed.K + "damage" || key == BattleFeed.K + "hp" || key == BattleFeed.K + "hp.up" || key == BattleFeed.K + "capture.roll";

        /// <summary>
        /// DS 메시지 창의 글: 입력이 열려 있고 내가 고를 차례면 「○○은(는) 무엇을 할까?」, 토큰을 묻는 중이면 그 물음,
        /// 아니면 연출이 지금까지 드러낸 마지막 문장(「○○의 △△!」 → 「효과가 굉장했다!」 → 「○○은(는) 쓰러졌다!」 … — 숫자 줄은 빼고).
        /// </summary>
        string DsMessage(BattleFeed feed, BattleMon me)
        {
            if (Open && AskingToken) return stage.T("screen.battle.guide.token");
            if (Open && actingNow && me.Exists) return KoJosa.Fix(stage.T("screen.battle.ds.ask").Replace("{mon}", MonName(me)));
            if (cap != null && local < capStep.EndMs)
            {
                // 포획 단계(68행): 던지는 동안 「○○을(를) 던졌다!」, 판정 뒤 결말 대사(성공 +350 · 실패 +250 — 결정 54 ①). 기록 줄은 판정 때 한꺼번에 붙는다
                bool told = local - capStep.StartMs >= CaptureStage.LineMs(cap);
                foreach (var line in feed.Lines)
                    if (ReferenceEquals(line.Event, cap) && !Quiet(line.Key) && (line.Key == BattleFeed.K + "ball") != told) return line.Text(LineT);
                return "";
            }
            // 가장 늦게 드러난 문장(같은 때면 뒤에 적힌 것): 재생 중인 항목의 줄은 드러난 때로 견준다 — 받은 순서와 드러나는 순서가 다를 수 있다
            // (인트로는 야생이 먼저 나오고 내 포켓몬이 그 뒤에 나오지만 이벤트는 내 쪽이 먼저다). 그 앞 항목의 줄은 받은 순서 그대로다
            var playing = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            BattleLine last = null;
            int lastAt = int.MinValue;
            for (int i = feed.Lines.Count - 1; i >= 0; i--)
            {
                var line = feed.Lines[i];
                if (Quiet(line.Key) || !feed.Shows(line, hidden)) continue;
                int at = playing != null && line.Event != null && FxReveal.TryAt(playing, line.Event, out int ms) ? ms : -1;
                if (at > lastAt)
                {
                    last = line;
                    lastAt = at;
                }
                if (at < 0) break;
            }
            // 준비 동작(결정 143 ⑦): 기술을 고른 뒤 맞는 순간(59행)의 줄이 드러나기 전까지 — 굴리는 동안 · 재굴림 창이 열린 동안 — 「○○의 △△!」(그보다 늦게 드러난 줄이 있으면 그 줄)
            var prep = feed.PrepLine(seen);
            if (prep != null && (last == null || last.Event == null || feed.Events.IndexOf(last.Event) < feed.Events.IndexOf(prep.Event))) return prep.Text(LineT);
            if (last == null) return "";
            if (!ReferenceEquals(last, messageLine))
            {
                messageLine = last;
                messageText = last.Text(LineT);
                // 부른 기술(흉내내기·손가락흔들기)은 기술 줄과 같은 때 드러난다 — 「○○의 손가락흔들기!」 아래 줄에 「△△이(가) 나왔다!」
                int at = feed.Lines.IndexOf(last);
                if (last.Key == BattleFeed.K + "move.called" && at > 0 && ReferenceEquals(feed.Lines[at - 1].Event, last.Event))
                    messageText = feed.Lines[at - 1].Text(LineT) + "\n" + last.Text(k => k == last.Key ? stage.T("screen.battle.ds.called") : LineT(k));
            }
            return messageText;
        }

        // 우선권 토큰의 그림(결정 73 ⑦ — DS 상자 옆과 같은 16×16 도트, 정수배) + 마우스 오버 이름표 「우선권 토큰」과 짧은 설명. 그림이 없으면 글자 배지
        void Token(VisualElement line, string name)
        {
            var tex = GenAssets.Load<Texture2D>(DsStage.TokenKey);
            VisualElement token;
            if (tex != null)
            {
                token = Add(line, new VisualElement(), name, "sn-token-icon");
                token.style.backgroundImage = new StyleBackground(tex);
            }
            else token = Add(line, Kit.Badge(stage.T("screen.battle.token"), KitBadgeKind.Gold, Kit.F2), name, null);
            stage.Tips.Hook(token, stage.T("screen.battle.token"), stage.T("screen.battle.token.tip"), TipSide.Below, reading: true);
        }

        // Pt 틀 32×16 그림(타입·상태). 그림이 없으면 글 배지로 대신한다
        void TypeIcon(VisualElement parent, string key, string fallback)
        {
            var tex = GenAssets.Load<Texture2D>(key);
            if (tex != null)
            {
                var icon = Add(parent, new VisualElement(), null, "sn-type-icon");
                icon.style.backgroundImage = new StyleBackground(tex);
            }
            else if (fallback != null) parent.Add(Kit.Badge(fallback, KitBadgeKind.Plain, Kit.F2));
        }

        /// <summary>알파 스트라이크 옆 배울 수 있는 타입 아이콘 묶음의 클래스(결정 182 — 상대 기술 칩 줄 · 내 기술 줄).</summary>
        public const string AlphaTypesClass = "sn-battle-alpha-types";

        // 알파 스트라이크가 쓰일 수 있는 타입(rulings A26 — 그 포켓몬의 배울 수 있는 타입, LegendRules.AlphaTypes): 작은 타입 그림 묶음 + 마우스 오버 「배우는 타입」(결정 182)
        void AlphaTypeIcons(VisualElement parent, PokemonState pk, string name)
        {
            var types = new List<int>(4);
            LegendRules.AlphaTypes(flow.Ctx, pk, types);
            if (types.Count == 0) return;
            var box = Add(parent, new VisualElement(), name, AlphaTypesClass);
            foreach (int t in types) TypeIcon(box, IconArt.Type((PokemonType)t), stage.T("ui.types." + (PokemonType)t));
            stage.Tips.Hook(box, stage.T("screen.party.learn"), stage.T("screen.battle.alpha.types.tip"), TipSide.Below, reading: true);
        }

        // 작은 아이템 그림(볼·규토리)
        static VisualElement Icon(VisualElement parent, string key)
        {
            var icon = Add(parent, new VisualElement(), null, "sn-icon");
            var tex = GenAssets.Load<Texture2D>(key);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            return icon;
        }

        void FillLog(BattleFeed feed)
        {
            logLines.Clear();
            int from = Math.Max(0, feed.Lines.Count - ShownLines);
            for (int i = from; i < feed.Lines.Count; i++)
            {
                var line = feed.Lines[i];
                if (!feed.Shows(line, hidden)) continue;   // 그 줄의 이벤트가 연출에서 드러난 뒤에 붙는다(결과가 먼저 새지 않게)
                var row = Add(logLines, new VisualElement(), null, "sn-battle-logline");
                var mark = Add(row, new VisualElement(), null, "sn-battle-mark");
                if (line.Side == feed.MySide) mark.AddToClassList("sn-battle-mark-mine");
                else if (line.Side >= 0) mark.AddToClassList("sn-battle-mark-foe");
                Add(row, Kit.Wrapped(line.Text(LineT), Kit.F2), null, "sn-battle-logtext");
                // 위로 넘친 옛 줄은 반쯤 잘려 보이지 않게 통째로 감춘다(자리는 그대로 — 배치가 바뀌지 않는다)
                row.RegisterCallback<GeometryChangedEvent>(e => row.style.visibility = row.layout.y < -0.5f ? Visibility.Hidden : Visibility.Visible);
            }
        }

        void FillDock(BattleFeed feed, BattleMon me, BattleMon foe, PendingChoice choice, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            dock.Clear();
            moves.Clear();
            balls.Clear();
            bench.Clear();
            orders.Clear();
            picks.Clear();
            abilityPicks.Clear();
            alphaTypes.Clear();
            partyMoves.Clear();
            rerollDice.Clear();
            RerollShown = false;
            bool acting = choice != null && choice.Kind == ChoiceKind.BattleAction;
            bool benching = choice != null && (choice.Kind == ChoiceKind.SendIn || choice.Kind == ChoiceKind.NextPokemon || choice.Kind == ChoiceKind.SwitchWindow);
            bool rewarding = choice != null && choice.Kind == ChoiceKind.Reward && RewardReady(choice.Sub);
            bool rolling = rollEntry != null;
            CanStay = benching && choice.Kind == ChoiceKind.SwitchWindow && legal.Exists(a => a.Kind == AnswerKind.Decline);
            if (ForfeitArmed && (!acting || !open || rolling || choice.Id != armedChoice || !legal.Exists(a => a.Kind == AnswerKind.Forfeit))) ForfeitArmed = false;   // 다른 선택 · 연출 · [기권] 꺼짐 = 풀림

            // 차례 칸: 연출이 도는 동안에는 지금 재생 중인 행동의 쪽을 적는다(내 다음 선택이 이미 열려 있어도 상대의 공격이 보이는 동안은 「상대의 차례」)
            var turn = Add(dock, new VisualElement(), "battle-turn", "sn-battle-turn");
            var playing = !Ready && flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            string title = seen.Ended ? stage.T("screen.battle.turn.end")
                : playing != null ? (playing.Seat == SideSeat(feed) && me.Exists ?stage.T("screen.battle.turn.mine").Replace("{mon}", MonName(me)) : stage.T("screen.battle.turn.foe"))
                : (acting || AskingOrder || AskingToken || AskingFate || AskingAsk || AskingPick) && me.Exists ? stage.T("screen.battle.turn.mine").Replace("{mon}", MonName(me))   // 우선권 토큰을 묻는 것도 내 차례(전에는 「상대의 차례」로 떴다)
                : !feed.Mine && me.Exists && flow.Client.View.Pending.Exists(p => p.Seat == SideSeat(feed) && p.Kind == ChoiceKind.BattleAction) ? stage.T("screen.battle.turn.mine").Replace("{mon}", MonName(me))   // 남의 배틀: 아래쪽 좌석이 기술을 고르는 중(M7 마감 감사 1)
                : benching ? stage.T("screen.battle.turn.bench")
                : stage.T("screen.battle.turn.foe");
            Add(turn, Kit.Text(title, Kit.F2), "battle-turn-title", "kit-title");
            if (me.Exists && foe.Exists)
            {
                var versus = Add(turn, new VisualElement(), "battle-turn-speed", "sn-battle-line");
                versus.Add(Kit.Pill(speed, me.Speed.ToString(inv), me.Speed < me.BaseSpeed ? KitTone.Low : me.Speed > me.BaseSpeed ? KitTone.High : KitTone.Base));
                versus.Add(Kit.Text(me.Speed > foe.Speed ? ">" : me.Speed < foe.Speed ? "<" : "=", Kit.F2));
                versus.Add(Kit.Pill(speed, foe.Speed.ToString(inv), foe.Speed < foe.BaseSpeed ? KitTone.Low : foe.Speed > foe.BaseSpeed ? KitTone.High : KitTone.Base));
                stage.Tips.Hook(versus, stage.T("screen.common.speed"), null, TipSide.Above, clear: dock);
            }
            // 앰프 · 장작(결정 87 V9): 배틀 중에 쓰는 수라 차례 칸에도 — 그 카드일 때만 한 줄
            if (feed.Source != null && !seen.Ended)
            {
                var cardLine = new VisualElement { name = CardState };
                cardLine.AddToClassList("sn-battle-line");
                if (CardAsk.StateChips(stage, flow.Ctx, cardLine, feed.Source.Seats[SideSeat(feed)], CardState, TipSide.Above, dock) > 0) turn.Add(cardLine);   // 아래쪽 좌석의 카드(남의 배틀이면 그 좌석 — 공개 값)
            }
            string guide = null;
            if (seen.Ended || feed.Ended) guide = null;
            else if (choice == null) guide = null;
            else if (!open) guide = "screen.board.guide.wait";
            else if (acting) guide = ForfeitArmed ? "screen.battle.forfeit.again" : bag ? "screen.battle.guide.ball" : alphaOpen ? "screen.battle.guide.alpha" : partyOpen ? "screen.battle.guide.partymoves" : abilityOpen ? "screen.card.guide.potion" : "screen.battle.guide.pick";
            else if (AskingToken) guide = "screen.battle.guide.token";
            else if (AskingOrder) guide = "screen.battle.guide.order";
            else if (AskingFate) guide = PsychicOn(legal) ? "screen.battle.guide.psychic" : legal.Exists(IsAmp) ? "screen.battle.guide.amp" : "screen.battle.guide.fate";   // psychic R(결정 131) · 앰프(결정 134)가 같은 창에 있으면
            else if (AskingAsk) guide = choice.Sub == ChoiceSub.AlluringStew ? "screen.battle.guide.stew" : "screen.battle.guide.option";
            else if (AskingPick) guide = "screen.battle.guide.effect_pick";   // 「○○: 하나를 선택하십시오.」(같은 키를 두 번 적어 기술 고르기 안내가 덮였었다)
            else if (AskingRespond) guide = choice.Sub == ChoiceSub.Trade ? "screen.battle.guide.trade" : "screen.battle.guide.challenge";
            else if (benching) guide = choice.Kind == ChoiceKind.SendIn ? "screen.battle.guide.sendin" : choice.Kind == ChoiceKind.NextPokemon ? "screen.battle.guide.next" : "screen.battle.guide.switch";
            else if (rewarding) guide = null;   // 결과 창이 묻는다
            else if (choice.Kind == ChoiceKind.Discard) guide = "screen.board.guide.wait";   // 운명 손패 버리기: 손패 창(S30)이 이 화면 위에서 받는다
            else
            {
                guide = "screen.board.guide.not_ready";   // 받을 화면이 아직 없는 선택(진화 분기·진화 옛 기술·종막 …): 대신 답하지 않는다(기본값으로 넘기는 선택은 GameFlow.PassEmptyReaction)
                flow.NoteNotReady();
            }
            // 트레이너 카드가 연 물음(출처 = 카드): 머리 「카드 — 능력」(마우스 오버 = 능력 문구) + 짧은 안내 — 기술 효과의 물음 꼴(기술 이름 자리)이 아니다
            bool cardAsk = (AskingAsk || AskingPick) && CardAsk.FromCard(choice);
            if (cardAsk && guide != null && open) guide = AskingAsk ? "screen.card.guide.option" : "screen.card.guide.pick";
            if (open && (cardAsk || (acting && abilityOpen)))
            {
                int card = cardAsk ? choice.Source.Id : CardAsk.CardOf(flow.Client.View, flow.Client.Seat), slot = cardAsk ? CardAsk.Slot(ctx, choice) : 2;
                var head = Add(turn, Kit.Wrapped(CardAsk.Head(stage, ctx, card, slot), Kit.F2), CardHead, "sn-battle-card-head");
                stage.Tips.Hook(head, null, CardAsk.Text(stage, ctx, card, slot), TipSide.Above, reading: true, clear: dock);
                turn.AddToClassList("sn-battle-turn-card");   // 영어 1080 벌: 카드 머리가 두 줄이라 안내 줄을 뺀다(USS sn-lang-en — 단추가 독 밖으로 밀린다, M9 lang3)
            }
            if (guide != null) Add(turn, Kit.Wrapped(stage.T(guide).Replace("{move}", AskMove(choice)).Replace("{name}", choice != null && choice.Subject >= 0 && choice.Subject < flow.Client.View.Seats.Length ? flow.SeatName(choice.Subject) : ""), Kit.F2), "battle-guide", null);
            if (AskingFate)
            {
                var passRow = Add(turn, new VisualElement(), null, "sn-foot-row");
                var pass = Add(passRow, Kit.Button(stage.T("screen.battle.fate.pass"), KitButtonKind.Normal, () => PressFatePass()), "battle-fate-pass", null);
                pass.SetEnabled(open && legal.Exists(a => a.Kind == AnswerKind.Decline));
                // 방 판(M7 화면 ② — 04 §5 ① 「이번 배틀 묻지 않기」 · 02 SetAutoPass [가정 M7-26]): 이 배틀 동안 내 운명 카드 반응 창을 묻지 않고 「안 씀」 — [넘기기] 옆 짧은 단추(뜻은 마우스 오버)
                if (flow.Room.InGame)
                {
                    var auto = Add(passRow, Kit.Button(stage.T("screen.battle.fate.autopass"), KitButtonKind.Normal, () => PressAutoPass()), AutoPassButton, null);
                    auto.SetEnabled(open);
                    if (open) stage.Tips.Hook(auto, null, stage.T("screen.battle.fate.autopass.tip"), TipSide.Above, reading: true, clear: dock);
                }
            }
            if (AskingAsk || AskingRespond || (AskingPick && legal.Exists(a => a.Kind == AnswerKind.Decline)))
            {
                var ask = Add(turn, new VisualElement(), null, "sn-foot-row");
                if (!AskingPick)
                {
                    var yes = Add(ask, Kit.Button(stage.T(AskingRespond ? "screen.prompt.accept" : "screen.battle.ask.yes"), KitButtonKind.Main, () => { if (AskingRespond) PressRespond(true); else PressAsk(true); }), "battle-ask-yes", null);
                    yes.SetEnabled(open && legal.Exists(a => a.Kind == AnswerKind.Accept));
                    if (open && choice.Sub == ChoiceSub.AlluringStew) stage.Tips.Hook(yes, null, stage.T("screen.battle.ask.stew.tip"), TipSide.Above, reading: true, clear: dock);
                }
                var no = Add(ask, Kit.Button(stage.T(AskingRespond ? "screen.prompt.decline" : "screen.battle.fate.pass"), KitButtonKind.Normal, () => { if (AskingRespond) PressRespond(false); else if (AskingPick) PressPick(-1); else PressAsk(false); }), "battle-ask-no", null);
                no.SetEnabled(open && legal.Exists(a => a.Kind == AnswerKind.Decline));
            }
            // [대기 포켓몬 기술]: 대기 포켓몬이 나오며 쓰는 기술(덤벼들기·애크러뱃 — 합법 답에 있을 때만). 누르면 기술 줄 자리가 그 기술 줄로 바뀐다
            PartyMovesOn = acting && open && !rolling && me.Exists && legal.Exists(IsPartyMove);
            if (acting && !rolling && me.Exists && !bag && !alphaOpen && !partyOpen && legal.Exists(IsPartyMove))
            {
                var party = Add(Add(turn, new VisualElement(), null, "sn-foot-row"), Kit.Button(stage.T("screen.battle.partymoves"), KitButtonKind.Normal, () => OpenPartyMoves()), "battle-partymoves", null);
                party.SetEnabled(open);
                if (open) stage.Tips.Hook(party, null, stage.T("screen.battle.partymoves.tip"), TipSide.Above, reading: true, clear: dock);
            }
            else if (acting && !rolling && partyOpen)
            {
                var foot = Add(turn, new VisualElement(), null, "sn-foot-row");
                var back = Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => ClosePartyMoves()), "battle-partymove-back", null);
                back.SetEnabled(open);
                int count = legal.FindAll(IsPartyMove).Count, pages = (count + MoveRows - 1) / MoveRows;
                if (pages > 1)
                {
                    var next = Add(foot, Kit.Button(stage.T("screen.battle.partymoves.next").Replace("{n}", (Math.Min(PartyPage, pages - 1) + 1).ToString(inv)).Replace("{max}", pages.ToString(inv)), KitButtonKind.Normal, () => NextPartyMoves()), "battle-partymove-next", null);
                    next.SetEnabled(open);
                }
            }
            // 트레이너 카드의 배틀 행동(턴을 쓰지 않는다 — rulings A35·A41): lady R [고급상처약] → 기술 줄 자리가 회복할 포켓몬 줄 · gentleman R [프레셔스 볼] → 바로 답
            int myCard = CardAsk.CardOf(flow.Client.View, flow.Client.Seat);
            bool potion = acting && !rolling && me.Exists && myCard >= 0 && legal.Exists(IsPotion);
            AbilityOn = potion && open && !bag && !alphaOpen && !partyOpen;
            CherishOn = acting && open && !rolling && me.Exists && myCard >= 0 && !bag && !alphaOpen && !partyOpen && !abilityOpen && legal.Exists(IsCherish);
            if (potion && !bag && !alphaOpen && !partyOpen && !abilityOpen)
            {
                var use = Add(Add(turn, new VisualElement(), null, "sn-foot-row"), Kit.Button(stage.T(CardAsk.AbilityKey(ctx, myCard, 2)), KitButtonKind.Normal, () => OpenAbility()), AbilityButton, null);
                use.SetEnabled(open);
                if (open) stage.Tips.Hook(use, CardAsk.Head(stage, ctx, myCard, 2), CardAsk.Text(stage, ctx, myCard, 2), TipSide.Above, reading: true, clear: dock);
            }
            else if (acting && !rolling && abilityOpen)
            {
                var foot = Add(turn, new VisualElement(), null, "sn-foot-row");
                var back = Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => CloseAbility()), AbilityBack, null);
                back.SetEnabled(open);
            }
            if (acting && !rolling && me.Exists && myCard >= 0 && !bag && !alphaOpen && !partyOpen && !abilityOpen && legal.Exists(IsCherish))
            {
                var cherish = Add(Add(turn, new VisualElement(), null, "sn-foot-row"), Kit.Button(stage.T("item.cherish_ball"), KitButtonKind.Normal, () => PressCherish()), CherishButton, null);
                cherish.SetEnabled(open);
                if (open) stage.Tips.Hook(cherish, CardAsk.Head(stage, ctx, myCard, 2), stage.T("item.cherish_ball.text"), TipSide.Above, reading: true, clear: dock);
            }
            if (CanStay)
            {
                var stay = Add(Add(turn, new VisualElement(), null, "sn-foot-row"), Kit.Button(stage.T("screen.battle.bench.stay"), KitButtonKind.Normal, () => PressStay()), "battle-bench-stay", null);
                stay.SetEnabled(open);
            }
            if (AskingToken)
            {
                var ask = Add(turn, new VisualElement(), null, "sn-foot-row");
                var use = Add(ask, Kit.Button(stage.T("screen.battle.token.use"), KitButtonKind.Main, () => PressToken(true)), "battle-token-use", null);
                var pass = Add(ask, Kit.Button(stage.T("screen.battle.token.pass"), KitButtonKind.Normal, () => PressToken(false)), "battle-token-pass", null);
                use.SetEnabled(open && legal.Exists(a => a.Kind == AnswerKind.Accept));
                pass.SetEnabled(open && legal.Exists(a => a.Kind == AnswerKind.Decline));
            }
            // 운명의 변화 손패 훑어보기(결정 80 N3 나 · 자리 결정 170 — 기둥에서 차례 칸 맨 아래로): 보기만 하는 손패 창을 이 화면 위에 연다 — 낼 수 있는 때는 이 칸이 따로 묻는다.
            // 내 손패를 여는 내 단추라 내가 싸우는 쪽일 때만(남의 배틀을 볼 때 그 좌석 칸에 내 장수가 섞이지 않게 — M7 마감 감사 1)
            FateOn = false;
            var fateState = feed.Source;
            // 차례 칸 맨 아래 단추 줄에 단추가 하나뿐이면([넘기기] · [뒤로] · [교체 안 함] …) 그 옆에 — 칸 높이가 모자라 위 줄이 눌리지 않게(1080 벌 · 두 줄 안내).
            // 포켓몬 고르기(출전 · 교체 · 다음 포켓몬 — 독이 대기 포켓몬 칸으로 바뀐 때)와 맨 아래에 단추 둘 이상인 줄이 선 동안([사용]·[넘기기] · [수락]·[거절] · [뒤로]·[다음 n/m] …)은
            // 두지 않는다(1080 벌에서 칸 높이가 모자라다 — 답하고 나면 · [뒤로] 뒤에 다시 선다) [가정]
            var lastRow = turn.childCount > 0 ? turn[turn.childCount - 1] : null;
            bool footRow = lastRow != null && lastRow.ClassListContains("sn-foot-row");
            bool fateRoom = !benching && !(footRow && lastRow.childCount > 1);
            if (fateState != null && fateState.Battle != null && SideSeat(feed) == flow.Client.Seat && fateRoom)
            {
                string single = footRow && lastRow.childCount == 1 ? lastRow[0].name : null;
                var holder = single == "battle-fate-pass" || single == "battle-ask-no" || single == "battle-partymove-back" || single == AbilityBack ? lastRow : turn;   // 짧은 단추 옆에만(긴 단추 옆이면 칸 폭을 넘는다)
                var fate = Add(holder, Kit.Button(stage.T("screen.board.fate").Replace("{n}", fateState.Seats[flow.Client.Seat].FateHand.Count.ToString(inv)).Replace("{max}", FateRules.HandLimit.ToString(inv)), KitButtonKind.Normal, () => flow.ToggleFate()), FateButton, "sn-battle-fate");   // 다시 누르면 닫힌다(결정 176)
                FateOn = flow.CanOpenFate;
                fate.SetEnabled(flow.CanPeek);   // 결정 125: 연출 중 · 결과 창에서도 열린다(그 위 정보 창 층에 — 보기만은 같다)
                if (flow.CanPeek) stage.Tips.Hook(fate, null, stage.T("screen.battle.fate.tip"), TipSide.Above, reading: true, clear: dock);
            }

            // 기술 세로 3줄: 합법 답의 기술만(시그니처 → 배운 기술 순서 = 엔진 순서). 남는 줄은 빈 칸
            var list = Add(dock, new VisualElement(), "battle-moves", "sn-battle-moves");
            // 내 포켓몬이 화면에 나온 뒤에만(인트로·조우 전환 동안에는 다음 선택이 이미 와 있어도 기술 줄을 그리지 않는다 — 드러나기 전 정보가 먼저 보이지 않게)
            if (acting && !bag && !rolling && me.Exists)
                foreach (var a in legal)
                    if (a.Kind == AnswerKind.UseMove && a.Arg < 0 && (a.Target != BattleRules.AlphaStrike || !moves.Exists(m => m.Target == BattleRules.AlphaStrike))) moves.Add(a);   // 알파 스트라이크는 한 줄(누르면 타입 줄 — OpenAlpha). 출전 포켓몬의 기술만 — 대기 포켓몬이 나오며 쓰는 기술(Arg = 그 포켓몬, rulings G20)은 차례 칸의 [대기 포켓몬 기술] 줄(FillPartyMoves)
            int signature = me.Exists ? ctx.Cards.Pokemon[me.Def].Signature : -1;
            var state = feed.Source;
            // 남의 배틀(결정 120 · T12): 아래쪽 좌석이 기술을 고르는 동안 그 포켓몬의 기술(공개 — 카드에 적힌 것)을 내 배틀과 같은 줄로 — 누를 수 없다
            bool watchMoves = !acting && !feed.Mine && !seen.Ended && me.Exists && state != null && me.Id < state.Pokemon.Count
                && flow.Client.View.Pending.Exists(p => p.Seat == SideSeat(feed) && !p.Answered && p.Kind == ChoiceKind.BattleAction);
            WatchMoves = watchMoves;
            watchSeat = watchMoves ? SideSeat(feed) : -1;
            watchRows.Clear();
            PeekMove = -1;
            if (watchMoves)
            {
                var known = new List<int>();
                PokemonRules.MoveOptions(ctx, state.Pokemon[me.Id], known);
                foreach (int mv in known) moves.Add(new ChoiceAnswer(AnswerKind.UseMove, mv));
            }
            FillRoll(list, feed);   // 굴림 단계가 시작한 항목이 재생 중이면 이 자리는 굴림판이다(04 §4-4)
            if (!rolling && benching) FillBench(list, feed, legal, open);
            else if (!rolling && acting && bag) FillBalls(list, legal, open);
            else if (!rolling && acting && alphaOpen) FillAlpha(list, foe, legal, open);
            else if (!rolling && acting && partyOpen) FillPartyMoves(list, feed, foe, legal, open);
            else if (!rolling && acting && abilityOpen) FillAbility(list, feed, legal, open);
            else if (!rolling && AskingOrder) FillOrders(list, legal, open);
            else if (!rolling && rerollStep != null) FillReroll(list);   // 재굴림 연출(43행)이 도는 동안은 다음 물음(G6 한 바퀴 더)보다 주사위 줄이 먼저
            else if (!rolling && AskingFate) FillFate(list, legal, open);
            else if (!rolling && AskingPick) FillPicks(list, choice, legal, open);
            bool custom = AskingOrder || AskingFate || AskingPick || rerollStep != null || (acting && (alphaOpen || partyOpen || abilityOpen));
            // poke_maniac B 포켓몬 분장: 상대의 시그니처로 더한 기술 줄에 꼬리표(마우스 오버 = 카드 — 능력)
            int disguise = -1;
            if (acting && me.Exists && foe.Exists && myCard >= 0 && TrainerRules.Has(flow.Client.View, ctx, flow.Client.Seat, "poke_maniac", AbilityColor.Blue))
            {
                int foeSig = ctx.Cards.Pokemon[foe.Def].Signature;
                var mine = me.Id >= 0 && me.Id < flow.Client.View.Pokemon.Count ? flow.Client.View.Pokemon[me.Id] : null;
                if (mine != null && foeSig != ctx.Cards.Pokemon[me.Def].Signature && Array.IndexOf(mine.Moves, foeSig) < 0) disguise = foeSig;
            }
            moveRows = !rolling && !benching && !bag && !custom && acting;   // 단축키 1~3이 누를 기술 줄이 떠 있다(KeyTarget)
            for (int i = 0; !rolling && !benching && !bag && !custom && i < Math.Max(MoveRows, moves.Count); i++)
            {
                if (i >= moves.Count)
                {
                    Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
                    continue;
                }
                int index = i, move = moves[i].Target;
                string nameKey = BattleFeed.MoveNameKey(ctx, move);
                var type = BattleRules.MoveType(ctx, move);
                var row = Add(list, new VisualElement(), "battle-move-" + (i + 1).ToString(inv), "sn-battle-move");
                if (i < MoveKeys.Length) row.Add(Kit.Badge(MoveKeys[i], KitBadgeKind.Plain, Kit.F1));   // 단축키 표시만(키 입력은 뒤 묶음)
                TypeIcon(row, IconArt.Type(type), stage.T("ui.types." + type));
                Add(row, Kit.Text((move == signature ? "★" : "") + stage.T(nameKey), Kit.F2), null, "sn-battle-move-name");
                Add(row, Kit.Text(stage.T("screen.battle.power"), Kit.F2), null, "kit-muted");
                // 위력 = 굴릴 주사위 수(04 §10-2 — 엔진이 굴릴 때 쓰는 함수 그대로: 기술 효과의 「공격력 = n」 + 상시 보정). 「?」 기술(효과가 정함)은 「?」
                bool unknown = !BattleRules.IsTactic(move) && ctx.Cards.Moves[move].AttackStrength == null;
                int basePower = BattleRules.MoveStrength(ctx, move), power = basePower;
                if (!unknown && state != null && me.Exists) power = BattleRules.RollPower(state, ctx, move, me.Id, foe.Id, new List<Mod>());
                row.Add(Kit.Pill(null, unknown ? "?" : power.ToString(inv), unknown || power == basePower ? KitTone.Base : power < basePower ? KitTone.Low : KitTone.High));
                if (foe.Exists && type != PokemonType.Typeless)
                {
                    var types = ctx.Cards.Pokemon[foe.Def].Types;
                    int bonus = TypeChart.Bonus(type, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless);
                    if (bonus != 0) row.Add(Kit.Badge(Signed(bonus), bonus > 0 ? KitBadgeKind.Ok : KitBadgeKind.Red, Kit.F2));
                }
                if (move == BattleRules.AlphaStrike)
                {
                    row.Add(Kit.Badge(stage.T("screen.battle.alpha.pick"), KitBadgeKind.Plain, Kit.F1));   // 누르면 타입을 고른다
                    if (state != null && me.Id >= 0 && me.Id < state.Pokemon.Count) AlphaTypeIcons(row, state.Pokemon[me.Id], "battle-move-alpha-types");   // 결정 182: 고를 수 있는 타입(배울 수 있는 타입)
                }
                if (move == disguise)
                {
                    var tag = Add(row, Kit.Badge(stage.T("screen.card.disguise"), KitBadgeKind.Gold, Kit.F1), "battle-move-disguise", null);
                    if (open) stage.Tips.Hook(tag, CardAsk.Head(stage, ctx, myCard, 1), CardAsk.Text(stage, ctx, myCard, 1), TipSide.Above, reading: true, clear: dock);
                }
                if (watchMoves)   // 보기 전용: 누르지 못하고 효과 글만 마우스 오버
                {
                    row.AddToClassList("sn-battle-move-watch");
                    watchRows.Add(new KeyValuePair<VisualElement, int>(row, move));
                    stage.Tips.Hook(row, stage.T(nameKey), stage.TOrNull(nameKey + ".text"), TipSide.Above, reading: true, clear: dock);
                    continue;
                }
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => { flow.Room.Peek(PeekSpot.Move, move, true); PressMove(index); });
                // 방 판: 내가 가리키는 기술(남의 같은 기술 줄에 — 결정 120 · Protocol 10)
                row.RegisterCallback<PointerEnterEvent>(e => { if (open) flow.Room.Peek(PeekSpot.Move, move); });
                row.RegisterCallback<PointerLeaveEvent>(e => { if (open) flow.Room.Peek(PeekSpot.Move, -1); });
                if (open) stage.Tips.Hook(row, stage.T(nameKey), stage.TOrNull(nameKey + ".text"), TipSide.Above, reading: true, clear: dock);
            }

            // 가방 · 포켓몬 · 기권
            var side = Add(dock, new VisualElement(), "battle-side", "sn-battle-side");
            // [정비](T): 배틀 중에도 엔진이 정비를 받는 때(MaintOn)에 정비 판을 이 화면 위에 연다 — 연출 중·내 공격 처리 중에는 흐린 단추
            if (MaintOn)
            {
                var maint = Add(side, Kit.Button(stage.T("screen.battle.maint"), KitButtonKind.Normal, () => flow.ToggleParty()), "battle-maint", null);   // 다시 누르면 닫힌다(결정 176 — T와 같은 함수)
                Add(maint, Kit.Badge("T", KitBadgeKind.Plain, Kit.F1), null, "sn-battle-key");
                stage.Tips.Hook(maint, null, stage.T("screen.battle.maint.tip"), TipSide.Above, reading: true, clear: dock);
            }
            else Off(side, "battle-maint", "screen.battle.maint", "T", "screen.battle.maint.off", open).clicked += () => flow.OpenParty();   // 결정 125: 흐린 단추를 눌러도 판은 열린다(보기만)
            // [가방]: 합법 답에 볼 던지기가 있을 때만 켜진다(야생 배틀 · 가진 볼). 누르면 기술 줄 자리가 볼 줄로 바뀐다. 자발 교체는 규칙에 없어 [포켓몬] 단추는 없다
            bool canThrow = acting && legal.Exists(a => a.Kind == AnswerKind.ThrowBall);
            BagOn = canThrow && open;
            bool canForfeit = acting && legal.Exists(a => a.Kind == AnswerKind.Forfeit);
            ForfeitOn = canForfeit && open;
            // 남의 배틀을 볼 때(관전)는 [가방] · [기권]을 두지 않는다 — 흐린 단추의 이유 툴팁이 내 배틀 이야기라 헷갈린다 [가정 #400]. [정비]는 관전 중에도 그대로
            if (feed.Mine)
            {
                if (canThrow)
                {
                    var bagButton = Add(side, Kit.Button(stage.T("screen.battle.bag"), KitButtonKind.Normal, () => ToggleBag()), "battle-bag", null);
                    Add(bagButton, Kit.Badge("Q", KitBadgeKind.Plain, Kit.F1), null, "sn-battle-key");
                    bagButton.SetEnabled(open);
                    if (open) stage.Tips.Hook(bagButton, null, stage.T("screen.battle.bag.tip"), TipSide.Above, reading: true, clear: dock);
                }
                else if (UncatchKey(flow.Battle, foe) is string why) Off(side, "battle-bag", "screen.battle.bag.none", "Q", why, open);   // 87·92행: 울트라 비스트 · 네크로즈마 · 시련 전의 수호신 = 회색 「포획 불가」
                else Off(side, "battle-bag", "screen.battle.bag", "Q", "screen.battle.bag.off", open);
                if (canForfeit)
                {
                    var forfeit = Add(side, Kit.Button(stage.T("screen.battle.forfeit"), KitButtonKind.End, () => PressForfeit()), "battle-forfeit", null);
                    Add(forfeit, Kit.Badge("E", KitBadgeKind.Plain, Kit.F1), null, "sn-battle-key");
                    forfeit.SetEnabled(open);
                    if (open) stage.Tips.Hook(forfeit, null, stage.T("screen.battle.forfeit.tip"), TipSide.Above, reading: true, clear: dock);
                }
                else Off(side, "battle-forfeit", "screen.battle.forfeit", "E", "screen.battle.forfeit.off", open);
            }

            // 계산 줄: 피해 계산의 칸(주사위 + 효과 + 상성 = 데미지)과 그 피해로 바뀐 체력. 굴림판이 떠도 그 공격이 맞는 순간(59행 — 결정 171 ①)까지는
            // 앞 공격의 계산을 남겨 둔다(CalcHeld — 결정 70 ⑧). 맞는 순간부터 이 공격의 칸이 왼쪽부터 켜지고, 체력 줄은 체력이 착지한 뒤에 뜬다
            var calc = Add(dock, new VisualElement(), "battle-calc", "sn-battle-calc");
            CalcLit = 0;
            CalcHpShown = false;
            CaptureSumLit = CaptureVerdictLit = false;
            CalcHeld = CalcTaken = false;
            if (benching && !rolling)
            {
                // 대기 포켓몬 칸이 독 오른쪽 끝까지 쓴다
                side.style.display = DisplayStyle.None;
                calc.style.display = DisplayStyle.None;
            }
            var last = seen.Damage;
            // 포획 굴림 중: 포획률은 처음부터, 합은 다 센 뒤, 성공·실패는 판정 때 켜진다
            var caught = rolling ? (rollIsCapture ? cap : null) : seen.Capture;
            bool sumLit = !rollIsCapture || Counted >= rollFinal.Count, verdictLit = !rollIsCapture || ReferenceEquals(seen.Capture, cap);
            int calcMs = int.MaxValue;
            var step = CalcStep;   // 지금 항목에 이 공격의 계산이 있다(굴림판이 떠 있거나 · 재굴림 창 뒤의 항목처럼 굴림 없이)
            if (step != null && caught == null)
            {
                int ms = CalcMs;
                if (ms >= 0)
                {
                    last = (DamageBreakdown)step.Events[0];
                    calcMs = ms;
                }
                else
                {
                    // 이 공격의 계산은 아직: 앞 공격의 것(드러난 마지막 피해 — 이 항목의 것은 아직 가려져 있다)을 다 켜진 모습으로 남겨 둔다
                    if (ReferenceEquals(last, step.Events[0])) last = null;
                    CalcHeld = last != null && last.Steps != null;
                }
            }
            var calcHead = Add(calc, new VisualElement(), "battle-calc-head", "sn-battle-calc-head");
            Add(calcHead, Kit.Text(stage.T(caught != null ? "screen.battle.calc.capture" : "screen.battle.calc"), Kit.F2), null, "kit-title");
            DieNet(calcHead);
            if (caught != null)
            {
                // 포획 시도: 주사위 합 / 포획률 → 성공·실패(이벤트 값 그대로)
                Add(calc, Kit.Text(stage.T("screen.battle.calc.target").Replace("{mon}", LineT(feed.MonKey(caught.TargetId))), Kit.F2), "battle-calc-target", "kit-muted");
                var chips = Add(calc, new VisualElement(), "battle-calc-chips", "sn-battle-chips");
                var sum = Chip(chips, "screen.battle.calc.capture.sum", caught.Sum.ToString(inv), false);
                sum.name = "battle-calc-capture-sum";
                chips.Add(Kit.Text("/", Kit.F2));
                Chip(chips, "screen.battle.calc.capture.rate", caught.CatchRate.ToString(inv) + "+", false);
                var equals = Add(chips, Kit.Text("=", Kit.F2), null, null);
                var verdict = Chip(chips, "screen.battle.calc.capture", stage.T(caught.Success ? "screen.battle.calc.capture.ok" : "screen.battle.calc.capture.fail"), !caught.Success);
                verdict.name = "battle-calc-capture";
                if (caught.Success) verdict.AddToClassList("sn-battle-chip-ok");
                if (!sumLit) sum.style.visibility = Visibility.Hidden;
                if (!verdictLit) equals.style.visibility = verdict.style.visibility = Visibility.Hidden;
                CaptureSumLit = sumLit;
                CaptureVerdictLit = verdictLit;
            }
            else if (last == null || last.Steps == null)
            {
                if (!rolling) Add(calc, Kit.Text(stage.T("screen.battle.calc.none"), Kit.F2), "battle-calc-none", "kit-muted");
            }
            else FillCalc(calc, feed, last, calcMs);
            if (CalcHeld)
            {
                CalcLit = 0;
                CalcHpShown = false;
            }
        }

        // 피해 계산 한 번의 칸: 주사위 + 효과 + 상성(늘) · 2배 · 감소 · 무효 · 상한 · 응징(걸렸을 때만) = 데미지, 아래에 「○○ ♥앞 → ♥뒤」.
        // ms = 계산 단계 안 시각(다 켜진 모습이면 int.MaxValue). 아직 켜지지 않은 칸은 자리만 차지한다(값이 먼저 새지 않고, 켜질 때 줄이 밀리지 않는다)
        void FillCalc(VisualElement calc, BattleFeed feed, DamageBreakdown last, int ms)
        {
            var inv = CultureInfo.InvariantCulture;
            // 누가 맞았나(결정 171 ②): 맞은 포켓몬의 작은 도트 얼굴 + 이름표(상대 = 붉은 바탕 · 내 쪽 = 그 좌석 색) + 「준 피해」·「받은 피해」.
            // 내가 준 피해는 왼쪽에서, 받은 피해는 오른쪽에서(얼굴이 바깥) — 색에만 기대지 않는다
            bool taken = OnMySide(feed, last.TargetId);
            CalcTaken = taken;
            var body = Add(calc, new VisualElement(), "battle-calc-body", "sn-battle-calc-body");
            if (taken) body.AddToClassList("sn-battle-calc-taken");
            var target = feed.MonOf(last.TargetId);
            var mons = MonArt.Load();
            var face = Add(body, new VisualElement(), "battle-calc-face", "sn-battle-calc-face");
            if (mons != null && target.Exists) face.Add(mons.Front(flow.Ctx.Cards.Pokemon[target.Def].Key));
            var col = Add(body, new VisualElement(), null, "sn-battle-calc-col");
            var who = Add(col, new VisualElement(), null, "sn-battle-calc-who");
            var tag = Add(who, new VisualElement(), "battle-calc-target", "sn-battle-calc-tag");
            if (taken) tag.style.backgroundColor = new StyleColor(flow.SeatColor(SideSeat(feed)));
            else tag.AddToClassList("sn-battle-calc-tag-foe");
            tag.Add(Kit.Text(LineT(feed.MonKey(last.TargetId)), Kit.F2));
            Add(who, Kit.Text(stage.T(taken ? "screen.battle.calc.taken" : "screen.battle.calc.dealt"), Kit.F2), "battle-calc-dir", "kit-muted");
            calc = col;
            var chips = Add(calc, new VisualElement(), "battle-calc-chips", "sn-battle-chips");
            int dice = 0, effect = 0, matchup = 0;
            var cells = new List<KeyValuePair<string, string>>();   // (칩 이름 글, 값)
            // 상성(③)의 출처별 몫(결정 194): 표 상성 = 「상성」, 균열 카드(무모한 돌격) · 트레이너 카드(약점 분석)는 그 이름으로 나눠 보인다 — 몫이 없으면 지금처럼 「상성」 하나
            var extra = new List<KeyValuePair<string, string>>();
            foreach (var s in last.Steps)
                switch (s.Kind)
                {
                    case DamageStepKind.Dice: dice += s.Value; break;
                    case DamageStepKind.Effect: effect += s.Value; break;
                    case DamageStepKind.Effectiveness:
                        string named = MatchupName(s.Source);
                        if (named == null) matchup += s.Value;
                        else extra.Add(new KeyValuePair<string, string>(named, Signed(s.Value)));
                        break;
                    case DamageStepKind.Double: cells.Add(new KeyValuePair<string, string>(stage.T("screen.battle.calc.double"), "×2")); break;
                    case DamageStepKind.Reduction: cells.Add(new KeyValuePair<string, string>(stage.T("screen.battle.calc.reduction"), Signed(s.Value))); break;
                    case DamageStepKind.Threshold: cells.Add(new KeyValuePair<string, string>(stage.T("screen.battle.calc.threshold"), s.Value.ToString(inv))); break;
                    case DamageStepKind.Cap: cells.Add(new KeyValuePair<string, string>(stage.T("screen.battle.calc.cap"), s.Value.ToString(inv))); break;
                    case DamageStepKind.Retribution: cells.Add(new KeyValuePair<string, string>(stage.T("screen.battle.calc.retribution"), Signed(s.Value))); break;
                }
            cells.InsertRange(0, extra);
            cells.Insert(0, new KeyValuePair<string, string>(stage.T("screen.battle.calc.effectiveness"), Signed(matchup)));
            cells.Insert(0, new KeyValuePair<string, string>(stage.T("screen.battle.calc.effect"), Signed(effect)));
            cells.Insert(0, new KeyValuePair<string, string>(stage.T("screen.battle.calc.dice"), dice.ToString(inv)));
            int added = 3 + extra.Count;   // 「+」로 잇는 칸: 주사위 · 효과 · 상성(과 그 몫)
            CalcMatchups = 1 + extra.Count;
            bool all = ms == int.MaxValue, total = all || BattleRoll.TotalLit(ms);
            int lit = all ? cells.Count : BattleRoll.CalcLit(cells.Count, ms);
            for (int i = 0; i < cells.Count; i++)
            {
                VisualElement op = i > 0 && i < added ? Add(chips, Kit.Text("+", Kit.F2), null, null) : null;
                var chip = ChipText(chips, cells[i].Key, cells[i].Value, false);
                if (i >= 2 && i < added) chip.name = "battle-calc-matchup-" + (i - 2).ToString(inv);   // 0 = 표 상성 · 1~ = 출처가 있는 몫
                if (i < lit) continue;
                chip.style.visibility = Visibility.Hidden;
                if (op != null) op.style.visibility = Visibility.Hidden;
            }
            var equals = Add(chips, Kit.Text("=", Kit.F2), null, null);
            var sum = Chip(chips, "screen.battle.calc.total", last.Final.ToString(inv), true);
            sum.name = "battle-calc-total";
            if (!total) equals.style.visibility = sum.style.visibility = Visibility.Hidden;
            CalcLit = lit + (total ? 1 : 0);

            // 그 피해로 바뀐 체력(착지한 뒤에)
            var hp = seen.DamageHp;
            CalcHpShown = total && ReferenceEquals(last, seen.Damage) && hp != null;
            var line = Add(calc, new VisualElement(), "battle-calc-hp", "sn-battle-calc-hp");   // 이름은 위 이름표가 말한다
            line.Add(Kit.Pill(heart, (CalcHpShown ? hp.Before : 0).ToString(inv)));
            line.Add(Kit.Text("→", Kit.F2));
            line.Add(Kit.Pill(heart, (CalcHpShown ? hp.After : 0).ToString(inv), CalcHpShown && hp.After * 2 <= hp.Max ? KitTone.Low : KitTone.Base));
            if (!CalcHpShown) line.style.visibility = Visibility.Hidden;
        }

        // ── 굴림판(04 §4-4, §8-8 56·57행) ──

        // 독의 기술 줄 자리에 굴림판: 주사위 그림(DiceBoard — 녹화본 재생) + 머리(타입 · 기술 이름 · 「주사위 n개」 · 결과 줄) + 오른쪽 합계.
        // 결과 줄에는 센 주사위까지의 면이 왼쪽부터 서고(면 바꾸기로 바뀐 것은 회색) 합계는 그만큼의 합이다. 고리·꼬리표는 멈춘 주사위 자리에 놓아 두고 Animate가 켜고 끈다
        void FillRoll(VisualElement list, BattleFeed feed)
        {
            rollPanel = rollBoard = null;
            rollRings.Clear();
            rollFaces.Clear();
            rollTags.Clear();
            shownFaces.Clear();
            RollSum = -1;
            if (rollEntry == null) return;
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var rolled = rollIsCapture ? null : (DiceRolled)BattleRoll.StepOf(rollEntry).Events[0];
            int move = int.MinValue;
            var type = rolled != null ? rolled.Type : PokemonType.Typeless;
            if (rolled != null)
                foreach (var e in rollEntry.Events)
                {
                    if (ReferenceEquals(e, rolled)) break;
                    if (e is MoveSelected sel && (move == int.MinValue || sel.Side == rolled.Side)) { move = sel.MoveKey; type = sel.Type; }   // 흉내내기·손가락흔들기가 부른 기술이 있으면 그 기술(같은 공격의 둘째 MoveSelected)
                }

            list.AddToClassList("sn-battle-roll");
            rollPanel = Add(list, new VisualElement(), "battle-roll", "sn-battle-rollpanel");
            rollBoard = Add(rollPanel, new VisualElement(), "battle-roll-board", "sn-battle-rollboard");
            // 결정 188: 굴린 뒤 창(디아루가)을 앞둔 포획 굴림은 판정 전에 DiceRolled(Kind = Capture)로 먼저 구른다 — 몸 색 · 머리는 포획 굴림과 같게
            bool ballRoll = rolled != null && rolled.Kind == DiceKind.Capture;
            var body = rollIsCapture || ballRoll ? CaptureBody : DiceBoard.TypeBody(type);
            if (!rollIsCapture) rollBody = body;
            var art = Board(body);
            if (art != null && rollScene != null) rollBoard.style.backgroundImage = Background.FromRenderTexture(art.Output);

            var head = Add(rollPanel, new VisualElement(), "battle-roll-head", "sn-battle-rollhead");
            if (rollIsCapture || ballRoll)
            {
                // 포획 굴림의 머리: 던진 볼 · 「포획 주사위 n개」 · 남은 볼(던지는 순간 줄어든다 — 표시 값, 판정 앞 굴림에는 없다)
                int ballKey = rollIsCapture ? cap.BallKey : rolled.BallKey;
                int dice = rollIsCapture ? rollFinal.Count : rolled.Faces != null ? rolled.Faces.Length : 0;
                Icon(head, GenAssets.ItemIcons + BattleFeed.BallItem(ballKey) + ".png");
                head.Add(Kit.Text(stage.T(BattleFeed.BallKey(ballKey)), Kit.F2));
                Add(head, Kit.Text(stage.T("screen.battle.roll.capture").Replace("{n}", dice.ToString(inv)), Kit.F1), "battle-roll-count", "kit-muted");
                if (rollIsCapture) Add(head, Kit.Text("×" + BallStock(ballKey).ToString(inv), Kit.F1), "battle-roll-stock", null);
            }
            else
            {
                if (Enum.IsDefined(typeof(PokemonType), type)) TypeIcon(head, IconArt.Type(type), stage.T("ui.types." + type));
                if (move != int.MinValue)
                {
                    var attacker = feed.MonOf(rolled.PokemonId);
                    bool star = attacker.Exists && ctx.Cards.Pokemon[attacker.Def].Signature == move;
                    head.Add(Kit.Text((star ? "★" : "") + stage.T(BattleFeed.MoveNameKey(ctx, move)), Kit.F2));
                }
                Add(head, Kit.Text(stage.T("screen.battle.roll.dice").Replace("{n}", (rolled.Faces != null ? rolled.Faces.Length : 0).ToString(inv)), Kit.F1), "battle-roll-count", "kit-muted");
            }

            // 결과 줄: 센 주사위까지(왼쪽부터 = 엔진 순서)
            int counted = Counted;
            var faces = Add(head, new VisualElement(), "battle-roll-faces", "sn-battle-rollfaces");
            for (int k = 0; k < counted; k++)
            {
                var face = rollFinal[k];
                shownFaces.Add(face);
                bool grey = rollIsCapture ? k == CaptureStage.Converted(cap) : BattleRoll.Changed(rollEntry, k);
                VisualElement icon;
                if (art != null && face != AttackDieFace.None)
                    icon = new Image { image = art.Faces, sourceRect = DiceBoard.FaceRect(face, grey), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                else
                    icon = Kit.Badge(face == AttackDieFace.None ? "–" : stage.T(BattleFeed.K + "face." + (face == AttackDieFace.StatusSymbol ? "status" : face == AttackDieFace.CriticalHit ? "crit" : "hit")), KitBadgeKind.Plain, Kit.F1);
                Add(faces, icon, "battle-roll-face-" + (k + 1).ToString(inv), art != null && face != AttackDieFace.None ? "sn-battle-rollface" : null);
                rollFaces.Add(icon);
            }
            RollSum = BattleRoll.Sum(rollFinal, counted);
            var side = Add(rollPanel, new VisualElement(), "battle-roll-side", "sn-battle-rollside");
            Add(side, Kit.Text(stage.T("screen.battle.roll.sum"), Kit.F1), null, "kit-muted");
            Add(side, Kit.Text(RollSum.ToString(inv), Kit.F3), "battle-roll-sum", null);

            // 고리(세는 주사위를 짚는다)와 꼬리표(면 바꾸기의 원인 — 상태 그림): 멈춘 자리에 놓는다. 자리는 굴림마다 다르다(데이터) — 켜고 끄기는 Animate
            if (art == null || rollScene == null) return;
            for (int k = 0; k < rollScene.Dice; k++)
            {
                var at = art.ToPanel(rollScene.RestPos(rollScene.Order[k]));
                var ring = Add(rollBoard, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-roll-ring-" + (k + 1).ToString(inv), "sn-battle-rollring");
                ring.style.left = at.x;
                ring.style.top = at.y;
                rollRings.Add(ring);
            }
            foreach (var e in rollEntry.Events)
            {
                if (!(e is DiceFacesChanged changed) || changed.Changes == null) continue;
                string key = changed.Source.Kind == SourceKind.Status && Enum.IsDefined(typeof(StatusEffect), changed.Source.Id) ? DsStage.StatusKey((StatusEffect)changed.Source.Id) : null;
                var tex = key != null ? GenAssets.Load<Texture2D>(key) : null;
                if (tex == null) continue;
                foreach (var c in changed.Changes)
                {
                    if (c.Index < 0 || c.Index >= rollScene.Dice) continue;
                    int flipAt = int.MaxValue;
                    foreach (var flip in rollScene.Flips) if (flip.Index == c.Index) { flipAt = flip.AtMs; break; }
                    if (flipAt == int.MaxValue) continue;
                    var at = art.ToPanel(rollScene.RestPos(rollScene.Order[c.Index]));
                    var tag = Add(rollBoard, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-roll-tag-" + (c.Index + 1).ToString(inv), "sn-type-icon");
                    tag.AddToClassList("sn-battle-rolltag");
                    tag.style.backgroundImage = new StyleBackground(tex);
                    tag.style.left = at.x;
                    tag.style.top = at.y;
                    rollTags.Add(new KeyValuePair<int, VisualElement>(flipAt - 150, tag));   // 꼬리표는 넘기기 150 전에(spikes/dice-roll §4: 멈춤 + 300 꼬리표 → + 150 넘김)
                }
            }
        }

        // 굴림판 그림(벌이 바뀌면 새로 만든다). 주사위 몸 색 = 그 기술의 타입 색. 셰이더가 없으면 null — 굴림판은 머리와 결과 줄만 선다
        DiceBoard Board(Color32 body)
        {
            if (boardFailed) return null;
            if (board != null && board.TokenHeight != stage.Grade.TokenHeight)
            {
                board.Dispose();
                board = null;
                boardFor = renderedFor = null;
            }
            try
            {
                if (board == null) board = new DiceBoard(stage.Grade.TokenHeight, body);
                else if (!ReferenceEquals(boardFor, rollEntry) || boardCapture != rollIsCapture) board.SetBody(body);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning("[BattleScreen] 굴림판을 만들지 못했다: " + e.Message);
                boardFailed = true;
                board = null;
                return null;
            }
            if (!ReferenceEquals(boardFor, rollEntry) || boardCapture != rollIsCapture) renderedFor = null;
            boardFor = rollEntry;
            boardCapture = rollIsCapture;
            return board;
        }

        static string Signed(int n) => n > 0 ? "+" + n.ToString(CultureInfo.InvariantCulture) : n < 0 ? "−" + (-n).ToString(CultureInfo.InvariantCulture) : "0";

        VisualElement Chip(VisualElement parent, string labelKey, string value, bool total) => ChipText(parent, stage.T(labelKey), value, total);

        VisualElement ChipText(VisualElement parent, string label, string value, bool total)
        {
            var chip = Add(parent, new VisualElement(), null, "sn-battle-chip");
            if (total) chip.AddToClassList("sn-battle-chip-total");
            Add(chip, Kit.Text(label, Kit.F1), null, total ? null : "kit-muted");
            chip.Add(Kit.Text(value, Kit.F2));
            return chip;
        }

        /// <summary>계산 줄의 상성 칸 수(표 상성 1 + 출처가 있는 몫 — 결정 194, 확인용).</summary>
        public int CalcMatchups { get; private set; }

        // 상성 몫의 출처 이름(결정 194): 균열 카드 = 카드 이름(무모한 돌격) · 트레이너 카드 = 그 카드의 빨강 능력 이름(scientist R 약점 분석). 표 상성(출처 없음)이면 null
        string MatchupName(SourceRef src)
        {
            var ctx = flow.Ctx;
            if (src.Kind == SourceKind.BreachCard && src.Id >= 0 && src.Id < ctx.Decks.CardIds.Count) return stage.T(ctx.Decks.CardIds[src.Id]);
            if (src.Kind == SourceKind.TrainerCard && src.Id >= 0) return stage.T(CardAsk.AbilityKey(ctx, src.Id, 2));
            return null;
        }

        // 지금은 누를 수 없는 단추: 흐린 모양 + 이유 툴팁(꺼진 요소는 마우스를 받지 못해 켜 둔 채 흐리게만 한다 — BoardHud와 같다)
        Button Off(VisualElement parent, string name, string textKey, string key, string tipKey, bool open)
        {
            var button = Add(parent, Kit.Button(stage.T(textKey), KitButtonKind.Normal, null), name, null);
            button.AddToClassList("sn-hud-off");
            Add(button, Kit.Badge(key, KitBadgeKind.Plain, Kit.F1), null, "sn-battle-key");
            if (open) stage.Tips.Hook(button, stage.T(textKey), stage.T(tipKey), TipSide.Above, reading: true, clear: dock);
            return button;
        }

        // 독의 기술 줄 자리에 대기 포켓몬 칸: 합법 답(Pick)의 포켓몬만, 왼쪽 → 오른쪽 = 엔진 순서
        void FillBench(VisualElement list, BattleFeed feed, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            list.AddToClassList("sn-battle-bench");
            var mons = MonArt.Load();
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick) continue;
                int index = bench.Count;
                bench.Add(a);
                var mon = feed.MonOf(a.Target);
                var tile = Add(list, new VisualElement(), "battle-bench-" + (index + 1).ToString(inv), "sn-battle-benchmon");
                if (mon.Exists)
                {
                    tile.Add(mons != null ? mons.Front(flow.Ctx.Cards.Pokemon[mon.Def].Key) : new VisualElement());
                    var col = Add(tile, new VisualElement(), null, "sn-battle-mon-info");
                    col.Add(Kit.Text(MonName(mon), Kit.F2));
                    col.Add(Kit.Pill(heart, mon.Hp.ToString(inv), mon.Hp * 2 <= mon.MaxHp ? KitTone.Low : KitTone.Base, mon.MaxHp.ToString(inv)));
                    col.Add(Kit.Pill(speed, mon.Speed.ToString(inv), mon.Speed < mon.BaseSpeed ? KitTone.Low : mon.Speed > mon.BaseSpeed ? KitTone.High : KitTone.Base));
                    if (mon.Status != StatusEffect.None) TypeIcon(col, DsStage.StatusKey(mon.Status), null);
                }
                tile.SetEnabled(open);
                stage.OnTap(tile, SoundKeys.Decide, e => PressBench(index));
                HookMon(tile, a.Target);   // 마우스 오버 = 그 포켓몬 카드(결정 169)
            }
            list.EnableInClassList("sn-battle-bench-many", bench.Count >= 5);   // 다섯 이상: 도트 창을 줄인다(여섯 칸이 독 폭에 들어오게 — M5 마감 검토)
        }

        // 독의 기술 줄 자리에 효과 순서 줄(결정 72 [가정 꼴]): 합법 답마다 한 줄(첫 줄 = 기본 순서) — 순서(상태 그림 · 이름) → 그 순서로 바꾼 마지막 굴림 · 데미지 · 기절.
        // 결과는 엔진의 미리 셈(EffectRules.Preview — 실제 피해와 같은 식)을 보기 사본에서 그대로 읽는다(화면이 규칙을 세지 않는다)
        void FillOrders(VisualElement list, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            list.AddToClassList("sn-battle-orders");
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick || a.Values.Count == 0) continue;
                int index = orders.Count;
                orders.Add(a);
                var r = EffectRules.Preview(view, ctx, a.Values);
                var row = Add(list, new VisualElement(), "battle-order-" + (index + 1).ToString(inv), "sn-battle-move");
                row.AddToClassList("sn-battle-order");
                if (index < MoveKeys.Length) row.Add(Kit.Badge(MoveKeys[index], KitBadgeKind.Plain, Kit.F1));
                for (int i = 0; i < a.Values.Count; i++)
                {
                    if (i > 0) Add(row, Kit.Text("→", Kit.F2), null, "kit-muted");
                    var src = EffectRules.OrderSource(view, ctx, a.Values[i]);   // 지닌물건·지속 보정(M5)은 쌓인 프레임의 출처(그 아이템 · 건 기술)
                    if (src.Kind == SourceKind.Status)
                    {
                        var st = (StatusEffect)src.Id;
                        TypeIcon(row, DsStage.StatusKey(st), stage.T(MonParts.StatusKey(st)));   // Pt 상태 그림에 이름이 적혀 있다(그림이 없으면 글자)
                    }
                    else Add(row, Kit.Text(stage.T(src.Kind == SourceKind.Item ? ctx.Items.Items[src.Id].Key : BattleFeed.MoveNameKey(ctx, src.Id)), Kit.F2), null, "sn-battle-order-name");
                }
                var faces = Add(row, new VisualElement(), "battle-order-faces-" + (index + 1).ToString(inv), "sn-battle-rollfaces");
                faces.AddToClassList("sn-battle-order-faces");
                foreach (var f in r.Faces)
                    if (f != AttackDieFace.None) faces.Add(FaceIcon(f));
                row.Add(Kit.Pill(null, stage.T("screen.battle.order.damage").Replace("{n}", r.Damage.ToString(inv)), KitTone.Base));
                if (r.Faints) row.Add(Kit.Badge(stage.T("screen.battle.order.faint"), KitBadgeKind.Red, Kit.F2));
                if (r.AttackerFaints) row.Add(Kit.Badge(stage.T("screen.battle.order.self_faint"), KitBadgeKind.Red, Kit.F2));
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => PressOrder(index));
                if (open) stage.Tips.Hook(row, null, stage.T("screen.battle.order.tip"), TipSide.Above, reading: true, clear: dock);
            }
            for (int i = orders.Count; i < MoveRows; i++) Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
        }

        /// <summary>공격 주사위의 여섯 면(결정 101 ⑤ — 일반 타격 4 · 급소 1 · 상태 이상 1): 계산 줄 머리의 작은 전개도 <c>battle-dienet</c>가 늘 펼쳐 보인다.</summary>
        public static readonly AttackDieFace[] DieFaces = { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol };

        // 주사위 6면 구성 안내(결정 101 ⑤): 계산 줄 머리 오른쪽에 여섯 면을 한 줄로 늘 펼쳐 둔다 — 마우스 오버 = 한 줄 설명. 굴리지 않는다(판정과 무관한 안내)
        void DieNet(VisualElement head)
        {
            var net = Add(head, new VisualElement(), "battle-dienet", "sn-battle-dienet");
            Add(net, Kit.Text(stage.T("screen.battle.dienet"), Kit.F1), null, "kit-muted");
            // 면 그림은 굴림판의 것 — 아직 굴린 적이 없으면 여기서 만든다(몸 색은 다음 굴림이 그 기술의 타입 색으로 바꾼다)
            if (board == null && !boardFailed)
            {
                try { board = new DiceBoard(stage.Grade.TokenHeight, CaptureBody); }
                catch (InvalidOperationException) { boardFailed = true; }
            }
            for (int i = 0; i < DieFaces.Length; i++)
            {
                // 회색 면(몸 색과 무관한 칸 — 굴림마다 몸 색이 바뀌어도 이 그림은 그대로다), 굴림판 결과 줄의 면(sn-battle-rollface)과 섞이지 않게 따로 부른다
                VisualElement face = board != null
                    ? new Image { image = board.Faces, sourceRect = DiceBoard.FaceRect(DieFaces[i], true), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }
                    : FaceIcon(DieFaces[i]);
                if (board != null) face.AddToClassList("sn-battle-netface");
                face.name = "battle-dienet-face-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                net.Add(face);
            }
            stage.Tips.Hook(net, stage.T("screen.battle.dienet.tip.title"), stage.T("screen.battle.dienet.tip"), TipSide.Above, reading: true, clear: dock);
        }

        // 주사위 면 하나(굴림판 결과 줄과 같은 그림 — 굴림판이 없으면 글자)
        VisualElement FaceIcon(AttackDieFace face)
        {
            if (board != null)
            {
                var img = new Image { image = board.Faces, sourceRect = DiceBoard.FaceRect(face, false), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                img.AddToClassList("sn-battle-rollface");
                return img;
            }
            return Kit.Badge(stage.T(BattleFeed.K + "face." + (face == AttackDieFace.StatusSymbol ? "status" : face == AttackDieFace.CriticalHit ? "crit" : "hit")), KitBadgeKind.Plain, Kit.F1);
        }

        // 독의 기술 줄 자리에 가진 볼 줄(합법 ThrowBall만 — 등급 순서) + [뒤로]
        void FillBalls(VisualElement list, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            list.AddToClassList("sn-battle-balls");
            var seat = flow.Client.View.Seats[flow.Client.Seat];
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.ThrowBall) continue;
                int index = balls.Count;
                balls.Add(a);
                string key = BattleFeed.BallKey(a.Target);
                var row = Add(list, new VisualElement(), "battle-ball-" + (index + 1).ToString(inv), "sn-battle-move");
                row.AddToClassList("sn-battle-ball");
                Icon(row, GenAssets.ItemIcons + BattleFeed.BallItem(a.Target) + ".png");
                Add(row, Kit.Text(stage.T(key), Kit.F2), null, "sn-battle-move-name");
                row.Add(Kit.Text("×" + seat.Balls[a.Target].ToString(inv), Kit.F2));
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => PressBall(index));
                if (open) stage.Tips.Hook(row, stage.T(key), stage.T("screen.battle.ball.tip").Replace("{n}", CaptureRules.Dice((Ball)a.Target).ToString(inv)), TipSide.Above, reading: true, clear: dock);
            }
            var back = Add(list, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => CloseBag()), "battle-bag-back", "sn-battle-ball");
            back.SetEnabled(open);
        }

        // 이 화면이 받는 배틀 뒤 선택(그 밖의 갈래 — 트레이너전 보상·진화 분기·공유 축복 — 는 M5)
        static bool RewardReady(ChoiceSub sub) => sub == ChoiceSub.PartyFull || sub == ChoiceSub.TeachMove || sub == ChoiceSub.CoralColor || sub == ChoiceSub.TrainerReward;

        void FillResult(BattleFeed feed, PendingChoice choice, List<ChoiceAnswer> legal)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            result.Clear();
            rewards.Clear();
            bool asking = choice != null && choice.Kind == ChoiceKind.Reward && RewardReady(choice.Sub);
            // 결과 창은 배틀 끝이 화면에 드러난 때 뜬다(70행 — 받은 것이 하나씩 내려앉는다). 물음과 [확인]은 연출이 다 돈 뒤에:
            // 끝난 배틀은 그 끝까지 화면에 보인 뒤(GameFlow.BattleOver), 살아 있는 배틀의 물음(야생이 도망칠 때 받을 색)은 대기열이 빈 뒤
            bool over = flow.BattleOver;
            asking = asking && (over || (!feed.Ended && Ready));
            bool shown = over || asking || (feed.Ended && seen.Ended);
            ResultShown = shown;
            resultDrops.Clear();
            resultFoot = null;
            result.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) return;
            // 줄이 내려앉는 때(항목 시각): 결과 단계(70행)에서 줄마다, 막 목표와 그 뒤의 줄은 막 목표 단계(94행)에서. 그 항목이 다 재생됐으면 모두 내려앉은 모습
            var playing = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var endStep = playing != null ? playing.Step(700) : null;
            var goalStep = playing != null ? playing.Step(940) : null;
            int dropped = 0, afterGoal = -1;
            void Drop(VisualElement line, bool goal = false)
            {
                if (goal && goalStep != null) afterGoal = 0;
                int at = endStep == null ? 0 : afterGoal >= 0 ? goalStep.StartMs + ResultStage.LineAtMs(afterGoal++) : endStep.StartMs + ResultStage.LineAtMs(dropped++);
                line.pickingMode = PickingMode.Ignore;
                resultDrops.Add(new KeyValuePair<int, VisualElement>(at, line));
            }
            Add(result, new VisualElement(), null, "kit-scrim");
            var panel = Add(result, Kit.Panel(stage.Skin), "battle-result-panel", "sn-battle-result-panel");
            if (asking) panel.AddToClassList("sn-battle-result-wide");
            string foeName = feed.FoeId >= 0 ? LineT(feed.MonKey(feed.FoeId)) : stage.T(BattleFeed.K + "mon.unknown");
            string title = !feed.Ended ? stage.T("screen.battle.result.flee").Replace("{mon}", foeName)
                : feed.Result == BattleResult.Captured ? stage.T("screen.battle.result.captured").Replace("{mon}", foeName)
                : feed.Result == BattleResult.Fled ? stage.T("screen.battle.result.fled").Replace("{mon}", foeName)
                : feed.Drawn ? stage.T("screen.battle.result.draw")
                : feed.Mine ? stage.T(feed.Won ? "screen.battle.result.win" : "screen.battle.result.lose")
                : stage.T(feed.Won ? "screen.battle.result.win.seat" : "screen.battle.result.lose.seat").Replace("{name}", flow.SeatName(SideSeat(feed)));   // 남의 배틀: 아래쪽 좌석의 승패(Won = 아래쪽이 이김) — 「승리!」가 나인 듯 보이지 않게
            Add(panel, Kit.Wrapped(KoJosa.Fix(title), Kit.FTitle), "battle-result-title", "kit-title").AddToClassList("sn-battle-result-title");   // 긴 이름은 창 안에서 줄이 바뀐다
            var lines = Add(panel, new VisualElement(), "battle-result-lines", null);
            if (feed.Ended)
            {
                // 받은 것: 이긴(잡은) 좌석의 여행 점수·규토리(BattleEnded) + 도망친 야생이 남긴 규토리(Fled)
                int jp = 0, apricorns = 0;
                var colors = new int[SeatState.ApricornColors];
                foreach (var list in new[] { feed.Won ? feed.Changes : new ResChange[0], feed.FledChanges })
                    foreach (var c in list)
                    {
                        if (c == null || c.Seat != SideSeat(feed)) continue;   // 남의 배틀은 그 좌석이 받은 것(T16 — 그 사람 화면과 같게)
                        if (c.Res == ResKind.Jp) jp += c.After - c.Before;
                        else if (c.Res == ResKind.Apricorn)
                        {
                            apricorns += c.After - c.Before;
                            if (c.Key >= 0 && c.Key < colors.Length) colors[c.Key] += c.After - c.Before;
                        }
                    }
                if (jp > 0) Drop(Add(lines, Kit.Text(stage.T("screen.battle.result.jp").Replace("{n}", jp.ToString(inv)), Kit.F2), "battle-result-jp", null));
                ResultApricorns.Clear();
                if (apricorns > 0)
                {
                    // 얻은 규토리는 색마다 아이콘 + 수(결정 101 ③ — 「규토리 +n」 글 대신)
                    var row = Add(lines, new VisualElement(), "battle-result-apricorn", "sn-battle-result-row");
                    Add(row, Kit.Text(stage.T("screen.battle.result.apricorn"), Kit.F2), null, null);
                    for (int c = 0; c < colors.Length; c++)
                    {
                        if (colors[c] <= 0) continue;
                        var color = (ApricornColor)c;
                        var chip = Add(row, new VisualElement(), "battle-result-apricorn-" + color.ToString().ToLowerInvariant(), "sn-battle-result-chip");
                        WorkUi.Apricorn(chip, color);
                        Add(chip, Kit.Text("+" + colors[c].ToString(inv), Kit.F2), null, null);
                        ResultApricorns.Add(new KeyValuePair<ApricornColor, int>(color, colors[c]));
                    }
                    Drop(row);
                }
            }
            // 2막(94행 — 결정 74): 간부를 이겨 받은 조각 · 열린 파티 칸이 막 목표 단계에 차례로 내려앉는다
            bool goalFirst = true;
            if (feed.ShardGot >= 0)
            {
                Drop(Add(lines, Kit.Text(KoJosa.Fix(stage.T("screen.battle.result.shard").Replace("{shard}", stage.T("screen.shard." + feed.ShardGot.ToString(inv)))), Kit.F2), "battle-result-shard", null), goalFirst);
                goalFirst = false;
            }
            if (feed.SlotsOpened > 0)
            {
                Drop(Add(lines, Kit.Text(stage.T("screen.battle.result.slot"), Kit.F2), "battle-result-slot", null), goalFirst);
                goalFirst = false;
            }
            if (feed.GoalMetAct >= 0)
            {
                string goal = stage.TOrNull("screen.battle.result.goal." + feed.GoalMetAct.ToString(inv));
                if (goal != null) Drop(Add(lines, Kit.Wrapped(goal, Kit.F2), "battle-result-goal", null), goalFirst);
            }
            // 여행 스탬프(47행 — 결정 74): 이 배틀로 받은 스탬프와 기술 카드가 그 스탬프 단계의 도장이 찍힐 때 내려앉는다(덮개 DealFx가 그 위에서 찍는다)
            ResultStampApricorns.Clear();
            foreach (var got in feed.Stamps)
            {
                int at = 0;
                if (playing != null)
                    foreach (var st in playing.Steps)
                        if (st.Row == DealRows.Stamp && st.Events.Contains(got)) at = st.StartMs + StampBeats.SlamAt;
                var stampLine = Add(lines, Kit.Text(stage.T("screen.battle.result.stamp").Replace("{n}", got.CountAfter.ToString(inv)), Kit.F2), "battle-result-stamp", null);
                stampLine.pickingMode = PickingMode.Ignore;
                resultDrops.Add(new KeyValuePair<int, VisualElement>(at, stampLine));
                // 어떤 조건으로 무슨 스탬프를 찍었나(결정 101 ④): 도장(번호) + 조건 문구
                int slot = DealFx.SlotOf(ctx, got.StampKey);
                if (slot >= 0)
                {
                    var why = Add(lines, new VisualElement(), "battle-result-stamp-cond", "sn-battle-result-row");
                    why.Add(StampScreen.Seal(slot));
                    Add(why, Kit.Text(stage.T("screen.stamp.cond." + (slot + 1).ToString(inv)), Kit.F2), null, null);
                    why.pickingMode = PickingMode.Ignore;
                    resultDrops.Add(new KeyValuePair<int, VisualElement>(at, why));
                }
                // 그 스탬프의 보상 규토리(결정 80 — M9 다듬기 P19): 색마다 아이콘 + 수(위의 규토리 줄과 같은 꼴)
                var paid = new int[SeatState.ApricornColors];
                int paidAll = 0;
                if (got.Changes != null)
                    foreach (var c in got.Changes)
                    {
                        if (c == null || c.Seat != got.Seat || c.Res != ResKind.Apricorn || c.Key < 0 || c.Key >= paid.Length || c.After <= c.Before) continue;
                        paid[c.Key] += c.After - c.Before;
                        paidAll += c.After - c.Before;
                    }
                if (paidAll > 0)
                {
                    var reward = Add(lines, new VisualElement(), "battle-result-stamp-reward", "sn-battle-result-row");
                    Add(reward, Kit.Text(stage.T("screen.battle.result.stamp.reward"), Kit.F2), null, null);
                    for (int c = 0; c < paid.Length; c++)
                    {
                        if (paid[c] <= 0) continue;
                        var color = (ApricornColor)c;
                        var chip = Add(reward, new VisualElement(), "battle-result-stamp-apricorn-" + color.ToString().ToLowerInvariant(), "sn-battle-result-chip");
                        WorkUi.Apricorn(chip, color);
                        Add(chip, Kit.Text("+" + paid[c].ToString(inv), Kit.F2), null, null);
                        ResultStampApricorns.Add(new KeyValuePair<ApricornColor, int>(color, paid[c]));
                    }
                    reward.pickingMode = PickingMode.Ignore;
                    resultDrops.Add(new KeyValuePair<int, VisualElement>(at, reward));
                }
                if (got.MoveKeys == null || got.MoveKeys.Length == 0) continue;
                var cards = Add(lines, Kit.Text(stage.T("screen.battle.result.stamp.moves").Replace("{n}", got.MoveKeys.Length.ToString(inv)), Kit.F2), "battle-result-stamp-moves", null);
                cards.pickingMode = PickingMode.Ignore;
                resultDrops.Add(new KeyValuePair<int, VisualElement>(at, cards));
            }
            // 트레이너 카드 해금(70행 — M5에서 넘어온 N4 · M6): 「○○ 카드 실버 해금!」 한 줄(스탬프 줄 꼴). 카드 뒤집기 연출(48행)은 [확인] 뒤 보드에서
            foreach (var got in feed.Unlocks)
            {
                string card = got.CardKey >= 0 && got.CardKey < ctx.Cards.TrainerCards.Count ? stage.T("trainer_card." + ctx.Cards.TrainerCards[got.CardKey].Key) : "";
                string tier = stage.T("screen.trainer.tier." + Mathf.Clamp(got.TierAfter, 2, 3).ToString(inv));
                Drop(Add(lines, Kit.Wrapped(stage.T("screen.battle.result.unlock").Replace("{card}", card).Replace("{tier}", tier), Kit.F2), "battle-result-unlock", null));
            }
            if (feed.ReleasedKey != null) lines.Add(Kit.Text(KoJosa.Fix(stage.T("screen.battle.result.released").Replace("{mon}", stage.T(feed.ReleasedKey))), Kit.F2));
            if (feed.Taught != null)
                lines.Add(Kit.Text(KoJosa.Fix(stage.T("screen.battle.result.taught").Replace("{mon}", ViewName(feed.Taught.PokemonId)).Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, feed.Taught.MoveKey)))), Kit.F2));
            SpoilsLine(lines, feed);   // 트레이너 배틀 승리 보상(46행)
            // 울트라 버스트(M6 「UB 배틀 화면 묶음」): 프리즘 아머가 옮겨 감(88행) · 모두 이동력 +2(92행) · 네크로즈마 다음 페이즈(90행 — 보드 연출은 [확인] 뒤)
            if (feed.PrismMove != null && feed.PrismMove.After != null && feed.PrismMove.After.Length > 1)
                Drop(Add(lines, Kit.Wrapped(stage.T("screen.battle.result.prism").Replace("{a}", feed.PrismMove.After[0].ToString(inv)).Replace("{b}", feed.PrismMove.After[1].ToString(inv)), Kit.F2), "battle-result-prism", null));
            if (feed.Guardian != null)
                Drop(Add(lines, Kit.Wrapped(stage.T("screen.battle.result.guardian").Replace("{n}", UbTrial.MovePerGuardian.ToString(inv)).Replace("{total}", (UbTrial.MovePerGuardian * feed.Guardian.Total).ToString(inv)), Kit.F2), "battle-result-guardian", null));
            if (feed.PhaseDown != null)
                Drop(Add(lines, Kit.Text(stage.T("screen.battle.result.phase").Replace("{n}", feed.PhaseDown.Next.ToString(inv)), Kit.F2), "battle-result-phase", null));
            var view = flow.Client.View;
            if (feed.Whiteout) Drop(Add(lines, Kit.Wrapped(stage.T(BlastedOff(feed, view) ? "screen.battle.result.blastoff" : "screen.battle.result.whiteout"), Kit.F2), null, null));
            if (feed.RevealedAct >= 0 && view.Hos != null)
            {
                string act = stage.TOrNull("screen.board.act." + view.Hos.Act.ToString(inv));
                if (act != null) Drop(Add(lines, Kit.Text(KoJosa.Fix(stage.T("screen.battle.result.act").Replace("{act}", act)), Kit.F2), null, null));
            }
            if (asking)
            {
                FillReward(panel, choice, legal);
                return;
            }
            // [확인]은 그 항목이 다 재생된 뒤에 보인다(자리는 처음부터 잡아 둔다 — 줄이 내려앉는 동안 창 크기가 바뀌지 않게)
            resultFoot = Add(panel, new VisualElement(), "battle-result-foot", "sn-battle-result-foot");
            if (feed.Mine) Add(resultFoot, Kit.Text(stage.T("screen.battle.result.turn"), Kit.F2), null, "kit-muted");   // 「배틀을 마치면 턴이 종료됩니다」는 당사자에게만
            Add(resultFoot, Kit.Button(stage.T("screen.battle.result.ok"), KitButtonKind.Main, () => Confirm()), "battle-result-ok", null).SetEnabled(!flow.ReleasePending);   // 균열 카드 묶음이 끝나 가르치기가 올 때까지(결정 185)
        }

        // team_rocket R: 쓸 포켓몬이 바닥나 고향 마을이 아닌 고른 칸으로 날아갔다(다음 턴 시작에 모두 회복 — rulings A35)
        bool BlastedOff(BattleFeed feed, GameState view)
        {
            int home = view.Seats[flow.Client.Seat].HomeVillage;
            return feed.WhiteoutTo >= 0 && home >= 0 && home < flow.Ctx.Fixtures.Fixtures.Count && flow.Ctx.Fixtures.Fixtures[home].Cell != feed.WhiteoutTo;
        }

        string ViewName(int pokemon)
        {
            var view = flow.Client.View;
            return pokemon >= 0 && pokemon < view.Pokemon.Count && view.Pokemon[pokemon].Def >= 0 ? LineT("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[pokemon].Def].Key) : stage.T(BattleFeed.K + "mon.unknown");
        }

        // 배틀 뒤 선택: 단추 하나 = 합법 답 하나(엔진 순서 — battle-reward-<n>). 값은 지금 보기에서 읽는다(배틀은 이미 닫혔을 수 있다)
        void FillReward(VisualElement panel, PendingChoice choice, List<ChoiceAnswer> legal)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            bool ready = Ready;
            var box = Add(panel, new VisualElement(), "battle-reward", "sn-battle-reward");
            var mons = MonArt.Load();
            rewards.AddRange(legal);
            VisualElement Tile(VisualElement parent, int id, int index, bool click)
            {
                var tile = Add(parent, new VisualElement(), click ? "battle-reward-" + (index + 1).ToString(inv) : null, "sn-battle-benchmon");
                var p = view.Pokemon[id];
                var def = ctx.Cards.Pokemon[p.Def];
                tile.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var col = Add(tile, new VisualElement(), null, "sn-battle-mon-info");
                col.Add(Kit.Text(LineT("pokemon." + def.Key), Kit.F2));
                int hp = PokemonRules.Hp(ctx, p), max = PokemonRules.MaxHp(ctx, p);
                col.Add(Kit.Pill(heart, hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
                if (click)
                {
                    tile.SetEnabled(ready);
                    stage.OnTap(tile, SoundKeys.Decide, e => PressReward(index));
                }
                return tile;
            }
            switch (choice.Sub)
            {
                case ChoiceSub.PartyFull:
                {
                    Add(box, Kit.Wrapped(stage.T("screen.battle.reward.partyfull"), Kit.F2), "battle-reward-ask", null);
                    var row = Add(box, new VisualElement(), null, "sn-battle-reward-row");
                    for (int i = 0; i < legal.Count; i++)
                    {
                        var tile = Tile(row, legal[i].Target, i, true);
                        if (legal[i].Target == choice.Subject) tile.Q(className: "sn-battle-mon-info").Add(Kit.Badge(stage.T("screen.battle.reward.caught"), KitBadgeKind.Gold, Kit.F1));
                    }
                    break;
                }
                case ChoiceSub.TeachMove:
                {
                    string moveKey = BattleFeed.MoveNameKey(ctx, choice.Subject);
                    Add(box, Kit.Wrapped(KoJosa.Fix(stage.T("screen.battle.reward.teach").Replace("{move}", stage.T(moveKey))), Kit.F2), "battle-reward-ask", null);
                    Add(box, MonParts.MoveRow(stage, ctx, choice.Subject, false, TipSide.Below, panel), "battle-reward-move", null);   // 정비 판(S28)의 기술 카드와 같은 줄
                    Add(box, Kit.Wrapped(stage.T("screen.battle.reward.teach.slot"), Kit.F1), null, "kit-muted");
                    // 포켓몬마다 한 칸: 도트 · 이름 + 기술 칸 단추(합법 답의 (포켓몬, 칸)만 — 칸에 지금 있는 기술 이름, 비었으면 「빈 칸」)
                    var row = Add(box, new VisualElement(), null, "sn-battle-reward-row");
                    VisualElement slots = null;
                    int tileOf = -1;
                    for (int i = 0; i < legal.Count; i++)
                    {
                        if (legal[i].Kind != AnswerKind.Pick) continue;
                        int index = i, id = legal[i].Target, slot = legal[i].Arg;
                        if (id != tileOf)
                        {
                            tileOf = id;
                            var tile = Tile(row, id, i, false);
                            slots = Add(tile.Q(className: "sn-battle-mon-info"), new VisualElement(), null, "sn-battle-slots");
                        }
                        int old = slot >= 0 && slot < view.Pokemon[id].Moves.Length ? view.Pokemon[id].Moves[slot] : -1;
                        var tactic = old < 0 ? LegendRules.TacticAt(view.Pokemon[id], slot) : LegendTactic.None;   // 결정 195: 전설 특수기술이 차지한 빈 칸 — 가르치면 덮인다
                        string label = old >= 0 ? stage.T(BattleFeed.MoveNameKey(ctx, old)) : tactic != LegendTactic.None ? stage.T(MonParts.TacticKey(tactic)) : stage.T("screen.battle.reward.teach.empty");
                        var button = Add(slots, Kit.Button(label, KitButtonKind.Normal, () => PressReward(index)), "battle-reward-" + (i + 1).ToString(inv), null);
                        button.SetEnabled(ready);
                        if (tactic != LegendTactic.None)
                        {
                            button.AddToClassList(MonParts.LegendSlotClass);
                            stage.Tips.Hook(button, label, MonParts.TacticTip(stage, tactic), TipSide.Above, reading: true);
                        }
                        if (ready && old >= 0) MonParts.MoveTip(stage, ctx, button, old, TipSide.Below, panel);   // 바꿀 원래 기술의 이름 + 효과(결정 186)
                    }
                    for (int i = 0; i < legal.Count; i++)
                    {
                        if (legal[i].Kind != AnswerKind.Decline) continue;
                        int index = i;
                        Add(box, Kit.Button(stage.T("screen.battle.reward.teach.decline"), KitButtonKind.Normal, () => PressReward(index)), "battle-reward-" + (i + 1).ToString(inv), null).SetEnabled(ready);
                    }
                    break;
                }
                case ChoiceSub.TrainerReward:
                    FillSpoils(box, choice, legal, ready);
                    break;
                default:   // CoralColor: 받을 색 조합(합법 답의 조합 목록 그대로)
                {
                    Add(box, Kit.Wrapped(KoJosa.Fix(stage.T("screen.battle.reward.coral").Replace("{n}", choice.Count.ToString(inv))), Kit.F2), "battle-reward-ask", null);
                    var row = Add(box, new VisualElement(), null, "sn-battle-reward-row");
                    for (int i = 0; i < legal.Count; i++)
                    {
                        int index = i;
                        var button = Add(row, Kit.Button("", KitButtonKind.Normal, () => PressReward(index)), "battle-reward-" + (i + 1).ToString(inv), "sn-battle-colors");
                        foreach (int color in legal[i].Values) Icon(button, EncounterScreen.ApricornKey((ApricornColor)color));
                        button.SetEnabled(ready);
                    }
                    break;
                }
            }
        }

        // ── 입력 ──

        /// <summary>기술 줄을 눌렀다(위에서부터 0). 그 줄의 답(합법 <c>UseMove</c>)을 낸다. 입력이 닫혀 있거나 없는 줄이면 false.</summary>
        public bool PressMove(int index)
        {
            if (!Open || index < 0 || index >= moves.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            if (choice == null) return false;
            stage.Tips.Hide();
            if (moves[index].Target == BattleRules.AlphaStrike) return OpenAlpha();   // 타입을 고르는 줄로(76행)
            return flow.Answer(choice, moves[index]).Accepted;
        }

        /// <summary>[가방]: 기술 줄 자리를 가진 볼 줄로 바꾼다(합법 답에 볼 던지기가 있을 때만). 엔진에 답을 내지 않는다.</summary>
        public bool OpenBag()
        {
            if (!BagOn || bag) return false;
            bag = true;
            partyOpen = false;
            Refresh();
            return true;
        }

        /// <summary>볼 줄의 [뒤로]: 기술 줄로 돌아간다.</summary>
        public bool CloseBag()
        {
            if (!bag) return false;
            bag = false;
            Refresh();
            return true;
        }

        /// <summary>[가방] 단추 · 단축키 Q(결정 164): 볼 줄이 떠 있으면 기술 줄로, 아니면 볼 줄로.</summary>
        public bool ToggleBag() => bag ? CloseBag() : OpenBag();

        /// <summary>
        /// 단축키(결정 164 — 단추 오른쪽 위 글자 = <see cref="MoveKeys"/> · Q · E)가 지금 누를 독 요소: 1~3 = 기술 줄(효과 순서를 묻는 중이면 순서 줄) · Q = [가방] · E = [기권].
        /// 그 단추가 켜져 있지 않으면(연출 중 · 관전 · 꺼진 단추 · 다른 줄이 떠 있음) null — 키는 마우스로 못 누르는 것을 누르지 않는다.
        /// </summary>
        public VisualElement KeyTarget(char key)
        {
            if (dock == null || !Open || ShowsResult) return null;
            int slot = Array.IndexOf(MoveKeys, key.ToString());
            if (slot >= 0)
            {
                if (AskingOrder) return slot < orders.Count ? dock.Q("battle-order-" + (slot + 1).ToString(CultureInfo.InvariantCulture)) : null;
                return moveRows && slot < moves.Count ? dock.Q("battle-move-" + (slot + 1).ToString(CultureInfo.InvariantCulture)) : null;
            }
            if (key == 'Q') return BagOn ? dock.Q("battle-bag") : null;
            if (key == 'E') return ForfeitOn ? dock.Q("battle-forfeit") : null;
            return null;
        }

        /// <summary>
        /// E로 기권을 한 번 눌러 둔 상태인가(메인 가정 2026-11-21 — 보드에서 E로 돌리던 손버릇에 기권되지 않게): 키로 기권할 때만 E를 한 번 더 눌러야 하고, 그 사이 차례 칸에 안내 한 줄.
        /// 다른 키 · 다른 선택 · [기권]이 꺼지면 풀린다. 마우스 [기권]은 그대로 한 번에.
        /// </summary>
        public bool ForfeitArmed { get; private set; }
        int armedChoice = -1;

        /// <summary>단축키를 누른다 — 그 단추 · 줄과 같은 함수(<see cref="PressMove"/> · <see cref="PressOrder"/> · <see cref="ToggleBag"/> · <see cref="PressForfeit"/>). <see cref="KeyTarget"/>이 null이면 아무 일도 없다. E는 두 번째에 기권한다(<see cref="ForfeitArmed"/>).</summary>
        public bool PressKey(char key)
        {
            if (KeyTarget(key) == null) return false;
            if (key != 'E' && ForfeitArmed)
            {
                ForfeitArmed = false;
                Refresh();
            }
            int slot = Array.IndexOf(MoveKeys, key.ToString());
            if (slot >= 0) return AskingOrder ? PressOrder(slot) : PressMove(slot);
            if (key == 'Q') return ToggleBag();
            if (key != 'E') return false;
            if (ForfeitArmed)
            {
                ForfeitArmed = false;
                return PressForfeit();
            }
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            ForfeitArmed = choice != null;
            armedChoice = choice != null ? choice.Id : -1;
            Refresh();
            return ForfeitArmed;
        }

        /// <summary>Esc(M9 다듬기 P23): 기술 줄 자리에 떠 있는 [뒤로] 있는 줄(가방 · 알파 스트라이크 타입 · 대기 포켓몬 기술 · 회복할 포켓몬)을 닫고 기술 줄로. 닫았으면 true.</summary>
        public bool CloseRow() => CloseBag() || CloseAlpha() || ClosePartyMoves() || CloseAbility();

        /// <summary>볼 줄을 눌렀다(위에서부터 0). 그 줄의 답(합법 <c>ThrowBall</c>)을 낸다.</summary>
        public bool PressBall(int index)
        {
            if (!Open || !bag || index < 0 || index >= balls.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            if (choice == null) return false;
            stage.Tips.Hide();
            var answer = balls[index];
            bag = false;
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>대기 포켓몬 칸을 눌렀다(왼쪽에서부터 0): 출전·다음 포켓몬·교체의 답(합법 <c>Pick</c>)을 낸다.</summary>
        public bool PressBench(int index)
        {
            if (!Open || index < 0 || index >= bench.Count) return false;
            var choice = flow.Client.FirstChoice();
            if (choice == null || (choice.Kind != ChoiceKind.SendIn && choice.Kind != ChoiceKind.NextPokemon && choice.Kind != ChoiceKind.SwitchWindow)) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, bench[index]).Accepted;
        }

        /// <summary>교체 창의 [교체 안 함].</summary>
        public bool PressStay()
        {
            if (!Open || !CanStay) return false;
            var choice = flow.Client.Choice(ChoiceKind.SwitchWindow);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline) : null;
            if (answer == null) return false;
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>배틀 뒤 선택의 단추를 눌렀다(합법 답 순서로 0부터).</summary>
        public bool PressReward(int index)
        {
            if (!Ready || index < 0 || index >= rewards.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reward);
            if (choice == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, rewards[index]).Accepted;
        }

        /// <summary>
        /// 그 답에 맞는 화면 요소를 누른다(확인용 길잡이가 쓴다 — 기술 줄 · [가방] → 볼 줄 · 대기 포켓몬 칸 · [교체 안 함] · 토큰 · 배틀 뒤 선택 · [기권]).
        /// 화면에 그 답의 요소가 없으면 false다(화면이 받지 못하는 답).
        /// </summary>
        public bool Press(ChoiceAnswer answer)
        {
            if (answer == null) return false;
            if (rewards.Count > 0) return PressReward(rewards.FindIndex(a => a.SameAs(answer)));
            if (AskingToken) return PressToken(answer.Kind == AnswerKind.Accept);
            if (AskingOrder) return PressOrder(orders.FindIndex(a => a.SameAs(answer)));
            if (AskingFate)
            {
                if (answer.Kind == AnswerKind.Decline) return PressFatePass();
                if (IsAmp(answer)) return PressAmp(answer.Arg);   // 앰프(결정 134)
                if (answer.Kind == AnswerKind.UseAbility) return PsychicOn(flow.Client.Legal(flow.Client.Choice(ChoiceKind.Reaction))) ? PressPsychicAnswer(answer) : PressFatePotion(answer.Target);
                if (answer.Kind != AnswerKind.PlayFate || !Open) return false;   // 입력이 닫혀 있으면 고른 주사위를 건드리지 않는다
                fateDice.Clear();
                foreach (int v in answer.Values) if (v >= 0) fateDice.Add(v);
                var card = (FateCard)answer.Target;
                var splits = FateSplits(card);
                int wild = FateRules.WildsOf(answer);
                fateWild = wild < 0 ? 0 : Math.Max(0, splits.IndexOf(wild));
                return PressFate(card, answer.Arg);
            }
            if (AskingAsk) return PressAsk(answer.Kind == AnswerKind.Accept);
            if (AskingRespond) return PressRespond(answer.Kind == AnswerKind.Accept);
            if (AskingPick) return PressPick(answer.Kind == AnswerKind.Decline ? -1 : picks.FindIndex(a => a.SameAs(answer)));
            switch (answer.Kind)
            {
                case AnswerKind.UseMove:
                    if (answer.Target == BattleRules.AlphaStrike && answer.Arg < 0)
                    {
                        if (bag || (!alphaOpen && !OpenAlpha())) return false;
                        return PressAlpha(alphaTypes.FindIndex(a => a.SameAs(answer)));
                    }
                    if (answer.Arg >= 0)
                    {
                        if (!partyOpen && !OpenPartyMoves()) return false;
                        int move = partyMoves.FindIndex(a => a.SameAs(answer));
                        for (int k = 0; move >= 0 && move / MoveRows != PartyPage && k < PartyPages; k++) NextPartyMoves();   // 화면에 보이는 쪽으로 넘긴 뒤 누른다
                        return PressPartyMove(move);
                    }
                    return !bag && !alphaOpen && !partyOpen && PressMove(moves.FindIndex(a => a.SameAs(answer)));
                case AnswerKind.ThrowBall:
                    if (!bag && !OpenBag()) return false;
                    return PressBall(balls.FindIndex(a => a.SameAs(answer)));
                case AnswerKind.Forfeit: return PressForfeit();
                case AnswerKind.UseAbility:
                    if (IsCherish(answer)) return PressCherish();
                    if (!abilityOpen && !OpenAbility()) return false;
                    int pick = abilityPicks.FindIndex(a => a.SameAs(answer));
                    return PressAbility(pick);
                case AnswerKind.Pick: return PressBench(bench.FindIndex(a => a.SameAs(answer)));
                case AnswerKind.Decline: return PressStay();
                default: return false;
            }
        }

        /// <summary>[기권]: 합법 답에 기권이 있을 때만 낸다.</summary>
        public bool PressForfeit()
        {
            if (!Open) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Forfeit) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>우선권 토큰 묻기의 [사용]·[넘기기].</summary>
        public bool PressToken(bool use)
        {
            if (!Open) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            if (choice == null || choice.Sub != ChoiceSub.InitiativeToken) return false;
            var answer = flow.Client.Legal(choice).Find(a => a.Kind == (use ? AnswerKind.Accept : AnswerKind.Decline));
            if (answer == null) return false;
            return flow.Answer(choice, answer).Accepted;
        }

        // ── 운명 · 「쓸까」 · 고르기 · 응답(M5 「운명·응답 화면」) ──

        // 묻는 기술의 이름(EffectOption·EffectPick의 출처) — 안내 줄의 {move}
        string AskMove(PendingChoice choice)
        {
            if (choice == null || choice.Source.Kind != SourceKind.Move || choice.Source.Id < 0) return "";
            return stage.T(BattleFeed.MoveNameKey(flow.Ctx, choice.Source.Id));
        }

        List<int> FateSplits(FateCard card) => flow.Client != null && flow.Client.View != null ? FateRules.WildSplits(flow.Client.View, flow.Ctx, flow.Client.Seat, card) : new List<int>();

        /// <summary>지금 고른 주사위·아르세우스 칩으로 그 카드의 n장 답(합법 답에 없으면 null).</summary>
        public ChoiceAnswer FateAnswer(FateCard card, int n)
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.Reaction) : null;
            if (choice == null || choice.Sub != ChoiceSub.Fate) return null;
            var a = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)card, n);
            if (card == FateCard.Dialga)
            {
                if (n == 1 && fateDice.Count != 1) return null;
                if (fateDice.Count == 0) return null;
                a.Values.AddRange(fateDice);
            }
            if (n == FateRules.Play3Count)
            {
                var splits = FateSplits(card);
                if (fateWild > 0 && fateWild < splits.Count) a.Values.Add(FateRules.WildPay(splits[fateWild]));
            }
            var legal = flow.Client.Legal(choice);
            return legal.Find(x => x.SameAs(a));
        }

        // 독의 운명 줄: 디아루가면 주사위 칩 줄(누르면 고름) + 카드마다 한 줄([1장]·[3장] · 아르세우스 칩)
        void FillFate(VisualElement list, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var view = flow.Client.View;
            list.AddToClassList("sn-battle-orders");
            int rows = 0;
            bool dialga = legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga);
            var faces = view.Battle != null ? view.Battle.Faces : null;
            // psychic R(사용자 결정 131 — 굴린 뒤 반응 창의 반응): 같은 주사위 줄에서 하나를 고르고 능력 줄의 새 눈 단추를 누른다
            bool mind = PsychicOn(legal);
            if ((dialga || mind) && faces != null)
            {
                var row = Add(list, new VisualElement(), "battle-fate-dice", "sn-battle-move");
                row.AddToClassList("sn-battle-fate-row");
                Add(row, Kit.Text(stage.T("screen.battle.fate.dice"), Kit.F2), null, "kit-muted");
                for (int i = 0; i < faces.Count; i++)
                {
                    int index = i;
                    var chip = Add(row, new VisualElement(), "battle-fate-die-" + (i + 1).ToString(inv), "sn-battle-fate-die");
                    chip.Add(faces[i] == AttackDieFace.None ? (VisualElement)Kit.Badge("–", KitBadgeKind.Plain, Kit.F1) : FaceIcon(faces[i]));
                    if (fateDice.Contains(i)) chip.AddToClassList("sn-battle-fate-die-picked");
                    stage.OnTap(chip, SoundKeys.Select, e => ToggleFateDie(index), () => Open);
                }
                if (open) stage.Tips.Hook(row, null, stage.T(dialga ? "screen.battle.fate.dice.tip" : "screen.battle.fate.psychic.dice.tip"), TipSide.Above, reading: true, clear: dock);
                rows++;
            }
            if (mind && faces != null)
            {
                int card = CardAsk.CardOf(view, flow.Client.Seat);
                var row = Add(list, new VisualElement(), PsychicRow, "sn-battle-move");
                row.AddToClassList("sn-battle-fate-row");
                var name = Add(row, Kit.Text(stage.T(CardAsk.AbilityKey(flow.Ctx, card, 2)), Kit.F2), null, "sn-battle-move-name");
                stage.Tips.Hook(name, CardAsk.Head(stage, flow.Ctx, card, 2), CardAsk.Text(stage, flow.Ctx, card, 2), TipSide.Above, reading: true, clear: dock);
                var end = Add(row, new VisualElement(), null, "sn-battle-fate-buttons");
                for (int to = 0; to < PsychicFaces.Length; to++)
                {
                    int face = to;
                    string label = stage.T("screen.battle.fate.psychic." + PsychicFaces[face]);
                    var b = Add(end, Kit.Button(label, KitButtonKind.Main, () => PressPsychic(face)), PsychicRow + "-" + PsychicFaces[face], null);
                    b.SetEnabled(open && PsychicAnswer(face) != null);
                    if (open) stage.Tips.Hook(b, null, KoJosa.Fix(stage.T("screen.battle.fate.psychic.to.tip").Replace("{face}", label)), TipSide.Above, reading: true, clear: dock);
                }
                rows++;
            }
            // guitarist B·R 앰프(사용자 결정 134 — 같은 창의 공격하는 좌석 반응): 능력 이름 + [데미지 +n] · 전기 기술이면 [데미지 +2n (2배)]
            var amps = legal.FindAll(IsAmp);
            if (amps.Count > 0)
            {
                int card = CardAsk.CardOf(view, flow.Client.Seat);
                int amp = view.Seats[flow.Client.Seat].Amp;
                var row = Add(list, new VisualElement(), AmpRow, "sn-battle-move");
                row.AddToClassList("sn-battle-fate-row");
                var name = Add(row, Kit.Text(stage.T(CardAsk.AbilityKey(flow.Ctx, card, 1)), Kit.F2), null, "sn-battle-move-name");
                stage.Tips.Hook(name, CardAsk.Head(stage, flow.Ctx, card, 1), CardAsk.Text(stage, flow.Ctx, card, 1), TipSide.Above, reading: true, clear: dock);
                var end = Add(row, new VisualElement(), null, "sn-battle-fate-buttons");
                foreach (var a in amps)
                {
                    int times = a.Arg;
                    string label = stage.T(times == 2 ? "screen.card.pick.amp2" : "screen.card.pick.amp").Replace("{n}", (amp * times).ToString(inv));
                    var b = Add(end, Kit.Button(label, KitButtonKind.Main, () => PressAmp(times)), AmpRow + "-" + times.ToString(inv), null);
                    b.SetEnabled(open);
                    if (open) stage.Tips.Hook(b, null, times == 2 ? CardAsk.Text(stage, flow.Ctx, card, 2) : stage.T("screen.battle.fate.amp.tip"), TipSide.Above, reading: true, clear: dock);
                }
                rows++;
            }
            foreach (FateCard card in new[] { FateCard.Dialga, FateCard.Palkia, FateCard.Giratina, FateCard.Arceus })
            {
                bool one = legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)card && a.Arg == 1);
                bool three = legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)card && a.Arg == FateRules.Play3Count);
                if (!one && !three) continue;
                string key = FateCards.Key(card);
                var row = Add(list, new VisualElement(), "battle-fate-" + key, "sn-battle-move");
                row.AddToClassList("sn-battle-fate-row");
                var art = Add(row, new Image { image = FateCards.Art(card), scaleMode = ScaleMode.ScaleAndCrop, uv = FateCards.ArtUv, pickingMode = PickingMode.Ignore }, null, "sn-battle-fate-art");
                Add(row, Kit.Text(stage.T(FateCards.NameKey(card)), Kit.F2), null, "sn-battle-move-name");
                stage.Tips.Hook(art, stage.T(FateCards.NameKey(card)), FateCards.Tip(stage, card), TipSide.Above, reading: true, clear: dock);
                var end = Add(row, new VisualElement(), null, "sn-battle-fate-buttons");
                if (three)
                {
                    var splits = FateSplits(card);
                    if (splits.Count > 1)
                        for (int k = 0; k < splits.Count; k++)
                        {
                            int chip = k;
                            var w = Add(end, Kit.Button(stage.T("screen.battle.fate.wild").Replace("{n}", splits[k].ToString(inv)), k == fateWild ? KitButtonKind.Main : KitButtonKind.Normal, () => PickFateWild(chip)), "battle-fate-wild-" + splits[k].ToString(inv), null);
                            w.SetEnabled(open);
                            if (open) stage.Tips.Hook(w, null, stage.T("screen.battle.fate.wild.tip").Replace("{n}", splits[k].ToString(inv)), TipSide.Above, reading: true, clear: dock);   // 칩의 숫자 = 3장에 섞을 아르세우스 장수
                        }
                }
                if (one)
                {
                    var b = Add(end, Kit.Button(stage.T("screen.battle.fate.use.1"), KitButtonKind.Main, () => PressFate(card, 1)), "battle-fate-" + key + "-1", null);
                    b.SetEnabled(open && FateAnswer(card, 1) != null);
                    if (open) stage.Tips.Hook(b, null, stage.T(FateCards.NameKey(card) + ".text.1"), TipSide.Above, reading: true, clear: dock);
                }
                if (three)
                {
                    var b = Add(end, Kit.Button(stage.T("screen.battle.fate.use.3"), KitButtonKind.Main, () => PressFate(card, FateRules.Play3Count)), "battle-fate-" + key + "-3", null);
                    b.SetEnabled(open && FateAnswer(card, FateRules.Play3Count) != null);
                    if (open) stage.Tips.Hook(b, null, stage.T(FateCards.NameKey(card) + ".text.2"), TipSide.Above, reading: true, clear: dock);
                }
                rows++;
            }
            // lady R(자체 검토 — 배틀 중 언제든): 같은 창의 합법 답에 든 UseAbility(Target = 포켓몬) — 능력 이름 + 포켓몬마다 단추
            var potions = mind ? new List<ChoiceAnswer>() : legal.FindAll(a => a.Kind == AnswerKind.UseAbility && !IsAmp(a));   // psychic R(결정 131) · 앰프(결정 134)의 UseAbility는 위 줄
            if (potions.Count > 0)
            {
                int card = CardAsk.CardOf(view, flow.Client.Seat);
                var row = Add(list, new VisualElement(), FatePotion, "sn-battle-move");
                row.AddToClassList("sn-battle-fate-row");
                var name = Add(row, Kit.Text(stage.T(CardAsk.AbilityKey(flow.Ctx, card, 2)), Kit.F2), null, "sn-battle-move-name");
                stage.Tips.Hook(name, CardAsk.Head(stage, flow.Ctx, card, 2), CardAsk.Text(stage, flow.Ctx, card, 2), TipSide.Above, reading: true, clear: dock);
                var end = Add(row, new VisualElement(), null, "sn-battle-fate-buttons");
                for (int i = 0; i < potions.Count; i++)
                {
                    int target = potions[i].Target;
                    var b = Add(end, Kit.Button(CardAsk.PokemonName(stage, flow.Ctx, view, target), KitButtonKind.Main, () => PressFatePotion(target)), FatePotion + "-" + (i + 1).ToString(inv), null);
                    b.SetEnabled(open);
                    HookMon(b, target);   // 결정 169
                }
                rows++;
            }
            if (rows == 0) Add(Add(list, new VisualElement(), "battle-fate-none", "sn-battle-move"), Kit.Text(stage.T("screen.battle.fate.none"), Kit.F2), null, "kit-muted");   // 손패는 있지만 이 창에 낼 카드가 없다(숨은 손패 창 — 손패를 남이 알 수 없게 늘 열린다)
            for (int i = Math.Max(1, rows); i < MoveRows; i++) Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
        }

        // 독의 고르기 단추(EffectPick — 「안 함」은 차례 칸의 [넘기기]): 값의 이름은 그 기술이 고르게 하는 것으로 읽는다.
        // 빈 기술 칸 셋을 다른 차례처럼 깔고, 단추는 그 위 첫 칸부터 왼쪽으로 붙여 놓는다(결정 87 V1 ① — 칸 없이 빈 자리였고 단추가 독 양 끝으로 벌어졌다)
        void FillPicks(VisualElement list, PendingChoice choice, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < MoveRows; i++) Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
            var row = Add(list, new VisualElement(), PickRow, "sn-battle-picks");
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick) continue;
                int index = picks.Count;
                picks.Add(a);
                string icon = CardAsk.FromCard(choice) ? CardAsk.IconKey(flow.Ctx, choice, a.Target) : null;   // 트레이너 카드의 규토리 색 · 타입은 그림(+ 이름)
                var b = Add(row, CardAsk.Button(PickLabel(choice, a.Target), icon, KitButtonKind.Normal, () => PressPick(index)), "battle-pick-" + (index + 1).ToString(inv), null);
                b.SetEnabled(open);
            }
        }

        /// <summary>
        /// 고르기 값의 이름(기술 효과마다 고르는 것이 다르다 — rulings G20·G21): 미래예지 = 맞히기 셋 · 상대·내 기술(foul_play·mimic·kinesis · 둘째 공격) = 기술 이름 ·
        /// photon_geyser = 상태 이상 · 규토리를 쓰는 효과(belch · bug_bite) = 색 · adaptive_cloak 첫 고르기 = 폼 · 대상 고르기 = 포켓몬 · 그 밖 = 타입 [가정].
        /// </summary>
        public string PickLabel(PendingChoice choice, int value)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            if (CardAsk.FromCard(choice)) return CardAsk.PickLabel(stage, ctx, view, choice, value);   // 트레이너 카드의 고르기(값 종류는 카드마다 — 규토리 색 · 심볼 수 · 면 · 상태 · 포켓몬 …)
            string key = choice.Source.Kind == SourceKind.Move && choice.Source.Id >= 0 && choice.Source.Id < ctx.Cards.Moves.Count ? ctx.Cards.Moves[choice.Source.Id].Key : "";
            if (key == "future_sight") return stage.T("screen.battle.pick.guess." + (value == MoveHandlers.GuessMore ? "more" : value == MoveHandlers.GuessLess ? "less" : "equal"));
            bool moves = key == "foul_play" || key == "mimic" || key == "kinesis";
            if (!moves && choice.Subject >= 0 && choice.Subject < view.Pokemon.Count && choice.Count == 0)
            {
                var user = view.Pokemon[choice.Subject];
                moves = choice.Options.TrueForAll(o => Array.IndexOf(user.Moves, o) >= 0 || (ctx.Cards.Pokemon[user.Def].Signature == o));
            }
            if (moves && value >= 0 && value < ctx.Cards.Moves.Count) return stage.T(BattleFeed.MoveNameKey(ctx, value));
            if (key == "photon_geyser") return stage.T(MonParts.StatusKey((StatusEffect)value) ?? "screen.status.none");
            if (key == "belch" || key == "bug_bite" || choice.Options.TrueForAll(o => o >= 0 && o < SeatState.ApricornColors) && Array.IndexOf(ColorMoves, key) >= 0)
                return stage.T("screen.apricorn." + ((ApricornColor)value).ToString().ToLowerInvariant());
            if (key == "adaptive_cloak" && choice.Count == 0 && value >= 0 && value < ctx.Cards.Pokemon.Count) return stage.T("pokemon." + ctx.Cards.Pokemon[value].Key);
            if (choice.Options.TrueForAll(o => o >= 0 && o < view.Pokemon.Count && view.Pokemon[o].Def >= 0 && view.Pokemon[o].Owner != SeatId.System) && value < view.Pokemon.Count && !Enum.IsDefined(typeof(PokemonType), value))
                return stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[value].Def].Key);
            return Enum.IsDefined(typeof(PokemonType), value) ? stage.T("ui.types." + (PokemonType)value) : value.ToString(CultureInfo.InvariantCulture);
        }

        static readonly string[] ColorMoves = { "belch", "bug_bite", "pluck", "stuff_cheeks", "teatime" };

        // 디아루가 재굴림(43행): 굴림판에 멈춘 주사위를 다시 세우고 고른 주사위가 제자리에서 입체로 글리치한다(M9 다듬기 P38 — 재생은 GameFlow.StageReroll이 단계에 붙인 DiceRollScene, 다시 구르지 않는다).
        // 그 위의 머리 줄 = 주사위 줄(고른 칩이 깜빡이다 새 면으로 고정). 붙은 재생이 없거나 굴림판을 못 만들면 옛 그대로 독의 주사위 줄만
        void FillReroll(VisualElement list)
        {
            var inv = CultureInfo.InvariantCulture;
            var c = RerollBeats.Event(rerollStep);
            rerollBoard = null;
            rerollDrawn = null;
            var art = rerollStep.Tag is DiceRollScene ? Board(rollBody) : null;
            VisualElement row;
            if (art != null)
            {
                list.AddToClassList("sn-battle-roll");
                var panel = Add(list, new VisualElement(), "battle-reroll-panel", "sn-battle-rollpanel");
                rerollBoard = Add(panel, new VisualElement(), "battle-reroll-board", "sn-battle-rollboard");
                rerollBoard.style.backgroundImage = Background.FromRenderTexture(art.Output);
                row = Add(panel, new VisualElement(), "battle-reroll", "sn-battle-rollhead");
            }
            else row = Add(list, new VisualElement(), "battle-reroll", "sn-battle-move");
            row.AddToClassList("sn-battle-fate-row");
            Add(row, new Image { image = FateCards.Art(FateCard.Dialga), scaleMode = ScaleMode.ScaleAndCrop, uv = FateCards.ArtUv, pickingMode = PickingMode.Ignore }, null, "sn-battle-fate-art");
            Add(row, Kit.Text(stage.T("screen.battle.fate.reroll"), Kit.F2), null, "sn-battle-move-name");
            int n = flow.RollFaces.Count;
            foreach (var ch in c.Changes) n = Math.Max(n, ch.Index + 1);
            for (int i = 0; i < n; i++)
            {
                var chip = Add(row, new VisualElement(), "battle-reroll-die-" + (i + 1).ToString(inv), "sn-battle-fate-die");
                int k = Array.FindIndex(c.Changes, x => x.Index == i);
                if (k >= 0) chip.AddToClassList("sn-battle-fate-die-picked");
                rerollDice.Add(new KeyValuePair<int, VisualElement>(k, chip));
            }
            if (art == null) for (int i = 1; i < MoveRows; i++) Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
            RerollShown = true;
            AnimateReroll();
        }

        VisualElement rerollBoard;
        FxStep rerollDrawn;
        int rerollDrawnMs;
        // 마지막 공격 굴림의 주사위 몸 색(그 기술의 타입 색 — 재굴림이 같은 주사위를 다시 그린다)
        Color32 rollBody = DiceBoard.TypeBody(PokemonType.Typeless);

        /// <summary>재굴림 연출이 굴림판(입체 주사위)으로 그려지고 있나 — 확인용(M9 다듬기 P38).</summary>
        public bool RerollBoardShown => rerollStep != null && rerollBoard != null && rerollBoard.panel != null;

        void AnimateReroll()
        {
            if (rerollStep == null || rerollDice.Count == 0) return;
            if (rerollBoard != null && board != null && rerollStep.Tag is DiceRollScene glitched && !(ReferenceEquals(rerollDrawn, rerollStep) && rerollDrawnMs == local))
            {
                glitched.Pose(local, poses, out var glitch);
                board.Render(poses, glitched.Dice, glitch);
                rerollBoard.MarkDirtyRepaint();
                rerollDrawn = rerollStep;
                rerollDrawnMs = local;
            }
            var c = RerollBeats.Event(rerollStep);
            int per = PlayTiming.RerollPerMs.Value, t = local - rerollStep.StartMs;
            for (int i = 0; i < rerollDice.Count; i++)
            {
                var chip = rerollDice[i].Value;
                int k = rerollDice[i].Key;
                AttackDieFace face;
                float flash = 0f;
                if (k < 0) face = i < flow.RollFaces.Count ? flow.RollFaces[i] : AttackDieFace.None;
                else
                {
                    var pose = RerollBeats.At(t, k, per);
                    face = pose.Locked ? c.Changes[k].After : pose.Glitch > 0f ? (AttackDieFace)pose.Flicker : c.Changes[k].Before;
                    flash = pose.Flash;
                    chip.EnableInClassList("sn-battle-fate-die-glitch", pose.Glitch > 0f && !pose.Locked);
                }
                chip.EnableInClassList("sn-battle-fate-die-flash", flash > 0f);   // 클래스는 늘 그 시각 값으로(앞 호출에 기대지 않는다 — 한 번에 보내도 나눠 보내도 같다)
                if (chip.userData is AttackDieFace was && was == face) continue;
                chip.userData = face;
                chip.Clear();
                chip.Add(face == AttackDieFace.None ? (VisualElement)Kit.Badge("–", KitBadgeKind.Plain, Kit.F1) : FaceIcon(face));
            }
        }

        /// <summary>운명 줄의 주사위 칩을 누른다(고르기 · 다시 누르면 푼다).</summary>
        public bool ToggleFateDie(int index)
        {
            if (!Open || !AskingFate) return false;
            if (!fateDice.Remove(index)) fateDice.Add(index);
            Refresh();
            return true;
        }

        /// <summary>3장 효과에 함께 낼 아르세우스 칩(0 = 자동 나눔).</summary>
        public bool PickFateWild(int chip)
        {
            if (!Open || !AskingFate) return false;
            fateWild = chip;
            Refresh();
            return true;
        }

        /// <summary>운명 줄의 [1장]·[3장]: 지금 고른 주사위·칩으로 그 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressFate(FateCard card, int n)
        {
            if (!Open || !AskingFate) return false;
            var answer = FateAnswer(card, n);
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            if (answer == null || choice == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>운명 창의 [넘기기](안 씀 — 기본값).</summary>
        /// <summary>운명 창의 [이번 배틀 묻지 않기](방 판만) 요소 이름.</summary>
        public const string AutoPassButton = "battle-fate-autopass";

        /// <summary>
        /// 운명 창의 [이번 배틀 묻지 않기](방 판 — 04 §5 ①): 방에 SetAutoPass를 보낸다 — 방이 지금 열린 창과 이 배틀의 남은 운명 창을 최소 시간에 「안 씀」으로 넘긴다(02 [가정 M7-26]).
        /// 창이 열려 있지 않거나 방 판이 아니면 false.
        /// </summary>
        public bool PressAutoPass() => Open && AskingFate && flow.Room.AutoPass();

        public bool PressFatePass()
        {
            if (!Open || !AskingFate) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>「쓸까」(매혹적인 스튜 · 기술 효과)의 [사용]·[넘기기].</summary>
        public bool PressAsk(bool use)
        {
            if (!Open || !AskingAsk) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == (use ? AnswerKind.Accept : AnswerKind.Decline)) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>고르기 단추(위에서 0부터 — <see cref="Picks"/>), −1 = [넘기기](안 함).</summary>
        public bool PressPick(int index)
        {
            if (!Open || !AskingPick) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            if (choice == null) return false;
            var answer = index < 0 ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline) : index < picks.Count ? picks[index] : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>배틀 화면이 떠 있는 동안 열린 도전·거래 응답의 [수락]·[거절].</summary>
        public bool PressRespond(bool accept)
        {
            if (!Open || !AskingRespond) return false;
            var choice = flow.Client.Choice(ChoiceKind.Respond);
            // 거래를 받아들이며 줄 운명 카드를 골라야 하면(받아들이는 답이 여럿 — 결정 80 N2 나): 배틀 화면 위에 줄 카드 고르는 창(보드의 S41 프롬프트와 같다)
            if (accept && choice != null && choice.Sub == ChoiceSub.Trade && flow.Client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Accept).Count > 1)
            {
                stage.Tips.Hide();
                return flow.OpenTradePick();
            }
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == (accept ? AnswerKind.Accept : AnswerKind.Decline)) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>효과 순서 줄을 눌렀다(위에서 0부터 — 마우스 · 단축키 1~3, 결정 72).</summary>
        public bool PressOrder(int index)
        {
            if (!Open || index < 0 || index >= orders.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            if (choice == null || choice.Sub != ChoiceSub.EffectOrder) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, orders[index]).Accepted;
        }

        /// <summary>결과 창의 [확인]: 배틀 화면을 닫고 보드로 돌아간다(잡아 둔 턴 넘김 띠가 이어진다).</summary>
        public bool Confirm()
        {
            if (!ShowsResult || AskingReward || flow.ReleasePending) return false;
            flow.CloseBattle();
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
