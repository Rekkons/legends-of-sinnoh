using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Data
{
    /// <summary>방 만들기 S05의 [인터넷] 방 목록 설정(결정 95, 02 §3-1 [가정 M7-6]). Public = 목록에 올림(기본), 끄면 비공개 — 참가 코드·링크로만.</summary>
    public sealed class ListingOptions
    {
        public bool Public = true;
        /// <summary>방장 닉네임(목록의 방 이름 — 02 §3-1 ①).</summary>
        public string HostName;
        /// <summary>빌드 표지(<see cref="RoomListingModel.Stamp"/> — 방장 Hello로 만든다). 다르면 목록에서 「버전 다름」.</summary>
        public string Stamp;
    }

    /// <summary>
    /// UGS Lobby 데이터(공개, 짧은 키 — isekai RoomListingModel의 ListingData를 고쳐 옮김): h 방장 닉네임 · n 찬 자리 · m 자리 수 · k 캠페인 · c 제한 시간 프리셋 ·
    /// s 상태(lobby / game) · v 빌드 표지 · j Relay 참가 코드. 토큰·좌석 닉네임은 싣지 않는다.
    /// </summary>
    public sealed class ListingData
    {
        public const string Lobby = "lobby", Game = "game";

        public string Host = "";
        public int Members;
        public int Seats;
        public Campaign Campaign;
        public ClockPreset Clock;
        public string State = Lobby;
        public string Stamp = "";
        public string JoinCode = "";

        public Dictionary<string, string> ToDictionary() => new Dictionary<string, string>
        {
            ["h"] = Host ?? "",
            ["n"] = Members.ToString(CultureInfo.InvariantCulture),
            ["m"] = Seats.ToString(CultureInfo.InvariantCulture),
            ["k"] = ((int)Campaign).ToString(CultureInfo.InvariantCulture),
            ["c"] = ((int)Clock).ToString(CultureInfo.InvariantCulture),
            ["s"] = State ?? "",
            ["v"] = Stamp ?? "",
            ["j"] = JoinCode ?? "",
        };

        /// <summary>읽기: 없는 키·읽을 수 없는 숫자·모르는 열거 값은 빈칸·0.</summary>
        public static ListingData FromDictionary(IDictionary<string, string> d)
        {
            string S(string k) => d != null && d.TryGetValue(k, out var v) && v != null ? v : "";
            int I(string k) => int.TryParse(S(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            int k0 = I("k"), c0 = I("c");
            return new ListingData
            {
                Host = S("h"), Members = I("n"), Seats = I("m"),
                Campaign = Enum.IsDefined(typeof(Campaign), k0) ? (Campaign)k0 : Campaign.HeartOfSinnoh,
                Clock = Enum.IsDefined(typeof(ClockPreset), c0) ? (ClockPreset)c0 : ClockPreset.Off,
                State = S("s"), Stamp = S("v"), JoinCode = S("j"),
            };
        }

        /// <summary>보낼 값이 같은가(같으면 갱신을 보내지 않는다).</summary>
        public bool SameAs(ListingData other)
        {
            if (other == null) return false;
            var a = ToDictionary();
            var b = other.ToDictionary();
            return a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
        }
    }

    /// <summary>Lobby 조회 한 줄 그대로(서비스 층이 만든다).</summary>
    public sealed class LobbyRow
    {
        public string Id;
        /// <summary>로비를 만든 때(유닉스 ms, UTC — 서비스 층이 넣는다).</summary>
        public long CreatedMs;
        public IDictionary<string, string> Data;
    }

    /// <summary>목록 한 줄(S06 방 목록, 02 §3-1 ①).</summary>
    public sealed class ListedRoom
    {
        public string Id;
        public string Host;
        public int Members;
        public int Seats;
        public Campaign Campaign;
        public ClockPreset Clock;
        /// <summary>로비 단계가 아님(게임 중·결과·이어하기 대기) — 목록에 보이지 않는다.</summary>
        public bool InGame;
        public string Stamp;
        public string JoinCode;
        public long CreatedMs;

        public bool Full => Members >= Seats;
        public bool OtherVersion(string myStamp) => Stamp != myStamp;
        /// <summary>누르면 들어가는 줄: 로비 · 빈 자리 · 같은 빌드 · 코드 있음. 아니면 회색.</summary>
        public bool Joinable(string myStamp) => !InGame && !Full && !OtherVersion(myStamp) && JoinCode != null;

        public static ListedRoom From(LobbyRow row)
        {
            var d = ListingData.FromDictionary(row?.Data);
            return new ListedRoom
            {
                Id = row?.Id, Host = d.Host, Members = d.Members, Seats = d.Seats, Campaign = d.Campaign, Clock = d.Clock,
                InGame = d.State != ListingData.Lobby, Stamp = d.Stamp, JoinCode = JoinTarget.IsCode(d.JoinCode) ? d.JoinCode.ToUpperInvariant() : null,
                CreatedMs = row?.CreatedMs ?? 0,
            };
        }
    }

    /// <summary>방 목록 조회 결과. 실패하면 Rooms는 빈 목록이고 FailCode(<see cref="OnlineFail"/>)가 있다 — 화면은 [새로 고침]과 참가 코드·직접 연결 길(결정 92).</summary>
    public sealed class ListingQuery
    {
        public IReadOnlyList<ListedRoom> Rooms = new ListedRoom[0];
        public string FailCode;
        public bool Ok => FailCode == null;

        public static ListingQuery Done(IEnumerable<LobbyRow> rows, string myStamp) =>
            new ListingQuery { Rooms = RoomListingModel.Order((rows ?? Enumerable.Empty<LobbyRow>()).Where(r => r != null).Select(ListedRoom.From), myStamp) };

        public static ListingQuery Fail(string code) => new ListingQuery { FailCode = code ?? OnlineFail.ListFail };
    }

    /// <summary>방 목록의 판단(결정 95, 02 §3-1 ① [가정 M7-6] — isekai RoomListingModel을 고쳐 옮김): 로비 데이터 만들기 · 빌드 표지 · 보이는 줄과 순서.</summary>
    public static class RoomListingModel
    {
        /// <summary>UGS 로비 이름(화면에 쓰지 않는다 — 목록은 h를 보인다). 비면 「sinnoh」.</summary>
        public static string LobbyName(string host)
        {
            var s = string.IsNullOrWhiteSpace(host) ? "sinnoh" : host.Trim();
            return s.Length > 32 ? s.Substring(0, 32) : s;
        }

        /// <summary>빌드 표지: 승인의 E_VERSION·E_CONTENT가 보는 값(프로토콜 판 · 빌드 · 규칙 판 · 데이터 해시)의 FNV-1a 8자. 같으면 들어갈 수 있는 빌드다.</summary>
        public static string Stamp(Hello hello)
        {
            if (hello == null) throw new ArgumentNullException(nameof(hello));
            var s = string.Join("|", hello.ProtocolVersion.ToString(CultureInfo.InvariantCulture), hello.BuildId ?? "",
                hello.RulesVersion.ToString(CultureInfo.InvariantCulture), hello.ContentHash ?? "");
            uint h = 2166136261;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>지금 방 상태로 로비 데이터를 만든다: 찬 자리 = 앉은 사람, 자리 수 = 설정 Seats, 로비면 lobby · 그 밖은 game. 방장 닉네임은 1번 자리(없으면 options.HostName).</summary>
        public static ListingData Build(ListingOptions options, RoomState room, string joinCode)
        {
            var s = room?.Settings;
            var seats = room?.Seats ?? new List<RoomSeat>();
            return new ListingData
            {
                Host = seats.FirstOrDefault(x => x != null && x.Host)?.Nickname ?? options?.HostName ?? "",
                Members = seats.Count(x => x != null),
                Seats = s?.Seats ?? 0,
                Campaign = s?.Campaign ?? Campaign.HeartOfSinnoh,
                Clock = s?.Clock ?? ClockPreset.Off,
                State = room == null || room.Phase == RoomPhase.Lobby ? ListingData.Lobby : ListingData.Game,
                Stamp = options?.Stamp ?? "",
                JoinCode = joinCode ?? "",
            };
        }

        /// <summary>목록 순서(로비 단계 방만): 들어갈 수 있는 방 → 가득 찬 방 → 버전 다른 방, 묶음 안은 최근에 연 순.</summary>
        public static List<ListedRoom> Order(IEnumerable<ListedRoom> rooms, string myStamp) =>
            (rooms ?? Enumerable.Empty<ListedRoom>())
            .Where(r => r != null && !r.InGame && r.JoinCode != null)
            .OrderBy(r => r.Joinable(myStamp) ? 0 : !r.OtherVersion(myStamp) ? 1 : 2)
            .ThenByDescending(r => r.CreatedMs)
            .ToList();
    }

    /// <summary>
    /// 방 목록 서비스 층(UGS Lobby — Net의 RoomListing이 구현, 시험은 가짜). 콜백은 메인 스레드에서 한 번만 온다(ok 또는 fail).
    /// fail(code, gone): code = <see cref="OnlineFail"/>, gone = 그 로비가 이미 없음(지워짐·만료) — 더 보내지 않는다.
    /// </summary>
    public interface ILobbyBoard
    {
        void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail);
        void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail);
        void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail);
        /// <summary>지운다. 실패도 done(지우지 못한 로비는 하트비트가 끊겨 조회에서 빠진다).</summary>
        void Delete(string lobbyId, Action done);
        void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail);
    }

    /// <summary>
    /// 방장 쪽 공개 방 올리기(결정 95, isekai RoomListing의 방장 몫을 엔진 무관으로 고쳐 옮김): <see cref="Publish"/> → 매 프레임 <see cref="Tick"/>이
    /// 하트비트(<see cref="HeartbeatMs"/> — UGS Lobby는 30초 동안 활동이 없으면 조회에서 뺀다)와 묶은 갱신(같은 값은 안 보냄, <see cref="UpdateGapMs"/>에 한 번)을 보낸다.
    /// 한 번에 요청 하나. <see cref="Close"/> = 지우기(만드는 중이면 만든 뒤 바로 지움). 실패는 던지지 않고 <see cref="FailCode"/> — 방 열기·코드 참가는 그대로다.
    /// </summary>
    public sealed class ListingPublisher
    {
        public const long HeartbeatMs = 15000;
        public const long UpdateGapMs = 1000;

        readonly ILobbyBoard board;
        ListingData sent, wanted;
        long? lastBeat, lastSend;
        bool busy, closed, deleting;

        public ListingPublisher(ILobbyBoard board) => this.board = board ?? throw new ArgumentNullException(nameof(board));

        /// <summary>만든 로비 ID. 만들기 전·실패·지운 뒤 null.</summary>
        public string LobbyId { get; private set; }
        public bool Published => LobbyId != null;
        /// <summary>목록 실패(<see cref="OnlineFail"/>). null = 없음.</summary>
        public string FailCode { get; private set; }
        /// <summary>보낸 하트비트 · 갱신 수(시험·로그).</summary>
        public int Beats { get; private set; }
        public int Updates { get; private set; }
        /// <summary>요청이 남지 않았다(앱 종료가 지우기를 기다릴 때 본다).</summary>
        public bool Idle => !busy && !deleting;
        /// <summary>지웠다(Close 뒤 지우기 요청까지 끝남, 만든 적 없으면 바로).</summary>
        public bool Deleted { get; private set; }

        /// <summary>공개 로비를 만든다(로비 이름 · 최대 인원 = 자리 수 · 데이터). 결과는 Published·FailCode.</summary>
        public void Publish(ListingData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (busy || LobbyId != null || closed) throw new InvalidOperationException("이미 올렸거나 올리는 중이거나 닫았다");
            busy = true;
            wanted = data;
            board.Create(RoomListingModel.LobbyName(data.Host), Math.Max(1, data.Seats), data.ToDictionary(),
                id =>
                {
                    busy = false;
                    LobbyId = id;
                    sent = data;
                    if (closed) Delete();
                },
                code =>
                {
                    busy = false;
                    FailCode = code ?? OnlineFail.ListFail;
                    if (closed) Deleted = true;
                });
        }

        /// <summary>보낼 값을 바꾼다(실제로 보내는 것은 Tick).</summary>
        public void Update(ListingData data) => wanted = data;

        /// <summary>매 프레임: 갱신이 있으면 갱신(활동으로 친다), 없으면 하트비트 차례에 하트비트.</summary>
        public void Tick(long nowMs)
        {
            if (closed || busy || LobbyId == null) return;
            lastBeat ??= nowMs;
            if (wanted != null && !wanted.SameAs(sent) && (!lastSend.HasValue || nowMs - lastSend.Value >= UpdateGapMs))
            {
                lastSend = nowMs;
                lastBeat = nowMs;
                var data = wanted;
                int? max = sent == null || sent.Seats != data.Seats ? Math.Max(1, data.Seats) : (int?)null;
                busy = true;
                Updates++;
                board.Update(LobbyId, data.ToDictionary(), max, () => { busy = false; sent = data; }, Lost);
                return;
            }
            if (nowMs - lastBeat.Value < HeartbeatMs) return;
            lastBeat = nowMs;
            busy = true;
            Beats++;
            board.Heartbeat(LobbyId, () => busy = false, Lost);
        }

        // 로비가 없어졌으면 그만 보낸다. 그 밖(한도 초과·네트워크)은 다음 차례에 다시
        void Lost(string code, bool gone)
        {
            busy = false;
            if (!gone) return;
            LobbyId = null;
            FailCode = code ?? OnlineFail.ListFail;
        }

        /// <summary>목록에서 지운다(방 닫기·게임 끔). 두 번 불러도 된다.</summary>
        public void Close()
        {
            if (closed) return;
            closed = true;
            if (busy && LobbyId == null) return;   // 만드는 중 — 만든 뒤 지운다
            if (LobbyId == null) { Deleted = true; return; }
            Delete();
        }

        void Delete()
        {
            string id = LobbyId;
            LobbyId = null;
            deleting = true;
            board.Delete(id, () => { deleting = false; Deleted = true; });
        }
    }

    /// <summary>
    /// S06 방 목록 받기의 빈도 막기(02 §3-1 ① · [가정 M7-40·M7-43] — UGS Lobby 조회는 몇 초 안에 거듭하면 RateLimited): 화면을 열 때 한 번 + [새로 고침],
    /// 지난 조회를 시작한 때부터 <see cref="GapMs"/> 안에는 시작하지 않는다. 시각은 부르는 쪽이 준다(단조 ms).
    /// </summary>
    public sealed class RefreshGate
    {
        public const long GapMs = 5000;

        long? last;

        /// <summary>지금 조회를 시작해도 되나 — 되면 그 시각을 적고 true.</summary>
        public bool TryStart(long nowMs)
        {
            if (LeftMs(nowMs) > 0) return false;
            last = nowMs;
            return true;
        }

        /// <summary>다시 받을 수 있을 때까지 남은 ms(0 = 지금 된다).</summary>
        public long LeftMs(long nowMs) => last.HasValue ? Math.Max(0, last.Value + GapMs - nowMs) : 0;
    }

    /// <summary>
    /// 접속 실패 코드 → 화면 문구 키(02 §2-5 — 문구는 screens.json <c>screen.net.fail.&lt;코드 소문자&gt;</c> + <c>.tip</c>, 화면 ①). 모르는 코드는 <c>other</c>.
    /// 손님 쪽 로컬 코드(Net의 NetFail — NET_BAD_ADDRESS 등)는 Net이 엔진 쪽이라 여기 글자 그대로 둔다(같은 값을 쓴다). 값(Detail)은 「a/b」 — E_VERSION 방장/내 빌드 · E_FULL 인원/자리 수.
    /// </summary>
    public static class NetFailText
    {
        public const string Prefix = "screen.net.fail.";
        public const string Other = "other";
        /// <summary>방장: S04에서 고른 방 저장을 이어 열지 못했다(형식 판 · 데이터가 다르다 — 화면 ②).</summary>
        public const string ResumeFail = "NET_RESUME";

        /// <summary>문구가 있는 코드 전부(시험이 문자열 표와 견준다).</summary>
        public static readonly string[] Codes =
        {
            "NET_BAD_ADDRESS", "NET_UNREACHABLE", "NET_LOST", "NET_PORT_BUSY", "NET_HOST_SAVED", ResumeFail, OnlineFail.RelayAuth, OnlineFail.RelayCode, OnlineFail.ListFail,
            NetCode.Version, NetCode.Content, NetCode.Kicked, NetCode.Duplicate, NetCode.Full, NetCode.InGame, NetCode.BadHello, NetCode.HostClosed, NetCode.SeatRefused,
        };

        /// <summary>화면 문구 키(짧은 줄). 자세한 말은 이 키 + <c>.tip</c>.</summary>
        public static string Key(string code) => Prefix + (code != null && Array.IndexOf(Codes, code) >= 0 ? code.ToLowerInvariant() : Other);

        /// <summary>문구의 자리 표시({h} {c} · {n} {max} · {p})를 값으로 채운다. 값이 없으면 「?」.</summary>
        public static string Fill(string text, string code, string detail, int port = 0)
        {
            if (text == null) return "";
            string a = "?", b = "?";
            if (!string.IsNullOrEmpty(detail))
            {
                int bar = detail.IndexOf('/');
                a = bar < 0 ? detail : detail.Substring(0, bar);
                b = bar < 0 ? "?" : detail.Substring(bar + 1);
            }
            return text.Replace("{h}", a).Replace("{c}", b).Replace("{n}", a).Replace("{max}", b).Replace("{p}", port.ToString(CultureInfo.InvariantCulture));
        }
    }
}
