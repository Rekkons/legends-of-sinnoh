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
            // 원칙 답 문턱(결정 197)은 끈다: 갤럭시단 결정이 바뀌면 봇 판의 흐름이 바뀌어 전설 배틀이 안 나온다 — 이 시험은 「누가 굴리나 · 원칙 답인가」만 본다
            var strong = new StrongAgent { MaxSimulations = 8, Margin = 0 };
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

        /// <summary>결정 197 장면의 좌석 포켓몬 한 마리: 카드 키 · 잃은 체력 · 배운 기술 둘(null = 빈 칸).</summary>
        struct RoomMon
        {
            public string Key, Move0, Move1;
            public int Damage;
            public RoomMon(string key, int damage, string m0, string m1) { Key = key; Damage = damage; Move0 = m0; Move1 = m1; }
        }

        // 실제 방 판(11-21)의 좌석 파티 — 장면마다 앞 = 나와 있는 포켓몬
        static readonly RoomMon Skuntank = new RoomMon("435:0", 0, "headbutt", "neurotoxin"), Snorlax = new RoomMon("143:0", 0, "stinging_punch", null), Lickitung = new RoomMon("108:0", 0, "ice_fang", null),
            Mamoswine = new RoomMon("464:0", 0, "rampart_riposte", "infatuate");

        /// <summary>
        /// 결정 197 장면(실제 방 판 11-21의 같은 꼴 — 손으로): 혼자 판 좌석 0의 파티(앞 = 나와 있는 포켓몬) 대 UB 하나의 UB 배틀에서 UB 쪽 기술 고르기가 열린 호스트 원본.
        /// 좌석의 첫 행동은 기본 답으로 넘기고, UB 쪽 물음이 열리면 체력 · 상태를 그 장면 값으로 맞춘다.
        /// </summary>
        GameState RoomScene(string ub, int ubDamage, StatusEffect ubStatus, params RoomMon[] party)
        {
            var s = Battles.Game(ctx, 1);
            s.Seats[0].OpenSlots = s.Seats[0].Party.Length;
            var ids = new List<int>();
            foreach (var m in party)
            {
                int id = Scene.Give(s, ctx, 0, m.Key);
                var p = s.Pokemon[id];
                p.Moves[0] = m.Move0 != null ? ctx.Cards.MoveIds.IndexOf(m.Move0) : -1;
                p.Moves[1] = m.Move1 != null ? ctx.Cards.MoveIds.IndexOf(m.Move1) : -1;
                Assert.IsFalse(m.Move0 != null && p.Moves[0] < 0 || m.Move1 != null && p.Moves[1] < 0, m.Key + " 기술");
                ids.Add(id);
            }
            int u = Scene.Spawn(s, ctx, ub, SeatId.UltraBeast);
            s.Pokemon[u].HasLegendaryMove = true;   // UB는 전설 특수기술(알파 스트라이크 · 비스트 부스트)을 가진다 — 맵의 UB 토큰에서 나온 것처럼
            Battles.Start(s, ctx, null, BattleKind.UltraBeast, 0, SeatId.UltraBeast, -1, u);
            var seen = new List<string>();
            for (int guard = 0; guard < 40; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "열린 선택 — 지난 선택: " + string.Join(", ", seen));
                seen.Add($"{c.Seat}:{c.Kind}/{c.Sub}");
                if (c.Seat == SeatId.UltraBeast && c.Kind == ChoiceKind.BattleAction)
                {
                    for (int i = 0; i < party.Length; i++)
                    {
                        s.Pokemon[ids[i]].Damage = party[i].Damage;
                        s.Pokemon[ids[i]].Status = StatusEffect.None;
                    }
                    s.Pokemon[u].Damage = ubDamage;
                    s.Pokemon[u].Status = ubStatus;
                    Assert.AreEqual(ids[0], s.Battle.Sides[0].Active, "좌석 쪽에 나와 있는 포켓몬");
                    return s;
                }
                var pick = c.Default.Clone();
                if ((c.Kind == ChoiceKind.SendIn || c.Kind == ChoiceKind.NextPokemon) && c.Seat == 0)
                {
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(s, ctx, c, legal);
                    pick = legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == ids[0]) ?? pick;
                }
                Battles.Answer(s, ctx, c, pick, null);
            }
            Assert.Fail("UB 쪽 기술 고르기가 열리지 않았다: " + string.Join(", ", seen));
            return null;
        }

        /// <summary>그 장면의 UB 쪽 물음에 「강함」 한 벌(시계 없음 — 굴리기 한도로 끝나 결정적)을 씨 seed로 물은 답.</summary>
        AgentDecision StrongOn(GameState s, StrongAgent agent, ulong seed)
        {
            var c = s.Pending.Find(x => !x.Answered && x.Seat == SeatId.UltraBeast);
            var view = Engine.ViewFor(s, c.Seat);
            return agent.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget { Seed = seed });
        }

        static string AnswerName(Ctx cx, ChoiceAnswer a) =>
            a.Target == BattleRules.AlphaStrike && a.Values.Count > 0 ? "알파 " + (PokemonType)a.Values[0] : a.Target >= 0 ? cx.Cards.Moves[a.Target].Key : a.ToString();

        /// <summary>결정 197 장면 넷: (이름, 장면, 원칙 답과 같아야 하나 — null = 같은 답이기만).</summary>
        IEnumerable<(string name, GameState scene, bool? principle)> RoomScenes()
        {
            // 664: 철화구야(강철/비행, 독) 대 스컹탱크(독/악, 체력 가득) — 이륙(고유 기술) · 알파 강철/비행/풀/불꽃
            yield return ("664 철화구야 대 스컹탱크", RoomScene("797:0", 0, StatusEffect.Poisoned, Skuntank, new RoomMon("395:1", 9, "hidden_power", "powder_snow"), Mamoswine, Snorlax,
                new RoomMon("389:1", 10, "spore", "bite"), Lickitung), true);
            // 516 · 526 · 533: 페로코체(벌레/격투) 대 토대부기 · 엠페르트 — 매혹(고유 기술) · 알파 벌레/격투/비행/얼음
            yield return ("516 페로코체 대 토대부기", RoomScene("795:0", 0, StatusEffect.None, new RoomMon("389:1", 0, "spore", "bite"), new RoomMon("395:1", 0, "hidden_power", null),
                new RoomMon("464:0", 0, "rampart_riposte", null), new RoomMon("143:0", 0, null, null), Skuntank, Lickitung), null);
            yield return ("526 페로코체(졸림) 대 엠페르트", RoomScene("795:0", 0, StatusEffect.Drowsy, new RoomMon("395:1", 0, "hidden_power", null), new RoomMon("464:0", 0, "rampart_riposte", null),
                new RoomMon("143:0", 0, null, null), new RoomMon("389:1", 10, "spore", "bite"), Skuntank, Lickitung), null);
            yield return ("533 페로코체(졸림) 대 엠페르트(체력 2)", RoomScene("795:0", 8, StatusEffect.Drowsy, new RoomMon("395:1", 7, "hidden_power", null), new RoomMon("464:0", 0, "rampart_riposte", null),
                new RoomMon("143:0", 0, null, null), new RoomMon("389:1", 10, "spore", "bite"), Skuntank, Lickitung), null);
        }

        /// <summary>결정 197 장면마다 물어 볼 씨.</summary>
        const int RoomSeeds = 4;

        [Test]
        public void M10_결정197_실제방판장면은씨가달라도같은답이고철화구야는이륙()
        {
            // 실제 방 판(11-21): 강한 AI가 원칙 답(고유 기술)에서 굴리기 잡음보다 작은 차이로 벗어나 씨마다 답이 바뀌었다(664 — 이륙 · 알파 풀 · 이륙).
            // 짝지은 차이(공통 난수)가 표준오차 × StrongAgent.Margin을 넘을 때만 벗어나면, 같은 장면은 씨가 달라도 같은 답 — 664는 원칙 답(이륙)
            var lines = new List<string>();
            foreach (var (name, scene, principle) in RoomScenes())
            {
                var c = scene.Pending.Find(x => !x.Answered && x.Seat == SeatId.UltraBeast);
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(scene, ctx, c, legal);
                var p = PrincipleAgent.Choose(Engine.ViewFor(scene, c.Seat), ctx, c, legal).Answer;
                if (principle == true) Assert.AreEqual(ctx.Cards.MoveIds.IndexOf("lift_off"), p.Target, $"{name}: 원칙 답 = 고유 기술 이륙");
                var answers = new List<string>();
                var before = new List<string>();
                ChoiceAnswer first = null;
                for (ulong seed = 1; seed <= RoomSeeds; seed++)
                {
                    var old = StrongAgent.Strong();
                    old.Margin = 0;
                    old.CommonDice = false;   // 결정 197 전 셈(보고 줄만)
                    var o = StrongOn(scene, old, seed);
                    before.Add($"{AnswerName(ctx, o.Answer)}({o.Reason?.Power}% +{o.Reason?.Bonus})");
                    var flatOld = StrongOn(scene, new StrongAgent { MaxSimulations = 4000, Margin = 0 }, seed);
                    var flatNew = StrongOn(scene, new StrongAgent { MaxSimulations = 4000 }, seed);
                    before.Add($"[평평 4000 전 {AnswerName(ctx, flatOld.Answer)} +{flatOld.Reason?.Bonus} 후 {AnswerName(ctx, flatNew.Answer)}]");
                    // 664: 진단(평평 · 굴리기 4000)에서도 씨마다 원칙 답(이륙) — 옛 셈은 씨에 따라 알파 강철
                    if (principle == true) Assert.IsTrue(flatNew.Answer.SameAs(p), $"{name}: 평평 4000 씨 {seed}의 답 {AnswerName(ctx, flatNew.Answer)} — 원칙 답이어야 한다");
                    var d = StrongOn(scene, StrongAgent.Strong(), seed);
                    answers.Add($"{AnswerName(ctx, d.Answer)}({d.Reason?.Power}% +{d.Reason?.Bonus})");
                    if (first == null) first = d.Answer;
                    Assert.IsTrue(d.Answer.SameAs(first), $"{name}: 씨 {seed}의 답 {AnswerName(ctx, d.Answer)} ≠ 씨 1의 답 {AnswerName(ctx, first)}");
                }
                if (principle == true) Assert.IsTrue(first.SameAs(p), $"{name}: 원칙 답 {AnswerName(ctx, p)}이어야 한다 — {AnswerName(ctx, first)}");
                lines.Add($"{name}: 원칙 {AnswerName(ctx, p)} · 강함 전 {string.Join(" · ", before)} → 후 {string.Join(" · ", answers)}");
            }
            Report("M10 결정 197 실제 방 판 장면: " + string.Join(" / ", lines));
        }

        /// <summary>원칙 AI 점검의 봇 판: UB · 네크로즈마(울트라 버스트) · 전설 · 우두머리(신오의 심장)가 모두 나오는 판.</summary>
        static readonly (Campaign, int, ulong)[] PrincipleGames =
        {
            (Campaign.HeartOfSinnoh, 2, 1),
            (Campaign.UltraBurst, 2, 0),
        };
    }
}
