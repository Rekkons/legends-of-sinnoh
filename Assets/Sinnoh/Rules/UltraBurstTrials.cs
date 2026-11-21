using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 호수 시련 · 실버디(룰북 p.26, 01 §8-2, rulings A55 — M6 「호수 시련·실버디」). 울트라 버스트 판에서만.
    /// - 자리: 호수 동굴 셋(진실 = 엠라이트 481 · 예지 = 유크시 480 · 입지 = 아그놈 482)과 신오신전(실버디 773)에서 찾아보기(MapAction Search, Arg = 그 고정물 — 유물 없이, 그 카드가 전설 덱에 있을 때).
    ///   수호신은 BattleKind.Trial, 실버디는 BattleKind.Legendary — 둘 다 보통 전설 배틀이다(포획·보상·기술 가르치기 · 짐·도망 = 덱으로 돌아가 다시 도전).
    /// - 면역(p.26 「they are immune to capture attempts and all damage until the turn after their trials are overcome」): 시련을 넘기기 전과 넘긴 그 배틀 턴까지 수호신은
    ///   모든 피해(공격 · 효과 · 독 · 피로 · 넘겨받은 피해)를 받지 않고 볼을 던질 수 없다. 상태 이상은 걸린다(원작 커뮤니티 확인).
    ///   다음 배틀 턴부터는 보통 전설이다(원작 커뮤니티 확인).
    /// - 시련은 판에 한 번(원작 커뮤니티 확인 — UbState.TrialsOvercome): 넘긴 뒤의 배틀에는 면역이 없다. 넘기지 못한 진행은 배틀마다 새로 센다 [가정].
    ///   · 감정(엠라이트): 「Mesprit must take damage from battle fatigue」 — 엠라이트가 피로 피해를 입었을 때(면역이라 0이지만 「would take」, v1.4 문구).
    ///   · 지식(유크시): 「Uxie must be attacked with 2 different types that are super effective」 — 상성이 유리한(+) 공격을 서로 다른 타입 둘로.
    ///   · 의지(아그놈): 「Azelf must be attacked with 2 or more critical hits over your last 2 turns」 — 내 최근 두 배틀 턴의 공격 굴림에 나온 크리티컬 합이 2 이상(한 턴에 2개도, 1개 + 1개도 — 2턴 연속이 아니다).
    /// - 수호신을 쓰러뜨리거나 잡을 때마다(p.26 「After each Lake Guardian is defeated or captured, all trainers gain +2 movement per turn for the rest of the game」) 모든 좌석의 턴 이동력 +2(호수마다 한 번).
    /// - 실버디(p.26 「Defeating or capturing this Pokémon grants 1 Blessing of Arceus」): 축복 +1 — 판에 한 번 [가정 A55 ⑥].
    /// </summary>
    public static class UbTrial
    {
        public const int MovePerGuardian = 2;
        public const int KnowledgeGoal = 2, WillpowerGoal = 2;
        public const string Silvally = "773:0";

        /// <summary>호수 수호신 카드 키(Shard 순서: 감정·지식·의지).</summary>
        static readonly string[] Guardians = { "481:0", "480:0", "482:0" };

        /// <summary>그 카드가 호수 수호신이면 호수(Shard 값 0~2), 아니면 −1. 갤럭시단 간부의 수호신(「481:0@mars」)은 아니다.</summary>
        public static int LakeOf(Ctx ctx, int def)
        {
            if (def < 0 || def >= ctx.Cards.Pokemon.Count) return -1;
            return System.Array.IndexOf(Guardians, ctx.Cards.Pokemon[def].Key);
        }

        /// <summary>그 호수의 수호신 카드(포켓몬 표 색인).</summary>
        public static int Guardian(Ctx ctx, int lake) => ctx.Cards.PokemonIds.IndexOf(Guardians[lake]);

        /// <summary>
        /// 그 좌석의 칸에서 찾아보기로 싸울 호수 동굴·신오신전(고정물). 울트라 버스트 판이고 그 자리의 카드(수호신·실버디)가 전설 덱에 있을 때. 없으면 −1.
        /// </summary>
        public static int SiteAt(GameState s, Ctx ctx, int seat)
        {
            if (s.Ub == null) return -1;
            int cell = s.Seats[seat].Cell;
            if (cell < 0) return -1;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.LegendarySite || fixtures[f].Cell != cell) continue;
                int card = LegendRules.SiteCard(s, ctx, f);
                if (card >= 0 && (LakeOf(ctx, card) >= 0 || ctx.Cards.Pokemon[card].Key == Silvally)) return f;
            }
            return -1;
        }

        /// <summary>그 배틀의 호수(시련 배틀이고 상대가 수호신일 때), 아니면 −1.</summary>
        static int LakeOf(GameState s, Ctx ctx, BattleState b)
        {
            if (s.Ub == null || b == null || b.Kind != BattleKind.Trial || b.Sides[1].Roster.Count == 0) return -1;
            return LakeOf(ctx, s.Pokemon[b.Sides[1].Roster[0]].Def);
        }

        /// <summary>수호신이 지금 면역인가: 시련을 아직 넘기지 않았거나, 이 배틀에서 넘긴 그 배틀 턴이다(p.26 「until the turn after」).</summary>
        public static bool Immune(GameState s, Ctx ctx, BattleState b)
        {
            int lake = LakeOf(s, ctx, b);
            if (lake < 0) return false;
            return !s.Ub.TrialsOvercome[lake] || (b.TrialTurn >= 0 && b.Turn <= b.TrialTurn);
        }

        /// <summary>그 포켓몬이 지금 면역인 수호신인가(피해 적용 · 포획 답이 본다).</summary>
        public static bool Guards(GameState s, Ctx ctx, BattleState b, int pokemon) =>
            pokemon >= 0 && b != null && b.Kind == BattleKind.Trial && b.Sides[1].Roster.Contains(pokemon) && Immune(s, ctx, b);

        /// <summary>도전한 쪽의 배틀 턴이 시작됐다(BattleFlow.SelectStep): 의지의 시련은 크리티컬 수를 한 턴 민다(「over your last 2 turns」).</summary>
        internal static void TurnStarted(GameState s, Ctx ctx, BattleState b)
        {
            if (b.ActiveSide != 0 || LakeOf(s, ctx, b) != (int)Shard.Willpower) return;
            b.TrialPrev = b.TrialNow;
            b.TrialNow = 0;
        }

        /// <summary>
        /// 도전한 쪽이 수호신을 공격했다(BattleFlow.DamageStep — 그 공격의 타입과 굴린 면, 면 바꾸기 뒤): 지식 = 상성이 유리하면 그 타입을 적는다 · 의지 = 크리티컬을 센다.
        /// 채우면 시련을 넘긴다 — 이 공격의 피해는 아직 면역이다(넘긴 그 턴까지).
        /// </summary>
        internal static void Attacked(GameState s, Ctx ctx, Emitter fx, BattleState b, int defender, PokemonType type, List<AttackDieFace> faces)
        {
            int lake = LakeOf(s, ctx, b);
            if (lake < 0 || b.ActiveSide != 0 || s.Ub.TrialsOvercome[lake] || !b.Sides[1].Roster.Contains(defender)) return;
            if (lake == (int)Shard.Knowledge)
            {
                var types = ctx.Cards.Pokemon[s.Pokemon[defender].Def].Types;
                if (TypeChart.Bonus(type, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless) <= 0) return;
                int bit = 1 << (int)type;
                if ((b.TrialNow & bit) != 0) return;
                b.TrialNow |= bit;
                int count = 0;
                for (int m = b.TrialNow; m != 0; m &= m - 1) count++;
                Progress(s, fx, b, lake, count, KnowledgeGoal);
            }
            else if (lake == (int)Shard.Willpower)
            {
                int crits = 0;
                foreach (var f in faces)
                    if (f == AttackDieFace.CriticalHit) crits++;
                if (crits == 0) return;
                b.TrialNow += crits;
                Progress(s, fx, b, lake, b.TrialNow + b.TrialPrev, WillpowerGoal);
            }
        }

        /// <summary>면역인 수호신이 피로 피해를 입었을 것이다(BattleFlow.ApplyBuffer): 감정의 시련이면 넘긴다.</summary>
        internal static void Fatigued(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int lake = LakeOf(s, ctx, b);
            if (lake == (int)Shard.Emotion && !s.Ub.TrialsOvercome[lake]) Progress(s, fx, b, lake, 1, 1);
        }

        static void Progress(GameState s, Emitter fx, BattleState b, int lake, int count, int goal)
        {
            bool done = count >= goal;
            if (done)
            {
                s.Ub.TrialsOvercome[lake] = true;
                b.TrialTurn = b.Turn;
            }
            if (fx.On) fx.Add(new TrialProgress { Seat = b.Sides[0].Owner, Lake = lake, Count = count, Goal = goal, Done = done });
        }

        /// <summary>
        /// 전설을 쓰러뜨리거나 잡았다(BattleFlow.Conclude). 울트라 버스트 판에서: 호수 수호신이면 그 호수를 적고(GuardianDown — 처음 한 번) 이제부터 모든 좌석 이동력 +2 ·
        /// 실버디면 축복 +1(BlessingGained, Source = Rule:Silvally — 판에 한 번).
        /// </summary>
        internal static void Won(GameState s, Ctx ctx, Emitter fx, int seat, int def)
        {
            var ub = s.Ub;
            if (ub == null) return;
            int lake = LakeOf(ctx, def);
            if (lake >= 0)
            {
                if (ub.GuardiansDefeated[lake]) return;
                ub.GuardiansDefeated[lake] = true;
                if (fx.On) fx.Add(new GuardianDown { Seat = seat, Lake = lake, Total = Down(s) });
            }
            else if (ctx.Cards.Pokemon[def].Key == Silvally && !ub.SilvallyBlessed)
            {
                ub.SilvallyBlessed = true;
                UbBlessing.Gain(s, fx, seat, SourceRef.Rule(RuleSource.Silvally));
            }
        }

        /// <summary>쓰러뜨리거나 잡은 호수 수호신 수.</summary>
        public static int Down(GameState s)
        {
            int n = 0;
            if (s.Ub != null)
                foreach (bool d in s.Ub.GuardiansDefeated)
                    if (d) n++;
            return n;
        }

        /// <summary>턴 이동력 보정(TurnFlow.StartTurn): 수호신마다 +2(출처 = Rule:LakeGuardian 하나씩). 더한 합을 돌려준다.</summary>
        public static int MoveBonus(GameState s, List<Mod> mods)
        {
            int n = Down(s);
            for (int i = 0; i < n; i++) mods.Add(new Mod { Source = SourceRef.Rule(RuleSource.LakeGuardian), Delta = MovePerGuardian });
            return n * MovePerGuardian;
        }
    }
}
