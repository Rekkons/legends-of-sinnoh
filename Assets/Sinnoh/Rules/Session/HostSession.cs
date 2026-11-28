using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>방을 여는 값. 조립 지점(Net·시험)이 채운다 — VirtualAgent는 Rules가 Ai를 모르므로 여기로 꽂는다(01 §1).</summary>
    public sealed class HostConfig
    {
        /// <summary>방장(호스트 PC 프로필)의 토큰. 이 토큰은 늘 1번 자리다(02 §3-1).</summary>
        public string OwnerToken;
        public string OwnerNickname;
        /// <summary>방장 좌석의 주인공(<see cref="Protagonists.Keys"/> — 없거나 모르는 키면 자리 번호로 정한 기본, M9 다듬기 P06).</summary>
        public string OwnerProtagonist;
        public string RoomId;
        /// <summary>빌드 표지(02 §4-6 — 완전히 같아야 한다 [가정]).</summary>
        public string BuildId = "";
        public string ContentHash;
        /// <summary>게임 시드(엔진 Rng)와 AI 시드(게임 시드와 무관하게, 01 §9-1).</summary>
        public ulong Seed;
        public ulong AiSeed;
        public IAgent VirtualAgent;
        /// <summary>처음 방 설정(null = 기본). 이어하기(<see cref="HostSession.Resume"/>)에서는 저장된 값을 쓰고 이것은 보지 않는다.</summary>
        public RoomSettings Settings;
        /// <summary>
        /// 판의 저장을 만든다(02 §7 — SaveId → 그 판의 저장, 판마다 따로: 첫 판 = RoomId, [로비로] 뒤 n번째 판 = 「RoomId-n」. null = 저장 안 함).
        /// 조립 지점이 SaveId로 폴더를 정한다(Data SaveStore · 쓰기 스레드 SaveWriter). 받은 저장이 IDisposable이면 방이 그 판을 끝낼 때([로비로] · 방 닫기) Dispose한다 — 밀린 쓰기를 비운다.
        /// 이어하기(<see cref="HostSession.Resume"/>)는 저장의 SaveId로 부른다(같은 폴더에 이어 쓴다). 시험은 임시 폴더를 준다(사용자 저장 폴더 금지).
        /// </summary>
        public Func<string, ISaveStore> Store;
        /// <summary>머리 요약의 저장 시각(UTC ms). null = 0.</summary>
        public Func<long> SavedAt;
        /// <summary>AI 러너(02 §1-2 — 작업 스레드 <see cref="AgentWorker"/>, 갖는 쪽이 Dispose). null = 정해진 시각에 메인 스레드에서 바로 묻는다. 있든 없든 같은 시드면 같은 판이다.</summary>
        public IAgentRunner Runner;
        /// <summary>방장이 이 방의 판 기록을 보내나(결정 126 ④ — 조립 지점이 방장의 「플레이 기록 보내기」로 채운다). 방 상태 <see cref="RoomState.Records"/>로 손님에게 간다(규칙 · 해시 밖).</summary>
        public bool Records;
    }

    /// <summary>
    /// 방 호스트(02 §1-1 HostSession, M7 「세션 ①」): 로비(자리 6 · 준비 · 내보내기 · 방 설정) → 게임(엔진 핵 = <see cref="LocalSession"/>을 감싼다) — 연결 ↔ 좌석,
    /// 제한 시간(<see cref="TurnClock"/>, 02 §5), 일시정지·시스템 정지(§5-7), 끊김 → 그 시계만큼 기다린 뒤 자리 비움 → 0초(§6-1), 재접속(토큰 → 같은 자리 + 지금 보기),
    /// 가상 응답자의 사람 조종자(배틀에 없는 다음 좌석, 답이 없으면 방 설정 AI — 결정 29 ④, 02 §5-6).
    /// 시각(02 §5-7 끝): 바깥 시각(ms, 단조)을 모든 진입점의 인자로 받는다. 일시정지 동안은 「게임 시각」이 멈춘다 — 엔진 핵·시계·연출 멈춤·AI 답이 모두 게임 시각으로 돈다
    /// (그래서 GameUpdate.AnimEndMs도 게임 시각이다. 손님은 <see cref="ClockState.AnimLeftMs"/>를 쓴다). 정해진 일(AI 답·시간 초과)은 그 정해진 시각을 「지금」으로 적용해
    /// 진입점을 부르는 간격과 무관하게 같은 명령 열이 나온다.
    /// 보기 보내기(02 §4-4, 「세션 ②」): 핵이 좌석마다 낸 GameUpdate(보기 사본 전체)를 그 좌석의 <see cref="SeatStream"/>이 스냅샷이나 델타로 바꿔 연결에 보낸다 —
    /// 게임 시작·재접속·RequestResync·끊김 뒤에는 스냅샷, 그 밖은 델타. 손님은 <see cref="StreamReceiver"/>로 되읽는다.
    /// 「세션 ③」: 강퇴·자리 비움 투표(02 §6-2), 자리 확인 대기(저장·판에 없는 토큰 → 방장이 맡기거나 거절, §6-3), 방 저장(<see cref="HostConfig.Store"/> — 머리에 방 몫 <see cref="SaveRoom"/>)과
    /// 이어하기 대기(<see cref="Resume"/> — 모두 돌아오면 시작 · 방장의 [지금 재개]), 차단 컷신 E_HOLD(그 좌석이 범위 안인 until-end 동안 Answer를 거절, §5-7).
    /// 「호스트 러너」: AI 답은 러너(<see cref="HostConfig.Runner"/>, 작업 스레드)가 셈하고 이 방의 진입점(메인 스레드 한 곳)에서 넣는다 — 답을 기다리는 동안 게임 시각이 멈추고(AI 생각, 시계가 줄지 않는다)
    /// <see cref="AiGiveUpMs"/>(바깥 시각)를 넘기면 그 선택을 기본값으로 넘긴다. [방 닫기]·앱 종료 = <see cref="Close"/>(마지막 쓰기 → E_HOST_CLOSED).
    /// 「세션 ③-2」: 결과 뒤 [로비로](ReturnToLobby — 같은 자리 · 새 시드 · 새 SaveId), SetAutoPass(「이번 배틀 묻지 않기」).
    /// 링크 콜백(<see cref="IRoomLink.Send"/>) 안에서 이 방을 다시 부르지 않는다(InvalidOperationException).
    /// </summary>
    public sealed class HostSession : ISessionLink
    {
        /// <summary>시스템 정지의 주인(PausedBy에 든다 — 손님이 모두 끊김, 02 §5-7).</summary>
        public const int SystemPause = -1;
        /// <summary>투표 시간(02 §6-2 「60초 안」)과 부결 뒤 같은 대상·종류의 대기 — 바깥 시각(일시정지 중에도 흐른다) [가정 M7-18].</summary>
        public const long VoteMs = 60000, VoteCooldownMs = 60000;
        /// <summary>일시정지 투표 시간(바깥 시각 — isekai PauseVote.TimeLimitMs와 같은 30초) [가정 M7-88].</summary>
        public const long PauseVoteMs = 30000;
        /// <summary>러너의 답을 기다리는 한도(바깥 시각 ms) — 넘기면 그 선택을 기본값(Timeout)으로 넘긴다. AI 마감(AiSettings.DeadlineMs 2초, 결정 29 ③) + 여유 1초 [가정 M7-24].</summary>
        public const long AiGiveUpMs = 3000;

        sealed class Member
        {
            public string Token;
            public string Nickname;
            public Platform Platform;
            /// <summary>좌석의 주인공(로비에서 고른다 — <see cref="PickProtagonist"/>, 겉모양뿐). 겹쳐 골라도 된다 [가정 M7-94].</summary>
            public string Protagonist;
            public int Conn = -1;
            public bool Ready;
            public bool Away;
            /// <summary>강퇴 투표로 나갔다: 토큰 금지 · 끝까지 자리 비움(0초) · 자리 맡기기도 안 된다(02 §6-2).</summary>
            public bool Kicked;
            public bool Connected => Conn >= 0;
        }

        /// <summary>자리 확인 대기 한 연결(02 §6-3): 판·저장에 없는 토큰. 방장이 접속하지 않은 좌석에 맡기거나 거절한다.</summary>
        sealed class Claim
        {
            public int Id;
            public int Conn;
            public string Token;
            public string Nickname;
            public Platform Platform;
        }

        sealed class Vote
        {
            public int Id;
            public VoteKind Kind;
            public int Target;
            public int By;
            public readonly List<int> Yes = new List<int>();
            /// <summary>마감(바깥 시각).</summary>
            public long Deadline;
        }

        /// <summary>「이번 배틀 묻지 않기」 하나(04 §5 ①): 그 좌석 · 반응 갈래 · 켠 배틀(BattleState.Id — 배틀이 바뀌면 저절로 풀린다).</summary>
        sealed class AutoPassRule
        {
            public int Seat;
            public ChoiceSub Sub;
            public int Battle;
        }

        readonly Ctx ctx;
        readonly IRoomLink link;
        readonly HostConfig cfg;
        /// <summary>로비의 자리(길이 6, null = 빈 자리). 게임을 시작하면 빈 자리를 뺀 <see cref="seats"/>로 바뀐다.</summary>
        readonly Member[] slots = new Member[SeatId.MaxSeats];
        readonly List<Member> seats = new List<Member>();
        readonly HashSet<string> banned = new HashSet<string>();
        readonly List<int> pausedBy = new List<int>();
        /// <summary>일시정지 예약(방 설정 「모두」 · 「투표」 — 그 좌석의 차례나 선택이 오면 정지로 옮긴다, 결정 112 Q6).</summary>
        readonly List<int> pauseReserved = new List<int>();
        readonly HashSet<int> aiRequested = new HashSet<int>();
        readonly List<KeyValuePair<int, ServerMsg>> outbox = new List<KeyValuePair<int, ServerMsg>>();
        readonly List<ClockChoice> clockList = new List<ClockChoice>();
        readonly List<Claim> claims = new List<Claim>();
        readonly SocialRate social = new SocialRate(SocialRate.HostSlackMs);
        readonly PeekRate peeks = new PeekRate();
        readonly ChatRate chats = new ChatRate(SocialRate.HostSlackMs);
        /// <summary>부결 뒤 대기(「종류:대상」 → 바깥 시각).</summary>
        readonly Dictionary<string, long> voteCooldown = new Dictionary<string, long>();
        readonly List<AutoPassRule> autoPass = new List<AutoPassRule>();
        /// <summary>좌석마다 마지막으로 보낸 스트림(게임 시작 때 만든다, 02 §4-4).</summary>
        SeatStream[] streams;
        RoomSettings settings;
        string roomId;
        /// <summary>이 판의 SaveId(첫 판 = roomId, [로비로] 뒤 「roomId-n」)와 판 번호(1부터 — 시드도 판마다 다르다).</summary>
        string saveId;
        int games = 1;
        /// <summary>이 판의 저장(<see cref="HostConfig.Store"/>가 만든 것 — 판이 끝나면 IDisposable이면 닫는다).</summary>
        ISaveStore store;
        /// <summary>핵이 러너의 답을 기다리기 시작한 바깥 시각(−1 = 아님)과 그 물음 번호. 그동안 게임 시각이 멈춘다(02 §10 「AI 생각」 — 시계가 줄지 않는다).</summary>
        long thinkAt = -1;
        int thinkSerial;
        /// <summary>이어하기 대기 중인 저장(<see cref="Resume"/> — 시작하면 비운다).</summary>
        SaveRecord resumeFrom;
        Vote vote;
        int voteIds, claimIds;
        LocalSession core;
        TurnClock clock;
        long lastWall;
        long pausedTotal;
        long pausedAt;
        long gameAt;
        bool busy;
        int submitter = -1;

        public HostSession(Ctx ctx, IRoomLink link, HostConfig cfg, long nowMs)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.link = link ?? throw new ArgumentNullException(nameof(link));
            this.cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            if (string.IsNullOrEmpty(cfg.OwnerToken)) throw new ArgumentException("방장 토큰이 없다", nameof(cfg));
            if (cfg.VirtualAgent == null) throw new ArgumentException("VirtualAgent가 없다 — 가상 응답자에 답할 AI를 꽂는다", nameof(cfg));
            settings = cfg.Settings?.Clone() ?? new RoomSettings();
            if (settings.Problem(1) != null) throw new ArgumentException($"방 설정이 틀렸다({settings.Problem(1)})", nameof(cfg));
            slots[0] = new Member { Token = cfg.OwnerToken, Nickname = cfg.OwnerNickname, Protagonist = Protagonists.ForSeat(cfg.OwnerProtagonist, 0) };
            roomId = saveId = cfg.RoomId;
            lastWall = nowMs;
        }

        /// <summary>
        /// 방 저장으로 방을 다시 연다(02 §6-3 다시 열기 → S10 이어하기 대기실): 저장된 좌석·방 설정·금지 토큰으로 <see cref="RoomPhase.Waiting"/>에서 시작한다.
        /// 저장의 토큰으로 들어오면 그 좌석, 모르는 토큰은 자리 확인 대기, 강퇴된 토큰은 E_KICKED. 모든 좌석(강퇴 뺌)이 돌아오면 저절로 시작하고, 방장은 [지금 재개](<see cref="StartGame"/>)로
        /// 빠진 좌석을 「끊김」으로 두고 시작한다. 시작 = <see cref="LocalSession.Resume"/>(마지막 쉬는 때로 — 솔로와 같은 규칙 [가정 M7-19]) + 뱅크 되살림.
        /// 방장 자리(1번)의 토큰은 이 PC 프로필(cfg.OwnerToken)로 맞춘다. cfg.Store는 저장의 SaveId로 부른다 — 같은 저장 폴더를 돌려줘야 한다(이어 쓴다). 방 저장이 아니면(솔로 저장) NotSupportedException,
        /// 형식 판·데이터가 다르면 NotSupportedException(<see cref="LocalSession.Resume"/>와 같은 확인 — 손님을 받기 전에).
        /// </summary>
        public static HostSession Resume(Ctx ctx, IRoomLink link, HostConfig cfg, SaveRecord record, long nowMs)
        {
            if (record?.Head == null || record.Snapshot?.State == null) throw new ArgumentException("머리·스냅샷이 없는 세이브", nameof(record));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var room = record.Head.Room;
            if (room?.Settings == null || room.Seats == null || room.Seats.Count < RoomSettings.MinSeats) throw new NotSupportedException("방 저장이 아니다(머리에 방 몫이 없다)");
            if (room.Seats.Count != record.Head.SeatCount) throw new NotSupportedException($"방 저장의 좌석 {room.Seats.Count} ≠ 머리의 좌석 수 {record.Head.SeatCount}");
            LocalSession.CheckHead(ctx, record.Head, cfg.ContentHash);
            var h = new HostSession(ctx, link, cfg, nowMs);
            h.settings = room.Settings.Clone();
            h.roomId = h.saveId = record.Head.SaveId ?? cfg.RoomId;
            h.slots[0] = null;
            for (int i = 0; i < room.Seats.Count; i++)
            {
                var s = room.Seats[i];
                h.seats.Add(new Member
                {
                    Token = i == 0 ? cfg.OwnerToken : s.Token, Nickname = i == 0 && cfg.OwnerNickname != null ? cfg.OwnerNickname : s.Nickname, Platform = s.Platform,
                    Kicked = s.Kicked, Away = s.Kicked,
                    // 좌석의 주인공은 머리의 좌석별 주인공 목록에서(방 저장에 유지 — 세이브 꼴은 그대로, P06)
                    Protagonist = Protagonists.ForSeat(record.Head.Protagonists != null && i < record.Head.Protagonists.Count ? record.Head.Protagonists[i] : null, i),
                });
            }
            if (room.Banned != null)
                foreach (var t in room.Banned)
                    if (!string.IsNullOrEmpty(t)) h.banned.Add(t);
            h.resumeFrom = record;
            h.Phase = RoomPhase.Waiting;
            return h;
        }

        // ── 바깥에서 보는 것 ──

        public RoomPhase Phase { get; private set; }
        public RoomSettings Settings => settings.Clone();
        /// <summary>방 상태(방장이 보는 것 — 자리 확인 대기 목록 포함).</summary>
        public RoomState Room => BuildRoom(true);
        public ClockState Clock => BuildClock(GameNow(lastWall));
        public bool Paused => pausedBy.Count > 0;
        /// <summary>게임 핵(시험·호스트 화면의 디버그). 게임 전에는 null. 핵에 직접 명령을 넣지 않는다 — 방의 진입점으로.</summary>
        public LocalSession Game => core;
        public int SeatCount => Phase == RoomPhase.Lobby ? 0 : seats.Count;

        /// <summary>다음에 정해진 일(AI 답·시간 초과·투표 마감)의 바깥 시각. 없으면 long.MaxValue(일시정지 중에는 투표 마감만) — 러너는 이때 <see cref="Tick"/>을 부르면 된다.</summary>
        public long NextEventMs
        {
            get
            {
                long t = long.MaxValue;
                if (core != null && Phase == RoomPhase.InGame && !Paused)
                {
                    if (core.AwaitingAi) t = thinkAt >= 0 ? thinkAt + AiGiveUpMs : lastWall; // 러너의 답 — 오면 다음 진입점이 넣는다(러너는 매 프레임 Tick)
                    else
                    {
                        long x = Math.Min(core.NextDueMs, clock.NextExpiry(out _));
                        if (x != long.MaxValue) t = x + pausedTotal;
                    }
                }
                if (vote != null && vote.Deadline < t) t = vote.Deadline;
                return t;
            }
        }

        /// <summary>그 좌석이 범위 안인 차단 컷신의 남은 ms(게임 시각, 02 §5-7) — 그동안 그 좌석의 Answer는 E_HOLD. 좌석에 가는 <see cref="ClockState.HoldLeftMs"/>와 같다.</summary>
        public long HoldLeftMs(int seat) => core == null ? 0 : Math.Max(0, core.HoldEndOf(seat) - GameNow(lastWall));

        /// <summary>방장 멈춤(연출) 끝의 바깥 시각(일시정지가 없을 때 — 일시정지 중이면 늦어진다).</summary>
        public long AnimEndMs => core == null ? lastWall : core.AnimEndMs + pausedTotal;

        /// <summary>러너(<see cref="HostConfig.Runner"/>)의 AI 답을 기다린다 — 그동안 게임 시각이 멈춘다. 시각을 손으로 옮기는 드라이버(시험)는 러너가 셈을 끝내기를 기다린 뒤 Tick한다.</summary>
        public bool AwaitingAi => core != null && core.AwaitingAi;

        /// <summary>이 판의 SaveId(첫 판 = RoomId, [로비로] 뒤 n번째 판 = 「RoomId-n」).</summary>
        public string SaveId => saveId;

        /// <summary>그 가상 응답자(갤럭시단·UB)를 지금 조종하는 사람 좌석(−1 = AI). 화면의 「조종: ○○」과 시험이 쓴다.</summary>
        public int ControllerOf(int virtualSeat) => Controller(virtualSeat);

        /// <summary>그 연결의 좌석(로비에서는 자리 번호). 없으면 −1.</summary>
        public int SeatOf(int conn)
        {
            if (conn < 0) return -1;
            var list = Seated;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Conn == conn) return i;
            return -1;
        }

        IReadOnlyList<Member> Seated => Phase == RoomPhase.Lobby ? (IReadOnlyList<Member>)slots : seats;

        /// <summary>게임 시각 = 바깥 시각 − 일시정지 − AI 생각(러너의 답을 기다린 시간).</summary>
        long GameNow(long wall) => (Paused ? pausedAt : thinkAt >= 0 ? thinkAt : wall) - pausedTotal;

        // ── 진입점 ──

        /// <summary>
        /// 접속 승인(02 §4-6). 승인하면 자리를 잡고 Welcome → RoomUpdate(전원) → (게임 중 재접속이면) 지금 보기를 보낸다. 거절이면 아무것도 보내지 않는다 — 끊는 것은 부른 쪽(NGO 승인 응답).
        /// 게임 중·이어하기 대기에서는 그 판의 토큰이면 그 좌석, 모르는 토큰은 접속하지 않은 좌석이 있으면 자리 확인 대기(<see cref="Approval.Claim"/> — 방장이 정한다), 없으면 E_IN_GAME.
        /// </summary>
        public Approval Approve(int conn, Hello hello, long nowMs)
        {
            if (Phase == RoomPhase.Closed) return Approval.Reject(NetCode.HostClosed);
            Approval result = null;
            Op(nowMs, g => result = DoApprove(conn, hello, g));
            return result;
        }

        public void OnMessage(int conn, RoomMsg msg, long nowMs) => Op(nowMs, g => Handle(conn, msg, g));

        /// <summary>연결이 끊김(02 §6-1): 로비면 자리를 비우고(방장 자리는 남김), 게임 중이면 「끊김」 — 게임은 계속한다.</summary>
        public void OnDisconnected(int conn, long nowMs) => Op(nowMs, g => Lost(conn));

        /// <summary>시각을 넣는다(러너가 매 프레임 또는 <see cref="NextEventMs"/>에). 그때까지 정해진 일을 시각 순서대로 처리한다.</summary>
        public void Tick(long nowMs) => Op(nowMs, g => { });

        /// <summary>
        /// [방 닫기]·앱 종료(02 §6-3): 지금까지의 정해진 일을 처리하고, 저장을 끝까지 쓰고(머리 — 정지점, 밀린 스냅샷은 쉬는 때면 함께) 판의 저장을 닫은 뒤(쓰기 스레드면 밀린 쓰기를 비운다),
        /// 모든 연결(자리 확인 대기 포함)에 E_HOST_CLOSED 알림 → 끊기. 그 뒤 진입점은 아무것도 하지 않고 승인은 E_HOST_CLOSED. NGO 종료를 1초 미루는 일은 Net(IS02 §2-4).
        /// 러너(<see cref="HostConfig.Runner"/>)는 갖는 쪽이 이 뒤에 Dispose한다 — 기다리던 AI 답은 버린다.
        /// </summary>
        public void Close(long nowMs) => Op(nowMs, g => DoClose());

        void Op(long nowMs, Action<long> body)
        {
            if (busy) throw new InvalidOperationException("링크 콜백 안에서 방을 다시 부르지 않는다");
            if (Phase == RoomPhase.Closed) return;
            busy = true;
            try
            {
                if (nowMs > lastWall) lastWall = nowMs;
                Advance();
                body(GameNow(lastWall));
                if (Phase == RoomPhase.Closed) return;
                CheckVote(GameNow(lastWall));
                Advance();
            }
            finally
            {
                try
                {
                    Flush();
                }
                finally
                {
                    busy = false;
                }
            }
        }

        // ── 게임 시각 진행 ──

        /// <summary>
        /// 지금까지 게임 시각을 진행한다: <see cref="Run"/> → AI 생각 표시(<see cref="Think"/>) → 러너의 답이 <see cref="AiGiveUpMs"/>(바깥 시각)를 넘겼으면 그 선택을 기본값으로 넘기고 다시.
        /// </summary>
        void Advance()
        {
            for (int guard = 0; ; guard++)
            {
                if (guard > 1000) throw new InvalidOperationException("방 Advance가 끝나지 않는다");
                Run(GameNow(lastWall));
                Think();
                if (thinkAt < 0 || lastWall - thinkAt < AiGiveUpMs) return;
                core.GiveUpAi();
                Think();
            }
        }

        /// <summary>
        /// 핵이 러너의 답을 기다리면 게임 시각을 멈춘다(02 §10 「AI 생각」 — 시계·연출 멈춤이 줄지 않는다): 물음마다 그때의 바깥 시각부터, 답이 들어가면(또는 일시정지) 그만큼을 게임 시각에서 뺀다.
        /// 답은 물은 게임 시각으로 들어가므로 러너가 없을 때와 같은 시각 열이 된다 [가정 M7-24].
        /// </summary>
        void Think()
        {
            bool want = core != null && core.AwaitingAi && !Paused && Phase == RoomPhase.InGame;
            if (thinkAt >= 0 && (!want || core.AskSerial != thinkSerial)) EndThink();
            if (want && thinkAt < 0)
            {
                thinkAt = lastWall;
                thinkSerial = core.AskSerial;
            }
        }

        void EndThink()
        {
            if (thinkAt < 0) return;
            pausedTotal += lastWall - thinkAt;
            thinkAt = -1;
            thinkSerial = 0;
        }

        /// <summary>g까지 엔진 핵의 정해진 일과 시계의 시간 초과를 시각 순서대로(같은 시각이면 핵 먼저) 처리한다. 핵이 러너의 답을 기다리면 거기서 멈춘다(뒤의 시간 초과도 그 다음 — 같은 순서).</summary>
        void Run(long g)
        {
            if (core == null || Phase == RoomPhase.Lobby) return;
            Sync(gameAt);
            for (int guard = 0; Phase == RoomPhase.InGame && !Paused; guard++)
            {
                if (guard > 100000) throw new InvalidOperationException("방 Run이 끝나지 않는다");
                long tc = core.NextDueMs;
                long te = clock.NextExpiry(out int expired);
                long t = Math.Min(tc, te);
                if (t > g) break;
                if (t < gameAt) t = gameAt;
                clock.AdvanceTo(t);
                gameAt = t;
                if (tc <= te) core.Tick(t);
                else Expire(expired, t);
                Sync(t);
                if (core.AwaitingAi) return;
            }
            if (!Paused && g > gameAt && !core.AwaitingAi)
            {
                clock.AdvanceTo(g);
                gameAt = g;
            }
        }

        /// <summary>시계가 다 된 선택(02 §5-6): 사람 조종 가상 응답자 → 방 설정 AI · 숨은 손패 창 → 그 좌석의 기본 답(「안 씀」 — Timeout 표시 없이) · 그 밖 → Timeout(Default). 끊긴 좌석이면 자리 비움.</summary>
        void Expire(int id, long t)
        {
            var c = ChoiceRules.Find(core.Host, id);
            if (c == null || c.Answered) return;
            int seat = c.Seat >= 0 ? c.Seat : Controller(c.Seat);
            if (c.Seat < 0)
            {
                core.RequestAi(id, t);
                aiRequested.Add(id);
            }
            else if (c.HandGated) core.Inject(c.Seat, Command.AnswerTo(id, c.Default.Clone()), t); // [가정 M7-4] 손패 유무가 「시간 초과」 표시로 새지 않게
            else core.Inject(SeatId.System, Command.TimeoutOf(id), t);
            ToAll(new NoticeMsg { Code = NetCode.AutoResolved, Seat = seat, Kind = c.Kind });
            if (seat >= 0 && !seats[seat].Connected && !seats[seat].Away) SetAway(seat, true, t);
        }

        /// <summary>열린 선택으로 시계를 다시 맞춘다. 조종자가 없어진 가상 응답자 선택은 AI에게 넘긴다. 게임이 끝났으면 Result.</summary>
        void Sync(long t)
        {
            var s = core.Host;
            clockList.Clear();
            aiRequested.RemoveWhere(id => ChoiceRules.Find(s, id) == null);
            int hiddenMin = LocalSession.HiddenMinFor(s);   // 울트라 버스트(협동)는 0(결정 203)
            foreach (var c in s.Pending)
            {
                if (c.Answered || c.Clock == ClockKind.None) continue;
                int seat;
                bool own = c.Seat >= 0;
                if (own) seat = c.Seat;
                else
                {
                    if (aiRequested.Contains(c.Id)) continue;
                    seat = Controller(c.Seat);
                    if (seat < 0)
                    {
                        core.RequestAi(c.Id, t); // AI 조종이면 핵이 이미 같은 때로 잡아 두었다 — 다시 잡아도 같다
                        aiRequested.Add(c.Id);
                        continue;
                    }
                }
                var m = seats[seat];
                clockList.Add(new ClockChoice
                {
                    Id = c.Id, Seat = seat, Kind = c.Kind, Clock = c.Clock, TurnId = s.TurnId, Own = own, Offline = !m.Connected, Away = m.Away,
                    NotBefore = c.HandGated ? core.ClockStartOf(c.Id) + hiddenMin : long.MinValue,
                });
            }
            clock.OnRound(s.Round);
            clock.Sync(core.AnimEndMs, s.TurnId, clockList);
            PromoteReserved();
            if (core.GameOver && Phase == RoomPhase.InGame)
            {
                Phase = RoomPhase.Result;
                BroadcastRoom();
            }
        }

        /// <summary>
        /// 가상 응답자(갤럭시단·UB)의 사람 조종자 좌석(결정 29 ④): 방 설정이 「사람」이면 배틀 당사자(첫 사람 쪽, 없으면 차례 좌석)부터 시계 방향으로 배틀에 없는 다음 좌석.
        /// 그 좌석이 끊겼거나 자리 비움이면 −1(그 역할을 바로 AI가 맡는다, 02 §6-1 — 그 다음 좌석으로 넘기지 않는다 [가정 M7-5]). 야생·AI 조종·좌석이 없으면 −1.
        /// 울트라 버스트 모드는 늘 −1(결정 121 — UB · 네크로즈마 · 야생 모두 AI).
        /// </summary>
        int Controller(int virtualSeat)
        {
            if (core == null || virtualSeat >= 0) return -1;
            if (settings.Campaign == Campaign.UltraBurst) return -1;   // 울트라 버스트 모드는 상대를 사람이 조종하지 않는다(결정 121 — 사람 조종으로 저장된 판도 AI)
            if (virtualSeat != SeatId.Galactic || settings.Galactic != Control.Human) return -1;   // UB 조종 칸(settings.UltraBeast)은 울트라 버스트에서만 들었으니 이제 읽지 않는다
            var s = core.Host;
            int n = s.SeatCount;
            int anchor = s.ActiveSeat;
            if (s.Battle != null)
                foreach (var side in s.Battle.Sides)
                    if (side.Owner >= 0)
                    {
                        anchor = side.Owner;
                        break;
                    }
            if (anchor < 0) return -1;
            for (int k = 1; k < n; k++)
            {
                int seat = (anchor + k) % n;
                if (InBattle(s, seat)) continue;
                var m = seats[seat];
                return m.Connected && !m.Away ? seat : -1;
            }
            return -1;
        }

        static bool InBattle(GameState s, int seat)
        {
            if (s.Battle == null) return false;
            foreach (var side in s.Battle.Sides)
                if (side.Owner == seat) return true;
            return false;
        }

        // ── 승인·메시지 ──

        Approval DoApprove(int conn, Hello hello, long g)
        {
            if (hello == null || string.IsNullOrEmpty(hello.Token)) return Approval.Reject(NetCode.BadHello);
            if (hello.ProtocolVersion != Protocol.Version || hello.BuildId != cfg.BuildId || hello.RulesVersion != SaveRecord.CurrentRulesVersion)
                return Approval.Reject(NetCode.Version, $"{cfg.BuildId}/{hello.BuildId}");
            if (hello.ContentHash != cfg.ContentHash) return Approval.Reject(NetCode.Content);
            if (banned.Contains(hello.Token)) return Approval.Reject(NetCode.Kicked);
            if (SeatOf(conn) >= 0 || ClaimOf(conn) != null) return Approval.Reject(NetCode.Duplicate);
            var list = Seated;
            int seat = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Token == hello.Token) seat = i;
            if (seat >= 0 && list[seat].Connected) return Approval.Reject(NetCode.Duplicate);

            if (Phase == RoomPhase.Lobby)
            {
                if (seat < 0)
                {
                    for (int i = 0; i < settings.Seats && seat < 0; i++)
                        if (slots[i] == null) seat = i;
                    if (seat < 0) return Approval.Reject(NetCode.Full, $"{Occupied()}/{settings.Seats}");
                    slots[seat] = new Member { Token = hello.Token, Protagonist = Protagonists.ForSeat(hello.Protagonist, seat) };
                }
                var m = slots[seat];
                m.Conn = conn;
                m.Nickname = hello.Nickname;
                m.Platform = hello.Platform;
                m.Ready = false;
                Send(seat, new Welcome { YourSeat = seat, RoomId = roomId, IsHost = seat == 0 });
                BroadcastRoom();
                return Approval.Accept(seat);
            }

            if (seat < 0)
            {
                if (!AnyOpenSeat()) return Approval.Reject(NetCode.InGame);
                // 자리 확인 대기(02 §6-3): 거절하지 않고 방장에게 맡긴다 — 다른 PC로 들어오면 토큰이 새로 생긴다
                var c = new Claim { Id = ++claimIds, Conn = conn, Token = hello.Token, Nickname = hello.Nickname, Platform = hello.Platform };
                claims.Add(c);
                link.Send(conn, new NoticeMsg { Code = NetCode.SeatClaim });
                BroadcastRoom();
                return Approval.Claim();
            }
            Rejoin(seat, conn, hello.Platform, g);
            return Approval.Accept(seat);
        }

        /// <summary>
        /// 좌석에 연결이 돌아온다(재접속 · 자리 맡기기 · 이어하기 대기): Welcome → PLAYER_BACK → 자리 비움 풀기 → RoomUpdate → (게임 중이면) 지금 보기(이벤트 없이) 스냅샷.
        /// 이어하기 대기에서 모든 좌석이 돌아왔으면 시작한다.
        /// </summary>
        void Rejoin(int seat, int conn, Platform platform, long g)
        {
            var back = seats[seat];
            back.Conn = conn;
            back.Platform = platform;
            Send(seat, new Welcome { YourSeat = seat, RoomId = roomId, IsHost = seat == 0 });
            ToAll(new NoticeMsg { Code = NetCode.PlayerBack, Seat = seat });
            if (back.Away && !back.Kicked && Phase == RoomPhase.InGame) SetAway(seat, false, g);
            SystemPauseCheck();
            BroadcastRoom();
            if (core != null)
            {
                streams[seat].Reset();
                core.Resend(seat); // 지금 보기(이벤트 없이) 스냅샷 — 화면 전체를 다시 그린다(02 §6-1)
            }
            else if (Phase == RoomPhase.Waiting && AllBack()) BeginResume(g);
        }

        /// <summary>접속하지 않은 좌석이 있나(강퇴된 좌석 뺌) — 자리 확인 대기를 받을 자리.</summary>
        bool AnyOpenSeat()
        {
            foreach (var m in seats)
                if (!m.Connected && !m.Kicked) return true;
            return false;
        }

        bool AllBack()
        {
            foreach (var m in seats)
                if (!m.Connected && !m.Kicked) return false;
            return true;
        }

        Claim ClaimOf(int conn)
        {
            foreach (var c in claims)
                if (c.Conn == conn) return c;
            return null;
        }

        void Handle(int conn, RoomMsg msg, long g)
        {
            int seat = SeatOf(conn);
            if (msg == null) return;
            if (seat < 0)
            {
                if (msg is LeaveRoom) DropClaim(conn); // 자리 확인 대기는 나가기만 받는다
                return;
            }
            bool host = seat == 0;
            switch (msg)
            {
                case SetProfile p:
                    if (string.IsNullOrEmpty(p.Nickname)) return;
                    Seated[seat].Nickname = p.Nickname;
                    BroadcastRoom();
                    return;
                case PickProtagonist pp:
                    // 좌석의 주인공 고르기(M9 다듬기 P06 — 결정 112 Q4): 로비에서만 · 고를 수 있는 여덟 가운데 · 남과 겹쳐도 된다 [가정 M7-94]. 준비는 풀지 않는다(겉모양뿐)
                    if (Phase != RoomPhase.Lobby || !Protagonists.Has(pp.Key) || slots[seat].Protagonist == pp.Key) return;
                    slots[seat].Protagonist = pp.Key;
                    BroadcastRoom();
                    return;
                case SetReady r:
                    if (Phase != RoomPhase.Lobby || host) return;
                    slots[seat].Ready = r.Ready;
                    BroadcastRoom();
                    return;
                case ChangeSettings c:
                    if (!host || c.Settings == null) return;
                    ChangeTo(c.Settings);
                    return;
                case KickFromLobby k:
                    if (!host || Phase != RoomPhase.Lobby || k.Seat <= 0 || k.Seat >= slots.Length || slots[k.Seat] == null) return;
                    Kick(k.Seat);
                    return;
                case StartGame _:
                    if (!host) return;
                    if (Phase == RoomPhase.Waiting) BeginResume(g); // [지금 재개] — 빠진 좌석은 「끊김」으로(02 §6-3)
                    else if (CanStart()) Begin(g);
                    return;
                case StartVote sv:
                    if (Phase == RoomPhase.InGame) OpenVote(seat, sv);
                    return;
                case CastVote cv:
                    if (Phase == RoomPhase.InGame) Cast(seat, cv, g);
                    return;
                case AssignSeat a:
                    if (host && Phase != RoomPhase.Lobby) Assign(a, g);
                    return;
                case RefuseClaim rc:
                    if (host) Refuse(rc.ClaimId);
                    return;
                case Submit sub:
                    if (Phase == RoomPhase.InGame || Phase == RoomPhase.Result) SubmitFrom(seat, sub.Command, sub.Epoch, g);
                    return;
                case UndoStep u:
                    // 개인 한 수 무르기(결정 149, undo.md §2-2): 게임 중 · 일시정지가 아닐 때만 — 못 하면(끈 방 · 낡은 To · 못 하는 때) 그 좌석에게만 UNDO_DENIED
                    if (core == null) return;
                    if (Phase == RoomPhase.InGame && !Paused && core.Undo(seat, u.To, g)) AfterRewind(false, seat, g);
                    else Send(seat, new NoticeMsg { Code = NetCode.UndoDenied, Seat = seat });
                    return;
                case RequestUndoPoints _:
                    // 되돌리기 목록(방장만 · 게임 중 · 결과 화면) — 요청한 방장에게만. 핵이 자유가 아니면 빈 목록을 준다
                    if (!host || core == null || (Phase != RoomPhase.InGame && Phase != RoomPhase.Result)) return;
                    Send(0, new UndoPointsMsg { Points = core.UndoPoints() });
                    return;
                case RewindTo rw:
                    // 방장의 지난 턴으로 되돌리기(방장만 · 게임 중 · 결과 화면, 일시정지 중에도 [가정 U7]) — 목록에 없으면 UNDO_DENIED
                    if (!host || core == null || (Phase != RoomPhase.InGame && Phase != RoomPhase.Result)) return;
                    if (core.Rewind(rw.To, 0, g)) AfterRewind(true, 0, g);
                    else Send(0, new NoticeMsg { Code = NetCode.UndoDenied, Seat = 0 });
                    return;
                case SetPaused sp:
                    if (Phase != RoomPhase.InGame) return;
                    if (sp.All)
                    {
                        if (host) ClearPauses(); // [모두 해제] — 방장만, 정지 · 예약 모두
                    }
                    else if (sp.On) AskPause(seat);
                    else ReleasePause(seat, host);
                    return;
                case MarkAway ma:
                    if (!host || Phase != RoomPhase.InGame || ma.Seat < 0 || ma.Seat >= seats.Count) return;
                    if (!seats[ma.Seat].Connected && !seats[ma.Seat].Away) SetAway(ma.Seat, true, g);
                    return;
                case SetAutoPass ap:
                    if (Phase == RoomPhase.InGame) AutoPassFor(seat, ap, g);
                    return;
                case SendSocial so:
                {
                    // 소통(결정 102, 02 §4-8 — 핑 · 감정 표현 · 도장): 게임 중 · 결과 화면에서 좌석이 있는 누구나(남의 턴 · 일시정지 · 자리 비움 중에도) — 방 층 메시지라 게임 상태 · 세이브 · 리플레이에 넣지 않고
                    // 자리 비움도 풀지 않는다. 로비 · 틀린 코드 · 허용 목록 밖 대상 · 연타 제한(바깥 시각)에 걸리면 조용히 버리고, 넘길 때는 허용된 칸만으로 새로 만든다. 지난 소통은 기억하지 않는다
                    if (Phase != RoomPhase.InGame && Phase != RoomPhase.Result) return;
                    var relay = SocialRules.Relay(so, seat, seats.Count, ctx.Board.Count, core != null ? core.NextSeq : 0);
                    if (relay == null || !social.TryTake(seat, so.Kind, lastWall)) return;
                    ToAll(relay);
                    return;
                }
                case SendPeek pk:
                {
                    // 남의 고르기 보기(M9 다듬기 P09, 02 §4-10): 게임 중 · 보낸 좌석이 그 자리의 공개 선택(조우 카드 · 사당 카드 · 내 턴의 은행상회 줄)을 지금 열고 있을 때만 — 보낸 좌석을 뺀 전원에게,
                    // 허용된 칸만으로 새로 만들어 넘긴다. 비공개 선택 · 범위 밖 · 빈도 한도(바깥 시각)는 조용히 버린다. 방 층 메시지라 게임 상태 · 세이브 · 시계 · 자리 비움과 무관하고 기억하지 않는다
                    if (Phase != RoomPhase.InGame || core == null) return;
                    var relay = PeekRules.Relay(pk, seat, core.Host);
                    if (relay == null || !peeks.TryTake(seat, lastWall)) return;
                    for (int i = 0; i < seats.Count; i++)
                        if (i != seat) Send(i, relay);
                    return;
                }
                case SendChat sc:
                {
                    // 방 채팅(결정 140 ②): 방 설정이 켠 방에서 자리가 있는 누구나(로비 · 게임 중 · 결과 · 대기실) — 방 층 메시지라 게임 상태 · 세이브 · 리플레이 · 시계 · 자리 비움과 무관하고 기억하지 않는다.
                    // 끈 방 · 강퇴된 좌석 · 빈 글 · 연타 제한(바깥 시각)은 조용히 버리고, 글은 다듬어(제어 글자 · 200자) 보낸 좌석까지 전원에게
                    if (Phase == RoomPhase.Closed || Seated[seat].Kicked) return;
                    var relay = ChatRules.Relay(sc, seat, settings);
                    if (relay == null || !chats.TryTake(seat, lastWall)) return;
                    ToAll(relay);
                    return;
                }
                case ReturnToLobby _:
                    if (host && Phase == RoomPhase.Result) BackToLobby();
                    return;
                case RequestResync _:
                    if (core == null) return;
                    streams[seat].Reset();
                    core.Resend(seat);
                    return;
                case LeaveRoom _:
                    Lost(conn);
                    return;
            }
        }

        void ChangeTo(RoomSettings wanted)
        {
            if (Phase == RoomPhase.Lobby)
            {
                var next = wanted.Clone();
                int needed = 1;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null) needed = i + 1;
                if (next.Problem(needed) != null) return;
                settings = next;
            }
            else
            {
                var next = settings.InGameChange(wanted);
                if (next.Problem(1) != null) return;
                settings = next;
                SyncAgents(); // 조종 · 세기는 다음 물음부터(이미 러너에 낸 물음은 그대로)
                if (clock != null) clock.Settings = settings.ClockLengths(); // 새 단위부터(다음 선택부터, 02 §3-2) — 이어하기 대기에서는 시작 때 이 값으로
            }
            BroadcastRoom();
            if (core != null) ToAll(new ClockUpdate { Clock = BuildClock(gameAt) });
        }

        void Kick(int seat)
        {
            var m = slots[seat];
            banned.Add(m.Token);
            slots[seat] = null;
            if (m.Connected)
            {
                link.Send(m.Conn, new NoticeMsg { Code = NetCode.Kicked, Seat = seat });
                link.Close(m.Conn, NetCode.Kicked);
            }
            BroadcastRoom();
        }

        /// <summary>[게임 시작]을 켤 수 있나. 이어하기 대기에서는 늘 켠다([지금 재개]).</summary>
        bool CanStart()
        {
            if (Phase == RoomPhase.Waiting) return true;
            if (Phase != RoomPhase.Lobby) return false;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var m = slots[i];
                if (m == null) continue;
                if (!m.Connected || (i > 0 && !m.Ready)) return false;
                n++;
            }
            return n >= RoomSettings.MinSeats;
        }

        int Occupied()
        {
            int n = 0;
            foreach (var m in slots)
                if (m != null) n++;
            return n;
        }

        // 판이 시작될 때 방장의 연출 조정 값(기본값과 다른 Host 값 — RoomState.Tune, 02 §3-2 [가정 M7-73])
        List<TuneValue> tune;

        /// <summary>게임 시작(02 §3-1): 빈 자리를 빼고 좌석 수 = 들어온 사람. 좌석 번호가 바뀌었으면 Welcome을 다시 보낸다.</summary>
        void Begin(long g)
        {
            seats.Clear();
            foreach (var m in slots)
                if (m != null) seats.Add(m);
            streams = new SeatStream[seats.Count];
            for (int i = 0; i < streams.Length; i++) streams[i] = new SeatStream();
            Phase = RoomPhase.InGame;
            clock = new TurnClock(settings.ClockLengths(), seats.Count, g);
            gameAt = g;
            tune = Tunables.Shared.HostChanges();
            for (int i = 0; i < seats.Count; i++) Send(i, new Welcome { YourSeat = i, RoomId = roomId, IsHost = i == 0 });
            BroadcastRoom();
            store = cfg.Store?.Invoke(saveId);
            core = LocalSession.Start(ctx, settings.ToOptions(), seats.Count, SeedOf(cfg.Seed), settings.Campaign, CoreConfig(), g);
        }

        /// <summary>판 번호로 섞은 시드(첫 판은 받은 값 그대로 — [로비로] 뒤 판마다 다른 판, 같은 방 설정이면 늘 같은 열 [가정 M7-25]).</summary>
        ulong SeedOf(ulong first)
        {
            if (games <= 1) return first;
            ulong z = unchecked(first + (ulong)games * 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        /// <summary>판의 저장을 닫는다(쓰기 스레드면 밀린 쓰기를 비우고 스레드를 끝낸다).</summary>
        void CloseStore()
        {
            var s = store;
            store = null;
            (s as IDisposable)?.Dispose();
        }

        /// <summary>
        /// 이어하기 대기 → 게임(02 §6-3): 저장의 마지막 쉬는 때로 이어 받고(<see cref="LocalSession.Resume"/> toRest), 뱅크를 되살린다. 열린 선택은 기본 시간을 새로 받는다(02 §5-7 「방이 닫힌 동안」).
        /// 접속하지 않은 좌석은 「끊김」 — 끊김 규칙(§6-1)대로 간다. 손님이 모두 없으면 시스템 정지. 이어 받지 못하면(로그 어긋남) 대기실로 돌아가고 던진다.
        /// </summary>
        void BeginResume(long g)
        {
            var rec = resumeFrom;
            streams = new SeatStream[seats.Count];
            for (int i = 0; i < streams.Length; i++) streams[i] = new SeatStream();
            Phase = RoomPhase.InGame;
            clock = new TurnClock(settings.ClockLengths(), seats.Count, g);
            if (rec.Head.Room.Bank != null) clock.Restore(rec.Head.Room.Bank, rec.Head.Room.BankRound);
            gameAt = g;
            tune = Tunables.Shared.HostChanges();
            int sent = outbox.Count;
            try
            {
                store = cfg.Store?.Invoke(saveId);
                core = LocalSession.Resume(ctx, rec, CoreConfig(), g, toRest: true);
            }
            catch
            {
                Phase = RoomPhase.Waiting;
                streams = null;
                clock = null;
                CloseStore();
                outbox.RemoveRange(sent, outbox.Count - sent);
                throw;
            }
            resumeFrom = null;
            BroadcastRoom();
            SystemPauseCheck();
            core.WriteHeadNow(); // 게임 시작 정지점 — 맡긴 좌석의 새 토큰(02 §7)
        }

        /// <summary>가상 응답자 AI 고르기(<see cref="VirtualAgents"/>)에 지금 방 설정(갤럭시단 조종 · 세기)을 맞춘다 — 게임 시작 · 이어하기 · 게임 중 설정 바꾸기 때.</summary>
        void SyncAgents()
        {
            if (!(cfg.VirtualAgent is VirtualAgents va)) return;
            va.GalacticLevel = settings.GalacticAi;
            va.GalacticHuman = settings.Galactic == Control.Human;
        }

        SessionConfig CoreConfig()
        {
            SyncAgents();
            return new SessionConfig
            {
                VirtualAgent = cfg.VirtualAgent, Ai = settings.ToAi(SeedOf(cfg.AiSeed)), ContentHash = cfg.ContentHash, SaveId = saveId,
                Out = this, HumanVirtual = v => Controller(v) >= 0, Applied = OnApplied, Protagonists = SeatProtagonists(),
                Store = store, SavedAt = cfg.SavedAt, BeforeHead = FillHead,
                Runner = cfg.Runner, AutoPass = c => c.Seat >= 0 && AutoPassOn(c.Seat, c.Sub), Undo = settings.Undo,
            };
        }

        string[] SeatProtagonists()
        {
            var who = new string[seats.Count];
            for (int i = 0; i < who.Length; i++) who[i] = Protagonists.ForSeat(seats[i].Protagonist, i);
            return who;
        }

        /// <summary>머리의 방 몫(02 §7): 방 설정 · 좌석(토큰·닉네임·PC/웹·강퇴됨) · 금지 토큰 · 뱅크.</summary>
        void FillHead(SaveHead h)
        {
            var r = new SaveRoom { Settings = settings.Clone(), BankRound = clock?.Round ?? 0 };
            for (int i = 0; i < seats.Count; i++)
            {
                var m = seats[i];
                r.Seats.Add(new SaveRoomSeat { Token = m.Token, Nickname = m.Nickname, Platform = m.Platform, Kicked = m.Kicked });
                r.Bank.Add(clock != null ? clock.BankMs(i) : 0);
            }
            r.Banned.AddRange(banned);
            r.Banned.Sort(string.CompareOrdinal);
            h.Room = r;
        }

        void OnApplied(List<GameEvent> events)
        {
            foreach (var e in events)
                if (e is ChoiceReissued r) clock?.Rename(r.OldId, r.NewId);
        }

        /// <summary>
        /// 좌석이 낸 게임 명령: 되돌림 알림 전에 낸 명령(epoch &lt; 핵의 Epoch)은 Rewound(결정 149), 일시정지 중의 답은 E_PAUSED, 가상 응답자 선택은 그 조종자만,
        /// 자리 비움 좌석이 무엇이든 내면 풀린다(02 §6-2).
        /// </summary>
        void SubmitFrom(int seat, Command cmd, int epoch, long g)
        {
            if (cmd == null)
            {
                Send(seat, new RejectedMsg { Reason = RejectReason.BadCommand, ChoiceId = -1 });
                return;
            }
            if (epoch < core.Epoch)
            {
                Send(seat, new RejectedMsg { Reason = RejectReason.Rewound, ChoiceId = cmd.ChoiceId });
                return;
            }
            if (Paused && (cmd.Kind == CommandKind.Answer || cmd.Kind == CommandKind.Propose))
            {
                Send(seat, new RejectedMsg { Reason = RejectReason.Paused, ChoiceId = cmd.ChoiceId });
                return;
            }
            int asSeat = seat;
            if (cmd.Kind == CommandKind.Answer)
            {
                var c = ChoiceRules.Find(core.Host, cmd.ChoiceId);
                if (c != null && c.Seat < 0 && !c.Answered)
                {
                    if (Controller(c.Seat) != seat || aiRequested.Contains(c.Id))
                    {
                        Send(seat, new RejectedMsg { Reason = RejectReason.NotYourChoice, ChoiceId = cmd.ChoiceId });
                        return;
                    }
                    asSeat = c.Seat;
                }
                // 차단 컷신(02 §5-7 E_HOLD): 그 좌석이 범위 안인 until-end가 그 좌석 화면에서 도는 동안. 사람 조종자는 그 배틀 당사자의 컷신도 [가정 M7-21]
                if (HeldAt(seat, g) || (asSeat < 0 && HeldAt(BattleAnchor(), g)))
                {
                    Send(seat, new RejectedMsg { Reason = RejectReason.Hold, ChoiceId = cmd.ChoiceId });
                    return;
                }
            }
            if (seats[seat].Away) SetAway(seat, false, g);
            submitter = seat;
            try
            {
                core.Submit(asSeat, cmd, g);
            }
            finally
            {
                submitter = -1;
            }
        }

        bool HeldAt(int seat, long g) => seat >= 0 && core.HoldEndOf(seat) > g;

        /// <summary>배틀의 첫 사람 쪽 좌석(없으면 −1).</summary>
        int BattleAnchor()
        {
            var b = core.Host.Battle;
            if (b != null)
                foreach (var side in b.Sides)
                    if (side.Owner >= 0) return side.Owner;
            return -1;
        }

        void Lost(int conn)
        {
            if (DropClaim(conn)) return;
            int seat = SeatOf(conn);
            if (seat < 0) return;
            if (Phase == RoomPhase.Lobby)
            {
                if (seat == 0) slots[0].Conn = -1;
                else slots[seat] = null;
                BroadcastRoom();
                return;
            }
            seats[seat].Conn = -1;
            streams?[seat].Reset();
            ToAll(new NoticeMsg { Code = NetCode.PlayerLeft, Seat = seat });
            if (vote != null && vote.Kind == VoteKind.Pause && vote.Target == seat) Finish(false, GameNow(lastWall)); // 요청한 좌석이 끊기면 기각(isekai) — 건 정지 · 예약은 남는다
            SystemPauseCheck();
            BroadcastRoom();
        }

        /// <summary>자리 비움 켬/끔(02 §6-2): 로그에 HostOp 줄(엔진 상태는 그대로), 전원에게 알림.</summary>
        void SetAway(int seat, bool on, long g)
        {
            var m = seats[seat];
            if (m.Away == on) return;
            m.Away = on;
            core.Inject(SeatId.System, Command.Host(on ? HostOpKind.MarkAway : HostOpKind.ClearAway, seat), g);
            ToAll(new NoticeMsg { Code = on ? NetCode.Away : NetCode.AwayCleared, Seat = seat });
        }

        // ── 투표(02 §6-2) ──

        /// <summary>
        /// 투표를 연다: 하나씩 · 대상은 연 좌석이 아니고 강퇴되지 않은 좌석 · 강퇴는 방장이 대상이 될 수 없다 · 자리 비움은 접속해 있고 자리 비움이 아닌 좌석만(끊긴 좌석은 방장의 MarkAway) ·
        /// 부결 뒤 같은 대상·종류는 <see cref="VoteCooldownMs"/> 동안 못 연다. 못 열면 연 좌석에게 VOTE_DENIED. 연 좌석은 찬성으로 친다.
        /// </summary>
        void OpenVote(int by, StartVote sv)
        {
            bool ok = vote == null && sv.Target >= 0 && sv.Target < seats.Count && sv.Target != by && Enum.IsDefined(typeof(VoteKind), sv.Kind) && sv.Kind != VoteKind.Pause; // 일시정지 투표는 SetPaused로만(AskPause)
            if (ok)
            {
                var t = seats[sv.Target];
                if (t.Kicked) ok = false;
                else if (sv.Kind == VoteKind.Kick) ok = sv.Target != 0;
                else ok = t.Connected && !t.Away;
            }
            if (ok && voteCooldown.TryGetValue(VoteKey(sv.Kind, sv.Target), out long until) && lastWall < until) ok = false;
            if (!ok)
            {
                Send(by, new NoticeMsg { Code = NetCode.VoteDenied, Seat = sv.Target, Vote = sv.Kind });
                return;
            }
            vote = new Vote { Id = ++voteIds, Kind = sv.Kind, Target = sv.Target, By = by, Deadline = lastWall + VoteMs };
            vote.Yes.Add(by);
            BroadcastRoom();
        }

        static string VoteKey(VoteKind kind, int target) => (int)kind + ":" + target;

        /// <summary>찬반: 대상이 아닌 좌석만 · 그 투표 Id에만. 반대 하나면 부결.</summary>
        void Cast(int seat, CastVote cv, long g)
        {
            if (vote == null || cv.VoteId != vote.Id || seat == vote.Target) return;
            if (!cv.Yes)
            {
                Finish(false, g);
                return;
            }
            if (vote.Yes.Contains(seat)) return;
            vote.Yes.Add(seat);
            vote.Yes.Sort();
            BroadcastRoom();
        }

        /// <summary>투표 판정(진입점마다): 마감이 지났으면 부결, 대상을 뺀 접속 좌석(강퇴 뺌)이 모두 찬성이면 가결. 투표할 좌석이 하나도 없으면 부결.</summary>
        void CheckVote(long g)
        {
            if (vote == null) return;
            if (Phase != RoomPhase.InGame || lastWall >= vote.Deadline)
            {
                Finish(false, g);
                return;
            }
            int voters = 0;
            for (int i = 0; i < seats.Count; i++)
            {
                if (!Voter(i)) continue;
                if (!vote.Yes.Contains(i)) return;
                voters++;
            }
            Finish(voters > 0 || vote.Kind == VoteKind.Pause, g); // 일시정지 투표는 투표할 좌석이 없으면 바로 통과(isekai)
        }

        bool Voter(int seat) => vote != null && seat != vote.Target && seats[seat].Connected && !seats[seat].Kicked;

        /// <summary>투표를 닫는다: 전원에게 VOTE_PASSED·VOTE_FAILED(대상 좌석·종류). 가결 → 강퇴(E_KICKED · 토큰 금지 · 끝까지 0초)나 자리 비움, 부결 → 같은 대상·종류 대기.</summary>
        void Finish(bool pass, long g)
        {
            var v = vote;
            vote = null;
            ToAll(new NoticeMsg { Code = pass ? NetCode.VotePassed : NetCode.VoteFailed, Seat = v.Target, Vote = v.Kind });
            if (v.Kind == VoteKind.Pause)
            {
                // 일시정지 투표(결정 112 Q6): 가결 = 예약(이미 멈춰 있으면 바로 더한다), 부결 뒤 대기는 없다
                if (pass && Phase == RoomPhase.InGame)
                {
                    if (Paused) SetPause(v.Target, true);
                    else Reserve(v.Target);
                }
                BroadcastRoom();
                return;
            }
            if (!pass) voteCooldown[VoteKey(v.Kind, v.Target)] = lastWall + VoteCooldownMs;
            else if (Phase == RoomPhase.InGame)
            {
                if (v.Kind == VoteKind.Kick) KickSeat(v.Target, g);
                else SetAway(v.Target, true, g);
            }
            BroadcastRoom();
        }

        /// <summary>강퇴(02 §6-2): 토큰 금지(저장에도) · 연결을 E_KICKED로 끊는다 · 좌석은 끝까지 자리 비움(0초 기본값 — AI 트레이너로 바꾸지 않는다, 04 U8) · 로그에 HostOp Kick · 머리를 바로 쓴다(정지점).</summary>
        void KickSeat(int seat, long g)
        {
            var m = seats[seat];
            m.Kicked = true;
            m.Away = true;
            banned.Add(m.Token);
            if (m.Connected)
            {
                link.Send(m.Conn, new NoticeMsg { Code = NetCode.Kicked, Seat = seat });
                link.Close(m.Conn, NetCode.Kicked);
                m.Conn = -1;
                streams[seat].Reset();
            }
            core.Inject(SeatId.System, Command.Host(HostOpKind.Kick, seat), g);
            DropPause(seat, g);
            SystemPauseCheck();
            core.WriteHeadNow();
        }

        // ── 자리 확인 대기(02 §6-3) ──

        /// <summary>방장이 자리 확인 대기의 연결을 접속하지 않은 좌석(강퇴 뺌)에 맡긴다: 그 좌석의 토큰 = 새 토큰, 닉네임·PC/웹도 새 연결 것. 그 뒤는 재접속과 같다. 머리를 바로 쓴다(정지점).</summary>
        void Assign(AssignSeat a, long g)
        {
            var c = claims.Find(x => x.Id == a.ClaimId);
            if (c == null || a.Seat < 0 || a.Seat >= seats.Count) return;
            var m = seats[a.Seat];
            if (m.Connected || m.Kicked) return;
            claims.Remove(c);
            m.Token = c.Token;
            if (!string.IsNullOrEmpty(c.Nickname)) m.Nickname = c.Nickname;
            Rejoin(a.Seat, c.Conn, c.Platform, g);
            core?.WriteHeadNow();
        }

        void Refuse(int claimId)
        {
            var c = claims.Find(x => x.Id == claimId);
            if (c == null) return;
            claims.Remove(c);
            link.Send(c.Conn, new NoticeMsg { Code = NetCode.SeatRefused });
            link.Close(c.Conn, NetCode.SeatRefused);
            BroadcastRoom();
        }

        /// <summary>그 연결이 자리 확인 대기였으면 목록에서 뺀다(끊김·나가기).</summary>
        bool DropClaim(int conn)
        {
            var c = ClaimOf(conn);
            if (c == null) return false;
            claims.Remove(c);
            BroadcastRoom();
            return true;
        }

        // ── 무르기 · 되돌리기(결정 149 · 154, undo.md §2-2) ──

        /// <summary>
        /// 핵이 되돌린 뒤 방 몫을 맞춘다: 러너에 낸 물음 · 묻지 않기 · AI 생각 · 시계 목록을 버리고, 방장 되돌리기면 시계를 새로 세운다(뱅크는 지금 값 — 쓴 시간을 돌려주지 않는다,
        /// 충전한 라운드도 지금 값 — 같은 라운드를 다시 지나도 다시 충전하지 않는다 [가정 U8]). 개인 무르기는 시계를 두고 Sync만(맵 턴 단위가 턴에 붙어 다시 차지 않는다).
        /// 결과 화면이었으면 게임 중으로. 되돌림 갱신(핵이 이미 outbox에 넣었다)은 <see cref="Flush"/>가 늘 스냅샷으로 보낸다.
        /// </summary>
        void AfterRewind(bool host, int by, long g)
        {
            aiRequested.Clear();
            autoPass.Clear();
            EndThink();
            clockList.Clear();
            if (host)
            {
                var banks = new long[seats.Count];
                for (int i = 0; i < banks.Length; i++) banks[i] = clock.BankMs(i);
                int round = clock.Round;
                clock = new TurnClock(settings.ClockLengths(), seats.Count, g);
                clock.Restore(banks, round);
            }
            if (Phase == RoomPhase.Result) Phase = RoomPhase.InGame;
            ToAll(new NoticeMsg { Code = host ? NetCode.Rewound : NetCode.Undone, Seat = by, Round = core.Host.Round });   // 되돌린 라운드는 알림에 싣는다(알림 띠 줄마다 제 라운드 — 갱신보다 먼저 닿는다)
            Sync(g);
            BroadcastRoom();
            ToAll(new ClockUpdate { Clock = BuildClock(g) });
        }

        // ── 이번 배틀 묻지 않기(04 §5 ①) ──

        /// <summary>
        /// SetAutoPass: 본인 · 배틀 중에만. 켜면 이 배틀 동안 그 좌석의 그 갈래 숨은 손패 창(HandGated)을 묻지 않고 최소 시간에 「안 씀」으로 답한다(쓸 카드가 없는 좌석과 같은 길 — 남에게 같은 이벤트·기다림).
        /// 지금 열린 그 창에도 「안 씀」을 낸다(최소 시간 전이면 핵이 그때까지 들고 있다 — 일시정지 중이면 내지 않는다). 배틀이 바뀌면 저절로 풀린다 [가정 M7-26].
        /// </summary>
        void AutoPassFor(int seat, SetAutoPass ap, long g)
        {
            var b = core.Host.Battle;
            if (b == null) return;
            autoPass.RemoveAll(x => x.Battle != b.Id || (x.Seat == seat && x.Sub == ap.Sub));
            if (!ap.On) return;
            autoPass.Add(new AutoPassRule { Seat = seat, Sub = ap.Sub, Battle = b.Id });
            if (Paused) return;
            var open = core.Host.Pending.Find(c => !c.Answered && c.HandGated && c.Seat == seat && c.Sub == ap.Sub);
            if (open != null) SubmitFrom(seat, Command.AnswerTo(open.Id, ChoiceAnswer.Of(AnswerKind.Decline)), core.Epoch, g);
        }

        bool AutoPassOn(int seat, ChoiceSub sub)
        {
            var b = core?.Host.Battle;
            if (b == null) return false;
            foreach (var x in autoPass)
                if (x.Seat == seat && x.Sub == sub && x.Battle == b.Id) return true;
            return false;
        }

        // ── 결과 → 로비 · 방 닫기 ──

        /// <summary>
        /// [로비로](02 §3-1 — 결과 S64 뒤 방장): 끝난 판의 저장을 마지막으로 쓰고 닫은 뒤 같은 방 · 같은 자리(게임의 좌석 번호 그대로)로 로비. 끊긴 좌석·강퇴된 좌석은 빈칸(방장 자리는 남긴다),
        /// 금지 토큰은 그대로, 준비는 다시. 자리 확인 대기의 연결은 빈칸이 있으면 앉히고(Welcome) 없으면 E_FULL. 다음 판 = 판 번호 +1 → 새 SaveId(「RoomId-n」)·새 시드 [가정 M7-25].
        /// </summary>
        void BackToLobby()
        {
            core.WriteFinal();
            CloseStore();
            if (Paused) pausedTotal += lastWall - pausedAt;
            pausedBy.Clear();
            pauseReserved.Clear();
            EndThink();
            for (int i = 0; i < slots.Length; i++) slots[i] = null;
            for (int i = 0; i < seats.Count && i < slots.Length; i++)
            {
                var m = seats[i];
                if (i > 0 && (!m.Connected || m.Kicked)) continue;
                m.Ready = false;
                m.Away = false;
                slots[i] = m;
            }
            seats.Clear();
            core = null;
            clock = null;
            streams = null;
            vote = null;
            aiRequested.Clear();
            autoPass.Clear();
            games++;
            saveId = roomId + "-" + games;
            Phase = RoomPhase.Lobby;
            foreach (var c in claims)
            {
                int seat = -1;
                for (int i = 0; i < settings.Seats && seat < 0; i++)
                    if (slots[i] == null) seat = i;
                if (seat < 0)
                {
                    link.Send(c.Conn, new NoticeMsg { Code = NetCode.Full });
                    link.Close(c.Conn, NetCode.Full);
                    continue;
                }
                slots[seat] = new Member { Token = c.Token, Nickname = c.Nickname, Platform = c.Platform, Conn = c.Conn, Protagonist = Protagonists.ForSeat(null, seat) };
                Send(seat, new Welcome { YourSeat = seat, RoomId = roomId, IsHost = false });
            }
            claims.Clear();
            BroadcastRoom();
        }

        /// <summary><see cref="Close"/>의 몸통: 마지막 쓰기 → 저장 닫기 → 밀린 메시지 → 모든 연결에 E_HOST_CLOSED → 끊기.</summary>
        void DoClose()
        {
            core?.WriteFinal();
            CloseStore();
            Flush();
            var list = Seated;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.Connected) continue;
                link.Send(m.Conn, new NoticeMsg { Code = NetCode.HostClosed });
                link.Close(m.Conn, NetCode.HostClosed);
                m.Conn = -1;
            }
            foreach (var c in claims)
            {
                link.Send(c.Conn, new NoticeMsg { Code = NetCode.HostClosed });
                link.Close(c.Conn, NetCode.HostClosed);
            }
            claims.Clear();
            vote = null;
            EndThink();
            Phase = RoomPhase.Closed;
        }

        // ── 일시정지(02 §5-7 — 결정 112 Q6: isekai 02 §6-7 규칙을 옮김 [가정 M7-88]) ──

        /// <summary>
        /// 일시정지를 건다(방 설정 <see cref="RoomSettings.Pause"/>): 방장만 = 방장이 누르면 바로(손님은 PAUSE_DENIED) · 모두 = 누구나 예약(내 차례나 내 선택이 오면 멈춘다 — 지금 열려 있으면 바로) ·
        /// 투표 = 다른 접속 좌석이 모두 찬성하면 예약(<see cref="PauseVoteMs"/> 안 · 반대 하나면 기각 · 이미 멈춰 있으면 투표 없이 더한다 · 다른 투표가 열려 있으면 VOTE_DENIED).
        /// </summary>
        void AskPause(int seat)
        {
            if (pausedBy.Contains(seat) || pauseReserved.Contains(seat)) return;
            switch (settings.Pause)
            {
                case PauseMode.HostOnly:
                    if (seat != 0) Send(seat, new NoticeMsg { Code = NetCode.PauseDenied, Seat = seat });
                    else SetPause(seat, true);
                    return;
                case PauseMode.Everyone:
                    Reserve(seat);
                    return;
                default:
                    if (Paused)
                    {
                        SetPause(seat, true);
                        return;
                    }
                    if (vote != null)
                    {
                        Send(seat, new NoticeMsg { Code = NetCode.VoteDenied, Seat = seat, Vote = VoteKind.Pause });
                        return;
                    }
                    vote = new Vote { Id = ++voteIds, Kind = VoteKind.Pause, Target = seat, By = seat, Deadline = lastWall + PauseVoteMs };
                    vote.Yes.Add(seat);
                    BroadcastRoom();
                    return;
            }
        }

        /// <summary>예약: 그 좌석에 지금 열린 선택이 있으면 바로 건다, 없으면 예약 목록에(<see cref="PromoteReserved"/>가 선택이 열릴 때 옮긴다).</summary>
        void Reserve(int seat)
        {
            if (pausedBy.Contains(seat) || pauseReserved.Contains(seat)) return;
            if (HasOpenChoice(seat))
            {
                SetPause(seat, true);
                return;
            }
            pauseReserved.Add(seat);
            pauseReserved.Sort();
            BroadcastRoom();
        }

        /// <summary>그 좌석에 답하지 않은 선택(제 것 · 조종하는 가상 응답자 것)이 열려 있나 — 「내 차례나 내 선택」.</summary>
        bool HasOpenChoice(int seat)
        {
            if (core == null) return false;
            foreach (var c in core.Host.Pending)
                if (!c.Answered && (c.Seat == seat || (c.Seat < 0 && Controller(c.Seat) == seat))) return true;
            return false;
        }

        /// <summary>예약한 좌석에 선택이 열렸으면 정지로 옮긴다(상태가 바뀔 때마다 — <see cref="Sync"/>).</summary>
        void PromoteReserved()
        {
            if (pauseReserved.Count == 0) return;
            bool moved = false;
            for (int i = pauseReserved.Count - 1; i >= 0; i--)
            {
                int seat = pauseReserved[i];
                if (!HasOpenChoice(seat)) continue;
                pauseReserved.RemoveAt(i);
                SetPause(seat, true);
                moved = true;
            }
            if (moved) BroadcastRoom();
        }

        /// <summary>[일시정지 풀기] · [예약 취소]: 내가 건 정지 · 예약만 푼다(가결 전이면 내 일시정지 투표를 거둔다). 방장은 시스템 정지도 함께 푼다. 모두 풀리면 재개.</summary>
        void ReleasePause(int seat, bool host)
        {
            bool room = pauseReserved.Remove(seat);
            if (vote != null && vote.Kind == VoteKind.Pause && vote.Target == seat)
            {
                vote = null;
                room = true;
            }
            if (room) BroadcastRoom();
            bool was = Paused;
            bool any = pausedBy.Remove(seat);
            if (host) any |= pausedBy.Remove(SystemPause);
            if (any) PauseChanged(was);
        }

        /// <summary>강퇴된 좌석의 정지 · 예약 · 일시정지 투표를 지운다(비면 재개).</summary>
        void DropPause(int seat, long g)
        {
            if (pauseReserved.Remove(seat)) BroadcastRoom();
            if (pausedBy.Contains(seat)) SetPause(seat, false);
            if (vote != null && vote.Kind == VoteKind.Pause && vote.Target == seat) Finish(false, g);
        }

        void SetPause(int who, bool on)
        {
            bool was = Paused;
            if (on)
            {
                if (pausedBy.Contains(who)) return;
                pausedBy.Add(who);
                pausedBy.Sort();
            }
            else if (!pausedBy.Remove(who)) return;
            PauseChanged(was);
        }

        /// <summary>[모두 해제](방장): 정지와 예약을 모두 지운다(시스템 정지 포함).</summary>
        void ClearPauses()
        {
            if (pauseReserved.Count > 0)
            {
                pauseReserved.Clear();
                BroadcastRoom();
            }
            if (!Paused) return;
            pausedBy.Clear();
            PauseChanged(true);
        }

        void PauseChanged(bool was)
        {
            if (!was && Paused)
            {
                EndThink(); // AI 생각으로 멈춘 시간을 먼저 뺀다 — 풀리면 다시 센다
                pausedAt = lastWall;
                core?.WriteHeadNow(); // 정지점(02 §7) — 뱅크
            }
            else if (was && !Paused) pausedTotal += lastWall - pausedAt;
            if (was != Paused) ToAll(new NoticeMsg { Code = Paused ? NetCode.Paused : NetCode.Resumed });
            ToAll(new ClockUpdate { Clock = BuildClock(GameNow(lastWall)) });
            // 무르기를 켠 방: 일시정지 중에 나간 제안은 「못 함(Paused)」이다 — 풀리면 지금 제안을 다시 보낸다(이벤트 없는 지금 보기, 다음 행동까지 [무르기]가 막힌 채 남지 않게)
            if (was && !Paused && core != null && core.UndoMode != UndoMode.Off && Phase == RoomPhase.InGame)
                for (int s = 0; s < core.SeatCount; s++) core.Resend(s);
        }

        /// <summary>기다릴 손님 좌석(강퇴 뺌)이 하나 이상인데 모두 끊기면 시스템 정지, 하나라도 돌아오거나 기다릴 손님이 없어지면 푼다(02 §5-7 · isekai 02 §6-7).</summary>
        void SystemPauseCheck()
        {
            if (Phase != RoomPhase.InGame) return;
            bool waitFor = false, anyGuest = false;
            for (int i = 1; i < seats.Count; i++)
            {
                if (seats[i].Kicked) continue;
                waitFor = true;
                if (seats[i].Connected) anyGuest = true;
            }
            bool want = waitFor && !anyGuest;
            if (want != pausedBy.Contains(SystemPause)) SetPause(SystemPause, want);
        }

        // ── 보내기 ──

        /// <summary>엔진 핵 → 좌석(SessionConfig.Out). 가상 좌석의 거절은 그 명령을 낸 조종자에게. 보내기는 방 진입점이 끝날 때(시계를 붙여) 한다.</summary>
        void ISessionLink.Send(int seat, ServerMsg msg)
        {
            if (seat >= 0 && seat < seats.Count) outbox.Add(new KeyValuePair<int, ServerMsg>(seat, msg));
            else if (msg is RejectedMsg && submitter >= 0) outbox.Add(new KeyValuePair<int, ServerMsg>(submitter, msg));
        }

        void Send(int seat, ServerMsg msg) => outbox.Add(new KeyValuePair<int, ServerMsg>(seat, msg));

        void ToAll(ServerMsg msg)
        {
            var list = Seated;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) Send(i, msg);
        }

        /// <summary>방 상태를 전원에게 — 방장은 자리 확인 대기 목록이 든 것, 손님은 빈 목록.</summary>
        void BroadcastRoom()
        {
            RoomState guest = null, owner = null;
            var list = Seated;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) Send(i, new RoomUpdate { Room = i == 0 ? owner ?? (owner = BuildRoom(true)) : guest ?? (guest = BuildRoom(false)) });
        }

        /// <summary>
        /// 모아 둔 메시지를 연결로 보낸다. 게임 갱신은 그 좌석의 스트림으로 스냅샷·델타가 되고, 끊긴 좌석의 갱신은 버리며 스트림을 잊는다(돌아오면 스냅샷).
        /// 게임 갱신·ClockUpdate의 시계는 보낼 때의 것을 좌석마다 붙인다(HoldLeftMs가 좌석마다 다르다).
        /// </summary>
        void Flush()
        {
            if (outbox.Count == 0) return;
            var pending = outbox.ToArray();
            outbox.Clear();
            ClockState now = null;
            long g = GameNow(lastWall);
            var list = Seated;
            foreach (var p in pending)
            {
                var msg = p.Value;
                var u = msg as GameUpdate;
                if (core != null && (u != null || msg is ClockUpdate))
                {
                    var seatClock = ClockFor(now ?? (now = BuildClock(g)), p.Key, g);
                    if (u != null) u.Clock = seatClock;
                    else msg = new ClockUpdate { Clock = seatClock };
                }
                if (p.Key < 0 || p.Key >= list.Count || list[p.Key] == null) continue;
                if (!list[p.Key].Connected)
                {
                    if (u != null && streams != null) streams[p.Key].Reset();
                    continue;
                }
                if (u?.Undo != null) Touch(u.Undo, p.Key);
                if (u != null && streams != null)
                {
                    if (u.Rewind != null) streams[p.Key].Reset(); // 되돌림 갱신은 늘 스냅샷(보기가 크게 뛴다 — 델타 기준을 믿지 않는다, undo.md §2-2)
                    msg = streams[p.Key].Encode(u);
                }
                link.Send(list[p.Key].Conn, msg);
            }
        }

        /// <summary>좌석에 가는 무르기 제안을 방 몫으로 손본다(핵이 좌석마다 새로 만든 객체): 일시정지 중이면 못 함(Paused), 되돌리기 목록은 방장만.</summary>
        void Touch(UndoOffer offer, int seat)
        {
            if (Paused)
            {
                offer.Block = UndoBlock.Paused;
                offer.StepTo = -1;
            }
            if (seat != 0) offer.HostList = false;
        }

        RoomState BuildRoom(bool forOwner)
        {
            var r = new RoomState { Phase = Phase, RoomId = roomId, Settings = settings.Clone(), CanStart = CanStart(), Records = cfg.Records };
            r.PausedBy.AddRange(pausedBy);
            r.PauseReserved.AddRange(pauseReserved);
            if (tune != null && (Phase == RoomPhase.InGame || Phase == RoomPhase.Result)) r.Tune.AddRange(tune);
            if (Phase == RoomPhase.Lobby)
                for (int i = 0; i < settings.Seats; i++) r.Seats.Add(Row(slots[i], i));
            else
                for (int i = 0; i < seats.Count; i++) r.Seats.Add(Row(seats[i], i));
            if (vote != null)
            {
                var v = new RoomVote { Id = vote.Id, Kind = vote.Kind, Target = vote.Target, By = vote.By, LeftMs = Math.Max(0, vote.Deadline - lastWall) };
                v.Yes.AddRange(vote.Yes);
                for (int i = 0; i < seats.Count; i++)
                    if (Voter(i) && !vote.Yes.Contains(i)) v.Waiting.Add(i);
                r.Vote = v;
            }
            if (forOwner)
                foreach (var c in claims) r.Claims.Add(new RoomClaim { Id = c.Id, Nickname = c.Nickname, Platform = c.Platform, Suggested = Suggest(c) });
            return r;
        }

        /// <summary>자리 확인 대기의 추천 좌석(02 §6-3): 닉네임이 같은 접속하지 않은 좌석(강퇴 뺌). 없으면 −1.</summary>
        int Suggest(Claim c)
        {
            if (Phase == RoomPhase.Lobby) return -1;
            for (int i = 0; i < seats.Count; i++)
                if (!seats[i].Connected && !seats[i].Kicked && seats[i].Nickname == c.Nickname) return i;
            return -1;
        }

        static RoomSeat Row(Member m, int seat) => m == null ? null : new RoomSeat
        {
            Nickname = m.Nickname, Platform = m.Platform, Host = seat == 0, Ready = m.Ready, Connected = m.Connected, Away = m.Away, Kicked = m.Kicked,
            Protagonist = m.Protagonist,
        };

        /// <summary>그 좌석에 보낼 시계: 공용 값 + 그 좌석의 차단 컷신 남은 ms. 목록은 공용 값과 같이 쓴다(읽기만).</summary>
        ClockState ClockFor(ClockState b, int seat, long g) => new ClockState
        {
            Preset = b.Preset, PausedBy = b.PausedBy, AnimLeftMs = b.AnimLeftMs, Seats = b.Seats,
            HoldLeftMs = core != null ? Math.Max(0, core.HoldEndOf(seat) - g) : 0,
        };

        ClockState BuildClock(long g)
        {
            var r = new ClockState { Preset = settings.Clock };
            r.PausedBy.AddRange(pausedBy);
            if (core == null) return r;
            r.AnimLeftMs = Math.Max(0, core.AnimEndMs - g);
            r.Seats = clock.Seats();
            for (int i = 0; i < r.Seats.Count; i++)
            {
                r.Seats[i].Away = seats[i].Away;
                r.Seats[i].Connected = seats[i].Connected;
            }
            return r;
        }
    }
}
