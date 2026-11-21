using System;
using System.Collections.Generic;
using System.Text;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ③ 트레이너 능력 충돌 쌍(2026-11-20 사용자): 트레이너전 한 판을 좌석 0(카드 A) 대 좌석 1(카드 B) + 배틀 밖 좌석 2(카드 C)로 놓고,
    /// 셋 다 골드 · 운명 카드 2장씩 · 출전 포켓몬에 지닌물건 하나씩(쌍마다 돌려 가며)으로 「쓸 수 있으면 쓴다」 정책으로 끝까지 몬다. 같은 반응 묶음(GroupId — rulings G6)에 둘 이상이 「쓴다」고 답한 순간이 「같은 순간에 쓸 수 있는 쌍」이다.
    /// 본다: ① 예외 · 기본 답이 합법 밖 · 열린 선택 없이 멈춤 ② 적용 순서 — 묶음에서 쓴다고 답한 좌석들의 첫 AbilityUsed/FatePlayed가 G6 순서(공격하는 쪽 → 받는 쪽 → 배틀 밖, 시계 방향)인가
    /// ③ 한도 — 한 묶음 적용에서 같은 좌석의 같은 능력이 두 번 · 한 배틀 턴에 같은 능력 AbilityUsed 두 번 이상(정보 — 「매번」 능력은 맞다) ④ 둘째 바퀴 — 누가 썼는데 같은 창에서 다시 묻지 않았고, 묶음의 다른 좌석이 쓸 답을 갖고 있었다(정보).
    /// 결과는 의심 목록이다(판정은 사람이) — 시험은 ①만 실패로 본다.
    /// </summary>
    static class ExecAuditPairs
    {
        public sealed class Pair
        {
            public int A, B, C;
            public int Commands, Groups, Together, BothUse, Laps, Moments;
            public string Error;
            public readonly List<string> Order = new List<string>(), Limit = new List<string>(), NoLap = new List<string>();
            /// <summary>같은 묶음에서 같이 「쓴다」고 답한 출처 쌍(좌석:출처 종류:번호).</summary>
            public readonly HashSet<string> Met = new HashSet<string>();
        }

        static List<int> held;

        /// <summary>지닌물건(ItemKind.Held) 전부 — 쌍마다 돌려 가며 출전 포켓몬에게 쥐여 준다.</summary>
        static List<int> HeldPool(Ctx ctx)
        {
            if (held != null) return held;
            held = new List<int>();
            for (int i = 0; i < ctx.Items.Items.Count; i++)
                if (ctx.Items.Items[i].Kind == ItemKind.Held) held.Add(i);
            return held;
        }

        static int FirstOf(Ctx ctx, PokemonType type, int skip)
        {
            var pk = ctx.Cards.Pokemon;
            for (int i = 0; i < pk.Count; i++)
            {
                if (pk[i].Tier != EncounterTier.Moderate) continue;
                bool fits = false;
                foreach (var t in pk[i].Types) fits |= t == type;
                if (fits && skip-- <= 0) return i;
            }
            return -1;
        }

        static void Party(GameState s, Ctx ctx, int seat, params PokemonType[] types)
        {
            int n = 0;
            foreach (var t in types)
            {
                int def = FirstOf(ctx, t, seat);
                if (def < 0) continue;
                int p = PokemonRules.Spawn(s, def, seat, (int)DeckId.Weak);
                if (PokemonRules.AddToParty(s.Seats[seat], p) < 0) break;
                if (++n >= 3) break;
            }
        }

        /// <summary>한 쌍. a · b = 좌석 0 · 1의 카드(배틀하는 둘), c = 배틀 밖 좌석 2의 카드(−1 = 없음). fateless = 운명 카드를 주지 않는 좌석(−1 = 모두 준다).</summary>
        public static Pair Run(Ctx ctx, int a, int b, int c, ulong seed, int fateless = -1)
        {
            var r = new Pair { A = a, B = b, C = c };
            var s = Battles.Game(ctx, 3, seed);
            foreach (var st in s.Seats) st.OpenSlots = Math.Max(st.OpenSlots, 3);
            Party(s, ctx, 0, PokemonType.Psychic, PokemonType.Fire, PokemonType.Dragon);
            Party(s, ctx, 1, PokemonType.Ghost, PokemonType.Water, PokemonType.Grass);
            Party(s, ctx, 2, PokemonType.Normal);
            int[] cards = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                if (cards[i] < 0) continue;
                // 덱의 그 카드를 빼 온다(카드 수는 이 장면에서 보지 않는다 — 같은 카드를 둘이 가져도 된다)
                s.Seats[i].TrainerCard = cards[i];
                s.Seats[i].TrainerCardTier = TrainerRules.Gold;
                var fate = s.Decks[(int)DeckId.ChangeOfFate];
                for (int k = 0; k < 2 && fate.Count > 0 && i != fateless; k++)
                {
                    s.Seats[i].FateHand.Add(fate[fate.Count - 1]);
                    fate.RemoveAt(fate.Count - 1);
                }
                var pool = HeldPool(ctx);
                int item = pool.Count > 0 ? pool[(a * 7 + b * 3 + i) % pool.Count] : -1;
                int first = s.Seats[i].Party[0];
                if (item >= 0 && first >= 0) s.Pokemon[first].HeldItem = item;
            }
            TrainerRules.Refresh(s, ctx);
            var log = new EventLog();
            var rng = new Pcg32(seed, 0xC0F1);
            var legal = new List<ChoiceAnswer>();
            try
            {
                Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
                var useTurn = new Dictionary<string, int>();
                var moments = new Dictionary<string, List<(int Seat, string Src, int Rank)>>();
                for (int guard = 0; guard < 400; guard++)
                {
                    // 배틀 선택과 배틀 뒤 물음(Window = BattleEnd)까지 — Battles.Drive와 같은 끝
                    var open = s.Pending.FindAll(x => !x.Answered && (s.Battle != null || x.Window == Window.BattleEnd));
                    if (open.Count == 0) { if (s.Battle != null && !s.Pending.Exists(x => !x.Answered)) r.Error = "열린 선택 없이 멈췄다"; break; }
                    var first = open[0];
                    var group = first.GroupId >= 0 ? open.FindAll(x => x.GroupId == first.GroupId) : new List<PendingChoice> { first };
                    if (group.Count > 1) r.Groups++;
                    int[] rank = Ranks(s);
                    var yes = new List<int>();
                    bool othersHad = false;
                    Window window = first.Window;
                    for (int gi = 0; gi < group.Count; gi++)
                    {
                        var ch = group[gi];
                        Engine.Legal(s, ctx, ch, legal);
                        if (ch.Default == null || !ChoiceRules.Contains(legal, ch.Default)) { r.Error = $"{ch.Kind}/{ch.Sub} 기본 답 {ch.Default}이 합법 밖"; break; }
                        var pick = Pick(ch, legal, rng);
                        bool use = ch.Seat >= 0 && pick.Kind != AnswerKind.Decline && (ch.Kind == ChoiceKind.Reaction || pick.Kind == AnswerKind.UseAbility);
                        if (use)
                        {
                            yes.Add(ch.Seat);
                            // 같은 순간(같은 배틀 · 같은 배틀 턴 · 같은 창): 따로 묻는 카드 물음도 여기서 만난다
                            var bt = s.Battle;
                            string at = bt != null ? $"{bt.Id}:{bt.Turn}:{ch.Window}" : $"-:{ch.Window}";
                            if (!moments.TryGetValue(at, out var list)) moments[at] = list = new List<(int, string, int)>();
                            list.Add((ch.Seat, $"{ch.Seat}:{ch.Source.Kind}:{ch.Source.Id}", rank[ch.Seat]));
                        }
                        else if (ch.Seat >= 0 && legal.Exists(x => x.Kind != AnswerKind.Decline)) othersHad = true;
                        var cmd = ch.Seat < 0 ? Command.AiAnswer(ch.Id, ch.Default, new AiReason(AiRule.None, 0, 0)) : Command.AnswerTo(ch.Id, pick);
                        int from = log.Events.Count;
                        var res = Engine.Submit(s, ctx, ch.Seat, cmd, log);
                        r.Commands++;
                        if (!res.Accepted) { r.Error = $"{ch.Kind}/{ch.Sub} 답 {pick} 거절: {res}"; break; }
                        if (gi < group.Count - 1) continue;
                        // 묶음 적용 — 이 명령의 이벤트
                        var events = log.Events.GetRange(from, log.Events.Count - from);
                        if (yes.Count >= 2)
                        {
                            r.Together++;
                            if (group.Count > 1) r.BothUse++;
                            var src = new List<string>();
                            foreach (var g in group) if (yes.Contains(g.Seat)) src.Add($"{g.Seat}:{g.Source.Kind}:{g.Source.Id}");
                            src.Sort(StringComparer.Ordinal);
                            r.Met.Add(string.Join(" + ", src));
                        }
                        CheckOrder(events, yes, rank, window, r);
                        CheckLimit(s, events, r, useTurn);
                        if (yes.Count > 0)
                        {
                            var next = s.Pending.Find(x => !x.Answered);
                            bool lap = next != null && next.Kind == ChoiceKind.Reaction && next.Window == window;
                            if (lap) r.Laps++;
                            else if (othersHad && r.NoLap.Count < 5) r.NoLap.Add($"{window}: 좌석 {string.Join(",", yes)}이 쓴 뒤 다시 묻지 않음(묶음의 다른 좌석에 쓸 답이 있었다)");
                        }
                    }
                    if (r.Error != null) break;
                }
                // 같은 순간에 둘 이상의 좌석이 쓴 것(묶음이든 따로 묻는 물음이든): 출처 쌍 · 답한 순서가 G6 순위를 거스르나
                foreach (var kv in moments)
                {
                    var seen = new List<(int Seat, string Src, int Rank)>();
                    foreach (var m in kv.Value)
                        if (!seen.Exists(x => x.Seat == m.Seat)) seen.Add(m);
                    if (seen.Count < 2) continue;
                    r.Moments++;
                    var src = seen.ConvertAll(x => x.Src);
                    src.Sort(StringComparer.Ordinal);
                    r.Met.Add(string.Join(" + ", src));
                    for (int i = 1; i < seen.Count; i++)
                        if (seen[i].Rank < seen[i - 1].Rank && r.Order.Count < 5)
                            r.Order.Add($"{kv.Key}: 물은 순서 {string.Join(" → ", seen.ConvertAll(x => x.Src))} (G6 순위 {string.Join("→", seen.ConvertAll(x => x.Rank))} — 받는 쪽 · 밖이 먼저)");
                }
            }
            catch (Exception e)
            {
                r.Error = $"{e.GetType().Name}: {e.Message}";
            }
            return r;
        }

        /// <summary>쓸 수 있으면 쓴다: 카드 · 운명 · 우선권 반응은 「안 함」 아닌 답(고르게), 배틀 행동은 능력 1/2 · 아니면 기술(고르게 — 기권 빼고), 나머지는 고르게(기권 빼고).</summary>
        static ChoiceAnswer Pick(PendingChoice c, List<ChoiceAnswer> legal, Pcg32 rng)
        {
            var yes = legal.FindAll(x => x.Kind != AnswerKind.Decline && x.Kind != AnswerKind.Forfeit);
            if (yes.Count == 0) return legal[0].Clone();
            if (c.Kind == ChoiceKind.BattleAction)
            {
                var ability = yes.FindAll(x => x.Kind == AnswerKind.UseAbility);
                if (ability.Count > 0 && rng.Next(2) == 0) return ability[rng.Next(ability.Count)].Clone();
                var moves = yes.FindAll(x => x.Kind == AnswerKind.UseMove);
                if (moves.Count > 0) return moves[rng.Next(moves.Count)].Clone();
            }
            return yes[rng.Next(yes.Count)].Clone();
        }

        /// <summary>G6 순서: 배틀 턴을 가진 쪽(없으면 Side 0) 0 → 다른 쪽 1 → 배틀 밖 좌석은 맵 차례 좌석부터 시계 방향 2, 3, …</summary>
        static int[] Ranks(GameState s)
        {
            var rank = new int[s.SeatCount];
            for (int i = 0; i < rank.Length; i++) rank[i] = 100;
            var b = s.Battle;
            if (b == null) return rank;
            int k = b.ActiveSide >= 0 ? b.ActiveSide : 0;
            int first = b.Sides[k].Owner, second = b.Sides[1 - k].Owner;
            if (first >= 0) rank[first] = 0;
            if (second >= 0) rank[second] = 1;
            int start = s.ActiveSeat >= 0 ? s.ActiveSeat : 0, n = 2;
            for (int d = 0; d < s.SeatCount; d++)
            {
                int seat = (start + d) % s.SeatCount;
                if (rank[seat] == 100) rank[seat] = n++;
            }
            return rank;
        }

        static void CheckOrder(List<GameEvent> events, List<int> yes, int[] rank, Window window, Pair r)
        {
            if (yes.Count < 2) return;
            var order = new List<int>();
            foreach (var e in events)
            {
                int seat = e is AbilityUsed au ? au.Seat : e is FatePlayed fp ? fp.Seat : -1;
                if (seat >= 0 && yes.Contains(seat) && !order.Contains(seat)) order.Add(seat);
            }
            for (int i = 1; i < order.Count; i++)
                if (rank[order[i]] < rank[order[i - 1]] && r.Order.Count < 5)
                    r.Order.Add($"{window}: 적용 순서 좌석 {string.Join("→", order)} (G6 순위 {string.Join("→", order.ConvertAll(x => rank[x]))})");
        }

        static void CheckLimit(GameState s, List<GameEvent> events, Pair r, Dictionary<string, int> useTurn)
        {
            var once = new HashSet<string>();
            int turn = s.Battle != null ? s.Battle.Turn : -1;
            foreach (var e in events)
            {
                if (!(e is AbilityUsed au)) continue;
                string key = $"{au.Seat}:{au.CardKey}:{au.Ability}";
                if (!once.Add(key) && r.Limit.Count < 5) r.Limit.Add($"한 묶음 적용에서 {key} 두 번");
                string tk = key + "@" + turn;
                useTurn[tk] = (useTurn.TryGetValue(tk, out int n) ? n : 0) + 1;
                if (useTurn[tk] == 2 && r.Limit.Count < 5) r.Limit.Add($"배틀 턴 {turn}에 {key} 두 번 이상(정보)");
            }
        }

        public static string Tsv(Ctx ctx, List<Pair> pairs)
        {
            string K(int card) => card >= 0 ? ctx.Cards.TrainerCards[card].Key : "-";
            var sb = new StringBuilder("attacker_card\tdefender_card\toutside_card\tcommands\tgroups\tuse_together\tmoments\tlaps\terror\torder\tlimit\tno_lap\tmet_sources\n");
            foreach (var p in pairs)
                sb.Append($"{K(p.A)}\t{K(p.B)}\t{K(p.C)}\t{p.Commands}\t{p.Groups}\t{p.Together}\t{p.Moments}\t{p.Laps}\t{p.Error}\t{string.Join(" | ", p.Order)}\t{string.Join(" | ", p.Limit)}\t{string.Join(" | ", p.NoLap)}\t{string.Join(" | ", p.Met)}\n");
            return sb.ToString();
        }
    }
}
