using System.IO;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 다듬기 P06(결정 112 Q4): 방 로비에서 좌석마다 주인공 고르기 — 방 층 메시지 <see cref="PickProtagonist"/> · 방 상태 <see cref="RoomSeat.Protagonist"/> · 접속 인사의 지난 값 ·
    /// 방 저장 머리(<see cref="SaveHead.Protagonists"/>)와 이어 열기. 세이브는 임시 폴더(RULE-SAVE).
    /// </summary>
    public partial class RoomTests
    {
        static string ProtoOf(RoomState room, int seat) => room.Seats[seat]?.Protagonist;

        [Test]
        public void M9_좌석주인공_로비에서고르고모두에게알린다_겹쳐도되고_틀린키와게임중은버린다()
        {
            var r = new Room(ctx, net => new HostSession(ctx, net, Cfg(new RoomSettings { Seats = 4 }), 0));
            Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
            var picked = Hi("g2");
            picked.Protagonist = "b2w2_001_rosa";
            Assert.IsTrue(r.Host.Approve(2, picked, 0).Ok);
            var odd = Hi("g3");
            odd.Protagonist = Protagonists.Rival;   // 고를 수 없는 손님 인물 — 자리 번호로
            Assert.IsTrue(r.Host.Approve(3, odd, 0).Ok);

            var room = r.Net.Last<RoomUpdate>(1).Room;
            Assert.AreEqual(Protagonists.Keys[0], ProtoOf(room, 0), "고르지 않은 좌석은 자리 번호로(서로 다르게)");
            Assert.AreEqual(Protagonists.Keys[1], ProtoOf(room, 1));
            Assert.AreEqual("b2w2_001_rosa", ProtoOf(room, 2), "접속 인사에 실린 지난 값");
            Assert.AreEqual(Protagonists.Keys[3], ProtoOf(room, 3), "모르는 키는 자리 번호로");

            // 손님이 고른다 — 방장과 같은 인물이어도 된다 [가정 M7-94]. 모든 연결이 같은 방 상태를 받는다
            r.Host.OnMessage(1, new SetReady { Ready = true }, 0);
            int before = r.Net.All<RoomUpdate>(0).Count;
            r.Host.OnMessage(1, new PickProtagonist { Key = Protagonists.Keys[0] }, 0);
            Assert.AreEqual(before + 1, r.Net.All<RoomUpdate>(0).Count, "방장이 모두에게 알린다");
            for (int c = 0; c < 4; c++)
            {
                room = r.Net.Last<RoomUpdate>(c).Room;
                Assert.AreEqual(Protagonists.Keys[0], ProtoOf(room, 1), "연결 " + c);
                Assert.AreEqual(Protagonists.Keys[0], ProtoOf(room, 0), "방장의 값은 그대로");
            }
            Assert.IsTrue(room.Seats[1].Ready, "주인공을 바꿔도 준비는 풀리지 않는다");

            // 틀린 키 · 같은 값 · 고를 수 없는 인물은 버린다(알림 없음)
            before = r.Net.All<RoomUpdate>(0).Count;
            r.Host.OnMessage(1, new PickProtagonist { Key = "nobody" }, 0);
            r.Host.OnMessage(1, new PickProtagonist { Key = null }, 0);
            r.Host.OnMessage(1, new PickProtagonist { Key = Protagonists.Rival }, 0);
            r.Host.OnMessage(1, new PickProtagonist { Key = Protagonists.Keys[0] }, 0);
            Assert.AreEqual(before, r.Net.All<RoomUpdate>(0).Count);

            r.Host.OnMessage(0, new PickProtagonist { Key = "hgss_001_lyra" }, 0);
            r.Host.OnMessage(2, new SetReady { Ready = true }, 0);
            r.Host.OnMessage(3, new SetReady { Ready = true }, 0);
            r.Host.OnMessage(0, new StartGame(), 0);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
            room = r.Net.Last<RoomUpdate>(3).Room;
            CollectionAssert.AreEqual(new[] { "hgss_001_lyra", Protagonists.Keys[0], "b2w2_001_rosa", Protagonists.Keys[3] }, room.Seats.ConvertAll(s => s.Protagonist), "게임 중 방 상태도 좌석 순서로 싣는다");

            // 게임 중에는 바꾸지 못한다
            before = r.Net.All<RoomUpdate>(0).Count;
            r.Host.OnMessage(1, new PickProtagonist { Key = "bw_001_hilda" }, 0);
            Assert.AreEqual(before, r.Net.All<RoomUpdate>(0).Count);
            Assert.AreEqual(Protagonists.Keys[0], ProtoOf(r.Host.Room, 1));

            // 재접속: 인사에 다른 값을 실어 와도 좌석의 값 그대로
            r.Host.OnDisconnected(2, 0);
            var back = Hi("g2");
            back.Protagonist = "bw_000_hilbert";
            Assert.IsTrue(r.Host.Approve(22, back, 0).Ok);
            Assert.AreEqual("b2w2_001_rosa", ProtoOf(r.Host.Room, 2));
        }

        [Test]
        public void M9_좌석주인공_전선왕복과판번호()
        {
            Assert.AreEqual(14, Protocol.Version, "P06이 8로, P09가 9로, 결정 126 ④(RoomState.Records)가 10으로, 결정 120(남이 가리키는 마을 · 기술)이 11로, 결정 140 ①(RoomSettings.Custom — 시간 세부 조절)이 12로, 결정 140 ②(방 채팅)가 13으로, 결정 149(무르기)가 14로 올린 판(다음 판을 올리면 이 숫자도)");
            string json = MessageCodec.Encode(new PickProtagonist { Key = "pt_player_female" }, 3);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(json, out var msg, out int seq, out string error), error);
            Assert.AreEqual(("pt_player_female", 3), (((PickProtagonist)msg).Key, seq));
            var hello = Hi("t");
            hello.Protagonist = "bw_001_hilda";
            Assert.IsTrue(MessageCodec.TryDecodeHello(MessageCodec.EncodeHello(hello), out var copy, out error), error);
            Assert.AreEqual("bw_001_hilda", copy.Protagonist);
            var update = new RoomUpdate { Room = new RoomState { Settings = new RoomSettings() } };
            update.Room.Seats.Add(new RoomSeat { Nickname = "a", Protagonist = "hgss_000_ethan" });
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(update), out var server, out error), error);
            Assert.AreEqual("hgss_000_ethan", ((RoomUpdate)server).Room.Seats[0].Protagonist);
        }

        [Test]
        public void M9_플레이기록알림_방장설정이방상태로모두에게가고_전선을왕복한다_결정126()
        {
            foreach (bool on in new[] { false, true })
            {
                var cfg = Cfg(new RoomSettings { Seats = 3 });
                cfg.Records = on;
                var r = new Room(ctx, net => new HostSession(ctx, net, cfg, 0));
                Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
                Assert.AreEqual(on, r.Host.Room.Records, "방장 화면의 방 상태");
                Assert.AreEqual(on, r.Net.Last<RoomUpdate>(1).Room.Records, "손님이 받은 방 상태");
                string json = MessageCodec.Encode(r.Net.Last<RoomUpdate>(1));
                Assert.IsTrue(MessageCodec.TryDecodeServer(json, out var server, out string error), error);
                Assert.AreEqual(on, ((RoomUpdate)server).Room.Records, "전선 왕복");
            }
        }

        [Test]
        public void M9_좌석주인공_방저장머리에남고_이어열면그대로()
        {
            string root = TempRoot();
            try
            {
                string dir = Path.Combine(root, "room-m7");
                var r = SaveRoom(new SaveStore(dir));
                r.Host.OnMessage(0, new PickProtagonist { Key = "bw_001_hilda" }, 0);
                r.Host.OnMessage(2, new PickProtagonist { Key = "bw_001_hilda" }, 0);   // g2(4번째 자리) — 방장과 겹친다
                r.Host.OnMessage(1, new SetReady { Ready = true }, 0);
                r.Host.OnMessage(2, new SetReady { Ready = true }, 0);
                r.Host.OnMessage(0, new StartGame(), 0);
                Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
                var want = new[] { "bw_001_hilda", Protagonists.Keys[1], "bw_001_hilda" };
                r.Until(() => r.Host.Game.CommandCount >= 12, SavePick);
                r.Host.OnMessage(0, new SetPaused { On = true }, r.Now);   // 정지점 — 머리를 바로 쓴다
                var head = SaveStore.Load(dir).Head;
                CollectionAssert.AreEqual(want, head.Protagonists, "방 저장 머리의 좌석별 주인공(세이브 꼴은 그대로)");

                var again = ResumeRoom(dir, out _);
                Assert.AreEqual(RoomPhase.Waiting, again.Host.Phase);
                CollectionAssert.AreEqual(want, again.Host.Room.Seats.ConvertAll(s => s.Protagonist), "이어하기 대기실");
                JoinAll(again);
                CollectionAssert.AreEqual(want, again.Net.Last<RoomUpdate>(1).Room.Seats.ConvertAll(s => s.Protagonist), "이어 연 판");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
