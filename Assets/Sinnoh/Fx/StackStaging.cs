using System.Collections.Generic;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>한 칸에 말이 둘 이상 선 묶음 하나(결정 66 ①): 그 칸 · 세워 두는 말의 좌석 · 묶인 좌석들(좌석 번호 순 — 세워 둔 말 포함).</summary>
    public sealed class PieceStack
    {
        public readonly int Cell;
        public readonly int Stand;
        public readonly int[] Seats;

        public PieceStack(int cell, int stand, int[] seats)
        {
            Cell = cell;
            Stand = stand;
            Seats = seats;
        }

        public int Count => Seats.Length;
    }

    /// <summary>
    /// 한 칸에 말 여럿(결정 66 ①, 04 §3-1 「한 칸에 말 여럿」)의 순수 셈: 말은 하나만 세우고 머리 위 묶음 표지 안에서 그 칸의 말들이 한 명씩 돌아가며 보인다.
    /// <list type="bullet">
    /// <item>묶기(<see cref="Group"/>): 판 위에 있고 걷는 중이 아닌 좌석을 칸으로 모아, 둘 이상인 칸마다 묶음 하나. 걷는 중인 말은 묶음에서 빠져 따로 걷고 도착하면 묶인다.</item>
    /// <item>세워 두는 말(<see cref="Stand"/>): 그 칸에 보는 좌석이 있으면 그 말, 없으면 지금 턴인 좌석, 그것도 없으면 좌석 번호가 가장 앞선 말 [가정].</item>
    /// <item>돌아가는 그림(<see cref="Turn"/>·<see cref="Shown"/>): 시각(ms)만 받는다 — 한 명당 <see cref="DwellMs"/>씩, 좌석 번호 순으로. 같은 시각이면 같은 그림이다(캡처·시험).</item>
    /// </list>
    /// </summary>
    public static class PieceStacks
    {
        /// <summary>묶음 표지 안에서 한 명이 머무는 시간(ms) [가정].</summary>
        public static readonly Tunable DwellMs = Tunables.Shared.Define("fx.stackDwellMs", 1200, 200, 10000, TunableScope.Local);

        /// <summary>세워 두는 말의 좌석. seats = 그 칸의 좌석들(비어 있으면 −1).</summary>
        public static int Stand(IReadOnlyList<int> seats, int viewer, int active)
        {
            if (seats == null || seats.Count == 0) return -1;
            int first = seats[0];
            bool mine = false, turn = false;
            foreach (int s in seats)
            {
                if (s < first) first = s;
                if (s == viewer) mine = true;
                if (s == active) turn = true;
            }
            return mine ? viewer : turn ? active : first;
        }

        /// <summary>
        /// 좌석마다의 칸(−1 = 판에 없음)에서 묶음을 만든다(칸 번호 순). moving = 걷는 중인 좌석(묶음에서 뺀다 — null이면 없음).
        /// </summary>
        public static List<PieceStack> Group(IReadOnlyList<int> seatCells, int viewer, int active, ICollection<int> moving = null)
        {
            var byCell = new SortedDictionary<int, List<int>>();
            if (seatCells != null)
                for (int s = 0; s < seatCells.Count; s++)
                {
                    int c = seatCells[s];
                    if (c < 0 || (moving != null && moving.Contains(s))) continue;
                    if (!byCell.TryGetValue(c, out var list)) byCell[c] = list = new List<int>();
                    list.Add(s);
                }
            var stacks = new List<PieceStack>();
            foreach (var kv in byCell)
                if (kv.Value.Count >= 2) stacks.Add(new PieceStack(kv.Key, Stand(kv.Value, viewer, active), kv.Value.ToArray()));
            return stacks;
        }

        /// <summary>그 좌석이 든 묶음(없으면 null).</summary>
        public static PieceStack Of(IReadOnlyList<PieceStack> stacks, int seat)
        {
            if (stacks != null)
                foreach (var st in stacks)
                    if (System.Array.IndexOf(st.Seats, seat) >= 0) return st;
            return null;
        }

        /// <summary>그 칸의 묶음(없으면 null).</summary>
        public static PieceStack At(IReadOnlyList<PieceStack> stacks, int cell)
        {
            if (stacks != null)
                foreach (var st in stacks)
                    if (st.Cell == cell) return st;
            return null;
        }

        /// <summary>그 좌석의 말이 묶음에 가려 서지 않나(묶였고 세워 두는 말이 아니다).</summary>
        public static bool Tucked(IReadOnlyList<PieceStack> stacks, int seat)
        {
            var st = Of(stacks, seat);
            return st != null && st.Stand != seat;
        }

        /// <summary>시각 ms에 count명 가운데 몇째가 보이나(0부터). 한 명당 dwellMs. 음수 시각은 0으로 본다.</summary>
        public static int Turn(int count, long ms, int dwellMs)
        {
            if (count <= 0) return 0;
            if (ms < 0) ms = 0;
            if (dwellMs < 1) dwellMs = 1;
            return (int)(ms / dwellMs % count);
        }

        /// <summary>시각 ms에 그 묶음의 표지 안에 보이는 좌석(한 명당 <see cref="DwellMs"/>).</summary>
        public static int Shown(PieceStack stack, long ms) => stack.Seats[Turn(stack.Count, ms, DwellMs.Value)];
    }
}
