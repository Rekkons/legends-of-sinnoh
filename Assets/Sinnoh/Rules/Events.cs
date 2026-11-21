using System.Collections.Generic;

namespace Sinnoh.Rules
{
    // 이벤트(01 §3-6 연출 계약, §3-7 타입 표 — 정본). 화면은 이벤트에서 행동 종류를 추론하지 않는다. 연출에 필요한 값은 여기 싣는다.
    // 필드는 미설정 표지(int.MinValue·null)로 시작한다 [가정]. 0·−1은 값이다. 키 필드(…Key·Species)는 그 표의 int 색인이다.
    // 이 파일은 낼 M이 3인 타입을 모두 둔다. 내는 코드는 그 행동을 만드는 작업이 쓴다(04 §8-8, 시험 FxContract).
    // 타입·필드를 바꾸면 01 §3-7을 먼저 고치고 시험 코드 표 FxContract도 맞춘다.

    /// <summary>ActionStarted.Auto(01 §3-6 ①).</summary>
    public enum AutoKind { Human, Auto, Ai, Timeout }

    /// <summary>
    /// ActionStarted.Kind(01 §3-6 ①): 답한 선택의 ChoiceKind(MapAction이면 답 종류) / 정비 명령 종류 / 자동 단계.
    /// </summary>
    public enum ActionKind
    {
        // 선택(MapAction 말고)
        VillagePick, StarterPick, TrainerCardPick, HiddenSpot, EncounterPick, Lineup, SendIn, NextPokemon, SwitchWindow, BattleAction,
        Reaction, Respond, Simultaneous, Discard, Reward, Ending,
        // MapAction 답(01 §3-2)
        Move, Search, TakeApricorns, Offer, VillageHeal, Ginkgo, Fly, Craft, Evolve, PlayFate, UseAbility, Challenge, ProposeTrade, EndTurn,
        // 정비(01 §3-4)
        PartyOrder, SetMove, Release, HoldItem,
        // 자동 단계. Setup = 셋업 배분(트레이너 카드 3장)·게임 시작(남은 스타터 → 약한 덱, 숨겨진 장소 배치 R3)(2026-11-07 M3-d)
        TurnStart, TurnEnd, RoundStart, UbTurn, ActReveal, Prologue, Setup,
        // 정비: 아르세우스의 축복 쓰기(울트라 버스트, M6 — 저장된 값이 밀리지 않게 맨 끝)
        Blessing,
    }

    public enum TurnEndReason { EndTurn, Battle, Apricorns }

    /// <summary>ResChange.Res(01 §3-7). Key = 색(규토리)·볼 등급·아이템 색인 …, 장수·개수뿐이면 −1.</summary>
    public enum ResKind { Apricorn, Jp, Ball, Fate, Item, Stew, Relic }

    public enum StatusResult { Applied, Immune, Already }

    /// <summary>피해 단계(01 §6-4 ①~⑦). Retribution = 은하의 응징으로 옮긴 고정 피해(G5).</summary>
    public enum DamageStepKind { Dice, Effect, Effectiveness, Double, Reduction, Threshold, Cap, Retribution }

    /// <summary>
    /// BattleEnded.Result: 어떻게 끝났나. Win = 진 쪽의 쓸 포켓몬이 바닥남(야생 기절 포함), Captured = 포획, Fled = 야생이 도망,
    /// Forfeited = 기권, Draw = 둘 다 바닥남(트레이너전 무승부, 1막 마지막 동시 기절 rulings G13). Loss는 쓰지 않는다(이긴 쪽은 Winner가 말한다).
    /// </summary>
    public enum BattleResult { Win, Loss, Captured, Fled, Forfeited, Draw }

    /// <summary>BattleStarted.BgmKey: 배틀 상황 키(BGM 표는 M9 bgm.json이 이 키로 찾는다). 2026-11-07 M3-e.</summary>
    public enum BattleBgm { Wild, Trainer, GalacticGrunt, GalacticCommander, GalacticBoss, Legendary, God, UltraBeast, Necrozma }

    public enum SwitchReason { Switch, Fainted, Forced, BattleEnd }

    /// <summary>SwitchLocked.Reason. Frozen = 빙결은 교체·도망 불가(S7_6, rulings G14, M3-f).</summary>
    public enum SwitchLockReason { GalacticNoSwitchAfterKo, Frozen, BlastCrystal, NoEscape, MoveSwitchBlocked }

    /// <summary>FatigueChanged.Cause. Effect = 기술 효과가 올림(Source = 그 기술, M3-f).</summary>
    public enum FatigueCause { Roll, Capture, Switch, BeastBoost, Heal, Effect }

    public enum GalacticLeader { Grunt, Commander, Cyrus }

    /// <summary>MoveUsed.Category: 연출 갈래(물리 = 돌진, 특수 = 발사체, 변화 = 오라, 04 59행).</summary>
    public enum MoveCategory { Physical, Special, Status }

    public enum RetributionCause { Fainted, PlanB }

    /// <summary>굴림 종류: 공격 굴림 · 포획 굴림(굴린 뒤 창이 열릴 때만 판정 앞에 먼저 드러낸다 — 결정 188, 판정은 CaptureAttempt).</summary>
    public enum DiceKind { Attack, Capture }

    /// <summary>이벤트를 받는 곳(01 §3-1). Submit의 sink가 null이면 이벤트 객체를 만들지 않는다(AI 탐색).</summary>
    public interface IEventSink
    {
        void Add(GameEvent e);
    }

    /// <summary>
    /// 이벤트 없이 배틀 끝만 받는 곳(01 §9-3 「이벤트 없이」, §13 M8-3 — AI 굴리기). Submit의 sink로 주면 이벤트를 만들지 않고(Emitter.On = false — sink가 null일 때와 같은 길)
    /// BattleEnded를 낼 자리(배틀을 닫기 직전 — 귀환·회복 전, s.Battle이 아직 있다)에서 <see cref="BattleOver"/>만 부른다. <see cref="IEventSink.Add"/>는 불리지 않는다.
    /// </summary>
    public interface IBattleEndWatch : IEventSink
    {
        void BattleOver(GameState s, BattleState b, BattleResult result, int winner);
    }

    /// <summary>받은 이벤트를 순서대로 모은다(시험·세션).</summary>
    public sealed class EventLog : IEventSink
    {
        public readonly List<GameEvent> Events = new List<GameEvent>();

        public void Add(GameEvent e) => Events.Add(e);

        public void Clear() => Events.Clear();

        public List<T> OfType<T>() where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in Events)
                if (e is T t) r.Add(t);
            return r;
        }
    }

    /// <summary>
    /// 이벤트 공통 머리(01 §3-6 ②): Seq(행동), Index(행동 안 순서), Seat(행위자: 0~5 / System −1 / Galactic −2 / UltraBeast −3 / Wild −4),
    /// Source·ParentSource(연쇄 ⑦), StepIdx(이동 중 효과, 아니면 −1). Seq·Index·Seat·ParentSource는 Emitter가 채운다.
    /// </summary>
    public abstract class GameEvent
    {
        /// <summary>미설정 표지(01 §3-6 시험 「채움」).</summary>
        public const int U = int.MinValue;

        public int Seq = U;
        public int Index = U;
        public int Seat = U;
        public SourceRef Source = SourceRef.Unset;
        public SourceRef ParentSource = SourceRef.Unset;
        public int StepIdx = -1;

        /// <summary>
        /// 좌석 viewer에게 보낼 모양(01 §3-6 ⑩, Engine.FilterFor). 숨은 필드가 있는 타입만 덮어쓴다: 사본에서 그 필드를 null로 바꾸고 개수는 남긴다.
        /// 개수·종류는 숨은 정보에 따라 달라지지 않는다.
        /// </summary>
        public virtual GameEvent ForViewer(int viewer) => this;

        protected T Copy<T>() where T : GameEvent => (T)MemberwiseClone();
    }

    // ── 공용 원소 ──

    public sealed class ResChange
    {
        public int Seat = GameEvent.U;
        public ResKind Res = (ResKind)GameEvent.U;
        public int Key = GameEvent.U;
        public int Before = GameEvent.U;
        public int After = GameEvent.U;
    }

    public sealed class Mod
    {
        public SourceRef Source = SourceRef.Unset;
        public int Delta = GameEvent.U;
    }

    public sealed class SeatAnswer
    {
        public int Seat = GameEvent.U;
        public ChoiceAnswer Value;
    }

    public sealed class SeatCard
    {
        public int Seat = GameEvent.U;
        public int CardKey = GameEvent.U;
    }

    public sealed class PileCount
    {
        public DeckId Pile = (DeckId)GameEvent.U;
        public int Count = GameEvent.U;
    }

    public sealed class GradedCard
    {
        public EncounterTier Grade = (EncounterTier)GameEvent.U;
        public int CardKey = GameEvent.U;
    }

    public sealed class HealEntry
    {
        public int Id = GameEvent.U;
        public int HpBefore = GameEvent.U;
        public int HpAfter = GameEvent.U;
        public StatusEffect StatusBefore = (StatusEffect)GameEvent.U;
    }

    public sealed class BattleSideInfo
    {
        public int Owner = GameEvent.U;
        public int Seat = GameEvent.U;
    }

    public sealed class OrderSide
    {
        public int Base = GameEvent.U;
        public Mod[] Mods;
        public int Final = GameEvent.U;
    }

    public sealed class FaceChange
    {
        public int Index = GameEvent.U;
        public AttackDieFace Before = (AttackDieFace)GameEvent.U;
        public AttackDieFace After = (AttackDieFace)GameEvent.U;
    }

    public sealed class DamageStep
    {
        public DamageStepKind Kind = (DamageStepKind)GameEvent.U;
        public int Value = GameEvent.U;
        public SourceRef Source = SourceRef.Unset;
    }

    // ── 선택·행동 ──

    public sealed class ActionStarted : GameEvent
    {
        public ActionKind Kind = (ActionKind)U;
        public AutoKind Auto = (AutoKind)U;
        public int Continues = U;
        public int ReactsTo = U;
    }

    /// <summary>선택이 열림. Seat = 응답자. Legal은 그 좌석에만(가상 응답자 선택은 모두에게).</summary>
    public sealed class ChoiceRequested : GameEvent
    {
        public int Id = U;
        public ChoiceKind Kind = (ChoiceKind)U;
        public ChoiceSub Sub = (ChoiceSub)U;
        public int GroupId = U;
        public ClockKind Clock = (ClockKind)U;
        public Window Window = (Window)U;
        public bool HandGated;
        public int Count = U;
        public List<ChoiceAnswer> Legal;

        public override GameEvent ForViewer(int viewer)
        {
            if (Seat < 0 || viewer == Seat || Legal == null) return this;
            var c = Copy<ChoiceRequested>();
            c.Legal = null;
            return c;
        }
    }

    /// <summary>공개 답(비공개 묶음의 답은 SecretAnswered → SecretRevealed). Reason은 AI 사유(표시 전용).</summary>
    public sealed class ChoiceAnswered : GameEvent
    {
        public int Id = U;
        public ChoiceAnswer Answer;
        public bool ByTimeout;
        public bool ByAi;
        public AiReason Reason;

        /// <summary>거래 답의 운명 카드(rulings A29): 거래 제안(ProposeTrade)은 두 당사자만, 받아들임의 고른 카드(Accept + Values)는 답한 좌석만 Values를 본다(개수는 남긴다).</summary>
        public override GameEvent ForViewer(int viewer)
        {
            if (Answer == null || Answer.Values.Count == 0 || viewer == Seat) return this;
            if (!(Answer.Kind == AnswerKind.ProposeTrade && viewer != Answer.Target) && Answer.Kind != AnswerKind.Accept) return this;
            var c = Copy<ChoiceAnswered>();
            c.Answer = Answer.Clone();
            for (int i = 0; i < c.Answer.Values.Count; i++) c.Answer.Values[i] = Engine.Unknown;
            return c;
        }
    }

    public sealed class ChoiceReissued : GameEvent
    {
        public int OldId = U;
        public int NewId = U;
    }

    /// <summary>공개 정보로 쓸 것이 없어 건너뛴 창(01 §3-6 ⑥, 표시 전용).</summary>
    public sealed class WindowSkipped : GameEvent
    {
        public Window Window = (Window)U;
    }

    /// <summary>
    /// 비공개 묶음에서 한 좌석이 답함(답 없음). Window = 반응 창 묶음이면 그 창(창의 일부라 연출이 없다, 04 58행), 동시 선택(셋업 고르기·천상의 균열 …)이면 None(04 1·99행).
    /// </summary>
    public sealed class SecretAnswered : GameEvent
    {
        public int Group = U;
        public bool ByTimeout;
        public Window Window = (Window)U;
    }

    /// <summary>비공개 묶음이 다 답한 뒤 한꺼번에 공개. Answers는 적용 순서다. Window는 SecretAnswered와 같다(반응 창이면 연출 없음 — 쓴 반응은 자기 Seq의 결과 이벤트가 연출을 맡는다).</summary>
    public sealed class SecretRevealed : GameEvent
    {
        public int Group = U;
        public SeatAnswer[] Answers;
        public Window Window = (Window)U;
    }

    // ── 셋업 ──

    public sealed class TrainerCardsDealt : GameEvent
    {
        public int Count = U;
        /// <summary>본인만. 남에게는 null(Count는 남는다).</summary>
        public int[] CardKeys;

        public override GameEvent ForViewer(int viewer)
        {
            if (viewer == Seat || CardKeys == null) return this;
            var c = Copy<TrainerCardsDealt>();
            c.CardKeys = null;
            return c;
        }
    }

    public sealed class TrainerCardsRevealed : GameEvent
    {
        public SeatCard[] Picks;
        public int ReturnedCount = U;
    }

    public sealed class VillagePicked : GameEvent
    {
        public int VillageKey = U;
        public int Cell = U;
        public int SeatColor = U;
    }

    public sealed class StarterPicked : GameEvent
    {
        public int Species = U;
        public int PokemonId = U;
        public int PartySlot = U;
    }

    public sealed class TokensPlaced : GameEvent
    {
        public TokenKind Kind = (TokenKind)U;
        public int[] Cells;
        public int[] Villages;
    }

    public sealed class FlyBlocked : GameEvent
    {
        public int[] Villages;
    }

    // ── 턴 ──

    public sealed class RoundStarted : GameEvent
    {
        public int Round = U;
    }

    public sealed class TurnStarted : GameEvent
    {
        public int TurnId = U;
        public int Round = U;
        public bool IsExtra;
    }

    public sealed class TurnEnded : GameEvent
    {
        public int TurnId = U;
        public TurnEndReason Reason = (TurnEndReason)U;
    }

    public sealed class MoveAllowanceSet : GameEvent
    {
        public int Base = U;
        public Mod[] Mods;
        public int Total = U;
    }

    public sealed class ProtectionSet : GameEvent
    {
        public int[] Seats;
    }

    public sealed class ProtectionCleared : GameEvent { }

    // ── 이동 ──

    /// <summary>걷기. Path·Levels는 출발 칸부터 도착 칸까지(걸음 수 + 1개), Levels = 칸 level × 4(오르막 연출). StopAt = 공개를 물을 숨겨진 장소 칸(옛 꼴은 도착 칸, 결정 71 고른 길은 멈춘 칸 = Path 마지막) / −1. 고른 길에서 지나가기 뒤 남은 길은 다음 Moved(그 답의 Seq)다.</summary>
    public sealed class Moved : GameEvent
    {
        public int[] Path;
        public int[] Levels;
        public int AllowanceBefore = U;
        public int AllowanceAfter = U;
        public int StopAt = U;
    }

    public sealed class Flew : GameEvent
    {
        public int From = U;
        public int To = U;
    }

    public sealed class Whiteout : GameEvent
    {
        public int FromCell = U;
        public int ToCell = U;
    }

    public sealed class RespawnPending : GameEvent { }

    public sealed class RespawnCleared : GameEvent { }

    // ── 자원·토큰 ──

    public sealed class ApricornsTaken : GameEvent
    {
        public int Cell = U;
        public int Color = U;
        public int Grade = U;
        public int Count = U;
        public int Before = U;
        public int After = U;
    }

    public sealed class ApricornSpent : GameEvent
    {
        public ResChange[] Changes;
    }

    /// <summary>규토리를 얻음(출처 = Source). M3에서는 1막 공개의 빨강 3개(Source = Act, rulings G12). 04 26행(숨겨진 장소 규토리 더미)은 M5.</summary>
    public sealed class ApricornsGained : GameEvent
    {
        public ResChange[] Changes;
    }

    public sealed class UsageMarked : GameEvent
    {
        public UsageKind Kind = (UsageKind)U;
    }

    /// <summary>
    /// 제작(S10, p.14 — CraftRules). ItemKey = 만든 아이템, Changes = 낸 규토리(색마다) → 얻은 것(볼 = Res Ball·Key 볼 등급, 지닌물건 = Res Item·Key 아이템 — 가방 속 개수).
    /// 스튜는 얻은 것 없이 바로 <see cref="StewEaten"/>이 따른다.
    /// </summary>
    public sealed class Crafted : GameEvent
    {
        public int ItemKey = U;
        public ResChange[] Changes;
    }

    /// <summary>
    /// 스튜 효과(S10 — 만들자마자). 회복스튜는 뒤따르는 Healed(Source = 그 스튜)가 값을 싣는다. Moves = 더한 이동력(자극적인 스튜 3, 그 밖 0), MovesLeft = 그 뒤 남은 이동력.
    /// </summary>
    public sealed class StewEaten : GameEvent
    {
        public int ItemKey = U;
        public int Moves = U;
        public int MovesLeft = U;
    }

    /// <summary>
    /// 트레이너전 승리 보상(p.9, M5 「숨겨진 장소·유물·트레이너전 보상」): Seat = 이긴 좌석, From = 진 좌석. Changes = 유물(Res Relic, Key = 아이템 — 진 좌석 1 → 0, 이긴 좌석 0 → 1)
    /// 또는 운명(Res Fate, Key −1 — 두 좌석의 손패 장수, 카드는 싣지 않는다). 행은 「맵 결과·배틀 안 연출」·「운명·응답 화면」이 정한다.
    /// </summary>
    public sealed class SpoilsTaken : GameEvent
    {
        public int From = U;
        public ResChange[] Changes;
    }

    /// <summary>은행상회 거래 한 줄(A4). CardKey = 맨 위 거래 카드, Row = 줄(0 위 · 1 아래), Changes = 낸 규토리 → 받은 규토리(색마다). 두 줄을 다 하면 한 줄씩 둘이다.</summary>
    public sealed class GinkgoTraded : GameEvent
    {
        public int CardKey = U;
        public int Row = U;
        public ResChange[] Changes;
    }

    /// <summary>
    /// 진화(S9_1, A9, R14 — EvolveRules). From·To = 포켓몬 표 색인, JpCost = 낸 JP(우러난 스튜면 절반 — Halved), Changes = JP 하나, Pokemon = 완전 회복(그 포켓몬 하나 — 진화 전 체력 → 진화한 뒤 가득).
    /// </summary>
    public sealed class Evolved : GameEvent
    {
        public int PokemonId = U;
        public int From = U;
        public int To = U;
        public int JpCost = U;
        public bool Halved;
        public ResChange[] Changes;
        public HealEntry[] Pokemon;
    }

    /// <summary>소지 아이템 이동. PokemonId −1 = 가방으로, FromPokemonId −1 = 가방에서.</summary>
    public sealed class ItemHeld : GameEvent
    {
        public int PokemonId = U;
        public int ItemKey = U;
        public int FromPokemonId = U;
    }

    /// <summary>보드 토큰이 치워짐. M3: 1막 해방의 조무래기 토큰(rulings A1, 2026-11-07 M3-e). 사당·숨겨진 장소는 M5.</summary>
    public sealed class TokenRemoved : GameEvent
    {
        public int Cell = U;
        public TokenKind TokenKind = (TokenKind)U;
    }

    /// <summary>정비 파티 순서(2026-11-07 M3-c, 01 §3-7).</summary>
    public sealed class PartyReordered : GameEvent
    {
        public int[] Before;
        public int[] After;
    }

    // ── 카드 ──

    /// <summary>섞음. 새 순서는 싣지 않는다(호스트 원본 상태에만).</summary>
    public sealed class PileShuffled : GameEvent
    {
        public DeckId Pile = (DeckId)U;
    }

    public sealed class CardsReturned : GameEvent
    {
        public PileCount[] Returns;
    }

    // ── 찾아보기·조우 ──

    public sealed class Searched : GameEvent
    {
        public int Cell = U;
        public Biome Biome = (Biome)U;
        public Climate Climate = (Climate)U;
    }

    public sealed class EncounterDrawn : GameEvent
    {
        public GradedCard[] Cards;
    }

    /// <summary>고른 조우 카드. Position = 줄 안 자리(01 §3-7. 공통 머리 Index와 겹쳐 이름 바꿈).</summary>
    public sealed class EncounterPicked : GameEvent
    {
        public int CardKey = U;
        public EncounterTier Grade = (EncounterTier)U;
        public int Position = U;
    }

    /// <summary>SpotKey는 공개 뒤에만 싣는다. 길이는 정체와 무관(B2).</summary>
    public sealed class HiddenSpotRevealed : GameEvent
    {
        public int Cell = U;
        public int SpotKey = U;
    }

    public sealed class HiddenSpotPassed : GameEvent
    {
        public int Cell = U;
    }

    /// <summary>SpotResolved.Result — 공개한 숨겨진 장소의 결과(HiddenSpotRules, 04 §8-8 26~30행). AlphaWarded = 자극적인 스튜 등으로 배틀 없이 앞면으로 남은 우두머리.</summary>
    public enum SpotResult { Stash, Camp, Star, Alpha, Relic, AlphaWarded }

    /// <summary>
    /// 공개한 숨겨진 장소의 결과(M5 「맵 결과·배틀 안 연출」 — 04 §8-8 26 규토리 더미 · 27 야영지 · 28 떨어진 별 · 29 우두머리 · 30 유물). 공개(HiddenSpotRevealed) 바로 뒤,
    /// 결과가 바꾸는 값의 이벤트(ApricornsGained · FateDealt · TokenRemoved · BattleStarted)보다 먼저 온다 — 값은 이 행의 연출이 끝날 때 착지한다.
    /// Count = 규토리 개수(더미) · 모인 별 수(떨어진 별 — 이 별까지) · 그 밖 0. 앞면 우두머리를 나중에 찾아보기로 싸울 때도 Alpha로 한 번 온다.
    /// </summary>
    public sealed class SpotResolved : GameEvent
    {
        public int Cell = U;
        public int SpotKey = U;
        public SpotResult Result = (SpotResult)U;
        public int Count = U;
    }

    public sealed class ChallengeIssued : GameEvent
    {
        public int From = U;
        public int To = U;
        public int Cell = U;
        public bool Remote;
        public bool CanRefuse;
    }

    public sealed class ChallengeAnswered : GameEvent
    {
        public int To = U;
        public bool Accept;
        public bool ByTimeout;
    }

    // ── 포켓몬 ──

    public sealed class PokemonGained : GameEvent
    {
        public int PokemonId = U;
        public int Species = U;
        public int PartySlot = U;
        public int BallKey = U;
        public int Hp = U;
        public StatusEffect Status = (StatusEffect)U;
    }

    public sealed class Released : GameEvent
    {
        public int PokemonId = U;
        public int Species = U;
        public int[] MovesToDiscard;
    }

    public sealed class Healed : GameEvent
    {
        public HealEntry[] Pokemon;
    }

    /// <summary>체력 변화. 자해·피로 피해도 이것.</summary>
    public sealed class HpChanged : GameEvent
    {
        public int PokemonId = U;
        public int Before = U;
        public int After = U;
        public int Max = U;
    }

    public sealed class FormChanged : GameEvent
    {
        public int PokemonId = U;
        public int Before = U;
        public int After = U;
        public bool Temporary;
    }

    public sealed class MoveTaught : GameEvent
    {
        public int PokemonId = U;
        public int Slot = U;
        public int MoveKey = U;
        public int ReplacedKey = U;
    }

    public sealed class MoveForgotten : GameEvent
    {
        public int PokemonId = U;
        public int Slot = U;
        public int MoveKey = U;
    }

    public sealed class StatusApplied : GameEvent
    {
        public int PokemonId = U;
        public StatusEffect Status = (StatusEffect)U;
        public StatusResult Result = (StatusResult)U;
    }

    /// <summary>
    /// 상태이상 효과로 체력이 바뀜(독 2·혼란 3, rulings G14). 체력 변화는 이것이 싣는다 — 같은 피해로 HpChanged를 따로 내지 않는다.
    /// Effect = 체력 증감(피해는 음수). 피해 버퍼를 함께 적용하는 묶음이면 HpChanged와 같은 Index다(01 §3-6 ⑪). 마비·졸림의 면 바꾸기는 DiceFacesChanged(Source = Status).
    /// </summary>
    public sealed class StatusTicked : GameEvent
    {
        public int PokemonId = U;
        public StatusEffect Status = (StatusEffect)U;
        public int Effect = U;
        public int HpBefore = U;
        public int HpAfter = U;
    }

    public sealed class FatigueChanged : GameEvent
    {
        public int PokemonId = U;
        public int Before = U;
        public int After = U;
        public FatigueCause Cause = (FatigueCause)U;
    }

    public sealed class Fainted : GameEvent
    {
        public int PokemonId = U;
        public int Side = U;
        public int ById = U;
        public int PartyIndex = U;
    }

    // ── 배틀 ──

    public sealed class BattleStarted : GameEvent
    {
        public int BattleId = U;
        public BattleKind Kind = (BattleKind)U;
        public BattleSideInfo[] Sides;
        public int Cell = U;
        public Biome Biome = (Biome)U;
        public Climate Climate = (Climate)U;
        public int BgmKey = U;
        public bool Uncapturable;
        public bool FullHealed;
    }

    public sealed class GalacticBattle : GameEvent
    {
        public GalacticLeader Leader = (GalacticLeader)U;
        public int LineupCount = U;
        public int[] Lineup;
    }

    public sealed class SentIn : GameEvent
    {
        public int Side = U;
        public int PokemonId = U;
        public int Species = U;
        public int Hp = U;
        public int MaxHp = U;
        public StatusEffect Status = (StatusEffect)U;
        public int TrainerKey = U;
    }

    public sealed class SwitchedOut : GameEvent
    {
        public int Side = U;
        public int PokemonId = U;
        public SwitchReason Reason = (SwitchReason)U;
    }

    public sealed class InitiativeTokenAt : GameEvent
    {
        public int Side = U;
    }

    public sealed class OrderDecided : GameEvent
    {
        public OrderSide[] Sides;
        public int First = U;
        public bool TokenUsed;
        public int TokenTo = U;
    }

    public sealed class MoveSelected : GameEvent
    {
        public int Side = U;
        public int MoveKey = U;
        public PokemonType Type = (PokemonType)U;
        public int Power = U;
    }

    public sealed class MoveUsed : GameEvent
    {
        public int Side = U;
        public int PokemonId = U;
        public int MoveKey = U;
        public PokemonType Type = (PokemonType)U;
        public MoveCategory Category = (MoveCategory)U;
        public int TargetId = U;
    }

    public sealed class DiceRolled : GameEvent
    {
        public int Side = U;
        public int PokemonId = U;
        public DiceKind Kind = (DiceKind)U;
        public PokemonType Type = (PokemonType)U;
        public AttackDieFace[] Faces;
        public int Sum = U;
        public int Power = U;
        public Mod[] PowerMods;
        public int FatigueBefore = U;
        public int FatigueAfter = U;
        /// <summary>포획 굴림(Kind = Capture — 결정 188)이면 던진 볼 등급, 아니면 U.</summary>
        public int BallKey = U;
    }

    /// <summary>면 바꾸기. SrcBefore·SrcAfter = (s, r, c) 개수 3칸. 지운 주사위는 After = None, 더한 주사위는 Before = None(G14). 바꿀 면이 없었으면 Changes가 빈다(0 결과).</summary>
    public sealed class DiceFacesChanged : GameEvent
    {
        public FaceChange[] Changes;
        public int[] SrcBefore;
        public int[] SrcAfter;
    }

    /// <summary>타입 상성 결과(01 §3-7 TypeMatchup. 이름이 TypeChart의 enum Effectiveness와 겹쳐 바꿈).</summary>
    public sealed class TypeMatchup : GameEvent
    {
        public int AttackerId = U;
        public int DefenderId = U;
        public PokemonType Type = (PokemonType)U;
        public PokemonType[] DefTypes;
        public int Delta = U;
        public bool Typeless;
    }

    public sealed class DamageBreakdown : GameEvent
    {
        public int TargetId = U;
        public DamageStep[] Steps;
        public int Final = U;
    }

    public sealed class Fled : GameEvent
    {
        public int Side = U;
        public ResChange[] Changes;
    }

    public sealed class Forfeited : GameEvent { }

    /// <summary>포획. Shakes는 표시 전용(01 §6-5). Changes = 던진 볼(Res = Ball, Key = 볼 등급, 01 §3-6 ③) — 화면 볼 칩이 던지는 순간 Before → After.</summary>
    public sealed class CaptureAttempt : GameEvent
    {
        public int Side = U;
        public int BallKey = U;
        public int TargetId = U;
        public AttackDieFace[] Faces;
        public int Sum = U;
        public int CatchRate = U;
        public Mod[] RateMods;
        public bool StatusConverted;
        /// <summary>매혹적인 스튜로 심볼 하나를 일반 타격으로 바꿨다(S10 — Sum에 든다, Faces는 굴린 그대로).</summary>
        public bool Allured;
        public int Shakes = U;
        public bool Success;
        public ResChange[] Changes;
    }

    /// <summary>배틀 끝(04 70행). Winner = 이긴 쪽(Side 0·1), 없으면 −1(무승부·야생 도망). Changes = 이긴 좌석이 받은 JP·규토리.</summary>
    public sealed class BattleEnded : GameEvent
    {
        public int BattleId = U;
        public int Winner = U;
        public BattleResult Result = (BattleResult)U;
        public ResChange[] Changes;
    }

    public sealed class RetributionStored : GameEvent
    {
        public int Side = U;
        public int Before = U;
        public int After = U;
        public RetributionCause Cause = (RetributionCause)U;
    }

    /// <summary>은하의 응징 옮기기(G5). 반응 조건이 깨지면 Amount 0(01 §3-6 ⑥).</summary>
    public sealed class RetributionMoved : GameEvent
    {
        public int FromSide = U;
        public int ToPokemonId = U;
        public int Amount = U;
    }

    public sealed class SwitchLocked : GameEvent
    {
        public int Side = U;
        public SwitchLockReason Reason = (SwitchLockReason)U;
    }

    // ── 캠페인 ──

    public sealed class ActRevealed : GameEvent
    {
        public int ActKey = U;
        public int FirstSeat = U;
        public int PrevActKey = U;
    }

    public sealed class ActGoalMet : GameEvent
    {
        public int Act = U;
        public bool First;
        public ResChange[] Changes;
    }

    /// <summary>간부를 이겨 조각을 얻음(2막, 04 94행 — M5 「막 뼈대」). Count = 얻은 뒤 가진 조각 수, Cell = 그 호수 칸.</summary>
    public sealed class ShardGained : GameEvent
    {
        public Shard Shard = (Shard)U;
        public int Count = U;
        public int Cell = U;
    }

    /// <summary>열린 파티 칸이 늘었다(조각 1개마다 +1, A2·A3).</summary>
    public sealed class PartySlotUnlocked : GameEvent
    {
        public int Before = U;
        public int After = U;
    }

    /// <summary>시작의 사당 공물로 심장을 회복함(3막). ItemKey = 가방에 들어온 신오의 심장(아이템 색인). Source = 그 사당(Shrine, 고정물 색인).</summary>
    public sealed class HeartRestored : GameEvent
    {
        public int ItemKey = U;
        public int Cell = U;
    }

    /// <summary>종막 선택(04 95행). Traitor = 배신자를 골랐다(false = 영웅).</summary>
    public sealed class EndingChosen : GameEvent
    {
        public bool Traitor;
    }

    /// <summary>배신자가 정한 도전 순서(04 96행). Seats = 상대 좌석, 앞이 먼저(상대가 없으면 빈 배열).</summary>
    public sealed class TraitorOrderSet : GameEvent
    {
        public int[] Seats;
    }

    /// <summary>게임 끝(04 97행). Seat = 이긴 좌석. 결과 화면의 파티·통계(01 §3-7 Party·Stats)는 「막·종막 화면」 묶음이 더한다.</summary>
    public sealed class GameEnded : GameEvent
    {
        public GameEndReason Reason = (GameEndReason)U;
        public int[] Winners;
    }

    /// <summary>
    /// 여행 스탬프를 받음(04 47행, rulings A30 — StampRules). Seat = 받은 좌석(남의 차례에도), StampKey = 그 칸(아이템 색인 stamp.&lt;n&gt; — Source도 Stamp:그 색인), CountBefore/After = 모은 수,
    /// Changes = 보상 규토리·수호신의 서(Item), MoveKeys = 보상으로 기술 풀에 넣은 기술(약한·보통·강한 카드 5장의 시그니처 — 공개, 01 §4 [추정]). 행은 「스탬프·도전·거래 화면」.
    /// </summary>
    public sealed class StampGained : GameEvent
    {
        public int StampKey = U;
        public int CountBefore = U;
        public int CountAfter = U;
        public ResChange[] Changes;
        public int[] MoveKeys;
    }

    /// <summary>
    /// 트레이너 카드 등급이 올랐다(04 48행, rulings A20-2~5 — TrainerRules). Seat = 그 좌석(남의 차례 · 배틀 한가운데에도), CardKey = 트레이너 카드 색인(Source도 TrainerCard:그 색인),
    /// TierBefore/After = 1 브론즈 · 2 실버 · 3 골드(브론즈 → 골드는 UnlockOrder가 StraightToGold·RememberThenGold일 때), CausePokemonId = 쓰러뜨리거나 얻은 포켓몬,
    /// CauseGrade = 센 등급(Moderate·Strong — 옵션으로 강함이 된 전설·우두머리는 Strong), AbilitySlots = 이번에 풀린 능력 칸(1 파랑 · 2 빨강). 연출 행은 화면 묶음(M6 「트레이너 카드 화면」).
    /// </summary>
    public sealed class CardUnlocked : GameEvent
    {
        public int CardKey = U;
        public int TierBefore = U;
        public int TierAfter = U;
        public int CausePokemonId = U;
        public EncounterTier CauseGrade = (EncounterTier)U;
        public int[] AbilitySlots;
    }

    /// <summary>
    /// 트레이너 카드 능력을 썼다(04 50행 — TrainerRules.Used). CardKey = 트레이너 카드 색인, Ability = 파랑·빨강, Targets = 능력이 가리킨 것(포켓몬 Id·칸 등 — 능력마다, 없으면 빈 배열).
    /// 상시 보정은 이 이벤트를 내지 않는다(결과 이벤트의 Mods[]). 내는 코드는 능력 묶음이 쓴다.
    /// </summary>
    public sealed class AbilityUsed : GameEvent
    {
        public int CardKey = U;
        public AbilityColor Ability = (AbilityColor)U;
        public int[] Targets;
    }

    /// <summary>
    /// 트레이너 카드를 바꿨다(youngster R 「swap this card with one of the drawn cards」 — 01 §3-7 · rulings A40). Source = 바꾸기 전 카드(반바지 꼬마), FromCard·ToCard = 트레이너 카드 색인,
    /// Tier = 새 카드의 등급(골드 — A20-5). 새 카드의 「When unlocked」는 이 이벤트 뒤에 파랑 → 빨강 순서로. 뽑은 5장은 TrainerCardsDealt(본인만 카드)로 먼저 나간다.
    /// </summary>
    public sealed class TrainerCardSwapped : GameEvent
    {
        public int FromCard = U;
        public int ToCard = U;
        public int Tier = U;
    }

    /// <summary>
    /// 날씨 휠을 놓거나 돌렸다(parasol_lady B — 01 §3-7 WeatherSet · rulings A41). Weather = TrainerRules.WeatherTypes 색인(0 쾌청 · 1 눈 · 2 비 · 3 모래바람), Before = 앞 날씨(−1 = 처음 놓음),
    /// Source = 파라솔 아가씨 카드, Seat = 놓거나 바꾼 좌석, Changes = 낸 JP(사당에서 바꿀 때 3 — 처음 놓기는 무료라 빈 배열).
    /// </summary>
    public sealed class WeatherSet : GameEvent
    {
        public int Weather = U;
        public int Before = U;
        public ResChange[] Changes;
    }
}
