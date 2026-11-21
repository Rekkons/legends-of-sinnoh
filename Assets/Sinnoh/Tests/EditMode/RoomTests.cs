using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「세션 ①」 · 02 §10: 방(HostSession) — 세션 루프(로비·준비·시작·내보내기·가득 참), 시계(방에서의 시간 초과·일시정지·시스템 정지),
    /// 끊김(그 시계만큼 → 자리 비움 → 0초, 끔 방 120초), 재접속(토큰 → 같은 자리 + 지금 보기, 시간 이어짐), 사람 조종자(배틀 밖 다음 좌석, 답이 없으면 AI).
    /// 링크는 연결마다 받은 메시지를 모으는 가짜(<see cref="Net"/>) — 소켓·직렬화 없음(그것은 Net 묶음의 NetLoopTests).
    /// </summary>
    public partial class RoomTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Build = "test-build";
        const string Content = "test-content";

        /// <summary>연결마다 받은 메시지. 스냅샷·델타(세션 ②)는 연결마다의 손님 쪽 <see cref="StreamReceiver"/>로 되읽어 GameUpdate로 모은다 — 어긋나면 실패.</summary>
        sealed class Net : IRoomLink
        {
            public readonly Dictionary<int, List<ServerMsg>> Got = new Dictionary<int, List<ServerMsg>>();
            public readonly Dictionary<int, string> Closed = new Dictionary<int, string>();
            public readonly Dictionary<int, StreamReceiver> Streams = new Dictionary<int, StreamReceiver>();

            public void Send(int conn, ServerMsg msg)
            {
                if (!Got.TryGetValue(conn, out var list)) Got[conn] = list = new List<ServerMsg>();
                if (msg is GameSnapshot || msg is GameDelta)
                {
                    if (!Streams.TryGetValue(conn, out var rx)) Streams[conn] = rx = new StreamReceiver();
                    var u = rx.Receive(msg);
                    Assert.IsNotNull(u, $"연결 {conn}: 스트림이 어긋났다({rx.LastProblem})");
                    msg = u;
                }
                list.Add(msg);
            }

            public void Close(int conn, string code) => Closed[conn] = code;

            public List<T> All<T>(int conn) where T : ServerMsg
            {
                var r = new List<T>();
                if (Got.TryGetValue(conn, out var list))
                    foreach (var m in list)
                        if (m is T t) r.Add(t);
                return r;
            }

            public T Last<T>(int conn) where T : ServerMsg
            {
                var all = All<T>(conn);
                return all.Count == 0 ? null : all[all.Count - 1];
            }

            public int Count(int conn) => Got.TryGetValue(conn, out var l) ? l.Count : 0;
        }

        static Hello Hi(string token, Platform platform = Platform.Pc) => new Hello { BuildId = Build, ContentHash = Content, Token = token, Nickname = token, Platform = platform };

        HostConfig Cfg(RoomSettings settings = null) => new HostConfig
        {
            OwnerToken = "owner", OwnerNickname = "owner", RoomId = "room-m7", BuildId = Build, ContentHash = Content, Seed = 20261118, AiSeed = 77,
            VirtualAgent = new PrincipleAgent(), Settings = settings,
        };

        /// <summary>방 하나 + 좌석마다의 연결(처음엔 conn = 자리 번호). 봇은 좌석의 마지막 보기만 보고 답한다.</summary>
        sealed class Room
        {
            public readonly HostSession Host;
            public readonly Net Net = new Net();
            public readonly int[] Conn = { 0, 1, 2, 3, 4, 5 };
            public readonly bool[] Gone = new bool[6];
            public long Now;
            /// <summary>러너(작업 스레드) — 있으면 진입점마다 셈이 끝나기를 기다려 넣는다(<see cref="Settle"/>).</summary>
            public AgentWorker Worker;
            readonly Ctx ctx;
            readonly HashSet<int> answered = new HashSet<int>();
            int nextConn = 100;
            int seenEpoch;

            public Room(Ctx ctx, HostConfig cfg, int seats)
            {
                this.ctx = ctx;
                Host = new HostSession(ctx, Net, cfg, 0);
                Assert.IsTrue(Host.Approve(0, Hi("owner"), Now).Ok);
                for (int i = 1; i < seats; i++) Assert.IsTrue(Host.Approve(i, Hi("g" + i), Now).Ok);
                for (int i = 1; i < seats; i++) Host.OnMessage(i, new SetReady { Ready = true }, Now);
                Host.OnMessage(0, new StartGame(), Now);
                Assert.AreEqual(RoomPhase.InGame, Host.Phase);
            }

            /// <summary>로비를 건너뛰고 이미 연 방(이어하기 대기 · 손으로 짠 로비)을 감싼다 — 연결 번호는 그대로 자리 번호에서 시작한다.</summary>
            public Room(Ctx ctx, Func<IRoomLink, HostSession> open, long now = 0)
            {
                this.ctx = ctx;
                Now = now;
                Host = open(Net);
            }

            public GameState View(int seat) => Net.Last<GameUpdate>(Conn[seat])?.View;

            /// <summary>그 좌석의 마지막 보기에서 그 좌석이 답할 열린 선택(아직 낸 적 없는 것). 가상 응답자 선택은 뺀다.</summary>
            public PendingChoice Mine(int seat)
            {
                int epoch = Host.Game?.Epoch ?? 0;
                if (epoch != seenEpoch)
                {
                    answered.Clear(); // 되돌리면 선택 Id가 다시 쓰인다(M10 무르기)
                    seenEpoch = epoch;
                }
                var v = View(seat);
                return v?.Pending.Find(c => c.Seat == seat && !c.Answered && !answered.Contains(c.Id));
            }

            /// <summary>호스트가 지금 연 가상 응답자 선택(관전자 보기).</summary>
            public PendingChoice Virtual() => Host.Game.ViewFor(SeatId.Observer).Pending.Find(c => c.Seat < 0 && c.Seat != SeatId.Wild && !c.Answered);

            public bool IsOpen(int id) => Host.Game.HostCopy().Pending.Exists(c => c.Id == id && !c.Answered);

            public void Answer(int seat, PendingChoice c, ChoiceAnswer a)
            {
                answered.Add(c.Id);
                Host.OnMessage(Conn[seat], new Submit { Command = Command.AnswerTo(c.Id, a), Epoch = EpochOf(seat) }, Now);
                Settle();
            }

            /// <summary>그 좌석이 마지막 갱신에서 받은 되돌림 횟수(SeatClient.Epoch와 같은 꼴 — 끈 방은 0).</summary>
            public int EpochOf(int seat) => Net.Last<GameUpdate>(Conn[seat])?.Undo?.Epoch ?? 0;

            /// <summary>러너가 있으면 AI 답이 다 들어갈 때까지 같은 시각으로 기다린다(시각을 손으로 옮기는 드라이버 — 바깥 시각이 흐르지 않으니 AI 생각 시간 0).</summary>
            public void Settle()
            {
                if (Worker == null) return;
                for (int i = 0; Host.AwaitingAi; i++)
                {
                    Assert.Less(i, 1000, "러너의 답이 들어가지 않는다");
                    Assert.IsTrue(Worker.WaitIdle(), "러너가 셈을 끝내지 않는다");
                    Host.Tick(Now);
                }
            }

            /// <summary>멈춤 끝까지 시각을 옮기고, 접속한 좌석 하나의 열린 선택에 답한다(pick이 null이거나 합법이 아니면 Default). 답할 것이 없으면 false.</summary>
            public bool Step(Func<GameState, PendingChoice, ChoiceAnswer> pick = null)
            {
                Now = Math.Max(Now, Host.AnimEndMs);
                Host.Tick(Now);
                Settle();
                for (int seat = 0; seat < Host.SeatCount; seat++)
                {
                    if (Gone[seat]) continue;
                    var c = Mine(seat);
                    if (c == null) continue;
                    var a = pick?.Invoke(View(seat), c);
                    if (a != null)
                    {
                        var legal = new List<ChoiceAnswer>();
                        Engine.Legal(View(seat), ctx, c, legal);
                        if (!ChoiceRules.Contains(legal, a)) a = null;
                    }
                    Answer(seat, c, a ?? c.Default.Clone());
                    return true;
                }
                return false;
            }

            /// <summary>stop이 참이 될 때까지 답하고, 답할 것이 없으면 다음 정해진 일로 시각을 옮긴다.</summary>
            public void Until(Func<bool> stop, Func<GameState, PendingChoice, ChoiceAnswer> pick = null, int max = 20000)
            {
                for (int i = 0; i < max; i++)
                {
                    if (stop()) return;
                    if (Step(pick)) continue;
                    long t = Host.NextEventMs;
                    Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다(답할 것도 정해진 일도 없다)");
                    Now = Math.Max(Now, t);
                    Host.Tick(Now);
                    Settle();
                }
                Assert.Fail("조건에 닿지 않았다");
            }

            public void Drop(int seat)
            {
                Gone[seat] = true;
                Host.OnDisconnected(Conn[seat], Now);
            }

            /// <summary>같은 토큰으로 새 연결에서 돌아온다. 돌려주는 값 = 새 연결.</summary>
            public int Back(int seat)
            {
                int conn = nextConn++;
                var token = seat == 0 ? "owner" : "g" + seat;
                var ok = Host.Approve(conn, Hi(token), Now);
                Assert.IsTrue(ok.Ok, ok.ToString());
                Assert.AreEqual(seat, ok.Seat);
                Conn[seat] = conn;
                Gone[seat] = false;
                return conn;
            }

            public List<NoticeMsg> Notices(int seat, string code) => Net.All<NoticeMsg>(Conn[seat]).FindAll(n => n.Code == code);
        }

        static ChoiceAnswer ChallengeGalactic(GameState view, PendingChoice c) =>
            c.Kind == ChoiceKind.MapAction ? ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic) : null;

        // ── 세션 루프 ──

        [Test]
        public void M7_세션루프_로비준비시작내보내기와가득참()
        {
            var net = new Net();
            var host = new HostSession(ctx, net, Cfg(), 0);
            Assert.AreEqual(0, host.Approve(0, Hi("owner"), 0).Seat, "방장은 1번 자리");
            for (int i = 1; i <= 5; i++) Assert.AreEqual(i, host.Approve(i, Hi("g" + i, i == 2 ? Platform.Web : Platform.Pc), 0).Seat, "들어온 순서대로 빈 자리에");
            var full = host.Approve(6, Hi("g6"), 0);
            Assert.AreEqual(NetCode.Full, full.Code, "7번째는 가득 참");
            Assert.AreEqual("6/6", full.Detail);
            Assert.AreEqual(Platform.Web, host.Room.Seats[2].Platform);
            Assert.IsTrue(host.Room.Seats[0].Host);

            // 승인 순서(02 §4-6): 버전 → 데이터 → 금지 → 같은 토큰
            Assert.AreEqual(NetCode.Version, host.Approve(7, new Hello { ProtocolVersion = 99, BuildId = Build, ContentHash = Content, Token = "x" }, 0).Code);
            Assert.AreEqual(NetCode.Version, host.Approve(7, new Hello { BuildId = "other", ContentHash = Content, Token = "x" }, 0).Code);
            Assert.AreEqual(NetCode.Content, host.Approve(7, new Hello { BuildId = Build, ContentHash = "other", Token = "x" }, 0).Code);
            Assert.AreEqual(NetCode.Duplicate, host.Approve(7, Hi("g1"), 0).Code, "접속 중인 토큰");
            Assert.AreEqual(NetCode.BadHello, host.Approve(7, new Hello { BuildId = Build, ContentHash = Content }, 0).Code);

            // 내보내기: 방장만, 그 토큰은 다시 못 들어온다
            host.OnMessage(1, new KickFromLobby { Seat = 2 }, 0);
            Assert.IsNotNull(host.Room.Seats[2], "손님은 내보내지 못한다");
            host.OnMessage(0, new KickFromLobby { Seat = 5 }, 0);
            Assert.AreEqual(NetCode.Kicked, net.Closed[5]);
            Assert.IsNull(host.Room.Seats[5]);
            Assert.AreEqual(NetCode.Kicked, host.Approve(8, Hi("g5"), 0).Code);

            // 로비에서 끊기면 자리가 빈다 · 새 손님은 첫 빈 자리에
            host.OnDisconnected(4, 0);
            Assert.IsNull(host.Room.Seats[4]);
            Assert.AreEqual(4, host.Approve(9, Hi("g9"), 0).Seat);
            host.OnDisconnected(9, 0);

            // 방 설정: 손님은 못 바꾸고, 방장은 앉은 자리 밑으로 자리 수를 못 줄인다
            host.OnMessage(1, new ChangeSettings { Settings = new RoomSettings { Seats = 4 } }, 0);
            Assert.AreEqual(6, host.Settings.Seats);
            host.OnMessage(0, new ChangeSettings { Settings = new RoomSettings { Seats = 3, Clock = ClockPreset.Normal } }, 0);
            Assert.AreEqual(6, host.Settings.Seats, "4번 자리에 사람이 있다");
            host.OnMessage(0, new ChangeSettings { Settings = new RoomSettings { Seats = 4, Clock = ClockPreset.Normal } }, 0);
            Assert.AreEqual(4, host.Settings.Seats);
            Assert.AreEqual(4, host.Room.Seats.Count);
            Assert.AreEqual(ClockPreset.Normal, net.Last<RoomUpdate>(1).Room.Settings.Clock, "손님에게 방 설정이 간다");

            // 시작: 손님이 모두 준비해야 켜진다
            host.OnMessage(0, new StartGame(), 0);
            Assert.AreEqual(RoomPhase.Lobby, host.Phase);
            Assert.IsFalse(host.Room.CanStart);
            for (int i = 1; i <= 3; i++) host.OnMessage(i, new SetReady { Ready = true }, 0);
            Assert.IsTrue(host.Room.CanStart);
            host.OnMessage(0, new StartGame(), 0);
            Assert.AreEqual(RoomPhase.InGame, host.Phase);
            Assert.AreEqual(4, host.SeatCount, "빈 자리는 시작 때 빠진다");
            Assert.AreEqual(4, host.Game.SeatCount);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(i, net.Last<Welcome>(i).YourSeat);
                var u = net.Last<GameUpdate>(i);
                Assert.IsNotNull(u, $"좌석 {i}가 첫 보기를 받았다");
                Assert.AreEqual(i, u.View.Viewer, "자기 보기만 받는다(02 §1)");
                Assert.AreEqual(4, u.View.SeatCount);
                Assert.IsNotNull(u.Clock, "시계가 붙는다");
                Assert.AreEqual(ClockPreset.Normal, u.Clock.Preset);
            }
            Assert.AreEqual(NetCode.InGame, host.Approve(10, Hi("new"), 0).Code, "게임 중에는 그 판의 토큰만");
            Assert.AreEqual(NetCode.Duplicate, host.Approve(11, Hi("g1"), 0).Code);
            host.OnMessage(1, new ChangeSettings { Settings = new RoomSettings() }, 0);
            host.OnMessage(0, new ChangeSettings { Settings = new RoomSettings { Seats = 2, Campaign = Campaign.UltraBurst, Clock = ClockPreset.Relaxed } }, 0);
            Assert.AreEqual(ClockPreset.Relaxed, host.Settings.Clock, "게임 중에는 제한 시간을 바꿀 수 있고");
            Assert.AreEqual(4, host.Settings.Seats, "자리 수·캠페인은 못 바꾼다");
            Assert.AreEqual(Campaign.HeartOfSinnoh, host.Settings.Campaign);
        }

        // ── 시계 ──

        [Test]
        public void M7_시계_방에서시간초과는기본값_일시정지와시스템정지는시계를멈춤()
        {
            var r = new Room(ctx, Cfg(), 2);
            int x = -1;
            r.Until(() => (x = Seat(r)) >= 0);
            var c = r.Mine(x);
            Assert.AreEqual(ChoiceKind.VillagePick, c.Kind);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            Assert.AreEqual(ClockKind.Setup, r.Host.Clock.Seats[x].Running);
            Assert.AreEqual(r.Now + 90000, r.Host.NextEventMs, "고향 마을 90초(02 §5-4)");
            r.Host.Tick(r.Now + 89999);
            Assert.IsTrue(r.IsOpen(c.Id));
            r.Now += 90000;
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(c.Id), "시간 초과 → Default");
            Assert.AreEqual(1, r.Notices(0, NetCode.AutoResolved).Count);
            Assert.AreEqual(1, r.Notices(1, NetCode.AutoResolved).Count, "전원에게 알림");

            // 다음 고향 마을: 방장이 일시정지(방 설정 기본 「방장만」 — 결정 112 Q6) → 시계·연출이 멈추고 답은 E_PAUSED
            int y = -1;
            r.Until(() => (y = Seat(r)) >= 0);
            var d = r.Mine(y);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            long due = r.Host.NextEventMs;
            r.Now += 10000;
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.IsTrue(r.Host.Paused);
            CollectionAssert.AreEqual(new[] { 0 }, r.Host.Clock.PausedBy);
            Assert.AreEqual(long.MaxValue, r.Host.NextEventMs);
            r.Now += 1000000;
            r.Host.Tick(r.Now);
            Assert.IsTrue(r.IsOpen(d.Id), "일시정지 중에는 시간 초과가 없다");
            r.Answer(y, d, d.Default.Clone());
            Assert.AreEqual(RejectReason.Paused, r.Net.Last<RejectedMsg>(r.Conn[y]).Reason);
            Assert.IsTrue(r.IsOpen(d.Id));
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now); // 건 사람(방장)이 푼다
            Assert.IsFalse(r.Host.Paused);
            Assert.AreEqual(due + 1000000, r.Host.NextEventMs, "멈춘 만큼 뒤로 — 남은 80초 그대로");

            // 시스템 정지: 손님이 모두 끊기면 저절로 걸리고, 돌아오면 풀린다
            r.Drop(1);
            CollectionAssert.AreEqual(new[] { HostSession.SystemPause }, r.Host.Clock.PausedBy);
            r.Now += 500000;
            r.Host.Tick(r.Now);
            Assert.IsTrue(r.IsOpen(d.Id));
            int conn = r.Back(1);
            Assert.IsFalse(r.Host.Paused);
            Assert.AreEqual(due + 1500000, r.Host.NextEventMs);
            Assert.AreEqual(1, r.Net.All<Welcome>(conn).Count);
            var snap = r.Net.Last<GameUpdate>(conn);
            Assert.AreEqual(1, snap.View.Viewer);
            Assert.AreEqual(0, snap.Events.Count, "재접속은 지금 보기만(이벤트 없이)");
        }

        /// <summary>열린 선택이 있는 좌석(없으면 −1).</summary>
        static int Seat(Room r)
        {
            for (int s = 0; s < r.Host.SeatCount; s++)
                if (r.Mine(s) != null) return s;
            return -1;
        }

        // ── 끊김·재접속 ──

        /// <summary>손님 좌석의 첫 맵 행동까지(셋업은 Default).</summary>
        static int ToGuestMapAction(Room r)
        {
            int g = -1;
            r.Until(() =>
            {
                for (int s = 1; s < r.Host.SeatCount; s++)
                    if (r.Mine(s)?.Kind == ChoiceKind.MapAction) { g = s; return true; }
                return false;
            });
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            return g;
        }

        [Test]
        public void M7_끊김_그시계만큼만기다리고자리비움_새선택은0초_돌아오면풀림()
        {
            var r = new Room(ctx, Cfg(), 3);
            int g = ToGuestMapAction(r);
            var map = r.Mine(g);
            var clock = r.Host.Clock.Seats[g];
            Assert.AreEqual(ClockKind.MapTurn, clock.Running);
            Assert.AreEqual(180000, clock.LeftMs);
            Assert.AreEqual(r.Now + 180000 + clock.BankMs, r.Host.NextEventMs, "접속한 좌석은 턴 + 뱅크");
            r.Now += 50000;
            r.Drop(g);
            Assert.AreEqual(r.Now + 130000, r.Host.NextEventMs, "끊긴 좌석은 남은 기본 시간만(뱅크 안 씀)");
            Assert.IsFalse(r.Host.Room.Seats[g].Connected);
            Assert.AreEqual(1, r.Notices(0, NetCode.PlayerLeft).Count);
            Assert.IsFalse(r.Host.Paused, "다른 손님이 남아 있다");
            int before = r.Host.Game.CommandCount;
            r.Now += 130000;
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(map.Id), "턴 종료로 넘어갔다");
            Assert.IsTrue(r.Host.Room.Seats[g].Away, "한 번 시간 초과 뒤 자리 비움");
            Assert.AreEqual(1, r.Notices(0, NetCode.Away).Count);
            Assert.Greater(r.Host.Game.CommandCount, before + 1, "Timeout + HostOp 줄");

            // 자리 비움 좌석의 새 선택은 멈춤 끝에 바로 기본값
            PendingChoice next = null;
            r.Until(() => (next = r.Host.Game.ViewFor(g).Pending.Find(c => c.Seat == g && !c.Answered && !c.HandGated)) != null);
            Assert.LessOrEqual(r.Host.NextEventMs, Math.Max(r.Now, r.Host.AnimEndMs), "0초");
            r.Now = Math.Max(r.Now, r.Host.AnimEndMs);
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(next.Id));

            // 돌아오면 자리 비움이 풀리고 지금 보기를 받는다
            int conn = r.Back(g);
            Assert.IsFalse(r.Host.Room.Seats[g].Away);
            Assert.AreEqual(1, r.Notices(0, NetCode.AwayCleared).Count);
            Assert.AreEqual(1, r.Notices(0, NetCode.PlayerBack).Count);
            Assert.AreEqual(g, r.Net.Last<GameUpdate>(conn).View.Viewer);
            Assert.IsFalse(r.Net.Last<GameUpdate>(conn).Clock.Seats[g].Away);
        }

        [Test]
        public void M7_끊김_시계끔방은120초()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Clock = ClockPreset.Off }), 3);
            int x = -1;
            r.Until(() => (x = Seat(r)) > 0);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            Assert.AreEqual(long.MaxValue, r.Host.NextEventMs, "끔 방: 접속한 사람에게는 시간 초과가 없다");
            r.Drop(x);
            Assert.AreEqual(r.Now + 120000, r.Host.NextEventMs);
            r.Now += 120000;
            r.Host.Tick(r.Now);
            Assert.IsTrue(r.Host.Room.Seats[x].Away);
        }

        [Test]
        public void M7_재접속_같은자리와지금보기_남은시간이어짐()
        {
            var r = new Room(ctx, Cfg(), 3);
            int g = ToGuestMapAction(r);
            long due = r.Host.NextEventMs;
            long bank = r.Host.Clock.Seats[g].BankMs;
            r.Now += 40000;
            r.Host.Tick(r.Now);
            r.Drop(g);
            Assert.AreEqual(due - bank, r.Host.NextEventMs);
            r.Now += 20000;
            var stranger = r.Host.Approve(500, Hi("stranger"), r.Now);
            Assert.AreEqual(NetCode.SeatClaim, stranger.Code, "끊긴 좌석이 있으면 모르는 토큰은 자리 확인 대기(세션 ③)");
            r.Host.OnDisconnected(500, r.Now);
            Assert.AreEqual(0, r.Host.Room.Claims.Count, "끊긴 대기 연결은 목록에서 빠진다");
            int conn = r.Back(g);
            Assert.AreEqual(due, r.Host.NextEventMs, "끊긴 동안도 시간은 흘렀고, 돌아오면 뱅크가 다시 붙는다");
            Assert.AreEqual(NetCode.Duplicate, r.Host.Approve(501, Hi("g" + g), r.Now).Code, "접속 중인 같은 토큰");
            var w = r.Net.Last<Welcome>(conn);
            Assert.AreEqual(g, w.YourSeat);
            Assert.IsFalse(w.IsHost);
            var snap = r.Net.Last<GameUpdate>(conn);
            Assert.AreEqual(g, snap.View.Viewer);
            Assert.AreEqual(StateHash.Of(r.Host.Game.ViewFor(g)), StateHash.Of(snap.View), "지금 보기 = ViewFor(그 좌석)");
            Assert.IsNotNull(r.Mine(g), "열린 선택이 그대로 보인다");
            r.Host.OnMessage(conn, new RequestResync(), r.Now);
            Assert.AreEqual(0, r.Net.Last<GameUpdate>(conn).Events.Count);
        }

        // ── 사람 조종자 ──

        [Test]
        public void M7_조종자_배틀밖다음좌석이답하고_답이없거나끊기면AI()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Galactic = Control.Human }), 3);
            int human = 0, timedAi = 0, lostAi = 0;
            for (int i = 0; i < 20000 && lostAi == 0; i++)
            {
                var v = r.Virtual();
                int ctl = v != null ? r.Host.ControllerOf(v.Seat) : -1;
                if (v != null && ctl >= 0)
                {
                    var host = r.Host.Game.HostCopy();
                    Assert.IsNotNull(host.Battle, "갤럭시단 배틀 중");
                    int challenger = host.Battle.Sides[0].Owner >= 0 ? host.Battle.Sides[0].Owner : host.Battle.Sides[1].Owner;
                    Assert.AreEqual((challenger + 1) % 3, ctl, "배틀에 없는 다음 좌석(시계 방향, 결정 29 ④)");
                    r.Now = Math.Max(r.Now, r.Host.AnimEndMs);
                    r.Host.Tick(r.Now);
                    Assert.IsTrue(r.IsOpen(v.Id), "사람 조종이면 멈춤 끝에 AI가 답하지 않는다");
                    if (human == 0)
                    {
                        r.Answer(challenger, v, v.Default.Clone());
                        Assert.AreEqual(RejectReason.NotYourChoice, r.Net.Last<RejectedMsg>(r.Conn[challenger]).Reason, "조종자만 답한다");
                        Assert.IsTrue(r.IsOpen(v.Id));
                        r.Answer(ctl, v, v.Default.Clone());
                        Assert.IsFalse(r.IsOpen(v.Id), "조종자의 답을 받았다");
                        human++;
                    }
                    else if (timedAi == 0)
                    {
                        for (int k = 0; k < 20 && r.IsOpen(v.Id); k++)
                        {
                            Assert.Greater(r.Host.NextEventMs, r.Now, "기다린다");
                            r.Now = r.Host.NextEventMs;
                            r.Host.Tick(r.Now);
                        }
                        Assert.IsFalse(r.IsOpen(v.Id));
                        Assert.IsTrue(AnsweredByAi(r, v.Id, challenger), "답이 없으면 AI");
                        timedAi++;
                    }
                    else
                    {
                        r.Drop(ctl);
                        Assert.IsFalse(r.IsOpen(v.Id), "조종자가 끊기면 시각을 옮기지 않고 바로 AI(멈춤은 이미 끝났다)");
                        Assert.IsTrue(AnsweredByAi(r, v.Id, challenger));
                        Assert.AreEqual(-1, r.Host.ControllerOf(v.Seat), "끊긴 조종자 → AI");
                        r.Back(ctl);
                        lostAi++;
                    }
                    continue;
                }
                if (r.Step(ChallengeGalactic)) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
            }
            Assert.AreEqual(1, human);
            Assert.AreEqual(1, timedAi);
            Assert.AreEqual(1, lostAi);

            // AI 조종 방: 같은 판에서 갤럭시단 선택은 멈춤 끝에 AI가 답한다(조종자 없음)
            var a = new Room(ctx, Cfg(), 3);
            PendingChoice g = null;
            a.Until(() => (g = a.Virtual()) != null, ChallengeGalactic);
            Assert.AreEqual(-1, a.Host.ControllerOf(SeatId.Galactic));
            a.Now = Math.Max(a.Now, a.Host.AnimEndMs);
            a.Host.Tick(a.Now);
            Assert.IsFalse(a.IsOpen(g.Id));
        }

        [Test]
        public void M9_결정121_울트라버스트방은사람조종으로저장돼도UB와야생을AI가조종한다()
        {
            // 결정 121(T13): 울트라 버스트 모드는 방 설정 「사람 조종」을 쓸 수 없다 — 사람 조종(갤럭시단 · UB 칸 모두)으로 저장된 판이어도 조종자가 없다(가상 쪽 물음은 AI가 답한다)
            var r = new Room(ctx, Cfg(new RoomSettings { Campaign = Campaign.UltraBurst, Seats = 3, Galactic = Control.Human, UltraBeast = Control.Human }), 3);
            r.Until(() => r.Host.Game.ViewFor(0).Phase != Phase.Setup);
            foreach (int v in new[] { SeatId.UltraBeast, SeatId.Wild, SeatId.Galactic })
                Assert.AreEqual(-1, r.Host.ControllerOf(v), $"가상 좌석 {v}: 사람 조종자 없음");
            // 다른 모드는 그대로: 신오의 심장 방의 사람 조종 갤럭시단은 조종자가 있다
            var hos = new Room(ctx, Cfg(new RoomSettings { Seats = 3, Galactic = Control.Human }), 3);
            hos.Until(() => hos.Host.Game.ViewFor(0).Phase != Phase.Setup);
            Assert.GreaterOrEqual(hos.Host.ControllerOf(SeatId.Galactic), 0, "신오의 심장은 사람 조종 그대로");
        }

        /// <summary>6인 봇 판(사람 조종 갤럭시단): 좌석 보기의 Legal에서 시드로 고르고, 손님이 끊겼다 돌아오고, 가끔 일시정지한다. 돌려주는 값 = (끝 해시, 명령 수, 시간 초과 알림 수).</summary>
        (ulong Hash, int Commands, int Auto) Bots(ulong salt, int steps) => Outcome(BotsRoom(salt, steps, null, null));

        static (ulong Hash, int Commands, int Auto) Outcome(Room r) => (r.Host.Game.Hash, r.Host.Game.CommandCount, r.Notices(0, NetCode.AutoResolved).Count);

        /// <summary><see cref="Bots"/>의 방 — 러너(작업 스레드)·판 저장을 줄 수 있다(「호스트 러너」 시험).</summary>
        Room BotsRoom(ulong salt, int steps, AgentWorker worker, Func<string, ISaveStore> store)
        {
            var cfg = Cfg(new RoomSettings { Galactic = Control.Human, Clock = ClockPreset.Normal });
            cfg.Runner = worker;
            cfg.Store = store;
            var r = new Room(ctx, cfg, 6) { Worker = worker };
            var rng = new Pcg32(salt);
            Func<GameState, PendingChoice, ChoiceAnswer> pick = (view, c) =>
            {
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(view, ctx, c, legal);
                var galactic = legal.Find(a => a.Kind == AnswerKind.Challenge && a.Target == SeatId.Galactic);
                if (galactic != null && c.Id % 2 == 0) return galactic;
                if (c.Kind == ChoiceKind.HiddenSpot) return ChoiceAnswer.Of(AnswerKind.Decline);
                return legal[new Pcg32(salt, (ulong)c.Id).Next(legal.Count)];
            };
            for (int i = 0; i < steps && r.Host.Phase == RoomPhase.InGame; i++)
            {
                if (i % 53 == 20)
                {
                    int s = 1 + rng.Next(5);
                    int on = 0;
                    for (int k = 1; k < 6; k++)
                        if (!r.Gone[k]) on++;
                    if (r.Gone[s]) r.Back(s);
                    else if (on > 1) r.Drop(s); // 손님이 모두 끊기면 시스템 정지(따로 시험) — 여기서는 하나는 남긴다
                    r.Settle();
                }
                if (i % 211 == 100)
                {
                    r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
                    r.Now += 5000;
                    r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);
                    r.Settle();
                }
                var v = r.Virtual();
                int ctl = v != null ? r.Host.ControllerOf(v.Seat) : -1;
                if (v != null && ctl >= 0 && !r.Gone[ctl] && v.Id % 3 != 0)
                {
                    r.Now = Math.Max(r.Now, r.Host.AnimEndMs);
                    r.Host.Tick(r.Now);
                    r.Settle();
                    if (r.IsOpen(v.Id) && r.Host.ControllerOf(v.Seat) == ctl) r.Answer(ctl, v, v.Default.Clone());
                    continue;
                }
                if (r.Step(pick)) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, $"방이 멈췄다({i}번째) paused={r.Host.Paused} gone={string.Join(",", r.Gone)} open={string.Join(" ", r.Host.Game.HostCopy().Pending.FindAll(c => !c.Answered).ConvertAll(c => c.Id + ":" + c.Kind + "/" + c.Sub + "@" + c.Seat + (c.HandGated ? "H" : "")))} ctl={(r.Virtual() != null ? r.Host.ControllerOf(r.Virtual().Seat) : -9)}");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
                r.Settle();
            }
            for (int s = 0; s < 6; s++)
            {
                if (r.Gone[s]) continue;
                var view = r.View(s);
                Assert.AreEqual(StateHash.Of(r.Host.Game.ViewFor(s)), StateHash.Of(view), $"좌석 {s}의 마지막 보기 = 지금 ViewFor");
            }
            return r;
        }

        [Test]
        public void M7_세션루프_6인봇판_끊김재접속일시정지를섞어도멈추지않고같은판이된다()
        {
            var a = Bots(3, 2500);
            var b = Bots(3, 2500);
            TestContext.WriteLine($"[M7 6인 봇 방] 명령 {a.Commands}, 시간 초과 처리 {a.Auto}");
            Assert.Greater(a.Commands, 600, "판이 굴러갔다");
            Assert.Greater(a.Auto, 0, "끊긴 좌석의 시간 초과가 끼었다");
            Assert.AreEqual(a, b, "같은 입력 열 = 같은 판(시계·AI 답이 정해진 시각에 적용된다)");
        }

        static bool AnsweredByAi(Room r, int id, int observer)
        {
            foreach (var u in r.Net.All<GameUpdate>(r.Conn[observer]))
                foreach (var e in u.Events)
                    if (e is ChoiceAnswered x && x.Id == id) return x.ByAi;
            return false;
        }
    }
}
