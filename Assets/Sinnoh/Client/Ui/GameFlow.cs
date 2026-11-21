using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 지금 판의 저장 상태(04 S22): 저장하지 않는 판(확인용 · 저장 폴더 없음) · 저장됨 · 쓰기 실패(다음 명령 때 다시 쓴다).
    /// 「실패」는 셋을 묶는다 — 저장 폴더를 만들지 못해 저장 없이 도는 판(<see cref="GameFlow.SaveId"/>가 null — 나가면 사라진다) · 스냅샷·머리 쓰기 실패(로그 줄은 쓰여 있다) ·
    /// 로그 줄 쓰기 실패(그 명령은 거절돼 되돌려졌고 그 앞까지는 쓰여 있다). 뒤의 둘은 나가도 마지막으로 쓰인 곳까지 이어 받는다.
    /// </summary>
    public enum SaveStatus { Off, Saved, Failed }

    /// <summary>
    /// 화면 흐름과 게임 세션을 잇는 한 곳(04 §1 흐름, plan §4 「솔로 설정·셋업」·「보드 화면」): 타이틀 → 혼자 하기(S09) → 게임 준비(S12·S13) → 보드. 타이틀 → 설정(S02)도 여기서 연다.
    /// <list type="bullet">
    /// <item>세션: 솔로 설정에서 고른 값으로 <see cref="SoloGame.Start"/>(+ 원칙 AI) → 내 좌석의 손님(<see cref="Client"/> — 보기·선택·이벤트). 화면은 이것으로만 상태를 받고 답을 낸다.</item>
    /// <item>화면: 내 좌석에 열린 선택에 맞는 화면을 연다(고향 마을 고르기 → 스타팅 포켓몬·트레이너 카드). 셋업이 끝나면 화면을 닫고 보드 HUD(<see cref="BoardHud"/> — 늘 떠 있는 판)를 연다.
    /// 그 위에 게임 메뉴(<see cref="OpenMenu"/>)가 열린다.</item>
    /// <item>연출: 보드에서는 받은 이벤트를 화면 대기열(<see cref="Fx"/> — <c>Sinnoh.Fx</c>)에 넣는다. 걷기는 말을 칸에서 칸으로 옮기고(04 §8-8 11행), 턴 넘김은 HUD의 띠가 그린다(101행).
    /// 대기열이 비고 클릭 막기 시간이 지나야 입력이 열린다(<see cref="InputOpen"/>). 아직 부품이 없는 행은 대기열에서 덜어 낸다(<see cref="BuiltRow"/>).
    /// 카메라의 보는 점은 걷는 말을 따라가고(결정 57 ① — 방향·줌은 그대로), 내 턴 컷신 동안 내 말로 돌아온다(<see cref="Follow"/>).
    /// 마우스로 보드를 끌면 보는 점이 옮겨지고(결정 66 ② — <see cref="Pointer(float, float, bool, bool)"/>), 끈 단계·항목 동안은 따라가기를 쉰다.</item>
    /// <item>조우: 찾아보기 뒤 조우 고르기가 열리면 HUD 위에 조우 창(<see cref="EncounterScreen"/> — S45)을 연다. 고르면 야생 배틀이 시작돼 배틀 화면으로 바뀐다.
    /// 여는 때는 화면 대기열이 정한다(04 §4-10): 조우 창은 카드 공개(21행)가 시작할 때, 배틀 화면은 조우 전환(22행)의 가운데 — 가운데 필드의 줄무늬(<see cref="FieldWipe"/>)가 다 닫힌 때.</item>
    /// <item>배틀: <c>BattleStarted</c>가 든 갱신부터 배틀 하나를 따라가고(<see cref="Battle"/> — 기록 줄·결과), 그동안 HUD 위에 배틀 화면(<see cref="BattleScreen"/>)을 연다.
    /// 배틀이 끝나면 결과 창이 뜨고 화면 대기열을 세워 둔다(엔진은 이미 다음 턴이다 — 배틀 뒤 선택(놓아주기·기술 가르치기)이 열려 있으면 그 답 뒤에) — [확인]을 누르면(<see cref="CloseBattle"/>) 화면을 닫고 대기열을 풀어 턴 넘김 띠가 이어진다.</item>
    /// <item>막 카드(S14)·기록(S21)·보드 위 토큰: 막 공개(4행)는 HUD의 막 카드(<see cref="ActCard"/>)가 그리고, 받은 이벤트는 기록 줄(<see cref="Log"/> — <see cref="LogScreen"/>)로 남으며,
    /// 보기의 숨겨진 장소·조무래기 토큰을 보드에 세운다(<see cref="BoardTokens"/> — 조무래기는 서막의 놓기 연출 5행에서 떨어져 내려앉는다).</item>
    /// <item>보드: 타이틀·설정 뒤 전경은 시험 판 말 6, 게임이 시작되면 실제 좌석(고향 마을 색 · 고른 직업 그림 · 서 있는 칸)으로 다시 세운다. 카메라 자리와 보드 입력(Q·E·휠)도 화면에 따라 정한다 —
    /// 화면이 덮고 있는 동안은 입력을 끄고, 보드에서는 마우스가 HUD 위에 있을 때 휠과 칸 누르기를 막는다(<see cref="Pointer"/>).</item>
    /// </list>
    /// 엔진 루프를 모른다: 실행 중에는 <see cref="ScreenBoot"/>가 프레임마다 <see cref="Tick"/>·<see cref="Layout"/>·<see cref="Pointer"/>를 부르고, 배치 캡처·시험도 같은 것을 쓴다. 보드 없이(null)도 돈다.
    /// <para>저장(02 §7, 04 §1 — plan §4 「저장 기반·자동 저장」): 저장 폴더를 받은 흐름은 솔로 판을 처음부터 자동 저장한다(판 하나 = 폴더 하나 — 명령마다 로그 한 줄, 쉬는 때마다 스냅샷).
    /// 저장 목록(<see cref="Saves"/>) · 이어 받기(<see cref="Resume"/>) · 지우기(<see cref="DeleteSave"/>) · 저장 상태(<see cref="SaveStatus"/>)를 화면에 연다. 저장 폴더가 없으면(캡처·시험의 기본) 저장하지 않는다.</para>
    /// 시각은 <see cref="Tick"/>에 넣은 ms를 더한 값뿐이다(연출은 무대의 트윈 실행기가 따로 흘린다). 게임 시드는 밖에서 받는다(<paramref name="entropy"/> — 시험·캡처는 고정 값).
    /// </summary>
    public sealed partial class GameFlow
    {
        public readonly UiStage Stage;
        public readonly Ctx Ctx;

        readonly BoardBoot board;
        readonly BoardCameraRig rig;
        readonly Func<ulong> entropy;
        readonly Action quit;
        readonly bool? web;
        readonly WindowControl window;
        readonly string saves;
        readonly Func<long> clock;
        readonly string contentHash;
        bool tickFailed;
        Vector2Int laidFor;
        IUiScreen laidScreen;
        bool laid, gameSeats, boardCamSet, wasOpen;
        FxPlanner planner;
        int endedSeat = -1, turnApricorns, startedRound;
        FxStep camStep;
        Vector3 camFrom;
        // 마우스로 보드 끌기(결정 66 ②): 끌기 상태 · 잡은 땅의 점 · 끈 동안 따라가기를 쉬는 단계와 항목
        readonly BoardDrag drag = new BoardDrag();
        Vector3 dragGrab;
        bool dragGrabbed;
        FxStep heldStep;
        FxEntry heldEntry;
        readonly DiceLibrary dice;
        DiceBag diceBag;
        FxEntry battleEnd;
        bool battleHeld;
        // 방장 시각 판(HostPaced)으로 돌리나(확인용 — 방 판은 늘) · 내가 접은 남의 야생 배틀(그 배틀 동안 저절로 다시 열지 않는다 — 결정 120)
        bool hostPaced, wildFolded;
        // 들어가고 나오는 연출: 열 화면이 대기열의 제때(조우 창 = 공개 21행 시작 · 배틀 화면 = 전환 22행의 가운데)를 기다리는 중인가, 가운데 필드 줄무늬, 보드로 돌아오는 역전환의 시작 시각
        bool openWaiting;
        FieldWipe wipe;
        long wipeBackAt = long.MinValue;
        readonly List<string> turnLines = new List<string>();
        int turnJp;
        // 공개된 막 카드 수(막 카드에 적을 막 — 0 서막 · 1 1막 …) · 보드 위 토큰을 마지막으로 맞춘 모습(조무래기가 아직 떨어지기 전인가 · 떨어지는 중인가)
        int actsRevealed;
        int noticeAct = -1, noticeGot = -1;
        bool gruntsHeld, gruntsDropping, gruntsFlying;
        int gruntsLeaving;   // 마지막 SyncTokens가 붙잡아 둔 「해방된 마을의 조무래기 토큰」 수(94행 941)
        // 셋업 연출(04 §8-8 1~3·5행, 결정 66 ③): 화면을 맞춘 때의 셋업 단계 모습(바뀌면 다시 맞춘다) · 지금 내는 답의 종류(「고른 뒤」 단계의 꼬리표) · 받은 카드 단계를 이미 세웠나 ·
        // 숨겨진 장소 토큰이 아직 놓이기 전인가 · 떨어지는 중인가 · 게임 시작에서 카메라가 전체 줌으로 물러나 있나(첫 내 턴 컷신이 다시 데려온다) · 미끄러지기 시작한 줌
        int setupKey;
        ChoiceKind? answering;
        // 방 판(망 위): 답은 보내기만 하고 결과는 다음 갱신으로 온다 — 보낸 답의 종류를 내 SecretAnswered가 올 때까지 쥔다(「고른 뒤」 꼬리표, M7 「연출 실접속」)
        readonly Queue<ChoiceKind> sentKinds = new Queue<ChoiceKind>();
        bool pickOffered, spotsHeld, spotsDropping, wide;
        // 전체 줌으로 놓아 둔 보는 점(WideLook) — 그 자리 그대로면 HUD가 자리를 잡은 뒤·창 크기·방향이 바뀐 뒤에 다시 맞춘다(끌어 옮겼으면 두지 않는다)
        bool wideAimed;
        Vector3 wideAt;
        BoardCameraRig.Zoom camZoom;
        readonly List<BoardTokens.Token> tokenList = new List<BoardTokens.Token>();

        /// <summary>
        /// 카메라가 따라갈 점을 따라잡는 시간(ms) [가정]: 보던 점이 말에서 떨어져 있으면(남의 말이 걷기 시작할 때·내 턴 컷신) 이만큼에 걸쳐 다가가고, 그 뒤로는 말과 함께 간다.
        /// 단계가 더 짧으면 단계 길이. 화면 값이라 방장 멈춤에는 들지 않는다.
        /// </summary>
        public const int CameraCatchMs = 300;

        /// <summary>마지막으로 고른 솔로 설정(설정 화면을 다시 열면 그대로 보인다). 이어 받으면 그 판의 값이다. 판 밖에는 저장하지 않는다(실행하는 동안만).</summary>
        public SoloSettings Settings { get; private set; } = new SoloSettings { GalacticAi = SoloGame.NewGameAi, NonPlayer = SoloGame.NewGameNonPlayer, Trading = TradingReady, TrainerCards = SoloGame.NewGameTrainerCards, Undo = SoloGame.NewGameUndo };   // 솔로 설정의 처음 값: 갤럭시단 AI 「강함」(결정 118 ②) · 트레이너 카드 끔(결정 134) · 무르기 「내 턴만」(결정 154 ①)

        public LocalSession Session { get; private set; }

        /// <summary>내 좌석의 손님(보기·선택·이벤트). 게임 밖이면 null.</summary>
        public SeatClient Client { get; private set; }

        /// <summary>세션 시각(ms) — 게임을 시작한 뒤 <see cref="Tick"/>으로 흐른 시간.</summary>
        public long NowMs { get; private set; }

        /// <summary>셋업이 끝나 보드에 있다(보드 HUD가 떠 있다 — 그 위에 창이 없으면 보드 입력 켬).</summary>
        public bool OnBoard => Client != null && Client.View != null && Client.View.Phase != Phase.Setup;

        /// <summary>화면 대기열(보드에서 받은 이벤트의 연출). 게임 밖이면 null.</summary>
        public FxPlayer Fx { get; private set; }

        /// <summary>보드 기록(S21 — 이 판에서 내 좌석이 받은 이벤트의 줄). 게임 밖이면 null.</summary>
        public BoardLog Log { get; private set; }

        /// <summary>지금 따라가는 배틀(기록 줄 · 마지막 피해 계산 · 결과). 배틀 밖이면 null — 결과 창을 닫을 때 비운다.</summary>
        public BattleFeed Battle { get; private set; }

        /// <summary>
        /// 받을 화면이 없는 선택이 내 좌석에 열려 「이 선택 화면은 아직 준비 중입니다.」를 띄운 횟수(게임마다 0부터). 0이 아니면 판이 거기서 멈춘다 — 화면은 대신 답하지 않는다.
        /// </summary>
        public int NotReadyShown { get; private set; }

        /// <summary>HUD·배틀 화면이 준비 중 안내를 그렸다고 알린다.</summary>
        public void NoteNotReady() => NotReadyShown++;

        /// <summary>내 좌석이 받은 갱신의 이벤트(받은 순서 그대로). 확인용 길잡이(<c>FlowPilot</c>)가 판의 진행을 따라가는 데 쓴다.</summary>
        public event Action<IReadOnlyList<GameEvent>> EventsSeen;

        /// <summary>지금 떠 있는 보드 HUD(없으면 null).</summary>
        public BoardHud Hud => Stage.Hud as BoardHud;

        /// <summary>보드의 이동 표시(보드 없이 도는 흐름이면 null).</summary>
        public BoardMarks BoardMarks => board != null ? board.Marks : null;

        /// <summary>놓인 길의 칠하기(worker B — 감사 G5-1). 보드가 없으면 null.</summary>
        public BoardRoads BoardRoads => board != null ? board.Roads : null;

        /// <summary>
        /// 내 입력을 받아도 되나(04 §8-1 P4): 보드에 있고, 위에 창이 없고, 화면 대기열이 비어 클릭 막기 시간이 지났다.
        /// 세션은 연출 중에도 답을 받지만(솔로에는 기다릴 남이 없다) 화면이 연출이 끝날 때까지 단추·칸 누르기를 막는다.
        /// </summary>
        public bool InputOpen => BoardOpen && Stage.Current == null && Stage.Info == null && !Tutorial.DialogUp;

        /// <summary>
        /// 보드에 있고 화면 대기열이 비어 클릭 막기 시간이 지났다(위에 창이 떠 있어도). 보드 위 창 가운데 답을 내는 것(마을 창 — <see cref="VillageScreen"/>)이 단추를 켜는 데 쓴다.
        /// </summary>
        public bool BoardOpen => OnBoard && (Fx == null || Fx.Sequencer.InputOpen);

        /// <summary>
        /// 배틀 결과 창 위에도 뜨는 물음 창(균열 카드 버리기·옮기기 <see cref="BreachScreen"/> · 6장째 버리기 <see cref="FateScreen"/>)이 답 단추·카드 칸을 켜는 값: <see cref="BoardOpen"/>이거나 결과 창이 떠 있다(<see cref="BattleOver"/>).
        /// 결과 창이 [확인]을 기다리는 동안 화면 대기열은 일부러 서 있어, 그동안 남 좌석의 답이 실어 온 항목(길이 0)이 시작되지 못하고 걸린다 — 그것이 빠지기를 기다리면 단추가 영영 켜지지 않는다
        /// (플레이 시험 T22 — <see cref="Sinnoh.Client.BattleScreen.Ready"/>와 같은 규칙). 창을 여는 조건은 그대로 <see cref="BoardOpen"/>이다.
        /// </summary>
        public bool AnswerOpen => BoardOpen || (OnBoard && BattleOver);

        /// <summary>
        /// 부품이 있는 행(04 §8-8): 셋업(1~3행 — 트레이너 카드 받기·고른 뒤·공개 · 고향 마을 · 스타팅 포켓몬, <see cref="SetupRows"/>) · 막 카드(4행 — <see cref="ActCard"/>) ·
        /// 게임 시작의 숨겨진 장소 배치와 서막의 조무래기 놓기(5행 — <see cref="SpotDropBeats"/>·<see cref="TokenBeats"/>) · 걷기(11행) ·
        /// 공중날기(16행 — <see cref="FlyBeats"/>) · 숨겨진 장소 공개·지나가기(25행 — <see cref="SpotBeats"/>) · 회복(8·17행 — <see cref="HealBeats"/>) · 귀환(71행 — <see cref="WhiteoutBeats"/>) ·
        /// 턴 결과 요약 · 넘김 띠 · 내 턴 컷신(101행) · 배틀 연출(53·54·56·57·59~70·73·94행 — <see cref="BattleRows"/>) ·
        /// 들어가고 나오는 연출(찾아보기 20 · 조우 카드 21 · 조우 전환 22 · 갤럭시단 등장 72행 — <see cref="EnterRows"/>). 나머지 행의 단계는 대기열에서 덜어 낸다(<see cref="FxBuilt"/>) —
        /// 그릴 것이 없는데 입력만 막히지 않게. 남은 M4 행(결정 70 ② A2): 턴 시작 6 · 보호 해제 9 · 규토리 줍기 19 · 동시 공개 99행 — 6·99행은 좌석이 둘 이상인 판에서만 그린다(<see cref="FxBuilt.MultiOnly"/>, 결정 70 ① A1 나).
        /// </summary>
        public static bool BuiltRow(int row) => FxBuilt.Drawn(row);

        /// <summary>그 단계에 그릴 것이 있나: 부품이 있는 행(<see cref="BuiltRow"/>)이어도 그릴 것이 없는 배틀 단계는 덜어 낸다(<see cref="BattleRows.Draws"/>).</summary>
        /// <param name="seats">판의 좌석 수(0 = 모름 — 여럿으로 친다): 한 좌석 판은 여럿 판만의 행(6·99행)을 덜어 낸다.</param>
        public static bool Built(FxStep step, int seats = 0) => FxBuilt.Draws(step, seats);

        /// <summary>
        /// 좌석의 주인공(<see cref="Protagonists.Keys"/> — 공식 뒷모습 시트의 키, Gen <c>Trainers/Back/official/&lt;키&gt;.png</c>): 보드의 주인공 도트 말과
        /// 포획에서 볼을 던지는 뒷모습이 같은 인물이다(결정 53 ④·60). 솔로는 솔로 설정(S09 「주인공」)에서 고른 값이다 — 방 판은 로비에서 좌석마다 고른 값(방 상태 <see cref="RoomSeat.Protagonist"/>, M9 다듬기 P06).
        /// </summary>
        public string Protagonist(int seat)
        {
            if (seatProtagonists != null && seat >= 0 && seat < seatProtagonists.Length) return seatProtagonists[seat];
            if (roomLink != null && Room.Live != null && Protagonists.Has(Room.Live.ProtagonistOf(seat))) return Room.Live.ProtagonistOf(seat);
            return Protagonists.Has(Settings.Protagonist) ? Settings.Protagonist : Protagonists.Default;
        }

        // 좌석마다의 주인공(좌석 여럿인 확인용 판 — StartSeats). null = 모든 좌석이 솔로 설정의 주인공
        string[] seatProtagonists;

        /// <summary>보드의 말(보드 없이 도는 흐름이면 null).</summary>
        public BoardPieces BoardPieces => board != null ? board.Pieces : null;

        /// <summary>
        /// 묶음 표지(한 칸에 말 여럿 — 결정 66 ①) 안의 그림이 돌아가는 시각(ms): 기본은 세션 시각(<see cref="NowMs"/>). 확인용(캡처·시험)은 값을 넣어 고정한다(음수 = 다시 세션 시각).
        /// </summary>
        public long StackMs
        {
            get => stackMs >= 0 ? stackMs : NowMs;
            set => stackMs = value;
        }

        long stackMs = -1;

        /// <summary>지금 카메라의 줌 단계(전체 1 · 지역 2 · 근접 3 — <see cref="BoardPieces.ZoomK"/>). 카메라 없이 도는 흐름이면 지역.</summary>
        public int ZoomStep => rig != null ? BoardPieces.ZoomK(rig.CurrentZoom) : 2;

        /// <summary>
        /// 말 그림의 양식(S02 설정 「말 그림」, 04 §3-1): 보는 사람의 표시 설정이라 판·세션과 무관하다. 기본은 주인공 도트 [가정]. 바꾸면 보드의 말을 그 양식으로 다시 세운다.
        /// 음량과 같은 곳에 저장한다(<see cref="SoundPrefs.Piece"/> — 결정 70 ⑤, 플레이어는 PlayerPrefs · 에디터·확인용은 실행하는 동안만). 보는 사람의 값이라 판의 세이브에는 넣지 않는다.
        /// </summary>
        public PieceStyle PieceStyle
        {
            get => SoundPrefs.Piece;
            set
            {
                SoundPrefs.Piece = value;
                if (board == null) return;
                board.PieceStyle = SoundPrefs.Piece;
                // 게임 중(게임 메뉴의 설정 — 결정 70 ⑨): 보드는 마지막에 세운 칸으로 말을 다시 세우니 지금 칸으로 옮긴다
                if (gameSeats && Client != null && Client.View != null && board.Pieces != null) Arrange();
            }
        }

        /// <summary>가운데 필드의 전환 줄무늬가 지금 덮은 만큼(0 ~ 1 — 조우 전환 22행 · 결과 [확인] 뒤의 역전환). 확인용.</summary>
        public float WipeCover => wipe != null ? wipe.Cover : 0f;

        /// <summary>전환 줄무늬의 무늬 갈래(상대에 따라 — M9 다듬기 P14) · 전설 전환의 빛(0 ~ 1). 확인용.</summary>
        public WipeStyle WipeStyle => wipe != null ? wipe.Style : WipeStyle.Wild;
        public float WipeLight => wipe != null ? wipe.Light : 0f;

        /// <summary>
        /// 배틀이 끝났고 그 끝까지 화면에 다 보였다(끝난 갱신의 연출이 다 돌았다) — 결과 창이 뜨는 때. 그때 화면 대기열을 세워 둔다:
        /// 같은 갱신에 이어 오는 턴 넘김(요약 → 띠 → 컷신)은 [확인](<see cref="CloseBattle"/>) 뒤에 이어진다.
        /// </summary>
        public bool BattleOver => Battle != null && Battle.Ended && battleHeld;

        /// <summary>
        /// 화면 대기열이 방장 시각을 따르는 판인가(방 판은 늘 — 확인용 판은 값을 넣는다, 플레이 시험 T4 · 결정 126): 결과 창 · 게임 메뉴가 떠 있어도 대기열을 세우지 않고,
        /// 늦으면 방장 멈춤 끝에 맞춰 따라잡는다(<see cref="FxSequencer.CatchUpMs"/>). 남의 배틀 결과 창은 그 뒤 연출이 시작되면 저절로 닫힌다(내가 [확인]을 누르지 않아도 된다 — T8 · T16).
        /// 혼자 하는 판은 지금처럼 결과 창이 [확인]까지 대기열을 세운다(기다릴 남이 없다).
        /// </summary>
        public bool HostPaced
        {
            get => roomLink != null || hostPaced;
            set => hostPaced = value;
        }

        /// <summary>늦은 만큼이 이 값(ms)을 넘으면 따라잡는다 [가정 plan §5 #620]: 방장 멈춤과 내 대기열은 같은 길이 표에서 나오고 화면만 그리는 짧은 항목(받은 카드 800 · 프레임 · 지연)만큼은 늘 늦을 수 있다.</summary>
        public const long CatchUpFromMs = 1500;

        /// <summary>내 창(정비 · 트레이너 · 운명의 변화 · 기록 펼치기 …)이 떠 있다 — 그동안 남의 행동은 하던 일을 덮지 않는다(결정 120 · 22 Q6). 배틀 화면 자체는 창이 아니다.</summary>
        public bool OwnWindowUp => Top != null && !(Top is BattleScreen);

        /// <summary>
        /// 남의 야생 배틀(전설 · 우두머리 · 무리 · UB 포함 — 결정 114)을 뒤에서 그리는 중인가: 방 판의 화면 대기열은 끝까지 그리지만(결정 112 Q19 — 끝나는 시각을 방장과 맞춤)
        /// 가운데 필드를 저절로 바꾸지 않는다(결정 22 Q6 — 보드 · 하던 작업 유지). 보려면 <see cref="OpenWildWatch"/>(방 판 덮개의 「○○ 야생 배틀 중」 [보기]).
        /// </summary>
        public bool WildAside => Battle != null && planner != null && planner.WatchWild && Battle.Started && !Battle.Mine && FxRows.WildLike(Battle.Kind);

        /// <summary>남의 야생 배틀도 화면 대기열에 넣는가(방 판은 늘 켬 — <see cref="FxPlanner.WatchWild"/>). 확인용(캡처): 망 없는 판에서 켠다.</summary>
        public bool WatchWildBattles
        {
            get => planner != null && planner.WatchWild;
            set { if (planner != null) planner.WatchWild = value; }
        }

        /// <summary>뒤에서 그리는 남의 야생 배틀을 가운데 필드로 열어 보고 있나(결정 114).</summary>
        public bool WatchingWild { get; private set; }

        /// <summary>그 야생 배틀에서 싸우는 좌석(아래쪽 주인 — 없으면 −1).</summary>
        public int WildSeat
        {
            get
            {
                var b = Battle != null && Battle.Source != null ? Battle.Source.Battle : null;
                return WildAside && b != null ? b.Sides[Battle.MySide].Owner : -1;
            }
        }

        /// <summary>
        /// 「○○ 야생 배틀 중」 [보기]: 그 배틀 화면을 가운데 필드로 연다 — 내 창이 떠 있으면 먼저 닫는다(닫을 수 있는 창만 — Esc와 같은 닫기, 결정 120). 메뉴가 떠 있으면 열지 않는다. 열었으면 true.
        /// 창이 없으면 배틀은 저절로 열린다(<see cref="WildAutoOpens"/>) — [보기]는 창을 열어 둔 동안 · 내가 접은 뒤에 쓴다.
        /// </summary>
        public bool OpenWildWatch()
        {
            if (!WildAside || WatchingWild || MenuUp) return false;   // 규칙 쪽에서 이미 끝났어도 화면이 아직 그리는 중이면 연다(다 그리면 DropWildAside가 놓는다)
            for (int i = 0; i < 4 && Stage.Current != null; i++)
                if (!Escape()) break;
            if (Stage.Current != null) return false;   // 닫지 못하는 창(답해야 하는 창)
            WatchingWild = true;
            wildFolded = false;
            Sync();
            return true;
        }

        /// <summary>
        /// 남의 야생 배틀을 지금 저절로 열 때인가(결정 120 — 결정 114의 「저절로 바꾸지 않는다」를 고침): 내 창이 없고 메뉴가 닫혀 있고 내가 이 배틀을 접지 않았다.
        /// 창을 열어 둔 동안은 「○○ 야생 배틀 중」 단추만 뜨고(하던 작업 유지 — 결정 22 Q6), 창을 닫으면 그때 열린다.
        /// </summary>
        public bool WildAutoOpens => WildAside && !WatchingWild && !wildFolded && Stage.Current == null && !MenuUp;

        /// <summary>내가 접은 남의 야생 배틀인가(그 배틀이 끝날 때까지 저절로 다시 열지 않는다).</summary>
        public bool WildFolded => WildAside && wildFolded;

        /// <summary>[보드로] · Esc: 보던 야생 배틀 화면을 접고 보드로(배틀은 뒤에서 계속 그린다 — 그 배틀 동안 저절로 다시 열지 않는다). 결과 창이 떠 있으면 그 [확인]과 같다. 걷었으면 true.</summary>
        public bool CloseWildWatch()
        {
            if (!WatchingWild) return false;
            if (BattleOver)
            {
                CloseBattle();
                return true;
            }
            WatchingWild = false;
            wildFolded = true;
            if (Stage.Current is BattleScreen) Stage.Shut();
            Restage();
            Hud?.Refresh();
            return true;
        }

        // 뒤에서 그린 남의 야생 배틀이 끝까지 다 보였다(보지 않는 중): 결과 창 없이 놓는다 — 화면 대기열을 세우지 않는다(방장 멈춤과 같은 때에 끝난다)
        void DropWildAside()
        {
            Battle = null;
            battleEnd = null;
            battleHeld = false;
            WatchingWild = false;
            wildFolded = false;
            Hud?.Refresh();
        }

        /// <summary>
        /// 배틀 도중(끝나기 전)에 보드 칸을 고르는 카드 물음이 내게 열려 있다(team_rocket R 「travel to any tile」 — 배틀을 닫는 단계, rulings A35). 그동안 배틀 화면을 걷고 보드 HUD가 칸 칠하기로 받는다(<see cref="BoardHud.CardCells"/>).
        /// </summary>
        public bool BoardPickInBattle
        {
            get
            {
                var c = Battle != null && !Battle.Ended && Client != null ? Client.FirstChoice() : null;
                return c != null && BoardHud.BoardCardAsk(c) && c.Sub == ChoiceSub.EffectPick && CardAsk.KindOf(Ctx, c) == CardAsk.Value.Cell;
            }
        }

        /// <param name="ctx">엔진 정적 데이터. null이면 「혼자 하기」가 켜지지 않는다.</param>
        /// <param name="board">같은 씬의 보드(없으면 null — 화면만 돈다).</param>
        /// <param name="entropy">게임 시드·AI 시드를 주는 곳. 기본은 시각과 Guid를 섞은 값(엔진 밖 — 엔진은 받은 시드만 쓴다).</param>
        /// <param name="window">설정 화면이 창 크기를 읽고 바꾸는 곳. 기본은 엔진의 창(캡처·시험은 고정 값).</param>
        /// <param name="dice">주사위 녹화본 묶음(<c>Data/Dice/rolls.bytes</c>). null이면 굴림판에 주사위가 구르지 않고 결과 줄만 선다.</param>
        /// <param name="saves">저장 폴더(그 아래 폴더 하나 = 저장 하나 — 플레이어는 <c>persistentDataPath/saves</c>, <see cref="ScreenBoot"/>가 넘긴다). null이면 저장하지 않는다(캡처·시험의 기본).</param>
        /// <param name="clock">저장 시각을 주는 시계(UTC, 1970년부터의 ms). 기본은 시스템 시계 — 캡처·시험은 고정 값.</param>
        /// <param name="contentHash">게임 데이터의 묶음 해시(세이브 머리의 ContentHash, 01 §10). 기본은 보드의 것(<see cref="BoardBoot.ContentHash"/>) — 보드 없이 도는 흐름은 null(해시를 적지도 견주지도 않는다).</param>
        /// <param name="sound">음량 설정(S02 「배경음악」·「효과음」). 기본은 실행하는 동안만 쥐는 값(캡처·시험) — 플레이어는 <see cref="ScreenBoot"/>가 저장하는 것(<see cref="StoredSoundPrefs"/>)을 넘긴다.</param>
        public GameFlow(UiStage stage, Ctx ctx, BoardBoot board = null, BoardCameraRig rig = null, Func<ulong> entropy = null, Action quit = null, bool? web = null, WindowControl window = null,
            DiceLibrary dice = null, string saves = null, Func<long> clock = null, string contentHash = null, SoundPrefs sound = null)
        {
            SoundPrefs = sound ?? new SoundPrefs();
            this.saves = string.IsNullOrEmpty(saves) ? null : saves;
            this.clock = clock ?? SystemClock;
            this.contentHash = contentHash ?? (board != null ? board.ContentHash : null);
            this.dice = dice;
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Stage.TapSink = PlaySound;   // 내 클릭의 즉시 소리(04 §8-4) — 화면 단추·누를 칸이 무대에 알린다(받는 흐름은 하나 — 마지막에 만든 것)
            Ctx = ctx;
            this.board = board;
            this.rig = rig;
            this.entropy = entropy ?? SystemEntropy;
            this.quit = quit;
            this.web = web;
            this.window = window ?? new WindowControl();
            Tutorial = new TutorialMenu(this);
            Room = new RoomFlow(this);
            if (SoundPrefs.Lang != null) Stage.UseLang(SoundPrefs.Lang);   // 저장된 언어(S02 「언어」 — 다음 실행에도 남는다)
        }

        /// <summary>
        /// S02 「언어」(M9 언어 ② — 결정 111 ② · plan §5 #304): 내 화면 글의 언어(<c>ko</c> · <c>en</c>) — 화면 글 · 기록 · 배틀 DS 글 · 룰 설명서가 이 값 하나를 읽는다.
        /// 바꾸면 무대의 표를 바꾸고 열린 판 · 화면을 다시 만든다(<see cref="UiStage.UseLang"/>), 무대 밖 층(튜토리얼 말풍선 · 방 층)은 다음 그림에서 새 글로.
        /// 음량과 같은 곳에 저장한다(<see cref="SoundPrefs.Lang"/>). 판의 세이브 · 방 통신에는 넣지 않는다 — 엔진은 키만 보내고 좌석마다 자기 언어로 그린다(결정 34).
        /// 표가 없는 언어(무대에 <see cref="UiStage.Tables"/>가 없다)는 받지 않는다.
        /// </summary>
        public string Lang
        {
            get => Stage.Lang;
            set
            {
                if (!Stage.UseLang(value)) return;
                SoundPrefs.Lang = value;
                roomLayer?.Relabel();
                Tutorial.Layer?.Relabel();
                Hud?.Refresh();
            }
        }

        // ── 소리(결정 66 ④, 04 §8-4) ──

        /// <summary>음량 설정(S02). 재생 층(<see cref="SoundPlayer"/>)이 프레임마다 읽는다.</summary>
        public SoundPrefs SoundPrefs { get; }

        /// <summary>
        /// 소리를 내라는 부름: 화면 대기열의 소리 단서(연출 시각에 닿은 것 — <see cref="FxSequencer.Sound"/>, 다시 보기는 대기열이 막는다)와 즉시 소리(<see cref="PlaySound"/> — 내 클릭).
        /// 재생 층이 받는다(없으면 아무 일도 없다 — 캡처·시험).
        /// </summary>
        public event Action<SoundCall> Sound;

        /// <summary>즉시 소리 하나(내 클릭의 고르기·확정 같은 것, 04 §8-4 「즉시 소리와 연출 소리를 나눈다」). 키는 <see cref="SoundKeys"/>.</summary>
        public void PlaySound(string key)
        {
            if (!string.IsNullOrEmpty(key)) Sound?.Invoke(new SoundCall { Key = key, Immediate = true, Seq = -1, AtMs = 0, CueMs = Fx != null ? Fx.Sequencer.NowMs : 0 });
        }

        void OnCue(FxCue cue)
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            // 남 좌석의 해금 · 바꾸기 덮개가 내 창 · 메뉴 뒤로 숨었으면 그 소리도 내지 않는다(결정 120 ① · #709)
            if (seq != null && seq.Current != null && seq.Current.Seq == seq.CueSeq && !ShowsOthers && CardRows.OthersCover(seq.Current, cue, Client != null ? Client.Seat : -1)) return;
            Sound?.Invoke(new SoundCall { Key = cue.Sound, Seq = seq != null ? seq.CueSeq : -1, AtMs = cue.AtMs, Row = cue.Row, CueMs = seq != null ? seq.CueMs : 0 });
        }

        /// <summary>
        /// S02 「연출」(04 §8-1 P17 · §8-4): 내 화면의 재생 속도 — 보통 · 빠름 · 끄기(<see cref="Sinnoh.Fx.FxSpeed"/>). 음량과 같은 곳에 둔다(<see cref="SoundPrefs"/>).
        /// 방장 멈춤(<c>PlayTiming</c>)은 그대로다. 바꾸면 하던 판의 화면 대기열에도 바로 걸린다.
        /// </summary>
        public FxSpeed FxSpeed
        {
            get => SoundPrefs.Fx;
            set
            {
                SoundPrefs.Fx = value;
                if (Fx != null) Fx.Sequencer.Speed = SoundPrefs.Fx;
            }
        }

        /// <summary>
        /// 확인용(단서 시각 로그 — <c>ScreenProbe</c>·플레이 시험): <see cref="Skip"/>으로 건너뛰는 동안에도 소리 단서를 낸다. 기본은 끔 — 건너뛰기는 무음이다(04 §8-4 「건너뛰기·다시 보기는 무음」).
        /// 켜도 단서의 시각(<see cref="SoundCall.CueMs"/>)은 잘게 보낸 것과 같다(시퀀서가 항목 시작 + 항목 안 시각으로 센다).
        /// </summary>
        public bool SoundOnSkip { get; set; }

        MusicScene boardMusic;
        bool boardMusicSet;

        /// <summary>
        /// 지금 화면의 곡 상황(결정 66 ④ [가정 — plan §5 #52]): 게임 밖(타이틀·설정·혼자 하기·저장 목록) = 타이틀 · 방 로비(방 만들기·참가·로비·이어하기 대기실) = 로비(결정 130) · 셋업 = 게임 준비 · 게임 끝 · 배틀 화면 = 배틀 종류의 곡(결과 창이 뜨면 결과) ·
        /// 조우 창 · 그 밖(보드와 그 위 창 — 게임 메뉴·정비·마을·기록)은 보드의 곡 = 막 카드가 보인 막 + 내 말이 선 칸의 기후(<see cref="MusicScenes.Board"/>).
        /// 보드의 곡은 화면 대기열이 비었을 때만 다시 고른다 — 걷는 동안·막 카드가 내려앉기 전에는 그 앞 곡 그대로(보기가 아니라 보인 것을 따른다, 04 §8-3).
        /// 판에 들어와 처음 고르기 전에 대기열이 차 있으면(게임 시작 연출) 서막 곡이다.
        /// </summary>
        public MusicScene Music()
        {
            var view = Client != null ? Client.View : null;
            // 사용자 결정 130: 방 로비(방 만들기 · 참가 · 로비 · 이어하기 대기실)는 로비 곡(마박사 소개), 그 밖의 게임 밖 화면(타이틀 · 설정 · 혼자 하기 · 저장 목록 [가정 §5 #666])은 타이틀 곡
            if (view == null) return Stage.Current is LobbyScreen || Stage.Current is RoomCreateScreen || Stage.Current is RoomJoinScreen ? MusicScene.Lobby : MusicScene.Title;
            if (view.Phase == Phase.Setup) return MusicScene.Setup;
            if (view.Phase == Phase.GameOver) return MusicScene.GameOver;
            if (Stage.Current is BattleScreen)
            {
                var bgm = BattleRules.BgmOf(Battle != null ? Battle.Kind : BattleKind.Wild);
                return BattleOver ? MusicScenes.Result(bgm) : MusicScenes.Battle(bgm);
            }
            if (Stage.Current is EncounterScreen || (Stage.Current is CraftScreen && Client.Choice(ChoiceKind.EncounterPick) != null)) return MusicScene.Encounter;   // 조우 창 위의 제작 창(21행 [제작])도 조우 곡
            // 처음 고르기 전에 대기열이 차 있으면(게임 시작 연출 — 서막 카드 · 조무래기 놓기 · 1막 카드) 서막 곡: 셋업 곡이 끊기지 않고 이어진다
            if (!boardMusicSet && Fx != null && !Fx.Sequencer.IsEmpty) return MusicScene.Prologue;
            if (!boardMusicSet || Fx == null || Fx.Sequencer.IsEmpty)
            {
                int act = view.Hos != null ? ActBeats.ShownAct(view.Hos.Act, Fx != null ? Fx.Sequencer : null) : 0;
                int seat = Client.Seat, cell = seat >= 0 && seat < view.Seats.Length ? view.Seats[seat].Cell : -1;
                var climate = Ctx != null && cell >= 0 && cell < Ctx.Board.Count ? Ctx.Board[cell].Climate : Climate.None;
                boardMusic = MusicScenes.Board(act, climate);
                boardMusicSet = true;
            }
            return boardMusic;
        }

        static ulong SystemEntropy()
        {
            var g = Guid.NewGuid().ToByteArray();
            return BitConverter.ToUInt64(g, 0) ^ BitConverter.ToUInt64(g, 8) ^ (ulong)DateTime.UtcNow.Ticks;
        }

        static long SystemClock() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ── 화면 ──

        /// <summary>
        /// 타이틀을 연다. 하던 게임이 있으면 떠난다(저장하는 판은 그때까지가 이미 저장돼 있다 — 명령마다 쓴다).
        /// 저장 목록을 한 번 읽어 「이어하기」 줄을 정한다(04 §9-12): 저장이 하나라도 있으면 켜고(읽을 수 없는 것뿐이어도 — 목록에서 지울 수 있게) 그 아래에 가장 최근 판의 요약을 적는다.
        /// </summary>
        /// <param name="pick">처음 고른 줄. null이면 저장이 있을 때 「이어하기」, 없으면 「혼자 하기」 [가정].</param>
        public void OpenTitle(string pick = null)
        {
            // 참가 화면 · 로비에서 들어간 튜토리얼(결정 38 — RoomFlow.OpenTutorial)의 [타이틀로] · 목차 [뒤로]는 들어온 곳으로 돌아간다
            if (Room.BackFromTutorial(pick == TitleScreen.Tutorial)) return;
            Leave();
            var list = Ctx != null ? Saves() : new List<SaveEntry>();
            if (Ctx != null)
            {
                list.AddRange(Room.Saves());   // 방 저장도 「이어하기」를 켠다(M7 화면 ② — S04에 함께 보인다)
                SaveStore.Sort(list);
            }
            var latest = list.Find(CanResume);
            string line = latest != null ? SaveLine(latest.Head) : list.Count > 0 ? Stage.T("screen.title.continue.unreadable") : null;
            Stage.Open(new TitleScreen(quit, web, pick ?? (list.Count > 0 ? TitleScreen.Continue : TitleScreen.Solo), Ctx != null ? OpenSolo : (Action)null, OpenSettings,
                list.Count > 0 ? OpenSaves : (Action)null, line, Tutorial.Available ? Tutorial.Open : (Action)null, Tutorial.FirstTime,
                Ctx != null ? Room.OpenCreate : (Action)null, Ctx != null ? Room.OpenJoin : (Action)null, Ctx != null ? OpenTitleDex : (Action)null,
                rulebook: OpenTitleRulebook, credits: OpenCredits, notice: PlayNoticeDue ? AnswerPlayNotice : (Action<bool>)null,
                update: NewVersion, download: NewVersion != null ? DownloadRelease : (Action)null));
            Restage();
        }

        // ── 튜토리얼(M6.5 「화면 ② 목차·입구」, 07 §2) ──

        /// <summary>튜토리얼의 입구·목차·장 고르기·건너뛰기·끝내기 창·끝 창·기록(<see cref="TutorialMenu"/>).</summary>
        public TutorialMenu Tutorial { get; }

        /// <summary>튜토리얼 목차를 연다(타이틀 [튜토리얼] — 기록이 있을 때 · 끝내기 창·끝 창의 [목차로]). 하던 게임은 떠난다. [뒤로]는 타이틀의 「튜토리얼」 줄로.</summary>
        public void OpenTutorialMenu()
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 튜토리얼을 열지 못한다");
            Leave();
            Stage.Open(new TutorialMenuScreen(this));
            Restage();
        }

        /// <summary>튜토리얼 끝내기 창·끝 창이 떴다: 화면 대기열을 세운다(게임 메뉴와 같다).</summary>
        internal void PauseForDialog()
        {
            if (Fx != null) Fx.SetPaused(true);
            Restage();
            Hud?.Refresh();
        }

        /// <summary>튜토리얼 끝내기 창의 [계속하기]: 화면 대기열을 다시 돌리고, 창이 떠 있는 동안 열 때가 지난 화면이 있으면 지금 연다(<see cref="CloseMenu"/>와 같다).</summary>
        internal void ResumeAfterDialog()
        {
            if (Fx != null) Fx.SetPaused(false);
            if (Session != null) Sync();
        }

        /// <summary>저장 목록(S04)을 연다(타이틀 「이어하기」). 하던 게임이 있으면 떠난다. [돌아가기]는 타이틀의 「이어하기」 줄로.</summary>
        public void OpenSaves()
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 저장 목록을 열지 못한다");
            Leave();
            Stage.Open(new SaveListScreen(this));
            Restage();
        }

        /// <summary>저장 한 줄의 요약(타이틀 「이어하기」 아래 · 저장 목록): 「신오의 심장 · 1막 · 3라운드」. 셋업 가운데면 「신오의 심장 · 게임 준비」.</summary>
        public string SaveLine(SaveHead head)
        {
            const string dot = " · ";
            string campaign = Stage.T(head.Campaign == Campaign.UltraBurst ? "screen.solo.campaign.ub" : "screen.solo.campaign.hos");
            var sum = head.Summary;
            if (sum.Phase == Phase.Setup) return campaign + dot + Stage.T("screen.log.setup");
            string text = campaign;
            if (sum.Act >= 0) text += dot + Stage.T(BoardLog.ActKey(Math.Min(sum.Act, HosState.Acts - 1)));
            return text + dot + Stage.T("screen.log.round").Replace("{n}", sum.Round.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 저장 시각(UTC ms)을 화면 글로: 현지 시각 「2026-11-13 09:00」(숫자뿐이라 문자열 표에 두지 않는다). 0이면 빈 글.
        /// 날짜로 바꿀 수 없는 값(남이 고친 머리 — 9999년을 넘는 수)도 빈 글이다(던지지 않는다 — 저장 목록이 그 칸 때문에 열리지 않으면 지울 수도 없다).
        /// </summary>
        public static string SaveTime(long utcMs, bool date = true)
        {
            if (utcMs <= 0) return "";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(utcMs).ToLocalTime().ToString(date ? "yyyy-MM-dd HH:mm" : "HH:mm", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "";
            }
        }

        /// <summary>
        /// 지금 나가면 이어 받을 때 앞으로 돌아가나(04 §9-11 — 이어 받기는 마지막 쉬는 때로): 배틀 · 조우 고르기 · 배틀 뒤 선택의 한가운데다. 게임 메뉴의 나가기 안내가 쓴다.
        /// </summary>
        public bool LeaveRewinds => OnBoard && (Client.View.Battle != null || Client.Choice(ChoiceKind.EncounterPick) != null || Client.Choice(ChoiceKind.Reward) != null);

        /// <summary>설정(S02)을 연다(타이틀에서). [뒤로]는 타이틀의 「설정」 줄로 돌아간다.</summary>
        public void OpenSettings()
        {
            Leave();
            Stage.Open(new SettingsScreen(window, () => OpenTitle(TitleScreen.Settings), this));
            Restage();
        }

        /// <summary>솔로 설정(S09)을 연다. 하던 게임이 있으면 떠난다.</summary>
        public void OpenSolo()
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 혼자 하기를 열지 못한다");
            Leave();
            Stage.Open(new SoloScreen(this));
            Restage();
        }

        /// <summary>고른 설정으로 솔로 게임을 시작한다(시드는 <c>entropy</c>에서).</summary>
        public void StartSolo(SoloSettings settings) => StartSolo(settings, entropy(), entropy());

        /// <summary>고른 설정·시드로 솔로 게임을 시작한다: 세션을 만들고 셋업의 첫 선택 화면을 연다.</summary>
        /// <param name="prepare">확인용(캡처·시험·플레이어 확인)의 손본 판 — <see cref="SoloGame.Start"/>의 같은 인자. 게임 화면은 넘기지 않는다.</param>
        public void StartSolo(SoloSettings settings, ulong seed, ulong aiSeed, Action<GameState> prepare = null)
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 게임을 시작하지 못한다");
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Leave();
            Settings = settings.Clone();
            startedSeed = seed;
            startedAiSeed = aiSeed;
            // 저장: 혼자 하기는 자동 저장 한 칸(SoloSlot — 결정 70 ③). 새 판을 시작하면 그 칸을 덮는다(그 폴더만 지우고 새로 만든다 — 다른 이름의 저장은 건드리지 않는다).
            // 손본 판(확인용)은 저장하지 않는다. 폴더를 지우거나 만들지 못하면 저장 없이 시작하고 상태로 알린다
            ISaveStore store = null;
            string id = null;
            if (saves != null && prepare == null)
            {
                id = SoloSlot;
                try
                {
                    string dir = SaveStore.PathOf(saves, id);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    store = new SaveStore(dir);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    id = null;
                    SaveError = e.Message;
                }
            }
            saveId = id;
            Enter(SoloGame.Start(Ctx, Settings, seed, aiSeed, StrongAgent.Virtual(), 0, store, id, contentHash, prepare, clock, soloAi = SoloRunner(Settings.GalacticAi)), seed, false);
        }

        // ── 솔로 AI(M8 「연결」) ──
        // 가상 응답자 = 원칙 + 강한 AI(StrongAgent.Virtual — 세기는 SoloGame이 설정 · 머리에서 맞춘다). 「강함」 판은 생각을 작업 스레드(AgentWorker)에서 한다:
        // 화면이 멈추지 않고, 답은 정해진 시각(앞 연출이 끝난 때)으로 들어가 생각 시간이 연출 뒤에 숨는다(02 §5-7). 원칙 판은 그대로 이 스레드에서 바로 묻는다

        /// <summary>확인용(캡처): false면 「강함」 판도 AI를 이 스레드에서 바로 묻는다(작업 스레드 · 시간 마감 없음 — 같은 판이면 같은 그림). 게임은 true.</summary>
        public bool AiThread = true;

        static readonly System.Diagnostics.Stopwatch aiWatch = System.Diagnostics.Stopwatch.StartNew();
        AgentWorker soloAi;

        AgentWorker SoloRunner(GalacticAiLevel level) => AiThread && level == GalacticAiLevel.Strong ? new AgentWorker(() => aiWatch.ElapsedMilliseconds, "sinnoh-solo-ai") : null;

        /// <summary>
        /// 확인용(캡처·시험·플레이어 확인): 좌석이 여럿인 판을 연다 — 여럿이 하는 판(로비·좌석 연결, M7)이 생기기 전에 한 칸에 말 여럿(결정 66 ①)을 보는 데 쓴다. 게임 화면은 부르지 않는다.
        /// 셋업은 좌석마다 엔진의 첫 합법 답으로 끝내고, 그 상태를 <paramref name="prepare"/>로 손본 뒤(말을 한 칸에 모은다) 시작한다. 이 흐름은 좌석 0만 몬다 —
        /// 다른 좌석은 사람 좌석이라 아무도 답하지 않는다(그 좌석의 턴에서 판이 선다). 저장하지 않는다. 주인공은 좌석마다 목록의 다음 인물.
        /// </summary>
        /// <param name="liveSetup">true면 셋업을 대신 끝내지 않고 셋업부터 이 흐름이 본다(동시 공개 99행 확인 — 내 좌석은 화면으로, 다른 좌석은 <see cref="AnswerSeat"/>로 답한다). prepare는 쓰지 않는다.</param>
        public void StartSeats(SoloSettings settings, int seats, ulong seed, ulong aiSeed, Action<GameState> prepare = null, bool liveSetup = false, int[] clockMs = null)
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 게임을 시작하지 못한다");
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (seats < 1 || seats > Sinnoh.Data.PieceLayout.MaxSeats) throw new ArgumentOutOfRangeException(nameof(seats), seats, "1~6");
            Leave();
            if (Stage.Current != null) Stage.Shut();   // 떠 있던 화면(타이틀)을 닫는다 — 보드에는 HUD만 뜬다
            Settings = settings.Clone();
            startedSeed = seed;
            startedAiSeed = aiSeed;
            saveId = null;
            var who = new string[seats];
            int first = Math.Max(0, Array.IndexOf(Protagonists.Keys, Settings.Protagonist));
            for (int i = 0; i < seats; i++) who[i] = Protagonists.Keys[(first + i) % Protagonists.Keys.Length];
            var cfg = new SessionConfig { VirtualAgent = new PrincipleAgent(), Ai = Settings.ToAi(aiSeed), Protagonists = who, ClockMs = clockMs, Undo = Settings.Undo };
            Clocks = clockMs;
            var setup = LocalSession.Start(Ctx, Settings.ToOptions(), seats, seed, Settings.Campaign, cfg, 0);
            if (liveSetup)
            {
                seatProtagonists = who;
                others = null;
                Enter(setup, seed, false);
                return;
            }
            var clients = new SeatClient[seats];
            for (int i = 0; i < seats; i++) clients[i] = new SeatClient(setup.Link, Ctx, i);
            for (int guard = 0; clients[0].View.Phase == Phase.Setup; guard++)
            {
                bool any = false;
                foreach (var c in clients)
                {
                    var choice = c.FirstChoice();
                    if (choice == null) continue;
                    var legal = c.Legal(choice);
                    if (legal.Count > 0 && c.Answer(choice, legal[0], 0).Accepted) any = true;
                }
                if (!any || guard > 200) throw new InvalidOperationException("좌석 여럿인 판의 셋업이 끝나지 않는다");
            }
            var state = setup.HostCopy();
            prepare?.Invoke(state);
            seatProtagonists = who;
            Enter(LocalSession.FromState(Ctx, state, cfg, 0), seed, false);
        }

        /// <summary>
        /// 확인용(캡처·시험·플레이어 확인 — 찾아 둔 판의 막 길 <c>SoloTrail.After</c>): 지금 판(셋업이 끝나 첫 맵 행동이 열린 판)의 호스트 사본을 손봐 그 상태로 다시 연다.
        /// 저장하지 않는다. AI 시드는 이 판을 시작한 값 그대로(<c>SoloPilotTests</c>의 셋업 뒤 손보기와 같은 꼴 — 같은 길로 지나면 같은 판이다). 게임 화면은 부르지 않는다.
        /// </summary>
        public void Rebase(Action<GameState> prepare)
        {
            if (Session == null) throw new InvalidOperationException("다시 열 판이 없다");
            if (prepare == null) throw new ArgumentNullException(nameof(prepare));
            var state = Session.HostCopy();
            prepare(state);
            var who = seatProtagonists;
            Leave();
            if (Stage.Current != null) Stage.Shut();
            seatProtagonists = who;
            var cfg = new SessionConfig { VirtualAgent = new PrincipleAgent(), Ai = Settings.ToAi(startedAiSeed), Protagonists = who ?? new[] { Settings.Protagonist } };
            Enter(LocalSession.FromState(Ctx, state, cfg, 0), startedSeed, false);
        }

        /// <summary>
        /// 각본 튜토리얼(M6.5 — 07 §9-1 · T61): 진행기(<c>TutorialRunner.Hand</c>)가 연 세션(정해진 판 · 빨리감기로 보낸 국면 · 막 앞 판으로 다시 연 판)에 들어가 내 좌석 손님을 돌려준다 —
        /// 진행기가 이 손님을 같이 쓴다(링크는 좌석마다 손님 하나). 하던 게임은 떠난다. 저장하지 않는다(결정 90 Q6 — 저장하는 세션은 거절). 세션 시각은 <paramref name="nowMs"/>부터 잇는다.
        /// 셋업 가운데면 셋업 화면, 보드면 보드 HUD가 열린다(빨리감기로 넘어온 판은 이어 받은 판처럼 보기에서 세운다 — 앞 장의 연출은 틀지 않는다). 주인공은 마지막으로 고른 솔로 설정의 인물 —
        /// <paramref name="protagonists"/>를 주면 좌석마다 그 인물(여럿 판의 봇 좌석 = 용식 · 빛나, 결정 96 Q5 나 — <see cref="Protagonists.ForTutorial"/>).
        /// </summary>
        public SeatClient Adopt(LocalSession session, ulong seed, long nowMs = 0, string[] protagonists = null)
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 튜토리얼을 시작하지 못한다");
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Saving) throw new ArgumentException("튜토리얼 판은 저장하지 않는다", nameof(session));
            Leave();
            if (Stage.Current != null) Stage.Shut();
            startedSeed = seed;
            startedAiSeed = 0;
            seatProtagonists = protagonists;
            Enter(session, seed, false);
            NowMs = Math.Max(0, nowMs);
            return Client;
        }

        // ── 방(M7 화면 ① — 02 §3-1, 04 S05~S08) ──

        /// <summary>방 만들기(S05) · 참가하기(S06) · 로비와 방 설정(S07·S08) · 방 판에 들어가기(<see cref="RoomFlow"/>).</summary>
        public RoomFlow Room { get; }

        /// <summary>방 판(망 위의 내 좌석)에 들어가 있다 — 세션은 방장 PC의 방에 있고 이 흐름은 내 좌석의 보기만 받는다(<see cref="Session"/> = null).</summary>
        public bool InRoomGame => roomLink != null;

        ISeatLink roomLink;
        int roomSeats;
        bool keepRoom;

        /// <summary>
        /// 방 판에 들어간다(방이 게임을 시작해 내 좌석의 첫 보기가 왔다 — <see cref="RoomFlow"/>가 부른다): 로비 화면을 닫고 내 좌석의 손님을 링크에 붙여 셋업 화면 · 보드를 연다.
        /// 세션은 방장 PC의 방이다(시각·저장·AI는 방이 센다) — 이 흐름은 화면 시계만 흘린다. 방은 닫지 않는다. 주인공은 좌석마다 로비에서 고른 인물(<see cref="Protagonist"/> — P06).
        /// </summary>
        public void EnterRoom(ISeatLink link, int seat, int seats, ulong diceSeed)
        {
            if (Ctx == null) throw new InvalidOperationException("게임 데이터(Ctx) 없이는 방 판에 들어가지 못한다");
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (seat < 0 || seat >= seats) throw new ArgumentOutOfRangeException(nameof(seat), seat, "좌석 0~" + (seats - 1));
            KeepRoom(Leave);
            if (Stage.Current != null) Stage.Shut();
            startedSeed = diceSeed;
            startedAiSeed = 0;
            saveId = null;
            roomLink = link;
            roomSeats = seats;
            Drift = new AnimDrift();
            Enter(null, link, seat, seats, diceSeed, false);
            TradeLocked = !TradingReady;   // 방 판의 거래도 잠근다(결정 124 — 이어 연 방 포함)
        }

        /// <summary>방 화면(S05~S07)을 연다 — 하던 게임은 떠나지만 방은 남긴다(로비 ↔ 방 판 · 방을 여는 중).</summary>
        internal void ShowRoomScreen(IUiScreen screen)
        {
            KeepRoom(Leave);
            Stage.Open(screen);
            Restage();
        }

        void KeepRoom(Action act)
        {
            keepRoom = true;
            try { act(); }
            finally { keepRoom = false; }
        }

        /// <summary>새 방의 게임 시드·AI 시드(<c>entropy</c>).</summary>
        internal ulong NextSeed() => entropy();

        /// <summary>각본 튜토리얼(07 T61): 진행기가 세션을 화면 시계보다 앞으로 돌렸다(단계 끝의 정해진 일 · demo 답) — 화면 시계를 그 시각까지 당긴다(뒤로는 가지 않는다).</summary>
        internal void CatchUp(long ms)
        {
            if (ms > NowMs) NowMs = ms;
        }

        /// <summary>
        /// 각본 튜토리얼 층(07 §9-1)의 클릭 거르개: 보드 칸 누르기 · 좌석 칩 누르기(<see cref="Pointer"/>)가 그 패널 점(왼쪽 위가 (0, 0))을 물어 false면 삼킨다(끌기·휠은 그대로).
        /// null = 거르지 않는다. PC 층 요소의 클릭은 층이 패널에서 직접 거른다.
        /// </summary>
        public Func<Vector2, bool> ClickGate { get; set; }

        /// <summary>프레임 끝(<see cref="Tick"/>의 마지막)에 불린다 — 각본 튜토리얼 층이 단계를 넘기고 앵커를 따라간다.</summary>
        public event Action Ticked;

        bool Gated(float x, float y)
        {
            if (ClickGate == null || laidFor.y <= 0) return false;
            float scale = Stage.Grade.PanelScale;
            return !ClickGate(new Vector2(x / scale, (laidFor.y - y) / scale));
        }

        ulong startedSeed, startedAiSeed;

        /// <summary>
        /// 확인용(좌석 여럿인 판 — <see cref="StartSeats"/>): 지금 턴인 좌석이 갈 수 있는 칸(엔진의 합법 이동 답의 도착 칸). 내 좌석이 아니어도 된다. 이동을 고를 때가 아니면 비어 있다.
        /// </summary>
        public List<int> ActiveReach()
        {
            var cells = new List<int>();
            var client = ActiveClient();
            var choice = client != null ? client.Choice(ChoiceKind.MapAction) : null;
            if (choice != null)
                foreach (var a in client.Legal(choice))
                    if (a.Kind == AnswerKind.Move) cells.Add(a.Target);
            return cells;
        }

        /// <summary>
        /// 확인용(좌석 여럿인 판): 지금 턴인 좌석을 그 칸으로 걷게 한다 — 남의 말이 묶음에서 걸어 나가고 들어와 묶이는 것을 내 좌석의 화면으로 본다. 받아들여졌으면 true. 게임 화면은 부르지 않는다.
        /// </summary>
        public bool WalkActive(int cell)
        {
            var client = ActiveClient();
            var choice = client != null ? client.Choice(ChoiceKind.MapAction) : null;
            return choice != null && Guard(() => client.Answer(choice, ChoiceAnswer.Of(AnswerKind.Move, cell), NowMs)).Accepted;
        }

        /// <summary>
        /// 확인용(좌석 여럿인 판): 그 좌석에 열린 첫 선택에 답한다 — pick이 합법 답에서 고르고(null = 첫 답), 받아들여졌으면 true. 내 좌석은 화면으로 답하므로 여기서 답하지 않는다.
        /// 여럿이 하는 판(M7)이 생기기 전에 다른 좌석의 셋업 답 · 턴 종료를 넣어 그 연출(동시 공개 99행 · 턴 시작 6행 · 라운드 띠 102행)을 내 좌석의 화면으로 본다.
        /// </summary>
        public bool AnswerSeat(int seat, Func<ChoiceAnswer, bool> pick = null)
        {
            if (Session == null || Client == null || seat == Client.Seat || seat < 0 || seat >= Session.SeatCount) return false;
            if (others == null) others = new Dictionary<int, SeatClient>();
            if (!others.TryGetValue(seat, out var client)) others[seat] = client = new SeatClient(Session.Link, Ctx, seat);
            var choice = client.FirstChoice();
            if (choice == null) return false;
            foreach (var a in client.Legal(choice))
                if (pick == null || pick(a)) return Guard(() => client.Answer(choice, a, NowMs)).Accepted;
            return false;
        }

        /// <summary>확인용(좌석 여럿인 판): 그 좌석에 열린 첫 선택에 그 답을 그대로 낸다(합법 답에 늘어놓지 않는 답 — 거래 제안 · 고른 길). 받아들여졌으면 true.</summary>
        public bool AnswerSeatWith(int seat, ChoiceAnswer answer)
        {
            if (Session == null || Client == null || answer == null || seat == Client.Seat || seat < 0 || seat >= Session.SeatCount) return false;
            if (others == null) others = new Dictionary<int, SeatClient>();
            if (!others.TryGetValue(seat, out var client)) others[seat] = client = new SeatClient(Session.Link, Ctx, seat);
            var choice = client.FirstChoice();
            return choice != null && Guard(() => client.Answer(choice, answer, NowMs)).Accepted;
        }

        /// <summary>확인용(좌석 여럿인 판): 그 좌석이 자기 턴이 아닐 때 거래를 제안한다(명령 Propose — 남의 턴의 거래, 결정 75 B5 나). 받아들여졌으면 true.</summary>
        public bool ProposeSeat(int seat, ChoiceAnswer offer)
        {
            if (Session == null || Client == null || offer == null || seat == Client.Seat || seat < 0 || seat >= Session.SeatCount) return false;
            if (others == null) others = new Dictionary<int, SeatClient>();
            if (!others.TryGetValue(seat, out var client)) others[seat] = client = new SeatClient(Session.Link, Ctx, seat);
            return Guard(() => client.Propose(offer, NowMs)).Accepted;
        }

        /// <summary>확인용(좌석 여럿인 판): 그 좌석이 정비 명령을 낸다(남 좌석이 낸 아르세우스의 축복 — 나에게 동의 물음이 온다). 받아들여졌으면 true.</summary>
        public bool MaintainSeat(int seat, Maintenance m)
        {
            if (Session == null || Client == null || m == null || seat == Client.Seat || seat < 0 || seat >= Session.SeatCount) return false;
            if (others == null) others = new Dictionary<int, SeatClient>();
            if (!others.TryGetValue(seat, out var client)) others[seat] = client = new SeatClient(Session.Link, Ctx, seat);
            return Guard(() => client.Maintain(m, NowMs)).Accepted;
        }

        // 지금 턴인 좌석의 손님(내 좌석이면 내 손님, 아니면 그 좌석에 붙인 확인용 손님)
        SeatClient ActiveClient()
        {
            var view = Client != null ? Client.View : null;
            if (view == null || Session == null || view.ActiveSeat < 0 || view.ActiveSeat >= view.Seats.Length) return null;
            if (view.ActiveSeat == Client.Seat) return Client;
            if (others == null) others = new Dictionary<int, SeatClient>();
            if (!others.TryGetValue(view.ActiveSeat, out var client)) others[view.ActiveSeat] = client = new SeatClient(Session.Link, Ctx, view.ActiveSeat);
            return client;
        }

        Dictionary<int, SeatClient> others;

        // 세션 하나에 들어간다(새 판 · 이어 받은 판): 좌석 손님을 붙이고 화면을 맞춘다
        void Enter(LocalSession session, ulong diceSeed, bool resumed) => Enter(session, session.Link, SoloGame.Seat, session.SeatCount, diceSeed, resumed);

        // 좌석 하나에 들어간다: 세션이 이 PC에 있으면 session(솔로 · 튜토리얼 · 확인용 판), 방의 내 좌석이면 session = null에 망 위의 링크(EnterRoom)
        void Enter(LocalSession session, ISeatLink link, int mySeat, int seatCount, ulong diceSeed, bool resumed)
        {
            NowMs = 0;
            NotReadyShown = 0;
            Session = session;
            boardMusicSet = false;
            Log = new BoardLog(Ctx);
            Client = new SeatClient(link, Ctx, mySeat);
            // 이어 받은 판이 고르는 화면부터 열리면 받은 카드 연출(1행)을 다시 틀지 않는다
            pickOffered = Client.Choice(ChoiceKind.TrainerCardPick) != null;
            diceSeedBase = diceSeed;
            fakeUndo = null;
            fakePoints = null;
            Rebuild(mySeat, seatCount, diceSeed);
            // 이어 받은 판의 기록(S21)은 「저장한 곳에서 이어서 시작했다」 한 줄로 시작한다 [가정 — 지나간 줄은 저장하지 않는다]
            var view = Client.View;
            if (resumed) Log.Resumed(view != null && view.Phase != Phase.Setup ? view.Round : 0, actsRevealed);
            Client.Updated += OnUpdated;
            Sync();
        }

        // 지금 보기에서 화면 쪽 상태를 세운다(새 판 · 이어 받은 판 · 되돌린 판 — undo.md §3-5): 연출 대기열 · 맥락 · 턴 요약 합 · 굴림판 녹화본 고르기.
        // 손님 · 링크 · 세션 · 기록(BoardLog)은 건드리지 않는다 — 부르는 쪽(Enter · Rewound)이 맡는다. 끝에 Sync는 부르는 쪽이
        void Rebuild(int mySeat, int seatCount, ulong diceSeed)
        {
            ulong seed = diceSeed;
            planner = new FxPlanner(mySeat, seatCount) { WatchWild = Session == null };   // 방 판(세션이 이 PC에 없다)은 남의 야생전도 끝까지 그린다(결정 112 Q19)
            stackMs = -1;
            Fx = new FxPlayer(Stage.Tweens, new FxSequencer { Speed = SoundPrefs.Fx }, ShowFx);
            Fx.Sequencer.Started += e => fxBegan |= e.LengthMs > 0;
            Fx.Sequencer.Finished += Settle;
            Fx.Sequencer.Sound += OnCue;
            // 녹화본 고르기는 겉모양뿐이라 규칙 난수와 따로 둔다(시드는 판에서 — 같은 판을 같은 길로 지나면 같은 굴림이 나온다: 캡처·시험)
            diceBag = dice != null ? new DiceBag(new Pcg32(seed ^ 0xD1CE5EEDUL), dice) : null;
            battleEnd = null;
            battleHeld = false;
            WatchingWild = false;
            openWaiting = false;
            wipeBackAt = long.MinValue;
            endedSeat = -1;
            turnApricorns = 0;
            turnJp = 0;
            turnLines.Clear();
            Array.Clear(secretAnswers, 0, secretAnswers.Length);
            roundPending = 0;
            startedRound = 0;
            actsRevealed = 0;
            noticeAct = noticeGot = -1;
            gruntsHeld = gruntsDropping = gruntsFlying = false;
            spotsHeld = spotsDropping = wide = false;
            answering = null;
            sentKinds.Clear();
            undoWaitEpoch = -1;   // 방에서 보낸 무르기의 기다림(GameFlow.Undo.cs) — 새 판 · 되돌린 판은 처음부터
            // 이어 받은 판 · 되돌린 판: 지나간 이벤트가 없으니 보기에서 세운다(공개된 막 수 · 라운드 · 연출 맥락). 새 판이면 셋업이라 모두 처음 값이다
            var view = Client.View;
            unlockHold.Clear();
            if (view != null && view.Phase != Phase.Setup)
            {
                planner.From(view);
                unlockHold.Resume(view.Battle != null);   // 배틀 도중에 저장한 판(M6 화면 교차 검토 #4) — BattleStarted를 다시 보지 않는다
                actsRevealed = view.Hos != null ? view.Hos.Act + 1 : 0;
                startedRound = view.Round;
            }
            seenJp = null;
            seenRoads = -1;
            if (view != null && view.Phase != Phase.Setup) SeeJp(view);   // 여행 점수 변화 알림의 기준(새 판 · 다시 연 판 · 손본 판)
        }

        // ── 저장(02 §7, 04 S04·S22) ──

        /// <summary>저장 폴더(null = 이 흐름은 저장하지 않는다).</summary>
        public string SaveRoot => saves;

        /// <summary>지금 판의 저장 이름(저장 폴더 안 폴더 이름). 저장하지 않는 판·게임 밖이면 null.</summary>
        public string SaveId => Session != null && Session.Saving ? saveId : null;

        string saveId;

        /// <summary>게임 데이터의 묶음 해시(새 판의 세이브 머리에 적고, 이어 받을 때·저장 목록에서 머리의 것과 견준다). 모르면 null.</summary>
        public string ContentHash => contentHash;

        /// <summary>마지막 저장 쓰기 실패의 까닭(폴더를 만들지 못했다 · 로그 줄을 쓰지 못했다). 없으면 null — 다음 쓰기가 되면 지운다(내 답이 받아들여졌을 때 · 밀린 AI 답이 쓰였을 때).</summary>
        public string SaveError { get; private set; }

        /// <summary>지금 판의 저장 상태(게임 메뉴 S22 「저장 상태」 줄).</summary>
        public SaveStatus SaveStatus
        {
            get
            {
                if (Session == null) return SaveStatus.Off;
                if (SaveError != null) return SaveStatus.Failed;
                if (!Session.Saving) return SaveStatus.Off;
                return Session.SnapshotFailed ? SaveStatus.Failed : SaveStatus.Saved;
            }
        }

        /// <summary>지금 판의 머리를 마지막으로 쓴 때(UTC ms — 흐름의 시계). 저장하지 않는 판이면 0.</summary>
        public long SavedAtMs => Session != null && Session.Saving ? Session.Head.Summary.SavedAtMs : 0;

        /// <summary>
        /// 저장 목록(04 S04): 저장 폴더의 저장을 머리만 읽어 늘어놓는다(새것부터, 읽을 수 없는 것은 뒤). 깨진 것·다른 형식 판·데이터가 달라진 것·솔로가 아닌 것은
        /// <see cref="SaveEntry.Problem"/>이 달려 온다(화면은 「읽을 수 없음」) — 던지지 않는다. 저장 폴더가 없으면 빈 목록.
        /// 데이터가 달라졌나: 머리의 ContentHash가 지금 것과 다르거나(값만 바뀐 표 · 효과·보드 표도 잡는다) 표의 키 목록이 다르다. 해시가 없는 머리(해시를 적기 전의 저장)는 키 목록만 본다 [가정].
        /// </summary>
        public List<SaveEntry> Saves()
        {
            var list = SaveStore.List(saves);
            if (Ctx == null) return list;
            var now = SaveRecord.IdTablesOf(Ctx);
            foreach (var e in list)
            {
                if (!e.Readable) continue;
                string diff = e.Head.SeatCount != SoloGame.SeatCount ? "솔로 저장이 아니다"
                    : contentHash != null && e.Head.ContentHash != null && contentHash != e.Head.ContentHash ? "데이터 해시가 다르다"
                    : SaveRecord.IdTablesDiff(e.Head.IdTables, now);
                if (diff == null) continue;
                e.Fail(SaveProblem.DataChanged, diff);
            }
            SaveStore.Sort(list);   // 방금 걸러진 것을 뒤로
            return list;
        }

        /// <summary>이어 받을 수 있는 저장인가: 읽을 수 있고 끝나지 않은 판 [가정 — 끝난 판은 목록에 남지만 이어 받지 않는다].</summary>
        public static bool CanResume(SaveEntry entry) => entry != null && entry.Readable && !entry.Head.Summary.Finished;

        /// <summary>
        /// 저장을 이어 받는다: 하던 게임을 떠나 그 판의 마지막 쉬는 때(<see cref="SaveRest"/> — 배틀·조우의 한가운데에서 끊겼으면 그 앞)로 돌아와 화면을 맞춘다. 그 뒤로도 같은 저장에 이어 쓴다.
        /// 읽지 못하면(깨짐 · 다른 형식 판 · 데이터가 달라짐 · 끝난 판) 아무것도 바꾸지 않고 false — 까닭은 <see cref="ResumeError"/>. 던지지 않는다.
        /// 보드 기록(S21 — 첫 줄은 「저장한 곳에서 이어서 시작했다」)과 턴 결과 요약의 합은 이어 받은 뒤부터 다시 모은다 [가정].
        /// </summary>
        public bool Resume(string saveId)
        {
            ResumeError = null;
            if (Ctx == null || saves == null)
            {
                ResumeError = "저장 폴더나 게임 데이터가 없다";
                return false;
            }
            LocalSession session;
            SaveRecord record;
            AgentWorker runner = null;
            try
            {
                string dir = SaveStore.PathOf(saves, saveId);
                record = SaveStore.Load(dir);
                if (record.Head.Summary != null && record.Head.Summary.Finished) throw new NotSupportedException("끝난 판이다");
                runner = SoloRunner((record.Head.Ai ?? new AiSettings()).GalacticAiLevel);   // 이어하기 = 머리의 세기 그대로(M8 「연결」)
                session = SoloGame.Resume(Ctx, record, StrongAgent.Virtual(), 0, new SaveStore(dir), contentHash, clock, runner);
            }
            catch (Exception e)
            {
                // 남이 고쳤거나 쓰다 끊긴 파일이다: 어떤 꼴로 깨졌든 하던 화면은 그대로 두고 까닭만 남긴다
                runner?.Dispose();
                ResumeError = e.Message;
                return false;
            }
            Leave();
            soloAi = runner;
            TradeLocked = !TradingReady;   // 거래가 켜진 채 저장된 판도 거래는 잠근다(결정 124)
            if (Stage.Current != null) Stage.Shut();   // 이어 받기를 누른 화면(타이틀 · 저장 목록)을 닫는다 — 보드로 돌아오면 HUD만 뜬다
            this.saveId = saveId;
            Settings = SoloSettings.FromHead(record.Head);
            // 굴림판 녹화본 고르기(겉모양)는 이어 받은 자리마다 다르게 섞는다
            Enter(session, record.Head.Seed ^ ((ulong)session.CommandCount * 0x9E3779B97F4A7C15UL), true);
            return true;
        }

        /// <summary>마지막 <see cref="Resume"/>가 실패한 까닭(로그용 — 화면에는 짧은 안내만). 성공했으면 null.</summary>
        public string ResumeError { get; private set; }

        /// <summary>저장을 지운다(목록 S04의 [삭제]). 지금 하고 있는 판의 저장은 지우지 않는다(false). 지우지 못하면 false — 던지지 않는다.</summary>
        public bool DeleteSave(string saveId)
        {
            if (saves == null || (SaveId != null && SaveId == saveId)) return false;
            try
            {
                return SaveStore.Delete(saves, saveId);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 혼자 하기의 자동 저장 한 칸(결정 70 ③): 저장 폴더 아래 이 이름의 폴더 하나. 새 판을 시작하면(<see cref="StartSolo(SoloSettings)"/>) 이 칸을 덮는다.
        /// 전에 판마다 만들던 저장(<c>solo-&lt;날짜·시각&gt;-&lt;시드&gt;</c>)은 지우지 않는다 — 저장 목록에 그대로 보이고 이어 받으면 그 폴더에 이어 쓴다. 여럿 판 저장은 M7.
        /// </summary>
        public const string SoloSlot = "solo-auto";

        /// <summary>자동 저장 칸에 이어할 수 있는 판이 있나(새 판을 시작하면 덮인다 — 솔로 설정 화면이 알린다). 파일을 읽으니 화면을 열 때 한 번.</summary>
        public bool SoloSlotInUse
        {
            get
            {
                if (saves == null) return false;
                string dir = Path.Combine(saves, SoloSlot);
                return Directory.Exists(dir) && CanResume(SaveStore.Peek(dir));
            }
        }

        // 세션에 명령을 넣는 곳을 감싼다: 로그 줄을 쓰지 못하면 세션이 명령 전으로 되돌리고 던진다(02 §7) — 화면은 죽지 않고 그 답을 거절로 받으며 저장 상태가 「실패」가 된다
        SubmitResult Guard(Func<SubmitResult> act)
        {
            try
            {
                var r = act();
                if (r.Accepted && Session != null && Session.Saving) SaveError = null;   // 로그 줄이 쓰였다(저장 폴더를 못 만든 판은 끝까지 「실패」)
                return r;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SaveError = e.Message;
                return SubmitResult.Rejected(RejectReason.BadCommand);
            }
        }

        // ── 플레이 기록 올리기(02 §12) ──

        /// <summary>플레이 기록을 올리는 곳(null = 올리지 않는다 — 캡처·시험의 기본 · 웹). 플레이어는 <see cref="ScreenBoot"/>가 넣는다. 설정이 꺼져 있으면 받은 폴더를 버린다.</summary>
        public PlayUploads Uploads { get; set; }

        /// <summary>첫 실행의 플레이 기록 알림을 타이틀 위에 띄울 때인가(결정 126 ④ — 올리는 창구가 있는 PC 플레이어 · 아직 알림을 보지 않았다).</summary>
        public bool PlayNoticeDue => Uploads != null && !SoundPrefs.PlayNotice;

        /// <summary>지금 플레이 기록을 보내나(방장이 방을 열 때 로비 알림 줄 — <see cref="RoomState.Records"/>).</summary>
        public bool RecordsOn => Uploads != null && Uploads.Enabled;

        /// <summary>첫 실행 알림의 답: [확인](false) · [설정 열기](true). 본 것으로 적는다(다음 실행부터 뜨지 않는다 — 이때부터 올리기 시작).</summary>
        public void AnswerPlayNotice(bool openSettings)
        {
            SoundPrefs.PlayNotice = true;
            if (openSettings) OpenSettings();
        }

        bool uploadOffered;

        /// <summary>지금 판의 저장 폴더(솔로 = 내 저장 · 방 = 방장의 방 저장). 저장하지 않는 판 · 손님이면 null.</summary>
        string UploadDir() => Uploads == null ? null : SaveId != null ? SaveStore.PathOf(saves, SaveId) : Room.HostSaveDir;

        void Leave()
        {
            string uploadDir = Client != null ? UploadDir() : null;
            if (Client != null)
            {
                Client.Updated -= OnUpdated;
                Client.Close();
            }
            if (others != null)
                foreach (var kv in others) kv.Value.Close();
            others = null;
            Client = null;
            Session = null;
            soloAi?.Dispose();   // 「강함」 솔로 판의 AI 작업 스레드(M8 「연결」) — 셈하던 물음은 버린다
            soloAi = null;
            roomLink = null;
            roomSeats = 0;
            uploadOffered = false;
            TradeLocked = false;
            if (!keepRoom && !Room.Parked) Room.Close();   // 방(M7 화면 ①)에서 나간다(로비에 자리를 둔 채 튜토리얼을 하는 중이면 남긴다 — RoomFlow.OpenTutorial) — 방장이면 방 닫기. 방 화면 사이를 오갈 때·방 판에 들어갈 때는 남긴다(ShowRoomScreen · EnterRoom)
            Uploads?.Offer(uploadDir);   // 판을 떠날 때(방을 닫아 밀린 저장 쓰기를 비운 뒤)
            seatProtagonists = null;
            SaveError = null;
            tickFailed = false;
            saveId = null;
            Log = null;
            gruntsHeld = gruntsDropping = gruntsFlying = false;
            spotsHeld = spotsDropping = wide = false;
            answering = null;
            if (board != null && board.Tokens != null)
            {
                board.Tokens.Raise(-1);
                board.Tokens.Show(NoTokens);
            }
            if (board != null && board.Roads != null) board.Roads.Show(null, false, 0);
            Battle = null;
            WatchingWild = false;
            battleEnd = null;
            battleHeld = false;
            openWaiting = false;
            pulsing = false;
            wipeBackAt = long.MinValue;
            if (wipe != null) wipe.Set(0f);
            diceBag = null;
            boardCamSet = false;
            camStep = null;
            heldStep = null;
            heldEntry = null;
            drag.Kill();
            if (Fx != null) Fx.Sequencer.Clear();  // 재생 중인 연출을 버린다(실행기에 걸린 손잡이는 다음 프레임에 빠진다)
            Fx = null;
            Deals?.Show(null);   // 떠난 게임의 덮개(스탬프 · VS · 거래)를 걷는다
            Cards?.Show(null);   // 트레이너 카드 덮개도
            unlockHold.Clear();
            if (Sheets != null)   // 떠난 게임의 먼지·반짝임을 지운다(대기열이 없어 ShowFx가 더는 돌지 않는다 — 켜진 채 타이틀에 남는다)
            {
                Sheets.Begin();
                Sheets.End();
            }
            planner = null;
            Clocks = null;
            promptSeen.Clear();
            TimedOutAtMs = long.MinValue;
            fateAuto = false;
            warpHidden = -1;
            Stage.ShutInfo();   // 물음 창 · 배틀 위에 연 정보 창(결정 125)
            Stage.ShutHud();
            if (board != null && board.Marks != null) board.Marks.Hide();
            if (rig != null) rig.WheelEnabled = true;
        }

        int[] seenJp;

        // 여행 점수 변화 알림(M6): 이벤트(Changes)가 싣지 않은 차이 — 트레이너 카드 능력이 고친 것 등 — 를 좌석마다 기록 줄 하나로(새 판·다시 연 판의 첫 보기는 세지 않는다)
        void JpDrift(GameUpdate update)
        {
            var view = update.View;
            if (view == null || view.Phase == Phase.Setup) { seenJp = null; return; }
            if (seenJp != null && seenJp.Length == view.Seats.Length && Log != null)
                for (int s = 0; s < view.Seats.Length; s++)
                {
                    int drift = view.Seats[s].Jp - seenJp[s] - FxShown.JpDelta(update.Events, s);
                    if (drift != 0) Log.JpDrift(s, drift, update.Events);
                }
            SeeJp(view);
            RoadDrift(update);
        }

        int seenRoads = -1;

        // worker B의 도로(플레이 시험 — 결정 120): 도로 놓기는 이벤트가 없다 — 보기의 도로 칸이 늘면 그 턴 좌석(도로를 켠 좌석)의 기록 줄 하나(새 판 · 다시 연 판의 첫 보기는 세지 않는다)
        void RoadDrift(GameUpdate update)
        {
            var view = update.View;
            int roads = view != null && view.Roads != null ? view.Roads.Count : 0;
            int seat = view != null ? view.ActiveSeat : -1;
            if (seenRoads >= 0 && roads > seenRoads && Log != null && seat >= 0 && seat < view.Seats.Length && view.Seats[seat].Building) Log.RoadDrift(seat, roads - seenRoads, update.Events);
            seenRoads = view != null && view.Phase != Phase.Setup ? roads : -1;
        }

        void SeeJp(GameState view)
        {
            seenJp = new int[view.Seats.Length];
            for (int s = 0; s < view.Seats.Length; s++) seenJp[s] = view.Seats[s].Jp;
        }

        void OnUpdated(GameUpdate update)
        {
            // 무르기 · 되돌리기(결정 149 — undo.md §3-5): 되돌림 표지가 실린 갱신은 이벤트를 그리지 않고 보기에서 다시 세운다(이어하기와 같은 길).
            // 끊긴 동안의 되돌림(재접속 스냅샷에는 표지가 없고 Epoch만 앞섰다 — SeatClient.MissedRewind)도 같은 길로 [가정 U9]
            if (update.Rewind != null || Client.MissedRewind)
            {
                Rewound(update);
                return;
            }
            // 비공개 묶음(셋업의 스타팅 포켓몬·트레이너 카드)에 좌석마다 낸 답 수 — 고르는 화면의 좌석 줄이 「선택 완료」를 적는다(99행)
            foreach (var e in update.Events)
                if (e is SecretAnswered sa && sa.Window == Window.None && e.Seat >= 0 && e.Seat < secretAnswers.Length) secretAnswers[e.Seat]++;
            // 화면 대기열: 받은 이벤트 → 항목(방장과 같은 길이) → 부품이 있는 행만 남김. 셋업의 것도 넣는다(1~3행 — 결정 66 ③)
            var entries = planner.Plan(update.Events);
            Tag(update.Events, entries);
            Log?.Feed(update.Events);
            if (!uploadOffered && Client.View != null && Client.View.Phase == Phase.GameOver)   // 판이 끝났을 때 한 번(플레이 기록 올리기)
            {
                uploadOffered = true;
                Uploads?.Offer(UploadDir());
            }
            JpDrift(update);
            NotePrompts(update.Events);
            FeedBattle(update, entries);
            EventsSeen?.Invoke(update.Events);
            // 막 목표(94행)는 결과 창의 줄로만 그린다 — 배틀 밖에서 달성하면 그릴 곳이 아직 없다
            int seats = Session != null ? Session.SeatCount : roomSeats;
            bool followers = FollowerUpdate(update);
            FxBuilt.KeepSteps(entries, s => Built(s, seats) && (s.Row != 940 || Battle != null) && (WorkDraws(s) && ShrineDraws(s, followers) || OtherActs.Shows(s, Client.Seat, seats)) && DealDraws(s));
            // 귀환(71행)은 배틀 끝 항목에서 떼어 바로 뒤의 항목으로 — 결과 창 [확인] 뒤 보드에서 보인다(배틀 끝 항목이 다 재생되면 대기열이 선다 — Settle)
            WhiteoutBeats.Split(entries);
            GodBeats.SplitOff(entries);   // 신 격파(80행)도 결과 창 [확인] 뒤 보드에서
            GodBeats.SplitOff(entries, LiberateBeats.StepOf);   // 1막 해방의 조무래기 토큰 날아감(94행 941 — M9 다듬기 P18)도
            UbBattleRows.SplitPhase(entries);   // 네크로즈마 페이즈 격파(90행)도(M6 「UB 배틀 화면 묶음」)
            UbBattleRows.SplitSilvally(entries);   // 실버디 → 축복 +1(93행)도(M6 「연출 목록 채우기」)
            WhiteoutBeats.SplitOff(entries, ResultBeats.StepOf, ResultBeats.Ended);   // 게임 끝(97행)도 — 배신자 연전의 마지막 배틀 뒤 결과 창 [확인] 다음에
            WhiteoutBeats.SplitOff(entries, FallRows.StepOf, FallRows.Event);   // 탈락(91행 — 네크로즈마에게 진 배틀 끝 항목에 실린다)도 결과 창 [확인] 뒤 보드에서
            unlockHold.Defer(entries);   // 트레이너 카드 해금(48행 — 결정 84 M5 가)도: 배틀 중에 오른 등급은 배틀 끝 항목 뒤 새 항목으로(결과 창 [확인] 다음 보드에서 카드 뒤집기)
            if (OnBoard) StageDice(entries);
            OfferCards(update, entries);
            // 소리 단서(04 §8-4): 그리는 단계의 키 프레임에(덜어 낸 뒤 · 굴림 녹화본을 고른 뒤). 내가 당사자인 항목 = 내 좌석 · 판의 자동 처리·가상 좌석(솔로의 야생·갤럭시단 — 내 배틀의 상대).
            // 같은 칸의 말을 만남(13행 — 결정 73 ⑤)은 「!」와 같은 보기(Shared)로 그 칸에 다른 말이 있나를 본다
            // 울음(M9 소리 ③ — 층 Cry)은 포켓몬 표 색인(없으면 보기의 그 포켓몬)의 종 번호로
            SoundCues.Add(entries, entry => entry.Seat == Client.Seat || entry.Seat < 0, Shared, CryDex);
            Fx.Enqueue(entries);
            NoteDrift(update);
            if (OnBoard && Battle != null) Battle.Order(entries);   // 기록 줄은 드러나는 순서로(단계가 정해진 뒤에 센다)
            if (timedOut) CloseTimedOut();
            Sync();
        }

        // 받은 트레이너 카드(1행 「들어옴」): 게임 시작의 TrainerCardsDealt는 좌석이 붙기 전에 지나가 화면에 오지 않는다 — 내 트레이너 카드 고르기가 열린 갱신의 뒤에
        // 그 행의 길이(방장과 같은 값 — PlayTiming 10행)만큼의 단계 하나를 화면이 세운다 [가정]. 고르는 화면이 열릴 때 카드가 들어온다.
        // 이어 받은 판은 갱신 없이 화면부터 열리므로 틀지 않는다
        void OfferCards(GameUpdate update, List<FxEntry> entries)
        {
            if (pickOffered || update.View == null || update.View.Phase != Phase.Setup || Client.Choice(ChoiceKind.TrainerCardPick) == null) return;
            pickOffered = true;
            int ms = PlayTiming.RowMs(null, SetupRows.Deal);
            var entry = new FxEntry { Seq = -1, Seat = Client.Seat, Kind = ActionKind.Setup, QueueMs = ms };
            entry.Steps.Add(new FxStep { Row = SetupRows.Deal, Layer = FxLayer.Item, Ms = ms });
            entries.Add(entry);
        }

        // 배틀을 따라간다: 배틀이 시작된 갱신부터 그 배틀의 이벤트를 모은다. 끝난 갱신에서는 배틀 끝이 든 항목을 기억해 두고,
        // 그 항목이 다 재생되면(Settle) 화면 대기열을 세운다 — 마지막 공격·기절의 연출은 끝까지 보이고, 같은 갱신에 이어 오는 턴 넘김(요약 → 띠 → 컷신)은
        // 결과 창 뒤에서 지나가 버리지 않는다. [확인]이 푼다(CloseBattle)
        void FeedBattle(GameUpdate update, List<FxEntry> entries)
        {
            bool starts = false;
            foreach (var e in update.Events) if (e is BattleStarted) starts = true;
            // 방장 시각 판(T4): 결과 창을 열어 둔 채 다음 배틀이 시작됐다 — 끝난 배틀은 놓는다(대기열이 결과 창을 기다리지 않으니 새 배틀이 옛 배틀의 줄에 섞이지 않게)
            if (starts && BattleOver && HostPaced) DropHeldBattle();
            if (Battle == null)
            {
                if (!starts) return;
                Battle = new BattleFeed(Ctx, Client.Seat);
                wildFolded = false;
            }
            bool was = Battle.Ended;
            Battle.Feed(update.View, update.Events);
            if (was || !Battle.Ended || Fx == null) return;
            battleEnd = null;
            foreach (var entry in entries)
                foreach (var e in entry.Events)
                    if (e is BattleEnded) battleEnd = entry;
            if ((battleEnd == null || !OnBoard) && WildAside && !WatchingWild)
            {
                DropWildAside();
                return;
            }
            if (battleEnd == null || !OnBoard)
            {
                // 끝을 실은 항목이 대기열에 없다(있을 수 없는 길): 바로 세운다(방장 시각 판은 결과 창만)
                battleHeld = true;
                if (!HostPaced) Fx.SetPaused(true);
            }
        }

        // 결과 창이 떠 있던 끝난 배틀을 [확인] 없이 놓는다(방장 시각 판 — 남의 결과 창이 저절로 닫힘 · 열어 둔 채 다음 배틀이 시작됨)
        void DropHeldBattle()
        {
            if (Stage.Current is BattleScreen) Stage.Shut();
            Battle = null;
            battleEnd = null;
            battleHeld = false;
            WatchingWild = false;
            wildFolded = false;
            Restage();
            Hud?.Refresh();
        }

        // 굴림 단계(56행)에 녹화본 재생을 붙인다: 주사위 수의 묶음에서 한 벌을 고르고(섞기 주머니 + 앞뒤 뒤집기) 그 항목의 단계 시각으로 짠다.
        // 고르기는 항목을 받은 때 한 번 — 그리는 쪽은 시각만 넣는다(같은 시각이면 같은 그림)
        void StageDice(List<FxEntry> entries)
        {
            if (dice == null || diceBag == null) return;
            foreach (var entry in entries)
            {
                if (entry.Events.Exists(e => e is BattleStarted)) lastClip = null;   // 새 배틀: 앞 배틀의 굴림을 재굴림에 쓰지 않는다
                var rerolled = RerollBeats.StepOf(entry);
                var rolledAt = BattleRoll.StepOf(entry);
                if (rerolled != null && (rolledAt == null || rerolled.StartMs < rolledAt.StartMs)) StageReroll(rerolled);
                // 포획 굴림(68행): 던짐과 함께 구른다. 상태 이상인 대상이면 심볼 하나가 멈춘 뒤 제자리에서 일반으로 넘어간다(57행)
                var throwStep = CaptureStage.StepOf(entry);
                if (throwStep != null)
                {
                    var c = (CaptureAttempt)throwStep.Events[0];
                    int count = c.Faces != null ? c.Faces.Length : 0;
                    lastClip = null;   // 포획 굴림 뒤의 재굴림은 공격 굴림의 주사위로 그리지 않는다
                    if (count >= DiceGeo.MinDice && count <= DiceGeo.MaxDice && dice.Count(count) > 0)
                    {
                        var take = diceBag.Next(count);
                        var clip = dice.Clip(take);
                        var scene = new DiceRollScene(clip, take.Mirror, c.Faces, entry.Seq) { ThrowMs = throwStep.StartMs };
                        int stop = throwStep.StartMs + clip.LengthMs, turned = CaptureStage.Converted(c);
                        if (turned >= 0) scene.AddFlip(Math.Max(throwStep.StartMs, stop - DiceRollScene.FlipMs / 2), turned, AttackDieFace.RegularHit);
                        int verdict = CaptureStage.VerdictMs(c);
                        scene.CountMs = throwStep.StartMs + CaptureStage.CountAtMs(verdict, CaptureStage.CountEveryMs(verdict, clip.LengthMs, count), count, 0);
                        throwStep.Tag = scene;
                    }
                }
                var roll = BattleRoll.StepOf(entry);
                if (roll == null || roll.Events.Count == 0 || !(roll.Events[0] is DiceRolled d) || d.Faces == null) continue;
                int n = d.Faces.Length;
                lastClip = null;
                if (n < DiceGeo.MinDice || n > DiceGeo.MaxDice || dice.Count(n) == 0) continue;
                var pick = diceBag.Next(n);
                roll.Tag = DiceRollScene.Plan(entry, dice.Clip(pick), pick.Mirror, entry.Seq);
                // 이 굴림의 멈춘 모습을 쥐어 둔다 — 굴린 뒤 창의 디아루가 재굴림(43행)이 같은 주사위를 제자리에서 다시 그린다(M9 다듬기 P38)
                lastClip = dice.Clip(pick);
                lastMirror = pick.Mirror;
                lastSalt = entry.Seq;
                lastFaces = (AttackDieFace[])d.Faces.Clone();
                foreach (var step in entry.Steps)
                {
                    if (step.StartMs < roll.StartMs || step.Row == FateRows.Reroll) continue;
                    foreach (var e in step.Events)
                        if (e is DiceFacesChanged turned && turned.Changes != null)
                            foreach (var ch in turned.Changes)
                                if (ch.Index >= 0 && ch.Index < n && ch.After != AttackDieFace.None) lastFaces[ch.Index] = ch.After;
                }
                if (rerolled != null && rerolled.StartMs >= roll.StartMs) StageReroll(rerolled);
            }
        }

        // 마지막 공격 굴림의 녹화본 · 멈춘 면(면 바꾸기까지 지난 것 — 지워진 주사위는 그리던 면 그대로)
        DiceClip lastClip;
        bool lastMirror;
        int lastSalt;
        AttackDieFace[] lastFaces;

        // 디아루가 재굴림 단계(43행)에 「멈춘 주사위 + 제자리 글리치」 재생을 붙인다(M9 다듬기 P38): 마지막 공격 굴림의 녹화본을 멈춘 뒤의 모습으로 세우고,
        // 고른 주사위마다 PlayTiming.RerollPerMs 간격으로 글리치를 건다(RerollBeats와 같은 때). 쥐어 둔 굴림이 없으면(포획 굴림 · 이어 받은 판) 붙이지 않는다 — 화면은 옛 주사위 줄
        void StageReroll(FxStep step)
        {
            var changed = RerollBeats.Event(step);
            if (lastClip == null || lastFaces == null || changed == null || changed.Changes == null) return;
            foreach (var ch in changed.Changes)
                if (ch.Index >= lastFaces.Length || ch.Index >= 0 && ch.Before != AttackDieFace.None && ch.Before != lastFaces[ch.Index]) return;   // 쥐어 둔 굴림의 주사위가 아니다(포획 굴림의 재굴림 등)
            var scene = new DiceRollScene(lastClip, lastMirror, lastFaces, lastSalt) { ThrowMs = step.StartMs - 600000 };
            int per = PlayTiming.RerollPerMs.Value;
            for (int k = 0; k < changed.Changes.Length; k++)
            {
                var ch = changed.Changes[k];
                if (ch.Index < 0 || ch.After == AttackDieFace.None) continue;
                scene.AddReroll(step.StartMs + per * k, ch.Index, ch.After);
                lastFaces[ch.Index] = ch.After;
            }
            step.Tag = scene;
        }

        readonly int[] secretAnswers = new int[SeatId.MaxSeats];

        /// <summary>그 좌석이 지금까지 낸 비공개 답 수(셋업의 고르기 묶음 — 스타팅 포켓몬 · 트레이너 카드). 무엇을 골랐는지는 공개(99행) 전에는 모른다.</summary>
        public int SecretAnswers(int seat) => seat >= 0 && seat < secretAnswers.Length ? secretAnswers[seat] : 0;

        // 새 라운드가 시작됐다(RoundStarted) — 그 라운드의 첫 턴 시작 단계(6행)에 라운드 띠(102행)를 싣는다
        int roundPending;

        // 턴 넘김 단계에 띄울 것을 붙인다(누구 → 누구 · 라운드 · 끝난 턴에 얻은 것). 이벤트 순서대로 훑으며 턴마다 모은다
        void Tag(IReadOnlyList<GameEvent> events, List<FxEntry> entries)
        {
            var infos = new Dictionary<GameEvent, TurnBandInfo>();
            Dictionary<GameEvent, ActCardInfo> acts = null;
            Dictionary<GameEvent, ChoiceKind> chose = null;
            Dictionary<GameEvent, TurnStartInfo> starts = null;
            int allowance = -1;
            foreach (var e in events)
            {
                if (e is RoundStarted rs) roundPending = rs.Round;
                // 턴 시작(6행 — 여럿 판)에 띄울 것: 턴인 좌석 · 받은 이동력(같은 행동의 MoveAllowanceSet) · 라운드의 첫 턴이면 그 라운드(102행)
                if (e is TurnStarted begun)
                {
                    foreach (var o in events) if (o is MoveAllowanceSet set && o.Seq == e.Seq) allowance = set.Total;
                    (starts ?? (starts = new Dictionary<GameEvent, TurnStartInfo>()))[e] = new TurnStartInfo
                    {
                        Seat = begun.Seat, Mine = begun.Seat == Client.Seat, Moves = Math.Max(0, allowance), Round = begun.IsExtra ? 0 : roundPending,
                        Color = SeatColor(begun.Seat), Name = SeatName(begun.Seat), Face = Protagonist(begun.Seat),
                    };
                    roundPending = 0;
                }
                // 「고른 뒤」(11행)에 적을 것: 무엇을 고른 답인가(스타팅 포켓몬 · 트레이너 카드) — 비공개 답의 이벤트에는 없어 지금 내는 답에서 받는다
                if (e is SecretAnswered sa && sa.Window == Window.None)
                {
                    ChoiceKind? kind = answering;
                    if (!kind.HasValue && e.Seat == Client.Seat && sentKinds.Count > 0) kind = sentKinds.Dequeue();
                    if (kind.HasValue) (chose ?? (chose = new Dictionary<GameEvent, ChoiceKind>()))[e] = kind.Value;
                }
                if (e is ActRevealed ar)
                {
                    // 막 카드(4행)에 적을 것: 몇 막인가 · 처음 달성해 공개한 좌석(서막·1막은 없다)
                    var card = new ActCardInfo { Act = Math.Min(actsRevealed++, HosState.Acts - 1), FirstSeat = ar.FirstSeat };
                    if (ar.FirstSeat >= 0)
                    {
                        card.FirstName = SeatName(ar.FirstSeat);
                        card.FirstColor = SeatColor(ar.FirstSeat);
                        card.FirstFace = Protagonist(ar.FirstSeat);
                    }
                    (acts ?? (acts = new Dictionary<GameEvent, ActCardInfo>()))[e] = card;
                    noticeAct = card.Act;   // 막 안내 창(결정 101 ②)이 그 연출 뒤에 보일 막
                }
                if (e is ApricornsTaken a) turnApricorns += a.Count;
                else if (e is PokemonGained pg && pg.Species >= 0 && pg.Species < Ctx.Cards.Pokemon.Count)
                    turnLines.Add(KoJosa.Fix(Stage.T("screen.board.summary.caught").Replace("{mon}", Stage.T("pokemon." + Ctx.Cards.Pokemon[pg.Species].Key))));
                else if (e is BattleEnded be) Gain(be.Changes);
                else if (e is Fled fl) Gain(fl.Changes);
                else if (e is ActGoalMet goal)
                {
                    if (goal.Seat == Client.Seat) noticeGot = goal.Act;   // 막 안내 창의 「얻은 것」 줄
                    turnLines.Add(Stage.T("screen.board.summary.goal").Replace("{n}", goal.Act.ToString(CultureInfo.InvariantCulture)));
                }
                else if (e is TurnEnded te) endedSeat = te.Seat;
                else if (e is TurnStarted ts)
                {
                    var info = new TurnBandInfo { From = endedSeat, To = ts.Seat, Round = ts.Round, FromRound = startedRound, ToColor = SeatColor(ts.Seat), ToName = SeatName(ts.Seat), ToFace = Protagonist(ts.Seat) };
                    startedRound = ts.Round;
                    if (endedSeat >= 0)
                    {
                        info.FromColor = SeatColor(endedSeat);
                        info.FromName = SeatName(endedSeat);
                        info.FromFace = Protagonist(endedSeat);
                    }
                    // 그 턴에 얻은 것(04 §8-8 70·101행): 잡은 포켓몬 · 막 목표 → 여행 점수 → 규토리. 방장·화면이 요약 단계를 세우는 이벤트(PlayTiming.IsTurnGain)와 같은 것을 적는다
                    info.Lines.AddRange(turnLines);
                    if (turnJp > 0) info.Lines.Add(Stage.T("screen.board.summary.jp").Replace("{n}", turnJp.ToString(CultureInfo.InvariantCulture)));
                    if (turnApricorns > 0) info.Lines.Add(Stage.T("screen.board.summary.apricorns").Replace("{n}", turnApricorns.ToString(CultureInfo.InvariantCulture)));
                    infos[e] = info;
                    turnLines.Clear();
                    turnJp = 0;
                    turnApricorns = 0;
                    endedSeat = -1;
                }
            }
            foreach (var entry in entries)
                foreach (var step in entry.Steps)
                {
                    if ((step.Row == FxRows.TurnSummary || step.Row == FxRows.Handoff || step.Row == FxRows.MyTurn) && step.Events.Count > 0 && infos.TryGetValue(step.Events[0], out var info))
                        step.Tag = info;
                    else if (step.Row == TurnStartBeats.Row && starts != null && step.Events.Count > 0 && starts.TryGetValue(step.Events[0], out var start))
                        step.Tag = start;
                    else if (step.Row == ActBeats.Row && acts != null && step.Events.Count > 0 && acts.TryGetValue(step.Events[0], out var card))
                        step.Tag = card;
                    else if (step.Row == SetupRows.Chosen && chose != null && step.Events.Count > 0 && chose.TryGetValue(step.Events[0], out var kind))
                        step.Tag = kind;
                }
        }

        // 배틀로 받은 것(이긴 좌석의 여행 점수·규토리, 도망친 야생이 남긴 규토리)을 그 턴의 합에 더한다
        void Gain(ResChange[] changes)
        {
            if (changes == null) return;
            foreach (var c in changes)
            {
                if (c == null || c.After <= c.Before) continue;
                if (c.Res == ResKind.Jp) turnJp += c.After - c.Before;
                else if (c.Res == ResKind.Apricorn) turnApricorns += c.After - c.Before;
            }
        }

        /// <summary>연출 실접속의 자(방 판 — plan M7 완료 조건 6): 갱신마다 방장 멈춤 끝 − 내 화면 대기열 끝. 방 판에 들어갈 때 새로 센다.</summary>
        public AnimDrift Drift { get; private set; } = new AnimDrift();

        /// <summary>차이 줄(<c>[Sync] …</c>)을 로그에 남기나. 기본 켬 — 이벤트가 든 갱신마다 한 줄(방 판만).</summary>
        public bool DriftLog { get; set; } = true;

        // 방 판의 갱신 하나: 방장이 보낸 때의 남은 멈춤(시계에 실려 온다)과 이 갱신을 넣은 뒤 내 대기열에 남은 길이를 견준다. 이벤트 없는 갱신(지금 보기)은 세지 않는다
        // 늦었으면(내 대기열 끝이 방장 멈춤 끝보다 CatchUpFromMs 넘게 뒤) 그만큼을 따라잡는다 — 대기열 시계가 잠시 두 배로 흐른다(플레이 시험 T4). 확인용 방장 시각 판(망 없음)은 갱신의 방장 멈춤 끝(세션 시각)으로 센다
        void NoteDrift(GameUpdate update)
        {
            if (!HostPaced || update.Events == null || update.Events.Count == 0) return;
            long host;
            if (update.Clock != null) host = update.Clock.AnimLeftMs;
            else if (roomLink == null) host = Math.Max(0, update.AnimEndMs - NowMs);
            else return;
            long mine = 0;
            var seq = Fx.Sequencer;
            foreach (var entry in seq.Queued) mine += entry.LengthMs - (ReferenceEquals(entry, seq.Current) ? seq.LocalMs : 0);
            Drift.Note(host, mine);
            long late = Math.Max(0, mine) - Math.Max(0, host);
            seq.CatchUpMs = late > CatchUpFromMs ? late : 0;
            if (DriftLog && roomLink != null) Debug.Log(Drift.Line(Client.Seat, update.Version, update.Events.Count, host, mine));
        }

        /// <summary>좌석의 화면 이름: 방 판이면 그 좌석의 방 닉네임(HUD 좌석 칩 · 기록 · 턴 띠 — M7 「연출 실접속」), 아니면(혼자 하기 · 튜토리얼 · 닉네임 없음) 「플레이어 n」.</summary>
        public string SeatName(int seat)
        {
            string nick = Room.InGame ? Room.Live.NameOf(seat) : null;
            return !string.IsNullOrEmpty(nick) ? nick : Stage.T("screen.board.seat").Replace("{n}", (seat + 1).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>좌석 색(고향 마을 색, 04 §3-1 좌석 색 B). 고향을 아직 안 골랐으면 회색.</summary>
        public Color32 SeatColor(int seat)
        {
            var view = Client != null ? Client.View : null;
            if (view == null || seat < 0 || seat >= view.Seats.Length || view.Seats[seat].HomeVillage < 0) return SeatColors.Unassigned;
            return SeatColors.ForVillage(Ctx.Fixtures.Fixtures[view.Seats[seat].HomeVillage].Key);
        }

        /// <summary>내 좌석에 열린 선택에 맞는 화면으로 맞춘다. 같은 종류의 화면이 이미 열려 있으면 그대로 둔다(묶음의 답을 하나씩 내는 사이에 다시 만들지 않는다).</summary>
        void Sync()
        {
            if (Client == null || Client.View == null) return;
            // 셋업 연출(1~3행)이 대기열에 남아 있으면 셋업이 끝났어도(보기는 마지막 답과 함께 바로 다음 단계다) 고르는 화면이 그 연출을 끝까지 보인다
            var queue = Fx != null ? Fx.Sequencer : null;
            bool setup = Client.View.Phase == Phase.Setup;
            setupKey = SetupKey();
            if (!setup && !SetupRows.Pending(queue))
            {
                // 셋업 끝: 셋업 화면을 닫고 보드 HUD를 연다(그 위의 창 — 게임 메뉴 — 은 갱신이 와도 그대로 둔다)
                if (Stage.Current is SetupVillageScreen || Stage.Current is SetupPickScreen || Stage.Current is SetupDropScreen) Stage.Shut();
                if (Hud == null) Stage.OpenHud(new BoardHud(this));
                // 배틀: HUD 위에 배틀 화면을 연다(같은 화면이 열려 있으면 그대로 — 답을 하나씩 내는 사이에 다시 만들지 않는다). 닫는 것은 결과 창의 [확인]이다
                // 여는 때는 화면 대기열이 정한다: 배틀 화면은 조우 전환(22행)의 가운데 — 줄무늬가 다 닫힌 때 — 에 열고, 그 전에는 열려 있던 것(조우 창 · 보드)을 그대로 둔다.
                // 조우 창은 찾아보기(20행) 뒤 카드 공개(21행)가 시작할 때 연다. 그 단계가 대기열에 없으면(연출을 다 보냈다 · 대기열 없음) 바로 연다
                // 게임 메뉴가 떠 있는 동안에는 그 위로 열지 않고 기다린다 — 메뉴가 화면 대기열을 세워 두므로 위로 열면 대기열이 선 채 남는다. 메뉴를 닫을 때(CloseMenu) 다시 맞춘다
                openWaiting = false;
                bool menu = MenuUp;
                // 종막(S62·S63·S64 — M5 「막·종막 화면」): 종막 선택·순서 고르기·결과 창은 HUD 위에. 그 연출(95~97행)이 도는 동안·끝난 판은 그 창이 남는다
                int fin = FinaleStage();
                finaleShown = fin;
                if (fin == 0 && (Stage.Current is EndingScreen || Stage.Current is ResultScreen)) Stage.Shut();
                if (fin == FinaleResult)
                {
                    if (Stage.Current is ResultScreen) { }
                    else if (!menu) Stage.Open(new ResultScreen(this));
                    else openWaiting = true;
                }
                else if (fin == FinaleAsk)
                {
                    if (Stage.Current is EndingScreen) { }
                    else if (!menu) Stage.Open(new EndingScreen(this));
                    else openWaiting = true;
                }
                else if (Battle != null && WildAside && !WatchingWild && !(WildAutoOpens && EnterRows.Reached(Fx != null ? Fx.Sequencer : null, EnterRows.Wipe, EnterBeats.WipeSwapAt)))
                {
                    // 남의 야생 배틀(결정 114 — 결정 22 Q6): 내 창이 떠 있거나 내가 접었으면 뒤에서 그리기만 — 가운데 필드는 보드 · 하던 창 그대로.
                    // 창이 없으면(결정 120) 전환 22행의 가운데에 내 배틀처럼 연다 — 그때까지는 기다린다(ShowFx가 WildAutoOpens를 보고 다시 맞춘다)
                    if (Stage.Current is BattleScreen) Stage.Shut();
                }
                else if (Battle != null)
                {
                    // team_rocket R 날아갈 칸(배틀을 닫는 단계의 카드 고르기 — 값 = 보드 칸): 그 앞 연출이 다 돈 뒤 배틀 화면을 걷어 보드에서 칸을 고르게 하고, 답하면 배틀 화면을 다시 연다(결과 창)
                    bool boardPick = BoardPickInBattle;
                    if (boardPick && (Fx == null || Fx.Sequencer.InputOpen)) { if (Stage.Current is BattleScreen) Stage.Shut(); }
                    else if (boardPick) openWaiting = true;
                    else if (Stage.Current is BattleScreen) { }
                    else if (!menu && EnterRows.Reached(Fx != null ? Fx.Sequencer : null, EnterRows.Wipe, EnterBeats.WipeSwapAt))
                    {
                        if (WildAside) WatchingWild = true;   // 저절로 연 남의 야생 배틀(결정 120) — 접으면 뒤에서 계속
                        Stage.Open(new BattleScreen(this));
                    }
                    else openWaiting = true;
                }
                // 조우 고르기(S45): 찾아보기 뒤 카드가 나왔다 — HUD 위에 조우 창. 고르면 배틀이 시작돼 위에서 배틀 화면으로 바뀐다
                else if (Client.Choice(ChoiceKind.EncounterPick) != null)
                {
                    bool again = Stage.Current is EncounterScreen shown && shown.Stale;   // ace_trainer B 다시 찾아보기: 새 카드는 새 카드 공개(21행) 때 창을 다시 열어 그린다(그동안 옛 창은 꺼진 채)
                    if (again)
                    {
                        if (!menu && EnterRows.Reached(Fx != null ? Fx.Sequencer : null, EnterRows.Reveal)) Stage.Open(new EncounterScreen(this));
                        else openWaiting = true;
                    }
                    else if (Stage.Current is EncounterScreen || Stage.Current is CraftScreen) { }   // 조우 창 위의 제작 창(21행 [제작]): 닫으면 조우 창이 다시 선다
                    else if (!menu && EnterRows.Reached(Fx != null ? Fx.Sequencer : null, EnterRows.Reveal)) Stage.Open(new EncounterScreen(this));
                    else openWaiting = true;
                }
                else if (Stage.Current is EncounterScreen) Stage.Shut();
                SyncFate(menu);
                SyncShrine(menu);
                SyncBreach(menu);
                SyncBless(menu);
                // 진화 갈래 · 옛 기술을 묻는데 진화 창이 닫혀 있다(이어 받은 판): 진화 창을 연다
                if (Battle == null && Stage.Current == null && !menu && EvolveScreen.Waits(Client)) Stage.Open(new EvolveScreen(this));
                // 보드에서 카드가 연 놓아주기·기술 가르치기(M6 — 출처 = 카드): HUD 위 창. 답하면(다른 선택이 오면) 닫는다
                var first = Client.FirstChoice();
                if (Battle == null && Stage.Current == null && !menu && CardRewardScreen.Owns(first)) Stage.Open(new CardRewardScreen(this));
                else if (Stage.Current is CardRewardScreen && !CardRewardScreen.Owns(first)) Stage.Shut();
                // 반바지 꼬마 빨강의 카드 바꾸기(결정 87 V6 나): 뽑은 카드를 부채꼴로 펼친 창
                if (Battle == null && Stage.Current == null && !menu && CardSwapScreen.Owns(Ctx, first))
                {
                    EnsureDeals();
                    Stage.Open(new CardSwapScreen(this));
                }
                else if (Stage.Current is CardSwapScreen && !CardSwapScreen.Owns(Ctx, first)) Stage.Shut();
            }
            else if (SetupRows.Pending(queue, SetupRows.Village))
            {
                // 고향 마을 연출(2행): 마을 화면을 걷고 보드를 보인다(카메라가 마을로 · 말이 떨어진다) — 그동안 좌석 띠만 뜬다
                if (!(Stage.Current is SetupDropScreen)) Stage.Open(new SetupDropScreen(this));
            }
            else if (setup && Client.Choice(ChoiceKind.VillagePick) != null)
            {
                if (!(Stage.Current is SetupVillageScreen mine && mine.Watched < 0)) Stage.Open(new SetupVillageScreen(this));
            }
            else if (setup && OtherVillagePick() >= 0)
            {
                // 남이 고향 마을을 고르는 중(결정 120 ① · T2): 같은 화면을 보기만 — 누가 고르는지 · 그 후보, 고르면 고향 마을 연출(2행)
                int other = OtherVillagePick();
                if (!(Stage.Current is SetupVillageScreen shown && shown.Watched == other)) Stage.Open(new SetupVillageScreen(this, other));
            }
            else if (Stage.Current is SetupPickScreen) { }   // 고르는 중이거나, 고른 뒤의 연출이 끝날 때까지 남는다
            else if (setup && (Client.Choice(ChoiceKind.StarterPick) != null || Client.Choice(ChoiceKind.TrainerCardPick) != null)) Stage.Open(new SetupPickScreen(this));
            else if (Stage.Current is SetupDropScreen || Stage.Current is SetupVillageScreen) Stage.Shut();
            // 정보 창 아래의 창이 닫혀 보드만 남았다: 정보 창을 화면 자리로 옮긴다 — 보드에서 연 것처럼 조작을 받는다(결정 125). 전원 차단 연출 동안은 그대로(정비 판은 보기만)
            if (Stage.Info != null && Stage.Current == null && OnBoard && !FxBlocksMaint)
            {
                var info = Stage.Info;
                Stage.ShutInfo();
                Stage.Open(info);
            }
            Restage();
            Hud?.Refresh();
            (Stage.Current as BattleScreen)?.Refresh();
            (Stage.Current as EncounterScreen)?.Refresh();
            (Stage.Current as LogScreen)?.Refresh();
            (Top as PartyScreen)?.Refresh();
            (Top as MonScreen)?.Refresh();
            (Stage.Current as IWorkScreen)?.Refresh();   // 마을(은행상회) · 제작 · 진화 창
            (Stage.Current as TrainerScreen)?.Refresh();
            (Stage.Current as CardRewardScreen)?.Refresh();
            (Stage.Current as CardSwapScreen)?.Refresh();
            (Top as FateScreen)?.Refresh();
            (Stage.Current as StampScreen)?.Refresh();
            (Stage.Current as TradeScreen)?.Refresh();
            (Top as TradePickScreen)?.Refresh();
            (Stage.Current as ChallengeScreen)?.Refresh();
            (Top as BreachScreen)?.Refresh();
            (Top as BlessScreen)?.Refresh();
            Fall(Fx != null ? Fx.Sequencer : null);   // 탈락한 좌석의 말(이어 받은 판 · 손본 판 — 연출이 없어도)
            (Stage.Current as EndingScreen)?.Refresh();
            (Stage.Current as ResultScreen)?.Refresh();
            (Stage.Current as SetupPickScreen)?.Animate();
            (Stage.Current as SetupDropScreen)?.Animate();
            (Stage.Info as PartyScreen)?.Refresh();   // 정보 창 층(결정 125)
            (Stage.Info as MonScreen)?.Refresh();
            (Stage.Info as TrainerScreen)?.Refresh();
            (Stage.Info as FateScreen)?.Refresh();
            SyncWatch();   // 남의 행동 보기 창(결정 120 ① — GameFlow.Watch.cs)
            watch?.Refresh();
            wasOpen = InputOpen;
            wasBoardOpen = BoardOpen;
            wasLook = TokenLook;
        }

        // 고향 마을을 고르고 있는 다른 좌석(없으면 −1 — 공개 선택)
        int OtherVillagePick()
        {
            var view = Client.View;
            for (int s = 0; s < view.Seats.Length; s++)
                if (s != Client.Seat && !Tutorial.Running && SetupVillageScreen.PickOf(view, s) != null) return s;
            return -1;
        }

        // 셋업 연출이 화면을 바꾸는 때를 알아보는 값: 셋업 단계가 대기열에 남았나 · 고향 마을 단계가 남았나 — 바뀌면 화면을 다시 맞춘다(Sync)
        int SetupKey()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            return (SetupRows.Pending(seq) ? 1 : 0) | (SetupRows.Pending(seq, SetupRows.Village) ? 2 : 0);
        }

        /// <summary>
        /// 셋업 화면의 입력이 열려 있나(04 §8-1 P4): 화면 대기열이 비어 클릭 막기 시간이 지났다 — 카드가 들어오는 동안 · 고른 뒤의 연출 동안에는 고르기·[확정]이 듣지 않는다.
        /// </summary>
        public bool SetupOpen => Fx == null || Fx.Sequencer.InputOpen;

        bool wasBoardOpen;

        /// <summary>
        /// 결과 창의 [확인]: 배틀 화면을 닫고 보드로 돌아간다. 세워 둔 화면 대기열을 풀어 턴 넘김(요약 → 띠 → 내 턴 컷신)이 이어지고, 그 뒤에 입력이 열린다.
        /// 배틀이 아직 끝나지 않았거나 배틀 뒤 선택(놓아주기·기술 가르치기)이 아직 열려 있으면 아무것도 하지 않는다.
        /// </summary>
        public void CloseBattle()
        {
            if (!BattleOver || Client.Choice(ChoiceKind.Reward) != null) return;
            WatchingWild = false;
            Battle = null;
            battleEnd = null;
            battleHeld = false;
            if (Stage.Current is BattleScreen) Stage.Shut();
            // 역전환(70행 「가운데 필드가 보드로 돌아오는 역전환」): 줄무늬가 덮은 채로 시작해 걷힌다 — 뒤에 이어지는 턴 넘김과 함께 흐른다(이어질 것이 없으면 생략)
            if (Fx != null && !Fx.Sequencer.IsEmpty)
            {
                wipeBackAt = Fx.Sequencer.NowMs;
                Wipe(Fx.Sequencer);
            }
            if (Fx != null) Fx.SetPaused(false);
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
            // 결과 창이 떠 있는 동안 이미 열린 보드 선택(카드 바꾸기 · 카드가 연 고르기 창 — 배틀이 닫혀야 연다)은 다음 갱신이 오지 않으니 지금 연다(M6.5 클릭 경로 E12 S011 — 07 T152)
            Sync();
        }

        /// <summary>
        /// 받을 화면이 아직 없는 선택 가운데 기본값으로 넘기는 것 — 지금은 한 좌석 판의 낼 카드 없는 운명 창뿐이다(트레이너전 승리 보상 · 사당 카드의 고르기 · 천상의 균열은 그 화면 묶음이 뺐다).
        /// 다른 종류의 선택이면 건드리지 않는다(모르는 선택은 대신 답하지 않는다). 갱신을 받는 도중이 아니라 프레임(<see cref="Tick"/>)에서 낸다.
        /// </summary>
        void PassEmptyReaction()
        {
            if (Client == null) return;
            var choice = Client.FirstChoice();
            // 트레이너 배틀 승리 보상(Reward/TrainerReward, p.9)은 배틀 결과 창이 받는다(M5 「맵 결과·배틀 안 연출」 — BattleScreen.Rewards)
            // 사당 카드의 고르기 · 천상의 균열의 추종자·동률 고르기는 M5 「사당·균열 화면」부터 사당 판(ShrineScreen)이 받는다 — 대신 답하지 않는다
            // 낼 카드가 없는 운명 창(숨은 손패 창 — 답이 「안 씀」 하나뿐)은 한 좌석 판이면 묻지 않고 넘긴다 [가정 plan §5 #99 ④ — 숨길 상대가 없다. 여럿 판은 M7에서 최소 시간 뒤]
            // 각본 튜토리얼의 지금 단계가 운명 반응을 지정 행동으로 받으면 넘기지 않는다 — 사람이 [넘기기]를 누르는 단계다(07 T132)
            if (choice != null && choice.Kind == ChoiceKind.Reaction && choice.Sub == ChoiceSub.Fate && Session != null && Session.SeatCount == 1 && (Fx == null || Fx.Sequencer.IsEmpty) && !TutorialAsks(choice))
            {
                var legal = Client.Legal(choice);
                if (legal.Count == 1 && legal[0].Kind == AnswerKind.Decline)
                {
                    Answer(choice, legal[0]);
                    return;
                }
            }
            // 운명 손패 6장째 버리기(S30) · 운명 반응 창 · 매혹적인 스튜 · 기술 효과의 고르기·「쓸까」(배틀 독) · 거래·도전 응답(S41 — 「행동」 칸 자리)은
            // M5 「운명·응답 화면」부터 화면이 받는다(FateScreen · BattleScreen.AskingFate·AskingAsk · BoardHud.Prompting) — 대신 답하지 않는다
        }

        // 각본 튜토리얼(07 T132): 지금 단계가 지정 행동(command)이고 그 선택 종류의 답을 allow에 둔다
        bool TutorialAsks(PendingChoice choice)
        {
            var step = Tutorial.Runner != null && Tutorial.Runner.Session == Session ? Tutorial.Runner.Step : null;
            return step != null && step.Advance == Sinnoh.Rules.Tutorial.StepAdvance.Command && step.Allow.Exists(p => p.Choice == choice.Kind);
        }

        /// <summary>
        /// 사당 판(<see cref="ShrineScreen"/>)이 받는 선택: 사당 카드의 고르기(Reward, Source = ShrineEvent — ShrinePick·ShrineApricorns·ShrineStews·ShrineExchange·ShrineExchangeCard·ShrineMove·이어지는 TeachMove) ·
        /// 나눌 좌석 SharedBlessing · 천상의 균열의 추종자 비밀 선택과 동률 고르기. 보드 HUD의 안내 줄은 「기다리는 중」이다(판이 받는다).
        /// </summary>
        public static bool ShrineWaits(PendingChoice choice) => (choice.Kind == ChoiceKind.Reward && (choice.Source.Kind == SourceKind.ShrineEvent || choice.Sub == ChoiceSub.SharedBlessing))
            || (choice.Kind == ChoiceKind.Simultaneous && choice.Sub == ChoiceSub.Follower); // 천상의 균열 추종자 선택(rulings A31 — 동률 FractureTie는 Source = ShrineEvent라 위에 든다)

        /// <summary>선택에 답한다(화면이 부른다). 받아들여지면 갱신이 바로 와 화면이 바뀔 수 있다.</summary>
        public SubmitResult Answer(PendingChoice choice, ChoiceAnswer answer)
        {
            answering = choice != null ? choice.Kind : (ChoiceKind?)null;
            try
            {
                var r = Guard(() => Client.Answer(choice, answer, NowMs));
                // 셋업의 비공개 고르기(스타팅 포켓몬 · 트레이너 카드 — [확정]이 둘을 잇달아 보낸다)만: 보낸 순서대로 내 SecretAnswered와 짝짓는다
                if (InRoomGame && r.Accepted && choice != null && (choice.Kind == ChoiceKind.StarterPick || choice.Kind == ChoiceKind.TrainerCardPick)) sentKinds.Enqueue(choice.Kind);
                return r;
            }
            finally
            {
                answering = null;
            }
        }

        // ── 연출 ──

        // 화면 대기열이 프레임마다 부른다(연출이 도는 동안): 걷는 말을 옮기고, HUD의 띠·이동력을 그리고, 입력이 열리고 닫히는 때 HUD를 다시 그린다
        bool fxBegan;   // 지난 ShowFx 뒤로 길이가 있는 항목이 시작됐다

        void ShowFx(FxSequencer seq)
        {
            if (Fx == null || !ReferenceEquals(seq, Fx.Sequencer)) return;   // 떠난 게임의 대기열
            if (openWaiting && !MenuUp) Sync();   // 대기열이 제때에 닿았으면 조우 창·배틀 화면을 연다
            else if (WildAutoOpens) Sync();   // 남의 야생 배틀(결정 120): 내 창이 닫혔다 · 전환이 닿았다 — 가운데 필드로 연다
            else if (SetupKey() != setupKey) Sync();   // 셋업 연출의 단계가 넘어갔다: 마을 띠 → 고르는 화면 → 보드 HUD
            else if (Hud != null && FinaleStage() != finaleShown) Sync();   // 종막 연출의 단계가 넘어갔다: 종막 선택 → 결과 창(95 → 97행) · 순서 공개 뒤 연전
            // 남의 배틀 결과 창(T8 · T16 — 방장 시각 판): 그 뒤 연출(턴 넘김 · 그 사람의 다음 행동)이 시작되면 내가 [확인]을 누르지 않아도 닫힌다
            if (HostPaced && BattleOver && !Battle.Mine && seq.Current != null && !ReferenceEquals(seq.Current, battleEnd)) CloseBattle();
            DropPiece(seq);
            DropSpots(seq);
            Walk(seq);
            Fly(seq);
            Warp(seq);
            Gate(seq);
            UbFocus(seq);
            Fall(seq);
            Respawn(seq);
            TurnFocus(seq);
            Gather(seq);
            if (spotsLeaving != LeavingSpots(null)) SyncTokens();   // 빠진 숨겨진 장소의 공개 연출이 끝났다(또는 새로 왔다) — 붙잡은 토큰을 치우거나 세운다
            if (flyKey != FlyKey(UbFlying())) SyncTokens();   // 83행 토큰 날기의 칸이 바뀌었다(출발 칸 → 틈 속 → 깨어진 세계)
            RaiseSpot();
            Recentre(seq);
            Approach(seq);
            ActFocus(seq);
            ShrineFocus(seq);
            SyncFigures();
            ResultFocus(seq);
            DropTokens(seq);
            FlyGrunts(seq);
            Pulse(seq);
            Ripple();
            Wipe(seq);
            ShowSheets(seq);
            ShowDeals(seq);
            // 배틀 화면 위의 정비 판: 그 아래에서 연출(상대의 공격 …)이 시작되면 닫는다 — 보이지 않는 곳에서 배틀이 흘러가지 않게 [가정]. 정비의 항목은 길이 0이라 닫지 않는다.
            // 「끄기」는 받은 항목을 한 번에 다 보내 여기서는 재생 중인 항목이 보이지 않으니, 지난 번 뒤로 시작된 항목(fxBegan)도 함께 본다
            if (Stage.Current is BattleScreen over && over.Over != null && (fxBegan || seq.Current != null && seq.Current.LengthMs > 0)) over.ShutOver();
            fxBegan = false;
            (Stage.Current as BattleScreen)?.ShowFx(seq);
            (Stage.Current as EncounterScreen)?.ShowFx(seq);
            (Stage.Current as EndingScreen)?.ShowFx(seq);
            (Stage.Current as ResultScreen)?.ShowFx(seq);
            (Stage.Current as IWorkScreen)?.ShowFx(seq);   // 은행상회 18 · 제작 36·37 · 진화 39행은 그 창이 그린다
            SyncWatch();   // 남 좌석의 것은 보기 창이 그린다(결정 120 ①)
            watch?.ShowFx(seq);
            (Stage.Current as SetupPickScreen)?.Animate();
            (Stage.Current as SetupDropScreen)?.Animate();
            bool boardOpen = BoardOpen;
            if (boardOpen != wasBoardOpen) (Stage.Current as IWorkScreen)?.Refresh();   // 클릭 막기가 끝났다: 마을 · 제작 · 진화 창의 단추를 다시 켠다
            if (boardOpen != wasBoardOpen) (Stage.Current as CardSwapScreen)?.Refresh();
            if (boardOpen != wasBoardOpen) (Stage.Current as CardRewardScreen)?.Refresh();   // 카드가 연 놓아주기·가르치기 창도
            if (boardOpen != wasBoardOpen) (Top as BreachScreen)?.Refresh();   // 균열 카드 창 · 축복 창(M6 「UB 화면 묶음 2」)
            if (boardOpen != wasBoardOpen) (Top as BlessScreen)?.Refresh();
            wasBoardOpen = boardOpen;
            var hud = Hud;
            if (hud == null) return;
            hud.ShowFx(seq);
            (Results ?? (Results = new ResultFx(this))).Show(seq, Stage.Root != null ? Stage.Root.Q("board-hud") : null);   // 맵 결과(23·26~30·80행 — M5 「맵 결과·배틀 안 연출」)
            hud.ShowStacks();   // 걷는 말이 묶음에서 빠지고 닿아 묶이는 것 · 따라가는 카메라에 맞춘다
            bool open = InputOpen;
            // 막 안내 창(결정 101 ②): 막 카드 연출 · 막 목표 달성의 연출이 다 지나 보드 입력이 열리면 저절로 뜬다(사람이 하는 판만 — ActNotice, 튜토리얼 판은 마박사가 말한다)
            if (open && ActNotice && (noticeAct >= 0 || noticeGot >= 0) && !Tutorial.Running && !MenuUp && Client.View.Phase != Phase.GameOver && OpenAct(true)) open = InputOpen;
            int look = TokenLook;
            if (open != wasOpen || look != wasLook) hud.Refresh();   // 입력이 열리고 닫혔다 · 조무래기가 놓였다 · 숨겨진 장소가 뒤집혔다(칸 정보 줄 · 공개 정보 카드)
            wasOpen = open;
            wasLook = look;
        }

        // 걷기(04 §8-8 11행): 지금 항목의 걸음 단계에 맞춰 말을 칸 사이에 놓는다. 대기열이 비면 보기의 칸(칸 안 자리)에 다시 세운다
        void Walk(FxSequencer seq)
        {
            if (board == null || board.Pieces == null || Client == null || Client.View == null) return;
            var entry = seq.Current;
            if (entry == null)
            {
                if (seq.IsEmpty) Arrange();
                return;
            }
            FxStep walk = null;
            foreach (var s in entry.Steps)
                if (s.Row == 110 && s.Events.Count > 0 && (walk == null || s.StartMs <= seq.LocalMs)) walk = s;
            if (walk == null) return;
            var m = (Moved)walk.Events[0];
            var pose = FxWalk.At(m, walk.Local(seq.LocalMs));
            var ground = Vector3.Lerp(board.CellTop(m.Path[pose.From]), board.CellTop(m.Path[pose.To]), pose.T);
            // 직업 그림 말은 걷기 그림이 없어 칸마다 통통 튀고, 주인공 도트 말은 튀지 않고 가는 쪽을 보며 걷기 그림이 돈다(04 §3-1)
            var piece = board.Pieces.Of(m.Seat);
            bool dot = piece != null && piece.Style == PieceStyle.Dot;
            // 걸음이 끝났고 도착 칸에 다른 말이 있으면 묶인다(결정 66 ① — 걷는 동안은 묶음에서 빠져 따로 걷는다). 혼자 서는 칸이면 지금까지처럼 대기열이 빌 때 세운다
            int end = m.Path[m.Path.Length - 1];
            if (seq.LocalMs >= walk.StartMs + walk.Ms && Shared(m.Seat, end)) board.Pieces.Land(m.Seat, end);
            else
            {
                board.Pieces.Move(m.Seat, dot ? ground : ground + Vector3.up * (pose.Hop * BoardHud.HopHeight));
                if (dot && FieldWalk.Leg(pose, out int a, out int b)) board.Pieces.Pose(m.Seat, Face(board.CellTop(m.Path[b]) - board.CellTop(m.Path[a])), FieldWalk.Cycle(pose));
            }
            Follow(walk, ground, walk.Local(seq.LocalMs));   // 카메라는 튀는 높이를 빼고 따라간다(화면이 걸음마다 출렁이지 않게)
        }

        /// <summary>맵 결과 연출 층(숨겨진 장소 결과 26~30 · 전설 장소 23 · 신 격파 80행 — HUD 판 위). 연출이 한 번도 돌지 않았으면 null.</summary>
        public ResultFx Results { get; private set; }

        /// <summary>보드 위 도트 이펙트 판(결정 69 — 착지 먼지 · 숨겨진 장소 공개 반짝임): 화면 뿌리의 가장 아래 층(<see cref="UiStage.Under"/> — HUD 판·창이 덮는다). 연출이 한 번도 돌지 않았으면 null.</summary>
        public SheetLayer Sheets { get; private set; }

        // 보드 위 도트 이펙트(결정 69, SheetBeats): 지금 항목 + 막 끝난 항목의 꼬리(착지 먼지가 항목 끝에 걸친다 — 시퀀서 시각으로 마저 돈다)
        void ShowSheets(FxSequencer seq)
        {
            if (Sheets == null) Sheets = new SheetLayer();
            Stage.SetUnder(Sheets.Element);   // 뿌리에 직접 넣지 않는다 — 판·창의 순서는 무대가 지킨다(HUD가 먼저 뜬 이어 받은 판에서도 맨 아래)
            Sheets.Begin();
            // 우두머리 공개는 같은 명령에서 배틀이 시작돼(Battle이 이미 있다) 공개 단계(25행) 동안에도 보드의 반짝임을 그린다 — 배틀 화면은 그 뒤 전환(22행)에서 열린다
            if (board != null && board.Pieces != null && rig != null && laidFor.x > 0 && (Battle == null || SpotBeats.StepOf(seq.Current) != null))
            {
                if (seq.Current != null) BoardSheets(seq.Current, seq.LocalMs);
                var last = seq.Last;
                if (last != null && !ReferenceEquals(last, seq.Current) && seq.SinceLastMs < 2000) BoardSheets(last, last.LengthMs + (int)seq.SinceLastMs);
            }
            Sheets.End();
        }

        void BoardSheets(FxEntry entry, int local)
        {
            // 걷기 끝(11행)에는 먼지가 없다(결정 70 ⑬ ⑥ — 떨어질 때·내려앉을 때만)
            var fly = FlyBeats.StepOf(entry);
            if (fly != null)
            {
                var f = FlyBeats.Flight(fly);
                if (f.To >= 0 && f.To < board.Board.Count) SheetAt(SheetBeats.Dust, local - fly.EndMs, Feet(f.Seat, f.To));
            }
            var home = VillageBeats.StepOf(entry);
            if (home != null)
            {
                int cell = VillageBeats.Picked(home).Cell;
                if (cell >= 0 && cell < board.Board.Count) SheetAt(SheetBeats.Dust, local - home.StartMs - SheetBeats.VillageLandMs(home.Ms), Feet(VillageBeats.Picked(home).Seat, cell));
            }
            var spot = SpotBeats.StepOf(entry);
            int at = SpotBeats.Cell(spot, out bool reveal);
            if (reveal && at >= 0 && at < board.Board.Count) SheetAt(SheetBeats.Star, local - spot.StartMs - SheetBeats.SpotFaceMs(spot.Ms), board.CellTop(at) + Vector3.up * SpotHover(at));
        }

        // 먼지가 이는 자리: 그 좌석의 말이 그 칸에 내려선 발 점(말이 없으면 칸 윗면 가운데)
        Vector3 Feet(int seat, int cell)
        {
            var piece = board.Pieces.Of(seat);
            var top = board.CellTop(cell);
            if (piece == null || piece.Root == null) return top;
            var at = piece.Root.transform.position;
            return new Vector3(at.x, top.y, at.z);
        }

        // 월드의 한 점에 이펙트 하나: 텍셀 = 주인공 도트 말의 정수배(줌 단계마다 1·3·5 × 화면 높이)
        void SheetAt(string key, int ms, Vector3 world)
        {
            if (ms < 0 || !BoardPick.ScreenAt(rig.GetComponent<Camera>(), world, laidFor.x, laidFor.y, out var px)) return;
            float scale = Stage.Grade.PanelScale;
            int k = BoardPieces.Scale(BoardPieces.KFor(PieceStyle.Dot, BoardPieces.ZoomK(rig.CurrentZoom)), laidFor.y);
            Sheets.Add(key, ms, Mathf.Floor(px.x) / scale, (laidFor.y - Mathf.Floor(px.y)) / scale, k / scale);
        }

        /// <summary>공중날기의 호 높이(월드 유닛): 두 마을 사이 거리 × <see cref="FlyArc"/>, <see cref="FlyMinHeight"/> ~ <see cref="FlyMaxHeight"/> [가정].</summary>
        public const float FlyArc = 0.25f, FlyMinHeight = 1.5f, FlyMaxHeight = 5f;

        /// <summary>피규어 표의 공중날기 택시 묶음(찌르호크 — 원작 TTS 장막시티의 택시 표시).</summary>
        public const string TaxiFigures = "Taxi";

        /// <summary>
        /// 공중날기 찌르호크(결정 126 · T10 · T18 — Pt 본편 공중날기처럼): 말 머리 핀 위 <see cref="BirdAbove"/>에 붙어 날고, 떠오름 동안 하늘(<see cref="BirdSwoop"/> 위)에서 내려와 태우고,
        /// 내려앉음 동안 말을 내려 주고 다시 하늘로 올라간다(길이는 16행 그대로 — 새 행 · 소리 없음 [가정 plan §5 #642]).
        /// </summary>
        public const float BirdAbove = 0.2f, BirdSwoop = 5f;

        bool taxiFlying;

        /// <summary>찌르호크가 지금 날고 있나 · 그 높이(판 지역 좌표 y — 확인용, 없으면 NaN).</summary>
        public bool BirdShown(out float y)
        {
            y = float.NaN;
            var bird = TaxiFigure();
            if (bird == null || bird.Root == null || !bird.Root.activeSelf) return false;
            y = bird.Root.transform.localPosition.y;
            return true;
        }

        /// <summary>
        /// 창 위의 고르기가 보드의 칸을 보여 준다(T18 공중날기 목적지 칩 — 마우스가 창 위에 있어 끌기와 다투지 않는다): 카메라의 보는 점만 그 칸으로(방향 · 줌 그대로). 끄는 중 · 연출 중이면 하지 않는다.
        /// </summary>
        public bool PeekCell(int cell)
        {
            if (rig == null || board == null || cell < 0 || cell >= board.Board.Count || drag.Panning || (Fx != null && !Fx.Sequencer.IsEmpty)) return false;
            rig.LookAt(board.CellTop(cell));
            return true;
        }

        BoardFigures.Figure TaxiFigure()
        {
            if (board == null || board.Figures == null) return null;
            foreach (var f in board.Figures.Figures)
                if (f.Group == TaxiFigures) return f;
            return null;
        }

        // 공중날기(04 §8-8 16행): 찌르호크가 내려와 말을 태우고 떠올라 도착 마을까지 호를 그리며 날아가 내려 준다. 카메라의 보는 점은 땅 위 점을 따라간다
        void Fly(FxSequencer seq)
        {
            taxiFlying = false;
            var step = FlyBeats.StepOf(seq.Current);
            if (step == null || board == null || board.Pieces == null || seq.LocalMs < step.StartMs) return;
            var f = FlyBeats.Flight(step);
            if (f.From < 0 || f.To < 0 || f.From >= board.Board.Count || f.To >= board.Board.Count) return;
            int local = step.Local(seq.LocalMs);
            var pose = FlyBeats.At(local, step.Ms);
            Vector3 a = board.CellTop(f.From), b = board.CellTop(f.To);
            var ground = Vector3.Lerp(a, b, pose.T);
            float peak = Mathf.Clamp(Vector3.Distance(a, b) * FlyArc, FlyMinHeight, FlyMaxHeight);
            if (seq.LocalMs >= step.StartMs + step.Ms && Shared(f.Seat, f.To)) board.Pieces.Land(f.Seat, f.To);   // 내려앉은 마을에 다른 말이 있으면 묶인다
            else board.Pieces.Move(f.Seat, ground + Vector3.up * (pose.Height * peak));
            Follow(step, ground, local);
            var bird = TaxiFigure();
            if (bird == null || local >= step.Ms) return;
            int lift = step.Ms / FlyBeats.LiftPart, land = step.Ms / FlyBeats.LandPart;
            float swoop = local < lift ? 1f - Tween.At(local, 0, lift, 0f, 1f, Ease.OutQuad) : local > step.Ms - land ? Tween.At(local, step.Ms - land, land, 0f, 1f, Ease.InQuad) : 0f;
            taxiFlying = true;
            var carried = ground + Vector3.up * (pose.Height * peak);
            BoardFigures.PlaceAt(bird, carried + Vector3.up * (HeadHeight(f.Seat, carried) + BirdAbove + swoop * BirdSwoop));
        }

        // 그 좌석 말(그 자리에 선)의 머리 핀 높이(월드 유닛): 말은 화면 공간 정수배라 줌마다 월드 높이가 다르다 — 핀 px(BoardPieces.PinTop × 배수)를 그 자리의 px/유닛으로 나눈다
        float HeadHeight(int seat, Vector3 at)
        {
            var piece = board.Pieces != null ? board.Pieces.Of(seat) : null;
            var cam = rig != null ? rig.GetComponent<Camera>() : null;
            if (piece == null || cam == null || laidFor.y <= 0) return 0f;
            int step = BoardPieces.ZoomK(rig.CurrentZoom);
            float headPx = BoardPieces.PinTop(piece, step) * BoardPieces.Scale(piece, step, laidFor.y);
            if (!BoardPick.ScreenAt(cam, at, laidFor.x, laidFor.y, out var p0) || !BoardPick.ScreenAt(cam, at + Vector3.up, laidFor.x, laidFor.y, out var p1)) return 0f;
            float perUnit = Mathf.Abs(p1.y - p0.y);
            return perUnit > 0.01f ? headPx / perUnit : 0f;
        }

        // 그 칸에 그 좌석 말고 다른 좌석이 서 있나(보기)
        // 귀환(04 §8-8 71행): 보드가 다 어두운 동안 말이 고향 마을에 다시 서고, 카메라의 보는 점이 그리로 간다(어둠은 보드 HUD가 그린다 — BoardHud.ShowFx)
        void Respawn(FxSequencer seq)
        {
            var step = WhiteoutBeats.StepOf(seq.Current);
            if (step == null || board == null || board.Pieces == null || seq.LocalMs < step.StartMs) return;
            var w = WhiteoutBeats.Event(step);
            if (w.ToCell < 0 || w.ToCell >= board.Board.Count) return;
            int local = step.Local(seq.LocalMs), home = step.Ms * WhiteoutBeats.HomePermille / 1000;
            if (!WhiteoutBeats.At(local, step.Ms).Home) return;
            board.Pieces.Land(w.Seat, w.ToCell);
            Follow(step, board.CellTop(w.ToCell), local - home);
        }

        // 턴 시작(6행 — 여럿 판): 남의 턴이면 카메라의 보는 점이 그 좌석의 말로 간다(내 턴은 101행 컷신이 내 말로 돌아온다 — Recentre)
        void TurnFocus(FxSequencer seq)
        {
            var step = TurnStartBeats.StepOf(seq.Current);
            if (step == null || board == null || Client == null || Client.View == null || seq.LocalMs < step.StartMs || !(step.Tag is TurnStartInfo info) || info.Mine) return;
            int cell = info.Seat >= 0 && info.Seat < Client.View.Seats.Length ? Client.View.Seats[info.Seat].Cell : -1;
            if (cell < 0 || cell >= board.Board.Count) return;
            Follow(step, board.CellTop(cell), step.Local(seq.LocalMs));
        }

        /// <summary>규토리 줍기(19행)에서 말이 툭 내미는 높이 · 흔들리는 폭(월드 유닛) [가정].</summary>
        public const float GatherHop = 0.25f, GatherShake = 0.08f;

        // 규토리 줍기(04 §8-8 19행): 말이 툭 내밀고 두 번 흔들린다(떨어지는 규토리 · 「규토리 +n」은 보드 HUD가 그린다). 묶음 칸에 선 말은 그대로 둔다
        void Gather(FxSequencer seq)
        {
            var step = ApricornBeats.StepOf(seq.Current);
            if (step == null || board == null || board.Pieces == null || seq.LocalMs < step.StartMs) return;
            int seat = ApricornBeats.Seat(step, out int cell);
            if (seat < 0 || cell < 0 || cell >= board.Board.Count || Shared(seat, cell)) return;
            var pose = ApricornBeats.At(step.Local(seq.LocalMs), step.Ms);
            var right = rig != null ? rig.transform.right : Vector3.right;
            right.y = 0f;
            board.Pieces.Move(seat, board.CellTop(cell) + Vector3.up * (pose.Nudge * GatherHop) + right * (pose.Shake * GatherShake));
        }

        /// <summary>그 칸에 그 좌석 말고 다른 좌석의 말이 서 있나(보기) — 같은 칸의 말을 만남(13행)의 「!」가 본다.</summary>
        public bool SharedCell(int seat, int cell) => Shared(seat, cell);

        /// <summary>
        /// 울음 단서의 울음 번호(M9 소리 ③): 포켓몬 표 색인이 있으면 그 종, 없으면 보기에서 그 포켓몬의 표 색인으로(모르면 0 — 울음 없음).
        /// 폼 울음이 있는 폼(<see cref="CryForm"/> — 울트라 네크로즈마 등, 결정 143 ④)은 그 폼의 번호. 포켓몬 Id가 <see cref="SoundCues.ShownNecrozma"/>면 지금 페이즈 · 썬/문의 네크로즈마 폼.
        /// </summary>
        public int CryDex(int pokemonId, int def)
        {
            if (Ctx == null) return 0;
            if (pokemonId == SoundCues.ShownNecrozma && Client != null && Client.View.Ub != null)
            {
                // 피규어와 같은 폼(UbBoard.NecrozmaForm): 2페이즈라도 썬/문 동전을 던지기 전은 1페이즈 모습 그대로 [가정 #UB-시작-③]
                var ub = Client.View.Ub;
                def = UbNecrozma.Card(Ctx, ub.NecrozmaPhase == 2 && ub.SunMoon < 0 ? 1 : ub.NecrozmaPhase, ub.SunMoon);
            }
            else if ((def < 0 || def == GameEvent.U) && Client != null && pokemonId >= 0 && pokemonId < Client.View.Pokemon.Count) def = Client.View.Pokemon[pokemonId].Def;
            if (def < 0 || def >= Ctx.Cards.Pokemon.Count) return 0;
            int form = CryForm != null ? CryForm(Ctx.Cards.Pokemon[def].Key) : 0;
            return form > 0 ? form : Ctx.Cards.Pokemon[def].Dex;
        }

        /// <summary>폼 울음(결정 143 ④): 포켓몬 표 키 → 울음 번호(없으면 0 — 종의 울음). 재생 층의 울음 표(<see cref="CryBook.Form"/>)를 <see cref="ScreenBoot"/>가 잇는다(없으면 늘 종의 울음).</summary>
        public Func<string, int> CryForm;

        bool Shared(int seat, int cell)
        {
            var view = Client != null ? Client.View : null;
            if (view == null) return false;
            for (int s = 0; s < view.Seats.Length; s++)
                if (s != seat && view.Seats[s].Cell == cell && !view.Seats[s].Eliminated) return true;
            return false;
        }

        // 숨겨진 장소 토큰(04 §8-8 25행): 멈춰 서서 묻는 동안 떠 있고, [공개]면 뒤집히고 [지나가기]면 내려앉는다. 모습은 대기열 시각과 열린 선택, 보기에서만 나온다.
        // 공개한 앞면은 말이 그 칸에 서 있는 동안 머리 위에 떠 있다 [가정] — 내려앉으면 그 칸에 선 말에 깔려 무엇을 공개했는지 그림으로 읽을 수 없다(검토 반영)
        void RaiseSpot()
        {
            if (board == null || board.Tokens == null) return;
            int cell = -1;
            float lift = 0f, flip = 0f;
            var seq = Fx != null ? Fx.Sequencer : null;
            // 83행 토큰 날기(831): 재생 중인 단계의 토큰이 떠오르고 깨어진 세계에서 내려앉는다(다른 띄우기보다 먼저 — UB 턴은 입력이 닫혀 있다)
            if (seq != null && seq.Current != null)
                foreach (var s in seq.Current.Steps)
                {
                    var m = SpotFlyBeats.Event(s);
                    if (m == null || seq.LocalMs < s.StartMs || seq.LocalMs >= s.EndMs) continue;
                    var pose = SpotFlyBeats.At(m, seq.LocalMs - s.StartMs, s.Ms);
                    bool up = pose.Cell >= 0 && Client != null && Client.View.Tokens.Exists(t => t.Kind == TokenKind.HiddenSpot && t.Cell == m.To && t.FaceUp);
                    board.Tokens.Raise(pose.Cell, pose.Cell >= 0 ? pose.Height * SpotHover(pose.Cell) : 0f, up ? 1f : 0f);
                    board.Tokens.RaisedSwell = 1f;
                    return;
                }
            if (seq != null)
                foreach (var entry in seq.Queued)
                {
                    var step = SpotBeats.StepOf(entry);
                    int c = SpotBeats.Cell(step, out bool reveal);
                    if (c < 0) continue;
                    var pose = SpotBeats.At(ReferenceEquals(entry, seq.Current) ? seq.LocalMs - step.StartMs : 0, step.Ms, reveal);
                    cell = c;
                    lift = reveal && Stood(c) ? Math.Max(pose.Lift, 1f) : pose.Lift;
                    flip = pose.Flip;
                    break;
                }
            if (cell < 0 && Battle == null && Client != null && Client.View != null && (seq == null || seq.IsEmpty))
            {
                var choice = Client.Choice(ChoiceKind.HiddenSpot);
                if (choice != null && choice.Subject >= 0 && choice.Subject < Client.View.Tokens.Count)
                {
                    cell = Client.View.Tokens[choice.Subject].Cell;
                    lift = 1f;
                }
            }
            if (cell < 0 && Battle == null && Client != null && Client.View != null && !Travelling(seq))
            {
                // 공개된 숨겨진 장소에 선 말(내 말 먼저): 앞면이 머리 위에 떠 있다
                var view = Client.View;
                for (int k = 0; k < view.Seats.Length && cell < 0; k++)
                {
                    int s = k == 0 ? Client.Seat : k <= Client.Seat ? k - 1 : k;
                    if (view.Seats[s].Eliminated || view.Seats[s].Cell < 0) continue;
                    foreach (var t in view.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && !t.Destroyed && t.Cell == view.Seats[s].Cell) { cell = t.Cell; lift = 1f; flip = 1f; break; }
                }
            }
            board.Tokens.Raise(cell, cell >= 0 ? lift * SpotHover(cell) : 0f, flip);
            // 결과 연출(26~30행): 그 단계 동안 토큰이 부풀거나 접혔다 펴진다
            var result = seq != null ? ResultRows.SpotStep(seq.Current) : null;
            board.Tokens.RaisedSwell = result != null && seq.LocalMs >= result.StartMs && seq.LocalMs < result.EndMs
                ? SpotResultBeats.At(ResultRows.EventOf<SpotResolved>(result), seq.LocalMs - result.StartMs, result.Ms).Swell : 1f;
        }

        // 그 칸에 말이 서 있나(보기)
        bool Stood(int cell)
        {
            var view = Client != null ? Client.View : null;
            if (view == null) return false;
            foreach (var seat in view.Seats) if (seat.Cell == cell && !seat.Eliminated) return true;
            return false;
        }

        // 걷기·공중날기가 대기열에 남았나(보기의 칸은 이미 도착 칸이다 — 말이 닿기 전에는 그 칸의 토큰을 띄우지 않는다)
        static bool Travelling(FxSequencer seq)
        {
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
                if (entry.Step(110) != null || FlyBeats.StepOf(entry) != null) return true;
            return false;
        }

        /// <summary>떠 있는 숨겨진 장소 토큰과 핀 사이 틈(월드 유닛).</summary>
        public const float SpotGap = 0.15f;

        /// <summary>
        /// 그 칸에서 숨겨진 장소 토큰이 떠 있는 높이(월드 유닛, 칸 윗면 위 — 토큰 가운데): 그 칸에 선 말의 좌석 번호 핀 바로 위.
        /// 말은 화면 px(정수배)로 그려 줌마다 월드 높이가 다르므로 지금 카메라로 센다. 보드 없이 도는 흐름이거나 말이 없으면 <see cref="BoardTokens.HoverHeight"/>, 그보다 낮아지지 않는다.
        /// </summary>
        public float SpotHover(int cell)
        {
            float low = BoardTokens.HoverHeight;
            var view = Client != null ? Client.View : null;
            if (view == null || board == null || board.Pieces == null || rig == null || laidFor.x <= 0 || cell < 0 || cell >= board.Board.Count) return low;
            int zoom = BoardPieces.ZoomK(rig.CurrentZoom);
            float head = 0f;
            for (int s = 0; s < view.Seats.Length; s++)
            {
                var piece = view.Seats[s].Cell == cell ? board.Pieces.Of(s) : null;
                if (piece != null) head = Mathf.Max(head, BoardPieces.PinTop(piece, zoom) * BoardPieces.Scale(piece, zoom, laidFor.y));
            }
            if (head > 0f) head += StackLift(cell);   // 묶음 표지가 뜬 칸이면 그 위
            var cam = rig.GetComponent<Camera>();
            var top = board.CellTop(cell);
            if (head <= 0f || !BoardPick.ScreenAt(cam, top, laidFor.x, laidFor.y, out var a) || !BoardPick.ScreenAt(cam, top + Vector3.up, laidFor.x, laidFor.y, out var b) || b.y - a.y < 1f) return low;
            // 토큰은 카메라를 보고 선다: 반 변이 화면에서 차지하는 높이를 월드 높이로 바꾸면 반 변 / cos(내려다보는 각)
            float half = BoardTokens.SpotSize * 0.5f * (rig.CurrentZoom == BoardCameraRig.Zoom.Full ? BoardTokens.FullZoomScale : 1f) / Mathf.Cos(BoardCameraRig.Tilt * Mathf.Deg2Rad);
            return Mathf.Max(low, head / (b.y - a.y) + half + SpotGap);
        }

        // 땅 위에서 가는 쪽 → 카메라 기준 4방향(카메라가 없으면 판의 +x가 오른쪽, +z가 안쪽)
        Facing Face(Vector3 step)
        {
            if (rig == null) return FieldWalk.Face(step.x, step.z);
            var right = rig.transform.right;
            var away = rig.transform.forward;
            away.y = 0f;
            return FieldWalk.Face(step.x * right.x + step.z * right.z, step.x * away.x + step.z * away.z);
        }

        // ── 카메라 따라가기(결정 57 ①, 04 §3-2 안 ③) ──

        /// <summary>
        /// 카메라의 보는 점을 그 단계의 따라갈 점으로 옮긴다: 단계를 처음 볼 때의 보는 점에서 <see cref="CameraCatchMs"/>에 걸쳐 다가가고 그 뒤로는 그 점에 붙는다.
        /// 방향(Q·E)·줌은 건드리지 않는다. 시각은 연출 시각뿐이다(같은 항목·같은 ms면 같은 자리 — 단계가 시작되기 전에는 카메라가 서 있으므로 나눠 보내도 같다).
        /// 판 가장자리 제한은 없다(카메라에 그런 제한이 없다 — 가장자리 칸에 선 말도 화면 가운데에 온다).
        /// </summary>
        void Follow(FxStep step, Vector3 point, int localMs)
        {
            if (rig == null) return;
            // 내가 보드를 끌고 있거나 이 단계 동안 끌었으면 따라가기를 쉰다 — 다음 단계(다음 걸음·내 턴 컷신)가 지금 보는 점에서 다시 다가간다(결정 66 ②)
            if (drag.Panning) heldStep = step;
            if (ReferenceEquals(step, heldStep)) return;
            if (!ReferenceEquals(step, camStep))
            {
                camStep = step;
                camFrom = rig.Target;
            }
            rig.LookAt(Vector3.Lerp(camFrom, point, Tween.At(localMs, 0, Math.Min(CameraCatchMs, step.Ms), 0f, 1f, Ease.OutQuad)));
        }

        // 내 턴 컷신(101행) 동안 카메라가 내 말로 돌아온다 [가정] — 남의 걷기를 따라가 있던 카메라를 내 턴에 내 말로. 솔로에서는 이미 내 말을 보고 있어 움직이지 않는다
        void Recentre(FxSequencer seq)
        {
            var mine = seq.Current?.Step(FxRows.MyTurn);
            if (mine == null || seq.LocalMs < mine.StartMs || !MyCell(out var at)) return;
            // 게임 시작에서 전체 줌으로 물러나 있었으면(숨겨진 장소 배치 5행) 지역 줌으로 내 말에 다가간다
            if (wide) Glide(mine, BoardCameraRig.Zoom.Region, at, Tween.At(mine.Local(seq.LocalMs), 0, Math.Min(CameraCatchMs, mine.Ms), 0f, 1f, Ease.InOutQuad));
            else Follow(mine, at, mine.Local(seq.LocalMs));
        }

        // 카메라가 그 단계 동안 줌 단계 사이를 미끄러진다(셋업 연출 — 2·5행): 단계를 처음 볼 때의 보는 점·줌에서 t(0 ~ 1)만큼. 방향은 그대로다.
        // 시각은 연출 시각뿐이다(같은 항목·같은 ms면 같은 자리)
        void Glide(FxStep step, BoardCameraRig.Zoom to, Vector3 point, float t)
        {
            if (rig == null) return;
            if (!ReferenceEquals(step, camStep))
            {
                camStep = step;
                camFrom = rig.Target;
                camZoom = rig.CurrentZoom;
            }
            // 내가 보드를 끌고 있거나 이 단계 동안 끌었으면 보는 점은 두고 줌 거리만 미끄러진다(Follow와 같다 — 결정 66 ②)
            if (drag.Panning) heldStep = step;
            rig.Glide(camZoom, to, t, ReferenceEquals(step, heldStep) ? rig.Target : Vector3.Lerp(camFrom, point, t));
        }

        // 전체 줌으로 놓아 둔 자리를 지금 HUD·창 크기·방향에 다시 맞춘다(프레임마다 — 놓을 때 HUD가 아직 자리를 잡지 않았을 수 있다). 끌었거나 줌을 바꿨으면 두지 않는다
        void Rewide()
        {
            if (spotsDropping && Fx != null) DropSpots(Fx.Sequencer);   // 미끄러지는 중이면 같은 시각으로 다시 놓는다
            Reaim();   // T18 ①: 보드에서 공중날기 목적지를 고르는 동안의 전체 줌 — 오른쪽 기둥 창 왼쪽에 판이 오게
            if (!wide || !wideAimed || rig == null || camStep != null || rig.CurrentZoom != BoardCameraRig.Zoom.Full || rig.Target != wideAt) return;
            var look = WideLook();
            if (look == wideAt) return;
            wideAt = look;
            rig.LookAt(look);
        }

        /// <summary>
        /// 전체 줌으로 판을 볼 때의 보는 점(게임 시작의 숨겨진 장소 배치 ~ 1막 카드 — 04 §8-8 5행): 보이는 칸들이 화면에서 차지하는 네모의 가운데가
        /// 위 띠와 아래 판 사이의 가운데에 오는 자리다(화면 한가운데에 두면 판 아래쪽이 아래 판에 가린다). HUD가 아직 자리를 잡지 않았으면 화면 전체의 가운데.
        /// right = 판을 놓을 오른쪽 끝(물리 px — 오른쪽 기둥 창의 왼쪽 끝, 0 이하면 화면 끝).
        /// </summary>
        public Vector3 WideLook(double right = 0.0)
        {
            if (rig == null || board == null || laidFor.x <= 0) return Vector3.zero;
            bool distortion = board.DistortionWorldVisible;
            var pts = new List<double>();
            for (int c = 0; c < board.Board.Count; c++)
            {
                if (board.Board[c].DistortionWorld && !distortion) continue;
                var p = board.CellTop(c);
                pts.Add(p.x);
                pts.Add(p.y);
                pts.Add(p.z);
            }
            float scale = Stage.Grade.PanelScale;
            double bottom = 0.0, top = laidFor.y;
            var root = Stage.Root;
            var dock = root != null ? root.Q("board-hud-dock") : null;
            var status = root != null ? root.Q("board-hud-status") : null;
            if (dock != null && dock.worldBound.height > 0f) bottom = laidFor.y - dock.worldBound.yMin * scale;
            if (status != null && status.worldBound.height > 0f) top = laidFor.y - status.worldBound.yMax * scale;
            if (top <= bottom) { bottom = 0.0; top = laidFor.y; }
            BoardDrag.Frame(pts.ToArray(), 0.0, BoardCameraRig.Tilt, rig.Yaw * 60.0, BoardCameraRig.FieldOfView, BoardCameraRig.Distance(BoardCameraRig.Zoom.Full), laidFor.x, laidFor.y, 0.0, bottom, right > 0.0 ? right : laidFor.x, top, out double x, out double z);
            return new Vector3((float)x, 0f, (float)z);
        }

        /// <summary>고향 마을 고르기(2행)에서 말이 떨어지기 시작하는 높이(월드 유닛, 칸 윗면 위) [가정].</summary>
        public const float VillageDropHeight = 12f;

        // 고향 마을 고르기(04 §8-8 2행): 카메라가 고른 마을로 미끄러지고(전체 줌 → 지역 줌) 말이 위에서 떨어져 내려앉아 한 번 튄다
        void DropPiece(FxSequencer seq)
        {
            var step = VillageBeats.StepOf(seq.Current);
            if (step == null || board == null || seq.LocalMs < step.StartMs) return;
            var picked = VillageBeats.Picked(step);
            if (picked.Cell < 0 || picked.Cell >= board.Board.Count) return;
            int t = seq.LocalMs - step.StartMs;
            var pose = VillageBeats.At(t, step.Ms);
            var top = board.CellTop(picked.Cell);
            Glide(step, BoardCameraRig.Zoom.Region, top, pose.Glide);
            if (board.Pieces == null) return;
            if (t >= step.Ms) board.Pieces.Land(picked.Seat, picked.Cell);
            else if (pose.Fall >= 0f) board.Pieces.Move(picked.Seat, top + Vector3.up * ((1f - pose.Fall) * VillageDropHeight + pose.Hop * BoardHud.HopHeight));
        }

        // 고향 마을 연출(2행)에서 아직 내려앉지 않은 말은 세우지 않는다(떨어지는 말은 DropPiece가 옮긴다)
        void HideDropping(List<int> cells)
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return;
            foreach (var entry in seq.Queued)
            {
                var step = VillageBeats.StepOf(entry);
                if (step == null) continue;
                int seat = VillageBeats.Picked(step).Seat;
                if (seat >= 0 && seat < cells.Count && (!ReferenceEquals(entry, seq.Current) || seq.LocalMs < step.EndMs)) cells[seat] = -1;
            }
        }

        // 게임 시작의 숨겨진 장소 배치(04 §8-8 5행): 카메라가 전체 줌으로 물러나고 「?」 토큰이 칸 색인 순서로 떨어져 내려앉는다
        void DropSpots(FxSequencer seq)
        {
            if (spotsHeld != SpotsPending()) SyncTokens();
            var step = SpotDropBeats.StepOf(seq.Current);
            var placed = step != null && seq.LocalMs >= step.StartMs && seq.LocalMs < step.EndMs ? SpotDropBeats.Spots(step) : null;
            if (placed == null)
            {
                if (spotsDropping && board != null && board.Tokens != null) board.Tokens.LandSpots();
                spotsDropping = false;
                return;
            }
            int t = seq.LocalMs - step.StartMs, n = placed.Cells.Length, total = step.Ms;
            Glide(step, BoardCameraRig.Zoom.Full, WideLook(), SpotDropBeats.Wide(t));
            wide = true;
            if (board == null || board.Tokens == null) return;
            var cells = placed.Cells;
            board.Tokens.DropSpots(c => SpotDropBeats.Fall(t, total, Array.IndexOf(cells, c), n), c => SpotDropBeats.Squash(t, total, Array.IndexOf(cells, c), n));
            spotsDropping = true;
        }

        // 게임 시작의 숨겨진 장소 배치 단계가 대기열에서 아직 시작되지 않았나(그동안 숨겨진 장소 토큰은 보드에 없다)
        bool SpotsPending()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
            {
                var step = SpotDropBeats.StepOf(entry);
                if (step != null && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs)) return true;
            }
            return false;
        }

        // 조우 전환(22행)의 예비: 카메라의 보는 점이 배틀이 벌어지는 칸으로 다가간다(방향·줌은 그대로 — 결정 57)
        void Approach(FxSequencer seq)
        {
            var step = seq.Current != null ? seq.Current.Step(EnterRows.Wipe) : null;
            if (step == null || board == null || rig == null || seq.LocalMs < step.StartMs || step.Events.Count == 0 || !(step.Events[0] is BattleStarted bs)) return;
            if (bs.Cell < 0 || bs.Cell >= board.Board.Count) return;
            Follow(step, board.CellTop(bs.Cell), step.Local(seq.LocalMs));
        }

        // 막 카드(4행)의 예비: 2막부터는 처음 달성한 좌석의 말로 카메라의 보는 점이 다가간다(방향·줌은 그대로 — 결정 57)
        void ActFocus(FxSequencer seq)
        {
            var step = ActBeats.StepOf(seq.Current);
            var card = step != null ? step.Tag as ActCardInfo : null;
            if (card == null || card.FirstSeat < 0 || board == null || rig == null || seq.LocalMs < step.StartMs || Client == null || Client.View == null) return;
            int cell = card.FirstSeat < Client.View.Seats.Length ? Client.View.Seats[card.FirstSeat].Cell : -1;
            if (cell >= 0) Follow(step, board.CellTop(cell), step.Local(seq.LocalMs));
        }

        // 전설 이벤트(33행) · 신 강림(34행)의 예비: 카메라의 보는 점이 그 사당으로 다가간다(방향·줌은 그대로)
        void ShrineFocus(FxSequencer seq)
        {
            if (board == null || rig == null) return;
            var step = LegendBeats.StepOf(seq.Current);
            int cell = step != null && LegendBeats.Event(step) != null ? LegendBeats.Event(step).Cell : -1;
            if (cell < 0)
            {
                step = DescendBeats.StepOf(seq.Current);
                cell = step != null && DescendBeats.Event(step) != null ? DescendBeats.Event(step).Cell : -1;
            }
            if (step == null || cell < 0 || cell >= board.Board.Count || seq.LocalMs < step.StartMs) return;
            Follow(step, board.CellTop(cell), step.Local(seq.LocalMs));
        }

        /// <summary>피규어 표의 전설 사당 묶음(전설 일곱 · 신오의 신 셋 — <c>FigureLayout</c>의 group).</summary>
        public const string ShrineFigures = "LegendaryShrine";

        /// <summary>
        /// 전설 사당의 피규어(04 33·34행 — M5 「사당·균열 화면」)는 게임 중에는 그 토큰(전설 · 신)이 사당에 있을 때만 선다: 전설 이벤트 · 신 강림의 연출이 대기열에 남아 있으면
        /// 그 박자(<see cref="LegendBeats"/>·<see cref="DescendBeats"/>)대로 하늘에서 내려앉고, 바꿔 치운 것은 어두워지는 동안 사라진다. 게임 밖 · 셋업에서는 모두 선 채다(시험 판).
        /// 다른 묶음의 피규어(전설 장소 · 울트라 비스트 …)는 건드리지 않는다 [가정 — plan §5 #116].
        /// </summary>
        void SyncFigures()
        {
            if (board == null || board.Figures == null) return;
            var view = Client != null ? Client.View : null;
            bool game = view != null && view.Phase != Phase.Setup;
            var seq = Fx != null ? Fx.Sequencer : null;
            sitesBusy = false;
            ShadeIcons(view, seq);
            // 전설 장소 찾아보기(23행 — P25): 조우 전환의 앞에서 그 칸의 피규어가 솟았다 내려앉으며 Special Attack 클립을 한다
            int riseCell = -1, riseClip = 0;
            float riseHop = 0f, riseStretch = 1f;
            BattleStarted siteStart = null;
            var site = game && seq != null ? LegendSiteBeats.StepOf(seq.Current, out siteStart) : null;
            if (site != null && seq.LocalMs >= site.StartMs && seq.LocalMs < site.EndMs && LegendSiteBeats.Figure(seq.LocalMs - site.StartMs, out riseHop, out riseStretch, out riseClip)) riseCell = siteStart.Cell;
            // 신 격파(80행 — P28): 그 단계가 대기열에 남은 동안 쓰러진 신의 피규어는 선 채였다가 단계가 시작되면 빛으로 흩어진다
            int goneMs = -1;
            var gone = game ? GodBeats.Pending(seq, out goneMs) : null;
            foreach (var f in board.Figures.Figures)
            {
                if (f.Group == BeastFigures || f.Group == NecrozmaFigures)
                {
                    PlaceBeast(f, view, seq);
                    continue;
                }
                if (f.Group == TaxiFigures)
                {
                    // 장막시티에 늘 서 있던 찌르호크 택시는 없앴다 — 공중날기 연출(16행) 동안만 말을 태우고 난다(결정 126 · T10 — Fly가 세운다)
                    if (!taxiFlying || seq == null || FlyBeats.StepOf(seq.Current) == null) BoardFigures.Place(f, false);
                    continue;
                }
                bool rising = riseCell >= 0 && f.Spot.Cell == riseCell;
                if (f.Group != ShrineFigures)
                {
                    // 전설 장소의 피규어: 솟는 동안만 옮기고 끝나면 제자리 · Idle로 돌린다. 게임 중에는 결정 123대로 섰다 사라진다(PlaceSite)
                    if (rising) { BoardFigures.PlaceAt(f, BoardFigures.Rest(f) + Vector3.up * (riseHop * BoardFigures.RiseOf(f)), BeastClip.SpecialAttack, riseClip, riseStretch); risen = f; }
                    else if (ReferenceEquals(risen, f)) { BoardFigures.Place(f, true); risen = null; }
                    else if (game && f.Group == SiteFigures) PlaceSite(f, view, seq);
                    else if (f.Group == SiteFigures && (f.Root == null || !f.Root.activeSelf)) BoardFigures.Place(f, true);   // 게임 밖 · 셋업은 모두 선 채(시험 판)
                    continue;
                }
                bool on = true;
                float fall = 0f, squash = 1f, size = 1f;
                if (game)
                {
                    if (!Ctx.Cards.PokemonIds.TryIndexOf(f.Key, out int def)) continue;
                    on = false;
                    foreach (var t in view.Tokens)
                        if ((t.Kind == TokenKind.Legend || t.Kind == TokenKind.God) && t.Def == def && t.Cell >= 0 && !t.Destroyed)
                        {
                            on = true;
                            if (t.Kind == TokenKind.God) godStood = f;
                        }
                    if (!on && gone != null && ReferenceEquals(godStood, f))
                    {
                        on = ScatterBeats.At(goneMs, out size, out float lift);
                        fall = lift * BoardFigures.ScatterHeight / BoardFigures.DescendHeight;
                    }
                    if (seq != null)
                        foreach (var entry in seq.Queued)
                        {
                            bool cur = ReferenceEquals(entry, seq.Current);
                            var ls = LegendBeats.StepOf(entry);
                            var le = LegendBeats.Event(ls);
                            var gs = DescendBeats.StepOf(entry);
                            var gd = DescendBeats.Event(gs);
                            if (le != null && !(cur && seq.LocalMs >= ls.EndMs))
                            {
                                int local = cur ? seq.LocalMs - ls.StartMs : -1;
                                if (le.Placed && le.Species == def)
                                {
                                    var pose = local >= 0 ? LegendBeats.At(local, ls.Ms, true) : default;
                                    on = pose.Shown;
                                    fall = pose.Fall;
                                    squash = pose.Shown ? pose.Squash : 1f;
                                }
                                else if (le.ReplacedSpecies == def)
                                {
                                    // 달빛공터의 바꿔치기(P37): 있던 전설이 빛으로 흩어진다
                                    on = ScatterBeats.At(local < 0 ? -1 : local - LegendBeats.ScatterFromMs, out size, out float lift);
                                    fall = lift * BoardFigures.ScatterHeight / BoardFigures.DescendHeight;
                                }
                            }
                            if (gd != null && !(cur && seq.LocalMs >= gs.EndMs))
                            {
                                int local = cur ? seq.LocalMs - gs.StartMs : -1;
                                if (gd.CardKey == def && !gd.Stayed)
                                {
                                    var pose = local >= 0 ? DescendBeats.At(local, gs.Ms, false) : default;
                                    on = pose.Shown;
                                    fall = pose.Fall;
                                    squash = pose.Shown ? pose.Squash : 1f;
                                }
                                else if (gd.ReplacedGod > 0 && FractureRules.GodCard(Ctx, (SinnohGod)gd.ReplacedGod) == def) on = local < 0 || DescendBeats.OldShown(local);
                            }
                        }
                }
                if (on && rising && size >= 1f && fall <= 0f)
                {
                    // 전설 사당의 전설 · 시작의 사당의 신과 싸우러 들어갈 때도 솟는다(23행 — 같은 박자)
                    BoardFigures.PlaceAt(f, BoardFigures.Rest(f) + Vector3.up * (riseHop * BoardFigures.RiseOf(f)), BeastClip.SpecialAttack, riseClip, riseStretch);
                    continue;
                }
                BoardFigures.Place(f, on, fall * BoardFigures.DescendHeight, squash, size);
            }
        }

        // 23행에서 솟게 한 전설 장소 피규어(끝나면 제자리로) · 마지막으로 선 신 피규어(80행 — 토큰이 빠진 뒤 흩어질 것)
        BoardFigures.Figure risen, godStood;

        /// <summary>피규어 표의 전설 장소 묶음(전설 열둘).</summary>
        public const string SiteFigures = "LegendarySite";

        /// <summary>전설 장소 피규어가 나타나는 길이(ms — 0에서 커진다) · 사라지는 길이는 빛으로 흩어지는 <see cref="ScatterBeats.Ms"/>(결정 123 [가정 plan §5 #640]).</summary>
        public const int SiteAppearMs = 400;

        // 전설 장소 피규어마다 지금 선 쪽인가 · 바뀐 시각(게임 시계 NowMs — 처음 본 것은 연출 없음) · 어느 판의 것인가
        readonly Dictionary<BoardFigures.Figure, KeyValuePair<bool, long>> sites = new Dictionary<BoardFigures.Figure, KeyValuePair<bool, long>>();
        object sitesFor;
        bool sitesBusy;   // 바뀔 것이 남았거나(대기열이 비기를 기다림) 나타남 · 사라짐이 도는 중 — Tick이 SyncFigures를 부른다

        /// <summary>
        /// 결정 123: 전설 장소 피규어는 <see cref="LegendRules.SiteFigureShown"/>(누가 조건 유물을 가졌고 아직 만날 수 있음)일 때만 선다. 보기가 바뀌어도 앞 연출(유물 얻기 · 배틀 결과)이 다 보인 뒤
        /// (대기열이 빈 때) 바꾸고, 나타날 때는 커지며 · 사라질 때는 빛으로 흩어진다(연출 「끄기」면 곧바로). 이어하기 · 관전 · 웹 손님도 같은 보기라 같다.
        /// </summary>
        void PlaceSite(BoardFigures.Figure f, GameState view, FxSequencer seq)
        {
            int fixture = -1;
            var fixtures = Ctx.Fixtures.Fixtures;
            for (int i = 0; i < fixtures.Count && fixture < 0; i++)
                if (fixtures[i].Kind == FixtureKind.LegendarySite && fixtures[i].Cell == f.Spot.Cell) fixture = i;
            if (fixture < 0) return;
            if (!ReferenceEquals(sitesFor, Client))
            {
                sites.Clear();
                sitesFor = Client;
            }
            bool want = LegendRules.SiteFigureShown(view, Ctx, fixture);
            if (!sites.TryGetValue(f, out var st)) sites[f] = st = new KeyValuePair<bool, long>(want, long.MinValue);
            else if (st.Key != want && (seq == null || seq.IsEmpty)) sites[f] = st = new KeyValuePair<bool, long>(want, NowMs);
            if (st.Key != want) sitesBusy = true;
            long since = st.Value == long.MinValue || FxSpeed == FxSpeed.Off ? long.MaxValue : (NowMs - st.Value) * (FxSpeed == FxSpeed.Fast ? 2 : 1);
            int ms = (int)Math.Min(int.MaxValue, since);
            if (ms < Math.Max(SiteAppearMs, ScatterBeats.Ms)) sitesBusy = true;
            if (st.Key)
            {
                float size = ms >= SiteAppearMs ? 1f : Mathf.Max(0.01f, Tween.At(ms, 0, SiteAppearMs, 0f, 1f, Ease.OutQuad));
                BoardFigures.Place(f, true, 0f, 1f, size);
            }
            else
            {
                bool shown = ScatterBeats.At(Math.Min(ms, ScatterBeats.Ms), out float size, out float lift);
                BoardFigures.Place(f, shown, lift * BoardFigures.ScatterHeight, 1f, size);
            }
        }

        /// <summary>그 피규어(표의 키)가 지금 서 있나 · 그 크기 배율(확인용 — 빛으로 흩어지는 중이면 1보다 작다). 없으면 false · 0.</summary>
        public bool FigureShown(string key, out float size)
        {
            size = 0f;
            if (board == null || board.Figures == null) return false;
            foreach (var f in board.Figures.Figures)
                if (f.Key == key && f.Root != null)
                {
                    size = f.Root.activeSelf && f.FitScale > 0f ? f.Root.transform.localScale.x / f.FitScale : 0f;
                    return f.Root.activeSelf;
                }
            return false;
        }

        /// <summary>그 칸에 선 피규어가 제자리에서 솟은 높이(월드 유닛 — 23행 확인용). 없으면 −1.</summary>
        public float FigureRise(int cell)
        {
            if (board == null || board.Figures == null) return -1f;
            foreach (var f in board.Figures.Figures)
                if (f.Spot.Cell == cell && f.Root != null && f.Root.activeSelf) return f.Root.transform.localPosition.y - (float)f.Spot.Y;
            return -1f;
        }

        /// <summary>그 칸에 선 피규어가 23행에서 가장 높이 솟는 높이(월드 유닛 — <see cref="BoardFigures.RiseOf"/>, 확인용). 없으면 −1.</summary>
        public float FigureRiseTop(int cell)
        {
            if (board == null || board.Figures == null) return -1f;
            foreach (var f in board.Figures.Figures)
                if (f.Spot.Cell == cell && f.Root != null && f.Root.activeSelf) return BoardFigures.RiseOf(f);
            return -1f;
        }

        /// <summary>피규어 표의 울트라 비스트 묶음(UB 9) · 울트라 버스트 묶음(네크로즈마 — 판에 서는 것은 1페이즈 폼 하나, <c>FigureLayout</c>의 group).</summary>
        public const string BeastFigures = "UltraBeast", NecrozmaFigures = "UltraBurst";

        /// <summary>
        /// UB·네크로즈마 피규어(M6 「UB 보드 화면」): 게임 중에는 판의 UB 토큰 · 네크로즈마 토큰 칸에 선다(그 토큰이 맵에 없으면 — 쓰러뜨린 UB · 신오의 심장 판 — 서지 않는다).
        /// 화면 대기열에 남은 UB 이동(82행)이 지나기 전에는 그 이동의 출발 칸에, 이동 단계 동안은 칸 사이를 뛰어 옮기고(Walk 클립), 밟아 부수는 단계(83행) 동안은 공격 클립 —
        /// 셈은 <see cref="UbBoard.Of"/>(엔진 무관 — dotnet 시험이 같은 셈을 본다). 게임 밖(타이틀 뒤 시험 판)은 M4-2 배치 자리 그대로.
        /// </summary>
        void PlaceBeast(BoardFigures.Figure f, GameState view, FxSequencer seq)
        {
            if (view == null)
            {
                BoardFigures.Place(f, true);
                if (f.Group == NecrozmaFigures) foreach (var other in board.Figures.Forms) BoardFigures.Place(other, false);
                return;
            }
            if (!Ctx.Cards.PokemonIds.TryIndexOf(f.Key, out int def)) return;
            var at = UbBoard.Of(view.Tokens, def, seq != null ? seq.Queued : null, seq != null ? seq.Current : null, seq != null ? seq.LocalMs : 0, UbMoveBeats.Heavy(f.Key));
            // 네크로즈마는 페이즈 폼으로 바꿔 선다(M6 「UB 시작」 — 2페이즈 썬/문 · 3페이즈 울트라): 판의 1페이즈 피규어 자리에 그 폼을 세우고 나머지는 숨긴다
            var shown = f;
            if (f.Group == NecrozmaFigures)
            {
                string form = UbBoard.NecrozmaForm(view.Ub, seq != null ? seq.Queued : null);
                shown = board.Figures.FormOf(f, form) ?? f;
                foreach (var other in board.Figures.Forms)
                    if (other != shown) BoardFigures.Place(other, false);
                if (shown != f) BoardFigures.Place(f, false);
            }
            if (!at.Shown || at.From < 0 || at.To < 0 || at.From >= board.Board.Count || at.To >= board.Board.Count)
            {
                BoardFigures.Place(shown, false);
                return;
            }
            var ground = Vector3.Lerp(board.CellTop(at.From), board.CellTop(at.To), at.T);
            BoardFigures.PlaceAt(shown, ground + Vector3.up * at.Hop, at.Clip, at.ClipMs, at.Squash);
        }

        /// <summary>네크로즈마 피규어가 지금 선 폼(피규어 키 — 2·3페이즈 폼 확인용). 판에 서 있지 않으면 null.</summary>
        public string NecrozmaShown()
        {
            if (board == null || board.Figures == null) return null;
            foreach (var f in board.Figures.Figures)
                if (f.Group == NecrozmaFigures && f.Root != null && f.Root.activeSelf) return f.Key;
            foreach (var f in board.Figures.Forms)
                if (f.Root != null && f.Root.activeSelf) return f.Key;
            return null;
        }

        /// <summary>
        /// 게임이 바꾸는 고정물 아이콘(04 §8-8 83행): UB가 밟아 부순 나무·산호·사당은 파괴 단계의 박자대로 어두워지고(<see cref="UbBoard.ShadeOf"/>), kindler B가 맵에서 없앤 나무(칸 −1)는 사라진다.
        /// 게임 밖은 모두 처음 모습.
        /// </summary>
        void ShadeIcons(GameState view, FxSequencer seq)
        {
            var icons = board.Icons;
            if (icons == null) return;
            if (view == null)
            {
                icons.ClearShades();
                return;
            }
            foreach (var t in view.Tokens)
            {
                if (!UbBoard.Shaded(t.Kind) || t.Def < 0 || t.Def >= Ctx.Fixtures.Fixtures.Count) continue;
                var shade = UbBoard.ShadeOf(t, seq != null ? seq.Queued : null, seq != null ? seq.Current : null, seq != null ? seq.LocalMs : 0);
                icons.Shade(Ctx.Fixtures.Fixtures[t.Def].Key, shade.Dark, shade.Hidden);
            }
        }

        // UB 이동(82행) · 밟은 칸의 파괴(83행 — 네크로즈마의 원격 파괴 칸도): 카메라의 보는 점이 UB를 따라가고 부서지는 칸으로 다가간다(방향·줌 그대로)
        void UbFocus(FxSequencer seq)
        {
            var entry = seq.Current;
            if (entry == null || board == null || rig == null) return;
            FxStep at = null;
            foreach (var s in entry.Steps)
                if ((s.Row == UbRows.Move || s.Row == UbRows.Destroy || s.Row == UbRows.Fly || s.Row == UbRows.Strike || s.Row == UbBattleRows.Phase || s.Row == UbBattleRows.Silvally) && s.StartMs <= seq.LocalMs) at = s;
            if (at == null) return;
            int local = at.Local(seq.LocalMs);
            if (UbRows.First<UbMoved>(at) is UbMoved m && m.From >= 0 && m.To >= 0 && m.From < board.Board.Count && m.To < board.Board.Count)
                Follow(at, Vector3.Lerp(board.CellTop(m.From), board.CellTop(m.To), UbMoveBeats.At(local, at.Ms).T), local);
            else if (SpotFlyBeats.Event(at) is TokenMoved tm && tm.From >= 0 && tm.From < board.Board.Count)   // 83행 토큰 날기: 밟힌 칸 → 깨어진 세계의 그 칸
                Follow(at, tm.To >= 0 && tm.To < board.Board.Count ? Vector3.Lerp(board.CellTop(tm.From), board.CellTop(tm.To), SpotFlyBeats.At(tm, local, at.Ms).Cross) : board.CellTop(tm.From), local);
            else if (SilvallyBeats.Event(at) is BlessingGained bg && Client != null && bg.Seat >= 0 && bg.Seat < Client.View.Seats.Length && Client.View.Seats[bg.Seat].Cell is int sc && sc >= 0 && sc < board.Board.Count)
                Follow(at, board.CellTop(sc), local);   // 93행 실버디 축복: 이긴 좌석이 선 신전 칸
            else if (UbRows.First<CellDestroyed>(at) is CellDestroyed d && d.Cell >= 0 && d.Cell < board.Board.Count)
                Follow(at, board.CellTop(d.Cell), local);
            else if ((at.Row == UbRows.Strike || at.Row == UbBattleRows.Phase) && NecrozmaCell() is int n && n >= 0)
                Follow(at, board.CellTop(n), local);   // 84행 빛을 모음 · 90행 페이즈 격파의 빛: 네크로즈마로
        }

        /// <summary>네크로즈마 토큰의 칸(맵에 없으면 −1).</summary>
        public int NecrozmaCell()
        {
            var view = Client != null ? Client.View : null;
            if (view == null || board == null) return -1;
            foreach (var t in view.Tokens)
                if (t.Kind == TokenKind.Necrozma && t.Cell >= 0 && t.Cell < board.Board.Count) return t.Cell;
            return -1;
        }

        /// <summary>
        /// 그 UB·네크로즈마 피규어(포켓몬 표 색인)의 머리 위 한 점의 판 좌표(M6 「UB 화면 묶음 1」 — 움직이는 UB의 「자동」 꼬리표 · 네크로즈마의 빛 고리). 피규어가 서 있지 않으면 false.
        /// </summary>
        public bool BeastHead(int def, out Vector2 panel)
        {
            panel = default;
            if (board == null || board.Figures == null || rig == null || laidFor.x <= 0 || def < 0 || def >= Ctx.Cards.Pokemon.Count) return false;
            string key = Ctx.Cards.Pokemon[def].Key;
            BoardFigures.Figure fig = null;
            foreach (var f in board.Figures.Figures)
                if (f.Key == key && f.Root != null && f.Root.activeSelf) fig = f;
            if (fig == null && Client != null && Client.View.Tokens.Exists(t => t.Kind == TokenKind.Necrozma && t.Def == def))   // 네크로즈마는 페이즈 폼이 그 자리에 선다(Forms — 네크로즈마 폼만)
                foreach (var f in board.Figures.Forms)
                    if (f.Root != null && f.Root.activeSelf) fig = f;
            if (fig == null) return false;
            var top = fig.Root.transform.position + Vector3.up * (fig.FittedSize.y + 0.3f);
            if (!BoardPick.ScreenAt(rig.GetComponent<Camera>(), top, laidFor.x, laidFor.y, out var px)) return false;
            float scale = Stage.Grade.PanelScale;
            panel = new Vector2(Mathf.Round(px.x / scale), Mathf.Round((laidFor.y - px.y) / scale));
            return true;
        }

        // ── 보드 위 토큰(04 §3-1) ──

        static readonly BoardTokens.Token[] NoTokens = new BoardTokens.Token[0];

        /// <summary>
        /// 그 토큰이 지금 보드 위에 보이나: 조무래기 토큰은 서막의 놓기 연출(5행)이 시작될 때 나타난다. 글(칸 정보 줄 · 공개 정보 카드)도 보드 위 토큰과 같은 때에 바뀌게 이 판정을 거친다.
        /// </summary>
        public bool TokenShown(TokenState t) => t != null && !t.Destroyed && (t.Kind != TokenKind.Grunt || !GruntsPending()) && (t.Kind != TokenKind.HiddenSpot || !SpotsPending());

        /// <summary>그 숨겨진 장소 토큰이 지금 앞면으로 보이나: 공개 연출(25행)에서 뒤집히는 가운데(그림이 바뀌는 때)를 지나야 앞면이다 — 보기는 답과 함께 바로 앞면이다.</summary>
        public bool TokenFaceUp(TokenState t)
        {
            if (t == null || !t.FaceUp) return false;
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return true;
            foreach (var entry in seq.Queued)
            {
                var step = SpotBeats.StepOf(entry);
                if (SpotBeats.Cell(step, out bool reveal) != t.Cell || !reveal) continue;
                return ReferenceEquals(entry, seq.Current) && SpotBeats.At(seq.LocalMs - step.StartMs, step.Ms, true).Flip >= 0.5f;
            }
            return true;
        }

        /// <summary>토큰의 글이 달라지는 때를 알아보는 값(조무래기가 아직 안 놓였나 · 뒤집히기 전인 공개가 몇인가) — 바뀌면 HUD를 다시 그린다.</summary>
        public int TokenLook
        {
            get
            {
                var view = Client != null ? Client.View : null;
                if (view == null) return 0;
                int look = (GruntsPending() ? 1 : 0) + (SpotsPending() ? 1000 : 0);
                foreach (var t in view.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && !t.Destroyed && !TokenFaceUp(t)) look += 2;
                return look;
            }
        }

        int wasLook;

        // 서막의 조무래기 놓기 단계가 대기열에서 아직 시작되지 않았나(그동안 조무래기 토큰은 보드에 없다 — 서막 카드가 먼저 뜬다)
        bool GruntsPending()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
            {
                var step = TokenBeats.StepOf(entry);
                if (step != null && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs)) return true;
            }
            return false;
        }

        /// <summary>
        /// 보기의 토큰(숨겨진 장소 · 조무래기 — 보드 위에 있고 파괴되지 않은 것)을 보드에 세운다. 조무래기는 서막의 놓기 연출(5행)이 시작될 때 나타난다.
        /// 배틀이 떠 있는 동안에는 건드리지 않는다 — 이긴 조무래기 토큰은 결과 창을 닫고 보드로 돌아온 뒤 날아가 사라진다(94행 941 — <see cref="LeavingGrunts"/>가 그때까지 붙잡는다).
        /// </summary>
        void SyncTokens()
        {
            SyncFigures();
            if (board == null || board.Tokens == null) return;
            var view = Client != null ? Client.View : null;
            if (view == null || view.Phase == Phase.Setup)
            {
                board.Tokens.Show(NoTokens);
                return;
            }
            if (Battle != null && LeavingSpots(null) == 0) return;   // 우두머리 공개(29행)는 같은 명령에서 배틀이 시작된다 — 뒤집히는 토큰은 앞면으로 붙잡아 세운다
            gruntsHeld = GruntsPending();
            spotsHeld = SpotsPending();
            tokenList.Clear();
            var flying = UbFlying();
            foreach (var t in view.Tokens)
            {
                if (t.Cell < 0 || t.Destroyed) continue;
                if (t.Kind == TokenKind.HiddenSpot && spotsHeld) continue;
                if (t.Kind == TokenKind.HiddenSpot)
                {
                    int at = FlyCell(flying, t.Cell);   // 83행: 날기 단계가 남았으면 출발 칸 · 틈 속(−1)
                    if (at >= 0) tokenList.Add(new BoardTokens.Token(t.Kind, at, t.FaceUp, t.FaceUp ? SpotArt(t.Def) : t.Special ? BoardTokens.SpecialBack : null));   // 금색 ?(특별한 숨겨진 장소 — 공개 값)
                }
                else if (t.Kind == TokenKind.Grunt && !gruntsHeld) tokenList.Add(new BoardTokens.Token(t.Kind, t.Cell, true, null));
            }
            // 갈 칸이 없어 버려진 토큰(To −1 — 보기에는 없다): 날기 단계가 끝날 때까지 출발 칸에(뒷면)
            if (!spotsHeld)
                foreach (var kv in flying)
                    if (kv.Key.To < 0 && kv.Key.TokenKind == TokenKind.HiddenSpot && kv.Value.Cell >= 0) tokenList.Add(new BoardTokens.Token(TokenKind.HiddenSpot, kv.Value.Cell, false, null));
            flyKey = FlyKey(flying);
            spotsLeaving = LeavingSpots(tokenList);
            gruntsLeaving = LeavingGrunts(tokenList);
            board.Tokens.Show(tokenList);
            if (rig != null) board.Tokens.Face(rig.Yaw, rig.CurrentZoom == BoardCameraRig.Zoom.Full);
            RaiseSpot();
        }

        // 숨겨진 장소 앞면 그림의 키(아이템 표 — 없으면 null)
        string SpotArt(int def) => def >= 0 && def < Ctx.Items.Items.Count ? Ctx.Items.Items[def].Key : null;

        int spotsLeaving;   // 마지막 SyncTokens가 붙잡아 둔 「빠진 숨겨진 장소」 수
        int flyKey;   // 마지막 SyncTokens가 세운 83행 토큰 날기의 칸들(FlyKey)

        // 83행 토큰 날기(831): 대기열에 남은 날기 단계마다 그 토큰의 지금 모습(SpotFlyBeats.Pending)
        List<KeyValuePair<TokenMoved, SpotFlyBeats.Pose>> UbFlying()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            return SpotFlyBeats.Pending(seq != null ? seq.Queued : null, seq != null ? seq.Current : null, seq != null ? seq.LocalMs : 0);
        }

        // 보기의 그 칸(도착 칸)에 있는 숨겨진 장소 토큰이 지금 보일 칸: 날기 단계가 남았으면 그 모습의 칸, 없으면 그대로
        static int FlyCell(List<KeyValuePair<TokenMoved, SpotFlyBeats.Pose>> flying, int cell)
        {
            foreach (var kv in flying)
                if (kv.Key.To == cell && kv.Key.TokenKind == TokenKind.HiddenSpot) return kv.Value.Cell;
            return cell;
        }

        static int FlyKey(List<KeyValuePair<TokenMoved, SpotFlyBeats.Pose>> flying)
        {
            int key = flying.Count;
            foreach (var kv in flying) key = key * 397 + kv.Value.Cell + 2;
            return key;
        }

        /// <summary>
        /// 공개한 그 명령 안에서 맵에서 빠진 숨겨진 장소 토큰(규토리 더미 · 떨어진 별 · 유물 · 우두머리 — rulings A25, 보기에는 이미 없다)을 공개 연출(04 §8-8 25행)이 끝날 때까지 붙잡는다:
        /// 대기열에 남은 공개 단계(재생 중이면 그 단계가 끝나기 전)의 칸에 보기의 숨겨진 장소 토큰이 없으면 앞면 토큰 하나(into에 더한다). 반환 = 붙잡은 수 — 단계가 끝나면 줄어 SyncTokens가 치운다.
        /// </summary>
        int LeavingSpots(List<BoardTokens.Token> into)
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            var view = Client != null ? Client.View : null;
            if (seq == null || view == null) return 0;
            int n = 0;
            foreach (var entry in seq.Queued)
            {
                var step = SpotBeats.StepOf(entry);
                int cell = SpotBeats.Cell(step, out bool reveal);
                if (cell < 0 || !reveal || ReferenceEquals(entry, seq.Current) && seq.LocalMs >= SpotGone(entry, step) || SpotOnMap(view, cell)) continue;
                n++;
                if (into == null) continue;
                int def = -1;
                foreach (var e in step.Events)
                    if (e is HiddenSpotRevealed r) { def = r.SpotKey; break; }
                into.Add(new BoardTokens.Token(TokenKind.HiddenSpot, cell, true, SpotArt(def)));
            }
            return n;
        }

        /// <summary>
        /// 1막 해방(04 §8-8 94행 941 — M9 다듬기 P18)으로 맵에서 빠진 조무래기 토큰(보기에는 이미 없다)을 그 단계에서 날아가 사라질 때(<see cref="LiberateBeats.GoneAt"/>)까지 붙잡는다.
        /// 반환 = 붙잡은 수 — 줄면 <see cref="FlyGrunts"/>가 SyncTokens로 치운다.
        /// </summary>
        int LeavingGrunts(List<BoardTokens.Token> into)
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return 0;
            int n = 0;
            foreach (var entry in seq.Queued)
            {
                var step = LiberateBeats.StepOf(entry);
                if (step == null || ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs + LiberateBeats.GoneAt) continue;
                foreach (var e in step.Events)
                {
                    if (!(e is TokenRemoved gone) || gone.TokenKind != TokenKind.Grunt || gone.Cell < 0) continue;
                    n++;
                    into?.Add(new BoardTokens.Token(TokenKind.Grunt, gone.Cell, true, null));
                }
            }
            return n;
        }

        // 1막 해방(94행 941): 고향 마을의 조무래기 토큰이 움찔했다가 위로 날아가 사라진다(칩은 HUD 위의 ResultFx)
        void FlyGrunts(FxSequencer seq)
        {
            if (board == null || board.Tokens == null || Battle != null) return;
            if (gruntsLeaving != LeavingGrunts(null)) SyncTokens();
            var step = LiberateBeats.StepOf(seq.Current);
            bool on = step != null && seq.LocalMs >= step.StartMs && seq.LocalMs < step.StartMs + LiberateBeats.GoneAt;
            if (!on)
            {
                if (gruntsFlying) board.Tokens.Land();
                gruntsFlying = false;
                return;
            }
            int t = seq.LocalMs - step.StartMs;
            float lift = LiberateBeats.Lift(t);
            foreach (var e in step.Events)
                if (e is TokenRemoved gone && gone.TokenKind == TokenKind.Grunt) board.Tokens.Drop(gone.Cell, lift < 0f ? -1f : 1f - lift, LiberateBeats.Squash(t));
            gruntsFlying = true;
        }

        // 빠진 토큰이 보드에서 사라지는 항목 시각: 결과 연출(26~30행)이 있으면 그 안에서 터지거나 떠오르는 때, 없으면 공개 단계 끝
        static int SpotGone(FxEntry entry, FxStep reveal)
        {
            var result = ResultRows.SpotStep(entry);
            var e = ResultRows.EventOf<SpotResolved>(result);
            return e == null ? reveal.EndMs : result.StartMs + Math.Min(result.Ms, SpotResultBeats.GoneAt(e.Result, result.Ms));
        }

        static bool SpotOnMap(GameState view, int cell)
        {
            foreach (var t in view.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.Cell == cell && !t.Destroyed) return true;
            return false;
        }

        // 서막의 조무래기 놓기(5행): 고향 마을마다 순서대로 토큰이 위에서 떨어져 내려앉는다
        void DropTokens(FxSequencer seq)
        {
            if (board == null || board.Tokens == null) return;
            if (gruntsHeld != GruntsPending()) SyncTokens();
            var step = TokenBeats.StepOf(seq.Current);
            var placed = step != null && seq.LocalMs >= step.StartMs && seq.LocalMs < step.EndMs ? TokenBeats.Grunts(step) : null;
            if (placed == null || placed.Cells == null)
            {
                if (gruntsDropping) board.Tokens.Land();
                gruntsDropping = false;
                return;
            }
            int t = seq.LocalMs - step.StartMs;
            for (int i = 0; i < placed.Cells.Length; i++) board.Tokens.Drop(placed.Cells[i], TokenBeats.Fall(t, i), TokenBeats.Squash(t, i));
            gruntsDropping = true;
        }

        /// <summary>그 칸 윗면 가운데의 패널 좌표(왼쪽 위가 (0, 0)). 보드 없이 도는 흐름이거나 칸이 카메라 뒤면 false.</summary>
        public bool CellPoint(int cell, out Vector2 panel)
        {
            panel = default;
            if (board == null || rig == null || laidFor.x <= 0 || cell < 0 || cell >= board.Board.Count) return false;
            if (!BoardPick.Screen(board, rig.GetComponent<Camera>(), cell, laidFor.x, laidFor.y, out var px)) return false;
            float scale = Stage.Grade.PanelScale;
            panel = new Vector2(Mathf.Round(px.x / scale), Mathf.Round((laidFor.y - px.y) / scale));
            return true;
        }

        /// <summary>
        /// 핑의 누른 자리(결정 102 ①): 그 화면 점(물리 px, 왼쪽 아래가 (0, 0)) 아래의 칸과, 그 칸 윗면 가운데에서 보드 평면(월드 X · Z) 위 위치(칸 반지름 대비 천분율).
        /// 칸 밖 여백이면 가장 가까운 보이는 칸(반지름 <see cref="PingRules.CellMax"/> 천분율 안) — 없으면 false.
        /// </summary>
        public bool PingCellAt(float x, float y, out int cell, out int dx, out int dz)
        {
            cell = -1;
            dx = dz = 0;
            if (board == null || rig == null || laidFor.x <= 0) return false;
            var cam = rig.GetComponent<Camera>();
            cell = BoardPick.Cell(board, cam, x, y, laidFor.x, laidFor.y);
            if (!BoardPick.Ground(board, cam, x, y, laidFor.x, laidFor.y, rig.Target.y, out var hit)) return cell >= 0;
            float r = (float)BoardLayout.HexRadius;
            if (cell < 0)
            {
                float best = PingRules.CellMax / (float)PingRules.Unit * r;
                bool distortion = board.DistortionWorldVisible;
                for (int c = 0; c < board.Board.Count; c++)
                {
                    if (board.Board[c].DistortionWorld && !distortion) continue;
                    float ddx = (float)board.Looks[c].X - hit.x, ddz = (float)board.Looks[c].Z - hit.z;
                    float d = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                    if (d >= best) continue;
                    best = d;
                    cell = c;
                }
                if (cell < 0) return false;
            }
            var top = board.CellTop(cell);
            dx = PingRules.Clamp(PingAt.Cell, Mathf.RoundToInt((hit.x - top.x) / r * PingRules.Unit));
            dz = PingRules.Clamp(PingAt.Cell, Mathf.RoundToInt((hit.z - top.z) / r * PingRules.Unit));
            return true;
        }

        /// <summary><see cref="PingCellAt"/>의 반대: 그 칸 윗면 가운데에서 (dx, dz) 천분율만큼 떨어진 점의 패널 좌표 — 받는 쪽은 자기 카메라(방향 · 줌)로 풀어 같은 땅을 가리킨다.</summary>
        public bool CellSpot(int cell, int dx, int dz, out Vector2 panel)
        {
            panel = default;
            if (board == null || rig == null || laidFor.x <= 0 || cell < 0 || cell >= board.Board.Count) return false;
            float r = (float)BoardLayout.HexRadius;
            var at = board.CellTop(cell) + new Vector3(dx / (float)PingRules.Unit * r, 0f, dz / (float)PingRules.Unit * r);
            if (!BoardPick.ScreenAt(rig.GetComponent<Camera>(), at, laidFor.x, laidFor.y, out var px)) return false;
            float scale = Stage.Grade.PanelScale;
            panel = new Vector2(Mathf.Round(px.x / scale), Mathf.Round((laidFor.y - px.y) / scale));
            return true;
        }

        /// <summary>
        /// 그 칸 윗면 육각형을 카메라로 비춘 사각형(패널 좌표 — 왼쪽 위가 (0, 0)): 각본 튜토리얼 층의 앵커 <c>cell:n</c>(07 §7 · T16)이 구멍을 낸다.
        /// 보드 없이 도는 흐름이거나 칸이 카메라 뒤면 false.
        /// </summary>
        public bool CellRect(int cell, out Rect panel)
        {
            panel = default;
            if (board == null || rig == null || laidFor.x <= 0 || cell < 0 || cell >= board.Board.Count) return false;
            var cam = rig.GetComponent<Camera>();
            var top = board.CellTop(cell);
            float r = (float)BoardLayout.HexRadius, scale = Stage.Grade.PanelScale;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int k = 0; k < 12; k++)   // 꼭짓점 방향을 몰라도 되게 30°마다
            {
                float a = k * Mathf.PI / 6f;
                if (!BoardPick.ScreenAt(cam, top + new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)), laidFor.x, laidFor.y, out var px)) return false;
                float x = px.x / scale, y = (laidFor.y - px.y) / scale;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            panel = Rect.MinMaxRect(Mathf.Round(minX), Mathf.Round(minY), Mathf.Round(maxX), Mathf.Round(maxY));
            return true;
        }

        // 찾아보기(20행): 찾는 칸의 테두리가 맥박 친다(이동 표시의 목표 칸 색을 빌린다 — 나무·풀 흔들림은 보드 메시가 아직 없다 [가정])
        void Pulse(FxSequencer seq)
        {
            var marks = BoardMarks;
            if (marks == null) return;
            var step = seq.Current != null ? seq.Current.Step(EnterRows.Search) : null;
            var searched = step != null && step.Events.Count > 0 ? step.Events[0] as Searched : null;
            int t = step != null ? seq.LocalMs - step.StartMs : -1;
            // 고향 마을 고르기(2행)의 예비도 같은 맥박이다(자리 색 대신 목표 칸 색 [가정])
            var home = VillageBeats.StepOf(seq.Current);
            int homeMs = home != null ? seq.LocalMs - home.StartMs : -1;
            if (searched != null && searched.Cell >= 0 && t >= 0 && t < step.Ms && EnterRows.PulseOn(t))
            {
                marks.Show(NoCells, NoCells, searched.Cell);
                pulsing = true;
            }
            else if (home != null && homeMs >= 0 && homeMs < home.Ms && VillageBeats.At(homeMs, home.Ms).Pulse && VillageBeats.Picked(home).Cell >= 0)
            {
                marks.Show(NoCells, NoCells, VillageBeats.Picked(home).Cell);
                pulsing = true;
            }
            else if (pulsing)
            {
                // 켜 둔 맥박만 끈다(시간을 한 번에 길게 보내 단계를 건너뛰어도 남지 않게) — HUD가 칠한 이동 표시는 건드리지 않는다
                marks.Hide();
                pulsing = false;
            }
        }

        bool pulsing;

        static readonly int[] NoCells = new int[0];

        // 보드의 공간 일그러짐(M9 다듬기 P39 — 박자 RippleBeats.Board, 그리기 BoardRipple → 보드 셰이더의 꼭짓점): 깨어진 세계가 보이는 판의 잔물결 · 칸이 부서질 때(830) · 신 강림의 번개(341).
        // 값은 대기열 시계에서만 나온다. 연출 「끄기」에서는 없다(장식 없음)
        object rippleWorldOf;
        Vector3 rippleWorldAt;
        float rippleWorldFar;

        void Ripple()
        {
            if (board == null) return;
            var seq = Fx != null ? Fx.Sequencer : null;
            var pose = seq == null || FxSpeed == Sinnoh.Fx.FxSpeed.Off ? default(RipplePose) : RippleBeats.Board(seq.Current, seq.LocalMs, seq.NowMs, board.DistortionWorldVisible);
            if (!pose.On)
            {
                if (BoardRipple.On) BoardRipple.Clear();
                return;
            }
            float step = (float)Sinnoh.Data.BoardLayout.HexRadius * 2f;   // 칸 하나의 폭쯤
            if (pose.Cell >= 0 && pose.Cell < board.Board.Count)
            {
                BoardRipple.Set(board.CellTop(pose.Cell), pose.Cells * step, pose.Amp * step / 2f, pose.Phase, RippleBeats.WaveCells * step);
                return;
            }
            if (!ReferenceEquals(rippleWorldOf, board.Board))
            {
                // 깨어진 세계 칸들의 가운데와 가장 먼 칸까지의 거리(판이 바뀔 때 한 번)
                rippleWorldOf = board.Board;
                var sum = Vector3.zero;
                int n = 0;
                for (int c = 0; c < board.Board.Count; c++)
                    if (board.Board[c].DistortionWorld) { sum += board.CellTop(c); n++; }
                rippleWorldAt = n > 0 ? sum / n : Vector3.zero;
                rippleWorldFar = 0f;
                for (int c = 0; c < board.Board.Count; c++)
                    if (board.Board[c].DistortionWorld) rippleWorldFar = Mathf.Max(rippleWorldFar, Vector3.Distance(rippleWorldAt, board.CellTop(c)));
            }
            BoardRipple.Set(rippleWorldAt, rippleWorldFar + pose.Cells * step, pose.Amp * step / 2f, pose.Phase, RippleBeats.WaveCells * step);
        }

        // 가운데 필드의 전환 줄무늬(22행 · 역전환): 화면들 위에 얹는 층 하나. 덮은 만큼은 대기열 시각에서만 나온다
        void Wipe(FxSequencer seq)
        {
            var wipeStep = seq.Current != null ? seq.Current.Step(EnterRows.Wipe) : null;
            float cover = EnterRows.WipeCover(wipeStep, seq.LocalMs);
            var style = WipeBeats.StyleOf(wipeStep);   // 상대에 따라 다른 무늬(M9 다듬기 P14 — 87행 UB의 「균열이 찢어지는 전환」도 이 갈래)
            float lit = WipeBeats.Light(style, wipeStep, seq.LocalMs);
            if (WildAside && !WatchingWild && !WildAutoOpens) cover = lit = 0f;   // 뒤에서 그리는 남의 야생 배틀(창이 떠 있다 · 접었다)은 보드를 줄무늬로 덮지 않는다(결정 114 — 하던 작업 유지). 저절로 여는 배틀은 내 배틀처럼(결정 120)
            if (wipeBackAt != long.MinValue)
            {
                long since = seq.NowMs - wipeBackAt;
                if (since >= EnterBeats.WipeBackMs) wipeBackAt = long.MinValue;
                else cover = Math.Max(cover, 1f - Tween.Progress((int)since, 0, EnterBeats.WipeBackMs));
            }
            if (wipe == null)
            {
                if (cover <= 0f && lit <= 0f) return;
                wipe = new FieldWipe();
            }
            wipe.Attach(Stage.Root);
            if (wipeStep != null) wipe.Look(style);   // 역전환은 들어올 때의 무늬 그대로
            wipe.Set(cover, lit);
        }

        /// <summary>
        /// 그 좌석의 말 머리 위 점(패널 px — 왼쪽 위가 (0, 0)): 찾아보기의 「!」가 뜨는 자리. 보드 없이 도는 흐름(캡처·시험)에서는 가운데 필드의 한 점이다.
        /// 말이 화면 밖이거나 아직 배치 크기를 모르면 false.
        /// </summary>
        public bool PiecePoint(int seat, out Vector2 panel)
        {
            panel = default;
            var view = Client != null ? Client.View : null;
            var root = Stage.Root;
            if (view == null || root == null || seat < 0 || seat >= view.Seats.Length) return false;
            if (board == null || rig == null || laidFor.x <= 0)
            {
                var size = root.layout;
                if (float.IsNaN(size.width) || size.width <= 0f) return false;
                panel = new Vector2(Mathf.Round(size.width * 0.5f), Mathf.Round(size.height * 0.4f));
                return true;
            }
            int cell = view.Seats[seat].Cell;
            if (cell < 0 || !BoardPick.Screen(board, rig.GetComponent<Camera>(), cell, laidFor.x, laidFor.y, out var px)) return false;
            float scale = Stage.Grade.PanelScale;
            // 말 그림의 머리: 발 점에서 (발 줄 위의 줄 수) × 말 정수배만큼 위(BoardPieces.BodyRect와 같은 셈)
            var piece = board.Pieces != null ? board.Pieces.Of(seat) : null;
            // 「!」는 좌석 번호 핀 위에 뜬다(핀 = 그림 위 1텍셀 틈 + PinSize — BoardPieces.PinRect와 같은 셈. 핀을 덮지 않는다)
            int step = BoardPieces.ZoomK(rig.CurrentZoom);
            float head = piece != null ? BoardPieces.PinTop(piece, step) * BoardPieces.Scale(piece, step, laidFor.y) : 0f;
            // 그 좌석이 묶음에 들었으면 세워 둔 말의 핀 위 묶음 표지 위에 뜬다
            var stack = board.Pieces != null ? board.Pieces.StackAt(cell) : null;
            if (stack != null && Array.IndexOf(stack.Seats, seat) >= 0) head = StackHead(stack, head);
            panel = new Vector2(Mathf.Round(px.x / scale), Mathf.Round((laidFor.y - px.y - head) / scale) - BoardHud.BangAbove);
            return true;
        }

        // 묶음 표지가 뜬 칸에서 머리 위에 띄우는 것(「!」·뜬 숨겨진 장소 토큰)이 더 올라가는 높이(물리 px): 표지 한 변 + 위아래 틈. 묶음이 없으면 0
        float StackLift(int cell)
        {
            var stack = board != null && board.Pieces != null ? board.Pieces.StackAt(cell) : null;
            if (stack == null) return 0f;
            return (StackBadges.Size(board.Pieces.Of(stack.Stand), Stage.Grade.TokenHeight, ZoomStep) + 2f * StackBadges.Gap) * Stage.Grade.PanelScale;
        }

        // 묶음이 선 칸의 머리 높이(물리 px): 세워 둔 말의 핀 윗변 + 표지
        float StackHead(PieceStack stack, float fallback)
        {
            var stand = board.Pieces.Of(stack.Stand);
            if (stand == null) return fallback;
            int step = BoardPieces.ZoomK(rig.CurrentZoom);
            return BoardPieces.PinTop(stand, step) * BoardPieces.Scale(stand, step, laidFor.y) + StackLift(stack.Cell);
        }

        /// <summary>
        /// 그 말의 좌석 번호 핀 윗변 가운데(패널 px — 왼쪽 위가 (0, 0)): 묶음 표지가 이 위에 선다. 말이 선 자리에서 셰이더와 같은 셈으로 센다
        /// (<see cref="BoardPieces.PinRect"/>). 보드 없이 도는 흐름이거나 말이 카메라 뒤면 false.
        /// </summary>
        public bool HeadPoint(BoardPieces.Piece piece, out Vector2 panel)
        {
            panel = default;
            if (piece == null || piece.Root == null || board == null || rig == null || laidFor.x <= 0) return false;
            if (!BoardPick.ScreenAt(rig.GetComponent<Camera>(), piece.Root.transform.position, laidFor.x, laidFor.y, out var px)) return false;
            float scale = Stage.Grade.PanelScale;
            int step = BoardPieces.ZoomK(rig.CurrentZoom), k = BoardPieces.Scale(piece, step, laidFor.y);
            panel = new Vector2(Mathf.Round((Mathf.Floor(px.x) + k * 0.5f) / scale), Mathf.Round((laidFor.y - Mathf.Floor(px.y) - BoardPieces.PinTop(piece, step) * k) / scale));
            return true;
        }

        // 항목이 끝났다: 따라가던 카메라를 끝 자리에 놓는다 — 한 번에 길게 보낸 시간(캡처·끊긴 프레임)이 항목을 건너뛰어도 걸은 뒤 말이 화면 가운데에 온다
        void Settle(FxEntry entry)
        {
            camStep = null;
            bool dragged = ReferenceEquals(entry, heldEntry) || drag.Panning;   // 이 항목 동안 내가 보드를 끌었다 — 끝 자리로 되돌리지 않는다(결정 66 ②). 줌만 끝 모습에 맞춘다
            if ((gruntsDropping || gruntsFlying) && board != null && board.Tokens != null) board.Tokens.Land();
            gruntsDropping = gruntsFlying = false;
            if (TokenBeats.StepOf(entry) != null || LiberateBeats.StepOf(entry) != null) SyncTokens();   // 한 번에 길게 보낸 시간이 놓기 단계를 건너뛰어도 토큰이 선다
            // 셋업 연출(2·5행)의 끝 모습: 말은 고른 마을에 서고 카메라는 지역 줌으로 그 마을에 · 숨겨진 장소 토큰은 다 놓이고 카메라는 전체 줌으로 · 첫 내 턴 컷신이 지역 줌으로 내 말에
            var homeStep = VillageBeats.StepOf(entry);
            if (homeStep != null && board != null)
            {
                var picked = VillageBeats.Picked(homeStep);
                if (picked.Cell >= 0 && picked.Cell < board.Board.Count)
                {
                    if (board.Pieces != null) board.Pieces.Land(picked.Seat, picked.Cell);
                    if (rig != null) rig.Set(BoardCameraRig.Zoom.Region, rig.Yaw, dragged ? rig.Target : board.CellTop(picked.Cell));
                }
                if (pulsing && BoardMarks != null) BoardMarks.Hide();
                pulsing = false;
            }
            if (SpotDropBeats.StepOf(entry) != null)
            {
                SyncTokens();
                if (board != null && board.Tokens != null) board.Tokens.LandSpots();
                spotsDropping = false;
                wide = true;
                if (rig != null)
                {
                    rig.Set(BoardCameraRig.Zoom.Full, rig.Yaw, dragged ? rig.Target : WideLook());
                    wideAimed = !dragged;
                    wideAt = rig.Target;
                }
            }
            if (wide && entry.Step(FxRows.MyTurn) != null)
            {
                wide = false;
                if (rig != null && MyCell(out var mine)) rig.Set(BoardCameraRig.Zoom.Region, rig.Yaw, dragged ? rig.Target : mine);
            }
            if (SpotBeats.StepOf(entry) != null) SyncTokens();   // 공개한 명령 안에서 빠진 토큰은 공개 연출이 끝난 여기서 치운다(한 번에 길게 보낸 시간이 단계를 건너뛰어도) · 남은 토큰은 다시 띄운다
            if (ReferenceEquals(entry, battleEnd) && Fx != null && Battle != null && Battle.Ended && WildAside && !WatchingWild) DropWildAside();   // 결정 114: 보지 않던 남의 야생 배틀은 결과 창 없이
            else if (ReferenceEquals(entry, battleEnd) && Fx != null && Battle != null && Battle.Ended)
            {
                // 배틀 끝까지 다 보였다: 결과 창이 뜨고, 뒤에 기다리는 턴 넘김은 [확인]까지 선다.
                // 방장 시각 판(결정 126 · T4)은 대기열을 세우지 않는다 — 결과 창만 떠 있고 뒤 연출은 방장 시각대로 흐른다(남의 결과 창은 그 뒤 연출이 시작되면 저절로 닫힌다 — ShowFx)
                battleHeld = true;
                if (!HostPaced) Fx.SetPaused(true);
            }
            heldStep = null;
            heldEntry = null;
            ShowWarped(entry);
            if (rig == null || board == null || Fx == null || dragged) return;
            var warped = TeleportBeats.Event(TeleportBeats.StepOf(entry));
            if (warped != null && warped.To >= 0 && warped.To < board.Board.Count) rig.LookAt(board.CellTop(warped.To));
            var gated = GateBeats.Event(GateBeats.StepOf(entry));
            if (gated != null && gated.ToCell >= 0 && gated.ToCell < board.Board.Count) rig.LookAt(board.CellTop(gated.ToCell));
            Moved last = null;
            foreach (var s in entry.Steps)
                if (s.Row == 110 && s.Events.Count > 0) last = (Moved)s.Events[0];
            if (last != null && last.Path != null && last.Path.Length > 0) rig.LookAt(board.CellTop(last.Path[last.Path.Length - 1]));
            var flight = FlyBeats.Flight(FlyBeats.StepOf(entry));
            if (flight != null && flight.To >= 0 && flight.To < board.Board.Count) rig.LookAt(board.CellTop(flight.To));
            if (entry.Step(FxRows.MyTurn) != null && MyCell(out var at)) rig.LookAt(at);
            // 남의 턴 시작(6행): 따라간 카메라는 그 좌석의 말에 선다
            if (TurnStartBeats.StepOf(entry)?.Tag is TurnStartInfo began && !began.Mine && Client != null && Client.View != null && began.Seat < Client.View.Seats.Length)
            {
                int cell = Client.View.Seats[began.Seat].Cell;
                if (cell >= 0 && cell < board.Board.Count && last == null && flight == null) rig.LookAt(board.CellTop(cell));
            }
        }

        bool MyCell(out Vector3 at)
        {
            at = default;
            if (rig == null || board == null || Client == null || Client.View == null) return false;
            int cell = Client.View.Seats[Client.Seat].Cell;
            if (cell < 0) return false;
            at = board.CellTop(cell);
            return true;
        }

        void Arrange()
        {
            SeatLooks(Ctx, Client.View, Protagonist, out var cells);
            HideDropping(cells);
            StackView();
            board.Pieces.Arrange(cells);
        }

        // 한 칸에 말 여럿(결정 66 ①): 세워 두는 말 = 내 말 → 지금 턴인 좌석 → 번호가 앞선 좌석
        void StackView()
        {
            if (board == null || board.Pieces == null) return;
            if (Client == null || Client.View == null) board.Pieces.SetStackView(-1, -1);
            else board.Pieces.SetStackView(Client.Seat, Client.View.Phase != Phase.Setup ? Client.View.ActiveSeat : -1);
        }

        // ── 게임 메뉴 ──

        /// <summary>게임 메뉴(S22)를 HUD 위에 연다. 열려 있는 동안 보드 입력은 꺼지고, 혼자 하는 판은 화면 대기열도 다음 항목을 시작하지 않는다(방장 시각 판 <see cref="HostPaced"/>는 흐른다 — T4).</summary>
        public void OpenMenu()
        {
            if (!OnBoard || MenuUp) return;
            // 물음 창 · 배틀 · 결과 창 · 정보 창이 떠 있으면 그 위 정보 창 층에(T35 — 게임 메뉴는 어떤 상황에서도 열린다). 떠 있던 정보 창은 메뉴가 자리를 갖는다
            if (Stage.Current == null && Stage.Info == null) Stage.Open(new BoardMenuScreen(this));
            else Stage.OpenInfo(new BoardMenuScreen(this));
            if (!HostPaced) Fx.SetPaused(true);   // 방장 시각 판(T4)은 메뉴 뒤에서도 대기열이 흐른다 — 닫으면 그 시각의 화면을 연다(CloseMenu → Sync)
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
        }

        // 게임 메뉴 · 메뉴에서 연 설정이 있는 층의 창(정보 창 층 먼저 — T35)
        IUiScreen MenuLayer => Stage.Info is BoardMenuScreen || Stage.Info is SettingsScreen ? Stage.Info : Stage.Current;

        /// <summary>게임 메뉴나 그 메뉴에서 연 설정이 떠 있나(화면 자리든 정보 창 층이든) — 그동안 화면 대기열은 서 있고 조우 창·배틀 화면은 열지 않는다.</summary>
        public bool MenuUp => MenuLayer is BoardMenuScreen || (MenuLayer is SettingsScreen set && set.InGame) || Tutorial.DialogUp;   // 튜토리얼 끝내기 창·끝 창도(M6.5)

        /// <summary>
        /// 게임 메뉴의 [설정](결정 70 ⑨): 타이틀의 설정과 같은 화면을 게임 메뉴 자리에 연다(게임을 떠나지 않는다 — 화면 대기열은 메뉴 때처럼 서 있고 카메라는 그대로다).
        /// 창 크기를 바꿔도 화면은 토큰 벌만 바뀌고(<see cref="UiStage.SetGrade"/>) 보드 카메라는 보던 곳 그대로다.
        /// </summary>
        public void OpenMenuSettings()
        {
            if (!(MenuLayer is BoardMenuScreen)) return;
            OpenMenuLayer(new SettingsScreen(window, CloseMenuSettings, this, inGame: true));
        }

        /// <summary>게임 중 설정의 [뒤로]: 게임 메뉴로 돌아간다.</summary>
        public void CloseMenuSettings()
        {
            if (!(MenuLayer is SettingsScreen set) || !set.InGame) return;
            OpenMenuLayer(new BoardMenuScreen(this));
        }

        // 메뉴가 있던 층(화면 자리 · 정보 창 층)에 그 창을 연다
        void OpenMenuLayer(IUiScreen screen)
        {
            if (ReferenceEquals(MenuLayer, Stage.Info)) Stage.OpenInfo(screen);
            else Stage.Open(screen);
            Restage();
            Hud?.Refresh();
        }

        /// <summary>
        /// 게임 메뉴를 닫고 보드로 돌아간다. 메뉴가 떠 있는 동안 열 때가 지난 화면(조우 창 · 배틀 화면 — 찾아보기·조우 전환이 메뉴 뒤에서 끝났다)이 있으면 지금 연다:
        /// 대기열이 비어 있으면 화면 콜백(<see cref="ShowFx"/>)이 다시 오지 않으므로 여기서 맞춘다.
        /// </summary>
        public void CloseMenu()
        {
            if (!(MenuLayer is BoardMenuScreen)) return;
            if (Stage.Info is BoardMenuScreen) Stage.ShutInfo();   // 아래 창(물음 · 배틀 · 결과 창)으로 돌아간다(T35)
            else Stage.Shut();
            if (Fx != null) Fx.SetPaused(false);
            Sync();
        }

        // ── 기록 ──

        /// <summary>기록 창(S21)을 HUD 위에 연다. 보드에 있고 위에 다른 창이 없을 때만. 화면 대기열은 멈추지 않는다.</summary>
        public void OpenLog()
        {
            if (!OnBoard || Stage.Current != null) return;
            Stage.Open(new LogScreen(this));
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
        }

        /// <summary>
        /// 막 안내 창이 저절로 뜨나(결정 101 ② — 막이 바뀌었거나 내가 막 목표를 이룬 뒤, 보드 입력이 열릴 때). 사람이 하는 판만 켠다(<see cref="ScreenBoot"/>) —
        /// 확인·시험의 길잡이는 꺼 둔다(창이 떠 있으면 보드 입력이 닫힌다). 꺼져 있어도 막 배지를 누르면 열린다(<see cref="OpenAct"/>).
        /// </summary>
        public bool ActNotice { get; set; }

        /// <summary>
        /// 막 안내 창(<see cref="ActScreen"/> — 결정 101 ②)을 HUD 위에 연다: 지금 막의 「목표 / 달성하면 얻는 것 / 이번 막에서 달라지는 것」. 보드에 있고 위에 다른 창이 없을 때만. 화면 대기열은 멈추지 않는다.
        /// <paramref name="notice"/> = 저절로 뜨는 것(방금 바뀐 막 · 방금 이룬 목표의 「얻은 것」 줄 — 띄우면 그 알림은 비운다). 막 배지(<c>board-hud-act</c>)는 false로 부른다.
        /// </summary>
        public bool OpenAct(bool notice = false)
        {
            if (!OnBoard || Stage.Current != null || Client == null || Client.View == null || Client.View.Hos == null) return false;
            int now = ActBeats.ShownAct(Client.View.Hos.Act, Fx != null ? Fx.Sequencer : null);
            int act = notice && noticeAct >= 0 ? noticeAct : now;
            if (act < 0) return false;
            Stage.Open(new ActScreen(this, act, notice ? noticeGot : -1, notice && noticeAct >= 0));
            if (notice) noticeAct = noticeGot = -1;
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
            return true;
        }

        /// <summary>막 안내 창의 [확인](= Esc): 닫고 보드로 돌아간다.</summary>
        public void CloseAct()
        {
            if (!(Stage.Current is ActScreen)) return;
            Stage.Shut();
            Sync();
        }

        // ── 포켓몬 도감(S42 — 결정 105 ③) ──

        /// <summary>
        /// 포켓몬 도감(<see cref="DexScreen"/>)을 HUD 위에 연다(위 띠의 [도감]): 보드에 있고 위에 다른 창이 없을 때만 — 내 턴 · 남의 턴 · 연출 중 어느 때나. 화면 대기열은 멈추지 않는다(기록 창과 같다).
        /// 도감은 정적 데이터(<see cref="Ctx"/>)만 받는다 — 판의 덱 상태는 넘기지 않는다.
        /// </summary>
        public bool OpenDex()
        {
            if (Ctx == null || !CanPeek) return false;
            if (Front is DexScreen) return true;
            ShowInfo(new DexScreen(Ctx, CloseDex, true), Stage.Current == null);   // 창 · 배틀이 떠 있으면 그 위 정보 창 층에(결정 125)
            return true;
        }

        /// <summary>타이틀의 「포켓몬 도감」 줄: 도감을 연다. [뒤로]는 타이틀의 그 줄로 돌아간다.</summary>
        public void OpenTitleDex()
        {
            if (Ctx == null) return;
            Leave();
            Stage.Open(new DexScreen(Ctx, CloseDex, false));
            Restage();
        }

        /// <summary>도감의 [닫기](= 창 밖 누르기 · Esc): 게임 중이면 보드로, 타이틀에서 열었으면 타이틀로.</summary>
        public void CloseDex()
        {
            if (ShutInfo(Stage.Info is DexScreen)) return;
            if (!(Stage.Current is DexScreen dex)) return;
            if (!dex.InGame)
            {
                OpenTitle(TitleScreen.Dex);
                return;
            }
            Stage.Shut();
            Sync();
        }

        // ── 타입 상성 · 상태 이상(플레이 시험 T5 — 결정 125의 「언제나 열린다」) ──

        /// <summary>
        /// 위 띠의 [상성]: 타입 상성 · 상태 이상 창(<see cref="TypeChartScreen"/>)을 연다 — 보드에 창이 없으면 HUD 위에, 창 · 배틀 · 결과 창이 떠 있으면 그 위 정보 창 층에(아래 창은 그대로).
        /// 이미 떠 있으면 닫는다. 화면 대기열은 멈추지 않는다. 열었으면 true.
        /// </summary>
        public bool ToggleTypes()
        {
            if (Front is TypeChartScreen)
            {
                CloseTypes();
                return false;
            }
            if (!CanPeek) return false;
            ShowInfo(new TypeChartScreen(this), Stage.Current == null);
            return true;
        }

        /// <summary>타입 상성 · 상태 이상 창의 [닫기](= 창 밖 누르기 · Esc): 보드로(정보 창 층이면 아래 창으로) 돌아간다.</summary>
        public void CloseTypes()
        {
            if (ShutInfo(Stage.Info is TypeChartScreen)) return;
            if (!(Stage.Current is TypeChartScreen)) return;
            Stage.Shut();
            Sync();
        }

        /// <summary>기록 창을 닫고 보드로 돌아간다.</summary>
        public void CloseLog()
        {
            if (!(Stage.Current is LogScreen)) return;
            Stage.Shut();
            Sync();
        }

        // ── 정비(04 §3-5) ──

        /// <summary>
        /// 정비 판(S18 「내 판」 — 남 좌석이면 읽기만 하는 S39)을 지금 열 수 있나: 보드에 있고 위에 다른 창이 없고, 전원 차단 연출(막 카드 · 서막의 조무래기 놓기 — 04 §8-1 P2 until-end 전원)이 대기열에 없다.
        /// 걷기·턴 넘김 같은 연출 중에는 열린다(정비는 none — 04 §8-1 P2). 배틀 화면이 떠 있으면 그 위에 연다 — 배틀 화면이 입력을 받고 엔진이 정비를 막지 않는 때만(<see cref="BattleScreen.MaintOn"/>).
        /// </summary>
        public bool CanOpenParty
        {
            get
            {
                if (Tutorial.DialogUp) return false;   // 튜토리얼 끝내기 창·끝 창 아래로는 열지 않는다(단축키 T)
                if (OnBoard && Stage.Current is BattleScreen battle) return battle.MaintOn && battle.Over == null;
                if (!OnBoard || Stage.Current != null) return false;
                return !FxBlocksMaint;
            }
        }

        // 전원 차단 연출(막 카드 · 서막의 조무래기 놓기 · 숨겨진 장소 배치 · 셋업 끝 — 04 §8-1 P2 until-end 전원)이 대기열에 있나: 그동안 정비 판은 보기만 한다
        bool FxBlocksMaint
        {
            get
            {
                if (Fx != null)
                    foreach (var entry in Fx.Sequencer.Queued)
                        if (ActBeats.StepOf(entry) != null || TokenBeats.StepOf(entry) != null || SpotDropBeats.StepOf(entry) != null || SetupRows.EndOf(entry) >= 0) return true;
                return false;
            }
        }

        /// <summary>
        /// 정보 창(결정 125 — 정비 · 포켓몬 도감 · 트레이너 카드 · 운명의 변화 · 룰 설명서 · 상성표)을 지금 열 수 있나: 보드에 있으면 어떤 상황에서든 —
        /// 물음 창 · 배틀 · 결과 창 · 남의 턴 · 연출 중에도(그 상황에서 못 하는 조작은 그 창이 잠근다 — <see cref="PeekLock"/>). 튜토리얼 끝내기 창 · 끝 창과 게임 메뉴 아래로는 열지 않는다.
        /// </summary>
        public bool CanPeek => OnBoard && Client.View != null && !Tutorial.DialogUp && !MenuUp;

        /// <summary>맨 앞의 창: 정보 창 층(<see cref="UiStage.Info"/> — 결정 125)이 떠 있으면 그것, 아니면 <see cref="Top"/>. Esc와 정보 창의 여닫기가 본다.</summary>
        public IUiScreen Front => Stage.Info ?? Top;

        /// <summary>
        /// 정보 창 층에 연 내 정비 판이 조작을 받지 않는 까닭(글 키 — 결정 125): 배틀 중 · 다른 창이 떠 있다 · 전원 차단 연출 중.
        /// 화면 자리(보드 · 배틀 독의 [정비]가 켜진 때)에 연 판이면 null(지금처럼 엔진 검사가 칸을 켜고 끈다).
        /// </summary>
        public string PeekLock(IUiScreen screen)
        {
            if (screen == null || !ReferenceEquals(Stage.Info, screen)) return null;
            if (Stage.Current is BattleScreen) return "screen.peek.lock.battle";
            if (Stage.Current != null) return "screen.peek.lock.window";
            return "screen.peek.lock.fx";
        }

        // 정보 창을 연다: 그 창이 조작을 받는 때(here — 보드에 창이 없다 · 배틀 독의 [정비]가 켜졌다)면 지금처럼 화면 자리에, 아니면 정보 창 층에 —
        // 아래 창(물음 · 배틀 · 결과 창)은 그대로 남고 이 창을 닫으면 그대로 돌아간다(결정 125)
        void ShowInfo(IUiScreen screen, bool here)
        {
            if (here && Stage.Info == null)
            {
                ShowOver(screen);
                return;
            }
            Stage.OpenInfo(screen);
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
        }

        // 정보 창 층의 창이 그것이면 닫는다(아래 창으로 돌아간다)
        bool ShutInfo(bool match)
        {
            if (!match) return false;
            Stage.ShutInfo();
            Sync();
            return true;
        }

        /// <summary>
        /// 정비 판을 연다(seat &lt; 0 = 내 좌석). 화면 대기열은 멈추지 않는다. 보드에 창이 없으면 HUD 위에 — 그사이 열 때가 된 화면(조우 창·배틀 화면)은 이 판을 닫고 열린다.
        /// 배틀 화면이 떠 있고 정비를 받는 때면 그 화면 위에 연다(배틀 독의 [정비] — 닫으면 배틀로 돌아온다. 그 위에서 연출이 시작되면 닫힌다).
        /// 그 밖(물음 창 · 결과 창 · 연출 중의 배틀 · 전원 차단 연출 — 결정 125)에는 정보 창 층에 보기만 하는 판으로 연다.
        /// </summary>
        public bool OpenParty(int seat = -1)
        {
            if (!CanPeek) return false;
            if (seat < 0) seat = Client.Seat;
            if (seat >= Client.View.Seats.Length) return false;
            if (Front is PartyScreen open && open.Seat == seat) return true;
            ShowInfo(new PartyScreen(this, seat), CanOpenParty);
            return true;
        }

        /// <summary>정비 판을 닫고 보드로(배틀 화면 위에 열었으면 배틀로 · 정보 창 층이면 아래 창으로) 돌아간다.</summary>
        public void CloseParty()
        {
            if (ShutInfo(Stage.Info is PartyScreen)) return;
            if (!(Top is PartyScreen)) return;
            ShutTop();
            Sync();
        }

        /// <summary>단축키 T: 정비 판을 열거나 닫는다.</summary>
        public void ToggleParty()
        {
            if (Front is PartyScreen) CloseParty();
            else OpenParty();
        }

        /// <summary>맨 위의 창: 배틀 화면 위에 띄운 창(배틀 중 정비 판 · 그 카드 확대)이 있으면 그것, 아니면 열린 화면(없으면 null). 정보 창 층은 보지 않는다(<see cref="Front"/>).</summary>
        public IUiScreen Top => Stage.Current is BattleScreen battle && battle.Over != null ? battle.Over : Stage.Current;

        void ShutTop()
        {
            if (Stage.Current is BattleScreen battle && battle.Over != null) battle.ShutOver();
            else Stage.Shut();
        }

        /// <summary>
        /// 포켓몬 카드 확대(S38)를 연다 — 정비 판에서 포켓몬 칸을 눌렀을 때. [닫기]는 그 좌석의 정비 판으로 돌아간다(같은 층).
        /// 보기에 없는 포켓몬이면 열지 않는다.
        /// </summary>
        public bool OpenMon(int pokemon, int seat)
        {
            if (!OnBoard || !(Front is PartyScreen) || Client.View == null) return false;
            if (pokemon < 0 || pokemon >= Client.View.Pokemon.Count || Client.View.Pokemon[pokemon].Def < 0) return false;
            ShowInfo(new MonScreen(this, pokemon, seat), true);
            return true;
        }

        /// <summary>포켓몬 카드 확대를 닫고 그 좌석의 정비 판으로 돌아간다.</summary>
        public void CloseMon()
        {
            var mon = Front as MonScreen;
            if (mon == null) return;
            ShowInfo(new PartyScreen(this, mon.Seat), true);
        }

        // ── 마을 창(S23) · 트레이너 카드 판(S40) ──

        /// <summary>내 말이 마을 행동을 하는 곳(마을 · 울트라메가로폴리스 · 앞면 야영지 — <see cref="MapActions.VillagePlace"/>)에 서 있나. village = 그 마을의 고정물 색인(야영지면 −1).</summary>
        public bool AtVillage(out RuleSource source, out int village)
        {
            source = RuleSource.VillageHeal;
            village = -1;
            var view = Client != null ? Client.View : null;
            return view != null && view.Phase != Phase.Setup && MapActions.VillagePlace(view, Ctx, view.Seats[Client.Seat].Cell, out source, out village);
        }

        /// <summary>
        /// 마을 창(S23 — [전체 회복]·[공중날기])을 HUD 위에 연다: 내 턴의 맵 행동이 열려 있고 입력이 열려 있으며 마을 행동을 하는 곳에 서 있을 때만.
        /// 화면 대기열은 멈추지 않는다. 창 안의 단추가 답을 낸다.
        /// </summary>
        public bool OpenVillage()
        {
            if (!InputOpen || Client.Choice(ChoiceKind.MapAction) == null || !AtVillage(out _, out _)) return false;
            ShowOver(new VillageScreen(this));
            return true;
        }

        /// <summary>
        /// bird_keeper B 새 휘슬(rulings A36 — 「fly to a village from any tile」): 마을 밖 칸에서 [날기]가 마을 창의 공중날기 칸만 연다(감사 G1-1).
        /// 내 턴의 맵 행동에 공중날기 답이 있고 마을 행동을 하는 곳이 아닐 때만(마을이면 [마을]의 창).
        /// </summary>
        public bool OpenWhistle()
        {
            if (!InputOpen || AtVillage(out _, out _)) return false;
            var choice = Client.Choice(ChoiceKind.MapAction);
            if (choice == null || !Client.Legal(choice).Exists(a => a.Kind == AnswerKind.Fly)) return false;
            ShowOver(new VillageScreen(this, whistle: true));
            return true;
        }

        /// <summary>마을 창을 닫고 보드로 돌아간다.</summary>
        public void CloseVillage()
        {
            if (!(Stage.Current is VillageScreen)) return;
            Stage.Shut();
            Sync();
        }

        // ── 제작 창(S26) · 진화 창(S27) — M5 「제작·은행·진화 화면」 ──

        /// <summary>
        /// 제작 창(S26)을 연다: 보드 HUD의 [제작](맵 행동 — 위에 창이 없을 때) 또는 조우 창의 [제작](21행 — 고르기 전, 조우 창 위). 내 선택에 제작 답이 있고 입력이 열려 있을 때만.
        /// 닫으면(<see cref="CloseCraft"/>) 조우 창은 다시 선다.
        /// </summary>
        public bool OpenCraft()
        {
            if (!BoardOpen || (Stage.Current != null && !(Stage.Current is EncounterScreen e && e.Open))) return false;
            var choice = CraftScreen.ChoiceOf(this);
            if (choice == null || !Client.Legal(choice).Exists(a => a.Kind == AnswerKind.Craft)) return false;
            ShowOver(new CraftScreen(this));
            return true;
        }

        /// <summary>제작 창을 닫고 보드(조우 고르기 중이면 조우 창)로 돌아간다.</summary>
        public void CloseCraft()
        {
            if (!(Stage.Current is CraftScreen)) return;
            Stage.Shut();
            Sync();
        }

        /// <summary>진화 창(S27)을 연다: 보드 HUD의 [진화] — 내 턴의 맵 행동에 진화 답이 있고 입력이 열려 있을 때만.</summary>
        public bool OpenEvolve()
        {
            var choice = Client != null ? Client.Choice(ChoiceKind.MapAction) : null;
            if (!InputOpen || choice == null || !Client.Legal(choice).Exists(a => a.Kind == AnswerKind.Evolve)) return false;
            ShowOver(new EvolveScreen(this));
            return true;
        }

        /// <summary>진화 창을 닫고 보드로 돌아간다. 진화 연출 · 갈래 · 옛 기술을 묻는 동안은 닫지 않는다.</summary>
        public void CloseEvolve()
        {
            if (!(Stage.Current is EvolveScreen evolve) || (!evolve.CanClose && (EvolveScreen.Waits(Client) || (Fx != null && !Fx.Sequencer.IsEmpty)))) return;
            Stage.Shut();
            Sync();
        }

        // ── 사당 판(S31~S33 — M5 「사당·균열 화면」) ──

        /// <summary>
        /// 보드 HUD의 [공물]: 사당 판(오른쪽 기둥 — 보드를 덮지 않는다, 결정 75 A1 가)을 연다. 내 맵 행동에 공물 답이 있고 입력이 열려 있을 때만.
        /// 3막 시작의 사당의 심장 회복도 같은 판의 같은 답이다(<see cref="HeartOffer"/>).
        /// </summary>
        /// <param name="reach">psychic B 「염력」으로 바칠 떨어진 사당 칸(−1 = 내 칸 — T33: 그 칸에 선 것과 같은 판).</param>
        public bool OpenShrine(int reach = -1)
        {
            var choice = Client != null ? Client.Choice(ChoiceKind.MapAction) : null;
            if (!InputOpen || choice == null || !Client.Legal(choice).Exists(a => reach < 0 ? a.Kind == AnswerKind.Offer : ShrineScreen.IsReachOffer(a, reach))) return false;
            ShowOver(new ShrineScreen(this, true, reach));
            return true;
        }

        /// <summary>사당 판을 닫고 보드로 돌아간다. 내 선택 · 남은 연출 · 남의 추종자 선택이 있는 동안은 닫지 않는다.</summary>
        public void CloseShrine()
        {
            if (!(Stage.Current is ShrineScreen shrine) || !shrine.CanClose) return;
            Stage.Shut();
            Sync();
        }

        // 내 사당·균열 선택이 열렸거나(나눠 받은 축복 · 다음 턴의 만찬 스튜 · 추종자) 사당 판이 그릴 연출이 남았는데 판이 닫혀 있으면 연다
        void SyncShrine(bool menu)
        {
            if (Battle != null || Stage.Current != null || menu) return;
            if (ShrineScreen.ChoiceOf(Client) != null || ShrinePending()) Stage.Open(new ShrineScreen(this));
        }

        // 사당 판이 그릴 내 단계(공물 · 축복 · 집계)가 아직 남았나 — 남 좌석의 공물·축복은 판을 열지 않고 좌석 칩의 꼬리표로(OtherActs)
        bool ShrinePending()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
                foreach (var s in entry.Steps)
                    if (ShrineRows.InPanel(s.Row) && ShrineScreen.Mine(this, s) && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return true;
            return false;
        }

        // 이 갱신이 천상의 균열의 추종자 묶음인가(비밀 선택이 열려 있다 · 방금 다 답해 집계가 왔다) — 그 묶음의 「고른 뒤」·「동시 공개」 단계(셋업의 1·99행)는 사당 판의 집계(340)가 대신 그린다
        bool FollowerUpdate(GameUpdate update)
        {
            if (update.View != null && FractureRules.Choosing(update.View)) return true;
            foreach (var e in update.Events) if (e is FollowerTally) return true;
            return false;
        }

        // 사당 판 행(공물 310 · 축복 320 · 집계 340)은 내 화면에 그릴 것만 남긴다(ShrineScreen.Mine — 남의 공물·축복은 여럿 판이면 좌석 칩의 꼬리표로 남긴다: OtherActs)
        bool ShrineDraws(FxStep step, bool followers)
        {
            if (followers && (step.Row == SetupRows.Chosen || step.Row == RevealAllBeats.Row)) return false;
            return !ShrineRows.InPanel(step.Row) || ShrineScreen.Mine(this, step);
        }

        // 은행상회 18 · 제작 36·37 · 진화 39행의 단계는 그 행동을 낸 창이 그릴 때만 남긴다(창 밖에서 생긴 내 것은 기록 줄만 — 화려한 만찬의 스튜 등 · 남 좌석의 것은 좌석 칩의 꼬리표: OtherActs)
        bool WorkDraws(FxStep step) => !WorkRows.Built(step.Row) || (Stage.Current as IWorkScreen)?.Draws(step) == true;

        /// <summary>그 좌석이 트레이너 카드를 가졌나(트레이너 카드를 끈 판 · 아직 고르기 전이면 false).</summary>
        public bool HasTrainerCard(int seat)
        {
            var view = Client != null ? Client.View : null;
            return view != null && view.Options.TrainerCards && seat >= 0 && seat < view.Seats.Length && view.Seats[seat].TrainerCard >= 0;
        }

        /// <summary>
        /// 트레이너 카드 판(S40 — 직업 그림 · 등급 · 능력 문구 · 잠김, seat &lt; 0 = 내 좌석)을 HUD 위에 연다. 여는 조건은 정비 판과 같다(<see cref="CanOpenParty"/>).
        /// 트레이너 카드를 끈 판에서는 열리지 않는다. 보기만 한다(능력 사용은 M6).
        /// </summary>
        public bool OpenTrainer(int seat = -1)
        {
            if (!CanPeek) return false;   // 결정 125: 물음 창 · 배틀 · 결과 창 위에서는 정보 창 층에
            if (seat < 0) seat = Client.Seat;
            if (!HasTrainerCard(seat)) return false;
            if (Front is TrainerScreen open && open.Seat == seat) return true;
            ShowInfo(new TrainerScreen(this, seat), Stage.Current == null && CanOpenParty);
            return true;
        }

        /// <summary>트레이너 카드 판을 닫고 보드로(정보 창 층이면 아래 창으로) 돌아간다.</summary>
        public void CloseTrainer()
        {
            if (ShutInfo(Stage.Info is TrainerScreen)) return;
            if (!(Stage.Current is TrainerScreen)) return;
            Stage.Shut();
            Sync();
        }

        // ── 스탬프 판(S37) · 도전 상대 고르기(S35) · 거래 창(S36) — M5 「스탬프·도전·거래 화면」 ──

        /// <summary>스탬프·도전 VS·거래의 덮개(47·51·52행 — 무대의 맨 위 층 <see cref="UiStage.Over"/>: 창·배틀 화면 위). 연출이 한 번도 돌지 않았으면 null.</summary>
        public DealFx Deals { get; private set; }

        /// <summary>트레이너 카드 덮개(48·49·50행 — 해금 카드 뒤집기 · 카드 바꾸기 · 능력 사용의 작은 카드, M6 「트레이너 카드 해금 연출」): 스탬프 덮개 안의 맨 위. 연출이 한 번도 돌지 않았으면 null.</summary>
        public CardFx Cards { get; private set; }

        // 배틀 안에서 오른 트레이너 카드 등급을 결과 창 [확인] 뒤로 미루는 곳(48행 — 갱신 사이에 이어진다)
        readonly UnlockHold unlockHold = new UnlockHold();

        void ShowDeals(FxSequencer seq)
        {
            EnsureDeals();
            Deals.Show(seq);
            Cards.Show(seq);
            skipHint.style.display = CanSkip ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement skipHint;

        // 무대 맨 위 층의 덮개(스탬프·VS·거래 + 트레이너 카드 + 건너뛰기 안내 줄)를 세운다 — 카드 바꾸기 창이 카드 틀(CardFx.Face)을 쓰니 연출 전에도 부를 수 있다
        void EnsureDeals()
        {
            if (Deals == null) Deals = new DealFx(Stage, this);
            Stage.SetOver(Deals.Element);
            if (Cards == null)
            {
                Cards = new CardFx(Stage, this);
                Deals.Element.Add(Cards.Element);
            }
            if (skipHint == null || skipHint.parent != Deals.Element)
            {
                // 내 긴 연출 동안의 안내 줄(결정 87 V12 다 — 마우스를 받지 않는다)
                skipHint = new VisualElement { name = "fx-skip-hint", pickingMode = PickingMode.Ignore };
                skipHint.AddToClassList("sn-skip-hint");
                var text = Kit.Badge(Stage.T("screen.evolve.skip"), KitBadgeKind.Plain, Kit.F2);
                text.pickingMode = PickingMode.Ignore;
                skipHint.Add(text);
                skipHint.style.display = DisplayStyle.None;
                Deals.Element.Add(skipHint);
            }
        }

        // 스탬프(47행)는 받은 좌석의 화면에만 그린다(남의 것은 기록 줄만). 도전 VS · 거래는 모든 좌석에
        // 스탬프(47행)는 받은 좌석의 화면에 — 방장 시각 판은 남의 스탬프도 그 사람 화면과 같게 찍는다(T16 · 결정 120 — 내 창이 떠 있으면 DealFx가 그리지 않는다)
        bool DealDraws(FxStep step) => step.Row != DealRows.Stamp || (StampBeats.Event(step) is StampGained g && (g.Seat == Client.Seat || HostPaced));

        /// <summary>보드 HUD의 [스탬프 n/7]: 스탬프 판(seat &lt; 0 = 내 좌석)을 HUD 위에 연다. 여는 조건은 정비 판과 같다(보드에서만).</summary>
        public bool OpenStamp(int seat = -1)
        {
            if (Stage.Current != null || !CanOpenParty) return false;
            ShowOver(new StampScreen(this, seat < 0 ? Client.Seat : seat));
            return true;
        }

        /// <summary>
        /// 정비 판(S18 · 남 좌석은 S39)의 「여행 스탬프 n/7」(결정 80 N2 나 — 남의 스탬프는 숫자만 보이던 것): 판을 닫고 그 좌석의 스탬프 판을 연다.
        /// 보드 위에 연 판에서만(배틀 화면 위의 정비 판에서는 열지 않는다 — 스탬프 판은 보드에서만 연다).
        /// </summary>
        public bool OpenSeatStamp(int seat)
        {
            if (!(Stage.Current is PartyScreen) || Client == null || seat < 0 || seat >= Client.View.Seats.Length) return false;
            CloseParty();
            return OpenStamp(seat);
        }

        /// <summary>스탬프 판을 닫고 보드로 돌아간다.</summary>
        public void CloseStamp()
        {
            if (!(Stage.Current is StampScreen)) return;
            Stage.Shut();
            Sync();
        }

        /// <summary>
        /// 지금 내가 거래를 제안할 수 있는 상대(<see cref="TradeRules.Partners"/> — 내 턴이면 같은 칸·같은 신, 남의 턴이면 같은 신만). 보드 HUD가 [거래]를 켜는 데 쓴다.
        /// </summary>
        public List<int> TradePartners()
        {
            var into = new List<int>();
            if (Client != null && Client.View != null && Battle == null && !TradeLocked) TradeRules.Partners(Client.View, Ctx, Client.Seat, into);
            return into;
        }

        /// <summary>
        /// 플레이어 간 거래를 고를 수 있나(결정 124 — 플레이 시험 T32): 거짓이면 솔로 · 방 설정의 거래 줄은 「준비 중」(끔에 고정),
        /// 새 판은 거래 없이 시작하고, 거래가 켜진 채 저장된 판을 이어 하거나 방 판에 들어가면 그 판의 거래를 화면에서 잠근다(<see cref="TradeLocked"/>). 기능 코드는 그대로 둔다(규칙 · 창 · 연출).
        /// </summary>
        public static readonly bool TradingReady = false;

        /// <summary>이 판의 거래를 화면에서 잠갔나(결정 124 — 이어 한 판 · 방 판): [거래] · 염력 거래 단추가 서지 않는다. 확인용 판(StartSeats 등 — 규칙 기본값)은 잠그지 않는다.</summary>
        public bool TradeLocked { get; private set; }

        /// <summary>
        /// 보드 HUD의 [거래]: 거래 창을 연다(거래할 상대가 있고 입력이 열려 있을 때만 — 남의 턴에도). partner = 처음 고를 상대(psychic B 값 단추 「거래 ○○」 — 결정 84 T12 ②, 없거나 못 고르면 첫 상대).
        /// </summary>
        public bool OpenTrade(int partner = -1)
        {
            if (!InputOpen || TradePartners().Count == 0) return false;
            Hud?.CancelWarp();
            ShowOver(new TradeScreen(this, partner));
            return true;
        }

        /// <summary>거래 창을 닫고 보드로 돌아간다.</summary>
        public void CloseTrade()
        {
            if (!(Stage.Current is TradeScreen)) return;
            Stage.Shut();
            Sync();
        }

        /// <summary>거래를 제안한다(거래 창이 부른다): 내 턴이면 맵 행동의 답, 남의 턴이면 명령 Propose(결정 75 B5 나). 받아들여졌으면 true.</summary>
        public bool ProposeTrade(ChoiceAnswer offer)
        {
            if (Client == null || offer == null) return false;
            var choice = Client.Choice(ChoiceKind.MapAction);
            if (choice != null) return Answer(choice, offer).Accepted;
            return Guard(() => Client.Propose(offer, NowMs)).Accepted;
        }

        /// <summary>내가 낸 거래 제안의 응답을 기다리는 중인가(상대에게 Respond/Trade가 열려 있다 — 보드 HUD의 안내 줄).</summary>
        public bool TradeWaiting
        {
            get
            {
                var view = Client != null ? Client.View : null;
                if (view == null) return false;
                foreach (var c in view.Pending)
                    if (c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade && c.Subject == Client.Seat && c.Seat != Client.Seat && !c.Answered) return true;
                return false;
            }
        }

        /// <summary>
        /// S41 프롬프트의 [수락]이 줄 운명의 변화 카드를 골라야 할 때: 고르는 창을 연다. 배틀 화면이 떠 있으면(배틀 독의 [수락] — 결정 80 N2 나) 그 위에
        /// (연출이 돌지 않고 그 위에 다른 창이 없을 때).
        /// </summary>
        public bool OpenTradePick()
        {
            bool open = Stage.Current is BattleScreen battle ? AnswerOpen && battle.Over == null : InputOpen;
            if (!open || TradePickScreen.Asked(this, TradePickScreen.ChoiceOf(this)) == 0) return false;
            ShowOver(new TradePickScreen(this));
            return true;
        }

        /// <summary>줄 카드 고르는 창을 닫는다(응답은 프롬프트에 그대로 남는다).</summary>
        public void CloseTradePick()
        {
            if (!(Top is TradePickScreen)) return;
            ShutTop();
            Sync();
        }

        /// <summary>보드 HUD의 [도전]이 상대를 골라야 할 때(여럿 · 원격): 고르는 창을 연다.</summary>
        public bool OpenChallenge()
        {
            if (!InputOpen || !ChallengeScreen.Needed(ChallengeScreen.Legal(this))) return false;
            ShowOver(new ChallengeScreen(this));
            return true;
        }

        /// <summary>도전 상대 고르는 창을 닫고 보드로 돌아간다.</summary>
        public void CloseChallenge()
        {
            if (!(Stage.Current is ChallengeScreen)) return;
            Stage.Shut();
            Sync();
        }

        // ── 울트라 버스트의 균열 카드 · 축복 · 탈락(S58 · S59 · S61 — M6 「UB 화면 묶음 2」) ──

        // 균열 카드 묶음(붙이기 · UB 격파 뒤 버리기·옮기기)이 열렸으면 가운데 창을 연다 — 배틀 화면이 떠 있으면(UB 격파 뒤) 그 위에, 묶음이 끝나면 닫는다.
        // 내가 답한 뒤에도 남이 고르는 동안은 「기다리는 중」으로 남는다
        void SyncBreach(bool menu)
        {
            var view = Client.View;
            bool active = BreachScreen.Active(view) && (BreachScreen.ChoiceOf(Client) != null || Top is BreachScreen);
            if (!active)
            {
                if (Top is BreachScreen) ShutTop();
                return;
            }
            if (Top is BreachScreen || menu || !BoardOpen) return;
            if (Stage.Current is BattleScreen battle) { if (battle.Ready && battle.Over == null) battle.ShowOver(new BreachScreen(this)); }
            else if (Stage.Current == null) Stage.Open(new BreachScreen(this));
        }

        // 라운드 끝의 「UB 턴을 건너뛰겠습니까?」(결정 84 U2) · 모두 탈락한 뒤의 부활 고르기: 보드에서 저절로 열고, 답하면 닫는다.
        // 여럿 판의 건너뛰기는 다수결이라(결정 87 R3) 내가 답한 뒤에도 묶음이 끝날 때까지 현황 창으로 남는다
        void SyncBless(bool menu)
        {
            bool asked = BlessScreen.ChoiceOf(Client) != null || (Top is BlessScreen && BlessScreen.Voting(Client));
            if (Top is BlessScreen shown)
            {
                if (!asked && !shown.IsPanel) ShutTop();
                return;
            }
            if (asked && !menu && BoardOpen && Stage.Current == null) Stage.Open(new BlessScreen(this));
        }

        /// <summary>축복 판을 지금 열 수 있나: 울트라 버스트 판의 보드이고 위에 다른 창이 없다(여는 조건은 정비 판과 같다 — <see cref="CanOpenParty"/>).</summary>
        public bool CanOpenBless => Client != null && Client.View != null && Client.View.Ub != null && Stage.Current == null && CanOpenParty;

        /// <summary>
        /// 보드 위에 연 창이 접혀 머리 줄 띠만 남았나(축복 물음 · 균열 카드 창의 [접기] — 플레이 시험 T21): 그동안 보드 카메라(끌기 · 휠 · Q · E)가 움직인다 — 판을 보고 정하도록.
        /// 칸 고르기 · 행동은 그대로 닫혀 있다(<see cref="InputOpen"/>). 배틀 화면 위에 연 창은 치지 않는다(가운데 필드가 배틀이다).
        /// </summary>
        public bool Folded => Stage.Info == null && ((Stage.Current is BlessScreen bless && bless.Folded) || (Stage.Current is BreachScreen breach && breach.Folded));

        /// <summary>창을 접거나 폈다(그 창이 부른다): 보드 카메라 입력을 다시 맞춘다.</summary>
        public void Refold() => Restage();

        /// <summary>아르세우스의 축복 판(S59 — 결정 84 M4 가, 위 띠 축복 칩 <see cref="BoardUb.Bless"/>를 누르면 연다)을 HUD 위에 연다.</summary>
        public bool OpenBless()
        {
            if (!CanOpenBless) return false;
            ShowOver(new BlessScreen(this));
            return true;
        }

        /// <summary>축복 판을 닫는다(물음·부활 고르기는 답할 때까지 닫지 않는다).</summary>
        public void CloseBless()
        {
            if (!(Top is BlessScreen bless) || (!bless.IsPanel && BlessScreen.ChoiceOf(Client) != null)) return;
            ShutTop();
            Sync();
        }

        /// <summary>그 좌석의 말·칩이 지금 보이는 탈락 모습(91행 — 보기 값 + 대기열의 남은 탈락·부활 단계).</summary>
        public FallBeats.Pose FallShown(int seat)
        {
            var view = Client != null ? Client.View : null;
            bool out_ = view != null && seat >= 0 && seat < view.Seats.Length && view.Seats[seat].Eliminated;
            return FallBeats.Shown(Fx != null ? Fx.Sequencer : null, seat, out_);
        }

        // 탈락한 좌석의 말은 회색으로 바랜다(91행 — 부활하면 색을 되찾는다)
        void Fall(FxSequencer seq)
        {
            var view = Client != null ? Client.View : null;
            if (board == null || board.Pieces == null || view == null) return;
            for (int s = 0; s < view.Seats.Length; s++) board.Pieces.Grey(s, FallShown(s).Grey);
        }

        // ── 운명의 변화(S30 손패 창 · 6장째 버리기 · 펄기아 순간이동 — M5 「운명·응답 화면」) ──

        bool fateAuto;   // 버리기 때문에 저절로 연 손패 창(버리기가 끝나면 닫는다)

        /// <summary>
        /// 손패 창(S30)을 지금 열 수 있나: 보드에 있고 위에 다른 창이 없다(여는 조건은 정비 판과 같다).
        /// 배틀 화면이 떠 있으면 그 위에 보기만 하는 창으로 연다(결정 80 N3 나 — 배틀 기둥의 [운명의 변화 n/5]):
        /// 연출이 돌지 않고(<see cref="BattleScreen.Ready"/>) 그 위에 다른 창이 없을 때. 배틀 안에서 카드를 내는 것은 독의 물음이 따로 받는다.
        /// </summary>
        public bool CanOpenFate => Stage.Current is BattleScreen battle ? OnBoard && battle.Ready && battle.Over == null : Stage.Current == null && CanOpenParty;

        /// <summary>보드 HUD의 [운명의 변화]: 손패 창을 HUD 위에 연다.</summary>
        public bool OpenFate()
        {
            if (!CanPeek) return false;   // 결정 125: 열 수 없던 때(물음 창 · 연출 중의 배틀 · 결과 창)는 정보 창 층에 보기만 하는 창으로
            if (Front is FateScreen) return true;
            bool here = CanOpenFate;
            if (here) Hud?.CancelWarp();
            fateAuto = false;
            ShowInfo(new FateScreen(this), here);
            return true;
        }

        /// <summary>손패 창을 닫는다(버리는 중에는 닫지 않는다).</summary>
        public void CloseFate()
        {
            if (ShutInfo(Stage.Info is FateScreen)) return;
            if (!(Top is FateScreen fate) || fate.Discarding) return;
            fateAuto = false;
            ShutTop();
            Sync();
        }

        // 6장째 버리기가 열렸으면 앞 연출이 다 보인 뒤 손패 창을 연다(남의 턴 · 배틀 결과 창 위에도 — 결정 75 B1 ②), 다 버렸으면 저절로 연 창을 닫는다
        void SyncFate(bool menu)
        {
            bool discard = Client.Choice(ChoiceKind.Discard) != null;
            var fate = Top as FateScreen;
            if (discard && fate == null && !menu && BoardOpen && !(Stage.Current is EncounterScreen))
            {
                Hud?.CancelWarp();
                fateAuto = true;
                if (Stage.Current is BattleScreen battle) battle.ShowOver(new FateScreen(this));
                else Stage.Open(new FateScreen(this));
            }
            else if (!discard && fate != null && fateAuto)
            {
                fateAuto = false;
                ShutTop();
            }
        }

        /// <summary>
        /// 펄기아(1장 · 3장): 손패 창을 닫고 보드에서 갈 칸을 고르게 한다(<see cref="BoardHud.Warping"/> — 갈 수 있는 칸 = 그 답들의 칸). 내 맵 행동이 열려 있을 때만.
        /// </summary>
        public bool BeginWarp(List<ChoiceAnswer> answers)
        {
            if (answers == null || answers.Count == 0 || Client.Choice(ChoiceKind.MapAction) == null || Hud == null) return false;
            if (Top is FateScreen) ShutTop();
            fateAuto = false;
            Hud.BeginWarp(answers);
            Sync();
            return true;
        }

        int warpHidden = -1;   // 순간이동 틈 속이라 숨긴 말의 좌석(없으면 −1)

        // 펄기아 순간이동(04 §8-8 14행): 말이 출발 칸에서 떠올라 틈으로 사라졌다가 도착 칸 위에서 떨어져 내려앉는다. 카메라의 보는 점은 출발 칸 → 도착 칸
        void Warp(FxSequencer seq)
        {
            var step = TeleportBeats.StepOf(seq.Current);
            var t = TeleportBeats.Event(step);
            if (step == null || t == null || board == null || board.Pieces == null || seq.LocalMs < step.StartMs) return;
            if (t.From < 0 || t.To < 0 || t.From >= board.Board.Count || t.To >= board.Board.Count) return;
            int local = step.Local(seq.LocalMs);
            var pose = TeleportBeats.At(local, step.Ms);
            var piece = board.Pieces.Of(t.Seat);
            if (piece == null || piece.Root == null) return;
            if (pose.Hidden != !piece.Root.activeSelf) piece.Root.SetActive(!pose.Hidden);
            warpHidden = pose.Hidden ? t.Seat : -1;
            var ground = board.CellTop(pose.AtDest ? t.To : t.From);
            if (seq.LocalMs >= step.StartMs + step.Ms && Shared(t.Seat, t.To)) board.Pieces.Land(t.Seat, t.To);
            else board.Pieces.Move(t.Seat, ground + Vector3.up * pose.Height);
            Follow(step, ground, local);
        }

        // 왜곡 게이트 이동(04 §8-8 15행 — M6 「UB 보드 화면」): 말이 출발 게이트 속으로 가라앉아 사라지고, 보라 점선(보드 HUD — BoardHud.ShowGate)이 이어지는 동안 카메라의 보는 점이 건너편으로 가고,
        // 짝 게이트에서 솟아오른다. 틈 속의 말은 순간이동처럼 숨겼다가 단계가 끝나면 보인다(ShowWarped)
        void Gate(FxSequencer seq)
        {
            var step = GateBeats.StepOf(seq.Current);
            var g = GateBeats.Event(step);
            if (step == null || g == null || board == null || board.Pieces == null || seq.LocalMs < step.StartMs) return;
            if (g.FromCell < 0 || g.ToCell < 0 || g.FromCell >= board.Board.Count || g.ToCell >= board.Board.Count) return;
            int local = step.Local(seq.LocalMs);
            var pose = GateBeats.At(local, step.Ms);
            var piece = board.Pieces.Of(g.Seat);
            if (piece == null || piece.Root == null) return;
            if (pose.Hidden != !piece.Root.activeSelf) piece.Root.SetActive(!pose.Hidden);
            warpHidden = pose.Hidden ? g.Seat : -1;
            if (seq.LocalMs >= step.StartMs + step.Ms && Shared(g.Seat, g.ToCell)) board.Pieces.Land(g.Seat, g.ToCell);
            else board.Pieces.Move(g.Seat, board.CellTop(pose.AtDest ? g.ToCell : g.FromCell) + Vector3.down * (pose.Depth * GateBeats.SinkDepth));
            Follow(step, Vector3.Lerp(board.CellTop(g.FromCell), board.CellTop(g.ToCell), pose.Cross), local);
        }

        // 순간이동 단계가 끝났다(한 번에 건너뛰었어도): 숨긴 말을 다시 보인다
        void ShowWarped(FxEntry entry)
        {
            if (warpHidden < 0 || board == null || board.Pieces == null) return;
            var piece = board.Pieces.Of(warpHidden);
            if (piece != null && piece.Root != null) piece.Root.SetActive(true);
            warpHidden = -1;
        }

        // ── S41 응답 프롬프트의 시계(04 §8-8 98행 — 결정 29 ⑥ · rulings G9) ──

        /// <summary>확인용 좌석 여럿 판의 선택 시계(ClockKind마다 ms — <c>SessionConfig.ClockMs</c>). 솔로·이어 받은 판은 null(시계 없음).</summary>
        public int[] Clocks { get; private set; }

        readonly Dictionary<int, long> promptSeen = new Dictionary<int, long>();

        /// <summary>내 선택이 시간 초과로 기본값 답이 된 세션 시각(없으면 long.MinValue) — 「시간 초과 · 사용 안 함」이 <see cref="PromptBeats.TimeoutMs"/> 동안 남는다.</summary>
        public long TimedOutAtMs { get; private set; } = long.MinValue;

        /// <summary>마지막 공격·포획 굴림의 면(굴림 + 면 바꾸기를 넣은 것) — 디아루가 재굴림 줄(43행)이 다시 굴리지 않은 주사위를 그린다.</summary>
        public IReadOnlyList<AttackDieFace> RollFaces => rollFaces;
        readonly List<AttackDieFace> rollFaces = new List<AttackDieFace>();

        void NotePrompts(IReadOnlyList<GameEvent> events)
        {
            if (Client == null) return;
            foreach (var e in events)
            {
                if (e is DiceRolled d && d.Faces != null)
                {
                    rollFaces.Clear();
                    rollFaces.AddRange(d.Faces);
                }
                else if (e is DiceFacesChanged f && f.Changes != null)
                    foreach (var ch in f.Changes)
                    {
                        if (ch.Index < 0) continue;
                        while (rollFaces.Count <= ch.Index) rollFaces.Add(AttackDieFace.None);
                        rollFaces[ch.Index] = ch.After;
                    }
                if (e is ChoiceRequested r && r.Seat == Client.Seat && !promptSeen.ContainsKey(r.Id)) promptSeen[r.Id] = NowMs;
                else if ((e is ChoiceAnswered a && a.ByTimeout && promptSeen.ContainsKey(a.Id))
                    || (e is ChallengeAnswered c && c.ByTimeout && c.To == Client.Seat) || (e is TradeAnswered t && t.ByTimeout && t.To == Client.Seat))   // 도전·거래 응답의 시간 초과(기본값 = 거절)
                {
                    TimedOutAtMs = NowMs;
                    timedOut = true;
                }
            }
        }

        bool timedOut;

        // 내 선택이 시간 초과로 기본 답이 됐다(M8 · M9 마감 감사 2차 참고 ④ — 손님이 [확인]을 누르지 않은 막 안내 창이 시간 초과 뒤에도 남았다): 막 안내 창과
        // 그 선택에 답하던 창(마을 · 제작 · 진화 · 사당 · 도전 · 거래 · 거래 수락 고르기 · 운명 손패)을 닫는다. 사람이 연 읽기 창(기록 · 도감 · 스탬프 · 정비 · 트레이너 카드 · 상성 · 설명서)은 그대로 [가정 plan §5 #777].
        // 창마다의 「지금은 닫지 않는다」(버리는 중인 손패 · 진화 연출 · 사당의 남은 연출)는 그 닫기 함수가 지킨다
        void CloseTimedOut()
        {
            timedOut = false;
            switch (Top)
            {
                case ActScreen _: CloseAct(); break;
                case VillageScreen _: CloseVillage(); break;
                case CraftScreen _: CloseCraft(); break;
                case EvolveScreen _: CloseEvolve(); break;
                case ShrineScreen _: CloseShrine(); break;
                case ChallengeScreen _: CloseChallenge(); break;
                case TradeScreen _: CloseTrade(); break;
                case TradePickScreen _: CloseTradePick(); break;
                case FateScreen _: CloseFate(); break;
            }
        }

        /// <summary>그 선택의 남은 시간(ms)과 전체 길이. 시계가 없는 선택이면 −1.</summary>
        public int ClockLeftMs(PendingChoice choice, out int total)
        {
            total = 0;
            if (Room.InGame) return RoomClockLeftMs(choice, out total);
            if (choice == null || Clocks == null || (int)choice.Clock >= Clocks.Length || choice.Clock == ClockKind.None) return -1;
            total = Clocks[(int)choice.Clock];
            if (total <= 0) return -1;
            long seen = promptSeen.TryGetValue(choice.Id, out var at) ? at : NowMs;
            return (int)Math.Max(0, total - (NowMs - seen));
        }

        // 방 판(M7 화면 ②): 방장이 센 내 좌석 시계(받은 뒤 지난 만큼 화면에서 줄인다 — ClockView). 그 선택에 흐르는 시계가 아니면 −1
        int RoomClockLeftMs(PendingChoice choice, out int total)
        {
            total = 0;
            var live = Room.Live;
            var c = live.Clock;
            if (choice == null || c == null || Client == null || Client.Seat < 0 || Client.Seat >= c.Seats.Count) return -1;
            var s = c.Seats[Client.Seat];
            if (s.Running == ClockKind.None || s.ChoiceId != choice.Id) return -1;
            long elapsed = ClockView.Elapsed(c, live.NowMs - live.ClockAtMs);
            long left = ClockView.TotalMs(s, elapsed);
            total = (int)Math.Max(left, ClockView.LengthMs(live.Room?.Settings?.ClockLengths() ?? ClockSettings.For(c.Preset), s.Running, choice.Kind) +(ClockView.UsesBank(s.Running) ? s.BankMs : 0));
            return total <= 0 ? -1 : (int)left;
        }

        /// <summary>방 판의 화면 덮개 층(응답 시계 · 일시정지 · 투표 · 알림 · 재접속 덮개 S11 — M7 화면 ②). 방 판이 아니면 붙지 않는다.</summary>
        public RoomLayer RoomLayer => roomLayer;

        RoomLayer roomLayer;

        /// <summary>그 선택이 처음 내 보기에 열린 세션 시각(모르면 지금).</summary>
        public long PromptSeenMs(PendingChoice choice) => choice != null && promptSeen.TryGetValue(choice.Id, out var at) ? at : NowMs;

        void ShowOver(IUiScreen screen)
        {
            if (Stage.Current is BattleScreen battle) battle.ShowOver(screen);   // 배틀 중 정비 판 · 카드 확대: 배틀 화면은 그대로 두고 그 위에
            else Stage.Open(screen);
            Restage();
            Hud?.Refresh();
            wasOpen = InputOpen;
        }

        /// <summary>그 정비를 지금 낼 수 있나(엔진의 검사 그대로 — <see cref="SeatClient.CanMaintain"/>).</summary>
        public RejectReason CanMaintain(Maintenance m) => Client != null ? Client.CanMaintain(m) : RejectReason.BadCommand;

        /// <summary>정비를 낸다(화면이 부른다). 받아들여지면 갱신이 바로 와 화면이 다시 그려진다.</summary>
        public SubmitResult Maintain(Maintenance m) => Guard(() => Client.Maintain(m, NowMs));

        // ── 2막 이후·종막(04 S14·S54·S62~S64, M5 「막·종막 화면」) ──

        /// <summary>내 좌석이 지금 칸에서 3막 시작의 사당 공물(심장 회복)을 바칠 수 있나 — 보드 HUD의 [공물] 단추(<see cref="BoardHud.Offer"/>).</summary>
        public bool HeartOffer => Client != null && Client.View != null && Client.View.Hos != null && HosFlow.CanRestoreHeart(Client.View, Ctx, Client.Seat);

        /// <summary>종막 화면: 0 없음 · <see cref="FinaleAsk"/> 종막 선택(S62·S63 — 묻는 중 · 95·96행 연출) · <see cref="FinaleResult"/> 결과(S64 — 97행 · 끝난 판).</summary>
        public const int FinaleAsk = 1, FinaleResult = 2;

        /// <summary>
        /// 지금 띄울 종막 화면(<see cref="FinaleAsk"/> · <see cref="FinaleResult"/> · 0): 결과(97행)가 재생 중이면 결과 · 종막 선택·순서 공개(95·96행)가 아직 남았으면 종막 선택 ·
        /// 끝난 판(배틀 화면이 닫혔고 결과 단계가 남지 않았다)이면 결과 · 내 종막 물음이 열렸고 대기열이 비었으면(종막 카드가 다 착지했다) 종막 선택.
        /// </summary>
        public int FinaleStage()
        {
            var view = Client != null ? Client.View : null;
            if (view == null || (view.Hos == null && view.Ub == null)) return 0;   // 울트라 버스트도 결과 창(S64 — M6 「UB 배틀 화면 묶음」)
            var seq = Fx != null ? Fx.Sequencer : null;
            bool asking = false, resultLeft = false, resultOn = false;
            if (seq != null)
                foreach (var entry in seq.Queued)
                {
                    bool cur = ReferenceEquals(entry, seq.Current);
                    foreach (var st in entry.Steps)
                    {
                        bool done = cur && seq.LocalMs >= st.EndMs;
                        if (done) continue;
                        if (st.Row == FinaleRows.Ending || st.Row == FinaleRows.Order) asking = true;
                        else if (st.Row == UbRows.Lost) resultLeft = true;   // 86행 균열 패배 덮개가 다 돈 뒤에 결과 창
                        else if (st.Row == FinaleRows.Result)
                        {
                            resultLeft = true;
                            if (cur && seq.LocalMs >= st.StartMs) resultOn = true;
                        }
                    }
                }
            if (resultOn) return FinaleResult;
            if (asking) return FinaleAsk;
            if (view.Phase == Phase.GameOver) return Battle == null && !resultLeft ? FinaleResult : 0;
            var mine = Client.Choice(ChoiceKind.Ending);
            if (mine != null && (seq == null || seq.IsEmpty || Stage.Current is EndingScreen)) return FinaleAsk;
            return 0;
        }

        int finaleShown;

        // 게임 끝(97행)의 예비: 승리 좌석의 말로 카메라의 보는 점이 다가간다(방향·줌 그대로 — 막 카드와 같다)
        void ResultFocus(FxSequencer seq)
        {
            var step = ResultBeats.StepOf(seq.Current);
            var ended = ResultBeats.Ended(step);
            if (ended == null || ended.Winners == null || ended.Winners.Length == 0 || board == null || rig == null || seq.LocalMs < step.StartMs || Client == null || Client.View == null) return;
            int seat = ended.Winners[0];
            int cell = seat >= 0 && seat < Client.View.Seats.Length ? Client.View.Seats[seat].Cell : -1;
            if (cell >= 0) Follow(step, board.CellTop(cell), step.Local(seq.LocalMs));
        }

        /// <summary>결과 화면(S64)의 [타이틀로]: 판을 떠나 타이틀로 돌아간다(끝난 판의 저장은 「끝남」으로 남는다).</summary>
        public void LeaveFinished() => OpenTitle();

        // ── 프레임 ──

        /// <summary>세션 시각을 dtMs만큼 흘리고 정해진 일(AI 답·시간 초과)을 처리한다. 프레임마다.</summary>
        public void Tick(int dtMs)
        {
            Tutorial.Tick();   // 튜토리얼: 떠난 판 놓기 · 장 경계(기록) · 각본 끝 → 끝 창(M6.5)
            Room.Tick();       // 방(M7 화면 ①): 접속 · 로비 · 게임 시작 · 실패를 화면에
            if (Room.InGame || roomLayer != null) (roomLayer ??= new RoomLayer(this)).Tick();   // 방 판(화면 ②): 응답 시계 · 일시정지 · 투표 · 알림 · 재접속 덮개
            if (Session == null && roomLink == null) return;
            NowMs += Math.Max(0, dtMs);
            if (Session != null)
                try
                {
                    Session.Tick(NowMs);
                    if (tickFailed)
                    {
                        // 밀려 있던 일이 쓰였다(세션은 못 쓴 일을 다음 Tick에 다시 한다 — 던지지 않았으면 됐다)
                        tickFailed = false;
                        SaveError = null;
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    SaveError = e.Message;   // 정해진 일(AI 답)의 로그 줄을 쓰지 못했다: 세션이 되돌렸고 다음 프레임에 다시 한다
                    tickFailed = true;
                }
            PassEmptyReaction();
            // 6장째 버리기(S30): 앞 연출이 다 보인 뒤 버리기 창을 연다(배틀 결과 창 위에도)
            if (Client != null && Client.Choice(ChoiceKind.Discard) != null && !(Top is FateScreen) && BoardOpen && !MenuUp) Sync();
            // 균열 카드 묶음 · 축복 물음(M6 「UB 화면 묶음 2」): 앞 연출(턴 시작 띠 · 배틀 결과)이 다 보인 뒤 창을 연다.
            // 축복 물음은 보드에서만 연다(SyncBless) — 배틀 결과 창이 떠 있는 동안 프레임마다 Sync하면 결과 창을 매번 새로 지어 [확인]이 배치되지 않는다(M6.5 앵커 표가 찾음)
            else if (Client != null && ((BreachScreen.ChoiceOf(Client) != null && !(Top is BreachScreen)) || (BlessScreen.ChoiceOf(Client) != null && !(Top is BlessScreen) && Stage.Current == null)) && BoardOpen && !MenuUp) Sync();
            Hud?.TickPrompt();   // S41 응답 프롬프트의 남은 시간 막대 · 시간 초과 표시(98행 — 대기열 밖)
            if (sitesBusy && (Fx == null || Fx.Sequencer.IsEmpty)) SyncFigures();   // 전설 장소 피규어의 나타남 · 사라짐(결정 123 — 대기열이 빈 뒤, 대기열이 도는 동안은 ShowFx가 부른다)
            (Top as BreachScreen)?.Tick();   // 균열 카드 창 · 축복 물음의 남은 시간
            (Top as BlessScreen)?.Tick();
            Ripple();   // 깨어진 세계의 잔물결은 대기열이 비어 있어도 돈다(M9 다듬기 P39)
            if (Fx == null || Fx.Sequencer.IsEmpty)   // 남의 행동 보기 창(결정 120 ①): 대기열이 빈 동안(남이 고르는 중)에도 열고 닫고 · 가리키는 카드를 따라간다
            {
                SyncWatch();
                if (Fx != null) watch?.ShowFx(Fx.Sequencer);
            }
            ShowRoads(dtMs);
            Ticked?.Invoke();
        }

        /// <summary>
        /// worker B가 놓은 길(감사 G5-1 — rulings A36 「Mark the tiles」)을 보드에 칠한다: 보기의 길 칸(공개 값 — 방 판 손님 · 남의 화면도 같다).
        /// 새 칸은 말이 걷는 연출이 끝난 뒤(화면 대기열이 비면) 짧게 커지며 선다.
        /// </summary>
        void ShowRoads(int dtMs)
        {
            if (board == null) return;
            var view = Client != null ? Client.View : null;
            var roads = board.Roads;
            if (roads == null) return;
            roads.Show(view != null && view.Phase != Phase.Setup ? view.Roads : null, Fx == null || Fx.Sequencer.IsEmpty, dtMs);
        }

        /// <summary>
        /// 확인용(플레이어 확인 · 플레이 시험): 세션 시각과 연출 시각을 함께 ms만큼 더 보낸다 — 상대(AI)의 답을 기다리는 방장 멈춤과 화면 연출을 같이 건너뛴다
        /// (실시각으로는 공격 하나의 연출이 8초쯤이다). 게임 화면은 부르지 않는다. 무대의 트윈 실행기가 도는 중(연출 콜백 안)에는 부르지 못한다.
        /// </summary>
        public void Skip(int ms)
        {
            // 건너뛰기는 무음(04 §8-4): 건너뛴 연출의 소리 단서를 한꺼번에 내지 않는다 — 단서 시각 로그는 SoundOnSkip으로 켠다.
            // 세션 시각을 보내는 동안(상대의 답 → 갱신)부터 막는다 — 「연출 끄기」의 대표 소리도 갱신을 받을 때 난다
            var seq = Fx != null ? Fx.Sequencer : null;
            bool was = seq != null && seq.Muted;
            if (seq != null && !SoundOnSkip) seq.Muted = true;
            try
            {
                Tick(ms);
                Stage.Tweens.Advance(Math.Max(0, ms));
            }
            finally
            {
                if (seq != null) seq.Muted = was;
            }
        }

        /// <summary>
        /// 확인용(캡처 · 플레이 시험 · 플레이어 확인): 화면 대기열에서 그 단계의 <paramref name="permille"/> 지점까지 남은 시간(ms) — 연출의 한 순간(볼이 열린 때 · 토큰이 떨어지는 중)으로
        /// <see cref="Skip"/>할 때 쓴다. 그런 단계가 없으면 대기열 전체. 게임 화면은 쓰지 않는다.
        /// </summary>
        public int MsUntil(Func<FxStep, bool> match, int permille = 0)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null) return 0;
            int ms = 0;
            foreach (var entry in seq.Queued)
            {
                int local = ReferenceEquals(entry, seq.Current) ? seq.LocalMs : 0;
                foreach (var s in entry.Steps)
                    if (match(s)) return Math.Max(0, ms + s.StartMs + (int)((long)s.Ms * permille / 1000) - local);
                ms += entry.LengthMs - local;
            }
            return ms;
        }

        /// <summary>
        /// 확인용(캡처 · 플레이 시험 · 플레이어 확인): 화면 대기열이 다 비고 클릭 막기 시간까지 지나 입력이 열리는 데 남은 시간(ms) —
        /// 셋업 연출(고향 마을에 말이 떨어짐 → 카드가 들어옴)을 <see cref="Skip"/>으로 건너뛰고 다음 셋업 화면을 누를 때 쓴다. 게임 화면은 쓰지 않는다.
        /// </summary>
        public int QueueLeftMs
        {
            get
            {
                var seq = Fx != null ? Fx.Sequencer : null;
                if (seq == null) return 0;
                int ms = FxTune.ClickGuardMs.Value;
                foreach (var entry in seq.Queued) ms += entry.LengthMs - (ReferenceEquals(entry, seq.Current) ? seq.LocalMs : 0);
                return ms;
            }
        }

        /// <summary>
        /// 확인용(캡처 · 플레이 시험 · 플레이어 확인): 화면 대기열에서 턴 넘김(요약 · 넘김 띠 · 내 턴 컷신)이 든 항목 앞까지 남은 시간(ms) —
        /// 게임 시작의 고른 뒤·공개(셋업 1·3행) → 숨겨진 장소 배치 → 서막 카드 → 조무래기 놓기 → 1막 카드(약 20초)를 <see cref="Skip"/>으로 건너뛸 때 쓴다. 그런 항목이 없으면 대기열 전체. 게임 화면은 쓰지 않는다.
        /// </summary>
        public int OpeningMs
        {
            get
            {
                var seq = Fx != null ? Fx.Sequencer : null;
                if (seq == null) return 0;
                int ms = 0;
                foreach (var entry in seq.Queued)
                {
                    if (entry.Step(FxRows.TurnSummary) != null || entry.Step(FxRows.Handoff) != null || entry.Step(FxRows.MyTurn) != null || TurnStartBeats.StepOf(entry) != null) break;
                    ms += entry.LengthMs - (ReferenceEquals(entry, seq.Current) ? seq.LocalMs : 0);
                }
                return ms;
            }
        }

        /// <summary>
        /// 마우스 자리(물리 px, 왼쪽 아래가 (0, 0))와 이번 프레임에 왼쪽 단추를 눌렀는지를 받는다(프레임마다). 마우스가 HUD·창 위에 있으면 보드로 새지 않는다 —
        /// 휠 줌을 끄고 칸을 고르지 않는다. 보드 위면 그 칸을 HUD에 알리고(경로 표시), 눌렀으면 그 칸으로 이동을 낸다. 돌려주는 값 = 마우스 아래의 칸(없으면 −1).
        /// </summary>
        public int Pointer(float x, float y, bool pressed) => Point(x, y, pressed, false, false);

        /// <summary>
        /// 핑 키(Alt)를 누르고 있나 — <see cref="ScreenBoot"/>가 프레임마다 넣는다(결정 97 · 102, 02 §4-8). 켜져 있는 동안 누르기는 걷기 전 확인 · 단추 대신 그 자리(보드의 누른 자리 · 보드 밖 공개 대상)에 핑을 찍는다 —
        /// 바로 떼면 「여기 봐」, 누르고 있거나 끌면 종류 고리(방 판만 — <see cref="RoomLayer.PingInput"/>).
        /// </summary>
        public bool PingKey { get; set; }

        /// <summary>
        /// 마우스 자리와 단추가 눌려 있는지를 받는다(프레임마다 — 실행 중의 길, 결정 66 ②): 왼쪽 단추를 누른 자리에서 문턱(<see cref="BoardDrag.Threshold"/>)보다 멀리 끌면
        /// 보드를 옮기고(카메라의 보는 점 — 방향·줌은 그대로, 잡은 땅이 커서를 따라온다), 끌지 않고 떼면 그때 칸 누르기다. 가운데·오른쪽 단추(other)도 끌지만 누르기는 없다.
        /// HUD·창 위에서 시작한 끌기는 무시한다. 연출 중(입력이 닫혀 있어도)에도 끌 수 있고, 화면(타이틀·창)이 덮고 있으면 끌 수 없다.
        /// </summary>
        public int Pointer(float x, float y, bool left, bool other) => Point(x, y, left, other, true);

        /// <summary>
        /// 보드 위 판의 글 칸에 글을 치는 중인가(사당 판의 야생 카드 찾기 — M9 다듬기 P22): 그동안 글쇠가 단축키(카메라 Q · E · 정비 T · 스페이스 바)로 새지 않는다. 칸의 포커스가 켜고 끈다.
        /// </summary>
        public bool Typing
        {
            get => typing;
            set
            {
                typing = value;
                if (rig != null) rig.KeysEnabled = !value;
            }
        }
        bool typing;

        /// <summary>Esc: 걷기 전 확인을 한 단계 물린다(결정 70 ⑱ · 결정 71 ② — 마지막 경유 칸을 빼고, 없으면 고른 칸을 푼다, <see cref="BoardHud.Back"/>). 보드에 있고 위에 창이 없을 때만 — 물렸으면 true.</summary>
        public bool CancelPick() => OnBoard && Stage.Current == null && Hud != null && Hud.Back();

        /// <summary>
        /// Esc(결정 80 N3 나): 맨 위에 닫을 수 있는 창이 떠 있으면 그 창의 [닫기](= 창 밖 누르기)와 같은 길로 닫고, 창이 없으면 걷기 전 확인을 한 단계 물린다(<see cref="CancelPick"/>).
        /// 창마다의 「지금은 닫지 않는다」(버리는 중인 손패 창 · 진화 연출·물음 · 사당 판의 내 선택·남은 연출)는 그 닫기 함수가 그대로 지킨다.
        /// 답해야 하는 화면(조우 창 · 배틀 화면 · 종막 선택 · 결과 창 · 셋업)은 Esc를 받지 않는다. 무엇인가 닫거나 물렸으면 true.
        /// M9 다듬기 P23: 게임 메뉴는 한 단계씩(나가기 물음 · 방 판의 안쪽 쪽 → 메뉴 → 닫기, 메뉴에서 연 설정 → 메뉴 — <see cref="BoardMenuScreen.Escape"/>),
        /// 배틀 독의 [뒤로]가 있는 줄(가방 · 알파 스트라이크 타입 · 대기 포켓몬 기술 · 회복할 포켓몬)은 기술 줄로(<see cref="BattleScreen.CloseRow"/> — 튜토리얼 판은 받지 않는다).
        /// </summary>
        public bool Escape()
        {
            if (roomLayer != null && roomLayer.ChatEscape()) return true;   // 방 채팅 글 칸(결정 140 ②) = 포커스를 놓고 접기 — 아래 창은 그대로
            if (Stage.Current is LobbyScreen lobby && lobby.Chat != null && lobby.Chat.Escape()) return true;
            if (Tutorial.DialogUp || Tutorial.BookUp) return Tutorial.Escape();   // 튜토리얼 끝내기 창 = [계속하기](끝 창은 받지 않는다) · 끝 창 · 목차 위에 덮은 룰 설명서 = 닫기
            if (EscapeBook()) return true;   // 룰 설명서 큰 창 · 크레딧(타이틀에서 연 것도 — GameFlow.Rulebook.cs)
            if ((Stage.Info ?? Stage.Current) is DexScreen dex)   // 도감은 타이틀에서 연 것도 닫는다 — 상세 쪽을 보고 있으면 목록으로
            {
                if (!dex.Back()) CloseDex();
                return true;
            }
            if (Stage.Current is BugReportScreen bug)   // 타이틀 설정에서 연 버그 신고 창(결정 132 ①) = [취소](설정으로)
            {
                bug.Back();
                return true;
            }
            if (!OnBoard) return false;
            if (MenuLayer is BoardMenuScreen menu)   // 게임 메뉴(P23): 물음 · 안쪽 쪽(투표 · 방 설정 · 버그 신고)이면 메뉴로, 메뉴면 [게임으로 돌아가기]
            {
                menu.Escape();
                return true;
            }
            if (MenuLayer is SettingsScreen set && set.InGame)   // 메뉴에서 연 설정 = [뒤로](메뉴로)
            {
                CloseMenuSettings();
                return true;
            }
            var top = Front;   // 정보 창 층(결정 125)이 먼저 — 아래 창은 그대로
            if (top == null && WatchShown) return CloseWatch();   // 남의 행동 보기 창(결정 120 ①)
            if (top == null) return CancelPick();
            switch (top)
            {
                case StampScreen _: CloseStamp(); break;
                case TradeScreen _: CloseTrade(); break;
                case TradePickScreen _: CloseTradePick(); break;
                case ChallengeScreen _: CloseChallenge(); break;
                case BlessScreen bless when bless.IsPanel: CloseBless(); break;   // 축복 판만(물음·부활 고르기는 답해야 한다)
                case FateScreen _: CloseFate(); break;
                case CraftScreen _: CloseCraft(); break;
                case EvolveScreen _: CloseEvolve(); break;
                case ShrineScreen _: CloseShrine(); break;
                case MonScreen _: CloseMon(); break;
                case PartyScreen _: CloseParty(); break;
                case TrainerScreen _: CloseTrainer(); break;
                case VillageScreen village when village.EndMap(): return true;   // 보드에서 고르는 중(T18 ①)이면 창 꼴로 돌아온다(창은 그대로)
                case VillageScreen _: CloseVillage(); break;
                case LogScreen _: CloseLog(); break;
                case ActScreen _: CloseAct(); break;
                case TypeChartScreen _: CloseTypes(); break;
                case BattleScreen battle when !Tutorial.Running && battle.CloseRow(): return true;   // 독의 가방 줄 등 = [뒤로](P23 — 배틀 화면은 그대로)
                case BattleScreen _ when WatchingWild: CloseWildWatch(); break;   // 열어 본 남의 야생 배틀(결정 114) = [보드로]
                default: return false;
            }
            return !ReferenceEquals(Front, top);
        }

        /// <summary>
        /// 스페이스 바(결정 84 L11): 맨 위 창이 진화 창이고 내 진화 연출(39행)이 재생 중이면 착지로 건너뛴다(<see cref="EvolveScreen.Skip"/>).
        /// 결정 87 V12 다: 내 긴 연출(해금 · 카드 바꾸기 · VS · 거래 · 전설 이벤트 · 신 강림 · 전설 장소 — <see cref="SkipBeats"/>)도 그 단계의 착지로 소리 없이 건너뛴다.
        /// 지금 항목 안에서만 가므로 뒤 단계·뒤 항목은 그대로 돌고, 남 좌석의 항목은 받지 않는다(04 §8-1 P3). 건너뛰었으면 true.
        /// </summary>
        public bool SkipKey() => OnBoard && !Tutorial.DialogUp && ((Top is EvolveScreen evolve && evolve.Skip()) || SkipLong());

        /// <summary>지금 스페이스 바가 건너뛸 내 긴 연출이 재생 중인가(안내 줄 「스페이스 바: 건너뛰기」 <c>fx-skip-hint</c>가 떠 있다).</summary>
        public bool CanSkip => SkipTarget() >= 0;

        int SkipTarget()
        {
            var seq = Fx != null ? Fx.Sequencer : null;
            if (seq == null || seq.Current == null || Client == null) return -1;
            if (seq.Current.Seat == Client.Seat) return SkipBeats.TargetOf(seq.Current, seq.LocalMs);
            // 거래(52행)는 받아들인 좌석의 답에 실려 온다 — 제안한 내 거래도 내 것이다(주고받는 두 좌석)
            var trade = seq.Current.Step(DealRows.Trade);
            bool mine = trade != null && seq.LocalMs >= trade.StartMs && seq.LocalMs < trade.EndMs && TradeBeats.Event(trade) is TradeDone done && (done.A == Client.Seat || done.B == Client.Seat);
            return mine ? SkipBeats.TargetOf(seq.Current, seq.LocalMs) : -1;
        }

        bool SkipLong()
        {
            int to = SkipTarget();
            if (to < 0 || !Fx.Sequencer.SkipTo(to)) return false;
            ShowFx(Fx.Sequencer);
            return true;
        }

        /// <summary>마우스로 보드를 끌고 있는가(문턱을 넘었다).</summary>
        public bool Dragging => drag.Panning;

        int Point(float x, float y, bool left, bool other, bool held)
        {
            var hud = Hud;
            // 핑(결정 102 ①): Alt를 누른 채 누르면 그 누름은 핑의 것이다(보드 끌기 · 칸 고르기 · 좌석 칩 누르기로 새지 않는다)
            if (Room.InGame && roomLayer != null && laidFor.y > 0 && roomLayer.PingInput(x, y, left, held, PingKey))
            {
                if (held && drag.Held) drag.Kill();
                if (rig != null) rig.WheelEnabled = false;
                return -1;
            }
            // 좌석 칩 위면 공개 정보 카드(배틀 화면이 떠 있어도 위 띠의 칩은 그대로다 — 04 §4-6)
            var picked = OnBoard && hud != null ? Picked(x, y) : null;
            if (OnBoard && hud != null) hud.HoverUi(picked);
            bool ready = OnBoard && hud != null && board != null && rig != null && laidFor.x > 0;
            var flyPick = FlyPick;   // T18 ①: 공중날기 목적지를 보드에서 고르는 마을 창도 보드를 덮지 않는다(칸 누르기는 그 창으로)
            bool overUi = (Stage.Current != null && !Folded && flyPick == null) || Stage.Info != null || picked != null;   // 접힌 창(T21)은 보드를 덮지 않는다
            var e = DragEvent.None;
            bool sideClick = held && drag.Held && !drag.Panning && !drag.Dead && !drag.ByLeft;   // 가운데·오른쪽 단추를 끌지 않고 누르고 있다
            if (held)
            {
                if (drag.Held && (!ready || (Stage.Current != null && !Folded && flyPick == null) || Stage.Info != null)) drag.Kill();   // 끄는 중에 화면이 덮었다
                e = drag.Step(x, y, left, other, overUi || !ready, BoardDrag.Threshold(laidFor.y));
            }
            bool pressed = held ? e == DragEvent.Began && drag.ByLeft : left;
            if (pressed && OnBoard && hud != null && Stage.Current == null && (picked == null || !Gated(x, y))) hud.ClickUi(picked);
            if (!ready)
            {
                if (rig != null) rig.WheelEnabled = true;
                return -1;
            }
            if (board.Tokens != null) board.Tokens.Face(rig.Yaw, rig.CurrentZoom == BoardCameraRig.Zoom.Full);   // Q·E·휠로 바꾼 방향·줌에 조무래기 토큰을 맞춘다
            rig.WheelEnabled = !overUi;
            if (e == DragEvent.Began && !drag.Dead) dragGrabbed = BoardPick.Ground(board, rig.GetComponent<Camera>(), x, y, laidFor.x, laidFor.y, rig.Target.y, out dragGrab);
            if (e == DragEvent.Pan && dragGrabbed) Pan(x, y);
            // 끄는 동안에는 칸을 고르지 않는다(경로 표시가 끌려다니지 않게)
            int cell = overUi || drag.Panning ? -1 : BoardPick.Cell(board, rig.GetComponent<Camera>(), x, y, laidFor.x, laidFor.y);
            if (held) pressed = e == DragEvent.Click;
            // 묶음 표지 위면 그 칸의 공개 정보 카드(표지는 마우스를 받지 않는다 — 칸 고르기·누르기는 표지 밑의 칸 그대로). 끄는 동안에는 표지 카드도 띄우지 않는다
            hud.ShowStacks();
            float panelScale = Stage.Grade.PanelScale;
            int stack = overUi || drag.Panning || hud.Stacks == null ? -1 : hud.Stacks.CellAt(new Vector2(x / panelScale, (laidFor.y - y) / panelScale));
            hud.Hover(cell, stack);
            // 걷기 전 확인(결정 70 ⑱): 갈 수 있는 칸을 누르면 고르고(고르기 소리), 고른 칸을 다시 누르면 걷는다(확정 소리). 고른 뒤 다른 갈 수 있는 칸을 누르면 그 칸을 거쳐 가는 길(경유 칸 — 결정 71 ②, 고르기 소리).
            // 갈 수 없는 칸·보드 밖을 누르면 고른 칸이 풀리고, 가운데·오른쪽 단추를 끌지 않고 떼면 한 단계 물린다(마지막 경유 칸 → 고른 칸, 소리 없음). HUD·창 위의 누름은 보드로 새지 않는다
            if (flyPick != null)
            {
                flyPick.HoverCell(cell);
                if (pressed && !overUi && !Gated(x, y) && flyPick.ClickCell(cell)) PlaySound(SoundKeys.Select);
            }
            else if (pressed && !overUi && !Gated(x, y))
            {
                int was = hud.PickedCell, viasWas = hud.Vias.Count;
                if (hud.ClickCell(cell)) PlaySound(SoundKeys.Decide);
                else if (hud.PickedCell >= 0 && (hud.PickedCell != was || hud.Vias.Count > viasWas)) PlaySound(SoundKeys.Select);
            }
            else if (sideClick && e == DragEvent.Ended) hud.Back();
            return cell;
        }

        // 잡은 땅의 점이 커서 아래에 오도록 카메라의 보는 점을 옮긴다(높이·방향·줌은 그대로). 보는 점은 보이는 칸들의 가운데가 이루는 네모 안에 머문다 —
        // 걷는 말 따라가기가 닿는 범위와 같다(가장자리 칸도 화면 가운데에 올 수 있다)
        void Pan(float x, float y)
        {
            if (!BoardPick.GroundAt(rig.GetComponent<Camera>(), x, y, laidFor.x, laidFor.y, dragGrab.y, out var hit)) return;
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            bool distortion = board.DistortionWorldVisible;
            for (int c = 0; c < board.Board.Count; c++)
            {
                if (board.Board[c].DistortionWorld && !distortion) continue;
                double cx = (double)board.Looks[c].X, cz = (double)board.Looks[c].Z;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cz < minZ) minZ = cz;
                if (cz > maxZ) maxZ = cz;
            }
            if (minX > maxX) return;
            double tx = rig.Target.x, tz = rig.Target.z;
            BoardDrag.Pan(ref tx, ref tz, dragGrab.x, dragGrab.z, hit.x, hit.z, minX, maxX, minZ, maxZ);
            rig.LookAt(new Vector3((float)tx, rig.Target.y, (float)tz));
            // 지금 재생 중인 걸음·항목의 따라가기를 쉰다 — 같은 프레임에 떼도 되돌아가지 않게 여기서 적어 둔다
            heldStep = camStep;
            heldEntry = Fx != null ? Fx.Sequencer.Current : null;
        }

        /// <summary>창 크기(물리 px — 마지막 <see cref="Layout"/>).</summary>
        public Vector2Int LaidFor => laidFor;

        /// <summary>그 점(물리 px, 왼쪽 아래가 (0, 0))이 PC 층의 요소(HUD 판·단추·창) 위인가. 마우스를 받지 않는 요소(HUD 바탕·띠)는 치지 않는다.</summary>
        public bool OverUi(float x, float y) => Picked(x, y) != null;

        // 그 점 아래의 PC 층 요소(뿌리·패널 바탕은 치지 않는다). 없으면 null
        VisualElement Picked(float x, float y)
        {
            var root = Stage.Root;
            if (root == null || root.panel == null || laidFor.y <= 0) return null;
            float scale = Stage.Grade.PanelScale;
            var picked = root.panel.Pick(new Vector2(x / scale, (laidFor.y - y) / scale));
            return picked != null && picked != root && picked != root.parent ? picked : null;
        }

        // ── 보드 ──

        /// <summary>
        /// 창 크기(물리 px)에 맞춰 전경 카메라를 놓는다. 화면이 보드를 덮고 있으면 크기나 화면이 바뀌었을 때 다시 놓고,
        /// 보드에서는 셋업이 막 끝난 그때 한 번만 내 말 칸에 놓는다 — 그 뒤 창 크기·갱신으로는 건드리지 않는다(Q·E·휠로 바꾼 시점을 지킨다. 보는 점은 걷는 말을 따라간다 — <see cref="Follow"/>).
        /// </summary>
        public void Layout(int width, int height)
        {
            var size = new Vector2Int(width, height);
            Ripple();
            if (rig != null && board != null && board.Tokens != null) board.Tokens.Face(rig.Yaw, rig.CurrentZoom == BoardCameraRig.Zoom.Full);   // 캡처·시험은 Pointer 없이 이 길로 맞춘다
            if (laid && size == laidFor && ReferenceEquals(laidScreen, Stage.Current))
            {
                Rewide();
                Hud?.ShowStacks();   // 묶음 표지는 프레임마다 카메라(Q·E·휠·따라가기)와 시각에 맞춘다
                return;
            }
            laid = true;
            laidFor = size;
            laidScreen = Stage.Current;
            if (rig == null || width <= 0 || height <= 0) return;
            LayCamera(width, height);
            Rewide();
            Hud?.ShowStacks();
        }

        void LayCamera(int width, int height)
        {
            if (Stage.Current is IOverBoard over && !OnBoard) rig.Set(BoardCameraRig.Zoom.Full, 0, BoardBackdrop.Target(width, height, over.PanelRight));
            else if (OnBoard && !boardCamSet && board != null && Client.View.Seats[Client.Seat].Cell >= 0)
            {
                rig.Set(BoardCameraRig.Zoom.Region, 0, board.CellTop(Client.View.Seats[Client.Seat].Cell));   // 내 말이 선 칸을 본다(그 뒤로는 걷는 말을 따라가고 마우스로 끌어 옮긴다 — Follow · Pan)
                boardCamSet = true;
                RaiseSpot();   // 떠 있는 숨겨진 장소 토큰의 높이는 카메라로 센다 — 이어 받은 판은 카메라를 놓기 전에 토큰을 세웠다
            }
        }

        /// <summary>화면·게임 상태가 바뀌었다: 보드 입력을 켜고 끄고, 말을 세우거나 옮기고, 다음 <see cref="Layout"/>에서 카메라를 다시 놓게 한다.</summary>
        void Restage()
        {
            laid = false;
            if (rig != null) rig.InputEnabled = (Stage.Current == null || Folded || FlyPick != null) && Stage.Info == null && OnBoard;
            if (board == null) return;
            board.PieceStyle = PieceStyle;   // 보드가 처음 세운 말(직업 그림)을 내 설정의 양식으로
            // 깨어진 세계 25칸(왜곡 게이트 건너편 · 네크로즈마 받침)은 울트라 버스트 판에서만 보인다(04 §3-1 — 타이틀 뒤 시험 판 · 신오의 심장은 숨김)
            bool distortion = Client != null && Client.View != null && Client.View.Campaign == Campaign.UltraBurst;
            if (board.DistortionWorldVisible != distortion) board.DistortionWorldVisible = distortion;
            StackView();
            if (Client == null || Client.View == null)
            {
                if (gameSeats) board.ShowDemoSeats();   // 처음에는 보드가 이미 시험 판이다
                gameSeats = false;
            }
            else
            {
                // 말: 좌석의 겉모양(직업·고향 마을)이 바뀌었을 때만 다시 세운다. 같으면 옮기기만 하고, 연출이 도는 동안에는 건드리지 않는다 —
                // 걷는 말은 화면 대기열이 옮기고(Walk), 대기열이 비면 보기의 칸에 맞춘다
                var looks = SeatLooks(Ctx, Client.View, Protagonist, out var cells);
                HideDropping(cells);
                if (!gameSeats || !board.SameSeats(looks)) board.SetSeats(looks, cells);
                else if (Fx == null || Fx.Sequencer.IsEmpty) board.Pieces.Arrange(cells);
                gameSeats = true;
            }
            SyncTokens();
            if (laidFor.x > 0) Layout(laidFor.x, laidFor.y);
        }

        /// <summary>
        /// 보기의 좌석들 → 보드 말(직업 그림 · 주인공 · 고향 마을 색 · 선 칸). 어느 양식으로 그릴지는 보드가 정한다(<see cref="BoardPieces.StyleOf"/>).
        /// 말은 고향 마을을 고른 좌석부터 선다(04 §8-8 2행 — 고른 마을에 떨어져 내려앉는다): 트레이너 카드를 아직 고르지 않았으면 직업이 없어 주인공 도트로 서고,
        /// 직업 그림 양식이면 카드를 고른 뒤 직업 그림으로 바뀐다. 마을을 고르기 전에는 주인공 없이 칸 −1이다.
        /// </summary>
        /// <param name="protagonist">좌석 → 주인공 키(<see cref="Protagonist"/>). null이면 주인공 없이(직업 그림만).</param>
        public static List<BoardPieces.SeatLook> SeatLooks(Ctx ctx, GameState view, Func<int, string> protagonist, out List<int> cells)
        {
            var looks = new List<BoardPieces.SeatLook>();
            cells = new List<int>();
            for (int s = 0; s < view.Seats.Length; s++)
            {
                var seat = view.Seats[s];
                string trainer = seat.TrainerCard >= 0 ? ctx.Cards.TrainerCards[seat.TrainerCard].Key : null;
                string village = seat.HomeVillage >= 0 ? ctx.Fixtures.Fixtures[seat.HomeVillage].Key : null;
                bool standing = seat.HomeVillage >= 0 && seat.Cell >= 0;
                looks.Add(new BoardPieces.SeatLook(trainer, village, standing && protagonist != null ? protagonist(s) : null));
                cells.Add(standing ? seat.Cell : -1);
            }
            return looks;
        }
    }
}
