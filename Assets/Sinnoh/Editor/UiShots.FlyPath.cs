using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 167 · 168(2026-11-21): 공중날기 앞의 비전기술 컷인(16행 단계의 앞 1400ms — 띠가 펼쳐짐 · 찌르호크가 들어옴 · 가운데 · 빠져나감 · 접힘, 남 좌석의 것)과
    /// 걷기 전 확인에서 고른 길의 파란 육각 테두리(경유 칸이 있는 길 · 긴 길 · 6인 판). 판은 첫 턴부터 공중날기가 열리는 확인 판(고향이 아닌 마을 · 빨강 규토리 — <c>MouseProbe.StartFlyGame</c>과 같은 손봄).
    /// 장면마다 그 모습(띠 · 탄 것 · 주인공 / 파란 테두리 수 = 길 칸 수)을 단정한다. 모두 보드 위. 그림 <c>Logs/Review/m10/fly/</c> · <c>Logs/Review/m10/path/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] FlyPathScreens =
        {
            Screen("fly-cutin-open", s => FlyCutinAt(s, 1, 0, 200)),                         // 띠가 위아래로 펼쳐지는 중
            Screen("fly-cutin-in", s => FlyCutinAt(s, 1, 0, 480)),                           // 찌르호크(내 주인공을 태움)가 오른쪽에서 들어오는 중
            Screen("fly-cutin-mid", s => FlyCutinAt(s, 1, 0, FlyCutinBeats.InMs)),           // 가운데 — 울음
            Screen("fly-cutin-out", s => FlyCutinAt(s, 1, 0, 1080)),                         // 왼쪽으로 빠져나가는 중 — 휙
            Screen("fly-cutin-close", s => FlyCutinAt(s, 1, 0, 1320)),                       // 띠가 접히는 중
            Screen("fly-cutin-taxi", s => FlyCutinAt(s, 1, 0, FlyBeats.CutinMs + 300)),      // 컷인 뒤 이어지는 택시(찌르호크가 내려와 태움)
            Screen("fly-cutin-other", s => FlyCutinAt(s, 2, 1, FlyCutinBeats.InMs)),         // 좌석 2가 날 때 — 그 좌석의 주인공이 탄다(결정 120)
            Screen("path-via", s => PathShot(s, 1, 0, true)),                                // 경유 칸이 있는 길: 흰 테두리 경유 칸 + 나머지 사이 칸 파랑
            Screen("path-long", s => PathShot(s, 1, 8, false)),                              // 이동력 8의 긴 길
            Screen("path-6", s => PathShot(s, 6, 0, false)),                                 // 6인 판의 내 턴
        };

        // seats명 판: flier 좌석을 고향 마을이 아닌 마을에 세우고 빨강 규토리를 쥐여 준다(첫 턴부터 공중날기 — 고향 마을은 조무래기가 막는다)
        static Action<GameState> FlyReady(GameFlow flow, int flier) => state =>
        {
            var ctx = flow.Ctx;
            var st = state.Seats[flier];
            var fixtures = ctx.Fixtures.Fixtures;
            bool Home(int f) { foreach (var seat in state.Seats) if (seat.HomeVillage == f) return true; return false; }
            for (int f = 0; f < fixtures.Count; f++)
                if (fixtures[f].Kind == FixtureKind.Village && !Home(f) && !ctx.Board[fixtures[f].Cell].DistortionWorld) { st.Cell = fixtures[f].Cell; break; }
            st.Apricorns[(int)ApricornColor.Red] = Math.Max(1, st.Apricorns[(int)ApricornColor.Red]);
        };

        // flier 좌석이 첫 갈 수 있는 마을로 날고, 16행 단계가 시작한 뒤 ms인 순간(내 좌석이 아니면 내 턴을 넘긴 뒤 그 좌석의 답으로)
        static void FlyCutinAt(UiShot s, int seats, int flier, int ms)
        {
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), seats, Seed, AiSeed, FlyReady(flow, flier));
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            if (flier == flow.Client.Seat)
            {
                if (!flow.InputOpen || !flow.Hud.Press(BoardHud.Village) || !(flow.Stage.Current is VillageScreen village)) throw new InvalidOperationException("옮겨 둔 마을의 마을 창이 열리지 않았다");
                int dest = -1;
                foreach (int f in village.Dests) if (village.CanGo(f)) { dest = f; break; }
                if (!village.CanFly || !village.PickDest(dest) || !village.PressFly()) throw new InvalidOperationException("공중날기가 거절됐다(" + village.FlyWhy + ")");
            }
            else
            {
                for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flier; k++)
                {
                    EndActive(flow);
                    Pass(flow, BoardReadyMs * 8);
                }
                if (flow.Client.View.ActiveSeat != flier || !flow.AnswerSeat(flier, a => a.Kind == AnswerKind.Fly)) throw new InvalidOperationException("좌석 " + (flier + 1) + "의 공중날기가 거절됐다");
            }
            flow.Stage.Tweens.Advance(flow.MsUntil(st => st.Row == FlyBeats.Row));
            if (ms > 0) flow.Stage.Tweens.Advance(ms);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var cut = flow.FlyFx;
            var pose = FlyCutinBeats.At(ms, Sinnoh.Rules.Session.PlayTiming.FlyMs);
            if (!pose.Shown)
            {
                if (cut != null && cut.Shown) throw new InvalidOperationException("컷인 뒤에도 띠가 남았다");
                if (!flow.BirdShown(out _)) throw new InvalidOperationException("컷인 뒤 택시 찌르호크가 없다");
                return;
            }
            var root = flow.Stage.Root;
            if (cut == null || !cut.Shown)
            {
                var q = flow.Fx.Sequencer;
                var fs16 = FlyBeats.StepOf(q.Current);
                throw new InvalidOperationException(ms + "ms: 컷인이 보이지 않는다(층 " + (cut == null ? "없음" : "숨음") + " · 지금 항목 " + (q.Current == null ? "없음" : q.Current.LengthMs + "ms") + " · 16행 " + (fs16 == null ? "없음" : fs16.StartMs + "+" + fs16.Ms) + " · 시각 " + q.LocalMs + " · HUD 크기 " + (root.Q("board-hud") != null ? root.Q("board-hud").layout.ToString() : "없음") + ")");
            }
            var band = root.Q("board-fly-cutin-band").worldBound;
            var screen = root.worldBound;
            if (band.yMin < screen.yMin || band.yMax > screen.yMax || Mathf.Abs(band.center.y - screen.center.y) > 2f) throw new InvalidOperationException("띠가 화면 가운데에 있지 않다: " + band);
            if (flow.BirdShown(out _)) throw new InvalidOperationException("컷인 동안 택시 찌르호크가 벌써 내려왔다");
            if (pose.Riders)
            {
                if (cut.RiderShown != flow.Protagonist(flier)) throw new InvalidOperationException("탄 주인공 " + cut.RiderShown + " ≠ 좌석 " + (flier + 1) + "의 " + flow.Protagonist(flier));
                var bird = root.Q("board-fly-cutin-bird");
                if (bird == null) throw new InvalidOperationException("찌르호크 도트가 없다");
                var b = bird.worldBound;
                if (b.center.y < band.yMin || b.center.y > band.yMax) throw new InvalidOperationException("찌르호크가 띠 밖에 있다: " + b + " / " + band);
            }
        }

        // 걷기 전 확인: 길이 가장 긴 칸을 골라(via면 그 길 밖의 경유 칸을 더해) 그 길이 다 보이게 카메라를 그 가운데로 — 파란 테두리 = 출발 · 고른 칸 · 경유 칸 뺀 길 칸
        static void PathShot(UiShot s, int seats, int moves, bool via)
        {
            GameFlow flow;
            if (seats == 1) flow = Seats(s, 1, moves > 0 ? (Action<GameState>)(state => state.Seats[0].MovesLeft = moves) : null);
            else flow = MyTurn(s, seats, null);
            var hud = flow.Hud;
            if (hud == null || !flow.InputOpen || hud.Reach.Count == 0) throw new InvalidOperationException("걷기 입력이 열리지 않았다");
            var lengths = new Dictionary<int, int>();
            var targets = new List<int>(hud.Reach);
            foreach (int c in targets) { hud.PickCell(c); lengths[c] = hud.PickedPath.Count; }
            targets.Sort((x, y) => lengths[y] != lengths[x] ? lengths[y].CompareTo(lengths[x]) : x.CompareTo(y));
            int target = -1, viaCell = -1;
            foreach (int t in targets)
            {
                if (lengths[t] < 3) break;
                hud.PickCell(t);
                if (!via) { target = t; break; }
                var p = hud.PickedPath;
                foreach (int c in hud.Reach)
                    if (c != t && !p.Contains(c) && hud.AddVia(c)) { viaCell = c; break; }
                if (viaCell >= 0) { target = t; break; }
            }
            if (target < 0) throw new InvalidOperationException("사이 칸이 있는 길" + (via ? " + 경유 칸" : "") + "이 없다");
            var path = hud.PickedPath;
            if (moves > 0 && path.Count - 1 < 5) throw new InvalidOperationException("이동력 " + moves + "인데 길이 짧다(" + (path.Count - 1) + "걸음)");
            s.Rig.Set(BoardCameraRig.Zoom.Region, s.Rig.Yaw, s.Board.CellTop(path[path.Count / 2]));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var marks = flow.BoardMarks;
            int want = path.Count - 2 - (viaCell >= 0 ? 1 : 0);
            if (!marks.Held || marks.Trail.Count != want) throw new InvalidOperationException("파란 테두리 " + marks.Trail.Count + " ≠ 길 칸 " + want + "(길 " + string.Join(",", path) + ")");
            for (int i = 1; i < path.Count - 1; i++)
                if (path[i] != viaCell && !HasCell(marks.Trail, path[i])) throw new InvalidOperationException("길 칸 " + path[i] + "에 파란 테두리가 없다");
            if (viaCell >= 0 && (HasCell(marks.Trail, viaCell) || marks.Vias.Count != 1)) throw new InvalidOperationException("경유 칸이 흰 테두리가 아니다");
        }

    }
}
