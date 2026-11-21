using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// 화면(<c>Sinnoh.Client</c> RoomFlow)이 쓰는 방 하나(M7 화면 ① [가정 M7-41]): NetworkManager + <see cref="NetRunner"/>를 가진 GameObject를 만들고 방장 방(<see cref="OpenHost"/> →
    /// <see cref="StartDirect"/> 또는 <see cref="StartInternet"/>)이나 손님 접속(<see cref="Join"/>)을 연다. 화면 쪽은 NGO 형식을 모른다.
    /// <see cref="Dispose"/> = 방 닫기(방장 — 마지막 쓰기 · E_HOST_CLOSED) 또는 나가기(손님) → NGO가 멈추면 GameObject를 없앤다(<see cref="NetRunner.Release"/>).
    /// </summary>
    public sealed class NetRoom : IDisposable
    {
        readonly NetRunner runner;
        bool disposed;

        NetRoom(NetRunner runner) => this.runner = runner;

        /// <summary>방장 방이면 그 방(아니면 null).</summary>
        public NetHostSession Host { get; private set; }
        /// <summary>내 자리(방장 = 호스트 자리 · 손님 = 접속).</summary>
        public NetClientSession Seat { get; private set; }
        /// <summary>[인터넷] 방(방장 · <see cref="StartInternet"/> 뒤). 없으면 null.</summary>
        public InternetRoom Internet => runner ? runner.Internet : null;
        public bool IsHost => Host != null;
        /// <summary>직접 연결로 연 포트(방장 · <see cref="StartDirect"/> 뒤). 없으면 0.</summary>
        public int Port { get; private set; }
        public RoomState Room => Seat.Room;

        /// <summary>방 시각(null = Stopwatch). 시험이 가짜 시각으로 바꾼다.</summary>
        public Func<long> Clock
        {
            get => runner.Clock;
            set => runner.Clock = value;
        }

        /// <summary>
        /// 이 PC에서 방을 만든다(아직 열지 않는다). saves = 방 저장 폴더(null = 저장 안 함 — 시험·확인용). resume = 그 방 저장으로 이어하기 대기(S10 — 02 §6-3, HostSession.Resume).
        /// 연 뒤 <see cref="StartDirect"/> 또는 <see cref="StartInternet"/>.
        /// </summary>
        public static NetRoom OpenHost(Ctx ctx, HostConfig cfg, Hello owner, string saves = null, SaveRecord resume = null)
        {
            var manager = NetBootstrap.Create("NetRoom-Host");
            var room = new NetRoom(manager.gameObject.AddComponent<NetRunner>());
            try
            {
                room.Host = room.runner.OpenHost(manager, ctx, cfg, owner, saves, resume);
            }
            catch
            {
                room.runner.Shutdown();
                UnityEngine.Object.Destroy(manager.gameObject);   // 이어 여는 저장을 받지 못했다(형식 판 · 데이터) — 만든 NetworkManager를 남기지 않는다
                throw;
            }
            room.Seat = room.Host.Local;
            room.Saving = saves != null;
            return room;
        }

        /// <summary>방장 방이 판을 저장하나(저장 폴더를 받았다 — 에디터·확인용은 저장 안 함).</summary>
        public bool Saving { get; private set; }

        /// <summary>방장: 판 저장 쓰기 실패(<see cref="NetRunner.SaveProblems"/>). 손님·저장 안 함이면 빈 목록.</summary>
        public List<string> SaveProblems => Host != null && runner ? runner.SaveProblems : new List<string>();

        /// <summary>방장: 지금 판의 머리를 마지막으로 쓴 때(UTC ms). 게임 전·손님이면 0.</summary>
        public long SavedAtMs => Host?.Host.Game?.Head?.Summary != null ? Host.Host.Game.Head.Summary.SavedAtMs : 0;

        /// <summary>시험 주입점: 이 방의 NetworkManager(손님 쪽 망이 끊긴 것처럼 Shutdown).</summary>
        internal Unity.Netcode.NetworkManager Manager => runner ? runner.GetComponent<Unity.Netcode.NetworkManager>() : null;

        /// <summary>[직접 연결](결정 92): 그 포트로 연다(listen 기본 0.0.0.0 — 시험은 127.0.0.1). false = 포트를 열 수 없음(Seat.FailCode = NET_PORT_BUSY) 또는 호스트 자리 승인 실패.</summary>
        /// <remarks>webRoot(웹 손님판 폴더 — null = 없음)가 있으면 같은 수신 주소에 웹 손님 페이지 서버도 연다(02 §2-2 — 포트 8080~8089, <see cref="WebPort"/>). 없거나 못 열면 <see cref="WebNotice"/>.</remarks>
        public bool StartDirect(ushort port = NetBootstrap.DefaultPort, string listen = NetBootstrap.ListenAll, string webRoot = null)
        {
            if (Host == null) throw new InvalidOperationException("손님 접속은 열 수 없다");
            bool ok = Host.Start(port, listen);
            Port = ok ? port : 0;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (ok)
            {
                web?.Dispose();
                web = webRoot != null ? WebGuestServer.Start(webRoot, listen) : null;
                WebPort = web != null ? web.Port : 0;
                WebNotice = webRoot == null ? WebFail.NoWebGl : web == null ? WebFail.Tunnel : null;
            }
#endif
            return ok;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        WebGuestServer web;
#endif

        /// <summary>[직접 연결] 웹 손님 페이지 서버의 포트(없으면 0 — 웹 링크는 <see cref="OnlineLinks.DirectPage"/>).</summary>
        public int WebPort { get; private set; }
        /// <summary>[직접 연결] 웹 링크가 없는 까닭(<see cref="WebFail"/>). 있으면 null.</summary>
        public string WebNotice { get; private set; }

        /// <summary>
        /// [인터넷]: (웹 손님 페이지 + 터널) → Relay → 참가 코드 → 공개면 방 목록(<see cref="NetRunner.OpenInternet"/>). Relay가 실패하고 cloudflared가 있으면 게임을 127.0.0.1에 열고 두 번째 터널(02 §2-1).
        /// 결과는 돌려받은 방의 State. relay·board·web은 시험용(null = UGS · webRoot/cloudflared로 만든 <see cref="GuestWeb"/>).
        /// </summary>
        public InternetRoom StartInternet(ListingOptions listing, IRelayHost relay = null, ILobbyBoard board = null, string webRoot = null, string cloudflared = null,
            ushort port = NetBootstrap.DefaultPort, IGuestWeb web = null)
        {
            if (Host == null) throw new InvalidOperationException("손님 접속은 열 수 없다");
#if !UNITY_WEBGL || UNITY_EDITOR
            if (web == null)
            {
                var host = Host;
                web = new GuestWeb(webRoot, cloudflared, () => host.Start(port, "127.0.0.1") ? null : host.Local.FailCode ?? NetFail.PortBusy, port);
            }
#endif
            return runner.OpenInternet(Host, listing, relay, board, web);
        }

        /// <summary>손님: 참가 칸의 대상으로 붙는다. before = Connect 전에 접속을 손보는 곳(시험의 가짜 Relay 참가). 결과는 Seat.Status.</summary>
        public static NetRoom Join(JoinTarget target, Hello hello, Action<NetClientSession> before = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var manager = NetBootstrap.Create("NetRoom-Guest");
            var room = new NetRoom(manager.gameObject.AddComponent<NetRunner>());
            var c = new NetClientSession(manager);
            room.runner.Session = c;
            room.Seat = c;
            before?.Invoke(c);
            if (!c.Connect(target, hello) && c.Status != NetStatus.Failed) c.MarkFailed(NetFail.Unreachable);
            return room;
        }

        /// <summary>방 메시지를 보낸다(방장의 LeaveRoom = 방 닫기).</summary>
        public void Send(RoomMsg msg)
        {
            if (disposed) return;
            if (Host != null) Host.Send(msg);
            else Seat.Send(msg);
        }

        /// <summary>내 자리를 화면의 좌석 손님에 잇는 링크.</summary>
        public NetSeatLink SeatLink() => new NetSeatLink(Seat, Send);

        /// <summary>방장 화면 「이 주소를 친구에게」(직접 연결 — Tailscale · LAN 주소, 02 §2-1).</summary>
        public static List<KeyValuePair<string, string>> LocalAddresses() => NetBootstrap.LocalAddresses();

        /// <summary>방 닫기(방장) · 나가기(손님) → 세션·러너를 버리고 NGO가 멈추면 GameObject를 없앤다. 두 번 불러도 된다.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Host == null && Seat != null && Seat.Status == NetStatus.Connected) Seat.Leave();
#if !UNITY_WEBGL || UNITY_EDITOR
            web?.Dispose();
            web = null;
#endif
            if (runner) runner.Release();
        }

        /// <summary>
        /// 이 PC의 웹 손님판 폴더 · cloudflared(02 §2-2): 플레이어는 실행 파일 옆(<c>WebGL/</c> · <c>cloudflared.exe</c>, 개발은 <c>Tools/bin</c>도) — 없으면 null.
        /// 에디터(플레이 시험)는 null — 시험이 그 PC의 빌드 · 도구 유무에 따라 달라지지 않게(시험은 임시 폴더 · 가짜를 넣는다) [가정 M7-61].
        /// </summary>
        public static string DefaultWebRoot()
        {
#if UNITY_EDITOR || UNITY_WEBGL
            return null;
#else
            return WebFiles.FindRoot(System.IO.Path.GetDirectoryName(Application.dataPath), false);
#endif
        }

        /// <inheritdoc cref="DefaultWebRoot"/>
        public static string DefaultCloudflared()
        {
#if UNITY_EDITOR || UNITY_WEBGL
            return null;
#else
            return CloudflaredTunnel.FindExe(System.IO.Path.GetDirectoryName(Application.dataPath));
#endif
        }
    }
}
