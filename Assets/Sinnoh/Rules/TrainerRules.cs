using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>얻은 길(TrainerRules.Obtained) — 옵션이 가르는 것만 나눈다.</summary>
    public enum ObtainCause { Capture, CardEffect, Evolution, Trade }

    /// <summary>
    /// 트레이너 카드 확장(rulings A20 · trainer-cards.md): 등급 올리기(해금) · 능력 물음 <see cref="Has"/> · 「옮긴 것」 등록부(<see cref="Ported"/>).
    /// 해금(A20-2~5): 보통·강함 포켓몬을 쓰러뜨리거나(<see cref="Defeated"/> — BattleFlow.ReportFaint) 얻을 때(<see cref="Obtained"/> — 포획 · 등가 교환 · 진화 · 거래)
    /// 좌석의 TrainerCardTier를 1 → 2 → 3으로 올리고 CardUnlocked를 낸다. 순서는 RuleOptions.UnlockOrder.
    /// 능력 102 + 전용 아이템 11의 효과는 표(effects.json)가 아니라 C#이다(plan §5 #135 ①) — 능력을 옮긴 묶음이 <see cref="Ported"/>에 한 줄을 더한다(덮개 시험 M6_능력덮개_미구현수).
    /// </summary>
    public static partial class TrainerRules
    {
        public const int Bronze = 1, Silver = 2, Gold = 3;

        // ── 능력 물음 ──

        /// <summary>그 좌석의 카드 색인(트레이너 카드 확장을 끄거나 좌석이 아니면 −1).</summary>
        public static int CardOf(GameState s, int seat) => seat >= 0 && seat < s.Seats.Length ? s.Seats[seat].TrainerCard : -1;

        /// <summary>능력 칸 번호(TrainerAbility.Slot): 파랑 1 · 빨강 2.</summary>
        public static int SlotOf(AbilityColor color) => color == AbilityColor.Blue ? 1 : 2;

        /// <summary>그 등급에서 그 칸(파랑 = 실버부터, 빨강 = 골드부터)이 풀렸나(A20-2).</summary>
        public static bool Unlocked(int tier, AbilityColor color) => tier > SlotOf(color);

        /// <summary>
        /// 좌석이 그 카드(키 = trainer_cards.json key, 예 "ace_trainer")의 그 능력을 지금 쓸 수 있나 — 능력 묶음은 이것만 본다(등급으로 센다).
        /// 가상 좌석(야생·갤럭시단·UB)과 카드가 없는 좌석은 false. 모르는 키는 예외(IdTable — 오타를 조용히 넘기지 않는다).
        /// </summary>
        public static bool Has(GameState s, Ctx ctx, int seat, string cardKey, AbilityColor color)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(cardKey);
            return CardOf(s, seat) == card && Unlocked(s.Seats[seat].TrainerCardTier, color);
        }

        /// <summary>능력을 썼다(AbilityUsed — 04 50행). 상시 보정은 내지 않는다: 그 합을 쓰는 결과 이벤트의 Mods[]가 출처를 싣는다(01 §7-1).</summary>
        public static void Used(GameState s, Emitter fx, int seat, AbilityColor color, params int[] targets)
        {
            if (!fx.On) return;
            int card = s.Seats[seat].TrainerCard;
            fx.Add(new AbilityUsed { Seat = seat, Source = new SourceRef(SourceKind.TrainerCard, card), CardKey = card, Ability = color, Targets = targets ?? new int[0] });
        }

        // ── 해금 ──

        /// <summary>
        /// 해금 조건으로 세는 등급(A20-3·A20-4): 그 순간 카드의 Moderate·Strong. 우두머리 토큰이 붙은 것은 전설로 재분류돼 UnlockAlphaAsStrong일 때만,
        /// Legendary 카드는 UnlockLegendaryAsStrong일 때만 강함. 그 밖(Starter·Weak·UltraBeast·UltraBurst·갤럭시단)은 null.
        /// </summary>
        public static EncounterTier? Grade(GameState s, Ctx ctx, PokemonState p)
        {
            if (p.Def < 0) return null;
            var tier = ctx.Cards.Pokemon[p.Def].Tier;
            if (p.Alpha) return s.Options.UnlockAlphaAsStrong ? EncounterTier.Strong : (EncounterTier?)null;
            if (tier == EncounterTier.Legendary) return s.Options.UnlockLegendaryAsStrong ? EncounterTier.Strong : (EncounterTier?)null;
            return tier == EncounterTier.Moderate || tier == EncounterTier.Strong ? tier : (EncounterTier?)null;
        }

        /// <summary>
        /// 배틀에서 쪽 k의 포켓몬이 기절했다 → 상대 쪽이 좌석이면 그 좌석이 「쓰러뜨렸다」(무엇이 마지막으로 깎았나는 가리지 않는다 [가정, rulings A20-3]).
        /// 기절한 쪽이 다른 좌석이면 UnlockDefeatOtherTrainer일 때만. 갤럭시단·스타터·UB는 등급이 달라 걸리지 않는다.
        /// </summary>
        public static void Defeated(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int pokemon)
        {
            int seat = b.Sides[1 - k].Owner;
            if (seat < 0) return;
            if (b.Sides[k].Owner >= 0 && !s.Options.UnlockDefeatOtherTrainer) return;
            Count(s, ctx, fx, seat, pokemon);
        }

        /// <summary>좌석이 그 포켓몬을 얻었다(파티에 새로 들어옴 · 카드가 바뀜). 진화는 UnlockByEvolution, 거래는 UnlockTrade일 때만 센다.</summary>
        public static void Obtained(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, ObtainCause cause)
        {
            if (cause == ObtainCause.Evolution && !s.Options.UnlockByEvolution) return;
            if (cause == ObtainCause.Trade && !s.Options.UnlockTrade) return;
            Count(s, ctx, fx, seat, pokemon);
        }

        static void Count(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            if (seat < 0 || seat >= s.Seats.Length || !s.Options.TrainerCards) return; // A20-8
            var st = s.Seats[seat];
            int tier = st.TrainerCardTier;
            if (st.TrainerCard < 0 || tier < Bronze || tier >= Gold) return; // 골드면 무시, 단계는 내려가지 않는다(A20-5)
            var grade = Grade(s, ctx, s.Pokemon[pokemon]);
            if (grade == null) return;
            int to = tier;
            if (grade == EncounterTier.Moderate)
            {
                if (tier == Bronze) to = s.Options.UnlockOrder == UnlockOrder.RememberThenGold && st.StrongRemembered ? Gold : Silver;
            }
            else if (tier == Silver) to = Gold;
            else if (s.Options.UnlockOrder == UnlockOrder.StraightToGold) to = Gold;
            else if (s.Options.UnlockOrder == UnlockOrder.RememberThenGold) st.StrongRemembered = true;
            // SilverOnly: 브론즈에서의 강함은 아무 일도 없다(「has to be that specific tier … So Moderate first then Strong」, rulings A20-5)
            if (to == tier) return;
            st.TrainerCardTier = to;
            st.StrongRemembered = false;
            Refresh(s, ctx); // 풀린 능력의 최대 체력 보정(gardener R · hiker R · youngster B, rulings A32)
            var slots = new List<int>(2);
            if (tier < Silver) slots.Add(SlotOf(AbilityColor.Blue));
            if (to == Gold) slots.Add(SlotOf(AbilityColor.Red));
            if (fx.On)
                fx.Add(new CardUnlocked
                {
                    Seat = seat, Source = new SourceRef(SourceKind.TrainerCard, st.TrainerCard), CardKey = st.TrainerCard, TierBefore = tier, TierAfter = to,
                    CausePokemonId = pokemon, CauseGrade = grade.Value, AbilitySlots = slots.ToArray(),
                });
            foreach (int slot in slots) OnUnlock(s, ctx, fx, seat, slot == 1 ? AbilityColor.Blue : AbilityColor.Red);
        }

        /// <summary>
        /// 창 OnUnlock(01 §7-3 — 「When unlocked」 15개, A20-6): 그 능력이 풀리는 순간 1번(파랑 먼저 — youngster R로 바꾼 새 카드도 <see cref="Swap"/>가 부른다).
        /// 배틀 한가운데(기절·포획 직후)에도 불리니 고를 것은 그 자리에서 묻지 않고 SeatState.UnlockDue에 미룬다(<see cref="UnlockAsk"/>, rulings A40).
        /// </summary>
        static void OnUnlock(GameState s, Ctx ctx, Emitter fx, int seat, AbilityColor color)
        {
            if (color == Blue && Has(s, ctx, seat, "lady", Blue))
            {
                s.Seats[seat].Jp += HeritageJp; // lady B 「When unlocked, gain 7 Journey Points」(rulings A39)
                Used(s, fx, seat, Blue);
                return;
            }
            if (!UnlockedNow(s, ctx, fx, seat, color)) ItemUnlockedNow(s, ctx, fx, seat, color); // 무녀 셋 B·R · undivided_acolyte B · youngster R(rulings A40) · 전용 아이템 능력(A41)
        }

        // ── 상시 보정(M6 「상시 보정」 18, rulings A32) ──
        // 계산할 때 주인 좌석의 카드를 훑는다(01 §7-1). 출처 = SourceKind.TrainerCard(카드 색인) — 우선도·공격력은 Mods[], 피해는 DamageBreakdown 단계.

        const AbilityColor Blue = AbilityColor.Blue, Red = AbilityColor.Red;

        internal static SourceRef CardSource(GameState s, int seat) => new SourceRef(SourceKind.TrainerCard, s.Seats[seat].TrainerCard);

        static bool IsType(Ctx ctx, PokemonState p, PokemonType a, PokemonType b)
        {
            foreach (var t in ctx.Cards.Pokemon[p.Def].Types)
                if (t == a || t == b) return true;
            return false;
        }

        static bool IsType(Ctx ctx, PokemonState p, PokemonType a) => IsType(ctx, p, a, a);

        /// <summary>「Your Weak Pokémon」: 카드 등급 Weak 또는 Starter(원작 커뮤니티 확인 [가정, rulings A32]), 우두머리 토큰 없음.</summary>
        public static bool IsWeak(Ctx ctx, PokemonState p)
        {
            var tier = ctx.Cards.Pokemon[p.Def].Tier;
            return !p.Alpha && (tier == EncounterTier.Weak || tier == EncounterTier.Starter);
        }

        /// <summary>「Your Strong Pokémon」: 카드 등급 Strong, 우두머리 토큰 없음(원작 커뮤니티 확인, rulings A32).</summary>
        public static bool IsStrong(Ctx ctx, PokemonState p) => !p.Alpha && ctx.Cards.Pokemon[p.Def].Tier == EncounterTier.Strong;

        /// <summary>
        /// 우선도 보정(BattleRules.PriorityMods가 부른다): ace_trainer R 강함 +1 · bug_catcher R 벌레 +2 · gardener R 풀·바위 +3 · hiker R 바위·땅 −1 · youngster B 약함 +1.
        /// 좌석의 카드는 하나라 Mod도 많아야 하나다.
        /// </summary>
        public static void PriorityMods(GameState s, Ctx ctx, int pokemon, List<Mod> into)
        {
            var p = s.Pokemon[pokemon];
            int seat = p.Owner;
            if (CardOf(s, seat) < 0) return;
            int d = 0;
            if (Has(s, ctx, seat, "ace_trainer", Red) && IsStrong(ctx, p)) d += 1;
            if (Has(s, ctx, seat, "bug_catcher", Red) && IsType(ctx, p, PokemonType.Bug)) d += 2;
            if (Has(s, ctx, seat, "gardener", Red) && IsType(ctx, p, PokemonType.Grass, PokemonType.Rock)) d += 3;
            if (Has(s, ctx, seat, "hiker", Red) && IsType(ctx, p, PokemonType.Rock, PokemonType.Ground)) d -= 1;
            if (Has(s, ctx, seat, "youngster", Blue) && IsWeak(ctx, p)) d += 1;
            if (Has(s, ctx, seat, "swimmer", Blue) && IsType(ctx, p, PokemonType.Water)) d += 1; // 「your Water-type Pokémon gain +1 initiative」(rulings A36)
            if (Apex(s, ctx, p, Red)) d += ApexInitiative; // dragon_tamer R 「+2 initiative」(rulings A41)
            d -= AcolytePenalty(s, ctx, p); // 무녀 빨강: 데려온 신 −(없는 조각 · 못 쓰러뜨린 UB ÷ 3)(rulings A40 · G2)
            d += GrowthBonus(s, ctx, p); // pokemon_breeder R 알 기술 +5 · pokemon_ranger R 보통 +열린 칸(rulings A39)
            if (d != 0) into.Add(new Mod { Source = CardSource(s, seat), Delta = d });
        }

        /// <summary>최대 체력 보정(PokemonState.HealthMod에 적어 둘 값): gardener R 풀·바위 −2 · hiker R 바위·땅 +4 · youngster B 약함 +1. 좌석의 포켓몬이 아니면 0.</summary>
        public static int HealthBonus(GameState s, Ctx ctx, PokemonState p)
        {
            int seat = p.Owner;
            if (p.Def < 0 || CardOf(s, seat) < 0) return 0;
            int d = 0;
            if (Has(s, ctx, seat, "gardener", Red) && IsType(ctx, p, PokemonType.Grass, PokemonType.Rock)) d -= 2;
            if (Has(s, ctx, seat, "hiker", Red) && IsType(ctx, p, PokemonType.Rock, PokemonType.Ground)) d += 4;
            if (Has(s, ctx, seat, "youngster", Blue) && IsWeak(ctx, p)) d += 1;
            d += CatchHealth(s, ctx, p); // fisherman R: 월척만큼(rulings A38)
            d -= AcolytePenalty(s, ctx, p); // 무녀 빨강(rulings A40 · G2)
            if (Apex(s, ctx, p, Red)) d += ApexHealth; // dragon_tamer R 「+6 health」(rulings A41)
            d += GrowthBonus(s, ctx, p); // pokemon_breeder R 알 기술 +5 · pokemon_ranger R 보통 +열린 칸(rulings A39)
            return d;
        }

        /// <summary>
        /// 모든 포켓몬의 최대 체력 보정(HealthMod)을 다시 센다 — PokemonRules.MaxHp(ctx, p)가 좌석을 몰라 적어 둔다(rulings A32). 부르는 자리: 명령 하나가 끝날 때(Engine.Submit) ·
        /// 등급이 오른 자리 · 카드(Def)·주인(Owner)이 바뀐 자리. 받은 피해는 그대로다. 보정이 줄어 받은 피해가 새 최대 체력 이상이 되면 체력 1로 남긴다 [가정, A32].
        /// </summary>
        public static void Refresh(GameState s, Ctx ctx)
        {
            Ranches(s, ctx); // pokemon_ranger B: 잠긴 칸 보관(rulings A39)
            foreach (var p in s.Pokemon)
            {
                int mod = HealthBonus(s, ctx, p);
                if (mod == p.HealthMod) continue;
                bool up = p.Def >= 0 && !PokemonRules.IsFainted(ctx, p);
                p.HealthMod = mod;
                if (up && PokemonRules.IsFainted(ctx, p)) p.Damage = PokemonRules.MaxHp(ctx, p) - 1;
            }
        }

        /// <summary>
        /// artist R 「If your party has a move of every type」: 파티의 기술(시그니처 + 배운 기술)의 타입 가짓수 + 와일드카드(타입없음 기술 · 남아 있는 알파 스트라이크 · 비스트 부스트 하나씩 —
        /// 「Typeless moves and abilities act as wildcards」, rulings A32)가 18타입을 덮는다.
        /// </summary>
        public static bool Masterpiece(GameState s, Ctx ctx, int seat)
        {
            int types = 0, wild = 0;
            var seen = new bool[TypeChart.TypeCount];
            void Count(int move)
            {
                var t = ctx.Cards.Moves[move].Type;
                if (t == PokemonType.Typeless) wild++;
                else if (!seen[(int)t])
                {
                    seen[(int)t] = true;
                    types++;
                }
            }
            foreach (int id in s.Seats[seat].Party)
            {
                if (id < 0) continue;
                var p = s.Pokemon[id];
                Count(ctx.Cards.Pokemon[p.Def].Signature);
                foreach (int m in p.Moves)
                    if (m >= 0) Count(m);
                if (LegendRules.HasAlphaStrike(p)) wild++;
                if (LegendRules.HasBeastBoost(p)) wild++;
            }
            return types + wild >= TypeChart.TypeCount - 1; // 타입없음을 뺀 18타입
        }

        /// <summary>그 좌석이 한쪽인 배틀의 좌석들(「During your battles」) — 배틀 밖이면 그 포켓몬의 주인만.</summary>
        static void BattleSeats(GameState s, int fallback, List<int> into)
        {
            if (s.Battle != null)
            {
                foreach (var side in s.Battle.Sides)
                    if (side.Owner >= 0) into.Add(side.Owner);
            }
            else if (fallback >= 0) into.Add(fallback);
        }

        /// <summary>
        /// 공격력 보정(BattleRules.RollPower가 부른다): artist R 모든 타입이면 +2 · bug_catcher R 벌레 포켓몬의 벌레 기술 +1 · sky_trainer R 비행 포켓몬 +1(받는 쪽이 비행이 아닐 때) ·
        /// team_aqua R·team_magma R 그 좌석의 배틀에서 양쪽 모두 물·불꽃 기술 +1(원작 커뮤니티 확인). 유물 +1(ruin_maniac R)은 RelicRules.PowerMods가 <see cref="RelicMod"/>로.
        /// </summary>
        public static void PowerMods(GameState s, Ctx ctx, int move, int attacker, int defender, List<Mod> into)
        {
            var a = s.Pokemon[attacker];
            var type = BattleRules.AttackType(s, ctx, move);
            int seat = a.Owner;
            WeatherMod(s, ctx, type, seat, into); // parasol_lady B 날씨 휠: 휠을 가진 좌석의 그 타입 기술 +2(rulings A41 · 결정 84 T10 ②)
            if (CardOf(s, seat) >= 0)
            {
                int d = 0;
                if (Apex(s, ctx, a, Red)) d += ApexPower; // dragon_tamer R 「+1 attack strength to its moves」(rulings A41)
                if (Has(s, ctx, seat, "artist", Red) && Masterpiece(s, ctx, seat)) d += 2;
                if (Has(s, ctx, seat, "bug_catcher", Red) && type == PokemonType.Bug && IsType(ctx, a, PokemonType.Bug)) d += 1;
                if (Has(s, ctx, seat, "sky_trainer", Red) && IsType(ctx, a, PokemonType.Flying) && !(defender >= 0 && IsType(ctx, s.Pokemon[defender], PokemonType.Flying))) d += 1;
                // hex_maniac R 「your Ghost-type moves gain +1 attack strength」 · chef R 마스터워크 스튜(그 배틀 끝까지 「moves that are the same type as them」 +1) — rulings A34
                if (Has(s, ctx, seat, "hex_maniac", Red) && type == PokemonType.Ghost) d += 1;
                if (s.Battle != null && IsType(ctx, a, type))
                    foreach (var side in s.Battle.Sides)
                        if (side.Owner == seat && side.Masterwork) d += 1;
                if (d != 0) into.Add(new Mod { Source = CardSource(s, seat), Delta = d });
            }
            if (type != PokemonType.Water && type != PokemonType.Fire) return;
            var seats = new List<int>(2);
            BattleSeats(s, seat, seats);
            foreach (int k in seats)
                if (Has(s, ctx, k, type == PokemonType.Water ? "team_aqua" : "team_magma", Red)) into.Add(new Mod { Source = CardSource(s, k), Delta = 1 });
        }

        /// <summary>ruin_maniac R 「Your Artifacts gain +1 attack strength to their bonuses」: 유물 보정이 붙은 그 공격에 +1 더(RelicRules.PowerMods가 유물 Mod를 넣은 바로 뒤에 부른다).</summary>
        public static void RelicMod(GameState s, Ctx ctx, int attacker, List<Mod> into)
        {
            int seat = s.Pokemon[attacker].Owner;
            if (Has(s, ctx, seat, "ruin_maniac", Red)) into.Add(new Mod { Source = CardSource(s, seat), Delta = 1 });
        }

        /// <summary>ruin_maniac R 「cannot be taken by other trainers」: 그 좌석의 유물은 트레이너전 승리 보상으로 가져갈 수 없다.</summary>
        public static bool RelicsGuarded(GameState s, Ctx ctx, int seat) => Has(s, ctx, seat, "ruin_maniac", Red);

        /// <summary>beauty B 「During all your rolls, critical hits deal +2 damage but status symbols deal -1 damage」의 값(공격 굴림 · 포획 굴림이 같이 쓴다).</summary>
        static int LadyLuck(IReadOnlyList<AttackDieFace> faces) => 2 * AttackDice.Count(faces, AttackDieFace.CriticalHit) - AttackDice.Count(faces, AttackDieFace.StatusSymbol);

        /// <summary>
        /// 효과 피해 보정(01 §6-4 ② — EffectRules.DamageMods가 부른다): beauty B 크리티컬 +2·심볼 −1 · beauty R 노말·악 기술이면 심볼 +1(둘은 더한다 [가정, rulings A32]).
        /// </summary>
        public static void DamageMods(GameState s, Ctx ctx, int move, int attacker, IReadOnlyList<AttackDieFace> faces, List<Mod> into)
        {
            if (attacker < 0) return;
            AmpMod(s, attacker, into); // guitarist B·R: 이번 공격에 쓴 앰프(rulings A41)
            int seat = s.Pokemon[attacker].Owner;
            if (CardOf(s, seat) < 0 || !Has(s, ctx, seat, "beauty", Blue)) return;
            int d = LadyLuck(faces);
            if (d != 0) into.Add(new Mod { Source = CardSource(s, seat), Delta = d });
            if (!Has(s, ctx, seat, "beauty", Red)) return;
            var type = BattleRules.AttackType(s, ctx, move);
            int n = type == PokemonType.Normal || type == PokemonType.Dark ? AttackDice.Count(faces, AttackDieFace.StatusSymbol) : 0;
            if (n != 0) into.Add(new Mod { Source = CardSource(s, seat), Delta = n });
        }

        /// <summary>포획 굴림의 합에 더할 값: beauty B(「all your rolls」 — 원작 커뮤니티 확인, rulings A32). faces = 정확도·매혹적인 스튜를 거친 면.</summary>
        public static int CaptureBonus(GameState s, Ctx ctx, int seat, IReadOnlyList<AttackDieFace> faces) => Has(s, ctx, seat, "beauty", Blue) ? LadyLuck(faces) : 0;

        /// <summary>scientist R 「your damage from positive type effectiveness is increased by 1」: 상성 합이 0보다 클 때만 +1(원작 커뮤니티 확인). 없으면 0.</summary>
        public static int EffectivenessBonus(GameState s, Ctx ctx, int attacker, int matchup, out SourceRef source)
        {
            int seat = s.Pokemon[attacker].Owner;
            bool on = matchup > 0 && Has(s, ctx, seat, "scientist", Red);
            source = on ? CardSource(s, seat) : SourceRef.None;
            return on ? 1 : 0;
        }

        /// <summary>받는 포켓몬 쪽 카드의 피해 보정 한 벌(<see cref="TakenMods"/>). Reductions = 총 데미지 감소(양수, 카드마다 — 없으면 null), Nullify = 문턱(0 = 없음), Cap = 상한(0 = 없음).</summary>
        public sealed class Taken
        {
            public List<Mod> Reductions;
            public int Nullify, Cap;
            public SourceRef NullifySource = SourceRef.None, CapSource = SourceRef.None;

            public int Reduction
            {
                get
                {
                    int n = 0;
                    if (Reductions != null)
                        foreach (var m in Reductions) n += m.Delta;
                    return n;
                }
            }
        }

        /// <summary>
        /// 받는 피해 보정(01 §6-4 ⑤~⑦): delinquent R 악·독 −2 · bird_keeper R 비행은 2 이하 무효 · poke_fan B 약함 상한 3 · roller_skater R 상한 6 —
        /// 그리고 attacked(공격받는 포켓몬 — 「when attacked」)면 team_aqua R·team_magma R의 물·불꽃 −1(그 좌석의 배틀 양쪽 모두).
        /// </summary>
        public static Taken TakenMods(GameState s, Ctx ctx, int defender, bool attacked, PokemonType? moveType = null)
        {
            var r = new Taken();
            var p = s.Pokemon[defender];
            int seat = p.Owner;
            void Reduce(int k, int n) => (r.Reductions ?? (r.Reductions = new List<Mod>(1))).Add(new Mod { Source = CardSource(s, k), Delta = n });
            if (CardOf(s, seat) >= 0)
            {
                if (Has(s, ctx, seat, "delinquent", Red) && IsType(ctx, p, PokemonType.Dark, PokemonType.Poison)) Reduce(seat, 2);
                if (Has(s, ctx, seat, "bird_keeper", Red) && IsType(ctx, p, PokemonType.Flying))
                {
                    r.Nullify = 2;
                    r.NullifySource = CardSource(s, seat);
                }
                if (Has(s, ctx, seat, "poke_fan", Blue) && IsWeak(ctx, p)) r.Cap = 3;
                else if (Has(s, ctx, seat, "roller_skater", Red)) r.Cap = 6;
                if (r.Cap > 0) r.CapSource = CardSource(s, seat);
                // parasol_lady R 「Your Pokémon take 2 less damage from moves that match the current Weather」(공격 — 날씨 타입 기술, rulings A41)
                if (attacked && moveType != null && moveType == WeatherType(s) && Has(s, ctx, seat, "parasol_lady", Red)) Reduce(seat, UmbrellaGuard);
            }
            if (!attacked) return r;
            bool water = IsType(ctx, p, PokemonType.Water), fire = IsType(ctx, p, PokemonType.Fire);
            if (!water && !fire) return r;
            var seats = new List<int>(2);
            BattleSeats(s, seat, seats);
            foreach (int k in seats)
            {
                if (water && Has(s, ctx, k, "team_aqua", Red)) Reduce(k, 1);
                if (fire && Has(s, ctx, k, "team_magma", Red)) Reduce(k, 1);
            }
            return r;
        }

        /// <summary>
        /// 기술 효과가 다른 포켓몬에게 주는 고정 피해(op Damage — earthquake 등)에 받는 쪽 카드를 건다(「from attacks」 — 원작 커뮤니티 확인 [가정, rulings A32]): 감소 → 문턱 → 상한.
        /// </summary>
        public static int SideDamage(GameState s, Ctx ctx, int pokemon, int damage)
        {
            var t = TakenMods(s, ctx, pokemon, false);
            return DamageRules.Calculate(new DamageInput { DiceSum = damage, Reduction = t.Reduction, NullifyAtOrBelow = t.Nullify, Cap = t.Cap }, s.Options.DamageCapStage);
        }

        /// <summary>fairy_tale_girl B 「Your Pokémon heal 1 extra damage whenever they are healed by any effect」: 0이 아닌 회복 하나에 더할 값.</summary>
        public static int HealBonus(GameState s, Ctx ctx, PokemonState p) => Has(s, ctx, p.Owner, "fairy_tale_girl", Blue) ? 1 : 0;

        // ── 덮개(plan M6 완료 조건 3) ──

        /// <summary>능력의 덮개 키 = 문자열 표의 키("trainer_ability.&lt;카드 키&gt;.&lt;1 파랑 | 2 빨강&gt;").</summary>
        public static string AbilityKey(string cardKey, AbilityColor color) => $"trainer_ability.{cardKey}.{SlotOf(color)}";

        /// <summary>
        /// 「옮긴 것」 등록부: 키(능력 = <see cref="AbilityKey"/>, 전용 아이템 = items.json 키) → 어디서 처리하나(파일·함수, 또는 「효과 없음」 근거).
        /// 능력·아이템 하나를 옮긴 묶음이 여기에 한 줄을 더한다. 여기 없는 키가 「미구현」이다(<see cref="Unported"/>) — 「전용 아이템 능력」 묶음에서 0.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Ported = new Dictionary<string, string>
        {
            // 전용 아이템 — 표시용 주사위 둘은 규칙 효과가 없다(rulings G1: 어느 카드 문구에도 없고 세이브 설명도 빈칸 — 보정은 엔진이 센다)
            { "item.health_dice", "효과 없음(rulings G1 — 체력 보정 표시물)" },
            { "item.initiative_dice", "효과 없음(rulings G1 — 우선도 보정 표시물)" },
            // 상시 보정 18(M6 「상시 보정」, rulings A32 — 이 파일의 「상시 보정」 절)
            { "trainer_ability.ace_trainer.2", "TrainerRules.PriorityMods — 강함 우선도 +1" },
            { "trainer_ability.artist.2", "TrainerRules.PowerMods·Masterpiece — 모든 타입이면 공격력 +2" },
            { "trainer_ability.beauty.1", "TrainerRules.DamageMods·CaptureBonus — 크리티컬 +2·심볼 −1(포획 굴림에도)" },
            { "trainer_ability.beauty.2", "TrainerRules.DamageMods — 노말·악 기술의 심볼 +1" },
            { "trainer_ability.bird_keeper.2", "TrainerRules.TakenMods — 비행은 2 이하 무효(BattleFlow.Damage ⑥)" },
            { "trainer_ability.bug_catcher.2", "TrainerRules.PriorityMods +2 · PowerMods 벌레 기술 +1" },
            { "trainer_ability.delinquent.2", "TrainerRules.TakenMods — 악·독 총 데미지 감소 2" },
            { "trainer_ability.fairy_tale_girl.1", "TrainerRules.HealBonus — 회복마다 +1(BattleFlow.ApplyBuffer)" },
            { "trainer_ability.gardener.2", "TrainerRules.PriorityMods +3 · HealthBonus −2(PokemonState.HealthMod)" },
            { "trainer_ability.hiker.2", "TrainerRules.HealthBonus +4 · PriorityMods −1" },
            { "trainer_ability.poke_fan.1", "TrainerRules.TakenMods — 약함 상한 3" },
            { "trainer_ability.roller_skater.2", "TrainerRules.TakenMods — 상한 6" },
            { "trainer_ability.ruin_maniac.2", "TrainerRules.RelicMod(RelicRules.PowerMods) 유물 +1 · RelicsGuarded(트레이너전 보상에서 뺌)" },
            { "trainer_ability.scientist.2", "TrainerRules.EffectivenessBonus — 상성이 양수면 +1" },
            { "trainer_ability.sky_trainer.2", "TrainerRules.PowerMods — 비행 포켓몬 +1(받는 쪽이 비행이 아닐 때)" },
            { "trainer_ability.team_aqua.2", "TrainerRules.PowerMods 물 기술 +1 · TakenMods 물 포켓몬 −1(배틀 양쪽)" },
            { "trainer_ability.team_magma.2", "TrainerRules.PowerMods 불꽃 기술 +1 · TakenMods 불꽃 포켓몬 −1(배틀 양쪽)" },
            { "trainer_ability.youngster.1", "TrainerRules.HealthBonus +1 · PriorityMods +1(약함)" },
            // 배틀 굴림·공격력·타입 12(M6 묶음 3, rulings A34 — TrainerRules.Battle.cs)
            { "trainer_ability.chef.2", "TrainerRules.BeforeRoll·Ability(AbChef) — 규토리 모든 색 1씩 → BattleSide.Masterwork(PowerMods +1)" },
            { "trainer_ability.essentia.2", "TrainerRules.Opportune(BattleFlow.Damage) — 움직이지 않고 건 야생 배틀의 첫 공격 2배" },
            { "trainer_ability.ginkgo_merchant.2", "TrainerRules.BeforeRoll·Ability(AbGinkgo) — 규토리 1 → BattleState.StrengthTo 4·5·6" },
            { "trainer_ability.hex_maniac.1", "TrainerRules.Hexing(EffectRules.Step)·Ability(AbHex) — 상태 주사위로 대신(BattleState.HexAs)" },
            { "trainer_ability.hex_maniac.2", "TrainerRules.Hexing·Ability(AbHex) — 상태를 더 건다(StatusRules.InflictExtra) · PowerMods 고스트 +1" },
            { "trainer_ability.ninja_boy.1", "TrainerRules.AsksToken(BattleFlow.OrderStep·ApplyToken) — 토큰으로 우선도와 상관없이 선턴" },
            { "trainer_ability.ninja_boy.2", "TrainerRules.TokenUsed·Ability(AbToxinDart) — 토큰을 쓰면 독·혼란·졸림 중 하나(강제)" },
            { "trainer_ability.poke_maniac.1", "TrainerRules.DisguiseMove(ChoiceRules.BattleActions) — 상대의 시그니처 기술" },
            { "trainer_ability.psychic.2", "TrainerRules.PsychicLegal·ApplyPsychic(Reactions — 굴린 뒤 반응 창, 결정 131) — 주사위 1개를 다른 면으로" },
            { "trainer_ability.swimmer.2", "TrainerRules.BeforeRoll·Ability(AbSwimmer) — 물 기술의 타입을 벌레·비행으로" },
            { "trainer_ability.triathlete.2", "TrainerRules.BeforeRoll·Ability(AbTriathlete) — 아는 세 타입 중 하나로" },
            { "trainer_ability.veteran.2", "TrainerRules.Rolled·Ability(AbVeteran) — 얼음·드래곤 기술에 심볼 2개까지" },
            // 배틀 피해 뒤·기절·교체·상태 11(M6 묶음 4, rulings A35 — TrainerRules.After.cs)
            { "trainer_ability.aroma_lady.2", "TrainerRules.Dealt(BattleFlow.DamageStep) — 풀·페어리 기술로 준 피해의 절반(올림) 회복" },
            { "trainer_ability.black_belt.2", "MaintKind.Afflict(TrainerRules.CheckAfflict·Afflict) 화상 걸기 · Spirited(BattleFlow.Damage) 격투의 화상 = 총 피해 +2" },
            { "trainer_ability.expert.2", "TrainerRules.Protector·Protected(LegendRules.Fainted) — 비스트 부스트 대신 회복 3 · 피로 0 · 다음 공격 +2" },
            { "trainer_ability.fairy_tale_girl.2", "MaintKind.Afflict 졸림 걸기 · Dreaming(EffectRules.AddStatus)·TurnEnd — 페어리의 졸림 = 턴 끝 1 회복" },
            { "trainer_ability.kindler.2", "TrainerRules.Ignition·AbilityAfter(AbKindler) — 장작 1(SeatState.Firewood)로 화상 + 2 피해" },
            { "trainer_ability.lady.2", "TrainerRules.PotionTargets(ChoiceRules.BattleActions)·UsePotion — 배틀당 1번 완전 회복(BattleSide.Potion), 내 배틀 행동에서만 · 턴을 쓰지 않는다(결정 84 T6)" },
            { "trainer_ability.scuba_diver.2", "TrainerRules.AfterAttack(BattleState.ScubaBy)·Dived — 물 기술 뒤 교체 + 상태이상 회복" },
            { "trainer_ability.team_rocket.1", "TrainerRules.CanSteal(ChoiceRules.BattleActions)·ThiefMod·Stolen(BattleFlow.CaptureResolve) — 트레이너전 포획 −2 · 디아루가 재굴림 없음 · 그 배틀에 못 냄(BattleRules.Taken) · 둘째 지닌물건은 가방으로(결정 84 T7)" },
            { "trainer_ability.team_rocket.2", "TrainerRules.OwnFainted 상대에게 2 · BlastOff·AbilityAfter(AbBlastOff) 아무 칸으로(TurnFlow.Whiteout)" },
            { "trainer_ability.undivided_acolyte.2", "TrainerRules.AfterAttack — 공격 뒤 상대 체력 2 이하면 기절" },
            { "trainer_ability.worker.2", "TrainerRules.SwitchedIn(BattleFlow.SendIn) — 강철이 나오면 상대 피로 +3 · 교체·도망 금지" },
            // 이동 11(M6 묶음 5, rulings A36 — TrainerRules.Move.cs · MoveRules.MoveTerms · MapActions)
            { "trainer_ability.bird_keeper.1", "TrainerRules.Whistles(MapActions.Legal·Fly) — 아무 칸에서 규토리 1로 마을로" },
            { "trainer_ability.essentia.1", "TrainerRules.TurnClosing·ClosingWalk(BattleFlow.Close) — 배틀 뒤 남은 이동력으로 걷기만" },
            { "trainer_ability.hiker.1", "TrainerRules.FreeTile 산 2칸 무료 · Terms.AnyElevation 높이 무시" },
            { "trainer_ability.picnicker.1", "TrainerRules.FreeTile 숲 2칸 무료 · Campsites(MapActions UseAbility) 야영지로 날기" },
            { "trainer_ability.roller_skater.1", "TrainerRules.FreeTile 평지 2칸 무료 · WildFlees(BattleFlow.TryEnd) 야생은 둘을 쓰러뜨려야 도망" },
            { "trainer_ability.sky_trainer.1", "TrainerRules.Terms.Descend(MoveRules.StepCost) — 2층 이상 내려서기 + 활공 4칸" },
            { "trainer_ability.swimmer.1", "TrainerRules.FreeTile 바다 2칸 무료 · PriorityMods 물 +1" },
            { "trainer_ability.team_aqua.1", "TrainerRules.DiveTargets·Dive(MapActions EndTurn 답의 Target) — 턴 끝에 다른 바다 칸으로" },
            { "trainer_ability.team_magma.1", "TrainerRules.DiveTargets·Dive — 턴 끝에 다른 산 칸으로" },
            { "trainer_ability.triathlete.1", "TrainerRules.FreeTile — 산이 아닌 칸 2칸 무료" },
            { "trainer_ability.worker.1", "TrainerRules.CanBuild·Build·Paved(GameState.Roads · 켜기 전 밟은 칸 SeatState.Trod — 결정 84 T11 ②) · MoveRules.StepCost 이어진 길 1" },
            // 맵 상호작용 7(M6 묶음 6, rulings A37 — TrainerRules.Map.cs · MapActions의 UseAbility 답)
            { "trainer_ability.gardener.1", "TrainerRules.MapAbilities·UseMap — 선 나무에서 규토리 1" },
            { "trainer_ability.kindler.1", "TrainerRules.MapAbilities·UseMap — 나무 하나를 맵에서 없애고 장작 3(SeatState.Firewood)" },
            { "trainer_ability.psychic.1", "TrainerRules.ReachCells·ReachAnswer·UseMap(MapActions.Interactions) — 2칸 안의 칸과 상호작용 · TelekinesisTo(TradeRules 방식 Telekinesis) — 2칸 안 좌석과 거래(결정 84 T12 ②)" },
            { "trainer_ability.ruin_maniac.1", "TrainerRules.MapAbilities·UseMap·MapAnswered — 특별한 숨겨진 장소 공개(TokenState.Special) · 유물이면 가서 붙임" },
            { "trainer_ability.scientist.1", "TrainerRules.Observe·OpenTeach·MapAnswered — 배틀 없는 찾아보기 + 기술 가르치기" },
            { "trainer_ability.scuba_diver.1", "TrainerRules.SunkenPile·UseMap — 바다 칸에서 숨겨진 장소 버림 더미 한 장을 그 칸에" },
            { "trainer_ability.veteran.1", "TrainerRules.Stashed(HiddenSpotRules.Resolve) — 규토리 더미 같은 색 +2 · 이동력 +1" },
            // 보상·남의 행동 반응 11 + 월척 주사위(M6 묶음 7, rulings A38 — TrainerRules.Reward.cs)
            { "trainer_ability.aroma_lady.1", "TrainerRules.SweetScent(BattleFlow.CaptureRolled) — 나무의 포획 굴림 심볼 하나 → 일반 타격" },
            { "trainer_ability.fisherman.1", "TrainerRules.SweetScent 산호의 포획 굴림 · Landed(BattleFlow.CaptureResolve) 월척 주사위" },
            { "trainer_ability.fisherman.2", "TrainerRules.HealthBonus(CatchHealth) 월척만큼 체력 · MaintKind.CatchSize(CheckCatchSize·GrowCatch) JP 2로 한 단계" },
            { "item.catch_size_dice", "PokemonState.CatchSize(면 0~5 = −1·0·1·2·4·6, rulings G1) — TrainerRules.Landed·CatchHealth" },
            { "trainer_ability.artist.1", "TrainerRules.Sketches(ChoiceRules TeachMove, Window.BattleEnd) — 배울 수 있는 타입과 상관없이" },
            { "trainer_ability.backpacker.1", "TrainerRules.TentPitched·Tent(BattleFlow.Close)·RewardAnswered — 배틀 뒤 한 마리 완전 회복" },
            { "trainer_ability.black_belt.1", "TrainerRules.Training(BattleFlow.Conclude) — 쓰러뜨린 야생마다 JP +2" },
            { "trainer_ability.bug_catcher.1", "TrainerRules.Frenzy(BattleFlow.Conclude)·FrenzyNow(FinishClose) — 벌레 포획 JP +2 · 추가 턴" },
            { "trainer_ability.gentleman.1", "TrainerRules.Coins(BattleFlow.Conclude) — 야생 포획·격파 규토리 두 배" },
            { "trainer_ability.delinquent.1", "TrainerRules.OfferingTaken(ShrineRules.Offer) — 남이 바친 규토리" },
            { "trainer_ability.ginkgo_merchant.1", "TrainerRules.Monopoly(BankRules.Apply) — 남이 은행 거래에 낸 규토리" },
            // 제작·사당·스탬프·진화·파티 13(M6 묶음 8, rulings A39 — TrainerRules.Craft.cs · backpacker R은 같은 묶음의 뒤 커밋)
            { "trainer_ability.ace_trainer.1", "TrainerRules.CanRetry(ChoiceRules EncounterPick UseAbility · MapActions.Search retried) 조우당 한 번 다시 · Recipe(CraftRules) 배틀 아이템 색마다 하나" },
            { "trainer_ability.chef.1", "MaintKind.Convert(TrainerRules.CheckCraftMaint·ApplyCraftMaint) — 같은 단계 규토리 바꾸기" },
            { "trainer_ability.picnicker.2", "TrainerRules.Bonuses·Bonus(CraftRules.Legal·Make, Craft의 Arg) — 다른 스튜의 효과 공짜" },
            { "trainer_ability.poke_fan.2", "TrainerRules.Bonuses·Bonus(CraftRules.Legal·Make, Craft의 Arg) — 배틀 아이템 하나 더" },
            { "trainer_ability.expert.1", "TrainerRules.ShrineKeeper(ShrineRules.Offer) — 사당 카드 1장 더 뽑기(쓰는 수 그대로)" },
            { "trainer_ability.collector.1", "TrainerRules.Twice(StampRules.Gain) — 스탬프 보상 두 번" },
            { "trainer_ability.collector.2", "MaintKind.Adopt 풀의 포켓몬을 파티로 · TrainerRules.Pooled(놓아주기·기술 버림) — RuleOptions.CollectorDiscards" },
            { "trainer_ability.interviewers.2", "MaintKind.PassMove(TrainerRules.CheckCraftMaint·ApplyCraftMaint) — 배운 기술을 파티 안에서 맞바꾸기" },
            { "trainer_ability.lady.1", "TrainerRules.OnUnlock JP +7 · Heritage(EvolveRules.Cost) 진화 비용 −2" },
            { "trainer_ability.pokemon_breeder.1", "TrainerRules.EggAnswers(EvolveRules.Legal, Evolve Arg 2)·Hatch — 진화 대신 알 기술(PokemonState.EggMove · 진화 불가)" },
            { "trainer_ability.pokemon_breeder.2", "TrainerRules.GrowthBonus(HealthBonus·PriorityMods) — 알 기술 포켓몬 체력·우선도 +5" },
            { "trainer_ability.pokemon_ranger.1", "SeatState.Ranch(TrainerRules.Ranches) · PokemonRules.AddToParty·Fights — 잠긴 칸 보관(배틀 불가). UB 균열 카드 보관은 BreachRules.OpenAttach(보관할지 묻는다 · UbState.BreachStored — 결정 84 U11)" },
            { "trainer_ability.pokemon_ranger.2", "TrainerRules.GrowthBonus — 보통 포켓몬 체력·우선도 +연 칸 수(UB +3)" },
            { "trainer_ability.backpacker.2", "PokemonState.HeldItem2 · MaintKind.HoldItem Slot 1(TrainerRules.Pockets) · PokemonRules.HoldsItem·HeldItems.ItemOf(두 칸 훑기) — 서로 다른 지닌물건 둘(유물도) · Unpocket(거래로 남에게 가면 둘째 것은 가방으로)" },
            { "trainer_ability.interviewers.1", "TrainerRules.StampGained·Scoop(TurnFlow.OpenMapAction · BattleFlow.Close · ScoopAfterCommand)·ScoopEnded — 남이 스탬프를 얻으면 가서 도전 · 이기면 그 스탬프 · 턴 밖이면 ScoopBack(끝낼 턴 없음 — 결정 84 T14)" },
            // 해금될 때 — 무녀·카드 바꾸기 8 + 아르세우스의 추종자(M6 묶음 9, rulings A40 — TrainerRules.Unlock.cs)
            { "trainer_ability.diamond_acolyte.1", "TrainerRules.UnlockedNow·Devoted(ChoiceRules 추종자 고정) · FateRules.FollowerWorth 디아루가 3장 효과의 2장 몫" },
            { "trainer_ability.pearl_acolyte.1", "TrainerRules.UnlockedNow·Devoted · FateRules.FollowerWorth 펄기아 3장 효과의 2장 몫" },
            { "trainer_ability.platinum_acolyte.1", "TrainerRules.UnlockedNow·Devoted · FateRules.FollowerWorth 기라티나 3장 효과의 2장 몫" },
            { "trainer_ability.diamond_acolyte.2", "TrainerRules.Chosen(전설 덱의 디아루가 → 파티 · Join·DueRelease) · AcolytePenalty(HealthBonus·PriorityMods — RuleOptions.AcolyteRedTemporary)" },
            { "trainer_ability.pearl_acolyte.2", "TrainerRules.Chosen(펄기아) · AcolytePenalty" },
            { "trainer_ability.platinum_acolyte.2", "TrainerRules.Chosen(기라티나) · AcolytePenalty" },
            { "trainer_ability.undivided_acolyte.1", "SinnohGod.Arceus 추종자(FractureRules.Follows·Counts·MoveBonus · TradeRules.SameGod·DifferentGod · FateRules.FollowerWorth — RuleOptions.ArceusFollowerBlessing)" },
            { "trainer_ability.youngster.2", "TrainerRules.Career(덱 5장 → TrainerCardOffer · DueSwap)·UnlockAsk·Swap(TrainerCardSwapped · 새 카드 골드 + OnUnlock 파랑·빨강)" },
            // 해금될 때 — 전용 아이템 능력 10 + 전용 아이템 6(M6 묶음 10, rulings A41 — TrainerRules.Items.cs)
            { "trainer_ability.dragon_tamer.1", "TrainerRules.Hatch·Hatchling(약한 덱의 드래곤 · DueDragon) · ApexCost(EvolveRules.Cost +5) · Bound(뗄 수 없음)" },
            { "trainer_ability.dragon_tamer.2", "TrainerRules.Apex(HealthBonus +6 · PriorityMods +2 · PowerMods +1) — 골드부터" },
            { "trainer_ability.gentleman.2", "TrainerRules.CherishReady(ChoiceRules UseAbility CherishTarget) · BattleFlow.Cherish(CaptureResolve cherish) · Released(SeatState.CherishIn)" },
            { "trainer_ability.guitarist.1", "SeatState.Amp · TrainerRules.Amplify·CaptureCharge(심볼마다 +1, 5까지) · AmpLegal·ApplyAmp(Reactions — 굴린 뒤 반응 창, 결정 134)·AmpMod(DamageMods ②)" },
            { "trainer_ability.guitarist.2", "TrainerRules.AmpLegal·ApplyAmp(두 배 — 전기 기술, 결정 134) · BattleSide.AmpSpent(그 배틀에서 다시 못 씀)" },
            { "trainer_ability.parasol_lady.1", "GameState.Weather · DueWeather(첫 날씨 무료) · WeatherMod(PowerMods +2 휠을 가진 좌석만 — 결정 84 T10 ②) · WeatherAnswers·ChangeWeather(사당 JP 3 · WeatherSet)" },
            { "trainer_ability.parasol_lady.2", "TrainerRules.TakenMods(moveType) — 날씨 타입 기술 피해 −2" },
            { "trainer_ability.poke_maniac.2", "가방에 대타출동(Holdable — 정비 HoldItem) · TrainerRules.Substitute(BattleStep.PreDamage)·AbilityItems(AbSubstitute)·BattleFlow.SwapIn" },
            { "trainer_ability.tourist.1", "TrainerRules.TourAnswers·Stamp(MapAbilities·UseMap — SeatState.Tour · Snaps)" },
            { "trainer_ability.tourist.2", "TrainerRules.SnapOf(정비 SetMove로 스냅 가르치기) · Completed(8곳이면 JP 30)" },
            { "item.apex_dragon", "PokemonState.HeldItem(dragon_tamer B가 붙인다) — TrainerRules.Bound·Apex" },
            { "item.cherish_ball", "SeatState.CherishIn — gentleman R(뒤집기는 그림뿐 [가정 §5 #147 ⑩])" },
            { "item.amplifier", "SeatState.Amp(면 0~5, rulings G1) — guitarist B·R" },
            { "item.weather_wheel", "GameState.Weather(네 칸 쾌청·눈·비·모래바람 — TrainerRules.WeatherTypes, rulings G1)" },
            { "item.weather_wheel_base", "효과 없음 — 날씨 휠의 받침(rulings G1, 날씨 휠과 한 벌로 CardItems에)" },
            { "item.substitute", "가방의 지닌물건(TrainerRules.Holdable) — TrainerRules.Substitute" },
            { "item.landmark_tour", "SeatState.Tour(유물 전설 장소 8곳 — TrainerRules.Landmarks, rulings G1)" },
            { "item.follower_of_arceus", "SeatState.CardItems(undivided_acolyte B가 받는다) — 효과는 SinnohGod.Arceus 추종자 값(rulings A40)" },
        };

        /// <summary>덮개가 세는 모든 키: 능력 102(카드 표 순서 × 파랑·빨강) + 전용 아이템 11(아이템 표의 TrainerItem 행).</summary>
        public static List<string> AllKeys(Ctx ctx)
        {
            var keys = new List<string>(113);
            foreach (var card in ctx.Cards.TrainerCards)
                foreach (var a in card.Abilities) keys.Add(AbilityKey(card.Key, a.Color));
            foreach (var item in ctx.Items.Items)
                if (item.Kind == ItemKind.TrainerItem) keys.Add(item.Key);
            return keys;
        }

        /// <summary>아직 옮기지 않은 키(AllKeys 순서).</summary>
        public static List<string> Unported(Ctx ctx)
        {
            var left = AllKeys(ctx);
            left.RemoveAll(k => Ported.ContainsKey(k));
            return left;
        }
    }
}
