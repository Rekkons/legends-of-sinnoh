using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>굴리기 한 번의 결과(배틀 하나). 체력은 BattleEnded 때(귀환·회복 전) 좌석 쪽 파티의 값이다.</summary>
    public sealed class RolloutResult
    {
        /// <summary>배틀이 끝났다(BattleEnded를 봤다).</summary>
        public bool Ended;
        public BattleResult Result;
        /// <summary>이긴 쪽(배틀의 Side 0·1). 무승부·도주면 −1.</summary>
        public int Winner = -1;
        /// <summary>넣은 답 수.</summary>
        public int Steps;
        /// <summary>끝나기 전에 멈췄다: 열린 선택이 없거나(미뤄 둔 선택은 보기에 없다) 답이 거절됐거나 걸음 한도를 넘었다.</summary>
        public bool Stuck;
        /// <summary>깊이 제한(배틀 턴 수)에서 끊었다 — 판은 끊은 자리 그대로, 체력은 그때 값(<see cref="Rollout.ToEnd"/>의 turns).</summary>
        public bool Cut;
        /// <summary>쪽마다 좌석 파티의 남은 체력 합과 최대 체력 합(가상 쪽은 0 — 카드가 이미 덱으로 돌아갔다).</summary>
        public readonly int[] Hp = new int[BattleState.SideCount];
        public readonly int[] MaxHp = new int[BattleState.SideCount];
        /// <summary>쪽마다 좌석 파티의 포켓몬 수와 그중 쓰러진 수(가상 쪽은 0 — 결정 143 ①의 쓰러뜨린 몫).</summary>
        public readonly int[] Members = new int[BattleState.SideCount];
        public readonly int[] Fainted = new int[BattleState.SideCount];
        /// <summary>
        /// 이번 배틀 턴(굴리기를 시작한 판의 턴 — <see cref="Rollout.BaseTurn"/>)이 끝난 때 쪽마다 좌석 파티의 남은 체력(결정 143 ①의 이번 공격 몫 — 다음 턴이 열린 첫 걸음 · 그 전에 배틀이 끝나면 끝난 때).
        /// <see cref="TurnFilled"/>가 아니면(끊기거나 멈춤) 0.
        /// </summary>
        public readonly int[] TurnHp = new int[BattleState.SideCount];
        /// <summary>그때(<see cref="TurnHp"/>를 잰 때)의 최대 체력 합 — 이번 턴 비율은 같은 때 값으로 나눈다(폼이 배틀 끝에 되돌아가 최대 체력이 바뀌어도).</summary>
        public readonly int[] TurnMaxHp = new int[BattleState.SideCount];
        public bool TurnFilled;

        /// <summary>이번 배틀 턴이 끝난 때 그 쪽 좌석 파티의 남은 체력 비율(천분율 0~1000 — 최대 체력 합도 그때 값). 좌석이 아니면 0.</summary>
        public int TurnHpPermille(int side) => Permille(TurnHp[side], TurnMaxHp[side]);

        /// <summary>그 쪽 좌석 파티의 남은 체력 비율(천분율 0~1000 — 규칙·AI는 정수만 쓴다). 좌석이 아니면 0.</summary>
        public int HpPermille(int side) => Permille(Hp[side], MaxHp[side]);

        /// <summary>그 쪽 좌석 파티에서 쓰러진 비율(천분율 0~1000). 좌석이 아니면 0.</summary>
        public int FaintedPermille(int side) => Permille(Fainted[side], Members[side]);

        /// <summary>천분율을 0~1000으로 묶는다(점수의 몫이 음수 · 상한 밖으로 가지 않게 — 결정 143 ①).</summary>
        static int Permille(int part, int whole) => whole > 0 ? Math.Max(0, Math.Min(1000, part * 1000 / whole)) : 0;
    }

    /// <summary>굴리기 정책: 열린 선택 하나의 답(legal 안에서). 판은 채운 판이거나 호스트 원본이다.</summary>
    public delegate ChoiceAnswer RolloutPolicy(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal);

    /// <summary>
    /// 배틀 시뮬레이션 틀(01 §9-3, M8): 채운 판(<see cref="Determinizer"/>)에 실제 엔진을 그대로 돌린다 — 시뮬레이션 전용 규칙 사본이 없다.
    /// <see cref="Step"/> = 한 수 앞(답 하나를 넣는다), <see cref="ToEnd"/> = 지금 배틀(또는 곧 열릴 배틀 — 명단 고르기)이 끝날 때까지 정책으로 굴린다.
    /// 결과는 이벤트 없이 배틀을 닫는 자리에서 읽는다(<see cref="IBattleEndWatch"/>, 01 §13 M8-3).
    /// </summary>
    public static class Rollout
    {
        /// <summary>걸음 한도(배틀 하나는 보통 수십 걸음 — 넘으면 <see cref="RolloutResult.Stuck"/>).</summary>
        public const int MaxSteps = 2000;

        static readonly AiReason NoReason = new AiReason(AiRule.None, 0, 0);

        /// <summary>두 쪽 모두 p.25 원칙(<see cref="PrincipleAgent.Choose"/>)으로 답하는 정책.</summary>
        public static readonly RolloutPolicy Principle = (s, ctx, c, legal) => PrincipleAgent.Choose(s, ctx, c, legal).Answer;

        /// <summary>
        /// 상대 모델의 굴리기 정책(M8 「탐색」 — 01 §13-1 M8-19): 원칙(<see cref="Principle"/>)에 좌석의 배틀 수를 더한다 —
        /// ① 운명 반응 창(굴린 뒤)에서 디아루가 1장: 공격하는 쪽이면 지운 주사위 · 상태 심볼 하나, 받는 쪽이면 크리티컬 하나를 왼쪽부터(배틀 종류와 상관없이 — 갤럭시단 배틀에서 아끼지 않는다).
        /// ② 교체 창: 출전 포켓몬 남은 체력이 1/3 밑이고 벤치에 체력 60% 이상이 있으면 그중 남은 체력이 가장 큰 것으로.
        /// 우선권 토큰 · 은하의 응징은 원칙 그대로(쓴다). 기라티나 · 3장 효과는 내지 않는다. 가상 쪽(갤럭시단)은 원칙 그대로다.
        /// 같은 창의 psychic R(결정 131) · 앰프(결정 134)는 디아루가를 내지 않을 때 원칙 그대로(<see cref="TrainerRules.PsychicBest"/> — 심볼 · 일반 → 크리티컬 · <see cref="TrainerRules.AmpBest"/> — 쓴다, 두 배가 있으면 두 배).
        /// </summary>
        public static readonly RolloutPolicy Rival = (s, ctx, c, legal) =>
        {
            if (c.Seat >= 0 && s.Battle != null && legal.Count > 1)
            {
                if (c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Fate)
                {
                    var fate = Dialga(s, c, legal);
                    if (fate != null) return fate;
                }
                else if (c.Kind == ChoiceKind.SwitchWindow)
                {
                    var swap = Switch(s, ctx, c, legal);
                    if (swap != null) return swap;
                }
            }
            return PrincipleAgent.Choose(s, ctx, c, legal).Answer;
        };

        static ChoiceAnswer Dialga(GameState s, PendingChoice c, List<ChoiceAnswer> legal)
        {
            var b = s.Battle;
            if (c.Window != Window.AfterRoll || b.ActiveSide < 0) return null;
            bool attacking = b.Sides[b.ActiveSide].Owner == c.Seat;
            for (int f = 0; f < (attacking ? 2 : 1); f++)
            {
                var face = attacking ? (f == 0 ? AttackDieFace.None : AttackDieFace.StatusSymbol) : AttackDieFace.CriticalHit;
                int i = b.Faces.IndexOf(face);
                if (i < 0) continue;
                foreach (var a in legal)
                    if (a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga && a.Arg == 1 && a.Values.Count > 0 && a.Values[0] == i) return a;
            }
            return null;
        }

        /// <summary>교체 창에서 바꿀 남은 체력 비(‰) — 출전 쪽 상한 · 벤치 쪽 하한.</summary>
        const int SwitchBelow = 333, SwitchTo = 600;

        static ChoiceAnswer Switch(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            int k = ChoiceRules.BattleSideOf(s, c.Seat);
            if (k < 0) return null;
            int act = s.Battle.Sides[k].Active;
            if (act < 0 || Permille(ctx, s.Pokemon[act]) >= SwitchBelow) return null;
            ChoiceAnswer best = null;
            int bestHp = -1;
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick || a.Target < 0) continue;
                var p = s.Pokemon[a.Target];
                int hp = PokemonRules.Hp(ctx, p);
                if (Permille(ctx, p) < SwitchTo || hp <= bestHp) continue;
                best = a;
                bestHp = hp;
            }
            return best;
        }

        static int Permille(Ctx ctx, PokemonState p)
        {
            int max = PokemonRules.MaxHp(ctx, p);
            return max > 0 ? PokemonRules.Hp(ctx, p) * 1000 / max : 0;
        }

        /// <summary>한 수: 그 선택에 답을 넣는다(가상 응답자면 AI 답, 좌석이면 좌석 답). 거절이면 false(판은 그대로).</summary>
        public static bool Step(GameState sim, Ctx ctx, PendingChoice c, ChoiceAnswer a, IEventSink sink = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (a == null) throw new ArgumentNullException(nameof(a));
            var cmd = c.Seat < 0 ? Command.AiAnswer(c.Id, a, NoReason) : Command.AnswerTo(c.Id, a);
            return Engine.Submit(sim, ctx, c.Seat, cmd, sink).Accepted;
        }

        /// <summary>
        /// 정책으로 배틀 끝까지. first = 먼저 넣을 답(선택 Id + 답 — 탐색이 고르는 수), 없으면 바로 정책.
        /// 이벤트를 만들지 않는다(<see cref="IBattleEndWatch"/> — 01 §13 M8-3). events = true면 밑작업의 옛 길(이벤트를 모두 만들고 BattleEnded에서 읽음 — 견줌 시험용).
        /// turns &gt; 0 = 깊이 제한(01 §13-1 M8-15): 배틀 턴이 turns번 지나 다음 배틀 턴이 열린 첫 선택에서 끊는다(<see cref="RolloutResult.Cut"/> — 판은 그 자리, 평가는 부르는 쪽).
        /// 처음 판의 배틀 턴(없거나 0이면 1)부터 센다 — 배틀 행동 선택에서 끊으면 그 사이 공격 turns번이 끝났다. 끝까지 굴리기는 turns = 0(그 전 길 그대로).
        /// </summary>
        public static RolloutResult ToEnd(GameState sim, Ctx ctx, RolloutPolicy policy, int firstChoice = -1, ChoiceAnswer first = null, int maxSteps = MaxSteps, bool events = false, int turns = 0)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            var end = new EndSink(ctx);
            IEventSink sink = end;
            if (events)
            {
                var old = new EventEndSink(sim, ctx);
                sink = old;
                end = old.End;
            }
            var r = end.Result;
            var legal = new List<ChoiceAnswer>();
            int baseTurn = BaseTurn(sim);
            int cutAt = turns > 0 ? baseTurn + turns : int.MaxValue;
            if (first != null)
            {
                var c = sim.Pending.Find(x => x.Id == firstChoice);
                if (c == null || !Step(sim, ctx, c, first, sink))
                {
                    r.Stuck = true;
                    return r;
                }
                r.Steps++;
            }
            while (!r.Ended)
            {
                if (r.Steps >= maxSteps)
                {
                    r.Stuck = true;
                    break;
                }
                if (!r.TurnFilled && sim.Battle != null && sim.Battle.Turn > baseTurn) end.FillTurn(sim, sim.Battle);
                if (sim.Battle != null && sim.Battle.Turn >= cutAt)
                {
                    r.Cut = true;
                    end.Fill(sim, sim.Battle);
                    break;
                }
                PendingChoice c = null;
                foreach (var x in sim.Pending)
                    if (!x.Answered)
                    {
                        c = x;
                        break;
                    }
                if (c == null)
                {
                    r.Stuck = true;
                    break;
                }
                legal.Clear();
                Engine.Legal(sim, ctx, c, legal);
                if (legal.Count == 0 || !Step(sim, ctx, c, policy(sim, ctx, c, legal), sink))
                {
                    r.Stuck = true;
                    break;
                }
                r.Steps++;
            }
            return r;
        }

        /// <summary>깊이를 세기 시작하는 배틀 턴: 지금 배틀의 턴(배틀이 없거나 시작 전이면 1 — 첫 공격이 배틀 턴 1).</summary>
        public static int BaseTurn(GameState s) => s.Battle != null && s.Battle.Turn > 1 ? s.Battle.Turn : 1;

        /// <summary>배틀 끝의 결과와 그 순간(귀환·회복 전) 좌석 쪽 파티 체력. 이벤트 없이(IBattleEndWatch — 기본) 받는다.</summary>
        sealed class EndSink : IBattleEndWatch
        {
            readonly Ctx ctx;
            public readonly RolloutResult Result = new RolloutResult();

            public EndSink(Ctx ctx) => this.ctx = ctx;

            public void Add(GameEvent e) { }

            public void BattleOver(GameState s, BattleState b, BattleResult result, int winner)
            {
                if (Result.Ended) return;
                Result.Ended = true;
                Result.Result = result;
                Result.Winner = winner;
                Fill(s, b);
                if (!Result.TurnFilled) FillTurn(s, b);
            }

            /// <summary>
            /// 쪽마다 좌석 파티의 남은 체력 · 최대 체력 · 포켓몬 수 · 쓰러진 수(가상 쪽은 0). 이번 배틀에 잡은 상대 포켓몬(상대 명단에 있는 것 — 잡으면 바로 파티로 온다)은 세지 않는다:
            /// 세면 잡힘이 「좌석 파티가 체력을 잃음」으로 보여 야생 쪽이 잡힘을 쓰러짐보다 높게 쳤다(결정 143 ① 고침).
            /// </summary>
            public void Fill(GameState s, BattleState b)
            {
                for (int k = 0; k < BattleState.SideCount; k++)
                {
                    int owner = b.Sides[k].Owner;
                    if (owner < 0 || owner >= s.SeatCount) continue;
                    foreach (int id in s.Seats[owner].Party)
                    {
                        if (!Counted(s, b, k, id)) continue;
                        int hp = PokemonRules.Hp(ctx, s.Pokemon[id]);
                        Result.Hp[k] += hp;
                        Result.MaxHp[k] += PokemonRules.MaxHp(ctx, s.Pokemon[id]);
                        Result.Members[k]++;
                        if (hp <= 0) Result.Fainted[k]++;
                    }
                }
            }

            /// <summary>이번 배틀 턴이 끝난 때(다음 턴이 열렸거나 배틀이 끝남) 쪽마다 좌석 파티의 남은 체력 · 최대 체력(<see cref="RolloutResult.TurnHp"/> — 세는 포켓몬은 <see cref="Fill"/>과 같다).</summary>
            public void FillTurn(GameState s, BattleState b)
            {
                Result.TurnFilled = true;
                for (int k = 0; k < BattleState.SideCount; k++)
                {
                    int owner = b.Sides[k].Owner;
                    if (owner < 0 || owner >= s.SeatCount) continue;
                    foreach (int id in s.Seats[owner].Party)
                    {
                        if (!Counted(s, b, k, id)) continue;
                        Result.TurnHp[k] += PokemonRules.Hp(ctx, s.Pokemon[id]);
                        Result.TurnMaxHp[k] += PokemonRules.MaxHp(ctx, s.Pokemon[id]);
                    }
                }
            }

            /// <summary>쪽 k 좌석 파티의 그 칸을 센다: 빈 칸 · 빈 인스턴스가 아니고 상대 명단(이번 배틀에서 잡은 포켓몬)에 없다.</summary>
            static bool Counted(GameState s, BattleState b, int k, int id) => id >= 0 && s.Pokemon[id].Def >= 0 && !b.Sides[1 - k].Roster.Contains(id);
        }

        /// <summary>견줌용 옛 길(밑작업): 이벤트를 모두 만들고 BattleEnded 이벤트에서 결과를 읽는다 — 이벤트 없는 길과 결과·판이 같아야 한다(시험 M8_굴리기는이벤트없이도…).</summary>
        sealed class EventEndSink : IEventSink
        {
            readonly GameState s;
            public readonly EndSink End;

            public EventEndSink(GameState s, Ctx ctx)
            {
                this.s = s;
                End = new EndSink(ctx);
            }

            public void Add(GameEvent e)
            {
                if (e is BattleEnded end && s.Battle != null) End.BattleOver(s, s.Battle, end.Result, end.Winner);
            }
        }
    }
}
