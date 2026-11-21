using System;

namespace Sinnoh.Fx
{
    /// <summary>주사위 굴림 재생이 쓰는 최소 벡터(엔진 없음). 좌표·곱 순서는 Unity와 같다(x 오른쪽 · y 위 · z 안쪽).</summary>
    public readonly struct V3
    {
        public readonly float X, Y, Z;

        public V3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly V3 Right = new V3(1, 0, 0), Up = new V3(0, 1, 0), Forward = new V3(0, 0, 1);

        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
        public static V3 operator *(V3 a, float s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>단위 사원수(회전). <c>a * b</c> = b를 먼저, a를 나중에(Unity와 같다).</summary>
    public readonly struct Q4
    {
        public readonly float X, Y, Z, W;

        public Q4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static readonly Q4 Identity = new Q4(0, 0, 0, 1);

        public static Q4 operator *(Q4 a, Q4 b) => new Q4(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static V3 operator *(Q4 q, V3 v)
        {
            // v + 2w(u × v) + 2u × (u × v)
            float cx = q.Y * v.Z - q.Z * v.Y, cy = q.Z * v.X - q.X * v.Z, cz = q.X * v.Y - q.Y * v.X;
            float dx = q.Y * cz - q.Z * cy, dy = q.Z * cx - q.X * cz, dz = q.X * cy - q.Y * cx;
            return new V3(v.X + 2 * (q.W * cx + dx), v.Y + 2 * (q.W * cy + dy), v.Z + 2 * (q.W * cz + dz));
        }

        public Q4 Normalized
        {
            get
            {
                float m = (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
                return m < 1e-9f ? Identity : new Q4(X / m, Y / m, Z / m, W / m);
            }
        }

        public static Q4 AngleAxis(float degrees, V3 axis)
        {
            double h = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(h);
            return new Q4(axis.X * s, axis.Y * s, axis.Z * s, (float)Math.Cos(h));
        }

        /// <summary>구면 보간(가까운 쪽으로).</summary>
        public static Q4 Slerp(Q4 a, Q4 b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0)
            {
                b = new Q4(-b.X, -b.Y, -b.Z, -b.W);
                dot = -dot;
            }
            float wa, wb;
            if (dot > 0.9995f)
            {
                wa = 1 - t;
                wb = t;
            }
            else
            {
                double th = Math.Acos(dot), s = Math.Sin(th);
                wa = (float)(Math.Sin((1 - t) * th) / s);
                wb = (float)(Math.Sin(t * th) / s);
            }
            return new Q4(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb).Normalized;
        }

        /// <summary>같은 회전인가(q와 −q는 같다).</summary>
        public static bool Same(Q4 a, Q4 b, float eps = 1e-3f) => Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W) > 1 - eps;

        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }
}
