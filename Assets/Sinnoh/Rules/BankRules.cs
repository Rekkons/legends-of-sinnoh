using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 은행상회 거래(rulings A4, S11, 룰북 p.3 「Exchange your Apricorns with the Ginkgo Merchants according to one or both of the current Ginkgo Trades being offered.
    /// Shuffle the Ginkgo Trade deck afterwards.」, M5 「제작·은행·진화」). 마을·앞면 야영지·울트라메가로폴리스(<see cref="MapActions.VillagePlace"/>)에서 턴당 1번.
    /// 덱(9장)은 앞면이라 맨 위 카드 1장이 보인다(보기 사본에서도 — Engine.ViewFor). 카드의 두 줄(<see cref="CardDef.Trades"/>) 가운데 한 줄만 하거나 두 줄을 각 1번씩 한다.
    /// MapAction 답 Ginkgo(Target = <see cref="Top"/>·<see cref="Bottom"/>·<see cref="Both"/>). 거래를 마치면 덱을 섞는다.
    /// 두 줄은 어느 순서로든 되면 된다 [가정] — 한 줄에서 받은 규토리로 다른 줄을 낼 수 있다(결과는 순서와 상관없이 같다).
    /// </summary>
    public static class BankRules
    {
        public const int Top = 0, Bottom = 1, Both = 2;

        /// <summary>지금 거래 카드(덱 맨 위, cards 색인). 없으면 −1.</summary>
        public static int Card(GameState s)
        {
            var pile = s.Decks[(int)DeckId.GinkgoTrade];
            return pile.Count > 0 ? pile[pile.Count - 1] : -1;
        }

        /// <summary>두 줄을 다 할 때 먼저 할 줄(0·1). 어느 순서로도 안 되면 −1.</summary>
        static int FirstOfBoth(SeatState st, IReadOnlyList<CardTrade> t)
        {
            for (int first = 0; first < 2; first++)
            {
                if (!CraftRules.CanPay(st, t[first].Give)) continue;
                // 첫 줄을 한 뒤의 가방으로 둘째 줄을 본다
                var after = new SeatState();
                after.CopyFrom(st);
                var none = new List<ResChange>();
                CraftRules.MoveApricorns(after, 0, t[first].Give, -1, none);
                CraftRules.MoveApricorns(after, 0, t[first].Get, +1, none);
                if (CraftRules.CanPay(after, t[1 - first].Give)) return first;
            }
            return -1;
        }

        /// <summary>MapAction의 은행상회 답(위 줄 → 아래 줄 → 두 줄). 부르는 쪽이 장소(VillagePlace)를 본다.</summary>
        public static void Legal(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into)
        {
            if (MapActions.Used(s, seat, UsageKind.Ginkgo)) return;
            int card = Card(s);
            if (card < 0) return;
            var trades = ctx.Decks.Cards[card].Trades;
            if (trades == null) return;
            var st = s.Seats[seat];
            if (CraftRules.CanPay(st, trades[Top].Give)) into.Add(ChoiceAnswer.Of(AnswerKind.Ginkgo, Top));
            if (CraftRules.CanPay(st, trades[Bottom].Give)) into.Add(ChoiceAnswer.Of(AnswerKind.Ginkgo, Bottom));
            if (FirstOfBoth(st, trades) >= 0) into.Add(ChoiceAnswer.Of(AnswerKind.Ginkgo, Both));
        }

        /// <summary>거래 답 적용: 줄마다 GinkgoTraded → 덱 섞기(PileShuffled) → 턴당 1번 기록. 턴은 이어진다.</summary>
        public static void Apply(GameState s, Ctx ctx, Emitter fx, int seat, int rows)
        {
            var st = s.Seats[seat];
            int card = Card(s);
            var trades = ctx.Decks.Cards[card].Trades;
            if (rows == Both)
            {
                int first = FirstOfBoth(st, trades);
                Trade(st, fx, seat, card, first, trades[first]);
                Trade(st, fx, seat, card, 1 - first, trades[1 - first]);
                TrainerRules.Monopoly(s, ctx, fx, seat, trades[first].Give); // ginkgo_merchant B: 남이 버린 규토리를 얻는다(rulings A38)
                TrainerRules.Monopoly(s, ctx, fx, seat, trades[1 - first].Give);
            }
            else
            {
                Trade(st, fx, seat, card, rows, trades[rows]);
                TrainerRules.Monopoly(s, ctx, fx, seat, trades[rows].Give);
            }
            Piles.Shuffle(s.Decks[(int)DeckId.GinkgoTrade], s.Rng);
            if (fx.On) fx.Add(new PileShuffled { Pile = DeckId.GinkgoTrade });
            MapActions.Mark(s, fx, seat, UsageKind.Ginkgo);
            MapActions.Continue(s, ctx, fx, seat);
        }

        static void Trade(SeatState st, Emitter fx, int seat, int card, int row, CardTrade t)
        {
            var changes = new List<ResChange>();
            CraftRules.MoveApricorns(st, seat, t.Give, -1, changes);
            CraftRules.MoveApricorns(st, seat, t.Get, +1, changes);
            if (fx.On) fx.Add(new GinkgoTraded { Seat = seat, CardKey = card, Row = row, Changes = changes.ToArray() });
        }
    }
}
