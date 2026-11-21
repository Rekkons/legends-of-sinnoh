using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 좌석 하나의 손님 쪽(02 §1 「자기 보기만 그린다」): 링크로 받은 가장 새 보기(<see cref="View"/>)와 내 선택·합법 답을 주고, 답을 호스트에 낸다.
    /// 화면은 이것 하나로 지금 상태·선택지·이벤트를 받는다 — 호스트 원본을 만지지 않는다. 보기는 받을 때마다 새 사본이라 읽기만 한다.
    /// 갱신(<see cref="Updated"/>)은 호스트가 명령을 적용하는 자리에서 바로 온다(한 프로세스 링크) — 답을 낸 함수가 돌아오기 전에 불릴 수 있다.
    /// </summary>
    public sealed class SeatClient
    {
        readonly ISeatLink link;
        readonly Ctx ctx;

        public readonly int Seat;

        /// <summary>가장 새 보기(이 좌석의 ViewFor 사본). 아직 받은 것이 없으면 null.</summary>
        public GameState View { get; private set; }
        public int Version { get; private set; }
        /// <summary>방장 멈춤 끝(세션 시각 ms) — 그 전에는 연출이 돌고 있다(02 §5-7).</summary>
        public long AnimEndMs { get; private set; }
        /// <summary>마지막으로 받은 거절(없으면 null). 다음 갱신이 오면 지운다.</summary>
        public RejectedMsg LastRejected { get; private set; }
        /// <summary>받은 갱신 수(이벤트 없는 「지금 보기」도 센다).</summary>
        public int Updates { get; private set; }
        /// <summary>마지막 갱신의 「지금 무를 수 있나」(결정 149 — <see cref="GameUpdate.Undo"/>). 무르기를 끈 판은 null.</summary>
        public UndoOffer Undo { get; private set; }
        /// <summary>마지막으로 받은 되돌림 알림(<see cref="GameUpdate.Rewind"/>, 없으면 null — 다음 되돌림까지 남는다).</summary>
        public RewindNote LastRewind { get; private set; }
        /// <summary>마지막으로 받은 되돌림 횟수 — 호스트에 내는 모든 명령에 싣는다(<see cref="ClientMsg.Epoch"/>).</summary>
        public int Epoch { get; private set; }
        /// <summary>
        /// 마지막 갱신이 「모르고 지나간 되돌림」인가: 되돌림 표지는 없는데 제안의 Epoch가 내가 알던 것보다 크다 — 끊긴 동안 일어난 되돌림의 재접속 스냅샷(방장은 끊긴 좌석의 갱신을 버린다).
        /// 화면은 표지가 실린 갱신처럼 정리한다(기록 · 받은 방 몫 — undo.md [가정 U9]). 첫 갱신은 정리할 것이 없어 늘 false.
        /// </summary>
        public bool MissedRewind { get; private set; }

        /// <summary>새 보기를 받았다. 이벤트(이 좌석에 거른 것)는 연출용이고 진실은 보기다(02 §4-4).</summary>
        public event Action<GameUpdate> Updated;
        public event Action<RejectedMsg> Rejected;

        /// <summary>링크의 좌석 하나에 붙는다. 붙는 즉시 지금 보기가 온다(망 위의 방 좌석이면 받아 둔 마지막 보기).</summary>
        public SeatClient(ISeatLink link, Ctx ctx, int seat)
        {
            this.link = link ?? throw new ArgumentNullException(nameof(link));
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Seat = seat;
            link.Connect(seat, Receive);
        }

        void Receive(ServerMsg msg)
        {
            if (msg is GameUpdate u)
            {
                View = u.View;
                Version = u.Version;
                AnimEndMs = u.AnimEndMs;
                MissedRewind = u.Rewind == null && u.Undo != null && Updates > 0 && u.Undo.Epoch > Epoch;
                Undo = u.Undo;
                if (u.Undo != null) Epoch = u.Undo.Epoch;
                if (u.Rewind != null)
                {
                    LastRewind = u.Rewind;
                    Epoch = u.Rewind.Epoch;
                }
                LastRejected = null;
                Updates++;
                Updated?.Invoke(u);
            }
            else if (msg is RejectedMsg r)
            {
                LastRejected = r;
                Rejected?.Invoke(r);
            }
        }

        /// <summary>내가 아직 답하지 않은 그 종류의 선택(없으면 null).</summary>
        public PendingChoice Choice(ChoiceKind kind)
        {
            if (View == null) return null;
            foreach (var c in View.Pending)
                if (c.Seat == Seat && !c.Answered && c.Kind == kind) return c;
            return null;
        }

        /// <summary>내가 아직 답하지 않은 첫 선택(없으면 null).</summary>
        public PendingChoice FirstChoice()
        {
            if (View == null) return null;
            foreach (var c in View.Pending)
                if (c.Seat == Seat && !c.Answered) return c;
            return null;
        }

        /// <summary>그 선택에서 둘 수 있는 답 전부(Engine.Legal — 보기 사본에서도 자기 선택이면 호스트와 같다, 01 §9-2).</summary>
        public List<ChoiceAnswer> Legal(PendingChoice choice)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            var into = new List<ChoiceAnswer>();
            Engine.Legal(View, ctx, choice, into);
            return into;
        }

        /// <summary>선택에 답한다(세션 시각 nowMs). 거절이면 <see cref="LastRejected"/>에 남는다.</summary>
        public SubmitResult Answer(PendingChoice choice, ChoiceAnswer answer, long nowMs)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            return link.ToHost(Seat, new ClientMsg { Command = Command.AnswerTo(choice.Id, answer), Epoch = Epoch }, nowMs);
        }

        /// <summary>
        /// 그 정비를 지금 낼 수 있나(<see cref="MaintenanceRules.Check"/> — 호스트가 받을 때와 같은 검사를 보기 사본에서 돈다: 내 파티·가방·기술 풀·배틀·열린 선택은 내 보기에 그대로 있다).
        /// 화면은 이 값으로 칸을 켜고 끈다(규칙을 다시 세지 않는다). None = 낼 수 있다.
        /// </summary>
        public RejectReason CanMaintain(Maintenance m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            return View == null ? RejectReason.BadCommand : MaintenanceRules.Check(View, ctx, Seat, m);
        }

        /// <summary>정비(언제든 행동, 01 §3-4)를 낸다. 열린 선택에 답하지 않는다. 거절이면 결과에 이유가 온다.</summary>
        public SubmitResult Maintain(Maintenance m, long nowMs)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            return link.ToHost(Seat, new ClientMsg { Command = Command.Anytime(m), Epoch = Epoch }, nowMs);
        }

        /// <summary>남의 턴에 원격 거래를 제안한다(명령 Propose — 결정 75 B5 나, <see cref="TradeRules.SubmitOffer"/>). 열린 선택에 답하지 않는다.</summary>
        public SubmitResult Propose(ChoiceAnswer offer, long nowMs)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            return link.ToHost(Seat, new ClientMsg { Command = Command.ProposeTrade(offer), Epoch = Epoch }, nowMs);
        }

        /// <summary>링크에서 떨어진다(게임을 떠날 때).</summary>
        public void Close() => link.Disconnect(Seat);
    }
}
