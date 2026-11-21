using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>04 §8-1 P2 막기 등급. Step = 「항목 안 단계」 행(9·75): 부모 항목에 더하고 보기 시간을 따로 받지 않는다.</summary>
    public enum FxBlock { None, Queue, Step, UntilEndAll, UntilEndParty }

    /// <summary>항목 하나(= Seq 하나)의 길이. Seq = 그 항목의 ActionStarted.Seq(앞에 ActionStarted가 없으면 첫 이벤트의 Seq).</summary>
    public struct FxItem
    {
        public int Seq;
        /// <summary>대기열 항목 길이(max(행 길이 합, 보기 시간), 04 §8-5 규칙 1).</summary>
        public int QueueMs;
        /// <summary>보기 시간을 대기 전의 행 길이 합(차단 컷신 빼고). 화면이 따로 분류해 놓은 단계의 합과 같아야 한다(M4_연출길이방장화면같음). QueueMs − StepsMs = 끝에 멈춰 보이는 시간.</summary>
        public int StepsMs;
        /// <summary>그 좌석이 범위 안인 차단 컷신 길이(until-end, 02 §5-7 「차단 컷신 합」).</summary>
        public int BlockMs;
    }

    /// <summary>
    /// 좌석 하나의 배틀 맥락(04 §8-2): 지금 열린 배틀과 그 좌석이 당사자인가. 야생전(전설·우두머리·무리·UB 포함)은 당사자 좌석에만 센다(규칙 5).
    /// 배틀은 갱신 여러 개에 걸치므로 세션(과 M4 화면 대기열)이 좌석마다 하나씩 들고 이벤트를 흘려 보낸다. 이어하기는 <see cref="From"/>로 다시 세운다.
    /// </summary>
    public sealed class PlayTimingContext
    {
        public int Viewer;
        /// <summary>판의 좌석 수(턴 넘김 101행 — 한 좌석 판은 짧다, 결정 57 ③). 0 = 모름(여럿으로 센다). 상태에서 받는다(<see cref="From"/>).</summary>
        public int Seats;
        /// <summary>지금 이 좌석의 대기열에서 빼는 야생전 안이다(BattleStarted ~ BattleEnded).</summary>
        public bool SkippingBattle;
        /// <summary>
        /// 남의 야생전도 센다(방 판의 화면 대기열 — 결정 112 Q19 [가정 M7-91]). 방장 멈춤(좌석마다 센 것의 최댓값 = 당사자의 길이)은 그대로라
        /// 기본값(끔 — 방장 · 솔로 · 튜토리얼)을 바꾸지 않는다.
        /// </summary>
        public bool WatchWild;
        /// <summary>지금 턴에 턴 결과 요약에 적을 변화가 있었다(<see cref="PlayTiming.IsTurnGain"/> — TurnStarted에서 지운다). 턴은 갱신 여러 개에 걸친다.</summary>
        public bool TurnGain;
        /// <summary>지금 턴에 정비 보기(38·40·41행 — <see cref="PlayTiming.MaintRow"/>)를 이미 받은 좌석(비트) — 같은 턴의 정비는 묶어서 한 번(결정 120). TurnStarted에서 지운다.</summary>
        public int MaintSeen;

        public PlayTimingContext(int viewer, int seats = 0)
        {
            Viewer = viewer;
            Seats = seats;
        }

        /// <summary>보기·원본 상태에서 다시 세운다(이어하기·재접속). 그 턴에 이미 있었던 변화는 모른다 — 방장·화면이 똑같이 「없음」에서 시작한다.</summary>
        public void From(GameState s)
        {
            Seats = s.SeatCount;
            TurnGain = false;
            MaintSeen = 0;
            SkippingBattle = false;
            var b = s.Battle;
            if (b == null || !PlayTiming.IsWildKind(b.Kind) || WatchWild) return;
            SkippingBattle = b.Sides[0].Responder != Viewer && b.Sides[1].Responder != Viewer;
        }
    }

    /// <summary>
    /// 연출 길이(02 §5-7, 04 §8-5 길이 규칙). 방장 멈춤과 M4 화면 대기열이 이 함수 하나로 항목 길이를 센다. 입력은 그 좌석에 걸러진 이벤트뿐이다(01 §3-6 ⑩) —
    /// 길이가 숨은 정보에 따라 달라지지 않는다. 행 숫자는 04 §8-8 시간 열 범위의 위쪽 끝이고 모두 [가정]이다. 단계가 나뉜 행(1·21·61·64)은 단계마다 숫자 하나다.
    /// 수치를 바꾸면 방장·손님이 같은 빌드여야 한다(02 §4-6). 주사위·피해 행(43·56·57·61)은 조정 값 등록부(<see cref="Tunables.Shared"/>, Host)에서 읽는다 —
    /// 기본값은 Docs/Design/spikes/dice-roll/README.md §4 시간표다(04 §8-5 규칙 7). 연출 목업에서 정한 박자(걷기 11·찾아보기 20·포획 68·차례 넘김 101행,
    /// 결정 53·54 — 04 §8-5 규칙 8)도 Host 값이다. 나머지 행은 아직 상수다.
    /// </summary>
    public static class PlayTiming
    {
        // 공용 수치(04 §8-5 표)
        public const int RevealBaseMs = 3000;
        public const int RevealPerMs = 500;
        public const int RevealCapMs = 6000;
        public const int PlainMs = 1500;
        /// <summary>숨은 손패 반응 창 최소 시간(01 §3-3, 02 §5-7).</summary>
        public const int HiddenReactionMinMs = 800;

        // 주사위·피해 시간표(spikes/dice-roll §4, 결정 50) — Host 조정 값. 화면 부품은 이 값 안에서 박자를 나눈다.
        /// <summary>56행 던짐.</summary>
        public static readonly Tunable DiceThrowMs = Host("dice.throwMs", 400);
        /// <summary>56행 굴림: 녹화본 길이 800~1600의 위쪽 끝. 화면은 고른 녹화본이 짧으면 남는 시간을 멈춘 화면으로 채운다.</summary>
        public static readonly Tunable DiceRollMaxMs = Host("dice.rollMaxMs", 1600);
        /// <summary>56행 다 멈춘 뒤(면 바꾸기가 있으면 그 뒤) 세기 시작까지.</summary>
        public static readonly Tunable DiceCountGapMs = Host("dice.countGapMs", 250);
        /// <summary>56행 세기: 주사위 하나마다.</summary>
        public static readonly Tunable DiceCountPerMs = Host("dice.countPerMs", 130);
        /// <summary>57행 면 바꾸기 한 박자: 꼬리표·고리 300 + 150 뒤 넘김 280. 여러 개가 바뀌어도 함께 넘어간다.</summary>
        public static readonly Tunable FaceFlipMs = Host("dice.faceFlipMs", 730);
        /// <summary>61행 계산 줄: 세기 끝 + 150 효과 · + 100 상성 · + 100 데미지.</summary>
        public static readonly Tunable DamageCalcMs = Host("damage.calcMs", 350);
        /// <summary>61행 숫자 비행: 300 뒤 400.</summary>
        public static readonly Tunable DamageFlyMs = Host("damage.flyMs", 700);
        /// <summary>61행 HP 감소.</summary>
        public static readonly Tunable HpDrainMs = Host("damage.hpMs", 800);
        /// <summary>43행 글리치 재굴림: 주사위 하나마다(들어옴 200 · 지직 1000 · 고정 + 번쩍 · 1500까지 고리 — 결정 190으로 옛 600의 2.5배).</summary>
        public static readonly Tunable RerollPerMs = Host("reroll.perMs", 1500);
        /// <summary>43행 다시 세기 500 + 데미지 갱신 500(결정 190 — 옛 400의 2.5배).</summary>
        public static readonly Tunable RerollRecountMs = Host("reroll.recountMs", 1000);

        // 연출 목업 박자(결정 53·54, Logs/UiMock/r4-fx·r5-fx 「보통」) — Host 조정 값
        /// <summary>11행 걷기: 예비(웅크림). 걸음이 없으면 세지 않는다.</summary>
        public static readonly Tunable WalkPrepMs = Host("walk.prepMs", 150);
        /// <summary>11행 걷기: 칸당.</summary>
        public static readonly Tunable WalkStepMs = Host("walk.stepMs", 270);
        /// <summary>11행 걷기: 오르막 한 칸마다 더하는 값(목업에 오르막이 없어 설계 값 그대로 [가정]).</summary>
        public static readonly Tunable WalkUpMs = Host("walk.upMs", 80);
        /// <summary>11행 걷기: 끝 착지(눌렸다 펴짐).</summary>
        public static readonly Tunable WalkLandMs = Host("walk.landMs", 200);
        /// <summary>20행 찾아보기(「!」 → 흔들림 → 조우 창·카드).</summary>
        public static readonly Tunable SearchMs = Host("search.ms", 1750);
        /// <summary>68행 포획: 볼이 떨어져 튕긴 뒤 흔들림이 시작하는 때(던짐·굴림은 0에 함께 시작).</summary>
        public static readonly Tunable CaptureShakeStartMs = Host("capture.shakeStartMs", 2480);
        /// <summary>68행 포획: 흔들림 한 번.</summary>
        public static readonly Tunable CaptureShakeMs = Host("capture.shakeMs", 470);
        /// <summary>68행 포획: 판정이 나오는 가장 이른 때(가장 긴 굴림이 멈추고 다 세는 데 드는 시간).</summary>
        public static readonly Tunable CaptureVerdictMinMs = Host("capture.verdictMinMs", 2350);
        /// <summary>68행 포획 성공: 판정(딸깍) 뒤 별·결말 대사.</summary>
        public static readonly Tunable CaptureSuccessTailMs = Host("capture.successTailMs", 900);
        /// <summary>68행 포획 실패: 판정(튀어나옴) 뒤 결말 대사.</summary>
        public static readonly Tunable CaptureFailTailMs = Host("capture.failTailMs", 750);
        // 101행 턴 넘김(결정 57 ②③): 여럿 = 요약 800 + 띠 600 + 컷신 600 = 2000, 한 좌석 판 = 띠 500 + 컷신 500 = 1000(요약은 변화가 있을 때만 800 더)
        /// <summary>101행 턴 결과 요약.</summary>
        public static readonly Tunable TurnSummaryMs = Host("turn.summaryMs", 800);
        /// <summary>101행 넘김 띠.</summary>
        public static readonly Tunable HandoffMs = Host("turn.handoffMs", 600);
        /// <summary>101행 내 차례 컷신(차례 좌석만).</summary>
        public static readonly Tunable MyTurnMs = Host("turn.myTurnMs", 600);
        /// <summary>101행 넘김 띠 — 한 좌석 판.</summary>
        public static readonly Tunable SoloHandoffMs = Host("turn.soloHandoffMs", 500);
        /// <summary>101행 내 차례 컷신 — 한 좌석 판.</summary>
        public static readonly Tunable SoloMyTurnMs = Host("turn.soloMyTurnMs", 500);

        static Tunable Host(string key, int def) => Tunables.Shared.Define(key, def, 0, 20000, TunableScope.Host);

        /// <summary>한 좌석 판인가(결정 57 ③ — 턴 넘김이 짧다). 좌석 수를 모르면(0) 여럿으로 센다.</summary>
        public static bool IsSolo(int seats) => seats == 1;

        /// <summary>
        /// 턴 결과 요약에 적을 변화(101행)인가. 화면(<c>GameFlow</c>의 요약 줄)과 같은 것을 센다 — 주운 규토리 · 잡은 포켓몬 ·
        /// 배틀로 받은 여행 점수·규토리(도망친 야생이 남긴 것 포함) · 막 목표 달성. 요약 줄을 늘리면 여기도 늘린다
        /// (한 좌석 판은 이것이 없으면 요약 단계가 없다 — 방장·화면이 같은 이벤트로 가른다).
        /// </summary>
        public static bool IsTurnGain(GameEvent e)
        {
            switch (e)
            {
                case ApricornsTaken a: return a.Count > 0;
                case PokemonGained _: return true;
                case BattleEnded b: return Len(b.Changes) > 0;
                case Fled f: return Len(f.Changes) > 0;
                case ActGoalMet _: return true;
                default: return false;
            }
        }

        /// <summary>101행 턴 결과 요약을 보이나: 여럿이면 늘, 한 좌석 판은 그 턴에 변화가 있었을 때만(결정 57 ③ [가정]).</summary>
        public static bool ShowsTurnSummary(int seats, bool gain) => gain || !IsSolo(seats);

        /// <summary>101행 넘김 띠 길이(좌석 수에 따라).</summary>
        public static int HandoffFor(int seats) => IsSolo(seats) ? SoloHandoffMs.Value : HandoffMs.Value;

        /// <summary>101행 내 차례 컷신 길이(좌석 수에 따라).</summary>
        public static int MyTurnFor(int seats) => IsSolo(seats) ? SoloMyTurnMs.Value : MyTurnMs.Value;

        /// <summary>
        /// 43행 디아루가 재굴림 길이(고른 주사위 수 — 하나씩 차례로 글리치 + 다시 세기). 재굴림은 DiceFacesChanged(Source = 디아루가 카드, rulings A24 ①) → 행 430.
        /// </summary>
        public static int RerollMs(int dice) => RerollPerMs.Value * Math.Max(0, dice) + RerollRecountMs.Value;

        /// <summary>
        /// 68행 포획의 판정 시각(결정 54 ①): 볼 던지기와 포획 주사위 굴림이 0에 함께 시작하고, 흔들림이 끝나는 때 성공·실패가 나온다 —
        /// max(판정 최소 2350, 2480 + 470 × 흔들림 수 — 플레이 시험 T28로 던지기가 1.5배, 옛 1650). 화면은 합계가 이 시각 100ms 전에 서도록 세기 간격을 뒤에서부터 맞춘다.
        /// </summary>
        public static int CaptureVerdictMs(int shakes) =>
            Math.Max(CaptureVerdictMinMs.Value, CaptureShakeStartMs.Value + CaptureShakeMs.Value * Math.Max(0, shakes));

        /// <summary>
        /// 68행 포획 전체 길이 = 판정 + 결말(성공 900 · 실패 750): 성공(흔들림 3번) 4790, 실패 1 모자람(2번) 4170, 2 모자람(1번) 3700, 3 이상 모자람(0번) 3230.
        /// 포획 굴림은 따로 56행을 세지 않고 이 안에 겹친다(CaptureAttempt 하나가 던짐·굴림·흔들림·판정을 모두 싣는다). 흔들림 수는 공개 정보(눈 합 · 포획률)다.
        /// </summary>
        public static int CaptureMs(bool success, int shakes) =>
            CaptureVerdictMs(shakes) + (success ? CaptureSuccessTailMs.Value : CaptureFailTailMs.Value);

        /// <summary>행(04 §8-8 행 × 10 + 단계)마다 막기 등급과 「결과 보기」(보기 시간 3000 + 장당 500) 여부. 분류 안 된 이벤트는 −1.</summary>
        public const int Unknown = -1;
        const int RowSlots = 1040;

        /// <summary>그 이벤트가 닿는 행(04 §8-8 행 × 10 + 단계). 행이 없으면(창·미리보기·정비·다른 행의 일부) 0. 분류 안 된 타입은 Unknown.</summary>
        public static int RowOf(GameEvent e)
        {
            switch (e)
            {
                case TrainerCardsDealt _: return 10;
                case SecretAnswered sa: return sa.Window != Window.None ? 0 : 11; // 1행 「고른 뒤」(99행 동시 선택도 같은 값). 반응 창 묶음은 창의 일부(58행 none)
                case TrainerCardsRevealed _: return 12;
                case VillagePicked _: return 20;
                case StarterPicked _: return 30;
                case ActRevealed _: return 40;
                case TokensPlaced _: return 50;
                case TurnStarted _: return 60;
                case ItemHeld _:                       // 38·40·41행 정비(지닌물건 · 기술 · 놓아주기 · 순서): 남 화면만 정비 판 보기(결정 120 — RowMsFor · Bundled)
                case MoveTaught _:
                case MoveForgotten _:
                case Released _:
                case PartyReordered _: return MaintRow;
                case PokemonExchanged _: return MaintRow;   // 사당의 그다음 고르기 결과(동등한 교환 — 결정 120): 남 화면에 그 좌석의 판 보기(묶지 않는다 — IsShrineGift)
                case ApricornsGained ag: return ag.Source.Kind == SourceKind.ShrineEvent ? MaintRow : 0;   // 뜻밖의 선물의 규토리(사당 카드)만 — 그 밖(gardener B 등)은 자원 줄 착지뿐
                case Healed _: return 80;              // 8·17행(둘 다 회복 징글 2800)
                case ProtectionCleared _: return 90;
                case Moved _: return 110;
                case Flew _: return 160;
                case GinkgoTraded _: return 180;       // 18행 은행상회(M5 「제작·은행·진화 화면」)
                case Crafted _: return 360;            // 36행 제작
                case StewEaten _: return 370;          // 37행 스튜 효과
                case Evolved _: return 390;            // 39행 진화(본인 4800 · 남 2000 — RowMsFor)
                case ApricornsTaken _: return 190;
                case Searched _: return 200;
                case EncounterDrawn _: return 211;     // 21행 공개(뒤집기)는 공개 이벤트가 있는 찾아보기 항목에서
                case EncounterPicked _: return 212;    // 21행 「고른 뒤」
                case BattleStarted _: return 220;
                case HiddenSpotRevealed _:
                case HiddenSpotPassed _: return 250;
                // M5 「맵 결과·배틀 안 연출」: 숨겨진 장소 결과 26~30행 · 트레이너 배틀 승리 보상 46행 · 신 둘째 폼 78행 · 배틀 중 폼 바꾸기 79행 · 신 격파 80행
                case SpotResolved sr: return sr.Result == SpotResult.Stash ? 260 : sr.Result == SpotResult.Camp ? 270 : sr.Result == SpotResult.Star ? 280 : sr.Result == SpotResult.Relic ? 300 : 290;
                case SpoilsTaken _: return 461;
                case FormChanged fc: return !fc.Temporary ? 0 : fc.Source.Kind == SourceKind.Rule && fc.Source.Id == (int)RuleSource.CelestialAura ? 780 : 790;
                case TokenRemoved tr: return tr.TokenKind == TokenKind.Grunt ? 941 : tr.TokenKind == TokenKind.God && tr.Source.Kind != SourceKind.ShrineEvent ? 800 : 0; // 신 격파만 — 천상의 균열이 바꿔 놓은 신(FractureRules.Descend)은 강림 341이 그린다
                case ProtectionSet _: return 460;
                case ChallengeIssued _: return 510;
                case SentIn _: return 660;             // 인트로(53행)는 항목 단위로 고친다(Measure)
                case InitiativeTokenAt _: return 530;
                case OrderDecided _: return 540;
                case DiceRolled _: return 560;
                case DiceFacesChanged d: return d.Source.Kind == SourceKind.Fate ? 430 : d.Source.Kind == SourceKind.Status ? 630 : 570;   // 43행 디아루가 재굴림(M5 「운명·응답 화면」)
                case ChoiceAnswered a: return a.ByAi && a.Seat <= SeatId.Galactic ? 730 : 0; // 가상 응답자 AI 자막(73행). 나머지 답은 창(58·98행, none)
                case MoveUsed _: return 590;
                case TypeMatchup _: return 600;
                case DamageBreakdown _: return 611;
                case HpChanged h: return h.Source.Kind == SourceKind.Fate ? 440 : h.Source.Kind == SourceKind.Rule && h.Source.Id == (int)RuleSource.Fatigue ? 642 : 612;   // 44행 기라티나 희생
                case StatusApplied _: return 620;
                case StatusTicked _: return 630;
                case FatigueChanged f: return f.Cause == FatigueCause.Roll || f.Cause == FatigueCause.Capture || f.Cause == FatigueCause.Switch ? 0 : 641;
                case Fainted _: return 650;
                case SwitchedOut _: return 660;
                case Fled _:
                case Forfeited _: return 670;
                case CaptureAttempt _: return 680;
                case PokemonGained _: return 690;
                case BattleEnded _: return 700;
                case Whiteout _: return 710;
                case GalacticBattle _: return 720;
                case RetributionStored _: return 741;
                case RetributionMoved _: return 742;
                case SwitchLocked _: return 750;
                case ActGoalMet _: return 940;
                case ShardGained _:                    // 94행 2막(결정 74 — 「막·종막 화면」): 조각·파티 칸은 막 목표와 같은 박자(배틀 결과 창의 줄)
                case PartySlotUnlocked _: return 940;
                case HeartRestored _: return 943;      // 94행 3막: 시작의 사당 위에서 조각 셋이 심장으로 합쳐진다(보드)
                case EndingChosen _: return 950;       // 95행 종막 선택
                case TraitorOrderSet _: return 960;    // 96행 배신자 순서 공개
                case GameEnded ge: return ge.Reason == GameEndReason.BreachLost ? 860 : 970;   // 97행 게임 끝과 결과 · 86행 균열 70% 패배(M6 「UB 화면 묶음 1」 — 보드 덮개)
                case FateDealt fd: return fd.Source.Kind != SourceKind.Rule ? 420 : fd.Source.Id == (int)RuleSource.FallenStar ? 70 : fd.Source.Id == (int)RuleSource.Respawn ? 80 : 420;   // 7행 떨어진 별 · 8행 귀환의 「운명 1장」(회복 단계 끝에 — 길이 그대로) · 42행 받기(M5 「운명·응답 화면」)
                case FateDiscarded _: return 421;      // 42행 6장째 버리기
                case FatePlayed _: return 450;         // 45행 카드 내기(카드 지나가기 · 3장 합쳐짐)
                case Teleported _: return 140;         // 14행 펄기아 순간이동
                case GateTravel _: return 150;         // 15행 왜곡 게이트 이동(M6 「UB 보드 화면」)
                case UbMoved _: return 820;            // 82행 UB 한 칸 이동(이동마다 단계 하나)
                case CellDestroyed _: return 830;      // 83행 UB가 밟은 칸의 파괴(파괴마다 — 네크로즈마의 원격 파괴도 칸마다 이 박자)
                case UbTurnStarted _: return 821;      // 82행 예비: UB 턴 시작(균열 칩 눌림 · UB 턴 띠 · 102행 UB 페이즈 띠 — M6 「UB 화면 묶음 1」)
                case NecrozmaStrike _: return 840;     // 84행 네크로즈마가 빛을 모음(빛줄기는 칸마다 830 단계)
                case BreachBracket _:                  // 85행 균열 10% 구간 · 왜곡 균열 카드 받기(한 단계)
                case BreachCardsDealt _: return 850;
                case PrismReduced _: return 880;       // 88행 프리즘 아머가 막음(M6 「UB 배틀 화면 묶음」 — 배틀 화면)
                case PrismMoved _: return 881;         // 88행 UB 격파로 프리즘 아머 1개가 울트라 비스트 쪽으로
                case OnslaughtDrawn _: return 890;     // 89행 울트라 온슬롯 카드
                case NecrozmaPhase _: return 900;      // 90행 네크로즈마 페이즈 격파(결과 창 [확인] 뒤 보드)
                case CoinFlip _: return 901;           // 90행 썬/문 동전(2페이즈 첫 도전 — 조우 전환 앞 보드)
                case Eliminated _:                     // 91행 탈락·부활(M6 「UB 화면 묶음 2」 — 좌석마다 단계 하나)
                case Revived _: return 910;
                case TrialProgress _: return 920;      // 92행 호수 시련 진행·넘김
                case GuardianDown _: return 921;       // 92행 수호신 격파·포획 → 모두 이동력 +2
                case TokenMoved _: return 831;         // 83행 밟힌 칸의 숨겨진 장소 토큰이 깨어진 세계로 날아감(옮김마다 단계 하나 — M6 「연출 목록 채우기」)
                case BlessingGained bg: return bg.Source.Kind == SourceKind.Rule && bg.Source.Id == (int)RuleSource.Silvally ? 930 : 0;   // 93행 실버디 → 축복 +1(결과 창 [확인] 뒤 보드) · 아르세우스 운명 3장의 축복은 45행 카드 내기 안
                case WeatherSet _: return 350;         // 35행 날씨 휠 놓기·바꾸기(M6 「트레이너 카드 해금 연출」 — parasol_lady B, rulings A41)
                case CardUnlocked _: return 480;       // 48행 트레이너 카드 해금(결정 84 M5 가 — 남 좌석도 같은 길이 #708)
                case TrainerCardSwapped _: return 490; // 49행 반바지 꼬마 빨강 카드 바꾸기(rulings A40)
                case AbilityUsed _: return 500;        // 50행 능력 사용(출처 카드 통통 — 효과는 그 결과 행)
                case ShrineOffered _:                 // 31행 사당 공물과 카드 뽑기(M5 「사당·균열 화면」)
                case ShrineCardsDrawn _: return 310;
                case ShrineBlessing _: return 320;     // 32행 사당의 축복(바친 좌석 · 나눠 받은 좌석)
                case LegendEvent _: return 330;        // 33행 전설 이벤트
                case FollowerTally _: return 340;      // 34행 천상의 균열: 추종자 공개 · 집계
                case GodDescended _: return 341;       // 34행 신 강림
                case StampGained _: return 470;        // 47행 여행 스탬프(M5 「스탬프·도전·거래 화면」)
                case TradeDone _: return 520;          // 52행 거래(주고받기)
                case SecretRevealed sr: return sr.Window != Window.None ? 0 : 990; // 반응 창 묶음의 공개는 연출 없음 — 쓴 반응은 자기 Seq의 결과 이벤트가 맡는다
                // 행이 없는 것: 창(58·98)·미리보기·정비(38·40·41)·라운드 띠(102, 비차단) / 다른 행의 일부(3·4·5·6·8·17·19·21·53·66·71·94)
                case ActionStarted _:
                case ChoiceRequested _:
                case ChoiceReissued _:
                case WindowSkipped _:
                case RoundStarted _:
                case MoveSelected _:
                case ChallengeAnswered _:
                case CardsReturned _:
                case PileShuffled _:
                case FlyBlocked _:
                case MoveAllowanceSet _:
                case RespawnCleared _:
                case RespawnPending _:
                case UsageMarked _:
                case ApricornSpent _:
                case TurnEnded _:
                case TradeProposed _:  // 거래 제안·응답은 창(98행 — 대기열 밖)과 기록 줄만
                case TradeAnswered _:
                case BreachChanged _:  // 울트라 버스트: 균열 바뀜은 앞의 파괴 단계(830)가 그린다
                case BreachCardAttached _:
                case BreachCardDiscarded _:
                case BreachCardMoved _:
                case BlessingUsed _:
                case UbTurnSkipped _:
                    return 0;
                default:
                    return Unknown;
            }
        }

        static FxBlock BlockOf(int row)
        {
            switch (row)
            {
                case 40:
                case 330:
                case 341:
                case 950:
                case 860:
                case 970: return FxBlock.UntilEndAll;
                case 220:
                case 510:
                case 720: return FxBlock.UntilEndParty;
                case 90:
                case 750: return FxBlock.Step;
                default: return FxBlock.Queue;
            }
        }

        /// <summary>결과 보기 행(B 등급·P14): 보기 시간 = 3000 + 장당 500(상한 6000). 나머지 queue 항목은 1500.</summary>
        static bool IsResult(int row) => row == 12 || row == 200 || row == 211 || row == 560 || row == 611 || row == 612 || row == 680 || row == 990;

        /// <summary>같은 항목에서 같은 행이 여러 번 닿으면 더하는 행(걸음 · 면 바꾸기 — 이벤트마다 한 박자). 나머지 행은 큰 값 하나(「행마다 숫자 하나」 — 동시 피해는 같은 박자).</summary>
        static bool Sums(int row) => row == 110 || row == 570 || row == 470 || row == 820 || row == 830 || row == 831;   // 스탬프(47행)는 하나씩 — 보상이 다음 스탬프를 잇달아 부르기도 한다 · UB 이동·파괴(82·83행)도 하나씩

        /// <summary>그 이벤트의 행 길이(시간 열 범위의 위쪽 끝, 개수식 그대로). 화면 대기열도 단계 길이를 이 함수로 센다(행은 화면이 따로 고른다).</summary>
        public static int RowMs(GameEvent e, int row)
        {
            switch (row)
            {
                case 10: return 800;
                case 11: return 1200;
                case 12: return 2500;
                case 20: return 1800;
                case 30: return 2200;
                case 40: return 3600;
                case 50: return Math.Min(3500, 1500 + 300 * Len(((TokensPlaced)e).Villages));
                case 60: return 1400;
                case 80: return 2800;                   // 소리에 묶인 상수(회복 징글, 규칙 2)
                case 90: return 500;
                case 110:
                {
                    var m = (Moved)e;
                    int steps = Math.Max(0, Len(m.Path) - 1), up = 0;
                    if (m.Levels != null)
                        for (int i = 1; i < m.Levels.Length; i++)
                            if (m.Levels[i] > m.Levels[i - 1]) up++;
                    // 예비 + 칸당 + 오르막 + 끝 착지(결정 53 ① — 2칸 = 150 + 270 × 2 + 200 = 890). 걸음이 없으면 0
                    return steps == 0 ? 0 : WalkPrepMs.Value + WalkStepMs.Value * steps + WalkUpMs.Value * up + WalkLandMs.Value;
                }
                case 160: return FlyMs;                 // 결정 167: 컷인 + 택시(옛 3200)
                case 180: return 2500;
                // 사당·균열(M5 「사당·균열 화면」 — 04 31~34행 시간 열 안에서 고름 [가정 plan §5 #115])
                case 310: return 2500;
                case 320: return 1500;
                case 330: return 3500;
                case 340: return 1500;
                case 341: return 4000;
                case 360: return CraftMsOf(e as Crafted);   // 36행 제작(플레이 시험 T11 — 옛 1500, 볼은 더 짧게)
                case 370: return 1500;
                case 390: return EvolveMs;
                case MaintRow: return MaintMs;
                case 190: return 2400;
                case 200: return SearchMs.Value;
                case 211: return 2500;
                case 212: return 600;
                case 220:
                {
                    var k = ((BattleStarted)e).Kind;
                    return k == BattleKind.Wild || k == BattleKind.Swarm || k == BattleKind.Trainer ? 2000 : 2500; // 전설·VS 2500
                }
                case 250: return 700;
                case 460: return 1800;
                case 470: return StampMs(Len(((StampGained)e).Changes), Len(((StampGained)e).MoveKeys));   // 47행 [가정 plan §5 #125] 1500~2500
                case 510: return 2800;
                case 520: return 2500;                  // 52행 거래 [가정 plan §5 #126] 2000~3000
                case 530: return 3500;
                case 540: return ((OrderDecided)e).TokenUsed ? 1800 : 1200;
                case 560: return DiceThrowMs.Value + DiceRollMaxMs.Value + DiceCountGapMs.Value + DiceCountPerMs.Value * Len(((DiceRolled)e).Faces);
                case 570: return Len(((DiceFacesChanged)e).Changes) > 0 ? FaceFlipMs.Value : 0;   // 바꿀 면이 없었으면(0 결과) 넘기지 않는다
                case 590: return MoveHitMs;
                case 600: return ((TypeMatchup)e).Delta == 0 ? 900 : 1600;  // 효과 문장(700)은 상성이 있을 때만
                case 611: return DamageCalcMs.Value + DamageFlyMs.Value;
                case 612: return HpDrainMs.Value;
                case 620: return 1200;
                case 630: return 900;
                case 641: return e is FatigueChanged bf && bf.Cause == FatigueCause.BeastBoost ? 1200 : 300;   // 비스트 부스트(76행): 붉은 오라가 터지고 피로 3칸이 빠진다
                // M5 「맵 결과·배틀 안 연출」(04 §8-8 시간 열 안에서 고름 [가정])
                case 260: return 1500;
                case 270: return 1000;
                case 280: { var st = (SpotResolved)e; return 1800 + (st.Count > 0 && st.Count % 4 == 0 ? 800 : 0); }   // 4·8개째면 띠가 한 번 빛난다
                case 290: return ((SpotResolved)e).Result == SpotResult.AlphaWarded ? 1000 : 2400;
                case 300: return 1800;
                case 461: return 1500;
                case 780: return 3200;
                case 790: return 1000;
                case 800: return 1800;
                case 642: return 900;
                case 650: return 1800;
                case 660: return 2200;
                case 670: return 1800;
                case 680:
                {
                    var c = (CaptureAttempt)e;
                    return CaptureMs(c.Success, c.Shakes);
                }
                case 690: return 1200;
                case 700: return Math.Min(6000, 3000 + 500 * Len(((BattleEnded)e).Changes));
                case 710: return 2000;
                case 720: return 3500 + 200 * Math.Max(0, ((GalacticBattle)e).LineupCount);
                case 730: return 1000;
                case 741: return 600;
                case 742: return 1200;
                case 750: return 400;
                case 940: return 3000;
                case 941: return 1500;   // 94행 1막: 고향의 조무래기 토큰이 날아가고 「이동력 +2」 칩(보드 — 결과 창 [확인] 뒤, §8-5 「공개 없는 queue 항목」)
                case 943: return 2500;
                case 950: return 2800;
                case 960: return 1500;
                case 970: return 8000;
                case 990: return 2000;
                // 운명의 변화(M5 「운명·응답 화면」 — 04 7·14·42~45행 시간 열 안에서 고름 [가정 plan §5 #98])
                case 70: return Math.Min(2000, 800 + 150 * Len(((FateDealt)e).PerSeat));
                case 420: return 600;
                case 421: return 600;
                case 450: return ((FatePlayed)e).Play3 ? 1200 : 600;
                case 140: return 1800;
                // 울트라 버스트 보드(M6 「UB 보드 화면」 — 04 15·82·83행 시간 열 안에서 고름 [가정 plan §5 #156])
                case 150: return 1600;
                case 820: return 700;                   // 플레이 시험 T24: 옛 400 — 누가 어디로 가는지 읽히게
                case 830: return 1500;                  // 플레이 시험 T24: 옛 1000 — 무엇을 부쉈는지 읽히게
                // M6 「UB 화면 묶음 1」(04 82·84·85·86행 시간 열 안에서 고름 [가정 plan §5 #158])
                case 821: return 1600;                  // 플레이 시험 T24: 옛 1200
                case 840: return Len(((NecrozmaStrike)e).Cells) > 0 ? 1500 : 0;   // 칸마다 400은 그 칸의 파괴 단계(830)가 맡는다
                case 850: return e is BreachCardsDealt bd ? Math.Min(2500, 1500 + 150 * Len(bd.PerSeat)) : 1500;
                case 860: return 4500;
                // M6 「UB 배틀 화면 묶음」(04 88~92행 시간 열 안에서 고름 [가정 plan §5 #159])
                case 880: return 600;
                case 881: return 1000;
                case 890: return 1000;
                case 900: return 2500;
                case 901: return 1500;
                case 910: return 1500;
                case 920: return ((TrialProgress)e).Done ? 1500 : 600;
                case 921: return 1500;
                // M6 「연출 목록 채우기」(04 83·93행 시간 열 안에서 고름 [가정 plan §5 #163])
                case 831: return 1300;                  // 플레이 시험 T24: 옛 1000
                case 930: return 1400;
                // 트레이너 카드(M6 「트레이너 카드 해금 연출」 — 04 35·48·49·50행 시간 열 안에서 고름 [가정 #162 ①])
                case 350: return WeatherMs;
                case 480: return UnlockMsOf((CardUnlocked)e);
                case 490: return SwapMs;
                case 500: return AbilityMs;
                case 430: return RerollMs(Len(((DiceFacesChanged)e).Changes));
                case 440: return 1500;
                default: return 0;
            }
        }

        /// <summary>
        /// 59행 기술 사용 = 맞는 순간만(결정 143 ⑦ 배틀 템포 [가정 plan §5 #770]): 돌진 150 + 제자리 150. 당김과 「○○의 △△!」는 준비 동작으로 굴림(56행) 단계 안에 함께 돈다(화면 <c>PrepBeats</c>) —
        /// 옛 값 1400(당김 · 돌진 · 제자리 150씩 + 문장 읽기)은 주사위가 다 선 뒤에야 공격 모습이 나와 템포가 느렸다. 방장 · 관전 · 웹 손님이 모두 이 값을 쓴다.
        /// </summary>
        public const int MoveHitMs = 300;

        /// <summary>36행 제작 길이(플레이 시험 T11 [가정 plan §5 #622]): 볼(같은 것을 여러 번 만든다) · 그 밖. 옛 값은 둘 다 1500.</summary>
        public const int CraftMs = 1000, BallCraftMs = 700;

        /// <summary>그 제작의 길이: 볼(Changes에 볼 자원이 늘었다)이면 <see cref="BallCraftMs"/>, 아니면 <see cref="CraftMs"/>.</summary>
        public static int CraftMsOf(Crafted c)
        {
            if (c != null && c.Changes != null)
                foreach (var ch in c.Changes)
                    if (ch != null && ch.Res == ResKind.Ball && ch.After > ch.Before) return BallCraftMs;
            return CraftMs;
        }

        /// <summary>47행 여행 스탬프 길이: 도장 1500 + 보상 줄(규토리 색 수 · 기술 카드 묶음 하나)마다 250, 상한 2500(04 §8-8 「1500~2500」).</summary>
        public static int StampMs(int changes, int moves) => Math.Min(2500, 1500 + 250 * (Math.Max(0, changes) + (moves > 0 ? 1 : 0)));

        /// <summary>39행 진화 길이: 본인(진화 곡에 맞춤 — 결정 84 L11 실루엣 8번으로 줄임, 옛 값 6000) · 남 좌석(04 §8-8 39행 「본인 4800·남 2000」).</summary>
        public const int EvolveMs = 4800, EvolveOthersMs = 2000;

        /// <summary>16행 공중날기 길이: 앞의 비전기술 컷인(결정 167 — Pt 비전기술 컷인의 가로 띠, 1.5초 안쪽 [가정]) + 찌르호크 택시 3200(옛 길이 그대로).</summary>
        public const int FlyCutinMs = 1400, FlyTaxiMs = 3200, FlyMs = FlyCutinMs + FlyTaxiMs;

        /// <summary>
        /// 트레이너 카드 연출 길이(04 §8-8 35·48·49·50행 — M6 「트레이너 카드 해금 연출」 [가정 #162 ①]): 해금 한 박자 · 바로 골드면 한 박자 더(자물쇠 둘) ·
        /// 카드 바꾸기 · 능력 사용(출처 카드 통통) · 날씨 휠. 남 좌석도 같은 길이다 — 결정 120 ①로 남의 해금 · 바꾸기도 큰 덮개로 뒤집기 · 착지까지 보인다(옛 남 1200은 뒤집히기 전에 끝났다, plan §5 #708).
        /// </summary>
        public const int UnlockMs = 3200, UnlockStepMs = 800, SwapMs = 2000, AbilityMs = 600, WeatherMs = 1800;

        /// <summary>48행 해금 길이: 열린 능력 칸마다 자물쇠 한 박자(브론즈 → 골드면 둘 — 04 「골드 두 박자 +800」).</summary>
        public static int UnlockMsOf(CardUnlocked e) => UnlockMs + UnlockStepMs * (Math.Max(1, Math.Min(2, e != null ? Len(e.AbilitySlots) : 1)) - 1);

        /// <summary>정비 보기 행(04 §8-8 38·40·41행을 한 단계로 — 남 좌석의 정비 판이 바뀌는 모습, 결정 120 · 플레이 시험 [가정 plan §5 #672]): 남 화면 길이 · 내 화면은 0.</summary>
        public const int MaintRow = 380, MaintMs = 1500;

        /// <summary>같은 턴의 정비는 묶어서 한 번(결정 120): 정비 행이고 길이가 있으면 그 좌석이 이번 턴에 처음일 때만 그 길이, 아니면 0. seen = 이번 턴에 받은 좌석 비트(방장 · 화면이 같은 꼴로 든다).</summary>
        /// <summary>정비 보기 행에 실리는 사당의 그다음 고르기 결과(동등한 교환 · 뜻밖의 선물의 규토리 — 결정 120): 정비가 아니라 묶지 않는다.</summary>
        public static bool IsShrineGift(GameEvent e) => e is PokemonExchanged || (e is ApricornsGained ag && ag.Source.Kind == SourceKind.ShrineEvent);

        public static int Bundled(int row, GameEvent e, int ms, ref int seen)
        {
            if (row != MaintRow || ms <= 0 || e == null || e.Seat < 0 || e.Seat >= 30 || IsShrineGift(e)) return ms;
            int bit = 1 << e.Seat;
            if ((seen & bit) != 0) return 0;
            seen |= bit;
            return ms;
        }

        /// <summary>그 좌석의 화면에서 그 행의 길이: <see cref="RowMs"/>와 같고 진화(39행)는 남 좌석이면 짧다(해금 48 · 바꾸기 49는 남도 같다 — #708). 방장(<see cref="Items"/>)과 화면 대기열(FxPlanner)이 같이 쓴다.</summary>
        public static int RowMsFor(GameEvent e, int row, int viewer)
        {
            if (row == MaintRow && e != null && e.Seat == viewer) return 0;   // 내 정비는 내 판에서 이미 보인다
            if (e == null || e.Seat == viewer) return RowMs(e, row);
            if (row == 390) return EvolveOthersMs;
            return RowMs(e, row);
        }

        /// <summary>공개 장수(보기 시간 「장」 = 공개된 카드·주사위 수).</summary>
        static int RevealsOf(GameEvent e)
        {
            switch (e)
            {
                case EncounterDrawn d: return Len(d.Cards);
                case DiceRolled d: return Len(d.Faces);
                case CaptureAttempt c: return Len(c.Faces);
                case TrainerCardsRevealed t: return Len(t.Picks);
                case SecretRevealed r: return Len(r.Answers);
                default: return 0;
            }
        }

        static int Len(Array a) => a == null ? 0 : a.Length;

        /// <summary>야생 취급 배틀(04 §8-2): 당사자 좌석의 대기열에만 들어간다. 트레이너·갤럭시단·배신자 배틀은 모두에게.</summary>
        public static bool IsWildKind(BattleKind k) => k != BattleKind.Trainer && k != BattleKind.Galactic && k != BattleKind.Traitor;

        /// <summary>
        /// 그 좌석에 걸러진 이벤트 열(갱신 하나)을 항목(Seq)으로 나눠 길이를 센다(04 §8-5 규칙 1~5). ctx는 배틀 맥락이라 흘려 보내며 바꾼다.
        /// 차례 넘김(101행): 같은 갱신에서 TurnEnded 뒤 TurnStarted(추가 턴 아님)면 요약 + 띠, 차례 좌석에는 그 뒤에 내 차례 컷신(결정 53 ① — 게임 첫 턴은 컷신만).
        /// </summary>
        public static List<FxItem> Items(IReadOnlyList<GameEvent> events, PlayTimingContext ctx)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var items = new List<FxItem>();
            var rowMs = new int[RowSlots];
            var touched = new List<int>();
            int seq = int.MinValue, reveals = 0, blockMs = 0, extraQueue = 0;
            bool open = false, result = false, sawTurnEnd = false, battleStart = false;
            ActionKind kind = ActionKind.Setup;

            void Close()
            {
                if (!open) return;
                int sum = extraQueue, stepSum = 0;
                bool queue = extraQueue > 0;
                // 배틀 인트로(53행): 배틀을 연 항목·첫 출전 고르기·우선권 토큰이 놓인 항목의 출전은 66행이 아니라 53행이 센다
                if (touched.Contains(660) && (touched.Contains(530) || battleStart || kind == ActionKind.SendIn))
                {
                    if (!touched.Contains(530)) touched.Add(530);
                    rowMs[530] = RowMs(null, 530);
                    rowMs[660] = 0;
                }
                foreach (int r in touched)
                {
                    int ms = rowMs[r];
                    var block = BlockOf(r);
                    if (block == FxBlock.Step) stepSum += ms;
                    else
                    {
                        sum += ms;
                        queue = true;
                    }
                }
                int len;
                if (queue)
                {
                    int view = result ? Math.Min(RevealCapMs, RevealBaseMs + RevealPerMs * reveals) : PlainMs;
                    len = Math.Max(sum + stepSum, view);
                }
                else len = stepSum;
                if (len > 0 || blockMs > 0) items.Add(new FxItem { Seq = seq, QueueMs = len, StepsMs = sum + stepSum, BlockMs = blockMs });
                foreach (int r in touched) rowMs[r] = 0;
                touched.Clear();
                open = false;
                reveals = 0;
                blockMs = 0;
                extraQueue = 0;
                result = false;
                battleStart = false;
            }

            foreach (var e in events)
            {
                if (e == null) continue;
                if (e is ActionStarted a)
                {
                    Close();
                    open = true;
                    seq = e.Seq;
                    kind = a.Kind;
                    continue;
                }
                if (!open)
                {
                    open = true;
                    seq = e.Seq;
                }
                // 야생전은 당사자 좌석에만(04 §8-2): BattleStarted부터 BattleEnded까지 뺀다
                if (e is BattleStarted bs)
                {
                    bool party = false;
                    if (bs.Sides != null)
                        foreach (var side in bs.Sides)
                            if (side != null && side.Seat == ctx.Viewer) party = true;
                    ctx.SkippingBattle = IsWildKind(bs.Kind) && !party && !ctx.WatchWild;
                    if (ctx.SkippingBattle) continue;
                }
                if (ctx.SkippingBattle)
                {
                    if (e is BattleEnded) ctx.SkippingBattle = false;
                    continue;
                }
                if (e is BattleStarted || e is GalacticBattle) battleStart = true;
                if (IsTurnGain(e)) ctx.TurnGain = true;
                if (e is TurnEnded) sawTurnEnd = true;
                if (e is TurnStarted ts)
                {
                    if (!ts.IsExtra)
                    {
                        // 길이는 좌석 수에 따라(결정 57): 한 좌석 판은 띠·컷신이 짧고 요약은 그 턴에 변화가 있었을 때만
                        if (sawTurnEnd) extraQueue += (ShowsTurnSummary(ctx.Seats, ctx.TurnGain) ? TurnSummaryMs.Value : 0) + HandoffFor(ctx.Seats);
                        if (ts.Seat == ctx.Viewer) extraQueue += MyTurnFor(ctx.Seats); // 차례 좌석만: 띠 뒤에 내 차례 컷신(게임 첫 턴은 요약·띠 없이 컷신만)
                        sawTurnEnd = false;
                    }
                    ctx.TurnGain = false;
                    ctx.MaintSeen = 0;
                }

                int row = RowOf(e);
                if (row == Unknown) throw new InvalidOperationException($"PlayTiming: {e.GetType().Name}의 행이 없다 — 04 §8-8 행과 표를 먼저 정한다");
                if (row == 0) continue;
                int msRow = Bundled(row, e, RowMsFor(e, row, ctx.Viewer), ref ctx.MaintSeen);
                if (msRow == 0 && row == MaintRow) continue;
                var blk = BlockOf(row);
                if (blk == FxBlock.UntilEndAll || blk == FxBlock.UntilEndParty)
                {
                    if (blk == FxBlock.UntilEndAll || InParty(e, ctx.Viewer)) blockMs += msRow;
                    else if (OthersQueue(e)) extraQueue += msRow;
                    continue;
                }
                if (IsResult(row)) result = true;
                reveals += RevealsOf(e);
                if (!touched.Contains(row)) touched.Add(row);
                rowMs[row] = Sums(row) ? rowMs[row] + msRow : Math.Max(rowMs[row], msRow);
            }
            Close();
            return items;
        }

        /// <summary>차단 컷신의 당사자(02 §5-7): 배틀 = 양쪽 좌석, 갤럭시단 배틀 = 건 좌석, 도전 = 건 좌석과 받은 좌석.</summary>
        static bool InParty(GameEvent e, int viewer)
        {
            switch (e)
            {
                case BattleStarted b:
                    if (b.Sides != null)
                        foreach (var side in b.Sides)
                            if (side != null && side.Seat == viewer) return true;
                    return false;
                case ChallengeIssued c: return c.From == viewer || c.To == viewer;
                default: return e.Seat == viewer;
            }
        }

        /// <summary>범위 밖 좌석이 queue로 받는 차단 컷신(트레이너·갤럭시단 배틀 들어가기, 도전, 갤럭시단 라인업). 야생 취급 배틀은 항목 없음.</summary>
        static bool OthersQueue(GameEvent e) => !(e is BattleStarted b) || !IsWildKind(b.Kind);

        /// <summary>좌석 하나의 갱신 길이 = 대기열 항목 합 + 차단 컷신 합.</summary>
        public static int SeatMs(IReadOnlyList<GameEvent> events, PlayTimingContext ctx)
        {
            int ms = 0;
            foreach (var it in Items(events, ctx)) ms += it.QueueMs + it.BlockMs;
            return ms;
        }

        /// <summary>
        /// 방장 멈춤에 더할 길이(02 §5-7): 호스트 이벤트를 좌석마다 거른(Engine.FilterFor) 열의 길이 중 가장 긴 것. perSeat에 좌석별 길이를 채운다.
        /// ctxs = 좌석마다 하나(좌석 수와 같은 길이). holdPerSeat에 좌석마다 그 갱신 처음부터 그 좌석이 범위 안인 마지막 차단 컷신 항목의 끝까지 길이(없으면 0)를 채운다
        /// — 방이 그동안 그 좌석의 답을 E_HOLD로 거절한다(02 §5-7 차단 컷신, M7 「세션 ③」).
        /// </summary>
        public static int UpdateMs(IReadOnlyList<GameEvent> hostEvents, PlayTimingContext[] ctxs, int[] perSeat = null, int[] holdPerSeat = null)
        {
            if (hostEvents == null) throw new ArgumentNullException(nameof(hostEvents));
            if (ctxs == null) throw new ArgumentNullException(nameof(ctxs));
            int max = 0;
            var filtered = new List<GameEvent>(hostEvents.Count);
            for (int seat = 0; seat < ctxs.Length; seat++)
            {
                filtered.Clear();
                foreach (var e in hostEvents)
                {
                    var f = Engine.FilterFor(e, seat);
                    if (f != null) filtered.Add(f);
                }
                int ms = 0, hold = 0;
                foreach (var it in Items(filtered, ctxs[seat]))
                {
                    ms += it.QueueMs + it.BlockMs;
                    if (it.BlockMs > 0) hold = ms;
                }
                if (perSeat != null) perSeat[seat] = ms;
                if (holdPerSeat != null) holdPerSeat[seat] = hold;
                if (ms > max) max = ms;
            }
            return max;
        }

        /// <summary>방장 멈춤 끝(02 §5-7): max(지금, 앞 멈춤 끝) + 이 갱신의 길이. 앞 끝에 잇는다(밀린 항목을 덮지 않는다).</summary>
        public static long NextPauseEnd(long nowMs, long prevEndMs, int updateMs) => Math.Max(nowMs, prevEndMs) + updateMs;
    }
}
