using System;
using System.Runtime.CompilerServices;
using System.Text;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using Unity.Networking.Transport.Relay;
using UnityEngine;

// PlayMode 시험이 시험 주입점(LoseForTest · FakeRelayJoin)을 쓴다. 시험 어셈블리는 Transport를 참조하지 않아 RelayServerData를 쓸 수 없다
[assembly: InternalsVisibleTo("Sinnoh.Tests.PlayMode")]

namespace Sinnoh.Net
{
    public enum NetStatus
    {
        /// <summary>아직 접속하지 않음.</summary>
        Idle,
        /// <summary>처음 접속 시도 중(Welcome 전) — 자리 확인 대기(<see cref="NetClientSession.ClaimWait"/>)도 여기다.</summary>
        Connecting,
        /// <summary>Welcome을 받음 — 좌석이 정해졌다.</summary>
        Connected,
        /// <summary>방(게임 · 결과 · 이어하기 대기)에서 끊김 → 같은 토큰으로 3초마다 다시, 3분까지(02 §6-1).</summary>
        Reconnecting,
        /// <summary>접속 실패 · 거절 · 끊김. 이유는 FailCode.</summary>
        Failed,
        /// <summary>스스로 나감 또는 방을 닫음(호스트 자리).</summary>
        Closed,
    }

    /// <summary>NetRunner가 매 프레임 시각을 넣는 세션(호스트 = <see cref="NetHostSession"/>, 손님 = <see cref="NetClientSession"/>).</summary>
    public interface INetSession
    {
        void Tick(long nowMs);
    }

    /// <summary>손님 쪽 로컬 실패 코드(호스트가 보낸 E_* 말고, 02 §2-5). 화면 문구는 screens.json이 이 코드로 고른다(화면 묶음).</summary>
    public static class NetFail
    {
        /// <summary>주소 형식이 틀림(JoinTarget.TryParse false — 접속 시도 없음).</summary>
        public const string BadAddress = "NET_BAD_ADDRESS";
        /// <summary>처음 접속에 응답 없음 · 이름을 못 풂.</summary>
        public const string Unreachable = "NET_UNREACHABLE";
        /// <summary>접속 뒤 끊김(로비, 또는 방에서 재접속 3분 실패).</summary>
        public const string Lost = "NET_LOST";
        /// <summary>호스트: 포트를 열 수 없음.</summary>
        public const string PortBusy = "NET_PORT_BUSY";
        /// <summary>호스트: 닫지 않았는데 통신이 멈춰 방을 저장함(02 §6-3).</summary>
        public const string HostSaved = "NET_HOST_SAVED";
        /// <summary>[인터넷] UGS 초기화·익명 로그인·Relay·한도 — 화면은 [직접 연결] 안내(결정 92, <see cref="OnlineFail.SuggestDirect"/>).</summary>
        public const string RelayAuth = OnlineFail.RelayAuth;
        /// <summary>[인터넷] 참가 코드가 없거나 틀림 · 방이 닫힘.</summary>
        public const string RelayCode = OnlineFail.RelayCode;
        /// <summary>방 목록(UGS Lobby) 실패 — 방 열기·코드 참가는 그대로.</summary>
        public const string ListFail = OnlineFail.ListFail;
    }

    /// <summary>
    /// 화면 쪽 방 세션 over NGO(02 §1 · §6-1, isekai Net/NetClientSession을 고쳐 옮김 — Relay 참가(<see cref="RelayJoin"/>)는 Net ②). 손님 PC에서 호스트에 붙고, 호스트 PC에서는 NetHostSession이 호스트 자리용으로 쓴다(같은 메시지를 받는다).
    /// - 접속: StartClient 전에 ConnectionData에 Hello를 싣는다(02 §4-6). 거절 사유는 DisconnectReason의 「코드|값」.
    /// - 받기: <see cref="NetHostLink.ToClient"/> → <see cref="MessageCodec"/> → Welcome(좌석)·RoomUpdate(방)·스냅샷·델타(<see cref="StreamReceiver"/>로 되읽어 <see cref="Latest"/> — 02 §4-4)·ClockUpdate·알림·거절.
    ///   델타 기준·해시가 어긋나면 RequestResync를 한 번 보내고 다음 스냅샷을 기다린다([가정 M7-15]의 손님 몫).
    /// - 끊김: 사유 코드(E_*)가 있으면 Failed. 사유 없이 방(게임 · 결과 · 이어하기 대기)에서 끊기면 Reconnecting — 3초마다 같은 토큰으로 다시, 끊긴 때부터 3분이면 Failed(NET_LOST).
    ///   재시도 중 E_DUPLICATE(호스트가 옛 연결을 아직 끊김으로 못 봄)는 계속 시도한다. 로비에서 끊기면 Failed(NET_LOST). 게임 중 방이 닫히면 Failed(E_HOST_CLOSED)이고 [다시 시도](<see cref="Retry"/>)로 재접속 창을 연다.
    /// - 처음 접속의 E_DUPLICATE(새로 고침·재시작 직후): 처음 거절된 때부터 <see cref="DuplicateRetryMs"/> 동안 3초마다 다시(<see cref="ClearingOld"/>).
    /// - 자리 확인 대기(02 §6-3): 모르는 토큰으로 이어하기 대기·게임 중 방에 들어가면 호스트가 Welcome 대신 SEAT_CLAIM 알림만 보낸다 — <see cref="ClaimWait"/>, 상태는 Connecting(재접속 중이면 Reconnecting, 3분 시계는 멈춘다).
    /// - 시간은 <see cref="Tick"/>으로만 들어온다(NetRunner).
    /// </summary>
    public sealed class NetClientSession : INetSession, IDisposable
    {
        public const long RetryMs = 3000;
        public const long GiveUpMs = 180000;
        public const long DuplicateRetryMs = 30000;

        readonly NetworkManager manager;
        readonly bool hostLocal;
        StreamReceiver stream = new StreamReceiver();
        JoinTarget target;
        RelayAttempts<RelayServerData> relay;
        int lookup;
        byte[] hello;
        bool everConnected, live, resyncAsked;
        int seq;
        long? now, lostAt, nextTry, dupSince;

        public NetClientSession(NetworkManager manager) : this(manager, false) { }

        internal NetClientSession(NetworkManager manager, bool hostLocal)
        {
            this.manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            this.hostLocal = hostLocal;
            manager.OnClientStarted += OnStarted;
            if (hostLocal) return;
            manager.OnConnectionEvent += OnConnection;
            manager.OnClientStopped += OnStopped;
        }

        public NetStatus Status { get; private set; }
        public int Seat { get; private set; } = -1;
        /// <summary>받은 마지막 갱신(스냅샷·델타를 되읽은 것 — 보기 사본 전체 + 이벤트 + 시계). 없으면 null.</summary>
        public GameUpdate Latest { get; private set; }
        /// <summary>지금 내 보기(Latest.View). 받을 때마다 새 사본이라 읽기만 한다.</summary>
        public GameState View => Latest?.View;
        /// <summary>마지막 시계(갱신이나 ClockUpdate).</summary>
        public ClockState Clock { get; private set; }
        public RoomState Room { get; private set; }
        public string RoomId { get; private set; }
        public bool IsHost { get; private set; }
        /// <summary>마지막 거절(다음 갱신이 오면 지운다).</summary>
        public RejectedMsg LastRejected { get; private set; }
        /// <summary>Failed의 이유: 호스트가 보낸 E_* 또는 로컬 <see cref="NetFail"/>.</summary>
        public string FailCode { get; private set; }
        /// <summary>「코드|값」의 값(E_VERSION의 「호스트 빌드/내 빌드」, E_FULL의 「4/6」 등).</summary>
        public string FailDetail { get; private set; }
        /// <summary>끊길 때 있던 방 단계(방에 들어가기 전이면 null).</summary>
        public RoomPhase? FailPhase { get; private set; }
        /// <summary>자리 확인 대기 중이면 그 알림(SEAT_CLAIM), 아니면 null.</summary>
        public NoticeMsg ClaimWait { get; private set; }
        /// <summary>처음 접속이 E_DUPLICATE로 막혀 다시 시도하는 중(Connecting 동안만).</summary>
        public bool ClearingOld => dupSince.HasValue && Status == NetStatus.Connecting;
        public JoinTarget Target => target;
        /// <summary>Relay 참가 코드로 JoinAllocation을 하는 쪽. 기본은 RelayLink.Joiner(UGS). Connect 전에 바꾼다.</summary>
        public IRelayJoin<RelayServerData> RelayJoin { get; set; } = RelayLink.Joiner;
        /// <summary>지금까지 시작한 Relay 참가 수(재접속 포함 — 시험·로그).</summary>
        public int RelayTries => relay?.Started ?? 0;
        /// <summary>손님 쪽 스트림(스냅샷·델타 수 · 버린 수 — 시험·로그용).</summary>
        public StreamReceiver Stream => stream;
        /// <summary>보낸 RequestResync 수.</summary>
        public int Resyncs { get; private set; }

        /// <summary>받은 메시지(되읽기 전 그대로 — 화면 알림·로비가 쓴다).</summary>
        public event Action<ServerMsg> Received;
        /// <summary>새 보기를 받았다(스냅샷·델타를 되읽은 갱신).</summary>
        public event Action<GameUpdate> Updated;
        public event Action<NetStatus> StatusChanged;

        // ── 접속 ──

        /// <summary>
        /// 참가 칸에서 읽은 대상(<see cref="JoinTarget.TryParse"/>)에 붙는다. Relay 코드면 참가(JoinAllocation)가 끝난 뒤, 호스트 이름이면(데스크톱) 이름을 푼 뒤 StartClient하므로 true를 돌려주고 결과는 Status로 본다.
        /// false = 바로 실패(FailCode = NET_UNREACHABLE). Relay 참가가 처음에 실패하면 Failed(NET_RELAY_CODE · NET_RELAY_AUTH), 재접속 중이면 코드 없음만 바로 Failed이고 그 밖은 3초 뒤 같은 코드로 다시(isekai 00 §7 #218 ⑦).
        /// </summary>
        public bool Connect(JoinTarget target, Hello hello)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (hostLocal) throw new InvalidOperationException("호스트 자리는 NetHostSession.Start로 연다");
            if (hello == null) throw new ArgumentNullException(nameof(hello));
            if (Status == NetStatus.Connecting || Status == NetStatus.Connected || Status == NetStatus.Reconnecting) throw new InvalidOperationException("이미 접속 중: " + Status);
            relay?.Cancel();
            relay = target.Kind == JoinKind.Relay ? new RelayAttempts<RelayServerData>(RelayJoin) : null;
            this.target = target;
            this.hello = Encoding.UTF8.GetBytes(MessageCodec.EncodeHello(hello));
            stream = new StreamReceiver();
            everConnected = false;
            resyncAsked = false;
            Room = null;
            Latest = null;
            Clock = null;
            ClaimWait = null;
            Seat = -1;
            lostAt = nextTry = dupSince = null;
            FailCode = FailDetail = null;
            FailPhase = null;
            SetStatus(NetStatus.Connecting);
            return Begin();
        }

        /// <summary>[다시 시도](02 §6-1·§6-3): 방에서 끊김(NET_LOST) 또는 게임 중 방 닫힘(E_HOST_CLOSED)으로 실패한 뒤 재접속 창(3분)을 다시 연다. 그 밖의 실패에서는 아무것도 하지 않는다.</summary>
        public void Retry()
        {
            if (hostLocal || Status != NetStatus.Failed || !everConnected) return;
            bool lost = FailCode == NetFail.Lost && InRoom(Room?.Phase);
            bool hostClosed = FailCode == NetCode.HostClosed && InRoom(FailPhase);
            if (!lost && !hostClosed) return;
            FailCode = FailDetail = null;
            FailPhase = null;
            lostAt = now;
            nextTry = now;
            SetStatus(NetStatus.Reconnecting);
        }

        /// <summary>[나가기]: 접속 중이면 LeaveRoom을 보내고 끊는다.</summary>
        public void Leave()
        {
            if (hostLocal) throw new InvalidOperationException("호스트는 NetHostSession.Close로 방을 닫는다");
            if (Status == NetStatus.Connected || ClaimWait != null) Send(new LeaveRoom());
            ClaimWait = null;
            live = false;
            relay?.Cancel();
            if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
            SetStatus(NetStatus.Closed);
        }

        /// <summary>방(게임 · 결과 · 이어하기 대기)에 있나 — 끊기면 다시 붙는 단계.</summary>
        public static bool InRoom(RoomPhase? phase) => phase == RoomPhase.InGame || phase == RoomPhase.Result || phase == RoomPhase.Waiting;

        bool Begin()
        {
            live = true;
            if (target.Kind == JoinKind.Relay)
            {
                relay.Begin(target.Code, data => StartNow(data, null), AttemptFailed);   // 참가가 끝나면 StartClient(재접속도 같은 코드로 새 참가)
                return true;
            }
            if (!NeedsLookup(target.Host)) return StartNow(default, target.Host);
            Lookup(target.Host, ++lookup);   // 시도마다 새로 푼다(재접속 포함)
            return true;
        }

        bool StartNow(RelayServerData data, string address)
        {
            if (!live) return false;
            bool ok = target.Kind == JoinKind.Relay ? NetBootstrap.StartClientRelay(manager, data, hello)
                : target.Kind == JoinKind.Secure ? NetBootstrap.StartClientSecure(manager, target.Host, target.Port, hello, address)
                : NetBootstrap.StartClient(manager, address, target.Port, hello);
            if (ok) return true;
            if (live) OnLost(null);   // 바로 실패도 끊김과 같게
            return false;
        }

        // 데스크톱에서 호스트 이름을 먼저 푼다: Transport 6.6은 이름 풀기가 실패하면 알림 없이 연결을 닫아 NGO가 「접속 중」에 멈춘다(isekai 10-09 확인)
        static bool NeedsLookup(string host) => !System.Net.IPAddress.TryParse(host, out _);

        async void Lookup(string host, int mine)
        {
            string address = null;
            try
            {
                var list = await System.Net.Dns.GetHostAddressesAsync(host);
                foreach (var a in list)
                    if (address == null && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) address = a.ToString();   // IPv4 먼저(IPv6 길이 없는 집 망이 많다)
                if (address == null && list.Length > 0) address = list[0].ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] 호스트 이름 {host}을 풀지 못했다: {e.Message}");
            }
            if (mine != lookup || !live) return;
            if (address == null)
            {
                AttemptFailed(NetFail.Unreachable);
                return;
            }
            StartNow(default, address);
        }

        // NGO를 시작하기 전 단계(Relay 참가·이름 풀기)의 실패. 재접속 중이면 3초 뒤 다시(코드 없음은 바로 Failed), 처음 접속이면 그 코드로 Failed
        void AttemptFailed(string code)
        {
            if (!live) return;
            live = false;
            if (Status == NetStatus.Reconnecting && !OnlineFail.ReconnectFatal(code))
            {
                nextTry = now.HasValue ? now.Value + RetryMs : (long?)null;
                return;
            }
            Fail(code, null);
        }

        // ── 보내기 · 시각 ──

        public void Send(RoomMsg msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (Status != NetStatus.Connected && !(ClaimWait != null && msg is LeaveRoom))   // 자리 확인 대기 중에는 나가기만
            {
                Debug.LogWarning($"[Net] 접속 중이 아니라 보내지 않는다({Status}): {msg.GetType().Name}");
                return;
            }
            NetHostLink.Transmit(manager, NetHostLink.ToHost, NetworkManager.ServerClientId, Encoding.UTF8.GetBytes(MessageCodec.Encode(msg, ++seq)));
        }

        /// <summary>매 프레임 단조 시각 ms(NetRunner). 손님은 재접속 시계만 쓴다.</summary>
        public void Tick(long nowMs)
        {
            if (now.HasValue && nowMs < now.Value) return;
            now = nowMs;
            if (ClearingOld)
            {
                if (live || !nextTry.HasValue || nowMs < nextTry.Value || manager.IsListening || manager.ShutdownInProgress) return;
                nextTry = null;
                Begin();
                return;
            }
            if (Status != NetStatus.Reconnecting || ClaimWait != null) return;
            if (!lostAt.HasValue) lostAt = nowMs;
            if (nowMs - lostAt.Value >= GiveUpMs)
            {
                relay?.Cancel();   // 진행 중인 참가도 기다리지 않는다
                if (live)
                {
                    live = false;
                    if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown(true);
                }
                Fail(NetFail.Lost, null);
                return;
            }
            if (live) return;
            if (!nextTry.HasValue) nextTry = nowMs + RetryMs;
            if (nowMs < nextTry.Value || manager.IsListening || manager.ShutdownInProgress) return;   // 앞 연결의 종료가 끝나야 다시 시작한다
            if (!Begin() && Status == NetStatus.Reconnecting) nextTry = nowMs + RetryMs;
        }

        // ── NGO ──

        void OnStarted() => manager.CustomMessagingManager.RegisterNamedMessageHandler(NetHostLink.ToClient, OnServerMessage);

        void OnServerMessage(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId) return;
            if (!hostLocal && !live) return;   // 나가기·포기 뒤 종료 전에 도착한 메시지
            string error = null;
            if (!NetHostLink.TryRead(reader, out var json) || !MessageCodec.TryDecodeServer(json, out var msg, out error))
            {
                error = error ?? MessageCodec.BadFormat + "|길이 오류";
                Debug.LogWarning("[Net] 호스트 메시지를 읽을 수 없다: " + error);
                if (hostLocal) return;
                live = false;
                Split(error, out var code, out var detail);
                Fail(code, detail);   // 받은 쪽은 그 연결을 끊는다(02 §4-1)
                manager.Shutdown(true);
                return;
            }
            Handle(msg);
        }

        void Handle(ServerMsg msg)
        {
            switch (msg)
            {
                case Welcome w:
                    FailCode = FailDetail = null;
                    FailPhase = null;
                    ClaimWait = null;
                    Seat = w.YourSeat;
                    RoomId = w.RoomId;
                    IsHost = w.IsHost;
                    everConnected = true;
                    lostAt = nextTry = dupSince = null;
                    SetStatus(NetStatus.Connected);
                    break;
                case RoomUpdate r:
                    Room = r.Room;
                    break;
                case GameSnapshot _:
                case GameDelta _:
                {
                    var u = stream.Receive(msg);
                    if (u != null)
                    {
                        if (msg is GameSnapshot) resyncAsked = false;
                        Latest = u;
                        if (u.Clock != null) Clock = u.Clock;
                        LastRejected = null;
                        Received?.Invoke(msg);
                        Updated?.Invoke(u);
                        return;
                    }
                    if (stream.NeedsResync && !resyncAsked && Status == NetStatus.Connected)
                    {
                        resyncAsked = true;
                        Resyncs++;
                        Debug.LogWarning($"[Net] 보기 스트림이 어긋나 다시 받는다(RequestResync): {stream.LastProblem}");
                        Send(new RequestResync());
                    }
                    break;
                }
                case ClockUpdate c:
                    Clock = c.Clock;
                    break;
                case NoticeMsg n when n.Code == NetCode.SeatClaim && Status != NetStatus.Connected:
                    ClaimWait = n;
                    break;
                case RejectedMsg r:
                    LastRejected = r;
                    break;
            }
            Received?.Invoke(msg);
        }

        void OnConnection(NetworkManager m, ConnectionEventData e)
        {
            if (e.EventType == ConnectionEvent.ClientDisconnected) OnLost(manager.DisconnectReason);
        }

        void OnStopped(bool wasHost) => OnLost(manager.DisconnectReason);

        void OnLost(string reason)
        {
            if (!live) return;
            live = false;
            bool claiming = ClaimWait != null;
            ClaimWait = null;   // 끊기면 대기 항목도 호스트에서 지워졌다
            Split(reason, out var code, out var detail);
            if (Status == NetStatus.Reconnecting)
            {
                if (code != null && code != NetCode.Duplicate)
                {
                    Fail(code, detail);
                    return;
                }
                if (claiming) lostAt = now;   // 자리 확인 대기 동안 멈췄던 3분 시계를 다시
                if (now.HasValue && lostAt.HasValue && now.Value - lostAt.Value >= GiveUpMs)
                {
                    Fail(NetFail.Lost, null);
                    return;
                }
                nextTry = now.HasValue ? now.Value + RetryMs : (long?)null;
                return;
            }
            if (code == NetCode.Duplicate && Status == NetStatus.Connecting && !claiming)
            {
                long t = now ?? 0;
                if (!dupSince.HasValue) dupSince = t;
                if (t - dupSince.Value < DuplicateRetryMs)
                {
                    nextTry = t + RetryMs;
                    return;
                }
            }
            if (code != null)
            {
                Fail(code, detail);
                return;
            }
            if (Status == NetStatus.Connected && InRoom(Room?.Phase))
            {
                lostAt = now;
                nextTry = now.HasValue ? now.Value + RetryMs : (long?)null;
                SetStatus(NetStatus.Reconnecting);
                return;
            }
            Fail(Status == NetStatus.Connected || claiming ? NetFail.Lost : NetFail.Unreachable, null);
        }

        // 호스트가 보낸 사유는 「E_코드|값」이다. NGO가 스스로 만든 사유(전송 끊김 설명)는 코드가 없다
        static void Split(string reason, out string code, out string detail)
        {
            code = detail = null;
            if (string.IsNullOrEmpty(reason) || !reason.StartsWith("E_", StringComparison.Ordinal)) return;
            int bar = reason.IndexOf('|');
            code = bar < 0 ? reason : reason.Substring(0, bar);
            detail = bar < 0 ? null : reason.Substring(bar + 1);
        }

        void Fail(string code, string detail)
        {
            FailCode = code;
            FailDetail = detail;
            FailPhase = everConnected ? Room?.Phase : null;
            SetStatus(NetStatus.Failed);
        }

        void SetStatus(NetStatus s)
        {
            if (Status == s) return;
            Status = s;
            StatusChanged?.Invoke(s);
        }

        public void Dispose()
        {
            live = false;
            relay?.Cancel();
            if (!manager)   // NetworkManager가 먼저 없어졌다(씬·시험 정리)
            {
                if (Status != NetStatus.Failed) SetStatus(NetStatus.Closed);
                return;
            }
            manager.OnClientStarted -= OnStarted;
            if (!hostLocal)
            {
                manager.OnConnectionEvent -= OnConnection;
                manager.OnClientStopped -= OnStopped;
                if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
            }
            manager.CustomMessagingManager?.UnregisterNamedMessageHandler(NetHostLink.ToClient);
            if (Status != NetStatus.Failed) SetStatus(NetStatus.Closed);
        }

        // 호스트 자리: NetHostSession이 방을 닫을 때 · 열지 못했거나 NGO가 멈췄을 때
        internal void MarkClosed() => SetStatus(NetStatus.Closed);

        internal void MarkFailed(string code)
        {
            live = false;
            Fail(code, null);
        }

        // 시험 주입점: 호스트가 그 사유(「E_코드|값」)로 끊은 것처럼
        internal void LoseForTest(string reason) => OnLost(reason);

        // 시험 주입점: Relay 참가를 가짜로 — join(코드, 실패 콜백)만 받는다(성공은 RelayServerData가 필요해 흉내 내지 않는다, isekai)
        internal void FakeRelayJoin(Action<string, Action<string>> join) => RelayJoin = new FailOnlyRelayJoin(join);

        sealed class FailOnlyRelayJoin : IRelayJoin<RelayServerData>
        {
            readonly Action<string, Action<string>> join;
            public FailOnlyRelayJoin(Action<string, Action<string>> join) => this.join = join ?? throw new ArgumentNullException(nameof(join));
            public void Join(string code, Action<RelayServerData> ok, Action<string> fail) => join(code, fail);
        }
    }
}
