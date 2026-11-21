#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Sinnoh.Data;
using Debug = UnityEngine.Debug;

namespace Sinnoh.Net
{
    /// <summary>
    /// 웹 손님판 정적 서버(02 §2-2, isekai Net/Online/WebGuestServer를 고쳐 옮김): 호스트 PC가 실행 파일 옆 <c>WebGL/</c>을 <c>http://주소:8080/</c>으로 낸다(차 있으면 8081~8089).
    /// isekai의 HttpListener는 localhost 밖 주소에 관리자 권한(URL 예약)이 필요해(02 §11 #10) <b>TcpListener 위의 작은 HTTP/1.1 서버</b>로 바꿨다 —
    /// [인터넷] 방은 루프백(127.0.0.1)에만 열고 cloudflared 터널이 바깥 주소를 준다, [직접 연결] 방은 0.0.0.0에 열어 LAN · Tailscale 주소로 받는다.
    /// 응답 규칙은 Data <see cref="WebFiles"/>(Tools/serve-web.mjs와 같다): <c>.gz</c> = <c>Content-Encoding: gzip</c> + 원래 MIME, <c>ETag</c>(크기 + 수정 시각) · <c>Last-Modified</c> · <c>Cache-Control: no-cache</c>,
    /// 조건부 요청이 맞으면 304. GET · HEAD만(그 밖 405), 폴더면 index.html, 루트 밖 403, 없으면 404, <c>/alive</c> = 200 「ok」. 연결은 이어 쓴다(<c>Connection: keep-alive</c> — 5초 동안 다음 요청이 없으면 닫는다. 손님이 <c>Connection: close</c>를 보내면 그 응답 뒤에 닫는다).
    /// 연결은 스레드 풀에서 처리한다(Unity API를 쓰지 않는다 — 로그만). WebGL 빌드에서는 컴파일하지 않는다.
    /// </summary>
    public sealed class WebGuestServer : IDisposable
    {
        public const int PortTries = 10;   // 8080~8089
        const int ReadTimeoutMs = 10000, SendTimeoutMs = 30000, KeepAliveMs = 5000, CloseWaitMs = 2000, Chunk = 64 * 1024;

        readonly TcpListener listener;
        int closed;

        WebGuestServer(TcpListener listener, string root, IPAddress address, int port)
        {
            this.listener = listener;
            Root = root;
            Address = address;
            Port = port;
        }

        public string Root { get; }
        public IPAddress Address { get; }
        public int Port { get; }
        /// <summary>이 PC에서 여는 주소(시험 · 터널의 원 서버).</summary>
        public string LocalUrl => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/";
        /// <summary>받은 요청 수(시험 · 로그).</summary>
        public int Served => served;
        int served;

        /// <summary>root를 listen(기본 루프백) port부터 연다. 포트가 차 있으면 다음 포트(최대 <see cref="PortTries"/>개). 모두 실패하면 null.</summary>
        public static WebGuestServer Start(string root, string listen = "127.0.0.1", int port = OnlineLinks.PagePort)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root = Path.GetFullPath(root);
            var address = IPAddress.Parse(listen);
            for (int p = port; p < port + PortTries && p <= 65535; p++)
            {
                var l = new TcpListener(address, p);
                l.Server.ExclusiveAddressUse = true;
                try { l.Start(); }
                catch (SocketException e)
                {
                    Debug.Log($"[Net] 웹 손님판 포트 {p}를 열 수 없어 다음 포트로: {e.Message}");
                    continue;
                }
                var s = new WebGuestServer(l, root, address, p);
                s.Next();
                Debug.Log($"[Net] 웹 손님판 서버 {listen}:{p} ← {root}");
                return s;
            }
            Debug.LogWarning($"[Net] 웹 손님판 서버를 열지 못했다(포트 {port}~{port + PortTries - 1})");
            return null;
        }

        void Next()
        {
            if (closed != 0) return;
            try { listener.BeginAcceptTcpClient(OnAccept, null); }
            catch (Exception) { /* 닫힘 */ }
        }

        void OnAccept(IAsyncResult ar)
        {
            TcpClient client;
            try { client = listener.EndAcceptTcpClient(ar); }
            catch (Exception) { return; }   // 닫힘
            Next();
            ThreadPool.QueueUserWorkItem(_ => Handle(client));
        }

        // 연결 하나: 요청을 차례로 받는다(이어 쓰기). 응답마다 길이를 적으므로 손님은 연결을 닫지 않고 다음 요청을 보낸다.
        // 요청마다 닫는 꼴(Connection: close)은 이 PC의 웹 필터(백신 · 광고 막기 — 루프백 http도 거쳐 간다)가 번들 응답을 중간에 붙잡아 멈췄다(11-18 헤드리스 확인) — Node 서버(serve-web.mjs)처럼 이어 쓴다
        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = ReadTimeoutMs;
                    client.SendTimeout = SendTimeoutMs;
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    var buf = new byte[WebFiles.MaxHead];
                    int have = 0;
                    while (closed == 0)
                    {
                        var head = ReadHead(stream, buf, ref have, out bool tooBig);
                        if (head == null && !tooBig) break;   // 손님이 닫음 · 다음 요청 없음
                        Interlocked.Increment(ref served);
                        if (tooBig || !WebFiles.TryParseHead(head, out var method, out var target, out var headers))
                        {
                            Write(stream, tooBig ? 431 : 400, null, null, false, false);
                            break;
                        }
                        // 본문이 있을 수 있는 요청(GET · HEAD 밖 — 405)은 그 응답 뒤에 닫는다(본문을 다음 요청으로 읽지 않게)
                        bool keep = (method == "GET" || method == "HEAD") && !(headers.TryGetValue("Connection", out var conn) && conn.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0);
                        Serve(stream, client.Client, method, target, headers, keep);
                        if (!keep) break;
                        client.ReceiveTimeout = KeepAliveMs;
                    }
                }
                catch (IOException) { /* 손님이 끊음 · 다음 요청이 없어 시간 초과 */ }
                catch (SocketException e) { Debug.Log($"[Net] 웹 손님판 응답 끊김: {e.SocketErrorCode}"); }
                catch (ObjectDisposedException) { }
                catch (Exception e) { Debug.LogWarning("[Net] 웹 손님판 응답 실패: " + e.Message); }
                Finish(client);
            }
        }

        // 보낸 것이 다 나간 뒤에 닫는다: 보내기만 닫고(FIN) 손님이 닫을 때까지(최대 2초) 남은 입력을 버린다.
        // 보낸 직후 바로 닫으면 아직 나가지 않은 바이트가 버려지고 연결이 끊겨(RST) 큰 파일(data · wasm · 번들)이 중간에 잘린다(11-18 헤드리스 확인에서 잡음)
        static void Finish(TcpClient client)
        {
            try
            {
                var s = client.Client;
                if (s == null || !s.Connected) return;
                s.Shutdown(SocketShutdown.Send);
                s.ReceiveTimeout = CloseWaitMs;
                var junk = new byte[1024];
                while (s.Receive(junk) > 0) { }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        // 파일을 조각으로 보낸다 — 조각마다 다 나갈 때까지(Send가 덜 보내면 이어서)
        static void SendFile(Socket socket, string path)
        {
            var buf = new byte[Chunk];
            using (var f = File.OpenRead(path))
            {
                int n;
                while ((n = f.Read(buf, 0, buf.Length)) > 0)
                {
                    int off = 0;
                    while (off < n)
                    {
                        int sent = socket.Send(buf, off, n - off, SocketFlags.None);
                        if (sent <= 0) throw new IOException("보내기 0");
                        off += sent;
                    }
                }
            }
        }

        // 요청 머리 하나(빈 줄까지)를 읽는다 — 그 뒤에 이미 온 바이트(다음 요청)는 buf 앞에 남긴다. 손님이 닫았거나 아무것도 오지 않았으면 null, 머리가 너무 크면 null + tooBig
        static string ReadHead(NetworkStream stream, byte[] buf, ref int have, out bool tooBig)
        {
            tooBig = false;
            int scanned = 0;
            while (true)
            {
                for (int i = Math.Max(0, scanned - 3); i + 3 < have; i++)
                {
                    if (buf[i] != '\r' || buf[i + 1] != '\n' || buf[i + 2] != '\r' || buf[i + 3] != '\n') continue;
                    var head = Encoding.ASCII.GetString(buf, 0, i);
                    int rest = have - (i + 4);
                    Buffer.BlockCopy(buf, i + 4, buf, 0, rest);
                    have = rest;
                    return head;
                }
                scanned = have;
                if (have >= buf.Length) { tooBig = true; return null; }
                int r = stream.Read(buf, have, buf.Length - have);
                if (r <= 0) return null;
                have += r;
            }
        }

        void Serve(NetworkStream stream, Socket socket, string method, string target, Dictionary<string, string> req, bool keep)
        {
            bool head = method == "HEAD";
            if (method != "GET" && !head) { Write(stream, 405, null, null, false, keep); return; }
            if (WebFiles.IsAlive(target))
            {
                var body = Encoding.UTF8.GetBytes(WebFiles.AliveBody);
                Write(stream, 200, new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8", ["Cache-Control"] = "no-store" }, head ? null : body, false, keep, body.Length);
                return;
            }
            int status = WebFiles.Resolve(Root, target, out var path);
            if (status != 200) { Write(stream, status, null, null, false, keep); return; }
            if (Directory.Exists(path)) path = Path.Combine(path, "index.html");
            if (!File.Exists(path))
            {
                var nf = Encoding.UTF8.GetBytes("not found");
                Write(stream, 404, new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" }, head ? null : nf, false, keep, nf.Length);
                return;
            }
            var file = new FileInfo(path);
            long mtime = UnixMs(file.LastWriteTimeUtc);
            var etag = WebFiles.ETagOf(file.Length, mtime);
            var cache = new Dictionary<string, string>
            {
                ["ETag"] = etag,
                ["Last-Modified"] = file.LastWriteTimeUtc.ToString("r", CultureInfo.InvariantCulture),
                ["Cache-Control"] = "no-cache",   // 쓰기 전에 늘 확인(304) — 다시 빌드한 파일을 옛것으로 쓰지 않게
            };
            req.TryGetValue("If-None-Match", out var inm);
            req.TryGetValue("If-Modified-Since", out var ims);
            if (WebFiles.IsNotModified(inm, SinceMs(ims), etag, mtime))
            {
                Write(stream, 304, cache, null, false, keep);
                return;
            }
            var info = WebFiles.Describe(path, HeadBytes(path));
            cache["Content-Type"] = info.Type;
            if (info.Gzip) cache["Content-Encoding"] = "gzip";
            Write(stream, 200, cache, null, true, keep, file.Length);
            if (head) return;
            SendFile(socket, path);
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static long UnixMs(DateTime utc) => (utc.ToUniversalTime().Ticks - Epoch.Ticks) / TimeSpan.TicksPerMillisecond;

        /// <summary>If-Modified-Since(RFC 1123) → 유닉스 ms. 읽을 수 없으면 null.</summary>
        public static long? SinceMs(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? UnixMs(t) : (long?)null;
        }

        static byte[] HeadBytes(string path)
        {
            if (!path.EndsWith(".unityweb", StringComparison.OrdinalIgnoreCase)) return null;
            var b = new byte[2];
            using (var f = File.OpenRead(path))
                return f.Read(b, 0, 2) == 2 ? b : null;
        }

        static readonly Dictionary<int, string> Reasons = new Dictionary<int, string>
        {
            [200] = "OK", [304] = "Not Modified", [400] = "Bad Request", [403] = "Forbidden", [404] = "Not Found", [405] = "Method Not Allowed", [431] = "Request Header Fields Too Large",
        };

        // 상태 줄 + 머리(+ 본문). contentLength가 있으면 그 값, 없고 본문이 없으면 0(304는 길이를 적지 않는다)
        static void Write(Stream stream, int status, Dictionary<string, string> headers, byte[] body, bool bodyFollows, bool keep, long? contentLength = null)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Reasons.TryGetValue(status, out var r) ? r : "Error").Append("\r\n");
            if (headers != null)
                foreach (var kv in headers) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
            if (status != 304)
                sb.Append("Content-Length: ").Append((contentLength ?? (body != null ? body.Length : 0)).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append(keep ? "Connection: keep-alive\r\nKeep-Alive: timeout=5\r\n\r\n" : "Connection: close\r\n\r\n");
            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
            if (body != null && !bodyFollows) stream.Write(body, 0, body.Length);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            try { listener.Stop(); }
            catch (Exception) { /* 이미 닫힘 */ }
        }
    }
}
#endif
