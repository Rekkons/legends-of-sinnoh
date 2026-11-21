using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 봇 플레이아웃 — 울트라 버스트 판(M6 「봇 — UB 판」, 완료 조건 1). 정책은 시험용이라 단순하다(이기는 것이 목표가 아니라 UB 코드가 봇 판에서 돌게 한다) [가정 M6-봇]:
    /// ⑪ 모으기(라운드 ≤ 2 + 시드 % 4, 또는 파티 넷 미만이고 라운드 ≤ 8 + 시드 % 4): 찾아보기가 되면 1/2로, 나머지는 Legal에서 고르게.
    /// ⑫ 사냥: 다쳤으면 마을(울트라메가로폴리스 포함)에서 회복 — 파티 체력이 60% 밑이면 가까운 마을이 목표. 아니면 목표 = 프리즘이 넘어왔거나(UB 격파 3 이상) 균열 50% 이상이면 네크로즈마 토큰,
    ///    그 밖에는 가장 가까운 UB 토큰 · 호수 동굴(수호신)·신오신전(실버디) — 그 카드가 전설 덱에 있을 때. 걸음이 줄어드는 이동·왜곡 게이트·공중날기를 7/8로, 목표 칸에서는 7/8로 찾아보기(배틀).
    ///    걸음 표는 게이트를 이동력 없는 간선으로 넣은 거꾸로 걸음 셈(<see cref="Bot.UbDist"/> — 깨어진 세계는 게이트로만 이어진다).
    /// ⑬ 축복(정비 명령, 맵 행동 차례 앞에): 탈락한 좌석이 있으면 부활, 균열 50% 이상이고 축복이 둘 이상이면 다음 UB 턴 건너뛰기. 여럿 판의 동의 묶음은 허용 · 라운드 끝 건너뛰기 물음은 같은 기준(결정 84 U1·U2).
    /// ⑭ UB·네크로즈마·호수 시련 배틀은 ⑦처럼 고르게 고르지 않는다(Serious). 균열 카드 붙이기·버리기·옮기기는 기본 답.
    /// </summary>
    static partial class Playout
    {
        public sealed partial class Game
        {
            /// <summary>M6 「봇 — UB 판」: 쓰러뜨린 UB · 네크로즈마 페이즈 격파 · 탈락 · 부활 · 축복 쓰기(건너뛰기 · 부활) · 건너뛴 UB 턴 · 얻은 축복 · 넘긴 시련 · 쓰러뜨리거나 잡은 수호신 · 뽑은 온슬롯 카드.</summary>
            public int UbDefeated, NecrozmaPhases, Eliminations, Revivals, BlessingSkips, BlessingRevives, UbTurnsSkipped, BlessingsGained, TrialsDone, GuardiansDown, OnslaughtCards;
            /// <summary>M6 「울트라 버스트 규칙 합침」: UB·네크로즈마·시련 배틀이 열려 있는 동안 나온 AbilityUsed(트레이너 카드 능력이 그 배틀에서도 도는지 — 배틀을 끝낸 명령의 것은 세지 않는다).</summary>
            public int UbBattleAbilities;
            /// <summary>그 가운데 능력마다[카드 × 2 + (0 파랑 · 1 빨강)] — 없으면 null.</summary>
            public int[] UbAbilityUses;
        }

        /// <summary>울트라 버스트 쪽 배틀(UB · 네크로즈마 · 호수 시련).</summary>
        static bool UbSideBattle(BattleKind k) => k == BattleKind.UltraBeast || k == BattleKind.Necrozma || k == BattleKind.Trial;

        /// <summary>UB 판의 이벤트를 센다(After). 센 이벤트면 true.</summary>
        static bool CountUb(Game g, GameEvent e)
        {
            switch (e)
            {
                case TokenRemoved tr when tr.TokenKind == TokenKind.UltraBeast: g.UbDefeated++; return true; // UB 토큰은 격파로만 빠진다(UbBattle.Defeated)
                case NecrozmaPhase _: g.NecrozmaPhases++; return true;
                case Eliminated _: g.Eliminations++; return true;
                case Revived _: g.Revivals++; return true;
                case BlessingUsed bu:
                    if (bu.Target == UbBlessing.SkipTurn) g.BlessingSkips++;
                    else g.BlessingRevives++;
                    return true;
                case UbTurnSkipped _: g.UbTurnsSkipped++; return true;
                case BlessingGained _: g.BlessingsGained++; return true;
                case TrialProgress tp when tp.Done: g.TrialsDone++; return true;
                case GuardianDown _: g.GuardiansDown++; return true;
                case OnslaughtDrawn _: g.OnslaughtCards++; return true;
            }
            return false;
        }

        public sealed partial class Bot
        {
            readonly Dictionary<int, int[]> ubDists = new Dictionary<int, int[]>();
            readonly List<int> gateFrom = new List<int>(), gateTo = new List<int>();
            bool gatesRead;

            /// <summary>⑬ 아르세우스의 축복: 탈락한 좌석 부활 → 균열 50% 이상이고 축복이 둘 이상이면 UB 턴 건너뛰기. 할 것이 없으면 null.</summary>
            static Maintenance UbMaintain(GameState s, int seat)
            {
                var ub = s.Ub;
                if (ub == null || ub.Blessings <= 0) return null;
                for (int i = 0; i < s.SeatCount; i++)
                {
                    if (!s.Seats[i].Eliminated) continue;
                    var m = UbBlessing.ReviveCommand(i);
                    if (UbBlessing.Check(s, m) == RejectReason.None) return m;
                }
                if (UbTurn.Breach(s) >= 50 && ub.Blessings >= 2)
                {
                    var m = UbBlessing.SkipCommand();
                    if (UbBlessing.Check(s, m) == RejectReason.None) return m;
                }
                return null;
            }

            /// <summary>⑪·⑫ UB 판의 맵 행동(진화·제작 뒤).</summary>
            ChoiceAnswer UbMap(GameState s, Ctx ctx, PendingChoice c, SeatState me, Pcg32 rng)
            {
                bool hunt = s.Round > explore && (PartySize(me) >= 4 || s.Round > explore + 6);
                if (hunt)
                {
                    var heal = legal.Find(a => a.Kind == AnswerKind.VillageHeal);
                    if (heal != null && Hurt(s, ctx, me)) return heal.Clone();
                    bool village = HpFraction(s, ctx, me) < 0.6;
                    int goal = village ? NearestVillage(s, ctx, me.Cell) : UbGoal(s, ctx, me.Cell);
                    if (goal >= 0)
                    {
                        if (me.Cell == goal && !village)
                        {
                            var fight = legal.Find(a => a.Kind == AnswerKind.Search && (a.Arg >= 0 || UbBattle.IsToken(s, a.Target) || UbNecrozma.IsToken(s, a.Target)));
                            if (fight != null && rng.Next(8) != 0) return fight.Clone();
                            if (me.MovesLeft == 0) return ChoiceAnswer.Of(AnswerKind.EndTurn); // 목표 칸인데 이동력 0 — 다음 턴에
                        }
                        else if (rng.Next(8) != 0)
                        {
                            var dist = UbDist(s, ctx, goal);
                            ChoiceAnswer best = null;
                            int bestDist = Dist(dist, me.Cell);
                            foreach (var a in legal)
                            {
                                int d = a.Kind == AnswerKind.Move ? Dist(dist, a.Target)
                                    : a.Kind == AnswerKind.Fly ? Dist(dist, ctx.Fixtures.Fixtures[a.Target].Cell) : int.MaxValue;
                                if (d >= bestDist) continue;
                                best = a;
                                bestDist = d;
                            }
                            if (best != null) return best.Clone();
                            if (me.MovesLeft == 0) return ChoiceAnswer.Of(AnswerKind.EndTurn);
                        }
                    }
                }
                // ⑪ 모으기(목표가 없거나 갈 수 없을 때도)
                var search = legal.Find(a => a.Kind == AnswerKind.Search);
                if (search != null && rng.Next(2) == 0) return search.Clone();
                return legal[rng.Next(legal.Count)].Clone();
            }

            /// <summary>⑫ 목표 칸: 네크로즈마(UB 격파 3 이상 또는 균열 50% 이상) → 가장 가까운 UB 토큰 · 수호신·실버디 자리. 없으면 −1.</summary>
            int UbGoal(GameState s, Ctx ctx, int from)
            {
                if (s.Ub.Defeated >= 3 || UbTurn.Breach(s) >= 50)
                    foreach (var t in s.Tokens)
                        if (t.Kind == TokenKind.Necrozma && t.Cell >= 0) return t.Cell;
                int best = -1, bestDist = int.MaxValue;
                foreach (var t in s.Tokens)
                    if (t.Kind == TokenKind.UltraBeast && t.Cell >= 0) Nearer(s, ctx, from, t.Cell, ref best, ref bestDist);
                var fixtures = ctx.Fixtures.Fixtures;
                for (int f = 0; f < fixtures.Count; f++)
                {
                    if (fixtures[f].Kind != FixtureKind.LegendarySite) continue;
                    int card = LegendRules.SiteCard(s, ctx, f);
                    if (card >= 0 && (UbTrial.LakeOf(ctx, card) >= 0 || ctx.Cards.Pokemon[card].Key == UbTrial.Silvally)) Nearer(s, ctx, from, fixtures[f].Cell, ref best, ref bestDist);
                }
                return best;
            }

            /// <summary>가장 가까운 마을(울트라메가로폴리스 포함) 칸. 없으면 −1.</summary>
            int NearestVillage(GameState s, Ctx ctx, int from)
            {
                int best = -1, bestDist = int.MaxValue;
                foreach (var f in ctx.Fixtures.Fixtures)
                    if (f.Kind == FixtureKind.Village) Nearer(s, ctx, from, f.Cell, ref best, ref bestDist);
                return best;
            }

            void Nearer(GameState s, Ctx ctx, int from, int cell, ref int best, ref int bestDist)
            {
                int d = Dist(UbDist(s, ctx, cell), from);
                if (d >= bestDist) return;
                best = cell;
                bestDist = d;
            }

            /// <summary>
            /// 그 칸까지의 걸음 표(반걸음 단위): 걷기 한 칸 = 2 · 왜곡 게이트 = 1(이동력은 안 들지만 같은 값이면 게이트 쪽을 고르게). 도착 칸에서 거꾸로 센다(높낮이는 들어가는 쪽 기준 — ElevationAllows(이웃 → 칸)).
            /// 닿지 못하면 MoveRules.Unreachable. 보드·게이트는 판 내내 같아 칸마다 들고 있는다.
            /// </summary>
            int[] UbDist(GameState s, Ctx ctx, int goal)
            {
                if (ubDists.TryGetValue(goal, out var dist)) return dist;
                var board = ctx.Board;
                if (!gatesRead)
                {
                    gatesRead = true;
                    var fixtures = ctx.Fixtures.Fixtures;
                    foreach (var f in fixtures)
                        if (f.Kind == FixtureKind.Gate && f.Pair != FixtureDef.NoPair)
                        {
                            gateFrom.Add(f.Cell);
                            gateTo.Add(fixtures[f.Pair].Cell);
                        }
                }
                int n = board.Count;
                dist = new int[n];
                for (int i = 0; i < n; i++) dist[i] = MoveRules.Unreachable;
                var buckets = new List<int>[2 * n + 3];
                dist[goal] = 0;
                buckets[0] = new List<int> { goal };
                for (int k = 0; k < buckets.Length; k++)
                {
                    var bucket = buckets[k];
                    if (bucket == null) continue;
                    foreach (int c in bucket)
                    {
                        if (dist[c] != k) continue;
                        for (int d = 0; d < Board.Directions; d++)
                        {
                            int nb = board.Neighbor(c, d);
                            if (nb == Board.NoCell || !MoveRules.ElevationAllows(board[nb], board[c], s.Options.ElevationMode)) continue;
                            Relax(dist, buckets, nb, k + 2);
                        }
                        for (int g = 0; g < gateTo.Count; g++)
                            if (gateTo[g] == c) Relax(dist, buckets, gateFrom[g], k + 1);
                    }
                }
                ubDists[goal] = dist;
                return dist;
            }

            static void Relax(int[] dist, List<int>[] buckets, int cell, int d)
            {
                if (d >= buckets.Length || (dist[cell] != MoveRules.Unreachable && dist[cell] <= d)) return;
                dist[cell] = d;
                (buckets[d] ?? (buckets[d] = new List<int>())).Add(cell);
            }
        }
    }
}
