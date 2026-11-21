using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// op 어휘(01 §7-1, M3-f 처음 어휘 + M5 효과 기반의 Hold·Ask — 확정한 어휘 전체와 아직 만들지 않은 이름은 01 §7-6). 앞 넷은 상시 효과다 —
    /// 이벤트로 처리하지 않고 계산할 때 출처를 훑어 더한다(01 §7-1). 나머지는 효과 처리기(EffectRules)가 Stack 프레임으로 하나씩 실행한다.
    /// RemoveFaces·ReplaceFace는 절의 op로는 상태이상 기본 효과·지닌물건(코드 상수)만 쓴다 — 데이터는 RemoveFaces를 Hold의 걸 것으로만 쓴다(M5 지속 보정 갈래). 새 op는 맨 뒤에 더한다(IsStatic = 앞 넷).
    /// </summary>
    public enum EffectOp
    {
        // 상시(훑기)
        /// <summary>BeforeRoll: 기술 공격력이 Value가 된다(「attack strength is increased to n」). 다른 보정은 그 뒤에 더한다(G14).</summary>
        StrengthTo,
        /// <summary>BeforeDamage ②: Face 면 하나마다 Value(「Critical hits deal +1 damage」).</summary>
        FaceDamage,
        /// <summary>BeforeDamage ②: Value(「deal 2 extra damage」).</summary>
        ExtraDamage,
        /// <summary>BeforeDamage ④: 2배(「this move deals double damage」).</summary>
        Double,
        // 실행(프레임)
        /// <summary>Who에게 Status를 건다(면역·이미 걸림은 Result, S7_6).</summary>
        Inflict,
        /// <summary>Who의 피해를 Value만큼 회복(그 턴 피해 버퍼, 독이면 0 — G14).</summary>
        Heal,
        /// <summary>Who의 상태이상을 푼다. Status = None이면 아무 상태이상, 아니면 그 상태만.</summary>
        CureStatus,
        /// <summary>Who의 배틀 피로 +Value(5까지, G14).</summary>
        Fatigue,
        /// <summary>효과를 가진 포켓몬이 Value 피해(Face가 있으면 그 면 하나마다). 반동·혼란·독. 그 턴 피해 버퍼로.</summary>
        SelfDamage,
        /// <summary>지금 굴림에서 Face 면을 모두(Value 0), 또는 왼쪽부터 Value개 지운다(마비 G14 · 지닌물건). 지운 자리는 None.</summary>
        RemoveFaces,
        /// <summary>지금 굴림에서 Face 면 Value개를 왼쪽부터 To로 바꾼다(졸림, G14).</summary>
        ReplaceFace,
        /// <summary>Who에게 지속 보정을 건다(M5, rulings G16): Scope(언제까지) · Role(교체까지·배틀 끝까지일 때 공격할 때/받을 때) · Held(그때 적용할 상시 op) · Face · Value.</summary>
        Hold,
        /// <summary>효과 주인에게 「쓸까」를 묻는다(선택을 여는 op, 01 §7-2 Wait — Reaction/EffectOption). 안 쓰면 그 절의 나머지 op를 건너뛴다. 가상 쪽은 묻지 않고 쓴다(G16).</summary>
        Ask,
        /// <summary>지속 보정 전용(데이터 op 아님): 그 포켓몬은 나갈 때까지 Value 기술만 쓴다(결단의 머리띠, HeldItems). PokemonState.Mods에 UntilSwitch로 걸린다.</summary>
        MoveLock,
        /// <summary>지속 보정 전용: 「첫 공격에만」 지닌물건(폭발수정·별 방패)을 이 배틀에서 썼다(Value = 아이템, BattleEnd). Fresh인 동안 = 그 공격이 진행 중(별 방패의 상태이상 막기).</summary>
        ItemUsed,
        /// <summary>지속 보정 전용: 배틀 턴 Value 동안 교체하지 못한다(폭발수정에 맞은 공격자, UntilSwitch).</summary>
        SwitchLock,
        // ── M5 「기술 효과 — 지속 보정 갈래」(rulings G19). ExtendMods 말고는 Hold로 거는 것(hold 칸)이다 ──
        /// <summary>Hold: 굴림에 Face 면 Value개를 더한다(「add 1 regular hit to the roll」). 굴린 바로 뒤 — 면 바꾸기(마비·졸림·지우기)보다 먼저다(원작 커뮤니티 확인).</summary>
        AddFaces,
        /// <summary>Hold: 그 포켓몬 쪽의 배틀 턴이 끝날 때 Value 회복(「Heal 2 damage at the end of your turns」 — 턴 끝 피해 창, 독과 동시).</summary>
        TurnEndHeal,
        /// <summary>Hold: 상태이상에 걸리지 않는다(「immune to all status effects」). 이미 걸린 상태는 그대로다.</summary>
        StatusImmune,
        /// <summary>Hold: 교체·도망하지 못한다(「may no longer switch out or flee the battle」 — 빙결의 교체·도망 금지와 같은 자리).</summary>
        NoEscape,
        /// <summary>Hold: 폼을 바꾸지 못한다(imprison). 읽는 곳 <see cref="ModRules.NoFormChange"/> — 폼 바꾸기(「전설·알파·폼」 묶음)가 본다.</summary>
        NoFormChange,
        /// <summary>Hold(fairy_lock): 이 포켓몬이 출전해 있는 동안 어느 포켓몬도 기술 효과로 교체하지 못한다. 읽는 곳 <see cref="ModRules.MoveSwitchBlocked"/> — 교체 기술(「나머지 op 갈래」)이 본다.</summary>
        NoMoveSwitch,
        /// <summary>Hold: 받는 공격의 최종 피해가 Value를 넘지 않는다(「take at most 1 damage」, 01 §6-4 ⑦ 상한).</summary>
        DamageCap,
        /// <summary>Hold(sweet_scent): FaceDamage와 같되 그 포켓몬을 노린 포획 굴림에도 쓰인다(「On the next attack or capture roll against the target」 — 포획 합에 그 면 하나마다 Value).</summary>
        FaceDamageOrCapture,
        /// <summary>실행 op(eternal_glade): Who(User)에게 걸린 「교체할 때까지」 기술 효과를 「배틀이 끝날 때까지」로 바꾼다(<see cref="ModRules.Extend"/>).</summary>
        ExtendMods,
        // ── M5 「기술 효과 — 나머지 op 갈래」(rulings G20). 상시 표지 넷(FatigueDamage · IgnorePrism · FromParty)은 IsStatic이 따로 본다 ──
        /// <summary>실행: Who의 배틀 피로를 Value로 맞춘다(「Increase the battle fatigue of the target to 5」 · calm_mind 「reset your battle fatigue to 0」).</summary>
        FatigueTo,
        /// <summary>상시(BeforeDamage ②): 효과를 가진 포켓몬의 지금 배틀 피로만큼 추가 피해(calm_mind 「Deal extra damage equal to your current battle fatigue」 — 굴림으로 오른 피로 포함).</summary>
        FatigueDamage,
        /// <summary>실행: Who에게 Status와 Status2를 동시에 건다(S7_6 p.9 「two status effects can be inflicted through special moves that inflict them simultaneously」, StatusRules.InflictBoth).</summary>
        InflictBoth,
        /// <summary>실행: 상태이상 주사위를 굴려 나온 상태를 Who에게 건다(「Roll a status dice and inflict the result on the target」 — 여섯 면 = 여섯 상태).</summary>
        InflictRolled,
        /// <summary>실행: Who에게 고정 피해 Value(earthquake·prismatic_laser — 공격이 아니라 상성·2배·감소가 없다). 그 턴 피해 버퍼로.</summary>
        Damage,
        /// <summary>실행: 이 공격 뒤 Who의 체력이 Value 이하로 남으면 기절시킨다(「The target faints if they have 2 or less health remaining after this attack」).</summary>
        FaintBelow,
        /// <summary>실행: Who(Target) 주인의 가방에서 규토리 1개를 없앤다 — 색은 그 주인이 고른다(bug_bite 「The target decides which one」). 가방이 없는 쪽(야생·갤럭시단)은 아무 일도 없다.</summary>
        ConsumeApricorn,
        /// <summary>실행: 효과를 가진 포켓몬이 이 공격 동안 받은 피해(피해 버퍼의 그 포켓몬 몫)를 Who에게도 준다(outrage류 「Deal any damage taken by the user during this attack to the target as well」).</summary>
        ReflectTaken,
        /// <summary>실행: Who가 이 공격으로 쓰러지면 남는 피해를 그 쪽에 다음에 나오는 포켓몬에게(renegade_fury·overcut — BattleSide.Carry).</summary>
        CarryDamage,
        /// <summary>실행: 효과를 가진 포켓몬이 이번 배틀 턴 끝(턴 끝 효과 뒤)에 교체할 수 있다(「The user may switch out (if possible)」 — BattleState.SwitchAfter, 순서는 그대로).</summary>
        SwitchOut,
        /// <summary>상시 표지(multi_attack 「Ignores the effects of Prism Armour」): 프리즘 아머(울트라 버스트, M6)가 읽는다 — <see cref="EffectRules.IgnoresPrism"/>.</summary>
        IgnorePrism,
        /// <summary>상시 표지(lunge·acrobatics 「This move can be used from your party: Switch the user in and then use this attack」): 배틀 행동이 벤치 포켓몬의 이 기술을 늘어놓는다.</summary>
        FromParty,
        /// <summary>Hold(shell_trap): 공격받을 때 그 굴림에 Face가 하나라도 있으면 공격자에게 Value 피해(「the attacker takes 5 damage if any critical hits are rolled」).</summary>
        Retaliate,
        /// <summary>실행(M5 「전설·알파·폼」, rulings A26): Who(User)를 그 종의 상태 Value 폼으로 바꾼다(「Change to X Form if possible」 — 그 폼이 없거나 이미 그 폼·폼 바꾸기 금지면 아무 일도 없다, FormRules).</summary>
        ChangeForm,
        // ── M5 「기술 효과 — 핸들러 갈래」(rulings G21) ──
        /// <summary>상시 표지(sacred_sword 「The dice rolled by this attack cannot be replaced or removed by any effect」): 굴린 뒤 면 바꾸기·지우기·디아루가 재굴림·매혹적인 스튜를 막는다 — <see cref="EffectRules.DiceLocked"/>.</summary>
        LockDice,
        /// <summary>지속 보정 전용(데이터 op 아님 — 처리기 encore가 NextAttacked로 건다): 그 공격으로 쓰러지면 쓰러지기 전에 마지막 기술을 쓸 수 있다. Value = encore 절의 효과 Id(이어 묻는 프레임).</summary>
        FinalMove,
        /// <summary>
        /// Hold(barrier 「When the user is next attacked, all critical hits damage the attacker instead of the user」 — rulings G20 ⑷, 결정 76 B3 ① 가 · ② 가): 공격받을 때 그 굴림의 크리티컬 몫
        /// (크리티컬 눈 + 크리티컬에 붙는 보정)을 받는 포켓몬 대신 공격한 포켓몬이 받는다 — 상성·2배·화상 감소는 붙지 않고 공격이 아닌 효과 피해다(<see cref="ModRules.ReflectsCrits"/>, BattleFlow.Damage). 면·값 칸 없음.
        /// </summary>
        ReflectCrits,
    }

    /// <summary>
    /// 지속 보정이 남는 범위(M5, rulings G16). NextAttacked = 그 포켓몬이 다음에 공격받을 때 한 번 · NextAttack = 다음에 공격할 때 한 번 ·
    /// UntilSwitch = 나갈 때까지(교체·기절·배틀 끝) · BattleEnd = 배틀이 끝날 때까지(교체해도 남는다). None = 지속 보정이 아니다.
    /// </summary>
    public enum ModScope { None, NextAttacked, NextAttack, UntilSwitch, BattleEnd }

    /// <summary>
    /// 「턴당·조우당 1번」(01 §7-1 Limit, 기록은 GameState.Used의 UsageKind.Effect). Turn = 맵 턴당 · BattleTurn = 배틀 턴당 · Battle = 배틀(조우)당 ·
    /// UntilSwitch = 그 포켓몬이 나갈 때까지 한 번(「Effect may be used once until the user switches out」).
    /// </summary>
    public enum EffectLimit { None, Turn, BattleTurn, Battle, UntilSwitch }

    /// <summary>
    /// 대상이 없을 때(rulings G3). Skip = 그 절 전체가 효과 없이 넘어간다(한도도 쓰지 않는다 — 기본) · SkipOp = 대상 없는 op만 건너뛰고 나머지는 한다
    /// (「…and another Pokémon in the target party」처럼 덧붙은 대상).
    /// </summary>
    public enum EffectNoTarget { Skip, SkipOp }

    /// <summary>
    /// op 대상. Target = 공격받는 포켓몬, User = 효과를 가진 포켓몬, Party = 효과 주인의 파티 전부(User 포함, 가상 쪽은 명단).
    /// M5 나머지 op 갈래(rulings G20): TargetOther = 대상 파티의 다른 포켓몬 하나(기절하지 않은 것 — 둘 이상이면 효과 주인이 고른다, 「another Pokémon in the target party」),
    /// TargetOthers = 대상 파티의 다른 포켓몬 모두(「all Pokémon in the target party that is not the target」).
    /// </summary>
    public enum EffectWho { None, Target, User, Party, TargetOther, TargetOthers }

    /// <summary>효과를 가진 포켓몬의 자리(창에서 누구 것을 훑나). Attacker = 배틀 턴을 가진 쪽 출전 포켓몬, Defender = 다른 쪽.</summary>
    public enum EffectRole { Attacker, Defender }

    /// <summary>
    /// 효과 조건(01 §7-1 Condition). 적힌 것이 모두 맞아야 한다(그리고). 기본값은 「안 봄」. 대상 상태는 이번 공격의 효과가 처리되기 전 상태다(G14).
    /// UserParalysisPending은 상태이상 기본 효과(코드 상수) 전용이다 — 데이터는 쓰지 않는다.
    /// </summary>
    public sealed class EffectCondition
    {
        /// <summary>마지막 굴림의 상태이상 심볼이 n개 이상(「If any status symbols are rolled」 = 1). 0 = 안 봄.</summary>
        public readonly int MinSymbols;
        /// <summary>대상이 상태이상이 있다(「If the target has any status effects」).</summary>
        public readonly bool TargetAnyStatus;
        /// <summary>대상이 이미 그 상태다(「If the target is already X」). None = 안 봄.</summary>
        public readonly StatusEffect TargetStatus;
        /// <summary>대상이 그 타입이다(「to Flying-type Pokémon」). Typeless = 안 봄(포켓몬 타입에 타입없음은 없다).</summary>
        public readonly PokemonType TargetType;
        /// <summary>대상이 소지 아이템을 가졌다.</summary>
        public readonly bool TargetHeldItem;
        /// <summary>효과를 가진 포켓몬이 마비 대기 중이다(PokemonState.ParalysisPending).</summary>
        public readonly bool UserParalysisPending;
        /// <summary>효과를 가진 포켓몬이 직전 자기 턴과 같은 기술을 쓴다(혼란, 「If this move was used last turn」).</summary>
        public readonly bool SameMoveAsLast;
        /// <summary>효과를 가진 포켓몬이 직전 자기 턴에 이 기술을 쓰지 않았다(disguise 「If this move was not used last turn」 — 데이터는 sameMoveAsLast: false).</summary>
        public readonly bool NotSameMoveAsLast;
        /// <summary>효과 주인의 파티에 이 포켓몬 말고 기절하지 않은 그 타입 포켓몬이 있다(attack_order 「another non-fainted Bug-type Pokémon in your party」). Typeless = 안 봄.</summary>
        public readonly PokemonType PartyType;
        /// <summary>효과를 가진 포켓몬이 이 기술 말고 그 타입의 기술을 안다(wild_charge 「If the user knows another Electric-type move」). Typeless = 안 봄.</summary>
        public readonly PokemonType KnowsType;

        public EffectCondition(int minSymbols = 0, bool targetAnyStatus = false, StatusEffect targetStatus = StatusEffect.None,
            PokemonType targetType = PokemonType.Typeless, bool targetHeldItem = false, bool userParalysisPending = false, bool sameMoveAsLast = false,
            bool notSameMoveAsLast = false, PokemonType partyType = PokemonType.Typeless, PokemonType knowsType = PokemonType.Typeless)
        {
            PartyType = partyType;
            KnowsType = knowsType;
            if (sameMoveAsLast && notSameMoveAsLast) throw new ArgumentException("「직전 턴과 같은 기술」과 「직전 턴에 쓰지 않았다」를 함께 쓰지 않는다");
            NotSameMoveAsLast = notSameMoveAsLast;
            if (minSymbols < 0) throw new ArgumentOutOfRangeException(nameof(minSymbols), minSymbols, "심볼 수는 0 이상");
            if (targetAnyStatus && targetStatus != StatusEffect.None) throw new ArgumentException("「상태이상이 있다」와 「그 상태다」를 함께 쓰지 않는다");
            MinSymbols = minSymbols;
            TargetAnyStatus = targetAnyStatus;
            TargetStatus = targetStatus;
            TargetType = targetType;
            TargetHeldItem = targetHeldItem;
            UserParalysisPending = userParalysisPending;
            SameMoveAsLast = sameMoveAsLast;
        }

        /// <summary>아무것도 보지 않는다(데이터는 이런 조건 대신 null을 쓴다).</summary>
        public bool IsEmpty => MinSymbols == 0 && !TargetAnyStatus && TargetStatus == StatusEffect.None && TargetType == PokemonType.Typeless
            && !TargetHeldItem && !UserParalysisPending && !SameMoveAsLast && !NotSameMoveAsLast && PartyType == PokemonType.Typeless && KnowsType == PokemonType.Typeless;
    }

    /// <summary>op 하나. 쓰지 않는 칸은 Who = None, Status = None, Face·To = None, Value = 0.</summary>
    public sealed class EffectOpDef
    {
        public readonly EffectOp Op;
        public readonly EffectWho Who;
        public readonly StatusEffect Status;
        public readonly AttackDieFace Face;
        public readonly AttackDieFace To;
        public readonly int Value;
        /// <summary>Hold: 지속 보정의 범위. 그 밖은 None.</summary>
        public readonly ModScope Scope;
        /// <summary>Hold: 보정이 적용되는 때 — Attacker = 그 포켓몬이 공격할 때, Defender = 공격받을 때. NextAttack·NextAttacked는 범위가 정한다.</summary>
        public readonly EffectRole Role;
        /// <summary>Hold: 그때 적용할 것(<see cref="ModRules.CanHold"/> — 굴림 보정·면 더하기·지우기·턴 끝 회복·금지 등). 그 밖은 쓰지 않는다.</summary>
        public readonly EffectOp Held;
        /// <summary>InflictBoth: 함께 거는 둘째 상태. 그 밖은 None.</summary>
        public readonly StatusEffect Status2;

        public EffectOpDef(EffectOp op, EffectWho who = EffectWho.None, StatusEffect status = StatusEffect.None,
            AttackDieFace face = AttackDieFace.None, AttackDieFace to = AttackDieFace.None, int value = 0,
            ModScope scope = ModScope.None, EffectRole role = EffectRole.Attacker, EffectOp held = default, StatusEffect status2 = StatusEffect.None)
        {
            if ((op == EffectOp.InflictBoth) != (status2 != StatusEffect.None)) throw new ArgumentException($"{op}: 둘째 상태(Status2)는 InflictBoth만 쓴다", nameof(status2));
            if (op == EffectOp.InflictBoth && (status == StatusEffect.None || status == status2)) throw new ArgumentException("InflictBoth: 서로 다른 상태 둘", nameof(status));
            if ((who == EffectWho.TargetOther || who == EffectWho.TargetOthers) && op != EffectOp.Inflict && op != EffectOp.Damage)
                throw new ArgumentException($"{op}: 대상 파티의 다른 포켓몬(TargetOther·TargetOthers)은 Inflict·Damage만", nameof(who));
            Status2 = status2;
            if ((op == EffectOp.Hold) != (scope != ModScope.None)) throw new ArgumentException($"{op}: 범위(Scope)는 Hold만 쓴다", nameof(scope));
            if (op == EffectOp.Hold)
            {
                if (!ModRules.CanHold(held)) throw new ArgumentException($"Hold: {held}는 지속 보정으로 걸 수 없다", nameof(held));
                if (who != EffectWho.Target && who != EffectWho.User) throw new ArgumentException("Hold는 Target·User에게만", nameof(who));
                if (scope == ModScope.NextAttacked) role = EffectRole.Defender;
                else if (scope == ModScope.NextAttack) role = EffectRole.Attacker;
                bool once = scope == ModScope.NextAttacked || scope == ModScope.NextAttack;
                if (!ModRules.OnRoll(held))
                {
                    if (once) throw new ArgumentException($"Hold {held}: 굴림과 얽히지 않은 보정은 교체까지·배틀 끝까지만", nameof(scope));
                    role = EffectRole.Attacker;
                }
                if ((held == EffectOp.Inflict || held == EffectOp.DamageCap || held == EffectOp.Retaliate || held == EffectOp.ReflectCrits) && role != EffectRole.Defender) throw new ArgumentException($"Hold {held}: 공격받을 때(Defender)만", nameof(role));
                if ((held == EffectOp.Inflict) != (status != StatusEffect.None)) throw new ArgumentException($"Hold {held}: 상태(Status)는 Inflict만 쓴다", nameof(status));
                if (ModRules.HoldUsesFace(held) != (face != AttackDieFace.None)) throw new ArgumentException($"Hold {held}: 면(Face)이 맞지 않다", nameof(face));
                if (ModRules.HoldUsesValue(held) ? value == 0 && held != EffectOp.RemoveFaces : value != 0) throw new ArgumentException($"Hold {held}: 값(Value)이 맞지 않다(RemoveFaces 0 = 모두)", nameof(value));
                if (value < 0 && held != EffectOp.FaceDamage && held != EffectOp.ExtraDamage && held != EffectOp.FaceDamageOrCapture) throw new ArgumentException($"Hold {held}: 값은 1 이상", nameof(value));
            }
            if (op == EffectOp.ExtendMods && who != EffectWho.User) throw new ArgumentException("ExtendMods는 User에게만", nameof(who));
            Scope = scope;
            Role = role;
            Held = held;
            Op = op;
            Who = who;
            Status = status;
            Face = face;
            To = to;
            Value = value;
        }

        /// <summary>상시 효과(계산할 때 훑는 op): 앞 넷 + 나머지 op 갈래의 피로 피해·표지 둘.</summary>
        public bool IsStatic => IsStaticOp(Op);

        public static bool IsStaticOp(EffectOp op) => op <= EffectOp.Double || op == EffectOp.FatigueDamage || op == EffectOp.IgnorePrism || op == EffectOp.FromParty || op == EffectOp.LockDice;

        /// <summary>대상 포켓몬을 쓰는 실행 op(대상 없음 G3을 따질 op).</summary>
        public bool NeedsWho => Who != EffectWho.None;
    }

    /// <summary>
    /// 효과 하나(01 §7-1 EffectDef { Id, Window, Condition, Ops, Limit, OnNoTarget }). Id = EffectDb 색인. 기술은 문구 하나가 창이 다른 절 여럿이 된다
    /// (예: 독침붕류 = BeforeRoll 공격력 + BeforeDamage 걸기). Limit·OnNoTarget·Handler는 M5 효과 기반에서 더했다(rulings G16): 실행 절(프레임)만 쓴다.
    /// Handler가 있으면 op 대신 그 이름의 C# 처리기(<see cref="EffectHandler"/>, EffectDb.Handlers)가 프레임을 돈다 — Ops는 비운다.
    /// </summary>
    public sealed class EffectDef
    {
        public readonly int Id;
        /// <summary>출처: 기술 = {Move, 기술 색인}, 상태이상 기본 효과 = {Status, 상태}.</summary>
        public readonly SourceRef Source;
        public readonly Window Window;
        /// <summary>누구 것을 훑나(상태이상 기본 효과). 기술 절은 늘 Attacker(기술을 쓴 쪽).</summary>
        public readonly EffectRole Role;
        /// <summary>조건. null = 늘.</summary>
        public readonly EffectCondition When;
        public readonly IReadOnlyList<EffectOpDef> Ops;
        /// <summary>「턴당·조우당 1번」. None = 한도 없음.</summary>
        public readonly EffectLimit Limit;
        /// <summary>대상이 없을 때(G3). 기본 Skip.</summary>
        public readonly EffectNoTarget OnNoTarget;
        /// <summary>op로 안 되는 효과의 C# 처리기 이름(01 §7-1). 없으면 null.</summary>
        public readonly string Handler;

        public EffectDef(int id, SourceRef source, Window window, EffectRole role, EffectCondition when, IReadOnlyList<EffectOpDef> ops,
            EffectLimit limit = EffectLimit.None, EffectNoTarget onNoTarget = EffectNoTarget.Skip, string handler = null)
        {
            if (handler != null)
            {
                if (handler.Length == 0) throw new ArgumentException($"효과 {id}: 빈 처리기 이름", nameof(handler));
                if (ops != null && ops.Count > 0) throw new ArgumentException($"효과 {id}: 처리기({handler})가 있으면 op를 쓰지 않는다", nameof(ops));
                if (onNoTarget != EffectNoTarget.Skip) throw new ArgumentException($"효과 {id}: 처리기 절은 대상 없음 칸을 쓰지 않는다(처리기가 본다)", nameof(onNoTarget));
                ops = Array.Empty<EffectOpDef>();
            }
            else if (ops == null || ops.Count == 0) throw new ArgumentException($"효과 {id}: op가 없다", nameof(ops));
            Limit = limit;
            OnNoTarget = onNoTarget;
            Handler = handler;
            bool st = ops.Count > 0 && ops[0].IsStatic;
            if (st && (limit != EffectLimit.None || onNoTarget != EffectNoTarget.Skip)) throw new ArgumentException($"효과 {id}: 상시 절은 한도·대상 없음 칸을 쓰지 않는다");
            foreach (var o in ops)
            {
                if (o == null) throw new ArgumentException($"효과 {id}: op가 null이다", nameof(ops));
                if (o.IsStatic != st) throw new ArgumentException($"효과 {id}: 상시 op와 실행 op를 한 절에 섞지 않는다", nameof(ops));
            }
            if (when != null && when.IsEmpty) throw new ArgumentException($"효과 {id}: 빈 조건은 null로 쓴다", nameof(when));
            Id = id;
            Source = source;
            Window = window;
            Role = role;
            When = when;
            var a = new EffectOpDef[ops.Count];
            for (int i = 0; i < a.Length; i++) a[i] = ops[i];
            Ops = a;
        }

        /// <summary>상시 효과 절(훑기). 아니면 실행 절(프레임 — 처리기 절 포함). 셈 처리기 절(<see cref="EffectHandlers.Static"/> — 공격력·피해를 셈하는 처리기, rulings G21)도 상시다.</summary>
        public bool IsStatic => Ops.Count > 0 ? Ops[0].IsStatic : Handler != null && EffectHandlers.Static.ContainsKey(Handler);

        /// <summary>그 상태이상의 기본 효과다(Source.Kind = Status).</summary>
        public bool IsStatus => Source.Kind == SourceKind.Status;
    }

    /// <summary>
    /// 효과 정의 표(01 §7-1, 03 §1-3 effects.json). 앞쪽은 상태이상 기본 효과(코드 상수, S7_6·G14), 그 뒤가 데이터 효과(effects.json 행 순서)다.
    /// 데이터 효과의 Id = 기본 효과 수 + 행 번호. 기술마다 절 Id 목록을 든다. 효과가 없는 기술 = 피해만 주는 미구현 기술(G14, plan M3).
    /// </summary>
    public sealed class EffectDb
    {
        /// <summary>코드 상수 효과 수 = 상태이상 기본 효과 5 + 지닌물건의 굴린 뒤 면 바꾸기 2 + 지속 보정의 프레임 3 + 트레이너 카드 능력의 물음 프레임 1(데이터 효과 Id는 여기서 시작한다).</summary>
        public const int BuiltInCount = 11;
        /// <summary>트레이너 카드 능력이 여는 물음의 프레임(M6, rulings A34): 처리기 <see cref="TrainerRules.AbilityHandler"/>, 어느 능력인지는 프레임의 Locals[0], 출처·주인은 프레임이 싣는다. 창에서 훑지 않는다 — TrainerRules가 제자리에서 쌓는다.</summary>
        public const int TrainerAbility = 10;
        /// <summary>행운의 알 · 진화의 휘석의 굴린 뒤 면 바꾸기(S10). 출처의 아이템 색인은 프레임이 싣는다(EffectRules.Trigger — 정의의 출처는 {Item, −1}).</summary>
        public const int LuckyEggEffect = 5, EvioliteEffect = 6;
        /// <summary>
        /// 지속 보정이 쌓는 프레임(M5 지속 보정 갈래, rulings G19). 정의의 op는 자리만 채운다 — 하는 일은 그 포켓몬(프레임의 User)에 걸린 보정에서 읽는다(EffectRules.StepMods):
        /// ModFacesAttacker·ModFacesDefender = 굴린 뒤 면 지우기(AfterRoll, 공격하는 쪽·받는 쪽 — 결정 72 순서 선택에 든다) · ModAttacked = 공격받을 때 공격자에게 상태이상(BeforeDamage, 받는 쪽).
        /// 프레임의 출처 = 그 보정을 건 기술(여럿이면 먼저 건 것).
        /// </summary>
        public const int ModFacesAttacker = 7, ModFacesDefender = 8, ModAttacked = 9;

        /// <summary>지속 보정이 쌓는 프레임의 효과다.</summary>
        public static bool IsModEffect(int id) => id >= ModFacesAttacker && id <= ModAttacked;

        public readonly IReadOnlyList<EffectDef> Effects;
        /// <summary>처리기 등록부(이름 → C#). 기본은 <see cref="EffectHandlers.BuiltIn"/>. 정적 데이터의 일부라 상태에 들지 않는다 — 프레임은 효과 Id만 든다.</summary>
        public readonly IReadOnlyDictionary<string, EffectHandler> Handlers;
        readonly int[][] byMove;

        /// <summary>
        /// dataEffects = effects.json 행(Id = BuiltInCount + 행 번호, 기술 색인 순). moveCount = 기술 표 크기. handlers = 더할 처리기(시험용 — 기본 등록부 <see cref="EffectHandlers.BuiltIn"/>에 더한다,
        /// 같은 이름이면 handlers 것, 값이 null이면 그 이름을 뺀다(운명 처리기가 없는 판 — FateTests). 기본 등록부에 데이터가 쓰는 처리기가 있어 바꾸지 않고 더한다 — M5 나머지 op 갈래).
        /// </summary>
        public EffectDb(IReadOnlyList<EffectDef> dataEffects, int moveCount, IReadOnlyDictionary<string, EffectHandler> handlers = null)
        {
            if (dataEffects == null) throw new ArgumentNullException(nameof(dataEffects));
            if (handlers == null) Handlers = EffectHandlers.BuiltIn;
            else
            {
                var merged = new Dictionary<string, EffectHandler>(StringComparer.Ordinal);
                foreach (var kv in EffectHandlers.BuiltIn) merged[kv.Key] = kv.Value;
                foreach (var kv in handlers)
                    if (kv.Value == null) merged.Remove(kv.Key);
                    else merged[kv.Key] = kv.Value;
                Handlers = merged;
            }
            var all = new List<EffectDef>(BuiltInCount + dataEffects.Count);
            all.AddRange(BuiltIns());
            if (all.Count != BuiltInCount) throw new InvalidOperationException("상태이상 기본 효과 수가 BuiltInCount와 다르다");
            var lists = new List<int>[moveCount];
            int lastMove = -1;
            foreach (var e in dataEffects)
            {
                if (e == null) throw new ArgumentException("효과 행이 null이다", nameof(dataEffects));
                if (e.Id != all.Count) throw new ArgumentException($"효과 Id {e.Id}: 기대 {all.Count}(기본 효과 수 + 행 번호)");
                if (e.Source.Kind != SourceKind.Move) throw new ArgumentException($"효과 {e.Id}: 출처 {e.Source.Kind}는 아직 데이터 효과가 아니다(기술만, M3-f)");
                int m = e.Source.Id;
                if ((uint)m >= (uint)moveCount) throw new ArgumentException($"효과 {e.Id}: 기술 색인 {m}이 표 밖");
                if (m < lastMove) throw new ArgumentException($"효과 {e.Id}: 기술 색인 순서가 아니다({m} < {lastMove})");
                if (e.Role != EffectRole.Attacker) throw new ArgumentException($"효과 {e.Id}: 기술 절은 Attacker");
                if (e.Handler != null && !Handlers.ContainsKey(e.Handler) && !e.IsStatic) throw new ArgumentException($"효과 {e.Id}: 모르는 처리기 \"{e.Handler}\"");
                if (e.IsStatic && e.Handler != null && EffectHandlers.Static[e.Handler].Window != e.Window) throw new ArgumentException($"효과 {e.Id}: 셈 처리기 {e.Handler}의 창은 {EffectHandlers.Static[e.Handler].Window}");
                CheckWindow(e);
                lastMove = m;
                (lists[m] ?? (lists[m] = new List<int>())).Add(e.Id);
                all.Add(e);
            }
            Effects = all.ToArray();
            byMove = new int[moveCount][];
            for (int i = 0; i < moveCount; i++) byMove[i] = lists[i] != null ? lists[i].ToArray() : Array.Empty<int>();
        }

        /// <summary>데이터 효과(기술 절)가 쓰는 창과 op: 공격력은 BeforeRoll, 피해 보정과 실행 op는 BeforeDamage(G14). 기본 효과 전용 op는 못 쓴다.</summary>
        static void CheckWindow(EffectDef e)
        {
            foreach (var o in e.Ops)
            {
                var want = o.Op == EffectOp.StrengthTo || o.Op == EffectOp.FromParty || o.Op == EffectOp.LockDice ? Window.BeforeRoll : Window.BeforeDamage;
                if (o.Op == EffectOp.RemoveFaces || o.Op == EffectOp.ReplaceFace) throw new ArgumentException($"효과 {e.Id}: {o.Op}는 상태이상 기본 효과 전용");
                if (e.Window != want) throw new ArgumentException($"효과 {e.Id}: {o.Op}의 창은 {want}(지금 {e.Window})");
                if (e.When != null && e.When.UserParalysisPending) throw new ArgumentException($"효과 {e.Id}: 마비 대기 조건은 상태이상 기본 효과 전용");
            }
        }

        /// <summary>만들 때 받은 기술 표 크기(Ctx가 CardDb와 맞춰 본다).</summary>
        public int MoveCount => byMove.Length;

        /// <summary>그 기술의 효과 절 Id(데이터 순서). 효과가 없거나 플랜 B(−2)면 빈 목록.</summary>
        public IReadOnlyList<int> OfMove(int move) => move >= 0 && move < byMove.Length ? byMove[move] : Array.Empty<int>();

        /// <summary>효과를 옮긴 기술이다(피해만 주는 미구현 기술이 아니다).</summary>
        public bool Implemented(int move) => OfMove(move).Count > 0;

        /// <summary>데이터 효과(기본 효과 뒤).</summary>
        public IEnumerable<EffectDef> DataEffects()
        {
            for (int i = BuiltInCount; i < Effects.Count; i++) yield return Effects[i];
        }

        /// <summary>
        /// 상태이상 기본 효과(S7_6 p.9 표, rulings G14). 화상(총 데미지 −2)·빙결(공격력 +2·교체·도망 불가)·마비 우선도 −3은 상시 규칙이라
        /// BattleRules·BattleFlow가 직접 본다. 여기 있는 것은 창에서 일어나는 것만이다. 순서가 Id다(0~4).
        /// </summary>
        static EffectDef[] BuiltIns()
        {
            SourceRef St(StatusEffect x) => new SourceRef(SourceKind.Status, (int)x);
            EffectOpDef[] Ops(params EffectOpDef[] o) => o;
            return new[]
            {
                // 마비: 마비된 바로 다음 자기 턴의 공격 한 번, 일반 타격을 모두 지운다(그 턴에 공격하지 않으면 사라진다 — 원작 커뮤니티 확인, BattleFlow)
                new EffectDef(0, St(StatusEffect.Paralysed), Window.AfterRoll, EffectRole.Attacker, new EffectCondition(userParalysisPending: true),
                    Ops(new EffectOpDef(EffectOp.RemoveFaces, face: AttackDieFace.RegularHit))),
                // 졸림(공격할 때): 일반 타격 1개 → 상태이상 심볼 1개
                new EffectDef(1, St(StatusEffect.Drowsy), Window.AfterRoll, EffectRole.Attacker, null,
                    Ops(new EffectOpDef(EffectOp.ReplaceFace, face: AttackDieFace.RegularHit, to: AttackDieFace.StatusSymbol, value: 1))),
                // 졸림(공격받을 때): 상대의 일반 타격 1개 → 크리티컬 1개
                new EffectDef(2, St(StatusEffect.Drowsy), Window.AfterRoll, EffectRole.Defender, null,
                    Ops(new EffectOpDef(EffectOp.ReplaceFace, face: AttackDieFace.RegularHit, to: AttackDieFace.CriticalHit, value: 1))),
                // 혼란: 직전 턴과 같은 기술이면 3 피해
                new EffectDef(3, St(StatusEffect.Confused), Window.BeforeDamage, EffectRole.Attacker, new EffectCondition(sameMoveAsLast: true),
                    Ops(new EffectOpDef(EffectOp.SelfDamage, value: StatusRules.ConfusionDamage))),
                // 독: 자기 배틀 턴이 끝날 때 2 피해
                new EffectDef(4, St(StatusEffect.Poisoned), Window.BattleTurnEnd, EffectRole.Attacker, null,
                    Ops(new EffectOpDef(EffectOp.SelfDamage, value: StatusRules.PoisonDamage))),
                // 행운의 알(든 포켓몬이 공격할 때): 심볼 2개 → 크리티컬 1개 — 「if possible」 = 심볼이 2개 이상일 때만, 공격 한 번에 한 번(첫 심볼 → 크리티컬, 다음 심볼 제거)
                new EffectDef(LuckyEggEffect, new SourceRef(SourceKind.Item, -1), Window.AfterRoll, EffectRole.Attacker, new EffectCondition(minSymbols: 2),
                    Ops(new EffectOpDef(EffectOp.ReplaceFace, face: AttackDieFace.StatusSymbol, to: AttackDieFace.CriticalHit, value: 1),
                        new EffectOpDef(EffectOp.RemoveFaces, face: AttackDieFace.StatusSymbol, value: 1))),
                // 진화의 휘석(든 포켓몬이 공격받을 때): 일반 타격 2개까지 제거(약한·적당한 등급이거나 진화가 남은 포켓몬만 — HeldItems.EvioliteWorks)
                new EffectDef(EvioliteEffect, new SourceRef(SourceKind.Item, -1), Window.AfterRoll, EffectRole.Defender, null,
                    Ops(new EffectOpDef(EffectOp.RemoveFaces, face: AttackDieFace.RegularHit, value: 2))),
                // 지속 보정의 프레임(op는 자리 채움 — EffectRules.StepMods가 걸린 보정을 읽어 한다)
                new EffectDef(ModFacesAttacker, SourceRef.None, Window.AfterRoll, EffectRole.Attacker, null, Ops(new EffectOpDef(EffectOp.RemoveFaces))),
                new EffectDef(ModFacesDefender, SourceRef.None, Window.AfterRoll, EffectRole.Defender, null, Ops(new EffectOpDef(EffectOp.RemoveFaces))),
                new EffectDef(ModAttacked, SourceRef.None, Window.BeforeDamage, EffectRole.Defender, null, Ops(new EffectOpDef(EffectOp.Inflict, EffectWho.Target))),
                new EffectDef(TrainerAbility, new SourceRef(SourceKind.TrainerCard, -1), Window.BeforeRoll, EffectRole.Attacker, null, null, handler: TrainerRules.AbilityHandler),
            };
        }
    }

    /// <summary>상태이상 걸기·풀기 규칙(S7_6, p.9, rulings G14). 이벤트는 부르는 쪽이 낸다(StatusApplied·Healed).</summary>
    public static class StatusRules
    {
        public const int PoisonDamage = 2;
        public const int ConfusionDamage = 3;

        /// <summary>그 상태이상에 면역인 타입(p.9): 혼란 에스퍼 · 마비 전기 · 독 독 · 화상 불꽃 · 빙결 얼음 · 졸림 악.</summary>
        public static PokemonType ImmuneType(StatusEffect status)
        {
            switch (status)
            {
                case StatusEffect.Confused: return PokemonType.Psychic;
                case StatusEffect.Paralysed: return PokemonType.Electric;
                case StatusEffect.Poisoned: return PokemonType.Poison;
                case StatusEffect.Burned: return PokemonType.Fire;
                case StatusEffect.Frozen: return PokemonType.Ice;
                case StatusEffect.Drowsy: return PokemonType.Dark;
                default: throw new ArgumentOutOfRangeException(nameof(status), status, "상태이상이 아니다");
            }
        }

        /// <summary>
        /// 걸면 어떻게 되나: 면역 타입 → Immune, 이미 상태이상 → Already(둘 다면 Immune, G14), 아니면 Applied. 상태는 바꾸지 않는다.
        /// 「immune to all status effects」 지속 보정(tempered_steel, ModRules.StatusImmune)이 걸려 있으면 Immune.
        /// </summary>
        public static StatusResult Check(Ctx ctx, PokemonState p, StatusEffect status)
        {
            if (ModRules.StatusImmune(p)) return StatusResult.Immune;
            var immune = ImmuneType(status);
            foreach (var t in ctx.Cards.Pokemon[p.Def].Types)
                if (t == immune) return StatusResult.Immune;
            return p.Status != StatusEffect.None ? StatusResult.Already : StatusResult.Applied;
        }

        /// <summary>건다(Check가 Applied일 때만 바꾼다). 마비면 다음 공격 표시를 켠다(G14) — 출전 중이 아니면 나올 때 BattleFlow가 지운다(원작 커뮤니티 확인).</summary>
        public static StatusResult Inflict(Ctx ctx, PokemonState p, StatusEffect status)
        {
            var r = Check(ctx, p, status);
            if (r != StatusResult.Applied) return r;
            p.Status = status;
            p.ParalysisPending = status == StatusEffect.Paralysed;
            return r;
        }

        /// <summary>
        /// 둘을 동시에 건다(「Inflict both the X and Y statuses on the target simultaneously」 · photon_geyser, rulings G20): 상태이상이 이미 있으면 둘 다 Already(p.9 「cannot be afflicted with
        /// any further status effects」 — 원작 커뮤니티 확인), 아니면 상태마다 면역을 따로 본다 — 면역인 쪽만 Immune이고 나머지는 걸린다 [가정].
        /// 둘 다 걸리면 Status = a, Status2 = b. 결과는 a·b 순서로 돌려준다.
        /// </summary>
        public static void InflictBoth(Ctx ctx, PokemonState p, StatusEffect a, StatusEffect b, out StatusResult ra, out StatusResult rb)
        {
            ra = Check(ctx, p, a);
            rb = Check(ctx, p, b);
            if (ra != StatusResult.Applied && rb != StatusResult.Applied) return;
            if (ra == StatusResult.Applied && rb == StatusResult.Applied)
            {
                p.Status = a;
                p.Status2 = b;
            }
            else p.Status = ra == StatusResult.Applied ? a : b;
            p.ParalysisPending = Has(p, StatusEffect.Paralysed);
        }

        /// <summary>그 상태에 걸려 있다(둘째 칸 포함 — 동시에 건 상태 둘, S7_6).</summary>
        public static bool Has(PokemonState p, StatusEffect status) => status != StatusEffect.None && (p.Status == status || p.Status2 == status || (p.StatusMore & (1 << (int)status)) != 0);

        /// <summary>
        /// 이미 다른 상태가 있어도 더 건다(hex_maniac R 「You may inflict additional status effects onto a target so long as the target does not have that status effect already」, rulings A34):
        /// 면역 → Immune, 그 상태가 이미 있으면 Already, 아니면 빈 칸(Status → Status2 → StatusMore)에 건다. 수 제한이 없다.
        /// </summary>
        public static StatusResult InflictExtra(Ctx ctx, PokemonState p, StatusEffect status)
        {
            var r = Check(ctx, p, status);
            if (r == StatusResult.Immune) return r;
            if (Has(p, status)) return StatusResult.Already;
            if (p.Status == StatusEffect.None) p.Status = status;
            else if (p.Status2 == StatusEffect.None) p.Status2 = status;
            else p.StatusMore |= 1 << (int)status;
            if (status == StatusEffect.Paralysed) p.ParalysisPending = true;
            return StatusResult.Applied;
        }

        /// <summary>걸린 상태(첫 칸 → 둘째 칸, 없으면 빈 목록).</summary>
        public static void Statuses(PokemonState p, List<StatusEffect> into)
        {
            into.Clear();
            if (p.Status != StatusEffect.None) into.Add(p.Status);
            if (p.Status2 != StatusEffect.None) into.Add(p.Status2);
            for (int i = 1; p.StatusMore >> i != 0; i++)
                if ((p.StatusMore & (1 << i)) != 0) into.Add((StatusEffect)i);
        }

        /// <summary>그 상태 하나만 푼다(둘째 칸이 남으면 첫 칸으로 올린다). 마비를 풀면 마비 대기도 지운다.</summary>
        public static void Remove(PokemonState p, StatusEffect status)
        {
            if (p.StatusMore != 0)
            {
                // 셋 이상(hex_maniac R): 남은 것을 순서대로 다시 채운다(칸은 Status → Status2 → StatusMore)
                var left = new List<StatusEffect>(6);
                Statuses(p, left);
                left.Remove(status);
                p.Status = left.Count > 0 ? left[0] : StatusEffect.None;
                p.Status2 = left.Count > 1 ? left[1] : StatusEffect.None;
                p.StatusMore = 0;
                for (int i = 2; i < left.Count; i++) p.StatusMore |= 1 << (int)left[i];
            }
            else if (p.Status2 == status) p.Status2 = StatusEffect.None;
            else if (p.Status == status)
            {
                p.Status = p.Status2;
                p.Status2 = StatusEffect.None;
            }
            if (!Has(p, StatusEffect.Paralysed)) p.ParalysisPending = false;
        }

        /// <summary>상태이상을 푼다(전체 회복·기절·푸는 효과, p.9). 둘째 칸과 마비 대기도 함께 지운다.</summary>
        public static void Clear(PokemonState p)
        {
            p.Status = StatusEffect.None;
            p.Status2 = StatusEffect.None;
            p.StatusMore = 0;
            p.ParalysisPending = false;
        }

        /// <summary>독이면 피해를 회복하지 못한다(p.9 「You may no longer heal damage」).</summary>
        public static bool BlocksHealing(PokemonState p) => Has(p, StatusEffect.Poisoned);

        /// <summary>빙결이면 교체·도망 불가(p.9).</summary>
        public static bool BlocksEscape(PokemonState p) => Has(p, StatusEffect.Frozen);

        /// <summary>상태이상 주사위의 여섯 면(TTS 상태 주사위 — 상태마다 한 면). 굴림은 엔진 난수 Next(6).</summary>
        public static StatusEffect RollDie(IRng rng) => (StatusEffect)(1 + rng.Next(6));
    }
}
