using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 진화(S9_1, rulings A9, Lua R14, 룰북 p.5, M5 「제작·은행·진화」). 내 턴·배틀 밖에서 진화 비용만큼 JP를 버리고(우러난 스튜면 이번 턴 한 번 절반, 내림 — CraftRules)
    /// 완전 회복(피해·상태이상 — 절차의 첫 단계, A9) → 진화 카드를 진화 덱에서 꺼내 그 포켓몬이 된다. 지닌물건·배운 기술·볼·우두머리 토큰은 그대로 간다.
    /// MapAction 답 Evolve(Target = 포켓몬 인스턴스, Arg = 1이면 우러난 스튜의 반값을 이 진화에 쓴다 — 어느 진화에 쓸지 플레이어가 고른다, −1 = 제값).
    /// 진화 대상이 여럿이면(R14 — 이브이) 분기를 묻는다(Reward/EvolveBranch, 기본 = evolve_into 첫째, G9 — 반값을 골랐으면 Source = 우러난 스튜) — JP는 분기를 고른 뒤에 낸다.
    /// 그다음 옛 포켓몬의 시그니처 기술을 가르칠지 묻는다(Reward/EvolveTeach, 「regardless of the types it can learn」, 기본 = 안 가르침 G9) — 이미 가진 기술이면 묻지 않는다.
    /// 옛 카드: 옛 기술을 가르치면 새 카드 밑에 남고(<see cref="PokemonState.Under"/>, p.5 「by flipping the old card over and placing it under the new card」), 안 가르치면 제 덱의 버림 더미로.
    /// 밑에 둔 카드는 그 기술을 다른 기술로 바꾸거나 포켓몬을 놓아줄 때 버린다(<see cref="Forgot"/>).
    /// 진화 카드가 진화 덱에 없어도 진화한다 — 카드를 복제한 것으로 보고(원작 커뮤니티 확인) 그 포켓몬은 덱 밖 카드(Deck = −1)가 된다 [가정].
    /// 남은 장수는 공개 정보로 센다(덱 구성 − 나와 있는 진화 포켓몬 − 밑에 둔 진화 카드 − 진화 덱 버림 더미) — 보기 사본의 Legal이 원본과 같다(덱 내용은 보기에서 가린다).
    /// </summary>
    public static class EvolveRules
    {
        /// <summary>그 진화 카드가 진화 덱에 남은 장수(공개 정보로 센다).</summary>
        public static int Available(GameState s, Ctx ctx, int def)
        {
            int n = 0;
            foreach (var e in ctx.Deck(DeckId.Evolution).Cards)
                if (e.Card == def) n += e.Copies;
            if (n == 0) return 0;
            foreach (var p in s.Pokemon)
            {
                if (p.Def == def && p.Deck == (int)DeckId.Evolution) n--;
                for (int i = 0; i + 2 < p.Under.Count; i += 3)
                    if (p.Under[i] == (int)DeckId.Evolution && p.Under[i + 1] == def) n--;
            }
            foreach (int c in s.Discards[(int)DeckId.Evolution])
                if (c == def) n--;
            return n > 0 ? n : 0;
        }

        /// <summary>그 포켓몬이 지금 진화할 수 있는 대상(evolve_into 순서). 덱에 카드가 없어도 된다(복제, 원작 커뮤니티 확인).</summary>
        public static void Branches(GameState s, Ctx ctx, int pokemon, List<int> into)
        {
            into.Clear();
            foreach (int to in ctx.Cards.Pokemon[s.Pokemon[pokemon].Def].EvolveInto)
                if (!into.Contains(to)) into.Add(to);
        }

        /// <summary>그 좌석이 지금 그 포켓몬을 진화시킬 때 낼 JP(halved = 우러난 스튜의 반값을 쓴다 — 남아 있을 때만 절반, 내림). 진화가 없으면 −1.</summary>
        public static int Cost(GameState s, Ctx ctx, int seat, int pokemon, bool halved = false)
        {
            var cost = ctx.Cards.Pokemon[s.Pokemon[pokemon].Def].EvolveCost;
            if (!cost.HasValue) return -1;
            int c = System.Math.Max(0, cost.Value + TrainerRules.ApexCost(s, ctx, pokemon) - TrainerRules.Heritage(s, ctx, seat)); // dragon_tamer B +5 · lady B −2를 반값보다 먼저(원작 커뮤니티 확인, rulings A39 · A41)
            return halved && CraftRules.InfusedReady(s, seat) ? c / 2 : c;
        }

        /// <summary>진화시킬 수 있나: 내 파티 포켓몬이고, 진화 대상이 있고, JP가 된다(halved면 반값으로). 기절한 포켓몬도 된다(진화가 완전 회복시킨다, A9).</summary>
        public static bool CanEvolve(GameState s, Ctx ctx, int seat, int pokemon, bool halved = false)
        {
            if (seat < 0 || seat >= s.SeatCount || pokemon < 0 || PokemonRules.PartyIndexOf(s.Seats[seat], pokemon) < 0) return false;
            if (s.Pokemon[pokemon].EggMove) return false; // pokemon_breeder B 「This Pokémon can no longer evolve」(rulings A39)
            if (halved && !CraftRules.InfusedReady(s, seat)) return false;
            int cost = Cost(s, ctx, seat, pokemon, halved);
            if (cost < 0 || s.Seats[seat].Jp < cost) return false;
            var branches = new List<int>(2);
            Branches(s, ctx, pokemon, branches);
            return branches.Count > 0;
        }

        /// <summary>MapAction의 진화 답(파티 순서, G9): 포켓몬마다 제값(Arg −1) → 우러난 스튜가 남았으면 반값(Arg 1).</summary>
        public static void Legal(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into)
        {
            foreach (int p in s.Seats[seat].Party)
            {
                if (p < 0) continue;
                if (CanEvolve(s, ctx, seat, p)) into.Add(ChoiceAnswer.Of(AnswerKind.Evolve, p));
                if (CanEvolve(s, ctx, seat, p, true)) into.Add(ChoiceAnswer.Of(AnswerKind.Evolve, p, 1));
                if (CanEvolve(s, ctx, seat, p))
                {
                    var branches = new List<int>(2);
                    Branches(s, ctx, p, branches);
                    TrainerRules.EggAnswers(s, ctx, seat, p, branches, into); // pokemon_breeder B: 진화 대신 알 기술(Arg 2, rulings A39)
                }
            }
        }

        /// <summary>진화 답 적용: 대상이 하나면 바로, 여럿이면 분기를 묻는다(Continues = 이 Seq).</summary>
        public static void Apply(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, bool halved = false)
        {
            var branches = new List<int>(2);
            Branches(s, ctx, pokemon, branches);
            if (branches.Count == 1)
            {
                Evolve(s, ctx, fx, seat, pokemon, branches[0], halved);
                return;
            }
            var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.EvolveBranch, Seat = seat, Subject = pokemon };
            if (halved) c.Source = new SourceRef(SourceKind.Item, ctx.Items.Ids.IndexOf(CraftRules.InfusedStew));
            c.Options.AddRange(branches);
            ChoiceRules.Open(s, ctx, fx, c);
        }

        /// <summary>진화 분기 · 옛 기술 가르치기의 답. 끝나면 턴이 이어진다.</summary>
        public static void ApplyReward(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (c.Sub == ChoiceSub.EvolveBranch)
            {
                // 묻는 사이 정비(놓아주기)로 그 포켓몬이 없어졌거나 조건이 깨졌으면 아무것도 내지 않고 넘어간다(G3)
                bool halved = c.Source.Kind == SourceKind.Item;
                if (CanEvolve(s, ctx, c.Seat, c.Subject, halved)) Evolve(s, ctx, fx, c.Seat, c.Subject, a.Target, halved);
                else MapActions.Continue(s, ctx, fx, c.Seat);
                return;
            }
            int pokemon = c.Options[0];
            if (PokemonRules.PartyIndexOf(s.Seats[c.Seat], pokemon) >= 0)
            {
                var p = s.Pokemon[pokemon];
                if (a.Kind == AnswerKind.Pick)
                {
                    int old = p.Moves[a.Arg];
                    p.Moves[a.Arg] = c.Subject;
                    Forgot(s, p, old);
                    TrainerRules.Pooled(s, ctx, fx, c.Seat, old); // collector R(rulings A39)
                    if (fx.On) fx.Add(new MoveTaught { Seat = c.Seat, PokemonId = pokemon, Slot = a.Arg, MoveKey = c.Subject, ReplacedKey = old });
                }
                else Forgot(s, p, c.Subject); // 안 가르쳤다: 밑에 둔 옛 카드를 버린다
            }
            MapActions.Continue(s, ctx, fx, c.Seat);
        }

        /// <summary>그 포켓몬이 그 기술을 잃었다(다른 기술로 바꿈 · 안 가르침): 그 기술 때문에 밑에 둔 옛 카드를 제 덱의 버림 더미로.</summary>
        internal static void Forgot(GameState s, PokemonState p, int move)
        {
            if (move < 0) return;
            for (int i = 0; i + 2 < p.Under.Count; i += 3)
            {
                if (p.Under[i + 2] != move) continue;
                s.Discards[p.Under[i]].Add(p.Under[i + 1]);
                p.Under.RemoveRange(i, 3);
                return;
            }
        }

        /// <summary>그 포켓몬을 내보낸다(놓아주기): 밑에 둔 옛 카드를 모두 제 덱의 버림 더미로.</summary>
        internal static void DiscardUnder(GameState s, PokemonState p)
        {
            for (int i = 0; i + 2 < p.Under.Count; i += 3) s.Discards[p.Under[i]].Add(p.Under[i + 1]);
            p.Under.Clear();
        }

        static void Evolve(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, int to, bool halvedAsked)
        {
            var st = s.Seats[seat];
            var p = s.Pokemon[pokemon];
            int from = p.Def;
            bool halved = halvedAsked && CraftRules.InfusedReady(s, seat);
            int cost = Cost(s, ctx, seat, pokemon, halved);
            int jpBefore = st.Jp;
            st.Jp -= cost;
            if (halved) CraftRules.SpendInfused(s, seat);

            // A9: 완전 회복이 절차의 첫 단계다(피해·상태이상)
            int hpBefore = PokemonRules.Hp(ctx, p);
            var status = p.Status;
            p.Damage = 0;
            StatusRules.Clear(p);

            // p.5: 옛 포켓몬의 기술(시그니처)을 가르칠 수 있다 — 새 시그니처와 같거나 이미 배운 기술이면 묻지 않는다
            int move = ctx.Cards.Pokemon[from].Signature;
            bool ask = move != ctx.Cards.Pokemon[to].Signature && System.Array.IndexOf(p.Moves, move) < 0;
            // 옛 카드: 가르칠지 묻는 동안은 새 카드 밑에(답이 안 가르침이면 버린다), 묻지 않으면 제 덱의 버림 더미로. 진화 카드는 진화 덱에서(R14: 고르지 않은 분기는 덱에 남는다) — 없으면 복제
            if (p.Deck >= 0)
            {
                if (ask) p.Under.AddRange(new[] { p.Deck, from, move });
                else s.Discards[p.Deck].Add(from);
            }
            bool inDeck = Available(s, ctx, to) > 0;
            if (inDeck) s.Decks[(int)DeckId.Evolution].Remove(to);
            p.Def = to;
            p.Deck = inDeck ? (int)DeckId.Evolution : -1;
            p.FormBeforeBattle = -1;
            if (fx.On)
                fx.Add(new Evolved
                {
                    Seat = seat, PokemonId = pokemon, From = from, To = to, JpCost = cost, Halved = halved,
                    Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Jp, Key = -1, Before = jpBefore, After = st.Jp } },
                    Pokemon = new[] { new HealEntry { Id = pokemon, HpBefore = hpBefore, HpAfter = PokemonRules.Hp(ctx, p), StatusBefore = status } },
                });
            TrainerRules.Obtained(s, ctx, fx, seat, pokemon, ObtainCause.Evolution); // 트레이너 카드 해금: 진화로 된 보통·강함 = 얻음(rulings A20-4, UnlockByEvolution)

            if (!ask)
            {
                MapActions.Continue(s, ctx, fx, seat);
                return;
            }
            var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.EvolveTeach, Seat = seat, Subject = move };
            c.Options.Add(pokemon);
            ChoiceRules.Open(s, ctx, fx, c);
        }
    }
}
