using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 배틀 상태 기계(01 §6, rulings G13, M3-e). 처리 중 위치는 BattleState.Step·피해 버퍼에 두고 코루틴을 쓰지 않는다(01 §2-3) —
    /// <see cref="Run"/>이 「열린 선택이 없으면 지금 단계를 한 걸음」을 되풀이하고, 선택·반응 창이 열리면 멈춘다.
    /// 단계: SendIn(도전자 → 상대, 끝나면 우선권 토큰 S7_1) → Order(B5·R11, 동률이면 토큰 선택) → SelectAction → PreRoll(BeforeRoll 창) → Roll(피로 A10·R12,
    /// 면 바꾸기 효과 G14) → PostRoll(AfterRoll 창) → PreDamage(기술 사용·상성·피해 내역, 기술 효과·혼란 G14) → DamageWindow(BeforeDamage 창 G5)
    /// → ApplyDamage(동시 적용 p.13) → TurnEnd(직전 기술, 독 G14) → FaintCheck(턴 끝 피해 적용·기절·응징 저장, AfterFaint면 AfterDamage 창)
    /// → Replace(끝 판정·교체 창·다음 포켓몬 S7_5) → 다음 턴 / Order / End. 창에서 쌓인 효과 프레임(EffectRules)은 다음 단계 전에 돈다.
    /// Seq(01 §3-6 ①): 답 하나·가상 쪽 자동 기술 하나가 행동 하나다. 창이 닫히면 Reactions가 새 Seq(Continues)를 열고 여기로 돌아온다.
    /// 고를 것이 하나뿐인 출전·다음 포켓몬과, 답이 하나뿐인 가상 쪽(야생·갤럭시단) 기술은 묻지 않는다(01 §6-7). 좌석의 기술 선택은 늘 묻는다.
    /// </summary>
    public static class BattleFlow
    {
        /// <summary>1막 조무래기 배틀의 포켓몬 수(A1, p.21 「randomly draw 2」).</summary>
        public const int GruntLineup = 2;

        // ── 시작 ──

        /// <summary>야생 배틀(조우 고르기 뒤, S5_1). 카드는 인스턴스가 되고 끝나면 버림 더미(기절)·덱(도망·기권·짐)·파티(포획)로 간다.</summary>
        public static void StartWild(GameState s, Ctx ctx, Emitter fx, int seat, int card, int cell)
        {
            var grade = MapActions.GradeOf(ctx, card);
            int wild = PokemonRules.Spawn(s, card, SeatId.Wild, (int)MapActions.DeckOf(grade));
            var wp = s.Pokemon[wild];
            wp.HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, wp);
            var b = NewBattle(s, BattleKind.Wild, cell);
            b.Token = MapActions.EncounterTokenAt(s, cell);
            b.Uncapturable = ctx.Cards.Pokemon[card].Uncapturable;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, wild);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 우두머리 배틀(A17, 숨겨진 장소 공개 — HiddenSpotRules): 강한 카드에 우두머리 토큰을 붙인다(PokemonState.Alpha — 체력 +4 · 전설 · 포획 6 · JP 4, 규토리 없음).
        /// 우두머리가 건 배틀이라 우선권 토큰은 우두머리 쪽(p.16 「they start with Initiative Advantage」 · 원작 커뮤니티 확인 — 동률에서만).
        /// 알파 스트라이크·비스트 부스트의 효과는 「전설·알파·폼」 묶음. 끝나면 야생처럼(쓰러뜨림 = 버림 더미, 짐·도망·기권 = 강한 덱 — 토큰은 버려진다, 포획 = 토큰째 파티).
        /// </summary>
        public static void StartAlpha(GameState s, Ctx ctx, Emitter fx, int seat, int card, int cell)
        {
            int wild = PokemonRules.Spawn(s, card, SeatId.Wild, (int)DeckId.Strong);
            var wp = s.Pokemon[wild];
            wp.Alpha = true;
            wp.HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, wp);
            var b = NewBattle(s, BattleKind.Alpha, cell);
            b.Uncapturable = ctx.Cards.Pokemon[card].Uncapturable;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, wild);
            b.Sides[1].InitiativeToken = true;
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 전설 배틀(rulings A26 — 전설 장소 찾아보기 LegendRules.SearchSite, 사당 전설은 「사당 공물·카드」가 부른다): 카드는 인스턴스가 되고(전설 덱 — 쓰러뜨림 = 버림 더미,
        /// 짐·도망·기권 = 전설 덱(다시 도전한다 — 원작 커뮤니티 확인), 포획 = 파티). 전설 특수기술(알파 스트라이크·비스트 부스트)을 갖고, 내 포켓몬을 모두 쓰러뜨려야 도망친다(p.6).
        /// 우선권 토큰은 도전한 좌석(S7_1 기본).
        /// </summary>
        public static void StartLegendary(GameState s, Ctx ctx, Emitter fx, int seat, int card, int cell)
        {
            int wild = PokemonRules.Spawn(s, card, SeatId.Wild, (int)DeckId.Legendary);
            var wp = s.Pokemon[wild];
            wp.HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, wp);
            var b = NewBattle(s, BattleKind.Legendary, cell);
            b.Uncapturable = ctx.Cards.Pokemon[card].Uncapturable;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, wild);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 호수 시련(rulings A55 — 울트라 버스트의 호수 동굴 찾아보기, LegendRules.SearchSite): 전설 배틀과 같고(전설 덱의 카드 · 전설 특수기술 · 모두 쓰러뜨려야 도망 · 짐·도망 = 덱으로)
        /// 시련을 넘기기 전에는 수호신이 포획 시도와 모든 피해에 면역이다(UbTrial). 이기거나 잡으면 <see cref="Conclude"/>가 UbTrial.Won(모든 좌석 이동력 +2)을 잇는다.
        /// </summary>
        public static void StartTrial(GameState s, Ctx ctx, Emitter fx, int seat, int card, int cell)
        {
            int wild = PokemonRules.Spawn(s, card, SeatId.Wild, (int)DeckId.Legendary);
            var wp = s.Pokemon[wild];
            wp.HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, wp);
            var b = NewBattle(s, BattleKind.Trial, cell);
            b.Uncapturable = ctx.Cards.Pokemon[card].Uncapturable;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, wild);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 신 배틀(rulings A28 ① — 시작의 사당의 신 토큰 찾아보기, LegendRules.SearchSite): 전설 배틀과 같고(전설 특수기술 · 모두 쓰러뜨려야 도망 · 쓰러뜨림 = 버림 더미, 짐·도망·기권 = 전설 덱)
        /// 천상의 오라(LegendRules.AuraOn — 그 신의 천상의 유물이 없다)면 포획 7 · 첫 폼이 쓰러지면 둘째 폼 완전 회복(LegendRules.Ascend). 이기거나 잡으면 신 토큰이 빠지고 한 턴 더(Close).
        /// 우선권 토큰은 도전한 좌석(S7_1 기본).
        /// </summary>
        public static void StartGod(GameState s, Ctx ctx, Emitter fx, int seat, int card, int cell)
        {
            int wild = PokemonRules.Spawn(s, card, SeatId.Wild, (int)DeckId.Legendary);
            var wp = s.Pokemon[wild];
            wp.HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, wp);
            var b = NewBattle(s, BattleKind.God, cell);
            b.Uncapturable = ctx.Cards.Pokemon[card].Uncapturable;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, wild);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 무리 배틀(rulings A28 ② · A18 — 왜곡 게이트 찾아보기, LegendRules.SearchSite): 약한 · 적당한 · 강한 카드가 한 파티(명단 순서대로 나온다 — <see cref="Bench"/>).
        /// 야생 조우로 친다(포획·기술 가르치기). 내 포켓몬을 모두 쓰러뜨려야 도망 · 하나를 잡으면 다음 포켓몬 · 모두 이기면 JP 7 하나(<see cref="Conclude"/>). 우선권 토큰은 도전한 좌석.
        /// </summary>
        public static void StartSwarm(GameState s, Ctx ctx, Emitter fx, int seat, IReadOnlyList<int> cards, int cell)
        {
            var ids = new int[cards.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = PokemonRules.Spawn(s, cards[i], SeatId.Wild, (int)MapActions.DeckOf(MapActions.GradeOf(ctx, cards[i])));
                s.Pokemon[ids[i]].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, s.Pokemon[ids[i]]);
            }
            var b = NewBattle(s, BattleKind.Swarm, cell);
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Wild, ids);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// UB 배틀(rulings A53 — UB 토큰 칸에서 찾아보기, MapActions): 그 토큰의 UB 카드가 덱 밖의 새 인스턴스로 나온다(도전마다 완전 회복, p.25). 가상 쪽 = SeatId.UltraBeast ·
        /// 포획 불가 · 전설 특수기술 · 내 포켓몬을 모두 쓰러뜨리면 UB 승리(도망 없음). 이기면 <see cref="Conclude"/>가 UbBattle.Defeated · 균열 카드 버리기·옮기기를 잇는다. 우선권 토큰은 도전한 좌석.
        /// </summary>
        public static void StartUltraBeast(GameState s, Ctx ctx, Emitter fx, int seat, int token)
        {
            var t = s.Tokens[token];
            int ub = PokemonRules.Spawn(s, t.Def, SeatId.UltraBeast, -1);
            s.Pokemon[ub].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, s.Pokemon[ub]);
            var b = NewBattle(s, BattleKind.UltraBeast, t.Cell);
            b.Uncapturable = true;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.UltraBeast, ub);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 네크로즈마 배틀(rulings A54 — 네크로즈마 토큰 칸에서 찾아보기, UbNecrozma.Search): 지금 페이즈의 카드가 덱 밖의 새 인스턴스로 나온다(도전·페이즈마다 완전 회복, p.25).
        /// UB 배틀과 같은 꼴(가상 쪽 = SeatId.UltraBeast · 포획 불가 · 전설 특수기술 · 프리즘 아머는 울트라 버스트 쪽 개수). 끝나면 <see cref="Conclude"/>가 UbNecrozma.Concluded(다음 페이즈 · 승리 · 탈락)를 잇는다.
        /// </summary>
        public static void StartNecrozma(GameState s, Ctx ctx, Emitter fx, int seat, int cell, int card)
        {
            int nz = PokemonRules.Spawn(s, card, SeatId.UltraBeast, -1);
            s.Pokemon[nz].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, s.Pokemon[nz]);
            var b = NewBattle(s, BattleKind.Necrozma, cell);
            b.Uncapturable = true;
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.UltraBeast, nz);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>트레이너전(도전, rulings G7·G13). Side 0 = 건 좌석, Side 1 = 받은 좌석. 배틀 칸 = 건 좌석의 칸.</summary>
        public static void StartTrainer(GameState s, Ctx ctx, Emitter fx, int challenger, int target)
        {
            var b = NewBattle(s, BattleKind.Trainer, s.Seats[challenger].Cell);
            SeatSide(s, b.Sides[0], challenger);
            SeatSide(s, b.Sides[1], target);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>1막 조무래기 배틀(A1): 조무래기 덱을 섞고 2장을 앞면으로, 한 파티로 상대한다. 조종은 갤럭시단 가상 응답자(A16).</summary>
        public static void StartGalactic(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var deck = s.Decks[(int)DeckId.GalacticGrunt];
            Piles.Shuffle(deck, s.Rng);
            if (fx.On) fx.Add(new PileShuffled { Pile = DeckId.GalacticGrunt });
            var cards = new int[GruntLineup];
            var ids = new int[GruntLineup];
            for (int i = 0; i < GruntLineup; i++)
            {
                cards[i] = deck[deck.Count - 1];
                deck.RemoveAt(deck.Count - 1);
                ids[i] = PokemonRules.Spawn(s, cards[i], SeatId.Galactic, (int)DeckId.GalacticGrunt);
            }
            var b = NewBattle(s, BattleKind.Galactic, s.Seats[seat].Cell);
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Galactic, ids);
            Announce(s, ctx, fx, b);
            if (fx.On) fx.Add(new GalacticBattle { Seat = seat, Leader = GalacticLeader.Grunt, LineupCount = GruntLineup, Lineup = cards });
            Run(s, ctx, fx);
        }

        /// <summary>
        /// 간부·태홍 명단의 답(Lineup, A16): 고른 카드를 덱에서 꺼내 앞면으로 놓고(GalacticBattle — 고른 순서 = Options 순서) 배틀을 시작한다.
        /// 명단 공개가 도전자의 첫 포켓몬보다 먼저다(원작 커뮤니티 확인). c.Subject = 도전한 좌석, a.Values = Options 자리.
        /// </summary>
        public static void ApplyLineup(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            int seat = c.Subject;
            var cards = new int[a.Values.Count];
            var ids = new int[cards.Length];
            var leader = GalacticTrainer.None;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = c.Options[a.Values[i]];
                leader = ctx.Cards.Pokemon[cards[i]].Trainer;
                var deck = leader == GalacticTrainer.Cyrus ? DeckId.GalacticBoss : DeckId.GalacticCommander;
                s.Decks[(int)deck].Remove(cards[i]);
                ids[i] = PokemonRules.Spawn(s, cards[i], SeatId.Galactic, (int)deck);
            }
            bool boss = leader == GalacticTrainer.Cyrus;
            var b = NewBattle(s, BattleKind.Galactic, s.Seats[seat].Cell);
            SeatSide(s, b.Sides[0], seat);
            VirtualSide(b.Sides[1], SeatId.Galactic, ids);
            Announce(s, ctx, fx, b, boss ? BattleBgm.GalacticBoss : BattleBgm.GalacticCommander);
            if (fx.On) fx.Add(new GalacticBattle { Seat = seat, Leader = boss ? GalacticLeader.Cyrus : GalacticLeader.Commander, LineupCount = cards.Length, Lineup = cards });
            Run(s, ctx, fx);
        }

        /// <summary>종막 배신자 연전의 한 판(S16_3, HosFlow): Side 0 = 배신자(도전자 — 우선권 토큰), Side 1 = 상대. 말은 움직이지 않고 배틀 칸 = 배신자의 칸.</summary>
        public static void StartTraitor(GameState s, Ctx ctx, Emitter fx, int traitor, int target)
        {
            var b = NewBattle(s, BattleKind.Traitor, s.Seats[traitor].Cell);
            SeatSide(s, b.Sides[0], traitor);
            SeatSide(s, b.Sides[1], target);
            Announce(s, ctx, fx, b);
            Run(s, ctx, fx);
        }

        /// <summary>
        /// MapAction 도전(rulings G7·G13). 대상이 갤럭시단이면 1막 조무래기 배틀. 좌석이면 ChallengeIssued 뒤, 마을 안이면 응답을 묻고(Respond, Continues = 이 Seq)
        /// 마을 밖이면 거절할 수 없어 바로 배틀한다(p.3). 그 턴에 그 좌석에는 다시 도전하지 않는다.
        /// </summary>
        public static void Challenge(GameState s, Ctx ctx, Emitter fx, int seat, int target)
        {
            if (target == SeatId.Galactic)
            {
                // 호수·신오신전이면 간부·태홍(명단부터 — HosFlow), 아니면 고향 마을의 조무래기(A1)
                var leader = HosFlow.LeaderAt(s, ctx, seat);
                if (leader != GalacticTrainer.None) HosFlow.OpenLineup(s, ctx, fx, seat, leader);
                else StartGalactic(s, ctx, fx, seat);
                return;
            }
            s.Used.Add(new UsageMark { Seat = seat, Kind = UsageKind.Challenge, Key = target, Scope = s.TurnId });
            int cell = s.Seats[target].Cell;
            bool canRefuse = BattleRules.InVillage(ctx, cell);
            if (fx.On) fx.Add(new ChallengeIssued { Seat = seat, From = seat, To = target, Cell = cell, Remote = false, CanRefuse = canRefuse });
            if (canRefuse)
            {
                ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.Challenge, Seat = target, Subject = seat });
                return;
            }
            if (fx.On) fx.Add(new ChallengeAnswered { Seat = target, To = target, Accept = true, ByTimeout = false });
            StartTrainer(s, ctx, fx, seat, target);
        }

        /// <summary>도전 응답(Respond/Challenge). 거절이면 건 좌석의 턴이 이어진다.</summary>
        public static void ApplyRespond(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            int challenger = c.Subject;
            bool accept = a.Kind == AnswerKind.Accept;
            if (fx.On) fx.Add(new ChallengeAnswered { Seat = c.Seat, To = c.Seat, Accept = accept, ByTimeout = fx.Auto == AutoKind.Timeout });
            if (accept) StartTrainer(s, ctx, fx, challenger, c.Seat);
            else if (TrainerRules.ScoopRefused(s, ctx, fx, challenger)) { } // interviewers B의 도전을 거절했다 — 물은 자리로(rulings A38)
            else if (s.Phase == Phase.Turn && s.ActiveSeat == challenger) TurnFlow.OpenMapAction(s, ctx, fx, challenger);
        }

        static BattleState NewBattle(GameState s, BattleKind kind, int cell) =>
            new BattleState { Id = s.NextBattleId++, Kind = kind, Cell = cell, Step = BattleStep.SendIn, Turn = 0, ActiveSide = -1 };

        static void SeatSide(GameState s, BattleSide side, int seat)
        {
            side.Owner = seat;
            side.Responder = seat;
            var st = s.Seats[seat];
            for (int i = 0; i < st.Party.Length; i++)
                if (st.Party[i] >= 0 && PokemonRules.Fights(st, i)) side.Roster.Add(st.Party[i]); // 잠긴 칸에 둔 포켓몬은 빼고(pokemon_ranger B, rulings A39)
        }

        static void VirtualSide(BattleSide side, int owner, params int[] roster)
        {
            side.Owner = owner;
            side.Responder = owner;
            side.Roster.AddRange(roster);
        }

        static void Announce(GameState s, Ctx ctx, Emitter fx, BattleState b, BattleBgm? bgm = null)
        {
            s.Battle = b;
            s.Phase = Phase.Battle;
            if (!fx.On) return;
            var cell = b.Cell >= 0 ? ctx.Board[b.Cell] : null;
            fx.Add(new BattleStarted
            {
                BattleId = b.Id, Kind = b.Kind,
                Sides = new[]
                {
                    new BattleSideInfo { Owner = b.Sides[0].Owner, Seat = b.Sides[0].Responder },
                    new BattleSideInfo { Owner = b.Sides[1].Owner, Seat = b.Sides[1].Responder },
                },
                Cell = b.Cell, Biome = cell != null ? cell.Biome : Biome.None, Climate = cell != null ? cell.Climate : Climate.None,
                BgmKey = (int)(bgm ?? BattleRules.BgmOf(b.Kind)), Uncapturable = b.Uncapturable, FullHealed = false,
            });
        }

        // ── 진행 루프 ──

        /// <summary>열린 선택이 없는 동안 지금 단계를 한 걸음씩 돈다(01 §3-1 ③). 배틀이 끝나거나 선택·창이 열리면 멈춘다.</summary>
        public static void Run(GameState s, Ctx ctx, Emitter fx)
        {
            for (int guard = 0; s.Battle != null && s.Pending.Count == 0; guard++)
            {
                if (guard > 1000) throw new InvalidOperationException($"배틀 {s.Battle.Id}: 진행이 멈추지 않는다(단계 {s.Battle.Step})");
                // 창에서 쌓인 효과 프레임을 먼저 돈다(01 §3-1 ③ 「Stack 맨 위 효과나 지금 자동 단계」). 선택이 열리면 멈춘다
                if (s.Stack.Count > 0)
                {
                    EffectRules.Run(s, ctx, fx);
                    continue;
                }
                var b = s.Battle;
                switch (b.Step)
                {
                    case BattleStep.SendIn:
                        SendInStep(s, ctx, fx, b);
                        break;
                    case BattleStep.Order:
                        OrderStep(s, ctx, fx, b);
                        break;
                    case BattleStep.TurnStart: // 쓰지 않는 단계(저장된 값 자리) — 울트라 온슬롯(A21-1)은 SelectStep 머리에서 뽑는다
                        b.Step = BattleStep.SelectAction;
                        break;
                    case BattleStep.SelectAction:
                        SelectStep(s, ctx, fx, b);
                        break;
                    case BattleStep.PreRoll:
                        if (TrainerRules.BeforeRoll(s, ctx, b)) break; // 트레이너 카드의 굴리기 전 물음(rulings A34) — 프레임이 끝나면 이 단계로 돌아온다
                        b.Step = BattleStep.Roll;
                        Reactions.Open(s, ctx, fx, Window.BeforeRoll, true);
                        break;
                    case BattleStep.Roll:
                        if ((b.CardAsk & TrainerRules.AskedRolled) == 0)
                        {
                            RollStep(s, ctx, fx, b);
                            b.CardAsk |= TrainerRules.AskedRolled;
                            if (TrainerRules.Rolled(s, ctx, b)) break; // veteran R: 굴린 바로 뒤, 면 바꾸기 전에 심볼을 더할지 묻는다(rulings A34)
                        }
                        AfterRollEffects(s, ctx);
                        b.Step = BattleStep.PostRoll;
                        EffectRules.AskOrder(s, ctx, fx); // 결정 72: 면 바꾸기 순서로 결과가 갈리면 공격자에게 묻는다(답하면 쌓인 프레임을 그 순서로)
                        break;
                    case BattleStep.PostRoll:
                        TrainerRules.Amplify(s, ctx, b); // guitarist B: 심볼만큼 앰프를 올린다 — 쓸지는 굴린 뒤 반응 창에서(사용자 결정 134, rulings A41)
                        b.Step = BattleStep.PreDamage;
                        UbOnslaught.Sharpen(s, ctx, fx, b); // 울트라 온슬롯 2: 다른 면 바꾸기가 다 돈 뒤 남은 심볼 → 크리티컬(rulings A56 ③)
                        if (!EffectRules.DiceLocked(s, ctx) && CraftRules.AskAttack(s, ctx, fx, b)) break; // 매혹적인 스튜: 면 바꾸기·순서 선택 뒤에 쓸지 묻는다(답이 굴린 뒤 창을 연다)
                        Reactions.Open(s, ctx, fx, Window.AfterRoll, true);
                        break;
                    case BattleStep.PreDamage:
                        if (TrainerRules.Substitute(s, ctx, b)) break; // 대타출동: 피해 직전에 교체해 들어올지 묻는다(rulings A41)
                        DamageStep(s, ctx, fx, b);
                        b.Step = BattleStep.DamageWindow;
                        break;
                    case BattleStep.DamageWindow: // 기술 효과(자동)가 다 돈 뒤 같은 창의 반응(G6 공격 쪽 먼저, G14)
                        b.Step = BattleStep.ApplyDamage;
                        if (s.Options.GalacticRetributionTiming == GalacticRetributionTiming.BeforeDamage) Reactions.Open(s, ctx, fx, Window.BeforeDamage, true);
                        break;
                    case BattleStep.ApplyDamage:
                        ApplyBuffer(s, ctx, fx, b);
                        TrainerRules.AfterAttack(s, ctx, fx, b); // undivided_acolyte R 체력 2 이하 기절 · scuba_diver R 교체 표시(rulings A35)
                        b.Step = BattleStep.TurnEnd;
                        break;
                    case BattleStep.TurnEnd:
                    {
                        if (ExtraAttack(s, ctx, fx, b, true)) break; // 둘째 기술 · encore의 마지막 기술(rulings G21) — 턴 끝 효과 전
                        bool ends = EndsNow(s, ctx, b);
                        TurnEndStep(s, ctx, b, ends);
                        b.Step = BattleStep.FaintCheck;
                        // G5 기본(TurnEnd, 원작 커뮤니티 확인): 공격이었고 이번 피해로 배틀이 끝나지 않으면 턴 끝에 묻는다. 옮긴 피해는 독과 함께 FaintCheck가 적용한다.
                        if (!ends && b.Move != -1 && s.Options.GalacticRetributionTiming == GalacticRetributionTiming.TurnEnd) Reactions.Open(s, ctx, fx, Window.BattleTurnEnd, true);
                        break;
                    }
                    case BattleStep.FaintCheck:
                        ApplyBuffer(s, ctx, fx, b); // 턴 끝 효과(독 2)는 따로 모아 동시에 적용한다(p.13, §6-4)
                        FaintStep(s, ctx, fx, b);
                        b.Step = BattleStep.Replace;
                        // G5 다른 해석(AfterFaint): 공격이었으면 기절 확인(이번 기절의 2 저장) 뒤에 묻는다. 옮긴 피해는 Replace가 따로 적용한다.
                        if (b.Move != -1 && s.Options.GalacticRetributionTiming == GalacticRetributionTiming.AfterFaint) Reactions.Open(s, ctx, fx, Window.AfterDamage, true);
                        break;
                    case BattleStep.Replace:
                        ReplaceStep(s, ctx, fx, b);
                        break;
                    case BattleStep.Capture: // 볼을 굴린 뒤 창이 닫혔다 — 포획 판정을 잇는다(결정 76 ③)
                        CaptureRolled(s, ctx, fx, b);
                        break;
                    case BattleStep.Concluding: // team_rocket R: 갈 칸을 고른 뒤 미뤄 둔 배틀 끝을 잇는다(rulings A35)
                        Conclude(s, ctx, fx, (BattleResult)b.EndResult, b.EndWinner);
                        break;
                    case BattleStep.End:
                        return;
                    default:
                        throw new InvalidOperationException($"배틀 단계 {b.Step}");
                }
            }
        }

        /// <summary>출전(S7_1): 도전자 → 상대 순서. 둘 다 나오면 도전자가 우선권 토큰(처음부터 가진 쪽이 있으면 그대로 — 우두머리 A17, <see cref="StartAlpha"/>).</summary>
        static void SendInStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            var bench = new List<int>();
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var side = b.Sides[k];
                if (side.Active >= 0) continue;
                Bench(s, ctx, side, bench);
                if (bench.Count == 1)
                {
                    SendIn(s, ctx, fx, b, k, bench[0]);
                    continue;
                }
                ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.SendIn, Seat = side.Responder });
                return;
            }
            int holder = TokenHolder(b);
            if (holder < 0)
            {
                holder = 0;
                b.Sides[0].InitiativeToken = true;
            }
            if (fx.On) fx.Add(new InitiativeTokenAt { Side = holder });
            b.Step = BattleStep.Order;
        }

        /// <summary>
        /// 순서(01 §6-2): 비교 값(B5 InitiativeTie)이 높은 쪽이 선턴. 같으면 토큰 보유자가 쓸지 고른다(Reaction/InitiativeToken) —
        /// 묻기 전에 First = −1인 OrderDecided로 비교를 보이고, 답의 Seq에서 결과 OrderDecided를 낸다.
        /// </summary>
        static void OrderStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int holder = TokenHolder(b);
            if (holder < 0)
            {
                holder = 0;
                b.Sides[0].InitiativeToken = true;
            }
            var sides = OrderSides(s, ctx, b);
            int rapid = BreachRules.GoesFirst(s, ctx, b); // 균열 카드 rapid_strikes 「always goes first」(한쪽만 가졌을 때)
            // ninja_boy B(rulings A34): 토큰을 가진 좌석은 우선도가 달라도 토큰을 쓸지 고른다
            bool tie = sides[0].Final == sides[1].Final;
            if (rapid >= 0 || (!tie && !TrainerRules.AsksToken(s, ctx, b, holder, sides[holder].Final > sides[1 - holder].Final)))
            {
                int first = rapid >= 0 ? rapid : sides[0].Final > sides[1].Final ? 0 : 1;
                if (fx.On) fx.Add(new OrderDecided { Sides = sides, First = first, TokenUsed = false, TokenTo = holder });
                Decide(b, first);
                return;
            }
            if (fx.On) fx.Add(new OrderDecided { Sides = sides, First = -1, TokenUsed = false, TokenTo = holder });
            ChoiceRules.Open(s, ctx, fx, new PendingChoice
            {
                Kind = ChoiceKind.Reaction, Sub = ChoiceSub.InitiativeToken, Seat = b.Sides[holder].Responder, Subject = holder,
                Source = SourceRef.Rule(RuleSource.InitiativeToken), Count = tie ? 0 : 1, // Count 1 = 우선도가 다른데 묻는다(ninja_boy B) — 안 쓰면 높은 쪽이 선턴
            });
        }

        static OrderSide[] OrderSides(GameState s, Ctx ctx, BattleState b)
        {
            var r = new OrderSide[BattleState.SideCount];
            var mods = new List<Mod>();
            for (int k = 0; k < r.Length; k++)
            {
                int v = BattleRules.OrderValue(s, ctx, b.Sides[k].Active, mods, out int basePriority);
                r[k] = new OrderSide { Base = basePriority, Mods = mods.ToArray(), Final = v };
            }
            return r;
        }

        /// <summary>우선권 토큰 답(S7_1): 쓰면 보유자가 선턴이고 토큰은 상대에게, 안 쓰면 토큰이 없는 쪽이 선턴.</summary>
        public static void ApplyToken(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var b = s.Battle;
            int k = c.Subject >= 0 ? c.Subject : ChoiceRules.BattleSideOf(s, c.Seat);
            bool used = a.Kind == AnswerKind.Accept;
            var values = OrderSides(s, ctx, b);
            // 안 쓰면: 동률은 토큰이 없는 쪽, 우선도가 다르면(ninja_boy B의 물음) 높은 쪽이 선턴
            int first = used ? k : c.Count == 0 || values[0].Final == values[1].Final ? 1 - k : values[0].Final > values[1].Final ? 0 : 1;
            if (used)
            {
                b.Sides[k].InitiativeToken = false;
                b.Sides[1 - k].InitiativeToken = true;
            }
            if (fx.On) fx.Add(new OrderDecided { Sides = values, First = first, TokenUsed = used, TokenTo = TokenHolder(b) });
            Decide(b, first);
            if (used && c.Count != 0 && values[k].Final < values[1 - k].Final) TrainerRules.Used(s, fx, b.Sides[k].Owner, AbilityColor.Blue); // ninja_boy B: 느린 쪽이 토큰으로 먼저(rulings A34)
            if (used) TrainerRules.TokenUsed(s, ctx, b, k); // ninja_boy R: 독·혼란·졸림 중 하나(강제 고르기 — 진행 루프가 그 프레임부터 돈다)
            Run(s, ctx, fx);
        }

        static void Decide(BattleState b, int first)
        {
            b.ActiveSide = first;
            b.Step = BattleStep.SelectAction;
            b.Reorder = false;
            b.SwitchOffered = false;
        }

        static int TokenHolder(BattleState b)
        {
            for (int k = 0; k < BattleState.SideCount; k++)
                if (b.Sides[k].InitiativeToken) return k;
            return -1;
        }

        /// <summary>배틀 턴 시작: 차례 쪽의 BattleAction. 가상 쪽(야생·갤럭시단)의 답이 하나뿐이면 묻지 않고 새 Seq(Auto)로 쓴다.</summary>
        static void SelectStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            var side = b.Sides[k];
            b.Turn++;
            b.Move = -1;
            NewAttack(b);
            b.SwitchAfter = -1;
            b.ScubaBy = -1;
            b.PrismUsed = 0; // 프리즘 아머는 배틀 턴마다(S17_4)
            UbTrial.TurnStarted(s, ctx, b); // 의지의 시련: 내 배틀 턴마다 크리티컬 수를 한 턴씩 민다(S17_7)
            // 둘째 기술·마지막 기술(rulings G21)은 배틀 턴마다 · 받은 피해는 한 턴씩 민다(destiny_bond 「damage taken last turn」)
            b.SecondBy = b.SecondEffect = b.SecondTarget = -1;
            b.InSecond = false;
            b.FirstMove = b.ExtraMove = b.ExtraType = b.FinalSide = b.FinalMove = -1;
            b.ExtraFinal = false;
            StateParts.CopyInts(b.TakenLast, b.TakenNow);
            b.TakenNow.Clear();
            // 울트라 온슬롯(rulings A21-1 · A56): UB·네크로즈마의 배틀 턴이 시작될 때 1장 — 1 = 이번 턴 행동 없음(회복은 버퍼로) · 5 = 알파 스트라이크 두 번(둘째는 OfferSecond가 잇는다)
            int onslaught = UbOnslaught.TurnStarted(s, ctx, fx, b);
            if (onslaught == UbOnslaught.Rest)
            {
                b.Step = BattleStep.ApplyDamage;
                return;
            }
            if (onslaught == UbOnslaught.TwinStrike)
            {
                b.SecondBy = side.Active;
                b.SecondTarget = b.Sides[1 - k].Active;
                Act(s, ctx, fx, k, UbOnslaught.Strike(s, ctx, b));
                return;
            }
            var c = new PendingChoice { Kind = ChoiceKind.BattleAction, Seat = side.Responder };
            if (side.Responder < 0)
            {
                var legal = new List<ChoiceAnswer>();
                ChoiceRules.Legal(s, ctx, c, legal);
                if (legal.Count == 1)
                {
                    fx.Begin(side.Responder, ActionKind.BattleAction, AutoKind.Auto);
                    Act(s, ctx, fx, k, legal[0]);
                    return;
                }
            }
            ChoiceRules.Open(s, ctx, fx, c);
        }

        /// <summary>배틀 선택의 답(SendIn·NextPokemon·SwitchWindow·BattleAction). 적용한 뒤 진행을 잇는다.</summary>
        public static void ApplyChoice(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var b = s.Battle;
            int k = ChoiceRules.BattleSideOf(s, c.Seat);
            switch (c.Kind)
            {
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                    SendIn(s, ctx, fx, b, k, a.Target);
                    break;
                case ChoiceKind.SwitchWindow:
                    if (a.Kind == AnswerKind.Pick)
                    {
                        int left = b.Sides[k].Active;
                        LeaveField(s, fx, b, k, SwitchReason.Switch);
                        // scuba_diver R 「the user may switch out and then heal all status effects」(Source = 그 카드, rulings A35) — 기술 효과의 교체처럼 순서는 그대로
                        if (c.Source.Kind == SourceKind.TrainerCard) TrainerRules.Dived(s, ctx, fx, b.Sides[k].Owner, left);
                        SendIn(s, ctx, fx, b, k, a.Target);
                        // 기술 효과로 교체(Source = 그 기술, u_turn 등)면 순서를 그대로 두고 상대가 다음이다(p.8 「When you use a move effect that allows you to switch out: Turn order is maintained」)
                        if (c.Source.Kind != SourceKind.Move && c.Source.Kind != SourceKind.TrainerCard) b.Reorder = true;
                    }
                    break;
                case ChoiceKind.BattleAction:
                    Act(s, ctx, fx, k, a);
                    break;
                default:
                    throw new InvalidOperationException($"{c.Kind}: 배틀 선택이 아니다");
            }
            Run(s, ctx, fx);
        }

        /// <summary>기술(MoveSelected → PreRoll) · 볼(포획 A7) · 기권(S7_8).</summary>
        static void Act(GameState s, Ctx ctx, Emitter fx, int k, ChoiceAnswer a)
        {
            var b = s.Battle;
            switch (a.Kind)
            {
                case AnswerKind.UseMove:
                    // lunge·acrobatics 「This move can be used from your party: Switch the user in and then use this attack」(Arg = 벤치 포켓몬, rulings G20): 들여보내고 그 포켓몬이 공격한다 — 순서 그대로
                    if (a.Arg >= 0 && a.Arg != b.Sides[k].Active)
                    {
                        LeaveField(s, fx, b, k, SwitchReason.Switch);
                        SendIn(s, ctx, fx, b, k, a.Arg);
                    }
                    StampRules.Acted(b, k); // 기술을 쓰면 이어진 포획 실패가 끊긴다(스탬프 1)
                    TrainerRules.MoveChosen(s, ctx, fx, b, k, a.Target); // poke_maniac B: 상대의 시그니처 기술을 골랐으면 AbilityUsed(rulings A34)
                    if (a.Target == BattleRules.AlphaStrike) b.TypeTo = a.Values[0]; // 알파 스트라이크: 답이 고른 배울 수 있는 타입 = 이번 공격의 타입(rulings A26)
                    UseMove(s, ctx, fx, b, k, a.Target);
                    break;
                case AnswerKind.ThrowBall:
                    Capture(s, ctx, fx, b, k, (Ball)a.Target);
                    break;
                case AnswerKind.UseAbility:
                    if (a.Target == TrainerRules.CherishTarget)
                    {
                        Cherish(s, ctx, fx, b, k); // gentleman R 프레셔스 볼(rulings A41)
                        break;
                    }
                    // lady R(rulings A35): 턴을 쓰지 않는다 — 고치고 배틀 행동을 다시 묻는다(원작 커뮤니티 확인)
                    TrainerRules.UsePotion(s, ctx, fx, b, k, a.Target);
                    ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.BattleAction, Seat = b.Sides[k].Responder });
                    break;
                case AnswerKind.Forfeit:
                    if (fx.On) fx.Add(new Forfeited { Seat = b.Sides[k].Owner });
                    Conclude(s, ctx, fx, BattleResult.Forfeited, 1 - k);
                    break;
                default:
                    throw new InvalidOperationException($"BattleAction {a.Kind}");
            }
        }

        /// <summary>k 쪽 출전 포켓몬이 그 기술을 쓴다(MoveSelected → PreRoll): 배틀 행동 · 둘째 기술 · 마지막 기술(rulings G21)이 같이 쓴다.</summary>
        static void UseMove(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int move)
        {
            b.Move = move;
            HeldItems.MoveChosen(s, ctx, b.Sides[k].Active, move); // 결단의 머리띠: 나갈 때까지 이 기술만(S10)
            if (fx.On) fx.Add(new MoveSelected { Side = k, MoveKey = move, Type = BattleRules.AttackType(s, ctx, move), Power = BattleRules.MoveStrength(ctx, move) });
            b.Step = BattleStep.PreRoll;
            // 굴리기 전에 정하는 기술 효과(타입 고르기 · belch — rulings G20 · 복사·섞기 · 맞히기 — G21): 굴리기 전 반응 창보다 먼저 돈다(진행 루프가 Stack부터)
            EffectRules.Trigger(s, ctx, Window.BeforeRoll);
        }

        /// <summary>공격 하나의 임시값을 비운다(배틀 턴 시작 · 둘째 기술 · 마지막 기술).</summary>
        static void NewAttack(BattleState b)
        {
            b.AttackStrength = 0;
            b.FatePower = 0;
            b.Faces.Clear();
            b.TypeTo = -1;
            b.StrengthTo = 0;
            b.Called = b.EffectsOf = b.Guess = -1;
            b.CardAsk = 0;
            b.AmpDamage = 0;
        }

        // ── 추가 공격: 둘째 기술 · 마지막 기술(M5 핸들러 갈래, rulings G21) ──

        /// <summary>
        /// 공격이 끝난 자리에서 추가 공격을 잇는다. 배틀 턴 끝 단계 처음(turnEnd — 공격 피해를 적용한 뒤, 턴 끝 효과 전): 끝난 마지막 기술을 되돌리고 → encore의 마지막 기술 →
        /// 둘째 기술(RuleOptions.SecondMoveTurnEnd = Once). 교체 단계(both 출전, turnEnd = false): 둘째 기술(EachAttack — 첫 공격의 턴 끝·기절 확인·교체가 다 돈 뒤).
        /// 고른 추가 기술이 있으면 그 공격을 시작한다. 선택을 열었거나 공격을 시작했거나 배틀이 끝났으면 true.
        /// </summary>
        static bool ExtraAttack(GameState s, Ctx ctx, Emitter fx, BattleState b, bool turnEnd)
        {
            if (b.ExtraMove != -1)
            {
                StartExtra(s, ctx, fx, b, turnEnd);
                return true;
            }
            bool once = s.Options.SecondMoveTurnEnd == SecondMoveTurnEnd.Once;
            if (!turnEnd) return !once && b.SecondBy >= 0 && OfferSecond(s, ctx, fx, b);
            if (b.FinalSide >= 0) EndFinal(s, ctx, b);
            else if (OfferFinal(s, ctx, b)) return true;
            return once && b.SecondBy >= 0 && OfferSecond(s, ctx, fx, b);
        }

        /// <summary>
        /// encore(「If the user faints when it is next attacked, the user may use a final move before fainting」): 이 공격으로 받는 포켓몬의 체력이 0이 됐고 그 보정(FinalMove — 이 공격에 쓰인다)이 있으면
        /// 그 encore 절의 프레임(StepIndex 1)을 쌓아 마지막 기술을 묻는다. 공격한 포켓몬도 이 공격 동안 쓰러졌으면 없다(원작 커뮤니티 확인).
        /// </summary>
        static bool OfferFinal(GameState s, Ctx ctx, BattleState b)
        {
            int k = b.ActiveSide;
            if (k < 0 || b.Move == -1) return false;
            int att = b.Sides[k].Active, def = b.Sides[1 - k].Active;
            if (def < 0 || !PokemonRules.IsFainted(ctx, s.Pokemon[def])) return false;
            var m = ModRules.TakeFinalMove(s, def);
            if (m == null || att < 0 || PokemonRules.IsFainted(ctx, s.Pokemon[att])) return false;
            s.Stack.Add(new EffectFrame { EffectId = m.Value, StepIndex = 1, Source = m.Source, Owner = b.Sides[1 - k].Owner, User = def, Target = att });
            return true;
        }

        /// <summary>
        /// 둘째 기술을 묻는다(그 효과 절의 프레임을 StepIndex 1로 — 처리기 second_move). 그 포켓몬이 아직 출전 중이고 쓰러지지 않았어야 한다. 대상이 첫 공격으로 쓰러졌으면(바뀌었으면)
        /// RuleOptions.SecondMoveAfterKo일 때만 — 그때 Once면 여기서 기절을 알리고 다음 포켓몬을 받는다(교체 창 없이 [가정 G21]). 못 쓰면 둘째 기술은 없던 것(한도도 쓰지 않는다).
        /// </summary>
        static bool OfferSecond(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            var side = b.Sides[k];
            var other = b.Sides[1 - k];
            int user = b.SecondBy;
            int def = other.Active;
            bool ko = def < 0 || def != b.SecondTarget || PokemonRules.IsFainted(ctx, s.Pokemon[def]);
            bool twin = b.SecondEffect < 0; // 울트라 온슬롯 5의 둘째 알파 스트라이크: 상대가 쓰러졌어도 다음 포켓몬에게(rulings A56 ⑥ — UB에게 유리하게)
            if (side.Active != user || PokemonRules.IsFainted(ctx, s.Pokemon[user]) || (ko && !twin && !s.Options.SecondMoveAfterKo))
            {
                b.SecondBy = -1;
                return false;
            }
            if (def >= 0 && PokemonRules.IsFainted(ctx, s.Pokemon[def]))
            {
                ReportFaint(s, ctx, fx, b, 1 - k);
                if (TryEnd(s, ctx, fx, b)) return true;
            }
            if (other.Active < 0)
            {
                var bench = new List<int>();
                Bench(s, ctx, other, bench);
                if (bench.Count == 0)
                {
                    b.SecondBy = -1;
                    return false;
                }
                if (bench.Count > 1)
                {
                    ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.NextPokemon, Seat = other.Responder });
                    return true;
                }
                SendIn(s, ctx, fx, b, 1 - k, bench[0]);
            }
            if (twin)
            {
                // 묻지 않고 알파 스트라이크(타입은 지금 받는 포켓몬에게 원칙 AI가 고른다) — 진행 루프가 다음 걸음에 StartExtra로 잇는다
                b.SecondBy = -1;
                b.ExtraMove = BattleRules.AlphaStrike;
                b.ExtraType = PrincipleRules.AlphaType(ctx, s.Pokemon[user], s.Pokemon[other.Active]);
                return true;
            }
            s.Stack.Add(new EffectFrame { EffectId = b.SecondEffect, StepIndex = 1, Source = ctx.Effects.Effects[b.SecondEffect].Source, Owner = side.Owner, User = user, Target = other.Active });
            return true;
        }

        /// <summary>
        /// 고른 추가 기술의 공격을 시작한다. 마지막 기술: 원래 공격을 끝내고(한 번짜리 보정) 차례를 그 포켓몬 쪽으로 잠시 넘긴다(끝나면 <see cref="EndFinal"/>).
        /// 둘째 기술: Once면 첫 공격의 공격 끝(한 번짜리 보정 · 이 배틀에서 쓴 기술 · 직전 기술 둘째 칸)을 여기서 하고, 턴 끝은 둘째 뒤에 한 번이다.
        /// </summary>
        static void StartExtra(GameState s, Ctx ctx, Emitter fx, BattleState b, bool turnEnd)
        {
            int move = b.ExtraMove, type = b.ExtraType;
            bool final = b.ExtraFinal;
            b.ExtraMove = b.ExtraType = -1;
            b.ExtraFinal = false;
            int k = b.ActiveSide;
            int att = b.Sides[k].Active, def = b.Sides[1 - k].Active;
            if (final)
            {
                ModRules.AttackDone(s, att, def);
                b.FinalSide = k;
                b.FinalMove = b.Move;
                b.ActiveSide = 1 - k;
                NewAttack(b);
                if (move == BattleRules.AlphaStrike) b.TypeTo = type; // 고른 배울 수 있는 타입(Act와 같다, rulings A26)
                UseMove(s, ctx, fx, b, 1 - k, move);
                return;
            }
            if (turnEnd)
            {
                if (b.FinalMove == -1) ModRules.AttackDone(s, att, def == b.SecondTarget ? def : -1); // 마지막 기술 뒤면 이미 했다
                b.FirstMove = b.Move;
                b.UsedMoves.Add(att);
                b.UsedMoves.Add(b.Move);
            }
            b.FinalMove = -1;
            b.InSecond = true;
            NewAttack(b);
            if (move == BattleRules.AlphaStrike) b.TypeTo = type;
            UseMove(s, ctx, fx, b, k, move);
        }

        /// <summary>마지막 기술(encore)의 공격이 끝났다: 그 공격 끝(한 번짜리 보정) → 차례를 원래 쪽·기술로 되돌린다. FinalMove는 남겨 턴 끝 단계가 공격 끝을 두 번 하지 않게 한다.</summary>
        static void EndFinal(GameState s, Ctx ctx, BattleState b)
        {
            ModRules.AttackDone(s, b.Sides[b.ActiveSide].Active, b.Sides[b.FinalSide].Active);
            b.ActiveSide = b.FinalSide;
            b.Move = b.FinalMove;
            b.FinalSide = -1;
            NewAttack(b);
        }

        // ── 공격 ──

        /// <summary>굴림(01 §6-3·§6-4): 공격력 = clamp(기술 + 보정, 1, 9), 면 순서 그대로. 피로 +1, 피로 상태면 3 피해를 피해 버퍼로(A10, rulings G13).</summary>
        static void RollStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            int att = b.Sides[k].Active;
            int def = b.Sides[1 - k].Active;
            var mods = new List<Mod>();
            int power = BattleRules.RollPower(s, ctx, b.Move, att, def, mods); // 「공격력이 n이 된다」는 맨 앞(G14)
            b.Sides[k].BeastBoost = 0; // 비스트 부스트는 다음 공격 하나에(rulings A26)
            var faces = AttackDice.Roll(s.Rng, power);
            b.AttackStrength = power;
            b.Faces.Clear();
            b.Faces.AddRange(faces);
            var ap = s.Pokemon[att];
            int before = ap.Fatigue;
            int hurt = FatigueRules.Roll(ref ap.Fatigue, false, s.Options);
            if (hurt > 0) Buffer(b, att, -hurt, SourceRef.Rule(RuleSource.Fatigue));
            if (fx.On)
            {
                fx.Add(new DiceRolled
                {
                    Side = k, PokemonId = att, Kind = DiceKind.Attack, Type = BattleRules.AttackType(s, ctx, b.Move), Faces = faces, Sum = BattleRules.Sum(faces),
                    Power = power, PowerMods = mods.ToArray(), FatigueBefore = before, FatigueAfter = ap.Fatigue,
                });
                fx.Add(new FatigueChanged { PokemonId = att, Before = before, After = ap.Fatigue, Cause = FatigueCause.Roll });
            }
            // 굴림에 면을 더하는 지속 보정(「add 1 regular hit to the roll」): 굴린 바로 뒤 — 면 바꾸기(마비·졸림·지우기)보다 먼저(원작 커뮤니티 확인, rulings G19)
            ModRules.AddFaces(s, ModRules.Scanned(s, ctx, att), ModRules.Scanned(s, ctx, def), b.Faces, fx); // 체력 0인 포켓몬의 것은 훑지 않는다(rulings A24 ③)
        }

        /// <summary>굴린 뒤 면 바꾸기(01 §6-4 「굴림 → 면 변환」): 상태이상 기본 효과(마비 다음 공격·졸림 둘, G14)를 쌓는다. 굴린 뒤 반응 창보다 먼저 돈다.</summary>
        static void AfterRollEffects(GameState s, Ctx ctx) => EffectRules.Trigger(s, ctx, Window.AfterRoll);

        /// <summary>
        /// 배틀 턴 끝(S7_6, G14): 이번 턴 포켓몬의 직전 기술을 적는다(볼을 던진 턴이면 없음, 혼란 「직전 턴과 같은 기술」) → 턴 끝 효과(독)를 쌓는다.
        /// 쌓인 피해는 FaintCheck가 한꺼번에 적용한다.
        /// </summary>
        static void TurnEndStep(GameState s, Ctx ctx, BattleState b, bool ends)
        {
            int att = b.ActiveSide >= 0 ? b.Sides[b.ActiveSide].Active : -1;
            if (att >= 0)
            {
                s.Pokemon[att].LastMove = b.Move;
                // 둘째 기술을 쓴 턴(Once)이면 첫 기술도 「used last turn」(RuleOptions.SecondMoveLastTurn, rulings G21)
                s.Pokemon[att].LastMove2 = b.InSecond && b.FirstMove != -1 && s.Options.SecondMoveLastTurn == SecondMoveLastTurn.BothMoves ? b.FirstMove : -1;
                if (b.Move != -1)
                {
                    b.UsedMoves.Add(att); // rollout·lift_off 「Each time this attack is used this battle」
                    b.UsedMoves.Add(b.Move);
                }
            }
            // 공격이 끝났다: 한 번짜리 지속 보정(다음 공격할 때·다음 공격받을 때)을 쓴 것으로 지운다(G16). 볼을 던진 턴은 공격이 아니다. encore의 마지막 기술 뒤면 이미 했다(G21)
            if (b.Move != -1 && b.ActiveSide >= 0 && b.FinalMove == -1) ModRules.AttackDone(s, att, b.Sides[1 - b.ActiveSide].Active);
            // 피해 창 둘 중 턴 끝 창은 배틀이 끝나면 없다(p.9 독 「ignore when the battle ends」 · 원작 커뮤니티 확인)
            if (!ends)
            {
                EffectRules.Trigger(s, ctx, Window.BattleTurnEnd);
                ModRules.TurnEndHeal(s, ctx, b, att); // 「Heal 2 damage at the end of your turns」: 턴 끝 피해 창에 독과 함께(rulings G19)
                TrainerRules.TurnEnd(s, ctx, b, att); // fairy_tale_girl R: 졸림인 페어리 타입은 턴 끝에 1 회복(rulings A35)
            }
        }

        /// <summary>
        /// 지금 피해(턴 안의 피해 창)까지로 배틀이 끝나나 — <see cref="TryEnd"/>와 같은 판정을 기절 확인 전에 미리 본다(상태를 바꾸지 않는다).
        /// 출전 중이지만 체력 0인 포켓몬은 쓰러진 것으로 친다.
        /// </summary>
        static bool EndsNow(GameState s, Ctx ctx, BattleState b)
        {
            bool u0 = BattleRules.SideHasUsable(s, ctx, b.Sides[0]);
            bool u1 = BattleRules.SideHasUsable(s, ctx, b.Sides[1]);
            switch (b.Kind)
            {
                case BattleKind.Galactic:
                case BattleKind.Trainer:
                case BattleKind.Traitor:
                    return !u0 || !u1;
                default:
                {
                    if (!u1 && LegendRules.SecondForm(s, ctx, b, b.Sides[1].Active) < 0) return true; // 신의 첫 폼은 쓰러져도 둘째 폼이 나온다(rulings A28 ①)
                    if (!u1) return !u0; // 둘째 폼이 나오고 내가 바닥났으면 도망(모두 쓰러뜨린 뒤 — 회복한 둘째 폼은 상태이상이 없다)
                    var wild = s.Pokemon[WildOf(b)];
                    int mine = b.Sides[0].Active;
                    bool mineDown = mine < 0 || PokemonRules.IsFainted(ctx, s.Pokemon[mine]);
                    bool firstKo = mineDown && BattleRules.WildFleesAfterFirstKo(b.Kind, MapActions.GradeOf(ctx, wild.Def), wild.Alpha)
                        && TrainerRules.WildFlees(s, ctx, b, b.Sides[0].Fallen + (mine >= 0 ? 1 : 0)); // roller_skater B: 둘을 쓰러뜨린 뒤에만(rulings A36)
                    bool allDown = !u0 && FleesWhenAllDown(b.Kind);
                    return (!StatusRules.BlocksEscape(wild) && !ModRules.NoEscape(wild) && (firstKo || allDown)) || !u0;
                }
            }
        }

        /// <summary>
        /// 기술 사용 → 상성 → 피해 내역(01 §6-4 ①~⑦, G4) → 기술 효과(BeforeDamage 절, G14). 피해 계산은 효과 전 상태로 한다 — 「이미 X면」은 이번 공격이 건 상태를 보지 않는다.
        /// 단계값은 주사위 합·상성은 늘, 효과 피해(②, op마다)·2배(④)·감소(화상 −2)는 있을 때만. 문턱 ⑥·상한 ⑦은 그 op가 생길 때(M5·M6).
        /// 피해는 버퍼에 넣어 동시에 적용한다. 플랜 B면 은하의 응징 +1(A13). 끝에 기술 효과·혼란 프레임을 쌓는다(DamageWindow 전에 돈다).
        /// </summary>
        static void DamageStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            var side = b.Sides[k];
            int att = side.Active;
            int def = b.Sides[1 - k].Active;
            int move = b.Move;
            var type = BattleRules.AttackType(s, ctx, move); // 효과가 고른 타입(hidden_power 등, rulings G20)
            var src = new SourceRef(SourceKind.Move, move);
            UbOnslaught.Sharpen(s, ctx, fx, b); // 울트라 온슬롯 2: 굴린 뒤 창에서 다시 굴린 주사위의 심볼도(rulings A56 ③)
            if (fx.On) fx.Add(new MoveUsed { Side = k, PokemonId = att, MoveKey = move, Type = type, Category = BattleRules.CategoryOf(ctx, move), TargetId = def });

            var types = ctx.Cards.Pokemon[s.Pokemon[def].Def].Types;
            var effects = new List<Mod>();
            var calc = Damage(s, ctx, b, b.Faces, effects);
            int matchup = calc.Matchup, sum = calc.Sum, effect = calc.Effect, reduction = calc.Reduction, final = calc.Final;
            bool doubled = calc.Doubled;
            if (fx.On)
            {
                var defTypes = new PokemonType[types.Count];
                for (int i = 0; i < defTypes.Length; i++) defTypes[i] = types[i];
                fx.Add(new TypeMatchup { AttackerId = att, DefenderId = def, Type = type, DefTypes = defTypes, Delta = matchup, Typeless = type == PokemonType.Typeless });
                var steps = new List<DamageStep> { new DamageStep { Kind = DamageStepKind.Dice, Value = sum, Source = src } };
                foreach (var m in effects) steps.Add(new DamageStep { Kind = DamageStepKind.Effect, Value = m.Delta, Source = m.Source });
                steps.Add(new DamageStep { Kind = DamageStepKind.Effectiveness, Value = matchup, Source = SourceRef.None });
                if (calc.Analysis > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Effectiveness, Value = calc.Analysis, Source = calc.AnalysisSource }); // scientist R(rulings A32)
                // 2배 칩의 값 = 2배로 더해진 만큼(① + ② + ③)
                if (doubled) steps.Add(new DamageStep { Kind = DamageStepKind.Double, Value = sum + effect + matchup + calc.Analysis, Source = src });
                if (reduction > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Reduction, Value = -reduction, Source = new SourceRef(SourceKind.Status, (int)StatusEffect.Burned) });
                if (calc.Spirit > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Reduction, Value = calc.Spirit, Source = TrainerRules.SourceOf(s, s.Pokemon[att]) }); // black_belt R: 화상이 총 피해 +2로(rulings A35)
                if (calc.Shield > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Reduction, Value = -calc.Shield, Source = new SourceRef(SourceKind.Item, HeldItems.ItemOf(ctx, s.Pokemon[def], HeldItems.StarShield)) });
                // 받는 쪽 트레이너 카드(rulings A32): 총 데미지 감소(카드마다) · 문턱(실제로 0이 됐을 때만)
                if (calc.Card.Reductions != null)
                    foreach (var m in calc.Card.Reductions) steps.Add(new DamageStep { Kind = DamageStepKind.Reduction, Value = -m.Delta, Source = m.Source });
                if (calc.Nullified > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Threshold, Value = calc.Nullified, Source = calc.Card.NullifySource });
                if (calc.Cap > 0) steps.Add(new DamageStep { Kind = DamageStepKind.Cap, Value = calc.Cap, Source = calc.CapSource });
                fx.Add(new DamageBreakdown { TargetId = def, Steps = steps.ToArray(), Final = final });
            }
            if (calc.Opportune) TrainerRules.Used(s, fx, side.Owner, AbilityColor.Red, def);
            side.FirstAttackDone = true;
            Buffer(b, def, -final, src);
            TrainerRules.Dealt(s, ctx, fx, b, att, final); // aroma_lady R: 준 피해의 절반(올림) 회복(rulings A35)
            // barrier: 크리티컬 몫은 공격한 포켓몬이 받는다 — 공격이 아닌 효과 피해라 「공격받을 때」 효과(지닌물건·응징)는 돌지 않는다(rulings G20 ⑷)
            if (calc.Reflect > 0) Buffer(b, att, -calc.Reflect, calc.ReflectSource);
            HeldItems.Attacked(s, ctx, b, att, def); // 「첫 공격에만」 지닌물건(폭발수정·별 방패, S10)
            BreachRules.Attacked(s, ctx, b, att); // 균열 카드 nimble_frame: 공격하면 1 피해 · 턴 끝 뒤 교체 가능
            UbOnslaught.Attacked(s, ctx, b, att, def); // 울트라 온슬롯 4: 일반 타격 하나마다 공격한 포켓몬도 1 피해
            UbTrial.Attacked(s, ctx, fx, b, def, type, b.Faces); // 호수 시련(지식 · 의지): 이 공격으로 시련이 나아간다(S17_7, rulings A55)
            if (move == BattleRules.PlanB)
            {
                int before = side.Retribution;
                side.Retribution++;
                if (fx.On) fx.Add(new RetributionStored { Source = SourceRef.Rule(RuleSource.GalacticRetribution), Side = k, Before = before, After = side.Retribution, Cause = RetributionCause.PlanB });
            }
            TrainerRules.Ignition(s, ctx, b); // kindler R: 장작 1로 화상 + 2 피해를 물을 프레임 — 기술 효과가 끝난 뒤에 돈다(rulings A35)
            EffectRules.Trigger(s, ctx, Window.BeforeDamage);
        }

        /// <summary>피해 계산 한 번의 값(01 §6-4 ①~⑤): 주사위 합 · 효과 피해 합 · 상성 · 2배 · 화상 감소 · 최종.</summary>
        internal struct DamageCalc
        {
            public int Sum, Effect, Matchup, Reduction, Shield, Final;
            /// <summary>black_belt R: 화상인 격투 타입이 주는 총 피해 +2(화상의 −2 대신 — 없으면 0, rulings A35).</summary>
            public int Spirit;
            public bool Doubled;
            /// <summary>essentia R의 2배가 걸렸다(rulings A34).</summary>
            public bool Opportune;
            /// <summary>피해 상한(지속 보정 DamageCap — 「take at most 1 damage」)이 이번 피해를 실제로 줄였으면 그 상한, 아니면 0. CapSource = 건 출처.</summary>
            public int Cap;
            public SourceRef CapSource;
            /// <summary>
            /// 받는 포켓몬 대신 공격한 포켓몬이 받는 크리티컬 몫(barrier — rulings G20 ⑷, 결정 76 B3): 크리티컬 눈 + 크리티컬에 붙는 보정. 상성·2배·감소 없이 그대로다.
            /// 받는 쪽 셈에서는 효과 피해 −Reflect(출처 ReflectSource)로 빠진다. 없으면 0.
            /// </summary>
            public int Reflect;
            public SourceRef ReflectSource;
            /// <summary>scientist R의 상성 +1(③ — 없으면 0)과 그 출처(rulings A32).</summary>
            public int Analysis;
            public SourceRef AnalysisSource;
            /// <summary>받는 쪽 트레이너 카드의 감소·문턱·상한(TrainerRules.TakenMods). Nullified = 문턱이 이번 피해를 실제로 0으로 만들었으면 그 문턱, 아니면 0.</summary>
            public TrainerRules.Taken Card;
            public int Nullified;
        }

        /// <summary>
        /// 지금 공격(b.Move, 공격하는 쪽 → 받는 쪽)이 그 굴림(faces)으로 줄 피해. 실제 피해(DamageStep)와 결정 72 순서 미리 셈(EffectRules.Preview)이 같이 쓴다 — 상태를 바꾸지 않는다.
        /// effects가 있으면 효과 피해 Mod를 채운다.
        /// </summary>
        internal static DamageCalc Damage(GameState s, Ctx ctx, BattleState b, IReadOnlyList<AttackDieFace> faces, List<Mod> effects)
        {
            int att = b.Sides[b.ActiveSide].Active;
            int def = b.Sides[1 - b.ActiveSide].Active;
            var type = BattleRules.AttackType(s, ctx, b.Move);
            var types = ctx.Cards.Pokemon[s.Pokemon[def].Def].Types;
            var t1 = types[0];
            var t2 = types.Count > 1 ? types[1] : PokemonType.Typeless;
            var mods = effects ?? new List<Mod>();
            var r = new DamageCalc { Matchup = TypeChart.Bonus(type, t1, t2), Sum = AttackDice.Sum(faces) };
            int reckless = BreachRules.Reckless(ctx, s.Pokemon[att], s.Pokemon[def], r.Matchup); // 균열 카드 reckless_assault: 유리한 상성 +2(공격·피격)
            r.Matchup += reckless;
            r.Doubled = EffectRules.DamageMods(s, ctx, b.Move, att, def, faces, mods) | UbOnslaught.Doubles(s, b, def); // 울트라 온슬롯 6: 균열 카드가 붙은 상대에게 2배
            r.Opportune = TrainerRules.Opportune(s, ctx, b); // essentia R: 움직이지 않고 건 야생 배틀의 첫 공격은 2배(rulings A34)
            r.Doubled |= r.Opportune;
            if (ModRules.ReflectsCrits(s, ModRules.Scanned(s, ctx, def), out r.ReflectSource))
            {
                // 크리티컬 몫 = 그 굴림의 셈(눈 + 효과) − 크리티컬을 뺀 굴림의 셈. 크리티컬에 붙는 보정(「Critical hits deal +1 damage」)이 함께 돌아간다(원작 커뮤니티 확인)
                var plain = new List<AttackDieFace>(faces.Count);
                foreach (var f in faces) plain.Add(f == AttackDieFace.CriticalHit ? AttackDieFace.None : f);
                var rest = new List<Mod>();
                EffectRules.DamageMods(s, ctx, b.Move, att, def, plain, rest);
                int whole = r.Sum, spared = AttackDice.Sum(plain);
                foreach (var m in mods) whole += m.Delta;
                foreach (var m in rest) spared += m.Delta;
                r.Reflect = Math.Max(0, whole - spared);
                if (r.Reflect > 0) mods.Add(new Mod { Source = r.ReflectSource, Delta = -r.Reflect });
            }
            foreach (var m in mods) r.Effect += m.Delta;
            r.Spirit = TrainerRules.Spirited(s, ctx, s.Pokemon[att]) ? TrainerRules.SpiritDamage : 0;
            r.Reduction = r.Spirit == 0 && StatusRules.Has(s.Pokemon[att], StatusEffect.Burned) ? BattleRules.BurnReduction : 0;
            r.Shield = HeldItems.Reduction(ctx, s.Pokemon[def]); // 별 방패: 첫 공격에 2 덜 받는다(S10)
            // 트레이너 카드의 상시 보정(rulings A32): 공격 쪽 scientist R 상성 +1 · 받는 쪽 감소(delinquent · team_aqua · team_magma) · 문턱(bird_keeper) · 상한(poke_fan · roller_skater)
            r.Analysis = TrainerRules.EffectivenessBonus(s, ctx, att, r.Matchup, out r.AnalysisSource);
            r.Card = TrainerRules.TakenMods(s, ctx, def, true, type);
            var input = new DamageInput
            {
                DiceSum = r.Sum, EffectDamage = r.Effect, MoveType = type, DefenderType1 = t1, DefenderType2 = t2, EffectivenessBonus = r.Analysis + reckless, Double = r.Doubled,
                Reduction = r.Reduction + r.Shield + r.Card.Reduction - r.Spirit,
            };
            int open = DamageRules.Calculate(input, s.Options.DamageCapStage);
            input.NullifyAtOrBelow = r.Card.Nullify; // 「2 or less」(01 §6-4 ⑥)
            int uncapped = r.Card.Nullify > 0 ? DamageRules.Calculate(input, s.Options.DamageCapStage) : open;
            r.Nullified = uncapped < open ? r.Card.Nullify : 0;
            input.Cap = ModRules.DamageCap(s, def, out r.CapSource); // 「take at most 1 damage」(disguise, 01 §6-4 ⑦)
            if (r.Card.Cap > 0 && (input.Cap == 0 || r.Card.Cap < input.Cap))
            {
                input.Cap = r.Card.Cap; // 「can at most take 3·6 damage from a single attack」 — 겹치면 작은 값
                r.CapSource = r.Card.CapSource;
            }
            r.Final = input.Cap > 0 ? DamageRules.Calculate(input, s.Options.DamageCapStage) : uncapped;
            r.Cap = r.Final < uncapped ? input.Cap : 0;
            return r;
        }

        /// <summary>피해 버퍼에 (포켓몬, 체력 증감, 출처)를 더한다(p.13 동시 적용).</summary>
        internal static void Buffer(BattleState b, int pokemon, int delta, SourceRef source)
        {
            b.DamageBuffer.Add(pokemon);
            b.DamageBuffer.Add(delta);
            b.DamageBuffer.Add((int)source.Kind);
            b.DamageBuffer.Add(source.Id);
        }

        /// <summary>
        /// 버퍼를 한꺼번에 적용한다(같은 Index, 01 §3-6 ⑪). 깎은 출처를 그 쪽에 적어 둔다(기절 알림 ParentSource·ById — 상대가 깎았을 때만 ById, 반동·혼란·독은 −1).
        /// 독이면 회복은 0이 된다(G14). 상태이상 피해(독·혼란)는 HpChanged 대신 StatusTicked가 체력 변화를 싣는다.
        /// </summary>
        static void ApplyBuffer(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            var buf = b.DamageBuffer;
            if (buf.Count == 0) return;
            // 적용하기 전부터 체력 0인 출전 포켓몬(기라티나로 쓰러질 포켓몬 — 그 기술은 쓴다)은 이 회복을 받지 못한다(rulings A24 ③, 원작 커뮤니티 확인)
            var down = new int[BattleState.SideCount];
            for (int k = 0; k < down.Length; k++) down[k] = b.Sides[k].Active >= 0 && PokemonRules.IsFainted(ctx, s.Pokemon[b.Sides[k].Active]) ? b.Sides[k].Active : -2;
            List<int> benchKo = null;
            int prism = 0, prismOn = -1;
            bool fatigued = false;
            fx.BeginSameIndex();
            for (int i = 0; i < buf.Count; i += 4)
            {
                int p = buf[i];
                int delta = buf[i + 1];
                var src = new SourceRef((SourceKind)buf[i + 2], buf[i + 3]);
                var pk = s.Pokemon[p];
                if (delta > 0 && (StatusRules.BlocksHealing(pk) || Array.IndexOf(down, p) >= 0)) delta = 0;
                if (delta > 0) delta += TrainerRules.HealBonus(s, ctx, pk); // fairy_tale_girl B: 회복마다 +1(rulings A32)
                if (delta < 0 && UbTrial.Guards(s, ctx, b, p))
                {
                    // 호수 시련: 넘기기 전(넘긴 그 턴까지)의 수호신은 모든 피해에 면역(p.26). 피로 피해를 「입었을」 것이면 감정의 시련을 넘긴다(rulings A55 ②)
                    fatigued |= src.Kind == SourceKind.Rule && src.Id == (int)RuleSource.Fatigue;
                    delta = 0;
                }
                if (delta < 0)
                {
                    // 프리즘 아머(S17_4, rulings A53): UB·네크로즈마가 이번 배틀 턴에 받는 총 피해에서 ⌊개수/2⌋까지 — 2배 뒤의 값에서 뺀다
                    int cut = UbBattle.Reduce(s, ctx, b, p, -delta, src);
                    if (cut > 0)
                    {
                        delta += cut;
                        prism += cut;
                        prismOn = p;
                    }
                }
                if (delta < 0)
                {
                    b.TakenNow.Add(p); // 이번 배틀 턴에 받은 피해(destiny_bond 「damage taken last turn」 — 버퍼에 든 양, rulings G21)
                    b.TakenNow.Add(-delta);
                }
                int max = PokemonRules.MaxHp(ctx, pk);
                int before = PokemonRules.Hp(ctx, pk);
                pk.Damage = Math.Max(0, Math.Min(max, pk.Damage - delta));
                int k = SideOf(b, p);
                // 출전 중이 아닌 포켓몬이 효과 피해(earthquake 등)로 쓰러졌다 — 버퍼를 다 적용한 뒤 기절을 알린다(rulings G20)
                if (k < 0 && before > 0 && PokemonRules.Hp(ctx, pk) == 0 && SideOfAny(s, b, p) >= 0) (benchKo ?? (benchKo = new List<int>())).Add(p);
                if (delta < 0 && k >= 0)
                {
                    bool byOther = (src.Kind == SourceKind.Move && k != b.ActiveSide) || (src.Kind == SourceKind.Rule && src.Id == (int)RuleSource.GalacticRetribution)
                        || (src.Kind == SourceKind.TrainerCard && TrainerRules.CardOf(s, b.Sides[1 - k].Owner) == src.Id); // 상대 카드의 피해(team_rocket R · kindler R)
                    b.Sides[k].LastHit = src;
                    b.Sides[k].LastHitBy = byOther ? b.Sides[1 - k].Active : -1;
                }
                if (!fx.On) continue;
                if (src.Kind == SourceKind.Status)
                    fx.Add(new StatusTicked { Source = src, PokemonId = p, Status = (StatusEffect)src.Id, Effect = PokemonRules.Hp(ctx, pk) - before, HpBefore = before, HpAfter = PokemonRules.Hp(ctx, pk) });
                else fx.Add(new HpChanged { PokemonId = p, Source = src, Before = before, After = PokemonRules.Hp(ctx, pk), Max = max });
            }
            fx.EndSameIndex();
            buf.Clear();
            if (fatigued) UbTrial.Fatigued(s, ctx, fx, b);
            if (prism > 0 && fx.On) fx.Add(new PrismReduced { Seat = SeatId.UltraBeast, Side = SideOfAny(s, b, prismOn), PokemonId = prismOn, Amount = prism });
            if (benchKo != null)
                foreach (int p in benchKo) BenchFaint(s, ctx, fx, b, SideOfAny(s, b, p), p);
        }

        /// <summary>
        /// 벤치(출전 중이 아닌) 포켓몬의 기절(earthquake·prismatic_laser의 피해 — rulings G20): 기절 알림(Side = 그 쪽), 상태이상 풀림, 이번 배틀 기절(기권 가능), 갤럭시단이면 응징 +2(A13 「whenever their Pokémon faint」).
        /// 출전 중이 아니라 교체·순서는 그대로다.
        /// </summary>
        static void BenchFaint(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int p)
        {
            var side = b.Sides[k];
            side.FaintedThisBattle = true;
            side.Fallen++;
            if (fx.On) fx.Add(new Fainted { Seat = side.Owner, PokemonId = p, Side = k, ById = b.Sides[1 - k].Active, PartyIndex = BattleRules.IndexInSide(s, side, p) });
            StatusRules.Clear(s.Pokemon[p]);
            TrainerRules.OwnFainted(s, ctx, fx, b, k); // team_rocket R: 내 포켓몬이 기절하면 상대에게 2(rulings A35)
            if (side.Owner != SeatId.Galactic) return;
            int before = side.Retribution;
            side.Retribution += BattleRules.RetributionPerFaint;
            if (fx.On) fx.Add(new RetributionStored { Source = SourceRef.Rule(RuleSource.GalacticRetribution), Side = k, Before = before, After = side.Retribution, Cause = RetributionCause.Fainted });
        }

        /// <summary>그 포켓몬이 속한 배틀 쪽(출전 중이 아니어도 — 좌석 = 파티, 가상 = 명단). 없으면 −1.</summary>
        internal static int SideOfAny(GameState s, BattleState b, int pokemon)
        {
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var side = b.Sides[k];
                if (side.Active == pokemon || side.Roster.Contains(pokemon)) return k;
                if (side.Owner >= 0 && Array.IndexOf(s.Seats[side.Owner].Party, pokemon) >= 0) return k;
            }
            return -1;
        }

        static int SideOf(BattleState b, int pokemon)
        {
            for (int k = 0; k < BattleState.SideCount; k++)
                if (b.Sides[k].Active == pokemon) return k;
            return -1;
        }

        /// <summary>기절 확인(rulings G13): 공격받은 쪽 → 공격한 쪽. 연쇄는 같은 Seq + ParentSource = 깎은 출처(01 §3-6 ⑦).</summary>
        static void FaintStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int first = b.ActiveSide >= 0 ? 1 - b.ActiveSide : 0;
            ReportFaint(s, ctx, fx, b, first);
            ReportFaint(s, ctx, fx, b, 1 - first);
        }

        static void ReportFaint(GameState s, Ctx ctx, Emitter fx, BattleState b, int k)
        {
            var side = b.Sides[k];
            int p = side.Active;
            if (p < 0 || !PokemonRules.IsFainted(ctx, s.Pokemon[p])) return;
            side.FaintedThisBattle = true; // 기권은 이 뒤부터(S7_8, rulings G13)
            side.Fallen++;
            var parent = fx.Parent;
            fx.Parent = side.LastHit;
            if (fx.On) fx.Add(new Fainted { Seat = side.Owner, PokemonId = p, Side = k, ById = side.LastHitBy, PartyIndex = BattleRules.IndexInSide(s, side, p) });
            StatusRules.Clear(s.Pokemon[p]); // 기절하면 상태이상이 풀린다(p.9, G14). Fainted가 아이콘을 지운다
            TrainerRules.Defeated(s, ctx, fx, b, k, p); // 트레이너 카드 해금 「defeat」(rulings A20-3)
            if (side.Owner == SeatId.Galactic)
            {
                // A13: 갤럭시단 포켓몬이 기절할 때마다 2 저장(G5: 이번 공격에는 못 옮기고 다음 공격부터)
                int before = side.Retribution;
                side.Retribution += BattleRules.RetributionPerFaint;
                if (fx.On) fx.Add(new RetributionStored { Source = SourceRef.Rule(RuleSource.GalacticRetribution), Side = k, Before = before, After = side.Retribution, Cause = RetributionCause.Fainted });
            }
            TrainerRules.OwnFainted(s, ctx, fx, b, k); // team_rocket R: 내 포켓몬이 기절하면 상대에게 2(rulings A35)
            LegendRules.Fainted(s, ctx, fx, b, k); // 비스트 부스트: 상대 쪽 전설의 피로 −3 · 다음 공격 +1(rulings A26)
            LeaveField(s, fx, b, k, SwitchReason.Fainted);
            LegendRules.Ascend(s, ctx, fx, b, p); // 천상의 오라: 신의 첫 폼이 쓰러지면 둘째 폼 완전 회복 — 다음 포켓몬 자리에서 다시 나온다(rulings A28 ①)
            fx.Parent = parent;
            b.Reorder = true;
        }

        /// <summary>
        /// 출전 중 포켓몬이 나간다: 피로 0(S7_7, R12 액티브 존이 비면). SwitchedOut + FatigueChanged(After = 0).
        /// 마비 대기도 지운다 — 바로 다음 자기 턴에 공격하지 못했다(교체·기절·배틀 끝, rulings G14 · 원작 커뮤니티 확인).
        /// </summary>
        static void LeaveField(GameState s, Emitter fx, BattleState b, int k, SwitchReason reason)
        {
            var side = b.Sides[k];
            int p = side.Active;
            var pk = s.Pokemon[p];
            int before = pk.Fatigue;
            FatigueRules.SwitchOut(ref pk.Fatigue);
            pk.LastMove = pk.LastMove2 = -1;
            pk.ParalysisPending = false;
            ModRules.LeftField(pk);                 // 교체까지·한 번짜리 지속 보정은 나가면 사라진다(G16)
            EffectRules.ClearLimitsOf(s, p);        // 「나갈 때까지 한 번」 한도도 다시 열린다
            side.BeastBoost = 0;                    // 비스트 부스트는 그 전설이 다음 공격에 나설 때만(원작 커뮤니티 확인, rulings A26)
            side.Active = -1;
            if (!fx.On) return;
            fx.Add(new SwitchedOut { Seat = side.Owner, Side = k, PokemonId = p, Reason = reason });
            fx.Add(new FatigueChanged { PokemonId = p, Before = before, After = 0, Cause = FatigueCause.Switch });
        }

        /// <summary>
        /// 출전. 나오는 포켓몬은 마비 대기가 없다 — 벤치·배틀 밖에서 걸린 마비는 일반 타격을 지우지 않고 우선도 −3만 남는다(rulings G14 · 원작 커뮤니티 확인
        /// 「its only their *immediate* next attack」). 옛 저장(마비 대기가 배틀을 넘어 남던 판, 규칙 판 2)의 스냅샷도 여기서 새 규칙으로 맞춰진다.
        /// </summary>
        static void SendIn(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int pokemon)
        {
            var side = b.Sides[k];
            side.Active = pokemon;
            side.LastHit = SourceRef.None;
            side.LastHitBy = -1;
            var pk = s.Pokemon[pokemon];
            pk.ParalysisPending = false;
            ModRules.SentIn(s, b, k, pokemon); // eternal_glade로 늘어난 「배틀 끝까지」 효과는 파티 전체에(rulings G19)
            // renegade_fury·overcut 「any remaining damage from this attack is applied to the next Pokémon switched in」: 버퍼에 넣는다 — 다음 교체 단계가 적용하고 기절을 본다(rulings G20)
            if (side.Carry > 0)
            {
                Buffer(b, pokemon, -side.Carry, side.CarrySource);
                side.Carry = 0;
                side.CarrySource = SourceRef.None;
            }
            if (fx.On)
            fx.Add(new SentIn
            {
                Seat = side.Owner, Side = k, PokemonId = pokemon, Species = pk.Def, Hp = PokemonRules.Hp(ctx, pk), MaxHp = PokemonRules.MaxHp(ctx, pk), Status = pk.Status,
                TrainerKey = side.Owner >= 0 ? s.Seats[side.Owner].TrainerCard : -1,
            });
            // worker R: 배틀 도중에 나온 강철 타입 — 상대 피로 +3 · 교체·도망 금지(처음 내보내는 것은 아니다, rulings A35)
            if (b.Step != BattleStep.SendIn) TrainerRules.SwitchedIn(s, ctx, fx, b, k, pokemon);
            side.Entered = BreachRules.Count(ctx, pk, BreachKind.StatusOverload) > 0; // 균열 카드 status_overload: 나올 때 상태 주사위(양쪽이 다 나오면)
            BreachRules.SentIn(s, ctx, fx, b);
        }

        /// <summary>내보낼 수 있는 포켓몬(기절하지 않았고 출전 중이 아님): 좌석 = 파티 순서, 가상 = 명단 순서. ChoiceRules SendIn Legal과 같다.</summary>
        static void Bench(GameState s, Ctx ctx, BattleSide side, List<int> into)
        {
            into.Clear();
            if (side.Owner >= 0)
            {
                var st = s.Seats[side.Owner];
                for (int i = 0; i < st.Party.Length; i++)
                {
                    int p = st.Party[i];
                    if (p >= 0 && p != side.Active && PokemonRules.Fights(st, i) && !PokemonRules.IsFainted(ctx, s.Pokemon[p]) && !BattleRules.Taken(s, side, p)) into.Add(p); // 빼앗은 포켓몬은 못 낸다(결정 84 T7 ③)
                }
            }
            else
                foreach (int p in side.Roster)
                {
                    if (p == side.Active || !BattleRules.OnSide(s, ctx, side, p)) continue;
                    into.Add(p);
                    if (side.Owner == SeatId.Wild) return; // 야생 파티(무리 배틀)는 명단 순서대로 나온다(p.12 「enter the battle in this same order」, rulings A28 ②)
                }
        }

        // ── 교체·끝 ──

        /// <summary>
        /// 끝 판정 → 교체(S7_5, rulings G13): 한쪽만 기절했으면 기절하지 않은 쪽에 교체 창 한 번(갤럭시단이 쓰러뜨렸으면 A16 교체 금지), 그다음 다음 포켓몬
        /// (둘 다 기절이면 토큰 보유자 먼저). 다 차면 교체가 있었으면 Order, 없으면 다음 쪽의 턴.
        /// AfterFaint 옵션의 옮긴 응징은 여기서 따로 적용하고 기절을 다시 본다(G5 다른 해석).
        /// </summary>
        static void ReplaceStep(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            // 기절이 다시 피해를 쌓을 수 있다(team_rocket R의 2 · expert R의 회복, rulings A35) — 빌 때까지
            while (b.DamageBuffer.Count > 0)
            {
                ApplyBuffer(s, ctx, fx, b);
                FaintStep(s, ctx, fx, b);
            }
            if (TryEnd(s, ctx, fx, b)) return;
            // 기술 효과의 교체(u_turn 등 — 턴 끝 효과 뒤, rulings G20): 열면 답이 이 단계를 잇는다
            // scuba_diver R(물 기술 뒤 교체 — rulings A35)도 같은 자리: 둘 다면 창 하나(그 카드가 출처 — 나가면 상태이상이 풀린다)
            if (b.SwitchAfter >= 0 || b.ScubaBy >= 0)
            {
                int user = b.SwitchAfter >= 0 ? b.SwitchAfter : b.ScubaBy;
                bool scuba = user == b.ScubaBy;
                b.SwitchAfter = -1;
                if (b.Sides[0].Active >= 0 && b.Sides[1].Active >= 0) b.ScubaBy = -1; // 상대가 쓰러졌으면 아래 S7_5 교체 창이 같은 일을 한다
                if (MoveSwitch(s, ctx, fx, b, user, scuba)) return;
            }

            var s0 = b.Sides[0];
            var s1 = b.Sides[1];
            if (s0.Active >= 0 && s1.Active >= 0)
            {
                if (ExtraAttack(s, ctx, fx, b, false)) return; // 둘째 기술(SecondMoveTurnEnd = EachAttack — 첫 공격의 턴 끝·교체 뒤, rulings G21)
                if (b.Reorder) b.Step = BattleStep.Order;
                else
                {
                    b.ActiveSide = 1 - b.ActiveSide;
                    b.Step = BattleStep.SelectAction;
                }
                return;
            }

            var bench = new List<int>();
            bool both = s0.Active < 0 && s1.Active < 0;
            if (both) b.SwitchOffered = true; // 함께 기절하면 교체 창 없이 둘 다 다음 포켓몬(rulings G13)
            if (!both && !b.SwitchOffered)
            {
                b.SwitchOffered = true;
                int down = s0.Active < 0 ? 0 : 1;
                var other = b.Sides[1 - down];
                int ko = b.Sides[down].LastHitBy;
                if (other.Owner == SeatId.Galactic && s.Options.GalacticNoSwitchAfterKo && ko >= 0 && s.Pokemon[ko].Owner == SeatId.Galactic)
                {
                    if (fx.On) fx.Add(new SwitchLocked { Seat = SeatId.Galactic, Side = 1 - down, Reason = SwitchLockReason.GalacticNoSwitchAfterKo });
                }
                else
                {
                    Bench(s, ctx, other, bench);
                    if (bench.Count > 0)
                    {
                        // 빙결은 교체할 수 없다(p.9, G14): 바꿀 포켓몬이 있을 때만 잠김을 보인다
                        if (StatusRules.BlocksEscape(s.Pokemon[other.Active]))
                        {
                            if (fx.On) fx.Add(new SwitchLocked { Seat = other.Owner, Side = 1 - down, Reason = SwitchLockReason.Frozen });
                        }
                        else if (ModRules.NoEscape(s.Pokemon[other.Active]))
                        {
                            // 「The target may no longer switch out」(magma_storm 등, rulings G19)
                            if (fx.On) fx.Add(new SwitchLocked { Seat = other.Owner, Side = 1 - down, Reason = SwitchLockReason.NoEscape });
                        }
                        else if (HeldItems.SwitchLocked(s, other.Active))
                        {
                            // 폭발수정에 맞은 공격자는 이번 턴 교체하지 못한다(S10)
                            if (fx.On) fx.Add(new SwitchLocked { Seat = other.Owner, Side = 1 - down, Reason = SwitchLockReason.BlastCrystal });
                        }
                        else
                        {
                            var ask = new PendingChoice { Kind = ChoiceKind.SwitchWindow, Seat = other.Responder };
                            if (other.Active == b.ScubaBy) ask.Source = TrainerRules.ScubaSource(s, b, other.Active); // 물 기술로 쓰러뜨린 scuba_diver R: 이 교체로도 상태이상이 풀린다
                            b.ScubaBy = -1;
                            ChoiceRules.Open(s, ctx, fx, ask);
                            return;
                        }
                    }
                }
            }

            int holder = TokenHolder(b);
            int firstK = both ? (holder >= 0 ? holder : 0) : (s0.Active < 0 ? 0 : 1);
            for (int i = 0; i < BattleState.SideCount; i++)
            {
                int k = i == 0 ? firstK : 1 - firstK;
                var side = b.Sides[k];
                if (side.Active >= 0) continue;
                Bench(s, ctx, side, bench);
                if (bench.Count == 1)
                {
                    SendIn(s, ctx, fx, b, k, bench[0]);
                    continue;
                }
                ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.NextPokemon, Seat = side.Responder });
                return;
            }
        }

        /// <summary>
        /// 기술 효과로 교체(「The user may switch out (if possible)」, rulings G20): 그 포켓몬이 아직 출전 중이고 상대도 출전 중이면(상대가 쓰러졌으면 S7_5 교체 창이 같은 일을 한다) 교체 창을 연다
        /// (SwitchWindow, Source = 그 기술 — 답하면 순서 그대로). 막는 것: 빙결 · 교체·도망 금지(magma_storm 등) · 폭발수정에 맞은 턴 · fairy_lock(「can no longer switch out using move effects」) —
        /// 바꿀 포켓몬이 있을 때만 SwitchLocked를 보인다. teleport·shelter의 「may switch out」도 같다(원작 커뮤니티 확인).
        /// </summary>
        static bool MoveSwitch(GameState s, Ctx ctx, Emitter fx, BattleState b, int pokemon, bool scuba = false)
        {
            int k = SideOf(b, pokemon);
            if (k < 0 || b.Sides[1 - k].Active < 0) return false;
            var side = b.Sides[k];
            var bench = new List<int>();
            Bench(s, ctx, side, bench);
            if (bench.Count == 0) return false;
            var pk = s.Pokemon[pokemon];
            var reason = StatusRules.BlocksEscape(pk) ? SwitchLockReason.Frozen
                : ModRules.NoEscape(pk) ? SwitchLockReason.NoEscape
                : HeldItems.SwitchLocked(s, pokemon) ? SwitchLockReason.BlastCrystal
                : !scuba && ModRules.MoveSwitchBlocked(s) ? SwitchLockReason.MoveSwitchBlocked
                : (SwitchLockReason)(-1);
            if ((int)reason >= 0)
            {
                if (fx.On) fx.Add(new SwitchLocked { Seat = side.Owner, Side = k, Reason = reason });
                return false;
            }
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.SwitchWindow, Seat = side.Responder, Source = scuba ? TrainerRules.ScubaSource(s, b, pokemon) : new SourceRef(SourceKind.Move, b.Move) });
            return true;
        }

        /// <summary>
        /// 끝 판정(S7_8, A1, rulings G13). 야생: 야생이 바닥나면 승리(내 포켓몬이 함께 기절해도). 약한~강한 야생은 내 포켓몬 1마리를, 전설·우두머리·무리는 모두를 쓰러뜨리면 도망 —
        /// 쓰러뜨린 것이 내 마지막 포켓몬이어도 도망이다(규토리 2/1/0 뒤 귀환). 빙결된 야생은 도망치지 못해 내가 바닥나면 야생 승리(보상 없음).
        /// 갤럭시단: 내가 바닥나면 짐(조무래기도 바닥났으면 무승부 — 해방 아님), 조무래기만 바닥나면 승리. 트레이너전: 남은 쪽 승리, 둘 다 없으면 무승부.
        /// </summary>
        static bool TryEnd(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            bool u0 = BattleRules.SideHasUsable(s, ctx, b.Sides[0]);
            bool u1 = BattleRules.SideHasUsable(s, ctx, b.Sides[1]);
            switch (b.Kind)
            {
                case BattleKind.Galactic:
                    if (!u0) Conclude(s, ctx, fx, u1 ? BattleResult.Win : BattleResult.Draw, u1 ? 1 : -1);
                    else if (!u1) Conclude(s, ctx, fx, BattleResult.Win, 0);
                    else return false;
                    return true;
                case BattleKind.Trainer:
                case BattleKind.Traitor:
                    if (!u0 && !u1) Conclude(s, ctx, fx, BattleResult.Draw, -1);
                    else if (!u1) Conclude(s, ctx, fx, BattleResult.Win, 0);
                    else if (!u0) Conclude(s, ctx, fx, BattleResult.Win, 1);
                    else return false;
                    return true;
                default:
                {
                    if (!u1)
                    {
                        Conclude(s, ctx, fx, BattleResult.Win, 0);
                        return true;
                    }
                    var wild = s.Pokemon[WildOf(b)];
                    // 빙결된 야생은 도망치지 못한다(p.9, G14). 내가 바닥났을 때 도망치는 것은 p.8이 말한 야생 종류만(신·UB·네크로즈마·시련은 M5·M6)
                    bool canFlee = !StatusRules.BlocksEscape(wild) && !ModRules.NoEscape(wild); // 「may no longer … flee the battle」도 빙결처럼 막는다(G19)
                    bool firstKo = b.Sides[0].Active < 0 && BattleRules.WildFleesAfterFirstKo(b.Kind, MapActions.GradeOf(ctx, wild.Def), wild.Alpha)
                        && TrainerRules.WildFlees(s, ctx, b, b.Sides[0].Fallen); // roller_skater B: 둘을 쓰러뜨린 뒤에만(rulings A36)
                    bool allDown = !u0 && FleesWhenAllDown(b.Kind);
                    if (canFlee && (firstKo || allDown))
                    {
                        Flee(s, ctx, fx, b);
                        return true;
                    }
                    if (!u0)
                    {
                        Conclude(s, ctx, fx, BattleResult.Win, 1);
                        return true;
                    }
                    return false;
                }
            }
        }

        /// <summary>내 포켓몬을 모두 쓰러뜨리면 도망치는 배틀(p.8: 약한~강한은 그 전에 이미 도망, 전설·우두머리·무리·신은 이때).</summary>
        static bool FleesWhenAllDown(BattleKind k) => k == BattleKind.Wild || k == BattleKind.Swarm || k == BattleKind.Legendary || k == BattleKind.Alpha || k == BattleKind.God || k == BattleKind.Trial;

        /// <summary>도망을 판정할 야생: 출전 중인 것, 없으면 명단 첫 것(무리 배틀은 출전 중인 것이 도망을 정한다 — 셋이 한 파티라 함께 도망친다).</summary>
        static int WildOf(BattleState b) => b.Sides[1].Active >= 0 ? b.Sides[1].Active : b.Sides[1].Roster[0];
        /// <summary>그 배틀의 나무·산호가 지키는 규토리 3개(A8). 조우 토큰이 없거나 왜곡된 나무면 빈 목록.</summary>
        static List<int> Guarded(GameState s, Ctx ctx, BattleState b)
        {
            var r = new List<int>();
            if (b.Token < 0) return r;
            foreach (var c in ctx.Fixtures.Fixtures[s.Tokens[b.Token].Def].Apricorns) r.Add((int)c);
            r.Sort();
            return r;
        }

        /// <summary>야생 도망(S7_8, A8): 남긴 규토리 2/1/0. 섞인 산호에서 받을 색이 갈리면 묻는다(Reward/CoralColor).</summary>
        static void Flee(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            var wild = s.Pokemon[b.Sides[1].Roster[0]];
            int n = BattleRules.FleeApricorns(MapActions.GradeOf(ctx, wild.Def));
            var guarded = Guarded(s, ctx, b);
            b.Step = BattleStep.End;
            if (n > 0 && guarded.Count > 0)
            {
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.CoralColor, Seat = b.Sides[0].Owner, Count = n, Window = Window.BattleEnd };
                c.Options.AddRange(guarded);
                var legal = new List<ChoiceAnswer>();
                ChoiceRules.Legal(s, ctx, c, legal);
                if (legal.Count > 1)
                {
                    ChoiceRules.Open(s, ctx, fx, c);
                    return;
                }
                FinishFlee(s, ctx, fx, b, legal[0].Values);
                return;
            }
            FinishFlee(s, ctx, fx, b, new List<int>());
        }

        static void FinishFlee(GameState s, Ctx ctx, Emitter fx, BattleState b, List<int> colors)
        {
            int seat = b.Sides[0].Owner;
            var changes = new List<ResChange>();
            GainApricorns(s, seat, colors, changes);
            if (fx.On) fx.Add(new Fled { Seat = SeatId.Wild, Side = 1, Changes = changes.ToArray() });
            Conclude(s, ctx, fx, BattleResult.Fled, -1);
        }

        static void GainApricorns(GameState s, int seat, List<int> colors, List<ResChange> changes)
        {
            var ap = s.Seats[seat].Apricorns;
            for (int color = 0; color < SeatState.ApricornColors; color++)
            {
                int n = 0;
                foreach (int c in colors)
                    if (c == color) n++;
                if (n == 0) continue;
                int before = ap[color];
                ap[color] += n;
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = ap[color] });
            }
        }

        static void GainJp(GameState s, int seat, int jp, List<ResChange> changes)
        {
            var st = s.Seats[seat];
            int before = st.Jp;
            st.Jp += jp;
            changes.Add(new ResChange { Seat = seat, Res = ResKind.Jp, Key = -1, Before = before, After = st.Jp });
        }

        /// <summary>
        /// 포획(A7, 01 §6-5): 볼 등급만큼 타입없음 주사위, 상태이상이면 심볼 1개 → 일반. 흔들림 = 성공 3, 실패 max(0, 3 − (포획률 − 합)). 포획 굴림도 피로(R12, A10 옵션).
        /// 던진 볼은 CaptureAttempt.Changes(Res = Ball)로 싣는다(01 §3-6 ③). 잡힌 포켓몬은 액티브 존을 떠나 피로 0·직전 기술 없음(R12, G14, rulings G13).
        /// 볼을 굴린 뒤 굴린 뒤 창(Window.AfterRoll — 디아루가 재굴림, 사용자 결정 76 ③ F2 나)을 연다: 굴린 면은 Faces에, 볼은 CaptureBall에 두고 Step = Capture로 기다린다.
        /// 창이 닫히면(또는 물을 좌석이 없으면 바로) <see cref="CaptureRolled"/>가 다시 굴린 면으로 매혹적인 스튜 묻기 → 판정을 잇는다 [가정 — 디아루가가 스튜보다 먼저].
        /// </summary>
        static void Capture(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, Ball ball)
        {
            var balls = s.Seats[b.Sides[k].Owner].Balls;
            balls[(int)ball]--;
            b.Faces.Clear();
            b.Faces.AddRange(AttackDice.Roll(s.Rng, CaptureRules.Dice(ball)));
            b.CaptureBall = (int)ball;
            b.Step = BattleStep.Capture;
            bool stolen = s.Pokemon[b.Sides[1 - k].Active].Owner >= 0; // team_rocket B로 남의 포켓몬을 잡는 굴림에는 디아루가 재굴림이 없다(사용자 결정 84 T7 ①)
            if (!stolen && Reactions.Open(s, ctx, fx, Window.AfterRoll, true)) return;
            CaptureRolled(s, ctx, fx, b);
        }

        /// <summary>굴린 뒤 창 뒤의 포획(Step = Capture): 볼 주사위(Faces — 디아루가가 다시 굴렸을 수 있다)를 거두고 매혹적인 스튜 묻기 → 판정. 판정 자리는 다시 SelectAction에서 CaptureResolve가 정한다.</summary>
        static void CaptureRolled(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            int k = b.ActiveSide;
            var side = b.Sides[k];
            int seat = side.Owner;
            int target = b.Sides[1 - k].Active;
            var tp = s.Pokemon[target];
            var ball = (Ball)b.CaptureBall;
            int ballsBefore = s.Seats[seat].Balls[(int)ball] + 1;
            var raw = b.Faces.ToArray();
            TrainerRules.CaptureCharge(s, ctx, seat, raw); // guitarist B: 포획 굴림의 상태 심볼도 앰프에(rulings A33 · A41)
            b.Faces.Clear();
            b.CaptureBall = -1;
            b.Step = BattleStep.SelectAction;
            bool status = tp.Status != StatusEffect.None;
            var faces = CaptureRules.ApplyAccuracy(raw, status);
            faces = TrainerRules.SweetScent(s, ctx, fx, b, seat, faces); // aroma_lady B 나무 · fisherman B 산호: 심볼 하나 더 → 일반 타격(rulings A38)
            // sweet_scent 「On the next attack or capture roll against the target, status symbols deal +1 damage」: 남은 심볼 하나마다 포획 합 +1, 이 굴림에 쓰고 사라진다(G19).
            // 매혹적인 스튜를 쓴 굴림(심볼 하나 → 일반 타격)의 합도 미리 센다 — 쓸지 묻는 동안 보정은 이미 쓰였다
            // beauty B 「During all your rolls」: 포획 굴림에도 크리티컬 +2 · 심볼 −1(rulings A32)
            int thief = TrainerRules.ThiefMod(s, target); // team_rocket B: 남의 포켓몬이면 −2(rulings A35)
            int sumNo = BattleRules.Sum(faces) + ModRules.CaptureBonus(s, target, faces) + TrainerRules.CaptureBonus(s, ctx, seat, faces) + thief;
            var allure = CraftRules.Allured(faces);
            int sumYes = BattleRules.Sum(allure) + ModRules.CaptureBonus(s, target, allure) + TrainerRules.CaptureBonus(s, ctx, seat, allure) + thief;
            ModRules.CaptureRolled(s, target);
            // 매혹적인 스튜(S10): 굴림에 심볼이 있으면 쓸지 묻는다(Reaction/AlluringStew — 답이 CaptureResolve를 잇는다)
            if (CraftRules.AlluringOpen(s, seat, faces))
            {
                var ask = new PendingChoice
                {
                    Kind = ChoiceKind.Reaction, Sub = ChoiceSub.AlluringStew, Seat = side.Responder, Subject = 1,
                    Source = new SourceRef(SourceKind.Item, ctx.Items.Ids.IndexOf(CraftRules.AlluringStew)),
                };
                ask.Options.AddRange(new[] { (int)ball, ballsBefore, sumNo, sumYes, raw.Length });
                foreach (var f in raw) ask.Options.Add((int)f);
                ChoiceRules.Open(s, ctx, fx, ask);
                return;
            }
            CaptureResolve(s, ctx, fx, b, k, ball, ballsBefore, raw, sumNo, false);
        }

        /// <summary>매혹적인 스튜를 묻던 포획 굴림을 잇는다(CraftRules.ApplyAlluring — 굴림 값은 물을 때 Options에 실었다: 볼 · 던지기 전 볼 수 · 안 쓴 합 · 쓴 합 · 면 수 · 굴린 면).</summary>
        internal static void ResumeCapture(GameState s, Ctx ctx, Emitter fx, PendingChoice c, bool allured)
        {
            var b = s.Battle;
            int k = ChoiceRules.BattleSideOf(s, c.Seat);
            var o = c.Options;
            var raw = new AttackDieFace[o[4]];
            for (int i = 0; i < raw.Length; i++) raw[i] = (AttackDieFace)o[5 + i];
            CaptureResolve(s, ctx, fx, b, k, (Ball)o[0], o[1], raw, allured ? o[3] : o[2], allured);
            Run(s, ctx, fx);
        }

        /// <summary>gentleman R 「When the Cherish Ball is used to capture a wild Pokémon, it automatically captures that Pokémon」: 굴리지 않고 잡는다(피로도 없다 — 굴림이 없다 [가정 §5 #147 ⑤]). 볼은 그 포켓몬에(SeatState.CherishIn).</summary>
        static void Cherish(GameState s, Ctx ctx, Emitter fx, BattleState b, int k)
        {
            int seat = b.Sides[k].Owner;
            int target = b.Sides[1 - k].Active;
            s.Seats[seat].CherishIn = target;
            TrainerRules.Used(s, fx, seat, AbilityColor.Red, target);
            CaptureResolve(s, ctx, fx, b, k, Ball.Regular, s.Seats[seat].Balls[(int)Ball.Regular], new AttackDieFace[0], 0, false, true);
        }

        /// <summary>대타출동(TrainerRules.Substitute — 피해 직전): k 쪽 출전 포켓몬을 들이고 그 포켓몬을 내보낸다(보통 교체와 같다 — 피로 0 · 지속 보정 정리).</summary>
        internal static void SwapIn(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int pokemon)
        {
            LeaveField(s, fx, b, k, SwitchReason.Switch);
            SendIn(s, ctx, fx, b, k, pokemon);
        }

        static void CaptureResolve(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, Ball ball, int ballsBefore, AttackDieFace[] raw, int sum, bool allured, bool cherish = false)
        {
            var side = b.Sides[k];
            int seat = side.Owner;
            int att = side.Active;
            int target = b.Sides[1 - k].Active;
            var tp = s.Pokemon[target];
            var ap = s.Pokemon[att];
            var balls = s.Seats[seat].Balls;
            bool converted = tp.Status != StatusEffect.None && Array.IndexOf(raw, AttackDieFace.StatusSymbol) >= 0;
            int rate = CaptureRules.Rate(BattleRules.CatchRate(MapActions.GradeOf(ctx, tp.Def), tp.Alpha), LegendRules.AuraOn(s, ctx, b)); // 천상의 오라 7(두 폼 모두, rulings A28 ①)
            bool ok = cherish || sum >= rate;
            int before = ap.Fatigue;
            int hurt = cherish ? 0 : FatigueRules.Roll(ref ap.Fatigue, true, s.Options);
            if (hurt > 0) Buffer(b, att, -hurt, SourceRef.Rule(RuleSource.Fatigue));
            // 볼을 던진 턴은 공격이 아니다: 마비 대기는 여기서 사라지고 우선도 −3만 남는다(rulings G14 · 원작 커뮤니티 확인)
            ap.ParalysisPending = false;
            if (fx.On)
            {
                fx.Add(new CaptureAttempt
                {
                    Side = k, BallKey = (int)ball, TargetId = target, Faces = raw, Sum = sum, CatchRate = rate, RateMods = new Mod[0], StatusConverted = converted,
                    Shakes = ok ? 3 : Math.Max(0, 3 - (rate - sum)), Success = ok, Allured = allured,
                    Changes = cherish ? new ResChange[0] : new[] { new ResChange { Seat = seat, Res = ResKind.Ball, Key = (int)ball, Before = ballsBefore, After = balls[(int)ball] } },
                });
                if (s.Options.CaptureAddsFatigue && !cherish) fx.Add(new FatigueChanged { PokemonId = att, Before = before, After = ap.Fatigue, Cause = FatigueCause.Capture });
            }
            if (!ok)
            {
                b.Step = BattleStep.ApplyDamage; // 볼은 버리고 배틀 계속(p.5)
                StampRules.CaptureFailed(s, ctx, fx, b, k); // 2턴 이어 실패하면 스탬프 1
                return;
            }
            // 성공: 같은 턴의 피로 피해를 먼저 적용하고(rulings G13) 잡은 포켓몬을 파티로
            ApplyBuffer(s, ctx, fx, b);
            ReportFaint(s, ctx, fx, b, k);
            int fatigue = tp.Fatigue;
            FatigueRules.SwitchOut(ref tp.Fatigue);
            tp.LastMove = tp.LastMove2 = -1;
            tp.ParalysisPending = false;
            ModRules.LeftField(tp);
            EffectRules.ClearLimitsOf(s, target);
            if (fx.On && fatigue != 0) fx.Add(new FatigueChanged { PokemonId = target, Before = fatigue, After = 0, Cause = FatigueCause.Switch });
            bool stolen = tp.Owner >= 0;
            if (stolen) TrainerRules.Stolen(s, ctx, fx, b, k, target); // team_rocket B: 진 좌석의 파티에서 뺀다 — 붙은 것은 그대로 따라온다(rulings A35)
            tp.Owner = seat;
            tp.Ball = (int)ball;
            b.Sides[1 - k].Active = -1;
            if (stolen) TrainerRules.Unpocket(s, ctx, fx, target); // 둘째 지닌물건은 거래처럼 빼앗은 좌석의 가방으로(사용자 결정 84 T7 ④ — backpacker R 좌석이면 그대로)
            int slot = PokemonRules.AddToParty(s.Seats[seat], target);
            if (slot >= 0) Gained(s, ctx, fx, seat, target, slot);
            TrainerRules.Obtained(s, ctx, fx, seat, target, ObtainCause.Capture); // 트레이너 카드 해금 「obtain」(rulings A20-3 — 파티가 가득해도 잡은 순간)
            if (!stolen) TrainerRules.Landed(s, ctx, fx, b, seat, target); // fisherman B: 산호에서 잡으면 월척 주사위(rulings A38)
            StampRules.OnCaptured(s, ctx, fx, seat, target); // 전설·우두머리(신 포함)면 스탬프 4
            // 무리 배틀: 하나를 잡으면 쓰러뜨렸을 때처럼 다음 포켓몬이 나온다(교체 창 · 순서 다시 — rulings A28 ②). 파티가 가득이면 놓아주기는 배틀 끝에
            if (BattleRules.SideHasUsable(s, ctx, b.Sides[1 - k]))
            {
                b.Reorder = true;
                b.Step = BattleStep.Replace;
                return;
            }
            Conclude(s, ctx, fx, stolen ? BattleResult.Win : BattleResult.Captured, k); // 남의 마지막 쓸 포켓몬을 잡았으면 트레이너전 승리
        }

        static void Gained(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, int slot)
        {
            if (!fx.On) return;
            var p = s.Pokemon[pokemon];
            fx.Add(new PokemonGained { Seat = seat, PokemonId = pokemon, Species = p.Def, PartySlot = slot, BallKey = p.Ball, Hp = PokemonRules.Hp(ctx, p), Status = p.Status });
        }

        /// <summary>
        /// 배틀 끝(S7_8·S7_9, A1, rulings G13): 카드 정리·보상(JP·규토리)·BattleEnded → 도전 보호(트레이너전) → 1막 해방(갤럭시단) → 바닥난 좌석은 고향 마을로
        /// → 배틀을 닫고 배틀 뒤 선택(파티 가득 놓아주기 / 기술 가르치기, Window = BattleEnd)이 있으면 묻고, 없으면 턴을 끝낸다(p.3).
        /// winner = 이긴 쪽(−1 = 없음). 잡았지만 빈 칸이 없는 포켓몬은 명단에서 찾는다(놓아주기를 하나씩 묻는다 — Options = 남은 것).
        /// 신을 쓰러뜨리거나 잡았으면(UsageKind.GodVictory) 턴을 끝낼 때 한 턴 더(<see cref="Close"/>).
        /// </summary>
        static void Conclude(GameState s, Ctx ctx, Emitter fx, BattleResult result, int winner)
        {
            var b = s.Battle;
            if (TrainerRules.BlastOff(s, ctx, b, result, winner)) return; // team_rocket R: 쓸 포켓몬이 바닥났으면 갈 칸부터 묻는다(rulings A35 — 답한 뒤 BattleStep.Concluding이 다시 부른다)
            b.Step = BattleStep.End;
            ModRules.BattleEnded(s, b); // 배틀 끝까지인 지속 보정까지 모두 지운다(G16 — 명단을 치우기 전에)
            FormRules.BattleEnded(s, ctx, fx, b); // 기술로 바뀐 폼은 배틀 전 폼으로(p.6, rulings A26 — 카드를 덱·버림 더미로 보내기 전에)
            var changes = new List<ResChange>();
            int seat0 = b.Sides[0].Owner;
            var teach = new List<int>();
            var leader = GalacticTrainer.None;
            bool swarmWon = false;
            switch (b.Kind)
            {
                case BattleKind.Galactic:
                    leader = ctx.Cards.Pokemon[s.Pokemon[b.Sides[1].Roster[0]].Def].Trainer;
                    foreach (int p in b.Sides[1].Roster)
                    {
                        // 갤럭시단 카드는 제 덱으로(rulings G13 — 조무래기·간부·태홍). 장면 시험처럼 덱 밖에서 온 포켓몬이면 조무래기 덱
                        int deck = s.Pokemon[p].Deck;
                        bool galactic = deck == (int)DeckId.GalacticCommander || deck == (int)DeckId.GalacticBoss;
                        s.Decks[galactic ? deck : (int)DeckId.GalacticGrunt].Add(s.Pokemon[p].Def);
                        PokemonRules.Free(s, p);
                    }
                    break;
                case BattleKind.Trainer:
                case BattleKind.Traitor:
                    break;
                case BattleKind.Swarm:
                    // 무리 배틀(A18 · rulings A28 ②): 모두 쓰러뜨리거나 잡았으면 JP 7 하나(마리별 보상 대신, 규토리 없음). 쓰러뜨린 것의 기술은 져도 가르친다(원작 커뮤니티 확인) —
                    // 쓰러진 카드는 버림 더미, 쓰러지지 않은 카드(나오지 않은 것 포함)는 제 덱, 잡은 것은 파티(가득이면 아래 놓아주기)
                    swarmWon = result == BattleResult.Captured || (result == BattleResult.Win && winner == 0);
                    if (swarmWon) GainJp(s, seat0, BattleRules.SwarmRewardJp, changes);
                    int downs = 0;
                    foreach (int p in b.Sides[1].Roster)
                    {
                        var wp = s.Pokemon[p];
                        if (wp.Owner != SeatId.Wild) continue;
                        bool down = PokemonRules.IsFainted(ctx, wp);
                        if (down) downs++;
                        if (down) AddTeach(ctx, wp.Def, teach);
                        if (wp.Deck >= 0) (down ? s.Discards : s.Decks)[wp.Deck].Add(wp.Def);
                        PokemonRules.Free(s, p);
                    }
                    int belt = TrainerRules.Training(s, ctx, fx, seat0, downs); // black_belt B: 쓰러뜨린 야생마다 JP +2(rulings A38)
                    if (belt > 0) GainJp(s, seat0, belt, changes);
                    break;
                case BattleKind.Necrozma:
                    // 페이즈 격파는 「쓰러뜨림」이 아니다 — 보상·기술 가르치기·스탬프·균열 카드 버리기가 없다(rulings A54 ②). 카드는 덱 밖 인스턴스라 그냥 치운다
                    PokemonRules.Free(s, b.Sides[1].Roster[0]);
                    break;
                default:
                {
                    int wild = b.Sides[1].Roster[0];
                    var wp = s.Pokemon[wild];
                    var grade = MapActions.GradeOf(ctx, wp.Def);
                    bool won = result == BattleResult.Captured || (result == BattleResult.Win && winner == 0);
                    // UB를 쓰러뜨렸다: 토큰 제거 · 격파 수 · 프리즘 아머 1개 이동(p.24, rulings A53) — 보상은 전설처럼 아래에서(JP 6 · 기술 가르치기)
                    if (won && b.Kind == BattleKind.UltraBeast) UbBattle.Defeated(s, fx, seat0, wp.Def);
                    // 울트라 버스트: 호수 수호신을 쓰러뜨리거나 잡으면 모든 좌석 이동력 +2 · 실버디면 축복 +1(p.26, rulings A55)
                    if (won) UbTrial.Won(s, ctx, fx, seat0, wp.Def);
                    // 전설·신을 쓰러뜨리거나 잡았으면 그 토큰(사당 전설 · 시작의 사당의 신)이 빠진다 — 신이면 한 턴 더(p.21, rulings A28)
                    if (won && (b.Kind == BattleKind.Legendary || b.Kind == BattleKind.God) && LegendRules.RemoveToken(s, fx, seat0, wp.Def) && s.Hos != null)
                        MapActions.Mark(s, fx, seat0, UsageKind.GodVictory, -1, false);
                    if (result == BattleResult.Captured)
                    {
                        GainJp(s, seat0, BattleRules.RewardJp(grade, wp.Alpha, false), changes);
                        GainApricorns(s, seat0, TrainerRules.Coins(s, ctx, fx, seat0, Guarded(s, ctx, b)), changes); // gentleman B: 두 배(rulings A38)
                    }
                    else if (result == BattleResult.Win && winner == 0)
                    {
                        GainJp(s, seat0, BattleRules.RewardJp(grade, wp.Alpha, true), changes);
                        int beltJp = b.Kind == BattleKind.UltraBeast ? 0 : TrainerRules.Training(s, ctx, fx, seat0, 1); // black_belt B: JP +2 — UB는 「wild Pokémon」이 아니다(rulings A38 ④)
                        if (beltJp > 0) GainJp(s, seat0, beltJp, changes);
                        GainApricorns(s, seat0, TrainerRules.Coins(s, ctx, fx, seat0, Guarded(s, ctx, b)), changes); // gentleman B: 두 배(rulings A38)
                        AddTeach(ctx, wp.Def, teach);
                        if (wp.Deck >= 0) s.Discards[wp.Deck].Add(wp.Def); // 기절한 야생은 버림(S7_8)
                        PokemonRules.Free(s, wild);
                    }
                    else
                    {
                        if (wp.Deck >= 0) s.Decks[wp.Deck].Add(wp.Def); // 기절하지 않은 야생은 덱으로(S7_8)
                        PokemonRules.Free(s, wild);
                    }
                    break;
                }
            }
            // bug_catcher B: 잡은 벌레 타입마다 JP +2 · 차례 좌석이면 턴 끝에 추가 턴(rulings A38)
            for (int k = 0; k < BattleState.SideCount; k++)
            {
                int frenzy = TrainerRules.Frenzy(s, ctx, fx, b, k);
                if (frenzy > 0) GainJp(s, b.Sides[k].Owner, frenzy, changes);
            }
            TrainerRules.TentPitched(s, ctx, b); // backpacker B: 배틀 뒤 한 마리 완전 회복(Close 앞에서 묻는다, rulings A38)
            // 잡았지만 빈 칸이 없어 아직 파티 밖에 있는 포켓몬(명단 순서 — 무리 배틀은 여럿일 수 있다)
            var noRoom = new List<int>();
            int roomSeat = seat0;
            if (b.Kind != BattleKind.Galactic && b.Kind != BattleKind.Trainer && b.Kind != BattleKind.Traitor)
                foreach (int p in b.Sides[1].Roster)
                    if (s.Pokemon[p].Def >= 0 && s.Pokemon[p].Owner == seat0 && PokemonRules.PartyIndexOf(s.Seats[seat0], p) < 0) noRoom.Add(p);
            // team_rocket B(rulings A35): 트레이너전에서 잡았지만 빈 칸이 없는 남의 포켓몬 — 잡은 좌석(어느 쪽이든)이 놓아주기를 고른다
            if (b.Kind == BattleKind.Trainer)
                for (int k = 0; k < BattleState.SideCount; k++)
                    foreach (int p in b.Sides[1 - k].Roster)
                        if (s.Pokemon[p].Def >= 0 && s.Pokemon[p].Owner == b.Sides[k].Owner && PokemonRules.PartyIndexOf(s.Seats[b.Sides[k].Owner], p) < 0)
                        {
                            noRoom.Add(p);
                            roomSeat = b.Sides[k].Owner;
                        }

            for (int k = 0; k < BattleState.SideCount; k++)
            {
                var side = b.Sides[k];
                if (side.Owner < 0) continue;
                if (side.Active >= 0) LeaveField(s, fx, b, k, SwitchReason.BattleEnd);
                foreach (int p in s.Seats[side.Owner].Party)
                    if (p >= 0) s.Pokemon[p].LastMove = s.Pokemon[p].LastMove2 = -1;
            }
            if (fx.On) fx.Add(new BattleEnded { BattleId = b.Id, Winner = winner, Result = result, Changes = changes.ToArray() });
            fx.Watch?.BattleOver(s, b, result, winner); // 이벤트 없는 굴리기의 결과(01 §13 M8-3)
            StampRules.BattleEnded(s, ctx, fx, b, result, winner); // 트레이너·UB(혼자면 갤럭시단)를 이기면 스탬프 2
            if (swarmWon) StampRules.OnSwarmWon(s, ctx, fx, seat0); // 무리 배틀 셋을 모두 이겼다(JP 7과 같은 조건)면 스탬프 6
            TrainerRules.ScoopEnded(s, ctx, fx, b, result, winner); // interviewers B의 도전을 이겼으면 그 스탬프(rulings A38)

            if (b.Kind == BattleKind.Trainer)
            {
                // p.9: 배틀한 두 트레이너 모두 자기 다음 턴 시작 전까지 도전받지 않는다. 승리 보상(유물·운명 2장)은 아래 Reward/TrainerReward.
                s.Seats[b.Sides[0].Owner].Protected = true;
                s.Seats[b.Sides[1].Owner].Protected = true;
                if (fx.On) fx.Add(new ProtectionSet { Seat = SeatId.System, Seats = new[] { b.Sides[0].Owner, b.Sides[1].Owner } });
            }
            // 막 목표(이겼을 때만 — 무승부는 승리가 아니다): 조무래기 = 1막 해방, 간부 = 조각, 태홍 = 종막. after = 턴 끝 뒤에 할 일(Close)
            int after = 0;
            if (b.Kind == BattleKind.Galactic && result == BattleResult.Win && winner == 0)
            {
                if (HosFlow.ShardOf(leader) >= 0 || leader == GalacticTrainer.Cyrus) after = HosFlow.GalacticWon(s, ctx, fx, seat0, leader);
                else if (Liberate(s, ctx, fx, seat0)) after = 1;
            }

            // 종막 배신자 연전은 턴이 멈춘 뒤라 귀환이 없다(p.22 — 다음 판 전에 배신자만 회복한다)
            for (int k = 0; k < BattleState.SideCount && b.Kind != BattleKind.Traitor; k++)
            {
                int owner = b.Sides[k].Owner;
                if (owner >= 0 && !BattleRules.HasUsable(s, ctx, owner)) TurnFlow.Whiteout(s, ctx, fx, owner, owner == b.BlastSeat ? b.BlastTo : -1); // team_rocket R이 고른 칸(rulings A35)
            }
            for (int i = s.Used.Count - 1; i >= 0; i--)
                if (s.Used[i].Kind == UsageKind.FateBattle) s.Used.RemoveAt(i);
            CraftRules.BattleEnded(s, b); // 매혹적인 스튜는 다음 야생 조우까지만(S10)
            EffectRules.ClearLimits(s, ctx, EffectLimit.BattleTurn);
            EffectRules.ClearLimits(s, ctx, EffectLimit.Battle);
            EffectRules.ClearLimits(s, ctx, EffectLimit.UntilSwitch);
            s.Battle = null;
            s.Phase = Phase.Turn;

            if (b.Kind == BattleKind.Traitor)
            {
                HosFlow.AfterTraitorBattle(s, ctx, fx, winner);
                return;
            }
            // 네크로즈마: 이기면 다음 페이즈(3페이즈면 모두 승리), 지면(기권 포함) 탈락 — 게임이 끝났거나 부활할 좌석을 묻는 중이면 여기서 멈춘다(rulings A54)
            if (b.Kind == BattleKind.Necrozma && UbNecrozma.Concluded(s, ctx, fx, seat0, result == BattleResult.Win && winner == 0)) return;
            if (b.Kind != BattleKind.Trainer && noRoom.Count > 0 && AfterBattle(s, ctx, fx, seat0, noRoom, teach)) return;
            if (b.Kind == BattleKind.Trainer && winner >= 0 && (result == BattleResult.Win || result == BattleResult.Forfeited))
            {
                // p.9 「After winning a battle against another trainer: You may take 1 Artifact of your choice from them or take up to 2 of their Change of Fate cards at random」
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TrainerReward, Seat = b.Sides[winner].Owner, Subject = b.Sides[1 - winner].Owner, Window = Window.BattleEnd };
                var legal = new List<ChoiceAnswer>();
                ChoiceRules.Legal(s, ctx, c, legal);
                if (legal.Count > 0)
                {
                    // 이어서 물을 놓아주기(team_rocket B로 잡았지만 빈 칸이 없다): Options = [고를 좌석, 포켓몬…]
                    if (noRoom.Count > 0)
                    {
                        c.Options.Add(roomSeat);
                        c.Options.AddRange(noRoom);
                    }
                    ChoiceRules.Open(s, ctx, fx, c);
                    return;
                }
            }
            // UB 격파: 모든 좌석의 균열 카드 버리기·옮기기를 먼저 묻는다(기술 가르치기와 동시 — rulings A52 ⑥). 묶음이 끝나면 AfterRelease가 가르치기·턴 끝을 잇는다
            if (b.Kind == BattleKind.UltraBeast && result == BattleResult.Win && winner == 0 && BreachRules.OpenRelease(s, ctx, fx, seat0, teach)) return;
            if (AfterBattle(s, ctx, fx, roomSeat, noRoom, teach)) return;
            Close(s, ctx, fx, after);
        }

        /// <summary>
        /// 배틀 뒤 놓아주기·기술 가르치기를 차례로 하나씩 묻는다(Window = BattleEnd): 잡았지만 빈 칸이 없는 포켓몬(PartyFull)부터, 그다음 쓰러뜨린 야생의 기술(TeachMove — p.9 배울 수 있는 타입의
        /// 파티 포켓몬에게, 가르칠 포켓몬이 없으면 건너뛴다). 남은 것은 연 선택의 Options에 싣는다(앞의 Count개 = 남은 잡은 포켓몬, 나머지 = 남은 기술 — 무리 배틀은 여럿, rulings A28 ②). 열었으면 true.
        /// </summary>
        static bool AfterBattle(GameState s, Ctx ctx, Emitter fx, int seat, List<int> caught, List<int> moves)
        {
            if (caught.Count > 0)
            {
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.PartyFull, Seat = seat, Subject = caught[0], Window = Window.BattleEnd, Count = caught.Count - 1 };
                c.Options.AddRange(caught.GetRange(1, caught.Count - 1));
                c.Options.AddRange(moves);
                ChoiceRules.Open(s, ctx, fx, c);
                return true;
            }
            var legal = new List<ChoiceAnswer>();
            for (int i = 0; i < moves.Count; i++)
            {
                // 음수(~기술) = 앞 기술과 같은 포켓몬의 다른 폼 시그니처(AddTeach) — 앞 것을 안 가르쳤거나 배울 포켓몬이 없을 때만 여기까지 온다
                int move = moves[i] < 0 ? ~moves[i] : moves[i];
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = seat, Subject = move, Window = Window.BattleEnd };
                ChoiceRules.Legal(s, ctx, c, legal);
                if (legal.Count <= 1) continue;
                c.Options.AddRange(moves.GetRange(i + 1, moves.Count - i - 1));
                ChoiceRules.Open(s, ctx, fx, c);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 쓰러뜨린 포켓몬 하나가 가르칠 기술 묶음(<see cref="FormRules.Signatures"/> — 폼마다 다른 시그니처 가운데 하나, p.6 · 신도 원작 커뮤니티 확인)을 teach에 더한다.
        /// 첫째는 기술 색인, 둘째부터는 ~기술(음수) — 하나씩 묻고(안 가르치면 다음 폼의 것), 하나를 가르치면 그 묶음의 나머지는 묻지 않는다 [가정 plan §5 #95 ①].
        /// </summary>
        internal static void AddTeach(Ctx ctx, int def, List<int> teach)
        {
            var sigs = new List<int>();
            FormRules.Signatures(ctx, def, sigs);
            for (int i = 0; i < sigs.Count; i++) teach.Add(i == 0 ? sigs[i] : ~sigs[i]);
        }

        /// <summary>배틀 뒤 선택에 이어 물을 것(<see cref="AfterBattle"/>가 Options에 실은 것)을 묻고, 없으면 턴을 끝낸다. taught = 방금 기술을 가르쳤다(그 묶음의 다른 폼 시그니처를 건너뛴다).</summary>
        static void Next(GameState s, Ctx ctx, Emitter fx, PendingChoice c, bool taught = false)
        {
            int n = c.Sub == ChoiceSub.PartyFull ? Math.Max(0, Math.Min(c.Count, c.Options.Count)) : 0;
            var caught = c.Options.GetRange(0, n);
            var moves = c.Options.GetRange(n, c.Options.Count - n);
            if (taught) while (moves.Count > 0 && moves[0] < 0) moves.RemoveAt(0);
            if (!AfterBattle(s, ctx, fx, c.Seat, caught, moves)) Close(s, ctx, fx, 0);
        }

        /// <summary>UB 격파 뒤 균열 카드 버리기·옮기기 묶음(BreachRules.OpenRelease)이 끝났다: 미뤄 둔 기술 가르치기를 묻고, 없으면 턴을 끝낸다.</summary>
        internal static void AfterRelease(GameState s, Ctx ctx, Emitter fx, int seat, List<int> moves)
        {
            if (!AfterBattle(s, ctx, fx, seat, new List<int>(), moves)) Close(s, ctx, fx, 0);
        }

        /// <summary>네크로즈마에게 모두 탈락한 뒤 부활할 좌석을 골랐다(UbBlessing.ApplyRevive): 멈춰 둔 턴 끝을 잇는다.</summary>
        internal static void AfterRevive(GameState s, Ctx ctx, Emitter fx) => Close(s, ctx, fx, 0);

        /// <summary>1막 해방(A1): 1막 목표 달성, 고향 마을의 조무래기 토큰 제거, 이후 매 턴 이동력 +2(TurnFlow). 처음 해방한 좌석이면 true(2막 공개).</summary>
        static bool Liberate(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var st = s.Seats[seat];
            st.ActProgress = 1;
            bool first = s.Hos != null && s.Hos.FirstSeat[1] < 0;
            if (first) s.Hos.FirstSeat[1] = seat;
            if (fx.On) fx.Add(new ActGoalMet { Seat = seat, Act = 1, First = first, Changes = new ResChange[0] });
            int cell = ctx.Fixtures.Fixtures[st.HomeVillage].Cell;
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.Grunt || t.Cell != cell || t.Destroyed) continue;
                t.Cell = -1;
                if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = TokenKind.Grunt });
            }
            return first;
        }

        /// <summary>배틀 뒤 선택의 답(Window = BattleEnd): 산호 색(도망), 파티 가득 놓아주기(포획), 트레이너전 승리 보상, 기술 가르치기(격파).</summary>
        public static void ApplyReward(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            switch (c.Sub)
            {
                case ChoiceSub.CoralColor:
                    FinishFlee(s, ctx, fx, s.Battle, a.Values);
                    return;
                case ChoiceSub.PartyFull:
                {
                    int caught = c.Subject;
                    MaintenanceRules.Release(s, ctx, fx, c.Seat, a.Target);
                    if (a.Target != caught) Gained(s, ctx, fx, c.Seat, caught, PokemonRules.AddToParty(s.Seats[c.Seat], caught));
                    Next(s, ctx, fx, c);
                    return;
                }
                case ChoiceSub.TrainerReward:
                    TakeSpoils(s, ctx, fx, c, a);
                    if (c.Options.Count > 1 && AfterBattle(s, ctx, fx, c.Options[0], c.Options.GetRange(1, c.Options.Count - 1), new List<int>())) return; // team_rocket B의 놓아주기
                    Close(s, ctx, fx, 0);
                    return;
                case ChoiceSub.TeachMove:
                    Teach(s, ctx, fx, c, a);
                    Next(s, ctx, fx, c, a.Kind == AnswerKind.Pick);
                    return;
                default:
                    throw new InvalidOperationException($"배틀 뒤 선택 {c.Sub}");
            }
        }

        /// <summary>기술 가르치기의 답(Reward/TeachMove — Subject = 기술): Pick(포켓몬, 칸)이면 그 칸에 가르친다. 배틀 뒤 보상과 scientist B(TrainerRules.Map)가 같이 쓴다.</summary>
        internal static void Teach(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (a.Kind != AnswerKind.Pick) return;
            var p = s.Pokemon[a.Target];
            int old = p.Moves[a.Arg];
            p.Moves[a.Arg] = c.Subject;
            EvolveRules.Forgot(s, p, old); // 그 기술 때문에 밑에 둔 옛 카드는 버린다(p.5)
            TrainerRules.Pooled(s, ctx, fx, c.Seat, old); // collector R: 버린 기술은 스탬프 보상 풀로(rulings A39)
            if (fx.On) fx.Add(new MoveTaught { Seat = c.Seat, PokemonId = a.Target, Slot = a.Arg, MoveKey = c.Subject, ReplacedKey = old });
        }

        /// <summary>
        /// 트레이너전 승리 보상의 답(p.9): Pick = 진 좌석의 유물 하나(가방 또는 지닌 것, RelicRules.Take) · Accept(Target = 장수) = 진 좌석 손패에서 무작위로 그만큼(게임 난수).
        /// SpoilsTaken(자원 변화 — 운명은 장수만, 카드는 가린다). 받은 운명으로 손패가 넘치면 A12 버리기.
        /// </summary>
        static void TakeSpoils(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            int from = c.Subject, to = c.Seat;
            var changes = new List<ResChange>();
            int holder = -1;
            if (a.Kind == AnswerKind.Pick)
            {
                holder = RelicRules.Take(s, ctx, from, to, a.Target);
                changes.Add(new ResChange { Seat = from, Res = ResKind.Relic, Key = a.Target, Before = 1, After = 0 });
                changes.Add(new ResChange { Seat = to, Res = ResKind.Relic, Key = a.Target, Before = 0, After = 1 });
            }
            else
            {
                var loser = s.Seats[from].FateHand;
                var winner = s.Seats[to].FateHand;
                int lb = loser.Count, wb = winner.Count;
                for (int n = 0; n < a.Target && loser.Count > 0; n++)
                {
                    int i = s.Rng.Next(loser.Count);
                    winner.Add(loser[i]);
                    loser.RemoveAt(i);
                }
                changes.Add(new ResChange { Seat = from, Res = ResKind.Fate, Key = -1, Before = lb, After = loser.Count });
                changes.Add(new ResChange { Seat = to, Res = ResKind.Fate, Key = -1, Before = wb, After = winner.Count });
            }
            if (fx.On) fx.Add(new SpoilsTaken { Seat = to, From = from, Changes = changes.ToArray() });
            FormRules.HeldChanged(s, ctx, fx, holder); // 유물을 빼앗긴 쉐이미·로토무는 기본 폼으로(원작 커뮤니티 확인)
            FateRules.Overflow(s, ctx, fx);
        }

        /// <summary>
        /// 배틀 뒤 턴을 끝낸다(p.3 「your turn immediately ends after any battle」). after(HosFlow.GalacticWon): 1 = 막 목표를 처음 달성했다 — 턴 끝과 다음 턴 사이에
        /// 다음 막을 공개한다(1막 해방 → 2막, 조각 셋 → 3막). 2 = 태홍을 이겼다 — 종막 카드를 공개하고 다음 턴 대신 영웅·배신자를 묻는다(턴이 멈춘다).
        /// </summary>
        internal static void Close(GameState s, Ctx ctx, Emitter fx, int after)
        {
            if (s.Phase != Phase.Turn || s.ActiveSeat < 0) return;
            int seat = s.ActiveSeat;
            if (TrainerRules.UnlockAsk(s, ctx, fx, 1 + after)) return; // 배틀 중에 풀린 「When unlocked」의 고르기(답이 Close를 다시 부른다, rulings A40)
            if (TrainerRules.Tent(s, ctx, fx, after)) return; // backpacker B: 배틀 뒤 한 마리 완전 회복(답이 Close를 다시 부른다, rulings A38)
            if (TrainerRules.ScoopBack(s, ctx, fx)) return; // interviewers B의 턴 밖 도전이 끝났다 — 끝낼 턴 없이 물은 자리로(사용자 결정 84 T14)
            if (after == 0 && TrainerRules.Scoop(s, ctx, fx, 1)) return; // interviewers B: 이 턴에 스탬프를 얻은 좌석에 도전할까(rulings A38)
            // 턴 끝에 고를 것이 있는 트레이너 카드(team_aqua B·team_magma B의 칸 · essentia B의 남은 이동력, rulings A36): 닫는 MapAction을 열고 그 답이 FinishClose를 잇는다
            if (TrainerRules.TurnClosing(s, ctx, fx, seat, after, TurnEndReason.Battle)) return;
            FinishClose(s, ctx, fx, seat, after, TurnEndReason.Battle);
        }

        /// <summary>턴 끝(TurnEnded)과 그 뒤 할 일(막 공개 · 종막 · 추가 턴 · 다음 턴). Close와 닫는 MapAction의 턴 종료 답(MapActions.Apply)이 부른다.</summary>
        internal static void FinishClose(GameState s, Ctx ctx, Emitter fx, int seat, int after, TurnEndReason reason)
        {
            bool extra = MapActions.Used(s, seat, UsageKind.GodVictory); // 신을 이겼다(쓰러뜨림·포획) — 한 턴 더(p.21, rulings A28 ①)
            TurnFlow.FinishTurn(s, fx, reason);
            bool frenzy = TrainerRules.FrenzyNow(s, seat); // bug_catcher B: 벌레 타입을 잡았으면 추가 턴(rulings A38)
            if (after >= 1) SetupFlow.RevealAct(s, ctx, fx, seat);
            if (after == 2) HosFlow.OpenEnding(s, ctx, fx, seat);
            else if (extra || frenzy)
            {
                TurnFlow.ExtraTurn(s, ctx, fx, seat);
                if (frenzy) TrainerRules.FrenzyStarted(s, seat);
            }
            else TurnFlow.NextTurn(s, ctx, fx);
        }
    }
}
