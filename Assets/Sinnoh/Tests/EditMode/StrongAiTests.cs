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
    /// plan M8 「밑작업」: SampledRng · Determinizer(01 §9-4) · 굴리기 틀 Rollout(§9-3) · 강한 AI 1단계 StrongAgent(평평한 PIMC) · 시험대 GalacticBench(§9-7).
    /// 완료 조건 2(엿보기 없음): 보기가 같고 숨은 정보만 다른 두 원본에서 채운 판과 결정이 같다.
    /// </summary>
    public partial class StrongAiTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        /// <summary>
        /// 시험대 장면마다 시드 수. 관문은 줄인 판(dotnet 40 · Unity 10), 전체 숫자(01 §9-7 「1,000배틀」)는 관문 밖에서 SINNOH_BENCH_SEEDS로 dotnet을 한 번 돌려 plan-log에 적는다.
        /// </summary>
#if UNITY_EDITOR
        public static readonly int BenchSeeds = 10;
#else
        public static readonly int BenchSeeds = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BENCH_SEEDS"), out var n) && n > 0 ? n : 40;
#endif

        static readonly BenchScene[] Scenes = { BenchScene.Grunt, BenchScene.Commander, BenchScene.Cyrus };

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        // ── SampledRng ──

        [Test]
        public void M8_표본난수는같은씨에같은수열이고표본마다다르다()
        {
            var a = SampledRng.ForSample(42, 3);
            var b = SampledRng.ForSample(42, 3);
            var other = SampledRng.ForSample(42, 4);
            bool differs = false;
            for (int i = 0; i < 64; i++)
            {
                int x = a.Next(1000);
                Assert.AreEqual(x, b.Next(1000));
                if (x != other.Next(1000)) differs = true;
            }
            Assert.IsTrue(differs, "표본 번호가 다르면 수열이 다르다");
            Assert.AreEqual(a.RollAttackDie(), b.RollAttackDie());

            // 채운 판의 게임 난수: 같은 표본이면 같고, 0(보기의 가린 값)으로 남지 않는다
            var s1 = new GameState();
            var s2 = new GameState();
            SampledRng.ForSample(7, 0).Seed(s1);
            SampledRng.ForSample(7, 0).Seed(s2);
            Assert.AreEqual(s1.Rng.State, s2.Rng.State);
            Assert.AreEqual(s1.Rng.Increment, s2.Rng.Increment);
            Assert.AreEqual(1UL, s1.Rng.Increment & 1UL, "Pcg32 증분은 홀수");
        }

        // ── Determinizer ──

        static bool AnyUnknown(List<int> list)
        {
            foreach (int v in list)
                if (v == Engine.Unknown) return true;
            return false;
        }

        static List<int> Sorted(IEnumerable<int> cards)
        {
            var l = new List<int>(cards);
            l.Sort();
            return l;
        }

        /// <summary>그 덱에서 숨은 카드 묶음: 덱 + (운명이면 손패, 트레이너 카드면 후보).</summary>
        static List<int> Hidden(GameState s, int deck)
        {
            var l = new List<int>(s.Decks[deck]);
            foreach (var st in s.Seats)
            {
                if (deck == (int)DeckId.ChangeOfFate) l.AddRange(st.FateHand);
                if (deck == (int)DeckId.TrainerCard) l.AddRange(st.TrainerCardOffer);
            }
            l.Sort();
            return l;
        }

        static List<int> HiddenTokens(GameState s, GameState view)
        {
            var l = new List<int>();
            for (int i = 0; i < s.Tokens.Count; i++)
                if (view.Tokens[i].Kind == TokenKind.HiddenSpot && view.Tokens[i].Def < 0) l.Add(s.Tokens[i].Def * 2 + (s.Tokens[i].Special ? 1 : 0));
            l.Sort();
            return l;
        }

        /// <summary>실제 봇 판의 상태마다: 관전자 보기를 채우면 가려진 칸이 없고, 다시 만든 보기가 처음 보기와 같고, 숨은 카드 묶음이 원본과 같다. 돌려주는 값 = 본 상태 수.</summary>
        int CheckFills(int seats, ulong seed, Campaign campaign, int maxRounds, int every, int[] inexact, FillReport total)
        {
            int n = 0, seen = 0;
            Playout.Run(ctx, seats, seed, s =>
            {
                if (++n % every != 0 || s.Phase == Phase.GameOver) return;
                seen++;
                var view = Engine.ViewFor(s, SeatId.Observer);
                var report = new FillReport();
                var sim = Determinizer.Fill(view, ctx, SampledRng.ForSample(seed, n), report);
                string at = $"{campaign} {seats}인 시드 {seed} 명령 {n}";
                Assert.AreEqual(SeatId.HostViewer, sim.Viewer, at);
                for (int d = 0; d < GameState.DeckCount; d++) Assert.IsFalse(AnyUnknown(sim.Decks[d]), $"{at}: {(DeckId)d} 덱에 가려진 칸");
                foreach (var st in sim.Seats)
                {
                    Assert.IsFalse(AnyUnknown(st.FateHand), $"{at}: 운명 손패");
                    Assert.IsFalse(AnyUnknown(st.TrainerCardOffer), $"{at}: 트레이너 카드 후보");
                    Assert.AreNotEqual(Engine.Unknown, (int)st.Follower, $"{at}: 추종자");
                }
                foreach (var t in sim.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot) Assert.GreaterOrEqual(t.Def, 0, $"{at}: 숨겨진 장소");
                Assert.IsTrue(sim.Rng.State != 0 || sim.Rng.Increment != 0, $"{at}: 게임 난수");
                // 보기와 어긋나지 않는다: 채운 판의 관전자 보기 = 처음 보기
                Assert.AreEqual(StateHash.Of(view), StateHash.Of(Engine.ViewFor(sim, SeatId.Observer)), $"{at}: 채운 판의 보기가 달라졌다");
                // 숨은 카드 묶음이 원본과 같다(순서·주인만 다르다)
                Assert.AreEqual(HiddenTokens(s, view), HiddenTokens(sim, view), $"{at}: 뒷면 숨겨진 장소 묶음");
                Assert.AreEqual(0, report.PaddedTokens, at);
                for (int d = 0; d < GameState.DeckCount; d++)
                {
                    total.Borrowed[d] += report.Borrowed[d];
                    total.Padded[d] += report.Padded[d];
                    if (!Same(Hidden(s, d), Hidden(sim, d))) inexact[d]++;
                }
                total.Unresolved += report.Unresolved;
            }, null, campaign, maxRounds);
            return seen;
        }

        static bool Same(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        [Test]
        public void M8_채우기는보기의숨은칸을보기와어긋나지않게채운다()
        {
            var inexact = new int[GameState.DeckCount];
            var total = new FillReport();
            int seen = 0;
            seen += CheckFills(3, 1, Campaign.HeartOfSinnoh, 40, 17, inexact, total);
            seen += CheckFills(6, 2, Campaign.HeartOfSinnoh, 15, 17, inexact, total);
            seen += CheckFills(2, 3, Campaign.UltraBurst, 15, 17, inexact, total);
            Assert.Greater(seen, 60, "본 상태 수");
            var lines = new List<string>();
            for (int d = 0; d < GameState.DeckCount; d++)
                if (inexact[d] > 0 || total.Borrowed[d] > 0 || total.Padded[d] > 0) lines.Add($"{(DeckId)d} 다름 {inexact[d]} · 빌림 {total.Borrowed[d]} · 메움 {total.Padded[d]}");
            Report($"M8 채우기: 상태 {seen} · 되살리지 못한 선택 {total.Unresolved} · {(lines.Count == 0 ? "모든 덱의 숨은 묶음이 원본과 같다" : string.Join(" / ", lines))}");
            // 덱마다 숨은 카드 묶음(덱 + 남의 손)이 원본과 같아야 한다 — 빌림은 남은 스타팅 포켓몬이 약한 덱에 섞인 것(R15)
            for (int d = 0; d < GameState.DeckCount; d++)
            {
                Assert.AreEqual(0, inexact[d], $"{(DeckId)d}: 채운 묶음이 원본과 다른 상태 수");
                Assert.AreEqual(0, total.Padded[d], $"{(DeckId)d}: 덱 구성에서 메운 장수(셈이 어긋남)");
            }
        }

        [Test]
        public void M8_채우기는같은씨에같은판이고씨가다르면다르다()
        {
            var s = GalacticBench.Scene(ctx, BenchScene.Commander, 5);
            var view = Engine.ViewFor(s, SeatId.Observer);
            ulong a = StateHash.Of(Determinizer.Fill(view, ctx, SampledRng.ForSample(9, 0)));
            ulong b = StateHash.Of(Determinizer.Fill(view, ctx, SampledRng.ForSample(9, 0)));
            ulong c = StateHash.Of(Determinizer.Fill(view, ctx, SampledRng.ForSample(9, 1)));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c, "표본이 다르면 다른 판");
            // 판을 돌려 써도 같다(FillInto — 앞 표본의 찌꺼기가 남지 않는다)
            var reuse = Determinizer.Fill(view, ctx, SampledRng.ForSample(9, 1));
            Determinizer.FillInto(reuse, view, ctx, SampledRng.ForSample(9, 0));
            Assert.AreEqual(a, StateHash.Of(reuse));
            Assert.Throws<InvalidOperationException>(() => Determinizer.Fill(s, ctx, SampledRng.ForSample(9, 0)), "호스트 원본은 받지 않는다");
        }

        [Test]
        public void M8_채운판은엔진이그대로돈다()
        {
            // 실제 판 중간의 보기를 채워 기본 답(Timeout)으로 계속 돌린다 — 거절·예외 없이
            int n = 0, runs = 0, commands = 0;
            Playout.Run(ctx, 3, 4, s =>
            {
                if (++n % 37 != 0 || s.Phase == Phase.GameOver) return;
                var sim = Determinizer.Fill(Engine.ViewFor(s, SeatId.Observer), ctx, SampledRng.ForSample(4, n));
                runs++;
                for (int i = 0; i < 80 && sim.Phase != Phase.GameOver; i++)
                {
                    var c = sim.Pending.Find(x => !x.Answered);
                    if (c == null) break; // 미뤄 둔 선택(Held)은 보기에 없다
                    var r = Engine.Submit(sim, ctx, SeatId.System, Command.TimeoutOf(c.Id), null);
                    Assert.IsTrue(r.Accepted, $"명령 {n} 뒤 채운 판 {i}번째 {c.Kind}/{c.Sub}: {r}");
                    commands++;
                }
            }, null, Campaign.HeartOfSinnoh, 40);
            Assert.Greater(runs, 5);
            Assert.Greater(commands, runs * 20, "채운 판이 거의 바로 멈춘다");
        }

        // ── 굴리기 ──

        [Test]
        public void M8_한수앞_답을넣으면그선택이닫힌다()
        {
            var s = GalacticBench.Scene(ctx, BenchScene.Commander, 1);
            var sim = Determinizer.Fill(Engine.ViewFor(s, SeatId.Observer), ctx, SampledRng.ForSample(1, 0));
            var c = Battles.Only(sim, ChoiceKind.Lineup);
            var legal = Scene.Legal(sim, ctx, c);
            Assert.AreEqual(Scene.Legal(s, ctx, s.Pending[0]).Count, legal.Count, "채운 판의 합법 답 = 원본의 합법 답 수");
            Assert.IsTrue(Rollout.Step(sim, ctx, c, legal[legal.Count - 1]));
            Assert.IsNotNull(sim.Battle, "명단을 내면 배틀이 열린다");
            Assert.IsNull(sim.Pending.Find(x => x.Id == c.Id));
            Assert.IsNull(s.Battle, "원본은 그대로");
            Assert.IsFalse(Rollout.Step(sim, ctx, c, legal[0]), "닫힌 선택에 다시 넣으면 거절");
        }

        [Test]
        public void M8_굴리기는배틀끝까지가고같은판에서같은결과()
        {
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 5; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    var view = Engine.ViewFor(s, SeatId.Observer);
                    var a = Rollout.ToEnd(Determinizer.Fill(view, ctx, SampledRng.ForSample(seed, 0)), ctx, Rollout.Principle);
                    var b = Rollout.ToEnd(Determinizer.Fill(view, ctx, SampledRng.ForSample(seed, 0)), ctx, Rollout.Principle);
                    Assert.IsTrue(a.Ended, $"{scene} 시드 {seed}: 끝나지 않았다(걸음 {a.Steps})");
                    Assert.IsFalse(a.Stuck);
                    Assert.AreEqual(a.Winner, b.Winner);
                    Assert.AreEqual(a.Steps, b.Steps);
                    Assert.AreEqual(a.Hp[0], b.Hp[0]);
                    Assert.Greater(a.MaxHp[0], 0, "플레이어 파티 체력을 읽었다");
                    Assert.AreEqual(0, a.MaxHp[1], "갤럭시단 쪽은 좌석이 아니다");
                    if (a.Winner == 1) Assert.AreEqual(0, a.Hp[0], "갤럭시단이 이겼으면 플레이어 파티가 바닥났다");
                }
        }

        // ── 엿보기 없음(완료 조건 2) ──

        /// <summary>숨은 정보만 바꾼다: 운명 손패를 덱과 다시 섞어 나누고, 모든 덱 순서를 섞고(보이는 맨 위 은행상회 카드는 그대로), 뒷면 숨겨진 장소의 정체를 서로 바꾸고, 게임 난수를 바꾼다.
        /// keep = 손패를 그대로 둘 좌석(그 좌석의 보기로 견줄 때 — 자기 손패는 보인다), −1이면 모두 섞는다.</summary>
        static void Scramble(GameState s, ulong seed, int keep = -1)
        {
            var rng = new Pcg32(seed, 0x5C4A);
            var fate = s.Decks[(int)DeckId.ChangeOfFate];
            var counts = new int[s.SeatCount];
            for (int i = 0; i < s.SeatCount; i++)
            {
                if (i == keep) continue;
                counts[i] = s.Seats[i].FateHand.Count;
                fate.AddRange(s.Seats[i].FateHand);
                s.Seats[i].FateHand.Clear();
            }
            for (int d = 0; d < GameState.DeckCount; d++)
            {
                var id = (DeckId)d;
                if (id == DeckId.Starter || id == DeckId.HomeVillage || id == DeckId.Act) continue; // 섞지 않는 덱 — 순서가 공개 정보
                var pile = s.Decks[d];
                int top = id == DeckId.GinkgoTrade && pile.Count > 0 ? pile[pile.Count - 1] : -1;
                if (top >= 0) pile.RemoveAt(pile.Count - 1);
                Piles.Shuffle(pile, rng);
                if (top >= 0) pile.Add(top);
            }
            for (int i = 0; i < s.SeatCount; i++)
                for (int k = 0; k < counts[i]; k++)
                {
                    s.Seats[i].FateHand.Add(fate[fate.Count - 1]);
                    fate.RemoveAt(fate.Count - 1);
                }
            foreach (bool special in new[] { false, true })
            {
                var at = new List<int>();
                var defs = new List<int>();
                for (int i = 0; i < s.Tokens.Count; i++)
                {
                    var t = s.Tokens[i];
                    if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Special != special) continue;
                    at.Add(i);
                    defs.Add(t.Def);
                }
                Piles.Shuffle(defs, rng);
                for (int i = 0; i < at.Count; i++) s.Tokens[at[i]].Def = defs[i];
            }
            s.Rng.CopyFrom(new Pcg32(seed ^ 0xABCDEF, 77));
        }

        [Test]
        public void M8_엿보기없음_숨은정보만다른두원본에서결정이같다()
        {
            int decisions = 0, searched = 0;
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var a = GalacticBench.Scene(ctx, scene, seed);
                    // 플레이어에게 운명 손패 3장(숨은 정보)
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
                            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b), "두 원본은 달라야 한다(숨은 정보)");
                            var va = Engine.ViewFor(a, SeatId.Observer);
                            var vb = Engine.ViewFor(b, SeatId.Observer);
                            Assert.AreEqual(StateHash.Of(va), StateHash.Of(vb), "두 원본의 관전자 보기는 같아야 한다");
                            // 채운 판이 같다
                            Assert.AreEqual(StateHash.Of(Determinizer.Fill(va, ctx, SampledRng.ForSample(11, 2))), StateHash.Of(Determinizer.Fill(vb, ctx, SampledRng.ForSample(11, 2))),
                                $"{scene} 시드 {seed} 걸음 {step}: 채운 판이 숨은 정보에 따라 달라졌다");
                            // 결정이 같다(같은 AI 씨)
                            var agentA = new StrongAgent();
                            var agentB = new StrongAgent();
                            var budget = new AgentBudget { MaxSimulations = 48, Seed = 1234 + (ulong)c.Id };
                            var da = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            var db = agentB.Decide(new AgentInput { View = vb, Choice = vb.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            Assert.IsTrue(da.Answer.SameAs(db.Answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub}: {da.Answer} ≠ {db.Answer}");
                            Assert.AreEqual(agentA.Simulations, agentB.Simulations);
                            Assert.IsTrue(legal.Exists(x => x.SameAs(da.Answer)), "AI 답은 합법 답");
                            Assert.AreEqual(0, agentA.Stuck, "멈춘 굴리기");
                            decisions++;
                            if (agentA.Simulations > 0) searched++;
                            answer = da.Answer;
                        }
                        Assert.IsTrue(Rollout.Step(a, ctx, c, answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {answer}");
                    }
                }
            Assert.GreaterOrEqual(decisions, 15, "견준 결정 수");
            Assert.AreEqual(decisions, searched, "모든 결정이 굴리기를 돌렸다");
            Assert.Throws<InvalidOperationException>(() => new StrongAgent().Decide(
                new AgentInput { View = GalacticBench.Scene(ctx, BenchScene.Grunt, 0), Choice = new PendingChoice(), Ctx = ctx }, new AgentBudget()), "호스트 원본은 받지 않는다");
        }

        // ── 시험대(01 §9-7) ──

        [Test, Category("Slow")]
        public void M8_시험대기준선_원칙AI대원칙AI()
        {
            var sw = Stopwatch.StartNew();
            var principle = new PrincipleAgent();
            int battles = 0, wins = 0, draws = 0;
            foreach (var scene in Scenes)
            {
                var r = GalacticBench.Run(ctx, scene, BenchSeeds, principle, 0, 1, () => sw.ElapsedMilliseconds);
                Report($"M8 시험대 기준선(원칙 대 원칙) {scene}: {r}");
                Assert.AreEqual(BenchSeeds, r.Battles);
                Assert.AreEqual(0, r.Stuck, $"{scene}: 끝까지 가지 못한 배틀");
                Assert.AreEqual(r.Battles, r.GalacticWins + r.PlayerWins + r.Draws);
                battles += r.Battles;
                wins += r.GalacticWins;
                draws += r.Draws;
                // 결정성: 같은 시드면 같은 숫자
                var again = GalacticBench.Run(ctx, scene, Math.Min(BenchSeeds, 5), principle);
                var first = GalacticBench.Run(ctx, scene, Math.Min(BenchSeeds, 5), principle);
                Assert.AreEqual(first.GalacticWins, again.GalacticWins);
                Assert.AreEqual(first.Steps, again.Steps);
                Assert.AreEqual(first.PlayerHpLeft, again.PlayerHpLeft);
            }
            Report($"M8 시험대 기준선 합계: 배틀 {battles} · 갤럭시단 승률 {BenchResult.Percent((2 * wins + draws) * 500 / battles)} · {sw.ElapsedMilliseconds}ms");
        }

        [Test, Category("Slow")]
        public void M8_시험대_강한AI1단계가끝까지돈다()
        {
            // 1단계(평평한 PIMC)의 숫자를 남긴다 — 합격선(01 §9-7 「원칙 AI보다 10%p」)은 탐색·평가 묶음 뒤에 건다. 관문은 줄인 판.
            int seeds = Math.Max(2, BenchSeeds / 5);
            var sw = Stopwatch.StartNew();
            foreach (var scene in Scenes)
            {
                var strong = new StrongAgent();
                var r = GalacticBench.Run(ctx, scene, seeds, strong, BenchSims, 1, () => sw.ElapsedMilliseconds);
                var baseline = GalacticBench.Run(ctx, scene, seeds, new PrincipleAgent());
                Report($"M8 시험대 강한 AI 1단계(굴리기 {BenchSims}) {scene}: {r} · 굴리기 {strong.Simulations}(멈춤 {strong.Stuck}) ‖ 같은 시드 원칙 승률 {BenchResult.Percent(baseline.WinPermille)}");
                Assert.AreEqual(seeds, r.Battles);
                Assert.AreEqual(0, r.Stuck, $"{scene}: 끝까지 가지 못한 배틀");
                Assert.AreEqual(0, strong.Stuck, $"{scene}: 멈춘 굴리기");
                Assert.Greater(strong.Simulations, 0);
            }
        }

        // ── 굴리기 속도 · 이벤트 없이 결과 읽기(01 §13 M8-3) ──

        /// <summary>굴리기 결과를 한 줄로(견줌용).</summary>
        static string Line(RolloutResult r) => $"끝 {r.Ended} · 결과 {r.Result} · 승자 {r.Winner} · 걸음 {r.Steps} · 멈춤 {r.Stuck} · 체력 {r.Hp[0]}/{r.MaxHp[0]} {r.Hp[1]}/{r.MaxHp[1]}";

        [Test]
        public void M8_굴리기는이벤트없이도같은결과와같은판()
        {
            // 밑작업의 길(이벤트를 모두 만들고 BattleEnded에서 읽음)과 이벤트 없는 길이 같은 채운 판에서 같은 결과 · 같은 마지막 판(해시)을 낸다 — 실전 장면 한 판 + 합성 장면 셋 × 시드 3, 표본 2 × 첫 선택의 합법 답 전부
            var root = new GameState();
            var a = new GameState();
            var b = new GameState();
            var legal = new List<ChoiceAnswer>();
            int compared = 0;
            var scenes = RealScenes(ctx, 2, 0);
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++) scenes.Add(GalacticBench.Scene(ctx, scene, seed));
            using (Rig.Use(null))
                for (int i = 0; i < scenes.Count; i++)
                {
                    var view = Engine.ViewFor(scenes[i], SeatId.Observer);
                    for (int k = 0; k < 2; k++)
                    {
                        Determinizer.FillInto(root, view, ctx, SampledRng.ForSample((ulong)i, k));
                        var c = root.Pending.Find(x => !x.Answered);
                        legal.Clear();
                        Engine.Legal(root, ctx, c, legal);
                        foreach (var first in legal)
                        {
                            a.CopyFrom(root);
                            b.CopyFrom(root);
                            var ra = Rollout.ToEnd(a, ctx, Rollout.Principle, c.Id, first, Rollout.MaxSteps, events: true);
                            var rb = Rollout.ToEnd(b, ctx, Rollout.Principle, c.Id, first);
                            string at = $"장면 {i} 표본 {k} 첫 답 {first}";
                            Assert.IsTrue(ra.Ended, at);
                            Assert.AreEqual(Line(ra), Line(rb), at);
                            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b), $"{at}: 이벤트를 끄면 판이 달라졌다");
                            compared++;
                        }
                    }
                }
            Assert.Greater(compared, 200, "견준 굴리기 수");
        }

        /// <summary>
        /// 강한 AI 1단계의 결정 지문(합성 장면 셋 × 시드 3, 갤럭시단 선택마다 굴리기 48): 선택 종류 · 답 · 굴리기 수를 섞은 값. 밑작업 코드(a68513c)에서 잰 값과 같아야 한다 —
        /// 속도 손보기(이벤트 없이 결과 읽기)가 결정을 바꾸지 않았다. 탐색을 일부러 바꾸는 묶음은 이 값을 새로 재어 고친다.
        /// </summary>
        const ulong StageOneDigest = 0x4A4A4952CC986819UL;   // 결정 197: 잡음보다 작은 차이로는 원칙 답(옛 값 0x41503FF4448CA267)

        [Test]
        public void M8_강한AI결정지문은밑작업과같다()
        {
            ulong digest = 14695981039346656037UL;
            int decisions = 0;
            var legal = new List<ChoiceAnswer>();
            var agent = new StrongAgent();
            foreach (var scene in Scenes)
                for (ulong seed = 0; seed < 3; seed++)
                {
                    var s = GalacticBench.Scene(ctx, scene, seed);
                    for (int step = 0; step < 400; step++)
                    {
                        var c = s.Pending.Find(x => !x.Answered);
                        if (c == null || (s.Battle == null && c.Kind != ChoiceKind.Lineup)) break;
                        legal.Clear();
                        Engine.Legal(s, ctx, c, legal);
                        var answer = PrincipleAgent.Choose(s, ctx, c, legal).Answer;
                        if (c.Seat == SeatId.Galactic && legal.Count > 1)
                        {
                            var view = Engine.ViewFor(s, SeatId.Observer);
                            long before = agent.Simulations;
                            answer = agent.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() },
                                new AgentBudget { MaxSimulations = 48, Seed = 99 + (ulong)c.Id }).Answer;
                            foreach (long v in new long[] { (long)c.Kind, (long)answer.Kind, answer.Target, answer.Arg, answer.Values.Count, agent.Simulations - before })
                                digest = unchecked((digest ^ (ulong)v) * 1099511628211UL);
                            decisions++;
                        }
                        Assert.IsTrue(Rollout.Step(s, ctx, c, answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {answer}");
                    }
                }
            Report($"M8 결정 지문: 결정 {decisions} · 0x{digest:X16}");
            Assert.Greater(decisions, 20);
            Assert.AreEqual(StageOneDigest, digest, "강한 AI 1단계의 결정이 밑작업과 달라졌다");
        }

        [Test, Category("Slow")]
        public void M8_시험대_굴리기속도()
        {
            // 굴리기 하나 = 판 준비 + 배틀 끝까지(ToEnd). 채우기(FillInto)는 표본 하나에 한 번이라 따로 적는다(강한 AI는 표본마다 합법 답 수만큼 굴린다).
            // 세 길을 같은 판에서 견준다: ① 새 판 Clone + 이벤트 켬 · ② CopyFrom + 이벤트 켬(밑작업 StrongAgent) · ③ CopyFrom + 이벤트 없음(지금). 실전 장면 한 판(2인 시드 0) × 표본 8. 한 바퀴 데우고 두 번째 바퀴를 적는다.
            var list = RealScenes(ctx, 2, 0);
            var root = new GameState();
            var sim = new GameState();
            var sw = new Stopwatch();
            var names = new[] { "Clone + 이벤트", "CopyFrom + 이벤트(밑작업)", "CopyFrom + 이벤트 없음(지금)" };
            var ticks = new long[3];
            long fillT = 0, steps = 0;
            int rollouts = 0;
            using (Rig.Use(null))
                for (int round = 0; round < 2; round++)
                {
                    Array.Clear(ticks, 0, 3);
                    fillT = steps = 0;
                    rollouts = 0;
                    foreach (var s in list)
                    {
                        var view = Engine.ViewFor(s, SeatId.Observer);
                        for (int k = 0; k < 8; k++)
                        {
                            sw.Restart();
                            Determinizer.FillInto(root, view, ctx, SampledRng.ForSample(7, k));
                            fillT += sw.ElapsedTicks;
                            RolloutResult last = null;
                            for (int way = 0; way < 3; way++)
                            {
                                sw.Restart();
                                GameState g;
                                if (way == 0) g = root.Clone();
                                else
                                {
                                    sim.CopyFrom(root);
                                    g = sim;
                                }
                                var r = Rollout.ToEnd(g, ctx, Rollout.Principle, -1, null, Rollout.MaxSteps, events: way < 2);
                                ticks[way] += sw.ElapsedTicks;
                                Assert.IsTrue(r.Ended);
                                if (last != null) Assert.AreEqual(Line(last), Line(r), "길마다 결과가 같다");
                                last = r;
                            }
                            steps += last.Steps;
                            rollouts++;
                        }
                    }
                }
            double us(long t) => t * 1e6 / Stopwatch.Frequency / rollouts;
#if UNITY_EDITOR
            const string where = "Unity Mono";
#else
            const string where = "dotnet";
#endif
            var parts = new List<string>();
            for (int way = 0; way < 3; way++) parts.Add($"{names[way]} {us(ticks[way]):0} µs");
            Report($"M8 굴리기 속도({where}): 장면 {list.Count} · 굴리기 {rollouts} · 걸음 평균 {steps / (double)rollouts:0.0} — 굴리기당 {string.Join(" · ", parts)} · 걸음당(지금) {ticks[2] * 1e6 / Stopwatch.Frequency / steps:0.0} µs · 채우기 표본당 {us(fillT):0} µs");
            Assert.Greater(rollouts, 0);
        }

        // ── 시험대: 실전 장면(봇 판에서 뽑은 갤럭시단 배틀) ──

        /// <summary>봇 판 하나에서 갤럭시단 배틀이 열리는 자리의 원본 사본(조무래기 = 배틀 첫 선택, 간부·태홍 = 명단 고르기).</summary>
        static List<GameState> RealScenes(Ctx ctx, int seats, ulong seed, bool prepared = false)
        {
            var scenes = new List<GameState>();
            int lastBattle = -1, lastLineup = -1, skip = -1;
            Playout.Run(ctx, seats, seed, s =>
            {
                var lineup = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup && !x.Answered);
                if (lineup != null)
                {
                    if (lineup.Id == lastLineup) return;
                    lastLineup = lineup.Id;
                    skip = s.NextBattleId; // 이 명단으로 열릴 배틀은 다시 담지 않는다
                    scenes.Add(s.Clone());
                    return;
                }
                var b = s.Battle;
                if (b == null || b.Kind != BattleKind.Galactic || b.Id == lastBattle) return;
                lastBattle = b.Id;
                if (b.Id != skip) scenes.Add(s.Clone());
            }, prepared: prepared);
            return scenes;
        }

        static readonly string[] SceneKinds = { "조무래기", "간부", "태홍" };

        /// <summary>장면 종류: 0 조무래기(명단 없이 열린 배틀) · 1 간부(명단) · 2 태홍(명단 6 + 신오의 심장).</summary>
        static int KindOf(GameState s)
        {
            var lineup = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup);
            return lineup == null ? 0 : lineup.Count == HosFlow.BossLineup && s.Seats[lineup.Subject].Heart ? 2 : 1;
        }

        /// <summary>
        /// 실전 장면의 정본(01 §13 M8-8): 2인 봇 판 시드 0, 1, 2 …에서 나온 순서대로 앞 count개 — 시드만으로 다시 만든다. visit(판 시드, 판 안 번호, 원본).
        /// 판을 하나씩 만들고 버린다(장면을 한꺼번에 들고 있지 않는다). 돌려주는 값 = 쓴 봇 판 수.
        /// prepared = 봇 판 운영 규칙(Playout.Bot.Prepared — 파티가 모자라면 도전을 미룬다)을 켠 판에서 뽑는다(M8 「시험대 바로잡기」).
        /// </summary>
        static int ForEachRealScene(Ctx ctx, int count, Action<ulong, int, GameState> visit, bool prepared = false)
        {
            int seen = 0;
            ulong game = 0;
            for (; seen < count; game++)
            {
                if (game > (ulong)count) throw new InvalidOperationException($"봇 판 {game}개에서 장면이 {seen}개뿐이다");
                var list = RealScenes(ctx, 2, game, prepared);
                for (int i = 0; i < list.Count && seen < count; i++, seen++) visit(game, i, list[i]);
            }
            return (int)game;
        }

        /// <summary>
        /// 실전 장면 시험대의 장면 수. 정본은 <see cref="CanonScenes"/>(1,000 — 합격선을 재는 수), 관문은 줄인 수(dotnet 40 · Unity 20).
        /// 전체 숫자는 관문 밖에서 SINNOH_BENCH_SCENES=1000으로 dotnet을 한 번 돌려 plan-log에 적는다.
        /// </summary>
        public const int CanonScenes = 1000;
#if UNITY_EDITOR
        public static readonly int BenchScenes = 20;
#else
        public static readonly int BenchScenes = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES"), out var n) && n > 0 ? n : 40;
#endif
        /// <summary>강한 AI의 굴리기 한도(시험대). SINNOH_BENCH_SIMS로 바꾼다.</summary>
        public static readonly int BenchSims = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BENCH_SIMS"), out var sims) && sims > 0 ? sims : 64;

        /// <summary>승률 차(천분율)를 「+9.8%p」 꼴로.</summary>
        static string Points(int permille) => (permille < 0 ? "−" : "+") + BenchResult.Percent(Math.Abs(permille)) + "p";

        [Test]
        public void M8_실전장면은시드만으로같게다시만든다()
        {
            // 정본 장면 셋은 봇 판 시드에서만 나온다: 두 번 만들면 장면마다 같은 판(해시) · 같은 종류, 앞 장면 수는 셈과 같다
            var first = new List<ulong>();
            var kinds = new int[3];
            int games = ForEachRealScene(ctx, 30, (g, i, s) =>
            {
                first.Add(StateHash.Of(s));
                kinds[KindOf(s)]++;
            });
            int at = 0;
            ForEachRealScene(ctx, 30, (g, i, s) => Assert.AreEqual(first[at++], StateHash.Of(s), $"판 {g} 장면 {i}"));
            Assert.AreEqual(30, first.Count);
            Assert.AreEqual(30, at);
            Assert.GreaterOrEqual(games, 1);
            Assert.Greater(kinds[0], 0, "조무래기 장면");
            Assert.Greater(kinds[1], 0, "간부 장면");
        }

        [Test, Category("Slow")]
        public void M8_시험대실전장면_봇판의갤럭시단배틀()
        {
            // 01 §13 M8-5·M8-8: 봇이 실제로 키운 파티(기술 카드·지닌물건·진화·운명 손패)로 건 갤럭시단 배틀을 같은 자리에서 원칙 AI와 강한 AI로 다시 돌린다. 플레이어 쪽 = 좌석 봇(Playout.Bot).
            // 합격선 [가정 M8-8]: 전체 장면 합계에서 강한 AI 승률이 원칙 AI보다 GalacticBench.PassMargin(10%p) 이상 — 장면 종류마다의 차는 숫자만 남긴다(정하는 것은 사용자). 탐색 묶음 전까지는 단언하지 않는다.
            var sw = Stopwatch.StartNew();
            var basic = new[] { new BenchResult(), new BenchResult(), new BenchResult() };
            var strong = new[] { new BenchResult(), new BenchResult(), new BenchResult() };
            var agent = new StrongAgent();
            ulong digest = 14695981039346656037UL;
            int scenes = 0;
            int games = ForEachRealScene(ctx, BenchScenes, (game, i, s) =>
            {
                int kind = KindOf(s);
                ulong id = game * 1000 + (ulong)i;
                digest = unchecked((digest ^ StateHash.Of(s)) * 1099511628211UL);
                foreach (bool smart in new[] { false, true })
                {
                    var bot = new Playout.Bot(game);
                    var rng = new Pcg32(id, 0xB07);
                    RolloutPolicy player = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
                    if (smart) GalacticBench.Play(s.Clone(), ctx, agent, player, strong[kind], BenchSims, 1, id, () => sw.ElapsedMilliseconds);
                    else GalacticBench.Play(s.Clone(), ctx, new PrincipleAgent(), player, basic[kind], 0, 1, id);
                }
                scenes++;
            });
            var allBasic = new BenchResult();
            var allStrong = new BenchResult();
            var gaps = new List<string>();
            for (int k = 0; k < SceneKinds.Length; k++)
            {
                Report($"M8 시험대 실전 장면 {SceneKinds[k]} — 원칙: {basic[k]}");
                Report($"M8 시험대 실전 장면 {SceneKinds[k]} — 강한 AI 1단계(굴리기 {BenchSims}): {strong[k]}");
                Assert.AreEqual(0, basic[k].Stuck, $"{SceneKinds[k]}: 원칙 AI 판이 끝까지 가지 못했다");
                Assert.AreEqual(0, strong[k].Stuck, $"{SceneKinds[k]}: 강한 AI 판이 끝까지 가지 못했다");
                Assert.AreEqual(basic[k].Battles, strong[k].Battles);
                allBasic.Add(basic[k]);
                allStrong.Add(strong[k]);
                if (basic[k].Battles > 0)
                    gaps.Add($"{SceneKinds[k]} {basic[k].Battles}: {BenchResult.Percent(basic[k].WinPermille)} → {BenchResult.Percent(strong[k].WinPermille)} ({Points(strong[k].WinPermille - basic[k].WinPermille)})");
            }
            int gap = allStrong.WinPermille - allBasic.WinPermille;
            Report($"M8 시험대 실전 장면: 봇 판 {games}(2인) · 장면 {scenes}{(scenes == CanonScenes ? "(정본)" : $"(줄인 판 — 정본 {CanonScenes})")} · 장면 지문 0x{digest:X16} · 굴리기 {agent.Simulations}(멈춤 {agent.Stuck}) · {sw.ElapsedMilliseconds}ms");
            Report($"M8 합격선[가정 M8-8] 전체 {BenchResult.Percent(allBasic.WinPermille)} → {BenchResult.Percent(allStrong.WinPermille)} = {Points(gap)}(기준 {Points(GalacticBench.PassMargin)} — {(gap >= GalacticBench.PassMargin ? "넘음" : "미달")}) ‖ {string.Join(" · ", gaps)} ‖ 결정당 최대 {allStrong.MaxThinkMs}ms");
            Assert.AreEqual(BenchScenes, scenes, "장면 수");
        }

        // ── M8 「시험대 바로잡기」(결정 108 ①·109): 플레이어 쪽 강한 AI · 팽팽한 장면 · 파티 분포 · 준비된 도전 · 봇 운영 규칙 ──

        /// <summary>팽팽한 장면을 고르는 주사위 수(원칙 AI끼리 — dice 0 = 원본). SINNOH_BENCH_TIGHT_DICE로 바꾼다.</summary>
        public static readonly int TightDice = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BENCH_TIGHT_DICE"), out var td) && td > 0 ? td : 8;
        /// <summary>네 대진에서 장면 하나를 둘 주사위 수(dice 0 = 원본 — 정본 숫자와 같은 판). SINNOH_BENCH_DICE로 바꾼다.</summary>
        public static readonly int BenchDice = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_BENCH_DICE"), out var bd) && bd > 0 ? bd : 1;
        /// <summary>
        /// 바로잡기 시험대(네 대진 · 플레이어 쪽 강한 AI — dotnet 장면당 약 1.3 s)의 장면 수: 관문은 줄인 판(dotnet 10 · Unity 5), SINNOH_BENCH_SCENES를 주면 그 수 그대로(전체 = 1000).
        /// </summary>
#if UNITY_EDITOR
        public static readonly int FixScenes = 5;
#else
        public static readonly int FixScenes = Environment.GetEnvironmentVariable("SINNOH_BENCH_SCENES") != null ? BenchScenes : 10;
#endif
        /// <summary>네 대진의 장면 셋: 운영 규칙을 켠 봇 판(기본) · SINNOH_BENCH_PREPARED=0이면 옛 정본(운영 규칙 끔).</summary>
        public static readonly bool BenchPrepared = Environment.GetEnvironmentVariable("SINNOH_BENCH_PREPARED") != "0";

        static readonly string[] Matchups = { "갤 원칙 · 플 원칙", "갤 강한 · 플 원칙", "갤 원칙 · 플 강한", "갤 강한 · 플 강한" };
        static readonly string[] Bands = { "약함", "비슷함", "강함" };

        /// <summary>장면에 주사위를 달리한 판의 AI 씨 · 봇 씨(dice 0 = 정본과 같은 id).</summary>
        static ulong DiceId(ulong id, int dice) => dice == 0 ? id : id ^ ((ulong)dice << 48);

        /// <summary>
        /// 대진 하나(m: 0 갤 원칙 · 플 원칙 / 1 갤 강한 · 플 원칙 / 2 갤 원칙 · 플 강한 / 3 갤 강한 · 플 강한)를 그 장면 · 주사위로 한 번. 플레이어 원칙 = 좌석 봇(Playout.Bot — 갤럭시단 배틀은 ⑦ 원칙 값),
        /// 플레이어 강한 = 갤럭시단 배틀 안 좌석 선택을 강한 AI가 좌석 보기로(GalacticBench.SeatAgent), 그 밖은 봇.
        /// </summary>
        void PlayMatchup(int m, GameState scene, ulong game, ulong id, int dice, StrongAgent galactic, StrongAgent player, BenchResult into, Func<long> now, int sims = 0, int playerSims = 0)
        {
            if (sims <= 0) sims = BenchSims;
            if (playerSims <= 0) playerSims = sims;
            var s = GalacticBench.Reroll(scene, id, dice);
            ulong key = DiceId(id, dice);
            var bot = new Playout.Bot(game);
            var rng = new Pcg32(key, 0xB07);
            RolloutPolicy seat = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
            if (m >= 2) seat = GalacticBench.SeatAgent(player, seat, playerSims, 2, key);
            if (m % 2 == 1) GalacticBench.Play(s, ctx, galactic, seat, into, sims, 1, key, now);
            else GalacticBench.Play(s, ctx, new PrincipleAgent(), seat, into, 0, 1, key);
        }

        /// <summary>원칙 AI끼리 dice 0..TightDice−1로 둔 갤럭시단 승률(‰, 비김 반 승).</summary>
        int PrincipleRate(GameState scene, ulong game, ulong id)
        {
            var r = new BenchResult();
            for (int d = 0; d < TightDice; d++) PlayMatchup(0, scene, game, id, d, null, null, r, null);
            Assert.AreEqual(0, r.Stuck);
            return r.WinPermille;
        }

        /// <summary>파티 분포 표 한 칸(장면 종류 하나): 합을 모아 평균으로 적는다.</summary>
        sealed class PartyTable
        {
            public int Scenes;
            public long Count, Stage, Full, Tier, HpPermille, Power, HpRatio, Punch, Edge, Round, Shards;
            public readonly int[] Band = new int[3];
            public int Ready;
            public readonly List<int> Powers = new List<int>();

            public void Add(PartyMeasure m, GameState s, int seat)
            {
                Scenes++;
                Count += m.Count;
                Stage += m.StageSum;
                Full += m.FullyEvolved;
                Tier += m.TierSum;
                HpPermille += m.HpPermille;
                Power += m.Power;
                HpRatio += m.HpRatio;
                Punch += m.PunchRatio;
                Edge += m.TypeEdge;
                Round += s.Round;
                Shards += HosFlow.ShardCount(s.Seats[seat]);
                Band[(int)m.Band]++;
                if (m.Ready) Ready++;
                Powers.Add(m.Power);
            }

            static string Avg(long sum, int n) => n == 0 ? "-" : (sum / (double)n).ToString("0.0");

            int Median()
            {
                if (Powers.Count == 0) return 0;
                var l = new List<int>(Powers);
                l.Sort();
                return l[l.Count / 2];
            }

            public override string ToString() => Scenes == 0 ? "장면 0" :
                $"장면 {Scenes} · 포켓몬 {Avg(Count, Scenes)} · 진화 단계 합 {Avg(Stage, Scenes)} · 다 진화 {Avg(Full, Scenes)} · 등급 합 {Avg(Tier, Scenes)} · 남은 체력 {BenchResult.Percent(HpPermille / Scenes)}"
                + $" · 라운드 {Avg(Round, Scenes)} · 조각 {Avg(Shards, Scenes)} · 체력 비 {BenchResult.Percent(HpRatio / Scenes)} · 화력 비 {BenchResult.Percent(Punch / Scenes)} · 상성 {Edge / Scenes / 1000.0:+0.00;-0.00}"
                + $" · 세기 비 평균 {BenchResult.Percent(Power / Scenes)}(가운데 {BenchResult.Percent(Median())}) · 약함 {Band[0]} · 비슷함 {Band[1]} · 강함 {Band[2]} · 준비된 도전 {Ready}";
        }

        static int SeatOf(GameState s)
        {
            var lineup = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup && !x.Answered);
            return lineup != null ? lineup.Subject : s.Battle.Sides[0].Owner;
        }

        [Test]
        public void M8_파티재기_같은파티끼리는세기비1000이고센파티가크다()
        {
            // 같은 파티를 상대로 잰 세기 비 = 1000(체력 비 · 화력 비 모두 같음), 상성 0. 진화체로 바꾼 파티는 세기 비가 커진다.
            var s = Scene.Game(ctx, 1);
            s.Seats[0].OpenSlots = 3;
            foreach (var key in new[] { "393:0", "396:0", "399:0" }) Scene.Give(s, ctx, 0, key); // 팽도리 · 찌르꼬 · 비버니
            var foes = new List<PokemonState>();
            foreach (int id in s.Seats[0].Party)
                if (id >= 0) foes.Add(s.Pokemon[id]);
            var same = PartyGauge.Measure(s, ctx, 0, foes, foes.Count);
            Assert.AreEqual(3, same.Count);
            Assert.AreEqual(1000, same.HpRatio);
            Assert.AreEqual(1000, same.PunchRatio);
            Assert.AreEqual(0, same.TypeEdge);
            Assert.AreEqual(1000, same.Power);
            Assert.AreEqual(PartyBand.Even, same.Band);
            Assert.IsTrue(same.Ready);
            Assert.AreEqual(0, same.StageSum);

            var t = Scene.Game(ctx, 1, 8);
            t.Seats[0].OpenSlots = 3;
            foreach (var key in new[] { "395:2", "398:2", "400:0" }) Scene.Give(t, ctx, 0, key); // 엠페르트 · 찌르호크 · 비버통
            var strong = PartyGauge.Measure(t, ctx, 0, foes, foes.Count);
            Assert.Greater(strong.Power, PartyGauge.EvenHigh, $"진화체 파티: {strong.Power}");
            Assert.AreEqual(PartyBand.Strong, strong.Band);
            Assert.GreaterOrEqual(strong.StageSum, 3);
            // 다친 포켓몬은 남은 체력만, 쓰러진 포켓몬은 세지 않는다
            s.Pokemon[s.Seats[0].Party[0]].Damage = PokemonRules.MaxHp(ctx, s.Pokemon[s.Seats[0].Party[0]]);
            var hurt = PartyGauge.Measure(s, ctx, 0, foes, foes.Count);
            Assert.AreEqual(2, hurt.Count);
            Assert.Less(hurt.Power, 1000);
            Assert.IsFalse(hurt.Ready, "상대가 낼 수(3)보다 싸울 포켓몬이 적다");
            // 장면에서: 조무래기 = 열린 배틀의 두 마리, 간부 · 태홍 = 명단 고르기의 낼 수
            Assert.AreEqual(BattleFlow.GruntLineup, PartyGauge.OfScene(GalacticBench.Scene(ctx, BenchScene.Grunt, 0), ctx).FoeCount);
            Assert.AreEqual(HosFlow.BossLineup, PartyGauge.OfScene(GalacticBench.Scene(ctx, BenchScene.Cyrus, 0), ctx).FoeCount);
            var c = GalacticBench.Scene(ctx, BenchScene.Commander, 1);
            Assert.AreEqual(HosFlow.CommanderLineup(HosFlow.ShardCount(c.Seats[0])), PartyGauge.OfScene(c, ctx).FoeCount);
        }

        [Test]
        public void M8_팽팽한장면은시드만으로같게다시만든다()
        {
            // 팽팽한 장면 = 원칙 AI끼리 TightDice 주사위에서 갤럭시단 승률 30~70%(GalacticBench.TightLow·High). 장면 셋(운영 규칙 켠 봇 판)과 주사위가 시드에서만 나오므로 두 번 고르면 같은 장면 · 같은 승률.
            int n = 24;
            var first = new List<(ulong hash, int rate)>();
            ForEachRealScene(ctx, n, (g, i, s) => first.Add((StateHash.Of(s), PrincipleRate(s, g, g * 1000 + (ulong)i))), true);
            int at = 0, tight = 0;
            ForEachRealScene(ctx, n, (g, i, s) =>
            {
                Assert.AreEqual(first[at].hash, StateHash.Of(s), $"판 {g} 장면 {i}");
                int rate = PrincipleRate(s, g, g * 1000 + (ulong)i);
                Assert.AreEqual(first[at].rate, rate, $"판 {g} 장면 {i}: 승률");
                if (rate >= GalacticBench.TightLow && rate <= GalacticBench.TightHigh) tight++;
                at++;
            }, true);
            Assert.AreEqual(n, at);
            // 주사위를 바꾸면 판이 달라지고 원본(dice 0)은 그대로다
            ForEachRealScene(ctx, 1, (g, i, s) =>
            {
                Assert.AreEqual(StateHash.Of(s), StateHash.Of(GalacticBench.Reroll(s, 5, 0)));
                Assert.AreNotEqual(StateHash.Of(s), StateHash.Of(GalacticBench.Reroll(s, 5, 1)));
                Assert.AreEqual(StateHash.Of(GalacticBench.Reroll(s, 5, 3)), StateHash.Of(GalacticBench.Reroll(s, 5, 3)));
            }, true);
            Report($"M8 팽팽한 장면(앞 {n}장면 · 주사위 {TightDice}): {tight}");
        }

        [Test]
        public void M8_엿보기없음_플레이어쪽도숨은정보만다른두원본에서결정이같다()
        {
            // 플레이어 쪽 강한 AI(좌석 보기): 그 좌석에 안 보이는 것(덱 순서 · 숨겨진 장소 · 게임 난수)만 다른 두 원본에서 채운 판과 결정이 같다. 자기 운명 손패는 보이니 그대로 둔다.
            int decisions = 0;
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
                        if (c.Seat == 0 && legal.Count > 1)
                        {
                            asked++;
                            var b = a.Clone();
                            Scramble(b, seed * 31 + (ulong)step, 0);
                            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b), "두 원본은 달라야 한다(숨은 정보)");
                            var va = Engine.ViewFor(a, 0);
                            var vb = Engine.ViewFor(b, 0);
                            Assert.AreEqual(StateHash.Of(va), StateHash.Of(vb), "두 원본의 좌석 보기는 같아야 한다");
                            Assert.AreEqual(StateHash.Of(Determinizer.Fill(va, ctx, SampledRng.ForSample(11, 2))), StateHash.Of(Determinizer.Fill(vb, ctx, SampledRng.ForSample(11, 2))),
                                $"{scene} 시드 {seed} 걸음 {step}: 채운 판이 숨은 정보에 따라 달라졌다");
                            var agentA = new StrongAgent();
                            var agentB = new StrongAgent();
                            var budget = new AgentBudget { MaxSimulations = 48, Seed = 4321 + (ulong)c.Id };
                            var da = agentA.Decide(new AgentInput { View = va, Choice = va.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            var db = agentB.Decide(new AgentInput { View = vb, Choice = vb.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, budget);
                            Assert.IsTrue(da.Answer.SameAs(db.Answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub}: {da.Answer} ≠ {db.Answer}");
                            Assert.AreEqual(agentA.Simulations, agentB.Simulations);
                            Assert.Greater(agentA.Simulations, 0, "플레이어 쪽 결정도 굴리기를 돌렸다");
                            Assert.AreEqual(0, agentA.Stuck, "멈춘 굴리기");
                            Assert.IsTrue(legal.Exists(x => x.SameAs(da.Answer)), "AI 답은 합법 답");
                            decisions++;
                            answer = da.Answer;
                        }
                        Assert.IsTrue(Rollout.Step(a, ctx, c, answer), $"{scene} 시드 {seed} {c.Kind}/{c.Sub} {answer}");
                    }
                }
            Assert.GreaterOrEqual(decisions, 15, "견준 결정 수");
        }

        /// <summary>
        /// 운영 규칙을 끈 봇 판(정본)의 앞 30장면 지문(이 묶음에서 잼 — 같은 판의 1,000장면 지문이 시험대 고정 때 값 0x67AACF3D5C30502D와 같음을 관문 밖 시험대로 확인).
        /// 11-20 M9 실행 감사 K1: 좌석 상태에 ScoopNext가 들어 지문 값만 바뀜(0x11285D9966D83432 → — 그 칸을 빼고 세면 옛 값 그대로임을 확인, 봇 판의 수는 같다 · 관문 밖 1,000장면 지문도 같은 까닭으로 바뀐다 — 결정 122 · K1 · K2 · 결정 131 · 134 뒤 0x5ADDC36BE943907B, 11-20 M8 다시 재기 · 강함최종 시험대가 찍는다).
        /// </summary>
        const ulong OldScenes30 = 0x713819961293F202UL;

        [Test]
        public void M8_봇운영규칙은켠판만바꾼다()
        {
            // 운영 규칙(⑭)을 끈 판(기본)은 예전 봇 판 그대로 — 정본 앞 30장면 지문이 그대로다(1,000장면 전체 지문은 관문 밖 시험대 `M8_시험대_강함최종_네대진`이 찍는다 — 11-20 0x5ADDC36BE943907B).
            // 켠 판은 갤럭시단 도전이 준비된 쪽으로 옮겨 간다.
            ulong off = 14695981039346656037UL;
            ForEachRealScene(ctx, 30, (g, i, s) => off = unchecked((off ^ StateHash.Of(s)) * 1099511628211UL));
            Report($"M8 봇 운영 규칙 끔: 앞 30장면 지문 0x{off:X16}");
            Assert.AreEqual(OldScenes30, off, "운영 규칙을 끈 봇 판이 달라졌다");
            var before = new PartyTable();
            var after = new PartyTable();
            ForEachRealScene(ctx, 60, (g, i, s) => before.Add(PartyGauge.OfScene(s, ctx), s, SeatOf(s)));
            int games = ForEachRealScene(ctx, 60, (g, i, s) => after.Add(PartyGauge.OfScene(s, ctx), s, SeatOf(s)), true);
            Report($"M8 봇 운영 규칙 끔: {before}");
            Report($"M8 봇 운영 규칙 켬(봇 판 {games}): {after}");
            Assert.Greater(after.Ready * before.Scenes, before.Ready * after.Scenes, "켠 판의 준비된 도전 비율이 더 높다");
        }

        /// <summary>사람 자료(Logs/Review/m8/human-parties.md — 원작 커뮤니티 확인, 이 PC 안)에서 종 이름을 알 수 있는 파티: (날짜, 상대, 파티 영문 이름, 결과).</summary>
        static readonly (string when, GalacticTrainer foe, string[] party, string result)[] HumanParties =
        {
            ("2022-02-09 v1.2b", GalacticTrainer.MaleGrunt, new[] { "Torterra", "Cranidos", "Golduck" }, "이김(한 마리도 안 잃음)"),
            ("2023-05-12 v1.4a", GalacticTrainer.MaleGrunt, new[] { "Machamp", "Tyrogue", "Hitmonchan" }, "이김"),
            ("2025-07-29 v1.4a", GalacticTrainer.MaleGrunt, new[] { "Torterra" }, "짐(Murkrow에)"),
            ("2025-08-14 v1.4a", GalacticTrainer.Jupiter, new[] { "Decidueye", "Umbreon", "Alakazam" }, "간신히 이김"),
            ("2025-08-14 v1.4a", GalacticTrainer.Jupiter, new[] { "Bastiodon" }, "쉽게 이김(+진화의 휘석, 혼자 둘)"),
            ("2022-05-06 v1.3c", GalacticTrainer.Cyrus, new[] { "Samurott", "Luxray", "Flareon", "Umbreon", "Togekiss", "Blissey" }, "짐(아르세우스에)"),
        };

        [Test]
        public void M8_사람파티_세기비()
        {
            // 사람 파티를 같은 잣대(PartyGauge — 시그니처 기술만 · 체력 가득 · 지닌물건 없음)로 잰다: 파티 강도 칸 「비슷함」(800~1250‰)을 맞추는 근거. 상대 = 그 인물 카드 평균 × 낼 수.
            var lines = new List<string>();
            int found = 0;
            foreach (var h in HumanParties)
            {
                var s = Scene.Game(ctx, 1);
                s.Seats[0].OpenSlots = 6;
                var missing = new List<string>();
                foreach (var name in h.party)
                {
                    int def = -1;
                    for (int d = 0; d < ctx.Cards.Pokemon.Count && def < 0; d++)
                    {
                        var p = ctx.Cards.Pokemon[d];
                        if (p.Trainer == GalacticTrainer.None && p.State <= 1 && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) def = d;
                    }
                    if (def < 0)
                    {
                        missing.Add(name);
                        continue;
                    }
                    PokemonRules.AddToParty(s.Seats[0], PokemonRules.Spawn(s, def, 0, (int)DeckId.Weak));
                }
                if (missing.Count == h.party.Length)
                {
                    lines.Add($"{h.when} {h.foe}: {string.Join("·", h.party)} — 로스터에 없음");
                    continue;
                }
                var m = PartyGauge.Against(s, ctx, 0, h.foe);
                if (h.foe == GalacticTrainer.Jupiter) Assert.AreEqual(2, m.FoeCount, "조각 0 = 간부 2마리");
                found++;
                lines.Add($"{h.when} {h.foe} {h.result}: {string.Join("·", h.party)}{(missing.Count > 0 ? $"(로스터에 없음 {string.Join("·", missing)})" : "")} — 세기 비 {BenchResult.Percent(m.Power)} · 체력 비 {BenchResult.Percent(m.HpRatio)} · 화력 비 {BenchResult.Percent(m.PunchRatio)} · 상성 {m.TypeEdge / 1000.0:+0.00;-0.00} · {Bands[(int)m.Band]}");
            }
            foreach (var l in lines) Report($"M8 사람 파티: {l}");
            Assert.GreaterOrEqual(found, 4, "로스터에서 찾은 사람 파티 수");
        }

        [Test, Category("Slow")]
        public void M8_시험대바로잡기_네대진_팽팽한장면_파티강도()
        {
            // 결정 108 ①·109 ①: 장면(기본 = 운영 규칙 켠 봇 판)마다 네 대진(갤럭시단 원칙/강한 × 플레이어 원칙/강한)을 BenchDice 주사위로 두고,
            // 종류(조무래기 · 간부 · 태홍) · 팽팽한 장면(원칙끼리 TightDice 주사위 30~70%) · 준비된 도전 · 파티 강도 칸(약함 · 비슷함 · 강함)마다 승률을 낸다. 합격선은 아직 걸지 않는다(plan M8 「남은 결정」 ①).
            var sw = Stopwatch.StartNew();
            const int Groups = 9; // 0..2 종류 · 3 전체 · 4 팽팽 · 5 준비 · 6..8 칸
            var res = new BenchResult[Matchups.Length, Groups];
            for (int m = 0; m < Matchups.Length; m++)
                for (int g = 0; g < Groups; g++) res[m, g] = new BenchResult();
            var tables = new[] { new PartyTable(), new PartyTable(), new PartyTable() };
            var galactic = new StrongAgent();
            var player = new StrongAgent();
            ulong digest = 14695981039346656037UL, tightDigest = 14695981039346656037UL;
            int scenes = 0, tight = 0;
            var tightKinds = new int[3];
            var groups = new List<int>(5);
            int games = ForEachRealScene(ctx, FixScenes, (game, i, s) =>
            {
                int kind = KindOf(s);
                ulong id = game * 1000 + (ulong)i;
                ulong h = StateHash.Of(s);
                digest = unchecked((digest ^ h) * 1099511628211UL);
                var measure = PartyGauge.OfScene(s, ctx);
                tables[kind].Add(measure, s, SeatOf(s));
                int rate = PrincipleRate(s, game, id);
                bool isTight = rate >= GalacticBench.TightLow && rate <= GalacticBench.TightHigh;
                groups.Clear();
                groups.Add(kind);
                groups.Add(3);
                if (isTight)
                {
                    tight++;
                    tightKinds[kind]++;
                    tightDigest = unchecked((tightDigest ^ h) * 1099511628211UL);
                    groups.Add(4);
                }
                if (measure.Ready) groups.Add(5);
                groups.Add(6 + (int)measure.Band);
                for (int m = 0; m < Matchups.Length; m++)
                    for (int d = 0; d < BenchDice; d++)
                    {
                        var one = new BenchResult();
                        PlayMatchup(m, s, game, id, d, galactic, player, one, () => sw.ElapsedMilliseconds);
                        foreach (int g in groups) res[m, g].Add(one);
                    }
                scenes++;
            }, BenchPrepared);

            string set = BenchPrepared ? "운영 규칙 켠 봇 판" : "옛 정본(운영 규칙 끔)";
            Report($"M8 바로잡기: {set} {games}판(2인) · 장면 {scenes}{(scenes == CanonScenes ? "(1,000)" : "(줄인 판)")} · 장면 지문 0x{digest:X16} · 주사위 {BenchDice} · 강한 AI 굴리기 {BenchSims} · 갤 굴리기 {galactic.Simulations}(멈춤 {galactic.Stuck}) · 플 굴리기 {player.Simulations}(멈춤 {player.Stuck}) · {sw.ElapsedMilliseconds}ms");
            Report($"M8 바로잡기 팽팽한 장면(원칙끼리 주사위 {TightDice} · {BenchResult.Percent(GalacticBench.TightLow)}~{BenchResult.Percent(GalacticBench.TightHigh)}): {tight}(조무래기 {tightKinds[0]} · 간부 {tightKinds[1]} · 태홍 {tightKinds[2]}) · 지문 0x{tightDigest:X16}");
            for (int k = 0; k < 3; k++) Report($"M8 바로잡기 파티 분포 {SceneKinds[k]}: {tables[k]}");
            string[] names = { SceneKinds[0], SceneKinds[1], SceneKinds[2], "전체", "팽팽", "준비된 도전", Bands[0], Bands[1], Bands[2] };
            for (int m = 0; m < Matchups.Length; m++)
            {
                var parts = new List<string>();
                for (int g = 0; g < Groups; g++) parts.Add($"{names[g]} {res[m, g].Battles}: {BenchResult.Percent(res[m, g].WinPermille)}");
                Report($"M8 바로잡기 [{Matchups[m]}] {string.Join(" · ", parts)} · 플레이어 남은 체력 {BenchResult.Percent(res[m, 3].PlayerHpPermille)}{(m % 2 == 1 ? $" · 결정당 최대 {res[m, 3].MaxThinkMs}ms" : "")}");
            }
            for (int m = 0; m < Matchups.Length; m++)
                for (int g = 0; g < Groups; g++) Assert.AreEqual(0, res[m, g].Stuck, $"{Matchups[m]} {names[g]}: 끝까지 가지 못한 배틀");
            Assert.AreEqual(0, galactic.Stuck);
            Assert.AreEqual(0, player.Stuck);
            Assert.AreEqual(FixScenes, scenes, "장면 수");
            Assert.AreEqual(scenes * BenchDice, res[0, 3].Battles);
            Assert.Greater(player.Simulations, 0, "플레이어 쪽 강한 AI가 굴렸다");
        }
    }
}
