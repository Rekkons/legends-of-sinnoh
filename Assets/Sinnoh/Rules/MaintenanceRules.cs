using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 정비(언제든 행동, 01 §3-4, 결정 15). 열린 선택에 답하지 않고, 시점 검사만 통과하면 바로 적용한다.
    /// 명령은 결과를 적는 절대형(rulings G8): 지금 값 = 새 값이면 Rejected(NoChange) — 로그·Seq·이벤트 없음. 시점·대상은 rulings G10.
    /// 적용 뒤 Legal이 바뀐 열린 선택은 새 Id로 다시 연다(ChoiceReissued).
    /// </summary>
    public static class MaintenanceRules
    {
        public static ActionKind ActionKindOf(MaintKind k)
        {
            switch (k)
            {
                case MaintKind.PartyOrder: return ActionKind.PartyOrder;
                case MaintKind.SetMove: return ActionKind.SetMove;
                case MaintKind.Release: return ActionKind.Release;
                case MaintKind.Afflict:
                case MaintKind.CatchSize:
                case MaintKind.Convert:
                case MaintKind.Adopt:
                case MaintKind.PassMove:
                case MaintKind.AttachBreach: return ActionKind.UseAbility;
                case MaintKind.UseBlessing: return ActionKind.Blessing;
                default: return ActionKind.HoldItem;
            }
        }

        public static SubmitResult Submit(GameState s, Ctx ctx, int seat, Maintenance m, IEventSink sink)
        {
            if (m == null || seat < 0 || seat >= s.SeatCount) return SubmitResult.Rejected(RejectReason.BadCommand);
            var reason = Check(s, ctx, seat, m);
            if (reason != RejectReason.None) return SubmitResult.Rejected(reason);
            var before = ChoiceRules.Fingerprints(s, ctx);
            var fx = new Emitter(s, sink);
            fx.Begin(seat, ActionKindOf(m.Kind), AutoKind.Human);
            Apply(s, ctx, fx, seat, m);
            if (UbBlessing.Voting(s))
            {
                // 아르세우스의 축복 동의 묶음을 열었다(여럿 판 — 결정 84 U1): 맵 행동을 거두고 묻는다(열린 선택이 바뀐 것은 일부러다). 셈이 끝나면 맵 행동을 다시 연다
                s.CommandCount++;
                return SubmitResult.Ok;
            }
            StampRules.Sweep(s, ctx, fx); // 지니게 하기로 채운 스탬프 5 등(rulings A30)
            ChoiceRules.ReissueChanged(s, ctx, fx, before);
            TrainerRules.ScoopAfterCommand(s, ctx, fx); // interviewers B: 정비로 받은 스탬프(rulings A38)
            TrainerRules.UnlockAfterCommand(s, ctx, fx); // 정비(진화 등)로 풀린 「When unlocked」 물음(rulings A40)
            s.CommandCount++;
            return SubmitResult.Ok;
        }

        /// <summary>검사 순서: 대상 모양(내 파티) → 지금 값 = 새 값이면 NoChange(G8) → 시점 등급(01 §3-4, G10) → 그 행동 한 번으로 갈 수 있는 값인가.</summary>
        public static RejectReason Check(GameState s, Ctx ctx, int seat, Maintenance m)
        {
            if (m.Kind == MaintKind.UseBlessing) return UbBlessing.Check(s, m); // 아르세우스의 축복(울트라 버스트 — 내 파티와 상관없다)
            if (m.Kind == MaintKind.AttachBreach) return BreachRules.CheckStored(s, seat, m); // pokemon_ranger B: 보관한 균열 카드 붙이기(결정 87 R4)
            var st = s.Seats[seat];
            switch (m.Kind)
            {
                case MaintKind.PartyOrder:
                {
                    var now = PartyList(st);
                    if (!SameMultiset(now, m.Values)) return RejectReason.Illegal;
                    if (SameSequence(now, m.Values)) return RejectReason.NoChange;
                    return TimingCheck(s, seat, m);
                }
                case MaintKind.SetMove:
                {
                    if (PokemonRules.PartyIndexOf(st, m.Pokemon) < 0 || m.Slot < 0 || m.Slot >= PokemonState.MoveSlots) return RejectReason.Illegal;
                    var p = s.Pokemon[m.Pokemon];
                    if (BreachRules.Blocked(p, m.Slot)) return RejectReason.Illegal; // 균열 카드가 막은 칸(rulings A52 ①)
                    if (p.Moves[m.Slot] == m.Value) return RejectReason.NoChange;
                    var t = TimingCheck(s, seat, m);
                    if (t != RejectReason.None) return t;
                    if (m.Value == -1) return RejectReason.None; // 버리기
                    if (m.Value < 0 || m.Value >= ctx.Cards.Moves.Count) return RejectReason.Illegal;
                    bool snap = !st.StampMoves.Contains(m.Value);
                    if (snap && TrainerRules.SnapOf(s, ctx, seat, m.Value) < 0) return RejectReason.Illegal; // tourist R: 전설 스냅도 가르친다(rulings A41)
                    if ((snap || s.Options.StampMoveTypeLimit) && !ChoiceRules.CanLearn(ctx, p, ctx.Cards.Moves[m.Value].Type)) return RejectReason.Illegal; // G10 U15-2 · 스냅은 「that can learn them」
                    return RejectReason.None;
                }
                case MaintKind.Release:
                {
                    // G8: 대상 확인 값 = 종(Value). 파티에 없거나 그 Id의 종이 다르면 이미 놓아준 것 — Id는 다음 포켓몬이 다시 쓴다
                    if (m.Value < 0) return RejectReason.BadCommand;
                    if (PokemonRules.PartyIndexOf(st, m.Pokemon) < 0 || s.Pokemon[m.Pokemon].Def != m.Value) return RejectReason.NoChange;
                    if (BreachRules.Any(s.Pokemon[m.Pokemon])) return RejectReason.Illegal; // p.24 「Any action that leads to a Distortion Breach card leaving your party is not allowed」
                    var t = TimingCheck(s, seat, m);
                    if (t != RejectReason.None) return t;
                    return PokemonRules.PartyCount(st) >= 2 ? RejectReason.None : RejectReason.Illegal; // 마지막 1마리는 안 된다(p.27)
                }
                case MaintKind.HoldItem:
                {
                    if (PokemonRules.PartyIndexOf(st, m.Pokemon) < 0) return RejectReason.Illegal;
                    if (m.Value != -1 && (m.Value < 0 || m.Value >= ctx.Items.Items.Count || (ctx.Items.Items[m.Value].Kind != ItemKind.Held && !RelicRules.IsRelic(ctx, m.Value) && !TrainerRules.Holdable(ctx, m.Value)))) return RejectReason.Illegal; // 대타출동은 전용 아이템이지만 지닌물건(rulings A41)
                    var p = s.Pokemon[m.Pokemon];
                    if (TrainerRules.Bound(ctx, m.Slot == 1 ? p.HeldItem2 : p.HeldItem)) return RejectReason.Illegal; // 드래곤의 정점 「cannot be unequipped」(rulings A41)
                    if (p.Alpha) return RejectReason.Illegal; // 우두머리 토큰이 지닌물건 칸을 막는다(A17 · 원작 커뮤니티 확인)
                    // backpacker R: Slot 1 = 둘째 칸(「up to two Held Items so long as they are not the same item」, rulings A39)
                    if (m.Slot > 1 || (m.Slot == 1 && !TrainerRules.Pockets(s, ctx, seat))) return RejectReason.Illegal;
                    if ((m.Slot == 1 ? p.HeldItem2 : p.HeldItem) == m.Value) return RejectReason.NoChange;
                    if (m.Value >= 0 && (m.Slot == 1 ? p.HeldItem : p.HeldItem2) == m.Value) return RejectReason.Illegal;
                    var t = TimingCheck(s, seat, m);
                    if (t != RejectReason.None) return t;
                    if (m.Value == -1) return RejectReason.None;
                    return st.Bag.Contains(m.Value) || HolderOf(s, st, m.Value, m.Pokemon) >= 0 ? RejectReason.None : RejectReason.Illegal;
                }
                case MaintKind.ChangeForm:
                {
                    // 유물 폼(p.6 「You may change to the form specified by the item whilst outside of battle」, FormRules)
                    if (PokemonRules.PartyIndexOf(st, m.Pokemon) < 0) return RejectReason.Illegal;
                    var p = s.Pokemon[m.Pokemon];
                    if (p.Def == m.Value) return RejectReason.NoChange;
                    var t = TimingCheck(s, seat, m);
                    if (t != RejectReason.None) return t;
                    return FormRules.ItemFormAllowed(ctx, p, m.Value) ? RejectReason.None : RejectReason.Illegal;
                }
                case MaintKind.Afflict:
                    return TrainerRules.CheckAfflict(s, ctx, seat, m);
                case MaintKind.CatchSize:
                    return TrainerRules.CheckCatchSize(s, ctx, seat, m);
                case MaintKind.Convert:
                case MaintKind.Adopt:
                case MaintKind.PassMove:
                {
                    var r = TrainerRules.CheckCraftMaint(s, ctx, seat, m);
                    return r != RejectReason.None ? r : (m.Kind == MaintKind.PassMove ? RejectReason.None : TimingCheck(s, seat, m));
                }
                default:
                    return RejectReason.BadCommand;
            }
        }

        /// <summary>그 좌석이 진행 중인 배틀 한쪽의 주인이다(rulings G10 「배틀 참가 중」).</summary>
        public static bool InOwnBattle(GameState s, int seat) =>
            s.Battle != null && (s.Battle.Sides[0].Owner == seat || s.Battle.Sides[1].Owner == seat);

        /// <summary>그 좌석 포켓몬의 공격이 처리 중(배틀 턴이 그 좌석 쪽, Step PreRoll~ApplyDamage · 디아루가 창을 기다리는 포획 굴림 Capture — 결정 76 ③).</summary>
        public static bool OwnAttackProcessing(GameState s, int seat)
        {
            var b = s.Battle;
            return b != null && b.ActiveSide >= 0 && b.Sides[b.ActiveSide].Owner == seat
                && (b.Step >= BattleStep.PreRoll && b.Step <= BattleStep.ApplyDamage || b.Step == BattleStep.Capture);
        }

        static RejectReason TimingCheck(GameState s, int seat, Maintenance m)
        {
            bool inBattle = InOwnBattle(s, seat);
            switch (Maintenance.TimingOf(m.Kind))
            {
                case MaintTiming.Always:
                    if (inBattle && OwnAttackProcessing(s, seat)) return RejectReason.Timing;
                    if (m.Kind == MaintKind.SetMove && inBattle && s.Options.TeachInBattle == TeachInBattle.OwnBattleTurnStartOnly)
                    {
                        var b = s.Battle;
                        bool turnStart = b.Step == BattleStep.SelectAction && b.ActiveSide >= 0 && b.Sides[b.ActiveSide].Owner == seat;
                        if (!turnStart) return RejectReason.Timing;
                    }
                    return RejectReason.None;
                default: // NotInOwnBattle · OutsideBattle
                    if (inBattle) return RejectReason.Timing;
                    // 놓아주기: 어떤 포켓몬도 배틀 중에는 놓아줄 수 없다 — 남의 배틀이 도는 동안도(사용자 결정 84 T10 ③ 나 + 메모 「모든 포켓몬은 배틀중에 놓아줄수 없어」 ·
                    // 원작 커뮤니티 확인). 배틀이 끝난 뒤(BattleStep.End)의 파티 가득 놓아주기(S51)는 선택 답이라 여기를 지나지 않는다
                    if (m.Kind == MaintKind.Release && s.Battle != null && s.Battle.Step != BattleStep.End) return RejectReason.Timing;
                    return m.Kind == MaintKind.Release && BattleChoiceOpen(s, seat) ? RejectReason.Timing : RejectReason.None;
            }
        }

        /// <summary>
        /// 놓아주기를 막는 열린 선택(rulings G10): 그 좌석의 조우 고르기, 그 좌석이 걸었거나 받은 도전 응답, 그 좌석의 파티 가득 놓아주기(S51).
        /// 앞 둘은 「쓸 포켓몬 있음」을 보고 열렸고 Legal이 파티와 무관해 다시 열리지 않는다 — 성한 마지막 포켓몬을 놓아주면 답한 뒤 배틀을 시작할 수 없다.
        /// </summary>
        public static bool BattleChoiceOpen(GameState s, int seat)
        {
            foreach (var c in s.Pending)
            {
                if (c.Kind == ChoiceKind.EncounterPick && c.Seat == seat) return true;
                if (c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Challenge && (c.Seat == seat || c.Subject == seat)) return true;
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.PartyFull && c.Seat == seat) return true;
            }
            return false;
        }

        static void Apply(GameState s, Ctx ctx, Emitter fx, int seat, Maintenance m)
        {
            if (m.Kind == MaintKind.UseBlessing)
            {
                UbBlessing.Apply(s, ctx, fx, seat, m);
                return;
            }
            if (m.Kind == MaintKind.AttachBreach)
            {
                BreachRules.AttachStored(s, fx, seat, m);
                return;
            }
            var st = s.Seats[seat];
            switch (m.Kind)
            {
                case MaintKind.PartyOrder:
                {
                    var before = PartyList(st);
                    for (int i = 0; i < st.Party.Length; i++) st.Party[i] = i < m.Values.Count ? m.Values[i] : -1;
                    if (fx.On) fx.Add(new PartyReordered { Before = before.ToArray(), After = m.Values.ToArray() });
                    break;
                }
                case MaintKind.SetMove:
                {
                    var p = s.Pokemon[m.Pokemon];
                    int old = p.Moves[m.Slot];
                    p.Moves[m.Slot] = m.Value;
                    EvolveRules.Forgot(s, p, old); // 그 기술 때문에 밑에 둔 옛 카드는 버린다(p.5)
                    TrainerRules.Pooled(s, ctx, fx, seat, old); // collector R: 버린 기술은 스탬프 보상 풀로(rulings A39)
                    if (m.Value >= 0)
                    {
                        if (!st.StampMoves.Remove(m.Value)) st.Snaps.RemoveAt(TrainerRules.SnapOf(s, ctx, seat, m.Value)); // 풀에서 하나 꺼낸다(없으면 tourist R의 스냅 — 가르치면 쓴다). 밀려난 기술은 버린다(G10)
                        if (fx.On) fx.Add(new MoveTaught { PokemonId = m.Pokemon, Slot = m.Slot, MoveKey = m.Value, ReplacedKey = old });
                    }
                    else if (fx.On) fx.Add(new MoveForgotten { PokemonId = m.Pokemon, Slot = m.Slot, MoveKey = old });
                    break;
                }
                case MaintKind.Release:
                    Release(s, ctx, fx, seat, m.Pokemon);
                    break;
                case MaintKind.HoldItem:
                    Hold(s, ctx, fx, seat, m.Pokemon, m.Value, m.Slot == 1 ? 1 : 0);
                    break;
                case MaintKind.Afflict:
                    TrainerRules.Afflict(s, ctx, fx, seat, m);
                    break;
                case MaintKind.CatchSize:
                    TrainerRules.GrowCatch(s, ctx, fx, seat, m);
                    break;
                case MaintKind.Convert:
                case MaintKind.Adopt:
                case MaintKind.PassMove:
                    TrainerRules.ApplyCraftMaint(s, ctx, fx, seat, m);
                    break;
                case MaintKind.ChangeForm:
                {
                    int held = s.Pokemon[m.Pokemon].HeldItem;
                    FormRules.Change(s, ctx, fx, m.Pokemon, m.Value, held >= 0 ? new SourceRef(SourceKind.Item, held) : SourceRef.None);
                    break;
                }
            }
        }

        /// <summary>지니게 하기(정비 HoldItem · ruin_maniac B의 「attach it to a Pokémon」): item을 가방(없으면 그것을 든 다른 포켓몬)에서 꺼내 지니게 하고, 지니던 것은 가방으로. item −1 = 내려놓기.</summary>
        internal static void Hold(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, int item, int slot = 0)
        {
            var st = s.Seats[seat];
            var p = s.Pokemon[pokemon];
            int old = slot == 1 ? p.HeldItem2 : p.HeldItem;
            if (TrainerRules.Bound(ctx, old)) return; // 드래곤의 정점 칸에는 다른 것을 지니지 못한다(ruin_maniac B의 붙이기 — 가방에 남는다, rulings A41)
            int from = -1;
            if (item >= 0)
            {
                if (!st.Bag.Remove(item))
                {
                    from = HolderOf(s, st, item, pokemon);
                    PokemonRules.DropItem(s.Pokemon[from], item);
                }
            }
            if (old >= 0)
            {
                st.Bag.Add(old);
                if (fx.On) fx.Add(new ItemHeld { PokemonId = -1, ItemKey = old, FromPokemonId = pokemon });
            }
            if (slot == 1) p.HeldItem2 = item;
            else p.HeldItem = item;
            if (item >= 0 && fx.On) fx.Add(new ItemHeld { PokemonId = pokemon, ItemKey = item, FromPokemonId = from });
            // 유물을 더 지니지 않은 유물 폼은 기본 폼으로(p.6 「If the Pokémon is no longer holding the item, it returns to its default form」)
            FormRules.HeldChanged(s, ctx, fx, pokemon);
            FormRules.HeldChanged(s, ctx, fx, from);
        }

        /// <summary>파티 순서로 그 아이템을 든 다른 포켓몬. 없으면 −1.</summary>
        /// <summary>
        /// 놓아주기(rulings G10): 소지 아이템은 가방으로, 카드는 뽑아 온 덱의 버림 더미로(우두머리 토큰은 함께 버림), 배운 기술은 버린다.
        /// 파티에 없는 포켓몬(포획 뒤 파티 가득, rulings G13)도 같다. 정비와 BattleFlow가 부른다.
        /// </summary>
        public static void Release(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            var st = s.Seats[seat];
            var p = s.Pokemon[pokemon];
            TrainerRules.Released(s, pokemon); // gentleman R: 프레셔스 볼이 돌아온다(rulings A41 — 배틀 중에는 놓아주지 못한다, 결정 84 T10 ③)
            foreach (int held in new[] { p.HeldItem, p.HeldItem2 })
            {
                if (held < 0 || TrainerRules.Bound(ctx, held)) continue; // 드래곤의 정점은 가방으로 가지 않는다(rulings A41)
                st.Bag.Add(held);
                if (fx.On) fx.Add(new ItemHeld { Seat = seat, PokemonId = -1, ItemKey = held, FromPokemonId = pokemon });
            }
            var moves = new List<int>(PokemonState.MoveSlots);
            foreach (int mv in p.Moves)
                if (mv >= 0) moves.Add(mv);
            int species = p.Def;
            // collector R: 놓아준 포켓몬은 카드째 스탬프 보상 풀로(턴에 한 장이 기본 — 문구대로면 배운 기술도, rulings A39)
            bool pooled = TrainerRules.Pooled(s, ctx, fx, seat, ctx.Cards.Pokemon[p.Def].Signature, p.Def, p.Deck);
            if (pooled) foreach (int mv in moves) TrainerRules.Pooled(s, ctx, fx, seat, mv);
            if (p.Deck >= 0 && !pooled) s.Discards[p.Deck].Add(p.Def);
            EvolveRules.DiscardUnder(s, p);
            PokemonRules.RemoveFromParty(st, pokemon);
            PokemonRules.Free(s, pokemon);
            if (fx.On) fx.Add(new Released { Seat = seat, PokemonId = pokemon, Species = species, MovesToDiscard = moves.ToArray() });
        }

        static int HolderOf(GameState s, SeatState st, int item, int except)
        {
            foreach (int q in st.Party)
                if (q >= 0 && q != except && PokemonRules.HoldsItem(s.Pokemon[q], item)) return q;
            return -1;
        }

        static List<int> PartyList(SeatState st)
        {
            var r = new List<int>(st.Party.Length);
            foreach (int p in st.Party)
                if (p >= 0) r.Add(p);
            return r;
        }

        static bool SameSequence(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static bool SameMultiset(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            var x = new List<int>(a);
            var y = new List<int>(b);
            x.Sort();
            y.Sort();
            return SameSequence(x, y);
        }
    }
}
