using System;
using System.Collections.Generic;
using System.Text;
using Sinnoh.Data;
using Sinnoh.Rules.Session;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// 방(HostSession) ↔ NGO(02 §1 · §4-1, isekai Net/NetHostLink을 옮김). 호스트 PC에서 방이 보는 <see cref="IRoomLink"/>.
    /// - 통로: 이름 붙은 메시지 <see cref="ToHost"/>(손님 → 호스트) · <see cref="ToClient"/>(호스트 → 손님), 모두 ReliableFragmentedSequenced. 본문 = <see cref="MessageCodec"/> 봉투 JSON의 UTF-8 앞에 길이(int).
    /// - 연결 번호 = NGO clientId. 호스트 화면 자신은 0(ServerClientId)이고 NGO가 네트워크를 거치지 않고 바로 건넨다.
    /// - 승인: ConnectionData의 Hello를 <see cref="HostSession.Approve"/>에 넘기고, 거절이면 「코드|값」을 손님의 DisconnectReason으로 보낸다(02 §4-6).
    ///   승인 안에서 방이 보내는 Welcome·RoomUpdate는 연결이 끝날 때(ClientConnected)까지 쌓았다가 순서대로 보낸다(isekai: 승인 콜백은 N프레임, ClientConnected는 N+1프레임).
    /// - 받은 메시지와 끊김은 큐에 넣고 <see cref="Pump"/>(메인 스레드 — NetHostSession.Tick)에서 하나씩 방에 넣는다(02 §4-5 ①). 호스트 화면은 같은 호출 안에서 받으므로
    ///   그 콜백이 바로 답을 보내도 방의 진입점을 다시 부르지 않는다(HostSession은 링크 콜백 안의 재진입을 막는다). 승인은 NGO가 곧바로 답을 기다려 그 자리에서 넣는다 — 앞서 큐를 먼저 비운다.
    /// - 읽을 수 없는 메시지(E_FORMAT · E_VERSION)를 보낸 연결은 끊는다. 호스트 자리(0)는 끊김·끊기 길을 타지 않는다(방 닫기는 NetHostSession 몫).
    /// - 크기 경고(02 §4-7 [가정 M7-16]): 보낼 때 <see cref="MessageCodec.SizeWarning"/>이면 경고 로그.
    /// </summary>
    public sealed class NetHostLink : IRoomLink, IDisposable
    {
        public const string ToHost = "sinnoh.c2h";
        public const string ToClient = "sinnoh.h2c";

        struct Inbound
        {
            public int Conn;
            public RoomMsg Msg;   // null = 끊김
        }

        readonly NetworkManager manager;
        readonly Func<long> clock;
        HostSession host;
        readonly HashSet<int> approved = new HashSet<int>();    // 승인했고 아직 끊기지 않은 연결
        readonly HashSet<int> connected = new HashSet<int>();   // 연결이 끝나 바로 보낼 수 있는 연결
        readonly Dictionary<int, List<byte[]>> pending = new Dictionary<int, List<byte[]>>();
        readonly Dictionary<int, long> sentTo = new Dictionary<int, long>();
        readonly List<Inbound> inbox = new List<Inbound>();
        int approving = -1;
        int seq;
        bool closed, pumping;

        public NetHostLink(NetworkManager manager, Func<long> clock)
        {
            this.manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            manager.OnServerStarted += OnStarted;
            manager.OnConnectionEvent += OnConnection;
        }

        public void Attach(HostSession session) => host = session;

        /// <summary>호스트 자신(clientId 0)의 승인 거절 사유(NGO는 호스트 연결을 거절하지 못한다). null = 승인됨.</summary>
        public string HostRejected { get; private set; }

        /// <summary>경고 크기를 넘어 경고를 남긴 메시지 수 · 가장 큰 스냅샷·델타 바이트(본문, 길이 머리 뺌).</summary>
        public int Oversize { get; private set; }
        public int MaxSnapshotBytes { get; private set; }
        public int MaxDeltaBytes { get; private set; }

        /// <summary>그 연결에 보낸 본문 바이트 합(호스트 자리 0 포함 — 0은 네트워크를 지나지 않는다).</summary>
        public long BytesTo(int conn) => sentTo.TryGetValue(conn, out var b) ? b : 0;

        // ── NGO → 방 ──

        /// <summary>NGO 승인 콜백(NetBootstrap.StartHost에 넘긴다). 그 자리에서 답한다.</summary>
        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            int conn = (int)request.ClientNetworkId;
            var json = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : null;
            Approval a;
            if (closed || host == null) a = Approval.Reject(NetCode.HostClosed);
            else if (!MessageCodec.TryDecodeHello(json, out var hello, out var error)) a = Approval.Reject(MessageCodec.BadFormat, error);
            else
            {
                long now = clock();
                Pump(now);   // 먼저 온 끊김(같은 토큰의 옛 연결)을 넣은 뒤에 승인한다
                approving = conn;
                try
                {
                    a = host.Approve(conn, hello, now);
                }
                finally
                {
                    approving = -1;
                }
            }
            response.Approved = a.Ok;
            response.Reason = a.Ok ? null : Reason(a);
            if (a.Ok) approved.Add(conn);
            else pending.Remove(conn);
            if (request.ClientNetworkId == NetworkManager.ServerClientId) HostRejected = a.Ok ? null : Reason(a);
        }

        /// <summary>거절 사유 글 「코드|값」(값이 없으면 코드만) — 손님이 <see cref="NetClientSession.FailCode"/>·FailDetail로 나눠 읽는다.</summary>
        public static string Reason(Approval a) => a.Detail != null ? a.Code + "|" + a.Detail : a.Code;

        // NGO 호스트 시작 안에서 승인 콜백(호스트 자신) 다음에 불린다
        void OnStarted() => manager.CustomMessagingManager.RegisterNamedMessageHandler(ToHost, OnClientMessage);

        void OnConnection(NetworkManager m, ConnectionEventData e)
        {
            if (closed) return;
            int conn = (int)e.ClientId;
            switch (e.EventType)
            {
                case ConnectionEvent.ClientConnected:
                    if (approved.Contains(conn)) Flush(conn);
                    break;
                case ConnectionEvent.ClientDisconnected:
                    if (e.ClientId == NetworkManager.ServerClientId) break;   // NGO 종료 중 호스트 자리 끊김 — 방 닫기는 NetHostSession이 한다
                    connected.Remove(conn);
                    pending.Remove(conn);
                    if (approved.Remove(conn)) inbox.Add(new Inbound { Conn = conn });
                    break;
            }
        }

        // 쌓인 메시지를 순서대로 보낸다. 보내는 도중 생긴 메시지(호스트 자신은 같은 호출 안에서 받는다)도 뒤에 붙여 같은 순서로 보낸 뒤 바로 보내기로 바꾼다
        void Flush(int conn)
        {
            if (pending.TryGetValue(conn, out var list))
            {
                while (list.Count > 0 && approved.Contains(conn))
                {
                    var bytes = list[0];
                    list.RemoveAt(0);
                    Transmit(manager, ToClient, (ulong)conn, bytes);
                }
                pending.Remove(conn);
            }
            if (approved.Contains(conn)) connected.Add(conn);
        }

        void OnClientMessage(ulong sender, FastBufferReader reader)
        {
            int conn = (int)sender;
            if (closed || !approved.Contains(conn)) return;
            string error = null;
            if (!TryRead(reader, out var json) || !MessageCodec.TryDecodeRoom(json, out var msg, out _, out error))
            {
                error = error ?? MessageCodec.BadFormat + "|길이 오류";
                if (sender == NetworkManager.ServerClientId)
                {
                    Debug.LogError($"[Net] 호스트 자리의 메시지를 읽을 수 없다(버림): {error}");   // 같은 코덱으로 보내므로 일어나면 코드 버그
                    return;
                }
                Debug.LogWarning($"[Net] 연결 {conn}의 메시지를 읽을 수 없어 끊는다: {error}");
                approved.Remove(conn);
                connected.Remove(conn);
                pending.Remove(conn);
                inbox.Add(new Inbound { Conn = conn });
                manager.DisconnectClient(sender, error);
                return;
            }
            inbox.Add(new Inbound { Conn = conn, Msg = msg });
        }

        /// <summary>받아 둔 메시지·끊김을 받은 순서대로 방에 넣는다(메인 스레드, 시각 nowMs). 넣는 동안 새로 온 것도 이어서 넣는다.</summary>
        public void Pump(long nowMs)
        {
            if (pumping || host == null) return;
            pumping = true;
            try
            {
                for (int i = 0; i < inbox.Count; i++)
                {
                    var x = inbox[i];
                    if (x.Msg == null) host.OnDisconnected(x.Conn, nowMs);
                    else host.OnMessage(x.Conn, x.Msg, nowMs);
                }
            }
            finally
            {
                inbox.Clear();
                pumping = false;
            }
        }

        // ── 방 → NGO (IRoomLink) ──

        public void Send(int conn, ServerMsg msg)
        {
            if (closed) return;
            if (!approved.Contains(conn) && conn != approving) return;   // 끊겼거나 거절된 연결
            var bytes = Encoding.UTF8.GetBytes(MessageCodec.Encode(msg, ++seq));
            Measure(conn, msg, bytes.Length);
            if (connected.Contains(conn))
            {
                Transmit(manager, ToClient, (ulong)conn, bytes);
                return;
            }
            if (!pending.TryGetValue(conn, out var list)) pending[conn] = list = new List<byte[]>();
            list.Add(bytes);
        }

        void Measure(int conn, ServerMsg msg, int bytes)
        {
            sentTo.TryGetValue(conn, out var was);
            sentTo[conn] = was + bytes;
            if (msg is GameSnapshot && bytes > MaxSnapshotBytes) MaxSnapshotBytes = bytes;
            if (msg is GameDelta && bytes > MaxDeltaBytes) MaxDeltaBytes = bytes;
            var warning = MessageCodec.SizeWarning(msg, bytes);
            if (warning == null) return;
            Oversize++;
            Debug.LogWarning(warning);
        }

        /// <summary>그 연결을 끊는다(code = 손님 화면 문구 코드). 호스트 자리(0)는 끊지 않는다 — 방 닫기는 NetHostSession.Close.</summary>
        public void Close(int conn, string code)
        {
            if ((ulong)conn == NetworkManager.ServerClientId) return;
            approved.Remove(conn);
            connected.Remove(conn);
            pending.Remove(conn);
            if (closed) return;
            manager.DisconnectClient((ulong)conn, code);
        }

        /// <summary>StartHost 전에 부른다. 닫은 링크로 방을 다시 열 때 지난 연결 기록을 지운다.</summary>
        public void Open()
        {
            closed = false;
            approved.Clear();
            connected.Clear();
            pending.Clear();
            inbox.Clear();
            HostRejected = null;
        }

        /// <summary>링크를 닫는다: 남은 손님을 E_HOST_CLOSED로 끊고 그 뒤로는 아무것도 보내거나 받지 않는다. NGO 종료는 부른 쪽 몫이다.</summary>
        public void CloseAll()
        {
            if (closed) return;
            closed = true;
            if (manager.IsServer)
                foreach (var conn in approved)
                    if ((ulong)conn != NetworkManager.ServerClientId) manager.DisconnectClient((ulong)conn, NetCode.HostClosed);
            approved.Clear();
            connected.Clear();
            pending.Clear();
            inbox.Clear();
        }

        /// <summary>NGO를 건드리지 않고 닫는다(NetworkManager가 먼저 없어졌을 때) — 그 뒤 방이 보내는 것은 버린다.</summary>
        public void Abandon()
        {
            closed = true;
            approved.Clear();
            connected.Clear();
            pending.Clear();
            inbox.Clear();
        }

        public void Dispose()
        {
            if (!manager)   // NetworkManager가 먼저 없어졌다
            {
                Abandon();
                return;
            }
            CloseAll();
            manager.OnServerStarted -= OnStarted;
            manager.OnConnectionEvent -= OnConnection;
            manager.CustomMessagingManager?.UnregisterNamedMessageHandler(ToHost);
        }

        // ── 바이트(NetClientSession도 쓴다) ──

        internal static void Transmit(NetworkManager manager, string name, ulong to, byte[] bytes)
        {
            var cmm = manager.CustomMessagingManager;
            if (cmm == null) return;   // 종료 중
            using (var w = new FastBufferWriter(bytes.Length + sizeof(int), Allocator.Temp))
            {
                w.WriteValueSafe(bytes.Length);
                w.WriteBytesSafe(bytes);
                cmm.SendNamedMessage(name, to, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        internal static bool TryRead(FastBufferReader reader, out string json)
        {
            json = null;
            try
            {
                reader.ReadValueSafe(out int length);
                if (length < 0 || length > reader.Length - reader.Position) return false;
                var bytes = new byte[length];
                reader.ReadBytesSafe(ref bytes, length);
                json = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
