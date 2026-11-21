using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 남은 M4 연출 행(결정 70 ② A2 — 04 §8-8 6·9·13·19·99·102행): 좌석 여럿의 실제 판(봇 판 — 규칙 엔진이 낸 이벤트)에서
    /// 그 행의 단계가 화면 대기열에 서는지, 박자 함수가 길이 안에서 도는지.
    /// </summary>
    public class RowsLeftTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        /// <summary>봇 판(좌석 수 · 시드)을 좌석 0의 화면처럼 짠다: 갱신마다 (걸러진 이벤트, 화면 항목 — 그리는 단계만).</summary>
        internal static List<(IReadOnlyList<GameEvent> Events, List<FxEntry> Entries)> BotGame(Ctx ctx, int seats, ulong seed, int commands)
        {
            var agents = new IAgent[seats];
            for (int i = 0; i < seats; i++) agents[i] = new Playout.BotAgent(seed);
            var session = LocalSession.Start(ctx, new RuleOptions(), seats, seed, Campaign.HeartOfSinnoh,
                new SessionConfig { VirtualAgent = new PrincipleAgent(), SeatAgents = agents }, 0);
            var planner = new FxPlanner(0, seats);
            var got = new List<(IReadOnlyList<GameEvent>, List<FxEntry>)>();
            bool first = true;
            session.Link.Connect(0, m =>
            {
                if (!(m is GameUpdate u)) return;
                if (first)
                {
                    first = false;
                    planner.From(u.View);
                    return;
                }
                var entries = planner.Plan(u.Events);
                FxBuilt.KeepSteps(entries, s => FxBuilt.Draws(s, seats));
                got.Add((u.Events, entries));
            });
            while (session.CommandCount < commands && !session.GameOver)
            {
                long t = session.NextDueMs;
                if (t == long.MaxValue) break;
                session.Tick(t);
            }
            return got;
        }

        [Test]
        public void M4_남은행의박자는단계길이안에서돈다()
        {
            // 6행 턴 시작(1400): 배너는 들어와 머물다 5/8에서 나가고, 이동력은 단계 처음부터 100ms마다 하나, 단계 끝에는 다 찬다
            int total = PlayTiming.RowMs(new TurnStarted(), TurnStartBeats.Row);
            Assert.AreEqual(1400, total);
            Assert.AreEqual(0f, TurnStartBeats.Banner(-1, total).Shown);
            Assert.AreEqual(-1f, TurnStartBeats.Banner(0, total).Slide);
            Assert.AreEqual(0f, TurnStartBeats.Banner(total / 8, total).Slide, "들어온 뒤 제자리");
            Assert.AreEqual(1f, TurnStartBeats.Banner(700, total).Shown);
            Assert.AreEqual(0f, TurnStartBeats.Banner(total * 6 / 8, total).Shown, "나간 뒤");
            // 내 턴에는 배너도 카메라 이동도 없어 그리는 것이 이동력뿐이다 — 컷신이 끝나자마자(단계 처음) 차오른다(검토 반영: 60%에 시작하면 앞 840ms 동안 「남은 이동력 0」만 보였다)
            Assert.AreEqual(0, TurnStartBeats.Lit(-1, total, 3), "단계 전(넘김 띠 · 컷신 동안)");
            Assert.AreEqual(1, TurnStartBeats.Lit(0, total, 3));
            Assert.AreEqual(2, TurnStartBeats.Lit(100, total, 3));
            Assert.AreEqual(3, TurnStartBeats.Lit(200, total, 3));
            Assert.AreEqual(3, TurnStartBeats.Lit(839, total, 3));
            Assert.AreEqual(5, TurnStartBeats.Lit(total, total, 5));
            Assert.AreEqual(20, TurnStartBeats.Lit(total - 1, total, 20), "칸이 많아도 단계 끝 전에 다 켜진다");
            // 102행 라운드 띠(900 — 대기열 길이를 더하지 않는다): 왼쪽에서 들어와 머물다 오른쪽으로
            Assert.IsFalse(TurnStartBeats.Round(-1).Shown);
            Assert.AreEqual(-100f, TurnStartBeats.Round(0).X);
            Assert.AreEqual(0f, TurnStartBeats.Round(450).X);
            Assert.Greater(TurnStartBeats.Round(880).X, 50f);
            Assert.IsFalse(TurnStartBeats.Round(TurnStartBeats.RoundMs).Shown);
            Assert.Less(TurnStartBeats.RoundMs, total, "라운드 띠는 턴 시작 단계 안에서 끝난다");
            Assert.AreEqual(0, PlayTiming.RowOf(new RoundStarted()), "라운드 띠는 대기열 행이 아니다(방장 멈춤 0)");

            // 9행 보호 해제(500): 밝아짐 → 금 → 떨어짐(배지를 늘리지 않는다 — 검토 반영: 픽셀 글자가 비정수 배율로 번지고 칩 밖으로 나갔다)
            int shield = PlayTiming.RowMs(new ProtectionCleared(), ShieldBeats.Row);
            Assert.AreEqual(500, shield);
            Assert.IsFalse(ShieldBeats.At(0, shield).Lit);
            Assert.IsTrue(ShieldBeats.At(1, shield).Lit);
            Assert.IsTrue(ShieldBeats.At(249, shield).Lit);
            Assert.IsFalse(ShieldBeats.At(250, shield).Lit, "금이 가면 밝은 판이 꺼진다");
            Assert.IsFalse(ShieldBeats.At(249, shield).Cracked);
            Assert.IsTrue(ShieldBeats.At(250, shield).Cracked);
            Assert.AreEqual(0f, ShieldBeats.At(300, shield).Fall);
            Assert.AreEqual(1f, ShieldBeats.At(shield, shield).Fall);

            // 19행 규토리 줍기(2400): 3/8부터 150ms마다 하나씩 떨어져 튕기고, 3/4부터 모인다
            int take = PlayTiming.RowMs(new ApricornsTaken(), ApricornBeats.Row);
            Assert.AreEqual(2400, take);
            Assert.AreEqual(900, ApricornBeats.DropAt(take, 0));
            Assert.AreEqual(1200, ApricornBeats.DropAt(take, 2));
            Assert.AreEqual(-1f, ApricornBeats.Height(899, take, 0));
            Assert.AreEqual(1f, ApricornBeats.Height(900, take, 0));
            Assert.AreEqual(0f, ApricornBeats.Height(900 + ApricornBeats.FallMs, take, 0), 0.001f);
            Assert.Greater(ApricornBeats.Height(900 + ApricornBeats.FallMs + ApricornBeats.BounceMs / 2, take, 0), 0.2f, "닿은 뒤 한 번 튄다");
            Assert.Less(ApricornBeats.DropAt(take, 2) + ApricornBeats.FallMs + ApricornBeats.BounceMs, take * ApricornBeats.GatherFromEighths / 8, "셋째가 다 튄 뒤에 모인다");
            Assert.AreEqual(0f, ApricornBeats.At(1700, take).Label);
            Assert.AreEqual(1f, ApricornBeats.At(1900, take).Label);
            Assert.AreEqual(1f, ApricornBeats.At(take, take).Gather);

            // 99행 동시 공개(2000): 좌석마다 60ms 어긋나 뒤집힌다 — 여섯 좌석도 길이 안에서 끝난다
            int reveal = PlayTiming.RowMs(new SecretRevealed(), RevealAllBeats.Row);
            Assert.AreEqual(2000, reveal);
            Assert.AreEqual(1f, RevealAllBeats.Flip(-1, 0, out bool up));
            Assert.IsFalse(up, "단계가 오기 전에는 뒷면");
            Assert.AreEqual(0f, RevealAllBeats.Flip(RevealAllBeats.FlipAt(0) + RevealAllBeats.FlipMs / 2, 0, out up), 0.001f);
            Assert.IsTrue(up);
            RevealAllBeats.Flip(RevealAllBeats.FlipAt(0) + RevealAllBeats.FlipMs / 2, 1, out up);
            Assert.IsFalse(up, "다음 좌석은 60ms 뒤");
            Assert.Less(RevealAllBeats.FlipAt(SeatId.MaxSeats - 1) + RevealAllBeats.FlipMs, reveal);

            // 한 좌석 판은 6·99행을 그리지 않는다(결정 70 ① A1 나 · 결정 57) — 여럿 판과 좌석 수를 모를 때는 그린다
            foreach (int row in new[] { TurnStartBeats.Row, RevealAllBeats.Row })
            {
                Assert.IsTrue(FxBuilt.MultiOnly(row));
                Assert.IsFalse(FxBuilt.DrawnFor(row, 1), row + "행 — 한 좌석 판");
                Assert.IsTrue(FxBuilt.DrawnFor(row, 2), row + "행 — 두 좌석 판");
                Assert.IsTrue(FxBuilt.DrawnFor(row, 0));
            }
            foreach (int row in new[] { ShieldBeats.Row, ApricornBeats.Row }) Assert.IsTrue(FxBuilt.DrawnFor(row, 1), row + "행은 좌석 수와 무관하다");
        }

        [Test]
        public void M4_한좌석판의턴넘김은그대로1초이고여럿판은턴시작이붙는다()
        {
            // 솔로(결정 57 유지): 턴 종료 → 넘김 띠 500 + 컷신 500, 턴 시작 단계(6행)는 덜어 낸다
            foreach (int seats in new[] { 1, 2 })
            {
                var session = LocalSession.Start(ctx, new RuleOptions(), seats, 5, Campaign.HeartOfSinnoh, new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0);
                var clients = new SeatClient[seats];
                for (int i = 0; i < seats; i++) clients[i] = new SeatClient(session.Link, ctx, i);
                var planner = new FxPlanner(0, seats);
                var got = new List<FxEntry>();
                clients[0].Updated += u =>
                {
                    var entries = planner.Plan(u.Events);
                    FxBuilt.KeepSteps(entries, s => FxBuilt.Draws(s, seats));
                    SoundCues.Add(entries, e => e.Seat == 0 || e.Seat < 0);
                    got.AddRange(entries);
                };
                for (int guard = 0; clients[0].View.Phase == Phase.Setup; guard++)
                {
                    Assert.Less(guard, 200);
                    foreach (var c in clients)
                    {
                        var ch = c.FirstChoice();
                        if (ch != null) c.Answer(ch, c.Legal(ch)[0], 0);
                    }
                }
                // 셋업의 동시 공개(99행): 여럿 판에만 선다
                Assert.AreEqual(seats > 1, got.Exists(e => RevealAllBeats.StepOf(e) != null), seats + "인 셋업의 99행");
                got.Clear();
                // 한 바퀴: 좌석마다 턴 종료
                for (int k = 0; k < seats; k++)
                {
                    var active = clients[clients[0].View.ActiveSeat];
                    var ch = active.Choice(ChoiceKind.MapAction);
                    Assert.IsNotNull(ch);
                    Assert.IsTrue(active.Answer(ch, ChoiceAnswer.Of(AnswerKind.EndTurn), 0).Accepted);
                }
                var starts = got.FindAll(e => e.Kind == ActionKind.TurnStart);
                Assert.AreEqual(seats, starts.Count);
                foreach (var e in starts)
                {
                    if (seats == 1)
                    {
                        Assert.IsNull(TurnStartBeats.StepOf(e), "솔로에는 턴 시작 단계가 없다");
                        Assert.AreEqual(PlayTiming.SoloHandoffMs.Value + PlayTiming.SoloMyTurnMs.Value, e.LengthMs, "솔로 턴 넘김 1초");
                    }
                    else
                    {
                        var step = TurnStartBeats.StepOf(e);
                        Assert.IsNotNull(step, "여럿 판의 턴 시작 단계");
                        Assert.AreEqual(1400, step.Ms);
                        Assert.AreEqual(e.Steps.Count - 1, e.Steps.IndexOf(step), "넘김 띠(· 내 턴 컷신) 뒤에 온다");
                        Assert.AreEqual(3, TurnStartBeats.Allowance(e, e.Seat), "받은 이동력(기본 3)");
                        // 소리: 남의 턴 배너만 톡, 내 턴은 101행 컷신의 소리뿐
                        bool tick = e.Cues.Exists(c => c.IsSound && c.Row == TurnStartBeats.Row);
                        Assert.AreEqual(e.Seat != 0, tick, "좌석 " + e.Seat + "의 턴 시작 소리");
                    }
                }
                foreach (var c in clients) c.Close();
            }
        }

        [Test]
        public void M4_규토리줍기는빈찾아보기뒤엔진이내고단계가선다()
        {
            // 시험용 상태(ApricornBeats.Stage — 조우 덱을 비우고 나무 칸에 세운다) → 엔진의 길: [찾아보기] → 조우 없음 → [규토리 줍기] → ApricornsTaken + 턴 끝
            var setup = LocalSession.Start(ctx, new RuleOptions(), 1, 5, Campaign.HeartOfSinnoh, new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0);
            var first = new SeatClient(setup.Link, ctx, 0);
            for (int guard = 0; first.View.Phase == Phase.Setup; guard++)
            {
                Assert.Less(guard, 50);
                var ch = first.FirstChoice();
                first.Answer(ch, first.Legal(ch)[0], 0);
            }
            var state = setup.HostCopy();
            first.Close();
            int cell = ApricornBeats.Stage(ctx, state, 0);
            Assert.GreaterOrEqual(cell, 0, "규토리 나무 칸");
            var session = LocalSession.FromState(ctx, state, new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0);
            var client = new SeatClient(session.Link, ctx, 0);
            var planner = new FxPlanner(0, 1);
            planner.From(client.View);
            var got = new List<FxEntry>();
            client.Updated += u =>
            {
                var entries = planner.Plan(u.Events);
                FxBuilt.KeepSteps(entries, s => FxBuilt.Draws(s, 1));
                SoundCues.Add(entries, e => true);
                got.AddRange(entries);
            };
            var map = client.Choice(ChoiceKind.MapAction);
            Assert.IsTrue(client.Legal(map).Exists(a => a.Kind == AnswerKind.Search));
            Assert.IsFalse(client.Legal(map).Exists(a => a.Kind == AnswerKind.TakeApricorns), "찾아보기 전에는 줍지 못한다");
            Assert.IsTrue(client.Answer(map, ChoiceAnswer.Of(AnswerKind.Search), 0).Accepted);
            map = client.Choice(ChoiceKind.MapAction);
            Assert.IsNotNull(map, "조우가 없으면 맵 행동으로 돌아온다");
            Assert.IsTrue(client.Legal(map).Exists(a => a.Kind == AnswerKind.TakeApricorns));
            got.Clear();
            Assert.IsTrue(client.Answer(map, ChoiceAnswer.Of(AnswerKind.TakeApricorns), 0).Accepted);
            var entry = got.Find(e => ApricornBeats.StepOf(e) != null);
            Assert.IsNotNull(entry, "규토리 줍기 단계(19행)");
            var step = ApricornBeats.StepOf(entry);
            Assert.AreEqual(2400, step.Ms);
            Assert.AreEqual(3, ApricornBeats.Count(step), "나무의 규토리 3개");
            Assert.AreEqual(3, ApricornBeats.Colors(step).Count);
            Assert.AreEqual(0, ApricornBeats.Seat(step, out int at));
            Assert.AreEqual(cell, at);
            Assert.AreEqual(3, entry.Cues.FindAll(c => c.IsSound && c.Row == ApricornBeats.Row && c.Sound == SoundKeys.Token).Count, "닿는 소리 셋");
            // 턴이 끝나 다음 턴(솔로 — 요약 800: 규토리를 얻었다 + 띠 500 + 컷신 500)이 이어진다
            Assert.IsTrue(got.Exists(e => e.Step(FxRows.TurnSummary) != null && e.Step(FxRows.Handoff) != null), "규토리를 주운 턴은 요약이 선다");
            client.Close();
        }

        [Test, Category("Slow")]
        public void M4_남은행은봇판에서실제로나온다()
        {
            // 행 → 처음 나온 판(좌석 수 · 시드 · 갱신 번째)과 나온 횟수
            var rows = new[] { TurnStartBeats.Row, ShieldBeats.Row, ApricornBeats.Row, RevealAllBeats.Row };
            var count = new Dictionary<int, int>();
            var where = new Dictionary<int, string>();
            int rounds = 0;
            foreach (int row in rows) count[row] = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < PlayoutTests.SessionSeeds; seed++)
                {
                    var game = BotGame(ctx, seats, seed, PlayoutTests.SessionCommands * 4); // 운명 창·버리기(M5)가 명령을 더 써서 트레이너전 뒤 보호 해제(9행)가 나오려면 네 배
                    for (int u = 0; u < game.Count; u++)
                    {
                        foreach (var e in game[u].Events) if (e is RoundStarted && seats > 1) rounds++;
                        foreach (var entry in game[u].Entries)
                            foreach (var step in entry.Steps)
                            {
                                if (!count.ContainsKey(step.Row)) continue;
                                count[step.Row]++;
                                if (!where.ContainsKey(step.Row)) where[step.Row] = $"{seats}인 시드 {seed} 갱신 {u}";
                                Assert.Greater(step.Ms, 0, step.Row + "행 길이");
                                if (seats == 1) Assert.IsFalse(FxBuilt.MultiOnly(step.Row), step.Row + "행은 한 좌석 판에서 그리지 않는다(결정 70 ① A1 나 · 결정 57)");
                            }
                    }
                }
            foreach (int row in rows)
                Report($"[남은 행] {row}행: {count[row]}번" + (where.ContainsKey(row) ? " — 처음 " + where[row] : ""));
            Report($"[남은 행] 라운드 시작(102행, 여럿 판): {rounds}번");
            Assert.Greater(count[TurnStartBeats.Row], 0, "6행 턴 시작(여럿 판)");
            Assert.Greater(count[RevealAllBeats.Row], 0, "99행 동시 공개(여럿 판 셋업)");
            Assert.Greater(count[ShieldBeats.Row], 0, "9행 도전 보호 해제(트레이너전 뒤)");
            Assert.Greater(rounds, 0, "102행 라운드 시작");
        }
    }
}
