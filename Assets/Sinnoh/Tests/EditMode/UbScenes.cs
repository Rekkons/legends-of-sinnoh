using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>울트라 버스트 장면 빌더(M6 「UB 셋업·맵」): 실제 셋업을 거친 판(<see cref="Started"/>)과 토큰을 손으로 놓는 빈 판(<see cref="Bare"/> — UB 이동 점수표 대조용).</summary>
    static class UbScenes
    {
        /// <summary>울트라 버스트 판을 셋업 답까지 보내 첫 MapAction에 세운다(Flow.Started의 캠페인 인자).</summary>
        public static GameState Started(Ctx ctx, int seats, ulong seed = 7, IEventSink log = null, RuleOptions opt = null) =>
            Flow.Started(ctx, seats, seed, log, opt, null, false, Campaign.UltraBurst);

        /// <summary>
        /// 셋업을 건너뛴 울트라 버스트 판(턴 단계 · 라운드 1 · 차례 좌석 0): 나무·산호·사당 토큰은 제자리, UB·네크로즈마·숨겨진 장소는 보드에 없다(숨겨진 장소 토큰은 보드 밖 뒷면).
        /// 좌석 말도 보드 밖(−1) — 장면이 필요한 것만 놓는다.
        /// </summary>
        public static GameState Bare(Ctx ctx, int seats = 1, ulong seed = 7, RuleOptions opt = null)
        {
            var s = Engine.NewGame(ctx, opt ?? new RuleOptions(), seats, seed, Campaign.UltraBurst);
            s.Phase = Phase.Turn;
            s.Round = 1;
            s.TurnId = 1;
            s.ActiveSeat = 0;
            s.RoundStartSeat = 0;
            return s;
        }

        public static int Cell(Ctx ctx, int col, int row)
        {
            Assert.IsTrue(ctx.Board.TryGetIndex(new HexCoord(col, row), out int cell), $"칸 ({col},{row})이 없다");
            return cell;
        }

        public static int Fixture(Ctx ctx, string key)
        {
            int f = ctx.Fixtures.Ids.IndexOf(key);
            Assert.GreaterOrEqual(f, 0, key);
            return f;
        }

        /// <summary>UB 토큰을 그 칸에 놓고 토큰 색인을 돌려준다.</summary>
        public static int PlaceUb(GameState s, Ctx ctx, string key, int cell)
        {
            s.Tokens.Add(new TokenState { Kind = TokenKind.UltraBeast, Def = Scene.Def(ctx, key), Cell = cell, FaceUp = true });
            return s.Tokens.Count - 1;
        }

        /// <summary>보드 밖 숨겨진 장소 토큰 하나(조건에 맞는 첫 토큰)를 그 칸에 뒷면으로 놓고 토큰 색인을 돌려준다.</summary>
        public static int PlaceSpot(GameState s, Ctx ctx, int cell, System.Predicate<ItemDef> which = null)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell >= 0 || (which != null && !which(ctx.Items.Items[t.Def]))) continue;
                t.Cell = cell;
                return i;
            }
            Assert.Fail("놓을 숨겨진 장소 토큰이 없다");
            return -1;
        }

        /// <summary>그 칸의 토큰(종류). 없으면 null.</summary>
        public static TokenState TokenAt(GameState s, int cell, TokenKind kind) => s.Tokens.Find(t => t.Kind == kind && t.Cell == cell);

        /// <summary>그 칸의 이웃 칸(방향 0~5, 맵 밖 = Board.NoCell).</summary>
        public static int[] Around(Ctx ctx, int cell)
        {
            var r = new int[Board.Directions];
            for (int d = 0; d < r.Length; d++) r[d] = ctx.Board.Neighbor(cell, d);
            return r;
        }

        /// <summary>열린 균열 카드 붙이기(턴 시작의 동시 선택)를 모두 시간 초과 기본 답으로 넘긴다.</summary>
        public static void Attach(GameState s, Ctx ctx, IEventSink log = null)
        {
            NoSkip(s, ctx, log); // 라운드 끝 건너뛰기 물음이 남았으면 먼저(결정 84 U2)
            for (int guard = 0; guard < 200; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.Simultaneous && x.Sub == ChoiceSub.BreachAttach);
                if (c == null) return;
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log), "균열 카드 붙이기");
            }
            Assert.Fail("균열 카드 붙이기가 끝나지 않는다");
        }

        /// <summary>균열 강도가 percent%가 될 때까지 나무·산호·사당 토큰을 앞에서부터 파괴해 둔다(M6 「UB 화면 묶음 1」 — 85·86행 장면). 돌려준 값 = 그 뒤의 균열 강도.</summary>
        public static int Breach(GameState s, int percent)
        {
            foreach (var t in s.Tokens)
            {
                if (UbTurn.Breach(s) >= percent) break;
                if (UbTurn.IsTarget(t.Kind) && t.Cell >= 0 && !t.Destroyed) t.Destroyed = true;
            }
            return UbTurn.Breach(s);
        }

        /// <summary>모든 좌석이 [턴 종료]로 한 바퀴(라운드 끝의 UB 턴까지 돈다 — 턴 시작의 균열 카드 붙이기는 기본 답). 게임이 끝나면 멈춘다. 새 라운드 첫 턴의 붙이기는 열린 채 둔다.</summary>
        public static void EndRound(GameState s, Ctx ctx, IEventSink log = null)
        {
            int round = s.Round;
            int guard = 0;
            while (s.Phase == Phase.Turn && s.Round == round)
            {
                Attach(s, ctx, log);
                Assert.Less(guard++, 50, "라운드가 끝나지 않는다");
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
                NoSkip(s, ctx, log);
            }
        }

        /// <summary>라운드 끝 「UB 턴을 건너뛰겠습니까?」(Respond/UbSkip — 결정 84 U2)가 열려 있으면 기본 답(건너뛰지 않음)으로 답한다.</summary>
        public static void NoSkip(GameState s, Ctx ctx, IEventSink log = null)
        {
            // 탈락하지 않은 모든 좌석에 묻는다(결정 87 R3) — 묶음을 모두 기본 답으로
            foreach (var c in s.Pending.FindAll(x => !x.Answered && x.Kind == ChoiceKind.Respond && x.Sub == ChoiceSub.UbSkip))
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log), "UB 턴 건너뛰기 물음");
        }
    }
}
