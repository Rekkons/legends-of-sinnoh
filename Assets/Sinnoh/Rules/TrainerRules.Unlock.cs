using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 해금될 때: 무녀·카드 바꾸기 8(M6 묶음 9, rulings A40): diamond·pearl·platinum_acolyte B·R · undivided_acolyte B · youngster R.
    /// 「When unlocked」는 <see cref="OnUnlock"/>이 그 자리에서 처리하고, 고를 것(youngster R의 카드 · 파티가 가득일 때 놓아줄 포켓몬)은 좌석의 UnlockDue에 미뤄 두었다가
    /// 다음 쉬는 자리(맵 행동 앞 <see cref="UnlockAsk"/> · 배틀 뒤 턴 끝 앞 · 명령 끝 <see cref="UnlockAfterCommand"/>)에서 출처가 카드인 선택으로 묻는다 —
    /// 해금은 배틀 한가운데(기절·포획)에도 일어나 그 자리에서 묻으면 배틀 진행을 끊는다 [가정 plan §5 #146 ①]. 답은 <see cref="UnlockAnswered"/>(MapAnswered 맨 앞).
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>youngster R 「draw 5 trainer cards from the deck」.</summary>
        public const int CareerDraw = 5;

        /// <summary>UnlockDue의 종류: 카드 바꾸기(값 0) · 파티가 가득해 놓아줄 포켓몬 고르기(값 = 새 포켓몬).</summary>
        public const int DueSwap = 1, DueRelease = 2;

        static readonly string[] AcolyteKeys = { "diamond_acolyte", "pearl_acolyte", "platinum_acolyte" };
        static readonly SinnohGod[] AcolyteGods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        static SinnohGod AcolyteGod(GameState s, Ctx ctx, int seat, AbilityColor color)
        {
            if (!Seated(s, seat)) return SinnohGod.None;
            for (int i = 0; i < AcolyteKeys.Length; i++)
                if (Has(s, ctx, seat, AcolyteKeys[i], color)) return AcolyteGods[i];
            return SinnohGod.None;
        }

        /// <summary>무녀 파랑 「become a Follower of Dialga for the rest of the game」로 고정된 신(파랑이 풀린 무녀 셋 — 아니면 None).</summary>
        public static SinnohGod Devoted(GameState s, Ctx ctx, int seat) => AcolyteGod(s, ctx, seat, Blue);

        /// <summary>추종자를 바꿀 수 없는 좌석(무녀 파랑 · undivided_acolyte B 「for the rest of the game」) — 천상의 균열의 비밀 선택은 「그대로」만(ChoiceRules).</summary>
        public static bool FixedFollower(GameState s, Ctx ctx, int seat) => Devoted(s, ctx, seat) != SinnohGod.None || (Seated(s, seat) && Has(s, ctx, seat, "undivided_acolyte", Blue));

        // ── 해금될 때(TrainerRules.OnUnlock이 부른다) ──

        /// <summary>이 묶음의 「When unlocked」. 처리했으면(카드가 이 묶음 것이면) true.</summary>
        static bool UnlockedNow(GameState s, Ctx ctx, Emitter fx, int seat, AbilityColor color)
        {
            var st = s.Seats[seat];
            var god = AcolyteGod(s, ctx, seat, color);
            if (god != SinnohGod.None && color == Blue)
            {
                // 「When unlocked, become a Follower of Dialga for the rest of the game.」 — 추종자 토큰을 그 신으로 고정(공개 값 · 비밀 선택 중이면 공개될 때 보인다)
                st.Follower = god;
                Used(s, fx, seat, Blue, (int)god);
                return true;
            }
            if (god != SinnohGod.None)
            {
                Chosen(s, ctx, fx, seat, god);
                return true;
            }
            if (color == Blue && Has(s, ctx, seat, "undivided_acolyte", Blue))
            {
                // 「replace your Follower token with the Follower of Arceus token for the rest of the game」
                st.Follower = SinnohGod.Arceus;
                Give(s, ctx, st, "item.follower_of_arceus");
                Used(s, fx, seat, Blue, (int)SinnohGod.Arceus);
                return true;
            }
            if (color == Red && Has(s, ctx, seat, "youngster", Red))
            {
                Career(s, ctx, fx, seat);
                return true;
            }
            return false;
        }

        /// <summary>전용 아이템을 좌석의 트레이너 카드 아이템 칸에(G1 — 이미 있으면 그대로).</summary>
        static void Give(GameState s, Ctx ctx, SeatState st, string key)
        {
            int item = ctx.Items.Ids.IndexOf(key);
            if (item >= 0 && !st.CardItems.Contains(item)) st.CardItems.Add(item);
        }

        /// <summary>
        /// 무녀 빨강 「When unlocked, search the Legendary Pokémon deck for Dialga and add it to your party」: 전설 덱의 그 신 카드(폼 1)를 뽑아 파티에(포획한 것처럼 — 신 토큰은 맵에서 빠진다,
        /// 원작 커뮤니티 확인). 덱에 없으면(남이 잡았거나 쓰러뜨렸다 · 배틀 중) 아무것도 하지 않는다(rulings G3 — 원작 커뮤니티 확인).
        /// 「전설 포획」 스탬프는 아니다(A33). 파티가 가득이면 놓아줄 포켓몬을 미뤄 묻는다.
        /// </summary>
        static void Chosen(GameState s, Ctx ctx, Emitter fx, int seat, SinnohGod god)
        {
            int card = FractureRules.GodCard(ctx, god);
            var deck = s.Decks[(int)DeckId.Legendary];
            if (card < 0 || !deck.Contains(card)) return; // G3
            deck.Remove(card);
            LegendRules.RemoveToken(s, fx, seat, card);
            int p = PokemonRules.Spawn(s, card, seat, (int)DeckId.Legendary);
            s.Pokemon[p].Chosen = true;
            var st = s.Seats[seat];
            if (s.Options.AcolyteRedTemporary == AcolyteRedTemporary.FixedAtUnlock) st.AcolyteToll = Toll(s, seat);
            Used(s, fx, seat, Red, p);
            Join(s, ctx, fx, seat, p);
            Obtained(s, ctx, fx, seat, p, ObtainCause.CardEffect); // 해금 「obtain」(rulings A20-3 — 카드 효과로 얻음)
        }

        /// <summary>카드 효과로 얻은 포켓몬을 파티 맨 뒤에(PokemonGained). 빈 칸이 없으면 놓아줄 포켓몬을 미뤄 묻는다(DueRelease — 그동안 파티 밖에서 기다린다).</summary>
        static void Join(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            var st = s.Seats[seat];
            int slot = PokemonRules.AddToParty(st, pokemon);
            if (slot < 0)
            {
                st.UnlockDue.Add(DueRelease);
                st.UnlockDue.Add(pokemon);
                return;
            }
            var pk = s.Pokemon[pokemon];
            Refresh(s, ctx);
            if (fx.On) fx.Add(new PokemonGained { Seat = seat, PokemonId = pokemon, Species = pk.Def, PartySlot = slot, BallKey = pk.Ball, Hp = PokemonRules.Hp(ctx, pk), Status = pk.Status });
        }

        /// <summary>
        /// 무녀 빨강의 체력·우선도 −값(「by 1 for each Shard you are currently missing or for every 3 Ultra Beasts undefeated」): 신오의 심장 판 = 없는 조각 수(심장을 되살렸으면 0) ·
        /// 울트라 버스트 판 = 못 쓰러뜨린 UB ÷ 3(내림).
        /// </summary>
        public static int Toll(GameState s, int seat)
        {
            if (s.Ub != null) return UndefeatedBeasts(s) / 3;
            var st = s.Seats[seat];
            if (st.Heart) return 0;
            int missing = 0;
            foreach (bool have in st.Shards)
                if (!have) missing++;
            return missing;
        }

        /// <summary>
        /// 아직 쓰러뜨리지 못한 UB 수 = UB 9 − 쓰러뜨린 수(UbState.Defeated — UbBattle.Defeated가 센다, rulings A40 · A53).
        /// </summary>
        static int UndefeatedBeasts(GameState s) => System.Math.Max(0, UbState.Beasts - s.Ub.Defeated);

        /// <summary>무녀 빨강이 데려온 신(PokemonState.Chosen)의 체력·우선도에서 뺄 값 — 주인이 그 무녀 빨강일 때만. 옵션 FixedAtUnlock이면 해금 때 적은 값, 아니면 지금 다시 센다(G2).</summary>
        static int AcolytePenalty(GameState s, Ctx ctx, PokemonState p)
        {
            if (!p.Chosen || AcolyteGod(s, ctx, p.Owner, Red) != LegendRules.GodOf(ctx, p.Def)) return 0;
            int fixedToll = s.Seats[p.Owner].AcolyteToll;
            return fixedToll >= 0 ? fixedToll : Toll(s, p.Owner);
        }

        /// <summary>
        /// youngster R 「When unlocked, draw 5 trainer cards from the deck and swap this card with one of the drawn cards」: 덱 위에서 5장(모자라면 있는 만큼, 없으면 G3)을
        /// 본인만 보는 후보(SeatState.TrainerCardOffer)로 두고 고르기를 미룬다(강제 — 기본 답 = 첫 후보, A33).
        /// </summary>
        static void Career(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var deck = s.Decks[(int)DeckId.TrainerCard];
            var st = s.Seats[seat];
            st.TrainerCardOffer.Clear();
            for (int i = 0; i < CareerDraw && deck.Count > 0; i++)
            {
                st.TrainerCardOffer.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
            if (st.TrainerCardOffer.Count == 0) return; // G3
            Used(s, fx, seat, Red);
            st.UnlockDue.Add(DueSwap);
            st.UnlockDue.Add(0);
        }

        /// <summary>
        /// 카드를 바꾼다: 반바지 꼬마와 고르지 않은 후보는 덱에 넣고 섞는다(A20-1 구현 메모) → 새 카드 = 골드(A20-5) → TrainerCardSwapped → 새 카드의 「When unlocked」 파랑 → 빨강
        /// (「Triggers the "when unlocked" effects on the new trainer card」 — effects 복수, A20-5).
        /// </summary>
        static void Swap(GameState s, Ctx ctx, Emitter fx, int seat, int card)
        {
            var st = s.Seats[seat];
            var deck = s.Decks[(int)DeckId.TrainerCard];
            int old = st.TrainerCard;
            deck.Add(old);
            foreach (int c in st.TrainerCardOffer)
                if (c != card) deck.Add(c);
            st.TrainerCardOffer.Clear();
            Piles.Shuffle(deck, s.Rng);
            st.TrainerCard = card;
            st.TrainerCardTier = Gold;
            st.StrongRemembered = false;
            Refresh(s, ctx);
            if (fx.On) fx.Add(new TrainerCardSwapped { Seat = seat, Source = new SourceRef(SourceKind.TrainerCard, old), FromCard = old, ToCard = card, Tier = Gold });
            OnUnlock(s, ctx, fx, seat, Blue);
            OnUnlock(s, ctx, fx, seat, Red);
        }

        // ── 미뤄 둔 물음 ──

        static bool AnyDue(GameState s)
        {
            foreach (var st in s.Seats)
                if (st.UnlockDue.Count >= 2) return true;
            return false;
        }

        /// <summary>
        /// 미뤄 둔 「When unlocked」 물음 하나를 연다(배틀 밖 · 턴 단계에서만, 좌석 번호 순): at = 0 맵 행동 앞(답이 차례 좌석의 맵 행동을 다시 연다) · 1 + after = 배틀 뒤 턴 끝 앞(답이 BattleFlow.Close(after)를 다시 부른다).
        /// 카드 바꾸기 = Reaction/EffectPick(Count 1 강제 · 비공개 — 후보는 본인만, Options = 후보 카드) · 놓아주기 = Reward/PartyFull(Subject = 새 포켓몬 — 놓아줄 포켓몬, 첫 답 = 새 포켓몬). 열었으면 true.
        /// </summary>
        internal static bool UnlockAsk(GameState s, Ctx ctx, Emitter fx, int at)
        {
            if (s.Battle != null || s.Phase != Phase.Turn) return false;
            for (int seat = 0; seat < s.SeatCount; seat++)
            {
                var st = s.Seats[seat];
                if (st.UnlockDue.Count < 2) continue;
                int kind = st.UnlockDue[0], value = st.UnlockDue[1];
                st.UnlockDue.RemoveRange(0, 2);
                PendingChoice c;
                if (kind == DueSwap)
                {
                    if (st.TrainerCardOffer.Count == 0 || !Has(s, ctx, seat, "youngster", Red)) return UnlockAsk(s, ctx, fx, at);
                    c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Count = 1, Secret = true, Subject = DueSwap, Source = CardSource(s, seat) };
                    c.Options.AddRange(st.TrainerCardOffer);
                }
                else if (kind == DueWeather)
                {
                    // parasol_lady B 첫 날씨(강제 — 기본 답 = 쾌청, A33)
                    if (s.Weather >= 0 || !Has(s, ctx, seat, "parasol_lady", Blue)) return UnlockAsk(s, ctx, fx, at);
                    c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Count = 1, Subject = DueWeather, Source = CardSource(s, seat) };
                    for (int w = 0; w < WeatherTypes.Length; w++) c.Options.Add(w);
                }
                else if (kind == DueDragon)
                {
                    // dragon_tamer B 약한 덱의 드래곤(강제 — 기본 답 = 덱 위의 첫 드래곤). 그사이 하나만 남았으면 묻지 않는다
                    var dragons = Dragons(s, ctx);
                    if (dragons.Count == 0 || !Has(s, ctx, seat, "dragon_tamer", Blue)) return UnlockAsk(s, ctx, fx, at);
                    if (dragons.Count == 1)
                    {
                        Hatchling(s, ctx, fx, seat, dragons[0]);
                        return UnlockAsk(s, ctx, fx, at);
                    }
                    // 후보 = 약한 덱의 내용(덱은 남의 보기에서 가린다) — 본인만 보는 고르기(DueSwap과 같은 꼴, 감사 G3-4)
                    c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Count = 1, Secret = true, Subject = DueDragon, Source = CardSource(s, seat) };
                    c.Options.AddRange(dragons);
                }
                else
                {
                    if (PokemonRules.PartyIndexOf(st, value) >= 0 || s.Pokemon[value].Owner != seat) return UnlockAsk(s, ctx, fx, at);
                    if (PokemonRules.AddToParty(st, value) >= 0) // 그사이 빈 칸이 생겼다
                    {
                        PokemonRules.RemoveFromParty(st, value);
                        Join(s, ctx, fx, seat, value);
                        return UnlockAsk(s, ctx, fx, at);
                    }
                    c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.PartyFull, Seat = seat, Subject = value, Source = CardSource(s, seat) };
                }
                st.UnlockAt = at;
                ChoiceRules.Open(s, ctx, fx, c);
                return true;
            }
            return false;
        }

        /// <summary>명령 끝(Engine.Submit · 정비): 미뤄 둔 물음이 있고 열린 선택이 차례 좌석의 맵 행동 하나뿐이면 그것을 거두고(ChoiceReissued — 새 Id = 물음) 묻는다.</summary>
        internal static void UnlockAfterCommand(GameState s, Ctx ctx, Emitter fx)
        {
            if (!AnyDue(s) || s.Pending.Count != 1 || s.Held.Count > 0 || s.Battle != null) return;
            var open = s.Pending[0];
            if (open.Kind != ChoiceKind.MapAction || open.Answered || open.Seat != s.ActiveSeat) return;
            s.Pending.RemoveAt(0);
            if (fx.On) fx.Add(new ChoiceReissued { Seat = open.Seat, OldId = open.Id, NewId = s.NextChoiceId });
            if (!UnlockAsk(s, ctx, fx, 0)) TurnFlow.OpenMapAction(s, ctx, fx, open.Seat); // 미뤄 둔 것이 그사이 쓸모없어졌다 — 맵 행동을 다시
        }

        /// <summary>미뤄 둔 물음의 답(TrainerRules.MapAnswered 맨 앞). 이 물음이 아니면 false.</summary>
        static bool UnlockAnswered(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (c.Seat < 0 || c.Seat >= s.SeatCount) return false;
            var st = s.Seats[c.Seat];
            if (st.UnlockAt < 0) return false;
            int at = st.UnlockAt;
            st.UnlockAt = -1;
            if (c.Sub == ChoiceSub.PartyFull)
            {
                int fresh = c.Subject;
                MaintenanceRules.Release(s, ctx, fx, c.Seat, a.Target);
                if (a.Target != fresh) Join(s, ctx, fx, c.Seat, fresh);
            }
            else
            {
                int pick = a.Kind == AnswerKind.Pick ? a.Target : c.Options[0];
                if (c.Subject == DueWeather) SetWeather(s, ctx, fx, c.Seat, pick, false);
                else if (c.Subject == DueDragon) Hatchling(s, ctx, fx, c.Seat, pick);
                else Swap(s, ctx, fx, c.Seat, pick);
            }
            if (UnlockAsk(s, ctx, fx, at)) return true;
            if (at == 0) MapActions.Continue(s, ctx, fx, s.ActiveSeat);
            else BattleFlow.Close(s, ctx, fx, at - 1);
            return true;
        }
    }
}
