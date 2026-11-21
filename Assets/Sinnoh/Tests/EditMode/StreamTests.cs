using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「세션 ②」 · 02 §4-4·§4-7·§10: 상태 스트림(StateStream)·델타(StreamPatch)·좌석 스트림(SeatStream)·손님 쪽 적용(StreamReceiver).
    /// 델타 해시(봇 판 명령마다 손님 보기 = 호스트가 그 좌석에 만든 보기), 어긋남 → 스냅샷, 재접속 스냅샷, 크기(좌석별 바이트 → Relay 어림, 02 §2-4).
    /// 바이트는 Data 직렬화(SaveStore.Serialize — 필드만, 형 이름 없음)로 센다. 봉투(Envelope·"type")는 Net 묶음의 MessageCodec이 더한다(메시지마다 수십 바이트).
    /// 보기 누출(전선 위 바이트)은 ViewTests, 되읽기 반사 시험은 GameStateTests.
    /// </summary>
    public partial class StreamTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            Console.WriteLine(line);
        }

        // ── 코덱 ──

        [Test]
        public void M7_델타코덱_넣기빼기바꾸기왕복과작은패치()
        {
            var rng = new Pcg32(20261118);
            int total = 0, patchInts = 0;
            for (int trial = 0; trial < 400; trial++)
            {
                int sections = 1 + rng.Next(6);
                var from = new int[sections][];
                var to = new int[sections][];
                for (int s = 0; s < sections; s++)
                {
                    var a = new List<int>();
                    int len = rng.Next(trial % 5 == 0 ? 2000 : 60);
                    for (int i = 0; i < len; i++) a.Add(rng.Next(4) - 1); // −1·0·1·2만 — 같은 값이 많아 정렬이 헷갈리는 판
                    var b = new List<int>(a);
                    int edits = rng.Next(5);
                    for (int e = 0; e < edits; e++)
                    {
                        int at = b.Count == 0 ? 0 : rng.Next(b.Count + 1);
                        switch (rng.Next(3))
                        {
                            case 0:
                                b.Insert(at, 100 + rng.Next(50));
                                break;
                            case 1:
                                if (at < b.Count) b.RemoveAt(at);
                                break;
                            default:
                                if (at < b.Count) b[at] = 200 + rng.Next(50);
                                break;
                        }
                    }
                    if (trial % 37 == 0) b.Clear();
                    from[s] = a.ToArray();
                    to[s] = b.ToArray();
                    total += b.Count;
                }
                var patch = StreamPatch.Diff(from, to);
                var back = StreamPatch.Apply(from, patch);
                for (int s = 0; s < sections; s++) CollectionAssert.AreEqual(to[s], back[s], $"시도 {trial} 구역 {s}");
                Assert.AreEqual(StateStream.Hash(to), StateStream.Hash(back));
                patchInts += patch.Length;
            }
            Report($"[M7 델타 코덱] 400번 왕복, 새 스트림 {total} int에 패치 {patchInts} int");

            // 덩이 하나: 긴 구역 가운데에 하나 넣기 · 하나 빼기 · 하나 바꾸기 → 머리 4 + 값만
            var big = new int[3000];
            for (int i = 0; i < big.Length; i++) big[i] = i % 7;
            var ins = new List<int>(big);
            ins.Insert(1500, 99);
            Assert.AreEqual(5, StreamPatch.Diff(new[] { big }, new[] { ins.ToArray() }).Length, "넣기 하나");
            var del = new List<int>(big);
            del.RemoveAt(1500);
            Assert.AreEqual(4, StreamPatch.Diff(new[] { big }, new[] { del.ToArray() }).Length, "빼기 하나");
            var set = (int[])big.Clone();
            set[10] = 50;
            set[2900] = 51;
            Assert.AreEqual(10, StreamPatch.Diff(new[] { big }, new[] { set }).Length, "멀리 떨어진 바꾸기 둘 = 덩이 둘");
            Assert.AreEqual(0, StreamPatch.Diff(new[] { big }, new[] { (int[])big.Clone() }).Length, "같으면 빈 패치");

            // 모양이 틀린 패치는 FormatException(손님은 Resync)
            Assert.Throws<FormatException>(() => StreamPatch.Apply(new[] { big }, new[] { 0, 2999, 5, 0 }));
            Assert.Throws<FormatException>(() => StreamPatch.Apply(new[] { big }, new[] { 1, 0, 0, 0 }));
            Assert.Throws<FormatException>(() => StreamPatch.Apply(new[] { big }, new[] { 0, 0, 0, 3, 1 }));
            Assert.Throws<ArgumentException>(() => StreamPatch.Diff(new[] { big }, new[] { big, big }));
        }

        // ── 델타 해시: 솔로 핵 + 좌석 AI, 갱신마다 인코딩 → 되읽기 ──

        /// <summary>핵(LocalSession)의 갱신을 좌석마다 SeatStream으로 바꾸고 StreamReceiver로 되읽어 원래 보기와 맞대 본다.</summary>
        sealed class Loop : ISessionLink
        {
            public readonly SeatStream[] Enc;
            public readonly StreamReceiver[] Rx;
            public int Updates, Deltas;

            public Loop(int seats)
            {
                Enc = new SeatStream[seats];
                Rx = new StreamReceiver[seats];
                for (int i = 0; i < seats; i++)
                {
                    Enc[i] = new SeatStream();
                    Rx[i] = new StreamReceiver();
                }
            }

            public void Send(int seat, ServerMsg msg)
            {
                if (!(msg is GameUpdate u) || seat < 0) return;
                var wire = Enc[seat].Encode(u);
                if (wire is GameDelta) Deltas++;
                var got = Rx[seat].Receive(wire);
                Assert.IsNotNull(got, $"좌석 {seat} 갱신 {u.Version}: 되읽기 실패({Rx[seat].LastProblem})");
                Assert.AreEqual(u.View.Viewer, got.View.Viewer);
                Assert.AreEqual(StateHash.Of(u.View), StateHash.Of(got.View), $"좌석 {seat} 갱신 {u.Version}: 손님 보기 ≠ 호스트가 만든 보기");
                Assert.AreSame(u.Events, got.Events);
                Updates++;
            }
        }

        [Test]
        public void M7_델타해시_봇2에서6인_명령마다손님보기해시는ViewFor해시()
        {
            const int Seeds = 2, Commands = 400;
            int games = 0, updates = 0, deltas = 0;
            for (int seats = 2; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < Seeds; seed++)
                {
                    var agents = new IAgent[seats];
                    for (int i = 0; i < seats; i++) agents[i] = new Playout.BotAgent(seed);
                    var loop = new Loop(seats);
                    var session = LocalSession.Start(ctx, new RuleOptions(), seats, 9000 + seed, Campaign.HeartOfSinnoh,
                        new SessionConfig { VirtualAgent = new PrincipleAgent(), SeatAgents = agents, Out = loop }, 0);
                    while (session.CommandCount < Commands && !session.GameOver)
                    {
                        long t = session.NextDueMs;
                        Assert.AreNotEqual(long.MaxValue, t, $"{seats}인 시드 {seed}: 봇 판이 멈췄다");
                        session.Tick(t);
                    }
                    for (int i = 0; i < seats; i++)
                    {
                        Assert.AreEqual(1, loop.Rx[i].Snapshots, "스냅샷은 시작 때 하나");
                        Assert.AreEqual(0, loop.Rx[i].Dropped);
                        Assert.AreEqual(StateHash.Of(session.ViewFor(i)), StateHash.Of(loop.Rx[i].View), "마지막 보기 = 지금 ViewFor");
                    }
                    games++;
                    updates += loop.Updates;
                    deltas += loop.Deltas;
                }
            Report($"[M7 델타 해시] 봇 판 {games}개(2~6인 × 시드 {Seeds}, 판마다 {Commands}명령) 좌석 갱신 {updates}개(델타 {deltas}) 모두 손님 보기 해시 = ViewFor 해시");
            Assert.Greater(deltas, 5000);
        }

        // ── 방(HostSession) 봇 판 ──

        /// <summary>연결마다 손님 쪽 되읽기 + 바이트 세기. Drop·Corrupt로 다음 델타를 버리거나 망가뜨린다(어긋남 시험).</summary>
        sealed class Wire : IRoomLink
        {
            public readonly Dictionary<int, StreamReceiver> Rx = new Dictionary<int, StreamReceiver>();
            public readonly Dictionary<int, GameUpdate> Last = new Dictionary<int, GameUpdate>();
            public readonly Dictionary<int, List<ServerMsg>> Raw = new Dictionary<int, List<ServerMsg>>();
            public readonly HashSet<int> Drop = new HashSet<int>();
            public readonly HashSet<int> Corrupt = new HashSet<int>();
            public bool Measure;
            public readonly List<int> SnapshotBytes = new List<int>();
            public readonly List<int> DeltaBytes = new List<int>();
            public readonly long[] SeatBytes = new long[SeatId.MaxSeats];
            public readonly int[] SeatMessages = new int[SeatId.MaxSeats];
            public int SnapshotInts;
            /// <summary>델타 바이트 나눔: 패치 · 시계 · 이벤트(종류별).</summary>
            public long PatchBytes, ClockBytes;
            public readonly Dictionary<string, long> EventBytes = new Dictionary<string, long>();
            public int MaxDelta;
            public string MaxDeltaWhat;

            static int Bytes(object o) => Encoding.UTF8.GetByteCount(SaveStore.Serialize(o, false));

            public StreamReceiver Of(int conn)
            {
                if (!Rx.TryGetValue(conn, out var rx)) Rx[conn] = rx = new StreamReceiver();
                return rx;
            }

            public List<ServerMsg> RawOf(int conn)
            {
                if (!Raw.TryGetValue(conn, out var list)) Raw[conn] = list = new List<ServerMsg>();
                return list;
            }

            public void Send(int conn, ServerMsg msg)
            {
                RawOf(conn).Add(msg);
                if (!(msg is GameSnapshot) && !(msg is GameDelta)) return;
                if (Measure)
                {
                    int bytes = Encoding.UTF8.GetByteCount(SaveStore.Serialize(msg, false));
                    if (msg is GameSnapshot s)
                    {
                        SnapshotBytes.Add(bytes);
                        SnapshotInts = Math.Max(SnapshotInts, StateStream.Length(s.Sections));
                    }
                    else
                    {
                        var gd = (GameDelta)msg;
                        DeltaBytes.Add(bytes);
                        PatchBytes += Bytes(gd.Patch);
                        ClockBytes += Bytes(gd.Clock);
                        int biggest = 0;
                        string biggestKind = null;
                        foreach (var e in gd.Events)
                        {
                            string k = e.GetType().Name;
                            int eb = Bytes(e);
                            EventBytes.TryGetValue(k, out long was);
                            EventBytes[k] = was + eb;
                            if (eb > biggest)
                            {
                                biggest = eb;
                                biggestKind = k;
                            }
                        }
                        if (bytes > MaxDelta)
                        {
                            MaxDelta = bytes;
                            MaxDeltaWhat = $"이벤트 {gd.Events.Count}개 · 가장 큰 {biggestKind} {biggest} B · 패치 {gd.Patch.Length} int";
                        }
                    }
                    if (conn < SeatId.MaxSeats)
                    {
                        SeatBytes[conn] += bytes;
                        SeatMessages[conn]++;
                    }
                }
                if (msg is GameDelta d)
                {
                    if (Drop.Remove(conn)) return;
                    if (Corrupt.Remove(conn) && d.Patch.Length > 0)
                    {
                        var bad = (int[])d.Patch.Clone();
                        bad[bad.Length - 1] ^= 0x55; // 마지막 덩이의 마지막 값(또는 머리)을 바꾼다 — 해시나 모양에서 걸린다
                        msg = new GameDelta { BaseVersion = d.BaseVersion, Version = d.Version, Patch = bad, Hash = d.Hash, Events = d.Events, AnimEndMs = d.AnimEndMs, Clock = d.Clock };
                    }
                }
                var u = Of(conn).Receive(msg);
                if (u != null) Last[conn] = u;
            }

            public void Close(int conn, string code) { }
        }

        static Hello Hi(string token) => new Hello { BuildId = "b", ContentHash = "c", Token = token, Nickname = token };

        /// <summary>6좌석 방(연결 = 좌석)에서 좌석마다 봇(Playout.BotAgent)이 자기 연결로 되읽은 보기만 보고 답한다.</summary>
        sealed class BotRoom
        {
            public readonly HostSession Host;
            public readonly Wire Wire = new Wire();
            public readonly int[] Conn;
            public long Now;
            readonly Ctx ctx;
            readonly IAgent[] bots;
            readonly HashSet<int> answered = new HashSet<int>();
            readonly HashSet<int> fallback = new HashSet<int>();

            public BotRoom(Ctx ctx, int seats, ulong seed, ClockPreset clock, bool measure = false, Action<HostConfig> tweak = null)
            {
                this.ctx = ctx;
                Wire.Measure = measure;
                Conn = new int[seats];
                bots = new IAgent[seats];
                var cfg = new HostConfig
                {
                    OwnerToken = "s0", OwnerNickname = "s0", RoomId = "room-stream", BuildId = "b", ContentHash = "c", Seed = 7000 + seed, AiSeed = seed,
                    VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Clock = clock },
                };
                tweak?.Invoke(cfg);
                Host = new HostSession(ctx, Wire, cfg, 0);
                for (int i = 0; i < seats; i++)
                {
                    Conn[i] = i;
                    bots[i] = new Playout.BotAgent(seed);
                    Assert.IsTrue(Host.Approve(i, Hi("s" + i), 0).Ok);
                }
                for (int i = 1; i < seats; i++) Host.OnMessage(i, new SetReady { Ready = true }, 0);
                Host.OnMessage(0, new StartGame(), 0);
                Assert.AreEqual(RoomPhase.InGame, Host.Phase);
            }

            public GameState View(int seat) => Wire.Last.TryGetValue(Conn[seat], out var u) ? u.View : null;

            /// <summary>한 걸음: 멈춤 끝까지 시각을 옮기고 접속한 좌석 하나가 답한다(봇 답이 거절되면 다음엔 Default). 답할 것이 없으면 다음 정해진 일로.</summary>
            public void Step(Func<int, bool> skip = null, Func<int, PendingChoice, bool> leave = null)
            {
                Now = Math.Max(Now, Host.AnimEndMs);
                Host.Tick(Now);
                for (int seat = 0; seat < Conn.Length; seat++)
                {
                    if (skip != null && skip(seat)) continue;
                    var v = View(seat);
                    var c = v?.Pending.Find(x => x.Seat == seat && !x.Answered && !answered.Contains(x.Id) && (leave == null || !leave(seat, x)));
                    if (c == null) continue;
                    ChoiceAnswer a = fallback.Contains(c.Id) ? c.Default.Clone()
                        : bots[seat].Decide(new AgentInput { View = v, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget { Seed = (ulong)c.Id * 131 + (ulong)seat }).Answer;
                    int before = Wire.RawOf(Conn[seat]).Count;
                    answered.Add(c.Id);
                    Host.OnMessage(Conn[seat], new Submit { Command = Command.AnswerTo(c.Id, a) }, Now);
                    var raw = Wire.RawOf(Conn[seat]);
                    for (int i = before; i < raw.Count; i++)
                        if (raw[i] is RejectedMsg r && r.ChoiceId == c.Id)
                        {
                            Assert.IsFalse(fallback.Contains(c.Id), $"Default도 거절됐다({r.Reason})");
                            answered.Remove(c.Id);
                            fallback.Add(c.Id);
                        }
                    return;
                }
                long t = Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다(답할 것도 정해진 일도 없다)");
                Now = Math.Max(Now, t);
                Host.Tick(Now);
            }

            /// <summary>[로비로] 뒤 새 판: 선택 Id가 처음부터 다시 매겨지므로 답한 목록을 비운다.</summary>
            public void NewGame()
            {
                answered.Clear();
                fallback.Clear();
            }

            /// <summary>접속한 좌석의 되읽은 보기 = 지금 호스트의 ViewFor(그 좌석).</summary>
            public void Check(string when)
            {
                for (int seat = 0; seat < Conn.Length; seat++)
                {
                    if (Conn[seat] < 0) continue;
                    var v = View(seat);
                    Assert.IsNotNull(v, $"{when}: 좌석 {seat}에 보기가 없다");
                    Assert.AreEqual(StateHash.Of(Host.Game.ViewFor(seat)), StateHash.Of(v), $"{when}: 좌석 {seat}의 손님 보기 ≠ ViewFor");
                }
            }
        }

        [Test]
        public void M7_델타해시_어긋나면스냅샷다시받기_재접속은스냅샷()
        {
            var r = new BotRoom(ctx, 4, 1, ClockPreset.Relaxed);
            for (int i = 0; i < 60; i++) r.Step();
            r.Check("처음");
            for (int seat = 0; seat < 4; seat++)
            {
                Assert.AreEqual(1, r.Wire.Of(seat).Snapshots, "시작 때 스냅샷 하나");
                Assert.Greater(r.Wire.Of(seat).Deltas, 10, "그 뒤는 델타");
            }

            // ① 델타 하나를 잃으면 다음 델타의 기준이 어긋난다 → 버리고 Resync를 기다린다
            var rx = r.Wire.Of(2);
            r.Wire.Drop.Add(2);
            int guard = 0;
            while (!rx.NeedsResync && guard++ < 200) r.Step();
            Assert.IsTrue(rx.NeedsResync, "기준이 어긋난 델타를 알아챈다");
            StringAssert.Contains("기준", rx.LastProblem);
            int applied = rx.Deltas;
            for (int i = 0; i < 5; i++) r.Step(seat => seat == 2);
            Assert.AreEqual(applied, rx.Deltas, "스냅샷 전 델타는 모두 버린다");
            Assert.IsTrue(rx.NeedsResync);
            r.Host.OnMessage(2, new RequestResync(), r.Now);
            Assert.IsFalse(rx.NeedsResync, "RequestResync → 스냅샷");
            Assert.AreEqual(2, rx.Snapshots);
            r.Check("Resync 뒤");

            // ② 패치 값이 망가지면 해시(또는 모양)에서 걸린다
            r.Wire.Corrupt.Add(1);
            var rx1 = r.Wire.Of(1);
            guard = 0;
            while (!rx1.NeedsResync && guard++ < 200) r.Step();
            Assert.IsTrue(rx1.NeedsResync, "망가진 델타를 알아챈다");
            r.Host.OnMessage(1, new RequestResync(), r.Now);
            Assert.IsFalse(rx1.NeedsResync);
            for (int i = 0; i < 30; i++) r.Step();
            r.Check("망가진 델타 뒤 Resync");

            // ③ 끊긴 동안은 보내지 않고, 돌아오면(새 연결) 스냅샷 하나 → 그 뒤 델타
            r.Host.OnDisconnected(3, r.Now);
            int sent = r.Wire.RawOf(3).Count;
            r.Conn[3] = -1;
            for (int i = 0; i < 30; i++) r.Step(seat => seat == 3);
            int streamWhileAway = 0;
            var away = r.Wire.RawOf(3);
            for (int i = sent; i < away.Count; i++)
                if (away[i] is GameSnapshot || away[i] is GameDelta) streamWhileAway++;
            Assert.AreEqual(0, streamWhileAway, "끊긴 연결에는 갱신을 보내지 않는다");
            Assert.IsTrue(r.Host.Approve(103, Hi("s3"), r.Now).Ok);
            r.Conn[3] = 103;
            var back = r.Wire.RawOf(103).Find(m => m is GameSnapshot || m is GameDelta);
            Assert.IsInstanceOf<GameSnapshot>(back, "돌아온 연결의 첫 스트림 = 스냅샷");
            for (int i = 0; i < 30; i++) r.Step();
            r.Check("재접속 뒤");
            Assert.AreEqual(1, r.Wire.Of(103).Snapshots);
            Assert.Greater(r.Wire.Of(103).Deltas, 0, "그 뒤는 델타");
            Assert.AreEqual(0, r.Wire.Of(103).Dropped);
        }

        [Test]
        public void M7_크기_6인방봇판_좌석별바이트와KiB초()
        {
            // 02 §2-4·§4-7: 6인 방(시계 「느긋하게」)을 봇이 게임 끝까지(상한은 멈춤 막이) — 7걸음마다 손님 보기 = ViewFor, 스트림 메시지 바이트를 좌석마다 센다.
            // 숫자는 plan-log 「M7 세션 ②」에 옮긴다(조건 4의 어림 — 실측은 마감에 UGS 대시보드).
            const int MaxCommands = 40000;
            var sw = Stopwatch.StartNew();
            var r = new BotRoom(ctx, 6, 3, ClockPreset.Relaxed, measure: true);
            int steps = 0;
            while (r.Host.Phase == RoomPhase.InGame && r.Host.Game.CommandCount < MaxCommands)
            {
                r.Step();
                if (++steps % 7 == 0) r.Check($"걸음 {steps}");
            }
            r.Check("끝");
            int commands = r.Host.Game.CommandCount;
            bool over = r.Host.Game.GameOver;
            var w = r.Wire;
            Assert.AreEqual(6, w.SnapshotBytes.Count, "끊김이 없으면 스냅샷은 좌석마다 시작 때 하나");
            long deltaSum = 0;
            int deltaMax = 0;
            foreach (int b in w.DeltaBytes)
            {
                deltaSum += b;
                deltaMax = Math.Max(deltaMax, b);
            }
            var sorted = new List<int>(w.DeltaBytes);
            sorted.Sort();
            int p50 = sorted[sorted.Count / 2], p99 = sorted[sorted.Count * 99 / 100];
            int snapMax = 0;
            foreach (int b in w.SnapshotBytes) snapMax = Math.Max(snapMax, b);
            double deltaAvg = (double)deltaSum / w.DeltaBytes.Count;
            double perSeatUpdates = (double)w.DeltaBytes.Count / 6;
            double gameMinutes = r.Now / 60000.0;
            double seatBytesAvg = 0;
            for (int s = 0; s < 6; s++) seatBytesAvg += w.SeatBytes[s] / 6.0;
            // 어림 ① 02 §2-4 [가정]: 판 전체 명령 분당 6개 → 좌석마다 갱신 분당 6개(명령 하나 = 좌석마다 갱신 하나)
            double kibAssumed = deltaAvg * 6 / 60 / 1024;
            // 어림 ② 봇 판의 게임 시각(봇은 연출이 끝나자마자 답한다 — 사람보다 빠르다 = 위쪽 끝)
            double kibBotPace = seatBytesAvg / (r.Now / 1000.0) / 1024;
            Report($"[M7 크기] 6인 방 봇 판 명령 {commands}{(over ? "(게임 끝)" : "(상한)")} · 게임 시각 {gameMinutes:0.0}분(명령 분당 {commands / gameMinutes:0.0}) · {sw.Elapsed.TotalSeconds:0.0}초");
            Report($"[M7 크기] 스냅샷 {snapMax} B(스트림 {w.SnapshotInts} int) · 델타 {w.DeltaBytes.Count}개 평균 {deltaAvg:0} B · 중앙 {p50} B · 99% {p99} B · 최대 {deltaMax} B · 좌석당 갱신 {perSeatUpdates:0}개 {seatBytesAvg / 1024:0} KiB");
            Report($"[M7 크기] 좌석당 KiB/s: 명령 분당 6개 어림(02 §2-4) {kibAssumed:0.000} · 봇 빠르기(게임 시각) {kibBotPace:0.000} — 목표 평균 {StreamBudget.TargetBytesPerSecond / 1024.0:0.0}");
            var kinds = new List<KeyValuePair<string, long>>(w.EventBytes);
            kinds.Sort((x, y) => y.Value.CompareTo(x.Value));
            var top = new StringBuilder();
            for (int i = 0; i < Math.Min(5, kinds.Count); i++) top.Append($" {kinds[i].Key} {kinds[i].Value / (double)w.DeltaBytes.Count:0}");
            long events = 0;
            foreach (var k in kinds) events += k.Value;
            Report($"[M7 크기] 델타 평균 나눔(B): 패치 {w.PatchBytes / (double)w.DeltaBytes.Count:0} · 시계 {w.ClockBytes / (double)w.DeltaBytes.Count:0} · 이벤트 {events / (double)w.DeltaBytes.Count:0} (큰 종류:{top}) · 최대 델타 = {w.MaxDeltaWhat}");
            Assert.IsTrue(over, "6인 판을 끝까지 돌았다(봇 판 전체)");
            Assert.Less(snapMax, StreamBudget.SnapshotWarnBytes, "스냅샷 경고 한도(02 §4-7)");
            Assert.Less(w.PatchBytes / (double)w.DeltaBytes.Count, 1024, "델타의 상태 몫(패치)은 작다 — 크면 코덱(02 §11 #9)을 다시 본다");
            Assert.Less(deltaAvg, 4096, "델타 평균이 02 §4-7 어림(1~2 KB)에서 크게 벗어났다");
            Assert.Less(kibAssumed * 1024, StreamBudget.TargetBytesPerSecond, "좌석당 평균 0.3 KiB/s(02 §4-7 목표, 명령 분당 6개 어림)");
        }
    }
}
