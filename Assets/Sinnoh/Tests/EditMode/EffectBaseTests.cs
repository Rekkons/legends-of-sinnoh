using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「효과 기반」(01 §7-6, rulings G16·G3): 포켓몬 지속 보정 저장소(다음 공격받을 때·다음 공격할 때·교체까지·배틀 끝까지), 한도(Limit — GameState.Used),
    /// 대상 없음(OnNoTarget), 선택을 여는 op(Ask — Wait), 처리기 등록부(Handler), effects.json의 새 칸, 효과 수 정본.
    /// 틀만 보는 시험이라, 몸통박치기 tackle · 독침 poison_sting의 실제 효과(M5 지속 보정 갈래가 옮겼다 — HoldEffectTests)를 뺀 Ctx(<see cref="ctx"/>)에 시험용 절을 끼워(<see cref="With"/>) 실제 배틀 흐름을 돈다.
    /// 포켓몬: 비버니 399(노말, 체력 5, 우선도 4, 몸통박치기 2) · 실쿤 266(벌레, 8, 2, 독침 1) · 모부기 387(풀, 6, 4, 잎날가르기 3).
    /// </summary>
    public class EffectBaseTests
    {
        /// <summary>실제 데이터 그대로(효과 수 정본).</summary>
        Ctx real;
        /// <summary>시험 기술 둘(tackle · poison_sting)의 실제 효과 절을 뺀 Ctx — 이 파일의 장면은 이 둘을 「효과 없는 기술」로 쓴다.</summary>
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            real = TestData.LoadCtx();
            var defs = new List<EffectDef>();
            int tackle = Scene.Move(real, "tackle"), sting = Scene.Move(real, "poison_sting");
            foreach (var e in real.Effects.DataEffects())
                if (e.Source.Id != tackle && e.Source.Id != sting)
                    defs.Add(new EffectDef(EffectDb.BuiltInCount + defs.Count, e.Source, e.Window, e.Role, e.When, e.Ops, e.Limit, e.OnNoTarget, e.Handler));
            ctx = new Ctx(real.Board, real.Cards, real.Decks, real.Fixtures, real.Items, new EffectDb(defs, real.Cards.Moves.Count));
        }

        int Mv(string key) => Scene.Move(ctx, key);

        SourceRef MoveSrc(string key) => new SourceRef(SourceKind.Move, Mv(key));

        /// <summary>시험용 절(창 BeforeDamage, 실행 절). ops가 null이면 처리기 절.</summary>
        sealed class Clause
        {
            public string Move;
            public EffectOpDef[] Ops;
            public EffectLimit Limit;
            public EffectNoTarget OnNoTarget;
            public string Handler;
        }

        static Clause On(string move, params EffectOpDef[] ops) => new Clause { Move = move, Ops = ops };

        /// <summary>실제 효과 표에 시험용 절을 기술 표 순서대로 끼운 Ctx(Id는 다시 매긴다). 반환 ids = 끼운 절의 효과 Id(clauses 순서).</summary>
        Ctx With(out int[] ids, IReadOnlyDictionary<string, EffectHandler> handlers, params Clause[] clauses)
        {
            var defs = new List<EffectDef>();
            var done = new bool[clauses.Length];
            ids = new int[clauses.Length];
            int[] outIds = ids;
            void Insert(int before)
            {
                for (int i = 0; i < clauses.Length; i++)
                {
                    int m = Mv(clauses[i].Move);
                    if (done[i] || m >= before) continue;
                    Assert.IsFalse(ctx.Effects.Implemented(m), $"{clauses[i].Move}: 효과가 없는 기술에만 끼운다");
                    done[i] = true;
                    outIds[i] = EffectDb.BuiltInCount + defs.Count;
                    defs.Add(new EffectDef(outIds[i], new SourceRef(SourceKind.Move, m), Window.BeforeDamage, EffectRole.Attacker, null, clauses[i].Ops,
                        clauses[i].Limit, clauses[i].OnNoTarget, clauses[i].Handler));
                }
            }
            foreach (var e in ctx.Effects.DataEffects())
            {
                Insert(e.Source.Id);
                defs.Add(new EffectDef(EffectDb.BuiltInCount + defs.Count, e.Source, e.Window, e.Role, e.When, e.Ops, e.Limit, e.OnNoTarget, e.Handler));
            }
            Insert(int.MaxValue);
            return new Ctx(ctx.Board, ctx.Cards, ctx.Decks, ctx.Fixtures, ctx.Items, new EffectDb(defs, ctx.Cards.Moves.Count, handlers));
        }

        Ctx With(params Clause[] clauses) => With(out _, null, clauses);

        static EffectOpDef Hold(EffectWho who, ModScope scope, int extra, EffectRole role = EffectRole.Attacker) =>
            new EffectOpDef(EffectOp.Hold, who, value: extra, scope: scope, role: role, held: EffectOp.ExtraDamage);

        /// <summary>열린 배틀 선택 하나에 답한다: 기술 고르기 = 출전 포켓몬의 시그니처, 우선권 토큰 = 쓴다, 그 밖 = pick 또는 Default. 그 답의 이벤트를 돌려준다.</summary>
        List<GameEvent> Step(GameState s, Ctx c, EventLog log, Func<PendingChoice, ChoiceAnswer> pick = null)
        {
            Assert.AreEqual(1, s.Pending.Count, "열린 선택 하나");
            var ch = s.Pending[0];
            ChoiceAnswer a = pick?.Invoke(ch);
            if (a == null)
            {
                if (ch.Kind == ChoiceKind.BattleAction)
                {
                    int k = ChoiceRules.BattleSideOf(s, ch.Seat);
                    a = Battles.Move(c.Cards.Pokemon[s.Pokemon[s.Battle.Sides[k].Active].Def].Signature);
                }
                else if (ch.Sub == ChoiceSub.InitiativeToken) a = ChoiceAnswer.Of(AnswerKind.Accept);
                else a = ch.Default.Clone();
            }
            int from = log.Events.Count;
            Battles.Answer(s, c, ch, a, log);
            return log.Events.GetRange(from, log.Events.Count - from);
        }

        /// <summary>그 포켓몬이 받은 공격의 피해 내역(순서대로)에서 출처가 src인 효과 단계 값(없으면 0).</summary>
        static List<int> EffectSteps(List<GameEvent> events, int target, SourceRef src)
        {
            var r = new List<int>();
            foreach (var e in events)
            {
                if (!(e is DamageBreakdown br) || br.TargetId != target) continue;
                int v = 0;
                foreach (var st in br.Steps)
                    if (st.Kind == DamageStepKind.Effect && st.Source.Same(src)) v += st.Value;
                r.Add(v);
            }
            return r;
        }

        /// <summary>좌석 0 비버니 대 야생 실쿤(비버니가 빠르다). 좌석 0의 기술 고르기가 열려 있다.</summary>
        GameState WildBattle(Ctx c, EventLog log, out int a, out int d)
        {
            var s = Battles.Game(c, 2);
            a = Scene.Give(s, c, 0, "399:0");
            d = Scene.Spawn(s, c, "266:0", SeatId.Wild);
            Battles.Wild(s, c, log, 0, d);
            Battles.Only(s, ChoiceKind.BattleAction);
            return s;
        }

        // ── 지속 보정 저장소(rulings G16) ──

        [Test]
        public void M5_지속보정_다음공격받을때_건공격은빼고다음공격한번만()
        {
            // 몸통박치기에 시험용 「대상이 다음에 공격받을 때 피해 −2」: 건 그 공격에는 적용되지 않고(Fresh), 다음 공격 한 번에만 적용된 뒤 사라진다
            var c = With(On("tackle", Hold(EffectWho.Target, ModScope.NextAttacked, -2)));
            var src = MoveSrc("tackle");
            var log = new EventLog();
            var s = WildBattle(c, log, out int a, out int d);

            var first = Step(s, c, log);                                    // 비버니 공격 → 실쿤 반격까지
            CollectionAssert.AreEqual(new[] { 0 }, EffectSteps(first, d, src), "건 그 공격에는 적용하지 않는다");
            Assert.AreEqual(1, s.Pokemon[d].Mods.Count);
            var m = s.Pokemon[d].Mods[0];
            Assert.AreEqual(ModScope.NextAttacked, m.Scope);
            Assert.AreEqual(EffectRole.Defender, m.Role);
            Assert.IsFalse(m.Fresh, "공격이 끝나면 다음 공격부터 적용된다");
            Assert.IsTrue(m.Source.Same(src));
            Assert.AreEqual(0, s.Pokemon[a].Mods.Count, "대상에게만 걸린다");

            var second = Step(s, c, log);
            CollectionAssert.AreEqual(new[] { -2 }, EffectSteps(second, d, src), "다음 공격에 적용");
            Assert.AreEqual(1, s.Pokemon[d].Mods.Count, "쓴 것은 사라지고 이번 공격이 건 것이 남는다");

            // 실쿤이 공격할 때는 실쿤의 「공격받을 때」 보정이 쓰이지 않는다(위에서 반격 뒤에도 남아 있었다)
            CollectionAssert.AreEqual(new[] { 0 }, EffectSteps(first, a, src));
        }

        [Test]
        public void M5_지속보정_다음공격할때_한번쓰면사라지고받는쪽것은남는다()
        {
            var log = new EventLog();
            var s = WildBattle(ctx, log, out int a, out int d);
            var mine = MoveSrc("bulk_up");
            var theirs = MoveSrc("play_rough");
            // 비버니: 다음 공격할 때 +1. 실쿤: 다음 공격할 때 −1(「When the target next attacks …」 꼴) — 공격받는 동안에는 쓰이지 않는다
            ModRules.Add(s, a, ModScope.NextAttack, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, 1, mine);
            ModRules.Add(s, d, ModScope.NextAttack, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, -1, theirs);
            Assert.AreEqual(EffectRole.Attacker, s.Pokemon[a].Mods[0].Role, "한 번짜리는 범위가 Role을 정한다");
            Assert.IsFalse(s.Pokemon[a].Mods[0].Fresh, "공격 밖에서 건 것은 바로 다음 공격부터");

            var first = Step(s, ctx, log);                                  // 비버니 공격(+1) → 실쿤 공격(−1)
            CollectionAssert.AreEqual(new[] { 1 }, EffectSteps(first, d, mine));
            CollectionAssert.AreEqual(new[] { -1 }, EffectSteps(first, a, theirs), "실쿤의 것은 비버니의 공격 동안 남아 있다가 자기 공격에 쓰였다");
            Assert.AreEqual(0, s.Pokemon[a].Mods.Count);
            Assert.AreEqual(0, s.Pokemon[d].Mods.Count);

            var second = Step(s, ctx, log);
            CollectionAssert.AreEqual(new[] { 0 }, EffectSteps(second, d, mine), "한 번뿐");
            CollectionAssert.AreEqual(new[] { 0 }, EffectSteps(second, a, theirs));
        }

        /// <summary>
        /// 트레이너전: 좌석 0 [비버니 a, 모부기 a2] 대 좌석 1 [실쿤 d1, 모부기 d2]. 비버니에 「교체까지 공격할 때 +1」(until) · 「배틀 끝까지 공격할 때 +1」(whole)을 걸고
        /// 두 번 공격해 두 번째에 실쿤을 쓰러뜨린다 → 좌석 0의 교체 창이 열린 상태. hits = 비버니의 두 공격 이벤트.
        /// 몸통박치기에는 「나갈 때까지 한 번」 한도의 시험용 절(대상 피로 +1)이 있다 — limitId = 그 효과 Id.
        /// </summary>
        GameState TrainerBattle(out Ctx c, EventLog log, out int a, out int a2, out int d1, out List<GameEvent>[] hits, out SourceRef until, out SourceRef whole, out int limitId)
        {
            c = With(out var ids, null, new Clause { Move = "tackle", Ops = new[] { new EffectOpDef(EffectOp.Fatigue, EffectWho.Target, value: 1) }, Limit = EffectLimit.UntilSwitch });
            limitId = ids[0];
            var s = Battles.Game(c, 2);
            a = Scene.Give(s, c, 0, "399:0");
            a2 = Scene.Give(s, c, 0, "387:0");
            d1 = Scene.Give(s, c, 1, "266:0");
            Scene.Give(s, c, 1, "387:0");
            Battles.Start(s, c, log, BattleKind.Trainer, 0, 1, -1);
            int first0 = a, first1 = d1;
            Step(s, c, log, ch => ChoiceAnswer.Pick(first0));               // 출전: 비버니
            Step(s, c, log, ch => ChoiceAnswer.Pick(first1));               // 출전: 실쿤
            until = MoveSrc("bulk_up");
            whole = MoveSrc("rollout");
            ModRules.Add(s, a, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.ExtraDamage, AttackDieFace.None, 1, until);
            ModRules.Add(s, a, ModScope.BattleEnd, EffectRole.Attacker, EffectOp.ExtraDamage, AttackDieFace.None, 2, whole); // 값이 같으면 같은 보정이라 겹치지 않는다(rulings G19)
            hits = new List<GameEvent>[2];
            Assert.AreEqual(0, s.Pending[0].Seat, "비버니(우선도 4)가 실쿤(2)보다 먼저");
            hits[0] = Step(s, c, log);                                      // 비버니 공격(최대 4 + 3 < 8)
            Step(s, c, log);                                                // 실쿤 공격(최대 2 < 5)
            Battles.SetHp(s, c, d1, 1);
            hits[1] = Step(s, c, log);                                      // 비버니 공격: +3이라 반드시 쓰러진다
            Battles.Only(s, ChoiceKind.SwitchWindow);
            return s;
        }

        [Test]
        public void M5_지속보정_교체까지_공격마다적용되고나가면사라진다()
        {
            var log = new EventLog();
            var s = TrainerBattle(out var c, log, out int a, out int a2, out int d1, out var hits, out var until, out _, out int limitId);
            CollectionAssert.AreEqual(new[] { 1 }, EffectSteps(hits[0], d1, until));
            CollectionAssert.AreEqual(new[] { 1 }, EffectSteps(hits[1], d1, until), "쓰여도 남는다");
            Assert.AreEqual(2, s.Pokemon[a].Mods.Count);
            // 「나갈 때까지 한 번」 한도: 첫 공격에만 피로 효과, 기록은 그 포켓몬에 묶인다
            Assert.AreEqual(1, hits[0].FindAll(e => e is FatigueChanged f && f.Cause == FatigueCause.Effect).Count);
            Assert.AreEqual(0, hits[1].FindAll(e => e is FatigueChanged f && f.Cause == FatigueCause.Effect).Count);
            Assert.IsTrue(s.Used.Exists(u => u.Kind == UsageKind.Effect && u.Key == limitId && u.Subject == a && u.Seat == 0));

            // 쓰러진 실쿤에 걸려 있던 것도 나가면서 사라진다(한 번짜리 포함)
            Assert.AreEqual(0, s.Pokemon[d1].Mods.Count);

            Step(s, c, log, ch => ChoiceAnswer.Pick(a2));                   // 비버니 → 모부기 교체
            Assert.AreEqual(a2, s.Battle.Sides[0].Active);
            Assert.IsFalse(s.Pokemon[a].Mods.Exists(m => m.Scope == ModScope.UntilSwitch), "교체하면 사라진다");
            Assert.IsFalse(s.Used.Exists(u => u.Kind == UsageKind.Effect && u.Key == limitId), "나가면 한도가 다시 열린다");
        }

        [Test]
        public void M5_지속보정_배틀끝까지_교체해도남고배틀이끝나면사라진다()
        {
            var log = new EventLog();
            var s = TrainerBattle(out var c, log, out int a, out int a2, out int d1, out var hits, out _, out var whole, out _);
            CollectionAssert.AreEqual(new[] { 2 }, EffectSteps(hits[0], d1, whole));
            CollectionAssert.AreEqual(new[] { 2 }, EffectSteps(hits[1], d1, whole));
            // 쓰러진 포켓몬의 「배틀 끝까지」도 남는다
            ModRules.Add(s, d1, ModScope.BattleEnd, EffectRole.Defender, EffectOp.ExtraDamage, AttackDieFace.None, 1, whole);

            Step(s, c, log, ch => ChoiceAnswer.Pick(a2));                   // 교체
            Assert.AreEqual(0, s.Pokemon[a].Mods.Count);
            Assert.AreEqual(1, s.Pokemon[a2].Mods.Count, "교체해도 남는다 — 다음에 나온 포켓몬에게 옮겨 간다(eternal_glade 「Works on your whole party」, rulings G19)");
            Assert.AreEqual(ModScope.BattleEnd, s.Pokemon[a2].Mods[0].Scope);

            Battles.Drive(s, c, log);
            Assert.IsNull(s.Battle, "배틀이 끝났다");
            foreach (var p in s.Pokemon) Assert.AreEqual(0, p.Mods.Count, "배틀이 끝나면 모두 사라진다");
            Assert.IsFalse(s.Used.Exists(u => u.Kind == UsageKind.Effect), "배틀 안 한도 기록도 지운다");
        }

        [Test]
        public void M5_지속보정_면마다피해와복제해시()
        {
            // 「다음에 공격받을 때 상태이상 심볼이 +1 피해」(overheat 꼴): 굴림의 심볼 수만큼
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "399:0");
            int d = Scene.Give(s, ctx, 1, "266:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            var src = MoveSrc("overheat");
            ModRules.Add(s, d, ModScope.NextAttacked, EffectRole.Defender, EffectOp.FaceDamage, AttackDieFace.StatusSymbol, 1, src);
            b.Move = Mv("tackle");
            var faces = new[] { AttackDieFace.StatusSymbol, AttackDieFace.StatusSymbol, AttackDieFace.RegularHit };
            var mods = new List<Mod>();
            ModRules.DamageMods(s, a, d, faces, mods);
            Assert.AreEqual(1, mods.Count);
            Assert.AreEqual(2, mods[0].Delta);
            Assert.IsTrue(mods[0].Source.Same(src));
            mods.Clear();
            ModRules.DamageMods(s, d, a, faces, mods);
            Assert.AreEqual(0, mods.Count, "실쿤이 공격할 때는 쓰이지 않는다");

            // 상태 안의 데이터: 복제하면 같고(따로 든다), 값이 바뀌면 해시가 바뀐다
            var copy = s.Clone();
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(copy));
            Assert.AreNotSame(s.Pokemon[d].Mods[0], copy.Pokemon[d].Mods[0]);
            copy.Pokemon[d].Mods[0].Value = 2;
            Assert.AreNotEqual(StateHash.Of(s), StateHash.Of(copy));
            Assert.AreEqual(1, s.Pokemon[d].Mods[0].Value);
            copy.Pokemon[d].Mods.Clear();
            Assert.AreNotEqual(StateHash.Of(s), StateHash.Of(copy));

            Assert.Throws<ArgumentException>(() => ModRules.Add(s, d, ModScope.NextAttack, EffectRole.Attacker, EffectOp.Double, AttackDieFace.None, 0, src), "훑는 곳이 없는 op는 걸지 못한다");
            Assert.Throws<ArgumentException>(() => ModRules.Add(s, d, ModScope.None, EffectRole.Attacker, EffectOp.ExtraDamage, AttackDieFace.None, 1, src));
        }

        // ── 한도(Limit) · 대상 없음(OnNoTarget, G3) ──

        /// <summary>손으로 놓은 트레이너전(좌석 0 비버니가 몸통박치기로 좌석 1 실쿤을 공격)에서 BeforeDamage 창의 자동 효과만 돈다.</summary>
        GameState Scene2(Ctx c, out int a, out int d)
        {
            var s = Scene.Game(c, 2);
            a = Scene.Give(s, c, 0, "399:0");
            d = Scene.Give(s, c, 1, "266:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.Move = Mv("tackle");
            b.Faces.Add(AttackDieFace.RegularHit);
            return s;
        }

        static void RunWindow(GameState s, Ctx c)
        {
            EffectRules.Trigger(s, c, Window.BeforeDamage);
            Assert.IsTrue(EffectRules.Run(s, c, Scene.Fx(s, new EventLog(), 0, ActionKind.BattleAction)));
            Assert.AreEqual(0, s.Stack.Count);
        }

        [Test]
        public void M5_한도_배틀당_배틀턴당_맵턴당_나갈때까지한번()
        {
            Clause Tired(EffectLimit limit) => new Clause { Move = "tackle", Ops = new[] { new EffectOpDef(EffectOp.Fatigue, EffectWho.Target, value: 1) }, Limit = limit };

            // 배틀당: 두 번째부터는 효과 없음
            var c = With(out var ids, null, Tired(EffectLimit.Battle));
            var s = Scene2(c, out int a, out int d);
            RunWindow(s, c);
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[d].Fatigue);
            Assert.AreEqual(1, s.Used.FindAll(u => u.Kind == UsageKind.Effect && u.Key == ids[0] && u.Seat == 0).Count, "기록은 GameState.Used에 한 줄");
            Assert.IsTrue(EffectRules.Spent(s, c.Effects.Effects[ids[0]], 0, a));
            Assert.IsFalse(EffectRules.Spent(s, c.Effects.Effects[ids[0]], 1, a), "좌석마다 따로");

            // 배틀 턴당: 배틀 턴이 바뀌면 다시
            c = With(out ids, null, Tired(EffectLimit.BattleTurn));
            s = Scene2(c, out a, out d);
            RunWindow(s, c);
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[d].Fatigue);
            s.Battle.Turn++;
            RunWindow(s, c);
            Assert.AreEqual(2, s.Pokemon[d].Fatigue);

            // 맵 턴당: 턴이 바뀌면 다시(기록은 턴 시작에 지워진다 — 여기서는 판정만)
            c = With(out ids, null, Tired(EffectLimit.Turn));
            s = Scene2(c, out a, out d);
            RunWindow(s, c);
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[d].Fatigue);
            s.TurnId++;
            RunWindow(s, c);
            Assert.AreEqual(2, s.Pokemon[d].Fatigue);

            // 나갈 때까지 한 번: 포켓몬마다 따로
            c = With(out ids, null, Tired(EffectLimit.UntilSwitch));
            s = Scene2(c, out a, out d);
            int a2 = Scene.Give(s, c, 0, "387:0");
            RunWindow(s, c);
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[d].Fatigue);
            s.Battle.Sides[0].Active = a2;
            RunWindow(s, c);
            Assert.AreEqual(2, s.Pokemon[d].Fatigue, "다른 포켓몬은 따로 센다");

            // 한도 없는 효과는 기록하지 않는다. 상시 절은 한도를 쓰지 못한다
            c = With(Tired(EffectLimit.None));
            s = Scene2(c, out a, out d);
            RunWindow(s, c);
            Assert.AreEqual(0, s.Used.Count);
            Assert.Throws<ArgumentException>(() => new EffectDef(5, MoveSrc("tackle"), Window.BeforeDamage, EffectRole.Attacker, null,
                new[] { new EffectOpDef(EffectOp.ExtraDamage, value: 1) }, EffectLimit.Battle));
        }

        [Test]
        public void M5_한도_턴시작에맵턴당기록이지워진다()
        {
            var c = With(out var ids, null, new Clause { Move = "tackle", Ops = new[] { new EffectOpDef(EffectOp.Fatigue, EffectWho.Target, value: 1) }, Limit = EffectLimit.Turn });
            var s = Battles.Game(c, 2);
            Scene.Give(s, c, 0, "399:0");
            Scene.Give(s, c, 1, "266:0");
            s.Used.Add(new UsageMark { Seat = 0, Kind = UsageKind.Effect, Key = ids[0], Scope = s.TurnId });
            TurnFlow.StartTurn(s, c, new Emitter(s, new EventLog()), 1, false);
            Assert.IsFalse(s.Used.Exists(u => u.Kind == UsageKind.Effect), "지난 턴의 기록은 지운다");
        }

        [Test]
        public void G3_대상없는절은효과없이넘어가고한도도쓰지않는다_SkipOp는그op만건너뛴다()
        {
            EffectOpDef[] Ops() => new[]
            {
                new EffectOpDef(EffectOp.Inflict, EffectWho.Target, StatusEffect.Poisoned),
                new EffectOpDef(EffectOp.Fatigue, EffectWho.User, value: 1),
            };
            // 기본(Skip): 대상이 없으면 절 전체가 넘어간다 — 자기 피로 +1도 없고 한도도 쓰지 않는다
            var c = With(out var ids, null, new Clause { Move = "tackle", Ops = Ops(), Limit = EffectLimit.Battle });
            var s = Scene2(c, out int a, out int d);
            s.Battle.Sides[1].Active = -1;
            RunWindow(s, c);
            Assert.AreEqual(0, s.Pokemon[a].Fatigue);
            Assert.AreEqual(0, s.Used.Count, "쓰지 않은 효과는 한도를 쓰지 않는다(G3)");
            s.Battle.Sides[1].Active = d;
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[a].Fatigue, "대상이 생기면 그대로 한다");
            Assert.AreEqual(StatusEffect.Poisoned, s.Pokemon[d].Status);

            // SkipOp: 대상 없는 op만 건너뛴다
            c = With(out ids, null, new Clause { Move = "tackle", Ops = Ops(), OnNoTarget = EffectNoTarget.SkipOp });
            s = Scene2(c, out a, out d);
            s.Battle.Sides[1].Active = -1;
            RunWindow(s, c);
            Assert.AreEqual(1, s.Pokemon[a].Fatigue);
            Assert.AreEqual(StatusEffect.None, s.Pokemon[d].Status);
        }

        // ── 선택을 여는 op(Wait) · 처리기 ──

        [Test]
        public void M5_선택을여는op_Ask는효과주인에게묻고안쓰면나머지를건너뛴다_복제해도이어간다()
        {
            // 몸통박치기에 시험용 「써도 된다: 대상을 마비에」(한도 배틀당) — 「안 씀」이면 한도를 쓰지 않는다
            var c = With(out var ids, null, new Clause
            {
                Move = "tackle", Limit = EffectLimit.Battle,
                Ops = new[] { new EffectOpDef(EffectOp.Ask), new EffectOpDef(EffectOp.Inflict, EffectWho.Target, StatusEffect.Paralysed) },
            });
            var log = new EventLog();
            var s = WildBattle(c, log, out int a, out int d);
            Step(s, c, log);
            var ask = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            Assert.AreEqual(0, ask.Seat);
            Assert.AreEqual(a, ask.Subject);
            Assert.IsTrue(ask.Source.Same(MoveSrc("tackle")));
            var legal = Scene.Legal(s, c, ask);
            Assert.AreEqual(2, legal.Count);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "첫 답 = 안 함(G9)");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Assert.IsTrue(EffectRules.Waiting(s));
            Assert.AreEqual(BattleStep.DamageWindow, s.Battle.Step, "피해를 적용하기 전에 멈춘다");

            // 멈춘 자리는 상태 안에 있다: 복제본에서 다른 답으로 이어 간다
            var other = s.Clone();
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(other));
            Step(s, c, log, ch => ChoiceAnswer.Of(AnswerKind.Accept));
            Assert.AreEqual(StatusEffect.Paralysed, s.Pokemon[d].Status);
            Assert.AreEqual(0, s.Stack.Count);
            Assert.IsTrue(s.Used.Exists(u => u.Kind == UsageKind.Effect && u.Key == ids[0]));
            Battles.Only(s, ChoiceKind.BattleAction);                       // 배틀이 이어져 다음 내 차례

            Step(other, c, new EventLog(), ch => ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(StatusEffect.None, other.Pokemon[d].Status);
            Assert.AreEqual(0, other.Stack.Count);
            Assert.IsFalse(other.Used.Exists(u => u.Kind == UsageKind.Effect), "안 쓴 효과는 한도를 쓰지 않는다");
            Battles.Only(other, ChoiceKind.BattleAction);

            // 이미 쓴 배틀: 다시 묻지 않는다
            Step(s, c, log);
            Battles.Only(s, ChoiceKind.BattleAction);
        }

        [Test]
        public void M5_선택을여는op_가상쪽은묻지않고쓴다()
        {
            // 야생 실쿤의 독침에 시험용 「써도 된다: 대상을 화상에」 — 야생은 묻지 않는다(G16 [가정])
            var c = With(new Clause { Move = "poison_sting", Ops = new[] { new EffectOpDef(EffectOp.Ask), new EffectOpDef(EffectOp.Inflict, EffectWho.Target, StatusEffect.Burned) } });
            var log = new EventLog();
            var s = WildBattle(c, log, out int a, out _);
            Step(s, c, log);
            Battles.Only(s, ChoiceKind.BattleAction);
            Assert.AreEqual(StatusEffect.Burned, s.Pokemon[a].Status);
        }

        [Test]
        public void M5_처리기_등록부의이름으로프레임을돌고선택을사이에두어도이어간다()
        {
            // 시험용 처리기: 걸음 0 = 「쓸까」를 묻고 답을 Locals[0]에 옮긴다 → 걸음 1 = 썼으면 대상 피로를 4로, 한도를 쓴다
            EffectStepResult Probe(GameState st, Ctx cx, Emitter fx, EffectFrame f)
            {
                if (f.StepIndex == 0)
                {
                    if (!EffectRules.HasAnswer(f))
                        return EffectRules.Wait(st, cx, fx, f, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectOption, Seat = f.Owner, Source = f.Source, Subject = f.User });
                    f.Locals[0] = (AnswerKind)f.Locals[EffectFrame.LocalAnswerKind] == AnswerKind.Accept ? 1 : 0;
                    f.StepIndex = 1;
                    return EffectStepResult.Continue;
                }
                if (f.Locals[0] == 1)
                {
                    st.Pokemon[f.Target].Fatigue = 4;
                    EffectRules.Spend(st, cx, f);
                }
                return EffectStepResult.Done;
            }
            var handlers = new Dictionary<string, EffectHandler> { { "probe", Probe } };
            var c = With(out var ids, handlers, new Clause { Move = "tackle", Handler = "probe", Limit = EffectLimit.Battle });
            Assert.IsFalse(c.Effects.Effects[ids[0]].IsStatic);
            Assert.AreEqual(0, c.Effects.Effects[ids[0]].Ops.Count);

            var log = new EventLog();
            var s = WildBattle(c, log, out _, out int d);
            Step(s, c, log);
            Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            var other = s.Clone();                                          // 처리기는 C# 지역 변수를 들지 않는다 — 복제본도 이어 간다
            Step(s, c, log, ch => ChoiceAnswer.Of(AnswerKind.Accept));
            // 대상 피로 4 → 실쿤이 반격하며 굴려 5
            Assert.AreEqual(5, s.Pokemon[d].Fatigue);
            Assert.IsTrue(s.Used.Exists(u => u.Kind == UsageKind.Effect && u.Key == ids[0]));
            Step(other, c, new EventLog(), ch => ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(1, other.Pokemon[d].Fatigue, "안 쓰면 반격의 굴림 피로뿐");
            Assert.IsTrue(EffectRules.Spent(s, c.Effects.Effects[ids[0]], 0, s.Battle.Sides[0].Active), "한도를 썼으니 처리기를 다시 부르지 않는다");

            // 등록부에 없는 이름은 표를 만들 때 거부한다. 기본 등록부는 운명 네 장(fate.*, rulings A24) · 나머지 op 갈래의 고르기 넷(rulings G20) · 폼 기술 셋(rulings A26) · 핸들러 갈래의 프레임 처리기 여덟(rulings G21 — 공격력 셈은 셈 처리기 EffectHandlers.Static)
            Assert.Throws<ArgumentException>(() => With(new Clause { Move = "tackle", Handler = "probe" }));
            var builtIn = new List<string>(FateRules.Register(new Dictionary<string, EffectHandler>()).Keys) { "type_learnable", "type_any", "belch", "photon_geyser", "hangry_wheel", "adaptive_cloak", "zen_restoration", "foul_play", "mimic", "metronome", "kinesis", "future_sight", "drain_half", "second_move", "encore", TrainerRules.AbilityHandler };
            CollectionAssert.AreEquivalent(builtIn, new List<string>(EffectHandlers.BuiltIn.Keys));
            Assert.Throws<ArgumentException>(() => new EffectDef(5, MoveSrc("tackle"), Window.BeforeDamage, EffectRole.Attacker, null,
                new[] { new EffectOpDef(EffectOp.Ask) }, handler: "probe"), "처리기 절은 op를 쓰지 않는다");
        }

        // ── 데이터 ──

        const string BugBuzzOps = "\"ops\":[{\"op\":\"Inflict\",\"who\":\"Target\",\"status\":\"Paralysed\",\"face\":null,\"value\":null,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null}],\"limit\":null,\"onNoTarget\":null,\"handler\":null}";

        static string Swap(string text, string from, string to)
        {
            int i = text.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
            return text.Substring(0, i) + to + text.Substring(i + from.Length);
        }

        [Test]
        public void M5_효과표는한도_대상없음_처리기_지속보정칸을읽고같은바이트로쓴다()
        {
            var db = TestData.LoadCardDb();
            string text = TestData.ReadText("effects.json");
            // 한도 + 대상 없음 + Ask + 지속 보정 둘(한 번짜리 · 교체까지)
            string ops = "\"ops\":[{\"op\":\"Ask\",\"who\":null,\"status\":null,\"face\":null,\"value\":null,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null},"
                + "{\"op\":\"Hold\",\"who\":\"Target\",\"status\":null,\"face\":\"StatusSymbol\",\"value\":-1,\"scope\":\"NextAttack\",\"role\":null,\"hold\":\"FaceDamage\",\"status2\":null},"
                + "{\"op\":\"Hold\",\"who\":\"User\",\"status\":null,\"face\":null,\"value\":2,\"scope\":\"UntilSwitch\",\"role\":\"Defender\",\"hold\":\"ExtraDamage\",\"status2\":null}],"
                + "\"limit\":\"UntilSwitch\",\"onNoTarget\":\"SkipOp\",\"handler\":null}";
            string changed = Swap(text, BugBuzzOps, ops);
            var effects = EffectDbLoader.Load(changed, db);
            Assert.AreEqual(changed, EffectDbLoader.Write(effects, db));
            EffectDef e = null;
            foreach (int id in effects.OfMove(db.MoveIds.IndexOf("bug_buzz"))) e = effects.Effects[id];
            Assert.AreEqual(EffectLimit.UntilSwitch, e.Limit);
            Assert.AreEqual(EffectNoTarget.SkipOp, e.OnNoTarget);
            Assert.AreEqual(EffectOp.Ask, e.Ops[0].Op);
            Assert.AreEqual(ModScope.NextAttack, e.Ops[1].Scope);
            Assert.AreEqual(EffectRole.Attacker, e.Ops[1].Role);
            Assert.AreEqual(EffectOp.FaceDamage, e.Ops[1].Held);
            Assert.AreEqual(AttackDieFace.StatusSymbol, e.Ops[1].Face);
            Assert.AreEqual(-1, e.Ops[1].Value);
            Assert.AreEqual(ModScope.UntilSwitch, e.Ops[2].Scope);
            Assert.AreEqual(EffectRole.Defender, e.Ops[2].Role);
            Assert.AreEqual(EffectOp.ExtraDamage, e.Ops[2].Held);

            // 처리기 절: ops는 빈 배열, 이름은 등록부에 있어야 한다
            string handled = Swap(text, BugBuzzOps, "\"ops\":[],\"limit\":\"Battle\",\"onNoTarget\":null,\"handler\":\"probe\"}");
            Assert.Throws<FormatException>(() => EffectDbLoader.Load(handled, db), "기본 등록부에 없는 이름");
            var table = new Dictionary<string, EffectHandler> { { "probe", (s, c, fx, f) => EffectStepResult.Done } };
            var withHandler = EffectDbLoader.Load(handled, db, table);
            Assert.AreEqual(handled, EffectDbLoader.Write(withHandler, db));

            void Rejected(string to, string why) => Assert.Throws<FormatException>(() => EffectDbLoader.Load(Swap(text, BugBuzzOps, to), db, table), why);
            string Row(string op, string tail = "\"limit\":null,\"onNoTarget\":null,\"handler\":null}") => "\"ops\":[" + op + "]," + tail;
            string HoldOp(string scope, string role, string hold, string face = "null", string value = "1", string who = "\"Target\"") =>
                $"{{\"op\":\"Hold\",\"who\":{who},\"status\":null,\"face\":{face},\"value\":{value},\"scope\":{scope},\"role\":{role},\"hold\":{hold},\"status2\":null}}";
            Assert.DoesNotThrow(() => EffectDbLoader.Load(Swap(text, BugBuzzOps, Row(HoldOp("\"BattleEnd\"", "\"Attacker\"", "\"ExtraDamage\""))), db));
            Rejected(Row(HoldOp("\"NextAttacked\"", "\"Defender\"", "\"ExtraDamage\"")), "한 번짜리는 role을 적지 않는다");
            Rejected(Row(HoldOp("\"UntilSwitch\"", "null", "\"ExtraDamage\"")), "교체까지는 role이 있어야 한다");
            Rejected(Row(HoldOp("\"NextAttack\"", "null", "\"Double\"")), "걸 수 없는 op");
            Rejected(Row(HoldOp("\"NextAttack\"", "null", "\"FaceDamage\"")), "FaceDamage는 face가 있어야 한다");
            Rejected(Row(HoldOp("\"NextAttack\"", "null", "\"ExtraDamage\"", "\"CriticalHit\"")), "ExtraDamage는 face를 쓰지 않는다");
            Rejected(Row(HoldOp("\"NextAttack\"", "null", "\"ExtraDamage\"", value: "0")), "값 0");
            Rejected(Row(HoldOp("\"NextAttack\"", "null", "\"ExtraDamage\"", who: "\"Party\"")), "Party에는 걸지 않는다");
            Rejected(Row(HoldOp("\"None\"", "null", "\"ExtraDamage\"")), "scope None");
            Rejected(Row(HoldOp("null", "null", "\"ExtraDamage\"")), "scope 없음");
            Rejected(BugBuzzOps.Replace("\"scope\":null", "\"scope\":\"NextAttack\""), "Hold가 아닌 op의 scope");
            Rejected(BugBuzzOps.Replace("\"limit\":null", "\"limit\":\"None\""), "limit None");
            Rejected(BugBuzzOps.Replace("\"limit\":null", "\"limit\":\"Weekly\""), "모르는 limit");
            Rejected(BugBuzzOps.Replace("\"onNoTarget\":null", "\"onNoTarget\":\"Skip\""), "기본값은 적지 않는다");
            Rejected(BugBuzzOps.Replace("\"handler\":null", "\"handler\":\"probe\""), "처리기 절은 ops를 비운다");
            Rejected(BugBuzzOps.Replace("\"handler\":null", "\"handler\":\"\""), "빈 처리기 이름");
            Rejected("\"ops\":[],\"limit\":null,\"onNoTarget\":null,\"handler\":null}", "처리기 없이 빈 ops");
            Rejected(BugBuzzOps.Replace(",\"handler\":null", ""), "빠진 칸");
            // 상시 절(x_scissor)은 한도를 쓰지 못한다
            const string XScissor = "\"op\":\"FaceDamage\",\"who\":null,\"status\":null,\"face\":\"CriticalHit\",\"value\":1,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null}],\"limit\":null";
            Assert.Throws<FormatException>(() => EffectDbLoader.Load(Swap(text, XScissor, XScissor.Replace("\"limit\":null", "\"limit\":\"Battle\"")), db));

            // M5 지속 보정 갈래(rulings G19): 걸 것마다 쓰는 칸 · ExtendMods · sameMoveAsLast false
            string Hold2(string scope, string role, string hold, string face = "null", string value = "null", string status = "null", string who = "\"User\"") =>
                $"{{\"op\":\"Hold\",\"who\":{who},\"status\":{status},\"face\":{face},\"value\":{value},\"scope\":{scope},\"role\":{role},\"hold\":{hold},\"status2\":null}}";
            void Accepted(string op)
            {
                string t = Swap(text, BugBuzzOps, Row(op));
                Assert.AreEqual(t, EffectDbLoader.Write(EffectDbLoader.Load(t, db), db), op);
            }
            Accepted(Hold2("\"NextAttacked\"", "null", "\"AddFaces\"", "\"RegularHit\"", "1", who: "\"Target\""));
            Accepted(Hold2("\"UntilSwitch\"", "\"Attacker\"", "\"AddFaces\"", "\"CriticalHit\"", "2"));
            Accepted(Hold2("\"NextAttack\"", "null", "\"RemoveFaces\"", "\"RegularHit\"", "2", who: "\"Target\""));
            Accepted(Hold2("\"NextAttacked\"", "null", "\"DamageCap\"", value: "1"));
            Accepted(Hold2("\"NextAttacked\"", "null", "\"FaceDamageOrCapture\"", "\"StatusSymbol\"", "1", who: "\"Target\""));
            Accepted(Hold2("\"UntilSwitch\"", "\"Defender\"", "\"Inflict\"", status: "\"Frozen\""));
            Accepted(Hold2("\"UntilSwitch\"", "null", "\"TurnEndHeal\"", value: "2"));
            Accepted(Hold2("\"UntilSwitch\"", "null", "\"StatusImmune\""));
            Accepted(Hold2("\"UntilSwitch\"", "null", "\"NoEscape\"", who: "\"Target\""));
            Accepted(Hold2("\"UntilSwitch\"", "null", "\"NoFormChange\"", who: "\"Target\""));
            Accepted(Hold2("\"BattleEnd\"", "null", "\"NoMoveSwitch\""));
            Accepted("{\"op\":\"ExtendMods\",\"who\":\"User\",\"status\":null,\"face\":null,\"value\":null,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null}");
            Rejected(Row(Hold2("\"NextAttacked\"", "null", "\"TurnEndHeal\"", value: "2")), "굴림과 얽히지 않은 보정은 한 번짜리로 걸지 않는다");
            Rejected(Row(Hold2("\"UntilSwitch\"", "\"Attacker\"", "\"StatusImmune\"")), "굴림과 얽히지 않은 보정은 role을 적지 않는다");
            Rejected(Row(Hold2("\"UntilSwitch\"", "null", "\"StatusImmune\"", value: "1")), "값을 쓰지 않는 보정");
            Rejected(Row(Hold2("\"UntilSwitch\"", "null", "\"AddFaces\"", "\"RegularHit\"", "1")), "굴림 보정을 교체까지로 걸면 role이 있어야 한다");
            Rejected(Row(Hold2("\"NextAttacked\"", "null", "\"AddFaces\"", value: "1")), "AddFaces는 face가 있어야 한다");
            Rejected(Row(Hold2("\"NextAttacked\"", "null", "\"AddFaces\"", "\"RegularHit\"", "-1")), "더할 면 수는 1 이상");
            Rejected(Row(Hold2("\"NextAttacked\"", "null", "\"RemoveFaces\"", "\"RegularHit\"")), "지울 면 수가 없다");
            Rejected(Row(Hold2("\"NextAttack\"", "null", "\"DamageCap\"", value: "1")), "피해 상한은 공격받을 때만");
            Rejected(Row(Hold2("\"UntilSwitch\"", "\"Attacker\"", "\"Inflict\"", status: "\"Frozen\"")), "공격받을 때 공격자에게만");
            Rejected(Row(Hold2("\"UntilSwitch\"", "\"Defender\"", "\"Inflict\"")), "걸 상태가 없다");
            Rejected(Row(Hold2("\"UntilSwitch\"", "null", "\"NoEscape\"", status: "\"Frozen\"")), "상태 칸은 Inflict만");
            Rejected(Row(Hold2("\"UntilSwitch\"", "null", "\"MoveLock\"")), "지닌물건의 기록은 데이터로 걸지 않는다");
            Rejected(Row("{\"op\":\"ExtendMods\",\"who\":\"Target\",\"status\":null,\"face\":null,\"value\":null,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null}"), "ExtendMods는 User만");
            Rejected(Row("{\"op\":\"AddFaces\",\"who\":null,\"status\":null,\"face\":\"RegularHit\",\"value\":1,\"scope\":null,\"role\":null,\"hold\":null,\"status2\":null}"), "걸 것은 op로 쓰지 않는다");
            // 조건 sameMoveAsLast: true = 직전 턴과 같은 기술, false = 직전 턴에 쓰지 않았다(disguise)
            const string NoWhen = "\"key\":\"move.bug_buzz\",\"window\":\"BeforeDamage\",\"when\":{\"symbols\":1,\"targetStatus\":null,\"targetType\":null,\"targetHeldItem\":null,\"sameMoveAsLast\":null,\"partyType\":null,\"knowsType\":null}";
            string notLast = Swap(text, NoWhen, NoWhen.Replace("\"symbols\":1", "\"symbols\":null").Replace("\"sameMoveAsLast\":null", "\"sameMoveAsLast\":false"));
            var loaded = EffectDbLoader.Load(notLast, db);
            Assert.AreEqual(notLast, EffectDbLoader.Write(loaded, db));
            foreach (int id in loaded.OfMove(db.MoveIds.IndexOf("bug_buzz"))) e = loaded.Effects[id];
            Assert.IsTrue(e.When.NotSameMoveAsLast);
            Assert.IsFalse(e.When.SameMoveAsLast);
            Assert.Throws<ArgumentException>(() => new EffectCondition(sameMoveAsLast: true, notSameMoveAsLast: true));
        }

        [Test]
        public void M5_효과수정본()
        {
            // 정본(01 §7-1, 2026-11-14): 기술 236장 · 서로 다른 효과 문구 147종 · 공격력 「?」 7장(xlsx attackStrength 빈칸) — 옛 「? 23장」은 틀린 수였다
            var ctx = real;   // 실제 효과 표(이 파일의 다른 시험은 시험 기술 둘의 효과를 뺀 것을 쓴다)
            var moves = ctx.Cards.Moves;
            Assert.AreEqual(236, moves.Count);
            var texts = new HashSet<string>();
            int unknown = 0;
            var todo = new List<string>();
            var todoTexts = new HashSet<string>();
            var doneTexts = new HashSet<string>();
            for (int i = 0; i < moves.Count; i++)
            {
                Assert.IsNotEmpty(moves[i].En, $"{moves[i].Key}: 효과 문구 없는 기술은 없다");
                texts.Add(moves[i].En);
                if (moves[i].AttackStrength == null) unknown++;
                if (ctx.Effects.Implemented(i)) doneTexts.Add(moves[i].En);
                else
                {
                    todo.Add(moves[i].Key);
                    todoTexts.Add(moves[i].En);
                }
            }
            Assert.AreEqual(147, texts.Count, "서로 다른 효과 문구");
            Assert.AreEqual(7, unknown, "공격력 「?」");
            Assert.AreEqual(texts.Count, todoTexts.Count + doneTexts.Count, "같은 문구의 기술은 함께 옮긴다(옮긴 문구와 남은 문구가 겹치지 않는다)");

            // 미구현 목록 = build가 쓴 Tools/data/reports/effects-todo.tsv(머리 줄 + 기술 표 순서). 행 수와 키가 엔진의 효과 표와 같아야 한다
            var lines = new List<string>(File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/data/reports/effects-todo.tsv"), new System.Text.UTF8Encoding(false)).Split('\n'));
            Assert.AreEqual("key\ttype\tattackStrength\ten", lines[0]);
            Assert.AreEqual("", lines[lines.Count - 1], "끝 줄바꿈");
            var keys = new List<string>();
            int unknownTodo = 0;
            for (int i = 1; i < lines.Count - 1; i++)
            {
                var cells = lines[i].Split('\t');
                Assert.AreEqual(4, cells.Length, lines[i]);
                keys.Add(cells[0]);
                if (cells[2] == "?") unknownTodo++;
            }
            CollectionAssert.AreEqual(todo, keys, "effects-todo.tsv가 효과 표와 다르다 — node Tools/data/build.mjs");
            Assert.AreEqual(moves.Count, keys.Count + (moves.Count - todo.Count));
            Assert.LessOrEqual(unknownTodo, unknown);
            TestContext.WriteLine($"기술 {moves.Count} = 옮김 {moves.Count - todo.Count}(문구 {doneTexts.Count}종) + 미구현 {todo.Count}(문구 {todoTexts.Count}종, 「?」 {unknownTodo}) · 절 {ctx.Effects.Effects.Count - EffectDb.BuiltInCount}");
        }
    }
}
