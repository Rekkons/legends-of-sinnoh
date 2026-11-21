using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 결정 140 ② 방 채팅: 방 설정 <see cref="RoomSettings.Chat"/>(기본 끔 · 게임 중에도 바꾼다) · 방장 중계(<see cref="SendChat"/> → <see cref="ChatMsg"/> 보낸 좌석까지 전원) ·
    /// 끈 방 · 빈 글 · 자리 없는 연결 · 연타 제한은 버림 · 글 다듬기(제어 글자 · 200자) · 로비 자리 번호 / 게임 좌석 번호 · 저장 파일 · 플레이 기록 · 게임 상태에 들어가지 않는다.
    /// </summary>
    public partial class RoomTests
    {
        static int Chats(Room r, int conn) => r.Net.All<ChatMsg>(conn).Count;

        static RoomSettings ChatOn(int seats = 3) => new RoomSettings { Seats = seats, Clock = ClockPreset.Off, Chat = true };

        [Test]
        public void M10_결정140_채팅_기본꺼짐_게임중에도방장이켜고끔_끈방에서는버린다()
        {
            Assert.IsFalse(new RoomSettings().Chat, "기본 끔");
            var wanted = new RoomSettings { Chat = true, Seats = 2, TrainerCards = false };
            var live = new RoomSettings().InGameChange(wanted);
            Assert.IsTrue(live.Chat, "게임 중 바꾸기에 채팅이 들어 있다");
            Assert.AreEqual((SeatId.MaxSeats, true), (live.Seats, live.TrainerCards), "게임 전에만 바꾸는 칸은 그대로");

            // 기본 방(끔): 로비에서 보낸 글은 아무에게도 가지 않는다
            var r = new Room(ctx, net => new HostSession(ctx, net, Cfg(new RoomSettings { Seats = 3, Clock = ClockPreset.Off }), 0));
            Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
            r.Host.OnMessage(1, new SendChat { Text = "안녕하세요" }, 0);
            Assert.AreEqual((0, 0), (Chats(r, 0), Chats(r, 1)), "끈 방");

            // 방장이 켠다 → 로비에서 오간다(보낸 사람에게도)
            var on = r.Host.Room.Settings.Clone();
            on.Chat = true;
            r.Host.OnMessage(0, new ChangeSettings { Settings = on }, 0);
            Assert.IsTrue(r.Host.Room.Settings.Chat);
            r.Host.OnMessage(1, new SendChat { Text = "안녕하세요" }, 0);
            var got = r.Net.Last<ChatMsg>(0);
            Assert.AreEqual((1, "안녕하세요"), (got.From, got.Text));
            Assert.AreEqual(1, Chats(r, 1), "보낸 좌석도 받는다(화면은 받은 것만 그린다)");

            // 게임을 시작하고 방장이 게임 메뉴에서 끈다 → 버림 · 다시 켜면 오간다
            r.Host.OnMessage(1, new SetReady { Ready = true }, 0);
            r.Host.OnMessage(0, new StartGame(), 0);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
            var off = r.Host.Room.Settings.Clone();
            off.Chat = false;
            r.Host.OnMessage(0, new ChangeSettings { Settings = off }, 5000);
            Assert.IsFalse(r.Host.Room.Settings.Chat, "게임 중에도 끈다");
            int before = Chats(r, 1);
            r.Host.OnMessage(0, new SendChat { Text = "꺼졌나" }, 5000);
            Assert.AreEqual(before, Chats(r, 1));
            r.Host.OnMessage(0, new ChangeSettings { Settings = on.Clone() }, 5000);
            r.Host.OnMessage(0, new SendChat { Text = "켜졌다" }, 5000);
            Assert.AreEqual((0, "켜졌다"), (r.Net.Last<ChatMsg>(1).From, r.Net.Last<ChatMsg>(1).Text));
        }

        [Test]
        public void M10_결정140_채팅_로비는자리번호_게임은좌석번호_글다듬기_빈글과자리없는연결과연타는버림_전선왕복()
        {
            // 로비 6칸: owner 1번 · g1 2번 · g2 3번 → g1이 나가면 g2는 3번 자리 그대로(빈 자리가 섞인 번호)
            var r = new Room(ctx, net => new HostSession(ctx, net, Cfg(ChatOn()), 0));
            Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(2, Hi("g2"), 0).Ok);
            r.Host.OnDisconnected(1, 0);
            Assert.IsNull(r.Host.Room.Seats[1], "빈 자리");
            r.Host.OnMessage(2, new SendChat { Text = "로비에서" }, 0);
            Assert.AreEqual(2, r.Net.Last<ChatMsg>(0).From, "로비 = 자리 번호");

            // 다듬기: 줄바꿈 · 탭 · 제어 글자는 빈칸, 앞뒤 빈칸 빼기, 200자에서 자르기(서로게이트 쌍은 가르지 않는다)
            r.Host.OnMessage(2, new SendChat { Text = "  두 줄\n글\t\u0007끝  " }, 2000);
            Assert.AreEqual("두 줄 글  끝", r.Net.Last<ChatMsg>(0).Text);
            string longText = new string('가', ChatRules.MaxLength - 1) + "😀" + "넘침";
            r.Host.OnMessage(2, new SendChat { Text = longText }, 4000);
            Assert.AreEqual(new string('가', ChatRules.MaxLength - 1), r.Net.Last<ChatMsg>(0).Text, "200자 — 반쪽 그림 글자는 뺀다");
            Assert.AreEqual(ChatRules.MaxLength, ChatRules.Clean(new string('a', 500)).Length);
            Assert.IsNull(ChatRules.Clean(" \n\t "));
            Assert.IsNull(ChatRules.Clean(null));

            // 빈 글 · 빈칸만 · null은 버림
            int before = Chats(r, 0);
            r.Host.OnMessage(2, new SendChat { Text = "   " }, 6000);
            r.Host.OnMessage(2, new SendChat { Text = null }, 7000);
            r.Host.OnMessage(2, new SendChat { Text = "\n" }, 8000);
            Assert.AreEqual(before, Chats(r, 0), "빈 글");

            // 연타: 1초에 1번 + 10초에 5번(방장 여유 0.5초) — 넘으면 조용히 버리고 좌석마다 따로 센다
            long t = 100000;
            r.Host.OnMessage(2, new SendChat { Text = "1" }, t);
            r.Host.OnMessage(2, new SendChat { Text = "2" }, t + 100);
            Assert.AreEqual("1", r.Net.Last<ChatMsg>(0).Text, "1초 안의 둘째 줄");
            r.Host.OnMessage(0, new SendChat { Text = "방장" }, t + 100);
            Assert.AreEqual((0, "방장"), (r.Net.Last<ChatMsg>(2).From, r.Net.Last<ChatMsg>(2).Text), "다른 좌석은 따로");
            for (int i = 1; i < 6; i++) r.Host.OnMessage(2, new SendChat { Text = "n" + i }, t + i * 1000);
            Assert.AreEqual("n4", r.Net.All<ChatMsg>(0).Last(m => m.From == 2).Text, "10초에 다섯 줄까지");
            r.Host.OnMessage(2, new SendChat { Text = "다시" }, t + ChatRules.WindowMs + 100);
            Assert.AreEqual("다시", r.Net.Last<ChatMsg>(0).Text, "창이 지나면 다시");
            var rate = new ChatRate();
            Assert.IsTrue(rate.TryTake(0, 0));
            Assert.AreEqual(ChatRules.GapMs, rate.WaitMs(0, 0), "화면 쪽은 여유 없이 같은 표");

            // 자리 없는 연결(승인 전 · 나간 연결)은 버린다
            before = Chats(r, 0);
            r.Host.OnMessage(1, new SendChat { Text = "나간 사람" }, t + 30000);
            r.Host.OnMessage(77, new SendChat { Text = "모르는 연결" }, t + 30000);
            Assert.AreEqual(before, Chats(r, 0));

            // 게임 시작: 빈 자리를 빼고 다시 매긴 좌석 번호(g2 = 2번 좌석 → 번호 1)
            r.Host.OnMessage(2, new SetReady { Ready = true }, t + 30000);
            r.Host.OnMessage(0, new StartGame(), t + 30000);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
            r.Host.OnMessage(2, new SendChat { Text = "게임에서" }, t + 40000);
            var game = r.Net.Last<ChatMsg>(0);
            Assert.AreEqual((1, "게임에서"), (game.From, game.Text), "게임 = 좌석 번호");

            // 전선: 하위 형식은 저절로 등록 · 글 그대로 왕복
            string wire = MessageCodec.Encode(new SendChat { Text = "한글 <b>태그</b> \"따옴표\"" }, 4);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(wire, out var back, out _, out string error), error);
            Assert.AreEqual("한글 <b>태그</b> \"따옴표\"", ((SendChat)back).Text);
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(game), out var server, out error), error);
            Assert.AreEqual((1, "게임에서"), (((ChatMsg)server).From, ((ChatMsg)server).Text));
            Assert.Less(System.Text.Encoding.UTF8.GetByteCount(MessageCodec.Encode(new ChatMsg { From = 5, Text = new string('가', ChatRules.MaxLength) })), 700, "한 줄 한 통 700바이트 밑");
        }

        [Test]
        public void M10_결정140_채팅_저장파일과플레이기록과게임상태에들어가지않는다()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sinnoh-chat-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cfg = SaveCfg(new SaveStore(dir));
                cfg.Settings.Chat = true;
                cfg.Settings.Seats = 3;
                var r = new Room(ctx, net => new HostSession(ctx, net, cfg, 0));
                Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
                r.Host.OnMessage(1, new SendChat { Text = "lobby-secret-7Q 로비비밀" }, 0);
                r.Host.OnMessage(1, new SetReady { Ready = true }, 0);
                r.Host.OnMessage(0, new StartGame(), 0);
                Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
                for (int i = 0; i < 6; i++) r.Step();
                int commands = r.Host.Game.CommandCount;
                ulong hash = r.Host.Game.Hash;
                r.Now += 5000;
                r.Host.OnMessage(r.Conn[0], new SendChat { Text = "game-secret-7Q 게임비밀" }, r.Now);
                r.Host.OnMessage(r.Conn[1], new SendChat { Text = "guest-secret-7Q 손님비밀" }, r.Now);
                Assert.AreEqual(3, r.Net.All<ChatMsg>(r.Conn[0]).Count, "받기는 했다");
                Assert.AreEqual((commands, hash), (r.Host.Game.CommandCount, r.Host.Game.Hash), "게임 상태 · 명령 수 그대로(방 층 메시지)");
                for (int i = 0; i < 6; i++) r.Step();
                r.Host.Close(r.Now);

                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                Assert.Greater(files.Length, 0, "저장이 있다");
                foreach (var f in files)
                    StringAssert.DoesNotContain("secret-7Q", File.ReadAllText(f), "저장 파일 " + Path.GetFileName(f));
                var entry = SaveStore.Peek(dir);
                Assert.IsTrue(entry.Readable, entry.Problem.ToString());
                Assert.IsTrue(entry.Head.Room.Settings.Chat, "방 설정 값(켬)은 저장에 남는다 — 이어 열면 같은 방");
                string record = PlayRecord.Text(entry.Head, SaveStore.ReadLog(dir), "test");
                StringAssert.DoesNotContain("secret-7Q", record, "플레이 기록(버그 신고의 판 기록도 같은 꼴)");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
