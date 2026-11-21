using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>소리 표의 「층」 칸(04 §8-4): 효과음 · 울음 · BGM 징글(징글 동안 BGM 더킹).</summary>
    public enum SoundLayer { Se, Cry, Jingle }

    /// <summary>
    /// 소리 표의 한 줄(04 §8-4 「§8-8 행마다 나 / 남·관전 / 층 세 칸」의 뼈대): 행(04 §8-8 행 × 10 + 단계)의 박자 하나(<see cref="Beat"/>) → 낼 소리.
    /// <see cref="Me"/> = 내가 당사자인 항목(내 좌석의 행동 · 내 배틀 · 판의 자동 처리), <see cref="Others"/> = 남의 항목을 보거나 관전할 때.
    /// 칸 값은 <see cref="SoundKeys"/>의 키 · <see cref="SoundCues.Silent"/>(무음) · 남 칸만 <see cref="SoundCues.SameAsMe"/>(나와 같음) — 빈 칸은 없다(시험).
    /// </summary>
    public sealed class SoundCueRow
    {
        public readonly int Row;
        public readonly string Beat, Me, Others, Note;
        public readonly SoundLayer Layer;

        public SoundCueRow(int row, string beat, string me, string others, SoundLayer layer, string note)
        {
            Row = row;
            Beat = beat;
            Me = me;
            Others = others;
            Layer = layer;
            Note = note;
        }
    }

    /// <summary>
    /// 연출 단서에 소리를 단다(04 §8-4, 결정 66 ④ — plan §4 「연출 단서에 소리 달기」). 소리는 이벤트를 받을 때가 아니라 화면이 그리는 단계의 키 프레임에 단다:
    /// 단계 안 시각은 그 행을 그리는 박자 함수(<see cref="FxWalk"/>·<see cref="CaptureStage"/>·<see cref="EnterRows"/>·<see cref="DiceRollScene.Timeline"/> …)와 같은 식으로 센다.
    /// 화면이 대기열에 넣기 직전(그리지 않는 단계를 덜어 낸 뒤 — <see cref="FxBuilt"/>, 굴림 녹화본을 고른 뒤)에 부른다. 재생 시각은 시퀀서가 (지난 시각, 지금]으로 훑는다.
    /// <para>소리 표(<see cref="Table"/>)가 행 → 소리의 한 곳이다. 표에 없는 박자를 달려 하면 던진다 — 화면이 그리는 행(<see cref="FxBuilt.Drawn"/>)마다 표에 줄이 있는지는 교차 시험이 본다.</para>
    /// [가정 — plan §5 #54]: 같은 항목에서 같은 시각의 같은 소리는 한 번(동시 피해 · 한 박자의 면 바꾸기 여럿). 걸음마다는 무음(끝 착지만). M9 소리 ③: 울음(층 Cry — <c>dexOf</c>) · 새 효과음 키 · 행마다의 표 <see cref="SoundRows"/>, 기술·상태 SE는 무음 [가정 §5 #356], 징글 곡(곡 시퀀스 — 회복 SEQ_ASA · 시선 곡 도입부 · 진화 곡 · 팡파르 · 교환 곡)은 소리 ②에서 달았다.
    /// </summary>
    public static class SoundCues
    {
        /// <summary>무음 칸.</summary>
        public const string Silent = "-";
        /// <summary>남·관전 칸: 나와 같다.</summary>
        public const string SameAsMe = "=";
        /// <summary>받는 트레이너 카드 수(04 §8-8 1행 「3장 받기」 — 받기 단계에는 이벤트가 없다).</summary>
        public const int DealtCards = 3;

        /// <summary>숨겨진 장소 토큰 놓기 소리 사이의 가장 짧은 틈(ms) [가정 — plan §5 #54]: 토큰은 15ms마다 떨어지기도 해 그때마다 내면 소리가 이어 붙는다.</summary>
        public const int TokenGapMs = 120;

        const SoundLayer Se = SoundLayer.Se, Cry = SoundLayer.Cry;

        /// <summary>볼이 열린 뒤 울음까지의 틈(ms) [가정 plan §5 #352]: 04 §8-4 「울음과 SE가 같은 순간이면 울음을 먼저, SE는 맞는 순간으로 민다」 — 볼 열림 SE는 그 순간 그대로, 울음은 포켓몬이 드러난 뒤.</summary>
        public const int CryLagMs = 250;

        /// <summary>울음의 포켓몬 Id 자리에 넣는 약속 값: 「지금 판에 선 네크로즈마」(84행 원격 파괴 — 결정 143 ④). <c>dexOf</c>가 페이즈 · 썬/문으로 그 폼의 울음 번호를 낸다(GameFlow.CryDex).</summary>
        public const int ShownNecrozma = -2;

        /// <summary>포획 성공의 딸깍 뒤 팡파르까지(M9 소리 ② — 딸깍이 묻히지 않게 [가정 plan §5 #522]).</summary>
        public const int FanfareLagMs = 350;

        /// <summary>소리 표(뼈대 — 솔로는 「나」 칸만 쓴다. 남·관전 칸은 [가정]으로 채웠다, plan §5 #54).</summary>
        public static readonly SoundCueRow[] Table =
        {
            // 셋업(1~3행)
            new SoundCueRow(SetupRows.Deal, "flip", SoundKeys.CardFlip, SameAsMe, Se, "받은 카드가 앞면으로 뒤집히는 가운데 — 카드마다(어긋남 60)"),
            new SoundCueRow(SetupRows.Chosen, "pick", Silent, Silent, Se, "고른 소리는 단추의 즉시 소리(DECIDE) — 남 화면은 「선택 완료」만"),
            new SoundCueRow(SetupRows.Reveal, "flip", SoundKeys.CardFlip, SameAsMe, Se, "카드가 앞면으로 뒤집히는 가운데"),
            new SoundCueRow(SetupRows.Village, "land", SoundKeys.Land, SameAsMe, Se, "말이 마을에 내려앉는 프레임"),
            new SoundCueRow(SetupRows.Starter, "open", SoundKeys.BallOpen, SameAsMe, Se, "볼이 열리는 프레임"),
            new SoundCueRow(SetupRows.Starter, "cry", SoundKeys.Cry, SameAsMe, Cry, "흰 실루엣이 색으로 풀려 도트가 다 드러난 때(MonToPermille) — 그 스타터의 울음(M9 소리 ③)"),
            // 막 카드(4행) · 토큰 놓기(5행)
            new SoundCueRow(ActBeats.Row, "drop", SoundKeys.ActCard, SameAsMe, Se, "카드가 떨어지기 시작(THE_EVENT 징글은 곡 시퀀스 — M9)"),
            new SoundCueRow(TokenBeats.Row, "land", SoundKeys.Land, SameAsMe, Se, "조무래기 토큰이 마을마다 내려앉는 프레임"),
            new SoundCueRow(TokenBeats.Row, "drop", SoundKeys.Token, SameAsMe, Se, "숨겨진 장소 토큰이 하나씩 내려앉는 프레임"),
            // 보드
            new SoundCueRow(110, "land", SoundKeys.Land, SameAsMe, Se, "마지막 걸음이 닿는 프레임(걸음마다 무음 · 단차 DANSA는 M9)"),
            new SoundCueRow(110, "meet", SoundKeys.Meet, SameAsMe, SoundLayer.Jingle, "같은 칸의 말을 만남(13행 — 걷기 단계 끝에 붙는 대기열 밖 행): 걸어 들어간 칸에 다른 좌석의 말이 있으면 마지막 걸음이 닿을 때(「!」가 뜨는 때). 결정 73 ⑤ — 원작 시선 곡 SEQ_EYE_BOY의 도입부 + BGM 더킹(M9 소리 ②)"),
            new SoundCueRow(FlyBeats.Row, "land", SoundKeys.Land, SameAsMe, Se, "도착 마을에 내려앉는 프레임"),
            new SoundCueRow(SpotBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "[공개] 토큰이 앞면으로 바뀌는 가운데"),
            new SoundCueRow(SpotBeats.Row, "pass", Silent, Silent, Se, "[지나가기]는 내려앉기만"),
            new SoundCueRow(HealBeats.Row, "full", SoundKeys.Center, SameAsMe, SoundLayer.Jingle, "회복(8·17행) — 체력이 다 차는 단계 끝에 한 번만(결정 73 ⑥ D3 나 — 칸마다는 무음). 포켓몬센터 징글 SEQ_ASA + BGM 더킹(M9 소리 ②)"),
            new SoundCueRow(WhiteoutBeats.Row, "fall", SoundKeys.Fall, SameAsMe, Se, "귀환 암전이 내려오기 시작하는 때 — 짧은 하강음(M9 소리 ③ [가정 §5 #355])"),
            // 남은 M4 행(결정 70 ② A2 — 6·9·19·99행)
            new SoundCueRow(TurnStartBeats.Row, "in", Silent, SoundKeys.Select, Se, "턴 시작 배너가 들어옴(남의 턴 — 작은 톡). 내 턴의 소리는 101행 컷신이 낸다"),
            new SoundCueRow(ShieldBeats.Row, "crack", SoundKeys.Select, Silent, Se, "보호 배지에 금이 가는 때(작은 톡 — 남 화면은 무음)"),
            new SoundCueRow(ApricornBeats.Row, "drop", SoundKeys.Token, SameAsMe, Se, "규토리가 하나씩 바닥에 닿는 프레임(KINOMI 징글은 곡 시퀀스 — M9)"),
            new SoundCueRow(RevealAllBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "좌석 칩이 한꺼번에 뒤집히는 가운데 — 한 번"),
            // M5 「제작·은행·진화 화면」(18·36·37·39행) — 임시 효과음(정식 SE·진화 곡 SHINKA·팡파르는 M9) [가정]
            new SoundCueRow(BankBeats.Row, "get", SoundKeys.Token, SameAsMe, Se, "받은 규토리가 튀어나오기 시작(한 번 — DP_CARD 계열은 M9)"),
            new SoundCueRow(BankBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "섞은 덱의 새 맨 위 카드가 뒤집히는 가운데"),
            new SoundCueRow(CraftBeats.Row, "pop", SoundKeys.BallOpen, SameAsMe, Se, "완성품 둘레가 펑(PL_BOWABOWA는 M9)"),
            new SoundCueRow(StewBeats.Row, "bounce", SoundKeys.Select, SameAsMe, Se, "스튜 그릇이 처음 튀는 때(작은 톡 — 회복스튜의 징글은 17행)"),
            new SoundCueRow(WorkRows.Maint, "show", SoundKeys.Select, SameAsMe, Se, "정비(38·40·41행 — 지닌물건 · 기술 · 놓아주기 · 순서): 내 화면은 끌어 놓기 · 고르기의 select(내 정비는 단계가 없다 — 누르는 소리), 남 화면은 그 좌석의 정비 판 보기가 뜨는 때 작은 톡(결정 120)"),
            new SoundCueRow(EvolveBeats.Row, "song", SoundKeys.Evolve, Silent, SoundLayer.Jingle, "단계 처음 — 진화 곡 SEQ_SHINKA의 앞머리(빛이 터질 때까지 3.4초 · BGM 더킹). 남 화면은 2초짜리 알림이라 무음(M9 소리 ② [가정 plan §5 #522])"),
            new SoundCueRow(EvolveBeats.Row, "flash", SoundKeys.BallOpen, SameAsMe, Se, "화면이 하얗게 터지는 때"),
            new SoundCueRow(EvolveBeats.Row, "fanfare", SoundKeys.Fanfare, SameAsMe, SoundLayer.Jingle, "흰 빛이 걷힌 때(ClearPermille) — 팡파르 SEQ_FANFA5(원작 진화 끝, M9 소리 ②)"),
            new SoundCueRow(EvolveBeats.Row, "cry", SoundKeys.Cry, SameAsMe, Cry, "새 모습이 머무는 때(HoldPermille) — 진화한 종의 울음(M9 소리 ③)"),
            // M5 「맵 결과·배틀 안 연출」(26~30 · 46 · 74·75 · 78~80행) — 임시 효과음(정식 SE·PL_GIRA·FANFA는 M9) [가정]
            new SoundCueRow(ResultRows.Stash, "pop", SoundKeys.BallOpen, SameAsMe, Se, "규토리 더미가 터지는 때(KINOMI 징글은 M9)"),
            new SoundCueRow(ResultRows.Stash, "gain", SoundKeys.Token, SameAsMe, Se, "규토리가 말에 다 모인 때"),
            new SoundCueRow(ResultRows.Camp, "land", SoundKeys.Land, SameAsMe, Se, "텐트가 펴지며 내려앉는 때"),
            new SoundCueRow(ResultRows.Star, "land", SoundKeys.Shine, SameAsMe, Se, "별이 위 띠에 박히는 때(반짝 DP_KIRAKIRA — M9 소리 ③)"),
            new SoundCueRow(ResultRows.Alpha, "flash", SoundKeys.Hit, SameAsMe, Se, "붉은 섬광과 땅울림(피한 우두머리는 무음)"),
            new SoundCueRow(ResultRows.Relic, "land", SoundKeys.Token, SameAsMe, Se, "유물이 내 판에 닿는 때"),
            new SoundCueRow(ResultRows.GodGone, "banner", SoundKeys.MyTurn, SameAsMe, Se, "「한 번 더!」가 튀어나오는 때(짧은 팡파르는 M9)"),
            new SoundCueRow(ResultRows.Spoils, "drop", SoundKeys.Token, SameAsMe, Se, "보상 줄이 결과 창에 내려앉는 때"),
            new SoundCueRow(ResultRows.Stored, "in", SoundKeys.Select, SameAsMe, Se, "구슬이 응징 배지에 빨려 든 때(작은 톡)"),
            new SoundCueRow(ResultRows.Moved, "hit", SoundKeys.Hit, SameAsMe, Se, "응징 구슬이 터지는 때(옮긴 것이 있을 때만)"),
            new SoundCueRow(ResultRows.Lock, "lock", SoundKeys.Select, SameAsMe, Se, "자물쇠 칩이 붙는 때(작은 딸깍)"),
            new SoundCueRow(ResultRows.GodPhase, "rise", SoundKeys.BallOpen, SameAsMe, Se, "빛기둥 속에서 둘째 폼이 서는 때(BGM 고조는 M9)"),
            new SoundCueRow(ResultRows.Form, "flash", SoundKeys.BallOpen, SameAsMe, Se, "빛이 걷히며 새 폼이 서는 때"),
            new SoundCueRow(ResultRows.Form, "cry", SoundKeys.Cry, SameAsMe, Cry, "새 폼이 선 뒤(빛 + CryLagMs) — 그 종의 울음(M9 소리 ③)"),
            // 턴 넘김(101행)
            new SoundCueRow(FxRows.TurnSummary, "in", Silent, Silent, Se, "요약은 무음"),
            new SoundCueRow(FxRows.Handoff, "in", SoundKeys.Band, SameAsMe, Se, "넘김 띠가 들어옴(남 차례 작은 톡)"),
            new SoundCueRow(FxRows.MyTurn, "in", SoundKeys.MyTurn, Silent, Se, "내 턴 컷신 — 다른 SE보다 크게. 남 화면에는 이 단계가 없다(차례 좌석만)"),
            // 들어가고 나오는 연출
            new SoundCueRow(EnterRows.Search, "rustle", SoundKeys.Search, SameAsMe, Se, "「!」가 튀어나옴 — 나무 · 왜곡된 나무를 찾아봄: 풀숲 소리(결정 143 ⑥ — 옛 DP_FW230 대신)"),
            new SoundCueRow(EnterRows.Search, "splash", SoundKeys.SearchReef, SameAsMe, Se, "「!」가 튀어나옴 — 바다 칸(산호)을 찾아봄: 물 소리(결정 143 ⑥)"),
            new SoundCueRow(EnterRows.Reveal, "flip", SoundKeys.CardFlip, SameAsMe, Se, "조우 카드마다 뒤집히는 가운데(250 어긋남 — 강한·전설 카드는 가벼운 카드 뒤에 느리게, M9 다듬기 2)"),
            new SoundCueRow(EnterRows.Reveal, "thump", SoundKeys.Land, SameAsMe, Se, "강한·전설 카드가 떨어져 닿는 때(쿵 — 임시 land, M9 다듬기 2 [가정 §5 #415])"),
            new SoundCueRow(EnterRows.Picked, "pick", Silent, Silent, Se, "고른 소리는 단추의 즉시 소리(DECIDE)"),
            new SoundCueRow(EnterRows.Wipe, "close", SoundKeys.Wipe, SameAsMe, Se, "줄무늬가 닫히기 시작(배틀 곡이 이어 바뀐다)"),
            new SoundCueRow(EnterRows.Galactic, "in", Silent, Silent, Se, "시선 곡 EYE_GINGA는 곡 시퀀스 — M9"),
            new SoundCueRow(EnterRows.Galactic, "ball", SoundKeys.Token, SameAsMe, Se, "라인업 볼이 하나씩 놓이는 프레임(72행 S54 — 결정 74, 임시 token 톡 [가정 §5 #96])"),
            // 배틀
            new SoundCueRow(FxRows.Intro, "throw", SoundKeys.BallThrow, SameAsMe, Se, "배틀 시작의 첫 출전 — 주인공의 볼이 손을 떠남(결정 99 ②)"),
            new SoundCueRow(FxRows.Intro, "open", SoundKeys.BallOpen, SameAsMe, Se, "트레이너 쪽 출전마다 볼이 열리는 때(야생은 풀숲에서 — 무음)"),
            new SoundCueRow(FxRows.Intro, "cry", SoundKeys.Cry, SameAsMe, Cry, "출전마다 그 포켓몬이 드러난 때(볼이면 열림 + CryLagMs · 야생은 드러나는 때) — 울음(M9 소리 ③, 울트라 비스트도)"),
            new SoundCueRow(FxRows.SendIn, "open", SoundKeys.BallOpen, SameAsMe, Se, "나오는 포켓몬의 볼이 열리는 때"),
            new SoundCueRow(FxRows.SendIn, "cry", SoundKeys.Cry, SameAsMe, Cry, "볼이 열린 뒤(+ CryLagMs) — 나온 포켓몬의 울음(M9 소리 ③)"),
            new SoundCueRow(540, "pass", SoundKeys.Token, SameAsMe, Se, "우선권 토큰을 넘겨 상대 쪽에 놓이는 프레임(결정 75 — 토큰을 쓴 선턴만, 그 밖의 선턴은 무음)"),
            new SoundCueRow(560, "throw", SoundKeys.DiceRoll, SameAsMe, Se, "주사위를 던지는 때"),
            new SoundCueRow(560, "count", SoundKeys.Dice, SameAsMe, Se, "세기 틱 — 주사위마다"),
            new SoundCueRow(570, "flip", SoundKeys.Select, SameAsMe, Se, "면이 넘어가기 시작(같은 박자 여럿은 한 번)"),
            new SoundCueRow(590, "-", Silent, Silent, Se, "기술 SE는 원작이 기술마다 다른 시퀀스라 한 소리로 덮지 않는다 — 타격음은 61행(M9 소리 ③ [가정 plan §5 #356], 타입별 기술 SE는 다듬기 목록)"),
            new SoundCueRow(600, "-", Silent, Silent, Se, "타격음은 61행 체력에서 한 번"),
            new SoundCueRow(611, "-", Silent, Silent, Se, "계산 줄은 무음"),
            new SoundCueRow(612, "hit", SoundKeys.Hit, SameAsMe, Se, "체력 막대가 깎이기 시작"),
            new SoundCueRow(620, "-", Silent, Silent, Se, "상태 SE도 원작이 상태마다 다른 시퀀스 — 상태 이상 피해·면 바꾸기는 63행(M9 소리 ③ [가정 plan §5 #356])"),
            new SoundCueRow(630, "hit", SoundKeys.Hit, SameAsMe, Se, "상태 이상 피해 — 체력이 깎이기 시작"),
            new SoundCueRow(630, "flip", SoundKeys.Select, SameAsMe, Se, "상태 이상 면 바꾸기(굴림 없이 온 것만 — 굴림이 있으면 56행이 단다)"),
            new SoundCueRow(641, "exp", SoundKeys.Exp, SameAsMe, Se, "피로 칸이 오르기 시작하는 때(FatigueChanged After > Before — 04 「EXP 소리」, M9 소리 ③)"),
            new SoundCueRow(642, "hit", SoundKeys.Hit, SameAsMe, Se, "피로 피해 — 체력이 깎이기 시작"),
            new SoundCueRow(650, "faint", SoundKeys.Faint, SameAsMe, Se, "기절 — 도트가 내려가기 시작"),
            new SoundCueRow(650, "cry", SoundKeys.CryLow, SameAsMe, Cry, "기절 — 도트가 내려가기 시작하는 때 낮춘 울음(높이 CryBook.LowPitch, M9 소리 ③ [가정 §5 #352])"),
            new SoundCueRow(670, "flee", SoundKeys.Flee, SameAsMe, Se, "도망 — 단계 처음(Fled만 · 기권은 무음, M9 소리 ③)"),
            new SoundCueRow(CaptureStage.Row, "roll", SoundKeys.DiceRoll, SameAsMe, Se, "포획 주사위가 던짐과 함께 구르기 시작"),
            new SoundCueRow(CaptureStage.Row, "throw", SoundKeys.BallThrow, SameAsMe, Se, "볼이 손을 떠남"),
            new SoundCueRow(CaptureStage.Row, "open", SoundKeys.BallOpen, SameAsMe, Se, "볼이 열리며 포켓몬이 빨려 듦"),
            new SoundCueRow(CaptureStage.Row, "count", SoundKeys.Dice, SameAsMe, Se, "포획 주사위 세기 틱 — 주사위마다"),
            new SoundCueRow(CaptureStage.Row, "shake", SoundKeys.BallShake, SameAsMe, Se, "흔들림마다"),
            new SoundCueRow(CaptureStage.Row, "click", SoundKeys.Capture, SameAsMe, Se, "성공 — 마지막 흔들림 뒤 딸깍"),
            new SoundCueRow(CaptureStage.Row, "fanfare", SoundKeys.Fanfare, SameAsMe, SoundLayer.Jingle, "성공 — 딸깍 + FanfareLagMs 뒤 포획 팡파르 SEQ_FANFA5 + BGM 더킹(M9 소리 ②)"),
            new SoundCueRow(CaptureStage.Row, "break", SoundKeys.BallOpen, SameAsMe, Se, "실패 — 볼이 열리며 튀어나옴"),
            new SoundCueRow(690, "-", Silent, Silent, Se, "잡은 뒤는 결말 대사가 머무는 박자"),
            new SoundCueRow(700, "-", Silent, Silent, Se, "결과는 결과 곡(상황 곡 Result)"),
            new SoundCueRow(AiLineBeats.Row, "in", SoundKeys.Select, SameAsMe, Se, "AI 자막 띠가 들어옴(작은 톡)"),
            new SoundCueRow(940, "-", Silent, Silent, Se, "막 목표·조각·파티 칸은 결과 창의 줄"),
            // 2막 이후·종막(94~97행 — M5 「막·종막 화면」, 임시 소리 [가정 §5 #96] — FANFA·THE_EVENT·DENDO 곡 시퀀스는 M9)
            new SoundCueRow(LiberateBeats.Row, "flee", SoundKeys.Flee, SameAsMe, Se, "1막 해방: 조무래기 토큰이 날아오르기 시작하는 때(M9 다듬기 P18)"),
            new SoundCueRow(LiberateBeats.Row, "chip", SoundKeys.Shine, SameAsMe, Se, "「이동력 +2」 칩이 튀어나오는 때"),
            new SoundCueRow(FinaleRows.Heart, "merge", SoundKeys.Heal, SameAsMe, SoundLayer.Jingle, "조각 셋이 심장으로 합쳐지는 때(FANFA·BADGE는 M9)"),
            new SoundCueRow(FinaleRows.Ending, "burn", SoundKeys.Wipe, SameAsMe, Se, "고르지 않은 카드가 타기 시작(THE_EVENT는 M9)"),
            new SoundCueRow(FinaleRows.Order, "chip", SoundKeys.CardFlip, SameAsMe, Se, "배신자 순서 칩이 하나씩 놓이는 때"),
            new SoundCueRow(FinaleRows.Result, "title", SoundKeys.ActCard, SameAsMe, Se, "결과 제목이 뜨는 때(EV_DENDO·BLD_ENDING은 M9)"),
            // 스탬프·도전·거래(47·51·52행 — M5 「스탬프·도전·거래 화면」, 임시 소리 [가정 §5 #125 ⑥] — BADGE·EYE_·DP_VSDEMO·KOUKAN은 M9)
            new SoundCueRow(StampBeats.Row, "slam", SoundKeys.Stamp, SameAsMe, SoundLayer.Se, "도장이 찍히는 때(쾅 — PL_STAMP, M9 소리 ③)"),
            new SoundCueRow(StampBeats.Row, "reward", SoundKeys.Token, SameAsMe, Se, "보상 줄이 하나씩 내려앉기 시작하는 때"),
            new SoundCueRow(VsBeats.Row, "bang", SoundKeys.Bang, SameAsMe, Se, "「!」가 뜨는 때(시선 곡은 M9)"),
            new SoundCueRow(VsBeats.Row, "vs", SoundKeys.Hit, SameAsMe, Se, "가운데 VS가 박히는 때"),
            new SoundCueRow(TradeBeats.Row, "song", SoundKeys.Trade, SameAsMe, SoundLayer.Jingle, "단계 처음 — 교환 곡 SEQ_KOUKAN의 앞머리 2.2초 + BGM 더킹(M9 소리 ② [가정 plan §5 #522])"),
            new SoundCueRow(TradeBeats.Row, "cross", SoundKeys.CardFlip, SameAsMe, Se, "올린 것이 엇갈려 건너가기 시작하는 때"),
            new SoundCueRow(TradeBeats.Row, "get", SoundKeys.Token, SameAsMe, Se, "받는 쪽 칸이 빛나는 때"),
            // 운명의 변화(7·14·42~45행 — M5 「운명·응답 화면」, 임시 소리 [가정 §5 #98] — DP_KIRAKIRA·DP_CARD·PL_WARP·PL_GIRA는 M9)
            new SoundCueRow(FateRows.Star, "flip", SoundKeys.CardFlip, Silent, Se, "떨어진 별 카드가 내 손에 닿아 앞면으로 뒤집히는 때(남 화면은 뒷면 — 무음)"),
            new SoundCueRow(FateRows.Draw, "flip", SoundKeys.CardFlip, Silent, Se, "받은 카드가 내 손에 닿아 앞면으로 뒤집히는 때(귀환 8행의 「운명 1장」 포함)"),
            new SoundCueRow(FateRows.Discard, "tear", SoundKeys.Cancel, SameAsMe, Se, "버린 카드가 찢기기 시작하는 때"),
            new SoundCueRow(FateRows.Play, "in", SoundKeys.Select, SameAsMe, Se, "카드가 가운데로 튀어나오는 때(3장이면 합쳐지는 끝에 card_flip을 한 번 더)"),
            new SoundCueRow(FateRows.Play, "merge", SoundKeys.CardFlip, SameAsMe, Se, "3장이 하나로 합쳐지는 때"),
            new SoundCueRow(FateRows.Teleport, "warp", SoundKeys.Warp, SameAsMe, Se, "말이 공간 틈으로 사라지는 때(PL_WARP — M9 소리 ③)"),
            new SoundCueRow(FateRows.Teleport, "land", SoundKeys.Land, SameAsMe, Se, "도착 칸에 내려앉는 프레임"),
            // 울트라 버스트 보드(15·82·83행 — M6 「UB 보드 화면」, 임시 효과음 [가정 §5 #156] — PL_WARP · DP_SUTYA · 깨짐 SE는 M9)
            new SoundCueRow(UbRows.Gate, "sink", SoundKeys.Warp, SameAsMe, Se, "말이 게이트 속으로 다 가라앉는 때(PL_WARP — M9 소리 ③)"),
            new SoundCueRow(UbRows.Gate, "land", SoundKeys.Land, SameAsMe, Se, "짝 게이트에서 다 솟아오른 때(착지 고리)"),
            new SoundCueRow(UbRows.Move, "cry", SoundKeys.Cry, SameAsMe, Cry, "UB가 다음 칸에 내려앉는 프레임(이동마다) — 그 UB의 울음(결정 143 ④ PokeAPI — 옛 착지 소리 land 대신)"),
            new SoundCueRow(UbRows.Destroy, "break", SoundKeys.Destroy, SameAsMe, Se, "나무·산호·사당이 어두워지기 시작하는 때(결정 147 ① DP_W088 — 옛 bang)"),
            // M6 「UB 화면 묶음 1」(82행 예비 · 84~86행, 임시 효과음 [가정 §5 #158] — 빛 SE · PL_GIRA · 패배 곡은 M9)
            new SoundCueRow(UbRows.Start, "band", SoundKeys.Band, SameAsMe, Se, "UB 페이즈 띠가 들어오는 때(균열 칩이 눌린다)"),
            // 결정 147 ③: 빛 모음(옛 glow = heal) · 빛줄기(옛 beam = wipe) 소리는 뺐다 — 네크로즈마 턴은 폼 울음 + 칸마다 파괴 bang만
            new SoundCueRow(UbRows.Strike, "cry", SoundKeys.Cry, SameAsMe, Cry, "네크로즈마가 빛을 모으기 시작하는 때 — 지금 판에 선 네크로즈마 폼의 울음(울트라 네크로즈마 등 폼 울음, 결정 143 ④)"),
            new SoundCueRow(UbRows.Bracket, "pulse", SoundKeys.Hit, SameAsMe, Se, "화면 가장자리가 붉게 맥동하는 때(10% 구간)"),
            new SoundCueRow(UbRows.Bracket, "card", SoundKeys.CardFlip, SameAsMe, Se, "첫 균열 카드가 좌석 칩에 닿는 때(한 번)"),
            new SoundCueRow(UbRows.Lost, "end", Silent, Silent, Se, "패배 덮개 — 원작 「The End」는 곡이다(결정 87 V17 나): 게임 끝 곡 상황 MusicScene.GameOver = SEQ_BLD_ENDING이 튼다(M9 소리 ②) — 단서는 무음"),
            // M6 「UB 배틀 화면 묶음」(88~92행 — 임시 효과음 [가정 plan §5 #159], 04 표의 막는 SE·빛 SE·코인 SE·깨짐 SE는 M9)
            new SoundCueRow(UbBattleRows.Prism, "block", SoundKeys.Select, SameAsMe, Se, "프리즘 방패가 서며 「−n」이 튀는 때"),
            new SoundCueRow(UbBattleRows.PrismMove, "land", SoundKeys.Decide, SameAsMe, Se, "프리즘 조각이 울트라 비스트 쪽 칩에 닿는 때"),
            new SoundCueRow(UbBattleRows.Onslaught, "flip", SoundKeys.CardFlip, SameAsMe, Se, "온슬롯 카드가 앞면으로 뒤집히는 가운데"),
            new SoundCueRow(UbBattleRows.Phase, "burst", SoundKeys.Heal, SameAsMe, Se, "네크로즈마에서 빛이 터지는 때"),
            new SoundCueRow(UbBattleRows.Coin, "land", SoundKeys.Coin, SameAsMe, Se, "썬/문 동전이 떨어져 닿는 때(PL_COIN — M9 소리 ③)"),
            new SoundCueRow(UbBattleRows.Trial, "crack", SoundKeys.Bang, SameAsMe, Se, "시련을 넘겨 면역 막이 깨지는 때(넘긴 단계만)"),
            new SoundCueRow(UbBattleRows.Guardian, "plus", SoundKeys.Heal, SameAsMe, Se, "「이동력 +2」가 튀는 때"),
            // M6 「연출 목록 채우기」(83행 토큰 날기 · 93행 실버디 축복 — 임시 효과음 [가정 plan §5 #163], PL_WARP · DP_KIRAKIRA는 M9)
            new SoundCueRow(UbRows.Fly, "warp", SoundKeys.Warp, SameAsMe, Se, "떠오른 토큰이 틈 속으로 사라지는 때(PL_WARP — M9 소리 ③)"),
            new SoundCueRow(UbBattleRows.Silvally, "arrive", SoundKeys.Shine, SameAsMe, Se, "축복 아이콘이 위 띠 축복 칩에 박히는 때(금빛 고리 — 반짝, M9 소리 ③)"),
            // 탈락·부활(91행 — M6 「UB 화면 묶음 2」, 임시 효과음 [가정 §5 #161] — 하강음 · DP_KIRAKIRA는 M9)
            new SoundCueRow(FallRows.Row, "crack", SoundKeys.Fall, SameAsMe, Se, "탈락: 좌석 칩에 금이 가는 때(하강음 — M9 소리 ③)"),
            new SoundCueRow(FallRows.Row, "ring", SoundKeys.Shine, SameAsMe, Se, "부활: 금빛 고리가 발밑에 닿는 때(반짝 — M9 소리 ③)"),
            // 트레이너 카드(35·48·49·50행 — M6 「트레이너 카드 해금 연출」, 임시 효과음 [가정 #162 ①] — 돌림판 틱 · DP_KIRAKIRA4 · 짧은 FANFA · DP_CARD는 M9)
            new SoundCueRow(WeatherBeats.Row, "stop", SoundKeys.Select, SameAsMe, Se, "날씨 배지 돌림판이 고른 날씨에 멈추는 때(톡)"),
            // 48 · 49행 남 좌석: 결정 120 ① 큰 덮개가 내 것과 같은 길이로 뜨므로 소리도 같은 박자(plan §5 #709) — 내 창 · 메뉴로 덮개가 숨으면 재생 층에 보내지 않는다(CardRows.OthersCover)
            new SoundCueRow(UnlockBeats.Row, "stamp", SoundKeys.Token, SameAsMe, Se, "원인 포켓몬의 등급 아이콘이 카드 아래 띠에 박히는 때"),
            new SoundCueRow(UnlockBeats.Row, "break", SoundKeys.Hit, SameAsMe, Se, "자물쇠가 깨지는 때(자물쇠마다 — 바로 골드면 두 번)"),
            new SoundCueRow(UnlockBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "카드가 옆으로 서서 다음 등급 면으로 바뀌는 때(자물쇠마다)"),
            new SoundCueRow(UnlockBeats.Row, "lit", SoundKeys.Shine, SameAsMe, Se, "착지 — 열린 능력 칸에 금 테두리가 켜지는 때"),
            new SoundCueRow(SwapBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "옛 카드가 옆으로 서서 새 카드 면으로 바뀌는 때"),
            new SoundCueRow(SwapBeats.Row, "lit", SoundKeys.BallOpen, SameAsMe, Se, "착지 — 새 카드의 골드 꼬리표가 켜지는 때"),
            new SoundCueRow(AbilityBeats.Row, "in", SoundKeys.Select, Silent, Se, "출처 카드가 작게 떠오르는 때(작은 톡 — 효과음은 결과 행이 낸다, 남 화면은 꼬리표뿐)"),
            new SoundCueRow(FateRows.Reroll, "lock", SoundKeys.Dice, SameAsMe, Se, "다시 굴린 주사위가 새 면으로 고정되는 때(주사위마다 — 글리치 지직 SE는 M9)"),
            new SoundCueRow(FateRows.Sacrifice, "hit", SoundKeys.Hit, SameAsMe, Se, "그림자가 다 감싸 체력이 빠지기 시작하는 때"),
            // M5 「사당·균열 화면」(31~34행) — 임시 효과음(DP_KIRAKIRA · EV_LEGEND · D_AGITO 곡과 울음은 M9) [가정]
            new SoundCueRow(OfferBeats.Row, "pillar", SoundKeys.Heal, SameAsMe, Se, "바친 규토리가 사라지고 빛기둥이 서는 때"),
            new SoundCueRow(OfferBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "뽑은 카드가 앞면으로 뒤집히는 가운데(장마다)"),
            new SoundCueRow(BlessBeats.Row, "get", SoundKeys.Shine, SameAsMe, Se, "받은 것 꼬리표가 뜨는 때(축복 — 반짝, M9 소리 ③)"),
            new SoundCueRow(LegendBeats.Row, "land", SoundKeys.Land, SameAsMe, Se, "전설 피규어가 사당에 내려앉는 프레임(놓지 않는 카드면 무음 — 울음은 M9)"),
            new SoundCueRow(LegendBeats.Row, "rain", SoundKeys.Token, SameAsMe, Se, "좌석 칩에 규토리가 닿는 때(좌석마다)"),
            new SoundCueRow(TallyBeats.Row, "flip", SoundKeys.CardFlip, SameAsMe, Se, "추종자 토큰이 한꺼번에 뒤집히는 가운데 — 한 번"),
            new SoundCueRow(DescendBeats.Row, "bolt", SoundKeys.Wipe, SameAsMe, Se, "첫 번개(공간 균열)"),
            new SoundCueRow(DescendBeats.Row, "land", SoundKeys.Land, SameAsMe, Se, "신 피규어가 시작의 사당에 내려앉는 프레임(이미 있던 신이면 번개만)"),
        };

        static readonly Dictionary<(int, string), SoundCueRow> byBeat = Index();

        static Dictionary<(int, string), SoundCueRow> Index()
        {
            var d = new Dictionary<(int, string), SoundCueRow>();
            foreach (var r in Table)
            {
                if (d.ContainsKey((r.Row, r.Beat))) throw new InvalidOperationException("소리 표: 같은 박자 두 번 " + r.Row + " " + r.Beat);
                d.Add((r.Row, r.Beat), r);
            }
            return d;
        }

        /// <summary>그 행·박자의 표 줄(없으면 null).</summary>
        public static SoundCueRow Find(int row, string beat) => byBeat.TryGetValue((row, beat ?? ""), out var r) ? r : null;

        /// <summary>그 행에 표 줄이 있나.</summary>
        public static bool Covers(int row)
        {
            foreach (var r in Table) if (r.Row == row) return true;
            return false;
        }

        /// <summary>
        /// 항목 하나에 소리 단서를 단다(단계마다 그 행의 박자 → 표의 소리). <paramref name="mine"/> = 내가 당사자인 항목인가(「나」 칸 · 아니면 「남·관전」 칸).
        /// 이미 단 소리 단서가 있는 항목에 다시 부르면 같은 시각·같은 키는 건너뛴다.
        /// <paramref name="occupied"/>(좌석, 칸) = 그 칸에 그 좌석 말고 다른 좌석의 말이 서 있나(화면의 보기 — 같은 칸의 말을 만남 13행, 결정 73 ⑤). null이면 만남 소리를 달지 않는다.
        /// </summary>
        /// <paramref name="dexOf"/>(포켓몬 Id, 포켓몬 표 색인 — 모르면 <see cref="GameEvent.U"/>) = 그 포켓몬의 울음 번호(종 번호 · 폼 울음이면 PokeAPI 폼 id — 결정 143 ④, 모르면 0 이하. 포켓몬 Id가 <see cref="ShownNecrozma"/>면 지금 판의 네크로즈마) — 울음(층 <see cref="SoundLayer.Cry"/>, M9 소리 ③). null이면 울음을 달지 않는다.
        public static void Add(FxEntry entry, bool mine, Func<int, int, bool> occupied = null, Func<int, int, int> dexOf = null)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            bool rolled = entry.Step(560) != null;
            foreach (var s in entry.Steps) Step(entry, s, mine, rolled, occupied, dexOf);
        }

        public static void Add(IEnumerable<FxEntry> entries, Func<FxEntry, bool> mine, Func<int, int, bool> occupied = null, Func<int, int, int> dexOf = null)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (mine == null) throw new ArgumentNullException(nameof(mine));
            foreach (var e in entries) Add(e, mine(e), occupied, dexOf);
        }

        static void Step(FxEntry entry, FxStep s, bool mine, bool rolled, Func<int, int, bool> occupied, Func<int, int, int> dexOf)
        {
            int at = s.StartMs, total = s.Ms;
            switch (s.Row)
            {
                case SetupRows.Deal:
                {
                    int slide = total * DealBeats.SlidePermille / 1000, half = total * DealBeats.FlipPermille / 1000 / 2, every = total * DealBeats.EveryPermille / 1000;
                    for (int i = 0; i < DealtCards; i++) Emit(entry, s.Row, "flip", at + every * i + slide + half, mine);
                    return;
                }
                case SetupRows.Reveal:
                {
                    int lift = total * RevealBeats.LiftPermille / 1000, flipTo = total * RevealBeats.FlipToPermille / 1000;
                    Emit(entry, s.Row, "flip", at + lift + (flipTo - lift) / 2, mine);
                    return;
                }
                case SetupRows.Village:
                    if (VillageBeats.Picked(s) != null) Emit(entry, s.Row, "land", at + total * VillageBeats.LandPermille / 1000, mine);
                    return;
                case SetupRows.Starter:
                    Emit(entry, s.Row, "open", at + total * StarterBeats.OpenPermille / 1000, mine);
                    foreach (var e in s.Events)
                        if (e is StarterPicked sp) EmitCry(entry, s.Row, at + total * StarterBeats.MonToPermille / 1000, mine, dexOf, sp.PokemonId, sp.Species);
                    return;
                case ActBeats.Row:
                    Emit(entry, s.Row, "drop", at + total / ActBeats.DimPart, mine);
                    return;
                case TokenBeats.Row:
                {
                    var grunts = TokenBeats.Grunts(s);
                    if (grunts != null && grunts.Cells != null)
                        for (int i = 0; i < grunts.Cells.Length; i++) Emit(entry, s.Row, "land", at + TokenBeats.LeadMs + TokenBeats.EveryMs * i + TokenBeats.FallMs, mine);
                    var spots = SpotDropBeats.Spots(s);
                    if (spots != null)
                    {
                        int n = spots.Cells.Length;
                        // 토큰이 많으면(50개쯤이 1초 안에 떨어진다) 소리는 TokenGapMs에 하나까지 — 따닥따닥 이어지지 않게
                        int last = int.MinValue / 2;
                        for (int i = 0; i < n; i++)
                        {
                            int t = at + SpotDropBeats.DropAt(total, i, n) + SpotDropBeats.FallMs;
                            if (t - last < TokenGapMs) continue;
                            Emit(entry, s.Row, "drop", t, mine);
                            last = t;
                        }
                    }
                    return;
                }
                case 110:
                    foreach (var e in s.Events)
                        if (e is Moved m)
                        {
                            int landed = FxWalk.LandedAt(m);
                            if (landed < 0) continue;
                            Emit(entry, s.Row, "land", at + landed, mine);
                            // 같은 칸의 말을 만남(13행, 결정 73 ⑤): 「!」와 같은 조건·때(MeetBeats.Since — 두 칸 이상 걸은 길의 마지막 걸음이 닿을 때)
                            if (occupied != null && m.Path != null && m.Path.Length >= 2 && occupied(m.Seat, m.Path[m.Path.Length - 1])) Emit(entry, s.Row, "meet", at + landed, mine);
                        }
                    return;
                case FlyBeats.Row:
                    if (FlyBeats.Flight(s) != null) Emit(entry, s.Row, "land", s.EndMs, mine);
                    return;
                case SpotBeats.Row:
                    if (SpotBeats.Cell(s, out bool reveal) >= 0) Emit(entry, s.Row, reveal ? "flip" : "pass", at + (reveal ? total * SpotBeats.FlipPermille / 1000 / 2 : 0), mine);
                    return;
                case HealBeats.Row:
                {
                    // 결정 73 ⑥ D3 나: 체력이 다 차는 단계 끝에 한 번(결정 70 ⑫의 칸마다 소리는 뺐다 — 징글은 M9)
                    if (HealBeats.Healing(s) == null) return;
                    Emit(entry, s.Row, "full", s.EndMs, mine);
                    return;
                }
                case TurnStartBeats.Row:
                    if (TurnStartBeats.Started(s) != null) Emit(entry, s.Row, "in", at, mine);
                    return;
                case ShieldBeats.Row:
                    if (ShieldBeats.Seat(s) >= 0) Emit(entry, s.Row, "crack", at + total * ShieldBeats.CrackPermille / 1000, mine);
                    return;
                case ApricornBeats.Row:
                    for (int i = 0, n = ApricornBeats.Count(s); i < n; i++) Emit(entry, s.Row, "drop", at + ApricornBeats.DropAt(total, i) + ApricornBeats.FallMs, mine);
                    return;
                case RevealAllBeats.Row:
                    if (RevealAllBeats.Revealed(s) != null) Emit(entry, s.Row, "flip", at + RevealAllBeats.FlipAt(0) + RevealAllBeats.FlipMs / 2, mine);
                    return;
                case AiLineBeats.Row:
                    if (AiLineBeats.Answer(s) != null) Emit(entry, s.Row, "in", at, mine);
                    return;
                case BankBeats.Row:
                    if (WorkRows.EventOf<GinkgoTraded>(s) == null) return;
                    Emit(entry, s.Row, "get", at + BankBeats.GetStart(0), mine);
                    Emit(entry, s.Row, "flip", at + BankBeats.FlipAt + BankBeats.FlipMs / 2, mine);
                    return;
                case CraftBeats.Row:
                    if (WorkRows.EventOf<Crafted>(s) != null) Emit(entry, s.Row, "pop", at + CraftBeats.FlashAt, mine);
                    return;
                case WorkRows.Maint:
                    Emit(entry, s.Row, "show", at, mine);
                    return;
                case StewBeats.Row:
                    if (WorkRows.EventOf<StewEaten>(s) != null) Emit(entry, s.Row, "bounce", at, mine);
                    return;
                case EvolveBeats.Row:
                {
                    var ev = WorkRows.EventOf<Evolved>(s);
                    if (ev == null) return;
                    Emit(entry, s.Row, "song", at, mine);
                    Emit(entry, s.Row, "flash", at + EvolveBeats.FlashAt(total), mine);
                    Emit(entry, s.Row, "fanfare", at + total * EvolveBeats.ClearPermille / 1000, mine);
                    EmitCry(entry, s.Row, at + total * EvolveBeats.HoldPermille / 1000, mine, dexOf, ev.PokemonId, ev.To);
                    return;
                }
                case FxRows.TurnSummary:
                case FxRows.Handoff:
                case FxRows.MyTurn:
                    Emit(entry, s.Row, "in", at, mine);
                    return;
                case EnterRows.Search:
                    Emit(entry, s.Row, WorkRows.EventOf<Searched>(s) is Searched sd && sd.Biome == Biome.Ocean ? "splash" : "rustle", at, mine);
                    return;
                case EnterRows.Reveal:
                {
                    var heavy = EncounterCards.HeavyOf(s);
                    for (int i = 0; i < heavy.Length; i++)
                    {
                        Emit(entry, s.Row, "flip", at + EncounterCards.FaceAt(heavy, i), mine);
                        if (heavy[i]) Emit(entry, s.Row, "thump", at + EncounterCards.LandAt(heavy, i), mine);
                    }
                    return;
                }
                case EnterRows.Picked:
                    Emit(entry, s.Row, "pick", at, mine);
                    return;
                case EnterRows.Wipe:
                    Emit(entry, s.Row, "close", at + (int)((long)total * EnterBeats.WipeCloseFrom / 1000), mine);
                    return;
                case EnterRows.Galactic:
                {
                    Emit(entry, s.Row, "in", at, mine);
                    var lineup = LineupBeats.Lineup(s);
                    if (lineup != null)
                        for (int i = 0; i < lineup.LineupCount; i++) Emit(entry, s.Row, "ball", at + LineupBeats.BallAt(i) + LineupBeats.DropMs, mine);
                    return;
                }
                case LiberateBeats.Row:
                    Emit(entry, s.Row, "flee", at + LiberateBeats.LiftAtMs, mine);
                    Emit(entry, s.Row, "chip", at + LiberateBeats.ChipAtMs, mine);
                    return;
                case FinaleRows.Heart:
                    Emit(entry, s.Row, "merge", at + (int)((long)total * HeartBeats.MergePermille / 1000), mine);
                    return;
                case FinaleRows.Ending:
                    Emit(entry, s.Row, "burn", at + EndingBeats.BurnFromMs, mine);
                    return;
                case FinaleRows.Order:
                {
                    var order = OrderBeats.Order(s);
                    int n = order != null && order.Seats != null ? order.Seats.Length : 0;
                    for (int i = 0; i < n; i++) Emit(entry, s.Row, "chip", at + OrderBeats.ChipAt(total, i, n), mine);
                    return;
                }
                case FinaleRows.Result:
                    Emit(entry, s.Row, "title", at + ResultBeats.TitleAtMs, mine);
                    return;
                // 운명의 변화(M5 「운명·응답 화면」)
                case FateRows.Star:
                case FateRows.Draw:
                {
                    var dealt = FateRows.First<FateDealt>(s);
                    int n = dealt != null && dealt.PerSeat != null ? dealt.PerSeat.Length : 0;
                    for (int i = 0; i < n; i++)
                    {
                        int start = FateDealBeats.SeatGapMs * i, fly = Math.Max(1, Math.Min(FateDealBeats.FlyMs, total - start - FateDealBeats.FlipMs));
                        Emit(entry, s.Row, "flip", at + start + fly, mine);
                    }
                    return;
                }
                case ResultRows.Stash:
                {
                    var r = ResultRows.EventOf<SpotResolved>(s);
                    if (r == null) return;
                    Emit(entry, s.Row, "pop", at + SpotResultBeats.PopMs, mine);
                    Emit(entry, s.Row, "gain", at + Math.Min(total, SpotResultBeats.LandAt(r)), mine);
                    return;
                }
                case ResultRows.Camp:
                    Emit(entry, s.Row, "land", at + 400, mine);
                    return;
                case ResultRows.Star:
                case ResultRows.Relic:
                    Emit(entry, s.Row, "land", at + SpotResultBeats.LiftMs + SpotResultBeats.FlyMs, mine);
                    return;
                case ResultRows.Alpha:
                {
                    var r = ResultRows.EventOf<SpotResolved>(s);
                    if (r != null && r.Result == SpotResult.Alpha) Emit(entry, s.Row, "flash", at + SpotResultBeats.FlashAtMs, mine);
                    return;
                }
                case ResultRows.GodGone:
                    Emit(entry, s.Row, "banner", at + GodBeats.BannerAtMs, mine);
                    return;
                case ResultRows.Spoils:
                    Emit(entry, s.Row, "drop", at + SpoilsBeats.DropAtMs, mine);
                    return;
                case ResultRows.Stored:
                    Emit(entry, s.Row, "in", at + total * 60 / 100, mine);
                    return;
                case ResultRows.Moved:
                {
                    var m = ResultRows.EventOf<RetributionMoved>(s);
                    if (m != null && m.Amount > 0) Emit(entry, s.Row, "hit", at + total * 70 / 100, mine);
                    return;
                }
                case ResultRows.Lock:
                    Emit(entry, s.Row, "lock", at, mine);
                    return;
                case ResultRows.GodPhase:
                    Emit(entry, s.Row, "rise", at + total * 65 / 100, mine);
                    return;
                case ResultRows.Form:
                    Emit(entry, s.Row, "flash", at + total * 75 / 100, mine);
                    foreach (var e in s.Events)
                        if (e is FormChanged fc) { EmitCry(entry, s.Row, Math.Min(s.EndMs, at + total * 75 / 100 + CryLagMs), mine, dexOf, fc.PokemonId, fc.After); break; }
                    return;
                case FateRows.Discard:
                    Emit(entry, s.Row, "tear", at + FateDiscardBeats.RiseMs + FateDiscardBeats.ShakeMs, mine);
                    return;
                case FateRows.Play:
                {
                    Emit(entry, s.Row, "in", at, mine);
                    var played = FateRows.First<FatePlayed>(s);
                    if (played != null && played.Play3) Emit(entry, s.Row, "merge", at + FatePassBeats.MergeFromMs + FatePassBeats.MergeMs, mine);
                    return;
                }
                case FateRows.Teleport:
                    Emit(entry, s.Row, "warp", at + total * TeleportBeats.GonePermille / 1000, mine);
                    Emit(entry, s.Row, "land", s.EndMs, mine);
                    return;
                case UbRows.Gate:
                    if (GateBeats.Event(s) == null) return;
                    Emit(entry, s.Row, "sink", at + total * GateBeats.SinkPermille / 1000, mine);
                    Emit(entry, s.Row, "land", at + total * GateBeats.LandPermille / 1000, mine);
                    return;
                case UbRows.Move:
                    if (UbRows.First<UbMoved>(s) is UbMoved um) EmitCry(entry, s.Row, s.EndMs, mine, dexOf, -1, um.UbId);
                    return;
                case UbRows.Destroy:
                    if (UbRows.First<CellDestroyed>(s) == null) return;
                    Emit(entry, s.Row, "break", at + total * DestroyBeats.DarkFromPermille / 1000, mine);
                    return;
                case UbRows.Start:
                    if (UbTurnBeats.Started(s) != null) Emit(entry, s.Row, "band", at, mine);
                    return;
                case UbRows.Strike:
                    if (total <= 0) return;
                    EmitCry(entry, s.Row, at, mine, dexOf, ShownNecrozma, GameEvent.U);
                    return;
                case UbRows.Bracket:
                    Emit(entry, s.Row, "pulse", at, mine);
                    if (BracketBeats.Dealt(s) != null) Emit(entry, s.Row, "card", at + Math.Min(total, BracketBeats.CardFromMs + BracketBeats.CardFlyMs), mine);
                    return;
                case UbRows.Lost:
                    Emit(entry, s.Row, "end", at, mine);
                    return;
                case UbBattleRows.Prism:
                    Emit(entry, s.Row, "block", at + PrismBeats.PopAtMs, mine);
                    return;
                case UbBattleRows.PrismMove:
                    Emit(entry, s.Row, "land", at + PrismBeats.FlyFromMs + PrismBeats.FlyMs, mine);
                    return;
                case UbBattleRows.Onslaught:
                    Emit(entry, s.Row, "flip", at + total * (OnslaughtBeats.RisePermille + OnslaughtBeats.FlipPermille) / 2000, mine);
                    return;
                case UbBattleRows.Phase:
                    Emit(entry, s.Row, "burst", at, mine);
                    return;
                case UbBattleRows.Coin:
                    Emit(entry, s.Row, "land", at + CoinBeats.SpinMs, mine);
                    return;
                case UbBattleRows.Trial:
                    if (UbBattleRows.First<TrialProgress>(s) is TrialProgress tp && tp.Done) Emit(entry, s.Row, "crack", at + total * TrialBeats.CrackAtPermille / 1000, mine);
                    return;
                case UbBattleRows.Guardian:
                    Emit(entry, s.Row, "plus", at + 300, mine);
                    return;
                case UbRows.Fly:
                    if (SpotFlyBeats.Event(s) != null) Emit(entry, s.Row, "warp", at + total * SpotFlyBeats.RisePermille / 1000, mine);
                    return;
                case UbBattleRows.Silvally:
                    if (SilvallyBeats.Event(s) != null) Emit(entry, s.Row, "arrive", at + Math.Min(total, SilvallyBeats.FlyFromMs + SilvallyBeats.FlyMs), mine);
                    return;
                case FallRows.Row:
                {
                    var fall = FallRows.Event(s);
                    if (fall is Eliminated) Emit(entry, s.Row, "crack", at + total * FallBeats.CrackPermille / 1000, mine);
                    else if (fall is Revived) Emit(entry, s.Row, "ring", at + total * FallBeats.RingPermille / 1000, mine);
                    return;
                }
                case WeatherBeats.Row:
                    if (CardRows.First<WeatherSet>(s) != null) Emit(entry, s.Row, "stop", at + WeatherBeats.SpinMs, mine);
                    return;
                case UnlockBeats.Row:
                {
                    var u = CardRows.First<CardUnlocked>(s);
                    if (u == null) return;
                    int breaks = UnlockBeats.Breaks(u);
                    Emit(entry, s.Row, "stamp", at + UnlockBeats.IconLandAt, mine);
                    for (int k = 0; k < breaks; k++)
                    {
                        Emit(entry, s.Row, "break", at + UnlockBeats.BreakAt(k), mine);
                        Emit(entry, s.Row, "flip", at + UnlockBeats.BreakAt(k) + UnlockBeats.FlipMs / 2, mine);
                    }
                    Emit(entry, s.Row, "lit", at + UnlockBeats.LitAt(breaks), mine);
                    return;
                }
                case SwapBeats.Row:
                    if (CardRows.First<TrainerCardSwapped>(s) == null) return;
                    Emit(entry, s.Row, "flip", at + SwapBeats.FlipAt + SwapBeats.FlipMs / 2, mine);
                    Emit(entry, s.Row, "lit", at + SwapBeats.LitAt, mine);
                    return;
                case AbilityBeats.Row:
                    if (CardRows.First<AbilityUsed>(s) != null) Emit(entry, s.Row, "in", at, mine);
                    return;
                case FateRows.Reroll:
                {
                    var c = RerollBeats.Event(s);
                    int n = c != null && c.Changes != null ? c.Changes.Length : 0;
                    for (int k = 0; k < n; k++) Emit(entry, s.Row, "lock", at + PlayTiming.RerollPerMs.Value * k + RerollBeats.LockMs, mine);
                    return;
                }
                case FateRows.Sacrifice:
                    Emit(entry, s.Row, "hit", at + total * SacrificeBeats.CoverPermille / 1000, mine);
                    return;
                case OfferBeats.Row:
                {
                    if (ShrineRows.First<ShrineOffered>(s) != null) Emit(entry, s.Row, "pillar", at + OfferBeats.PillarSoundAt, mine);
                    var drawn = ShrineRows.First<ShrineCardsDrawn>(s);
                    for (int i = 0, n = drawn != null && drawn.CardKeys != null ? drawn.CardKeys.Length : 0; i < n; i++) Emit(entry, s.Row, "flip", at + OfferBeats.FlipAt(i), mine);
                    return;
                }
                case BlessBeats.Row:
                    if (ShrineRows.First<ShrineBlessing>(s) != null) Emit(entry, s.Row, "get", at + BlessBeats.BadgeAt, mine);
                    return;
                case LegendBeats.Row:
                {
                    var legend = LegendBeats.Event(s);
                    if (legend == null) return;
                    if (legend.Placed) Emit(entry, s.Row, "land", at + LegendBeats.LandAt, mine);
                    for (int i = 0; i < LegendBeats.RainSeats(entry); i++) Emit(entry, s.Row, "rain", at + LegendBeats.RainLandAt(i), mine);
                    return;
                }
                case TallyBeats.Row:
                    if (TallyBeats.Event(s) != null) Emit(entry, s.Row, "flip", at + TallyBeats.FlipSoundAt, mine);
                    return;
                case DescendBeats.Row:
                {
                    var god = DescendBeats.Event(s);
                    if (god == null) return;
                    Emit(entry, s.Row, "bolt", at + DescendBeats.BoltAt, mine);
                    if (!god.Stayed) Emit(entry, s.Row, "land", at + DescendBeats.LandAt, mine);
                    return;
                }
                case StampBeats.Row:
                {
                    var stamp = StampBeats.Event(s);
                    if (stamp == null) return;
                    Emit(entry, s.Row, "slam", at + StampBeats.SlamAt, mine);
                    for (int i = 0, n = StampBeats.Rewards(stamp); i < n; i++) Emit(entry, s.Row, "reward", at + StampBeats.RewardAt(i), mine);
                    return;
                }
                case VsBeats.Row:
                    if (VsBeats.Event(s) == null) return;
                    Emit(entry, s.Row, "bang", at, mine);
                    Emit(entry, s.Row, "vs", at + VsBeats.VsAt, mine);
                    return;
                case TradeBeats.Row:
                    if (TradeBeats.Event(s) == null) return;
                    Emit(entry, s.Row, "song", at, mine);
                    Emit(entry, s.Row, "cross", at + TradeBeats.CrossFromMs, mine);
                    Emit(entry, s.Row, "get", at + TradeBeats.GlowAt, mine);
                    return;
                case FxRows.Intro:
                case FxRows.SendIn:
                    foreach (var e in s.Events)
                        if (e is SentIn si)
                        {
                            bool ball = si.Seat != SeatId.Wild && si.Seat != SeatId.UltraBeast;
                            if (ball)
                            {
                                if (IntroStage.Thrown(entry, s, si)) Emit(entry, s.Row, "throw", FxReveal.At(entry, si) - EnterBeats.IntroFlyMs, mine);
                                // 갤럭시단의 첫 출전도 볼이 날아든다(M9 다듬기 P05 — 화면 밖에서 던진 볼)
                                else if (si.Seat == SeatId.Galactic && IntroStage.FoeThrown(entry, s, si)) Emit(entry, s.Row, "throw", Math.Max(s.StartMs, FxReveal.At(entry, si) - EnterBeats.IntroFoeFlyMs), mine);
                                Emit(entry, s.Row, "open", FxReveal.At(entry, si), mine);
                            }
                            // 울음: 볼이면 열린 뒤(CryLagMs — 볼 열림 SE를 밀지 않는다), 야생 · 울트라 비스트는 드러나는 때
                            EmitCry(entry, s.Row, FxReveal.At(entry, si) + (ball ? CryLagMs : 0), mine, dexOf, si.PokemonId, si.Species);
                        }
                    return;
                case 560:
                {
                    Emit(entry, s.Row, "throw", at + PlayTiming.DiceThrowMs.Value, mine);
                    var flips = new List<DiceFlip>();
                    int timeline = DiceRollScene.Timeline(entry, flips);
                    int count = s.Tag is DiceRollScene scene ? scene.CountMs : timeline;
                    int dice = BattleRoll.FinalFaces(entry).Count;
                    for (int k = 0; k < dice; k++) Emit(entry, s.Row, "count", BattleRoll.CountedAt(count, k), mine);
                    foreach (var f in flips) Emit(entry, 570, "flip", f.AtMs, mine);
                    return;
                }
                case 540:
                    foreach (var e in s.Events)
                        if (e is OrderDecided o && o.TokenUsed) { Emit(entry, s.Row, "pass", at + InitiativeBeats.PassMs, mine); break; }
                    return;
                case 570:
                    if (!rolled && Flips(s)) Emit(entry, s.Row, "flip", at + DiceRollScene.FlipLeadMs, mine);
                    return;
                case 612:
                case 642:
                    foreach (var e in s.Events)
                        if (e is HpChanged h && h.After < h.Before)
                        {
                            Emit(entry, s.Row, "hit", at, mine);
                            break;
                        }
                    return;
                case 630:
                    foreach (var e in s.Events)
                        if (e is StatusTicked t && t.HpBefore != GameEvent.U && t.HpAfter != GameEvent.U && t.HpAfter < t.HpBefore)
                        {
                            Emit(entry, s.Row, "hit", at, mine);
                            break;
                        }
                    if (!rolled && Flips(s)) Emit(entry, s.Row, "flip", at + DiceRollScene.FlipLeadMs, mine);
                    return;
                case 650:
                    Emit(entry, s.Row, "faint", at, mine);
                    foreach (var e in s.Events)
                        if (e is Fainted f) EmitCry(entry, s.Row, at, mine, dexOf, f.PokemonId, SpeciesIn(entry, f.PokemonId));
                    return;
                case 641:
                    foreach (var e in s.Events)
                        if (e is FatigueChanged fc && fc.After != GameEvent.U && fc.Before != GameEvent.U && fc.After > fc.Before) { Emit(entry, s.Row, "exp", at, mine); break; }
                    return;
                case 670:
                    foreach (var e in s.Events)
                        if (e is Fled) { Emit(entry, s.Row, "flee", at, mine); break; }
                    return;
                case WhiteoutBeats.Row:
                    if (WhiteoutBeats.Event(s) != null) Emit(entry, s.Row, "fall", at, mine);
                    return;
                case CaptureStage.Row:
                    Capture(entry, s, mine);
                    return;
                default:
                    // 무음으로 적은 행(선턴 · 기술 · 상성 · 계산 줄 · 상태 이상 · 피로 칸 · 도망 · 잡은 뒤 · 결과 · 막 목표 …)은 단서가 없다. 표에 없는 행은 교차 시험이 잡는다
                    return;
            }
        }

        /// <summary>같은 항목에서 그 포켓몬이 나온 종(SentIn — 기절한 야생은 배틀 끝에 보기에서 사라질 수 있다). 없으면 U(화면 보기로 찾는다).</summary>
        static int SpeciesIn(FxEntry entry, int pokemonId)
        {
            foreach (var e in entry.Events)
                if (e is SentIn si && si.PokemonId == pokemonId && si.Species != GameEvent.U) return si.Species;
            return GameEvent.U;
        }

        // 울음 단서(층 Cry): 표 줄의 칸이 Cry면 그대로 · CryLow면 낮춰서. 종 번호를 모르면(dexOf 없음 · 0 이하) 달지 않는다
        static void EmitCry(FxEntry entry, int row, int atMs, bool mine, Func<int, int, int> dexOf, int pokemonId, int species)
        {
            if (dexOf == null) return;
            var line = Find(row, "cry");
            if (line == null) throw new InvalidOperationException("소리 표에 " + row + "행 「cry」 박자가 없다");
            string key = mine || line.Others == SameAsMe ? line.Me : line.Others;
            if (key == Silent) return;
            int dex = dexOf(pokemonId, species);
            if (dex <= 0) return;
            string cue = SoundKeys.CryKey(dex, key == SoundKeys.CryLow);
            if (atMs < 0) atMs = 0;
            foreach (var c in entry.Cues)
                if (c.IsSound && c.AtMs == atMs && c.Sound == cue) return;
            entry.AddSound(atMs, cue, row);
        }

        static bool Flips(FxStep s)
        {
            foreach (var e in s.Events)
                if (e is DiceFacesChanged d && d.Changes != null && d.Changes.Length > 0) return true;
            return false;
        }

        // 포획(68행): 볼 던지기와 포획 굴림이 함께 시작 → 볼이 손을 떠남 → 열리며 빨려 듦 → 흔들림 → 판정(딸깍 · 튀어나옴). 세기 틱은 판정 100 전에 끝나도록 뒤에서부터(결정 54 ①)
        static void Capture(FxEntry entry, FxStep s, bool mine)
        {
            if (s.Events.Count == 0 || !(s.Events[0] is CaptureAttempt c)) return;
            int at = s.StartMs;
            Emit(entry, s.Row, "roll", at, mine);
            Emit(entry, s.Row, "throw", at + EnterBeats.ReleaseMs, mine);
            Emit(entry, s.Row, "open", at + EnterBeats.FlyEndMs, mine);
            int verdict = CaptureStage.VerdictMs(c);
            int dice = c.Faces != null ? c.Faces.Length : 0;
            int rollMs = s.Tag is DiceRollScene scene ? scene.Clip.LengthMs : PlayTiming.DiceRollMaxMs.Value;
            int every = CaptureStage.CountEveryMs(verdict, rollMs, dice);
            for (int k = 0; k < dice; k++) Emit(entry, s.Row, "count", at + CaptureStage.CountAtMs(verdict, every, dice, k), mine);
            int start = CaptureStage.ShakeStartMs(c);
            for (int k = 0; k < Math.Max(0, c.Shakes); k++) Emit(entry, s.Row, "shake", at + start + k * PlayTiming.CaptureShakeMs.Value, mine);
            Emit(entry, s.Row, c.Success ? "click" : "break", at + verdict, mine);
            if (c.Success) Emit(entry, s.Row, "fanfare", at + verdict + FanfareLagMs, mine);
        }

        static void Emit(FxEntry entry, int row, string beat, int atMs, bool mine)
        {
            var line = Find(row, beat);
            if (line == null) throw new InvalidOperationException($"소리 표에 {row}행 「{beat}」 박자가 없다 — SoundCues.Table에 한 줄(나 · 남·관전 · 층)을 더한다");
            string key = mine || line.Others == SameAsMe ? line.Me : line.Others;
            if (key == Silent) return;
            if (atMs < 0) atMs = 0;
            foreach (var c in entry.Cues)
                if (c.IsSound && c.AtMs == atMs && c.Sound == key) return;
            entry.AddSound(atMs, key, row);
        }
    }

    /// <summary>소리 표 「즉시 · 연출」 칸(04 §8-4): 연출 = 화면이 그리는 단계의 키 프레임(<see cref="SoundCues.Table"/>) · 즉시 = 누른 순간(내 손 조작 — 단추·칸·핑) · 무음.</summary>
    public enum SoundTiming { Staged, Immediate, Silent }

    /// <summary>
    /// 04 §8-8 행 하나(또는 행 밖의 소리 — <see cref="Tag"/>)의 소리 표 줄(M9 소리 ③, 완료 조건 7): 나 / 남·관전 / 층 · 즉시·연출 · 뜻.
    /// 칸 값은 효과음 키(<see cref="SoundKeys"/>) · 울음(<see cref="SoundKeys.Cry"/>·<see cref="SoundKeys.CryLow"/>) · 무음 <see cref="SoundCues.Silent"/> — 여럿이면 「 · 」로 잇는다. 빈 칸은 없다(시험).
    /// 출처(원작 자산 SE 심볼 · plinkfx 레시피)는 키마다 효과음 표(<c>Tools/assets/tables/se.json</c>)가 한 곳이다.
    /// </summary>
    public sealed class SoundRowLine
    {
        public readonly int Row;
        public readonly string Tag, Me, Others, Note;
        public readonly SoundTiming Timing;
        public readonly SoundLayer Layer;

        public SoundRowLine(int row, string tag, SoundTiming timing, string me, string others, SoundLayer layer, string note)
        {
            Row = row;
            Tag = tag;
            Timing = timing;
            Me = me;
            Others = others;
            Layer = layer;
            Note = note;
        }
    }

    /// <summary>
    /// 04 §8-8 행마다의 소리 표(M9 소리 ③ — 04 §8-4 「§8-8 행마다 나 / 남·관전 / 층 세 칸 + 즉시·연출 구분」, 완료 조건 7). 행 1~<see cref="Count"/> 모두 한 줄씩:
    /// 연출 행은 그 행을 그리는 단계의 박자 줄(<see cref="SoundCues.Table"/> — 단계 id의 끝 자리를 뗀 것이 행, 같은 칸 만남 13 = 걷기 11의 「meet」, 회복 17 = 8과 같은 단계)을 모은 것이고,
    /// 그 밖(내 손 조작 · 다른 행 박자 안에서 그리는 행 · 소리 없는 행)은 <see cref="Explicit"/>에 이유와 함께 적는다. 행 밖의 소리(방 판 핑 · 감정 표현 · 도장 · 응답 시계 · 누르는 소리)는 <see cref="Extras"/>.
    /// </summary>
    public static class SoundRows
    {
        public const int Count = 102;
        const string S = SoundCues.Silent;
        const SoundLayer Se = SoundLayer.Se;
        const SoundTiming Now = SoundTiming.Immediate, Mute = SoundTiming.Silent;

        /// <summary>단계 박자 줄이 없는 행(즉시 · 무음).</summary>
        public static readonly SoundRowLine[] Explicit =
        {
            new SoundRowLine(10, null, Now, SoundKeys.Select + " · " + SoundKeys.Decide, S, Se, "맵 행동 미리보기: 칸을 고르면 select · 걷기를 받아들이면 decide(GameFlow 보드 칸 누르기) — 남 화면에는 고르는 것이 없다"),
            new SoundRowLine(12, null, Mute, S, S, Se, "못 누르는 칸·단추는 무음(꺼진 단추 · UiStage.OffClass — 04 §8-4)"),
            new SoundRowLine(23, null, Mute, S, S, Se, "전설 장소 찾기: 20행 「!」 bang · 22행 전환 wipe가 그 항목 안에서 낸다. 전설 곡 EV_LEGEND는 곡 시퀀스(소리 ②)"),
            new SoundRowLine(24, null, Mute, S, S, Se, "무리 시작·진행: 배틀 안 단계 줄 점등(81행과 같이 다른 행의 박자 안) — 타격음·기절이 낸다"),
            new SoundRowLine(40, null, Now, SoundKeys.Decide, S, Se, "기술 가르치기·잊기: 칸을 누르면 decide — 내 화면만"),
            new SoundRowLine(41, null, Now, SoundKeys.Decide, S, Se, "놓아주기: 단추 decide — 내 화면만"),
            new SoundRowLine(55, null, Now, SoundKeys.Decide, S, Se, "배틀 행동 고르기: 기술·볼·교체 줄 decide — 남 화면에는 고르는 것이 보이지 않는다"),
            new SoundRowLine(58, null, Now, SoundKeys.Decide + " · " + SoundKeys.Timer, S, Se, "응답 창: 답 decide · 내 시계 남은 10초 timer(선택마다 한 번 — 행 밖 「응답 시계」 줄) — 남 화면은 시계가 빨개질 뿐"),
            new SoundRowLine(76, null, Mute, S, S, Se, "우두머리 일격·비스트 부스트: 다른 행의 박자 안(641 · 612 — 타격음 hit · 피로 exp가 낸다)"),
            new SoundRowLine(77, null, Mute, S, S, Se, "우두머리 토큰 묶임: 사슬 꾸밈은 다듬기 목록 — 소리 없음 [가정 plan §5 #356]"),
            new SoundRowLine(81, null, Mute, S, S, Se, "무리 진행·승리: 단계 줄 점등(다른 행의 박자 안) — 결과 창 줄이 낸다"),
            new SoundRowLine(87, null, Mute, S, S, Se, "UB 배틀 시작: 22행 전환 안(wipe · 출전 볼 열림 · 울음)"),
            new SoundRowLine(98, null, Now, SoundKeys.Decide, S, Se, "S41 프롬프트 답: 단추 decide(대기열 밖 — 남 화면에는 창이 없다)"),
            new SoundRowLine(100, null, Mute, S, S, Se, "기록 줄은 무음(도장은 행 밖 「도장」 줄)"),
            new SoundRowLine(102, null, Mute, S, S, Se, "라운드 띠는 비차단 알림 — 소리 없음(턴 넘김 101행 band가 낸다) [가정 plan §5 #356]"),
        };

        /// <summary>행 밖의 소리(즉시 — 방 판 소통 · 응답 시계 · 누르는 소리 · 튜토리얼 층, 04 §8-4 · 02 M7-54·86·87 · 07 §9-1).</summary>
        public static readonly SoundRowLine[] Extras =
        {
            new SoundRowLine(0, "누르기", Now, SoundKeys.Select + " · " + SoundKeys.Decide, S, Se, "키트 단추 · 누를 칸: 고르기 select · 답 decide(UiStage.ButtonSound · stage.OnTap) — 내 화면만"),
            new SoundRowLine(0, "닫기·뒤로", Mute, S, S, Se, "닫기·뒤로·취소는 무음(결정 70 ⑯)"),
            new SoundRowLine(0, "핑", Now, SoundKeys.Ping, SoundKeys.Ping, Se, "방 판 핑 「!」이 뜸(누가 찍든 — 02 M7-86, 옛 meet 대역 #273). 내가 찍는 순간은 select"),
            new SoundRowLine(0, "감정 표현", Now, SoundKeys.Emote, SoundKeys.Emote, Se, "감정 표현이 뜸(누구 것이든 — 02 M7-87, 옛 select 대역 #273)"),
            new SoundRowLine(0, "도장", Now, SoundKeys.Mark, SoundKeys.Mark, Se, "기록 줄 도장이 찍힘(02 M7-87, 옛 token 대역 #273)"),
            new SoundRowLine(0, "응답 시계", Now, SoundKeys.Timer, S, Se, "내 시계 남은 10초 — 선택마다 한 번(02 M7-54 #221, 옛 token 대역). 남의 시계는 빨강만"),
            // 각본 튜토리얼 층(07 §9-1 — 층은 내 화면에만 있다, M9 「튜토리얼 잇기」 [가정 plan §5 #391])
            new SoundRowLine(0, "튜토리얼 넘김", Now, SoundKeys.Decide, S, Se, "말풍선 [다음] · [이 장 건너뛰기] · [튜토리얼 끝내기]: 키트 단추의 확정 소리(UiStage.ButtonSound — 「누르기」 줄과 같다)"),
            new SoundRowLine(0, "튜토리얼 말풍선", Mute, S, S, Se, "단계가 떠 말풍선 글이 바뀔 때는 무음(원작 대화창도 글이 뜰 때 소리가 없다 [추정])"),
            new SoundRowLine(0, "튜토리얼 hint", Now, SoundKeys.Select, S, Se, "구멍 밖을 눌러 층이 삼킨 클릭(PC 층 · 보드 칸) — 누름 한 번에 select 한 번, 말풍선이 hint로 바뀐다. 거르개가 막은 명령은 그 단추의 decide뿐"),
        };

        /// <summary>그 단계 박자 줄이 04의 몇 행인가(단계 id의 끝 자리를 뗀다 — 101행 1010~1012도 같다).</summary>
        public static int RowOf(SoundCueRow beat) => beat.Row / 10;

        /// <summary>그 행을 그리는 단계의 박자 줄(연출 소리) — 없으면 빈 목록. 같은 칸 만남 13 = 걷기 11의 「meet」, 회복 17 = 8과 같은 단계(HealBeats).</summary>
        public static List<SoundCueRow> Beats(int row)
        {
            var list = new List<SoundCueRow>();
            foreach (var b in SoundCues.Table)
            {
                bool meet = b.Row == 110 && b.Beat == "meet";
                if (row == 13 ? meet : row == 17 ? b.Row == HealBeats.Row : RowOf(b) == row && !meet) list.Add(b);
            }
            return list;
        }

        /// <summary>그 행의 소리 표 줄(1~<see cref="Count"/>). 연출 행은 박자 줄을 모아 만든다(나 · 남 칸 = 무음이 아닌 키를 「 · 」로, 다 무음이면 무음 · 층 = 울음 &gt; 징글 &gt; 효과음). 줄이 없으면 null.</summary>
        public static SoundRowLine Line(int row)
        {
            foreach (var l in Explicit) if (l.Row == row) return l;
            var beats = Beats(row);
            if (beats.Count == 0) return null;
            var me = new List<string>();
            var others = new List<string>();
            var layer = SoundLayer.Se;
            var note = new List<string>();
            foreach (var b in beats)
            {
                if (b.Me != SoundCues.Silent && !me.Contains(b.Me)) me.Add(b.Me);
                string o = b.Others == SoundCues.SameAsMe ? b.Me : b.Others;
                if (o != SoundCues.Silent && !others.Contains(o)) others.Add(o);
                if (b.Layer == SoundLayer.Cry || (b.Layer == SoundLayer.Jingle && layer == SoundLayer.Se)) layer = b.Layer;
                note.Add(b.Row + " " + b.Beat);
            }
            bool silent = me.Count == 0 && others.Count == 0;
            return new SoundRowLine(row, null, silent ? SoundTiming.Silent : SoundTiming.Staged, me.Count > 0 ? string.Join(" · ", me) : SoundCues.Silent,
                others.Count > 0 ? string.Join(" · ", others) : SoundCues.Silent, layer, "연출 박자 " + string.Join(", ", note));
        }

        /// <summary>사람이 볼 소리 표(TSV — 행 · 이름표 · 즉시/연출 · 나 · 남·관전 · 층 · 뜻).</summary>
        public static string Tsv()
        {
            var sb = new System.Text.StringBuilder("row\ttag\ttiming\tme\tothers\tlayer\tnote\n");
            void Put(SoundRowLine l) => sb.Append(l.Row).Append('\t').Append(l.Tag ?? "").Append('\t').Append(l.Timing).Append('\t').Append(l.Me).Append('\t').Append(l.Others).Append('\t').Append(l.Layer).Append('\t').Append(l.Note).Append('\n');
            for (int r = 1; r <= Count; r++) { var l = Line(r); if (l != null) Put(l); }
            foreach (var l in Extras) Put(l);
            return sb.ToString();
        }
    }
}
