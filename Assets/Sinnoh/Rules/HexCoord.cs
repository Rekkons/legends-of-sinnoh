using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 보드 칸 좌표. TTS 보드의 doubled 좌표를 그대로 쓴다: col+row가 늘 짝수이고,
    /// 이웃은 (0,±2)·(±1,±1) 여섯 칸이다(Research/inventory.md §1.1, map_cells.csv 351칸에서 확인).
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        static readonly int[] DCol = { 0, 1, 1, 0, -1, -1 };
        static readonly int[] DRow = { 2, 1, -1, -2, -1, 1 };

        public readonly int Col;
        public readonly int Row;

        public HexCoord(int col, int row)
        {
            if (((col + row) & 1) != 0)
                throw new ArgumentException($"doubled 좌표는 col+row가 짝수여야 한다: ({col},{row})");
            Col = col;
            Row = row;
        }

        public IEnumerable<HexCoord> Neighbors()
        {
            for (int i = 0; i < DCol.Length; i++)
                yield return new HexCoord(Col + DCol[i], Row + DRow[i]);
        }

        public bool Equals(HexCoord other) => Col == other.Col && Row == other.Row;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => (Col * 397) ^ Row;
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
        public override string ToString() => $"({Col},{Row})";
    }
}
