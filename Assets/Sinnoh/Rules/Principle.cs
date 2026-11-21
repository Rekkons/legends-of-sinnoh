using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// p.25 원칙 AI의 기술 고르기(01 §9-6, rulings A16·G15). 시간 초과 BattleAction의 Default(01 §3-3)가 이것을 써서 Rules에 둔다 —
    /// Sinnoh.Ai의 PrincipleAgent가 감싸 갤럭시단·야생·UB 조종에 쓴다. 포켓몬 상태만 읽어(보기 사본에서도 같다) 숨은 정보와 무관하다.
    /// </summary>
    public static class PrincipleRules
    {
        /// <summary>값의 분모 6^9(01 §6-3). 공격력 n의 기대값을 6^n 분모로 센 뒤 6^(9−n)을 곱해 맞춘다.</summary>
        public const long Scale = 10077696;

        /// <summary>그 기술로 굴릴 주사위 수: 기술 공격력(「공격력 = n」 효과가 지금 맞으면 n, G14) → 1~9로 자름. 배틀 상시 보정은 넣지 않는다(G15).</summary>
        public static int Strength(Ctx ctx, int move, PokemonState attacker, PokemonState defender)
        {
            int strength = BattleRules.MoveStrength(ctx, move);
            // 알파 스트라이크: 그 포켓몬이 직전 턴에 썼으면 2(rulings A26 — 「공격력 = n」과 같은 자리)
            if (move == BattleRules.AlphaStrike && attacker != null && attacker.LastMove == BattleRules.AlphaStrike) strength = BattleRules.AlphaStrikeRepeatStrength;
            foreach (int id in ctx.Effects.OfMove(move))
            {
                var e = ctx.Effects.Effects[id];
                if (e.Window != Window.BeforeRoll || !e.IsStatic) continue;
                if (e.When != null && !EffectRules.Holds(ctx, e.When, attacker, defender, 0, move)) continue;
                foreach (var o in e.Ops)
                    if (o.Op == EffectOp.StrengthTo) strength = o.Value;
            }
            return DamageRules.AttackStrength(strength, 0);
        }

        /// <summary>
        /// 원칙 값 × <see cref="Scale"/> = E[(주사위 눈 합 + 효과 피해 + 상성) × (2배면 2)](G15). 효과 피해는 상시 효과(추가 피해·면당 피해·2배) 중
        /// 대상 조건이 지금 맞는 것, 주사위 조건은 (s, r, c) 묶음마다 본다. 방어 쪽이 없으면 상성 0.
        /// </summary>
        public static long Value(Ctx ctx, int move, PokemonState attacker, PokemonState defender)
        {
            int n = Strength(ctx, move, attacker, defender);
            int type = 0;
            if (defender != null && defender.Def >= 0)
            {
                var types = ctx.Cards.Pokemon[defender.Def].Types;
                type = TypeChart.Bonus(TypeOf(ctx, move, attacker, defender), types[0], types.Count > 1 ? types[1] : PokemonType.Typeless);
            }
            long sum = 0;
            foreach (var o in AttackDice.Outcomes(n)) sum += (long)o.Weight * OutcomeValue(ctx, move, attacker, defender, o, type);
            return sum * (Scale / AttackDice.TotalWeight(n));
        }

        /// <summary>원칙 값에 쓰는 기술 타입: 카드 타입, 알파 스트라이크는 <see cref="AlphaType"/>.</summary>
        static PokemonType TypeOf(Ctx ctx, int move, PokemonState attacker, PokemonState defender) =>
            move == BattleRules.AlphaStrike && attacker != null ? (PokemonType)AlphaType(ctx, attacker, defender) : BattleRules.MoveType(ctx, move);

        /// <summary>
        /// 원칙 AI가 고르는 알파 스트라이크 타입(rulings A26 · G15 「가장 큰 값」): 받는 포켓몬에게 상성 값이 가장 큰 배울 수 있는 타입, 같으면 카드 순서. 받는 쪽이 없으면 첫 타입.
        /// </summary>
        public static int AlphaType(Ctx ctx, PokemonState attacker, PokemonState defender)
        {
            var options = new List<int>(4);
            LegendRules.AlphaTypes(ctx, attacker, options);
            if (options.Count == 0) return (int)PokemonType.Typeless;
            if (defender == null || defender.Def < 0) return options[0];
            var types = ctx.Cards.Pokemon[defender.Def].Types;
            var t2 = types.Count > 1 ? types[1] : PokemonType.Typeless;
            int best = options[0], bestV = int.MinValue;
            foreach (int t in options)
            {
                int v = TypeChart.Bonus((PokemonType)t, types[0], t2);
                if (v > bestV)
                {
                    best = t;
                    bestV = v;
                }
            }
            return best;
        }

        /// <summary>원칙 기술의 답(<see cref="Move"/> — 알파 스트라이크면 <see cref="AlphaType"/>을 실은 답, ChoiceRules.AlphaStrikeAnswer).</summary>
        public static ChoiceAnswer Answer(Ctx ctx, PokemonState attacker, PokemonState defender, out AiReason reason)
        {
            int move = Move(ctx, attacker, defender, out reason);
            return move == BattleRules.AlphaStrike ? ChoiceRules.AlphaStrikeAnswer(AlphaType(ctx, attacker, defender)) : ChoiceAnswer.Of(AnswerKind.UseMove, move);
        }

        static int OutcomeValue(Ctx ctx, int move, PokemonState attacker, PokemonState defender, DiceOutcome o, int type)
        {
            int v = o.Sum + type;
            bool doubled = false;
            foreach (int id in ctx.Effects.OfMove(move))
            {
                var e = ctx.Effects.Effects[id];
                if (e.Window != Window.BeforeDamage || !e.IsStatic) continue;
                if (e.When != null && !EffectRules.Holds(ctx, e.When, attacker, defender, o.StatusSymbols, move)) continue;
                foreach (var op in e.Ops)
                {
                    if (op.Op == EffectOp.ExtraDamage) v += op.Value;
                    else if (op.Op == EffectOp.FaceDamage) v += op.Value * Count(o, op.Face);
                    else if (op.Op == EffectOp.Double) doubled = true;
                }
            }
            return doubled ? 2 * v : v;
        }

        static int Count(DiceOutcome o, AttackDieFace face) =>
            face == AttackDieFace.StatusSymbol ? o.StatusSymbols : face == AttackDieFace.RegularHit ? o.RegularHits : face == AttackDieFace.CriticalHit ? o.CriticalHits : 0;

        /// <summary>
        /// 그 기술의 실행 효과가 지금 드나(G15 「의미 있음」): 대상 상태이상 Applied · 자기 회복(피해 있음, 독 아님) · 자기 상태이상 풀기 · 대상 피로 5 미만.
        /// 주사위 조건은 나올 수 있으면(공격력 ≥ 심볼 수) 든다고 본다.
        /// </summary>
        public static bool EffectWorks(Ctx ctx, int move, PokemonState attacker, PokemonState defender)
        {
            int n = Strength(ctx, move, attacker, defender);
            foreach (int id in ctx.Effects.OfMove(move))
            {
                var e = ctx.Effects.Effects[id];
                if (e.IsStatic) continue;
                if (e.When != null && !EffectRules.Holds(ctx, e.When, attacker, defender, n, move)) continue;
                foreach (var op in e.Ops)
                {
                    bool self = op.Who == EffectWho.User || op.Who == EffectWho.Party;
                    switch (op.Op)
                    {
                        case EffectOp.Inflict:
                            if (op.Who == EffectWho.Target && defender != null && StatusRules.Check(ctx, defender, op.Status) == StatusResult.Applied) return true;
                            break;
                        case EffectOp.Heal:
                            if (self && attacker.Damage > 0 && !StatusRules.BlocksHealing(attacker)) return true;
                            break;
                        case EffectOp.CureStatus:
                            if (self && attacker.Status != StatusEffect.None && (op.Status == StatusEffect.None || op.Status == attacker.Status)) return true;
                            break;
                        case EffectOp.Fatigue:
                            if (op.Who == EffectWho.Target && defender != null && defender.Fatigue < FatigueRules.Max) return true;
                            break;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 원칙 기술(01 §9-6, G15): 시그니처 값이 최댓값 이상이거나 시그니처 효과가 지금 들면 시그니처, 아니면 값이 가장 큰 기술(동점이면 공격력 → 후보 순서).
        /// reason = 화면 자막(04 §5 ④, 표시 전용): Power = 주사위 수, Bonus = 값 − 공격력(0 쪽으로 자름). 플랜 B는 「공격력 3, 은하의 응징 +1」.
        /// </summary>
        public static int Move(Ctx ctx, PokemonState attacker, PokemonState defender, out AiReason reason)
        {
            var options = new List<int>(4);
            PokemonRules.MoveOptions(ctx, attacker, options);
            int sig = options[0];
            int best = -1, bestPower = int.MinValue;
            long bestValue = long.MinValue, sigValue = 0;
            foreach (int m in options)
            {
                long v = Value(ctx, m, attacker, defender);
                int pw = Strength(ctx, m, attacker, defender);
                if (m == sig) sigValue = v;
                if (v > bestValue || (v == bestValue && pw > bestPower))
                {
                    best = m;
                    bestValue = v;
                    bestPower = pw;
                }
            }
            int pick = sigValue >= bestValue || EffectWorks(ctx, sig, attacker, defender) ? sig : best;
            int power = Strength(ctx, pick, attacker, defender);
            long pickValue = pick == sig ? sigValue : bestValue;
            reason = pick == BattleRules.PlanB ? new AiReason(AiRule.PlanB, power, 1)
                : new AiReason(pick == sig ? AiRule.Signature : AiRule.BestValue, power, (int)((pickValue - power * Scale) / Scale));
            return pick;
        }

        /// <summary>그 포켓몬의 후보 기술 중 가장 큰 원칙 값(출전·다음 포켓몬 고르기, G15).</summary>
        public static long BestValue(Ctx ctx, PokemonState attacker, PokemonState defender)
        {
            var options = new List<int>(4);
            PokemonRules.MoveOptions(ctx, attacker, options);
            long best = long.MinValue;
            foreach (int m in options)
            {
                long v = Value(ctx, m, attacker, defender);
                if (v > best) best = v;
            }
            return best;
        }
    }
}
