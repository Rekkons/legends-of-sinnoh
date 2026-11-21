using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 보상·남의 행동 반응 11(M6 묶음 7, rulings A38): aroma_lady B · fisherman B·R · artist B · backpacker B · black_belt B · bug_catcher B · gentleman B ·
    /// delinquent B · ginkgo_merchant B · interviewers B + 전용 아이템 월척 주사위. 배틀 뒤 보상은 BattleFlow.Conclude가 부르고, 배틀 뒤 물음(backpacker B · interviewers B)은
    /// 출처가 카드인 선택을 BattleFlow.Close 앞에서 연다(답은 <see cref="MapAnswered"/>).
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>black_belt B 「gain 2 extra Journey Points」 · bug_catcher B 「gain 2 extra Journey Points」 · fisherman R 「for the cost of 2 Journey Points」.</summary>
        public const int TrainingJp = 2, FrenzyJp = 2, CatchGrowJp = 2;

        /// <summary>월척 주사위 면 0~5의 값(rulings G1 — Tiny −1 · Average 0 · Medium 1 · Large 2 · Huge 4 · Gargantuan 6).</summary>
        public static readonly int[] CatchSizes = { -1, 0, 1, 2, 4, 6 };

        // ── 포획 굴림(aroma_lady B · fisherman B 앞 절반) ──

        /// <summary>
        /// aroma_lady B 「During capture attempts at Apricorn Trees, replace 1 status symbol with 1 regular hit」 · fisherman B 「During capture attempts at Apricorn Reefs, …」:
        /// 규토리 나무·산호 토큰에서 건 야생 배틀의 포획 굴림이면 상태 심볼 하나를 일반 타격으로(상태이상의 바꾸기 뒤에 하나 더 — 강제). faces = ApplyAccuracy를 거친 면.
        /// </summary>
        internal static AttackDieFace[] SweetScent(GameState s, Ctx ctx, Emitter fx, BattleState b, int seat, AttackDieFace[] faces)
        {
            if (!Seated(s, seat) || b.Kind != BattleKind.Wild || b.Token < 0 || b.Token >= s.Tokens.Count) return faces;
            var kind = s.Tokens[b.Token].Kind;
            AbilityColor? color = null;
            if (kind == TokenKind.ApricornTree && Has(s, ctx, seat, "aroma_lady", Blue)) color = Blue;
            if (kind == TokenKind.ApricornReef && Has(s, ctx, seat, "fisherman", Blue)) color = Blue;
            int at = Array.IndexOf(faces, AttackDieFace.StatusSymbol);
            if (color == null || at < 0) return faces;
            var r = (AttackDieFace[])faces.Clone();
            r[at] = AttackDieFace.RegularHit;
            Used(s, fx, seat, Blue);
            return r;
        }

        // ── 월척(fisherman B 뒤 절반 · R) ──

        /// <summary>fisherman B 「After capturing a wild Pokémon at an Apricorn Reef, roll and then add a Catch Size dice to them」: 산호 토큰에서 건 야생 배틀에서 잡았으면 월척 주사위(면 0~5)를 굴려 붙인다.</summary>
        internal static void Landed(GameState s, Ctx ctx, Emitter fx, BattleState b, int seat, int pokemon)
        {
            if (!Seated(s, seat) || b.Kind != BattleKind.Wild || b.Token < 0 || s.Tokens[b.Token].Kind != TokenKind.ApricornReef) return;
            if (!Has(s, ctx, seat, "fisherman", Blue)) return;
            var p = s.Pokemon[pokemon];
            p.CatchSize = s.Rng.Next(CatchSizes.Length);
            Used(s, fx, seat, Blue, pokemon, p.CatchSize);
            Refresh(s, ctx);
        }

        /// <summary>fisherman R 「Your Pokémon now gain health equal to their Catch Size」(HealthBonus가 더한다 — 주사위가 없으면 0).</summary>
        static int CatchHealth(GameState s, Ctx ctx, PokemonState p) =>
            p.CatchSize >= 0 && p.CatchSize < CatchSizes.Length && Has(s, ctx, p.Owner, "fisherman", Red) ? CatchSizes[p.CatchSize] : 0;

        /// <summary>
        /// 정비 명령 CatchSize(fisherman R 「for the cost of 2 Journey Points: Increase the Catch Size of a Pokémon in your party to its next level」)의 검사:
        /// 내 파티 · 월척 주사위가 붙어 있고 끝 면이 아님 · 새 값 = 지금 + 1 · JP 2 · 내 턴·배틀 밖 [가정 plan §5 #144 ③].
        /// </summary>
        internal static RejectReason CheckCatchSize(GameState s, Ctx ctx, int seat, Maintenance m)
        {
            if (PokemonRules.PartyIndexOf(s.Seats[seat], m.Pokemon) < 0 || !Seated(s, seat) || !Has(s, ctx, seat, "fisherman", Red)) return RejectReason.Illegal;
            var p = s.Pokemon[m.Pokemon];
            if (p.CatchSize == m.Value) return RejectReason.NoChange;
            if (p.CatchSize < 0 || m.Value != p.CatchSize + 1 || m.Value >= CatchSizes.Length) return RejectReason.Illegal;
            if (s.Battle != null || s.Phase != Phase.Turn || s.ActiveSeat != seat) return RejectReason.Timing;
            return s.Seats[seat].Jp >= CatchGrowJp ? RejectReason.None : RejectReason.Illegal;
        }

        internal static void GrowCatch(GameState s, Ctx ctx, Emitter fx, int seat, Maintenance m)
        {
            s.Seats[seat].Jp -= CatchGrowJp;
            s.Pokemon[m.Pokemon].CatchSize = m.Value;
            Refresh(s, ctx);
            Used(s, fx, seat, Red, m.Pokemon, m.Value);
        }

        // ── 기술 가르치기(artist B) ──

        /// <summary>artist B 「After defeating a wild Pokémon, you may teach its move to any Pokémon in your party regardless of their learnable move types」: 배틀 뒤 가르치기(Window = BattleEnd)에서 타입을 보지 않는다.</summary>
        public static bool Sketches(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "artist", Blue);

        // ── 배틀 뒤 보상(BattleFlow.Conclude) ──

        /// <summary>black_belt B 「After defeating a wild Pokémon, gain 2 extra Journey Points」: 쓰러뜨린 야생 하나마다(무리 배틀은 쓰러진 것마다 — [가정 §5 #144 ④]) 더 받을 JP.</summary>
        internal static int Training(GameState s, Ctx ctx, Emitter fx, int seat, int defeated)
        {
            if (defeated <= 0 || !Seated(s, seat) || !Has(s, ctx, seat, "black_belt", Blue)) return 0;
            Used(s, fx, seat, Blue);
            return TrainingJp * defeated;
        }

        /// <summary>gentleman B 「After capturing or defeating a wild Pokémon, gain double the amount of Apricorns」: 받을 규토리 목록을 두 번 넣는다.</summary>
        internal static List<int> Coins(GameState s, Ctx ctx, Emitter fx, int seat, List<int> colors)
        {
            if (colors.Count == 0 || !Seated(s, seat) || !Has(s, ctx, seat, "gentleman", Blue)) return colors;
            Used(s, fx, seat, Blue);
            var r = new List<int>(colors);
            r.AddRange(colors);
            return r;
        }

        /// <summary>
        /// bug_catcher B 「After capturing a Bug-type Pokémon, gain 2 extra Journey Points and immediately start an extra turn. Cannot be repeated on the extra turn.」:
        /// 이 배틀에서 잡은 벌레 타입(맞은편 명단에서 이제 그 좌석의 것 — 야생 · team_rocket B)마다 JP 2, 차례 좌석이면 턴 끝에 추가 턴 한 번(BattleFlow.FinishClose).
        /// 그 추가 턴에는 둘 다 없다 [가정 §5 #144 ⑤]. 더할 JP를 돌려준다.
        /// </summary>
        internal static int Frenzy(GameState s, Ctx ctx, Emitter fx, BattleState b, int k)
        {
            int seat = b.Sides[k].Owner;
            if (!Seated(s, seat) || !Has(s, ctx, seat, "bug_catcher", Blue)) return 0;
            var st = s.Seats[seat];
            if (st.FrenzyTurn == s.TurnId) return 0;
            int n = 0;
            foreach (int p in b.Sides[1 - k].Roster)
            {
                var pk = s.Pokemon[p];
                if (pk.Def >= 0 && pk.Owner == seat && IsType(ctx, pk, PokemonType.Bug)) n++;
            }
            if (n == 0) return 0;
            if (seat == s.ActiveSeat) st.FrenzyDue = true;
            Used(s, fx, seat, Blue);
            return FrenzyJp * n;
        }

        /// <summary>턴 끝(BattleFlow.FinishClose): bug_catcher B의 추가 턴이 남았나(지운다).</summary>
        internal static bool FrenzyNow(GameState s, int seat)
        {
            var st = s.Seats[seat];
            bool due = st.FrenzyDue;
            st.FrenzyDue = false;
            return due;
        }

        /// <summary>추가 턴을 연 뒤: 그 턴이 bug_catcher B의 추가 턴이다(그 턴에는 다시 안 된다).</summary>
        internal static void FrenzyStarted(GameState s, int seat) => s.Seats[seat].FrenzyTurn = s.TurnId;

        /// <summary>backpacker B 「After any battle, fully restore a Pokémon in your party」: 이 배틀에 나온 좌석에 표시를 남긴다(종막 배신자 연전은 뺀다 [가정 §5 #144 ⑥]).</summary>
        internal static void TentPitched(GameState s, Ctx ctx, BattleState b)
        {
            if (b.Kind == BattleKind.Traitor) return;
            foreach (var side in b.Sides)
                if (Seated(s, side.Owner) && Has(s, ctx, side.Owner, "backpacker", Blue)) s.Seats[side.Owner].TentDue = true;
        }

        /// <summary>
        /// 배틀 뒤 턴 끝 앞(BattleFlow.Close): backpacker B의 회복을 묻는다 — 강제 고르기(Reaction/EffectPick, Count 1 = 「안 함」 없음 · 첫 합법 답 = 파티 순서 첫 포켓몬, rulings A33)
        /// 대상 = 다치거나 상태이상이거나 기절한 파티 포켓몬 [가정 §5 #144 ⑥]. Subject = 미뤄 둔 after. 열었으면 true.
        /// </summary>
        internal static bool Tent(GameState s, Ctx ctx, Emitter fx, int after)
        {
            for (int seat = 0; seat < s.SeatCount; seat++)
            {
                var st = s.Seats[seat];
                if (!st.TentDue) continue;
                st.TentDue = false;
                var c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Subject = after, Count = 1, Source = CardSource(s, seat) };
                foreach (int p in st.Party)
                {
                    if (p < 0) continue;
                    var pk = s.Pokemon[p];
                    if (pk.Damage > 0 || pk.Status != StatusEffect.None) c.Options.Add(p);
                }
                if (c.Options.Count == 0) continue;
                ChoiceRules.Open(s, ctx, fx, c);
                return true;
            }
            return false;
        }

        static void Restore(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon)
        {
            var pk = s.Pokemon[pokemon];
            int before = PokemonRules.Hp(ctx, pk);
            var status = pk.Status;
            pk.Damage = 0;
            StatusRules.Clear(pk);
            Used(s, fx, seat, Blue, pokemon);
            if (fx.On) fx.Add(new Healed { Seat = seat, Source = CardSource(s, seat), Pokemon = new[] { new HealEntry { Id = pokemon, HpBefore = before, HpAfter = PokemonRules.Hp(ctx, pk), StatusBefore = status } } });
        }

        // ── 남의 행동(delinquent B · ginkgo_merchant B) ──

        static int OtherWith(GameState s, Ctx ctx, int seat, string card, AbilityColor color)
        {
            for (int i = 0; i < s.SeatCount; i++)
                if (i != seat && Seated(s, i) && !s.Seats[i].Eliminated && Has(s, ctx, i, card, color)) return i;
            return -1;
        }

        static void Gain(GameState s, Emitter fx, int seat, IReadOnlyList<ApricornColor> colors)
        {
            var changes = new List<ResChange>();
            CraftRules.MoveApricorns(s.Seats[seat], seat, colors, +1, changes);
            Used(s, fx, seat, Blue);
            if (fx.On) fx.Add(new ApricornsGained { Seat = seat, Source = CardSource(s, seat), Changes = changes.ToArray() });
        }

        /// <summary>delinquent B 「When another trainer makes a Shrine Offering, gain the Apricorn they discard」(ShrineRules.Offer — 시작의 사당에서 심장을 되살린 규토리는 버린 것이 아니다 [가정 §5 #144 ⑦]).</summary>
        internal static void OfferingTaken(GameState s, Ctx ctx, Emitter fx, int seat, int color)
        {
            int thief = OtherWith(s, ctx, seat, "delinquent", Blue);
            if (thief >= 0) Gain(s, fx, thief, new[] { (ApricornColor)color });
        }

        /// <summary>ginkgo_merchant B 「When another trainer uses the current Ginkgo Trade, gain the Apricorns they discard」(BankRules.Apply — 줄마다).</summary>
        internal static void Monopoly(GameState s, Ctx ctx, Emitter fx, int seat, IReadOnlyList<ApricornColor> give)
        {
            int merchant = OtherWith(s, ctx, seat, "ginkgo_merchant", Blue);
            if (merchant >= 0 && give.Count > 0) Gain(s, fx, merchant, give);
        }

        // ── interviewers B: 남이 스탬프를 얻으면 ──

        /// <summary>
        /// interviewers B 「Whenever another trainer gains a Journey Stamp, you may instantly move to that trainer to challenge them to a battle. If you win the battle, you may also gain that Journey Stamp.」:
        /// 「Whenever」 — 스탬프를 얻은 좌석이 차례 좌석이 아니어도(사용자 결정 84 T14 나 — 옛 [가정 §5 #144 ①]은 그 좌석의 턴 안에서만) 표시를 남기고, 지금 턴의 다음 쉬는 자리(맵 행동을 열기 전 · 배틀 뒤 턴 끝 앞)에 묻는다.
        /// 그 좌석의 턴 밖이면 배틀만 하고 끝낼 턴은 없다(<see cref="ScoopBack"/>). 이미 묻는 중이면(또는 그 도전 배틀 중이면) 버리지 않고 SeatState.ScoopNext에 줄 세워
        /// 지금 물음이 끝난 뒤 다음 쉬는 자리에서 얻은 차례대로 묻는다(M9 실행 감사 K1 — 한 명령에 둘이 얻으면 StampRules.Sweep이 G6 맵 창 순서로 받는다).
        /// </summary>
        internal static void StampGained(GameState s, Ctx ctx, int seat, int stamp)
        {
            if (s.Phase == Phase.Setup || s.Phase == Phase.GameOver) return;
            int who = OtherWith(s, ctx, seat, "interviewers", Blue);
            if (who < 0) return;
            if (s.Seats[who].ScoopTarget >= 0 || s.Seats[who].ScoopAt != 0)
            {
                s.Seats[who].ScoopNext.Add(seat);
                s.Seats[who].ScoopNext.Add(stamp);
                return;
            }
            s.Seats[who].ScoopTarget = seat;
            s.Seats[who].ScoopStamp = stamp;
            s.Seats[who].ScoopAt = 0;
        }

        /// <summary>
        /// 남겨 둔 물음을 지금 열 수 있나: 배틀 밖 · 대상이 도전 보호·탈락이 아님 · 둘 다 쓸 포켓몬이 있다(대상이 차례 좌석이 아니어도 — 결정 84 T14). 못 열면 표시를 지운다.
        /// 표시가 비었고(물음 · 도전 배틀 · 돌아갈 자리 없음) 줄(ScoopNext)이 있으면 다음 것을 표시로 올린다.
        /// </summary>
        static int ScoopReady(GameState s, Ctx ctx)
        {
            for (int who = 0; who < s.SeatCount; who++)
            {
                var st = s.Seats[who];
                while (true)
                {
                    if (st.ScoopTarget < 0 && st.ScoopAt == 0 && st.ScoopNext.Count >= 2)
                    {
                        st.ScoopTarget = st.ScoopNext[0];
                        st.ScoopStamp = st.ScoopNext[1];
                        st.ScoopNext.RemoveRange(0, 2);
                    }
                    if (st.ScoopTarget < 0 || st.ScoopAt >= 2) break;
                    int target = st.ScoopTarget;
                    bool ok = s.Battle == null && s.Phase == Phase.Turn && !s.Seats[target].Protected && !s.Seats[target].Eliminated && !st.Eliminated
                        && BattleRules.HasUsable(s, ctx, who) && BattleRules.HasUsable(s, ctx, target) && Has(s, ctx, who, "interviewers", Blue);
                    if (ok) return who;
                    ClearScoop(st);
                }
            }
            return -1;
        }

        static void ClearScoop(SeatState st)
        {
            st.ScoopTarget = st.ScoopStamp = -1;
            st.ScoopAt = 0;
        }

        /// <summary>남겨 둔 interviewers B 물음을 연다(Reaction/EffectOption — 「may」라 안 감이 첫 답, Subject = 대상 좌석, Options = [스탬프]). at = 0 맵 행동 앞 · 1 배틀 뒤 턴 끝 앞. 열었으면 true.</summary>
        internal static bool Scoop(GameState s, Ctx ctx, Emitter fx, int at)
        {
            int who = ScoopReady(s, ctx);
            if (who < 0) return false;
            var st = s.Seats[who];
            st.ScoopAt = at;
            var c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectOption, Seat = who, Subject = st.ScoopTarget, Source = CardSource(s, who) };
            c.Options.Add(st.ScoopStamp);
            ChoiceRules.Open(s, ctx, fx, c);
            return true;
        }

        /// <summary>
        /// 명령 끝(StampRules.AfterCommand · 정비): 상태 조건 스탬프는 맵 행동을 다시 연 뒤에 받는다 — 표시가 남았고 열린 선택이 대상의 맵 행동 하나뿐이면 그것을 거두고(ChoiceReissued — 새 Id = 물음) 묻는다.
        /// </summary>
        internal static void ScoopAfterCommand(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Pending.Count != 1 || s.Held.Count > 0) return;
            var open = s.Pending[0];
            if (open.Kind != ChoiceKind.MapAction || open.Answered || open.Seat != s.ActiveSeat || ScoopReady(s, ctx) < 0) return;
            s.Pending.RemoveAt(0);
            if (fx.On) fx.Add(new ChoiceReissued { Seat = open.Seat, OldId = open.Id, NewId = s.NextChoiceId });
            Scoop(s, ctx, fx, 0);
        }

        /// <summary>물음을 연 자리로 돌아간다: 0 = 대상의 맵 행동 · 1 = 배틀 뒤 턴 끝.</summary>
        static void ScoopResume(GameState s, Ctx ctx, Emitter fx, int at)
        {
            if (at == 1) BattleFlow.Close(s, ctx, fx, 0);
            else MapActions.Continue(s, ctx, fx, s.ActiveSeat);
        }

        static void Scooped(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var st = s.Seats[c.Seat];
            int target = st.ScoopTarget, at = st.ScoopAt;
            if (a.Kind != AnswerKind.Accept || target < 0)
            {
                ClearScoop(st);
                ScoopResume(s, ctx, fx, at);
                return;
            }
            // 「instantly move to that trainer」: 이동력을 쓰지 않고 그 좌석의 칸으로 → 보통 도전(마을 안이면 거절할 수 있다 · 배틀은 그 칸에서, BattleFlow.Challenge)
            st.ScoopAt = at + 2;
            int from = st.Cell, to = s.Seats[target].Cell;
            Used(s, fx, c.Seat, Blue, target);
            if (from != to)
            {
                st.Cell = to;
                if (fx.On) fx.Add(new Flew { Seat = c.Seat, Source = CardSource(s, c.Seat), From = from, To = to });
            }
            BattleFlow.Challenge(s, ctx, fx, c.Seat, target);
        }

        /// <summary>interviewers B의 도전을 마을 안에서 거절했다(BattleFlow.ApplyRespond): 물음을 연 자리로 돌아간다. 그 도전이 아니면 false.</summary>
        internal static bool ScoopRefused(GameState s, Ctx ctx, Emitter fx, int challenger)
        {
            if (challenger < 0 || challenger >= s.SeatCount || s.Seats[challenger].ScoopAt < 2 || s.Seats[challenger].ScoopAt > 3) return false;
            int at = s.Seats[challenger].ScoopAt - 2;
            ClearScoop(s.Seats[challenger]);
            ScoopResume(s, ctx, fx, at);
            return true;
        }

        /// <summary>트레이너전 끝(BattleFlow.Conclude — 스탬프 2 뒤): interviewers B의 도전이었으면 이긴 쪽이 도전자일 때 그 스탬프를 받는다(받지 않을 까닭이 없어 묻지 않는다 [가정 §5 #144 ②]). 표시를 지운다.</summary>
        internal static void ScoopEnded(GameState s, Ctx ctx, Emitter fx, BattleState b, BattleResult result, int winner)
        {
            if (b.Kind != BattleKind.Trainer) return;
            int who = b.Sides[0].Owner;
            if (who < 0 || who >= s.SeatCount) return;
            var st = s.Seats[who];
            if (st.ScoopAt < 2 || st.ScoopAt > 3 || st.ScoopTarget != b.Sides[1].Owner) return;
            int stamp = st.ScoopStamp, at = st.ScoopAt - 2, target = st.ScoopTarget;
            ClearScoop(st);
            // 대상 좌석의 턴 밖(도전자도 차례 좌석이 아님): 끝낼 턴이 없다 — 배틀 뒤 물은 자리로 돌아간다(BattleFlow.Close → ScoopBack, 사용자 결정 84 T14 나).
            // 대상이 차례 좌석이면 그 턴이 이 배틀로 끝난다(원작 커뮤니티 확인) · 도전자가 차례 좌석이면 보통 배틀처럼 자기 턴이 끝난다
            if (target != s.ActiveSeat && who != s.ActiveSeat) st.ScoopAt = OffTurnBack + at;
            if (winner == 0 && (result == BattleResult.Win || result == BattleResult.Forfeited) && stamp >= 0) StampRules.Gain(s, ctx, fx, who, stamp);
        }

        /// <summary>SeatState.ScoopAt: 턴 밖 도전이 끝나 물은 자리(+ 0 맵 행동 · 1 배틀 뒤 턴 끝)로 돌아갈 차례(ScoopTarget = −1).</summary>
        const int OffTurnBack = 4;

        /// <summary>
        /// 배틀 뒤 턴 끝(BattleFlow.Close 처음): 방금 끝난 배틀이 대상의 턴 밖 interviewers B 도전이었으면 턴을 끝내지 않고 물은 자리로 돌아간다(결정 84 T14 나 — 「배틀만 하고 끝낼 턴은 없다」). 돌아갔으면 true.
        /// </summary>
        internal static bool ScoopBack(GameState s, Ctx ctx, Emitter fx)
        {
            foreach (var st in s.Seats)
            {
                if (st.ScoopTarget >= 0 || st.ScoopAt < OffTurnBack) continue;
                int at = st.ScoopAt - OffTurnBack;
                ClearScoop(st);
                ScoopResume(s, ctx, fx, at);
                return true;
            }
            return false;
        }

        /// <summary>턴 끝(TurnFlow.FinishTurn): 묻지 못한 interviewers B 표시 · 줄은 버린다(그 턴 안에서만).</summary>
        internal static void ScoopExpired(GameState s)
        {
            foreach (var st in s.Seats)
            {
                if ((st.ScoopTarget >= 0 && st.ScoopAt < 2) || st.ScoopAt >= OffTurnBack) ClearScoop(st);
                st.ScoopNext.Clear();
            }
        }

        /// <summary>배틀 밖 카드 물음의 답 가운데 이 묶음 것(backpacker B 회복 · interviewers B 도전). 처리했으면 true.</summary>
        static bool RewardAnswered(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            int seat = c.Seat;
            if (!Seated(s, seat)) return false;
            if (c.Sub == ChoiceSub.EffectPick && Has(s, ctx, seat, "backpacker", Blue))
            {
                if (a.Kind == AnswerKind.Pick) Restore(s, ctx, fx, seat, a.Target);
                BattleFlow.Close(s, ctx, fx, c.Subject);
                return true;
            }
            if (c.Sub == ChoiceSub.EffectOption && Has(s, ctx, seat, "interviewers", Blue))
            {
                Scooped(s, ctx, fx, c, a);
                return true;
            }
            return false;
        }
    }
}
