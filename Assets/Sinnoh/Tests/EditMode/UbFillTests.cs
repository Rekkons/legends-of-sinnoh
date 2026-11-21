using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「연출 목록 채우기」의 엔진 무관 셈: 83행 토큰 날기(831 — <see cref="SpotFlyBeats"/>) · 93행 실버디 → 축복 +1(930 — <see cref="SilvallyBeats"/>)의
    /// 방장·화면 행과 길이 · 박자 · 대기열이 남았을 때의 칸·축복 수 · 실버디 축복 단계가 배틀 끝 항목에서 떨어져 결과 창 [확인] 뒤에 서는 것.
    /// </summary>
    public class UbFillTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        [Test]
        public void M6_토큰날기와실버디축복_방장과화면이같은행과길이와그리는곳()
        {
            var fly = new TokenMoved { Seat = SeatId.UltraBeast, TokenKind = TokenKind.HiddenSpot, From = 3, To = 40 };
            var bless = new BlessingGained { Seat = 0, Source = SourceRef.Rule(RuleSource.Silvally), Total = 4 };
            foreach (var (e, row, ms) in new (GameEvent, int, int)[] { (fly, UbRows.Fly, 1300), (bless, UbBattleRows.Silvally, 1400) })
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
                Assert.IsFalse(BattleRows.Built(row), row + "행은 보드가 그린다");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
                Assert.IsFalse(FxRows.BlocksAll(row), row + "행은 전원 차단이 아니다");
            }
            Assert.IsTrue(FxRows.Chains(UbRows.Fly), "토큰 날기는 옮김마다 단계 하나");
            // 아르세우스 운명 카드 3장의 축복은 단계가 없다(45행 카드 내기 안)
            var fate = new BlessingGained { Seat = 0, Source = new SourceRef(SourceKind.Fate, 0), Total = 2 };
            Assert.AreEqual(0, PlayTiming.RowOf(fate));
            Assert.AreEqual(0, FxRows.Of(fate));
        }

        [Test]
        public void M6_토큰날기는밟힌칸에서떠올라틈속을지나깨어진세계에내려앉는다()
        {
            var m = new TokenMoved { TokenKind = TokenKind.HiddenSpot, From = 3, To = 40 };
            const int total = 1000;
            Assert.AreEqual(3, SpotFlyBeats.At(m, -1, total).Cell, "시작 전 = 출발 칸");
            var rising = SpotFlyBeats.At(m, 200, total);
            Assert.AreEqual(3, rising.Cell);
            Assert.Greater(rising.Height, 0f, "떠오른다");
            Assert.AreEqual(-1, SpotFlyBeats.At(m, 470, total).Cell, "틈 속");
            var landing = SpotFlyBeats.At(m, 800, total);
            Assert.AreEqual(40, landing.Cell, "깨어진 세계의 칸");
            Assert.Greater(landing.Height, 0f);
            Assert.Less(landing.Height, 1f, "내려앉는 중");
            Assert.Greater(landing.Cross, 0.99f, "카메라는 건너갔다");
            Assert.AreEqual(40, SpotFlyBeats.At(m, total, total).Cell, "끝 = 도착 칸");
            // 갈 칸이 없으면 떠오른 뒤 사라진다
            var lost = new TokenMoved { TokenKind = TokenKind.HiddenSpot, From = 3, To = -1 };
            Assert.AreEqual(-1, SpotFlyBeats.At(lost, 800, total).Cell);

            // 대기열: 시작 전 단계 = 출발 칸 · 재생 중 = 그 순간 · 끝난 단계는 빠진다
            var entry = new FxEntry { Seq = 3 };
            var step = new FxStep { Row = UbRows.Fly, StartMs = 400, Ms = total };
            step.Events.Add(m);
            entry.Steps.Add(step);
            entry.QueueMs = 1600;
            var seq = new FxSequencer();
            seq.Enqueue(new[] { entry });
            var before = SpotFlyBeats.Pending(seq.Queued, seq.Current, seq.LocalMs);
            Assert.AreEqual(1, before.Count);
            Assert.AreEqual(3, before[0].Value.Cell, "단계 전에는 출발 칸");
            seq.Advance(400 + 470);
            Assert.AreEqual(-1, SpotFlyBeats.Pending(seq.Queued, seq.Current, seq.LocalMs)[0].Value.Cell, "틈 속");
            seq.Advance(1500 - seq.LocalMs);
            Assert.AreEqual(0, SpotFlyBeats.Pending(seq.Queued, seq.Current, seq.LocalMs).Count, "단계가 끝나면 보기 칸");
        }

        [Test]
        public void M6_실제UB턴의토큰날기는그토큰이옮긴때마다단계하나다()
        {
            // UB가 본맵의 숨겨진 장소를 밟는 판을 시드에서 찾는다 — 날기 단계는 그 UB의 이동 단계 뒤
            for (ulong seed = 20261117; seed < 20261117 + 30; seed++)
            {
                var s = UbScenes.Started(ctx, 4, seed);
                var planner = new FxPlanner(0, 4);
                planner.From(s);
                var log = new EventLog();
                UbScenes.EndRound(s, ctx, log);
                var events = new List<GameEvent>();
                foreach (var e in log.Events)
                {
                    var f = Engine.FilterFor(e, 0);
                    if (f != null) events.Add(f);
                }
                int moved = events.FindAll(e => e is TokenMoved).Count;
                if (moved == 0) continue;
                var entries = planner.Plan(events);
                FxBuilt.KeepSteps(entries, st => FxBuilt.Draws(st, 4));
                int steps = 0;
                foreach (var entry in entries)
                    foreach (var st in entry.Steps)
                        if (st.Row == UbRows.Fly)
                        {
                            steps++;
                            Assert.AreEqual(1300, st.Ms);
                            Assert.IsNotNull(SpotFlyBeats.Event(st));
                        }
                Assert.AreEqual(moved, steps, "옮김마다 단계");
                return;
            }
            Assert.Fail("시험 준비: 숨겨진 장소를 밟는 UB 턴이 없다");
        }

        [Test]
        public void M6_실버디축복은배틀끝뒤로떨어지고칩숫자는아이콘이닿은뒤에오른다()
        {
            var entry = new FxEntry { Seq = 7, Seat = 0 };
            var end = new BattleEnded { Seat = 0, BattleId = 1, Winner = 0, Result = BattleResult.Win, Changes = new ResChange[0] };
            var gain = new BlessingGained { Seat = 0, Source = SourceRef.Rule(RuleSource.Silvally), Total = 4 };
            var endStep = new FxStep { Row = 700, StartMs = 0, Ms = 3000 };
            endStep.Events.Add(end);
            var blessStep = new FxStep { Row = UbBattleRows.Silvally, StartMs = 3000, Ms = 1400 };
            blessStep.Events.Add(gain);
            entry.Steps.Add(endStep);
            entry.Steps.Add(blessStep);
            entry.Events.Add(end);
            entry.Events.Add(gain);
            entry.QueueMs = 4400;
            var entries = new List<FxEntry> { entry };
            Assert.AreEqual(1, UbBattleRows.SplitSilvally(entries));
            Assert.AreEqual(2, entries.Count);
            Assert.IsNull(entries[0].Step(UbBattleRows.Silvally), "배틀 끝 항목에는 축복 단계가 없다");
            var tail = entries[1];
            Assert.AreEqual(UbBattleRows.Silvally, tail.Steps[0].Row);
            Assert.AreEqual(4400, entries[0].QueueMs + tail.QueueMs, "두 항목 길이의 합은 그대로(P5)");

            // 칩 숫자: 대기열에 남은 동안은 얻기 전(3), 아이콘이 닿으면 4
            var seq = new FxSequencer();
            seq.Enqueue(new[] { tail });
            Assert.AreEqual(3, SilvallyBeats.Shown(4, seq.Queued, seq.Current, seq.LocalMs), "시작 전");
            seq.Advance(SilvallyBeats.FlyFromMs + SilvallyBeats.FlyMs / 2);
            Assert.AreEqual(3, SilvallyBeats.Shown(4, seq.Queued, seq.Current, seq.LocalMs), "나는 중");
            var flying = SilvallyBeats.At(seq.LocalMs, 1400);
            Assert.Greater(flying.Fly, 0f);
            Assert.Less(flying.Fly, 1f);
            Assert.IsFalse(flying.Arrived);
            seq.Advance(SilvallyBeats.FlyMs);
            Assert.AreEqual(4, SilvallyBeats.Shown(4, seq.Queued, seq.Current, seq.LocalMs), "닿은 뒤");
            var landed = SilvallyBeats.At(seq.LocalMs, 1400);
            Assert.IsTrue(landed.Arrived);
            Assert.Greater(landed.Ring, 0f, "금빛 고리");
            Assert.AreEqual(4, SilvallyBeats.Shown(4, new List<FxEntry>(), null, 0), "대기열이 비면 보기 값");
        }
    }
}
