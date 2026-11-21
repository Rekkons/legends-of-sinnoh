using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 화면이 이벤트를 04 §8-8 행으로 고르는 표(행 × 10 + 단계). 방장 쪽 <c>PlayTiming.RowOf</c>와 따로 적는다 — 같은 함수로 길이를 세어도 분류가 어긋나면
    /// 화면과 방장 멈춤이 달라지므로(04 §8-1 P5, isekai IS04 §8.11) 두 표를 교차 시험 M4_연출길이방장화면같음이 견준다. 한쪽만 고치면 그 시험이 깨진다.
    /// </summary>
    public static class FxRows
    {
        public const int None = 0;
        public const int Unknown = -1;
        /// <summary>101행: 턴 결과 요약 · 넘김 띠 · 내 차례 컷신(차례 좌석만, 띠 뒤). 이벤트가 아니라 TurnEnded → TurnStarted 흐름에서 나온다.</summary>
        public const int TurnSummary = 1010, Handoff = 1011, MyTurn = 1012;
        /// <summary>배틀 인트로(53행)와 출전(66행).</summary>
        public const int Intro = 530, SendIn = 660;

        static readonly Dictionary<Type, Func<GameEvent, int>> Table = Build();

        static Dictionary<Type, Func<GameEvent, int>> Build()
        {
            var t = new Dictionary<Type, Func<GameEvent, int>>();
            void Row<T>(int row) where T : GameEvent => t.Add(typeof(T), e => row);
            void Pick<T>(Func<T, int> pick) where T : GameEvent => t.Add(typeof(T), e => pick((T)e));

            // 셋업·막
            Row<TrainerCardsDealt>(10);
            Pick<SecretAnswered>(e => e.Window == Window.None ? 11 : None);   // 반응 창 묶음의 답은 창의 일부
            Row<TrainerCardsRevealed>(12);
            Row<VillagePicked>(20);
            Row<StarterPicked>(30);
            Row<ActRevealed>(40);
            Row<TokensPlaced>(50);
            Row<ActGoalMet>(940);
            Row<ShardGained>(940);   // 94행 2막: 조각·파티 칸(결과 창의 줄 — 막 목표와 같은 박자)
            Row<PartySlotUnlocked>(940);
            Row<HeartRestored>(FinaleRows.Heart);
            Row<EndingChosen>(FinaleRows.Ending);
            Row<TraitorOrderSet>(FinaleRows.Order);
            Pick<GameEnded>(e => e.Reason == GameEndReason.BreachLost ? UbRows.Lost : FinaleRows.Result);   // 86행 균열 70% 패배는 보드 덮개(M6 「UB 화면 묶음 1」)
            Pick<SecretRevealed>(e => e.Window == Window.None ? 990 : None);
            // 보드
            Row<TurnStarted>(60);
            Row<Healed>(80);
            Row<ProtectionCleared>(90);
            Row<Moved>(110);
            Row<Flew>(160);
            Row<GinkgoTraded>(WorkRows.Bank);
            Row<Crafted>(WorkRows.Craft);
            Row<StewEaten>(WorkRows.Stew);
            Row<Evolved>(WorkRows.Evolve);
            Row<ItemHeld>(WorkRows.Maint);        // 38·40·41행 정비: 남 화면만 정비 판 보기(결정 120)
            Row<MoveTaught>(WorkRows.Maint);
            Row<MoveForgotten>(WorkRows.Maint);
            Row<Released>(WorkRows.Maint);
            Row<PartyReordered>(WorkRows.Maint);
            Row<PokemonExchanged>(WorkRows.Maint);   // 사당의 그다음 고르기 결과(동등한 교환) · 뜻밖의 선물의 규토리: 남 화면에 그 좌석의 판 보기(결정 120)
            Pick<ApricornsGained>(e => e.Source.Kind == SourceKind.ShrineEvent ? WorkRows.Maint : None);
            Row<ApricornsTaken>(190);
            Row<Searched>(200);
            Row<EncounterDrawn>(211);
            Row<EncounterPicked>(212);
            Row<HiddenSpotRevealed>(250);
            Row<HiddenSpotPassed>(250);
            // M5 「맵 결과·배틀 안 연출」(26~30 · 46 · 78~80행 — ResultStaging.cs)
            Pick<SpotResolved>(e => ResultRows.Of(e.Result));
            Row<SpoilsTaken>(ResultRows.Spoils);
            Pick<FormChanged>(e => !e.Temporary ? None : e.Source.Kind == SourceKind.Rule && e.Source.Id == (int)RuleSource.CelestialAura ? ResultRows.GodPhase : ResultRows.Form);
            Pick<TokenRemoved>(e => e.TokenKind == TokenKind.Grunt ? LiberateBeats.Row : e.TokenKind == TokenKind.God && e.Source.Kind != SourceKind.ShrineEvent ? ResultRows.GodGone : None);   // 1막 해방의 조무래기(94행 941) ·   // 균열이 바꿔 놓은 신은 격파가 아니다(강림 341의 DescendBeats.OldShown이 그린다)
            Row<ProtectionSet>(460);
            Row<Whiteout>(710);
            // 배틀 들어가기
            Row<BattleStarted>(220);
            Row<ChallengeIssued>(DealRows.Vs);
            // 스탬프·거래(47·52행 — M5 「스탬프·도전·거래 화면」)
            Row<StampGained>(DealRows.Stamp);
            Row<TradeDone>(DealRows.Trade);
            Row<GalacticBattle>(720);
            Row<InitiativeTokenAt>(Intro);
            Row<SentIn>(SendIn);
            Row<SwitchedOut>(SendIn);
            // 배틀
            Row<OrderDecided>(540);
            Row<DiceRolled>(560);
            Pick<DiceFacesChanged>(e => e.Source.Kind == SourceKind.Fate ? FateRows.Reroll : e.Source.Kind == SourceKind.Status ? 630 : 570);
            Row<MoveUsed>(590);
            Row<TypeMatchup>(600);
            Row<DamageBreakdown>(611);
            Pick<HpChanged>(e => e.Source.Kind == SourceKind.Fate ? FateRows.Sacrifice : e.Source.Kind == SourceKind.Rule && e.Source.Id == (int)RuleSource.Fatigue ? 642 : 612);
            // 운명의 변화(7·14·42~45행 — M5 「운명·응답 화면」)
            Pick<FateDealt>(e => e.Source.Kind != SourceKind.Rule ? FateRows.Draw : e.Source.Id == (int)RuleSource.FallenStar ? FateRows.Star : e.Source.Id == (int)RuleSource.Respawn ? 80 : FateRows.Draw);   // 귀환(8행)의 운명 1장은 회복 단계 끝에
            Row<FateDiscarded>(FateRows.Discard);
            Row<FatePlayed>(FateRows.Play);
            Row<Teleported>(FateRows.Teleport);
            // 울트라 버스트 보드(15·82·83행 — M6 「UB 보드 화면」, UbStaging.cs)
            Row<GateTravel>(UbRows.Gate);
            Row<UbMoved>(UbRows.Move);
            Row<CellDestroyed>(UbRows.Destroy);
            Row<TokenMoved>(UbRows.Fly);   // 83행 토큰 날기(M6 「연출 목록 채우기」)
            // M6 「UB 화면 묶음 1」(82행 예비 · 84 · 85행 — UbStaging.cs)
            Row<UbTurnStarted>(UbRows.Start);
            Row<NecrozmaStrike>(UbRows.Strike);
            Row<BreachBracket>(UbRows.Bracket);
            Row<BreachCardsDealt>(UbRows.Bracket);
            // M6 「UB 배틀 화면 묶음」(88~92행 — UbBattleStaging.cs)
            Row<PrismReduced>(UbBattleRows.Prism);
            Row<PrismMoved>(UbBattleRows.PrismMove);
            Row<OnslaughtDrawn>(UbBattleRows.Onslaught);
            Row<NecrozmaPhase>(UbBattleRows.Phase);
            Row<CoinFlip>(UbBattleRows.Coin);
            Row<TrialProgress>(UbBattleRows.Trial);
            Row<GuardianDown>(UbBattleRows.Guardian);
            Pick<BlessingGained>(e => UbBattleRows.BySilvally(e) ? UbBattleRows.Silvally : None);   // 93행 실버디 → 축복 +1(M6 「연출 목록 채우기」)
            Row<Eliminated>(FallRows.Row);   // 91행 탈락·부활(M6 「UB 화면 묶음 2」)
            Row<Revived>(FallRows.Row);
            // 트레이너 카드(35·48·49·50행 — M6 「트레이너 카드 해금 연출」, CardStaging.cs)
            Row<WeatherSet>(CardRows.Weather);
            Row<CardUnlocked>(CardRows.Unlock);
            Row<TrainerCardSwapped>(CardRows.Swap);
            Row<AbilityUsed>(CardRows.Ability);
            // 사당·균열(31~34행 — M5 「사당·균열 화면」)
            Row<ShrineOffered>(ShrineRows.Offer);
            Row<ShrineCardsDrawn>(ShrineRows.Offer);
            Row<ShrineBlessing>(ShrineRows.Blessing);
            Row<LegendEvent>(ShrineRows.Legend);
            Row<FollowerTally>(ShrineRows.Tally);
            Row<GodDescended>(ShrineRows.Descend);
            Row<StatusApplied>(620);
            Row<StatusTicked>(630);
            // 굴림·포획·교체로 바뀌는 피로 칸은 그 행(56·68·66)의 박자에 든다
            Pick<FatigueChanged>(e => e.Cause == FatigueCause.Roll || e.Cause == FatigueCause.Capture || e.Cause == FatigueCause.Switch ? None : 641);
            Row<Fainted>(650);
            Row<Fled>(670);
            Row<Forfeited>(670);
            Row<CaptureAttempt>(680);
            Row<PokemonGained>(690);
            Row<BattleEnded>(700);
            Pick<ChoiceAnswered>(e => e.ByAi && e.Seat <= SeatId.Galactic ? 730 : None);   // 가상 응답자 AI 자막
            Row<RetributionStored>(741);
            Row<RetributionMoved>(742);
            Row<SwitchLocked>(750);
            // 대기열에 단계가 없는 것: 창·미리보기·정비·라운드 띠, 다른 행의 일부
            foreach (var none in new[]
            {
                typeof(ActionStarted), typeof(ChoiceRequested), typeof(ChoiceReissued), typeof(WindowSkipped), typeof(RoundStarted), typeof(TurnEnded),
                typeof(MoveSelected),
                typeof(ChallengeAnswered), typeof(CardsReturned), typeof(PileShuffled), typeof(FlyBlocked), typeof(MoveAllowanceSet), typeof(RespawnCleared),
                typeof(RespawnPending), typeof(UsageMarked), typeof(ApricornSpent),
                typeof(TradeProposed), typeof(TradeAnswered), // 거래 제안·응답: 창(98행)과 기록 줄만
                typeof(BreachChanged), // 울트라 버스트: 균열 바뀜은 앞의 파괴 단계(830)가 그린다(UbGauge.Shown)
                typeof(BreachCardAttached), typeof(BreachCardDiscarded), typeof(BreachCardMoved),
                typeof(BlessingUsed), typeof(UbTurnSkipped),
            })
                t.Add(none, e => None);
            return t;
        }

        /// <summary>그 이벤트가 화면에서 닿는 행. 단계가 없으면 <see cref="None"/>, 표에 없는 타입이면 <see cref="Unknown"/>.</summary>
        public static int Of(GameEvent e) => e != null && Table.TryGetValue(e.GetType(), out var pick) ? pick(e) : Unknown;

        /// <summary>
        /// 턴 결과 요약(101행)에 적을 변화: 주운 규토리 · 잡은 포켓몬 · 배틀로 받은 여행 점수·규토리(도망친 야생이 남긴 것 포함) · 막 목표 달성.
        /// 방장 쪽 <c>PlayTiming.IsTurnGain</c>과 따로 적는다(한 좌석 판은 이것으로 요약 단계가 서고 빠진다).
        /// </summary>
        public static bool TurnGain(GameEvent e)
        {
            switch (e)
            {
                case ApricornsTaken a: return a.Count > 0;
                case PokemonGained _: return true;
                case BattleEnded b: return b.Changes != null && b.Changes.Length > 0;
                case Fled f: return f.Changes != null && f.Changes.Length > 0;
                case ActGoalMet _: return true;
                default: return false;
            }
        }

        /// <summary>전원 차단 컷신(04 §8-1 P2 until-end 전원).</summary>
        public static bool BlocksAll(int row) => row == 40 || row == FinaleRows.Ending || row == FinaleRows.Result || ShrineRows.BlocksAll(row) || row == UbRows.Lost;

        /// <summary>당사자 차단 컷신(until-end 당사자): 조우 전환·도전·갤럭시단 배틀.</summary>
        public static bool BlocksParty(int row) => row == 220 || row == 510 || row == 720;

        /// <summary>이벤트마다 단계가 하나씩 이어지는 행(걸음, 면 바꾸기). 나머지는 한 항목에 행마다 단계 하나(같은 박자).</summary>
        public static bool Chains(int row) => row == 110 || row == 570 || row == DealRows.Stamp || row == UbRows.Move || row == UbRows.Destroy || row == UbRows.Fly;   // 스탬프는 하나씩(보상이 다음 스탬프를 잇달아 부르기도 한다) · UB 이동·파괴도 하나씩(밟은 UB가 옮긴 뒤에 칸이 부서진다)

        /// <summary>야생 취급 배틀(전설·우두머리·무리·UB 포함): 당사자 좌석의 대기열에만 들어간다(04 §8-2, 결정 22 Q6).</summary>
        public static bool WildLike(BattleKind k)
        {
            switch (k)
            {
                // 가운데 필드가 모두 바뀌는 배틀은 모든 좌석이 본다
                case BattleKind.Trainer:
                case BattleKind.Galactic:
                case BattleKind.Traitor:
                    return false;
                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// 좌석 하나의 화면 대기열 짜기(04 §8-2): 받은 갱신(그 좌석에 걸러진 이벤트 열)을 항목(<c>Seq</c> 하나)으로 나누고 단계·표시 값 단서를 놓는다.
    /// 항목 길이는 방장과 같은 함수 <c>PlayTiming.Items</c>가 센다(P5). 배틀 맥락(남의 야생전 건너뛰기)은 갱신 사이에 이어지므로 좌석마다 하나를 들고 쓴다.
    /// </summary>
    public sealed class FxPlanner
    {
        public readonly int Viewer;
        readonly PlayTimingContext timing;
        bool skippingBattle, turnGain;
        int maintSeen;   // 이번 턴에 정비 보기를 받은 좌석(PlayTimingContext.MaintSeen과 같은 꼴 — 같은 턴의 정비는 한 번)
        int seats;

        /// <param name="seats">판의 좌석 수(턴 넘김 101행 — 한 좌석 판은 짧다, 결정 57 ③). 0 = 모름(여럿으로 짠다). <see cref="From"/>이 보기에서 다시 받는다.</param>
        public FxPlanner(int viewer, int seats = 0)
        {
            Viewer = viewer;
            this.seats = seats;
            timing = new PlayTimingContext(viewer, seats);
        }

        /// <summary>남의 야생전도 대기열에 넣는다(방 판 — 결정 112 Q19, <see cref="PlayTimingContext.WatchWild"/>). 기본 끔.</summary>
        public bool WatchWild
        {
            get => timing.WatchWild;
            set => timing.WatchWild = value;
        }

        /// <summary>보기에서 배틀 맥락을 다시 세운다(이어하기·재접속 — 첫 갱신을 받기 전에).</summary>
        public void From(GameState view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            timing.From(view);
            seats = view.SeatCount;
            turnGain = false;
            maintSeen = 0;
            var b = view.Battle;
            skippingBattle = b != null && !WatchWild && FxRows.WildLike(b.Kind) && b.Sides[0].Responder != Viewer && b.Sides[1].Responder != Viewer;
        }

        /// <summary>갱신 하나 → 항목 목록(Seq마다 하나, 길이 0인 항목 포함 — 표시 값 단서와 기록 줄이 거기 달린다).</summary>
        public List<FxEntry> Plan(IReadOnlyList<GameEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var entries = new List<FxEntry>();
            var anchors = new List<KeyValuePair<GameEvent, FxStep>>();   // 표시 값이 든 이벤트 → 그 값이 착지하는 단계(없으면 null = 항목 시작)
            FxEntry cur = null;
            FxStep last = null;
            bool sawTurnEnd = false, battleStart = false;

            void Close()
            {
                if (cur == null) return;
                Finish(cur, battleStart, anchors);
                entries.Add(cur);
                anchors.Clear();
                cur = null;
                last = null;
                battleStart = false;
            }

            FxStep Append(int row, FxLayer layer, int ms, GameEvent e)
            {
                var step = new FxStep { Row = row, Layer = layer, Ms = ms };
                if (e != null) step.Events.Add(e);
                cur.Steps.Add(step);
                return last = step;
            }

            foreach (var e in events)
            {
                if (e == null) continue;
                if (e is ActionStarted a)
                {
                    Close();
                    cur = new FxEntry { Seq = a.Seq, Seat = a.Seat, Kind = a.Kind, Auto = a.Auto, Continues = Math.Max(-1, a.Continues), ReactsTo = Math.Max(-1, a.ReactsTo) };
                    cur.Events.Add(a);
                    continue;
                }
                if (cur == null) cur = new FxEntry { Seq = e.Seq, Seat = e.Seat, Kind = ActionKind.Setup };
                cur.Events.Add(e);

                // 남의 야생전은 내 대기열에 넣지 않는다(알림 칩·기록 줄만): 시작부터 끝까지 단계 없음
                if (e is BattleStarted bs)
                {
                    skippingBattle = FxRows.WildLike(bs.Kind) && !InParty(bs) && !WatchWild;
                    if (skippingBattle) continue;
                }
                if (skippingBattle)
                {
                    if (e is BattleEnded) skippingBattle = false;
                    continue;
                }

                if (e is BattleStarted || e is GalacticBattle) battleStart = true;
                if (FxRows.TurnGain(e)) turnGain = true;
                if (e is TurnEnded) sawTurnEnd = true;
                if (e is TurnStarted ts)
                {
                    if (!ts.IsExtra)
                    {
                        // ② 띠: 턴이 넘어가면 요약 → 넘김 띠, 차례 좌석에는 이어서 내 차례 컷신(게임 첫 턴은 컷신만, 추가 턴은 넘김 없이).
                        // 한 좌석 판(결정 57 ③)은 띠·컷신이 짧고, 요약은 그 턴에 변화가 있었을 때만 선다
                        if (sawTurnEnd)
                        {
                            if (PlayTiming.ShowsTurnSummary(seats, turnGain)) Append(FxRows.TurnSummary, FxLayer.Band, PlayTiming.TurnSummaryMs.Value, e);
                            Append(FxRows.Handoff, FxLayer.Band, PlayTiming.HandoffFor(seats), e);
                        }
                        if (ts.Seat == Viewer) Append(FxRows.MyTurn, FxLayer.Band, PlayTiming.MyTurnFor(seats), e);
                        sawTurnEnd = false;
                    }
                    turnGain = false;
                    maintSeen = 0;
                }

                int row = FxRows.Of(e);
                if (row == FxRows.Unknown) throw new InvalidOperationException($"FxRows: {e.GetType().Name}의 행이 없다 — 04 §8-8 행과 PlayTiming 표를 함께 정한다");
                int rowMs = row > FxRows.None ? PlayTiming.Bundled(row, e, PlayTiming.RowMsFor(e, row, Viewer), ref maintSeen) : 0;
                if (row == WorkRows.Maint && rowMs == 0) row = FxRows.None;   // 내 정비 · 같은 턴의 다음 정비: 단계 없음(기록 줄만)
                if (row != FxRows.None)
                {
                    int ms = rowMs;
                    if (FxRows.BlocksAll(row) || FxRows.BlocksParty(row))
                    {
                        // ① 차단 컷신: 범위 안이면 끝까지 막는 컷신, 범위 밖이면 보드 위 장면(③)으로 보거나(트레이너·갤럭시단 배틀, 도전) 보지 않는다
                        if (FxRows.BlocksAll(row) || InParty(e)) Append(row, FxLayer.Block, ms, e);
                        else if (!(e is BattleStarted b) || !FxRows.WildLike(b.Kind)) Append(row, FxLayer.Item, ms, e);
                    }
                    else if (FxRows.Chains(row)) Append(row, FxLayer.Item, ms, e);
                    else
                    {
                        var step = cur.Step(row);
                        if (step == null) step = Append(row, FxLayer.Item, ms, e);
                        else
                        {
                            step.Ms = Math.Max(step.Ms, ms);
                            step.Events.Add(e);
                            last = step;
                        }
                    }
                }
                anchors.Add(new KeyValuePair<GameEvent, FxStep>(e, last));
            }
            Close();

            // 길이는 방장과 같은 함수로(P5). 길이가 있는 항목만 돌아오므로 Seq 순서로 맞춘다
            var items = PlayTiming.Items(events, timing);
            int at = 0;
            foreach (var entry in entries)
            {
                if (at >= items.Count || items[at].Seq != entry.Seq) continue;
                entry.QueueMs = items[at].QueueMs;
                entry.BlockMs = items[at].BlockMs;
                at++;
            }
            return entries;
        }

        /// <summary>항목을 닫는다: 배틀 인트로 정리 → 단계 시작 시각 → 표시 값 단서.</summary>
        static void Finish(FxEntry entry, bool battleStart, List<KeyValuePair<GameEvent, FxStep>> anchors)
        {
            // 배틀 인트로(53행): 배틀을 연 항목·첫 출전 고르기·우선권 토큰이 놓인 항목의 출전은 인트로 안에서 보인다(66행 박자를 따로 두지 않는다)
            var sendIn = entry.Step(FxRows.SendIn);
            if (sendIn != null && (battleStart || entry.Kind == ActionKind.SendIn || entry.Step(FxRows.Intro) != null))
            {
                var intro = entry.Step(FxRows.Intro);
                if (intro == null)
                {
                    sendIn.Row = FxRows.Intro;
                    sendIn.Ms = PlayTiming.RowMs(null, FxRows.Intro);
                }
                else
                {
                    intro.Events.AddRange(sendIn.Events);
                    entry.Steps.Remove(sendIn);
                    for (int i = 0; i < anchors.Count; i++)
                        if (anchors[i].Value == sendIn) anchors[i] = new KeyValuePair<GameEvent, FxStep>(anchors[i].Key, intro);
                }
            }

            int t = 0;
            foreach (var s in entry.Steps)
            {
                s.StartMs = t;
                t += s.Ms;
            }
            // 표시 값은 그 단계가 끝날 때 착지한다. 포획(68행)만 다르다 — 던진 볼은 손을 떠나는 순간 줄어든다(01 §3-6 ③)
            foreach (var kv in anchors)
                FxShown.AddCues(entry, kv.Key, kv.Value == null ? 0 : kv.Key is CaptureAttempt && kv.Value.Row == CaptureStage.Row ? kv.Value.StartMs + Math.Min(kv.Value.Ms, EnterBeats.ReleaseMs) : kv.Value.EndMs);
        }

        /// <summary>차단 컷신의 당사자: 배틀 = 양쪽 좌석, 도전 = 건 좌석과 받은 좌석, 나머지 = 행위자.</summary>
        bool InParty(GameEvent e)
        {
            switch (e)
            {
                case BattleStarted b:
                    if (b.Sides != null)
                        foreach (var side in b.Sides)
                            if (side != null && side.Seat == Viewer) return true;
                    return false;
                case ChallengeIssued c: return c.From == Viewer || c.To == Viewer;
                default: return e.Seat == Viewer;
            }
        }
    }
}
