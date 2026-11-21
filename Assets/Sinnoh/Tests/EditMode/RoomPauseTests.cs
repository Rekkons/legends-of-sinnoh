using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 결정 112 Q6(isekai 02 §6-7 · PauseTests U103 · U103b · U103c를 옮김 — 02 §11-1 [가정 M7-88]): 방 설정 <see cref="RoomSettings.Pause"/>
    /// 방장만(방장이 누르면 바로 · 손님은 PAUSE_DENIED) · 모두(누구나 예약 — 내 차례나 내 선택이 오면 멈춘다) · 투표(다른 접속 좌석이 모두 찬성하면 예약 · 30초 · 반대 하나면 부결),
    /// 건 사람이 자기 것만 풀고 모두 풀리면 재개 · 방장의 [모두 해제] · 강퇴된 좌석의 정지는 빠진다 · 기다릴 손님이 없으면 시스템 정지가 풀린다.
    /// </summary>
    public partial class RoomTests
    {
        static RoomSettings PauseBy(PauseMode mode) => new RoomSettings { Pause = mode };

        /// <summary>g 말고 열린 선택이 없는 손님 좌석.</summary>
        static int IdleGuest(Room r, int g)
        {
            for (int s = 1; s < r.Host.SeatCount; s++)
                if (s != g && r.Mine(s) == null) return s;
            return -1;
        }

        [Test]
        public void M7_일시정지_방장만모드_방장은바로_손님은PAUSE_DENIED_건사람만풀고_방설정기본은방장만()
        {
            Assert.AreEqual(PauseMode.HostOnly, new RoomSettings().Pause, "기본 = 방장만(isekai)");
            Assert.AreEqual("enum", new RoomSettings { Pause = (PauseMode)9 }.Problem(1));
            var r = new Room(ctx, Cfg(), 3);
            int g = ToGuestMapAction(r);
            Assert.AreEqual(PauseMode.HostOnly, r.Net.Last<RoomUpdate>(r.Conn[g]).Room.Settings.Pause, "손님도 방 설정을 본다");

            // 손님은 못 건다 — 그 좌석에게만 PAUSE_DENIED
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = true }, r.Now);
            Assert.IsFalse(r.Host.Paused);
            Assert.AreEqual(1, r.Notices(g, NetCode.PauseDenied).Count);
            Assert.AreEqual(0, r.Notices(0, NetCode.PauseDenied).Count);
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved, "방장만 모드에는 예약이 없다");

            // 방장은 남의 차례에도 바로 멈춘다 → 다시 누르면 풀린다
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            CollectionAssert.AreEqual(new[] { 0 }, r.Host.Clock.PausedBy);
            Assert.AreEqual(1, r.Notices(g, NetCode.Paused).Count);
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = false }, r.Now);
            Assert.IsTrue(r.Host.Paused, "손님은 방장의 정지를 풀지 못한다");
            r.Host.OnMessage(r.Conn[g], new SetPaused { All = true }, r.Now);
            Assert.IsTrue(r.Host.Paused, "[모두 해제]는 방장만");
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);
            Assert.IsFalse(r.Host.Paused);
            Assert.AreEqual(1, r.Notices(g, NetCode.Resumed).Count);
        }

        [Test]
        public void M7_일시정지_모두모드_남의차례에걸면예약_내선택이오면정지로옮겨간다_예약취소_모두해제()
        {
            var r = new Room(ctx, Cfg(PauseBy(PauseMode.Everyone)), 3);
            int g = ToGuestMapAction(r);
            int o = IdleGuest(r, g);
            Assert.GreaterOrEqual(o, 1, "선택이 없는 다른 손님");

            // 선택이 없는 좌석(o)이 걸면 예약 — 게임은 그대로, 방 상태에 예약이 실린다
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            Assert.IsFalse(r.Host.Paused, "예약은 멈추지 않는다");
            CollectionAssert.AreEqual(new[] { o }, r.Host.Room.PauseReserved);
            CollectionAssert.AreEqual(new[] { o }, r.Net.Last<RoomUpdate>(r.Conn[g]).Room.PauseReserved, "모두가 예약을 본다");
            // 예약 취소(끔) → 목록에서 빠진다
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = false }, r.Now);
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved);

            // 지금 선택이 열린 좌석(g)이 걸면 바로 멈춘다 · 답은 E_PAUSED
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = true }, r.Now);
            CollectionAssert.AreEqual(new[] { g }, r.Host.Clock.PausedBy);
            var map = r.Mine(g);
            r.Host.OnMessage(r.Conn[g], new Submit { Command = Command.AnswerTo(map.Id, map.Default.Clone()) }, r.Now);
            Assert.AreEqual(RejectReason.Paused, r.Net.Last<RejectedMsg>(r.Conn[g]).Reason);
            // 멈춘 동안 다른 좌석의 걸기는 예약(선택이 없다) · 방장 [모두 해제] = 정지와 예약 모두
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            CollectionAssert.AreEqual(new[] { o }, r.Host.Room.PauseReserved);
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);
            Assert.IsTrue(r.Host.Paused, "방장의 [일시정지 풀기]는 제 것(과 시스템 정지)만 — 손님의 정지는 남는다");
            r.Host.OnMessage(r.Conn[0], new SetPaused { All = true }, r.Now);
            Assert.IsFalse(r.Host.Paused);
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved, "[모두 해제]는 예약도 지운다");

            // 예약 → 그 좌석의 차례(선택)가 오면 정지로 옮겨 간다
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            CollectionAssert.AreEqual(new[] { o }, r.Host.Room.PauseReserved);
            r.Until(() => r.Host.Paused);
            CollectionAssert.AreEqual(new[] { o }, r.Host.Clock.PausedBy, "예약한 좌석의 선택이 열렸다");
            Assert.IsNotNull(r.Host.Game.HostCopy().Pending.Find(c => c.Seat == o && !c.Answered), "o의 선택이 열려 있다");
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved, "예약은 정지로 옮겨 갔다");
            Assert.AreEqual(long.MaxValue, r.Host.NextEventMs, "시계가 멈췄다");
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = false }, r.Now);
            Assert.IsFalse(r.Host.Paused, "건 사람이 풀면 재개");
        }

        [Test]
        public void M7_일시정지_투표모드_반대면부결_모두찬성이면예약_정지중이면투표없이더한다_요청취소_요청좌석이끊기면부결()
        {
            var r = new Room(ctx, Cfg(PauseBy(PauseMode.Vote)), 3);
            int g = ToGuestMapAction(r);
            int o = IdleGuest(r, g);

            // o의 요청 → 일시정지 투표(대상 = 요청한 좌석 · 30초 · 투표할 좌석 = 나머지 접속 좌석)
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            var v = r.Host.Room.Vote;
            Assert.IsNotNull(v);
            Assert.AreEqual((VoteKind.Pause, o), (v.Kind, v.Target));
            Assert.AreEqual(HostSession.PauseVoteMs, v.LeftMs);
            CollectionAssert.AreEquivalent(new[] { 0, g }, v.Waiting);
            Assert.IsFalse(r.Host.Paused, "투표 중에도 게임은 그대로");
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.AreEqual(v.Id, r.Host.Room.Vote.Id, "투표는 하나씩");
            var busy = r.Notices(0, NetCode.VoteDenied);
            Assert.AreEqual(1, busy.Count);
            Assert.AreEqual(VoteKind.Pause, busy[0].Vote);
            r.Host.OnMessage(r.Conn[0], new StartVote { Target = o, Kind = VoteKind.Pause }, r.Now);
            Assert.AreEqual(v.Id, r.Host.Room.Vote.Id, "일시정지 투표는 StartVote로 열지 않는다");
            // 반대 하나면 부결(대기 없이 다시 요청할 수 있다)
            r.Host.OnMessage(r.Conn[g], new CastVote { VoteId = v.Id, Yes = false }, r.Now);
            Assert.IsNull(r.Host.Room.Vote);
            var failed = r.Notices(0, NetCode.VoteFailed);
            Assert.AreEqual((1, VoteKind.Pause, o), (failed.Count, failed[0].Vote, failed[0].Seat));
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved);

            // 요청 취소: 가결 전에 풀면 투표를 거둔다
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote, "부결 뒤 대기 없이 다시 연다");
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = false }, r.Now);
            Assert.IsNull(r.Host.Room.Vote, "요청을 거뒀다");

            // 모두 찬성 → 예약(o는 선택이 없다)
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            int id = r.Host.Room.Vote.Id;
            r.Host.OnMessage(r.Conn[0], new CastVote { VoteId = id, Yes = true }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote);
            r.Host.OnMessage(r.Conn[g], new CastVote { VoteId = id, Yes = true }, r.Now);
            Assert.IsNull(r.Host.Room.Vote);
            Assert.AreEqual(1, r.Notices(g, NetCode.VotePassed).FindAll(n => n.Vote == VoteKind.Pause).Count);
            CollectionAssert.AreEqual(new[] { o }, r.Host.Room.PauseReserved, "가결 = 예약");
            Assert.IsFalse(r.Host.Paused);
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = false }, r.Now);
            CollectionAssert.IsEmpty(r.Host.Room.PauseReserved);

            // 선택이 열린 g의 요청이 가결되면 바로 멈추고, 멈춘 동안 o의 요청은 투표 없이 더한다
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = true }, r.Now);
            id = r.Host.Room.Vote.Id;
            r.Host.OnMessage(r.Conn[0], new CastVote { VoteId = id, Yes = true }, r.Now);
            r.Host.OnMessage(r.Conn[o], new CastVote { VoteId = id, Yes = true }, r.Now);
            CollectionAssert.AreEqual(new[] { g }, r.Host.Clock.PausedBy);
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            Assert.IsNull(r.Host.Room.Vote, "멈춰 있으면 투표 없이");
            CollectionAssert.AreEqual(new[] { System.Math.Min(g, o), System.Math.Max(g, o) }, r.Host.Clock.PausedBy);
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = false }, r.Now);
            Assert.IsTrue(r.Host.Paused, "둘이 걸면 둘 다 풀어야 재개");
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = false }, r.Now);
            Assert.IsFalse(r.Host.Paused);

            // 30초 안에 모이지 않으면 부결 · 요청한 좌석이 끊기면 부결
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            Assert.AreEqual(r.Now + HostSession.PauseVoteMs, System.Math.Min(r.Host.NextEventMs, r.Now + HostSession.PauseVoteMs));
            r.Now += HostSession.PauseVoteMs;
            r.Host.Tick(r.Now);
            Assert.IsNull(r.Host.Room.Vote, "30초 → 부결");
            r.Host.OnMessage(r.Conn[o], new SetPaused { On = true }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote);
            int before = r.Notices(0, NetCode.VoteFailed).Count;
            r.Drop(o);
            Assert.IsNull(r.Host.Room.Vote, "요청한 좌석이 끊기면 부결");
            Assert.AreEqual(before + 1, r.Notices(0, NetCode.VoteFailed).Count);
        }

        [Test]
        public void M7_일시정지_강퇴된좌석의정지는빠져재개_기다릴손님이없으면시스템정지가풀린다()
        {
            // 모두 모드: 선택이 열린 g가 멈춘 뒤 강퇴되면 그 정지가 빠지고 비었으니 재개
            var r = new Room(ctx, Cfg(PauseBy(PauseMode.Everyone)), 3);
            int g = ToGuestMapAction(r);
            int o = IdleGuest(r, g);
            r.Host.OnMessage(r.Conn[g], new SetPaused { On = true }, r.Now);
            CollectionAssert.AreEqual(new[] { g }, r.Host.Clock.PausedBy);
            r.Host.OnMessage(r.Conn[o], new StartVote { Target = g, Kind = VoteKind.Kick }, r.Now);
            r.Host.OnMessage(r.Conn[0], new CastVote { VoteId = r.Host.Room.Vote.Id, Yes = true }, r.Now);
            Assert.IsTrue(r.Host.Room.Seats[g].Kicked);
            Assert.IsFalse(r.Host.Paused, "강퇴된 좌석의 정지는 빠진다");

            // 2인 방: 손님이 끊기면 시스템 정지 → 그 손님이 강퇴되면 기다릴 손님이 없어 풀린다
            var two = new Room(ctx, Cfg(), 2);
            two.Now = two.Host.AnimEndMs;
            two.Host.Tick(two.Now);
            two.Drop(1);
            CollectionAssert.AreEqual(new[] { HostSession.SystemPause }, two.Host.Clock.PausedBy);
            two.Host.OnMessage(two.Conn[0], new StartVote { Target = 1, Kind = VoteKind.Kick }, two.Now);
            Assert.IsTrue(two.Host.Room.Seats[1].Kicked, "투표할 좌석 = 방장뿐 → 가결");
            Assert.IsFalse(two.Host.Paused, "기다릴 손님이 없다");
        }
    }
}
