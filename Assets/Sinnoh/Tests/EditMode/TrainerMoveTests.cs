using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「이동」(rulings A36 — 능력 11): hiker B · picnicker B · roller_skater B · swimmer B · triathlete B · sky_trainer B · bird_keeper B · team_aqua B · team_magma B · essentia B · worker B.
    /// </summary>
    public class TrainerMoveTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Bidoof = "399:0", Turtwig = "387:0", Piplup = "393:0", Garchomp = "445:0";

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        void Card(GameState s, int seat, string key, int tier = TrainerRules.Silver)
        {
            s.Seats[seat].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
        }

        /// <summary>시작한 판의 차례 좌석에 카드를 주고 그 칸에 세운다. 숨겨진 장소는 치운다(걷다가 멈추지 않게).</summary>
        GameState Table(string card, out int seat, EventLog log, int tier = TrainerRules.Silver)
        {
            var s = Flow.Started(ctx, 2, 5, log);
            seat = s.ActiveSeat;
            if (card != null) Card(s, seat, card, tier);
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) t.Destroyed = true;
            return s;
        }

        static void Stand(GameState s, int seat, int cell, int moves)
        {
            s.Seats[seat].Cell = cell;
            s.Seats[seat].MovesLeft = moves;
        }

        bool Open(int from, int to) => MoveRules.ElevationAllows(ctx.Board[from], ctx.Board[to], new RuleOptions().ElevationMode);

        IEnumerable<int> Around(int cell)
        {
            for (int d = 0; d < Board.Directions; d++)
            {
                int n = ctx.Board.Neighbor(cell, d);
                if (n != Board.NoCell) yield return n;
            }
        }

        // ── 걷기의 값(MoveRules — 좌석 조건) ──

        static Board Line(params int[] levelQuarters)
        {
            var cells = new Cell[levelQuarters.Length];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Cell(new HexCoord(0, 2 * i), levelQuarters[i], Biome.Plains, Climate.Temperate, true, false, "");
            return new Board(cells);
        }

        [Test]
        public void A20_6_worker_파랑_이어진길은몇칸이든이동력1()
        {
            // 「All trainers can move through any number of interconnected Roads for 1 tile move.」 — 길 위에서 이어진 다른 길 칸까지 1(원작 커뮤니티 확인)
            var b = Line(4, 4, 4, 4, 4, 4);
            var t = new MoveRules.MoveTerms { Mode = ElevationMode.Floor, Roads = new List<int> { 1, 2, 3, 4 } };
            Assert.AreEqual(1, MoveRules.PathCost(b, 1, new[] { 2, 3, 4 }, t), "길 → 길 → 길 = 1");
            Assert.AreEqual(1 + 1 + 1, MoveRules.PathCost(b, 0, new[] { 1, 2, 3, 4, 5 }, t), "길에 올라서기 1 + 길 1 + 길에서 내려서기 1");
            Assert.AreEqual(1, MoveRules.Reachable(b, 1, 1, t)[4]);
            Assert.AreEqual(MoveRules.Unreachable, MoveRules.Reachable(b, 1, 1, t)[5]);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, MoveRules.Path(b, 1, 4, 1, t));
            Assert.AreEqual(0, MoveRules.PathCost(b, 2, new[] { 3 }, t, 0, true), "이어 가는 중이면 0");
            // 길이 없으면 옛 셈 그대로
            var plain = new MoveRules.MoveTerms { Mode = ElevationMode.Floor };
            Assert.IsTrue(plain.Plain);
            Assert.AreEqual(3, MoveRules.PathCost(b, 1, new[] { 2, 3, 4 }, plain));
            CollectionAssert.AreEqual(MoveRules.Reachable(b, 0, 3, ElevationMode.Floor), MoveRules.Reachable(b, 0, 3, plain));
        }

        [Test]
        public void A20_6_sky_trainer_파랑_2층이상낮은칸으로내려서고_높아지지않는4칸을더간다()
        {
            // 「You may move to adjacent tiles that are 2 or more elevations lower than your current tile. After doing so, you may move up to 4 additional tiles in a row so long as your elevation does not increase.」
            var b = Line(16, 4, 4, 4, 4, 4, 4, 8);
            var sky = new MoveRules.MoveTerms { Mode = ElevationMode.Floor, Descend = true };
            var plain = new MoveRules.MoveTerms { Mode = ElevationMode.Floor };
            Assert.AreEqual(-1, MoveRules.PathCost(b, 0, new[] { 1 }, plain), "보통은 2층 차를 못 내려선다");
            Assert.AreEqual(1, MoveRules.PathCost(b, 0, new[] { 1 }, sky));
            Assert.AreEqual(1, MoveRules.PathCost(b, 0, new[] { 1, 2, 3, 4, 5 }, sky), "내려선 뒤 4칸은 무료");
            Assert.AreEqual(2, MoveRules.PathCost(b, 0, new[] { 1, 2, 3, 4, 5, 6 }, sky), "다섯째 칸부터는 이동력");
            Assert.AreEqual(-1, MoveRules.PathCost(b, 1, new[] { 0 }, sky), "올라가지는 못한다");
            Assert.AreEqual(1, MoveRules.Reachable(b, 0, 1, sky)[5]);
            Assert.AreEqual(MoveRules.Unreachable, MoveRules.Reachable(b, 0, 1, sky)[6]);
            // 높이가 오르는 걸음은 활공이 아니다
            var up = Line(16, 4, 5, 4);
            Assert.AreEqual(1 + 1 + 1, MoveRules.PathCost(up, 0, new[] { 1, 2, 3 }, sky), "오르는 걸음에서 활공이 끝난다");
        }

        [Test]
        public void A20_6_hiker_파랑_높이와상관없이어느칸으로든()
        {
            // 「Additionally, you can move onto any tile regardless of its elevation.」
            var log = new EventLog();
            var s = Table(null, out int seat, log);
            int from = -1, to = -1;
            for (int c = 0; c < ctx.Board.Count && from < 0; c++)
                foreach (int n in Around(c))
                    if (!Open(c, n) && !ctx.Board[c].DistortionWorld && from < 0)
                    {
                        from = c;
                        to = n;
                    }
            Assert.GreaterOrEqual(from, 0, "높이로 막힌 이웃 칸");
            Stand(s, seat, from, 1);
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == to && a.Arg == -1), "카드가 없으면 못 간다");
            Card(s, seat, "hiker");
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == to && a.Arg == -1));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { from, to }), log);
            Assert.AreEqual(to, s.Seats[seat].Cell);
            Assert.AreEqual(0, s.Seats[seat].MovesLeft);
            Card(s, seat, "hiker", TrainerRules.Bronze);
            s.Seats[seat].MovesLeft = 1;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == from && a.Arg == -1), "브론즈");
        }

        // ── 턴당 1번 지형 2칸 무료 ──

        /// <summary>그 좌석이 무료로 두 칸 이어 밟을 수 있는 자리(시작 칸 · 첫 칸 · 둘째 칸).</summary>
        bool FreeRun(GameState s, int seat, out int x, out int n1, out int n2)
        {
            for (x = 0; x < ctx.Board.Count; x++)
            {
                if (ctx.Board[x].DistortionWorld) continue;
                foreach (int a in Around(x))
                {
                    if (!TrainerRules.FreeTile(s, ctx, seat, a) || !Open(x, a)) continue;
                    foreach (int b in Around(a))
                    {
                        if (b == x || !TrainerRules.FreeTile(s, ctx, seat, b) || !Open(a, b)) continue;
                        bool near = false;
                        foreach (int k in Around(x))
                            if (k == b) near = true;
                        if (near) continue; // 둘째 칸이 시작 칸의 이웃이면 한 걸음에 닿는다
                        n1 = a;
                        n2 = b;
                        return true;
                    }
                }
            }
            n1 = n2 = -1;
            return false;
        }

        [TestCase("hiker", Biome.Mountain)]
        [TestCase("picnicker", Biome.Forest)]
        [TestCase("roller_skater", Biome.Plains)]
        [TestCase("swimmer", Biome.Ocean)]
        [TestCase("triathlete", Biome.None)]
        public void A20_6_지형2칸무료_이동력없이그지형칸을두칸(string card, Biome biome)
        {
            // 「Once per turn, you may move up to 2 Mountain·Forest·Plains·Ocean·non-Mountain tiles in a row for free.」
            var log = new EventLog();
            var s = Table(card, out int seat, log);
            Assert.IsTrue(FreeRun(s, seat, out int x, out int n1, out int n2));
            if (biome != Biome.None)
            {
                Assert.AreEqual(biome, ctx.Board[n1].Biome);
                Assert.AreEqual(biome, ctx.Board[n2].Biome);
            }
            else Assert.AreNotEqual(Biome.Mountain, ctx.Board[n2].Biome, "triathlete: 산이 아닌 칸");
            Stand(s, seat, x, 0);
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Move && a.Arg == -1), "이동력 0 — 보통 걷기는 없다");
            Assert.IsTrue(legal.Exists(a => a.Kind == AnswerKind.Move && a.Target == n2 && a.Arg == TrainerRules.FreeArg));
            foreach (var a in legal)
                if (a.Kind == AnswerKind.Move) Assert.IsTrue(TrainerRules.FreeTile(s, ctx, seat, a.Target), "무료로 닿는 칸은 그 지형뿐");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, n2, TrainerRules.FreeArg), log);
            Assert.AreEqual(n2, s.Seats[seat].Cell);
            Assert.AreEqual(0, s.Seats[seat].MovesLeft);
            Assert.AreEqual(0, s.Seats[seat].FreeLeft);
            Assert.AreEqual(3, Flow.Of<Moved>(log)[0].Path.Length, "두 걸음");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move), "턴당 한 번");
            // 카드가 없는 좌석에는 없다
            var s2 = Table(null, out int seat2, new EventLog());
            Stand(s2, seat2, x, 0);
            Assert.IsFalse(Flow.MapLegal(s2, ctx).Exists(a => a.Kind == AnswerKind.Move));
        }

        [Test]
        public void A20_6_무료지형이동길의멈출칸미리보기는엔진과같은셈이다_이동력1()
        {
            // M6 화면 교차 검토 #3: 미리 보기(MapActions.StopsOn)가 걸음마다 이동력 1을 쓴다고 셌다 — 무료 걸음은 이동력을 쓰지 않아 이동력 1이 그대로 남고, 엔진은 첫 칸의 숨겨진 장소에서 멈춘다
            var log = new EventLog();
            var s = Table("hiker", out int seat, log);
            Assert.IsTrue(FreeRun(s, seat, out int x, out int n1, out int n2));
            Stand(s, seat, x, 1);
            int spot = s.Tokens.FindIndex(t => t.Kind == TokenKind.HiddenSpot);
            s.Tokens[spot].Cell = n1;
            s.Tokens[spot].FaceUp = false;
            s.Tokens[spot].Destroyed = false;
            var path = new List<int> { x, n1, n2 };
            var steps = path.GetRange(1, 2);
            Assert.IsTrue(TrainerRules.FreeWalkAllowed(s, ctx, seat, steps), "시험 준비: 무료 2칸");
            CollectionAssert.AreEqual(new[] { n1 }, MapActions.StopsOn(s, ctx, seat, steps, free: true), "무료 길: 첫 칸 뒤에도 이동력 1 — 멈춘다");
            var walk = ChoiceAnswer.MoveAlong(path);
            walk.Arg = TrainerRules.FreeArg;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, walk, log);
            var ask = Flow.Only(s, ChoiceKind.HiddenSpot);
            Assert.AreEqual(n1, s.Seats[seat].Cell, "엔진도 그 칸에서 멈춘다");
            Assert.AreEqual(1, s.Seats[seat].MovesLeft);
            CollectionAssert.AreEqual(new[] { n2 }, ask.Options, "남은 길");
            Assert.AreEqual(MapActions.FreeRest, ask.Count, "남은 길도 무료(화면은 이 값으로 남은 길의 멈출 칸을 센다)");
            CollectionAssert.IsEmpty(MapActions.StopsOn(s, ctx, seat, ask.Options, free: true), "남은 길에는 더 멈출 칸이 없다");
        }

        [Test]
        public void M6_UB판의무료지형이동은게이트이동이아니다()
        {
            // 왜곡 게이트 이동(Arg = 건너편 게이트)과 무료 지형 이동(Arg = FreeArg)이 Move의 Arg를 같이 쓴다 — 울트라 버스트 판에서도 무료 이동은 고른 칸으로 걷는다(rulings A42,
            // 합칠 때 Apply가 Arg ≥ 0이면 게이트로 보아 고정물 1의 칸으로 옮겼다)
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "triathlete");
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) t.Destroyed = true;
            Assert.IsTrue(FreeRun(s, seat, out int x, out int n1, out int n2));
            Stand(s, seat, x, 0);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == n2 && a.Arg == TrainerRules.FreeArg));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, n2, TrainerRules.FreeArg), log);
            Assert.AreEqual(n2, s.Seats[seat].Cell);
            Assert.AreEqual(0, Flow.Of<GateTravel>(log).Count, "게이트 이동이 아니다");
            Assert.AreEqual(1, Flow.Of<Moved>(log).Count);
        }

        [Test]
        public void A36_무료칸은이어서만_게이트나날기로옮기면남은칸이끊기고_이동으로센다()
        {
            // 감사 G2-1 · G4-4(11-20): 순간 이동(왜곡 게이트 · 공중날기 · 펄기아) 뒤에도 남은 무료 칸이 이어졌고(「in a row」 — 원작 커뮤니티 확인), 게이트 건너기는 Moved를 켜지 않았다(essentia R — rulings A34 ③)
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "triathlete");
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) t.Destroyed = true;
            var fixtures = ctx.Fixtures.Fixtures;
            int gate = -1;
            for (int f = 0; f < fixtures.Count && gate < 0; f++)
                if (fixtures[f].Kind == FixtureKind.Gate && !ctx.Board[fixtures[f].Cell].DistortionWorld) gate = f;
            Assert.GreaterOrEqual(gate, 0, "시험 준비: 깨어진 세계 밖 게이트");
            int pair = fixtures[gate].Pair, far = fixtures[pair].Cell;
            var st = s.Seats[seat];
            Stand(s, seat, fixtures[gate].Cell, 3);
            st.Moved = false;
            st.FreeLeft = 1; // 무료로 한 칸 걸어 게이트에 닿았다
            var cross = A(AnswerKind.Move, far, pair);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(cross)), "게이트 건너기");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, cross, log);
            Assert.AreEqual(far, st.Cell);
            Assert.AreEqual(0, st.FreeLeft, "게이트 건너편에서 남은 무료 칸을 이어 쓰지 않는다");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Arg == TrainerRules.FreeArg));
            Assert.IsTrue(st.Moved, "게이트로 건넜으면 움직였다");

            // 아직 무료 칸을 쓰지 않았으면(2칸) 날아간 곳에서 쓸 수 있다
            var s2 = Table("bird_keeper", out int seat2, new EventLog());
            int cell = WildCell(s2, c => ctx.Board[c].Biome == Biome.Forest);
            Stand(s2, seat2, cell, 0);
            s2.Seats[seat2].Apricorns[(int)ApricornColor.Blue] = 1;
            var fly = Flow.MapLegal(s2, ctx).Find(a => a.Kind == AnswerKind.Fly);
            s2.Seats[seat2].FreeLeft = TrainerRules.FreeTiles;
            Flow.Answer(s2, ctx, ChoiceKind.MapAction, fly, new EventLog());
            Assert.AreEqual(TrainerRules.FreeTiles, s2.Seats[seat2].FreeLeft);
        }

        [Test]
        public void M6_무료칸은턴당한번_이어서만_다음턴에다시()
        {
            var log = new EventLog();
            var s = Table("picnicker", out int seat, log);
            Assert.IsTrue(FreeRun(s, seat, out int x, out int n1, out int n2));
            Stand(s, seat, x, 3);
            // 한 칸만 무료로: 남은 한 칸은 바로 이어서 쓸 수 있다(사이의 상호작용은 된다 — 원작 커뮤니티 확인)
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswerFree(x, n1), log);
            Assert.AreEqual(3, s.Seats[seat].MovesLeft, "이동력은 그대로");
            Assert.AreEqual(1, s.Seats[seat].FreeLeft);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == n2 && a.Arg == TrainerRules.FreeArg));
            var two = ChoiceAnswerFree(n1, n2);
            two.Values.Add(n1);
            two.Target = n1;
            Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, Flow.Only(s, ChoiceKind.MapAction), two), "남은 무료 칸(1)을 넘는 길");
            // 보통 걸음을 걸으면 남은 무료 칸은 사라진다(「in a row」 — 원작 커뮤니티 확인)
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { n1, x }), log);
            Assert.AreEqual(2, s.Seats[seat].MovesLeft);
            Assert.AreEqual(0, s.Seats[seat].FreeLeft);
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Arg == TrainerRules.FreeArg));
            // 다음 내 턴에 다시
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.AreEqual(TrainerRules.FreeTiles, s.Seats[seat].FreeLeft);
        }

        static ChoiceAnswer ChoiceAnswerFree(int from, int to)
        {
            var a = ChoiceAnswer.MoveAlong(new List<int> { from, to });
            a.Arg = TrainerRules.FreeArg;
            return a;
        }

        [Test]
        public void A20_6_swimmer_파랑_물타입우선도1()
        {
            // 「Additionally, your Water-type Pokémon gain +1 initiative.」
            var s = Battles.Game(ctx, 2);
            int piplup = Scene.Give(s, ctx, 0, Piplup), turtwig = Scene.Give(s, ctx, 0, Turtwig);
            Card(s, 0, "swimmer");
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, piplup, mods);
            Assert.AreEqual(1, mods.Count);
            Assert.AreEqual(1, mods[0].Delta);
            mods.Clear();
            TrainerRules.PriorityMods(s, ctx, turtwig, mods);
            Assert.AreEqual(0, mods.Count);
        }

        // ── 활공 · 날기 ──

        [Test]
        public void A20_6_sky_trainer_파랑_판에서내려서면활공4칸이열린다()
        {
            var log = new EventLog();
            var s = Table("sky_trainer", out int seat, log);
            int from = -1, to = -1;
            for (int c = 0; c < ctx.Board.Count && from < 0; c++)
                foreach (int n in Around(c))
                    if (!Open(c, n) && ctx.Board[n].Floor < ctx.Board[c].Floor && !ctx.Board[c].DistortionWorld && from < 0)
                    {
                        from = c;
                        to = n;
                    }
            Assert.GreaterOrEqual(from, 0);
            Stand(s, seat, from, 1);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == to));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { from, to }), log);
            Assert.AreEqual(0, s.Seats[seat].MovesLeft);
            Assert.AreEqual(MoveRules.GlideSteps, s.Seats[seat].Glide);
            int next = -1;
            foreach (int n in Around(to))
                if (n != from && Open(to, n) && ctx.Board[n].LevelQuarters <= ctx.Board[to].LevelQuarters && next < 0) next = n;
            if (next < 0) return; // 더 내려갈 칸이 없는 자리
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == next), "이동력 0이어도 활공으로 간다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { to, next }), log);
            Assert.AreEqual(MoveRules.GlideSteps - 1, s.Seats[seat].Glide);
            Assert.AreEqual(0, s.Seats[seat].MovesLeft);
        }

        int WildCell(GameState s, Func<int, bool> ok)
        {
            for (int c = 0; c < ctx.Board.Count; c++)
            {
                if (ctx.Board[c].DistortionWorld || !ok(c)) continue;
                bool village = false;
                foreach (var f in ctx.Fixtures.Fixtures)
                    if (f.Cell == c) village = true;
                if (!village) return c;
            }
            Assert.Fail("그런 칸이 없다");
            return -1;
        }

        [Test]
        public void A20_6_bird_keeper_파랑_아무칸에서규토리1로조무래기없는마을로()
        {
            // 「You may fly to a village from any tile by discarding 1 Apricorn. There cannot be any Team Galactic Grunts on the target village.」
            var log = new EventLog();
            var s = Table("bird_keeper", out int seat, log);
            int cell = WildCell(s, c => ctx.Board[c].Biome == Biome.Forest);
            Stand(s, seat, cell, 0);
            for (int c = 0; c < SeatState.ApricornColors; c++) s.Seats[seat].Apricorns[c] = 0;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Fly), "규토리가 없다");
            s.Seats[seat].Apricorns[(int)ApricornColor.Blue] = 1;
            var flights = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Fly);
            Assert.Greater(flights.Count, 0);
            foreach (var a in flights)
            {
                var f = ctx.Fixtures.Fixtures[a.Target];
                Assert.AreEqual(FixtureKind.Village, f.Kind);
                Assert.IsFalse(SetupFlow.GruntAt(s, f.Cell), "조무래기가 있는 마을로는 못 간다");
                Assert.IsFalse(ctx.Board[f.Cell].DistortionWorld);
                Assert.AreEqual((int)ApricornColor.Blue, a.Arg);
            }
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, flights[0], log);
            Assert.AreEqual(ctx.Fixtures.Fixtures[flights[0].Target].Cell, s.Seats[seat].Cell);
            Assert.AreEqual(0, s.Seats[seat].Apricorns[(int)ApricornColor.Blue]);
            Assert.AreEqual(1, Flow.Of<AbilityUsed>(log).Count);
            Assert.AreEqual(1, Flow.Of<Flew>(log).Count);
            // 카드가 없으면 마을 밖에서는 못 난다
            var s2 = Table(null, out int seat2, new EventLog());
            Stand(s2, seat2, cell, 0);
            for (int c = 0; c < SeatState.ApricornColors; c++) s2.Seats[seat2].Apricorns[c] = 0;
            s2.Seats[seat2].Apricorns[(int)ApricornColor.Blue] = 1;
            Assert.IsFalse(Flow.MapLegal(s2, ctx).Exists(a => a.Kind == AnswerKind.Fly));
        }

        [Test]
        public void A20_6_picnicker_파랑_아무칸에서규토리1로앞면야영지로()
        {
            // 「Additionally, you may fly to Campsites from any tile by discarding 1 Apricorn.」
            var log = new EventLog();
            var s = Table("picnicker", out int seat, log);
            int camp = ctx.Items.Ids.IndexOf("hidden_spot.campsite");
            var token = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Cell >= 0);
            Assert.IsNotNull(token);
            token.Destroyed = false;
            token.Def = camp;
            token.FaceUp = false;
            int cell = WildCell(s, c => c != token.Cell);
            Stand(s, seat, cell, 0);
            for (int c = 0; c < SeatState.ApricornColors; c++) s.Seats[seat].Apricorns[c] = 0;
            s.Seats[seat].Apricorns[(int)ApricornColor.Red] = 1;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility), "뒷면 야영지로는 못 간다");
            token.FaceUp = true;
            var fly = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.UseAbility && a.Target == token.Cell);
            Assert.IsNotNull(fly);
            Assert.AreEqual((int)ApricornColor.Red, fly.Arg);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, fly, log);
            Assert.AreEqual(token.Cell, s.Seats[seat].Cell);
            Assert.AreEqual(0, s.Seats[seat].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(seat, s.ActiveSeat, "턴은 이어진다");
        }

        // ── 턴 끝 ──

        [TestCase("team_aqua", Biome.Ocean)]
        [TestCase("team_magma", Biome.Mountain)]
        public void A20_6_턴끝에다른바다산칸으로(string card, Biome biome)
        {
            // 「If you are standing on an Ocean·Mountain tile at the end of your turn, you may instantly move to any other Ocean·Mountain tile.」
            var log = new EventLog();
            var s = Table(card, out int seat, log);
            int here = WildCell(s, c => ctx.Board[c].Biome == biome);
            Stand(s, seat, here, 0);
            var ends = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.EndTurn);
            int same = 0;
            for (int c = 0; c < ctx.Board.Count; c++)
                if (c != here && ctx.Board[c].Biome == biome && !ctx.Board[c].DistortionWorld) same++;
            Assert.AreEqual(1 + same, ends.Count, "그대로 끝내기 + 다른 같은 지형 칸마다");
            Assert.AreEqual(-1, ends[0].Target);
            Assert.AreEqual(AnswerKind.EndTurn, Flow.MapLegal(s, ctx)[0].Kind, "첫 답 = 그대로 턴 종료");
            foreach (var a in ends)
                if (a.Target >= 0) Assert.AreEqual(biome, ctx.Board[a.Target].Biome, "호수는 바다가 아니다(원작 커뮤니티 확인)");
            log.Clear();
            int to = ends[ends.Count - 1].Target;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn, to), log);
            Assert.AreEqual(to, s.Seats[seat].Cell);
            Assert.AreNotEqual(seat, s.ActiveSeat, "턴이 끝났다");
            Assert.AreEqual(1, Flow.Of<AbilityUsed>(log).Count);
            Assert.AreEqual(1, Flow.Of<TurnEnded>(log).Count);
            // 그 지형이 아닌 칸에서는 없다
            var s2 = Table(card, out int seat2, new EventLog());
            Stand(s2, seat2, WildCell(s2, c => ctx.Board[c].Biome == Biome.Plains), 0);
            Assert.AreEqual(1, Flow.MapLegal(s2, ctx).FindAll(a => a.Kind == AnswerKind.EndTurn).Count);
        }

        /// <summary>야생 배틀을 이겨 턴이 끝나려는 자리까지: 좌석 0의 한카리아스 대 체력 1 비버니. moves = 배틀 전 남은 이동력.</summary>
        GameState Won(string card, int cell, int moves, EventLog log)
        {
            var s = Battles.Game(ctx, 2);
            int mine = Scene.Give(s, ctx, 0, Garchomp);
            Scene.Give(s, ctx, 1, Turtwig);
            if (card != null) Card(s, 0, card);
            if (cell >= 0) s.Seats[0].Cell = cell;
            s.Seats[0].MovesLeft = moves;
            int wild = Scene.Spawn(s, ctx, Bidoof, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            Battles.Wild(s, ctx, log, 0, wild);
            for (int guard = 0; guard < 6 && s.Battle != null; guard++)
            {
                var c = Battles.Only(s, ChoiceKind.BattleAction);
                var roll = new[] { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit };
                s.Rng = new Pcg32(Battles.Seed(seed => Same(AttackDice.Roll(new Pcg32(seed), 4), roll)));
                Battles.Answer(s, ctx, c, Battles.Move(ctx.Cards.Pokemon[s.Pokemon[mine].Def].Signature), log);
                while (s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.Reward) Battles.Answer(s, ctx, s.Pending[0], s.Pending[0].Default.Clone(), log);
            }
            Assert.IsNull(s.Battle, "배틀이 끝났다");
            return s;
        }

        static bool Same(AttackDieFace[] a, AttackDieFace[] b)
        {
            for (int i = 0; i < b.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        [Test]
        public void A20_6_essentia_파랑_배틀뒤남은이동력으로걷기만()
        {
            // 「After a battle, you may use any movement you have remaining but cannot interact with anything else this turn.」
            var log = new EventLog();
            var s = Won("essentia", -1, 2, log);
            Assert.AreEqual(0, s.ActiveSeat, "턴이 아직 끝나지 않았다");
            var legal = Flow.MapLegal(s, ctx);
            Assert.AreEqual(AnswerKind.EndTurn, legal[0].Kind);
            foreach (var a in legal) Assert.IsTrue(a.Kind == AnswerKind.EndTurn || a.Kind == AnswerKind.Move, $"{a.Kind}: 걷기와 턴 종료뿐");
            var step = legal.Find(a => a.Kind == AnswerKind.Move);
            Assert.IsNotNull(step);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, step, log);
            Assert.AreEqual(step.Target, s.Seats[0].Cell);
            Assert.AreEqual(0, s.ActiveSeat);
            foreach (var a in Flow.MapLegal(s, ctx)) Assert.IsTrue(a.Kind == AnswerKind.EndTurn || a.Kind == AnswerKind.Move);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Assert.AreEqual(1, s.ActiveSeat);
            Assert.AreEqual(TurnEndReason.Battle, Flow.Of<TurnEnded>(log)[0].Reason);
            Assert.AreEqual(0, s.Seats[0].Closing);
            // 이동력이 남지 않았거나 카드가 없으면 배틀이 턴을 바로 끝낸다
            Assert.AreEqual(1, Won("essentia", -1, 0, new EventLog()).ActiveSeat);
            Assert.AreEqual(1, Won(null, -1, 2, new EventLog()).ActiveSeat);
        }

        [Test]
        public void A20_6_team_aqua_파랑_배틀로끝난턴에도바다칸으로()
        {
            int here = -1;
            for (int c = 0; c < ctx.Board.Count && here < 0; c++)
                if (ctx.Board[c].Biome == Biome.Ocean && !ctx.Board[c].DistortionWorld) here = c;
            var log = new EventLog();
            var s = Won("team_aqua", here, 0, log);
            Assert.AreEqual(0, s.ActiveSeat, "턴 끝에 옮길 칸을 고른다");
            var legal = Flow.MapLegal(s, ctx);
            foreach (var a in legal) Assert.AreEqual(AnswerKind.EndTurn, a.Kind, "턴 종료(그대로 · 옮길 칸)뿐");
            Assert.Greater(legal.Count, 1);
            Assert.AreEqual(-1, Flow.Only(s, ChoiceKind.MapAction).Default.Target, "기본 답 = 그대로 턴 종료");
            int to = legal[1].Target;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, legal[1], log);
            Assert.AreEqual(to, s.Seats[0].Cell);
            Assert.AreEqual(1, s.ActiveSeat);
        }

        // ── 야생의 도망 · 길 ──

        /// <summary>야생 배틀에서 내 첫 포켓몬(체력 1)이 쓰러진 자리(그렇게 되는 시드를 찾는다). 도망쳤으면 Fled가 있다.</summary>
        GameState Downed(string card, EventLog log)
        {
            for (ulong seed = 1; seed < 60; seed++)
            {
                log.Clear();
                var s = Battles.Game(ctx, 2);
                int first = Scene.Give(s, ctx, 0, Turtwig);
                Scene.Give(s, ctx, 0, Piplup);
                Scene.Give(s, ctx, 1, Turtwig);
                if (card != null) Card(s, 0, card);
                Battles.SetHp(s, ctx, first, 1);
                int wild = Scene.Spawn(s, ctx, Garchomp, SeatId.Wild);
                Battles.Wild(s, ctx, log, 0, wild);
                if (s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.SendIn) Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Pick(first), log);
                if (s.Pending.Count == 1 && s.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Of(AnswerKind.Decline), log);
                if (log.OfType<Fainted>().Exists(f => f.PokemonId == first)) return s;
                s.Rng = new Pcg32(seed);
                if (s.Battle != null && s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.BattleAction)
                    Battles.Answer(s, ctx, s.Pending[0], Battles.Move(ctx.Cards.Pokemon[s.Pokemon[first].Def].Signature), log);
                if (log.OfType<Fainted>().Exists(f => f.PokemonId == first)) return s;
            }
            Assert.Fail("첫 포켓몬이 쓰러지는 시드가 없다");
            return null;
        }

        [Test]
        public void A20_6_roller_skater_파랑_야생은내포켓몬둘을쓰러뜨려야도망()
        {
            // 「Additionally, non-Legendary wild Pokémon can now only flee after defeating two of your Pokémon.」
            var log = new EventLog();
            var s = Downed("roller_skater", log);
            Assert.AreEqual(0, log.OfType<Fled>().Count, "하나를 쓰러뜨려서는 도망치지 않는다");
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(1, s.Battle.Sides[0].Fallen);
            Assert.IsFalse(TrainerRules.WildFlees(s, ctx, s.Battle, 1));
            Assert.IsTrue(TrainerRules.WildFlees(s, ctx, s.Battle, 2));
            // 카드가 없으면 첫 포켓몬을 쓰러뜨리고 도망친다
            log = new EventLog();
            s = Downed(null, log);
            Assert.AreEqual(1, log.OfType<Fled>().Count);
            Assert.IsNull(s.Battle);
        }

        [Test]
        public void A20_6_worker_파랑_JP2로이번턴에밟는칸마다길_이어진길은모두에게1()
        {
            // 「Costs 2 Journey Points: You may build Roads on any non-Ocean tiles you move onto this turn. All trainers can move through any number of interconnected Roads for 1 tile move.」
            var log = new EventLog();
            var s = Table("worker", out int seat, log);
            int x = -1;
            List<int> path = null;
            for (int c = 0; c < ctx.Board.Count && path == null; c++)
            {
                if (ctx.Board[c].DistortionWorld || ctx.Board[c].Biome != Biome.Plains) continue;
                var reach = MoveRules.Reachable(ctx.Board, c, 3, s.Options.ElevationMode);
                for (int d = 0; d < reach.Length && path == null; d++)
                {
                    if (reach[d] != 3) continue;
                    var p = MoveRules.Path(ctx.Board, c, d, 3, s.Options.ElevationMode);
                    if (p.TrueForAll(k => ctx.Board[k].Biome != Biome.Ocean))
                    {
                        x = c;
                        path = p;
                    }
                }
            }
            Assert.IsNotNull(path);
            Stand(s, seat, x, 3);
            s.Seats[seat].Jp = 1;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility), "JP가 모자라다");
            s.Seats[seat].Jp = 3;
            var build = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.UseAbility && a.Target == -1);
            Assert.IsNotNull(build);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, build, log);
            Assert.AreEqual(1, s.Seats[seat].Jp);
            Assert.IsTrue(s.Seats[seat].Building);
            Assert.AreEqual(1, Flow.Of<AbilityUsed>(log).Count);
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility), "턴당 한 번");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(path), log);
            CollectionAssert.AreEqual(path.GetRange(1, 3), s.Roads, "밟은 칸마다(출발 칸은 아니다)");
            Assert.AreEqual(0, s.Seats[seat].MovesLeft, "놓으면서 걷는 걸음은 보통 값");
            // 다른 좌석도 이어진 길을 이동력 1로 지난다
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            int other = s.ActiveSeat;
            Assert.AreNotEqual(seat, other);
            Stand(s, other, path[1], 1);
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Move && a.Target == path[3]), "길 세 칸의 끝까지 이동력 1");
            var along = ChoiceAnswer.MoveAlong(new List<int> { path[1], path[2], path[3] });
            Flow.Answer(s, ctx, ChoiceKind.MapAction, along, log);
            Assert.AreEqual(path[3], s.Seats[other].Cell);
            Assert.AreEqual(0, s.Seats[other].MovesLeft);
            Assert.IsFalse(s.Seats[other].Building);
            Assert.AreEqual(3, s.Roads.Count, "남이 지나가도 길은 늘지 않는다");
        }

        [Test]
        public void A20_6_worker_파랑_턴도중에켜면켜기전에밟은칸도길_결정84()
        {
            // 사용자 결정 84 T11 ② 나: 「any non-Ocean tiles you move onto this turn」 = 그 턴에 밟은 칸 모두(켜기 전 걸음도). 출발 칸은 밟은 칸이 아니다
            var log = new EventLog();
            var s = Table("worker", out int seat, log);
            int x = -1;
            List<int> path = null;
            for (int c = 0; c < ctx.Board.Count && path == null; c++)
            {
                if (ctx.Board[c].DistortionWorld || ctx.Board[c].Biome != Biome.Plains) continue;
                var reach = MoveRules.Reachable(ctx.Board, c, 3, s.Options.ElevationMode);
                for (int d = 0; d < reach.Length && path == null; d++)
                {
                    if (reach[d] != 3) continue;
                    var p = MoveRules.Path(ctx.Board, c, d, 3, s.Options.ElevationMode);
                    if (p.TrueForAll(k => ctx.Board[k].Biome != Biome.Ocean))
                    {
                        x = c;
                        path = p;
                    }
                }
            }
            Assert.IsNotNull(path);
            Stand(s, seat, x, 3);
            s.Seats[seat].Jp = 3;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(path.GetRange(0, 3)), log);
            Assert.AreEqual(0, s.Roads.Count, "켜기 전에는 길이 없다");
            CollectionAssert.AreEqual(path.GetRange(1, 2), s.Seats[seat].Trod, "밟은 칸을 적어 둔다");
            var build = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.UseAbility && a.Target == -1);
            Assert.IsNotNull(build);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, build, log);
            CollectionAssert.AreEqual(path.GetRange(1, 2), s.Roads, "켜기 전에 밟은 두 칸에 길(출발 칸은 아니다)");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(path.GetRange(2, 2)), log);
            CollectionAssert.AreEqual(path.GetRange(1, 3), s.Roads, "켠 뒤 걸음도");
        }
    }
}
