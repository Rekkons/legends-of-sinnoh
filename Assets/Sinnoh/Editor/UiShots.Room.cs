using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M7 「화면 ① 방 만들기 · 참가 · 로비 · 방 설정」 확인 장면(04 S05~S08 · 02 §2-5 · §3, 결정 92·95): 망 없이 — 방 목록은 가짜 서비스(<see cref="FakeRooms"/>), 로비는 가짜 방(<see cref="FakeLobby"/>),
    /// 실패 문구는 <see cref="RoomFlow.ShowFail"/>로 띄운다(편집 모드에는 NGO가 돌지 않는다 — 실제 접속은 PlayMode <c>RoomFlowTests</c>). 실제 UGS·내 PC 주소를 찍지 않는다.
    /// 장면마다 그 화면의 글이 칸 밖으로 넘치지 않는지 · 판이 화면 안인지 · 로비의 두 판이 겹치지 않는지 단정한다. 모두 보드 위 화면이다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RoomScreens =
        {
            Screen("room-create", s => RoomCreate(s, null)),                                 // S05 기본: [인터넷(추천)] · 목록에 올리기 · 6명 · 신오의 심장
            Screen("room-create-direct", s => RoomCreate(s, c => { c.SetInternet(false); c.SetSeats(4); })),   // [직접 연결]: 목록 줄 흐림 · 포트 안내
            Screen("room-create-tip", s =>                                                     // [직접 연결]에 마우스를 올린 모습(설명 툴팁)
            {
                var flow = RoomCreate(s, null);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-create-opt-mode-direct"));
            }),
            Screen("room-create-fail", s =>                                                    // Relay 실패(결정 92): 「Unity 서버를 쓸 수 없습니다.」 + [직접 연결로]
            {
                var flow = s.Flow();
                flow.OpenTitle();
                flow.Room.ShowFail(OnlineFail.RelayAuth, null, true);
                if (flow.Stage.Root.Q("room-create-to-direct") == null) throw new InvalidOperationException("[직접 연결로]가 없다");
                RoomFit(s, flow, "room-create-col");
            }),
            Screen("room-create-fail-tip", s =>                                                // 실패 문구의 자세한 말(마우스 오버)
            {
                var flow = s.Flow();
                flow.OpenTitle();
                flow.Room.ShowFail(OnlineFail.RelayAuth, null, true);
                RoomFit(s, flow, "room-create-col");
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-create-fail-text"));
            }),
            Screen("room-join", s => RoomJoin(s, FakeRooms(), null)),                         // S06: 공개 방 넷(들어갈 수 있는 방 둘 · 가득 참 · 버전 다름) — 게임 중인 방은 빠진다
            Screen("room-join-tip", s =>                                                       // 버전 다른 줄에 마우스를 올린 모습
            {
                var flow = RoomJoin(s, FakeRooms(), null);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-join-row-3"));
            }),
            Screen("room-join-empty", s => RoomJoin(s, new FakeBoard(), null)),                // 공개 방이 없다 · [새로 고침]은 5초 동안 꺼짐
            Screen("room-join-bad", s => RoomJoin(s, FakeRooms(), j => { j.SetTarget("100.101.102"); j.Join(); })),   // 틀린 주소: 「주소 형식이 올바르지 않습니다.」
            Screen("room-join-fail", s =>                                                      // 방 목록 실패 + Unity 서버 실패(결정 92): 직접 연결 주소를 받으라는 안내
            {
                var flow = s.Flow();
                flow.OpenTitle();
                flow.Room.Board = new FakeBoard { FailQuery = OnlineFail.ListFail };
                flow.Room.OpenJoin();
                flow.Room.ShowFail(OnlineFail.RelayAuth, null, false);
                if (flow.Stage.Root.Q("room-join-direct-hint") == null || flow.Stage.Root.Q("room-join-list-fail") == null) throw new InvalidOperationException("직접 연결 안내 · 목록 실패 칸이 없다");
                RoomFit(s, flow, "room-join-col");
            }),
            Screen("room-lobby-host", s => RoomLobby(s, FakeLobby.Internet())),                // S07·S08 방장(인터넷 방): 참가 코드 · 목록에 올림 · 4명(둘 준비) · [게임 시작] 꺼짐
            Screen("room-lobby-direct", s => RoomLobby(s, FakeLobby.Direct())),                // 방장(직접 연결): 주소 둘 · 포트 · 6명 모두 준비 → [게임 시작] 켜짐
            Screen("room-lobby-tunnel", s =>                                                   // M7 웹 손님: Relay 실패 → 두 번째 터널(참가 주소 wss:// + [복사]) · 웹 링크 ?join=
            {
                var l = FakeLobby.Internet();
                l.Tunneled = true; l.Listed = false;
                l.JoinCode = "wss://lazy-dog-sleeps-well.trycloudflare.com";
                l.WebLinks = new[] { "https://quick-brown-fox-jumps.trycloudflare.com/?join=lazy-dog-sleeps-well.trycloudflare.com" };
                var flow = RoomLobby(s, l);
                if (flow.Stage.Root.Q("lobby-code-copy") == null || flow.Stage.Root.Q("lobby-web-copy") == null) throw new InvalidOperationException("[복사] 단추가 없다");
            }),
            Screen("room-lobby-noweb", s =>                                                    // 웹 손님판 폴더가 없는 방: 「웹 손님판이 없습니다.」(NET_NO_WEBGL) — 방은 그대로
            {
                var l = FakeLobby.Internet();
                l.WebLinks = new string[0]; l.WebNotice = WebFail.NoWebGl;
                var flow = RoomLobby(s, l);
                if (flow.Stage.Root.Q("lobby-web-fail") == null) throw new InvalidOperationException("웹 링크 알림 칸이 없다");
            }),
            Screen("room-lobby-guest", s => RoomLobby(s, FakeLobby.Guest())),                  // 손님(3번 자리 · 준비 안 함): 「○○의 방」 · 울트라 버스트 방 설정 읽기만
            Screen("room-lobby-proto", s =>                                                    // M9 다듬기 P06: 손님이 「내 주인공」에서 다른 인물을 누른다 → 방에 보냄 · 방이 돌려준 값으로 줄 · 금색 칸
            {
                var lobby = FakeLobby.Guest();
                var flow = RoomLobby(s, lobby);
                var screen = (LobbyScreen)flow.Stage.Current;
                if (screen.ProtagonistShown(2) != "bw_001_hilda" || screen.ProtagonistShown(0) != "pt_player_male") throw new InvalidOperationException("자리 줄의 주인공 도트가 방 상태와 다르다");
                if (!screen.PickProtagonist("b2w2_001_rosa") || !(lobby.Sent[lobby.Sent.Count - 1] is PickProtagonist pick) || pick.Key != "b2w2_001_rosa") throw new InvalidOperationException("「내 주인공」이 방에 보내지 않았다");
                if (screen.ProtagonistShown(2) != "bw_001_hilda") throw new InvalidOperationException("방이 돌려주기 전에 줄을 바꿨다");
                // 방이 돌려준 방 상태(새 객체)
                var next = new RoomState { Phase = RoomPhase.Lobby, RoomId = lobby.Room.RoomId, Settings = lobby.Room.Settings, Seats = new List<RoomSeat>(lobby.Room.Seats) };
                next.Seats[2] = new RoomSeat { Nickname = next.Seats[2].Nickname, Connected = true, Protagonist = "b2w2_001_rosa" };
                lobby.Room = next;
                screen.Sync();
                RoomFit(s, flow, "lobby-col");
                var opt = flow.Stage.Root.Q("lobby-proto-b2w2_001_rosa");
                if (screen.ProtagonistShown(2) != "b2w2_001_rosa" || opt == null || !opt.ClassListContains("sn-opt-picked")) throw new InvalidOperationException("고른 주인공이 줄 · 칸에 보이지 않는다");
                var box = flow.Stage.Root.Q("lobby-proto").worldBound;
                foreach (string key in Protagonists.Keys)
                {
                    var at = flow.Stage.Root.Q("lobby-proto-" + key).worldBound;
                    if (at.width < 1f || at.xMax > flow.Stage.Root.Q("lobby-col").worldBound.xMax + 1f || at.yMin < box.yMin - 1f || at.yMax > box.yMax + 1f) throw new InvalidOperationException("주인공 칸이 줄 밖: " + key + " " + at);
                }
                flow.Stage.Tips.Show(opt);
            }),
            Screen("room-lobby-tip", s =>                                                      // 방장의 제한 시간 「느긋하게」에 마우스를 올린 모습(수치 툴팁)
            {
                var flow = RoomLobby(s, FakeLobby.Internet());
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-set-opt-clock-relaxed"));
            }),
            // 결정 140 ① 제한 시간 세부(M10): 프리셋 줄 아래 두 열 열 칸 — 방장 [−] 값 [+] · 직접 설정 · 손님 읽기 + 「변경됨」
            Screen("room-clock-host", s => ClockCheck(s, RoomLobby(s, FakeLobby.Internet()), true)),   // 방장 · 「느긋하게」 값
            Screen("room-clock-custom", s =>                                                   // 방장이 [+] · [−]를 눌러 직접 설정이 된 방(방이 돌려준 상태)
            {
                var lobby = FakeLobby.Internet();
                var flow = RoomLobby(s, lobby);
                var screen = (LobbyScreen)flow.Stage.Current;
                if (!screen.Settings.StepClock("map_turn", +1) || !(lobby.Sent[lobby.Sent.Count - 1] is ChangeSettings sent) || sent.Settings.Clock != ClockPreset.Custom) throw new InvalidOperationException("[+]가 직접 설정을 보내지 않았다");
                var next = sent.Settings.Clone();
                next.StepClock("bank_start", +1);
                next.StepClock("reaction", -1);
                lobby.Room = new RoomState { Phase = RoomPhase.Lobby, RoomId = lobby.Room.RoomId, Settings = next, Seats = lobby.Room.Seats };
                screen.Sync();
                ClockCheck(s, flow, true);
                if (!(flow.Stage.Root.Q("room-set-opt-clock-custom")?.ClassListContains("sn-opt-picked") ?? false)) throw new InvalidOperationException("「직접 설정」 칸이 없다");
            }),
            Screen("room-clock-tip", s =>                                                      // 칸 이름에 마우스를 올린 모습(뜻 + 범위)
            {
                var flow = ClockCheck(s, RoomLobby(s, FakeLobby.Internet()), true);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-set-clock-bank_round-label"));
            }),
            Screen("room-clock-guest", s =>                                                    // 손님: 방장이 바꾼 칸 · 프리셋 줄에 「변경됨」
            {
                var lobby = FakeLobby.Guest();
                var flow = RoomLobby(s, lobby);
                var screen = (LobbyScreen)flow.Stage.Current;
                var next = lobby.Room.Settings.Clone();
                next.StepClock("map_turn", +1);
                next.StepClock("setup_pick", +1);
                next.StepClock("bank_max", -1);
                lobby.Room = new RoomState { Phase = RoomPhase.Lobby, RoomId = lobby.Room.RoomId, Settings = next, Seats = lobby.Room.Seats };
                screen.Sync();
                ClockCheck(s, flow, false);
                foreach (string name in new[] { "room-set-clock-changed", "room-set-clock-map_turn-changed", "room-set-clock-setup_pick-changed", "room-set-clock-bank_max-changed" })
                    if (flow.Stage.Root.Q(name) == null) throw new InvalidOperationException("「변경됨」이 없다: " + name);
                if (flow.Stage.Root.Q("room-set-clock-reaction-changed") != null) throw new InvalidOperationException("안 바뀐 칸에 「변경됨」");
            }),
        };

        // 시간 세부 칸(결정 140 ①): 열 칸이 모두 방 설정 판 안 · 칸끼리 겹치지 않음 · 방장만 [−] [+] · 판 글 넘침 없음
        static GameFlow ClockCheck(UiShot s, GameFlow flow, bool host)
        {
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var panel = root.Q("lobby-set").worldBound;
            var cells = new List<UnityEngine.Rect>();
            foreach (var f in ClockSettings.Fields)
            {
                var cell = root.Q("room-set-clock-" + f.Key) ?? throw new InvalidOperationException("시간 칸이 없다: " + f.Key);
                var at = cell.worldBound;
                if (at.width < 1f || at.xMin < panel.xMin - 1f || at.xMax > panel.xMax + 1f || at.yMax > panel.yMax + 1f) throw new InvalidOperationException("시간 칸이 판 밖: " + f.Key + " " + at + " / " + panel);
                foreach (var other in cells)
                    if (other.Overlaps(new UnityEngine.Rect(at.x + 1f, at.y + 1f, at.width - 2f, at.height - 2f))) throw new InvalidOperationException("시간 칸이 겹친다: " + f.Key);
                cells.Add(at);
                if ((root.Q("room-set-clock-" + f.Key + "-plus") != null) != host) throw new InvalidOperationException("[+]는 방장만: " + f.Key);
                foreach (var part in new[] { "-label", "-value", "-minus", "-plus" })
                {
                    var e = root.Q("room-set-clock-" + f.Key + part);
                    if (e == null) continue;
                    var b = e.worldBound;
                    if (b.xMin < at.xMin - 1f || b.xMax > at.xMax + 1f) throw new InvalidOperationException("칸 안 요소가 칸 밖: " + f.Key + part + " " + b + " / " + at);
                }
            }
            RoomFit(s, flow, "lobby-set");
            return flow;
        }

        static GameFlow RoomCreate(UiShot s, Action<RoomCreateScreen> act)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            flow.Room.OpenCreate();
            var create = flow.Stage.Current as RoomCreateScreen ?? throw new InvalidOperationException("방 만들기가 열리지 않았다");
            act?.Invoke(create);
            RoomFit(s, flow, "room-create-col");
            return flow;
        }

        static GameFlow RoomJoin(UiShot s, FakeBoard board, Action<RoomJoinScreen> act)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            board.Stamp = flow.Room.Stamp;
            flow.Room.Board = board;
            flow.Room.OpenJoin();
            var join = flow.Stage.Current as RoomJoinScreen ?? throw new InvalidOperationException("참가하기가 열리지 않았다");
            if (board.Queries != 1) throw new InvalidOperationException("화면을 열 때 방 목록을 한 번 받아야 한다: " + board.Queries);
            if (join.PressRefresh() || board.Queries != 1) throw new InvalidOperationException("[새로 고침]이 5초 안에 다시 받았다");
            act?.Invoke(flow.Stage.Current as RoomJoinScreen);
            RoomFit(s, flow, "room-join-col");
            return flow;
        }

        static GameFlow RoomLobby(UiShot s, FakeLobby lobby)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            flow.Room.ShowLobby(lobby);
            var screen = flow.Stage.Current as LobbyScreen ?? throw new InvalidOperationException("로비가 열리지 않았다");
            RoomFit(s, flow, "lobby-col");
            RoomFit(s, flow, "lobby-set");
            Apart(flow.Stage.Root, "lobby-col", "lobby-set");
            if (screen.CanStart != (flow.Stage.Root.Q<Button>("lobby-start")?.enabledSelf ?? false) && lobby.IsHost) throw new InvalidOperationException("[게임 시작] 켜짐이 방 상태와 다르다");
            return flow;
        }

        // 그 판의 글이 칸 밖으로 넘치지 않고 판이 화면 안에 든다
        static void RoomFit(UiShot s, GameFlow flow, string name)
        {
            s.Panel.Tick(3);
            var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            var bad = WorkUi.Overflows(e);
            if (bad.Count > 0) throw new InvalidOperationException(name + " 글 넘침: " + string.Join(" / ", bad));
            var root = flow.Stage.Root.worldBound;
            var at = e.worldBound;
            if (at.xMin < root.xMin - 1f || at.yMin < root.yMin - 1f || at.xMax > root.xMax + 1f || at.yMax > root.yMax + 1f)
                throw new InvalidOperationException(name + "이(가) 화면 밖으로 나간다: " + at + " / " + root);
        }

        static FakeBoard FakeRooms()
        {
            var b = new FakeBoard();
            b.Add("lb-1", 5, new ListingData { Host = "빛나", Members = 2, Seats = 4, Campaign = Campaign.HeartOfSinnoh, Clock = ClockPreset.Relaxed });
            b.Add("lb-2", 4, new ListingData { Host = "열두글자방장닉네임입니다", Members = 3, Seats = 6, Campaign = Campaign.UltraBurst, Clock = ClockPreset.Normal, PcOnly = true });
            b.Add("lb-3", 3, new ListingData { Host = "Dawn", Members = 3, Seats = 3, Campaign = Campaign.HeartOfSinnoh, Clock = ClockPreset.Off });
            b.Add("lb-4", 6, new ListingData { Host = "뽀뽀라", Members = 1, Seats = 6, Campaign = Campaign.HeartOfSinnoh, Clock = ClockPreset.Relaxed }, otherVersion: true);
            b.Add("lb-5", 7, new ListingData { Host = "게임 중", Members = 2, Seats = 2, State = ListingData.Game });
            return b;
        }

        /// <summary>UGS Lobby 대신(캡처): 조회는 그 자리에서 끝난다. 빌드 표지는 조회 때 그 흐름의 것으로 채운다(버전 다른 줄만 다른 값).</summary>
        sealed class FakeBoard : ILobbyBoard
        {
            public string FailQuery;
            public int Queries;
            readonly List<(LobbyRow row, bool other)> rows = new List<(LobbyRow, bool)>();

            public void Add(string id, long created, ListingData data, bool otherVersion = false)
            {
                data.JoinCode = "Q7K2M" + id.Substring(id.Length - 1);
                rows.Add((new LobbyRow { Id = id, CreatedMs = created, Data = data.ToDictionary() }, otherVersion));
            }

            /// <summary>조회 줄의 빌드 표지(그 흐름의 Stamp).</summary>
            public string Stamp = "";

            public void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail) => fail(OnlineFail.ListFail);
            public void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Delete(string lobbyId, Action done) => done();

            public void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail)
            {
                Queries++;
                if (FailQuery != null)
                {
                    fail(FailQuery);
                    return;
                }
                var list = new List<LobbyRow>();
                foreach (var (row, other) in rows)
                {
                    var d = new Dictionary<string, string>(row.Data);
                    var data = ListingData.FromDictionary(d);
                    data.Stamp = other ? "00000000" : Stamp;
                    list.Add(new LobbyRow { Id = row.Id, CreatedMs = row.CreatedMs, Data = data.ToDictionary() });
                }
                ok(list);
            }
        }

        /// <summary>가짜 방(캡처): 정해 둔 방 상태 · 초대 칸. 보낸 것은 적기만 한다.</summary>
        sealed class FakeLobby : ILobbySource
        {
            public RoomState Room { get; set; }
            public int Seat { get; set; }
            public bool IsHost { get; set; }
            public string JoinCode { get; set; }
            public bool Listed { get; set; }
            public string ListFail { get; set; }
            public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; set; } = new KeyValuePair<string, string>[0];
            public int Port { get; set; }
            public bool Tunneled { get; set; }
            public IReadOnlyList<string> WebLinks { get; set; } = new string[0];
            public string WebNotice { get; set; }
            public bool WebPending { get; set; }
            public readonly List<RoomMsg> Sent = new List<RoomMsg>();
            public void Send(RoomMsg msg) => Sent.Add(msg);

            static RoomSeat Member(string nick, bool host = false, bool ready = false, Platform platform = Platform.Pc) =>
                new RoomSeat { Nickname = nick, Host = host, Ready = ready, Connected = true, Platform = platform };

            // 좌석의 주인공(M9 다듬기 P06): 자리마다 다른 인물 — 3번 · 4번 자리는 같은 인물(겹쳐 고르기)
            static readonly string[] Heroes = { "pt_player_male", "pt_player_female", "bw_001_hilda", "bw_001_hilda", "hgss_000_ethan", "b2w2_001_rosa" };

            static FakeLobby Dress(FakeLobby l)
            {
                for (int i = 0; i < l.Room.Seats.Count; i++)
                    if (l.Room.Seats[i] != null) l.Room.Seats[i].Protagonist = Heroes[i % Heroes.Length];
                return l;
            }

            public static FakeLobby Internet() => Dress(new FakeLobby
            {
                IsHost = true, Seat = 0, JoinCode = "Q7K2MZ", Listed = true, WebLinks = new[] { "https://quick-brown-fox-jumps.trycloudflare.com/?code=Q7K2MZ" },
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot", Settings = new RoomSettings { Seats = 5 },
                    Seats = new List<RoomSeat> { Member("방장트레이너", host: true), Member("빛나", ready: true), Member("용식"), Member("Barry", ready: true, platform: Platform.Web), null, null },
                },
            });

            public static FakeLobby Direct() => Dress(new FakeLobby
            {
                IsHost = true, Seat = 0, Port = 7777, WebLinks = new[] { "http://100.101.102.103:8080/", "http://192.168.0.10:8080/" },
                Addresses = new[] { new KeyValuePair<string, string>("100.101.102.103", "Tailscale"), new KeyValuePair<string, string>("192.168.0.10", "LAN") },
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot", CanStart = true, Settings = new RoomSettings { Seats = 6, Clock = ClockPreset.Normal, Galactic = Control.Human },
                    Seats = new List<RoomSeat>
                    {
                        Member("방장트레이너", host: true), Member("빛나", ready: true), Member("용식", ready: true), Member("Barry", ready: true), Member("열두글자닉네임입니다요요", ready: true), Member("Lucas", ready: true, platform: Platform.Web),
                    },
                },
            });

            public static FakeLobby Guest() => Dress(new FakeLobby
            {
                IsHost = false, Seat = 2,
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot",
                    Settings = new RoomSettings { Seats = 4, Campaign = Campaign.UltraBurst, UbDifficulty = UbDifficulty.VeteranTrainer, UltraOnslaught = true, Trading = false },
                    Seats = new List<RoomSeat> { Member("빛나", host: true), Member("용식", ready: true), Member("나의닉네임"), null, null, null },
                },
            });
        }
    }
}
