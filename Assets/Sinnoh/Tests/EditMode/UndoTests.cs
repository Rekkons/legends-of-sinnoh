using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using EventLog = Sinnoh.Rules.EventLog; // Unity의 System.Diagnostics에도 EventLog가 있다

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M10 무르기 · 되돌리기(결정 149 · 154, Docs/Design/undo.md §5 「EditMode — 규칙」): 운 표시(UndoRules.Probe · Revealed)와 그 반증,
    /// 판정(StepTarget · Points), 실행(LocalSession.Undo · Rewind — 다시 적용 + 로그 자르기), 갱신에 싣기.
    /// 판은 모든 좌석이 상태 없는 AI(선택 Id로 정한 Legal 하나)라 되돌린 뒤 같은 답 = 같은 명령 열이 된다.
    /// </summary>
    public class UndoTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        /// <summary>상태 없는 좌석 AI: 예산 시드(= AI 시드 × 상수 + 선택 Id)로 Legal 하나를 고른다 — 되돌린 뒤 같은 선택 Id면 같은 답.</summary>
        sealed class PickAgent : IAgent
        {
            readonly List<ChoiceAnswer> legal = new List<ChoiceAnswer>();

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                Engine.Legal(input.View, input.Ctx, input.Choice, legal);
                return new AgentDecision(legal[new Pcg32(budget.Seed, 0x51).Next(legal.Count)].Clone(), null);
            }
        }

        /// <summary>메모리 세이브: 줄 · 스냅샷 · 머리 · 쓰기 순서(Ops)를 든다.</summary>
        sealed class MemStore : ISaveStore
        {
            public readonly List<SaveLogLine> Lines = new List<SaveLogLine>();
            public readonly List<SaveSnapshot> Snaps = new List<SaveSnapshot>();
            public readonly List<string> Ops = new List<string>();
            public SaveHead Head;
            public bool FailTruncate;

            public void WriteHead(SaveHead head)
            {
                Head = head;
                Ops.Add("head");
            }

            public void WriteSnapshot(SaveSnapshot snapshot)
            {
                Snaps.Add(snapshot);
                Ops.Add("snap:" + snapshot.CommandCount);
            }

            public void AppendCommand(SaveLogLine line) => Lines.Add(line);

            public void TruncateLog(int commandCount)
            {
                if (FailTruncate) throw new IOException("시험: 로그 자르기 실패");
                Lines.RemoveAll(l => l.Seq >= commandCount);
                Ops.Add("trunc:" + commandCount);
            }
        }

        /// <summary>시험이 손으로 답을 돌려주는 러너(RoomRunnerTests와 같은 꼴).</summary>
        sealed class FakeRunner : IAgentRunner
        {
            public readonly List<AgentRequest> Asked = new List<AgentRequest>();
            public readonly Queue<AgentReply> Ready = new Queue<AgentReply>();

            public void Post(AgentRequest request) => Asked.Add(request);

            public bool TryTake(out AgentReply reply)
            {
                reply = Ready.Count > 0 ? Ready.Dequeue() : null;
                return reply != null;
            }

            public AgentRequest Last => Asked[Asked.Count - 1];

            /// <summary>좌석 선택에 물을 때마다 다른 Legal을 고른다(시간 마감으로 끝난 강한 AI처럼 — 같은 입력에도 답이 달라진다). 가상 좌석은 그대로.</summary>
            public bool Vary;
            int calls;
            readonly List<ChoiceAnswer> legal = new List<ChoiceAnswer>();

            public void Answer(AgentRequest q)
            {
                AgentDecision d;
                if (Vary && q.Input.Choice.Seat >= 0)
                {
                    Engine.Legal(q.Input.View, q.Input.Ctx, q.Input.Choice, legal);
                    d = new AgentDecision(legal[calls++ % legal.Count].Clone(), null);
                }
                else d = q.Agent.Decide(q.Input, q.Budget);
                Ready.Enqueue(new AgentReply { Serial = q.Serial, ChoiceId = q.ChoiceId, Decision = d });
            }
        }

        /// <summary>
        /// 봇 판 하나: 좌석 갱신을 받아 두고, 받아들인 명령마다 CommandCount → 해시를 적는다 — 같은 자리에 다시 오면(되돌린 뒤) 해시가 같아야 한다.
        /// 좌석 0이 받은 이벤트도 (Seq · Index)마다 글로 적어 두고 다시 받으면 같아야 한다(같은 명령 = 같은 결과 · 같은 주사위).
        /// </summary>
        sealed class Table
        {
            public readonly LocalSession S;
            public readonly GameUpdate[] Last;
            public readonly List<RewindNote>[] Notes;
            public readonly Dictionary<int, ulong> Hashes = new Dictionary<int, ulong>();
            readonly Dictionary<string, string> seen = new Dictionary<string, string>();
            public int Replayed, ReplayedDice;
            public long Now;
            public readonly FakeRunner Runner;

            public Table(Ctx ctx, int seats, ulong seed, UndoMode mode, ISaveStore store = null, bool runner = false, Func<int, Command, bool> allow = null, SaveRecord resume = null, Campaign campaign = Campaign.HeartOfSinnoh)
            {
                var agents = new IAgent[seats];
                for (int i = 0; i < seats; i++) agents[i] = new PickAgent();
                if (runner) Runner = new FakeRunner();
                var cfg = new SessionConfig
                {
                    VirtualAgent = new PrincipleAgent(), SeatAgents = agents, Store = store, Runner = Runner, Undo = mode, Allow = allow, SaveId = "m10-undo", ContentHash = "test-content",
                };
                cfg.Applied = _ => Record();
                S = resume != null ? LocalSession.Resume(ctx, resume, cfg, 0) : LocalSession.Start(ctx, new RuleOptions(), seats, seed, campaign, cfg, 0);
                Record();
                Last = new GameUpdate[seats];
                Notes = new List<RewindNote>[seats];
                for (int i = 0; i < seats; i++)
                {
                    int seat = i;
                    Notes[i] = new List<RewindNote>();
                    S.Link.Connect(i, m =>
                    {
                        if (!(m is GameUpdate u)) return;
                        Last[seat] = u;
                        if (u.Rewind != null) Notes[seat].Add(u.Rewind);
                        if (seat == 0) See(u.Events);
                    });
                }
            }

            void Record()
            {
                if (S == null) return;
                int c = S.CommandCount;
                ulong h = S.Hash;
                if (Hashes.TryGetValue(c, out var old)) Assert.AreEqual(old, h, $"CommandCount {c}에 다시 왔는데 상태가 다르다");
                else Hashes[c] = h;
            }

            void See(List<GameEvent> events)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    string key = S.CommandCount + ":" + i, text = EventDump.One(e); // 받아들인 명령 자리 + 그 갱신 안 순서(Seq는 한 행동의 여러 명령이 같이 쓴다)
                    if (seen.TryGetValue(key, out var old))
                    {
                        Assert.AreEqual(old, text, $"되돌린 뒤 같은 자리(Seq {e.Seq})의 이벤트가 다르다");
                        Replayed++;
                        if (e is DiceRolled) ReplayedDice++;
                    }
                    else seen[key] = text;
                }
            }

            public int Active => S.HostCopy().ActiveSeat;

            public UndoOffer OfferActive()
            {
                int a = Active;
                return a >= 0 ? S.OfferFor(a) : null;
            }

            /// <summary>정해진 일 하나만큼 시각을 옮긴다(러너가 있으면 물음에 바로 답한다).</summary>
            public void Step()
            {
                if (Runner != null && S.AwaitingAi) Runner.Answer(Runner.Last);
                long t = S.NextDueMs;
                Assert.AreNotEqual(long.MaxValue, t, "봇 판이 멈췄다");
                Now = Math.Max(Now, t);
                S.Tick(Now);
            }

            public void Run(int commands)
            {
                for (int guard = 0; S.CommandCount < commands && !S.GameOver; guard++)
                {
                    Assert.Less(guard, 200000);
                    Step();
                }
            }

            public bool Undo(int seat, int to) => S.Undo(seat, to, Now);
        }

        // ── 운 표시(§1-2) ──

        GameState Fresh(int seats = 2, ulong seed = 3)
        {
            var s = Engine.NewGame(ctx, new RuleOptions(), seats, seed);
            Engine.Start(s, ctx, null);
            // 뒷면 숨겨진 장소가 없을 수 있어 하나 둔다(색인은 덧붙이기만 — 판정이 색인으로 본다)
            s.Tokens.Add(new TokenState { Kind = TokenKind.HiddenSpot, Def = 1, Cell = 0 });
            s.Seats[0].FateHand.AddRange(new[] { 4, 5 });
            s.Seats[1].FateHand.AddRange(new[] { 6, 7 });
            return s;
        }

        static TokenState FaceDownSpot(GameState s) => s.Tokens.FindLast(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed);

        /// <summary>엔진 직접 봇 판(Playout)의 명령마다: 사본에 적용해 운 표시를 세고 넘긴다(each(전 상태, 좌석, 명령, 뒤 상태, 이벤트, 운)). 원본은 고치지 않는다.</summary>
        void Audit(int seats, ulong seed, int maxRounds, Action<GameState, int, Command, GameState, List<GameEvent>, bool> each, Campaign campaign = Campaign.HeartOfSinnoh)
        {
            Playout.Run(ctx, seats, seed, campaign: campaign, maxRounds: maxRounds, commands: (s, seat, cmd) =>
            {
                var probe = UndoRules.Probe(s);
                var after = s.Clone();
                var log = new EventLog();
                Assert.IsTrue(Engine.Submit(after, ctx, seat, cmd, log).Accepted);
                each(s, seat, cmd, after, log.Events, UndoRules.Revealed(probe, after, seat, log.Events));
            });
        }

        [Test]
        public void M10_무르기_운표시_주사위굴림은운이다()
        {
            var s = Fresh();
            var p = UndoRules.Probe(s);
            Assert.IsFalse(UndoRules.Revealed(p, s.Clone(), 0, null), "아무것도 안 바뀌면 운이 아니다");
            var rolled = s.Clone();
            rolled.Rng.NextUInt();
            Assert.IsTrue(UndoRules.Revealed(p, rolled, 0, null), "Rng가 움직였다 = 주사위 · 동전 · 섞기");
            int dice = 0;
            Audit(2, 5, 8, (before, seat, cmd, after, events, luck) =>
            {
                if (!events.Exists(e => e is DiceRolled || e is CoinFlip || e is PileShuffled)) return;
                dice++;
                Assert.IsTrue(luck, $"굴림 · 섞기가 있는 명령({cmd.Kind})이 운이 아니다");
            });
            Assert.Greater(dice, 0, "봇 판에 굴림이 없었다");
        }

        [Test]
        public void M10_무르기_운표시_카드뽑기와숨은장소공개는운이다()
        {
            var s = Fresh();
            var p = UndoRules.Probe(s);
            int deck = Array.FindIndex(s.Decks, d => d.Count > 0);
            var drawn = s.Clone();
            drawn.Decks[deck].RemoveAt(drawn.Decks[deck].Count - 1);
            Assert.IsTrue(UndoRules.Revealed(p, drawn, 0, null), "덱에서 빠짐");
            var flipped = s.Clone();
            FaceDownSpot(flipped).FaceUp = true;
            Assert.IsTrue(UndoRules.Revealed(p, flipped, 0, null), "뒷면 숨겨진 장소가 앞면");
            var swapped = s.Clone();
            FaceDownSpot(swapped).Def += 1;
            Assert.IsTrue(UndoRules.Revealed(p, swapped, 0, null), "뒷면 숨겨진 장소의 정체가 바뀜");
            var taken = s.Clone();
            FaceDownSpot(taken).Cell = -1;
            Assert.IsTrue(UndoRules.Revealed(p, taken, 0, null), "뒷면 숨겨진 장소가 없어짐");
            int draws = 0;
            Audit(2, 6, 8, (before, seat, cmd, after, events, luck) =>
            {
                if (!events.Exists(e => e is EncounterDrawn || e is HiddenSpotRevealed || e is FateDealt)) return;
                draws++;
                Assert.IsTrue(luck, $"뽑기 · 공개가 있는 명령({cmd.Kind})이 운이 아니다");
            });
            Assert.Greater(draws, 0, "봇 판에 뽑기가 없었다");
        }

        [Test]
        public void M10_무르기_운표시_덱에넣기만한명령은운이아니다()
        {
            var s = Fresh();
            var p = UndoRules.Probe(s);
            int deck = Array.FindIndex(s.Decks, d => d.Count > 0);
            var put = s.Clone();
            put.Decks[deck].Insert(0, put.Decks[deck][0]); // 같은 카드를 더 넣어도(다중집합) 빠진 것이 없다
            put.Decks[deck].Reverse();                      // 순서만 바뀌어도(섞기는 Rng로 잡는다) 빠진 것이 없다
            Assert.IsFalse(UndoRules.Revealed(p, put, 0, null), "넣기만 했다");
            var mine = s.Clone();
            mine.Seats[0].FateHand.Clear();
            Assert.IsFalse(UndoRules.Revealed(p, mine, 0, null), "내 손패에서 빠진 것은 나에게 새 정보가 아니다");
            var gift = s.Clone();
            gift.Seats[1].FateHand.Add(9);
            Assert.IsFalse(UndoRules.Revealed(p, gift, 0, null), "남의 손패에 들어가기만 했다");
        }

        [Test]
        public void M10_무르기_운표시_남의비밀공개는운이다()
        {
            var s = Fresh();
            var p = UndoRules.Probe(s);
            var played = s.Clone();
            played.Seats[1].FateHand.Remove(6);
            Assert.IsTrue(UndoRules.Revealed(p, played, 0, null), "남의 운명 손패에서 빠짐");
            Assert.IsFalse(UndoRules.Revealed(p, played, 1, null), "낸 좌석 자신의 손패");
            Assert.IsTrue(UndoRules.Revealed(p, played, SeatId.Galactic, null), "가상 좌석이 낸 명령이면 모든 좌석이 남이다");
            var offer = s.Clone();
            offer.Seats[1].TrainerCardOffer.Add(3);
            var po = UndoRules.Probe(offer);
            var picked = offer.Clone();
            picked.Seats[1].TrainerCardOffer.Clear();
            Assert.IsTrue(UndoRules.Revealed(po, picked, 0, null), "남의 트레이너 카드 후보에서 빠짐");
            foreach (var e in new GameEvent[] { new SecretRevealed(), new TrainerCardsRevealed(), new FollowerTally() })
                Assert.IsTrue(UndoRules.Revealed(p, s.Clone(), 0, new List<GameEvent> { e }), e.GetType().Name);
            Assert.IsFalse(UndoRules.Revealed(p, s.Clone(), 0, new List<GameEvent> { new Moved() }));
        }

        /// <summary>숨은 몫만 흔든 사본: 덱마다 섞기(은행상회 맨 위 공개 카드는 그대로) · Rng 새 시드 · 뒷면 숨겨진 장소 정체 서로 바꾸기(금색 ? 끼리 · 보통끼리).</summary>
        static GameState Shake(GameState s, Pcg32 rng)
        {
            var x = s.Clone();
            x.Rng.CopyFrom(new Pcg32(((ulong)rng.NextUInt() << 32) | rng.NextUInt(), rng.NextUInt()));
            for (int d = 0; d < x.Decks.Length; d++)
            {
                var pile = x.Decks[d];
                int n = d == (int)DeckId.GinkgoTrade ? pile.Count - 1 : pile.Count;
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (pile[i], pile[j]) = (pile[j], pile[i]);
                }
            }
            foreach (bool special in new[] { false, true })
            {
                var spots = x.Tokens.FindAll(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Special == special);
                for (int i = spots.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (spots[i].Def, spots[j].Def) = (spots[j].Def, spots[i].Def);
                }
            }
            return x;
        }

        static void SameViews(GameState a, GameState b, string where)
        {
            for (int v = -1; v < a.SeatCount; v++)
            {
                int viewer = v < 0 ? SeatId.Observer : v;
                GameState va = Engine.ViewFor(a, viewer), vb = Engine.ViewFor(b, viewer);
                if (StateHash.Of(va) != StateHash.Of(vb)) Assert.Fail($"{where}: 보기 {viewer}가 다르다 — {FirstDiff(SaveStore.Serialize(va, true), SaveStore.Serialize(vb, true))}");
            }
        }

        /// <summary>두 글의 처음 다른 줄(앞 줄 몇 개와 함께).</summary>
        static string FirstDiff(string a, string b)
        {
            string[] x = a.Split('\n'), y = b.Split('\n');
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i])
                    return $"줄 {i}: [{string.Join(" | ", x, Math.Max(0, i - 16), Math.Min(16, i)).Trim()}] {x[i].Trim()} ≠ {y[i].Trim()}";
            return $"길이 {x.Length} ≠ {y.Length}";
        }

        [Test, Category("Slow")]
        public void M10_무르기_운표시없는명령은숨은정보와무관하다()
        {
            // 반증(§1-2): 운이 아니라고 본 줄마다, 전 상태의 숨은 몫을 흔든 사본에도 같은 명령이 들어가고 모든 좌석 · 관전자의 보기와 받는 이벤트가 같다.
            // 걸리면 Revealed에 빠진 길이 있다(그 줄을 운으로 쳐야 한다).
            var rng = new Pcg32(154, 149);
            int plain = 0, lucky = 0;
            void Check(GameState before, int seat, Command cmd, GameState after, List<GameEvent> events, bool luck)
            {
                if (luck)
                {
                    lucky++;
                    return;
                }
                plain++;
                var shaken = Shake(before, rng);
                SameViews(before, shaken, "흔든 사본(적용 전)");
                var log = new EventLog();
                var r = Engine.Submit(shaken, ctx, seat, cmd, log);
                Assert.IsTrue(r.Accepted, $"운 아닌 명령({cmd.Kind} 좌석 {seat})이 숨은 몫을 흔든 판에서 거절됐다: {r}");
                SameViews(after, shaken, $"운 아닌 명령({cmd.Kind} 좌석 {seat}, Seq {before.CommandCount}) 뒤");
                for (int v = -1; v < before.SeatCount; v++)
                {
                    int viewer = v < 0 ? SeatId.Observer : v;
                    Assert.AreEqual(EventDump.Of(events, viewer), EventDump.Of(log.Events, viewer), $"운 아닌 명령({cmd.Kind}) 이벤트가 보기 {viewer}에게 다르다");
                }
            }
            for (int seats = 1; seats <= 4; seats++) Audit(seats, (ulong)(40 + seats), 40, Check);
            Audit(2, 47, 30, Check, Campaign.UltraBurst);
            TestContext.Progress.WriteLine($"M10 반증: 운 아닌 줄 {plain} · 운 {lucky}");
            Assert.Greater(plain, 200);
            Assert.Greater(lucky, 50);
        }

        // ── 판정(§1-4) ──

        static UndoLine L(int seat, bool rest, bool luck = false, bool turnStarted = false, int turnId = 1) =>
            new UndoLine { Line = new SaveLogLine { Seat = seat }, RestAfter = rest, Luck = luck, TurnStarted = turnStarted, TurnId = turnId, Phase = Phase.Turn };

        [Test]
        public void M10_무르기_내턴_남의명령을넘지못한다()
        {
            // 좌석 0의 턴: [0] 턴 시작(쉬는 때) · [1] 좌석 0 걷기 · [2] 좌석 1의 남의 턴 제안(Propose) · [3] 좌석 0 걷기 — 바닥 0(쉬는 때)
            var book = new List<UndoLine> { L(1, true, turnStarted: true), L(0, true), L(1, true), L(0, true) };
            var now = new GameState { Phase = Phase.Turn, ActiveSeat = 0, CommandCount = 4, TurnId = 1 };
            Assert.AreEqual(3, UndoRules.StepTarget(book, 0, true, now, 0, UndoMode.Turn, out var why), "바로 앞 줄은 내 것");
            Assert.AreEqual(UndoBlock.None, why);
            now.CommandCount = 3;
            Assert.AreEqual(-1, UndoRules.StepTarget(book.GetRange(0, 3), 0, true, now, 0, UndoMode.Turn, out why), "남의 제안이 낀 앞으로는 못 무른다");
            Assert.AreEqual(UndoBlock.Others, why);
            Assert.AreEqual(2, UndoRules.StepTarget(book.GetRange(0, 3), 0, true, now, 0, UndoMode.Free, out why), "자유는 넘는다");
            Assert.AreEqual(UndoBlock.None, why);
            // 시스템(Timeout · HostOp)과 AI가 조종하는 가상 좌석(갤럭시단 답 — HumanVirtual 아님)은 넘는다. 사람 조종자의 답은 아래 시험
            foreach (int other in new[] { SeatId.System, SeatId.Galactic })
            {
                var b = new List<UndoLine> { L(1, true, turnStarted: true), L(0, false), L(other, true) };
                Assert.AreEqual(1, UndoRules.StepTarget(b, 0, true, now, 0, UndoMode.Turn, out why), $"좌석 {other}");
                Assert.AreEqual(UndoBlock.None, why);
            }
            // 운이 낀 줄: 내 턴 무르기는 막고 자유는 넘는다
            var lucky = new List<UndoLine> { L(1, true, turnStarted: true), L(0, false, luck: true), L(0, true) };
            Assert.AreEqual(-1, UndoRules.StepTarget(lucky, 0, true, now, 0, UndoMode.Turn, out why));
            Assert.AreEqual(UndoBlock.Luck, why);
            Assert.AreEqual(1, UndoRules.StepTarget(lucky, 0, true, now, 0, UndoMode.Free, out why));
            // 턴 처음 · 남의 턴 · 끔 · 셋업 · 바닥
            now.CommandCount = 1;
            Assert.AreEqual(-1, UndoRules.StepTarget(book.GetRange(0, 1), 0, true, now, 0, UndoMode.Free, out why));
            Assert.AreEqual(UndoBlock.TurnStart, why);
            Assert.AreEqual(-1, UndoRules.StepTarget(book.GetRange(0, 1), 0, true, now, 1, UndoMode.Free, out why));
            Assert.AreEqual(UndoBlock.NotYourTurn, why);
            Assert.AreEqual(-1, UndoRules.StepTarget(book.GetRange(0, 1), 0, true, now, 0, UndoMode.Off, out why));
            Assert.AreEqual(UndoBlock.Off, why);
            var floor = new GameState { Phase = Phase.Turn, ActiveSeat = 0, CommandCount = 11, TurnId = 1 };
            Assert.AreEqual(-1, UndoRules.StepTarget(new List<UndoLine> { L(0, false) }, 10, false, floor, 0, UndoMode.Free, out why), "바닥이 쉬는 때가 아니고 그 뒤에도 없다");
            Assert.AreEqual(UndoBlock.Floor, why);
            Assert.AreEqual(10, UndoRules.StepTarget(new List<UndoLine> { L(0, true) }, 10, true, floor, 0, UndoMode.Turn, out why), "턴 시작이 바닥 앞이면 바닥까지");
        }

        [Test]
        public void M10_무르기_내턴_사람이조종한갤럭시단답은남의것이다()
        {
            // 결정 154 ③(검토 반영): 방에서 갤럭시단을 사람이 조종하면 그 사람이 낸 가상 좌석 답은 「남이 한 일」 — 내 턴 무르기는 그 앞으로 못 간다(자유는 넘는다). AI 답은 그대로 넘는다
            var s = Fresh();
            var probe = UndoRules.Probe(s);
            var human = UndoRules.LineOf(new SaveLogLine { Seq = 2, Seat = SeatId.Galactic, Command = Command.AnswerTo(1, ChoiceAnswer.Of(AnswerKind.Decline)) }, probe, s, null, 0);
            var ai = UndoRules.LineOf(new SaveLogLine { Seq = 2, Seat = SeatId.Galactic, Command = Command.AiAnswer(1, ChoiceAnswer.Of(AnswerKind.Decline), default) }, probe, s, null, 0);
            Assert.IsTrue(human.HumanVirtual, "사람 조종자의 답(ByAi 아님)");
            Assert.IsFalse(ai.HumanVirtual);
            Assert.IsFalse(human.Replayable, "사람 답은 되돌린 뒤 다시 내지 않는다");
            Assert.IsTrue(ai.Replayable);
            var now = new GameState { Phase = Phase.Turn, ActiveSeat = 0, CommandCount = 3, TurnId = 1 };
            foreach (var (line, blocked) in new[] { (human, true), (ai, false) })
            {
                line.RestAfter = true;
                var b = new List<UndoLine> { L(1, true, turnStarted: true), L(0, false), line };
                Assert.AreEqual(blocked ? -1 : 1, UndoRules.StepTarget(b, 0, true, now, 0, UndoMode.Turn, out var why), blocked ? "사람 조종자" : "AI");
                Assert.AreEqual(blocked ? UndoBlock.Others : UndoBlock.None, why);
                Assert.AreEqual(1, UndoRules.StepTarget(b, 0, true, now, 0, UndoMode.Free, out why), "자유는 넘는다");
                Assert.AreEqual(UndoBlock.None, why);
            }
        }

        [Test]
        public void M10_무르기_사람조종자가AI답표지를실은가상좌석답은BadCommand()
        {
            // 사람 조종자가 가상 좌석 답에 ByAi를 실어 보내 「남이 한 일」 막음(HumanVirtual)을 피하지 못한다 — 표지 없는 답은 그 검사를 지나 다음 검사로 간다
            var session = LocalSession.Start(ctx, new RuleOptions(), 1, 5, Campaign.HeartOfSinnoh, new SessionConfig { VirtualAgent = new PrincipleAgent(), HumanVirtual = v => v == SeatId.Galactic }, 0);
            var ai = session.Submit(SeatId.Galactic, Command.AiAnswer(0, ChoiceAnswer.Of(AnswerKind.Decline), default), 0);
            Assert.IsFalse(ai.Accepted);
            Assert.AreEqual(RejectReason.BadCommand, ai.Reason);
            var human = session.Submit(SeatId.Galactic, Command.AnswerTo(0, ChoiceAnswer.Of(AnswerKind.Decline)), 0);
            Assert.IsFalse(human.Accepted, "열린 갤럭시단 선택이 없는 판");
            Assert.AreNotEqual(RejectReason.BadCommand, human.Reason, "표지 없는 사람 답은 BadCommand가 아니다");
            Assert.AreEqual(RejectReason.BadCommand, session.Submit(SeatId.Wild, Command.AnswerTo(0, ChoiceAnswer.Of(AnswerKind.Decline)), 0).Reason, "사람이 조종하지 않는 가상 좌석");
        }

        [Test]
        public void M10_무르기_UB건너뛰기투표중에는끝낸턴을못무른다()
        {
            // 라운드 끝 UB 「건너뛸까」 투표(UbBlessing.AskSkip)는 다음 턴을 시작하지 않아 ActiveSeat가 턴을 끝낸 좌석에 남는다 — 그래도 끝낸 턴은 무르지 못한다(자유도)
            var turn = new Table(ctx, 2, 34, UndoMode.Turn, runner: true, campaign: Campaign.UltraBurst);
            var free = new Table(ctx, 2, 34, UndoMode.Free, runner: true, campaign: Campaign.UltraBurst);
            int votes = 0;
            while (turn.S.CommandCount < 1500 && !turn.S.GameOver && votes < 6)
            {
                turn.Step();
                free.Step();
                Assert.AreEqual(turn.S.Hash, free.S.Hash);
                var h = free.S.HostCopy();
                if (!h.Pending.Exists(c => !c.Answered && c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip)) continue;
                votes++;
                Assert.GreaterOrEqual(h.ActiveSeat, 0);
                foreach (var t in new[] { turn, free })
                {
                    var o = t.S.OfferFor(h.ActiveSeat);
                    Assert.AreEqual(UndoBlock.NotYourTurn, o.Block, $"{t.S.UndoMode}: 투표 중 턴을 끝낸 좌석 {h.ActiveSeat}");
                    Assert.AreEqual(-1, o.StepTo);
                    Assert.IsFalse(t.Undo(h.ActiveSeat, t.S.CommandCount - 1));
                }
            }
            Assert.Greater(votes, 0, "라운드 끝 UB 건너뛰기 투표가 열리지 않았다");
            Assert.AreEqual(0, free.S.Epoch);
        }

        [Test]
        public void M10_무르기_되돌린뒤AI답은다시묻지않고그대로낸다()
        {
            // 검토 반영(undo.md §1-6): 시간 마감으로 끝난 강한 AI는 같은 입력에도 답이 달라질 수 있다 — 러너가 물을 때마다 다른 답을 내도,
            // 되돌린 뒤 같은 자리 · 같은 상태로 오면 버린 갈래의 AI 답을 그대로 내서 같은 판이 된다(Record가 자리마다 해시를 맞대 본다)
            var t = new Table(ctx, 2, 33, UndoMode.Free, runner: true);
            t.Runner.Vary = true;
            t.Run(250);
            int end = t.S.CommandCount, asked = t.Runner.Asked.Count;
            var points = t.S.UndoPoints();
            var p = points[points.Count / 2];
            Assert.IsTrue(t.S.Rewind(p.To, 0, t.Now));
            int replayedBefore = t.Replayed;
            t.Run(end);
            Assert.AreEqual(end, t.S.CommandCount);
            for (int i = asked; i < t.Runner.Asked.Count; i++)
                Assert.GreaterOrEqual(t.Runner.Asked[i].Input.View.CommandCount, end, "버린 갈래 안의 AI 차례는 러너에 다시 묻지 않는다");
            Assert.Greater(t.Replayed, replayedBefore, "되돌린 뒤 같은 자리의 이벤트를 다시 받았다(같은 글)");
            // 다시 이어 가면 새 자리부터는 여느 때처럼 묻는다
            t.Run(end + 30);
            Assert.Greater(t.Runner.Asked.Count, asked);
        }

        [Test]
        public void M10_무르기_내턴_셋업에는없다()
        {
            var t = new Table(ctx, 2, 11, UndoMode.Free);
            int checks = 0;
            while (t.S.HostCopy().Phase == Phase.Setup)
            {
                for (int i = 0; i < 2; i++)
                {
                    Assert.AreEqual(UndoBlock.Setup, t.Last[i].Undo.Block, "셋업 고르기는 비밀 동시 선택");
                    Assert.AreEqual(-1, t.Last[i].Undo.StepTo);
                    Assert.IsFalse(t.Undo(i, t.S.CommandCount - 1));
                }
                checks++;
                t.Step();
            }
            Assert.Greater(checks, 1);
        }

        [Test]
        public void M10_무르기_내턴_남의턴에는못무른다()
        {
            var t = new Table(ctx, 2, 12, UndoMode.Free);
            int checks = 0;
            while (t.S.CommandCount < 250 && !t.S.GameOver)
            {
                t.Step();
                var h = t.S.HostCopy();
                if (h.Phase == Phase.Setup || h.ActiveSeat < 0) continue;
                int other = 1 - h.ActiveSeat;
                var o = t.S.OfferFor(other);
                Assert.AreEqual(UndoBlock.NotYourTurn, o.Block);
                Assert.AreEqual(-1, o.StepTo);
                Assert.AreEqual(UndoBlock.NotYourTurn, t.Last[other].Undo.Block, "갱신에도 실린다");
                var mine = t.S.OfferFor(h.ActiveSeat);
                if (mine.StepTo >= 0) Assert.IsFalse(t.Undo(other, mine.StepTo), "차례 좌석의 목표로도 남은 못 무른다");
                checks++;
            }
            Assert.Greater(checks, 50);
        }

        // ── 실행(§1-5) ──

        [Test]
        public void M10_무르기_내턴_한수씩되돌리고같은명령은같은결과()
        {
            const int n = 400;
            var reference = new Table(ctx, 2, 21, UndoMode.Turn);
            reference.Run(n);
            var t = new Table(ctx, 2, 21, UndoMode.Turn);
            int undos = 0, last = 0;
            while (t.S.CommandCount < n && !t.S.GameOver)
            {
                t.Step();
                var o = t.OfferActive();
                if (o == null || o.StepTo < 0 || t.S.CommandCount <= last || undos >= 12) continue;
                Assert.AreEqual(UndoBlock.None, o.Block);
                int seat = t.Active;
                Assert.IsFalse(t.Undo(seat, o.StepTo - 1), "낡은 목표는 받지 않는다");
                last = t.S.CommandCount;
                Assert.IsTrue(t.Undo(seat, o.StepTo));
                undos++;
                Assert.AreEqual(o.StepTo, t.S.CommandCount);
                Assert.IsTrue(SaveRest.IsRest(t.S.HostCopy()), "되돌린 자리는 쉬는 때");
                Assert.AreEqual(reference.Hashes[o.StepTo], t.S.Hash, "그때의 호스트 원본과 같다(Rng 포함)");
                Assert.AreEqual(undos, t.S.Epoch);
                for (int i = 0; i < 2; i++)
                {
                    var note = t.Notes[i][t.Notes[i].Count - 1];
                    Assert.AreEqual(RewindKind.Step, note.Kind);
                    Assert.AreEqual(seat, note.By);
                    Assert.AreEqual(o.StepTo, note.To);
                    Assert.AreEqual(undos, note.Epoch);
                    Assert.AreEqual(undos, t.Last[i].Undo.Epoch);
                    Assert.AreEqual(undos, t.Notes[i].Count, "되돌림마다 모든 좌석에 알림 한 번");
                }
            }
            Assert.GreaterOrEqual(undos, 3, "무를 자리가 없었다");
            Assert.AreEqual(undos, t.S.Undos);
            int common = 0;
            foreach (var kv in t.Hashes)
                if (reference.Hashes.TryGetValue(kv.Key, out var h))
                {
                    Assert.AreEqual(h, kv.Value, $"CommandCount {kv.Key}: 무른 판과 무르지 않은 판이 갈렸다");
                    common++;
                }
            Assert.Greater(common, n / 2);
            Assert.Greater(t.Replayed, 0, "되돌린 뒤 같은 자리의 이벤트를 다시 받았다(같은 글)");
        }

        [Test]
        public void M10_무르기_내턴_운이드러난뒤로는못무른다()
        {
            var t = new Table(ctx, 2, 22, UndoMode.Turn);
            var free = new Table(ctx, 2, 22, UndoMode.Free); // 같은 판(무르기 설정은 판을 바꾸지 않는다)
            int luck = 0;
            while (t.S.CommandCount < 500 && !t.S.GameOver)
            {
                t.Step();
                free.Step();
                Assert.AreEqual(t.S.Hash, free.S.Hash);
                var o = t.OfferActive();
                if (o == null || o.Block != UndoBlock.Luck) continue;
                luck++;
                ulong h = t.S.Hash;
                for (int c = Math.Max(0, t.S.CommandCount - 40); c < t.S.CommandCount; c++) Assert.IsFalse(t.Undo(t.Active, c), $"운이 드러난 뒤 {c}로");
                Assert.AreEqual(h, t.S.Hash);
                Assert.AreEqual(0, t.S.Epoch);
                var f = free.OfferActive();
                Assert.AreNotEqual(UndoBlock.Luck, f.Block, "자유는 운을 보지 않는다");
            }
            Assert.Greater(luck, 5);
        }

        [Test]
        public void M10_무르기_내턴_턴처음앞으로는못간다()
        {
            var t = new Table(ctx, 2, 23, UndoMode.Free);
            int starts = 0;
            while (t.S.CommandCount < 400 && !t.S.GameOver)
            {
                t.Step();
                var o = t.OfferActive();
                if (o == null || o.Block != UndoBlock.TurnStart) continue;
                starts++;
                for (int c = Math.Max(0, t.S.CommandCount - 40); c < t.S.CommandCount; c++) Assert.IsFalse(t.Undo(t.Active, c));
                Assert.AreEqual(0, t.S.Epoch);
            }
            Assert.Greater(starts, 5);
        }

        [Test]
        public void M10_무르기_배틀중무르기는배틀앞보드로()
        {
            var t = new Table(ctx, 2, 24, UndoMode.Free);
            int undos = 0, last = 0;
            while (t.S.CommandCount < 600 && !t.S.GameOver && undos < 4)
            {
                t.Step();
                var h = t.S.HostCopy();
                if (h.Battle == null || t.S.CommandCount <= last) continue;
                var o = t.OfferActive();
                if (o == null || o.StepTo < 0) continue;
                Assert.Less(o.StepTo, t.S.CommandCount);
                last = t.S.CommandCount;
                Assert.IsTrue(t.Undo(h.ActiveSeat, o.StepTo));
                undos++;
                var back = t.S.HostCopy();
                Assert.IsNull(back.Battle, "배틀 앞 보드");
                Assert.IsTrue(SaveRest.IsRest(back));
                Assert.AreEqual(o.StepTo, back.CommandCount);
            }
            Assert.Greater(undos, 0, "배틀 중에 무를 자리가 없었다");
        }

        [Test]
        public void M10_무르기_자유_운을넘어턴처음까지()
        {
            var turn = new Table(ctx, 2, 25, UndoMode.Turn);
            var t = new Table(ctx, 2, 25, UndoMode.Free);
            while (true)
            {
                Assert.Less(t.S.CommandCount, 600, "운이 드러난 자리가 없었다");
                turn.Step();
                t.Step();
                var o = turn.OfferActive();
                if (o != null && o.Block == UndoBlock.Luck) break;
            }
            int seat = t.Active, turnId = t.S.HostCopy().TurnId, undos = 0;
            for (var f = t.S.OfferFor(seat); f.StepTo >= 0; f = t.S.OfferFor(seat))
            {
                Assert.IsTrue(t.Undo(seat, f.StepTo));
                undos++;
                Assert.Less(undos, 200);
            }
            Assert.Greater(undos, 0, "자유는 운을 넘는다");
            var at = t.S.HostCopy();
            Assert.AreEqual(UndoBlock.TurnStart, t.S.OfferFor(seat).Block, "내 턴 처음에서 멈춘다");
            Assert.AreEqual(turnId, at.TurnId);
            Assert.AreEqual(seat, at.ActiveSeat);
        }

        [Test]
        public void M10_무르기_자유_같은명령은같은주사위()
        {
            var t = new Table(ctx, 2, 26, UndoMode.Free);
            int undos = 0, last = 0;
            while (t.S.CommandCount < 700 && !t.S.GameOver)
            {
                bool dice = false;
                int before = t.Last[0].Version;
                t.Step();
                if (t.Last[0].Version != before) dice = t.Last[0].Events.Exists(e => e is DiceRolled);
                if (!dice || t.S.CommandCount <= last || undos >= 6) continue;
                var o = t.OfferActive();
                if (o == null || o.StepTo < 0) continue;
                last = t.S.CommandCount;
                Assert.IsTrue(t.Undo(t.Active, o.StepTo), "굴린 뒤 자유로 되돌린다");
                undos++;
            }
            Assert.Greater(undos, 0, "굴림 뒤 되돌릴 자리가 없었다");
            Assert.Greater(t.ReplayedDice, 0, "같은 답 → 같은 주사위 면(See가 글로 맞대 본다)");
        }

        [Test]
        public void M10_무르기_방장목록_턴시작마다한줄과한일()
        {
            var t = new Table(ctx, 2, 27, UndoMode.Free);
            var starts = new HashSet<int>();
            t.S.Link.Connect(1, m =>
            {
                if (m is GameUpdate u)
                    foreach (var e in u.Events)
                        if (e is TurnStarted) starts.Add(e.Seq);
            });
            t.Run(350);
            var now = t.S.HostCopy();
            var points = t.S.UndoPoints();
            Assert.Greater(points.Count, 3);
            Assert.That(points.Count, Is.InRange(starts.Count - 1, starts.Count), "턴 시작마다 한 줄(지금 턴이 아직 쉬는 때 전이면 하나 적다)");
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                Assert.Less(p.To, now.CommandCount);
                if (i > 0) Assert.Less(p.To, points[i - 1].To, "새것부터");
                Assert.That(p.Seat, Is.InRange(0, 1));
                Assert.Greater(p.Round, 0);
                Assert.AreEqual(i == 0 && p.TurnId == now.TurnId, p.Current);
                for (int k = 1; k < p.Deeds.Count; k++)
                    Assert.IsFalse(p.Deeds[k].Kind == UndoDeedKind.Moved && p.Deeds[k - 1].Kind == UndoDeedKind.Moved, "이어진 걸음은 한 줄");
            }
            Assert.IsTrue(points.Exists(p => p.Deeds.Exists(d => d.Kind == UndoDeedKind.Moved)), "한 일: 이동");
            Assert.IsTrue(points.Exists(p => p.Deeds.Exists(d => d.Kind == UndoDeedKind.Battle && d.Def >= 0)), "한 일: 배틀(상대)");
            Assert.IsEmpty(new Table(ctx, 2, 27, UndoMode.Turn).S.UndoPoints(), "자유가 아니면 목록이 없다");
        }

        [Test]
        public void M10_무르기_방장되돌리기_지난턴상태와같다()
        {
            var store = new MemStore();
            var t = new Table(ctx, 2, 28, UndoMode.Free, store);
            t.Run(300);
            int end = t.S.CommandCount;
            var points = t.S.UndoPoints();
            var p = points[points.Count / 2];
            Assert.IsFalse(t.S.Rewind(p.To + 1, 0, t.Now), "목록에 없는 자리");
            Assert.IsFalse(new Table(ctx, 2, 28, UndoMode.Turn).S.Rewind(p.To, 0, 0), "자유가 아니면 못 한다");
            store.Ops.Clear();
            Assert.IsTrue(t.S.Rewind(p.To, 0, t.Now));
            Assert.AreEqual(p.To, t.S.CommandCount);
            Assert.AreEqual(t.Hashes[p.To], t.S.Hash, "그 턴 시작의 호스트 원본과 같다");
            var at = t.S.HostCopy();
            Assert.AreEqual(p.Round, at.Round);
            Assert.AreEqual(p.Seat, at.ActiveSeat);
            Assert.AreEqual(p.TurnId, at.TurnId);
            CollectionAssert.AreEqual(new[] { "snap:" + p.To, "trunc:" + p.To, "head" }, store.Ops, "스냅샷 먼저 → 로그 자르기 → 머리");
            Assert.AreEqual(p.To, store.Lines.Count);
            Assert.AreEqual(p.To, store.Head.Summary.CommandCount);
            Assert.IsTrue(store.Snaps[store.Snaps.Count - 1].Checkpoints.TrueForAll(c => c.CommandCount <= p.To));
            for (int i = 0; i < 2; i++)
            {
                var note = t.Last[i].Rewind;
                Assert.IsNotNull(note, "되돌린 직후 갱신에 알림");
                Assert.AreEqual(RewindKind.Host, note.Kind);
                Assert.AreEqual(p.To, note.To);
                Assert.AreEqual(at.NextSeq, note.NextSeq);
                Assert.AreEqual(p.Round, note.Round);
                Assert.AreEqual(p.Seat, note.TurnSeat);
                Assert.AreEqual(1, note.Epoch);
            }
            var shorter = t.S.UndoPoints();
            Assert.IsTrue(shorter.TrueForAll(q => q.To < p.To), "버린 미래의 자리는 목록에서 빠진다");
            t.Step();
            Assert.IsNull(t.Last[0].Rewind, "알림은 되돌린 직후 한 번");
            t.Run(end); // 같은 답 → 같은 판(Record가 자리마다 해시를 맞대 본다)
            Assert.AreEqual(end, t.S.CommandCount);
            Assert.AreEqual(end, store.Lines.Count, "로그가 다시 이어진다");
            for (int i = 0; i < store.Lines.Count; i++) Assert.AreEqual(i, store.Lines[i].Seq);
            // 로그 자르기가 실패하면 메모리는 그대로(화면은 저장 실패로 받는다)
            var q2 = t.S.UndoPoints()[1];
            store.FailTruncate = true;
            ulong h = t.S.Hash;
            Assert.Throws<IOException>(() => t.S.Rewind(q2.To, 0, t.Now));
            Assert.AreEqual(h, t.S.Hash);
            Assert.AreEqual(end, t.S.CommandCount);
            Assert.AreEqual(1, t.S.Epoch);
        }

        [Test]
        public void M10_무르기_방장되돌리기_600줄판다시적용시간()
        {
            // §7-1 ④: 방장 되돌리기는 바닥(판 시작)부터 다시 적용한다 — 600줄 봇 판에서 바로 앞 턴 시작으로(가장 긴 다시 적용) 되돌리는 시간을 잰다(1초를 넘으면 턴 시작 사본을 더 둔다).
            // 3000줄 판도 함께 잰다(봇 판 한 판의 길이 쪽).
            foreach (int n in new[] { 600, 3000 })
            {
                var t = new Table(ctx, 2, 29, UndoMode.Free);
                t.Run(n);
                var points = t.S.UndoPoints();
                var p = points[0].Current ? points[1] : points[0];
                int lines = t.S.CommandCount;
                var sw = Stopwatch.StartNew();
                Assert.IsTrue(t.S.Rewind(p.To, 0, t.Now));
                sw.Stop();
                TestContext.Progress.WriteLine($"M10 방장 되돌리기: {lines}줄 판 → {p.To}줄 자리(바닥부터 다시 적용), {sw.ElapsedMilliseconds} ms");
                Assert.AreEqual(t.Hashes[p.To], t.S.Hash);
            }
        }

        [Test]
        public void M10_무르기_러너답을기다리는중에되돌리면늦은답은버린다()
        {
            var reference = new Table(ctx, 2, 30, UndoMode.Free);
            reference.Run(300);
            var t = new Table(ctx, 2, 30, UndoMode.Free, runner: true);
            AgentRequest q = null;
            UndoOffer o = null;
            while (t.S.CommandCount < 300)
            {
                if (t.S.AwaitingAi && (o = t.OfferActive()) != null && o.StepTo >= 0)
                {
                    q = t.Runner.Last;
                    break;
                }
                t.Step();
            }
            Assert.IsNotNull(q, "AI 답을 기다리며 무를 자리가 없었다");
            int stale = t.S.StaleAiAnswers;
            Assert.IsTrue(t.Undo(t.Active, o.StepTo));
            Assert.IsFalse(t.S.AwaitingAi, "물음을 거둔다");
            t.Runner.Answer(q); // 되돌림 전 물음의 늦은 답
            t.S.Tick(t.Now);
            Assert.AreEqual(stale + 1, t.S.StaleAiAnswers, "늦은 답은 버린다");
            Assert.AreEqual(reference.Hashes[t.S.CommandCount], t.S.Hash);
            t.Run(300);
            foreach (var kv in t.Hashes)
                if (reference.Hashes.TryGetValue(kv.Key, out var h)) Assert.AreEqual(h, kv.Value, $"CommandCount {kv.Key}");
        }

        [Test]
        public void M10_무르기_끔과튜토리얼은제안이없다()
        {
            foreach (var t in new[] { new Table(ctx, 2, 31, UndoMode.Off), new Table(ctx, 2, 31, UndoMode.Free, allow: (_, __) => true) })
            {
                Assert.AreEqual(UndoMode.Off, t.S.UndoMode, "튜토리얼(거르개 · 판 손보기)은 늘 끔");
                Assert.AreEqual(UndoMode.Off, t.S.Head.Undo);
                for (int k = 0; k < 150; k++)
                {
                    t.Step();
                    Assert.IsNull(t.Last[0].Undo, "끈 판의 갱신에는 싣지 않는다(지금과 같은 바이트)");
                    Assert.IsNull(t.Last[0].Rewind);
                }
                Assert.AreEqual(UndoBlock.Off, t.S.OfferFor(t.Active < 0 ? 0 : t.Active).Block);
                Assert.IsEmpty(t.S.UndoPoints());
                for (int c = 0; c < t.S.CommandCount; c++) Assert.IsFalse(t.Undo(t.Active < 0 ? 0 : t.Active, c));
                Assert.AreEqual(0, t.S.Epoch);
            }
        }

        [Test]
        public void M10_무르기_이어하기뒤바닥아래로는못간다()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sinnoh-m10-undo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = new Table(ctx, 2, 32, UndoMode.Free, new SaveStore(dir));
                t.Run(300);
                var rec = SaveStore.Load(dir);
                rec.Commands.Clear(); // 스냅샷 자리 그대로(턴 끝 뒤의 첫 쉬는 때 — 그 턴의 TurnStarted 줄은 바닥 앞)
                int floor = rec.Snapshot.CommandCount;
                Assert.Greater(floor, 0);
                t.Run(floor + 120); // 끊지 않은 판도 같은 데까지(견줄 해시)
                var r = new Table(ctx, 2, 32, UndoMode.Free, resume: rec);
                Assert.AreEqual(floor, r.S.CommandCount);
                Assert.AreEqual(UndoBlock.Floor, r.OfferActive().Block, "이어 받은 자리 앞으로는 못 간다");
                Assert.IsEmpty(r.S.UndoPoints());
                r.Run(floor + 120);
                Assert.IsTrue(r.S.UndoPoints().TrueForAll(p => p.To >= floor));
                for (var o = r.OfferActive(); o != null && o.StepTo >= 0; o = r.OfferActive()) Assert.IsTrue(r.Undo(r.Active, o.StepTo));
                Assert.GreaterOrEqual(r.S.CommandCount, floor);
                Assert.AreEqual(t.Hashes[r.S.CommandCount], r.S.Hash, "이어 간 판을 무른 자리 = 끊지 않은 판의 그 자리");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void M10_무르기_머리에설정이남는다()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sinnoh-m10-undo-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.AreEqual(34, SaveRecord.CurrentFormat);
                var s = SoloGame.Start(ctx, new SoloSettings { Undo = UndoMode.Turn }, 5, 6, new PrincipleAgent(), 0, new SaveStore(dir), "m10-head");
                Assert.AreEqual(UndoMode.Turn, s.UndoMode);
                var rec = SaveStore.Load(dir);
                Assert.AreEqual(34, rec.Head.FormatVersion);
                Assert.AreEqual(UndoMode.Turn, rec.Head.Undo);
                Assert.AreEqual(UndoMode.Turn, SoloSettings.FromHead(rec.Head).Undo);
                Assert.AreEqual(UndoMode.Turn, SoloGame.Resume(ctx, rec, new PrincipleAgent(), 0).UndoMode);
                Assert.AreEqual(UndoMode.Off, new SoloSettings().Undo, "시험 · 확인용 판의 기본은 끔");
                Assert.AreEqual(UndoMode.Turn, SoloGame.NewGameUndo, "사람이 여는 새 판은 내 턴 무르기");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
