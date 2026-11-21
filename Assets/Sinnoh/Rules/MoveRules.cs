using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 맵 이동 판정(S3_1, A6). 순수 함수만 둔다. 이동력 보정(1막 조무래기 +2, 추종자 +1, 스튜 +3 등)과
    /// 높이를 무시하는 효과(펄기아 운명의 변화, 등산가·스카이 트레이너)는 부르는 쪽이 정한다.
    /// </summary>
    public static class MoveRules
    {
        /// <summary>게임 시작 때 한 턴 이동력(최대 3칸, S3_1 p.3).</summary>
        public const int BaseMovement = 3;

        /// <summary>Reachable에서 못 가는 칸.</summary>
        public const int Unreachable = -1;

        /// <summary>A6: 이웃 칸 from → to가 높이 때문에 막히지 않는가. 오르기·내리기 모두 같다. 이웃인지는 보지 않는다.</summary>
        public static bool ElevationAllows(Cell from, Cell to, ElevationMode mode)
        {
            switch (mode)
            {
                case ElevationMode.Floor: return Math.Abs(to.Floor - from.Floor) < 2;
                case ElevationMode.RawDiff: return Math.Abs(to.LevelQuarters - from.LevelQuarters) < 2 * 4; // level 2.0 = 8/4
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "모르는 ElevationMode");
            }
        }

        /// <summary>
        /// start에서 movement걸음 안에 갈 수 있는 칸마다 최소 걸음 수(start = 0), 못 가면 Unreachable. 길이 = board.Count.
        /// 한 걸음 = 이웃 칸 하나이고 ElevationAllows를 지나야 한다.
        /// </summary>
        public static int[] Reachable(Board board, int start, int movement, ElevationMode mode)
        {
            var steps = new int[board.Count];
            for (int i = 0; i < steps.Length; i++) steps[i] = Unreachable;
            var queue = new int[board.Count];
            int head = 0, tail = 0;
            steps[start] = 0;
            queue[tail++] = start;
            while (head < tail)
            {
                int c = queue[head++];
                if (steps[c] >= movement) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = board.Neighbor(c, d);
                    if (n == Board.NoCell || steps[n] != Unreachable || !ElevationAllows(board[c], board[n], mode)) continue;
                    steps[n] = steps[c] + 1;
                    queue[tail++] = n;
                }
            }
            return steps;
        }

        /// <summary>
        /// start → dest 최단 경로(출발·도착 칸 포함). 넓이 우선이고 이웃은 HexCoord.Neighbors() 방향 순서로 보므로 같은 길이 경로 중 늘 같은 것을 고른다.
        /// movement걸음 안에 못 가면 null.
        /// </summary>
        public static List<int> Path(Board board, int start, int dest, int movement, ElevationMode mode)
        {
            var parent = new int[board.Count];
            var steps = new int[board.Count];
            for (int i = 0; i < steps.Length; i++) steps[i] = Unreachable;
            var queue = new int[board.Count];
            int head = 0, tail = 0;
            steps[start] = 0;
            parent[start] = -1;
            queue[tail++] = start;
            while (head < tail && steps[dest] == Unreachable)
            {
                int c = queue[head++];
                if (steps[c] >= movement) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = board.Neighbor(c, d);
                    if (n == Board.NoCell || steps[n] != Unreachable || !ElevationAllows(board[c], board[n], mode)) continue;
                    steps[n] = steps[c] + 1;
                    parent[n] = c;
                    queue[tail++] = n;
                }
            }
            if (steps[dest] == Unreachable) return null;
            var path = new List<int>(steps[dest] + 1);
            for (int c = dest; c != -1; c = parent[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>
        /// 결정 71 고른 길: steps(출발 칸 뺀 걸음 칸 — 마지막이 도착 칸)가 start에서 한 칸씩 이어지고(이웃 · ElevationAllows) movement걸음 안인가.
        /// 빈 목록은 false. 같은 칸을 다시 밟아도 된다(걸음마다 이동력 1).
        /// </summary>
        public static bool PathAllows(Board board, int start, IReadOnlyList<int> steps, int movement, ElevationMode mode)
        {
            if (steps == null || steps.Count == 0 || steps.Count > movement) return false;
            int at = start;
            foreach (int next in steps)
            {
                if (next < 0 || next >= board.Count || !Adjacent(board, at, next) || !ElevationAllows(board[at], board[next], mode)) return false;
                at = next;
            }
            return true;
        }

        public static bool Adjacent(Board board, int a, int b)
        {
            for (int d = 0; d < Board.Directions; d++)
                if (board.Neighbor(a, d) == b) return true;
            return false;
        }

        /// <summary>
        /// 결정 71 경유 칸: start → vias[0] → … → dest를 구간마다 최단 경로(<see cref="Path"/>)로 잇는다(출발·도착 칸 포함). 걸음 합이 movement를 넘거나
        /// 못 가는 구간이 있으면 null. 경유 칸이 없으면 Path와 같다.
        /// </summary>
        public static List<int> Route(Board board, int start, IReadOnlyList<int> vias, int dest, int movement, ElevationMode mode)
        {
            var route = new List<int> { start };
            int at = start;
            int count = vias != null ? vias.Count : 0;
            for (int i = 0; i <= count; i++)
            {
                int to = i < count ? vias[i] : dest;
                var leg = Path(board, at, to, movement - (route.Count - 1), mode);
                if (leg == null) return null;
                for (int k = 1; k < leg.Count; k++) route.Add(leg[k]);
                at = to;
            }
            return route;
        }

        // ── 좌석마다 다른 이동 조건(M6 묶음 5 「이동」, rulings A36) ──
        // 옛 함수(위)는 그대로다 — 조건이 없으면(MoveTerms.Plain) 아래 함수도 옛 함수를 그대로 부른다(같은 칸·같은 길).

        /// <summary>
        /// 한 좌석의 걷기 조건: 높이 규칙 · hiker B 「you can move onto any tile regardless of its elevation」(AnyElevation) ·
        /// sky_trainer B 「You may move to adjacent tiles that are 2 or more elevations lower … After doing so, you may move up to 4 additional tiles in a row so long as your elevation does not increase」(Descend) ·
        /// worker B의 길(Roads — 「All trainers can move through any number of interconnected Roads for 1 tile move」).
        /// </summary>
        public sealed class MoveTerms
        {
            public ElevationMode Mode;
            public bool AnyElevation;
            public bool Descend;
            /// <summary>길이 놓인 칸(GameState.Roads). 없으면 null·빈 목록.</summary>
            public IReadOnlyList<int> Roads;

            public bool Plain => !AnyElevation && !Descend && (Roads == null || Roads.Count == 0);

            public bool Road(int cell)
            {
                if (Roads == null) return false;
                for (int i = 0; i < Roads.Count; i++)
                    if (Roads[i] == cell) return true;
                return false;
            }
        }

        /// <summary>sky_trainer B: 2층 이상 내려선 뒤 무료로 더 갈 수 있는 칸 수.</summary>
        public const int GlideSteps = 4;

        /// <summary>
        /// 한 걸음 from → to(이웃 칸)의 이동력 값. 못 가면 −1. glide = 남은 활공 칸(0~4), chain = 바로 앞 걸음이 길 → 길이었다(이어진 길은 몇 칸이든 1) — 걸음 뒤 값으로 바꾼다.
        /// 길 → 길: 이어 가는 중이면 0, 아니면 1. 활공 중(높이가 오르지 않는 걸음)은 0. 2층 이상 내려서면(Descend) 그 걸음은 1이고 활공 4칸이 열린다.
        /// </summary>
        public static int StepCost(Board board, MoveTerms t, int from, int to, ref int glide, ref bool chain)
        {
            var a = board[from];
            var b = board[to];
            bool plain = ElevationAllows(a, b, t.Mode);
            bool drop = !plain && t.Descend && b.Floor < a.Floor;
            if (!plain && !drop && !t.AnyElevation) return -1;
            bool road = t.Road(from) && t.Road(to);
            int cost;
            if (glide > 0 && b.LevelQuarters <= a.LevelQuarters)
            {
                cost = 0;
                glide--;
            }
            else
            {
                cost = road && chain ? 0 : 1;
                glide = 0;
            }
            if (drop) glide = GlideSteps;
            chain = road;
            return cost;
        }

        const int StatesPerCell = (GlideSteps + 1) * 2;

        static int StateOf(int cell, int glide, bool chain) => cell * StatesPerCell + glide * 2 + (chain ? 1 : 0);

        /// <summary>값이 0·1인 걸음의 최단 값 찾기(0-1 넓이 우선). cost[상태] = 그 상태까지의 최소 이동력(못 가면 Unreachable), parent[상태] = 앞 상태.</summary>
        static void Search(Board board, int start, int movement, MoveTerms t, int glide, bool chain, out int[] cost, out int[] parent)
        {
            int n = board.Count * StatesPerCell;
            cost = new int[n];
            parent = new int[n];
            for (int i = 0; i < n; i++) cost[i] = Unreachable;
            var done = new bool[n];
            var deque = new LinkedList<int>();
            int s0 = StateOf(start, glide, chain);
            cost[s0] = 0;
            parent[s0] = -1;
            deque.AddFirst(s0);
            while (deque.Count > 0)
            {
                int st = deque.First.Value;
                deque.RemoveFirst();
                if (done[st]) continue;
                done[st] = true;
                int c = st / StatesPerCell;
                int g = st % StatesPerCell / 2;
                bool ch = st % 2 == 1;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int nb = board.Neighbor(c, d);
                    if (nb == Board.NoCell) continue;
                    int g2 = g;
                    bool ch2 = ch;
                    int step = StepCost(board, t, c, nb, ref g2, ref ch2);
                    if (step < 0 || cost[st] + step > movement) continue;
                    int to = StateOf(nb, g2, ch2);
                    if (cost[to] != Unreachable && cost[to] <= cost[st] + step) continue;
                    cost[to] = cost[st] + step;
                    parent[to] = st;
                    if (step == 0) deque.AddFirst(to);
                    else deque.AddLast(to);
                }
            }
        }

        /// <summary>
        /// 조건 t로 start에서 이동력 movement 안에 갈 수 있는 칸마다 최소 이동력 값(start = 0), 못 가면 Unreachable. glide·chain = 지금 그 좌석의 활공 칸 수·길 이어 가기.
        /// 조건이 없으면 옛 <see cref="Reachable(Board, int, int, ElevationMode)"/>와 같다.
        /// </summary>
        public static int[] Reachable(Board board, int start, int movement, MoveTerms t, int glide = 0, bool chain = false)
        {
            if (t.Plain && glide == 0) return Reachable(board, start, movement, t.Mode);
            Search(board, start, movement, t, glide, chain, out var cost, out _);
            var best = new int[board.Count];
            for (int c = 0; c < best.Length; c++)
            {
                best[c] = Unreachable;
                for (int k = 0; k < StatesPerCell; k++)
                {
                    int v = cost[c * StatesPerCell + k];
                    if (v != Unreachable && (best[c] == Unreachable || v < best[c])) best[c] = v;
                }
            }
            return best;
        }

        /// <summary>조건 t로 start → dest의 가장 싼 길(출발·도착 칸 포함). 이동력 movement 안에 못 가면 null. 조건이 없으면 옛 <see cref="Path(Board, int, int, int, ElevationMode)"/>와 같다.</summary>
        public static List<int> Path(Board board, int start, int dest, int movement, MoveTerms t, int glide = 0, bool chain = false)
        {
            if (t.Plain && glide == 0) return Path(board, start, dest, movement, t.Mode);
            Search(board, start, movement, t, glide, chain, out var cost, out var parent);
            int end = -1;
            for (int k = 0; k < StatesPerCell; k++)
            {
                int st = dest * StatesPerCell + k;
                if (cost[st] != Unreachable && (end < 0 || cost[st] < cost[end])) end = st;
            }
            if (end < 0) return null;
            var path = new List<int>();
            for (int st = end; st != -1; st = parent[st]) path.Add(st / StatesPerCell);
            path.Reverse();
            return path;
        }

        /// <summary>
        /// 고른 길(steps = 출발 칸 뺀 걸음 칸)의 이동력 값. 이웃이 아니거나 못 가는 걸음이 있거나 빈 길이면 −1. 같은 칸을 다시 밟아도 된다.
        /// </summary>
        public static int PathCost(Board board, int start, IReadOnlyList<int> steps, MoveTerms t, int glide = 0, bool chain = false)
        {
            if (steps == null || steps.Count == 0) return -1;
            int at = start, total = 0;
            foreach (int next in steps)
            {
                if (next < 0 || next >= board.Count || !Adjacent(board, at, next)) return -1;
                int step = StepCost(board, t, at, next, ref glide, ref chain);
                if (step < 0) return -1;
                total += step;
                at = next;
            }
            return total;
        }
    }
}
