using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 자동 업데이트 흐름 시험의 도구(결정 161 7-2): RFC 8032 Ed25519 서명(시험 키로 견본 판 정보를 그 자리에서 서명 — 주소에 임의 포트가 들어가니 미리 서명해 둘 수 없다) ·
    /// 127.0.0.1 임의 포트의 작은 HTTP 서버(TcpListener — HttpListener는 URL ACL 때문에 쓰지 않는다): GET · Range 206 · 중간에 끊기 · 404 · 429 · 느린 응답.
    /// 실제 R2 · 네트워크에 나가지 않는다.
    /// </summary>
    static class TestEd25519
    {
        static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        static readonly BigInteger D = Mod(-121665 * Inv(121666));
        static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);

        /// <summary>RFC 8032 §7.1 시험 1 시드(pack.mjs keyFromSeed와 같은 것).</summary>
        public static readonly byte[] Seed = Hex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");

        static BigInteger Mod(BigInteger x)
        {
            var m = x % P;
            return m.Sign < 0 ? m + P : m;
        }

        static BigInteger Inv(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);

        static BigInteger Le(byte[] b, int at, int n)
        {
            var buf = new byte[n + 1];
            Buffer.BlockCopy(b, at, buf, 0, n);
            return new BigInteger(buf);
        }

        struct Pt
        {
            public BigInteger X, Y, Z, T;
        }

        static Pt Add(Pt p, Pt q)
        {
            var a = Mod((p.Y - p.X) * (q.Y - q.X));
            var b = Mod((p.Y + p.X) * (q.Y + q.X));
            var c = Mod(p.T * 2 * D * q.T);
            var d = Mod(p.Z * 2 * q.Z);
            BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
            return new Pt { X = Mod(e * f), Y = Mod(g * h), Z = Mod(f * g), T = Mod(e * h) };
        }

        static Pt Mul(BigInteger s, Pt p)
        {
            var q = new Pt { X = 0, Y = 1, Z = 1, T = 0 };
            while (s.Sign > 0)
            {
                if (!s.IsEven) q = Add(q, p);
                p = Add(p, p);
                s >>= 1;
            }
            return q;
        }

        static Pt Base()
        {
            var y = Mod(4 * Inv(5));
            var x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
            var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(x * x - x2).IsZero) x = Mod(x * SqrtM1);
            if (!x.IsEven) x = P - x;
            return new Pt { X = x, Y = y, Z = 1, T = Mod(x * y) };
        }

        static byte[] Encode(Pt p)
        {
            var zi = Inv(p.Z);
            var x = Mod(p.X * zi);
            var y = Mod(p.Y * zi);
            var b = new byte[32];
            var raw = y.ToByteArray();
            Buffer.BlockCopy(raw, 0, b, 0, Math.Min(32, raw.Length));
            if (!x.IsEven) b[31] |= 0x80;
            return b;
        }

        static byte[] Sha512(params byte[][] parts)
        {
            using (var h = SHA512.Create())
            {
                var all = new MemoryStream();
                foreach (var p in parts) all.Write(p, 0, p.Length);
                return h.ComputeHash(all.ToArray());
            }
        }

        static BigInteger Scalar(byte[] seed, out byte[] prefix)
        {
            var h = Sha512(seed);
            var a = new byte[32];
            Buffer.BlockCopy(h, 0, a, 0, 32);
            a[0] &= 248;
            a[31] &= 127;
            a[31] |= 64;
            prefix = new byte[32];
            Buffer.BlockCopy(h, 32, prefix, 0, 32);
            return Le(a, 0, 32);
        }

        public static byte[] PublicKey(byte[] seed) => Encode(Mul(Scalar(seed, out _), Base()));

        public static byte[] Sign(byte[] seed, byte[] msg)
        {
            var a = Scalar(seed, out var prefix);
            var pub = Encode(Mul(a, Base()));
            var r = Le(Sha512(prefix, msg), 0, 64) % L;
            var rEnc = Encode(Mul(r, Base()));
            var k = Le(Sha512(rEnc, pub, msg), 0, 64) % L;
            var s = (r + k * a) % L;
            var sig = new byte[64];
            Buffer.BlockCopy(rEnc, 0, sig, 0, 32);
            var sRaw = s.ToByteArray();
            Buffer.BlockCopy(sRaw, 0, sig, 32, Math.Min(32, sRaw.Length));
            return sig;
        }

        public static byte[] Hex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>서명한 latest.json 글(<c>{"format":1,"manifest":"…","signature":"…"}</c>).</summary>
        public static string Latest(string manifest)
        {
            string sig = Convert.ToBase64String(Sign(Seed, Encoding.UTF8.GetBytes(manifest)));
            return "{\"format\":1,\"manifest\":\"" + manifest.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"signature\":\"" + sig + "\"}";
        }
    }

    /// <summary>시험 안의 작은 HTTP/1.1 서버(127.0.0.1 임의 포트, 연결 하나에 요청 여럿 — keep-alive).</summary>
    sealed class FakeHttp : IDisposable
    {
        public sealed class Reply
        {
            public int Code = 200;
            public byte[] Body = new byte[0];
            /// <summary>몸을 이만큼만 보내고 연결을 끊는다(-1 = 다 보냄).</summary>
            public int CutAfter = -1;
            /// <summary>머리를 보내기 전에 기다린다(ms).</summary>
            public int DelayMs;
            /// <summary>Range를 받나(200 응답일 때).</summary>
            public bool Ranges = true;
        }

        readonly TcpListener listener;
        readonly Thread thread;
        volatile bool stop;
        readonly object gate = new object();
        readonly List<string> log = new List<string>();

        /// <summary>요청 경로 → 응답(다른 스레드에서 부른다).</summary>
        public Func<string, Reply> Handle;

        public int Port { get; }

        public string Url => "http://127.0.0.1:" + Port + "/";

        public FakeHttp()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            thread = new Thread(Loop) { IsBackground = true, Name = "FakeHttp" };
            thread.Start();
        }

        /// <summary>받은 요청 줄(「경로 range=…」).</summary>
        public List<string> Log
        {
            get { lock (gate) return new List<string>(log); }
        }

        void Loop()
        {
            while (!stop)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                var t = new Thread(() => Serve(c)) { IsBackground = true };
                t.Start();
            }
        }

        const string Crlf = "\r\n";

        // 한 연결에서 요청을 여럿 받는다(HTTP/1.1 keep-alive — 받는 쪽이 연결을 다시 쓰면 닫힌 연결에 보낸 요청이 RST로 끊긴다)
        void Serve(TcpClient c)
        {
            try
            {
                using (c)
                {
                    var s = c.GetStream();
                    var one = new byte[1];
                    while (!stop)
                    {
                        var head = new StringBuilder();
                        while (!head.ToString().EndsWith(Crlf + Crlf, StringComparison.Ordinal))
                        {
                            if (s.Read(one, 0, 1) <= 0) return;
                            head.Append((char)one[0]);
                        }
                        if (!Answer(c, s, head.ToString())) return;
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        // 응답 하나. 중간에 끊었으면 false(연결을 RST로 닫는다)
        bool Answer(TcpClient c, NetworkStream s, string head)
        {
            var lines = head.Split(new[] { Crlf }, StringSplitOptions.RemoveEmptyEntries);
            string path = lines[0].Split(' ')[1];
            string range = null;
            foreach (var l in lines)
                if (l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = l.Substring(6).Trim();
            lock (gate) log.Add(path + (range != null ? " range=" + range : ""));
            var r = Handle?.Invoke(path) ?? new Reply { Code = 404 };
            if (r.DelayMs > 0) Thread.Sleep(r.DelayMs);
            int code = r.Code;
            var body = r.Body;
            string extra = "";
            if (code == 200 && r.Ranges && range != null && range.StartsWith("bytes=", StringComparison.Ordinal) && range.EndsWith("-", StringComparison.Ordinal))
            {
                int from = int.Parse(range.Substring(6, range.Length - 7));
                if (from >= body.Length)
                {
                    code = 416;
                    body = new byte[0];
                    extra = "Content-Range: bytes */" + r.Body.Length + Crlf;
                }
                else
                {
                    code = 206;
                    extra = "Content-Range: bytes " + from + "-" + (r.Body.Length - 1) + "/" + r.Body.Length + Crlf;
                    var part = new byte[body.Length - from];
                    Buffer.BlockCopy(body, from, part, 0, part.Length);
                    body = part;
                }
            }
            string status = code == 200 ? "OK" : code == 206 ? "Partial Content" : code == 404 ? "Not Found" : code == 416 ? "Range Not Satisfiable" : code == 429 ? "Too Many Requests" : "Error";
            var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + status + Crlf + "Content-Type: application/octet-stream" + Crlf + "Content-Length: " + body.Length + Crlf + extra + Crlf);
            s.Write(h, 0, h.Length);
            int n = r.CutAfter >= 0 ? Math.Min(r.CutAfter, body.Length) : body.Length;
            s.Write(body, 0, n);
            s.Flush();
            if (r.CutAfter < 0) return true;
            Thread.Sleep(100);
            c.Client.LingerState = new LingerOption(true, 0);   // RST — 받는 쪽이 끊김으로 본다
            return false;
        }

        public void Dispose()
        {
            stop = true;
            listener.Stop();
        }
    }
}
