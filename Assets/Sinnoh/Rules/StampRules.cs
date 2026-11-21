using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 여행 스탬프(룰북 p.15 Journey Stamps, rulings A30, M5 「스탬프」). 칸 7개 = SeatState.Stamps(색인 = items.json stamp.&lt;n&gt; − 1, 윗줄 왼쪽부터) — 「Stamps can be claimed in any order and
    /// are immediately awarded upon their condition being met」 · 「Once a stamp is received, it is kept for the remainder of the game」.
    /// 받으면 모은 수(1~7)의 보상을 바로 받는다(「Stamp rewards may be claimed immediately after reaching each milestone」 — 더 낮은 보상 고르기는 v1.4a에 없다):
    /// 1 빨강·파랑·노랑 규토리 1 · 2 약한 포켓몬 카드 5장 · 3 분홍·초록·하양 1 · 4 보통 5장 · 5 검정 3 · 6 강한 5장 · 7 수호신의 서.
    /// 카드 5장은 그 덱에서 무작위로 뽑아 트레이너 곁에 둔다 — 기술 풀(StampMoves = 그 카드의 시그니처, 정비로 가르친다)에 넣고 카드는 조우 풀에서 빠진다(SeatState.StampCards).
    /// 조건: 이벤트 넷은 고리(포획 실패 · 포획 · 배틀 끝 · 무리 배틀 승리), 상태 셋(규토리 일곱 색 · 지닌물건 셋 · 운명 셋)은 명령 끝마다 훑는다(<see cref="AfterCommand"/>).
    /// 수호신의 서(보상 7)를 지닌 완전히 진화한 스타팅 포켓몬은 배틀 밖에서 수호신 폼(상태 2)으로 바꿀 수 있다 — 유물 폼과 같은 길(FormRules, 정비 ChangeForm).
    /// </summary>
    public static class StampRules
    {
        /// <summary>스탬프 칸(SeatState.Stamps 색인).</summary>
        public const int FailedCaptures = 0, TrainerWin = 1, AllApricorns = 2, LegendaryCaught = 3, HeldItems = 4, SwarmWin = 5, FateCards = 6;

        /// <summary>스탬프 1: 이어서 실패할 포획 수(「2 turns in a row」).</summary>
        public const int FailsInARow = 2;
        /// <summary>스탬프 5: 파티가 지닌 물건 수(「3 or more」).</summary>
        public const int HeldNeeded = 3;
        /// <summary>스탬프 7: 서로 다른 운명 카드 수(「3 different」).</summary>
        public const int FateKindsNeeded = 3;
        /// <summary>보상 2·4·6: 뽑는 카드 수(「Randomly draw 5」).</summary>
        public const int CardsDrawn = 5;

        public const string ScrollOfNobility = "item.scroll_of_nobility";

        /// <summary>노블 폼(p.6 「Noble Form (State 2)」).</summary>
        public const int NobleState = 2;

        /// <summary>받은 스탬프 수.</summary>
        public static int Count(SeatState st)
        {
            int n = 0;
            foreach (bool b in st.Stamps)
                if (b) n++;
            return n;
        }

        /// <summary>스탬프 칸의 아이템 색인(items.json stamp.&lt;n&gt; — 이벤트의 StampKey · SourceRef Stamp의 Id).</summary>
        public static int ItemOf(Ctx ctx, int stamp) => ctx.Items.Ids.IndexOf("stamp." + (stamp + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // ── 고리: 이벤트 조건 ──

        /// <summary>
        /// 스탬프 1(「You fail to capture a wild Pokémon 2 turns in a row during a battle」): 포획에 실패했다(BattleFlow.CaptureResolve). 그 쪽의 이어진 실패 수를 올리고 2번째면 받는다.
        /// 실제로 던져 실패해야 한다(원작 커뮤니티 확인) — 기술을 쓰면 끊긴다(<see cref="Acted"/>).
        /// </summary>
        public static void CaptureFailed(GameState s, Ctx ctx, Emitter fx, BattleState b, int side)
        {
            var sd = b.Sides[side];
            sd.FailedCaptures++;
            if (sd.Owner >= 0 && sd.FailedCaptures >= FailsInARow) Gain(s, ctx, fx, sd.Owner, FailedCaptures);
        }

        /// <summary>그 쪽이 배틀 턴에 볼이 아닌 행동(기술)을 했다 — 이어진 포획 실패가 끊긴다.</summary>
        public static void Acted(BattleState b, int side) => b.Sides[side].FailedCaptures = 0;

        /// <summary>
        /// 스탬프 4(「You have captured a Legendary Pokémon」): 볼을 던져 잡았다(BattleFlow.CaptureResolve — 다른 길로 잡는 묶음은 이것을 부른다). 전설 카드이거나 우두머리 토큰이면 받는다
        /// (우두머리 = 전설 — 원작 커뮤니티 확인). 던지지 않고 얻은 전설(무녀 빨강 · 거래로 받은 수호신 폼)은 아니다
        /// (원작 커뮤니티 확인).
        /// </summary>
        public static void OnCaptured(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            var p = s.Pokemon[pokemon];
            if (seat >= 0 && (p.Alpha || ctx.Cards.Pokemon[p.Def].Legendary)) Gain(s, ctx, fx, seat, LegendaryCaught);
        }

        /// <summary>
        /// 스탬프 2(「You win a battle against another trainer or an Ultra Beast」): 배틀이 끝났다(BattleFlow.Conclude, winner = 이긴 쪽 −1 = 없음). 트레이너전·배신자전 승리(기권승 포함) ·
        /// 울트라 비스트·네크로즈마 격파 · 혼자 하는 판이면 갤럭시단 격파(원작 커뮤니티 확인 — 여럿이면 조무래기는 트레이너가 아니다).
        /// 거래는 v1.4a 문구에서 빠졌다(v1.4 「or trade with another trainer」).
        /// </summary>
        public static void BattleEnded(GameState s, Ctx ctx, Emitter fx, BattleState b, BattleResult result, int winner)
        {
            if (winner < 0 || b.Sides[winner].Owner < 0) return;
            bool won = result == BattleResult.Win || result == BattleResult.Forfeited;
            switch (b.Kind)
            {
                case BattleKind.Trainer:
                case BattleKind.Traitor:
                    break;
                case BattleKind.UltraBeast: // 네크로즈마의 페이즈 격파는 「쓰러뜨림」이 아니다(원작 커뮤니티 확인, rulings A54 ②) — 3페이즈 격파는 게임 끝
                    won &= result == BattleResult.Win;
                    break;
                case BattleKind.Galactic:
                    won &= result == BattleResult.Win && s.SeatCount == 1;
                    break;
                default:
                    return;
            }
            if (won) Gain(s, ctx, fx, b.Sides[winner].Owner, TrainerWin);
        }

        /// <summary>
        /// 스탬프 6(「You have been victorious at a Swarm Battle」): 무리 배틀 셋을 모두 이겼다(쓰러뜨림·포획, rulings A18) — BattleFlow.Conclude가 JP 7과 같은 조건에서 BattleEnded 뒤에 부른다.
        /// </summary>
        public static void OnSwarmWon(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (seat >= 0) Gain(s, ctx, fx, seat, SwarmWin);
        }

        // ── 상태 조건(명령 끝마다) ──

        /// <summary>
        /// 명령 끝(Engine.SubmitAnswer · MaintenanceRules.Submit): 상태 조건 셋을 모든 좌석에 훑는다. 받은 것이 있으면 보상으로 바뀐 합법 답의 열린 선택을 다시 연다(ChoiceReissued).
        /// </summary>
        public static void AfterCommand(GameState s, Ctx ctx, Emitter fx)
        {
            if (!Due(s, ctx)) return;
            var before = ChoiceRules.Fingerprints(s, ctx);
            Sweep(s, ctx, fx);
            ChoiceRules.ReissueChanged(s, ctx, fx, before);
        }

        /// <summary>상태 조건을 채웠는데 아직 받지 않은 좌석이 있다.</summary>
        public static bool Due(GameState s, Ctx ctx)
        {
            if (s.Phase == Phase.Setup || s.Phase == Phase.GameOver) return false;
            for (int seat = 0; seat < s.SeatCount; seat++)
                if (DueAt(s, ctx, seat) >= 0) return true;
            return false;
        }

        /// <summary>
        /// 모든 좌석의 상태 조건을 받는다(정비는 ReissueChanged를 스스로 한다). 좌석은 G6 맵 창 순서(차례 좌석부터 시계 방향) — 한 명령에 둘이 같이 얻으면
        /// 그 차례로 받고 interviewers B도 그 차례로 묻는다(M9 실행 감사 K1).
        /// </summary>
        public static void Sweep(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Phase == Phase.Setup || s.Phase == Phase.GameOver) return;
            int first = s.ActiveSeat >= 0 && s.ActiveSeat < s.SeatCount ? s.ActiveSeat : 0;
            for (int i = 0; i < s.SeatCount; i++) SweepSeat(s, ctx, fx, (first + i) % s.SeatCount);
        }

        static void SweepSeat(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            for (int stamp = DueAt(s, ctx, seat); stamp >= 0; stamp = DueAt(s, ctx, seat)) Gain(s, ctx, fx, seat, stamp);
        }

        /// <summary>그 좌석이 채웠는데 아직 받지 않은 상태 조건 스탬프(칸 순서로 첫 것). 없으면 −1.</summary>
        static int DueAt(GameState s, Ctx ctx, int seat)
        {
            var st = s.Seats[seat];
            if (!st.Stamps[AllApricorns] && HasAllApricorns(st)) return AllApricorns;
            if (!st.Stamps[HeldItems] && HeldCount(s, st) >= HeldNeeded) return HeldItems;
            if (!st.Stamps[FateCards] && FateKinds(ctx, st) >= FateKindsNeeded) return FateCards;
            return -1;
        }

        /// <summary>스탬프 3(「You have at least 1 of every Apricorn type in your bag」).</summary>
        public static bool HasAllApricorns(SeatState st)
        {
            foreach (int n in st.Apricorns)
                if (n < 1) return false;
            return true;
        }

        /// <summary>
        /// 스탬프 5(「You have 3 or more held items equipped in your party」): 파티 포켓몬이 지닌 물건 수 — 유물·수호신의 서도 지닌물건이다(Lev 👍 원작 커뮤니티 확인).
        /// 우두머리 토큰은 지닌물건이 아니다(지닌물건 칸을 막을 뿐 — PokemonState.Alpha).
        /// </summary>
        public static int HeldCount(GameState s, SeatState st)
        {
            int n = 0;
            foreach (int p in st.Party)
            {
                if (p >= 0 && s.Pokemon[p].HeldItem >= 0) n++;
                if (p >= 0 && s.Pokemon[p].HeldItem2 >= 0) n++; // backpacker R의 둘째 칸도 「held items equipped」 하나(rulings A39)
            }
            return n;
        }

        /// <summary>
        /// 스탬프 7(「You have 3 different Change of Fate cards in your hand」): 손패의 서로 다른 카드 종류 수(디아루가 · 펄기아 · 기라티나 · 아르세우스).
        /// 아르세우스는 와일드카드가 아니라 아르세우스 한 종이다(결정 76 ④ F4 나 — v1.4a 문구, 옛 v1.4의 「Arceus counts as any」는 빠졌다).
        /// </summary>
        public static int FateKinds(Ctx ctx, SeatState st)
        {
            var seen = new bool[4];
            int n = 0;
            foreach (int card in st.FateHand)
            {
                int k = (int)FateRules.KindOf(ctx, card);
                if (seen[k]) continue;
                seen[k] = true;
                n++;
            }
            return n;
        }

        // ── 받기 · 보상 ──

        /// <summary>
        /// 스탬프를 받는다(이미 있으면 false): 칸을 채우고 모은 수의 보상(<see cref="Reward"/>) → StampGained → 보상으로 채운 상태 조건도 이어 받는다.
        /// </summary>
        public static bool Gain(GameState s, Ctx ctx, Emitter fx, int seat, int stamp)
        {
            var st = s.Seats[seat];
            if (st.Stamps[stamp]) return false;
            int before = Count(st);
            st.Stamps[stamp] = true;
            var changes = new List<ResChange>();
            var moves = new List<int>();
            Reward(s, ctx, seat, before + 1, changes, moves);
            if (TrainerRules.Twice(s, ctx, fx, seat)) Reward(s, ctx, seat, before + 1, changes, moves); // collector B: 보상을 두 번(rulings A39)
            int key = ItemOf(ctx, stamp);
            if (fx.On)
                fx.Add(new StampGained
                {
                    Seat = seat, Source = new SourceRef(SourceKind.Stamp, key), StampKey = key, CountBefore = before, CountAfter = before + 1,
                    Changes = changes.ToArray(), MoveKeys = moves.ToArray(),
                });
            TrainerRules.StampGained(s, ctx, seat, stamp); // interviewers B: 남이 스탬프를 얻으면 도전할지 묻는다(rulings A38)
            SweepSeat(s, ctx, fx, seat);
            return true;
        }

        /// <summary>
        /// 모은 수 milestone(1~7)의 보상: 규토리(1·3·5) · 그 덱에서 무작위 5장의 시그니처를 기술 풀로(2 약한 · 4 보통 · 6 강한 — 카드는 조우 풀에서 빠져 StampCards에, 덱이 모자라면 남은 만큼) ·
        /// 수호신의 서를 가방에(7). changes = 자원 변화, moves = 풀에 넣은 기술(뽑은 순서). 컬렉터 파랑(「claim the reward twice」, M6)은 이것을 한 번 더 부른다.
        /// </summary>
        public static void Reward(GameState s, Ctx ctx, int seat, int milestone, List<ResChange> changes, List<int> moves)
        {
            switch (milestone)
            {
                case 1: Apricorns(s, seat, changes, ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow); break;
                case 2: Draw(s, ctx, seat, DeckId.Weak, moves); break;
                case 3: Apricorns(s, seat, changes, ApricornColor.Pink, ApricornColor.Green, ApricornColor.White); break;
                case 4: Draw(s, ctx, seat, DeckId.Moderate, moves); break;
                case 5: Apricorns(s, seat, changes, ApricornColor.Black, ApricornColor.Black, ApricornColor.Black); break;
                case 6: Draw(s, ctx, seat, DeckId.Strong, moves); break;
                case 7:
                {
                    var bag = s.Seats[seat].Bag;
                    int scroll = ctx.Items.Ids.IndexOf(ScrollOfNobility);
                    int before = 0;
                    foreach (int it in bag)
                        if (it == scroll) before++;
                    bag.Add(scroll);
                    changes.Add(new ResChange { Seat = seat, Res = ResKind.Item, Key = scroll, Before = before, After = before + 1 });
                    break;
                }
            }
        }

        static void Apricorns(GameState s, int seat, List<ResChange> changes, params ApricornColor[] colors)
        {
            var ap = s.Seats[seat].Apricorns;
            for (int color = 0; color < SeatState.ApricornColors; color++)
            {
                int n = 0;
                foreach (var c in colors)
                    if ((int)c == color) n++;
                if (n == 0) continue;
                int before = ap[color];
                ap[color] += n;
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = ap[color] });
            }
        }

        static void Draw(GameState s, Ctx ctx, int seat, DeckId deck, List<int> moves)
        {
            var st = s.Seats[seat];
            var pile = s.Decks[(int)deck];
            Piles.Shuffle(pile, s.Rng); // 「Randomly draw」
            for (int i = 0; i < CardsDrawn && pile.Count > 0; i++)
            {
                int card = pile[pile.Count - 1];
                pile.RemoveAt(pile.Count - 1);
                st.StampCards.Add((int)deck);
                st.StampCards.Add(card);
                int move = ctx.Cards.Pokemon[card].Signature;
                st.StampMoves.Add(move);
                moves.Add(move);
            }
        }

        // ── 수호신의 서(보상 7) ──

        static string Species(Ctx ctx, int def)
        {
            string key = ctx.Cards.Pokemon[def].Key;
            int at = key.IndexOf(':');
            return at < 0 ? key : key.Substring(0, at);
        }

        static int Card(Ctx ctx, string species, int state) =>
            ctx.Cards.PokemonIds.TryIndexOf(species + ":" + state.ToString(System.Globalization.CultureInfo.InvariantCulture), out int id) ? id : -1;

        /// <summary>그 카드 종의 수호신 폼(상태 2 · 기후 Noble — 완전히 진화한 스타팅 포켓몬 18종). 없으면 −1.</summary>
        public static int NobleForm(Ctx ctx, int def)
        {
            if (def < 0) return -1;
            int noble = Card(ctx, Species(ctx, def), NobleState);
            return noble >= 0 && ctx.Cards.Pokemon[noble].Climate == Climate.Noble ? noble : -1;
        }

        /// <summary>
        /// 정비 폼 바꾸기(FormRules.ItemFormAllowed): 수호신 폼이 있는 종이면 true를 돌려주고 ok = 갈 수 있나 — 수호신의 서를 지니면 수호신 폼으로, 기본 폼(상태 1)으로는 언제든
        /// (「allows you to use the Noble Form (State 2) of any fully evolved Starter Pokémon when held」 · 원작 커뮤니티 확인). 그 밖의 종이면 false.
        /// </summary>
        public static bool NobleAllowed(Ctx ctx, PokemonState p, int to, out bool ok)
        {
            ok = false;
            int noble = NobleForm(ctx, p.Def);
            if (noble < 0) return false;
            if (to == Card(ctx, Species(ctx, p.Def), FormRules.DefaultState)) ok = true;
            else if (to == noble) ok = Rules.HeldItems.Holds(ctx, p, ScrollOfNobility);
            return true;
        }

        /// <summary>지닌물건이 바뀌었다(FormRules.HeldChanged): 수호신 폼인데 서를 더 지니지 않으면 기본 폼으로. 수호신 폼이 있는 종이면 true(유물 폼은 보지 않는다).</summary>
        public static bool NobleHeldChanged(GameState s, Ctx ctx, Emitter fx, int pokemon)
        {
            var p = s.Pokemon[pokemon];
            int noble = NobleForm(ctx, p.Def);
            if (noble < 0) return false;
            if (p.Def != noble || Rules.HeldItems.Holds(ctx, p, ScrollOfNobility)) return true;
            int home = Card(ctx, Species(ctx, p.Def), FormRules.DefaultState);
            if (home < 0) return true;
            p.Def = home;
            if (fx.On) fx.Add(new FormChanged { Source = SourceRef.None, PokemonId = pokemon, Before = noble, After = home, Temporary = false });
            return true;
        }
    }
}
