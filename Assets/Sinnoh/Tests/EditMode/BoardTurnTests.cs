using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-3 「보드 화면」(04 §3-4 S19·S16, §8-8 10·11·101행): 화면 좌표로 칸 고르기의 순수 부분(광선 → 칸), 걷기 시간표, 부품 없는 행 덜어 내기,
    /// 그리고 보드 화면이 쓰는 길(좌석 손님의 합법 이동 → 답 → 화면 대기열)로 이동 한 번 · 턴 종료 · 다음 라운드까지.
    /// </summary>
    public class BoardTurnTests
    {
        Ctx ctx;
        Board board;
        IReadOnlyList<CellLook> looks;
        const double Radius = 1.1;

        // 화면(BoardPick.Prisms)과 같은 식: 칸 프리즘 = 층 띠의 맨 아래 ~ 맨 위(BoardLayout.Bands)
        HexPrism[] Prisms(bool distortionWorld)
        {
            var prisms = new HexPrism[board.Count];
            for (int c = 0; c < prisms.Length; c++)
            {
                var bands = BoardLayout.Bands(board[c], looks[c]);
                prisms[c] = new HexPrism
                {
                    X = (double)looks[c].X, Z = (double)looks[c].Z, Bottom = (double)bands[0].Bottom, Top = (double)bands[bands.Length - 1].Top,
                    Hidden = board[c].DistortionWorld && !distortionWorld,
                };
            }
            return prisms;
        }

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            board = BoardLoader.Load(TestData.ReadText("board.json"), out var l);
            looks = l;
        }

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        // ── 광선 → 칸 ──

        [Test]
        public void M4_칸가운데로내린광선은그칸을고른다_351칸()
        {
            Assert.AreEqual(Radius, (double)BoardLayout.HexRadius);
            var all = Prisms(true);
            var mainOnly = Prisms(false);
            int main = 0, distortion = 0;
            for (int c = 0; c < board.Count; c++)
            {
                double x = (double)looks[c].X, z = (double)looks[c].Z, top = (double)BoardLayout.TopY(board[c]);
                int hit = HexPick.Cell(all, Radius, x, 50, z, 0, -1, 0, out double t);
                Assert.AreEqual(c, hit, $"칸 {c} {board[c].Coord}");
                Assert.AreEqual(50 - top, t, 1e-9, "윗면에서 멈춘다");
                // 가장자리 안쪽(안접 반지름의 95%) 여섯 곳도 같은 칸
                for (int k = 0; k < 6; k++)
                {
                    double a = Math.PI / 6 + k * Math.PI / 3, r = 1.1 * Math.Sqrt(3) / 2 * 0.95;
                    Assert.AreEqual(c, HexPick.Cell(all, Radius, x + r * Math.Cos(a), 50, z + r * Math.Sin(a), 0, -1, 0, out _), $"칸 {c} 가장자리 {k}");
                }
                if (board[c].DistortionWorld)
                {
                    distortion++;
                    Assert.AreNotEqual(c, HexPick.Cell(mainOnly, Radius, x, 50, z, 0, -1, 0, out _), "깨어진 세계는 보일 때만 고른다");
                }
                else main++;
            }
            Assert.AreEqual(326, main);
            Assert.AreEqual(25, distortion);
        }

        [Test]
        public void M4_비스듬한광선은앞을가린높은칸의옆면을고르고판밖은고르지않는다()
        {
            // 이웃보다 두 층 넘게 높은 칸을 찾아, 낮은 이웃 쪽에서 낮게 쏜다: 낮은 칸 너머의 윗면이 아니라 높은 칸의 옆면에 닿는다
            var mainOnly = Prisms(false);
            int tested = 0;
            for (int c = 0; c < board.Count && tested < 20; c++)
            {
                if (board[c].DistortionWorld) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = board.Neighbor(c, d);
                    if (n == Board.NoCell || board[n].DistortionWorld) continue;
                    double high = (double)BoardLayout.TopY(board[c]), low = (double)BoardLayout.TopY(board[n]);
                    if (high - low < 1.5) continue;
                    double cx = (double)looks[c].X, cz = (double)looks[c].Z, nx = (double)looks[n].X, nz = (double)looks[n].Z;
                    // 낮은 칸 가운데 바로 위(윗면 + 0.3)에서 높은 칸 가운데 쪽으로 수평에 가깝게
                    double oy = low + 0.3;
                    int hit = HexPick.Cell(mainOnly, Radius, nx, oy, nz, cx - nx, -0.01, cz - nz, out double t);
                    Assert.AreEqual(c, hit, $"낮은 칸 {n} → 높은 칸 {c}");
                    Assert.That(t, Is.InRange(0.3, 0.6), "두 칸 사이 변(가운데 사이 거리의 절반)에서 닿는다");
                    tested++;
                    break;
                }
            }
            Assert.That(tested, Is.GreaterThan(0), "높이 차 큰 이웃이 있다");
            Assert.AreEqual(HexPick.NoCell, HexPick.Cell(mainOnly, Radius, 500, 50, 500, 0, -1, 0, out double none), "판 밖");
            Assert.IsTrue(double.IsPositiveInfinity(none));
            Assert.AreEqual(HexPick.NoCell, HexPick.Cell(mainOnly, Radius, (double)looks[0].X, 50, (double)looks[0].Z, 0, 1, 0, out _), "위로 가는 광선");
        }

        // ── 걷기 시간표 ──

        static Moved Walk(params int[] levels)
        {
            var path = new int[levels.Length];
            for (int i = 0; i < path.Length; i++) path[i] = 10 + i;
            return new Moved { Seat = 0, Path = path, Levels = levels, AllowanceBefore = 3, AllowanceAfter = 3 - (levels.Length - 1), StopAt = -1 };
        }

        [Test]
        public void M4_주인공도트말은가는쪽을보고한칸에걸음그림하나와선모습하나다_결정52()
        {
            // 보는 쪽(카메라 기준 4방향, 04 §3-1): 더 큰 성분을 따르고 같으면 좌우, 안 움직이면 앞모습
            Assert.AreEqual(Facing.East, FieldWalk.Face(1f, 0f));
            Assert.AreEqual(Facing.West, FieldWalk.Face(-1f, 0.2f));
            Assert.AreEqual(Facing.North, FieldWalk.Face(0.5f, 0.87f), "헥스의 비스듬한 걸음은 안쪽·앞쪽 성분이 더 크면 뒷모습·앞모습");
            Assert.AreEqual(Facing.South, FieldWalk.Face(-0.5f, -0.87f));
            Assert.AreEqual(Facing.East, FieldWalk.Face(0.87f, 0.5f));
            Assert.AreEqual(Facing.West, FieldWalk.Face(-0.7f, 0.7f), "같으면 좌우");
            Assert.AreEqual(Facing.South, FieldWalk.Face(0f, 0f));

            // 걷기 그림(한 바퀴 = 선 모습 0 · 걸음 A 1 · 선 모습 2 · 걸음 B 3): 예비는 선 모습, 걸음의 앞 절반은 발을 내민 그림(칸마다 A·B 번갈아), 뒤 절반은 선 모습, 착지는 선 모습
            var m = Walk(0, 0, 4, 4);   // 3걸음(270 · 350 · 270)
            int total = PlayTiming.RowMs(m, 110);
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, 0)), "예비");
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, 149)));
            Assert.AreEqual(1, FieldWalk.Cycle(FxWalk.At(m, 150 + 60)), "첫 걸음 앞 절반 = 걸음 A");
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, 150 + 135)), "첫 걸음 뒤 절반 = 선 모습");
            Assert.AreEqual(3, FieldWalk.Cycle(FxWalk.At(m, 150 + 270 + 100)), "둘째 걸음 = 걸음 B");
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, 150 + 270 + 200)));
            Assert.AreEqual(1, FieldWalk.Cycle(FxWalk.At(m, 150 + 270 + 350 + 100)), "셋째 걸음 = 다시 걸음 A");
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, total - 100)), "착지");
            Assert.AreEqual(0, FieldWalk.Cycle(FxWalk.At(m, total + 5000)));
            for (int ms = 0; ms <= total; ms += 7) Assert.That(FieldWalk.Cycle(FxWalk.At(m, ms)), Is.InRange(0, FieldWalk.CycleLength - 1));

            // 가는 두 칸: 예비는 첫 걸음, 걷는 중은 지금 걸음, 착지 뒤는 마지막 걸음(끝난 뒤에도 가던 쪽을 본다)
            Assert.IsTrue(FieldWalk.Leg(FxWalk.At(m, 0), out int a, out int b));
            Assert.AreEqual((0, 1), (a, b));
            Assert.IsTrue(FieldWalk.Leg(FxWalk.At(m, 150 + 270 + 10), out a, out b));
            Assert.AreEqual((1, 2), (a, b));
            Assert.IsTrue(FieldWalk.Leg(FxWalk.At(m, total), out a, out b));
            Assert.AreEqual((2, 3), (a, b));
            Assert.IsFalse(FieldWalk.Leg(FxWalk.At(Walk(0), 100), out _, out _), "걸음이 없는 길");
        }

        [Test]
        public void M4_솔로설정은주인공을싣고기본은Pt남자주인공이다_결정60()
        {
            var set = new SoloSettings();
            Assert.AreEqual("pt_player_male", set.Protagonist);
            Assert.AreEqual(Protagonists.Default, set.Protagonist);
            Assert.AreEqual(8, Protagonists.Keys.Length);
            CollectionAssert.AllItemsAreUnique(Protagonists.Keys);
            Assert.IsTrue(Protagonists.Has(Protagonists.Default));
            Assert.IsFalse(Protagonists.Has("pt_rival"));
            Assert.IsFalse(Protagonists.Has(null));
            set.Protagonist = "b2w2_001_rosa";
            Assert.AreEqual("b2w2_001_rosa", set.Clone().Protagonist, "사본에도 실린다");
            // 겉모양뿐이다: 규칙 옵션은 주인공과 무관하다
            Assert.AreEqual(new SoloSettings().ToOptions().TrainerCards, set.ToOptions().TrainerCards);
        }

        [Test]
        public void M4_걷기시간표는예비칸마다오르막착지이고합이방장길이와같다_결정53()
        {
            var m = Walk(0, 0, 4, 4);   // 3걸음, 둘째가 오르막
            int total = PlayTiming.RowMs(m, 110);
            Assert.AreEqual(150 + 270 * 3 + 80 + 200, total);

            var p = FxWalk.At(m, 0);
            Assert.AreEqual(0, p.From);
            Assert.AreEqual(0f, p.T);
            Assert.AreEqual(0f, p.Hop, "예비 동안은 제자리");
            p = FxWalk.At(m, 149);
            Assert.AreEqual(0f, p.T);

            p = FxWalk.At(m, 150 + 135);   // 첫 걸음 한가운데
            Assert.AreEqual(0, p.From);
            Assert.AreEqual(1, p.To);
            Assert.AreEqual(0.5f, p.T, 1e-4f);
            Assert.AreEqual(1f, p.Hop, 1e-4f);
            Assert.AreEqual(0, p.Landed);

            p = FxWalk.At(m, 150 + 270 + 175);   // 오르막 걸음(350) 한가운데 — 더 높이 튄다
            Assert.AreEqual(1, p.From);
            Assert.AreEqual(2, p.To);
            Assert.AreEqual(0.5f, p.T, 1e-4f);
            Assert.AreEqual(FxWalk.UpHop, p.Hop, 1e-4f);
            Assert.AreEqual(1, p.Landed);

            p = FxWalk.At(m, 150 + 270 + 350 + 269);
            Assert.AreEqual(2, p.From);
            Assert.AreEqual(3, p.To);
            Assert.AreEqual(2, p.Landed);

            foreach (int ms in new[] { total - 200, total - 1, total, total + 5000 })
            {
                p = FxWalk.At(m, ms);
                Assert.AreEqual(3, p.From, "착지: 마지막 칸 " + ms);
                Assert.AreEqual(3, p.To);
                Assert.AreEqual(0f, p.Hop);
                Assert.AreEqual(3, p.Landed);
            }
            // 같은 ms면 같은 모습(앞 호출에 기대지 않는다)
            var a = FxWalk.At(m, 400);
            FxWalk.At(m, 900);
            var b = FxWalk.At(m, 400);
            Assert.AreEqual(a.T, b.T);
            Assert.AreEqual(a.From, b.From);

            // 조정 값을 바꾸면 시간표가 함께 바뀐다(방장과 같은 값을 읽는다)
            Tunables.Shared.Set("walk.stepMs", 500);
            Assert.AreEqual(0.5f, FxWalk.At(m, 150 + 250).T, 1e-4f);
            Assert.AreEqual(0, FxWalk.At(Walk(0), 100).To, "걸음 없는 이동");
        }

        // ── 부품 없는 행 덜어 내기 ──

        [Test]
        public void M4_부품없는행을덜면단계와단서가당겨지고남은것이없으면길이0()
        {
            var entry = new FxEntry { Seq = 5, QueueMs = 4000, BlockMs = 3600 };
            entry.Steps.Add(new FxStep { Row = 40, Layer = FxLayer.Block, StartMs = 0, Ms = 3600 });
            entry.Steps.Add(new FxStep { Row = 60, Layer = FxLayer.Item, StartMs = 3600, Ms = 1400 });
            entry.Steps.Add(new FxStep { Row = 110, Layer = FxLayer.Item, StartMs = 5000, Ms = 620 });
            entry.Steps.Add(new FxStep { Row = 80, Layer = FxLayer.Item, StartMs = 5620, Ms = 1980 });
            entry.AddShown(4000, ShownKey.Hp(1), 5, 4);     // 60행 중간
            entry.AddShown(5620, ShownKey.Hp(2), 9, 3);     // 걸음 끝
            entry.AddShown(7600, ShownKey.Hp(3), 2, 8);     // 80행 끝
            FxBuilt.Keep(entry, row => row == 110);
            Assert.AreEqual(1, entry.Steps.Count);
            Assert.AreEqual(110, entry.Steps[0].Row);
            Assert.AreEqual(0, entry.Steps[0].StartMs, "앞 단계가 빠진 만큼 당긴다");
            Assert.AreEqual(0, entry.BlockMs);
            Assert.AreEqual(PlayTiming.PlainMs, entry.QueueMs, "남은 단계(620)가 있으면 보기 시간까지는 지킨다");
            Assert.AreEqual(0, entry.Cues[0].AtMs, "빠진 단계 안의 단서는 그 자리(남은 시간축의 같은 곳)로");
            Assert.AreEqual(620, entry.Cues[1].AtMs);
            Assert.AreEqual(620, entry.Cues[2].AtMs);

            // 대기열에 넣어 돌리면 길이만큼 걸리고 단서가 모두 닿는다
            var seq = new FxSequencer();
            seq.Enqueue(new[] { entry });
            Assert.AreEqual(5, seq.Shown.Get(ShownKey.Hp(1), 99), "넣는 순간 옛 값으로 붙잡는다");
            seq.Advance(0);
            Assert.AreEqual(4, seq.Shown.Get(ShownKey.Hp(1), 99));
            seq.Advance(620);
            Assert.AreEqual(3, seq.Shown.Get(ShownKey.Hp(2), 99));
            seq.Advance(PlayTiming.PlainMs - 620);
            Assert.IsTrue(seq.IsEmpty);

            // 남은 단계가 없으면 길이 0(그 자리에서 지나간다), 뺀 것이 없으면 그대로
            var gone = new FxEntry { Seq = 6, QueueMs = 3000, BlockMs = 3600 };
            gone.Steps.Add(new FxStep { Row = 40, Layer = FxLayer.Block, StartMs = 0, Ms = 3600 });
            gone.Steps.Add(new FxStep { Row = 50, Layer = FxLayer.Item, StartMs = 3600, Ms = 1800 });
            FxBuilt.Keep(gone, row => false);
            Assert.AreEqual(0, gone.LengthMs);
            var same = new FxEntry { Seq = 7, QueueMs = 4500 };
            same.Steps.Add(new FxStep { Row = 110, Layer = FxLayer.Item, StartMs = 0, Ms = 620 });
            FxBuilt.Keep(same, row => true);
            Assert.AreEqual(4500, same.QueueMs, "뺀 것이 없으면 방장과 같은 길이 그대로");
        }

        [Test]
        public void M4_대기열을비우면재생중인항목과붙잡은값을버리고바로입력이열린다()
        {
            var seq = new FxSequencer();
            var a = new FxEntry { Seq = 1, QueueMs = 1000 };
            a.AddShown(900, ShownKey.Hp(1), 7, 2);
            int finished = 0;
            seq.Finished += e => finished++;
            seq.Enqueue(new[] { a, new FxEntry { Seq = 2, QueueMs = 500 } });
            seq.Toast("알림");
            seq.Advance(300);
            Assert.IsFalse(seq.InputOpen);
            seq.Clear();
            Assert.IsTrue(seq.IsEmpty);
            Assert.IsTrue(seq.InputOpen, "게임을 떠나면 클릭 막기도 없다");
            Assert.IsTrue(seq.Idle);
            Assert.IsNull(seq.Last);
            Assert.AreEqual(0, seq.Shown.Count);
            Assert.AreEqual(0, finished, "버린 항목은 끝난 것으로 알리지 않는다");
            seq.Advance(5000);
            Assert.AreEqual(0, finished);
        }

        // ── 보드 화면이 쓰는 길로 턴 돌리기 ──

        sealed class Seat
        {
            public LocalSession Session;
            public SeatClient Client;
            public FxPlanner Planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            public List<FxEntry> Entries = new List<FxEntry>();
            public List<GameEvent> Events = new List<GameEvent>();
        }

        Seat StartOnBoard(ulong seed)
        {
            var s = new Seat();
            s.Session = SoloGame.Start(ctx, new SoloSettings(), seed, 99, new PrincipleAgent(), 0);
            s.Client = new SeatClient(s.Session.Link, ctx, SoloGame.Seat);
            s.Client.Updated += u =>
            {
                s.Events.AddRange(u.Events);
                s.Entries.AddRange(s.Planner.Plan(u.Events));
            };
            var c = s.Client;
            Assert.IsTrue(c.Answer(c.Choice(ChoiceKind.VillagePick), c.Legal(c.Choice(ChoiceKind.VillagePick))[0], 0).Accepted);
            Assert.IsTrue(c.Answer(c.Choice(ChoiceKind.StarterPick), c.Legal(c.Choice(ChoiceKind.StarterPick))[0], 0).Accepted);
            Assert.IsTrue(c.Answer(c.Choice(ChoiceKind.TrainerCardPick), c.Legal(c.Choice(ChoiceKind.TrainerCardPick))[0], 0).Accepted);
            Assert.AreEqual(Phase.Turn, c.View.Phase);
            return s;
        }

        [Test]
        public void M4_솔로첫턴은내차례컷신만이고이동한번턴종료뒤다음라운드내턴이열린다()
        {
            var s = StartOnBoard(11);
            var c = s.Client;
            var view = c.View;
            Assert.AreEqual(1, view.Round);
            Assert.AreEqual(0, view.ActiveSeat);
            Assert.AreEqual(MoveRules.BaseMovement, view.Seats[0].MovesLeft, "서막: 이동력 3");
            // 게임 첫 턴: 넘김 띠 없이 내 차례 컷신만(04 §8-8 101행)
            var first = s.Entries.Find(e => e.Step(FxRows.MyTurn) != null);
            Assert.IsNotNull(first, "첫 턴의 내 차례 컷신");
            Assert.IsNull(first.Step(FxRows.Handoff));
            Assert.IsNull(first.Step(FxRows.TurnSummary));

            // 합법 이동 = 이동력 안에 닿는 칸(엔진이 준 답 그대로 — 화면은 다시 세지 않는다)
            var act = c.Choice(ChoiceKind.MapAction);
            var legal = c.Legal(act);
            var moves = legal.FindAll(a => a.Kind == AnswerKind.Move);
            Assert.That(moves.Count, Is.GreaterThan(0));
            Assert.IsTrue(legal.Exists(a => a.Kind == AnswerKind.EndTurn), "턴 종료는 늘 합법");
            var reach = MoveRules.Reachable(ctx.Board, view.Seats[0].Cell, view.Seats[0].MovesLeft, view.Options.ElevationMode);
            foreach (var m in moves) Assert.That(reach[m.Target], Is.InRange(1, 3));

            // 두 걸음 떨어진 칸으로 걷는다
            var two = moves.Find(m => reach[m.Target] == 2);
            Assert.IsNotNull(two);
            int from = view.Seats[0].Cell;
            s.Entries.Clear();
            s.Events.Clear();
            Assert.IsTrue(c.Answer(act, two, 100).Accepted);
            var moved = (Moved)s.Events.Find(e => e is Moved);
            Assert.IsNotNull(moved);
            CollectionAssert.AreEqual(MoveRules.Path(ctx.Board, from, two.Target, 3, view.Options.ElevationMode), moved.Path, "경로는 엔진이 정한 최단 경로");
            Assert.AreEqual(two.Target, c.View.Seats[0].Cell);
            Assert.AreEqual(1, c.View.Seats[0].MovesLeft);
            var walk = s.Entries.Find(e => e.Step(110) != null);
            Assert.IsNotNull(walk, "걷기 항목");
            Assert.AreEqual(PlayTiming.RowMs(moved, 110), walk.Step(110).Ms);
            Assert.AreSame(moved, walk.Step(110).Events[0]);
            Assert.AreEqual(walk.Step(110).Ms, FxWalkLength(moved));

            // 걸은 뒤에도 턴은 이어진다. 숨겨진 장소에 멈췄으면 그 선택이, 아니면 맵 행동이 다시 열린다
            var next = c.FirstChoice();
            Assert.IsNotNull(next);
            if (next.Kind == ChoiceKind.HiddenSpot)
            {
                var answers = c.Legal(next);
                Assert.AreEqual(2, answers.Count, "공개 / 지나가기");
                Assert.IsTrue(c.Answer(next, answers.Find(a => a.Kind == AnswerKind.Decline), 200).Accepted);
                next = c.FirstChoice();
            }
            Assert.AreEqual(ChoiceKind.MapAction, next.Kind);

            // 턴 종료 → 솔로는 바로 다음 라운드의 내 턴: 넘김 띠 500 → 내 차례 컷신 500(결정 57 ③ — 주운 것이 없는 턴이라 요약은 건너뛴다)
            s.Entries.Clear();
            s.Events.Clear();
            Assert.IsTrue(c.Answer(next, ChoiceAnswer.Of(AnswerKind.EndTurn), 300).Accepted);
            Assert.IsTrue(s.Events.Exists(e => e is TurnEnded));
            var round = (RoundStarted)s.Events.Find(e => e is RoundStarted);
            Assert.AreEqual(2, round.Round);
            Assert.AreEqual(2, c.View.Round);
            Assert.AreEqual(0, c.View.ActiveSeat);
            Assert.AreEqual(MoveRules.BaseMovement, c.View.Seats[0].MovesLeft, "이동력이 다시 찬다");
            Assert.IsNotNull(c.Choice(ChoiceKind.MapAction), "다음 라운드의 맵 행동");
            var band = s.Entries.Find(e => e.Step(FxRows.Handoff) != null);
            Assert.IsNotNull(band);
            Assert.IsFalse(s.Events.Exists(PlayTiming.IsTurnGain), "이 턴에는 얻은 것이 없다");
            Assert.IsNull(band.Step(FxRows.TurnSummary), "「변화 없음」이면 요약 단계가 없다");
            Assert.AreEqual(500, band.Step(FxRows.Handoff).Ms);
            Assert.AreEqual(500, band.Step(FxRows.MyTurn).Ms);
            Assert.AreEqual(0, band.Step(FxRows.Handoff).StartMs);
            Assert.AreEqual(500, band.Step(FxRows.MyTurn).StartMs, "띠 뒤에 컷신");
            FxBuilt.Keep(band, row => row == 110 || row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn);
            Assert.AreEqual(1000, band.LengthMs, "솔로의 턴 넘김은 1초");
        }

        static int FxWalkLength(Moved m)
        {
            // 시간표를 1ms씩 훑어 마지막 칸에 닿아 튐이 끝나는 때 + 착지 = 방장 길이
            int total = PlayTiming.RowMs(m, 110), last = m.Path.Length - 1;
            int landed = 0;
            while (landed < total && FxWalk.At(m, landed).Landed < last) landed++;
            return landed + PlayTiming.WalkLandMs.Value;
        }
    }
}
