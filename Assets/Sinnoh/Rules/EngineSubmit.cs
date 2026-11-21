using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    public static partial class Engine
    {
        /// <summary>
        /// 명령 하나(01 §3-1): ① 검증(응답자 / 정비 시점 / 답 ∈ Legal → 아니면 Rejected) → ② 적용(상태 + 이벤트) → ③ 진행(입력이 필요할 때까지)
        /// → ④ 멈춤(새 선택 + ChoiceRequested). 거절이면 상태·Seq·로그가 그대로다. sink가 null이면 이벤트를 만들지 않는다(AI 탐색).
        /// 좌석: Answer는 그 선택의 Seat(가상 응답자 값 포함), Anytime은 0~SeatCount−1, Timeout·HostOp는 SeatId.System(세션).
        /// </summary>
        public static SubmitResult Submit(GameState s, Ctx ctx, int seat, Command cmd, IEventSink sink)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (s.Viewer != SeatId.HostViewer) throw new InvalidOperationException("보기 사본(ViewFor)에는 Submit하지 않는다 — 호스트 원본만");
            if (cmd == null) return SubmitResult.Rejected(RejectReason.BadCommand);
            var r = Dispatch(s, ctx, seat, cmd, sink);
            if (r.Accepted) TrainerRules.Refresh(s, ctx); // 트레이너 카드의 최대 체력 보정을 다시 센다(주인·카드·등급이 바뀌었을 수 있다 — rulings A32)
            return r;
        }

        static SubmitResult Dispatch(GameState s, Ctx ctx, int seat, Command cmd, IEventSink sink)
        {
            switch (cmd.Kind)
            {
                case CommandKind.HostOp:
                    // 엔진 상태는 그대로, 로그에 줄만 남는다(02 §6-2). 이벤트·Seq 없음.
                    if (seat != SeatId.System) return SubmitResult.Rejected(RejectReason.NotYourChoice);
                    s.CommandCount++;
                    return SubmitResult.Ok;
                case CommandKind.Anytime:
                    if (s.Phase == Phase.GameOver) return SubmitResult.Rejected(RejectReason.GameOver);
                    return MaintenanceRules.Submit(s, ctx, seat, cmd.Maint, sink);
                case CommandKind.Answer:
                case CommandKind.Timeout:
                    if (s.Phase == Phase.GameOver) return SubmitResult.Rejected(RejectReason.GameOver);
                    return SubmitAnswer(s, ctx, seat, cmd, sink);
                case CommandKind.Propose:
                    if (s.Phase == Phase.GameOver) return SubmitResult.Rejected(RejectReason.GameOver);
                    return TradeRules.SubmitOffer(s, ctx, seat, cmd, sink); // 남의 턴 원격 거래 제안(결정 75 B5 나)
                default:
                    return SubmitResult.Rejected(RejectReason.BadCommand);
            }
        }

        static SubmitResult SubmitAnswer(GameState s, Ctx ctx, int seat, Command cmd, IEventSink sink)
        {
            bool timeout = cmd.Kind == CommandKind.Timeout;
            if (timeout && seat != SeatId.System) return SubmitResult.Rejected(RejectReason.NotYourChoice);
            var c = ChoiceRules.Find(s, cmd.ChoiceId);
            if (c == null) return SubmitResult.Rejected(RejectReason.NoSuchChoice);
            if (c.Answered && (timeout || !UbBlessing.CanRevote(s, c))) return SubmitResult.Rejected(RejectReason.AlreadyAnswered);   // 건너뛰기 다수결만 다 모이기 전에 다시 답한다(T29)
            if (!timeout && c.Seat != seat) return SubmitResult.Rejected(RejectReason.NotYourChoice);
            ChoiceAnswer a;
            if (timeout) a = c.Default.Clone();
            else if (cmd.Answer == null) return SubmitResult.Rejected(RejectReason.BadCommand);
            else a = cmd.Answer.Clone();
            if (!ChoiceRules.IsLegal(s, ctx, c, a))
            {
                if (timeout) throw new InvalidOperationException($"선택 {c.Id} {c.Kind}: Default {a}가 Legal 밖이다(01 §3-3 「늘 합법」)");
                return SubmitResult.Rejected(RejectReason.Illegal);
            }

            var fx = new Emitter(s, sink);
            var auto = timeout ? AutoKind.Timeout : cmd.ByAi ? AutoKind.Ai : AutoKind.Human;
            fx.Begin(c.Seat, ChoiceRules.ActionKindOf(c.Kind, a.Kind), auto,
                ChoiceRules.Continues(c.Kind) ? c.FromSeq : -1,
                c.Kind == ChoiceKind.Reaction ? c.FromSeq : -1);
            Answer(s, ctx, fx, c, a, auto, cmd.ByAi ? cmd.Reason : null);
            Advance(s, ctx, fx);
            StampRules.AfterCommand(s, ctx, fx); // 여행 스탬프의 상태 조건(규토리·지닌물건·운명, rulings A30)
            TrainerRules.ScoopAfterCommand(s, ctx, fx); // interviewers B: 맵 행동을 다시 연 뒤에 받은 스탬프(rulings A38)
            TrainerRules.UnlockAfterCommand(s, ctx, fx); // 미뤄 둔 「When unlocked」 물음(rulings A40)
            s.CommandCount++;
            return SubmitResult.Ok;
        }

        /// <summary>
        /// 답 받기. 묶음(GroupId)에 다른 선택이 있으면 받아 두고(비공개면 SecretAnswered, 아니면 ChoiceAnswered), 다 답하면 적용 순서대로
        /// 공개(SecretRevealed)하고 적용한다. 혼자면 바로 적용. 반응 창의 선택은 Reactions가 적용한다.
        /// </summary>
        static void Answer(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a, AutoKind auto, AiReason reason)
        {
            bool timeout = auto == AutoKind.Timeout;
            var members = new List<PendingChoice>();
            foreach (var m in s.Pending)
                if (m.GroupId == c.GroupId) members.Add(m);

            if (c.Secret)
            {
                if (fx.On) fx.Add(new SecretAnswered { Group = c.GroupId, ByTimeout = timeout, Window = c.Window });
            }
            else if (fx.On) fx.Add(new ChoiceAnswered { Id = c.Id, Answer = a.Clone(), ByTimeout = timeout, ByAi = auto == AutoKind.Ai, Reason = reason });

            c.Answered = true;
            c.Answer.CopyFrom(a);
            c.AnswerAuto = auto;
            foreach (var m in members)
                if (!m.Answered) return; // 묶음의 나머지를 기다린다

            foreach (var m in members) s.Pending.Remove(m);
            var win = c.Kind == ChoiceKind.Reaction && c.Window != Window.None ? Reactions.OfGroup(s, c.GroupId) : null;
            if (win != null && members.Count > 1)
            {
                Reactions.ResolveGroup(s, ctx, fx, win, members);
                return;
            }
            if (c.Secret && fx.On)
            {
                var answers = new SeatAnswer[members.Count];
                for (int i = 0; i < members.Count; i++) answers[i] = new SeatAnswer { Seat = members[i].Seat, Value = members[i].Answer.Clone() };
                fx.Add(new SecretRevealed { Group = c.GroupId, Answers = answers, Window = c.Window });
            }
            if (win != null)
            {
                Reactions.Answered(s, ctx, fx, c, a);
                return;
            }
            if (SetupFlow.IsPickGroup(members))
            {
                SetupFlow.ApplyPicks(s, ctx, fx, members); // 스타터·트레이너 카드: 묶음을 한꺼번에(TrainerCardsRevealed)
                return;
            }
            if (FractureRules.IsGroup(members))
            {
                FractureRules.ApplyPicks(s, ctx, fx, members); // 천상의 균열 추종자: 한꺼번에 공개 → 집계 → 강림(rulings A31)
                return;
            }
            if (UbBlessing.IsVote(members))
            {
                UbBlessing.Tally(s, ctx, fx, members); // 아르세우스의 축복 동의: 하나라도 막으면 쓰지 않는다(결정 84 U1)
                return;
            }
            if (UbBlessing.IsSkipVote(members))
            {
                UbBlessing.TallySkip(s, ctx, fx, members); // 라운드 끝 「UB 턴을 건너뛰겠습니까?」: 탈락하지 않은 좌석의 다수결(결정 87 R3)
                return;
            }
            if (BreachRules.IsGroup(members))
            {
                BreachRules.ApplyPicks(s, ctx, fx, members); // 균열 카드 붙이기 · UB 격파 뒤 버리기·옮기기: 좌석 순서로 적용하고 흐름을 잇는다(rulings A52)
                return;
            }
            foreach (var m in members) Apply(s, ctx, fx, m, m.Answer); // 연 순서(= 좌석 순서로 연다)
        }

        /// <summary>
        /// 답 적용(종류별). 선택은 닫혔고 답 이벤트가 나갔다. 그 선택을 연 기능이 여기에 적용을 잇는다 —
        /// 셋업·맵·찾아보기·숨겨진 장소(M3-d), 배틀·우선권 토큰·도전 응답·배틀 뒤 보상(M3-e BattleFlow), 그 밖 보상·천상의 균열·종막(M5), 균열 카드(M6).
        /// 스타터·트레이너 카드 고르기는 묶음으로 Answer가 SetupFlow.ApplyPicks에 넘긴다.
        /// </summary>
        static void Apply(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            // 효과 프레임이 연 선택(op Ask · 처리기, 01 §7-2 Wait): 답을 프레임에 넣고 Advance가 이어 돈다
            if (EffectRules.Waiting(s))
            {
                EffectRules.Answered(s, a);
                return;
            }
            switch (c.Kind)
            {
                case ChoiceKind.VillagePick:
                    SetupFlow.ApplyVillage(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.MapAction:
                    MapActions.Apply(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.HiddenSpot:
                    MapActions.ApplyHiddenSpot(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.EncounterPick:
                    MapActions.ApplyEncounterPick(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.Lineup:
                    BattleFlow.ApplyLineup(s, ctx, fx, c, a); // 간부·태홍 명단(A16, M5 「막 뼈대」)
                    break;
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                case ChoiceKind.SwitchWindow:
                case ChoiceKind.BattleAction:
                    BattleFlow.ApplyChoice(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.Reaction:
                    if (c.Sub == ChoiceSub.InitiativeToken) BattleFlow.ApplyToken(s, ctx, fx, c, a);
                    else if (c.Sub == ChoiceSub.EffectOrder) EffectRules.ApplyOrder(s, ctx, a); // 결정 72: 늘어놓은 면 바꾸기는 Advance가 돌고 배틀을 잇는다
                    else if (c.Sub == ChoiceSub.AlluringStew) CraftRules.ApplyAlluring(s, ctx, fx, c, a); // 매혹적인 스튜: 쓰면 심볼 하나 → 일반 타격, 멈춘 자리를 잇는다
                    else if (c.Source.Kind == SourceKind.TrainerCard && s.Battle == null) TrainerRules.MapAnswered(s, ctx, fx, c, a); // 배틀 밖 트레이너 카드 능력의 물음(rulings A37)
                    break;
                case ChoiceKind.Respond:
                    if (c.Sub == ChoiceSub.Challenge) BattleFlow.ApplyRespond(s, ctx, fx, c, a);
                    else if (c.Sub == ChoiceSub.Trade) TradeRules.ApplyRespond(s, ctx, fx, c, a); // 거래 응답(rulings A29)
                    break;
                case ChoiceKind.Reward:
                    if (c.Window == Window.BattleEnd) BattleFlow.ApplyReward(s, ctx, fx, c, a); // 배틀 뒤 보상. 그 밖 보상(공유 축복 …)은 M5
                    else if (c.Sub == ChoiceSub.EvolveBranch || c.Sub == ChoiceSub.EvolveTeach) EvolveRules.ApplyReward(s, ctx, fx, c, a); // 진화 분기 · 옛 기술 가르치기(R14, p.5)
                    else if (c.Sub == ChoiceSub.FractureTie) FractureRules.ApplyTie(s, ctx, fx, c, a); // 천상의 균열 동률(A31)
                    else if (c.Source.Kind == SourceKind.TrainerCard) TrainerRules.MapAnswered(s, ctx, fx, c, a); // scientist B의 기술 가르치기(rulings A37)
                    else if (c.Sub == ChoiceSub.Revive) UbBlessing.ApplyRevive(s, ctx, fx, c, a); // 모두 탈락 뒤 부활할 좌석(울트라 버스트, A54)
                    else if (c.Source.Kind == SourceKind.ShrineEvent) ShrineRules.ApplyReward(s, ctx, fx, c, a); // 사당 카드의 고르기 · 나눌 좌석(A19)
                    break;
                case ChoiceKind.Ending:
                    HosFlow.ApplyEnding(s, ctx, fx, c, a);
                    break;
                case ChoiceKind.Discard:
                    FateRules.ApplyDiscard(s, ctx, fx, c, a); // 운명 손패 6장째(A12)
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 진행 루프(01 §3-1 ③): 열린 선택이 없으면 Stack 맨 위 효과나 지금 자동 단계를 한 걸음 돈다를 되풀이한다.
        /// 셋업·턴 넘김·라운드(M3-d)는 답을 적용하는 코드가 다음 자동 단계(새 Seq)를 바로 부른다. 배틀(M3-e)도 같다 — 답 적용과 반응 창 닫힘이
        /// BattleFlow.Run을 부른다. 효과 단계(Stack, M3-f EffectRules): 배틀 진행 루프가 단계마다 먼저 돈다. 여기서는 답이 효과 프레임의 선택을 채운 뒤
        /// (op Ask · 처리기, M5 효과 기반 — Apply의 EffectRules.Answered) 남은 프레임을 마저 돌고 배틀을 잇는다.
        /// </summary>
        static void Advance(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Pending.Count > 0 || s.Stack.Count == 0) return;
            if (EffectRules.Run(s, ctx, fx) && s.Battle != null) BattleFlow.Run(s, ctx, fx);
        }

        /// <summary>
        /// 셋업을 시작한다(01 §3-5, SetupFlow): 트레이너 카드 배분과 첫 고향 마을 고르기를 연다. NewGame 바로 뒤 한 번만 부른다 —
        /// 명령이 아니라 새 게임의 일부라서 로그(CommandCount)에 남지 않고, 이어하기·리플레이도 NewGame + Start 뒤에 명령을 다시 적용한다.
        /// </summary>
        public static void Start(GameState s, Ctx ctx, IEventSink sink)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (s.Viewer != SeatId.HostViewer) throw new InvalidOperationException("보기 사본(ViewFor)에서는 시작하지 않는다");
            if (s.Phase != Phase.Setup || s.NextSeq != 0 || s.Pending.Count > 0) throw new InvalidOperationException("Start는 NewGame 바로 뒤에 한 번만");
            SetupFlow.Begin(s, ctx, new Emitter(s, sink));
        }

        /// <summary>그 선택의 합법 답(01 §9-2, ChoiceRules.Legal).</summary>
        public static void Legal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into) => ChoiceRules.Legal(s, ctx, c, into);

        /// <summary>
        /// 좌석 보기(01 §4): GameState 사본. 숨은 칸은 Unknown(−1)으로 바꾸고 개수만 남긴다. 거르는 곳은 여기와 FilterFor뿐이다.
        /// 숨는 것: 남의 운명 손패·트레이너 카드 후보, 모든 덱의 순서와 내용(버림 더미는 공개 · 전설 덱은 순서만), 뒷면 숨겨진 장소의 정체, Rng 상태,
        /// 남의 비공개 선택(트레이너 카드 고르기·버리기·비공개 묶음)의 후보·Default·받아 둔 답. viewer = SeatId.Observer면 모든 좌석이 남이다.
        /// 추종자(SeatState.Follower = 공개된 값)는 늘 보이고, 천상의 균열의 비밀 선택 동안만 남의 것을 가린다(FractureRules.HideFollowers, rulings A31 ①②).
        /// </summary>
        public static GameState ViewFor(GameState s, int viewer)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Viewer != SeatId.HostViewer) throw new InvalidOperationException("보기는 호스트 원본에서 만든다");
            var v = s.Clone();
            v.Viewer = viewer;
            if (viewer == SeatId.HostViewer) return v;
            for (int i = 0; i < v.Seats.Length; i++)
            {
                if (i == viewer) continue;
                Hide(v.Seats[i].FateHand);
                Hide(v.Seats[i].TrainerCardOffer);
            }
            FractureRules.HideFollowers(s, v, viewer);
            foreach (var pile in v.Decks) Hide(pile);
            // 전설 덱은 내용이 공개 정보다(처음 = 데이터의 전설 전부, 잡히거나 쓰러뜨린 전설만 빠진다 — p.18 「removed from the game once it is defeated or captured」):
            // 순서만 가린다(카드 색인 오름차순). 보기의 맵 행동 Legal(전설 장소 · 사당 전설 · 신 찾아보기 — LegendRules.SiteCard·TokenAt)이 호스트와 같아야 한다
            var legend = v.Decks[(int)DeckId.Legendary];
            legend.Clear();
            legend.AddRange(s.Decks[(int)DeckId.Legendary]);
            legend.Sort();
            // 은행상회 덱은 앞면이라 맨 위 거래 카드 1장은 모두가 본다(rulings A4)
            var ginkgo = s.Decks[(int)DeckId.GinkgoTrade];
            if (ginkgo.Count > 0) v.Decks[(int)DeckId.GinkgoTrade][ginkgo.Count - 1] = ginkgo[ginkgo.Count - 1];
            foreach (var t in v.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp) t.Def = Unknown;
            v.Held.Clear(); // 미뤄 둔 선택은 열릴 때 ChoiceRequested로 알린다
            v.Rng.State = 0;
            v.Rng.Increment = 0;
            foreach (var c in v.Pending)
            {
                if (c.Seat == viewer || !ChoiceRules.HidesFromOthers(c) || (c.Sub == ChoiceSub.Trade && c.Subject == viewer)) continue; // 거래 응답은 제안한 좌석도 본다
                Hide(c.Options);
                HideAnswer(c.Default);
                HideAnswer(c.Answer);
            }
            return v;
        }

        /// <summary>보기에서 가린 값.</summary>
        public const int Unknown = -1;

        static void Hide(List<int> list)
        {
            for (int i = 0; i < list.Count; i++) list[i] = Unknown;
        }

        static void HideAnswer(ChoiceAnswer a)
        {
            a.Kind = AnswerKind.Decline;
            a.Target = Unknown;
            a.Arg = Unknown;
            Hide(a.Values);
        }

        /// <summary>
        /// 그 좌석에 보낼 이벤트(01 §3-6 ⑩). 남의 카드 키는 null(장수는 남김), Legal은 그 좌석에만. 비공개 답은 애초에 SecretAnswered(답 없음)로 낸다.
        /// 개수·종류는 숨은 정보에 따라 달라지지 않는다. null이면 그 좌석에 보내지 않는다(지금은 없음). 호스트 원본(HostViewer)은 그대로.
        /// </summary>
        public static GameEvent FilterFor(GameEvent e, int viewer)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return viewer == SeatId.HostViewer ? e : e.ForViewer(viewer);
        }
    }
}
