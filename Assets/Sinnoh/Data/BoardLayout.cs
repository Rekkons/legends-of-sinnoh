using System;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>칸 프리즘의 층 띠 하나(화면용, 월드 유닛, Bottom &lt; Top). Tint = 그 띠 옆면 틴트 "#rrggbbaa".</summary>
    public readonly struct PrismBand
    {
        public readonly decimal Bottom;
        public readonly decimal Top;
        public readonly string Tint;

        public PrismBand(decimal bottom, decimal top, string tint)
        {
            Bottom = bottom;
            Top = top;
            Tint = tint;
        }
    }

    /// <summary>
    /// 칸 헥스 프리즘 모양(화면용, 엔진 무관 — dotnet 시험이 board.json 351칸을 모두 본다). Client 보드 메시가 쓴다(M4-2b, d3-3 §2-1).
    /// 판 모양 [확인 inventory §1.1]: flat-top(꼭짓점이 ±x), 외접 반지름 1.1, 칸 중심 = board.json (x, z), 층 0.78.
    /// 원작 칸 = 기둥 조각(두께 0.78) n개 + 덮개 하나(CellLook.Pillars = 기둥 틴트 아래 → 위, Tint = 덮개).
    /// 그리기 [가정 M4-2a·M4-2b]: 윗면 y = level × 0.78, 본맵 칸은 밑면 −0.04(탁자 위)부터 기둥을 0.78씩 쌓고 덮개가 윗면까지 채운다
    /// (산은 덮개도 기둥이라 0.82, 평원 0.04, 숲 0.235, 호수 0.625 — 호수 덮개 밑 얇은 조각(0.039)은 덮개에 합쳤다).
    /// 깨어진 세계 칸은 원작처럼 떠 있다: 기둥 하나가 덮개 바로 밑에 있고 그 밑은 비었다(바닥판은 울트라 버스트 화면, M6).
    /// </summary>
    public static class BoardLayout
    {
        public const decimal HexRadius = 1.1m;
        public const decimal LayerHeight = 0.78m;
        public const decimal BaseY = -0.04m;

        /// <summary>윗면 높이 = level × 0.78.</summary>
        public static decimal TopY(Cell cell) => cell.LevelQuarters * LayerHeight / 4;

        /// <summary>층 띠 아래 → 위. 마지막이 덮개(윗면 포함, 틴트 = look.Tint).</summary>
        public static PrismBand[] Bands(Cell cell, CellLook look)
        {
            int n = look.Pillars.Count;
            decimal bottom = cell.DistortionWorld ? BaseY + LayerHeight * (cell.Floor - n) : BaseY;
            var bands = new PrismBand[n + 1];
            for (int i = 0; i < n; i++)
                bands[i] = new PrismBand(bottom + LayerHeight * i, bottom + LayerHeight * (i + 1), look.Pillars[i]);
            decimal coverBottom = bottom + LayerHeight * n;
            decimal top = TopY(cell);
            if (top <= coverBottom) throw new ArgumentException($"칸 {cell.Coord}: 기둥 {n}개 위 덮개 두께가 0 이하(윗면 {top}, 덮개 밑 {coverBottom})");
            bands[n] = new PrismBand(coverBottom, top, look.Tint);
            return bands;
        }
    }
}
