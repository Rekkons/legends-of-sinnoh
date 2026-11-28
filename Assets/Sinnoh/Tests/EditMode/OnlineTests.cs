using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「Net ② 인터넷 방 · 방 목록」의 엔진·UGS 무관 몫(02 §2-1 · §2-5 · §3-1, 결정 92 · 95 [가정 M7-6 · M7-35~]): 망 없이 가짜 서비스 층(<see cref="FakeBoard"/> · <see cref="FakeRelay"/>)으로
    /// 방 목록 데이터·순서, 공개 방 올리기 · 하트비트 · 갱신 묶음 · 닫을 때 지우기, 인터넷 방 열기 흐름(비공개는 목록에 없음 · Relay 실패 → 직접 연결 안내), Relay 시도의 늦은 결과 버리기를 본다.
    /// 실제 UGS 호출은 PlayMode <c>OnlineUgsTests</c>([Explicit] — 관문 밖).
    /// </summary>
    public sealed class OnlineTests
    {
        const string Stamp = "abcd1234";

        /// <summary>UGS Lobby 대신: 요청을 적어 두고, 시험이 <see cref="Finish"/>로 하나씩 끝낸다(Auto면 바로 성공).</summary>
        sealed class FakeBoard : ILobbyBoard
        {
            public bool Auto = true;
            public string FailCreate, FailQuery;
            public readonly List<string> Calls = new List<string>();
            public readonly Dictionary<string, LobbyRow> Lobbies = new Dictionary<string, LobbyRow>();
            readonly Queue<Action> pending = new Queue<Action>();
            int next;
            public long Now;
            public int Pending => pending.Count;

            void Later(Action a)
            {
                if (Auto) a();
                else pending.Enqueue(a);
            }

            public void Finish() => pending.Dequeue()();

            public void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail)
            {
                Calls.Add($"create {name} {maxPlayers}");
                Later(() =>
                {
                    if (FailCreate != null) { fail(FailCreate); return; }
                    var id = "L" + ++next;
                    Lobbies[id] = new LobbyRow { Id = id, CreatedMs = Now, Data = new Dictionary<string, string>(data) };
                    ok(id);
                });
            }

            public void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail)
            {
                Calls.Add($"update {lobbyId} n={data["n"]} s={data["s"]}" + (maxPlayers.HasValue ? $" max={maxPlayers}" : ""));
                Later(() =>
                {
                    if (!Lobbies.TryGetValue(lobbyId, out var row)) { fail(OnlineFail.ListFail, true); return; }
                    row.Data = new Dictionary<string, string>(data);
                    ok();
                });
            }

            public void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail)
            {
                Calls.Add($"beat {lobbyId} @{Now / 1000}");
                Later(() =>
                {
                    if (!Lobbies.ContainsKey(lobbyId)) { fail(OnlineFail.ListFail, true); return; }
                    ok();
                });
            }

            public void Delete(string lobbyId, Action done)
            {
                Calls.Add($"delete {lobbyId}");
                Later(() =>
                {
                    Lobbies.Remove(lobbyId);
                    done();
                });
            }

            public void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail)
            {
                Calls.Add("query");
                Later(() =>
                {
                    if (FailQuery != null) fail(FailQuery);
                    else ok(Lobbies.Values.ToList());
                });
            }
        }

        sealed class FakeRelay : IRelayHost
        {
            public string Fail;
            public string Code = "Q7K2MZ";
            public int Asked = -1;
            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail)
            {
                Asked = maxConnections;
                if (Fail != null) fail(Fail);
                else ok(Code);
            }
        }

        static RoomState Room(RoomPhase phase, int members, int seats = 6, string host = "방장")
        {
            var r = new RoomState { Phase = phase, RoomId = "room", Settings = new RoomSettings { Seats = seats, Campaign = Campaign.UltraBurst, Clock = ClockPreset.Normal } };
            for (int i = 0; i < SeatId.MaxSeats; i++)
                r.Seats.Add(i < members ? new RoomSeat { Nickname = i == 0 ? host : "g" + i, Host = i == 0, Connected = true } : null);
            return r;
        }

        static LobbyRow Row(string id, int minute, int members, int seats, string state = ListingData.Lobby, string stamp = Stamp, string code = "ABC123") => new LobbyRow
        {
            Id = id, CreatedMs = minute * 60000L,
            Data = new ListingData { Host = id, Members = members, Seats = seats, State = state, Stamp = stamp, JoinCode = code }.ToDictionary(),
        };

        [Test]
        public void M7_방목록_로비데이터는짧은키8개로왕복하고_방상태에서만든다_게임중은game_빌드표지는판이다르면다르다()
        {
            var opts = new ListingOptions { HostName = "예비", Stamp = Stamp };
            var d = RoomListingModel.Build(opts, Room(RoomPhase.Lobby, 3, 4), "Q7K2MZ");
            var dict = d.ToDictionary();
            CollectionAssert.AreEquivalent(new[] { "h", "n", "m", "k", "c", "s", "v", "j" }, dict.Keys, "토큰·좌석 닉네임은 싣지 않는다");
            Assert.AreEqual(("방장", 3, 4, Campaign.UltraBurst, ClockPreset.Normal, ListingData.Lobby, Stamp, "Q7K2MZ"),
                (d.Host, d.Members, d.Seats, d.Campaign, d.Clock, d.State, d.Stamp, d.JoinCode), "방장 닉네임은 1번 자리");
            var back = ListingData.FromDictionary(dict);
            Assert.IsTrue(back.SameAs(d), "왕복");
            Assert.AreEqual(ListingData.Game, RoomListingModel.Build(opts, Room(RoomPhase.InGame, 3, 4), "Q7K2MZ").State);
            Assert.AreEqual(ListingData.Game, RoomListingModel.Build(opts, Room(RoomPhase.Waiting, 0, 4), "Q7K2MZ").State, "이어하기 대기도 game");
            Assert.AreEqual("예비", RoomListingModel.Build(opts, null, null).Host, "방 상태가 없으면 옵션 닉네임");
            Assert.IsFalse(RoomListingModel.Build(opts, Room(RoomPhase.Lobby, 4, 4), "Q7K2MZ").SameAs(d), "한 칸이라도 다르면 보낸다");

            var junk = ListingData.FromDictionary(new Dictionary<string, string> { ["n"] = "x", ["k"] = "9", ["c"] = "-1" });
            Assert.AreEqual((0, Campaign.HeartOfSinnoh, ClockPreset.Off, ""), (junk.Members, junk.Campaign, junk.Clock, junk.JoinCode), "읽을 수 없는 값은 0·기본");

            var hello = new Hello { BuildId = "b1", ContentHash = "c1" };
            var s0 = RoomListingModel.Stamp(hello);
            Assert.AreEqual(8, s0.Length);
            Assert.AreEqual(s0, RoomListingModel.Stamp(new Hello { BuildId = "b1", ContentHash = "c1", Token = "다른 토큰", Nickname = "x" }), "토큰·닉네임은 표지 밖");
            Assert.AreNotEqual(s0, RoomListingModel.Stamp(new Hello { BuildId = "b2", ContentHash = "c1" }));
            Assert.AreNotEqual(s0, RoomListingModel.Stamp(new Hello { BuildId = "b1", ContentHash = "c2" }));
            Assert.AreNotEqual(s0, RoomListingModel.Stamp(new Hello { BuildId = "b1", ContentHash = "c1", ProtocolVersion = Protocol.Version + 1 }));
            Assert.AreEqual("sinnoh", RoomListingModel.LobbyName("  "));
        }

        [Test]
        public void M7_방목록_로비단계방만보이고_들어갈수있는방최근순_가득참_버전다름은회색()
        {
            var rows = new[]
            {
                Row("old-open", 1, 2, 6),
                Row("new-open", 9, 1, 4),
                Row("full", 8, 4, 4),
                Row("other", 7, 1, 6, stamp: "ffff0000"),
                Row("playing", 6, 3, 6, state: ListingData.Game),
                Row("nocode", 5, 1, 6, code: ""),
            };
            var q = ListingQuery.Done(rows, Stamp);
            Assert.IsTrue(q.Ok);
            CollectionAssert.AreEqual(new[] { "new-open", "old-open", "full", "other" }, q.Rooms.Select(r => r.Id).ToArray(), "게임 중·코드 없는 방은 없다");
            var byId = q.Rooms.ToDictionary(r => r.Id);
            Assert.IsTrue(byId["new-open"].Joinable(Stamp));
            Assert.IsTrue(byId["full"].Full && !byId["full"].Joinable(Stamp), "가득 참 = 회색");
            Assert.IsTrue(byId["other"].OtherVersion(Stamp) && !byId["other"].Joinable(Stamp), "버전 다름 = 회색(빼지 않는다)");
            Assert.AreEqual("ABC123", byId["new-open"].JoinCode, "고르면 이 코드로 들어간다");

            var fail = ListingQuery.Fail(null);
            Assert.AreEqual((false, OnlineFail.ListFail, 0), (fail.Ok, fail.FailCode, fail.Rooms.Count));
            Assert.IsTrue(OnlineFail.SuggestDirect(fail.FailCode), "목록 실패 → 직접 연결 안내(결정 92)");
        }

        [Test]
        public void M7_방목록올리기_하트비트15초_갱신은1초묶음_같은값은안보냄_로비가없어지면멈춤_닫으면지움()
        {
            var board = new FakeBoard();
            var p = new ListingPublisher(board);
            var data = new ListingData { Host = "방장", Members = 1, Seats = 6, Stamp = Stamp, JoinCode = "Q7K2MZ" };
            p.Publish(data);
            Assert.IsTrue(p.Published);
            Assert.AreEqual("create 방장 6", board.Calls.Single());

            void At(long ms) { board.Now = ms; p.Tick(ms); }
            At(0);
            p.Update(data);
            At(1000);
            At(14999);
            Assert.AreEqual(1, board.Calls.Count, "같은 값은 보내지 않고, 15초 전에는 하트비트도 없다");
            At(15000);
            Assert.AreEqual("beat L1 @15", board.Calls.Last());

            var two = new ListingData { Host = "방장", Members = 2, Seats = 6, Stamp = Stamp, JoinCode = "Q7K2MZ" };
            var three = new ListingData { Host = "방장", Members = 3, Seats = 5, Stamp = Stamp, JoinCode = "Q7K2MZ" };
            p.Update(two);
            At(15100);
            p.Update(three);
            At(15500);
            Assert.AreEqual("update L1 n=2 s=lobby", board.Calls.Last(), "1초 안의 갱신은 묶는다");
            At(16100);
            Assert.AreEqual("update L1 n=3 s=lobby max=5", board.Calls.Last(), "자리 수가 바뀌면 최대 인원도");
            At(30000);
            Assert.AreEqual("update L1 n=3 s=lobby max=5", board.Calls.Last(), "갱신도 활동이다 — 하트비트는 마지막 갱신 15초 뒤");
            At(31100);
            Assert.AreEqual("beat L1 @31", board.Calls.Last());
            Assert.AreEqual((2, 2), (p.Beats, p.Updates), "하트비트 2 · 갱신 2");

            board.Lobbies.Clear();   // 만료·지워짐
            At(46100);
            Assert.IsFalse(p.Published, "로비가 없어졌으면 그만 보낸다");
            Assert.AreEqual(OnlineFail.ListFail, p.FailCode);
            int calls = board.Calls.Count;
            At(70000);
            Assert.AreEqual(calls, board.Calls.Count);
            p.Close();
            Assert.IsTrue(p.Deleted, "올린 것이 없으면 바로 끝");

            // 닫기: 올린 방은 지운다 · 만드는 중에 닫으면 만든 뒤 바로 지운다
            var b2 = new FakeBoard();
            var p2 = new ListingPublisher(b2);
            p2.Publish(data);
            p2.Close();
            Assert.AreEqual("delete L1", b2.Calls.Last());
            Assert.IsTrue(p2.Deleted && !b2.Lobbies.Any());

            var b3 = new FakeBoard { Auto = false };
            var p3 = new ListingPublisher(b3);
            p3.Publish(data);
            p3.Close();
            Assert.IsFalse(p3.Idle, "만드는 중");
            b3.Finish();
            Assert.AreEqual("delete L1", b3.Calls.Last(), "만든 뒤 바로 지운다");
            Assert.IsFalse(p3.Deleted);
            b3.Finish();
            Assert.IsTrue(p3.Deleted && p3.Idle && !b3.Lobbies.Any());

            var b4 = new FakeBoard { FailCreate = OnlineFail.RelayAuth };
            var p4 = new ListingPublisher(b4);
            p4.Publish(data);
            Assert.AreEqual((false, OnlineFail.RelayAuth), (p4.Published, p4.FailCode), "만들기 실패는 던지지 않고 코드");
        }

        [Test]
        public void M7_인터넷방_Relay코드로열고공개방은목록에올림_게임을시작하면목록에서안보임_닫으면지움_비공개는목록에없음()
        {
            var board = new FakeBoard();
            var relay = new FakeRelay();
            var room = Room(RoomPhase.Lobby, 1);
            int started = 0;
            var opts = new ListingOptions { HostName = "방장", Stamp = Stamp };
            var net = new InternetRoom(relay, () => { started++; return null; }, () => room, opts, board);
            int changed = 0;
            net.Changed += () => changed++;
            net.Open();
            Assert.AreEqual((InternetRoom.Phase.Ready, "Q7K2MZ", 1, 5), (net.State, net.JoinCode, started, relay.Asked), "maxConnections = 자리 수 6 − 1 [가정 M7-35]");
            Assert.AreEqual(1, changed);
            Assert.IsTrue(net.Listing.Published);

            ListingQuery seen = null;
            void Browse() => board.Query(rows => seen = ListingQuery.Done(rows, Stamp), c => seen = ListingQuery.Fail(c));
            Browse();
            Assert.AreEqual("Q7K2MZ", seen.Rooms.Single().JoinCode, "공개 방은 목록에 뜨고 그 코드로 들어간다");
            Assert.AreEqual(1, seen.Rooms.Single().Members);

            room = Room(RoomPhase.Lobby, 3);
            net.Tick(0);
            net.Tick(1000);
            Browse();
            Assert.AreEqual(3, seen.Rooms.Single().Members, "방 상태는 1초에 한 번 읽어 갱신");
            room = Room(RoomPhase.InGame, 3);
            net.Tick(2000);
            net.Tick(3000);
            Browse();
            Assert.IsEmpty(seen.Rooms, "게임을 시작하면 목록에서 보이지 않는다(로비 단계 방만)");
            room = Room(RoomPhase.Lobby, 3);
            net.Tick(4000);
            net.Tick(5000);
            Browse();
            Assert.AreEqual(1, seen.Rooms.Count, "[로비로] 돌아오면 다시 보인다");

            room = Room(RoomPhase.Closed, 0);
            net.Tick(6000);
            Assert.AreEqual(InternetRoom.Phase.Closed, net.State, "방이 닫히면 목록에서 지운다");
            Assert.IsNull(net.JoinCode);
            Assert.IsEmpty(board.Lobbies);
            net.Dispose();
            Assert.AreEqual(1, board.Calls.Count(c => c.StartsWith("delete")), "두 번 지우지 않는다");
            board.FailQuery = OnlineFail.ListFail;
            Browse();
            Assert.AreEqual((false, 0, true), (seen.Ok, seen.Rooms.Count, OnlineFail.SuggestDirect(seen.FailCode)), "목록을 못 받으면 빈 목록 + 코드·직접 연결 길");

            // 비공개: 코드는 있고 목록에는 없다
            var b2 = new FakeBoard();
            var hidden = new InternetRoom(new FakeRelay { Code = "PRIV01" }, () => null, () => Room(RoomPhase.Lobby, 1), new ListingOptions { Public = false }, b2);
            hidden.Open();
            hidden.Tick(0);
            hidden.Tick(20000);
            Assert.AreEqual((InternetRoom.Phase.Ready, "PRIV01"), (hidden.State, hidden.JoinCode));
            Assert.IsNull(hidden.Listing);
            Assert.IsEmpty(b2.Calls, "Lobby를 부르지 않는다");
            var nullListing = new InternetRoom(new FakeRelay(), () => null, () => null, null, null);
            nullListing.Open();
            Assert.AreEqual(InternetRoom.Phase.Ready, nullListing.State, "목록 설정이 없으면 비공개");

            // 목록 실패는 방을 실패시키지 않는다
            var b3 = new FakeBoard { FailCreate = OnlineFail.ListFail };
            var listFail = new InternetRoom(new FakeRelay(), () => null, () => Room(RoomPhase.Lobby, 1), new ListingOptions(), b3);
            listFail.Open();
            Assert.AreEqual((InternetRoom.Phase.Ready, OnlineFail.ListFail, false), (listFail.State, listFail.ListFail, listFail.SuggestDirect), "코드 참가는 그대로");
        }

        [Test]
        public void M7_인터넷방_Relay실패는방을열지않고직접연결안내_코드틀림은안내없음_시작실패는그코드()
        {
            int started = 0;
            var board = new FakeBoard();
            var down = new InternetRoom(new FakeRelay { Fail = OnlineFail.RelayAuth }, () => { started++; return null; }, () => null, new ListingOptions(), board);
            down.Open();
            Assert.AreEqual((InternetRoom.Phase.Failed, OnlineFail.RelayAuth, true, 0), (down.State, down.FailCode, down.SuggestDirect, started),
                "Unity 서버를 쓸 수 없음 → [직접 연결](결정 92) · 방을 열지 않는다");
            Assert.IsNull(down.JoinCode);
            Assert.IsEmpty(board.Calls, "목록에도 올리지 않는다");

            var busy = new InternetRoom(new FakeRelay(), () => "NET_PORT_BUSY", () => null, null, null);
            busy.Open();
            Assert.AreEqual((InternetRoom.Phase.Failed, "NET_PORT_BUSY", false), (busy.State, busy.FailCode, busy.SuggestDirect));

            Assert.IsTrue(OnlineFail.SuggestDirect(OnlineFail.RelayAuth));
            Assert.IsTrue(OnlineFail.SuggestDirect(OnlineFail.ListFail));
            Assert.IsFalse(OnlineFail.SuggestDirect(OnlineFail.RelayCode), "코드가 틀린 것은 다시 확인");
            Assert.IsFalse(OnlineFail.SuggestDirect(null));
            Assert.IsTrue(OnlineFail.ReconnectFatal(OnlineFail.RelayCode), "재접속 중 코드 없음 → 바로 실패");
            Assert.IsFalse(OnlineFail.ReconnectFatal(OnlineFail.RelayAuth), "일시 오류 → 3초 뒤 다시");

            // 할당을 기다리는 동안 닫으면 늦게 온 코드로 열지 않는다
            Action<string> late = null;
            var slow = new InternetRoom(new LateRelay(ok => late = ok), () => { started++; return null; }, () => null, null, null);
            slow.Open();
            slow.Dispose();
            late("LATE01");
            Assert.AreEqual((InternetRoom.Phase.Closed, 0, (string)null), (slow.State, started, slow.JoinCode));

            Assert.AreEqual(5, InternetRoom.MaxConnections(6));
            Assert.AreEqual(1, InternetRoom.MaxConnections(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InternetRoom(new FakeRelay(), () => null, () => null, null, null, 7));
        }

        sealed class LateRelay : IRelayHost
        {
            readonly Action<Action<string>> keep;
            public LateRelay(Action<Action<string>> keep) => this.keep = keep;
            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail) => keep(ok);
        }

        sealed class HeldJoin : IRelayJoin<int>
        {
            public readonly List<(string code, Action<int> ok, Action<string> fail)> Calls = new List<(string, Action<int>, Action<string>)>();
            public void Join(string code, Action<int> ok, Action<string> fail) => Calls.Add((code, ok, fail));
        }

        [Test]
        public void M7_Relay시도_재접속도같은코드로새참가_늦게온결과와취소뒤결과는버린다()
        {
            var join = new HeldJoin();
            var tries = new RelayAttempts<int>(join);
            var got = new List<string>();
            tries.Begin("Q7K2MZ", d => got.Add("ok" + d), e => got.Add("fail " + e));
            tries.Begin("Q7K2MZ", d => got.Add("ok" + d), e => got.Add("fail " + e));
            Assert.AreEqual((2, true), (tries.Started, tries.Pending));
            Assert.IsTrue(join.Calls.All(c => c.code == "Q7K2MZ"), "같은 코드로 새 참가");
            join.Calls[0].ok(1);
            Assert.IsEmpty(got, "앞 시도의 늦은 결과는 버린다");
            join.Calls[1].fail(null);
            CollectionAssert.AreEqual(new[] { "fail " + OnlineFail.RelayAuth }, got, "이유 없는 실패는 RelayAuth");
            Assert.IsFalse(tries.Pending);
            tries.Begin("Q7K2MZ", d => got.Add("ok" + d), e => got.Add("fail " + e));
            tries.Cancel();
            join.Calls[2].ok(3);
            Assert.AreEqual(1, got.Count, "취소(나가기 · 3분 포기) 뒤 결과도 버린다");
            Assert.Throws<ArgumentException>(() => tries.Begin("", _ => { }, _ => { }));
        }

        /// <summary>
        /// 화면 ①(S06 · 02 §2-5 · §3-1 [가정 M7-43]): [새로 고침]은 지난 조회를 시작한 때부터 5초 안에는 받지 않는다(UGS Lobby 빈도 한도) · 실패 코드마다 화면 문구 키와 자세한 말(.tip)이 있고
        /// 모르는 코드는 「other」 · 값(「a/b」)이 문구의 자리 표시에 들어간다.
        /// </summary>
        [Test]
        public void M7_화면_새로고침은5초에한번_실패코드는모두문구와자세한말이있고값이자리에든다()
        {
            var gate = new RefreshGate();
            Assert.IsTrue(gate.TryStart(1000), "화면을 열 때 한 번");
            Assert.AreEqual(RefreshGate.GapMs, gate.LeftMs(1000));
            Assert.IsFalse(gate.TryStart(1000 + RefreshGate.GapMs - 1), "5초 안에는 받지 않는다");
            Assert.AreEqual(1, gate.LeftMs(1000 + RefreshGate.GapMs - 1));
            Assert.IsTrue(gate.TryStart(1000 + RefreshGate.GapMs), "5초 뒤에는 된다");
            Assert.IsFalse(gate.TryStart(1000 + RefreshGate.GapMs + 10), "막힌 시도는 시각을 다시 적지 않고 지난 시작부터 센다");

            var ko = TestData.LoadStrings("ko");
            var en = TestData.LoadStrings("en");
            Assert.AreEqual(18, NetFailText.Codes.Length, "결정 210: 웹 손님판 코드 셋(NET_TUNNEL · NET_NO_WEBGL · NET_NO_CLOUDFLARED)을 뺐다");
            foreach (var code in NetFailText.Codes.Append("E_SOMETHING_NEW").Append(null))
            {
                string key = NetFailText.Key(code);
                Assert.IsTrue(ko.Contains(key) && ko.Contains(key + ".tip") && en.Contains(key) && en.Contains(key + ".tip"), code + ": " + key);
            }
            Assert.AreEqual("screen.net.fail.other", NetFailText.Key("E_SOMETHING_NEW"));
            Assert.AreEqual("screen.net.fail.net_relay_auth", NetFailText.Key(OnlineFail.RelayAuth));
            Assert.AreEqual("Unity 서버를 쓸 수 없습니다.", ko[NetFailText.Key(OnlineFail.RelayAuth)], "Relay 실패 → 직접 연결 안내의 첫 줄(결정 92)");
            Assert.AreEqual("방장 1.0 · 내 게임 0.9입니다. GitHub 받기 페이지에서 둘 다 최신 버전으로 업데이트하십시오.",NetFailText.Fill(ko[NetFailText.Key(NetCode.Version) + ".tip"], NetCode.Version, "1.0/0.9"));
            Assert.AreEqual("6/6명이 들어와 있습니다.", NetFailText.Fill(ko[NetFailText.Key(NetCode.Full) + ".tip"], NetCode.Full, "6/6"));
            Assert.AreEqual("포트 7777번을 쓸 수 없습니다.", NetFailText.Fill(ko[NetFailText.Key("NET_PORT_BUSY")], "NET_PORT_BUSY", null, 7777));
        }

        /// <summary>
        /// plan M7 「화면 ②」 응답 시계(02 §5-3 끝 줄 · §5-5 · §5-7 끝 줄 [가정 M7-51]): 손님 화면은 받은 시계를 받은 뒤 지난 만큼 줄이되 연출(AnimLeftMs) · 내 차단 컷신(HoldLeftMs) 가운데 늦은 쪽까지는 멈추고,
        /// 일시정지 중에는 줄지 않으며, 뱅크를 쓰는 시계(MapTurn · BattleAction)는 기본 시간이 다 된 뒤 뱅크가 준다(Reaction은 뱅크를 쓰지 않는다). 남은 10초 = 기본 + 뱅크. 「0:23」은 초 올림.
        /// </summary>
        [Test]
        public void M7_화면_응답시계는받은뒤지난만큼줄고_연출차단동안멈추며일시정지에는줄지않고_기본시간뒤뱅크를쓴다()
        {
            var map = new SeatClock { Running = ClockKind.MapTurn, ChoiceId = 7, LeftMs = 20000, BankMs = 30000 };
            var react = new SeatClock { Running = ClockKind.Reaction, ChoiceId = 9, LeftMs = 12000, BankMs = 30000 };
            var idle = new SeatClock { BankMs = 30000 };
            var c = new ClockState { Preset = ClockPreset.Relaxed, AnimLeftMs = 1500, HoldLeftMs = 2500, Seats = new List<SeatClock> { map, idle, react } };

            Assert.AreEqual(0, ClockView.Elapsed(c, 2000), "차단 컷신(2.5초)이 끝나기 전에는 줄지 않는다(연출 1.5초보다 늦은 쪽)");
            Assert.AreEqual(500, ClockView.Elapsed(c, 3000));
            c.PausedBy.Add(1);
            Assert.AreEqual(0, ClockView.Elapsed(c, 60000), "일시정지 중에는 줄지 않는다");
            c.PausedBy.Clear();

            Assert.AreEqual(5000, ClockView.LeftMs(map, 15000));
            Assert.AreEqual(30000, ClockView.BankMs(map, 15000), "기본 시간이 남아 있으면 뱅크는 그대로");
            Assert.AreEqual(0, ClockView.LeftMs(map, 26000));
            Assert.AreEqual(24000, ClockView.BankMs(map, 26000), "기본 시간 뒤에는 뱅크가 준다(MapTurn)");
            Assert.AreEqual(24000, ClockView.TotalMs(map, 26000));
            Assert.IsFalse(ClockView.Low(map, 26000));
            Assert.IsTrue(ClockView.Low(map, 40000), "기본 + 뱅크가 10초 안이면 빨강");

            Assert.AreEqual(2000, ClockView.TotalMs(react, 10000), "Reaction은 뱅크를 쓰지 않는다");
            Assert.AreEqual(30000, ClockView.BankMs(react, 20000));
            Assert.IsTrue(ClockView.Low(react, 2000), "Reaction 12초 − 2초 = 10초 = 빨강");
            Assert.IsFalse(ClockView.Low(idle, 999999), "흐르는 시계가 없으면 빨강이 아니다");
            Assert.AreEqual(0, ClockView.TotalMs(idle, 0));

            CollectionAssert.AreEqual(new[] { 2 }, ClockView.Waiting(c, 0), "응답 대기 줄 = 나를 뺀 흐르는 시계의 좌석");
            CollectionAssert.AreEqual(new[] { 0, 2 }, ClockView.Waiting(c, -1));
            Assert.AreEqual(180000, ClockView.LengthMs(ClockSettings.For(ClockPreset.Relaxed), ClockKind.MapTurn));
            Assert.AreEqual(15000, ClockView.LengthMs(ClockSettings.For(ClockPreset.Normal), ClockKind.Reaction));
            Assert.AreEqual(0, ClockView.LengthMs(ClockSettings.For(ClockPreset.Off), ClockKind.MapTurn), "시계를 끈 방");
            Assert.AreEqual("0:23", ClockView.Text(22001));
            Assert.AreEqual("0:01", ClockView.Text(200));
            Assert.AreEqual("0:00", ClockView.Text(0));
            Assert.AreEqual("3:00", ClockView.Text(180000));
        }
    }
}
