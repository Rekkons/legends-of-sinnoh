using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>강한 AI의 탐색 꼴(<see cref="StrongAgent.Search"/>, M8 「탐색」).</summary>
    public enum SearchKind
    {
        /// <summary>1단계 평평한 PIMC(밑작업 — 결정 지문 그대로): 표본마다 합법 답 하나하나를 굴리기 정책으로 굴려 평균.</summary>
        Flat,
        /// <summary>1수 기대값: 표본마다 뿌리 답 뒤 첫 굴림(들)의 §6-3 묶음을 무게대로 고루 나눠 정해 넣고(<see cref="ForcedDice"/>) 굴린다 — 첫 결정 지점에서 트리를 멈춘다.</summary>
        Expect,
        /// <summary>표본마다 MCTS: 결정 노드(UCT — 상대 쪽 노드는 상대에게 좋은 수 · 최대·최소) · 우연 노드(굴림의 §6-3 묶음, 무게대로 고루) · 깊이 제한 뒤 판세 평가.</summary>
        Tree,
    }

    /// <summary>
    /// 표본 하나의 탐색 트리(M8 「탐색」 — 01 §9-6 「표본마다 MCTS. 우연 노드는 §6-3 분포에서 뽑고 깊이 제한 뒤 평가 함수」, 01 §13-1 M8-17·M8-18).
    /// 판을 저장하지 않는다: 반복마다 채운 판을 뿌리에서 다시 돌리며(엔진 그대로) 지나는 노드를 따라간다 — 트리 안의 굴림은 모두 정해 넣고(<see cref="IRig"/>) 게임 난수는 뿌리 사본 그대로라
    /// 같은 길이면 같은 판이다. 트리 밖(처음 닿은 노드부터)은 굴리기 정책과 반복마다 다른 주사위(AI 난수)로 끝까지(또는 깊이 제한까지) 간다.
    /// 뿌리 답은 고루 돈다(평평한 PIMC와 같은 몫 — 답마다 같은 반복 수), 뿌리 아래 결정 노드는 UCT(정수 — 평균 + 탐험 × √N / (1 + n)), 우연 노드는 묶음 무게 비율에 가장 못 미친 묶음.
    /// 값은 모두 찾는 쪽(my) 점수 — 상대 쪽 결정 노드는 「점수 상한 − 평균」으로 고른다. 인스턴스는 스레드 하나에서 쓴다.
    /// </summary>
    sealed class SearchTree : IRig
    {
        sealed class Node
        {
            /// <summary>0 = 아직 모름(처음 닿은 노드 — 다음에 닿을 때 정한다) · 1 = 결정 · 2 = 우연.</summary>
            public int Kind;
            public int Side;
            public int Prior;
            public int Dice;
            public long Sum;
            public int Visits;
            public readonly List<ChoiceAnswer> Answers = new List<ChoiceAnswer>();
            public readonly List<Node> Kids = new List<Node>();

            public void Reset()
            {
                Kind = Prior = Dice = Visits = 0;
                Side = -1;
                Sum = 0;
                Answers.Clear();
                Kids.Clear();
            }
        }

        /// <summary>점수 상한(이김 + 피해 몫들) — 상대 쪽 노드가 뒤집어 본다.</summary>
        int Top => Score.Top;
        /// <summary>UCT 셈의 잔눈(평균 · 탐험 몫에 곱한다).</summary>
        const long Fine = 16;

        readonly List<Node> pool = new List<Node>();
        readonly List<Node> roots = new List<Node>();
        readonly List<Node> path = new List<Node>(64);
        readonly BattleEval eval = new BattleEval();
        readonly RolloutPolicy descend;
        int used;

        // 반복 하나의 상태
        bool inTree;
        Node cur;
        SampledRng rng;

        // 결정 하나의 설정
        public SearchKind Kind;
        public int My;
        public int Explore;
        public RolloutPolicy Policy;
        public int Depth;
        /// <summary>뿌리 답마다 이김 몫(‰)의 합 — 자막의 승산(M8 「연결」). null = 세지 않음. 점수 · 고르기와 무관.</summary>
        public long[] Wins;
        public int[] Weights;
        /// <summary>점수의 피해 몫 무게(<see cref="StrongAgent.Score(int, RolloutResult, int, ScoreWeights)"/> — 야생 · UB 쪽은 크다, 결정 143 ①).</summary>
        public ScoreWeights Score = StrongAgent.OldWeights;
        /// <summary>마감(null = 없음) — 반복마다 본다(StrongAgent의 예산).</summary>
        public System.Func<bool> Stop;

        /// <summary>센 것(StrongAgent가 더해 읽는다).</summary>
        public long Simulations, Stuck, Cuts, Mismatch, Nodes;
        /// <summary>만든 결정 노드 수(<see cref="StrongAgent.NodeKey"/>마다 — 지우지 않고 쌓는다).</summary>
        public readonly Dictionary<int, long> Kinds = new Dictionary<int, long>();

        public SearchTree()
        {
            descend = Descend;
        }

        Node New()
        {
            if (used == pool.Count) pool.Add(new Node());
            var n = pool[used++];
            n.Reset();
            Nodes++;
            return n;
        }

        /// <summary>
        /// 표본 하나(root = 채운 판)에서 반복 each × 답 수. 뿌리 답 i의 점수 합 · 굴린 수를 sum · count에 더한다. random = 트리 밖 주사위의 AI 난수(표본마다 다른 수열).
        /// 걸어 둔 손보기(<see cref="Rig"/>)는 부르는 쪽이 이것으로 바꿔 건다. 반복마다 <see cref="Stop"/>(마감)을 먼저 보고 지났으면 그때까지로 멈춘다 — 다 돌았으면 true, 마감으로 멈췄으면 false.
        /// </summary>
        public bool Run(GameState root, GameState sim, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal, int each, SampledRng random, long[] sum, int[] count)
        {
            used = 0;
            roots.Clear();
            for (int i = 0; i < legal.Count; i++) roots.Add(New());
            rng = random;
            int total = each * legal.Count;
            for (int t = 0; t < total; t++)
            {
                if (Stop != null && Stop()) return false;
                int i = t % legal.Count;
                sim.CopyFrom(root);
                path.Clear();
                cur = roots[i];
                path.Add(cur);
                inTree = true;
                var r = Rollout.ToEnd(sim, ctx, descend, c.Id, legal[i], Rollout.MaxSteps, false, Depth);
                inTree = false;
                Simulations++;
                int win;
                if (r.Cut)
                {
                    Cuts++;
                    win = eval.WinPermille(sim, ctx, My, Weights);
                }
                else if (!r.Ended)
                {
                    Stuck++;
                    continue;
                }
                else win = StrongAgent.WinOf(r, My);
                int score = StrongAgent.Score(win, r, My, Score);
                foreach (var n in path)
                {
                    n.Visits++;
                    n.Sum += score;
                }
                sum[i] += score;
                count[i]++;
                if (Wins != null) Wins[i] += win;
            }
            return true;
        }

        /// <summary>굴리기 정책 자리에 끼우는 트리 따라가기: 트리 안이면 지금 노드(결정)에서 UCT로 고르고, 처음 닿은 노드면 트리를 나와 굴리기 정책으로.</summary>
        ChoiceAnswer Descend(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            if (!inTree || legal.Count < 2) return Policy(s, ctx, c, legal);
            int side = s.Battle != null ? ChoiceRules.BattleSideOf(s, c.Seat) : -1;
            var n = cur;
            if (side < 0 || n.Visits == 0 || Kind != SearchKind.Tree)
            {
                inTree = false;
                return Policy(s, ctx, c, legal);
            }
            if (n.Kind == 0)
            {
                n.Kind = 1;
                n.Side = side;
                int key = StrongAgent.NodeKey(c.Kind, c.Sub, side == My);
                Kinds.TryGetValue(key, out long seen);
                Kinds[key] = seen + 1;
                var prior = Policy(s, ctx, c, legal);
                for (int i = 0; i < legal.Count; i++)
                {
                    n.Answers.Add(legal[i].Clone());
                    n.Kids.Add(null);
                    if (legal[i].SameAs(prior)) n.Prior = i;
                }
            }
            else if (n.Kind != 1 || n.Answers.Count != legal.Count || n.Side != side)
            {
                Mismatch++;
                inTree = false;
                return Policy(s, ctx, c, legal);
            }
            int j = Select(n);
            if (!legal[j].SameAs(n.Answers[j]))
            {
                Mismatch++;
                inTree = false;
                return Policy(s, ctx, c, legal);
            }
            var kid = n.Kids[j];
            if (kid == null) n.Kids[j] = kid = New();
            path.Add(kid);
            cur = kid;
            return legal[j];
        }

        /// <summary>결정 노드에서: 안 가 본 답(굴리기 정책 답 먼저, 그다음 Legal 순서) → UCT(정수, 같으면 앞 답).</summary>
        int Select(Node n)
        {
            var kids = n.Kids;
            if (kids[n.Prior] == null || kids[n.Prior].Visits == 0) return n.Prior;
            for (int i = 0; i < kids.Count; i++)
                if (kids[i] == null || kids[i].Visits == 0) return i;
            long root = Isqrt(n.Visits);
            int best = 0;
            long bestV = long.MinValue;
            for (int i = 0; i < kids.Count; i++)
            {
                var k = kids[i];
                long mean = k.Sum * Fine / k.Visits;
                if (n.Side != My) mean = Top * Fine - mean;
                long v = mean + Explore * Fine * root / (1 + k.Visits);
                if (v > bestV)
                {
                    bestV = v;
                    best = i;
                }
            }
            return best;
        }

        public void Shuffled(List<int> pile) { }

        /// <summary>굴림: 트리 안이면 지금 노드(우연)의 묶음을 정해 넣고, 트리 밖이면 AI 난수로 면을 새로 굴린다(반복마다 다른 주사위).</summary>
        public void Rolled(AttackDieFace[] faces)
        {
            if (inTree)
            {
                var n = cur;
                if (n.Visits == 0) inTree = false;
                else
                {
                    if (n.Kind == 0)
                    {
                        n.Kind = 2;
                        n.Dice = faces.Length;
                        int m = ForcedDice.Outcomes(n.Dice).Length;
                        for (int i = 0; i < m; i++) n.Kids.Add(null);
                    }
                    if (n.Kind == 2 && n.Dice == faces.Length)
                    {
                        var outs = ForcedDice.Outcomes(n.Dice);
                        int j = Stratify(n, outs);
                        ForcedDice.Apply(faces, outs[j]);
                        var kid = n.Kids[j];
                        if (kid == null) n.Kids[j] = kid = New();
                        path.Add(kid);
                        cur = kid;
                        return;
                    }
                    Mismatch++;
                    inTree = false;
                }
            }
            for (int i = 0; i < faces.Length; i++) faces[i] = rng.RollAttackDie();
        }

        /// <summary>우연 노드에서: 지금까지 나눈 수가 묶음 무게 비율에 가장 못 미친 묶음(같으면 앞 묶음) — 나눈 수가 늘면 무게 비율에 맞는다(1수 기대값).</summary>
        static int Stratify(Node n, DiceOutcome[] outs)
        {
            long total = AttackDice.TotalWeight(n.Dice);
            long seen = 0;
            foreach (var k in n.Kids)
                if (k != null) seen += k.Visits;
            int best = 0;
            long bestGap = long.MinValue;
            for (int i = 0; i < outs.Length; i++)
            {
                long v = n.Kids[i]?.Visits ?? 0;
                long gap = (seen + 1) * outs[i].Weight - v * total;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>정수 제곱근(내림).</summary>
        public static long Isqrt(long x)
        {
            if (x <= 0) return 0;
            long r = 0;
            for (long bit = 1L << 30; bit > 0; bit >>= 1)
            {
                long t = r | bit;
                if (t * t <= x) r = t;
            }
            return r;
        }
    }
}
