using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ① 능력 발동 표(엔진 무관 — dotnet에서 돈다): 트레이너 카드를 좌석마다 정해 준 봇 판(<see cref="Playout.Run"/>)을 돌리며 능력 102개마다 센다.
    /// · 풀림 = 그 능력이 풀린 좌석 수(CardUnlocked · youngster R로 바꾼 새 카드 · 시작에 풀어 준 판).
    /// · 제안 = 좌석 선택이 열렸을 때 그 능력 때문에 생긴 합법 답이 하나라도 있는 선택 수 — 그 좌석의 등급만 한 칸 내려 Legal을 다시 셈(빨강 = 골드 − 실버, 파랑 = 실버 − 브론즈) ·
    ///   출처가 카드인 선택(Source.Kind = TrainerCard)은 통째로 그 카드의 제안(색은 답 뒤 AbilityUsed로, 못 가리면 같은 갈래에서 배운 색 · 그래도 모르면 「색 모름」).
    ///   막음 = 등급을 내리면 생기는 답(그 능력이 막는 답 — essentia B 같은 제한).
    /// · 고름 = 낸 답이 그 능력의 답이었던 명령(시간 초과의 기본 답 포함) + 등급을 내리면 거절되는 정비 명령.
    /// · 사용 = AbilityUsed 수 · 효과 = 고른 명령 가운데 판 상태(좌석 · 포켓몬 · 토큰 · 덱 · 길 · 날씨 · 막 · UB)가 바뀐 명령 수 · 출처 이벤트 = Source · ParentSource · Mod가 그 카드인 이벤트(카드 단위 — 상시 보정은 여기로만 보인다).
    /// 다른 좌석의 카드가 이 좌석의 답을 바꾸는 것(OtherWith)은 그 좌석 등급을 내리지 않아 제안으로 잡히지 않는다.
    /// </summary>
    static class ExecAudit
    {
        public const int Blue = 0, Red = 1, Unknown = 2;

        public sealed class Row
        {
            public string Card;
            public int Color; // Blue · Red
            public long Seats, Unlocked, Offered, OfferedAnswers, Blocked, Chosen, Used, Changed, SourceEvents;
        }

        public sealed class Table
        {
            public Row[] Rows;
            /// <summary>색을 못 가린 카드 출처 선택 수[카드].</summary>
            public long[] UnknownColorOffers;
            public int Games, Commands;
            public List<string> Errors = new List<string>();
            public readonly Dictionary<string, int> Learned = new Dictionary<string, int>();
            public Row Of(int card, int color) => Rows[card * 2 + color];
        }

        public static Table NewTable(Ctx ctx)
        {
            int n = ctx.Cards.TrainerCards.Count;
            var t = new Table { Rows = new Row[n * 2], UnknownColorOffers = new long[n] };
            for (int k = 0; k < n; k++)
                for (int c = 0; c < 2; c++) t.Rows[k * 2 + c] = new Row { Card = ctx.Cards.TrainerCards[k].Key, Color = c };
            return t;
        }

        /// <summary>
        /// 한 판. cards[seat] = 그 좌석이 고를 카드(TrainerCardPick에서 후보 첫 장과 바꿔 넣고 고른다 — 카드 총수 보존). unlockAtStart = 첫 맵 턴에 보통 → 강함을 얻게 해 골드로(UB 판처럼 짧은 판).
        /// </summary>
        public static void Game(Ctx ctx, Table t, int seats, ulong seed, int[] cards, Campaign campaign, bool unlockAtStart, int maxRounds = 0, RuleOptions options = null)
        {
            var g = new Playout.Game();
            var legal = new List<ChoiceAnswer>();
            var lower = new List<ChoiceAnswer>();
            var lowest = new List<ChoiceAnswer>();
            Pend pend = null;
            bool started = false;
            void Count(GameState s, int seat, int color) => t.Of(s.Seats[seat].TrainerCard, color).Unlocked++;

            ChoiceAnswer Steer(GameState s, PendingChoice c)
            {
                if (c.Kind != ChoiceKind.TrainerCardPick || cards == null || c.Seat >= cards.Length || cards[c.Seat] < 0) return null;
                int want = cards[c.Seat];
                var offer = s.Seats[c.Seat].TrainerCardOffer;
                if (!offer.Contains(want))
                {
                    var deck = s.Decks[(int)DeckId.TrainerCard];
                    int at = deck.IndexOf(want);
                    if (at >= 0) { deck[at] = offer[0]; offer[0] = want; }
                    else
                        foreach (var other in s.Seats)
                        {
                            int j = other.TrainerCardOffer.IndexOf(want);
                            if (j < 0 || other == s.Seats[c.Seat]) continue;
                            other.TrainerCardOffer[j] = offer[0];
                            offer[0] = want;
                            break;
                        }
                    // 바꿔 넣은 후보에 맞게 열린 카드 고르기의 기본 답(첫 후보)을 고친다
                    foreach (var o in s.Pending)
                        if (o.Kind == ChoiceKind.TrainerCardPick && !s.Seats[o.Seat].TrainerCardOffer.Contains(o.Default.Target))
                            o.Default = ChoiceAnswer.Pick(s.Seats[o.Seat].TrainerCardOffer[0]);
                }
                Engine.Legal(s, ctx, c, legal);
                return legal.Find(a => a.Target == want)?.Clone();
            }

            void Before(GameState s, int seat, Command cmd)
            {
                pend = null;
                if (cmd.Kind == CommandKind.Anytime)
                {
                    if (seat < 0 || s.Seats[seat].TrainerCard < 0) return;
                    int color = MaintColor(s, ctx, seat, cmd.Maint);
                    if (color < 0) return;
                    pend = new Pend { Seat = seat, Card = s.Seats[seat].TrainerCard, Chosen = color, Hash = Hash(s) };
                    return;
                }
                var c = s.Pending.Find(x => x.Id == cmd.ChoiceId);
                if (c == null || c.Seat < 0 || c.Seat >= s.SeatCount) return;
                var st = s.Seats[c.Seat];
                if (st.TrainerCard < 0) return;
                var answer = cmd.Kind == CommandKind.Timeout ? c.Default : cmd.Answer;
                var p = new Pend { Seat = c.Seat, Card = st.TrainerCard, Chosen = -1, Kind = c.Kind, Sub = c.Sub, Subject = c.Subject };
                if (c.Source.Kind == SourceKind.TrainerCard && c.Source.Id == st.TrainerCard)
                {
                    p.CardAsk = true;
                    p.AskYes = answer != null && answer.Kind != AnswerKind.Decline;
                    if (p.AskYes) p.Hash = Hash(s);
                    pend = p;
                    return;
                }
                int tier = st.TrainerCardTier;
                if (tier < TrainerRules.Silver) return;
                Engine.Legal(s, ctx, c, legal);
                try
                {
                    if (tier == TrainerRules.Gold)
                    {
                        st.TrainerCardTier = TrainerRules.Silver;
                        Engine.Legal(s, ctx, c, lower);
                        Diff(legal, lower, answer, Red, p);
                    }
                    else lower.Clear();
                    st.TrainerCardTier = TrainerRules.Bronze;
                    Engine.Legal(s, ctx, c, lowest);
                    Diff(tier == TrainerRules.Gold ? lower : legal, lowest, answer, Blue, p);
                }
                finally { st.TrainerCardTier = tier; }
                if (p.Chosen >= 0) p.Hash = Hash(s);
                if (p.Offers[0] + p.Offers[1] + p.Blocks[0] + p.Blocks[1] + (p.Chosen >= 0 ? 1 : 0) > 0) pend = p;
            }

            void After(GameState s, int seat, Command cmd, EventLog log)
            {
                t.Commands++;
                int usedColor = -1;
                foreach (var e in log.Events)
                {
                    switch (e)
                    {
                        case AbilityUsed au:
                            if (au.CardKey >= 0) t.Of(au.CardKey, au.Ability == AbilityColor.Blue ? Blue : Red).Used++;
                            if (pend != null && au.CardKey == pend.Card) usedColor = au.Ability == AbilityColor.Blue ? Blue : Red;
                            continue;
                        case CardUnlocked cu:
                            if (cu.AbilitySlots != null)
                                foreach (int slot in cu.AbilitySlots) t.Of(cu.CardKey, slot == 1 ? Blue : Red).Unlocked++;
                            continue;
                        case TrainerCardSwapped sw:
                            t.Of(sw.ToCard, Blue).Unlocked++;
                            t.Of(sw.ToCard, Red).Unlocked++;
                            t.Of(sw.ToCard, Blue).Seats++;
                            t.Of(sw.ToCard, Red).Seats++;
                            continue;
                    }
                    Sources(e, t);
                }
                var p = pend;
                pend = null;
                if (p == null) return;
                if (p.CardAsk)
                {
                    string key = $"{p.Card}/{p.Kind}/{p.Sub}/{p.Subject}";
                    int color = usedColor;
                    if (color >= 0) t.Learned[key] = color;
                    else if (!t.Learned.TryGetValue(key, out color))
                    {
                        int tier = s.Seats[p.Seat].TrainerCard == p.Card ? s.Seats[p.Seat].TrainerCardTier : TrainerRules.Gold;
                        color = tier == TrainerRules.Silver ? Blue : Unknown;
                    }
                    if (color == Unknown)
                    {
                        t.UnknownColorOffers[p.Card]++;
                        return;
                    }
                    var row = t.Of(p.Card, color);
                    row.Offered++;
                    row.OfferedAnswers++;
                    if (!p.AskYes) return;
                    row.Chosen++;
                    if (Hash(s) != p.Hash) row.Changed++;
                    return;
                }
                for (int k = 0; k < 2; k++)
                {
                    var row = t.Of(p.Card, k);
                    if (p.Offers[k] > 0) { row.Offered++; row.OfferedAnswers += p.Offers[k]; }
                    if (p.Blocks[k] > 0) row.Blocked++;
                }
                if (p.Chosen < 0) return;
                var r = t.Of(p.Card, p.Chosen);
                r.Chosen++;
                if (Hash(s) != p.Hash) r.Changed++;
            }

            void Each(GameState s)
            {
                if (started || s.Phase != Phase.Turn) return;
                started = true;
                foreach (var st in s.Seats)
                    if (st.TrainerCard >= 0)
                    {
                        t.Of(st.TrainerCard, Blue).Seats++;
                        t.Of(st.TrainerCard, Red).Seats++;
                    }
                if (!unlockAtStart) return;
                for (int i = 0; i < s.SeatCount; i++)
                {
                    int before = s.Seats[i].TrainerCardTier;
                    UnlockNow(s, ctx, i);
                    int after = s.Seats[i].TrainerCardTier;
                    if (s.Seats[i].TrainerCard < 0) continue;
                    if (before < TrainerRules.Silver && after >= TrainerRules.Silver) Count(s, i, Blue);
                    if (before < TrainerRules.Gold && after >= TrainerRules.Gold) Count(s, i, Red);
                }
            }

            try
            {
                Playout.Run(ctx, seats, seed, Each, g, campaign, maxRounds, options, steer: Steer, commands: Before, submitted: After);
                t.Games++;
            }
            catch (Exception e)
            {
                t.Errors.Add($"{campaign} {seats}인 시드 {seed}: {e.GetType().Name}: {e.Message}");
            }
        }

        sealed class Pend
        {
            public int Seat, Card, Chosen, Subject;
            public ChoiceKind Kind;
            public ChoiceSub Sub;
            public bool CardAsk, AskYes;
            public ulong Hash;
            public readonly int[] Offers = new int[2], Blocks = new int[2];
        }

        /// <summary>full에 있고 low에 없는 답 = 그 색의 답(Offers), low에만 있는 답 = 그 색이 막은 답(Blocks). 낸 답이 그 색의 답이면 Chosen.</summary>
        static void Diff(List<ChoiceAnswer> full, List<ChoiceAnswer> low, ChoiceAnswer answer, int color, Pend p)
        {
            foreach (var a in full)
            {
                if (ChoiceRules.Contains(low, a)) continue;
                p.Offers[color]++;
                if (answer != null && p.Chosen < 0 && Same(a, answer)) p.Chosen = color;
            }
            foreach (var a in low)
                if (!ChoiceRules.Contains(full, a)) p.Blocks[color]++;
        }

        /// <summary>결정 71 고른 길(Move + Values)은 Legal에 옛 꼴(도착 칸만)로 있다.</summary>
        static bool Same(ChoiceAnswer legal, ChoiceAnswer answer) =>
            legal.SameAs(answer) || (answer.Kind == AnswerKind.Move && answer.Values.Count > 0 && legal.Kind == AnswerKind.Move && legal.Target == answer.Target && legal.Arg == answer.Arg && legal.Values.Count == 0);

        /// <summary>정비가 그 좌석의 능력 때문에 되는가: 등급을 내리면 거절되는 색(빨강 먼저). 아니면 −1.</summary>
        static int MaintColor(GameState s, Ctx ctx, int seat, Maintenance m)
        {
            var st = s.Seats[seat];
            int tier = st.TrainerCardTier;
            if (tier < TrainerRules.Silver || m == null) return -1;
            try
            {
                if (tier == TrainerRules.Gold)
                {
                    st.TrainerCardTier = TrainerRules.Silver;
                    if (MaintenanceRules.Check(s, ctx, seat, m) != RejectReason.None) return Red;
                }
                st.TrainerCardTier = TrainerRules.Bronze;
                if (MaintenanceRules.Check(s, ctx, seat, m) != RejectReason.None) return Blue;
                return -1;
            }
            finally { st.TrainerCardTier = tier; }
        }

        /// <summary>보통 한 마리 → 강함 한 마리를 카드 효과로 얻은 것처럼(실버 → 골드, 「When unlocked」 포함 — UbPlayoutTests.LateGame과 같은 길). 파티에 빈칸이 없으면 그만큼 못 푼다.</summary>
        static void UnlockNow(GameState s, Ctx ctx, int seat)
        {
            var st = s.Seats[seat];
            if (st.TrainerCard < 0) return;
            foreach (var deck in new[] { DeckId.Moderate, DeckId.Strong })
            {
                var pile = s.Decks[(int)deck];
                if (pile.Count == 0 || PokemonRules.PartyCount(st) >= st.OpenSlots) return;
                int card = pile[pile.Count - 1];
                pile.RemoveAt(pile.Count - 1);
                int p = PokemonRules.Spawn(s, card, seat, (int)deck);
                PokemonRules.AddToParty(st, p);
                TrainerRules.Obtained(s, ctx, Scene.Fx(s, null, seat), seat, p, ObtainCause.CardEffect);
            }
        }

        // ── 판 상태 지문(효과가 있었나) — 선택 · 사용 기록 · 번호는 뺀다 ──

        sealed class Fnv : IStateWriter
        {
            public ulong H = 14695981039346656037UL;
            public void Int(int value) => ULong((ulong)(uint)value);
            public void ULong(ulong value)
            {
                for (int i = 0; i < 8; i++)
                {
                    H ^= (value >> (i * 8)) & 0xFF;
                    H *= 1099511628211UL;
                }
            }
        }

        static ulong Hash(GameState s)
        {
            var w = new Fnv();
            foreach (var st in s.Seats) st.Walk(w);
            foreach (var p in s.Pokemon) p.Walk(w);
            foreach (var t in s.Tokens) t.Walk(w);
            void Ints(List<int> list)
            {
                w.Int(list.Count);
                foreach (int x in list) w.Int(x);
            }
            for (int d = 0; d < GameState.DeckCount; d++)
            {
                Ints(s.Decks[d]);
                Ints(s.Discards[d]);
            }
            Ints(s.Roads);
            w.Int(s.Weather);
            w.Int(s.FallenStars);
            s.Hos?.Walk(w);
            s.Ub?.Walk(w);
            return w.H;
        }

        // ── 출처 이벤트(Source · ParentSource · Mod — 반사로 훑는다) ──

        static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();

        static void Sources(GameEvent e, Table t)
        {
            var seen = new HashSet<int>();
            Walk(e, seen, 0);
            foreach (int card in seen)
                if (card >= 0 && card * 2 + 1 < t.Rows.Length)
                {
                    t.Rows[card * 2].SourceEvents++;
                    t.Rows[card * 2 + 1].SourceEvents++;
                }
        }

        static void Walk(object o, HashSet<int> into, int depth)
        {
            if (o == null || depth > 3) return;
            var type = o.GetType();
            if (!fields.TryGetValue(type, out var fs)) fields[type] = fs = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fs)
            {
                var ft = f.FieldType;
                if (ft == typeof(SourceRef))
                {
                    var src = (SourceRef)f.GetValue(o);
                    if (src.Kind == SourceKind.TrainerCard) into.Add(src.Id);
                }
                else if (ft == typeof(Mod)) Walk(f.GetValue(o), into, depth + 1);
                else if (typeof(IEnumerable).IsAssignableFrom(ft) && ft != typeof(string))
                {
                    if (!(f.GetValue(o) is IEnumerable list)) continue;
                    foreach (var x in list)
                        if (x != null && !x.GetType().IsPrimitive && !x.GetType().IsEnum && x.GetType().Namespace == "Sinnoh.Rules") Walk(x, into, depth + 1);
                }
                else if (!ft.IsPrimitive && !ft.IsEnum && ft.IsClass && ft.Namespace == "Sinnoh.Rules" && !typeof(GameEvent).IsAssignableFrom(ft)) Walk(f.GetValue(o), into, depth + 1);
            }
        }

        // ── 표 ──

        /// <summary>
        /// 판정: 안 풀림(풀린 좌석 0) · 안 나옴(풀렸는데 제안 · 고름 · 사용 · 출처 이벤트 모두 0) · 효과 0(고른 명령에서 판 상태가 한 번도 안 바뀜) · 답만(제안은 있는데 봇이 한 번도 안 고름) · 사용만(AbilityUsed는 있는데 제안 · 고름 0 — 저절로 도는 능력).
        /// </summary>
        public static string Verdict(Row r)
        {
            if (r.Unlocked == 0) return "안 풀림";
            if (r.Offered == 0 && r.Chosen == 0 && r.Used == 0 && r.SourceEvents == 0) return "안 나옴";
            if (r.Chosen > 0 && r.Changed == 0) return "효과 0";
            if (r.Offered > 0 && r.Chosen == 0 && r.Used == 0) return "답만";
            if (r.Offered == 0 && r.Chosen == 0 && r.Used > 0) return "사용만";
            if (r.Offered == 0 && r.Chosen == 0 && r.Used == 0) return "출처 이벤트만";
            return "ok";
        }

        public static string Tsv(Table t)
        {
            var sb = new StringBuilder();
            sb.Append("card\tcolor\tseats\tunlocked\toffered_choices\toffered_answers\tblocked_choices\tchosen\tability_used\tchosen_changed\tsource_events(card)\tunknown_color_asks(card)\tverdict\n");
            foreach (var r in t.Rows)
            {
                int card = Array.IndexOf(t.Rows, r) / 2;
                sb.Append($"{r.Card}\t{(r.Color == Blue ? "B" : "R")}\t{r.Seats}\t{r.Unlocked}\t{r.Offered}\t{r.OfferedAnswers}\t{r.Blocked}\t{r.Chosen}\t{r.Used}\t{r.Changed}\t{r.SourceEvents}\t{t.UnknownColorOffers[card]}\t{Verdict(r)}\n");
            }
            return sb.ToString();
        }
    }
}
