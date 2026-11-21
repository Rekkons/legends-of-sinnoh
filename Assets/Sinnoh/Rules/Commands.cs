using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>명령 종류(01 §3-2). 명령 한 줄이 세이브 로그 한 줄이다(01 §10).</summary>
    public enum CommandKind
    {
        /// <summary>열린 선택에 답한다.</summary>
        Answer,
        /// <summary>정비(언제든 행동, 01 §3-4). 열린 선택에 답하지 않는다.</summary>
        Anytime,
        /// <summary>세션만 넣는다(seat = SeatId.System). 그 선택의 Default로 처리한다.</summary>
        Timeout,
        /// <summary>세션만 넣는다. 조종자 바꾸기·자리 비움(02 §6-2). 엔진 상태는 바꾸지 않고 로그에만 남는다.</summary>
        HostOp,
        /// <summary>남의 턴에 원격 거래를 제안한다(Answer = ProposeTrade 거래 내용, 결정 75 B5 나 — TradeRules.SubmitOffer). 열린 선택에 답하지 않는다.</summary>
        Propose,
    }

    /// <summary>HostOp 종류(02 §6-2). 엔진은 값을 보지 않는다.</summary>
    public enum HostOpKind { MarkAway, ClearAway, Kick, SetController }

    /// <summary>
    /// AI가 고른 이유(04 §5 ④). 「AI: 플랜 B (공격력 3, 은하의 응징 +1)」. Lookahead = 강한 AI가 수를 읽어 골랐다(M8 「연결」 — 갤럭시단 배틀 안의 선택,
    /// Power = 고른 수의 승산 %, Bonus = 원칙 수보다 높은 승산 %p(0 = 원칙 수와 같거나 낮음) [가정 01 §13-1 M8-33]).
    /// </summary>
    public enum AiRule { None, Signature, BestValue, PlanB, Lookahead }

    /// <summary>AI 사유. 표시 전용이라 상태·해시에 들어가지 않는다(01 §3-6 ⑨).</summary>
    public sealed class AiReason
    {
        public AiRule Rule;
        /// <summary>공격력(주사위 수). Lookahead면 고른 수의 승산(%).</summary>
        public int Power;
        /// <summary>더한 값(상성·효과·응징 등). Lookahead면 원칙 수보다 높은 승산(%p).</summary>
        public int Bonus;

        public AiReason() { }

        public AiReason(AiRule rule, int power, int bonus)
        {
            Rule = rule;
            Power = power;
            Bonus = bonus;
        }
    }

    /// <summary>정비 명령 종류(01 §3-4 표). 결과를 적는 절대형이다(rulings G8). 나머지 정비(요리사 파랑 등)는 M6에서 더한다.</summary>
    public enum MaintKind
    {
        /// <summary>파티 순서 = Values(지금 파티 포켓몬의 순열).</summary>
        PartyOrder,
        /// <summary>포켓몬 Pokemon의 기술 칸 Slot = Value(기술 색인, −1 = 빈 칸). 새 기술은 스탬프 기술 풀에서.</summary>
        SetMove,
        /// <summary>포켓몬 Pokemon 놓아주기 = 파티에 없음. Value = 그 포켓몬의 종(Def, 대상 확인 — 다르면 이미 놓아준 것, rulings G8).</summary>
        Release,
        /// <summary>포켓몬 Pokemon의 소지 아이템 = Value(아이템 색인, −1 = 없음 → 가방으로).</summary>
        HoldItem,
        /// <summary>포켓몬 Pokemon의 폼 = Value(포켓몬 표 색인 — 같은 종의 카드). 유물 폼(쉐이미·로토무·디아루가·펄기아·기라티나 — 그 유물을 지닐 때, 기본 폼은 언제든), 배틀 밖(p.6, FormRules, rulings A26).</summary>
        ChangeForm,
        /// <summary>내 포켓몬에게 상태이상을 건다(트레이너 카드 black_belt R = 화상 · fairy_tale_girl R = 졸림 — 내 턴·배틀 밖, rulings A35). Pokemon = 대상, Value = StatusEffect.</summary>
        Afflict,
        /// <summary>fisherman R 「for the cost of 2 Journey Points: Increase the Catch Size of a Pokémon in your party to its next level」 — Pokemon = 대상, Value = 새 월척 면(지금 면 + 1, rulings A38).</summary>
        CatchSize,
        /// <summary>chef B 「You may freely convert the Apricorns in your bag for different Apricorns of the same tier」 — Slot = 낼 색, Value = 받을 색(같은 단계, 하나씩, rulings A39).</summary>
        Convert,
        /// <summary>collector R 「The moves gained from stamp rewards can be added to your party as Pokémon instead」 — Value = 스탬프 보상 풀의 포켓몬 카드(포켓몬 표 색인, rulings A39).</summary>
        Adopt,
        /// <summary>interviewers R 「swap your taught moves between the Pokémon in your party」 — Pokemon·Slot = 한쪽, Value = 다른 포켓몬, Values[0] = 그 칸(맞바꾼다 — 빈 칸이면 옮기기, rulings A39).</summary>
        PassMove,
        /// <summary>아르세우스의 축복 1개를 쓴다(울트라 버스트, UbBlessing — rulings A54 ⑤): Value = −1이면 다음 UB 턴 건너뛰기, 좌석 번호면 그 탈락한 좌석 부활. 어느 좌석이든(탈락한 좌석도) 낸다.</summary>
        UseBlessing,
        /// <summary>pokemon_ranger B가 보관한 균열 카드 하나를 꺼내 붙인다(울트라 버스트, BreachRules.CheckStored — 사용자 결정 87 R4, rulings A44 ③): Pokemon·Slot = 붙일 칸(균열 카드가 없는 기술 칸 —
        /// 가르친 기술이 있으면 버린다), Value = 보관한 카드(카드 표 색인). 내 턴 · 배틀 밖.</summary>
        AttachBreach,
    }

    /// <summary>정비 시점 등급(01 §3-4, rulings G10).</summary>
    public enum MaintTiming
    {
        /// <summary>언제든. 배틀 참가 중이면 그 좌석 포켓몬의 공격 처리 중만 막는다.</summary>
        Always,
        /// <summary>그 좌석이 배틀 참가 중이 아닐 때.</summary>
        NotInOwnBattle,
        /// <summary>배틀 밖(그 좌석이 배틀 참가 중이 아닐 때, 남의 차례 포함).</summary>
        OutsideBattle,
    }

    /// <summary>정비 명령 하나(rulings G8): 대상 하나 + 새 값.</summary>
    public sealed class Maintenance
    {
        public MaintKind Kind;
        public int Pokemon = -1;
        public int Slot = -1;
        public int Value = -1;
        public List<int> Values = new List<int>();

        public static MaintTiming TimingOf(MaintKind k)
        {
            switch (k)
            {
                case MaintKind.Release: return MaintTiming.NotInOwnBattle;
                case MaintKind.HoldItem:
                case MaintKind.Afflict:
                case MaintKind.ChangeForm:
                case MaintKind.Adopt: return MaintTiming.OutsideBattle;
                default: return MaintTiming.Always;
            }
        }
    }

    /// <summary>
    /// 명령(01 §3-2). Answer = ChoiceId + Answer(+ AI면 ByAi·Reason, 표시 전용) / Anytime = Maint / Timeout = ChoiceId / HostOp = HostOp·HostSeat.
    /// </summary>
    public sealed class Command
    {
        public CommandKind Kind;
        public int ChoiceId = -1;
        public ChoiceAnswer Answer;
        /// <summary>AI가 낸 답(ActionStarted.Auto = Ai, ChoiceAnswered.ByAi). 상태에 영향 없음.</summary>
        public bool ByAi;
        public AiReason Reason;
        public Maintenance Maint;
        public HostOpKind HostOp;
        public int HostSeat = -1;

        public static Command AnswerTo(int choiceId, ChoiceAnswer answer) => new Command { Kind = CommandKind.Answer, ChoiceId = choiceId, Answer = answer };

        public static Command AiAnswer(int choiceId, ChoiceAnswer answer, AiReason reason) =>
            new Command { Kind = CommandKind.Answer, ChoiceId = choiceId, Answer = answer, ByAi = true, Reason = reason };

        public static Command TimeoutOf(int choiceId) => new Command { Kind = CommandKind.Timeout, ChoiceId = choiceId };

        public static Command Anytime(Maintenance m) => new Command { Kind = CommandKind.Anytime, Maint = m };

        public static Command Host(HostOpKind op, int seat) => new Command { Kind = CommandKind.HostOp, HostOp = op, HostSeat = seat };

        public static Command ProposeTrade(ChoiceAnswer offer) => new Command { Kind = CommandKind.Propose, Answer = offer };
    }

    /// <summary>거절 이유(01 §3-1 ①). 거절은 이벤트가 아니다(04 12행). NoChange는 세션이 화면에 알리지 않는다(G8).</summary>
    public enum RejectReason
    {
        None,
        /// <summary>그 Id의 열린 선택이 없다(이미 처리됐거나 ChoiceReissued로 바뀜).</summary>
        NoSuchChoice,
        /// <summary>답할 좌석이 아니다. Timeout·HostOp는 SeatId.System만.</summary>
        NotYourChoice,
        /// <summary>묶음에서 이미 답했다.</summary>
        AlreadyAnswered,
        /// <summary>답이 Legal 밖이거나 정비 대상·값이 그 행동 한 번으로 갈 수 없다.</summary>
        Illegal,
        /// <summary>정비 새 값 = 지금 값(rulings G8). 로그·Seq·이벤트가 없다.</summary>
        NoChange,
        /// <summary>정비 시점 등급(01 §3-4)에 맞지 않는다.</summary>
        Timing,
        /// <summary>명령 모양이 틀렸다(빈 답, 좌석 범위 밖 등).</summary>
        BadCommand,
        GameOver,
        /// <summary>각본 튜토리얼이 이 단계에서 받지 않는 명령(07 §6-3 — 엔진에 가지 않고 로그에 남지 않는다). 세션의 거르개만 낸다.</summary>
        Tutorial,
        /// <summary>방이 일시정지 중이라 답(Answer·Propose)을 받지 않는다(02 §4-5 ⑤ E_PAUSED — 정비는 받는다). 방(HostSession)만 낸다.</summary>
        Paused,
        /// <summary>그 좌석이 범위 안인 차단 컷신(until-end)이 아직 그 좌석 화면에서 돌고 있어 답(Answer)을 받지 않는다(02 §5-7 E_HOLD — 손님은 고른 것을 그대로 두고 다시 누른다). 방(HostSession)만 낸다.</summary>
        Hold,
        /// <summary>판이 되돌려진 뒤에 닿은, 되돌리기 전에 낸 명령이다(결정 149 — 명령의 Epoch가 세션보다 작다). 방(HostSession)만 낸다.</summary>
        Rewound,
    }

    /// <summary>Submit 결과(01 §3-1): Accepted | Rejected(이유).</summary>
    public readonly struct SubmitResult
    {
        public readonly bool Accepted;
        public readonly RejectReason Reason;

        SubmitResult(bool accepted, RejectReason reason)
        {
            Accepted = accepted;
            Reason = reason;
        }

        public static SubmitResult Ok => new SubmitResult(true, RejectReason.None);

        public static SubmitResult Rejected(RejectReason reason) => new SubmitResult(false, reason);

        public override string ToString() => Accepted ? "Accepted" : $"Rejected({Reason})";
    }
}
