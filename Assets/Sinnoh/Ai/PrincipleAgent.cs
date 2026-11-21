using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Ai
{
    /// <summary>
    /// p.25 원칙 AI(01 §9-6, rulings A16·G15). 결정적이고 빠르다 — 갤럭시단 조종(강한 AI는 M8), 야생·UB 기술 선택, 시간 초과 기본 기술과 같은 함수를 쓴다.
    /// 기술 = PrincipleRules.Move, 출전·다음 포켓몬 = 상대 출전 포켓몬에게 원칙 값이 가장 큰 기술을 가진 것, 교체 창 = 안 함,
    /// 우선권 토큰·은하의 응징 = 쓴다, 그 밖 = 그 선택의 Default(G9). 보기 사본만 받는다(호스트 원본이면 예외).
    /// </summary>
    public sealed class PrincipleAgent : IAgent
    {
        public AgentDecision Decide(AgentInput input, AgentBudget budget)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.View == null || input.Choice == null || input.Ctx == null) throw new ArgumentException("View·Choice·Ctx가 있어야 한다", nameof(input));
            if (input.View.Viewer == SeatId.HostViewer)
                throw new InvalidOperationException("AI는 보기 사본(ViewFor)만 본다 — 호스트 원본을 받았다(01 §9-1, rulings G15)");
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(input.View, input.Ctx, input.Choice, legal);
            return Choose(input.View, input.Ctx, input.Choice, legal);
        }

        /// <summary>
        /// 원칙 답(Decide의 속) — legal = 그 선택의 합법 답. 상태의 공개 칸만 읽어 보기 사본과 채운 판(M8 Determinizer — 굴리기 정책 <see cref="Rollout"/>)에서 같은 답을 낸다.
        /// </summary>
        public static AgentDecision Choose(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            if (legal.Count == 0) throw new InvalidOperationException($"{c.Kind} 선택 {c.Id}: 합법 답이 없다");

            switch (c.Kind)
            {
                case ChoiceKind.BattleAction:
                {
                    int k = ChoiceRules.BattleSideOf(s, c.Seat);
                    var b = s.Battle;
                    int def = b.Sides[1 - k].Active;
                    var want = PrincipleRules.Answer(ctx, s.Pokemon[b.Sides[k].Active], def >= 0 ? s.Pokemon[def] : null, out var why);
                    return Pick(legal, want, why);
                }
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                    return BestMatchup(s, ctx, c, legal);
                case ChoiceKind.SwitchWindow:
                    return Pick(legal, ChoiceAnswer.Of(AnswerKind.Decline), null);
                case ChoiceKind.Reaction when c.Sub == ChoiceSub.InitiativeToken || c.Sub == ChoiceSub.Retribution:
                    return Pick(legal, ChoiceAnswer.Of(AnswerKind.Accept), null);
                case ChoiceKind.Reaction when c.Sub == ChoiceSub.EffectOrder:
                    return Pick(legal, EffectRules.BestOrder(s, ctx, legal), null); // 결정 72 [가정]: 봇은 공격자에게 가장 유리한 순서(쓰러뜨림 → 피해)
                case ChoiceKind.Reaction when c.Sub == ChoiceSub.Fate:
                    return Pick(legal, TrainerRules.PsychicBest(legal) ?? TrainerRules.AmpBest(legal) ?? c.Default, null); // 결정 131 [가정 §5 #665]: 좌석 봇은 굴린 뒤 창의 psychic R을 쓴다(심볼 → 크리티컬, 없으면 일반 → 크리티컬) · 결정 134 [가정 §5 #618]: 앰프는 쓴다(두 배가 있으면 두 배) — 운명 카드는 그대로 기본(안 씀)
                default:
                    return Pick(legal, c.Default, null);
            }
        }

        /// <summary>내보낼 수 있는 포켓몬 중 상대 출전 포켓몬에게 원칙 값이 가장 큰 기술을 가진 것(동점이면 Legal 순서 = 명단·파티 순서).</summary>
        static AgentDecision BestMatchup(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            int k = ChoiceRules.BattleSideOf(s, c.Seat);
            int foe = k >= 0 ? s.Battle.Sides[1 - k].Active : -1;
            var defender = foe >= 0 ? s.Pokemon[foe] : null;
            ChoiceAnswer best = null;
            long bestValue = long.MinValue;
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Pick || a.Target < 0) continue;
                long v = PrincipleRules.BestValue(ctx, s.Pokemon[a.Target], defender);
                if (v > bestValue)
                {
                    best = a;
                    bestValue = v;
                }
            }
            return Pick(legal, best ?? legal[0], null);
        }

        /// <summary>답이 Legal 안이면 그것, 아니면 Legal 첫 답(G9) — AI 답은 늘 합법이다.</summary>
        static AgentDecision Pick(List<ChoiceAnswer> legal, ChoiceAnswer want, AiReason why)
        {
            if (want != null)
                foreach (var a in legal)
                    if (a.SameAs(want)) return new AgentDecision(a.Clone(), why);
            return new AgentDecision(legal[0].Clone(), why);
        }
    }
}
