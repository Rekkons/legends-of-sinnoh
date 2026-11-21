using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「세션 ③-2」 · 02 §3-1·§4-2·§7 · 04 §5 ①: 결과 뒤 [로비로](같은 방 · 같은 자리 · 강퇴 토큰 금지 그대로 · 새 시드 · 새 SaveId — 판마다 따로 저장)와
    /// SetAutoPass(「이번 배틀 묻지 않기」 — 그 배틀의 숨은 손패 창을 최소 시간에 「안 씀」, 배틀이 바뀌면 다시 묻는다). 봇 방은 <see cref="BotRoom"/>.
    /// </summary>
    public partial class StreamTests
    {
        const int MaxBotCommands = 40000;

        [Test]
        public void M7_로비로_같은방같은자리_강퇴토큰금지그대로_새시드새SaveId()
        {
            string root = Path.Combine(Path.GetTempPath(), "sinnoh-lobby-" + Guid.NewGuid().ToString("N"));
            var made = new List<string>();
            var writers = new List<SaveWriter>();
            try
            {
                var r = new BotRoom(ctx, 3, 5, ClockPreset.Relaxed, tweak: c => c.Store = id =>
                {
                    made.Add(id);
                    var w = new SaveWriter(Path.Combine(root, id));
                    writers.Add(w);
                    return w;
                });
                for (int i = 0; i < 40; i++) r.Step();
                // 2번 좌석을 강퇴 투표로 내보낸다(방장이 열고 1번이 찬성)
                r.Host.OnMessage(r.Conn[0], new StartVote { Target = 2, Kind = VoteKind.Kick }, r.Now);
                r.Host.OnMessage(r.Conn[1], new CastVote { VoteId = r.Host.Room.Vote.Id, Yes = true }, r.Now);
                Assert.IsTrue(r.Host.Room.Seats[2].Kicked);
                r.Conn[2] = -1;
                while (r.Host.Phase == RoomPhase.InGame && r.Host.Game.CommandCount < MaxBotCommands) r.Step();
                Assert.AreEqual(RoomPhase.Result, r.Host.Phase, "게임이 끝났다");
                int ended = r.Host.Game.CommandCount;
                Report($"[M7 로비로] 첫 판 명령 {ended}");

                r.Host.OnMessage(r.Conn[1], new ReturnToLobby(), r.Now);
                Assert.AreEqual(RoomPhase.Result, r.Host.Phase, "손님은 [로비로]를 누르지 못한다");
                r.Host.OnMessage(r.Conn[0], new ReturnToLobby(), r.Now);
                Assert.AreEqual(RoomPhase.Lobby, r.Host.Phase);
                Assert.IsNull(r.Host.Game, "판을 내려놓았다");
                var room = r.Host.Room;
                Assert.AreEqual("s0", room.Seats[0].Nickname);
                Assert.IsTrue(room.Seats[0].Host);
                Assert.AreEqual("s1", room.Seats[1].Nickname);
                Assert.IsFalse(room.Seats[1].Ready, "준비는 다시");
                Assert.IsNull(room.Seats[2], "강퇴된(끊긴) 좌석은 빈칸");
                Assert.AreEqual(1, r.Host.SeatOf(r.Conn[1]), "같은 자리");
                Assert.AreEqual(ended, SaveStore.ReadLog(Path.Combine(root, "room-stream")).Count, "끝난 판의 저장을 다 쓰고 닫았다");

                Assert.AreEqual(NetCode.Kicked, r.Host.Approve(20, Hi("s2"), r.Now).Code, "강퇴 토큰은 [로비로] 뒤에도 금지");
                var ok = r.Host.Approve(21, Hi("s9"), r.Now);
                Assert.AreEqual(2, ok.Seat, "새 손님은 빈 자리에");
                r.Conn[2] = 21;
                r.Host.OnMessage(r.Conn[0], new StartGame(), r.Now);
                Assert.AreEqual(RoomPhase.Lobby, r.Host.Phase, "모두 준비해야 시작");
                r.Host.OnMessage(r.Conn[1], new SetReady { Ready = true }, r.Now);
                r.Host.OnMessage(r.Conn[2], new SetReady { Ready = true }, r.Now);
                r.NewGame();
                r.Host.OnMessage(r.Conn[0], new StartGame(), r.Now);
                Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
                Assert.AreEqual("room-stream-2", r.Host.SaveId);
                CollectionAssert.AreEqual(new[] { "room-stream", "room-stream-2" }, made, "판마다 저장을 따로 만든다");
                for (int i = 0; i < 30; i++) r.Step();
                r.Check("새 판");
                Assert.IsTrue(writers[1].Flush(10000));
                var h1 = SaveStore.Load(Path.Combine(root, "room-stream")).Head;
                var h2 = SaveStore.Load(Path.Combine(root, "room-stream-2")).Head;
                Assert.AreEqual("room-stream-2", h2.SaveId);
                Assert.AreNotEqual(h1.Seed, h2.Seed, "새 시드");
                Assert.AreNotEqual(h1.Ai.Seed, h2.Ai.Seed, "새 AI 시드");
                CollectionAssert.AreEqual(new[] { "s0", "s1", "s9" }, h2.Room.Seats.ConvertAll(s => s.Token));
                CollectionAssert.Contains(h2.Room.Banned, "s2");
                r.Host.Close(r.Now);
                Assert.AreEqual(0, writers[1].Pending, "방을 닫으며 새 판의 저장도 비웠다");
            }
            finally
            {
                foreach (var w in writers) w.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void M7_자동넘김_이번배틀묻지않기_그배틀의운명창은최소시간에안씀_배틀이바뀌면다시묻는다()
        {
            var r = new BotRoom(ctx, 4, 2, ClockPreset.Relaxed);
            // 봇은 숨은 손패 운명 창에 답하지 않는다 — 쓸 카드가 있으면 시간 초과(AUTO_RESOLVED), 없으면 세션이 최소 시간에 「안 씀」
            Func<int, PendingChoice, bool> fate = (s, c) => c.HandGated && c.Sub == ChoiceSub.Fate;
            int seat = -1;
            PendingChoice first = null;
            while (first == null)
            {
                Assert.Less(r.Host.Game.CommandCount, MaxBotCommands, "쓸 카드가 있는 운명 창이 열리지 않았다");
                Assert.AreEqual(RoomPhase.InGame, r.Host.Phase, "운명 창 전에 게임이 끝났다");
                for (int s = 0; s < r.Conn.Length && first == null; s++)
                {
                    var v = r.View(s);
                    var c = v?.Pending.Find(x => x.Seat == s && !x.Answered && fate(s, x));
                    if (c == null) continue;
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(v, ctx, c, legal);
                    if (legal.Count < 2) continue;
                    seat = s;
                    first = c;
                }
                if (first == null) r.Step(leave: fate);
            }
            int battle = r.Host.Game.HostCopy().Battle.Id;
            var notices = r.Wire.RawOf(r.Conn[0]);
            int mark = notices.Count;
            r.Host.OnMessage(r.Conn[seat], new SetAutoPass { Sub = ChoiceSub.Fate }, r.Now);
            int windows = 0;
            var seen = new HashSet<int> { first.Id };
            while (r.Host.Game.HostCopy().Battle?.Id == battle)
            {
                Assert.Less(r.Host.Game.CommandCount, MaxBotCommands);
                var c = r.View(seat)?.Pending.Find(x => x.Seat == seat && !x.Answered && fate(seat, x));
                if (c != null && seen.Add(c.Id)) windows++;
                r.Step(leave: fate);
            }
            Assert.IsFalse(r.Host.Game.HostCopy().Pending.Exists(x => x.Id == first.Id && !x.Answered), "SetAutoPass 때 열려 있던 창도 넘겼다");
            Assert.AreEqual(0, AutoResolved(notices, mark, seat), "이 배틀에서는 그 좌석의 운명 창이 시간 초과로 가지 않았다(최소 시간에 「안 씀」)");
            Report($"[M7 자동넘김] 좌석 {seat} · 배틀 {battle} · 켠 뒤 새로 열린 운명 창 {windows}");

            // 배틀이 바뀌면 풀린다: 다음에 쓸 카드가 있는 그 좌석의 운명 창은 다시 묻는다(봇이 답하지 않아 시간 초과까지 기다린다)
            mark = notices.Count;
            while (AutoResolved(notices, mark, seat) == 0)
            {
                Assert.Less(r.Host.Game.CommandCount, MaxBotCommands, "다음 배틀의 운명 창이 열리지 않았다");
                Assert.AreEqual(RoomPhase.InGame, r.Host.Phase, "다음 운명 창 전에 게임이 끝났다");
                r.Step(leave: fate);
            }
        }

        /// <summary>mark 뒤에 그 좌석의 반응 창이 시간 초과로 처리됐다는 알림 수.</summary>
        static int AutoResolved(List<ServerMsg> got, int mark, int seat)
        {
            int n = 0;
            for (int i = mark; i < got.Count; i++)
                if (got[i] is NoticeMsg m && m.Code == NetCode.AutoResolved && m.Seat == seat && m.Kind == ChoiceKind.Reaction) n++;
            return n;
        }
    }
}
