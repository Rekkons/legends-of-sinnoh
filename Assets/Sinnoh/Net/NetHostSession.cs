using System;
using System.Text;
using Sinnoh.Data;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// 호스트 PC의 방(02 §1, isekai Net/NetHostSession을 고쳐 옮김): <see cref="HostSession"/>(유일한 진실) + <see cref="NetHostLink"/>(손님 연결) + 호스트 자리의 화면 세션(<see cref="Local"/>).
    /// 호스트 자리도 손님과 같은 길이다: NGO 호스트 클라이언트(clientId 0)로 승인받고 같은 이름 붙은 메시지를 NGO 안 루프백으로 주고받는다 — 호스트 화면은 GameState를 직접 읽지 않는다.
    /// <see cref="Tick"/>(NetRunner가 매 프레임) = 받아 둔 메시지를 넣고(<see cref="NetHostLink.Pump"/>) 방 시계를 진행(<see cref="HostSession.Tick"/>).
    /// 호스트 자리의 LeaveRoom은 방 닫기(<see cref="Close"/>)다. 닫지 않았는데 NGO가 멈추면(전송 실패·다른 코드의 Shutdown) 방을 닫으며 저장하고(HostSession.Close — 마지막 쓰기)
    /// Local을 Failed(게임 중이면 NET_HOST_SAVED, 아니면 NET_LOST)로 둔다 — 자동으로 다시 열지 않는다(02 §6-3).
    /// </summary>
    public sealed class NetHostSession : INetSession, IDisposable
    {
        /// <summary>방을 닫은 뒤 NGO를 내리기까지 기다리는 시간(손님이 있을 때) — 닫힘 이유가 끊김보다 먼저 닿게(02 §6-3, isekai).</summary>
        public const long CloseGraceMs = 1000;

        readonly NetworkManager manager;
        readonly Func<long> clock;
        readonly byte[] hello;
        bool starting, closing, stopped;
        System.Diagnostics.Stopwatch closeWatch;
        NetworkTickSystem closeTicks;

        /// <summary>open은 넘겨받은 링크로 방을 만든다(새 방 = HostSession 생성자, 이어하기 = HostSession.Resume). 방장 Hello의 토큰은 방의 OwnerToken과 같아야 한다. clock = 방의 바깥 시각(NetRunner.NowMs).</summary>
        public NetHostSession(NetworkManager manager, Hello ownerHello, Func<IRoomLink, HostSession> open, Func<long> clock)
        {
            this.manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            if (ownerHello == null) throw new ArgumentNullException(nameof(ownerHello));
            if (open == null) throw new ArgumentNullException(nameof(open));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            hello = Encoding.UTF8.GetBytes(MessageCodec.EncodeHello(ownerHello));
            Link = new NetHostLink(manager, clock);
            Host = open(Link) ?? throw new ArgumentException("방을 만들지 못했다", nameof(open));
            Link.Attach(Host);
            Local = new NetClientSession(manager, true);
            manager.OnTransportFailure += OnTransportFailure;
            manager.OnServerStopped += OnServerStopped;
        }

        public HostSession Host { get; }
        public NetHostLink Link { get; }
        /// <summary>호스트 자리(좌석 · 보기 · 방 상태).</summary>
        public NetClientSession Local { get; }
        public ushort Port { get; private set; }
        public int Seat => Local.Seat;

        /// <summary>호스트 자리가 보낸다. LeaveRoom = 방 닫기.</summary>
        public void Send(RoomMsg msg)
        {
            if (msg is LeaveRoom)
            {
                Close();
                return;
            }
            Local.Send(msg);
        }

        /// <summary>받아 둔 메시지를 넣고 방 시계를 진행한다. 닫았거나 NGO가 스스로 멈춘 뒤에는 넣지 않는다(손님 없이 시계가 흘러 자동 처리·저장이 닫은 방을 덮지 않게).</summary>
        public void Tick(long nowMs)
        {
            if (stopped || closing) return;
            Link.Pump(nowMs);
            Host.Tick(nowMs);
        }

        /// <summary>
        /// 방을 연다(직접 연결 — 기본 0.0.0.0:7777, 시험은 127.0.0.1). 호스트 자리의 승인·Welcome은 이 안에서 끝난다.
        /// false = 포트를 열 수 없음(Local.FailCode = NET_PORT_BUSY) 또는 호스트 자리 승인 실패(Link.HostRejected).
        /// </summary>
        public bool Start(ushort port = NetBootstrap.DefaultPort, string listenAddress = NetBootstrap.ListenAll)
        {
            Link.Open();
            closing = false;
            stopped = false;
            if (closeTicks != null)
            {
                closeTicks.Tick -= CloseTick;
                closeTicks = null;
            }
            manager.NetworkConfig.ConnectionData = hello;
            starting = true;
            bool ok;
            try
            {
                ok = NetBootstrap.StartHost(manager, port, Link.Approve, listenAddress);
            }
            finally
            {
                starting = false;
            }
            if (!ok)
            {
                Link.CloseAll();
                Local.MarkFailed(NetFail.PortBusy);
                return false;
            }
            Port = port;
            if (Local.Status == NetStatus.Connected) return true;
            Debug.LogError("[Net] 호스트 자리를 승인받지 못했다: " + (Link.HostRejected ?? "Welcome 없음"));
            Close();
            return false;
        }

        /// <summary>
        /// [방 닫기]·앱 종료(02 §6-3): 방을 닫고(HostSession.Close — 마지막 쓰기 → 모든 연결에 E_HOST_CLOSED 알림 → 끊기) NGO를 내린다.
        /// 손님이 있었으면 NGO 종료를 <see cref="CloseGraceMs"/> 미룬다(같은 프레임에 내리면 닫힘 이유가 닿기 전에 소켓이 닫혀 손님이 NET_LOST로 본다). 그동안 새 접속은 E_HOST_CLOSED로 거절한다.
        /// 같은 NetworkManager로 다시 열기(Start)는 IsListening이 false가 된 뒤.
        /// </summary>
        public void Close()
        {
            if (closing)
            {
                Local.MarkClosed();
                return;
            }
            closing = true;
            bool guests = manager.IsServer && manager.ConnectedClientsIds.Count > (manager.IsHost ? 1 : 0);
            if (!stopped)
            {
                Link.Pump(clock());
                Host.Close(clock());
            }
            Link.CloseAll();
            if (manager.IsListening && !manager.ShutdownInProgress)
            {
                var ticks = manager.NetworkTickSystem;
                if (guests && ticks != null)
                {
                    manager.ConnectionApprovalCallback = RejectClosed;
                    closeWatch = System.Diagnostics.Stopwatch.StartNew();
                    closeTicks = ticks;
                    ticks.Tick += CloseTick;
                }
                else manager.Shutdown();
            }
            Local.MarkClosed();
        }

        /// <summary>닫는 중이라 NGO 종료를 기다린다(손님에게 닫힘 이유가 닿을 때까지).</summary>
        public bool ShuttingDown => closeTicks != null;

        void CloseTick()
        {
            if (closeWatch.ElapsedMilliseconds < CloseGraceMs) return;
            closeTicks.Tick -= CloseTick;
            closeTicks = null;
            if (manager && manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
        }

        static void RejectClosed(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Approved = false;
            response.Reason = NetCode.HostClosed;
        }

        void OnTransportFailure()
        {
            if (!starting) Stopped("[Net] 방을 닫지 않았는데 전송이 실패했다");
        }

        void OnServerStopped(bool wasHost)
        {
            if (!starting) Stopped("[Net] 방을 닫지 않았는데 NGO 호스트가 멈췄다");
        }

        void Stopped(string why)
        {
            if (closing || stopped || Local.Status == NetStatus.Failed) return;
            Debug.LogWarning(why);
            stopped = true;
            bool inGame = NetClientSession.InRoom(Host.Phase);
            Link.CloseAll();       // 먼저 닫는다 — 멈춘 NGO에 끊기를 부르지 않게
            Host.Close(clock());   // 마지막 쓰기 — 손님에게는 이미 닿지 않는다
            Local.MarkFailed(inGame ? NetFail.HostSaved : NetFail.Lost);
        }

        public void Dispose()
        {
            if (!manager)   // NetworkManager가 먼저 없어졌다 — 방만 닫는다(마지막 쓰기)
            {
                Link.Abandon();
                if (!closing && !stopped) Host.Close(clock());
                closing = true;
                Local.Dispose();
                return;
            }
            Close();
            manager.OnTransportFailure -= OnTransportFailure;
            manager.OnServerStopped -= OnServerStopped;
            Link.Dispose();
            Local.Dispose();
        }
    }
}
