using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「스탬프·도전·거래 화면」의 연출 행(04 §8-8 47·51·52행 — <c>DealStaging.cs</c>, plan §5 #125·#126): 방장 <c>PlayTiming</c>과 화면 <c>FxRows</c>가 같은 행·길이를 고르고,
    /// 실제 장면의 대기열에 그 단계가 서고, 박자가 단계 길이 안에서 끝나고, 기록 줄 · 소리 단서가 선다.
    /// </summary>
    public class DealStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        // 좌석 둘 판의 첫 턴: 두 좌석을 같은 칸(마을 밖)에 세운다
        GameState SameTile(EventLog log)
        {
            var s = Flow.Started(ctx, 2, 32);
            int me = s.ActiveSeat, them = 1 - me;
            s.Seats[them].Cell = s.Seats[me].Cell;
            return s;
        }

        [Test]
        public void M5_거래행_방장과화면이같은행과길이()
        {
            var one = new StampGained { Seat = 0, StampKey = 0, CountBefore = 0, CountAfter = 1, Changes = new[] { new ResChange(), new ResChange(), new ResChange() }, MoveKeys = new int[0] };
            var cards = new StampGained { Seat = 0, StampKey = 0, CountBefore = 1, CountAfter = 2, Changes = new ResChange[0], MoveKeys = new[] { 1, 2, 3, 4, 5 } };
            var events = new (GameEvent e, int row, int ms)[]
            {
                (one, DealRows.Stamp, 2250),
                (cards, DealRows.Stamp, 1750),
                (new ChallengeIssued { Seat = 0, From = 0, To = 1 }, DealRows.Vs, 2800),
                (new TradeDone { Seat = 1, A = 0, B = 1 }, DealRows.Trade, 2500),
            };
            foreach (var (e, row, ms) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
            }
            Assert.AreEqual(2500, PlayTiming.StampMs(7, 5), "상한 2500(04 47행 1500~2500)");
            Assert.IsTrue(FxRows.BlocksParty(DealRows.Vs), "도전 VS는 당사자 차단(04 51행)");
            Assert.AreEqual(0, FxRows.Of(new TradeProposed()), "거래 제안은 기록 줄과 응답 창만");
            Assert.AreEqual(0, FxRows.Of(new TradeAnswered()));
        }

        [Test]
        public void M5_거래행_스탬프는하나씩이어지고박자가길이안에끝난다_47()
        {
            // 규토리 여섯 색을 쥐고 일곱째 색을 주우면… 대신 손으로: 첫 스탬프의 보상(빨강·파랑·노랑)이 일곱 색을 채워 둘째 스탬프(약한 5장)를 잇달아 부른다
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 32);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            for (int c = (int)ApricornColor.Pink; c < SeatState.ApricornColors; c++) st.Apricorns[c] = 1;
            var fx = new Emitter(s, log);
            fx.Begin(seat, ActionKind.ProposeTrade, AutoKind.Human);
            Assert.IsTrue(StampRules.Gain(s, ctx, fx, seat, StampRules.TrainerWin));
            var got = log.OfType<StampGained>();
            Assert.AreEqual(2, got.Count, "보상이 채운 일곱 색 → 스탬프 3");
            var entries = new FxPlanner(seat, 2).Plan(log.Events);
            var entry = entries.Find(e => e.Step(DealRows.Stamp) != null);
            var steps = entry.Steps.FindAll(x => x.Row == DealRows.Stamp);
            Assert.AreEqual(2, steps.Count, "스탬프마다 단계 하나");
            Assert.AreEqual(steps[0].EndMs, steps[1].StartMs);
            Assert.AreEqual(2250, steps[0].Ms, "1500 + 규토리 세 색 × 250");
            Assert.AreEqual(1750, steps[1].Ms, "1500 + 기술 카드 묶음 250");
            Assert.AreEqual(4000, PlayTiming.Items(log.Events, new PlayTimingContext(seat, 2)).Find(i => i.Seq == entry.Seq).StepsMs, "방장도 더한다");
            foreach (var step in steps)
            {
                var e = StampBeats.Event(step);
                int n = StampBeats.Rewards(e);
                Assert.LessOrEqual(StampBeats.RewardAt(n - 1) + StampBeats.RewardDropMs, step.Ms - StampBeats.OutMs, "마지막 보상 줄이 걷히기 전에 내려앉는다");
                Assert.IsFalse(StampBeats.At(StampBeats.SlamAt - 1, step.Ms).Stamped);
                Assert.IsTrue(StampBeats.At(StampBeats.SlamAt, step.Ms).Stamped);
                Assert.AreEqual(1.8f, StampBeats.At(0, step.Ms).Scale, "1.8배로 내려와");
                Assert.AreEqual(1f, StampBeats.At(StampBeats.SlamAt + StampBeats.SlamMs, step.Ms).Scale, "1배로 찍힌다");
                Assert.AreEqual(0f, StampBeats.At(step.Ms, step.Ms).Show, "단계 끝에는 걷혔다");
            }
            // 보상 수는 단계 끝에 착지한다(표시 값) · 소리: 쾅 + 보상 줄마다
            SoundCues.Add(entry, true);
            var sounds = entry.Cues.FindAll(c => c.IsSound && c.Row == DealRows.Stamp);
            Assert.AreEqual(1 + 3 + 1 + 1, sounds.Count);
            Assert.AreEqual((steps[0].StartMs + StampBeats.SlamAt, SoundKeys.Stamp), (sounds[0].AtMs, sounds[0].Sound));   // M9 소리 ③: 도장 PL_STAMP(옛 hit)
            var red = entry.Cues.Find(c => !c.IsSound && c.Key.Equals(ShownKey.Resource(seat, ResKind.Apricorn, (int)ApricornColor.Red)));
            Assert.AreEqual(steps[0].EndMs, red.AtMs, "규토리는 그 스탬프 단계 끝에");
            // 기록 줄
            var board = new BoardLog(ctx);
            board.Feed(log.Events);
            Assert.AreEqual(2, new List<BoardLogLine>(board.Lines).FindAll(l => l.Line.Key == BoardLog.K + "stamp").Count);
        }

        [Test]
        public void M5_거래행_도전은VS단계뒤에응답이나배틀_51()
        {
            var log = new EventLog();
            var s = SameTile(log);
            int me = s.ActiveSeat, them = 1 - me;
            var challenge = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Challenge && a.Target == them);
            Assert.IsNotNull(challenge, "같은 칸 도전");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, challenge, log);
            foreach (int viewer in new[] { me, them })
            {
                var entries = new FxPlanner(viewer, 2).Plan(log.Events);
                var entry = entries.Find(e => e.Step(DealRows.Vs) != null);
                Assert.IsNotNull(entry, "VS 단계");
                var vs = entry.Step(DealRows.Vs);
                Assert.AreEqual(FxLayer.Block, vs.Layer, "당사자는 끝까지 막는 컷신");
                Assert.AreEqual(2800, vs.Ms);
                Assert.AreEqual(me, VsBeats.Event(vs).From);
            }
            var pose = VsBeats.At(VsBeats.VsAt + VsBeats.VsMs, 2800);
            Assert.AreEqual(0f, pose.Slide, 0.001f, "얼굴이 제자리에 선 뒤");
            Assert.AreEqual(1f, pose.Vs, "VS가 박혔다");
            Assert.AreEqual(1f, VsBeats.At(0, 2800).Slide, "처음에는 화면 밖");
            Assert.AreEqual(0f, VsBeats.At(2800, 2800).Show);
            var board = new BoardLog(ctx);
            board.Feed(log.Events);
            Assert.IsTrue(new List<BoardLogLine>(board.Lines).Exists(l => l.Line.Key == BoardLog.K + "challenge" && l.Seat == me));
        }

        [Test]
        public void M5_거래행_거래는받아들인항목에주고받기단계_52()
        {
            var log = new EventLog();
            var s = SameTile(log);
            int me = s.ActiveSeat, them = 1 - me;
            s.Seats[me].Apricorns[(int)ApricornColor.Blue] = 2;
            s.Seats[them].Jp = 3;
            var offer = TradeRules.Offer(them, 0, (int)TradeKind.Apricorn, (int)ApricornColor.Blue, 2, 1, (int)TradeKind.Jp, -1, 3);
            Assert.IsTrue(TradeRules.CanOffer(s, ctx, me, offer), "내 턴 · 같은 칸");
            Assert.IsFalse(TradeRules.CanOffer(s, ctx, them, TradeRules.Offer(me, 0, (int)TradeKind.Jp, -1, 1)), "남의 턴 · 같은 신의 추종자가 아니다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, offer, log);
            var respond = s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade);
            Scene.Accept(Engine.Submit(s, ctx, them, Command.AnswerTo(respond.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), "수락");
            foreach (int viewer in new[] { me, them })
            {
                var entries = new FxPlanner(viewer, 2).Plan(log.Events);
                var entry = entries.Find(e => e.Step(DealRows.Trade) != null);
                Assert.IsNotNull(entry, "거래 단계");
                var step = entry.Step(DealRows.Trade);
                Assert.AreEqual(2500, step.Ms);
                var done = TradeBeats.Event(step);
                Assert.AreEqual(1, done.GiveA.Length);
                Assert.AreEqual(1, done.GiveB.Length);
                // 받은 수는 단계 끝에 착지한다
                var jp = entry.Cues.Find(c => !c.IsSound && c.Key.Equals(ShownKey.Resource(me, ResKind.Jp, -1)));
                Assert.AreEqual(step.EndMs, jp.AtMs);
            }
            Assert.AreEqual(0f, TradeBeats.At(0, 2500).Cross);
            Assert.AreEqual(TradeBeats.Mid, TradeBeats.At(TradeBeats.GatherMs, 2500).Cross, 0.001f, "가운데로 모였다");
            Assert.AreEqual(1f, TradeBeats.At(TradeBeats.GlowAt, 2500).Cross, 0.001f, "건너갔다");
            Assert.Greater(TradeBeats.At(TradeBeats.GlowAt + TradeBeats.GlowMs / 2, 2500).Glow, 0.9f, "받는 쪽이 빛난다");
            Assert.LessOrEqual(TradeBeats.GlowAt + TradeBeats.GlowMs, 2500 - TradeBeats.OutMs);
            var board = new BoardLog(ctx);
            board.Feed(log.Events);
            var lines = new List<BoardLogLine>(board.Lines);
            Assert.IsTrue(lines.Exists(l => l.Line.Key == BoardLog.K + "trade.offer" && l.Seat == me));
            Assert.IsTrue(lines.Exists(l => l.Line.Key == BoardLog.K + "trade.done" && l.Seat == them));
        }

        [Test]
        public void M10_남좌석의스탬프는배틀끝항목에서떼어받은차례대로보드항목으로선다_결정178()
        {
            // 좌석 0의 화면: 배틀 끝 단계 뒤에 내 스탬프 하나 · 좌석 1의 스탬프 둘(3 → 4)이 잇달아 실려 왔다
            FxStep Stamp(int seat, int after)
            {
                var st = new FxStep { Row = DealRows.Stamp, Ms = PlayTiming.StampMs(1, 0) };
                st.Events.Add(new StampGained { Seat = seat, CountBefore = after - 1, CountAfter = after });
                return st;
            }
            var entry = new FxEntry { Seq = 9, Seat = 1 };
            var end = new FxStep { Row = 700, Ms = 1200 };
            end.Events.Add(new BattleEnded { Seat = 1 });
            var steps = new[] { end, Stamp(0, 2), Stamp(1, 3), Stamp(1, 4) };
            int t = 0;
            foreach (var st in steps)
            {
                st.StartMs = t;
                t += st.Ms;
                entry.Steps.Add(st);
                entry.Events.AddRange(st.Events);
            }
            entry.QueueMs = t;
            var entries = new List<FxEntry> { entry };

            Assert.AreEqual(2, DealRows.SplitOthers(entries, 0), "남의 스탬프 둘을 뗀다");
            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual(2, entry.Steps.Count, "배틀 끝 항목에는 배틀 끝 · 내 스탬프(결과 창 위에 찍는다)");
            Assert.AreEqual(0, StampBeats.Event(entry.Steps[1]).Seat);
            Assert.AreEqual(3, StampBeats.Event(entries[1].Steps[0]).CountAfter, "받은 차례대로(3 → 4)");
            Assert.AreEqual(4, StampBeats.Event(entries[2].Steps[0]).CountAfter);
            Assert.AreEqual(0, entries[1].Steps[0].StartMs);
            Assert.AreEqual(t, entries[0].LengthMs + entries[1].LengthMs + entries[2].LengthMs, "길이의 합은 그대로(P5)");
            Assert.IsTrue(OtherActs.Shows(entries[1].Steps[0], 0, 2), "뗀 단계는 그 좌석 칩 꼬리표");
            Assert.AreEqual("stamp", OtherActs.KeyOf(entries[1].Steps[0]));
            Assert.AreEqual(0, DealRows.SplitOthers(entries, 0), "두 번 떼지 않는다");
        }
    }
}
