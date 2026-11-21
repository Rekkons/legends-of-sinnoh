using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 네크로즈마 · 탈락 · 아르세우스의 축복(룰북 p.23·p.25, rulings A14 · A54 — M6 「네크로즈마·탈락·축복」). 실제 울트라 버스트 판에서 좌석을 네크로즈마 토큰 칸에 세우고 찾아보기로 배틀을 연다.
    /// 내 포켓몬은 악식킹(799:0 — 체력 23 · chomp 악) 한 마리. 이기는 장면은 네크로즈마의 체력을 1로 깎고 프리즘 아머를 치운 뒤 시드를 고르고, 지는 장면은 내 체력을 1로 깎는다(결정적).
    /// </summary>
    public class UbNecrozmaTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Mine = "799:0";

        int Mv(string key) => Scene.Move(ctx, key);

        static RuleOptions Difficulty(UbDifficulty d) => new RuleOptions { UbDifficulty = d };

        /// <summary>그 좌석의 파티를 악식킹 한 마리로 바꾼다(스타터는 놓아준다). 악식킹 인스턴스를 돌려준다.</summary>
        int Tank(GameState s, int seat)
        {
            int starter = s.Seats[seat].Party[0];
            int tank = Scene.Give(s, ctx, seat, Mine);
            MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, seat, ActionKind.Release), seat, starter);
            return tank;
        }

        static int NecrozmaToken(GameState s) => s.Tokens.FindIndex(t => t.Kind == TokenKind.Necrozma && t.Cell >= 0);

        /// <summary>차례 좌석을 네크로즈마 토큰 칸에 세우고 찾아보기로 배틀을 연다. 네크로즈마 인스턴스를 돌려준다.</summary>
        int Fight(GameState s, EventLog log)
        {
            int token = NecrozmaToken(s);
            Assert.GreaterOrEqual(token, 0, "네크로즈마 토큰");
            s.Seats[s.ActiveSeat].Cell = s.Tokens[token].Cell;
            var search = ChoiceAnswer.Of(AnswerKind.Search, token);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(search)), "네크로즈마 토큰 칸에서 찾아보기");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(BattleKind.Necrozma, s.Battle.Kind);
            return s.Battle.Sides[1].Roster[0];
        }

        /// <summary>배틀이 끝날 때까지 답한다: 그 좌석의 배틀 행동은 chomp(forfeit면 기권이 되는 대로 기권), 그 밖(네크로즈마 · 창 · 교체)은 기본 답.</summary>
        void Drive(GameState s, EventLog log, int seat, bool forfeit = false)
        {
            int move = Mv("chomp");
            for (int i = 0; i < 400; i++)
            {
                if (s.Battle == null) return;
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
            Assert.Fail("배틀이 끝나지 않는다");
        }

        /// <summary>차례 좌석이 네크로즈마에게 진다(파티의 체력을 모두 1로 깎고 싸운다).</summary>
        void Lose(GameState s, EventLog log)
        {
            int seat = s.ActiveSeat;
            foreach (int p in s.Seats[seat].Party)
                if (p >= 0) Battles.SetHp(s, ctx, p, 1);
            Fight(s, log);
            Drive(s, log, seat);
            Assert.IsFalse(log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Win && e.Winner == 0), "졌다");
        }

        sealed class Run
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int Seat, Tank, Foe, FoeDef, FoeHp;
        }

        /// <summary>그 페이즈의 네크로즈마를 이긴다: 프리즘 아머를 치우고 네크로즈마의 체력을 1로 깎은 뒤, 이기는 시드를 고른다.</summary>
        Run Win(int seats, int phase, Action<GameState, int> prepare = null)
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var r = new Run { S = UbScenes.Started(ctx, seats, 3) };
                var s = r.S;
                r.Seat = s.ActiveSeat;
                r.Tank = Tank(s, r.Seat);
                s.Ub.NecrozmaPhase = phase;
                s.Ub.PrismUltraBurst = 0;
                s.Ub.PrismUltraBeast = UbState.PrismArmourTotal;
                prepare?.Invoke(s, r.Tank);
                s.Rng.CopyFrom(new Pcg32(seed));
                r.Foe = Fight(s, r.Log);
                var foe = s.Pokemon[r.Foe];
                r.FoeDef = foe.Def;
                r.FoeHp = PokemonRules.Hp(ctx, foe);
                Battles.SetHp(s, ctx, r.Foe, 1);
                Drive(s, r.Log, r.Seat);
                if (r.Log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Win && e.Winner == 0)) return r;
            }
            Assert.Fail("네크로즈마를 이기는 시드가 없다");
            return null;
        }

        static SubmitResult Use(GameState s, Ctx ctx, int seat, Maintenance m, EventLog log) => Engine.Submit(s, ctx, seat, Command.Anytime(m), log);

        // ── 페이즈 ──

        [Test]
        public void S17_6_페이즈체력은24_30_30_36이고도전마다완전회복()
        {
            // p.25 「Necrozma has 3 phases」 · pokemon.json 800:1~4 · 「The Ultra Beasts and Necrozma are fully restored between battle attempts and phases」
            int[] health = { 24, 30, 30, 36 };
            string[] keys = { UbNecrozma.Phase1, UbNecrozma.DuskMane, UbNecrozma.DawnWings, UbNecrozma.Ultra };
            int[] cards = { UbNecrozma.Card(ctx, 1, -1), UbNecrozma.Card(ctx, 2, UbNecrozma.Sun), UbNecrozma.Card(ctx, 2, UbNecrozma.Moon), UbNecrozma.Card(ctx, 3, UbNecrozma.Moon) };
            for (int i = 0; i < cards.Length; i++)
            {
                Assert.AreEqual(Scene.Def(ctx, keys[i]), cards[i], keys[i]);
                Assert.AreEqual(health[i], ctx.Cards.Pokemon[cards[i]].Health, keys[i]);
            }

            var s = UbScenes.Started(ctx, 1, 3);
            int seat = s.ActiveSeat;
            Tank(s, seat);
            var log = new EventLog();
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Search && a.Target == NecrozmaToken(s)), "토큰 칸에 서야 한다");
            int foe = Fight(s, log);
            Assert.AreEqual(cards[0], s.Pokemon[foe].Def);
            Assert.AreEqual(24, PokemonRules.Hp(ctx, s.Pokemon[foe]));
            Assert.AreEqual(SeatId.UltraBeast, s.Battle.Sides[1].Owner);
            Assert.IsTrue(s.Battle.Uncapturable, "포획 불가(p.25)");
            Assert.IsTrue(s.Pokemon[foe].HasLegendaryMove, "전설 취급 — 알파 스트라이크·비스트 부스트");
            Assert.AreEqual(9, UbBattle.Prism(s, s.Battle), "프리즘 아머는 울트라 버스트 쪽 9개 = −4");
            Assert.AreEqual(0, log.OfType<CoinFlip>().Count, "1페이즈에는 동전이 없다");
            var act = s.Pending.Find(c => c.Kind == ChoiceKind.BattleAction && c.Seat == seat);
            if (act != null) Assert.IsFalse(Scene.Legal(s, ctx, act).Exists(a => a.Kind == AnswerKind.ThrowBall));
        }

        [Test]
        public void S17_6_2페이즈는썬문동전으로정하고다시던지지않는다()
        {
            // p.25 「Use the Sun/Moon coin to determine which form to battle」 — 처음 도전할 때 한 번 [가정 A54 ③]
            var seen = new HashSet<int>();
            for (ulong seed = 0; seed < 40 && seen.Count < 2; seed++)
            {
                var s = UbScenes.Started(ctx, 1, 3);
                Tank(s, s.ActiveSeat);
                s.Ub.NecrozmaPhase = 2;
                s.Rng.CopyFrom(new Pcg32(seed));
                var log = new EventLog();
                int foe = Fight(s, log);
                var flips = log.OfType<CoinFlip>();
                Assert.AreEqual(1, flips.Count);
                Assert.AreEqual(s.Ub.SunMoon, flips[0].Result);
                Assert.AreEqual(Scene.Def(ctx, flips[0].Result == UbNecrozma.Sun ? UbNecrozma.DuskMane : UbNecrozma.DawnWings), s.Pokemon[foe].Def);
                Assert.AreEqual(30, PokemonRules.Hp(ctx, s.Pokemon[foe]));
                Assert.Less(log.Events.IndexOf(flips[0]), log.Events.FindIndex(e => e is BattleStarted), "동전이 배틀보다 먼저");
                seen.Add(flips[0].Result);
            }
            Assert.AreEqual(2, seen.Count, "썬과 문이 모두 나온다");

            // 져서 탈락 → 축복으로 부활(혼자) → 다시 도전해도 같은 폼, 동전은 다시 던지지 않는다
            var g = UbScenes.Started(ctx, 1, 3);
            int tank = Tank(g, 0);
            g.Ub.NecrozmaPhase = 2;
            var first = new EventLog();
            Lose(g, first);
            int coin = g.Ub.SunMoon;
            Assert.GreaterOrEqual(coin, 0);
            UbScenes.Attach(g, ctx);
            Assert.AreEqual(Phase.Turn, g.Phase);
            Assert.AreEqual(PokemonRules.MaxHp(ctx, g.Pokemon[tank]), PokemonRules.Hp(ctx, g.Pokemon[tank]), "부활한 다음 턴 시작에 회복");
            var again = new EventLog();
            int foe2 = Fight(g, again);
            Assert.AreEqual(0, again.OfType<CoinFlip>().Count);
            Assert.AreEqual(UbNecrozma.Card(ctx, 2, coin), g.Pokemon[foe2].Def);
            Assert.AreEqual(30, PokemonRules.Hp(ctx, g.Pokemon[foe2]), "도전마다 완전 회복");
        }

        [Test]
        public void S17_6_페이즈를쓰러뜨리면배틀이끝나고영구히다음페이즈_보상없음()
        {
            // p.25 「When a phase is successfully defeated, the battle ends and Necrozma proceeds to its next phase permanently」 ·
            // 원작 커뮤니티 확인 · 균열 카드 버리기는 UB만(원작 커뮤니티 확인)
            int jp = -1;
            var r = Win(2, 1, (s, tank) =>
            {
                jp = s.Seats[s.ActiveSeat].Jp;
                s.Pokemon[tank].Breach[1] = BreachRules.CardOf(ctx, BreachKind.GravitationalDrag);
            });
            var st = r.S;
            Assert.AreEqual(24, r.FoeHp);
            Assert.IsNull(st.Battle);
            Assert.AreEqual(2, st.Ub.NecrozmaPhase);
            var phase = r.Log.OfType<NecrozmaPhase>();
            Assert.AreEqual(1, phase.Count);
            Assert.AreEqual(1, phase[0].Phase);
            Assert.AreEqual(2, phase[0].Next);
            Assert.AreEqual(r.Seat, phase[0].Seat);
            Assert.AreEqual(jp, st.Seats[r.Seat].Jp, "JP 없음");
            Assert.AreEqual(0, r.Log.OfType<StampGained>().Count, "스탬프 없음");
            Assert.IsFalse(r.Log.OfType<ChoiceRequested>().Exists(e => e.Sub == ChoiceSub.TeachMove || e.Sub == ChoiceSub.BreachAttach), "기술 가르치기·균열 카드 버리기 없음");
            Assert.AreEqual(0, st.Ub.Defeated, "UB 격파 수가 아니다");
            Assert.AreEqual(0, r.Log.OfType<PrismMoved>().Count);
            Assert.GreaterOrEqual(NecrozmaToken(st), 0, "토큰은 남는다");
            Assert.IsTrue(r.Log.OfType<TurnEnded>().Exists(e => e.Seat == r.Seat && e.Reason == TurnEndReason.Battle), "턴이 끝난다");
            Assert.AreEqual(Phase.Turn, st.Phase);
            Assert.AreNotEqual(r.Seat, st.ActiveSeat, "다음 좌석의 턴");
            Assert.IsFalse(st.Seats[r.Seat].Eliminated);
            Assert.AreEqual(-1, st.Pokemon[r.Foe].Def, "네크로즈마 인스턴스는 치운다");
        }

        [Test]
        public void S17_6_3페이즈를쓰러뜨리면모두승리()
        {
            // p.23 「Your only objective is to defeat Necrozma」 · 원작 커뮤니티 확인
            var r = Win(3, 3, (s, tank) => s.Seats[(s.ActiveSeat + 1) % 3].Eliminated = true);
            Assert.AreEqual(36, r.FoeHp);
            Assert.AreEqual(Scene.Def(ctx, UbNecrozma.Ultra), r.FoeDef);
            Assert.AreEqual(Phase.GameOver, r.S.Phase);
            Assert.AreEqual(UbResult.Won, r.S.Ub.Result);
            var end = r.Log.OfType<GameEnded>();
            Assert.AreEqual(1, end.Count);
            Assert.AreEqual(GameEndReason.NecrozmaDefeated, end[0].Reason);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, end[0].Winners, "모둠의 승리 — 탈락한 좌석도");
            Assert.AreEqual(-1, NecrozmaToken(r.S));
            Assert.AreEqual(0, r.Log.OfType<NecrozmaPhase>().Count);
            Assert.AreEqual(0, r.S.Pending.Count);
            Scene.Reject(Use(r.S, ctx, 0, UbBlessing.SkipCommand(), null), RejectReason.GameOver);
        }

        // ── 탈락 ──

        [Test]
        public void S17_6_네크로즈마에게지면탈락하고턴을건너뛴다()
        {
            // p.25 「If a trainer is defeated by Necrozma, they are eliminated from the game」
            var s = UbScenes.Started(ctx, 3, 3);
            int out0 = s.ActiveSeat;
            Assert.AreEqual(s.RoundStartSeat, out0);
            Tank(s, out0);
            var log = new EventLog();
            Lose(s, log);
            Assert.IsTrue(s.Seats[out0].Eliminated);
            var gone = log.OfType<Eliminated>();
            Assert.AreEqual(1, gone.Count);
            Assert.AreEqual(out0, gone[0].Seat);
            Assert.AreEqual(Phase.Turn, s.Phase);
            Assert.AreEqual(3, s.Ub.Blessings, "남은 좌석이 있으면 축복을 쓰지 않는다");
            Assert.AreEqual((out0 + 1) % 3, s.ActiveSeat);
            // 두 라운드 동안 탈락한 좌석(라운드 첫 좌석)의 턴은 없다 — 라운드는 남은 두 좌석으로 돈다
            var later = new EventLog();
            int round = s.Round;
            UbScenes.EndRound(s, ctx, later);
            UbScenes.EndRound(s, ctx, later);
            Assert.AreEqual(round + 2, s.Round);
            var turns = later.OfType<TurnStarted>();
            Assert.IsFalse(turns.Exists(e => e.Seat == out0), "탈락한 좌석의 턴");
            Assert.AreEqual(2, turns.FindAll(e => e.Seat == (out0 + 2) % 3).Count);
            Assert.AreEqual(2, later.OfType<UbTurnStarted>().Count, "라운드 끝마다 UB 턴");
            Assert.AreEqual((out0 + 1) % 3, s.ActiveSeat, "새 라운드는 탈락하지 않은 첫 좌석부터");
            // 탈락한 좌석은 같은 칸에 있어도 도전 대상이 아니다
            UbScenes.Attach(s, ctx);
            s.Seats[s.ActiveSeat].Cell = s.Seats[out0].Cell;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Challenge && a.Target == out0));
        }

        [Test]
        public void S17_6_기권해도탈락한다()
        {
            // 「defeated by Necrozma」 — 기권도 진 것이다 [가정 A54 ④](제작자 답 없음 · 원작 커뮤니티 확인)
            var s = UbScenes.Started(ctx, 2, 3);
            int seat = s.ActiveSeat;
            int starter = s.Seats[seat].Party[0];
            int tank = Scene.Give(s, ctx, seat, Mine);
            Battles.SetHp(s, ctx, starter, 1);
            var log = new EventLog();
            Fight(s, log);
            Drive(s, log, seat, true);
            Assert.IsTrue(log.OfType<BattleEnded>().Exists(e => e.Result == BattleResult.Forfeited), "기권으로 끝났다");
            Assert.Greater(PokemonRules.Hp(ctx, s.Pokemon[tank]), 0, "쓸 포켓몬이 남았다");
            Assert.IsTrue(s.Seats[seat].Eliminated);
            Assert.AreEqual(1, s.Ub.NecrozmaPhase);
        }

        [Test]
        public void M6_모두탈락이면패배()
        {
            // 축복이 없으면(마스터 0개 — A14) 부활할 길이 없다
            var s = UbScenes.Started(ctx, 1, 3, null, Difficulty(UbDifficulty.MasterTrainer));
            Assert.AreEqual(0, s.Ub.Blessings);
            Tank(s, 0);
            var log = new EventLog();
            Lose(s, log);
            Assert.IsTrue(s.Seats[0].Eliminated);
            Assert.AreEqual(Phase.GameOver, s.Phase);
            Assert.AreEqual(UbResult.Lost, s.Ub.Result);
            var end = log.OfType<GameEnded>();
            Assert.AreEqual(1, end.Count);
            Assert.AreEqual(GameEndReason.AllEliminated, end[0].Reason);
            Assert.AreEqual(0, end[0].Winners.Length);
            Assert.AreEqual(0, s.Pending.Count);
            Assert.AreEqual(0, log.OfType<UbTurnStarted>().Count, "게임이 끝나면 UB 턴이 돌지 않는다");
        }

        // ── 아르세우스의 축복 ──

        [Test]
        public void A14_축복으로UB턴을건너뛴다()
        {
            // p.23 「Blessing of Arceus: Use to skip a movement turn of the Ultra Beasts」 — 원작은 버튼을 누르지 않는 것이라 이동·네크로즈마의 파괴·균열 카드 딜이 모두 없다
            var s = UbScenes.Started(ctx, 2, 3);
            Assert.AreEqual(3, s.Ub.Blessings);
            int other = (s.ActiveSeat + 1) % 2;
            var log = new EventLog();
            Scene.Accept(Use(s, ctx, other, UbBlessing.SkipCommand(), log), "차례가 아닌 좌석도 낸다");
            Assert.AreEqual(3, s.Ub.Blessings, "여럿 판은 다른 좌석의 동의부터(결정 84 U1)");
            var vote = s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing);
            Assert.AreEqual(s.ActiveSeat, vote.Seat);
            Assert.AreEqual(other, vote.Subject);
            Battles.Answer(s, ctx, vote, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(2, s.Ub.Blessings);
            Assert.IsTrue(s.Ub.SkipNext);
            var used = log.OfType<BlessingUsed>();
            Assert.AreEqual(1, used.Count);
            Assert.AreEqual(other, used[0].Seat);
            Assert.AreEqual(UbBlessing.SkipTurn, used[0].Target);
            Assert.AreEqual(2, used[0].Left);
            Assert.AreEqual(ActionKind.Blessing, log.OfType<ActionStarted>()[0].Kind);
            Scene.Reject(Use(s, ctx, s.ActiveSeat, UbBlessing.SkipCommand(), log), RejectReason.NoChange, "이미 건너뛰기로 한 턴이 있다");
            Assert.AreEqual(2, s.Ub.Blessings);

            int breach = UbTurn.Breach(s);
            var ubs = s.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast).ConvertAll(t => t.Cell);
            UbScenes.EndRound(s, ctx, log);
            Assert.AreEqual(2, s.Round);
            var skipped = log.OfType<UbTurnSkipped>();
            Assert.AreEqual(1, skipped.Count);
            Assert.AreEqual(1, skipped[0].Round);
            Assert.AreEqual(0, log.OfType<UbTurnStarted>().Count);
            Assert.AreEqual(0, log.OfType<UbMoved>().Count);
            Assert.AreEqual(0, log.OfType<NecrozmaStrike>().Count);
            Assert.AreEqual(0, log.OfType<CellDestroyed>().Count);
            Assert.AreEqual(breach, UbTurn.Breach(s));
            CollectionAssert.AreEqual(ubs, s.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast).ConvertAll(t => t.Cell));
            Assert.IsFalse(s.Ub.SkipNext, "한 턴만");
            UbScenes.EndRound(s, ctx, log);
            Assert.AreEqual(1, log.OfType<UbTurnStarted>().Count, "다음 라운드의 UB 턴은 돈다");
            Assert.AreEqual(1, log.OfType<NecrozmaStrike>().Count);

            // 축복이 없으면(마스터) 못 쓴다 · 신오의 심장 판에는 없다
            var master = UbScenes.Started(ctx, 1, 3, null, Difficulty(UbDifficulty.MasterTrainer));
            Scene.Reject(Use(master, ctx, 0, UbBlessing.SkipCommand(), null), RejectReason.Illegal);
            var hos = Flow.Started(ctx, 1, 3);
            Scene.Reject(Use(hos, ctx, 0, UbBlessing.SkipCommand(), null), RejectReason.Illegal);
        }

        [Test]
        public void A14_축복으로탈락한좌석이고향마을에서부활한다()
        {
            // p.23 「or revive an eliminated trainer」 · 원작 커뮤니티 확인
            var s = UbScenes.Started(ctx, 2, 3);
            int a = s.ActiveSeat, b = (a + 1) % 2;
            int tank = Tank(s, a);
            Scene.Reject(Use(s, ctx, b, UbBlessing.ReviveCommand(a), null), RejectReason.NoChange, "탈락하지 않은 좌석");
            Scene.Reject(Use(s, ctx, b, UbBlessing.ReviveCommand(7), null), RejectReason.BadCommand);
            Lose(s, new EventLog());
            Assert.IsTrue(s.Seats[a].Eliminated);
            Assert.AreEqual(b, s.ActiveSeat);
            int home = ctx.Fixtures.Fixtures[s.Seats[a].HomeVillage].Cell;
            s.Seats[a].Cell = UbScenes.Cell(ctx, 0, 0); // 어디에 있었든 고향 마을에 선다

            var log = new EventLog();
            int fate = s.Seats[a].FateHand.Count;
            Scene.Accept(Use(s, ctx, a, UbBlessing.ReviveCommand(a), log), "탈락한 좌석도 낸다");
            Battles.Answer(s, ctx, s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing), ChoiceAnswer.Of(AnswerKind.Accept), log); // 다른 좌석의 동의(결정 84 U1)
            Assert.IsFalse(s.Seats[a].Eliminated);
            Assert.AreEqual(home, s.Seats[a].Cell);
            Assert.AreEqual(2, s.Ub.Blessings);
            var used = log.OfType<BlessingUsed>();
            Assert.AreEqual(1, used.Count);
            Assert.AreEqual(a, used[0].Target);
            var back = log.OfType<Revived>();
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(a, back[0].Seat);
            Assert.AreEqual(home, back[0].Cell);
            Assert.Less(log.Events.IndexOf(used[0]), log.Events.IndexOf(back[0]));
            Assert.AreEqual(PokemonRules.MaxHp(ctx, s.Pokemon[tank]), PokemonRules.Hp(ctx, s.Pokemon[tank]), "부활하면 곧바로 회복(결정 84 U3 ② 나)");
            Assert.IsTrue(log.OfType<Healed>().Exists(e => e.Seat == a));
            Assert.IsFalse(s.Seats[a].RespawnPending, "귀환 표시도 지운다");

            UbScenes.EndRound(s, ctx, log); // b의 턴 끝 → UB 턴 → 새 라운드: a의 턴
            UbScenes.Attach(s, ctx, log);
            Assert.AreEqual(a, s.ActiveSeat, "부활한 좌석의 턴이 돌아온다");
            Assert.AreEqual(fate, s.Seats[a].FateHand.Count, "운명의 변화 카드는 없다(결정 84 U3 ② 나)");
        }

        [Test]
        public void A14_여럿판축복은다른좌석이하나라도막으면쓰지않는다_묻는동안맵행동은거둔다_결정84()
        {
            // 사용자 결정 84 U1 나 + 메모 「누가 부활버튼 눌렀을때 상단 바로 물어본는걸로」: 낸 좌석을 뺀 모든 좌석에게 한꺼번에 묻고 하나라도 막으면 쓰지 않는다
            var s = UbScenes.Started(ctx, 3, 3);
            UbScenes.Attach(s, ctx);
            int me = s.ActiveSeat;
            var log = new EventLog();
            Scene.Accept(Use(s, ctx, me, UbBlessing.SkipCommand(), log));
            var votes = s.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing);
            CollectionAssert.AreEquivalent(new[] { (me + 1) % 3, (me + 2) % 3 }, votes.ConvertAll(c => c.Seat), "다른 좌석 모두");
            Assert.IsTrue(votes.TrueForAll(v => v.GroupId == votes[0].GroupId), "한 묶음");
            foreach (var v in votes)
            {
                Assert.AreEqual(AnswerKind.Accept, v.Default.Kind, "시간 초과 = 막지 않음");
                Assert.IsTrue(ChoiceRules.Contains(Scene.Legal(s, ctx, v), v.Default));
            }
            Scene.Reject(Use(s, ctx, me, UbBlessing.ReviveCommand(me), null), RejectReason.NoChange);
            Scene.Reject(Use(s, ctx, votes[0].Seat, UbBlessing.SkipCommand(), null), RejectReason.Timing, "묻는 동안에는 다시 내지 않는다");
            // 묻는 동안 차례 좌석의 맵 행동은 거둔다 — 셈이 끝나면 다시 연다
            Assert.IsNull(s.Pending.Find(c => c.Kind == ChoiceKind.MapAction), "묻는 동안 맵 행동이 없다");
            Assert.AreEqual(1, log.OfType<ChoiceReissued>().Count);
            Battles.Answer(s, ctx, votes[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, votes[1], ChoiceAnswer.Of(AnswerKind.Decline), log);
            Assert.AreEqual(3, s.Ub.Blessings, "하나라도 막으면 쓰지 않는다");
            Assert.IsFalse(s.Ub.SkipNext);
            Assert.AreEqual(0, log.OfType<BlessingUsed>().Count);
            Assert.AreEqual(me, Flow.Only(s, ChoiceKind.MapAction).Seat, "같은 좌석의 맵 행동이 다시 열린다");
            // 쉬는 때가 아니면(배틀·다른 선택이 열림) 여럿 판에서는 낼 수 없다
            var busy = UbScenes.Started(ctx, 2, 3);
            busy.Pending.Add(new PendingChoice { Kind = ChoiceKind.Reaction, Seat = 1 });
            Scene.Reject(Use(busy, ctx, 1, UbBlessing.SkipCommand(), null), RejectReason.Timing, "쉬는 때만");
            // 모두 허용하면 쓴다
            Scene.Accept(Use(s, ctx, me, UbBlessing.SkipCommand(), log));
            foreach (var v in s.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing))
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(v.Id), log));
            Assert.AreEqual(2, s.Ub.Blessings);
            Assert.IsTrue(s.Ub.SkipNext);
            Assert.AreEqual(me, log.OfType<BlessingUsed>()[0].Seat, "낸 좌석이 쓴다");
        }

        [Test]
        public void A14_라운드가끝나면축복이남았을때UB턴을건너뛸지묻는다_결정84()
        {
            // 사용자 결정 84 U2 나 — 원작은 라운드가 끝난 뒤 균열 강도 단추를 누르지 않는 것이 건너뛰기(룰북 627행 「Each time all trainers have taken a turn, click the button」)
            var solo = UbScenes.Started(ctx, 1, 3);
            UbScenes.Attach(solo, ctx);
            var log = new EventLog();
            Flow.Answer(solo, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            var ask = Battles.Only(solo, ChoiceKind.Respond, ChoiceSub.UbSkip);
            Assert.AreEqual(0, ask.Seat, "라운드의 마지막 좌석");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "건너뛰지 않음이 기본");
            Assert.IsTrue(ChoiceRules.Contains(Scene.Legal(solo, ctx, ask), ask.Default));
            Assert.AreEqual(0, log.OfType<UbTurnStarted>().Count, "답하기 전에는 UB 턴이 돌지 않는다");
            Assert.AreEqual(1, solo.Round);
            Battles.Answer(solo, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(2, solo.Ub.Blessings);
            Assert.AreEqual(1, log.OfType<UbTurnSkipped>().Count);
            Assert.AreEqual(0, log.OfType<UbTurnStarted>().Count);
            Assert.AreEqual(2, solo.Round, "새 라운드");
            // 건너뛰지 않으면 UB 턴이 돈다
            UbScenes.Attach(solo, ctx, log);
            Flow.Answer(solo, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            Battles.Answer(solo, ctx, Battles.Only(solo, ChoiceKind.Respond, ChoiceSub.UbSkip), ChoiceAnswer.Of(AnswerKind.Decline), log);
            Assert.AreEqual(1, log.OfType<UbTurnStarted>().Count);
            Assert.AreEqual(2, solo.Ub.Blessings);
            Assert.AreEqual(3, solo.Round);
            // 축복이 없으면 묻지 않는다
            var master = UbScenes.Started(ctx, 1, 3, null, Difficulty(UbDifficulty.MasterTrainer));
            UbScenes.Attach(master, ctx);
            var mlog = new EventLog();
            Flow.Answer(master, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), mlog);
            Assert.IsNull(master.Pending.Find(c => c.Kind == ChoiceKind.Respond));
            Assert.AreEqual(1, mlog.OfType<UbTurnStarted>().Count);
        }

        /// <summary>턴을 넘겨 라운드 끝의 건너뛰기 묶음까지 간다(탈락한 좌석은 턴이 없다).</summary>
        List<PendingChoice> ToSkipVote(GameState s, EventLog log)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var asks = s.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip);
                if (asks.Count > 0) return asks;
                UbScenes.Attach(s, ctx, log);
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            }
            Assert.Fail("건너뛰기 물음이 열리지 않았다");
            return null;
        }

        [Test]
        public void A14_건너뛰기다수결은모두고르기전에는내표를바꿀수있다_T29()
        {
            // 플레이 시험 T29 「울트라 비스트 건너뛰기는 투표는 전부 선택전에는 바꿀수 있도록」: 답하지 않은 좌석이 남은 동안은 이미 낸 표를 다시 낸다 — 마지막 표가 오면 셈하고 끝
            var s = UbScenes.Started(ctx, 3, 3);
            var log = new EventLog();
            var asks = ToSkipVote(s, log);
            int blessings = s.Ub.Blessings;
            Battles.Answer(s, ctx, asks[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, asks[1], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.IsTrue(UbBlessing.CanRevote(s, asks[0]), "한 좌석이 아직 고르는 중");
            Assert.IsFalse(UbBlessing.CanRevote(s, asks[2]), "답하지 않은 표는 다시 내는 것이 아니다");
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(asks[0].Id, ChoiceAnswer.Of(AnswerKind.Decline)), log), "좌석 1이 표를 바꾼다");
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(asks[1].Id, ChoiceAnswer.Of(AnswerKind.Decline)), log), "좌석 2도 바꾼다");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(asks[1].Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), RejectReason.NotYourChoice, "남의 표는 바꾸지 못한다");
            Scene.Reject(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(asks[0].Id), log), RejectReason.AlreadyAnswered, "낸 표는 시간 초과로 덮지 않는다");
            Assert.AreEqual(1, s.Round, "묶음이 다 답할 때까지 기다린다");
            Battles.Answer(s, ctx, asks[2], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(blessings, s.Ub.Blessings, "바꾼 표로 셈한다 — 1 대 2, 건너뛰지 않는다");
            Assert.AreEqual(0, log.OfType<UbTurnSkipped>().Count);
            Assert.IsFalse(UbBlessing.CanRevote(s, asks[0]), "셈한 뒤에는 바꾸지 못한다");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(asks[0].Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), RejectReason.NoSuchChoice, "닫힌 묶음");
        }

        [Test]
        public void A14_라운드끝건너뛰기는탈락하지않은모든좌석의다수결_동률은건너뛰지않는다_결정87()
        {
            // 사용자 결정 87 R3 다 + 메모 「다수결」: 모든 좌석에 한꺼번에 묻고 과반이 「건너뛰기」면 건너뛴다 — 다수결이 곧 동의(Respond/Blessing 묶음을 거치지 않는다)
            var s = UbScenes.Started(ctx, 3, 3);
            var log = new EventLog();
            var asks = ToSkipVote(s, log);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, asks.ConvertAll(c => c.Seat), "모든 좌석 — 좌석 순서");
            Assert.IsTrue(asks.TrueForAll(c => c.GroupId == asks[0].GroupId && c.Default.Kind == AnswerKind.Decline), "한 묶음 · 건너뛰지 않음이 기본");
            Scene.Reject(Use(s, ctx, 0, UbBlessing.SkipCommand(), null), RejectReason.Timing, "묻는 동안 축복 정비는 받지 않는다");
            Battles.Answer(s, ctx, asks[1], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, asks[0], ChoiceAnswer.Of(AnswerKind.Decline), log);
            Assert.AreEqual(1, s.Round, "묶음이 다 답할 때까지 기다린다");
            Battles.Answer(s, ctx, asks[2], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(2, s.Ub.Blessings, "2 대 1 — 건너뛴다");
            Assert.AreEqual(1, log.OfType<UbTurnSkipped>().Count);
            Assert.AreEqual(0, log.OfType<UbTurnStarted>().Count);
            Assert.AreEqual(1, log.OfType<BlessingUsed>()[0].Seat, "쓴 좌석 = 「건너뛰기」를 고른 첫 좌석");
            Assert.IsNull(s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing), "동의 묶음을 다시 열지 않는다");
            Assert.AreEqual(2, s.Round);
            // 1 대 2 — UB 턴이 돈다
            asks = ToSkipVote(s, log);
            Battles.Answer(s, ctx, asks[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, asks[1], ChoiceAnswer.Of(AnswerKind.Decline), log);
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(asks[2].Id), log), "시간 초과 = 건너뛰지 않음");
            Assert.AreEqual(2, s.Ub.Blessings);
            Assert.AreEqual(1, log.OfType<UbTurnStarted>().Count);
            // 동률(1 대 1)은 건너뛰지 않는다 [가정 §5 #170 ①]
            var two = UbScenes.Started(ctx, 2, 3);
            var log2 = new EventLog();
            var pair = ToSkipVote(two, log2);
            Assert.AreEqual(2, pair.Count);
            Battles.Answer(two, ctx, pair[0], ChoiceAnswer.Of(AnswerKind.Accept), log2);
            Battles.Answer(two, ctx, pair[1], ChoiceAnswer.Of(AnswerKind.Decline), log2);
            Assert.AreEqual(3, two.Ub.Blessings);
            Assert.AreEqual(1, log2.OfType<UbTurnStarted>().Count, "동률 — UB 턴이 돈다");
            // 탈락한 좌석은 세지 않는다: 셋 가운데 하나가 탈락 → 남은 둘
            var out3 = UbScenes.Started(ctx, 3, 3);
            int gone = out3.ActiveSeat == 2 ? 1 : 2;
            out3.Seats[gone].Eliminated = true;
            var log3 = new EventLog();
            var left = ToSkipVote(out3, log3);
            Assert.AreEqual(2, left.Count);
            Assert.IsFalse(left.Exists(c => c.Seat == gone), "탈락한 좌석은 묻지 않는다");
            foreach (var c in left) Battles.Answer(out3, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), log3);
            Assert.AreEqual(2, out3.Ub.Blessings);
            Assert.AreEqual(1, log3.OfType<UbTurnSkipped>().Count);
        }

        [Test]
        public void A14_축복동의는탈락한좌석에게묻지않는다_물을좌석이없으면바로쓴다_결정87()
        {
            // 사용자 결정 87 R2 나
            var s = UbScenes.Started(ctx, 3, 3);
            UbScenes.Attach(s, ctx);
            int me = s.ActiveSeat, gone = (me + 1) % 3, other = (me + 2) % 3;
            s.Seats[gone].Eliminated = true;
            var log = new EventLog();
            Scene.Accept(Use(s, ctx, me, UbBlessing.SkipCommand(), log));
            var votes = s.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing);
            CollectionAssert.AreEqual(new[] { other }, votes.ConvertAll(c => c.Seat), "탈락한 좌석은 묻지 않는다");
            Battles.Answer(s, ctx, votes[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.IsTrue(s.Ub.SkipNext);
            Assert.AreEqual(2, s.Ub.Blessings);
            // 탈락한 좌석이 낸 것은 탈락하지 않은 좌석 모두에게 묻는다
            Scene.Accept(Use(s, ctx, gone, UbBlessing.ReviveCommand(gone), log), "탈락한 좌석도 낸다");
            votes = s.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing);
            CollectionAssert.AreEquivalent(new[] { me, other }, votes.ConvertAll(c => c.Seat));
            foreach (var v in votes) Battles.Answer(s, ctx, v, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.IsFalse(s.Seats[gone].Eliminated);
            // 물을 좌석이 없으면(다른 좌석이 모두 탈락) 바로 쓴다 — 맵 행동을 거두지 않는다
            var two = UbScenes.Started(ctx, 2, 3);
            UbScenes.Attach(two, ctx);
            int a = two.ActiveSeat, b = (a + 1) % 2;
            two.Seats[b].Eliminated = true;
            var log2 = new EventLog();
            Scene.Accept(Use(two, ctx, a, UbBlessing.ReviveCommand(b), log2));
            Assert.IsNull(two.Pending.Find(c => c.Kind == ChoiceKind.Respond), "묻지 않는다");
            Assert.IsFalse(two.Seats[b].Eliminated, "바로 부활");
            Assert.AreEqual(2, two.Ub.Blessings);
            Assert.AreEqual(a, Flow.Only(two, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void A14_모두탈락했는데축복이남으면부활한다()
        {
            // 혼자: 바로 부활(축복 1개) — 게임이 이어진다
            var solo = UbScenes.Started(ctx, 1, 3);
            Tank(solo, 0);
            var log = new EventLog();
            Lose(solo, log);
            Assert.AreEqual(Phase.Turn, solo.Phase);
            Assert.IsFalse(solo.Seats[0].Eliminated);
            Assert.AreEqual(2, solo.Ub.Blessings);
            Assert.AreEqual(1, log.OfType<Eliminated>().Count);
            Assert.AreEqual(1, log.OfType<BlessingUsed>().Count);
            Assert.AreEqual(1, log.OfType<Revived>().Count);
            Assert.AreEqual(0, log.OfType<GameEnded>().Count);
            UbScenes.NoSkip(solo, ctx, log); // 라운드 끝 건너뛰기 물음(결정 84 U2)
            Assert.AreEqual(1, log.OfType<UbTurnStarted>().Count, "턴이 끝나고 UB 턴이 돈다");
            Assert.AreEqual(2, solo.Round);

            // 여럿: 마지막으로 탈락한 좌석이 부활할 좌석을 고른다(기본 = 자기)
            var s = UbScenes.Started(ctx, 2, 3);
            int a = s.ActiveSeat, b = (a + 1) % 2;
            Tank(s, a);
            s.Seats[b].Eliminated = true;
            var log2 = new EventLog();
            Lose(s, log2);
            Assert.AreEqual(0, UbNecrozma.Remaining(s));
            Assert.AreEqual(Phase.Turn, s.Phase);
            var ask = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.Revive);
            Assert.AreEqual(a, ask.Seat);
            var legal = Scene.Legal(s, ctx, ask);
            CollectionAssert.AreEqual(new[] { a, b }, legal.ConvertAll(x => x.Target));
            Assert.IsTrue(ChoiceRules.Contains(legal, ask.Default));
            Assert.AreEqual(a, ask.Default.Target);
            Scene.Reject(Use(s, ctx, b, UbBlessing.ReviveCommand(b), null), RejectReason.Timing, "묻는 동안에는 정비 명령으로 쓰지 않는다");
            Battles.Answer(s, ctx, ask, ChoiceAnswer.Pick(b), log2);
            Assert.IsFalse(s.Seats[b].Eliminated);
            Assert.IsTrue(s.Seats[a].Eliminated);
            Assert.AreEqual(2, s.Ub.Blessings);
            Assert.AreEqual(a, log2.OfType<BlessingUsed>()[0].Seat);
            Assert.AreEqual(b, log2.OfType<Revived>()[0].Seat);
            UbScenes.Attach(s, ctx, log2);
            Assert.AreEqual(b, s.ActiveSeat, "부활한 좌석의 턴으로 넘어간다");
            Assert.IsTrue(log2.OfType<TurnEnded>().Exists(e => e.Seat == a));
        }

        [Test]
        public void S13_4_아르세우스3장은울트라버스트에서축복하나()
        {
            // p.19 「Arceus: … This Play 3 effect grants you 1 Blessing of Arceus during the Ultra Burst campaign.」
            var s = UbScenes.Started(ctx, 2, 3);
            int seat = s.ActiveSeat;
            int key = FateRules.CardKey(ctx, FateCard.Arceus);
            s.Seats[seat].FateHand.AddRange(new[] { key, key });
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Arceus), "2장으로는 못 낸다");
            s.Seats[seat].FateHand.Add(key);
            var play = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Arceus);
            Assert.IsNotNull(play);
            Assert.AreEqual(FateRules.Play3Count, play.Arg);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, play, log);
            Assert.AreEqual(4, s.Ub.Blessings);
            Assert.AreEqual(0, s.Seats[seat].FateHand.Count);
            var gained = log.OfType<BlessingGained>();
            Assert.AreEqual(1, gained.Count);
            Assert.AreEqual(seat, gained[0].Seat);
            Assert.AreEqual(4, gained[0].Total);
            Assert.AreEqual(SourceKind.Fate, gained[0].Source.Kind);
            Assert.AreEqual(seat, s.ActiveSeat, "턴은 이어진다");
        }
    }
}
