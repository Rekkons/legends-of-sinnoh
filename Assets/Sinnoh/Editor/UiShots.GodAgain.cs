using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 198(2026-11-21 — 울트라 버스트 방 판에서 펄기아를 잡았더니 「한 번 더!」가 떴는데 턴이 넘어갔다): 신 격파(80행)의 배너 · 기록 줄은 추가 턴이 실제로 오는 때만 「한 번 더」.
    /// 신오의 심장 판(추가 턴 — 「한 번 더!」) · 울트라 버스트 판(추가 턴 없음 — 「신을 넘어섰다!」) 둘 다 시작의 사당의 기라티나를 이긴 뒤 [확인] → 보드의 80행 700ms. 그림 <c>Logs/Review/m10/god-again/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] GodAgainScreens =
        {
            Screen("god-again-heart", FitBoard(s => GodWin(s, false))),
            Screen("god-again-ub", FitBoard(s => GodWin(s, true))),
        };

        static void GodWin(UiShot s, bool ub)
        {
            var ctx = s.Board.Ctx;
            Action<GameState> prepare = state =>
            {
                GiveTeam(ctx, state);
                int cell = HosFlow.OriginShrineCell(ctx);
                state.Seats[state.ActiveSeat].Cell = cell;
                state.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = ctx.Cards.PokemonIds.IndexOf("487:1"), Cell = cell, FaceUp = true });
            };
            // 같은 시드의 혼자 판(캠페인만 다르다 — 주사위가 같아 둘 다 이긴다)
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Campaign = ub ? Campaign.UltraBurst : Campaign.HeartOfSinnoh }, 1, Seed, AiSeed, prepare);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            DealSettle(s, flow, MyMap);
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(시작의 사당의 신 토큰)");
            var result = Fight(flow, b => b.ShowsResult);
            if (!flow.Battle.Won) throw new InvalidOperationException("신을 이기지 못했다");
            for (int i = 0; i < 20 && result.AskingReward; i++)
            {
                result.PressReward(result.Rewards.Count - 1);
                Pass(flow, BattleStepMs);
            }
            for (int i = 0; i < 40 && !flow.BattleOver; i++) Pass(flow, BattleStepMs);
            var step = Queued(flow, st => st.Row == ResultRows.GodGone);
            if (step == null) throw new InvalidOperationException("신 격파 단계(800)가 대기열에 없다");
            if (!result.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            Into(s, flow, step, 700);
            string want = flow.Stage.T(ub ? "screen.board.god.done" : "screen.board.god.again");
            if (flow.Results == null || !flow.Results.BannerShown || flow.Results.BannerText != want) throw new InvalidOperationException("배너 「" + (flow.Results != null ? flow.Results.BannerText : "없음") + "」 ≠ 「" + want + "」");
            string line = ub ? "screen.log.line.god.done" : "screen.log.line.god";
            bool logged = false;
            foreach (var l in flow.Log.Lines) logged |= l.Line != null && l.Line.Key == line;
            if (!logged) throw new InvalidOperationException("기록 줄 " + line + "이 없다");
        }
    }
}
