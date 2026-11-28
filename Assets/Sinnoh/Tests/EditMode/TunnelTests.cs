using System;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// [인터넷] 방의 Relay 실패 때 터널(02 §2-1)의 엔진·망 무관 몫: cloudflared 출력 읽기(<see cref="TunnelWatch"/>), Relay가 실패하면 게임을 루프백에 열고 터널(가짜 <see cref="IGameTunnel"/> — 도구 없음 · 실패).
    /// 소켓 · 프로세스는 PlayMode <c>TunnelFlowTests</c>(루프백).
    /// </summary>
    public sealed class TunnelTests
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

        sealed class FakeGameTunnel : IGameTunnel
        {
            public bool CanTunnel { get; set; } = true;
            public FakeTunnel Game = new FakeTunnel();
            public string LoopbackFail;
            public int Loopbacks, Games;
            public bool Disposed;

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

        static InternetRoom Open(Relay relay, FakeGameTunnel tunnel, Func<string> start = null)
        {
            var net = new InternetRoom(relay, start ?? (() => null), () => (RoomState)null, null, null, 6, tunnel);
            net.Open();
            return net;
        }

        [Test]
        public void M7_터널_출력읽기_주소를찾으면Ready_API주소는아님_30초안에없으면실패_끝나면실패()
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
        public void M7_터널_Relay가실패하면게임을터널로_PC손님은wss주소_도구가없거나터널이실패하면직접연결안내()
        {
            var tunnel = new FakeGameTunnel();
            int relayStarts = 0;
            var net = Open(new Relay { Fail = OnlineFail.RelayAuth }, tunnel, () => { relayStarts++; return null; });
            Assert.AreEqual((InternetRoom.Phase.Starting, InternetRoom.GameRoute.Tunnel, 1, 1, 0), (net.State, net.Route, tunnel.Loopbacks, tunnel.Games, relayStarts), "게임을 루프백에 열고 터널을 기다린다");
            Assert.IsNull(net.JoinText);
            int changed = 0;
            net.Changed += () => changed++;
            tunnel.Game.Next = "https://game-xyz.trycloudflare.com";
            net.Tick(1000);
            Assert.AreEqual((InternetRoom.Phase.Ready, "wss://game-xyz.trycloudflare.com", OnlineFail.RelayAuth, (string)null), (net.State, net.JoinText, net.RelayFailCode, net.JoinCode));
            Assert.AreEqual(1, changed, "주소를 받으면 알린다");
            Assert.IsTrue(JoinTarget.TryParse(net.JoinText, out var pc) && pc.Kind == JoinKind.Secure, "PC판 손님의 참가 칸");
            Assert.IsNull(net.Listing, "터널 방은 방 목록에 올리지 않는다");
            net.Tick(2000);
            Assert.AreEqual(1, changed, "바뀐 때만 알린다");
            tunnel.Game.Die = true;
            net.Tick(3000);
            Assert.AreEqual((InternetRoom.Phase.Ready, (string)null), (net.State, net.JoinText), "연 뒤 게임 터널이 끊기면 주소를 걷는다 [가정 M7-60]");
            net.Dispose();
            Assert.IsTrue(tunnel.Game.Disposed && tunnel.Disposed, "방을 닫으면 터널을 끝낸다");

            var noTool = new FakeGameTunnel { CanTunnel = false };
            var a = Open(new Relay { Fail = OnlineFail.RelayAuth }, noTool);
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayAuth, true, 0), (a.State, a.FailCode, a.SuggestDirect, noTool.Loopbacks), "도구가 없으면 [직접 연결] 안내(결정 92)");

            var dead = new FakeGameTunnel();
            var d = Open(new Relay { Fail = OnlineFail.RelayAuth }, dead);
            dead.Game.Die = true;
            d.Tick(1000);
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayAuth, true), (d.State, d.FailCode, d.SuggestDirect), "터널도 실패");
            Assert.IsTrue(dead.Game.Disposed && dead.Disposed);

            var busy = new FakeGameTunnel { LoopbackFail = "NET_PORT_BUSY" };
            var p = Open(new Relay { Fail = OnlineFail.RelayAuth }, busy);
            Assert.AreEqual((InternetRoom.Phase.Failed, "NET_PORT_BUSY", 0), (p.State, p.FailCode, busy.Games));

            var code = Open(new Relay { Fail = OnlineFail.RelayCode }, new FakeGameTunnel { CanTunnel = false });
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayCode), (code.State, code.FailCode));

            var ok = Open(new Relay(), new FakeGameTunnel());
            Assert.AreEqual((InternetRoom.Phase.Ready, InternetRoom.GameRoute.Relay, "Q7K2MZ", "Q7K2MZ"), (ok.State, ok.Route, ok.JoinCode, ok.JoinText), "Relay가 되면 터널을 쓰지 않는다");
        }
    }
}
