using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「웹 손님」(02 §2-1 · §2-2 · §8 [가정 M7-59~]): 망 · UGS · cloudflared 없이 —
    /// ① 호스트의 페이지 서버 <see cref="WebGuestServer"/>(임시 폴더 · 루프백): index.html · <c>.gz</c>의 gzip 머리 · ETag/304 · 404 · 폴더 밖 403 · HEAD · 405 · /alive · 포트가 차 있으면 다음 포트,
    /// ② [직접 연결] 방: 손님판 폴더가 있으면 페이지 서버가 함께 열리고 로비에 웹 링크 + [복사], 없으면 「웹 손님판이 없습니다.」(NET_NO_WEBGL — 방은 그대로),
    /// ③ [인터넷] 방(가짜 터널): Relay가 실패하면 게임을 루프백에 열고 두 번째 터널 → 로비에 「참가 주소 wss://…」 + 웹 링크 <c>?join=</c>, 터널 도구가 없는 길은 <c>RoomFlowTests</c>(직접 연결 안내),
    /// ④ 웹 손님판 시작: 페이지 주소(<c>?code=</c> · 직접 연결 페이지)로 참가 칸을 채운 S06.
    /// 포트는 루프백에만 연다. 클립보드는 가짜로 바꿔 끼운다(사용자의 클립보드를 건드리지 않는다).
    /// </summary>
    public sealed class WebGuestFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class FakeTunnel : ITunnel
        {
            public TunnelState State { get; set; }
            public string Url { get; set; }
            public string Next;
            public bool Disposed;

            public void Tick(long nowMs)
            {
                if (Next == null) return;
                Url = Next;
                State = TunnelState.Ready;
                Next = null;
            }

            public void Dispose() => Disposed = true;
        }

        sealed class FakeWeb : IGuestWeb
        {
            public Func<string> Loopback;
            public readonly FakeTunnel Page = new FakeTunnel(), Game = new FakeTunnel();
            public bool Disposed;
            public string PageMissing => null;
            public bool CanTunnel => true;
            public ITunnel OpenPage() => Page;
            public string StartGameLoopback() => Loopback();
            public ITunnel OpenGame() => Game;
            public void Dispose() => Disposed = true;
        }

        sealed class DownRelay : IRelayHost
        {
            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail) => fail(OnlineFail.RelayAuth);
        }

        readonly List<string> errors = new List<string>();
        ScreenBoot boot;
        RenderTexture rt;
        string temp;

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        // 손님판 폴더 흉내: index.html · Build/app.wasm.gz · StreamingAssets/content/board.gz
        string MakeWebRoot()
        {
            temp = Path.Combine(Application.temporaryCachePath, "sinnoh-webguest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(temp, "Build"));
            Directory.CreateDirectory(Path.Combine(temp, "StreamingAssets", "content"));
            File.WriteAllText(Path.Combine(temp, "index.html"), "<html>신오</html>", new UTF8Encoding(false));
            foreach (var f in new[] { Path.Combine("Build", "app.wasm.gz"), Path.Combine("StreamingAssets", "content", "board.gz") })
                using (var file = File.Create(Path.Combine(temp, f)))
                using (var gz = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
                    gz.Write(Encoding.ASCII.GetBytes("payload-" + f), 0, 8 + f.Length);
            return temp;
        }

        IEnumerator Boot()
        {
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();   // 방 닫기
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length, "방이 남았다");
            Application.logMessageReceived -= Grab;
            if (rt != null)
            {
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
            boot = null;
            errors.Clear();
            if (temp != null && Directory.Exists(temp)) Directory.Delete(temp, true);
            temp = null;
        }

        static IEnumerator RoomGone()
        {
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
        }

        static string Text(VisualElement root, string name) => (root.Q(name) is Label l ? l.text : root.Q(name)?.Q<Label>()?.text)?.Replace("⁠", "");

        sealed class Reply
        {
            public int Status;
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public byte[] Body;
        }

        // 루프백 요청 하나(서버는 요청 하나 = 연결 하나)
        static Reply Ask(int port, string method, string target, string extraHeader = null)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = client.SendTimeout = 5000;
                client.Connect(Loopback, port);
                var stream = client.GetStream();
                // Connection: close — 응답 뒤에 서버가 닫는다(닫힐 때까지 읽는다). 이어 쓰기는 KeepAlive가 본다
                var req = Encoding.ASCII.GetBytes(method + " " + target + " HTTP/1.1\r\nHost: " + Loopback + "\r\nConnection: close\r\n" + (extraHeader != null ? extraHeader + "\r\n" : "") + "\r\n");
                stream.Write(req, 0, req.Length);
                var all = new MemoryStream();
                var buf = new byte[4096];
                int n;
                while ((n = stream.Read(buf, 0, buf.Length)) > 0) all.Write(buf, 0, n);
                var bytes = all.ToArray();
                int split = -1;
                for (int i = 0; i + 3 < bytes.Length; i++)
                    if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') { split = i; break; }
                Assert.That(split, Is.GreaterThan(0), "응답 머리 없음: " + method + " " + target);
                var lines = Encoding.ASCII.GetString(bytes, 0, split).Split(new[] { "\r\n" }, StringSplitOptions.None);
                var reply = new Reply { Status = int.Parse(lines[0].Split(' ')[1]) };
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    reply.Headers[lines[i].Substring(0, colon)] = lines[i].Substring(colon + 1).Trim();
                }
                reply.Body = new byte[bytes.Length - split - 4];
                Array.Copy(bytes, split + 4, reply.Body, 0, reply.Body.Length);
                // 이 PC의 웹 필터(백신 · 광고 막기)가 루프백 http의 text/html 응답을 다시 쓰기도 한다(머리 추가 · chunked로 바꿈) — 본문만 되돌려 본다
                if (reply.Headers.TryGetValue("Transfer-Encoding", out var te) && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) reply.Body = Dechunk(reply.Body);
                return reply;
            }
        }

        static byte[] Dechunk(byte[] body)
        {
            var all = new MemoryStream();
            int at = 0;
            while (at < body.Length)
            {
                int eol = Array.IndexOf(body, (byte)'\n', at);
                if (eol < 0) break;
                int size = Convert.ToInt32(Encoding.ASCII.GetString(body, at, eol - at).Trim().Split(';')[0], 16);
                if (size == 0) break;
                all.Write(body, eol + 1, size);
                at = eol + 1 + size + 2;
            }
            return all.ToArray();
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_웹손님_페이지서버는루프백에서_index와gz의gzip머리_ETag와304_404_403_HEAD_405_alive를내고_포트가차있으면다음포트()
        {
            yield return null;
            string root = MakeWebRoot();
            const int first = 47850;
            WebGuestServer server = null, second = null;
            try
            {
                server = WebGuestServer.Start(root, Loopback, first);
                Assert.IsNotNull(server);
                Assert.AreEqual(first, server.Port);

                var index = Ask(first, "GET", "/");
                Assert.AreEqual((200, "text/html; charset=utf-8", "no-cache"), (index.Status, index.Headers["Content-Type"], index.Headers["Cache-Control"]));
                Assert.AreEqual("<html>신오</html>", Encoding.UTF8.GetString(index.Body));
                Assert.IsFalse(index.Headers.ContainsKey("Content-Encoding"));
                if (index.Headers.TryGetValue("Content-Length", out var length)) Assert.AreEqual(index.Body.Length.ToString(), length);

                var wasm = Ask(first, "GET", "/Build/app.wasm.gz?v=1");
                Assert.AreEqual((200, "application/wasm", "gzip"), (wasm.Status, wasm.Headers["Content-Type"], wasm.Headers["Content-Encoding"]), ".gz = Content-Encoding: gzip + 원래 MIME");
                Assert.AreEqual((0x1f, 0x8b), ((int)wasm.Body[0], (int)wasm.Body[1]), "바이트는 그대로(브라우저가 푼다)");
                Assert.AreEqual(new FileInfo(Path.Combine(root, "Build", "app.wasm.gz")).Length, wasm.Body.Length);
                var bundle = Ask(first, "GET", "/StreamingAssets/content/board.gz");
                Assert.AreEqual((200, "application/octet-stream", "gzip"), (bundle.Status, bundle.Headers["Content-Type"], bundle.Headers["Content-Encoding"]), "Gen 번들");

                // 큰 파일(24 MB — data · wasm · 번들 크기)도 끝까지 같은 바이트로: 보낸 직후 닫으면 중간에 잘렸다(11-18 헤드리스 확인)
                var big = new byte[24 * 1024 * 1024];
                new System.Random(7).NextBytes(big);
                File.WriteAllBytes(Path.Combine(root, "Build", "big.data"), big);
                for (int i = 0; i < 3; i++)
                {
                    var got = Ask(first, "GET", "/Build/big.data");
                    Assert.AreEqual((200, big.Length), (got.Status, got.Body.Length), "큰 파일 " + i);
                    Assert.IsTrue(got.Body[0] == big[0] && got.Body[big.Length / 2] == big[big.Length / 2] && got.Body[big.Length - 1] == big[big.Length - 1]);
                }

                // 연결을 이어 쓴다(브라우저 · cloudflared의 기본): 한 연결로 요청 둘 — 응답마다 길이를 적고 Connection: keep-alive
                using (var client = new TcpClient())
                {
                    client.ReceiveTimeout = client.SendTimeout = 5000;
                    client.Connect(Loopback, first);
                    var stream = client.GetStream();
                    for (int i = 0; i < 2; i++)
                    {
                        var ask = Encoding.ASCII.GetBytes("GET /alive HTTP/1.1\r\nHost: " + Loopback + "\r\n\r\n");
                        stream.Write(ask, 0, ask.Length);
                        var text = new StringBuilder();
                        var buf = new byte[1024];
                        while (!text.ToString().EndsWith("\r\n\r\nok", StringComparison.Ordinal) && !text.ToString().EndsWith("ok\r\n0\r\n\r\n", StringComparison.Ordinal))
                        {
                            int n = stream.Read(buf, 0, buf.Length);
                            Assert.That(n, Is.GreaterThan(0), "이어 쓰는 연결이 닫혔다 " + i);
                            text.Append(Encoding.ASCII.GetString(buf, 0, n));
                        }
                        StringAssert.StartsWith("HTTP/1.1 200", text.ToString());
                        StringAssert.Contains("keep-alive", text.ToString().ToLowerInvariant(), "요청 " + i);
                    }
                }

                string etag = wasm.Headers["ETag"];
                var same = Ask(first, "GET", "/Build/app.wasm.gz", "If-None-Match: " + etag);
                Assert.AreEqual((304, 0, etag), (same.Status, same.Body.Length, same.Headers["ETag"]), "조건부 요청이 맞으면 304");
                Assert.AreEqual(200, Ask(first, "GET", "/Build/app.wasm.gz", "If-None-Match: \"other\"").Status);
                Assert.AreEqual(304, Ask(first, "GET", "/Build/app.wasm.gz", "If-Modified-Since: " + wasm.Headers["Last-Modified"]).Status);

                var head = Ask(first, "HEAD", "/index.html");
                Assert.AreEqual((200, 0), (head.Status, head.Body.Length));
                if (head.Headers.TryGetValue("Content-Length", out var headLength)) Assert.AreEqual(index.Body.Length.ToString(), headLength);
                Assert.AreEqual(404, Ask(first, "GET", "/nope.js").Status);
                Assert.AreEqual(403, Ask(first, "GET", "/../outside.txt").Status, "폴더 밖");
                Assert.AreEqual(405, Ask(first, "POST", "/index.html").Status);
                var alive = Ask(first, "GET", "/alive?t=1");
                Assert.AreEqual((200, "ok", "no-store"), (alive.Status, Encoding.ASCII.GetString(alive.Body), alive.Headers["Cache-Control"]));
                Assert.That(server.Served, Is.GreaterThanOrEqualTo(11));

                second = WebGuestServer.Start(root, Loopback, first);
                Assert.AreEqual(first + 1, second.Port, "포트가 차 있으면 다음 포트(02 §2-2)");
                Assert.AreEqual(200, Ask(first + 1, "GET", "/index.html").Status);
            }
            finally
            {
                server?.Dispose();
                second?.Dispose();
                Directory.Delete(root, true);
                temp = null;
            }
            Assert.Throws<SocketException>(() => Ask(first, "GET", "/"), "닫으면 받지 않는다");
        }

        [UnityTest, NUnit.Framework.Timeout(90000)]
        public IEnumerator M7_웹손님_직접연결방은손님판폴더가있으면페이지서버와웹링크_복사_없으면웹손님판이없습니다_방은그대로()
        {
            yield return Boot();
            const ushort port = 47821;
            var flow = boot.Flow;
            var stage = flow.Stage;
            string copied = null;
            LobbyScreen.Clipboard = t => copied = t;
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            Assert.IsNull(flow.Room.WebRoot, "에디터의 기본값 = 없음(시험이 이 PC의 빌드 유무에 기대지 않는다 [가정 M7-61])");

            // 손님판 폴더 없음: 방은 열리고 알림 한 줄
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            create.SetInternet(false);
            Assert.IsTrue(create.Start());
            Assert.IsInstanceOf<LobbyScreen>(stage.Current);
            Assert.AreEqual((0, WebFail.NoWebGl), (flow.Room.Net.WebPort, flow.Room.Net.WebNotice));
            yield return null;
            yield return null;
            Assert.AreEqual("웹 손님판이 없습니다.", Text(stage.Root, "lobby-web-fail-text"));
            Assert.IsNull(stage.Root.Q("lobby-web-link"));
            flow.Room.Leave();
            yield return RoomGone();

            // 손님판 폴더 있음: 같은 수신 주소에 페이지 서버(8080~8089) + 로비의 웹 링크
            flow.Room.WebRoot = MakeWebRoot();
            flow.Room.OpenCreate();
            create = (RoomCreateScreen)stage.Current;
            create.SetInternet(false);
            Assert.IsTrue(create.Start());
            var net = flow.Room.Net;
            Assert.That(net.WebPort, Is.InRange(OnlineLinks.PagePort, OnlineLinks.PagePort + WebGuestServer.PortTries - 1));
            Assert.IsNull(net.WebNotice);
            Assert.AreEqual(200, Ask(net.WebPort, "GET", "/index.html").Status, "방이 열린 동안 페이지가 나간다");
            yield return null;
            yield return null;
            var lobby = (LobbyScreen)stage.Current;
            var addresses = NetRoom.LocalAddresses();
            if (addresses.Count > 0)
            {
                string link = OnlineLinks.DirectPage(addresses[0].Key, net.WebPort, port);
                Assert.AreEqual(addresses[0].Key + ":" + net.WebPort, Text(stage.Root, "lobby-web-link"), "화면에는 주소:포트만 — 전체 링크는 [복사]");
                StringAssert.EndsWith("?port=" + port, link, "게임 포트가 7777이 아니면 링크에 싣는다");
                Assert.AreEqual("Direct " + addresses[0].Key + ":" + port, JoinTarget.FromPage(link).Kind + " " + JoinTarget.FromPage(link).Text, "그 링크를 연 웹 손님의 참가 칸");
                Assert.IsTrue(lobby.PressCopy("lobby-web-copy"));
                Assert.AreEqual((link, link, "복사함"), (copied, lobby.Copied, stage.Root.Q<Button>("lobby-web-copy").text));
            }
            else Assert.IsNull(stage.Root.Q("lobby-web-link"), "주소가 없는 PC(공유기 · Tailscale 없음)");
            int webPort = net.WebPort;
            flow.Room.Leave();
            Assert.Throws<SocketException>(() => Ask(webPort, "GET", "/"), "방을 닫으면 페이지 서버도 닫는다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        [UnityTest, NUnit.Framework.Timeout(90000)]
        public IEnumerator M7_웹손님_인터넷방Relay가실패해도터널이있으면루프백게임과두번째터널_로비에참가주소wss와웹링크join()
        {
            yield return Boot();
            const ushort port = 47822;
            var flow = boot.Flow;
            var stage = flow.Stage;
            string copied = null;
            LobbyScreen.Clipboard = t => copied = t;
            var web = new FakeWeb();
            web.Loopback = () => flow.Room.Net.Host.Start(port, Loopback) ? null : "NET_PORT_BUSY";
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Board = RoomFlow.NoRooms;
            flow.Room.Relay = new DownRelay();
            flow.Room.Web = web;
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            Assert.IsTrue(flow.Room.Create.Internet);
            Assert.IsTrue(create.Start(), "Relay는 실패했지만 터널로 여는 중");
            Assert.IsInstanceOf<RoomCreateScreen>(stage.Current, "터널 주소를 기다린다");
            Assert.AreEqual((InternetRoom.Phase.Starting, InternetRoom.GameRoute.Tunnel), (flow.Room.Net.Internet.State, flow.Room.Net.Internet.Route));
            web.Game.Next = "https://lazy-dog.trycloudflare.com";
            for (int i = 0; i < 120 && !(stage.Current is LobbyScreen); i++) yield return null;
            Assert.IsInstanceOf<LobbyScreen>(stage.Current, "게임 터널 주소를 받으면 로비");
            yield return null;
            Assert.AreEqual("lazy-dog.trycloudflare.com", Text(stage.Root, "lobby-code"), "화면에는 호스트만 — 전체(wss://…)는 [복사]");
            Assert.AreEqual("참가 주소", Text(stage.Root, "lobby-code-label"));
            Assert.AreEqual("웹 링크를 만드는 중…", Text(stage.Root, "lobby-web-wait"));
            web.Page.Next = "https://quick-fox.trycloudflare.com";
            const string link = "https://quick-fox.trycloudflare.com/?join=lazy-dog.trycloudflare.com";
            for (int i = 0; i < 120 && stage.Root.Q("lobby-web-link") == null; i++) yield return null;
            Assert.AreEqual("quick-fox.trycloudflare.com", Text(stage.Root, "lobby-web-link"));
            Assert.AreEqual("abcdefghij-abcdefghij-abcdefghij-abcdefghij…", LobbyScreen.ShortLink("https://abcdefghij-abcdefghij-abcdefghij-abcdefghij-abcdefghij.trycloudflare.com/?code=Q7K2MZ"), "아주 긴 이름은 뒤를 줄인다");
            var lobby = (LobbyScreen)stage.Current;
            Assert.IsTrue(lobby.PressCopy("lobby-code-copy"));
            Assert.AreEqual("wss://lazy-dog.trycloudflare.com", copied);
            Assert.IsTrue(lobby.PressCopy("lobby-web-copy"));
            Assert.AreEqual(link, copied);
            flow.Room.Leave();
            yield return RoomGone();
            Assert.IsTrue(web.Page.Disposed && web.Game.Disposed && web.Disposed, "방을 닫으면 터널을 끝낸다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_웹손님_페이지주소가참가대상이면참가칸을채운S06을열고_코드없는터널페이지는타이틀그대로()
        {
            yield return Boot();
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.Room.Board = RoomFlow.NoRooms;
            Assert.IsFalse(flow.Room.OpenFromPage("https://quick-fox.trycloudflare.com/"), "https 페이지에 코드가 없다");
            Assert.IsInstanceOf<TitleScreen>(stage.Current);
            Assert.IsTrue(flow.Room.OpenFromPage("https://quick-fox.trycloudflare.com/?code=q7k2mz"));
            Assert.IsInstanceOf<RoomJoinScreen>(stage.Current);
            yield return null;
            Assert.AreEqual("Q7K2MZ", ((RoomJoinScreen)stage.Current).TargetText, "참가 코드가 채워져 있다 — 닉네임만 넣으면 된다");
            flow.OpenTitle();
            Assert.IsTrue(flow.Room.OpenFromPage("http://100.101.102.103:8081/?port=7790"));
            yield return null;
            Assert.AreEqual("100.101.102.103:7790", ((RoomJoinScreen)stage.Current).TargetText, "직접 연결 페이지 = 같은 주소의 게임 포트");
            ((RoomJoinScreen)stage.Current).SetTarget("");   // 참가 칸 글은 실행하는 동안 남는다 — 다음 시험을 위해 비운다
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }
    }
}
