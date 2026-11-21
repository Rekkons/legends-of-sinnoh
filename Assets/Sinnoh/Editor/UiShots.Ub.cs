using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「UB 보드 화면」의 기본 몫(목업 고르기와 무관한 보드 쪽 — 04 §8-8 15·82·83행)의 UI 캡처 장면: 울트라 버스트 4인 판(시드 20261117 — UB 목업과 같은 판)에서
    /// UB 9 · 네크로즈마 피규어가 토큰 칸에 서고, UB 턴 동안 칸에서 칸으로 옮기고, 밟은 나무·산호·사당이 어두워지고, 좌석 말이 왜곡 게이트로 건너가는 순간들.
    /// 균열 게이지 · UB 턴 띠 · 균열 카드 창(목업 M1~M3)은 그리지 않는다. 보드 쪽 행이라 FxShots(3D 보드가 없다)에는 장면이 없다(14행 순간이동과 같다).
    /// </summary>
    public static partial class UiShots
    {
        public const ulong UbSeed = 20261117;
        public const int UbSeats = 4;

        public static readonly KeyValuePair<string, Action<UiShot>>[] UbScreens =
        {
            // 게임 시작 뒤 첫 내 턴: 전체 줌 — 본맵의 UB 9(야생 칸) · 깨어진 세계(보인다)와 가운데의 네크로즈마
            Screen("ub-board-wide", s => UbLook(s, null, BoardCameraRig.Zoom.Full)),
            // 지역 줌: UB 한 마리(첫 UB 토큰 칸) · 네크로즈마와 깨어진 세계의 게이트
            Screen("ub-board-beast", s => UbLook(s, null, BoardCameraRig.Zoom.Region, (flow, st) => BeastCell(flow, st, TokenKind.UltraBeast))),
            Screen("ub-board-distortion", s => UbLook(s, null, BoardCameraRig.Zoom.Region, (flow, st) => BeastCell(flow, st, TokenKind.Necrozma))),
            // 네 라운드 뒤: 부서진 나무·산호·사당이 어둡다(지역 줌 — 부서진 나무가 가장 많은 곳)
            Screen("ub-board-destroyed", s => UbLook(s, st => UbRounds(s.Board.Ctx, st, 4), BoardCameraRig.Zoom.Region, (flow, st) => DestroyedCell(s, st))),
            // 82행 UB 이동: 첫 이동 단계 한가운데(Walk · 칸 사이 점프 · 카메라가 따라간다) · 끝 무렵
            Screen("row-82-move", s => UbTurnInto(s, st => st.Row == UbRows.Move, 200)),
            Screen("row-82-land", s => UbTurnInto(s, st => st.Row == UbRows.Move, 390)),
            // 83행 밟은 칸의 파괴: 첫 파괴 단계 — 공격 클립 · 나무가 어두워지는 중 → 다 어두워짐
            Screen("row-83-break", s => UbTurnInto(s, st => st.Row == UbRows.Destroy, 550)),
            Screen("row-83-dark", s => UbTurnInto(s, st => st.Row == UbRows.Destroy, 990)),
            // 15행 왜곡 게이트 이동: 게이트에 선 내 말 · 건너편 칸을 고름(안내 줄 「왜곡 게이트로 건너갑니다」) → 가라앉음 → 보라 점선(카메라가 건너간다) → 솟아오름 → 착지 고리
            Screen("row-15-pick", s => GatePick(s)),
            Screen("row-15-sink", s => GateInto(s, 400)),
            Screen("row-15-line", s => GateInto(s, 760)),
            Screen("row-15-rise", s => GateInto(s, 1150)),
            Screen("row-15-land", s => GateInto(s, 1450)),
            // 결정 189 ① UB · 네크로즈마 발밑 붉은 바닥 고리(지역 줌 — 첫 UB) · ② 균열 칩 누름(찾기): 다가가는 중(150ms — 첫 번쩍임) · 다가간 뒤 둘째 번쩍임(600ms) · 다시 눌러 다음 UB(150ms)
            Screen("ub-ring", s => UbRing(s, -1)),
            Screen("ub-find-mid", s => UbRing(s, 150)),
            Screen("ub-find-flash", s => UbRing(s, 600)),
            Screen("ub-find-next", s => UbRing(s, 150, 2)),
        };

        // 결정 189: 고리가 선 UB마다 하나(자리 = 피규어의 땅) — ms < 0이면 첫 UB를 지역 줌으로, 아니면 균열 칩을 presses번 누르고(앞 찾기는 끝까지) 마지막 누름에서 ms 지난 순간
        static void UbRing(UiShot s, int ms, int presses = 1)
        {
            var flow = UbGame(s);
            var rings = s.Board.Rings;
            int n = flow.BeastsShown.Count;
            if (rings == null || n != UbState.Beasts + 1 || rings.Points.Count != n) throw new InvalidOperationException("바닥 고리 " + (rings != null ? rings.Points.Count : -1) + " · 선 UB " + n + " ≠ 10");
            for (int i = 0; i < n; i++)
                if ((rings.Points[i] - flow.BeastGrounds[i]).sqrMagnitude > 1e-6f || rings.Lit[i]) throw new InvalidOperationException("고리 " + i + "가 UB의 땅에 없거나 번쩍인다");
            if (ms < 0)
            {
                s.Rig.Set(BoardCameraRig.Zoom.Region, s.Rig.Yaw, flow.BeastGrounds[0]);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                return;
            }
            for (int p = 0; p < presses; p++)
            {
                if (p > 0) Pass(flow, GameFlow.FindMs);
                if (!flow.FindBeast()) throw new InvalidOperationException("균열 칩 누름(찾기)이 거절됐다");
            }
            if (flow.Found != flow.BeastsShown[(presses - 1) % n]) throw new InvalidOperationException("누를 때마다 다음 UB가 아니다: " + flow.Found);
            var from = s.Rig.Target;
            Pass(flow, ms);
            int at = -1;
            for (int i = 0; i < n && at < 0; i++) if (flow.BeastsShown[i] == flow.Found) at = i;
            bool wantLit = ms < GameFlow.FindMs && (ms / GameFlow.FindFlashMs) % 2 == 0;
            for (int i = 0; i < n; i++)
                if (rings.Lit[i] != (i == at && wantLit)) throw new InvalidOperationException("고리 " + i + " 번쩍임 " + rings.Lit[i] + " (" + ms + "ms)");
            float left = (s.Rig.Target - flow.FoundPoint).magnitude;
            if (ms >= GameFlow.CameraCatchMs ? left > 1e-3f : left < 1e-3f || (s.Rig.Target - from).magnitude < 1e-3f) throw new InvalidOperationException("카메라 보는 점이 " + ms + "ms에 맞지 않는다(남은 거리 " + left + ")");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 울트라 버스트 4인 판을 셋업 뒤(prepare로 손봐) 내 첫 턴 입력이 열린 때까지
        static GameFlow UbGame(UiShot s, Action<GameState> prepare = null, int seats = UbSeats, SoloSettings settings = null)
        {
            var flow = s.Flow();
            flow.StartSeats(settings ?? new SoloSettings { Campaign = Campaign.UltraBurst }, seats, UbSeed, AiSeed, prepare);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, MyMap);
            if (!flow.Client.View.Seats[0].Eliminated && !s.Board.DistortionWorldVisible) throw new InvalidOperationException("울트라 버스트 판인데 깨어진 세계가 숨어 있다");
            Beasts(s, flow);
            return flow;
        }

        static bool MyMap(GameFlow f) => f.InputOpen && f.Hud != null && f.Client.View.ActiveSeat == f.Client.Seat && f.Client.Choice(ChoiceKind.MapAction) != null;

        // 그 판을 그 줌으로 본다(at = 보는 칸 — null이면 전체 줌의 자리)
        static void UbLook(UiShot s, Action<GameState> prepare, BoardCameraRig.Zoom zoom, Func<GameFlow, GameState, int> at = null)
        {
            var flow = UbGame(s, prepare);
            int cell = at != null ? at(flow, flow.Client.View) : -1;
            s.Rig.Set(zoom, s.Rig.Yaw, cell >= 0 ? s.Board.CellTop(cell) : flow.WideLook());
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        static int BeastCell(GameFlow flow, GameState view, TokenKind kind)
        {
            foreach (var t in view.Tokens)
                if (t.Kind == kind && t.Cell >= 0) return t.Cell;
            throw new InvalidOperationException(kind + " 토큰이 맵에 없다");
        }

        // 부서진 나무·산호·사당이 둘레 두 칸쯤(4.2 유닛) 안에 가장 많은 부서진 칸
        static int DestroyedCell(UiShot s, GameState view)
        {
            int best = -1, most = 0;
            foreach (var t in view.Tokens)
            {
                if (!UbBoard.Shaded(t.Kind) || !t.Destroyed || t.Cell < 0) continue;
                int n = 0;
                foreach (var o in view.Tokens)
                    if (UbBoard.Shaded(o.Kind) && o.Destroyed && o.Cell >= 0 && UnityEngine.Vector3.Distance(s.Board.CellTop(t.Cell), s.Board.CellTop(o.Cell)) <= 4.2f) n++;
                if (n > most) { most = n; best = t.Cell; }
            }
            if (best < 0) throw new InvalidOperationException("부서진 나무가 없다");
            return best;
        }

        // 셋업 뒤 판을 rounds 라운드 [턴 종료]로 넘긴다(그 밖의 선택은 시간 초과 기본 답 — 균열 카드 붙이기는 첫 빈 칸). 좌석 0의 맵 행동에서 멈춘다
        static void UbRounds(Ctx ctx, GameState st, int rounds)
        {
            int target = st.Round + rounds;
            for (int guard = 0; ; guard++)
            {
                if (guard > 4000) throw new InvalidOperationException("라운드가 끝나지 않는다");
                if (st.Phase == Phase.GameOver) throw new InvalidOperationException("판이 끝났다");
                var c = st.Pending.Find(x => !x.Answered && x.Kind != ChoiceKind.MapAction) ?? st.Pending.Find(x => !x.Answered);
                if (c == null) throw new InvalidOperationException("열린 선택이 없다");
                if (st.Round >= target && c.Kind == ChoiceKind.MapAction && c.Seat == 0) return;
                var r = c.Kind == ChoiceKind.MapAction
                    ? Engine.Submit(st, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.EndTurn)), null)
                    : Engine.Submit(st, ctx, SeatId.System, Command.TimeoutOf(c.Id), null);
                if (!r.Accepted) throw new InvalidOperationException("거절: " + c.Kind + "/" + c.Sub + " " + r.Reason);
            }
        }

        // 피규어가 UB 토큰 칸에 섰는가(보드 셈 UbBoard.Of와 같은 자리 — 대기열이 빈 때)
        static void Beasts(UiShot s, GameFlow flow)
        {
            int shown = 0;
            foreach (var f in s.Board.Figures.Figures)
            {
                if (f.Group != GameFlow.BeastFigures && f.Group != GameFlow.NecrozmaFigures) continue;
                if (!flow.Ctx.Cards.PokemonIds.TryIndexOf(f.Key, out int def)) continue;
                var at = UbBoard.Of(flow.Client.View.Tokens, def, null, null, 0);
                // 네크로즈마는 그 페이즈의 폼이 1페이즈 피규어 자리에 선다(M6 「UB 시작」)
                var fig = f.Group == GameFlow.NecrozmaFigures ? s.Board.Figures.FormOf(f, UbBoard.NecrozmaForm(flow.Client.View.Ub, null)) ?? f : f;
                if (fig != f && f.Root.activeSelf) throw new InvalidOperationException(f.Key + ": 다른 폼(" + fig.Key + ")이 섰는데 1페이즈 피규어가 남았다");
                if (at.Shown != fig.Root.activeSelf) throw new InvalidOperationException(fig.Key + ": 피규어가 " + (at.Shown ? "서지 않았다" : "치워지지 않았다"));
                if (!at.Shown) continue;
                shown++;
                var want = s.Board.CellTop(at.From);
                var got = fig.Root.transform.localPosition;
                if ((want - got).sqrMagnitude > 1e-4f) throw new InvalidOperationException(f.Key + ": 피규어가 토큰 칸(" + at.From + ")에 서지 않았다 " + got + " ≠ " + want);
            }
            if (shown != UbState.Beasts + 1) throw new InvalidOperationException("선 UB·네크로즈마 피규어 " + shown + " ≠ 10");
        }

        // 내 [턴 종료]부터 모든 좌석이 턴을 마쳐 라운드 끝의 UB 턴이 대기열에 서면, 그 단계(match의 첫 단계) 시작에서 ms 지난 순간으로
        static GameFlow UbTurnInto(UiShot s, Func<FxStep, bool> match, int ms, Action<GameState> prepare = null)
        {
            var flow = UbGame(s, prepare);
            for (int k = 0; k < UbSeats * 2 && Queued(flow, match) == null; k++)
            {
                if (flow.Client.View.ActiveSeat == flow.Client.Seat) DealSettle(s, flow, MyMap);
                EndActive(flow);
                for (int i = 0; i < 4 && Queued(flow, match) == null; i++) Pass(flow, 1);
                if (Queued(flow, match) == null) Pass(flow, BoardReadyMs * 8);
            }
            var step = Queued(flow, match);
            if (step == null) throw new InvalidOperationException("UB 턴 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
            return flow;
        }

        // 내 말을 본맵의 디프마시 게이트에 세운 판(내 첫 턴)
        static GameFlow GateGame(UiShot s)
        {
            var flow = UbGame(s);
            var ctx = flow.Ctx;
            int gate = ctx.Fixtures.Ids.IndexOf("gate.deepmarsh_main");
            flow.Rebase(st => st.Seats[st.ActiveSeat].Cell = ctx.Fixtures.Fixtures[gate].Cell);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            DealSettle(s, flow, MyMap);
            return flow;
        }

        static int GateAcross(GameFlow flow) => flow.Ctx.Fixtures.Fixtures[flow.Ctx.Fixtures.Ids.IndexOf("gate.deepmarsh_distortion")].Cell;

        static void GatePick(UiShot s)
        {
            var flow = GateGame(s);
            if (!flow.Hud.PickCell(GateAcross(flow))) throw new InvalidOperationException("게이트 건너편 칸을 고르지 못했다");
            s.Panel.Tick(3);
            var guide = flow.Stage.Root.Q<Label>("board-hud-guide");
            if (guide == null || guide.text != flow.Stage.T("screen.board.guide.gate")) throw new InvalidOperationException("안내 줄이 게이트 건너기가 아니다: " + (guide != null ? guide.text : "없음"));
        }

        static void GateInto(UiShot s, int ms)
        {
            var flow = GateGame(s);
            if (!flow.Hud.Walk(GateAcross(flow))) throw new InvalidOperationException("게이트 이동이 거절됐다");
            var step = Queued(flow, st => st.Row == UbRows.Gate);
            if (step == null) throw new InvalidOperationException("15행 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
        }
    }
}
