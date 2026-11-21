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
    /// plan M8 「판세 평가」(01 §9-5, 결정 108 ②): 판세 평가 BattleEval · 무게 맞추기(무거운 탐색의 굴리기에 맞춤 — 손으로 정하지 않는다) · 깊이 제한 굴리기(StrongAgent.Depth) ·
    /// 시험대(끝까지 vs 깊이 제한 네 대진) · 평가로 다시 잰 세기 비(M8-9 견줌).
    /// </summary>
    public partial class StrongAiTests
    {
        /// <summary>시험대의 깊이 제한(배틀 턴 — 맞추기 보고에서 끝까지 굴리기 답과 가장 많이 같은 8, [가정 M8-15]). SINNOH_EVAL_DEPTH로 바꾼다.</summary>
        public static readonly int EvalDepth = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_EVAL_DEPTH"), out var ed) && ed > 0 ? ed : 8;
        /// <summary>견주는 깊이(맞추기 보고 — 깊이 제한 답이 끝까지 굴리기 답과 같은 비율).</summary>
        static readonly int[] EvalDepths = { 1, 2, 4, 8 };
        /// <summary>굴리기 하나에서 기록하는 배틀 턴(1~8 — 깊이 제한이 끊을 자리).</summary>
        const int RecordTurns = 8;

        /// <summary>무게 맞추기의 장면 수(옛 정본 앞부터). 관문은 줄인 판, 정본 무게는 관문 밖에서 SINNOH_EVAL_FIT_SCENES=1000.</summary>
#if UNITY_EDITOR
        public static readonly int FitScenes = 2;
#else
        public static readonly int FitScenes = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_EVAL_FIT_SCENES"), out var fs) && fs > 0 ? fs : 3;
#endif
        /// <summary>평가 시험대(끝까지 vs 깊이 제한 · 세기 비 다시 재기)의 장면 수: 관문은 줄인 판(dotnet 6 · Unity 3), SINNOH_BENCH_SCENES를 주면 그 수.</summary>
#if UNITY_EDITOR
        public static readonly int EvalScenes = 3;
#else
        public static readonly int EvalScenes = Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES") != null ? BenchScenes : 6;
#endif

        // ── 판 고르기 ──

        /// <summary>배틀 행동 선택(두 쪽이 다 나온 첫 자리)까지 원칙 AI로 민다(장면의 명단 · 출전).</summary>
        void AdvanceToAction(GameState s, RolloutPolicy seat = null)
        {
            var legal = new List<ChoiceAnswer>();
            for (int step = 0; step < 200; step++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "열린 선택");
                if (s.Battle != null && c.Kind == ChoiceKind.BattleAction) return;
                legal.Clear();
                Engine.Legal(s, ctx, c, legal);
                var a = c.Seat == SeatId.Galactic || seat == null ? PrincipleAgent.Choose(s, ctx, c, legal).Answer : seat(s, ctx, c, legal);
                Assert.IsTrue(Rollout.Step(s, ctx, c, a));
            }
            Assert.Fail("배틀 행동 선택까지 가지 못했다");
        }

        /// <summary>그 쪽 명단을 출전 포켓몬 체력 1만 남기고 모두 쓰러뜨린다(뻔히 진 쪽).</summary>
        void Rout(GameState s, int k)
        {
            var sd = s.Battle.Sides[k];
            foreach (int id in sd.Roster)
            {
                var p = s.Pokemon[id];
                if (p.Def < 0) continue;
                p.Damage = PokemonRules.MaxHp(ctx, p) - (id == sd.Active ? 1 : 0);
            }
        }

        /// <summary>실전 장면(옛 정본 앞 n)의 갤럭시단 배틀을 원칙 AI · 좌석 봇으로 돌리며 배틀 중 열린 선택마다 판 사본을 모은다.</summary>
        List<GameState> BattleStates(int scenes)
        {
            var list = new List<GameState>();
            var legal = new List<ChoiceAnswer>();
            ForEachRealScene(ctx, scenes, (game, i, s0) =>
            {
                var s = s0.Clone();
                var bot = new Playout.Bot(game);
                var rng = new Pcg32(game * 1000 + (ulong)i, 0xB07);
                for (int step = 0; step < Rollout.MaxSteps; step++)
                {
                    var c = s.Pending.Find(x => !x.Answered);
                    if (c == null || (s.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                    if (s.Battle != null) list.Add(s.Clone());
                    legal.Clear();
                    Engine.Legal(s, ctx, c, legal);
                    var a = c.Seat == SeatId.Galactic ? PrincipleAgent.Choose(s, ctx, c, legal).Answer : bot.Answer(s, ctx, c, rng);
                    Assert.IsTrue(Rollout.Step(s, ctx, c, a));
                }
            });
            return list;
        }

        // ── 평가 시험 ──

        [Test]
        public void M8_평가_한쪽이뻔히앞선장면에서부호가맞다()
        {
            // 합성 장면 셋 × 시드 3: 배틀 행동 선택까지 민 판을 기준으로, 플레이어 명단을 출전 포켓몬 체력 1만 남기면 갤럭시단 평가가 오르고(500 넘음) 갤럭시단 명단을 그렇게 하면 내린다(500 밑)
            var eval = new BattleEval();
            int n = 0;
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    AdvanceToAction(s);
                    int even = eval.WinPermille(s, ctx, 1);
                    var lost = s.Clone();
                    Rout(lost, 0);
                    var won = s.Clone();
                    Rout(won, 1);
                    int up = eval.WinPermille(lost, ctx, 1), down = eval.WinPermille(won, ctx, 1);
                    string at = $"{scene} 시드 {seed}: 기준 {even} · 플레이어 무너짐 {up} · 갤럭시단 무너짐 {down}";
                    Assert.Greater(up, 500, at);
                    Assert.Greater(up, even, at);
                    Assert.Less(down, 500, at);
                    Assert.Less(down, even, at);
                    Assert.AreEqual(1000 - up, eval.WinPermille(lost, ctx, 0), at);
                    n++;
                }
            Assert.AreEqual(9, n);
        }

        [Test]
        public void M8_평가_갤럭시단과플레이어를뒤집으면대칭()
        {
            // 모든 항목이 「이 쪽 − 상대 쪽」: 쪽을 뒤집으면 항목은 부호만, 이길 확률은 1000 − 그 값(맞춘 무게 · 아무 무게 모두). 실전 장면 배틀의 열린 선택마다 + 합성 장면
            var states = BattleStates(6);
            foreach (var scene in Scenes)
            {
                var s = GalacticBench.Scene(ctx, scene, 1);
                AdvanceToAction(s);
                states.Add(s);
            }
            var eval = new BattleEval();
            var f0 = new int[BattleEval.FeatureCount];
            var f1 = new int[BattleEval.FeatureCount];
            var rng = new Pcg32(808, 3);
            var odd = new int[BattleEval.FeatureCount];
            for (int i = 0; i < odd.Length; i++) odd[i] = rng.Next(20001) - 10000;
            var moved = new int[BattleEval.FeatureCount];
            foreach (var s in states)
            {
                eval.Features(s, ctx, 0, f0);
                eval.Features(s, ctx, 1, f1);
                for (int i = 0; i < BattleEval.FeatureCount; i++)
                {
                    Assert.AreEqual(-f1[i], f0[i], $"{BattleEval.Names[i]}");
                    if (f0[i] != 0) moved[i]++;
                }
                Assert.AreEqual(1000, eval.WinPermille(s, ctx, 0) + eval.WinPermille(s, ctx, 1));
                Assert.AreEqual(1000, eval.WinPermille(s, ctx, 0, odd) + eval.WinPermille(s, ctx, 1, odd));
            }
            var parts = new List<string>();
            for (int i = 0; i < BattleEval.FeatureCount; i++) parts.Add($"{BattleEval.Names[i]} {moved[i]}");
            Report($"M8 평가 대칭: 판 {states.Count} · 0이 아닌 항목 수 — {string.Join(" · ", parts)}");
            Assert.Greater(states.Count, 100);
            Assert.Greater(moved[(int)EvalFeature.HpPermille], 0);
            Assert.Greater(moved[(int)EvalFeature.Fate], 0, "실전 장면에는 운명 손패가 있다");
        }

        [Test]
        public void M8_평가_결정적이고엿보기없음_깊이제한결정도같다()
        {
            // 숨은 정보만 다른 두 원본(운명 손패 · 덱 순서 · 숨겨진 장소 · 게임 난수): 평가(원본 · 보기 · 채운 판)가 같고, 깊이 제한 강한 AI의 결정도 같다(같은 AI 씨). 같은 입력을 두 번 물으면 같은 답.
            var eval = new BattleEval();
            int decisions = 0, cuts = 0;
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
                    for (int step = 0, asked = 0; step < 400 && asked < 3; step++)
                    {
                        var c = a.Pending.Find(x => !x.Answered);
                        if (c == null || (a.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(a, ctx, c, legal);
                        ChoiceAnswer answer = PrincipleAgent.Choose(a, ctx, c, legal).Answer;
                        if (c.Seat == SeatId.Galactic && legal.Count > 1)
                        {
                            asked++;
                            var b = a.Clone();
                            Scramble(b, seed * 31 + (ulong)step);
                            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b));
                            var va = Engine.ViewFor(a, SeatId.Observer);
                            var vb = Engine.ViewFor(b, SeatId.Observer);
                            if (a.Battle != null)
                            {
                                int e = eval.WinPermille(a, ctx, 1);
                                Assert.AreEqual(e, eval.WinPermille(b, ctx, 1), "원본 둘의 평가");
                                Assert.AreEqual(e, eval.WinPermille(va, ctx, 1), "보기의 평가 = 원본의 평가(공개 칸만)");
                                Assert.AreEqual(e, eval.WinPermille(vb, ctx, 1));
                                Assert.AreEqual(e, eval.WinPermille(Determinizer.Fill(va, ctx, SampledRng.ForSample(5, 1)), ctx, 1), "채운 판의 평가");
                            }
                            var budget = new AgentBudget { MaxSimulations = 48, Seed = 777 + (ulong)c.Id };
                            var agentA = new StrongAgent { Depth = 2 };
                            var agentB = new StrongAgent { Depth = 2 };
                            var da = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            var db = agentB.Decide(new AgentInput { View = vb, Choice = vb.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            var again = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            Assert.IsTrue(da.Answer.SameAs(db.Answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub}: {da.Answer} ≠ {db.Answer}");
                            Assert.IsTrue(da.Answer.SameAs(again.Answer), "같은 입력 · 같은 씨 → 같은 답");
                            Assert.AreEqual(agentA.Cuts, 2 * agentB.Cuts);
                            Assert.AreEqual(0, agentA.Stuck);
                            Assert.IsTrue(legal.Exists(x => x.SameAs(da.Answer)), "AI 답은 합법 답");
                            cuts += (int)agentB.Cuts;
                            decisions++;
                            answer = da.Answer;
                        }
                        Assert.IsTrue(Rollout.Step(a, ctx, c, answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {answer}");
                    }
                }
            Assert.GreaterOrEqual(decisions, 15, "견준 결정 수");
            Assert.Greater(cuts, 0, "깊이 제한에서 끊은 굴리기");
        }

        [Test]
        public void M8_평가_깊이제한굴리기는끝까지굴리기의앞부분이다()
        {
            // 끊은 굴리기 = 끝까지 굴리기를 배틀 턴 n번 뒤에서 멈춘 것: 끊은 판에서 마저 굴리면 끝까지 굴린 결과 · 걸음 수 · 마지막 판과 같다. 끊기 전에 끝난 배틀은 그대로 끝.
            var root = new GameState();
            var full = new GameState();
            var cut = new GameState();
            var legal = new List<ChoiceAnswer>();
            int compared = 0, cutCount = 0;
            var scenes = new List<GameState>();
            foreach (var scene in Scenes) scenes.Add(GalacticBench.Scene(ctx, scene, 2));
            ForEachRealScene(ctx, 8, (g, i, s) => scenes.Add(s));
            using (Rig.Use(null))
                foreach (var s in scenes)
                {
                    var view = Engine.ViewFor(s, SeatId.Observer);
                    var c = view.Pending.Find(x => !x.Answered);
                    legal.Clear();
                    Engine.Legal(view, ctx, c, legal);
                    for (int k = 0; k < 2; k++)
                    {
                        Determinizer.FillInto(root, view, ctx, SampledRng.ForSample(31, k));
                        foreach (var a in legal)
                        {
                            full.CopyFrom(root);
                            var rf = Rollout.ToEnd(full, ctx, Rollout.Principle, c.Id, a);
                            Assert.IsTrue(rf.Ended);
                            foreach (int d in EvalDepths)
                            {
                                cut.CopyFrom(root);
                                var rc = Rollout.ToEnd(cut, ctx, Rollout.Principle, c.Id, a, Rollout.MaxSteps, false, d);
                                Assert.IsFalse(rc.Stuck);
                                if (rc.Ended)
                                {
                                    Assert.AreEqual(Line(rf), Line(rc), "끊기 전에 끝남");
                                    continue;
                                }
                                Assert.IsTrue(rc.Cut);
                                Assert.GreaterOrEqual(cut.Battle.Turn, Rollout.BaseTurn(root) + d, "배틀 턴 n번 뒤에 끊었다");
                                Assert.Less(rc.Steps, rf.Steps);
                                cutCount++;
                                var rest = Rollout.ToEnd(cut, ctx, Rollout.Principle);
                                Assert.IsTrue(rest.Ended);
                                Assert.AreEqual(rf.Steps, rc.Steps + rest.Steps, "걸음 수");
                                Assert.AreEqual(rf.Winner, rest.Winner);
                                Assert.AreEqual(rf.Hp[0], rest.Hp[0]);
                                Assert.AreEqual(StateHash.Of(full), StateHash.Of(cut), "마저 굴린 판 = 끝까지 굴린 판");
                                compared++;
                            }
                        }
                    }
                }
            Report($"M8 깊이 제한 굴리기: 장면 {scenes.Count} · 끊은 굴리기 {cutCount}");
            Assert.Greater(compared, 100);
        }

        // ── 무게 맞추기(결정 108 ② — 무거운 탐색에 맞춘다) ──

        /// <summary>굴리기 하나에서 배틀 턴마다 판의 항목을 적는 정책(원칙 — Rollout.Principle과 같은 답). 깊이 d에서 끊을 판 = First ≤ d ≤ Last인 줄.</summary>
        sealed class EvalRecorder
        {
            readonly BattleEval eval = new BattleEval();
            readonly List<int[]> pool = new List<int[]>();
            public readonly List<int> Hp0 = new List<int>(), First = new List<int>(), Last = new List<int>();
            public int Count, Base, Next;
            public readonly RolloutPolicy Policy;

            public EvalRecorder(RolloutPolicy basis = null)
            {
                Policy = (s, cx, c, legal) =>
                {
                    var b = s.Battle;
                    if (b != null && Next <= RecordTurns && b.Turn >= Base + Next)
                    {
                        if (pool.Count == Count) pool.Add(new int[BattleEval.FeatureCount]);
                        eval.Features(s, cx, 1, pool[Count]);
                        Hp0.Add(SeatHpPermille(s, cx, 0));
                        First.Add(Next);
                        while (Next <= RecordTurns && b.Turn >= Base + Next) Next++;
                        Last.Add(Next - 1);
                        Count++;
                    }
                    return basis != null ? basis(s, cx, c, legal) : PrincipleAgent.Choose(s, cx, c, legal).Answer;
                };
            }

            public int[] Row(int i) => pool[i];

            public void Begin(GameState sim)
            {
                Base = Rollout.BaseTurn(sim);
                Next = 1;
                Count = 0;
                Hp0.Clear();
                First.Clear();
                Last.Clear();
            }

            /// <summary>깊이 d에서 끊을 줄(없으면 −1 — 그 전에 배틀이 끝났다).</summary>
            public int At(int d)
            {
                for (int i = 0; i < Count; i++)
                    if (First[i] <= d && d <= Last[i]) return i;
                return -1;
            }
        }

        /// <summary>그 쪽 좌석 파티의 남은 체력(‰) — Rollout이 배틀 끝에 읽는 값과 같은 셈(가상 쪽은 0).</summary>
        static int SeatHpPermille(GameState s, Ctx cx, int k)
        {
            int owner = s.Battle.Sides[k].Owner;
            if (owner < 0 || owner >= s.SeatCount) return 0;
            int hp = 0, max = 0;
            foreach (int id in s.Seats[owner].Party)
            {
                if (id < 0 || s.Pokemon[id].Def < 0) continue;
                hp += PokemonRules.Hp(cx, s.Pokemon[id]);
                max += PokemonRules.MaxHp(cx, s.Pokemon[id]);
            }
            return max > 0 ? hp * 1000 / max : 0;
        }

        /// <summary>맞추기 모음: 정규 방정식(정수로 쌓는다 — 순서 · 값이 같으면 같다) · 결정 견줌.</summary>
        sealed class FitSums
        {
            public readonly long[,] Xtx = new long[BattleEval.FeatureCount, BattleEval.FeatureCount];
            public readonly long[] Xty = new long[BattleEval.FeatureCount];
            public long Yy, Ysum, Rows, Rollouts;
            public int Scenes, Decisions, Stuck, Checked;
            public readonly int[] Agree = new int[EvalDepths.Length];
            public long RootErr;
            public int RootN;
            public ulong Digest = 14695981039346656037UL;
            /// <summary>사람 기록(결정 113): 넣은 사람 장면 · 사람 답(합법 답 둘 이상) · 그 가운데 원칙 답 · 탐색 답(플레이어 쪽 1단계)과 같은 수.</summary>
            public int HumanScenes, HumanMoves, HumanSamePrinciple, HumanSameSearch;
        }

        /// <summary>
        /// 무게 맞추기의 자료(옛 정본 앞 scenes): 장면의 배틀을 원칙 AI · 좌석 봇으로 돌리며(대진 「갤 원칙 · 플 원칙」 주사위 0과 같은 판) 갤럭시단의 결정(합법 답 둘 이상)마다
        /// 강한 AI 1단계와 같은 PIMC(관전자 보기 · 표본 = min(16, 굴리기 BenchSims ÷ 답 수) · 같은 AI 씨)를 돌린다. 굴리기마다 배틀 턴 1~8의 판 항목(갤럭시단 쪽) → 그 굴리기의 갤럭시단 승패(이김 1000 · 비김 500 · 짐 0)를 한 줄씩.
        /// weights = 견줄 무게: 깊이 d마다 「끊은 판의 평가 + 피해 몫」으로 고른 답이 끝까지 굴리기의 답과 같은 비율(같은 굴리기의 앞부분이라 따로 굴리지 않는다).
        /// check = 앞 몇 결정은 실제 StrongAgent(끝까지 · 깊이 EvalDepth)의 답과 이 셈의 답이 같은지 본다(weights가 Default일 때).
        /// human = 사람 장면(결정 113 — <see cref="HumanRecord"/>, null · 비면 지금 그대로): 봇 장면 뒤에 사람 배틀마다 저장의 명령을 그대로 다시 적용하며(사람이 실제로 간 길)
        /// 갤럭시단 결정마다 같은 자료를 모으고, 사람 답(합법 답 둘 이상)마다 원칙 답 · 탐색 답(플레이어 쪽 강한 AI 1단계 — 그 좌석 보기)과 같은지 센다.
        /// </summary>
        FitSums Collect(int scenes, int[] weights, int check, RolloutPolicy basis = null, IReadOnlyList<HumanBattle> human = null)
        {
            var sums = new FitSums();
            var rec = new EvalRecorder(basis);
            var eval = new BattleEval();
            var root = new GameState();
            var sim = new GameState();
            var legal = new List<ChoiceAnswer>();
            var vl = new List<ChoiceAnswer>();
            var noLog = new List<GameEvent>();
            int n = BattleEval.FeatureCount;
            // 갤럭시단 결정 하나(합법 답 둘 이상)의 자료 — id = AI 씨에 섞는 장면 값(GalacticBench.Play와 같은 셈)
            void Galactic(GameState g, PendingChoice c, ulong id)
            {
                var view = Engine.ViewFor(g, SeatId.Observer);
                var vc = view.Pending.Find(x => x.Id == c.Id);
                vl.Clear();
                Engine.Legal(view, ctx, vc, vl);
                var principle = PrincipleAgent.Choose(view, ctx, vc, vl).Answer;
                ulong seed = SampledRng.Mix(1UL ^ (id << 20) ^ (ulong)c.Id); // GalacticBench.Play의 AI 씨(aiSeed 1)
                int samples = Math.Max(1, Math.Min(StrongAgent.Samples, BenchSims / vl.Count));
                var full = new long[vl.Count];
                var win = new long[vl.Count];
                var count = new int[vl.Count];
                var depth = new long[EvalDepths.Length, vl.Count];
                for (int k = 0; k < samples; k++)
                {
                    Determinizer.FillInto(root, view, ctx, SampledRng.ForSample(seed, k));
                    for (int i = 0; i < vl.Count; i++)
                    {
                        sim.CopyFrom(root);
                        rec.Begin(sim);
                        var r = Rollout.ToEnd(sim, ctx, rec.Policy, vc.Id, vl[i]);
                        sums.Rollouts++;
                        if (!r.Ended)
                        {
                            sums.Stuck++;
                            continue;
                        }
                        int y = r.Winner == 1 ? 1000 : r.Winner < 0 ? 500 : 0;
                        int sc = y + StrongAgent.DamageWeight * (1000 - r.HpPermille(0)) / 1000;
                        full[i] += sc;
                        win[i] += y;
                        count[i]++;
                        for (int row = 0; row < rec.Count; row++)
                        {
                            var f = rec.Row(row);
                            long t = y - 500;
                            for (int p = 0; p < n; p++)
                            {
                                if (f[p] == 0) continue;
                                sums.Xty[p] += f[p] * t;
                                for (int q = 0; q < n; q++) sums.Xtx[p, q] += (long)f[p] * f[q];
                            }
                            sums.Yy += t * t;
                            sums.Ysum += t;
                            sums.Rows++;
                        }
                        for (int d = 0; d < EvalDepths.Length; d++)
                        {
                            int at = rec.At(EvalDepths[d]);
                            depth[d, i] += at < 0 ? sc : BattleEval.WinPermille(rec.Row(at), weights) + StrongAgent.DamageWeight * (1000 - rec.Hp0[at]) / 1000;
                        }
                    }
                }
                int pick = Pimc(full, count, vl, principle);
                sums.Decisions++;
                sums.Digest = unchecked((sums.Digest ^ (ulong)pick ^ ((ulong)full[pick] << 16)) * 1099511628211UL);
                for (int d = 0; d < EvalDepths.Length; d++)
                {
                    var row = new long[vl.Count];
                    for (int i = 0; i < vl.Count; i++) row[i] = depth[d, i];
                    if (Pimc(row, count, vl, principle) == pick) sums.Agree[d]++;
                }
                if (g.Battle != null && count[pick] > 0)
                {
                    sums.RootErr += Math.Abs(eval.WinPermille(view, ctx, 1, weights) - win[pick] / count[pick]);
                    sums.RootN++;
                }
                if (sums.Checked < check && weights == BattleEval.Default && basis == null)
                {
                    // 이 셈 = 실제 StrongAgent(끝까지 · 깊이 EvalDepth)의 답 — 평균이 가장 큰 답(Pimc)만 따라 하므로 원칙 답 문턱(결정 197 Margin)은 끈다
                    var budget = new AgentBudget { MaxSimulations = BenchSims, Seed = seed };
                    var input = new AgentInput { View = view, Choice = vc, Ctx = ctx, PublicLog = noLog };
                    Assert.IsTrue(vl[pick].SameAs(new StrongAgent { Margin = 0 }.Decide(input, budget).Answer), $"판 {id} 선택 {c.Id}: 끝까지 굴리기 답");
                    int di = Array.IndexOf(EvalDepths, EvalDepth);
                    if (di >= 0)
                    {
                        var row = new long[vl.Count];
                        for (int i = 0; i < vl.Count; i++) row[i] = depth[di, i];
                        Assert.IsTrue(vl[Pimc(row, count, vl, principle)].SameAs(new StrongAgent { Depth = EvalDepth, Margin = 0 }.Decide(input, budget).Answer), $"판 {id} 선택 {c.Id}: 깊이 {EvalDepth} 답");
                    }
                    sums.Checked++;
                }
            }

            // 사람 답 하나(합법 답 둘 이상): 그 좌석 보기의 원칙 답 · 탐색 답(플레이어 쪽 1단계 — 시험대의 「플 강한」)과 같은가
            var seatAgent = new StrongAgent();
            void HumanMove(GameState g, PendingChoice c, ChoiceAnswer said, ulong id)
            {
                var view = Engine.ViewFor(g, c.Seat);
                var vc = view.Pending.Find(x => x.Id == c.Id);
                vl.Clear();
                Engine.Legal(view, ctx, vc, vl);
                if (vl.Count < 2) return;
                sums.HumanMoves++;
                if (PrincipleAgent.Choose(view, ctx, vc, vl).Answer.SameAs(said)) sums.HumanSamePrinciple++;
                var input = new AgentInput { View = view, Choice = vc, Ctx = ctx, PublicLog = noLog };
                var budget = new AgentBudget { MaxSimulations = BenchSims, Seed = SampledRng.Mix(2UL ^ (id << 20) ^ (ulong)c.Id) }; // GalacticBench.SeatAgent의 AI 씨(aiSeed 2)
                if (seatAgent.Decide(input, budget).Answer.SameAs(said)) sums.HumanSameSearch++;
            }

            using (Rig.Use(null))
            {
                ForEachRealScene(ctx, scenes, (game, idx, s0) =>
                {
                    sums.Scenes++;
                    ulong id = game * 1000 + (ulong)idx;
                    var g = s0.Clone();
                    var bot = new Playout.Bot(game);
                    var rng = new Pcg32(id, 0xB07);
                    for (int step = 0; step < Rollout.MaxSteps; step++)
                    {
                        var c = g.Pending.Find(x => !x.Answered);
                        if (c == null || (g.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(g, ctx, c, legal);
                        ChoiceAnswer answer;
                        if (c.Seat != SeatId.Galactic) answer = bot.Answer(g, ctx, c, rng);
                        else
                        {
                            answer = PrincipleAgent.Choose(g, ctx, c, legal).Answer;
                            if (legal.Count > 1) Galactic(g, c, id);
                        }
                        Assert.IsTrue(Rollout.Step(g, ctx, c, answer));
                    }
                });
                for (int h = 0; human != null && h < human.Count; h++)
                {
                    // 사람 배틀: 저장의 명령을 그대로 다시 적용한다(사람이 실제로 간 길 — 갤럭시단 답도 그때 AI가 낸 것)
                    var hb = human[h];
                    sums.HumanScenes++;
                    ulong id = HumanSceneId(h);
                    var g = hb.Scene.Clone();
                    int next = 0;
                    for (int li = 0; li < hb.Lines.Count; li++)
                    {
                        var line = hb.Lines[li];
                        var c = line.Command.Kind == CommandKind.Answer ? g.Pending.Find(x => x.Id == line.Command.ChoiceId && !x.Answered) : null;
                        if (c != null && c.Seat == SeatId.Galactic)
                        {
                            legal.Clear();
                            Engine.Legal(g, ctx, c, legal);
                            if (legal.Count > 1) Galactic(g, c, id);
                        }
                        if (next < hb.Human.Count && hb.Human[next] == li)
                        {
                            next++;
                            if (c != null) HumanMove(g, c, line.Command.Answer, id);
                        }
                        Assert.IsTrue(Engine.Submit(g, ctx, line.Seat, line.Command, null).Accepted, $"사람 장면 {h} 명령 {line.Seq}: 다시 적용");
                    }
                }
            }
            return sums;
        }

        /// <summary>StrongAgent와 같은 고르기: 평균(엇곱)이 가장 큰 답, 같으면 Legal 순서 — 원칙 답이 가장 큰 평균과 같으면 원칙 답.</summary>
        static int Pimc(long[] sum, int[] count, List<ChoiceAnswer> legal, ChoiceAnswer principle)
        {
            int best = -1, same = -1;
            for (int i = 0; i < legal.Count; i++)
            {
                if (count[i] == 0) continue;
                if (legal[i].SameAs(principle)) same = i;
                if (best < 0 || sum[i] * count[best] > sum[best] * count[i]) best = i;
            }
            if (best < 0) return 0;
            return same >= 0 && sum[same] * count[best] >= sum[best] * count[same] ? same : best;
        }

        /// <summary>
        /// 최소제곱(상수항 없음 — 대칭) + 작은 능선(항목이 늘 0이면 무게 0): 무게 = β × WeightScale을 반올림.
        /// r2 = 설명한 몫(천분율, 줄 평균 기준 — 상수항이 없어 음수일 수 있다), r2Half = 500 기준(평가가 없을 때 = 늘 500과 견줌).
        /// </summary>
        static int[] Solve(FitSums sums, out int r2, out int r2Half)
        {
            int n = BattleEval.FeatureCount;
            var a = new double[n, n + 1];
            for (int p = 0; p < n; p++)
            {
                for (int q = 0; q < n; q++) a[p, q] = sums.Xtx[p, q];
                a[p, p] += 1 + sums.Xtx[p, p] * 1e-6;
                a[p, n] = sums.Xty[p];
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                for (int q = 0; q <= n; q++)
                {
                    double t = a[col, q];
                    a[col, q] = a[piv, q];
                    a[piv, q] = t;
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double m = a[r, col] / a[col, col];
                    if (m == 0) continue;
                    for (int q = col; q <= n; q++) a[r, q] -= m * a[col, q];
                }
            }
            var beta = new double[n];
            for (int p = 0; p < n; p++) beta[p] = a[p, n] / a[p, p];
            double sse = sums.Yy;
            for (int p = 0; p < n; p++)
            {
                sse -= 2 * beta[p] * sums.Xty[p];
                for (int q = 0; q < n; q++) sse += beta[p] * beta[q] * sums.Xtx[p, q];
            }
            double sst = sums.Rows > 0 ? sums.Yy - (double)sums.Ysum * sums.Ysum / sums.Rows : 0;
            r2 = sst > 0 ? (int)Math.Round(1000 * (1 - sse / sst)) : 0;
            r2Half = sums.Yy > 0 ? (int)Math.Round(1000 * (1 - sse / sums.Yy)) : 0;
            var w = new int[n];
            for (int p = 0; p < n; p++) w[p] = (int)Math.Round(beta[p] * BattleEval.WeightScale);
            return w;
        }

        static string Table(int[] w)
        {
            var parts = new List<string>();
            for (int i = 0; i < w.Length; i++) parts.Add($"{BattleEval.Names[i]} {w[i]}");
            return string.Join(" · ", parts);
        }

        [Test]
        public void M8_평가_무게맞추기는같은장면같은씨에같은무게()
        {
            // 결정성(결정 108 ② 맞추기 도구): 같은 장면 · 같은 AI 씨면 모은 자료 · 맞춘 무게가 같다
            var a = Collect(2, BattleEval.Default, 2);
            var b = Collect(2, BattleEval.Default, 0);
            var wa = Solve(a, out int ra, out _);
            var wb = Solve(b, out int rb, out _);
            Report($"M8 평가 맞추기(장면 2): 줄 {a.Rows} · 결정 {a.Decisions} · 무게 {Table(wa)} · R² {ra}‰");
            Assert.Greater(a.Rows, 100);
            Assert.AreEqual(a.Rows, b.Rows);
            Assert.AreEqual(a.Yy, b.Yy);
            Assert.AreEqual(a.Digest, b.Digest);
            Assert.AreEqual(Table(wa), Table(wb));
            Assert.AreEqual(ra, rb);
            Assert.AreEqual(0, a.Stuck);
            Assert.AreEqual(2, a.Checked, "실제 StrongAgent 답과 견줌");
        }

        [Test, Category("Slow")]
        public void M8_평가_무게맞추기_정본()
        {
            // 맞추기 도구(관문은 줄인 판 — 정본 무게는 관문 밖 SINNOH_EVAL_FIT_SCENES=1000): 옛 정본 앞 FitScenes 장면의 자료로 무게를 맞추고, 지금 무게(Default)로 깊이마다 답이 끝까지 굴리기와 같은 비율을 낸다.
            // 맞춘 표를 BattleEval.Default에 옮기면 같은 셋에서 다시 돌려 같은 표가 나온다(결정적).
            var sw = Stopwatch.StartNew();
            var sums = Collect(FitScenes, BattleEval.Default, 3);
            var w = Solve(sums, out int r2, out int r2Half);
            var agree = new List<string>();
            for (int d = 0; d < EvalDepths.Length; d++) agree.Add($"깊이 {EvalDepths[d]} {BenchResult.Percent(sums.Decisions > 0 ? sums.Agree[d] * 1000 / sums.Decisions : 0)}");
            Report($"M8 평가 맞추기: 장면 {sums.Scenes} · 결정 {sums.Decisions} · 굴리기 {sums.Rollouts}(멈춤 {sums.Stuck}) · 줄 {sums.Rows}(갤 승 평균 {BenchResult.Percent(sums.Rows > 0 ? 500 + sums.Ysum / sums.Rows : 0)}) · R² {r2}‰(500 기준 {r2Half}‰) · {sw.ElapsedMilliseconds}ms · 자료 지문 0x{sums.Digest:X16}");
            Report($"M8 평가 맞춘 무게: {Table(w)}");
            Report($"M8 평가 맞춘 무게(C#): {{ {string.Join(", ", w)} }}");
            Report($"M8 평가 지금 무게(Default): {Table(BattleEval.Default)} — 끝까지 굴리기 답과 같음: {string.Join(" · ", agree)} · 결정 자리 평가와 PIMC 승률 차 평균 {(sums.RootN > 0 ? sums.RootErr / sums.RootN : 0)}‰({sums.RootN})");
            Assert.AreEqual(0, sums.Stuck);
            Assert.Greater(sums.Rows, 0);
            Assert.AreEqual(Math.Min(3, sums.Decisions), sums.Checked);
        }

        // ── 시험대: 끝까지 vs 깊이 제한(옛 정본) ──

        static readonly string[] EvalMatchups =
        {
            "갤 원칙 · 플 원칙", "갤 강한(끝까지) · 플 원칙", "갤 강한(깊이) · 플 원칙", "갤 원칙 · 플 강한(끝까지)", "갤 원칙 · 플 강한(깊이)", "갤 강한(끝까지) · 플 강한(끝까지)", "갤 강한(깊이) · 플 강한(깊이)",
        };

        [Test, Category("Slow")]
        public void M8_시험대평가_끝까지대깊이제한_네대진()
        {
            // 옛 정본(운영 규칙 끔) 앞 EvalScenes 장면 × 네 대진 — 강한 AI는 끝까지 굴리기와 깊이 EvalDepth 둘 다(굴리기 BenchSims · 주사위 0). 결정당 시간은 갤럭시단 쪽(µs, 결정 = 합법 답 둘 이상).
            var sw = Stopwatch.StartNew();
            Func<long> micros = () => sw.ElapsedTicks * 1000000 / Stopwatch.Frequency;
            const int Groups = 5; // 0..2 종류 · 3 전체 · 4 팽팽
            int ms = EvalMatchups.Length;
            var res = new BenchResult[ms, Groups];
            for (int m = 0; m < ms; m++)
                for (int g = 0; g < Groups; g++) res[m, g] = new BenchResult();
            var gal = new StrongAgent[ms];
            var pl = new StrongAgent[ms];
            int[] code = { 0, 1, 1, 2, 2, 3, 3 };
            for (int m = 0; m < ms; m++)
            {
                bool deep = m == 2 || m == 4 || m == 6;
                if (code[m] % 2 == 1) gal[m] = new StrongAgent { Depth = deep ? EvalDepth : 0 };
                if (code[m] >= 2) pl[m] = new StrongAgent { Depth = deep ? EvalDepth : 0 };
            }
            int scenes = 0, tight = 0;
            var groups = new List<int>(3);
            ulong digest = 14695981039346656037UL;
            int games = ForEachRealScene(ctx, EvalScenes, (game, i, s) =>
            {
                int kind = KindOf(s);
                ulong id = game * 1000 + (ulong)i;
                digest = unchecked((digest ^ StateHash.Of(s)) * 1099511628211UL);
                int rate = PrincipleRate(s, game, id);
                groups.Clear();
                groups.Add(kind);
                groups.Add(3);
                if (rate >= GalacticBench.TightLow && rate <= GalacticBench.TightHigh)
                {
                    groups.Add(4);
                    tight++;
                }
                for (int m = 0; m < ms; m++)
                {
                    var one = new BenchResult();
                    PlayMatchup(code[m], s, game, id, 0, gal[m], pl[m], one, micros);
                    foreach (int g in groups) res[m, g].Add(one);
                }
                scenes++;
            });
            string[] names = { SceneKinds[0], SceneKinds[1], SceneKinds[2], "전체", "팽팽" };
            Report($"M8 평가 시험대: 옛 정본 {games}판(2인) · 장면 {scenes}{(scenes == CanonScenes ? "(1,000)" : "(줄인 판)")} · 장면 지문 0x{digest:X16} · 팽팽 {tight} · 굴리기 {BenchSims} · 깊이 {EvalDepth} · {sw.ElapsedMilliseconds}ms");
            for (int m = 0; m < ms; m++)
            {
                var parts = new List<string>();
                for (int g = 0; g < Groups; g++) parts.Add($"{names[g]} {res[m, g].Battles}: {BenchResult.Percent(res[m, g].WinPermille)}");
                var all = res[m, 3];
                string think = gal[m] == null ? "" : $" · 갤 결정 {all.Decisions} · 결정당 {(all.Decisions > 0 ? all.ThinkMs / all.Decisions : 0)}µs(최대 {all.MaxThinkMs / 1000}ms) · 굴리기 {gal[m].Simulations}(결정당 {(all.Decisions > 0 ? gal[m].Simulations / all.Decisions : 0)} · 끊음 {gal[m].Cuts})";
                string seat = pl[m] == null ? "" : $" · 플 굴리기 {pl[m].Simulations}(끊음 {pl[m].Cuts})";
                Report($"M8 평가 시험대 [{EvalMatchups[m]}] {string.Join(" · ", parts)} · 플레이어 남은 체력 {BenchResult.Percent(all.PlayerHpPermille)}{think}{seat}");
            }
            for (int m = 0; m < ms; m++)
            {
                Assert.AreEqual(0, res[m, 3].Stuck, EvalMatchups[m]);
                Assert.AreEqual(scenes, res[m, 3].Battles);
                if (gal[m] != null) Assert.AreEqual(0, gal[m].Stuck);
                if (pl[m] != null) Assert.AreEqual(0, pl[m].Stuck);
            }
            Assert.AreEqual(EvalScenes, scenes);
            Assert.Greater(gal[2].Cuts, 0, "깊이 제한이 끊었다");
            Assert.AreEqual(0, gal[1].Cuts, "끝까지 굴리기는 끊지 않는다");
        }

        // ── 세기 비를 평가로 다시 재기(M8-9 견줌) ──

        /// <summary>갤럭시단 승률(‰)로 본 플레이어 파티 칸: 700 넘음 = 약함 · 300~700 = 비슷함(팽팽한 장면과 같은 구간 M8-11) · 300 밑 = 강함.</summary>
        static int RateBand(int galacticPermille) => galacticPermille > GalacticBench.TightHigh ? 0 : galacticPermille >= GalacticBench.TightLow ? 1 : 2;

        static double Pearson(List<int> x, List<int> y)
        {
            int n = x.Count;
            if (n < 2) return 0;
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++)
            {
                mx += x[i];
                my += y[i];
            }
            mx /= n;
            my /= n;
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                sxy += (x[i] - mx) * (y[i] - my);
                sxx += (x[i] - mx) * (x[i] - mx);
                syy += (y[i] - my) * (y[i] - my);
            }
            return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
        }

        [Test, Category("Slow")]
        public void M8_시험대평가_세기비를평가로다시잰다()
        {
            // 장면마다: 실제 = 원칙 AI끼리 주사위 TightDice의 갤럭시단 승률 · 옛 잣대 = PartyGauge 세기 비(M8-9 칸) · 새 잣대 = 배틀 행동 선택까지 원칙으로 민 판의 평가(갤럭시단 이길 확률).
            // 칸을 실제 승률 칸(RateBand)과 견준다 — 두 셋(옛 정본 · 운영 규칙 켠 봇 판). 태홍 장면(운영 규칙 켬 — 옛 잣대 「비슷함」인데 원칙끼리 98% 짐)을 새 잣대가 「약함」으로 잡는지.
            var eval = new BattleEval();
            foreach (bool prepared in new[] { false, true })
            {
                var actual = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                var predicted = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                var power = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                var oldHit = new int[4];
                var newHit = new int[4];
                var bandOld = new int[4, 3];
                var bandNew = new int[4, 3];
                var bandReal = new int[4, 3];
                long err = 0;
                int cyrusEvenOld = 0, cyrusEvenWeak = 0, cyrusEvenWeakNew = 0;
                int games = ForEachRealScene(ctx, EvalScenes, (game, i, s) =>
                {
                    int kind = KindOf(s);
                    ulong id = game * 1000 + (ulong)i;
                    int rate = PrincipleRate(s, game, id);
                    var m = PartyGauge.OfScene(s, ctx);
                    var g = s.Clone();
                    var bot = new Playout.Bot(game);
                    var rng = new Pcg32(id, 0xB07);
                    AdvanceToAction(g, (x, cx, c, legal) => bot.Answer(x, cx, c, rng));
                    int e = eval.WinPermille(g, ctx, 1);
                    int real = RateBand(rate), mine = RateBand(e), old = (int)m.Band; // 칸 번호: 약함 0 · 비슷함 1 · 강함 2(PartyBand와 같은 순서)
                    foreach (int k in new[] { kind, 3 })
                    {
                        actual[k].Add(rate);
                        predicted[k].Add(e);
                        power[k].Add(m.Power);
                        bandOld[k, old]++;
                        bandNew[k, mine]++;
                        bandReal[k, real]++;
                        if (old == real) oldHit[k]++;
                        if (mine == real) newHit[k]++;
                    }
                    err += Math.Abs(e - rate);
                    if (kind == 2 && m.Band == PartyBand.Even)
                    {
                        cyrusEvenOld++;
                        if (real == 0) cyrusEvenWeak++;
                        if (real == 0 && mine == 0) cyrusEvenWeakNew++;
                    }
                }, prepared);
                string set = prepared ? "운영 규칙 켠 봇 판" : "옛 정본";
                string[] names = { SceneKinds[0], SceneKinds[1], SceneKinds[2], "전체" };
                Report($"M8 평가 세기 비 [{set}] 봇 판 {games} · 장면 {actual[3].Count} · 평가와 실제 승률 차 평균 {(actual[3].Count > 0 ? err / actual[3].Count : 0)}‰ · 태홍 옛 칸 비슷함 {cyrusEvenOld} 중 실제 약함 {cyrusEvenWeak} · 그 가운데 새 칸 약함 {cyrusEvenWeakNew}");
                for (int k = 0; k < 4; k++)
                {
                    int n = actual[k].Count;
                    if (n == 0) continue;
                    long sa = 0, sp = 0, so = 0;
                    for (int j = 0; j < n; j++)
                    {
                        sa += actual[k][j];
                        sp += predicted[k][j];
                        so += power[k][j];
                    }
                    Report($"M8 평가 세기 비 [{set}] {names[k]} {n}: 실제 갤 승률 {BenchResult.Percent(sa / n)} · 평가 {BenchResult.Percent(sp / n)} · 옛 세기 비 {BenchResult.Percent(so / n)}"
                        + $" ‖ 칸(약함/비슷함/강함) 실제 {bandReal[k, 0]}/{bandReal[k, 1]}/{bandReal[k, 2]} · 옛 {bandOld[k, 0]}/{bandOld[k, 1]}/{bandOld[k, 2]} · 새 {bandNew[k, 0]}/{bandNew[k, 1]}/{bandNew[k, 2]}"
                        + $" ‖ 칸 맞음 옛 {BenchResult.Percent(oldHit[k] * 1000 / n)} · 새 {BenchResult.Percent(newHit[k] * 1000 / n)} ‖ 상관 실제~평가 {Pearson(actual[k], predicted[k]):0.00} · 실제~옛 세기 비 {Pearson(actual[k], power[k]):0.00}");
                }
                Assert.AreEqual(EvalScenes, actual[3].Count);
            }
        }
    }
}
