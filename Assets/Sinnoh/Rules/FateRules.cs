using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>운명의 변화 카드 네 종류(S13, cards.json change_of_fate.*). 값 순서 = 카드 데이터 순서.</summary>
    public enum FateCard { Dialga, Palkia, Giratina, Arceus }

    // ── 이벤트(01 §3-7 「카드」) ──

    /// <summary>FateDealt.PerSeat 원소. CardKeys는 본인만(남에게는 null — 장수는 Before·After로 남는다).</summary>
    public sealed class FateDeal
    {
        public int Seat = GameEvent.U;
        public int Before = GameEvent.U;
        public int After = GameEvent.U;
        public int[] CardKeys;
    }

    /// <summary>
    /// 운명의 변화 카드를 받음(S13). Source = 까닭(Rule:FallenStar 턴 시작 떨어진 별 · Rule:Respawn 귀환 · Rule:TraitorReturn 배신자가 쓴 카드가 버림 더미에서 손으로 · 그 밖은 그 묶음의 출처).
    /// StarCount = 모인 떨어진 별 수(떨어진 별일 때만, 아니면 −1). 덱이 바닥나 덜 받았으면 After − Before가 준 장수다.
    /// </summary>
    public sealed class FateDealt : GameEvent
    {
        public int StarCount = U;
        public FateDeal[] PerSeat;

        public override GameEvent ForViewer(int viewer)
        {
            if (PerSeat == null) return this;
            var c = Copy<FateDealt>();
            c.PerSeat = new FateDeal[PerSeat.Length];
            for (int i = 0; i < PerSeat.Length; i++)
            {
                var p = PerSeat[i];
                c.PerSeat[i] = p.Seat == viewer ? p : new FateDeal { Seat = p.Seat, Before = p.Before, After = p.After, CardKeys = null };
            }
            return c;
        }
    }

    /// <summary>손패 6장째를 버림(A12). 버림 더미는 공개라 카드도 모두가 본다.</summary>
    public sealed class FateDiscarded : GameEvent
    {
        public int CardKey = U;
        public int Before = U;
        public int After = U;
    }

    /// <summary>
    /// 운명의 변화 효과 하나를 냄(A11 — 1장 효과 또는 3장 효과가 1번). Card = 낸 효과의 카드(FateCard), CardKeys = 손에서 버림 더미로 간 카드(1장 · 3장 — 추종자를 세면 2장),
    /// WildcardAs = 아르세우스가 대신한 카드(FateCard, 없으면 −1), FollowerCounted = 추종자 토큰을 셌다(p.19 1장 몫 — 무녀 파랑·아르세우스의 추종자는 2장 몫일 수 있다, FateRules.FollowerWorth). Before·After = 손패 장수.
    /// </summary>
    public sealed class FatePlayed : GameEvent
    {
        public FateCard Card = (FateCard)U;
        public bool Play3;
        public int[] CardKeys;
        public int WildcardAs = U;
        public bool FollowerCounted;
        public int Before = U;
        public int After = U;
    }

    /// <summary>펄기아 순간이동(rulings A24 ②): 걷지 않고 곧바로 From → To(Source = 펄기아 카드). 이동력은 그대로다.</summary>
    public sealed class Teleported : GameEvent
    {
        public int From = U;
        public int To = U;
    }

    /// <summary>
    /// 운명의 변화(S13, rulings A11·A12, README 결정 75 B1): 나눠 주기 · 손패 5장 · 6장째 버리기 · 턴당 효과 1개 · 1장·3장 내기 · 배신자 연전의 쓴 카드 반환.
    /// 네 장의 효과(rulings A24)는 처리기(<see cref="HandlerName"/>, <see cref="Register"/>)로 돈다 — 처리기가 없는 효과(아르세우스 3장 = M6)는 합법 답에 나오지 않는다.
    /// 더미: GameState.Decks·Discards[ChangeOfFate](끝 = 맨 위). 덱이 바닥나면 버림 더미를 섞어 덱으로(p.19).
    /// </summary>
    public static class FateRules
    {
        /// <summary>손패 상한(p.19 「You may keep up to 5」).</summary>
        public const int HandLimit = 5;
        /// <summary>떨어진 별 몇 개마다 턴 시작에 1장(p.16 「For every 4 Fallen Stars collected」).</summary>
        public const int StarsPerCard = 4;
        /// <summary>3장 효과에 드는 장수(추종자·아르세우스 포함).</summary>
        public const int Play3Count = 3;

        const int Pile = (int)DeckId.ChangeOfFate;
        static readonly string[] Keys = { "change_of_fate.dialga", "change_of_fate.palkia", "change_of_fate.giratina", "change_of_fate.arceus" };
        static readonly FateCard[] Cards = { FateCard.Dialga, FateCard.Palkia, FateCard.Giratina, FateCard.Arceus };

        /// <summary>그 종류의 카드 색인(cards 표).</summary>
        public static int CardKey(Ctx ctx, FateCard card) => ctx.Decks.CardIds.IndexOf(Keys[(int)card]);

        /// <summary>카드 색인 → 종류.</summary>
        public static FateCard KindOf(Ctx ctx, int cardKey)
        {
            foreach (var c in Cards)
                if (CardKey(ctx, c) == cardKey) return c;
            throw new ArgumentOutOfRangeException(nameof(cardKey), cardKey, "운명의 변화 카드가 아니다");
        }

        // ── 나눠 주기 ──

        /// <summary>좌석 하나에 count장(아래 Deal과 같다).</summary>
        public static void Deal(GameState s, Ctx ctx, Emitter fx, SourceRef source, int seat, int count, int starCount = -1) =>
            Deal(s, ctx, fx, source, new[] { seat }, count, starCount);

        /// <summary>
        /// 좌석마다 덱 맨 위에서 count장(좌석 순서대로 한 좌석씩). 덱이 바닥나면 버림 더미를 섞어 덱으로 쓰고(PileShuffled), 그것도 없으면 덜 받는다.
        /// FateDealt 하나를 내고 손패 넘침(A12)을 본다. count ≤ 0이면 아무 일도 없다.
        /// </summary>
        public static void Deal(GameState s, Ctx ctx, Emitter fx, SourceRef source, IReadOnlyList<int> seats, int count, int starCount = -1)
        {
            if (seats == null) throw new ArgumentNullException(nameof(seats));
            if (count <= 0 || seats.Count == 0) return;
            var per = new FateDeal[seats.Count];
            for (int i = 0; i < seats.Count; i++)
            {
                var hand = s.Seats[seats[i]].FateHand;
                int before = hand.Count;
                var got = new List<int>(count);
                for (int n = 0; n < count; n++)
                {
                    int card = Draw(s, fx);
                    if (card < 0) break;
                    hand.Add(card);
                    got.Add(card);
                }
                per[i] = new FateDeal { Seat = seats[i], Before = before, After = hand.Count, CardKeys = got.ToArray() };
            }
            if (fx.On) fx.Add(new FateDealt { Seat = seats.Count == 1 ? seats[0] : SeatId.System, Source = source, StarCount = starCount, PerSeat = per });
            Overflow(s, ctx, fx);
        }

        static int Draw(GameState s, Emitter fx)
        {
            var deck = s.Decks[Pile];
            if (deck.Count == 0)
            {
                var discards = s.Discards[Pile];
                if (discards.Count == 0) return -1;
                deck.AddRange(discards);
                discards.Clear();
                Piles.Shuffle(deck, s.Rng);
                if (fx.On) fx.Add(new PileShuffled { Pile = DeckId.ChangeOfFate });
            }
            int card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return card;
        }

        /// <summary>턴 시작의 떨어진 별 운명(p.16, 04 7행): 모인 별 4개마다 그 턴의 좌석이 1장.</summary>
        public static void DealStars(GameState s, Ctx ctx, Emitter fx, int seat) =>
            Deal(s, ctx, fx, SourceRef.Rule(RuleSource.FallenStar), seat, s.FallenStars / StarsPerCard, s.FallenStars);

        // ── 손패 6장째(A12, 결정 75 B1) ──

        /// <summary>넘친 손패를 버릴 카드를 고르는 선택이 열려 있다 — 그동안 새로 여는 선택은 미뤄 둔다(ChoiceRules.Open → GameState.Held).</summary>
        public static bool Discarding(GameState s)
        {
            foreach (var c in s.Pending)
                if (c.Kind == ChoiceKind.Discard && c.Seat >= 0 && c.Seat < s.SeatCount && s.Seats[c.Seat].FateHand.Count > HandLimit) return true;
            return false;
        }

        /// <summary>
        /// 손패가 5장을 넘은 좌석마다 버릴 카드를 묻는다(차례 좌석부터 시계 방향, 한 장씩 — 넘친 만큼 다시 묻는다). 남의 턴이어도 바로 묻고, 새로 받은 카드도 버릴 수 있다(결정 75 B1 ①②).
        /// 그 선택은 열린 선택의 맨 앞에 서고, 다 버릴 때까지 새 선택은 열리지 않는다(이미 열려 있던 선택은 그대로다 [가정]).
        /// RuleOptions.FateHandOverflow = DiscardAtTurnEnd면 그 좌석의 턴 끝(<see cref="TurnEnded"/>)에만 본다. 손패를 늘리는 곳은 끝에 이것을 부른다(Deal은 스스로 부른다).
        /// </summary>
        public static void Overflow(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Options.FateHandOverflow != FateHandOverflow.DiscardImmediately || s.Phase == Phase.GameOver) return;
            int first = s.ActiveSeat >= 0 ? s.ActiveSeat : 0;
            for (int i = 0; i < s.SeatCount; i++) AskDiscard(s, ctx, fx, (first + i) % s.SeatCount);
        }

        /// <summary>턴 끝(다음 턴을 열기 전): 「턴 끝에」 옵션이면 턴을 끝낸 좌석의 넘친 손패를 묻는다.</summary>
        public static void TurnEnded(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Options.FateHandOverflow != FateHandOverflow.DiscardAtTurnEnd || s.Phase == Phase.GameOver || s.ActiveSeat < 0) return;
            AskDiscard(s, ctx, fx, s.ActiveSeat);
        }

        static void AskDiscard(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (s.Seats[seat].FateHand.Count <= HandLimit) return;
            foreach (var p in s.Pending)
                if (p.Kind == ChoiceKind.Discard && p.Seat == seat) return;
            var c = ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.Discard, Seat = seat, Source = new SourceRef(SourceKind.Fate, -1) });
            // 버리기가 먼저다: 「첫 선택」을 보는 화면·봇이 이것부터 답한다
            s.Pending.Remove(c);
            int at = 0;
            while (at < s.Pending.Count && s.Pending[at].Kind == ChoiceKind.Discard) at++;
            s.Pending.Insert(at, c);
        }

        /// <summary>버리기 답: 고른 카드를 버림 더미로(FateDiscarded). 아직 넘치면 다시 묻고, 버리기가 모두 끝나면 미뤄 둔 선택을 연다.</summary>
        public static void ApplyDiscard(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var hand = s.Seats[c.Seat].FateHand;
            int before = hand.Count;
            if (!hand.Remove(a.Target)) throw new InvalidOperationException($"좌석 {c.Seat}: 손패에 없는 카드 {a.Target}");
            s.Discards[Pile].Add(a.Target);
            if (fx.On) fx.Add(new FateDiscarded { Seat = c.Seat, Source = c.Source, CardKey = a.Target, Before = before, After = hand.Count });
            AskDiscard(s, ctx, fx, c.Seat);
            if (Discarding(s)) return;
            ChoiceRules.Release(s, ctx, fx);
            // 버리기를 기다리느라 멈춘 배틀 진행(배신자 연전의 다음 배틀)을 잇는다
            if (s.Pending.Count == 0 && s.Battle != null) BattleFlow.Run(s, ctx, fx);
        }

        // ── 턴당 효과 1개(A11) ──

        static bool InOwnBattle(GameState s, int seat)
        {
            var b = s.Battle;
            if (b == null) return false;
            for (int k = 0; k < BattleState.SideCount; k++)
                if (b.Sides[k].Owner == seat) return true;
            return false;
        }

        /// <summary>
        /// 그 좌석이 지금 운명 효과를 낼 수 있나(A11): 내 배틀 안이면 그 배틀 턴에 아직 안 냈을 때, 배틀 밖이면 내 턴이고 이번 턴에 아직 안 냈을 때. 두 한도는 따로 센다.
        /// 남의 배틀·남의 턴(배틀 밖)에는 못 낸다. RuleOptions.FateLimitScope = WholeTurn이면 배틀 안팎을 합쳐 맵 턴 하나에 1개다.
        /// </summary>
        public static bool CanUse(GameState s, int seat)
        {
            if (seat < 0 || seat >= s.SeatCount) return false;
            bool battle = InOwnBattle(s, seat);
            if (!battle && (s.Battle != null || s.ActiveSeat != seat || s.Phase != Phase.Turn)) return false;
            bool whole = s.Options.FateLimitScope == FateLimitScope.WholeTurn;
            foreach (var u in s.Used)
            {
                if (u.Seat != seat) continue;
                if (u.Kind == UsageKind.FateMap && u.Scope == s.TurnId && (whole || !battle)) return false;
                if (u.Kind == UsageKind.FateBattle && battle && !whole && u.Scope == s.Battle.Turn) return false;
            }
            return true;
        }

        static void MarkUsed(GameState s, Emitter fx, int seat)
        {
            bool battle = InOwnBattle(s, seat) && s.Options.FateLimitScope != FateLimitScope.WholeTurn;
            var kind = battle ? UsageKind.FateBattle : UsageKind.FateMap;
            s.Used.Add(new UsageMark { Seat = seat, Kind = kind, Scope = battle ? s.Battle.Turn : s.TurnId });
            if (fx.On) fx.Add(new UsageMarked { Seat = seat, Kind = kind });
        }

        // ── 내기 ──

        /// <summary>효과 처리기 이름(EffectDb.Handlers — EffectHandlers.BuiltIn): "fate.&lt;카드&gt;" · 3장 효과는 "fate.&lt;카드&gt;.play3". <see cref="Register"/>가 등록한다.</summary>
        public static string HandlerName(FateCard card, bool play3) => "fate." + card.ToString().ToLowerInvariant() + (play3 ? ".play3" : "");

        /// <summary>그 효과가 구현돼 있나(처리기가 등록됐나). 없으면 합법 답에 나오지 않는다.</summary>
        public static bool Ready(Ctx ctx, FateCard card, bool play3) => ctx.Effects.Handlers.ContainsKey(HandlerName(card, play3));

        /// <summary>그 카드의 효과가 이 캠페인에 있나: 아르세우스의 3장 효과(아르세우스의 축복 1개)는 울트라 버스트에서만(p.19 「during the Ultra Burst campaign」 — 신오의 심장에서는 낼 수 없다 [가정]).</summary>
        public static bool InCampaign(GameState s, FateCard card) => card != FateCard.Arceus || s.Ub != null;

        /// <summary>
        /// 그 카드를 내는 자리(rulings A24): 디아루가 = 굴린 뒤 창(AfterRoll — 면 바꾸기·순서 선택·매혹적인 스튜 뒤, 결정 75 B2) · 기라티나 = 굴리기 전 창(BeforeRoll, 내가 공격하는 배틀 턴 — p.19 「Before attacking」) ·
        /// 펄기아·아르세우스 = 내 턴 배틀 밖 맵 행동(Window.None). 포획 굴림에도 굴린 뒤 창이 열려 디아루가로 볼 주사위를 다시 굴린다(사용자 결정 76 ③ F2 나 — BattleFlow.Capture).
        /// </summary>
        public static Window SpotOf(FateCard card) =>
            card == FateCard.Dialga ? Window.AfterRoll : card == FateCard.Giratina ? Window.BeforeRoll : Window.None;

        /// <summary>
        /// 그 효과를 내려고 손에서 뺄 카드(p.19). 1장 효과 = 그 카드 1장(아르세우스는 1장 효과가 없다 — 와일드카드). 3장 효과 = 3장 가운데 추종자 토큰이 늘 1장으로 세지고
        /// (내 추종 신과 같은 카드일 때), 나머지는 그 카드와 아르세우스로 채운다 — 그 카드가 1장은 있어야 한다. 아르세우스의 3장 효과는 아르세우스 3장.
        /// arceus = 함께 낼 아르세우스 장수(플레이어가 고른다 — 사용자 결정 76 ② F1 나, 답 <see cref="WildsOf"/>). −1 = 자동(그 카드부터 내고 모자란 만큼 아르세우스 — 봇·시간 초과의 첫 답).
        /// 못 내면 false(pay는 비운다).
        /// </summary>
        public static bool Payment(GameState s, Ctx ctx, int seat, FateCard card, bool play3, List<int> pay, int arceus = -1)
        {
            pay.Clear();
            var st = s.Seats[seat];
            int own = CardKey(ctx, card), wild = CardKey(ctx, FateCard.Arceus);
            int have = 0;
            foreach (int c in st.FateHand)
                if (c == own) have++;
            if (!play3)
            {
                if (card == FateCard.Arceus || have < 1 || arceus > 0) return false;
                pay.Add(own);
                return true;
            }
            var splits = WildSplits(s, ctx, seat, card);
            if (splits.Count == 0) return false;
            int fromWild = arceus < 0 ? splits[0] : arceus;
            if (!splits.Contains(fromWild)) return false;
            int need = Play3Count - FollowerWorth(s, ctx, seat, card);
            for (int i = 0; i < need - fromWild; i++) pay.Add(own);
            for (int i = 0; i < fromWild; i++) pay.Add(wild);
            return true;
        }

        /// <summary>
        /// 3장 효과에 함께 낼 수 있는 아르세우스 장수(오름차순 — 첫 값 = 자동 나눔 「그 카드부터」). 그 카드가 1장은 있어야 하고
        /// (원작 커뮤니티 확인), 아르세우스 3장 효과는 0뿐. 못 내면 빈 목록.
        /// </summary>
        public static List<int> WildSplits(GameState s, Ctx ctx, int seat, FateCard card)
        {
            var r = new List<int>(Play3Count);
            var st = s.Seats[seat];
            int own = CardKey(ctx, card), wild = CardKey(ctx, FateCard.Arceus);
            int have = 0, wilds = 0;
            foreach (int c in st.FateHand)
            {
                if (c == own) have++;
                else if (c == wild) wilds++;
            }
            int worth = FollowerWorth(s, ctx, seat, card);
            int need = Play3Count - worth;
            if (card == FateCard.Arceus) wilds = 0;
            // 무녀 파랑(토큰 = 2장 몫)이면 남은 1장을 아르세우스 카드로 내도 된다(원작 커뮤니티 확인 [가정 plan §5 #139 ③], rulings A40)
            int most = worth >= 2 ? need : need - 1;
            for (int w = Math.Max(0, need - have); w <= Math.Min(wilds, most); w++) r.Add(w);
            return r;
        }

        /// <summary>
        /// 3장 효과 답에 고른 아르세우스 장수를 싣는 값(답 Values 끝의 음수 −count — 효과가 고른 것(주사위 자리 · 칸)은 0 이상이라 섞이지 않는다).
        /// 자동 나눔(<see cref="WildSplits"/>의 첫 값)인 답에는 싣지 않는다 [가정 — 결정 76 ② 답 꼴].
        /// </summary>
        public static int WildPay(int count) => -count;

        /// <summary>답이 고른 아르세우스 장수(Values 끝의 음수). 없으면 −1(자동).</summary>
        public static int WildsOf(ChoiceAnswer a)
        {
            int n = a.Values.Count;
            return n > 0 && a.Values[n - 1] < 0 ? -a.Values[n - 1] : -1;
        }

        /// <summary>
        /// 추종자 토큰이 그 카드의 3장 효과에서 몇 장 몫인가(p.19 「Your Follower token counts as one of the cards」 = 1). 무녀 파랑(diamond·pearl·platinum_acolyte B 「always counts as 2 of the cards needed for Dialga's Play 3 effect」) = 2 ·
        /// 아르세우스의 추종자(undivided_acolyte B) = 세 신 모두 1 · 아르세우스 카드는 RuleOptions.ArceusFollowerBlessing이면 2(rulings A40). 따르지 않으면 0.
        /// </summary>
        public static int FollowerWorth(GameState s, Ctx ctx, int seat, FateCard card)
        {
            var follower = s.Seats[seat].Follower;
            if (card == FateCard.Arceus) return follower == SinnohGod.Arceus && s.Options.ArceusFollowerBlessing ? 2 : 0;
            var god = card == FateCard.Dialga ? SinnohGod.Dialga : card == FateCard.Palkia ? SinnohGod.Palkia : SinnohGod.Giratina;
            if (!FractureRules.Follows(follower, god)) return 0;
            return follower == god && TrainerRules.Devoted(s, ctx, seat) == god ? 2 : 1;
        }

        /// <summary>
        /// 낼 수 있는 효과를 답으로: PlayFate(Target = FateCard, Arg = 1 또는 3, Values = 고른 것 — <see cref="Choices"/>). spot = 지금 자리(맵 행동이면 Window.None).
        /// 순서 = 카드 데이터 순서, 1장 → 3장, 그 안은 고른 것 순서(G9). 3장 효과는 고른 것마다 자동 나눔 → 아르세우스를 더 쓰는 나눔 순(Values 끝 <see cref="WildPay"/>, 결정 76 ②).
        /// </summary>
        static void Plays(GameState s, Ctx ctx, int seat, Window spot, List<ChoiceAnswer> into)
        {
            if (!CanUse(s, seat)) return;
            var pay = new List<int>(Play3Count);
            var picks = new List<ChoiceAnswer>();
            foreach (var card in Cards)
            {
                if (SpotOf(card) != spot) continue;
                for (int n = 1; n <= Play3Count; n += Play3Count - 1)
                {
                    if (!Ready(ctx, card, n == Play3Count) || !InCampaign(s, card) || !Payment(s, ctx, seat, card, n == Play3Count, pay)) continue;
                    if (n != Play3Count)
                    {
                        Choices(s, ctx, seat, card, n, into);
                        continue;
                    }
                    picks.Clear();
                    Choices(s, ctx, seat, card, n, picks);
                    var splits = WildSplits(s, ctx, seat, card);
                    foreach (var a in picks)
                    {
                        into.Add(a);
                        for (int i = 1; i < splits.Count; i++)
                        {
                            var b = a.Clone();
                            b.Values.Add(WildPay(splits[i]));
                            into.Add(b);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 효과마다 고를 것을 실은 답(rulings A24 「구현 세부」): 디아루가 1장 = 다시 굴릴 주사위 자리 하나(지금 굴림의 자리 순서) · 3장 = 비지 않은 자리 묶음(오름차순, 묶음 비트 순서) ·
        /// 펄기아 = 갈 칸 하나(칸 색인 순서, <see cref="TeleportCells"/>) · 그 밖 = 고를 것 없음.
        /// </summary>
        static void Choices(GameState s, Ctx ctx, int seat, FateCard card, int n, List<ChoiceAnswer> into)
        {
            switch (card)
            {
                case FateCard.Dialga:
                {
                    int dice = s.Battle != null && !EffectRules.DiceLocked(s, ctx) ? s.Battle.Faces.Count : 0; // sacred_sword: 다시 굴릴 주사위가 없다(원작 커뮤니티 확인, rulings G21)
                    if (n == 1)
                        for (int i = 0; i < dice; i++) into.Add(Answer(card, n, i));
                    else
                        for (int mask = 1; mask < 1 << dice; mask++)
                        {
                            var a = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)card, n);
                            for (int i = 0; i < dice; i++)
                                if ((mask >> i & 1) != 0) a.Values.Add(i);
                            into.Add(a);
                        }
                    break;
                }
                case FateCard.Palkia:
                    foreach (int cell in TeleportCells(s, ctx, seat, n == Play3Count)) into.Add(Answer(card, n, cell));
                    break;
                default:
                    into.Add(ChoiceAnswer.Of(AnswerKind.PlayFate, (int)card, n));
                    break;
            }
        }

        static ChoiceAnswer Answer(FateCard card, int n, int value)
        {
            var a = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)card, n);
            a.Values.Add(value);
            return a;
        }

        /// <summary>
        /// 펄기아가 갈 수 있는 칸(rulings A24 ②, p.19): 1장 = 높이와 상관없이 이웃 칸 걸음 1~3, 3장 = 맵의 모든 칸. 지금 칸은 빼고,
        /// 신오의 심장 캠페인에서는 깨어진 세계 칸도 뺀다(울트라 버스트는 넣는다) [가정]. 칸 색인 순서.
        /// </summary>
        public static List<int> TeleportCells(GameState s, Ctx ctx, int seat, bool anywhere)
        {
            var board = ctx.Board;
            int from = s.Seats[seat].Cell;
            var r = new List<int>();
            if (from < 0) return r;
            bool world = s.Campaign == Campaign.UltraBurst;
            var steps = new int[board.Count];
            for (int i = 0; i < steps.Length; i++) steps[i] = anywhere ? 0 : MoveRules.Unreachable;
            if (!anywhere)
            {
                // 넓이 우선, 높이를 보지 않는다(MoveRules.Reachable에서 ElevationAllows만 뺀 것)
                var queue = new Queue<int>();
                steps[from] = 0;
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    if (steps[c] >= TeleportRange) continue;
                    for (int d = 0; d < Board.Directions; d++)
                    {
                        int nb = board.Neighbor(c, d);
                        if (nb == Board.NoCell || steps[nb] != MoveRules.Unreachable) continue;
                        steps[nb] = steps[c] + 1;
                        queue.Enqueue(nb);
                    }
                }
            }
            for (int c = 0; c < board.Count; c++)
                if (c != from && steps[c] != MoveRules.Unreachable && (world || !board[c].DistortionWorld)) r.Add(c);
            return r;
        }

        /// <summary>펄기아 1장의 거리(p.19 「up to 3 tiles away」).</summary>
        public const int TeleportRange = 3;

        /// <summary>답이 고른 것 → 처리기 프레임의 Target(디아루가 3장 = 주사위 자리 비트, 그 밖 = 고른 값 하나, 없으면 −1). 끝의 아르세우스 장수(음수)는 세지 않는다.</summary>
        static int TargetOf(ChoiceAnswer a)
        {
            int n = a.Values.Count - (WildsOf(a) >= 0 ? 1 : 0);
            if (n == 0) return -1;
            if (a.Target != (int)FateCard.Dialga || a.Arg != Play3Count) return a.Values[0];
            int mask = 0;
            for (int i = 0; i < n; i++) mask |= 1 << a.Values[i];
            return mask;
        }

        /// <summary>맵 행동의 운명 답(내 턴 · 배틀 밖).</summary>
        public static void Legal(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into) => Plays(s, ctx, seat, Window.None, into);

        /// <summary>운명 반응 창(Reaction/Fate)의 답 — 「안 씀」 뒤에 붙는다. 기라티나는 내가 공격하는 배틀 턴에만(p.19 「Before attacking」).</summary>
        public static void ReactionLegal(GameState s, Ctx ctx, int seat, Window window, List<ChoiceAnswer> into)
        {
            if (window == Window.None || s.Battle == null) return;
            int k = ChoiceRules.BattleSideOf(s, seat);
            if (k < 0 || s.Battle.Sides[k].Owner != seat) return;
            if (window == Window.BeforeRoll && k != s.Battle.ActiveSide) return;
            Plays(s, ctx, seat, window, into);
        }

        /// <summary>맵 행동 PlayFate 답 적용. 턴은 이어진다 — 부르는 쪽(MapActions)이 MapAction을 다시 연다.</summary>
        public static void ApplyMap(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a) => Play(s, ctx, fx, seat, (FateCard)a.Target, a.Arg == Play3Count, TargetOf(a), WildsOf(a));

        /// <summary>반응 창의 PlayFate 답 적용. 앞 반응 때문에 못 내게 됐으면 효과 없이 false(G6·G3 — 카드도 쓰지 않는다).</summary>
        public static bool ApplyReaction(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            if (a.Kind != AnswerKind.PlayFate) return false;
            return Play(s, ctx, fx, seat, (FateCard)a.Target, a.Arg == Play3Count, TargetOf(a), WildsOf(a));
        }

        /// <summary>
        /// 효과 하나를 낸다: 손에서 버림 더미로(p.19 「Discard all played cards」) → 사용 기록(A11) → 배신자면 쓴 카드를 적어 둠 → FatePlayed → 효과 처리기.
        /// target = 답이 고른 것(<see cref="TargetOf"/> — 처리기 프레임의 Target. 합법 답인지는 Engine이 이미 봤다), arceus = 함께 낼 아르세우스 장수(−1 = 자동, <see cref="Payment"/>).
        /// 낼 수 없으면(한도·카드 모자람·처리기 없음) 아무 일 없이 false.
        /// </summary>
        public static bool Play(GameState s, Ctx ctx, Emitter fx, int seat, FateCard card, bool play3, int target = -1, int arceus = -1)
        {
            var pay = new List<int>(Play3Count);
            if (!CanUse(s, seat) || !Ready(ctx, card, play3) || !InCampaign(s, card) || !Payment(s, ctx, seat, card, play3, pay, arceus)) return false;
            var st = s.Seats[seat];
            int before = st.FateHand.Count;
            int wild = CardKey(ctx, FateCard.Arceus);
            bool usedWild = false;
            foreach (int c in pay)
            {
                st.FateHand.Remove(c);
                s.Discards[Pile].Add(c);
                if (c == wild && card != FateCard.Arceus) usedWild = true;
            }
            MarkUsed(s, fx, seat);
            if (s.Hos != null && s.Hos.Traitor == seat) s.Hos.TraitorFate.AddRange(pay);
            var source = new SourceRef(SourceKind.Fate, CardKey(ctx, card));
            if (fx.On)
                fx.Add(new FatePlayed
                {
                    Seat = seat, Source = source, Card = card, Play3 = play3, CardKeys = pay.ToArray(), WildcardAs = usedWild ? (int)card : -1,
                    FollowerCounted = play3 && FollowerWorth(s, ctx, seat, card) > 0, Before = before, After = st.FateHand.Count,
                });
            // 효과(rulings A24): 프레임은 Stack에 올리지 않고 한 번 부른다 — 고를 것(칸 · 주사위)은 답이 싣고 와서 선택을 사이에 두지 않는다(반응 창 G6이 답 하나로 끝난다)
            int k = ChoiceRules.BattleSideOf(s, seat);
            var frame = new EffectFrame { Source = source, Owner = seat, User = k >= 0 ? s.Battle.Sides[k].Active : -1, Target = target };
            frame.Locals[0] = play3 ? 1 : 0;
            var r = ctx.Effects.Handlers[HandlerName(card, play3)](s, ctx, fx, frame);
            if (r != EffectStepResult.Done) throw new NotSupportedException($"{HandlerName(card, play3)}: 운명 효과는 프레임 한 걸음에 끝난다");
            return true;
        }

        // ── 네 장의 효과(rulings A24) ──

        /// <summary>
        /// 처리기 등록(EffectHandlers.BuiltIn이 부른다): 디아루가 · 펄기아 · 기라티나의 1장·3장. 아르세우스는 1장 효과가 없고, 3장 효과(아르세우스의 축복 +1)는 울트라 버스트 판에서만 낸다(<see cref="InCampaign"/> — 신오의 심장 캠페인에서는 합법 답에 나오지 않는다 [가정]).
        /// </summary>
        public static Dictionary<string, EffectHandler> Register(Dictionary<string, EffectHandler> into)
        {
            foreach (bool play3 in new[] { false, true })
            {
                into[HandlerName(FateCard.Dialga, play3)] = Reroll;
                into[HandlerName(FateCard.Palkia, play3)] = Teleport;
                into[HandlerName(FateCard.Giratina, play3)] = Sacrifice;
            }
            into[HandlerName(FateCard.Arceus, true)] = Blessing;
            return into;
        }

        static bool Play3Of(EffectFrame f) => f.Locals[0] == 1;

        /// <summary>아르세우스 3장(p.19 「This Play 3 effect grants you 1 Blessing of Arceus during the Ultra Burst campaign」): 모둠의 축복 +1(UbBlessing.Gain, rulings A54 ⑦).</summary>
        static EffectStepResult Blessing(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            UbBlessing.Gain(s, fx, f.Owner, f.Source);
            return EffectStepResult.Done;
        }

        /// <summary>
        /// 디아루가(A24 ①): 고른 주사위를 한 번씩 다시 굴린다 — 1장 = Target 자리 하나, 3장 = Target 비트의 자리들(자리 순서로). 면 바꾸기는 다시 하지 않고(결정 75 B2 ①),
        /// 지운 주사위(면 없음)도 굴리고(②), 피로는 그대로다(③). DiceFacesChanged(출처 = 디아루가 카드) 하나.
        /// </summary>
        static EffectStepResult Reroll(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var faces = s.Battle?.Faces;
            int mask = Play3Of(f) ? f.Target : f.Target >= 0 && f.Target < 31 ? 1 << f.Target : 0;
            if (faces == null || mask <= 0 || mask >> faces.Count != 0) throw new InvalidOperationException($"디아루가: 다시 굴릴 주사위 {f.Target}가 지금 굴림 밖이다(Legal 밖)");
            var before = AttackDice.Src(faces);
            var changes = new List<FaceChange>();
            for (int i = 0; i < faces.Count; i++)
            {
                if ((mask >> i & 1) == 0) continue;
                var was = faces[i];
                faces[i] = AttackDice.Roll(s.Rng, 1)[0]; // 한 번 굴림 — 튜토리얼 주사위 대본도 지난다(07 §6-2)
                changes.Add(new FaceChange { Index = i, Before = was, After = faces[i] });
            }
            if (fx.On) fx.Add(new DiceFacesChanged { Source = f.Source, Changes = changes.ToArray(), SrcBefore = before, SrcAfter = AttackDice.Src(faces) });
            return EffectStepResult.Done;
        }

        /// <summary>
        /// 펄기아(A24 ②): Target 칸으로 곧바로(Teleported). 이동력은 그대로고, 닿은 칸에 뒷면 숨겨진 장소가 있으면 걸어서 닿았을 때처럼 묻는다(G11·G12 — 답한 뒤 맵 행동이 다시 열린다).
        /// </summary>
        static EffectStepResult Teleport(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            int seat = f.Owner;
            if (!TeleportCells(s, ctx, seat, Play3Of(f)).Contains(f.Target)) throw new InvalidOperationException($"펄기아: 칸 {f.Target}는 갈 수 없다(Legal 밖)");
            var st = s.Seats[seat];
            int from = st.Cell;
            st.Cell = f.Target;
            st.Moved = true;
            st.Glide = 0;
            st.RoadChain = false;
            TrainerRules.Jumped(st); // 활공 · 길 · 무료 칸은 이어서만(rulings A36 「in a row」 — 공중날기와 같은 꼴)
            if (fx.On) fx.Add(new Teleported { Seat = seat, Source = f.Source, From = from, To = f.Target });
            int spot = MapActions.CanInteract(s, seat) ? MapActions.FaceDownSpotAt(s, f.Target) : -1;
            if (spot >= 0) MapActions.OpenSpot(s, ctx, fx, seat, spot, null);
            return EffectStepResult.Done;
        }

        /// <summary>기라티나 1장의 자해 피해(p.19 「deal 2 damage」).</summary>
        public const int SacrificeDamage = 2;

        /// <summary>
        /// 기라티나(A24 ③): 내 출전 포켓몬에게 바로 피해(1장 = 2, 3장 = 지금 체력 전부)를 주고 이번 기술 공격력을 더한다(1장 = 1, 3장 = ⌊피해/2⌋ — BattleState.FatePower).
        /// 체력 0이 돼도 그 기술은 쓰고(굴림·피해가 그대로 돈다) 기절 확인에서 쓰러진다. HpChanged(출처 = 기라티나 카드).
        /// </summary>
        static EffectStepResult Sacrifice(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            int k = ChoiceRules.BattleSideOf(s, f.Owner);
            if (b == null || k < 0 || f.User < 0) throw new InvalidOperationException("기라티나: 내 배틀의 출전 포켓몬이 없다");
            var pk = s.Pokemon[f.User];
            int max = PokemonRules.MaxHp(ctx, pk);
            int hp = PokemonRules.Hp(ctx, pk);
            int damage = Play3Of(f) ? hp : Math.Min(SacrificeDamage, hp);
            pk.Damage = Math.Min(max, pk.Damage + damage);
            b.FatePower = Play3Of(f) ? damage / 2 : 1;
            b.Sides[k].LastHit = f.Source;
            b.Sides[k].LastHitBy = -1;
            if (fx.On) fx.Add(new HpChanged { PokemonId = f.User, Source = f.Source, Before = hp, After = PokemonRules.Hp(ctx, pk), Max = max });
            return EffectStepResult.Done;
        }

        /// <summary>굴림 공격력 보정(BattleRules.RollPower): 이번 배틀 턴에 기라티나를 냈으면 공격하는 포켓몬의 기술에 그만큼(출처 = 기라티나 카드). 최대 9는 RollPower가 자른다.</summary>
        public static void PowerMods(GameState s, Ctx ctx, int attacker, List<Mod> into)
        {
            var b = s.Battle;
            if (b == null || b.FatePower == 0 || b.ActiveSide < 0 || b.Sides[b.ActiveSide].Active != attacker) return;
            into.Add(new Mod { Source = new SourceRef(SourceKind.Fate, CardKey(ctx, FateCard.Giratina)), Delta = b.FatePower });
        }

        // ── 배신자 연전(S16_3, p.22) ──

        /// <summary>
        /// 배신자가 한 명을 이길 때마다: 연전 동안 쓴 운명 카드가 버림 더미에서 손으로 돌아온다(p.22 「return any Change of Fate cards used to your hand」, 04 96행).
        /// 「쓴 카드」 = 배신자가 된 뒤 낸 카드 [가정] — 버림 더미에 남아 있는 것만 돌아온다. 돌아온 것이 있으면 FateDealt(Source = Rule:TraitorReturn).
        /// </summary>
        public static void ReturnUsed(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var used = s.Hos.TraitorFate;
            var hand = s.Seats[seat].FateHand;
            int before = hand.Count;
            var back = new List<int>(used.Count);
            foreach (int c in used)
            {
                int at = s.Discards[Pile].LastIndexOf(c);
                if (at < 0) continue;
                s.Discards[Pile].RemoveAt(at);
                hand.Add(c);
                back.Add(c);
            }
            used.Clear();
            if (back.Count == 0) return;
            if (fx.On)
                fx.Add(new FateDealt
                {
                    Seat = seat, Source = SourceRef.Rule(RuleSource.TraitorReturn), StarCount = -1,
                    PerSeat = new[] { new FateDeal { Seat = seat, Before = before, After = hand.Count, CardKeys = back.ToArray() } },
                });
            Overflow(s, ctx, fx);
        }
    }
}
