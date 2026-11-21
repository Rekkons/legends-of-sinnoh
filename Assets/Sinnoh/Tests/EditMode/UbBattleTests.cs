using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// UB 배틀과 프리즘 아머(룰북 p.24·p.25, rulings A53 — M6 「UB 배틀·프리즘 아머」). 실제 울트라 버스트 판에서 좌석을 UB 토큰 칸에 세우고 찾아보기로 배틀을 연다.
    /// 내 포켓몬은 오래 버티는 것(악식킹 799:0 — 체력 23)으로 바꿔 두고, 주사위가 갈리는 장면은 게임 난수를 다시 심어(<see cref="Seeded"/>) 원하는 결과가 나오는 시드를 고른다(결정적).
    /// </summary>
    public class UbBattleTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Foe = "797:0";  // 철화구야: 체력 21 · 시그니처 lift_off(강철)
        const string Mine = "799:0"; // 악식킹: 체력 23 · 시그니처 chomp(악) · 강철을 배울 수 있다

        int Mv(string key) => Scene.Move(ctx, key);

        /// <summary>울트라 버스트 판(혼자) — 차례 좌석의 파티를 악식킹 한 마리로 바꾸고 그 UB 토큰 칸에 세운다.</summary>
        GameState Game(out int seat, out int tank, out int token, int seats = 1, bool keepStarter = false)
        {
            var s = UbScenes.Started(ctx, seats, 3);
            seat = s.ActiveSeat;
            int starter = s.Seats[seat].Party[0];
            tank = Scene.Give(s, ctx, seat, Mine);
            if (!keepStarter) MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, seat, ActionKind.Release), seat, starter);
            int def = Scene.Def(ctx, Foe);
            token = s.Tokens.FindIndex(t => t.Kind == TokenKind.UltraBeast && t.Def == def && t.Cell >= 0);
            Assert.GreaterOrEqual(token, 0, "UB 토큰");
            s.Seats[seat].Cell = s.Tokens[token].Cell;
            return s;
        }

        /// <summary>UB 토큰 찾아보기로 배틀을 연다. UB 인스턴스를 돌려준다.</summary>
        int Fight(GameState s, int token, EventLog log)
        {
            var search = ChoiceAnswer.Of(AnswerKind.Search, token);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(search)), "UB 토큰 칸에서 찾아보기");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            Assert.IsNotNull(s.Battle);
            return s.Battle.Sides[1].Roster[0];
        }

        /// <summary>stop이 참이 될 때까지 답한다: 내 배틀 행동은 그 기술(forfeit면 기권이 되는 대로 기권), 그 밖(UB · 창 · 교체)은 기본 답.</summary>
        void Drive(GameState s, EventLog log, int seat, int move, Func<bool> stop, bool forfeit = false)
        {
            for (int i = 0; i < 400; i++)
            {
                if (stop()) return;
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "열린 선택 없이 멈췄다");
                var a = c.Default.Clone();
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == seat)
                {
                    var legal = Scene.Legal(s, ctx, c);
                    var give = forfeit ? legal.Find(x => x.Kind == AnswerKind.Forfeit) : null;
                    a = (give ?? legal.Find(x => x.Kind == AnswerKind.UseMove && x.Target == move && x.Arg < 0) ?? a).Clone();
                }
                Battles.Answer(s, ctx, c, a, log);
            }
            Assert.Fail("끝나지 않는다");
        }

        /// <summary>게임 난수를 0부터 다시 심어 가며 장면을 돌려 ok가 참인 첫 결과.</summary>
        T Seeded<T>(Func<ulong, T> run, Predicate<T> ok) where T : class
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var r = run(seed);
                if (ok(r)) return r;
            }
            Assert.Fail("원하는 주사위 결과가 나오는 시드가 없다");
            return null;
        }

        sealed class Run
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int Seat, Tank, Token, Ub;
        }

        /// <summary>프리즘 아머 n개를 울트라 비스트 쪽에 둔 판에서 내 포켓몬이 그 기술로 UB를 한 번 칠 때까지.</summary>
        Run FirstHit(ulong seed, int prism, string move)
        {
            var r = new Run();
            r.S = Game(out r.Seat, out r.Tank, out r.Token);
            r.S.Ub.PrismUltraBeast = prism;
            r.S.Ub.PrismUltraBurst = UbState.PrismArmourTotal - prism;
            r.S.Rng.CopyFrom(new Pcg32(seed));
            if (move != "chomp") r.S.Pokemon[r.Tank].Moves[0] = Mv(move);
            r.Ub = Fight(r.S, r.Token, r.Log);
            Drive(r.S, r.Log, r.Seat, Mv(move), () => r.Log.OfType<DamageBreakdown>().Exists(e => e.TargetId == r.Ub) && r.S.Pending.Count > 0 || r.S.Battle == null);
            return r;
        }

        [Test]
        public void S17_4_프리즘아머둘마다총피해1감소()
        {
            // p.24 「For every 2 Prism Armour tokens: Reduce the total damage taken per turn by 1.」 · 처음 9개는 모두 울트라 버스트 쪽 — 네크로즈마 −4, UB 0
            Assert.AreEqual(0, UbBattle.Reduction(0));
            Assert.AreEqual(0, UbBattle.Reduction(1));
            Assert.AreEqual(1, UbBattle.Reduction(2));
            Assert.AreEqual(2, UbBattle.Reduction(5));
            Assert.AreEqual(4, UbBattle.Reduction(9));
            var s = UbScenes.Started(ctx, 1, 3);
            Assert.AreEqual(0, UbBattle.Prism(s, new BattleState { Kind = BattleKind.UltraBeast }));
            Assert.AreEqual(9, UbBattle.Prism(s, new BattleState { Kind = BattleKind.Necrozma }));
            Assert.AreEqual(0, UbBattle.Prism(s, new BattleState { Kind = BattleKind.Legendary }), "그 밖의 배틀에는 없다");
            // 처음(UB 쪽 0개)에는 UB가 받는 피해가 줄지 않는다
            var r = Seeded(seed => FirstHit(seed, 0, "chomp"), x => x.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == x.Ub).Final >= 1);
            int final = r.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == r.Ub).Final;
            var hp = r.Log.OfType<HpChanged>().Find(e => e.PokemonId == r.Ub && e.Source.Kind == SourceKind.Move);
            Assert.AreEqual(final, hp.Before - hp.After);
            Assert.AreEqual(0, r.Log.OfType<PrismReduced>().Count);
        }

        [Test]
        public void S17_4_내차례의공격피해를줄인다()
        {
            // p.24 「Attack damage is the primary example of damage that may be reduced during your turns.」 — 2배·화상 뒤의 총 피해에서(FAQ p.13)
            var r = Seeded(seed => FirstHit(seed, 5, "chomp"), x => x.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == x.Ub).Final >= 3);
            int final = r.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == r.Ub).Final;
            var hp = r.Log.OfType<HpChanged>().Find(e => e.PokemonId == r.Ub && e.Source.Kind == SourceKind.Move);
            Assert.AreEqual(final - 2, hp.Before - hp.After, "5개 = 2 감소");
            var cut = r.Log.OfType<PrismReduced>().Find(e => e.PokemonId == r.Ub);
            Assert.AreEqual(2, cut.Amount);
            Assert.AreEqual(1, cut.Side);
            // 피해가 감소량보다 작으면 0까지만
            var small = Seeded(seed => FirstHit(seed, 9, "chomp"), x =>
            {
                int f = x.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == x.Ub).Final;
                return f >= 1 && f <= 3;
            });
            var none = small.Log.OfType<HpChanged>().Find(e => e.PokemonId == small.Ub && e.Source.Kind == SourceKind.Move);
            Assert.AreEqual(none.Before, none.After);
            Assert.AreEqual(small.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == small.Ub).Final, small.Log.OfType<PrismReduced>().Find(e => e.PokemonId == small.Ub).Amount);
        }

        [Test]
        public void S17_4_턴당한번_UB차례의피로와독을합쳐서줄인다()
        {
            // 원작 커뮤니티 확인
            var s = Game(out int seat, out int tank, out int token);
            s.Ub.PrismUltraBeast = 4;
            s.Ub.PrismUltraBurst = 5;
            var log = new EventLog();
            int ub = Fight(s, token, log);
            Assert.IsFalse(log.OfType<DiceRolled>().Exists(e => e.PokemonId == ub), "아직 UB는 굴리지 않았다");
            s.Pokemon[ub].Fatigue = FatigueRules.Max; // 다음 굴림에 피로 피해 3
            s.Pokemon[ub].Status = StatusEffect.Poisoned; // 턴 끝에 독 2
            Drive(s, log, seat, Mv("chomp"), () => log.OfType<StatusTicked>().Exists(e => e.PokemonId == ub) && s.Pending.Count > 0 || s.Battle == null);
            Assert.IsNotNull(s.Battle, "배틀이 이어진다");
            var events = log.Events;
            int from = events.FindIndex(e => e is HpChanged h && h.PokemonId == ub && h.Source.Same(SourceRef.Rule(RuleSource.Fatigue)));
            int to = events.FindIndex(e => e is StatusTicked t && t.PokemonId == ub);
            Assert.GreaterOrEqual(from, 0);
            Assert.Greater(to, from);
            var fatigue = (HpChanged)events[from];
            Assert.AreEqual(FatigueRules.Damage - 2, fatigue.Before - fatigue.After, "피로 3에서 2를 줄였다");
            var poison = (StatusTicked)events[to];
            Assert.AreEqual(-StatusRules.PoisonDamage, poison.Effect, "같은 턴의 몫을 이미 다 썼다 — 독 2는 그대로");
            int cut = 0;
            for (int i = from; i <= to + 1 && i < events.Count; i++)
                if (events[i] is PrismReduced p && p.PokemonId == ub) cut += p.Amount;
            Assert.AreEqual(2, cut, "5 피해에서 2 = 3");
        }

        Run Win(ulong seed, bool breach)
        {
            var r = new Run();
            r.S = Game(out r.Seat, out r.Tank, out r.Token);
            r.S.Rng.CopyFrom(new Pcg32(seed));
            if (breach)
            {
                r.S.Pokemon[r.Tank].Moves[0] = Mv("tackle");
                r.S.Pokemon[r.Tank].Breach[1] = BreachRules.CardOf(ctx, BreachKind.GravitationalDrag);
            }
            r.Ub = Fight(r.S, r.Token, r.Log);
            var foe = r.S.Pokemon[r.Ub];
            foe.Damage = PokemonRules.MaxHp(ctx, foe) - 1;
            Drive(r.S, r.Log, r.Seat, Mv("chomp"), () => r.S.Battle == null);
            return r;
        }

        static bool Won(Run r) => r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Win && e.Winner == 0);

        [Test]
        public void S17_4_UB를쓰러뜨리면프리즘하나가울트라비스트쪽으로간다()
        {
            // p.24 「When an Ultra Beast is defeated, move a Prism Armour token from the Ultra Burst side to the Ultra Beast side.」 · p.25 「treated like any other Legendary Pokémon for tactics and rewards」
            var r = Seeded(seed => Win(seed, false), Won);
            var s = r.S;
            Assert.AreEqual(-1, s.Tokens[r.Token].Cell, "UB 토큰이 맵에서 빠진다");
            Assert.IsTrue(r.Log.OfType<TokenRemoved>().Exists(e => e.TokenKind == TokenKind.UltraBeast));
            Assert.AreEqual(1, s.Ub.Defeated);
            Assert.AreEqual(8, s.Ub.PrismUltraBurst);
            Assert.AreEqual(1, s.Ub.PrismUltraBeast);
            var moved = r.Log.OfType<PrismMoved>()[0];
            CollectionAssert.AreEqual(new[] { 9, 0 }, moved.Before);
            CollectionAssert.AreEqual(new[] { 8, 1 }, moved.After);
            var jp = Array.Find(r.Log.OfType<BattleEnded>()[0].Changes, c => c.Res == ResKind.Jp);
            Assert.AreEqual(UbBattle.RewardJp, jp.After - jp.Before, "전설 보상 JP 6(원작 커뮤니티 확인");
            Assert.GreaterOrEqual(r.Log.OfType<StampGained>().Count, 1, "스탬프 2(UB 격파)");
            Assert.IsFalse(s.Pokemon.Exists(p => p.Def >= 0 && p.Owner == SeatId.UltraBeast), "UB 카드는 치운다");
            // UB의 기술을 가르칠 수 있다(강철 — 악식킹이 배울 수 있는 타입)
            var teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
            Assert.AreEqual(Mv("lift_off"), teach.Subject);
            Battles.Answer(s, ctx, teach, ChoiceAnswer.Pick(r.Tank, 0), r.Log);
            Assert.AreEqual(Mv("lift_off"), s.Pokemon[r.Tank].Moves[0]);
            // 턴이 끝난다 — 추가 턴은 없다(혼자: 라운드 끝 → 건너뛰기 물음 → UB 턴 → 다음 라운드)
            UbScenes.NoSkip(s, ctx, r.Log);
            Assert.AreEqual(1, r.Log.OfType<TurnEnded>().Count);
            Assert.IsFalse(r.Log.OfType<TurnStarted>().Exists(e => e.IsExtra));
            Assert.AreEqual(2, s.Round);
            // 남은 UB는 프리즘 아머 1개(아직 감소 0), 네크로즈마는 8개(−4)
            Assert.AreEqual(0, UbBattle.Reduction(UbBattle.Prism(s, new BattleState { Kind = BattleKind.UltraBeast })));
            Assert.AreEqual(4, UbBattle.Reduction(UbBattle.Prism(s, new BattleState { Kind = BattleKind.Necrozma })));
        }

        [Test]
        public void S17_5_UB격파뒤균열카드버리기를기술가르치기보다먼저묻는다()
        {
            // p.24 「when an Ultra Beast is defeated, all trainers may discard or move 1 Distortion Breach card」 · 원작 커뮤니티 확인
            var r = Seeded(seed => Win(seed, true), Won);
            var s = r.S;
            var release = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            Assert.AreEqual(BreachRules.Release, release.Subject);
            Assert.AreEqual(r.Seat, release.Seat);
            Battles.Answer(s, ctx, release, ChoiceAnswer.Pick(r.Tank, 1), r.Log);
            Assert.AreEqual(1, r.Log.OfType<BreachCardDiscarded>().Count);
            Assert.IsFalse(BreachRules.Any(s.Pokemon[r.Tank]));
            // 비운 칸에 UB의 기술을 가르칠 수 있다
            var teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
            Assert.IsTrue(Scene.Legal(s, ctx, teach).Exists(a => a.SameAs(ChoiceAnswer.Pick(r.Tank, 1))));
            Battles.Answer(s, ctx, teach, ChoiceAnswer.Pick(r.Tank, 1), r.Log);
            Assert.AreEqual(Mv("lift_off"), s.Pokemon[r.Tank].Moves[1]);
            Assert.AreEqual(1, r.Log.OfType<TurnEnded>().Count);
        }

        [Test]
        public void S17_6_UB는포획불가_도전마다완전회복()
        {
            // p.25 「cannot be captured」 · 「The Ultra Beasts and Necrozma are fully restored between battle attempts and phases.」
            var s = Game(out int seat, out int tank, out int token, 1, true);
            int starter = s.Seats[seat].Party[0];
            Battles.SetHp(s, ctx, starter, 1);
            s.Seats[seat].Balls[0] = 3;
            var log = new EventLog();
            var search = ChoiceAnswer.Of(AnswerKind.Search, token);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            var started = log.OfType<BattleStarted>()[0];
            Assert.AreEqual(BattleKind.UltraBeast, started.Kind);
            Assert.IsTrue(started.Uncapturable);
            Assert.AreEqual(SeatId.UltraBeast, started.Sides[1].Owner);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(starter), log);
            int ub = s.Battle.Sides[1].Roster[0];
            Assert.IsTrue(s.Pokemon[ub].HasLegendaryMove, "전설 특수기술(알파 스트라이크·비스트 부스트)");
            Assert.AreEqual(PokemonRules.MaxHp(ctx, s.Pokemon[ub]), log.OfType<SentIn>().Find(e => e.PokemonId == ub).Hp);
            s.Pokemon[ub].Damage = 5; // 이번 도전에서 깎아 둔다
            bool asked = false;
            Drive(s, log, seat, ctx.Cards.Pokemon[s.Pokemon[starter].Def].Signature, () =>
            {
                var c = s.Pending.Find(x => x.Kind == ChoiceKind.BattleAction && x.Seat == seat);
                if (c != null)
                {
                    asked = true;
                    Assert.IsFalse(Scene.Legal(s, ctx, c).Exists(a => a.Kind == AnswerKind.ThrowBall), "볼을 가졌어도 던질 수 없다");
                }
                return s.Battle == null;
            }, true);
            Assert.IsTrue(asked);
            var ended = log.OfType<BattleEnded>()[0];
            Assert.AreNotEqual(0, ended.Winner, "스타터가 쓰러진 뒤 기권했거나 졌다");
            Assert.GreaterOrEqual(s.Tokens[token].Cell, 0, "UB 토큰은 남는다");
            Assert.AreEqual(0, s.Ub.Defeated);
            Assert.AreEqual(UbState.PrismArmourTotal, s.Ub.PrismUltraBurst);
            Assert.IsFalse(s.Pokemon.Exists(p => p.Def >= 0 && p.Owner == SeatId.UltraBeast));
            // 다시 도전하면 완전 회복한 UB가 나온다(혼자: 턴 끝 → UB 턴 → 다음 내 턴 — 토큰이 옮겨 간 칸에서)
            UbScenes.Attach(s, ctx, log);
            Assert.IsTrue(BattleRules.HasUsable(s, ctx, seat));
            s.Seats[seat].Cell = s.Tokens[token].Cell;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            if (s.Battle.Sides[0].Active < 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, Scene.Legal(s, ctx, Battles.Only(s, ChoiceKind.SendIn))[0], log);
            int again = s.Battle.Sides[1].Roster[0];
            var sent = log.OfType<SentIn>().Find(e => e.PokemonId == again);
            Assert.AreEqual(sent.MaxHp, sent.Hp);
            Assert.AreEqual(0, s.Pokemon[again].Damage);
        }

        [Test]
        public void M6_IgnorePrism은프리즘을무시()
        {
            // multi_attack 「Ignores the effects of Prism Armour」 — 줄지 않고 그 턴의 몫도 쓰지 않는다
            var r = Seeded(seed => FirstHit(seed, 9, "multi_attack"), x => x.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == x.Ub).Final >= 1);
            int final = r.Log.OfType<DamageBreakdown>().Find(e => e.TargetId == r.Ub).Final;
            var hp = r.Log.OfType<HpChanged>().Find(e => e.PokemonId == r.Ub && e.Source.Kind == SourceKind.Move);
            Assert.AreEqual(final, hp.Before - hp.After);
            Assert.AreEqual(0, r.Log.OfType<PrismReduced>().FindAll(e => e.PokemonId == r.Ub).Count);
        }
    }
}
