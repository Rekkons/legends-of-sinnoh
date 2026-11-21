using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 라운드·턴(01 §3-5, S2_5). 라운드 첫 좌석(마지막에 고향 마을을 고른 좌석)부터 시계 방향(좌석 번호 오름차순, G6).
    /// 턴 시작(자동 Seq TurnStart): 떨어진 별 운명(FateRules) → 도전 보호 해제 → 귀환 회복 + 운명 1장 → 이동력 → MapAction. 추가 턴(신을 이겼을 때, rulings A28 ①)은 <see cref="ExtraTurn"/>.
    /// 턴 끝은 그 원인 행동의 Seq 안에서 TurnEnded를 내고, 다음 턴(필요하면 새 라운드)을 새 Seq로 연다(01 §3-6 ① (b)).
    /// </summary>
    public static class TurnFlow
    {
        /// <summary>셋업이 끝나면: 턴 단계로, 첫 라운드.</summary>
        public static void StartGame(GameState s, Ctx ctx, Emitter fx)
        {
            s.Phase = Phase.Turn;
            s.Round = 0;
            StartRound(s, ctx, fx);
        }

        internal static void StartRound(GameState s, Ctx ctx, Emitter fx)
        {
            s.Round++;
            fx.Begin(SeatId.System, ActionKind.RoundStart, AutoKind.Auto);
            if (fx.On) fx.Add(new RoundStarted { Seat = SeatId.System, Round = s.Round });
            int first = s.RoundStartSeat;
            for (int i = 0; i < s.SeatCount && s.Seats[first].Eliminated; i++) first = (first + 1) % s.SeatCount; // 탈락한 좌석은 턴이 없다(울트라 버스트, rulings A54 ④)
            StartTurn(s, ctx, fx, first, false);
        }

        /// <summary>맵 턴 단위의 사용 기록(턴이 바뀌면 지운다, 01 §2-2 Used).</summary>
        static bool MapScoped(UsageKind k) =>
            k == UsageKind.Ability || k == UsageKind.VillageHeal || k == UsageKind.Ginkgo || k == UsageKind.Fly || k == UsageKind.FateMap || k == UsageKind.EmptySearch
            || k == UsageKind.Challenge || k == UsageKind.Stew || k == UsageKind.InfusedStew || k == UsageKind.AlluringStew || k == UsageKind.ShrineOffer || k == UsageKind.GodVictory;

        /// <summary>턴 시작(01 §3-5): TurnStarted → 도전 보호 해제(04 9행) → 귀환 회복(8행) → 이동력(6행) → MapAction.</summary>
        public static void StartTurn(GameState s, Ctx ctx, Emitter fx, int seat, bool extra)
        {
            s.ActiveSeat = seat;
            s.TurnId++;
            fx.Begin(seat, ActionKind.TurnStart, AutoKind.Auto);
            if (fx.On) fx.Add(new TurnStarted { Seat = seat, TurnId = s.TurnId, Round = s.Round, IsExtra = extra });
            for (int i = s.Used.Count - 1; i >= 0; i--)
                if (MapScoped(s.Used[i].Kind) && s.Used[i].Scope != s.TurnId) s.Used.RemoveAt(i);
            EffectRules.ClearLimits(s, ctx, EffectLimit.Turn); // 「턴당 1번」 효과(01 §7-1 Limit)

            var st = s.Seats[seat];
            FateRules.DealStars(s, ctx, fx, seat); // 떨어진 별 4개마다 1장(p.16, 04 7행)
            if (st.Protected)
            {
                st.Protected = false;
                if (fx.On) fx.Add(new ProtectionCleared { Seat = seat });
            }
            if (st.RespawnPending)
            {
                // 사용할 포켓몬이 바닥난 다음 내 턴 시작: 파티 전체 회복 + 운명의 변화 1장(S7_9 p.9).
                HealParty(s, ctx, fx, seat, SourceRef.Rule(RuleSource.Respawn));
                FateRules.Deal(s, ctx, fx, SourceRef.Rule(RuleSource.Respawn), seat, 1);
                st.RespawnPending = false;
                if (fx.On) fx.Add(new RespawnCleared { Seat = seat });
            }

            var mods = new List<Mod>();
            int total = MoveRules.BaseMovement;
            if (s.Hos != null && st.ActProgress >= 1)
            {
                // 1막 해방: 이후 매 턴 이동력 +2(A1)
                mods.Add(new Mod { Source = new SourceRef(SourceKind.Act, ctx.Decks.CardIds.IndexOf(SetupFlow.ActOneKey)), Delta = 2 });
                total += 2;
            }
            total += UbTrial.MoveBonus(s, mods); // 울트라 버스트: 쓰러뜨리거나 잡은 호수 수호신마다 모든 좌석 +2(p.26, rulings A55 ⑤)
            total += FractureRules.MoveBonus(s, ctx, seat, mods); // 시작의 사당에 내 신 토큰이 있으면 +1(p.18, rulings A31 ⑥)
            st.MovesLeft = total;
            st.MoveBonus = 0;
            st.Moved = false;
            st.Glide = 0;
            st.RoadChain = false;
            st.FreeLeft = TrainerRules.FreeTiles; // 턴당 1번 지형 2칸 무료(트레이너 카드 — rulings A36)
            st.Building = false;
            st.Trod.Clear();
            st.Closing = 0;
            if (fx.On) fx.Add(new MoveAllowanceSet { Seat = seat, Base = MoveRules.BaseMovement, Mods = mods.ToArray(), Total = total });
            if (BreachRules.OpenAttach(s, ctx, fx)) return; // 손의 균열 카드를 붙일 수 있는 좌석 모두에게 먼저 묻는다(p.24, rulings A52 — 끝나면 OpenTurn)
            OpenTurn(s, ctx, fx, seat);
        }

        /// <summary>턴 시작의 선택: 나눠 받은 화려한 만찬이면 스튜부터(A19), 아니면 맵 행동.</summary>
        internal static void OpenTurn(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (!ShrineRules.TurnStarted(s, ctx, fx, seat)) OpenMapAction(s, ctx, fx, seat);
        }

        /// <summary>차례 좌석의 맵 행동 선택(01 §3-2, 답 = 행동 하나).</summary>
        public static void OpenMapAction(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            TrainerRules.Unreach(s); // psychic B로 닿아 있던 칸에서 제 칸으로(rulings A37)
            if (seat == s.ActiveSeat && TrainerRules.UnlockAsk(s, ctx, fx, 0)) return; // 미뤄 둔 「When unlocked」 물음(답이 맵 행동을 다시 연다, rulings A40)
            if (seat == s.ActiveSeat && TrainerRules.Scoop(s, ctx, fx, 0)) return; // interviewers B: 이 턴에 스탬프를 얻은 좌석에 도전할까(답이 맵 행동을 다시 연다, rulings A38)
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = seat });
        }

        /// <summary>지금 행동 Seq 안에서 턴을 끝내고(TurnEnded) 다음 좌석의 턴을 연다. 라운드 첫 좌석으로 돌아오면 새 라운드.</summary>
        public static void EndTurn(GameState s, Ctx ctx, Emitter fx, TurnEndReason reason)
        {
            FinishTurn(s, fx, reason);
            NextTurn(s, ctx, fx);
        }

        /// <summary>EndTurn 앞 절반: 지금 Seq 안에서 TurnEnded. 사이에 자동 단계(1막 해방 뒤 막 공개 등)를 끼울 때 NextTurn과 나눠 부른다.</summary>
        public static void FinishTurn(GameState s, Emitter fx, TurnEndReason reason)
        {
            int seat = s.ActiveSeat;
            TrainerRules.Unreach(s);
            TrainerRules.ScoopExpired(s);
            if (fx.On) fx.Add(new TurnEnded { Seat = seat, TurnId = s.TurnId, Reason = reason });
            s.Seats[seat].MovesLeft = 0;
            s.Seats[seat].Closing = 0;
        }

        /// <summary>EndTurn 뒤 절반: 다음 좌석의 턴(라운드 첫 좌석이면 새 라운드).</summary>
        public static void NextTurn(GameState s, Ctx ctx, Emitter fx)
        {
            FateRules.TurnEnded(s, ctx, fx); // 「턴 끝에 버리기」 옵션(A12)
            int next = (s.ActiveSeat + 1) % s.SeatCount;
            // 탈락한 좌석(울트라 버스트, rulings A54 ④)은 건너뛴다 — 라운드 첫 좌석 자리에 닿으면 라운드 끝
            while (next != s.RoundStartSeat && s.Seats[next].Eliminated) next = (next + 1) % s.SeatCount;
            if (next == s.RoundStartSeat)
            {
                if (s.Ub != null && UbTurn.RoundEnd(s, ctx, fx)) return; // 울트라 버스트: 모든 좌석이 한 번씩 한 뒤 UB 턴(p.23, 01 §8-2) — 거기서 게임이 끝나거나 건너뛸지 묻는 중이면 새 라운드를 열지 않는다
                StartRound(s, ctx, fx);
            }
            else StartTurn(s, ctx, fx, next, false);
        }

        /// <summary>
        /// 추가 턴(p.21 「if you are victorious against a God of Sinnoh in this campaign, you may immediately take another turn」, rulings A28 ①): 다음 좌석 대신 같은 좌석의 새 턴(TurnStarted.IsExtra).
        /// 「may」지만 받아서 잃는 것이 없어 묻지 않는다 — 원하지 않으면 그 턴에 바로 [턴 종료] [가정 plan §5 #89 ④]. 그 뒤 순서는 그대로 다음 좌석부터.
        /// </summary>
        public static void ExtraTurn(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            FateRules.TurnEnded(s, ctx, fx); // 「턴 끝에 버리기」 옵션(A12)은 보통 턴 끝과 같게
            StartTurn(s, ctx, fx, seat, true);
        }

        /// <summary>파티 전체 회복(피해·상태이상 0, S3_3). 회복할 것이 없는 포켓몬도 싣는다(0 결과, 01 §3-6 ⑥).</summary>
        public static void HealParty(GameState s, Ctx ctx, Emitter fx, int seat, SourceRef source)
        {
            var entries = new List<HealEntry>();
            foreach (int p in s.Seats[seat].Party)
            {
                if (p < 0) continue;
                var pk = s.Pokemon[p];
                int before = PokemonRules.Hp(ctx, pk);
                var status = pk.Status;
                pk.Damage = 0;
                StatusRules.Clear(pk);
                entries.Add(new HealEntry { Id = p, HpBefore = before, HpAfter = PokemonRules.Hp(ctx, pk), StatusBefore = status });
            }
            if (fx.On) fx.Add(new Healed { Seat = seat, Source = source, Pokemon = entries.ToArray() });
        }

        /// <summary>
        /// 사용할 포켓몬이 바닥남(S7_9 p.9): 즉시 고향 마을로, 다음 내 턴 시작에 회복(RespawnPending). 배틀이 끝날 때 BattleFlow가 부른다.
        /// </summary>
        public static void Whiteout(GameState s, Ctx ctx, Emitter fx, int seat, int to = -1)
        {
            var st = s.Seats[seat];
            int from = st.Cell;
            int home = to >= 0 ? to : ctx.Fixtures.Fixtures[st.HomeVillage].Cell; // to = team_rocket R이 고른 칸(rulings A35)
            st.Cell = home;
            st.RespawnPending = true;
            if (fx.On)
            {
                fx.Add(new Whiteout { Seat = seat, FromCell = from, ToCell = home });
                fx.Add(new RespawnPending { Seat = seat });
            }
        }
    }
}
