using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「웹 손님」의 엔진·망 무관 몫(02 §2-1 · §2-2 · §8 [가정 M7-59~]): 페이지 주소로 참가 칸 채우기(<see cref="JoinTarget.FromPage"/>), 웹 링크(<see cref="OnlineLinks"/>),
    /// 페이지 서버의 응답 규칙(<see cref="WebFiles"/>), cloudflared 출력 읽기(<see cref="TunnelWatch"/>), [인터넷] 방의 웹 몫(가짜 <see cref="IGuestWeb"/> — 터널 없음 · 실패 · Relay 실패 때 두 번째 터널).
    /// 소켓 · 프로세스는 PlayMode <c>WebGuestFlowTests</c>(루프백)와 관문 밖 실제 터널 확인.
    /// </summary>
    public sealed class WebGuestTests
    {
        sealed class FakeTunnel : ITunnel
        {
            public TunnelState State { get; set; }
            public string Url { get; set; }
            public string Next;      // 다음 Tick에 받을 주소
            public bool Die;         // 다음 Tick에 실패
            public bool Disposed;

            public void Tick(long nowMs)
            {
                if (Die) { State = TunnelState.Failed; Url = null; Die = false; }
                else if (Next != null) { Url = Next; State = TunnelState.Ready; Next = null; }
            }

            public void Dispose() => Disposed = true;
        }

        sealed class FakeWeb : IGuestWeb
        {
            public string PageMissing { get; set; }
            public bool CanTunnel { get; set; } = true;
            public FakeTunnel Page = new FakeTunnel(), Game = new FakeTunnel();
            public string LoopbackFail;
            public int Loopbacks, Pages, Games;
            public bool Disposed;

            public ITunnel OpenPage() { Pages++; return Page; }
            public string StartGameLoopback() { Loopbacks++; return LoopbackFail; }
            public ITunnel OpenGame() { Games++; return Game; }
            public void Dispose() => Disposed = true;
        }

        sealed class Relay : IRelayHost
        {
            public string Fail, Code = "Q7K2MZ";
            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail)
            {
                if (Fail != null) fail(Fail);
                else ok(Code);
            }
        }

        static InternetRoom Open(Relay relay, FakeWeb web, Func<string> start = null)
        {
            var net = new InternetRoom(relay, start ?? (() => null), () => (RoomState)null, null, null, 6, web);
            net.Open();
            return net;
        }

        [Test]
        public void M7_웹손님_페이지주소로참가칸을채운다_code와join링크_http페이지는같은주소의게임포트_https페이지에코드가없으면안채움()
        {
            string Read(string url) => JoinTarget.FromPage(url)?.Kind + " " + JoinTarget.FromPage(url)?.Text;
            Assert.AreEqual("Relay Q7K2MZ", Read("https://abc-def.trycloudflare.com/?code=q7k2mz"));
            Assert.AreEqual("Secure wss://game-xyz.trycloudflare.com", Read("https://abc-def.trycloudflare.com/?join=game-xyz.trycloudflare.com"));
            Assert.AreEqual("Direct 100.101.102.103:7777", Read("http://100.101.102.103:8080/"), "직접 연결 페이지 = 같은 주소의 ws://주소:7777");
            Assert.AreEqual("Direct 192.168.0.5:7790", Read("http://192.168.0.5:8081/?port=7790"));
            Assert.AreEqual("Direct pc.local:7777", Read("http://pc.local:8080/index.html"));
            Assert.IsNull(JoinTarget.FromPage("https://abc-def.trycloudflare.com/"), "https 페이지에서 ws://는 막힌다 — 채우지 않는다");
            Assert.IsNull(JoinTarget.FromPage("http://192.168.0.5:8080/?port=0"));
            Assert.IsNull(JoinTarget.FromPage("http://192.168.0.5:8080/?port=70000"));
            Assert.IsNull(JoinTarget.FromPage("https://abc.trycloudflare.com/?code=toolongcode"), "코드 모양이 아니면 채우지 않는다");
            Assert.IsNull(JoinTarget.FromPage(""));
            Assert.IsNull(JoinTarget.FromPage("unity-webgl"));

            // 링크는 참가 칸이 그대로 읽는다
            Assert.AreEqual("https://p.trycloudflare.com/?code=Q7K2MZ", OnlineLinks.CodeLink("https://p.trycloudflare.com", "Q7K2MZ"));
            Assert.AreEqual("https://p.trycloudflare.com/?join=g.trycloudflare.com", OnlineLinks.JoinLink("https://p.trycloudflare.com/", "g.trycloudflare.com"));
            Assert.AreEqual("http://100.64.0.7:8080/", OnlineLinks.DirectPage("100.64.0.7", 8080, 7777));
            Assert.AreEqual("http://100.64.0.7:8083/?port=7790", OnlineLinks.DirectPage("100.64.0.7", 8083, 7790));
            Assert.AreEqual("Direct 100.64.0.7:7790", Read(OnlineLinks.DirectPage("100.64.0.7", 8083, 7790)), "방장 화면의 링크 → 손님 참가 칸");
            Assert.IsTrue(JoinTarget.TryParse(OnlineLinks.CodeLink("https://p.trycloudflare.com", "Q7K2MZ"), out var t) && t.Kind == JoinKind.Relay);
            Assert.AreEqual("g.trycloudflare.com", OnlineLinks.HostOf("https://g.trycloudflare.com"));
            Assert.IsNull(OnlineLinks.HostOf("???"));
            Assert.Throws<ArgumentException>(() => OnlineLinks.DirectPage("", 8080, 7777));
        }

        [Test]
        public void M7_웹손님_페이지서버규칙_경로는폴더밖을못읽고_gz는gzip머리와원래MIME_ETag와조건부요청304_요청머리읽기()
        {
            string root = Path.Combine(Path.GetTempPath(), "sinnoh-web-" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual(200, WebFiles.Resolve(root, "/Build/a.wasm.gz?x=1", out var path));
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "Build", "a.wasm.gz")), path);
            Assert.AreEqual(200, WebFiles.Resolve(root, "/", out path));
            Assert.AreEqual(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar));
            Assert.AreEqual(403, WebFiles.Resolve(root, "/../secret.txt", out _), "폴더 밖");
            Assert.AreEqual(403, WebFiles.Resolve(root, "/a/%2e%2e/%2e%2e/secret.txt", out _), "풀어 쓴 .. 도");
            Assert.AreEqual(400, WebFiles.Resolve(root, "index.html", out _));
            Assert.AreEqual(400, WebFiles.Resolve(root, null, out _));

            var wasm = WebFiles.Describe("Build/WebGL.wasm.gz", null);
            Assert.AreEqual(("application/wasm", true), (wasm.Type, wasm.Gzip));
            var bundle = WebFiles.Describe("StreamingAssets/content/board.gz", null);
            Assert.AreEqual(("application/octet-stream", true), (bundle.Type, bundle.Gzip), "번들(확장자 없음)");
            var html = WebFiles.Describe("index.html", null);
            Assert.AreEqual(("text/html; charset=utf-8", false), (html.Type, html.Gzip));
            Assert.AreEqual("text/plain; charset=utf-8", WebFiles.Describe("LICENSE.md", null).Type, "배포 문서는 브라우저에서 글로 열린다(plan §5 #534)");
            Assert.IsTrue(WebFiles.Describe("Build/a.data.unityweb", new byte[] { 0x1f, 0x8b }).Gzip);
            Assert.IsFalse(WebFiles.Describe("Build/a.data.unityweb", new byte[] { 0x55, 0x6e }).Gzip, "압축하지 않은 unityweb");
            Assert.AreEqual("application/json; charset=utf-8", WebFiles.Describe("Build/a.symbols.json.gz", null).Type);

            string etag = WebFiles.ETagOf(0x1234, 0xabcdef);
            Assert.AreEqual("\"1234-abcdef\"", etag, "serve-web.mjs와 같은 식");
            Assert.IsTrue(WebFiles.IsNotModified(etag, null, etag, 5000));
            Assert.IsTrue(WebFiles.IsNotModified("\"x\", W/" + etag, null, etag, 5000), "쉼표 목록 · 약한 표시");
            Assert.IsTrue(WebFiles.IsNotModified("*", null, etag, 5000));
            Assert.IsFalse(WebFiles.IsNotModified("\"other\"", 9999999, etag, 5000), "If-None-Match가 있으면 그것만 본다");
            Assert.IsTrue(WebFiles.IsNotModified(null, 5000, etag, 5999), "수정 시각은 초 단위로 자른다");
            Assert.IsFalse(WebFiles.IsNotModified(null, 4000, etag, 5999));
            Assert.IsFalse(WebFiles.IsNotModified(null, null, etag, 5000));

            Assert.IsTrue(WebFiles.TryParseHead("GET /Build/a.js?x HTTP/1.1\r\nHost: 127.0.0.1:8080\r\nif-none-match: \"1-2\"", out var method, out var target, out var headers));
            Assert.AreEqual(("GET", "/Build/a.js?x", "\"1-2\"", "127.0.0.1:8080"), (method, target, headers["If-None-Match"], headers["host"]));
            Assert.IsFalse(WebFiles.TryParseHead("GET /", out _, out _, out _));
            Assert.IsFalse(WebFiles.TryParseHead("GET / HTTP/1.1\r\nbroken line", out _, out _, out _));
            Assert.IsFalse(WebFiles.TryParseHead("", out _, out _, out _));
            Assert.IsTrue(WebFiles.IsAlive("/alive?t=1"));
            Assert.IsFalse(WebFiles.IsAlive("/alive/x"));

            Assert.IsNull(WebFiles.FindRoot(root, false), "WebGL/index.html이 없다 → NET_NO_WEBGL");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "WebGL"));
                File.WriteAllText(Path.Combine(root, "WebGL", "index.html"), "x");
                Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "WebGL")), WebFiles.FindRoot(root, false));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void M7_웹손님_터널출력읽기_주소를찾으면Ready_API주소는아님_30초안에없으면실패_끝나면실패()
        {
            Assert.AreEqual("https://quick-brown-fox.trycloudflare.com", TunnelWatch.FindUrl("2026-11-18T01:02:03Z INF |  https://Quick-Brown-Fox.trycloudflare.com   |"));
            Assert.IsNull(TunnelWatch.FindUrl("failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\": EOF"), "API 주소는 터널 주소가 아니다");
            Assert.IsNull(TunnelWatch.FindUrl(null));
            Assert.AreEqual("tunnel --no-autoupdate --url http://127.0.0.1:8081", TunnelWatch.Arguments(8081));

            var w = new TunnelWatch();
            Assert.IsFalse(w.Tick(0));
            w.Scan("INF Requesting new quick Tunnel on trycloudflare.com...");
            Assert.IsFalse(w.Tick(1000));
            w.Scan("INF |  https://abc-def.trycloudflare.com  |");
            w.Scan("INF |  https://later.trycloudflare.com  |");
            Assert.IsTrue(w.Tick(2000));
            Assert.AreEqual((TunnelState.Ready, "https://abc-def.trycloudflare.com"), (w.State, w.Url), "첫 주소");
            Assert.IsFalse(w.Tick(60000), "받은 뒤에는 시간 제한이 없다");
            w.Exited();
            Assert.IsTrue(w.Tick(61000));
            Assert.AreEqual(TunnelState.Failed, w.State, "받은 뒤 끝나면 주소가 사라진다");

            var slow = new TunnelWatch();
            slow.Tick(100);
            Assert.IsFalse(slow.Tick(100 + TunnelWatch.TimeoutMs - 1));
            Assert.IsTrue(slow.Tick(100 + TunnelWatch.TimeoutMs));
            Assert.AreEqual(TunnelState.Failed, slow.State, "30초");
            slow.Scan("https://late.trycloudflare.com");
            Assert.IsFalse(slow.Tick(99999), "실패 뒤에는 바뀌지 않는다");

            var stopped = new TunnelWatch();
            stopped.Stop();
            Assert.AreEqual(TunnelState.Stopped, stopped.State);
            var none = new TunnelWatch();
            none.FailNow();
            none.Stop();
            Assert.AreEqual(TunnelState.Failed, none.State);
        }

        [Test]
        public void M7_웹손님_인터넷방의웹링크_터널주소를받으면code링크_손님판이없거나도구가없으면알림만_터널실패도방은그대로()
        {
            var web = new FakeWeb();
            var net = Open(new Relay(), web);
            Assert.AreEqual((InternetRoom.Phase.Ready, InternetRoom.GameRoute.Relay, "Q7K2MZ", "Q7K2MZ"), (net.State, net.Route, net.JoinCode, net.JoinText));
            Assert.AreEqual((1, 0, (string)null, (string)null), (web.Pages, web.Games, net.GuestLink, net.Notice), "터널 주소를 기다리는 중");
            web.Page.Next = "https://page-abc.trycloudflare.com";
            int changed = 0;
            net.Changed += () => changed++;
            net.Tick(1000);
            Assert.AreEqual("https://page-abc.trycloudflare.com/?code=Q7K2MZ", net.GuestLink);
            Assert.AreEqual(1, changed);
            net.Tick(2000);
            Assert.AreEqual(1, changed, "바뀐 때만 알린다");
            web.Page.Die = true;
            net.Tick(3000);
            Assert.AreEqual((InternetRoom.Phase.Ready, (string)null, WebFail.Tunnel, "Q7K2MZ"), (net.State, net.GuestLink, net.Notice, net.JoinCode), "페이지 터널이 끊겨도 게임은 Relay로 이어진다");
            net.Dispose();
            Assert.IsTrue(web.Page.Disposed && web.Disposed, "방을 닫으면 터널 · 페이지 서버를 끝낸다");

            foreach (var missing in new[] { WebFail.NoWebGl, WebFail.NoCloudflared })
            {
                var none = new FakeWeb { PageMissing = missing };
                var room = Open(new Relay(), none);
                Assert.AreEqual((InternetRoom.Phase.Ready, missing, 0, (string)null), (room.State, room.Notice, none.Pages, room.GuestLink), "웹 링크만 빼고 방은 연다");
            }

            var broken = new FakeWeb();
            broken.Page.State = TunnelState.Failed;   // 페이지 서버를 못 열었다
            var b = Open(new Relay(), broken);
            Assert.AreEqual((InternetRoom.Phase.Ready, WebFail.Tunnel), (b.State, b.Notice));

            var plain = new InternetRoom(new Relay(), () => null, () => null, null, null);
            plain.Open();
            Assert.AreEqual((InternetRoom.Phase.Ready, (string)null, (string)null), (plain.State, plain.Notice, plain.GuestLink), "웹 몫이 없는 방(시험 · 에디터)");
            CollectionAssert.IsSubsetOf(new[] { WebFail.Tunnel, WebFail.NoWebGl, WebFail.NoCloudflared }, NetFailText.Codes, "알림 코드마다 문구");
        }

        [Test]
        public void M7_웹손님_Relay가실패하면게임도두번째터널_PC손님은wss주소_웹은join링크_도구가없거나터널이실패하면직접연결안내()
        {
            var web = new FakeWeb();
            int relayStarts = 0;
            var net = Open(new Relay { Fail = OnlineFail.RelayAuth }, web, () => { relayStarts++; return null; });
            Assert.AreEqual((InternetRoom.Phase.Starting, InternetRoom.GameRoute.Tunnel, 1, 1, 0), (net.State, net.Route, web.Loopbacks, web.Games, relayStarts), "게임을 루프백에 열고 두 번째 터널을 기다린다");
            Assert.IsNull(net.JoinText);
            web.Game.Next = "https://game-xyz.trycloudflare.com";
            net.Tick(1000);
            Assert.AreEqual((InternetRoom.Phase.Ready, "wss://game-xyz.trycloudflare.com", OnlineFail.RelayAuth, (string)null), (net.State, net.JoinText, net.RelayFailCode, net.JoinCode));
            Assert.IsTrue(JoinTarget.TryParse(net.JoinText, out var pc) && pc.Kind == JoinKind.Secure, "PC판 손님의 참가 칸");
            Assert.IsNull(net.GuestLink, "페이지 터널은 아직");
            Assert.IsNull(net.Listing, "터널 방은 방 목록에 올리지 않는다");
            web.Page.Next = "https://page-abc.trycloudflare.com";
            net.Tick(2000);
            Assert.AreEqual("https://page-abc.trycloudflare.com/?join=game-xyz.trycloudflare.com", net.GuestLink);
            Assert.AreEqual("Secure wss://game-xyz.trycloudflare.com", JoinTarget.FromPage(net.GuestLink).Kind + " " + JoinTarget.FromPage(net.GuestLink).Text, "웹 손님의 참가 칸");
            web.Game.Die = true;
            net.Tick(3000);
            Assert.AreEqual((InternetRoom.Phase.Ready, (string)null, (string)null), (net.State, net.JoinText, net.GuestLink), "연 뒤 게임 터널이 끊기면 주소를 걷는다 [가정 M7-60]");

            var noTool = new FakeWeb { CanTunnel = false, PageMissing = WebFail.NoCloudflared };
            var a = Open(new Relay { Fail = OnlineFail.RelayAuth }, noTool);
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayAuth, true, 0), (a.State, a.FailCode, a.SuggestDirect, noTool.Loopbacks), "도구가 없으면 [직접 연결] 안내(결정 92)");

            var dead = new FakeWeb();
            var d = Open(new Relay { Fail = OnlineFail.RelayAuth }, dead);
            dead.Game.Die = true;
            d.Tick(1000);
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayAuth, true), (d.State, d.FailCode, d.SuggestDirect), "두 번째 터널도 실패");
            Assert.IsTrue(dead.Game.Disposed && dead.Page.Disposed && dead.Disposed);

            var busy = new FakeWeb { LoopbackFail = "NET_PORT_BUSY" };
            var p = Open(new Relay { Fail = OnlineFail.RelayAuth }, busy);
            Assert.AreEqual((InternetRoom.Phase.Failed, "NET_PORT_BUSY", 0), (p.State, p.FailCode, busy.Games));

            var code = Open(new Relay { Fail = OnlineFail.RelayCode }, new FakeWeb { CanTunnel = false });
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayCode), (code.State, code.FailCode));
        }
    }
}
