using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 봇 플레이아웃(plan M3 완료 조건 1 · M5 조건 3, 01 §12): 좌석 봇이 Legal에서 고르고, 가상 응답자(갤럭시단)는 원칙 AI가 관전자 보기로 답한다. 게임 끝(종막, Phase.GameOver)까지.
    /// 봇은 그 좌석에 보이는 것(자기 선택의 Legal·자기 좌석·라운드)만 읽어서 원본에도 보기에도 같은 답을 낸다 — 세션 판(BotAgent)과 엔진 직접 판(Run)이 같은 정책이다.
    /// 정책 [가정 M3-h] — 고르게 고르면 1막 해방에 닿지 않는다(스타터 체력 3 대 조무래기 7~9 두 마리):
    /// ① 모으기(파티가 열린 칸만큼 차기 전, 또는 라운드 ≤ 2 + 시드 % 4): 갤럭시단 도전을 빼고, 찾아보기가 되면 1/2로 찾아보고 나머지는 Legal에서 고르게.
    ///    야생 배틀에서 파티에 빈칸이 있고 볼이 있으면 1/2로 던진다.
    /// ② 사냥(모은 뒤, 또는 라운드 &gt; 40 + 시드 % 4): 고향 마을에서 다친 포켓몬이 있으면 마을 회복, 아니면 3/4로 갤럭시단 도전. 밖이면 3/4로 집 쪽
    ///    (공중날기 → 고향 / 고향까지 걸음이 줄어드는 이동).
    /// ③ 배틀 기술 선택은 3/4로 p.25 원칙 기술(01 §9-6, 시간 초과 기본값과 같은 함수), 나머지는 Legal에서 고르게.
    /// ④ 1막을 끝낸 뒤(M5 「막 뼈대」, [가정] §5 #79): 사냥 때 목표 칸(조각이 없는 가장 가까운 호수 → 조각 셋이면 시작의 사당 → 심장이 있으면 신오신전)으로 걸음이 줄어드는
    ///    이동·공중날기를 7/8로 고르고, 마을에서 다쳤으면 회복, 목표 칸에서는 3/4로 도전(사당은 늘 공물 — 신 토큰이 막으면 3/4로 신 배틀). 바칠 규토리가 없으면 모으기처럼 돈다.
    ///    종막 선택(영웅·배신자)과 배신자 도전 순서는 Legal에서 고르게.
    /// ⑤ 제작(M5 「제작·은행·진화」 — 제작 대역을 뺐다): 볼을 만들 수 있으면 1/2로 가장 좋은 볼을 만든다(모으기·사냥 공통). 조각 셋을 모으고 심장이 아직 없으면 시작의 사당에 바칠 규토리를
    ///    남기려고 볼을 만들지 않는다. 그 밖의 제작·은행상회·진화는 Legal에서 고르게 고를 때 나온다.
    /// ⑥ 운명(M5 「운명 네 장 효과」): 굴린 뒤 창에서 이득이 분명할 때만 디아루가 1장(공격하면 0인 눈, 맞으면 크리티컬), 펄기아·기라티나·3장은 내지 않는다.
    ///    ⑦의 배틀 밖에서는 손패가 가득(5장)일 때만 낸다.
    /// M5 「봇 종막 대역 0」 — 시험이 대신 이겨 주던 대역(전력: 두 번 이어 지면 상대 명단 체력 1)을 뺐다. 그 전 정책으로 대역을 끄면 간부 2~13% · 태홍 0~1%를 이겼다(plan-log 같은 절):
    /// ⑦ 이겨야 막이 나아가는 배틀(갤럭시단 · 신 · 배신자 연전)은 고르게 고르지 않는다 — 기권하지 않고, 기술은 원칙 값이 가장 큰 것, 출전·다음 포켓몬은 상대 출전 포켓몬에게 원칙 값 × 남은 체력이 가장 큰 것,
    ///    교체 창은 안 함, 우선권 토큰은 쓴다.
    /// ⑧ 키우기: 진화할 수 있으면 먼저 진화(우러난 스튜 반값부터). 기술 가르치기(보상·진화·스탬프 풀)는 다음에 이겨야 할 갤럭시단 카드들에게 낼 원칙 값의 합(타입 상성)이 가장 많이 느는 자리에.
    ///    파티가 셋 이상이면 조우 카드는 3/4로 체력이 가장 큰 것. 파티가 가득이어도 상대가 가장 약한 포켓몬보다 세면 볼을 던지고, 가득 찼을 때는 가장 약한 포켓몬을 놓아준다.
    /// ⑨ 지니기: 엔진 직접 판은 맵 행동 차례 앞에 정비 명령을 낸다 — 가방의 지닌물건·유물을 아무것도 안 지닌 가장 센 포켓몬에게(유물은 그 타입 기술이 있는 포켓몬, 진화의 휘석은 아직 진화할 포켓몬,
    ///    천상의 유물·폼 유물·신속의 띠는 지니지 않는다), 스탬프 기술은 ⑧과 같은 자리에. 1막 뒤 아무것도 안 지닌 포켓몬이 있으면 1/2로 지닌물건을 만든다(결단의 머리띠 → 별의 방패 → 행복의 알 → 폭발수정).
    /// ⑩ 간부·태홍·신에게 가기 전에 파티 체력이 80% 밑이면 회복스튜를 만들거나 고향 마을로 가서 회복한다. 간부·태홍은 파티 세기가 상대 명단의 <see cref="Bot.ReadyRatio"/>배에
    ///    못 미치면 <see cref="Bot.Patience"/>라운드는 모으기처럼 돌며 키우고 <see cref="Bot.Push"/>라운드는 도전하기를 되풀이한다(태홍 승률이 약 8%라 키우기 없이 되풀이하면 끝나지 않는 판이 남았다).
    /// ⑪ 트레이너 카드(M6 「봇 — 트레이너 카드」 — 능력 코드가 봇 판에서 돌게, 이기려는 것이 아니다): 카드의 「쓸까」·고르기는 대개 쓴다 · 맵 행동의 UseAbility는 턴에 한 번까지 1/2(사냥 1/4) ·
    ///    알 기술 1/3 · 다시 찾아보기 1/3 · 프레셔스 볼(볼과 같은 조건)·완전 회복(절반 이하) · 정비로 화상·졸림·월척·규토리 바꾸기·풀의 포켓몬·기술 맞바꾸기·둘째 지닌물건·스냅 가르치기.
    /// </summary>
    static partial class Playout
    {
        /// <summary>한 판 상한(명령 수). 넘으면 「게임 끝에 닿지 않았다」로 실패.</summary>
        public const int MaxCommands = 100000;

        public sealed partial class Game
        {
            public int Seats;
            public ulong Seed;
            public int Commands;
            public int Timeouts;
            public int Rounds;
            public int LiberatedBy = -1;
            /// <summary>공개된 막(0 서막 ~ 4 종막).</summary>
            public int Act = -1;
            /// <summary>게임이 끝났다 · 끝난 까닭 · 이긴 좌석.</summary>
            public bool Ended;
            public GameEndReason Reason;
            public int Winner = -1;
            public int Captures;
            /// <summary>M5 「제작·은행·진화」: 볼 제작 · 그 밖 제작 · 은행상회 거래(줄) · 진화 수.</summary>
            public int BallsCrafted, OtherCrafted, Trades, Evolutions;
            /// <summary>M5 「운명 네 장 효과」: 봇이 낸 운명 효과 수(디아루가 — 정책 ⑥).</summary>
            public int FatePlays;
            /// <summary>봇 정책 ⑨: 정비 명령 수(지니게 하기 · 스탬프 기술).</summary>
            public int Maintenance;
            /// <summary>M5 「사당 공물·카드」: 공물 수 · 3장 뽑기 수 · 나눠 받은 축복 수(4인 이상).</summary>
            public int ShrineOffers, ShrineExtras, ShrineShared;
            /// <summary>M5 「스탬프」: 받은 여행 스탬프 수[칸].</summary>
            public int[] Stamps = new int[SeatState.StampCount];
            /// <summary>M5 「천상의 균열」·「거래·원격 도전」: 신 강림 수 · 원격 도전 수 · 좌석끼리 거래 수.</summary>
            public int GodDescents, RemoteChallenges, SeatTrades;
            /// <summary>M6 「해금 뼈대」: 트레이너 카드가 실버·골드에 닿은 좌석 수(CardUnlocked).</summary>
            public int SilverSeats, GoldSeats;
            /// <summary>M6 「봇 — 트레이너 카드」: 능력마다 AbilityUsed 수[카드 × 2 + (0 파랑 · 1 빨강)] · 게임 끝의 카드마다 좌석 수 · 그 가운데 골드인 좌석 수[카드].</summary>
            public int[] AbilityUses, CardSeats, CardGold;
            /// <summary>M6 「봇 — 트레이너 카드」: AbilityUsed를 내지 않는 능력 정비 — backpacker R 둘째 지닌물건 · tourist R 스냅 가르치기. 정비 종류마다 수[MaintKind].</summary>
            public int SecondHeld, SnapsTaught;
            public int[] MaintKinds = new int[Enum.GetValues(typeof(MaintKind)).Length];
            public int[] Battles = new int[Enum.GetValues(typeof(BattleKind)).Length];
            /// <summary>배틀 종류마다 포획 시도 · 성공 수(결정 122 포획 난도 재기).</summary>
            public int[] CaptureTries = new int[Enum.GetValues(typeof(BattleKind)).Length], CapturesOf = new int[Enum.GetValues(typeof(BattleKind)).Length];
            /// <summary>마지막으로 시작한 배틀의 종류(포획 시도를 그 종류에 센다).</summary>
            public int BattleKindNow;
            /// <summary>방장 멈춤 합(ms) — 갱신마다 좌석별 PlayTiming 길이의 최댓값을 더한다(AI 생각·숨은 손패 창 대기는 빼고).</summary>
            public long PauseMs;
        }

        /// <summary>
        /// 엔진에 직접 한 판(이벤트 켬). 명령마다: 받아들임, 규토리·JP·볼 ≥ 0, 카드 총수 보존, 열린 선택마다 Default ∈ Legal, 모든 이벤트가 PlayTiming 행에 분류됨.
        /// 1/10 확률로 답 대신 Timeout(Default 적용)을 넣는다. 어긋나면 InvalidOperationException. each = 명령마다 원본을 본다(복제 시간 시험이 상태를 고른다).
        /// campaign = 울트라 버스트면 막 없이 돈다(M6 「UB 셋업·맵」). maxRounds > 0이면 그 라운드를 넘길 때 멈춘다(게임 끝 전이어도). options = 규칙 옵션(null이면 기본 — 울트라 온슬롯 판, M6 「봇 — UB 판」).
        /// agent = 가상 응답자 AI(null이면 원칙 — M8 「원칙 AI 점검」이 VirtualAgents를 꽂는다).
        /// commands = 명령마다 넣기 바로 전(판 · 좌석 · 명령) — M8 「사람 기록」이 봇 판을 저장 꼴로 적는다.
        /// steer = 좌석 선택의 답을 대신 정한다(null을 돌려주면 봇 — 시간 초과 주사위보다 먼저, 판을 손봐도 된다) · submitted = 명령을 넣은 바로 뒤(판 · 좌석 · 명령 · 그 명령의 이벤트) — M9 실행 감사(ExecAudit).
        /// </summary>
        public static Game Run(Ctx ctx, int seats, ulong seed, Action<GameState> each = null, Game into = null, Campaign campaign = Campaign.HeartOfSinnoh, int maxRounds = 0, RuleOptions options = null, bool prepared = false,
            IAgent agent = null, Action<GameState, int, Command> commands = null,
            Func<GameState, PendingChoice, ChoiceAnswer> steer = null, Action<GameState, int, Command, EventLog> submitted = null)
        {
            var g = into ?? new Game();
            g.Seats = seats;
            g.Seed = seed;
            // 봇 판의 기본 옵션 = rulings 기본값이되 UB · 야생 기술 선택은 원칙(결정 122 — 봇 판 · 시험대는 원칙 유지, 판 지문이 그대로)
            var s = Engine.NewGame(ctx, options ?? new RuleOptions { NonPlayerMovePolicy = NonPlayerMovePolicy.Principle }, seats, seed, campaign);
            int cardCount = ctx.Cards.TrainerCards.Count;
            g.AbilityUses = new int[cardCount * 2];
            g.CardSeats = new int[cardCount];
            g.CardGold = new int[cardCount];
            int maintained = -1;
            int total = CardTotal(ctx);
            var timing = new PlayTimingContext[seats];
            for (int i = 0; i < seats; i++) timing[i] = new PlayTimingContext(i);
            var log = new EventLog();
            var legal = new List<ChoiceAnswer>();
            var bot = new Bot(seed) { Prepared = prepared };
            var ai = agent ?? new PrincipleAgent();
            var rng = new Pcg32(seed, 0xB07);

            Engine.Start(s, ctx, log);
            After(s, ctx, g, total, log, timing, legal);
            while (s.Phase != Phase.GameOver && (maxRounds <= 0 || s.Round <= maxRounds))
            {
                if (g.Commands >= MaxCommands) throw new InvalidOperationException($"{MaxCommands}명령 안에 게임 끝에 닿지 않았다(라운드 {s.Round}, 막 {(s.Hos != null ? s.Hos.Act : -1)})");
                var c = s.Pending.Find(x => !x.Answered);
                if (c == null) throw new InvalidOperationException($"열린 선택 없이 멈췄다(단계 {s.Phase})");
                int seat;
                Command cmd;
                Maintenance m;
                if (c.Kind == ChoiceKind.MapAction && c.Seat >= 0 && c.Id != maintained && (m = bot.Maintain(s, ctx, c.Seat)) != null)
                {
                    // ⑨ 정비(언제든 행동): 맵 행동에 답하기 전에. 다시 연 선택(새 Id)이면 다시 본다
                    if (m.Kind == MaintKind.HoldItem && m.Slot == 1) g.SecondHeld++;
                    else if (m.Kind == MaintKind.SetMove && m.Value >= 0 && !s.Seats[c.Seat].StampMoves.Contains(m.Value)) g.SnapsTaught++;
                    g.MaintKinds[(int)m.Kind]++;
                    log.Clear();
                    var maint = Command.Anytime(m);
                    commands?.Invoke(s, c.Seat, maint);
                    var mr = Engine.Submit(s, ctx, c.Seat, maint, log);
                    if (!mr.Accepted) throw new InvalidOperationException($"정비 {m.Kind}(포켓몬 {m.Pokemon} · 값 {m.Value})가 거절됐다: {mr}");
                    g.Commands++;
                    g.Maintenance++;
                    After(s, ctx, g, total, log, timing, legal);
                    submitted?.Invoke(s, c.Seat, maint, log);
                    continue;
                }
                maintained = c.Id;
                ChoiceAnswer steered = c.Seat >= 0 ? steer?.Invoke(s, c) : null;
                if (steered != null)
                {
                    seat = c.Seat;
                    cmd = Command.AnswerTo(c.Id, steered);
                }
                else if (rng.Next(10) == 0)
                {
                    seat = SeatId.System;
                    cmd = Command.TimeoutOf(c.Id);
                    g.Timeouts++;
                }
                else if (c.Seat < 0)
                {
                    var view = Engine.ViewFor(s, SeatId.Observer);
                    var d = ai.Decide(new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget());
                    seat = c.Seat;
                    cmd = Command.AiAnswer(c.Id, d.Answer, d.Reason);
                }
                else
                {
                    seat = c.Seat;
                    cmd = Command.AnswerTo(c.Id, bot.Answer(s, ctx, c, rng));
                }
                log.Clear();
                commands?.Invoke(s, seat, cmd);
                var r = Engine.Submit(s, ctx, seat, cmd, log);
                if (!r.Accepted) throw new InvalidOperationException($"{c.Kind}/{c.Sub} 선택 {c.Id}에 낸 {(cmd.Kind == CommandKind.Timeout ? "Timeout" : cmd.Answer.ToString())}이 거절됐다: {r}");
                g.Commands++;
                After(s, ctx, g, total, log, timing, legal);
                submitted?.Invoke(s, seat, cmd, log);
                foreach (var e in log.Events)
                {
                    if (e is GameEnded end)
                    {
                        g.Ended = true;
                        g.Reason = end.Reason;
                        g.Winner = end.Winners.Length > 0 ? end.Winners[0] : -1;
                    }
                }
                if (s.Hos != null)
                {
                    g.Act = s.Hos.Act;
                    g.LiberatedBy = s.Hos.FirstSeat[1];
                }
                g.Rounds = s.Round;
                each?.Invoke(s);
            }
            foreach (var st in s.Seats)
            {
                if (st.TrainerCard < 0) continue;
                g.CardSeats[st.TrainerCard]++;
                if (st.TrainerCardTier == TrainerRules.Gold) g.CardGold[st.TrainerCard]++;
            }
            return g;
        }

        static void After(GameState s, Ctx ctx, Game g, int total, EventLog log, PlayTimingContext[] timing, List<ChoiceAnswer> legal)
        {
            foreach (var seat in s.Seats)
            {
                foreach (int a in seat.Apricorns)
                    if (a < 0) throw new InvalidOperationException($"규토리 {a} < 0");
                foreach (int b in seat.Balls)
                    if (b < 0) throw new InvalidOperationException($"볼 {b} < 0");
                if (seat.Jp < 0) throw new InvalidOperationException($"JP {seat.Jp} < 0");
            }
            int cards = CardCount(s);
            if (cards != total) throw new InvalidOperationException($"카드 총수 {cards} ≠ {total}");
            foreach (var c in s.Pending)
            {
                if (c.Answered) continue;
                Engine.Legal(s, ctx, c, legal);
                if (c.Default == null || !ChoiceRules.Contains(legal, c.Default))
                    throw new InvalidOperationException($"{c.Kind}/{c.Sub} 선택 {c.Id}의 Default {c.Default}가 Legal 밖이다");
            }
            foreach (var e in log.Events)
            {
                if (e is BattleStarted b)
                {
                    g.Battles[(int)b.Kind]++;
                    g.BattleKindNow = (int)b.Kind;
                }
                else if (e is CaptureAttempt ca)
                {
                    // 배틀 종류마다 포획 시도 · 성공(결정 122 — 강한 AI 전환 뒤 포획 난도를 잰다)
                    int kind = g.BattleKindNow;
                    g.CaptureTries[kind]++;
                    if (ca.Success)
                    {
                        g.Captures++;
                        g.CapturesOf[kind]++;
                    }
                }
                else if (e is Crafted cr)
                {
                    if (CraftRules.BallOf(ctx, cr.ItemKey) >= 0) g.BallsCrafted++;
                    else g.OtherCrafted++;
                }
                else if (e is GinkgoTraded) g.Trades++;
                else if (e is Evolved) g.Evolutions++;
                else if (e is FatePlayed) g.FatePlays++;
                else if (e is ShrineOffered so) { g.ShrineOffers++; if (so.Extra) g.ShrineExtras++; }
                else if (e is ShrineBlessing sb && sb.Seat != sb.From) g.ShrineShared++;
                else if (e is GodDescended) g.GodDescents++;
                else if (e is ChallengeIssued ci && ci.Remote) g.RemoteChallenges++;
                else if (e is TradeDone) g.SeatTrades++;
                else if (e is CardUnlocked cu)
                {
                    if (cu.TierBefore < TrainerRules.Silver) g.SilverSeats++;
                    if (cu.TierAfter == TrainerRules.Gold) g.GoldSeats++;
                }
                else if (e is TrainerCardSwapped sw)
                {
                    g.CardSeats[sw.FromCard]++; // youngster R로 바꾼 카드도 그 판에서 돈 카드다(바꾸는 때는 골드)
                    g.CardGold[sw.FromCard]++;
                }
                else if (e is AbilityUsed au)
                {
                    int slot = au.CardKey * 2 + (au.Ability == AbilityColor.Blue ? 0 : 1);
                    g.AbilityUses[slot]++;
                    if (s.Battle != null && UbSideBattle(s.Battle.Kind))
                    {
                        g.UbBattleAbilities++;
                        (g.UbAbilityUses ??= new int[g.AbilityUses.Length])[slot]++;
                    } // M6 「울트라 버스트 규칙 합침」: UB·네크로즈마·시련 배틀 안에서 쓴 능력
                }
                else if (CountUb(g, e)) { } // M6 「봇 — UB 판」(PlayoutUb)
                else if (e is StampGained sg) g.Stamps[ctx.Items.Items[sg.StampKey].Key[6] - '1']++; // stamp.<n>
            }
            g.PauseMs += PlayTiming.UpdateMs(log.Events, timing); // 분류 안 된 이벤트면 여기서 예외
        }

        /// <summary>처음 카드 총수 = 덱 정의 장수의 합(NewGame 직후 덱에 모두 있다).</summary>
        public static int CardTotal(Ctx ctx)
        {
            int n = 0;
            for (int d = 0; d < GameState.DeckCount; d++) n += ctx.Deck((DeckId)d).Count;
            return n;
        }

        /// <summary>
        /// 지금 카드 수: 덱 + 버림 더미(공개된 막 포함) + 손(운명·트레이너 카드 후보·고른 트레이너 카드) + 덱에서 나온 포켓몬 인스턴스
        /// + 열린 조우 고르기의 뽑은 카드(고를 때까지 Options에 있다) + 포켓몬 밑에 둔 옛 카드. 덱에 없어 복제한 진화 카드(Deck = −1)는 세지 않는다.
        /// </summary>
        public static int CardCount(GameState s)
        {
            int n = 0;
            for (int d = 0; d < GameState.DeckCount; d++) n += s.Decks[d].Count + s.Discards[d].Count;
            foreach (var seat in s.Seats) n += seat.FateHand.Count + seat.TrainerCardOffer.Count + (seat.TrainerCard >= 0 ? 1 : 0) + seat.StampCards.Count / 2; // 스탬프 보상으로 곁에 둔 카드
            foreach (var p in s.Pokemon)
            {
                if (p.Def >= 0 && p.Deck >= 0) n++;
                if (p.Def >= 0) n += BreachRules.Attached(p); // 붙은 왜곡 균열 카드(M6 「균열 카드 36장」)
                n += p.Under.Count / 3;   // 진화하며 옛 기술을 가르친 포켓몬 밑에 둔 옛 카드(p.5)
            }
            foreach (var list in new[] { s.Pending, s.Held }) // 미뤄 둔 조우 고르기도(축복 동의 중 — 결정 84 U1)
                foreach (var c in list)
                    if (c.Kind == ChoiceKind.EncounterPick && !c.Answered) n += c.Options.Count;
            if (s.Ub != null) n += (s.Ub.BreachHands.Count + s.Ub.BreachStored.Count) / 2; // 손에 든 · pokemon_ranger B가 보관한 왜곡 균열 카드(M6 「UB 턴」 · 결정 84 U11)
            return n;
        }

        /// <summary>좌석 봇(클래스 설명의 정책). 고향까지 걸음 표만 들고 있다.</summary>
        public sealed partial class Bot
        {
            readonly int explore;
            readonly List<ChoiceAnswer> legal = new List<ChoiceAnswer>();
            readonly Dictionary<int, int[]> dists = new Dictionary<int, int[]>();

            /// <summary>⑪ 맵 능력을 쓴 턴(좌석마다) · 능력 정비를 한 라운드(좌석 × 종류) · 정비 쪽 무작위(Maintain은 답의 rng를 받지 않는다).</summary>
            readonly Dictionary<int, int> mapAbilityTurn = new Dictionary<int, int>();
            readonly HashSet<long> abilityMaint = new HashSet<long>();
            readonly Pcg32 maintRng;

            /// <summary>
            /// ⑭ 운영 규칙(M8 「시험대 바로잡기」 — 결정 109 ②, 기본 끔 · 시험대 장면을 만드는 판만 켠다): 갤럭시단(조무래기 · 간부 · 태홍)에 도전하기 전에 파티를 그 상대 덱과 잰다(<see cref="PartyGauge.Against"/>).
            /// 준비가 안 됐으면(<see cref="PartyMeasure.Ready"/> — 세기 비 비슷함 이상 · 낼 수만큼) 도전을 미루고 모으기처럼 돈다(포획 · 진화 · 제작 — 다쳤으면 마을 회복 먼저).
            /// 간부 · 태홍은 ⑩의 세기 잣대 대신 이것을 쓴다. 미루는 것은 ⑩과 같이 <see cref="Patience"/>라운드, 그 뒤 <see cref="Push"/>라운드는 도전한다(판이 끝나게).
            /// </summary>
            public bool Prepared;

            public Bot(ulong seed)
            {
                explore = 2 + (int)(seed % 4);
                maintRng = new Pcg32(seed, 0xCA2D);
            }

            /// <summary>
            /// ⑪ 트레이너 카드의 물음(Reaction — EffectOption「쓸까」·EffectPick 고르기, 출처 = 카드): 대개 쓴다(「안 함」은 1/8, 사당에 바칠 규토리를 모으는 중이면 1/2 — 규토리를 내는 능력이 있다),
            /// 고를 것은 합법 답 가운데 고르게. 이기려는 정책이 아니라 능력 코드가 봇 판에서 돌게 하려는 것이다(M6 완료 조건 4).
            /// </summary>
            ChoiceAnswer CardAsk(GameState s, PendingChoice c, Pcg32 rng)
            {
                var yes = legal.FindAll(a => a.Kind != AnswerKind.Decline);
                if (yes.Count == 0) return legal[0].Clone();
                var me = s.Seats[c.Seat];
                bool saving = me.ActProgress == 2 && !me.Heart;
                if (yes.Count < legal.Count && rng.Next(saving ? 2 : 8) == 0) return legal.Find(a => a.Kind == AnswerKind.Decline).Clone();
                return yes[rng.Next(yes.Count)].Clone();
            }

            /// <summary>⑪ 배틀 행동의 능력: gentleman R 프레셔스 볼(볼을 던질 때와 같은 조건, 1/2) · lady R 완전 회복(파티에서 남은 체력 비율이 가장 낮은 포켓몬이 절반 이하면). 없으면 null.</summary>
            ChoiceAnswer BattleAbility(GameState s, Ctx ctx, PendingChoice c, SeatState me, Pcg32 rng)
            {
                ChoiceAnswer potion = null;
                double low = 0.5;
                foreach (var a in legal)
                {
                    if (a.Kind != AnswerKind.UseAbility) continue;
                    if (a.Target == TrainerRules.CherishTarget)
                    {
                        if ((PartySize(me) < me.OpenSlots || Upgrade(s, ctx, me)) && rng.Next(2) == 0) return a.Clone();
                        continue;
                    }
                    if (a.Target < 0) continue;
                    var p = s.Pokemon[a.Target];
                    double f = (double)PokemonRules.Hp(ctx, p) / Math.Max(1, PokemonRules.MaxHp(ctx, p));
                    if (f > low) continue;
                    potion = a;
                    low = f;
                }
                return potion?.Clone();
            }

            /// <summary>s는 원본이든 그 좌석 보기든 같다 — 공개 정보와 자기 선택의 Legal만 읽는다.</summary>
            public ChoiceAnswer Answer(GameState s, Ctx ctx, PendingChoice c, Pcg32 rng)
            {
                Engine.Legal(s, ctx, c, legal);
                if (legal.Count == 0) throw new InvalidOperationException($"{c.Kind}/{c.Sub} 선택 {c.Id}: 합법 답이 없다");
                if (c.Kind == ChoiceKind.Reaction && c.Source.Kind == SourceKind.TrainerCard && (c.Sub == ChoiceSub.EffectOption || c.Sub == ChoiceSub.EffectPick))
                    return CardAsk(s, c, rng); // ⑪ 트레이너 카드의 「쓸까」·고르기
                if (c.Kind == ChoiceKind.EncounterPick)
                {
                    var retry = legal.Find(a => a.Kind == AnswerKind.UseAbility); // ⑪ ace_trainer B 다시 찾아보기
                    if (retry != null && rng.Next(3) == 0) return retry.Clone();
                }
                switch (c.Kind)
                {
                    case ChoiceKind.MapAction:
                        return Map(s, ctx, c, rng);
                    case ChoiceKind.Simultaneous when c.Sub == ChoiceSub.BreachAttach:
                        return c.Default.Clone();   // 균열 카드 붙이기·버리기·옮기기(M6): 기본 답(빈 칸 먼저 — 가르친 기술을 버리지 않는다 · pokemon_ranger B는 보관)
                    case ChoiceKind.Respond when c.Sub == ChoiceSub.Blessing:
                        return c.Default.Clone();   // ⑬ 축복 동의(결정 84 U1): 막지 않는다(기본)
                    case ChoiceKind.Respond when c.Sub == ChoiceSub.UbSkip:
                        // ⑬ 라운드 끝 건너뛰기(결정 84 U2): 정비로 내는 때와 같은 기준 — 균열 50% 이상이고 축복이 둘 이상
                        return ChoiceAnswer.Of(UbTurn.Breach(s) >= 50 && s.Ub.Blessings >= 2 ? AnswerKind.Accept : AnswerKind.Decline);
                    case ChoiceKind.Reaction when c.Sub == ChoiceSub.AlluringStew:
                        return c.Default.Clone();   // 매혹적인 스튜: 결과가 좋아질 때만 쓴다(기본)
                    case ChoiceKind.Reaction when c.Sub == ChoiceSub.Fate:
                        return Mind(s, c, rng) ?? SureFate(s, c);      // ⑪ psychic R · 앰프(결정 131 · 134 — 같은 창) · ⑥ 운명: 이득이 분명할 때만
                    case ChoiceKind.BattleAction:
                    {
                        var me = s.Seats[c.Seat];
                        bool serious = Serious(s.Battle);
                        var card = BattleAbility(s, ctx, c, me, rng); // ⑪ lady R 완전 회복 · gentleman R 프레셔스 볼
                        if (card != null) return card;
                        var ball = legal.FindLast(a => a.Kind == AnswerKind.ThrowBall); // 가장 좋은 볼
                        if (ball != null && (PartySize(me) < me.OpenSlots || Upgrade(s, ctx, me)) && rng.Next(2) == 0) return ball.Clone(); // ⑧ 파티가 가득이면 가장 약한 것보다 셀 때만
                        if (!serious && rng.Next(4) == 0) break;
                        var b = s.Battle;
                        int k = ChoiceRules.BattleSideOf(s, c.Seat);
                        int foe = b.Sides[1 - k].Active;
                        var attacker = s.Pokemon[b.Sides[k].Active];
                        var defender = foe >= 0 ? s.Pokemon[foe] : null;
                        var want = PrincipleRules.Answer(ctx, attacker, defender, out _);
                        var hit = legal.Find(a => a.SameAs(want));
                        if (serious) return BestMove(ctx, attacker, defender, hit);
                        if (hit != null) return hit.Clone();
                        break;
                    }
                    case ChoiceKind.SendIn when Serious(s.Battle):
                    case ChoiceKind.NextPokemon when Serious(s.Battle):
                        return BestMatchup(s, ctx, c);
                    case ChoiceKind.SwitchWindow when Serious(s.Battle):
                        return legal[0].Clone(); // 교체 안 함
                    case ChoiceKind.Reaction when c.Sub == ChoiceSub.InitiativeToken && Serious(s.Battle):
                        return legal[legal.Count - 1].Clone(); // 우선권 토큰을 쓴다(Accept)
                    case ChoiceKind.EncounterPick when PartySize(s.Seats[c.Seat]) >= 3:
                    {
                        // ⑧ 조우 고르기: 파티가 셋 이상이면 체력이 가장 큰 카드(JP·포획이 크다)
                        ChoiceAnswer best = null;
                        int hp = -1;
                        foreach (var a in legal)
                        {
                            if (a.Kind != AnswerKind.Pick) continue;
                            int h = ctx.Cards.Pokemon[a.Target].Health;
                            if (h <= hp) continue;
                            best = a;
                            hp = h;
                        }
                        if (best != null && rng.Next(4) != 0) return best.Clone();
                        break;
                    }
                    case ChoiceKind.Reward when c.Sub == ChoiceSub.TeachMove:
                        return Teach(s, ctx, c.Seat, c.Subject, legal);
                    case ChoiceKind.Reward when c.Sub == ChoiceSub.EvolveTeach:
                        return Teach(s, ctx, c.Seat, c.Subject, legal);
                    case ChoiceKind.Reward when c.Sub == ChoiceSub.PartyFull:
                    {
                        // ⑧ 파티 가득: 가장 약한 포켓몬(새로 잡은 것 포함)을 놓아준다
                        ChoiceAnswer worst = null;
                        int low = int.MaxValue;
                        foreach (var a in legal)
                        {
                            int v = Score(ctx, s.Pokemon[a.Target]);
                            if (v >= low) continue;
                            worst = a;
                            low = v;
                        }
                        return worst.Clone();
                    }
                }
                return legal[rng.Next(legal.Count)].Clone();
            }

            /// <summary>포켓몬 세기(봇의 잣대): 최대 체력 + 2 × 가장 큰 기술 공격력.</summary>
            static int Score(Ctx ctx, PokemonState p)
            {
                int best = 0;
                var options = new List<int>(4);
                PokemonRules.MoveOptions(ctx, p, options);
                foreach (int m in options) best = Math.Max(best, BattleRules.MoveStrength(ctx, m));
                return PokemonRules.MaxHp(ctx, p) + 2 * best;
            }

            /// <summary>파티 남은 체력 / 최대 체력 합.</summary>
            static double HpFraction(GameState s, Ctx ctx, SeatState me)
            {
                int hp = 0, max = 0;
                foreach (int p in me.Party)
                {
                    if (p < 0) continue;
                    hp += PokemonRules.Hp(ctx, s.Pokemon[p]);
                    max += PokemonRules.MaxHp(ctx, s.Pokemon[p]);
                }
                return max == 0 ? 1 : (double)hp / max;
            }

            /// <summary>파티에 아무것도 안 지닌 포켓몬이 있고 가방에 지닐 것이 없다.</summary>
            static bool Unheld(GameState s, Ctx ctx, SeatState me)
            {
                foreach (int item in me.Bag)
                    if (ctx.Items.Items[item].Kind == ItemKind.Held) return false;
                foreach (int p in me.Party)
                    if (p >= 0 && s.Pokemon[p].HeldItem < 0 && !s.Pokemon[p].Alpha) return true;
                return false;
            }

            /// <summary>지금 야생 상대가 파티에서 가장 약한 포켓몬보다 센가(파티가 가득일 때 볼을 던질 까닭).</summary>
            static bool Upgrade(GameState s, Ctx ctx, SeatState me)
            {
                int foe = s.Battle.Sides[1].Active;
                if (foe < 0) return false;
                int low = int.MaxValue;
                foreach (int p in me.Party)
                    if (p >= 0) low = Math.Min(low, Score(ctx, s.Pokemon[p]));
                return Score(ctx, s.Pokemon[foe]) > low + 2;
            }

            static int Strength(Ctx ctx, int move) => move < 0 ? -1 : BattleRules.MoveStrength(ctx, move);

            /// <summary>
            /// ⑧ 기술 가르치기(트레이너전·야생 격파 보상 · 진화의 옛 시그니처): 이득(<see cref="Gain"/>)이 가장 큰 (포켓몬, 칸). 이득이 없으면 안 가르침.
            /// </summary>
            ChoiceAnswer Teach(GameState s, Ctx ctx, int seat, int move, List<ChoiceAnswer> legal)
            {
                ChoiceAnswer best = null;
                double bestGain = 0;
                foreach (var a in legal)
                {
                    if (a.Kind != AnswerKind.Pick) continue;
                    double g = Gain(s, ctx, seat, s.Pokemon[a.Target], a.Arg, move);
                    if (g <= bestGain) continue;
                    best = a;
                    bestGain = g;
                }
                return (best ?? legal[0]).Clone();
            }

            /// <summary>
            /// 그 칸의 기술을 move로 바꾸는 이득. 다음에 이겨야 할 갤럭시단(1막 뒤 = 아직 못 이긴 간부들의 카드, 조각 셋 = 태홍의 카드)이 있으면
            /// 그 카드들 각각에게 가장 큰 원칙 값의 합(타입 상성 — 사람이 상대에 맞춰 기술을 고르는 것처럼)이 늘어난 만큼, 없으면 공격력이 늘어난 만큼.
            /// 같은 기술이 이미 있으면 0. 포켓몬 세기를 아주 작게 더해 같으면 센 포켓몬에게.
            /// </summary>
            double Gain(GameState s, Ctx ctx, int seat, PokemonState p, int slot, int move)
            {
                int old = p.Moves[slot];
                int sig = ctx.Cards.Pokemon[p.Def].Signature;
                if (old == move || Array.IndexOf(p.Moves, move) >= 0 || sig == move) return 0;
                var foes = FoeCards(s, ctx, seat);
                double g;
                if (foes.Count == 0) g = old < 0 ? Strength(ctx, move) + 0.5 : Strength(ctx, move) - Strength(ctx, old);
                else
                {
                    int other = p.Moves[1 - slot];
                    g = SetValue(ctx, p.Def, sig, move, other, foes) - SetValue(ctx, p.Def, sig, old, other, foes);
                    if (old < 0) g += 0.01;
                }
                return g <= 0 ? 0 : g + Score(ctx, p) * 1e-4;
            }

            readonly Dictionary<long, double> values = new Dictionary<long, double>();
            readonly Dictionary<int, List<int>> foeCards = new Dictionary<int, List<int>>();
            static readonly List<int> NoFoes = new List<int>();

            /// <summary>다음에 이겨야 할 갤럭시단 카드(포켓몬 표 색인): 1막 뒤 = 그 좌석이 아직 조각을 못 받은 간부들의 카드, 조각 셋 = 태홍의 카드, 그 밖 = 없음.</summary>
            List<int> FoeCards(GameState s, Ctx ctx, int seat)
            {
                var st = s.Seats[seat];
                if (st.ActProgress < 1 || st.ActProgress > 2) return NoFoes;
                int key = st.ActProgress == 2 ? -1 : (st.Shards[0] ? 1 : 0) | (st.Shards[1] ? 2 : 0) | (st.Shards[2] ? 4 : 0);
                if (foeCards.TryGetValue(key, out var list)) return list;
                list = new List<int>();
                foreach (var e in ctx.Deck(key < 0 ? DeckId.GalacticBoss : DeckId.GalacticCommander).Cards)
                {
                    var trainer = ctx.Cards.Pokemon[e.Card].Trainer;
                    int shard = HosFlow.ShardOf(trainer);
                    if (key < 0 ? trainer == GalacticTrainer.Cyrus : shard >= 0 && (key >> shard & 1) == 0) list.Add(e.Card);
                }
                foeCards[key] = list;
                return list;
            }

            /// <summary>기술 셋(시그니처 + 두 칸, −1 = 빈칸)이 그 카드들 각각에게 낼 가장 큰 원칙 값의 합(값 ÷ Scale).</summary>
            double SetValue(Ctx ctx, int def, int sig, int m1, int m2, List<int> foes)
            {
                double sum = 0;
                foreach (int foe in foes)
                {
                    double best = MoveValue(ctx, sig, def, foe);
                    if (m1 >= 0) best = Math.Max(best, MoveValue(ctx, m1, def, foe));
                    if (m2 >= 0) best = Math.Max(best, MoveValue(ctx, m2, def, foe));
                    sum += best;
                }
                return sum;
            }

            /// <summary>그 카드가 그 기술로 그 카드에게 낼 원칙 값(값 ÷ Scale, 다친 데 없는 새 포켓몬끼리). 셈을 들고 있는다.</summary>
            double MoveValue(Ctx ctx, int move, int attacker, int defender)
            {
                long key = ((long)move * 4096 + attacker) * 4096 + defender;
                if (values.TryGetValue(key, out double v)) return v;
                var a = new PokemonState { Def = attacker, Owner = 0 };
                var d = new PokemonState { Def = defender, Owner = SeatId.Galactic };
                v = PrincipleRules.Value(ctx, move, a, d) / (double)PrincipleRules.Scale;
                values[key] = v;
                return v;
            }

            /// <summary>
            /// ⑨ 정비(엔진 직접 판만 — 맵 행동 차례 앞에): 가방의 지닌물건·유물을 아무것도 안 지닌 포켓몬에게(유물은 그 타입 포켓몬, 진화의 돌은 아직 진화할 포켓몬, 그 밖은 가장 센 포켓몬),
            /// 스탬프 기술 풀의 기술을 빈 칸 또는 더 약한 배운 기술 자리에. 할 것이 없으면 null.
            /// </summary>
            public Maintenance Maintain(GameState s, Ctx ctx, int seat)
            {
                var ub = UbMaintain(s, seat); // M6 「봇 — UB 판」: 아르세우스의 축복(PlayoutUb)
                if (ub != null) return ub;
                var st = s.Seats[seat];
                foreach (int item in st.Bag)
                {
                    var kind = ctx.Items.Items[item].Kind;
                    bool relic = RelicRules.IsRelic(ctx, item);
                    if (kind != ItemKind.Held && !relic) continue;
                    var types = RelicRules.TypesOf(ctx, item);
                    if (relic && types.Count == 0) continue; // 천상의 유물(가지면 된다)·폼 유물은 지니지 않는다
                    if (ctx.Items.Items[item].Key == SwiftnessSash) continue; // 신속의 띠(공격력 −1)는 지니지 않는다
                    bool eviolite = ctx.Items.Items[item].Key == HeldItems.Eviolite;
                    int target = -1, bestScore = int.MinValue;
                    foreach (int p in st.Party)
                    {
                        if (p < 0) continue;
                        var pk = s.Pokemon[p];
                        if (pk.HeldItem >= 0 || pk.Alpha) continue;
                        var def = ctx.Cards.Pokemon[pk.Def];
                        if (relic && types.Count > 0 && !UsesType(ctx, pk, types)) continue;
                        if (eviolite && def.EvolveInto.Count == 0) continue;
                        int v = Score(ctx, pk);
                        if (v <= bestScore) continue;
                        target = p;
                        bestScore = v;
                    }
                    if (target >= 0) return new Maintenance { Kind = MaintKind.HoldItem, Pokemon = target, Value = item };
                    if (!TrainerRules.Pockets(s, ctx, seat)) continue;
                    // ⑪ backpacker R: 아무것도 안 지닌 포켓몬이 없으면 둘째 칸(Slot 1)에 — 같은 조건 · 첫 칸과 다른 아이템
                    foreach (int p in st.Party)
                    {
                        if (p < 0) continue;
                        var pk = s.Pokemon[p];
                        if (pk.HeldItem < 0 || pk.HeldItem2 >= 0 || pk.Alpha || pk.HeldItem == item) continue;
                        if (relic && types.Count > 0 && !UsesType(ctx, pk, types)) continue;
                        if (eviolite && ctx.Cards.Pokemon[pk.Def].EvolveInto.Count == 0) continue;
                        int v = Score(ctx, pk);
                        if (v <= bestScore) continue;
                        target = p;
                        bestScore = v;
                    }
                    var second = new Maintenance { Kind = MaintKind.HoldItem, Pokemon = target, Slot = 1, Value = item };
                    if (target >= 0 && MaintenanceRules.Check(s, ctx, seat, second) == RejectReason.None) return second;
                }
                var teachable = new List<int>(st.StampMoves);
                foreach (int snap in st.Snaps)
                {
                    int sig = ctx.Cards.Pokemon[snap].Signature; // ⑪ tourist R: 전설 스냅도 가르친다(배울 수 있는 포켓몬에게만)
                    if (sig >= 0 && !teachable.Contains(sig) && TrainerRules.SnapOf(s, ctx, seat, sig) >= 0) teachable.Add(sig);
                }
                foreach (int move in teachable)
                {
                    var type = ctx.Cards.Moves[move].Type;
                    bool snapMove = !st.StampMoves.Contains(move);
                    int target = -1, slot = -1;
                    double bestGain = 0;
                    foreach (int p in st.Party)
                    {
                        if (p < 0) continue;
                        var pk = s.Pokemon[p];
                        if ((snapMove || s.Options.StampMoveTypeLimit) && !ChoiceRules.CanLearn(ctx, pk, type)) continue;
                        for (int k = 0; k < PokemonState.MoveSlots; k++)
                        {
                            if (BreachRules.Blocked(pk, k)) continue; // 균열 카드가 막은 칸
                            double g = Gain(s, ctx, seat, pk, k, move);
                            if (g <= bestGain) continue;
                            target = p;
                            slot = k;
                            bestGain = g;
                        }
                    }
                    if (target >= 0) return new Maintenance { Kind = MaintKind.SetMove, Pokemon = target, Slot = slot, Value = move };
                }
                return AbilityMaint(s, ctx, seat);
            }

            /// <summary>
            /// ⑪ 능력 정비(M6 — 좌석 × 종류마다 라운드에 한 번까지, 엔진 Check를 지난 것만): black_belt R 격투에 화상 · fairy_tale_girl R 페어리에 졸림(맞는 타입이 없으면 1/6로 아무에게나) ·
            /// fisherman R 월척 한 단계(JP 6 이상일 때) · chef B 가장 많은 색 하나를 같은 단계의 가장 적은 색으로(2 이상 차이) · collector R 풀의 포켓몬을 파티로(1/2) ·
            /// interviewers R 배운 기술 맞바꾸기(1/3). 할 것이 없으면 null.
            /// </summary>
            Maintenance AbilityMaint(GameState s, Ctx ctx, int seat)
            {
                var st = s.Seats[seat];
                if (st.TrainerCard < 0) return null;
                Maintenance Once(MaintKind kind, Maintenance m)
                {
                    if (m == null || MaintenanceRules.Check(s, ctx, seat, m) != RejectReason.None) return null;
                    abilityMaint.Add(((long)seat * 1000000 + s.Round) * 16 + (int)kind);
                    return m;
                }
                bool Fresh(MaintKind kind) => !abilityMaint.Contains(((long)seat * 1000000 + s.Round) * 16 + (int)kind);
                Maintenance m;
                if (Fresh(MaintKind.Afflict))
                {
                    var status = TrainerRules.Has(s, ctx, seat, "black_belt", AbilityColor.Red) ? StatusEffect.Burned
                        : TrainerRules.Has(s, ctx, seat, "fairy_tale_girl", AbilityColor.Red) ? StatusEffect.Drowsy : StatusEffect.None;
                    if (status != StatusEffect.None)
                    {
                        var want = status == StatusEffect.Burned ? PokemonType.Fighting : PokemonType.Fairy;
                        bool any = maintRng.Next(6) == 0;
                        foreach (int p in st.Party)
                        {
                            if (p < 0) continue;
                            bool fits = false;
                            foreach (var t in ctx.Cards.Pokemon[s.Pokemon[p].Def].Types) fits |= t == want;
                            if (!any && !fits) continue;
                            if ((m = Once(MaintKind.Afflict, new Maintenance { Kind = MaintKind.Afflict, Pokemon = p, Value = (int)status })) != null) return m;
                        }
                    }
                }
                if (Fresh(MaintKind.CatchSize) && st.Jp >= 6 && TrainerRules.Has(s, ctx, seat, "fisherman", AbilityColor.Red))
                    foreach (int p in st.Party)
                        if (p >= 0 && s.Pokemon[p].CatchSize >= 0
                            && (m = Once(MaintKind.CatchSize, new Maintenance { Kind = MaintKind.CatchSize, Pokemon = p, Value = s.Pokemon[p].CatchSize + 1 })) != null) return m;
                if (Fresh(MaintKind.Convert) && TrainerRules.Has(s, ctx, seat, "chef", AbilityColor.Blue))
                {
                    int from = -1, to = -1, gap = 1;
                    for (int a = 0; a < SeatState.ApricornColors; a++)
                        for (int b = 0; b < SeatState.ApricornColors; b++)
                            if (a != b && MapActions.ApricornGrade((ApricornColor)a) == MapActions.ApricornGrade((ApricornColor)b) && st.Apricorns[a] - st.Apricorns[b] > gap)
                            {
                                from = a;
                                to = b;
                                gap = st.Apricorns[a] - st.Apricorns[b];
                            }
                    if (from >= 0 && (m = Once(MaintKind.Convert, new Maintenance { Kind = MaintKind.Convert, Slot = from, Value = to })) != null) return m;
                }
                if (Fresh(MaintKind.Adopt) && st.StampCards.Count >= 2 && TrainerRules.Has(s, ctx, seat, "collector", AbilityColor.Red) && maintRng.Next(2) == 0)
                    for (int i = 0; i + 1 < st.StampCards.Count; i += 2)
                        if ((m = Once(MaintKind.Adopt, new Maintenance { Kind = MaintKind.Adopt, Value = st.StampCards[i + 1] })) != null) return m;
                if (Fresh(MaintKind.PassMove) && TrainerRules.Has(s, ctx, seat, "interviewers", AbilityColor.Red) && maintRng.Next(3) == 0)
                    foreach (int p in st.Party)
                        foreach (int q in st.Party)
                        {
                            if (p < 0 || q < 0 || p == q) continue;
                            for (int a = 0; a < PokemonState.MoveSlots; a++)
                                for (int b = 0; b < PokemonState.MoveSlots; b++)
                                {
                                    if (s.Pokemon[p].Moves[a] < 0) continue; // 배운 기술이 있는 칸에서
                                    var pass = new Maintenance { Kind = MaintKind.PassMove, Pokemon = p, Slot = a, Value = q };
                                    pass.Values.Add(b);
                                    if ((m = Once(MaintKind.PassMove, pass)) != null) return m;
                                }
                        }
                // pokemon_ranger B: 보관한 균열 카드를 빈 기술 칸에 꺼내 붙인다(결정 87 R4 — 1/4, 내 턴·배틀 밖은 엔진 Check가 가른다)
                if (s.Ub != null && Fresh(MaintKind.AttachBreach) && BreachRules.StoredCount(s, seat) > 0 && maintRng.Next(4) == 0)
                {
                    int card = -1;
                    for (int i = 0; i + 1 < s.Ub.BreachStored.Count && card < 0; i += 2)
                        if (s.Ub.BreachStored[i] == seat) card = s.Ub.BreachStored[i + 1];
                    foreach (int p in st.Party)
                        for (int k = 0; p >= 0 && k < PokemonState.MoveSlots; k++)
                            if (s.Pokemon[p].Moves[k] < 0
                                && (m = Once(MaintKind.AttachBreach, new Maintenance { Kind = MaintKind.AttachBreach, Pokemon = p, Slot = k, Value = card })) != null) return m;
                }
                return null;
            }

            /// <summary>그 포켓몬의 기술 중 그 타입(유물이 올리는 두 타입)인 것이 있나.</summary>
            static bool UsesType(Ctx ctx, PokemonState p, IReadOnlyList<PokemonType> types)
            {
                var options = new List<int>(4);
                PokemonRules.MoveOptions(ctx, p, options);
                foreach (int m in options)
                    foreach (var u in types)
                        if (BattleRules.MoveType(ctx, m) == u) return true;
                return false;
            }

            /// <summary>⑦ 이겨야 막이 나아가는 배틀(간부·태홍·조무래기 · 신 · 배신자 연전): 고르게 고르지 않는다 — 기권하지 않고 값이 가장 큰 기술, 출전은 상대에게 값이 가장 큰 포켓몬.</summary>
            static bool Serious(BattleState b) => b != null && (b.Kind == BattleKind.Galactic || b.Kind == BattleKind.God || b.Kind == BattleKind.Traitor
                || b.Kind == BattleKind.UltraBeast || b.Kind == BattleKind.Necrozma || b.Kind == BattleKind.Trial); // M6: UB · 네크로즈마 · 호수 시련도

            /// <summary>Legal의 기술 답(벤치에서 쓰는 기술은 뺀다) 중 원칙 값이 가장 큰 것, 같으면 원칙 기술(principle). 기술이 없으면 Legal 첫 답.</summary>
            ChoiceAnswer BestMove(Ctx ctx, PokemonState attacker, PokemonState defender, ChoiceAnswer principle)
            {
                ChoiceAnswer best = principle;
                long bestValue = principle != null ? PrincipleRules.Value(ctx, principle.Target, attacker, defender) : long.MinValue;
                foreach (var a in legal)
                {
                    if (a.Kind != AnswerKind.UseMove || a.Arg >= 0 || a.Values.Count > 0) continue;
                    long v = PrincipleRules.Value(ctx, a.Target, attacker, defender);
                    if (v <= bestValue) continue;
                    best = a;
                    bestValue = v;
                }
                if (best == null) best = legal.Find(a => a.Kind != AnswerKind.Forfeit) ?? legal[0];
                return best.Clone();
            }

            /// <summary>내보낼 포켓몬: 상대 출전 포켓몬에게 원칙 값이 가장 큰 기술 × 남은 체력이 가장 큰 것(같으면 Legal 순서).</summary>
            ChoiceAnswer BestMatchup(GameState s, Ctx ctx, PendingChoice c)
            {
                int k = ChoiceRules.BattleSideOf(s, c.Seat);
                int foe = k >= 0 ? s.Battle.Sides[1 - k].Active : -1;
                var defender = foe >= 0 ? s.Pokemon[foe] : null;
                ChoiceAnswer best = null;
                long bestValue = long.MinValue;
                foreach (var a in legal)
                {
                    if (a.Kind != AnswerKind.Pick || a.Target < 0) continue;
                    var p = s.Pokemon[a.Target];
                    long v = PrincipleRules.BestValue(ctx, p, defender) * (PokemonRules.Hp(ctx, p) + 2);
                    if (v <= bestValue) continue;
                    best = a;
                    bestValue = v;
                }
                return (best ?? legal[0]).Clone();
            }

            static int PartySize(SeatState seat)
            {
                int n = 0;
                foreach (int p in seat.Party)
                    if (p >= 0) n++;
                return n;
            }

            /// <summary>
            /// ⑪ psychic R · guitarist 앰프(사용자 결정 131 · 134 — 굴린 뒤 반응 창의 반응, 운명과 같은 선택 · 카드는 좌석마다 하나라 둘이 함께 서지 않는다): 공격마다 처음 물을 때 한 번 ⑪의 셈(<see cref="CardAsk"/> — 안 함 1/8 · 그 밖 고르게)으로 정한다 —
            /// 옛 따로 묻기(PostRoll)와 같은 난수를 쓴다. 쓰면 그 답, 안 쓰거나 이미 정했으면 null(운명은 ⑥ 그대로).
            /// </summary>
            ChoiceAnswer Mind(GameState s, PendingChoice c, Pcg32 rng)
            {
                var b = s.Battle;
                var yes = legal.FindAll(a => a.Kind == AnswerKind.UseAbility);
                if (b == null || yes.Count == 0) return null;
                string at = c.Seat + ":" + s.TurnId + ":" + b.Turn + ":" + b.ActiveSide + ":" + b.Move;
                if (at == mindAt) return null;
                mindAt = at;
                var me = s.Seats[c.Seat];
                bool saving = me.ActProgress == 2 && !me.Heart;
                if (rng.Next(saving ? 2 : 8) == 0) return null;
                return yes[rng.Next(yes.Count)].Clone();
            }

            string mindAt;

            /// <summary>⑥ 운명 반응 창(M5 「운명 네 장 효과」 [가정] §5 #83): 굴린 뒤 창에서 디아루가 1장 — 공격하는 쪽은 0인 눈(지운 주사위 → 심볼), 받는 쪽은 크리티컬을 왼쪽부터 하나. 그 밖(기라티나 · 3장)은 안 낸다.</summary>
            ChoiceAnswer SureFate(GameState s, PendingChoice c)
            {
                var b = s.Battle;
                // ⑥ 보충: 간부·태홍·신·배신자 배틀 밖에서는 손패가 가득(5장 — 6장째는 버린다, A12)일 때만 낸다(이겨야 할 배틀에 아껴 둔다)
                if (b != null && !Serious(b) && s.Seats[c.Seat].FateHand.Count < FateRules.HandLimit) return legal[0].Clone();
                if (b != null && c.Window == Window.AfterRoll && b.ActiveSide >= 0)
                    foreach (var face in b.Sides[b.ActiveSide].Owner == c.Seat ? new[] { AttackDieFace.None, AttackDieFace.StatusSymbol } : new[] { AttackDieFace.CriticalHit })
                    {
                        int i = b.Faces.IndexOf(face);
                        var hit = i < 0 ? null : legal.Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga && a.Arg == 1 && a.Values[0] == i);
                        if (hit != null) return hit.Clone();
                    }
                return legal[0].Clone();
            }

            /// <summary>⑩ 도전할 파티 세기 ÷ 상대 명단 세기, 못 미친 채 기다릴 라운드(60판 진단으로 고른 값 — plan-log 「M5 봇 종막 대역 0」).</summary>
            public const double ReadyRatio = 1.3;
            public const int Patience = 40;
            public const int Push = 15;

            /// <summary>
            /// ⑩ 도전할 만한가(목표 칸이 호수 = 간부, 신오신전 = 태홍일 때만 — 사당은 늘 그렇다): 파티 세기 Σ(최대 체력 × 기술 공격력) ≥ <see cref="ReadyRatio"/> × 상대 명단 세기
            /// (그 인물의 카드 평균 × 낼 수). 못 미치면 <see cref="Patience"/>라운드 키우고 <see cref="Push"/>라운드 도전하기를 되풀이한다.
            /// </summary>
            bool Ready(GameState s, Ctx ctx, int seat, int goal)
            {
                var leader = GalacticTrainer.None;
                int shard = HosFlow.LakeShard(ctx, goal);
                if (shard >= 0) leader = HosFlow.CommanderOf((Shard)shard);
                else if (goal == HosFlow.TempleCell(ctx)) leader = GalacticTrainer.Cyrus;
                if (leader == GalacticTrainer.None) return true;
                return ReadyFor(s, ctx, seat, leader);
            }

            /// <summary>⑩·⑭ 그 인물에게 도전할 만한가(기다림 · 되풀이 포함). ⑭가 켜져 있으면 파티 재기(<see cref="PartyGauge"/>), 아니면 ⑩의 세기 잣대.</summary>
            bool ReadyFor(GameState s, Ctx ctx, int seat, GalacticTrainer leader)
            {
                var me = s.Seats[seat];
                int key = seat * 16 + (int)leader;
                if (!waitSince.TryGetValue(key, out int since)) waitSince[key] = since = s.Round;
                if ((s.Round - since) % (Patience + Push) >= Patience) return true; // 기다린 뒤 Push라운드 동안은 도전하고, 다시 기다린다
                if (Prepared) return PartyGauge.Against(s, ctx, seat, leader).Ready; // ⑭
                double team = 0;
                foreach (int p in me.Party)
                {
                    if (p < 0) continue;
                    var pk = s.Pokemon[p];
                    team += PokemonRules.MaxHp(ctx, pk) * Power(ctx, pk, me.Heart);
                }
                double foe = 0;
                int cards = 0;
                foreach (var e in ctx.Deck(leader == GalacticTrainer.Cyrus ? DeckId.GalacticBoss : DeckId.GalacticCommander).Cards)
                {
                    var d = ctx.Cards.Pokemon[e.Card];
                    if (d.Trainer != leader) continue;
                    foe += d.Health * BattleRules.MoveStrength(ctx, d.Signature);
                    cards++;
                }
                int n = leader == GalacticTrainer.Cyrus ? HosFlow.BossLineup : HosFlow.CommanderLineup(HosFlow.ShardCount(me));
                return team >= ReadyRatio * foe / Math.Max(1, cards) * n;
            }

            readonly Dictionary<int, int> waitSince = new Dictionary<int, int>();

            /// <summary>그 포켓몬의 가장 큰 기술 공격력 + 지닌 유물이 그 기술 타입이면 1 + 결단의 머리띠 1 + 신오의 심장(heart)이 있고 같은 타입이면 1.</summary>
            static double Power(Ctx ctx, PokemonState p, bool heart)
            {
                var options = new List<int>(4);
                PokemonRules.MoveOptions(ctx, p, options);
                var relic = RelicRules.TypesOf(ctx, p.HeldItem);
                var own = ctx.Cards.Pokemon[p.Def].Types;
                double best = 0;
                foreach (int m in options)
                {
                    double v = BattleRules.MoveStrength(ctx, m);
                    var t = BattleRules.MoveType(ctx, m);
                    foreach (var u in relic)
                        if (u == t) v += 1;
                    if (heart)
                        foreach (var u in own)
                            if (u == t) v += 1;
                    best = Math.Max(best, v);
                }
                if (HeldItems.Holds(ctx, p, HeldItems.ChoiceBand)) best += HeldItems.ChoiceBandPower;
                return best;
            }

            /// <summary>⑨ 만들 지닌물건 순서: 결단의 머리띠(늘 공격력 +1) → 별의 방패 → 행복의 알 → 폭발수정. 진화의 휘석·신속의 띠는 만들지 않는다(고르게 고를 때만).</summary>
            const string SwiftnessSash = "item.swiftness_sash";

            static readonly string[] HeldOrder = { HeldItems.ChoiceBand, HeldItems.StarShield, HeldItems.LuckyEgg, HeldItems.BlastCrystal };

            ChoiceAnswer Map(GameState s, Ctx ctx, PendingChoice c, Pcg32 rng)
            {
                legal.RemoveAll(a => a.Kind == AnswerKind.PlayFate); // ⑥ 맵에서는 운명을 내지 않는다(펄기아 — 이득이 분명한 때를 고르지 않는다)
                var me = s.Seats[c.Seat];
                var egg = legal.Find(a => a.Kind == AnswerKind.Evolve && a.Arg == TrainerRules.EggArg);
                if (egg != null && rng.Next(3) == 0) return egg.Clone(); // ⑪ pokemon_breeder B 알 기술(진화 대신)
                // ⑫ tourist B 랜드마크 도장: 선 칸이 아직 안 찍은 유물 전설 장소면 늘 찍는다(UseAbility, Target = 고정물 — 날씨 바꾸기는 Target < 0)
                bool tourist = TrainerRules.Has(s, ctx, c.Seat, "tourist", AbilityColor.Blue);
                var stamp = tourist ? legal.Find(a => a.Kind == AnswerKind.UseAbility && a.Target >= 0) : null;
                if (stamp != null) return stamp.Clone();
                int tour = tourist ? TourCell(s, ctx, me) : -1;
                // ⑪ 맵 행동의 능력(UseAbility — 맵 상호작용 · 날기 · 길 · 날씨 · 도장): 턴에 한 번까지, 모으기 1/2 · 사냥 1/4
                var abilities = legal.FindAll(a => a.Kind == AnswerKind.UseAbility);
                if (abilities.Count > 0 && (!mapAbilityTurn.TryGetValue(c.Seat, out int turn) || turn != s.TurnId)
                    && rng.Next(s.Round > explore && PartySize(me) >= me.OpenSlots ? 4 : 2) == 0)
                {
                    mapAbilityTurn[c.Seat] = s.TurnId;
                    return abilities[rng.Next(abilities.Count)].Clone();
                }
                var evolve = legal.Find(a => a.Kind == AnswerKind.Evolve && a.Arg == 1) ?? legal.Find(a => a.Kind == AnswerKind.Evolve && a.Arg != TrainerRules.EggArg);
                if (evolve != null) return evolve.Clone(); // ⑧ 진화할 수 있으면 먼저(우러난 스튜 반값부터)
                bool keepForOffer = me.ActProgress == 2 && !me.Heart;
                if (me.ActProgress >= 1 && !keepForOffer && Unheld(s, ctx, me) && rng.Next(2) == 0)
                {
                    ChoiceAnswer held = null;
                    foreach (string key in HeldOrder)
                        if ((held = legal.Find(a => a.Kind == AnswerKind.Craft && ctx.Items.Items[a.Target].Key == key)) != null) break;
                    if (held != null) return held.Clone(); // ⑨ 1막 뒤: 아무것도 안 지닌 포켓몬이 있으면 지닌물건을 만든다
                }
                var ball = legal.FindLast(a => a.Kind == AnswerKind.Craft && CraftRules.BallOf(ctx, a.Target) >= 0); // 데이터 순서의 마지막 = 가장 좋은 볼
                if (ball != null && !(me.ActProgress == 2 && !me.Heart) && rng.Next(2) == 0) return ball.Clone(); // 사당에 바칠 규토리는 남긴다
                if (s.Ub != null) return UbMap(s, ctx, c, me, rng); // M6 「봇 — UB 판」(PlayoutUb)
                var galactic = legal.Find(a => a.Kind == AnswerKind.Challenge && a.Target == SeatId.Galactic);
                bool hunt = s.Round > explore && (PartySize(me) >= me.OpenSlots || s.Round > 40 + explore);
                // ④ 1막 뒤: 바칠 규토리가 없으면 모으기처럼 돈다
                int goal = hunt && s.Hos != null && me.ActProgress >= 1 ? GoalCell(s, ctx, me) : -1;
                if (goal >= 0 && me.ActProgress == 2 && !me.Heart && Apricorns(me) == 0) hunt = false;
                if (goal >= 0 && !Ready(s, ctx, c.Seat, goal)) hunt = false; // ⑩ 간부·태홍에게 아직 약하면 모으기처럼 돈다(진화·포획·기술로 키운다)
                if (Prepared && hunt && goal < 0 && galactic != null && s.Hos != null && me.ActProgress == 0 && !ReadyFor(s, ctx, c.Seat, GalacticTrainer.MaleGrunt)) hunt = false; // ⑭ 조무래기도
                if (!hunt)
                {
                    if (Prepared && galactic != null && Hurt(s, ctx, me))
                    {
                        var rest = legal.Find(a => a.Kind == AnswerKind.VillageHeal); // ⑭ 미루는 동안 다쳤으면 마을 회복부터
                        if (rest != null) return rest.Clone();
                    }
                    if (galactic != null) legal.Remove(galactic);
                    var toward = tour >= 0 && rng.Next(2) == 0 ? Toward(s, ctx, me, tour) : null; // ⑫ tourist B: 모으는 동안 절반은 가장 가까운 안 찍은 랜드마크로
                    if (toward != null) return toward;
                    var search = legal.Find(a => a.Kind == AnswerKind.Search);
                    if (search != null && rng.Next(2) == 0) return search.Clone();
                    return legal[rng.Next(legal.Count)].Clone();
                }
                if (goal >= 0)
                {
                    var heal = legal.Find(a => a.Kind == AnswerKind.VillageHeal);
                    if (heal != null && Hurt(s, ctx, me)) return heal.Clone();
                    if (HpFraction(s, ctx, me) < 0.8)
                    {
                        // ⑩ 다친 채 간부·태홍·신에게 가지 않는다: 회복스튜를 만들 수 있으면 만들고, 아니면 고향 마을로 가서 회복
                        var stew = legal.Find(a => a.Kind == AnswerKind.Craft && ctx.Items.Items[a.Target].Key == CraftRules.RestoringStew);
                        if (stew != null) return stew.Clone();
                        if (me.HomeVillage >= 0) goal = ctx.Fixtures.Fixtures[me.HomeVillage].Cell;
                    }
                    var offer = me.Cell == goal ? legal.Find(a => a.Kind == AnswerKind.Offer) : null; // 목표 칸(시작의 사당)에서만 — 다른 사당은 고르게 고를 때(M5 「사당 공물·카드」)
                    if (offer != null) return offer.Clone();
                    var god = me.Cell == goal ? legal.Find(a => a.Kind == AnswerKind.Search && a.Arg == HosFlow.OriginShrine(ctx)) : null; // 신 토큰이 심장 공물을 막으면 신 배틀(「천상의 균열」)
                    if (god != null && rng.Next(4) != 0) return god.Clone();
                    if (galactic != null)
                    {
                        if (rng.Next(4) != 0) return galactic.Clone();
                    }
                    else if (rng.Next(8) != 0)
                    {
                        var dist = DistTo(s, ctx, goal);
                        int here = Dist(dist, me.Cell);
                        if (here == 0) return ChoiceAnswer.Of(AnswerKind.EndTurn); // 목표 칸인데 이번 턴에는 할 수 없다(이동력 0) — 다음 턴에
                        ChoiceAnswer best = null;
                        int bestDist = here;
                        bool keep = me.ActProgress == 2 && !me.Heart && Apricorns(me) <= 1; // 사당에 바칠 것은 공중날기에 쓰지 않는다
                        foreach (var a in legal)
                        {
                            int d = a.Kind == AnswerKind.Move ? Dist(dist, a.Target)
                                : a.Kind == AnswerKind.Fly && !keep ? Dist(dist, ctx.Fixtures.Fixtures[a.Target].Cell) : int.MaxValue;
                            if (d >= bestDist) continue;
                            best = a;
                            bestDist = d;
                        }
                        if (best != null) return best.Clone();
                    }
                    return legal[rng.Next(legal.Count)].Clone();
                }
                var tourStep = tour >= 0 && rng.Next(2) == 0 ? Toward(s, ctx, me, tour) : null; // ⑫ 막 목표가 없을 때도 절반은 랜드마크로
                if (tourStep != null) return tourStep;
                if (galactic != null)
                {
                    var heal = legal.Find(a => a.Kind == AnswerKind.VillageHeal);
                    if (heal != null && Hurt(s, ctx, me)) return heal.Clone();
                    if (rng.Next(4) != 0) return galactic.Clone();
                }
                else if (me.HomeVillage >= 0 && rng.Next(4) != 0)
                {
                    var fly = legal.Find(a => a.Kind == AnswerKind.Fly && a.Target == me.HomeVillage);
                    if (fly != null) return fly.Clone();
                    var dist = DistTo(s, ctx, ctx.Fixtures.Fixtures[me.HomeVillage].Cell);
                    ChoiceAnswer best = null;
                    int bestDist = Dist(dist, me.Cell);
                    foreach (var a in legal)
                        if (a.Kind == AnswerKind.Move && Dist(dist, a.Target) < bestDist)
                        {
                            best = a;
                            bestDist = Dist(dist, a.Target);
                        }
                    if (best != null) return best.Clone();
                }
                return legal[rng.Next(legal.Count)].Clone();
            }

            /// <summary>⑫ tourist B: 아직 도장을 안 찍은 유물 전설 장소 가운데 가장 가까운 칸(걸어서 닿는 것). 없으면 −1.</summary>
            int TourCell(GameState s, Ctx ctx, SeatState me)
            {
                var marks = TrainerRules.Landmarks(ctx);
                int best = -1, bestDist = int.MaxValue;
                for (int i = 0; i < marks.Count; i++)
                {
                    if ((me.Tour & (1 << i)) != 0) continue;
                    int cell = ctx.Fixtures.Fixtures[marks[i]].Cell;
                    int d = Dist(DistTo(s, ctx, cell), me.Cell);
                    if (d >= bestDist) continue;
                    best = cell;
                    bestDist = d;
                }
                return best;
            }

            /// <summary>그 칸에 더 가까워지는 걷기·공중날기 답(가장 가까워지는 것). 이미 그 칸이거나 더 가까워질 답이 없으면 null.</summary>
            ChoiceAnswer Toward(GameState s, Ctx ctx, SeatState me, int cell)
            {
                var dist = DistTo(s, ctx, cell);
                int bestDist = Dist(dist, me.Cell);
                if (bestDist == 0) return null;
                ChoiceAnswer best = null;
                foreach (var a in legal)
                {
                    int d = a.Kind == AnswerKind.Move ? Dist(dist, a.Target) : a.Kind == AnswerKind.Fly ? Dist(dist, ctx.Fixtures.Fixtures[a.Target].Cell) : int.MaxValue;
                    if (d >= bestDist) continue;
                    best = a;
                    bestDist = d;
                }
                return best?.Clone();
            }

            int[] DistTo(GameState s, Ctx ctx, int cell)
            {
                if (!dists.TryGetValue(cell, out var dist))
                {
                    dist = MoveRules.Reachable(ctx.Board, cell, ctx.Board.Count, s.Options.ElevationMode);
                    dists[cell] = dist;
                }
                return dist;
            }

            /// <summary>1막 뒤의 목표 칸: 조각이 없는 가장 가까운 호수 → 조각 셋이면 시작의 사당 → 심장이 있으면 신오신전. 할 것이 없으면 −1.</summary>
            int GoalCell(GameState s, Ctx ctx, SeatState me)
            {
                if (me.ActProgress >= 3) return -1;
                if (me.ActProgress == 2) return s.Hos.Act < 3 ? -1 : me.Heart ? HosFlow.TempleCell(ctx) : HosFlow.OriginShrineCell(ctx);
                if (s.Hos.Act < 2) return -1;
                int best = -1, bestDist = int.MaxValue;
                for (int i = 0; i < SeatState.ShardCount; i++)
                {
                    if (me.Shards[i]) continue;
                    int cell = HosFlow.LakeCell(ctx, (Shard)i);
                    int d = Dist(DistTo(s, ctx, cell), me.Cell);
                    if (d >= bestDist) continue;
                    best = cell;
                    bestDist = d;
                }
                return best;
            }

            static int Apricorns(SeatState seat)
            {
                int n = 0;
                foreach (int a in seat.Apricorns) n += a;
                return n;
            }

            static bool Hurt(GameState s, Ctx ctx, SeatState seat)
            {
                foreach (int p in seat.Party)
                    if (p >= 0 && (s.Pokemon[p].Damage > 0 || s.Pokemon[p].Status != StatusEffect.None)) return true;
                return false;
            }

            static int Dist(int[] dist, int cell) => cell >= 0 && dist[cell] != MoveRules.Unreachable ? dist[cell] : int.MaxValue;
        }

        /// <summary>세션 판의 좌석 AI: 같은 정책을 그 좌석 보기로 돈다. 1/10 확률로 Default를 낸다(시간 초과 대신 — 좌석 AI에는 시계가 없다).</summary>
        public sealed class BotAgent : IAgent
        {
            readonly Bot bot;

            public BotAgent(ulong seed) => bot = new Bot(seed);

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                var rng = new Pcg32(budget.Seed, 0xB07);
                var c = input.Choice;
                if (rng.Next(10) == 0) return new AgentDecision(c.Default.Clone(), null);
                return new AgentDecision(bot.Answer(input.View, input.Ctx, c, rng), null);
            }
        }
    }
}
