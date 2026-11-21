using System;
using System.Collections.Generic;
using Sinnoh.Rules.Session;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>
    /// 각본 진행기(07 §6-4): 정해진 판으로 세션을 열고(<see cref="TutorialDeal"/> · 거르개 · 봇 붙잡기) 단계를 하나씩 넘긴다. 좌석 0 = 나.
    /// 화면은 단계를 보여 주고 사람의 명령을 세션에 보낸다 — 거르개(<see cref="Allows"/>)가 그 단계 `allow` 밖을 막는다.
    /// 자동 진행(<see cref="Play"/>·<see cref="PlayToEnd"/>)은 시험·빨리감기의 길이다: 단계의 `auto`를 내 좌석으로 내고, bot 단계는 봇 답을 풀어 주고,
    /// 가상 응답자(야생·갤럭시단) 답은 정해진 시각까지 세션을 돌린다. 단계 끝마다 `expect`를 엔진 값과 맞대 보고 어긋남을 <see cref="Failures"/>에 모은다.
    /// 판은 저장하지 않는다(결정 90 Q6 — SessionConfig.Store 없음).
    /// 막 앞 판(<see cref="TutorialStart.After"/>, 07 T25): 셋업이 끝난 뒤 첫 단계 끝에 호스트 사본을 손보기 함수로 고쳐 그 상태로 세션을 다시 연다(<c>GameFlow.Rebase</c>와 같은 꼴).
    /// 함수는 Rules 밖(Ai 막 길)이라 조립 지점이 이름으로 찾아 꽂는다(생성자의 afters).
    /// </summary>
    public sealed class TutorialRunner
    {
        public const int Me = 0;

        readonly Ctx ctx;
        readonly TutorialScript script;
        readonly IAgent virtualAgent;
        readonly Func<string, Action<Ctx, GameState>> afters;
        // 화면 쪽(07 §9-1): 새 세션(과 그 시각)에 들어가 내 좌석 손님을 돌려준다 · 화면 시계 · 화면 시계를 진행기 시계까지 당기기(Hand — 07 T61). null = 진행기 혼자(시험·빨리감기)
        Func<LocalSession, long, SeatClient> attach;
        Func<long> clock;
        Action<long> catchUp;
        ScriptAgent[] bots;
        SessionConfig cfg;
        Action<Ctx, GameState> after;
        bool demoing;
        long now;
        // 화면 쪽 진행(07 §9-1): 거르개를 지난 내 명령 앞의 명령 수(-1 = 없음) · 이 단계 bot의 목표 답 수
        int armed = -1, botTarget;
        DiceRolled lastRoll;
        CaptureAttempt lastCapture;
        int? lastMatchup, lastDamage;   // 내 쪽 마지막 공격의 상성 값 · 최종 데미지(결정 104 ② — expect `matchup`·`damage`)
        bool myRoll;

        public LocalSession Session { get; private set; }
        public SeatClient Client { get; private set; }
        public TutorialDeal Deal { get; private set; }
        /// <summary>지금 단계 번호(끝나면 Steps.Count).</summary>
        public int Index { get; private set; }
        public TutorialStep Step => Index >= 0 && Index < script.Steps.Count ? script.Steps[Index] : null;
        public bool Finished => Session != null && Index >= script.Steps.Count;
        /// <summary>대조 어긋남(단계 id: 키 기대 ≠ 실제). 비어야 통과.</summary>
        public readonly List<string> Failures = new List<string>();
        /// <summary>거르개가 막은 내 명령 수.</summary>
        public int Blocked { get; private set; }
        /// <summary>막 앞 판 손보기를 걸었나(After가 없는 각본은 늘 false).</summary>
        public bool Rebased { get; private set; }

        /// <summary>
        /// 화면 쪽(07 §9-1): 이 단계에서 받아들여진 내 지정 행동 수(거르개를 지나 엔진이 적용한 것). Command 단계는 <c>Auto.Count</c>개를 받으면 끝난 것으로 본다 —
        /// 자동 진행이 내는 답 수와 같다.
        /// </summary>
        public int Taken { get; private set; }

        /// <summary>화면 쪽 bot 단계: 이 단계의 봇 답을 다 냈다(<see cref="ReleaseBot"/> 뒤 세션이 화면 시각으로 돈다).</summary>
        public bool BotDone
        {
            get
            {
                var step = Step;
                var bot = step != null && step.Advance == StepAdvance.Bot ? Bot(step.BotSeat) : null;
                return bot != null && bot.Consumed >= botTarget;
            }
        }

        /// <param name="afters">막 앞 판 손보기 이름 → 함수(모르는 이름은 null). 각본에 <see cref="TutorialStart.After"/>가 있을 때만 쓴다.</param>
        /// <param name="attach">
        /// 화면 쪽(07 §9-1): 새 세션(처음 · 막 앞 판으로 다시 연 판)에 들어가 내 좌석 손님을 돌려준다 — 화면 흐름이 세션에 들어가며 만든 손님을 진행기도 같이 쓴다
        /// (링크는 좌석마다 손님 하나). null이면 진행기가 손님을 만든다(시험·빨리감기). 돌려받은 손님은 닫지 않는다(화면 흐름의 것).
        /// </param>
        public TutorialRunner(Ctx ctx, TutorialScript script, IAgent virtualAgent, Func<string, Action<Ctx, GameState>> afters = null, Func<LocalSession, SeatClient> attach = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.script = script ?? throw new ArgumentNullException(nameof(script));
            this.virtualAgent = virtualAgent ?? throw new ArgumentNullException(nameof(virtualAgent));
            this.afters = afters;
            if (attach != null) this.attach = (session, _) => attach(session);
        }

        /// <summary>
        /// 화면에 넘긴다(07 T61 — 목차·장 고르기·건너뛰기가 연 판에 튜토리얼 층이 붙는 한 길): 지금 판(빨리감기로 보낸 국면 — 호스트 사본)을 같은 설정(손본 판·거르개·봇 붙잡기)으로
        /// 화면 시각 <paramref name="startMs"/>에 새로 열고(<see cref="Reclock"/>와 같은 꼴 — 저장 없음), 진행기가 만든 손님은 닫고 화면 흐름의 손님을 같이 쓴다(<paramref name="attach"/> — 세션과 그 시각).
        /// 그 뒤로는 진행기 시계가 화면 시계(<paramref name="clock"/>) 밑으로 내려가지 않고, 진행기가 세션을 앞으로 돌리면(단계 끝 <see cref="Finish"/>의 정해진 일 · demo 답)
        /// 화면 시계를 그 시각까지 당긴다(<paramref name="catchUp"/>) — 세션이 화면 시계보다 앞서 다음 가상 응답이 그만큼 늦어지지 않게. 막 앞 판으로 다시 열 때도 같은 손님 길.
        /// </summary>
        public void Hand(Func<LocalSession, long, SeatClient> attach, Func<long> clock, Action<long> catchUp, long startMs = 0)
        {
            if (Session == null) throw new InvalidOperationException("아직 시작하지 않았다");
            if (this.attach != null) throw new InvalidOperationException("이미 화면에 넘겼다");
            this.attach = attach ?? throw new ArgumentNullException(nameof(attach));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.catchUp = catchUp ?? throw new ArgumentNullException(nameof(catchUp));
            Drain();
            var s = Session.HostCopy();
            Deal.Rebase(s);
            Client.Updated -= Watch;
            Client.Rejected -= Count;
            Client.Close();
            Client = null;
            now = startMs;
            Open(LocalSession.FromState(ctx, s, cfg, startMs), startMs);
        }

        // 화면 쪽: 진행기 시계를 화면 시계 밑으로 두지 않는다 · 앞섰으면 화면 시계를 당긴다(07 T61)
        void Pull()
        {
            if (clock != null) now = Math.Max(now, clock());
        }

        void Push()
        {
            if (catchUp != null && now > clock()) catchUp(now);
        }

        /// <summary>각본(화면이 대사·앵커를 읽는다).</summary>
        public TutorialScript Script => script;

        public ScriptAgent Bot(int seat) => bots != null && seat >= 0 && seat < bots.Length ? bots[seat] : null;

        /// <summary>판을 차리고 첫 단계에 들어간다.</summary>
        public void Begin()
        {
            if (Session != null) throw new InvalidOperationException("이미 시작했다");
            var st = script.Start;
            if (st.After != null)
                after = afters?.Invoke(st.After) ?? throw new ArgumentException($"{script.Id}: 막 앞 판 손보기 「{st.After}」를 꽂지 않았다(조립 지점의 afters)");
            Deal = st.BuildDeal(ctx);
            var agents = new IAgent[st.Seats];
            bots = new ScriptAgent[st.Seats];
            if (script.Bots != null && script.Bots.Length != st.Seats) throw new ArgumentException($"봇 큐 {script.Bots.Length}좌석 ≠ 인원 {st.Seats}");
            for (int i = 0; i < st.Seats; i++)
            {
                var q = script.Bots?[i];
                if (i == Me)
                {
                    if (q != null) throw new ArgumentException("좌석 0(나)에는 봇 큐를 두지 않는다");
                    continue;
                }
                bots[i] = new ScriptAgent(i, q ?? throw new ArgumentException($"좌석 {i}: 봇 각본 큐가 없다"));
                agents[i] = bots[i];
            }
            cfg = new SessionConfig
            {
                VirtualAgent = virtualAgent,
                SeatAgents = st.Seats > 1 ? agents : null,
                Rig = Deal,
                Allow = Allows,
                Hold = seat => bots[seat] != null && bots[seat].Waiting,
            };
            Index = -1;
            Open(LocalSession.Start(ctx, st.Options, st.Seats, st.Seed, st.Campaign, cfg, 0), 0);
            Enter(0);
        }

        void Open(LocalSession session, long atMs)
        {
            if (Client != null)
            {
                Client.Updated -= Watch;
                Client.Rejected -= Count;
                if (attach == null) Client.Close();
            }
            Session = session;
            Client = attach != null ? attach(session, atMs) ?? throw new InvalidOperationException("화면 쪽이 세션의 손님을 주지 않았다") : new SeatClient(Session.Link, ctx, Me);
            armed = -1;
            Client.Updated += Watch;
            Client.Rejected += Count;
        }

        void Count(RejectedMsg r)
        {
            if (r.Reason == RejectReason.Tutorial) Blocked++;
            else armed = -1;   // 거르개는 지났지만 엔진이 거절했다
        }

        /// <summary>막 앞 판(07 T25): 셋업이 끝난 판의 호스트 사본을 손봐 같은 설정(손본 판·거르개·봇 붙잡기)으로 다시 연다. 저장하지 않는다.</summary>
        void Rebase()
        {
            var s = Session.HostCopy();
            after(ctx, s);
            Rebased = true;
            Deal.Rebase(s);
            now = Math.Max(now, Session.AnimEndMs);
            Open(LocalSession.FromState(ctx, s, cfg, now), now);
        }

        void Watch(GameUpdate u)
        {
            if (armed >= 0 && Session.CommandCount > armed)
            {
                Taken++;
                armed = -1;
            }
            foreach (var e in u.Events)
            {
                if (e is DiceRolled d && d.Kind == DiceKind.Attack)   // 판정 앞 포획 굴림(결정 188)은 공격 굴림이 아니다
                {
                    myRoll = d.Side == 0;
                    if (myRoll)
                    {
                        lastRoll = d;
                        lastMatchup = lastDamage = null;
                    }
                }
                else if (e is TypeMatchup t && myRoll) lastMatchup = t.Delta;
                else if (e is DamageBreakdown b && myRoll) lastDamage = b.Final;
                else if (e is CaptureAttempt c) lastCapture = c;
            }
        }

        void Enter(int i)
        {
            Index = i;
            // 굴림 값은 지정 행동 단계에서 일어나고 뒤따르는 [다음] 단계까지 남는다(굴린 뒤에 값을 말하는 대사 — 07 T192)
            if (Step == null || Step.Advance != StepAdvance.Next)
            {
                lastRoll = null;
                lastCapture = null;
                lastMatchup = lastDamage = null;
            }
            Taken = 0;
            armed = -1;
            var step = Step;
            botTarget = step != null && step.Advance == StepAdvance.Bot ? (Bot(step.BotSeat)?.Consumed ?? 0) + step.BotAnswers : 0;
            if (step != null && step.Dice.Count > 0) Deal.Force(step.Dice.ToArray());
        }

        /// <summary>
        /// 화면 쪽 bot 단계(07 §9-1): 이 단계의 봇 답을 풀어 준다. 세션은 화면 흐름의 시각으로 돌므로 여기서 돌리지 않는다 — 다 냈는지는 <see cref="BotDone"/>.
        /// 자동 진행은 <see cref="Play"/>(풀고 바로 돌린다).
        /// </summary>
        public void ReleaseBot()
        {
            var step = Step;
            if (step == null || step.Advance != StepAdvance.Bot) throw new InvalidOperationException("bot 단계가 아니다");
            var bot = Bot(step.BotSeat) ?? throw new InvalidOperationException($"{step.Id}: 봇 좌석 {step.BotSeat}가 없다");
            bot.Released = Math.Max(bot.Released, botTarget);
        }

        /// <summary>
        /// 거르개(07 §6-3): 내 좌석 명령이 이 단계 `allow`에 맞나. 봇·다른 좌석은 통과(세션이 AI 좌석 명령을 받지 않는다). Free 단계는 모두 통과,
        /// Next·Bot·Screen 단계와 끝난 뒤에는 모두 막는다. Demo 단계는 진행기가 `auto`를 내는 동안 그 표기만 통과한다(사람 명령은 막는다).
        /// 맞아도 엔진이 다시 검증한다(거르개는 합법 답의 부분집합만 남긴다).
        /// </summary>
        public bool Allows(int seat, Command cmd)
        {
            if (seat != Me) return true;
            var step = Step;
            if (step == null || cmd == null) return false;
            switch (step.Advance)
            {
                case StepAdvance.Free: return true;
                case StepAdvance.Command:
                    if (!Any(step.Allow, cmd)) return false;
                    armed = Session.CommandCount;   // 엔진이 적용하면 갱신에서 센다(Taken)
                    return true;
                case StepAdvance.Demo: return demoing && Any(step.Auto, cmd);
                default: return false;
            }
        }

        bool Any(List<AnswerPattern> patterns, Command cmd)
        {
            foreach (var p in patterns)
            {
                if (cmd.Kind == CommandKind.Anytime) { if (p.Matches(cmd.Maint, Client?.View?.Seats[Me].Party)) return true; }
                else if (cmd.Kind == CommandKind.Answer && p.Matches(MyChoice(cmd.ChoiceId), cmd.Answer)) return true;
            }
            return false;
        }

        PendingChoice MyChoice(int id)
        {
            var v = Client?.View;
            if (v == null) return null;
            foreach (var c in v.Pending)
                if (c.Id == id) return c;
            return null;
        }

        /// <summary>지금 단계를 자동으로 끝내고 다음 단계로(07 §6-4 빨리감기와 같은 길). Demo 단계는 화면도 이것을 부른다(마박사가 대신 둔다).</summary>
        public void Play()
        {
            var step = Step ?? throw new InvalidOperationException("각본이 끝났다");
            Pull();
            try { PlayStep(step); }
            finally { Push(); }
        }

        void PlayStep(TutorialStep step)
        {
            switch (step.Advance)
            {
                case StepAdvance.Command:
                case StepAdvance.Demo:
                    if (step.Auto.Count == 0) throw new InvalidOperationException($"{step.Id}: {step.Advance} 단계에 auto가 없다");
                    demoing = step.Advance == StepAdvance.Demo;
                    try
                    {
                        foreach (var p in step.Auto) Answer(step, p);
                    }
                    finally { demoing = false; }
                    break;
                case StepAdvance.Bot:
                {
                    var bot = Bot(step.BotSeat) ?? throw new InvalidOperationException($"{step.Id}: 봇 좌석 {step.BotSeat}가 없다");
                    // 목표는 단계에 들어갈 때 센 값(화면 쪽이 먼저 풀어 봇이 일부 답했어도 같다)
                    bot.Released = Math.Max(bot.Released, botTarget);
                    Drain();
                    if (bot.Consumed != botTarget) throw new InvalidOperationException($"{step.Id}: 봇 좌석 {step.BotSeat}가 답 {step.BotAnswers}개를 내지 못했다({bot.Consumed - (botTarget - step.BotAnswers)}개)");
                    break;
                }
            }
            Finish();
        }

        /// <summary>
        /// 빨리감기(07 §2-2 · §6-4): 판이 결정적이라 앞 단계의 명령을 자동 진행과 같은 길로 보내면 그 국면이 된다(국면 스냅샷을 두지 않는다).
        /// 시작하지 않았으면 판을 차리고, 지금 단계가 <paramref name="go"/>를 만족하는 동안 <see cref="Play"/>한다 — 본편 장 고르기 = 「그 장보다 앞이면」,
        /// [이 장 건너뛰기] = 「그 장이면」(화면은 연출 없이 끝 국면만 받는다: 보낸 뒤 <see cref="Reclock"/>로 새로 열어 화면 흐름에 넘긴다).
        /// </summary>
        public void PlayWhile(Func<TutorialStep, bool> go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (Session == null) Begin();
            for (int guard = 0; !Finished && go(Step); guard++)
            {
                if (guard > 10000) throw new InvalidOperationException("빨리감기가 끝나지 않는다");
                Play();
            }
        }

        /// <summary>
        /// 화면 흐름에 넘기기 전에 세션 시각을 <paramref name="nowMs"/>로 다시 맞춘다: 지금 판(호스트 사본)을 같은 설정(손본 판·거르개·봇 붙잡기)으로 그 시각에 새로 연다(저장 없음 —
        /// 막 앞 판 손보기와 같은 꼴). 빨리감기는 연출 시각만큼 시계를 앞으로 보내 두므로, 0부터 흐르는 화면 시계에 그대로 넘기면 다음 가상 응답이 그만큼 늦어진다.
        /// 판(상태 해시)·단계·주사위 대본은 그대로다.
        /// </summary>
        public void Reclock(long nowMs)
        {
            if (Session == null) throw new InvalidOperationException("아직 시작하지 않았다");
            Drain();
            var s = Session.HostCopy();
            Deal.Rebase(s);
            now = nowMs;
            Open(LocalSession.FromState(ctx, s, cfg, nowMs), nowMs);
        }

        /// <summary>남은 단계를 모두 자동으로.</summary>
        public void PlayToEnd()
        {
            if (Session == null) Begin();
            for (int guard = 0; !Finished; guard++)
            {
                if (guard > 10000) throw new InvalidOperationException("각본이 끝나지 않는다");
                Play();
            }
        }

        /// <summary>
        /// 화면 쪽: 지금 단계를 끝냈다고 알린다(Next의 [다음] · Command 지정 행동을 받은 뒤 · Free · Screen 그 화면이 열림). 대조하고 다음 단계로.
        /// 막 앞 판 손보기가 남았고 셋업이 끝났으면 대조 전에 건다(그 단계의 expect는 손본 판의 값).
        /// </summary>
        public void Finish()
        {
            var step = Step ?? throw new InvalidOperationException("각본이 끝났다");
            Pull();
            try { FinishStep(step); }
            finally { Push(); }
        }

        void FinishStep(TutorialStep step)
        {
            Drain();
            if (after != null && !Rebased && Session.HostCopy().Phase != Phase.Setup)
            {
                Rebase();
                Drain();
            }
            Check(step);
            if (Deal.DiceLeft > 0)
            {
                Failures.Add($"{step.Id}: 주사위 대본 {Deal.DiceLeft}개가 남았다");
                Deal.ClearDice();
            }
            Enter(Index + 1);
        }

        void Answer(TutorialStep step, AnswerPattern p)
        {
            Drain();
            var v = Client.View;
            now = Math.Max(now, Session.AnimEndMs);
            if (p.IsMaint)
            {
                var m = p.ToMaintenance(v.Seats[Me].Party);
                var mr = Client.Maintain(m, now);
                if (!mr.Accepted) throw new InvalidOperationException($"{step.Id}: 자동 정비 {p}이 거절됐다({mr.Reason})");
                return;
            }
            foreach (var c in v.Pending)
            {
                if (c.Seat != Me || c.Answered) continue;
                var a = p.First(c, Client.Legal(c)) ?? Proposal(c, p);
                if (a == null) continue;
                var r = Client.Answer(c, a, now);
                if (!r.Accepted) throw new InvalidOperationException($"{step.Id}: 자동 답 {a}이 거절됐다({r.Reason}) — allow가 auto를 덮지 않는다");
                return;
            }
            var open = new List<string>();
            foreach (var c in v.Pending)
                if (c.Seat == Me && !c.Answered) open.Add(c.Kind.ToString());
            throw new InvalidOperationException($"{step.Id}: 자동 답 {p}에 맞는 내 선택이 없다(열린 것: {string.Join(",", open)})");
        }

        /// <summary>
        /// 거래 제안은 합법 답에 늘어놓지 않는다(내용이 끝없다 — rulings A29, ChoiceRules.IsLegal이 TradeRules.OfferAllowed로 본다). 그래서 맵 행동의 ProposeTrade 표기는
        /// 상대(Target)와 거래 내용(Values — 넷씩 [주는 쪽, TradeKind, Key, Count])을 다 적었을 때 그대로 답을 만든다(확장 장 E10). 엔진이 다시 검증한다.
        /// </summary>
        static ChoiceAnswer Proposal(PendingChoice c, AnswerPattern p)
        {
            if (c.Kind != ChoiceKind.MapAction || p.Kind != AnswerKind.ProposeTrade || !p.Target.HasValue || p.Values == null || (p.Arg.HasValue && p.Arg.Value != -1)) return null;
            if (p.Choice.HasValue && p.Choice.Value != ChoiceKind.MapAction) return null;
            var a = ChoiceAnswer.Of(AnswerKind.ProposeTrade, p.Target.Value);
            a.Values.AddRange(p.Values);
            return a;
        }

        /// <summary>가상 응답자·풀어 준 봇 답이 정해진 대로 세션을 돌린다(붙잡힌 봇 답은 NextDueMs에 없다).</summary>
        void Drain()
        {
            for (int guard = 0; ; guard++)
            {
                if (guard > 100000) throw new InvalidOperationException("세션이 멈추지 않는다");
                long t = Session.NextDueMs;
                if (t == long.MaxValue) return;
                now = Math.Max(now, t);
                Session.Tick(now);
            }
        }

        void Check(TutorialStep step)
        {
            if (step.Expect.Count == 0) return;
            var host = Session.HostCopy();
            foreach (var e in step.Expect)
            {
                int? got;
                try { got = Value(host, e.Key); }
                catch (Exception x) when (x is InvalidOperationException || x is ArgumentException)
                {
                    Failures.Add($"{step.Id}: {e.Key} 읽기 실패 — {x.Message}");
                    continue;
                }
                if (got == null) Failures.Add($"{step.Id}: {e.Key} — 이 단계에 그 굴림이 없다");
                else if (got.Value != e.Value) Failures.Add($"{step.Id}: {e.Key} 기대 {e.Value} ≠ 엔진 {got.Value}");
            }
        }

        int? Value(GameState host, string key)
        {
            switch (key)
            {
                case "power": return lastRoll?.Power;
                case "diceSum": return lastRoll?.Sum;
                case "matchup": return lastMatchup;
                case "damage": return lastDamage;
                case "captureRate": return lastCapture?.CatchRate;
                case "captureSum": return lastCapture?.Sum;
                default: return TutorialExpect.Read(host, ctx, Me, key);
            }
        }
    }
}
