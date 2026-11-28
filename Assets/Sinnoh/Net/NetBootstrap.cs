using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Sinnoh.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// NetworkManager + UnityTransport를 코드로 차리고 호스트·손님으로 시작하는 정적 도우미(02 §1 · §2-3, isekai Net/NetBootstrap을 옮김 — Relay 몫 <see cref="UseRelay"/>·<see cref="StartClientRelay"/>는 Net ②).
    /// NGO는 연결·승인·이름 붙은 메시지 통로로만 쓴다(NetworkObject·RPC·씬 동기화 없음). 전송은 늘 WebSocket(Relay · 터널도 같은 길 — 웹 손님판이 있던 때 정한 것을 결정 210 뒤에도 그대로 둔다).
    /// </summary>
    public static class NetBootstrap
    {
        public const ushort DefaultPort = JoinTarget.DefaultPort;
        /// <summary>호스트 수신 주소 기본값 = 모든 인터페이스(02 §2-3 — 비우면 루프백에만 묶여 다른 PC가 못 붙는다). 시험은 127.0.0.1만 연다(방화벽 창을 띄우지 않으려고).</summary>
        public const string ListenAll = "0.0.0.0";
        /// <summary>보내기·받기 큐 칸 수(02 §2-3 — isekai 4인 방에서 128칸이 넘쳤다).</summary>
        public const int PacketQueueSize = 512;

        /// <summary>NetworkManager + UnityTransport를 가진 GameObject 하나를 만든다(Play 중에만). 씬 동기화·플레이어 오브젝트는 쓰지 않는다. DontDestroyOnLoad는 NetworkManager가 스스로 건다.</summary>
        public static NetworkManager Create(string name = "NetworkManager")
        {
            // 꺼진 채로 만들어 NetworkConfig를 넣은 뒤 켠다 — NetworkManager.Awake가 이 설정으로 프리팹 목록을 차린다
            var go = new GameObject(name);
            go.SetActive(false);
            var transport = AddTransport(go);
            var manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = true,
                PlayerPrefab = null,
            };
            go.SetActive(true);
            return manager;
        }

        /// <summary>UnityTransport를 붙이고 차린다: WebSocket · 연결 시도 1초 × 10번 · 끊김 판정 30초(02 §2-3) · 큐 512칸.</summary>
        public static UnityTransport AddTransport(GameObject go)
        {
            var transport = go.AddComponent<UnityTransport>();
            transport.UseWebSockets = true;
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 10;
            transport.DisconnectTimeoutMS = 30000;
            transport.MaxPacketQueueSize = PacketQueueSize;
            return transport;
        }

        /// <summary>호스트로 시작한다(기본 0.0.0.0). false = 포트를 열 수 없음(사용 중 등).</summary>
        public static bool StartHost(NetworkManager manager, ushort port,
            Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse> approval, string listenAddress = ListenAll)
        {
            var t = (UnityTransport)manager.NetworkConfig.NetworkTransport;
            if (t.Protocol != UnityTransport.ProtocolType.RelayUnityTransport) t.SetConnectionData(listenAddress, port, listenAddress);
            manager.ConnectionApprovalCallback = approval;
            return manager.StartHost();
        }

        /// <summary>호스트 address:port에 손님으로 붙는다(ws://, 암호화 없음). connectionData = 승인 콜백이 받는 Hello.</summary>
        public static bool StartClient(NetworkManager manager, string address, ushort port, byte[] connectionData)
        {
            var t = (UnityTransport)manager.NetworkConfig.NetworkTransport;
            t.UseEncryption = false;
            t.SetConnectionData(address, port);
            manager.NetworkConfig.ConnectionData = connectionData;
            return manager.StartClient();
        }

        /// <summary>
        /// wss://host:port에 손님으로 붙는다(터널). TLS 서버 이름 = host. 데스크톱은 WebSocket 경로에 이름 Host 줄을 끼운다(<see cref="SecureHandshakePath"/> — Transport의 핸드셰이크 Host는
        /// 해석된 IP라 Cloudflare가 403을 준다, isekai 10-09 측정). address = 미리 풀어 둔 IP(없으면 Transport가 host를 푼다).
        /// </summary>
        public static bool StartClientSecure(NetworkManager manager, string host, ushort port, byte[] connectionData, string address = null)
        {
            var t = (UnityTransport)manager.NetworkConfig.NetworkTransport;
            t.UseEncryption = true;
            t.SetClientSecrets(host);
            t.SetConnectionData(address ?? host, port);
            t.ConnectionData.WebSocketPath = SecureHandshakePath(host, port);
            manager.NetworkConfig.ConnectionData = connectionData;
            return manager.StartClient();
        }

        /// <summary>Relay(wss) 연결 정보로 손님으로 붙는다(02 §2-1 [인터넷]). 암호화는 Relay 정보대로 Transport가 정한다.</summary>
        public static bool StartClientRelay(NetworkManager manager, RelayServerData relay, byte[] connectionData)
        {
            UseRelay(manager, relay);
            manager.NetworkConfig.ConnectionData = connectionData;
            return manager.StartClient();
        }

        /// <summary>manager의 UnityTransport를 Relay로 바꾼다(방장은 그 뒤 StartHost — 주소·포트를 덮지 않는다). 통신은 그대로 WebSocket이라 Relay 정보도 wss여야 한다.</summary>
        public static void UseRelay(NetworkManager manager, RelayServerData relay)
        {
            var t = (UnityTransport)manager.NetworkConfig.NetworkTransport;
            t.UseEncryption = false;   // Relay는 UseEncryption과 상관없이 Relay 정보로 암호화한다(NGO UnityTransport 소스, isekai)
            t.SetRelayServerData(relay);
        }

        /// <summary>
        /// 데스크톱 wss 손님의 WebSocket 경로: <c>/ HTTP/1.1\r\nHost: 이름\r\nX-Sinnoh:</c> — 요청 줄은 <c>GET /</c> 그대로, 이름 Host가 IP Host보다 앞에 온다(isekai 그대로).
        /// host가 DNS 이름 글자가 아니거나 경로가 125바이트(FixedString128Bytes)를 넘으면 "/".
        /// </summary>
        public static string SecureHandshakePath(string host, ushort port)
        {
            if (string.IsNullOrEmpty(host)) return "/";
            foreach (char c in host)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-')) return "/";
            var name = port == JoinTarget.SecurePort ? host : host + ":" + port.ToString(CultureInfo.InvariantCulture);
            var path = "/ HTTP/1.1\r\nHost: " + name + "\r\nX-Sinnoh:";
            return path.Length <= 125 ? path : "/";
        }

        public const string KindTailscale = "Tailscale";   // 100.64.0.0/10
        public const string KindLan = "LAN";               // 192.168.*, 10.*, 172.16~31.*

        /// <summary>방장 화면 「이 주소를 친구에게」 칸(02 §2-1 [직접 연결]): 켜진 인터페이스의 IPv4 주소와 종류(Key = 주소, Value = Kind*). 루프백 · 가상 어댑터 · Tailscale·LAN이 아닌 주소는 뺀다.</summary>
        public static List<KeyValuePair<string, string>> LocalAddresses()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (IsVirtualAdapter(nic.Name, nic.Description)) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = a.Address.GetAddressBytes();
                    string kind = b[0] == 100 && b[1] >= 64 && b[1] <= 127 ? KindTailscale
                        : b[0] == 192 && b[1] == 168 || b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 ? KindLan
                        : null;
                    if (kind != null) list.Add(new KeyValuePair<string, string>(a.Address.ToString(), kind));
                }
            }
            return list;
        }

        /// <summary>친구가 붙을 수 없는 가상 어댑터인가: WSL·Hyper-V 「vEthernet (…)」, VirtualBox·VMware 호스트 전용 어댑터(Tailscale 어댑터는 아니다).</summary>
        public static bool IsVirtualAdapter(string name, string description)
        {
            return name != null && name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)
                || description != null && (description.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0
                    || description.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                    || description.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
