using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>판 위 플레이어 말 하나의 자리(화면용, 월드 유닛). Seat = 좌석 색인(0~5), Slot = 칸 안 자리(-1 = 가운데, 0~5 = FigureLayout.SlotOffset), Y = 칸 윗면.</summary>
    public sealed class PieceSpot
    {
        public readonly int Seat;
        public readonly int Cell;
        public readonly int Slot;
        public readonly decimal X;
        public readonly decimal Y;
        public readonly decimal Z;

        public PieceSpot(int seat, int cell, int slot, decimal x, decimal y, decimal z)
        {
            Seat = seat;
            Cell = cell;
            Slot = slot;
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// 플레이어 말을 세울 칸 안 자리(M4-2d, d3-3 §2-3, 04 §3-1 「한 칸에 말이 여럿이면 칸 안 6자리로 벌린다」, 엔진 무관 — dotnet 시험).
    /// 피규어와 같은 6자리(FigureLayout.SlotOffset, 반지름 0.5)를 쓴다. 한 칸 말 n개는 좌석 순서대로:
    /// - 피규어가 없는 칸: n = 1이면 가운데, 아니면 자리 i·6/n(FigureLayout.SlotFor).
    /// - 피규어가 선 칸 [가정 M4-2d]: 피규어가 쓰지 않은 바깥 자리 f개(가운데 피규어는 바깥 자리를 쓰지 않음) 중 i·f/n번째. 말이 f개보다 많으면 피규어 자리와 겹쳐도 SlotFor로 벌린다.
    /// </summary>
    public static class PieceLayout
    {
        public const int MaxSeats = 6;

        /// <summary>
        /// seatCells[i] = 좌석 i의 칸(−1 = 판에 없음). figures = 판에 선 피규어 자리(없으면 null).
        /// 결과 = 판에 있는 좌석만, 좌석 순서.
        /// </summary>
        public static List<PieceSpot> Place(Board board, IReadOnlyList<CellLook> looks, IReadOnlyList<int> seatCells, IReadOnlyList<FigureSpot> figures = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (looks == null || looks.Count != board.Count) throw new ArgumentException("looks는 칸마다 하나", nameof(looks));
            if (seatCells == null) throw new ArgumentNullException(nameof(seatCells));
            if (seatCells.Count > MaxSeats) throw new ArgumentException($"좌석 {seatCells.Count} > {MaxSeats}", nameof(seatCells));

            var count = new int[board.Count];
            for (int s = 0; s < seatCells.Count; s++)
            {
                int c = seatCells[s];
                if (c == -1) continue;
                if (c < 0 || c >= board.Count) throw new ArgumentOutOfRangeException(nameof(seatCells), $"좌석 {s}: 칸 {c}");
                count[c]++;
            }

            var used = new int[board.Count];
            var spots = new List<PieceSpot>();
            for (int s = 0; s < seatCells.Count; s++)
            {
                int c = seatCells[s];
                if (c == -1) continue;
                int slot = SlotFor(used[c]++, count[c], FreeSlots(figures, c));
                FigureLayout.SlotOffset(slot, out decimal dx, out decimal dz);
                spots.Add(new PieceSpot(s, c, slot, looks[c].X + dx, BoardLayout.TopY(board[c]), looks[c].Z + dz));
            }
            return spots;
        }

        /// <summary>
        /// 한 칸 말 n개 중 i번째의 자리. free = 피규어가 선 칸이면 피규어가 쓰지 않은 바깥 자리(0~5 오름차순), 피규어가 없는 칸이면 null.
        /// </summary>
        public static int SlotFor(int i, int n, IReadOnlyList<int> free)
        {
            if (free == null) return FigureLayout.SlotFor(i, n);
            if (n < 1 || n > MaxSeats || i < 0 || i >= n) throw new ArgumentOutOfRangeException(nameof(i), $"{i}/{n}");
            if (n > free.Count) return FigureLayout.SlotFor(i, n);
            return free[i * free.Count / n];
        }

        /// <summary>칸 c에 피규어가 서 있으면 피규어가 쓰지 않은 바깥 자리(오름차순), 없으면 null.</summary>
        public static List<int> FreeSlots(IReadOnlyList<FigureSpot> figures, int c)
        {
            if (figures == null) return null;
            var taken = new bool[6];
            bool any = false;
            foreach (var f in figures)
            {
                if (f.Cell != c) continue;
                any = true;
                if (f.Slot >= 0) taken[f.Slot] = true;
            }
            if (!any) return null;
            var free = new List<int>();
            for (int k = 0; k < 6; k++) if (!taken[k]) free.Add(k);
            return free;
        }

        /// <summary>
        /// 시험 판의 고향 마을 [가정 M4-2d — 실제 고향은 셋업(S12)이 고른 SeatState.HomeVillage]: 좌석 i = fixtures 순서 i번째 마을(고정물 색인).
        /// </summary>
        public static List<int> DemoHomes(FixtureDb fixtures, int seats)
        {
            if (fixtures == null) throw new ArgumentNullException(nameof(fixtures));
            if (seats < 0 || seats > MaxSeats) throw new ArgumentOutOfRangeException(nameof(seats), seats, "0~6");
            var homes = new List<int>();
            for (int f = 0; f < fixtures.Fixtures.Count && homes.Count < seats; f++)
                if (fixtures.Fixtures[f].Kind == FixtureKind.Village) homes.Add(f);
            if (homes.Count < seats) throw new InvalidOperationException($"마을 {homes.Count} < 좌석 {seats}");
            return homes;
        }
    }
}
