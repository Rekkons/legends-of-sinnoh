using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// AI 하나(01 §9-1). Rules/Session에 정의하고 Sinnoh.Ai가 구현한다 — 세션은 이것만 알고, 호스트 조립 지점(Client·시험)이 구현을 꽂는다.
    /// 세션이 가상 응답자(갤럭시단·UB·야생)의 선택을 넘긴다. 답은 늘 그 선택의 Legal 안이어야 한다.
    /// </summary>
    public interface IAgent
    {
        AgentDecision Decide(AgentInput input, AgentBudget budget);
    }

    /// <summary>
    /// 가상 응답자 AI 고르기(M8 「원칙 AI 점검」, 01 §9-6 · 결정 26 · 30 ③ · rulings A16): 조립 지점(Client · 시험)이 <see cref="SessionConfig.VirtualAgent"/>에 꽂으면
    /// 세션이 가상 좌석 선택을 물을 때(메인 스레드 — <see cref="LocalSession"/>이 물음에 고른 AI를 싣는다) 좌석 값으로 AI 하나를 고른다.
    /// 갤럭시단 = 조종이 AI면 세기(<see cref="GalacticLevel"/>), 사람이면(<see cref="GalacticHuman"/>) 그 사람이 답하지 않았을 때 대신 답하는 AI라 늘 강함(결정 30 ③ [가정 01 §13-1 M8-28]).
    /// UB · 네크로즈마 · 야생(전설 · 우두머리 · 신 · 시련 · 무리) = <see cref="RuleOptions.NonPlayerMovePolicy"/>(기본 강한 AI — 결정 122, p.25 원칙은 옵션).
    /// 강한 AI가 없으면(null — 「강함」을 꽂기 전) 원칙. 방(<see cref="HostSession"/>)은 방 설정이 바뀔 때마다 두 칸을 다시 맞춘다.
    /// </summary>
    public sealed class VirtualAgents : IAgent
    {
        public IAgent Principle;
        public IAgent Strong;
        /// <summary>갤럭시단 AI 세기(방 · 솔로 설정 — 조종이 AI일 때).</summary>
        public GalacticAiLevel GalacticLevel = GalacticAiLevel.Principle;
        /// <summary>갤럭시단을 사람이 조종한다(방 설정 「사람」). 그 좌석이 답하지 않거나 없을 때만 AI가 묻힌다(02 §5-6).</summary>
        public bool GalacticHuman;

        /// <summary>그 가상 좌석의 선택에 강한 AI를 쓰나(결정 26 · 30 ③ · A16).</summary>
        public static bool WantsStrong(int virtualSeat, GalacticAiLevel galacticLevel, bool galacticHuman, NonPlayerMovePolicy nonPlayer)
        {
            if (virtualSeat == SeatId.Galactic) return galacticHuman || galacticLevel == GalacticAiLevel.Strong;
            return nonPlayer == NonPlayerMovePolicy.Strong;
        }

        /// <summary>그 가상 좌석의 선택에 답할 AI(강한 AI가 없으면 원칙).</summary>
        public IAgent For(int virtualSeat, NonPlayerMovePolicy nonPlayer)
        {
            if (Principle == null) throw new InvalidOperationException("VirtualAgents.Principle이 없다");
            return Strong != null && WantsStrong(virtualSeat, GalacticLevel, GalacticHuman, nonPlayer) ? Strong : Principle;
        }

        public AgentDecision Decide(AgentInput input, AgentBudget budget)
        {
            if (input?.Choice == null || input.View == null) throw new ArgumentException("View·Choice가 있어야 한다", nameof(input));
            return For(input.Choice.Seat, input.View.Options.NonPlayerMovePolicy).Decide(input, budget);
        }
    }

    /// <summary>
    /// AI 입력(01 §9-1). View = 보기 사본(가상 응답자는 ViewFor(Observer)), PublicLog = 그 보기 주인에게 거른 공개 기록, Choice = 보기 안의 그 선택.
    /// 원본 자리가 없다 — 타입으로 엿보기를 막고, AI는 View.Viewer가 호스트 원본이면 거절한다(rulings G15).
    /// </summary>
    public sealed class AgentInput
    {
        public GameState View;
        public List<GameEvent> PublicLog;
        public PendingChoice Choice;
        public Ctx Ctx;
    }

    /// <summary>
    /// AI 예산(01 §9-1). 시간은 밖에서 넣는다(마감 + 시계 함수): Now = 단조 시계(ms — <see cref="AgentWorker"/>가 넣는다), Deadline = 그 시계의 마감 시각(Now가 null이면 보지 않는다).
    /// 시험은 MaxSimulations만 써서 결정적으로 돈다(시간 마감으로 끝난 결정은 결정적이 아니다). Seed = AI 자기 난수(게임 Rng와 따로).
    /// </summary>
    public sealed class AgentBudget
    {
        public int MaxSimulations;
        public long Deadline;
        public Func<long> Now;
        public ulong Seed;
    }

    /// <summary>AI 답 + 사유(04 §5 ④ 자막, ChoiceAnswered.Reason — 표시 전용, 상태·해시와 무관).</summary>
    public sealed class AgentDecision
    {
        public ChoiceAnswer Answer;
        public AiReason Reason;

        public AgentDecision(ChoiceAnswer answer, AiReason reason)
        {
            Answer = answer ?? throw new ArgumentNullException(nameof(answer));
            Reason = reason ?? new AiReason(AiRule.None, 0, 0);
        }
    }

    /// <summary>
    /// AI 한 번 묻기(02 §1-2 AgentRequest): 세션이 정해진 시각에 메인 스레드에서 만들어 러너에 넘긴다. Input은 이 물음만의 사본이다(보기 사본 · 공개 기록 사본 — 메인 스레드가 고치지 않는다).
    /// Serial = 세션이 매기는 번호(낡은 답 가리기), ThinkMs = 마감(선택이 열린 시각 + <see cref="AiSettings.DeadlineMs"/>)까지 남은 ms — 러너가 자기 시계로 Budget.Deadline을 만든다.
    /// </summary>
    public sealed class AgentRequest
    {
        public int Serial;
        public int ChoiceId;
        public IAgent Agent;
        public AgentInput Input;
        public AgentBudget Budget;
        public int ThinkMs;
    }

    /// <summary>러너가 돌려준 답. Error면 Decision이 없다(그 AI가 던졌다).</summary>
    public sealed class AgentReply
    {
        public int Serial;
        public int ChoiceId;
        public AgentDecision Decision;
        public Exception Error;
    }

    /// <summary>
    /// AI 러너(02 §1-2): Post·TryTake는 세션을 돌리는 한 스레드(호스트 메인 스레드)에서만 부른다 — 답을 계산하는 곳은 러너가 정한다(<see cref="AgentWorker"/> = 작업 스레드 하나).
    /// 세션은 받은 답을 「그 물음이 아직 열려 있을 때만」 넣는다(<see cref="LocalSession"/>).
    /// </summary>
    public interface IAgentRunner
    {
        void Post(AgentRequest request);

        /// <summary>다 된 답 하나를 꺼낸다(없으면 false). 낸 순서대로 나온다.</summary>
        bool TryTake(out AgentReply reply);
    }
}
