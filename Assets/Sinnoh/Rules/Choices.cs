using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 좌석 값(01 §3-6 ②)과 보기 값(01 §2-2 Viewer). 실제 좌석은 0~5, 그 밖은 가상 행위자·응답자다 [가정].
    /// </summary>
    public static class SeatId
    {
        public const int MaxSeats = 6;
        public const int System = -1;
        public const int Galactic = -2;
        public const int UltraBeast = -3;
        public const int Wild = -4;

        /// <summary>GameState.Viewer: 호스트 원본(숨은 정보 전부).</summary>
        public const int HostViewer = -1;
        /// <summary>GameState.Viewer: 관전자 보기(공개 정보만, 01 §4) [가정].</summary>
        public const int Observer = -5;
    }

    /// <summary>효과·선택의 출처 종류(01 §3-7 SourceRef).</summary>
    public enum SourceKind { None, Move, TrainerCard, Fate, Item, Status, Relic, Heart, Weather, BreachCard, Onslaught, Shrine, HiddenSpot, Stamp, Act, Rule, ShrineEvent }

    /// <summary>
    /// 출처(01 §3-7): Kind + 그 표의 int 색인. 없음 = {None, −1}(<see cref="None"/>). 구조체 기본값 {None, 0}은 쓰지 않는다.
    /// </summary>
    public struct SourceRef
    {
        public SourceKind Kind;
        public int Id;

        public SourceRef(SourceKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static SourceRef None => new SourceRef(SourceKind.None, -1);

        /// <summary>이벤트 필드의 미설정 표지(01 §3-6 시험). 값 None과 다르다.</summary>
        public static SourceRef Unset => new SourceRef((SourceKind)int.MinValue, int.MinValue);

        public static SourceRef Rule(RuleSource r) => new SourceRef(SourceKind.Rule, (int)r);

        public bool IsUnset => (int)Kind == int.MinValue || Id == int.MinValue;

        public bool Same(SourceRef o) => Kind == o.Kind && Id == o.Id;

        internal void Walk(IStateWriter w)
        {
            w.Int((int)Kind);
            w.Int(Id);
        }

        internal static SourceRef Read(IStateReader r)
        {
            var kind = (SourceKind)r.Int();
            int id = r.Int();
            return new SourceRef(kind, id);
        }
    }

    /// <summary>SourceRef.Kind = Rule일 때 Id(01 §3-7). 쓰는 곳에서 더한다.</summary>
    public enum RuleSource { FallenStar, Respawn, VillageHeal, CampHeal, MegalopolisHeal, Fatigue, Silvally, TraitorReturn, InitiativeToken, GalacticRetribution, Fly, AlphaStrike, BeastBoost, CelestialAura, GodFollower, LakeGuardian }

    /// <summary>결정 지점(01 §3-2 표). 뼈대(Clock·Default·Legal)는 ChoiceRules(M3-c), 답 적용은 그 기능을 만드는 작업.</summary>
    public enum ChoiceKind
    {
        VillagePick, StarterPick, TrainerCardPick,
        MapAction,
        HiddenSpot, EncounterPick,
        Lineup,
        SendIn, NextPokemon, SwitchWindow,
        BattleAction,
        Reaction,
        Respond,
        Simultaneous,
        Discard, Reward, Ending,
    }

    /// <summary>
    /// 같은 ChoiceKind 안의 갈래(01 §3-2 표의 답 칸). Reaction: 우선권 토큰·은하의 응징·운명 카드 / Respond: 도전·거래 /
    /// Simultaneous: 추종자·균열 카드 / Reward: 트레이너전 보상·기술 가르치기·진화 분기·공유 축복·산호 색·포획 뒤 파티 가득(놓아줄 포켓몬, rulings G13) /
    /// Ending: 영웅·배신자·배신자 도전 순서.
    /// </summary>
    public enum ChoiceSub
    {
        None,
        InitiativeToken, Retribution, Fate,
        Challenge, Trade,
        Follower, BreachAttach,
        TrainerReward, TeachMove, EvolveBranch, SharedBlessing, CoralColor,
        EndingChoice, TraitorOrder,
        PartyFull,
        /// <summary>Reaction: 굴린 뒤 면 바꾸기의 적용 순서(결정 72 — 순서로 결과가 갈릴 때만 공격자에게, EffectRules.AskOrder). Subject = 공격하는 포켓몬, 답 = PickMany(효과 Id, 앞이 먼저).</summary>
        EffectOrder,
        /// <summary>Reaction: 「쓸까」를 묻는 효과(op Ask, M5 — 「You may …」·「The user may …」). Source = 그 효과의 출처, Subject = 효과를 가진 포켓몬, 답 = Decline(첫 답·기본) / Accept. 답은 기다리던 효과 프레임이 받는다(EffectRules.Answered).</summary>
        EffectOption,
        /// <summary>Reward: 진화한 포켓몬에게 옛 포켓몬의 시그니처 기술을 가르칠까(p.5 — 배울 수 있는 타입과 상관없이, EvolveRules). Subject = 그 기술, Options = [진화한 포켓몬], 답 = Decline(첫 답·기본) / Pick(포켓몬, 칸).</summary>
        EvolveTeach,
        /// <summary>
        /// Reaction: 매혹적인 스튜를 이 굴림에 쓸까(S10 「Replace any 1 status symbol with 1 regular hit once during your next wild encounter」 — CraftRules). Subject = 굴림 종류
        /// (0 = 공격 굴림 — 면 바꾸기·순서 선택이 끝난 뒤, 1 = 포획 굴림 — Options에 이어 갈 값), 답 = Decline(첫 답) / Accept. 기본(시간 초과·봇) = 결과가 좋아질 때만 Accept.
        /// </summary>
        AlluringStew,
        /// <summary>
        /// Reaction: 기술 효과가 여는 고르기(M5 나머지 op 갈래, rulings G20 — EffectRules.Pick): 공격 타입(hidden_power·judgement) · 상태(photon_geyser) · 규토리 색(bug_bite·belch) ·
        /// 대상 파티의 다른 포켓몬(shocking_verity·earthquake …). Source = 그 기술, Subject = 효과를 가진 포켓몬, Options = 고를 값(PokemonType·StatusEffect·색·포켓몬 인스턴스 — 무엇인지는 Source의 기술이 정한다),
        /// Count = 0이면 「안 함」(Decline)이 첫 답(「You may …」), 1이면 반드시 하나. 답 = Pick(값). 기다리던 효과 프레임이 받는다(EffectRules.Answered).
        /// </summary>
        EffectPick,
        /// <summary>
        /// Reward: 사당 카드(M5 「사당 공물·카드」, ShrineRules — Source = 그 카드 ShrineEvent): ShrinePick = 3장 뽑기에서 처리할 카드(Options = 남은 뽑은 카드) ·
        /// ShrineApricorns = 뜻밖의 선물 아무 색 Count개(PickMany) · ShrineStews = 화려한 만찬 서로 다른 스튜 Count개(PickMany 아이템, Subject 1 = 나눠 받아 자기 턴 시작에) ·
        /// ShrineExchange = 동등한 교환할 파티 포켓몬(Decline 첫 답, Count = 단계) · ShrineExchangeCard = 그 자리에 올 야생 카드(Options = [카드, 더미] 짝, Subject = 포켓몬) ·
        /// ShrineMove = 고대의 지혜로 배울 기술(Options = 열 때의 버림 더미 기술, Decline 첫 답) → 이어서 TeachMove(같은 Source). 나눌 좌석은 SharedBlessing.
        /// </summary>
        ShrinePick, ShrineApricorns, ShrineStews, ShrineExchange, ShrineExchangeCard, ShrineMove,
        /// <summary>Reward: 천상의 균열 집계가 동률(FractureRules, rulings A31 ⑤) — 뽑은 좌석이 내려올 신을 고른다. Source = 그 카드(ShrineEvent), Options = 동률인 신(SinnohGod, 신 순서), 답 = Pick(신).</summary>
        FractureTie,
        /// <summary>Reward: 모두 탈락했고 아르세우스의 축복이 남았다(울트라 버스트, UbBlessing — rulings A54 ⑥) — 방금 탈락한 좌석이 부활할 좌석을 고른다. Options = 탈락한 좌석, 답 = Pick(좌석).</summary>
        Revive,
        /// <summary>
        /// Respond: 다른 좌석이 아르세우스의 축복을 쓰자고 냈다(울트라 버스트 여럿 판, UbBlessing — 사용자 결정 84 U1 나). 낸 좌석을 뺀 모든 좌석에게 한꺼번에(묶음) 묻고 하나라도 막으면(Decline) 쓰지 않는다.
        /// Subject = 낸 좌석, Options = [값(UbBlessing.SkipTurn 또는 부활할 좌석), 라운드 끝 물음에서 나왔으면 1], 답 = Decline(막기) / Accept(허용 — 기본·시간 초과).
        /// </summary>
        Blessing,
        /// <summary>Respond: 라운드가 끝났고 축복이 남았다 — 「UB 턴을 건너뛰겠습니까?」(울트라 버스트, UbBlessing — 사용자 결정 84 U2 나). Seat = 라운드의 마지막 좌석, Subject = −1, 답 = Decline(첫 답·기본) / Accept.</summary>
        UbSkip,
    }

    /// <summary>선택에 거는 시계 종류(01 §3-3, 02 §5-3). 길이는 세션이 정한다.</summary>
    public enum ClockKind { None, MapTurn, BattleAction, Reaction, Setup, Respond }

    /// <summary>시점 창(01 §7-3). None = 창 밖.</summary>
    public enum Window
    {
        None,
        // 맵
        TurnStart, OnMoveStep, OnSearch, OnEncounterRevealed, BeforeBattle, OnCapture, OnDefeat, OnStampGained, OnUnlock, OnShrineOffer, TurnEnd, OnBreachBracket,
        // 배틀
        BattleStart, BattleTurnStart, BeforeRoll, AfterRoll, BeforeDamage, AfterDamage, OnFaint, BattleTurnEnd, BattleEnd,
    }

    /// <summary>
    /// 답 종류. Decline = 「안 함」(지나가기·거절·교체 안 함·안 씀, G9의 첫 답). MapAction 답은 01 §3-2 표의 행동, BattleAction은 기술·볼·기권.
    /// </summary>
    public enum AnswerKind
    {
        Decline, Accept, Pick,
        Move, Search, TakeApricorns, Offer, VillageHeal, Ginkgo, Fly, Craft, Evolve, PlayFate, UseAbility, Challenge, ProposeTrade, EndTurn,
        UseMove, ThrowBall, Forfeit,
    }

    /// <summary>
    /// 선택의 답(01 §3-2). 값의 뜻은 답 종류가 정한다: Target = 고른 것(칸·카드·포켓몬 인스턴스·좌석·마을·기술 …), Arg = 둘째 값
    /// (알파 스트라이크 타입 등), Values = 여럿 고르기(명단·순서). 없음은 −1·빈 목록.
    /// Move: Target = 도착 칸. Values가 비면 옛 꼴(도착 칸만 — 엔진이 최단 경로로 걷고 도착 칸에서만 묻는다), 차 있으면 고른 길(결정 71 — 출발 칸 뺀 걸음 칸,
    /// 마지막 = Target, <see cref="MoveAlong"/>)이고 길 위의 뒷면 숨겨진 장소마다 멈춰 묻는다. 고른 길은 Legal에 늘어놓지 않고 ChoiceRules.IsLegal이 길을 따져 받는다.
    /// </summary>
    public sealed class ChoiceAnswer : IStatePart<ChoiceAnswer>
    {
        public AnswerKind Kind;
        public int Target = -1;
        public int Arg = -1;
        public List<int> Values = new List<int>();

        public ChoiceAnswer() { }

        public ChoiceAnswer(AnswerKind kind, int target = -1, int arg = -1)
        {
            Kind = kind;
            Target = target;
            Arg = arg;
        }

        public static ChoiceAnswer Of(AnswerKind kind, int target = -1, int arg = -1) => new ChoiceAnswer(kind, target, arg);

        public static ChoiceAnswer Pick(int target, int arg = -1) => new ChoiceAnswer(AnswerKind.Pick, target, arg);

        public static ChoiceAnswer PickMany(List<int> values)
        {
            var a = new ChoiceAnswer(AnswerKind.Pick);
            a.Values.AddRange(values);
            return a;
        }

        /// <summary>결정 71 고른 길로 이동: path = 출발 칸부터 도착 칸까지(<c>MoveRules.Route</c>가 돌려주는 꼴). 답에는 출발 칸을 빼고 싣는다.</summary>
        public static ChoiceAnswer MoveAlong(IReadOnlyList<int> path)
        {
            if (path == null || path.Count < 2) throw new System.ArgumentException("출발 칸과 한 걸음 이상", nameof(path));
            var a = new ChoiceAnswer(AnswerKind.Move, path[path.Count - 1]);
            for (int i = 1; i < path.Count; i++) a.Values.Add(path[i]);
            return a;
        }

        public ChoiceAnswer Clone()
        {
            var c = new ChoiceAnswer();
            c.CopyFrom(this);
            return c;
        }

        /// <summary>값이 같다(검증 「답 ∈ Legal」, 01 §3-1). Values는 순서까지 같아야 한다 — Legal이 정한 정규형으로 보낸다.</summary>
        public bool SameAs(ChoiceAnswer o)
        {
            if (o == null || Kind != o.Kind || Target != o.Target || Arg != o.Arg || Values.Count != o.Values.Count) return false;
            for (int i = 0; i < Values.Count; i++)
                if (Values[i] != o.Values[i]) return false;
            return true;
        }

        public override string ToString() => Values.Count == 0 ? $"{Kind}({Target},{Arg})" : $"{Kind}({Target},{Arg},[{string.Join(",", Values)}])";

        public void CopyFrom(ChoiceAnswer o)
        {
            Kind = o.Kind;
            Target = o.Target;
            Arg = o.Arg;
            StateParts.CopyInts(Values, o.Values);
        }

        public void Walk(IStateWriter w)
        {
            w.Int((int)Kind);
            w.Int(Target);
            w.Int(Arg);
            StateParts.Ints(w, Values);
        }

        public void Read(IStateReader r)
        {
            Kind = (AnswerKind)r.Int();
            Target = r.Int();
            Arg = r.Int();
            StateParts.ReadInts(r, Values);
        }
    }

    /// <summary>
    /// 열린 선택(01 §3-3). 엔진은 누가 답하는지 모르고 Seat(가상 응답자 포함)만 싣는다. Default는 시간 초과 답이고 늘 합법이다.
    /// </summary>
    public sealed class PendingChoice : IStatePart<PendingChoice>
    {
        /// <summary>증가 번호(GameState.NextChoiceId).</summary>
        public int Id;
        public ChoiceKind Kind;
        /// <summary>0~5, 또는 SeatId.Galactic·UltraBeast·Wild.</summary>
        public int Seat;
        /// <summary>동시 선택 묶음. 묶음이 다 답해야 진행.</summary>
        public int GroupId = -1;
        /// <summary>다 답할 때까지 남의 답을 숨긴다(천상의 균열).</summary>
        public bool Secret;
        public ClockKind Clock;
        public ChoiceAnswer Default = new ChoiceAnswer();
        /// <summary>무엇 때문에 묻나(화면 문구용).</summary>
        public SourceRef Source = SourceRef.None;
        public Window Window;
        /// <summary>숨은 손패로만 쓸 수 있는 반응 창(01 §3-3 [가정]).</summary>
        public bool HandGated;
        /// <summary>같은 Kind 안의 갈래. 없으면 None.</summary>
        public ChoiceSub Sub;
        /// <summary>고를 개수(Lineup k·산호 색 n). 하나면 1.</summary>
        public int Count = 1;
        /// <summary>열 때 정한 공개 후보(조우 카드·명단·진화 분기·균열 카드 대상 …). 상태에서 읽는 선택(손패·트레이너 카드 후보·파티)은 비운다.</summary>
        public List<int> Options = new List<int>();
        /// <summary>대상(Respond: 건 좌석, Reaction 응징: 공격한 포켓몬, Reward: 상대 좌석·가르칠 기술 …, Sub가 정함). 없으면 −1.</summary>
        public int Subject = -1;
        /// <summary>이 선택을 연 행동의 Seq(01 §3-6 ①-2: 답의 Continues·ReactsTo). 없으면 −1.</summary>
        public int FromSeq = -1;
        /// <summary>묶음(GroupId)에서 답을 받아 두었다. 묶음이 다 답하면 적용한다.</summary>
        public bool Answered;
        /// <summary>받아 둔 답. 비공개 묶음이면 남의 보기에서 가린다(01 §4).</summary>
        public ChoiceAnswer Answer = new ChoiceAnswer();
        /// <summary>받아 둔 답을 낸 방식(묶음을 적용할 때 ActionStarted.Auto).</summary>
        public AutoKind AnswerAuto;

        public void CopyFrom(PendingChoice o)
        {
            Id = o.Id;
            Kind = o.Kind;
            Seat = o.Seat;
            GroupId = o.GroupId;
            Secret = o.Secret;
            Clock = o.Clock;
            Default.CopyFrom(o.Default);
            Source = o.Source;
            Window = o.Window;
            HandGated = o.HandGated;
            Sub = o.Sub;
            Count = o.Count;
            StateParts.CopyInts(Options, o.Options);
            Subject = o.Subject;
            FromSeq = o.FromSeq;
            Answered = o.Answered;
            Answer.CopyFrom(o.Answer);
            AnswerAuto = o.AnswerAuto;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Id);
            w.Int((int)Kind);
            w.Int(Seat);
            w.Int(GroupId);
            StateParts.Bool(w, Secret);
            w.Int((int)Clock);
            Default.Walk(w);
            Source.Walk(w);
            w.Int((int)Window);
            StateParts.Bool(w, HandGated);
            w.Int((int)Sub);
            w.Int(Count);
            StateParts.Ints(w, Options);
            w.Int(Subject);
            w.Int(FromSeq);
            StateParts.Bool(w, Answered);
            Answer.Walk(w);
            w.Int((int)AnswerAuto);
        }

        public void Read(IStateReader r)
        {
            Id = r.Int();
            Kind = (ChoiceKind)r.Int();
            Seat = r.Int();
            GroupId = r.Int();
            Secret = StateParts.ReadBool(r);
            Clock = (ClockKind)r.Int();
            Default.Read(r);
            Source = SourceRef.Read(r);
            Window = (Window)r.Int();
            HandGated = StateParts.ReadBool(r);
            Sub = (ChoiceSub)r.Int();
            Count = r.Int();
            StateParts.ReadInts(r, Options);
            Subject = r.Int();
            FromSeq = r.Int();
            Answered = StateParts.ReadBool(r);
            Answer.Read(r);
            AnswerAuto = (AutoKind)r.Int();
        }
    }

    /// <summary>
    /// 처리 중인 효과 하나(01 §7-2). 선택을 사이에 두고 C# 지역 변수를 들지 않는다 — 이어 갈 값은 Locals에 둔다.
    /// EffectId = EffectDb 색인, StepIndex = 다음에 할 op 자리(0이면 조건부터 본다). User·Target = 효과를 가진 포켓몬과 대상(배틀 효과, M3-f).
    /// Locals 자리(M5 효과 기반): [0] = 효과마다 자유(면 바꾸기 상쇄 표시 · 처리기) · [LocalWait] = 선택 대기(EffectRules.WaitAsked·WaitAnswered) ·
    /// [LocalAnswerKind]·[LocalAnswerTarget] = 받은 답의 Kind·Target.
    /// </summary>
    public sealed class EffectFrame : IStatePart<EffectFrame>
    {
        public const int LocalCount = 4;
        public const int LocalWait = 1;
        public const int LocalAnswerKind = 2;
        public const int LocalAnswerTarget = 3;

        public int EffectId = -1;
        public int StepIndex;
        public SourceRef Source = SourceRef.None;
        /// <summary>효과를 가진 좌석(가상 행위자 포함).</summary>
        public int Owner = SeatId.System;
        /// <summary>효과를 가진 포켓몬 인스턴스(기술을 쓴 포켓몬, 상태이상에 걸린 포켓몬). 없으면 −1.</summary>
        public int User = -1;
        /// <summary>대상(포켓몬 인스턴스·칸·좌석, 효과가 정함). 없으면 −1.</summary>
        public int Target = -1;
        public int[] Locals = new int[LocalCount];

        public void CopyFrom(EffectFrame o)
        {
            EffectId = o.EffectId;
            StepIndex = o.StepIndex;
            Source = o.Source;
            Owner = o.Owner;
            User = o.User;
            Target = o.Target;
            Locals = StateParts.CopyArray(Locals, o.Locals);
        }

        public void Walk(IStateWriter w)
        {
            w.Int(EffectId);
            w.Int(StepIndex);
            Source.Walk(w);
            w.Int(Owner);
            w.Int(User);
            w.Int(Target);
            StateParts.Ints(w, Locals);
        }

        public void Read(IStateReader r)
        {
            EffectId = r.Int();
            StepIndex = r.Int();
            Source = SourceRef.Read(r);
            Owner = r.Int();
            User = r.Int();
            Target = r.Int();
            Locals = StateParts.ReadArray(r, Locals);
        }
    }

    /// <summary>
    /// 「턴당·조우당 1번」 기록 종류(01 §2-2 Used, §7-2). Effect는 EffectDef.Limit(Key = 효과 Id).
    /// EmptySearch = 이번 턴 그 칸(Key)을 찾아봤는데 조우가 없었다 — 규토리 줍기를 열고 같은 칸 다시 찾아보기를 닫는다(rulings G12).
    /// Challenge = 이번 턴 그 좌석(Key)에 도전했다 — 거절당해도 그 턴에 다시 도전하지 않는다(rulings G13).
    /// Stew = 이번 턴 그 스튜(Key = 아이템)를 만들었다(p.14 「Each type of stew can only be crafted once per turn」) · InfusedStew = 이번 턴 우러난 스튜의 진화 반값이 남아 있다(진화 한 번에 쓴다) ·
    /// AlluringStew = 매혹적인 스튜가 그 턴의 다음 야생 조우를 기다린다(맵 턴 단위 — 쓰거나 그 조우가 끝나거나 턴이 바뀌면 지운다, CraftRules).
    /// ShrineOffer = 이번 턴 사당 공물을 바쳤다(p.17 「Once per turn」 — 어느 사당이든) · Banquet = 화려한 만찬을 나눠 받아 다음 자기 턴 시작에 스튜를 고른다(Key = 단계, Subject = 카드, Scope −1 — ShrineRules).
    /// GodVictory = 이번 턴 신을 쓰러뜨리거나 잡았다 — 배틀 뒤 턴을 끝낼 때 다음 좌석 대신 한 턴 더(p.21 「you may immediately take another turn」, rulings A28 ①).
    /// </summary>
    public enum UsageKind { VillageHeal, Ginkgo, Fly, FateMap, FateBattle, Ability, Effect, EmptySearch, Challenge, Stew, InfusedStew, AlluringStew, ShrineOffer, Banquet, GodVictory }

    /// <summary>
    /// 사용 기록 하나. Scope = 기록이 속한 단위 번호(맵 턴 = TurnId, 배틀 턴 = BattleState.Turn 등). 단위가 바뀌면 엔진이 지운다.
    /// Subject = 「그 포켓몬이 나갈 때까지 한 번」(EffectLimit.UntilSwitch)의 포켓몬 인스턴스 — 그 밖은 −1.
    /// </summary>
    public sealed class UsageMark : IStatePart<UsageMark>
    {
        public int Seat;
        public UsageKind Kind;
        public int Key = -1;
        public int Scope;
        public int Subject = -1;

        public void CopyFrom(UsageMark o)
        {
            Seat = o.Seat;
            Kind = o.Kind;
            Key = o.Key;
            Scope = o.Scope;
            Subject = o.Subject;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Seat);
            w.Int((int)Kind);
            w.Int(Key);
            w.Int(Scope);
            w.Int(Subject);
        }

        public void Read(IStateReader r)
        {
            Seat = r.Int();
            Kind = (UsageKind)r.Int();
            Key = r.Int();
            Scope = r.Int();
            Subject = r.Int();
        }
    }
}
