using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M8 「예산 · 시간」(결정 29 ③ 결정당 2초, 01 §13-1 M8-24~27): 마감을 굴리기마다 본다 · 굴리기 수 한도면 결정적 · 마감이 매우 짧아도 합법 답 ·
    /// 표본 수 K(16~64) · 「강함」 조립 StrongAgent.Strong() · 결정당 시간 재기(Explicit — 관문 밖).
    /// </summary>
    public partial class StrongAiTests
    {
        /// <summary>합성 장면 셋 × 시드 seeds에서 원칙으로 진행하며 만난 갤럭시단 결정 자리(합법 답 둘 이상 — 명단 포함)의 원본 사본, 장면마다 앞 perScene개.</summary>
        List<GameState> BudgetSpots(ulong seeds, int perScene)
        {
            var spots = new List<GameState>();
            var legal = new List<ChoiceAnswer>();
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < seeds; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    int got = 0;
                    for (int step = 0; step < 400 && got < perScene; step++)
                    {
                        var c = s.Pending.Find(x => !x.Answered);
                        if (c == null || (s.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(s, ctx, c, legal);
                        if (c.Seat == SeatId.Galactic && legal.Count > 1)
                        {
                            spots.Add(s.Clone());
                            got++;
                        }
                        Assert.IsTrue(Rollout.Step(s, ctx, c, PrincipleAgent.Choose(s, ctx, c, legal).Answer));
                    }
                }
            return spots;
        }

        /// <summary>원본 s의 열린 갤럭시단 선택을 관전자 보기로 묻는다.</summary>
        AgentDecision AskGalactic(StrongAgent agent, GameState s, AgentBudget budget, out List<ChoiceAnswer> legal)
        {
            var c = s.Pending.Find(x => !x.Answered);
            legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);
            var view = Engine.ViewFor(s, SeatId.Observer);
            return agent.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
        }

        /// <summary>시험이 쓰는 꼴 셋: 평평(굴리기 48) · 탐색(Tree 96 · 깊이 8) · 「강함」(굴리기 한도는 예산에서 96으로 줄임).</summary>
        static readonly string[] BudgetKinds = { "평평", "탐색", "강함" };

        static StrongAgent BudgetAgent(int k) => k == 0 ? new StrongAgent() : k == 1 ? Searcher(SearchKind.Tree) : StrongAgent.Strong();

        static int BudgetCap(int k) => k == 0 ? 48 : 96;

        // ── 「강함」 조립 · 표본 수 ──

        [Test]
        public void M8_예산_강함조립과표본수()
        {
            var a = StrongAgent.Strong();
            Assert.AreEqual(SearchKind.Tree, a.Search);
            Assert.AreEqual(8, a.Depth);
            Assert.IsTrue(a.ScreenLineup);
            Assert.AreEqual(StrongAgent.StrongSimulations, a.MaxSimulations);
            Assert.AreEqual(64, a.MaxSamples);
            Assert.AreEqual(1500, a.ThinkMs, "러너 마감(열린 시각 + 2초)보다 여유");
            Assert.Less(a.ThinkMs, new AiSettings().DeadlineMs);
            Assert.AreNotSame(a, StrongAgent.Strong(), "부를 때마다 새 인스턴스(스레드 하나에서 쓴다)");
            // 기본값은 옛 설정 그대로(결정 지문 둘)
            var d = new StrongAgent();
            Assert.AreEqual(SearchKind.Flat, d.Search);
            Assert.AreEqual(StrongAgent.Samples, d.MaxSamples);
            Assert.AreEqual(StrongAgent.DefaultSimulations, d.MaxSimulations);
            Assert.AreEqual(0, d.ThinkMs);
            // K = 한도 ÷ 답 수(탐색 꼴은 ÷ 16도)를 1~상한으로 — 「강함」 한도 1024
            Assert.AreEqual(32, StrongAgent.SampleCount(1024, 2, SearchKind.Tree, 64), "답 둘(운명 반응 · 우선권 토큰)");
            Assert.AreEqual(21, StrongAgent.SampleCount(1024, 3, SearchKind.Tree, 64));
            Assert.AreEqual(8, StrongAgent.SampleCount(1024, 8, SearchKind.Tree, 64), "답 여덟(기술 · 출전)");
            Assert.AreEqual(4, StrongAgent.SampleCount(1024, 16, SearchKind.Tree, 64), "거른 명단 16");
            Assert.AreEqual(64, StrongAgent.SampleCount(4096, 2, SearchKind.Tree, 64), "상한 64");
            Assert.AreEqual(1, StrongAgent.SampleCount(8, 28, SearchKind.Tree, 64), "한도가 작아도 표본 하나");
            Assert.AreEqual(16, StrongAgent.SampleCount(48, 2, SearchKind.Flat, 16), "옛 평평 꼴 상한 16");
            Assert.AreEqual(3, StrongAgent.SampleCount(48, 16, SearchKind.Flat, 16));
        }

        // ── 마감 ──

        [Test]
        public void M8_예산_가짜시계로마감을굴리기마다보고지나면더굴리지않는다()
        {
            // 가짜 시계: 부를 때마다 1씩 가고, 마감 = 시작 + d. 마감을 본 뒤로는 굴리기(Simulations · 굴리기 정책 부름)가 하나도 늘지 않는다.
            // 굴리기마다 시계를 먼저 보므로 굴리기 수 < d — 표본 하나마다 보던 옛 길이면 표본 하나(답 수 × 반복)를 다 돌았다.
            var spots = BudgetSpots(1, 2);
            int checkedCount = 0, sawCut = 0;
            foreach (var s in spots)
                for (int k = 0; k < 3; k++)
                    foreach (int d in new[] { 4, 9 })
                    {
                        var agent = BudgetAgent(k);
                        long t = 0, deadline = long.MaxValue, simsAtDeadline = -1, policyCalls = 0, policyAtDeadline = -1;
                        if (k > 0)
                        {
                            var inner = Rollout.Rival;
                            agent.Policy = (g, cx, ch, lg) =>
                            {
                                policyCalls++;
                                return inner(g, cx, ch, lg);
                            };
                        }
                        Func<long> clock = () =>
                        {
                            long now = t++;
                            if (now >= deadline && simsAtDeadline < 0)
                            {
                                simsAtDeadline = agent.Simulations;
                                policyAtDeadline = policyCalls;
                            }
                            return now;
                        };
                        deadline = d; // 시작에 한 번 보면 0 → 마감 d
                        var budget = new AgentBudget { MaxSimulations = 4096, Seed = 77, Now = clock, Deadline = d };
                        long before = agent.Simulations;
                        var dec = AskGalactic(agent, s, budget, out var legal);
                        string at = $"{BudgetKinds[k]} d {d} {s.Pending.Find(x => !x.Answered).Kind}";
                        Assert.IsTrue(legal.Exists(x => x.SameAs(dec.Answer)), $"{at}: 합법 답");
                        Assert.IsTrue(agent.LastTimedOut, $"{at}: 시간 마감으로 끝났다");
                        Assert.GreaterOrEqual(simsAtDeadline, 0, $"{at}: 마감을 봤다");
                        Assert.AreEqual(1, agent.TimeOuts);
                        if (k == 0) Assert.AreEqual(simsAtDeadline, agent.Simulations, $"{at}: 마감 뒤 굴리기 없음");
                        else Assert.AreEqual(policyAtDeadline, policyCalls, $"{at}: 마감 뒤 굴리기 정책을 부르지 않았다");
                        long ran = agent.Simulations - before;
                        Assert.Less(ran, d, $"{at}: 굴리기마다 시계를 본다");
                        if (ran > 0) sawCut++;
                        checkedCount++;
                    }
            Assert.AreEqual(spots.Count * 6, checkedCount);
            Assert.Greater(sawCut, spots.Count, "마감 전에 굴리기를 돌린 결정");
        }

        [Test]
        public void M8_예산_마감이매우짧아도합법답()
        {
            // 마감이 이미 지났다(시계 100 · 마감 0) · 시작과 같다 · 자기 마감이 1 ms: 굴리기 없이(또는 거의 없이) 합법 답 — 아무것도 못 굴렸으면 원칙 답.
            var spots = BudgetSpots(1, 2);
            int n = 0;
            foreach (var s in spots)
                for (int k = 0; k < 3; k++)
                {
                    foreach (long deadline in new long[] { 0, 100 })
                    {
                        var agent = BudgetAgent(k);
                        var dec = AskGalactic(agent, s, new AgentBudget { MaxSimulations = BudgetCap(k), Seed = 5, Now = () => 100, Deadline = deadline }, out var legal);
                        Assert.IsTrue(legal.Exists(x => x.SameAs(dec.Answer)), "합법 답");
                        Assert.AreEqual(0, agent.Simulations, "굴리기 없음");
                        Assert.IsTrue(agent.LastTimedOut);
                        var view = Engine.ViewFor(s, SeatId.Observer);
                        var vc = view.Pending.Find(x => !x.Answered);
                        var vl = new List<ChoiceAnswer>();
                        Engine.Legal(view, ctx, vc, vl);
                        Assert.IsTrue(PrincipleAgent.Choose(view, ctx, vc, vl).Answer.SameAs(dec.Answer), "원칙 답");
                        n++;
                    }
                    // 자기 마감(ThinkMs)만 짧다: 예산 마감은 멀어도 시작 + 1에서 멈춘다(시계가 부를 때마다 1 간다)
                    var own = BudgetAgent(k);
                    own.ThinkMs = 1;
                    long t = 0;
                    var d2 = AskGalactic(own, s, new AgentBudget { MaxSimulations = BudgetCap(k), Seed = 5, Now = () => t++, Deadline = long.MaxValue }, out var legal2);
                    Assert.IsTrue(legal2.Exists(x => x.SameAs(d2.Answer)), "합법 답");
                    Assert.IsTrue(own.LastTimedOut, "자기 마감");
                    Assert.LessOrEqual(own.Simulations, 1);
                    n++;
                }
            Assert.AreEqual(spots.Count * 9, n);
        }

        [Test]
        public void M8_예산_굴리기수한도로끝나면결정적이고엿보기없음()
        {
            // 굴리기 수 한도로 끝난 결정(시계가 있어도 마감 전): 시계 없이 · 시계 있이 · 다시 물어도 · 숨은 정보만 다른 원본에서도 답 · 굴리기 수가 같다.
            var spots = BudgetSpots(1, 2);
            int n = 0;
            foreach (var a in spots)
            {
                var b = a.Clone();
                Scramble(b, 31 + (ulong)n);
                Assert.AreEqual(StateHash.Of(Engine.ViewFor(a, SeatId.Observer)), StateHash.Of(Engine.ViewFor(b, SeatId.Observer)), "두 원본의 보기는 같다");
                for (int k = 0; k < 3; k++)
                {
                    int cap = BudgetCap(k);
                    ulong seed = 900 + (ulong)n;
                    var plain = BudgetAgent(k);
                    var timed = BudgetAgent(k);
                    var hidden = BudgetAgent(k);
                    var d0 = AskGalactic(plain, a, new AgentBudget { MaxSimulations = cap, Seed = seed }, out _);
                    var d1 = AskGalactic(timed, a, new AgentBudget { MaxSimulations = cap, Seed = seed, Now = () => 0, Deadline = long.MaxValue }, out _);
                    var d2 = AskGalactic(plain, a, new AgentBudget { MaxSimulations = cap, Seed = seed }, out _);
                    var d3 = AskGalactic(hidden, b, new AgentBudget { MaxSimulations = cap, Seed = seed, Now = () => 0, Deadline = 1 }, out _);
                    string at = $"{BudgetKinds[k]} 자리 {n}";
                    Assert.IsFalse(timed.LastTimedOut, at);
                    Assert.IsFalse(hidden.LastTimedOut, at);
                    Assert.AreEqual(0, timed.TimeOuts + hidden.TimeOuts + plain.TimeOuts, at);
                    Assert.IsTrue(d0.Answer.SameAs(d1.Answer), $"{at}: 시계가 있어도 같다");
                    Assert.IsTrue(d0.Answer.SameAs(d2.Answer), $"{at}: 다시 물어도 같다");
                    Assert.IsTrue(d0.Answer.SameAs(d3.Answer), $"{at}: 숨은 정보만 다른 원본");
                    Assert.AreEqual(plain.Simulations, 2 * timed.Simulations, at);
                    Assert.AreEqual(timed.Simulations, hidden.Simulations, at);
                    Assert.Greater(timed.Simulations, 0, at);
                    Assert.LessOrEqual(timed.Simulations, cap, $"{at}: 한도 안");
                    Assert.AreEqual(0, timed.Mismatch, at);
                }
                n++;
            }
            Assert.AreEqual(Scenes.Length * 2, n);
        }

        // ── 결정당 시간(관문 밖) ──

        /// <summary>결정당 시간 재기의 장면 수(SINNOH_BUDGET_SCENES — 기본 dotnet 40 · Unity 20) · 굴리기 한도(SINNOH_BUDGET_SIMS, 0 = 「강함」 기본) · 시간 마감(SINNOH_BUDGET_CLOCK=0이면 끔 — 한도만의 시간).</summary>
#if UNITY_EDITOR
        static readonly int BudgetScenes = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BUDGET_SCENES"), out var bs) && bs > 0 ? bs : 20;
#else
        static readonly int BudgetScenes = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BUDGET_SCENES"), out var bs) && bs > 0 ? bs : 40;
#endif
        static readonly int BudgetSims = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BUDGET_SIMS"), out var bsim) && bsim > 0 ? bsim : 0;
        static readonly bool BudgetClock = Environment.GetEnvironmentVariable("SINNOH_BUDGET_CLOCK") != "0";

        /// <summary>갤럭시단 AI를 감싸 러너처럼 마감(시작 + 2초, M7-24)을 넣고 결정당 시간(µs)을 선택 종류마다 모은다.</summary>
        sealed class BudgetClockAgent : IAgent
        {
            readonly StrongAgent inner;
            readonly Stopwatch sw = Stopwatch.StartNew();
            readonly Func<long> ms;
            public readonly Dictionary<string, List<long>> Times = new Dictionary<string, List<long>>();
            public readonly Dictionary<string, int> Outs = new Dictionary<string, int>();

            public BudgetClockAgent(StrongAgent inner)
            {
                this.inner = inner;
                ms = () => sw.ElapsedMilliseconds;
            }

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                if (BudgetClock)
                {
                    budget.Now = ms;
                    budget.Deadline = ms() + new AiSettings().DeadlineMs;
                }
                long t0 = sw.ElapsedTicks;
                var d = inner.Decide(input, budget);
                long us = (sw.ElapsedTicks - t0) * 1000000 / Stopwatch.Frequency;
                if (inner.LastSamples > 0) // 찾아본 결정만(답 하나 · 배틀 밖은 원칙 답)
                {
                    string key = input.Choice.Kind == ChoiceKind.Reaction ? $"Reaction/{input.Choice.Sub}" : input.Choice.Kind.ToString();
                    foreach (var k in new[] { key, "전체" })
                    {
                        if (!Times.TryGetValue(k, out var list)) Times[k] = list = new List<long>();
                        list.Add(us);
                        if (inner.LastTimedOut) Outs[k] = (Outs.TryGetValue(k, out int o) ? o : 0) + 1;
                    }
                }
                return d;
            }
        }

        [Test, Explicit("결정당 시간 재기 — 관문 밖(dotnet --filter · Unity -testFilter로 짚어 돈다)")]
        public void M8_예산_결정당시간_강함설정()
        {
            // 옛 정본(운영 규칙 끔) 앞 BudgetScenes 장면 × 갤 「강함」(StrongAgent.Strong() — 굴리기 한도 = BudgetSims 또는 기본 · 러너처럼 마감 시작 + 2초) · 플 원칙(좌석 봇), 주사위 0.
            // 선택 종류마다 결정 수 · 평균 · 99% · 최대(ms) · 시간 마감으로 끝난 수. 한 프로세스 · 한 스레드.
            var agent = StrongAgent.Strong();
            var clock = new BudgetClockAgent(agent);
            var sw = Stopwatch.StartNew();
            var all = new BenchResult();
            int scenes = 0;
            ForEachRealScene(ctx, BudgetScenes, (game, i, s) =>
            {
                ulong id = game * 1000 + (ulong)i;
                var s0 = GalacticBench.Reroll(s, id, 0);
                var bot = new Playout.Bot(game);
                var rng = new Pcg32(id, 0xB07);
                RolloutPolicy seat = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
                GalacticBench.Play(s0, ctx, clock, seat, all, BudgetSims, 1, id);
                scenes++;
            });
#if UNITY_EDITOR
            const string where = "Unity Mono";
#else
            const string where = "dotnet";
#endif
            Report($"M8 예산 결정당 시간({where}): 옛 정본 장면 {scenes} · 「강함」 굴리기 {(BudgetSims > 0 ? BudgetSims : StrongAgent.StrongSimulations)} · 표본 상한 {agent.MaxSamples} · 깊이 {agent.Depth} · 자기 마감 {agent.ThinkMs} ms{(BudgetClock ? "" : "(시계 끔)")} · 갤 승률 {BenchResult.Percent(all.WinPermille)} · 굴리기 {agent.Simulations} · 시간 마감 {agent.TimeOuts} · {sw.ElapsedMilliseconds}ms");
            var keys = new List<string>(clock.Times.Keys);
            keys.Sort(string.CompareOrdinal);
            foreach (var k in keys)
            {
                var list = clock.Times[k];
                list.Sort();
                long sum = 0;
                foreach (long v in list) sum += v;
                long p99 = list[Math.Min(list.Count - 1, (int)Math.Ceiling(list.Count * 0.99) - 1)];
                Report($"M8 예산 결정당 시간 [{k}] 결정 {list.Count} · 평균 {sum / list.Count / 1000.0:0.0} ms · 99% {p99 / 1000.0:0.0} ms · 최대 {list[list.Count - 1] / 1000.0:0.0} ms · 시간 마감 {(clock.Outs.TryGetValue(k, out int o) ? o : 0)}");
            }
            Assert.AreEqual(scenes, all.Battles);
            Assert.AreEqual(0, all.Stuck);
            Assert.AreEqual(0, agent.Mismatch);
            Assert.IsTrue(clock.Times.ContainsKey("전체"));
            if (BudgetClock) Assert.LessOrEqual(clock.Times["전체"][clock.Times["전체"].Count - 1], 2000000L, "결정 하나 2초 이하(결정 29 ③)");
        }

        // ── 마감 준비: 최종 시험대(「강함」 Strong()) ──

        [Test, Explicit("최종 시험대 — 관문 밖(dotnet --filter, SINNOH_BENCH_SCENES=1000 · SINNOH_BENCH_SHARD=i/n)")]
        public void M8_시험대_강함최종_네대진()
        {
            // 옛 정본(운영 규칙 끔) × 네 대진(갤 원칙/「강함」 × 플 원칙(좌석 봇)/1단계(평평 · 굴리기 BenchSims)), 주사위 0. 갤 「강함」 = StrongAgent.Strong()을
            // 러너처럼 마감(시작 + 2초)을 넣어 묻는다(BudgetClockAgent — 한도 1024 · 자기 마감 1.5초, 시간 마감으로 끝난 결정은 결정적이 아니다).
            // 칸: 종류 · 전체 · 팽팽(원칙끼리 TightDice 30~70%) · 준비된 도전 · 파티 강도. 조각마다 원자료 줄(합치기용)을 낸다. 장면 수: SINNOH_BENCH_SCENES(없으면 4).
            int count = Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES") != null ? BenchScenes : 4;
            int shard = 0, shards = 1;
            var sh = Environment.GetEnvironmentVariable("SINNOH_BENCH_SHARD");
            if (!string.IsNullOrEmpty(sh))
            {
                var parts = sh.Split('/');
                shard = int.Parse(parts[0]);
                shards = int.Parse(parts[1]);
            }
            var sw = Stopwatch.StartNew();
            const int Groups = 9; // 0..2 종류 · 3 전체 · 4 팽팽 · 5 준비 · 6..8 칸
            var res = new BenchResult[4, Groups];
            for (int m = 0; m < 4; m++)
                for (int g = 0; g < Groups; g++) res[m, g] = new BenchResult();
            var strong = new StrongAgent[4];
            var clocks = new BudgetClockAgent[4];
            var pl = new StrongAgent[4];
            for (int m = 0; m < 4; m++)
            {
                if (m % 2 == 1) clocks[m] = new BudgetClockAgent(strong[m] = StrongAgent.Strong());
                if (m >= 2) pl[m] = new StrongAgent();
            }
            int scenes = 0, tight = 0, index = 0;
            ulong digest = 14695981039346656037UL;   // 장면 지문 — 조각과 상관없이 모든 장면(1,000이면 정본 지문, 규칙이 바뀌면 바뀐다)
            var groups = new List<int>(5);
            ForEachRealScene(ctx, count, (game, i, s) =>
            {
                digest = unchecked((digest ^ StateHash.Of(s)) * 1099511628211UL);
                if (index++ % shards != shard) return;
                int kind = KindOf(s);
                ulong id = game * 1000 + (ulong)i;
                var measure = PartyGauge.OfScene(s, ctx);
                int rate = PrincipleRate(s, game, id);
                groups.Clear();
                groups.Add(kind);
                groups.Add(3);
                if (rate >= GalacticBench.TightLow && rate <= GalacticBench.TightHigh)
                {
                    groups.Add(4);
                    tight++;
                }
                if (measure.Ready) groups.Add(5);
                groups.Add(6 + (int)measure.Band);
                for (int m = 0; m < 4; m++)
                {
                    var one = new BenchResult();
                    if (m % 2 == 0) PlayMatchup(m, s, game, id, 0, null, pl[m], one, null);
                    else
                    {
                        // PlayMatchup과 같은 판 · 같은 씨, 굴리기 한도만 「강함」 기본(0 → MaxSimulations 1024)
                        var s0 = GalacticBench.Reroll(s, id, 0);
                        var bot = new Playout.Bot(game);
                        var rng = new Pcg32(id, 0xB07);
                        RolloutPolicy seat = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
                        if (m == 3) seat = GalacticBench.SeatAgent(pl[m], seat, BenchSims, 2, id);
                        GalacticBench.Play(s0, ctx, clocks[m], seat, one, 0, 1, id);
                    }
                    foreach (int g in groups) res[m, g].Add(one);
                }
                scenes++;
            });
            string[] names = { SceneKinds[0], SceneKinds[1], SceneKinds[2], "전체", "팽팽", "준비", Bands[0], Bands[1], Bands[2] };
            string[] rows = { "갤 원칙 · 플 원칙", "갤 강함 · 플 원칙", "갤 원칙 · 플 1단계", "갤 강함 · 플 1단계" };
            Report($"M8 강함최종(dotnet): 옛 정본 장면 {scenes}{(shards > 1 ? $"(조각 {shard}/{shards})" : "")} · 장면 {index}개 지문 0x{digest:X16} · 팽팽 {tight} · 「강함」 굴리기 {StrongAgent.StrongSimulations} · 표본 상한 {StrongAgent.StrongSamples} · 깊이 {StrongAgent.StrongDepth} · 자기 마감 {StrongAgent.StrongThinkMs} ms{(BudgetClock ? "" : "(시계 끔)")} · 플 1단계 굴리기 {BenchSims} · {sw.ElapsedMilliseconds}ms");
            for (int m = 0; m < 4; m++)
            {
                var parts = new List<string>();
                var raw = new List<string>();
                for (int g = 0; g < Groups; g++)
                {
                    parts.Add($"{names[g]} {res[m, g].Battles}: {BenchResult.Percent(res[m, g].WinPermille)}");
                    raw.Add($"{res[m, g].Battles},{res[m, g].GalacticWins},{res[m, g].Draws},{res[m, g].PlayerHpLeft}");
                }
                string think = strong[m] == null ? "" : $" · 굴리기 {strong[m].Simulations} · 시간 마감 {strong[m].TimeOuts}";
                Report($"M8 강함최종 [{rows[m]}] {string.Join(" · ", parts)} · 플레이어 남은 체력 {BenchResult.Percent(res[m, 3].PlayerHpPermille)}{think}");
                // 합치기용 원자료: 무리마다 배틀,갤 승,비김,플 남은 체력 합
                Report($"M8 강함최종 원자료 {m} {string.Join(" ", raw)}");
                if (clocks[m] == null) continue;
                var keys = new List<string>(clocks[m].Times.Keys);
                keys.Sort(string.CompareOrdinal);
                foreach (var k in keys)
                {
                    var list = clocks[m].Times[k];
                    long sum = 0, max = 0;
                    foreach (long v in list)
                    {
                        sum += v;
                        if (v > max) max = v;
                    }
                    // 선택 종류마다: 결정 수,µs 합,최대 µs,시간 마감
                    Report($"M8 강함최종 시간 {m} {k} {list.Count},{sum},{max},{(clocks[m].Outs.TryGetValue(k, out int o) ? o : 0)}");
                }
                // 전체 결정 시간 분포(ms 칸:수 — 99%를 조각 합쳐 셈)
                var hist = new SortedDictionary<long, int>();
                foreach (long v in clocks[m].Times["전체"]) hist[v / 1000] = (hist.TryGetValue(v / 1000, out int c0) ? c0 : 0) + 1;
                var cells = new List<string>();
                foreach (var kv in hist) cells.Add($"{kv.Key}:{kv.Value}");
                Report($"M8 강함최종 분포 {m} {string.Join(" ", cells)}");
            }
            for (int m = 0; m < 4; m++)
            {
                Assert.AreEqual(0, res[m, 3].Stuck, rows[m]);
                Assert.AreEqual(scenes, res[m, 3].Battles, rows[m]);
                if (strong[m] != null)
                {
                    Assert.AreEqual(0, strong[m].Stuck);
                    Assert.AreEqual(0, strong[m].Mismatch);
                }
                if (pl[m] != null) Assert.AreEqual(0, pl[m].Stuck);
            }
            Assert.Greater(scenes, 0);
        }
    }
}
