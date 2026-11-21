using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>왜곡 균열 카드 여섯 종류(cards.json `distortion_breach.*` — 종류마다 6장, 덱 36장).</summary>
    public enum BreachKind { None, RecklessAssault, RapidStrikes, GravitationalDrag, NimbleFrame, ChromaticUnion, StatusOverload }

    /// <summary>
    /// 왜곡 균열 카드(룰북 p.24, rulings A52 — M6 「균열 카드 36장」). 받은 카드는 좌석의 손(UbState.BreachHands, UbTurn.DealBreachCards)에 있다가 파티 포켓몬의 배운 기술 칸에 붙는다
    /// (PokemonState.Breach — 그 칸의 기술을 막는다, 원작 커뮤니티 확인).
    /// - 붙이기(강제 — p.24 「must attach」): 턴이 시작될 때마다 손에 카드가 있고 붙일 칸이 있는 좌석 모두에게 한꺼번에 묻는다(Simultaneous/BreachAttach, 공개 — 좌석마다 손의 첫 카드부터 한 장씩,
    ///   묶음이 다 답하면 남은 카드로 다시). 붙일 칸 = 내 파티 포켓몬의 균열 카드가 없는 기술 칸 — 빈 칸이 다른 포켓몬에 있어도 가르친 기술을 버리고 붙일 수 있고, 빈 칸이 없으면 반드시 기술 하나를 버린다
    ///   (원작 커뮤니티 확인
    /// 원작 커뮤니티 확인 — v1.3 답 [가정 A52 ②]). 모든 칸이 균열 카드면 손에 든 채 기다린다(p.24 「keep the card in your hand and attach it as soon as possible」 — 다음 턴 시작에 다시 본다 [가정 A52 ③]).
    ///   시간 초과 기본 답 = 빈 칸이 있으면 첫 빈 칸, 없으면 첫 합법 답(강제 고르기라 「안 함」이 없다 — A52 ④).
    /// - 파티를 떠나게 하는 행동 불가(p.24): 놓아주기(정비 · 파티 가득) · 거래 · 동등한 교환. 진화는 된다(카드는 그대로 — 원작 커뮤니티 확인).
    /// - UB를 쓰러뜨리면 모든 좌석이 1장을 버리거나 자기 파티 안에서 옮길 수 있다(<see cref="OpenRelease"/> — p.24 · 원작 커뮤니티 확인).
    ///   손에 든 카드 · pokemon_ranger B가 보관한 카드도 버릴 수 있다 — 옮기기는 붙은 카드만(사용자 결정 84 U10 나 · U11).
    /// - pokemon_ranger B(울트라 버스트 「you may store up to 2 Distortion Breach cards in each empty slot」, rulings A42): 붙이기 물음에서 보관(Decline — 첫 답·기본)을 고를 수 있다(빈 칸 × 2장까지, 사용자 결정 84 U11 나).
    ///   보관한 카드(UbState.BreachStored)는 다시 묻지 않는다 — 내 턴 · 배틀 밖의 정비(MaintKind.AttachBreach)로 언제든 꺼내 붙일 수 있다(사용자 결정 87 R4 — 막는 문구·제작자 답이 없다, rulings A44 ③).
    ///   칸이 차서 보관할 자리가 줄면 넘친 것이 손으로 돌아와 붙여야 한다.
    ///   네크로즈마 페이즈 격파로는 없다(원작 커뮤니티 확인) — 부르는 곳이 UB 격파뿐이다.
    /// - 효과(영문 카드 문구, rulings A15 · A52 ⑤): 한 포켓몬에 둘까지 붙고(원작 커뮤니티 확인) 같은 카드 둘은 겹친다 [가정].
    ///   reckless_assault 「When the user attacks or is attacked, positive type effectiveness damage is increased by 2」 = 상성 보너스가 양수인 공격에 +2(공격·피격 — <see cref="Reckless"/>).
    ///   rapid_strikes 「always goes first but reduces its moves with greater than 3 total attack strength to 3」 = 순서 비교 없이 선턴(둘 다면 보통 비교), 보정 뒤 공격력 3 초과는 3.
    ///   gravitational_drag 「Reduce the initiative of the user by 3 but increase the base health of the user by 3」 = 우선도 −3 · 최대 체력 +3.
    ///   nimble_frame 「When the user attacks, it takes 1 damage, but may switch out afterwards. Does not affect the current turn order」 = 공격 피해와 함께 자기 1 피해 → 턴 끝 뒤 교체 창(순서 그대로).
    ///   chromatic_union 「The moves of the user are now Typeless, but their attack strengths are increased by 1」 = 쓰는 기술이 모두 타입없음(고른 타입 포함) · 공격력 +1.
    ///   status_overload 「When the user switches in, roll a status dice and inflict the result on both the user and the target」 = 나올 때(첫 출전 포함) 상태 주사위 하나 → 둘 다에게(상대가 아직 없으면 나온 뒤).
    /// </summary>
    public static class BreachRules
    {
        public const int RecklessBonus = 2;
        public const int RapidCap = 3;
        public const int DragPriority = -3;
        public const int DragHealth = 3;
        public const int NimbleDamage = 1;
        public const int ChromaticPower = 1;

        /// <summary>PendingChoice.Subject(Simultaneous/BreachAttach): 붙이기(그 밖의 값) · UB 격파 뒤 버리기·옮기기(<see cref="Release"/>).</summary>
        public const int Release = 1;

        static readonly string[] Keys =
        {
            null, "distortion_breach.reckless_assault", "distortion_breach.rapid_strikes", "distortion_breach.gravitational_drag",
            "distortion_breach.nimble_frame", "distortion_breach.chromatic_union", "distortion_breach.status_overload",
        };

        /// <summary>그 종류의 카드 색인(카드 표).</summary>
        public static int CardOf(Ctx ctx, BreachKind kind) => ctx.Decks.CardIds.IndexOf(Keys[(int)kind]);

        public static BreachKind KindOf(Ctx ctx, int card)
        {
            if (card < 0) return BreachKind.None;
            for (int k = 1; k < Keys.Length; k++)
                if (ctx.Decks.CardIds.IndexOf(Keys[k]) == card) return (BreachKind)k;
            return BreachKind.None;
        }

        /// <summary>균열 카드가 붙어 있나.</summary>
        public static bool Any(PokemonState p) => p.Breach[0] >= 0 || p.Breach[1] >= 0;

        /// <summary>붙은 균열 카드 수(0~2).</summary>
        public static int Attached(PokemonState p) => (p.Breach[0] >= 0 ? 1 : 0) + (p.Breach[1] >= 0 ? 1 : 0);

        /// <summary>그 종류의 카드가 몇 장 붙었나.</summary>
        public static int Count(Ctx ctx, PokemonState p, BreachKind kind)
        {
            if (!Any(p)) return 0;
            int card = CardOf(ctx, kind), n = 0;
            foreach (int c in p.Breach)
                if (c == card) n++;
            return n;
        }

        static SourceRef Source(Ctx ctx, BreachKind kind) => new SourceRef(SourceKind.BreachCard, CardOf(ctx, kind));

        // ── 효과 ──

        /// <summary>gravitational_drag: 최대 체력 +3(장마다).</summary>
        public static int HealthBonus(Ctx ctx, PokemonState p) => Any(p) ? DragHealth * Count(ctx, p, BreachKind.GravitationalDrag) : 0;

        /// <summary>gravitational_drag: 우선도 −3(장마다 — BattleRules.PriorityMods).</summary>
        public static void PriorityMods(Ctx ctx, PokemonState p, List<Mod> into)
        {
            for (int n = Count(ctx, p, BreachKind.GravitationalDrag); n > 0; n--)
                into.Add(new Mod { Source = Source(ctx, BreachKind.GravitationalDrag), Delta = DragPriority });
        }

        /// <summary>chromatic_union: 공격력 +1(장마다 — BattleRules.PowerMods).</summary>
        public static void PowerMods(Ctx ctx, PokemonState attacker, List<Mod> into)
        {
            for (int n = Count(ctx, attacker, BreachKind.ChromaticUnion); n > 0; n--)
                into.Add(new Mod { Source = Source(ctx, BreachKind.ChromaticUnion), Delta = ChromaticPower });
        }

        /// <summary>chromatic_union: 그 포켓몬이 쓰는 기술은 타입없음.</summary>
        public static bool Typeless(Ctx ctx, PokemonState attacker) => Count(ctx, attacker, BreachKind.ChromaticUnion) > 0;

        /// <summary>rapid_strikes: 보정 뒤 공격력이 3을 넘으면 3(BattleRules.RollPower 끝 — 줄인 만큼을 mods에 적는다).</summary>
        public static int CapPower(Ctx ctx, PokemonState attacker, int power, List<Mod> mods)
        {
            if (power <= RapidCap || Count(ctx, attacker, BreachKind.RapidStrikes) == 0) return power;
            mods.Add(new Mod { Source = Source(ctx, BreachKind.RapidStrikes), Delta = RapidCap - power });
            return RapidCap;
        }

        /// <summary>rapid_strikes: 늘 먼저 가는 쪽(0·1). 둘 다 가졌거나 아무도 없으면 −1(보통 순서 비교).</summary>
        public static int GoesFirst(GameState s, Ctx ctx, BattleState b)
        {
            bool r0 = b.Sides[0].Active >= 0 && Count(ctx, s.Pokemon[b.Sides[0].Active], BreachKind.RapidStrikes) > 0;
            bool r1 = b.Sides[1].Active >= 0 && Count(ctx, s.Pokemon[b.Sides[1].Active], BreachKind.RapidStrikes) > 0;
            return r0 == r1 ? -1 : r0 ? 0 : 1;
        }

        /// <summary>reckless_assault: 상성 보너스가 양수인 공격이면 공격한 쪽·받는 쪽의 카드마다 +2(상성 몫에 더한다 — 2배 전).</summary>
        public static int Reckless(Ctx ctx, PokemonState attacker, PokemonState defender, int matchup)
        {
            if (matchup <= 0) return 0;
            return RecklessBonus * (Count(ctx, attacker, BreachKind.RecklessAssault) + Count(ctx, defender, BreachKind.RecklessAssault));
        }

        /// <summary>reckless_assault 몫의 출처(피해 계산 줄의 상성 칸을 표 상성과 나눠 보인다 — 결정 194).</summary>
        public static SourceRef RecklessSource(Ctx ctx) => Source(ctx, BreachKind.RecklessAssault);

        /// <summary>nimble_frame(BattleFlow.DamageStep — 공격 피해를 버퍼에 넣은 뒤): 공격한 포켓몬이 1 피해(장마다, 같은 묶음으로 적용) · 턴 끝 뒤 교체할 수 있다(BattleState.SwitchAfter — 순서 그대로).</summary>
        internal static void Attacked(GameState s, Ctx ctx, BattleState b, int attacker)
        {
            int n = Count(ctx, s.Pokemon[attacker], BreachKind.NimbleFrame);
            if (n == 0) return;
            BattleFlow.Buffer(b, attacker, -NimbleDamage * n, Source(ctx, BreachKind.NimbleFrame));
            b.SwitchAfter = attacker;
        }

        /// <summary>
        /// status_overload(BattleFlow.SendIn 끝): 나온 포켓몬의 「나올 때」가 남아 있고(BattleSide.Entered) 양쪽이 다 나와 있으면 카드마다 상태 주사위 하나를 굴려(게임 난수) 그 포켓몬과 상대 출전 포켓몬에게 건다
        /// (StatusApplied, Source = 그 카드 — 면역·이미 걸림은 결과로). 상대가 아직 없으면(첫 출전의 도전자 · 함께 쓰러진 뒤) 상대가 나올 때 한다 — 그때 둘 다 남았으면 도전자 쪽부터.
        /// </summary>
        internal static void SentIn(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var side = b.Sides[k];
                int user = side.Active, target = b.Sides[1 - k].Active;
                if (!side.Entered || user < 0 || target < 0) continue;
                side.Entered = false;
                var src = Source(ctx, BreachKind.StatusOverload);
                for (int n = Count(ctx, s.Pokemon[user], BreachKind.StatusOverload); n > 0; n--)
                {
                    var status = StatusRules.RollDie(s.Rng);
                    EffectRules.InflictOne(s, ctx, fx, user, status, src);
                    EffectRules.InflictOne(s, ctx, fx, target, status, src);
                }
            }
        }

        // ── 기술 칸 ──

        /// <summary>그 기술 칸을 균열 카드가 막고 있나(가르치기·정비가 본다).</summary>
        public static bool Blocked(PokemonState p, int slot) => p.Breach[slot] >= 0;

        /// <summary>그 좌석이 손에 든 균열 카드 가운데 첫 카드(받은 순서). 없으면 −1.</summary>
        public static int FirstInHand(GameState s, int seat)
        {
            if (s.Ub == null) return -1;
            var h = s.Ub.BreachHands;
            for (int i = 0; i + 1 < h.Count; i += 2)
                if (h[i] == seat) return h[i + 1];
            return -1;
        }

        /// <summary>pokemon_ranger B: 지금 한 장 더 보관할 수 있나(빈 칸 × 2 − 보관한 수 — rulings A42 · 결정 84 U11).</summary>
        public static bool CanStore(GameState s, Ctx ctx, int seat) => s.Ub != null && StoredCount(s, seat) < TrainerRules.BreachStorage(s, ctx, seat);

        /// <summary>그 좌석이 보관한 균열 카드 수(pokemon_ranger B — UbState.BreachStored).</summary>
        public static int StoredCount(GameState s, int seat)
        {
            int n = 0;
            var h = s.Ub != null ? s.Ub.BreachStored : null;
            for (int i = 0; h != null && i + 1 < h.Count; i += 2)
                if (h[i] == seat) n++;
            return n;
        }

        /// <summary>보관할 자리가 줄었으면(빈 칸에 포켓몬이 들어옴 · 카드를 잃음) 넘친 보관 카드를 늦게 보관한 것부터 손으로 돌린다 — 손의 카드는 붙여야 한다.</summary>
        static void Overflow(GameState s, Ctx ctx, int seat)
        {
            int room = TrainerRules.BreachStorage(s, ctx, seat);
            var h = s.Ub.BreachStored;
            for (int i = h.Count - 2; i >= 0 && StoredCount(s, seat) > room; i -= 2)
            {
                if (h[i] != seat) continue;
                s.Ub.BreachHands.Add(seat);
                s.Ub.BreachHands.Add(h[i + 1]);
                h.RemoveRange(i, 2);
            }
        }

        /// <summary>그 좌석의 파티에 균열 카드를 붙일 칸이 있나(균열 카드가 없는 기술 칸).</summary>
        public static bool CanAttach(GameState s, int seat)
        {
            foreach (int p in s.Seats[seat].Party)
                if (p >= 0 && Attached(s.Pokemon[p]) < PokemonState.MoveSlots) return true;
            return false;
        }

        // ── 붙이기 · UB 격파 뒤 버리기·옮기기 ──

        /// <summary>
        /// 붙이기 묶음을 연다(TurnFlow.StartTurn — 그 턴의 다른 선택보다 먼저): 손에 카드가 있고 붙일 칸이 있는 좌석마다 손의 첫 카드 하나(Options = [카드]). 탈락한 좌석은 건너뛴다 [가정 A52 ③].
        /// pokemon_ranger B 좌석에 보관할 자리가 남았으면(<see cref="CanStore"/>) 「보관」(Decline)이 첫 답·기본 답이다(사용자 결정 84 U11 나 — Legal이 그때 상태로 센다).
        /// 열었으면 true — 묶음이 다 답하면 <see cref="ApplyPicks"/>가 남은 카드로 다시 열거나 그 턴을 잇는다.
        /// </summary>
        internal static bool OpenAttach(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Ub == null) return false;
            for (int seat = 0; seat < s.SeatCount; seat++) Overflow(s, ctx, seat);
            if (s.Ub.BreachHands.Count == 0) return false;
            var group = new List<PendingChoice>();
            for (int seat = 0; seat < s.SeatCount; seat++)
            {
                int card = FirstInHand(s, seat);
                if (card < 0 || s.Seats[seat].Eliminated || !CanAttach(s, seat)) continue;
                var c = new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.BreachAttach, Seat = seat, Subject = 0, Source = new SourceRef(SourceKind.BreachCard, card) };
                c.Options.Add(card);
                group.Add(c);
            }
            if (group.Count == 0) return false;
            ChoiceRules.OpenGroup(s, ctx, fx, group);
            return true;
        }

        /// <summary>
        /// UB를 쓰러뜨렸다(p.24 「when an Ultra Beast is defeated, all trainers may discard or move 1 Distortion Breach card」): 균열 카드가 붙은 포켓몬이 있는 좌석 모두에게 한꺼번에 묻는다
        /// (Simultaneous/BreachAttach, Subject = <see cref="Release"/>, 공개). Options = 이어 갈 값([이긴 좌석, 가르칠 기술 …] — 묶음이 끝나면 BattleFlow.AfterRelease가 기술 가르치기·턴 끝을 잇는다).
        /// 버리기·옮기기와 UB의 기술 가르치기는 동시라(원작 커뮤니티 확인) 칸을 비운 뒤 그 기술을 가르칠 수 있게 이것을 먼저 묻는다.
        /// 손에 든(못 붙인) 카드 · 보관한 카드도 버릴 수 있다(사용자 결정 84 U10 나 · U11) — 옮기기는 붙은 카드만. 열었으면 true.
        /// </summary>
        public static bool OpenRelease(GameState s, Ctx ctx, Emitter fx, int seat, List<int> moves)
        {
            if (s.Ub == null) return false;
            var group = new List<PendingChoice>();
            for (int i = 0; i < s.SeatCount; i++)
            {
                if (s.Seats[i].Eliminated || (!HasAttached(s, i) && FirstInHand(s, i) < 0 && StoredCount(s, i) == 0)) continue;
                var c = new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.BreachAttach, Seat = i, Subject = Release, Source = new SourceRef(SourceKind.BreachCard, -1) };
                c.Options.Add(seat);
                c.Options.AddRange(moves);
                group.Add(c);
            }
            if (group.Count == 0) return false;
            ChoiceRules.OpenGroup(s, ctx, fx, group);
            return true;
        }

        static bool HasAttached(GameState s, int seat)
        {
            foreach (int p in s.Seats[seat].Party)
                if (p >= 0 && Any(s.Pokemon[p])) return true;
            return false;
        }

        /// <summary>
        /// 합법 답(ChoiceRules.Legal — 상태에서 읽는다). 붙이기: (보관할 수 있으면 Decline = 보관 — 첫 답) → Pick(포켓몬, 칸) — 파티 순서 × 칸 0~1, 균열 카드가 없는 칸.
        /// 버리기·옮기기: Decline(안 함 — 첫 답) → 붙은 카드마다(파티 순서 × 칸) Pick(포켓몬, 칸) = 버리기 → 그 카드를 옮길 자리마다 Pick(포켓몬, 칸) + Values [받는 포켓몬, 칸]
        /// (같은 파티의 다른 포켓몬의 균열 카드가 없는 칸 — 가르친 기술이 있으면 버리고 붙는다) → 손·보관의 카드 종류마다 Pick(<see cref="HandTarget"/>, 카드) = 그 카드 한 장 버리기(손 먼저).
        /// </summary>
        public static void Legal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            var party = s.Seats[c.Seat].Party;
            if (c.Subject != Release)
            {
                if (CanStore(s, ctx, c.Seat)) into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // pokemon_ranger B: 보관(결정 84 U11)
                foreach (int p in party)
                    for (int slot = 0; p >= 0 && slot < PokemonState.MoveSlots; slot++)
                        if (!Blocked(s.Pokemon[p], slot)) into.Add(ChoiceAnswer.Pick(p, slot));
                return;
            }
            into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
            foreach (int p in party)
                for (int slot = 0; p >= 0 && slot < PokemonState.MoveSlots; slot++)
                {
                    if (!Blocked(s.Pokemon[p], slot)) continue;
                    into.Add(ChoiceAnswer.Pick(p, slot));
                    foreach (int q in party)
                        for (int to = 0; q >= 0 && q != p && to < PokemonState.MoveSlots; to++)
                        {
                            if (Blocked(s.Pokemon[q], to)) continue;
                            var a = ChoiceAnswer.Pick(p, slot);
                            a.Values.Add(q);
                            a.Values.Add(to);
                            into.Add(a);
                        }
                }
            var loose = new List<int>();
            foreach (var pile in new[] { s.Ub.BreachHands, s.Ub.BreachStored })
                for (int i = 0; i + 1 < pile.Count; i += 2)
                    if (pile[i] == c.Seat && !loose.Contains(pile[i + 1])) loose.Add(pile[i + 1]);
            foreach (int card in loose) into.Add(ChoiceAnswer.Pick(HandTarget, card));
        }

        /// <summary>버리기 답 Pick의 Target: 손·보관의 카드(Arg = 카드 색인 — 붙은 카드는 Target = 포켓몬).</summary>
        public const int HandTarget = -1;

        /// <summary>시간 초과 기본 답(A52 ④): 붙이기 = 보관할 수 있으면 보관(결정 84 U11), 아니면 빈 기술 칸이 있으면 첫 빈 칸(가르친 기술을 버리지 않는다), 없으면 첫 합법 답 · 버리기·옮기기 = 안 함.</summary>
        public static ChoiceAnswer Default(GameState s, PendingChoice c, List<ChoiceAnswer> legal)
        {
            if (c.Subject != Release && legal[0].Kind != AnswerKind.Decline)
                foreach (var a in legal)
                    if (s.Pokemon[a.Target].Moves[a.Arg] < 0) return a;
            return legal[0];
        }

        /// <summary>균열 카드 묶음인가(Engine.Answer — 묶음이 다 답했을 때).</summary>
        public static bool IsGroup(List<PendingChoice> members) =>
            members.Count > 0 && members[0].Kind == ChoiceKind.Simultaneous && members[0].Sub == ChoiceSub.BreachAttach;

        /// <summary>
        /// 묶음의 답을 연 순서(좌석 순서)대로 적용한다. 붙이기: 남은 손 카드로 묶음을 다시 열거나(없으면) 그 턴을 잇는다(TurnFlow.OpenTurn).
        /// 버리기·옮기기: 배틀 뒤 흐름을 잇는다(BattleFlow.AfterRelease).
        /// </summary>
        internal static void ApplyPicks(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> members)
        {
            bool release = members[0].Subject == Release;
            foreach (var m in members)
            {
                if (release) ApplyRelease(s, ctx, fx, m, m.Answer);
                else if (m.Answer.Kind == AnswerKind.Decline) Store(s, m.Seat, m.Options[0]); // pokemon_ranger B: 보관(결정 84 U11)
                else Attach(s, ctx, fx, m.Seat, m.Options[0], m.Answer.Target, m.Answer.Arg);
            }
            if (release)
            {
                var o = members[0].Options;
                BattleFlow.AfterRelease(s, ctx, fx, o[0], o.GetRange(1, o.Count - 1));
                return;
            }
            if (OpenAttach(s, ctx, fx)) return;
            if (s.Phase == Phase.Turn && s.ActiveSeat >= 0) TurnFlow.OpenTurn(s, ctx, fx, s.ActiveSeat);
        }

        /// <summary>
        /// 손의 그 카드를 그 포켓몬의 그 칸에 붙인다. 그 칸의 가르친 기술은 버린다(MoveForgotten 없이 BreachCardAttached.ReplacedKey로 알린다 — 그 기술 때문에 밑에 둔 옛 카드도 버린다).
        /// 전설 특수기술은 배운 칸 수로 덮이므로(LegendRules — 균열 카드도 센다) 첫 카드가 알파 스트라이크를, 둘째가 비스트 부스트를 덮는다(원작 커뮤니티 확인).
        /// </summary>
        public static void Attach(GameState s, Ctx ctx, Emitter fx, int seat, int card, int pokemon, int slot)
        {
            var h = s.Ub != null ? s.Ub.BreachHands : null;
            for (int i = 0; h != null && i + 1 < h.Count; i += 2)
                if (h[i] == seat && h[i + 1] == card)
                {
                    h.RemoveRange(i, 2);
                    break;
                }
            int old = Put(s, s.Pokemon[pokemon], slot, card);
            if (fx.On) fx.Add(new BreachCardAttached { Seat = seat, PokemonId = pokemon, Slot = slot, CardKey = card, ReplacedKey = old });
        }

        /// <summary>손의 그 카드를 보관한다(pokemon_ranger B — 공개 상태 UbState.BreachStored, 따로 이벤트는 없다: 붙이기 답 Decline이 알린다).</summary>
        static void Store(GameState s, int seat, int card)
        {
            if (!Take(s.Ub.BreachHands, seat, card)) return;
            s.Ub.BreachStored.Add(seat);
            s.Ub.BreachStored.Add(card);
        }

        /// <summary>
        /// 정비 명령 AttachBreach의 검사(MaintenanceRules.Check — 사용자 결정 87 R4): 울트라 버스트 판 · 그 좌석이 그 카드를 보관하고 있다 · 붙일 칸 = 내 파티 포켓몬의 균열 카드가 없는 기술 칸(붙이기 규칙 그대로) ·
        /// 때 = 내 턴 · 배틀과 효과 처리 밖 · 균열 카드 묶음(붙이기 · 버리기·옮기기)이 열려 있지 않을 때.
        /// </summary>
        public static RejectReason CheckStored(GameState s, int seat, Maintenance m)
        {
            if (s.Ub == null) return RejectReason.Illegal;
            bool has = false;
            var h = s.Ub.BreachStored;
            for (int i = 0; i + 1 < h.Count; i += 2)
                if (h[i] == seat && h[i + 1] == m.Value) has = true;
            if (!has) return RejectReason.Illegal;
            if (PokemonRules.PartyIndexOf(s.Seats[seat], m.Pokemon) < 0 || m.Slot < 0 || m.Slot >= PokemonState.MoveSlots || Blocked(s.Pokemon[m.Pokemon], m.Slot)) return RejectReason.Illegal;
            if (s.Phase != Phase.Turn || s.ActiveSeat != seat || s.Battle != null || s.Stack.Count > 0) return RejectReason.Timing;
            foreach (var list in new[] { s.Pending, s.Held })
                foreach (var c in list)
                    if (c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.BreachAttach) return RejectReason.Timing;
            return RejectReason.None;
        }

        /// <summary>보관한 그 카드를 꺼내 그 칸에 붙인다(BreachCardAttached — 그 칸의 가르친 기술은 버린다, <see cref="Attach"/>와 같다).</summary>
        internal static void AttachStored(GameState s, Emitter fx, int seat, Maintenance m)
        {
            if (!Take(s.Ub.BreachStored, seat, m.Value)) return;
            int old = Put(s, s.Pokemon[m.Pokemon], m.Slot, m.Value);
            if (fx.On) fx.Add(new BreachCardAttached { Seat = seat, PokemonId = m.Pokemon, Slot = m.Slot, CardKey = m.Value, ReplacedKey = old });
        }

        /// <summary>둘씩 [좌석, 카드] 목록에서 그 좌석의 그 카드 하나를 뺀다(앞에서부터). 뺐으면 true.</summary>
        static bool Take(List<int> pile, int seat, int card)
        {
            for (int i = 0; i + 1 < pile.Count; i += 2)
                if (pile[i] == seat && pile[i + 1] == card)
                {
                    pile.RemoveRange(i, 2);
                    return true;
                }
            return false;
        }

        static int Put(GameState s, PokemonState p, int slot, int card)
        {
            int old = p.Moves[slot];
            p.Moves[slot] = -1;
            if (old >= 0) EvolveRules.Forgot(s, p, old);
            p.Breach[slot] = card;
            return old;
        }

        static void ApplyRelease(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (a.Kind != AnswerKind.Pick) return;
            if (a.Target == HandTarget)
            {
                // 손(먼저)·보관의 카드 한 장을 버린다(결정 84 U10 · U11) — PokemonId·Slot = −1
                if (!Take(s.Ub.BreachHands, c.Seat, a.Arg) && !Take(s.Ub.BreachStored, c.Seat, a.Arg)) return;
                s.Discards[(int)DeckId.DistortionBreach].Add(a.Arg);
                if (fx.On) fx.Add(new BreachCardDiscarded { Seat = c.Seat, PokemonId = -1, Slot = -1, CardKey = a.Arg });
                return;
            }
            var p = s.Pokemon[a.Target];
            int card = p.Breach[a.Arg];
            p.Breach[a.Arg] = -1;
            if (a.Values.Count == 0)
            {
                s.Discards[(int)DeckId.DistortionBreach].Add(card);
                if (fx.On) fx.Add(new BreachCardDiscarded { Seat = c.Seat, PokemonId = a.Target, Slot = a.Arg, CardKey = card });
                return;
            }
            int old = Put(s, s.Pokemon[a.Values[0]], a.Values[1], card);
            if (fx.On) fx.Add(new BreachCardMoved { Seat = c.Seat, CardKey = card, FromId = a.Target, FromSlot = a.Arg, ToId = a.Values[0], ToSlot = a.Values[1], ReplacedKey = old });
        }
    }
}
