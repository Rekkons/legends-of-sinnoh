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
    /// plan M8 「탐색」(01 §9-6, §13-1 M8-17~): 주사위 정해 넣기 ForcedDice · 표본마다 탐색(1수 기대값 Expect · MCTS Tree — SearchTree) · 상대 모델 굴리기 Rollout.Rival ·
    /// 상대 모델 무게(BattleEval.Rival) · 시험대(옛 정본 네 대진 — 1단계 · 깊이 8 숫자와 견줌).
    /// </summary>
    public partial class StrongAiTests
    {
        /// <summary>새 탐색 시험대의 굴리기 한도(SINNOH_SEARCH_SIMS, 기본 256)와 꼴(SINNOH_SEARCH_KIND = tree · expect).</summary>
        public static readonly int SearchSims = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_SEARCH_SIMS"), out var ss) && ss > 0 ? ss : 256;
        public static readonly SearchKind SearchBench = Environment.GetEnvironmentVariable("SINNOH_SEARCH_KIND") == "expect" ? SearchKind.Expect : SearchKind.Tree;
        /// <summary>탐색 시험대의 장면 수: 관문은 줄인 판(dotnet 4 · Unity 2), SINNOH_BENCH_SCENES를 주면 그 수. SINNOH_BENCH_SHARD="i/n"이면 장면 번호 % n = i만(관문 밖에서 나눠 돌림).</summary>
#if UNITY_EDITOR
        public static readonly int SearchScenes = 2;
#else
        public static readonly int SearchScenes = Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES") != null ? BenchScenes : 4;
#endif

        /// <summary>탐색 시험대에서 돌릴 대진(SINNOH_BENCH_ROWS = 「1,3」 꼴 — 0 갤 원칙 · 플 원칙 / 1 갤 새 · 플 원칙 / 2 갤 원칙 · 플 새 / 3 갤 새 · 플 새, 기본 전부).</summary>
        static readonly List<int> BenchRows = Environment.GetEnvironmentVariable("SINNOH_BENCH_ROWS") is string rows && rows.Length > 0
            ? new List<string>(rows.Split(',')).ConvertAll(int.Parse) : new List<int> { 0, 1, 2, 3 };

        /// <summary>시험이 쓰는 탐색 AI(깊이 EvalDepth — 평가 묶음의 시험대 깊이).</summary>
        static StrongAgent Searcher(SearchKind kind) => new StrongAgent { Search = kind, Depth = EvalDepth };

        // ── 주사위 정해 넣기 ──

        [Test]
        public void M8_탐색_주사위정해넣기는묶음대로고나머지는새로굴린다()
        {
            var fd = new ForcedDice();
            var game = new Pcg32(5, 1);
            for (int n = AttackDice.MinDice; n <= AttackDice.MaxDice; n++)
            {
                var outs = ForcedDice.Outcomes(n);
                Assert.AreSame(outs, ForcedDice.Outcomes(n), "묶음 표는 한 번 만든다");
                long w = 0;
                foreach (var o in outs) w += o.Weight;
                Assert.AreEqual(AttackDice.TotalWeight(n), w, "무게 합 = 6^n");
                using (Rig.Use(fd))
                    foreach (var o in outs)
                    {
                        fd.Clear();
                        fd.Push(o);
                        var faces = AttackDice.Roll(game, n);
                        Assert.AreEqual(o.StatusSymbols, AttackDice.Count(faces, AttackDieFace.StatusSymbol));
                        Assert.AreEqual(o.RegularHits, AttackDice.Count(faces, AttackDieFace.RegularHit));
                        Assert.AreEqual(o.CriticalHits, AttackDice.Count(faces, AttackDieFace.CriticalHit));
                        Assert.AreEqual(o.Sum, AttackDice.Sum(faces));
                        Assert.AreEqual(1, fd.Forced);
                        Assert.AreEqual(n, fd.FirstDice);
                    }
            }
            // 주사위 수가 다르면 넣지 않고(Mismatch), 넣을 것이 없으면 Free로 새로 굴린다(같은 씨면 같은 면) · Free가 없으면 그대로
            fd.Clear();
            fd.Push(ForcedDice.Outcomes(2)[0]);
            fd.Free = new SampledRng(9);
            AttackDieFace[] a, b, plain;
            using (Rig.Use(fd)) a = AttackDice.Roll(new Pcg32(1, 1), 3);
            Assert.AreEqual(1, fd.Mismatch);
            fd.Free = new SampledRng(9);
            using (Rig.Use(fd)) b = AttackDice.Roll(new Pcg32(2, 7), 3);
            Assert.AreEqual(a, b, "새로 굴린 면은 Free에서만 나온다");
            fd.Free = null;
            using (Rig.Use(fd)) plain = AttackDice.Roll(new Pcg32(2, 7), 3);
            Assert.AreEqual(AttackDice.Roll(new Pcg32(2, 7), 3), plain);
        }

        // ── 뻔한 수 ──

        /// <summary>갤럭시단 배틀 행동 선택(답 둘 이상)까지 원칙 AI로 민다. 못 가면 false.</summary>
        bool AdvanceToGalacticAction(GameState s)
        {
            var legal = new List<ChoiceAnswer>();
            for (int step = 0; step < 200; step++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c == null || (s.Battle == null && c.Kind != ChoiceKind.Lineup)) return false;
                legal.Clear();
                Engine.Legal(s, ctx, c, legal);
                if (s.Battle != null && c.Kind == ChoiceKind.BattleAction && c.Seat == SeatId.Galactic && legal.Count > 1) return true;
                Assert.IsTrue(Rollout.Step(s, ctx, c, PrincipleAgent.Choose(s, ctx, c, legal).Answer));
            }
            return false;
        }

        /// <summary>그 답을 넣고 이번 공격에서 바로 이길 확률(‰ — 첫 굴림의 §6-3 묶음을 모두 정해 넣어 무게로 셈, 그 뒤 반응은 원칙).</summary>
        int KillPermille(GameState g, PendingChoice c, ChoiceAnswer a)
        {
            var fd = new ForcedDice();
            using (Rig.Use(fd)) Rollout.ToEnd(g.Clone(), ctx, Rollout.Principle, c.Id, a, Rollout.MaxSteps, false, 1);
            int n = fd.FirstDice;
            if (n == 0) return 0;
            long won = 0;
            foreach (var o in ForcedDice.Outcomes(n))
            {
                fd.Clear();
                fd.Push(o);
                RolloutResult r;
                using (Rig.Use(fd)) r = Rollout.ToEnd(g.Clone(), ctx, Rollout.Principle, c.Id, a, Rollout.MaxSteps, false, 1);
                Assert.AreEqual(1, fd.Forced);
                if (r.Ended && r.Winner == 1) won += o.Weight;
            }
            return (int)(won * 1000 / AttackDice.TotalWeight(n));
        }

        [Test]
        public void M8_탐색_뻔한수장면에서그수()
        {
            // 플레이어 쪽을 출전 포켓몬 하나 · 체력 h로 남긴 갤럭시단 배틀 행동 선택(시그니처 기술 / 플랜 B): 이번 공격으로 바로 이길 확률이 한 답은 90% 이상 · 다른 답은 50% 이하인 자리에서
            // 1수 기대값 · 트리 탐색이 바로 이기는 답(가장 큰 확률에서 5%p 안)을 고른다.
            int found = 0;
            var legal = new List<ChoiceAnswer>();
            var lines = new List<string>();
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 8 && found < 6; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    if (!AdvanceToGalacticAction(s)) continue;
                    for (int h = 1; h <= 6; h++)
                    {
                        var g = s.Clone();
                        Rout(g, 0);
                        var act = g.Pokemon[g.Battle.Sides[0].Active];
                        int max = PokemonRules.MaxHp(ctx, act);
                        if (h >= max) break;
                        act.Damage = max - h;
                        var c = g.Pending.Find(x => !x.Answered);
                        legal.Clear();
                        Engine.Legal(g, ctx, c, legal);
                        var kill = new int[legal.Count];
                        int hi = 0, lo = 1000;
                        for (int i = 0; i < legal.Count; i++)
                        {
                            kill[i] = KillPermille(g, c, legal[i]);
                            hi = Math.Max(hi, kill[i]);
                            lo = Math.Min(lo, kill[i]);
                        }
                        if (hi < 900 || lo > 500) continue;
                        var view = Engine.ViewFor(g, SeatId.Observer);
                        var vc = view.Pending.Find(x => x.Id == c.Id);
                        foreach (var kind in new[] { SearchKind.Expect, SearchKind.Tree })
                        {
                            var agent = Searcher(kind);
                            var d = agent.Decide(new AgentInput { View = view, Choice = vc, Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget { MaxSimulations = 256, Seed = 31 + seed });
                            int at = legal.FindIndex(x => x.SameAs(d.Answer));
                            Assert.GreaterOrEqual(at, 0);
                            Assert.GreaterOrEqual(kill[at], hi - 50, $"{scene} 시드 {seed} 체력 {h} {kind}: {d.Answer}(바로 이김 {kill[at]}‰ — 가장 큰 {hi}‰)");
                            Assert.AreEqual(0, agent.Mismatch);
                            Assert.AreEqual(0, agent.Stuck);
                        }
                        lines.Add($"{scene} 시드 {seed} 체력 {h}: {string.Join(" · ", Array.ConvertAll(kill, k => k + "‰"))}");
                        found++;
                        break;
                    }
                }
            Report($"M8 탐색 뻔한 수: {string.Join(" ‖ ", lines)}");
            Assert.GreaterOrEqual(found, 3, "뻔한 수 장면");
        }

        // ── 결정성 · 엿보기 없음 ──

        [Test]
        public void M8_탐색_결정적이고엿보기없음_숨은정보만다른두원본에서결정이같다()
        {
            // 숨은 정보만 다른 두 원본(갤럭시단 쪽 = 관전자 보기 — 플레이어 운명 손패 · 덱 · 숨겨진 장소 · 게임 난수, 플레이어 쪽 = 좌석 보기 — 자기 손패는 둠):
            // 두 꼴(Expect · Tree)의 결정 · 굴리기 수 · 노드 수가 같고, 같은 입력을 두 번 물어도 같다(굴리기 수 한도 — 시계 없음). 트리와 판이 어긋난 적이 없다.
            int decisions = 0, seatDecisions = 0;
            long nodes = 0;
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var a = GalacticBench.Scene(ctx, scene, seed);
                    var fate = a.Decks[(int)DeckId.ChangeOfFate];
                    for (int k = 0; k < 3; k++)
                    {
                        a.Seats[0].FateHand.Add(fate[fate.Count - 1]);
                        fate.RemoveAt(fate.Count - 1);
                    }
                    var legal = new List<ChoiceAnswer>();
                    for (int step = 0, asked = 0; step < 400 && asked < 4; step++)
                    {
                        var c = a.Pending.Find(x => !x.Answered);
                        if (c == null || (a.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(a, ctx, c, legal);
                        ChoiceAnswer answer = PrincipleAgent.Choose(a, ctx, c, legal).Answer;
                        bool galactic = c.Seat == SeatId.Galactic;
                        if (legal.Count > 1 && (galactic || (c.Seat == 0 && a.Battle != null)))
                        {
                            asked++;
                            int viewer = galactic ? SeatId.Observer : 0;
                            var b = a.Clone();
                            Scramble(b, seed * 37 + (ulong)step, galactic ? -1 : 0);
                            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b));
                            var va = Engine.ViewFor(a, viewer);
                            var vb = Engine.ViewFor(b, viewer);
                            Assert.AreEqual(StateHash.Of(va), StateHash.Of(vb), "두 원본의 보기는 같다");
                            foreach (var kind in new[] { SearchKind.Expect, SearchKind.Tree })
                            {
                                var budget = new AgentBudget { MaxSimulations = 96, Seed = 555 + (ulong)c.Id };
                                var agentA = Searcher(kind);
                                var agentB = Searcher(kind);
                                var da = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                                var db = agentB.Decide(new AgentInput { View = vb, Choice = vb.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                                long n1 = agentA.Nodes;
                                var again = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                                string at = $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {kind} 좌석 {c.Seat}";
                                Assert.IsTrue(da.Answer.SameAs(db.Answer), $"{at}: {da.Answer} ≠ {db.Answer}");
                                Assert.IsTrue(da.Answer.SameAs(again.Answer), $"{at}: 같은 입력 · 같은 씨 → 같은 답");
                                Assert.AreEqual(agentA.Simulations, 2 * agentB.Simulations, at);
                                Assert.AreEqual(n1, agentB.Nodes, at);
                                Assert.AreEqual(2 * n1, agentA.Nodes, at);
                                Assert.AreEqual(0, agentA.Mismatch, $"{at}: 트리와 판이 어긋났다");
                                Assert.AreEqual(0, agentA.Stuck, at);
                                Assert.Greater(agentA.Simulations, 0, at);
                                Assert.IsTrue(legal.Exists(x => x.SameAs(da.Answer)), "AI 답은 합법 답");
                                if (kind == SearchKind.Tree)
                                {
                                    nodes += agentB.Nodes;
                                    answer = da.Answer;
                                }
                            }
                            decisions++;
                            if (!galactic) seatDecisions++;
                        }
                        Assert.IsTrue(Rollout.Step(a, ctx, c, answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {answer}");
                    }
                }
            Report($"M8 탐색 엿보기 없음: 결정 {decisions}(플레이어 쪽 {seatDecisions}) · 트리 노드 {nodes}");
            Assert.GreaterOrEqual(decisions, 15, "견준 결정 수");
            Assert.GreaterOrEqual(seatDecisions, 3, "플레이어 쪽 결정 수");
        }

        /// <summary>트리 탐색의 결정 지문(합성 장면 셋 × 시드 3, 갤럭시단 선택마다 굴리기 96 · 깊이 8): 선택 종류 · 답 · 굴리기 수 · 노드 수를 섞은 값 — dotnet · Unity Mono가 같아야 한다. 탐색을 일부러 바꾸는 묶음은 새로 재어 고친다.</summary>
        const ulong TreeDigest = 0x382FEAA9936121C9UL;   // 결정 197: 공통 난수 · 원칙 답 문턱(옛 값 0x6617EE46A9334EAA)

        [Test]
        public void M8_탐색_트리결정지문()
        {
            ulong digest = 14695981039346656037UL;
            int decisions = 0;
            var legal = new List<ChoiceAnswer>();
            var agent = Searcher(SearchKind.Tree);
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    for (int step = 0, asked = 0; step < 400 && asked < 6; step++)
                    {
                        var c = s.Pending.Find(x => !x.Answered);
                        if (c == null || (s.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(s, ctx, c, legal);
                        var answer = PrincipleAgent.Choose(s, ctx, c, legal).Answer;
                        if (c.Seat == SeatId.Galactic && legal.Count > 1)
                        {
                            asked++;
                            var view = Engine.ViewFor(s, SeatId.Observer);
                            long sims = agent.Simulations, nodes = agent.Nodes;
                            answer = agent.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() },
                                new AgentBudget { MaxSimulations = 96, Seed = 99 + (ulong)c.Id }).Answer;
                            foreach (long v in new long[] { (long)c.Kind, (long)answer.Kind, answer.Target, answer.Arg, answer.Values.Count, agent.Simulations - sims, agent.Nodes - nodes })
                                digest = unchecked((digest ^ (ulong)v) * 1099511628211UL);
                            decisions++;
                        }
                        Assert.IsTrue(Rollout.Step(s, ctx, c, answer));
                    }
                }
            Report($"M8 탐색 트리 결정 지문: 결정 {decisions} · 노드 {agent.Nodes} · 0x{digest:X16}");
            Assert.Greater(decisions, 20);
            Assert.AreEqual(0, agent.Mismatch);
            Assert.AreEqual(TreeDigest, digest, "트리 탐색의 결정이 달라졌다(엔진마다 같아야 한다)");
        }

        // ── 상대 모델 ──

        [Test]
        public void M8_탐색_상대모델은운명카드와교체를쓰고원칙과다르다()
        {
            // 옛 정본 앞 30장면의 갤럭시단 배틀을 양쪽 상대 모델(Rollout.Rival)로: 좌석이 디아루가를 내고 교체 창에서 바꾼 적이 있다. 가상 쪽(갤럭시단) 답은 늘 원칙과 같다.
            int fates = 0, switches = 0, galactic = 0, differs = 0;
            RolloutPolicy watch = (s, cx, c, legal) =>
            {
                var a = Rollout.Rival(s, cx, c, legal);
                var p = Rollout.Principle(s, cx, c, legal);
                if (c.Seat == SeatId.Galactic)
                {
                    galactic++;
                    Assert.IsTrue(a.SameAs(p), "갤럭시단 쪽은 원칙 그대로");
                }
                else if (!a.SameAs(p))
                {
                    differs++;
                    if (a.Kind == AnswerKind.PlayFate) fates++;
                    if (c.Kind == ChoiceKind.SwitchWindow) switches++;
                }
                Assert.IsTrue(legal.Exists(x => x.SameAs(a)));
                return a;
            };
            int ended = 0;
            ForEachRealScene(ctx, 30, (game, i, s) =>
            {
                var r = Rollout.ToEnd(s.Clone(), ctx, watch);
                if (r.Ended) ended++;
            });
            Report($"M8 탐색 상대 모델: 배틀 30 끝남 {ended} · 원칙과 다른 답 {differs}(디아루가 {fates} · 교체 {switches}) · 갤럭시단 답 {galactic}");
            Assert.AreEqual(30, ended);
            Assert.Greater(fates, 0, "디아루가를 냈다");
            Assert.Greater(switches, 0, "교체했다");
        }

        [Test, Category("Slow")]
        public void M8_탐색_상대모델무게맞추기()
        {
            // 상대 모델 굴리기(Rollout.Rival)로 무게를 다시 맞춘다(판세 평가 맞추기와 같은 셈 — 굴리기 정책만 다르다). 관문은 줄인 판, 정본은 관문 밖 SINNOH_EVAL_FIT_SCENES=1000 — 맞춘 표를 BattleEval.Rival에 옮긴다.
            var sw = Stopwatch.StartNew();
            var sums = Collect(FitScenes, BattleEval.Rival, 0, Rollout.Rival);
            var w = Solve(sums, out int r2, out int r2Half);
            var agree = new List<string>();
            for (int d = 0; d < EvalDepths.Length; d++) agree.Add($"깊이 {EvalDepths[d]} {BenchResult.Percent(sums.Decisions > 0 ? sums.Agree[d] * 1000 / sums.Decisions : 0)}");
            Report($"M8 탐색 상대 모델 맞추기: 장면 {sums.Scenes} · 결정 {sums.Decisions} · 굴리기 {sums.Rollouts}(멈춤 {sums.Stuck}) · 줄 {sums.Rows}(갤 승 평균 {BenchResult.Percent(sums.Rows > 0 ? 500 + sums.Ysum / sums.Rows : 0)}) · R² {r2}‰(500 기준 {r2Half}‰) · {sw.ElapsedMilliseconds}ms · 자료 지문 0x{sums.Digest:X16}");
            Report($"M8 탐색 상대 모델 맞춘 무게: {Table(w)}");
            Report($"M8 탐색 상대 모델 맞춘 무게(C#): {{ {string.Join(", ", w)} }}");
            Report($"M8 탐색 상대 모델 지금 무게(Rival): {Table(BattleEval.Rival)} — 끝까지 굴리기 답과 같음: {string.Join(" · ", agree)} · 결정 자리 평가와 PIMC 승률 차 평균 {(sums.RootN > 0 ? sums.RootErr / sums.RootN : 0)}‰({sums.RootN})");
            Assert.AreEqual(0, sums.Stuck);
            Assert.Greater(sums.Rows, 0);
        }

        // ── 시험대: 옛 정본 네 대진(새 탐색) ──

        [Test, Category("Slow")]
        public void M8_시험대탐색_네대진()
        {
            // 옛 정본(운영 규칙 끔) 앞 SearchScenes 장면 × 네 대진(갤 원칙/새 탐색 × 플 원칙/새 탐색), 주사위 0 · 새 탐색 = SearchBench(굴리기 SearchSims · 깊이 EvalDepth).
            // 1단계 · 깊이 8 줄은 같은 장면 · 같은 씨의 plan-log 「M8 판세 평가」 숫자와 견준다(그 둘은 이 묶음에서 바뀌지 않았다 — 결정 지문). 결정당 시간은 갤럭시단 쪽(µs).
            var sw = Stopwatch.StartNew();
            Func<long> micros = () => sw.ElapsedTicks * 1000000 / Stopwatch.Frequency;
            int shard = 0, shards = 1;
            var sh = Environment.GetEnvironmentVariable("SINNOH_BENCH_SHARD");
            if (!string.IsNullOrEmpty(sh))
            {
                var parts = sh.Split('/');
                shard = int.Parse(parts[0]);
                shards = int.Parse(parts[1]);
            }
            const int Groups = 5; // 0..2 종류 · 3 전체 · 4 팽팽
            var res = new BenchResult[4, Groups];
            for (int m = 0; m < 4; m++)
                for (int g = 0; g < Groups; g++) res[m, g] = new BenchResult();
            var gal = new StrongAgent[4];
            var pl = new StrongAgent[4];
            // SINNOH_SEARCH_PLAYER=flat이면 플레이어 쪽 강한 AI = 1단계(끝까지 · 굴리기 BenchSims — 「M8 시험대 바로잡기」의 플 강한과 같은 AI)
            bool flatPlayer = Environment.GetEnvironmentVariable("SINNOH_SEARCH_PLAYER") == "flat";
            for (int m = 0; m < 4; m++)
            {
                if (m % 2 == 1) gal[m] = new StrongAgent { Search = SearchBench, Depth = EvalDepth };
                if (m >= 2) pl[m] = flatPlayer ? new StrongAgent() : new StrongAgent { Search = SearchBench, Depth = EvalDepth };
            }
            int scenes = 0, tight = 0, index = 0;
            var groups = new List<int>(3);
            ForEachRealScene(ctx, SearchScenes, (game, i, s) =>
            {
                if (index++ % shards != shard) return;
                int kind = KindOf(s);
                ulong id = game * 1000 + (ulong)i;
                int rate = PrincipleRate(s, game, id);
                groups.Clear();
                groups.Add(kind);
                groups.Add(3);
                if (rate >= GalacticBench.TightLow && rate <= GalacticBench.TightHigh)
                {
                    groups.Add(4);
                    tight++;
                }
                for (int m = 0; m < 4; m++)
                {
                    if (!BenchRows.Contains(m)) continue;
                    var one = new BenchResult();
                    PlayMatchup(m, s, game, id, 0, gal[m], pl[m], one, micros, SearchSims, flatPlayer ? BenchSims : SearchSims);
                    foreach (int g in groups) res[m, g].Add(one);
                }
                scenes++;
            });
            string[] names = { SceneKinds[0], SceneKinds[1], SceneKinds[2], "전체", "팽팽" };
            string[] rows = { "갤 원칙 · 플 원칙", "갤 새 · 플 원칙", "갤 원칙 · 플 새", "갤 새 · 플 새" };
#if UNITY_EDITOR
            const string where = "Unity Mono";
#else
            const string where = "dotnet";
#endif
            Report($"M8 탐색 시험대({where}): 옛 정본 장면 {scenes}{(shards > 1 ? $"(조각 {shard}/{shards})" : "")} · 팽팽 {tight} · 꼴 {SearchBench} · 굴리기 {SearchSims} · 깊이 {EvalDepth}{(flatPlayer ? $" · 플 강한 = 1단계(굴리기 {BenchSims})" : "")} · {sw.ElapsedMilliseconds}ms");
            for (int m = 0; m < 4; m++)
            {
                var parts = new List<string>();
                for (int g = 0; g < Groups; g++) parts.Add($"{names[g]} {res[m, g].Battles}: {BenchResult.Percent(res[m, g].WinPermille)}");
                var all = res[m, 3];
                string think = gal[m] == null ? "" : $" · 갤 결정 {all.Decisions} · 결정당 {(all.Decisions > 0 ? all.ThinkMs / all.Decisions : 0)}µs(최대 {all.MaxThinkMs / 1000}ms) · 굴리기 {gal[m].Simulations}(결정당 {(all.Decisions > 0 ? gal[m].Simulations / all.Decisions : 0)} · 끊음 {gal[m].Cuts} · 노드 {gal[m].Nodes})";
                string seat = pl[m] == null ? "" : $" · 플 굴리기 {pl[m].Simulations}(노드 {pl[m].Nodes})";
                Report($"M8 탐색 시험대 [{rows[m]}] {string.Join(" · ", parts)} · 플레이어 남은 체력 {BenchResult.Percent(all.PlayerHpPermille)}{think}{seat}");
                // 조각을 합치는 원자료(한 줄 — 무리마다 배틀 · 갤 승 · 비김 · 결정 · 시간 µs · 최대 µs)
                var raw = new List<string>();
                for (int g = 0; g < Groups; g++) raw.Add($"{res[m, g].Battles},{res[m, g].GalacticWins},{res[m, g].Draws},{res[m, g].Decisions},{res[m, g].ThinkMs},{res[m, g].MaxThinkMs}");
                Report($"M8 탐색 원자료 {m} {string.Join(" ", raw)} sims {gal[m]?.Simulations ?? 0} {pl[m]?.Simulations ?? 0}");
            }
            for (int m = 0; m < 4; m++)
            {
                if (!BenchRows.Contains(m)) continue;
                Assert.AreEqual(0, res[m, 3].Stuck, rows[m]);
                Assert.AreEqual(scenes, res[m, 3].Battles);
                if (gal[m] != null)
                {
                    Assert.AreEqual(0, gal[m].Stuck);
                    Assert.AreEqual(0, gal[m].Mismatch);
                }
                if (pl[m] != null)
                {
                    Assert.AreEqual(0, pl[m].Stuck);
                    Assert.AreEqual(0, pl[m].Mismatch);
                }
            }
            Assert.Greater(scenes, 0);
        }
    }
}
