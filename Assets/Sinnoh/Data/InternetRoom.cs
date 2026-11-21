using System;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Data
{
    /// <summary>
    /// [인터넷] 방의 실패 코드(02 §2-5 — 문구는 screens.json, 화면 ①). Unity 서버(UGS 초기화 · 익명 로그인 · Relay · Lobby)를 못 쓰면
    /// <see cref="SuggestDirect"/> — 화면은 「Unity 서버를 쓸 수 없음 → [직접 연결]」 길을 보인다(결정 92: 직접 연결은 대체 창구).
    /// </summary>
    public static class OnlineFail
    {
        /// <summary>UGS 초기화 · 익명 로그인 · Relay 할당 · 서비스 장애 · 무료 한도 초과.</summary>
        public const string RelayAuth = "NET_RELAY_AUTH";
        /// <summary>참가 코드가 없거나 틀림 · 그 방이 닫힘.</summary>
        public const string RelayCode = "NET_RELAY_CODE";
        /// <summary>방 목록(UGS Lobby) 만들기 · 조회 · 갱신 실패(대시보드에서 Lobby가 꺼짐 · 한도 · 네트워크).</summary>
        public const string ListFail = "NET_LIST";

        /// <summary>Unity 서버 쪽 실패라 [직접 연결]을 권한다(코드가 틀린 것은 아님).</summary>
        public static bool SuggestDirect(string code) => code == RelayAuth || code == ListFail;

        /// <summary>재접속 중 이 Relay 실패는 다시 해도 소용없나(isekai 00 §7 #218 ⑦): 코드가 없음(방장이 Relay를 잃음·방이 닫힘)이면 3분 동안 헛요청 없이 바로 실패. 그 밖은 3초 뒤 다시.</summary>
        public static bool ReconnectFatal(string code) => code == RelayCode;
    }

    /// <summary>참가 코드로 Relay 연결 정보(T)를 받는 쪽(손님 — Net의 RelayLink, 시험은 가짜). ok 또는 fail(<see cref="OnlineFail"/>)을 메인 스레드에서 한 번.</summary>
    public interface IRelayJoin<T>
    {
        void Join(string code, Action<T> ok, Action<string> fail);
    }

    /// <summary>방장 Relay 할당(Net의 RelayLink — 할당하고 Transport에 Relay(wss)를 넣은 뒤 ok(참가 코드)). fail = <see cref="OnlineFail"/>.</summary>
    public interface IRelayHost
    {
        void Allocate(int maxConnections, Action<string> ok, Action<string> fail);
    }

    /// <summary>
    /// Relay 손님의 접속 시도(isekai RelayAttempts를 옮김): 처음 접속과 재접속 모두 같은 참가 코드로 JoinAllocation을 새로 한다.
    /// 시도마다 번호를 붙여, 끝나기 전에 새 시도·취소(나가기·3분 포기)가 오면 늦게 온 결과는 버린다.
    /// </summary>
    public sealed class RelayAttempts<T>
    {
        readonly IRelayJoin<T> relay;
        int serial;

        public RelayAttempts(IRelayJoin<T> relay) => this.relay = relay ?? throw new ArgumentNullException(nameof(relay));

        public bool Pending { get; private set; }
        public int Started { get; private set; }

        public void Begin(string code, Action<T> start, Action<string> fail)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("참가 코드가 없다", nameof(code));
            int mine = ++serial;
            Pending = true;
            Started++;
            relay.Join(code,
                data => { if (mine != serial) return; Pending = false; start(data); },
                error => { if (mine != serial) return; Pending = false; fail(error ?? OnlineFail.RelayAuth); });
        }

        public void Cancel()
        {
            serial++;
            Pending = false;
        }
    }

    /// <summary>
    /// [인터넷] 방 열기의 방장 쪽 흐름(02 §2-1 · §2-2 · §3-1, isekai Net/Online/InternetRoom을 고쳐 옮김 — 엔진 무관이라 Data):
    /// Relay 할당(<see cref="IRelayHost"/>, maxConnections = 자리 수 − 1) → 방 열기(startHost — NGO StartHost) → 참가 코드 → 공개 방이면 방 목록에 올림(<see cref="ListingPublisher"/>).
    /// 웹 손님 몫(<see cref="IGuestWeb"/>, 없으면 웹 링크 없음): 열 때 페이지 서버 + 페이지 터널 → 받으면 <see cref="GuestLink"/>(<c>https://…/?code=</c>). 페이지를 못 내면 <see cref="Notice"/>(NET_NO_WEBGL · NET_NO_CLOUDFLARED · NET_TUNNEL) — 방은 그대로 연다.
    /// Relay가 실패하면: 터널 도구가 있으면 게임을 루프백에 열고 두 번째 터널(wss — <see cref="Route"/> = Tunnel, PC판 손님 = <c>wss://터널</c>, 웹 = <c>?join=</c>),
    /// 없거나 그 터널도 실패하면 Failed + FailCode(Relay 실패 코드) — <see cref="SuggestDirect"/>로 [직접 연결] 안내(결정 92).
    /// 목록 실패는 방을 실패시키지 않는다(<see cref="ListFail"/>, 코드는 그대로 된다). <see cref="Tick"/>이 터널 상태를 옮기고, 방 상태를 1초에 한 번 읽어 목록을 갱신하고, 방이 닫히면 목록에서 지운다.
    /// 방(NetHostSession)은 만든 쪽이 닫는다 — <see cref="Dispose"/>는 목록 · 터널 · 페이지 서버만 끝낸다.
    /// </summary>
    public sealed class InternetRoom : IDisposable
    {
        public enum Phase { Starting, Ready, Failed, Closed }
        public enum GameRoute { None, Relay, Tunnel }

        readonly IRelayHost relay;
        readonly Func<string> startHost;
        readonly Func<RoomState> room;
        readonly ListingOptions listing;
        readonly ILobbyBoard board;
        readonly IGuestWeb web;
        readonly int seats;
        ITunnel page, game;
        long? lastRead;
        bool opened;

        /// <param name="startHost">Relay가 들어간 Transport로 방을 연다 — null = 열림, 아니면 실패 코드.</param>
        /// <param name="room">지금 방 상태(HostSession.Room). 목록 갱신에만 쓴다.</param>
        /// <param name="listing">null이거나 Public = false면 비공개(목록에 없음, 코드로만).</param>
        /// <param name="seats">Relay를 할당할 자리 수 — 로비에서 자리 수를 늘릴 수 있어 방의 최대(6)를 준다 [가정 M7-35].</param>
        /// <param name="web">웹 손님 페이지 · 터널(null = 웹 링크 없음 · Relay 실패면 바로 Failed).</param>
        public InternetRoom(IRelayHost relay, Func<string> startHost, Func<RoomState> room, ListingOptions listing, ILobbyBoard board, int seats = SeatId.MaxSeats, IGuestWeb web = null)
        {
            this.relay = relay ?? throw new ArgumentNullException(nameof(relay));
            this.startHost = startHost ?? throw new ArgumentNullException(nameof(startHost));
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            this.listing = listing;
            this.board = board;
            this.web = web;
            if (seats < RoomSettings.MinSeats || seats > SeatId.MaxSeats) throw new ArgumentOutOfRangeException(nameof(seats));
            this.seats = seats;
            if (Public && board == null) throw new ArgumentNullException(nameof(board), "공개 방은 목록 서비스가 있어야 한다");
        }

        public Phase State { get; private set; }
        public GameRoute Route { get; private set; }
        /// <summary>Relay 참가 코드(Route = Relay).</summary>
        public string JoinCode { get; private set; }
        /// <summary>게임 터널 호스트(Route = Tunnel, 받은 뒤).</summary>
        public string GameHost { get; private set; }
        /// <summary>PC판 손님이 참가 칸에 넣을 글: 참가 코드 또는 <c>wss://게임 터널 호스트</c>. 없으면 null.</summary>
        public string JoinText => Route == GameRoute.Relay ? JoinCode : GameHost != null ? JoinTarget.Secure(GameHost).Text : null;
        public string FailCode { get; private set; }
        /// <summary>Relay가 실패해 터널로 바꿨으면 그 이유(아니면 null).</summary>
        public string RelayFailCode { get; private set; }
        /// <summary>웹 손님 페이지 <c>https://….trycloudflare.com</c>(받은 뒤).</summary>
        public string PageUrl { get; private set; }
        /// <summary>웹 손님에게 줄 링크(페이지 + code/join). 없으면 null.</summary>
        public string GuestLink { get; private set; }
        /// <summary>웹 링크가 없는 까닭(실패는 아님 — <see cref="WebFail"/>). 없으면 null.</summary>
        public string Notice { get; private set; }
        public bool Public => listing != null && listing.Public;
        /// <summary>공개 방의 목록(비공개 · 터널 방이면 null).</summary>
        public ListingPublisher Listing { get; private set; }
        /// <summary>목록 실패(방은 그대로 열려 있다). 없으면 null.</summary>
        public string ListFail => Listing?.FailCode;
        /// <summary>실패를 [직접 연결]로 안내하나(결정 92).</summary>
        public bool SuggestDirect => State == Phase.Failed && OnlineFail.SuggestDirect(FailCode);
        public event Action Changed;

        /// <summary>(웹 몫이 있으면 페이지 서버 · 터널) → Relay 할당 → 방 열기 → 코드 → (공개면) 목록. 결과는 State·Changed.</summary>
        public void Open()
        {
            if (opened) throw new InvalidOperationException("이미 열었다");
            opened = true;
            StartPage();
            relay.Allocate(MaxConnections(seats), Allocated, RelayFailed);
        }

        /// <summary>Relay maxConnections = 자리 수 − 1(방장은 할당 주인이라 세지 않는다, 02 §2-1).</summary>
        public static int MaxConnections(int seats) => Math.Max(1, Math.Min(SeatId.MaxSeats, seats) - 1);

        void StartPage()
        {
            if (web == null) return;
            Notice = web.PageMissing;
            if (Notice != null) return;
            page = web.OpenPage();
            if (page.State == TunnelState.Failed) Notice = WebFail.Tunnel;
        }

        void Allocated(string code)
        {
            if (State != Phase.Starting) return;   // 그사이 닫음
            var fail = startHost();
            if (fail != null) { Fail(fail); return; }
            JoinCode = code;
            Route = GameRoute.Relay;
            State = Phase.Ready;
            if (Public)
            {
                Listing = new ListingPublisher(board);
                Listing.Publish(RoomListingModel.Build(listing, room(), JoinCode));
            }
            Refresh();
        }

        // Relay 실패: 터널 도구가 있으면 게임을 루프백에 열고 두 번째 터널(02 §2-1) — 받으면 Ready. 없으면 Failed(결정 92 — [직접 연결] 안내)
        void RelayFailed(string code)
        {
            if (State != Phase.Starting) return;
            RelayFailCode = code ?? OnlineFail.RelayAuth;
            if (web == null || !web.CanTunnel) { Fail(RelayFailCode); return; }
            var fail = web.StartGameLoopback();
            if (fail != null) { Fail(fail); return; }
            Route = GameRoute.Tunnel;
            game = web.OpenGame();
            if (game.State == TunnelState.Failed) Fail(RelayFailCode);
            else Changed?.Invoke();
        }

        void Fail(string code)
        {
            if (State != Phase.Starting) return;
            FailCode = code;
            State = Phase.Failed;
            Stop();
            JoinCode = GameHost = PageUrl = GuestLink = null;
            Changed?.Invoke();
        }

        // 받은 주소로 웹 링크를 다시 만든다
        void Refresh()
        {
            GuestLink = PageUrl == null ? null
                : Route == GameRoute.Relay && JoinCode != null ? OnlineLinks.CodeLink(PageUrl, JoinCode)
                : Route == GameRoute.Tunnel && GameHost != null ? OnlineLinks.JoinLink(PageUrl, GameHost)
                : null;
            Changed?.Invoke();
        }

        /// <summary>매 프레임: 터널 주소 · 실패를 옮기고, 1초에 한 번 방 상태로 목록 값을 고치고(같으면 안 보냄) 하트비트를 보낸다. 방이 닫혔으면 목록에서 지운다.</summary>
        public void Tick(long nowMs)
        {
            if (State == Phase.Failed || State == Phase.Closed) return;
            if (page != null)
            {
                var before = page.State;
                page.Tick(nowMs);
                if (page.State != before)
                {
                    if (page.State == TunnelState.Ready) PageUrl = page.Url;
                    else { PageUrl = null; Notice = WebFail.Tunnel; }
                    Refresh();
                }
            }
            if (game != null)
            {
                var before = game.State;
                game.Tick(nowMs);
                if (game.State != before)
                {
                    if (game.State == TunnelState.Ready)
                    {
                        GameHost = OnlineLinks.HostOf(game.Url);
                        State = Phase.Ready;
                    }
                    else if (State == Phase.Starting) { Fail(RelayFailCode); return; }
                    else GameHost = null;   // 연 뒤 게임 터널이 끊김: 손님이 닿을 길이 없다(방은 방장이 닫는다) [가정 M7-60]
                    Refresh();
                }
            }
            if (State != Phase.Ready || Listing == null) return;
            if (!lastRead.HasValue || nowMs - lastRead.Value >= ListingPublisher.UpdateGapMs)
            {
                lastRead = nowMs;
                var r = room();
                if (r != null && r.Phase == RoomPhase.Closed)
                {
                    Dispose();
                    return;
                }
                Listing.Update(RoomListingModel.Build(listing, r, JoinCode));
            }
            Listing.Tick(nowMs);
        }

        /// <summary>지우기 요청이 남지 않았다(앱 종료가 기다린다).</summary>
        public bool Idle => Listing == null || Listing.Idle;

        void Stop()
        {
            page?.Dispose();
            game?.Dispose();
            web?.Dispose();
            page = game = null;
        }

        /// <summary>목록에서 지우고 터널 · 페이지 서버를 끝낸다(방 닫기 · 앱 종료). 방은 닫지 않는다.</summary>
        public void Dispose()
        {
            if (State == Phase.Closed) return;
            State = Phase.Closed;
            Listing?.Close();
            Stop();
            JoinCode = GameHost = PageUrl = GuestLink = null;
            Changed?.Invoke();
        }
    }
}
