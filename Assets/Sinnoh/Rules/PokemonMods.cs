using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 포켓몬에 걸린 지속 보정 하나(M5 효과 기반, rulings G16). PokemonState.Mods에 건 순서대로 쌓인다 — 상태 안의 데이터라 복제·저장·해시에 든다.
    /// Scope = 언제까지, Role = 언제 적용하나(Attacker = 이 포켓몬이 공격할 때 / Defender = 공격받을 때), Op·Face·Value = 그때 적용할 상시 op.
    /// Fresh = 공격이 진행되는 동안 걸렸다 — 그 공격에는 적용하지 않고 그 공격이 끝나도 쓰이지 않는다(「When the target is next attacked」는 다음 공격부터).
    /// </summary>
    public sealed class PokemonMod : IStatePart<PokemonMod>
    {
        public ModScope Scope;
        public EffectRole Role;
        public EffectOp Op;
        public AttackDieFace Face;
        public int Value;
        /// <summary>건 출처(그 기술·카드). 피해 내역(DamageBreakdown)의 단계 출처로 나간다.</summary>
        public SourceRef Source = SourceRef.None;
        public bool Fresh;

        public void CopyFrom(PokemonMod o)
        {
            Scope = o.Scope;
            Role = o.Role;
            Op = o.Op;
            Face = o.Face;
            Value = o.Value;
            Source = o.Source;
            Fresh = o.Fresh;
        }

        public void Walk(IStateWriter w)
        {
            w.Int((int)Scope);
            w.Int((int)Role);
            w.Int((int)Op);
            w.Int((int)Face);
            w.Int(Value);
            Source.Walk(w);
            StateParts.Bool(w, Fresh);
        }

        public void Read(IStateReader r)
        {
            Scope = (ModScope)r.Int();
            Role = (EffectRole)r.Int();
            Op = (EffectOp)r.Int();
            Face = (AttackDieFace)r.Int();
            Value = r.Int();
            Source = SourceRef.Read(r);
            Fresh = StateParts.ReadBool(r);
        }
    }

    /// <summary>
    /// 지속 보정 저장소의 규칙(M5 효과 기반, rulings G16). 거는 곳은 <see cref="Add"/> 하나(op Hold와 처리기가 부른다), 지우는 곳은 배틀 단계가 부르는 셋:
    /// <see cref="AttackDone"/>(공격 하나가 끝남 — 한 번짜리) · <see cref="LeftField"/>(나감 — 배틀 끝까지만 남김) · <see cref="BattleEnded"/>(전부).
    /// 적용은 계산할 때 훑는다(01 §7-1 상시 효과): 새 종류는 <see cref="CanHold"/>와 훑는 곳을 함께 더한다. 훑는 곳(M5 「기술 효과 — 지속 보정 갈래」, rulings G19):
    /// 피해 보정 <see cref="DamageMods"/>(FaceDamage·ExtraDamage·FaceDamageOrCapture) · 피해 상한 <see cref="DamageCap"/> · 크리티컬 되돌리기 <see cref="ReflectsCrits"/> · 굴림에 면 더하기 <see cref="AddFaces"/>(굴린 바로 뒤) ·
    /// 굴린 뒤 면 지우기 <see cref="RemoveFaces"/>(AfterRoll 프레임 — 결정 72 순서 선택에 든다) · 공격받을 때 공격자에게 상태이상 <see cref="AttackedStatuses"/>(BeforeDamage 프레임) ·
    /// 턴 끝 회복 <see cref="TurnEndHeal"/> · 상태이상 면역 <see cref="StatusImmune"/> · 교체·도망 금지 <see cref="NoEscape"/> · 폼 바꾸기 금지 <see cref="NoFormChange"/> ·
    /// 기술 효과 교체 금지 <see cref="MoveSwitchBlocked"/> · 포획 굴림 <see cref="CaptureBonus"/>.
    /// </summary>
    public static class ModRules
    {
        /// <summary>지속 보정으로 걸 수 있는 op(훑는 곳이 있는 것만). 「기술 효과 — 지속 보정 갈래」가 면 더하기·지우기 등을 더한다(01 §7-6).</summary>
        public static bool CanHold(EffectOp op) => OnRoll(op) || op == EffectOp.TurnEndHeal || op == EffectOp.StatusImmune || op == EffectOp.NoEscape
            || op == EffectOp.NoFormChange || op == EffectOp.NoMoveSwitch;

        /// <summary>
        /// 공격 굴림과 얽힌 보정(공격할 때·공격받을 때 — Role을 쓰고, 한 번짜리 범위로 걸 수 있고, Fresh면 건너뛴다). 그 밖(턴 끝 회복·면역·금지)은 걸린 순간부터 있고
        /// 교체까지·배틀 끝까지로만 건다.
        /// </summary>
        public static bool OnRoll(EffectOp op) => op == EffectOp.FaceDamage || op == EffectOp.ExtraDamage || op == EffectOp.FaceDamageOrCapture || op == EffectOp.AddFaces
            || op == EffectOp.RemoveFaces || op == EffectOp.Inflict || op == EffectOp.DamageCap || op == EffectOp.Retaliate || op == EffectOp.FinalMove || op == EffectOp.ReflectCrits;

        /// <summary>그 보정이 면(Face) 칸을 쓴다.</summary>
        public static bool HoldUsesFace(EffectOp op) => op == EffectOp.FaceDamage || op == EffectOp.FaceDamageOrCapture || op == EffectOp.AddFaces || op == EffectOp.RemoveFaces
            || op == EffectOp.Retaliate;

        /// <summary>그 보정이 값(Value) 칸을 쓴다(Inflict의 Value는 건 상태 — 데이터는 status 칸).</summary>
        public static bool HoldUsesValue(EffectOp op) => op == EffectOp.FaceDamage || op == EffectOp.FaceDamageOrCapture || op == EffectOp.ExtraDamage || op == EffectOp.AddFaces
            || op == EffectOp.RemoveFaces || op == EffectOp.TurnEndHeal || op == EffectOp.DamageCap || op == EffectOp.Retaliate;

        /// <summary>
        /// 그 포켓몬에 지속 보정을 건다. NextAttacked·NextAttack은 Role을 범위에 맞춘다. 공격이 진행 중이면(기술을 고른 뒤 ~ 배틀 턴 끝 단계 전) Fresh다 —
        /// 배틀 턴 끝 단계(AttackDone이 Fresh를 내린 뒤)와 그 뒤에 건 것은 바로 다음 공격부터 적용된다(메인 검토 11-14: 「&lt;= TurnEnd」면 턴 끝에 건 보정이 한 번 건너뛰었다).
        /// 굴림과 얽히지 않은 보정(<see cref="OnRoll"/>이 아닌 것)은 Fresh가 없다.
        /// 같은 보정은 겹치지 않는다(룰북 FAQ p.13 「Can the effect of a move stack with itself? No」 · 원작 커뮤니티 확인, rulings G19): 같은 내용
        /// (op · 자리 · 면 · 값, 범위는 한 번짜리끼리 같거나 오래가는 것(교체까지·배틀 끝까지)끼리)의 보정이 이미 걸려 있으면 새로 걸지 않고 그것을 돌려준다 — 출처(기술)가 달라도 같다.
        /// 지금 공격에 쓰이고 사라질 한 번짜리(공격하는 포켓몬의 「다음 공격할 때」 · 받는 포켓몬의 「다음 공격받을 때」, Fresh가 아닌 것)는 견주지 않는다.
        /// </summary>
        public static PokemonMod Add(GameState s, int pokemon, ModScope scope, EffectRole role, EffectOp op, AttackDieFace face, int value, SourceRef source)
        {
            if (scope == ModScope.None) throw new ArgumentException("지속 보정의 범위가 없다", nameof(scope));
            if (!CanHold(op)) throw new ArgumentException($"{op}는 지속 보정으로 걸 수 없다", nameof(op));
            if (scope == ModScope.NextAttacked) role = EffectRole.Defender;
            else if (scope == ModScope.NextAttack) role = EffectRole.Attacker;
            var b = s.Battle;
            bool attacking = b != null && b.Move != -1 && b.Step < BattleStep.TurnEnd && b.ActiveSide >= 0;
            int attacker = attacking ? b.Sides[b.ActiveSide].Active : -1, defender = attacking ? b.Sides[1 - b.ActiveSide].Active : -1;
            foreach (var old in s.Pokemon[pokemon].Mods)
            {
                if (old.Op != op || old.Role != role || old.Face != face || old.Value != value || Lasting(old.Scope) != Lasting(scope)) continue;
                if (!Lasting(scope) && old.Scope != scope) continue;
                bool spending = !old.Fresh && ((old.Scope == ModScope.NextAttack && pokemon == attacker) || (old.Scope == ModScope.NextAttacked && pokemon == defender));
                if (!spending) return old;
            }
            var m = new PokemonMod { Scope = scope, Role = role, Op = op, Face = face, Value = value, Source = source, Fresh = OnRoll(op) && attacking };
            s.Pokemon[pokemon].Mods.Add(m);
            return m;
        }

        static bool Lasting(ModScope scope) => scope == ModScope.UntilSwitch || scope == ModScope.BattleEnd;

        /// <summary>
        /// 공격 하나가 끝났다(배틀 턴 끝, 기술을 쓴 턴만): 공격한 포켓몬의 「다음 공격할 때」와 받은 포켓몬의 「다음 공격받을 때」 가운데 이번 공격에 적용된 것(Fresh가 아닌 것)을 지우고,
        /// 두 포켓몬의 Fresh를 내린다. 볼을 던진 턴은 공격이 아니라 부르지 않는다.
        /// </summary>
        public static void AttackDone(GameState s, int attacker, int defender)
        {
            Spend(s, attacker, ModScope.NextAttack);
            Spend(s, defender, ModScope.NextAttacked);
        }

        static void Spend(GameState s, int pokemon, ModScope once)
        {
            if (pokemon < 0) return;
            var mods = s.Pokemon[pokemon].Mods;
            for (int i = mods.Count - 1; i >= 0; i--)
            {
                if (mods[i].Scope == once && !mods[i].Fresh) mods.RemoveAt(i);
                else mods[i].Fresh = false;
            }
        }

        /// <summary>출전 중이던 포켓몬이 나갔다(교체·기절·배틀 끝): 배틀 끝까지인 보정만 남긴다(G16 — 액티브 존을 비우면 한 번짜리도 사라진다).</summary>
        public static void LeftField(PokemonState p)
        {
            for (int i = p.Mods.Count - 1; i >= 0; i--)
            {
                if (p.Mods[i].Scope != ModScope.BattleEnd) p.Mods.RemoveAt(i);
                else p.Mods[i].Fresh = false;
            }
        }

        /// <summary>
        /// 그 쪽에 포켓몬이 나왔다: 같은 쪽의 다른 포켓몬이 들고 있던 「배틀 끝까지」 보정(eternal_glade가 늘린 것 — 데이터는 BattleEnd로 바로 걸지 않는다)을 나온 포켓몬에게 옮긴다
        /// [가정 — 원작 커뮤니티 확인(다음에 나오는 포켓몬에게도). 보정을 쪽에 두지 않고 출전 포켓몬에게 옮겨
        /// 훑는 곳은 그대로 포켓몬의 Mods를 읽는다 — 출전 중인 포켓몬만 굴리고·맞고·턴 끝을 맞으니 결과가 같다]. 같은 내용이 이미 있으면 옮기지 않고 버린다(겹치지 않는다).
        /// 옮기는 것은 기술이 건 보정뿐이다(<see cref="Extend"/>가 바꾼 것) — 지닌물건의 기록(별 방패·폭발수정의 ItemUsed)은 든 포켓몬에게 남는다(M5 규칙 끝 검토 반영).
        /// </summary>
        internal static void SentIn(GameState s, BattleState b, int k, int pokemon)
        {
            var side = b.Sides[k];
            var mine = s.Pokemon[pokemon].Mods;
            void Take(int q)
            {
                if (q < 0 || q == pokemon) return;
                var mods = s.Pokemon[q].Mods;
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (m.Scope != ModScope.BattleEnd || m.Source.Kind != SourceKind.Move) continue;
                    mods.RemoveAt(i--);
                    if (!mine.Exists(x => x.Scope == ModScope.BattleEnd && x.Op == m.Op && x.Role == m.Role && x.Face == m.Face && x.Value == m.Value)) mine.Add(m);
                }
            }
            if (side.Owner >= 0)
                foreach (int q in s.Seats[side.Owner].Party) Take(q);
            foreach (int q in side.Roster) Take(q);
        }

        /// <summary>
        /// 굴림·피해 셈이 그 포켓몬의 지속 보정을 훑는가: 체력 0이면 −1(훑지 않는다). 기라티나로 체력 0이 된 포켓몬이 그 공격을 쓰는 동안(rulings A24 ③) · encore의 마지막 기술 —
        /// 쓰러진 포켓몬의 것을 훑지 않는 G14 그대로(프레임은 EffectRules.AddStatus·AddItem·AddMods가 거른다).
        /// </summary>
        internal static int Scanned(GameState s, Ctx ctx, int pokemon) => pokemon >= 0 && !PokemonRules.IsFainted(ctx, s.Pokemon[pokemon]) ? pokemon : -1;

        /// <summary>배틀이 끝났다: 그 배틀에 있던 포켓몬(좌석 파티·명단)의 지속 보정을 모두 지운다.</summary>
        public static void BattleEnded(GameState s, BattleState b)
        {
            foreach (var side in b.Sides)
            {
                if (side.Owner >= 0)
                    foreach (int p in s.Seats[side.Owner].Party)
                        if (p >= 0) s.Pokemon[p].Mods.Clear();
                foreach (int p in side.Roster) s.Pokemon[p].Mods.Clear();
            }
        }

        /// <summary>
        /// 피해 보정(01 §6-4 ②): 공격하는 포켓몬의 Attacker 보정 → 받는 포켓몬의 Defender 보정 순서로 FaceDamage(그 면 하나마다)·ExtraDamage를 Mod{건 출처, 증감}로 더한다(0은 뺀다).
        /// 합만 쓰므로 순서는 결과를 바꾸지 않는다. Fresh는 건너뛴다. 실제 피해와 결정 72 순서 미리 셈(EffectRules.Preview)이 같이 쓴다.
        /// </summary>
        public static void DamageMods(GameState s, int attacker, int defender, IReadOnlyList<AttackDieFace> faces, List<Mod> into)
        {
            Scan(s, attacker, EffectRole.Attacker, faces, into);
            Scan(s, defender, EffectRole.Defender, faces, into);
        }

        static void Scan(GameState s, int pokemon, EffectRole role, IReadOnlyList<AttackDieFace> faces, List<Mod> into)
        {
            if (pokemon < 0) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
            {
                if (m.Fresh || m.Role != role) continue;
                int d;
                switch (m.Op)
                {
                    case EffectOp.FaceDamage:
                    case EffectOp.FaceDamageOrCapture: d = m.Value * AttackDice.Count(faces, m.Face); break;
                    case EffectOp.ExtraDamage: d = m.Value; break;
                    default: continue;
                }
                if (d != 0) into.Add(new Mod { Source = m.Source, Delta = d });
            }
        }

        /// <summary>받는 포켓몬의 피해 상한(「take at most 1 damage」 — 01 §6-4 ⑦). 여럿이면 가장 작은 값, 없으면 0. source = 그 보정을 건 출처.</summary>
        public static int DamageCap(GameState s, int defender, out SourceRef source)
        {
            source = SourceRef.None;
            int cap = 0;
            if (defender < 0) return 0;
            foreach (var m in s.Pokemon[defender].Mods)
            {
                if (m.Op != EffectOp.DamageCap || m.Fresh || m.Role != EffectRole.Defender || (cap > 0 && m.Value >= cap)) continue;
                cap = m.Value;
                source = m.Source;
            }
            return cap;
        }

        /// <summary>받는 포켓몬이 이번 공격의 크리티컬을 공격한 쪽으로 되돌리나(barrier — rulings G20 ⑷). source = 그 보정을 건 출처(되돌린 피해의 출처).</summary>
        public static bool ReflectsCrits(GameState s, int defender, out SourceRef source)
        {
            source = SourceRef.None;
            if (defender < 0) return false;
            foreach (var m in s.Pokemon[defender].Mods)
            {
                if (m.Op != EffectOp.ReflectCrits || m.Fresh || m.Role != EffectRole.Defender) continue;
                source = m.Source;
                return true;
            }
            return false;
        }

        // ── 굴림의 면(더하기 · 지우기) ──

        /// <summary>
        /// 굴림에 면을 더한다(「add 1 regular hit to the roll」 · 「Add 2 regular hits to your attack rolls」): 공격하는 포켓몬의 Attacker 보정 → 받는 포켓몬의 Defender 보정, 건 순서.
        /// 굴린 바로 뒤에 한다 — 더한 면도 그 굴림의 일부라 뒤따르는 면 바꾸기(마비가 일반 타격을 모두 지움 — 원작 커뮤니티 확인 · 졸림 · 지우기 보정)가 함께 본다. 결정 72 순서 선택에는 들지 않는다.
        /// 보정 하나마다 DiceFacesChanged(출처 = 건 기술, 더한 주사위는 Before = None · Index = 굴림 끝).
        /// </summary>
        internal static void AddFaces(GameState s, int attacker, int defender, List<AttackDieFace> faces, Emitter fx)
        {
            AddFacesOf(s, attacker, EffectRole.Attacker, faces, fx);
            AddFacesOf(s, defender, EffectRole.Defender, faces, fx);
        }

        static void AddFacesOf(GameState s, int pokemon, EffectRole role, List<AttackDieFace> faces, Emitter fx)
        {
            if (pokemon < 0) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
            {
                if (m.Op != EffectOp.AddFaces || m.Fresh || m.Role != role) continue;
                var before = AttackDice.Src(faces);
                var changes = new FaceChange[m.Value];
                for (int i = 0; i < changes.Length; i++)
                {
                    changes[i] = new FaceChange { Index = faces.Count, Before = AttackDieFace.None, After = m.Face };
                    faces.Add(m.Face);
                }
                if (fx.On) fx.Add(new DiceFacesChanged { Source = m.Source, Changes = changes, SrcBefore = before, SrcAfter = AttackDice.Src(faces) });
            }
        }

        /// <summary>그 포켓몬이 그 자리(공격할 때·공격받을 때)에서 이번 굴림의 면을 지우는 보정을 가졌다. source = 그 가운데 먼저 건 것의 출처(프레임 출처).</summary>
        public static bool RemovesFaces(GameState s, int pokemon, EffectRole role, out SourceRef source)
        {
            source = SourceRef.None;
            if (pokemon < 0) return false;
            foreach (var m in s.Pokemon[pokemon].Mods)
            {
                if (m.Op != EffectOp.RemoveFaces || m.Fresh || m.Role != role) continue;
                source = m.Source;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 굴린 뒤 면 지우기(「remove 1 regular hit from the roll」): 그 포켓몬의 그 자리 보정을 건 순서대로, 보정마다 Face 면을 왼쪽부터 Value개 지운다(지운 자리는 None).
        /// 지우기끼리는 순서가 결과를 바꾸지 않아 포켓몬 하나의 것을 프레임 하나(EffectDb.ModFacesAttacker·ModFacesDefender)가 한꺼번에 한다 —
        /// 실행(report로 이벤트)과 결정 72 미리 셈(report = null)이 같이 쓴다.
        /// </summary>
        public static void RemoveFaces(GameState s, int pokemon, EffectRole role, List<AttackDieFace> faces, Action<PokemonMod, List<FaceChange>, int[]> report)
        {
            if (pokemon < 0) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
            {
                if (m.Op != EffectOp.RemoveFaces || m.Fresh || m.Role != role) continue;
                var before = report != null ? AttackDice.Src(faces) : null;
                var changes = new List<FaceChange>();
                int left = m.Value > 0 ? m.Value : int.MaxValue; // 0 = 모두(kings_shield 「remove all regular hits」, rulings A26)
                for (int i = 0; i < faces.Count && left > 0; i++)
                {
                    if (faces[i] != m.Face) continue;
                    changes.Add(new FaceChange { Index = i, Before = faces[i], After = AttackDieFace.None });
                    faces[i] = AttackDieFace.None;
                    left--;
                }
                report?.Invoke(m, changes, before);
            }
        }

        // ── 공격받을 때 공격자에게 · 턴 끝 · 면역 · 금지 ──

        /// <summary>그 포켓몬이 공격받을 때 공격자에게 거는 상태이상 보정(frigid_ice — Value = 상태, 건 순서). 프레임 EffectDb.ModAttacked가 건다.</summary>
        public static void AttackedStatuses(GameState s, int pokemon, List<PokemonMod> into)
        {
            into.Clear();
            if (pokemon < 0) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
                if (m.Op == EffectOp.Inflict && !m.Fresh && m.Role == EffectRole.Defender) into.Add(m);
        }

        /// <summary>그 포켓몬이 공격받을 때 공격자에게 되돌리는 피해 보정(shell_trap — Face가 굴림에 있으면 Value, 건 순서). 프레임 EffectDb.ModAttacked가 건다(rulings G20).</summary>
        public static void Retaliations(GameState s, int pokemon, List<PokemonMod> into)
        {
            into.Clear();
            if (pokemon < 0) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
                if (m.Op == EffectOp.Retaliate && !m.Fresh && m.Role == EffectRole.Defender) into.Add(m);
        }

        /// <summary>
        /// 그 포켓몬 쪽의 배틀 턴이 끝났다(턴 끝 피해 창 — 배틀이 끝나는 턴에는 없다, G14): 턴 끝 회복 보정을 피해 버퍼에 넣는다(독과 함께 동시에 적용, 독이면 0).
        /// 이미 쓰러진 포켓몬(턴 안 피해로 체력 0)은 회복하지 않는다.
        /// </summary>
        internal static void TurnEndHeal(GameState s, Ctx ctx, BattleState b, int pokemon)
        {
            if (pokemon < 0 || PokemonRules.IsFainted(ctx, s.Pokemon[pokemon])) return;
            foreach (var m in s.Pokemon[pokemon].Mods)
                if (m.Op == EffectOp.TurnEndHeal) BattleFlow.Buffer(b, pokemon, m.Value, m.Source);
        }

        static bool Has(PokemonState p, EffectOp op)
        {
            foreach (var m in p.Mods)
                if (m.Op == op) return true;
            return false;
        }

        /// <summary>상태이상에 걸리지 않는다(tempered_steel). StatusRules.Check가 본다.</summary>
        public static bool StatusImmune(PokemonState p) => Has(p, EffectOp.StatusImmune);

        /// <summary>교체·도망하지 못한다(「The target may no longer switch out or flee the battle」). 빙결과 같은 자리(교체 창 · 야생의 도망)가 본다.</summary>
        public static bool NoEscape(PokemonState p) => Has(p, EffectOp.NoEscape);

        /// <summary>폼을 바꾸지 못한다(imprison 「may no longer change form」). 폼을 바꾸는 곳(FormRules.Change · adaptive_cloak)이 본다.</summary>
        public static bool NoFormChange(PokemonState p) => Has(p, EffectOp.NoFormChange);

        /// <summary>
        /// 지금 배틀에서 기술 효과로 교체하지 못한다(fairy_lock 「All Pokémon can no longer switch out using move effects」): 출전 중인 포켓몬 가운데 그 보정을 가진 것이 있다.
        /// 교체 기술(SwitchOut — BattleFlow.MoveSwitch · FromParty — ChoiceRules.FromPartyMoves)이 본다.
        /// </summary>
        public static bool MoveSwitchBlocked(GameState s)
        {
            if (s.Battle == null) return false;
            foreach (var side in s.Battle.Sides)
                if (side.Active >= 0 && Has(s.Pokemon[side.Active], EffectOp.NoMoveSwitch)) return true;
            return false;
        }

        /// <summary>
        /// eternal_glade 「Your currently active effects that would last until the user switches out now last until the end of the battle」: 그 포켓몬에 걸린 「교체할 때까지」 기술 효과를
        /// 「배틀이 끝날 때까지」로 바꾼다. 「Until the user switches out:」 효과는 모두 쓴 포켓몬 자신에게 걸린다 — 상대가 건 교체·도망·폼 금지와 지닌물건의 기록(결단의 머리띠 등)은
        /// 그 문구의 효과가 아니라 그대로 둔다. 바꾼 수를 돌려준다.
        /// </summary>
        public static int Extend(PokemonState p)
        {
            int n = 0;
            foreach (var m in p.Mods)
            {
                if (m.Scope != ModScope.UntilSwitch || m.Source.Kind != SourceKind.Move || m.Op == EffectOp.NoEscape || m.Op == EffectOp.NoFormChange) continue;
                m.Scope = ModScope.BattleEnd;
                n++;
            }
            return n;
        }

        /// <summary>encore의 「다음에 공격받을 때」 마지막 기술 보정(FinalMove, Value = encore 절의 효과 Id)을 이 공격에 쓰고 돌려준다 — 없으면(Fresh 포함) null. BattleFlow가 체력 0일 때 부른다(rulings G21).</summary>
        internal static PokemonMod TakeFinalMove(GameState s, int pokemon)
        {
            var mods = s.Pokemon[pokemon].Mods;
            int i = mods.FindIndex(m => m.Op == EffectOp.FinalMove && !m.Fresh && m.Role == EffectRole.Defender);
            if (i < 0) return null;
            var r = mods[i];
            mods.RemoveAt(i);
            return r;
        }

        // ── 포획 굴림(sweet_scent) ──

        /// <summary>그 포켓몬을 노린 포획 굴림의 합에 더할 값(「On the next attack or capture roll against the target, status symbols deal +1 damage」): 그 면 하나마다 Value.</summary>
        public static int CaptureBonus(GameState s, int target, IReadOnlyList<AttackDieFace> faces)
        {
            int n = 0;
            if (target < 0) return 0;
            foreach (var m in s.Pokemon[target].Mods)
                if (m.Op == EffectOp.FaceDamageOrCapture && !m.Fresh && m.Role == EffectRole.Defender) n += m.Value * AttackDice.Count(faces, m.Face);
            return n;
        }

        /// <summary>그 포켓몬에게 포획 굴림을 던졌다: 포획 굴림에도 쓰이는 한 번짜리 보정을 쓴 것으로 지운다(다른 「다음에 공격받을 때」는 남는다 — 볼은 공격이 아니다, G16).</summary>
        internal static void CaptureRolled(GameState s, int target)
        {
            if (target < 0) return;
            s.Pokemon[target].Mods.RemoveAll(m => m.Op == EffectOp.FaceDamageOrCapture && m.Scope == ModScope.NextAttacked && !m.Fresh);
        }
    }

    /// <summary>
    /// op로 안 되는 효과의 C# 처리기(01 §7-1·§7-2). 프레임 한 걸음을 돌고 Continue(다시 불린다) / Wait(선택을 열었다 — <see cref="EffectRules.Wait"/>) / Done(프레임을 뺀다)을 돌려준다.
    /// 선택을 사이에 두고 C# 지역 변수를 들지 않는다: 이어 갈 값은 f.StepIndex·f.Locals에 둔다(답은 Locals[EffectFrame.LocalAnswerKind·LocalAnswerTarget]).
    /// 조건(When)·한도(Limit)는 처리기를 부르기 전에 처리기가 본 것과 같이 본다 — 한도를 쓰는 때는 처리기가 <see cref="EffectRules.Spend"/>로 정한다.
    /// </summary>
    public delegate EffectStepResult EffectHandler(GameState s, Ctx ctx, Emitter fx, EffectFrame f);

    /// <summary>
    /// 처리기 등록부(01 §7-1 `Handler: "&lt;이름&gt;"`). 이름 = effects.json 행의 handler 칸. 코드 상수라 숨은 상태가 없다 — 시험은 EffectDb에 다른 표를 넣는다.
    /// 운명의 변화 네 장(`fate.*`, FateRules.Register — rulings A24)과 나머지 op 갈래(rulings G20)의 고르기 넷 · 핸들러 갈래(rulings G21)의 여덟(<see cref="MoveHandlers"/>)이 있다. 공격력 셈은 프레임이 아니라 <see cref="Static"/>(01 §7-6).
    /// </summary>
    public static class EffectHandlers
    {
        public static readonly IReadOnlyDictionary<string, EffectHandler> BuiltIn = Build();

        /// <summary>
        /// 셈 처리기 등록부(M5 「기술 효과 — 핸들러 갈래」, rulings G21): 프레임이 아니라 계산할 때 부르는 처리기 — 공격력(BeforeRoll, EffectRules.StrengthMods ← BattleRules.RollPower)과
        /// 효과 피해(BeforeDamage ②, EffectRules.DamageMods). 이 이름의 절은 상시 절이다(EffectDef.IsStatic). 코드 상수라 EffectDb에 넣어 바꾸지 않는다.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, StaticHandler> Static = MoveHandlers.Statics();

        static Dictionary<string, EffectHandler> Build()
        {
            var d = FateRules.Register(new Dictionary<string, EffectHandler>(StringComparer.Ordinal));
            MoveHandlers.Register(d);
            TrainerRules.Register(d); // 트레이너 카드 능력의 물음(M6, rulings A34)
            return d;
        }
    }

    /// <summary>
    /// 셈 처리기 하나(<see cref="EffectHandlers.Static"/>). Window = BeforeRoll(공격력) · BeforeDamage(효과 피해 ②). Value(s, ctx, 기술, 공격하는 포켓몬, 받는 포켓몬, 굴림 — 공격력이면 null) = 그 값.
    /// Sets(공격력만) = 「Attack strength is equal to …」(「공격력 = n」 — StrengthTo처럼 맨 앞, 다른 보정은 그 뒤 G14), 아니면 「is increased by …」(+n). 상태를 바꾸지 않는다.
    /// </summary>
    public sealed class StaticHandler
    {
        public readonly Window Window;
        public readonly bool Sets;
        public readonly Func<GameState, Ctx, int, int, int, IReadOnlyList<AttackDieFace>, int> Value;

        public StaticHandler(Window window, bool sets, Func<GameState, Ctx, int, int, int, IReadOnlyList<AttackDieFace>, int> value)
        {
            if (window != Window.BeforeRoll && window != Window.BeforeDamage) throw new ArgumentException("셈 처리기의 창은 BeforeRoll·BeforeDamage", nameof(window));
            if (sets && window != Window.BeforeRoll) throw new ArgumentException("「공격력 = n」은 BeforeRoll", nameof(sets));
            Window = window;
            Sets = sets;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
