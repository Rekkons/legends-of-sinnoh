using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 울트라 버스트 셋업·맵(룰북 p.23·p.26, rulings A50, M6 「UB 셋업·맵」): 시작 규토리·파티 칸·축복, UB 9마리와 네크로즈마의 시작 자리(Lua 2d936ce9b1.lua:467-562),
    /// 이동력 3(막 보너스 없음), 왜곡 게이트 이동, 울트라메가로폴리스, 라운드 끝의 UB 턴 자리, 막 없이 도는 봇 판.
    /// </summary>
    public class UbSetupTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        [Test]
        public void S17_2_규토리는빨강파랑노랑3씩_파티는6칸_막은없다()
        {
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
            {
                var log = new EventLog();
                var s = UbScenes.Started(ctx, seats, (ulong)(30 + seats), log);
                Assert.IsNull(s.Hos, "울트라 버스트에는 막이 없다");
                Assert.IsNotNull(s.Ub);
                Assert.AreEqual(Phase.Turn, s.Phase);
                foreach (var seat in s.Seats)
                {
                    for (int c = 0; c < SeatState.ApricornColors; c++)
                        Assert.AreEqual(c <= (int)ApricornColor.Yellow ? 3 : 0, seat.Apricorns[c], $"{seats}인 {(ApricornColor)c}");
                    Assert.AreEqual(6, seat.OpenSlots, "p.23 all 6 Pokémon slots unlocked");
                    Assert.AreEqual(0, seat.ActProgress);
                }
                Assert.AreEqual(0, Flow.Of<ActRevealed>(log).Count, "서막·1막 카드를 공개하지 않는다");
                Assert.IsFalse(s.Tokens.Exists(t => t.Kind == TokenKind.Grunt), "조무래기 토큰이 없다");
                var gained = Flow.Of<ApricornsGained>(log);
                Assert.AreEqual(1, gained.Count);
                Assert.AreEqual(seats * 3, gained[0].Changes.Length);
            }
            // 신오의 심장은 그대로: 빨강 3(1막)뿐, 3칸
            var hos = Flow.Started(ctx, 2, 31);
            Assert.AreEqual(3, hos.Seats[0].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(0, hos.Seats[0].Apricorns[(int)ApricornColor.Blue]);
            Assert.AreEqual(3, hos.Seats[0].OpenSlots);
            Assert.IsFalse(hos.Tokens.Exists(t => t.Kind == TokenKind.UltraBeast || t.Kind == TokenKind.Necrozma), "신오의 심장에는 UB가 없다");
        }

        [TestCase(UbDifficulty.NewTrainer, 3)]
        [TestCase(UbDifficulty.VeteranTrainer, 1)]
        [TestCase(UbDifficulty.MasterTrainer, 0)]
        public void S17_2_축복은난이도대로(UbDifficulty difficulty, int blessings)
        {
            var s = UbScenes.Started(ctx, 2, 5, null, new RuleOptions { UbDifficulty = difficulty });
            Assert.AreEqual(blessings, s.Ub.Blessings);
            Assert.AreEqual(UbState.PrismArmourTotal, s.Ub.PrismUltraBurst, "프리즘 아머 9개는 모두 울트라 버스트 쪽에서 시작(p.24)");
            Assert.AreEqual(0, s.Ub.PrismUltraBeast);
        }

        [Test]
        public void S17_2_UB판은이동력3에서시작_막보너스가없다()
        {
            // 원작 커뮤니티 확인
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 9, log);
            for (int round = 0; round < 3; round++) UbScenes.EndRound(s, ctx, log);
            var sets = Flow.Of<MoveAllowanceSet>(log);
            Assert.GreaterOrEqual(sets.Count, 7);
            foreach (var m in sets)
            {
                Assert.AreEqual(MoveRules.BaseMovement, m.Total, "심장·1막 보너스 없이 3");
                Assert.AreEqual(0, m.Mods.Length);
            }
            Assert.AreEqual(3, s.Seats[s.ActiveSeat].MovesLeft);
        }

        [Test]
        public void M6_UB셋업_토큰9와네크로즈마()
        {
            // Lua 2d936ce9b1.lua:467-472(토큰의 지형·기후 태그가 맞는 야생 칸) · :474-494(깨어진 세계·쓴 지역·고정물·토큰 칸 제외) · :556-562(UB 먼저, 네크로즈마는 깨어진 세계 가운데)
            var ubs = UltraBurstSetup.UltraBeasts(ctx);
            Assert.AreEqual(9, ubs.Count);
            int centre = UbScenes.Cell(ctx, -10, 18);
            Assert.AreEqual(centre, UltraBurstSetup.NecrozmaCell(ctx));
            Assert.IsTrue(ctx.Board[centre].Wild && ctx.Board[centre].DistortionWorld);
            for (ulong seed = 0; seed < 30; seed++)
            {
                var log = new EventLog();
                var s = UbScenes.Started(ctx, 1 + (int)(seed % 6), seed, log);
                var tokens = s.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast);
                Assert.AreEqual(9, tokens.Count, $"시드 {seed}");
                var defs = new HashSet<int>();
                var cells = new HashSet<int>();
                var regions = new HashSet<string>();
                foreach (var t in tokens)
                {
                    Assert.IsTrue(defs.Add(t.Def), "UB마다 하나");
                    CollectionAssert.Contains(ubs, t.Def);
                    Assert.IsTrue(t.FaceUp);
                    Assert.IsTrue(cells.Add(t.Cell), "한 칸에 하나");
                    var cell = ctx.Board[t.Cell];
                    Assert.IsTrue(cell.Wild, "야생 칸");
                    Assert.IsFalse(cell.DistortionWorld, "깨어진 세계 밖");
                    Assert.IsTrue(UltraBurstSetup.Habitat(ctx.Cards.Pokemon[t.Def].Key, out var biome, out var climate));
                    Assert.AreEqual(biome, cell.Biome, $"시드 {seed} {ctx.Cards.Pokemon[t.Def].Key} 지형");
                    Assert.AreEqual(climate, cell.Climate, $"시드 {seed} {ctx.Cards.Pokemon[t.Def].Key} 기후");
                    foreach (var f in ctx.Fixtures.Fixtures)
                        if (f.Kind != FixtureKind.Taxi) Assert.AreNotEqual(t.Cell, f.Cell, $"고정물 {f.Key} 칸에는 놓지 않는다");
                    Assert.IsTrue(regions.Add(cell.Region), $"시드 {seed}: 지역 {cell.Region}이 겹쳤다");
                }
                var necrozma = s.Tokens.FindAll(t => t.Kind == TokenKind.Necrozma);
                Assert.AreEqual(1, necrozma.Count);
                Assert.AreEqual(centre, necrozma[0].Cell);
                Assert.AreEqual(Scene.Def(ctx, "800:1"), necrozma[0].Def);
                // 숨겨진 장소·유물은 UB가 선 칸과 깨어진 세계에 놓이지 않는다(같은 claimed 목록을 이어 쓴다)
                int spots = 0;
                foreach (var t in s.Tokens)
                {
                    if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0) continue;
                    spots++;
                    Assert.IsFalse(cells.Contains(t.Cell), "UB 칸에 숨겨진 장소");
                    Assert.IsFalse(ctx.Board[t.Cell].DistortionWorld, "숨겨진 장소는 깨어진 세계 밖에서 시작한다(:476)");
                }
                Assert.AreEqual(52, spots);
                var placed = Flow.Of<TokensPlaced>(log);
                Assert.AreEqual(3, placed.Count, "UB · 네크로즈마 · 숨겨진 장소");
                Assert.AreEqual(TokenKind.UltraBeast, placed[0].Kind);
                CollectionAssert.AreEqual(tokens.ConvertAll(t => t.Cell), placed[0].Cells, "토큰 순서");
                Assert.AreEqual(TokenKind.Necrozma, placed[1].Kind);
                Assert.AreEqual(TokenKind.HiddenSpot, placed[2].Kind);
            }
        }

        [Test]
        public void M6_UB셋업_서식지표는UB9마리_피규어배치와같은표()
        {
            var seen = new HashSet<string>();
            foreach (int def in UltraBurstSetup.UltraBeasts(ctx))
            {
                string key = ctx.Cards.Pokemon[def].Key;
                Assert.IsTrue(UltraBurstSetup.Habitat(key, out var biome, out var climate), key);
                Assert.AreNotEqual(Biome.None, biome);
                Assert.AreNotEqual(Climate.None, climate);
                Assert.IsTrue(seen.Add(biome + "/" + climate), $"{key}: 서식지가 겹친다");
                Assert.IsTrue(FigureLayout.UltraBeastHabitat(key, out var b2, out var c2));
                Assert.AreEqual(biome, b2);
                Assert.AreEqual(climate, c2);
                int candidates = 0;
                for (int c = 0; c < ctx.Board.Count; c++)
                    if (ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && ctx.Board[c].Biome == biome && ctx.Board[c].Climate == climate) candidates++;
                Assert.Greater(candidates, 0, $"{key}: 서식지가 맞는 야생 칸이 없다");
            }
            Assert.IsFalse(UltraBurstSetup.Habitat("800:1", out _, out _), "네크로즈마는 서식지 표에 없다");
        }

        [Test]
        public void S17_7_왜곡게이트는비용없이이어진다()
        {
            // p.26 「Each Distortion Gate links to its exact counterpart and does not cost a tile move to use」
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 5, log);
            var st = s.Seats[s.ActiveSeat];
            string[] names = { "westbreeze", "snowdrift", "deepmarsh", "lilly" };
            foreach (string name in names)
            {
                int main = UbScenes.Fixture(ctx, "gate." + name + "_main"), dist = UbScenes.Fixture(ctx, "gate." + name + "_distortion");
                int mainCell = ctx.Fixtures.Fixtures[main].Cell, distCell = ctx.Fixtures.Fixtures[dist].Cell;
                Assert.IsFalse(ctx.Board[mainCell].DistortionWorld);
                Assert.IsTrue(ctx.Board[distCell].DistortionWorld);
                st.Cell = mainCell;
                st.MovesLeft = 0; // 이동력이 없어도 된다(쓰지 않는다)
                var legal = Flow.MapLegal(s, ctx);
                var gates = legal.FindAll(a => a.Kind == AnswerKind.Move && a.Arg >= 0);
                Assert.AreEqual(1, gates.Count, "건너편 게이트 하나뿐");
                Assert.AreEqual(distCell, gates[0].Target);
                Assert.AreEqual(dist, gates[0].Arg);
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, distCell, dist), log);
                Assert.AreEqual(distCell, st.Cell);
                Assert.AreEqual(0, st.MovesLeft, "이동력을 쓰지 않는다");
                var travel = Flow.Of<GateTravel>(log);
                Assert.AreEqual(1, travel.Count);
                Assert.AreEqual(main, travel[0].FromGate);
                Assert.AreEqual(dist, travel[0].ToGate);
                Assert.AreEqual(mainCell, travel[0].FromCell);
                Assert.AreEqual(distCell, travel[0].ToCell);
                Assert.AreEqual(0, Flow.Of<Moved>(log).Count);
                Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴은 이어진다");
                // 돌아오기도 같은 답(턴당 횟수 제한 없음 [가정 A50 ④])
                st.MovesLeft = 2;
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, mainCell, main), log);
                Assert.AreEqual(mainCell, st.Cell);
                Assert.AreEqual(2, st.MovesLeft);
                // 다른 게이트로는 못 간다
                int other = UbScenes.Fixture(ctx, "gate." + (name == "lilly" ? "deepmarsh" : "lilly") + "_distortion");
                Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, Flow.Only(s, ChoiceKind.MapAction), A(AnswerKind.Move, ctx.Fixtures.Fixtures[other].Cell, other)));
            }
        }

        [Test]
        public void S17_7_신오의심장에서는왜곡게이트로건너가지못한다()
        {
            var s = Flow.Started(ctx, 2, 5);
            var st = s.Seats[s.ActiveSeat];
            int main = UbScenes.Fixture(ctx, "gate.deepmarsh_main"), dist = UbScenes.Fixture(ctx, "gate.deepmarsh_distortion");
            st.Cell = ctx.Fixtures.Fixtures[main].Cell;
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Move && a.Arg >= 0));
            Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, Flow.Only(s, ChoiceKind.MapAction), A(AnswerKind.Move, ctx.Fixtures.Fixtures[dist].Cell, dist)));
        }

        [Test]
        public void S17_7_메가로폴리스는회복과은행상회_공중날기는오지도가지도못한다()
        {
            // p.26 「fully restore your party and trade with the Ginkgo Merchants once per turn like villages do, however you cannot fly to or from there」
            var s = UbScenes.Started(ctx, 2, 5);
            var st = s.Seats[s.ActiveSeat];
            int mega = UbScenes.Fixture(ctx, "village.ultra_megalopolis");
            st.Cell = ctx.Fixtures.Fixtures[mega].Cell;
            for (int c = 0; c < SeatState.ApricornColors; c++) st.Apricorns[c] = 9; // 은행상회 맨 위 카드가 무엇이든 낼 수 있게
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(Flow.Has(legal, AnswerKind.VillageHeal), "회복");
            Assert.IsTrue(Flow.Has(legal, AnswerKind.Ginkgo), "은행상회");
            Assert.IsFalse(Flow.Has(legal, AnswerKind.Fly), "메가로폴리스에서 날아가지 못한다(from)");
            Assert.IsTrue(MapActions.VillagePlace(s, ctx, st.Cell, out var source, out _));
            Assert.AreEqual(RuleSource.MegalopolisHeal, source);
            st.Cell = ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "village.eterna")].Cell;
            legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(Flow.Has(legal, AnswerKind.Fly), "다른 마을에서는 난다");
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Fly && a.Target == mega), "메가로폴리스로 날아가지 못한다(to)");
        }

        [Test]
        public void M6_UB판은라운드끝마다UB턴이열린다()
        {
            for (int seats = 1; seats <= 3; seats++)
            {
                var log = new EventLog();
                var s = UbScenes.Started(ctx, seats, 11, log);
                Assert.AreEqual(0, Flow.Of<UbTurnStarted>(log).Count, "첫 라운드 앞에는 없다");
                log.Clear();
                UbScenes.EndRound(s, ctx, log);
                UbScenes.EndRound(s, ctx, log);
                var turns = Flow.Of<UbTurnStarted>(log);
                Assert.AreEqual(2, turns.Count, $"{seats}인: 라운드마다 한 번");
                Assert.AreEqual(1, turns[0].Round);
                Assert.AreEqual(2, turns[1].Round);
                Assert.AreEqual(4, turns[0].TotalMoves, "균열 0%면 4칸(R4)");
                Assert.AreEqual(3, s.Round);
                // UB 턴은 라운드의 마지막 턴 끝과 새 라운드 사이의 자동 행동이다
                int ub = log.Events.FindIndex(e => e is UbTurnStarted);
                int round = log.Events.FindIndex(e => e is RoundStarted);
                Assert.Less(ub, round);
                var act = log.Events.Find(e => e is ActionStarted a && a.Kind == ActionKind.UbTurn) as ActionStarted;
                Assert.IsNotNull(act);
                Assert.AreEqual(AutoKind.Auto, act.Auto);
                Assert.AreEqual(SeatId.System, act.Seat);
            }
            // 신오의 심장에는 UB 턴이 없다
            var hlog = new EventLog();
            var hos = Flow.Started(ctx, 2, 11, hlog);
            Flow.Answer(hos, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), hlog);
            Flow.Answer(hos, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), hlog);
            Assert.AreEqual(0, Flow.Of<UbTurnStarted>(hlog).Count);
        }

        [Test, Category("Slow")]
        public void M6_UB판은막없이턴이돈다()
        {
            // 봇 5라운드 × 1~6인 × 시드 3: 예외 0(명령마다 받아들임 · 카드 총수 · Default ∈ Legal · 이벤트 분류 — Playout.Run)
            int games = 0, commands = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var g = Playout.Run(ctx, seats, seed, null, null, Campaign.UltraBurst, 5);
                    Assert.GreaterOrEqual(g.Rounds, 5, $"{seats}인 시드 {seed}");
                    games++;
                    commands += g.Commands;
                }
            TestContext.WriteLine($"UB 봇 판 {games}판 × 5라운드: 명령 {commands}");
        }
    }
}
