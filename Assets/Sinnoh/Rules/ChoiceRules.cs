using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 선택 뼈대(01 §3-2·§3-3, rulings G9): ChoiceKind마다 시계·Legal·Default, 열기(ChoiceRequested), 정비 뒤 다시 열기(ChoiceReissued, §3-4).
    /// Legal 순서(G9): ① 「안 함」 → ② 원문 순서 → ③ 데이터 순서(내 포켓몬은 파티 순서) → ④ 좌석은 다음부터 시계 방향 → ⑤ 칸 색인.
    /// Default는 늘 Legal 안이다 — 열 때 확인하고 어기면 예외(봇 플레이아웃 조건 「모든 선택의 Default가 합법」).
    /// 답을 적용하는 일은 그 선택을 여는 기능이 맡는다(셋업·맵 M3-d, 배틀·도전·배틀 뒤 보상 M3-e BattleFlow, 그 밖 보상·종막 M5). Legal은 상태에서 읽는 것이 기본이고,
    /// 상태에 없는 공개 후보만 PendingChoice.Options에 둔다.
    /// </summary>
    public static class ChoiceRules
    {
        /// <summary>선택마다 거는 시계(02 §5-2).</summary>
        public static ClockKind ClockOf(ChoiceKind k)
        {
            switch (k)
            {
                case ChoiceKind.VillagePick:
                case ChoiceKind.StarterPick:
                case ChoiceKind.TrainerCardPick:
                    return ClockKind.Setup;
                case ChoiceKind.MapAction:
                case ChoiceKind.HiddenSpot:
                case ChoiceKind.EncounterPick:
                    return ClockKind.MapTurn;
                case ChoiceKind.Lineup:
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                case ChoiceKind.SwitchWindow:
                case ChoiceKind.BattleAction:
                    return ClockKind.BattleAction;
                case ChoiceKind.Reaction:
                    return ClockKind.Reaction;
                default:
                    return ClockKind.Respond; // Respond·Simultaneous·Discard·Reward·Ending
            }
        }

        /// <summary>
        /// 답이 앞 행동을 잇는 선택(01 §3-6 ①-2): 찾아보기 → 조우 고르기, 숨겨진 장소 멈춤 → 공개·지나가기, 도전·거래 → 응답,
        /// 동시 선택 → 공개(셋업 고르기·천상의 균열·균열 카드), 손패 6장째 → 버리기, 간부·태홍 도전 → 명단(M5). 반응은 Continues가 아니라 ReactsTo다.
        /// </summary>
        public static bool Continues(ChoiceKind k) =>
            k == ChoiceKind.Lineup || k == ChoiceKind.EncounterPick || k == ChoiceKind.HiddenSpot || k == ChoiceKind.Respond || k == ChoiceKind.Simultaneous
            || k == ChoiceKind.StarterPick || k == ChoiceKind.TrainerCardPick || k == ChoiceKind.Discard;

        /// <summary>남의 보기에서 후보·Default·받아 둔 답을 가리는 선택(01 §4): 비공개 묶음, 트레이너 카드 후보, 운명 손패 버리기, 거래 응답, 동등한 교환의 카드(조우 덱 내용).</summary>
        public static bool HidesFromOthers(PendingChoice c) => c.Secret || c.Kind == ChoiceKind.TrainerCardPick || c.Kind == ChoiceKind.Discard
            || (c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade) // 거래 내용의 운명 카드(제안한 좌석은 본다 — Engine.ViewFor)
            || (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.ShrineExchangeCard); // 고르는 좌석만 조우 덱을 찾는다(덱 내용은 숨는다 — Engine.ViewFor)

        /// <summary>ActionStarted.Kind(01 §3-6 ①): MapAction이면 답 종류, 아니면 선택 종류.</summary>
        public static ActionKind ActionKindOf(ChoiceKind k, AnswerKind a)
        {
            if (k != ChoiceKind.MapAction)
            {
                switch (k)
                {
                    case ChoiceKind.VillagePick: return ActionKind.VillagePick;
                    case ChoiceKind.StarterPick: return ActionKind.StarterPick;
                    case ChoiceKind.TrainerCardPick: return ActionKind.TrainerCardPick;
                    case ChoiceKind.HiddenSpot: return ActionKind.HiddenSpot;
                    case ChoiceKind.EncounterPick: return ActionKind.EncounterPick;
                    case ChoiceKind.Lineup: return ActionKind.Lineup;
                    case ChoiceKind.SendIn: return ActionKind.SendIn;
                    case ChoiceKind.NextPokemon: return ActionKind.NextPokemon;
                    case ChoiceKind.SwitchWindow: return ActionKind.SwitchWindow;
                    case ChoiceKind.BattleAction: return ActionKind.BattleAction;
                    case ChoiceKind.Reaction: return ActionKind.Reaction;
                    case ChoiceKind.Respond: return ActionKind.Respond;
                    case ChoiceKind.Simultaneous: return ActionKind.Simultaneous;
                    case ChoiceKind.Discard: return ActionKind.Discard;
                    case ChoiceKind.Reward: return ActionKind.Reward;
                    case ChoiceKind.Ending: return ActionKind.Ending;
                }
                throw new ArgumentOutOfRangeException(nameof(k), k, null);
            }
            switch (a)
            {
                case AnswerKind.Move: return ActionKind.Move;
                case AnswerKind.Search: return ActionKind.Search;
                case AnswerKind.TakeApricorns: return ActionKind.TakeApricorns;
                case AnswerKind.Offer: return ActionKind.Offer;
                case AnswerKind.VillageHeal: return ActionKind.VillageHeal;
                case AnswerKind.Ginkgo: return ActionKind.Ginkgo;
                case AnswerKind.Fly: return ActionKind.Fly;
                case AnswerKind.Craft: return ActionKind.Craft;
                case AnswerKind.Evolve: return ActionKind.Evolve;
                case AnswerKind.PlayFate: return ActionKind.PlayFate;
                case AnswerKind.UseAbility: return ActionKind.UseAbility;
                case AnswerKind.Challenge: return ActionKind.Challenge;
                case AnswerKind.ProposeTrade: return ActionKind.ProposeTrade;
                case AnswerKind.EndTurn: return ActionKind.EndTurn;
            }
            throw new ArgumentOutOfRangeException(nameof(a), a, "MapAction 답이 아니다");
        }

        public static PendingChoice Find(GameState s, int id)
        {
            foreach (var c in s.Pending)
                if (c.Id == id) return c;
            return null;
        }

        // ── 열기 ──

        /// <summary>
        /// 선택 하나를 연다(01 §3-1 ④). 부르는 쪽이 Kind·Seat·Sub·Window·Source·HandGated·Secret·Options·Count·Subject를 채운다.
        /// 여기서 Id·Clock·FromSeq(기본 지금 Seq)·GroupId(없으면 자기 Id)·Default를 정하고 ChoiceRequested를 낸다.
        /// </summary>
        public static PendingChoice Open(GameState s, Ctx ctx, Emitter fx, PendingChoice c, int fromSeq = int.MinValue)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            c.Id = s.NextChoiceId++;
            if (c.GroupId < 0) c.GroupId = c.Id;
            c.FromSeq = fromSeq == int.MinValue ? fx.Seq : fromSeq;
            if (Hold(s, c)) return c;
            Prepare(s, ctx, fx, c);
            return c;
        }

        /// <summary>운명 손패 버리기(A12)가 열려 있는 동안 새 선택은 미뤄 둔다(GameState.Held) — 버리기 자신은 빼고. 「맞추기 전에는 다른 행동을 하지 않는다」.</summary>
        static bool Hold(GameState s, PendingChoice c)
        {
            if (c.Kind == ChoiceKind.Discard || !(FateRules.Discarding(s) || TradeRules.Waiting(s))) return false; // 거래 응답도 막는다(rulings A29)
            s.Held.Add(c);
            return true;
        }

        /// <summary>미뤄 둔 선택을 연 순서대로 연다(Legal·Default는 지금 상태로). 버리기가 모두 끝났을 때 FateRules가 부른다.</summary>
        internal static void Release(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Held.Count == 0 || FateRules.Discarding(s) || TradeRules.Waiting(s)) return;
            var held = new List<PendingChoice>(s.Held);
            s.Held.Clear();
            foreach (var c in held) Prepare(s, ctx, fx, c);
        }

        /// <summary>동시 선택 묶음(01 §3-3 GroupId): 모두 같은 GroupId(첫 Id). 묶음이 다 답해야 진행한다. Secret은 부르는 쪽이 정한다.</summary>
        public static void OpenGroup(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> group, int fromSeq = int.MinValue)
        {
            if (group == null || group.Count == 0) throw new ArgumentException("빈 묶음", nameof(group));
            int first = s.NextChoiceId;
            foreach (var c in group)
            {
                c.Id = s.NextChoiceId++;
                c.GroupId = first;
                c.FromSeq = fromSeq == int.MinValue ? fx.Seq : fromSeq;
            }
            foreach (var c in group)
                if (!Hold(s, c)) Prepare(s, ctx, fx, c);
        }

        static void Prepare(GameState s, Ctx ctx, Emitter fx, PendingChoice c)
        {
            c.Clock = ClockOf(c.Kind);
            c.Answered = false;
            c.Answer.CopyFrom(new ChoiceAnswer());
            var legal = new List<ChoiceAnswer>();
            Legal(s, ctx, c, legal);
            if (legal.Count == 0) throw new InvalidOperationException($"{c.Kind}/{c.Sub} 좌석 {c.Seat}: 합법 답이 없는 선택은 열지 않는다(01 §3-3 「쓸 것이 있을 때만 묻는다」)");
            c.Default.CopyFrom(ComputeDefault(s, ctx, c, legal));
            if (!Contains(legal, c.Default)) throw new InvalidOperationException($"{c.Kind}/{c.Sub}: Default {c.Default}가 Legal 밖이다");
            s.Pending.Add(c);
            if (fx.On) fx.Add(Requested(c, legal));
        }

        static ChoiceRequested Requested(PendingChoice c, List<ChoiceAnswer> legal)
        {
            var copy = new List<ChoiceAnswer>(legal.Count);
            foreach (var a in legal) copy.Add(a.Clone());
            return new ChoiceRequested
            {
                Seat = c.Seat, Id = c.Id, Kind = c.Kind, Sub = c.Sub, GroupId = c.GroupId, Clock = c.Clock, Source = c.Source,
                Window = c.Window, HandGated = c.HandGated, Count = c.Count, Legal = copy,
            };
        }

        // ── 다시 열기(정비, 01 §3-4) ──

        /// <summary>열린 선택마다 Legal 지문(FNV-1a). 정비 전후를 비교한다.</summary>
        public static List<ulong> Fingerprints(GameState s, Ctx ctx)
        {
            var r = new List<ulong>(s.Pending.Count);
            var legal = new List<ChoiceAnswer>();
            foreach (var c in s.Pending)
            {
                Legal(s, ctx, c, legal);
                var h = new Fnv1a64();
                foreach (var a in legal) a.Walk(h);
                r.Add(h.Value);
            }
            return r;
        }

        /// <summary>
        /// 정비가 선택지를 바꾼 열린 선택을 닫고 새 Id로 다시 연다(ChoiceReissued + ChoiceRequested). 받아 둔 답은 버린다 — 답은 늘 그때 상태로 다시 검증한다.
        /// before는 같은 Pending 순서의 Fingerprints(정비는 선택을 더하거나 빼지 않는다 — 아르세우스의 축복 동의 묶음은 MaintenanceRules.Submit이 따로 다룬다).
        /// </summary>
        public static void ReissueChanged(GameState s, Ctx ctx, Emitter fx, List<ulong> before)
        {
            var after = Fingerprints(s, ctx);
            if (after.Count != before.Count) throw new InvalidOperationException("정비가 열린 선택 수를 바꿨다");
            for (int i = 0; i < after.Count; i++)
            {
                if (after[i] == before[i]) continue;
                var c = s.Pending[i];
                int old = c.Id;
                c.Id = s.NextChoiceId++;
                s.Pending.RemoveAt(i);
                if (fx.On) fx.Add(new ChoiceReissued { Seat = c.Seat, OldId = old, NewId = c.Id });
                Prepare(s, ctx, fx, c);
                // Prepare가 맨 뒤에 다시 넣었다. 순서를 지키려고 제자리로 옮긴다(Fingerprints 순서 = Pending 순서).
                s.Pending.RemoveAt(s.Pending.Count - 1);
                s.Pending.Insert(i, c);
            }
        }

        // ── Legal ──

        public static bool Contains(List<ChoiceAnswer> legal, ChoiceAnswer a)
        {
            foreach (var x in legal)
                if (x.SameAs(a)) return true;
            return false;
        }

        /// <summary>
        /// 답이 합법인가(검증 「답 ∈ Legal」). 결정 71 고른 길(Move + Values)만 Legal에 늘어놓지 않는다 — 같은 도착 칸의 옛 꼴이 Legal에 있고
        /// 길이 이어지고 남은 이동력 안이면 받는다(<see cref="MapActions.WalkAllowed"/>).
        /// </summary>
        public static bool IsLegal(GameState s, Ctx ctx, PendingChoice c, ChoiceAnswer a)
        {
            var legal = new List<ChoiceAnswer>();
            Legal(s, ctx, c, legal);
            if (a != null && c.Kind == ChoiceKind.MapAction && a.Kind == AnswerKind.Move && a.Values.Count > 0)
                return Contains(legal, ChoiceAnswer.Of(AnswerKind.Move, a.Target, a.Arg)) && MapActions.WalkAllowed(s, ctx, c.Seat, a);
            if (a != null && c.Kind == ChoiceKind.MapAction && a.Kind == AnswerKind.ProposeTrade) return TradeRules.OfferAllowed(s, ctx, c.Seat, a); // 거래 내용도 늘어놓지 않는다(rulings A29)
            return Contains(legal, a);
        }

        /// <summary>
        /// 그 선택에서 둘 수 있는 답 전부(01 §9-2). 화면 하이라이트·Default·봇·AI·검증이 모두 이것을 쓴다. 순서는 G9.
        /// 보기 사본(ViewFor)에서도 자기 선택이면 같은 답이 나온다(자기 손패·후보는 보이고, 나머지는 공개 정보다).
        /// </summary>
        public static void Legal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            into.Clear();
            switch (c.Kind)
            {
                case ChoiceKind.VillagePick:
                {
                    var fixtures = ctx.Fixtures.Fixtures;
                    for (int f = 0; f < fixtures.Count; f++)
                    {
                        if (!SetupFlow.IsHomeVillageOption(s, ctx, f)) continue;
                        bool taken = false;
                        foreach (var seat in s.Seats)
                            if (seat.HomeVillage == f) taken = true;
                        if (!taken) into.Add(ChoiceAnswer.Pick(f));
                    }
                    break;
                }
                case ChoiceKind.StarterPick:
                    // 고른 마을 카드의 스타터 두 마리(열 때 Options, rulings G12). 스타터 덱 자체는 보기에서 가린다.
                    PickDistinct(c.Options, into);
                    break;
                case ChoiceKind.TrainerCardPick:
                    PickDistinct(s.Seats[c.Seat].TrainerCardOffer, into);
                    break;
                case ChoiceKind.MapAction:
                    // 턴 끝(늘 됨, Default) → 이동·칸 행동·마을 행동(MapActions, M3-d). 사당·은행상회·제작·도전 … 은 그 작업이 더한다.
                    MapActions.Legal(s, ctx, c, into);
                    break;
                case ChoiceKind.HiddenSpot:
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // 지나가기
                    into.Add(ChoiceAnswer.Of(AnswerKind.Accept));  // 공개(A23)
                    break;
                case ChoiceKind.EncounterPick:
                    // 조우 카드 1장(반드시, S5_1). 같은 카드가 둘일 수 있어 Arg = 줄 안 자리.
                    for (int i = 0; i < c.Options.Count; i++) into.Add(ChoiceAnswer.Pick(c.Options[i], i));
                    // 고르기 전에도 제작은 된다(룰북 p.2 「you may still craft items like Poké Balls before the battle begins」) — 답하면 같은 고르기를 다시 연다
                    CraftRules.Legal(s, ctx, c.Seat, into);
                    if (c.Count == 0 && TrainerRules.CanRetry(s, ctx, c.Seat)) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility)); // ace_trainer B: 조우당 한 번 다시 찾아보기(rulings A39)
                    break;
                case ChoiceKind.Lineup:
                    // 명단 Options에서 Count마리(A16). Values = Options 자리, 오름차순.
                    Combinations(c.Options.Count, c.Count, comb => into.Add(ChoiceAnswer.PickMany(comb)));
                    break;
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                    BenchOptions(s, ctx, c.Seat, into);
                    break;
                case ChoiceKind.SwitchWindow:
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // 교체 안 함(G9)
                    BenchOptions(s, ctx, c.Seat, into);
                    break;
                case ChoiceKind.BattleAction:
                    BattleActions(s, ctx, c.Seat, into);
                    break;
                case ChoiceKind.Reaction:
                    if (c.Sub == ChoiceSub.EffectOrder)
                    {
                        EffectRules.OrderOptions(s, ctx, into); // 결정 72: 결과가 다른 순서마다 하나(첫 답 = 기본 순서). 「안 씀」은 없다
                        break;
                    }
                    if (c.Sub == ChoiceSub.EffectPick)
                    {
                        if (c.Count == 0) into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // 「You may …」: 안 함이 첫 답(G9)
                        PickDistinct(c.Options, into);   // 기술 효과의 고르기(rulings G20): 열 때 정한 값
                        break;
                    }
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // 안 씀
                    if (c.Sub == ChoiceSub.InitiativeToken || c.Sub == ChoiceSub.Retribution || c.Sub == ChoiceSub.EffectOption || c.Sub == ChoiceSub.AlluringStew) into.Add(ChoiceAnswer.Of(AnswerKind.Accept));
                    if (c.Sub == ChoiceSub.Fate)
                    {
                        FateRules.ReactionLegal(s, ctx, c.Seat, c.Window, into); // 손패에서 이 창에 낼 수 있는 효과(S13)
                        TrainerRules.PsychicLegal(s, ctx, c.Seat, c.Window, into); // psychic R(결정 131 — 굴린 뒤 창의 공격하는 좌석)
                        TrainerRules.AmpLegal(s, ctx, c.Seat, c.Window, into); // guitarist B·R 앰프(결정 134 — 같은 창 · 같은 좌석)
                    }
                    break;
                case ChoiceKind.Respond:
                    if (c.Sub == ChoiceSub.Trade)
                    {
                        TradeRules.RespondLegal(s, ctx, c, into); // 거절 → 받아들임(줄 운명 카드마다, rulings A29)
                        break;
                    }
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline)); // 거절
                    into.Add(ChoiceAnswer.Of(AnswerKind.Accept));
                    break;
                case ChoiceKind.Simultaneous:
                    if (c.Sub == ChoiceSub.Follower)
                    {
                        // 안 고름 — 한 번 따르면 못 돌아간다(rulings A31 ①). 따르던 신이 게임에서 빠졌으면 그대로 둘 수도 있다(Decline = 그대로 — 세지 않는다) [가정 plan §5 #95 ②]
                        var follower = s.Seats[c.Seat].Follower;
                        // 무녀 파랑 「become a Follower of Dialga for the rest of the game」 · 아르세우스의 추종자 「for the rest of the game」: 바꿀 수 없다 — 그대로(그 신 · 빠졌으면 Decline)만(rulings A40)
                        if (TrainerRules.FixedFollower(s, ctx, c.Seat))
                        {
                            if (follower != SinnohGod.Arceus && FractureRules.InGame(s, ctx, follower)) into.Add(ChoiceAnswer.Pick((int)follower));
                            else into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                            break;
                        }
                        if (follower == SinnohGod.None || !FractureRules.InGame(s, ctx, follower)) into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                        // 이미 빠진 신(잡히거나 쓰러짐)은 고를 수 없다(원작 커뮤니티 확인)
                        foreach (var god in new[] { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina })
                            if (FractureRules.InGame(s, ctx, god)) into.Add(ChoiceAnswer.Pick((int)god));
                    }
                    else BreachRules.Legal(s, ctx, c, into); // BreachAttach: 붙일 (포켓몬, 칸) · UB 격파 뒤 버리기·옮기기(rulings A52)
                    break;
                case ChoiceKind.Discard:
                    PickDistinct(s.Seats[c.Seat].FateHand, into);
                    break;
                case ChoiceKind.Reward:
                    RewardOptions(s, ctx, c, into);
                    break;
                case ChoiceKind.Ending:
                    if (c.Sub == ChoiceSub.TraitorOrder) Permutations(c.Options, perm => into.Add(ChoiceAnswer.PickMany(perm)));
                    else
                    {
                        into.Add(ChoiceAnswer.Pick(0)); // 영웅
                        into.Add(ChoiceAnswer.Pick(1)); // 배신자
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(c.Kind), c.Kind, null);
            }
        }

        static void PickDistinct(List<int> values, List<ChoiceAnswer> into)
        {
            for (int i = 0; i < values.Count; i++)
            {
                bool seen = false;
                for (int k = 0; k < i; k++)
                    if (values[k] == values[i]) seen = true;
                if (!seen) into.Add(ChoiceAnswer.Pick(values[i]));
            }
        }

        /// <summary>배틀에서 그 응답자가 맡은 쪽(Responder, 없으면 Owner). 없으면 −1.</summary>
        public static int BattleSideOf(GameState s, int responder)
        {
            var b = s.Battle;
            if (b == null) return -1;
            for (int k = 0; k < BattleState.SideCount; k++)
                if (b.Sides[k].Responder == responder) return k;
            for (int k = 0; k < BattleState.SideCount; k++)
                if (b.Sides[k].Owner == responder) return k;
            return -1;
        }

        /// <summary>내보낼 수 있는 포켓몬: 기절하지 않았고 출전 중이 아님. 좌석 쪽은 파티 순서, 가상 쪽은 명단 순서.</summary>
        static void BenchOptions(GameState s, Ctx ctx, int responder, List<ChoiceAnswer> into)
        {
            int k = BattleSideOf(s, responder);
            if (k < 0) return;
            var side = s.Battle.Sides[k];
            if (side.Owner >= 0)
            {
                var st = s.Seats[side.Owner];
                for (int i = 0; i < st.Party.Length; i++)
                {
                    int p = st.Party[i];
                    if (p >= 0 && p != side.Active && PokemonRules.Fights(st, i) && !PokemonRules.IsFainted(ctx, s.Pokemon[p]) && !BattleRules.Taken(s, side, p)) into.Add(ChoiceAnswer.Pick(p)); // 이 배틀에서 빼앗은 포켓몬은 못 낸다(결정 84 T7 ③)
                }
            }
            else
            {
                foreach (int p in side.Roster)
                    if (p != side.Active && BattleRules.OnSide(s, ctx, side, p)) into.Add(ChoiceAnswer.Pick(p));
            }
        }

        /// <summary>포획을 시도할 수 있는 배틀 종류(01 §6-5: 야생 배틀, UB·네크로즈마·uncapturable 불가).</summary>
        public static bool CaptureAllowed(BattleState b) =>
            !b.Uncapturable && b.Kind != BattleKind.Trainer && b.Kind != BattleKind.Galactic && b.Kind != BattleKind.Traitor
            && b.Kind != BattleKind.UltraBeast && b.Kind != BattleKind.Necrozma;

        /// <summary>기술(시그니처 → 배운 기술 → 갤럭시단은 플랜 B → 전설은 알파 스트라이크 × 배울 수 있는 타입, A26) → 볼(등급 순서, 가진 것) → 기권(이번 배틀에서 내 포켓몬이 1마리 기절한 뒤부터, 01 §6-5, rulings G13).</summary>
        static void BattleActions(GameState s, Ctx ctx, int responder, List<ChoiceAnswer> into)
        {
            int k = BattleSideOf(s, responder);
            if (k < 0) return;
            var b = s.Battle;
            var side = b.Sides[k];
            if (side.Active < 0) return;
            var moves = new List<int>(4);
            PokemonRules.MoveOptions(ctx, s.Pokemon[side.Active], moves);
            int unlocked = moves.Count;
            HeldItems.LockMoves(ctx, s.Pokemon[side.Active], moves); // 결단의 머리띠: 고른 기술만(S10)
            // poke_maniac B(rulings A34): 상대 출전 포켓몬의 시그니처 기술도 쓸 수 있다 — 결단의 머리띠로 묶여 있으면 없다
            int disguise = moves.Count == unlocked ? TrainerRules.DisguiseMove(s, ctx, b, k, moves) : -1;
            if (disguise >= 0) moves.Add(disguise);
            foreach (int m in moves)
            {
                if (m != BattleRules.AlphaStrike)
                {
                    into.Add(ChoiceAnswer.Of(AnswerKind.UseMove, m));
                    continue;
                }
                // 알파 스트라이크(rulings A26): 배울 수 있는 타입마다 답 하나(Values[0] = 타입, 카드 순서)
                var types = new List<int>(4);
                LegendRules.AlphaTypes(ctx, s.Pokemon[side.Active], types);
                foreach (int t in types) into.Add(AlphaStrikeAnswer(t));
            }
            FromPartyMoves(s, ctx, side, into);
            if (side.Owner < 0) return;
            var seat = s.Seats[side.Owner];
            // team_rocket B(rulings A35): 좌석끼리의 트레이너전에서도 볼을 던질 수 있다(굴림 −2) · 시련을 넘기기 전의 수호신은 포획 시도에 면역(p.26)
            if ((CaptureAllowed(b) || TrainerRules.CanSteal(s, ctx, b, k)) && b.Sides[1 - k].Active >= 0 && !UbTrial.Guards(s, ctx, b, b.Sides[1 - k].Active))
                for (int ball = 0; ball < SeatState.BallKinds; ball++)
                    if (seat.Balls[ball] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.ThrowBall, ball));
            // gentleman R 프레셔스 볼(rulings A41): 야생 쪽에만 — UseAbility(CherishTarget) · 굴리지 않고 잡는다 · 시련 전 수호신의 포획 면역은 여기도(rulings A55 ②)
            if (CaptureAllowed(b) && b.Sides[1 - k].Owner == SeatId.Wild && b.Sides[1 - k].Active >= 0 && !UbTrial.Guards(s, ctx, b, b.Sides[1 - k].Active) && TrainerRules.CherishReady(s, ctx, side.Owner))
                into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.CherishTarget));
            // lady R(rulings A35): 배틀당 1번 파티의 포켓몬 하나를 완전 회복(UseAbility, Target = 포켓몬 — 턴을 쓰지 않는다)
            var potion = new List<int>(6);
            TrainerRules.PotionTargets(s, ctx, b, k, potion);
            foreach (int p in potion) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, p));
            if (side.FaintedThisBattle) into.Add(ChoiceAnswer.Of(AnswerKind.Forfeit));
        }

        /// <summary>알파 스트라이크를 그 타입으로 쓰는 답(UseMove Target = AlphaStrike, Values = [타입], rulings A26).</summary>
        public static ChoiceAnswer AlphaStrikeAnswer(int type)
        {
            var a = ChoiceAnswer.Of(AnswerKind.UseMove, BattleRules.AlphaStrike);
            a.Values.Add(type);
            return a;
        }

        /// <summary>
        /// 벤치에서 쓰는 기술(lunge·acrobatics 「This move can be used from your party: Switch the user in and then use this attack」, rulings G20): UseMove(기술, Arg = 그 벤치 포켓몬), 파티 순서.
        /// 출전 중인 포켓몬이 빙결·교체 금지여도 된다(들어오는 쪽이 바꾼다 — 원작 커뮤니티 확인). fairy_lock(「can no longer switch out using move effects」)은 막는다 [가정].
        /// </summary>
        static void FromPartyMoves(GameState s, Ctx ctx, BattleSide side, List<ChoiceAnswer> into)
        {
            if (ModRules.MoveSwitchBlocked(s)) return;
            var bench = new List<ChoiceAnswer>();
            var moves = new List<int>(4);
            BenchOptions(s, ctx, side.Responder, bench);
            foreach (var p in bench)
            {
                PokemonRules.MoveOptions(ctx, s.Pokemon[p.Target], moves);
                foreach (int m in moves)
                    if (m >= 0 && EffectRules.UsableFromParty(ctx, m)) into.Add(ChoiceAnswer.Of(AnswerKind.UseMove, m, p.Target));
            }
        }

        static void RewardOptions(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            switch (c.Sub)
            {
                case ChoiceSub.TrainerReward:
                {
                    // 원문 순서(p.9): 상대 유물 1개(가방·지닌 것, 데이터 순서) → 운명 최대 2장(Accept, Target = 장수). Subject = 진 좌석.
                    // 「You may」지만 안 받는 답은 두지 않는다 [가정 plan §5 #85 ⑤ — 받아서 잃는 것이 없다: 넘친 운명은 버리기(A12)로 고른다]
                    var loser = s.Seats[c.Subject];
                    var relics = RelicRules.Owned(s, ctx, c.Subject);
                    if (TrainerRules.RelicsGuarded(s, ctx, c.Subject)) relics.Clear(); // ruin_maniac R 「cannot be taken by other trainers」(rulings A32)
                    relics.Sort();
                    PickDistinct(relics, into);
                    if (loser.FateHand.Count > 0) into.Add(ChoiceAnswer.Of(AnswerKind.Accept, Math.Min(2, loser.FateHand.Count)));
                    break;
                }
                case ChoiceSub.TeachMove:
                {
                    // 안 가르침 → 파티 순서 × 칸 0~1(MoveSlots, 시그니처 포함 3개). 배울 수 있는 타입만(p.9 「Move Type matches one of the Learnable Move Types」, 타입없음은 누구나). Subject = 기술.
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                    var type = ctx.Cards.Moves[c.Subject].Type;
                    bool any = c.Window == Window.BattleEnd && TrainerRules.Sketches(s, ctx, c.Seat); // artist B: 쓰러뜨린 야생의 기술은 타입과 상관없이(rulings A38)
                    foreach (int p in s.Seats[c.Seat].Party)
                    {
                        if (p < 0 || (!any && !CanLearn(ctx, s.Pokemon[p], type))) continue;
                        for (int slot = 0; slot < PokemonState.MoveSlots; slot++)
                            if (!BreachRules.Blocked(s.Pokemon[p], slot)) into.Add(ChoiceAnswer.Pick(p, slot)); // 균열 카드가 막은 칸은 못 쓴다(rulings A52 ①)
                    }
                    break;
                }
                case ChoiceSub.SharedBlessing:
                    // 다른 트레이너 1명(A19), 고르는 좌석의 다음부터 시계 방향.
                    for (int i = 1; i < s.SeatCount; i++) into.Add(ChoiceAnswer.Pick((c.Seat + i) % s.SeatCount));
                    break;
                case ChoiceSub.CoralColor:
                {
                    // 산호 규토리 색 중 Count개(A8). 색 표 순서로 정렬한 뒤 조합, 같은 색 목록은 한 번.
                    var colors = new List<int>(c.Options);
                    colors.Sort();
                    Combinations(colors.Count, c.Count, comb =>
                    {
                        var vals = new List<int>(comb.Count);
                        foreach (int i in comb) vals.Add(colors[i]);
                        var a = ChoiceAnswer.PickMany(vals);
                        if (!Contains(into, a)) into.Add(a);
                    });
                    break;
                }
                case ChoiceSub.PartyFull:
                    // 놓아줄 포켓몬(rulings G13): 새로 잡은 포켓몬(Subject, 파티 밖)이 먼저 — 가진 것을 잃지 않는 답(G9 원칙) → 파티 순서
                    into.Add(ChoiceAnswer.Pick(c.Subject));
                    foreach (int p in s.Seats[c.Seat].Party)
                        if (p >= 0 && !BreachRules.Any(s.Pokemon[p])) into.Add(ChoiceAnswer.Pick(p)); // 균열 카드가 붙은 포켓몬은 놓아줄 수 없다(p.24)
                    break;
                case ChoiceSub.EvolveTeach:
                    // 안 가르침 → 진화한 포켓몬(Options[0])의 칸 0~1. 배울 수 있는 타입을 보지 않는다(p.5 「regardless of the types it can learn」). Subject = 옛 시그니처 기술.
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                    for (int slot = 0; slot < PokemonState.MoveSlots; slot++)
                        if (!BreachRules.Blocked(s.Pokemon[c.Options[0]], slot)) into.Add(ChoiceAnswer.Pick(c.Options[0], slot));
                    break;
                default: // EvolveBranch: evolve_into 순서(R14) · 사당 카드의 고르기는 ShrineRules
                    if (ShrineRules.Owns(c.Sub)) ShrineRules.Legal(s, ctx, c, into);
                    else PickDistinct(c.Options, into);
                    break;
            }
        }

        /// <summary>그 타입의 기술을 배울 수 있나(p.9): 배울 수 있는 타입 중 하나와 같거나 타입없음(「Moves that are Typeless can be taught to any Pokémon.」).</summary>
        public static bool CanLearn(Ctx ctx, PokemonState p, PokemonType moveType)
        {
            if (moveType == PokemonType.Typeless) return true;
            foreach (var t in ctx.Cards.Pokemon[p.Def].LearnTypes)
                if (t == moveType) return true;
            return false;
        }

        /// <summary>n개 중 k개 자리 조합, 사전 순(첫째 = 0..k−1).</summary>
        static void Combinations(int n, int k, Action<List<int>> f)
        {
            if (k < 0 || k > n) return;
            var idx = new List<int>(k);
            for (int i = 0; i < k; i++) idx.Add(i);
            while (true)
            {
                f(new List<int>(idx));
                int j = k - 1;
                while (j >= 0 && idx[j] == n - k + j) j--;
                if (j < 0) return;
                idx[j]++;
                for (int t = j + 1; t < k; t++) idx[t] = idx[t - 1] + 1;
            }
        }

        /// <summary>목록의 순열, 원래 순서 기준 사전 순(첫째 = 원래 순서).</summary>
        static void Permutations(List<int> items, Action<List<int>> f)
        {
            int n = items.Count;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            while (true)
            {
                var p = new List<int>(n);
                for (int i = 0; i < n; i++) p.Add(items[idx[i]]);
                f(p);
                int a = n - 2;
                while (a >= 0 && idx[a] >= idx[a + 1]) a--;
                if (a < 0) return;
                int b = n - 1;
                while (idx[b] <= idx[a]) b--;
                int t = idx[a]; idx[a] = idx[b]; idx[b] = t;
                Array.Reverse(idx, a + 1, n - a - 1);
            }
        }

        // ── Default ──

        /// <summary>
        /// 시간 초과 답(01 §3-3 표, G9). 셋업 고르기는 엔진 Rng로 무작위(열 때 정하고 상태에 둔다 — Timeout이 로그에 남아 재현된다),
        /// 버리기는 가장 나중에 받은 카드, 천상의 균열은 지금 추종자, BattleAction은 p.25 원칙 기술, 나머지는 Legal 첫 답.
        /// </summary>
        static ChoiceAnswer ComputeDefault(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            switch (c.Kind)
            {
                case ChoiceKind.VillagePick:
                case ChoiceKind.StarterPick:
                case ChoiceKind.TrainerCardPick:
                    return legal[s.Rng.Next(legal.Count)];
                case ChoiceKind.Discard:
                {
                    var hand = s.Seats[c.Seat].FateHand;
                    return ChoiceAnswer.Pick(hand[hand.Count - 1]);
                }
                case ChoiceKind.Simultaneous when c.Sub == ChoiceSub.Follower:
                {
                    var keep = ChoiceAnswer.Pick((int)s.Seats[c.Seat].Follower);
                    return Contains(legal, keep) ? keep : legal[0];
                }
                case ChoiceKind.Simultaneous when c.Sub == ChoiceSub.BreachAttach:
                    return BreachRules.Default(s, c, legal); // 강제 고르기: 빈 칸 먼저(rulings A52 ④)
                case ChoiceKind.BattleAction:
                {
                    int k = BattleSideOf(s, c.Seat);
                    var b = s.Battle;
                    int def = b.Sides[1 - k].Active;
                    var want = PrincipleRules.Answer(ctx, s.Pokemon[b.Sides[k].Active], def >= 0 ? s.Pokemon[def] : null, out _);
                    return Contains(legal, want) ? want : legal[0]; // 결단의 머리띠로 묶였으면 그 기술(Legal 첫 답)
                }
                case ChoiceKind.Reward when c.Sub == ChoiceSub.FractureTie:
                    return FractureRules.TieDefault(s, ctx, c, legal); // 뽑은 좌석의 추종자 → 이미 있는 신 → 첫 신 [가정]
                case ChoiceKind.Respond when c.Sub == ChoiceSub.Blessing:
                    return ChoiceAnswer.Of(AnswerKind.Accept); // 축복 동의: 시간 초과 = 막지 않음(결정 84 U1)
                case ChoiceKind.Reaction when c.Sub == ChoiceSub.AlluringStew:
                    return CraftRules.AlluringHelps(s, ctx, c) ? ChoiceAnswer.Of(AnswerKind.Accept) : legal[0]; // 결과가 좋아질 때만 쓴다 [가정]
                default:
                    return legal[0];
            }
        }
    }
}
