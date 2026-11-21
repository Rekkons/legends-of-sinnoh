using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 배틀 장면 빌더(M3-e). Scene.Game에 고향 마을·칸을 붙여 배틀 뒤 귀환·턴 넘김이 실제처럼 돈다. 배틀은 SendIn 단계부터 BattleFlow.Run으로 돈다.
    /// 주사위가 갈리는 장면은 <see cref="Seed"/>로 원하는 결과가 나오는 난수 시드를 찾는다(시험은 결정적이다).
    /// </summary>
    static class Battles
    {
        /// <summary>장면 게임: 좌석 i의 고향 마을 = 깨어진 세계가 아닌 마을의 i번째, 말은 그 마을 칸.</summary>
        public static GameState Game(Ctx ctx, int seats = 2, ulong seed = 7, RuleOptions opt = null)
        {
            var s = Scene.Game(ctx, seats, seed, opt);
            var fixtures = ctx.Fixtures.Fixtures;
            int v = 0;
            for (int f = 0; f < fixtures.Count && v < seats; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || ctx.Board[fixtures[f].Cell].DistortionWorld) continue;
                s.Seats[v].HomeVillage = f;
                s.Seats[v].Cell = fixtures[f].Cell;
                v++;
            }
            return s;
        }

        /// <summary>
        /// 배틀을 SendIn 단계에 놓고 돈다(BattleStarted 없이). Side 0 = 좌석 seat0, Side 1 = owner1(좌석이면 그 파티, 아니면 roster1).
        /// token = 조우 토큰 색인(A8 규토리, 없으면 −1).
        /// </summary>
        public static BattleState Start(GameState s, Ctx ctx, IEventSink log, BattleKind kind, int seat0, int owner1, int token, params int[] roster1)
        {
            var b = new BattleState { Id = s.NextBattleId++, Kind = kind, Cell = s.Seats[seat0].Cell, Token = token, Step = BattleStep.SendIn, ActiveSide = -1 };
            Side(s, b.Sides[0], seat0, null);
            Side(s, b.Sides[1], owner1, roster1);
            s.Battle = b;
            s.Phase = Phase.Battle;
            BattleFlow.Run(s, ctx, Scene.Fx(s, log, seat0, kind == BattleKind.Wild ? ActionKind.EncounterPick : ActionKind.Challenge));
            return b;
        }

        static void Side(GameState s, BattleSide side, int owner, int[] roster)
        {
            side.Owner = owner;
            side.Responder = owner;
            if (owner >= 0)
            {
                foreach (int p in s.Seats[owner].Party)
                    if (p >= 0) side.Roster.Add(p);
            }
            else side.Roster.AddRange(roster);
        }

        /// <summary>야생 배틀(조우 토큰 없음).</summary>
        public static BattleState Wild(GameState s, Ctx ctx, IEventSink log, int seat, int wild, int token = -1) =>
            Start(s, ctx, log, BattleKind.Wild, seat, SeatId.Wild, token, wild);

        /// <summary>열린 선택 하나(종류가 맞아야 한다).</summary>
        public static PendingChoice Only(GameState s, ChoiceKind kind, ChoiceSub sub = ChoiceSub.None)
        {
            Assert.AreEqual(1, s.Pending.Count, $"열린 선택이 하나가 아니다: {Describe(s)}");
            Assert.AreEqual(kind, s.Pending[0].Kind, Describe(s));
            if (sub != ChoiceSub.None) Assert.AreEqual(sub, s.Pending[0].Sub);
            return s.Pending[0];
        }

        static string Describe(GameState s)
        {
            var parts = new List<string>();
            foreach (var c in s.Pending) parts.Add($"{c.Kind}/{c.Sub}@{c.Seat}");
            return string.Join(", ", parts);
        }

        /// <summary>답한다. 좌석이면 사람 답, 가상 응답자(갤럭시단·야생)면 AI 답.</summary>
        public static void Answer(GameState s, Ctx ctx, PendingChoice c, ChoiceAnswer a, IEventSink log)
        {
            var cmd = c.Seat < 0 ? Command.AiAnswer(c.Id, a, new AiReason(AiRule.None, 0, 0)) : Command.AnswerTo(c.Id, a);
            Scene.Accept(Engine.Submit(s, ctx, c.Seat, cmd, log), $"{c.Kind}/{c.Sub} {a}");
        }

        public static void Answer(GameState s, Ctx ctx, ChoiceKind kind, ChoiceAnswer a, IEventSink log) => Answer(s, ctx, Only(s, kind), a, log);

        public static ChoiceAnswer Move(int move) => ChoiceAnswer.Of(AnswerKind.UseMove, move);

        /// <summary>배틀 선택과 배틀 뒤 선택(Window = BattleEnd)에 pick(없거나 null이면 Default)으로 답하며 배틀이 끝날 때까지 돈다.</summary>
        public static void Drive(GameState s, Ctx ctx, IEventSink log, Func<GameState, PendingChoice, ChoiceAnswer> pick = null, int limit = 300)
        {
            for (int i = 0; i < limit; i++)
            {
                var c = s.Pending.Find(x => !x.Answered && (s.Battle != null || x.Window == Window.BattleEnd));
                if (c == null) return;
                Answer(s, ctx, c, pick?.Invoke(s, c) ?? c.Default.Clone(), log);
            }
            Assert.Fail("배틀이 끝나지 않는다");
        }

        /// <summary>0부터 시드를 넣어 보며 ok가 참인 첫 시드(시험이 원하는 주사위 결과를 결정적으로 고른다).</summary>
        public static ulong Seed(Func<ulong, bool> ok, int tries = 3000)
        {
            for (ulong seed = 0; seed < (ulong)tries; seed++)
                if (ok(seed)) return seed;
            Assert.Fail($"{tries}개 시드 안에 원하는 결과가 없다");
            return 0;
        }

        /// <summary>그 포켓몬의 체력을 hp로(피해를 늘려서).</summary>
        public static void SetHp(GameState s, Ctx ctx, int pokemon, int hp)
        {
            var p = s.Pokemon[pokemon];
            p.Damage = PokemonRules.MaxHp(ctx, p) - hp;
        }

        public static int Hp(GameState s, Ctx ctx, int pokemon) => PokemonRules.Hp(ctx, s.Pokemon[pokemon]);
    }
}
