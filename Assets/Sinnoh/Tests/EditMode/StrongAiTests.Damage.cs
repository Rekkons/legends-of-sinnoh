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
    /// 결정 143 ①: 지는 쪽(야생 · UB — 가상 쪽 Wild · UltraBeast) 강한 AI 점수의 피해 · 쓰러뜨린 몫(<see cref="StrongAgent.Underdog"/>).
    /// 장면 = 봇 판에서 지는 쪽 가상 응답자의 첫 배틀 선택(합법 답 둘 이상)이 열린 호스트 원본(배틀마다 하나), 배틀 끝까지 지는 쪽 = 강한 AI(관전자 보기) · 좌석 = 봇.
    /// </summary>
    public partial class StrongAiTests
    {
        /// <summary>지는 쪽 장면 재기의 결과(장면 여럿 합).</summary>
        sealed class UnderdogResult
        {
            public int Battles, AiWins, Stuck;
            /// <summary>좌석 파티가 잃은 체력 합 · 쓰러진 수 합 · 지는 쪽이 낸 기술 수(UseMove 답).</summary>
            public long Damage, Knockouts, Moves, Decisions, ThinkTicks;

            public override string ToString() =>
                $"배틀 {Battles} · 준 피해 {Damage}(배틀당 {Avg(Damage, Battles)} · 기술당 {Avg(Damage, Moves)}) · 쓰러뜨림 {Knockouts}(배틀당 {Avg(Knockouts, Battles)}) · 기술 {Moves} · 지는 쪽 승 {AiWins}"
                + (Stuck > 0 ? $" · 멈춤 {Stuck}" : "");

            static string Avg(long sum, long n) => n > 0 ? $"{sum * 100 / n / 100}.{sum * 100 / n % 100:00}" : "0";
        }

        /// <summary>지는 쪽 장면: 울트라 버스트 · 신오의 심장 2인 봇 판(시드 0부터, 원칙 AI 판 — 판 지문 그대로)에서 배틀마다 첫 지는 쪽 선택. count개를 채우면 멈춘다.</summary>
        static List<(ulong game, GameState scene)> UnderdogScenes(Ctx ctx, int count)
        {
            var list = new List<(ulong, GameState)>();
            var legal = new List<ChoiceAnswer>();
            for (ulong seed = 0; list.Count < count; seed++)
            {
                if (seed > (ulong)count) throw new InvalidOperationException($"봇 판 {seed}개에서 지는 쪽 장면이 {list.Count}개뿐이다");
                foreach (var campaign in new[] { Campaign.UltraBurst, Campaign.HeartOfSinnoh })
                {
                    int last = -1;
                    ulong game = seed * 2 + (campaign == Campaign.UltraBurst ? 0UL : 1UL);
                    Playout.Run(ctx, 2, seed, s =>
                    {
                        var b = s.Battle;
                        if (list.Count >= count || b == null || b.Id == last) return;
                        var c = s.Pending.Find(x => !x.Answered);
                        if (c == null || c.Seat >= 0 || c.Kind == ChoiceKind.Lineup) return;
                        int k = ChoiceRules.BattleSideOf(s, c.Seat);
                        if (k < 0 || (b.Sides[k].Owner != SeatId.Wild && b.Sides[k].Owner != SeatId.UltraBeast) || b.Sides[1 - k].Owner < 0) return;
                        legal.Clear();
                        Engine.Legal(s, ctx, c, legal);
                        if (legal.Count < 2) return;
                        last = b.Id;
                        list.Add((game, s.Clone()));
                    }, null, campaign);
                    if (list.Count >= count) break;
                }
            }
            return list;
        }

        /// <summary>좌석 파티의 남은 체력 합 · 쓰러진 수.</summary>
        static (int hp, int fainted) SeatParty(GameState s, Ctx cx, int seat)
        {
            int hp = 0, fainted = 0;
            foreach (int id in s.Seats[seat].Party)
            {
                if (id < 0 || s.Pokemon[id].Def < 0) continue;
                int h = PokemonRules.Hp(cx, s.Pokemon[id]);
                hp += h;
                if (h <= 0) fainted++;
            }
            return (hp, fainted);
        }

        /// <summary>장면 하나를 배틀 끝까지: 지는 쪽 = ai(관전자 보기, 씨 = 장면 · 선택 Id), 좌석 = 봇(Playout.Bot — 시험대와 같은 꼴). 판 s는 그 자리에서 고친다.</summary>
        static void PlayUnderdog(GameState s, Ctx cx, ulong game, ulong id, IAgent ai, UnderdogResult res)
        {
            var c0 = s.Pending.Find(x => !x.Answered);
            int k = ChoiceRules.BattleSideOf(s, c0.Seat);
            int seat = s.Battle.Sides[1 - k].Owner;
            var before = SeatParty(s, cx, seat);
            var bot = new Playout.Bot(game);
            var rng = new Pcg32(id, 0xB07);
            var noLog = new List<GameEvent>();
            var sw = new Stopwatch();
            RolloutPolicy policy = (g, x, c, legal) =>
            {
                if (c.Seat >= 0) return bot.Answer(g, x, c, rng);
                var view = Engine.ViewFor(g, SeatId.Observer);
                sw.Restart();
                var d = ai.Decide(new AgentInput { View = view, Choice = view.Pending.Find(p => p.Id == c.Id), Ctx = x, PublicLog = noLog },
                    new AgentBudget { Seed = SampledRng.Mix(id ^ ((ulong)c.Id << 24)) });
                if (legal.Count > 1)
                {
                    res.Decisions++;
                    res.ThinkTicks += sw.ElapsedTicks;
                }
                if (d.Answer.Kind == AnswerKind.UseMove) res.Moves++;
                foreach (var a in legal)
                    if (a.SameAs(d.Answer)) return a;
                throw new InvalidOperationException($"장면 {id}: AI 답 {d.Answer}가 {c.Kind}/{c.Sub}의 합법 답이 아니다");
            };
            var r = Rollout.ToEnd(s, cx, policy);
            res.Battles++;
            if (!r.Ended)
            {
                res.Stuck++;
                return;
            }
            // 배틀 끝(귀환 · 회복 전) 체력은 굴리기 결과에서 — ToEnd가 돌려준 판은 이미 닫힌 뒤다
            res.Damage += before.hp - r.Hp[1 - k];
            res.Knockouts += r.Fainted[1 - k] - before.fainted;
            if (r.Winner == k) res.AiWins++;
        }

        /// <summary>옛 점수(결정 143 전 — 피해 몫 100 · 쓰러뜨린 몫 0)로 되돌린 강한 AI.</summary>
        static StrongAgent OldScore(StrongAgent a)
        {
            a.Underdog = StrongAgent.OldWeights;
            return a;
        }

        /// <summary>관문의 지는 쪽 장면 수 · 굴리기 한도(줄인 판 — 「강함」 한 벌의 탐색 꼴 · 깊이, 시계 없음 = 결정적).</summary>
        const int UnderdogGateScenes = 16, UnderdogGateSims = 128;

        [Test]
        public void M10_결정143_지는쪽강한AI는옛점수보다피해를더준다()
        {
            // 결정 143 ①: 같은 장면 · 같은 씨 · 같은 굴리기 한도에서 새 점수(StrongAgent.DefaultUnderdog)가 옛 점수(피해 100만)보다 좌석 파티에 피해를 더 준다.
            // 굴리기 한도로 끝나는 결정이라 결정적이다(숫자가 바뀌면 탐색 · 규칙이 바뀐 것 — 보고 줄을 보고 고친다)
            var scenes = UnderdogScenes(ctx, UnderdogGateScenes);
            var res = new[] { new UnderdogResult(), new UnderdogResult() };
            for (int v = 0; v < 2; v++)
            {
                var a = new StrongAgent { Search = SearchKind.Tree, Depth = StrongAgent.StrongDepth, MaxSimulations = UnderdogGateSims, MaxSamples = StrongAgent.StrongSamples };
                if (v == 0) OldScore(a);
                for (int i = 0; i < scenes.Count; i++) PlayUnderdog(scenes[i].scene.Clone(), ctx, scenes[i].game, (ulong)i, a, res[v]);
                Assert.AreEqual(0, a.Mismatch);
            }
            Report($"M10 결정 143 지는 쪽(관문 장면 {scenes.Count} · 굴리기 {UnderdogGateSims}): 옛 점수 {res[0]} / 새 점수 {res[1]}");
            Assert.AreEqual(0, res[0].Stuck + res[1].Stuck);
            Assert.Greater(res[1].Damage, res[0].Damage, "새 점수가 좌석 파티에 피해를 더 준다");
        }

        /// <summary>야생 배틀(좌석 0 = 불꽃숭이 · 야생 = 비버니 체력 1, 몬스터볼 1개)의 좌석 첫 배틀 행동 자리.</summary>
        GameState CatchOrFaintScene()
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "390:0");
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            int wild = Scene.Spawn(s, ctx, "399:0", SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            Battles.Wild(s, ctx, null, 0, wild);
            return s;
        }

        /// <summary>그 장면에서 좌석이 first(kind 답)를 낸 뒤 원칙으로 배틀 끝까지 — 시드를 넣어 보며 ok인 첫 굴리기.</summary>
        RolloutResult CatchOrFaint(AnswerKind kind, Func<RolloutResult, bool> ok)
        {
            RolloutResult found = null;
            Battles.Seed(seed =>
            {
                var s = CatchOrFaintScene();
                var c = Battles.Only(s, ChoiceKind.BattleAction);
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(s, ctx, c, legal);
                var first = legal.Find(a => a.Kind == kind);
                Assert.IsNotNull(first, $"{kind} 답");
                s.Rng = new Pcg32(seed);
                var r = Rollout.ToEnd(s, ctx, Rollout.Principle, c.Id, first);
                if (!r.Ended || !ok(r)) return false;
                found = r;
                return true;
            });
            return found;
        }

        [Test]
        public void M10_결정143_잡힘은쓰러짐보다야생점수가높지않다()
        {
            // 잡힌 포켓몬이 바로 좌석 파티에 들어가도(AddToParty) 굴리기 결과의 좌석 파티는 배틀에 들어온 그대로다 — 이번 배틀 상대 명단에서 온 포켓몬은 세지 않는다.
            // 옛 셈은 잡힌 비버니(체력 1)의 최대 체력을 좌석 쪽에 더해 야생 쪽 「준 피해」 몫이 생겼다 → 야생 AI가 잡힘을 쓰러짐보다 높게 쳤다
            var start = CatchOrFaintScene();
            var (hp0, _) = SeatParty(start, ctx, 0);
            var caught = CatchOrFaint(AnswerKind.ThrowBall, r => r.Result == BattleResult.Captured);
            var fainted = CatchOrFaint(AnswerKind.UseMove, r => r.Result != BattleResult.Captured && r.Winner == 0 && r.Hp[0] == hp0);
            int wildSide = 1;
            int c = StrongAgent.Score(StrongAgent.WinOf(caught, wildSide), caught, wildSide, StrongAgent.DefaultUnderdog);
            int f = StrongAgent.Score(StrongAgent.WinOf(fainted, wildSide), fainted, wildSide, StrongAgent.DefaultUnderdog);
            Assert.LessOrEqual(c, f, $"잡힘 {c} ≤ 쓰러짐 {f}");
            Assert.AreEqual(hp0, caught.Hp[0], "잡힌 포켓몬은 좌석 쪽 체력에 들지 않는다");
            Assert.AreEqual(fainted.MaxHp[0], caught.MaxHp[0], "최대 체력도 배틀에 들어온 파티만");
            Assert.AreEqual(1, caught.Members[0]);
            Assert.AreEqual(1000, caught.TurnHpPermille(0));
        }

        [Test]
        public void M10_결정143_체력비율은0부터1000까지()
        {
            // 폼이 배틀 끝에 되돌아가 최대 체력이 줄어도(남은 체력이 셀 때의 최대보다 크다) 비율은 0~1000 — 피해 몫이 음수 · 점수가 상한 밖으로 가지 않는다
            var r = new RolloutResult();
            r.Hp[0] = 120;
            r.MaxHp[0] = 100;
            r.TurnHp[0] = 130;
            r.TurnMaxHp[0] = 100;
            r.Members[0] = 1;
            Assert.AreEqual(1000, r.HpPermille(0));
            Assert.AreEqual(1000, r.TurnHpPermille(0));
            Assert.AreEqual(0, r.FaintedPermille(0));
            r.Winner = 1;
            var w = StrongAgent.DefaultUnderdog;
            int score = StrongAgent.Score(StrongAgent.WinOf(r, 1), r, 1, w);
            Assert.AreEqual(StrongAgent.WinScore, score, "피해 몫 0 — 음수 아님");
            Assert.LessOrEqual(score, w.Top);
        }

        [Test]
        public void M10_결정143_갤럭시단과명단은옛점수그대로()
        {
            // 지는 쪽 무게는 야생 · UB 쪽만: 갤럭시단 배틀 · 명단의 답 · 찾은 양(굴리기 · 노드)은 무게를 바꿔도 같다(시험대 · 결정 지문 그대로).
            // 장면은 실제로 찾아보는 자리만(가상 응답자 · 합법 답 둘 이상 · 표본 > 0) — 갤럭시단 배틀 선택과 명단 둘 다
            Assert.AreEqual(500, StrongAgent.ExploreFor(500, StrongAgent.OldWeights), "옛 점수의 탐험 무게는 그대로");
            Assert.AreEqual(500 * 3500 / 1100, StrongAgent.ExploreFor(500, StrongAgent.DefaultUnderdog), "지는 쪽은 점수 상한에 비례");
            var scenes = RealScenes(ctx, 2, 0);
            var legal = new List<ChoiceAnswer>();
            int battles = 0, lineups = 0;
            for (int i = 0; i < scenes.Count && (battles < 2 || lineups < 2); i++)
            {
                // 배틀 장면은 첫 선택이 좌석 것일 수 있다 — 원칙으로 앞으로 돌려 갤럭시단 쪽 선택(합법 답 둘 이상)이 열린 자리까지
                var s = scenes[i].Clone();
                PendingChoice c = null;
                for (int step = 0; step < 60; step++)
                {
                    c = s.Pending.Find(x => !x.Answered);
                    if (c == null) break;
                    legal.Clear();
                    Engine.Legal(s, ctx, c, legal);
                    if ((c.Kind == ChoiceKind.Lineup || (s.Battle != null && c.Seat < 0)) && legal.Count > 1) break;
                    if (s.Battle == null && c.Kind != ChoiceKind.Lineup || legal.Count == 0 || !Rollout.Step(s, ctx, c, Rollout.Principle(s, ctx, c, legal)))
                    {
                        c = null;
                        break;
                    }
                    c = null;
                }
                if (c == null) continue;
                var view = Engine.ViewFor(s, SeatId.Observer);
                c = view.Pending.Find(x => x.Id == c.Id);
                bool lineup = c.Kind == ChoiceKind.Lineup;
                if (!lineup)
                {
                    Assert.Less(c.Seat, 0, $"장면 {i}: 가상 응답자");
                    Assert.AreEqual(BattleKind.Galactic, view.Battle.Kind, $"장면 {i}");
                    Assert.AreEqual(SeatId.Galactic, view.Battle.Sides[ChoiceRules.BattleSideOf(view, c.Seat)].Owner, $"장면 {i}: 갤럭시단 쪽");
                }
                var input = new AgentInput { View = view, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() };
                var oldA = new StrongAgent { Search = SearchKind.Tree, Depth = StrongAgent.StrongDepth, MaxSimulations = 64 };
                var newA = new StrongAgent { Search = SearchKind.Tree, Depth = StrongAgent.StrongDepth, MaxSimulations = 64, Underdog = new ScoreWeights(7, 9, 11) };
                var a = oldA.Decide(input, new AgentBudget { Seed = (ulong)i });
                var w = newA.Decide(input, new AgentBudget { Seed = (ulong)i });
                Assert.Greater(oldA.LastSamples, 0, $"장면 {i}: 찾아봤다");
                Assert.Greater(oldA.Simulations, 0, $"장면 {i}");
                Assert.AreEqual(oldA.Simulations, newA.Simulations, $"장면 {i}: 굴리기 수");
                Assert.AreEqual(oldA.Nodes, newA.Nodes, $"장면 {i}: 노드 수");
                Assert.IsTrue(a.Answer.SameAs(w.Answer), $"장면 {i}({c.Kind})");
                if (lineup) lineups++;
                else battles++;
            }
            Assert.GreaterOrEqual(battles, 2, "갤럭시단 배틀 선택");
            Assert.GreaterOrEqual(lineups, 2, "명단");
        }

        [Test, Explicit("결정 143 ① 전후 재기 — 지는 쪽 장면(SINNOH_UNDERDOG_SCENES, 없으면 60) × 「강함」 한 벌(시계 없음), 수 분")]
        public void M10_결정143_지는쪽전후재기()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_UNDERDOG_SCENES"), out int n) && n > 0 ? n : 60;
            var scenes = UnderdogScenes(ctx, count);
            var kinds = new Dictionary<BattleKind, int>();
            foreach (var (_, s) in scenes) kinds[s.Battle.Kind] = (kinds.TryGetValue(s.Battle.Kind, out int m) ? m : 0) + 1;
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add($"{kv.Key} {kv.Value}");
            Report($"M10 결정 143 지는 쪽 장면 {scenes.Count}: {string.Join(" · ", parts)}");
            // 견줄 줄: 원칙 · 옛 점수(SINNOH_UNDERDOG_OLD=0이면 뺀다) · 새 점수 무게(SINNOH_UNDERDOG_W = 「피해,쓰러뜨림,이번 턴;…」 — 없으면 기본 무게 하나)
            var rows = new List<(string name, IAgent agent)> { ("원칙", new PrincipleAgent()) };
            if (Environment.GetEnvironmentVariable("SINNOH_UNDERDOG_OLD") != "0") rows.Add(("옛 점수", OldScore(StrongAgent.Strong())));
            var spec = Environment.GetEnvironmentVariable("SINNOH_UNDERDOG_W");
            if (string.IsNullOrEmpty(spec)) rows.Add(("새 점수", StrongAgent.Strong()));
            else
                foreach (var one in spec.Split(';'))
                {
                    var w = Array.ConvertAll(one.Split(','), int.Parse);
                    var a = StrongAgent.Strong();
                    a.Underdog = new ScoreWeights(w[0], w[1], w[2]);
                    rows.Add(($"무게 {one}", a));
                }
            for (int v = 0; v < rows.Count; v++)
            {
                var all = new UnderdogResult();
                var ub = new UnderdogResult();
                var wild = new UnderdogResult();
                var (name, agent) = rows[v];
                for (int i = 0; i < scenes.Count; i++)
                {
                    var one = new UnderdogResult();
                    var kind = scenes[i].scene.Battle.Kind;
                    PlayUnderdog(scenes[i].scene.Clone(), ctx, scenes[i].game, (ulong)i, agent, one);
                    // 장면별 원자료(잡음 · 짝지은 차를 밖에서 센다): 줄 · 장면 · 배틀 종류 · 준 피해 · 쓰러뜨림 · 기술 수 · 지는 쪽 승 · 멈춤
                    Report($"M10 결정 143 원자료 {v} {i} {kind} {one.Damage} {one.Knockouts} {one.Moves} {one.AiWins} {one.Stuck}");
                    foreach (var into in new[] { all, kind == BattleKind.UltraBeast || kind == BattleKind.Necrozma ? ub : wild })
                    {
                        into.Battles += one.Battles;
                        into.AiWins += one.AiWins;
                        into.Stuck += one.Stuck;
                        into.Damage += one.Damage;
                        into.Knockouts += one.Knockouts;
                        into.Moves += one.Moves;
                        into.Decisions += one.Decisions;
                        into.ThinkTicks += one.ThinkTicks;
                    }
                }
                long ms10 = all.Decisions > 0 ? all.ThinkTicks * 10000 / Stopwatch.Frequency / all.Decisions : 0;
                Report($"M10 결정 143 [{name}] 전체 {all} / UB · 네크로즈마 {ub} / 야생 {wild} · 결정 {all.Decisions} · 결정당 {ms10 / 10}.{ms10 % 10}ms");
                Assert.AreEqual(0, all.Stuck, name);
            }
        }
    }
}
