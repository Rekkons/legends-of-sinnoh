using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「운명·응답 화면」의 연출 행(04 §8-8 7·8·14·42~45행 — <c>FateStaging.cs</c>, plan §5 #98): 방장 <c>PlayTiming</c>과 화면 <c>FxRows</c>가 같은 행·길이를 고르고, 박자가 단계 안에서 끝난다.
    /// </summary>
    public class FateStagingTests
    {
        static FateDealt Dealt(RuleSource why, int seats)
        {
            var d = new FateDealt { Seat = 0, Source = SourceRef.Rule(why), StarCount = 4, PerSeat = new FateDeal[seats] };
            for (int i = 0; i < seats; i++) d.PerSeat[i] = new FateDeal { Seat = i, Before = 0, After = 1, CardKeys = new[] { 0 } };
            return d;
        }

        [Test]
        public void M5_운명행_방장과화면이같은행과길이()
        {
            var events = new (GameEvent e, int row, int ms)[]
            {
                (Dealt(RuleSource.FallenStar, 1), FateRows.Star, 950),
                (Dealt(RuleSource.FallenStar, 6), FateRows.Star, 1700),
                (Dealt(RuleSource.Respawn, 1), 80, 2800),   // 귀환의 운명 1장은 회복 단계에 얹는다(길이 그대로)
                (Dealt(RuleSource.TraitorReturn, 1), FateRows.Draw, 600),
                (new FateDiscarded { Seat = 0, CardKey = 0, Before = 6, After = 5 }, FateRows.Discard, 600),
                (new FatePlayed { Seat = 0, Card = FateCard.Palkia, Play3 = false }, FateRows.Play, 600),
                (new FatePlayed { Seat = 0, Card = FateCard.Dialga, Play3 = true }, FateRows.Play, 1200),
                (new Teleported { Seat = 0, From = 1, To = 2 }, FateRows.Teleport, 1800),
                (new DiceFacesChanged { Source = new SourceRef(SourceKind.Fate, 0), Changes = new FaceChange[2] }, FateRows.Reroll, PlayTiming.RerollMs(2)),
                (new HpChanged { Source = new SourceRef(SourceKind.Fate, 2), Before = 5, After = 3 }, FateRows.Sacrifice, 1500),
            };
            foreach (var (e, row, ms) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
            }
            Assert.AreEqual(1600, PlayTiming.RerollMs(2), "주사위당 600 + 다시 세기 400");
        }

        [Test]
        public void M5_운명박자_내카드만앞면으로_버리면찢기고_순간이동은틈을지나도착칸()
        {
            const int total = 600;
            var mine = FateDealBeats.At(total - 1, total, 0, true);
            var theirs = FateDealBeats.At(total - 1, total, 0, false);
            Assert.AreEqual(1f, mine.T, 0.02, "끝에는 손에 닿았다");
            Assert.AreEqual(1f, mine.Face, 0.02, "내 카드는 앞면으로");
            Assert.AreEqual(0f, theirs.Face, "남의 카드는 뒷면 그대로");
            Assert.AreEqual(0f, FateDealBeats.At(total, total, 0, true).Show, "단계가 끝나면 걷힌다");

            Assert.AreEqual(0f, FateDiscardBeats.At(0, total).Tear);
            Assert.Greater(FateDiscardBeats.At(total - 1, total).Tear, 0.95f, "끝에는 다 찢겼다");

            const int warp = 1800;
            Assert.IsFalse(TeleportBeats.At(100, warp).AtDest);
            Assert.IsTrue(TeleportBeats.At(warp * (TeleportBeats.GonePermille + TeleportBeats.BackPermille) / 2000, warp).Hidden, "틈 속에서는 말을 그리지 않는다");
            var land = TeleportBeats.At(warp - 1, warp);
            Assert.IsTrue(land.AtDest);
            Assert.Less(land.Height, 0.05f, "끝에는 도착 칸에 내려앉았다");

            int per = PlayTiming.RerollPerMs.Value;
            Assert.IsTrue(RerollBeats.At(RerollBeats.LockMs, 0, per).Locked, "첫 주사위 고정");
            Assert.IsFalse(RerollBeats.At(RerollBeats.LockMs, 1, per).Locked, "둘째는 다음 차례(하나씩)");
            Assert.Greater(RerollBeats.At(per + RerollBeats.InMs, 1, per).Glitch, 0f);

            var pass = FatePassBeats.At(FatePassBeats.MergeFromMs + FatePassBeats.MergeMs, 1200, true);
            Assert.AreEqual(0f, pass.Spread, 1e-3, "3장은 합쳐졌다");
            Assert.AreEqual(0f, FatePassBeats.At(600, 1200, true).X, "가운데에 머문다");
        }
    }
}
