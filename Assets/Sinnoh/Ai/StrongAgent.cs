using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Ai
{
    /// <summary>
    /// 강한 AI — 갤럭시단(01 §9-4·§9-6, 결정 26·29, M8). 1단계 = 평평한 PIMC: 표본 K개(<see cref="Determinizer"/>)마다 합법 답 하나하나를 넣고
    /// 배틀 끝까지 원칙 정책으로 굴려(<see cref="Rollout"/>) 점수를 평균하고 가장 좋은 답을 고른다. 점수(정수) = 이김 <see cref="WinScore"/> · 비김 반 · 짐 0 + 상대 파티에 남긴 피해(천분율) × <see cref="DamageWeight"/> / 1000
    /// (결정 29 ① — 지는 쪽(야생 · UB)은 피해 몫이 크고 쓰러뜨린 몫을 더한다, 결정 143 ① <see cref="Underdog"/>). 같은 표본에서는 모든 답이 같은 주사위 수열로 굴린다(답끼리 차이만 남게).
    /// 보기 사본만 받는다(호스트 원본이면 예외). 배틀의 선택(갤럭시단 명단 · 출전 · 다음 포켓몬 · 기술 · 알파 스트라이크 타입 · 응징 · 우선권 토큰 — 갤럭시단 · UB · 네크로즈마 · 야생 배틀, 결정 122)만 찾아보고 그 밖은 원칙 AI 답이다.
    /// 예산(M8 「예산 · 시간」, 01 §13-1 M8-24): <see cref="AgentBudget.MaxSimulations"/> = 굴리기 수 한도(0 이하면 <see cref="MaxSimulations"/>), 시계(<see cref="AgentBudget.Now"/>, ms)가 있으면
    /// 마감 = <see cref="AgentBudget.Deadline"/>과 (시작 + <see cref="ThinkMs"/>) 중 이른 쪽을 **굴리기마다**(트리 반복마다 · 표본 채우기 앞) 보고, 지나면 그때까지의 결과로 답한다(아무것도 못 굴렸으면 원칙 답).
    /// 굴리기 수 한도로 끝난 결정은 결정적(같은 보기 · 같은 씨면 같은 답 — 시험 · 시험대), 시간 마감으로 끝난 결정(<see cref="LastTimedOut"/>)은 결정적이 아니다.
    /// 강한 AI 기본 조립(갤럭시단 세기 「강함」)은 <see cref="Strong"/>.
    /// 깊이 제한(<see cref="Depth"/> &gt; 0, M8 「판세 평가」): 굴리기를 배틀 턴 n번 뒤에서 끊고 판세 평가(<see cref="BattleEval"/>)로 이김 몫을 센다 — 기본 0은 끝까지(1단계 그대로).
    /// 탐색(<see cref="Search"/> = Expect · Tree, M8 「탐색」): 표본마다 트리(<see cref="SearchTree"/>) — 1수 기대값 · MCTS(우연 노드 · 상대 쪽 노드 · 깊이 제한 뒤 평가), 굴리기는 상대 모델 <see cref="Rollout.Rival"/>.
    /// 명단(<see cref="ScreenLineup"/>, M8 「선출 · 순서」): 거르기(원칙 값 빠른 점수) → 깊이(남은 후보에 한도를 모두). 출전 · 다음 포켓몬 · 응징 · 우선권 토큰은 트리 안 결정 노드다.
    /// 인스턴스는 스레드 하나에서 쓴다(판을 돌려 쓴다).
    /// </summary>
    public sealed class StrongAgent : IAgent
    {
        /// <summary>표본 수 상한의 기본값(결정 29 ③의 아래 끝 — 01 §13 M8-2). 결정마다의 상한은 <see cref="MaxSamples"/>.</summary>
        public const int Samples = 16;
        public const int DefaultSimulations = 256;

        /// <summary>
        /// 「강함」 설정 한 벌(01 §13-1 M8-26): 탐색 = Tree · 깊이 <see cref="StrongDepth"/> · 명단 거르기 켬 · 굴리기 한도 <see cref="StrongSimulations"/> · 표본 상한 <see cref="StrongSamples"/> ·
        /// 자기 마감 <see cref="StrongThinkMs"/> ms(러너 마감 = 선택이 열린 시각 + 2초보다 여유, M7-24). 연결 묶음의 조립 지점이 이것을 꽂는다.
        /// </summary>
        public static StrongAgent Strong() => new StrongAgent
        {
            Search = SearchKind.Tree, Depth = StrongDepth, ScreenLineup = true,
            MaxSimulations = StrongSimulations, MaxSamples = StrongSamples, ThinkMs = StrongThinkMs,
        };

        public const int StrongDepth = 8, StrongSimulations = 1024, StrongSamples = 64, StrongThinkMs = 1500;

        /// <summary>
        /// 조립 지점(Client 솔로 · 방 — M8 「연결」)이 꽂는 가상 응답자 AI 한 벌: 원칙 + 「강함」(<see cref="Strong"/>). 어느 쪽을 쓸지는 세기 · 조종으로 세션이 고른다
        /// (<see cref="VirtualAgents"/> — 솔로는 <see cref="SoloGame"/>, 방은 <see cref="HostSession"/>이 세기를 맞춘다). 튜토리얼은 이것을 쓰지 않고 원칙 AI 그대로다.
        /// </summary>
        public static VirtualAgents Virtual() => new VirtualAgents { Principle = new PrincipleAgent(), Strong = Strong() };

        /// <summary>굴리기 수 한도의 기본값(예산의 <see cref="AgentBudget.MaxSimulations"/>가 0 이하일 때).</summary>
        public int MaxSimulations = DefaultSimulations;

        /// <summary>
        /// 표본 수 상한 K(결정 29 ③, 01 §13-1 M8-25): 표본 수 = 한도 ÷ 답 수(탐색 꼴은 ÷ <see cref="TreeEach"/>도)를 1~K로 — 예산이 크고 답이 적은 선택(운명 반응 · 우선권 토큰)일수록 표본이 많고
        /// 답이 많은 선택(기술 · 명단)은 적다. 기본 16(결정 지문 그대로), 「강함」 64.
        /// </summary>
        public int MaxSamples = Samples;

        /// <summary>표본 수 K = 한도 ÷ 답 수(탐색 꼴은 ÷ <see cref="TreeEach"/>도)를 1~max로.</summary>
        public static int SampleCount(int cap, int answers, SearchKind kind, int max)
        {
            int per = Math.Max(1, cap / answers);
            return Math.Max(1, Math.Min(max, kind == SearchKind.Flat ? per : per / TreeEach));
        }

        /// <summary>자기 마감(ms): 결정을 시작한 때 + 이것과 예산 마감 중 이른 쪽에서 멈춘다(시계가 있을 때만). 0 = 예산 마감만.</summary>
        public int ThinkMs;

        /// <summary>마지막 결정이 시간 마감으로 끝났나(굴리기 수 한도를 다 쓰기 전에 멈췄다 — 그 결정은 결정적이 아니다) · 그런 결정 수.</summary>
        public bool LastTimedOut;
        public long TimeOuts;

        Func<long> clock;
        long stopAt;
        readonly Func<bool> stop;

        public StrongAgent()
        {
            stop = Over;
        }

        /// <summary>마감이 지났나(시계가 없으면 늘 false). 지났으면 <see cref="LastTimedOut"/>을 켠다.</summary>
        bool Over()
        {
            if (clock == null || clock() < stopAt) return false;
            LastTimedOut = true;
            return true;
        }
        /// <summary>이긴 굴리기의 점수(비김은 반).</summary>
        public const int WinScore = 1000;
        /// <summary>상대 파티를 바닥냈을 때 더하는 점수(이김 1000에 견주어 1/10 — 01 §13 M8-2). 남긴 피해 비율만큼 받는다.</summary>
        public const int DamageWeight = 100;

        /// <summary>
        /// 야생 · UB 쪽 점수 무게(결정 143 ① [가정 #760~#763]): 찾는 쪽 주인이 갤럭시단이 아닌 가상 쪽(<see cref="SeatId.Wild"/> · <see cref="SeatId.UltraBeast"/> — 야생 · 전설 · 우두머리 · 신 · 시련 · 무리 · UB · 네크로즈마 배틀)이면
        /// 옛 점수(<see cref="OldWeights"/> — 피해 몫 100) 대신 이것: 배틀 끝 피해 몫 · 쓰러뜨린 몫 · 이번 턴 피해 몫(<see cref="ScoreWeights"/>).
        /// 이 쪽은 이김 몫이 답마다 거의 같다(야생은 거의 늘 지고 UB · 네크로즈마는 거의 늘 이긴다) — 그래서 피해 · 쓰러뜨림 · 이번 공격이 기술을 가른다.
        /// 이기려면 상대 파티를 모두 쓰러뜨려야 하므로 세 몫은 이김과 같은 쪽을 본다. 갤럭시단 · 명단 · 좌석 쪽은 옛 점수 그대로(시험대 · 결정 지문). 전후 재기는 이것에 <see cref="OldWeights"/>를 넣는다.
        /// </summary>
        public ScoreWeights Underdog = DefaultUnderdog;
        public static readonly ScoreWeights DefaultUnderdog = new ScoreWeights(1000, 500, 1000);
        /// <summary>옛 점수(결정 29 ① — 피해 몫 <see cref="DamageWeight"/>만).</summary>
        public static readonly ScoreWeights OldWeights = new ScoreWeights(DamageWeight, 0, 0);

        /// <summary>지금 결정의 무게(<see cref="Decide"/>가 쪽을 보고 정한다).</summary>
        ScoreWeights weights = OldWeights;

        readonly GameState root = new GameState();
        readonly GameState sim = new GameState();
        readonly BattleEval eval = new BattleEval();

        /// <summary>
        /// 깊이 제한(배틀 턴 수, 01 §13-1 M8-15): 0 = 배틀 끝까지 굴린다(1단계 그대로). n &gt; 0 = 배틀 턴 n번 뒤 판세 평가(<see cref="BattleEval"/>)로 끊는다 —
        /// 이김 몫 = 평가의 이길 확률(‰), 피해 몫은 끊은 때의 상대 파티 체력으로 같은 셈.
        /// </summary>
        public int Depth;
        /// <summary>평가 무게(null = 평평하면 <see cref="BattleEval.Default"/>, 탐색이면 <see cref="BattleEval.Rival"/>) — 맞추기 시험이 바꿔 끼운다.</summary>
        public int[] Weights;

        /// <summary>
        /// 탐색 꼴(M8 「탐색」, 01 §13-1 M8-17): 기본 <see cref="SearchKind.Flat"/> = 1단계 그대로(결정 지문 시험). <see cref="SearchKind.Expect"/> · <see cref="SearchKind.Tree"/>는
        /// 표본마다 트리(<see cref="SearchTree"/>) — 표본 수 = 굴리기 한도 ÷ (답 수 × <see cref="TreeEach"/>)를 1~<see cref="MaxSamples"/>로, 표본마다 답 하나에 (한도 ÷ 답 수 ÷ 표본 수)번.
        /// </summary>
        public SearchKind Search;
        /// <summary>트리의 탐험 무게(옛 점수 상한 <see cref="OldWeights"/>.Top = 1100 기준 점수 단위 — UCT 평균 + Explore × √N / (1 + n)). 트리에는 지금 무게의 상한에 비례해 넣는다(<see cref="ExploreFor"/> — 갤럭시단 쪽은 그대로).</summary>
        public int Explore = DefaultExplore;
        public const int DefaultExplore = 500;
        /// <summary>탐색이 표본 하나에 답마다 바라는 반복 수(이보다 적으면 표본을 줄인다).</summary>
        public const int TreeEach = 16;
        /// <summary>굴리기 정책(null = 평평하면 <see cref="Rollout.Principle"/>, 탐색이면 상대 모델 <see cref="Rollout.Rival"/>).</summary>
        public RolloutPolicy Policy;

        readonly SearchTree tree = new SearchTree();

        /// <summary>
        /// 명단 거르기 → 깊이(M8 「선출 · 순서」, 01 §13-1 M8-22): 켜면 명단 고르기(간부 C(5,k) · 태홍 C(8,6) = 28가지)에서 먼저 원칙 값 빠른 점수(<see cref="LineupScreen"/>)로
        /// 후보를 <see cref="LineupKeep"/>개로 좁히고, 남은 후보에 굴리기 한도를 모두 준다(표본이 <see cref="LineupSamples"/>개 밑으로 떨어지지 않게). 기본 끔(결정 지문 그대로).
        /// </summary>
        public bool ScreenLineup;
        /// <summary>명단 깊이 단계가 바라는 표본 수(후보 수를 이것에 맞춰 줄인다).</summary>
        public const int LineupSamples = 4;

        /// <summary>마지막 결정이 돌린 표본 수 · 찾아본 답 수(거른 뒤).</summary>
        public int LastSamples, LastAnswers;

        /// <summary>탐색이 만든 노드 수 · 트리와 판이 어긋난 수(0이어야 한다 — 같은 길이면 같은 판).</summary>
        public long Nodes, Mismatch;

        /// <summary>탐색 트리가 만든 결정 노드(선택 종류 · 갈래 · 쪽마다 — <see cref="NodeKey"/>). 시험이 「어떤 선택이 트리 안 노드인가」를 본다.</summary>
        public Dictionary<int, long> NodeKinds => tree.Kinds;

        /// <summary>결정 노드 갈래의 열쇠: 선택 종류 · 갈래 · 찾는 쪽(mine)인가.</summary>
        public static int NodeKey(ChoiceKind kind, ChoiceSub sub, bool mine) => ((int)kind * 64 + (int)sub) * 2 + (mine ? 1 : 0);

        /// <summary>명단 거르기가 남길 후보 수: 한도 ÷ (<see cref="LineupSamples"/> × 표본 하나에 답마다 바라는 반복 — 평평하면 1, 탐색이면 <see cref="TreeEach"/>)를 2~답 수로.</summary>
        public static int LineupKeep(int cap, int answers, SearchKind kind)
        {
            int each = kind == SearchKind.Flat ? 1 : TreeEach;
            return Math.Max(2, Math.Min(answers, cap / (LineupSamples * each)));
        }

        /// <summary>지금까지 돌린 굴리기 수(시험대가 읽는다).</summary>
        public long Simulations;
        /// <summary>끝나기 전에 멈춘 굴리기 수(점수에서 뺀다).</summary>
        public long Stuck;
        /// <summary>깊이 제한에서 끊은 굴리기 수.</summary>
        public long Cuts;

        public AgentDecision Decide(AgentInput input, AgentBudget budget)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.View == null || input.Choice == null || input.Ctx == null) throw new ArgumentException("View·Choice·Ctx가 있어야 한다", nameof(input));
            if (input.View.Viewer == SeatId.HostViewer)
                throw new InvalidOperationException("AI는 보기 사본(ViewFor)만 본다 — 호스트 원본을 받았다(01 §9-1, rulings G15)");
            var view = input.View;
            var ctx = input.Ctx;
            var c = input.Choice;
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(view, ctx, c, legal);
            var principle = PrincipleAgent.Choose(view, ctx, c, legal);
            int my = SideOf(view, c);
            LastSamples = 0;
            LastAnswers = legal.Count;
            LastTimedOut = false;
            if (legal.Count < 2 || my < 0) return principle;
            weights = IsUnderdog(view, c, my) ? Underdog : OldWeights;

            budget = budget ?? new AgentBudget();
            int cap = budget.MaxSimulations > 0 ? budget.MaxSimulations : MaxSimulations > 0 ? MaxSimulations : DefaultSimulations;
            clock = budget.Now;
            if (clock != null)
            {
                stopAt = budget.Deadline;
                long own = clock() + ThinkMs;
                if (ThinkMs > 0 && own < stopAt) stopAt = own;
            }
            var d = Searching(view, ctx, c, legal, principle, my, cap, budget);
            if (LastTimedOut) TimeOuts++;
            clock = null;
            return d;
        }

        AgentDecision Searching(GameState view, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal, AgentDecision principle, int my, int cap, AgentBudget budget)
        {
            if (ScreenLineup && c.Kind == ChoiceKind.Lineup)
            {
                // 거르기(원칙 값 빠른 점수 — 굴리기 없음) → 깊이(남은 후보에 한도를 모두)
                int keep = LineupKeep(cap, legal.Count, Search);
                if (keep < legal.Count)
                {
                    var scores = new long[legal.Count];
                    LineupScreen.Scores(view, ctx, c, legal, scores);
                    var kept = new List<int>(keep);
                    LineupScreen.Top(scores, legal.Count, keep, kept);
                    var narrowed = new List<ChoiceAnswer>(keep);
                    foreach (int i in kept) narrowed.Add(legal[i]);
                    legal = narrowed;
                    LastAnswers = legal.Count;
                }
            }
            var sum = new long[legal.Count];
            var count = new int[legal.Count];
            var wins = new long[legal.Count];
            // 자막(M8 「연결」): 배틀 안의 선택만 — 명단은 배틀 화면이 뜨기 전이라 그릴 곳이 없다
            bool told = c.Kind != ChoiceKind.Lineup;
            if (Search != SearchKind.Flat)
            {
                Searched(view, ctx, c, legal, my, cap, budget, sum, count, wins);
                return Pick(legal, principle, sum, count, wins, told);
            }
            int samples = SampleCount(cap, legal.Count, SearchKind.Flat, MaxSamples);
            var part = new long[legal.Count];
            var partN = new int[legal.Count];
            var partW = new long[legal.Count];
            using (Rig.Use(null)) // 표본·굴리기는 튜토리얼 판 손보기와 무관하다
            {
                for (int k = 0; k < samples && !Over(); k++)
                {
                    LastSamples++;
                    Determinizer.FillInto(root, view, ctx, SampledRng.ForSample(budget.Seed, k));
                    Array.Clear(part, 0, part.Length);
                    Array.Clear(partN, 0, partN.Length);
                    Array.Clear(partW, 0, partW.Length);
                    bool cut = false;
                    for (int i = 0; i < legal.Count; i++)
                    {
                        if (Over())
                        {
                            cut = true;
                            break;
                        }
                        sim.CopyFrom(root);
                        var r = Rollout.ToEnd(sim, ctx, Rollout.Principle, c.Id, legal[i], Rollout.MaxSteps, false, Depth);
                        Simulations++;
                        int win;
                        if (r.Cut)
                        {
                            Cuts++;
                            win = eval.WinPermille(sim, ctx, my, Weights);
                        }
                        else if (!r.Ended)
                        {
                            Stuck++;
                            continue;
                        }
                        else win = WinOf(r, my);
                        part[i] += Score(win, r, my, weights);
                        partN[i]++;
                        partW[i] += win;
                    }
                    // 마감에 끊긴 표본은 버린다(답마다 같은 표본 셋으로 견준다) — 첫 표본이면 그것이라도 쓴다
                    if (cut && k > 0)
                    {
                        LastSamples--;
                        break;
                    }
                    for (int i = 0; i < legal.Count; i++)
                    {
                        sum[i] += part[i];
                        count[i] += partN[i];
                        wins[i] += partW[i];
                    }
                    if (cut) break;
                }
            }

            return Pick(legal, principle, sum, count, wins, told);
        }

        /// <summary>탐색(Expect · Tree): 표본마다 채운 판에서 트리 하나(<see cref="SearchTree.Run"/>) — 트리 안 굴림은 트리가 정해 넣고 밖은 표본의 AI 난수로 굴린다.</summary>
        void Searched(GameState view, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal, int my, int cap, AgentBudget budget, long[] sum, int[] count, long[] wins)
        {
            int per = Math.Max(1, cap / legal.Count);
            int samples = SampleCount(cap, legal.Count, Search, MaxSamples);
            int each = Math.Max(1, per / samples);
            tree.Stop = stop;
            tree.Kind = Search;
            tree.My = my;
            tree.Explore = ExploreFor(Explore, weights);
            tree.Policy = Policy ?? Rollout.Rival;
            tree.Depth = Depth;
            tree.Weights = Weights ?? BattleEval.Rival;
            tree.Score = weights;
            tree.Simulations = tree.Stuck = tree.Cuts = tree.Mismatch = tree.Nodes = 0;
            tree.Wins = wins;
            using (Rig.Use(tree))
                for (int k = 0; k < samples && !Over(); k++)
                {
                    LastSamples++;
                    var rng = SampledRng.ForSample(budget.Seed, k);
                    Determinizer.FillInto(root, view, ctx, rng);
                    if (!tree.Run(root, sim, ctx, c, legal, each, rng, sum, count)) break; // 마감 — 끊긴 트리도 쓴다(뿌리 답은 고루 돌아 답마다 한 번 차이뿐)
                }
            tree.Wins = null;
            Simulations += tree.Simulations;
            Stuck += tree.Stuck;
            Cuts += tree.Cuts;
            Mismatch += tree.Mismatch;
            Nodes += tree.Nodes;
        }

        /// <summary>
        /// 평균이 가장 큰 답(평균은 나누지 않고 엇곱해 견준다 — 정수). 비기면 원칙 답, 그다음 Legal 순서.
        /// 자막(told — M8 「연결」, 01 §13-1 M8-33): 「수읽기」(<see cref="AiRule.Lookahead"/>) — Power = 고른 답의 승산 %(이김 몫 평균), Bonus = 원칙 답보다 높은 승산 %p(낮거나 같으면 0).
        /// told가 아니면(명단) 원칙 답의 사유 · 사유 없음. 답은 자막과 무관하다(결정 지문 그대로).
        /// </summary>
        static AgentDecision Pick(List<ChoiceAnswer> legal, AgentDecision principle, long[] sum, int[] count, long[] wins, bool told)
        {
            int best = -1, same = -1;
            for (int i = 0; i < legal.Count; i++)
            {
                if (count[i] == 0) continue;
                if (legal[i].SameAs(principle.Answer)) same = i;
                if (best < 0 || sum[i] * count[best] > sum[best] * count[i]) best = i;
            }
            if (best < 0) return principle;
            // 비기면 원칙 답, 그다음 Legal 순서
            if (same >= 0 && sum[same] * count[best] >= sum[best] * count[same]) best = same;
            if (!told) return best == same ? principle : new AgentDecision(legal[best].Clone(), new AiReason(AiRule.None, 0, 0));
            int odds = Odds(wins[best], count[best]);
            int over = same >= 0 && same != best ? Math.Max(0, odds - Odds(wins[same], count[same])) : 0;
            return new AgentDecision((best == same ? principle.Answer : legal[best]).Clone(), new AiReason(AiRule.Lookahead, odds, over));
        }

        /// <summary>이김 몫 합(‰) ÷ 굴린 수 → 승산 %(반올림, 0~100).</summary>
        static int Odds(long wins, int count) => count <= 0 ? 0 : (int)Math.Max(0, Math.Min(100, (wins + 5L * count) / (10L * count)));

        /// <summary>
        /// 이 선택에서 찾아볼 쪽(배틀의 Side). 배틀 밖의 선택이면 −1 — 명단 고르기는 배틀 전이라 갤럭시단 쪽(1, BattleFlow.ApplyLineup).
        /// 갤럭시단 배틀뿐 아니라 UB · 네크로즈마 · 야생(전설 · 우두머리 · 신 · 시련 · 무리) 배틀도 찾아본다(결정 122 — 어느 AI를 쓸지는 <see cref="VirtualAgents"/>가 고른다).
        /// </summary>
        static int SideOf(GameState view, PendingChoice c)
        {
            if (c.Kind == ChoiceKind.Lineup) return 1;
            return view.Battle == null ? -1 : ChoiceRules.BattleSideOf(view, c.Seat);
        }

        /// <summary>찾는 쪽이 <see cref="Underdog"/> 무게를 쓰나: 배틀 안(명단 아님)이고 그 쪽 주인이 갤럭시단이 아닌 가상 쪽(야생 · UB).</summary>
        static bool IsUnderdog(GameState view, PendingChoice c, int my)
        {
            if (c.Kind == ChoiceKind.Lineup || view.Battle == null) return false;
            int owner = view.Battle.Sides[my].Owner;
            return owner == SeatId.Wild || owner == SeatId.UltraBeast;
        }

        /// <summary>
        /// 트리의 탐험 무게를 점수 상한에 비례하게(결정 143 ① [가정 #767]): explore × w.Top ÷ <see cref="OldWeights"/>.Top — 야생 · UB 쪽은 상한이 1100 → 3500이라 탐험 비중이 1/3로 줄지 않게.
        /// 옛 점수(갤럭시단 · 명단 · 좌석 쪽)는 explore 그대로(결정 · 시험대 지문 그대로).
        /// </summary>
        public static int ExploreFor(int explore, ScoreWeights w) => (int)((long)explore * w.Top / OldWeights.Top);

        internal static int Score(RolloutResult r, int my) => Score(WinOf(r, my), r, my, OldWeights);

        /// <summary>끝까지 굴린 결과의 이김 몫: 이김 1000 · 비김 500 · 짐 0.</summary>
        public static int WinOf(RolloutResult r, int my) => r.Winner == my ? WinScore : r.Winner < 0 ? WinScore / 2 : 0;

        /// <summary>
        /// 점수 = 이김 몫(끝까지 = 이김 1000 · 비김 500 · 짐 0, 끊은 굴리기 = 평가 ‰) + 상대 좌석 파티에 남긴 피해 몫(Damage × 잃은 체력 ‰ / 1000)
        /// + 쓰러뜨린 몫(Knockout × 쓰러진 비율 ‰ / 1000) + 이번 턴 피해 몫(Turn × 이번 배틀 턴이 끝난 때 잃어 있는 체력 ‰ / 1000 — 답마다 시작 체력이 같아 이번 공격의 피해 차만 남는다).
        /// 갤럭시단 쪽 = <see cref="OldWeights"/>(옛 점수 그대로), 야생 · UB 쪽 = <see cref="Underdog"/>(결정 143 ①).
        /// </summary>
        public static int Score(int win, RolloutResult r, int my, ScoreWeights w)
        {
            int foe = 1 - my;
            int score = win + w.Damage * (1000 - r.HpPermille(foe)) / 1000;
            if (w.Knockout != 0) score += w.Knockout * r.FaintedPermille(foe) / 1000;
            if (w.Turn != 0) score += w.Turn * (1000 - r.TurnHpPermille(foe)) / 1000;
            return score;
        }
    }

    /// <summary>강한 AI 점수의 피해 몫 무게 셋(결정 143 ① — <see cref="StrongAgent.Score(int, RolloutResult, int, ScoreWeights)"/>). 값 1000 = 이김 몫과 같은 크기.</summary>
    public struct ScoreWeights
    {
        /// <summary>배틀 끝(또는 깊이 제한에서 끊은 때) 상대 좌석 파티가 잃은 체력 몫 · 쓰러진 비율 몫 · 이번 배틀 턴이 끝난 때 잃은 체력 몫.</summary>
        public int Damage, Knockout, Turn;

        public ScoreWeights(int damage, int knockout, int turn)
        {
            Damage = damage;
            Knockout = knockout;
            Turn = turn;
        }

        /// <summary>점수 상한(이김 + 세 몫) — 탐색 트리의 상대 쪽 노드가 뒤집어 본다.</summary>
        public int Top => StrongAgent.WinScore + Damage + Knockout + Turn;
    }
}
