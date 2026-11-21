using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 제작·사당·스탬프·진화·파티(M6 묶음 8, rulings A39): ace_trainer B · chef B · picnicker R · poke_fan R · expert B · collector B·R · interviewers R ·
    /// lady B · pokemon_breeder B·R · pokemon_ranger B·R. (backpacker R의 지닌물건 둘은 rulings A39 — 같은 묶음의 뒤 커밋.)
    /// 정비 명령 셋(Convert · Adopt · PassMove)은 <see cref="CheckCraftMaint"/>·<see cref="ApplyCraftMaint"/>.
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>lady B 「When unlocked, gain 7 Journey Points … Evolve Costs … reduced by 2」 · pokemon_breeder R 「+5 health and +5 initiative」 · ranger R UB 「+3 … immediately」.</summary>
        public const int HeritageJp = 7, HeritageDiscount = 2, PerfectIvs = 5, RangerUbBonus = 3;

        /// <summary>사용 기록(UsageKind.Ability)의 Key — collector R 뒷부분의 턴당 한 장(맵 능력의 Key = 칸 번호 1·2와 겹치지 않게).</summary>
        const int CollectorKey = 20;

        // ── ace_trainer B ──

        /// <summary>ace_trainer B 「Battle Items only need 1 of each Apricorn on the recipe to craft」: 배틀 아이템(지닌물건 레시피)의 재료를 색마다 하나로. 그 밖은 레시피 그대로.</summary>
        public static IReadOnlyList<ApricornColor> Recipe(GameState s, Ctx ctx, int seat, ItemDef d)
        {
            if (d.Recipe == null || d.Kind != ItemKind.Held || !Seated(s, seat) || !Has(s, ctx, seat, "ace_trainer", Blue)) return d.Recipe;
            var r = new List<ApricornColor>(d.Recipe.Count);
            foreach (var c in d.Recipe)
                if (!r.Contains(c)) r.Add(c);
            return r;
        }

        /// <summary>ace_trainer B 「You may retry a Pokédex search once per encounter」: 조우 고르기(EncounterPick, Count 0 = 아직 다시 하지 않음)에 UseAbility 답을 더한다.</summary>
        public static bool CanRetry(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "ace_trainer", Blue);

        // ── picnicker R · poke_fan R(제작 답의 Arg = 공짜로 더 받을 것) ──

        /// <summary>
        /// 제작 답에 붙일 공짜 몫(CraftRules.Legal — Craft(Target = 아이템, Arg = 공짜 몫)): picnicker R 「When crafting an Apricorn Stew, you may gain the effect of another Apricorn Stew for free」 = 다른 스튜 셋 ·
        /// poke_fan R 「When crafting a Battle Item, you may gain another Battle Item for free」 = 배틀 아이템 아무것(같은 것도 — 「another」를 「하나 더」로 읽는다 [가정 plan §5 #145 ③]).
        /// </summary>
        internal static void Bonuses(GameState s, Ctx ctx, int seat, int item, List<int> into)
        {
            into.Clear();
            if (!Seated(s, seat)) return;
            var d = ctx.Items.Items[item];
            bool stew = d.Kind == ItemKind.Stew && Has(s, ctx, seat, "picnicker", Red);
            bool kit = d.Kind == ItemKind.Held && d.Recipe != null && Has(s, ctx, seat, "poke_fan", Red);
            if (!stew && !kit) return;
            for (int j = 0; j < ctx.Items.Items.Count; j++)
            {
                var e = ctx.Items.Items[j];
                if (e.Recipe == null || e.Kind != d.Kind || (stew && j == item)) continue;
                into.Add(j);
            }
        }

        /// <summary>제작한 뒤 공짜 몫을 받는다(CraftRules.Make): 스튜는 그 효과(만든 스튜의 턴당 1번을 쓰지 않는다), 배틀 아이템은 가방에. changes = 제작 이벤트의 자원 변화(배틀 아이템).</summary>
        internal static void Bonus(GameState s, Ctx ctx, Emitter fx, int seat, int item, int bonus, List<ResChange> changes)
        {
            var d = ctx.Items.Items[item];
            Used(s, fx, seat, Red, bonus);
            if (d.Kind == ItemKind.Stew) return; // 효과는 만든 스튜 뒤에(CraftRules.Make가 Eat(free))
            var bag = s.Seats[seat].Bag;
            int before = 0;
            foreach (int b in bag)
                if (b == bonus) before++;
            bag.Add(bonus);
            changes.Add(new ResChange { Seat = seat, Res = ResKind.Item, Key = bonus, Before = before, After = before + 1 });
        }

        // ── expert B ──

        /// <summary>expert B 「When you make a Shrine Offering, draw 1 extra Shrine Event card. … you still play the same number」: 더 뽑을 장수.</summary>
        internal static int ShrineKeeper(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (!Seated(s, seat) || !Has(s, ctx, seat, "expert", Blue)) return 0;
            Used(s, fx, seat, Blue);
            return 1;
        }

        // ── collector B·R ──

        /// <summary>collector B 「When claiming any stamp reward, you may claim the reward twice」: 받을 때 보상을 한 번 더(묻지 않는다 — 보상은 모두 이득 [가정 plan §5 #145 ④]).</summary>
        internal static bool Twice(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (!Seated(s, seat) || !Has(s, ctx, seat, "collector", Blue)) return false;
            Used(s, fx, seat, Blue);
            return true;
        }

        /// <summary>
        /// collector R 뒷부분 「when you discard any Pokémon or moves, add them to your stamp rewards move pool」 — 놓아준 포켓몬(카드째 — 그 시그니처가 풀에) · 이미 배운 기술을 버림(원작 커뮤니티 확인
        /// 「just when releasing Pokémon or discarding already learnt moves」). RuleOptions.CollectorDiscards: 턴에 한 장(기본) · 문구대로 · 없음. 풀에 넣었으면 true(카드는 버림 더미로 가지 않는다).
        /// </summary>
        internal static bool Pooled(GameState s, Ctx ctx, Emitter fx, int seat, int move, int card = -1, int deck = -1)
        {
            if (move < 0 || !Seated(s, seat) || !Has(s, ctx, seat, "collector", Red) || s.Options.CollectorDiscards == CollectorDiscards.Off) return false;
            if (s.Options.CollectorDiscards == CollectorDiscards.OncePerTurn)
            {
                if (MapActions.Used(s, seat, UsageKind.Ability, CollectorKey)) return false;
                s.Used.Add(new UsageMark { Seat = seat, Kind = UsageKind.Ability, Key = CollectorKey, Scope = s.TurnId });
            }
            var st = s.Seats[seat];
            if (card >= 0)
            {
                st.StampCards.Add(deck);
                st.StampCards.Add(card);
            }
            st.StampMoves.Add(move);
            Used(s, fx, seat, Red, move);
            return true;
        }

        /// <summary>스탬프 보상 풀에서 그 포켓몬 카드가 있는 자리(StampCards 짝의 앞 칸) — 그 시그니처가 풀에 남아 있을 때만. 없으면 −1.</summary>
        static int PoolCard(Ctx ctx, SeatState st, int card)
        {
            if (card < 0 || card >= ctx.Cards.Pokemon.Count || !st.StampMoves.Contains(ctx.Cards.Pokemon[card].Signature)) return -1;
            for (int i = 0; i + 1 < st.StampCards.Count; i += 2)
                if (st.StampCards[i + 1] == card) return i;
            return -1;
        }

        // ── 정비 명령: chef B Convert · collector R Adopt · interviewers R PassMove ──

        internal static RejectReason CheckCraftMaint(GameState s, Ctx ctx, int seat, Maintenance m)
        {
            if (!Seated(s, seat)) return RejectReason.Illegal;
            var st = s.Seats[seat];
            switch (m.Kind)
            {
                case MaintKind.Convert:
                {
                    // 「freely convert」: 언제든(정비 Always — 내 공격 처리 중만 아니면) · 같은 단계의 다른 색으로 하나씩 [가정 plan §5 #145 ①]
                    if (!Has(s, ctx, seat, "chef", Blue)) return RejectReason.Illegal;
                    if (m.Slot < 0 || m.Slot >= SeatState.ApricornColors || m.Value < 0 || m.Value >= SeatState.ApricornColors) return RejectReason.Illegal;
                    if (m.Slot == m.Value) return RejectReason.NoChange;
                    if (MapActions.ApricornGrade((ApricornColor)m.Slot) != MapActions.ApricornGrade((ApricornColor)m.Value) || st.Apricorns[m.Slot] < 1) return RejectReason.Illegal;
                    return RejectReason.None;
                }
                case MaintKind.Adopt:
                {
                    if (!Has(s, ctx, seat, "collector", Red) || PoolCard(ctx, st, m.Value) < 0) return RejectReason.Illegal;
                    int cap = st.Ranch ? st.Party.Length : st.OpenSlots;
                    return PokemonRules.PartyCount(st) < cap ? RejectReason.None : RejectReason.Illegal;
                }
                case MaintKind.PassMove:
                {
                    // 「During your turn whilst outside of battle」 · 「The Pokémon you give the move to must be able to learn it」(맞바꾸면 양쪽 다)
                    if (!Has(s, ctx, seat, "interviewers", Red)) return RejectReason.Illegal;
                    int to = m.Value, toSlot = m.Values.Count > 0 ? m.Values[0] : -1;
                    if (PokemonRules.PartyIndexOf(st, m.Pokemon) < 0 || PokemonRules.PartyIndexOf(st, to) < 0 || to == m.Pokemon) return RejectReason.Illegal;
                    if (m.Slot < 0 || m.Slot >= PokemonState.MoveSlots || toSlot < 0 || toSlot >= PokemonState.MoveSlots) return RejectReason.Illegal;
                    var p = s.Pokemon[m.Pokemon];
                    var q = s.Pokemon[to];
                    if (BreachRules.Blocked(p, m.Slot) || BreachRules.Blocked(q, toSlot)) return RejectReason.Illegal; // 균열 카드가 막은 칸은 가르치기에서 빠진다(rulings A52 ①)
                    int a = p.Moves[m.Slot], b = q.Moves[toSlot];
                    if (a == b) return RejectReason.NoChange;
                    if (s.Battle != null || s.Phase != Phase.Turn || s.ActiveSeat != seat) return RejectReason.Timing;
                    if (a >= 0 && !ChoiceRules.CanLearn(ctx, q, ctx.Cards.Moves[a].Type)) return RejectReason.Illegal;
                    if (b >= 0 && !ChoiceRules.CanLearn(ctx, p, ctx.Cards.Moves[b].Type)) return RejectReason.Illegal;
                    return RejectReason.None;
                }
                default:
                    return RejectReason.BadCommand;
            }
        }

        internal static void ApplyCraftMaint(GameState s, Ctx ctx, Emitter fx, int seat, Maintenance m)
        {
            var st = s.Seats[seat];
            switch (m.Kind)
            {
                case MaintKind.Convert:
                {
                    var changes = new List<ResChange>();
                    CraftRules.MoveApricorns(st, seat, new[] { (ApricornColor)m.Slot }, -1, changes);
                    CraftRules.MoveApricorns(st, seat, new[] { (ApricornColor)m.Value }, +1, changes);
                    Used(s, fx, seat, Blue, m.Slot, m.Value);
                    if (fx.On) fx.Add(new ApricornsGained { Seat = seat, Source = CardSource(s, seat), Changes = changes.ToArray() });
                    return;
                }
                case MaintKind.Adopt:
                {
                    int at = PoolCard(ctx, st, m.Value);
                    int deck = st.StampCards[at];
                    st.StampCards.RemoveRange(at, 2);
                    st.StampMoves.Remove(ctx.Cards.Pokemon[m.Value].Signature);
                    int p = PokemonRules.Spawn(s, m.Value, seat, deck);
                    int slot = PokemonRules.AddToParty(st, p);
                    Used(s, fx, seat, Red, p);
                    var pk = s.Pokemon[p];
                    if (fx.On) fx.Add(new PokemonGained { Seat = seat, PokemonId = p, Species = pk.Def, PartySlot = slot, BallKey = pk.Ball, Hp = PokemonRules.Hp(ctx, pk), Status = pk.Status });
                    Refresh(s, ctx);
                    Obtained(s, ctx, fx, seat, p, ObtainCause.CardEffect); // 해금 「obtain」(rulings A20-3 — 카드 효과로 얻음)
                    return;
                }
                case MaintKind.PassMove:
                {
                    var p = s.Pokemon[m.Pokemon];
                    var q = s.Pokemon[m.Value];
                    int toSlot = m.Values[0];
                    int a = p.Moves[m.Slot], b = q.Moves[toSlot];
                    p.Moves[m.Slot] = b;
                    q.Moves[toSlot] = a;
                    MoveUnder(p, q, a);
                    MoveUnder(q, p, b);
                    Used(s, fx, seat, Red, m.Pokemon, m.Value);
                    if (fx.On)
                    {
                        if (a >= 0) fx.Add(new MoveTaught { Seat = seat, PokemonId = m.Value, Slot = toSlot, MoveKey = a, ReplacedKey = b });
                        else fx.Add(new MoveForgotten { PokemonId = m.Value, Slot = toSlot, MoveKey = b });
                        if (b >= 0) fx.Add(new MoveTaught { Seat = seat, PokemonId = m.Pokemon, Slot = m.Slot, MoveKey = b, ReplacedKey = a });
                        else fx.Add(new MoveForgotten { PokemonId = m.Pokemon, Slot = m.Slot, MoveKey = a });
                    }
                    return;
                }
            }
        }

        /// <summary>옮긴 기술 때문에 밑에 둔 옛 카드(진화하며 옛 기술을 가르쳤다, p.5)는 그 기술을 따라간다.</summary>
        static void MoveUnder(PokemonState from, PokemonState to, int move)
        {
            if (move < 0) return;
            for (int i = 0; i + 2 < from.Under.Count; i += 3)
            {
                if (from.Under[i + 2] != move) continue;
                to.Under.AddRange(from.Under.GetRange(i, 3));
                from.Under.RemoveRange(i, 3);
                return;
            }
        }

        // ── backpacker R ──

        /// <summary>backpacker R 「Your Pokémon can now hold and use up to two Held Items so long as they are not the same item」: 정비 HoldItem의 Slot 1(둘째 칸)을 쓸 수 있다 — 유물도(유물 = 지닌물건, A33).</summary>
        public static bool Pockets(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "backpacker", Red);

        /// <summary>
        /// 거래로 주인이 바뀐 포켓몬의 둘째 지닌물건: 새 주인이 backpacker R이 아니면 새 주인의 가방으로 — 둘째 칸은 backpacker R 좌석의 포켓몬만 쓴다
        /// (원작 커뮤니티 확인 — 자체 검토).
        /// team_rocket B로 빼앗은 포켓몬도 거래처럼 부른다(사용자 결정 84 T7 ④ 나 — 그 카드의 「keeps everything it currently has attached」보다 backpacker R의 「*your*」를 따른다).
        /// </summary>
        internal static void Unpocket(GameState s, Ctx ctx, Emitter fx, int pokemon)
        {
            var p = s.Pokemon[pokemon];
            int seat = p.Owner;
            if (p.HeldItem2 < 0 || seat < 0 || seat >= s.SeatCount || Pockets(s, ctx, seat)) return;
            int item = p.HeldItem2;
            p.HeldItem2 = -1;
            s.Seats[seat].Bag.Add(item);
            if (fx.On) fx.Add(new ItemHeld { Seat = seat, PokemonId = -1, ItemKey = item, FromPokemonId = pokemon });
            FormRules.HeldChanged(s, ctx, fx, pokemon);
        }

        // ── lady B · pokemon_breeder B ──

        /// <summary>lady B 「the Evolve Costs of your Pokémon are reduced by 2」: 진화 비용에서 뺄 값(반값보다 먼저 — dragon_tamer B의 +5와 같은 순서 [가정 plan §5 #145 ⑤]).</summary>
        public static int Heritage(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "lady", Blue) ? HeritageDiscount : 0;

        /// <summary>pokemon_breeder B: 진화 답의 알 기술 꼴(Evolve, Arg = 2, Values = [진화형, 칸]).</summary>
        public const int EggArg = 2;

        /// <summary>
        /// pokemon_breeder B 「When evolving a Weak Pokémon, you may instead teach the move of its evolution to the pre-evolution as an Egg Move. This Pokémon can no longer evolve.」:
        /// 약한 포켓몬(스타터 포함 — A32 ①)이 진화할 수 있을 때 진화 답 대신 원작 커뮤니티 확인. 비용은 진화와 같다 [가정 plan §5 #145 ⑥].
        /// </summary>
        internal static void EggAnswers(GameState s, Ctx ctx, int seat, int pokemon, List<int> branches, List<ChoiceAnswer> into)
        {
            var p = s.Pokemon[pokemon];
            if (!Seated(s, seat) || !Has(s, ctx, seat, "pokemon_breeder", Blue) || !IsWeak(ctx, p)) return;
            foreach (int to in branches)
            {
                int move = ctx.Cards.Pokemon[to].Signature;
                if (move < 0 || move == ctx.Cards.Pokemon[p.Def].Signature || System.Array.IndexOf(p.Moves, move) >= 0) continue;
                for (int slot = 0; slot < PokemonState.MoveSlots; slot++)
                {
                    if (BreachRules.Blocked(p, slot)) continue; // 균열 카드가 막은 칸은 못 쓴다(rulings A52 ①)
                    var a = ChoiceAnswer.Of(AnswerKind.Evolve, pokemon, EggArg);
                    a.Values.Add(to);
                    a.Values.Add(slot);
                    into.Add(a);
                }
            }
        }

        /// <summary>알 기술 답 적용: 진화 비용을 내고 그 칸에 진화형의 시그니처를 가르친다(밀려난 기술은 버림 — collector R 풀) · 더는 진화하지 못한다. 턴은 이어진다.</summary>
        public static void Hatch(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            var st = s.Seats[seat];
            var p = s.Pokemon[a.Target];
            int to = a.Values[0], slot = a.Values[1];
            st.Jp -= EvolveRules.Cost(s, ctx, seat, a.Target);
            int move = ctx.Cards.Pokemon[to].Signature;
            int old = p.Moves[slot];
            p.Moves[slot] = move;
            EvolveRules.Forgot(s, p, old);
            Pooled(s, ctx, fx, seat, old);
            p.EggMove = true;
            Refresh(s, ctx);
            Used(s, fx, seat, Blue, a.Target, move);
            if (fx.On) fx.Add(new MoveTaught { Seat = seat, PokemonId = a.Target, Slot = slot, MoveKey = move, ReplacedKey = old });
            MapActions.Continue(s, ctx, fx, seat);
        }

        // ── pokemon_breeder R · pokemon_ranger R(HealthBonus · PriorityMods가 더한다) ──

        /// <summary>
        /// pokemon_breeder R 「Your Pokémon that were taught an Egg Move gain +5 health and +5 initiative」 + pokemon_ranger R 「Your Moderate Pokémon gain +1 health and +1 initiative for each locked party slot
        /// that you unlock. In the Ultra Burst campaign, you get the maximum of +3 … immediately」(처음부터 열린 칸은 세지 않는다 — A33): 체력·우선도에 같이 더할 값.
        /// </summary>
        static int GrowthBonus(GameState s, Ctx ctx, PokemonState p)
        {
            int seat = p.Owner, d = 0;
            if (!Seated(s, seat)) return 0;
            if (p.EggMove && Has(s, ctx, seat, "pokemon_breeder", Red)) d += PerfectIvs;
            if (Has(s, ctx, seat, "pokemon_ranger", Red) && !p.Alpha && ctx.Cards.Pokemon[p.Def].Tier == EncounterTier.Moderate)
                d += s.Campaign == Campaign.UltraBurst ? RangerUbBonus : System.Math.Max(0, s.Seats[seat].OpenSlots - CampaignRules.OpenPartySlots(s.Campaign, 0));
            return d;
        }

        // ── pokemon_ranger B ──

        /// <summary>
        /// pokemon_ranger B 「You may use any locked party slots to store your Pokémon but these Pokémon cannot be used in battle」: 좌석 칸 Ranch를 적는다(Refresh) — PokemonRules.AddToParty가 잠긴 칸까지 채우고
        /// 배틀(명단·교체·쓸 포켓몬)은 열린 칸(OpenSlots 앞)만 본다(PokemonRules.Fights). UB 판의 「균열 카드 2장 보관」은 균열 카드 묶음(wt-s2)이 이 칸에 붙인다 [가정 plan §5 #145 ②].
        /// </summary>
        static void Ranches(GameState s, Ctx ctx)
        {
            for (int seat = 0; seat < s.SeatCount; seat++) s.Seats[seat].Ranch = Seated(s, seat) && Has(s, ctx, seat, "pokemon_ranger", Blue);
        }

        /// <summary>pokemon_ranger B 울트라 버스트 「store up to 2 Distortion Breach cards in each empty slot」의 빈 칸 하나에 둘 수 있는 장수.</summary>
        public const int RangerBreachPerSlot = 2;

        /// <summary>
        /// pokemon_ranger B 울트라 버스트 판: 보관할 수 있는 균열 카드 수 = 빈 파티 칸 × 2(울트라 버스트는 여섯 칸이 모두 열려 있어 「empty slot」 = 포켓몬이 없는 칸).
        /// 「may store」 — 붙이기 물음에서 보관할지 고른다(BreachRules.OpenAttach — 사용자 결정 84 U11 나, 옛 [가정 §5 #149 ①]은 묻지 않고 늘 보관). 카드가 없거나 신오의 심장이면 0.
        /// </summary>
        public static int BreachStorage(GameState s, Ctx ctx, int seat)
        {
            if (s.Campaign != Campaign.UltraBurst || !Seated(s, seat) || !Has(s, ctx, seat, "pokemon_ranger", Blue)) return 0;
            int empty = 0;
            foreach (int p in s.Seats[seat].Party)
                if (p < 0) empty++;
            return empty * RangerBreachPerSlot;
        }
    }
}
