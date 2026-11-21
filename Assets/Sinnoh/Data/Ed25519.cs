using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Sinnoh.Data
{
    /// <summary>
    /// Ed25519 서명 확인(RFC 8032 §5.1.7) — 확인만 한다(서명은 Node 포장 도구 <c>Tools/release/pack.mjs</c>가 한다). 엔진 없음.
    /// 판 정보 파일(<see cref="ReleaseFeed"/>) 하나를 실행마다 한 번 보는 데 쓰니 빠르기보다 짧고 옳게 — BigInteger 기준 구현(확장 좌표 · 완전 덧셈식).
    /// </summary>
    public static class Ed25519
    {
        static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        static readonly BigInteger D = Mod(-121665 * Inv(121666));
        static readonly BigInteger D2 = Mod(2 * D);
        static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
        static readonly Point Base = BasePoint();

        struct Point
        {
            public BigInteger X, Y, Z, T;
            public Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t) { X = x; Y = y; Z = z; T = t; }
        }

        /// <summary>공개 키 32바이트 · 서명 64바이트로 msg를 확인한다. 꼴이 틀리면(길이 · 점이 아님 · S ≥ L) 거짓 — 예외를 내지 않는다.</summary>
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64 || message == null) return false;
            var a = Decode(publicKey, 0);
            var r = Decode(signature, 0);
            if (a == null || r == null) return false;
            var s = Le(signature, 32, 32);
            if (s >= L) return false;
            byte[] hash;
            using (var sha = SHA512.Create())
            {
                var buf = new byte[64 + message.Length];
                Buffer.BlockCopy(signature, 0, buf, 0, 32);
                Buffer.BlockCopy(publicKey, 0, buf, 32, 32);
                Buffer.BlockCopy(message, 0, buf, 64, message.Length);
                hash = sha.ComputeHash(buf);
            }
            var h = Le(hash, 0, 64) % L;
            return Same(Mul(s, Base), Add(r.Value, Mul(h, a.Value)));
        }

        static BigInteger Mod(BigInteger x)
        {
            var m = x % P;
            return m.Sign < 0 ? m + P : m;
        }

        static BigInteger Inv(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);

        // 작은 끝 순서 부호 없는 정수(BigInteger(byte[])는 2의 보수라 0 바이트를 하나 더 붙인다)
        static BigInteger Le(byte[] b, int at, int n)
        {
            var buf = new byte[n + 1];
            Buffer.BlockCopy(b, at, buf, 0, n);
            return new BigInteger(buf);
        }

        static Point Add(Point p, Point q)
        {
            var a = Mod((p.Y - p.X) * (q.Y - q.X));
            var b = Mod((p.Y + p.X) * (q.Y + q.X));
            var c = Mod(p.T * D2 * q.T);
            var d = Mod(p.Z * 2 * q.Z);
            BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
            return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        static Point Mul(BigInteger s, Point p)
        {
            var q = new Point(0, 1, 1, 0);
            while (s.Sign > 0)
            {
                if (!s.IsEven) q = Add(q, p);
                p = Add(p, p);
                s >>= 1;
            }
            return q;
        }

        static bool Same(Point p, Point q) => Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

        static BigInteger? RecoverX(BigInteger y, int sign)
        {
            if (y >= P) return null;
            var x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
            if (x2.IsZero) return sign == 0 ? BigInteger.Zero : (BigInteger?)null;
            var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(x * x - x2).IsZero) x = Mod(x * SqrtM1);
            if (!Mod(x * x - x2).IsZero) return null;
            if ((int)(x & 1) != sign) x = P - x;
            return x;
        }

        static Point? Decode(byte[] b, int at)
        {
            var buf = new byte[32];
            Buffer.BlockCopy(b, at, buf, 0, 32);
            int sign = buf[31] >> 7;
            buf[31] &= 0x7F;
            var y = Le(buf, 0, 32);
            var x = RecoverX(y, sign);
            if (x == null) return null;
            return new Point(x.Value, y, 1, Mod(x.Value * y));
        }

        static Point BasePoint()
        {
            var y = Mod(4 * Inv(5));
            var x = RecoverX(y, 0).Value;
            return new Point(x, y, 1, Mod(x * y));
        }
    }
}
