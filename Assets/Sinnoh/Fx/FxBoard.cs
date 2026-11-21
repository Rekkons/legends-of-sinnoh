using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>걷는 말의 한 순간(04 §8-8 11행): 경로의 <see cref="From"/>번째 칸에서 <see cref="To"/>번째 칸으로 <see cref="T"/>(0~1)만큼 갔고, 통 튀는 높이는 <see cref="Hop"/>(0~1, 오르막은 더 높다).</summary>
    public struct WalkPose
    {
        public int From, To;
        public float T;
        public float Hop;
        /// <summary>지금까지 다 디딘 걸음 수(남은 이동력 칩이 하나씩 꺼지는 값).</summary>
        public int Landed;
    }

    /// <summary>
    /// 걷기 시간표(04 §8-8 11행, 결정 53 ①): 예비 → 칸마다 한 걸음(오르막은 더 길다) → 끝 착지. 길이는 방장과 같은 조정 값
    /// (<see cref="PlayTiming.WalkPrepMs"/>·<see cref="PlayTiming.WalkStepMs"/>·<see cref="PlayTiming.WalkUpMs"/>·<see cref="PlayTiming.WalkLandMs"/>)으로 세므로
    /// 합이 늘 <c>PlayTiming.RowMs(Moved, 110)</c>과 같다. 같은 ms면 같은 모습이다(앞 호출에 기대지 않는다).
    /// </summary>
    public static class FxWalk
    {
        /// <summary>오르막 걸음이 더 높이 튀는 배수.</summary>
        public const float UpHop = 1.4f;

        public static WalkPose At(Moved m, int ms)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            int steps = m.Path == null ? 0 : m.Path.Length - 1;
            var pose = new WalkPose();
            if (steps <= 0) return pose;
            int t = ms - PlayTiming.WalkPrepMs.Value;
            if (t < 0) { pose.To = 1; return pose; }
            for (int i = 1; i <= steps; i++)
            {
                bool up = m.Levels != null && i < m.Levels.Length && m.Levels[i] > m.Levels[i - 1];
                int len = PlayTiming.WalkStepMs.Value + (up ? PlayTiming.WalkUpMs.Value : 0);
                if (t < len)
                {
                    float p = len <= 0 ? 1f : t / (float)len;
                    pose.From = i - 1;
                    pose.To = i;
                    pose.T = p;
                    pose.Hop = (float)Math.Sin(Math.PI * p) * (up ? UpHop : 1f);
                    pose.Landed = i - 1;
                    return pose;
                }
                t -= len;
            }
            pose.From = pose.To = steps;
            pose.T = 1f;
            pose.Landed = steps;
            return pose;
        }

        /// <summary>마지막 걸음이 닿는 단계 안 시각(<see cref="At"/>의 Landed가 걸음 수가 되는 첫 ms — 끝 착지가 시작한다). 걸음이 없으면 −1.</summary>
        public static int LandedAt(Moved m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            int steps = m.Path == null ? 0 : m.Path.Length - 1;
            if (steps <= 0) return -1;
            int t = PlayTiming.WalkPrepMs.Value;
            for (int i = 1; i <= steps; i++)
            {
                bool up = m.Levels != null && i < m.Levels.Length && m.Levels[i] > m.Levels[i - 1];
                t += PlayTiming.WalkStepMs.Value + (up ? PlayTiming.WalkUpMs.Value : 0);
            }
            return t;
        }
    }

    /// <summary>주인공 도트 말이 보는 쪽(카메라 기준 4방향, 04 §3-1): 북 = 화면 안쪽(뒷모습), 남 = 화면 앞쪽(앞모습).</summary>
    public enum Facing { North, South, West, East }

    /// <summary>
    /// 주인공 도트 말의 걷기 그림(04 §3-1 양식 ①): 걸음(<see cref="WalkPose"/>)에서 보는 쪽과 걷기 한 바퀴 [선 모습, 걸음 A, 선 모습, 걸음 B]의 자리를 고른다.
    /// 원작 필드처럼 한 칸에 그림 둘이다 — 걸음의 앞 절반은 발을 내민 그림(칸마다 A·B가 번갈아), 뒤 절반은 선 모습. 예비·착지 동안은 선 모습이다.
    /// 같은 걸음이면 같은 그림이다(앞 호출에 기대지 않는다).
    /// </summary>
    public static class FieldWalk
    {
        /// <summary>걷기 한 바퀴의 자리 수.</summary>
        public const int CycleLength = 4;

        /// <summary>
        /// 땅 위에서 가는 쪽 → 보는 쪽: right = 카메라 오른쪽 성분, away = 카메라에서 멀어지는 성분(둘 다 땅 평면). 더 큰 성분을 따르고, 같으면 좌우다. 안 움직이면 앞모습(남).
        /// </summary>
        public static Facing Face(float right, float away)
        {
            if (right == 0f && away == 0f) return Facing.South;
            if (Math.Abs(right) >= Math.Abs(away)) return right >= 0f ? Facing.East : Facing.West;
            return away > 0f ? Facing.North : Facing.South;
        }

        /// <summary>그 걸음의 걷기 자리(0 = 선 모습 · 1 = 걸음 A · 3 = 걸음 B).</summary>
        public static int Cycle(WalkPose pose)
        {
            if (pose.From == pose.To || pose.T <= 0f || pose.T >= 0.5f) return 0;
            return pose.From % 2 == 0 ? 1 : 3;
        }

        /// <summary>그 걸음이 가는 두 칸(경로 색인): 걷는 중이면 지금 걸음, 착지 뒤면 마지막 걸음(끝난 뒤에도 가던 쪽을 본다). 걸음이 없으면 false.</summary>
        public static bool Leg(WalkPose pose, out int from, out int to)
        {
            from = pose.From;
            to = pose.To;
            if (from == to) from = to - 1;
            return from >= 0 && to > from;
        }
    }

    /// <summary>
    /// 아직 부품이 없는 행을 화면 대기열에서 덜어 낸다(M4-3 「화면」 — 화면을 묶음으로 나눠 만드는 동안만 쓴다).
    /// <see cref="FxPlanner"/>는 방장과 같은 길이로 항목을 짠다(P5). 그런데 그 행을 그릴 부품이 아직 없으면 화면은 아무것도 보이지 않은 채 입력만 막힌다 —
    /// 그래서 화면 쪽에서 「지금 그릴 수 있는 행」만 남기고 나머지 단계의 시간을 뺀다. 방장 멈춤(<c>AnimEndMs</c>)은 그대로다(솔로에는 기다릴 남이 없다).
    /// 화면이 다 생기면 이 거르기는 없어져야 한다 — 남은 행은 <c>GameFlow.BuiltRows</c>에서 본다.
    /// </summary>
    public static class FxBuilt
    {
        /// <summary>
        /// 화면이 그리는 행(<c>GameFlow.BuiltRow</c>가 이것이다 — 엔진 없이 소리 표 교차 시험이 같은 목록을 본다): 셋업 1~3행 · 막 카드 4 · 토큰 놓기 5 · 걷기 11 · 공중날기 16 ·
        /// 숨겨진 장소 25 · 회복 8·17 · 귀환 71 · 턴 넘김 101 · 턴 시작 6 · 보호 해제 9 · 규토리 줍기 19 · 동시 공개 99(6·99는 여럿 판만 — <see cref="MultiOnly"/>) · 심장 회복 94(943) · 종막 95·96·97(<see cref="FinaleRows"/>) · 운명 7·14·42~45(<see cref="FateRows"/>) · 배틀 행(<see cref="BattleRows.Built"/>) · 들어가고 나오는 행(<see cref="EnterRows.Built"/>).
        /// </summary>
        public static bool Drawn(int row) => row == 110 || SetupRows.Built(row) || row == ActBeats.Row || row == TokenBeats.Row || row == LiberateBeats.Row || row == FlyBeats.Row || row == SpotBeats.Row
            || row == HealBeats.Row || row == WhiteoutBeats.Row || row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn || BattleRows.Built(row) || EnterRows.Built(row)
            || row == TurnStartBeats.Row || row == ShieldBeats.Row || row == ApricornBeats.Row || row == RevealAllBeats.Row || FinaleRows.Built(row) || FateRows.Built(row) || WorkRows.Built(row) || ShrineRows.Built(row) || DealRows.Built(row) || ResultRows.Board(row)
            || UbRows.Built(row)    // 울트라 버스트 보드 15·82·83행(UbStaging.cs)
            || UbBattleRows.Board(row) // 네크로즈마 페이즈·동전 90행(UbBattleStaging.cs — 배틀 쪽 88~92행은 BattleRows.Built)
            || FallRows.Built(row)     // 탈락·부활 91행(FallStaging.cs — M6 「UB 화면 묶음 2」)
            || CardRows.Built(row); // 트레이너 카드 35·48·49·50행(CardStaging.cs)

        /// <summary>
        /// 좌석이 둘 이상인 판에서만 그리는 행(결정 70 ① A1 나 · ② A2 나): 턴 시작 배너 6행(한 좌석 판은 넘김 띠 + 컷신 — 결정 57) ·
        /// 동시 공개 99행(한 좌석 판은 셋업 1·3행 공개가 같은 순간을 그린다). 한 좌석 판에서는 지금처럼 대기열에서 덜어 낸다.
        /// </summary>
        public static bool MultiOnly(int row) => row == TurnStartBeats.Row || row == RevealAllBeats.Row;

        /// <summary>좌석 수가 seats인 판에서 화면이 그리는 행인가(0 = 모름 — 여럿으로 친다).</summary>
        public static bool DrawnFor(int row, int seats) => Drawn(row) && !(MultiOnly(row) && PlayTiming.IsSolo(seats));

        /// <summary>그 단계에 그릴 것이 있나(<c>GameFlow.Built</c>가 이것이다): 그 판에서 그리는 행이고, 그릴 것이 없는 배틀 단계 · 숨겨진 장소도 조무래기도 아닌 토큰 놓기가 아니다.</summary>
        public static bool Draws(FxStep step, int seats) => step != null && DrawnFor(step.Row, seats) && BattleRows.Draws(step) && EnterRows.Draws(step)
            && (step.Row != TokenBeats.Row || TokenBeats.Grunts(step) != null || SpotDropBeats.Spots(step) != null);

        /// <summary>
        /// built(행)이 false인 단계를 뺀다. 뺀 것이 없으면 항목은 그대로다. 뺐으면: 단계 시작 시각을 당기고, 단서 시각도 그만큼 당기고,
        /// 길이는 남은 단계 합(③ 단계가 남았으면 보기 시간 <see cref="PlayTiming.PlainMs"/>까지는 지킨다 — 원래 길이를 넘지 않게. ② 띠만 남았으면 띠 길이 그대로), 차단 컷신 길이는 남은 컷신 합.
        /// </summary>
        public static void Keep(FxEntry entry, Func<int, bool> built)
        {
            if (built == null) throw new ArgumentNullException(nameof(built));
            KeepSteps(entry, s => built(s.Row));
        }

        /// <summary>단계마다 묻는 꼴: 같은 행이라도 그릴 것이 없는 단계(쓰러진 포켓몬이 물러나기만 하는 66행처럼)를 덜어 낸다.</summary>
        public static void KeepSteps(FxEntry entry, Func<FxStep, bool> built)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (built == null) throw new ArgumentNullException(nameof(built));
            var cut = new List<FxStep>();
            foreach (var s in entry.Steps) if (!built(s)) cut.Add(s);
            if (cut.Count == 0) return;
            entry.Reveal = null;
            for (int i = 0; i < entry.Cues.Count; i++)
            {
                var cue = entry.Cues[i];
                int gone = 0;
                foreach (var s in cut) gone += Math.Max(0, Math.Min(cue.AtMs, s.EndMs) - s.StartMs);
                cue.AtMs -= gone;
                entry.Cues[i] = cue;
            }
            foreach (var s in cut) entry.Steps.Remove(s);
            int t = 0, queue = 0, block = 0;
            foreach (var s in entry.Steps)
            {
                s.StartMs = t;
                t += s.Ms;
                if (s.Layer == FxLayer.Block) block += s.Ms;
                else queue += s.Ms;
            }
            // 보기 시간은 ③ 단계(보드·배틀 액션의 결과)가 남았을 때만 지킨다 — ② 띠만 남은 항목(턴 넘김)은 띠 길이 그대로다(결정 57: 넘김 2초·솔로 1초)
            bool item = false;
            foreach (var s in entry.Steps) if (s.Layer == FxLayer.Item) item = true;
            entry.QueueMs = queue == 0 ? 0 : item ? Math.Max(queue, Math.Min(entry.QueueMs, PlayTiming.PlainMs)) : queue;
            entry.BlockMs = block;
        }

        public static void Keep(IEnumerable<FxEntry> entries, Func<int, bool> built)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var e in entries) Keep(e, built);
        }

        public static void KeepSteps(IEnumerable<FxEntry> entries, Func<FxStep, bool> built)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var e in entries) KeepSteps(e, built);
        }
    }
}
