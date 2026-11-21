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
    /// plan M8 「선출 · 순서」(01 §13-1 M8-22~): 명단 거르기(LineupScreen — 원칙 값 빠른 점수) → 깊이(남은 후보에 굴리기 한도를 모두 — StrongAgent.ScreenLineup) ·
    /// 출전 · 다음 포켓몬 · 응징 · 우선권 토큰이 탐색 트리 안 결정 노드인지 · 시험대(옛 정본의 간부 · 태홍 장면).
    /// </summary>
    public partial class StrongAiTests
    {
        static StrongAgent Drafter(SearchKind kind) => new StrongAgent { Search = kind, Depth = kind == SearchKind.Flat ? 0 : EvalDepth, ScreenLineup = true };

        /// <summary>명단 고르기가 열린 원본에서 후보마다 참값(‰ — 그 원본을 주사위만 바꿔 tries번, 두 쪽 원칙으로 끝까지 · 비김 반 승).</summary>
        int[] LineupTruth(GameState s, PendingChoice c, List<ChoiceAnswer> legal, ulong id, int tries)
        {
            var v = new int[legal.Count];
            for (int i = 0; i < legal.Count; i++)
            {
                long w = 0;
                for (int d = 1; d <= tries; d++)
                {
                    var r = Rollout.ToEnd(GalacticBench.Reroll(s, id, d), ctx, Rollout.Principle, c.Id, legal[i]);
                    Assert.IsTrue(r.Ended);
                    w += r.Winner == 1 ? 1000 : r.Winner < 0 ? 500 : 0;
                }
                v[i] = (int)(w / tries);
            }
            return v;
        }

        AgentDecision AskLineup(StrongAgent agent, GameState s, int cap, ulong seed)
        {
            var view = Engine.ViewFor(s, SeatId.Observer);
            var vc = view.Pending.Find(x => x.Kind == ChoiceKind.Lineup && !x.Answered);
            return agent.Decide(new AgentInput { View = view, Choice = vc, Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget { MaxSimulations = cap, Seed = seed });
        }

        // ── 거르기 · 예산 나누기 ──

        [Test]
        public void M8_선출_거르기는점수순이고예산을나눠도표본이줄지않는다()
        {
            // 남길 후보 수: 한도 ÷ (표본 4 × 답마다 반복 — 평평 1 · 탐색 16)을 2~답 수로
            Assert.AreEqual(16, StrongAgent.LineupKeep(64, 28, SearchKind.Flat));
            Assert.AreEqual(4, StrongAgent.LineupKeep(256, 28, SearchKind.Tree));
            Assert.AreEqual(2, StrongAgent.LineupKeep(8, 28, SearchKind.Tree), "한도가 작아도 둘은 남긴다");
            Assert.AreEqual(5, StrongAgent.LineupKeep(1024, 5, SearchKind.Tree), "답 수보다 많이 남기지 않는다");
            // 앞 keep개(같으면 Legal 순서 — 앞 답 먼저), 담는 순서는 Legal 순서
            var top = new List<int>();
            LineupScreen.Top(new long[] { 5, 9, 5, 1, 9 }, 5, 3, top);
            CollectionAssert.AreEqual(new[] { 0, 1, 4 }, top);
            LineupScreen.Top(new long[] { 5, 9 }, 2, 3, top);
            CollectionAssert.AreEqual(new[] { 0, 1 }, top);

            // 태홍 명단 28가지 · 간부 명단: 거르기를 켜면 표본이 LineupSamples 이상(끄면 탐색 256에서 1 · 평평 64에서 2)이고 굴리기 수는 한도 안
            var lines = new List<string>();
            foreach (var scene in new[] { BenchScene.Cyrus, BenchScene.Commander })
            {
                var s = GalacticBench.Scene(ctx, scene, 0);
                var c = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup);
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(s, ctx, c, legal);
                if (scene == BenchScene.Cyrus) Assert.AreEqual(28, legal.Count, "태홍 C(8,6)");
                foreach (var (kind, cap) in new[] { (SearchKind.Flat, 64), (SearchKind.Tree, 256) })
                {
                    var plain = new StrongAgent { Search = kind, Depth = kind == SearchKind.Flat ? 0 : EvalDepth };
                    var draft = Drafter(kind);
                    AskLineup(plain, s, cap, 7);
                    var d = AskLineup(draft, s, cap, 7);
                    int keep = StrongAgent.LineupKeep(cap, legal.Count, kind);
                    Assert.AreEqual(keep, draft.LastAnswers, $"{scene} {kind}: 거른 뒤 답 수");
                    Assert.GreaterOrEqual(draft.LastSamples, Math.Min(StrongAgent.Samples, StrongAgent.LineupSamples), $"{scene} {kind}: 표본 수");
                    Assert.GreaterOrEqual(draft.LastSamples, plain.LastSamples);
                    Assert.LessOrEqual(draft.Simulations, cap, $"{scene} {kind}: 한도 안");
                    Assert.IsTrue(legal.Exists(x => x.SameAs(d.Answer)), "AI 답은 합법 답");
                    lines.Add($"{scene} {kind} 한도 {cap}: 답 {legal.Count} → {draft.LastAnswers} · 표본 {plain.LastSamples} → {draft.LastSamples} · 굴리기 {plain.Simulations} → {draft.Simulations}");
                    if (scene == BenchScene.Cyrus) Assert.AreEqual(kind == SearchKind.Flat ? 2 : 1, plain.LastSamples, "거르기 없이는 표본이 1~2로 떨어진다");
                }
                // 한도가 아주 작아도 표본은 1 밑으로 떨어지지 않는다
                var tiny = Drafter(SearchKind.Tree);
                AskLineup(tiny, s, 8, 7);
                Assert.GreaterOrEqual(tiny.LastSamples, 1);
                Assert.AreEqual(2, tiny.LastAnswers);
            }
            Report($"M8 선출 예산 나누기: {string.Join(" ‖ ", lines)}");
        }

        // ── 뻔한 명단 ──

        [Test]
        public void M8_선출_뻔한명단_상성이쏠린장면에서그명단()
        {
            // 간부 장면의 도전자 파티를 한 카드(보통 덱 카드를 차례로)로 모두 바꿔 상성을 한쪽으로 쏠리게 한 원본에서: 참값(주사위만 바꿔 두 쪽 원칙으로 끝까지)이
            // 가장 큰 명단은 800‰ 이상이고 원칙 답(첫 명단)은 그보다 300‰ 이상 낮은 자리. 거르기 → 깊이(평평 64 · 탐색 256)가 가장 큰 참값에서 100‰ 안의 명단을 고른다.
            const int tries = 32;
#if UNITY_EDITOR
            const int want = 1;
#else
            const int want = 3;
#endif
            int found = 0;
            var sw = Stopwatch.StartNew();
            var legal = new List<ChoiceAnswer>();
            var lines = new List<string>();
            var cards = ctx.Deck(DeckId.Moderate).Cards;
            for (int ci = 0; ci < cards.Count && found < want; ci++)
            {
                int card = cards[ci].Card;
                var s = GalacticBench.Scene(ctx, BenchScene.Commander, (ulong)ci);
                var st = s.Seats[0];
                for (int i = 0; i < st.OpenSlots; i++) s.Pokemon[st.Party[i]].Def = card;
                var c = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup);
                legal.Clear();
                Engine.Legal(s, ctx, c, legal);
                var pre = LineupTruth(s, c, legal, (ulong)ci, tries / 4); // 먼저 적게 굴려 쏠리지 않은 장면을 거른다
                int preBest = 0;
                foreach (int v in pre) preBest = Math.Max(preBest, v);
                if (preBest < 700 || pre[0] > preBest - 200) continue;
                var truth = LineupTruth(s, c, legal, (ulong)ci, tries);
                int best = 0;
                foreach (int v in truth) best = Math.Max(best, v);
                if (best < 800 || truth[0] > best - 300) continue;
                foreach (var (kind, cap) in new[] { (SearchKind.Flat, 64), (SearchKind.Tree, 256) })
                {
                    var agent = Drafter(kind);
                    var d = AskLineup(agent, s, cap, 41 + (ulong)ci);
                    int at = legal.FindIndex(x => x.SameAs(d.Answer));
                    Assert.GreaterOrEqual(at, 0);
                    Assert.GreaterOrEqual(truth[at], best - 100, $"카드 {ctx.Cards.Pokemon[card].Key} {kind}: {d.Answer} 참값 {truth[at]}‰(가장 큰 {best}‰ · 원칙 {truth[0]}‰)");
                    Assert.AreEqual(0, agent.Stuck);
                    Assert.AreEqual(0, agent.Mismatch);
                }
                lines.Add($"{ctx.Cards.Pokemon[card].Key} × {st.OpenSlots}: 명단 {legal.Count} · 원칙 {truth[0]}‰ · 가장 큰 {best}‰ · {sw.ElapsedMilliseconds}ms");
                found++;
            }
            Report($"M8 선출 뻔한 명단: {string.Join(" ‖ ", lines)}");
            Assert.GreaterOrEqual(found, want, "상성이 쏠린 장면");
        }

        // ── 결정성 · 엿보기 없음 ──

        [Test]
        public void M8_선출_결정적이고엿보기없음_숨은정보만다른두원본에서명단이같다()
        {
            // 간부 · 태홍 장면(도전자에게 운명 손패 3장): 숨은 정보만 다른 두 원본(관전자 보기가 같다)에서 거르기 → 깊이(평평 · 탐색)의 명단 · 굴리기 수가 같고, 다시 물어도 같다.
            const ulong seeds = 3;
            int decisions = 0;
            foreach (var scene in new[] { BenchScene.Commander, BenchScene.Cyrus })
                for (ulong seed = 0; seed < seeds; seed++)
                {
                    var a = GalacticBench.Scene(ctx, scene, seed);
                    var fate = a.Decks[(int)DeckId.ChangeOfFate];
                    for (int k = 0; k < 3; k++)
                    {
                        a.Seats[0].FateHand.Add(fate[fate.Count - 1]);
                        fate.RemoveAt(fate.Count - 1);
                    }
                    var b = a.Clone();
                    Scramble(b, seed * 41 + 3);
                    Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b));
                    Assert.AreEqual(StateHash.Of(Engine.ViewFor(a, SeatId.Observer)), StateHash.Of(Engine.ViewFor(b, SeatId.Observer)), "두 원본의 보기는 같다");
                    var legal = new List<ChoiceAnswer>();
                    var c = a.Pending.Find(x => x.Kind == ChoiceKind.Lineup);
                    Engine.Legal(a, ctx, c, legal);
                    foreach (var (kind, cap) in new[] { (SearchKind.Flat, 64), (SearchKind.Tree, 96) })
                    {
                        var agentA = Drafter(kind);
                        var agentB = Drafter(kind);
                        var da = AskLineup(agentA, a, cap, 555 + seed);
                        var db = AskLineup(agentB, b, cap, 555 + seed);
                        var again = AskLineup(agentA, a, cap, 555 + seed);
                        string at = $"{scene} 시드 {seed} {kind}";
                        Assert.IsTrue(da.Answer.SameAs(db.Answer), $"{at}: {da.Answer} ≠ {db.Answer}");
                        Assert.IsTrue(da.Answer.SameAs(again.Answer), $"{at}: 같은 입력 · 같은 씨 → 같은 답");
                        Assert.AreEqual(agentA.Simulations, 2 * agentB.Simulations, at);
                        Assert.AreEqual(0, agentA.Mismatch, at);
                        Assert.AreEqual(0, agentA.Stuck, at);
                        Assert.IsTrue(legal.Exists(x => x.SameAs(da.Answer)), "AI 답은 합법 답");
                        decisions++;
                    }
                }
            Assert.AreEqual((int)seeds * 4, decisions);
        }

        // ── 트리 안 결정 노드 ──

        [Test]
        public void M8_선출_출전다음포켓몬응징우선권토큰은탐색트리안의노드다()
        {
            // 간부 · 태홍 장면을 원칙으로 진행하며 갤럭시단 선택마다(명단 포함) 트리 탐색(거르기 켬 · 굴리기 256): 트리가 만든 결정 노드 갈래에
            // 갤럭시단 쪽 출전 · 다음 포켓몬 · 기술 · 은하의 응징, 도전자 쪽 출전 · 다음 포켓몬 · 기술 · 우선권 토큰이 있다.
            const ulong seeds = 2;
            const int ask = 8;
            var agent = Drafter(SearchKind.Tree);
            var legal = new List<ChoiceAnswer>();
            foreach (var scene in new[] { BenchScene.Commander, BenchScene.Cyrus })
                for (ulong seed = 0; seed < seeds; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    for (int step = 0, asked = 0; step < 400 && asked < ask; step++)
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
                            answer = agent.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() },
                                new AgentBudget { MaxSimulations = 256, Seed = 77 + (ulong)c.Id }).Answer;
                        }
                        Assert.IsTrue(Rollout.Step(s, ctx, c, answer));
                    }
                }
            var kinds = agent.NodeKinds;
            var seen = new List<string>();
            foreach (var kv in kinds)
            {
                int k = kv.Key;
                seen.Add($"{(ChoiceKind)(k / 2 / 64)}/{(ChoiceSub)(k / 2 % 64)}{(k % 2 == 1 ? "(갤)" : "(도전자)")} {kv.Value}");
            }
            seen.Sort(StringComparer.Ordinal);
            Report($"M8 선출 트리 노드 갈래: {string.Join(" · ", seen)} · 노드 {agent.Nodes}");
            Assert.AreEqual(0, agent.Mismatch);
            foreach (var (kind, sub, mine) in new[]
            {
                (ChoiceKind.SendIn, ChoiceSub.None, true), (ChoiceKind.NextPokemon, ChoiceSub.None, true), (ChoiceKind.BattleAction, ChoiceSub.None, true),
                (ChoiceKind.Reaction, ChoiceSub.Retribution, true),
                (ChoiceKind.SendIn, ChoiceSub.None, false), (ChoiceKind.NextPokemon, ChoiceSub.None, false), (ChoiceKind.BattleAction, ChoiceSub.None, false),
                (ChoiceKind.Reaction, ChoiceSub.InitiativeToken, false),
            })
                Assert.IsTrue(kinds.ContainsKey(StrongAgent.NodeKey(kind, sub, mine)), $"트리 안 노드 {kind}/{sub} {(mine ? "갤럭시단" : "도전자")} 쪽");
        }

        // ── 시험대: 옛 정본의 간부 · 태홍 장면 ──

        /// <summary>
        /// 선출 시험대: 관문은 옛 정본 앞의 간부 · 태홍 장면 LineupScenes개(dotnet 2 · Unity 1, Unity는 대진 둘만), SINNOH_BENCH_SCENES를 주면 옛 정본 앞 그 수 가운데 간부 · 태홍 전부(1000 → 707).
        /// </summary>
#if UNITY_EDITOR
        public static readonly int LineupScenes = 1;
        static readonly List<int> LineupRows = new List<int> { 0, 6 };
#else
        public static readonly int LineupScenes = 2;
        /// <summary>돌릴 대진(SINNOH_LINEUP_ROWS = 「6,7」 꼴 — 갤 i · 플 j = 2i + j, 갤 0 원칙 · 1 1단계 · 2 새 탐색 · 3 새 탐색 + 새 선출 · 4 1단계 + 새 선출, 플 0 원칙 · 1 1단계, 기본 전부).</summary>
        static readonly List<int> LineupRows = Environment.GetEnvironmentVariable("SINNOH_LINEUP_ROWS") is string rows && rows.Length > 0
            ? new List<string>(rows.Split(',')).ConvertAll(int.Parse) : new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
#endif

        /// <summary>옛 정본 앞(관문 밖 SINNOH_BENCH_SCENES개 · 관문은 앞에서부터)의 간부 · 태홍 장면 — visit(판 시드, 판 안 번호, 원본). 관문은 LineupScenes개에서 멈춘다.</summary>
        static void ForEachLineupScene(Ctx ctx, Action<ulong, int, GameState> visit)
        {
            bool full = Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES") != null;
            int limit = full ? BenchScenes : CanonScenes, want = full ? int.MaxValue : LineupScenes;
            int seen = 0, got = 0;
            for (ulong game = 0; seen < limit && got < want; game++)
            {
                var list = RealScenes(ctx, 2, game);
                for (int i = 0; i < list.Count && seen < limit && got < want; i++, seen++)
                    if (KindOf(list[i]) != 0)
                    {
                        visit(game, i, list[i]);
                        got++;
                    }
            }
        }

        /// <summary>갤럭시단 AI를 감싸 명단 결정만 따로 잰다(µs).</summary>
        sealed class LineupClock : IAgent
        {
            readonly IAgent inner;
            readonly Func<long> now;
            public long Count, Sum, Max;

            public LineupClock(IAgent inner, Func<long> now)
            {
                this.inner = inner;
                this.now = now;
            }

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                if (input.Choice.Kind != ChoiceKind.Lineup) return inner.Decide(input, budget);
                long t0 = now();
                var d = inner.Decide(input, budget);
                long us = now() - t0;
                Count++;
                Sum += us;
                if (us > Max) Max = us;
                return d;
            }
        }

        [Test, Category("Slow")]
        public void M8_시험대선출_간부태홍()
        {
            // 옛 정본(운영 규칙 끔 · 주사위 0)의 간부 · 태홍 장면 × 대진(갤 원칙 / 1단계 / 새 탐색 / 새 탐색 + 새 선출 / 1단계 + 새 선출 × 플 원칙 / 플 1단계).
            // 1단계 = 평평 · 끝까지 · 굴리기 BenchSims, 새 탐색 = Tree · 깊이 EvalDepth · 굴리기 SearchSims, 플 1단계 = 평평 BenchSims(「M8 탐색」 SINNOH_SEARCH_PLAYER=flat과 같은 AI).
            // 거르기 없는 줄은 「M8 탐색」 · 「판세 평가」의 같은 장면 · 같은 씨 숫자와 같아야 한다. 명단 결정 시간은 따로(µs). SINNOH_BENCH_SHARD="i/n" = 간부 · 태홍 장면 번호 % n = i만.
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
            const int Gals = 5, Rows = Gals * 2, Groups = 3; // 0 간부 · 1 태홍 · 2 합계
            string[] galNames = { "갤 원칙", "갤 1단계", "갤 새", "갤 새 + 새 선출", "갤 1단계 + 새 선출" };
            string[] plNames = { "플 원칙", "플 1단계" };
            var res = new BenchResult[Rows, Groups];
            var gal = new StrongAgent[Rows];
            var clock = new LineupClock[Rows];
            var pl = new StrongAgent[Rows];
            for (int r = 0; r < Rows; r++)
            {
                for (int g = 0; g < Groups; g++) res[r, g] = new BenchResult();
                int gi = r / 2;
                gal[r] = gi == 0 ? null : gi == 1 ? new StrongAgent() : gi == 2 ? Searcher(SearchKind.Tree)
                    : gi == 3 ? Drafter(SearchKind.Tree) : Drafter(SearchKind.Flat);
                if (gal[r] != null) clock[r] = new LineupClock(gal[r], micros);
                if (r % 2 == 1) pl[r] = new StrongAgent();
            }
            int scenes = 0, index = 0;
            ForEachLineupScene(ctx, (game, i, s) =>
            {
                int kind = KindOf(s);
                if (index++ % shards != shard) return;
                ulong id = game * 1000 + (ulong)i;
                for (int r = 0; r < Rows; r++)
                {
                    if (!LineupRows.Contains(r)) continue;
                    int gi = r / 2;
                    var one = new BenchResult();
                    int m = (gal[r] != null ? 1 : 0) + (pl[r] != null ? 2 : 0);
                    var s0 = GalacticBench.Reroll(s, id, 0);
                    var bot = new Playout.Bot(game);
                    var rng = new Pcg32(id, 0xB07);
                    RolloutPolicy seat = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
                    if (pl[r] != null) seat = GalacticBench.SeatAgent(pl[r], seat, BenchSims, 2, id);
                    int sims = gi == 1 || gi == 4 ? BenchSims : SearchSims;
                    if (gal[r] != null) GalacticBench.Play(s0, ctx, clock[r], seat, one, sims, 1, id, micros);
                    else GalacticBench.Play(s0, ctx, new PrincipleAgent(), seat, one, 0, 1, id);
                    res[r, kind - 1].Add(one);
                    res[r, 2].Add(one);
                }
                scenes++;
            });
#if UNITY_EDITOR
            const string where = "Unity Mono";
#else
            const string where = "dotnet";
#endif
            string[] names = { "간부", "태홍", "합계" };
            Report($"M8 선출 시험대({where}): 옛 정본 간부 · 태홍 장면 {scenes}{(shards > 1 ? $"(조각 {shard}/{shards})" : "")} · 새 탐색 굴리기 {SearchSims} · 1단계 {BenchSims} · 깊이 {EvalDepth} · {sw.ElapsedMilliseconds}ms");
            for (int r = 0; r < Rows; r++)
            {
                if (!LineupRows.Contains(r)) continue;
                var parts = new List<string>();
                for (int g = 0; g < Groups; g++) parts.Add($"{names[g]} {res[r, g].Battles}: {BenchResult.Percent(res[r, g].WinPermille)}");
                var all = res[r, 2];
                string think = gal[r] == null ? "" : $" · 갤 결정 {all.Decisions} · 결정당 {(all.Decisions > 0 ? all.ThinkMs / all.Decisions : 0)}µs(최대 {all.MaxThinkMs / 1000}ms) · 명단 {clock[r].Count}번 평균 {(clock[r].Count > 0 ? clock[r].Sum / clock[r].Count / 1000 : 0)}ms(최대 {clock[r].Max / 1000}ms)";
                Report($"M8 선출 시험대 [{galNames[r / 2]} · {plNames[r % 2]}] {string.Join(" · ", parts)}{think}");
                var raw = new List<string>();
                for (int g = 0; g < Groups; g++) raw.Add($"{res[r, g].Battles},{res[r, g].GalacticWins},{res[r, g].Draws},{res[r, g].Decisions},{res[r, g].ThinkMs},{res[r, g].MaxThinkMs}");
                Report($"M8 선출 원자료 {r} {string.Join(" ", raw)} lineup {clock[r]?.Count ?? 0},{clock[r]?.Sum ?? 0},{clock[r]?.Max ?? 0}");
            }
            for (int r = 0; r < Rows; r++)
            {
                if (!LineupRows.Contains(r)) continue;
                Assert.AreEqual(0, res[r, 2].Stuck, $"{galNames[r / 2]} · {plNames[r % 2]}");
                Assert.AreEqual(scenes, res[r, 2].Battles);
                if (gal[r] != null)
                {
                    Assert.AreEqual(0, gal[r].Stuck);
                    Assert.AreEqual(0, gal[r].Mismatch);
                    Assert.AreEqual(scenes, clock[r].Count, "장면마다 명단 결정 하나");
                }
            }
            Assert.Greater(scenes, 0);
        }
    }
}
