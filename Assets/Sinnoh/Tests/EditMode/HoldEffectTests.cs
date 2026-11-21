using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「기술 효과 — 지속 보정 갈래」(01 §7-6, rulings G16·G19): 다음에 공격받을·공격할 때 · 교체할 때까지 · 교체·도망 금지 기술 55장의 효과.
    /// 효과의 근거는 xlsx 영문 문구다(기술 키가 TestCase·시험 이름에 있다 — <see cref="Moved"/>가 목록). 실제 배틀 흐름(트레이너전 — 두 좌석이 번갈아 기술을 쓴다)으로 돌고,
    /// 굴림은 그 면이 나오는 난수 시드를 찾아 정한다(<see cref="SeedFor"/> — 시험은 결정적이다).
    /// 포켓몬: 해피너스 242(노말, 체력 14, 우선도 6) 대 악식킹 799(악/드래곤, 23, 5) — 좌석 0이 먼저다. 기술은 배운 기술 칸 0에 넣어 쓴다(두번치기 double_hit = 공격력 2, 일반 타격마다 +1 — 남는 상태가 없다. 전광석화는 핸들러 갈래가 둘째 기술로 옮겼다).
    /// </summary>
    public class HoldEffectTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const AttackDieFace X = AttackDieFace.None;
        const string Plain = "double_hit";

        /// <summary>이 묶음이 옮긴 기술 55장(effects-todo의 그 갈래 56장 가운데 — 남긴 것은 <see cref="Left"/>).</summary>
        static readonly string[] Moved =
        {
            // 다음에 공격받을·공격할 때 34
            "struggle_bug", "flower_shield", "bite", "crunch", "chomp", "iron_tail", "charge_beam", "peck", "air_cutter",
            "draco_meteor", "close_combat", "overheat", "leaf_storm", "headlong_rush", "play_rough", "wing_attack", "aurora_beam", "tackle", "water_gun",
            "dazzling_gleam", "mud_bomb", "flash_cannon", "muddy_water", "temporal_guard", "disguise", "bulldoze", "bubble_beam", "sweet_scent",
            "conversion_2", "poison_sting", "lunar_blessing", "psyshield_bash", "steel_wing", "take_heart",
            // 교체할 때까지 11
            "power_plant", "moonlight", "repairing_rock", "bulk_up", "fiery_dance", "ominous_wind", "conversion", "ancient_power", "frigid_ice", "tempered_steel", "eternal_glade",
            // 교체·도망 금지 10
            "fairy_lock", "magma_storm", "spirit_shackle", "sand_tomb", "whirlpool", "coil", "bug_trap", "anchor_shot", "crush_grip", "imprison",
        };

        /// <summary>그 갈래에서 남긴 기술: kings_shield 「… Change to Blade Form if possible.」 — 폼 바꾸기와 함께 「전설·알파·폼」 묶음이 옮겼다(LegendFormTests, rulings A26).</summary>
        static readonly string[] Left = { "kings_shield" };

        int Mv(string key)
        {
            int m = Scene.Move(ctx, key);
            Assert.GreaterOrEqual(m, 0, key);
            return m;
        }

        SourceRef MoveSrc(string key) => new SourceRef(SourceKind.Move, Mv(key));

        // ── 장면 ──

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            /// <summary>좌석 0의 포켓몬(먼저 움직인다) · 좌석 1의 포켓몬.</summary>
            public int A, D;
        }

        /// <summary>트레이너전: 좌석 0의 a 대 좌석 1의 d(각 한 마리). 좌석 0의 기술 고르기가 열려 있다.</summary>
        Arena Duel(string a = "242:0", string d = "799:0")
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, a);
            t.D = Scene.Give(t.S, ctx, 1, d);
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            return t;
        }

        /// <summary>공격력 n의 굴림이 정확히 그 면들로 나오는 난수 시드(굴림은 그 행동의 첫 난수다).</summary>
        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 2000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        /// <summary>
        /// 차례인 좌석의 출전 포켓몬이 그 기술을 쓴다(배운 기술 칸 0에 넣는다). faces를 주면 그 굴림이 나온다(기술 공격력만큼). 굴리기 전에 두 포켓몬의 피로를 0으로 돌린다(피로 피해가 끼지 않게).
        /// 반환 = 그 답의 이벤트(다음 선택이 열릴 때까지).
        /// </summary>
        List<GameEvent> Turn(Arena t, string move, params AttackDieFace[] faces)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int active = t.S.Battle.Sides[ChoiceRules.BattleSideOf(t.S, ch.Seat)].Active;
            int m = Mv(move);
            t.S.Pokemon[active].Moves[0] = m;
            t.S.Pokemon[t.A].Fatigue = 0;
            t.S.Pokemon[t.D].Fatigue = 0;
            if (faces.Length > 0) t.S.Rng = new Pcg32(SeedFor(faces));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(m), t.Log);
            var events = t.Log.Events.GetRange(from, t.Log.Events.Count - from);
            if (faces.Length > 0) CollectionAssert.AreEqual(faces, One<DiceRolled>(events).Faces, $"{move}: 굴림");
            return events;
        }

        /// <summary>두 포켓몬의 피해를 지운다(긴 장면에서 쓰러지지 않게).</summary>
        static void Rest(Arena t)
        {
            t.S.Pokemon[t.A].Damage = 0;
            t.S.Pokemon[t.D].Damage = 0;
        }

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = events.FindAll(e => e is T);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return (T)l[0];
        }

        /// <summary>그 출처의 면 바꾸기 이벤트.</summary>
        static List<DiceFacesChanged> FaceChanges(List<GameEvent> events, SourceRef src)
        {
            var r = new List<DiceFacesChanged>();
            foreach (var e in events)
                if (e is DiceFacesChanged d && d.Source.Same(src)) r.Add(d);
            return r;
        }

        /// <summary>그 공격의 피해 내역에서 주사위 단계 값(면 바꾸기가 끝난 마지막 굴림의 합).</summary>
        static int DiceStep(List<GameEvent> events) => One<DamageBreakdown>(events).Steps[0].Value;

        /// <summary>그 공격의 피해 내역에서 출처가 src인 효과 단계의 합(없으면 0).</summary>
        static int EffectStep(List<GameEvent> events, SourceRef src)
        {
            int v = 0;
            foreach (var st in One<DamageBreakdown>(events).Steps)
                if (st.Kind == DamageStepKind.Effect && st.Source.Same(src)) v += st.Value;
            return v;
        }

        static int Worth(AttackDieFace f) => f == R ? 1 : f == C ? 2 : 0;

        static void AssertMod(PokemonMod m, ModScope scope, EffectRole role, EffectOp op, AttackDieFace face, int value, SourceRef src)
        {
            Assert.AreEqual(scope, m.Scope);
            Assert.AreEqual(role, m.Role);
            Assert.AreEqual(op, m.Op);
            Assert.AreEqual(face, m.Face);
            Assert.AreEqual(value, m.Value);
            Assert.IsTrue(m.Source.Same(src), "출처 = 건 기술");
            Assert.IsFalse(m.Fresh, "공격이 끝나면 다음 공격부터 적용된다");
        }

        List<PokemonMod> Mods(Arena t, int pokemon) => t.S.Pokemon[pokemon].Mods;

        // ── 덮개 ──

        [Test]
        public void M5_효과_지속보정갈래55장은효과표에있고남긴것은미구현목록에있다()
        {
            Assert.AreEqual(55, Moved.Length);
            Assert.AreEqual(55, new HashSet<string>(Moved).Count);
            foreach (string key in Moved) Assert.IsTrue(ctx.Effects.Implemented(Mv(key)), key);
            foreach (string key in Left) Assert.IsTrue(ctx.Effects.Implemented(Mv(key)), $"{key}: 폼 바꾸기와 함께 옮겼다(rulings A26)");
            // 지속 보정을 거는 절은 모두 실행 절(BeforeDamage)이고, 걸 것은 훑는 곳이 있는 것뿐이다
            int holds = 0;
            foreach (var e in ctx.Effects.DataEffects())
                foreach (var o in e.Ops)
                {
                    if (o.Op != EffectOp.Hold) continue;
                    Assert.AreEqual(Window.BeforeDamage, e.Window);
                    Assert.IsTrue(ModRules.CanHold(o.Held), o.Held.ToString());
                    string key = ctx.Cards.Moves[e.Source.Id].Key;
                    // 나머지 op 갈래(rulings G20)도 지속 보정을 건다: teleport·shelter(다음 공격할 때 지우기) · shell_trap(Retaliate)
                    if (Array.IndexOf(Moved, key) < 0 && Array.IndexOf(Left, key) >= 0) continue; // kings_shield: 「전설·알파·폼」이 옮겼다(LegendFormTests)
                    if (key == "barrier") continue; // M5 마감 검토 반영이 옮겼다(ReflectCrits — 결정 76 B3, OpEffectTests.G20_barrier_*)
                    if (Array.IndexOf(Moved, key) < 0)
                    {
                        Assert.Contains(key, OpEffectTests.Moved, "지속 보정을 거는 기술은 두 갈래 것뿐");
                        continue;
                    }
                    holds++;
                }
            Assert.AreEqual(59, holds, "Hold op 수(55장 가운데 eternal_glade는 ExtendMods, 두 개를 거는 기술 5장: power_plant · ominous_wind · conversion · ancient_power · imprison)");
            int moves = 0;
            foreach (var m in ctx.Cards.Moves)
                if (!ctx.Effects.Implemented(Scene.Move(ctx, m.Key))) moves++;
            Assert.AreEqual(139 - Moved.Length - OpEffectTests.Moved.Length - LegendFormTests.FormMoves.Length - HandlerEffectTests.Moved.Length - 1, moves, "(− 1 = barrier, 결정 76 B3 — M5 마감 검토 반영) 미구현 139 → 84(지속 보정 갈래) → 39(나머지 op 갈래, OpEffectTests) → 31(폼 기술 8, LegendFormTests) → 1(핸들러 갈래 30, HandlerEffectTests)");
        }

        // ── 메인 검토(11-14): 배틀 턴 끝에 건 보정의 Fresh ──

        [Test]
        public void M5_지속보정_턴끝에건보정은바로다음공격에적용된다()
        {
            // 공격이 끝난 뒤(AttackDone 뒤)의 배틀 턴 끝 단계에 건 보정은 그 공격 도중에 건 것이 아니다 — Fresh가 아니어야 바로 다음 공격에 쓰인다
            // (고치기 전: ModRules.Add의 판정이 「Step <= TurnEnd」라 Fresh로 걸려 다음 공격 한 번을 건너뛰고 그다음 공격에야 쓰였다)
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "399:0");
            int d = Scene.Give(s, ctx, 1, "266:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.Move = Mv("ember");
            var src = MoveSrc("ember");
            b.Step = BattleStep.ApplyDamage;
            Assert.IsTrue(ModRules.Add(s, d, ModScope.NextAttacked, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, 1, src).Fresh, "공격 도중에 건 것은 그 공격을 뺀다");
            b.Step = BattleStep.TurnEnd;
            ModRules.AttackDone(s, a, d);
            Assert.IsFalse(s.Pokemon[d].Mods[0].Fresh);
            var late = ModRules.Add(s, d, ModScope.NextAttacked, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, 2, src);
            Assert.IsFalse(late.Fresh, "배틀 턴 끝(AttackDone 뒤)에 건 보정은 다음 공격부터 바로 적용된다");
            var mods = new List<Mod>();
            ModRules.DamageMods(s, a, d, new[] { R }, mods);
            Assert.AreEqual(2, mods.Count, "둘 다 다음 공격에 적용");
            ModRules.AttackDone(s, a, d);
            Assert.AreEqual(0, s.Pokemon[d].Mods.Count, "한 번 쓰고 둘 다 사라진다(건너뛰어 남지 않는다)");
        }

        [Test]
        public void M5_지속보정_턴끝창의효과가건보정은상대의바로다음공격에쓰인다()
        {
            // 실제 흐름: 몸통박치기의 절을 배틀 턴 끝 창(Window.BattleTurnEnd) 처리기로 바꿔 끼운다 — 「자신이 다음에 공격받을 때 +1」을 건다 →
            // 바로 이어지는 상대의 공격에 적용되고 사라진다(턴 끝 창의 프레임은 FaintCheck 단계에서 돈다)
            var src = MoveSrc("tackle");
            EffectStepResult AtTurnEnd(GameState st, Ctx cx, Emitter fx, EffectFrame f)
            {
                ModRules.Add(st, f.User, ModScope.NextAttacked, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, 1, f.Source);
                return EffectStepResult.Done;
            }
            var handlers = new Dictionary<string, EffectHandler> { { "at_turn_end", AtTurnEnd } };
            var c = Swap(handlers, "tackle", m => new[] { new EffectDef(0, m, Window.BattleTurnEnd, EffectRole.Attacker, null, null, handler: "at_turn_end") });
            var log = new EventLog();
            var s = Battles.Game(c, 2);
            int a = Scene.Give(s, c, 0, "399:0");
            int d = Scene.Spawn(s, c, "266:0", SeatId.Wild);
            Battles.Wild(s, c, log, 0, d);
            int from = log.Events.Count;
            Battles.Answer(s, c, ChoiceKind.BattleAction, Battles.Move(Mv("tackle")), log);   // 비버니 공격 → 턴 끝 → 실쿤 반격까지
            Battles.Only(s, ChoiceKind.BattleAction);
            int applied = 0;
            foreach (var e in log.Events.GetRange(from, log.Events.Count - from))
                if (e is DamageBreakdown br && br.TargetId == a)
                    foreach (var st in br.Steps)
                        if (st.Kind == DamageStepKind.Effect && st.Source.Same(src)) applied += st.Value;
            Assert.AreEqual(1, applied, "턴 끝에 건 보정이 실쿤의 바로 다음 공격에 적용됐다");
            Assert.IsFalse(s.Pokemon[a].Mods.Exists(m => m.Source.Same(src)), "쓴 것은 사라졌다");
        }

        /// <summary>실제 효과 표에서 그 기술의 절만 바꿔 끼운 Ctx(Id는 다시 매긴다). make(출처) = 새 절들(Id는 무시하고 다시 매긴다).</summary>
        Ctx Swap(IReadOnlyDictionary<string, EffectHandler> handlers, string move, Func<SourceRef, EffectDef[]> make)
        {
            int target = Mv(move);
            var defs = new List<EffectDef>();
            bool done = false;
            void Insert()
            {
                done = true;
                foreach (var e in make(new SourceRef(SourceKind.Move, target)))
                    defs.Add(new EffectDef(EffectDb.BuiltInCount + defs.Count, e.Source, e.Window, e.Role, e.When, e.Ops, e.Limit, e.OnNoTarget, e.Handler));
            }
            foreach (var e in ctx.Effects.DataEffects())
            {
                if (e.Source.Id == target) continue;
                if (!done && e.Source.Id > target) Insert();
                defs.Add(new EffectDef(EffectDb.BuiltInCount + defs.Count, e.Source, e.Window, e.Role, e.When, e.Ops, e.Limit, e.OnNoTarget, e.Handler));
            }
            if (!done) Insert();
            return new Ctx(ctx.Board, ctx.Cards, ctx.Decks, ctx.Fixtures, ctx.Items, new EffectDb(defs, ctx.Cards.Moves.Count, handlers));
        }

        // ── 다음에 공격받을 때 · 공격할 때 ──

        // 「When the target is next attacked, add 1 regular hit to the roll.」 · 「… add 1 critical hit to the roll.」 · 「… add 2 status symbol(s) to the roll.」
        [TestCase("bite", R, 1)]
        [TestCase("crunch", R, 1)]
        [TestCase("chomp", R, 1)]
        [TestCase("iron_tail", R, 1)]
        [TestCase("charge_beam", C, 1)]
        [TestCase("peck", C, 1)]
        [TestCase("air_cutter", C, 1)]
        [TestCase("bulldoze", S, 2)]
        [TestCase("bubble_beam", S, 2)]
        public void M5_효과_대상이다음에공격받을때굴림에면을더한다(string move, AttackDieFace face, int n)
        {
            var t = Duel();
            var src = MoveSrc(move);
            var first = Turn(t, move);
            Assert.AreEqual(0, FaceChanges(first, src).Count, "건 그 공격에는 더하지 않는다(G16)");
            AssertMod(Mods(t, t.D)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.AddFaces, face, n, src);
            Assert.AreEqual(1, Mods(t, t.D).Count);
            Assert.AreEqual(0, Mods(t, t.A).Count, "대상에게만 걸린다");
            Rest(t);

            var back = Turn(t, Plain);                                      // 대상이 공격할 때는 쓰이지 않는다
            Assert.AreEqual(0, FaceChanges(back, src).Count);
            Assert.AreEqual(1, Mods(t, t.D).Count);
            Rest(t);

            var second = Turn(t, Plain, R, S);                              // 대상이 다음에 공격받는다: 굴린 2개 뒤에 n개가 붙는다
            var added = One<DiceFacesChanged>(second);
            Assert.IsTrue(added.Source.Same(src));
            Assert.AreEqual(n, added.Changes.Length);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(2 + i, added.Changes[i].Index, "더한 주사위는 굴림 끝에");
                Assert.AreEqual(X, added.Changes[i].Before);
                Assert.AreEqual(face, added.Changes[i].After);
            }
            Assert.AreEqual(1 + n * Worth(face), DiceStep(second), "더한 면도 피해에 든다");
            Assert.AreEqual(0, Mods(t, t.D).Count, "한 번 쓰고 사라진다");
        }

        // 「When the target next attacks, remove 1 regular hit from the roll.」
        [TestCase("play_rough")]
        [TestCase("wing_attack")]
        [TestCase("aurora_beam")]
        [TestCase("tackle")]
        [TestCase("water_gun")]
        public void M5_효과_대상이다음에공격할때일반타격하나를지운다(string move)
        {
            var t = Duel();
            var src = MoveSrc(move);
            Turn(t, move);
            AssertMod(Mods(t, t.D)[0], ModScope.NextAttack, EffectRole.Attacker, EffectOp.RemoveFaces, R, 1, src);
            Assert.AreEqual(0, Mods(t, t.A).Count);
            Rest(t);

            var back = Turn(t, Plain, R, C);                                // 대상의 다음 공격: 일반 타격 하나가 지워진다
            var removed = One<DiceFacesChanged>(back);
            Assert.IsTrue(removed.Source.Same(src));
            Assert.AreEqual(1, removed.Changes.Length);
            Assert.AreEqual(0, removed.Changes[0].Index);
            Assert.AreEqual(R, removed.Changes[0].Before);
            Assert.AreEqual(X, removed.Changes[0].After);
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, removed.SrcBefore);
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, removed.SrcAfter);
            Assert.AreEqual(2, DiceStep(back));
            Assert.AreEqual(0, Mods(t, t.D).Count, "한 번 쓰고 사라진다");
        }

        [Test]
        public void M5_효과_tackle_지울일반타격이없어도그공격에쓴것이다()
        {
            var t = Duel();
            var src = MoveSrc("tackle");
            Turn(t, "tackle");
            var back = Turn(t, Plain, C, S);
            var removed = One<DiceFacesChanged>(back);
            Assert.IsTrue(removed.Source.Same(src));
            Assert.AreEqual(0, removed.Changes.Length, "바꿀 면이 없었다(0 결과)");
            Assert.AreEqual(2, DiceStep(back));
            Assert.AreEqual(0, Mods(t, t.D).Count);
            // 같은 것을 두 번 걸어도 하나(룰북 FAQ p.13 「Can the effect of a move stack with itself? No」, rulings G19): 대상이 볼·교체 없이 공격하지 않은 채 두 번 맞는 일은 좌석끼리 배틀에서는 없어 직접 건다
            ModRules.Add(t.S, t.A, ModScope.NextAttack, EffectRole.Attacker, EffectOp.RemoveFaces, R, 1, src);
            ModRules.Add(t.S, t.A, ModScope.NextAttack, EffectRole.Attacker, EffectOp.RemoveFaces, R, 1, src);
            Assert.AreEqual(1, Mods(t, t.A).Count, "같은 보정은 다시 걸지 않는다");
            var twice = Turn(t, Plain, R, R);
            Assert.AreEqual(1, FaceChanges(twice, src).Count);
            Assert.AreEqual(1, DiceStep(twice));
        }

        // 룰북 FAQ p.13 「Can the effect of a move stack with itself? No, this is most notable for effects that apply until the user switches out.」(v1.3c·v1.4·v1.4a 같은 문구)
        // · 원작 커뮤니티 확인
        // · 원작 커뮤니티 확인(power_plant를 여러 번 써도)
        [Test]
        public void M5_효과_겹침_같은기술을또써도하나_문구가같은다른기술도하나_이번공격에쓰인것은새로건다()
        {
            var t = Duel();
            Turn(t, "bulk_up");
            Turn(t, Plain, S, S);
            Turn(t, "bulk_up");
            Assert.AreEqual(1, Mods(t, t.A).FindAll(m => m.Op == EffectOp.AddFaces).Count, "벌크업을 또 써도 +2 하나");
            Rest(t);
            Turn(t, Plain, S, S);
            Assert.AreEqual(2, DiceStep(Turn(t, Plain, S, S)), "더한 일반 타격 2(4가 아니다)");

            // moonlight · repairing_rock 「Until the user switches out: Heal 2 damage at the end of your turns.」 — 문구가 같으면 함께 써도 2
            var u = Duel();
            var a = u.S.Pokemon[u.A];
            a.Damage = 9;
            Turn(u, "moonlight");
            Assert.AreEqual(7, a.Damage);
            Turn(u, Plain, S, S);
            Turn(u, "repairing_rock");
            Assert.AreEqual(1, Mods(u, u.A).FindAll(m => m.Op == EffectOp.TurnEndHeal).Count);
            Assert.AreEqual(5, a.Damage, "턴 끝 회복 2 한 번");

            // 「다음에 공격받을 때」가 이번 공격에 쓰이는 중이면 새로 건 것은 따로 남는다(이번 공격이 옛것을 쓴다)
            var v = Duel();
            Turn(v, "iron_tail");
            Turn(v, Plain, S, S);
            Turn(v, "iron_tail");
            Assert.AreEqual(1, Mods(v, v.D).Count, "옛것은 이번 공격에 쓰이고 새것이 남는다");
            Assert.IsFalse(Mods(v, v.D)[0].Fresh);
        }

        // 「When the user is next attacked, remove 1 critical hit from the roll.」 · 「… remove 1 regular hit …」 · 「… remove 2 regular hits …」
        // poison_sting 「Inflict the Poisoned status on the target. When the user is next attacked, remove 2 regular hits from the roll.」
        // take_heart 「Heal any status effects. When the user is next attacked, remove 1 regular hit from the roll.」
        [TestCase("struggle_bug", C, 1)]
        [TestCase("flower_shield", C, 1)]
        [TestCase("conversion_2", R, 2)]
        [TestCase("psyshield_bash", R, 1)]
        [TestCase("steel_wing", R, 1)]
        [TestCase("poison_sting", R, 2)]
        [TestCase("take_heart", R, 1)]
        public void M5_효과_자신이다음에공격받을때굴림의면을지운다(string move, AttackDieFace face, int n)
        {
            var t = Duel();
            var src = MoveSrc(move);
            if (move == "take_heart") t.S.Pokemon[t.A].Status = StatusEffect.Burned;
            var first = Turn(t, move);
            AssertMod(Mods(t, t.A)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.RemoveFaces, face, n, src);
            Assert.AreEqual(1, Mods(t, t.A).Count);
            Assert.AreEqual(0, Mods(t, t.D).Count, "자신에게만 걸린다");
            if (move == "poison_sting")
            {
                Assert.AreEqual(StatusEffect.Poisoned, t.S.Pokemon[t.D].Status);
                Assert.AreEqual(StatusResult.Applied, One<StatusApplied>(first).Result);
            }
            if (move == "take_heart") Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status, "자신의 상태이상을 푼다");
            Rest(t);

            var back = Turn(t, Plain, face, face);                          // 상대의 공격: 그 면 n개(굴린 2개까지)가 지워진다
            var removed = One<DiceFacesChanged>(back);
            Assert.IsTrue(removed.Source.Same(src));
            Assert.AreEqual(n, removed.Changes.Length);
            foreach (var ch in removed.Changes)
            {
                Assert.AreEqual(face, ch.Before);
                Assert.AreEqual(X, ch.After);
            }
            Assert.AreEqual((2 - n) * Worth(face), DiceStep(back));
            Assert.AreEqual(0, Mods(t, t.A).Count, "한 번 쓰고 사라진다");
        }

        // 「During this attack and when the user is next attacked, status symbols deal +1 damage.」
        [TestCase("draco_meteor")]
        [TestCase("close_combat")]
        [TestCase("overheat")]
        [TestCase("leaf_storm")]
        [TestCase("headlong_rush")]
        public void M5_효과_이번공격과자신이다음에공격받을때심볼이1피해(string move)
        {
            var t = Duel();
            var src = MoveSrc(move);
            var first = Turn(t, move, S, S, R, S, R);                       // 이번 공격: 심볼 3개 = +3 (걸린 보정은 아직 없다)
            Assert.AreEqual(3, EffectStep(first, src));
            AssertMod(Mods(t, t.A)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.FaceDamage, S, 1, src);
            Assert.AreEqual(0, Mods(t, t.D).Count);
            Rest(t);

            var back = Turn(t, Plain, S, R);                                // 자신이 공격받는다: 상대 굴림의 심볼 1개 = +1
            Assert.AreEqual(1, EffectStep(back, src));
            Assert.AreEqual(0, Mods(t, t.A).Count, "한 번 쓰고 사라진다");
            Rest(t);
            Turn(t, Plain);
            Assert.AreEqual(0, EffectStep(Turn(t, Plain, S, S), src), "그다음 공격에는 없다");
        }

        // 「When the target next attacks, status symbols deal -1 damage.」 · temporal_guard 「… deal -2 damage.」
        [TestCase("dazzling_gleam", -1)]
        [TestCase("mud_bomb", -1)]
        [TestCase("flash_cannon", -1)]
        [TestCase("muddy_water", -1)]
        [TestCase("temporal_guard", -2)]
        public void M5_효과_대상이다음에공격할때심볼이피해를깎는다(string move, int each)
        {
            var t = Duel();
            var src = MoveSrc(move);
            Turn(t, move);
            AssertMod(Mods(t, t.D)[0], ModScope.NextAttack, EffectRole.Attacker, EffectOp.FaceDamage, S, each, src);
            Rest(t);
            var back = Turn(t, Plain, S, S);
            Assert.AreEqual(2 * each, EffectStep(back, src));
            Assert.AreEqual(0, One<DamageBreakdown>(back).Final, "0 아래로는 내려가지 않는다");
            Assert.AreEqual(0, Mods(t, t.D).Count);
            Turn(t, Plain);
            Assert.AreEqual(0, EffectStep(Turn(t, Plain, S, S), src), "한 번뿐");
        }

        // 「If this move was not used last turn, take at most 1 damage when the user is next attacked.」
        [Test]
        public void M5_효과_disguise_직전턴에쓰지않았으면다음에받는피해는1까지()
        {
            var t = Duel();
            var src = MoveSrc("disguise");
            Turn(t, "disguise");
            AssertMod(Mods(t, t.A)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.DamageCap, X, 1, src);
            Rest(t);
            var back = Turn(t, Plain, C, C);                                // 4 → 1
            var br = One<DamageBreakdown>(back);
            Assert.AreEqual(1, br.Final);
            var cap = Array.Find(br.Steps, st => st.Kind == DamageStepKind.Cap);
            Assert.IsNotNull(cap, "상한 단계");
            Assert.AreEqual(1, cap.Value);
            Assert.IsTrue(cap.Source.Same(src));
            Assert.AreEqual(13, Battles.Hp(t.S, ctx, t.A));
            Assert.AreEqual(0, Mods(t, t.A).Count, "한 번 쓰고 사라진다");

            Turn(t, "disguise");                                            // 직전 턴에도 썼다: 걸리지 않는다
            Assert.AreEqual(0, Mods(t, t.A).Count, "직전 턴에 쓴 기술이면 효과가 없다");
            Rest(t);
            br = One<DamageBreakdown>(Turn(t, Plain, C, C));
            Assert.AreEqual(4, br.Final);
            Assert.IsNull(Array.Find(br.Steps, st => st.Kind == DamageStepKind.Cap));

            Turn(t, Plain);                                                 // 다른 기술을 한 번 쓰면 다시 된다
            Turn(t, Plain);
            Turn(t, "disguise");
            Assert.AreEqual(1, Mods(t, t.A).Count);
            Rest(t);
            br = One<DamageBreakdown>(Turn(t, Plain, S, S));                // 1 이하의 피해는 그대로(상한 단계 없음 — 두번치기는 일반 타격마다 +1이라 심볼 둘 = 0)
            Assert.AreEqual(0, br.Final);
            Assert.IsNull(Array.Find(br.Steps, st => st.Kind == DamageStepKind.Cap));
        }

        // 「On the next attack or capture roll against the target, status symbols deal +1 damage.」
        [Test]
        public void M5_효과_sweet_scent_대상을노린다음공격의심볼이1피해()
        {
            var t = Duel();
            var src = MoveSrc("sweet_scent");
            Turn(t, "sweet_scent");
            AssertMod(Mods(t, t.D)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.FaceDamageOrCapture, S, 1, src);
            Rest(t);
            Turn(t, Plain);                                                 // 대상의 공격에는 쓰이지 않는다
            Assert.AreEqual(1, Mods(t, t.D).Count);
            var hit = Turn(t, Plain, S, S);
            Assert.AreEqual(2, EffectStep(hit, src));
            Assert.AreEqual(0, Mods(t, t.D).Count, "한 번 쓰고 사라진다");
        }

        [Test]
        public void M5_효과_sweet_scent_대상을노린다음포획굴림의심볼이1씩세어진다()
        {
            // 야생에게 달콤한향기 → 야생의 반격 → 몬스터볼(주사위 3개): 남은 심볼 하나마다 포획 합 +1, 그 굴림에 쓰고 사라진다(성공이든 실패든)
            int Throw(bool scent, string wildKey, AttackDieFace[] faces, out GameState state, out int wild)
            {
                var s = Battles.Game(ctx, 2);
                int a = Scene.Give(s, ctx, 0, "242:0");
                wild = Scene.Spawn(s, ctx, wildKey, SeatId.Wild);
                var log = new EventLog();
                Battles.Wild(s, ctx, log, 0, wild);
                if (s.Pending.Count == 1 && s.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
                int m = Mv(scent ? "sweet_scent" : Plain);
                s.Pokemon[a].Moves[0] = m;
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(m), log);
                Battles.Only(s, ChoiceKind.BattleAction);
                Assert.AreEqual(scent, s.Pokemon[wild].Mods.Exists(x => x.Op == EffectOp.FaceDamageOrCapture), "야생의 반격(공격하는 쪽)에는 쓰이지 않고 남는다");
                s.Seats[0].Balls[(int)Ball.Regular] = 1;
                s.Rng = new Pcg32(SeedFor(faces));
                int from = log.Events.Count;
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
                var attempt = One<CaptureAttempt>(log.Events.GetRange(from, log.Events.Count - from));
                CollectionAssert.AreEqual(faces, attempt.Faces);
                state = s;
                return attempt.Sum;
            }
            // 약한 야생 실쿤(포획률 3)
            Assert.AreEqual(1, Throw(false, "266:0", new[] { S, R, S }, out var plain, out _), "달콤한향기가 없으면 심볼은 0");
            Assert.IsNotNull(plain.Battle, "합 1 < 3: 실패");
            Assert.AreEqual(3, Throw(true, "266:0", new[] { S, R, S }, out var caught, out int w), "심볼 둘이 1씩");
            Assert.IsNull(caught.Battle, "합 3 = 포획률 3: 잡았다");
            Assert.GreaterOrEqual(Array.IndexOf(caught.Seats[0].Party, w), 0, "잡은 포켓몬이 파티에 있다");
            Assert.AreEqual(0, caught.Pokemon[w].Mods.Count);
            // 적당한 야생 수풀부기(포획률 4): 심볼 셋 = 3 < 4 — 실패한 굴림에도 쓴 것이다
            Assert.AreEqual(3, Throw(true, "388:0", new[] { S, S, S }, out var missed, out int w2));
            Assert.IsNotNull(missed.Battle, "실패하면 배틀이 이어진다");
            Assert.IsFalse(missed.Pokemon[w2].Mods.Exists(x => x.Op == EffectOp.FaceDamageOrCapture), "포획 굴림에 쓰고 사라진다");
        }

        // 「Heal 3 damage from the user. When the user is next attacked, status symbols deal -1 damage.」
        [Test]
        public void M5_효과_lunar_blessing_3회복하고다음에공격받을때심볼이피해를깎는다()
        {
            var t = Duel();
            var src = MoveSrc("lunar_blessing");
            t.S.Pokemon[t.A].Damage = 5;
            Turn(t, "lunar_blessing");
            Assert.AreEqual(2, t.S.Pokemon[t.A].Damage, "3 회복");
            AssertMod(Mods(t, t.A)[0], ModScope.NextAttacked, EffectRole.Defender, EffectOp.FaceDamage, S, -1, src);
            var back = Turn(t, Plain, S, C);
            Assert.AreEqual(-1, EffectStep(back, src));
            Assert.AreEqual(1, One<DamageBreakdown>(back).Final);
            Assert.AreEqual(0, Mods(t, t.A).Count);
        }

        // ── 교체할 때까지 ──

        // bulk_up 「Until the user switches out: Add 2 regular hits to your attack rolls.」 · fiery_dance 「… Add 1 critical hit to your attack rolls.」
        // ominous_wind · conversion · ancient_power 「… Add 1 critical hit and 1 status symbol to your attack rolls.」
        // power_plant 「… Heal 1 damage at the end of your turns and add 1 regular hit to your attack rolls.」
        [TestCase("bulk_up", "RR")]
        [TestCase("fiery_dance", "C")]
        [TestCase("ominous_wind", "CS")]
        [TestCase("conversion", "CS")]
        [TestCase("ancient_power", "CS")]
        [TestCase("power_plant", "R")]
        public void M5_효과_교체할때까지자신의공격굴림에면을더한다(string move, string extra)
        {
            var want = new List<AttackDieFace>();
            foreach (char ch in extra) want.Add(ch == 'R' ? R : ch == 'C' ? C : S);
            int worth = 0;
            foreach (var f in want) worth += Worth(f);
            var t = Duel();
            var src = MoveSrc(move);
            var first = Turn(t, move);
            Assert.AreEqual(0, FaceChanges(first, src).Count, "건 그 공격의 굴림에는 더하지 않는다(이미 굴렸다)");
            var adds = Mods(t, t.A).FindAll(m => m.Op == EffectOp.AddFaces);
            Assert.AreEqual(move == "bulk_up" ? 1 : want.Count, adds.Count);
            foreach (var m in adds)
            {
                Assert.AreEqual(ModScope.UntilSwitch, m.Scope);
                Assert.AreEqual(EffectRole.Attacker, m.Role);
                Assert.IsTrue(m.Source.Same(src));
                Assert.IsFalse(m.Fresh);
            }
            Assert.AreEqual(0, Mods(t, t.D).Count);
            Rest(t);

            for (int round = 0; round < 2; round++)                         // 공격마다 — 쓰여도 남는다
            {
                var back = Turn(t, Plain, R, R);
                Assert.AreEqual(0, FaceChanges(back, src).Count, "상대의 굴림에는 더하지 않는다");
                Rest(t);
                var hit = Turn(t, Plain, S, S);
                var added = new List<AttackDieFace>();
                foreach (var e in FaceChanges(hit, src))
                    foreach (var chg in e.Changes)
                    {
                        Assert.AreEqual(2 + added.Count, chg.Index);
                        Assert.AreEqual(X, chg.Before);
                        added.Add(chg.After);
                    }
                CollectionAssert.AreEqual(want, added, $"{round + 1}번째 공격");
                Assert.AreEqual(worth, DiceStep(hit));
                Rest(t);
            }
            // 나가면 사라진다(G16)
            ModRules.LeftField(t.S.Pokemon[t.A]);
            Assert.AreEqual(0, Mods(t, t.A).Count);
        }

        // moonlight · repairing_rock 「Until the user switches out: Heal 2 damage at the end of your turns.」 · power_plant 「… Heal 1 damage at the end of your turns and …」
        [TestCase("moonlight", 2)]
        [TestCase("repairing_rock", 2)]
        [TestCase("power_plant", 1)]
        public void M5_효과_교체할때까지자기턴이끝날때마다회복한다(string move, int n)
        {
            var t = Duel();
            var src = MoveSrc(move);
            var a = t.S.Pokemon[t.A];
            a.Damage = 9;
            var first = Turn(t, move);
            var heal = Mods(t, t.A).Find(m => m.Op == EffectOp.TurnEndHeal);
            Assert.AreEqual(ModScope.UntilSwitch, heal.Scope);
            Assert.AreEqual(n, heal.Value);
            Assert.IsTrue(heal.Source.Same(src));
            Assert.AreEqual(9 - n, a.Damage, "쓴 그 턴이 끝날 때부터 회복한다");
            var hp = first.FindAll(e => e is HpChanged h && h.PokemonId == t.A && h.Source.Same(src));
            Assert.AreEqual(1, hp.Count, "턴 끝 회복은 건 기술이 출처인 HpChanged");
            Assert.AreEqual(n, ((HpChanged)hp[0]).After - ((HpChanged)hp[0]).Before);

            Turn(t, Plain, S, S);                                           // 상대의 턴 끝에는 회복하지 않는다(피해 0인 굴림)
            Assert.AreEqual(9 - n, a.Damage);
            Turn(t, Plain);                                                 // 내 턴 끝마다
            Assert.AreEqual(9 - 2 * n, a.Damage);

            Turn(t, Plain, S, S);
            a.Status = StatusEffect.Poisoned;                               // 독이면 회복하지 못한다(p.9): 독 2만 받는다
            Turn(t, Plain);
            Assert.AreEqual(9 - 2 * n + 2, a.Damage);
        }

        // 「Until the user switches out: When the user is attacked, inflict the Frozen status on the attacker.」
        [Test]
        public void M5_효과_frigid_ice_교체할때까지공격받으면공격자가얼음()
        {
            var t = Duel();
            var src = MoveSrc("frigid_ice");
            var first = Turn(t, "frigid_ice");
            AssertMod(Mods(t, t.A)[0], ModScope.UntilSwitch, EffectRole.Defender, EffectOp.Inflict, X, (int)StatusEffect.Frozen, src);
            Assert.AreEqual(0, first.FindAll(e => e is StatusApplied).Count, "자신이 공격할 때는 걸지 않는다");
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status);

            var back = Turn(t, Plain);                                      // 공격받는다 → 공격자가 얼음
            var applied = One<StatusApplied>(back);
            Assert.AreEqual(t.D, applied.PokemonId);
            Assert.AreEqual(StatusEffect.Frozen, applied.Status);
            Assert.AreEqual(StatusResult.Applied, applied.Result);
            Assert.IsTrue(applied.Source.Same(src));
            Assert.AreEqual(StatusEffect.Frozen, t.S.Pokemon[t.D].Status);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status);
            Assert.AreEqual(1, Mods(t, t.A).Count, "쓰여도 남는다");

            Turn(t, Plain);
            var again = One<StatusApplied>(Turn(t, Plain));                 // 이미 얼음: 0 결과
            Assert.AreEqual(StatusResult.Already, again.Result);

            // 얼음 타입 공격자는 면역
            var ice = Duel("242:0", "361:0");
            Turn(ice, "frigid_ice", S, S, S);
            Assert.AreEqual(StatusResult.Immune, One<StatusApplied>(Turn(ice, Plain)).Result);
            Assert.AreEqual(StatusEffect.None, ice.S.Pokemon[ice.D].Status);
        }

        // 「Until the user switches out: The user is immune to all status effects.」
        [Test]
        public void M5_효과_tempered_steel_교체할때까지상태이상에걸리지않는다()
        {
            var t = Duel();
            var src = MoveSrc("tempered_steel");
            Turn(t, "tempered_steel");
            AssertMod(Mods(t, t.A)[0], ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.StatusImmune, X, 0, src);
            Assert.IsTrue(ModRules.StatusImmune(t.S.Pokemon[t.A]));
            var back = Turn(t, "twister");                                  // 「Inflict the Paralysed status on the target.」
            var applied = One<StatusApplied>(back);
            Assert.AreEqual(t.A, applied.PokemonId);
            Assert.AreEqual(StatusResult.Immune, applied.Result);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status);
            Turn(t, "tempered_steel");
            Assert.AreEqual(1, Mods(t, t.A).Count, "또 써도 겹쳐 걸리지 않는다");

            // 나가면 다시 걸린다
            ModRules.LeftField(t.S.Pokemon[t.A]);
            Assert.AreEqual(StatusResult.Applied, StatusRules.Check(ctx, t.S.Pokemon[t.A], StatusEffect.Paralysed));

            // 이미 걸린 상태이상은 면역이 풀어 준다(원작 커뮤니티 확인 — v1.4a 문구를 인용한 질문, rulings G19)
            var u = Duel();
            u.S.Pokemon[u.A].Status = StatusEffect.Burned;
            Turn(u, "tempered_steel");
            Assert.AreEqual(StatusEffect.None, u.S.Pokemon[u.A].Status);
        }

        // 「Your currently active effects that would last until the user switches out now last until the end of the battle.」
        [Test]
        public void M5_효과_eternal_glade_교체할때까지인자기효과가배틀끝까지남는다()
        {
            var t = Duel();
            var a = t.S.Pokemon[t.A];
            Turn(t, "bulk_up");
            Turn(t, "magma_storm");                                         // 상대가 건 교체·도망 금지(와 화상)
            Turn(t, "moonlight");
            Turn(t, Plain, S, S);
            Assert.AreEqual(3, Mods(t, t.A).Count);
            // 지닌물건의 기록(결단의 머리띠 꼴)도 「교체할 때까지」지만 기술 효과가 아니다
            a.Mods.Add(new PokemonMod { Scope = ModScope.UntilSwitch, Role = EffectRole.Attacker, Op = EffectOp.MoveLock, Value = Mv(Plain), Source = new SourceRef(SourceKind.Item, 0) });

            Turn(t, "eternal_glade");
            Assert.AreEqual(ModScope.BattleEnd, a.Mods.Find(m => m.Op == EffectOp.AddFaces).Scope);
            Assert.AreEqual(ModScope.BattleEnd, a.Mods.Find(m => m.Op == EffectOp.TurnEndHeal).Scope);
            Assert.AreEqual(ModScope.UntilSwitch, a.Mods.Find(m => m.Op == EffectOp.NoEscape).Scope, "상대가 건 금지는 「교체할 때까지인 내 효과」가 아니다");
            Assert.AreEqual(ModScope.UntilSwitch, a.Mods.Find(m => m.Op == EffectOp.MoveLock).Scope);
            Assert.AreEqual(0, Mods(t, t.D).Count, "상대의 효과는 바꾸지 않는다");

            Turn(t, Plain, S, S);
            Turn(t, "fiery_dance");                                         // 그 뒤에 건 것은 다시 「교체할 때까지」
            Assert.AreEqual(ModScope.UntilSwitch, a.Mods.Find(m => m.Op == EffectOp.AddFaces && m.Face == C).Scope);

            ModRules.LeftField(a);                                          // 나가도 배틀 끝까지인 둘은 남는다
            Assert.AreEqual(2, a.Mods.Count);
            Assert.IsTrue(a.Mods.TrueForAll(m => m.Scope == ModScope.BattleEnd));
            ModRules.BattleEnded(t.S, t.S.Battle);
            Assert.AreEqual(0, a.Mods.Count, "배틀이 끝나면 사라진다");
        }

        // eternal_glade로 늘어난 효과는 파티 전체에: 원작 커뮤니티 확인(다음에 나오는 포켓몬에게도)
        // · 원작 커뮤니티 확인
        [Test]
        public void M5_효과_eternal_glade_늘어난효과는쓴포켓몬이쓰러져도다음에나오는포켓몬에게간다()
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, "242:0");
            int next = Scene.Give(t.S, ctx, 0, "399:0");
            t.D = Scene.Give(t.S, ctx, 1, "799:0");
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            Battles.Answer(t.S, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(t.A), t.Log);
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Turn(t, "bulk_up");
            Turn(t, Plain, S, S);
            Turn(t, "moonlight");
            Turn(t, Plain, S, S);
            Turn(t, "eternal_glade");
            Battles.SetHp(t.S, ctx, t.A, 1);
            Turn(t, Plain, C, C);                                           // 상대가 쓰러뜨린다 → 다음 포켓몬이 나온다
            Assert.AreEqual(next, t.S.Battle.Sides[0].Active);
            Assert.AreEqual(0, Mods(t, t.A).Count, "옮겨 갔다");
            var moved = Mods(t, next);
            Assert.AreEqual(2, moved.Count);
            Assert.IsTrue(moved.TrueForAll(m => m.Scope == ModScope.BattleEnd));
            Assert.IsTrue(moved.Exists(m => m.Op == EffectOp.AddFaces && m.Source.Same(MoveSrc("bulk_up"))));
            Assert.IsTrue(moved.Exists(m => m.Op == EffectOp.TurnEndHeal && m.Source.Same(MoveSrc("moonlight"))));
        }

        // ── 교체·도망 금지 ──

        // fairy_lock 「Until the user switches out: All Pokémon can no longer switch out using move effects.」
        [Test]
        public void M5_효과_fairy_lock_교체할때까지모든포켓몬이기술효과로교체하지못한다()
        {
            var t = Duel();
            var src = MoveSrc("fairy_lock");
            Assert.IsFalse(ModRules.MoveSwitchBlocked(t.S));
            Turn(t, "fairy_lock");
            AssertMod(Mods(t, t.A)[0], ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoMoveSwitch, X, 0, src);
            Assert.AreEqual(0, Mods(t, t.D).Count);
            Assert.IsTrue(ModRules.MoveSwitchBlocked(t.S), "출전 중인 포켓몬이 걸고 있으면 양쪽 모두 막힌다");
            Assert.IsFalse(ModRules.NoEscape(t.S.Pokemon[t.D]), "기술 효과의 교체만 막는다 — 교체 창·도망은 그대로");
            Turn(t, Plain);
            Turn(t, "fairy_lock");
            Assert.AreEqual(1, Mods(t, t.A).Count, "또 써도 겹쳐 걸리지 않는다");
            ModRules.LeftField(t.S.Pokemon[t.A]);
            Assert.IsFalse(ModRules.MoveSwitchBlocked(t.S), "건 포켓몬이 나가면 풀린다");
        }

        // 「… The target may no longer switch out or flee the battle.」 앞 절: magma_storm 화상 · crush_grip 마비 · spirit_shackle·sand_tomb·whirlpool 피로 +2 · coil 피로 +3 ·
        // bug_trap·anchor_shot 「Deals 4 extra damage to Bug-type(Water-type) Pokémon.」 · imprison 「The target may no longer change form, switch out or flee the battle.」
        [TestCase("magma_storm")]
        [TestCase("crush_grip")]
        [TestCase("spirit_shackle")]
        [TestCase("sand_tomb")]
        [TestCase("whirlpool")]
        [TestCase("coil")]
        [TestCase("bug_trap")]
        [TestCase("anchor_shot")]
        [TestCase("imprison")]
        public void M5_효과_대상은교체도도망도하지못한다(string move)
        {
            var t = Duel();
            var src = MoveSrc(move);
            var d = t.S.Pokemon[t.D];
            var first = Turn(t, move);
            var lockMod = d.Mods.Find(m => m.Op == EffectOp.NoEscape);
            AssertMod(lockMod, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoEscape, X, 0, src);
            Assert.IsTrue(ModRules.NoEscape(d));
            Assert.IsFalse(ModRules.NoEscape(t.S.Pokemon[t.A]));
            Assert.AreEqual(0, Mods(t, t.A).Count);
            switch (move)
            {
                case "magma_storm": Assert.AreEqual(StatusEffect.Burned, d.Status); break;
                case "crush_grip": Assert.AreEqual(StatusEffect.Paralysed, d.Status); break;
                case "spirit_shackle":
                case "sand_tomb":
                case "whirlpool": Assert.AreEqual(2, d.Fatigue); break;
                case "coil": Assert.AreEqual(3, d.Fatigue); break;
                case "imprison":
                    Assert.IsTrue(ModRules.NoFormChange(d));
                    AssertMod(d.Mods.Find(m => m.Op == EffectOp.NoFormChange), ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoFormChange, X, 0, src);
                    break;
                default:
                    Assert.AreEqual(0, EffectStep(first, src), "그 타입이 아니면 추가 피해가 없다");
                    break;
            }
            Assert.AreEqual(move == "imprison" ? 2 : 1, d.Mods.Count);
            Rest(t);
            Turn(t, Plain);
            Turn(t, move);
            Assert.AreEqual(move == "imprison" ? 2 : 1, d.Mods.Count, "또 써도 겹쳐 걸리지 않는다");
            ModRules.LeftField(d);
            Assert.IsFalse(ModRules.NoEscape(d), "나가면(기절·배틀 끝) 사라진다");
        }

        [TestCase("bug_trap", PokemonType.Bug)]
        [TestCase("anchor_shot", PokemonType.Water)]
        public void M5_효과_그타입의대상에게는4추가피해(string move, PokemonType type)
        {
            // 좌석 0보다 느리고 체력이 넉넉한 그 타입 포켓몬
            string key = null;
            foreach (var p in ctx.Cards.Pokemon)
            {
                bool has = false;
                foreach (var ty in p.Types) has |= ty == type;
                if (has && p.Initiative < 6 && p.Health >= 5 && (key == null || p.Health > ctx.Cards.Pokemon[Scene.Def(ctx, key)].Health)) key = p.Key;
            }
            Assert.IsNotNull(key, type.ToString());
            var t = Duel("242:0", key);
            var src = MoveSrc(move);
            var first = Turn(t, move, S, S, S, S);
            Assert.AreEqual(4, EffectStep(first, src));
            Assert.IsTrue(ModRules.NoEscape(t.S.Pokemon[t.D]));
        }

        /// <summary>
        /// 트레이너전: 좌석 0 [해피너스 a, 비버니] 대 좌석 1 [악식킹 d, 실쿤]. a가 두번치기 → d가 move → a가 크리 둘로 체력 1의 d를 쓰러뜨린다.
        /// 반환 = 그 마지막 공격의 이벤트(쓰러뜨린 뒤 교체 창까지).
        /// </summary>
        List<GameEvent> KoAfter(string move, out GameState s, out int a)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, "242:0");
            Scene.Give(t.S, ctx, 0, "399:0");
            t.D = Scene.Give(t.S, ctx, 1, "799:0");
            Scene.Give(t.S, ctx, 1, "266:0");
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            Battles.Answer(t.S, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(t.A), t.Log);
            Battles.Answer(t.S, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(t.D), t.Log);
            Turn(t, Plain, S, S);
            Turn(t, move);
            Battles.SetHp(t.S, ctx, t.D, 1);
            s = t.S;
            a = t.A;
            // 굴림을 정한 답(Turn은 다음 선택이 기술 고르기라고 보지 않는다)
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            t.S.Pokemon[t.A].Moves[0] = Mv(Plain);
            t.S.Rng = new Pcg32(SeedFor(new[] { C, C }));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(Mv(Plain)), t.Log);
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        [Test]
        public void M5_효과_교체금지_상대를쓰러뜨려도교체창이열리지않는다()
        {
            // 걸리지 않았으면: 쓰러뜨린 쪽(좌석 0)에 교체 창
            var free = KoAfter("ember", out var s0, out _);
            Assert.AreEqual(1, free.FindAll(e => e is Fainted).Count);
            Assert.AreEqual(0, Battles.Only(s0, ChoiceKind.SwitchWindow).Seat);
            Assert.AreEqual(0, free.FindAll(e => e is SwitchLocked).Count);

            // 마그마스톰 「The target may no longer switch out or flee the battle」에 걸린 포켓몬은 교체 창이 없다
            var held = KoAfter("magma_storm", out var s1, out int a);
            Assert.AreEqual(1, held.FindAll(e => e is Fainted).Count);
            var locked = One<SwitchLocked>(held);
            Assert.AreEqual(SwitchLockReason.NoEscape, locked.Reason);
            Assert.AreEqual(0, locked.Side);
            Assert.IsFalse(s1.Pending.Exists(c => c.Kind == ChoiceKind.SwitchWindow));
            Assert.AreEqual(a, s1.Battle.Sides[0].Active, "그대로 싸운다");
            Assert.IsTrue(ModRules.NoEscape(s1.Pokemon[a]));
        }

        [Test]
        public void M5_효과_도망금지_걸린야생은내포켓몬을쓰러뜨려도도망치지못한다()
        {
            // 좌석 0 [비버니(체력 1로 둔다), 모부기] 대 야생 실쿤(약한 야생 — 내 포켓몬 1마리를 쓰러뜨리면 도망친다, p.8).
            // 비버니가 기술을 쓰고 실쿤의 반격에 쓰러진다: 바다회오리에 걸린 실쿤은 도망치지 못해 배틀이 이어지고, 내 포켓몬이 바닥나면 도망이 아니라 야생의 승리다(빙결과 같다)
            BattleResult? After(string move, bool second, out bool battleOn)
            {
                GameState Setup(out int a, out int wild, EventLog log)
                {
                    var s = Battles.Game(ctx, 2);
                    a = Scene.Give(s, ctx, 0, "399:0");
                    if (second) Scene.Give(s, ctx, 0, "387:0");
                    wild = Scene.Spawn(s, ctx, "266:0", SeatId.Wild);
                    Battles.Wild(s, ctx, log, 0, wild);
                    if (second) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(a), log);
                    Battles.SetHp(s, ctx, a, 1);
                    s.Pokemon[a].Moves[0] = Mv(move);
                    return s;
                }
                // 실쿤의 반격이 비버니를 쓰러뜨리고 실쿤은 살아남는 난수 시드
                ulong seed = Battles.Seed(x =>
                {
                    var log = new EventLog();
                    var s = Setup(out int a, out int wild, log);
                    s.Rng = new Pcg32(x);
                    Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv(move)), log);
                    return log.Events.Exists(e => e is Fainted f && f.PokemonId == a) && !log.Events.Exists(e => e is Fainted f && f.PokemonId == wild);
                });
                var real = new EventLog();
                var st = Setup(out _, out _, real);
                st.Rng = new Pcg32(seed);
                Battles.Answer(st, ctx, ChoiceKind.BattleAction, Battles.Move(Mv(move)), real);
                battleOn = st.Battle != null;
                var end = real.Events.Find(e => e is BattleEnded) as BattleEnded;
                return end?.Result;
            }
            Assert.AreEqual(BattleResult.Fled, After(Plain, true, out bool on), "걸리지 않은 약한 야생은 한 마리를 쓰러뜨리면 도망친다");
            Assert.IsFalse(on);
            Assert.IsNull(After("whirlpool", true, out on), "걸린 야생은 도망치지 못한다");
            Assert.IsTrue(on, "배틀이 이어진다(다음 포켓몬)");
            Assert.AreEqual(BattleResult.Win, After("whirlpool", false, out on), "내 포켓몬이 바닥나면 도망이 아니라 야생의 승리");
            Assert.IsFalse(on);
        }

        // ── 굴림의 순서(rulings G14 — 원작 커뮤니티 확인 · 결정 72·73) ──

        [Test]
        public void G14_마비는벌크업이더한일반타격도지운다()
        {
            // 벌크업(교체할 때까지 굴림에 일반 타격 2개)을 건 포켓몬이 마비된 바로 다음 공격: 더한 면이 먼저 붙고 마비가 굴림의 일반 타격을 모두 지운다 — 순서를 묻지 않는다
            var t = Duel();
            var bulk = MoveSrc("bulk_up");
            Turn(t, "bulk_up");
            Turn(t, Plain, S, S);
            Assert.AreEqual(StatusResult.Applied, StatusRules.Inflict(ctx, t.S.Pokemon[t.A], StatusEffect.Paralysed));
            var hit = Turn(t, Plain, R, C);
            Battles.Only(t.S, ChoiceKind.BattleAction);
            var changes = hit.FindAll(e => e is DiceFacesChanged);
            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes[0].Source.Same(bulk), "더하기가 먼저");
            Assert.AreEqual(SourceKind.Status, changes[1].Source.Kind);
            var gone = new List<int>();
            foreach (var ch in ((DiceFacesChanged)changes[1]).Changes) gone.Add(ch.Index);
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, gone, "굴린 일반 타격과 더한 둘을 모두 지운다");
            Assert.AreEqual(2, DiceStep(hit), "크리티컬 하나만 남는다");
            Assert.AreEqual(0, hit.FindAll(e => e is ChoiceRequested r && r.Sub == ChoiceSub.EffectOrder).Count);
        }

        [Test]
        public void M5_결정72_지우기보정과졸림의순서로피해가갈리면공격자가고른다()
        {
            // 몸통박치기에 맞은 악식킹(다음 공격할 때 일반 타격 1개 지움)이 졸린 해피너스를 친다, 굴림 [일반, 심볼]:
            // 졸림 먼저(기본) = 일반 → 크리, 지울 일반이 없다 → 피해 2 / 지우기 먼저 = 일반이 사라져 졸림이 바꿀 것이 없다 → 피해 0
            var t = Duel();
            var tackle = MoveSrc("tackle");
            Turn(t, "tackle");
            t.S.Pokemon[t.A].Status = StatusEffect.Drowsy;
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            t.S.Pokemon[t.D].Moves[0] = Mv(Plain);
            t.S.Rng = new Pcg32(SeedFor(new[] { R, S }));
            Battles.Answer(t.S, ctx, ch, Battles.Move(Mv(Plain)), t.Log);
            var order = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            Assert.AreEqual(1, order.Seat, "공격하는 좌석에게 묻는다");
            var legal = Scene.Legal(t.S, ctx, order);
            Assert.AreEqual(2, legal.Count);
            CollectionAssert.AreEqual(new[] { 2, EffectDb.ModFacesAttacker }, legal[0].Values, "첫 답 = 기본 순서(졸림 먼저, 지속 보정은 그 뒤)");
            var keep = EffectRules.Preview(t.S, ctx, legal[0].Values);
            var spare = EffectRules.Preview(t.S, ctx, legal[1].Values);
            CollectionAssert.AreEqual(new[] { C, S }, keep.Faces);
            Assert.AreEqual(2, keep.Damage);
            CollectionAssert.AreEqual(new[] { X, S }, spare.Faces);
            Assert.AreEqual(0, spare.Damage);
            Assert.IsTrue(EffectRules.OrderSource(t.S, ctx, EffectDb.ModFacesAttacker).Same(tackle), "순서 줄의 이름 = 건 기술");
            Assert.AreEqual(SourceKind.Status, EffectRules.OrderSource(t.S, ctx, 2).Kind);

            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, order, legal[1], t.Log);                // 일부러 약하게
            var after = t.Log.Events.GetRange(from, t.Log.Events.Count - from);
            Assert.AreEqual(0, One<DamageBreakdown>(after).Final);
            Assert.AreEqual(1, FaceChanges(after, tackle)[0].Changes.Length);
            Assert.AreEqual(0, Mods(t, t.D).Count, "쓴 보정은 사라진다");
        }

        [Test]
        public void M5_결정73_지우기보정이있어도피해가같으면묻지않는다()
        {
            // 같은 장면에서 굴림에 일반 타격이 둘이면 어느 순서든 크리 하나만 남아(졸림 1개 → 크리, 지우기 1개) 피해가 같다 → 묻지 않는다
            var t = Duel();
            Turn(t, "tackle");
            t.S.Pokemon[t.A].Status = StatusEffect.Drowsy;
            var hit = Turn(t, Plain, R, R);
            Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(0, hit.FindAll(e => e is ChoiceRequested r && r.Sub == ChoiceSub.EffectOrder).Count);
            Assert.AreEqual(2, DiceStep(hit));
        }
    }
}
