using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3 완료 조건 2 「M3_ViewFor손패누출0」(01 §12 보기 누출): 숨은 정보만 다른 두 상태 → 남의 보기·걸러진 이벤트가 같다(B2).
    /// 숨은 것(01 §4): 남의 운명 손패·트레이너 카드 후보, 덱 순서·내용, 뒷면 숨겨진 장소, Rng, 남의 비공개 선택의 후보·Default·받아 둔 답.
    /// </summary>
    public class ViewTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        /// <summary>같은 공개 상태, variant마다 숨은 정보만 다르다. 좌석 0·1은 트레이너전 중.</summary>
        GameState Make(int variant, EventLog log)
        {
            var s = Scene.Game(ctx, 3, 2026);
            Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 1, "390:0");
            Scene.Give(s, ctx, 2, "393:0");
            var kinds = ctx.Deck(DeckId.ChangeOfFate).Cards;
            var trainer = ctx.Deck(DeckId.TrainerCard).Cards;
            var seat1 = s.Seats[1];
            seat1.Stamps[StampRules.FateCards] = true; // 스탬프 7(서로 다른 운명 3장)은 숨은 손패로 공개 사건을 만든다(원작도 시트에 찍는다, rulings A30) — 이 시험은 그 칸을 미리 채운다
            if (variant == 0)
            {
                seat1.FateHand.AddRange(new[] { kinds[0].Card, kinds[0].Card, kinds[1].Card });
                seat1.TrainerCardOffer.AddRange(new[] { trainer[0].Card, trainer[1].Card, trainer[2].Card });
            }
            else
            {
                seat1.FateHand.AddRange(new[] { kinds[2].Card, kinds[3].Card, kinds[3].Card });
                seat1.TrainerCardOffer.AddRange(new[] { trainer[10].Card, trainer[20].Card, trainer[30].Card });
                // 덱 순서, 뒷면 토큰 정체, Rng
                var rng = new Pcg32(424242);
                var ginkgo = s.Decks[(int)DeckId.GinkgoTrade];
                int open = ginkgo[ginkgo.Count - 1];
                foreach (var pile in s.Decks) Piles.Shuffle(pile, rng);
                // 은행상회 덱의 맨 위 거래 카드는 공개 정보다(rulings A4) — 두 판에서 같게 둔다
                ginkgo.Remove(open);
                ginkgo.Add(open);
                int first = -1;
                for (int i = 0; i < s.Tokens.Count; i++)
                {
                    var t = s.Tokens[i];
                    if (t.Kind != TokenKind.HiddenSpot) continue;
                    if (first < 0) first = i;
                    else if (s.Tokens[first].Def != t.Def)
                    {
                        int d = t.Def;
                        t.Def = s.Tokens[first].Def;
                        s.Tokens[first].Def = d;
                        break;
                    }
                }
                s.Rng = new Pcg32(99);
            }
            // 뒷면 토큰 하나는 판 위에(자리는 공개)
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) { t.Cell = 100; break; }
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.Step = BattleStep.PostRoll;

            // 좌석 1의 숨은 선택: 트레이너 카드 고르기(비공개, Default 무작위), 손패 6장째 버리기(Default = 마지막 카드)
            var fx = Scene.Fx(s, log);
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.TrainerCardPick, Seat = 1, Secret = true });
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.Discard, Seat = 1 });
            // 비공개 묶음: 좌석 1이 먼저 다른 답을 냈다
            var group = new List<PendingChoice>();
            for (int i = 0; i < 3; i++) group.Add(new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.Follower, Seat = i, Secret = true });
            ChoiceRules.OpenGroup(s, ctx, fx, group);
            var god = variant == 0 ? SinnohGod.Dialga : SinnohGod.Giratina;
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(group[1].Id, ChoiceAnswer.Pick((int)god)), log));
            return s;
        }

        static void SameFor(GameState a, GameState b, EventLog la, EventLog lb, string when)
        {
            foreach (int viewer in new[] { 0, 2, SeatId.Observer })
            {
                Assert.AreEqual(StateHash.Of(Engine.ViewFor(a, viewer)), StateHash.Of(Engine.ViewFor(b, viewer)), $"{when}: 좌석 {viewer}의 보기가 숨은 정보로 달라졌다");
                Assert.AreEqual(EventDump.Of(la.Events, viewer), EventDump.Of(lb.Events, viewer), $"{when}: 좌석 {viewer}에게 간 이벤트가 숨은 정보로 달라졌다");
            }
        }

        [Test]
        public void M3_ViewFor손패누출0()
        {
            var la = new EventLog();
            var lb = new EventLog();
            var a = Make(0, la);
            var b = Make(1, lb);
            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b), "원본은 다르다");
            Assert.AreNotEqual(StateHash.Of(Engine.ViewFor(a, 1)), StateHash.Of(Engine.ViewFor(b, 1)), "본인은 자기 손패를 본다");
            Assert.AreNotEqual(EventDump.Of(la.Events, 1), EventDump.Of(lb.Events, 1), "본인은 자기 Legal을 받는다");
            SameFor(a, b, la, lb, "셋업 뒤");

            // 둘 다에서 합법인 같은 명령 열: 정비, 비공개 묶음 답, 운명 반응 창(장수로 연다) + 「안 씀」
            foreach (var (s, log) in new[] { (a, la), (b, lb) })
            {
                var m = new Maintenance { Kind = MaintKind.HoldItem, Pokemon = s.Seats[2].Party[0], Value = Scene.Item(ctx, "item.lucky_egg") };
                s.Seats[2].Bag.Add(m.Value);
                Scene.Accept(Engine.Submit(s, ctx, 2, Command.Anytime(m), log));
                var follower0 = s.Pending.Find(c => c.Kind == ChoiceKind.Simultaneous && c.Seat == 0);
                Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(follower0.Id, ChoiceAnswer.Pick((int)SinnohGod.Palkia)), log));
                var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
                Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.AfterRoll));
                foreach (var c in s.Pending.FindAll(c => c.Kind == ChoiceKind.Reaction))
                    Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Decline)), log));
            }
            SameFor(a, b, la, lb, "명령 뒤");

            // 보기 내용 확인: 남의 손패·후보·덱·뒷면 토큰·Rng는 Unknown, 개수는 그대로, 내 것은 보인다
            var v = Engine.ViewFor(a, 0);
            Assert.AreEqual(0, v.Viewer);
            Assert.AreEqual(3, v.Seats[1].FateHand.Count);
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, v.Seats[1].FateHand);
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, v.Seats[1].TrainerCardOffer);
            Assert.AreEqual(a.Decks[(int)DeckId.Weak].Count, v.Decks[(int)DeckId.Weak].Count);
            Assert.IsTrue(v.Decks[(int)DeckId.Weak].TrueForAll(x => x == Engine.Unknown));
            Assert.IsTrue(v.Tokens.TrueForAll(t => t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Def == Engine.Unknown));
            Assert.IsTrue(v.Tokens.Exists(t => t.Kind == TokenKind.HiddenSpot && t.Cell == 100), "뒷면 토큰의 자리는 공개");
            Assert.AreEqual(0UL, v.Rng.State);
            var discard = v.Pending.Find(c => c.Kind == ChoiceKind.Discard);
            Assert.AreEqual(Engine.Unknown, discard.Default.Target, "남의 버리기 Default(가장 나중 카드)는 가린다");
            var mine = Engine.ViewFor(a, 1);
            CollectionAssert.AreEqual(a.Seats[1].FateHand, mine.Seats[1].FateHand);
            Assert.AreEqual(a.Pending.Find(c => c.Kind == ChoiceKind.Discard).Default.Target, mine.Pending.Find(c => c.Kind == ChoiceKind.Discard).Default.Target);
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(Engine.ViewFor(a, SeatId.HostViewer)), "호스트 원본 보기 = 원본");

            // 자기 보기에서 Legal이 원본과 같다(손님이 하이라이트를 계산한다, 01 §1)
            var origDiscard = a.Pending.Find(c => c.Kind == ChoiceKind.Discard);
            Assert.AreEqual(EventDump.One(Scene.Legal(a, ctx, origDiscard)), EventDump.One(Scene.Legal(mine, ctx, mine.Pending.Find(c => c.Kind == ChoiceKind.Discard))));
        }

        /// <summary>그 좌석에 가는 메시지 바이트(02 §4-4): 첫 상태 스냅샷 + 다음 상태 델타, 이벤트는 그 좌석에 거른 것. Data 직렬화(SaveStore.Serialize).</summary>
        static string Wire(GameState s0, List<GameEvent> e0, GameState s1, List<GameEvent> e1, int viewer)
        {
            var enc = new SeatStream();
            var snap = enc.Encode(new GameUpdate { Version = 1, View = Engine.ViewFor(s0, viewer), Events = Filter(e0, viewer), AnimEndMs = 0 });
            var delta = enc.Encode(new GameUpdate { Version = 2, View = Engine.ViewFor(s1, viewer), Events = Filter(e1, viewer), AnimEndMs = 0 });
            Assert.IsInstanceOf<GameSnapshot>(snap);
            Assert.IsInstanceOf<GameDelta>(delta);
            return SaveStore.Serialize(snap, false) + "\n" + SaveStore.Serialize(delta, false);
        }

        static List<GameEvent> Filter(List<GameEvent> events, int viewer)
        {
            var r = new List<GameEvent>();
            foreach (var e in events)
            {
                var f = Engine.FilterFor(e, viewer);
                if (f != null) r.Add(f);
            }
            return r;
        }

        [Test]
        public void M7_보기누출_전선위바이트_스냅샷델타이벤트가같다()
        {
            // 02 §10 「보기 누출(전선 위)」: M3_ViewFor손패누출0의 두 상태(숨은 정보만 다르다)와 같은 명령 열 — 남 좌석에 가는 스냅샷·델타·이벤트의 직렬화 바이트가 같다.
            var la = new EventLog();
            var lb = new EventLog();
            var a = Make(0, la);
            var b = Make(1, lb);
            var a0 = a.Clone();
            var b0 = b.Clone();
            var ea0 = new List<GameEvent>(la.Events);
            var eb0 = new List<GameEvent>(lb.Events);
            foreach (var (s, log) in new[] { (a, la), (b, lb) })
            {
                var m = new Maintenance { Kind = MaintKind.HoldItem, Pokemon = s.Seats[2].Party[0], Value = Scene.Item(ctx, "item.lucky_egg") };
                s.Seats[2].Bag.Add(m.Value);
                Scene.Accept(Engine.Submit(s, ctx, 2, Command.Anytime(m), log));
                var follower0 = s.Pending.Find(c => c.Kind == ChoiceKind.Simultaneous && c.Seat == 0);
                Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(follower0.Id, ChoiceAnswer.Pick((int)SinnohGod.Palkia)), log));
                var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
                Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.AfterRoll));
            }
            var ea1 = la.Events.GetRange(ea0.Count, la.Events.Count - ea0.Count);
            var eb1 = lb.Events.GetRange(eb0.Count, lb.Events.Count - eb0.Count);
            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b), "원본은 다르다");
            foreach (int viewer in new[] { 0, 2, SeatId.Observer })
                Assert.AreEqual(Wire(a0, ea0, a, ea1, viewer), Wire(b0, eb0, b, eb1, viewer), $"좌석 {viewer}에게 가는 메시지 바이트가 숨은 정보로 달라졌다");
            Assert.AreNotEqual(Wire(a0, ea0, a, ea1, 1), Wire(b0, eb0, b, eb1, 1), "본인(좌석 1)은 자기 손패·후보를 받는다");
        }

        [Test]
        public void M3_FilterFor는남의카드키와Legal을가림()
        {
            var dealt = new TrainerCardsDealt { Seat = 1, Count = 3, CardKeys = new[] { 4, 5, 6 } };
            var other = (TrainerCardsDealt)Engine.FilterFor(dealt, 0);
            Assert.IsNull(other.CardKeys);
            Assert.AreEqual(3, other.Count, "장수는 남는다");
            Assert.AreSame(dealt, Engine.FilterFor(dealt, 1));
            Assert.AreSame(dealt, Engine.FilterFor(dealt, SeatId.HostViewer));
            Assert.IsNull(((TrainerCardsDealt)Engine.FilterFor(dealt, SeatId.Observer)).CardKeys);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, dealt.CardKeys, "원본은 그대로");

            var legal = new List<ChoiceAnswer> { ChoiceAnswer.Of(AnswerKind.Decline) };
            var req = new ChoiceRequested { Seat = 2, Id = 9, Legal = legal };
            Assert.IsNull(((ChoiceRequested)Engine.FilterFor(req, 0)).Legal);
            Assert.AreEqual(9, ((ChoiceRequested)Engine.FilterFor(req, 0)).Id);
            Assert.AreSame(req, Engine.FilterFor(req, 2));
            var galactic = new ChoiceRequested { Seat = SeatId.Galactic, Legal = legal };
            Assert.AreSame(galactic, Engine.FilterFor(galactic, SeatId.Observer), "가상 응답자 선택은 공개(갤럭시단 AI는 관전자 보기로 답한다)");
        }

        [Test]
        public void B2_보기의뒷면숨겨진장소순서는칸순_놓은순서가새지않음()
        {
            // 실제 셋업(R3): 유물 묶음을 먼저 놓고 지역이 차면 마지막 숨겨진 장소가 겹친다. 보기는 토큰 순서·칸을 그대로 보내므로 목록 순서가 놓은 순서면
            // 「같은 지역 쌍의 큰 색인보다 뒤 = 유물 묶음」을 알아낼 수 있었다(검토: 300판 중 94판, 133개, 틀림 0). 셋업 뒤 뒷면 토큰은 칸 순서다(rulings G12).
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = Flow.Started(ctx, 3, seed, timeout: true);
                var v = Engine.ViewFor(s, SeatId.Observer);
                int prev = -1, onBoard = 0;
                bool offBoard = false;
                for (int i = 0; i < v.Tokens.Count; i++)
                {
                    var t = v.Tokens[i];
                    if (t.Kind != TokenKind.HiddenSpot || t.FaceUp) continue;
                    Assert.AreEqual(Engine.Unknown, t.Def);
                    Assert.AreEqual(s.Tokens[i].Cell, t.Cell);
                    if (t.Cell < 0)
                    {
                        offBoard = true;
                        continue;
                    }
                    Assert.IsFalse(offBoard, $"시드 {seed}: 보드 밖 토큰은 맨 뒤");
                    Assert.Greater(t.Cell, prev, $"시드 {seed}: 뒷면 토큰 목록 순서 = 칸 순서");
                    prev = t.Cell;
                    onBoard++;
                }
                Assert.AreEqual(52, onBoard, $"시드 {seed}: 유물 17 + 숨겨진 장소 35가 모두 놓였다");
            }
        }
    }
}
