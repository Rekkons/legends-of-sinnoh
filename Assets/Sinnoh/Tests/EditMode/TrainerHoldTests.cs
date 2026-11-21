using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>M6 「제작·사당·스탬프·진화·파티」의 backpacker R(rulings A39): 지닌물건 둘(`PokemonState.HeldItem2`) — 같은 것 불가 · 유물도.</summary>
    public class TrainerHoldTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static Maintenance Hold(int pokemon, int item, int slot) => new Maintenance { Kind = MaintKind.HoldItem, Pokemon = pokemon, Value = item, Slot = slot };

        static SubmitResult Do(GameState s, Ctx ctx, Maintenance m) => MaintenanceRules.Submit(s, ctx, 0, m, new EventLog());

        GameState Pack(out int p, out int band, out int sash, bool card = true)
        {
            var s = Battles.Game(ctx, 2);
            p = Scene.Give(s, ctx, 0, "445:0");
            Scene.Give(s, ctx, 0, "399:0");
            band = Scene.Item(ctx, "item.choice_band");
            sash = Scene.Item(ctx, "item.swiftness_sash");
            s.Seats[0].Bag.AddRange(new[] { band, sash, band });
            if (card)
            {
                s.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("backpacker");
                s.Seats[0].TrainerCardTier = TrainerRules.Gold;
            }
            return s;
        }

        [Test]
        public void A20_6_배낭여행자빨강_지닌물건둘_같은것은안됨()
        {
            var none = Pack(out int q, out int qband, out int qsash, false);
            Scene.Accept(Do(none, ctx, Hold(q, qband, 0)));
            Scene.Reject(Do(none, ctx, Hold(q, qsash, 1)), RejectReason.Illegal, "카드 없음: 둘째 칸이 없다");

            var s = Pack(out int p, out int band, out int sash);
            Scene.Accept(Do(s, ctx, Hold(p, band, 0)));
            Scene.Reject(Do(s, ctx, Hold(p, band, 1)), RejectReason.Illegal, "같은 아이템은 안 된다");
            Scene.Accept(Do(s, ctx, Hold(p, sash, 1)));
            var pk = s.Pokemon[p];
            Assert.AreEqual(band, pk.HeldItem);
            Assert.AreEqual(sash, pk.HeldItem2);
            var mods = new List<Mod>();
            BattleRules.PowerMods(s, ctx, p, -1, mods);
            Assert.IsTrue(mods.Exists(m => m.Source.Id == band) && mods.Exists(m => m.Source.Id == sash), "두 아이템 모두 쓴다");
            var pri = new List<Mod>();
            BattleRules.PriorityMods(s, ctx, p, pri);
            Assert.IsTrue(pri.Exists(m => m.Source.Id == sash), "둘째 칸의 민첩의 띠");
            Assert.AreEqual(2, StampRules.HeldCount(s, s.Seats[0]), "지닌물건 둘로 센다");

            int bag = s.Seats[0].Bag.Count;
            Scene.Accept(Do(s, ctx, new Maintenance { Kind = MaintKind.Release, Pokemon = p, Value = pk.Def }));
            Assert.AreEqual(bag + 2, s.Seats[0].Bag.Count, "놓아주면 둘 다 가방으로");
        }

        [Test]
        public void A20_6_배낭여행자빨강_둘째지닌물건은배낭여행자의포켓몬만_거래하면받은좌석의가방으로()
        {
            // 원작 커뮤니티 확인(자체 검토)
            var s = Pack(out int p, out int band, out int sash);
            Scene.Accept(Do(s, ctx, Hold(p, band, 0)));
            Scene.Accept(Do(s, ctx, Hold(p, sash, 1)));
            var log = new EventLog();
            int cell = Flow.StepInto(s, ctx, s.Seats[0].Cell);
            s.Seats[0].Cell = s.Seats[1].Cell = cell;
            Scene.Give(s, ctx, 1, "399:0");
            s.Seats[1].Jp = 3;
            var map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            var offer = TradeRules.Offer(1, 0, (int)TradeKind.Pokemon, p, 1, 1, (int)TradeKind.Jp, -1, 1);
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(map.Id, offer), log), "거래 제안");
            var respond = s.Pending.Find(c => c.Kind == ChoiceKind.Respond);
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(respond.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), "거래 받아들임");
            var pk = s.Pokemon[p];
            Assert.AreEqual(1, pk.Owner);
            Assert.AreEqual(band, pk.HeldItem, "첫 칸은 그대로 따라간다");
            Assert.AreEqual(-1, pk.HeldItem2, "둘째 칸은 backpacker의 포켓몬만 — 받은 좌석은 backpacker가 아니다");
            CollectionAssert.Contains(s.Seats[1].Bag, sash, "받은 좌석의 가방으로");
            Assert.IsTrue(log.OfType<ItemHeld>().Exists(e => e.FromPokemonId == p && e.PokemonId == -1 && e.ItemKey == sash));
        }

        [Test]
        public void A20_6_배낭여행자빨강_유물도둘째칸에_유물공격력을센다()
        {
            var s = Pack(out int p, out int band, out _);
            int relic = -1;
            for (int i = 0; i < ctx.Items.Items.Count && relic < 0; i++)
                if (RelicRules.IsRelic(ctx, i) && RelicRules.TypesOf(ctx, i).Count > 0) relic = i;
            Assert.GreaterOrEqual(relic, 0);
            s.Seats[0].Bag.Add(relic);
            Scene.Accept(Do(s, ctx, Hold(p, band, 0)));
            Scene.Accept(Do(s, ctx, Hold(p, relic, 1)));
            CollectionAssert.Contains(RelicRules.Owned(s, ctx, 0), relic, "둘째 칸의 유물도 가진 것");
            var type = RelicRules.TypesOf(ctx, relic)[0];
            int move = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count && move < 0; m++)
                if (ctx.Cards.Moves[m].Type == type) move = m;
            var mods = new List<Mod>();
            RelicRules.PowerMods(s, ctx, move, p, mods);
            Assert.IsTrue(mods.Exists(m => m.Source.Kind == SourceKind.Relic && m.Source.Id == relic), "둘째 칸 유물의 공격력 +1");
        }
    }
}
