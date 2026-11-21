using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「운명 딜·손패」(S13, rulings A11·A12, README 결정 75 B1): 나눠 주기(귀환 · 떨어진 별 · 덱 바닥) · 손패 6장째 버리기 · 턴당 효과 1개 · 1장·3장 내기 · 운명 반응 창.
    /// 네 장의 효과는 다음 묶음이라, 내기 시험은 아무 일도 하지 않는 처리기를 등록한 표(<see cref="With"/>)로 돈다. 배신자 연전의 반환은 ActTests.
    /// </summary>
    public class FateTests
    {
        Ctx ctx;
        readonly List<string> calls = new List<string>();

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [SetUp]
        public void Reset() => calls.Clear();

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        static ChoiceAnswer PlayOf(FateCard card, int n, params int[] values)
        {
            var a = A(AnswerKind.PlayFate, (int)card, n);
            a.Values.AddRange(values); // 고른 것(A24 — 디아루가 주사위 자리 · 펄기아 칸)
            return a;
        }

        /// <summary>그 효과들의 처리기(부른 이름만 적는다)를 등록한 표. 기본 등록부의 운명 처리기(fate.*)는 뺀다(null = 뺀다 — EffectDb).</summary>
        Ctx With(params string[] handlers)
        {
            var table = NoFate();
            foreach (string name in handlers)
            {
                string n = name;
                table[n] = (s, c, fx, f) =>
                {
                    calls.Add(n);
                    return EffectStepResult.Done;
                };
            }
            return new Ctx(ctx.Board, ctx.Cards, ctx.Decks, ctx.Fixtures, ctx.Items, new EffectDb(new List<EffectDef>(ctx.Effects.DataEffects()), ctx.Cards.Moves.Count, table));
        }

        /// <summary>기본 등록부의 운명 처리기를 모두 빼는 표(값 null — EffectDb가 뺀다). 기술 처리기(MoveHandlers)는 데이터가 쓰니 남는다.</summary>
        internal static Dictionary<string, EffectHandler> NoFate()
        {
            var table = new Dictionary<string, EffectHandler>();
            foreach (string name in FateRules.Register(new Dictionary<string, EffectHandler>()).Keys) table[name] = null;
            return table;
        }

        int Key(FateCard c) => FateRules.CardKey(ctx, c);

        /// <summary>손패를 꼭 이것으로.</summary>
        void Hand(GameState s, int seat, params FateCard[] cards)
        {
            s.Seats[seat].FateHand.Clear();
            foreach (var c in cards) s.Seats[seat].FateHand.Add(Key(c));
        }

        static readonly FateCard[] Five = { FateCard.Dialga, FateCard.Dialga, FateCard.Palkia, FateCard.Giratina, FateCard.Arceus };

        /// <summary>차례 좌석이 턴을 넘긴다(다음 좌석의 턴 시작까지).</summary>
        static void EndTurn(GameState s, Ctx ctx, EventLog log = null) => Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);

        static int FateTotal(GameState s)
        {
            int n = s.Decks[(int)DeckId.ChangeOfFate].Count + s.Discards[(int)DeckId.ChangeOfFate].Count;
            foreach (var st in s.Seats) n += st.FateHand.Count;
            return n;
        }

        // ── 나눠 주기 ──

        [Test]
        public void S7_9_귀환한다음내턴시작에운명1장_남에게는장수만()
        {
            var s = Flow.Started(ctx, 2, 5);
            int next = 1 - s.ActiveSeat;
            s.Seats[next].RespawnPending = true;
            int top = Scene.Top(s, DeckId.ChangeOfFate, 1)[0];
            var log = new EventLog();
            EndTurn(s, ctx, log);
            CollectionAssert.AreEqual(new[] { top }, s.Seats[next].FateHand, "덱 맨 위 1장(p.9 「Draw 1 Change of Fate card when you start your next turn」)");
            Assert.AreEqual(42, FateTotal(s));
            var dealt = Flow.Of<FateDealt>(log);
            Assert.AreEqual(1, dealt.Count);
            Assert.IsTrue(dealt[0].Source.Same(SourceRef.Rule(RuleSource.Respawn)));
            Assert.AreEqual(-1, dealt[0].StarCount);
            Assert.AreEqual(next, dealt[0].PerSeat[0].Seat);
            Assert.AreEqual(0, dealt[0].PerSeat[0].Before);
            Assert.AreEqual(1, dealt[0].PerSeat[0].After);
            CollectionAssert.AreEqual(new[] { top }, dealt[0].PerSeat[0].CardKeys);
            var other = (FateDealt)Engine.FilterFor(dealt[0], 1 - next);
            Assert.IsNull(other.PerSeat[0].CardKeys, "남에게는 카드가 가려진다");
            Assert.AreEqual(1, other.PerSeat[0].After, "장수는 남는다");
            Assert.IsNotNull(((FateDealt)Engine.FilterFor(dealt[0], next)).PerSeat[0].CardKeys);
            Assert.Less(log.Events.IndexOf(Flow.Of<Healed>(log)[0]), log.Events.IndexOf(dealt[0]), "회복 뒤에 1장");
            Assert.AreEqual(next, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void S13_1_떨어진별4개마다턴시작에1장()
        {
            var s = Flow.Started(ctx, 2, 5);
            var log = new EventLog();
            s.FallenStars = 3;
            EndTurn(s, ctx, log);
            Assert.AreEqual(0, Flow.Of<FateDealt>(log).Count, "3개까지는 없다");
            s.FallenStars = 4;
            EndTurn(s, ctx, log);
            int seat = s.ActiveSeat;
            Assert.AreEqual(1, s.Seats[seat].FateHand.Count);
            var dealt = Flow.Of<FateDealt>(log)[0];
            Assert.IsTrue(dealt.Source.Same(SourceRef.Rule(RuleSource.FallenStar)));
            Assert.AreEqual(4, dealt.StarCount);
            Assert.AreEqual(0, s.Seats[1 - seat].FateHand.Count, "그 턴의 좌석만(p.16 「at the start of their turns」)");
            s.FallenStars = 8;
            EndTurn(s, ctx, log);
            Assert.AreEqual(2, s.Seats[1 - seat].FateHand.Count, "8개면 2장");
            Assert.AreEqual(42, FateTotal(s));
        }

        [Test]
        public void S13_3_덱이바닥나면버림더미를섞어뽑는다()
        {
            var s = Flow.Started(ctx, 2, 5);
            var deck = s.Decks[(int)DeckId.ChangeOfFate];
            var discards = s.Discards[(int)DeckId.ChangeOfFate];
            discards.AddRange(deck);
            deck.Clear();
            var log = new EventLog();
            FateRules.Deal(s, ctx, Scene.Fx(s, log), SourceRef.None, 0, 2);
            Assert.AreEqual(2, s.Seats[0].FateHand.Count);
            Assert.AreEqual(40, deck.Count, "버림 더미가 덱이 됐다(p.19)");
            Assert.AreEqual(0, discards.Count);
            Assert.AreEqual(1, Flow.Of<PileShuffled>(log).FindAll(e => e.Pile == DeckId.ChangeOfFate).Count);

            deck.Clear();
            FateRules.Deal(s, ctx, Scene.Fx(s, log), SourceRef.None, 1, 3);
            Assert.AreEqual(0, s.Seats[1].FateHand.Count, "덱도 버림 더미도 없으면 받지 못한다");
            var last = Flow.Of<FateDealt>(log);
            Assert.AreEqual(0, last[last.Count - 1].PerSeat[0].After);
        }

        // ── 손패 6장째(A12) ──

        [Test]
        public void A12_6장째는바로골라버려5장_새로받은카드도버릴수있다()
        {
            var s = Flow.Started(ctx, 2, 5);
            int next = 1 - s.ActiveSeat;
            Hand(s, next, Five);
            s.Seats[next].RespawnPending = true;
            int top = Scene.Top(s, DeckId.ChangeOfFate, 1)[0];
            var log = new EventLog();
            EndTurn(s, ctx, log);
            Assert.AreEqual(6, s.Seats[next].FateHand.Count);
            var discard = Flow.Only(s, ChoiceKind.Discard);
            Assert.AreEqual(next, discard.Seat);
            Assert.AreEqual(1, s.Held.Count, "맞추기 전에는 맵 행동이 열리지 않는다(A12)");
            Assert.AreEqual(0, Flow.Of<ChoiceRequested>(log).FindAll(e => e.Kind == ChoiceKind.MapAction && e.Seat == next).Count);
            var legal = Scene.Legal(s, ctx, discard);
            Assert.IsTrue(ChoiceRules.Contains(legal, ChoiceAnswer.Pick(top)), "새로 받은 카드도 버릴 수 있다(결정 75 B1 ①)");
            Assert.AreEqual(top, discard.Default.Target, "기본값 = 가장 나중에 받은 카드");
            Assert.IsFalse(Sinnoh.Rules.Session.SaveRest.IsRest(s), "버리는 중에는 스냅샷을 쓰지 않는다");

            log.Clear();
            int drop = Key(FateCard.Giratina);
            Flow.Answer(s, ctx, ChoiceKind.Discard, ChoiceAnswer.Pick(drop), log);
            Assert.AreEqual(5, s.Seats[next].FateHand.Count);
            var pile = s.Discards[(int)DeckId.ChangeOfFate];
            Assert.AreEqual(drop, pile[pile.Count - 1], "운명의 변화 버림 더미로");
            var ev = Flow.Of<FateDiscarded>(log)[0];
            Assert.AreEqual(drop, ev.CardKey);
            Assert.AreEqual(6, ev.Before);
            Assert.AreEqual(5, ev.After);
            Assert.AreEqual(next, ev.Seat);
            Assert.AreEqual(ActionKind.Discard, Flow.Of<ActionStarted>(log)[0].Kind);
            Assert.AreEqual(0, s.Held.Count);
            Assert.AreEqual(next, Flow.Only(s, ChoiceKind.MapAction).Seat, "버린 뒤에 맵 행동이 열린다");
            Assert.AreEqual(1, Flow.Of<ChoiceRequested>(log).FindAll(e => e.Kind == ChoiceKind.MapAction && e.Seat == next).Count);
        }

        [Test]
        public void A12_두장넘치면두번묻는다_시간초과는나중에받은카드()
        {
            var s = Flow.Started(ctx, 2, 5);
            int next = 1 - s.ActiveSeat;
            Hand(s, next, Five);
            s.FallenStars = 8;
            var tops = Scene.Top(s, DeckId.ChangeOfFate, 2);
            EndTurn(s, ctx);
            Assert.AreEqual(7, s.Seats[next].FateHand.Count);
            var first = Flow.Only(s, ChoiceKind.Discard);
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(first.Id), null));
            Assert.AreEqual(6, s.Seats[next].FateHand.Count);
            var second = Flow.Only(s, ChoiceKind.Discard);
            Assert.AreNotEqual(first.Id, second.Id);
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(second.Id), null));
            Assert.AreEqual(5, s.Seats[next].FateHand.Count);
            var pile = s.Discards[(int)DeckId.ChangeOfFate];
            CollectionAssert.AreEquivalent(tops, pile.GetRange(pile.Count - 2, 2), "시간 초과 = 가장 나중에 받은 카드부터");
            Assert.AreEqual(next, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void A12_남의턴이어도바로묻는다()
        {
            var s = Flow.Started(ctx, 3, 5);
            int me = s.ActiveSeat, other = (me + 1) % 3;
            Hand(s, other, Five);
            var log = new EventLog();
            FateRules.Deal(s, ctx, Scene.Fx(s, log, me, ActionKind.Reward), SourceRef.None, other, 1); // 남의 턴에 받는 운명(사당 카드 · 트레이너전 보상 …)
            Assert.AreEqual(2, s.Pending.Count);
            Assert.AreEqual(ChoiceKind.Discard, s.Pending[0].Kind, "버리기가 열린 선택의 맨 앞에 선다(결정 75 B1 ②)");
            Assert.AreEqual(other, s.Pending[0].Seat);
            Assert.AreEqual(ChoiceKind.MapAction, s.Pending[1].Kind, "이미 열려 있던 선택은 그대로");
            var mine = Engine.ViewFor(s, me);
            Assert.AreEqual(Engine.Unknown, mine.Pending[0].Default.Target, "남의 버리기 후보는 가려진다");
            Scene.Accept(Engine.Submit(s, ctx, other, Command.AnswerTo(s.Pending[0].Id, s.Pending[0].Default.Clone()), log));
            Assert.AreEqual(5, s.Seats[other].FateHand.Count);
            Assert.AreEqual(me, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void A12_턴끝옵션이면그좌석의턴끝에버린다()
        {
            var opt = new RuleOptions { FateHandOverflow = FateHandOverflow.DiscardAtTurnEnd };
            var s = Flow.Started(ctx, 2, 5, null, opt);
            int next = 1 - s.ActiveSeat;
            Hand(s, next, Five);
            s.Seats[next].RespawnPending = true;
            EndTurn(s, ctx);
            Assert.AreEqual(6, s.Seats[next].FateHand.Count);
            Assert.AreEqual(next, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴 동안에는 6장을 든다");
            EndTurn(s, ctx);
            var discard = Flow.Only(s, ChoiceKind.Discard);
            Assert.AreEqual(next, discard.Seat);
            Assert.AreEqual(1, s.Held.Count, "다음 좌석의 맵 행동은 버린 뒤에");
            Flow.Answer(s, ctx, ChoiceKind.Discard, discard.Default.Clone());
            Assert.AreEqual(5, s.Seats[next].FateHand.Count);
            Assert.AreEqual(1 - next, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void A12_미뤄둔선택은복제와해시에들고보기에서는비운다()
        {
            var s = Flow.Started(ctx, 2, 5);
            int next = 1 - s.ActiveSeat;
            Hand(s, next, Five);
            s.Seats[next].RespawnPending = true;
            EndTurn(s, ctx);
            var c = s.Clone();
            Assert.AreEqual(1, c.Held.Count);
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(c));
            c.Held.Clear();
            Assert.AreNotEqual(StateHash.Of(s), StateHash.Of(c));
            c.CopyFrom(s);
            c.FallenStars = 1;
            Assert.AreNotEqual(StateHash.Of(s), StateHash.Of(c));
            Assert.AreEqual(0, Engine.ViewFor(s, next).Held.Count);
        }

        // ── 턴당 효과 1개(A11) ──

        GameState TrainerBattle(Ctx cx, int activeSide, RuleOptions opt = null)
        {
            var s = Scene.Game(cx, 3, 11, opt);
            Scene.Give(s, cx, 0, "387:0");
            Scene.Give(s, cx, 1, "390:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.ActiveSide = activeSide;
            b.Step = BattleStep.PostRoll;
            b.Faces.AddRange(new[] { AttackDieFace.RegularHit, AttackDieFace.StatusSymbol }); // 디아루가가 고를 주사위 둘(A24)
            return s;
        }

        [Test]
        public void A11_배틀밖내턴1개_배틀턴마다1개_따로센다()
        {
            var cx = With("fate.palkia", "fate.dialga");
            var s = Scene.Game(cx, 3, 11);
            Hand(s, 0, FateCard.Palkia, FateCard.Palkia, FateCard.Dialga, FateCard.Dialga);
            Hand(s, 1, FateCard.Palkia);
            Assert.IsTrue(FateRules.CanUse(s, 0));
            Assert.IsFalse(FateRules.CanUse(s, 1), "배틀 밖 남의 턴에는 못 낸다");
            var log = new EventLog();
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, log, 0, ActionKind.PlayFate), 0, FateCard.Palkia, false));
            Assert.IsFalse(FateRules.CanUse(s, 0), "배틀 밖 내 턴에 1개");
            Assert.IsFalse(FateRules.Play(s, cx, Scene.Fx(s, log, 0, ActionKind.PlayFate), 0, FateCard.Palkia, false));
            Assert.AreEqual(3, s.Seats[0].FateHand.Count, "못 내면 카드를 쓰지 않는다");
            Assert.AreEqual(UsageKind.FateMap, Flow.Of<UsageMarked>(log)[0].Kind);

            // 같은 턴에 벌어진 내 배틀: 배틀 턴마다 1개, 맵의 1개와 따로(원작 커뮤니티 확인)
            Scene.Give(s, cx, 0, "387:0");
            Scene.Give(s, cx, 1, "390:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Assert.IsTrue(FateRules.CanUse(s, 0));
            Assert.IsTrue(FateRules.CanUse(s, 1), "남의 턴에 벌어진 내 배틀에서는 낼 수 있다");
            Assert.IsFalse(FateRules.CanUse(s, 2), "남의 배틀에는 못 낸다");
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, log, 0, ActionKind.Reaction), 0, FateCard.Dialga, false));
            Assert.IsFalse(FateRules.CanUse(s, 0), "그 배틀 턴에는 더 못 낸다");
            b.Turn++;
            Assert.IsTrue(FateRules.CanUse(s, 0), "다음 배틀 턴에는 다시");
        }

        [Test]
        public void A11_내턴전체1개옵션이면맵에서냈을때배틀에서못낸다()
        {
            var cx = With("fate.palkia", "fate.dialga");
            var s = Scene.Game(cx, 2, 11, new RuleOptions { FateLimitScope = FateLimitScope.WholeTurn });
            Hand(s, 0, FateCard.Palkia, FateCard.Dialga, FateCard.Dialga);
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, null, 0, ActionKind.PlayFate), 0, FateCard.Palkia, false));
            Scene.Give(s, cx, 0, "387:0");
            Scene.Give(s, cx, 1, "390:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Assert.IsFalse(FateRules.CanUse(s, 0));
            b.Turn++;
            Assert.IsFalse(FateRules.CanUse(s, 0), "배틀 턴이 바뀌어도 같은 맵 턴이면 못 낸다");
            s.TurnId++;
            Assert.IsTrue(FateRules.CanUse(s, 0));
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, null, 0, ActionKind.Reaction), 0, FateCard.Dialga, false));
            b.Turn++;
            Assert.IsFalse(FateRules.CanUse(s, 0));
        }

        // ── 내기 ──

        [Test]
        public void S13_2_맵행동으로1장내기_버림더미로_턴은이어지고그턴에는다시못낸다()
        {
            var cx = With("fate.palkia");
            var s = Flow.Started(cx, 2, 5);
            int seat = s.ActiveSeat;
            Hand(s, seat, FateCard.Palkia, FateCard.Palkia, FateCard.Dialga);
            var legal = Flow.MapLegal(s, cx);
            var palkia = legal.Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Palkia && a.Arg == 1);
            Assert.IsNotNull(palkia, "펄기아 1장(답마다 갈 칸 하나 — A24)");
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Arg == 3), "3장 효과의 처리기가 없다");
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga), "디아루가는 굴린 뒤 창에서");
            var log = new EventLog();
            Flow.Answer(s, cx, ChoiceKind.MapAction, palkia, log);
            CollectionAssert.AreEqual(new[] { "fate.palkia" }, calls);
            CollectionAssert.AreEqual(new[] { Key(FateCard.Palkia), Key(FateCard.Dialga) }, s.Seats[seat].FateHand);
            var pile = s.Discards[(int)DeckId.ChangeOfFate];
            Assert.AreEqual(Key(FateCard.Palkia), pile[pile.Count - 1], "낸 카드는 버린다(p.19)");
            Assert.AreEqual(ActionKind.PlayFate, Flow.Of<ActionStarted>(log)[0].Kind);
            var ev = Flow.Of<FatePlayed>(log)[0];
            Assert.AreEqual(seat, ev.Seat);
            Assert.AreEqual(FateCard.Palkia, ev.Card);
            Assert.IsFalse(ev.Play3);
            CollectionAssert.AreEqual(new[] { Key(FateCard.Palkia) }, ev.CardKeys);
            Assert.AreEqual(-1, ev.WildcardAs);
            Assert.AreEqual(3, ev.Before);
            Assert.AreEqual(2, ev.After);
            Assert.AreEqual(SourceKind.Fate, ev.Source.Kind);
            Assert.AreSame(ev, Engine.FilterFor(ev, 1 - seat), "낸 카드는 모두가 본다");
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴은 이어진다");
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, cx), AnswerKind.PlayFate), "이번 턴에는 더 못 낸다(A11)");
            EndTurn(s, cx);
            EndTurn(s, cx);
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, cx), AnswerKind.PlayFate), "다음 내 턴에는 다시");
        }

        [Test]
        public void S13_3_3장내기_아르세우스가대신하고추종자는1장으로센다()
        {
            var s = Scene.Game(ctx, 2, 11);
            var pay = new List<int>();
            int d = Key(FateCard.Dialga), a = Key(FateCard.Arceus);

            Hand(s, 0, FateCard.Dialga, FateCard.Dialga);
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay), "2장으로는 3장 효과를 못 낸다");
            Assert.IsTrue(FateRules.Payment(s, ctx, 0, FateCard.Dialga, false, pay));
            CollectionAssert.AreEqual(new[] { d }, pay);

            Hand(s, 0, FateCard.Dialga, FateCard.Dialga, FateCard.Dialga, FateCard.Arceus);
            Assert.IsTrue(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay));
            CollectionAssert.AreEqual(new[] { d, d, d }, pay, "같은 카드 3장 — 아르세우스는 아낀다");

            Hand(s, 0, FateCard.Dialga, FateCard.Arceus, FateCard.Arceus, FateCard.Palkia);
            Assert.IsTrue(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay));
            CollectionAssert.AreEqual(new[] { d, a, a }, pay, "아르세우스는 다른 카드의 3장 효과에 그 카드로 센다(p.19 Wildcard)");
            Assert.IsTrue(FateRules.Payment(s, ctx, 0, FateCard.Palkia, false, new List<int>()), "펄기아 1장은 낼 수 있다");
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Arceus, false, pay), "아르세우스는 1장 효과가 없다");
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Arceus, true, pay), "아르세우스 3장 효과는 아르세우스 3장");

            Hand(s, 0, FateCard.Dialga, FateCard.Dialga, FateCard.Giratina, FateCard.Giratina);
            s.Seats[0].Follower = SinnohGod.Dialga;
            Assert.IsTrue(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay));
            CollectionAssert.AreEqual(new[] { d, d }, pay, "디아루가 추종자는 2장이면 된다(p.19)");
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Giratina, true, pay), "추종자는 자기 신의 카드에만");
            // 그 신의 실제 카드가 1장은 있어야 한다: 추종자 + 아르세우스 2장으로는 안 된다(원작 커뮤니티 확인)
            Hand(s, 0, FateCard.Arceus, FateCard.Arceus, FateCard.Palkia);
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay), "디아루가 카드 0장");
            Hand(s, 0, FateCard.Arceus, FateCard.Arceus, FateCard.Arceus);
            s.Seats[0].Follower = SinnohGod.None;
            Assert.IsFalse(FateRules.Payment(s, ctx, 0, FateCard.Dialga, true, pay), "아르세우스 3장은 디아루가 3장 효과가 아니다");
            s.Seats[0].Follower = SinnohGod.Dialga;

            // 실제로 내기: 추종자 + 디아루가 1 + 아르세우스 1
            var cx = With("fate.dialga.play3");
            Scene.Give(s, cx, 0, "387:0");
            Scene.Give(s, cx, 1, "390:0");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Hand(s, 0, FateCard.Dialga, FateCard.Arceus, FateCard.Palkia);
            var log = new EventLog();
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, log, 0, ActionKind.Reaction), 0, FateCard.Dialga, true));
            CollectionAssert.AreEqual(new[] { Key(FateCard.Palkia) }, s.Seats[0].FateHand);
            var ev = Flow.Of<FatePlayed>(log)[0];
            Assert.IsTrue(ev.Play3);
            Assert.IsTrue(ev.FollowerCounted);
            Assert.AreEqual((int)FateCard.Dialga, ev.WildcardAs);
            CollectionAssert.AreEqual(new[] { d, a }, ev.CardKeys);
            Assert.AreEqual(UsageKind.FateBattle, Flow.Of<UsageMarked>(log)[0].Kind);
            CollectionAssert.AreEqual(new[] { "fate.dialga.play3" }, calls);
        }

        [Test]
        public void S13_3_3장효과에함께낼카드를고른다_첫답은같은카드부터()
        {
            // 결정 76 ② F1 나(§5 #81 ④): 손패 기라티나 2 · 아르세우스 2 · 펄기아 1로 기라티나 3장 — 기라티나 2 + 아르세우스 1(지금 자동 규칙 · 첫 답 · 봇과 시간 초과)
            // 또는 기라티나 1 + 아르세우스 2(답 Values 끝의 음수 = 아르세우스 장수, FateRules.WildPay). 그 신의 실제 카드 1장은 늘 낸다(원작 커뮤니티 확인).
            int g = Key(FateCard.Giratina), a = Key(FateCard.Arceus), p = Key(FateCard.Palkia);
            var pay = new List<int>();
            var cx = With("fate.giratina.play3");
            var s = TrainerBattle(cx, 0);
            Hand(s, 0, FateCard.Giratina, FateCard.Giratina, FateCard.Arceus, FateCard.Arceus, FateCard.Palkia);
            Hand(s, 1);
            Assert.IsTrue(FateRules.Payment(s, cx, 0, FateCard.Giratina, true, pay));
            CollectionAssert.AreEqual(new[] { g, g, a }, pay, "고르지 않으면 같은 카드부터");
            Assert.IsTrue(FateRules.Payment(s, cx, 0, FateCard.Giratina, true, pay, 2));
            CollectionAssert.AreEqual(new[] { g, a, a }, pay);
            Assert.IsFalse(FateRules.Payment(s, cx, 0, FateCard.Giratina, true, pay, 0), "기라티나 2장으로는 모자란다");
            Assert.IsFalse(FateRules.Payment(s, cx, 0, FateCard.Giratina, true, pay, 3), "그 신의 카드가 1장은 있어야 한다");

            var log = new EventLog();
            Assert.IsTrue(Reactions.Open(s, cx, Scene.Fx(s, log, 0, ActionKind.BattleAction), Window.BeforeRoll));
            var c = Flow.Only(s, ChoiceKind.Reaction);
            var legal = Scene.Legal(s, cx, c);
            Assert.AreEqual(3, legal.Count, "안 씀 · 자동 나눔 · 아르세우스 2장");
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind);
            Assert.IsTrue(legal[1].SameAs(PlayOf(FateCard.Giratina, 3)), "자동 나눔(같은 카드부터)이 먼저");
            Assert.IsTrue(legal[2].SameAs(PlayOf(FateCard.Giratina, 3, FateRules.WildPay(2))));
            Scene.Accept(Engine.Submit(s, cx, 0, Command.AnswerTo(c.Id, legal[2]), log));
            var ev = Flow.Of<FatePlayed>(log)[0];
            CollectionAssert.AreEqual(new[] { g, a, a }, ev.CardKeys);
            Assert.AreEqual((int)FateCard.Giratina, ev.WildcardAs);
            CollectionAssert.AreEquivalent(new[] { g, p }, s.Seats[0].FateHand, "기라티나 1장을 남겼다");
            CollectionAssert.AreEqual(new[] { "fate.giratina.play3" }, calls);

            // 같은 카드로 다 채우면 아르세우스를 섞어 내는 답도 나온다(기라티나 3 · 아르세우스 1 → 0장 또는 1장), 추종자가 1장을 세면 그만큼 줄어든다
            var t = TrainerBattle(cx, 0);
            Hand(t, 0, FateCard.Giratina, FateCard.Giratina, FateCard.Giratina, FateCard.Arceus);
            Hand(t, 1);
            Assert.IsTrue(Reactions.Open(t, cx, Scene.Fx(t, null, 0, ActionKind.BattleAction), Window.BeforeRoll));
            var l2 = Scene.Legal(t, cx, Flow.Only(t, ChoiceKind.Reaction));
            Assert.AreEqual(3, l2.Count);
            Assert.IsTrue(l2[1].SameAs(PlayOf(FateCard.Giratina, 3)));
            Assert.IsTrue(l2[2].SameAs(PlayOf(FateCard.Giratina, 3, FateRules.WildPay(1))));
            t.Seats[0].Follower = SinnohGod.Giratina;
            Assert.IsTrue(FateRules.Payment(t, cx, 0, FateCard.Giratina, true, pay, 1));
            CollectionAssert.AreEqual(new[] { g, a }, pay, "추종자 + 기라티나 1 + 아르세우스 1");
        }

        [Test]
        public void S13_2_효과가아직없는카드는합법답에나오지않는다()
        {
            var ctx = With(); // 처리기가 하나도 없는 표(네 장의 효과는 「운명 네 장 효과」가 BuiltIn에 등록했다 — FateEffectTests)
            var s = Flow.Started(ctx, 2, 5);
            int seat = s.ActiveSeat;
            Hand(s, seat, FateCard.Dialga, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia, FateCard.Arceus);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.PlayFate));
            foreach (FateCard c in System.Enum.GetValues(typeof(FateCard)))
            {
                Assert.IsFalse(FateRules.Ready(ctx, c, false), c.ToString());
                Assert.IsFalse(FateRules.Ready(ctx, c, true), c.ToString());
            }
            Assert.IsFalse(FateRules.Play(s, ctx, Scene.Fx(s, null, seat, ActionKind.PlayFate), seat, FateCard.Palkia, false));
            Assert.AreEqual(5, s.Seats[seat].FateHand.Count);

            var b = TrainerBattle(ctx, 0);
            Hand(b, 0, FateCard.Dialga, FateCard.Giratina);
            var c0 = Scene.Open(b, ctx, null, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.Fate, Seat = 0, Window = Window.AfterRoll, HandGated = true });
            var only = Scene.Legal(b, ctx, c0);
            Assert.AreEqual(1, only.Count, "운명 창의 답은 「안 씀」뿐");
            Assert.AreEqual(AnswerKind.Decline, only[0].Kind);
        }

        // ── 운명 반응 창 ──

        [Test]
        public void S13_2_반응창_기라티나는굴리기전내공격에_디아루가는굴린뒤에()
        {
            var cx = With("fate.dialga", "fate.giratina", "fate.palkia");
            var s = TrainerBattle(cx, 0);
            Hand(s, 0, FateCard.Dialga, FateCard.Giratina, FateCard.Palkia);
            Hand(s, 1, FateCard.Dialga, FateCard.Giratina);
            List<ChoiceAnswer> At(int seat, Window w)
            {
                var c = Scene.Open(s, cx, null, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.Fate, Seat = seat, Window = w, HandGated = true });
                var legal = Scene.Legal(s, cx, c);
                s.Pending.Remove(c);
                return legal;
            }
            var before0 = At(0, Window.BeforeRoll);
            Assert.AreEqual(2, before0.Count);
            Assert.AreEqual(AnswerKind.Decline, before0[0].Kind, "첫 답 = 안 씀(G9)");
            Assert.IsTrue(before0[1].SameAs(PlayOf(FateCard.Giratina, 1)));
            Assert.AreEqual(1, At(1, Window.BeforeRoll).Count, "기라티나는 내가 공격할 때만(p.19 「Before attacking」)");
            var after0 = At(0, Window.AfterRoll);
            Assert.AreEqual(1 + 2, after0.Count, "안 씀 + 주사위 둘 가운데 하나(A24)");
            Assert.IsTrue(after0[1].SameAs(PlayOf(FateCard.Dialga, 1, 0)), "펄기아는 창에 나오지 않는다");
            Assert.IsTrue(At(1, Window.AfterRoll)[1].SameAs(PlayOf(FateCard.Dialga, 1, 0)), "상대 굴림에도 디아루가(A11, FateOnOpponentBattleTurn)");
        }

        [Test]
        public void A11_반응창에서낸좌석은그배틀턴에다시묻지않는다()
        {
            var cx = With("fate.dialga");
            var s = TrainerBattle(cx, 0);
            Hand(s, 0, FateCard.Dialga, FateCard.Dialga);
            Hand(s, 1, FateCard.Dialga);
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
            Assert.IsTrue(Reactions.Open(s, cx, fx, Window.AfterRoll));
            Assert.AreEqual(2, s.Pending.Count, "두 좌석에 한꺼번에(G6)");
            var mine = s.Pending.Find(c => c.Seat == 0);
            var theirs = s.Pending.Find(c => c.Seat == 1);
            Scene.Accept(Engine.Submit(s, cx, 1, Command.AnswerTo(theirs.Id, A(AnswerKind.Decline)), log));
            Scene.Accept(Engine.Submit(s, cx, 0, Command.AnswerTo(mine.Id, PlayOf(FateCard.Dialga, 1, 0)), log));
            CollectionAssert.AreEqual(new[] { "fate.dialga" }, calls);
            Assert.AreEqual(1, Flow.Of<FatePlayed>(log).Count);
            Assert.AreEqual(1, s.Seats[0].FateHand.Count);
            // 누가 썼으니 한 바퀴 더(G6): 이번 배틀 턴에 이미 낸 좌석 0은 빼고 좌석 1만 다시 묻는다
            var again = Flow.Only(s, ChoiceKind.Reaction);
            Assert.AreEqual(1, again.Seat);
            Assert.AreEqual(2, s.Windows[0].Round);
            Scene.Accept(Engine.Submit(s, cx, 1, Command.AnswerTo(again.Id, PlayOf(FateCard.Dialga, 1, 1)), log));
            Assert.AreEqual(2, Flow.Of<FatePlayed>(log).Count, "상대도 자기 한도로 1개");
            Assert.AreEqual(0, s.Windows.Count, "둘 다 냈으면 더 물을 좌석이 없어 창이 닫힌다");
            Assert.AreEqual(0, s.Pending.Count);
        }
    }
}
