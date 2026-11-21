using System;
using System.Collections.Generic;

namespace Sinnoh.Fx
{
    /// <summary>칸 헥스 프리즘 하나(보드 유닛): 가운데 (X, Z), 밑면 ~ 윗면 높이. 화면 쪽이 보드 배치(<c>Sinnoh.Data.BoardLayout</c>)에서 만든다.</summary>
    public struct HexPrism
    {
        public double X, Z, Bottom, Top;
        /// <summary>지금 고를 수 없는 칸(보이지 않는 깨어진 세계).</summary>
        public bool Hidden;
    }

    /// <summary>
    /// 광선 → 칸(화면 좌표로 칸 고르기의 순수 부분, 04 §3-4 S19 — 엔진 무관, dotnet 시험이 351칸을 모두 본다).
    /// 칸 = flat-top 헥스 프리즘(꼭짓점이 ±x). 광선이 가장 먼저 닿는 프리즘의 칸을 준다 — 윗면뿐 아니라 옆면에 닿아도 그 칸이다
    /// (높은 칸의 옆면 뒤에 있는 낮은 칸은 화면에서도 가려져 있다).
    /// 프리즘과 광선의 만남은 평행한 변 세 쌍(위에서 본 육각형) + 높이 한 쌍, 네 띠의 구간을 겹쳐 셈한다(걸음을 나눠 훑는 어림이 아니다).
    /// 화면 계산이라 부동소수를 쓴다 — 규칙 상태에는 닿지 않는다(고른 칸은 엔진의 합법 답과 견준 뒤에만 답이 된다).
    /// </summary>
    public static class HexPick
    {
        public const int NoCell = -1;

        static readonly double Sqrt3Half = Math.Sqrt(3.0) / 2.0;
        // 변에 수직인 방향 셋(x, z): 위아래 변 · 오른쪽 위 변 · 오른쪽 아래 변
        static readonly double[] Nx = { 0.0, Sqrt3Half, Sqrt3Half };
        static readonly double[] Nz = { 1.0, 0.5, -0.5 };

        /// <summary>
        /// 점 (ox, oy, oz)에서 방향 (dx, dy, dz)로 가는 광선이 가장 먼저 닿는 칸(prisms의 색인). 없으면 <see cref="NoCell"/>.
        /// radius = 헥스 외접 반지름. distance = 닿은 곳까지의 광선 매개변수(방향 벡터 길이 단위, 못 닿으면 +∞).
        /// </summary>
        public static int Cell(IReadOnlyList<HexPrism> prisms, double radius, double ox, double oy, double oz, double dx, double dy, double dz, out double distance)
        {
            if (prisms == null) throw new ArgumentNullException(nameof(prisms));
            double apothem = radius * Sqrt3Half;
            int best = NoCell;
            distance = double.PositiveInfinity;
            for (int c = 0; c < prisms.Count; c++)
            {
                var p = prisms[c];
                if (p.Hidden) continue;
                double t0 = 0.0, t1 = double.PositiveInfinity;
                double half = (p.Top - p.Bottom) * 0.5;
                if (!Slab(oy - (p.Bottom + half), dy, half, ref t0, ref t1)) continue;
                double rx = ox - p.X, rz = oz - p.Z;
                bool hit = true;
                for (int k = 0; k < 3 && hit; k++)
                    hit = Slab(rx * Nx[k] + rz * Nz[k], dx * Nx[k] + dz * Nz[k], apothem, ref t0, ref t1);
                if (!hit || t0 >= distance) continue;
                distance = t0;
                best = c;
            }
            return best;
        }

        // 띠 |s + v·t| ≤ h 와 구간 [t0, t1]의 겹침
        static bool Slab(double s, double v, double h, ref double t0, ref double t1)
        {
            if (Math.Abs(v) < 1e-12) return Math.Abs(s) <= h;
            double a = (-h - s) / v, b = (h - s) / v;
            if (a > b) { double swap = a; a = b; b = swap; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }
    }
}
