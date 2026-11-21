using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>LocalSession 설정. 호스트 조립 지점(Client·시험)이 채운다 — Rules는 Ai를 모르므로 AI 구현을 여기로 꽂는다(01 §1).</summary>
    public sealed class SessionConfig
    {
        /// <summary>가상 응답자(갤럭시단·UB·야생)의 선택에 답하는 AI(필수). 솔로는 조종이 AI 고정이다(02 §3-3).</summary>
        public IAgent VirtualAgent;
        /// <summary>좌석별 AI(null = 사람, 링크로 답한다). 솔로는 비운다(핫시트·AI 트레이너 없음, 04 U8) — 시험·봇 플레이아웃이 쓴다.</summary>
        public IAgent[] SeatAgents;
        /// <summary>세이브(null = 저장 안 함). Data SaveStore.</summary>
        public ISaveStore Store;
        /// <summary>데이터 해시(manifest contentHash). 머리에 적고 이어하기 때 비교한다.</summary>
        public string ContentHash;
        public string SaveId;
        public AiSettings Ai = new AiSettings();
        /// <summary>사람 좌석 선택의 시계(ClockKind 색인, ms, 0 = 그 종류 없음). null = 시계 없음(솔로, 02 §1-3). TurnClock 5종·뱅크는 M7.</summary>
        public int[] ClockMs;
        /// <summary>저장 시각을 주는 시계(UTC, 1970년부터의 ms — 머리 요약의 SavedAtMs). null = 적지 않음(0). 엔진 밖 값이라 상태·해시에 들지 않는다.</summary>
        public Func<long> SavedAt;
        /// <summary>좌석마다의 주인공 키(새 게임의 머리에 적는다 — 겉모양뿐). null = 적지 않음.</summary>
        public string[] Protagonists;
        /// <summary>판 손보기(각본 튜토리얼의 정해진 판·주사위 대본, 07 §6-1·§6-2). null = 평소 판. 세이브와 함께 쓸 수 없다(다시 적용하면 같은 판이 안 된다).</summary>
        public ISessionRig Rig;
        /// <summary>사람 좌석 명령 거르개(07 §6-3): false면 엔진에 보내지 않고 <see cref="RejectReason.Tutorial"/>. null = 모두 통과. AI 좌석·세션이 내는 답은 거르지 않는다.</summary>
        public Func<int, Command, bool> Allow;
        /// <summary>AI 좌석 붙잡기(07 §6-4): true인 좌석의 AI 답은 풀어 줄 때까지 기다린다(봇 각본이 대사보다 앞서지 않게). 가상 응답자는 붙잡지 않는다. null = 붙잡지 않음.</summary>
        public Func<int, bool> Hold;
        /// <summary>좌석에 보내는 곳(M7 방 — <see cref="HostSession"/>이 좌석 → 연결로 옮기고 시계를 붙인다). null = 이 세션의 <see cref="LocalSession.Link"/>. 가상 좌석(음수)의 거절도 여기로 온다.</summary>
        public ISessionLink Out;
        /// <summary>
        /// 가상 응답자(갤럭시단·UB)를 지금 사람이 조종하나(02 §5-6, 결정 29 ④ — M7 방). true면 선택이 열릴 때 AI 답을 잡지 않고 그 가상 좌석 값으로 낸 답(<see cref="LocalSession.Submit"/>)을 받는다.
        /// 답이 없을 때 AI로 넘기는 일은 방(<see cref="HostSession"/>)이 맡는다. null = 늘 AI.
        /// </summary>
        public Func<int, bool> HumanVirtual;
        /// <summary>받아들인 명령·시작의 호스트 이벤트(걸러지기 전) — 방이 다시 열린 선택(ChoiceReissued)의 시계를 잇는 데 쓴다. 읽기만 한다.</summary>
        public Action<List<GameEvent>> Applied;
        /// <summary>머리(head.json)를 쓰기 바로 전에 부른다 — 방(<see cref="HostSession"/>)이 방 몫(<see cref="SaveHead.Room"/>: 방 설정·좌석 토큰·금지 토큰·뱅크)을 채운다. null = 그대로.</summary>
        public Action<SaveHead> BeforeHead;
        /// <summary>
        /// AI 답을 셈하는 러너(02 §1-2 — M7 방, <see cref="AgentWorker"/>). null = 정해진 시각에 이 스레드에서 바로 묻는다(솔로·튜토리얼·시험).
        /// 있으면 정해진 시각에 물음(<see cref="AgentRequest"/>)만 내고 답이 올 때까지 그 뒤의 정해진 일을 미룬다 — 답은 그 정해진 시각으로 넣어 러너가 없을 때와 같은 명령 열이 된다 [가정 M7-24].
        /// </summary>
        public IAgentRunner Runner;
        /// <summary>
        /// 그 숨은 손패 창(HandGated)을 묻지 않고 넘기나(04 §5 ① 「이번 배틀 묻지 않기」 — 방의 SetAutoPass): true면 쓸 카드가 없는 좌석처럼 최소 시간에 그 좌석의 「안 씀」으로 답한다.
        /// null = 넘기지 않음.
        /// </summary>
        public Func<PendingChoice, bool> AutoPass;
        /// <summary>
        /// 무르기 설정(결정 149, undo.md §1-7). 기본 끔 — 끈 판 · 튜토리얼(Rig · Allow가 있으면 늘 끔) · 시험은 지금과 같다. 머리(<see cref="SaveHead.Undo"/>)에 적고,
        /// 이어하기는 부르는 쪽이 머리 · 방 설정에서 넣는다. 규칙이 아니라 RuleOptions 밖이다.
        /// </summary>
        public UndoMode Undo;
    }

    /// <summary>세션이 거는 판 손보기(07 §6-1): 엔진을 부르는 동안 <see cref="Sinnoh.Rules.Rig"/>로 걸리고, 새 판을 만든 직후 Attach, 명령을 받아들일 때마다 Settle.</summary>
    public interface ISessionRig : IRig
    {
        /// <summary>NewGame 직후(셋업 Engine.Start 전) 호스트 원본에 붙는다 — NewGame이 이미 섞은 덱의 순서를 고칠 자리.</summary>
        void Attach(GameState host);

        /// <summary>Engine.Start·받아들인 명령 뒤, 갱신을 보내기 전(뒷면 숨겨진 장소 정체 맞추기처럼 놓인 뒤에야 고칠 수 있는 것).</summary>
        void Settle(GameState host);
    }

    /// <summary>
    /// 솔로 호스트(02 §1-3, M3 최소판): 엔진 원본 하나 + 좌석별 LoopbackLink 갱신 + 가상 응답자 AI + 연출 멈춤(PlayTiming) + 세이브.
    /// 방(M7)에서는 <see cref="HostSession"/>이 이것을 게임 핵으로 감싸고 로비·좌석 연결·TurnClock·일시정지·사람 조종자를 맡는다(SessionConfig.Out·HumanVirtual·Applied 갈고리) [가정 M7-1].
    /// 솔로·튜토리얼은 그대로 이것을 직접 쓴다. 시각은 Tick·Submit 인자(ms, 단조)로만 받는다.
    /// 시간 규칙(02 §5-7, rulings G15 아래 02 쪽 [가정]):
    /// ① 방장 멈춤 끝 = max(지금, 앞 끝) + 좌석별(걸러진 이벤트) PlayTiming 길이의 최댓값. 새 선택의 시계 시작 = 그 갱신 뒤 멈춤 끝.
    /// ② AI 답(가상 응답자·AI 좌석)은 시계 시작 시각에 적용한다(생각 시간을 연출 뒤에 숨김). 사람 답은 받는 즉시 적용한다(막기는 화면 몫).
    /// ③ 숨은 손패 반응 창(HandGated)은 시계 시작 + HiddenReactionMinMs 전에는 어떤 답도 적용하지 않는다. 쓸 카드가 없는 사람 좌석은 그 시각에
    ///    세션이 그 좌석의 「안 씀」으로 답한다 — 사람이 안 쓴 것과 같은 명령이라 남에게 가는 이벤트가 손패와 무관하다(01 §3-3).
    ///    울트라 버스트(협동)는 최소 시간이 없다 — 쓸 카드가 없는 좌석은 시계 시작에 바로 「안 씀」(결정 203, <see cref="HiddenMinFor"/>).
    /// ④ 시계(ClockMs)가 있으면 사람 좌석 선택은 시계 시작 + 길이에 Timeout. 솔로 기본은 없음.
    /// 정해진 일은 그 정해진 시각을 「지금」으로 적용한다 — Tick 간격과 무관하게 같은 명령 열·같은 멈춤이 나온다.
    /// 예외(02 §7, M3 검토): 엔진 예외나 로그 줄 쓰기 실패면 명령 전 상태로 되돌리고 던진다 — 로그에 없는 명령이 상태에 남지 않고, 정해진 일은 다음 Tick이 다시 한다.
    /// 스냅샷·머리 쓰기 실패는 로그의 캐시라 던지지 않고 Warnings에 적은 뒤 다음 명령 뒤에 다시 쓴다.
    /// 스냅샷은 쉬는 때(<see cref="SaveRest"/>)에만 찍는다: 셋업 끝·턴 끝·배틀 끝·게임 끝이 낀 명령 뒤의 첫 쉬는 때(배틀 뒤 선택이 열려 있으면 그 답 뒤).
    /// </summary>
    public sealed class LocalSession
    {
        enum DueKind { Agent, HiddenDecline, Timeout, Held }

        sealed class Due
        {
            public int ChoiceId;
            public DueKind Kind;
            public long At;
            public int Seat;
            public Command Held;
            /// <summary>사람 조종자에게서 넘겨받은 AI 물음(<see cref="RequestAi"/>) — 마감을 넘겨받은 때부터 센다.</summary>
            public bool Handed;
        }

        sealed class Opened
        {
            public int ChoiceId;
            public long ClockStart;
        }

        /// <summary>러너에 낸 물음(하나뿐 — 답이 올 때까지 뒤의 정해진 일을 미룬다). Count = 물을 때의 CommandCount(그 사이 판이 바뀌면 다시 묻는다).</summary>
        sealed class Asked
        {
            public Due Due;
            public int Serial;
            public int Count;
        }

        readonly Ctx ctx;
        readonly GameState state;
        readonly SessionConfig cfg;
        readonly SaveHead head;
        readonly List<SaveCheckpoint> checkpoints;
        readonly PlayTimingContext[] timing;
        readonly List<GameEvent> publicLog = new List<GameEvent>();
        readonly List<Due> due = new List<Due>();
        readonly List<Opened> opened = new List<Opened>();
        readonly LoopbackLink link = new LoopbackLink();
        /// <summary>명령 전 상태(재사용 버퍼, CopyFrom). 적용 도중 예외가 나면 되돌린다.</summary>
        readonly GameState backup = new GameState();
        readonly List<string> warnings = new List<string>();
        /// <summary>좌석마다 그 좌석 화면 대기열의 끝 · 차단 컷신(until-end) 끝(세션 시각 — 02 §5-7, 방의 E_HOLD). 갱신마다 셈 칸(seatMs·holdMs)을 다시 쓴다.</summary>
        readonly long[] seatEnd, holdEnd;
        readonly int[] seatMs, holdMs;
        long animEnd;
        int version;
        bool snapshotDue;
        Asked asked;
        int askSerial;

        // 무르기 책(undo.md §1-3 — UndoMode가 Off가 아닐 때만 쌓는다, 저장하지 않는다): 바닥 상태 + 그 뒤 받아들인 줄 빈틈없이 + 지금 턴의 첫 쉬는 때 사본(다시 적용 출발점)
        readonly List<UndoLine> book = new List<UndoLine>();
        GameState baseState;
        int baseCount, basePublic;
        bool baseRest;
        GameState turnBase;
        int turnBaseCount;
        /// <summary>책에 TurnStarted가 왔지만 아직 쉬는 때에 닿지 않았다(turnBase는 앞 턴의 것).</summary>
        bool turnPending;
        /// <summary>다음 Publish에만 싣는 되돌림 알림.</summary>
        RewindNote note;
        /// <summary>되돌려 버린 줄 가운데 AI 답 · 시간 초과(Seq → 책 줄, undo.md §1-6): 같은 자리 · 같은 상태 해시로 AI 차례가 오면 묻지 않고 그 줄을 낸다. 지나간 자리 · 갈라진 판에서는 버린다.</summary>
        readonly Dictionary<int, UndoLine> replay = new Dictionary<int, UndoLine>();

        LocalSession(Ctx ctx, GameState state, SessionConfig cfg, SaveHead head, List<SaveCheckpoint> checkpoints, long nowMs)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            if (cfg.VirtualAgent == null) throw new ArgumentException("VirtualAgent가 없다 — 가상 응답자 선택에 답할 AI를 꽂는다", nameof(cfg));
            if (cfg.SeatAgents != null && cfg.SeatAgents.Length != state.SeatCount) throw new ArgumentException("SeatAgents 길이 ≠ 좌석 수", nameof(cfg));
            if (state.Viewer != SeatId.HostViewer) throw new ArgumentException("호스트 원본이 아니다", nameof(state));
            if (cfg.Rig != null && cfg.Store != null) throw new ArgumentException("손본 판(Rig)은 저장하지 않는다 — 다시 적용하면 같은 판이 되지 않는다", nameof(cfg));
            this.head = head;
            this.checkpoints = checkpoints;
            timing = new PlayTimingContext[state.SeatCount];
            for (int i = 0; i < timing.Length; i++)
            {
                timing[i] = new PlayTimingContext(i);
                timing[i].From(state);
            }
            animEnd = nowMs;
            seatEnd = new long[state.SeatCount];
            holdEnd = new long[state.SeatCount];
            seatMs = new int[state.SeatCount];
            holdMs = new int[state.SeatCount];
            for (int i = 0; i < seatEnd.Length; i++) seatEnd[i] = holdEnd[i] = nowMs;
            link.Attach(this);
        }

        /// <summary>새 게임(NewGame + Start). 세이브가 있으면 머리와 게임 시작 스냅샷(CommandCount 0)을 쓴다.</summary>
        public static LocalSession Start(Ctx ctx, RuleOptions opt, int seatCount, ulong seed, Campaign campaign, SessionConfig cfg, long nowMs)
        {
            GameState s;
            var log = new EventLog();
            using (Rules.Rig.Use(cfg?.Rig))
            {
                s = Engine.NewGame(ctx, opt, seatCount, seed, campaign);
                cfg?.Rig?.Attach(s);
            }
            var session = new LocalSession(ctx, s, cfg, NewHead(ctx, s, seed, cfg), new List<SaveCheckpoint>(), nowMs);
            using (Rules.Rig.Use(cfg.Rig))
            {
                Engine.Start(s, ctx, log);
                cfg.Rig?.Settle(s);
            }
            session.WriteSnapshot();
            if (session.Booking) session.SetBook(s.Clone(), null);
            session.Publish(log.Events, nowMs);
            session.basePublic = session.publicLog.Count;
            return session;
        }

        /// <summary>이미 만든 상태(장면·시험)로 시작한다. 시드를 몰라 머리의 Seed는 0이고, 이어하기는 이 시작 스냅샷에서 한다.</summary>
        public static LocalSession FromState(Ctx ctx, GameState state, SessionConfig cfg, long nowMs)
        {
            var session = new LocalSession(ctx, state, cfg, NewHead(ctx, state, 0, cfg), new List<SaveCheckpoint>(), nowMs);
            session.WriteSnapshot();
            if (session.Booking) session.SetBook(state.Clone(), null);
            session.Publish(new List<GameEvent>(), nowMs);
            return session;
        }

        /// <summary>
        /// 이어하기(01 §10, 02 §7): 스냅샷 + 그 뒤 명령을 다시 적용한다(원래 적용과 같은 길로 — 이벤트를 만들고 턴 끝만 본다). 다시 적용한 명령이 거절되면 어긋남(InvalidOperationException).
        /// 데이터 확인: 머리의 IdTables가 지금 데이터의 키 목록과 표마다 같아야 하고, ContentHash가 둘 다 있으면 같아야 한다 — 다르면 NotSupportedException
        /// (IdTable로 색인을 다시 맞추는 일은 데이터가 처음 바뀔 때, 02 §7). 스냅샷은 호스트 원본이어야 한다(보기 사본이면 ArgumentException).
        /// 체크포인트(01 §10): 규칙 판이 같으면 스냅샷 상태의 해시를 같은 CommandCount의 체크포인트와 맞대 보고(JSON 왕복 손실도 잡는다), 다시 적용하며 턴 끝마다
        /// 있는 체크포인트는 맞대 보고 없는 것은 채운다(깨진 최신 스냅샷·스냅샷을 쓰기 전에 끊긴 판). 세이브에 이미 있는 줄은 다시 쓰지 않는다.
        /// </summary>
        /// <param name="toRest">로그의 마지막 쉬는 때(<see cref="SaveRest"/>)까지만 다시 적용한다 — 그 뒤의 줄(배틀·조우의 한가운데)은 버린다.
        /// 스냅샷부터 쉬는 때가 하나도 없으면 끝까지 다시 적용한다. 버린 줄 수는 <see cref="Rewound"/>. 줄이 어긋나면(거절 · Seq) 버리지 않고 던진다 — 파일은 그대로 둔다.</param>
        /// <remarks>세이브(cfg.Store)가 있으면 이어 받은 자리 뒤의 로그를 늘 잘라 낸다(<see cref="ISaveStore.TruncateLog"/>): 쉬는 때로 돌아가며 버린 줄, 그리고 읽을 때 버려진 줄
        /// (가운데가 깨진 줄·Seq가 끊긴 줄과 그 뒤 — 남겨 두면 새 줄이 그 뒤에 붙어 다음에 읽히지 않는다).</remarks>
        public static LocalSession Resume(Ctx ctx, SaveRecord record, SessionConfig cfg, long nowMs, bool toRest = false)
        {
            if (record?.Head == null || record.Snapshot?.State == null) throw new ArgumentException("머리·스냅샷이 없는 세이브", nameof(record));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            CheckHead(ctx, record.Head, cfg.ContentHash);
            var snap = record.Snapshot.State;
            if (snap.Viewer != SeatId.HostViewer || (snap.Rng.Increment & 1UL) == 0)
                throw new ArgumentException($"호스트 원본이 아닌 스냅샷이다(Viewer {snap.Viewer}, Rng 증분 {snap.Rng.Increment}) — 보기 사본은 이어 가지 않는다", nameof(record));
            var s = snap.Clone();
            if (s.CommandCount != record.Snapshot.CommandCount) throw new InvalidOperationException($"스냅샷 CommandCount {record.Snapshot.CommandCount} ≠ 상태 {s.CommandCount}");
            s.Options.FixedEffectOrder = record.Head.RulesVersion < SaveRecord.EffectOrderRulesVersion; // 결정 72 전의 판: 순서를 묻지 않는다(저장에 없는 값이라 머리의 판에서 셈)
            bool sameRules = record.Head.RulesVersion == SaveRecord.CurrentRulesVersion;
            var checkpoints = new List<SaveCheckpoint>(record.Snapshot.Checkpoints);
            if (sameRules) VerifyCheckpoint(checkpoints, s, "스냅샷");
            IReadOnlyList<SaveLogLine> lines = record.Commands;
            int keep = toRest ? LastRest(ctx, s, record.Commands) : lines.Count;
            if (keep < lines.Count) lines = record.Commands.GetRange(0, keep);
            bool booking = UndoOf(cfg) != UndoMode.Off;
            var floor = booking ? s.Clone() : null; // 책의 바닥 = 이어 받은 스냅샷(그 앞으로는 되돌리지 못한다 — undo.md §1-3 [가정 U1])
            var into = booking ? new List<UndoLine>() : null;
            ReapplyLines(ctx, s, lines, checkpoints, sameRules, into);
            cfg.Store?.TruncateLog(s.CommandCount);
            var session = new LocalSession(ctx, s, cfg, record.Head, checkpoints, nowMs);
            session.Rewound = record.Commands.Count - keep;
            session.SetBook(floor, into);
            session.Publish(new List<GameEvent>(), nowMs);
            return session;
        }

        /// <summary>
        /// 리플레이(01 §10): 시드와 첫 상태(NewGame + Start)에서 로그 줄을 모두 다시 적용하며 체크포인트마다 해시를 맞대 본다. 어긋나면 InvalidOperationException.
        /// Start로 시작한 세이브만 된다(FromState는 시드가 없다). 돌려주는 상태는 마지막 줄 뒤다. 받은 checkpoints는 고치지 않는다.
        /// </summary>
        public static GameState Replay(Ctx ctx, SaveHead head, IReadOnlyList<SaveLogLine> lines, IReadOnlyList<SaveCheckpoint> checkpoints)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            CheckHead(ctx, head, null);
            var s = Engine.NewGame(ctx, head.Options, head.SeatCount, head.Seed, head.Campaign);
            s.Options.FixedEffectOrder = head.RulesVersion < SaveRecord.EffectOrderRulesVersion;
            Engine.Start(s, ctx, null);
            var known = new List<SaveCheckpoint>();
            if (checkpoints != null) known.AddRange(checkpoints);
            ReapplyLines(ctx, s, lines, known, true);
            return s;
        }

        /// <summary>
        /// 그 상태에서 줄을 차례로 적용할 때 마지막 쉬는 때까지의 줄 수(사본에서 센다). 쉬는 때가 없으면 전부.
        /// 어긋난 줄(Seq가 맞지 않거나 거절)이 있으면 InvalidOperationException — 그 줄과 뒤의 성한 줄을 「쉬는 때 뒤」로 쳐서 조용히 버리지 않는다.
        /// </summary>
        static int LastRest(Ctx ctx, GameState from, List<SaveLogLine> lines)
        {
            var s = from.Clone();
            int keep = SaveRest.IsRest(s) ? 0 : -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Seq != s.CommandCount) throw new InvalidOperationException($"세이브 로그 Seq {lines[i].Seq} ≠ 지금 CommandCount {s.CommandCount}");
                var r = Engine.Submit(s, ctx, lines[i].Seat, lines[i].Command, null);
                if (!r.Accepted) throw new InvalidOperationException($"세이브 로그 {lines[i].Seq}번 명령이 거절됐다({r}) — 다시 적용이 어긋났다");
                if (SaveRest.IsRest(s)) keep = i + 1;
            }
            return keep < 0 ? lines.Count : keep;
        }

        /// <summary>그 머리를 이 빌드·데이터로 이어 갈 수 있나(형식 판 · 데이터 해시 · IdTables). 아니면 NotSupportedException. 방의 이어하기 대기(<see cref="HostSession.Resume"/>)가 손님을 받기 전에 먼저 본다.</summary>
        internal static void CheckHead(Ctx ctx, SaveHead head, string contentHash)
        {
            if (head.FormatVersion != SaveRecord.CurrentFormat) throw new NotSupportedException($"세이브 형식 {head.FormatVersion}은 이 빌드({SaveRecord.CurrentFormat})가 열지 못한다");
            if (contentHash != null && head.ContentHash != null && contentHash != head.ContentHash)
                throw new NotSupportedException($"데이터 해시가 다르다(세이브 {head.ContentHash} / 지금 {contentHash}) — IdTable 다시 맞추기는 아직 없다(02 §7)");
            string diff = SaveRecord.IdTablesDiff(head.IdTables, SaveRecord.IdTablesOf(ctx));
            if (diff != null) throw new NotSupportedException($"데이터 표가 세이브와 다르다({diff}) — IdTable 다시 맞추기는 아직 없다(02 §7)");
        }

        /// <summary>
        /// 로그 줄을 차례로 다시 적용한다. 턴 끝이면 그 CommandCount의 체크포인트를 맞대 보거나(verify이고 있으면) 채운다.
        /// into가 있으면 줄마다 무르기 책 줄(<see cref="UndoRules.LineOf"/>, PublicLog 0 — 이어 받은 판의 공개 기록은 이어 간 뒤부터다)을 덧붙인다.
        /// </summary>
        static void ReapplyLines(Ctx ctx, GameState s, IReadOnlyList<SaveLogLine> lines, List<SaveCheckpoint> checkpoints, bool verify, List<UndoLine> into = null)
        {
            var watch = new UndoWatch();
            foreach (var line in lines)
            {
                if (line.Seq != s.CommandCount) throw new InvalidOperationException($"세이브 로그 Seq {line.Seq} ≠ 지금 CommandCount {s.CommandCount}");
                watch.TurnEnded = false;
                var probe = into != null ? UndoRules.Probe(s) : null;
                ulong beforeHash = into != null && UndoRules.IsReplayable(line) ? StateHash.Of(s) : 0;
                if (into != null) watch.Events = new List<GameEvent>();
                var r = Engine.Submit(s, ctx, line.Seat, line.Command, watch);
                if (!r.Accepted) throw new InvalidOperationException($"세이브 로그 {line.Seq}번 명령이 거절됐다({r}) — 다시 적용이 어긋났다");
                if (into != null)
                {
                    var u = UndoRules.LineOf(line, probe, s, watch.Events, 0);
                    u.PublicLog = 0;
                    u.BeforeHash = beforeHash;
                    into.Add(u);
                }
                if (!watch.TurnEnded) continue;
                if (verify && VerifyCheckpoint(checkpoints, s, $"로그 {line.Seq}번 뒤")) continue;
                if (Find(checkpoints, s.CommandCount) == null) checkpoints.Add(new SaveCheckpoint { CommandCount = s.CommandCount, Hash = StateHash.Of(s) });
            }
        }

        /// <summary>그 CommandCount의 체크포인트가 있으면 해시를 맞대 본다(다르면 InvalidOperationException). 있었으면 true.</summary>
        static bool VerifyCheckpoint(List<SaveCheckpoint> checkpoints, GameState s, string where)
        {
            var c = Find(checkpoints, s.CommandCount);
            if (c == null) return false;
            ulong h = StateHash.Of(s);
            if (h != c.Hash) throw new InvalidOperationException($"{where}: 상태 해시 {h:X16} ≠ 체크포인트 {c.Hash:X16}(CommandCount {s.CommandCount}) — 다시 적용이 어긋났다");
            return true;
        }

        static SaveCheckpoint Find(List<SaveCheckpoint> checkpoints, int commandCount)
        {
            foreach (var c in checkpoints)
                if (c.CommandCount == commandCount) return c;
            return null;
        }

        /// <summary>다시 적용할 때 쓰는 받는 곳: 턴 끝(TurnEnded)을 보고, Events가 있으면 이벤트를 모은다(무르기 책). 이벤트를 만드는 원래 적용과 같은 길로 돈다.</summary>
        sealed class UndoWatch : IEventSink
        {
            public bool TurnEnded;
            public List<GameEvent> Events;

            public void Add(GameEvent e)
            {
                if (e is TurnEnded) TurnEnded = true;
                Events?.Add(e);
            }
        }

        static SaveHead NewHead(Ctx ctx, GameState s, ulong seed, SessionConfig cfg)
        {
            var h = new SaveHead
            {
                SaveId = cfg.SaveId, ContentHash = cfg.ContentHash, IdTables = SaveRecord.IdTablesOf(ctx), Ai = cfg.Ai ?? new AiSettings(),
                Campaign = s.Campaign, SeatCount = s.SeatCount, Seed = seed, Undo = UndoOf(cfg),
            };
            if (cfg.Protagonists != null) h.Protagonists.AddRange(cfg.Protagonists);
            h.Options.CopyFrom(s.Options);
            return h;
        }

        /// <summary>이 판의 무르기 설정: 손본 판(Rig — 튜토리얼)이나 명령 거르개(Allow)가 있으면 늘 끔.</summary>
        static UndoMode UndoOf(SessionConfig cfg) => cfg.Rig != null || cfg.Allow != null ? UndoMode.Off : cfg.Undo;

        // ── 무르기 · 되돌리기(결정 149, undo.md §1-3~§1-5) ──

        /// <summary>이 판의 무르기 설정(<see cref="SessionConfig.Undo"/> — Rig 또는 Allow가 있으면 늘 Off).</summary>
        public UndoMode UndoMode => UndoOf(cfg);

        bool Booking => UndoMode != UndoMode.Off;

        /// <summary>책의 바닥을 놓는다(Start · FromState = 첫 상태, Resume = 이어 받은 스냅샷 + 다시 적용한 줄). 끈 판은 아무것도 두지 않는다.</summary>
        void SetBook(GameState floor, List<UndoLine> lines)
        {
            if (!Booking) return;
            baseState = floor;
            baseCount = floor.CommandCount;
            baseRest = SaveRest.IsRest(floor);
            basePublic = 0;
            if (lines != null) book.AddRange(lines);
            RebuildTurnBase();
        }

        /// <summary>받아들인 명령 하나를 책에 쌓는다(Apply — 상태는 이미 그 명령 뒤).</summary>
        void AddLine(SaveLogLine line, List<GameEvent> hostEvents)
        {
            var u = UndoRules.LineOf(line, UndoRules.Probe(backup), state, hostEvents, publicLog.Count);
            if (u.Replayable) u.BeforeHash = StateHash.Of(backup);
            book.Add(u);
            if (u.TurnStarted) turnPending = true;
            if (!turnPending || !u.RestAfter) return;
            turnBase = state.Clone();
            turnBaseCount = state.CommandCount;
            turnPending = false;
        }

        /// <summary>책에서 지금 턴의 시작 자리를 찾아 turnBase를 다시 세운다(이어 받은 뒤 · 그보다 앞으로 되돌린 뒤).</summary>
        void RebuildTurnBase()
        {
            turnBase = null;
            turnPending = false;
            int t = book.Count - 1;
            while (t >= 0 && !book[t].TurnStarted) t--;
            if (t < 0) return;
            for (int j = t; j < book.Count; j++)
            {
                if (!book[j].RestAfter) continue;
                int pos = baseCount + j + 1;
                var s = pos == state.CommandCount ? state.Clone() : Rebuild(pos);
                turnBase = s;
                turnBaseCount = pos;
                return;
            }
            turnPending = true;
        }

        /// <summary>책의 그 자리(CommandCount) 상태를 새로 만든다: 그 앞의 가장 가까운 출발점(지금 턴 시작 사본 · 바닥)에서 책의 줄을 다시 적용(체크포인트로 어긋남을 잡는다 — 어긋나면 던진다).</summary>
        GameState Rebuild(int to)
        {
            var from = turnBase != null && turnBaseCount <= to ? turnBase : baseState;
            var s = from.Clone();
            var lines = new List<SaveLogLine>(to - s.CommandCount);
            for (int i = s.CommandCount; i < to; i++) lines.Add(book[i - baseCount].Line);
            ReapplyLines(ctx, s, lines, new List<SaveCheckpoint>(checkpoints), head.RulesVersion == SaveRecord.CurrentRulesVersion);
            return s;
        }

        /// <summary>
        /// 되돌림 하나(undo.md §1-5): 그 자리 상태를 다시 적용으로 만들고 → 저장(스냅샷 먼저 → 로그 자르기 — 실패하면 메모리는 그대로 두고 던진다) → 메모리 → 머리 → 모든 좌석에 되돌림 알림이 실린 갱신.
        /// </summary>
        void RewindTo(int to, RewindKind kind, int by, long nowMs)
        {
            var s = Rebuild(to);
            if (s.CommandCount != to || !SaveRest.IsRest(s)) throw new InvalidOperationException($"되돌린 자리 {to}가 쉬는 때가 아니다(CommandCount {s.CommandCount})");
            int publicKeep = to == baseCount ? basePublic : book[to - baseCount - 1].PublicLog;
            if (cfg.Store != null)
            {
                FillSummary(s);
                try
                {
                    cfg.Store.WriteSnapshot(new SaveSnapshot { CommandCount = to, State = s.Clone(), Checkpoints = checkpoints.FindAll(c => c.CommandCount <= to) });
                    cfg.Store.TruncateLog(to);
                }
                catch
                {
                    FillSummary(state);
                    throw;
                }
            }
            state.CopyFrom(s);
            for (int i = to - baseCount; i < book.Count; i++)
                if (book[i].Replayable) replay[book[i].Line.Seq] = book[i];
            book.RemoveRange(to - baseCount, book.Count - (to - baseCount));
            checkpoints.RemoveAll(c => c.CommandCount > to);
            if (publicLog.Count > publicKeep) publicLog.RemoveRange(publicKeep, publicLog.Count - publicKeep);
            due.Clear();
            opened.Clear();
            asked = null; // 러너가 늦게 보내는 답은 번호가 달라 버려진다
            animEnd = nowMs;
            for (int i = 0; i < seatEnd.Length; i++)
            {
                seatEnd[i] = holdEnd[i] = nowMs;
                timing[i].From(state);
            }
            snapshotDue = false;
            if (turnPending || (turnBase != null && turnBaseCount > to)) RebuildTurnBase();
            Epoch++;
            Undos++;
            if (cfg.Store != null)
            {
                WriteHeadNow();
                SnapshotFailed = false;
            }
            note = new RewindNote { Kind = kind, By = by, To = to, Round = state.Round, TurnSeat = state.ActiveSeat, NextSeq = state.NextSeq, Epoch = Epoch };
            try
            {
                Publish(new List<GameEvent>(), nowMs);
            }
            finally
            {
                note = null;
            }
        }

        /// <summary>세션이 되돌린 횟수(0부터) — <see cref="RewindNote.Epoch"/> · <see cref="UndoOffer.Epoch"/> · 방의 낡은 명령 거르기(방 메시지 Submit의 Epoch).</summary>
        public int Epoch { get; private set; }

        /// <summary>이 세션에서 한 무르기 · 되돌리기 수.</summary>
        public int Undos { get; private set; }

        /// <summary>
        /// 그 좌석에 보낼 「지금 무를 수 있나」(늘 새 객체, null이 아니다). 끈 판은 Mode = Off · Block = Off. 갱신(<see cref="GameUpdate.Undo"/>)에는 끈 판이면 싣지 않는다(null).
        /// HostList = 자유 — 방은 좌석 0이 아니면 끈다(HostSession).
        /// </summary>
        public UndoOffer OfferFor(int seat)
        {
            var mode = UndoMode;
            if (mode == UndoMode.Off) return new UndoOffer { Mode = mode, StepTo = -1, Block = UndoBlock.Off, Epoch = Epoch };
            int to = UndoRules.StepTarget(book, baseCount, baseRest, state, seat, mode, out var why);
            return new UndoOffer { Mode = mode, StepTo = to, Block = why, HostList = mode == UndoMode.Free, Epoch = Epoch };
        }

        /// <summary>방장 되돌리기 목록(새것부터). 자유가 아니면 빈 목록.</summary>
        public List<UndoPoint> UndoPoints() => UndoMode == UndoMode.Free ? UndoRules.Points(book, baseCount, state) : new List<UndoPoint>();

        /// <summary>
        /// 개인 「한 수 무르기」: to가 그 좌석의 지금 <see cref="UndoOffer.StepTo"/>와 같을 때만 되돌리고 true. 아니면(두 번 눌림 · 낡은 요청 · 못 함) 아무것도 바꾸지 않고 false.
        /// 되돌리면 저장(스냅샷 먼저 → 로그 자르기)과 메모리를 고치고, 모든 좌석에 <see cref="GameUpdate.Rewind"/>가 실린 갱신을 보낸다.
        /// </summary>
        public bool Undo(int seat, int to, long nowMs)
        {
            if (!Booking || seat < 0 || seat >= state.SeatCount) return false;
            int target = UndoRules.StepTarget(book, baseCount, baseRest, state, seat, UndoMode, out _);
            if (target < 0 || target != to) return false;
            RewindTo(to, RewindKind.Step, seat, nowMs);
            return true;
        }

        /// <summary>방장(솔로 플레이어)의 되돌리기: 자유이고 to가 <see cref="UndoPoints"/>에 있을 때만 그 턴 시작으로 되돌리고 true. 아니면 false.</summary>
        public bool Rewind(int to, int bySeat, long nowMs)
        {
            if (UndoMode != UndoMode.Free || !UndoPoints().Exists(p => p.To == to)) return false;
            RewindTo(to, RewindKind.Host, bySeat, nowMs);
            return true;
        }

        // ── 바깥에서 보는 것 ──

        public LoopbackLink Link => link;
        ISessionLink Out => cfg.Out ?? link;
        /// <summary>호스트 원본(방 HostSession이 열린 선택·턴·배틀을 읽는다 — 고치지 않는다).</summary>
        internal GameState Host => state;
        public int SeatCount => state.SeatCount;
        public int CommandCount => state.CommandCount;
        /// <summary>지금까지 시작한 행동 수(이벤트 머리 Seq의 다음 값 — 소통의 도장 · 기록 줄 핑이 가리킬 수 있는 범위).</summary>
        public int NextSeq => state.NextSeq;
        public ulong Hash => StateHash.Of(state);
        /// <summary>방장 멈춤 끝(세션 시각). 그 전에는 새 선택의 시계가 시작하지 않는다.</summary>
        public long AnimEndMs => animEnd;
        /// <summary>다음에 정해진 일(AI 답·숨은 손패 창·시간 초과)의 시각. 없으면 long.MaxValue.</summary>
        public long NextDueMs
        {
            get
            {
                if (asked != null) return asked.Due.At; // 답을 기다린다 — 뒤의 일은 그 다음
                long t = long.MaxValue;
                foreach (var d in due)
                    if (d.At < t && !Held(d)) t = d.At;
                return t;
            }
        }

        /// <summary>붙잡힌 AI 좌석의 답(<see cref="SessionConfig.Hold"/>) — 풀릴 때까지 Tick이 건너뛰고 NextDueMs에 넣지 않는다.</summary>
        bool Held(Due d) => d.Kind == DueKind.Agent && d.Seat >= 0 && cfg.Hold != null && cfg.Hold(d.Seat);
        public bool GameOver => state.Phase == Phase.GameOver;
        public SaveHead Head => head;
        /// <summary>턴 끝마다 상태 해시(01 §10). 스냅샷에 함께 쓴다.</summary>
        public IReadOnlyList<SaveCheckpoint> Checkpoints => checkpoints;
        /// <summary>던지지 않고 넘긴 문제(스냅샷·머리 쓰기 실패). 화면이 경고로 보인다.</summary>
        public IReadOnlyList<string> Warnings => warnings;
        /// <summary>세이브에 쓰는 판인가.</summary>
        public bool Saving => cfg.Store != null;
        /// <summary>써야 할 스냅샷·머리가 밀려 있다(쓰기에 실패했거나 아직 쉬는 때가 아니다) — 로그 줄은 명령마다 쓰여 있다.</summary>
        public bool SnapshotDue => snapshotDue;
        /// <summary>마지막 스냅샷·머리 쓰기가 실패했다(다음 쉬는 때의 명령 뒤에 다시 쓴다 — 되면 풀린다).</summary>
        public bool SnapshotFailed { get; private set; }
        /// <summary>이어 받으며 버린 줄 수(쉬는 때로 돌아간 만큼, <see cref="Resume"/>의 toRest). 새 게임이면 0.</summary>
        public int Rewound { get; private set; }
        /// <summary>러너(<see cref="SessionConfig.Runner"/>)의 답을 기다린다 — 그동안 뒤의 정해진 일은 미뤄진다(방은 이동안 게임 시각을 멈춘다).</summary>
        public bool AwaitingAi => asked != null;
        /// <summary>기다리는 물음의 번호(0 = 없음) — 방이 물음마다 AI 생각 시간을 잰다.</summary>
        public int AskSerial => asked?.Serial ?? 0;
        /// <summary>버린 낡은 답 수(다른 번호·다른 Id · 선택이 닫힘 · 물은 뒤 판이 바뀌어 다시 물음).</summary>
        public int StaleAiAnswers { get; private set; }
        /// <summary>답이 늦어 기본값(Timeout)으로 넘긴 선택 수(<see cref="GiveUpAi"/>).</summary>
        public int LateAi { get; private set; }

        /// <summary>좌석 보기(01 §4). 화면은 링크로 받은 보기를 쓰고, 이것은 시험·재접속용이다.</summary>
        public GameState ViewFor(int viewer) => Engine.ViewFor(state, viewer);

        /// <summary>호스트 원본 사본(시험·디버그). 화면은 쓰지 않는다(02 §1 「자기 보기만 그린다」).</summary>
        public GameState HostCopy() => state.Clone();

        /// <summary>관전자에게 거른 공개 기록(AI 입력, 01 §9-4). 이어하기 뒤에는 이어 간 뒤부터다.</summary>
        public IReadOnlyList<GameEvent> PublicLog => publicLog;

        // ── 명령 ──

        /// <summary>
        /// 좌석이 보낸 명령(링크 ToHost). 먼저 nowMs까지 정해진 일을 처리한다. 숨은 손패 창에 이른 답은 검증만 하고 최소 시간 뒤에 적용한다(Accepted).
        /// Timeout·HostOp는 세션만 넣는다. 거절이면 그 좌석에 RejectedMsg(NoChange는 보내지 않음, rulings G8).
        /// </summary>
        public SubmitResult Submit(int seat, Command cmd, long nowMs)
        {
            Tick(nowMs);
            bool virtualHuman = seat < 0 && seat != SeatId.System && cfg.HumanVirtual != null && cfg.HumanVirtual(seat); // 사람 조종자가 가상 좌석 값으로 낸 답(02 §5-6)
            if (cmd == null || (seat < 0 && !virtualHuman) || seat >= state.SeatCount) return SubmitResult.Rejected(RejectReason.BadCommand);
            // 사람 조종자는 AI 답 표지(ByAi)를 싣지 못한다 — 실으면 무르기의 「남이 한 일」 막음(UndoLine.HumanVirtual)을 피한다
            if (virtualHuman && (cmd.Kind != CommandKind.Answer || cmd.ByAi)) return Reject(seat, cmd, SubmitResult.Rejected(RejectReason.BadCommand));
            if (cmd.Kind == CommandKind.Timeout || cmd.Kind == CommandKind.HostOp || SeatAgent(seat) != null)
                return Reject(seat, cmd, SubmitResult.Rejected(RejectReason.NotYourChoice));
            if (cfg.Allow != null && !cfg.Allow(seat, cmd)) return Reject(seat, cmd, SubmitResult.Rejected(RejectReason.Tutorial));
            if (cmd.Kind == CommandKind.Answer)
            {
                var c = ChoiceRules.Find(state, cmd.ChoiceId);
                if (c != null && !c.Answered && c.HandGated && c.Seat == seat && HiddenMinMs > 0)
                {
                    long earliest = ClockStart(c.Id) + HiddenMinMs;
                    if (nowMs < earliest)
                    {
                        if (cmd.Answer == null || !ChoiceRules.IsLegal(state, ctx, c, cmd.Answer)) return Reject(seat, cmd, SubmitResult.Rejected(RejectReason.Illegal));
                        due.RemoveAll(d => d.ChoiceId == c.Id);
                        due.Add(new Due { ChoiceId = c.Id, Kind = DueKind.Held, At = earliest, Seat = seat, Held = cmd });
                        return SubmitResult.Ok;
                    }
                }
            }
            var r = Apply(seat, cmd, nowMs);
            return r.Accepted ? r : Reject(seat, cmd, r);
        }

        SubmitResult Reject(int seat, Command cmd, SubmitResult r)
        {
            if (r.Reason != RejectReason.NoChange) Out.Send(seat, new RejectedMsg { Reason = r.Reason, ChoiceId = cmd?.ChoiceId ?? -1 });
            return r;
        }

        /// <summary>
        /// nowMs까지 정해진 일을 시각 순서대로 처리한다(02 §5-7). 화면은 매 프레임 부른다.
        /// 러너가 있으면(<see cref="SessionConfig.Runner"/>) AI 차례에서 물음만 내고, 답이 오면(다음 Tick들) 그 정해진 시각으로 넣은 뒤 이어 간다 — 답이 올 때까지 뒤의 일은 미룬다.
        /// </summary>
        public void Tick(long nowMs)
        {
            if (cfg.Runner != null && asked == null)
                while (cfg.Runner.TryTake(out _)) StaleAiAnswers++; // 묻지 않은 답(거둔 물음 · 기본값으로 넘긴 뒤 온 답)
            for (int guard = 0; ; guard++)
            {
                if (guard > 100000) throw new InvalidOperationException("세션 Tick이 끝나지 않는다");
                if (asked != null)
                {
                    if (!TakeAnswer()) return;
                    continue;
                }
                Due next = null;
                foreach (var d in due)
                    if (d.At <= nowMs && !Held(d) && (next == null || d.At < next.At)) next = d;
                if (next == null) return;
                due.Remove(next);
                var c = ChoiceRules.Find(state, next.ChoiceId);
                if (c == null || c.Answered) continue;
                var again = next.Kind == DueKind.Agent ? Replay(c) : null;
                if (again == null && next.Kind == DueKind.Agent && cfg.Runner != null)
                {
                    Ask(next, c);
                    continue;
                }
                int seat;
                Command cmd;
                SubmitResult r;
                try
                {
                    switch (next.Kind)
                    {
                        case DueKind.Agent when again != null:
                            seat = again.Seat;
                            cmd = again.Command;
                            break;
                        case DueKind.Agent:
                        {
                            var d = Decide(c);
                            seat = c.Seat;
                            cmd = Command.AiAnswer(c.Id, d.Answer, d.Reason);
                            break;
                        }
                        case DueKind.HiddenDecline:
                            seat = c.Seat;
                            cmd = Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Decline));
                            break;
                        case DueKind.Timeout:
                            seat = SeatId.System;
                            cmd = Command.TimeoutOf(c.Id);
                            break;
                        default:
                            seat = next.Seat;
                            cmd = next.Held;
                            break;
                    }
                    r = Apply(seat, cmd, next.At);
                }
                catch
                {
                    due.Add(next); // 상태는 Apply가 되돌렸다 — 다음 Tick에 같은 일을 다시 한다
                    throw;
                }
                if (!r.Accepted && next.Kind != DueKind.Held)
                    throw new InvalidOperationException($"세션이 낸 {next.Kind} 답이 거절됐다: 선택 {c.Id} {c.Kind}/{c.Sub} → {r}");
                if (!r.Accepted) Reject(seat, cmd, r);
            }
        }

        /// <summary>
        /// 되돌려 버린 갈래에서 이 자리 · 이 선택에 AI가 낸 줄(답 · 늦어서 넘긴 Timeout)이 있고 상태 해시가 그때와 같으면 그 줄 — 다시 묻지 않는다(시간 마감으로 끝난 강한 AI는 답이 달라질 수 있다, undo.md §1-6).
        /// 다른 선택의 줄이면(같은 자리에 함께 열린 묶음) 그대로 두고, 자리가 같은데 상태가 다르면 판이 갈라졌다 — 남은 것을 모두 버린다.
        /// </summary>
        SaveLogLine Replay(PendingChoice c)
        {
            if (replay.Count == 0 || !replay.TryGetValue(state.CommandCount, out var u) || u.Line.Command.ChoiceId != c.Id) return null;
            replay.Remove(state.CommandCount);
            if (u.BeforeHash != StateHash.Of(state))
            {
                replay.Clear();
                return null;
            }
            return u.Line;
        }

        /// <summary>숨은 손패 창의 최소 시간. 한 좌석 판은 손패를 숨길 상대가 없어 두지 않는다 [가정 — plan §5 #81 ⑦]: 그 창은 여느 선택처럼 그 좌석이 답한다(쓸 카드가 없으면 화면이 「안 씀」 — GameFlow.PassEmptyReaction).
        /// 울트라 버스트(협동 — 모두가 한편)도 두지 않는다(결정 203): 쓸 카드가 없는 좌석의 「안 씀」은 시계 시작에 바로(<see cref="Schedule"/>) — 신오의 심장(경쟁)은 그대로 최소 시간 뒤.</summary>
        int HiddenMinMs => HiddenMinFor(state);

        /// <summary>그 판의 숨은 손패 창 최소 시간(방 시계의 NotBefore도 같은 값 — HostSession).</summary>
        public static int HiddenMinFor(GameState s) => s.SeatCount > 1 && s.Campaign != Campaign.UltraBurst ? PlayTiming.HiddenReactionMinMs : 0;

        IAgent SeatAgent(int seat) => cfg.SeatAgents != null && seat >= 0 && seat < cfg.SeatAgents.Length ? cfg.SeatAgents[seat] : null;

        /// <summary>AI에게 묻는다: 가상 응답자 = 관전자 보기, AI 좌석 = 그 좌석 보기. 공개 기록은 관전자에게 거른 것(01 §9-1·§9-4).</summary>
        AgentDecision Decide(PendingChoice c)
        {
            var input = InputFor(c, false, out var agent, out var budget);
            var d = agent.Decide(input, budget);
            if (d?.Answer == null) throw new InvalidOperationException($"AI가 선택 {c.Id}에 답하지 않았다");
            return d;
        }

        /// <summary>AI 입력 + 예산. copyLog = 공개 기록을 사본으로(러너 — 작업 스레드가 읽는 동안 이 스레드가 덧붙인다).</summary>
        AgentInput InputFor(PendingChoice c, bool copyLog, out IAgent agent, out AgentBudget budget)
        {
            int viewer = c.Seat < 0 ? SeatId.Observer : c.Seat;
            // 가상 응답자 AI 고르기(VirtualAgents — M8 원칙 AI 점검)는 여기서(메인 스레드) 한다: 러너의 작업 스레드가 방 설정 칸을 읽지 않게
            agent = c.Seat < 0 ? (cfg.VirtualAgent is VirtualAgents va ? va.For(c.Seat, state.Options.NonPlayerMovePolicy) : cfg.VirtualAgent) : SeatAgent(c.Seat);
            var view = Engine.ViewFor(state, viewer);
            var vc = view.Pending.Find(x => x.Id == c.Id);
            budget = new AgentBudget
            {
                MaxSimulations = cfg.Ai?.MaxSimulations ?? 0, Deadline = long.MaxValue, Now = null, // 시간 예산은 러너가 자기 시계로 넣는다(AgentWorker)
                // 게임 시드와 무관한 방 값(AiSettings.Seed)에서 만든다 — 게임 시드를 되돌려 셀 수 있으면 숨은 상태가 모두 재현된다(01 §4·§9-1)
                Seed = unchecked((head.Ai?.Seed ?? 0UL) * 0x9E3779B97F4A7C15UL + (ulong)c.Id),
            };
            return new AgentInput { View = view, PublicLog = copyLog ? new List<GameEvent>(publicLog) : publicLog, Choice = vc, Ctx = ctx };
        }

        /// <summary>
        /// 러너에 묻는다(02 §1-2): 마감 = 선택이 열린 시각(시계 시작) + AiSettings.DeadlineMs. 사람 조종자에게서 넘겨받은 선택(<see cref="RequestAi"/> — 시간 초과 · 끊김)은
        /// 넘겨받은 때 + DeadlineMs(결정 30 ③ — 대신 답하는 강한 AI도 2초를 쓴다 [가정 01 §13-1 M8-29]). 답이 올 때까지 <see cref="AwaitingAi"/>.
        /// </summary>
        void Ask(Due d, PendingChoice c)
        {
            var input = InputFor(c, true, out var agent, out var budget);
            long left = (d.Handed ? d.At : ClockStart(c.Id)) + (head.Ai?.DeadlineMs ?? 0) - d.At;
            int serial = ++askSerial;
            cfg.Runner.Post(new AgentRequest { Serial = serial, ChoiceId = c.Id, Agent = agent, Input = input, Budget = budget, ThinkMs = (int)Math.Max(0, Math.Min(int.MaxValue, left)) });
            asked = new Asked { Due = d, Serial = serial, Count = state.CommandCount };
        }

        /// <summary>
        /// 러너의 답을 꺼낸다(02 §1-2 「그 Id가 아직 열려 있을 때만」): 물은 선택이 닫혔으면 물음을 거둔다 · 다른 번호·다른 Id의 답은 버린다 · 물은 뒤 판이 바뀌었으면(사람의 명령) 다시 묻는다 ·
        /// 맞는 답은 정해진 시각으로 넣는다. 넣었거나 물음이 바뀌었으면 true, 아직 답이 없으면 false.
        /// </summary>
        bool TakeAnswer()
        {
            var a = asked;
            var c = ChoiceRules.Find(state, a.Due.ChoiceId);
            if (c == null || c.Answered)
            {
                asked = null; // 늦게 오는 답은 번호가 달라 버려진다
                return true;
            }
            while (cfg.Runner.TryTake(out var reply))
            {
                if (reply == null || reply.Serial != a.Serial || reply.ChoiceId != c.Id)
                {
                    StaleAiAnswers++;
                    continue;
                }
                asked = null;
                if (reply.Error != null)
                {
                    due.Add(a.Due); // 동기 실행과 같이 — 다음 Tick에 다시 묻는다
                    throw new InvalidOperationException($"AI가 선택 {c.Id}에 답하다 던졌다: {reply.Error.Message}", reply.Error);
                }
                if (state.CommandCount != a.Count)
                {
                    StaleAiAnswers++;
                    Ask(a.Due, c);
                    return true;
                }
                SubmitResult r;
                try
                {
                    r = Apply(c.Seat, Command.AiAnswer(c.Id, reply.Decision.Answer, reply.Decision.Reason), a.Due.At);
                }
                catch
                {
                    due.Add(a.Due);
                    throw;
                }
                if (!r.Accepted) throw new InvalidOperationException($"세션이 낸 Agent 답이 거절됐다: 선택 {c.Id} {c.Kind}/{c.Sub} → {r}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 러너의 답이 늦었다(방이 바깥 시각으로 잰다 — <see cref="HostSession.AiGiveUpMs"/>): 그 선택을 Timeout(기본값)으로 물은 시각에 닫고 Warnings에 적는다. 늦게 온 답은 버려진다.
        /// 기다리던 물음이 없으면 false. 러너 없이 쓰는 핵은 부르지 않는다.
        /// </summary>
        public bool GiveUpAi()
        {
            var a = asked;
            if (a == null) return false;
            asked = null;
            var c = ChoiceRules.Find(state, a.Due.ChoiceId);
            if (c == null || c.Answered) return true;
            LateAi++;
            warnings.Add($"AI 답이 늦어 선택 {c.Id}({c.Kind}/{c.Sub})을 기본값으로 넘겼다");
            SubmitResult r;
            try
            {
                r = Apply(SeatId.System, Command.TimeoutOf(c.Id), a.Due.At);
            }
            catch
            {
                due.Add(a.Due);
                throw;
            }
            if (!r.Accepted) throw new InvalidOperationException($"늦은 AI 선택 {c.Id}의 Timeout이 거절됐다: {r}");
            return true;
        }

        /// <summary>
        /// 엔진에 넣고, 받아들이면 로그 한 줄 → 체크포인트(턴 끝)·스냅샷(셋업 끝·턴 끝·배틀 끝·게임 끝 뒤의 첫 쉬는 때, 앞서 못 쓴 스냅샷) → 좌석별 갱신.
        /// 엔진 예외·로그 줄 쓰기 실패면 명령 전 상태로 되돌리고 던진다(로그에 없는 명령이 상태에 남지 않게).
        /// </summary>
        SubmitResult Apply(int seat, Command cmd, long atMs)
        {
            var line = new SaveLogLine { Seq = state.CommandCount, Seat = seat, Command = cmd };
            var log = new EventLog();
            backup.CopyFrom(state);
            SubmitResult r;
            try
            {
                using (Rules.Rig.Use(cfg.Rig))
                {
                    r = Engine.Submit(state, ctx, seat, cmd, log);
                    if (r.Accepted) cfg.Rig?.Settle(state);
                }
                if (r.Accepted) cfg.Store?.AppendCommand(line);
            }
            catch
            {
                state.CopyFrom(backup);
                throw;
            }
            if (!r.Accepted) return r;
            bool turnEnd = false, battleEnd = false;
            foreach (var e in log.Events)
            {
                if (e is TurnEnded) turnEnd = true;
                else if (e is BattleEnded) battleEnd = true;
            }
            if (turnEnd) checkpoints.Add(new SaveCheckpoint { CommandCount = state.CommandCount, Hash = StateHash.Of(state) });
            if (replay.Count > 0) replay.Remove(line.Seq); // 지나간 자리
            if (Booking) AddLine(line, log.Events);
            bool setupEnd = backup.Phase == Phase.Setup && state.Phase != Phase.Setup;   // 셋업 끝: 저장 목록의 요약(파티·고향)이 첫 턴 끝을 기다리지 않게
            if (turnEnd || battleEnd || setupEnd || state.Phase == Phase.GameOver) snapshotDue = true;
            if (snapshotDue && SaveRest.IsRest(state)) WriteSnapshot();
            Publish(log.Events, atMs);
            return r;
        }

        /// <summary>머리 요약(저장 목록)을 그 상태로 채운다.</summary>
        void FillSummary(GameState st)
        {
            var sum = head.Summary;
            sum.Campaign = st.Campaign;
            sum.Round = st.Round;
            sum.Act = st.Hos != null ? st.Hos.Act : -1;
            sum.Phase = st.Phase;
            sum.CommandCount = st.CommandCount;
            sum.Finished = st.Phase == Phase.GameOver;
            sum.SavedAtMs = cfg.SavedAt != null ? cfg.SavedAt() : 0;
            sum.Seats.Clear();
            foreach (var seat in st.Seats)
            {
                var x = new SaveSeatSummary { HomeVillage = seat.HomeVillage, TrainerCard = seat.TrainerCard, Jp = seat.Jp };
                foreach (int id in seat.Party)
                    if (id >= 0 && id < st.Pokemon.Count) x.Party.Add(st.Pokemon[id].Def);
                sum.Seats.Add(x);
            }
        }

        void WriteSnapshot()
        {
            if (cfg.Store == null) return;
            FillSummary(state);
            try
            {
                cfg.Store.WriteSnapshot(new SaveSnapshot { CommandCount = state.CommandCount, State = state.Clone(), Checkpoints = new List<SaveCheckpoint>(checkpoints) });
                cfg.BeforeHead?.Invoke(head);
                cfg.Store.WriteHead(head);
                snapshotDue = false;
                SnapshotFailed = false;
            }
            catch (Exception e)
            {
                // 스냅샷·머리는 로그의 캐시다(02 §7). 판은 이어 가고 다음 명령 뒤에 다시 쓴다
                snapshotDue = true;
                SnapshotFailed = true;
                warnings.Add($"스냅샷 {state.CommandCount} 쓰기 실패(다음 명령 뒤에 다시 씀): {e.Message}");
            }
        }

        /// <summary>갱신 하나: 공개 기록 → 방장 멈춤 끝(PlayTiming) → 좌석마다 보기 + 거른 이벤트 → 새로 열린 선택의 시계·AI 일정.</summary>
        void Publish(List<GameEvent> hostEvents, long atMs)
        {
            version++;
            try
            {
                cfg.Applied?.Invoke(hostEvents);
                foreach (var e in hostEvents)
                {
                    var f = Engine.FilterFor(e, SeatId.Observer);
                    if (f != null) publicLog.Add(f);
                }
                animEnd = PlayTiming.NextPauseEnd(atMs, animEnd, PlayTiming.UpdateMs(hostEvents, timing, seatMs, holdMs));
                for (int seat = 0; seat < seatEnd.Length; seat++)
                {
                    // 그 좌석 화면은 자기 대기열 끝에 이어 재생한다 — 차단 컷신은 그 안의 자리까지(방장 멈춤 끝을 넘지 않는다)
                    long start = Math.Max(atMs, seatEnd[seat]);
                    if (holdMs[seat] > 0) holdEnd[seat] = Math.Max(holdEnd[seat], start + holdMs[seat]);
                    seatEnd[seat] = start + seatMs[seat];
                }
                for (int seat = 0; seat < state.SeatCount; seat++)
                {
                    var events = new List<GameEvent>(hostEvents.Count);
                    foreach (var e in hostEvents)
                    {
                        var f = Engine.FilterFor(e, seat);
                        if (f != null) events.Add(f);
                    }
                    Out.Send(seat, new GameUpdate { Version = version, View = Engine.ViewFor(state, seat), Events = events, AnimEndMs = animEnd, Undo = Booking ? OfferFor(seat) : null, Rewind = note });
                }
            }
            finally
            {
                Schedule(); // 보내기가 던져도 새 선택의 AI·시계 일정은 잡는다(판이 멈추지 않게)
            }
        }

        /// <summary>링크에 새로 붙은 좌석에 지금 보기를 보낸다(이벤트 없이).</summary>
        internal void Resend(int seat)
        {
            if (seat >= state.SeatCount) return;
            Out.Send(seat, new GameUpdate { Version = version, View = Engine.ViewFor(state, seat), Events = new List<GameEvent>(), AnimEndMs = animEnd, Undo = Booking ? OfferFor(seat) : null });
        }

        // ── 방(HostSession)이 쓰는 갈고리 ──

        /// <summary>그 선택의 시계 시작(열린 갱신 뒤 방장 멈춤 끝, 세션 시각).</summary>
        internal long ClockStartOf(int choiceId) => ClockStart(choiceId);

        /// <summary>그 좌석이 범위 안인 차단 컷신(until-end)이 그 좌석 화면에서 끝나는 세션 시각(02 §5-7). 그 전의 그 좌석 Answer는 방이 E_HOLD로 거절한다.</summary>
        internal long HoldEndOf(int seat) => seat >= 0 && seat < holdEnd.Length ? holdEnd[seat] : long.MinValue;

        /// <summary>머리만 지금 쓴다(02 §7 정지점 — 강퇴·자리 맡기기·일시정지). 스냅샷은 쉬는 때 규칙 그대로. 실패는 던지지 않고 Warnings에.</summary>
        internal void WriteHeadNow()
        {
            if (cfg.Store == null) return;
            try
            {
                cfg.BeforeHead?.Invoke(head);
                cfg.Store.WriteHead(head);
            }
            catch (Exception e)
            {
                warnings.Add($"머리 쓰기 실패(다음 스냅샷 때 다시 씀): {e.Message}");
            }
        }

        /// <summary>방 닫기·앱 종료·[로비로]의 마지막 쓰기(02 §6-3·§7 정지점): 밀린 스냅샷이 있고 지금이 쉬는 때면 스냅샷 + 머리, 아니면 머리만(로그는 명령마다 쓰여 있다).</summary>
        internal void WriteFinal()
        {
            if (snapshotDue && SaveRest.IsRest(state)) WriteSnapshot();
            else WriteHeadNow();
        }

        /// <summary>사람이 조종하던 가상 응답자 선택을 AI에게 넘긴다(시간 초과·조종자 끊김, 02 §5-6): 시계 시작과 atMs 가운데 늦은 때에 AI 답을 적용한다(다음 Tick).</summary>
        internal void RequestAi(int choiceId, long atMs)
        {
            var c = ChoiceRules.Find(state, choiceId);
            if (c == null || c.Answered || asked?.Due.ChoiceId == choiceId) return; // 이미 러너에 물었다
            due.RemoveAll(d => d.ChoiceId == choiceId);
            due.Add(new Due { ChoiceId = choiceId, Kind = DueKind.Agent, At = Math.Max(atMs, ClockStart(choiceId)), Seat = c.Seat, Handed = true });
        }

        /// <summary>세션이 내는 명령(시간 초과 Timeout · 숨은 손패 창의 기본 답 · HostOp)을 그 시각에 바로 적용한다. 거절이면 어긋남(InvalidOperationException).</summary>
        internal void Inject(int seat, Command cmd, long atMs)
        {
            var r = Apply(seat, cmd, atMs);
            if (!r.Accepted) throw new InvalidOperationException($"방이 낸 {cmd.Kind} 명령이 거절됐다(좌석 {seat}, 선택 {cmd.ChoiceId}): {r}");
        }

        long ClockStart(int choiceId)
        {
            foreach (var o in opened)
                if (o.ChoiceId == choiceId) return o.ClockStart;
            return animEnd;
        }

        void Schedule()
        {
            opened.RemoveAll(o => !IsOpen(o.ChoiceId));
            due.RemoveAll(d => !IsOpen(d.ChoiceId));
            foreach (var c in state.Pending)
            {
                if (c.Answered || ClockStartKnown(c.Id)) continue;
                long start = animEnd;
                opened.Add(new Opened { ChoiceId = c.Id, ClockStart = start });
                long gate = c.HandGated ? start + HiddenMinMs : start;
                if (c.Seat < 0 || SeatAgent(c.Seat) != null)
                {
                    if (c.Seat < 0 && cfg.HumanVirtual != null && cfg.HumanVirtual(c.Seat)) continue; // 사람 조종자 — 시계·AI 대신은 방이 맡는다(RequestAi)
                    due.Add(new Due { ChoiceId = c.Id, Kind = DueKind.Agent, At = gate, Seat = c.Seat });
                    continue;
                }
                // 여럿 판: 쓸 카드가 없는(또는 「이번 배틀 묻지 않기」) 사람 좌석은 묻지 않고 gate에 「안 씀」 — 심장 판 gate = 최소 시간 뒤, 울트라 버스트 판 gate = 바로(결정 203)
                if (c.HandGated && state.SeatCount > 1 && (OnlyDecline(c) || (cfg.AutoPass != null && cfg.AutoPass(c))))
                {
                    due.Add(new Due { ChoiceId = c.Id, Kind = DueKind.HiddenDecline, At = gate, Seat = c.Seat });
                    continue;
                }
                int ms = cfg.ClockMs != null && (int)c.Clock < cfg.ClockMs.Length ? cfg.ClockMs[(int)c.Clock] : 0;
                if (c.Clock != ClockKind.None && ms > 0) due.Add(new Due { ChoiceId = c.Id, Kind = DueKind.Timeout, At = start + ms, Seat = SeatId.System });
            }
        }

        bool IsOpen(int choiceId)
        {
            var c = ChoiceRules.Find(state, choiceId);
            return c != null && !c.Answered;
        }

        bool ClockStartKnown(int choiceId)
        {
            foreach (var o in opened)
                if (o.ChoiceId == choiceId) return true;
            return false;
        }

        bool OnlyDecline(PendingChoice c)
        {
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(state, ctx, c, legal);
            return legal.Count == 1 && legal[0].Kind == AnswerKind.Decline;
        }
    }
}
