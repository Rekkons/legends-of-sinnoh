using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Client
{
    /// <summary>방에서 온 알림 하나(02 §4-3 Notice — 끊김 · 재접속 · 시간 초과 처리 · 자리 비움 · 일시정지 · 투표)와 받은 때(화면 시계 ms).</summary>
    public sealed class RoomNotice
    {
        public string Code;
        public int Seat = -1;
        public ChoiceKind Kind;
        public VoteKind Vote;
        /// <summary>무르기 · 되돌리기 알림의 되돌린 라운드(<see cref="NoticeMsg.Round"/> — 그 밖은 0).</summary>
        public int Round;
        public long AtMs;
    }

    /// <summary>받은 핑 하나(결정 97 · 102, 02 §4-8): 찍은 좌석 · 종류(0 여기 봐 · 1 ? · 2 좋아 · 3 조심) · 대상 · 받은 때(화면 시계 ms).</summary>
    public sealed class RoomPing
    {
        public int Seat;
        public int Code;
        public PingTarget Target;
        public long AtMs;
        /// <summary>보드 칸 핑이면 그 칸(아니면 −1).</summary>
        public int Cell => Target != null && Target.At == PingAt.Cell ? Target.N : -1;
    }

    /// <summary>받은 「남의 고르기」 하나(M9 다듬기 P09, 02 §4-10): 가리키는 좌석 · 자리 · 후보 안 자리 · 눌렀나 · 받은 때(화면 시계 ms).</summary>
    public sealed class RoomPeek
    {
        public int Seat;
        public PeekSpot Spot;
        public int Index;
        public bool Press;
        public long AtMs;
    }

    /// <summary>받은 감정 표현 하나(결정 102 ②): 보낸 좌석 · 문구 번호 · 받은 때(화면 시계 ms).</summary>
    public sealed class RoomEmote
    {
        public int Seat;
        public int Code;
        public long AtMs;
    }

    /// <summary>
    /// 받은 채팅 한 줄(결정 140 ②): 보낸 자리 번호 · 받을 때의 닉네임 · 주인공 · 좌석 색(로비는 회색 — 고향 마을 전) · 글 · 받은 때(화면 시계 ms).
    /// 로비 자리 번호와 게임 좌석 번호가 달라도 줄은 받을 때의 모습 그대로다.
    /// </summary>
    public sealed class RoomChatLine
    {
        public int Seat;
        public string Name;
        public string Protagonist;
        public UnityEngine.Color32 Color;
        public string Text;
        public long AtMs;
    }

    /// <summary>
    /// 방의 지금 모습(M7 「화면 ②」): 로비·대기실(<see cref="ILobbySource"/>)과 게임 화면(응답 시계 · 일시정지 · 투표 · 알림 · 재접속 덮개 S11 · 게임 메뉴의 방 줄)이 읽는 한 곳.
    /// 실제 방은 <see cref="NetRoom"/>을 매 프레임 <see cref="Pull"/>로 옮기고(받은 시계의 시각 · 끊긴 때 · 알림), 캡처·확인용은 값을 직접 채운 가짜다(<see cref="RoomFlow.ShowGame"/> — 망 없이).
    /// 시계는 받은 때(<see cref="ClockAtMs"/>)부터 화면에서만 줄인다(<see cref="ClockView"/>).
    /// </summary>
    public sealed class RoomLive : ILobbySource
    {
        /// <summary>알림을 보이는 시간(ms) [가정 M7-55].</summary>
        public const long NoticeMs = 5000;
        const int KeepNotices = 8;

        readonly NetRoom net;
        readonly Func<long> now;
        readonly List<RoomNotice> notices = new List<RoomNotice>();

        /// <summary>실제 방을 잇는다(now = 화면 시계 ms — RoomFlow의 Stopwatch).</summary>
        public RoomLive(NetRoom net, Func<long> now, IReadOnlyList<KeyValuePair<string, string>> addresses)
        {
            this.net = net ?? throw new ArgumentNullException(nameof(net));
            this.now = now ?? throw new ArgumentNullException(nameof(now));
            Addresses = addresses ?? new KeyValuePair<string, string>[0];
            net.Seat.Received += OnReceived;
            Pull();
        }

        /// <summary>가짜(캡처·확인용): 값을 직접 채운다. 보낸 것은 <see cref="Sent"/>에 적기만 한다.</summary>
        public RoomLive(Func<long> now)
        {
            this.now = now ?? throw new ArgumentNullException(nameof(now));
            Status = NetStatus.Connected;
        }

        /// <summary>망 없는 가짜인가.</summary>
        public bool Fake => net == null;
        public NetRoom Net => net;
        public long NowMs => now();

        // ── 로비(ILobbySource) ──

        public RoomState Room { get; set; }
        /// <summary>방 상태를 받은 때(화면 시계 ms — 투표 남은 시간을 화면에서 줄인다).</summary>
        public long RoomAtMs { get; set; }
        public int Seat { get; set; }
        public bool IsHost { get; set; }
        public string JoinCode { get; set; }
        public bool Listed { get; set; }
        public string ListFail { get; set; }
        public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; set; } = new KeyValuePair<string, string>[0];
        public int Port { get; set; }
        public bool Tunneled { get; set; }
        public IReadOnlyList<string> WebLinks { get; set; } = new string[0];
        public string WebNotice { get; set; }
        public bool WebPending { get; set; }

        /// <summary>보낸 방 메시지(가짜만 적는다 — 시험·캡처가 본다).</summary>
        public readonly List<RoomMsg> Sent = new List<RoomMsg>();

        public void Send(RoomMsg msg)
        {
            if (msg == null) return;
            if (net != null) net.Send(msg);
            else Sent.Add(msg);
        }

        // ── 게임 ──

        /// <summary>마지막으로 받은 시계와 받은 때(화면 시계 ms). 시계를 받기 전이면 null.</summary>
        public ClockState Clock { get; set; }
        public long ClockAtMs { get; set; }

        /// <summary>내 접속 상태 · 실패 이유(E_* · NET_*) · 끊길 때의 방 단계 · 다시 붙기 시작한 때(화면 시계 ms, 아니면 −1).</summary>
        public NetStatus Status { get; set; }
        public string FailCode { get; set; }
        public string FailDetail { get; set; }
        public RoomPhase? FailPhase { get; set; }
        public long LostAtMs { get; set; } = -1;

        /// <summary>방장: 판을 저장하나 · 쓰기 실패(<see cref="NetRunner.SaveProblems"/>) · 머리를 마지막으로 쓴 때(UTC ms).</summary>
        public bool Saving { get; set; }
        public List<string> SaveProblems { get; set; } = new List<string>();
        public long SavedAtMs { get; set; }

        /// <summary>[다시 시도]를 쓸 수 있는 실패인가(02 §6-1·§6-3): 게임 중 끊겨 3분 안에 못 붙었다(NET_LOST) · 게임 중에 방장이 방을 닫았다(E_HOST_CLOSED — 같은 주소로 다시 열면 붙는다).</summary>
        public bool CanRetry =>
            !IsHost && Status == NetStatus.Failed
            && ((FailCode == NetFail.Lost && NetClientSession.InRoom(Room?.Phase)) || (FailCode == NetCode.HostClosed && NetClientSession.InRoom(FailPhase)));

        /// <summary>게임 중 끊겨 3분 안에 못 붙은 실패 코드(NET_LOST — 캡처가 Sinnoh.Net 없이 쓴다).</summary>
        public const string LostCode = NetFail.Lost;

        /// <summary>확인용(가짜): 다시 붙는 중(끊긴 때 = 화면 시계 ms).</summary>
        public void FakeReconnecting(long lostAtMs)
        {
            Status = NetStatus.Reconnecting;
            LostAtMs = lostAtMs;
        }

        /// <summary>확인용(가짜): 실패(코드 · 끊길 때의 방 단계).</summary>
        public void FakeFailed(string code, RoomPhase? phase = null)
        {
            Status = NetStatus.Failed;
            FailCode = code;
            FailPhase = phase;
        }

        /// <summary>재접속 덮개가 뜨는 상태인가(다시 붙는 중 · 실패).</summary>
        public bool Lost => Status == NetStatus.Reconnecting || Status == NetStatus.Failed;

        /// <summary>시험·캡처: [다시 시도]를 누른 수(가짜만).</summary>
        public int Retried { get; private set; }

        /// <summary>[다시 시도]: 같은 토큰으로 다시 붙는다(재접속 창 3분을 다시 연다).</summary>
        public void Retry()
        {
            if (!CanRetry) return;
            if (net != null) net.Seat.Retry();
            else Retried++;
        }

        /// <summary>최근 알림(오래된 것부터 — 화면은 <see cref="NoticeMs"/> 안의 것만 보인다).</summary>
        public IReadOnlyList<RoomNotice> Notices => notices;

        /// <summary>알림을 더한다(가짜 · 실제 방의 받은 알림).</summary>
        public void Note(RoomNotice n)
        {
            if (n == null) return;
            notices.Add(n);
            if (notices.Count > KeepNotices) notices.RemoveAt(0);
        }

        // ── 소통(결정 97 · 102, 02 §4-8): 핑 · 감정 표현 · 도장 ──

        const int KeepPings = 18;   // 6좌석 × 한도 3
        /// <summary>감정 표현 말풍선을 보이는 시간(ms) [가정 M7-83].</summary>
        public const long EmoteMs = 3200;
        readonly List<RoomPing> pings = new List<RoomPing>();
        readonly List<RoomEmote> emotes = new List<RoomEmote>();
        readonly Dictionary<long, SortedDictionary<int, int>> marks = new Dictionary<long, SortedDictionary<int, int>>();
        readonly SocialRate rate = new SocialRate();
        readonly SocialMutes mutes = new SocialMutes();

        /// <summary>아직 보이는 핑(오래된 것부터 — <see cref="DropOldPings"/>가 4초 지난 것을 뺀다).</summary>
        public IReadOnlyList<RoomPing> Pings => pings;

        /// <summary>받은 핑 수(화면이 새 핑의 소리를 한 번 낸다).</summary>
        public int PingsSeen { get; private set; }

        /// <summary>보드 칸 가운데의 「여기 봐」 핑을 더한다(확인용 — 옛 꼴).</summary>
        public void AddPing(int seat, int cell, long atMs) => AddPing(seat, 0, PingTarget.Cell(cell), atMs);

        /// <summary>핑을 더한다(실제 방의 받은 핑 · 가짜).</summary>
        public void AddPing(int seat, int code, PingTarget target, long atMs)
        {
            if (target == null) return;
            pings.Add(new RoomPing { Seat = seat, Code = code, Target = target, AtMs = atMs });
            if (pings.Count > KeepPings) pings.RemoveAt(0);
            PingsSeen++;
        }

        /// <summary>보이는 시간(<see cref="PingRules.ShowMs"/>)이 지난 핑을 뺀다.</summary>
        public void DropOldPings()
        {
            long t = now();
            pings.RemoveAll(p => t - p.AtMs >= PingRules.ShowMs);
        }

        /// <summary>아직 보이는 감정 표현(좌석마다 마지막 하나).</summary>
        public IReadOnlyList<RoomEmote> Emotes => emotes;

        /// <summary>받은 감정 표현 수(화면이 소리를 한 번 낸다).</summary>
        public int EmotesSeen { get; private set; }

        /// <summary>감정 표현을 더한다(그 좌석의 앞 말풍선을 갈음한다).</summary>
        public void AddEmote(int seat, int code, long atMs)
        {
            emotes.RemoveAll(e => e.Seat == seat);
            emotes.Add(new RoomEmote { Seat = seat, Code = code, AtMs = atMs });
            EmotesSeen++;
        }

        public void DropOldEmotes()
        {
            long t = now();
            emotes.RemoveAll(e => t - e.AtMs >= EmoteMs);
        }

        /// <summary>받은 도장 수(화면이 소리를 한 번 내고 다시 그린다).</summary>
        public int MarksSeen { get; private set; }

        /// <summary>
        /// 그 행동(기록 줄의 Seq)의 그 좌석 줄(owner — 한 행동에 여러 좌석의 줄이 있을 수 있다: 동시 공개 등)에 찍힌 도장: 찍은 좌석 → 종류(0 대단해 · 1 헉 · 2 아깝다). 없으면 null.
        /// 한 사람 1개 — 다시 찍으면 바뀐다.
        /// </summary>
        public SortedDictionary<int, int> MarksOf(int seq, int owner) => marks.TryGetValue(MarkKey(seq, owner), out var m) ? m : null;

        static long MarkKey(int seq, int owner) => seq * 16L + owner;

        public void AddMark(int from, int seq, int owner, int code)
        {
            long key = MarkKey(seq, owner);
            if (!marks.TryGetValue(key, out var m)) marks[key] = m = new SortedDictionary<int, int>();
            m[from] = code;
            MarksSeen++;
        }

        /// <summary>그 좌석의 반응(감정 표현 · 도장) 또는 핑을 내 화면에서 숨기나(상대별 — 결정 102, isekai #190). 받는 쪽 거르기일 뿐이다 — 상대에게 알리지 않고 판이 끝나면 잊는다 [가정 M7-85].</summary>
        public bool Muted(int seat, bool pings) => mutes.Hides(seat, pings);

        /// <summary>그 좌석의 반응 · 핑 숨김을 켜고 끈다. 켜면 떠 있던 그 좌석의 핑 · 말풍선 · 도장도 걷는다.</summary>
        public void SetMuted(int seat, bool pings, bool on)
        {
            if (seat == Seat) return;
            mutes.Set(seat, pings, on);
            if (!on) return;
            if (pings)
            {
                this.pings.RemoveAll(p => p.Seat == seat);
                peeks.Remove(seat);
            }
            else
            {
                emotes.RemoveAll(e => e.Seat == seat);
                foreach (var m in marks.Values) m.Remove(seat);
                MarksSeen++;
            }
        }

        /// <summary>그 종류를 다시 보낼 수 있을 때까지 남은 ms(지금 되면 0 — 화면이 「잠시 뒤에」를 보인다).</summary>
        public long SocialWaitMs(SocialKind kind) => Seat < 0 ? 0 : rate.WaitMs(Seat, kind, now());

        /// <summary>
        /// 소통 하나를 방에 보낸다(표시는 방이 돌려준 것으로). 좌석이 없거나 코드 · 대상이 틀리거나 연타 제한(<see cref="SocialRate"/> — 방장과 같은 표)에 걸리면 보내지 않고 false.
        /// 도장은 남의 행동에만(owner ≠ 내 좌석).
        /// </summary>
        public bool Social(SocialKind kind, int code, PingTarget target = null, int seq = -1, int owner = -1)
        {
            if (Seat < 0 || code < 0 || code >= SocialCodes.Of(kind)) return false;
            if (kind == SocialKind.Ping && target == null) return false;
            if (kind == SocialKind.Mark && (seq < 0 || owner < 0 || owner == Seat)) return false;
            if (!rate.TryTake(Seat, kind, now())) return false;
            Send(new SendSocial { Kind = kind, Code = code, Target = kind == SocialKind.Ping ? target.Copy() : null, Seq = kind == SocialKind.Mark ? seq : -1, Owner = kind == SocialKind.Mark ? owner : -1 });
            return true;
        }

        /// <summary>그 칸 가운데에 「여기 봐」 핑(옛 꼴).</summary>
        public bool Ping(int cell) => cell >= 0 && Social(SocialKind.Ping, 0, PingTarget.Cell(cell));

        /// <summary>받은 소통을 화면 몫으로 옮긴다: 숨긴 좌석의 것은 버린다(내 것은 늘 보인다).</summary>
        public void Take(SocialMsg m)
        {
            if (!mutes.Shows(m, Seat)) return;
            switch (m.Kind)
            {
                case SocialKind.Ping:
                    AddPing(m.From, m.Code, m.Target, now());
                    break;
                case SocialKind.Emote:
                    AddEmote(m.From, m.Code, now());
                    break;
                case SocialKind.Mark:
                    AddMark(m.From, m.Seq, m.Owner, m.Code);
                    break;
            }
        }

        // ── 남의 고르기 보기(M9 다듬기 P09, 02 §4-10) ──

        readonly Dictionary<int, RoomPeek> peeks = new Dictionary<int, RoomPeek>();
        PeekSpot wantSpot, sentSpot;
        int wantIndex = -1, sentIndex = -1;
        bool wantPress, sentPress;
        long peekSentAt = long.MinValue / 2;

        /// <summary>그 좌석이 지금 가리키는 것(받은 마지막 소식 — 없으면 null).</summary>
        public RoomPeek PeekOf(int seat) => peeks.TryGetValue(seat, out var p) ? p : null;

        /// <summary>받은 소식 수(시험 · 확인).</summary>
        public int PeeksSeen { get; private set; }

        /// <summary>받은 것을 화면 몫으로 옮긴다: 내 것 · 핑을 숨긴 좌석의 것은 버린다(상대별 끄기의 「핑 끄기」를 따른다 [가정 M7-95]). 자리 −1 = 가리키지 않는다(지운다).</summary>
        public void TakePeek(PeekMsg m)
        {
            if (m == null || m.From == Seat || m.From < 0 || mutes.Hides(m.From, true)) return;
            PeeksSeen++;
            if (m.Index < 0) peeks.Remove(m.From);
            else peeks[m.From] = new RoomPeek { Seat = m.From, Spot = m.Spot, Index = m.Index, Press = m.Press, AtMs = now() };
        }

        /// <summary>
        /// 내가 지금 가리키는 것(공개 선택 화면이 부른다 — 조우 창 · 사당 판의 카드 고르기 · 은행상회 칸): 마지막 모습만 쥐고 <see cref="PeekRate.GapMs"/>마다 한 번 보낸다(<see cref="FlushPeek"/> — 사이의 것은 버린다).
        /// index −1 = 아무것도 가리키지 않는다. 좌석이 없으면 false.
        /// </summary>
        public bool Peek(PeekSpot spot, int index, bool press = false)
        {
            if (Seat < 0 || spot == PeekSpot.None) return false;
            wantSpot = spot;
            wantIndex = index < 0 ? -1 : index;
            wantPress = press && index >= 0;
            FlushPeek();
            return true;
        }

        /// <summary>쥐고 있는 마지막 모습이 보낸 것과 다르고 보내는 간격이 지났으면 보낸다(방 판 덮개 층이 매 프레임 부른다).</summary>
        public void FlushPeek()
        {
            if (wantSpot == PeekSpot.None || (wantIndex == sentIndex && wantPress == sentPress && (wantSpot == sentSpot || wantIndex < 0))) return;
            long t = now();
            if (t - peekSentAt < PeekRate.GapMs) return;
            peekSentAt = t;
            sentSpot = wantSpot;
            sentIndex = wantIndex;
            sentPress = wantPress;
            Send(new SendPeek { Spot = wantSpot, Index = wantIndex, Press = wantPress });
        }

        // ── 방 채팅(결정 140 ②) — 메모리에만(방을 떠나면 잊는다 · 저장 파일 · 플레이 기록 · 버그 신고 · 로그에 넣지 않는다) ──

        /// <summary>기억하는 줄 수(넘으면 오래된 것부터 버린다) [가정 plan §5 #753].</summary>
        public const int KeepChat = 200;
        readonly List<RoomChatLine> chat = new List<RoomChatLine>();
        readonly ChatRate chatRate = new ChatRate();

        /// <summary>받은 채팅(오래된 것부터, 많아야 <see cref="KeepChat"/>줄).</summary>
        public IReadOnlyList<RoomChatLine> Chat => chat;

        /// <summary>받은 채팅 줄 수(버린 줄도 센다 — 화면이 새 줄 · 안 읽은 줄을 센다).</summary>
        public int ChatSeen { get; private set; }

        /// <summary>방 설정의 채팅이 켜져 있나(방 상태가 없으면 끔 — 기본 끔).</summary>
        public bool ChatOn => Room != null && Room.Settings != null && Room.Settings.Chat;

        /// <summary>게임 중 좌석 색(받은 줄에 적어 둔다 — RoomFlow가 <see cref="GameFlow.SeatColor"/>를 넣는다). 없거나 로비면 회색.</summary>
        public Func<int, UnityEngine.Color32> ToneOf { get; set; }

        /// <summary>다시 보낼 수 있을 때까지 남은 ms(연타 제한 — 0 = 지금).</summary>
        public long ChatWaitMs => Seat < 0 ? 0 : chatRate.WaitMs(Seat, now());

        /// <summary>
        /// 채팅 한 줄을 방에 보낸다(표시는 방이 돌려준 것으로). 끈 방 · 자리 없음 · 빈 글 · 연타 제한(<see cref="ChatRate"/> — 방장과 같은 표)이면 보내지 않고 false.
        /// 글은 방장과 같이 다듬는다(<see cref="ChatRules.Clean"/> — 제어 글자 · 200자).
        /// </summary>
        public bool SendChat(string text)
        {
            if (!ChatOn || Seat < 0) return false;
            string clean = ChatRules.Clean(text);
            if (clean == null || !chatRate.TryTake(Seat, now())) return false;
            Send(new SendChat { Text = clean });
            return true;
        }

        /// <summary>받은 채팅을 기록에 더한다(실제 방의 받은 것 · 가짜): 받을 때의 닉네임 · 주인공 · 좌석 색을 함께 적는다.</summary>
        public void TakeChat(ChatMsg m)
        {
            if (m == null || string.IsNullOrEmpty(m.Text)) return;
            bool lobby = Room == null || Room.Phase == RoomPhase.Lobby;
            chat.Add(new RoomChatLine
            {
                Seat = m.From, Name = NameOf(m.From), Protagonist = ProtagonistOf(m.From),
                Color = lobby || ToneOf == null ? SeatColors.Unassigned : ToneOf(m.From), Text = m.Text, AtMs = now(),
            });
            if (chat.Count > KeepChat) chat.RemoveAt(0);
            ChatSeen++;
        }

        /// <summary>그 좌석의 닉네임(방 상태 — 없으면 null).</summary>
        public string NameOf(int seat) => Room != null && seat >= 0 && seat < Room.Seats.Count && Room.Seats[seat] != null ? Room.Seats[seat].Nickname : null;

        /// <summary>그 좌석의 주인공 키(방 상태 — 없으면 null, M9 다듬기 P06).</summary>
        public string ProtagonistOf(int seat) => Room != null && seat >= 0 && seat < Room.Seats.Count && Room.Seats[seat] != null ? Room.Seats[seat].Protagonist : null;

        // ── 무르기 · 되돌리기(결정 149 · 154 — undo.md §2-1 · §3-5) ──

        /// <summary>방장이 보낸 되돌리기 목록(새것부터 — <see cref="UndoPointsMsg"/>). 아직 받지 않았거나 낡았으면 null(화면은 「불러오는 중」).</summary>
        public List<UndoPoint> UndoPoints { get; set; }

        /// <summary>되돌리기 목록을 방장 세션에 달라고 한다(방장만 — 게임 메뉴의 「되돌리기」 쪽을 열 때). 받을 때까지 <see cref="UndoPoints"/>는 null — keep이면 받아 둔 목록을 새 목록이 올 때까지 둔다(쪽이 열린 채 판이 흘러 다시 받을 때).</summary>
        public void RequestUndoPoints(bool keep = false)
        {
            if (!keep) UndoPoints = null;
            Send(new RequestUndoPoints());
        }

        /// <summary>
        /// 판이 되돌려졌다(되돌림 표지가 실린 갱신 — GameFlow.Rewound): 선택 Id와 기록 줄 번호(Seq)가 다시 쓰이므로 그것으로 기억하던 것을 비운다 —
        /// 남의 고르기 보기 · 내가 보낸 고르기 · 지운 미래(nextSeq부터)의 기록 줄에 찍힌 도장 · 받은 되돌리기 목록. 핑은 4초면 사라져 그대로 둔다.
        /// 도장을 지워도 <see cref="MarksSeen"/>(새 도장 소리)는 늘리지 않는다 — 칩은 <see cref="RoomLayer.Rewound"/>가 다시 그리게 한다.
        /// </summary>
        public void Rewound(int nextSeq)
        {
            peeks.Clear();
            wantSpot = sentSpot = PeekSpot.None;
            wantIndex = sentIndex = -1;
            wantPress = sentPress = false;
            var gone = new List<long>();
            foreach (var k in marks.Keys) if (k / 16 >= nextSeq) gone.Add(k);
            foreach (var k in gone) marks.Remove(k);
            UndoPoints = null;
        }

        /// <summary>일시정지 중인가 · 내가 풀 수 있나(결정 112 Q6 [가정 M7-88] — 내가 건 것만, 방장은 시스템 정지도. 남의 것까지 푸는 것은 방장의 [모두 해제] <see cref="CanClearAll"/>).</summary>
        public bool Paused => Clock != null && Clock.PausedBy.Count > 0;
        public bool CanUnpause => Paused && (Clock.PausedBy.Contains(Seat) || (IsHost && Clock.PausedBy.Contains(HostSession.SystemPause)));

        /// <summary>방 설정의 일시정지를 거는 사람(방 상태가 없으면 기본 「방장만」).</summary>
        public PauseMode PauseMode => Room != null && Room.Settings != null ? Room.Settings.Pause : PauseMode.HostOnly;

        /// <summary>일시정지 예약한 좌석(없으면 빈 목록).</summary>
        public List<int> PauseReserved => Room != null && Room.PauseReserved != null ? Room.PauseReserved : NoSeats;

        static readonly List<int> NoSeats = new List<int>();

        /// <summary>내가 예약해 두었나 · 내 일시정지 투표가 열려 있나.</summary>
        public bool PauseMine => PauseReserved.Contains(Seat) || (Room?.Vote != null && Room.Vote.Kind == VoteKind.Pause && Room.Vote.Target == Seat);

        /// <summary>
        /// 내가 지금 일시정지를 걸 수 있나: 이미 걸었거나 예약 · 요청하지 않았고, 「방장만」이면 방장, 「투표」면 열린 투표가 없거나 이미 멈춰 있을 때(멈춰 있으면 투표 없이 더한다).
        /// </summary>
        public bool CanPause => Seat >= 0 && !(Clock != null && Clock.PausedBy.Contains(Seat)) && !PauseMine
            && (PauseMode != PauseMode.HostOnly || IsHost) && (PauseMode != PauseMode.Vote || Paused || Room?.Vote == null);

        /// <summary>방장의 [모두 해제]: 남이 건 정지나 예약이 있을 때(isekai — 내 것만이면 [일시정지 풀기]로).</summary>
        public bool CanClearAll
        {
            get
            {
                if (!IsHost) return false;
                foreach (int s in PauseReserved) if (s != Seat) return true;
                if (Clock != null) foreach (int s in Clock.PausedBy) if (s != Seat && s != HostSession.SystemPause) return true;
                return false;
            }
        }

        /// <summary>실제 방의 지금 값을 옮긴다(매 프레임 — RoomFlow.Tick). 가짜는 아무것도 하지 않는다.</summary>
        public void Pull()
        {
            if (net == null) return;
            var s = net.Seat;
            if (!ReferenceEquals(s.Clock, Clock))
            {
                Clock = s.Clock;
                ClockAtMs = now();
            }
            if (!ReferenceEquals(s.Room, Room)) RoomAtMs = now();
            Room = s.Room;
            Seat = s.Seat;
            IsHost = net.IsHost;
            var internet = net.Internet;
            JoinCode = internet != null ? internet.JoinText : null;
            Tunneled = internet != null && internet.Route == InternetRoom.GameRoute.Tunnel;
            if (internet != null)
            {
                WebNotice = internet.Notice;
                WebPending = internet.GuestLink == null && internet.Notice == null;
                if (internet.GuestLink == null) { if (WebLinks.Count > 0) WebLinks = new string[0]; }
                else if (WebLinks.Count != 1 || WebLinks[0] != internet.GuestLink) WebLinks = new[] { internet.GuestLink };
            }
            else if (net.IsHost && net.Port > 0)
            {
                WebNotice = net.WebNotice;
                WebPending = false;
                if (net.WebPort > 0 && WebLinks.Count == 0)
                {
                    var links = new List<string>();
                    foreach (var a in Addresses) links.Add(OnlineLinks.DirectPage(a.Key, net.WebPort, net.Port));
                    WebLinks = links;
                }
            }
            Listed = net.Internet != null && net.Internet.Listing != null && net.Internet.Listing.Published;
            ListFail = net.Internet != null ? net.Internet.ListFail : null;
            Port = net.Port;
            if (s.Status == NetStatus.Reconnecting && Status != NetStatus.Reconnecting) LostAtMs = now();
            else if (s.Status == NetStatus.Connected) LostAtMs = -1;
            Status = s.Status;
            FailCode = s.FailCode;
            FailDetail = s.FailDetail;
            FailPhase = s.FailPhase;
            Saving = net.Saving;
            if (net.IsHost)
            {
                SaveProblems = net.SaveProblems;
                SavedAtMs = net.SavedAtMs;
            }
        }

        /// <summary>실제 방의 알림 받기를 뗀다(방을 버릴 때).</summary>
        public void Detach()
        {
            if (net != null) net.Seat.Received -= OnReceived;
        }

        void OnReceived(ServerMsg msg)
        {
            if (msg is NoticeMsg n) Note(new RoomNotice { Code = n.Code, Seat = n.Seat, Kind = n.Kind, Vote = n.Vote, Round = n.Round, AtMs = now() });
            else if (msg is SocialMsg s) Take(s);
            else if (msg is PeekMsg p) TakePeek(p);
            else if (msg is UndoPointsMsg up) UndoPoints = up.Points ?? new List<UndoPoint>();
            else if (msg is ChatMsg c)
            {
                // 받은 줄의 이름 · 주인공은 이 글을 보낼 때의 방 상태로(같은 프레임에 온 RoomUpdate — 로비 → 게임 시작으로 자리 번호가 바뀌었을 수 있다)
                if (!ReferenceEquals(net.Seat.Room, Room))
                {
                    RoomAtMs = now();
                    Room = net.Seat.Room;
                }
                TakeChat(c);
            }
        }
    }
}
