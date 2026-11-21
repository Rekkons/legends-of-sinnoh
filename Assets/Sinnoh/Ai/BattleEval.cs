using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>
    /// 판세 평가의 항목(01 §9-5, M8 「판세 평가」). 값은 모두 「이 쪽 − 상대 쪽」이라 쪽을 뒤집으면 부호만 바뀐다(갤럭시단 ↔ 플레이어 대칭).
    /// 공개 칸만 읽는다(포켓몬 카드 · 체력 · 상태 · 피로 · 토큰 · 응징 · 프리즘 아머 · 운명 손패 장수) — 보기 사본 · 채운 판 · 호스트 원본에서 같은 값이다(엿보기 없음).
    /// </summary>
    public enum EvalFeature
    {
        /// <summary>명단의 남은 체력 비율(‰).</summary>
        HpPermille,
        /// <summary>살아 있는 수.</summary>
        Alive,
        /// <summary>남은 체력 합.</summary>
        Hp,
        /// <summary>출전끼리 상성: 내 출전 포켓몬이 상대 출전 포켓몬에게 주는 기대 피해(원칙 값 — 공격력 · 효과 피해 · 상성, 0.1 단위).</summary>
        Matchup,
        /// <summary>명단 화력: 살아 있는 (내 포켓몬, 상대 포켓몬) 짝의 기대 피해 평균(0.1 단위).</summary>
        Punch,
        /// <summary>버티기: 상대 화력으로 내 남은 체력을 바닥내는 데 드는 공격 수(0.1 단위, 1000에서 자름).</summary>
        Race,
        /// <summary>출전 포켓몬의 피로(0~5).</summary>
        Fatigue,
        /// <summary>살아 있는 포켓몬에 걸린 상태이상 수.</summary>
        Status,
        /// <summary>우선권 토큰(0·1).</summary>
        Initiative,
        /// <summary>은하의 응징 저장량.</summary>
        Retribution,
        /// <summary>프리즘 아머(울트라 비스트 쪽 배틀만 — 갤럭시단 배틀은 0).</summary>
        Prism,
        /// <summary>그 쪽 좌석의 운명 손패 장수(공개 값 — 가상 쪽은 0).</summary>
        Fate,
        /// <summary>출전 포켓몬의 우선도(보정 뒤 — BattleRules.OrderValue).</summary>
        Speed,
        /// <summary>지금 행동하는 쪽(0·1).</summary>
        ToMove,
        /// <summary>갤럭시단 쪽의 살아 있는 수(그 밖은 0) — 쓰러질 때마다 응징 2를 쌓고 플랜 B가 있는 쪽(태홍 6마리)의 몫.</summary>
        Galactic,
    }

    /// <summary>
    /// 판세 평가(01 §9-5, M8 「판세 평가」 — 결정 108 ②): 배틀 중인 판에서 그 쪽(Side 0·1)의 이길 확률(천분율 0~1000) = 500 + Σ 무게 × 항목 / <see cref="WeightScale"/>.
    /// 항목(<see cref="EvalFeature"/>)이 「이 쪽 − 상대 쪽」이고 상수항이 없어 두 쪽 값의 합은 늘 1000이다(대칭). 정수만.
    /// 무게는 손으로 정하지 않았다: 시험 쪽 맞추기(<c>StrongAiTests.M8_평가_무게맞추기</c> — 무거운 탐색(끝까지 굴리기 PIMC)의 굴리기가 지난 판의 항목 → 그 굴리기의 승패, 최소제곱)로 맞춘 표가 <see cref="Default"/>다 [가정 01 §13-1 M8-14].
    /// 원칙 값은 효과가 공격 타입을 고르는 기술(judgement 「type_any」 · hidden_power 류)을 받는 쪽에게 가장 좋은 타입으로 센다(가상 쪽이 실제로 그렇게 고른다 — 태홍의 아르세우스).
    /// 인스턴스는 스레드 하나에서 쓴다(셈 칸을 돌려 쓴다).
    /// </summary>
    public sealed class BattleEval
    {
        public const int FeatureCount = 15;
        /// <summary>무게의 분모(무게 1 = 항목 1에 천분의 1‰).</summary>
        public const int WeightScale = 1000;
        /// <summary>버티기 항목의 상한(공격 100번).</summary>
        public const int RaceCap = 1000;

        /// <summary>
        /// 맞춘 무게(<see cref="EvalFeature"/> 순서). 맞춘 판: 옛 정본(운영 규칙 끔) 실전 장면 1,000 · 갤럭시단 결정마다 굴리기 64(표본 16 이하) · 굴리기마다 배틀 턴 1~8의 판 — 숫자는 plan-log 「M8 판세 평가」.
        /// </summary>
        public static readonly int[] Default = { 406, 64111, 8189, -97, 6041, -54, -21569, -33274, 12493, 11222, 0, -24327, 3564, 37388, 40747 };

        /// <summary>
        /// 상대 모델 굴리기(<see cref="Rollout.Rival"/> — 좌석이 운명 카드 · 교체를 쓴다)에 맞춘 무게(M8 「탐색」 — 탐색 꼴 Expect · Tree의 기본, 01 §13-1 M8-20). 맞춘 판은 <see cref="Default"/>와 같고 굴리기 정책만 다르다
        /// (시험 <c>M8_탐색_상대모델무게맞추기</c>, SINNOH_EVAL_FIT_SCENES=1000 — 숫자는 plan-log 「M8 탐색」).
        /// 운명 손패(<see cref="EvalFeature.Fate"/>)만 맞춘 값(−18,976)을 버리고 0으로 둔다 [가정 01 §13-1 M8-20]: 음수 무게는 장면 사이 섞임이고, 배틀 안에서 이 값은 카드를 낼 때만 바뀌어
        /// 탐색이 「카드를 버리면 평가가 오른다」로 읽는다(플레이어 쪽 탐색이 운명 카드를 헛되이 냈다 — 판세 평가의 「플레이어 쪽 깊이 제한이 약해짐」도 같은 까닭).
        /// </summary>
        public static readonly int[] Rival = { 459, 52294, 7962, 53, 5615, 99, -19623, -34781, 14884, 12310, 0, 0, 3056, 40912, 52257 };

        public static readonly string[] Names = Enum.GetNames(typeof(EvalFeature));

        readonly int[] f = new int[FeatureCount];
        readonly int[][] side = { new int[FeatureCount], new int[FeatureCount] };
        readonly List<int>[] alive = { new List<int>(6), new List<int>(6) };
        readonly List<int> moves = new List<int>(4);
        readonly List<Mod> mods = new List<Mod>(4);

        /// <summary>그 쪽의 이길 확률(‰, 0~1000). weights = null이면 <see cref="Default"/>. 배틀이 없으면 500.</summary>
        public int WinPermille(GameState s, Ctx ctx, int k, int[] weights = null)
        {
            if (s.Battle == null) return 500;
            Features(s, ctx, k, f);
            return WinPermille(f, weights);
        }

        /// <summary>항목 값으로 이길 확률(‰). 나눗셈은 0 쪽으로 잘라 두 쪽 합이 1000이다.</summary>
        public static int WinPermille(int[] features, int[] weights = null)
        {
            var w = weights ?? Default;
            long raw = 0;
            for (int i = 0; i < FeatureCount; i++) raw += (long)w[i] * features[i];
            long e = 500 + raw / WeightScale;
            return e < 0 ? 0 : e > 1000 ? 1000 : (int)e;
        }

        /// <summary>항목 값(「k 쪽 − 상대 쪽」)을 into에 채운다. 배틀 중인 판이어야 한다.</summary>
        public void Features(GameState s, Ctx ctx, int k, int[] into)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Battle == null) throw new InvalidOperationException("배틀 중인 판이 아니다");
            Sides(s, ctx);
            for (int i = 0; i < FeatureCount; i++) into[i] = side[k][i] - side[1 - k][i];
        }

        /// <summary>쪽마다의 값(항목 차가 아니라 그 쪽 값 — 보고용). k 쪽 값을 into에.</summary>
        public void SideValues(GameState s, Ctx ctx, int k, int[] into)
        {
            Sides(s, ctx);
            Array.Copy(side[k], into, FeatureCount);
        }

        void Sides(GameState s, Ctx ctx)
        {
            var b = s.Battle;
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var v = side[k];
                Array.Clear(v, 0, FeatureCount);
                var sd = b.Sides[k];
                var list = alive[k];
                list.Clear();
                int hp = 0, max = 0;
                foreach (int id in sd.Roster)
                {
                    var p = s.Pokemon[id];
                    if (p.Def < 0) continue;
                    int h = PokemonRules.Hp(ctx, p);
                    hp += h;
                    max += PokemonRules.MaxHp(ctx, p);
                    if (h <= 0) continue;
                    list.Add(id);
                    v[(int)EvalFeature.Status] += Statuses(p);
                }
                v[(int)EvalFeature.HpPermille] = max > 0 ? hp * 1000 / max : 0;
                v[(int)EvalFeature.Alive] = list.Count;
                v[(int)EvalFeature.Hp] = hp;
                int act = sd.Active;
                bool up = act >= 0 && s.Pokemon[act].Def >= 0 && !PokemonRules.IsFainted(ctx, s.Pokemon[act]);
                if (up)
                {
                    v[(int)EvalFeature.Fatigue] = s.Pokemon[act].Fatigue;
                    v[(int)EvalFeature.Speed] = BattleRules.OrderValue(s, ctx, act, mods, out _);
                }
                v[(int)EvalFeature.Initiative] = sd.InitiativeToken ? 1 : 0;
                v[(int)EvalFeature.Retribution] = sd.Retribution;
                v[(int)EvalFeature.Prism] = Prism(s, b, sd);
                v[(int)EvalFeature.Fate] = sd.Owner >= 0 && sd.Owner < s.SeatCount ? s.Seats[sd.Owner].FateHand.Count : 0;
                v[(int)EvalFeature.ToMove] = b.ActiveSide == k ? 1 : 0;
                v[(int)EvalFeature.Galactic] = sd.Owner == SeatId.Galactic ? list.Count : 0;
            }
            // 화력 · 출전끼리 · 버티기(두 쪽 명단을 서로 본다)
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var mine = alive[k];
                var theirs = alive[1 - k];
                long sum = 0;
                foreach (int a in mine)
                    foreach (int d in theirs)
                        sum += Tenths(Value(ctx, s.Pokemon[a], s.Pokemon[d]));
                int pairs = mine.Count * theirs.Count;
                side[k][(int)EvalFeature.Punch] = pairs > 0 ? (int)(sum / pairs) : 0;
                int act = b.Sides[k].Active, foe = b.Sides[1 - k].Active;
                if (act >= 0 && foe >= 0 && mine.Contains(act) && theirs.Contains(foe))
                    side[k][(int)EvalFeature.Matchup] = (int)Tenths(Value(ctx, s.Pokemon[act], s.Pokemon[foe]));
            }
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                int punch = Math.Max(10, side[1 - k][(int)EvalFeature.Punch]);
                side[k][(int)EvalFeature.Race] = (int)Math.Min(RaceCap, (long)side[k][(int)EvalFeature.Hp] * 100 / punch);
            }
        }

        static long Tenths(long value) => Math.Max(0, value) * 10 / PrincipleRules.Scale;

        static int Statuses(PokemonState p)
        {
            int n = p.Status != StatusEffect.None ? 1 : 0;
            if (p.Status2 != StatusEffect.None) n++;
            for (int bits = p.StatusMore; bits != 0; bits &= bits - 1) n++;
            return n;
        }

        /// <summary>프리즘 아머(S17_4): 울트라 비스트 쪽(가상 쪽 UltraBeast)이 지키는 수 — 네크로즈마 배틀이면 울트라 버스트 쪽 몫. 그 밖 0 [가정 M8-14].</summary>
        static int Prism(GameState s, BattleState b, BattleSide sd)
        {
            if (s.Ub == null || sd.Owner != SeatId.UltraBeast) return 0;
            return b.Kind == BattleKind.Necrozma ? s.Ub.PrismUltraBurst : s.Ub.PrismUltraBeast;
        }

        /// <summary>
        /// 공격 쪽 기술 후보 중 가장 큰 원칙 값(<see cref="PrincipleRules.Value"/>, 분모 <see cref="PrincipleRules.Scale"/>). 효과가 공격 타입을 고르는 기술은
        /// 고를 수 있는 타입 중 받는 쪽에게 가장 좋은 상성으로 바꿔 센다(카드 타입 상성과의 차 × Scale — 2배 효과는 넣지 않는다).
        /// </summary>
        long Value(Ctx ctx, PokemonState attacker, PokemonState defender)
        {
            moves.Clear();
            PokemonRules.MoveOptions(ctx, attacker, moves);
            long best = long.MinValue;
            foreach (int m in moves)
            {
                long v = PrincipleRules.Value(ctx, m, attacker, defender) + TypePick(ctx, m, attacker, defender) * PrincipleRules.Scale;
                if (v > best) best = v;
            }
            return best == long.MinValue ? 0 : best;
        }

        static int TypePick(Ctx ctx, int move, PokemonState attacker, PokemonState defender)
        {
            if (BattleRules.IsTactic(move)) return 0;
            bool any = false, learn = false;
            foreach (int id in ctx.Effects.OfMove(move))
            {
                var h = ctx.Effects.Effects[id].Handler;
                if (h == "type_any") any = true;
                else if (h == "type_learnable") learn = true;
            }
            if (!any && !learn) return 0;
            var types = ctx.Cards.Pokemon[defender.Def].Types;
            var t2 = types.Count > 1 ? types[1] : PokemonType.Typeless;
            int card = TypeChart.Bonus(BattleRules.MoveType(ctx, move), types[0], t2);
            int best = card;
            if (any)
            {
                foreach (PokemonType t in Enum.GetValues(typeof(PokemonType)))
                    best = Math.Max(best, TypeChart.Bonus(t, types[0], t2));
            }
            else
                foreach (var t in ctx.Cards.Pokemon[attacker.Def].LearnTypes)
                    best = Math.Max(best, TypeChart.Bonus(t, types[0], t2));
            return best - card;
        }
    }
}
