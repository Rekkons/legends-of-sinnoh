using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M8 「원칙 AI 점검」(01 §9-6 · 결정 26 · 30 ③ · rulings A16): 가상 응답자 AI 고르기(<see cref="VirtualAgents"/>) — 갤럭시단만 세기 · 조종을 보고,
    /// UB · 네크로즈마 · 야생 전설 · 우두머리는 강한 AI를 꽂아도 p.25 원칙 답이다. 사람 조종 갤럭시단의 무응답 → 강한 AI는 RoomRunnerTests의 M8_원칙_….
    /// </summary>
    public partial class StrongAiTests
    {
        static readonly int KindCount = Enum.GetValues(typeof(BattleKind)).Length;

        /// <summary>
        /// 가상 응답자 물음을 지켜보는 AI: 안쪽(<see cref="VirtualAgents"/>)에 넘기고 물음마다 자리(배틀 종류 — 명단은 갤럭시단, 배틀 밖은 <see cref="KindCount"/>칸) ·
        /// 강한 AI가 굴렸나 · 원칙 답과 같나를 센다.
        /// </summary>
        sealed class VirtualWatch : IAgent
        {
            readonly VirtualAgents inner;
            readonly StrongAgent strong;
            readonly List<ChoiceAnswer> legal = new List<ChoiceAnswer>();
            public readonly int[] Asked = new int[KindCount + 1], Searched = new int[KindCount + 1], Same = new int[KindCount + 1];
            /// <summary>고른 알파 스트라이크 타입(서로 다른 것).</summary>
            public readonly HashSet<int> AlphaTypes = new HashSet<int>();

            public VirtualWatch(VirtualAgents inner, StrongAgent strong)
            {
                this.inner = inner;
                this.strong = strong;
            }

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                var view = input.View;
                var c = input.Choice;
                int at = view.Battle != null ? (int)view.Battle.Kind : c.Kind == ChoiceKind.Lineup ? (int)BattleKind.Galactic : KindCount;
                long before = strong.Simulations;
                var d = inner.Decide(input, budget);
                legal.Clear();
                Engine.Legal(view, input.Ctx, c, legal);
                Asked[at]++;
                if (strong.Simulations > before) Searched[at]++;
                if (d.Answer.SameAs(PrincipleAgent.Choose(view, input.Ctx, c, legal).Answer)) Same[at]++;
                if (d.Answer.Kind == AnswerKind.UseMove && d.Answer.Target == BattleRules.AlphaStrike && d.Answer.Values.Count > 0) AlphaTypes.Add(d.Answer.Values[0]);
                return d;
            }
        }

        [Test]
        public void M8_원칙_가상응답자고르기는갤럭시단만세기와조종을본다()
        {
            // 표: 갤럭시단 = 사람 조종(무응답 대신, 결정 30 ③)이거나 세기 「강함」이면 강한 AI · UB와 야생 = NonPlayerMovePolicy(기본 원칙). 강한 AI를 안 꽂았으면 늘 원칙
            var principle = new PrincipleAgent();
            var strong = new PrincipleAgent(); // 표지(어느 AI를 골랐나만 본다)
            int n = 0;
            foreach (int seat in new[] { SeatId.Galactic, SeatId.UltraBeast, SeatId.Wild })
                foreach (GalacticAiLevel level in Enum.GetValues(typeof(GalacticAiLevel)))
                    foreach (bool human in new[] { false, true })
                        foreach (NonPlayerMovePolicy policy in Enum.GetValues(typeof(NonPlayerMovePolicy)))
                        {
                            bool want = seat == SeatId.Galactic ? human || level == GalacticAiLevel.Strong : policy == NonPlayerMovePolicy.Strong;
                            string at = $"좌석 {seat} · 세기 {level} · 사람 조종 {human} · 비플레이어 {policy}";
                            Assert.AreEqual(want, VirtualAgents.WantsStrong(seat, level, human, policy), at);
                            var va = new VirtualAgents { Principle = principle, Strong = strong, GalacticLevel = level, GalacticHuman = human };
                            Assert.AreSame(want ? strong : principle, va.For(seat, policy), at);
                            va.Strong = null;
                            Assert.AreSame(principle, va.For(seat, policy), at + " · 강한 AI 없음");
                            n++;
                        }
            Assert.AreEqual(24, n);
            var basic = new VirtualAgents { Principle = principle, Strong = strong };
            Assert.AreSame(principle, basic.For(SeatId.Galactic, NonPlayerMovePolicy.Principle), "기본(AI 조종 · 원칙 세기) = 지금 솔로 · 방");
            Assert.AreEqual(NonPlayerMovePolicy.Strong, new RuleOptions().NonPlayerMovePolicy, "UB · 야생 기본 = 강한 AI(결정 122)");
            Assert.AreEqual(NonPlayerMovePolicy.Principle, new SoloSettings().ToOptions().NonPlayerMovePolicy, "솔로 설정 형의 기본(시험 · 확인용 판) = 원칙");
            Assert.AreEqual(NonPlayerMovePolicy.Strong, new SoloSettings { NonPlayer = SoloGame.NewGameNonPlayer }.ToOptions().NonPlayerMovePolicy, "사람이 여는 새 솔로 판 = 강한 AI");
            Assert.AreEqual(NonPlayerMovePolicy.Strong, new RoomSettings().ToOptions().NonPlayerMovePolicy, "방 = 강한 AI");
        }

        [Test]
        public void M8_원칙_강한AI를꽂아도UB네크로즈마야생전설우두머리는원칙답()
        {
            // 봇 판(신오의 심장 · 울트라 버스트)의 가상 응답자에 VirtualAgents(원칙 + 강한 AI — 세기 「강함」)를 꽂고 규칙 옵션은 원작 원칙(NonPlayerMovePolicy.Principle — 결정 122의 옵션):
            // 갤럭시단 배틀 · 명단 = 강한 AI가 굴린다, 그 밖(UB · 네크로즈마 · 전설 · 우두머리 · 신 · 시련 · 무리 · 배틀 밖) = 굴리기 0 · 답이 원칙 답과 같다.
            // 보통 야생 배틀은 가상 응답자 물음이 없다(기술 하나 — 합법 답이 하나면 엔진이 바로 둔다, BattleFlow.SelectAction)
            var strong = new StrongAgent { MaxSimulations = 8 };
            var watch = new VirtualWatch(new VirtualAgents { Principle = new PrincipleAgent(), Strong = strong, GalacticLevel = GalacticAiLevel.Strong }, strong);
            foreach (var (campaign, seats, seed) in PrincipleGames)
            {
                var g = Playout.Run(ctx, seats, seed, null, null, campaign, 0, new RuleOptions { NonPlayerMovePolicy = NonPlayerMovePolicy.Principle }, false, watch);
                Assert.IsTrue(g.Ended, $"{campaign} {seats}인 시드 {seed}: 게임 끝");            }
            var parts = new List<string>();
            for (int k = 0; k <= KindCount; k++)
            {
                if (watch.Asked[k] == 0) continue;
                string name = k == KindCount ? "배틀 밖" : ((BattleKind)k).ToString();
                parts.Add($"{name} {watch.Asked[k]}(굴림 {watch.Searched[k]} · 원칙 답 {watch.Same[k]})");
                if (k == (int)BattleKind.Galactic) continue;
                Assert.AreEqual(0, watch.Searched[k], $"{name}: 강한 AI가 굴리지 않는다");
                Assert.AreEqual(watch.Asked[k], watch.Same[k], $"{name}: 원칙 답");
            }
            Report($"M8 원칙 AI 점검(봇 판 {PrincipleGames.Length}): {string.Join(" · ", parts)}");
            foreach (var k in new[] { BattleKind.UltraBeast, BattleKind.Necrozma, BattleKind.Legendary, BattleKind.Alpha })
                Assert.Greater(watch.Asked[(int)k], 0, $"{k} 배틀의 가상 응답자 물음이 나왔다");
            Assert.Greater(watch.Searched[(int)BattleKind.Galactic], 0, "갤럭시단 배틀은 강한 AI가 굴렸다");
        }

        [Test]
        public void M9_결정122_UB네크로즈마야생은기본이강한AI_알파스트라이크타입을고르고원칙과다른수도둔다()
        {
            // 결정 122(T26 · T31): 사람이 조종하지 않는 쪽 전부의 기본 = 강한 AI(RuleOptions 기본값). 같은 봇 판을 규칙 옵션 기본값(new RuleOptions — 봇 판의 null 기본은 원칙)으로 돌린다 — 갤럭시단은 세기 「원칙」이라 굴리지 않고,
            // UB · 네크로즈마 · 전설 · 우두머리 배틀의 가상 응답자 물음은 강한 AI가 굴린다. 알파 스트라이크 답에는 타입이 실리고, 원칙 답과 다른 수(기술 바꿔 쓰기)가 나온다.
            var strong = new StrongAgent { Search = SearchKind.Tree, Depth = StrongAgent.StrongDepth, MaxSimulations = 32 };
            var watch = new VirtualWatch(new VirtualAgents { Principle = new PrincipleAgent(), Strong = strong, GalacticLevel = GalacticAiLevel.Principle }, strong);
            foreach (var (campaign, seats, seed) in PrincipleGames)
                Assert.IsTrue(Playout.Run(ctx, seats, seed, null, null, campaign, 0, new RuleOptions(), false, watch).Ended, $"{campaign} {seats}인 시드 {seed}: 게임 끝");
            var parts = new List<string>();
            int asked = 0, same = 0;
            for (int k = 0; k <= KindCount; k++)
            {
                if (watch.Asked[k] == 0) continue;
                parts.Add($"{(k == KindCount ? "배틀 밖" : ((BattleKind)k).ToString())} {watch.Asked[k]}(굴림 {watch.Searched[k]} · 원칙 답 {watch.Same[k]})");
                if (k == (int)BattleKind.Galactic || k == KindCount) continue;
                asked += watch.Asked[k];
                same += watch.Same[k];
            }
            Report($"M9 결정 122 강한 AI(봇 판 {PrincipleGames.Length}): {string.Join(" · ", parts)} · 알파 스트라이크 타입 {watch.AlphaTypes.Count}가지");
            Assert.AreEqual(0, watch.Searched[(int)BattleKind.Galactic], "갤럭시단은 세기 「원칙」 그대로");
            foreach (var k in new[] { BattleKind.UltraBeast, BattleKind.Legendary, BattleKind.Alpha })
                Assert.Greater(watch.Searched[(int)k], 0, $"{k} 배틀은 강한 AI가 굴렸다");
            Assert.Less(same, asked, "원칙 답과 다른 수가 나온다(같은 수만 되풀이하지 않는다)");
            Assert.Greater(watch.AlphaTypes.Count, 1, "알파 스트라이크 타입을 상대에 맞춰 고른다(한 타입만이 아니다)");
        }

        /// <summary>시계를 넣어 묻는 AI(게임의 러너처럼 — 마감 = 지금 + AiSettings.DeadlineMs) · 가상 응답자 배틀 물음마다 걸린 시간의 최댓값 · 합 · 수.</summary>
        sealed class TimedAgent : IAgent
        {
            readonly IAgent inner;
            readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            /// <summary>러너 마감(AiSettings.DeadlineMs 기본 2초).</summary>
            public static readonly int Deadline = new AiSettings().DeadlineMs;
            public long MaxMs, SumMs;
            public int Count;

            public TimedAgent(IAgent inner) => this.inner = inner;

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                long t0 = watch.ElapsedMilliseconds;
                var d = inner.Decide(input, new AgentBudget { Now = () => watch.ElapsedMilliseconds, Deadline = t0 + Deadline, Seed = (ulong)input.Choice.Id });
                long ms = watch.ElapsedMilliseconds - t0;
                if (input.View.Battle != null && input.View.Battle.Kind != BattleKind.Galactic)
                {
                    MaxMs = Math.Max(MaxMs, ms);
                    SumMs += ms;
                    Count++;
                }
                return d;
            }
        }

        [Test, Explicit("결정 122 한 번 재기 — 강한 AI 전환 전후 포획률 · 결정 시간(수 분)")]
        public void M9_결정122_강한AI전환전후_포획률과결정시간을잰다()
        {
            // 신오의 심장 2인 봇 판 시드 1~6을 원칙(옵션) · 강한 AI(기본 — 「강함」 한 벌, 시계 마감 2초)로 두 번 돌려 배틀 종류마다 포획 시도 · 성공을 견준다
            var lines = new List<string>();
            foreach (var policy in new[] { NonPlayerMovePolicy.Principle, NonPlayerMovePolicy.Strong })
            {
                var timed = new TimedAgent(new VirtualAgents { Principle = new PrincipleAgent(), Strong = StrongAgent.Strong() });
                var tries = new int[KindCount];
                var hits = new int[KindCount];
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var g = Playout.Run(ctx, 2, seed, null, null, Campaign.HeartOfSinnoh, 0, new RuleOptions { NonPlayerMovePolicy = policy }, false, timed);
                    for (int k = 0; k < KindCount; k++) { tries[k] += g.CaptureTries[k]; hits[k] += g.CapturesOf[k]; }
                }
                var parts = new List<string>();
                for (int k = 0; k < KindCount; k++)
                    if (tries[k] > 0) parts.Add($"{(BattleKind)k} {hits[k]}/{tries[k]}");
                lines.Add($"{policy}: 포획 {string.Join(" · ", parts)} · 가상 응답자 배틀 결정 {timed.Count}번 최대 {timed.MaxMs}ms 평균 {(timed.Count > 0 ? timed.SumMs / timed.Count : 0)}ms");
                Assert.LessOrEqual(timed.MaxMs, TimedAgent.Deadline + 200, "결정 시간 2초 안");
            }
            Report("M9 결정 122 포획률 · 결정 시간: " + string.Join(" / ", lines));
        }

        /// <summary>원칙 AI 점검의 봇 판: UB · 네크로즈마(울트라 버스트) · 전설 · 우두머리(신오의 심장)가 모두 나오는 판.</summary>
        static readonly (Campaign, int, ulong)[] PrincipleGames =
        {
            (Campaign.HeartOfSinnoh, 2, 1),
            (Campaign.UltraBurst, 2, 0),
        };
    }
}
