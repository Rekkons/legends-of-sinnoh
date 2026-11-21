using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「연출 목록 채우기」의 UI 캡처 장면(04 §8-8 M 열 6인 행 가운데 앞 묶음이 장면 없이 남긴 것 — 울트라 버스트 4인 판, 시드 <see cref="UbSeed"/>):
    /// 83행 토큰 날기(831 — 밟힌 칸에서 떠오름 → 깨어진 세계에서 내려앉음) · 92행 수호신 격파 921(「모든 트레이너 이동력 +2」) · 93행 실버디 → 축복 +1(930 — 결과 창 [확인] 뒤 보드).
    /// 92행 진행만 보이는 단계(row-92-progress)는 <see cref="TrialProgressOnly"/>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] FillScreens =
        {
            // 83행: UB 곁에 뒷면 숨겨진 장소를 놓아 둔 판 — 밟힌 칸에서 떠오름 → 깨어진 세계의 빈 칸에서 내려앉음
            Screen("row-83-fly-rise", s => SpotFly(s, 250, true)),
            Screen("row-83-fly-land", s => SpotFly(s, 800, false)),
            // 92행 수호신 격파(921 — 의지의 시련을 넘긴 판의 아그놈을 이긴다): DS 위 「모든 트레이너 이동력 +2」
            Battle("row-92-guardian", s => Seen(FightInto(s, UbAt(s, Lake, more: st => st.Ub.TrialsOvercome[(int)Shard.Willpower] = true), st => st.Row == UbBattleRows.Guardian, 600), b => b.BannerShown > 0f, "이동력 +2 판")),
            // 93행 실버디(신오신전): 결과 창 [확인] 뒤 보드 — 신전 칸의 금빛 → 위 띠 축복 칩으로 나는 아이콘 → 칩에 박히는 금빛 고리(수 +1)
            Screen("row-93-light", s => SilvallyBoard(s, 340, ub => ub.GoldShown > 0f, "신전 칸의 금빛")),
            Screen("row-93-fly", s => SilvallyBoard(s, SilvallyBeats.FlyFromMs + SilvallyBeats.FlyMs / 2, ub => ub.BlessFly > 0f && ub.BlessFly < 1f, "나는 축복 아이콘")),
            Screen("row-93-ring", s => SilvallyBoard(s, SilvallyBeats.FlyFromMs + SilvallyBeats.FlyMs + 120, ub => ub.RingShown > 0f, "축복 칩의 금빛 고리")),
        };

        // 각 UB 곁의 빈 야생 칸에 본맵의 뒷면 숨겨진 장소를 하나씩 옮겨 둔다(UB는 숨겨진 장소에 끌린다 — UbTurn.Score의 LureScore)
        static void SpotsByBeasts(Ctx ctx, GameState st)
        {
            var spots = st.Tokens.FindAll(t => t.Kind == TokenKind.HiddenSpot && t.Cell >= 0 && !t.FaceUp && !t.Special && !ctx.Board[t.Cell].DistortionWorld);
            int k = 0;
            foreach (var ub in st.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast && t.Cell >= 0))
                for (int d = 0; d < Sinnoh.Rules.Board.Directions && k < spots.Count; d++)
                {
                    int n = ctx.Board.Neighbor(ub.Cell, d);
                    if (n == Sinnoh.Rules.Board.NoCell || !ctx.Board[n].Wild || ctx.Board[n].DistortionWorld || st.Tokens.Exists(t => t.Cell == n) || Array.Exists(st.Seats, seat => seat.Cell == n)) continue;
                    spots[k++].Cell = n;
                    break;
                }
        }

        // 83행 토큰 날기 단계 안의 ms: rise = 밟힌 칸에서 떠오르는 중(출발 칸), 아니면 깨어진 세계의 칸에서 내려앉는 중
        static void SpotFly(UiShot s, int ms, bool rise)
        {
            var flow = UbTurnInto(s, st => st.Row == UbRows.Fly, ms, st => SpotsByBeasts(s.Board.Ctx, st));
            var step = Queued(flow, st => st.Row == UbRows.Fly);
            var m = SpotFlyBeats.Event(step);
            var tokens = s.Board.Tokens;
            int want = rise ? m.From : m.To;
            if (want < 0) throw new InvalidOperationException("갈 칸이 없는 토큰이다(버림) — 내려앉는 장면이 아니다");
            if (!tokens.RaisedDrawn || tokens.RaisedCell != want || tokens.RaisedHeight <= 0f)
                throw new InvalidOperationException("떠 있는 토큰: 칸 " + tokens.RaisedCell + "(바란 칸 " + want + ") · 그려짐 " + tokens.RaisedDrawn + " · 높이 " + tokens.RaisedHeight);
            if (s.Board.Ctx.Board[want].DistortionWorld == rise) throw new InvalidOperationException(rise ? "출발 칸이 깨어진 세계다" : "도착 칸이 깨어진 세계가 아니다");
            UbClean(flow.Hud);
        }

        // 93행: 신오신전의 실버디를 이겨 결과 창 [확인] → 보드의 축복 단계 안의 ms(칩 숫자는 아이콘이 닿기 전 = 얻기 전 값)
        static void SilvallyBoard(UiShot s, int ms, Func<BoardUb, bool> seen, string what)
        {
            var flow = UbAt(s, Silvally);
            UbResultWindow(s, flow, "battle-result-title");
            var result = (BattleScreen)flow.Stage.Current;
            for (int i = 0; i < 20 && result.AskingReward; i++)
            {
                result.PressReward(result.Rewards.Count - 1);   // 전설을 이긴 보상(가르치기 · 놓아주기)은 마지막 답(가르치지 않기)
                Pass(flow, BattleStepMs);
            }
            for (int i = 0; i < 40 && !flow.BattleOver; i++) Pass(flow, BattleStepMs);
            if (!result.Confirm()) throw new InvalidOperationException("[확인]이 거절됐다(보상 물음 " + result.AskingReward + ")");
            FxStep step = null;
            for (int i = 0; i < 20 && (step = Queued(flow, st => st.Row == UbBattleRows.Silvally)) == null; i++) Pass(flow, 1);
            if (step == null) throw new InvalidOperationException("실버디 축복 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
            var ub = flow.Hud.Ub;
            if (!seen(ub)) throw new InvalidOperationException(what + "이(가) 보이지 않는다");
            int now = flow.Client.View.Ub.Blessings, want = ms < SilvallyBeats.FlyFromMs + SilvallyBeats.FlyMs ? now - 1 : now;
            if (ub.ShownBless != want) throw new InvalidOperationException("축복 칩 " + ub.ShownBless + " ≠ " + want + "(보기 " + now + ")");
            UbClean(flow.Hud);
        }

        // 92행 진행만 보이는 단계(넘기지 않은 시련 진행 — 의지의 시련 크리티컬 1/2): 판 난수를 다시 심어 가며 그 단계가 오는 첫 판
        static void TrialProgressOnly(UiShot s)
        {
            for (ulong seed = 1; seed <= 16; seed++)
            {
                ulong at = seed;
                if (seed > 1) s.Panel.Root.Clear();   // 앞 판의 화면 요소를 걷고 새 판으로(같은 패널 하나)
                var flow = UbAt(s, Lake, more: st => st.Rng.CopyFrom(new Pcg32(at)));
                try
                {
                    FightInto(s, flow, st => st.Row == UbBattleRows.Trial && UbRows.First<TrialProgress>(st) is TrialProgress p && !p.Done, 150);
                }
                catch (InvalidOperationException) { continue; }   // 이 판은 진행만 보이는 단계 없이 넘기거나 끝났다
                Seen(flow, b => b.TrialShown != null && !b.TrialPassed && b.ImmuneShown, "넘기기 전 시련 칩 · 면역");
                return;
            }
            throw new InvalidOperationException("진행만 보이는 시련 단계가 오는 판이 없다");
        }
    }
}
