using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 실제 흐름(Engine.Start → 셋업 답 → 턴)으로 장면을 만든다(M3-d). Scene은 흐름을 건너뛰고 자리를 바로 만든다.
    /// </summary>
    static class Flow
    {
        /// <summary>
        /// NewGame + Start 뒤 셋업 선택에 모두 답해 첫 MapAction까지 간다. answer가 null이거나 null을 돌려주면 그 선택의 Default를 사람 답으로 보낸다.
        /// timeout이면 Default 대신 Timeout 명령을 보낸다.
        /// </summary>
        public static GameState Started(Ctx ctx, int seats, ulong seed = 7, IEventSink log = null, RuleOptions opt = null,
            Func<GameState, PendingChoice, ChoiceAnswer> answer = null, bool timeout = false, Campaign campaign = Campaign.HeartOfSinnoh)
        {
            var s = Engine.NewGame(ctx, opt ?? new RuleOptions(), seats, seed, campaign);
            Engine.Start(s, ctx, log);
            int guard = 0;
            while (s.Phase == Phase.Setup)
            {
                Assert.Less(guard++, 100, "셋업이 끝나지 않는다");
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "셋업 중인데 답할 선택이 없다");
                if (timeout)
                {
                    Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log), c.Kind.ToString());
                    continue;
                }
                var a = answer?.Invoke(s, c) ?? c.Default.Clone();
                Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, a), log), c.Kind.ToString());
            }
            return s;
        }

        /// <summary>열린 선택 하나(종류가 맞아야 한다).</summary>
        public static PendingChoice Only(GameState s, ChoiceKind kind)
        {
            Assert.AreEqual(1, s.Pending.Count, "열린 선택이 하나가 아니다");
            Assert.AreEqual(kind, s.Pending[0].Kind);
            return s.Pending[0];
        }

        /// <summary>열린 선택 하나에 답한다(그 좌석이 사람으로).</summary>
        public static void Answer(GameState s, Ctx ctx, ChoiceKind kind, ChoiceAnswer a, IEventSink log = null)
        {
            var c = Only(s, kind);
            Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, a), log), $"{kind} {a}");
        }

        /// <summary>지금 MapAction의 합법 답.</summary>
        public static List<ChoiceAnswer> MapLegal(GameState s, Ctx ctx) => Scene.Legal(s, ctx, Only(s, ChoiceKind.MapAction));

        public static bool Has(List<ChoiceAnswer> legal, AnswerKind kind) => legal.Exists(x => x.Kind == kind);

        /// <summary>그 종류·조우 등급의 첫 나무·산호 토큰 색인(토큰 목록 순서).</summary>
        public static int Tree(GameState s, Ctx ctx, TokenKind kind, EncounterTier tier)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind == kind && ctx.Fixtures.Fixtures[t.Def].Tier == tier) return i;
            }
            Assert.Fail($"{kind} {tier} 토큰이 없다");
            return -1;
        }

        /// <summary>그 덱에서 조건에 맞는 카드를 모두 버림 더미로 옮기고(keep장 남김) 옮긴 수를 돌려준다.</summary>
        public static int Strip(GameState s, Ctx ctx, DeckId deck, Biome biome, Climate climate, int keep)
        {
            var pile = s.Decks[(int)deck];
            int kept = 0, moved = 0;
            for (int i = pile.Count - 1; i >= 0; i--)
            {
                var p = ctx.Cards.Pokemon[pile[i]];
                if (p.Biome != biome || p.Climate != climate) continue;
                if (kept < keep)
                {
                    kept++;
                    continue;
                }
                s.Discards[(int)deck].Add(pile[i]);
                pile.RemoveAt(i);
                moved++;
            }
            return moved;
        }

        /// <summary>to 칸으로 한 걸음에 들어갈 수 있는 이웃 칸(높이 A6, 고정물·토큰 없는 칸). 없으면 −1.</summary>
        public static int StepInto(GameState s, Ctx ctx, int to)
        {
            for (int d = 0; d < Board.Directions; d++)
            {
                int n = ctx.Board.Neighbor(to, d);
                if (n == Board.NoCell || !MoveRules.ElevationAllows(ctx.Board[n], ctx.Board[to], s.Options.ElevationMode)) continue;
                bool used = false;
                foreach (var t in s.Tokens)
                    if (t.Cell == n) used = true;
                foreach (var f in ctx.Fixtures.Fixtures)
                    if (f.Cell == n) used = true;
                if (!used) return n;
            }
            return -1;
        }

        public static List<T> Of<T>(EventLog log) where T : GameEvent => log.OfType<T>();
    }
}
