using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「사당·균열 화면」의 연출 행(04 §8-8 31~34행 — <c>ShrineStaging.cs</c>, plan §5 #115): 방장 <c>PlayTiming</c>과 화면 <c>FxRows</c>가 같은 행·길이를 고르고,
    /// 실제 장면의 대기열에 그 단계가 서고, 박자가 단계 길이 안에서 끝난다.
    /// </summary>
    public class ShrineStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [Test]
        public void M5_사당행_방장과화면이같은행과길이()
        {
            var card = new SourceRef(SourceKind.ShrineEvent, 0);
            var events = new (GameEvent e, int row, int ms, bool all)[]
            {
                (new ShrineOffered { Seat = 0, Tier = 2 }, ShrineRows.Offer, 2500, false),
                (new ShrineCardsDrawn { Seat = 0, Tier = 2, CardKeys = new[] { 0, 1, 2 } }, ShrineRows.Offer, 2500, false),
                (new ShrineBlessing { Seat = 1, From = 0, Source = card, CardKey = 0, Tier = 2 }, ShrineRows.Blessing, 1500, false),
                (new LegendEvent { Seat = 0, Source = card, Placed = true }, ShrineRows.Legend, 3500, true),
                (new FollowerTally { Seat = 0, Source = card, Counts = new int[4], Tied = new int[0] }, ShrineRows.Tally, 1500, false),
                (new GodDescended { Seat = 0, Source = card, God = 2 }, ShrineRows.Descend, 4000, true),
            };
            foreach (var (e, row, ms, all) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.AreEqual(all, FxRows.BlocksAll(row), e.GetType().Name + " 전원 차단");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
            }
            Assert.AreEqual(WorkRows.Maint, FxRows.Of(new PokemonExchanged()), "동등한 교환의 답은 남 화면에 그 좌석의 판 보기(결정 120 — 정비 보기 행)");
            Assert.AreEqual(WorkRows.Maint, PlayTiming.RowOf(new PokemonExchanged()));
        }

        static FxEntry EntryWith(List<FxEntry> entries, int row) => entries.Find(e => e.Step(row) != null);

        [Test]
        public void M5_사당행_공물과축복이한항목에차례로선다_31_32()
        {
            var log = new EventLog();
            var s = ShrineScenes.Tranquil(ctx, log);
            var entries = new FxPlanner(s.ActiveSeat, 2).Plan(log.Events);
            var entry = EntryWith(entries, ShrineRows.Offer);
            Assert.IsNotNull(entry, "공물 단계");
            var offer = entry.Step(ShrineRows.Offer);
            var bless = entry.Step(ShrineRows.Blessing);
            Assert.IsNotNull(bless, "평온한 순간은 고를 것이 없어 같은 항목에서 축복까지");
            Assert.AreEqual(2500, offer.Ms);
            Assert.AreEqual(offer.EndMs, bless.StartMs, "공물 뒤에 축복");
            Assert.IsNotNull(ShrineRows.First<ShrineOffered>(offer));
            Assert.AreEqual(1, ShrineRows.First<ShrineCardsDrawn>(offer).CardKeys.Length);
            Assert.AreEqual(2, ShrineRows.First<ShrineBlessing>(bless).Tier, "분홍 = 2단계");
            Assert.AreEqual(9 + 4, s.Seats[s.ActiveSeat].Jp, "여행 점수 +4");
            // 박자가 단계 안에서 끝난다: 마지막 카드의 뒤집기 · 꼬리표
            Assert.LessOrEqual(OfferBeats.FlipAt(ShrineRules.ExtraDraw - 1) + OfferBeats.CardFlipMs / 2, offer.Ms, "3장째도 단계 안에서 뒤집힌다");
            Assert.LessOrEqual(BlessBeats.ArriveAt + BlessBeats.ArriveMs, bless.Ms);
            OfferBeats.Card(offer.Ms, 2, out float show, out float drop, out bool face);
            Assert.IsTrue(show >= 1f && drop <= 0f && face, "단계 끝에는 세 장 모두 앞면으로 제자리");
            OfferBeats.Card(0, 0, out show, out _, out face);
            Assert.IsTrue(show <= 0f && !face, "처음에는 카드가 없다");
        }

        [Test]
        public void M5_사당행_전설이벤트는전원차단이고좌석마다규토리비_33()
        {
            var log = new EventLog();
            var s = ShrineScenes.Legend(ctx, log);
            var entries = new FxPlanner(s.ActiveSeat, 2).Plan(log.Events);
            var entry = EntryWith(entries, ShrineRows.Legend);
            var step = LegendBeats.StepOf(entry);
            var legend = LegendBeats.Event(step);
            Assert.IsNotNull(legend);
            Assert.IsTrue(legend.Placed, "프리져 토큰을 놓는다");
            Assert.AreEqual(FxLayer.Block, step.Layer, "until-end(전원)");
            Assert.AreEqual(2, LegendBeats.RainSeats(entry), "모든 좌석");
            Assert.LessOrEqual(LegendBeats.RainLandAt(SeatId.MaxSeats - 1) + LegendBeats.PlusMs, step.Ms, "6인이어도 「+n」이 단계 안에서 보인다");
            var before = LegendBeats.At(LegendBeats.FallFromMs - 1, step.Ms, true);
            var falling = LegendBeats.At(LegendBeats.FallFromMs + LegendBeats.FallMs / 2, step.Ms, true);
            var landed = LegendBeats.At(LegendBeats.LandAt + 10, step.Ms, true);
            Assert.IsFalse(before.Shown, "어두워지는 동안에는 아직 없다");
            Assert.IsTrue(falling.Shown && falling.Fall > 0f && falling.Fall < 1f, "내려오는 중");
            Assert.IsTrue(landed.Shown && landed.Fall <= 0f && landed.Ring >= 0f, "내려앉으면 고리");
            Assert.IsFalse(LegendBeats.At(LegendBeats.LandAt, step.Ms, false).Shown, "놓지 않는 카드는 피규어가 없다");
            Assert.AreEqual(0f, LegendBeats.At(step.Ms, step.Ms, true).Dark, "끝나면 밝다");
        }

        [Test]
        public void M5_균열행_집계뒤에강림이서고추종자가반짝인다_34()
        {
            var log = new EventLog();
            var s = ShrineScenes.Fracture(ctx, log);
            var entries = new FxPlanner(s.ActiveSeat, 2).Plan(log.Events);
            var entry = EntryWith(entries, ShrineRows.Descend);
            Assert.IsNotNull(entry, "강림 단계");
            var tally = entry.Step(ShrineRows.Tally);
            var descend = DescendBeats.StepOf(entry);
            Assert.IsNotNull(tally, "집계 단계가 같은 항목에");
            Assert.LessOrEqual(tally.EndMs, descend.StartMs, "공개·집계 뒤에 강림");
            Assert.AreEqual(2, TallyBeats.Event(tally).Counts[(int)SinnohGod.Palkia]);
            Assert.AreEqual((int)SinnohGod.Palkia, DescendBeats.Event(descend).God);
            Assert.AreEqual(FxLayer.Block, descend.Layer);
            Assert.LessOrEqual(TallyBeats.FlipEveryMs * (SeatId.MaxSeats - 1) + TallyBeats.FlipMs, TallyBeats.BarFromMs, "6인의 토큰이 다 뒤집힌 뒤 막대");
            Assert.LessOrEqual(TallyBeats.LeadAt + 150, tally.Ms);
            TallyBeats.Flip(0, 0, out bool up);
            Assert.IsFalse(up, "처음에는 뒷면");
            TallyBeats.Flip(TallyBeats.FlipMs, 0, out up);
            Assert.IsTrue(up);
            Assert.IsFalse(DescendBeats.At(DescendBeats.FallFromMs - 1, descend.Ms, false).Shown, "번개 전에는 신이 없다");
            Assert.IsTrue(DescendBeats.At(DescendBeats.BoltAt + 10, descend.Ms, false).Flash > 0f, "번개");
            Assert.IsTrue(DescendBeats.At(0, descend.Ms, true).Shown, "이미 있던 신은 그대로 서 있다");
            DescendBeats.Spark(DescendBeats.SparkAt + 300, out float plus);
            Assert.AreEqual(1f, plus, "「이동력 +1」");
            Assert.LessOrEqual(DescendBeats.SparkAt + DescendBeats.SparkMs, descend.Ms);
            Assert.AreEqual(SinnohGod.Palkia, FractureRules.OriginGod(s, ctx));
        }

        [Test]
        public void M9_다듬기6_축복일곱가지는갈래마다다르게움직이고_판위빛기둥은단계마다높이가다르다()
        {
            // P36: 판 위 빛기둥 — 사당 판과 같은 박자, 단계마다 높이 1/3 · 2/3 · 1
            Assert.AreEqual(1f / 3f, OfferBeats.BoardHeight(1), 0.001f);
            Assert.AreEqual(2f / 3f, OfferBeats.BoardHeight(2), 0.001f);
            Assert.AreEqual(1f, OfferBeats.BoardHeight(3));
            Assert.AreEqual(1f, OfferBeats.BoardHeight(9), "상한");
            Assert.AreEqual(0f, OfferBeats.Pillar(OfferBeats.PillarAt - 1, out float light));
            Assert.AreEqual(0f, light);
            Assert.AreEqual(1f, OfferBeats.Pillar(OfferBeats.PillarAt + OfferBeats.PillarRiseMs, out light));
            Assert.AreEqual(1f, light);
            Assert.LessOrEqual(OfferBeats.PillarAt + OfferBeats.PillarRiseMs + OfferBeats.PillarHoldMs + OfferBeats.PillarFadeMs, PlayTiming.RowMs(new ShrineOffered(), ShrineRows.Offer), "방장 길이(2500) 안");

            // P35: 축복 일곱 장 = 갈래 일곱(겹치지 않는다), 그 밖의 사당 카드는 None
            var cards = new[] { ShrineRules.FatefulEncounter, ShrineRules.UnexpectedGift, ShrineRules.EquivalentExchange, ShrineRules.AncientWisdom, ShrineRules.SplendidBanquet, ShrineRules.HiddenTreasure, ShrineRules.TranquilMoment };
            var seen = new HashSet<BlessGift>();
            foreach (string card in cards)
            {
                Assert.GreaterOrEqual(ShrineRules.CardKey(ctx, card), 0, card);
                var gift = BlessBeats.GiftOf(card);
                Assert.AreNotEqual(BlessGift.None, gift, card);
                Assert.IsTrue(seen.Add(gift), "갈래가 겹친다: " + card);
            }
            Assert.AreEqual(BlessGift.None, BlessBeats.GiftOf(ShrineRules.CelestialFracture));
            Assert.AreEqual(BlessGift.None, BlessBeats.GiftOf(null));
            int total = PlayTiming.RowMs(new ShrineBlessing(), ShrineRows.Blessing);
            Assert.AreEqual(1500, total);
            Assert.LessOrEqual(BlessBeats.GiftFromMs + BlessBeats.GiftEveryMs * (BlessBeats.MaxGifts - 1) + BlessBeats.GiftMs, total - BlessBeats.GiftOutMs, "마지막 그림도 걷히기 전에 내려앉는다(방장 길이 그대로)");
            Assert.AreEqual(3, BlessBeats.Gifts(BlessGift.Fate, 3));
            Assert.AreEqual(2, BlessBeats.Gifts(BlessGift.Apricorn, 2));
            Assert.AreEqual(1, BlessBeats.Gifts(BlessGift.Banquet, 1));
            Assert.AreEqual(1, BlessBeats.Gifts(BlessGift.Treasure, 3));
            Assert.AreEqual(0, BlessBeats.Gifts(BlessGift.Tranquil, 3), "여행 점수는 숫자만");
            int mid = BlessBeats.GiftFromMs + BlessBeats.GiftMs / 4, end = BlessBeats.GiftFromMs + BlessBeats.GiftMs + 160;
            var poses = new List<string>();
            foreach (var gift in seen)
            {
                Assert.AreEqual(0f, BlessBeats.Gift(gift, BlessBeats.GiftFromMs - 1, total, 0).Show, "꼬리표 전에는 없다");
                Assert.AreEqual(0f, BlessBeats.Gift(gift, mid, total, 2).Show, "뒤 그림은 어긋나 나온다");
                var p = BlessBeats.Gift(gift, mid, total, 0);
                Assert.AreEqual(1f, p.Show);
                poses.Add(p.X.ToString("0.00") + "/" + p.Y.ToString("0.00") + "/" + p.Wide.ToString("0.00") + "/" + p.Size.ToString("0.00"));
                var landed = BlessBeats.Gift(gift, end, total, 0);
                Assert.AreEqual(0f, landed.X, 0.001f, gift + " 제자리");
                Assert.AreEqual(0f, landed.Y, 0.001f, gift + " 제자리");
                Assert.AreEqual(1f, landed.Wide, 0.001f);
                Assert.AreEqual(1f, landed.Size, 0.001f);
                Assert.AreEqual(0f, BlessBeats.Gift(gift, total, total, 0).Show, "단계가 끝나면 걷힌다");
            }
            // 여행 점수는 움직이지 않고 숫자가 굴러 오른다 — 나머지 여섯은 가운데 자세가 서로 다르다
            Assert.AreEqual(7, new HashSet<string>(poses).Count, "갈래마다 다른 움직임: " + string.Join(" ", poses));
            Assert.AreEqual(0, BlessBeats.Roll(BlessBeats.GiftFromMs, 4));
            Assert.That(BlessBeats.Roll(BlessBeats.GiftFromMs + BlessBeats.GiftMs / 2, 4), Is.InRange(1, 4));
            Assert.AreEqual(4, BlessBeats.Roll(BlessBeats.GiftFromMs + BlessBeats.GiftMs, 4));
        }
    }
}
