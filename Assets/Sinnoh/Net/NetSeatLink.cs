using System;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Net
{
    /// <summary>
    /// 방의 내 좌석을 화면의 좌석 손님(<see cref="SeatClient"/>)에 잇는 링크(M7 화면 ① [가정 M7-41]): 받은 갱신(<see cref="NetClientSession.Updated"/> — 스냅샷·델타를 되읽은 보기)과
    /// 거절을 그대로 넘기고, 답·정비·제안은 <see cref="Submit"/>으로 보낸다(호스트 자리도 같은 길 — NGO 안 루프백, 02 §1-1). 보내기는 곧바로 Ok이고 결과는 다음 갱신·거절로 온다.
    /// 붙을 때 받아 둔 마지막 보기가 있으면 바로 넘긴다(솔로의 「붙으면 지금 보기」와 같은 꼴).
    /// </summary>
    public sealed class NetSeatLink : ISeatLink
    {
        readonly NetClientSession session;
        readonly Action<RoomMsg> send;
        Action<ServerMsg> receive;

        /// <param name="send">보내는 곳(null = session.Send). 호스트 자리는 <see cref="NetHostSession.Send"/>.</param>
        public NetSeatLink(NetClientSession session, Action<RoomMsg> send = null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.send = send ?? session.Send;
        }

        /// <summary>보낸 명령 수(시험·로그).</summary>
        public int Sent { get; private set; }

        public void Connect(int seat, Action<ServerMsg> onReceive)
        {
            Disconnect(seat);
            receive = onReceive ?? throw new ArgumentNullException(nameof(onReceive));
            session.Updated += OnUpdated;
            session.Received += OnReceived;
            if (session.Latest != null) receive(session.Latest);
        }

        public void Disconnect(int seat)
        {
            session.Updated -= OnUpdated;
            session.Received -= OnReceived;
            receive = null;
        }

        public SubmitResult ToHost(int seat, ClientMsg msg, long nowMs)
        {
            if (msg == null || msg.Command == null || session.Status != NetStatus.Connected) return SubmitResult.Rejected(RejectReason.BadCommand);
            Sent++;
            send(new Submit { Command = msg.Command, Epoch = msg.Epoch });
            return SubmitResult.Ok;
        }

        void OnUpdated(GameUpdate u) => receive?.Invoke(u);

        void OnReceived(ServerMsg msg)
        {
            if (msg is RejectedMsg) receive?.Invoke(msg);
        }
    }
}
