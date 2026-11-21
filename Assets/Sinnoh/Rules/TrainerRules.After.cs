using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 배틀 피해 뒤·기절·교체·상태 11(M6 묶음 4, rulings A35): aroma_lady R · undivided_acolyte R · scuba_diver R · worker R · team_rocket B·R · expert R · lady R ·
    /// kindler R · black_belt R · fairy_tale_girl R. 배틀 안 물음은 묶음 3의 프레임(<see cref="EffectDb.TrainerAbility"/>)을 그대로 쓴다.
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>프레임 Locals[0]의 능력 번호(묶음 3의 1~8에 이어서).</summary>
        public const int AbKindler = 9, AbBlastOff = 10;

        /// <summary>undivided_acolyte R 「the opposing Pokémon faints if they have 2 or less health remaining」.</summary>
        public const int JudgementHp = 2;
        /// <summary>worker R 「increase the battle fatigue of the opposing Pokémon by 3」.</summary>
        public const int RoadWorksFatigue = 3;
        /// <summary>team_rocket R 「deal 2 damage to the opposing Pokémon」 · kindler R 「deal 2 damage」 · black_belt R 「Increase the total damage you deal by 2」.</summary>
        public const int BlastDamage = 2, IgnitionDamage = 2, SpiritDamage = 2;
        /// <summary>team_rocket B 「with -2 to your capture rolls」.</summary>
        public const int ThiefPenalty = 2;
        /// <summary>expert R 「heal 3 damage … increase the attack strength of their next attack by 2」(비스트 부스트의 +1 대신 — 원작 커뮤니티 확인).</summary>
        public const int ProtectorHeal = 3, ProtectorPower = 2;

        static bool Seated(GameState s, int seat) => seat >= 0 && seat < s.SeatCount && CardOf(s, seat) >= 0;

        // ── 피해를 준 자리 · 공격 뒤 ──

        /// <summary>
        /// aroma_lady R 「When you attack with a Grass-type or Fairy-type move, the user heals for half the damage dealt, rounded up」: 이번 공격이 대상에게 준 최종 피해(대상의 남은 체력과 상관없이)의
        /// 절반(올림)을 그 턴 버퍼로 회복한다 — 기술의 흡수(giga_drain 내림)와 따로 더한다(원작 커뮤니티 확인).
        /// </summary>
        internal static void Dealt(GameState s, Ctx ctx, Emitter fx, BattleState b, int att, int final)
        {
            int seat = s.Pokemon[att].Owner;
            if (final <= 0 || !Seated(s, seat) || !Has(s, ctx, seat, "aroma_lady", Red)) return;
            var type = BattleRules.AttackType(s, ctx, b.Move);
            if (type != PokemonType.Grass && type != PokemonType.Fairy) return;
            BattleFlow.Buffer(b, att, (final + 1) / 2, CardSource(s, seat));
            Used(s, fx, seat, Red, att);
        }

        /// <summary>
        /// kindler R 「When you attack with a Fire-type move, you may inflict the Burned status effect and deal 2 damage to the opposing Pokémon by using 1 Firewood from your bag」:
        /// 기술 효과 자리(피해 계산 뒤 — 이번 공격의 「이미 화상이면」은 보지 못한다, 원작 커뮤니티 확인)에서 묻는 프레임을 쌓는다. 부르는 쪽이 기술 효과보다 먼저 쌓아 기술 효과가 끝난 뒤에 돈다.
        /// </summary>
        internal static void Ignition(GameState s, Ctx ctx, BattleState b)
        {
            int k = b.ActiveSide;
            int seat = b.Sides[k].Owner;
            int def = b.Sides[1 - k].Active;
            if (def < 0 || !Seated(s, seat) || s.Seats[seat].Firewood <= 0 || !Has(s, ctx, seat, "kindler", Red)) return;
            if (BattleRules.AttackType(s, ctx, b.Move) != PokemonType.Fire) return;
            Push(s, seat, AbKindler, b.Sides[k].Active, def);
        }

        /// <summary>
        /// 공격 피해를 적용한 바로 뒤(BattleFlow ApplyDamage — 「After attacking」 · 「After you attack」): undivided_acolyte R — 상대 출전 포켓몬의 남은 체력이 1~2면 기절 ·
        /// scuba_diver R — 물 타입 기술이었으면 교체할 수 있다고 적어 둔다(교체 단계가 교체 창을 연다).
        /// </summary>
        internal static void AfterAttack(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            if (k < 0 || b.Move == -1) return;
            int seat = b.Sides[k].Owner;
            if (!Seated(s, seat)) return;
            int att = b.Sides[k].Active, def = b.Sides[1 - k].Active;
            if (def >= 0 && Has(s, ctx, seat, "undivided_acolyte", Red))
            {
                var dp = s.Pokemon[def];
                int hp = PokemonRules.Hp(ctx, dp);
                if (hp > 0 && hp <= JudgementHp)
                {
                    var src = CardSource(s, seat);
                    dp.Damage = PokemonRules.MaxHp(ctx, dp);
                    b.Sides[1 - k].LastHit = src;
                    b.Sides[1 - k].LastHitBy = att;
                    Used(s, fx, seat, Red, def);
                    if (fx.On) fx.Add(new HpChanged { PokemonId = def, Source = src, Before = hp, After = 0, Max = PokemonRules.MaxHp(ctx, dp) });
                }
            }
            if (att >= 0 && !PokemonRules.IsFainted(ctx, s.Pokemon[att]) && Has(s, ctx, seat, "scuba_diver", Red)
                && BattleRules.AttackType(s, ctx, b.Move) == PokemonType.Water) b.ScubaBy = att;
        }

        /// <summary>scuba_diver R의 교체 창이 여는 선택의 출처(그 카드). 답이 Pick이면 <see cref="Dived"/>.</summary>
        internal static SourceRef ScubaSource(GameState s, BattleState b, int pokemon) => CardSource(s, s.Pokemon[pokemon].Owner);

        /// <summary>scuba_diver R 「the user may switch out and then heal all status effects」: 교체해 나간 포켓몬의 상태이상을 모두 푼다.</summary>
        internal static void Dived(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            var pk = s.Pokemon[pokemon];
            var before = pk.Status;
            StatusRules.Clear(pk);
            Used(s, fx, seat, Red, pokemon);
            int hp = PokemonRules.Hp(ctx, pk);
            if (fx.On) fx.Add(new Healed { Seat = seat, Source = CardSource(s, seat), Pokemon = new[] { new HealEntry { Id = pokemon, HpBefore = hp, HpAfter = hp, StatusBefore = before } } });
        }

        // ── 출전 · 기절 ──

        /// <summary>
        /// worker R 「When your Steel-type Pokémon are switched in, increase the battle fatigue of the opposing Pokémon by 3 and prevent them from switching out or fleeing the battle」:
        /// 배틀 도중의 출전(교체 · 다음 포켓몬 — 처음 내보내는 것은 「sends out」이라 아니다 [가정])에 상대 출전 포켓몬의 피로 +3(최대 5)과 교체·도망 금지(그 포켓몬이 나갈 때까지).
        /// </summary>
        internal static void SwitchedIn(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int pokemon)
        {
            int seat = b.Sides[k].Owner;
            int foe = b.Sides[1 - k].Active;
            if (foe < 0 || !Seated(s, seat) || !Has(s, ctx, seat, "worker", Red) || !IsType(ctx, s.Pokemon[pokemon], PokemonType.Steel)) return;
            var fp = s.Pokemon[foe];
            if (PokemonRules.IsFainted(ctx, fp)) return;
            var src = CardSource(s, seat);
            int before = fp.Fatigue;
            fp.Fatigue = Math.Min(FatigueRules.Max, before + RoadWorksFatigue);
            ModRules.Add(s, foe, ModScope.UntilSwitch, EffectRole.Defender, EffectOp.NoEscape, AttackDieFace.None, 0, src);
            Used(s, fx, seat, Red, foe);
            if (fx.On) fx.Add(new FatigueChanged { Source = src, PokemonId = foe, Before = before, After = fp.Fatigue, Cause = FatigueCause.Effect });
        }

        /// <summary>team_rocket R 「When one of your Pokémon faints, deal 2 damage to the opposing Pokémon」: k 쪽 포켓몬이 기절했다 — 상대 출전 포켓몬에게 2(그 턴 버퍼).</summary>
        internal static void OwnFainted(GameState s, Ctx ctx, Emitter fx, BattleState b, int k)
        {
            int seat = b.Sides[k].Owner;
            int foe = b.Sides[1 - k].Active;
            if (foe < 0 || !Seated(s, seat) || !Has(s, ctx, seat, "team_rocket", Red) || PokemonRules.IsFainted(ctx, s.Pokemon[foe])) return;
            BattleFlow.Buffer(b, foe, -BlastDamage, CardSource(s, seat));
            Used(s, fx, seat, Red, foe);
        }

        /// <summary>
        /// team_rocket R 「If you are out of usable Pokémon, you may travel to any tile on the map and are automatically fully restored at the start of your next turn」:
        /// 배틀을 닫기 직전(BattleFlow.Conclude 처음)에 그 좌석의 쓸 포켓몬이 바닥났으면 갈 칸을 묻는 프레임을 쌓고 끝내기를 미룬다(BattleStep.Concluding — 프레임이 끝나면 다시 닫는다).
        /// 쌓았으면 true.
        /// </summary>
        internal static bool BlastOff(GameState s, Ctx ctx, BattleState b, BattleResult result, int winner)
        {
            if (b.BlastAsked || b.Kind == BattleKind.Traitor) return false;
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                int seat = b.Sides[k].Owner;
                if (!Seated(s, seat) || !Has(s, ctx, seat, "team_rocket", Red) || BattleRules.HasUsable(s, ctx, seat)) continue;
                b.BlastAsked = true;
                b.BlastSeat = seat;
                b.BlastTo = -1;
                b.EndResult = (int)result;
                b.EndWinner = winner;
                b.Step = BattleStep.Concluding;
                int any = -1;
                foreach (int p in s.Seats[seat].Party)
                    if (p >= 0 && any < 0) any = p;
                Push(s, seat, AbBlastOff, any, -1);
                return true;
            }
            return false;
        }

        // ── 비스트 부스트 ──

        /// <summary>expert R 「When Beast Boost triggers, your Legendary Pokémon heal 3 damage, reset their battle fatigue to 0 and increase the attack strength of their next attack by 2」를 가진 좌석인가.</summary>
        public static bool Protector(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "expert", Red);

        /// <summary>expert R의 회복 3(그 턴 버퍼)과 능력 알림 — 피로 0·다음 공격 +2는 LegendRules.Fainted가 비스트 부스트 대신 건다.</summary>
        internal static void Protected(GameState s, Emitter fx, BattleState b, int seat, int legend)
        {
            BattleFlow.Buffer(b, legend, ProtectorHeal, CardSource(s, seat));
            Used(s, fx, seat, Red, legend);
        }

        // ── lady R: 배틀 행동의 합법 답(턴을 쓰지 않는다) ──

        /// <summary>
        /// lady R 「Once per battle, heal all damage and status effects from a non-fainted Pokémon in your party」: k 쪽이 지금 고칠 수 있는 포켓몬(파티 순서 — 나와 있는 포켓몬 포함,
        /// 원작 커뮤니티 확인). 고칠 것이 없는 포켓몬은 뺀다. 이 배틀에서 이미 썼으면 빈 목록.
        /// </summary>
        public static void PotionTargets(GameState s, Ctx ctx, BattleState b, int k, List<int> into)
        {
            into.Clear();
            int seat = b.Sides[k].Owner;
            if (b.Sides[k].Potion || !Seated(s, seat) || !Has(s, ctx, seat, "lady", Red)) return;
            foreach (int p in s.Seats[seat].Party)
            {
                if (p < 0) continue;
                var pk = s.Pokemon[p];
                if (!PokemonRules.IsFainted(ctx, pk) && (pk.Damage > 0 || pk.Status != StatusEffect.None)) into.Add(p);
            }
        }

        /// <summary>
        /// lady R을 쓴다(BattleAction의 UseAbility 답): 그 포켓몬의 피해·상태이상을 모두 지운다. 배틀 턴은 그대로다(부르는 쪽이 배틀 행동을 다시 묻는다).
        /// 내 배틀 행동 고르기에서만 쓰고 턴을 쓰지 않는다 — 반응 창(굴리기 전·굴린 뒤)에서는 묻지 않는다(사용자 결정 84 T6 다 —
        /// 자체 검토가 원작 커뮤니티 확인로 넓혔던 것을 거둔다).
        /// </summary>
        internal static void UsePotion(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int pokemon)
        {
            int seat = b.Sides[k].Owner;
            var pk = s.Pokemon[pokemon];
            b.Sides[k].Potion = true;
            int before = PokemonRules.Hp(ctx, pk);
            var status = pk.Status;
            pk.Damage = 0;
            StatusRules.Clear(pk);
            Used(s, fx, seat, Red, pokemon);
            if (fx.On) fx.Add(new Healed { Seat = seat, Source = CardSource(s, seat), Pokemon = new[] { new HealEntry { Id = pokemon, HpBefore = before, HpAfter = PokemonRules.Hp(ctx, pk), StatusBefore = status } } });
        }

        // ── team_rocket B: 트레이너 배틀의 포획 ──

        /// <summary>
        /// team_rocket B 「During a battle with another trainer, you can attempt to capture their Pokémon but with -2 to your capture rolls」: k 쪽이 이 배틀에서 볼을 던질 수 있나 —
        /// 좌석끼리의 트레이너전(갤럭시단·종막 연전 아님)이고 상대의 파티가 둘 이상일 때(마지막 포켓몬은 못 잡는다 — 사용자 결정 84 T7 ① 가). 그 포획 굴림에는 디아루가 재굴림 창이 없다(BattleFlow.Capture — 같은 답의 메모).
        /// </summary>
        public static bool CanSteal(GameState s, Ctx ctx, BattleState b, int k)
        {
            int seat = b.Sides[k].Owner, victim = b.Sides[1 - k].Owner;
            return b.Kind == BattleKind.Trainer && Seated(s, seat) && victim >= 0 && victim < s.SeatCount && Has(s, ctx, seat, "team_rocket", Blue)
                && PokemonRules.PartyCount(s.Seats[victim]) > 1
                && (b.Sides[1 - k].Active < 0 || !BreachRules.Any(s.Pokemon[b.Sides[1 - k].Active])); // 균열 카드가 붙은 포켓몬은 그 파티를 떠날 수 없다(p.24 — rulings A52 ⑤)
        }

        /// <summary>포획 굴림의 합에 더할 값: 남의 포켓몬이면 −2(team_rocket B).</summary>
        public static int ThiefMod(GameState s, int target) => s.Pokemon[target].Owner >= 0 ? -ThiefPenalty : 0;

        /// <summary>
        /// team_rocket B로 남의 포켓몬을 잡았다(주인을 바꾸기 전에 부른다): 진 좌석의 파티에서 빼고(「The captured Pokémon keeps everything it currently has attached」 — 지닌물건·기술·토큰은 그대로 따라간다),
        /// 그 쪽은 기권할 수 있게 된다(원작 커뮤니티 확인).
        /// ruin_maniac R의 유물(「cannot be taken by other trainers」)은 지닌 채로 가져가지 못하고 주인의 가방에 남는다 [가정]. 둘째 지닌물건은 빼앗은 좌석의 가방으로(Unpocket — 결정 84 T7 ④),
        /// 빼앗은 포켓몬은 그 배틀에 나오지 못한다(BattleRules.Taken — 결정 84 T7 ③).
        /// </summary>
        internal static void Stolen(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int target)
        {
            int thief = b.Sides[k].Owner, victim = b.Sides[1 - k].Owner;
            var tp = s.Pokemon[target];
            var vs = s.Seats[victim];
            PokemonRules.RemoveFromParty(vs, target);
            b.Sides[1 - k].FaintedThisBattle = true;
            foreach (int item in new[] { tp.HeldItem, tp.HeldItem2 }) // 지닌물건 두 칸(backpacker R)
            {
                if (item < 0 || !RelicRules.IsRelic(ctx, item) || !RelicsGuarded(s, ctx, victim)) continue;
                PokemonRules.DropItem(tp, item);
                vs.Bag.Add(item);
                if (fx.On) fx.Add(new ItemHeld { Seat = victim, PokemonId = -1, ItemKey = item, FromPokemonId = target });
                FormRules.HeldChanged(s, ctx, fx, target);
            }
            Used(s, fx, thief, Blue, target);
        }

        // ── black_belt R · fairy_tale_girl R: 화상·졸림의 효과 바꾸기 ──

        /// <summary>black_belt R 「Replace the effect of Burned when applied to your Fighting-type Pokémon: Increase the total damage you deal by 2」가 걸리는 포켓몬인가(화상인 내 격투 타입).</summary>
        public static bool Spirited(GameState s, Ctx ctx, PokemonState p) =>
            Seated(s, p.Owner) && StatusRules.Has(p, StatusEffect.Burned) && Has(s, ctx, p.Owner, "black_belt", Red) && IsType(ctx, p, PokemonType.Fighting);

        /// <summary>fairy_tale_girl R 「Replace the effect of Drowsy when applied to your Fairy-type Pokémon: At the end of your turns during battle, heal 1 damage」가 걸리는 포켓몬인가(졸림인 내 페어리 타입).</summary>
        public static bool Dreaming(GameState s, Ctx ctx, PokemonState p) =>
            Seated(s, p.Owner) && StatusRules.Has(p, StatusEffect.Drowsy) && Has(s, ctx, p.Owner, "fairy_tale_girl", Red) && IsType(ctx, p, PokemonType.Fairy);

        /// <summary>그 포켓몬 주인의 카드 출처(Spirited·Dreaming의 피해 내역·회복 출처).</summary>
        public static SourceRef SourceOf(GameState s, PokemonState p) => CardSource(s, p.Owner);

        /// <summary>배틀 턴 끝(턴 끝 피해 창): 졸림인 내 페어리 타입은 1 회복(fairy_tale_girl R — 그 턴 버퍼, 독과 함께).</summary>
        internal static void TurnEnd(GameState s, Ctx ctx, BattleState b, int pokemon)
        {
            if (pokemon < 0) return;
            var p = s.Pokemon[pokemon];
            if (!PokemonRules.IsFainted(ctx, p) && Dreaming(s, ctx, p)) BattleFlow.Buffer(b, pokemon, 1, CardSource(s, p.Owner));
        }

        /// <summary>
        /// 정비 명령 Afflict(black_belt R 화상 · fairy_tale_girl R 졸림 — 「During your turn whilst outside of battle, you may inflict the … status on your Pokémon」)의 검사:
        /// 내 턴 · 배틀 밖 · 내 파티의 쓰러지지 않은 포켓몬 · 그 상태가 걸리는 포켓몬(면역 타입·이미 다른 상태면 거절 — 원작 커뮤니티 확인).
        /// </summary>
        internal static RejectReason CheckAfflict(GameState s, Ctx ctx, int seat, Maintenance m)
        {
            var status = (StatusEffect)m.Value;
            if (PokemonRules.PartyIndexOf(s.Seats[seat], m.Pokemon) < 0) return RejectReason.Illegal;
            if (CardOf(s, seat) < 0) return RejectReason.Illegal;
            bool has = (status == StatusEffect.Burned && Has(s, ctx, seat, "black_belt", Red)) || (status == StatusEffect.Drowsy && Has(s, ctx, seat, "fairy_tale_girl", Red));
            if (!has) return RejectReason.Illegal;
            var p = s.Pokemon[m.Pokemon];
            if (StatusRules.Has(p, status)) return RejectReason.NoChange;
            if (s.Battle != null || s.Phase != Phase.Turn || s.ActiveSeat != seat) return RejectReason.Timing;
            if (PokemonRules.IsFainted(ctx, p)) return RejectReason.Illegal;
            return StatusRules.Check(ctx, p, status) == StatusResult.Applied ? RejectReason.None : RejectReason.Illegal;
        }

        internal static void Afflict(GameState s, Ctx ctx, Emitter fx, int seat, Maintenance m)
        {
            var status = (StatusEffect)m.Value;
            var r = StatusRules.Inflict(ctx, s.Pokemon[m.Pokemon], status);
            Used(s, fx, seat, Red, m.Pokemon);
            if (fx.On) fx.Add(new StatusApplied { Source = CardSource(s, seat), PokemonId = m.Pokemon, Status = status, Result = r });
        }

        // ── 처리기(묶음 3의 Ability가 모르는 번호를 넘긴다) ──

        static EffectStepResult AbilityAfter(GameState s, Ctx ctx, Emitter fx, EffectFrame f, int seat, int responder, int code)
        {
            var b = s.Battle;
            switch (code & 15)
            {
                case AbKindler:
                {
                    if (f.Target < 0 || s.Seats[seat].Firewood <= 0) return EffectStepResult.Done;
                    var r = Option(s, ctx, fx, f, responder, SlotOf(Red), out bool yes);
                    if (r != EffectStepResult.Continue) return r;
                    if (!yes) return EffectStepResult.Done;
                    s.Seats[seat].Firewood--;
                    Used(s, fx, seat, Red, f.Target);
                    EffectRules.InflictOne(s, ctx, fx, f.Target, StatusEffect.Burned, f.Source);
                    // 「deal 2 damage」도 공격에서 받는 피해라 받는 쪽 카드(delinquent R 감소 · bird_keeper R 문턱 · 상한)를 지난다 — 기술 효과의 고정 피해와 같은 길(사용자 결정 135, rulings A35 · A32)
                    int n = SideDamage(s, ctx, f.Target, IgnitionDamage);
                    if (n > 0) BattleFlow.Buffer(b, f.Target, -n, f.Source);
                    return EffectStepResult.Done;
                }
                case AbBlastOff:
                {
                    // 「you may travel to any tile on the map」: 안 감(= 고향 마을로, S7_9)이 첫 답 · 지금 칸도 고를 수 있다(원작 커뮤니티 확인)
                    var options = new List<int>(ctx.Board.Count);
                    for (int c = 0; c < ctx.Board.Count; c++)
                        if (!ctx.Board[c].DistortionWorld) options.Add(c);
                    var r = EffectRules.Pick(s, ctx, fx, f, responder, options, true, f.Source, -1, out int cell);
                    if (r != EffectStepResult.Continue) return r;
                    if (cell < 0 || cell >= ctx.Board.Count || ctx.Board[cell].DistortionWorld) return EffectStepResult.Done;
                    b.BlastTo = cell;
                    Used(s, fx, seat, Red);
                    return EffectStepResult.Done;
                }
                default:
                    return AbilityItems(s, ctx, fx, f, seat, responder, code); // 전용 아이템 능력(대타출동 — TrainerRules.Items.cs)
            }
        }
    }
}
