using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-g: 솔로 세션(LocalSession·LoopbackLink·IAgent, 02 §1-3), 연출 멈춤(PlayTiming, 02 §5-7), 세이브(SaveRecord·SaveStore, 02 §7).
    /// </summary>
    public class SessionTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        /// <summary>AI 입력을 모아 두는 대리(엿보기 시험).</summary>
        sealed class Spy : IAgent
        {
            readonly IAgent inner = new PrincipleAgent();
            public readonly List<AgentInput> Inputs = new List<AgentInput>();
            public readonly List<AgentDecision> Decisions = new List<AgentDecision>();

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                Inputs.Add(input);
                var d = inner.Decide(input, budget);
                Decisions.Add(d);
                return d;
            }
        }

        /// <summary>정해진 번째 쓰기 한 번만 IOException을 던지는 세이브(백신·인덱서가 파일을 잠깐 잡은 경우).</summary>
        sealed class FlakyStore : ISaveStore
        {
            readonly ISaveStore inner;
            readonly string failOn;
            readonly int failAt;
            int calls;
            public int Thrown;

            public FlakyStore(ISaveStore inner, string failOn, int failAt)
            {
                this.inner = inner;
                this.failOn = failOn;
                this.failAt = failAt;
            }

            void Maybe(string op)
            {
                if (op != failOn || calls++ != failAt) return;
                Thrown++;
                throw new IOException($"시험: {op} {failAt}번째에 파일이 잠겼다");
            }

            public void WriteHead(SaveHead head)
            {
                Maybe("head");
                inner.WriteHead(head);
            }

            public void WriteSnapshot(SaveSnapshot snapshot)
            {
                Maybe("snap");
                inner.WriteSnapshot(snapshot);
            }

            public void AppendCommand(SaveLogLine line)
            {
                Maybe("append");
                inner.AppendCommand(line);
            }

            public void TruncateLog(int commandCount) => inner.TruncateLog(commandCount);
        }

        /// <summary>AI가 받은 예산 시드를 모은다(선택 Id와 함께).</summary>
        sealed class SeedSpy : IAgent
        {
            readonly IAgent inner = new PrincipleAgent();
            public readonly List<int> Ids = new List<int>();
            public readonly List<ulong> Seeds = new List<ulong>();

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                Ids.Add(input.Choice.Id);
                Seeds.Add(budget.Seed);
                return inner.Decide(input, budget);
            }
        }

        /// <summary>좌석 0 화면 대신: 링크로 받은 마지막 갱신을 들고, 보기만 보고 답한다(시드 + 선택 Id로 정한 Legal 하나 — 숨은 정보·시각과 무관).</summary>
        sealed class Seat0
        {
            public readonly LocalSession S;
            public GameUpdate Last;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public int Human;

            public Seat0(LocalSession s)
            {
                S = s;
                s.Link.Connect(0, m =>
                {
                    if (m is GameUpdate u)
                    {
                        Last = u;
                        Events.AddRange(u.Events);
                    }
                });
            }

            public PendingChoice Mine() => Last.View.Pending.Find(c => c.Seat == 0 && !c.Answered);

            /// <summary>정해진 일(AI 답)을 그 시각대로 다 처리한 뒤 좌석 0의 선택에 하나 답한다. 답할 것이 없으면 false.</summary>
            public bool Step(Ctx ctx, ulong salt, Func<GameState, PendingChoice, ChoiceAnswer> pick = null)
            {
                S.Tick(long.MaxValue / 4);
                if (S.GameOver) return false;
                var c = Mine();
                if (c == null) return false;
                var legal0 = new List<ChoiceAnswer>();
                Engine.Legal(Last.View, ctx, c, legal0);
                ChoiceAnswer a = pick?.Invoke(Last.View, c);
                if (a != null && !ChoiceRules.Contains(legal0, a)) a = null;
                if (a == null)
                {
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(Last.View, ctx, c, legal);
                    a = legal[new Pcg32(salt, (ulong)c.Id).Next(legal.Count)];
                }
                var r = S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(c.Id, a) }, S.AnimEndMs);
                Assert.IsTrue(r.Accepted, $"{c.Kind}/{c.Sub} {a}: {r}");
                Human++;
                return true;
            }

            /// <summary>humanCommands까지 답한다. MapAction에서 갤럭시단 도전(1막)을 할 수 있으면 그것을 고른다 — AI 답이 판에 끼게.</summary>
            public void Run(Ctx ctx, ulong salt, int humanCommands)
            {
                while (Human < humanCommands)
                    if (!Step(ctx, salt, PreferGalactic)) break;
                S.Tick(long.MaxValue / 4);
            }

            /// <summary>Run과 같되 IOException(세이브 쓰기 실패)은 세어 두고 다음 「프레임」에 다시 한다(Unity Update가 예외를 로그만 남기고 넘어가듯). 던진 수를 돌려준다.</summary>
            public int RunCatching(Ctx ctx, ulong salt, int humanCommands)
            {
                int thrown = 0;
                for (int frame = 0; Human < humanCommands && frame < 10000; frame++)
                {
                    try
                    {
                        if (!Step(ctx, salt, PreferGalactic)) break;
                    }
                    catch (IOException)
                    {
                        thrown++;
                    }
                }
                S.Tick(long.MaxValue / 4);
                return thrown;
            }

            static ChoiceAnswer PreferGalactic(GameState view, PendingChoice c) =>
                c.Kind == ChoiceKind.MapAction && c.Id % 3 == 0 ? ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic) : null;
        }

        SessionConfig Cfg(ISaveStore store = null, IAgent agent = null) =>
            new SessionConfig { VirtualAgent = agent ?? new PrincipleAgent(), Store = store, ContentHash = "test-content", SaveId = "m3g" };

        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "sinnoh-m3g-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void Clean(params string[] dirs)
        {
            foreach (var d in dirs)
                if (d != null && Directory.Exists(d)) Directory.Delete(d, true);
        }

        const ulong Seed = 20261107;
        const ulong Salt = 11;
        const int Commands = 120;

        // ── 세이브·이어하기 ──

        [Test]
        public void M3_이어하기해시같음()
        {
            string a = TempDir(), b = TempDir(), c = TempDir();
            try
            {
                // 끊지 않고 간 판
                var runA = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(new SaveStore(a)), 0));
                runA.Run(ctx, Salt, Commands);
                Assert.AreEqual(Commands, runA.Human, "판이 일찍 멈췄다");
                ulong final = runA.S.Hash;
                int finalCount = runA.S.CommandCount;
                Assert.Greater(finalCount, Commands, "AI 답도 명령으로 남는다");

                // 리플레이(01 §10): 시드와 첫 상태(NewGame + Start)에서 로그 전부 → 같은 해시. 명령마다 해시를 적어 둔다
                var log = SaveStore.ReadLog(a);
                Assert.AreEqual(finalCount, log.Count);
                var replay = Engine.NewGame(ctx, new RuleOptions(), 1, Seed);
                Engine.Start(replay, ctx, null);
                var hashes = new List<ulong> { StateHash.Of(replay) };
                foreach (var line in log)
                {
                    Assert.AreEqual(replay.CommandCount, line.Seq);
                    Scene.Accept(Engine.Submit(replay, ctx, line.Seat, line.Command, null), $"리플레이 {line.Seq}");
                    hashes.Add(StateHash.Of(replay));
                }
                Assert.AreEqual(final, StateHash.Of(replay), "리플레이");

                // 중간에 끊고(세션을 버림) 세이브에서 이어 간 판 = 끊지 않은 판
                var runB = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(new SaveStore(b)), 0));
                runB.Run(ctx, Salt, Commands / 2 + 1); // 쉬는 때 바로 뒤에서 끊는다(흐름이 바뀌면 다시 맞춘다 — 핸들러 갈래만이면 +2, 사당·전설과 합친 뒤 +1, 11-14)
                int cut = runB.S.CommandCount;
                var rec = SaveStore.Load(b);
                Assert.IsEmpty(rec.Warnings);
                Assert.Less(rec.Snapshot.CommandCount, cut, "스냅샷 뒤 로그 줄을 다시 적용하는 경우를 시험한다");
                Assert.Greater(rec.Commands.Count, 0);
                var resumed = new Seat0(LocalSession.Resume(ctx, rec, Cfg(new SaveStore(b)), 0)) { Human = runB.Human };
                Assert.AreEqual(cut, resumed.S.CommandCount);
                Assert.AreEqual(hashes[cut], resumed.S.Hash, "이어 받은 상태");
                resumed.Run(ctx, Salt, Commands);
                Assert.AreEqual(finalCount, resumed.S.CommandCount);
                Assert.AreEqual(final, resumed.S.Hash, "이어 간 판의 끝 해시");

                // 쓰다 끊긴 마지막 줄 + 깨진 최신 스냅샷(02 §10 세이브 시험): 그 줄을 버리고 앞 스냅샷 + 로그로
                var runC = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(new SaveStore(c)), 0));
                runC.Run(ctx, Salt, Commands / 3);
                int cutC = runC.S.CommandCount;
                File.AppendAllText(Path.Combine(c, SaveStore.LogFile), "{\"Seq\":" + cutC + ",\"Seat\":0,\"Comm");
                File.WriteAllText(Path.Combine(c, "snap-" + (cutC + 1) + ".json"), "{ 깨짐");
                var recC = SaveStore.Load(c);
                Assert.AreEqual(2, recC.Warnings.Count, string.Join(" / ", recC.Warnings));
                var resumedC = new Seat0(LocalSession.Resume(ctx, recC, Cfg(new SaveStore(c)), 0)) { Human = runC.Human };
                Assert.AreEqual(cutC, resumedC.S.CommandCount);
                Assert.AreEqual(hashes[cutC], resumedC.S.Hash);
                File.Delete(Path.Combine(c, "snap-" + (cutC + 1) + ".json"));
                resumedC.Run(ctx, Salt, Commands);
                Assert.AreEqual(final, resumedC.S.Hash, "잘린 줄 뒤에 이어 쓴 로그");
                var again = SaveStore.Load(c);
                Assert.IsEmpty(again.Warnings, "잘린 조각은 다음 덧붙이기 전에 잘라 냈다");
                Assert.AreEqual(final, LocalSession.Resume(ctx, again, Cfg(), 0).Hash);
            }
            finally
            {
                Clean(a, b, c);
            }
        }

        [Test]
        public void M3_세이브파일은머리스냅샷로그()
        {
            string d = TempDir();
            try
            {
                var run = new Seat0(LocalSession.Start(ctx, new RuleOptions { CaptureAddsFatigue = false }, 1, 7, Campaign.HeartOfSinnoh, Cfg(new SaveStore(d)), 0));
                run.Run(ctx, 3, 40);
                var files = new List<string>();
                foreach (var f in Directory.GetFiles(d)) files.Add(Path.GetFileName(f));
                Assert.Contains(SaveStore.HeadFile, files);
                Assert.Contains(SaveStore.LogFile, files);
                Assert.AreEqual(SaveStore.KeepSnapshots, files.FindAll(f => f.StartsWith("snap-")).Count, "최근 2개만: " + string.Join(",", files));
                Assert.IsFalse(files.Exists(f => f.EndsWith(".tmp")), "임시 파일이 남지 않는다");
                var rec = SaveStore.Load(d);
                Assert.AreEqual("test-content", rec.Head.ContentHash);
                Assert.AreEqual(7UL, rec.Head.Seed);
                Assert.IsFalse(rec.Head.Options.CaptureAddsFatigue, "RuleOptions가 머리에");
                Assert.AreEqual(ctx.Cards.PokemonIds.Count, rec.Head.IdTables.Find(t => t.Name == "pokemon").Keys.Count);
                Assert.AreEqual(run.S.CommandCount, rec.Snapshot.CommandCount + rec.Commands.Count);
                Assert.AreEqual(rec.Snapshot.CommandCount, rec.Head.Summary.CommandCount);
                // 스냅샷 왕복: 같은 해시, Rng(ulong)까지
                var snap = rec.Snapshot;
                var back = SaveStore.Deserialize<SaveSnapshot>(SaveStore.Serialize(snap, false));
                Assert.AreEqual(StateHash.Of(snap.State), StateHash.Of(back.State));
                Assert.AreEqual(snap.State.Rng.State, back.State.Rng.State);
                Assert.AreEqual(SeatId.HostViewer, back.State.Viewer);
                Assert.Throws<NotSupportedException>(() => LocalSession.Resume(ctx, rec, new SessionConfig { VirtualAgent = new PrincipleAgent(), ContentHash = "other" }, 0),
                    "데이터 해시가 다르면 아직 거절(02 §7 IdTable 다시 맞추기는 나중)");
            }
            finally
            {
                Clean(d);
            }
        }

        [Test]
        public void M3_세이브쓰기실패에도판이멈추지않음()
        {
            // 검토 재현: 쓰기 예외가 엔진이 상태를 바꾼 뒤에 나면 Publish·Schedule을 건너뛰어 솔로 판이 영구히 멈추고 로그에 빈칸이 생겼다
            string a = TempDir(), b = TempDir(), c = TempDir();
            try
            {
                const int n = 60;
                var baseRun = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(new SaveStore(a)), 0));
                baseRun.Run(ctx, Salt, n);

                // 로그 줄 쓰기가 한 번 실패: 명령 전 상태로 되돌리고 던진다 → 다음 프레임에 같은 일을 다시 해 같은 판이 된다
                var appendFail = new FlakyStore(new SaveStore(b), "append", 7);
                var runB = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(appendFail), 0));
                Assert.AreEqual(1, runB.RunCatching(ctx, Salt, n), "예외는 한 번 바깥으로");
                Assert.AreEqual(1, appendFail.Thrown);
                Assert.AreEqual(baseRun.S.CommandCount, runB.S.CommandCount);
                Assert.AreEqual(baseRun.S.Hash, runB.S.Hash, "실패가 판을 바꾸지 않는다");
                var log = SaveStore.ReadLog(b);
                Assert.AreEqual(runB.S.CommandCount, log.Count);
                for (int i = 0; i < log.Count; i++) Assert.AreEqual(i, log[i].Seq, "로그에 빈칸이 없다");
                var recB = SaveStore.Load(b);
                Assert.IsEmpty(recB.Warnings);
                Assert.AreEqual(runB.S.Hash, LocalSession.Resume(ctx, recB, Cfg(), 0).Hash);

                // 머리 쓰기가 한 번 실패: 스냅샷·머리는 로그의 캐시라 던지지 않고 경고, 다음 명령 뒤에 다시 쓴다
                var headFail = new FlakyStore(new SaveStore(c), "head", 2);
                var runC = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(headFail), 0));
                Assert.AreEqual(0, runC.RunCatching(ctx, Salt, n));
                Assert.AreEqual(1, headFail.Thrown);
                Assert.AreEqual(1, runC.S.Warnings.Count, string.Join(" / ", runC.S.Warnings));
                Assert.AreEqual(baseRun.S.Hash, runC.S.Hash);
                var recC = SaveStore.Load(c);
                Assert.AreEqual(runC.S.Hash, LocalSession.Resume(ctx, recC, Cfg(), 0).Hash);
            }
            finally
            {
                Clean(a, b, c);
            }
        }

        /// <summary>가장 새 snap-&lt;n&gt;.json(번호, 경로).</summary>
        static (int Number, string Path) NewestSnap(string dir)
        {
            int best = -1;
            string path = null;
            foreach (var f in Directory.GetFiles(dir, "snap-*.json"))
            {
                int num = int.Parse(Path.GetFileNameWithoutExtension(f).Substring("snap-".Length));
                if (num > best)
                {
                    best = num;
                    path = f;
                }
            }
            return (best, path);
        }

        [Test]
        public void M3_이어하기는체크포인트를맞대보고빠진것을채운다()
        {
            // 01 §10 「다시 적용할 때 해시가 다르면 어긋남을 알린다」: 검토 때는 체크포인트를 쓰기만 하고 비교하는 곳이 없었다
            string d = TempDir();
            try
            {
                var run = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, Cfg(new SaveStore(d)), 0));
                int n = 50;
                run.Run(ctx, Salt, n);
                // 가장 새 스냅샷이 턴 끝(체크포인트가 있는 곳)일 때까지 조금 더 간다
                while (run.S.Checkpoints[run.S.Checkpoints.Count - 1].CommandCount != NewestSnap(d).Number && n < 200) run.Run(ctx, Salt, ++n);
                var expected = new List<SaveCheckpoint>(run.S.Checkpoints);
                Assert.AreEqual(expected[expected.Count - 1].CommandCount, NewestSnap(d).Number);

                // 리플레이: 시드 + 로그 전부, 체크포인트마다 맞대 본다
                var head = SaveStore.Load(d).Head;
                var lines = SaveStore.ReadLog(d);
                Assert.AreEqual(run.S.Hash, StateHash.Of(LocalSession.Replay(ctx, head, lines, expected)));
                var tampered = new List<SaveCheckpoint>();
                foreach (var cp in expected) tampered.Add(new SaveCheckpoint { CommandCount = cp.CommandCount, Hash = cp.Hash });
                tampered[tampered.Count / 2].Hash ^= 1;
                Assert.Throws<InvalidOperationException>(() => LocalSession.Replay(ctx, head, lines, tampered), "체크포인트가 다르면 어긋남");

                // 최신 스냅샷이 깨지면 앞 스냅샷 + 로그로 이어 가고, 그 사이 턴 끝 체크포인트를 다시 채운다(전에는 영구히 빠졌다)
                File.WriteAllText(NewestSnap(d).Path, "{ 깨짐");
                var rec = SaveStore.Load(d);
                Assert.AreEqual(1, rec.Warnings.Count, string.Join(" / ", rec.Warnings));
                Assert.Less(rec.Snapshot.Checkpoints.Count, expected.Count, "앞 스냅샷에는 그 뒤 체크포인트가 없다");
                var resumed = LocalSession.Resume(ctx, rec, Cfg(), 0);
                Assert.AreEqual(run.S.Hash, resumed.Hash);
                Assert.AreEqual(expected.Count, resumed.Checkpoints.Count, "빠진 체크포인트를 채웠다");
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert.AreEqual(expected[i].CommandCount, resumed.Checkpoints[i].CommandCount);
                    Assert.AreEqual(expected[i].Hash, resumed.Checkpoints[i].Hash);
                }

                // 이미 아는 체크포인트와 다시 적용한 해시가 다르면 어긋남을 알린다
                var rec2 = SaveStore.Load(d);
                var later = expected.Find(cp => cp.CommandCount > rec2.Snapshot.CommandCount);
                rec2.Snapshot.Checkpoints.Add(new SaveCheckpoint { CommandCount = later.CommandCount, Hash = later.Hash ^ 1 });
                Assert.Throws<InvalidOperationException>(() => LocalSession.Resume(ctx, rec2, Cfg(), 0), "다시 적용한 턴 끝의 해시 ≠ 체크포인트");

                // 스냅샷 상태 해시가 같은 CommandCount의 체크포인트와 다르면(JSON 왕복 손실 등) 거절
                var host = run.S.HostCopy();
                var rec3 = new SaveRecord { Head = run.S.Head, Snapshot = new SaveSnapshot { CommandCount = host.CommandCount, State = host } };
                rec3.Snapshot.Checkpoints.Add(new SaveCheckpoint { CommandCount = host.CommandCount, Hash = StateHash.Of(host) ^ 1 });
                Assert.Throws<InvalidOperationException>(() => LocalSession.Resume(ctx, rec3, Cfg(), 0), "스냅샷 해시 ≠ 체크포인트");
                rec3.Snapshot.Checkpoints[0].Hash = StateHash.Of(host);
                Assert.AreEqual(StateHash.Of(host), LocalSession.Resume(ctx, rec3, Cfg(), 0).Hash);
            }
            finally
            {
                Clean(d);
            }
        }

        [Test]
        public void M3_이어하기는데이터표가다르면거절_보기사본도거절()
        {
            var cfg = new SessionConfig { VirtualAgent = new PrincipleAgent() }; // ContentHash 없음(manifest 없는 개발 빌드)
            var run = SoloToMap(cfg);
            var host = run.S.HostCopy();
            SaveRecord Rec(GameState st) => new SaveRecord { Head = run.S.Head, Snapshot = new SaveSnapshot { CommandCount = st.CommandCount, State = st } };
            Assert.IsNull(run.S.Head.ContentHash);
            Assert.AreEqual(StateHash.Of(host), LocalSession.Resume(ctx, Rec(host), cfg, 0).Hash, "성한 세이브");

            // 해시가 없어도 표의 키 목록이 다르면(행 하나가 끼어 색인이 밀림) 거절 — 검토 재현: 전에는 다른 종으로 게임을 이어 갔다
            var keys = run.S.Head.IdTables.Find(t => t.Name == "pokemon").Keys;
            keys.Insert(1, "9999:0");
            Assert.Throws<NotSupportedException>(() => LocalSession.Resume(ctx, Rec(host), cfg, 0));
            keys.RemoveAt(1);
            Assert.AreEqual(StateHash.Of(host), LocalSession.Resume(ctx, Rec(host), cfg, 0).Hash);

            // 보기 사본(Viewer ≠ 원본, Rng 0/0)이 든 세이브는 덮어써서 이어 가지 않는다
            Assert.Throws<ArgumentException>(() => LocalSession.Resume(ctx, Rec(Engine.ViewFor(host, SeatId.Observer)), cfg, 0));
            var even = host.Clone();
            even.Rng.Increment &= ~1UL;
            Assert.Throws<ArgumentException>(() => LocalSession.Resume(ctx, Rec(even), cfg, 0), "PCG 증분은 늘 홀수");
        }

        // ── AI 연결 ──

        /// <summary>1인 판을 셋업부터 첫 MapAction까지(좌석 0은 Default로 답한다).</summary>
        Seat0 SoloToMap(SessionConfig cfg, ulong seed = Seed)
        {
            var run = new Seat0(LocalSession.Start(ctx, new RuleOptions(), 1, seed, Campaign.HeartOfSinnoh, cfg, 0));
            for (int i = 0; i < 20; i++)
            {
                run.S.Tick(run.S.AnimEndMs);
                var c = run.Mine();
                if (c.Kind == ChoiceKind.MapAction) return run;
                Assert.IsTrue(run.S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(c.Id, c.Default.Clone()) }, run.S.AnimEndMs).Accepted);
            }
            Assert.Fail("첫 MapAction에 닿지 않았다");
            return null;
        }

        static bool IsMasked(GameState v, out string why)
        {
            why = null;
            if (v.Viewer != SeatId.Observer) why = $"Viewer {v.Viewer}";
            else if (v.Rng.State != 0 || v.Rng.Increment != 0) why = "Rng";
            else
            {
                foreach (var seat in v.Seats)
                {
                    if (seat.FateHand.Exists(x => x != Engine.Unknown)) why = "운명 손패";
                    if (seat.TrainerCardOffer.Exists(x => x != Engine.Unknown)) why = "트레이너 카드 후보";
                }
                for (int d = 0; d < v.Decks.Length; d++)
                {
                    // 전설 덱은 내용이 공개 정보라 순서만 가린다(카드 색인 오름차순 — Engine.ViewFor)
                    if (d == (int)DeckId.Legendary)
                    {
                        for (int i = 1; i < v.Decks[d].Count; i++)
                            if (v.Decks[d][i - 1] > v.Decks[d][i]) why = "전설 덱 순서";
                        continue;
                    }
                    // 은행상회 덱의 맨 위 거래 카드 1장은 공개(rulings A4)
                    int hidden = d == (int)DeckId.GinkgoTrade ? v.Decks[d].Count - 1 : v.Decks[d].Count;
                    for (int i = 0; i < hidden; i++)
                        if (v.Decks[d][i] != Engine.Unknown) why = "덱 순서";
                }
                foreach (var t in v.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && t.Def != Engine.Unknown) why = "뒷면 숨겨진 장소";
            }
            return why == null;
        }

        [Test]
        public void M3_AI는관전자보기와공개기록만본다()
        {
            // 솔로 판: 갤럭시단 AI(조무래기 배틀)가 받은 모든 입력에서 숨은 칸이 가려졌고, 공개 기록은 관전자에게 거른 것이다
            var spy = new Spy();
            var run = SoloToMap(Cfg(agent: spy));
            var map = run.Mine();
            Assert.IsTrue(run.S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(map.Id, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic)) }, run.S.AnimEndMs).Accepted);
            for (int i = 0; i < 60 && run.S.HostCopy().Battle != null; i++) run.Step(ctx, 5);
            Assert.Greater(spy.Inputs.Count, 0, "갤럭시단 선택이 AI에게 갔다");
            foreach (var input in spy.Inputs)
            {
                Assert.IsTrue(IsMasked(input.View, out var why), why);
                Assert.Less(input.Choice.Seat, 0, "가상 응답자 선택만");
                foreach (var e in input.PublicLog)
                    if (e is TrainerCardsDealt t) Assert.IsNull(t.CardKeys, "남의 트레이너 카드 후보는 공개 기록에 없다");
                    else if (e is ChoiceRequested r && r.Seat >= 0) Assert.IsNull(r.Legal, "좌석 선택의 Legal은 그 좌석에만");
            }
            Assert.IsTrue(run.Events.Exists(e => e is ActionStarted s && s.Seat == SeatId.Galactic && s.Auto == AutoKind.Ai), "AI 답은 Auto = Ai");
            var answered = run.Events.Find(e => e is ChoiceAnswered x && x.ByAi) as ChoiceAnswered;
            Assert.IsNotNull(answered?.Reason, "AI 사유가 실린다(04 73행)");

            // 숨은 정보만 다른 두 원본 → AI가 받는 보기가 같고 결정도 같다
            var s = Flow.Started(ctx, 1, Seed);
            var c = Flow.Only(s, ChoiceKind.MapAction);
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic)), null));
            Assert.IsTrue(s.Pending.Exists(x => x.Seat == SeatId.Galactic), "갤럭시단 선택이 열려 있다");
            var x1 = s.Clone();
            var x2 = s.Clone();
            var fate = ctx.Deck(DeckId.ChangeOfFate).Cards;
            x1.Seats[0].FateHand.Add(fate[0].Card);
            x2.Seats[0].FateHand.Add(fate[fate.Count - 1].Card);
            x2.Rng.State ^= 0x5DEECE66DUL;
            x2.Decks[(int)DeckId.Strong].Reverse();
            x2.Decks[(int)DeckId.TrainerCard].Reverse();
            int h1 = -1, h2 = -1;
            for (int i = 0; i < x2.Tokens.Count; i++)
            {
                var t = x2.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.FaceUp) continue;
                if (h1 < 0) h1 = i;
                else if (x2.Tokens[h1].Def != t.Def) { h2 = i; break; }
            }
            Assert.GreaterOrEqual(h2, 0);
            int tmp = x2.Tokens[h1].Def;
            x2.Tokens[h1].Def = x2.Tokens[h2].Def;
            x2.Tokens[h2].Def = tmp;
            Assert.AreNotEqual(StateHash.Of(x1), StateHash.Of(x2));
            var spy1 = new Spy();
            var spy2 = new Spy();
            var s1 = LocalSession.FromState(ctx, x1, Cfg(agent: spy1), 0);
            var s2 = LocalSession.FromState(ctx, x2, Cfg(agent: spy2), 0);
            s1.Tick(long.MaxValue / 4);
            s2.Tick(long.MaxValue / 4);
            // 첫 결정만 비교한다 — 그 뒤 주사위는 게임 Rng(숨은 정보)라 공개 결과가 갈릴 수 있다
            Assert.Greater(spy1.Inputs.Count, 0);
            Assert.Greater(spy2.Inputs.Count, 0);
            Assert.AreEqual(StateHash.Of(spy1.Inputs[0].View), StateHash.Of(spy2.Inputs[0].View), "AI가 받은 보기");
            Assert.AreEqual(spy1.Decisions[0].Answer.ToString(), spy2.Decisions[0].Answer.ToString(), "AI 결정");
        }

        /// <summary>1인 판에서 갤럭시단 배틀을 걸어 AI가 받은 예산 시드를 모은다.</summary>
        SeedSpy AiSeeds(ulong gameSeed, ulong aiSeed)
        {
            var spy = new SeedSpy();
            var cfg = Cfg(agent: spy);
            cfg.Ai.Seed = aiSeed;
            var run = SoloToMap(cfg, gameSeed);
            var map = run.Mine();
            Assert.IsTrue(run.S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(map.Id, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic)) }, run.S.AnimEndMs).Accepted);
            run.S.Tick(long.MaxValue / 4);
            Assert.Greater(spy.Ids.Count, 0, "갤럭시단 선택이 AI에게 갔다");
            Assert.AreEqual(aiSeed, run.S.Head.Ai.Seed, "AI 시드는 머리(방 설정)에 남는다");
            return spy;
        }

        [Test]
        public void M3_AI예산시드는게임시드와무관()
        {
            // 검토 재현: 예산 시드 = 게임 시드 × 홀수 + 선택 Id라 AI가 되돌려 게임 시드를 얻고 NewGame으로 숨은 상태(뒷면 52개·덱 순서·Rng)를 모두 재현했다(01 §4·§9-1).
            // 이제 AI 시드는 방 값(AiSettings.Seed)과 선택 Id만으로 정해진다 — 게임 시드가 달라도 같은 선택이면 같다.
            var a = AiSeeds(101, 777);
            var b = AiSeeds(202, 777);
            int common = 0;
            for (int i = 0; i < a.Ids.Count; i++)
            {
                int j = b.Ids.IndexOf(a.Ids[i]);
                if (j < 0) continue;
                Assert.AreEqual(a.Seeds[i], b.Seeds[j], $"선택 {a.Ids[i]}: 게임 시드가 달라도 AI 시드는 같다");
                common++;
            }
            Assert.Greater(common, 0, "두 판에 같은 Id의 AI 선택이 있다");
            var c = AiSeeds(101, 778);
            Assert.AreEqual(a.Ids[0], c.Ids[0]);
            Assert.AreNotEqual(a.Seeds[0], c.Seeds[0], "AI 시드를 바꾸면 바뀐다");
        }

        // ── 연출 멈춤과 시계 ──

        [Test]
        public void M3_AI답은연출멈춤이끝난뒤적용()
        {
            var run = SoloToMap(Cfg());
            var map = run.Mine();
            long t0 = run.S.AnimEndMs;
            Assert.IsTrue(run.S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(map.Id, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic)) }, t0).Accepted);
            long end = run.S.AnimEndMs;
            Assert.Greater(end, t0, "배틀 들어가기 연출만큼 멈춘다");
            var galactic = run.Last.View.Pending.Find(x => x.Seat == SeatId.Galactic);
            Assert.IsNotNull(galactic, "갤럭시단 선택이 열렸다");
            Assert.AreEqual(end, run.S.NextDueMs, "AI 답 = 시계 시작(멈춤 끝)");
            int count = run.S.CommandCount;
            run.S.Tick(end - 1);
            Assert.AreEqual(count, run.S.CommandCount, "멈춤이 끝나기 전에는 적용하지 않는다");
            run.S.Tick(end);
            Assert.Greater(run.S.CommandCount, count);
            Assert.IsNull(run.S.ViewFor(0).Pending.Find(x => x.Id == galactic.Id));
        }

        [Test]
        public void M3_멈춤끝까지시계를걸지않음()
        {
            var cfg = Cfg();
            cfg.ClockMs = new int[6];
            cfg.ClockMs[(int)ClockKind.MapTurn] = 1000;
            var run = SoloToMap(cfg);
            var map = run.Mine();
            long end = run.S.AnimEndMs;
            Assert.AreEqual(end + 1000, run.S.NextDueMs, "MapTurn 시계는 멈춤 끝에서 시작");
            run.S.Tick(end + 999);
            Assert.IsNotNull(run.S.ViewFor(0).Pending.Find(x => x.Id == map.Id));
            int at = run.Events.Count;
            run.S.Tick(end + 1000);
            Assert.IsNull(run.S.ViewFor(0).Pending.Find(x => x.Id == map.Id), "시간 초과");
            var news = run.Events.GetRange(at, run.Events.Count - at);
            Assert.IsTrue(news.Exists(e => e is ActionStarted s && s.Auto == AutoKind.Timeout));
            Assert.IsTrue(news.Exists(e => e is ChoiceAnswered x && x.Id == map.Id && x.ByTimeout));
        }

        [Test]
        public void M3_숨은손패반응창은최소시간뒤적용()
        {
            // 운명 1장을 가진 좌석 0의 갤럭시단 배틀: 운명 창(HandGated)은 쓸 카드가 없어도 열리고(장수로), 세션이 800ms 뒤 「안 씀」으로 답한다
            var s = Battles.Game(ctx, 2); // 한 좌석 판은 최소 시간이 없다(숨길 상대가 없음)
            Scene.Give(s, ctx, 0, "418:0");
            s.Seats[0].FateHand.Add(ctx.Deck(DeckId.ChangeOfFate).Cards[0].Card);
            int g = Scene.Spawn(s, ctx, "399:0", SeatId.Galactic);
            Battles.Start(s, ctx, null, BattleKind.Galactic, 0, SeatId.Galactic, -1, g);
            var run = new Seat0(LocalSession.FromState(ctx, s, Cfg(), 0));
            PendingChoice gate = null;
            for (int i = 0; i < 20 && gate == null; i++)
            {
                gate = run.S.ViewFor(0).Pending.Find(x => x.HandGated && x.Seat == 0);
                if (gate != null) break;
                // AI 답을 하나씩 처리하고, 좌석 0의 다른 선택에는 Default로 답한다
                long next = run.S.NextDueMs;
                if (next != long.MaxValue) run.S.Tick(next);
                else
                {
                    var c = run.Mine();
                    Assert.IsNotNull(c, "진행이 멈췄다");
                    Assert.IsTrue(run.S.Link.ToHost(0, new ClientMsg { Command = Command.AnswerTo(c.Id, c.Default.Clone()) }, run.S.AnimEndMs).Accepted);
                }
            }
            Assert.IsNotNull(gate, "운명 창이 열리지 않았다");
            long start = run.S.AnimEndMs;
            Assert.AreEqual(start + PlayTiming.HiddenReactionMinMs, run.S.NextDueMs);
            run.S.Tick(start + PlayTiming.HiddenReactionMinMs - 1);
            Assert.IsNotNull(run.S.ViewFor(0).Pending.Find(x => x.Id == gate.Id), "최소 시간 전");
            int at = run.Events.Count;
            run.S.Tick(start + PlayTiming.HiddenReactionMinMs);
            var news = run.Events.GetRange(at, run.Events.Count - at);
            var ans = news.Find(e => e is ChoiceAnswered x && x.Id == gate.Id) as ChoiceAnswered;
            Assert.IsNotNull(ans, "그 시각에 답했다");
            Assert.AreEqual(AnswerKind.Decline, ans.Answer.Kind);
            Assert.IsFalse(ans.ByTimeout, "사람이 안 쓴 것과 같은 모양(손패를 흘리지 않음)");
            Assert.IsFalse(ans.ByAi);
        }

        // ── PlayTiming ──

        [Test]
        public void M3_연출길이표는M3이벤트를모두분류()
        {
            int n = 0;
            foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(GameEvent).IsAssignableFrom(t)) continue;
                var e = (GameEvent)Activator.CreateInstance(t);
                Assert.AreNotEqual(PlayTiming.Unknown, PlayTiming.RowOf(e), $"{t.Name}의 04 §8-8 행이 없다");
                e.Seat = 0;
                var items = PlayTiming.Items(new List<GameEvent> { new ActionStarted { Seq = 0, Seat = 0, Kind = ActionKind.TurnStart }, e }, new PlayTimingContext(0));
                foreach (var it in items) Assert.GreaterOrEqual(it.QueueMs + it.BlockMs, 0, t.Name);
                n++;
            }
            Assert.Greater(n, 60, "이벤트 타입 수");
        }

        [Test]
        public void M3_방장멈춤은앞끝에잇기()
        {
            Assert.AreEqual(800, PlayTiming.NextPauseEnd(100, 500, 300), "앞 멈춤이 남았으면 그 끝에 잇는다");
            Assert.AreEqual(1200, PlayTiming.NextPauseEnd(900, 500, 300), "끝났으면 지금부터");

            // 셋업 갱신(2인): 좌석마다 거른 열의 길이를 세고 방장 멈춤 = 최댓값. 남의 카드 키가 가려져도 길이는 같다
            var s = Engine.NewGame(ctx, new RuleOptions(), 2, 5);
            var log = new EventLog();
            Engine.Start(s, ctx, log);
            var ctxs = new[] { new PlayTimingContext(0), new PlayTimingContext(1) };
            var per = new int[2];
            int max = PlayTiming.UpdateMs(log.Events, ctxs, per);
            Assert.Greater(max, 0);
            Assert.AreEqual(Math.Max(per[0], per[1]), max);
            Assert.AreEqual(per[0], per[1], "트레이너 카드 배분은 좌석마다 같은 길이");

            // 정비(38·40·41행)만 있는 갱신: 정비한 좌석의 화면은 0, 남 화면은 정비 판 보기 MaintMs — 방장 멈춤 = 최댓값(결정 120). 같은 턴의 다음 정비는 묶어서 0(04 §8-5 규칙 3)
            var none = new List<GameEvent>
            {
                new ActionStarted { Seq = 9, Seat = 0, Kind = ActionKind.PartyOrder, Auto = AutoKind.Human },
                new PartyReordered { Seq = 9, Seat = 0 },
                new ChoiceReissued { Seq = 9, Seat = 0 },
            };
            Assert.AreEqual(PlayTiming.MaintMs, PlayTiming.UpdateMs(none, ctxs, per));
            Assert.AreEqual((0, PlayTiming.MaintMs), (per[0], per[1]), "정비한 좌석 0 · 남 좌석 정비 판 보기");
            Assert.AreEqual(0, PlayTiming.UpdateMs(none, ctxs), "같은 턴의 다음 정비는 묶어서 한 번");
            Assert.Greater(PlayTiming.UpdateMs(new List<GameEvent> { new TurnStarted { Seq = 10, Seat = 1 } }, ctxs), 0);
            Assert.AreEqual(PlayTiming.MaintMs, PlayTiming.UpdateMs(none, ctxs), "다음 턴에는 다시");
        }

        [Test]
        public void M3_야생배틀항목은당사자좌석만()
        {
            // 좌석 0의 야생 배틀: 좌석 1은 BattleStarted ~ BattleEnded를 세지 않고, 차례 넘김(101행)부터 센다(04 §8-2·§8-5 규칙 5)
            var ev = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.EncounterPick },
                new EncounterPicked { Seq = 1, Seat = 0 },
                new BattleStarted { Seq = 1, Seat = 0, Kind = BattleKind.Wild, Sides = new[] { new BattleSideInfo { Owner = 0, Seat = 0 }, new BattleSideInfo { Owner = SeatId.Wild, Seat = SeatId.Wild } } },
                new SentIn { Seq = 1, Seat = 0 },
                new InitiativeTokenAt { Seq = 1, Seat = 0 },
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.BattleAction },
                new DiceRolled { Seq = 2, Seat = 0, Faces = new[] { AttackDieFace.RegularHit, AttackDieFace.CriticalHit } },
                new DamageBreakdown { Seq = 2, Seat = 0, Steps = new DamageStep[2] },
                new HpChanged { Seq = 2, Seat = 0 },
                new Fainted { Seq = 2, Seat = 0 },
                new BattleEnded { Seq = 2, Seat = 0 },
                new TurnEnded { Seq = 2, Seat = 0 },
                new ActionStarted { Seq = 3, Seat = SeatId.System, Kind = ActionKind.TurnStart },
                new TurnStarted { Seq = 3, Seat = 1 },
            };
            var mine = PlayTiming.Items(ev, new PlayTimingContext(0));
            var other = PlayTiming.Items(ev, new PlayTimingContext(1));
            Assert.AreEqual(3, mine.Count);
            Assert.AreEqual(2000, mine[0].BlockMs, "조우 전환은 당사자 차단 컷신(22행)");
            Assert.AreEqual(Math.Max(600 + 3500, PlainMs()), mine[0].QueueMs, "고른 뒤 600 + 인트로 3500(53행)");
            Assert.AreEqual(2510 + 1050 + 800 + 1800 + 3000, mine[1].QueueMs, "주사위 400+1600+250+130×2 + 계산 줄·숫자 비행 1050 + HP 800 + 기절 1800 + 결과 3000(보기 시간 4000보다 길다) — spikes/dice-roll §4");
            Assert.AreEqual(2, other.Count, "좌석 1: 조우 고르기(보드 위 공개)와 턴 시작만");
            Assert.AreEqual(0, other[0].BlockMs);
            Assert.AreEqual(Math.Max(600, PlainMs()), other[0].QueueMs, "배틀 인트로는 세지 않는다");
            Assert.AreEqual(1400 + 800 + 600 + 600, other[1].QueueMs, "배너 + 요약 + 넘김 띠 + 내 차례 컷신(좌석 1의 차례 — 결정 53 ① 순서 · 57 ② 길이)");
            Assert.AreEqual(1400 + 800 + 600, mine[2].QueueMs, "남의 차례에는 컷신이 없다");

            // 같은 배틀이 갤럭시단 배틀이면 모두 센다(가운데 필드가 모두 바뀐다)
            ((BattleStarted)ev[2]).Kind = BattleKind.Galactic;
            var all = PlayTiming.Items(ev, new PlayTimingContext(1));
            Assert.AreEqual(3, all.Count);
            Assert.AreEqual(0, all[0].BlockMs, "범위 밖 좌석은 차단하지 않고");
            Assert.AreEqual(600 + 2500 + 3500, all[0].QueueMs, "queue로 받는다(VS 2500)");

            // 이어하기: 열린 야생 배틀은 상태에서 다시 세운다
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "418:0");
            Scene.Give(s, ctx, 1, "399:0");
            Battles.Wild(s, ctx, null, 0, Scene.Spawn(s, ctx, "396:0", SeatId.Wild));
            var c1 = new PlayTimingContext(1);
            c1.From(s);
            Assert.IsTrue(c1.SkippingBattle);
            var c0 = new PlayTimingContext(0);
            c0.From(s);
            Assert.IsFalse(c0.SkippingBattle);
        }

        static int PlainMs() => PlayTiming.PlainMs;

        /// <summary>두 좌석을 사람으로 몬 판의 좌석별 기록: 받은 이벤트(글), 갱신마다 PlayTiming 길이, 멈춤 끝.</summary>
        sealed class Track
        {
            public readonly string[] Dump;
            public readonly string[] Lengths;
            public string AnimEnds = "";
            public int HandGatedHuman, HandGatedSession, WildSkipped, Galactic, Commands;

            public Track(int seats)
            {
                Dump = new string[seats];
                Lengths = new string[seats];
                for (int i = 0; i < seats; i++) Dump[i] = Lengths[i] = "";
            }
        }

        /// <summary>
        /// 좌석 0·1을 사람으로(답 = 시드 + 선택 Id로 고른 그 좌석 보기의 Legal — 숨은 정보와 무관) 몬다. 첫 MapAction은 갤럭시단 도전, 숨겨진 장소는 늘 지나가기(정체가 드러나지 않게).
        /// 숨은 손패 창은 Id가 짝수면 사람이 바로 「안 씀」(최소 시간까지 묶임), 홀수면 세션이 최소 시간에 「안 씀」. 시각 = 멈춤 끝(사람) / 다음 정해진 일(AI·창).
        /// </summary>
        Track Drive(GameState start, int answers)
        {
            int seats = start.SeatCount;
            var session = LocalSession.FromState(ctx, start, Cfg(), 0);
            var track = new Track(seats);
            var last = new GameUpdate[seats];
            var timing = new PlayTimingContext[seats];
            for (int i = 0; i < seats; i++)
            {
                int seat = i;
                timing[i] = new PlayTimingContext(i);
                session.Link.Connect(i, m =>
                {
                    if (!(m is GameUpdate u)) return;
                    if (last[seat] == null) timing[seat].From(u.View);
                    last[seat] = u;
                    bool skipping = timing[seat].SkippingBattle;
                    // 내가 뽑은 운명 카드는 숨은 덱 순서에서 온다 — 장수만 남긴 꼴로 견준다
                    var shown = new List<GameEvent>();
                    foreach (var ev in u.Events) shown.Add(ev is FateDealt ? Engine.FilterFor(ev, SeatId.Observer) : ev);
                    track.Dump[seat] += EventDump.One(shown) + "\n";
                    track.Lengths[seat] += PlayTiming.SeatMs(u.Events, timing[seat]) + ",";
                    if (seat == 0) track.AnimEnds += u.AnimEndMs + ",";
                    if (!skipping && timing[seat].SkippingBattle) track.WildSkipped++;
                    if (seat != 0) return;
                    foreach (var e in u.Events)
                        if (e is BattleStarted b && b.Kind == BattleKind.Galactic) track.Galactic++;
                        else if (e is ChoiceRequested r && r.HandGated)
                        {
                            if (r.Id % 2 == 0) track.HandGatedHuman++;
                            else track.HandGatedSession++;
                        }
                });
            }
            var done = new HashSet<int>();
            bool challenged = false;
            int human = 0;
            for (int guard = 0; guard < 5000; guard++)
            {
                long now = session.AnimEndMs;
                session.Tick(now);
                if (session.GameOver) break;
                PendingChoice mine = null;
                int who = -1;
                for (int i = 0; i < seats && mine == null; i++)
                {
                    mine = last[i].View.Pending.Find(c => c.Seat == i && !c.Answered && !done.Contains(c.Id) && (!c.HandGated || c.Id % 2 == 0));
                    who = i;
                }
                if (mine != null && human < answers)
                {
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(last[who].View, ctx, mine, legal);
                    var a = legal[new Pcg32(Salt, (ulong)mine.Id).Next(legal.Count)];
                    var galactic = legal.Find(x => x.Kind == AnswerKind.Challenge && x.Target == SeatId.Galactic);
                    if (mine.Kind == ChoiceKind.MapAction && !challenged && galactic != null)
                    {
                        a = galactic;
                        challenged = true;
                    }
                    if (mine.Kind == ChoiceKind.HiddenSpot) a = ChoiceAnswer.Of(AnswerKind.Decline);
                    Assert.IsTrue(session.Link.ToHost(who, new ClientMsg { Command = Command.AnswerTo(mine.Id, a) }, now).Accepted, $"{mine.Kind} {a}");
                    done.Add(mine.Id);
                    human++;
                    continue;
                }
                long next = session.NextDueMs;
                if (next == long.MaxValue) break;
                session.Tick(next);
            }
            track.Commands = session.CommandCount;
            return track;
        }

        [Test]
        public void M3_연출길이숨은정보불변()
        {
            // 02 §5-7: 숨은 정보만 다른 두 원본에 둘 다에서 합법인 같은 명령 열 → 좌석별 걸러진 이벤트 열과 연출 길이·방장 멈춤이 같다(01 §3-6 ⑩).
            // 숨은 것: 두 좌석의 운명 손패(장수는 같고 카드가 다름 → 운명 반응 창은 장수로 열린다, 01 §3-3 HandGated), 뒷면 숨겨진 장소 정체, 트레이너 카드·운명 덱 순서.
            var s = Flow.Started(ctx, 2, Seed);
            var x1 = s.Clone();
            var x2 = s.Clone();
            x2.Decks[(int)DeckId.ChangeOfFate].Reverse();
            x2.Decks[(int)DeckId.TrainerCard].Reverse();
            foreach (var x in new[] { x1, x2 })
            {
                foreach (var st in x.Seats) st.Stamps[StampRules.FateCards] = true; // 스탬프 7은 숨은 손패로 공개 사건을 만든다(rulings A30) — 미리 채운다
                var fate = x.Decks[(int)DeckId.ChangeOfFate];
                for (int seat = 0; seat < 2; seat++)
                    for (int k = 0; k <= seat; k++)
                    {
                        x.Seats[seat].FateHand.Add(fate[fate.Count - 1]);
                        fate.RemoveAt(fate.Count - 1);
                    }
            }
            int h1 = -1, h2 = -1;
            for (int i = 0; i < x2.Tokens.Count; i++)
            {
                var t = x2.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Cell < 0) continue;
                if (h1 < 0) h1 = i;
                else if (x2.Tokens[h1].Def != t.Def) { h2 = i; break; }
            }
            Assert.GreaterOrEqual(h2, 0, "정체가 다른 뒷면 숨겨진 장소 두 개");
            int tmp = x2.Tokens[h1].Def;
            x2.Tokens[h1].Def = x2.Tokens[h2].Def;
            x2.Tokens[h2].Def = tmp;
            CollectionAssert.AreNotEqual(x1.Seats[0].FateHand, x2.Seats[0].FateHand, "운명 카드가 다르다");
            CollectionAssert.AreNotEqual(x1.Seats[1].FateHand, x2.Seats[1].FateHand);
            Assert.AreNotEqual(StateHash.Of(x1), StateHash.Of(x2), "원본은 다르다");
            Assert.AreEqual(StateHash.Of(Engine.ViewFor(x1, SeatId.Observer)), StateHash.Of(Engine.ViewFor(x2, SeatId.Observer)), "공개 정보는 같다");

            const int answers = 80;
            // 운명 네 장의 효과(A24)가 있으면 자기 손패에 따라 자기 합법 답이 달라진다(낼 카드가 있으면 세션이 「안 씀」을 대신 내지 않는다) — 이 시험은 남의 숨은 정보만 보므로 처리기 없는 표로 돈다
            var full = ctx;
            ctx = new Ctx(full.Board, full.Cards, full.Decks, full.Fixtures, full.Items, new EffectDb(new List<EffectDef>(full.Effects.DataEffects()), full.Cards.Moves.Count, FateTests.NoFate()));
            Track a, b;
            try
            {
                a = Drive(x1, answers);
                b = Drive(x2, answers);
            }
            finally
            {
                ctx = full;
            }
            TestContext.WriteLine($"명령 {a.Commands}, 갤럭시단 배틀 {a.Galactic}, 숨은 손패 창 사람 {a.HandGatedHuman}·세션 {a.HandGatedSession}, 야생전 관전 {a.WildSkipped}, 갱신 {a.AnimEnds.Split(',').Length - 1}");
            Assert.AreEqual(a.Commands, b.Commands, "명령 수");
            Assert.Greater(a.Commands, answers, "AI 답도 끼었다");
            Assert.Greater(a.Galactic, 0, "갤럭시단 배틀(운명 창이 열리는 배틀)");
            Assert.Greater(a.HandGatedHuman, 0, "숨은 손패 창에 사람이 바로 답했다(최소 시간까지 묶임)");
            Assert.Greater(a.HandGatedSession, 0, "숨은 손패 창에 세션이 최소 시간에 「안 씀」으로 답했다");
            Assert.Greater(a.WildSkipped, 0, "야생전 관전 좌석도 있었다");
            for (int seat = 0; seat < 2; seat++)
            {
                Assert.AreEqual(a.Dump[seat], b.Dump[seat], $"좌석 {seat}에 간 이벤트가 숨은 정보로 달라졌다");
                Assert.AreEqual(a.Lengths[seat], b.Lengths[seat], $"좌석 {seat}의 연출 길이가 숨은 정보로 달라졌다");
            }
            Assert.AreEqual(a.AnimEnds, b.AnimEnds, "방장 멈춤 끝");
        }
    }
}
