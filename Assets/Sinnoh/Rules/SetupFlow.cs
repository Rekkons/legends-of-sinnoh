using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 셋업(01 §3-5, A20-1, 결정 25, rulings G12). 차례:
    /// ① Engine.Start: 트레이너 카드 3장씩(옵션 TrainerCards) → 처음 고르는 좌석(엔진 Rng)의 고향 마을 고르기.
    /// ② 마을 고르기는 반시계(좌석 번호가 줄어드는 쪽). 마지막에 고른 좌석이 라운드 첫 좌석(S2_5).
    /// ③ 스타터(마을 카드의 두 마리) + 트레이너 카드 1장을 모든 좌석이 비공개 동시 묶음으로 고르고 한꺼번에 공개.
    /// ④ 게임 시작(자동): 남은 스타터 → 약한 덱에 섞음(R15), 숨겨진 장소·유물 배치(R3, B3 고침).
    /// ⑤ 신오의 심장: 서막 카드 공개 → 모든 고향 마을에 조무래기(S16_2) → 1막 카드 공개 + 빨강 규토리 3개(p.21). 울트라 버스트는 ④의 숨겨진 장소 앞에 UB 9 · 네크로즈마 · 규토리 3색(UltraBurstSetup, M6)이고 막이 없다.
    /// ⑥ 첫 라운드(TurnFlow).
    /// 행동 경계(01 §3-6 ①): ①④⑤의 각 단계는 자동 Seq(Setup·ActReveal·Prologue), ②③은 답의 Seq다.
    /// </summary>
    public static class SetupFlow
    {
        /// <summary>트레이너마다 나눠 주는 트레이너 카드(A20-1).</summary>
        public const int TrainerCardOffer = 3;

        /// <summary>1막 공개 때 좌석마다 얻는 빨강 규토리(p.21 「At the start of Act I, you gain 3 Red Apricorns.」).</summary>
        public const int ActOneRedApricorns = 3;

        /// <summary>1막 카드 키(cards.json). 1막 해방 보상 이동력 +2의 출처(TurnFlow).</summary>
        public const string ActOneKey = "act.journey";

        /// <summary>셋업 시작(Engine.Start). 새 게임(셋업 단계, 열린 선택 없음)에서만 부른다.</summary>
        public static void Begin(GameState s, Ctx ctx, Emitter fx)
        {
            fx.Begin(SeatId.System, ActionKind.Setup, AutoKind.Auto);
            if (s.Options.TrainerCards)
            {
                var deck = s.Decks[(int)DeckId.TrainerCard];
                for (int i = 0; i < s.SeatCount; i++)
                {
                    var offer = s.Seats[i].TrainerCardOffer;
                    for (int k = 0; k < TrainerCardOffer; k++)
                    {
                        offer.Add(deck[deck.Count - 1]);
                        deck.RemoveAt(deck.Count - 1);
                    }
                    if (fx.On) fx.Add(new TrainerCardsDealt { Seat = i, Count = TrainerCardOffer, CardKeys = offer.ToArray() });
                }
            }
            OpenVillagePick(s, ctx, fx, s.Rng.Next(s.SeatCount));
        }

        static void OpenVillagePick(GameState s, Ctx ctx, Emitter fx, int seat) =>
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.VillagePick, Seat = seat });

        /// <summary>고를 수 있는 고향 마을인가: 마을이고, 울트라메가로폴리스(깨어진 세계의 마을)는 울트라 버스트에서만(G12).</summary>
        public static bool IsHomeVillageOption(GameState s, Ctx ctx, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            return f.Kind == FixtureKind.Village && (s.Campaign == Campaign.UltraBurst || !ctx.Board[f.Cell].DistortionWorld);
        }

        /// <summary>마을 고정물 → 그 고향 마을 카드(cards 색인, 좌석 색). 키: village.x → home_village.x.</summary>
        public static int HomeCardOf(Ctx ctx, int fixture)
        {
            string key = ctx.Fixtures.Fixtures[fixture].Key;
            const string prefix = "village.";
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException($"{key}: 마을이 아니다", nameof(fixture));
            return ctx.Decks.CardIds.IndexOf("home_" + key);
        }

        /// <summary>마을 고르기 답: 말을 마을에 놓고 다음 좌석(반시계)이 고른다. 모두 골랐으면 스타터·트레이너 카드 묶음을 연다.</summary>
        public static void ApplyVillage(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var seat = s.Seats[c.Seat];
            int f = a.Target;
            int cell = ctx.Fixtures.Fixtures[f].Cell;
            seat.HomeVillage = f;
            seat.Cell = cell;
            if (fx.On) fx.Add(new VillagePicked { Seat = c.Seat, VillageKey = f, Cell = cell, SeatColor = HomeCardOf(ctx, f) });
            int next = (c.Seat - 1 + s.SeatCount) % s.SeatCount;
            if (s.Seats[next].HomeVillage < 0)
            {
                OpenVillagePick(s, ctx, fx, next);
                return;
            }
            for (int i = 0; i < s.SeatCount; i++)
                if (s.Seats[i].HomeVillage < 0) return; // 장면(시험)이 일부만 골랐다
            s.RoundStartSeat = c.Seat;
            OpenPicks(s, ctx, fx);
        }

        /// <summary>스타터 + 트레이너 카드: 좌석마다 둘, 모든 좌석이 한 비공개 묶음(결정 25, 04 §8-8 1·3행).</summary>
        static void OpenPicks(GameState s, Ctx ctx, Emitter fx)
        {
            var group = new List<PendingChoice>();
            for (int i = 0; i < s.SeatCount; i++)
            {
                var starter = new PendingChoice { Kind = ChoiceKind.StarterPick, Seat = i, Secret = true };
                starter.Options.AddRange(ctx.Fixtures.Fixtures[s.Seats[i].HomeVillage].Starters);
                group.Add(starter);
                if (s.Options.TrainerCards) group.Add(new PendingChoice { Kind = ChoiceKind.TrainerCardPick, Seat = i, Secret = true });
            }
            ChoiceRules.OpenGroup(s, ctx, fx, group);
        }

        /// <summary>셋업 고르기 묶음인가(스타터·트레이너 카드). Engine.Answer가 묶음 적용을 여기에 넘긴다.</summary>
        public static bool IsPickGroup(List<PendingChoice> members)
        {
            foreach (var m in members)
                if (m.Kind != ChoiceKind.StarterPick && m.Kind != ChoiceKind.TrainerCardPick) return false;
            return members.Count > 0;
        }

        /// <summary>
        /// 묶음을 다 답한 뒤(SecretRevealed 다음): 스타터를 파티에, 트레이너 카드를 앞면으로, 고르지 않은 카드는 덱에 섞는다(A20-1).
        /// 셋업 중이고 라운드 첫 좌석이 정해졌으면 게임 시작으로 잇는다.
        /// </summary>
        public static void ApplyPicks(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> members)
        {
            var reveals = new List<SeatCard>();
            int returned = 0;
            var tcDeck = s.Decks[(int)DeckId.TrainerCard];
            foreach (var m in members)
            {
                var seat = s.Seats[m.Seat];
                int pick = m.Answer.Target;
                if (m.Kind == ChoiceKind.StarterPick)
                {
                    var starters = s.Decks[(int)DeckId.Starter];
                    int at = starters.LastIndexOf(pick);
                    if (at >= 0) starters.RemoveAt(at);
                    // 고른 스타터는 약한 덱에서 온 것으로 친다(G12) — 남은 스타터가 약한 덱으로 가기 때문이다
                    int id = PokemonRules.Spawn(s, pick, m.Seat, (int)DeckId.Weak);
                    int slot = PokemonRules.AddToParty(seat, id);
                    if (slot < 0) throw new InvalidOperationException($"좌석 {m.Seat}: 스타터를 넣을 파티 칸이 없다");
                    if (fx.On) fx.Add(new StarterPicked { Seat = m.Seat, Species = pick, PokemonId = id, PartySlot = slot });
                }
                else
                {
                    seat.TrainerCard = pick;
                    seat.TrainerCardTier = 1;
                    bool kept = false;
                    foreach (int card in seat.TrainerCardOffer)
                    {
                        if (card == pick && !kept)
                        {
                            kept = true;
                            continue;
                        }
                        tcDeck.Add(card);
                        returned++;
                    }
                    seat.TrainerCardOffer.Clear();
                    reveals.Add(new SeatCard { Seat = m.Seat, CardKey = pick });
                }
            }
            if (reveals.Count > 0)
            {
                Piles.Shuffle(tcDeck, s.Rng);
                if (fx.On)
                {
                    fx.Add(new TrainerCardsRevealed { Picks = reveals.ToArray(), ReturnedCount = returned });
                    fx.Add(new CardsReturned { Returns = new[] { new PileCount { Pile = DeckId.TrainerCard, Count = returned } } });
                    fx.Add(new PileShuffled { Pile = DeckId.TrainerCard });
                }
            }
            if (s.Phase == Phase.Setup && s.RoundStartSeat >= 0) StartGame(s, ctx, fx);
        }

        /// <summary>게임 시작 버튼(원작 setup_game): 남은 스타터 → 약한 덱(R15), 숨겨진 장소·유물 배치(R3) → 서막·1막 → 첫 라운드.</summary>
        static void StartGame(GameState s, Ctx ctx, Emitter fx)
        {
            fx.Begin(SeatId.System, ActionKind.Setup, AutoKind.Auto);
            var starters = s.Decks[(int)DeckId.Starter];
            var weak = s.Decks[(int)DeckId.Weak];
            int moved = starters.Count;
            weak.AddRange(starters);
            starters.Clear();
            Piles.Shuffle(weak, s.Rng);
            if (fx.On)
            {
                fx.Add(new CardsReturned { Returns = new[] { new PileCount { Pile = DeckId.Weak, Count = moved } } });
                fx.Add(new PileShuffled { Pile = DeckId.Weak });
            }
            if (s.Ub != null) UltraBurstSetup.Begin(s, ctx, fx); // 울트라 버스트: UB 9 · 네크로즈마 · 규토리 3색 — 숨겨진 장소보다 먼저(Lua setup_game, rulings A50)
            var placed = PlaceHiddenSpots(s, ctx);
            SortHiddenByCell(s);
            if (fx.On) fx.Add(new TokensPlaced { Kind = TokenKind.HiddenSpot, Cells = placed.ToArray(), Villages = new int[0] });

            if (s.Hos != null)
            {
                RevealAct(s, ctx, fx, -1);
                Prologue(s, ctx, fx);
                RevealAct(s, ctx, fx, -1);
            }
            TurnFlow.StartGame(s, ctx, fx);
        }

        /// <summary>
        /// 숨겨진 장소·유물 배치(R3 1:1, B3 고침, rulings G12). 원작 순서대로 유물 토큰(우두머리·유물·천상의 유물)을 먼저, 숨겨진 장소를 나중에 놓는다.
        /// 후보 = 깨어진 세계가 아닌 야생 칸 중 고정물·먼저 놓은 토큰이 없는 칸(토큰에 지형·기후 태그가 없어 모든 야생 칸). 토큰마다 후보를 섞어
        /// 처음 맞는 칸에 놓고, 이미 토큰을 놓은 지역은 건너뛴다. 한 바퀴 다 봐도 없을 때만 지역 기록을 비우고 다시 본다(B3: Lua는 맞지 않는 칸 하나에서 비웠다).
        /// 돌려주는 칸 목록은 칸 색인 순서다 — 놓은 순서(= 종류 묶음)를 알리면 뒷면 정체가 샌다(B2). 토큰 목록 순서는 셋업이 이어서 SortHiddenByCell로 지운다.
        /// </summary>
        public static List<int> PlaceHiddenSpots(GameState s, Ctx ctx)
        {
            var board = ctx.Board;
            var blocked = new bool[board.Count];
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind != FixtureKind.Taxi) blocked[f.Cell] = true;
            foreach (var t in s.Tokens)
                if (t.Cell >= 0) blocked[t.Cell] = true;
            var candidates = new List<int>();
            for (int c = 0; c < board.Count; c++)
                if (board[c].Wild && !board[c].DistortionWorld && !blocked[c]) candidates.Add(c);

            var regions = new List<string>();
            var placed = new List<int>();
            var order = new List<int>(candidates.Count);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var t in s.Tokens)
                {
                    if (t.Kind != TokenKind.HiddenSpot || t.Cell >= 0 || t.Destroyed) continue;
                    bool artifact = ctx.Items.Items[t.Def].Kind != ItemKind.HiddenSpot;
                    if (artifact != (pass == 0)) continue;
                    order.Clear();
                    order.AddRange(candidates);
                    Piles.Shuffle(order, s.Rng);
                    int cell = FirstSafe(board, order, blocked, regions);
                    if (cell < 0)
                    {
                        regions.Clear();
                        cell = FirstSafe(board, order, blocked, regions);
                    }
                    if (cell < 0) continue;
                    t.Cell = cell;
                    blocked[cell] = true;
                    if (board[cell].Region != null) regions.Add(board[cell].Region);
                    placed.Add(cell);
                }
            }
            placed.Sort();
            return placed;
        }

        /// <summary>
        /// 뒷면 숨겨진 장소 토큰을 토큰 목록 안의 제자리들에서 칸 색인 순으로 다시 늘어놓는다(rulings G12). 보기(ViewFor)는 Def만 가리고 토큰 순서·칸은 그대로 보내므로,
        /// 놓은 순서(유물 묶음 먼저, 지역이 차면 마지막 숨겨진 장소가 겹침)가 목록 순서와 이어지면 뒷면 정체가 샌다(B2와 같은 종류). 보드 밖(−1)은 섞인 처음 순서대로 맨 뒤.
        /// </summary>
        public static void SortHiddenByCell(GameState s)
        {
            var slots = new List<int>();
            var tokens = new List<TokenState>();
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.FaceUp) continue;
                slots.Add(i);
                tokens.Add(t);
            }
            // 안정 정렬(삽입): 칸이 같은 것은 보드 밖(−1)뿐이고 그 순서는 처음 섞은 순서다
            for (int i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i];
                int j = i - 1;
                while (j >= 0 && CellKey(tokens[j]) > CellKey(t))
                {
                    tokens[j + 1] = tokens[j];
                    j--;
                }
                tokens[j + 1] = t;
            }
            for (int i = 0; i < slots.Count; i++) s.Tokens[slots[i]] = tokens[i];
        }

        static int CellKey(TokenState t) => t.Cell >= 0 ? t.Cell : int.MaxValue;

        internal static int FirstSafe(Board board, List<int> order, bool[] blocked, List<string> regions)
        {
            foreach (int c in order)
            {
                if (blocked[c]) continue;
                string r = board[c].Region;
                if (r != null && regions.Contains(r)) continue;
                return c;
            }
            return -1;
        }

        /// <summary>
        /// 막 카드 공개(01 §8-1): 막 덱 맨 위를 공개해 막 버림 더미(공개)에 둔다. firstSeat = 처음 달성해 공개한 좌석(서막·1막은 −1).
        /// 1막이면 모든 좌석이 빨강 규토리 3개(p.21).
        /// </summary>
        public static void RevealAct(GameState s, Ctx ctx, Emitter fx, int firstSeat)
        {
            var deck = s.Decks[(int)DeckId.Act];
            var shown = s.Discards[(int)DeckId.Act];
            int prev = shown.Count > 0 ? shown[shown.Count - 1] : -1;
            int card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            shown.Add(card);
            s.Hos.Act++;
            fx.Begin(firstSeat >= 0 ? firstSeat : SeatId.System, ActionKind.ActReveal, AutoKind.Auto);
            if (fx.On) fx.Add(new ActRevealed { ActKey = card, FirstSeat = firstSeat, PrevActKey = prev });
            if (s.Hos.Act == 1)
            {
                var changes = new ResChange[s.SeatCount];
                for (int i = 0; i < s.SeatCount; i++)
                {
                    var ap = s.Seats[i].Apricorns;
                    int before = ap[(int)ApricornColor.Red];
                    ap[(int)ApricornColor.Red] += ActOneRedApricorns;
                    changes[i] = new ResChange { Seat = i, Res = ResKind.Apricorn, Key = (int)ApricornColor.Red, Before = before, After = before + ActOneRedApricorns };
                }
                if (fx.On) fx.Add(new ApricornsGained { Seat = SeatId.System, Source = new SourceRef(SourceKind.Act, card), Changes = changes });
            }
        }

        /// <summary>서막(S16_2): 모든 고향 마을에 조무래기 토큰(앞면). 조무래기가 있는 마을은 공중날기가 막힌다(A5).</summary>
        static void Prologue(GameState s, Ctx ctx, Emitter fx)
        {
            fx.Begin(SeatId.System, ActionKind.Prologue, AutoKind.Auto);
            var villages = new List<int>();
            var cells = new List<int>();
            for (int i = 0; i < s.SeatCount; i++)
            {
                int f = s.Seats[i].HomeVillage;
                if (villages.Contains(f)) continue;
                int cell = ctx.Fixtures.Fixtures[f].Cell;
                s.Tokens.Add(new TokenState { Kind = TokenKind.Grunt, Def = f, Cell = cell, FaceUp = true });
                villages.Add(f);
                cells.Add(cell);
            }
            if (fx.On)
            {
                fx.Add(new TokensPlaced { Kind = TokenKind.Grunt, Cells = cells.ToArray(), Villages = villages.ToArray() });
                fx.Add(new FlyBlocked { Villages = villages.ToArray() });
            }
        }

        /// <summary>그 칸에 조무래기 토큰이 있나(A5 공중날기, S3_3).</summary>
        public static bool GruntAt(GameState s, int cell)
        {
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Grunt && t.Cell == cell && !t.Destroyed) return true;
            return false;
        }
    }
}
