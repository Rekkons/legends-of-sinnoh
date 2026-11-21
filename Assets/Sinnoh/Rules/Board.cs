using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 칸 지형(세이브 덮개 타일 태그). 태그가 없는 칸(깨어진 세계, 호수·천관산 정상 World Token 칸, 본맵 L3 1칸)은 None.
    /// Ruins는 포켓몬 카드 태그에만 있다(신·호수 3신 등 13행, xlsx pokemon.biome). 칸에는 없다.
    /// </summary>
    public enum Biome { None, Plains, Forest, Mountain, Ocean, Ruins }

    /// <summary>
    /// 칸 기후(세이브 덮개 타일 태그). 태그가 없는 칸은 None.
    /// Noble(수호신 폼)·Space(UB·갤럭시단 카드 등)는 포켓몬 카드 태그에만 있다(xlsx pokemon.climate). 칸에는 없다.
    /// </summary>
    public enum Climate { None, Cold, Temperate, Warm, Noble, Space }

    /// <summary>보드 칸 하나(정적 데이터, 01 §2-1). board.json 행 하나다.</summary>
    public sealed class Cell
    {
        public readonly HexCoord Coord;
        /// <summary>level × 4. 원본 level이 0.25 단위(숲 1.25, 호수 0.75)라 정수로 둔다(01 §0 float 금지).</summary>
        public readonly int LevelQuarters;
        public readonly Biome Biome;
        public readonly Climate Climate;
        /// <summary>야생(Wild Area) 칸. 343칸이다(S1_1).</summary>
        public readonly bool Wild;
        /// <summary>깨어진 세계(Distortion World) 칸. 25칸이다.</summary>
        public readonly bool DistortionWorld;
        /// <summary>영문 지역 이름(세이브 Description). 없으면 null. 표시 이름 판별(B2)에 쓰지 않는다 — 숨겨진 장소 지역 분산(R3)에서 같은 지역인지만 본다.</summary>
        public readonly string Region;

        public Cell(HexCoord coord, int levelQuarters, Biome biome, Climate climate, bool wild, bool distortionWorld, string region)
        {
            if (levelQuarters < 0) throw new ArgumentOutOfRangeException(nameof(levelQuarters), levelQuarters, "level은 0 이상");
            Coord = coord;
            LevelQuarters = levelQuarters;
            Biome = biome;
            Climate = climate;
            Wild = wild;
            DistortionWorld = distortionWorld;
            Region = region;
        }

        /// <summary>층(온전한 층 = 쌓인 기둥 수) = floor(level), A6. 숲 덮개 +0.25·호수 0.75는 층이 아니다. LevelQuarters ≥ 0이라 정수 나눗셈이 floor다.</summary>
        public int Floor => LevelQuarters / 4;
    }

    /// <summary>
    /// 보드(정적 데이터, 01 §2-1). 칸은 엔진 안에서 0~Count-1 색인이고 HexCoord와 서로 바꾼다.
    /// 이웃 표는 칸마다 HexCoord.Neighbors() 순서의 6방향이고, 보드 밖이면 NoCell이다.
    /// </summary>
    public sealed class Board
    {
        public const int Directions = 6;
        public const int NoCell = -1;

        readonly Cell[] cells;
        readonly int[] neighbors;
        readonly Dictionary<HexCoord, int> index;

        public Board(IReadOnlyList<Cell> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            this.cells = new Cell[cells.Count];
            index = new Dictionary<HexCoord, int>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i] ?? throw new ArgumentException($"칸 {i}이 null이다", nameof(cells));
                if (index.ContainsKey(c.Coord)) throw new ArgumentException($"칸 {c.Coord}이 두 번 나온다", nameof(cells));
                index.Add(c.Coord, i);
                this.cells[i] = c;
            }

            neighbors = new int[this.cells.Length * Directions];
            for (int i = 0; i < this.cells.Length; i++)
            {
                int d = 0;
                foreach (var n in this.cells[i].Coord.Neighbors())
                    neighbors[i * Directions + d++] = index.TryGetValue(n, out int j) ? j : NoCell;
            }
        }

        public int Count => cells.Length;

        public Cell this[int cell] => cells[cell];

        public bool TryGetIndex(HexCoord coord, out int cell) => index.TryGetValue(coord, out cell);

        /// <summary>칸 cell의 dir(0~5) 방향 이웃 색인. 보드 밖이면 NoCell.</summary>
        public int Neighbor(int cell, int dir)
        {
            if ((uint)dir >= Directions) throw new ArgumentOutOfRangeException(nameof(dir), dir, "방향은 0~5");
            return neighbors[cell * Directions + dir];
        }
    }
}
