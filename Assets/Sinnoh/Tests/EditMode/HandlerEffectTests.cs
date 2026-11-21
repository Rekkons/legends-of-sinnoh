using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「기술 효과 — 핸들러 갈래」(01 §7-6, rulings G21): 공격력 동적·「?」 15 · 복사·섞기 3 · 미분류 5 · 둘째 기술 6 · encore = 30장.
    /// 효과의 근거는 xlsx 영문 문구다(기술 키가 시험 이름·TestCase에 있다 — <see cref="Moved"/>가 목록, 남긴 것은 <see cref="Left"/>). 실제 배틀 흐름(트레이너전)으로 돈다.
    /// 포켓몬: 해피너스 242(노말, 체력 14, 우선도 6, 시그니처 double_edge) 대 악식킹 799(악/드래곤, 23, 5, chomp) — 좌석 0이 먼저다. 기술은 배운 기술 칸 0에 넣어 쓴다.
    /// </summary>
    public class HandlerEffectTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        /// <summary>남는 상태가 없는 공격력 2(두번치기 「Regular hits deal +1 damage」).</summary>
        const string Plain = "double_hit";

        /// <summary>이 묶음이 옮긴 기술 30장.</summary>
        public static readonly string[] Moved =
        {
            // 공격력 동적·「?」 15
            "thunder_song", "fire_song", "frost_song", "surf", "revenge", "destiny_bond", "rampart_riposte", "flail", "rollout", "lift_off",
            "eruption", "electro_ball", "seismic_toss", "heavy_slam", "foul_play",
            // 복사·섞기 3 · 미분류 5
            "kinesis", "mimic", "metronome", "sacred_sword", "giga_drain", "power_whip", "future_sight", "soft_boiled",
            // 두 번째 기술 7
            "mach_punch", "shadow_sneak", "quick_attack", "extreme_speed", "bullet_punch", "aqua_jet", "encore",
        };

        /// <summary>
        /// 남긴 기술(효과 표 밖): 없음 — barrier는 결정 76 B3 답으로 M5 마감 검토 반영이 옮겼다(rulings G20 ⑷, OpEffectTests.G20_barrier_*). 폼 바꾸기가 문구에 있는 8장은 「전설·알파·폼」이 옮겼다(LegendFormTests.FormMoves).
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
            public int A, D;
            public List<int> DBench = new List<int>(), ABench = new List<int>();
        }

        /// <summary>트레이너전: 좌석 0(a + aBench) 대 좌석 1(d + dBench). 좌석 0의 기술 고르기가 열려 있다.</summary>
        Arena Duel(string a = "242:0", string d = "799:0", string[] dBench = null, string[] aBench = null, RuleOptions opt = null)
        {
            var t = new Arena { S = Battles.Game(ctx, 2, 7, opt) };
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
            if (Battles.Only(t.S, ChoiceKind.BattleAction).Seat == 1) Turn(t, Plain);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0의 차례");
            return t;
        }

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

        /// <summary>차례인 좌석의 출전 포켓몬이 그 기술을 쓴다(배운 칸 0). faces를 주면 그 굴림이 나온다. 굴리기 전에 출전 둘의 피로를 0으로. 반환 = 다음 선택이 열릴 때까지의 이벤트.</summary>
        List<GameEvent> Turn(Arena t, string move, AttackDieFace[] faces = null)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var b = t.S.Battle;
            int active = b.Sides[ChoiceRules.BattleSideOf(t.S, ch.Seat)].Active;
            int m = Mv(move);
            t.S.Pokemon[active].Moves[0] = m;
            t.S.Pokemon[b.Sides[0].Active].Fatigue = 0;
            t.S.Pokemon[b.Sides[1].Active].Fatigue = 0;
            if (faces != null) t.S.Rng = new Pcg32(SeedFor(faces));
            return Answer(t, ch, Battles.Move(m));
        }

        List<GameEvent> Answer(Arena t, PendingChoice ch, ChoiceAnswer a)
        {
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, a, t.Log);
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        /// <summary>열린 효과 고르기(Reaction/EffectPick)에 답한다. faces를 주면 그 다음 굴림이 그 면으로 나온다.</summary>
        List<GameEvent> Pick(Arena t, int value, AttackDieFace[] faces = null)
        {
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            if (faces != null) t.S.Rng = new Pcg32(SeedFor(faces));
            return Answer(t, c, ChoiceAnswer.Pick(value));
        }

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

        static int EffectStep(DamageBreakdown br, SourceRef src)
        {
            int v = 0;
            foreach (var st in br.Steps)
                if (st.Kind == DamageStepKind.Effect && st.Source.Same(src)) v += st.Value;
            return v;
        }

        static AttackDieFace[] F(params AttackDieFace[] f) => f;

        int Power(Arena t, string move, int attacker, int defender) => BattleRules.RollPower(t.S, ctx, Mv(move), attacker, defender, new List<Mod>());

        // ── 덮개 ──

        [Test]
        public void M5_효과_핸들러갈래30장은효과표에있고남긴barrier만미구현목록에있다()
        {
            Assert.AreEqual(30, Moved.Length);
            Assert.AreEqual(30, new HashSet<string>(Moved).Count);
            foreach (string key in Moved) Assert.IsTrue(ctx.Effects.Implemented(Mv(key)), key);
            var todo = new List<string>();
            foreach (var m in ctx.Cards.Moves)
                if (!ctx.Effects.Implemented(Mv(m.Key))) todo.Add(m.Key);
            CollectionAssert.AreEquivalent(Left, todo, "미구현 39 → 31(폼 기술 8, LegendFormTests) → 1(barrier) → 0(결정 76 B3)");
            foreach (string key in OpEffectTests.Left) CollectionAssert.Contains(todo, key);
            // 셈 처리기 절은 상시 절이다(프레임으로 쌓지 않는다)
            foreach (int id in ctx.Effects.OfMove(Mv("eruption"))) Assert.IsTrue(ctx.Effects.Effects[id].IsStatic);
            foreach (int id in ctx.Effects.OfMove(Mv("kinesis"))) Assert.IsFalse(ctx.Effects.Effects[id].IsStatic);
        }

        // ── 공격력 「?」·동적 ──

        [Test]
        public void M5_효과_eruption_heavy_slam_seismic_toss_electro_ball_공격력이그값_상한()
        {
            var t = Duel();
            Assert.AreEqual(7, Power(t, "eruption", t.A, t.D), "체력 14 → 상한 7");
            Battles.SetHp(t.S, ctx, t.A, 3);
            Assert.AreEqual(3, Power(t, "eruption", t.A, t.D), "지금 체력");
            Assert.AreEqual(5, Power(t, "heavy_slam", t.A, t.D), "기본 체력 14의 절반 7 → 상한 5");
            Assert.AreEqual(7, Power(t, "seismic_toss", t.A, t.D), "대상 기본 체력 23의 절반 11 → 상한 7");
            Assert.AreEqual(3, Power(t, "electro_ball", t.A, t.D), "우선도 6의 절반");
            t.S.Pokemon[t.A].Status = StatusEffect.Paralysed;
            Assert.AreEqual(1, Power(t, "electro_ball", t.A, t.D), "지금 우선도 = 6 − 3(마비)");
            t.S.Pokemon[t.A].Status = StatusEffect.None;
            t.S.Pokemon[t.A].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            Assert.AreEqual(3, Power(t, "electro_ball", t.A, t.D), "민첩의 띠: 우선도 8 → 4, 띠의 공격력 −1(원작 커뮤니티 확인의 결)");
            t.S.Pokemon[t.A].HeldItem = -1;

            var small = Duel("399:0", "399:0");                              // 비버니 체력 5
            Assert.AreEqual(2, Power(small, "heavy_slam", small.A, small.D));
            Assert.AreEqual(2, Power(small, "seismic_toss", small.A, small.D));

            // 실제 굴림도 같은 값(공격력 「?」 = 0에 「= n」 보정) — 출처는 그 기술
            Battles.SetHp(t.S, ctx, t.A, 14);
            var roll = One<DiceRolled>(Turn(t, "eruption"));
            Assert.AreEqual(7, roll.Power);
            Assert.IsTrue(Array.Exists(roll.PowerMods, m => m.Source.Same(MoveSrc("eruption")) && m.Delta == 7));
        }

        [Test]
        public void M5_효과_flail_revenge_노래_surf_파티와받은피해로공격력이늘어난다()
        {
            var t = Duel(aBench: new[] { "399:0", "266:0" });
            Assert.AreEqual(1, Power(t, "flail", t.A, t.D));
            t.S.Pokemon[t.A].Damage = 5;
            Assert.AreEqual(3, Power(t, "flail", t.A, t.D), "지금 받은 피해 2마다 +1(원작 커뮤니티 확인)");
            t.S.Pokemon[t.A].Damage = 0;

            Assert.AreEqual(2, Power(t, "revenge", t.A, t.D));
            Battles.SetHp(t.S, ctx, t.ABench[0], 0);
            Assert.AreEqual(3, Power(t, "revenge", t.A, t.D), "기절한 파티 포켓몬마다 +1");
            Battles.SetHp(t.S, ctx, t.ABench[1], 0);
            Assert.AreEqual(4, Power(t, "revenge", t.A, t.D));

            var u = Duel(aBench: new[] { "399:0", "266:0" });
            u.S.Pokemon[u.A].Moves[1] = Mv("thunder_song");                   // 자신의 노래는 세지 않는다(「other」)
            foreach (string song in new[] { "thunder_song", "fire_song", "frost_song" }) Assert.AreEqual(5, Power(u, song, u.A, u.D), song);
            u.S.Pokemon[u.ABench[0]].Moves[0] = Mv("perish_song");             // perish_song도 「Song move」(키가 _song)
            u.S.Pokemon[u.ABench[1]].Moves[1] = Mv("frost_song");
            Assert.AreEqual(7, Power(u, "fire_song", u.A, u.D), "노래를 아는 다른 포켓몬 둘");
            Battles.SetHp(u.S, ctx, u.ABench[1], 0);
            Assert.AreEqual(6, Power(u, "fire_song", u.A, u.D), "기절한 포켓몬은 세지 않는다");
            Assert.AreEqual(4, Power(u, "surf", u.A, u.D));
            u.S.Pokemon[u.ABench[0]].Moves[1] = Mv("surf");
            Assert.AreEqual(5, Power(u, "surf", u.A, u.D), "파도타기를 아는 다른 포켓몬마다 +1");
        }

        [TestCase("destiny_bond")]
        [TestCase("rampart_riposte")]
        public void M5_효과_앞배틀턴에받은피해만큼공격력이늘어난다(string move)
        {
            var t = Duel();
            Turn(t, Plain, F(S, S));
            Turn(t, Plain, F(R, R));                                          // 악식킹의 두번치기: 일반 둘 = 4
            Assert.AreEqual(1 + 4, One<DiceRolled>(Turn(t, move)).Power, "바로 앞(상대) 배틀 턴에 받은 4");
            Turn(t, Plain, F(S, S));                                          // 0 피해
            Assert.AreEqual(1, One<DiceRolled>(Turn(t, move)).Power, "앞 턴에 받은 피해가 없다(그 전 턴의 것은 세지 않는다)");
        }

        [TestCase("rollout")]
        [TestCase("lift_off")]
        public void M5_효과_이배틀에서쓸때마다공격력1(string move)
        {
            var t = Duel();
            var ev = Turn(t, move);
            Assert.AreEqual(3, One<DiceRolled>(ev).Power, "처음은 카드 공격력");
            if (move == "lift_off") Assert.AreEqual(StatusEffect.Burned, t.S.Pokemon[t.D].Status, "lift_off는 화상도 건다");
            Turn(t, Plain);
            Assert.AreEqual(4, One<DiceRolled>(Turn(t, move)).Power);
            Turn(t, Plain);
            Turn(t, Plain);                                                   // 다른 기술을 써도 이 배틀의 수는 남는다(「this battle」)
            Turn(t, Plain);
            Assert.AreEqual(5, One<DiceRolled>(Turn(t, move)).Power);
        }

        [Test]
        public void M5_효과_foul_play_대상기술의기본공격력_셈은대상쪽에서_정해지지않으면1()
        {
            var t = Duel();
            t.S.Pokemon[t.D].Moves[0] = Mv("eruption");
            t.S.Pokemon[t.D].Moves[1] = Mv("mimic");
            Turn(t, "foul_play");
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { Mv("chomp"), Mv("eruption"), Mv("mimic") }, c.Options, "대상의 기술(시그니처 → 배운 칸)");
            Assert.AreEqual(1, c.Count, "하나는 반드시 고른다");
            var copy = t.S.Clone();
            var copy2 = t.S.Clone();
            Assert.AreEqual(5, One<DiceRolled>(Pick(t, Mv("chomp"))).Power, "카드 공격력 5(원작 커뮤니티 확인)");
            t.S = copy;
            Assert.AreEqual(7, One<DiceRolled>(Pick(t, Mv("eruption"))).Power, "「?」는 대상 쪽에서 본 값 — 악식킹 체력 23 → 7");
            t.S = copy2;
            Assert.AreEqual(1, One<DiceRolled>(Pick(t, Mv("mimic"))).Power, "정해지지 않은 공격력 = 최소 1");
        }

        // ── 복사·섞기 ──

        [Test]
        public void M5_효과_mimic_대상의기술로공격_쓴기술은mimic()
        {
            var t = Duel();
            t.S.Pokemon[t.D].Moves[0] = Mv("ember");
            Turn(t, "mimic");
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { Mv("chomp"), Mv("ember") }, c.Options);
            var ev = Pick(t, Mv("ember"));
            var roll = One<DiceRolled>(ev);
            Assert.AreEqual(2, roll.Power, "부른 기술의 공격력");
            Assert.AreEqual(PokemonType.Fire, roll.Type);
            Assert.AreEqual(PokemonType.Fire, One<MoveUsed>(ev).Type);
            Assert.AreEqual(Mv("mimic"), One<MoveUsed>(ev).MoveKey);
            Assert.AreEqual(StatusEffect.Burned, t.S.Pokemon[t.D].Status, "부른 기술의 효과");
            Assert.AreEqual(Mv("mimic"), t.S.Pokemon[t.A].LastMove, "직전 기술 = mimic(원작 커뮤니티 확인)");

            // 혼란이면 mimic을 이어 쓰면 3 피해(부른 기술이 달라도)
            Turn(t, Plain);
            t.S.Pokemon[t.A].Status = StatusEffect.Confused;
            t.S.Pokemon[t.D].Moves[0] = Mv(Plain);
            Turn(t, "mimic");
            ev = Pick(t, Mv(Plain));
            Assert.AreEqual(1, All<HpChanged>(ev, h => h.PokemonId == t.A).Count + All<StatusTicked>(ev, h => h.PokemonId == t.A && h.Status == StatusEffect.Confused).Count, "혼란 3 피해");

            // mimic만 아는 대상이면 metronome처럼(원작 커뮤니티 확인)
            var u = Duel(d: "439:0");
            Assert.AreEqual("mimic", ctx.Cards.Moves[ctx.Cards.Pokemon[u.S.Pokemon[u.D].Def].Signature].Key, "마임맨 주니어의 시그니처 mimic");
            var ev2 = Turn(u, "mimic");
            Assert.AreEqual(1, All<PileShuffled>(ev2, p => p.Pile == DeckId.Evolution).Count, "진화 덱을 섞는다");
        }

        [Test]
        public void M5_효과_metronome_진화덱을섞고맨위카드의기술로공격()
        {
            var t = Duel();
            Assert.Greater(t.S.Decks[(int)DeckId.Evolution].Count, 0);
            var ev = Turn(t, "metronome");
            Assert.AreEqual(1, All<PileShuffled>(ev, p => p.Pile == DeckId.Evolution).Count);
            var deck = t.S.Decks[(int)DeckId.Evolution];
            int called = ctx.Cards.Pokemon[deck[deck.Count - 1]].Signature;
            var used = One<MoveUsed>(ev);
            Assert.AreEqual(Mv("metronome"), used.MoveKey);
            Assert.AreEqual(BattleRules.MoveType(ctx, called), used.Type, "맨 위 카드의 시그니처 기술의 타입");
            if (ctx.Cards.Moves[called].AttackStrength is int n) Assert.AreEqual(n, One<DiceRolled>(ev).Power, "그 기술의 공격력");
        }

        [Test]
        public void M5_효과_mimic_metronome_부른기술을둘째MoveSelected로알린다()
        {
            // 화면이 「흉내내기로 불꽃세례가 나왔다!」를 적을 수 있게(leftovers §1 #6): 부른 기술은 같은 공격의 둘째 MoveSelected(타입·공격력 = 부른 기술), 쓴 기술(MoveUsed)은 그대로
            var t = Duel();
            t.S.Pokemon[t.D].Moves[0] = Mv("ember");
            var first = Turn(t, "mimic");
            Assert.AreEqual(Mv("mimic"), One<MoveSelected>(first).MoveKey, "고르기 전에는 쓴 기술만");
            var ev = Pick(t, Mv("ember"));
            var called = One<MoveSelected>(ev);
            Assert.AreEqual(Mv("ember"), called.MoveKey);
            Assert.AreEqual(PokemonType.Fire, called.Type);
            Assert.AreEqual(2, called.Power);
            Assert.AreEqual(One<MoveUsed>(ev).Side, called.Side, "공격하는 쪽");
            Assert.Less(ev.IndexOf(called), ev.IndexOf(One<MoveUsed>(ev)), "굴리기 전에 알린다");

            var u = Duel();
            var ev2 = Turn(u, "metronome");
            var deck = u.S.Decks[(int)DeckId.Evolution];
            int top = ctx.Cards.Pokemon[deck[deck.Count - 1]].Signature;
            var sel = All<MoveSelected>(ev2);
            Assert.AreEqual(2, sel.Count, "쓴 기술 + 부른 기술");
            Assert.AreEqual(Mv("metronome"), sel[0].MoveKey);
            Assert.AreEqual(top, sel[1].MoveKey, "진화 덱 맨 위 카드의 시그니처 기술");
            Assert.AreEqual(BattleRules.MoveType(ctx, top), sel[1].Type);
        }

        [Test]
        public void M5_효과_kinesis_고른기술의효과를복사_공격력셈도_한도는쓴포켓몬에게()
        {
            var t = Duel();
            t.S.Pokemon[t.D].Moves[0] = Mv("eruption");
            Turn(t, "kinesis");
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { Mv("double_edge"), Mv("chomp"), Mv("eruption") }, c.Options, "자기 기술(kinesis 빼고) → 대상 기술");
            var copy = t.S.Clone();
            var ev = Pick(t, Mv("eruption"));
            var roll = One<DiceRolled>(ev);
            Assert.AreEqual(7, roll.Power, "공격력을 정하는 효과도 복사한다(원작 커뮤니티 확인)");
            Assert.AreEqual(PokemonType.Psychic, roll.Type, "타입은 kinesis");

            t.S = copy;
            var ev2 = Pick(t, Mv("double_edge"), F(C, C, R));
            Assert.AreEqual(4, EffectStep(One<DamageBreakdown>(ev2), MoveSrc("double_edge")), "크리마다 +2(복사한 효과 — 출처는 그 기술)");

            // 한도 있는 효과(quick_attack 「once until the user switches out」)를 복사하면 쓴 포켓몬의 한도
            var u = Duel();
            u.S.Pokemon[u.A].Moves[1] = Mv("quick_attack");
            Turn(u, "kinesis");
            Pick(u, Mv("quick_attack"));
            var second = Battles.Only(u.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, second.Count, "둘째 기술 — 「may」");
            Pick(u, Mv("double_edge"));
            int id = ctx.Effects.OfMove(Mv("quick_attack"))[0];
            Assert.IsTrue(EffectRules.Spent(u.S, ctx.Effects.Effects[id], 0, u.A), "복사한 효과의 한도를 그 포켓몬이 썼다(원작 커뮤니티 확인)");
        }

        // ── 미분류 ──

        [Test]
        public void M5_효과_sacred_sword_굴린면을바꾸거나지우지못한다_디아루가도없다()
        {
            var t = Duel();
            t.S.Pokemon[t.A].Status = StatusEffect.Paralysed;
            t.S.Pokemon[t.A].ParalysisPending = true;
            t.S.Pokemon[t.D].Status = StatusEffect.Drowsy;
            var ev = Turn(t, "sacred_sword", F(R, R, R, S));
            Assert.AreEqual(0, All<DiceFacesChanged>(ev).Count, "마비의 지우기 · 졸림의 바꾸기가 없다");
            Assert.IsFalse(t.S.Pokemon[t.A].ParalysisPending, "바로 다음 공격에 쓴 것이다");
            Assert.AreEqual(3, One<DamageBreakdown>(ev).Steps[0].Value, "주사위 그대로(일반 셋)");

            // 디아루가 재굴림도 낼 수 없다(원작 커뮤니티 확인) — 같은 굴림의 다른 기술은 묻는다
            foreach (string move in new[] { "sacred_sword", "rollout" })
            {
                var u = Duel();
                u.S.Seats[0].FateHand.Add(FateRules.CardKey(ctx, FateCard.Dialga));
                Turn(u, move, move == "rollout" ? F(R, R, R) : F(R, R, R, S));
                List<ChoiceAnswer> after = null;
                for (int i = 0; i < 4 && u.S.Pending.Count == 1 && u.S.Pending[0].Sub == ChoiceSub.Fate; i++)
                {
                    var c = u.S.Pending[0];
                    if (c.Window == Window.AfterRoll) after = Scene.Legal(u.S, ctx, c);
                    Answer(u, c, ChoiceAnswer.Of(AnswerKind.Decline));
                }
                Assert.IsNotNull(after, "굴린 뒤 창(손패가 있으면 연다 — 손패를 감춘다)");
                Assert.AreEqual(move == "sacred_sword" ? 1 : 1 + 3, after.Count, move == "sacred_sword" ? "안 냄뿐 — 다시 굴릴 주사위가 없다" : "주사위 셋 중 하나를 다시 굴린다");
            }
        }

        [TestCase("giga_drain")]
        [TestCase("power_whip")]
        public void M5_효과_준피해의절반을회복한다(string move)
        {
            var t = Duel();
            t.S.Pokemon[t.A].Damage = 10;
            var ev = Turn(t, move, move == "giga_drain" ? F(C, C, R, R) : F(C, C, R, R, R));
            int dealt = One<DamageBreakdown>(ev).Final;
            Assert.Greater(dealt, 1);
            Assert.AreEqual(14 - 10 + dealt / 2, Battles.Hp(t.S, ctx, t.A), "준 피해의 절반(내림)");
            if (move != "power_whip") return;
            Turn(t, Plain);
            ev = Turn(t, move, F(C, C, C));
            Assert.AreEqual(3, One<DiceRolled>(ev).Power, "직전 턴에 썼으면 공격력 3");
        }

        [Test]
        public void M5_효과_giga_drain_대상의남은체력보다많이주어도준피해로회복_독이면0()
        {
            var t = Duel();
            t.S.Pokemon[t.A].Damage = 10;
            Battles.SetHp(t.S, ctx, t.D, 1);
            var ev = Turn(t, "giga_drain", F(C, C, R, R));
            Assert.AreEqual(4 + One<DamageBreakdown>(ev).Final / 2, Battles.Hp(t.S, ctx, t.A), "원작 커뮤니티 확인");
            var u = Duel();
            u.S.Pokemon[u.A].Damage = 10;
            u.S.Pokemon[u.A].Status = StatusEffect.Poisoned;
            Turn(u, "giga_drain", F(C, C, R, R));
            Assert.AreEqual(4 - StatusRules.PoisonDamage, Battles.Hp(u.S, ctx, u.A), "독이면 회복 0(턴 끝 독 2)");
        }

        [Test]
        public void M5_효과_soft_boiled_4회복_이어쓰면공격력4에1회복()
        {
            var t = Duel();
            t.S.Pokemon[t.A].Damage = 10;
            var ev = Turn(t, "soft_boiled", F(S));
            Assert.AreEqual(1, One<DiceRolled>(ev).Power);
            Assert.AreEqual(8, Battles.Hp(t.S, ctx, t.A), "4 회복");
            Turn(t, Plain, F(S, S));
            ev = Turn(t, "soft_boiled", F(S, S, S, S));
            Assert.AreEqual(4, One<DiceRolled>(ev).Power, "직전 턴에 썼으면 공격력 4");
            Assert.AreEqual(9, Battles.Hp(t.S, ctx, t.A), "1만 회복");
        }

        [Test]
        public void M5_효과_future_sight_굴리기전에맞히고틀리면3덜준다()
        {
            var t = Duel();
            Turn(t, "future_sight");
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { MoveHandlers.GuessMore, MoveHandlers.GuessLess, MoveHandlers.GuessEqual }, c.Options);
            var copy = t.S.Clone();
            var faces = F(C, C, S, R, R, R, R);                               // 크리 2 > 심볼 1
            var ev = Pick(t, MoveHandlers.GuessMore, faces);
            Assert.AreEqual(0, EffectStep(One<DamageBreakdown>(ev), MoveSrc("future_sight")), "맞혔다");
            t.S = copy;
            ev = Pick(t, MoveHandlers.GuessEqual, faces);
            Assert.AreEqual(MoveHandlers.FutureSightMiss, EffectStep(One<DamageBreakdown>(ev), MoveSrc("future_sight")), "틀렸다: 효과 피해 −3");
        }

        // ── 둘째 기술 ──

        [TestCase("quick_attack")]
        [TestCase("mach_punch")]
        [TestCase("shadow_sneak")]
        [TestCase("extreme_speed")]
        [TestCase("bullet_punch")]
        [TestCase("aqua_jet")]
        public void M5_효과_둘째기술_같은배틀턴에한번더_나갈때까지한번(string move)
        {
            var t = Duel();
            t.S.Pokemon[t.A].Moves[1] = Mv(Plain);
            var ev = Turn(t, move, F(R, R));
            Assert.AreEqual(1, All<DiceRolled>(ev).Count);
            int first = One<DamageBreakdown>(ev).Final;
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, c.Seat);
            Assert.AreEqual(0, c.Count, "「The user may」 — 안 씀이 첫 답");
            CollectionAssert.AreEqual(new[] { Mv("double_edge"), Mv(move), Mv(Plain) }, c.Options, "그 포켓몬의 기술(볼·기권은 없다 — 원작 커뮤니티 확인)");
            Assert.IsTrue(c.Source.Same(MoveSrc(move)));
            ev = Pick(t, Mv(Plain), F(R, R));
            Assert.AreEqual(1, All<DiceRolled>(ev, d => d.Side == 0).Count, "같은 배틀 턴에 둘째 공격(원작 커뮤니티 확인)");
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "그다음 상대 차례");
            Assert.AreEqual(23 - first - 4, Battles.Hp(t.S, ctx, t.D), "두 공격의 피해");
            Turn(t, Plain, F(S, S));
            ev = Turn(t, move);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "나갈 때까지 한 번 — 다시 묻지 않는다");
        }

        [Test]
        public void M5_효과_둘째기술_안쓰면한도가남는다_옵션이면쓴다()
        {
            foreach (bool spends in new[] { false, true })
            {
                var t = Duel(opt: new RuleOptions { SecondMoveDeclineSpends = spends });
                Turn(t, "quick_attack");
                Answer(t, Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick), ChoiceAnswer.Of(AnswerKind.Decline));
                Turn(t, Plain);
                Turn(t, "quick_attack");
                Assert.AreEqual(spends ? ChoiceKind.BattleAction : ChoiceKind.Reaction, t.S.Pending[0].Kind, spends ? "안 써도 한도를 썼다(옵션)" : "안 썼으니 다시 묻는다");
            }
        }

        [Test]
        public void M5_효과_둘째기술_첫공격으로쓰러뜨리면없다_옵션이면다음포켓몬에게()
        {
            var t = Duel(dBench: new[] { "399:0" });
            Battles.SetHp(t.S, ctx, t.D, 1);
            Turn(t, "quick_attack", F(R, R));
            Assert.IsFalse(t.S.Pending.Exists(p => p.Sub == ChoiceSub.EffectPick), "쓰러뜨렸으면 둘째 기술이 없다(기본)");
            int id = ctx.Effects.OfMove(Mv("quick_attack"))[0];
            Assert.IsFalse(EffectRules.Spent(t.S, ctx.Effects.Effects[id], 0, t.A), "쓰지 않았으니 한도가 남는다");

            var u = Duel(dBench: new[] { "399:0" }, opt: new RuleOptions { SecondMoveAfterKo = true });
            Battles.SetHp(u.S, ctx, u.D, 1);
            u.S.Pokemon[u.A].Moves[1] = Mv(Plain);
            var ev = Turn(u, "quick_attack", F(R, R));
            Assert.AreEqual(1, All<Fainted>(ev, f => f.PokemonId == u.D).Count, "기절을 먼저 알린다");
            Assert.AreEqual(u.DBench[0], u.S.Battle.Sides[1].Active, "남은 하나가 나온다");
            Pick(u, Mv(Plain), F(R, S));
            Assert.AreEqual(5 - 2, Battles.Hp(u.S, ctx, u.DBench[0]), "나온 포켓몬에게 둘째 공격(옵션)");
        }

        [Test]
        public void M5_효과_둘째기술_이어걸지못한다()
        {
            var t = Duel();
            t.S.Pokemon[t.A].Moves[1] = Mv("aqua_jet");
            Turn(t, "quick_attack");
            Pick(t, Mv("aqua_jet"));
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "둘째로 쓴 aqua_jet은 셋째를 주지 않는다(원작 커뮤니티 확인)");
            int id = ctx.Effects.OfMove(Mv("aqua_jet"))[0];
            Assert.IsFalse(EffectRules.Spent(t.S, ctx.Effects.Effects[id], 0, t.A), "효과를 쓰지 않았다");
        }

        [Test]
        public void M5_효과_둘째기술_턴끝은두공격뒤한번_옵션이면공격마다()
        {
            foreach (var mode in new[] { SecondMoveTurnEnd.Once, SecondMoveTurnEnd.EachAttack })
            {
                var t = Duel(opt: new RuleOptions { SecondMoveTurnEnd = mode });
                t.S.Pokemon[t.A].Status = StatusEffect.Poisoned;
                t.S.Pokemon[t.A].Moves[1] = Mv(Plain);
                var ev = Turn(t, "quick_attack", F(S, S));
                ev.AddRange(Pick(t, Mv(Plain), F(S, S)));
                int ticks = All<StatusTicked>(ev, x => x.PokemonId == t.A && x.Status == StatusEffect.Poisoned).Count;
                Assert.AreEqual(mode == SecondMoveTurnEnd.Once ? 1 : 2, ticks, mode.ToString());
                Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            }
        }

        [Test]
        public void M5_효과_둘째기술_다음턴의직전기술은둘다_옵션이면둘째만()
        {
            foreach (var mode in new[] { SecondMoveLastTurn.BothMoves, SecondMoveLastTurn.SecondOnly })
            {
                var t = Duel(opt: new RuleOptions { SecondMoveLastTurn = mode });
                t.S.Pokemon[t.A].Moves[1] = Mv(Plain);
                Turn(t, "quick_attack", F(S, S));
                Pick(t, Mv(Plain), F(S, S));
                Assert.AreEqual(Mv(Plain), t.S.Pokemon[t.A].LastMove);
                Assert.AreEqual(mode == SecondMoveLastTurn.BothMoves ? Mv("quick_attack") : -1, t.S.Pokemon[t.A].LastMove2);
                Turn(t, Plain, F(S, S));
                t.S.Pokemon[t.A].Status = StatusEffect.Confused;
                var ev = Turn(t, "quick_attack", F(S, S));
                bool hurt = All<StatusTicked>(ev, x => x.PokemonId == t.A && x.Status == StatusEffect.Confused).Count + All<HpChanged>(ev, x => x.PokemonId == t.A && x.Source.Kind == SourceKind.Status).Count > 0;
                Assert.AreEqual(mode == SecondMoveLastTurn.BothMoves, hurt, $"{mode}: 첫 기술(quick_attack)도 「used last turn」인가(원작 커뮤니티 확인)");
            }
        }

        [Test]
        public void M5_효과_둘째기술_가상쪽은묻지않고원칙기술()
        {
            var s = Battles.Game(ctx, 2);
            int me = Scene.Give(s, ctx, 0, "242:0");
            int g = Scene.Spawn(s, ctx, "799:0", SeatId.Galactic);
            var log = new EventLog();
            Battles.Start(s, ctx, log, BattleKind.Galactic, 0, SeatId.Galactic, -1, g);
            s.Pokemon[g].Moves[0] = Mv("quick_attack");
            s.Pokemon[me].Moves[0] = Mv(Plain);
            for (int i = 0; i < 4 && s.Pending.Count > 0 && s.Pending[0].Seat >= 0; i++)
                Battles.Answer(s, ctx, s.Pending[0], s.Pending[0].Kind == ChoiceKind.BattleAction ? Battles.Move(Mv(Plain)) : s.Pending[0].Default.Clone(), log);
            var ch = Battles.Only(s, ChoiceKind.BattleAction);
            Assert.Less(ch.Seat, 0, "갤럭시단 차례");
            int from = log.Events.Count;
            Battles.Answer(s, ctx, ch, Battles.Move(Mv("quick_attack")), log);
            var ev = log.Events.GetRange(from, log.Events.Count - from);
            Assert.AreEqual(2, All<DiceRolled>(ev, d => d.PokemonId == g).Count, "묻지 않고 둘째 기술을 쓴다(G16 가상 쪽)");
            Assert.IsFalse(s.Pending.Exists(p => p.Sub == ChoiceSub.EffectPick));
        }

        // 알파 스트라이크(p.6 「an attack that can be any learnable type」, rulings A26 ①)를 둘째 기술로 고르면 이어서 배울 수 있는 타입을 고르고 그 타입으로 나간다(M5 규칙 끝 검토 반영)
        [Test]
        public void A26_둘째기술로고른알파스트라이크도고른타입으로나간다()
        {
            var t = Duel("59:0");                                           // 윈디(시그니처 extreme_speed — 둘째 기술), 배운 기술 없음
            var a = t.S.Pokemon[t.A];
            a.HasLegendaryMove = true;
            a.Moves[0] = a.Moves[1] = -1;
            var types = new List<int>();
            LegendRules.AlphaTypes(ctx, a, types);
            Assert.Greater(types.Count, 1, "고를 타입이 둘 이상");
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            a.Fatigue = 0;
            Answer(t, ch, Battles.Move(Mv("extreme_speed")));
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.Contains(c.Options, BattleRules.AlphaStrike);
            Pick(t, BattleRules.AlphaStrike);
            var typed = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(types, typed.Options, "배울 수 있는 타입(카드 순서)");
            Assert.AreEqual(1, typed.Count, "타입은 꼭 고른다");
            var ev = Pick(t, types[1]);
            var rolled = All<DiceRolled>(ev, d => d.PokemonId == t.A);
            Assert.AreEqual(1, rolled.Count, "둘째 공격");
            Assert.AreEqual((PokemonType)types[1], rolled[0].Type, "고른 타입(타입없음이 아니다)");
        }

        // ── encore ──

        [Test]
        public void M5_효과_encore_다음공격으로쓰러지면마지막기술을쓰고쓰러진다()
        {
            var t = Duel(aBench: new[] { "399:0" });
            t.S.Pokemon[t.A].Moves[1] = Mv(Plain);
            Turn(t, "encore", F(S, S));
            Battles.SetHp(t.S, ctx, t.A, 1);
            var ev = Turn(t, Plain, F(R, R));                                 // 악식킹이 해피너스를 쓰러뜨린다
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, c.Seat);
            Assert.AreEqual(0, c.Count, "「may」");
            Assert.IsTrue(c.Source.Same(MoveSrc("encore")));
            Assert.AreEqual(0, All<Fainted>(ev).Count, "쓰러지기 전에 묻는다");
            int dBefore = Battles.Hp(t.S, ctx, t.D);
            ev = Pick(t, Mv(Plain), F(R, R));
            Assert.AreEqual(1, All<DiceRolled>(ev, d => d.PokemonId == t.A).Count, "마지막 기술");
            Assert.AreEqual(dBefore - 4, Battles.Hp(t.S, ctx, t.D));
            Assert.AreEqual(1, All<Fainted>(ev, f => f.PokemonId == t.A).Count, "그 뒤 쓰러진다(원작 커뮤니티 확인)");
            Assert.AreEqual(Mv(Plain), t.S.Pokemon[t.D].LastMove, "원래 차례의 직전 기술은 악식킹의 기술");

            // 회복하는 기술이어도 쓰러진다(원작 커뮤니티 확인)
            var w = Duel(aBench: new[] { "399:0" });
            w.S.Pokemon[w.A].Moves[1] = Mv("giga_drain");
            Turn(w, "encore", F(S, S));
            Battles.SetHp(w.S, ctx, w.A, 1);
            Turn(w, Plain, F(R, R));
            ev = Pick(w, Mv("giga_drain"), F(C, C, R, R));
            Assert.Greater(One<DamageBreakdown>(ev).Final, 1);
            Assert.AreEqual(1, All<Fainted>(ev, f => f.PokemonId == w.A).Count);
            Assert.AreEqual(0, Battles.Hp(w.S, ctx, w.A), "회복 0");

            // 안 쓰면 그대로 쓰러진다
            var u = Duel(aBench: new[] { "399:0" });
            Turn(u, "encore", F(S, S));
            Battles.SetHp(u.S, ctx, u.A, 1);
            Turn(u, Plain, F(R, R));
            ev = Answer(u, Battles.Only(u.S, ChoiceKind.Reaction, ChoiceSub.EffectPick), ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(0, All<DiceRolled>(ev).Count);
            Assert.AreEqual(1, All<Fainted>(ev, f => f.PokemonId == u.A).Count);
        }

        [Test]
        public void M5_효과_encore_쓰러지지않으면한번쓰고사라진다_공격한쪽도쓰러지면없다()
        {
            var t = Duel(aBench: new[] { "399:0" });
            Turn(t, "encore", F(S, S));
            Turn(t, Plain, F(S, S));                                          // 받았지만 쓰러지지 않았다 — 「next attacked」에 쓴 것
            Assert.IsFalse(t.S.Pokemon[t.A].Mods.Exists(m => m.Op == EffectOp.FinalMove));
            Turn(t, Plain, F(S, S));
            Battles.SetHp(t.S, ctx, t.A, 1);
            Turn(t, Plain, F(R, R));
            Assert.IsFalse(t.S.Pending.Exists(p => p.Sub == ChoiceSub.EffectPick), "보정이 남지 않았다");

            // 공격한 쪽도 같은 공격에 쓰러지면(돌진 반동) 마지막 기술이 없다(원작 커뮤니티 확인)
            var u = Duel(aBench: new[] { "399:0" }, dBench: new[] { "399:0" });
            Turn(u, "encore", F(S, S));
            Battles.SetHp(u.S, ctx, u.A, 1);
            Battles.SetHp(u.S, ctx, u.D, 2);
            Turn(u, "double_edge", F(C, S, S, S, S));                          // 크리 하나: 반동 2로 악식킹도 쓰러진다
            Assert.IsFalse(u.S.Pending.Exists(p => p.Sub == ChoiceSub.EffectPick));
            Assert.IsTrue(PokemonRules.IsFainted(ctx, u.S.Pokemon[u.A]));
        }

        // ── 복제 ──

        [Test]
        public void M5_효과_둘째기술을묻는동안복제해도같게이어간다()
        {
            var t = Duel();
            t.S.Pokemon[t.A].Moves[1] = Mv(Plain);
            Turn(t, "quick_attack", F(R, R));
            var copy = t.S.Clone();
            Assert.AreEqual(StateHash.Of(t.S), StateHash.Of(copy));
            var l2 = new EventLog();
            t.S.Rng = new Pcg32(SeedFor(F(R, R)));
            copy.Rng = new Pcg32(SeedFor(F(R, R)));
            Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Pick(Mv(Plain)), t.Log);
            Battles.Answer(copy, ctx, copy.Pending[0], ChoiceAnswer.Pick(Mv(Plain)), l2);
            Assert.AreEqual(StateHash.Of(t.S), StateHash.Of(copy));
        }
    }
}
