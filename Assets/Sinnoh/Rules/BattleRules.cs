using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 배틀 규칙 질의(01 §6, rulings G13). 상태를 바꾸지 않는다 — 진행은 BattleFlow. 상시 보정(우선도·공격력·총 데미지 감소)은 계산할 때
    /// 출처를 훑어 더하고 훑은 출처를 Mods로 돌려준다(01 §7-1, §3-6 ④). 트레이너 카드·균열 카드·유물·심장·날씨 보정은 그 작업(M5·M6)이 더한다.
    /// </summary>
    public static class BattleRules
    {
        /// <summary>플랜 B(갤럭시단 전술 보드, A13): 데이터 기술 표 밖이라 기술 키 −2. 타입없음 공격력 3, 쓰면 은하의 응징 +1(rulings G13).</summary>
        public const int PlanB = -2;
        public const int PlanBStrength = 3;

        /// <summary>
        /// 알파 스트라이크(p.6 「Provides an attack that can be any learnable type of the Legendary Pokémon. However, its attack strength is decreased to 2 if this move was used last turn.」, rulings A26):
        /// 데이터 기술 표 밖이라 기술 키 −3. 공격력 4, 직전 턴에 그 포켓몬이 썼으면 2(<see cref="LegendRules.PowerMods"/>). 타입은 답이 싣는다(UseMove Values[0] → BattleState.TypeTo).
        /// </summary>
        public const int AlphaStrike = -3;
        public const int AlphaStrikeStrength = 4;
        public const int AlphaStrikeRepeatStrength = 2;

        /// <summary>데이터 기술 표 밖의 기술(플랜 B · 알파 스트라이크)인가.</summary>
        public static bool IsTactic(int move) => move == PlanB || move == AlphaStrike;

        /// <summary>은하의 응징: 기절마다 2(A13).</summary>
        public const int RetributionPerFaint = 2;

        /// <summary>마비 우선도 −3(S7_6), 민첩의 띠 우선도 +2·공격력 −1, 빙결 대상 공격력 +2, 화상 총 데미지 −2(rules-summary §7.6·§10).</summary>
        public const int ParalysisPriority = -3;
        public const int SashPriority = 2;
        public const int SashPower = -1;
        public const int FrozenTargetPower = 2;
        public const int BurnReduction = 2;
        /// <summary>신오의 심장: 지닌 포켓몬과 같은 타입의 기술 공격력 +1(p.22).</summary>
        public const int HeartPower = 1;

        /// <summary>무리 배틀 승리 보상(A18): 마리별 보상 대신 JP 7 하나.</summary>
        public const int SwarmRewardJp = 7;

        /// <summary>야생을 쓰러뜨리면 더 받는 JP(p.9 「only after a wild Pokémon faints: … an extra 2」).</summary>
        public const int DefeatBonusJp = 2;

        public static int MoveStrength(Ctx ctx, int move) => move == PlanB ? PlanBStrength : move == AlphaStrike ? AlphaStrikeStrength : PokemonRules.AttackStrength(ctx, move);

        public static PokemonType MoveType(Ctx ctx, int move) => IsTactic(move) ? PokemonType.Typeless : ctx.Cards.Moves[move].Type;

        /// <summary>지금 공격의 타입: 효과가 고른 타입(BattleState.TypeTo — hidden_power·judgement, rulings G20)이 있으면 그것, 아니면 기술 카드의 타입. move가 지금 쓰는 기술이 아니면 카드 타입.</summary>
        public static PokemonType AttackType(GameState s, Ctx ctx, int move)
        {
            var b = s.Battle;
            // 균열 카드 chromatic_union 「The moves of the user are now Typeless」: 지금 공격하는 포켓몬에 붙어 있으면 고른 타입까지 타입없음(rulings A52 ⑤)
            if (b != null && b.Move == move && b.ActiveSide >= 0 && b.Sides[b.ActiveSide].Active >= 0 && BreachRules.Typeless(ctx, s.Pokemon[b.Sides[b.ActiveSide].Active])) return PokemonType.Typeless;
            return b != null && b.Move == move && b.TypeTo >= 0 ? (PokemonType)b.TypeTo : MoveType(ctx, StrengthMove(s, move));
        }

        /// <summary>지금 공격의 공격력·타입을 정하는 기술(mimic·metronome이 부른 기술 — BattleState.Called, rulings G21). move가 지금 쓰는 기술이 아니면 그대로.</summary>
        public static int StrengthMove(GameState s, int move)
        {
            var b = s.Battle;
            return b != null && b.Move == move && b.Called >= 0 ? b.Called : move;
        }

        /// <summary>지금 공격의 효과를 읽는 기술(kinesis가 복사한 기술 · 부른 기술 — BattleState.EffectsOf, rulings G21). move가 지금 쓰는 기술이 아니면 그대로.</summary>
        public static int EffectMove(GameState s, int move)
        {
            var b = s.Battle;
            return b != null && b.Move == move && b.EffectsOf >= 0 ? b.EffectsOf : move;
        }

        /// <summary>
        /// MoveUsed.Category(연출 갈래, 04 59행) [가정]: 카드에 물리·특수 구분이 없어 3세대식 타입 나눔을 쓴다 — 노말·격투·비행·독·땅·바위·벌레·고스트·강철 = 물리(돌진),
        /// 나머지 = 특수(발사체). 공격력 「?」(효과가 정함)는 변화(오라). 규칙에는 쓰지 않는다.
        /// </summary>
        public static MoveCategory CategoryOf(Ctx ctx, int move)
        {
            if (!IsTactic(move) && ctx.Cards.Moves[move].AttackStrength == null) return MoveCategory.Status;
            switch (MoveType(ctx, move))
            {
                case PokemonType.Normal:
                case PokemonType.Fighting:
                case PokemonType.Flying:
                case PokemonType.Poison:
                case PokemonType.Ground:
                case PokemonType.Rock:
                case PokemonType.Bug:
                case PokemonType.Ghost:
                case PokemonType.Steel:
                case PokemonType.Typeless:
                    return MoveCategory.Physical;
                default:
                    return MoveCategory.Special;
            }
        }

        /// <summary>포획률(S5_2): 약한 3 · 적당한 4 · 강한 5 · 전설 6. 우두머리는 전설로 친다(A17). 천상의 오라 7은 신 배틀(M5)이 CaptureRules.Rate로.</summary>
        public static int CatchRate(EncounterTier grade, bool alpha)
        {
            if (alpha) return 6;
            switch (grade)
            {
                case EncounterTier.Starter:
                case EncounterTier.Weak: return 3;
                case EncounterTier.Moderate: return 4;
                case EncounterTier.Strong: return 5;
                default: return 6;
            }
        }

        /// <summary>
        /// 야생 포획·격파 JP(S5_2, p.9): 약한 1 · 적당한 2 · 강한 3 · 전설 4, 쓰러뜨렸으면 +2. 우두머리는 전설로 4(+2)(A17). 무리 배틀은 마리별이 아니라 <see cref="SwarmRewardJp"/>(A18).
        /// </summary>
        public static int RewardJp(EncounterTier grade, bool alpha, bool defeated)
        {
            int jp;
            if (alpha) jp = 4;
            else
                switch (grade)
                {
                    case EncounterTier.Starter:
                    case EncounterTier.Weak: jp = 1; break;
                    case EncounterTier.Moderate: jp = 2; break;
                    case EncounterTier.Strong: jp = 3; break;
                    default: jp = 4; break;
                }
            return defeated ? jp + DefeatBonusJp : jp;
        }

        /// <summary>야생이 도망칠 때 남기는 규토리(A8): 약한 2 · 적당한 1 · 강한 0.</summary>
        public static int FleeApricorns(EncounterTier grade)
        {
            switch (grade)
            {
                case EncounterTier.Starter:
                case EncounterTier.Weak: return 2;
                case EncounterTier.Moderate: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// 내 포켓몬 1마리를 쓰러뜨리면 야생이 도망치나(S7_8 p.8): 약한·적당한·강한 야생만. 전설·우두머리(A17)·무리(A18)·신은 내 포켓몬을 모두 쓰러뜨려야 한다.
        /// </summary>
        public static bool WildFleesAfterFirstKo(BattleKind kind, EncounterTier grade, bool alpha)
        {
            if (kind != BattleKind.Wild || alpha) return false;
            return grade == EncounterTier.Starter || grade == EncounterTier.Weak || grade == EncounterTier.Moderate || grade == EncounterTier.Strong;
        }

        /// <summary>전설 특수기술(알파 스트라이크·비스트 부스트)을 갖나(R13, Lua 9d9ee428cb.lua:497): 전설·UB·울트라 버스트 카드 또는 우두머리 토큰 소지.</summary>
        public static bool LegendaryMovesEligible(Ctx ctx, PokemonState p)
        {
            var d = ctx.Cards.Pokemon[p.Def];
            return p.Alpha || d.Legendary || d.Tier == EncounterTier.Legendary || d.Tier == EncounterTier.UltraBeast || d.Tier == EncounterTier.UltraBurst;
        }

        /// <summary>
        /// 우선도 보정 출처(01 §6-2, B5): 마비 −3, 민첩의 띠 +2. 순서 = 상태이상 → 소지 아이템(좌석 안은 데이터 순, 01 §3-6 ④).
        /// </summary>
        public static void PriorityMods(GameState s, Ctx ctx, int pokemon, List<Mod> into)
        {
            into.Clear();
            var p = s.Pokemon[pokemon];
            if (StatusRules.Has(p, StatusEffect.Paralysed))
                into.Add(new Mod { Source = new SourceRef(SourceKind.Status, (int)StatusEffect.Paralysed), Delta = ParalysisPriority });
            int sash = ctx.Items.Ids.IndexOf("item.swiftness_sash");
            if (PokemonRules.HoldsItem(p, sash))
                into.Add(new Mod { Source = new SourceRef(SourceKind.Item, sash), Delta = SashPriority });
            BreachRules.PriorityMods(ctx, p, into); // 균열 카드 gravitational_drag −3
            TrainerRules.PriorityMods(s, ctx, pokemon, into); // 트레이너 카드의 상시 우선도(rulings A32)
        }

        /// <summary>
        /// 순서 비교 값(B5, rulings B5 덧붙임): 보정 후 우선도면 카드 우선도 + 보정, 기본 우선도(Lua R11)면 카드 우선도만(보정을 훑지 않아 mods는 빈다).
        /// </summary>
        public static int OrderValue(GameState s, Ctx ctx, int pokemon, List<Mod> mods, out int basePriority)
        {
            basePriority = ctx.Cards.Pokemon[s.Pokemon[pokemon].Def].Initiative;
            mods.Clear();
            if (s.Options.InitiativeTie == InitiativeTie.BasePriority) return basePriority;
            PriorityMods(s, ctx, pokemon, mods);
            int v = basePriority;
            foreach (var m in mods) v += m.Delta;
            return v;
        }

        /// <summary>공격력 보정 출처(01 §6-4): 공격자 민첩의 띠 −1 · 결단의 머리띠 +1(체력 0인 공격자의 지닌물건은 보지 않는다 — rulings A24 ③), 빙결 대상 +2. 효과·카드 보정은 M5·M6.</summary>
        public static void PowerMods(GameState s, Ctx ctx, int attacker, int defender, List<Mod> into)
        {
            into.Clear();
            var a = s.Pokemon[attacker];
            int sash = ctx.Items.Ids.IndexOf("item.swiftness_sash");
            if (!PokemonRules.IsFainted(ctx, a) && PokemonRules.HoldsItem(a, sash))
                into.Add(new Mod { Source = new SourceRef(SourceKind.Item, sash), Delta = SashPower });
            if (!PokemonRules.IsFainted(ctx, a) && HeldItems.Holds(ctx, a, HeldItems.ChoiceBand))
                into.Add(new Mod { Source = new SourceRef(SourceKind.Item, HeldItems.ItemOf(ctx, a, HeldItems.ChoiceBand)), Delta = HeldItems.ChoiceBandPower });
            if (defender >= 0 && StatusRules.Has(s.Pokemon[defender], StatusEffect.Frozen))
                into.Add(new Mod { Source = new SourceRef(SourceKind.Status, (int)StatusEffect.Frozen), Delta = FrozenTargetPower });
            BreachRules.PowerMods(ctx, a, into); // 균열 카드 chromatic_union +1
        }

        /// <summary>신오의 심장(p.22 「Whilst held, it increases the attack strength of moves that are the same type as the user by 1」): 지닌 포켓몬의 타입과 같은 타입의 기술 +1.</summary>
        static void HeartMod(GameState s, Ctx ctx, int move, int attacker, List<Mod> into)
        {
            var a = s.Pokemon[attacker];
            int heart = ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey);
            if (move == PlanB || PokemonRules.IsFainted(ctx, a) || !PokemonRules.HoldsItem(a, heart)) return;
            var type = AttackType(s, ctx, move);
            foreach (var t in ctx.Cards.Pokemon[a.Def].Types)
                if (t == type)
                {
                    into.Add(new Mod { Source = new SourceRef(SourceKind.Item, heart), Delta = HeartPower });
                    return;
                }
        }

        /// <summary>
        /// 그 기술로 굴릴 주사위 수(01 §6-3): clamp(기술 공격력 + 보정, 1, 9). 보정 = 기술 효과의 「공격력 = n」(맨 앞, G14) → 지닌물건·얼음 대상(<see cref="PowerMods"/>) · 신오의 심장 · 유물.
        /// 굴림(<c>BattleFlow.RollStep</c>)과 화면의 기술 줄 「위력」이 같은 값을 쓴다. <paramref name="mods"/>는 비우고 채운다.
        /// </summary>
        public static int RollPower(GameState s, Ctx ctx, int move, int attacker, int defender, List<Mod> mods)
        {
            PowerMods(s, ctx, attacker, defender, mods);
            HeartMod(s, ctx, move, attacker, mods);
            RelicRules.PowerMods(s, ctx, move, attacker, mods); // 지닌 유물(p.17): 그 두 타입의 기술 +1
            TrainerRules.PowerMods(s, ctx, move, attacker, defender, mods); // 트레이너 카드의 상시 공격력(rulings A32)
            EffectRules.StrengthMods(s, ctx, move, attacker, defender, mods);
            FateRules.PowerMods(s, ctx, attacker, mods); // 기라티나(rulings A24 ③): 이번 배틀 턴 기술에
            LegendRules.PowerMods(s, ctx, move, attacker, mods); // 알파 스트라이크 직전 턴 → 2 · 비스트 부스트(rulings A26)
            int delta = 0;
            foreach (var m in mods) delta += m.Delta;
            int power = DamageRules.AttackStrength(MoveStrength(ctx, StrengthMove(s, move)), delta); // 부른 기술의 공격력(mimic·metronome) — 「?」·동적 공격력은 StrengthMods의 셈 처리기(G21)
            return BreachRules.CapPower(ctx, s.Pokemon[attacker], power, mods); // 균열 카드 rapid_strikes: 3 초과는 3
        }

        /// <summary>좌석이 쓸 수 있는 포켓몬(기절하지 않음)이 파티에 있나.</summary>
        public static bool HasUsable(GameState s, Ctx ctx, int seat)
        {
            var st = s.Seats[seat];
            for (int i = 0; i < st.Party.Length; i++)
                if (st.Party[i] >= 0 && PokemonRules.Fights(st, i) && !PokemonRules.IsFainted(ctx, s.Pokemon[st.Party[i]])) return true; // 잠긴 칸은 빼고(pokemon_ranger B)
            return false;
        }

        /// <summary>
        /// 이 배틀에서 상대 좌석에게서 빼앗은 포켓몬인가(team_rocket B — 상대 좌석 쪽 명단에 있던 포켓몬이 내 쪽에 있다): 그 배틀에는 나오지 못한다(사용자 결정 84 T7 ③ 나).
        /// side = 내 쪽(좌석). 상대가 가상 쪽(야생·무리 — 잡은 포켓몬)이면 거르지 않는다.
        /// </summary>
        public static bool Taken(GameState s, BattleSide side, int pokemon)
        {
            var b = s.Battle;
            if (b == null) return false;
            var other = b.Sides[0] == side ? b.Sides[1] : b.Sides[0];
            return other.Owner >= 0 && other.Owner != side.Owner && other.Roster.Contains(pokemon);
        }

        /// <summary>배틀 한쪽에 쓸 수 있는 포켓몬이 남았나(좌석 = 파티 — 이 배틀에서 빼앗은 포켓몬은 빼고, 가상 = 명단).</summary>
        public static bool SideHasUsable(GameState s, Ctx ctx, BattleSide side)
        {
            if (side.Owner >= 0)
            {
                var st = s.Seats[side.Owner];
                for (int i = 0; i < st.Party.Length; i++)
                {
                    int p = st.Party[i];
                    if (p >= 0 && PokemonRules.Fights(st, i) && !PokemonRules.IsFainted(ctx, s.Pokemon[p]) && !Taken(s, side, p)) return true;
                }
                return false;
            }
            foreach (int p in side.Roster)
                if (OnSide(s, ctx, side, p)) return true;
            return false;
        }

        /// <summary>가상 쪽 명단의 그 포켓몬이 아직 그 쪽에서 싸울 수 있나: 기절하지 않았고 잡혀 가지 않았다(무리 배틀은 하나를 잡아도 다음이 나온다 — rulings A28 ②).</summary>
        public static bool OnSide(GameState s, Ctx ctx, BattleSide side, int pokemon) =>
            s.Pokemon[pokemon].Owner == side.Owner && !PokemonRules.IsFainted(ctx, s.Pokemon[pokemon]);

        /// <summary>포켓몬이 그 쪽의 몇 번째인가(Fainted.PartyIndex): 좌석 = 파티 칸, 가상 = 명단 자리.</summary>
        public static int IndexInSide(GameState s, BattleSide side, int pokemon) =>
            side.Owner >= 0 ? PokemonRules.PartyIndexOf(s.Seats[side.Owner], pokemon) : side.Roster.IndexOf(pokemon);

        /// <summary>배틀 BGM 상황 키(BattleStarted.BgmKey).</summary>
        public static BattleBgm BgmOf(BattleKind kind)
        {
            switch (kind)
            {
                case BattleKind.Trainer:
                case BattleKind.Traitor: return BattleBgm.Trainer;
                case BattleKind.Galactic: return BattleBgm.GalacticGrunt;
                case BattleKind.Legendary:
                case BattleKind.Alpha: return BattleBgm.Legendary;
                case BattleKind.God: return BattleBgm.God;
                case BattleKind.UltraBeast:
                case BattleKind.Trial: return BattleBgm.UltraBeast;
                case BattleKind.Necrozma: return BattleBgm.Necrozma;
                default: return BattleBgm.Wild;
            }
        }

        /// <summary>마을 칸인가(도전 거절은 마을 안에서만, p.3). 야영지는 마을이 아니다.</summary>
        public static bool InVillage(Ctx ctx, int cell)
        {
            if (cell < 0) return false;
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village && f.Cell == cell) return true;
            return false;
        }

        internal static int Sum(AttackDieFace[] faces) => AttackDice.Sum(faces);
    }
}
