using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-g: p.25 원칙 AI(01 §9-6, rulings A16·G15)와 PrincipleAgent(Sinnoh.Ai). AI는 보기 사본만 받는다.
    /// A16 확정(원작 커뮤니티 확인): 야생 전설·우두머리 기술은 원작 커뮤니티 확인.
    /// </summary>
    public class AiTests
    {
        Ctx ctx;
        readonly PrincipleAgent agent = new PrincipleAgent();

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        AgentDecision Decide(GameState host, int choiceId, int viewer = SeatId.Observer)
        {
            var view = Engine.ViewFor(host, viewer);
            var c = view.Pending.Find(x => x.Id == choiceId);
            Assert.IsNotNull(c, $"보기에 선택 {choiceId}가 없다");
            return agent.Decide(new AgentInput { View = view, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget());
        }

        [Test]
        public void G15_원칙값은효과피해를기대값으로더한다()
        {
            var s = Scene.Game(ctx, 1);
            var me = s.Pokemon[Scene.Give(s, ctx, 0, "399:0")];
            var water = s.Pokemon[Scene.Spawn(s, ctx, "418:0", SeatId.Wild)];
            long S = PrincipleRules.Scale;

            // 효과 없는 값 = 공격력 + 상성(눈 합 기대값 = 공격력, 01 §6-3)
            int hex = Scene.Move(ctx, "hex"); // 고스트 3, 대상이 상태이상이면 2배
            Assert.AreEqual(3 * S, PrincipleRules.Value(ctx, hex, me, water));
            water.Status = StatusEffect.Poisoned;
            Assert.AreEqual(6 * S, PrincipleRules.Value(ctx, hex, me, water), "2배는 대상 조건이 지금 맞을 때");
            water.Status = StatusEffect.None;

            // 크리마다 +1: 주사위 4개 × 1/6 = 2/3 → 정확히 분모 6^9
            Assert.AreEqual((4 * 6 + 4) * S / 6, PrincipleRules.Value(ctx, Scene.Move(ctx, "x_scissor"), me, water));
            // 크리 +1·심볼 −1은 기대값에서 상쇄
            Assert.AreEqual(5 * S, PrincipleRules.Value(ctx, Scene.Move(ctx, "megahorn"), me, water));

            // 「공격력 = n」(G14): 전기 1 → 마비 대상이면 3, 물에 상성 +2
            int nuzzle = Scene.Move(ctx, "nuzzle");
            Assert.AreEqual(1, PrincipleRules.Strength(ctx, nuzzle, me, water));
            Assert.AreEqual(3 * S, PrincipleRules.Value(ctx, nuzzle, me, water));
            water.Status = StatusEffect.Paralysed;
            Assert.AreEqual(3, PrincipleRules.Strength(ctx, nuzzle, me, water));
            Assert.AreEqual(5 * S, PrincipleRules.Value(ctx, nuzzle, me, water));

            // 대상 타입 조건 추가 피해: 무릎차기 격투 5, 비행 타입이면 +4(노말 +2, 비행 −2 → 상성 0)
            int hjk = Scene.Move(ctx, "high_jump_kick");
            var bird = s.Pokemon[Scene.Spawn(s, ctx, "396:0", SeatId.Wild)];
            Assert.AreEqual(5 * S, PrincipleRules.Value(ctx, hjk, me, water));
            Assert.AreEqual(9 * S, PrincipleRules.Value(ctx, hjk, me, bird));
        }

        [Test]
        public void G15_시그니처는효과가들면쓴다()
        {
            // 불꽃숭이: 시그니처 불꽃세례(불 2, 화상) + 화염방사(불 4). 물 상대에 값 0 < 2지만 화상이 들면 시그니처(결정 29 ②)
            var s = Scene.Game(ctx, 1);
            var me = s.Pokemon[Scene.Give(s, ctx, 0, "390:0")];
            int ember = ctx.Cards.Pokemon[me.Def].Signature;
            int flamethrower = Scene.Move(ctx, "flamethrower");
            me.Moves[0] = flamethrower;
            var water = s.Pokemon[Scene.Spawn(s, ctx, "418:0", SeatId.Wild)];

            Assert.AreEqual(ember, PrincipleRules.Move(ctx, me, water, out var why));
            Assert.AreEqual(AiRule.Signature, why.Rule);
            Assert.AreEqual(2, why.Power);
            Assert.AreEqual(-2, why.Bonus, "불 → 물 −2");

            water.Status = StatusEffect.Burned; // 이미 걸림 → 효과가 안 든다
            Assert.AreEqual(flamethrower, PrincipleRules.Move(ctx, me, water, out why));
            Assert.AreEqual(AiRule.BestValue, why.Rule);
            Assert.AreEqual(4, why.Power);

            var fire = s.Pokemon[Scene.Spawn(s, ctx, "390:0", SeatId.Wild)]; // 불꽃 타입은 화상 면역
            Assert.AreEqual(flamethrower, PrincipleRules.Move(ctx, me, fire, out _));
        }

        [Test]
        public void G15_원칙AI다음포켓몬은상성이가장좋은것()
        {
            // 갤럭시단 명단 [노말, 풀] 대 내 물 포켓몬 → 풀(Default = 명단 첫째와 다름)
            var s = Battles.Game(ctx, 1);
            Scene.Give(s, ctx, 0, "418:0");
            int normal = Scene.Spawn(s, ctx, "399:0", SeatId.Galactic);
            int grass = Scene.Spawn(s, ctx, "387:0", SeatId.Galactic);
            Battles.Start(s, ctx, null, BattleKind.Galactic, 0, SeatId.Galactic, -1, normal, grass);
            var c = Battles.Only(s, ChoiceKind.SendIn);
            Assert.AreEqual(SeatId.Galactic, c.Seat);
            Assert.AreEqual(normal, c.Default.Target, "Default는 명단 순서(01 §3-3)");
            var d = Decide(s, c.Id);
            Assert.AreEqual(ChoiceAnswer.Pick(grass).ToString(), d.Answer.ToString());

            // 기술: 보기에서 고른 답 = 원칙 기술, 사유가 채워진다(ChoiceAnswered.Reason, 04 73행)
            Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AiAnswer(c.Id, d.Answer, d.Reason), null));
            var turn = s.Pending.Find(x => x.Kind == ChoiceKind.BattleAction && x.Seat == SeatId.Galactic);
            if (turn == null)
            {
                // 내 차례가 먼저면 내 기술(Default)로 한 번 돌린다
                var mine = Battles.Only(s, ChoiceKind.BattleAction);
                Scene.Accept(Engine.Submit(s, ctx, mine.Seat, Command.AnswerTo(mine.Id, mine.Default.Clone()), null));
                turn = s.Pending.Find(x => x.Kind == ChoiceKind.BattleAction && x.Seat == SeatId.Galactic);
            }
            Assert.IsNotNull(turn, "갤럭시단 기술 선택이 열리지 않았다");
            var ai = Decide(s, turn.Id);
            int want = PrincipleRules.Move(ctx, s.Pokemon[grass], s.Pokemon[s.Battle.Sides[0].Active], out var why);
            Assert.AreEqual(ChoiceAnswer.Of(AnswerKind.UseMove, want).ToString(), ai.Answer.ToString());
            Assert.AreEqual(why.Rule, ai.Reason.Rule);
            Assert.AreEqual(why.Power, ai.Reason.Power);
        }

        [Test]
        public void G15_원칙AI는우선권토큰을쓰고응징을옮긴다()
        {
            var s = Scene.Game(ctx, 1);
            Scene.Give(s, ctx, 0, "418:0");
            int g1 = Scene.Spawn(s, ctx, "399:0", SeatId.Galactic);
            int g2 = Scene.Spawn(s, ctx, "387:0", SeatId.Galactic);
            Scene.Battle(s, BattleKind.Galactic, 0, SeatId.Galactic, g1, g2);
            var token = Scene.Open(s, ctx, null, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.InitiativeToken, Seat = SeatId.Galactic, Subject = 1 });
            var retribution = Scene.Open(s, ctx, null, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.Retribution, Seat = SeatId.Galactic });
            var sw = Scene.Open(s, ctx, null, new PendingChoice { Kind = ChoiceKind.SwitchWindow, Seat = SeatId.Galactic });
            Assert.AreEqual(AnswerKind.Decline, token.Default.Kind, "Default는 안 씀(01 §3-3)");
            Assert.AreEqual(AnswerKind.Accept, Decide(s, token.Id).Answer.Kind);
            Assert.AreEqual(AnswerKind.Accept, Decide(s, retribution.Id).Answer.Kind);
            Assert.AreEqual(AnswerKind.Decline, Decide(s, sw.Id).Answer.Kind, "교체 창은 교체 안 함");
        }

        [Test]
        public void M3_AI는호스트원본을받지않는다()
        {
            var s = Scene.Game(ctx, 1);
            Scene.Give(s, ctx, 0, "418:0");
            int g = Scene.Spawn(s, ctx, "399:0", SeatId.Galactic);
            Scene.Battle(s, BattleKind.Galactic, 0, SeatId.Galactic, g);
            var c = Scene.Open(s, ctx, null, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.Retribution, Seat = SeatId.Galactic });
            var input = new AgentInput { View = s, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() };
            Assert.Throws<InvalidOperationException>(() => agent.Decide(input, new AgentBudget()), "호스트 원본(Viewer −1)");
            Assert.AreEqual(AnswerKind.Accept, Decide(s, c.Id).Answer.Kind, "관전자 보기는 받는다");
        }
    }
}
