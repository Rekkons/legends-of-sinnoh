using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-c 반응 창(01 §3-3·§7-3, rulings G5·G6): 공개 정보로 열기·건너뛰기, 한꺼번에/차례로 묻기, 적용 순서, 닫힌 뒤 원래 행동 잇기.
    /// 운명 카드 효과(M5)가 없어 운명 창의 Legal은 「안 씀」뿐이다. 「반응을 쓰면 한 바퀴 더」는 둘 이상이 쓸 수 있는 반응이 생기는 M5에서 시험한다.
    /// </summary>
    public class ReactionTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        /// <summary>트레이너전: 좌석 0 대 좌석 1, 배틀 턴은 activeSide, 두 좌석 모두 운명 카드 handN장.</summary>
        GameState TrainerBattle(int activeSide, int hand0, int hand1, RuleOptions opt = null)
        {
            var s = Scene.Game(ctx, 3, 11, opt);
            Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 1, "390:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.ActiveSide = activeSide;
            b.Step = BattleStep.PostRoll;
            s.Seats[0].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, hand0));
            s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, hand1));
            return s;
        }

        [Test]
        public void M3_쓸반응이없으면창건너뜀()
        {
            var s = TrainerBattle(0, 0, 0);
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
            int seq = s.NextSeq;
            Assert.IsFalse(Reactions.Open(s, ctx, fx, Window.AfterRoll));
            Assert.AreEqual(0, s.Pending.Count);
            Assert.AreEqual(0, s.Windows.Count);
            Assert.AreEqual(seq, s.NextSeq, "건너뛰면 새 Seq가 없다");
            var skip = log.OfType<WindowSkipped>();
            Assert.AreEqual(1, skip.Count);
            Assert.AreEqual(Window.AfterRoll, skip[0].Window);
            Assert.AreEqual(fx.Seq, skip[0].Seq);

            // 갤럭시단 배틀이 아니면 응징 창도 없다
            Assert.IsFalse(Reactions.Open(s, ctx, fx, Window.BeforeDamage));
        }

        [Test]
        public void G6_한꺼번에묻고공격쪽부터적용()
        {
            var s = TrainerBattle(1, 2, 1); // 배틀 턴 = 좌석 1(Side 1)
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 1, ActionKind.BattleAction);
            int attack = fx.Seq;
            Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.AfterRoll));
            Assert.AreEqual(2, s.Pending.Count, "한 묶음으로 한꺼번에(G6)");
            var first = s.Pending[0];
            var second = s.Pending[1];
            Assert.AreEqual(1, first.Seat, "공격 쪽이 먼저");
            Assert.AreEqual(0, second.Seat);
            Assert.AreEqual(first.GroupId, second.GroupId);
            foreach (var c in s.Pending)
            {
                Assert.AreEqual(ChoiceKind.Reaction, c.Kind);
                Assert.AreEqual(ChoiceSub.Fate, c.Sub);
                Assert.IsTrue(c.HandGated, "운명 장수로 연 창(01 §3-3)");
                Assert.IsTrue(c.Secret);
                Assert.AreEqual(ClockKind.Reaction, c.Clock);
                Assert.AreEqual(attack, c.FromSeq);
                Assert.AreEqual(1, Scene.Legal(s, ctx, c).Count, "쓸 카드 효과가 아직 없다(M5) → 「안 씀」 하나");
            }

            // 뒤 좌석이 먼저 답해도 공개·적용은 G6 순서
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(second.Id, ChoiceAnswer.Of(AnswerKind.Decline)), log));
            var r0 = log.OfType<ActionStarted>();
            Assert.AreEqual(ActionKind.Reaction, r0[r0.Count - 1].Kind);
            Assert.AreEqual(attack, r0[r0.Count - 1].ReactsTo, "반응 답은 새 Seq, ReactsTo = 공격(01 §3-6 ①-2)");
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(first.Id), log));
            var rev = log.OfType<SecretRevealed>();
            Assert.AreEqual(1, rev.Count);
            Assert.AreEqual(1, rev[0].Answers[0].Seat, "공격 쪽 → 다른 쪽");
            Assert.AreEqual(0, rev[0].Answers[1].Seat);
            Assert.AreEqual(0, s.Windows.Count, "아무도 안 쓰면 닫힌다");
            var starts = log.OfType<ActionStarted>();
            var cont = starts[starts.Count - 1];
            Assert.AreEqual(ActionKind.BattleAction, cont.Kind, "창이 닫히면 원래 행동이 새 Seq로 이어진다(01 §3-6 ① (c))");
            Assert.AreEqual(attack, cont.Continues);
            Assert.AreEqual(1, cont.Seat);
        }

        [Test]
        public void M3_반응창묶음은연출이없다_전원안씀이면길이0()
        {
            // 두 좌석 모두 운명 1장인 굴린 뒤 창: 비공개 묶음의 답(SecretAnswered)과 공개(SecretRevealed)는 창의 일부(04 58행 none)라 좌석마다 길이 0.
            // 전에는 1행 「고른 뒤」·99행 공개로 세어 공격마다 「안 씀」 연출로 약 5.5초씩 멈췄다(검토). 쓴 반응은 자기 Seq의 결과 이벤트가 연출을 맡는다.
            var s = TrainerBattle(1, 1, 1);
            var fx = Scene.Fx(s, new EventLog(), 1, ActionKind.BattleAction);
            Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.AfterRoll));
            Assert.AreEqual(2, s.Pending.Count);
            var ctxs = new PlayTimingContext[s.SeatCount];
            for (int i = 0; i < ctxs.Length; i++) ctxs[i] = new PlayTimingContext(i);
            var per = new int[s.SeatCount];
            int answered = 0, revealed = 0;
            foreach (var c in new List<PendingChoice>(s.Pending))
            {
                var update = new EventLog();
                Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Decline)), update));
                Assert.AreEqual(0, PlayTiming.UpdateMs(update.Events, ctxs, per), "전원 안 씀 반응 바퀴의 방장 멈춤 0");
                CollectionAssert.AreEqual(new int[s.SeatCount], per, "좌석마다 길이 0");
                foreach (var e in update.Events)
                {
                    if (e is SecretAnswered sa)
                    {
                        answered++;
                        Assert.AreEqual(Window.AfterRoll, sa.Window);
                    }
                    else if (e is SecretRevealed sr)
                    {
                        revealed++;
                        Assert.AreEqual(Window.AfterRoll, sr.Window);
                    }
                }
            }
            Assert.AreEqual(2, answered);
            Assert.AreEqual(1, revealed);
            Assert.AreEqual(0, s.Windows.Count);
            // 창 밖의 동시 선택(셋업 고르기·천상의 균열)은 그대로 1·99행
            Assert.AreEqual(11, PlayTiming.RowOf(new SecretAnswered { Window = Window.None }));
            Assert.AreEqual(990, PlayTiming.RowOf(new SecretRevealed { Window = Window.None }));
        }

        [Test]
        public void G6_차례로묻기면앞결과를보고답함()
        {
            var s = TrainerBattle(0, 1, 1, new RuleOptions { ReactionAsk = ReactionAsk.Sequential });
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
            Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.BeforeRoll));
            Assert.AreEqual(1, s.Pending.Count, "한 좌석씩");
            var c0 = s.Pending[0];
            Assert.AreEqual(0, c0.Seat, "공격 쪽부터");
            Assert.IsFalse(c0.Secret);
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(c0.Id, ChoiceAnswer.Of(AnswerKind.Decline)), log));
            Assert.AreEqual(1, log.OfType<ChoiceAnswered>().Count, "앞 좌석의 답이 공개된 뒤");
            Assert.AreEqual(1, s.Pending.Count);
            Assert.AreEqual(1, s.Pending[0].Seat, "다음 좌석을 묻는다");
            Assert.AreEqual(1, s.Windows.Count);
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(s.Pending[0].Id, ChoiceAnswer.Of(AnswerKind.Decline)), log));
            Assert.AreEqual(0, s.Pending.Count);
            Assert.AreEqual(0, s.Windows.Count);
            Assert.AreEqual(0, log.OfType<SecretRevealed>().Count);
        }

        [Test]
        public void M3_운명창은장수로열고손패와무관()
        {
            // 상대 배틀 턴에도(FateOnOpponentBattleTurn 기본 켬) 열고, 끄면 배틀 턴 쪽만
            var s = TrainerBattle(0, 1, 1, new RuleOptions { FateOnOpponentBattleTurn = false });
            var fx = Scene.Fx(s, null, 0, ActionKind.BattleAction);
            var cands = new List<ReactionCandidate>();
            Reactions.Candidates(s, ctx, Window.AfterRoll, cands);
            Assert.AreEqual(1, cands.Count);
            Assert.AreEqual(0, cands[0].Seat);

            // 이번 배틀 차례에 운명을 이미 썼으면 열지 않는다(A11 확정, 원작 커뮤니티 확인,
            // 원작 커뮤니티 확인)
            var t = TrainerBattle(0, 1, 1);
            t.Used.Add(new UsageMark { Seat = 1, Kind = UsageKind.FateBattle, Scope = t.Battle.Turn });
            Reactions.Candidates(t, ctx, Window.AfterRoll, cands);
            Assert.AreEqual(1, cands.Count);
            Assert.AreEqual(0, cands[0].Seat);

            // 손패 내용이 달라도(같은 장수) 창 모양이 같다
            var a = TrainerBattle(0, 2, 2);
            var b = TrainerBattle(0, 2, 2);
            var kinds = ctx.Deck(DeckId.ChangeOfFate).Cards;
            a.Seats[1].FateHand.Clear();
            a.Seats[1].FateHand.AddRange(new[] { kinds[0].Card, kinds[0].Card });
            b.Seats[1].FateHand.Clear();
            b.Seats[1].FateHand.AddRange(new[] { kinds[1].Card, kinds[kinds.Count - 1].Card });
            var la = new EventLog();
            var lb = new EventLog();
            Reactions.Open(a, ctx, Scene.Fx(a, la, 0, ActionKind.BattleAction), Window.AfterRoll);
            Reactions.Open(b, ctx, Scene.Fx(b, lb, 0, ActionKind.BattleAction), Window.AfterRoll);
            Assert.AreEqual(StateHash.Of(Engine.ViewFor(a, 0)), StateHash.Of(Engine.ViewFor(b, 0)));
            Assert.AreEqual(EventDump.Of(la.Events, 0), EventDump.Of(lb.Events, 0));
        }

        [Test]
        public void M3_응징반응은공격한포켓몬에게()
        {
            // G5 BeforeDamage(2026-11-14 전의 기본 — 기본은 턴 끝, 원작 커뮤니티 확인): 공격받는 갤럭시단 쪽이 저장량 전부를 공격한 포켓몬에게 옮긴다. 피해 버퍼로(적용은 배틀 단계)
            var s = Scene.Game(ctx, 2, 7, new RuleOptions { GalacticRetributionTiming = GalacticRetributionTiming.BeforeDamage });
            int mine = Scene.Give(s, ctx, 0, "387:0");
            int grunt = Scene.Spawn(s, ctx, "399:0", SeatId.Galactic);
            var b = Scene.Battle(s, BattleKind.Galactic, 0, SeatId.Galactic, grunt);
            b.Step = BattleStep.PreDamage;
            b.Sides[1].Retribution = 2;
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 0, ActionKind.BattleAction);
            int attack = fx.Seq;
            Assert.IsFalse(Reactions.Open(s, ctx, fx, Window.AfterDamage), "AfterFaint 창은 옵션을 바꿨을 때만");
            Assert.IsTrue(Reactions.Open(s, ctx, fx, Window.BeforeDamage));
            var c = s.Pending[0];
            Assert.AreEqual(SeatId.Galactic, c.Seat, "가상 응답자(01 §3-3)");
            Assert.AreEqual(ChoiceSub.Retribution, c.Sub);
            Assert.IsFalse(c.HandGated);
            Assert.AreEqual(mine, c.Subject);
            var req = log.OfType<ChoiceRequested>()[0];
            Assert.IsNotNull(Engine.FilterFor(req, 0) is ChoiceRequested r0 ? r0.Legal : null, "가상 응답자 선택의 Legal은 모두에게");

            Scene.Accept(Engine.Submit(s, ctx, SeatId.Galactic, Command.AiAnswer(c.Id, ChoiceAnswer.Of(AnswerKind.Accept), new AiReason(AiRule.PlanB, 3, 1)), log));
            Assert.AreEqual(0, b.Sides[1].Retribution);
            CollectionAssert.AreEqual(new[] { mine, -2, (int)SourceKind.Rule, (int)RuleSource.GalacticRetribution }, b.DamageBuffer, "(포켓몬, 체력 증감, 출처)");
            var moved = log.OfType<RetributionMoved>();
            Assert.AreEqual(1, moved.Count);
            Assert.AreEqual(1, moved[0].FromSide);
            Assert.AreEqual(mine, moved[0].ToPokemonId);
            Assert.AreEqual(2, moved[0].Amount);
            Assert.AreEqual(SourceKind.Rule, moved[0].Source.Kind);
            var starts = log.OfType<ActionStarted>();
            Assert.AreEqual(attack, starts[1].ReactsTo);
            Assert.AreEqual(moved[0].Seq, starts[1].Seq, "쓴 반응은 그 반응의 Seq");
            Assert.AreEqual(attack, starts[2].Continues, "다 쓰면(저장 0) 창이 닫히고 공격이 이어진다");
            Assert.AreEqual(0, s.Windows.Count);

            // 옵션 AfterFaint면 AfterDamage 창에서 묻는다
            var t = Scene.Game(ctx, 2, 7, new RuleOptions { GalacticRetributionTiming = GalacticRetributionTiming.AfterFaint });
            Scene.Give(t, ctx, 0, "387:0");
            var tb = Scene.Battle(t, BattleKind.Galactic, 0, SeatId.Galactic, Scene.Spawn(t, ctx, "399:0", SeatId.Galactic));
            tb.Sides[1].Retribution = 2;
            var tfx = Scene.Fx(t, null, 0, ActionKind.BattleAction);
            Assert.IsFalse(Reactions.Open(t, ctx, tfx, Window.BeforeDamage));
            Assert.IsTrue(Reactions.Open(t, ctx, tfx, Window.AfterDamage));

            // 기본(TurnEnd)이면 공격한 쪽 턴 끝(BattleTurnEnd) 창에서만 묻는다(원작 커뮤니티 확인)
            var u = Scene.Game(ctx, 2);
            Scene.Give(u, ctx, 0, "387:0");
            var ub = Scene.Battle(u, BattleKind.Galactic, 0, SeatId.Galactic, Scene.Spawn(u, ctx, "399:0", SeatId.Galactic));
            ub.Sides[1].Retribution = 2;
            var ufx = Scene.Fx(u, null, 0, ActionKind.BattleAction);
            Assert.IsFalse(Reactions.Open(u, ctx, ufx, Window.BeforeDamage));
            Assert.IsFalse(Reactions.Open(u, ctx, ufx, Window.AfterDamage));
            Assert.IsTrue(Reactions.Open(u, ctx, ufx, Window.BattleTurnEnd));
        }

        [Test]
        public void M3_응징기본값은안옮김()
        {
            var s = Scene.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "387:0");
            var b = Scene.Battle(s, BattleKind.Galactic, 0, SeatId.Galactic, Scene.Spawn(s, ctx, "399:0", SeatId.Galactic));
            b.Sides[1].Retribution = 4;
            var log = new EventLog();
            Reactions.Open(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), Window.BattleTurnEnd);
            var c = s.Pending[0];
            Assert.AreEqual(AnswerKind.Decline, c.Default.Kind, "Reaction Default = 안 씀(01 §3-3)");
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log));
            Assert.AreEqual(4, b.Sides[1].Retribution);
            Assert.AreEqual(0, b.DamageBuffer.Count);
            Assert.AreEqual(0, log.OfType<RetributionMoved>().Count);
            Assert.AreEqual(0, s.Windows.Count);
        }
    }
}
