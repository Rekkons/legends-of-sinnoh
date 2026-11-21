using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「튜토리얼 입구 · 연출 실접속」(완료 조건 6, 02 §3-2 「연출 조정 값」 · §5-7 [가정 M7-73·74]): 방장의 Host 조정 값이 판 시작 때 방 상태에 실려 손님이 맞춘다 ·
    /// 갱신마다 방장 멈춤 끝 − 화면 대기열 끝을 적는 자(<see cref="AnimDrift"/>).
    /// </summary>
    public partial class RoomTests
    {
        [Test]
        public void M7_연출조정값_방장이바꾼Host값만판시작때방상태에실리고_손님은기본으로돌린뒤받은값을넣는다_Local값은그대로_전선위왕복()
        {
            var walk = PlayTiming.WalkStepMs;
            var local = FxTune.ClickGuardMs;   // Local(내 화면 값)
            try
            {
                // 손대지 않은 방: 빈 목록(전선 위에 싣는 것이 없다)
                var plain = new Room(ctx, Cfg(), 2);
                Assert.IsEmpty(plain.Net.Last<RoomUpdate>(plain.Conn[1]).Room.Tune, "기본값 그대로면 싣지 않는다");

                // 방장이 걷기 한 걸음을 바꿔 두고 판을 시작한다 → 게임 중 방 상태에 그 값만
                Assert.IsTrue(Tunables.Shared.Set(walk.Key, walk.Default + 137));
                var r = new Room(ctx, Cfg(), 3);
                var room = r.Net.Last<RoomUpdate>(r.Conn[2]).Room;
                Assert.AreEqual(RoomPhase.InGame, room.Phase);
                Assert.AreEqual(1, room.Tune.Count, "기본값과 다른 Host 값만");
                Assert.AreEqual((walk.Key, walk.Default + 137), (room.Tune[0].Key, room.Tune[0].Value));

                // 전선 위 왕복(프로토콜 판 5)
                Assert.GreaterOrEqual(Protocol.Version, 5, "RoomState.Tune을 더해 판을 올렸다");
                var wire = MessageCodec.Encode(new RoomUpdate { Room = room }, 3);
                Assert.IsTrue(MessageCodec.TryDecodeServer(wire, out var back, out var error), error);
                var got = ((RoomUpdate)back).Room.Tune;
                Assert.AreEqual((1, walk.Key, walk.Default + 137), (got.Count, got[0].Key, got[0].Value));

                // 손님: 자기 Host 값(다른 키를 바꿔 둠)은 기본으로 돌아가고 받은 값이 들어간다. Local 값 · 모르는 키 · Local 키는 건드리지 않는다
                Tunables.Shared.Reset();
                Assert.IsTrue(Tunables.Shared.Set(PlayTiming.WalkPrepMs.Key, PlayTiming.WalkPrepMs.Default + 55));
                Assert.IsTrue(Tunables.Shared.Set(local.Key, local.Max));
                got.Add(new TuneValue { Key = "no.such.key", Value = 1 });
                got.Add(new TuneValue { Key = local.Key, Value = local.Min });
                Assert.AreEqual(2, Tunables.Shared.MatchHost(got), "모르는 키 · Local 키는 넣지 않는다");
                Assert.AreEqual(walk.Default + 137, walk.Value, "방장 값");
                Assert.AreEqual(PlayTiming.WalkPrepMs.Default, PlayTiming.WalkPrepMs.Value, "내가 바꿔 둔 Host 값은 기본으로");
                Assert.AreEqual(local.Max, local.Value, "Local 값은 내 화면 것 그대로");

                // 손대지 않은 방에 들어가면 모두 기본으로
                Assert.AreEqual(0, Tunables.Shared.MatchHost(new List<TuneValue>()));
                Assert.AreEqual(walk.Default, walk.Value);
                Assert.IsEmpty(Tunables.Shared.HostChanges());
            }
            finally
            {
                Tunables.Shared.Reset();
            }
        }

        [Test]
        public void M7_연출차이_갱신마다방장멈춤끝빼기화면대기열끝을적고_양쪽최댓값과로그줄을준다()
        {
            var d = new AnimDrift();
            Assert.AreEqual((0, 0L, 0L, 0L), (d.Count, d.MaxMs, d.MinMs, d.WorstMs));
            Assert.AreEqual(20, d.Note(1200, 1180), "방장이 20ms 더 길다");
            Assert.AreEqual(-340, d.Note(800, 1140), "내 화면이 340ms 더 길다");
            Assert.AreEqual(0, d.Note(0, 0));
            Assert.AreEqual(35, d.Note(35, -5), "음수 길이는 0으로 본다");
            Assert.AreEqual((4, 35L, -340L, -340L, 35L), (d.Count, d.MaxMs, d.MinMs, d.WorstMs, d.LastMs));
            Assert.AreEqual("[Sync] seat=1 v=42 events=3 host=35 mine=0 diff=+35 max=+35 min=-340", d.Line(1, 42, 3, 35, -5));
            var up = new AnimDrift();
            up.Note(500, 100);
            up.Note(100, 150);
            Assert.AreEqual(400, up.WorstMs, "절댓값이 큰 쪽(부호 그대로)");
        }
    }
}
