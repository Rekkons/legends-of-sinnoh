using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 제작하는 지닌물건(배틀 아이템) 6개의 훅(S10, 룰북 p.14, M5 「제작·은행·진화」). 든 포켓몬에게만 효과가 있다(p.27). 민첩의 띠(우선도 +2 · 공격력 −1)는 BattleRules가 직접 훑는다(M3).
    /// - 결단의 머리띠 「Increase the attack strength of a move by 1, but the user may only use this move until switched out.」 —
    ///   공격력 +1(BattleRules.PowerMods), 나온 뒤 처음 고른 기술에 묶인다(나갈 때까지 — 교체·기절·배틀 끝).
    /// - 행운의 알 「When the user attacks, replace 2 status symbols with 1 critical hit if possible.」 · 진화의 휘석 「When the user is attacked, remove 2 regular hits.
    ///   Only works on Weak/Moderate Pokémon or Pokémon that are not fully evolved.」 — 굴린 뒤 면 바꾸기(EffectDb.LuckyEggEffect·EvioliteEffect 프레임, 순서는 결정 72 틀).
    /// - 별 방패 「Only on the first attack against the user, the user takes 2 less damage and is immune to any status effects inflicted this turn.」 ·
    ///   폭발수정 「Only on the first attack against the user, deal 1 damage to the attacker and prevent them from switching out this turn.」 —
    ///   「첫 공격」 = 그 배틀에서 든 포켓몬이 처음 공격받을 때 한 번 [가정](교체해 나갔다 와도 다시 되지 않는다). 피해 계산 때 <see cref="Attacked"/>가 쓴 것으로 적는다.
    /// 기록은 PokemonState.Mods의 지속 보정(EffectOp.MoveLock·ItemUsed·SwitchLock — 데이터 op가 아니다)이라 나가거나 배틀이 끝나면 ModRules가 지운다 — 새 상태 칸이 없다.
    /// </summary>
    public static class HeldItems
    {
        public const string ChoiceBand = "item.choice_band";
        public const string LuckyEgg = "item.lucky_egg";
        public const string Eviolite = "item.eviolite";
        public const string StarShield = "item.star_shield";
        public const string BlastCrystal = "item.blast_crystal";

        public const int ChoiceBandPower = 1;
        public const int StarShieldReduction = 2;
        public const int BlastCrystalDamage = 1;

        /// <summary>그 포켓몬이 그 아이템을 들고 있나.</summary>
        public static bool Holds(Ctx ctx, PokemonState p, string key) => ItemOf(ctx, p, key) >= 0;

        /// <summary>그 포켓몬이 지닌 그 아이템의 색인(지닌물건 두 칸 — backpacker R). 없으면 −1.</summary>
        public static int ItemOf(Ctx ctx, PokemonState p, string key)
        {
            if (p == null) return -1;
            if (p.HeldItem >= 0 && ctx.Items.Items[p.HeldItem].Key == key) return p.HeldItem;
            if (p.HeldItem2 >= 0 && ctx.Items.Items[p.HeldItem2].Key == key) return p.HeldItem2;
            return -1;
        }

        static PokemonMod Find(PokemonState p, EffectOp op)
        {
            foreach (var m in p.Mods)
                if (m.Op == op) return m;
            return null;
        }

        // ── 결단의 머리띠 ──

        /// <summary>결단의 머리띠로 묶인 기술. 없으면 −1.</summary>
        public static int LockedMove(Ctx ctx, PokemonState p)
        {
            if (!Holds(ctx, p, ChoiceBand)) return -1;
            var m = Find(p, EffectOp.MoveLock);
            return m != null ? m.Value : -1;
        }

        /// <summary>기술을 골랐다(BattleFlow): 결단의 머리띠를 들었고 아직 묶이지 않았으면 그 기술에 묶는다.</summary>
        internal static void MoveChosen(GameState s, Ctx ctx, int pokemon, int move)
        {
            if (pokemon < 0) return;
            var p = s.Pokemon[pokemon];
            if (!Holds(ctx, p, ChoiceBand)) return;
            var m = Find(p, EffectOp.MoveLock);
            if (m != null)
            {
                m.Value = move; // 묶인 기술을 정비로 버렸을 때만 다르다(LockMoves) — 새로 고른 기술에 다시 묶인다
                return;
            }
            p.Mods.Add(new PokemonMod
            {
                Scope = ModScope.UntilSwitch, Role = EffectRole.Attacker, Op = EffectOp.MoveLock, Face = AttackDieFace.None, Value = move,
                Source = new SourceRef(SourceKind.Item, ItemOf(ctx, p, ChoiceBand)),
            });
        }

        /// <summary>쓸 수 있는 기술 후보를 묶인 기술 하나로 줄인다. 묶인 기술이 후보에 없으면(정비로 그 기술을 버렸다) 그대로 둔다.</summary>
        internal static void LockMoves(Ctx ctx, PokemonState p, List<int> moves)
        {
            int locked = LockedMove(ctx, p);
            if (locked == -1 || !moves.Contains(locked)) return;
            moves.Clear();
            moves.Add(locked);
        }

        // ── 행운의 알 · 진화의 휘석(굴린 뒤 면 바꾸기) ──

        /// <summary>진화의 휘석이 듣는 포켓몬: 약한·적당한 등급 카드(스타터 포함)이거나 진화가 남았다.</summary>
        public static bool EvioliteWorks(Ctx ctx, PokemonState p)
        {
            var d = ctx.Cards.Pokemon[p.Def];
            return d.Tier == EncounterTier.Weak || d.Tier == EncounterTier.Moderate || d.Tier == EncounterTier.Starter || d.EvolveInto.Count > 0;
        }

        /// <summary>그 자리(공격하는 쪽·받는 쪽)에서 그 포켓몬의 지닌물건이 쌓을 굴린 뒤 면 바꾸기 효과 Id. 없으면 −1.</summary>
        public static int FaceEffect(Ctx ctx, PokemonState p, EffectRole role)
        {
            if (role == EffectRole.Attacker) return Holds(ctx, p, LuckyEgg) ? EffectDb.LuckyEggEffect : -1;
            return Holds(ctx, p, Eviolite) && EvioliteWorks(ctx, p) ? EffectDb.EvioliteEffect : -1;
        }

        // ── 별 방패 · 폭발수정(첫 공격에만) ──

        /// <summary>「첫 공격에만」 아이템을 들었고 이 배틀에서 아직 공격받지 않았다.</summary>
        static bool FirstAttackReady(Ctx ctx, PokemonState p, string key) => Holds(ctx, p, key) && Find(p, EffectOp.ItemUsed) == null;

        /// <summary>별 방패가 이번 공격에서 줄일 총 데미지(01 §6-4 ⑤). 첫 공격이 아니면 0.</summary>
        public static int Reduction(Ctx ctx, PokemonState defender) => FirstAttackReady(ctx, defender, StarShield) ? StarShieldReduction : 0;

        /// <summary>폭발수정이 이번 공격에서 공격자에게 줄 피해. 첫 공격이 아니면 0.</summary>
        public static int BlastDamage(Ctx ctx, PokemonState defender) => FirstAttackReady(ctx, defender, BlastCrystal) ? BlastCrystalDamage : 0;

        /// <summary>별 방패가 이번 턴 걸리는 상태이상을 막는다(첫 공격을 받는 그 턴 — 그 공격이 끝나면 풀린다).</summary>
        public static bool Shielded(Ctx ctx, PokemonState p)
        {
            if (!Holds(ctx, p, StarShield)) return false;
            var m = Find(p, EffectOp.ItemUsed);
            return m != null && m.Fresh;
        }

        /// <summary>
        /// 공격받았다(BattleFlow 피해 계산 바로 뒤 — 빗나가거나 0이어도 공격이다): 「첫 공격에만」 아이템을 쓴 것으로 적고(배틀 끝까지), 폭발수정이면 공격자에게 1 피해(그 턴 피해 버퍼 — 동시 적용)와 이번 턴 교체 잠금.
        /// </summary>
        internal static void Attacked(GameState s, Ctx ctx, BattleState b, int attacker, int defender)
        {
            if (defender < 0) return;
            var d = s.Pokemon[defender];
            bool blast = FirstAttackReady(ctx, d, BlastCrystal);
            if (!blast && !FirstAttackReady(ctx, d, StarShield)) return;
            int used = ItemOf(ctx, d, blast ? BlastCrystal : StarShield);
            var src = new SourceRef(SourceKind.Item, used);
            d.Mods.Add(new PokemonMod { Scope = ModScope.BattleEnd, Role = EffectRole.Defender, Op = EffectOp.ItemUsed, Face = AttackDieFace.None, Value = used, Source = src, Fresh = true });
            if (!blast || attacker < 0) return;
            BattleFlow.Buffer(b, attacker, -BlastCrystalDamage, src);
            s.Pokemon[attacker].Mods.Add(new PokemonMod { Scope = ModScope.UntilSwitch, Role = EffectRole.Attacker, Op = EffectOp.SwitchLock, Face = AttackDieFace.None, Value = b.Turn, Source = src });
        }

        /// <summary>폭발수정에 맞아 이번 배틀 턴에 교체하지 못한다.</summary>
        public static bool SwitchLocked(GameState s, int pokemon)
        {
            if (pokemon < 0 || s.Battle == null) return false;
            foreach (var m in s.Pokemon[pokemon].Mods)
                if (m.Op == EffectOp.SwitchLock && m.Value == s.Battle.Turn) return true;
            return false;
        }
    }
}
