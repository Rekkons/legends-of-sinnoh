using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>손님 → 호스트(02 §4-2 Submit의 최소판). 정비는 ChoiceId 없이 Command.Maint.</summary>
    public sealed class ClientMsg
    {
        public Command Command;
        /// <summary>낸 좌석이 마지막으로 받은 되돌림 횟수(<see cref="UndoOffer.Epoch"/> — <see cref="SeatClient"/>가 싣는다). 방은 지금보다 작으면 <see cref="RejectReason.Rewound"/>로 거절한다(결정 149).</summary>
        public int Epoch;
    }

    /// <summary>호스트 → 손님(02 §4-3). 방 메시지(Welcome·RoomUpdate·NoticeMsg)는 RoomMessages.cs, 스냅샷·델타(GameSnapshot·GameDelta)는 GameStream.cs.</summary>
    public abstract class ServerMsg { }

    /// <summary>
    /// 갱신 하나(한 프로세스 안의 꼴): 그 좌석의 보기 사본 전체 + 그 좌석에 거른 이벤트 + 방장 멈춤 끝(AnimEndMs, 세션 시각).
    /// 솔로는 이것을 그대로 받고, 방(<see cref="HostSession"/>)은 연결에 보낼 때 <see cref="SeatStream"/>으로 스냅샷·델타로 바꾸며 손님 쪽 <see cref="StreamReceiver"/>가 다시 이 꼴로 되돌린다.
    /// 이벤트는 연출용이고 진실은 보기다(02 §4-4).
    /// </summary>
    public sealed class GameUpdate : ServerMsg
    {
        public int Version;
        public GameState View;
        public List<GameEvent> Events;
        public long AnimEndMs;
        /// <summary>방의 시계(02 §5-7 ClockState — <see cref="HostSession"/>이 붙인다). 솔로(LocalSession 바로)는 null.</summary>
        public ClockState Clock;
        /// <summary>이 좌석이 지금 무를 수 있나(결정 149 — 좌석마다, <see cref="LocalSession.OfferFor"/>). 무르기를 끈 판은 null.</summary>
        public UndoOffer Undo;
        /// <summary>되돌린 직후의 갱신에만(모든 좌석) — 화면은 이것을 받으면 연출 · 창을 걷고 보기에서 다시 세운다(이어하기와 같은 길). 평소 null.</summary>
        public RewindNote Rewind;
    }

    /// <summary>명령 거절(02 §4-3 Rejected). NoChange(정비 같은 값)는 보내지 않는다(rulings G8).</summary>
    public sealed class RejectedMsg : ServerMsg
    {
        public RejectReason Reason;
        public int ChoiceId;
    }

    /// <summary>호스트 → 바깥(02 §1-1 ISessionLink). 좌석 하나에 보낸다.</summary>
    public interface ISessionLink
    {
        void Send(int seat, ServerMsg msg);
    }

    /// <summary>
    /// 좌석 손님(<see cref="SeatClient"/>) → 호스트 쪽 길(M7 화면 ① [가정 M7-41]): 한 프로세스(<see cref="LoopbackLink"/>)든 망 위(Net의 NetSeatLink — 방의 내 좌석)든 같은 꼴이다.
    /// 붙으면 그 좌석의 지금 보기가 온다. 망 위의 ToHost는 보내기만 하고 Ok를 돌려준다(거절은 나중에 <see cref="RejectedMsg"/>로 온다).
    /// </summary>
    public interface ISeatLink
    {
        void Connect(int seat, Action<ServerMsg> onReceive);
        void Disconnect(int seat);
        SubmitResult ToHost(int seat, ClientMsg msg, long nowMs);
    }

    /// <summary>
    /// 한 프로세스 안의 링크(02 §1-3 솔로). 소켓·직렬화 없이 동기로 메시지 객체를 넘긴다 — 보기는 매번 새 사본이라 받는 쪽은 읽기만 한다.
    /// 좌석마다 받는 함수 하나. 연결이 없는 좌석(AI 좌석 등)에 보낸 메시지는 버린다.
    /// </summary>
    public sealed class LoopbackLink : ISessionLink, ISeatLink
    {
        readonly Action<ServerMsg>[] clients = new Action<ServerMsg>[SeatId.MaxSeats];
        LocalSession host;

        internal void Attach(LocalSession session) => host = session;

        /// <summary>좌석 하나를 붙인다. 붙이면 그 좌석의 지금 보기가 바로 온다(이벤트 없이).</summary>
        public void Connect(int seat, Action<ServerMsg> onReceive)
        {
            if (seat < 0 || seat >= SeatId.MaxSeats) throw new ArgumentOutOfRangeException(nameof(seat), seat, "좌석 0~5");
            clients[seat] = onReceive ?? throw new ArgumentNullException(nameof(onReceive));
            host?.Resend(seat);
        }

        public void Disconnect(int seat) => clients[seat] = null;

        /// <summary>좌석이 명령을 보낸다(세션 시각 nowMs).</summary>
        public SubmitResult ToHost(int seat, ClientMsg msg, long nowMs)
        {
            if (host == null) throw new InvalidOperationException("세션에 붙지 않은 링크");
            if (msg == null || msg.Command == null) return SubmitResult.Rejected(RejectReason.BadCommand);
            return host.Submit(seat, msg.Command, nowMs);
        }

        void ISessionLink.Send(int seat, ServerMsg msg)
        {
            if (seat >= 0 && seat < clients.Length) clients[seat]?.Invoke(msg);
        }
    }
}
