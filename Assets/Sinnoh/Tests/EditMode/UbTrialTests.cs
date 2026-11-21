using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 호수 시련 · 실버디(룰북 p.26, rulings A55 — M6 「호수 시련·실버디」). 실제 울트라 버스트 판에서 좌석을 호수 동굴·신오신전 칸에 세우고 찾아보기로 배틀을 연다.
    /// 내 포켓몬은 악식킹(799:0 — 체력 23 · chomp 악) 한 마리, 둘째 기술 칸에 x_scissor(벌레) — 둘 다 유크시(에스퍼·얼음)에게 효과가 굉장하다.
    /// </summary>
    public class UbTrialTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Mine = "799:0";
        const string Verity = "legendary_site.lake_verity_cavern", Acuity = "legendary_site.lake_acuity_cavern", Valor = "legendary_site.lake_valor_cavern", Temple = "legendary_site.temple_of_sinnoh";
        const int Emotion = (int)Shard.Emotion, Knowledge = (int)Shard.Knowledge, Willpower = (int)Shard.Willpower;

        int Mv(string key) => Scene.Move(ctx, key);

        sealed class Run
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int Seat, Tank, Foe;
        }

        /// <summary>울트라 버스트 판 — 차례 좌석의 파티를 악식킹 한 마리(chomp · x_scissor · tackle)로 바꾼다.</summary>
        Run Game(int seats = 1, ulong seed = 0)
        {
            var r = new Run { S = UbScenes.Started(ctx, seats, 3) };
            var s = r.S;
            r.Seat = s.ActiveSeat;
            int starter = s.Seats[r.Seat].Party[0];
            r.Tank = Scene.Give(s, ctx, r.Seat, Mine);
            MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, r.Seat, ActionKind.Release), r.Seat, starter);
            s.Pokemon[r.Tank].Moves[0] = Mv("x_scissor");
            s.Pokemon[r.Tank].Moves[1] = Mv("tackle");
            s.Rng.CopyFrom(new Pcg32(seed));
            return r;
        }

        /// <summary>차례 좌석을 그 고정물 칸에 세우고 찾아보기(Arg = 고정물)로 배틀을 연다.</summary>
        void Fight(Run r, string site, BattleKind kind = BattleKind.Trial)
        {
            var s = r.S;
            int f = UbScenes.Fixture(ctx, site);
            s.Seats[s.ActiveSeat].Cell = ctx.Fixtures.Fixtures[f].Cell;
            var search = ChoiceAnswer.Of(AnswerKind.Search, -1, f);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(search)), site + "에서 찾아보기");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, r.Log);
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(kind, s.Battle.Kind);
            r.Foe = s.Battle.Sides[1].Roster[0];
        }

        /// <summary>stop이 참이 될 때까지 답한다: 내 배틀 행동은 pick이 고른 답(없으면 chomp), 그 밖은 기본 답. 배틀이 끝나면 멈춘다.</summary>
        void Drive(Run r, Func<bool> stop, Func<List<ChoiceAnswer>, ChoiceAnswer> pick = null)
        {
            var s = r.S;
            for (int i = 0; i < 400; i++)
            {
                if (s.Battle == null || stop()) return;
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "열린 선택 없이 멈췄다");
                var a = c.Default.Clone();
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == r.Seat)
                {
                    var legal = Scene.Legal(s, ctx, c);
                    a = (pick?.Invoke(legal) ?? Use(legal, "chomp") ?? a).Clone();
                }
                Battles.Answer(s, ctx, c, a, r.Log);
            }
            Assert.Fail("끝나지 않는다");
        }

        ChoiceAnswer Use(List<ChoiceAnswer> legal, string move) => legal.Find(x => x.Kind == AnswerKind.UseMove && x.Target == Mv(move) && x.Arg < 0);

        /// <summary>지금 내 배틀 행동이 열려 있으면 그 합법 답, 아니면 null.</summary>
        List<ChoiceAnswer> MyActions(Run r)
        {
            var c = r.S.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.BattleAction && x.Seat == r.Seat);
            return c == null ? null : Scene.Legal(r.S, ctx, c);
        }

        /// <summary>내 기술을 n번 쓸 때까지(n번째 내 배틀 행동이 열릴 때 멈춘다 — 그 사이 상대 턴 포함).</summary>
        void Turns(Run r, int n, Func<int, string> move)
        {
            int used = 0;
            Drive(r, () => used >= n && MyActions(r) != null, legal =>
            {
                r.S.Pokemon[r.Tank].Damage = 0; // 내 포켓몬이 쓰러지지 않게(시련만 본다)
                r.S.Pokemon[r.Tank].Fatigue = 0;
                var a = Use(legal, move(used));
                used++;
                return a;
            });
        }

        int Hp(Run r) => PokemonRules.Hp(ctx, r.S.Pokemon[r.Foe]);

        static List<TrialProgress> Progress(Run r) => r.Log.OfType<TrialProgress>();

        // ── 감정(엠라이트) ──

        [Test]
        public void S17_7_감정_피로피해를입으면넘기고다음턴부터피해와포획이된다()
        {
            // p.26 「Trial of Emotion: Mesprit must take damage from battle fatigue」 · 「immune to capture attempts and all damage until the turn after their trials are overcome」
            var r = Game();
            r.S.Seats[r.Seat].Balls[(int)Ball.Regular] = 3;
            Fight(r, Verity);
            var s = r.S;
            Assert.AreEqual(Scene.Def(ctx, "481:0"), s.Pokemon[r.Foe].Def);
            Assert.IsTrue(UbTrial.Immune(s, ctx, s.Battle));
            s.Pokemon[r.Foe].Fatigue = FatigueRules.Max; // 다음 굴림에 피로 피해 3
            Drive(r, () => Progress(r).Count > 0 && MyActions(r) != null);
            Assert.IsNotNull(s.Battle);
            var p = Progress(r);
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual(Emotion, p[0].Lake);
            Assert.IsTrue(p[0].Done);
            Assert.AreEqual(r.Seat, p[0].Seat);
            Assert.IsTrue(s.Ub.TrialsOvercome[Emotion]);
            Assert.AreEqual(10, Hp(r), "넘긴 그 턴의 피로 피해도 면역(입었을 피해 — v1.4 「would take」)");
            var tired = r.Log.OfType<HpChanged>().Find(e => e.PokemonId == r.Foe && e.Source.Same(SourceRef.Rule(RuleSource.Fatigue)));
            Assert.IsNotNull(tired);
            Assert.AreEqual(tired.Before, tired.After);
            // 다음 턴(내 턴)부터 보통 전설: 볼을 던질 수 있고 피해가 들어간다
            Assert.IsFalse(UbTrial.Immune(s, ctx, s.Battle));
            Assert.IsTrue(MyActions(r).Exists(a => a.Kind == AnswerKind.ThrowBall));
            int before = r.Log.Events.Count;
            Drive(r, () => r.Log.Events.FindIndex(before, e => e is HpChanged h && h.PokemonId == r.Foe && h.After < h.Before) >= 0);
            Assert.GreaterOrEqual(r.Log.Events.FindIndex(before, e => e is HpChanged h && h.PokemonId == r.Foe && h.After < h.Before), 0, "면역이 풀려 피해가 들어간다");
        }

        [Test]
        public void S17_7_감정_못채우면피해도포획도안되지만상태이상은걸린다()
        {
            // 원작 커뮤니티 확인 — 면역은 포획 시도와 모든 피해뿐
            var r = Game();
            r.S.Seats[r.Seat].Balls[(int)Ball.Regular] = 3;
            Fight(r, Verity);
            var s = r.S;
            s.Pokemon[r.Foe].Status = StatusEffect.Poisoned;
            if (MyActions(r) == null) Drive(r, () => MyActions(r) != null);
            Assert.IsFalse(MyActions(r).Exists(a => a.Kind == AnswerKind.ThrowBall), "포획 시도에 면역");
            Turns(r, 2, i => "chomp");
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(0, Progress(r).Count);
            Assert.IsFalse(s.Ub.TrialsOvercome[Emotion]);
            Assert.AreEqual(10, Hp(r), "공격도 독도 피해가 없다");
            Assert.IsTrue(r.Log.OfType<DamageBreakdown>().Exists(e => e.TargetId == r.Foe), "공격은 했다");
            var ticks = r.Log.OfType<StatusTicked>().FindAll(e => e.PokemonId == r.Foe);
            Assert.Greater(ticks.Count, 0, "독은 걸려 있고 턴 끝에 돈다");
            foreach (var t in ticks) Assert.AreEqual(0, t.Effect);
            Assert.AreEqual(StatusEffect.Poisoned, s.Pokemon[r.Foe].Status);
            Assert.IsFalse(MyActions(r).Exists(a => a.Kind == AnswerKind.ThrowBall));
        }

        // ── 지식(유크시) ──

        [Test]
        public void S17_7_지식_효과가굉장한서로다른두타입으로공격하면넘긴다()
        {
            // p.26 「Trial of Knowledge: Uxie must be attacked with 2 different types that are super effective」 — 유크시 에스퍼·얼음: 악 +, 벌레 +
            var r = Game();
            Fight(r, Acuity);
            var s = r.S;
            Assert.AreEqual(Scene.Def(ctx, "480:0"), s.Pokemon[r.Foe].Def);
            Turns(r, 1, i => "chomp");
            var p = Progress(r);
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual(Knowledge, p[0].Lake);
            Assert.AreEqual(1, p[0].Count);
            Assert.AreEqual(2, p[0].Goal);
            Assert.IsFalse(p[0].Done);
            Assert.AreEqual(12, Hp(r));
            Turns(r, 1, i => "x_scissor");
            Assert.IsNotNull(s.Battle);
            p = Progress(r);
            Assert.AreEqual(2, p.Count);
            Assert.AreEqual(2, p[1].Count);
            Assert.IsTrue(p[1].Done);
            Assert.IsTrue(s.Ub.TrialsOvercome[Knowledge]);
            Assert.AreEqual(12, Hp(r), "넘긴 공격의 피해는 아직 면역(「until the turn after」)");
            Assert.IsFalse(UbTrial.Immune(s, ctx, s.Battle), "다음 내 턴 — 면역이 풀렸다");
        }

        [Test]
        public void S17_7_지식_같은타입이나효과가굉장하지않은타입으로는못넘긴다()
        {
            var r = Game();
            Fight(r, Acuity);
            var s = r.S;
            Turns(r, 4, i => i == 2 ? "tackle" : "chomp"); // 악 · 악 · 노말 · 악
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(1, Progress(r).Count, "같은 타입은 한 번만 센다 · 노말은 효과가 굉장하지 않다");
            Assert.IsFalse(s.Ub.TrialsOvercome[Knowledge]);
            Assert.IsTrue(UbTrial.Immune(s, ctx, s.Battle));
            Assert.AreEqual(12, Hp(r));
        }

        [Test]
        public void A55_시련전수호신은프레셔스볼도못잡는다_넘긴다음턴부터는된다()
        {
            // 감사 G4-1(11-20): 보통 볼 답은 면역을 보는데 gentleman R 프레셔스 볼 답(UseAbility CherishTarget)은 보지 않았다 — p.26 「immune to capture attempts」(rulings A55 ②)
            var r = Game();
            var s = r.S;
            s.Seats[r.Seat].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("gentleman");
            s.Seats[r.Seat].TrainerCardTier = TrainerRules.Gold;
            TrainerRules.Refresh(s, ctx);
            Assert.IsTrue(TrainerRules.CherishReady(s, ctx, r.Seat), "시험 준비: 프레셔스 볼이 손에");
            var cherish = ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.CherishTarget);
            Fight(r, Verity);
            Assert.IsTrue(UbTrial.Immune(s, ctx, s.Battle));
            if (MyActions(r) == null) Drive(r, () => MyActions(r) != null);
            Assert.IsFalse(ChoiceRules.Contains(MyActions(r), cherish), "시련 전 — 포획 면역");
            s.Pokemon[r.Foe].Fatigue = FatigueRules.Max;
            Drive(r, () => Progress(r).Count > 0 && MyActions(r) != null);
            Assert.IsFalse(UbTrial.Immune(s, ctx, s.Battle));
            Assert.IsTrue(ChoiceRules.Contains(MyActions(r), cherish), "넘긴 다음 턴부터");
        }

        // ── 의지(아그놈) ──

        /// <summary>아그놈과 내 턴 turns번: 내 턴마다 굴린 크리티컬 수(둘째 값 = 그 턴 뒤 시련을 넘겼나)를 적는다.</summary>
        List<int[]> Azelf(ulong seed, int turns, out Run run)
        {
            var r = Game(1, seed);
            run = r;
            Fight(r, Valor);
            var s = r.S;
            Battles.SetHp(s, ctx, r.Tank, 23);
            var rows = new List<int[]>();
            for (int t = 0; t < turns && s.Battle != null; t++)
            {
                if (MyActions(r) == null) Drive(r, () => MyActions(r) != null);
                if (s.Battle == null) break;
                s.Pokemon[r.Tank].Damage = 0; // 내 포켓몬이 쓰러지지 않게
                s.Pokemon[r.Tank].Fatigue = 0;
                int at = r.Log.Events.Count;
                Turns(r, 1, i => "tackle");
                int crits = 0;
                bool rolled = false;
                for (int i = at; i < r.Log.Events.Count; i++)
                    if (r.Log.Events[i] is DiceRolled d && d.PokemonId == r.Tank && !rolled)
                    {
                        rolled = true;
                        foreach (var f in d.Faces) if (f == AttackDieFace.CriticalHit) crits++;
                    }
                if (!rolled) break; // 혼란 등으로 못 쳤다
                rows.Add(new[] { crits, s.Ub.TrialsOvercome[Willpower] ? 1 : 0 });
                if (s.Ub.TrialsOvercome[Willpower]) break; // 넘긴 뒤는 보통 전설 배틀
            }
            return rows;
        }

        [Test]
        public void S17_7_의지_최근두턴의크리티컬합이2이상이면넘긴다()
        {
            // p.26 「Trial of Willpower: Azelf must be attacked with 2 or more critical hits over your last 2 turns」 — 한 턴에 2개도, 1개 + 1개(잇단 두 턴)도. 「2턴 연속 2개씩」이 아니다
            bool single = false, split = false;
            for (ulong seed = 0; seed < 300 && !(single && split); seed++)
            {
                var rows = Azelf(seed, 4, out var r);
                int done = rows.FindIndex(x => x[1] == 1);
                if (done < 0) continue;
                int now = rows[done][0], prev = done > 0 ? rows[done - 1][0] : 0;
                Assert.GreaterOrEqual(now + prev, 2, $"시드 {seed}: 넘긴 턴과 그 앞 턴의 크리티컬 합");
                Assert.Greater(now, 0, "넘긴 턴에 크리티컬이 있었다");
                for (int i = 1; i < done; i++) Assert.Less(rows[i][0] + rows[i - 1][0], 2, $"시드 {seed}: 더 일찍 넘겼어야 한다");
                if (done > 0) Assert.Less(rows[0][0], 2);
                var p = Progress(r);
                Assert.IsTrue(p[p.Count - 1].Done);
                Assert.AreEqual(Willpower, p[p.Count - 1].Lake);
                Assert.AreEqual(now + prev, p[p.Count - 1].Count);
                Assert.AreEqual(8, Hp(r), "넘긴 공격의 피해는 아직 면역");
                if (now >= 2) single = true;
                else if (now == 1 && prev == 1) split = true;
            }
            Assert.IsTrue(single, "한 턴에 크리티컬 2개로 넘긴 판");
            Assert.IsTrue(split, "잇단 두 턴에 1개씩으로 넘긴 판");
        }

        [Test]
        public void S17_7_의지_크리티컬이두턴넘게떨어져있으면못넘긴다()
        {
            // 1 · 0 · 1: 최근 두 턴의 합이 한 번도 2가 아니다
            bool found = false;
            for (ulong seed = 0; seed < 600 && !found; seed++)
            {
                var rows = Azelf(seed, 3, out var r);
                if (rows.Count < 3 || rows[0][0] != 1 || rows[1][0] != 0 || rows[2][0] != 1) continue;
                found = true;
                Assert.AreEqual(0, rows[2][1], "크리티컬 합은 2지만 최근 두 턴이 아니다");
                Assert.IsFalse(r.S.Ub.TrialsOvercome[Willpower]);
                Assert.IsTrue(UbTrial.Immune(r.S, ctx, r.S.Battle));
                Assert.AreEqual(8, Hp(r));
                var p = Progress(r);
                Assert.AreEqual(2, p.Count);
                Assert.AreEqual(1, p[0].Count);
                Assert.AreEqual(1, p[1].Count, "앞의 앞 턴 것은 세지 않는다");
                Assert.IsFalse(p[1].Done);
            }
            Assert.IsTrue(found, "크리티컬 1 · 0 · 1이 나오는 시드");
        }

        // ── 수호신 · 실버디 ──

        /// <summary>시련을 이미 넘긴 것으로 두고 그 호수의 수호신을 체력 1에서 쓰러뜨린다(이기는 시드를 고른다).</summary>
        Run Beat(GameState from, string site, int lake)
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = from.Clone();
                s.Rng.CopyFrom(new Pcg32(seed));
                var r = new Run { S = s, Seat = s.ActiveSeat };
                r.Tank = Array.Find(s.Seats[r.Seat].Party, p => p >= 0);
                Battles.SetHp(s, ctx, r.Tank, 23);
                s.Ub.TrialsOvercome[lake] = true;
                Fight(r, site);
                Assert.IsFalse(UbTrial.Immune(s, ctx, s.Battle), "시련은 판에 한 번 — 넘긴 뒤의 배틀에는 면역이 없다");
                Battles.SetHp(s, ctx, r.Foe, 1);
                Drive(r, () => false);
                while (s.Phase == Phase.Turn && s.Pending.Exists(c => !c.Answered && c.Kind == ChoiceKind.Reward)) // 기술 가르치기는 안 한다
                {
                    var c = s.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.Reward);
                    Battles.Answer(s, ctx, c, c.Default.Clone(), r.Log);
                }
                if (r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Win && e.Winner == 0)) return r;
            }
            Assert.Fail("수호신을 이기는 시드가 없다");
            return null;
        }

        [Test]
        public void S17_7_수호신마다이동력2()
        {
            // p.26 「After each Lake Guardian is defeated or captured, all trainers gain +2 movement per turn for the rest of the game」 · 처음은 3(원작 커뮤니티 확인)
            var g = Game(2);
            var s0 = g.S;
            Assert.AreEqual(MoveRules.BaseMovement, s0.Seats[g.Seat].MovesLeft);
            var r = Beat(s0, Verity, Emotion);
            var s = r.S;
            var down = r.Log.OfType<GuardianDown>();
            Assert.AreEqual(1, down.Count);
            Assert.AreEqual(Emotion, down[0].Lake);
            Assert.AreEqual(1, down[0].Total);
            Assert.AreEqual(r.Seat, down[0].Seat);
            Assert.IsTrue(s.Ub.GuardiansDefeated[Emotion]);
            Assert.AreEqual(UbBattle.RewardJp, r.Log.OfType<BattleEnded>()[0].Changes[0].After - r.Log.OfType<BattleEnded>()[0].Changes[0].Before, "보통 전설 보상(JP 6)");
            // 두 좌석 모두 다음 턴부터 3 + 2
            UbScenes.Attach(s, ctx, r.Log);
            Assert.AreNotEqual(r.Seat, s.ActiveSeat);
            Assert.AreEqual(MoveRules.BaseMovement + 2, s.Seats[s.ActiveSeat].MovesLeft);
            var set = r.Log.OfType<MoveAllowanceSet>();
            var last = set[set.Count - 1];
            Assert.AreEqual(MoveRules.BaseMovement + 2, last.Total);
            Assert.IsTrue(Array.Exists(last.Mods, m => m.Source.Same(SourceRef.Rule(RuleSource.LakeGuardian)) && m.Delta == 2));
            UbScenes.EndRound(s, ctx, r.Log);
            UbScenes.Attach(s, ctx, r.Log);
            Assert.AreEqual(r.Seat, s.ActiveSeat);
            Assert.AreEqual(MoveRules.BaseMovement + 2, s.Seats[r.Seat].MovesLeft);
            // 둘째 수호신 → +4
            var r2 = Beat(s, Valor, Willpower);
            Assert.AreEqual(2, r2.Log.OfType<GuardianDown>()[0].Total);
            UbScenes.Attach(r2.S, ctx, r2.Log);
            Assert.AreEqual(MoveRules.BaseMovement + 4, r2.S.Seats[r2.S.ActiveSeat].MovesLeft);
            // 쓰러뜨린 수호신은 전설 덱에 없어 다시 만나지 않는다
            int verity = UbScenes.Fixture(ctx, Verity);
            r2.S.Seats[r2.S.ActiveSeat].Cell = ctx.Fixtures.Fixtures[verity].Cell;
            Assert.IsFalse(Flow.MapLegal(r2.S, ctx).Exists(a => a.Kind == AnswerKind.Search && a.Arg == verity));
        }

        [Test]
        public void S17_7_수호신을잡아도이동력2()
        {
            // 「defeated or captured」 — 시련을 넘긴 뒤에는 볼을 던질 수 있다
            for (ulong seed = 0; seed < 300; seed++)
            {
                var r = Game(1, seed);
                var s = r.S;
                s.Seats[r.Seat].Balls[(int)Ball.Regular] = 1;
                s.Ub.TrialsOvercome[Knowledge] = true;
                Fight(r, Acuity);
                Battles.SetHp(s, ctx, r.Foe, 1);
                bool thrown = false;
                Drive(r, () => false, legal =>
                {
                    var ball = thrown ? null : legal.Find(a => a.Kind == AnswerKind.ThrowBall);
                    thrown |= ball != null;
                    return ball;
                });
                if (!r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Captured)) continue;
                Assert.AreEqual(1, r.Log.OfType<GuardianDown>().Count);
                Assert.AreEqual(Knowledge, r.Log.OfType<GuardianDown>()[0].Lake);
                Assert.IsTrue(s.Ub.GuardiansDefeated[Knowledge]);
                Assert.GreaterOrEqual(PokemonRules.PartyIndexOf(s.Seats[r.Seat], r.Foe), 0, "유크시가 파티에 들어왔다");
                return;
            }
            Assert.Fail("유크시를 잡는 시드가 없다");
        }

        [Test]
        public void S17_7_실버디_축복1()
        {
            // p.26 「Defeating or capturing this Pokémon grants 1 Blessing of Arceus」 · 판에 한 번 [가정 A55 ⑥](원작 커뮤니티 확인)
            int silvally = Scene.Def(ctx, UbTrial.Silvally);
            Run won = null;
            for (ulong seed = 0; seed < 200 && won == null; seed++)
            {
                var r = Game(1, seed);
                Fight(r, Temple, BattleKind.Legendary);
                Assert.AreEqual(silvally, r.S.Pokemon[r.Foe].Def);
                Assert.IsFalse(UbTrial.Immune(r.S, ctx, r.S.Battle), "실버디에게는 시련이 없다");
                Battles.SetHp(r.S, ctx, r.Foe, 1);
                Drive(r, () => false);
                if (r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Win && e.Winner == 0)) won = r;
            }
            Assert.IsNotNull(won, "실버디를 이기는 시드");
            var s = won.S;
            Assert.AreEqual(4, s.Ub.Blessings, "신인 3 + 1");
            var gained = won.Log.OfType<BlessingGained>();
            Assert.AreEqual(1, gained.Count);
            Assert.AreEqual(won.Seat, gained[0].Seat);
            Assert.AreEqual(4, gained[0].Total);
            Assert.IsTrue(gained[0].Source.Same(SourceRef.Rule(RuleSource.Silvally)));
            Assert.AreEqual(0, UbTrial.Down(s), "수호신이 아니다 — 이동력은 그대로");
            Assert.IsTrue(s.Ub.SilvallyBlessed);

            // 신오의 심장 판의 신오신전에는 실버디가 없다
            var hos = Flow.Started(ctx, 1, 3);
            int temple = UbScenes.Fixture(ctx, Temple);
            hos.Seats[0].Cell = ctx.Fixtures.Fixtures[temple].Cell;
            Assert.IsFalse(Flow.MapLegal(hos, ctx).Exists(a => a.Kind == AnswerKind.Search && a.Arg == temple));
        }

        [Test]
        public void S17_7_결정128_수호신과실버디피규어는울트라버스트판에서만_만날수있는동안서고_잡거나쓰러뜨리면사라지고_지면그대로()
        {
            // 사용자 결정 128(2026-11-20): 조건 유물이 없는 전설 장소 넷(호수 동굴 셋 · 신오신전)의 피규어 = 「만날 수 있는 동안 서 있고, 잡거나 쓰러뜨리면 사라진다」.
            // 이 넷은 울트라 버스트 판에서만 만난다(rulings A43 ④ — 신오의 심장 판에서는 세우지 않는다). 판정은 실제 상태(그 카드가 전설 덱에 있나)로 — LegendRules.SiteFigureShown
            string[] sites = { Verity, Acuity, Valor, Temple };
            var hos = Flow.Started(ctx, 1, 3);
            foreach (string site in sites)
                Assert.IsFalse(LegendRules.SiteFigureShown(hos, ctx, UbScenes.Fixture(ctx, site)), site + ": 신오의 심장 판에서는 세우지 않는다");

            var fresh = Game(1, 0);
            foreach (string site in sites)
                Assert.IsTrue(LegendRules.SiteFigureShown(fresh.S, ctx, UbScenes.Fixture(ctx, site)), site + ": 울트라 버스트 판 — 만날 수 있는 동안 선다");

            // 쓰러뜨림(버림 더미 — 아무도 다시 못 만남) → 사라진다 · 다른 장소는 그대로
            var down = Beat(fresh.S, Valor, Willpower);
            Assert.IsFalse(LegendRules.SiteFigureShown(down.S, ctx, UbScenes.Fixture(ctx, Valor)), "쓰러뜨린 아그놈의 피규어는 사라진다");
            Assert.IsTrue(LegendRules.SiteFigureShown(down.S, ctx, UbScenes.Fixture(ctx, Verity)), "다른 호수는 그대로");

            // 잡음(그 사람 파티) → 사라진다
            bool caught = false;
            for (ulong seed = 0; seed < 300 && !caught; seed++)
            {
                var r = Game(1, seed);
                r.S.Seats[r.Seat].Balls[(int)Ball.Regular] = 1;
                r.S.Ub.TrialsOvercome[Knowledge] = true;
                Fight(r, Acuity);
                Battles.SetHp(r.S, ctx, r.Foe, 1);
                bool thrown = false;
                Drive(r, () => false, legal =>
                {
                    var ball = thrown ? null : legal.Find(a => a.Kind == AnswerKind.ThrowBall);
                    thrown |= ball != null;
                    return ball;
                });
                if (!r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Captured)) continue;
                caught = true;
                Assert.IsFalse(LegendRules.SiteFigureShown(r.S, ctx, UbScenes.Fixture(ctx, Acuity)), "잡힌 유크시의 피규어는 사라진다");
            }
            Assert.IsTrue(caught, "유크시를 잡는 시드");

            // 짐(내 포켓몬이 모두 쓰러짐 — 전설은 덱으로 돌아간다) → 그대로 선다
            bool lost = false;
            for (ulong seed = 0; seed < 200 && !lost; seed++)
            {
                var r = Game(1, seed);
                Fight(r, Temple, BattleKind.Legendary);
                Battles.SetHp(r.S, ctx, r.Tank, 1);
                Drive(r, () => false, legal => { Battles.SetHp(r.S, ctx, r.Tank, 1); return null; });
                if (!r.Log.OfType<BattleEnded>().Exists(e => e.Result != BattleResult.Win && e.Result != BattleResult.Captured)) continue;
                lost = true;
                Assert.GreaterOrEqual(LegendRules.SiteCard(r.S, ctx, UbScenes.Fixture(ctx, Temple)), 0, "진 배틀의 실버디는 전설 덱으로");
                Assert.IsTrue(LegendRules.SiteFigureShown(r.S, ctx, UbScenes.Fixture(ctx, Temple)), "져도 피규어는 그대로 선다");
            }
            Assert.IsTrue(lost, "실버디에게 지는 시드");
        }
    }
}
