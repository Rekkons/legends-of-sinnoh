using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>파티 강도 칸(상대 명단과 견줌, 01 §13-1 M8-9): 세기 비 <see cref="PartyGauge.EvenLow"/> 밑 = 약함 · 그 위 <see cref="PartyGauge.EvenHigh"/>까지 = 비슷함 · 넘으면 강함.</summary>
    public enum PartyBand { Weak, Even, Strong }

    /// <summary>
    /// 갤럭시단 배틀 앞 플레이어 파티 한 번 잰 값(M8 「시험대 바로잡기」 — 결정 108 ①·109 ①). 비율은 모두 천분율(1000 = 같음).
    /// 세기 비 = 체력 비 × 화력 비 / 1000: 한쪽이 상대를 바닥내는 데 드는 공격 수(상대 체력 ÷ 내 기대 피해)를 견준 것 — 1000보다 크면 플레이어가 먼저 바닥낸다는 셈이다.
    /// 기대 피해 = 원칙 값(<see cref="PrincipleRules.BestValue"/> — 공격력 · 효과 피해 · 상성)이라 체급과 상성이 함께 든다. 선공 · 상태이상 · 교체 · 트레이너 카드는 넣지 않는다.
    /// </summary>
    public sealed class PartyMeasure
    {
        /// <summary>싸울 수 있는 포켓몬(열린 칸 · 체력 &gt; 0) 수 · 진화 단계 합(기본 0 · 한 번 진화 1 · 두 번 2) · 더 진화하지 않는 수 · 등급 합(<see cref="PartyGauge.TierPoints"/>).</summary>
        public int Count, StageSum, FullyEvolved, TierSum;
        /// <summary>그 포켓몬들의 남은 체력 합 · 최대 체력 합.</summary>
        public int Hp, MaxHp;
        /// <summary>상대가 낼 수(조무래기 2 · 간부 조각 수 + 2 · 태홍 6)와 그 명단의 체력 합(명단 고르기면 그 인물 카드 평균 × 낼 수).</summary>
        public int FoeCount, FoeHp;
        /// <summary>체력 비(‰) = 남은 체력 ÷ 상대 체력.</summary>
        public int HpRatio;
        /// <summary>화력 비(‰) = (파티 → 상대 기대 피해 합) ÷ (상대 → 파티 기대 피해 합) — 모든 (내 포켓몬, 상대 카드) 짝.</summary>
        public int PunchRatio;
        /// <summary>상성 점수(천분의 일 단위) = 짝마다 (내 기술 중 가장 좋은 상성 − 상대 기술 중 가장 좋은 상성)의 평균. 0 = 맞먹음, +1000 = 짝마다 한 단계 유리.</summary>
        public int TypeEdge;
        /// <summary>세기 비(‰) = 체력 비 × 화력 비 / 1000.</summary>
        public int Power;

        public PartyBand Band => Power < PartyGauge.EvenLow ? PartyBand.Weak : Power <= PartyGauge.EvenHigh ? PartyBand.Even : PartyBand.Strong;

        /// <summary>준비된 도전(01 §13-1 M8-10): 세기 비가 비슷함 이상이고 상대가 낼 수만큼 싸울 포켓몬이 있다.</summary>
        public bool Ready => Power >= PartyGauge.EvenLow && Count >= FoeCount;

        /// <summary>남은 체력 비율(‰).</summary>
        public int HpPermille => MaxHp > 0 ? Hp * 1000 / MaxHp : 0;
    }

    /// <summary>
    /// 플레이어 파티 재기(M8 「시험대 바로잡기」): 시험대 장면의 파티 분포 표 · 파티 강도 칸 · 「준비된 도전」, 봇 판 운영 규칙(파티가 모자라면 도전을 미룸)이 같은 잣대를 쓴다.
    /// 판의 공개 칸만 읽는다(보기 사본에서도 같다). 정수만.
    /// </summary>
    public static class PartyGauge
    {
        /// <summary>
        /// 비슷함 칸의 아래 · 위(세기 비 ‰) [가정 01 §13-1 M8-9]. 사람 자료(원작 커뮤니티 확인, 시험 M8_사람파티_세기비)에서 접전이던 파티 셋(조무래기에 이김 97% · 주피터에 간신히 이김 126% · 태홍에 짐 94%)이 들게 잡았다.
        /// </summary>
        public const int EvenLow = 800, EvenHigh = 1300;

        /// <summary>등급 점수(등급 합): 스타팅 · 약함 1 · 보통 2 · 강함 3 · 전설 · 울트라 비스트 4. 그 밖(갤럭시단 카드) 0.</summary>
        public static int TierPoints(EncounterTier t) => t == EncounterTier.Starter || t == EncounterTier.Weak ? 1 : t == EncounterTier.Moderate ? 2 : t == EncounterTier.Strong ? 3
            : t == EncounterTier.Legendary || t == EncounterTier.UltraBeast || t == EncounterTier.UltraBurst ? 4 : 0;

        /// <summary>진화 단계: 이 카드로 진화하는 카드를 거슬러 센 수(기본 0).</summary>
        public static int Stage(Ctx ctx, int def)
        {
            var all = ctx.Cards.Pokemon;
            int stage = 0;
            for (int guard = 0; guard < 4; guard++)
            {
                int parent = -1;
                for (int i = 0; i < all.Count && parent < 0; i++)
                    for (int k = 0; k < all[i].EvolveInto.Count; k++)
                        if (all[i].EvolveInto[k] == def)
                        {
                            parent = i;
                            break;
                        }
                if (parent < 0 && all[def].State >= 2)
                {
                    // 다른 면(폼) 카드는 진화 대상에 첫 면으로 적힌다 — 같은 카드 키의 첫 면으로 옮겨 다시 찾는다
                    string head = all[def].Key.Substring(0, all[def].Key.IndexOf(':') + 1);
                    int first = -1;
                    for (int i = 0; i < all.Count && first < 0; i++)
                        if (all[i].State == 1 && all[i].Key.StartsWith(head, StringComparison.Ordinal)) first = i;
                    if (first < 0) break;
                    def = first;
                    continue;
                }
                if (parent < 0) break;
                stage++;
                def = parent;
            }
            return stage;
        }

        /// <summary>
        /// 시험대 장면(갤럭시단 배틀의 첫 선택이 열린 판 — 조무래기 = 열린 배틀, 간부 · 태홍 = 명단 고르기)에서 도전한 좌석의 파티를 그 상대와 잰다.
        /// 명단 고르기면 그 인물의 카드 전부의 평균 × 낼 수(아직 무엇을 낼지 모른다).
        /// </summary>
        public static PartyMeasure OfScene(GameState s, Ctx ctx)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var lineup = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup && !x.Answered);
            var foes = new List<PokemonState>();
            if (lineup != null)
            {
                foreach (int card in lineup.Options) foes.Add(new PokemonState { Def = card, Owner = SeatId.Galactic });
                return Measure(s, ctx, lineup.Subject, foes, lineup.Count);
            }
            var b = s.Battle;
            if (b == null || b.Kind != BattleKind.Galactic) throw new InvalidOperationException("갤럭시단 배틀 장면이 아니다");
            foreach (int id in b.Sides[1].Roster) foes.Add(s.Pokemon[id]);
            return Measure(s, ctx, b.Sides[0].Owner, foes, foes.Count);
        }

        /// <summary>도전하기 전에(봇 판 운영 규칙): 그 좌석의 파티를 그 인물(조무래기 · 간부 · 태홍)의 덱 카드 평균 × 낼 수와 잰다.</summary>
        public static PartyMeasure Against(GameState s, Ctx ctx, int seat, GalacticTrainer leader)
        {
            var deck = leader == GalacticTrainer.Cyrus ? DeckId.GalacticBoss : leader == GalacticTrainer.FemaleGrunt || leader == GalacticTrainer.MaleGrunt ? DeckId.GalacticGrunt : DeckId.GalacticCommander;
            var foes = new List<PokemonState>();
            foreach (var e in ctx.Deck(deck).Cards)
            {
                var t = ctx.Cards.Pokemon[e.Card].Trainer;
                if (deck == DeckId.GalacticGrunt ? t == GalacticTrainer.FemaleGrunt || t == GalacticTrainer.MaleGrunt : t == leader)
                    foes.Add(new PokemonState { Def = e.Card, Owner = SeatId.Galactic });
            }
            int n = deck == DeckId.GalacticBoss ? HosFlow.BossLineup : deck == DeckId.GalacticGrunt ? BattleFlow.GruntLineup : HosFlow.CommanderLineup(HosFlow.ShardCount(s.Seats[seat]));
            return Measure(s, ctx, seat, foes, n);
        }

        /// <summary>좌석 파티(열린 칸 · 체력 &gt; 0)를 상대 카드들(foes — 그 가운데 foeCount마리가 나온다)과 잰다.</summary>
        public static PartyMeasure Measure(GameState s, Ctx ctx, int seat, IReadOnlyList<PokemonState> foes, int foeCount)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (foes == null) throw new ArgumentNullException(nameof(foes));
            var m = new PartyMeasure { FoeCount = foeCount };
            var st = s.Seats[seat];
            var mine = new List<PokemonState>();
            for (int i = 0; i < st.Party.Length; i++)
            {
                int id = st.Party[i];
                if (id < 0 || !PokemonRules.Fights(st, i)) continue;
                var p = s.Pokemon[id];
                if (p.Def < 0) continue;
                int hp = PokemonRules.Hp(ctx, p);
                if (hp <= 0) continue;
                mine.Add(p);
                var def = ctx.Cards.Pokemon[p.Def];
                m.Count++;
                m.Hp += hp;
                m.MaxHp += PokemonRules.MaxHp(ctx, p);
                m.StageSum += Stage(ctx, p.Def);
                if (def.EvolveInto.Count == 0) m.FullyEvolved++;
                m.TierSum += TierPoints(def.Tier);
            }
            if (foes.Count == 0) return m;
            long foeHp = 0;
            foreach (var f in foes) foeHp += PokemonRules.MaxHp(ctx, f);
            m.FoeHp = (int)(foeHp * foeCount / foes.Count);
            m.HpRatio = m.FoeHp > 0 ? (int)((long)m.Hp * 1000 / m.FoeHp) : 0;
            long give = 0, take = 0, edge = 0;
            var moves = new List<int>(4);
            foreach (var p in mine)
                foreach (var f in foes)
                {
                    give += Math.Max(0, PrincipleRules.BestValue(ctx, p, f));
                    take += Math.Max(0, PrincipleRules.BestValue(ctx, f, p));
                    edge += BestBonus(ctx, p, f, moves) - BestBonus(ctx, f, p, moves);
                }
            m.PunchRatio = take > 0 ? (int)Math.Min(100000, give * 1000 / take) : give > 0 ? 100000 : 0;
            m.TypeEdge = mine.Count > 0 ? (int)(edge * 1000 / (mine.Count * foes.Count)) : 0;
            m.Power = (int)((long)m.HpRatio * m.PunchRatio / 1000);
            return m;
        }

        /// <summary>공격 쪽 기술 후보 중 받는 쪽에게 가장 좋은 상성 값(TypeChart.Bonus — 두 타입 합).</summary>
        static int BestBonus(Ctx ctx, PokemonState attacker, PokemonState defender, List<int> moves)
        {
            moves.Clear();
            PokemonRules.MoveOptions(ctx, attacker, moves);
            var types = ctx.Cards.Pokemon[defender.Def].Types;
            var t2 = types.Count > 1 ? types[1] : PokemonType.Typeless;
            int best = int.MinValue;
            foreach (int mv in moves)
            {
                int v = TypeChart.Bonus(BattleRules.MoveType(ctx, mv), types[0], t2);
                if (v > best) best = v;
            }
            return best == int.MinValue ? 0 : best;
        }
    }
}
