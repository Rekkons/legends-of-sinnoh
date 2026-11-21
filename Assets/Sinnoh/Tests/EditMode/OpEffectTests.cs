using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「기술 효과 — 나머지 op 갈래」(01 §7-6, rulings G20): 상태 둘·주사위·임의 · 피로 · 조건부 추가·타 포켓몬·즉사 · 규토리 · 타입 선택 · 교체 기술 · 피해 이월·반사의 45장.
    /// 효과의 근거는 xlsx 영문 문구다(기술 키가 TestCase·시험 이름에 있다 — <see cref="Moved"/>가 목록, 남긴 것은 <see cref="Left"/>). 실제 배틀 흐름(트레이너전)으로 돌고,
    /// 굴림은 그 면이 나오는 난수 시드를 찾아 정한다(<see cref="SeedFor"/>). 포켓몬: 해피너스 242(노말, 체력 14, 우선도 6) 대 악식킹 799(악/드래곤, 23, 5) — 좌석 0이 먼저다.
    /// </summary>
    public class OpEffectTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        /// <summary>남는 상태가 없는 공격력 2(두번치기 「Regular hits deal +1 damage」 — 전광석화는 핸들러 갈래가 둘째 기술로 옮겼다, rulings G21).</summary>
        const string Plain = "double_hit";

        /// <summary>이 묶음이 옮긴 기술 45장(01 §7-6 갈래 수 55장 가운데 — 남긴 것은 <see cref="Left"/>).</summary>
        public static readonly string[] Moved =
        {
            // 피로 3
            "memento", "perish_song", "calm_mind",
            // 조건부 추가·타 포켓몬·즉사 11
            "attack_order", "wild_charge", "lava_plume", "shocking_verity", "blazing_valor", "chilling_acuity", "earthquake", "prismatic_laser", "shadow_force", "fissure", "stinging_punch",
            // 상태 둘·주사위·임의 11
            "infatuate", "topsy_turvy", "dark_void", "ion_deluge", "fire_lash", "avalanche", "neurotoxin", "splash", "tri_attack", "power_gem", "photon_geyser",
            // 규토리 2 · 타입 선택 4
            "bug_bite", "belch", "hidden_power", "hidden_punch", "multi_attack", "judgement",
            // 교체 기술 7
            "u_turn", "volt_switch", "flip_turn", "teleport", "shelter", "lunge", "acrobatics",
            // 피해 이월·반사 7
            "renegade_fury", "overcut", "outrage", "raging_fury", "petal_dance", "thrash", "shell_trap",
        };

        /// <summary>
        /// 그 갈래에서 남긴 기술(rulings G20 「남긴 것」 — 폼 바꾸기가 문구에 있는 hangry_wheel · adaptive_cloak은 「전설·알파·폼」이 옮겼다, LegendFormTests) · barrier는 결정 76 B3 답(가·가)으로 M5 마감 검토 반영이 옮겼다(rulings G20 ⑷) — 남은 것 없음.
        /// 같은 배틀 턴의 둘째 공격(「The user may use a second move this turn」 6장과 encore)은 핸들러 갈래가 옮겼다(rulings G21, HandlerEffectTests).
        /// </summary>
        public static readonly string[] Left =
        {
        };

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
            /// <summary>좌석 0의 출전 포켓몬(먼저 움직인다) · 좌석 1의 출전 포켓몬 · 좌석 1의 나머지 · 좌석 0의 나머지.</summary>
            public int A, D;
            public List<int> DBench = new List<int>(), ABench = new List<int>();
        }

        /// <summary>트레이너전: 좌석 0(a + aBench) 대 좌석 1(d + dBench). 출전은 각 파티 첫째. 좌석 0의 기술 고르기가 열려 있다.</summary>
        Arena Duel(string a = "242:0", string d = "799:0", string[] dBench = null, string[] aBench = null)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, a);
            foreach (var k in aBench ?? new string[0]) t.ABench.Add(Scene.Give(t.S, ctx, 0, k));
            t.D = Scene.Give(t.S, ctx, 1, d);
            foreach (var k in dBench ?? new string[0]) t.DBench.Add(Scene.Give(t.S, ctx, 1, k));
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            for (int guard = 0; guard < 6 && t.S.Pending.Count == 1; guard++)
            {
                var c = t.S.Pending[0];
                if (c.Kind == ChoiceKind.SendIn) Battles.Answer(t.S, ctx, c, ChoiceAnswer.Pick(c.Seat == 0 ? t.A : t.D), t.Log);
                else if (c.Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
                else break;
            }
            // 좌석 1이 더 빠르면(시험이 고른 종) 효과 없는 기술로 한 번 넘긴다 — 시험은 늘 좌석 0의 차례에서 시작한다
            if (Battles.Only(t.S, ChoiceKind.BattleAction).Seat == 1) Turn(t, Plain);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0의 차례");
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
        /// 차례인 좌석의 출전 포켓몬이 그 기술을 쓴다(배운 기술 칸 0). faces를 주면 그 굴림이 나온다. keepFatigue가 아니면 굴리기 전에 출전 둘의 피로를 0으로.
        /// 반환 = 그 답의 이벤트(다음 선택이 열릴 때까지).
        /// </summary>
        List<GameEvent> Turn(Arena t, string move, AttackDieFace[] faces = null, bool keepFatigue = false)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var b = t.S.Battle;
            int active = b.Sides[ChoiceRules.BattleSideOf(t.S, ch.Seat)].Active;
            int m = Mv(move);
            t.S.Pokemon[active].Moves[0] = m;
            if (!keepFatigue)
            {
                t.S.Pokemon[b.Sides[0].Active].Fatigue = 0;
                t.S.Pokemon[b.Sides[1].Active].Fatigue = 0;
            }
            if (faces != null) t.S.Rng = new Pcg32(SeedFor(faces));
            return Answer(t, ch, Battles.Move(m));
        }

        List<GameEvent> Answer(Arena t, PendingChoice ch, ChoiceAnswer a)
        {
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, a, t.Log);
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        List<GameEvent> Answer(Arena t, ChoiceAnswer a) => Answer(t, t.S.Pending[0], a);

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = events.FindAll(e => e is T);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return (T)l[0];
        }

        static List<T> All<T>(List<GameEvent> events, Func<T, bool> ok = null) where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in events)
                if (e is T x && (ok == null || ok(x))) r.Add(x);
            return r;
        }

        /// <summary>그 공격의 피해 내역에서 출처가 src인 효과 단계의 합(없으면 0).</summary>
        static int EffectStep(List<GameEvent> events, SourceRef src)
        {
            int v = 0;
            foreach (var st in One<DamageBreakdown>(events).Steps)
                if (st.Kind == DamageStepKind.Effect && st.Source.Same(src)) v += st.Value;
            return v;
        }

        static AttackDieFace[] F(params AttackDieFace[] f) => f;

        /// <summary>그 타입을 가진 포켓몬 키(포켓몬 표 순서로 첫째 — 시험이 종을 못 박지 않게). without = 갖지 않을 타입.</summary>
        string Of(PokemonType type, PokemonType without = PokemonType.Typeless)
        {
            var p = ctx.Cards.Pokemon;
            for (int i = 0; i < p.Count; i++)
            {
                if (p[i].Legendary) continue;
                bool has = false, bad = false;
                foreach (var x in p[i].Types)
                {
                    if (x == type) has = true;
                    if (without != PokemonType.Typeless && x == without) bad = true;
                }
                if (has && !bad) return ctx.Cards.PokemonIds[i];
            }
            Assert.Fail($"{type} 포켓몬이 없다");
            return null;
        }

        /// <summary>그 타입의 기술 키(기술 표 순서로 첫째, 효과 없는 것 우선이 아니어도 된다 — 배운 칸에 두기만 한다).</summary>
        string MoveOf(PokemonType type, string except)
        {
            foreach (var m in ctx.Cards.Moves)
                if (m.Type == type && m.Key != except) return m.Key;
            Assert.Fail($"{type} 기술이 없다");
            return null;
        }

        // ── 덮개 ──

        [Test]
        public void M5_효과_나머지op갈래45장은효과표에있고남긴것은미구현목록에있다()
        {
            Assert.AreEqual(45, Moved.Length);
            Assert.AreEqual(45, new HashSet<string>(Moved).Count);
            foreach (string key in Moved) Assert.IsTrue(ctx.Effects.Implemented(Mv(key)), key);
            foreach (string key in Left) Assert.IsFalse(ctx.Effects.Implemented(Mv(key)), $"{key}: 문구를 다 옮길 수 없어 넣지 않는다(G14 반쪽 효과 없음)");
            var todo = new List<string>();
            foreach (var m in ctx.Cards.Moves)
                if (!ctx.Effects.Implemented(Mv(m.Key))) todo.Add(m.Key);
            Assert.AreEqual(0, todo.Count, "미구현 84 → 39 → 31(「전설·알파·폼」 폼 기술 8장, rulings A26) → 1(핸들러 갈래 30장, rulings G21) → 0(barrier — 결정 76 B3, rulings G20 ⑷)");
            foreach (string key in Left) CollectionAssert.Contains(todo, key);
        }

        // ── 피로 ──

        [TestCase("memento", false)]
        [TestCase("perish_song", true)]
        public void M5_효과_피로를5로(string move, bool user)
        {
            var t = Duel();
            var ev = Turn(t, move);
            Assert.AreEqual(5, t.S.Pokemon[t.D].Fatigue, "대상 피로 5");
            Assert.AreEqual(user ? 5 : 1, t.S.Pokemon[t.A].Fatigue, user ? "자신도 5" : "자신은 굴림 피로 1뿐");
            Assert.IsTrue(All<FatigueChanged>(ev, f => f.Cause == FatigueCause.Effect && f.After == 5).Count == (user ? 2 : 1));
        }

        [Test]
        public void M5_효과_calm_mind_지금피로만큼추가피해하고0으로()
        {
            var t = Duel();
            var src = MoveSrc("calm_mind");
            // 피로 0 → 굴림으로 1 → 추가 피해 1(「current battle fatigue」 — 굴림으로 오른 것 포함), 뒤에 0
            var ev = Turn(t, "calm_mind", F(R, R));
            Assert.AreEqual(1, EffectStep(ev, src));
            Assert.AreEqual(0, t.S.Pokemon[t.A].Fatigue);
            Turn(t, Plain);
            t.S.Pokemon[t.A].Fatigue = 3;
            ev = Turn(t, "calm_mind", F(R, R), keepFatigue: true);
            Assert.AreEqual(4, EffectStep(ev, src), "피로 3 → 굴림 4 → 추가 피해 4");
            Assert.AreEqual(0, t.S.Pokemon[t.A].Fatigue);
        }

        // ── 조건부 추가 ──

        [Test]
        public void M5_효과_attack_order_파티에기절하지않은다른벌레가있으면2()
        {
            string bug = Of(PokemonType.Bug);
            var src = MoveSrc("attack_order");
            var t = Duel(bug, aBench: new[] { "242:0" });
            Assert.AreEqual(0, EffectStep(Turn(t, "attack_order", F(R, R, R, R)), src), "자신이 벌레여도 「another」가 아니다");
            Turn(t, Plain);
            var t2 = Duel("242:0", aBench: new[] { bug });
            Assert.AreEqual(2, EffectStep(Turn(t2, "attack_order", F(R, R, R, R)), src));
            Turn(t2, Plain);
            Battles.SetHp(t2.S, ctx, t2.ABench[0], 0);
            Assert.AreEqual(0, EffectStep(Turn(t2, "attack_order", F(R, R, R, R)), src), "기절한 벌레는 세지 않는다");
        }

        [TestCase("wild_charge", PokemonType.Electric)]
        [TestCase("lava_plume", PokemonType.Fire)]
        public void M5_효과_다른같은타입기술을알면2(string move, PokemonType type)
        {
            var src = MoveSrc(move);
            var t = Duel();
            Assert.AreEqual(0, EffectStep(Turn(t, move, F(R, R, R, R)), src), "그 기술 하나만 안다");
            Turn(t, Plain);
            t.S.Pokemon[t.A].Moves[1] = Mv(MoveOf(type, move));
            Assert.AreEqual(2, EffectStep(Turn(t, move, F(R, R, R, R)), src), "배운 칸에 다른 같은 타입 기술");
        }

        // ── 타 포켓몬 ──

        [TestCase("shocking_verity", StatusEffect.Paralysed)]
        [TestCase("blazing_valor", StatusEffect.Burned)]
        [TestCase("chilling_acuity", StatusEffect.Frozen)]
        public void M5_효과_심볼이면대상과대상파티의다른포켓몬하나에(string move, StatusEffect status)
        {
            var src = MoveSrc(move);
            var t = Duel(dBench: new[] { "242:0", "242:0" });
            var faces = F(S, R, R, R);
            var ev = Turn(t, move, faces);
            // 다른 포켓몬이 둘 — 효과 주인(좌석 0)이 고른다(Reaction/EffectPick, 대상 파티 순서)
            var pick = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, pick.Seat);
            Assert.IsTrue(pick.Source.Same(src));
            CollectionAssert.AreEqual(t.DBench, pick.Options);
            var legal = Scene.Legal(t.S, ctx, pick);
            Assert.AreEqual(2, legal.Count);
            Assert.IsTrue(legal[0].SameAs(ChoiceAnswer.Pick(t.DBench[0])) && legal[1].SameAs(ChoiceAnswer.Pick(t.DBench[1])), "안 고르기는 없다");
            Assert.AreEqual(status, t.S.Pokemon[t.D].Status, "대상은 먼저 걸렸다");
            var copy = t.S.Clone();
            ev = Answer(t, ChoiceAnswer.Pick(t.DBench[1]));
            Assert.AreEqual(status, t.S.Pokemon[t.DBench[1]].Status);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.DBench[0]].Status);
            Assert.AreEqual(1, All<StatusApplied>(ev, x => x.PokemonId == t.DBench[1] && x.Source.Same(src)).Count);
            // 복제본도 같은 자리에서 이어 간다
            Battles.Answer(copy, ctx, copy.Pending[0], ChoiceAnswer.Pick(t.DBench[1]), new EventLog());
            Assert.AreEqual(StateHash.Of(t.S), StateHash.Of(copy));

            // 심볼이 없으면 아무 일도 없다
            var t2 = Duel(dBench: new[] { "242:0" });
            Turn(t2, move, F(R, R, R, R));
            Assert.AreEqual(StatusEffect.None, t2.S.Pokemon[t2.D].Status);
            Assert.AreEqual(StatusEffect.None, t2.S.Pokemon[t2.DBench[0]].Status);
        }

        [Test]
        public void M5_효과_blazing_valor_대상이면역이어도다른포켓몬에는건다_하나뿐이면묻지않는다()
        {
            // 원작 커뮤니티 확인
            var t = Duel(d: Of(PokemonType.Fire), dBench: new[] { "242:0" });
            Turn(t, "blazing_valor", F(S, R, R, R));
            Assert.AreEqual(ChoiceKind.BattleAction, t.S.Pending[0].Kind, "다른 포켓몬이 하나면 묻지 않는다");
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status, "불꽃은 화상 면역");
            Assert.AreEqual(StatusEffect.Burned, t.S.Pokemon[t.DBench[0]].Status);
            // 야생(대상 파티가 한 마리)이면 대상에게만
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "242:0");
            int w = Scene.Spawn(s, ctx, "799:0", SeatId.Wild);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, w);
            s.Pokemon[s.Battle.Sides[0].Active].Moves[0] = Mv("blazing_valor");
            s.Rng = new Pcg32(SeedFor(F(S, R, R, R)));
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("blazing_valor")), log);
            Assert.AreEqual(StatusEffect.Burned, s.Pokemon[w].Status);
        }

        [Test]
        public void M5_효과_earthquake_심볼둘이면대상이아닌하나에2_벤치가쓰러지면기절()
        {
            var src = MoveSrc("earthquake");
            var t = Duel(dBench: new[] { "242:0", "242:0" });
            Turn(t, "earthquake", F(S, R, R, R, R));
            Assert.AreEqual(ChoiceKind.BattleAction, t.S.Pending[0].Kind, "심볼 하나면 없다");
            Assert.AreEqual(0, t.S.Pokemon[t.DBench[0]].Damage + t.S.Pokemon[t.DBench[1]].Damage);
            Turn(t, Plain);
            Battles.SetHp(t.S, ctx, t.DBench[0], 2);
            Turn(t, "earthquake", F(S, S, R, R, R));
            Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            var ev = Answer(t, ChoiceAnswer.Pick(t.DBench[0]));
            Assert.AreEqual(0, Battles.Hp(t.S, ctx, t.DBench[0]));
            var hp = One<HpChanged>(All<GameEvent>(ev, e => e is HpChanged h && h.PokemonId == t.DBench[0]));
            Assert.IsTrue(hp.Source.Same(src));
            var faint = One<Fainted>(ev);
            Assert.AreEqual(t.DBench[0], faint.PokemonId);
            Assert.AreEqual(1, faint.Side);
            Assert.IsTrue(t.S.Battle.Sides[1].FaintedThisBattle, "벤치 기절도 이번 배틀 기절(기권 가능)");
            Assert.AreEqual(t.D, t.S.Battle.Sides[1].Active, "출전 포켓몬은 그대로 — 교체 없음");
        }

        [Test]
        public void M5_효과_prismatic_laser_심볼둘이면대상이아닌모두에2()
        {
            var t = Duel(dBench: new[] { "242:0", "242:0" });
            Turn(t, "prismatic_laser", F(S, S, R, R, R, R, R));
            Assert.AreEqual(2, t.S.Pokemon[t.DBench[0]].Damage);
            Assert.AreEqual(2, t.S.Pokemon[t.DBench[1]].Damage);
        }

        // ── 즉사 ──

        [TestCase("shadow_force")]
        [TestCase("fissure")]
        public void M5_효과_공격뒤체력2이하면기절_심볼은1덜(string move)
        {
            var src = MoveSrc(move);
            var faces = F(S, S, R, R, R);
            // 피해 한 번을 재 둔다(상성은 종에 따라 — 최종 피해만 본다)
            var t = Duel();
            var ev = Turn(t, move, faces);
            Assert.AreEqual(-2, EffectStep(ev, src), "심볼 둘 → −2");
            int dmg = One<DamageBreakdown>(ev).Final;
            // 공격 뒤 체력 2 → 기절
            var t2 = Duel();
            Battles.SetHp(t2.S, ctx, t2.D, dmg + 2);
            ev = Turn(t2, move, faces);
            Assert.AreEqual(1, All<Fainted>(ev, x => x.PokemonId == t2.D).Count, "2 이하로 남으면 기절");
            Assert.AreEqual(1, All<HpChanged>(ev, x => x.PokemonId == t2.D && x.Source.Same(src) && x.After == 0 && x.Before == 2).Count, "남은 2를 더 깎는다(동시에)");
            // 공격 뒤 체력 3 → 남는다
            var t3 = Duel();
            Battles.SetHp(t3.S, ctx, t3.D, dmg + 3);
            Turn(t3, move, faces);
            Assert.AreEqual(3, Battles.Hp(t3.S, ctx, t3.D));
        }

        [Test]
        public void M5_효과_stinging_punch_크리마다1더하고2회복()
        {
            var src = MoveSrc("stinging_punch");
            var t = Duel();
            t.S.Pokemon[t.A].Damage = 8;
            var ev = Turn(t, "stinging_punch", F(C, C, R, S));
            Assert.AreEqual(2, EffectStep(ev, src));
            Assert.AreEqual(4, t.S.Pokemon[t.A].Damage, "크리 둘 → 4 회복");
        }

        // ── 상태 둘 ──

        [TestCase("infatuate", StatusEffect.Paralysed, StatusEffect.Confused)]
        [TestCase("topsy_turvy", StatusEffect.Drowsy, StatusEffect.Confused)]
        [TestCase("dark_void", StatusEffect.Drowsy, StatusEffect.Paralysed)]
        [TestCase("ion_deluge", StatusEffect.Paralysed, StatusEffect.Poisoned)]
        [TestCase("fire_lash", StatusEffect.Burned, StatusEffect.Poisoned)]
        [TestCase("avalanche", StatusEffect.Frozen, StatusEffect.Paralysed)]
        [TestCase("neurotoxin", StatusEffect.Poisoned, StatusEffect.Drowsy)]
        public void S7_6_동시에2개_이미걸려있으면둘다안걸린다(string move, StatusEffect a, StatusEffect b)
        {
            var src = MoveSrc(move);
            // 해피너스(노말)에게: 둘 다 걸린다 — 좌석 1이 쓰게 좌석 0은 몸통박치기
            var t = Duel("242:0", "242:0");
            Turn(t, Plain);
            t.S.Pokemon[t.D].Moves[0] = Mv(move);
            var ev = Answer(t, Battles.Move(Mv(move)));
            var p = t.S.Pokemon[t.A];
            Assert.IsTrue(StatusRules.Has(p, a) && StatusRules.Has(p, b), $"{p.Status}/{p.Status2}");
            Assert.AreEqual(2, All<StatusApplied>(ev, x => x.Source.Same(src) && x.Result == StatusResult.Applied).Count);

            // 이미 상태이상이 있으면 둘 다 Already(p.9 — 원작 커뮤니티 확인)
            var t2 = Duel("242:0", "242:0");
            Turn(t2, Plain);
            var other = a == StatusEffect.Confused || b == StatusEffect.Confused ? StatusEffect.Burned : StatusEffect.Confused;
            if (other == a || other == b) other = StatusEffect.Drowsy;
            t2.S.Pokemon[t2.A].Status = other;
            t2.S.Pokemon[t2.D].Moves[0] = Mv(move);
            ev = Answer(t2, Battles.Move(Mv(move)));
            Assert.AreEqual(other, t2.S.Pokemon[t2.A].Status);
            Assert.AreEqual(StatusEffect.None, t2.S.Pokemon[t2.A].Status2);
            Assert.AreEqual(2, All<StatusApplied>(ev, x => x.Source.Same(src) && x.Result == StatusResult.Already).Count);
        }

        [Test]
        public void S7_6_동시에2개_한쪽면역이면나머지만_둘다있으면둘다작동하고풀면둘다()
        {
            // ion_deluge(마비 + 독)를 전기 타입에게: 마비는 면역, 독만 걸린다 [가정 G20 — 상태마다 면역을 따로 본다]
            string elec = Of(PokemonType.Electric, PokemonType.Poison);
            var t = Duel(elec, "242:0");
            Turn(t, Plain);
            t.S.Pokemon[t.D].Moves[0] = Mv("ion_deluge");
            Answer(t, Battles.Move(Mv("ion_deluge")));
            Assert.AreEqual(StatusEffect.Poisoned, t.S.Pokemon[t.A].Status);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status2);

            // 화상 + 독: 공격할 때 총 데미지 −2(화상) · 자기 턴 끝 2 피해(독) · 회복 못 함, 우선도는 그대로
            var p = new PokemonState { Def = Scene.Def(ctx, "242:0"), Status = StatusEffect.Burned, Status2 = StatusEffect.Poisoned };
            Assert.IsTrue(StatusRules.BlocksHealing(p));
            Assert.IsFalse(StatusRules.BlocksEscape(p));
            var q = new PokemonState { Def = Scene.Def(ctx, "242:0"), Status = StatusEffect.Paralysed, Status2 = StatusEffect.Frozen };
            Assert.IsTrue(StatusRules.BlocksEscape(q), "둘째 칸의 빙결도 교체·도망을 막는다");
            StatusRules.Remove(q, StatusEffect.Paralysed);
            Assert.AreEqual(StatusEffect.Frozen, q.Status, "하나를 풀면 둘째가 첫 칸으로");
            Assert.AreEqual(StatusEffect.None, q.Status2);
            StatusRules.Clear(p);
            Assert.AreEqual(StatusEffect.None, p.Status2, "다 풀면 둘째 칸도");

            // 실제 배틀: fire_lash에 걸린 해피너스가 공격하면 −2 감소와 턴 끝 독 2를 둘 다 받는다
            var t2 = Duel("242:0", "799:0");
            t2.S.Pokemon[t2.A].Status = StatusEffect.Burned;
            t2.S.Pokemon[t2.A].Status2 = StatusEffect.Poisoned;
            var ev = Turn(t2, Plain, F(R, R));
            var br = One<DamageBreakdown>(ev);
            Assert.IsTrue(Array.Exists(br.Steps, x => x.Kind == DamageStepKind.Reduction && x.Value == -2), "화상 −2");
            Assert.IsTrue(All<StatusTicked>(ev, x => x.PokemonId == t2.A && x.Status == StatusEffect.Poisoned).Count == 1, "독 2");
        }

        // ── 상태 주사위 ──

        [Test]
        public void M5_효과_splash_상태주사위를굴려나온상태를건다_결정적()
        {
            var src = MoveSrc("splash");
            var t = Duel();
            var copy = t.S.Clone();
            var ev = Turn(t, "splash", F(R));
            var st = One<StatusApplied>(All<GameEvent>(ev, e => e is StatusApplied x && x.Source.Same(src)));
            Assert.AreEqual(t.D, st.PokemonId);
            Assert.That((int)st.Status, Is.InRange(1, 6));
            if (st.Result == StatusResult.Applied) Assert.AreEqual(st.Status, t.S.Pokemon[t.D].Status);
            // 같은 시드면 같은 면(엔진 난수)
            copy.Pokemon[copy.Battle.Sides[0].Active].Moves[0] = Mv("splash");
            copy.Rng = new Pcg32(SeedFor(F(R)));
            Battles.Answer(copy, ctx, copy.Pending[0], Battles.Move(Mv("splash")), new EventLog());
            Assert.AreEqual(t.S.Pokemon[t.D].Status, copy.Pokemon[t.D].Status);
            // 여섯 면이 모두 나온다(TTS 상태 주사위)
            var seen = new HashSet<StatusEffect>();
            var rng = new Pcg32(1);
            for (int i = 0; i < 200; i++) seen.Add(StatusRules.RollDie(rng));
            Assert.AreEqual(6, seen.Count);
        }

        [TestCase("tri_attack")]
        [TestCase("power_gem")]
        public void M5_효과_심볼이면상태주사위(string move)
        {
            var src = MoveSrc(move);
            var t = Duel();
            var ev = Turn(t, move, F(R, R, R, R));
            Assert.AreEqual(0, All<StatusApplied>(ev, x => x.Source.Same(src)).Count, "심볼이 없으면 굴리지 않는다");
            Turn(t, Plain);
            ev = Turn(t, move, F(S, R, R, R));
            Assert.AreEqual(1, All<StatusApplied>(ev, x => x.Source.Same(src)).Count);
        }

        // ── 고르기(처리기) ──

        [Test]
        public void M5_효과_photon_geyser_상태를고르고심볼이면하나더동시에()
        {
            var src = MoveSrc("photon_geyser");
            var t = Duel();
            Turn(t, "photon_geyser", F(R, R, R, R, R));
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(6, Scene.Legal(t.S, ctx, c).Count, "여섯 상태 — 「any」를 좁히지 않는다");
            Assert.AreEqual(1, c.Count, "안 고르기는 없다");
            Answer(t, ChoiceAnswer.Pick((int)StatusEffect.Burned));
            Assert.AreEqual(StatusEffect.Burned, t.S.Pokemon[t.D].Status);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status2, "심볼이 없으면 하나");

            var t2 = Duel();
            Turn(t2, "photon_geyser", F(S, R, R, R, R));
            Answer(t2, ChoiceAnswer.Pick((int)StatusEffect.Burned));
            var second = Battles.Only(t2.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.DoesNotContain(second.Options, (int)StatusEffect.Burned, "다른 상태");
            Assert.AreEqual(5, second.Options.Count);
            var ev = Answer(t2, ChoiceAnswer.Pick((int)StatusEffect.Poisoned));
            Assert.AreEqual(StatusEffect.Burned, t2.S.Pokemon[t2.D].Status);
            Assert.AreEqual(StatusEffect.Poisoned, t2.S.Pokemon[t2.D].Status2);
            Assert.AreEqual(2, All<StatusApplied>(ev, x => x.Source.Same(src) && x.Result == StatusResult.Applied).Count);

            // 이미 상태이상이면 묻지 않고 아무것도 안 걸린다(원작 커뮤니티 확인)
            var t3 = Duel();
            t3.S.Pokemon[t3.D].Status = StatusEffect.Confused;
            Turn(t3, "photon_geyser", F(S, R, R, R, R));
            Assert.AreEqual(ChoiceKind.BattleAction, t3.S.Pending[0].Kind);
            Assert.AreEqual(StatusEffect.Confused, t3.S.Pokemon[t3.D].Status);
        }

        [Test]
        public void M5_효과_bug_bite_대상주인이규토리색을고른다_가방없으면없음()
        {
            var src = MoveSrc("bug_bite");
            var t = Duel();
            t.S.Seats[1].Apricorns[0] = 2;
            t.S.Seats[1].Apricorns[3] = 1;
            Turn(t, "bug_bite", F(R, R, R));
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(1, c.Seat, "「The target decides which one」");
            CollectionAssert.AreEqual(new[] { 0, 3 }, c.Options);
            var ev = Answer(t, ChoiceAnswer.Pick(3));
            Assert.AreEqual(0, t.S.Seats[1].Apricorns[3]);
            Assert.AreEqual(2, t.S.Seats[1].Apricorns[0]);
            var spent = One<ApricornSpent>(ev);
            Assert.IsTrue(spent.Source.Same(src));
            Assert.AreEqual(1, spent.Changes[0].Seat);
            // 한 색뿐이면 묻지 않는다 · 없으면 아무 일도 없다
            Turn(t, Plain);
            Turn(t, "bug_bite", F(R, R, R));
            Assert.AreEqual(1, t.S.Seats[1].Apricorns[0]);
            Turn(t, Plain);
            t.S.Seats[1].Apricorns[0] = 0;
            var ev2 = Turn(t, "bug_bite", F(R, R, R));
            Assert.AreEqual(0, All<ApricornSpent>(ev2).Count);
        }

        [Test]
        public void M5_효과_belch_규토리를쓰면공격력7_안쓰면4_갤럭시단은못쓴다()
        {
            var t = Duel();
            t.S.Seats[0].Apricorns[2] = 1;
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            t.S.Pokemon[t.A].Moves[0] = Mv("belch");
            var ev = Answer(t, ch, Battles.Move(Mv("belch")));
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, c.Count, "「You may」 — 안 씀이 첫 답");
            Assert.IsTrue(Scene.Legal(t.S, ctx, c)[0].SameAs(ChoiceAnswer.Of(AnswerKind.Decline)));
            Assert.AreEqual(0, All<DiceRolled>(ev).Count, "굴리기 전에 묻는다");
            var copy = t.S.Clone();
            ev = Answer(t, ChoiceAnswer.Pick(2));
            Assert.AreEqual(7, One<DiceRolled>(ev).Power);
            Assert.AreEqual(0, t.S.Seats[0].Apricorns[2]);
            Assert.AreEqual(1, All<ApricornSpent>(ev).Count);
            var ev2 = new EventLog();
            Battles.Answer(copy, ctx, copy.Pending[0], ChoiceAnswer.Of(AnswerKind.Decline), ev2);
            Assert.AreEqual(4, One<DiceRolled>(ev2.Events).Power, "안 쓰면 기술 공격력 4");
            Assert.AreEqual(1, copy.Seats[0].Apricorns[2]);
            // 다음 공격(같은 기술)은 다시 묻는다 — 이번 공격만
            Turn(t, Plain);
            var ev3 = Turn(t, "belch");
            Assert.AreEqual(4, One<DiceRolled>(ev3).Power, "규토리가 없으면 묻지 않고 4");
        }

        [TestCase("hidden_power")]
        [TestCase("hidden_punch")]
        [TestCase("multi_attack")]
        public void M5_효과_배울수있는타입을골라그타입으로공격(string move)
        {
            var t = Duel();
            Turn(t, move);
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            var learn = new List<int>();
            foreach (var x in ctx.Cards.Pokemon[t.S.Pokemon[t.A].Def].LearnTypes)
                if (!learn.Contains((int)x)) learn.Add((int)x);
            CollectionAssert.AreEqual(learn, c.Options, "배울 수 있는 타입(카드 순서)");
            int pick = learn[learn.Count - 1];
            var ev = Answer(t, ChoiceAnswer.Pick(pick));
            Assert.AreEqual((PokemonType)pick, One<MoveUsed>(ev).Type);
            Assert.AreEqual((PokemonType)pick, One<TypeMatchup>(ev).Type);
            Assert.AreEqual((PokemonType)pick, One<DiceRolled>(ev).Type);
            Assert.AreEqual(move == "multi_attack", EffectRules.IgnoresPrism(ctx, Mv(move)), "multi_attack만 프리즘 아머를 무시한다");
        }

        [Test]
        public void M5_효과_judgement_모든타입_가상쪽은상성이가장좋은타입()
        {
            var t = Duel();
            Turn(t, "judgement");
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(Enum.GetValues(typeof(PokemonType)).Length, c.Options.Count, "타입없음 포함 모든 타입");
            var ev = Answer(t, ChoiceAnswer.Pick((int)PokemonType.Fairy));
            Assert.AreEqual(PokemonType.Fairy, One<MoveUsed>(ev).Type);

            // 갤럭시단 포켓몬이 쓰면 묻지 않는다: 받는 포켓몬(해피너스 노말)에게 상성 값이 가장 큰 타입
            var s = Battles.Game(ctx, 2);
            int me = Scene.Give(s, ctx, 0, "242:0");
            int g = Scene.Spawn(s, ctx, "799:0", SeatId.Galactic);
            var log = new EventLog();
            Battles.Start(s, ctx, log, BattleKind.Galactic, 0, SeatId.Galactic, -1, g);
            s.Pokemon[g].Moves[0] = Mv("judgement");
            s.Pokemon[me].Moves[0] = Mv(Plain);
            int from = log.Events.Count;
            for (int i = 0; i < 4 && s.Battle != null; i++)
            {
                var p = s.Pending[0];
                if (p.Kind == ChoiceKind.BattleAction) Battles.Answer(s, ctx, p, p.Seat == 0 ? Battles.Move(Mv(Plain)) : Battles.Move(Mv("judgement")), log);
                else Battles.Answer(s, ctx, p, p.Default.Clone(), log);
                if (log.Events.Exists(e => e is MoveUsed m && m.PokemonId == g)) break;
            }
            var used = log.Events.Find(e => e is MoveUsed m && m.PokemonId == g) as MoveUsed;
            Assert.IsNotNull(used);
            int best = int.MinValue;
            foreach (PokemonType x in Enum.GetValues(typeof(PokemonType))) best = Math.Max(best, TypeChart.Bonus(x, PokemonType.Normal, PokemonType.Typeless));
            Assert.AreEqual(best, TypeChart.Bonus(used.Type, PokemonType.Normal, PokemonType.Typeless));
        }

        // ── 교체 기술 ──

        [TestCase("u_turn")]
        [TestCase("volt_switch")]
        [TestCase("flip_turn")]
        public void M5_효과_교체기술_턴끝효과뒤에교체창_순서는그대로(string move)
        {
            var t = Duel(aBench: new[] { "799:0" });
            t.S.Pokemon[t.A].Status = StatusEffect.Poisoned;
            var ev = Turn(t, move, F(R, R, R));
            var c = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            Assert.AreEqual(0, c.Seat);
            Assert.IsTrue(c.Source.Same(MoveSrc(move)), "기술 효과로 여는 교체 창");
            Assert.AreEqual(1, All<StatusTicked>(ev, x => x.PokemonId == t.A).Count, "독(턴 끝 효과)이 먼저 — 원작 커뮤니티 확인");
            ev = Answer(t, ChoiceAnswer.Pick(t.ABench[0]));
            Assert.AreEqual(t.ABench[0], t.S.Battle.Sides[0].Active);
            Assert.AreEqual(1, All<SwitchedOut>(ev, x => x.PokemonId == t.A && x.Reason == SwitchReason.Switch).Count);
            Assert.AreEqual(0, All<OrderDecided>(ev).Count, "순서를 다시 정하지 않는다(p.8)");
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "상대가 다음");

            // 안 바꾸면 그대로
            var t2 = Duel(aBench: new[] { "799:0" });
            Turn(t2, move, F(R, R, R));
            Answer(t2, ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(t2.A, t2.S.Battle.Sides[0].Active);
            Assert.AreEqual(1, Battles.Only(t2.S, ChoiceKind.BattleAction).Seat);
        }

        [TestCase("teleport")]
        [TestCase("shelter")]
        public void M5_효과_teleport_shelter_교체하고대상의다음공격에서일반타격2개지움(string move)
        {
            var t = Duel(aBench: new[] { "799:0" });
            Turn(t, move, F(R));
            Answer(t, ChoiceAnswer.Pick(t.ABench[0]));
            var ev = Turn(t, Plain, F(R, R));
            var gone = All<DiceFacesChanged>(ev, x => x.Source.Same(MoveSrc(move)));
            Assert.AreEqual(1, gone.Count);
            Assert.AreEqual(2, gone[0].Changes.Length, "일반 타격 2개");
        }

        [Test]
        public void M5_효과_교체기술_빙결_교체도망금지_폭발수정_fairy_lock이면잠김()
        {
            // 빙결(p.9)
            var t = Duel(aBench: new[] { "799:0" });
            t.S.Pokemon[t.A].Status = StatusEffect.Frozen;
            var ev = Turn(t, "u_turn", F(R, R, R));
            Assert.AreEqual(SwitchLockReason.Frozen, One<SwitchLocked>(ev).Reason);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            // 교체·도망 금지(magma_storm 등 — ModRules.NoEscape)
            t = Duel(aBench: new[] { "799:0" });
            ModRules.Add(t.S, t.A, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoEscape, AttackDieFace.None, 0, MoveSrc("magma_storm"));
            ev = Turn(t, "volt_switch", F(R, R, R));
            Assert.AreEqual(SwitchLockReason.NoEscape, One<SwitchLocked>(ev).Reason);
            // 폭발수정에 맞은 공격자(HeldItems.SwitchLocked, 원작 커뮤니티 확인)
            t = Duel(aBench: new[] { "799:0" });
            t.S.Pokemon[t.D].HeldItem = Scene.Item(ctx, "item.blast_crystal");
            ev = Turn(t, "flip_turn", F(R, R, R));
            Assert.AreEqual(SwitchLockReason.BlastCrystal, One<SwitchLocked>(ev).Reason);
            // fairy_lock(「All Pokémon can no longer switch out using move effects」 — 상대가 걸었다, ModRules.MoveSwitchBlocked)
            t = Duel(aBench: new[] { "799:0" });
            ModRules.Add(t.S, t.D, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoMoveSwitch, AttackDieFace.None, 0, MoveSrc("fairy_lock"));
            ev = Turn(t, "teleport", F(R));
            Assert.AreEqual(SwitchLockReason.MoveSwitchBlocked, One<SwitchLocked>(ev).Reason);
            // 바꿀 포켓몬이 없으면 아무것도 보이지 않는다
            t = Duel();
            ev = Turn(t, "u_turn", F(R, R, R));
            Assert.AreEqual(0, All<SwitchLocked>(ev).Count);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
        }

        [Test]
        public void M5_효과_교체기술로상대를쓰러뜨리면S7_5교체창하나뿐()
        {
            var t = Duel(dBench: new[] { "242:0" }, aBench: new[] { "799:0" });
            Battles.SetHp(t.S, ctx, t.D, 1);
            Turn(t, "u_turn", F(R, R, R));
            var c = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            Assert.AreEqual(SourceKind.None, c.Source.Kind, "상대 기절 뒤의 교체 창(순서를 다시 정한다)");
        }

        [TestCase("lunge")]
        [TestCase("acrobatics")]
        public void M5_효과_파티에서쓰는기술_들여보내고공격_빙결이어도된다_fairy_lock이면안된다(string move)
        {
            var t = Duel(aBench: new[] { "799:0" });
            int m = Mv(move);
            t.S.Pokemon[t.ABench[0]].Moves[0] = m;
            t.S.Pokemon[t.A].Status = StatusEffect.Frozen;   // 원작 커뮤니티 확인
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var want = ChoiceAnswer.Of(AnswerKind.UseMove, m, t.ABench[0]);
            Assert.IsTrue(Scene.Legal(t.S, ctx, ch).Exists(x => x.SameAs(want)), "벤치 포켓몬의 기술(Arg = 그 포켓몬)");
            t.S.Rng = new Pcg32(SeedFor(F(R, R, R)));
            var ev = Answer(t, ch, want);
            Assert.AreEqual(t.ABench[0], t.S.Battle.Sides[0].Active);
            Assert.AreEqual(t.ABench[0], One<MoveUsed>(ev).PokemonId);
            Assert.AreEqual(1, All<SwitchedOut>(ev, x => x.PokemonId == t.A).Count);
            Assert.AreEqual(0, All<OrderDecided>(ev).Count, "순서 그대로");
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);

            var t2 = Duel(aBench: new[] { "799:0" });
            t2.S.Pokemon[t2.ABench[0]].Moves[0] = m;
            ModRules.Add(t2.S, t2.D, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoMoveSwitch, AttackDieFace.None, 0, MoveSrc("fairy_lock"));
            Assert.IsFalse(Scene.Legal(t2.S, ctx, Battles.Only(t2.S, ChoiceKind.BattleAction)).Exists(x => x.SameAs(ChoiceAnswer.Of(AnswerKind.UseMove, m, t2.ABench[0]))));
        }

        // ── 피해 이월·반사 ──

        [TestCase("renegade_fury")]
        [TestCase("overcut")]
        public void M5_효과_대상이쓰러지면남은피해는다음에나오는포켓몬에게(string move)
        {
            var src = MoveSrc(move);
            var t = Duel(dBench: new[] { "242:0" });
            Battles.SetHp(t.S, ctx, t.D, 2);
            var ev = Turn(t, move, F(R, R, R, R, R));   // 2 남은 악식킹에게 최종 피해 n(상성은 종에 따라) → 기절, n − 2 넘침
            int final = One<DamageBreakdown>(ev).Final;
            Assert.Greater(final, 2);
            Assert.AreEqual(t.DBench[0], t.S.Battle.Sides[1].Active);
            Assert.AreEqual(final - 2, t.S.Pokemon[t.DBench[0]].Damage, "넘친 피해");
            var hp = All<HpChanged>(ev, x => x.PokemonId == t.DBench[0]);
            Assert.AreEqual(1, hp.Count);
            Assert.IsTrue(hp[0].Source.Same(src));
            Assert.AreEqual(0, t.S.Battle.Sides[1].Carry);
            // 쓰러지지 않으면 넘치지 않는다
            var t2 = Duel(dBench: new[] { "242:0" });
            Turn(t2, move, F(R, R, R, R, R));
            Assert.AreEqual(0, t2.S.Battle.Sides[1].Carry);
        }

        [TestCase("outrage")]
        [TestCase("raging_fury")]
        [TestCase("petal_dance")]
        [TestCase("thrash")]
        public void M5_효과_혼란을걸고이공격동안받은피해를대상에게도(string move)
        {
            var src = MoveSrc(move);
            var t = Duel();
            var ev = Turn(t, move, F(R, R, R, R, R));
            Assert.AreEqual(StatusEffect.Confused, t.S.Pokemon[t.A].Status);
            Assert.AreEqual(1, All<HpChanged>(ev, x => x.PokemonId == t.D).Count, "받은 피해가 없으면 공격 피해뿐");
            // 둘째: 혼란 + 같은 기술 → 3, 피로 상태(5)로 굴려 3 — 원작 커뮤니티 확인(피로) → 대상에게 6 더
            Turn(t, Plain);
            t.S.Pokemon[t.A].Fatigue = 5;
            t.S.Pokemon[t.A].Damage = 0;
            ev = Turn(t, move, F(R, R, R, R, R), keepFatigue: true);
            var extra = All<HpChanged>(ev, x => x.PokemonId == t.D && x.Source.Same(src) && x.Before - x.After == 6);
            Assert.AreEqual(1, extra.Count, "혼란 3 + 피로 3");
        }

        [Test]
        public void M5_효과_shell_trap_다음에공격받을때크리가있으면공격자5()
        {
            var src = MoveSrc("shell_trap");
            var t = Duel();
            Turn(t, "shell_trap", F(R, R));
            var ev = Turn(t, Plain, F(R, S));   // 좌석 1의 공격 — 크리가 없다
            Assert.AreEqual(0, All<HpChanged>(ev, x => x.PokemonId == t.D && x.Source.Same(src)).Count);
            Assert.IsFalse(t.S.Pokemon[t.A].Mods.Exists(m => m.Op == EffectOp.Retaliate), "한 번 쓰면 사라진다(크리가 없어도)");

            var t2 = Duel();
            Turn(t2, "shell_trap", F(R, R));
            ev = Turn(t2, Plain, F(C, R));
            var hit = All<HpChanged>(ev, x => x.PokemonId == t2.D && x.Source.Same(src));
            Assert.AreEqual(1, hit.Count);
            Assert.AreEqual(5, hit[0].Before - hit[0].After);
        }

        // barrier 「When the user is next attacked, all critical hits damage the attacker instead of the user」(rulings G20 ⑷ — 결정 76 B3 ① 가: 크리티컬 몫만(크리티컬 보정 포함) · ② 가: 공격이 아니다)
        [Test]
        public void G20_barrier_다음에공격받을때크리티컬몫은공격한쪽이받는다()
        {
            var src = MoveSrc("barrier");
            var t = Duel();
            Turn(t, "barrier", F(R, R, R));
            Assert.IsTrue(t.S.Pokemon[t.A].Mods.Exists(m => m.Op == EffectOp.ReflectCrits && m.Scope == ModScope.NextAttacked));
            // 좌석 1의 x_scissor(「Critical hits deal +1 damage」, 벌레 → 노말 상성 0): 크리 둘 · 일반 하나 · 심볼 하나
            var ev = Turn(t, "x_scissor", F(C, C, R, S));
            var back = All<HpChanged>(ev, x => x.PokemonId == t.D && x.Source.Same(src));
            Assert.AreEqual(1, back.Count, "되돌린 피해는 한 번, 출처 = 배리어");
            Assert.AreEqual(6, back[0].Before - back[0].After, "크리 둘(2 × 2) + 크리티컬 보정(+1 × 2) — 원작 커뮤니티 확인");
            var br = One<DamageBreakdown>(ev);
            Assert.AreEqual(t.A, br.TargetId);
            Assert.AreEqual(1, br.Final, "받는 쪽에는 일반 타격 하나만 남는다");
            Assert.AreEqual(-6, EffectStep(ev, src), "피해 내역에 빠진 몫이 배리어 출처로 적힌다");
            Assert.IsFalse(t.S.Pokemon[t.A].Mods.Exists(m => m.Op == EffectOp.ReflectCrits), "한 번 쓰면 사라진다");
            Assert.AreEqual(-1, t.S.Battle.Sides[1].LastHitBy, "되돌린 피해는 공격이 아니다(② 가 — 반동과 같은 효과 피해)");

            // 크리가 없으면 아무 일도 없고, 그래도 그 공격에 쓴 것이다(G16 「다음」)
            var t2 = Duel();
            Turn(t2, "barrier", F(R, R, R));
            ev = Turn(t2, "x_scissor", F(R, R, S, S));
            Assert.AreEqual(0, All<HpChanged>(ev, x => x.PokemonId == t2.D && x.Source.Same(src)).Count);
            Assert.AreEqual(2, One<DamageBreakdown>(ev).Final);
            Assert.IsFalse(t2.S.Pokemon[t2.A].Mods.Exists(m => m.Op == EffectOp.ReflectCrits));
        }

        [Test]
        public void G20_barrier_되돌린피해에는상성과2배와화상감소가붙지않는다()
        {
            var src = MoveSrc("barrier");
            // ① 가: 격투 → 노말(상성 +) · 공격한 포켓몬이 화상(총 데미지 −2) — 되돌린 몫은 크리 눈 그대로
            var t = Duel();
            Turn(t, "barrier", F(R, R, R));
            t.S.Pokemon[t.D].Status = StatusEffect.Burned;
            var ev = Turn(t, "triple_kick", F(C, C, R));   // 「Regular hits deal +1 damage」
            int matchup = One<TypeMatchup>(ev).Delta;
            Assert.Greater(matchup, 0, "시험 준비: 격투 → 노말은 효과가 굉장하다");
            var back = All<HpChanged>(ev, x => x.PokemonId == t.D && x.Source.Same(src));
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(4, back[0].Before - back[0].After, "크리 둘 = 4 — 상성도 화상 감소도 붙지 않는다");
            Assert.AreEqual(Math.Max(0, 1 + 1 + matchup - BattleRules.BurnReduction), One<DamageBreakdown>(ev).Final, "받는 쪽: 일반 타격 1 + 보정 1 + 상성 − 화상 2");

            // 2배 기술(hex 「If the target has any status effects, this move deals double damage」): 되돌린 몫은 2배가 되지 않는다
            var t2 = Duel();
            Turn(t2, "barrier", F(R, R, R));
            t2.S.Pokemon[t2.A].Status = StatusEffect.Poisoned;
            int strength = ctx.Cards.Moves[Mv("hex")].AttackStrength.Value;
            var faces = new AttackDieFace[strength];
            for (int i = 0; i < faces.Length; i++) faces[i] = i == 0 ? C : S;
            ev = Turn(t2, "hex", faces);
            back = All<HpChanged>(ev, x => x.PokemonId == t2.D && x.Source.Same(src));
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(2, back[0].Before - back[0].After, "크리 하나 = 2(2배 아님)");
        }

        // ── M5 규칙 끝 검토 반영 ──

        // heal_bell 「Heal 1 damage and all status effects from the user and from all Pokémon in your party」: 기술 효과로는 기절한 포켓몬을 되살리지 못한다
        // (원작 커뮤니티 확인)
        [Test]
        public void G20_heal_bell은기절한파티포켓몬을되살리지않는다()
        {
            var t = Duel(aBench: new[] { "242:0", "242:0" });
            int down = t.ABench[0], hurt = t.ABench[1];
            Battles.SetHp(t.S, ctx, down, 0);
            Battles.SetHp(t.S, ctx, hurt, 10);
            t.S.Pokemon[down].Status = StatusEffect.None;
            Turn(t, "heal_bell", F(S, S, S));
            Turn(t, Plain, F(S, S));
            Assert.AreEqual(0, Battles.Hp(t.S, ctx, down), "기절한 벤치 포켓몬은 그대로");
            Assert.IsTrue(PokemonRules.IsFainted(ctx, t.S.Pokemon[down]));
            Assert.AreEqual(11, Battles.Hp(t.S, ctx, hurt), "기절하지 않은 파티 포켓몬은 1 회복");
        }

        // 별 방패 「Only on the first attack against the user」: 그 배틀에서 든 포켓몬마다 한 번 — 교체로 들어온 포켓몬이 벤치의 사용 기록을 가져가지 않는다(ModRules.SentIn은 기술 보정만 옮긴다)
        [Test]
        public void G19_교체해들어온포켓몬은벤치포켓몬의별방패사용기록을가져가지않는다()
        {
            var t = Duel(aBench: new[] { "242:0" });
            int shield = Scene.Item(ctx, HeldItems.StarShield);
            int b = t.ABench[0];
            t.S.Pokemon[t.A].HeldItem = shield;
            t.S.Pokemon[b].HeldItem = shield;
            Turn(t, Plain, F(S, S));
            Turn(t, Plain, F(R, R));                                        // 좌석 1이 A를 친다 — A의 별 방패를 썼다
            Assert.AreEqual(0, HeldItems.Reduction(ctx, t.S.Pokemon[t.A]));
            Turn(t, "u_turn", F(S, S, S));
            Answer(t, ChoiceAnswer.Pick(b));
            Assert.AreEqual(b, t.S.Battle.Sides[0].Active);
            Assert.AreEqual(HeldItems.StarShieldReduction, HeldItems.Reduction(ctx, t.S.Pokemon[b]), "B는 아직 공격받지 않았다");
            Assert.AreEqual(0, HeldItems.Reduction(ctx, t.S.Pokemon[t.A]), "A의 기록은 A에게 남는다(다시 나와도 다시 되지 않는다)");
        }

        // rulings A24 ③ [가정]: 기라티나로 체력 0이 된 포켓몬이 그 공격을 쓰는 동안 그 포켓몬 자신의 상태이상·지닌물건·지속 보정은 훑지 않는다(G14 — 기술 효과는 돈다)
        [Test]
        public void A24_체력0인공격자의지속보정과지닌물건공격력은훑지않는다()
        {
            var t = Duel();
            var bulk = MoveSrc("bulk_up");
            ModRules.Add(t.S, t.A, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.AddFaces, R, 2, bulk);
            ModRules.Add(t.S, t.A, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.ExtraDamage, AttackDieFace.None, 2, MoveSrc("draco_meteor"));
            t.S.Pokemon[t.A].HeldItem = Scene.Item(ctx, HeldItems.ChoiceBand);
            var mods = new List<Mod>();
            Assert.AreEqual(3, BattleRules.RollPower(t.S, ctx, Mv(Plain), t.A, t.D, mods), "체력이 있으면 결단의 머리띠 +1");
            t.S.Pokemon[t.A].Damage = PokemonRules.MaxHp(ctx, t.S.Pokemon[t.A]);   // 기라티나 3장 뒤와 같은 상태
            Assert.AreEqual(2, BattleRules.RollPower(t.S, ctx, Mv(Plain), t.A, t.D, mods), "체력 0이면 지닌물건의 공격력을 보지 않는다");
            var ev = Turn(t, Plain, F(R, R));
            Assert.AreEqual(0, All<DiceFacesChanged>(ev, x => x.Source.Same(bulk)).Count, "면 더하기를 훑지 않는다");
            Assert.AreEqual(0, EffectStep(ev, MoveSrc("draco_meteor")), "피해 보정을 훑지 않는다");
        }

        // ── 기술마다(완료 조건 1 「효과 Id마다 시험」 — 규칙 끝 감사 effect-cover: 그 기술을 부르는 시험이 없던 기술) ──

        [Test]
        public void M5_효과_uproar_심볼마다1더하고자신과대상의졸림만푼다()
        {
            // 「Status symbols deal +1 damage. Heal any Drowsy status effects from the user and the target.」 — op CureStatus를 쓰는 기술은 이것 하나(악은 졸림 면역이라 대상은 노말)
            var src = MoveSrc("uproar");
            var t = Duel(d: "242:0");
            t.S.Pokemon[t.A].Status = StatusEffect.Drowsy;
            t.S.Pokemon[t.D].Status = StatusEffect.Drowsy;
            var ev = Turn(t, "uproar", F(S, S, S));
            Assert.AreEqual(3, EffectStep(ev, src), "심볼 3개 × +1(졸림의 면 바꾸기는 일반 타격에만 — 바꿀 것이 없다)");
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status, "자신의 졸림");
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status, "대상의 졸림");
            // 풀기 op 둘(자신 · 대상) = 상태만 푼 회복 둘
            var heals = All<Healed>(ev, h => h.Source.Same(src));
            Assert.AreEqual(2, heals.Count);
            CollectionAssert.AreEquivalent(new[] { t.A, t.D }, new[] { heals[0].Pokemon[0].Id, heals[1].Pokemon[0].Id });
            foreach (var h in heals) Assert.AreEqual(StatusEffect.Drowsy, h.Pokemon[0].StatusBefore);

            // 졸림이 아닌 상태이상은 그대로 둔다
            var t2 = Duel(d: "242:0");
            t2.S.Pokemon[t2.A].Status = StatusEffect.Burned;
            t2.S.Pokemon[t2.D].Status = StatusEffect.Poisoned;
            Turn(t2, "uproar", F(S, S, S));
            Assert.AreEqual(StatusEffect.Burned, t2.S.Pokemon[t2.A].Status);
            Assert.AreEqual(StatusEffect.Poisoned, t2.S.Pokemon[t2.D].Status);
        }

        [Test]
        public void M5_효과_poltergeist_대상이지닌물건이있을때만1더하고마비()
        {
            // 「If the target has a held item, deal 1 extra damage and inflict the Paralysed status on the target.」 — 조건 targetHeldItem을 쓰는 기술은 이것 하나
            var src = MoveSrc("poltergeist");
            var t = Duel();
            var ev = Turn(t, "poltergeist", F(R, R, R, R));
            Assert.AreEqual(0, EffectStep(ev, src), "지닌물건이 없으면 추가 피해 없음");
            Assert.AreEqual(0, All<StatusApplied>(ev, x => x.Source.Same(src)).Count, "마비도 없음");
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status);

            var t2 = Duel();
            t2.S.Pokemon[t2.D].HeldItem = Scene.Item(ctx, HeldItems.ChoiceBand);
            ev = Turn(t2, "poltergeist", F(R, R, R, R));
            Assert.AreEqual(1, EffectStep(ev, src));
            var st = One<StatusApplied>(All<GameEvent>(ev, e => e is StatusApplied a && a.Source.Same(src)));
            Assert.AreEqual(t2.D, st.PokemonId);
            Assert.AreEqual(StatusResult.Applied, st.Result);
            Assert.AreEqual(StatusEffect.Paralysed, t2.S.Pokemon[t2.D].Status);
        }

        /// <summary>
        /// 기술 키 → 그 영문 문구가 보이는 효과(「·」로 여럿). 문구마다 무엇을 보는지:
        /// Inflict:상태 = 늘 건다 · Sym1:상태 / Sym2:상태 = 심볼이 1 / 2개 이상이면 건다(하나 모자라면 안 건다) · C+n · R+n · S+n · S-n = 그 면마다 효과 피해 n ·
        /// Self:n = 크리티컬마다 자신이 n 피해 · Repeat:n = 직전 턴과 같은 기술이면 공격력 n · Already3:상태 = 이미 그 상태면 공격력 3 ·
        /// Double:상태 = 이미 그 상태면 2배(이번 공격이 건 것으로는 아니다, G14 — Any = 아무 상태이상) · Fatigue:n = 자신의 배틀 피로 +n.
        /// uproar · poltergeist는 위의 따로 시험. barrier는 미구현(규칙 끝 물을 것 B3 — M3_효과없는기술은피해만주고미구현목록에오름이 「미구현」을 단정).
        /// </summary>
        static readonly object[] EachMove =
        {
            // Critical hits deal +1 damage.
            new object[] { "night_slash", "C+1" }, new object[] { "air_slash", "C+1" }, new object[] { "leaf_blade", "C+1" },
            new object[] { "cross_poison", "C+1" }, new object[] { "psycho_cut", "C+1" }, new object[] { "razor_shell", "C+1" },
            // Critical hits deal +2 damage.
            new object[] { "spacial_rend", "C+2" }, new object[] { "octazooka", "C+2" },
            // Regular hits deal +1 damage.
            new object[] { "dual_chop", "R+1" }, new object[] { "triple_kick", "R+1" }, new object[] { "bonemerang", "R+1" },
            // Status symbols deal +1 damage.
            new object[] { "dragon_pulse", "S+1" }, new object[] { "aura_sphere", "S+1" }, new object[] { "swift", "S+1" },
            // Critical hits deal +1 damage but status symbols deal -1 damage.
            new object[] { "mountain_gale", "C+1·S-1" }, new object[] { "stone_edge", "C+1·S-1" },
            // Critical hits deal +2 damage but for each critical hit rolled: The user takes 2 / 3 damage.
            new object[] { "flare_blitz", "C+2·Self:2" }, new object[] { "brave_bird", "C+2·Self:2" }, new object[] { "wood_hammer", "C+2·Self:2" },
            new object[] { "mind_blown", "C+2·Self:3" }, new object[] { "explosion", "C+2·Self:3" }, new object[] { "head_smash", "C+2·Self:3" },
            // If this move was used last turn, attack strength is decreased to 2. / (sky_attack) to 3. Critical hits deal +1 damage.
            new object[] { "roar_of_time", "Repeat:2" }, new object[] { "solar_beam", "Repeat:2" }, new object[] { "rock_wrecker", "Repeat:2" },
            new object[] { "ripple_in_space", "Repeat:2" }, new object[] { "sky_attack", "Repeat:3·C+1" },
            // Inflict the X status on the target.
            new object[] { "thunder_shock", "Inflict:Paralysed" }, new object[] { "lick", "Inflict:Paralysed" },
            new object[] { "powder_snow", "Inflict:Frozen" }, new object[] { "confusion", "Inflict:Confused" },
            // If any status symbols are rolled: Inflict the X status on the target.
            new object[] { "dragon_breath", "Sym1:Burned" }, new object[] { "fire_fang", "Sym1:Burned" }, new object[] { "seed_flare", "Sym1:Burned" },
            new object[] { "scald", "Sym1:Burned" }, new object[] { "thunderbolt", "Sym1:Paralysed" }, new object[] { "headbutt", "Sym1:Paralysed" },
            new object[] { "body_slam", "Sym1:Paralysed" }, new object[] { "ice_fang", "Sym1:Frozen" }, new object[] { "ice_beam", "Sym1:Frozen" },
            new object[] { "ice_punch", "Sym1:Frozen" }, new object[] { "poison_jab", "Sym1:Poisoned" }, new object[] { "water_pulse", "Sym1:Confused" },
            // If 2 or more status symbols are rolled: Inflict the X status on the target.
            new object[] { "dragon_rush", "Sym2:Paralysed" }, new object[] { "fire_blast", "Sym2:Burned" },
            new object[] { "sludge_wave", "Sym2:Poisoned" }, new object[] { "hydro_pump", "Sym2:Confused" },
            // Inflict the X status on the target. Status symbols deal -1 damage. / Critical hits deal +1 damage. / but increase your battle fatigue by 2.
            new object[] { "zap_cannon", "Inflict:Paralysed·S-1" }, new object[] { "inferno", "Inflict:Burned·S-1" }, new object[] { "sheer_cold", "Inflict:Frozen·S-1" },
            new object[] { "poison_tail", "Inflict:Poisoned·C+1" }, new object[] { "force_palm", "Inflict:Paralysed·Fatigue:2" },
            // Inflict the X status on the target. If the target is already X, attack strength is increased to 3.
            new object[] { "sweet_kiss", "Inflict:Confused·Already3:Confused" }, new object[] { "confuse_ray", "Inflict:Confused·Already3:Confused" },
            new object[] { "will_o_wisp", "Inflict:Burned·Already3:Burned" }, new object[] { "grass_whistle", "Inflict:Drowsy·Already3:Drowsy" },
            new object[] { "toxic", "Inflict:Poisoned·Already3:Poisoned" },
            // Inflict the X status on the target. If the target is already X, this move deals double damage.
            new object[] { "chatter", "Inflict:Confused·Double:Confused" }, new object[] { "menacing_moonraze", "Inflict:Frozen·Double:Frozen" },
            new object[] { "barb_barrage", "Inflict:Poisoned·Double:Poisoned" }, new object[] { "searing_sunraze", "Inflict:Burned·Double:Burned" },
            // If the target has any status effects, this move deals double damage.
            new object[] { "venoshock", "Double:Any" },
        };

        /// <summary>처음 count개가 face이고 나머지가 일반 타격인 굴림 n개.</summary>
        static AttackDieFace[] Roll(int n, AttackDieFace face, int count)
        {
            var f = new AttackDieFace[n];
            for (int i = 0; i < n; i++) f[i] = i < count ? face : R;
            return f;
        }

        /// <summary>그 상태이상이 걸리는 대상(악식킹은 악 — 졸림 면역이라 졸림만 노말 해피너스).</summary>
        static string TargetFor(StatusEffect x) => x == StatusEffect.Drowsy ? "242:0" : "799:0";

        [TestCaseSource(nameof(EachMove))]
        public void M5_효과_기술마다문구의효과가걸린다(string move, string spec)
        {
            var src = MoveSrc(move);
            int n = ctx.Cards.Moves[Mv(move)].AttackStrength.Value;
            foreach (string part in spec.Split('·'))
            {
                int colon = part.IndexOf(':');
                string kind = colon < 0 ? part : part.Substring(0, colon), arg = colon < 0 ? null : part.Substring(colon + 1);
                StatusEffect Status() => (StatusEffect)Enum.Parse(typeof(StatusEffect), arg);
                if (colon < 0)
                {
                    // 면마다 효과 피해: 그 면 둘(일반 타격 +n이면 모두) + 나머지 일반 타격
                    var face = kind[0] == 'C' ? C : kind[0] == 'S' ? S : R;
                    int per = int.Parse(kind.Substring(1));
                    var t = Duel();
                    var ev = Turn(t, move, face == R ? Roll(n, R, n) : Roll(n, face, Math.Min(2, n)));
                    int count = AttackDice.Count(One<DiceRolled>(ev).Faces, face);
                    Assert.Greater(count, 0, part);
                    Assert.AreEqual(per * count, EffectStep(ev, src), $"{move} {part}");
                    continue;
                }
                switch (kind)
                {
                    case "Inflict":
                    {
                        var t = Duel(d: TargetFor(Status()));
                        var ev = Turn(t, move, Roll(n, R, n));
                        var st = One<StatusApplied>(All<GameEvent>(ev, e => e is StatusApplied a && a.Source.Same(src)));
                        Assert.AreEqual(StatusResult.Applied, st.Result, part);
                        Assert.AreEqual(t.D, st.PokemonId, part);
                        Assert.AreEqual(Status(), t.S.Pokemon[t.D].Status, part);
                        break;
                    }
                    case "Sym1":
                    case "Sym2":
                    {
                        int need = kind == "Sym1" ? 1 : 2;
                        var t = Duel(d: TargetFor(Status()));
                        Turn(t, move, Roll(n, S, need));
                        Assert.AreEqual(Status(), t.S.Pokemon[t.D].Status, $"{move}: 심볼 {need}개");
                        var t2 = Duel(d: TargetFor(Status()));
                        var ev = Turn(t2, move, Roll(n, S, need - 1));
                        Assert.AreEqual(0, All<StatusApplied>(ev, a => a.Source.Same(src)).Count, $"{move}: 심볼 {need - 1}개면 걸지 않는다");
                        Assert.AreEqual(StatusEffect.None, t2.S.Pokemon[t2.D].Status);
                        break;
                    }
                    case "Self":
                    {
                        int per = int.Parse(arg);
                        var t = Duel();
                        var ev = Turn(t, move, Roll(n, C, 2));
                        Assert.AreEqual(2, AttackDice.Count(One<DiceRolled>(ev).Faces, C));
                        Assert.AreEqual(2 * per, t.S.Pokemon[t.A].Damage, $"{move}: 크리티컬 2 × {per}");
                        break;
                    }
                    case "Repeat":
                    {
                        var t = Duel();
                        var first = One<DiceRolled>(Turn(t, move, Roll(n, S, n)));
                        Assert.AreEqual(n, first.Power, "처음 쓰면 그대로");
                        t.S.Pokemon[t.A].Damage = t.S.Pokemon[t.D].Damage = 0;
                        Turn(t, Plain, F(S, S));
                        t.S.Pokemon[t.A].Damage = t.S.Pokemon[t.D].Damage = 0;
                        var again = One<DiceRolled>(Turn(t, move));
                        Assert.AreEqual(int.Parse(arg), again.Power, $"{move}: 직전 턴과 같은 기술");
                        Assert.IsTrue(again.PowerMods[0].Source.Same(src));
                        break;
                    }
                    case "Already3":
                    {
                        var t = Duel(d: TargetFor(Status()));
                        t.S.Pokemon[t.D].Status = Status();
                        var roll = One<DiceRolled>(Turn(t, move));
                        Assert.AreEqual(3, roll.Power, $"{move}: 이미 {arg}");
                        Assert.IsTrue(roll.PowerMods[0].Source.Same(src));
                        break;
                    }
                    case "Double":
                    {
                        var pre = arg == "Any" ? StatusEffect.Burned : Status();
                        var t = Duel(d: TargetFor(pre));
                        t.S.Pokemon[t.D].Status = pre;
                        var dbl = Array.Find(One<DamageBreakdown>(Turn(t, move)).Steps, x => x.Kind == DamageStepKind.Double);
                        Assert.IsNotNull(dbl, $"{move}: 이미 {pre}면 2배");
                        Assert.IsTrue(dbl.Source.Same(src));
                        var t2 = Duel(d: TargetFor(pre));
                        Assert.IsNull(Array.Find(One<DamageBreakdown>(Turn(t2, move)).Steps, x => x.Kind == DamageStepKind.Double),
                            $"{move}: 상태이상이 없던 대상(이번 공격이 건 것)으로는 2배가 아니다(G14)");
                        break;
                    }
                    case "Fatigue":
                    {
                        var t = Duel();
                        var ev = Turn(t, move, Roll(n, R, n));
                        var f = One<FatigueChanged>(All<GameEvent>(ev, e => e is FatigueChanged x && x.Cause == FatigueCause.Effect && x.Source.Same(src)));
                        Assert.AreEqual(t.A, f.PokemonId);
                        Assert.AreEqual(int.Parse(arg), f.After - f.Before, $"{move}: 자신의 피로 +{arg}");
                        break;
                    }
                    default:
                        Assert.Fail($"모르는 갈래 {part}");
                        break;
                }
            }
        }

        // ── 데이터 ──

        [Test]
        public void M5_효과표_나머지op갈래칸을읽고같은바이트로쓴다()
        {
            // 로더가 새 칸(status2 · partyType · knowsType · TargetOther · 처리기 절)을 읽고 build.mjs와 같은 바이트로 쓴다(전체 표 왕복은 M3_효과표왕복같은바이트)
            var e = ctx.Effects;
            var both = e.Effects[e.OfMove(Mv("infatuate"))[0]].Ops[0];
            Assert.AreEqual(EffectOp.InflictBoth, both.Op);
            Assert.AreEqual(StatusEffect.Confused, both.Status2);
            Assert.AreEqual(PokemonType.Bug, e.Effects[e.OfMove(Mv("attack_order"))[0]].When.PartyType);
            Assert.AreEqual(PokemonType.Electric, e.Effects[e.OfMove(Mv("wild_charge"))[0]].When.KnowsType);
            Assert.AreEqual(EffectWho.TargetOther, e.Effects[e.OfMove(Mv("shocking_verity"))[0]].Ops[1].Who);
            Assert.AreEqual("type_any", e.Effects[e.OfMove(Mv("judgement"))[0]].Handler);
            Assert.AreEqual(Window.BeforeRoll, e.Effects[e.OfMove(Mv("belch"))[0]].Window);
            Assert.IsTrue(EffectRules.UsableFromParty(ctx, Mv("lunge")));
            Assert.IsFalse(EffectRules.UsableFromParty(ctx, Mv("u_turn")));
            Assert.Throws<ArgumentException>(() => new EffectOpDef(EffectOp.InflictBoth, EffectWho.Target, StatusEffect.Burned, status2: StatusEffect.Burned), "서로 다른 상태 둘");
            Assert.Throws<ArgumentException>(() => new EffectOpDef(EffectOp.Heal, EffectWho.TargetOther, value: 1), "다른 포켓몬은 Inflict·Damage만");
        }
    }
}
