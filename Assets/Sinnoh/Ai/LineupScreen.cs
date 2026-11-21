using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>
    /// 명단 거르기(M8 「선출 · 순서」 — 01 §13-1 M8-22): 굴리기 없이 원칙 값(<see cref="PrincipleRules.BestValue"/>)으로 명단 후보마다 빠른 점수를 매긴다.
    /// 포켓몬 f 하나의 점수 = 「쓰러지기 전에 도전자 파티를 몇 마리 쓰러뜨리나」(천분율) = f 최대 체력 × Σ_p (f가 p에게 주는 값 ÷ p 남은 체력) ÷ Σ_p (p가 f에게 주는 값)
    /// — 도전자 파티(열린 칸 · 체력 &gt; 0) 평균 상대의 「턴당 쓰러뜨리는 몫 × 버티는 턴」. 명단 점수 = 고른 포켓몬 점수의 합(같은 수를 고르므로 합으로 견준다).
    /// 공개 칸만 읽는다(보기 · 채운 판에서 같은 값). 선공 · 상태이상 · 특성 · 플랜 B · 은하의 응징은 넣지 않는다 — 그것은 깊이 단계(굴리기)가 본다.
    /// </summary>
    public static class LineupScreen
    {
        /// <summary>명단 후보(legal — ChoiceKind.Lineup의 합법 답)마다 점수를 into에 적는다(길이 ≥ legal.Count).</summary>
        public static void Scores(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal, long[] into)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (c == null || c.Kind != ChoiceKind.Lineup) throw new ArgumentException("명단 고르기가 아니다", nameof(c));
            var st = s.Seats[c.Subject];
            var foes = new List<PokemonState>();
            for (int i = 0; i < st.Party.Length; i++)
            {
                int id = st.Party[i];
                if (id < 0 || !PokemonRules.Fights(st, i)) continue;
                var p = s.Pokemon[id];
                if (p.Def >= 0 && PokemonRules.Hp(ctx, p) > 0) foes.Add(p);
            }
            var mon = new long[c.Options.Count];
            for (int o = 0; o < mon.Length; o++) mon[o] = MonScore(ctx, new PokemonState { Def = c.Options[o], Owner = SeatId.Galactic }, foes);
            for (int i = 0; i < legal.Count; i++)
            {
                long v = 0;
                foreach (int o in legal[i].Values) v += mon[o];
                into[i] = v;
            }
        }

        /// <summary>포켓몬 하나의 점수(천분율 — 쓰러지기 전에 쓰러뜨리는 도전자 포켓몬 수 × 1000). 상대가 없으면 0.</summary>
        public static long MonScore(Ctx ctx, PokemonState mon, List<PokemonState> foes)
        {
            if (foes.Count == 0) return 0;
            long kill = 0, take = 0;
            foreach (var p in foes)
            {
                kill += Math.Max(0, PrincipleRules.BestValue(ctx, mon, p)) * 1000 / Math.Max(1, PokemonRules.Hp(ctx, p));
                take += Math.Max(0, PrincipleRules.BestValue(ctx, p, mon));
            }
            return PokemonRules.MaxHp(ctx, mon) * kill / Math.Max(1, take);
        }

        /// <summary>점수가 큰 앞 keep개의 자리(같으면 Legal 순서 — 앞 답 먼저)를 into에 Legal 순서로 담는다.</summary>
        public static void Top(long[] scores, int count, int keep, List<int> into)
        {
            into.Clear();
            if (keep >= count)
            {
                for (int i = 0; i < count; i++) into.Add(i);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                int better = 0;
                for (int j = 0; j < count && better < keep; j++)
                    if (scores[j] > scores[i] || (scores[j] == scores[i] && j < i)) better++;
                if (better < keep) into.Add(i);
            }
        }
    }
}
