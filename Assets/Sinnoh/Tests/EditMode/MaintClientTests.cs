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
    /// plan §4 「정비」(04 §3-5)의 순수 부분: 좌석 손님의 정비(<see cref="SeatClient.CanMaintain"/>·<see cref="SeatClient.Maintain"/> — 화면이 칸을 켜고 끄는 검사와 호스트가 받는 검사가 같다, rulings G8·G10),
    /// 쥐여 준 판(<see cref="SoloTrail.Kit"/>), 정비 이벤트의 보드 기록 줄.
    /// </summary>
    public class MaintClientTests
    {
        Ctx ctx;
        StringTable ko;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        sealed class Run
        {
            public SeatClient Client;
            public BoardLog Log;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public long Now => 0;
        }

        // 쥐여 준 판을 시작해 셋업만 끝낸다(스타팅 포켓몬 하나 · 기술 카드 여섯 · 지닌물건 셋)
        Run Start()
        {
            var trail = SoloTrail.Kit;
            var run = new Run { Log = new BoardLog(ctx) };
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.PrepareFor(ctx));
            run.Client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            run.Client.Updated += u =>
            {
                run.Events.AddRange(u.Events);
                run.Log.Feed(u.Events);
            };
            for (int i = 0; i < 20 && run.Client.View.Phase == Phase.Setup; i++)
            {
                var c = run.Client.FirstChoice();
                Assert.IsNotNull(c, "셋업의 선택");
                var legal = run.Client.Legal(c);
                var a = c.Kind == ChoiceKind.VillagePick ? legal[trail.Village] : c.Kind == ChoiceKind.StarterPick ? legal[trail.Starter] : legal[0];
                Assert.IsTrue(run.Client.Answer(c, a, run.Now).Accepted, c.Kind.ToString());
            }
            Assert.AreNotEqual(Phase.Setup, run.Client.View.Phase);
            return run;
        }

        int Move(string key)
        {
            for (int i = 0; i < ctx.Cards.Moves.Count; i++) if (ctx.Cards.Moves[i].Key == key) return i;
            Assert.Fail("기술 없음: " + key);
            return -1;
        }

        static Maintenance SetMove(int p, int slot, int move) => new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = slot, Value = move };
        static Maintenance Hold(int p, int item) => new Maintenance { Kind = MaintKind.HoldItem, Pokemon = p, Value = item };

        [Test]
        public void M4_쥐여준판은기술카드와지닌물건이내보기에있고길은포획판과같다()
        {
            var run = Start();
            var me = run.Client.View.Seats[0];
            Assert.AreEqual(SoloTrail.KitMoves.Length, me.StampMoves.Count);
            Assert.AreEqual(SoloTrail.KitItems.Length, me.Bag.Count);
            foreach (int item in me.Bag) Assert.AreEqual(ItemKind.Held, ctx.Items.Items[item].Kind);
            Assert.AreEqual(SoloTrail.Catch.Seed, SoloTrail.Kit.Seed);
            Assert.AreEqual(SoloTrail.Catch.Balls, SoloTrail.Kit.Balls);
            Assert.IsNull(SoloTrail.Defeat.PrepareFor(ctx), "손볼 것이 없는 판");
            Assert.IsNotNull(SoloTrail.Catch.PrepareFor(ctx));
        }

        [Test]
        public void G8_좌석손님의정비검사는호스트가받는것과같다()
        {
            var run = Start();
            var client = run.Client;
            int p = client.View.Seats[0].Party[0];
            var def = ctx.Cards.Pokemon[client.View.Pokemon[p].Def];
            // 풀의 카드마다: 배울 수 있는 타입이면 None, 아니면 Illegal(G10 U15-2) — 화면이 칸을 켜는 검사
            int can = 0, cannot = 0;
            foreach (int move in client.View.Seats[0].StampMoves)
            {
                bool learnable = ChoiceRules.CanLearn(ctx, client.View.Pokemon[p], ctx.Cards.Moves[move].Type);
                Assert.AreEqual(learnable ? RejectReason.None : RejectReason.Illegal, client.CanMaintain(SetMove(p, 0, move)), ctx.Cards.Moves[move].Key + " → " + def.Key);
                if (learnable) can++; else cannot++;
            }
            Assert.Greater(can, 0);
            Assert.Greater(cannot, 0);
            Assert.AreEqual(RejectReason.Illegal, client.CanMaintain(SetMove(p, PokemonState.MoveSlots, Move("surf"))), "칸 2는 없다");

            // 낼 수 있다고 본 것은 받아들여지고 보기가 바뀐다
            int surf = Move("surf"), before = client.Updates;
            Assert.AreEqual(RejectReason.None, client.CanMaintain(SetMove(p, 0, surf)));
            Assert.IsTrue(client.Maintain(SetMove(p, 0, surf), run.Now).Accepted);
            Assert.Greater(client.Updates, before, "받아들여지면 갱신이 온다");
            Assert.AreEqual(surf, client.View.Pokemon[p].Moves[0]);
            Assert.IsFalse(client.View.Seats[0].StampMoves.Contains(surf), "풀에서 꺼냈다");
            // 낼 수 없다고 본 것은 같은 이유로 거절된다
            int drain = Move("giga_drain");
            var why = client.CanMaintain(SetMove(p, 1, drain));
            Assert.AreEqual(RejectReason.Illegal, why);
            var result = client.Maintain(SetMove(p, 1, drain), run.Now);
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(why, result.Reason);
            Assert.IsNotNull(client.LastRejected);
            // 같은 값(G8): NoChange
            Assert.AreEqual(RejectReason.NoChange, client.CanMaintain(SetMove(p, 1, -1)));
            // 마지막 포켓몬은 놓아주지 못한다(p.27)
            Assert.AreEqual(RejectReason.Illegal, client.CanMaintain(new Maintenance { Kind = MaintKind.Release, Pokemon = p, Value = client.View.Pokemon[p].Def }));
            // 지닌물건: 가방에 있는 것만
            int band = client.View.Seats[0].Bag[0];
            Assert.AreEqual(RejectReason.None, client.CanMaintain(Hold(p, band)));
            Assert.IsTrue(client.Maintain(Hold(p, band), run.Now).Accepted);
            Assert.AreEqual(band, client.View.Pokemon[p].HeldItem);
            Assert.AreEqual(RejectReason.NoChange, client.CanMaintain(Hold(p, band)));
            Assert.AreEqual(SoloTrail.KitItems.Length - 1, client.View.Seats[0].Bag.Count);
        }

        [Test]
        public void M4_기록은정비의기술과지닌물건을줄로남긴다()
        {
            var run = Start();
            var client = run.Client;
            int p = client.View.Seats[0].Party[0], surf = Move("surf"), band = client.View.Seats[0].Bag[0];
            Assert.IsTrue(client.Maintain(SetMove(p, 0, surf), run.Now).Accepted);
            Assert.IsTrue(client.Maintain(SetMove(p, 0, -1), run.Now).Accepted);
            Assert.IsTrue(client.Maintain(Hold(p, band), run.Now).Accepted);
            Assert.IsTrue(client.Maintain(Hold(p, -1), run.Now).Accepted);
            var keys = new List<string>();
            var texts = new List<string>();
            foreach (var line in run.Log.Lines)
            {
                keys.Add(line.Line.Key);
                texts.Add(line.Text(k => ko[k], seat => "플레이어 " + (seat + 1)));
            }
            string mon = ko["pokemon." + ctx.Cards.Pokemon[client.View.Pokemon[p].Def].Key];
            int taught = keys.IndexOf("screen.log.line.taught"), forgot = keys.IndexOf("screen.log.line.forgot"), held = keys.IndexOf("screen.log.line.held"), unheld = keys.IndexOf("screen.log.line.unheld");
            Assert.IsTrue(taught >= 0 && forgot > taught && held > forgot && unheld > held, string.Join(" | ", keys));
            StringAssert.Contains(mon, texts[forgot]);
            StringAssert.Contains(ko["move.surf"], texts[forgot]);
            StringAssert.Contains(ko[ctx.Items.Items[band].Key], texts[held]);
            StringAssert.Contains(ko[ctx.Items.Items[band].Key], texts[unheld]);
            foreach (int i in new[] { forgot, held, unheld })
            {
                StringAssert.DoesNotContain("{", texts[i]);
                StringAssert.DoesNotContain("(", texts[i], "조사를 골랐다: " + texts[i]);
            }
        }
    }
}
