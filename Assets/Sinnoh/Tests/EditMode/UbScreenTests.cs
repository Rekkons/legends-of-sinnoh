using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「UB 화면 묶음 1」(결정 84 M1 가 · M2 가)의 엔진 무관 셈(<c>UbStaging.cs</c>): UB 턴 시작 821 · 빛 모음 840 · 10% 구간 850 · 70% 패배 860의 방장·화면 행과 길이,
    /// UB 턴 띠의 이동 수 · 균열 칩의 굴러 오르는 값(<see cref="UbGauge"/>) · UB 턴 기록 줄이 단계마다 한 줄씩 붙는 것(<see cref="BoardLog.Shown"/>).
    /// </summary>
    public class UbScreenTests
    {
        const ulong UbSeed = 20261117;

        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        // 울트라 버스트 판(균열을 breach%까지 올려 둔 뒤)의 한 라운드를 [턴 종료]로 넘긴 갱신 → 좌석 0의 화면 항목(그리는 단계만)
        List<FxEntry> Round(int seats, ulong seed, int breach, out GameState s, out List<GameEvent> events)
        {
            s = UbScenes.Started(ctx, seats, seed);
            if (breach > 0) Assert.AreEqual(breach, UbScenes.Breach(s, breach));
            var planner = new FxPlanner(0, seats);
            planner.From(s);
            var log = new EventLog();
            UbScenes.EndRound(s, ctx, log);
            events = new List<GameEvent>();
            foreach (var e in log.Events)
            {
                var f = Engine.FilterFor(e, 0);
                if (f != null) events.Add(f);
            }
            var entries = planner.Plan(events);
            FxBuilt.KeepSteps(entries, st => FxBuilt.Draws(st, seats));
            return entries;
        }

        static FxEntry UbEntry(List<FxEntry> entries)
        {
            foreach (var e in entries)
                if (e.Kind == ActionKind.UbTurn) return e;
            Assert.Fail("UB 턴 항목이 없다");
            return null;
        }

        [Test]
        public void M6_UB화면행_방장과화면이같은행과길이와차단()
        {
            var deals = new BreachDeal[4];
            for (int i = 0; i < deals.Length; i++) deals[i] = new BreachDeal { Seat = i, CardKeys = new[] { 1 } };
            var events = new (GameEvent e, int row, int ms)[]
            {
                (new UbTurnStarted { Seat = SeatId.System, Round = 3, TotalMoves = 5, Breach = 12 }, UbRows.Start, 1600),
                (new NecrozmaStrike { Seat = SeatId.UltraBeast, Cells = new[] { 4, 5 }, Count = 2 }, UbRows.Strike, 1500),
                (new NecrozmaStrike { Seat = SeatId.UltraBeast, Cells = new int[0], Count = 2 }, UbRows.Strike, 0),
                (new BreachBracket { Seat = SeatId.System, Before = 9, After = 11, Brackets = 1 }, UbRows.Bracket, 1500),
                (new BreachCardsDealt { Seat = SeatId.System, PerSeat = deals }, UbRows.Bracket, 2100),
                (new GameEnded { Seat = SeatId.System, Reason = GameEndReason.BreachLost, Winners = new int[0] }, UbRows.Lost, 4500),
            };
            foreach (var (e, row, ms) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
                Assert.IsFalse(FxRows.Chains(row), row + "행은 항목에 단계 하나");
            }
            Assert.IsTrue(FxRows.BlocksAll(UbRows.Lost), "86행은 전원 차단 덮개");
            Assert.IsFalse(FxRows.BlocksAll(UbRows.Start) || FxRows.BlocksAll(UbRows.Strike) || FxRows.BlocksAll(UbRows.Bracket));
            // 다른 까닭의 게임 끝은 97행 그대로
            var hero = new GameEnded { Seat = SeatId.System, Reason = GameEndReason.Hero, Winners = new[] { 0 } };
            Assert.AreEqual(FinaleRows.Result, PlayTiming.RowOf(hero));
            Assert.AreEqual(FinaleRows.Result, FxRows.Of(hero));
        }

        [Test]
        public void M6_UB턴항목은시작821로열리고빛모음840뒤에네크로즈마의파괴가선다()
        {
            var ub = UbEntry(Round(4, UbSeed, 0, out _, out _));
            Assert.AreEqual(UbRows.Start, ub.Steps[0].Row, "UB 턴 항목의 첫 단계 = 시작(예비)");
            int strike = -1, firstNecrozma = -1, lastBeast = -1;
            for (int i = 0; i < ub.Steps.Count; i++)
            {
                var st = ub.Steps[i];
                if (st.Row == UbRows.Strike) strike = i;
                if (StrikeBeats.ByNecrozma(st) && firstNecrozma < 0) firstNecrozma = i;
                if (st.Row == UbRows.Move || st.Row == UbRows.Destroy && !StrikeBeats.ByNecrozma(st)) lastBeast = i;
            }
            Assert.GreaterOrEqual(strike, 0, "네크로즈마의 원격 파괴는 늘 있다 — 빛 모음 단계");
            Assert.Greater(firstNecrozma, strike, "빛을 모은 뒤 칸마다 내리꽂힌다");
            Assert.Greater(strike, lastBeast, "UB가 다 움직인 뒤 네크로즈마");
            // 빛줄기: 칸에 닿은 뒤 어두워지기 시작한다
            int total = ub.Steps[firstNecrozma].Ms;
            Assert.AreEqual(1f, StrikeBeats.Beam(total * StrikeBeats.BeamDownPermille / 1000, total).Reach, 1e-3);
            Assert.Less(StrikeBeats.BeamDownPermille, DestroyBeats.DarkFromPermille);
            Assert.AreEqual(0f, StrikeBeats.Beam(total * StrikeBeats.BeamGonePermille / 1000, total).Alpha);
            Assert.Greater(StrikeBeats.Glow(ub.Steps[strike].Ms / 2, ub.Steps[strike].Ms), 0f);
        }

        [Test]
        public void M6_UB턴띠는시작부터항목끝까지남고이동수는내려앉을때마다는다()
        {
            var ub = UbEntry(Round(4, UbSeed, 0, out _, out var events));
            var started = UbTurnBeats.Started(ub.Steps[0]);
            Assert.IsNotNull(started);
            var at0 = UbTurnBeats.At(ub, 0);
            Assert.IsTrue(at0.Phase, "102행 UB 페이즈 띠가 처음에 지나간다");
            Assert.AreEqual(0f, at0.Band, "위 가운데 띠는 페이즈 띠가 빠질 때 들어온다");
            Assert.AreEqual(started.TotalMoves, at0.Total);
            Assert.AreEqual(UbTurnBeats.PressScale, UbTurnBeats.At(ub, UbTurnBeats.PressAtMs).Press, 1e-4, "균열 칩이 눌린다");
            Assert.AreEqual(1f, UbTurnBeats.At(ub, UbTurnBeats.PressEndMs).Press, 1e-4);
            Assert.AreEqual(1f, UbTurnBeats.At(ub, UbTurnBeats.BandFromMs + UbTurnBeats.BandInMs).Band, 1e-4);
            Assert.IsFalse(UbTurnBeats.At(ub, TurnStartBeats.RoundMs).Phase);
            int landed = 0;
            foreach (var st in ub.Steps)
            {
                if (st.Row != UbRows.Move) continue;
                var m = UbRows.First<UbMoved>(st);
                var during = UbTurnBeats.At(ub, st.StartMs + 1);
                Assert.AreEqual(landed, during.Landed, "내려앉기 전");
                Assert.AreEqual(m.UbId, during.Moving, "움직이는 UB의 꼬리표");
                Assert.AreEqual(1f, during.Band, 1e-4, "UB 턴 동안 띠가 남는다");
                landed++;
                Assert.AreEqual(landed, UbTurnBeats.At(ub, st.EndMs).Landed, "내려앉으면 하나 는다");
            }
            Assert.Greater(landed, 0);
            Assert.LessOrEqual(landed, started.TotalMoves);
            Assert.AreEqual(0f, UbTurnBeats.At(ub, ub.LengthMs).Band, "항목이 끝나면 걷힌다");
            Assert.AreEqual(-1, UbTurnBeats.At(ub, ub.LengthMs).Moving);
            Assert.AreEqual(0f, UbTurnBeats.At(null, 0).Band, "UB 턴 항목이 아니면 없다");
        }

        [Test]
        public void M6_균열칩은파괴단계동안굴러오르고대기열이비면보기값이다()
        {
            var entries = Round(2, 5, 9, out var s, out _);
            var ub = UbEntry(entries);
            BreachChanged first = null;
            FxStep at = null;
            FxStep last = null;
            foreach (var e in ub.Events)
            {
                if (e is CellDestroyed)
                    foreach (var st in ub.Steps)
                        if (st.Events.Contains(e)) last = st;
                if (e is BreachChanged b && b.After != b.Before) { first = b; at = last; break; }
            }
            Assert.IsNotNull(first, "UB 턴에 균열이 바뀐다");
            Assert.IsNotNull(at, "균열 바뀜은 앞의 파괴 단계에 붙는다");
            int final = UbGauge.Breach(s.Tokens);
            Assert.AreEqual(UbTurn.Breach(s), final, "보기 값 = 엔진 셈");
            Assert.AreEqual(final, UbGauge.Shown(s.Tokens, null, null, 0), "대기열이 비면 보기 값");
            Assert.AreEqual(first.Before, UbGauge.Shown(s.Tokens, entries, null, 0), "아직 재생하지 않은 항목 — 바뀌기 전 값");
            Assert.AreEqual(first.Before, UbGauge.Shown(s.Tokens, entries, ub, at.StartMs - 1));
            Assert.AreEqual(first.After, UbGauge.Shown(s.Tokens, entries, ub, at.EndMs), "그 파괴 단계가 끝나면 바뀐 값(다음 바뀜의 처음 값)");
            int mid = UbGauge.Shown(s.Tokens, entries, ub, at.StartMs + at.Ms * DestroyBeats.DarkToPermille / 1000);
            Assert.AreEqual(first.After, mid, "어두워지기가 끝나면 다 굴러 올랐다");
            Assert.AreEqual(final, UbGauge.Shown(s.Tokens, entries, ub, ub.LengthMs), "항목 끝 = 보기 값");
            // 막대 칸 · 다음 10%까지 남은 수
            Assert.AreEqual(1f, UbGauge.Fill(23, 1));
            Assert.AreEqual(0.3f, UbGauge.Fill(23, 2), 1e-4);
            Assert.AreEqual(0f, UbGauge.Fill(23, 6));
            var fresh = UbScenes.Started(ctx, 2, 5);
            UbScenes.Breach(fresh, 9);
            Assert.AreEqual(1, UbGauge.NextNeed(fresh.Tokens), "9% → 10%는 하나 더(131개 중 13개)");
        }

        [Test]
        public void M6_균열10퍼센트를넘으면구간단계850이카드받기와함께선다()
        {
            var entries = Round(2, 5, 9, out var s, out var events);
            var ub = UbEntry(entries);
            var step = BracketBeats.StepOf(ub);
            Assert.IsNotNull(step, "9% → 10%를 넘었다 — 구간 단계");
            var dealt = BracketBeats.Dealt(step);
            Assert.IsNotNull(dealt, "카드 받기도 같은 단계");
            Assert.AreEqual(2, dealt.PerSeat.Length);
            Assert.AreEqual(System.Math.Min(2500, 1500 + 150 * 2), step.Ms);
            Assert.AreEqual(1f, BracketBeats.Pulse(BracketBeats.PulsePeakMs), 1e-4);
            Assert.AreEqual(0f, BracketBeats.Pulse(BracketBeats.PulseEndMs));
            for (int i = 0; i < SeatId.MaxSeats; i++)
                Assert.LessOrEqual(BracketBeats.CardFromMs + BracketBeats.CardEveryMs * i + BracketBeats.CardFlyMs, PlayTiming.RowMs(new BreachCardsDealt { PerSeat = new BreachDeal[i + 1] }, UbRows.Bracket), "좌석 " + (i + 1) + "명 — 카드가 단계 안에 닿는다");
            Assert.AreEqual(-1f, BracketBeats.Card(BracketBeats.CardFromMs - 1, 0));
            Assert.AreEqual(2f, BracketBeats.Card(BracketBeats.CardFromMs + BracketBeats.CardFlyMs, 0));
        }

        [Test]
        public void M6_균열70퍼센트패배는보드덮개860단계다()
        {
            var entries = Round(2, 5, 69, out var s, out var events);
            Assert.AreEqual(Phase.GameOver, s.Phase, "69% → 70%: 패배");
            Assert.AreEqual(UbResult.Lost, s.Ub.Result);
            FxStep lost = null;
            foreach (var e in entries)
                if (LostBeats.StepOf(e) is FxStep st) lost = st;
            Assert.IsNotNull(lost, "패배 덮개 단계");
            Assert.AreEqual(FxLayer.Block, lost.Layer, "전원 차단");
            Assert.AreEqual(4500, lost.Ms);
            Assert.AreEqual(0f, LostBeats.At(0).Wash);
            Assert.AreEqual(0f, LostBeats.At(LostBeats.TitleFromMs - 1).Title);
            var end = LostBeats.At(lost.Ms);
            Assert.AreEqual(1f, end.Wash);
            Assert.AreEqual(1f, end.Dark);
            Assert.AreEqual(1f, end.Title, "단계 안에 다 보인다");
            // 기록 줄은 「{name}의 승리」가 아니라 패배 줄
            var log = new BoardLog(ctx);
            log.Feed(events);
            Assert.IsTrue(log.Lines[log.Lines.Count - 1].Line.Key == "screen.log.line.ub.lost", "균열 패배 기록 줄: " + log.Lines[log.Lines.Count - 1].Line.Key);
        }

        [Test]
        public void M6_UB턴기록줄은그단계가시작될때한줄씩붙는다()
        {
            var entries = Round(4, UbSeed, 0, out _, out var events);
            var ub = UbEntry(entries);
            var log = new BoardLog(ctx);
            log.Feed(events);
            int all = 0;
            foreach (var line in log.Lines) if (line.Seq == ub.Seq && line.Kind == BoardLogKind.Line) all++;
            Assert.Greater(all, 2, "UB 턴 · 이동 · 파괴 줄");
            var seq = new FxSequencer();
            seq.Enqueue(new[] { ub });
            seq.Advance(1);
            Assert.AreSame(ub, seq.Current);
            int Shown()
            {
                var into = new List<BoardLogLine>();
                log.Shown(seq, into);
                int n = 0;
                foreach (var line in into) if (line.Seq == ub.Seq && line.Kind == BoardLogKind.Line) n++;   // 머리 줄(라운드 머리)은 세지 않는다
                return n;
            }
            Assert.AreEqual(1, Shown(), "UB 턴 시작 줄만");
            FxStep move = null;
            foreach (var st in ub.Steps) if (st.Row == UbRows.Move) { move = st; break; }
            // 첫 이동 단계의 줄: 같은 UB가 잇달아 옮겨 모은 줄이면 마지막 걸음의 단계가 시작될 때 보인다(첫 걸음에 끝 칸 수가 먼저 뜨지 않게 — M6 화면 교차 검토 #5)
            bool own = log.Lines.Any(l => l.Seq == ub.Seq && l.Line != null && move.Events.Contains(l.Line.Event));
            seq.Advance(move.StartMs + 1 - seq.LocalMs);
            Assert.AreEqual(own ? 2 : 1, Shown(), "첫 이동이 시작되면 그 줄(모은 줄이면 아직)");
            int before = Shown();
            seq.Advance(ub.LengthMs - 1 - seq.LocalMs);
            Assert.Greater(Shown(), before, "단계가 지날수록 붙는다");
            Assert.Less(Shown(), all, "단계 없는 줄(균열 n% → m%)은 항목 끝에");
            seq.Advance(2);
            Assert.AreEqual(all, Shown(), "항목이 끝나면 모두");
        }

        [Test]
        public void M6_잇단UB이동을모은줄은마지막걸음단계에서보인다()
        {
            // M6 화면 교차 검토 #5: 같은 UB의 잇단 이동을 한 줄(「n칸 이동했다」)로 모으면서 줄의 이벤트가 첫 걸음이라 첫 걸음 단계에 끝 칸 수가 먼저 떴다
            Round(4, UbSeed, 0, out _, out var events);
            int ubId = -1;
            foreach (var ev in events) if (ev is UbMoved um) { ubId = um.UbId; break; }
            Assert.GreaterOrEqual(ubId, 0, "시험 준비: UB 이동");
            const int seq = 9001;
            var first = new UbMoved { Seat = SeatId.UltraBeast, Seq = seq, UbId = ubId, From = 0, To = 1, StepIdx = 0 };
            var second = new UbMoved { Seat = SeatId.UltraBeast, Seq = seq, UbId = ubId, From = 1, To = 2, StepIdx = 1 };
            var log = new BoardLog(ctx);
            log.Feed(new List<GameEvent> { first, second });
            var merged = log.Lines.FirstOrDefault(l => l.Seq == seq && l.Line != null && l.Line.Key == "screen.log.line.ub.move");
            Assert.IsNotNull(merged);
            Assert.AreEqual("2", merged.Line.Args.Find(a => a.Name == "n").Value, "한 줄로 모은다");
            Assert.AreSame(second, merged.Line.Event, "줄의 이벤트 = 마지막 걸음");
            var a1 = new FxStep { Row = UbRows.Move, Layer = FxLayer.Item, Ms = 700, StartMs = 0 };
            a1.Events.Add(first);
            var a2 = new FxStep { Row = UbRows.Move, Layer = FxLayer.Item, Ms = 700, StartMs = 700 };
            a2.Events.Add(second);
            var entry = new FxEntry { Seq = seq, Seat = SeatId.UltraBeast, Kind = ActionKind.UbTurn, QueueMs = 1400 };
            entry.Steps.Add(a1);
            entry.Steps.Add(a2);
            entry.Events.Add(first);
            entry.Events.Add(second);
            var sq = new FxSequencer();
            sq.Enqueue(new[] { entry });
            sq.Advance(1);
            var into = new List<BoardLogLine>();
            log.Shown(sq, into);
            Assert.IsFalse(into.Contains(merged), "첫 걸음에는 아직");
            sq.Advance(701 - sq.LocalMs);
            into.Clear();
            log.Shown(sq, into);
            Assert.IsTrue(into.Contains(merged), "마지막 걸음의 단계가 시작되면");
        }
    }
}
