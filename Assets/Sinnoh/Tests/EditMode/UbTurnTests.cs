using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// UB 턴(automation R4~R8 · B4, rulings A51, M6 「UB 턴」 — plan M6 완료 조건 2): Lua `raw/lua/d66d964ab6.lua`의 점수표(:411-423) · 이웃을 섞어 훑기(:466-490) ·
    /// 점수 합(:492-503) · 첫 최고점(:505-515, get_max_key_and_value :215-223) · 턴(:611-667)을 손으로 따라간 답과 같은 칸을 고르는지 본다.
    /// 섞는 순서는 ScriptedRng로 정한다(<see cref="Order"/> — Piles.Shuffle이 그 순열을 내는 rng 값).
    /// </summary>
    public class UbTurnTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Ub1 = "793:0", Ub2 = "794:0";

        /// <summary>Piles.Shuffle(0..n−1)이 order를 내게 하는 rng.Next 값(끝에서 앞으로, j = 그 자리에 올 값이 지금 있는 곳).</summary>
        static List<int> Order(params int[] order)
        {
            var cur = new List<int>();
            for (int i = 0; i < order.Length; i++) cur.Add(i);
            var js = new List<int>();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = cur.IndexOf(order[i]);
                Assert.LessOrEqual(j, i, "순열이 아니다");
                js.Add(j);
                int t = cur[i];
                cur[i] = cur[j];
                cur[j] = t;
            }
            return js;
        }

        static ScriptedRng Rng(params int[] order) => new ScriptedRng(Order(order), null);

        bool HasFixture(int cell)
        {
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Cell == cell) return true;
            return false;
        }

        /// <summary>깨어진 세계 밖 야생 칸이고 점수가 붙는 고정물(마을·전설 장소)이 없다 — 나무·산호·사당 토큰은 <see cref="Clear"/>가 치운다(보드에 고정물 없는 칸이 드물다).</summary>
        bool Plain(int cell)
        {
            if (cell == Board.NoCell || !ctx.Board[cell].Wild || ctx.Board[cell].DistortionWorld) return false;
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Cell == cell && (f.Kind == FixtureKind.Village || f.Kind == FixtureKind.LegendarySite)) return false;
            return true;
        }

        /// <summary>그 칸과 이웃 칸의 나무·산호·사당 토큰을 보드에서 치운다(장면: 빈 야생 칸 = 1000점).</summary>
        int Clear(GameState s, int x)
        {
            var cells = new List<int>(UbScenes.Around(ctx, x)) { x };
            foreach (var t in s.Tokens)
                if (UbTurn.IsTarget(t.Kind) && cells.Contains(t.Cell)) t.Cell = -1;
            return x;
        }

        int Find(Predicate<int> which, string what)
        {
            for (int c = 0; c < ctx.Board.Count; c++)
                if (which(c)) return c;
            Assert.Fail($"{what} 칸이 없다");
            return -1;
        }

        /// <summary>이웃 여섯이 모두 맵 안의 그런 칸(<see cref="Clear"/> 뒤 이웃 점수가 모두 1000).</summary>
        int Open() => Find(c => Plain(c) && Array.TrueForAll(UbScenes.Around(ctx, c), Plain), "사방이 빈 야생");

        [Test]
        public void R4_점수표_야생대숨겨진장소()
        {
            // :411-423 야생 +1000 · 숨겨진 장소 +10 → 숨겨진 장소가 있는 야생 칸 1010이 섞은 순서와 상관없이 뽑힌다
            var s = UbScenes.Bare(ctx);
            int x = Clear(s, Open());
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            UbScenes.PlaceSpot(s, ctx, n[3]);
            for (int d = 0; d < 6; d++) Assert.AreEqual(d == 3 ? 1010 : 1000, UbTurn.Score(s, ctx, n[d]), $"방향 {d}");
            Assert.AreEqual(n[3], UbTurn.Choose(s, ctx, ub, Rng(0, 1, 2, 3, 4, 5)));
            Assert.AreEqual(n[3], UbTurn.Choose(s, ctx, ub, Rng(3, 5, 4, 2, 1, 0)));
            Assert.AreEqual(n[3], UbTurn.Choose(s, ctx, ub, Rng(5, 4, 0, 1, 2, 3)));
            // 유물·우두머리 토큰(Artifact Token)도 +10
            var s2 = UbScenes.Bare(ctx);
            Clear(s2, x);
            UbScenes.PlaceSpot(s2, ctx, n[1], item => item.Kind == ItemKind.Artifact);
            UbScenes.PlaceSpot(s2, ctx, n[2], item => item.Kind == ItemKind.AlphaPokemon);
            Assert.AreEqual(1010, UbTurn.Score(s2, ctx, n[1]));
            Assert.AreEqual(1010, UbTurn.Score(s2, ctx, n[2]));
        }

        [Test]
        public void R4_점수표_파괴된칸()
        {
            // 나무·산호·사당 +10(1010) → 파괴되면 −20(990): 멀쩡하면 그 칸으로, 파괴된 뒤에는 빈 야생 칸(1000)보다 낮아 피한다
            var s = UbScenes.Bare(ctx);
            int x = Clear(s, Open());
            const int dir = 2;
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            s.Tokens.Find(t => t.Kind == TokenKind.ApricornTree && t.Cell >= 0).Cell = n[dir]; // 다른 곳의 나무 토큰 하나를 이웃 칸에
            var tree = s.Tokens.Find(t => t.Cell == n[dir] && UbTurn.IsTarget(t.Kind));
            Assert.AreEqual(1010, UbTurn.Score(s, ctx, n[dir]));
            var last = new int[6];
            for (int i = 0; i < 6; i++) last[i] = (dir + 1 + i) % 6; // 나무 방향이 맨 뒤
            Assert.AreEqual(n[dir], UbTurn.Choose(s, ctx, ub, Rng(last)), "멀쩡한 나무 1010 > 1000");
            tree.Destroyed = true;
            Assert.AreEqual(990, UbTurn.Score(s, ctx, n[dir]));
            var first = new int[6];
            for (int i = 0; i < 6; i++) first[i] = (dir + i) % 6; // 나무 방향이 맨 앞
            Assert.AreEqual(n[(dir + 1) % 6], UbTurn.Choose(s, ctx, ub, Rng(first)), "파괴된 칸 990 < 1000 — 다음으로 훑은 빈 칸");
        }

        [Test]
        public void R4_점수표_방문한칸()
        {
            // 이번 턴 방문 −200(야생 칸에 붙는 표시): 800 < 1000
            var s = UbScenes.Bare(ctx);
            int x = Clear(s, Open());
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            for (int d = 0; d < 6; d++)
                if (d != 2) s.Ub.Visited.Add(n[d]);
            Assert.AreEqual(800, UbTurn.Score(s, ctx, n[0]));
            Assert.AreEqual(1000, UbTurn.Score(s, ctx, n[2]));
            Assert.AreEqual(n[2], UbTurn.Choose(s, ctx, ub, Rng(0, 1, 3, 4, 5, 2)), "방문하지 않은 칸 하나");
            s.Ub.Visited.Add(n[2]);
            Assert.AreEqual(n[4], UbTurn.Choose(s, ctx, ub, Rng(4, 0, 1, 2, 3, 5)), "모두 방문했으면 동점(800) — 섞은 순서 첫째로 간다(막히지 않는다)");
            // 숨겨진 장소가 있어도 방문한 칸은 810
            UbScenes.PlaceSpot(s, ctx, n[5]);
            Assert.AreEqual(810, UbTurn.Score(s, ctx, n[5]));
            Assert.AreEqual(n[5], UbTurn.Choose(s, ctx, ub, Rng(4, 0, 1, 2, 3, 5)));
        }

        [Test]
        public void R4_점수표_마을트레이너피하기()
        {
            // 마을·전설·트레이너 −100 · 다른 UB는 태그 둘(Ultra Beast −100 + Legendary Encounter Card −100) = −200
            var s = UbScenes.Bare(ctx, 2);
            int x = Clear(s, Open());
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            s.Seats[0].Cell = n[0];
            UbScenes.PlaceUb(s, ctx, Ub2, n[1]);
            s.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = Scene.Def(ctx, "144:0"), Cell = n[2], FaceUp = true });
            s.Ub.Visited.Add(n[3]);
            s.Seats[1].Cell = n[5];
            UbScenes.PlaceSpot(s, ctx, n[5]);
            CollectionAssert.AreEqual(new[] { 900, 800, 900, 800, 1000, 910 }, Array.ConvertAll(n, c => UbTurn.Score(s, ctx, c)));
            Assert.AreEqual(n[4], UbTurn.Choose(s, ctx, ub, Rng(0, 1, 2, 3, 5, 4)), "아무것도 없는 야생 칸");
            // 빈 칸이 없으면 덜 나쁜 칸: 트레이너 + 숨겨진 장소 910
            s.Ub.Visited.Add(n[4]);
            Assert.AreEqual(n[5], UbTurn.Choose(s, ctx, ub, Rng(0, 1, 2, 3, 4, 5)));
            // 탈락한 좌석의 말은 세지 않는다 [가정 A51 ②]
            s.Seats[0].Eliminated = true;
            Assert.AreEqual(1000, UbTurn.Score(s, ctx, n[0]));
            // 고정물: 마을(야생 칸 위) 900 · 전설 장소 900(야생)·−100(야생 아님) · 울트라메가로폴리스 0(야생 아님 · Village Token 태그 없음) · 네크로즈마 칸 900
            var bare = UbScenes.Bare(ctx);
            Assert.AreEqual(900, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "village.eterna")].Cell));
            Assert.AreEqual(900, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "legendary_site.magma_chamber")].Cell));
            Assert.AreEqual(-100, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "legendary_site.temple_of_sinnoh")].Cell));
            Assert.AreEqual(0, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "village.ultra_megalopolis")].Cell));
            int centre = UltraBurstSetup.NecrozmaCell(ctx);
            Assert.AreEqual(1000, UbTurn.Score(bare, ctx, centre));
            bare.Tokens.Add(new TokenState { Kind = TokenKind.Necrozma, Def = Scene.Def(ctx, "800:1"), Cell = centre, FaceUp = true });
            Assert.AreEqual(900, UbTurn.Score(bare, ctx, centre));
            // 왜곡된 나무 · 게이트 · 전설 사당은 점수가 없다(태그가 표에 없다)
            Assert.AreEqual(1000, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "distorted_tree.1")].Cell));
            Assert.AreEqual(1000, UbTurn.Score(bare, ctx, ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "gate.lilly_main")].Cell));
        }

        [Test]
        public void R4_점수표_동점은섞은순서첫째()
        {
            // get_max_key_and_value(:215-223)는 엄밀히 큰 것만 바꾼다 → 동점이면 섞은 순서(:468)에서 처음 나온 칸
            var s = UbScenes.Bare(ctx);
            int x = Clear(s, Open());
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            Assert.AreEqual(n[4], UbTurn.Choose(s, ctx, ub, Rng(4, 2, 0, 1, 3, 5)));
            Assert.AreEqual(n[1], UbTurn.Choose(s, ctx, ub, Rng(1, 5, 0, 2, 3, 4)));
            Assert.AreEqual(n[0], UbTurn.Choose(s, ctx, ub, Rng(0, 1, 2, 3, 4, 5)));
            // 동점인 최고점 둘(숨겨진 장소 1010 둘) 가운데도 먼저 훑은 쪽
            UbScenes.PlaceSpot(s, ctx, n[2]);
            UbScenes.PlaceSpot(s, ctx, n[5]);
            Assert.AreEqual(n[5], UbTurn.Choose(s, ctx, ub, Rng(0, 5, 1, 2, 3, 4)));
            Assert.AreEqual(n[2], UbTurn.Choose(s, ctx, ub, Rng(0, 2, 1, 5, 3, 4)));
            var rng = Rng(0, 2, 1, 5, 3, 4);
            UbTurn.Choose(s, ctx, ub, rng);
            Assert.IsTrue(rng.Exhausted, "이웃 섞기는 rng 5번");
        }

        [Test]
        public void R4_점수표_맵가장자리()
        {
            // 맵 밖은 훑은 것이 없어 0점(:492-503) — 야생 칸(1000)에 밀린다
            var s = UbScenes.Bare(ctx);
            int x = Find(c =>
            {
                if (!Plain(c)) return false;
                var a = UbScenes.Around(ctx, c);
                int off = 0;
                foreach (int k in a)
                {
                    if (k == Board.NoCell) off++;
                    else if (!Plain(k)) return false;
                }
                return off >= 2;
            }, "가장자리");
            Clear(s, x);
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            Assert.AreEqual(0, UbTurn.Score(s, ctx, Board.NoCell));
            var order = new List<int>();
            for (int d = 0; d < 6; d++)
                if (n[d] == Board.NoCell) order.Add(d);
            int firstOn = -1;
            for (int d = 0; d < 6; d++)
                if (n[d] != Board.NoCell)
                {
                    if (firstOn < 0) firstOn = d;
                    order.Add(d);
                }
            Assert.AreEqual(n[firstOn], UbTurn.Choose(s, ctx, ub, Rng(order.ToArray())), "맵 밖을 먼저 훑어도 맵 안 첫 야생 칸");
            // 맵 밖이 뽑히면(모든 이웃이 0보다 낮을 때) 옮기지 않는다
            Assert.IsFalse(UbTurn.Step(s, ctx, new Emitter(s, null), ub, Board.NoCell, 0, Rng()));
            Assert.AreEqual(x, s.Tokens[ub].Cell);
        }

        [Test]
        public void R4_총이동은4더하기균열10분의1_방문표시는UB가선칸만남는다()
        {
            // :612 한도 = 4 + ⌊턴 시작 균열/10⌋ · :625-632 옮길 때마다 센다 · :637 한도에서 멈춤 · :446-464 방문 표시는 UB가 선 칸만
            for (int destroyed = 0; destroyed <= 13; destroyed += 13)
            {
                var s = UbScenes.Bare(ctx);
                int ub = UbScenes.PlaceUb(s, ctx, Ub1, Clear(s, Open()));
                int marked = 0;
                foreach (var t in s.Tokens)
                    if (UbTurn.IsTarget(t.Kind) && t.Cell >= 0 && marked < destroyed)
                    {
                        t.Destroyed = true;
                        marked++;
                    }
                int breach = UbTurn.Breach(s);
                Assert.AreEqual(destroyed == 0 ? 0 : 10, breach);
                var log = new EventLog();
                UbTurn.Run(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), new Pcg32(3));
                var moves = Flow.Of<UbMoved>(log);
                Assert.AreEqual(4 + breach / 10, moves.Count, "UB 하나가 여러 바퀴 돌며 한도까지");
                for (int i = 0; i < moves.Count; i++)
                {
                    Assert.AreEqual(i, moves[i].StepIdx);
                    Assert.AreEqual(s.Tokens[ub].Def, moves[i].UbId);
                    if (i > 0) Assert.AreEqual(moves[i - 1].To, moves[i].From);
                }
                var started = Flow.Of<UbTurnStarted>(log);
                Assert.AreEqual(4 + breach / 10, started[0].TotalMoves);
                Assert.AreEqual(breach, started[0].Breach);
                Assert.AreEqual(breach, s.Ub.BreachAtTurnStart);
                CollectionAssert.AreEqual(new[] { s.Tokens[ub].Cell }, s.Ub.Visited, "UB가 선 칸의 표시만 남는다");
            }
            // UB 아홉이면 한 바퀴의 앞 넷만 옮긴다(총 이동은 모두가 나눠 쓴다 — p.25 「4 tile moves per turn shared between all of them」)
            var all = UbScenes.Started(ctx, 2, 4);
            var before = all.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast).ConvertAll(t => t.Cell);
            var alog = new EventLog();
            UbScenes.EndRound(all, ctx, alog);
            var after = all.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast).ConvertAll(t => t.Cell);
            int changed = 0;
            for (int i = 0; i < before.Count; i++)
                if (before[i] != after[i]) changed++;
            Assert.AreEqual(4, Flow.Of<UbMoved>(alog).Count);
            Assert.AreEqual(4, changed, "서로 다른 UB 넷");
            Assert.AreEqual(4, all.Ub.Visited.Count);
        }

        [Test]
        public void R4_한바퀴아무도못움직이면멈춤()
        {
            // :640 한 바퀴에 이동 수가 그대로면 멈춘다 — UB가 없거나(모두 쓰러뜨림) 고른 칸이 야생 칸이 아닌 때. 네크로즈마의 파괴는 그래도 한다(:646)
            var s = UbScenes.Bare(ctx, 3);
            var log = new EventLog();
            UbTurn.Run(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), new Pcg32(1));
            Assert.AreEqual(0, Flow.Of<UbMoved>(log).Count);
            Assert.AreEqual(1, Flow.Of<NecrozmaStrike>(log).Count);
            Assert.AreEqual(2, s.Ub.Destroyed);
            // 보드 밖(쓰러진) UB 토큰은 돌지 않는다
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, Clear(s, Open()));
            s.Tokens[ub].Cell = -1;
            log.Clear();
            UbTurn.Run(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), new Pcg32(1));
            Assert.AreEqual(0, Flow.Of<UbMoved>(log).Count);
        }

        [Test]
        public void R5_숨겨진장소는깨어진세계의빈칸으로_없으면버림()
        {
            // :545-552 숨겨진 장소·유물 → 깨어진 세계의 안전한 야생 칸(:517-543), 없으면 버림 존. 뒷면은 그대로
            var s = UbScenes.Bare(ctx);
            int x = Clear(s, Open());
            var n = UbScenes.Around(ctx, x);
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, x);
            int spot = UbScenes.PlaceSpot(s, ctx, n[0]);
            var log = new EventLog();
            var fx = Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn);
            Assert.IsTrue(UbTurn.Step(s, ctx, fx, ub, n[0], 0, new Pcg32(5)));
            Assert.AreEqual(n[0], s.Tokens[ub].Cell);
            int to = s.Tokens[spot].Cell;
            Assert.IsTrue(to >= 0 && ctx.Board[to].Wild && ctx.Board[to].DistortionWorld, "깨어진 세계의 야생 칸");
            Assert.IsFalse(s.Tokens[spot].FaceUp);
            Assert.AreNotEqual(UltraBurstSetup.NecrozmaCell(ctx), to);
            foreach (var f in ctx.Fixtures.Fixtures) Assert.AreNotEqual(to, f.Cell, "왜곡된 나무·게이트 칸이 아니다");
            var moved = Flow.Of<TokenMoved>(log);
            Assert.AreEqual(1, moved.Count);
            Assert.AreEqual(n[0], moved[0].From);
            Assert.AreEqual(to, moved[0].To);
            Assert.AreEqual(TokenKind.HiddenSpot, moved[0].TokenKind);
            // 빈 칸: 야생 24 − 왜곡된 나무 6 − 게이트 4 − 가운데 1 = 13. 다 차면 버린다(To = −1)
            int free = 1;
            while (free < 13)
            {
                int k = UbScenes.PlaceSpot(s, ctx, n[1]);
                UbTurn.Interact(s, ctx, fx, n[1], s.Tokens[ub].Def, new Pcg32((ulong)free));
                Assert.GreaterOrEqual(s.Tokens[k].Cell, 0, $"{free + 1}번째도 자리가 있다");
                free++;
            }
            var cells = new HashSet<int>();
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0) Assert.IsTrue(cells.Add(t.Cell), "한 칸에 하나");
            Assert.AreEqual(13, cells.Count);
            int extra = UbScenes.PlaceSpot(s, ctx, n[1]);
            log.Clear();
            UbTurn.Interact(s, ctx, fx, n[1], s.Tokens[ub].Def, new Pcg32(9));
            Assert.AreEqual(-1, s.Tokens[extra].Cell, "빈 칸이 없으면 버린다");
            Assert.AreEqual(-1, Flow.Of<TokenMoved>(log)[0].To);
            // 말이 선 칸은 빈 칸이 아니다
            var s2 = UbScenes.Bare(ctx);
            for (int c = 0; c < ctx.Board.Count; c++)
            {
                if (!ctx.Board[c].Wild || !ctx.Board[c].DistortionWorld) continue;
                s2.Seats[0].Cell = c;
                for (ulong seed = 0; seed < 4; seed++) Assert.AreNotEqual(c, UbTurn.FreeDistortionCell(s2, ctx, new Pcg32(seed)));
            }
        }

        [Test]
        public void R5_고른칸이야생이아니면옮기지않아도_그칸의것은처리한다()
        {
            // :625-634 야생 칸일 때만 옮기고 센다 — handle_object_interactions는 옮기지 않아도 고른 쪽에 부른다
            var s = UbScenes.Bare(ctx);
            int nonWild = Find(c => !ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && !HasFixture(c), "고정물 없는 야생 아닌");
            int ub = UbScenes.PlaceUb(s, ctx, Ub1, Clear(s, Open()));
            int at = s.Tokens[ub].Cell;
            int spot = UbScenes.PlaceSpot(s, ctx, nonWild);
            var log = new EventLog();
            Assert.IsFalse(UbTurn.Step(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), ub, nonWild, 0, new Pcg32(2)), "옮기지 않는다(세지 않는다)");
            Assert.AreEqual(at, s.Tokens[ub].Cell);
            Assert.AreEqual(0, Flow.Of<UbMoved>(log).Count);
            Assert.AreEqual(0, s.Ub.Visited.Count);
            Assert.AreNotEqual(nonWild, s.Tokens[spot].Cell, "그 칸의 숨겨진 장소는 보낸다");
            Assert.AreEqual(1, Flow.Of<TokenMoved>(log).Count);
        }

        [Test]
        public void R5_나무와사당은파괴되고_조우와공물을못한다()
        {
            // :553-557 사당·나무 → Encounter 태그를 떼고 Breach Claimed. p.25 「destroyed preventing all further interactions with those tokens by trainers」
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 6, log);
            var st = s.Seats[s.ActiveSeat];
            var tree = s.Tokens.Find(t => t.Kind == TokenKind.ApricornTree);
            var shrine = s.Tokens.Find(t => t.Kind == TokenKind.Shrine);
            for (int c = 0; c < SeatState.ApricornColors; c++) st.Apricorns[c] = 5;
            st.MovesLeft = 3;
            st.Cell = tree.Cell;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Search), "멀쩡한 나무에서는 찾아본다");
            st.Cell = shrine.Cell;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "멀쩡한 사당에서는 바친다");

            log.Clear();
            var fx = Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn);
            int ub = s.Tokens.Find(t => t.Kind == TokenKind.UltraBeast).Def;
            UbTurn.Interact(s, ctx, fx, tree.Cell, ub, new Pcg32(1));
            UbTurn.Interact(s, ctx, fx, shrine.Cell, ub, new Pcg32(1));
            Assert.IsTrue(tree.Destroyed && shrine.Destroyed);
            Assert.AreEqual(2, s.Ub.Destroyed);
            var destroyed = Flow.Of<CellDestroyed>(log);
            Assert.AreEqual(2, destroyed.Count);
            Assert.AreEqual(tree.Cell, destroyed[0].Cell);
            Assert.AreEqual(TokenKind.ApricornTree, destroyed[0].TokenKind);
            Assert.AreEqual(ub, destroyed[0].ByUbId);
            Assert.AreEqual(TokenKind.Shrine, destroyed[1].TokenKind);
            var changed = Flow.Of<BreachChanged>(log);
            Assert.AreEqual(2, changed.Count, "0 → 1 → 2%");
            Assert.AreEqual(2, changed[1].After);
            Assert.AreEqual(UltraBurstRules.BreachStrength(2), UbTurn.Breach(s));
            // 이미 파괴된 칸을 다시 밟아도 그대로
            UbTurn.Interact(s, ctx, fx, tree.Cell, ub, new Pcg32(1));
            Assert.AreEqual(2, Flow.Of<CellDestroyed>(log).Count);
            Assert.AreEqual(2, s.Ub.Destroyed);

            st.Cell = tree.Cell;
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(Flow.Has(legal, AnswerKind.Search), "파괴된 나무에서는 찾아보지 못한다");
            Assert.IsFalse(Flow.Has(legal, AnswerKind.TakeApricorns));
            st.Cell = shrine.Cell;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "파괴된 사당에는 바치지 못한다");
        }

        [Test]
        public void R6_네크로즈마는남은나무사당중인원절반올림()
        {
            // :563-596 나무·사당을 섞어 Breach Claimed가 아닌 것부터 ⌈인원/2⌉개(:587)
            var expected = new[] { 1, 1, 2, 2, 3, 3 };
            for (int seats = 1; seats <= 6; seats++)
            {
                var s = UbScenes.Bare(ctx, seats);
                var log = new EventLog();
                UbTurn.Strike(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), new Pcg32((ulong)seats));
                var strike = Flow.Of<NecrozmaStrike>(log);
                Assert.AreEqual(1, strike.Count);
                Assert.AreEqual(expected[seats - 1], strike[0].Count, $"{seats}인");
                Assert.AreEqual(expected[seats - 1], strike[0].Cells.Length);
                Assert.AreEqual(expected[seats - 1], s.Ub.Destroyed);
                var destroyed = Flow.Of<CellDestroyed>(log);
                Assert.AreEqual(expected[seats - 1], destroyed.Count);
                foreach (var d in destroyed)
                {
                    Assert.AreEqual(-1, d.ByUbId, "원격");
                    CollectionAssert.Contains(strike[0].Cells, d.Cell);
                    Assert.IsTrue(UbTurn.IsTarget(d.TokenKind));
                }
                Assert.AreEqual(expected[seats - 1], new HashSet<int>(strike[0].Cells).Count, "서로 다른 칸");
            }
            // 남은 것이 모자라면 남은 만큼만 — 이미 파괴된 것은 다시 고르지 않는다
            var few = UbScenes.Bare(ctx, 6);
            var targets = few.Tokens.FindAll(t => UbTurn.IsTarget(t.Kind));
            Assert.AreEqual(UltraBurstRules.BreachTargets, targets.Count, "사당 13 + 나무 86 + 산호 32");
            for (int i = 1; i < targets.Count; i++) targets[i].Destroyed = true;
            var flog = new EventLog();
            UbTurn.Strike(few, ctx, Scene.Fx(few, flog, SeatId.System, ActionKind.UbTurn), new Pcg32(8));
            Assert.IsTrue(targets[0].Destroyed);
            Assert.AreEqual(1, Flow.Of<CellDestroyed>(flog).Count);
            Assert.AreEqual(100, UbTurn.Breach(few));
            // 말이 서 있는 칸도 친다(원작 커뮤니티 확인)
            var stand = UbScenes.Bare(ctx, 1);
            var live = stand.Tokens.FindAll(t => UbTurn.IsTarget(t.Kind));
            for (int i = 1; i < live.Count; i++) live[i].Destroyed = true;
            stand.Seats[0].Cell = live[0].Cell;
            UbTurn.Strike(stand, ctx, new Emitter(stand, null), new Pcg32(8));
            Assert.IsTrue(live[0].Destroyed);
        }

        /// <summary>UB를 모두 보드에서 치우고 나무·사당 n개를 파괴해 둔다(라운드 끝에 네크로즈마만 친다).</summary>
        static void Prepare(GameState s, int destroyed)
        {
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.UltraBeast) t.Cell = -1;
            int n = 0;
            foreach (var t in s.Tokens)
                if (UbTurn.IsTarget(t.Kind) && n < destroyed)
                {
                    t.Destroyed = true;
                    n++;
                }
        }

        [Test]
        public void R8_70이면패배()
        {
            // :652 균열 ≥ 70이면 게임 오버 · :600 그때는 균열 카드를 주지 않는다. 원작 커뮤니티 확인
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 1, 3, log);
            Prepare(s, 91); // 91/131 = 69% → 네크로즈마 1개 → 92/131 = 70%
            Assert.AreEqual(69, UbTurn.Breach(s));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            UbScenes.NoSkip(s, ctx, log);
            Assert.AreEqual(Phase.GameOver, s.Phase);
            Assert.AreEqual(UbResult.Lost, s.Ub.Result);
            Assert.AreEqual(70, UbTurn.Breach(s));
            var ended = Flow.Of<GameEnded>(log);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(GameEndReason.BreachLost, ended[0].Reason);
            Assert.AreEqual(0, ended[0].Winners.Length, "이긴 좌석이 없다");
            Assert.AreEqual(0, Flow.Of<RoundStarted>(log).Count, "새 라운드를 열지 않는다");
            Assert.AreEqual(0, Flow.Of<BreachCardsDealt>(log).Count);
            Assert.IsFalse(s.Pending.Exists(c => !c.Answered), "열린 선택이 없다");
            // 69%에서 멈추면 이어진다
            var s2 = UbScenes.Started(ctx, 1, 3);
            Prepare(s2, 90); // 90 → 91 = 69%
            Flow.Answer(s2, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn));
            UbScenes.NoSkip(s2, ctx);
            Assert.AreEqual(Phase.Turn, s2.Phase);
            Assert.AreEqual(UbResult.None, s2.Ub.Result);
            Assert.AreEqual(2, s2.Round);
        }

        [Test]
        public void B4_넘은구간만큼좌석마다균열카드()
        {
            // 룰북 p.24 「At every increment of 10% breach strength, trainers will be dealt a random Distortion Breach card」 — 기본 옵션 EachBracketCrossed
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 3, log);
            Prepare(s, 12); // 12/131 = 9% → 네크로즈마 1개 → 13/131 = 10%
            Assert.AreEqual(9, UbTurn.Breach(s));
            int deck = s.Decks[(int)DeckId.DistortionBreach].Count;
            Assert.AreEqual(36, deck);
            log.Clear();
            UbScenes.EndRound(s, ctx, log);
            Assert.AreEqual(10, UbTurn.Breach(s));
            var bracket = Flow.Of<BreachBracket>(log);
            Assert.AreEqual(1, bracket.Count);
            Assert.AreEqual(9, bracket[0].Before);
            Assert.AreEqual(10, bracket[0].After);
            Assert.AreEqual(1, bracket[0].Brackets);
            var dealt = Flow.Of<BreachCardsDealt>(log);
            Assert.AreEqual(1, dealt.Count);
            Assert.AreEqual(2, dealt[0].PerSeat.Length);
            for (int seat = 0; seat < 2; seat++)
            {
                Assert.AreEqual(seat, dealt[0].PerSeat[seat].Seat);
                CollectionAssert.AreEqual(UbTurn.BreachHand(s, seat), dealt[0].PerSeat[seat].CardKeys);
                Assert.AreEqual(1, UbTurn.BreachHand(s, seat).Count);
            }
            Assert.AreEqual(34, s.Decks[(int)DeckId.DistortionBreach].Count);
            // 다음 UB 턴의 총 이동은 5(R4)
            log.Clear();
            UbScenes.EndRound(s, ctx, log);
            Assert.AreEqual(5, Flow.Of<UbTurnStarted>(log)[0].TotalMoves);
            Assert.AreEqual(0, Flow.Of<BreachCardsDealt>(log).Count, "구간을 넘지 않으면 주지 않는다");

            // 한 턴에 두 구간을 넘으면 2장씩
            var two = UbScenes.Bare(ctx, 3);
            var tlog = new EventLog();
            UbTurn.DealBreachCards(two, ctx, Scene.Fx(two, tlog, SeatId.System, ActionKind.UbTurn), 8, 21);
            Assert.AreEqual(2, Flow.Of<BreachBracket>(tlog)[0].Brackets);
            for (int seat = 0; seat < 3; seat++) Assert.AreEqual(2, UbTurn.BreachHand(two, seat).Count);
            Assert.AreEqual(30, two.Decks[(int)DeckId.DistortionBreach].Count);
            // 덱이 떨어지면 버림 더미를 섞어 잇고, 그래도 없으면 남은 만큼만
            var deckPile = two.Decks[(int)DeckId.DistortionBreach];
            var discard = two.Discards[(int)DeckId.DistortionBreach];
            discard.AddRange(deckPile.GetRange(0, 28));
            deckPile.RemoveRange(0, 28);
            discard.RemoveRange(2, 26); // 덱 2 + 버림 2
            tlog.Clear();
            UbTurn.DealBreachCards(two, ctx, Scene.Fx(two, tlog, SeatId.System, ActionKind.UbTurn), 21, 43);
            Assert.AreEqual(1, Flow.Of<PileShuffled>(tlog).Count);
            Assert.AreEqual(0, deckPile.Count + discard.Count);
            Assert.AreEqual(4, UbTurn.BreachHand(two, 0).Count);
            Assert.AreEqual(4, UbTurn.BreachHand(two, 1).Count);
            Assert.AreEqual(2, UbTurn.BreachHand(two, 2).Count, "덱이 바닥나 받지 못했다");
        }

        [Test]
        public void B4_옵션OncePerTurn이면한턴에한장()
        {
            // Lua handle_breach_cards(:598-609)는 구간을 몇 개 넘든 deal(1)
            var s = UbScenes.Bare(ctx, 3, 7, new RuleOptions { BreachCardsPerBracket = BreachCardsPerBracket.OncePerTurn });
            var log = new EventLog();
            UbTurn.DealBreachCards(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), 8, 21);
            for (int seat = 0; seat < 3; seat++) Assert.AreEqual(1, UbTurn.BreachHand(s, seat).Count);
            Assert.AreEqual(2, Flow.Of<BreachBracket>(log)[0].Brackets, "넘은 구간 수는 그대로 알린다");
            // 70% 이상이면 어느 옵션이든 주지 않는다
            log.Clear();
            UbTurn.DealBreachCards(s, ctx, Scene.Fx(s, log, SeatId.System, ActionKind.UbTurn), 65, 70);
            Assert.AreEqual(0, Flow.Of<BreachCardsDealt>(log).Count);
        }

        [Test]
        public void M6_UB턴은같은시드면같은결과()
        {
            GameState Play(ulong seed, EventLog log)
            {
                var s = UbScenes.Started(ctx, 3, seed, log);
                for (int i = 0; i < 4; i++) UbScenes.EndRound(s, ctx, log);
                return s;
            }
            var la = new EventLog();
            var lb = new EventLog();
            var a = Play(21, la);
            var b = Play(21, lb);
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b));
            Assert.AreEqual(la.Events.Count, lb.Events.Count);
            var ma = Flow.Of<UbMoved>(la);
            var mb = Flow.Of<UbMoved>(lb);
            Assert.GreaterOrEqual(ma.Count, 16, "4라운드 × 4칸 이상");
            for (int i = 0; i < ma.Count; i++)
            {
                Assert.AreEqual(ma[i].UbId, mb[i].UbId);
                Assert.AreEqual(ma[i].To, mb[i].To);
            }
            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(Play(22, new EventLog())));
            // 복제한 판에서 이어도 같다(방문 표시·손의 균열 카드가 복제에 든다)
            var c = UbScenes.Started(ctx, 3, 21);
            UbScenes.EndRound(c, ctx);
            var d = c.Clone();
            for (int i = 0; i < 3; i++)
            {
                UbScenes.EndRound(c, ctx);
                UbScenes.EndRound(d, ctx);
            }
            Assert.AreEqual(StateHash.Of(c), StateHash.Of(d));
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(c));
        }

        [Test, Category("Slow")]
        public void M6_UB봇판은승패로끝난다()
        {
            // 봇은 UB·호수 시련·네크로즈마를 사냥한다(M6 「봇 — UB 판」, PlayoutUb) — 균열 패배 · 모두 탈락 패배(탈락·부활·축복)로 끝난다. 여러 시드는 UbPlayoutTests.M6_봇플레이아웃UB.
            // 끝까지 예외 0(Playout.Run의 명령마다 확인 · 카드 총수에 손의 균열 카드)
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
            {
                GameState last = null;
                var g = Playout.Run(ctx, seats, (ulong)(40 + seats), s => last = s, null, Campaign.UltraBurst);
                Assert.IsTrue(g.Ended, $"{seats}인");
                Assert.IsTrue(g.Reason == GameEndReason.BreachLost || g.Reason == GameEndReason.AllEliminated || g.Reason == GameEndReason.NecrozmaDefeated, g.Reason.ToString());
                Assert.AreEqual(g.Reason == GameEndReason.NecrozmaDefeated ? UbResult.Won : UbResult.Lost, last.Ub.Result);
                if (g.Reason == GameEndReason.BreachLost) Assert.GreaterOrEqual(UbTurn.Breach(last), UltraBurstRules.LossThreshold);
                else if (g.Reason == GameEndReason.AllEliminated) Assert.AreEqual(0, last.Ub.Blessings);
                int hands = 0;
                for (int seat = 0; seat < seats; seat++) hands += UbTurn.BreachHand(last, seat).Count;
                foreach (var p in last.Pokemon) if (p.Def >= 0) hands += BreachRules.Attached(p); // 붙인 카드(M6 「균열 카드 36장」)
                Assert.Greater(hands, 0, "균열 카드를 받았다");
                TestContext.WriteLine($"UB 봇 판 {seats}인: {g.Reason} · 탈락 {System.Array.FindAll(last.Seats, x => x.Eliminated).Length} · 축복 {last.Ub.Blessings} · 네크로즈마 페이즈 {last.Ub.NecrozmaPhase} · 라운드 {g.Rounds} · 명령 {g.Commands} · 균열 {UbTurn.Breach(last)}% · 손의 균열 카드 {hands}");
            }
        }
    }
}
