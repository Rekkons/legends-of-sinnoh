using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「Net ① 직접 연결 · NGO 루프」(02 §1 · §2-1 · §4-6 · §6-1 · §6-3 · §10 「NGO 실제 소켓」·「직접 연결 한 판」, isekai NetLoopTests 꼴):
    /// 한 프로세스에 NetworkManager 여럿을 만들어 127.0.0.1의 실제 TCP WebSocket으로 붙인다 — Relay·UGS 호출은 없다(직접 연결, 결정 92).
    /// 호스트 자리도 NGO 호스트 클라이언트로 손님과 같은 메시지를 받는다(NetHostSession.Local). 봇은 자기 연결로 받은 보기만 보고 답한다.
    /// 호스트 방의 시각은 가짜 시각(<see cref="VClock"/>)이라 연출 멈춤·정해진 일로 건너뛴다(AI 러너가 셈하는 동안은 멈춘다). 손님 시계(재접속 3초)는 실제 Stopwatch.
    /// 호스트는 시험에서만 127.0.0.1에 묶는다(방화벽 창을 띄우지 않으려고 — 실제 방은 0.0.0.0). 시험마다 포트를 따로 쓴다(47791~).
    /// </summary>
    public sealed partial class NetLoopTests
    {
        const string Loopback = "127.0.0.1";
        const string Build = "net-build", Content = "net-content";

        static Ctx ctx;
        readonly List<NetRunner> runners = new List<NetRunner>();
        readonly List<string> dirs = new List<string>();

        sealed class VClock
        {
            public long Now;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = runners.Count - 1; i >= 0; i--)
                if (runners[i] != null) runners[i].Shutdown();
            // NGO 종료(방 닫기 유예 1초 포함)가 끝난 뒤에 없앤다 — 도는 중에 없애면 Transport가 이미 버린 연결 표를 읽는다
            float end = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < end && runners.Exists(r => r != null && r.GetComponent<NetworkManager>() is var m && m != null && (m.IsListening || m.ShutdownInProgress)))
                yield return null;
            yield return null;
            for (int i = runners.Count - 1; i >= 0; i--)
                if (runners[i] != null) UnityEngine.Object.Destroy(runners[i].gameObject);
            runners.Clear();
            yield return null;
            yield return null;
            foreach (var d in dirs)
                if (Directory.Exists(d)) Directory.Delete(d, true);
            dirs.Clear();
        }

        // ── 시험 ──

        [UnityTest, NUnit.Framework.Timeout(180000)]
        public IEnumerator M7_NGO_6자리방_127001로로비준비시작_7번째는E_FULL_봇이셋업을지나맵턴까지()
        {
            const ushort port = 47791;
            var vc = new VClock();
            var host = OpenHost(port, Cfg("net-six", 6), vc);
            Assert.AreEqual(NetStatus.Connected, host.Local.Status, "호스트 자리는 Start 안에서 Welcome을 받는다");
            Assert.AreEqual(0, host.Seat);
            Assert.IsTrue(host.Local.IsHost);

            var guests = new List<NetClientSession>();
            for (int i = 1; i < 6; i++) guests.Add(Join(port, Hi("g" + i), "Guest" + i));
            yield return Until(() => guests.All(g => g.Status == NetStatus.Connected) && Members(host.Local.Room) == 6, 30, "손님 다섯 접속");
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, guests.Select(g => g.Seat).ToArray(), "들어온 순서대로 빈 자리");
            Assert.IsTrue(guests.All(g => !g.IsHost && g.RoomId == "net-six" && g.Room.Phase == RoomPhase.Lobby));

            var seventh = Join(port, Hi("g7"), "Guest7");
            yield return Until(() => seventh.Status == NetStatus.Failed, 15, "7번째 거절");
            Assert.AreEqual(NetCode.Full, seventh.FailCode);
            Assert.AreEqual("6/6", seventh.FailDetail);
            Assert.IsNull(seventh.FailPhase, "방에 들어가기 전");

            foreach (var g in guests) g.Send(new SetReady { Ready = true });
            yield return Until(() => host.Local.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            var bots = new List<Bot> { new Bot(host.Local, host.Send, null) };
            foreach (var g in guests) bots.Add(new Bot(g, g.Send, null));
            yield return Until(() => bots.All(b => b.Session.View != null), 10, "첫 스냅샷");
            Assert.AreEqual(RoomPhase.InGame, guests[0].Room.Phase);

            int start = host.Host.Game.CommandCount;
            yield return Drive(host, vc, bots, () => host.Local.View.Phase != Phase.Setup && host.Host.Game.CommandCount >= start + 60, 120, "셋업 뒤 맵 턴까지");
            yield return Settle(host, bots);
            Check(host, bots);
            Assert.IsTrue(bots.All(b => b.Sent > 0), "모든 좌석이 답했다(호스트 자리 포함)");
            Assert.IsEmpty(bots.SelectMany(b => b.Bad), "합법 밖 거절 없음");
            Assert.AreEqual(0, host.Link.Oversize, "경고 크기를 넘은 메시지 없음");
            Report($"[M7 NGO 6자리] 명령 {host.Host.Game.CommandCount} · 가장 큰 스냅샷 {host.Link.MaxSnapshotBytes} B · 델타 {host.Link.MaxDeltaBytes} B · 손님 1에게 {host.Link.BytesTo(1) / 1024} KiB");
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_NGO_승인에서판이다르면E_VERSION_데이터가다르면E_CONTENT로끊기고자리가안생긴다()
        {
            const ushort port = 47792;
            var host = OpenHost(port, Cfg("net-version", 3), new VClock());
            var old = Hi("old");
            old.ProtocolVersion = Protocol.Version + 1;
            var a = Join(port, old, "Old");
            var other = Hi("other");
            other.ContentHash = "other-content";
            var b = Join(port, other, "Other");
            yield return Until(() => a.Status == NetStatus.Failed && b.Status == NetStatus.Failed, 15, "거절");
            Assert.AreEqual(NetCode.Version, a.FailCode);
            Assert.AreEqual(Build + "/" + Build, a.FailDetail, "값은 방장 빌드/내 빌드");
            Assert.AreEqual(NetCode.Content, b.FailCode);
            Assert.AreEqual(-1, a.Seat);
            Assert.AreEqual(1, Members(host.Host.Room), "자리가 생기지 않는다");
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_NGO_로비에서방을닫으면손님은끊김보다닫힘이유가먼저_E_HOST_CLOSED_다시시도없음()
        {
            const ushort port = 47793;
            var host = OpenHost(port, Cfg("net-close", 4), new VClock());
            var g1 = Join(port, Hi("g1"), "Guest1");
            var g2 = Join(port, Hi("g2"), "Guest2");
            yield return Until(() => g1.Status == NetStatus.Connected && g2.Status == NetStatus.Connected && Members(host.Local.Room) == 3, 15, "손님 둘 접속");

            host.Send(new LeaveRoom());   // 방장의 [나가기] = 방 닫기
            Assert.AreEqual(NetStatus.Closed, host.Local.Status);
            Assert.AreEqual(RoomPhase.Closed, host.Host.Phase);
            Assert.IsTrue(host.ShuttingDown, "손님이 있으면 NGO를 바로 내리지 않는다(닫힘 이유가 먼저 닿게)");
            yield return Until(() => g1.Status == NetStatus.Failed && g2.Status == NetStatus.Failed, 15, "손님 끊김");
            foreach (var g in new[] { g1, g2 })
            {
                Assert.AreEqual(NetCode.HostClosed, g.FailCode, "끊김(NET_LOST)이 아니라 닫힘 이유");
                Assert.AreEqual(RoomPhase.Lobby, g.FailPhase);
                g.Retry();
                Assert.AreEqual(NetStatus.Failed, g.Status, "로비에서 닫힌 방은 [다시 시도]가 없다");
            }
            yield return Until(() => !Manager(host).IsListening, 10, "호스트 NGO 종료(유예 뒤)");
        }

        [UnityTest, NUnit.Framework.Timeout(90000)]
        public IEnumerator M7_NGO_게임중손님네트워크가끊기면다시시도해같은자리_스냅샷부터이어감_같은토큰새접속은E_DUPLICATE재시도()
        {
            const ushort port = 47794;
            var vc = new VClock();
            var host = OpenHost(port, Cfg("net-back", 3), vc);
            var g1 = Join(port, Hi("g1"), "Guest1");
            var g2 = Join(port, Hi("g2"), "Guest2");
            yield return Until(() => g1.Status == NetStatus.Connected && g2.Status == NetStatus.Connected && Members(host.Local.Room) == 3, 15, "손님 둘 접속");
            g1.Send(new SetReady { Ready = true });
            g2.Send(new SetReady { Ready = true });
            yield return Until(() => host.Local.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            var bots = new[] { new Bot(host.Local, host.Send, null), new Bot(g1, g1.Send, null), new Bot(g2, g2.Send, null) };
            yield return Until(() => bots.All(b => b.Session.View != null), 10, "첫 스냅샷");
            int start = host.Host.Game.CommandCount;
            yield return Drive(host, vc, bots, () => host.Host.Game.CommandCount >= start + 8, 60, "봇 8수");

            // ① 손님 쪽 네트워크가 끊긴 것처럼(사유 없음) → Reconnecting → 3초 뒤 같은 토큰으로 같은 자리
            int seat = g1.Seat;
            int snaps = g1.Stream.Snapshots;
            ManagerOf(g1).Shutdown();
            yield return Until(() => g1.Status == NetStatus.Reconnecting, 10, "재접속 대기");
            yield return Until(() => !host.Host.Room.Seats[seat].Connected, 15, "호스트가 끊김을 안다");
            yield return Until(() => g1.Status == NetStatus.Connected, 20, "3초 뒤 같은 토큰으로 재접속");
            Assert.AreEqual(seat, g1.Seat, "같은 자리");
            yield return Until(() => g1.Stream.Snapshots > snaps && host.Host.Room.Seats[seat].Connected, 10, "지금 보기 스냅샷");
            bots[1].Forget();   // 끊기기 직전에 보낸 답이 닿지 않았으면 같은 선택이 다시 보인다
            start = host.Host.Game.CommandCount;
            yield return Drive(host, vc, bots, () => host.Host.Game.CommandCount >= start + 4, 60, "재접속 뒤 봇 4수");
            yield return Settle(host, bots);
            Check(host, bots);

            // ② 같은 토큰의 새 접속(다른 창) → E_DUPLICATE → 처음 접속은 3초마다 다시 시도(ClearingOld) → 옛 연결이 나가면 그 자리로
            var twin = Join(port, Hi("g2"), "Twin");
            yield return Until(() => twin.ClearingOld, 15, "E_DUPLICATE 뒤 다시 시도 중");
            Assert.AreEqual(NetStatus.Connecting, twin.Status);
            g2.Leave();
            yield return Until(() => twin.Status == NetStatus.Connected, 20, "옛 연결이 나간 뒤 같은 자리로");
            Assert.AreEqual(2, twin.Seat);
            Assert.IsEmpty(bots.SelectMany(b => b.Bad), "합법 밖 거절 없음");
        }

        /// <summary>
        /// 결정 92 · M7 완료 조건 7(02 §10 「직접 연결 한 판」): Relay·UGS 없이 「IP:포트」로 방에 들어가 한 판을 끝낸다. 3인 방(손님 둘).
        /// ① 참가 칸 「127.0.0.1:포트」로 로비 → 준비 → 시작 → 셋업을 NGO 위로 지나 첫 맵 행동에서 [방 닫기](마지막 쓰기 · 손님은 E_HOST_CLOSED — 게임 중이라 [다시 시도]가 있다).
        /// ② 그 방 저장을 막 앞으로 손본다(<see cref="SoloTrail.Heart"/> — 셋업부터 끝까지는 봇 판 평균 명령 1만이라 PlayMode에 둘 수 없다 [가정 M7-31]. dotnet <c>WireTests.M7_막앞방저장_…</c>가 같은 길을 코덱 위에서 간다).
        /// ③ 같은 주소로 다시 연다(이어하기 대기) → 손님은 [다시 시도]로 같은 주소에 다시 붙는다(직접 연결은 주소가 그대로 — 02 §6-3) → 모두 오면 시작 → 길잡이가 종막까지 → 결과.
        /// </summary>
        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M7_직접연결_한판_IP포트로로비셋업_방닫고같은주소로다시열어_모두돌아와종막까지()
        {
            const ushort port = 47795;
            string dir = Path.Combine(Path.GetTempPath(), "sinnoh-netloop-" + Guid.NewGuid().ToString("N"));
            dirs.Add(dir);
            Assert.IsTrue(JoinTarget.TryParse(Loopback + ":" + port, out var target), "참가 칸 IP:포트");
            Assert.AreEqual(JoinKind.Direct, target.Kind);

            // ① 로비 → 셋업
            var vc = new VClock();
            var host = OpenHost(port, Cfg("net-heart", 3), vc, saves: dir);
            var g1 = Join(target, Hi("g1"), "Guest1");
            var g2 = Join(target, Hi("g2"), "Guest2");
            yield return Until(() => g1.Status == NetStatus.Connected && g2.Status == NetStatus.Connected && Members(host.Local.Room) == 3, 15, "손님 둘 접속");
            g1.Send(new SetReady { Ready = true });
            g2.Send(new SetReady { Ready = true });
            yield return Until(() => host.Local.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            var setupBots = new[] { new Bot(host.Local, host.Send, null), new Bot(g1, g1.Send, null), new Bot(g2, g2.Send, null) };
            yield return Drive(host, vc, setupBots, () => host.Local.View != null && host.Local.View.Phase != Phase.Setup, 60, "셋업");
            yield return Settle(host, setupBots);
            int setupCommands = host.Host.Game.CommandCount;
            host.Close();
            yield return Until(() => g1.Status == NetStatus.Failed && g2.Status == NetStatus.Failed, 15, "방 닫힘");
            foreach (var g in new[] { g1, g2 })
            {
                Assert.AreEqual(NetCode.HostClosed, g.FailCode);
                Assert.AreEqual(RoomPhase.InGame, g.FailPhase, "게임 중에 닫혔다 — [다시 시도]가 있다");
            }
            var oldManager = Manager(host);
            yield return Until(() => !oldManager.IsListening, 10, "호스트 NGO 종료");
            Drop(oldManager);
            yield return null;

            // ② 방 저장을 막 앞으로
            var record = SaveStore.Load(Path.Combine(dir, "net-heart"));
            Assert.AreEqual(3, record.Head.Room.Seats.Count, "머리에 방 몫(좌석 토큰)");
            Assert.AreNotEqual(Phase.Setup, record.Snapshot.State.Phase, "셋업 뒤 스냅샷");
            SoloTrail.Heart.After(ctx, record.Snapshot.State);
            record.Commands.Clear();

            // ③ 같은 주소로 다시 열기 → 다시 시도 → 모두 오면 시작
            var vc2 = new VClock { Now = vc.Now };
            var again = OpenHost(port, Cfg("net-heart", 3), vc2, resume: record);
            Assert.AreEqual(RoomPhase.Waiting, again.Host.Phase, "이어하기 대기");
            g1.Retry();
            g2.Retry();
            Assert.AreEqual(NetStatus.Reconnecting, g1.Status);
            yield return Until(() => again.Host.Phase == RoomPhase.InGame && g1.Status == NetStatus.Connected && g2.Status == NetStatus.Connected
                                     && g1.View != null && g2.View != null && again.Local.View != null, 30, "모두 돌아와 시작");
            Assert.AreEqual((1, 2), (g1.Seat, g2.Seat), "같은 자리");

            // ④ 종막까지
            var bots = new[]
            {
                new Bot(again.Local, again.Send, new SoloPilot { Finale = true }), new Bot(g1, g1.Send, new SoloPilot { Finale = true }), new Bot(g2, g2.Send, new SoloPilot { Finale = true }),
            };
            int ended = 0;
            foreach (var b in bots) b.Session.Updated += u => { if (u.Events.Exists(e => e is GameEnded)) ended++; };
            yield return Drive(again, vc2, bots, () => again.Host.Phase == RoomPhase.Result, 180, "종막까지");
            yield return Settle(again, bots);
            Assert.IsTrue(again.Host.Game.GameOver, "게임이 끝났다");
            Assert.AreEqual(3, ended, "세 좌석 모두 게임 끝을 받았다");
            Assert.IsTrue(bots.All(b => b.Session.Room.Phase == RoomPhase.Result), "모두 결과 단계");
            Check(again, bots);
            Assert.IsEmpty(bots.SelectMany(b => b.Bad), "합법 밖 거절 없음");
            Assert.IsTrue(bots.All(b => b.Session.Stream.Dropped == 0 && b.Session.Resyncs == 0), "스트림이 어긋나지 않았다");
            Report($"[M7 직접 연결 한 판] 셋업 명령 {setupCommands} · 이어 한 판 명령 {again.Host.Game.CommandCount} · 답 {bots.Sum(b => b.Sent)} · " +
                   $"손님 1에게 {again.Link.BytesTo(ConnOf(again, 1)) / 1024} KiB · 가장 큰 델타 {again.Link.MaxDeltaBytes} B");

            again.Close();
            yield return Until(() => g1.Status == NetStatus.Failed && g2.Status == NetStatus.Failed, 15, "방 닫힘");
            Assert.AreEqual(RoomPhase.Result, g1.FailPhase);
        }

        // ── 차림 ──

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            Debug.Log(line);
        }

        static Ctx LoadCtx()
        {
            if (ctx != null) return ctx;
            const string dir = "Assets/Sinnoh/Data/Json/";
            Stream F(string f) => File.OpenRead(dir + f);
            using (var b = F("board.json"))
            using (var p = F("pokemon.json"))
            using (var m = F("moves.json"))
            using (var t = F("trainer_cards.json"))
            using (var i = F("items.json"))
            using (var d = F("decks.json"))
            using (var c = F("cards.json"))
            using (var x = F("fixtures.json"))
            using (var e = F("effects.json"))
            {
                var board = BoardLoader.Load(b);
                var db = CardDbLoader.Load(p, m, t);
                var items = ItemDbLoader.Load(i);
                ctx = new Ctx(board, db, DeckDbLoader.Load(d, c, db), FixtureDbLoader.Load(x, board, db, items), items, EffectDbLoader.Load(e, db));
            }
            return ctx;
        }

        static Hello Hi(string token) => new Hello { BuildId = Build, ContentHash = Content, Token = token, Nickname = token, Platform = Platform.Pc };

        static HostConfig Cfg(string roomId, int seats) => new HostConfig
        {
            OwnerToken = "own", OwnerNickname = "방장", RoomId = roomId, BuildId = Build, ContentHash = Content, Seed = 20261118, AiSeed = SoloTrail.AiSeed,
            VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = seats, Clock = ClockPreset.Off },
        };

        static int Members(RoomState room) => room == null ? 0 : room.Seats.Count(s => s != null);

        NetRunner NewRunner(string name)
        {
            var m = NetBootstrap.Create(name);
            var r = m.gameObject.AddComponent<NetRunner>();
            runners.Add(r);
            return r;
        }

        NetHostSession OpenHost(ushort port, HostConfig cfg, VClock vc, string saves = null, SaveRecord resume = null)
        {
            var r = NewRunner("Host");
            r.Clock = () => vc.Now;
            var host = r.OpenHost(r.GetComponent<NetworkManager>(), LoadCtx(), cfg, Hi("own"), saves, resume);
            Assert.IsTrue(host.Start(port, Loopback), "방 열기 " + port);
            return host;
        }

        NetClientSession Join(ushort port, Hello hello, string name) => Join(JoinTarget.Direct(Loopback, port), hello, name);

        NetClientSession Join(JoinTarget target, Hello hello, string name)
        {
            var r = NewRunner(name);
            var c = new NetClientSession(r.GetComponent<NetworkManager>());
            r.Session = c;
            Assert.IsTrue(c.Connect(target, hello));
            return c;
        }

        NetworkManager Manager(NetHostSession host) => runners.First(r => ReferenceEquals(r.Session, host)).GetComponent<NetworkManager>();

        NetworkManager ManagerOf(NetClientSession c) => runners.First(r => ReferenceEquals(r.Session, c)).GetComponent<NetworkManager>();

        void Drop(NetworkManager m)
        {
            var r = m.GetComponent<NetRunner>();
            r.Shutdown();
            runners.Remove(r);
            UnityEngine.Object.Destroy(m.gameObject);
        }

        static int ConnOf(NetHostSession host, int seat)
        {
            for (int conn = 1; conn < 64; conn++)
                if (host.Host.SeatOf(conn) == seat) return conn;
            return -1;
        }

        static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }

        /// <summary>
        /// 봇이 답하는 동안 방의 가짜 시각을 옮긴다: 늘 연출 멈춤 끝까지 · 차단 컷신 끝까지, 세 프레임 동안 아무도 답하지 않고 명령도 늘지 않으면 다음 정해진 일까지(AI 러너가 셈하는 동안은 멈춘다).
        /// </summary>
        static IEnumerator Drive(NetHostSession host, VClock vc, IEnumerable<Bot> bots, Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            int idle = 0, last = -1;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"시간 초과: {what}(명령 {host.Host.Game?.CommandCount})");
                bool acted = false;
                foreach (var b in bots) acted |= b.Step();
                if (!host.Host.AwaitingAi)
                {
                    long hold = 0;
                    for (int s = 0; s < host.Host.SeatCount; s++) hold = Math.Max(hold, host.Host.HoldLeftMs(s));
                    vc.Now = Math.Max(vc.Now + 1 + hold, host.Host.AnimEndMs);
                    int n = host.Host.Game?.CommandCount ?? 0;
                    idle = acted || n != last ? 0 : idle + 1;
                    last = n;
                    if (idle >= 3)
                    {
                        long t = host.Host.NextEventMs;
                        if (t != long.MaxValue && t > vc.Now) vc.Now = t;
                        idle = 0;
                    }
                }
                yield return null;
            }
        }

        /// <summary>보낸 것이 다 닿고 마지막 갱신이 모든 좌석에 올 때까지(손님 판 = 호스트 판).</summary>
        static IEnumerator Settle(NetHostSession host, IEnumerable<Bot> bots)
        {
            int v = -1;
            for (int i = 0; i < 600; i++)
            {
                int now = host.Host.Game.CommandCount;
                bool same = bots.All(b => b.Session.View != null && StateHash.Of(b.Session.View) == StateHash.Of(host.Host.Game.ViewFor(b.Session.Seat)));
                if (same && now == v) yield break;
                v = now;
                yield return null;
            }
        }

        /// <summary>좌석마다 NGO로 받아 되읽은 보기 = 호스트 ViewFor(그 좌석).</summary>
        static void Check(NetHostSession host, IEnumerable<Bot> bots)
        {
            foreach (var b in bots)
                Assert.AreEqual(StateHash.Of(host.Host.Game.ViewFor(b.Session.Seat)), StateHash.Of(b.Session.View), $"좌석 {b.Session.Seat}의 받은 보기 = ViewFor");
        }

        /// <summary>한 좌석의 봇: 받은 보기에서 내 첫 선택에 한 번 답한다(길잡이가 있으면 길잡이 → 없거나 모르면 셋업은 첫 합법 답, 그 밖은 Default). E_HOLD 거절은 다시, 그 밖의 거절은 다음엔 Default.</summary>
        sealed class Bot
        {
            public readonly NetClientSession Session;
            readonly Action<RoomMsg> send;
            readonly SoloPilot pilot;
            readonly HashSet<int> sent = new HashSet<int>();
            readonly HashSet<int> fallback = new HashSet<int>();
            /// <summary>합법 밖·명령 꼴 거절(늦게 닿은 답의 NoSuchChoice·AlreadyAnswered는 뺀다 — 시간 초과·자동 「안 씀」과 겹칠 수 있다).</summary>
            public readonly List<string> Bad = new List<string>();
            public int Sent;

            public Bot(NetClientSession session, Action<RoomMsg> send, SoloPilot pilot)
            {
                Session = session;
                this.send = send;
                this.pilot = pilot;
                session.Updated += u => pilot?.Saw(u.Events);
                session.Received += m =>
                {
                    if (!(m is RejectedMsg r)) return;
                    sent.Remove(r.ChoiceId);
                    if (r.Reason == RejectReason.Hold) return;
                    fallback.Add(r.ChoiceId);
                    if (r.Reason == RejectReason.Illegal || r.Reason == RejectReason.BadCommand || r.Reason == RejectReason.NotYourChoice) Bad.Add($"좌석 {session.Seat} 선택 {r.ChoiceId}: {r.Reason}");
                };
            }

            public void Forget() => sent.Clear();

            public bool Step()
            {
                var v = Session.View;
                if (Session.Status != NetStatus.Connected || v == null) return false;
                var c = v.Pending.Find(x => x.Seat == Session.Seat && !x.Answered && !sent.Contains(x.Id));
                if (c == null) return false;
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(v, LoadCtx(), c, legal);
                ChoiceAnswer a = null;
                if (!fallback.Contains(c.Id))
                {
                    a = legal.Count > 0 ? pilot?.Decide(LoadCtx(), v, c, legal) : null;
                    if (a == null && v.Phase == Phase.Setup && legal.Count > 0) a = legal[0];
                }
                a = a ?? c.Default.Clone();
                sent.Add(c.Id);
                Sent++;
                send(new Submit { Command = Command.AnswerTo(c.Id, a) });
                return true;
            }
        }
    }
}
