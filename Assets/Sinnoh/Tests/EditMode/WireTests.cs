using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「Net ① 직접 연결 · NGO 루프」의 엔진 무관 몫(02 §4-1 · §2-1 · §6-3): 전선 위 코덱(Data <see cref="MessageCodec"/> — 봉투 · "type" · E_FORMAT · E_VERSION),
    /// 참가 칸(<see cref="JoinTarget"/> — 코드 · 링크 · IP[:포트] · wss · 호스트 이름), 막 앞 방 저장에서 이어하기 대기 → 종막까지(PlayMode <c>M7_직접연결_한판</c>이 NGO 위에서 같은 길을 간다).
    /// 방과 손님 사이의 모든 메시지는 <see cref="CodecLink"/>가 바이트로 바꿨다 되읽은 사본으로 오간다(다시 쓰면 같은 바이트여야 한다).
    /// </summary>
    public sealed class WireTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            Console.WriteLine(line);
        }

        /// <summary>방 → 연결을 MessageCodec 바이트로 바꿨다 되읽어 손님 쪽 스트림으로 보기를 만든다. 다시 쓴 바이트가 다르거나 되읽기가 어긋나면 실패.</summary>
        sealed class CodecLink : IRoomLink
        {
            public readonly Dictionary<int, StreamReceiver> Rx = new Dictionary<int, StreamReceiver>();
            public readonly Dictionary<int, GameUpdate> Last = new Dictionary<int, GameUpdate>();
            public readonly Dictionary<int, List<ServerMsg>> Got = new Dictionary<int, List<ServerMsg>>();
            public readonly Dictionary<int, string> Closed = new Dictionary<int, string>();
            public readonly HashSet<string> EventTypes = new HashSet<string>();
            public readonly HashSet<string> MsgTypes = new HashSet<string>();
            public long Bytes;
            public int Messages, MaxDelta, MaxSnapshot, RequestedWithLegal, Hangul;

            public void Send(int conn, ServerMsg msg)
            {
                var json = MessageCodec.Encode(msg, Messages);
                Assert.IsTrue(MessageCodec.TryDecodeServer(json, out var copy, out var error), error);
                Assert.AreEqual(msg.GetType(), copy.GetType());
                Assert.AreEqual(json, MessageCodec.Encode(copy, Messages), $"{msg.GetType().Name}: 되읽어 다시 쓰면 같은 바이트");
                int bytes = Encoding.UTF8.GetByteCount(json);
                Bytes += bytes;
                foreach (char ch in json) if (ch >= 0xAC00 && ch <= 0xD7A3) Hangul++;
                Messages++;
                MsgTypes.Add(copy.GetType().Name);
                if (!Got.TryGetValue(conn, out var list)) Got[conn] = list = new List<ServerMsg>();
                list.Add(copy);
                List<GameEvent> events = null;
                if (copy is GameSnapshot s)
                {
                    events = s.Events;
                    MaxSnapshot = Math.Max(MaxSnapshot, bytes);
                }
                if (copy is GameDelta d)
                {
                    events = d.Events;
                    MaxDelta = Math.Max(MaxDelta, bytes);
                }
                if (events == null) return;
                foreach (var e in events)
                {
                    EventTypes.Add(e.GetType().Name);
                    if (e is ChoiceRequested r && r.Legal != null) RequestedWithLegal++;
                }
                if (!Rx.TryGetValue(conn, out var rx)) Rx[conn] = rx = new StreamReceiver();
                var u = rx.Receive(copy);
                Assert.IsNotNull(u, $"연결 {conn}: 스트림이 어긋났다({rx.LastProblem})");
                Last[conn] = u;
            }

            public void Close(int conn, string code) => Closed[conn] = code;
        }

        static T Round<T>(T msg) where T : RoomMsg
        {
            var json = MessageCodec.Encode(msg, 41);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(json, out var copy, out int seq, out var error), error);
            Assert.AreEqual(41, seq);
            Assert.AreEqual(json, MessageCodec.Encode(copy, 41));
            return (T)copy;
        }

        static Hello Round(Hello h)
        {
            Assert.IsTrue(MessageCodec.TryDecodeHello(MessageCodec.EncodeHello(h), out var copy, out var error), error);
            return copy;
        }

        static Hello Hi(string token) => new Hello { BuildId = "b", ContentHash = "c", Token = token, Nickname = token };

        /// <summary>방 하나 + 좌석 = 연결. 좌석마다 답하는 쪽(answer — null이면 Default)이 자기 연결로 되읽은 보기만 보고 답한다. 메시지는 모두 코덱을 지난다.</summary>
        sealed class Table
        {
            public HostSession Host;
            public CodecLink Link = new CodecLink();
            public readonly int[] Conn;
            public long Now;
            public int Answers;
            readonly HashSet<int> sent = new HashSet<int>();
            readonly HashSet<int> fallback = new HashSet<int>();

            public Table(int seats) => Conn = new int[seats];

            public GameState View(int seat) => Link.Last.TryGetValue(Conn[seat], out var u) ? u.View : null;

            public void Join(int seat, int conn, string token)
            {
                Conn[seat] = conn;
                var ok = Host.Approve(conn, Round(Hi(token)), Now);
                Assert.IsTrue(ok.Ok, ok.ToString());
            }

            public void Msg(int seat, RoomMsg m) => Host.OnMessage(Conn[seat], Round(m), Now);

            /// <summary>멈춤 끝까지 시각을 옮기고 한 좌석이 답한다. 답할 것이 없으면 다음 정해진 일로. 거절된 답은 다음엔 Default.</summary>
            public void Step(Func<int, GameState, PendingChoice, List<ChoiceAnswer>, ChoiceAnswer> answer)
            {
                Now = Math.Max(Now, Host.AnimEndMs);
                Host.Tick(Now);
                for (int seat = 0; seat < Conn.Length; seat++)
                {
                    var v = View(seat);
                    var c = v?.Pending.Find(x => x.Seat == seat && !x.Answered && !sent.Contains(x.Id));
                    if (c == null) continue;
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(v, Ctx, c, legal);
                    var a = fallback.Contains(c.Id) ? c.Default.Clone() : answer?.Invoke(seat, v, c, legal) ?? c.Default.Clone();
                    sent.Add(c.Id);
                    Answers++;
                    var got = Link.Got.TryGetValue(Conn[seat], out var l) ? l.Count : 0;
                    Msg(seat, new Submit { Command = Command.AnswerTo(c.Id, a) });
                    var after = Link.Got[Conn[seat]];
                    for (int i = got; i < after.Count; i++)
                        if (after[i] is RejectedMsg r && r.ChoiceId == c.Id)
                        {
                            Assert.IsFalse(fallback.Contains(c.Id), $"Default도 거절됐다({r.Reason})");
                            sent.Remove(c.Id);
                            fallback.Add(c.Id);
                        }
                    return;
                }
                long t = Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다(답할 것도 정해진 일도 없다)");
                Now = Math.Max(Now, t);
                Host.Tick(Now);
            }

            public Ctx Ctx;
        }

        [Test]
        public void M7_코덱_4인봇방의모든메시지를바이트로왕복해도같은판_Legal은싣지않는다()
        {
            var t = new Table(4) { Ctx = ctx };
            var cfg = new HostConfig
            {
                OwnerToken = "s0", OwnerNickname = "s0", RoomId = "room-wire", BuildId = "b", ContentHash = "c", Seed = 2026111801, AiSeed = 9,
                VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = 4 },
            };
            t.Host = new HostSession(ctx, t.Link, cfg, 0);
            for (int i = 0; i < 4; i++) t.Join(i, i, "s" + i);
            for (int i = 1; i < 4; i++) t.Msg(i, new SetReady { Ready = true });
            t.Msg(0, new StartGame());
            Assert.AreEqual(RoomPhase.InGame, t.Host.Phase);
            var bots = new IAgent[4];
            for (int i = 0; i < 4; i++) bots[i] = new Playout.BotAgent(30 + (ulong)i);
            for (int step = 0; step < 1500 && t.Host.Phase == RoomPhase.InGame; step++)
                t.Step((seat, v, c, legal) => bots[seat].Decide(new AgentInput { View = v, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() },
                    new AgentBudget { Seed = (ulong)c.Id * 131 + (ulong)seat }).Answer);
            for (int seat = 0; seat < 4; seat++)
                Assert.AreEqual(StateHash.Of(t.Host.Game.ViewFor(seat)), StateHash.Of(t.View(seat)), $"좌석 {seat}: 코덱을 지난 보기 = ViewFor");
            Assert.Greater(t.Answers, 500);
            Assert.AreEqual(0, t.Link.RequestedWithLegal, "ChoiceRequested.Legal은 전선에 싣지 않는다 [가정 M7-29]");
            CollectionAssert.IsSubsetOf(new[] { "Welcome", "RoomUpdate", "GameSnapshot", "GameDelta" }, t.Link.MsgTypes);
            Report($"[M7 코덱] 4인 봇 방 답 {t.Answers} · 명령 {t.Host.Game.CommandCount} · 메시지 {t.Link.Messages}개 {t.Link.Bytes / 1024} KiB · 가장 큰 스냅샷 {t.Link.MaxSnapshot} B · 델타 {t.Link.MaxDelta} B · " +
                   $"메시지 종류 {t.Link.MsgTypes.Count} · 이벤트 종류 {t.Link.EventTypes.Count}");
        }

        /// <summary>
        /// M9 언어 ② · 결정 34(좌석마다 다른 언어 [가정]): 방의 전선에는 문자열 키 · 코드 · 빈칸 값만 오가고 화면 글(한글)은 없다 — 닉네임을 로마자로 둔 2인 봇 방의 모든 메시지 바이트에 한글 0.
        /// 그래서 좌석마다 자기 언어로 그린다: 각 좌석이 받은 이벤트로 만든 보드 기록(<see cref="Sinnoh.Fx.BoardLog"/>)을 ko 표로 그리면 한글 줄, en 표로 그리면 한글 0 — 같은 줄 수.
        /// </summary>
        [Test]
        public void M9_언어_전선에는한글이없고_좌석마다받은기록을자기언어로그린다()
        {
            var t = new Table(2) { Ctx = ctx };
            var cfg = new HostConfig
            {
                OwnerToken = "s0", OwnerNickname = "s0", RoomId = "room-lang", BuildId = "b", ContentHash = "c", Seed = 2026111802, AiSeed = 9,
                VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = 2 },
            };
            t.Host = new HostSession(ctx, t.Link, cfg, 0);
            for (int i = 0; i < 2; i++) t.Join(i, i, "s" + i);
            t.Msg(1, new SetReady { Ready = true });
            t.Msg(0, new StartGame());
            Assert.AreEqual(RoomPhase.InGame, t.Host.Phase);
            var bots = new IAgent[2];
            for (int i = 0; i < 2; i++) bots[i] = new Playout.BotAgent(40 + (ulong)i);
            for (int step = 0; step < 400 && t.Host.Phase == RoomPhase.InGame; step++)
                t.Step((seat, v, c, legal) => bots[seat].Decide(new AgentInput { View = v, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() },
                    new AgentBudget { Seed = (ulong)c.Id * 131 + (ulong)seat }).Answer);
            Assert.Greater(t.Answers, 100);
            Assert.AreEqual(0, t.Link.Hangul, "전선 위 메시지에 한글이 없다(엔진 · 방은 키와 값만 보낸다)");

            var ko = TestData.LoadStrings("ko");
            var en = TestData.LoadStrings("en");
            var hangul = new System.Text.RegularExpressions.Regex("[가-힣]");
            for (int seat = 0; seat < 2; seat++)
            {
                var log = new Sinnoh.Fx.BoardLog(ctx);
                foreach (var m in t.Link.Got[t.Conn[seat]])
                {
                    if (m is GameSnapshot s) log.Feed(s.Events);
                    else if (m is GameDelta d) log.Feed(d.Events);
                }
                Assert.Greater(log.Lines.Count, 20, $"좌석 {seat}: 받은 기록 줄");
                int koLines = 0;
                foreach (var line in log.Lines)
                {
                    string inKo = line.Text(k => ko[k], s => "P" + (s + 1));
                    string inEn = line.Text(k => en[k], s => "P" + (s + 1));
                    if (hangul.IsMatch(inKo)) koLines++;
                    Assert.IsFalse(hangul.IsMatch(inEn), $"좌석 {seat} 영어 기록 줄에 한글: {inEn}");
                    Assert.IsFalse(inEn.Contains("{"), $"좌석 {seat} 영어 기록 줄에 채우지 않은 빈칸: {inEn}");
                }
                Assert.Greater(koLines, log.Lines.Count / 2, $"좌석 {seat}: 같은 줄을 한국어로 그리면 한글");
            }
        }

        [Test]
        public void M7_코덱_깨진봉투는E_FORMAT_판이다르면E_VERSION_모르는형식과빠진필드도거절()
        {
            string Code(string json) => MessageCodec.TryDecodeServer(json, out _, out var e) ? null : e.Split('|')[0];
            string RoomCode(string json) => MessageCodec.TryDecodeRoom(json, out _, out _, out var e) ? null : e.Split('|')[0];
            var ok = MessageCodec.Encode(new NoticeMsg { Code = NetCode.PlayerLeft, Seat = 2 }, 5);
            Assert.IsNull(Code(ok));
            Assert.AreEqual(MessageCodec.BadFormat, Code(""));
            Assert.AreEqual(MessageCodec.BadFormat, Code("{\"V\":3"));
            Assert.AreEqual(MessageCodec.BadFormat, Code("[1,2]"));
            Assert.AreEqual(NetCode.Version, Code(ok.Replace("\"V\":" + Protocol.Version, "\"V\":" + (Protocol.Version + 1))), "판이 다르면 E_VERSION");
            Assert.AreEqual(MessageCodec.BadFormat, Code(ok.Replace("NoticeMsg", "Nope")), "모르는 Type");
            Assert.AreEqual(MessageCodec.BadFormat, Code(ok.Replace("\"Seat\"", "\"Sit\"")), "없는 필드");
            Assert.AreEqual(MessageCodec.BadFormat, Code(MessageCodec.Encode(new Welcome(), 1).Replace("Welcome", "GameUpdate")), "한 프로세스 꼴 GameUpdate는 전선에 없다");
            Assert.Throws<ArgumentException>(() => MessageCodec.Encode(new GameUpdate(), 0));
            Assert.AreEqual(MessageCodec.BadFormat, RoomCode(ok), "호스트 메시지를 손님 → 호스트 통로로 받지 않는다");

            // 순서가 다른 봉투(손으로 쓴 것)도 같은 메시지로 읽는다
            var reordered = "{\"Body\":{\"Code\":\"AWAY\",\"Seat\":1,\"Kind\":0,\"Vote\":0},\"Type\":\"NoticeMsg\",\"Seq\":2,\"V\":" + Protocol.Version + "}";
            Assert.IsTrue(MessageCodec.TryDecodeServer(reordered, out var m, out var err), err);
            Assert.AreEqual(1, ((NoticeMsg)m).Seat);

            // 다형 이벤트: "type"이 맨 앞 · 모르는 type · type 두 번 → E_FORMAT
            var delta = new GameDelta { Version = 2, BaseVersion = 1, Patch = new int[0], Events = new List<GameEvent> { new ChoiceRequested { Id = 7, Seat = 1, Legal = new List<ChoiceAnswer> { ChoiceAnswer.Pick(3) } } }, Clock = new ClockState() };
            var dj = MessageCodec.Encode(delta);
            StringAssert.Contains("{\"type\":\"ChoiceRequested\"", dj);
            StringAssert.DoesNotContain("Legal", dj, "Legal은 싣지 않는다 [가정 M7-29]");
            Assert.IsTrue(MessageCodec.TryDecodeServer(dj, out var back, out err), err);
            var ev = (ChoiceRequested)((GameDelta)back).Events[0];
            Assert.AreEqual(7, ev.Id);
            Assert.IsNull(ev.Legal);
            Assert.AreEqual(MessageCodec.BadFormat, Code(dj.Replace("\"type\":\"ChoiceRequested\"", "\"type\":\"Nope\"")));
            Assert.AreEqual(MessageCodec.BadFormat, Code(dj.Replace("\"type\":\"ChoiceRequested\"", "\"type\":\"ChoiceRequested\",\"type\":\"ChoiceRequested\"")));

            // 큰 해시(ulong 위쪽 절반)도 그대로
            var snap = new GameSnapshot { Version = 1, Viewer = 2, Sections = new[] { new[] { 1, 2 }, new int[0] }, Hash = ulong.MaxValue - 5, Events = new List<GameEvent>(), Clock = new ClockState() };
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(snap), out var sb, out err), err);
            Assert.AreEqual(ulong.MaxValue - 5, ((GameSnapshot)sb).Hash);

            // Hello
            Assert.IsFalse(MessageCodec.TryDecodeHello("not json", out _, out err));
            StringAssert.StartsWith(MessageCodec.BadFormat, err);
            var h = Round(new Hello { BuildId = "b1", ContentHash = "c1", Token = "t", Nickname = "손님", Platform = Platform.Web });
            Assert.AreEqual(("b1", "t", "손님", Platform.Web, Protocol.Version), (h.BuildId, h.Token, h.Nickname, h.Platform, h.ProtocolVersion));

            // 크기 경고 문턱
            Assert.IsNull(MessageCodec.SizeWarning(new GameDelta(), StreamBudget.DeltaWarnBytes - 1));
            Assert.IsNotNull(MessageCodec.SizeWarning(new GameDelta(), StreamBudget.DeltaWarnBytes));
            Assert.IsNull(MessageCodec.SizeWarning(new GameSnapshot(), StreamBudget.DeltaWarnBytes));
            Assert.IsNotNull(MessageCodec.SizeWarning(new GameSnapshot(), StreamBudget.SnapshotWarnBytes));
        }

        [Test]
        public void M7_참가칸_IP포트와코드wss호스트이름을읽고_틀린주소는거절()
        {
            string Read(string s) => JoinTarget.TryParse(s, out var t) ? t.Kind + " " + t.Text : null;
            Assert.AreEqual("Direct 100.101.102.103:7777", Read(" 100.101.102.103 "), "포트를 빼면 7777");
            Assert.AreEqual("Direct 127.0.0.1:47791", Read("127.0.0.1:47791"));
            Assert.AreEqual("Direct 10.0.0.7:7777", Read("010.000.000.007"), "앞 0은 지운다");
            Assert.AreEqual("Relay Q7K2MZ", Read("q7k2mz"), "영숫자 6자 = 참가 코드(대문자로)");
            Assert.AreEqual("Secure wss://host.example:8443", Read("wss://Host.Example:8443/"));
            Assert.AreEqual("Secure wss://t.trycloudflare.com", Read("t.trycloudflare.com"), "터널 이름은 포트가 없으면 wss");
            Assert.AreEqual("Direct my-pc.tail1234.ts.net:7777", Read("My-PC.tail1234.ts.net"));
            Assert.AreEqual("Direct my-pc:9000", Read("my-pc:9000"));
            foreach (var bad in new[] { "", "   ", "1.2.3", "1.2.3.256", "1.2.3.4:0", "1.2.3.4:65536", "1.2.3.4:x", "999.1.1.1", "-bad.host", "wss://a/b", "https://x/?code=12", "https://abc.trycloudflare.com/?code=q7k2mz", "http://x/", "a b" })
                Assert.IsNull(Read(bad), $"「{bad}」는 거절");
            Assert.IsTrue(JoinTarget.TryParseAddress("192.168.0.5:7778", out var ip, out var port));
            Assert.AreEqual(("192.168.0.5", (ushort)7778), (ip, port));
            Assert.IsFalse(JoinTarget.TryParseAddress("pc.local", out _, out _), "주소 읽기는 IPv4만");
        }

        /// <summary>
        /// 막 앞 방 저장(<see cref="SoloTrail.Heart"/> — 3막 · 조각 셋 · 좌석 0이 시작의 사당에)을 만든다: 3인 방에서 로비 → 시작 → 셋업(첫 합법 답)을 코덱 위로 지나 첫 맵 행동에서 방을 닫고(마지막 쓰기),
        /// 그 저장의 스냅샷을 손본다(뒤 명령은 버린다 — 손본 판이라 저장은 이어 쓰지 않는다). PlayMode <c>M7_직접연결_한판</c>은 같은 일을 NGO 위에서 한다.
        /// </summary>
        public static SaveRecord HeartRoomSave(Ctx ctx, SaveRecord saved)
        {
            Assert.AreNotEqual(Phase.Setup, saved.Snapshot.State.Phase, "셋업 뒤 스냅샷");
            SoloTrail.Heart.After(ctx, saved.Snapshot.State);
            saved.Commands.Clear();
            return saved;
        }

        [Test]
        public void M7_막앞방저장_이어하기대기에서같은토큰셋이모두오면시작_길잡이가종막까지()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sinnoh-wire-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = new Table(3) { Ctx = ctx };
                var cfg = new HostConfig
                {
                    OwnerToken = "own", OwnerNickname = "방장", RoomId = "room-heart", BuildId = "b", ContentHash = "c", Seed = 20261118, AiSeed = SoloTrail.AiSeed,
                    VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = 3, Clock = ClockPreset.Off }, Store = id => new SaveStore(Path.Combine(dir, id)),
                };
                t.Host = new HostSession(ctx, t.Link, cfg, 0);
                t.Join(0, 0, "own");
                t.Join(1, 1, "g1");
                t.Join(2, 2, "g2");
                t.Msg(1, new SetReady { Ready = true });
                t.Msg(2, new SetReady { Ready = true });
                t.Msg(0, new StartGame());
                for (int i = 0; i < 400 && t.Host.Game.HostCopy().Phase == Phase.Setup; i++) t.Step((seat, v, c, legal) => legal[0]);
                Assert.AreNotEqual(Phase.Setup, t.Host.Game.HostCopy().Phase, "셋업이 끝났다");
                t.Host.Close(t.Now);
                Assert.AreEqual(RoomPhase.Closed, t.Host.Phase);
                for (int i = 1; i < 3; i++) Assert.AreEqual(NetCode.HostClosed, t.Link.Closed[i]);

                var record = HeartRoomSave(ctx, SaveStore.Load(Path.Combine(dir, "room-heart")));
                Assert.AreEqual(3, record.Head.Room.Seats.Count);
                var u = new Table(3) { Ctx = ctx, Now = t.Now };
                var cfg2 = new HostConfig
                {
                    OwnerToken = "own", OwnerNickname = "방장", RoomId = "room-heart", BuildId = "b", ContentHash = "c", AiSeed = SoloTrail.AiSeed, VirtualAgent = new PrincipleAgent(),
                };
                u.Host = HostSession.Resume(ctx, u.Link, cfg2, record, u.Now);
                Assert.AreEqual(RoomPhase.Waiting, u.Host.Phase);
                u.Join(1, 11, "g1");
                u.Join(0, 10, "own");
                Assert.AreEqual(RoomPhase.Waiting, u.Host.Phase, "한 좌석이 아직 안 왔다");
                u.Join(2, 12, "g2");
                Assert.AreEqual(RoomPhase.InGame, u.Host.Phase, "모두 오면 저절로 시작");

                var pilots = new[] { new SoloPilot { Finale = true }, new SoloPilot { Finale = true }, new SoloPilot { Finale = true } };
                int seen = 0;
                for (int i = 0; i < 6000 && u.Host.Phase == RoomPhase.InGame; i++)
                    u.Step((seat, v, c, legal) => pilots[seat].Decide(ctx, v, c, legal));
                foreach (var list in u.Link.Got.Values)
                    foreach (var m in list)
                        if (m is GameDelta d && d.Events.Exists(e => e is GameEnded)) seen++;
                Assert.AreEqual(RoomPhase.Result, u.Host.Phase, "게임이 끝났다");
                Assert.IsTrue(u.Host.Game.GameOver);
                Assert.AreEqual(3, seen, "세 좌석 모두 게임 끝 이벤트를 받았다");
                for (int seat = 0; seat < 3; seat++)
                    Assert.AreEqual(StateHash.Of(u.Host.Game.ViewFor(seat)), StateHash.Of(u.View(seat)), $"좌석 {seat}: 코덱을 지난 보기 = ViewFor");
                Report($"[M7 막 앞 방] 셋업 답 {t.Answers} · 이어 한 판 답 {u.Answers} · 명령 {u.Host.Game.CommandCount} · 메시지 {u.Link.Messages}개 {u.Link.Bytes / 1024} KiB");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
